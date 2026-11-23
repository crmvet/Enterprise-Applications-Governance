using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.Data.SqlClient;
using Product.Engine.Runtime.Callers;
using Product.Engine.Runtime.Ports;

namespace Product.Azure.Stores.SqlServer;

// One Azure SQL database: a table per entity (sql/0001-tables.sql), plus the host tables for the rate limit counters
// (0004), the approval outbox (0005) and the store keys (0006). A pooled connection per operation, and on each
// connection the caller in SESSION_CONTEXT, read-only for the connection's life (document 22, section 11.1; a pooled
// connection is reset before reuse, which clears it). Reads and writes are parameterized; the only names in SQL text come
// from the entity catalog and from column names RowStore checked to be plain identifiers. Transient failures are retried
// as SqlTransientRetry describes.
internal sealed class SqlServerRowDatabase : IRowDatabase
{
    // SQL Server error numbers: a duplicate key in a unique index (2601) or a unique or primary key constraint (2627), and
    // a write refused by a row-level security block predicate (33504).
    private const int DuplicateKeyRow = 2601;
    private const int DuplicateKeyConstraint = 2627;
    private const int BlockPredicate = 33504;

    private const string RowColumns = "[Id], [Key], [Columns], [Version]";

    private const string OutboxColumns = "[ApprovalRecordId], [DecisionId], [EnqueuedOn], [State], [ClaimId], [ClaimedUntil], [Attempts], [CardRef], [RunnerRunUrl], [UpdatedOn], [RowVersion]";

    // A new pending row, once per Approval Record.
    private const string OutboxEnqueue =
        "INSERT INTO [host].[ApprovalOutbox] ([ApprovalRecordId], [DecisionId], [EnqueuedOn], [State], [Attempts], [UpdatedOn], [RowVersion])\n"
        + "SELECT @id, @decision, @on, 'pending', 0, @on, 1\n"
        + "WHERE NOT EXISTS (SELECT 1 FROM [host].[ApprovalOutbox] WITH (UPDLOCK, HOLDLOCK) WHERE [ApprovalRecordId] = @id);";

    // A settled row never changes again.
    private const string OutboxSettle =
        "UPDATE [host].[ApprovalOutbox] SET [State] = @state, [UpdatedOn] = @on, [RowVersion] = [RowVersion] + 1\n"
        + "WHERE [ApprovalRecordId] = @id AND [State] NOT IN ('answered', 'expired', 'cancelled');";

    // The store keys (host.StoreKey, sql/0006-store-keys.sql).
    private const string StoreKeyRemoveAll = "DELETE FROM [host].[StoreKey] WHERE [Entity] = @entity AND [RowId] = @id;";
    private const string StoreKeyRemove = "DELETE FROM [host].[StoreKey] WHERE [Entity] = @entity AND [RowId] = @id AND [KeyName] = @name;";
    private const string StoreKeyAdd = "INSERT INTO [host].[StoreKey] ([Entity], [KeyName], [ValueHash], [RowId]) VALUES (@entity, @name, @hash, @id);";

    // The longest entity key the store key table holds (varchar(64)).
    internal const int MaxEntityKeyLength = 64;

    private readonly string _connectionString;
    private readonly EntityCatalog _catalog;
    private readonly int _commandTimeoutSeconds;
    private readonly bool _setSessionContext;
    private readonly SqlTransientRetry _retry;
    private readonly ConcurrentDictionary<string, string> _statements = new(StringComparer.Ordinal);

    public SqlServerRowDatabase(string connectionString, EntityCatalog catalog, int commandTimeoutSeconds, bool setSessionContext, SqlTransientRetry? retry = null)
    {
        _connectionString = connectionString;
        _catalog = catalog;
        _commandTimeoutSeconds = commandTimeoutSeconds;
        _setSessionContext = setSessionContext;
        _retry = retry ?? new SqlTransientRetry();
    }

    public StoredRowData? Get(EntityInfo entity, string id)
    {
        return _retry.Run(() =>
        {
            using SqlConnection connection = OpenOnce(StoreCallContext.Current, RowStoreOperations.Read);
            using SqlCommand command = Command(connection, "SELECT " + RowColumns + " FROM " + entity.QualifiedTable + " WHERE [Id] = @id");
            command.Parameters.Add(IdParameter("@id", id));
            List<StoredRowData> rows = Read(command, entity.Key);
            return rows.Count == 0 ? null : rows[0];
        });
    }

    public IReadOnlyList<StoredRowData> GetByKey(EntityInfo entity, string key)
    {
        return _retry.Run(() =>
        {
            using SqlConnection connection = OpenOnce(StoreCallContext.Current, RowStoreOperations.Read);
            using SqlCommand command = Command(connection, "SELECT " + RowColumns + " FROM " + entity.QualifiedTable + " WHERE [Key] = @key ORDER BY [Id]");
            command.Parameters.Add(KeyParameter("@key", key));
            return Read(command, entity.Key);
        });
    }

    public IReadOnlyList<StoredRowData> Query(EntityInfo entity, IReadOnlyList<KeyValuePair<string, string>> textConditions)
    {
        return _retry.Run(() => QueryOnce(entity, textConditions));
    }

    private IReadOnlyList<StoredRowData> QueryOnce(EntityInfo entity, IReadOnlyList<KeyValuePair<string, string>> textConditions)
    {
        using SqlConnection connection = OpenOnce(StoreCallContext.Current, RowStoreOperations.Read);
        var sql = new StringBuilder("SELECT " + RowColumns + " FROM " + entity.QualifiedTable);
        using SqlCommand command = connection.CreateCommand();
        command.CommandTimeout = _commandTimeoutSeconds;
        for (int i = 0; i < textConditions.Count; i++)
        {
            string name = "@p" + i.ToString(CultureInfo.InvariantCulture);

            // The column name is a plain identifier (RowStore checks it). JSON_VALUE compares in the database collation,
            // which may ignore case: RowStore filters the result exactly.
            sql.Append(i == 0 ? " WHERE " : " AND ").Append("JSON_VALUE([Columns], N'$.\"").Append(textConditions[i].Key).Append("\"') = ").Append(name);
            command.Parameters.Add(new SqlParameter(name, SqlDbType.NVarChar, RowStore.MaxNarrowingLength) { Value = textConditions[i].Value });
        }

        command.CommandText = sql.Append(" ORDER BY [Id]").ToString();
        return Read(command, entity.Key);
    }

    public StoredRowData? ProbeInForce()
    {
        EntityInfo set = _catalog.Get("configuration-set");
        return _retry.Run(() =>
        {
            using SqlConnection connection = OpenOnce(StoreCallContext.Current, RowStoreOperations.Read);
            using SqlCommand command = Command(connection, "SELECT TOP (1) " + RowColumns + " FROM " + set.QualifiedTable + " WHERE [IsInForce] = 1 AND [Key] IS NOT NULL ORDER BY [Id]");
            List<StoredRowData> rows = Read(command, set.Key);
            return rows.Count == 0 ? null : rows[0];
        });
    }

    public IReadOnlyList<StoredRowData> GetVersionRows(string configurationSetKey, int versionNumber)
    {
        EntityInfo version = _catalog.Get("configuration-set-version");
        PromotedColumn setColumn = version.Promoted.Single(p => p.JsonName == "configurationSet");
        return _retry.Run(() =>
        {
            using SqlConnection connection = OpenOnce(StoreCallContext.Current, RowStoreOperations.Read);
            using SqlCommand command = Command(connection, "SELECT " + RowColumns + " FROM " + version.QualifiedTable + " WHERE [ConfigurationSetKey] = @set AND [VersionNumber] = @number ORDER BY [Id]");
            command.Parameters.Add(new SqlParameter("@set", SqlDbType.NVarChar, setColumn.MaxLength) { Value = configurationSetKey.Length <= setColumn.MaxLength ? configurationSetKey : string.Empty });
            command.Parameters.Add(new SqlParameter("@number", SqlDbType.Int) { Value = versionNumber });
            return Read(command, version.Key);
        });
    }

    // One database transaction. A transient failure before the transaction is told to commit is retried whole on a fresh
    // connection (the server rolled the transaction back); a failure during the commit is not.
    public void Apply(IReadOnlyList<RowChange> changes, StoreCallContext? context, string engineOperation, IReadOnlyList<ApprovalOutboxWrite>? outbox = null)
    {
        if (context == null && !string.Equals(engineOperation, RowStoreOperations.Seed, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("An engine write on Azure SQL runs as its caller: the host enters a StoreCallContext for the request before the engine runs, so row-level security can read it.");
        }

        bool committing = false;
        _retry.Run(
            () =>
            {
                committing = false;
                ApplyOnce(changes, context, engineOperation, outbox, () => committing = true);
            },
            _ => !committing);
    }

    private void ApplyOnce(IReadOnlyList<RowChange> changes, StoreCallContext? context, string engineOperation, IReadOnlyList<ApprovalOutboxWrite>? outbox, Action committing)
    {
        using SqlConnection connection = OpenOnce(context, engineOperation);
        using SqlTransaction transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);

        // The outbox writes first (host.ApprovalOutbox, sql/0005-approval-outbox.sql): a row change that fails afterwards
        // rolls them back with the rest.
        foreach (ApprovalOutboxWrite write in outbox ?? Array.Empty<ApprovalOutboxWrite>())
        {
            using SqlCommand command = Command(connection, write.IsEnqueue ? OutboxEnqueue : OutboxSettle);
            command.Transaction = transaction;
            command.Parameters.Add(IdParameter("@id", write.ApprovalRecordId));
            command.Parameters.Add(new SqlParameter("@on", SqlDbType.DateTime2) { Value = write.OnUtc });
            if (write.IsEnqueue)
            {
                command.Parameters.Add(new SqlParameter("@decision", SqlDbType.VarChar, RowStore.MaxIdLength) { Value = write.DecisionId });
            }
            else
            {
                command.Parameters.Add(new SqlParameter("@state", SqlDbType.VarChar, 16) { Value = write.State });
            }

            command.ExecuteNonQuery();
        }

        foreach (RowChange change in changes)
        {
            using SqlCommand command = Command(connection, Statement(change));
            command.Transaction = transaction;
            command.Parameters.Add(IdParameter("@id", change.Id));
            if (change.Kind != RowChangeKind.Insert)
            {
                command.Parameters.Add(new SqlParameter("@version", SqlDbType.BigInt) { Value = change.ExpectedVersion });
            }

            if (change.Kind != RowChangeKind.Delete)
            {
                command.Parameters.Add(KeyParameter("@key", change.Key));
                command.Parameters.Add(new SqlParameter("@columns", SqlDbType.NVarChar, -1) { Value = change.ColumnsText });
                foreach (PromotedColumn column in change.Entity.Promoted)
                {
                    command.Parameters.Add(PromotedParameter(column, column.ValueOf(change.Columns!)));
                }
            }

            int affected;
            try
            {
                affected = command.ExecuteNonQuery();
            }
            catch (SqlException e) when (e.Number == DuplicateKeyRow || e.Number == DuplicateKeyConstraint)
            {
                throw e.Message.IndexOf("'PK_", StringComparison.Ordinal) >= 0
                    ? RowStore.RowCreatedFirst(change.Entity, change.Id)
                    : RowStore.UniqueKeyTaken(change.Entity, change.Columns);
            }
            catch (SqlException e) when (e.Number == BlockPredicate)
            {
                throw new InvalidOperationException(
                    "Azure SQL refused " + change + " by a row-level security block predicate (operation " + engineOperation + ", actor kind "
                    + (context?.ActorKindWire ?? "none") + "). The engine's guard should have refused it first.",
                    e);
            }

            if (affected != 1)
            {
                throw RowStore.RowChangedFirst(change.Entity, change.Id);
            }
        }

        ApplyStoreKeys(connection, transaction, changes);
        committing();
        transaction.Commit();
    }

    // Every removal of the commit first, then every addition (RowDatabase.cs, IRowDatabase.Apply).
    private void ApplyStoreKeys(SqlConnection connection, SqlTransaction transaction, IReadOnlyList<RowChange> changes)
    {
        foreach (RowChange change in changes)
        {
            StoreKeyChanges keys = change.StoreKeys;
            if (keys.RemovesAll)
            {
                using SqlCommand command = Command(connection, StoreKeyRemoveAll);
                command.Transaction = transaction;
                command.Parameters.Add(EntityParameter(change.Entity.Key));
                command.Parameters.Add(IdParameter("@id", change.Id));
                command.ExecuteNonQuery();
            }

            foreach (StoreKey key in keys.Removed)
            {
                using SqlCommand command = Command(connection, StoreKeyRemove);
                command.Transaction = transaction;
                command.Parameters.Add(EntityParameter(change.Entity.Key));
                command.Parameters.Add(IdParameter("@id", change.Id));
                command.Parameters.Add(new SqlParameter("@name", SqlDbType.VarChar, StoreKey.MaxNameLength) { Value = key.Name });
                command.ExecuteNonQuery();
            }
        }

        foreach (RowChange change in changes)
        {
            foreach (StoreKeyValue value in change.StoreKeys.Added)
            {
                using SqlCommand command = Command(connection, StoreKeyAdd);
                command.Transaction = transaction;
                command.Parameters.Add(EntityParameter(value.Key.EntityKey));
                command.Parameters.Add(new SqlParameter("@name", SqlDbType.VarChar, StoreKey.MaxNameLength) { Value = value.Key.Name });
                command.Parameters.Add(new SqlParameter("@hash", SqlDbType.Binary, 32) { Value = value.Hash });
                command.Parameters.Add(IdParameter("@id", change.Id));
                try
                {
                    command.ExecuteNonQuery();
                }
                catch (SqlException e) when (e.Number == DuplicateKeyRow || e.Number == DuplicateKeyConstraint)
                {
                    throw RowStore.StoreKeyTaken(value);
                }
            }
        }
    }

    // One statement that commits on its own: only the connection open is retried.
    public long Increment(string counterKey, DateTime windowStartUtc, TimeSpan windowLength)
    {
        using SqlConnection connection = Open(StoreCallContext.Current, RowStoreOperations.Write);
        using SqlCommand command = Command(connection,
            "SET NOCOUNT ON; SET XACT_ABORT ON;\n"
            + "DECLARE @count bigint;\n"
            + "BEGIN TRANSACTION;\n"
            + "UPDATE [host].[RateLimitCounter] WITH (UPDLOCK, HOLDLOCK)\n"
            + "SET @count = [Count] = CASE WHEN [WindowStart] = @start THEN [Count] + 1 ELSE 1 END, [WindowStart] = @start\n"
            + "WHERE [CounterKey] = @key;\n"
            + "IF @@ROWCOUNT = 0\n"
            + "BEGIN\n"
            + "    INSERT INTO [host].[RateLimitCounter] ([CounterKey], [WindowStart], [Count]) VALUES (@key, @start, 1);\n"
            + "    SET @count = 1;\n"
            + "END;\n"
            + "COMMIT TRANSACTION;\n"
            + "SELECT @count;");
        command.Parameters.Add(new SqlParameter("@key", SqlDbType.NVarChar, 400) { Value = counterKey });
        command.Parameters.Add(new SqlParameter("@start", SqlDbType.DateTime2) { Value = windowStartUtc });
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public ApprovalOutboxItem? OutboxGet(string approvalRecordId)
    {
        return _retry.Run(() =>
        {
            using SqlConnection connection = OpenOnce(StoreCallContext.Current, RowStoreOperations.Read);
            using SqlCommand command = Command(connection, "SELECT " + OutboxColumns + " FROM [host].[ApprovalOutbox] WHERE [ApprovalRecordId] = @id;");
            command.Parameters.Add(IdParameter("@id", approvalRecordId));
            List<ApprovalOutboxItem> items = ReadOutbox(command);
            return items.Count == 0 ? null : items[0];
        });
    }

    public IReadOnlyList<ApprovalOutboxItem> OutboxClaimable(DateTime nowUtc, int limit)
    {
        return _retry.Run(() =>
        {
            using SqlConnection connection = OpenOnce(StoreCallContext.Current, RowStoreOperations.Read);
            using SqlCommand command = Command(connection, "SELECT TOP (@limit) " + OutboxColumns + " FROM [host].[ApprovalOutbox] "
                + "WHERE [State] = 'pending' OR ([State] = 'claimed' AND [ClaimedUntil] <= @now) ORDER BY [EnqueuedOn], [ApprovalRecordId];");
            command.Parameters.Add(new SqlParameter("@limit", SqlDbType.Int) { Value = limit });
            command.Parameters.Add(new SqlParameter("@now", SqlDbType.DateTime2) { Value = nowUtc });
            return ReadOutbox(command);
        });
    }

    public IReadOnlyList<ApprovalOutboxItem> OutboxAll(int limit)
    {
        return _retry.Run(() =>
        {
            using SqlConnection connection = OpenOnce(StoreCallContext.Current, RowStoreOperations.Read);
            using SqlCommand command = Command(connection, "SELECT TOP (@limit) " + OutboxColumns + " FROM [host].[ApprovalOutbox] ORDER BY [EnqueuedOn], [ApprovalRecordId];");
            command.Parameters.Add(new SqlParameter("@limit", SqlDbType.Int) { Value = limit });
            return ReadOutbox(command);
        });
    }

    // A compare and set in one statement: only the connection open is retried, so a set that may have happened is never
    // made twice.
    public bool OutboxSet(ApprovalOutboxItem next)
    {
        using SqlConnection connection = Open(StoreCallContext.Current, RowStoreOperations.Write);
        using SqlCommand command = Command(connection, "UPDATE [host].[ApprovalOutbox] SET [State] = @state, [ClaimId] = @claim, [ClaimedUntil] = @until, [Attempts] = @attempts, "
            + "[CardRef] = @card, [RunnerRunUrl] = @run, [UpdatedOn] = @updated, [RowVersion] = @version WHERE [ApprovalRecordId] = @id AND [RowVersion] = @expected "
            + "AND [State] NOT IN ('answered', 'expired', 'cancelled');");
        command.Parameters.Add(IdParameter("@id", next.ApprovalRecordId));
        command.Parameters.Add(new SqlParameter("@state", SqlDbType.VarChar, 16) { Value = next.State });
        command.Parameters.Add(new SqlParameter("@claim", SqlDbType.VarChar, 64) { Value = (object?)next.ClaimId ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@until", SqlDbType.DateTime2) { Value = next.ClaimedUntilUtc.HasValue ? next.ClaimedUntilUtc.Value : (object)DBNull.Value });
        command.Parameters.Add(new SqlParameter("@attempts", SqlDbType.Int) { Value = next.Attempts });
        command.Parameters.Add(new SqlParameter("@card", SqlDbType.NVarChar, 400) { Value = (object?)next.CardRef ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@run", SqlDbType.NVarChar, 2048) { Value = (object?)next.RunnerRunUrl ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@updated", SqlDbType.DateTime2) { Value = next.UpdatedOnUtc });
        command.Parameters.Add(new SqlParameter("@version", SqlDbType.BigInt) { Value = next.RowVersion });
        command.Parameters.Add(new SqlParameter("@expected", SqlDbType.BigInt) { Value = next.RowVersion - 1 });
        return command.ExecuteNonQuery() == 1;
    }

    public void EnsureSchema(EntityCatalog catalog)
    {
        int applied = _retry.Run(() => SqlServerMigrations.AppliedVersion(_connectionString));
        if (applied < SqlServerMigrations.RequiredVersion)
        {
            throw new InvalidOperationException(
                "The Azure SQL schema is at version " + applied + " and this build needs version " + SqlServerMigrations.RequiredVersion
                + ". A deployment identity applies the migrations (the local server's migrate command); the app identity never runs DDL.");
        }
    }

    public bool CanConnect()
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            connection.Open();
            using SqlCommand command = Command(connection, "SELECT 1");
            return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
    }

    // The session context keys of document 22, section 11.1, read-only for the connection's life. Without a caller
    // context only a seed sets keys (as the deployment identity); every other connection sets none, and the row-level
    // security filter then hides every product row. A seed is always setup work, whoever runs it.
    internal static IReadOnlyList<KeyValuePair<string, object>> SessionValues(StoreCallContext? context, string engineOperation)
    {
        var values = new List<KeyValuePair<string, object>>();
        bool seed = string.Equals(engineOperation, RowStoreOperations.Seed, StringComparison.Ordinal);
        if (context == null && !seed)
        {
            return values;
        }

        void Add(string key, object? value)
        {
            if (value != null && !(value is string text && text.Length == 0))
            {
                values.Add(new KeyValuePair<string, object>(key, value));
            }
        }

        Add("PrincipalId", context?.PrincipalId ?? "seed");
        Add("Entry", seed ? EntryWire(null) : EntryWire(context?.Entry));
        Add("ActorKind", context?.ActorKindWire);
        Add("OnBehalfOfId", context?.OnBehalfOfId);
        Add("ClientApp", context?.ClientApp);
        Add("ClientAppVerified", context == null ? null : context.ClientAppVerified);
        Add("Roles", context == null || context.RoleKeys.Count == 0 ? null : string.Join(",", context.RoleKeys));
        Add("CorrelationId", context?.CorrelationId);
        Add("EngineOperation", engineOperation);
        return values;
    }

    internal static string EntryWire(CallerEntry? entry)
    {
        switch (entry)
        {
            case CallerEntry.Person:
                return "person";
            case CallerEntry.AiClient:
                return "ai-client";
            case CallerEntry.Workload:
                return "workload";
            default:
                return "setup";
        }
    }

    private static SqlParameter IdParameter(string name, string id)
    {
        return new SqlParameter(name, SqlDbType.VarChar, RowStore.MaxIdLength) { Value = id };
    }

    private static SqlParameter EntityParameter(string entityKey)
    {
        return new SqlParameter("@entity", SqlDbType.VarChar, MaxEntityKeyLength) { Value = entityKey };
    }

    private static SqlParameter KeyParameter(string name, string? key)
    {
        return new SqlParameter(name, SqlDbType.NVarChar, RowStore.MaxKeyLength) { Value = (object?)key ?? DBNull.Value };
    }

    private static SqlParameter PromotedParameter(PromotedColumn column, object? value)
    {
        string name = "@" + column.SqlName;
        switch (column.Kind)
        {
            case PromotedKind.Flag:
                return new SqlParameter(name, SqlDbType.Bit) { Value = value ?? DBNull.Value };
            case PromotedKind.Number:
                return new SqlParameter(name, SqlDbType.Int) { Value = value ?? DBNull.Value };
            default:
                return new SqlParameter(name, SqlDbType.NVarChar, column.MaxLength) { Value = value ?? DBNull.Value };
        }
    }

    private static List<ApprovalOutboxItem> ReadOutbox(SqlCommand command)
    {
        var items = new List<ApprovalOutboxItem>();
        using SqlDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            items.Add(new ApprovalOutboxItem(
                reader.GetString(0),
                reader.GetString(1),
                DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc),
                reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                DateTime.SpecifyKind(reader.GetDateTime(9), DateTimeKind.Utc),
                reader.GetInt64(10)));
        }

        return items;
    }

    private static List<StoredRowData> Read(SqlCommand command, string entityKey)
    {
        var rows = new List<StoredRowData>();
        using SqlDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            string id = reader.GetString(0);
            string? key = reader.IsDBNull(1) ? null : reader.GetString(1);
            rows.Add(new StoredRowData(new StoredRow(entityKey, id, key, RowJson.Parse(reader.GetString(2))), reader.GetInt64(3)));
        }

        return rows;
    }

    // The INSERT, UPDATE or DELETE of a change, cached per entity.
    private string Statement(RowChange change)
    {
        EntityInfo e = change.Entity;
        return _statements.GetOrAdd(change.Kind + " " + e.Key, _ =>
        {
            string promotedNames = string.Concat(e.Promoted.Select(p => ", [" + p.SqlName + "]"));
            string promotedValues = string.Concat(e.Promoted.Select(p => ", @" + p.SqlName));
            string promotedSets = string.Concat(e.Promoted.Select(p => ", [" + p.SqlName + "] = @" + p.SqlName));
            switch (change.Kind)
            {
                case RowChangeKind.Insert:
                    return "INSERT INTO " + e.QualifiedTable + " ([Id], [Key], [Columns], [Version]" + promotedNames + ") VALUES (@id, @key, @columns, 1" + promotedValues + ");";
                case RowChangeKind.Update:
                    return "UPDATE " + e.QualifiedTable + " SET [Key] = @key, [Columns] = @columns, [Version] = [Version] + 1, [ModifiedOn] = SYSUTCDATETIME()" + promotedSets
                        + " WHERE [Id] = @id AND [Version] = @version;";
                default:
                    return "DELETE FROM " + e.QualifiedTable + " WHERE [Id] = @id AND [Version] = @version;";
            }
        });
    }

    private SqlCommand Command(SqlConnection connection, string sql)
    {
        SqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = _commandTimeoutSeconds;
        return command;
    }

    // A connection with its session context, retried while the failure is transient.
    private SqlConnection Open(StoreCallContext? context, string engineOperation)
    {
        return _retry.Run(() => OpenOnce(context, engineOperation));
    }

    private SqlConnection OpenOnce(StoreCallContext? context, string engineOperation)
    {
        var connection = new SqlConnection(_connectionString);
        try
        {
            connection.Open();
            if (_setSessionContext)
            {
                IReadOnlyList<KeyValuePair<string, object>> values = SessionValues(context, engineOperation);
                if (values.Count > 0)
                {
                    var sql = new StringBuilder();
                    using SqlCommand command = Command(connection, string.Empty);
                    for (int i = 0; i < values.Count; i++)
                    {
                        string name = "@v" + i.ToString(CultureInfo.InvariantCulture);
                        sql.Append("EXEC sys.sp_set_session_context @key = N'").Append(values[i].Key).Append("', @value = ").Append(name).Append(", @read_only = 1;\n");
                        command.Parameters.Add(values[i].Value is bool flag
                            ? new SqlParameter(name, SqlDbType.Bit) { Value = flag }
                            : new SqlParameter(name, SqlDbType.NVarChar, 4000) { Value = values[i].Value });
                    }

                    command.CommandText = sql.ToString();
                    command.ExecuteNonQuery();
                }
            }

            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }
}
