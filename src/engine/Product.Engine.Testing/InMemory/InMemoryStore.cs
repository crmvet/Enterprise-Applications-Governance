using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Snapshots;
using Product.Engine.Runtime.Ports;

namespace Product.Engine.Testing.InMemory;

// An in-memory store adapter for tests and the local harness. It implements both storage ports over
// one set of rows, so the version probe reads the C01 row activation writes, and the cache reads the C02 row it creates.
//
// It lives in the testing assembly, not in Product.Engine.Runtime: a host that references only the
// runtime cannot bind a store whose rows live in one process's memory and vanish on restart. Only test projects and the
// engine command line reference this assembly (RuntimeDependencyRuleTests).
//
// It is an adapter, so it holds no rule. What it does:
// - Rows by entity and ID, in logical names. Configuration rows also carry their key path.
// - IDs it makes itself: the entity key and a running number ("decision-000012"), so a run is repeatable. IDs of a
//   rolled-back transaction are skipped, as a database identity does.
// - One transaction at a time per caller: writes go to an overlay that reads see, and Commit applies them at once.
// - Two unique keys, as document 25, section 8 asks of a store: Audit Entry Request Key and Deployment Record
//   Idempotency Key. A commit that would repeat one throws StoreConflictException.
// - Seeding for tests: rows that exist before the engine runs (the estate and the directory), and a version as an
//   earlier activation left it. Seeding is not an engine write and is never a product path.
public sealed class InMemoryStore : IRecordStore, IConfigurationStore, IKeySetReader
{
    private static readonly (string Entity, string Column)[] UniqueKeys =
    {
        ("audit-entry", "requestKey"),
        ("deployment-record", "idempotencyKey"),
    };

    private readonly object _gate = new();
    private readonly Dictionary<string, SortedDictionary<string, StoredRow>> _rows = new(StringComparer.Ordinal);

    // The last record number given per entity (RecordNumbers), kept across transactions as a database counter is.
    private readonly Dictionary<string, long> _recordNumbers = new(StringComparer.Ordinal);
    private long _nextId;

    public InMemoryStore(string storeId = "in-memory")
    {
        if (string.IsNullOrEmpty(storeId))
        {
            throw new ArgumentException("A store has an ID.", nameof(storeId));
        }

        StoreId = storeId;
    }

    public string StoreId { get; }

    // How many version probes and C02 reads the store answered (for the cache tests).
    public int ProbeCount { get; private set; }

    public int VersionReadCount { get; private set; }

    public int CommitCount { get; private set; }

    // How many reads the engine's transactions made (by ID, by key, by query, and by key set; document 25, B25-23): what
    // a read would cost in round trips on a database. Seeding and the test's own reads of committed rows do not count.
    public int TransactionReads => _transactionReads;

    private int _transactionReads;

    // ---- IRecordReader over committed rows ----

    public StoredRow? Get(string entity, string id)
    {
        lock (_gate)
        {
            return _rows.TryGetValue(entity, out SortedDictionary<string, StoredRow>? rows) && rows.TryGetValue(id, out StoredRow? row) ? row : null;
        }
    }

    public StoredRow? GetByKey(string entity, string key)
    {
        lock (_gate)
        {
            return Committed(entity).FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.Ordinal));
        }
    }

    public IReadOnlyList<StoredRow> Query(RowQuery query)
    {
        lock (_gate)
        {
            return Committed(query.Entity).Where(query.Matches).ToList();
        }
    }

    // ---- IKeySetReader over committed rows ----

    public IReadOnlyList<StoredRow> GetMany(string entity, IReadOnlyCollection<string> ids)
    {
        lock (_gate)
        {
            if (!_rows.TryGetValue(entity, out SortedDictionary<string, StoredRow>? rows))
            {
                return Array.Empty<StoredRow>();
            }

            return KeySetReads.Distinct(ids).Select(id => rows.TryGetValue(id, out StoredRow? row) ? row : null).Where(r => r != null).Select(r => r!).OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
        }
    }

    public IReadOnlyList<StoredRow> QueryIn(string entity, string column, IReadOnlyCollection<string> values)
    {
        var set = new HashSet<string>(values ?? Array.Empty<string>(), StringComparer.Ordinal);
        lock (_gate)
        {
            return Committed(entity).Where(r => KeySetReads.HoldsOneOf(r, column, set)).ToList();
        }
    }

    // Every committed row of an entity, by ID.
    public IReadOnlyList<StoredRow> Rows(string entity)
    {
        lock (_gate)
        {
            return Committed(entity).ToList();
        }
    }

    // Every committed row, by entity then ID, as canonical text. Two runs of the same calls give the same text.
    public string Dump()
    {
        return DumpWithout();
    }

    // The dump without the rows of the named entities, such as audit-entry: a refused call writes nothing but the product
    // event that records it (document 25, B25-13), so a test that proves "nothing was written" leaves that event out and
    // checks it on its own.
    public string DumpWithout(params string[] entities)
    {
        lock (_gate)
        {
            var lines = new List<string>();
            foreach (KeyValuePair<string, SortedDictionary<string, StoredRow>> entity in _rows.Where(e => !entities.Contains(e.Key, StringComparer.Ordinal)).OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                foreach (StoredRow row in entity.Value.Values)
                {
                    lines.Add(entity.Key + " " + row.Id + (row.Key == null ? string.Empty : " [" + row.Key + "]") + " " + CanonicalJson.Write(row.Columns, CanonicalProfile.Snapshot));
                }
            }

            return string.Join("\n", lines);
        }
    }

    public int Count(string entity)
    {
        lock (_gate)
        {
            return _rows.TryGetValue(entity, out SortedDictionary<string, StoredRow>? rows) ? rows.Count : 0;
        }
    }

    public IStoreTransaction Begin()
    {
        return new Transaction(this);
    }

    // ---- IConfigurationStore ----

    // The working set marked in force and its current version number (C01).
    public InForceVersion? ProbeInForceVersion()
    {
        lock (_gate)
        {
            ProbeCount++;
            StoredRow? set = Committed("configuration-set").FirstOrDefault(r => r.Flag("isInForce") && r.Key != null);
            long? number = set?.Number("currentVersionNumber");
            return set == null || !number.HasValue || number.Value < 1 ? null : new InForceVersion(set.Key!, (int)number.Value);
        }
    }

    public StoredVersion? GetVersion(string configurationSetKey, int versionNumber)
    {
        lock (_gate)
        {
            VersionReadCount++;
            StoredRow? version = Committed("configuration-set-version").FirstOrDefault(r =>
                string.Equals(r.Text("configurationSet"), configurationSetKey, StringComparison.Ordinal) && r.Number("versionNumber") == versionNumber);
            JsonTreeNode? content = version?.Column("contentJson");
            return version == null || content == null ? null : new StoredVersion(configurationSetKey, versionNumber, content, version.Text("contentHash") ?? string.Empty);
        }
    }

    // ---- Seeding for tests and the local harness (not an engine write) ----

    // A row that exists before the engine runs, such as a registered application, a deployment target or a directory
    // object's Principal Reference. Returns its ID.
    public string Seed(string entity, JsonTreeNode columns, string? id = null, string? key = null)
    {
        lock (_gate)
        {
            id ??= NewIdLocked(entity);
            Table(entity)[id] = new StoredRow(entity, id, key, columns);
            return id;
        }
    }

    public string Seed(string entity, string columnsJson, string? id = null, string? key = null)
    {
        return Seed(entity, CanonicalJson.Parse(columnsJson), id, key);
    }

    // A version as an earlier activation left it in force (document 25, section 6.1): the working set (C01) in force at
    // this version, the C02 row with Content Json and Content Hash, and the live rows equal to the snapshot. Replaces the
    // live rows of an earlier seeded version. publishedOnUtc, the seed moment, is the version row's Published On when
    // given (wave 4b verify round), as RowStore writes it.
    public void SeedVersion(CompiledSnapshot snapshot, DateTime? publishedOnUtc = null)
    {
        if (snapshot == null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        lock (_gate)
        {
            string setKey = snapshot.ConfigurationSetKey;
            StoredRow? set = Committed("configuration-set").FirstOrDefault(r => r.Key == setKey);
            JsonTreeNode setColumns = Columns(
                ("setKind", JsonTreeNode.FromString(EngineContract.ConfigurationSetKinds.ToWire(ConfigurationSetKind.Working))),
                ("isInForce", JsonTreeNode.True),
                ("currentVersionNumber", JsonTreeNode.FromInt64(snapshot.VersionNumber)));
            string setId = set?.Id ?? NewIdLocked("configuration-set");
            Table("configuration-set")[setId] = new StoredRow("configuration-set", setId, setKey, setColumns);

            string versionId = NewIdLocked("configuration-set-version");
            Table("configuration-set-version")[versionId] = new StoredRow("configuration-set-version", versionId, null, Columns(
                ("configurationSet", JsonTreeNode.FromString(setKey)),
                ("versionNumber", JsonTreeNode.FromInt64(snapshot.VersionNumber)),
                ("contentJson", snapshot.ContentJson),
                ("contentHash", JsonTreeNode.FromString(snapshot.ContentHash)),
                ("snapshotSchemaVersion", JsonTreeNode.FromString(snapshot.Snapshot.SchemaVersion.Major + "." + snapshot.Snapshot.SchemaVersion.Minor)),
                ("publishedOn", publishedOnUtc.HasValue ? JsonTreeNode.FromString(CanonicalJson.FormatUtc(publishedOnUtc.Value.Kind == DateTimeKind.Utc ? publishedOnUtc.Value : publishedOnUtc.Value.ToUniversalTime())) : null)));

            foreach (SnapshotEntity entity in snapshot.Snapshot.Entities)
            {
                SortedDictionary<string, StoredRow> table = Table(entity.Name);
                foreach (string stale in table.Values.Where(r => r.Key != null).Select(r => r.Id).ToList())
                {
                    table.Remove(stale);
                }

                foreach (SnapshotRow row in entity.Rows)
                {
                    string id = NewIdLocked(entity.Name);
                    JsonTreeNode columns = Merge(row.Columns, Columns(
                        ("key", JsonTreeNode.FromString(row.Key)),
                        ("isActive", JsonTreeNode.FromBoolean(row.IsActive)),
                        ("origin", JsonTreeNode.FromString(EngineContract.Origins.ToWire(row.Origin))),
                        ("engineMeaning", OptionalText(row.EngineMeaning))));
                    table[id] = new StoredRow(entity.Name, id, row.KeyPath, columns);
                }
            }
        }
    }

    // Replaces a stored row's columns outside the engine: only for tests that prove what the engine does with a
    // tampered store (a changed Content Json, for example).
    public void Tamper(string entity, string id, JsonTreeNode columns)
    {
        lock (_gate)
        {
            StoredRow row = Table(entity)[id];
            Table(entity)[id] = new StoredRow(entity, id, row.Key, columns);
        }
    }

    // ---- Internals ----

    // Small JSON helpers (the runtime keeps its own internal; this assembly needs only these three).
    private static JsonTreeNode Columns(params (string Name, JsonTreeNode? Value)[] members)
    {
        return JsonTreeNode.CreateObject(members.Where(m => m.Value != null).Select(m => new KeyValuePair<string, JsonTreeNode>(m.Name, m.Value!)));
    }

    // The members of b replace those of a with the same name.
    private static JsonTreeNode Merge(JsonTreeNode a, JsonTreeNode b)
    {
        var members = new SortedDictionary<string, JsonTreeNode>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, JsonTreeNode> member in a.Members)
        {
            members[member.Key] = member.Value;
        }

        foreach (KeyValuePair<string, JsonTreeNode> member in b.Members)
        {
            members[member.Key] = member.Value;
        }

        return JsonTreeNode.CreateObject(members);
    }

    private static JsonTreeNode? OptionalText(string? value)
    {
        return value == null ? null : JsonTreeNode.FromString(value);
    }

    private IEnumerable<StoredRow> Committed(string entity)
    {
        return _rows.TryGetValue(entity, out SortedDictionary<string, StoredRow>? rows) ? rows.Values : Enumerable.Empty<StoredRow>();
    }

    private SortedDictionary<string, StoredRow> Table(string entity)
    {
        if (!_rows.TryGetValue(entity, out SortedDictionary<string, StoredRow>? rows))
        {
            rows = new SortedDictionary<string, StoredRow>(StringComparer.Ordinal);
            _rows.Add(entity, rows);
        }

        return rows;
    }

    private string NewIdLocked(string entity)
    {
        _nextId++;
        return entity + "-" + _nextId.ToString("D6", CultureInfo.InvariantCulture);
    }

    private sealed class Transaction : IStoreTransaction, IKeySetReader
    {
        private readonly InMemoryStore _store;

        // Pending writes by entity and ID. A null row is a delete.
        private readonly Dictionary<string, SortedDictionary<string, StoredRow?>> _pending = new(StringComparer.Ordinal);
        private bool _done;

        public Transaction(InMemoryStore store)
        {
            _store = store;
        }

        public StoredRow? Get(string entity, string id)
        {
            CountRead();
            return Find(entity, id);
        }

        public StoredRow? GetByKey(string entity, string key)
        {
            CountRead();
            return Merged(entity).FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.Ordinal));
        }

        public IReadOnlyList<StoredRow> Query(RowQuery query)
        {
            CountRead();
            return Merged(query.Entity).Where(query.Matches).ToList();
        }

        public IReadOnlyList<StoredRow> GetMany(string entity, IReadOnlyCollection<string> ids)
        {
            CountRead();
            return KeySetReads.Distinct(ids).Select(id => Find(entity, id)).Where(r => r != null).Select(r => r!).OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
        }

        public IReadOnlyList<StoredRow> QueryIn(string entity, string column, IReadOnlyCollection<string> values)
        {
            CountRead();
            var set = new HashSet<string>(values ?? Array.Empty<string>(), StringComparer.Ordinal);
            return Merged(entity).Where(r => KeySetReads.HoldsOneOf(r, column, set)).ToList();
        }

        private StoredRow? Find(string entity, string id)
        {
            if (_pending.TryGetValue(entity, out SortedDictionary<string, StoredRow?>? rows) && rows.TryGetValue(id, out StoredRow? row))
            {
                return row;
            }

            return _store.Get(entity, id);
        }

        private void CountRead()
        {
            System.Threading.Interlocked.Increment(ref _store._transactionReads);
        }

        public string NewId(string entity)
        {
            lock (_store._gate)
            {
                return _store.NewIdLocked(entity);
            }
        }

        // One above the highest number given or held (committed rows and this transaction's own), reserved at once.
        public long NextRecordNumber(string entity, string prefix)
        {
            lock (_store._gate)
            {
                long highest = _store._recordNumbers.TryGetValue(entity, out long given) ? given : 0;
                foreach (StoredRow row in Merged(entity))
                {
                    long? held = RecordNumbers.Parse(prefix, row.Text(RecordNumbers.Column));
                    if (held.HasValue && held.Value > highest)
                    {
                        highest = held.Value;
                    }
                }

                _store._recordNumbers[entity] = highest + 1;
                return highest + 1;
            }
        }

        public void Create(string entity, string id, string? key, JsonTreeNode columns)
        {
            Open();
            if (Find(entity, id) != null)
            {
                throw new InvalidOperationException(entity + " " + id + " exists already.");
            }

            Pending(entity)[id] = new StoredRow(entity, id, key, columns);
        }

        public void Update(string entity, string id, JsonTreeNode columns)
        {
            Open();
            StoredRow row = Find(entity, id) ?? throw new InvalidOperationException(entity + " " + id + " does not exist.");
            var merged = new SortedDictionary<string, JsonTreeNode>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, JsonTreeNode> member in row.Columns.Members)
            {
                merged[member.Key] = member.Value;
            }

            foreach (KeyValuePair<string, JsonTreeNode> member in columns.Members)
            {
                if (member.Value.Kind == JsonTreeKind.Null)
                {
                    merged.Remove(member.Key);
                }
                else
                {
                    merged[member.Key] = member.Value;
                }
            }

            Pending(entity)[id] = new StoredRow(entity, id, row.Key, JsonTreeNode.CreateObject(merged));
        }

        public void Delete(string entity, string id)
        {
            Open();
            if (Find(entity, id) == null)
            {
                throw new InvalidOperationException(entity + " " + id + " does not exist.");
            }

            Pending(entity)[id] = null;
        }

        public void Commit()
        {
            Open();
            lock (_store._gate)
            {
                foreach ((string entity, string column) in UniqueKeys)
                {
                    if (!_pending.TryGetValue(entity, out SortedDictionary<string, StoredRow?>? added))
                    {
                        continue;
                    }

                    foreach (StoredRow row in added.Values.Where(r => r != null).Select(r => r!))
                    {
                        string? value = row.Text(column);
                        if (value != null && _store.Committed(entity).Any(r => r.Id != row.Id && string.Equals(r.Text(column), value, StringComparison.Ordinal)))
                        {
                            throw new StoreConflictException(entity + " " + column + " \"" + value + "\" is taken by a transaction that committed first.");
                        }
                    }
                }

                foreach (KeyValuePair<string, SortedDictionary<string, StoredRow?>> entity in _pending)
                {
                    SortedDictionary<string, StoredRow> table = _store.Table(entity.Key);
                    foreach (KeyValuePair<string, StoredRow?> write in entity.Value)
                    {
                        if (write.Value == null)
                        {
                            table.Remove(write.Key);
                        }
                        else
                        {
                            table[write.Key] = write.Value;
                        }
                    }
                }

                _store.CommitCount++;
            }

            _done = true;
        }

        public void Dispose()
        {
            _done = true;
            _pending.Clear();
        }

        private void Open()
        {
            if (_done)
            {
                throw new InvalidOperationException("The transaction is committed or rolled back.");
            }
        }

        private SortedDictionary<string, StoredRow?> Pending(string entity)
        {
            if (!_pending.TryGetValue(entity, out SortedDictionary<string, StoredRow?>? rows))
            {
                rows = new SortedDictionary<string, StoredRow?>(StringComparer.Ordinal);
                _pending.Add(entity, rows);
            }

            return rows;
        }

        private IEnumerable<StoredRow> Merged(string entity)
        {
            var rows = new SortedDictionary<string, StoredRow>(StringComparer.Ordinal);
            foreach (StoredRow row in _store.Rows(entity))
            {
                rows[row.Id] = row;
            }

            if (_pending.TryGetValue(entity, out SortedDictionary<string, StoredRow?>? pending))
            {
                foreach (KeyValuePair<string, StoredRow?> write in pending)
                {
                    if (write.Value == null)
                    {
                        rows.Remove(write.Key);
                    }
                    else
                    {
                        rows[write.Key] = write.Value;
                    }
                }
            }

            return rows.Values;
        }
    }
}

// A clock a test or the local harness sets by hand.
public sealed class ManualClock : IEngineClock
{
    public ManualClock(DateTime utcNow)
    {
        UtcNow = utcNow;
    }

    public DateTime UtcNow { get; set; }

    public void Advance(TimeSpan by)
    {
        UtcNow = UtcNow.Add(by);
    }
}
