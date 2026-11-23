using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Product.Engine.Core.Canonical;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Policy;

namespace Product.Mcp.Testing;

// Store stand-ins for tests and the local harness. They live in the testing assembly, which no host references, so a
// production build cannot bind them (DependencyRuleTests in the MCP core tests).

// Wraps a record store whose IDs are not GUIDs (the in-memory store makes "decision-000012") so that new rows get GUID
// IDs, as Dataverse and Azure SQL give them: the MCP contract types every operational ID as a GUID (common.schema.json,
// id). The IDs are repeatable: a counter in the wrapper, so two fresh stores given the same calls make the same IDs.
// Everything else passes through. For tests and the local harness.
public sealed class GuidIdRecordStore : IRecordStore
{
    private readonly IRecordStore _inner;
    private readonly string _prefix;
    private long _next;

    // The prefix is the first 24 characters of each GUID: "00000000-0000-4000-9000-" by default, so seeded test rows
    // (another prefix) never collide with engine-made ones.
    public GuidIdRecordStore(IRecordStore inner, string prefix = "00000000-0000-4000-9000-")
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        if (prefix == null || prefix.Length != 24)
        {
            throw new ArgumentException("The prefix is the first 24 characters of a GUID, ending with a hyphen.", nameof(prefix));
        }

        _prefix = prefix;
    }

    public string StoreId => _inner.StoreId;

    public StoredRow? Get(string entity, string id)
    {
        return _inner.Get(entity, id);
    }

    public StoredRow? GetByKey(string entity, string key)
    {
        return _inner.GetByKey(entity, key);
    }

    public IReadOnlyList<StoredRow> Query(RowQuery query)
    {
        return _inner.Query(query);
    }

    public IStoreTransaction Begin()
    {
        return new Transaction(this, _inner.Begin());
    }

    private string NextId()
    {
        return _prefix + Interlocked.Increment(ref _next).ToString("x12", CultureInfo.InvariantCulture);
    }

    private sealed class Transaction : IStoreTransaction
    {
        private readonly GuidIdRecordStore _owner;
        private readonly IStoreTransaction _inner;

        public Transaction(GuidIdRecordStore owner, IStoreTransaction inner)
        {
            _owner = owner;
            _inner = inner;
        }

        public StoredRow? Get(string entity, string id)
        {
            return _inner.Get(entity, id);
        }

        public StoredRow? GetByKey(string entity, string key)
        {
            return _inner.GetByKey(entity, key);
        }

        public IReadOnlyList<StoredRow> Query(RowQuery query)
        {
            return _inner.Query(query);
        }

        public string NewId(string entity)
        {
            return _owner.NextId();
        }

        public void Create(string entity, string id, string? key, JsonTreeNode columns)
        {
            _inner.Create(entity, id, key, columns);
        }

        public void Update(string entity, string id, JsonTreeNode columns)
        {
            _inner.Update(entity, id, columns);
        }

        public void Delete(string entity, string id)
        {
            _inner.Delete(entity, id);
        }

        public void Commit()
        {
            _inner.Commit();
        }

        public void Dispose()
        {
            _inner.Dispose();
        }
    }
}

// The rate limit counters of the in-memory store, for tests and the local harness: the stand-in for the counter table of
// the real store (or a gateway's rate-limit-by-key policy). It belongs to the store, not to the server: the server core
// asks it on every call and keeps no count itself. Counters of ended windows are dropped.
public sealed class InMemoryRateLimitStore : IRateLimitStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, (DateTime WindowStart, long Count)> _counters = new(StringComparer.Ordinal);

    public long Increment(string counterKey, DateTime windowStartUtc, TimeSpan windowLength)
    {
        if (string.IsNullOrEmpty(counterKey))
        {
            throw new ArgumentException("A counter has a key.", nameof(counterKey));
        }

        lock (_gate)
        {
            if (!_counters.TryGetValue(counterKey, out (DateTime WindowStart, long Count) counter) || counter.WindowStart != windowStartUtc)
            {
                counter = (windowStartUtc, 0);
            }

            counter.Count++;
            _counters[counterKey] = counter;
            return counter.Count;
        }
    }
}
