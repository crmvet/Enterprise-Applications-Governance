using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using Product.Engine.Runtime.Ports;

namespace Product.Azure.Stores.Sqlite;

// One SQLite file: every row of every entity in one table, keyed by entity and ID, with the row's columns as canonical
// JSON, an optimistic version and the entity's unique key value; plus the rate limit counters and the schema version.
// A pooled connection per operation. The caller context is ignored (SQLite has no row-level security).
internal sealed class SqliteRowDatabase : IRowDatabase
{
    // The schema this build creates.
    public const int SchemaVersion = 1;

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
    };

    private const string RowColumns = "\"Id\", \"Key\", \"Columns\", \"Version\"";

    private readonly string _connectionString;
    private readonly string? _filePath;
    private readonly bool _inMemory;
    private readonly int _busyTimeoutMilliseconds;
    private readonly object _gate = new();
    private SqliteConnection? _keepAlive;
    private volatile bool _ready;
    private bool _disposed;

    public SqliteRowDatabase(SqliteConnectionStringBuilder builder, int busyTimeoutMilliseconds)
    {
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

    public void Apply(IReadOnlyList<RowChange> changes, StoreCallContext? context, string engineOperation)
    {
        using SqliteConnection connection = Open();
        using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
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

    private static void AddRowValues(SqliteCommand command, RowChange change, string now)
    {
        command.Parameters.AddWithValue("$key", (object?)change.Key ?? DBNull.Value);
        command.Parameters.AddWithValue("$columns", change.ColumnsText);
        command.Parameters.AddWithValue("$unique", (object?)change.UniqueKey ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", now);
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
