using System;
using Microsoft.Data.SqlClient;

namespace Product.Azure.Stores.SqlServer;

// Settings of the Azure SQL store.
public sealed class SqlServerStoreOptions
{
    // Required, from configuration (never a file in the repository, never a password). In Azure, the app's user-assigned
    // managed identity:
    //   Server=tcp:<server>.database.windows.net,1433;Database=<db>;Authentication=Active Directory Managed Identity;
    //   User Id=<identity client ID>;Encrypt=True
    // For a deployment identity running the migrations: Authentication=Active Directory Default.
    public string ConnectionString { get; set; } = string.Empty;

    // The store's identity in the snapshot cache key. Default: "azuresql:" and the server host and database name.
    public string? StoreId { get; set; }

    // Defaults: random lowercase GUIDs (RowIds.RandomGuid).
    public Func<string, string>? NewId { get; set; }

    public Func<string, string>? NewSeedId { get; set; }

    public int CommandTimeoutSeconds { get; set; } = 30;

    // Retries of transient Azure SQL failures (SqlTransientRetry): how many, and the wait before the first. Microsoft's
    // guidance for Azure SQL Database: at least 5 seconds, then twice as long each time, up to 60 seconds. 0 retries
    // turns them off.
    public int TransientRetries { get; set; } = SqlTransientRetry.DefaultRetries;

    public TimeSpan TransientRetryFirstDelay { get; set; } = SqlTransientRetry.DefaultFirstDelay;

    // Writes the caller to SESSION_CONTEXT on every connection (document 22, section 11.1). Row-level security reads it,
    // so a store without it sees no product row. Off only for diagnostics.
    public bool SetSessionContext { get; set; } = true;
}

// The engine's storage ports over Azure SQL Database: one table per first-release entity (sql/0001-tables.sql), the
// caller in SESSION_CONTEXT on every connection for row-level security (sql/0002-security.sql), the app's managed
// identity as a least-privilege database user (sql/0003-app-user.sql). The app identity never runs DDL: EnsureSchema
// only checks that a deployment identity has applied the migrations this build needs (SqlServerMigrations).
public sealed class SqlServerProductStore : RowStore
{
    public override string StoreKind => "azure-sql";

    public override int? RequiredSchemaVersion => SqlServerMigrations.RequiredVersion;

    public SqlServerProductStore(SqlServerStoreOptions options)
        : this(options ?? throw new ArgumentNullException(nameof(options)), Check(options), EntityCatalog.Embedded)
    {
    }

    private SqlServerProductStore(SqlServerStoreOptions options, SqlConnectionStringBuilder builder, EntityCatalog catalog)
        : base(
            new RowStoreOptions
            {
                StoreId = string.IsNullOrEmpty(options.StoreId) ? DefaultStoreId(builder) : options.StoreId!,
                NewId = options.NewId ?? RowIds.RandomGuid,
                NewSeedId = options.NewSeedId ?? RowIds.RandomGuid,
            },
            new SqlServerRowDatabase(options.ConnectionString, catalog, options.CommandTimeoutSeconds, options.SetSessionContext, new SqlTransientRetry(options.TransientRetries, options.TransientRetryFirstDelay)),
            catalog)
    {
    }

    private static SqlConnectionStringBuilder Check(SqlServerStoreOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new ArgumentException("The Azure SQL store needs a connection string (ConnectionStrings:Product).", nameof(options));
        }

        if (options.CommandTimeoutSeconds < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "The command timeout is at least one second.");
        }

        if (options.TransientRetries < 0 || options.TransientRetries > SqlTransientRetry.MaxRetries)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Transient retries are 0 to " + SqlTransientRetry.MaxRetries + ".");
        }

        if (options.TransientRetryFirstDelay < TimeSpan.Zero || options.TransientRetryFirstDelay > SqlTransientRetry.MaxDelay)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "The first transient retry delay is 0 to 60 seconds.");
        }

        var builder = new SqlConnectionStringBuilder(options.ConnectionString);
        if (string.IsNullOrEmpty(builder.DataSource) || string.IsNullOrEmpty(builder.InitialCatalog))
        {
            throw new ArgumentException("The Azure SQL connection string names a server and a database.", nameof(options));
        }

        return builder;
    }

    // "tcp:contoso.database.windows.net,1433" and "product" give "azuresql:contoso.database.windows.net/product".
    private static string DefaultStoreId(SqlConnectionStringBuilder builder)
    {
        string host = builder.DataSource;
        if (host.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
        {
            host = host.Substring(4);
        }

        int comma = host.IndexOf(',');
        if (comma >= 0)
        {
            host = host.Substring(0, comma);
        }

        return "azuresql:" + host.Trim().ToLowerInvariant() + "/" + builder.InitialCatalog;
    }
}
