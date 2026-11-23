using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Snapshots;
using Product.Engine.Runtime.Ports;

namespace Product.Azure.Stores;

// The engine's storage ports over one row database, written once for both providers. It behaves as InMemoryStore in
// Product.Engine.Testing does, so the engine fixtures, the walkthrough and the MCP replays give the same answers on a
// file or a database as in process:
// - committed reads by ID, by key and by query, in ID order (ordinal);
// - a transaction is an overlay: writes stay in memory, reads see them, nothing reaches the database before Commit, and
//   Dispose without Commit discards them;
// - Commit is one database transaction. A row is inserted once with version 1; an update or a delete names the version
//   it read, so a row another transaction changed first throws StoreConflictException, as does a unique key another
//   transaction took first. The engine retries a conflict once and so sees the first result;
// - the two unique keys of document 25, section 8 (Audit Entry Request Key, Deployment Record Idempotency Key), with the
//   in-memory store's message;
// - the other keys the logical model asks the store to enforce (StoreKeys.cs), which the in-memory store does not hold:
//   the database refuses a second row with the same value at commit, as a StoreConflictException, so the engine's retry
//   sees the first result (document 25, section 8, concurrent duplicates);
// - seeding (IStoreSeeder) with the same rows and columns as InMemoryStore.Seed, Tamper and SeedVersion.
// It holds no rule: the engine decides what to write.
//
// Beside the engine's rows it keeps the approval outbox (ApprovalOutbox.cs), a host table outside the entity catalog:
// an engine commit that inserts a pending Approval Record opens its outbox row, and one that writes a final outcome
// settles it, in the same database transaction (the commit hook). Seeding never touches it, and Dump leaves it out.
//
// A few checks go further than the in-memory store, so a local run fails where Azure SQL would: a row of an entity the
// logical model has no table for cannot be written (reads of it find nothing, as in memory); a row of an append-only
// entity (document 22, section 2.3, class A and O14) cannot be changed once committed; an ID is at most 36 printable
// ASCII characters and a key path at most 450 characters; a second create of one ID, or a second Configuration Set
// Version with the same set and number, is a conflict. Two transactions that change one row are a conflict for the
// second to commit, where the in-memory store lets the last one win.
public abstract class RowStore : IProductStore
{
    // The longest row ID and key path the Azure SQL tables hold (varchar(36) and nvarchar(450)).
    public const int MaxIdLength = 36;
    public const int MaxKeyLength = 450;

    // The longest string value a database narrows a query by (JSON_VALUE returns at most 4000 characters).
    public const int MaxNarrowingLength = 4000;

    private readonly RowStoreOptions _options;
    private bool _disposed;

    protected RowStore(RowStoreOptions options, IRowDatabase database, EntityCatalog? catalog = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrEmpty(options.StoreId))
        {
            throw new ArgumentException("A store has an ID (RowStoreOptions.StoreId).", nameof(options));
        }

        Database = database ?? throw new ArgumentNullException(nameof(database));
        Catalog = catalog ?? EntityCatalog.Embedded;
        StoreId = options.StoreId;
    }

    public string StoreId { get; }

    public EntityCatalog Catalog { get; }

    protected IRowDatabase Database { get; }

    // ---- IRecordReader over committed rows ----

    public StoredRow? Get(string entity, string id)
    {
        return Catalog.TryGet(entity, out EntityInfo? info) && IsStorableId(id) ? Database.Get(info, id)?.Row : null;
    }

    public StoredRow? GetByKey(string entity, string key)
    {
        return CommittedByKey(entity, key).FirstOrDefault()?.Row;
    }

    public IReadOnlyList<StoredRow> Query(RowQuery query)
    {
        if (query == null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        return CommittedQuery(query).Select(r => r.Row).ToList();
    }

    public IStoreTransaction Begin()
    {
        ThrowIfDisposed();
        return new Transaction(this);
    }

    // Every committed row of an entity, by ID.
    public IReadOnlyList<StoredRow> Rows(string entity)
    {
        return Catalog.TryGet(entity, out EntityInfo? info) ? Database.Query(info, Array.Empty<KeyValuePair<string, string>>()).Select(r => r.Row).OrderBy(r => r.Id, StringComparer.Ordinal).ToList() : Array.Empty<StoredRow>();
    }

    // Every committed row, by entity then ID, as canonical text, in the form of InMemoryStore.Dump: two stores given the
    // same calls give the same text.
    public string Dump()
    {
        var lines = new List<string>();
        foreach (EntityInfo entity in Catalog.Entities.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            foreach (StoredRow row in Rows(entity.Key))
            {
                lines.Add(entity.Key + " " + row.Id + (row.Key == null ? string.Empty : " [" + row.Key + "]") + " " + CanonicalJson.Write(row.Columns, CanonicalProfile.Snapshot));
            }
        }

        return string.Join("\n", lines);
    }

    // ---- IConfigurationStore ----

    // The working set marked in force and its current version number (C01), as InMemoryStore reads it.
    public InForceVersion? ProbeInForceVersion()
    {
        StoredRow? set = Database.ProbeInForce()?.Row;
        long? number = set?.Number("currentVersionNumber");
        return set == null || set.Key == null || !number.HasValue || number.Value < 1 || number.Value > int.MaxValue ? null : new InForceVersion(set.Key, (int)number.Value);
    }

    public StoredVersion? GetVersion(string configurationSetKey, int versionNumber)
    {
        if (string.IsNullOrEmpty(configurationSetKey))
        {
            return null;
        }

        StoredRow? version = Database.GetVersionRows(configurationSetKey, versionNumber)
            .Select(r => r.Row)
            .OrderBy(r => r.Id, StringComparer.Ordinal)
            .FirstOrDefault(r => string.Equals(r.Text("configurationSet"), configurationSetKey, StringComparison.Ordinal) && r.Number("versionNumber") == versionNumber);
        JsonTreeNode? content = version?.Column("contentJson");
        return version == null || content == null ? null : new StoredVersion(configurationSetKey, versionNumber, content, version.Text("contentHash") ?? string.Empty);
    }

    // ---- ICounterStore ----

    public long Increment(string counterKey, DateTime windowStartUtc, TimeSpan windowLength)
    {
        if (string.IsNullOrEmpty(counterKey))
        {
            throw new ArgumentException("A counter has a key.", nameof(counterKey));
        }

        if (windowStartUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("A counter window starts at a UTC time.", nameof(windowStartUtc));
        }

        ThrowIfDisposed();
        return Database.Increment(counterKey, windowStartUtc, windowLength);
    }

    // ---- IStoreSeeder (local runs, demos and tests; never an engine write) ----

    public bool HasInForceVersion => ProbeInForceVersion() != null;

    // A row that exists before the engine runs. Without an ID, RowStoreOptions.NewSeedId makes one. A row with the same
    // ID is replaced, as InMemoryStore.Seed does.
    public string Seed(string entity, JsonTreeNode columns, string? id = null, string? key = null)
    {
        EntityInfo info = RequireEntity(entity);
        CheckColumns(columns);
        id ??= _options.NewSeedId(entity);
        CheckId(id);
        CheckKey(key);
        StoredRowData? existing = Database.Get(info, id);
        if (existing != null && info.Storage == EntityStorage.AppendOnlyLedger)
        {
            throw AppendOnlyRow(info, id);
        }

        string? storedKey = string.IsNullOrEmpty(key) ? null : key;
        RowChange change = existing == null
            ? RowChange.Insert(info, id, storedKey, columns)
            : RowChange.Update(info, id, storedKey, columns, existing.Version, existing.Row);
        CheckUniqueLengths(change);
        Database.Apply(new[] { change }, StoreCallContext.Current, RowStoreOperations.Seed);
        return id;
    }

    // Replaces a stored row's columns and keeps its key, as InMemoryStore.Tamper does. Throws when the row does not exist,
    // and for an append-only row, which Azure SQL never changes (an append-only ledger table takes only inserts).
    public void Replace(string entity, string id, JsonTreeNode columns)
    {
        EntityInfo info = RequireEntity(entity);
        CheckColumns(columns);
        StoredRowData existing = (IsStorableId(id) ? Database.Get(info, id) : null)
            ?? throw new InvalidOperationException(entity + " " + id + " does not exist.");
        if (info.Storage == EntityStorage.AppendOnlyLedger)
        {
            throw AppendOnlyRow(info, id);
        }

        RowChange change = RowChange.Update(info, id, existing.Row.Key, columns, existing.Version, existing.Row);
        CheckUniqueLengths(change);
        Database.Apply(new[] { change }, StoreCallContext.Current, RowStoreOperations.Seed);
    }

    // A version as an earlier activation left it in force, with the rows InMemoryStore.SeedVersion writes, in the same
    // order (so seeded IDs match): the working set in force at this version, the Configuration Set Version row, and the
    // live rows equal to the snapshot, replacing the live rows of an earlier seeded version. One database transaction.
    public void SeedVersion(CompiledSnapshot snapshot)
    {
        if (snapshot == null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        var changes = new List<RowChange>();
        string setKey = snapshot.ConfigurationSetKey;
        EntityInfo setEntity = RequireEntity("configuration-set");
        StoredRowData? set = CommittedByKey("configuration-set", setKey).FirstOrDefault();
        JsonTreeNode setColumns = RowJson.Object(
            ("setKind", JsonTreeNode.FromString(EngineContract.ConfigurationSetKinds.ToWire(ConfigurationSetKind.Working))),
            ("isInForce", JsonTreeNode.True),
            ("currentVersionNumber", JsonTreeNode.FromInt64(snapshot.VersionNumber)));
        string setId = set?.Row.Id ?? _options.NewSeedId("configuration-set");
        changes.Add(set == null
            ? RowChange.Insert(setEntity, setId, setKey, setColumns)
            : RowChange.Update(setEntity, setId, setKey, setColumns, set.Version, set.Row));

        EntityInfo versionEntity = RequireEntity("configuration-set-version");
        string versionId = _options.NewSeedId("configuration-set-version");
        changes.Add(RowChange.Insert(versionEntity, versionId, null, RowJson.Object(
            ("configurationSet", JsonTreeNode.FromString(setKey)),
            ("versionNumber", JsonTreeNode.FromInt64(snapshot.VersionNumber)),
            ("contentJson", snapshot.ContentJson),
            ("contentHash", JsonTreeNode.FromString(snapshot.ContentHash)),
            ("snapshotSchemaVersion", JsonTreeNode.FromString(snapshot.Snapshot.SchemaVersion.Major + "." + snapshot.Snapshot.SchemaVersion.Minor)))));

        foreach (SnapshotEntity entity in snapshot.Snapshot.Entities)
        {
            EntityInfo info = RequireEntity(entity.Name);
            foreach (StoredRowData stale in Database.Query(info, Array.Empty<KeyValuePair<string, string>>()).Where(r => r.Row.Key != null))
            {
                changes.Add(RowChange.Delete(info, stale.Row.Id, stale.Version));
            }

            foreach (SnapshotRow row in entity.Rows)
            {
                string id = _options.NewSeedId(entity.Name);
                JsonTreeNode columns = RowJson.Merge(row.Columns, RowJson.Object(
                    ("key", JsonTreeNode.FromString(row.Key)),
                    ("isActive", JsonTreeNode.FromBoolean(row.IsActive)),
                    ("origin", JsonTreeNode.FromString(EngineContract.Origins.ToWire(row.Origin))),
                    ("engineMeaning", row.EngineMeaning == null ? null : JsonTreeNode.FromString(row.EngineMeaning))));
                CheckKey(row.KeyPath);
                changes.Add(RowChange.Insert(info, id, row.KeyPath, columns));
            }
        }

        Database.Apply(changes, StoreCallContext.Current, RowStoreOperations.Seed);
    }

    // ---- IApprovalOutboxStore (host bookkeeping for the approval runner; never an engine row) ----

    public ApprovalOutboxItem? GetOutboxItem(string approvalRecordId)
    {
        ThrowIfDisposed();
        return IsStorableId(approvalRecordId) ? Database.OutboxGet(approvalRecordId) : null;
    }

    public IReadOnlyList<ApprovalOutboxItem> ClaimableOutboxItems(DateTime nowUtc, int limit)
    {
        ThrowIfDisposed();
        return Database.OutboxClaimable(nowUtc.Kind == DateTimeKind.Utc ? nowUtc : DateTime.SpecifyKind(nowUtc.ToUniversalTime(), DateTimeKind.Utc), Math.Max(1, limit));
    }

    public IReadOnlyList<ApprovalOutboxItem> OutboxItems(int limit)
    {
        ThrowIfDisposed();
        return Database.OutboxAll(Math.Max(1, limit));
    }

    public bool TrySetOutboxItem(ApprovalOutboxItem next)
    {
        if (next == null)
        {
            throw new ArgumentNullException(nameof(next));
        }

        ThrowIfDisposed();
        return IsStorableId(next.ApprovalRecordId) && Database.OutboxSet(next);
    }

    // ---- IProductStore ----

    public void EnsureSchema()
    {
        ThrowIfDisposed();
        Database.EnsureSchema(Catalog);
    }

    public bool CanConnect()
    {
        try
        {
            return !_disposed && Database.CanConnect();
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            Database.Dispose();
        }

        _disposed = true;
    }

    // ---- Shared by the providers ----

    // The message InMemoryStore gives when a unique key is taken.
    public static StoreConflictException UniqueKeyTaken(EntityInfo entity, JsonTreeNode? columns)
    {
        string names = string.Join(" and ", entity.UniqueColumns.Select(c => c.JsonName));
        string? value = columns == null ? null : RowJson.UniqueValue(entity, columns, "/");
        return new StoreConflictException(entity.Key + " " + (names.Length == 0 ? "key" : names) + " \"" + (value ?? string.Empty) + "\" is taken by a transaction that committed first.");
    }

    // The message when a store key's value is taken by another row: committed by another transaction, or written twice in
    // this one.
    public static StoreConflictException StoreKeyTaken(StoreKeyValue value)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        string names = string.Join(" and ", value.Key.Parts.Select(p => p == StoreKey.KeyPathPart ? "key" : p));
        return new StoreConflictException(value.Key.EntityKey + " " + names + " \"" + value.Display + "\" is taken by another row.");
    }

    public static StoreConflictException RowCreatedFirst(EntityInfo entity, string id)
    {
        return new StoreConflictException(entity.Key + " " + id + " was created by a transaction that committed first.");
    }

    public static StoreConflictException RowChangedFirst(EntityInfo entity, string id)
    {
        return new StoreConflictException(entity.Key + " " + id + " was changed or deleted by a transaction that committed first.");
    }

    // A committed row of an append-only entity (document 22, section 2.3, class A and O14) is never changed or deleted,
    // by the engine or by seeding: Azure SQL keeps it in an append-only ledger table, which takes only inserts.
    public static InvalidOperationException AppendOnlyRow(EntityInfo entity, string id)
    {
        return new InvalidOperationException(entity.Key + " " + id + " is append-only (history class " + entity.HistoryClass + "): its rows are inserted once and never changed or deleted.");
    }

    // ---- Internals ----

    private static bool IsStorableId(string? id)
    {
        if (string.IsNullOrEmpty(id) || id!.Length > MaxIdLength)
        {
            return false;
        }

        foreach (char c in id)
        {
            if (c < '!' || c > '~')
            {
                return false;
            }
        }

        return true;
    }

    private static void CheckId(string id)
    {
        if (!IsStorableId(id))
        {
            throw new InvalidOperationException("Row ID \"" + id + "\" cannot be stored: an ID is 1 to " + MaxIdLength + " printable ASCII characters, such as a GUID.");
        }
    }

    private static void CheckKey(string? key)
    {
        if (key != null && key.Length > MaxKeyLength)
        {
            throw new InvalidOperationException("Key path \"" + key.Substring(0, 64) + "...\" is longer than " + MaxKeyLength + " characters.");
        }
    }

    private static void CheckColumns(JsonTreeNode columns)
    {
        if (columns == null || columns.Kind != JsonTreeKind.Object)
        {
            throw new ArgumentException("A row's columns are a JSON object.", nameof(columns));
        }
    }

    private static void CheckUniqueLengths(RowChange change)
    {
        if (change.Columns == null)
        {
            return;
        }

        foreach (PromotedColumn column in change.Entity.UniqueColumns)
        {
            if (column.IsTooLong(change.Columns))
            {
                throw new InvalidOperationException(change.Entity.Key + " " + column.JsonName + " is longer than " + column.MaxLength + " characters, the most its unique index holds.");
            }
        }
    }

    private EntityInfo RequireEntity(string entity)
    {
        ThrowIfDisposed();
        return Catalog.Get(entity);
    }

    private IReadOnlyList<StoredRowData> CommittedByKey(string entity, string? key)
    {
        if (string.IsNullOrEmpty(key) || key!.Length > MaxKeyLength || !Catalog.TryGet(entity, out EntityInfo? info))
        {
            return Array.Empty<StoredRowData>();
        }

        // Exactly this key: Azure SQL compares strings with trailing spaces ignored, even in a binary collation, so the
        // database may return a row whose key differs by trailing spaces.
        return Database.GetByKey(info, key)
            .Where(r => string.Equals(r.Row.Key, key, StringComparison.Ordinal))
            .OrderBy(r => r.Row.Id, StringComparer.Ordinal)
            .ToList();
    }

    // The committed rows that match, by ID: the database narrows by the string conditions it can compare, then every
    // condition is checked here (a JSON null matches an absent column; numbers and booleans compare as JSON).
    private IReadOnlyList<StoredRowData> CommittedQuery(RowQuery query)
    {
        if (!Catalog.TryGet(query.Entity, out EntityInfo? info))
        {
            return Array.Empty<StoredRowData>();
        }

        var narrowing = new List<KeyValuePair<string, string>>();
        foreach (KeyValuePair<string, JsonTreeNode> condition in query.Conditions)
        {
            if (condition.Value.Kind == JsonTreeKind.String && condition.Value.GetString().Length <= MaxNarrowingLength && IsPlainName(condition.Key)
                && !narrowing.Any(n => string.Equals(n.Key, condition.Key, StringComparison.Ordinal)))
            {
                narrowing.Add(new KeyValuePair<string, string>(condition.Key, condition.Value.GetString()));
            }
        }

        return Database.Query(info, narrowing)
            .Where(r => query.Matches(r.Row))
            .OrderBy(r => r.Row.Id, StringComparer.Ordinal)
            .ToList();
    }

    // A column name a JSON path can carry without escaping.
    private static bool IsPlainName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 128)
        {
            return false;
        }

        foreach (char c in name)
        {
            if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_'))
            {
                return false;
            }
        }

        return true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(GetType().Name);
        }
    }

    // One engine transaction. Pending writes by entity and ID; a null row is a delete.
    private sealed class Transaction : IStoreTransaction
    {
        private readonly RowStore _store;
        private readonly SortedDictionary<string, SortedDictionary<string, Pending>> _pending = new(StringComparer.Ordinal);
        private bool _done;

        public Transaction(RowStore store)
        {
            _store = store;
        }

        public StoredRow? Get(string entity, string id)
        {
            if (_pending.TryGetValue(entity, out SortedDictionary<string, Pending>? rows) && id != null && rows.TryGetValue(id, out Pending? pending))
            {
                return pending.Row;
            }

            return _store.Get(entity, id!);
        }

        public StoredRow? GetByKey(string entity, string key)
        {
            var rows = new SortedDictionary<string, StoredRow>(StringComparer.Ordinal);
            SortedDictionary<string, Pending>? pending = _pending.TryGetValue(entity, out SortedDictionary<string, Pending>? p) ? p : null;
            foreach (StoredRowData row in _store.CommittedByKey(entity, key))
            {
                if (pending == null || !pending.ContainsKey(row.Row.Id))
                {
                    rows[row.Row.Id] = row.Row;
                }
            }

            if (pending != null)
            {
                foreach (Pending write in pending.Values)
                {
                    if (write.Row != null && string.Equals(write.Row.Key, key, StringComparison.Ordinal))
                    {
                        rows[write.Row.Id] = write.Row;
                    }
                }
            }

            return rows.Values.FirstOrDefault();
        }

        public IReadOnlyList<StoredRow> Query(RowQuery query)
        {
            if (query == null)
            {
                throw new ArgumentNullException(nameof(query));
            }

            var rows = new SortedDictionary<string, StoredRow>(StringComparer.Ordinal);
            SortedDictionary<string, Pending>? pending = _pending.TryGetValue(query.Entity, out SortedDictionary<string, Pending>? p) ? p : null;
            foreach (StoredRowData row in _store.CommittedQuery(query))
            {
                if (pending == null || !pending.ContainsKey(row.Row.Id))
                {
                    rows[row.Row.Id] = row.Row;
                }
            }

            if (pending != null)
            {
                foreach (Pending write in pending.Values)
                {
                    if (write.Row != null && query.Matches(write.Row))
                    {
                        rows[write.Row.Id] = write.Row;
                    }
                }
            }

            return rows.Values.ToList();
        }

        public string NewId(string entity)
        {
            return _store._options.NewId(entity);
        }

        public void Create(string entity, string id, string? key, JsonTreeNode columns)
        {
            Open();
            EntityInfo info = _store.RequireEntity(entity);
            CheckId(id);
            CheckKey(key);
            if (Get(entity, id) != null)
            {
                throw new InvalidOperationException(entity + " " + id + " exists already.");
            }

            var row = new StoredRow(entity, id, key, columns);
            SortedDictionary<string, Pending> rows = Rows(entity);
            if (rows.TryGetValue(id, out Pending? deleted))
            {
                // Deleted earlier in this transaction: the commit writes the new row over the committed one.
                deleted.Row = row;
            }
            else
            {
                rows[id] = new Pending(info, row, null);
            }
        }

        public void Update(string entity, string id, JsonTreeNode columns)
        {
            Open();
            EntityInfo info = _store.RequireEntity(entity);
            if (columns == null || columns.Kind != JsonTreeKind.Object)
            {
                throw new ArgumentException("A row's columns are a JSON object.", nameof(columns));
            }

            Pending pending = Touch(info, entity, id);
            StoredRow row = pending.Row ?? throw new InvalidOperationException(entity + " " + id + " does not exist.");
            pending.Row = new StoredRow(entity, id, row.Key, RowJson.Patch(row.Columns, columns));
        }

        public void Delete(string entity, string id)
        {
            Open();
            EntityInfo info = _store.RequireEntity(entity);
            Pending pending = Touch(info, entity, id);
            if (pending.Row == null)
            {
                throw new InvalidOperationException(entity + " " + id + " does not exist.");
            }

            pending.Row = null;
        }

        public void Commit()
        {
            Open();
            var changes = new List<RowChange>();
            foreach (SortedDictionary<string, Pending> rows in _pending.Values)
            {
                foreach (KeyValuePair<string, Pending> write in rows)
                {
                    Pending p = write.Value;
                    RowChange? change = p.CommittedVersion == null
                        ? (p.Row == null ? null : RowChange.Insert(p.Entity, write.Key, p.Row.Key, p.Row.Columns))
                        : (p.Row == null ? RowChange.Delete(p.Entity, write.Key, p.CommittedVersion.Value) : RowChange.Update(p.Entity, write.Key, p.Row.Key, p.Row.Columns, p.CommittedVersion.Value, p.Committed));
                    if (change != null)
                    {
                        CheckUniqueLengths(change);
                        changes.Add(change);
                    }
                }
            }

            if (changes.Count > 0)
            {
                bool activation = changes.Any(c => c.Kind == RowChangeKind.Insert && string.Equals(c.Entity.Key, "configuration-set-version", StringComparison.Ordinal));

                // The commit hook: a new pending Approval Record opens its outbox row and a final outcome settles it, in
                // the same database transaction as the engine's rows.
                IReadOnlyList<ApprovalOutboxWrite> outbox = ApprovalOutboxHook.Plan(changes, DateTime.UtcNow);
                _store.Database.Apply(changes, StoreCallContext.Current, activation ? RowStoreOperations.Activation : RowStoreOperations.Write, outbox);
            }

            _done = true;
            _pending.Clear();
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

        private SortedDictionary<string, Pending> Rows(string entity)
        {
            if (!_pending.TryGetValue(entity, out SortedDictionary<string, Pending>? rows))
            {
                rows = new SortedDictionary<string, Pending>(StringComparer.Ordinal);
                _pending.Add(entity, rows);
            }

            return rows;
        }

        // The pending entry of a row this transaction changes, made from the committed row (with the version it read) on
        // first touch. A committed append-only row is never changed (Azure SQL refuses it in an append-only ledger table);
        // a row created in this transaction still may be, since the commit inserts it once.
        private Pending Touch(EntityInfo info, string entity, string id)
        {
            SortedDictionary<string, Pending> rows = Rows(entity);
            if (id != null && rows.TryGetValue(id, out Pending? pending))
            {
                if (pending.CommittedVersion != null && info.Storage == EntityStorage.AppendOnlyLedger)
                {
                    throw AppendOnlyRow(info, id);
                }

                return pending;
            }

            StoredRowData committed = (IsStorableId(id) ? _store.Database.Get(info, id!) : null)
                ?? throw new InvalidOperationException(entity + " " + id + " does not exist.");
            if (info.Storage == EntityStorage.AppendOnlyLedger)
            {
                throw AppendOnlyRow(info, id!);
            }

            pending = new Pending(info, committed.Row, committed.Version, committed.Row);
            rows[id!] = pending;
            return pending;
        }
    }

    private sealed class Pending
    {
        public Pending(EntityInfo entity, StoredRow? row, long? committedVersion, StoredRow? committed = null)
        {
            Entity = entity;
            Row = row;
            CommittedVersion = committedVersion;
            Committed = committed;
        }

        public EntityInfo Entity { get; }

        // The row as this transaction leaves it; null when deleted.
        public StoredRow? Row { get; set; }

        // The version of the committed row when the transaction first touched it; null when the row did not exist.
        public long? CommittedVersion { get; }

        // The committed row as the transaction first read it, at CommittedVersion; null when the row did not exist.
        public StoredRow? Committed { get; }
    }
}
