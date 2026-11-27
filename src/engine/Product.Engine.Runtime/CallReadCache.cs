using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Runtime.Ports;

namespace Product.Engine.Runtime;

// The rows one read call has read, kept for the rest of that call (document 25, B25-23). A read such as the portfolio
// summary asks the store about the same rows many times: the owners of each application, the person behind each stamp,
// the portfolio of each application. Over a network every ask is a round trip, so the reads of a read call go through
// this cache: each row, query and key is read from the store once, and a read the cache can answer from rows it holds
// costs no store call:
// - a query with no condition loads the whole entity; later reads of that entity are answered from it;
// - a key set read (IKeySetReader, used by ComposedCall.Prefetch) loads the rows of many values of one column at once;
//   a later query with that column and one of those values is answered from them;
// - a query with one text condition also counts as a key set read of its one value;
// - every row read is kept by ID, so a later read by ID of it costs nothing.
// An answer from the cache is the one the store would give: the same rows, filtered by RowQuery.Matches, by ID. A read
// call never commits, and any write through the cache empties it first, so it never answers from rows a write changed.
// One call, one thread: the cache is not shared.
internal sealed class CallReadCache : IStoreTransaction, IKeySetReader
{
    private static readonly IReadOnlyList<StoredRow> NoRows = new ReadOnlyCollection<StoredRow>(new List<StoredRow>());

    private readonly IStoreTransaction _inner;
    private readonly Dictionary<string, EntityReads> _entities = new(StringComparer.Ordinal);

    public CallReadCache(IStoreTransaction inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    // The reads the cache passed to the store.
    public int StoreReads { get; private set; }

    // ---- IRecordReader ----

    public StoredRow? Get(string entity, string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return _inner.Get(entity, id);
        }

        EntityReads e = Entity(entity);
        if (e.ById.TryGetValue(id, out StoredRow? known))
        {
            return known;
        }

        if (e.All != null)
        {
            return null;
        }

        StoreReads++;
        StoredRow? row = _inner.Get(entity, id);
        e.ById[id] = row;
        return row;
    }

    public StoredRow? GetByKey(string entity, string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return _inner.GetByKey(entity, key);
        }

        EntityReads e = Entity(entity);
        if (e.ByKey.TryGetValue(key, out StoredRow? known))
        {
            return known;
        }

        StoredRow? row;
        if (e.All != null)
        {
            row = e.All.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.Ordinal));
        }
        else
        {
            StoreReads++;
            row = _inner.GetByKey(entity, key);
            if (row != null)
            {
                e.ById[row.Id] = row;
            }
        }

        e.ByKey[key] = row;
        return row;
    }

    public IReadOnlyList<StoredRow> Query(RowQuery query)
    {
        if (query == null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        EntityReads e = Entity(query.Entity);
        string memo = MemoKey(query);
        if (e.Queries.TryGetValue(memo, out IReadOnlyList<StoredRow>? known))
        {
            return known;
        }

        IReadOnlyList<StoredRow> result = FromHeld(e, query) ?? FromStore(e, query);
        e.Queries[memo] = result;
        return result;
    }

    // ---- IKeySetReader ----

    public IReadOnlyList<StoredRow> GetMany(string entity, IReadOnlyCollection<string> ids)
    {
        EntityReads e = Entity(entity);
        List<string> keys = KeySetReads.Distinct(ids);
        if (e.All == null)
        {
            List<string> missing = keys.Where(id => id.Length > 0 && !e.ById.ContainsKey(id)).ToList();
            if (missing.Count > 0)
            {
                StoreReads++;
                foreach (StoredRow row in _inner.GetMany(entity, missing))
                {
                    e.ById[row.Id] = row;
                }

                foreach (string id in missing.Where(id => !e.ById.ContainsKey(id)))
                {
                    e.ById[id] = null;
                }
            }
        }

        return Sorted(keys.Select(id => e.ById.TryGetValue(id, out StoredRow? row) ? row : null).Where(r => r != null).Select(r => r!));
    }

    public IReadOnlyList<StoredRow> QueryIn(string entity, string column, IReadOnlyCollection<string> values)
    {
        EntityReads e = Entity(entity);
        List<string> keys = KeySetReads.Distinct(values);
        ColumnIndex index = Index(e, column);
        List<string> missing = keys.Where(v => !index.Covers(v)).ToList();
        if (missing.Count > 0)
        {
            StoreReads++;
            IReadOnlyList<StoredRow> rows = _inner.QueryIn(entity, column, missing);
            foreach (string value in missing)
            {
                index.Cover(value);
            }

            foreach (StoredRow row in rows)
            {
                e.ById[row.Id] = row;
                if (row.Column(column) is JsonTreeNode held && held.Kind == JsonTreeKind.String)
                {
                    index.Add(held.GetString(), row);
                }
            }
        }

        return Sorted(keys.SelectMany(index.Rows));
    }

    // ---- What a composed read call loads ahead (ComposedCall.Prefetch) ----

    public void LoadAll(string entity)
    {
        Query(RowQuery.For(entity));
    }

    // ---- IStoreTransaction: passed through. A write empties the cache first. ----

    public string NewId(string entity)
    {
        return _inner.NewId(entity);
    }

    public long NextRecordNumber(string entity, string prefix)
    {
        return _inner.NextRecordNumber(entity, prefix);
    }

    public void Create(string entity, string id, string? key, JsonTreeNode columns)
    {
        _entities.Clear();
        _inner.Create(entity, id, key, columns);
    }

    public void Update(string entity, string id, JsonTreeNode columns)
    {
        _entities.Clear();
        _inner.Update(entity, id, columns);
    }

    public void Delete(string entity, string id)
    {
        _entities.Clear();
        _inner.Delete(entity, id);
    }

    public void Commit()
    {
        _inner.Commit();
    }

    public void Dispose()
    {
        _entities.Clear();
        _inner.Dispose();
    }

    // ---- Internals ----

    // The answer from rows the cache holds, or null when it holds too few to answer.
    private static IReadOnlyList<StoredRow>? FromHeld(EntityReads e, RowQuery query)
    {
        IEnumerable<StoredRow>? candidates = null;
        foreach (KeyValuePair<string, JsonTreeNode> condition in query.Conditions)
        {
            if (condition.Value.Kind != JsonTreeKind.String)
            {
                continue;
            }

            if (e.All != null)
            {
                candidates = CompleteIndex(e, condition.Key).Rows(condition.Value.GetString());
                break;
            }

            if (e.Columns.TryGetValue(condition.Key, out ColumnIndex? index) && index.Covers(condition.Value.GetString()))
            {
                candidates = index.Rows(condition.Value.GetString());
                break;
            }
        }

        candidates ??= e.All;
        return candidates == null ? null : Sorted(candidates.Where(query.Matches));
    }

    private IReadOnlyList<StoredRow> FromStore(EntityReads e, RowQuery query)
    {
        StoreReads++;
        IReadOnlyList<StoredRow> rows = new ReadOnlyCollection<StoredRow>(_inner.Query(query).ToList());
        foreach (StoredRow row in rows)
        {
            e.ById[row.Id] = row;
        }

        if (query.Conditions.Count == 0)
        {
            e.All = rows;
            e.Columns.Clear();
            e.ByKey.Clear();
        }
        else if (query.Conditions.Count == 1 && query.Conditions[0].Value.Kind == JsonTreeKind.String)
        {
            // Every row with this value: a key set read of one value.
            ColumnIndex index = Index(e, query.Conditions[0].Key);
            string value = query.Conditions[0].Value.GetString();
            if (!index.Covers(value))
            {
                index.Cover(value);
                foreach (StoredRow row in rows)
                {
                    index.Add(value, row);
                }
            }
        }

        return rows;
    }

    private EntityReads Entity(string entity)
    {
        if (!_entities.TryGetValue(entity, out EntityReads? reads))
        {
            reads = new EntityReads();
            _entities.Add(entity, reads);
        }

        return reads;
    }

    private static ColumnIndex Index(EntityReads e, string column)
    {
        if (e.All != null)
        {
            return CompleteIndex(e, column);
        }

        if (!e.Columns.TryGetValue(column, out ColumnIndex? index))
        {
            index = new ColumnIndex();
            e.Columns.Add(column, index);
        }

        return index;
    }

    // The index of a loaded entity by one column: every value, so it answers any value.
    private static ColumnIndex CompleteIndex(EntityReads e, string column)
    {
        if (e.Columns.TryGetValue(column, out ColumnIndex? index) && index.IsComplete)
        {
            return index;
        }

        index = new ColumnIndex(complete: true);
        foreach (StoredRow row in e.All!)
        {
            if (row.Column(column) is JsonTreeNode value && value.Kind == JsonTreeKind.String)
            {
                index.Add(value.GetString(), row);
            }
        }

        e.Columns[column] = index;
        return index;
    }

    private static IReadOnlyList<StoredRow> Sorted(IEnumerable<StoredRow> rows)
    {
        var byId = new SortedDictionary<string, StoredRow>(StringComparer.Ordinal);
        foreach (StoredRow row in rows)
        {
            byId[row.Id] = row;
        }

        return byId.Count == 0 ? NoRows : new ReadOnlyCollection<StoredRow>(byId.Values.ToList());
    }

    // The query as text: each condition's column and its value as canonical JSON, in order.
    private static string MemoKey(RowQuery query)
    {
        var parts = new List<string>(query.Conditions.Count * 2);
        foreach (KeyValuePair<string, JsonTreeNode> condition in query.Conditions)
        {
            parts.Add(condition.Key);
            parts.Add(CanonicalJson.Write(condition.Value, CanonicalProfile.Request));
        }

        return string.Join("\u001f", parts);
    }

    // What the cache holds of one entity.
    private sealed class EntityReads
    {
        // Every row, by ID, once a query with no condition read them.
        public IReadOnlyList<StoredRow>? All { get; set; }

        // Rows read by ID, and IDs read that have no row (null).
        public Dictionary<string, StoredRow?> ById { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, StoredRow?> ByKey { get; } = new(StringComparer.Ordinal);

        // Rows by the value of one column, for the values read so far.
        public Dictionary<string, ColumnIndex> Columns { get; } = new(StringComparer.Ordinal);

        // Answers given, by the query's text.
        public Dictionary<string, IReadOnlyList<StoredRow>> Queries { get; } = new(StringComparer.Ordinal);
    }

    // The rows of an entity by the value of one column, for the values it covers: every row with a covered value is in it.
    private sealed class ColumnIndex
    {
        private readonly HashSet<string> _covered = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<StoredRow>> _rows = new(StringComparer.Ordinal);

        public ColumnIndex(bool complete = false)
        {
            IsComplete = complete;
        }

        public bool IsComplete { get; }

        public bool Covers(string value)
        {
            return IsComplete || _covered.Contains(value);
        }

        public void Cover(string value)
        {
            _covered.Add(value);
        }

        public void Add(string value, StoredRow row)
        {
            if (!_rows.TryGetValue(value, out List<StoredRow>? rows))
            {
                rows = new List<StoredRow>();
                _rows.Add(value, rows);
            }

            rows.Add(row);
        }

        public IEnumerable<StoredRow> Rows(string value)
        {
            return _rows.TryGetValue(value, out List<StoredRow>? rows) ? rows : Enumerable.Empty<StoredRow>();
        }
    }
}
