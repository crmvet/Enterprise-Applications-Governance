using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Product.Engine.Runtime.Ports;

namespace Product.Azure.Stores.Sqlite;

// One SQLite file: every row of every entity in one table, keyed by entity and ID, with the row's columns as canonical
// JSON, an optimistic version and the entity's unique key value; the store keys of the rows (StoreKeys.cs); plus the
// rate limit counters, the approval outbox and the schema version. A pooled connection per operation. The caller
// context is ignored (SQLite has no row-level security).
internal sealed class SqliteRowDatabase : IRowDatabase
{
    // The schema this build creates. 2 adds the approval outbox; 3 adds the store keys; 4 builds the store keys again,
    // because the data location key now counts locations in service only (a withdrawn draft no longer holds its External
    // Id; StoreKeyScope.InServiceRows). A file at an earlier version gets the new tables on first use, and its store keys
    // are built from the rows it holds.
    public const int SchemaVersion = 4;

    // SQLite result codes for a constraint failure (19) on a unique index (2067) or on the primary key (1555).
    private const int ConstraintError = 19;
    private const int UniqueError = 2067;
    private const int PrimaryKeyError = 1555;

    private static readonly string[] SchemaStatements =
    {
        "CREATE TABLE IF NOT EXISTS \"Row\" ("
            + "\"Entity\" TEXT NOT NULL, \"Id\" TEXT NOT NULL, \"Key\" TEXT NULL, \"Columns\" TEXT NOT NULL, \"Version\" INTEGER NOT NULL, "
            + "\"UniqueKey\" TEXT NULL, \"CreatedOn\" TEXT NOT NULL, \"ModifiedOn\" TEXT NOT NULL, "
            + "PRIMARY KEY (\"Entity\", \"Id\")) WITHOUT ROWID",
        "CREATE UNIQUE INDEX IF NOT EXISTS \"UX_Row_UniqueKey\" ON \"Row\" (\"Entity\", \"UniqueKey\") WHERE \"UniqueKey\" IS NOT NULL",
        "CREATE INDEX IF NOT EXISTS \"IX_Row_Key\" ON \"Row\" (\"Entity\", \"Key\")",
        "CREATE TABLE IF NOT EXISTS \"RateLimitCounter\" (\"CounterKey\" TEXT NOT NULL PRIMARY KEY, \"WindowStart\" TEXT NOT NULL, \"Count\" INTEGER NOT NULL)",
        "CREATE TABLE IF NOT EXISTS \"SchemaVersion\" (\"Version\" INTEGER NOT NULL PRIMARY KEY, \"AppliedOn\" TEXT NOT NULL)",

        // The approval outbox (sql/0005-approval-outbox.sql on Azure SQL): IDs, states and times only.
        "CREATE TABLE IF NOT EXISTS \"ApprovalOutbox\" ("
            + "\"ApprovalRecordId\" TEXT NOT NULL PRIMARY KEY, \"DecisionId\" TEXT NOT NULL, \"EnqueuedOn\" TEXT NOT NULL, "
            + "\"State\" TEXT NOT NULL CHECK (\"State\" IN ('pending', 'claimed', 'sent', 'answered', 'expired', 'cancelled', 'failed')), "
            + "\"ClaimId\" TEXT NULL, \"ClaimedUntil\" TEXT NULL, \"Attempts\" INTEGER NOT NULL, \"CardRef\" TEXT NULL, \"RunnerRunUrl\" TEXT NULL, "
            + "\"UpdatedOn\" TEXT NOT NULL, \"RowVersion\" INTEGER NOT NULL) WITHOUT ROWID",
        "CREATE INDEX IF NOT EXISTS \"IX_ApprovalOutbox_State\" ON \"ApprovalOutbox\" (\"State\", \"EnqueuedOn\")",

        // The store keys (sql/0006-store-keys.sql on Azure SQL): the SHA-256 of each key value, never the value itself.
        "CREATE TABLE IF NOT EXISTS \"RowKey\" (\"Entity\" TEXT NOT NULL, \"KeyName\" TEXT NOT NULL, \"ValueHash\" BLOB NOT NULL, \"Id\" TEXT NOT NULL, "
            + "PRIMARY KEY (\"Entity\", \"KeyName\", \"ValueHash\")) WITHOUT ROWID",
        "CREATE INDEX IF NOT EXISTS \"IX_RowKey_Row\" ON \"RowKey\" (\"Entity\", \"Id\")",

        // The record number counters (sql/0007-record-numbers.sql on Azure SQL): the last number given per entity. A file
        // of an earlier build gets the table empty on first use; the numbers its rows hold count all the same.
        "CREATE TABLE IF NOT EXISTS \"RecordNumber\" (\"Entity\" TEXT NOT NULL PRIMARY KEY, \"LastNumber\" INTEGER NOT NULL) WITHOUT ROWID",
    };

    private const string RowColumns = "\"Id\", \"Key\", \"Columns\", \"Version\"";

    private const string OutboxColumns = "\"ApprovalRecordId\", \"DecisionId\", \"EnqueuedOn\", \"State\", \"ClaimId\", \"ClaimedUntil\", \"Attempts\", \"CardRef\", \"RunnerRunUrl\", \"UpdatedOn\", \"RowVersion\"";

    private readonly string _connectionString;
    private readonly EntityCatalog _catalog;
    private readonly string? _filePath;
    private readonly bool _inMemory;
    private readonly int _busyTimeoutMilliseconds;
    private readonly object _gate = new();
    private SqliteConnection? _keepAlive;
    private volatile bool _ready;
    private bool _disposed;

    public SqliteRowDatabase(SqliteConnectionStringBuilder builder, int busyTimeoutMilliseconds, EntityCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        if (string.Equals(builder.DataSource, ":memory:", StringComparison.Ordinal))
        {
            throw new ArgumentException("A private in-memory SQLite database lives only as long as one connection. Use a file, or \"Data Source=<name>;Mode=Memory;Cache=Shared\".", nameof(builder));
        }

        _inMemory = builder.Mode == SqliteOpenMode.Memory;
        _filePath = _inMemory ? null : Path.GetFullPath(builder.DataSource);
        _busyTimeoutMilliseconds = busyTimeoutMilliseconds;
        _connectionString = builder.ToString();
    }

    public StoredRowData? Get(EntityInfo entity, string id)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = Command(connection, "SELECT " + RowColumns + " FROM \"Row\" WHERE \"Entity\" = $entity AND \"Id\" = $id");
        command.Parameters.AddWithValue("$entity", entity.Key);
        command.Parameters.AddWithValue("$id", id);
        List<StoredRowData> rows = Read(command, entity);
        return rows.Count == 0 ? null : rows[0];
    }

    public IReadOnlyList<StoredRowData> GetByKey(EntityInfo entity, string key)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = Command(connection, "SELECT " + RowColumns + " FROM \"Row\" WHERE \"Entity\" = $entity AND \"Key\" = $key ORDER BY \"Id\"");
        command.Parameters.AddWithValue("$entity", entity.Key);
        command.Parameters.AddWithValue("$key", key);
        return Read(command, entity);
    }

    public IReadOnlyList<StoredRowData> Query(EntityInfo entity, IReadOnlyList<KeyValuePair<string, string>> textConditions)
    {
        using SqliteConnection connection = Open();
        string sql = "SELECT " + RowColumns + " FROM \"Row\" WHERE \"Entity\" = $entity";
        using SqliteCommand command = connection.CreateCommand();
        command.Parameters.AddWithValue("$entity", entity.Key);
        for (int i = 0; i < textConditions.Count; i++)
        {
            // The name is a plain identifier (RowStore checks it); the value is a parameter.
            sql += " AND json_extract(\"Columns\", '$.\"" + textConditions[i].Key + "\"') = $p" + i.ToString(CultureInfo.InvariantCulture);
            command.Parameters.AddWithValue("$p" + i.ToString(CultureInfo.InvariantCulture), textConditions[i].Value);
        }

        command.CommandText = sql + " ORDER BY \"Id\"";
        return Read(command, entity);
    }

    // One read for many IDs: the IDs travel as one JSON array parameter, which json_each opens.
    public IReadOnlyList<StoredRowData> GetMany(EntityInfo entity, IReadOnlyList<string> ids)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = Command(connection, "SELECT " + RowColumns + " FROM \"Row\" WHERE \"Entity\" = $entity AND \"Id\" IN (SELECT \"value\" FROM json_each($ids)) ORDER BY \"Id\"");
        command.Parameters.AddWithValue("$entity", entity.Key);
        command.Parameters.AddWithValue("$ids", JsonArray(ids));
        return Read(command, entity);
    }

    // One read for many values of one column, as Query narrows by one value.
    public IReadOnlyList<StoredRowData> QueryIn(EntityInfo entity, string column, IReadOnlyList<string> values)
    {
        using SqliteConnection connection = Open();

        // The name is a plain identifier (RowStore checks it); the values are one parameter.
        using SqliteCommand command = Command(connection, "SELECT " + RowColumns + " FROM \"Row\" WHERE \"Entity\" = $entity "
            + "AND json_extract(\"Columns\", '$.\"" + column + "\"') IN (SELECT \"value\" FROM json_each($values)) ORDER BY \"Id\"");
        command.Parameters.AddWithValue("$entity", entity.Key);
        command.Parameters.AddWithValue("$values", JsonArray(values));
        return Read(command, entity);
    }

    public StoredRowData? ProbeInForce()
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = Command(connection, "SELECT " + RowColumns + " FROM \"Row\" WHERE \"Entity\" = 'configuration-set' AND \"Key\" IS NOT NULL "
            + "AND json_type(\"Columns\", '$.isInForce') = 'true' ORDER BY \"Id\" LIMIT 1");
        List<StoredRowData> rows = Read(command, "configuration-set");
        return rows.Count == 0 ? null : rows[0];
    }

    public IReadOnlyList<StoredRowData> GetVersionRows(string configurationSetKey, int versionNumber)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = Command(connection, "SELECT " + RowColumns + " FROM \"Row\" WHERE \"Entity\" = 'configuration-set-version' "
            + "AND json_extract(\"Columns\", '$.configurationSet') = $set AND json_extract(\"Columns\", '$.versionNumber') = $number ORDER BY \"Id\"");
        command.Parameters.AddWithValue("$set", configurationSetKey);
        command.Parameters.AddWithValue("$number", versionNumber);
        return Read(command, "configuration-set-version");
    }

    public void Apply(IReadOnlyList<RowChange> changes, StoreCallContext? context, string engineOperation, IReadOnlyList<ApprovalOutboxWrite>? outbox = null)
    {
        using SqliteConnection connection = Open();
        using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

        // The outbox writes first: a row change that fails afterwards rolls them back with the rest.
        foreach (ApprovalOutboxWrite write in outbox ?? Array.Empty<ApprovalOutboxWrite>())
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.Parameters.AddWithValue("$id", write.ApprovalRecordId);
            command.Parameters.AddWithValue("$on", ApprovalOutboxItem.FormatUtc(write.OnUtc));
            if (write.IsEnqueue)
            {
                command.CommandText = "INSERT INTO \"ApprovalOutbox\" (" + OutboxColumns + ") VALUES ($id, $decision, $on, 'pending', NULL, NULL, 0, NULL, NULL, $on, 1) "
                    + "ON CONFLICT (\"ApprovalRecordId\") DO NOTHING";
                command.Parameters.AddWithValue("$decision", write.DecisionId);
            }
            else
            {
                command.CommandText = "UPDATE \"ApprovalOutbox\" SET \"State\" = $state, \"UpdatedOn\" = $on, \"RowVersion\" = \"RowVersion\" + 1 "
                    + "WHERE \"ApprovalRecordId\" = $id AND \"State\" NOT IN ('answered', 'expired', 'cancelled')";
                command.Parameters.AddWithValue("$state", write.State);
            }

            command.ExecuteNonQuery();
        }

        foreach (RowChange change in changes)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.Parameters.AddWithValue("$entity", change.Entity.Key);
            command.Parameters.AddWithValue("$id", change.Id);
            switch (change.Kind)
            {
                case RowChangeKind.Insert:
                    command.CommandText = "INSERT INTO \"Row\" (\"Entity\", \"Id\", \"Key\", \"Columns\", \"Version\", \"UniqueKey\", \"CreatedOn\", \"ModifiedOn\") "
                        + "VALUES ($entity, $id, $key, $columns, 1, $unique, $now, $now)";
                    AddRowValues(command, change, now);
                    break;
                case RowChangeKind.Update:
                    command.CommandText = "UPDATE \"Row\" SET \"Key\" = $key, \"Columns\" = $columns, \"Version\" = \"Version\" + 1, \"UniqueKey\" = $unique, \"ModifiedOn\" = $now "
                        + "WHERE \"Entity\" = $entity AND \"Id\" = $id AND \"Version\" = $version";
                    AddRowValues(command, change, now);
                    command.Parameters.AddWithValue("$version", change.ExpectedVersion);
                    break;
                default:
                    command.CommandText = "DELETE FROM \"Row\" WHERE \"Entity\" = $entity AND \"Id\" = $id AND \"Version\" = $version";
                    command.Parameters.AddWithValue("$version", change.ExpectedVersion);
                    break;
            }

            int affected;
            try
            {
                affected = command.ExecuteNonQuery();
            }
            catch (SqliteException e) when (e.SqliteErrorCode == ConstraintError && e.SqliteExtendedErrorCode == UniqueError)
            {
                throw RowStore.UniqueKeyTaken(change.Entity, change.Columns);
            }
            catch (SqliteException e) when (e.SqliteErrorCode == ConstraintError && e.SqliteExtendedErrorCode == PrimaryKeyError)
            {
                throw RowStore.RowCreatedFirst(change.Entity, change.Id);
            }

            if (affected != 1)
            {
                throw RowStore.RowChangedFirst(change.Entity, change.Id);
            }
        }

        ApplyStoreKeys(connection, transaction, changes);
        transaction.Commit();
    }

    public long Increment(string counterKey, DateTime windowStartUtc, TimeSpan windowLength)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = Command(connection, "INSERT INTO \"RateLimitCounter\" (\"CounterKey\", \"WindowStart\", \"Count\") VALUES ($key, $start, 1) "
            + "ON CONFLICT (\"CounterKey\") DO UPDATE SET \"Count\" = CASE WHEN \"WindowStart\" = excluded.\"WindowStart\" THEN \"Count\" + 1 ELSE 1 END, "
            + "\"WindowStart\" = excluded.\"WindowStart\" RETURNING \"Count\"");
        command.Parameters.AddWithValue("$key", counterKey);
        command.Parameters.AddWithValue("$start", windowStartUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture));
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    // One immediate transaction: the counter and the highest number the entity's rows hold, read under the write lock,
    // and the counter moved to the next number before the lock is let go.
    public long NextRecordNumber(EntityInfo entity, string prefix)
    {
        using SqliteConnection connection = Open();
        using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        using SqliteCommand command = Command(connection,
            "SELECT MAX(COALESCE((SELECT \"LastNumber\" FROM \"RecordNumber\" WHERE \"Entity\" = $entity), 0), "
            + "COALESCE((SELECT MAX(CAST(substr(json_extract(\"Columns\", '$.recordNumber'), $start) AS INTEGER)) FROM \"Row\" "
            + "WHERE \"Entity\" = $entity AND json_extract(\"Columns\", '$.recordNumber') GLOB $pattern), 0)) + 1");
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$entity", entity.Key);
        command.Parameters.AddWithValue("$start", prefix.Length + 2);
        command.Parameters.AddWithValue("$pattern", prefix + "-[0-9]*");
        long next = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);

        using SqliteCommand keep = Command(connection, "INSERT INTO \"RecordNumber\" (\"Entity\", \"LastNumber\") VALUES ($entity, $next) "
            + "ON CONFLICT (\"Entity\") DO UPDATE SET \"LastNumber\" = excluded.\"LastNumber\"");
        keep.Transaction = transaction;
        keep.Parameters.AddWithValue("$entity", entity.Key);
        keep.Parameters.AddWithValue("$next", next);
        keep.ExecuteNonQuery();
        transaction.Commit();
        return next;
    }

    public ApprovalOutboxItem? OutboxGet(string approvalRecordId)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = Command(connection, "SELECT " + OutboxColumns + " FROM \"ApprovalOutbox\" WHERE \"ApprovalRecordId\" = $id");
        command.Parameters.AddWithValue("$id", approvalRecordId);
        List<ApprovalOutboxItem> items = ReadOutbox(command);
        return items.Count == 0 ? null : items[0];
    }

    public IReadOnlyList<ApprovalOutboxItem> OutboxClaimable(DateTime nowUtc, int limit)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = Command(connection, "SELECT " + OutboxColumns + " FROM \"ApprovalOutbox\" "
            + "WHERE \"State\" = 'pending' OR (\"State\" = 'claimed' AND \"ClaimedUntil\" <= $now) "
            + "ORDER BY \"EnqueuedOn\", \"ApprovalRecordId\" LIMIT $limit");
        command.Parameters.AddWithValue("$now", ApprovalOutboxItem.FormatUtc(nowUtc));
        command.Parameters.AddWithValue("$limit", limit);
        return ReadOutbox(command);
    }

    public IReadOnlyList<ApprovalOutboxItem> OutboxAll(int limit)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = Command(connection, "SELECT " + OutboxColumns + " FROM \"ApprovalOutbox\" ORDER BY \"EnqueuedOn\", \"ApprovalRecordId\" LIMIT $limit");
        command.Parameters.AddWithValue("$limit", limit);
        return ReadOutbox(command);
    }

    public bool OutboxSet(ApprovalOutboxItem next)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = Command(connection, "UPDATE \"ApprovalOutbox\" SET \"State\" = $state, \"ClaimId\" = $claim, \"ClaimedUntil\" = $until, "
            + "\"Attempts\" = $attempts, \"CardRef\" = $card, \"RunnerRunUrl\" = $run, \"UpdatedOn\" = $updated, \"RowVersion\" = $version "
            + "WHERE \"ApprovalRecordId\" = $id AND \"RowVersion\" = $expected AND \"State\" NOT IN ('answered', 'expired', 'cancelled')");
        command.Parameters.AddWithValue("$id", next.ApprovalRecordId);
        command.Parameters.AddWithValue("$state", next.State);
        command.Parameters.AddWithValue("$claim", (object?)next.ClaimId ?? DBNull.Value);
        command.Parameters.AddWithValue("$until", next.ClaimedUntilUtc.HasValue ? ApprovalOutboxItem.FormatUtc(next.ClaimedUntilUtc.Value) : (object)DBNull.Value);
        command.Parameters.AddWithValue("$attempts", next.Attempts);
        command.Parameters.AddWithValue("$card", (object?)next.CardRef ?? DBNull.Value);
        command.Parameters.AddWithValue("$run", (object?)next.RunnerRunUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("$updated", ApprovalOutboxItem.FormatUtc(next.UpdatedOnUtc));
        command.Parameters.AddWithValue("$version", next.RowVersion);
        command.Parameters.AddWithValue("$expected", next.RowVersion - 1);
        return command.ExecuteNonQuery() == 1;
    }

    public void EnsureSchema(EntityCatalog catalog)
    {
        Ready();
    }

    public bool CanConnect()
    {
        try
        {
            // No schema is made here: a probe only reads.
            using SqliteConnection connection = OpenRaw();
            using SqliteCommand command = Command(connection, "SELECT \"Version\" FROM \"SchemaVersion\" ORDER BY \"Version\" DESC LIMIT 1");
            return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) >= SchemaVersion;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _keepAlive?.Dispose();
            _keepAlive = null;
            using var connection = new SqliteConnection(_connectionString);
            SqliteConnection.ClearPool(connection);
        }
    }

    // Every removal of the commit first, then every addition (RowDatabase.cs, IRowDatabase.Apply).
    private static void ApplyStoreKeys(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<RowChange> changes)
    {
        foreach (RowChange change in changes)
        {
            StoreKeyChanges keys = change.StoreKeys;
            if (keys.RemovesAll)
            {
                using SqliteCommand command = Command(connection, "DELETE FROM \"RowKey\" WHERE \"Entity\" = $entity AND \"Id\" = $id");
                command.Transaction = transaction;
                command.Parameters.AddWithValue("$entity", change.Entity.Key);
                command.Parameters.AddWithValue("$id", change.Id);
                command.ExecuteNonQuery();
            }

            foreach (StoreKey key in keys.Removed)
            {
                using SqliteCommand command = Command(connection, "DELETE FROM \"RowKey\" WHERE \"Entity\" = $entity AND \"Id\" = $id AND \"KeyName\" = $name");
                command.Transaction = transaction;
                command.Parameters.AddWithValue("$entity", change.Entity.Key);
                command.Parameters.AddWithValue("$id", change.Id);
                command.Parameters.AddWithValue("$name", key.Name);
                command.ExecuteNonQuery();
            }
        }

        foreach (RowChange change in changes)
        {
            foreach (StoreKeyValue value in change.StoreKeys.Added)
            {
                if (!TryAddKey(connection, transaction, change.Id, value))
                {
                    throw RowStore.StoreKeyTaken(value);
                }
            }
        }
    }

    // False when the value is taken by another row.
    private static bool TryAddKey(SqliteConnection connection, SqliteTransaction transaction, string id, StoreKeyValue value)
    {
        using SqliteCommand command = Command(connection, "INSERT INTO \"RowKey\" (\"Entity\", \"KeyName\", \"ValueHash\", \"Id\") VALUES ($entity, $name, $hash, $id)");
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$entity", value.Key.EntityKey);
        command.Parameters.AddWithValue("$name", value.Key.Name);
        command.Parameters.AddWithValue("$hash", value.Hash);
        command.Parameters.AddWithValue("$id", id);
        try
        {
            command.ExecuteNonQuery();
            return true;
        }
        catch (SqliteException e) when (e.SqliteErrorCode == ConstraintError && e.SqliteExtendedErrorCode == PrimaryKeyError)
        {
            return false;
        }
    }

    // Builds the store keys of a file that an earlier build wrote, from the rows it holds, inside the schema transaction.
    // A file that holds two rows with one key value cannot be used: the message names the key, and nothing changes.
    private void RebuildStoreKeys(SqliteConnection connection, SqliteTransaction transaction)
    {
        using (SqliteCommand clear = Command(connection, "DELETE FROM \"RowKey\""))
        {
            clear.Transaction = transaction;
            clear.ExecuteNonQuery();
        }

        var rows = new List<(string Entity, string Id, string? Key, string Columns)>();
        using (SqliteCommand read = Command(connection, "SELECT \"Entity\", \"Id\", \"Key\", \"Columns\" FROM \"Row\" ORDER BY \"Entity\", \"Id\""))
        {
            read.Transaction = transaction;
            using SqliteDataReader reader = read.ExecuteReader();
            while (reader.Read())
            {
                rows.Add((reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3)));
            }
        }

        foreach ((string entity, string id, string? key, string columns) in rows)
        {
            if (!_catalog.TryGet(entity, out EntityInfo? info) || info.StoreKeys.Count == 0)
            {
                continue;
            }

            foreach (StoreKeyValue value in StoreKeyChanges.Values(info.StoreKeys, key, RowJson.Parse(columns)))
            {
                if (!TryAddKey(connection, transaction, id, value))
                {
                    throw new InvalidOperationException(
                        "The SQLite file holds two rows with one value of a key the logical model asks the store to enforce (" + RowStore.StoreKeyTaken(value).Message
                        + ") Change or remove one of them with an earlier build, or start a new file.");
                }
            }
        }
    }

    private static void AddRowValues(SqliteCommand command, RowChange change, string now)
    {
        command.Parameters.AddWithValue("$key", (object?)change.Key ?? DBNull.Value);
        command.Parameters.AddWithValue("$columns", change.ColumnsText);
        command.Parameters.AddWithValue("$unique", (object?)change.UniqueKey ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", now);
    }

    // A JSON array of strings, as one parameter.
    private static string JsonArray(IEnumerable<string> values)
    {
        return RowJson.Write(Product.Engine.Core.Canonical.JsonTreeNode.CreateArray(values.Select(Product.Engine.Core.Canonical.JsonTreeNode.FromString)));
    }

    private static SqliteCommand Command(SqliteConnection connection, string sql)
    {
        SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return command;
    }

    private static List<StoredRowData> Read(SqliteCommand command, EntityInfo entity)
    {
        return Read(command, entity.Key);
    }

    private static List<StoredRowData> Read(SqliteCommand command, string entityKey)
    {
        var rows = new List<StoredRowData>();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            string id = reader.GetString(0);
            string? key = reader.IsDBNull(1) ? null : reader.GetString(1);
            rows.Add(new StoredRowData(new StoredRow(entityKey, id, key, RowJson.Parse(reader.GetString(2))), reader.GetInt64(3)));
        }

        return rows;
    }

    private static List<ApprovalOutboxItem> ReadOutbox(SqliteCommand command)
    {
        var items = new List<ApprovalOutboxItem>();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            items.Add(new ApprovalOutboxItem(
                reader.GetString(0),
                reader.GetString(1),
                ApprovalOutboxItem.ParseUtc(reader.GetString(2)),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : ApprovalOutboxItem.ParseUtc(reader.GetString(5)),
                reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                ApprovalOutboxItem.ParseUtc(reader.GetString(9)),
                reader.GetInt64(10)));
        }

        return items;
    }

    private SqliteConnection Open()
    {
        Ready();
        return OpenRaw();
    }

    private SqliteConnection OpenRaw()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SqliteProductStore));
        }

        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using SqliteCommand pragma = Command(connection, "PRAGMA busy_timeout = " + _busyTimeoutMilliseconds.ToString(CultureInfo.InvariantCulture));
        pragma.ExecuteNonQuery();
        return connection;
    }

    // Creates the schema once per store: idempotent statements, so several processes on one file agree.
    private void Ready()
    {
        if (_ready)
        {
            return;
        }

        lock (_gate)
        {
            if (_ready)
            {
                return;
            }

            if (_filePath != null)
            {
                string? folder = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(folder))
                {
                    Directory.CreateDirectory(folder);
                }
            }

            SqliteConnection connection = OpenRaw();
            bool keep = false;
            try
            {
                if (_filePath != null)
                {
                    using SqliteCommand wal = Command(connection, "PRAGMA journal_mode = WAL");
                    wal.ExecuteScalar();
                }

                using (SqliteTransaction transaction = connection.BeginTransaction(deferred: false))
                {
                    foreach (string statement in SchemaStatements)
                    {
                        using SqliteCommand command = Command(connection, statement);
                        command.Transaction = transaction;
                        command.ExecuteNonQuery();
                    }

                    using (SqliteCommand recorded = Command(connection, "SELECT COALESCE(MAX(\"Version\"), 0) FROM \"SchemaVersion\""))
                    {
                        recorded.Transaction = transaction;
                        if (Convert.ToInt64(recorded.ExecuteScalar(), CultureInfo.InvariantCulture) < SchemaVersion)
                        {
                            RebuildStoreKeys(connection, transaction);
                        }
                    }

                    using SqliteCommand version = Command(connection, "INSERT OR IGNORE INTO \"SchemaVersion\" (\"Version\", \"AppliedOn\") VALUES ($version, $now)");
                    version.Transaction = transaction;
                    version.Parameters.AddWithValue("$version", SchemaVersion);
                    version.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
                    version.ExecuteNonQuery();
                    transaction.Commit();
                }

                if (_inMemory)
                {
                    // A shared in-memory database lives while one connection to it is open.
                    _keepAlive = connection;
                    keep = true;
                }
            }
            finally
            {
                if (!keep)
                {
                    connection.Dispose();
                }
            }

            _ready = true;
        }
    }
}
