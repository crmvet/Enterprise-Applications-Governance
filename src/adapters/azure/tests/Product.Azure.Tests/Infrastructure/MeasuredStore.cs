using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Data.Sqlite;
using Product.Azure.Stores;
using Product.Azure.Stores.Sqlite;
using Product.Engine.Core.Canonical;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Json;

namespace Product.Azure.Tests;

// A SQLite store that counts its database reads: every call RowStore makes to the database to read rows is one round
// trip on Azure SQL, so the count is what a read costs over a slow path. Writes are not counted. Seeds a rows file in one
// database transaction, so a large generated estate loads in seconds.
internal sealed class MeasuredSqliteStore : RowStore
{
    private readonly CountingRowDatabase _counting;

    private MeasuredSqliteStore(RowStoreOptions options, CountingRowDatabase database)
        : base(options, database, EntityCatalog.Embedded)
    {
        _counting = database;
    }

    public override string StoreKind => "sqlite";

    // The database reads since the store opened.
    public long Reads => _counting.Reads;

    // What each read since the store opened asked for, in order: the entity and the kind of read.
    public IReadOnlyList<string> Trace => _counting.Trace;

    public static MeasuredSqliteStore Open(string file)
    {
        var builder = new SqliteConnectionStringBuilder(ProductTestHost.ConnectionString(file));
        var database = new CountingRowDatabase(new SqliteRowDatabase(builder, 5000, EntityCatalog.Embedded));
        return new MeasuredSqliteStore(
            new RowStoreOptions { StoreId = "sqlite:measured.db", NewId = RowIds.RandomGuid, NewSeedId = RowIds.EntityCounter() },
            database);
    }

    // The rows of a seed file ({ "rows": [ { "entity", "id", "key", "columns" } ] }), inserted in one transaction. Every row
    // is new and has an ID.
    public int SeedAll(JsonTreeNode rowsFile)
    {
        var changes = new List<RowChange>();
        foreach (JsonTreeNode row in Js.Items(rowsFile, "rows"))
        {
            changes.Add(RowChange.Insert(Catalog.Get(Js.Text(row, "entity")!), Js.Text(row, "id")!, Js.Text(row, "key"), Js.Member(row, "columns")!));
        }

        Database.Apply(changes, null, RowStoreOperations.Seed);
        return changes.Count;
    }
}

// The provider gateway with a read counter.
internal sealed class CountingRowDatabase : IRowDatabase
{
    private readonly IRowDatabase _inner;
    private readonly List<string> _trace = new();
    private long _reads;

    public CountingRowDatabase(IRowDatabase inner)
    {
        _inner = inner;
    }

    public long Reads => Interlocked.Read(ref _reads);

    public IReadOnlyList<string> Trace
    {
        get
        {
            lock (_trace)
            {
                return _trace.ToArray();
            }
        }
    }

    public StoredRowData? Get(EntityInfo entity, string id)
    {
        Count("get " + entity.Key);
        return _inner.Get(entity, id);
    }

    public IReadOnlyList<StoredRowData> GetByKey(EntityInfo entity, string key)
    {
        Count("key " + entity.Key);
        return _inner.GetByKey(entity, key);
    }

    public IReadOnlyList<StoredRowData> Query(EntityInfo entity, IReadOnlyList<KeyValuePair<string, string>> textConditions)
    {
        Count("query " + entity.Key + (textConditions.Count == 0 ? string.Empty : " by " + string.Join(", ", textConditions.Select(c => c.Key))));
        return _inner.Query(entity, textConditions);
    }

    public IReadOnlyList<StoredRowData> GetMany(EntityInfo entity, IReadOnlyList<string> ids)
    {
        Count("get many " + entity.Key);
        return _inner.GetMany(entity, ids);
    }

    public IReadOnlyList<StoredRowData> QueryIn(EntityInfo entity, string column, IReadOnlyList<string> values)
    {
        Count("query " + entity.Key + " by many " + column);
        return _inner.QueryIn(entity, column, values);
    }

    public StoredRowData? ProbeInForce()
    {
        Count("version probe");
        return _inner.ProbeInForce();
    }

    public IReadOnlyList<StoredRowData> GetVersionRows(string configurationSetKey, int versionNumber)
    {
        Count("version rows");
        return _inner.GetVersionRows(configurationSetKey, versionNumber);
    }

    public void Apply(IReadOnlyList<RowChange> changes, StoreCallContext? context, string engineOperation, IReadOnlyList<ApprovalOutboxWrite>? outbox = null)
    {
        _inner.Apply(changes, context, engineOperation, outbox);
    }

    public ApprovalOutboxItem? OutboxGet(string approvalRecordId)
    {
        return _inner.OutboxGet(approvalRecordId);
    }

    public IReadOnlyList<ApprovalOutboxItem> OutboxClaimable(DateTime nowUtc, int limit)
    {
        return _inner.OutboxClaimable(nowUtc, limit);
    }

    public IReadOnlyList<ApprovalOutboxItem> OutboxAll(int limit)
    {
        return _inner.OutboxAll(limit);
    }

    public bool OutboxSet(ApprovalOutboxItem next)
    {
        return _inner.OutboxSet(next);
    }

    public long Increment(string counterKey, DateTime windowStartUtc, TimeSpan windowLength)
    {
        return _inner.Increment(counterKey, windowStartUtc, windowLength);
    }

    public long NextRecordNumber(EntityInfo entity, string prefix)
    {
        return _inner.NextRecordNumber(entity, prefix);
    }

    public void EnsureSchema(EntityCatalog catalog)
    {
        _inner.EnsureSchema(catalog);
    }

    public bool CanConnect()
    {
        return _inner.CanConnect();
    }

    public void Dispose()
    {
        _inner.Dispose();
    }

    private void Count(string what)
    {
        Interlocked.Increment(ref _reads);
        lock (_trace)
        {
            _trace.Add(what);
        }
    }
}
