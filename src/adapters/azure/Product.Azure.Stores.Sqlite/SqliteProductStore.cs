using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace Product.Azure.Stores.Sqlite;

// Settings of the SQLite store.
public sealed class SqliteStoreOptions
{
    // Required, such as "Data Source=data/product.db". "Mode=Memory;Cache=Shared" with a name keeps the rows in memory
    // for as long as the store lives.
    public string ConnectionString { get; set; } = string.Empty;

    // The store's identity in the snapshot cache key. Default: "sqlite:" and the file name without its folder, so no local
    // path reaches the cache key or telemetry.
    public string? StoreId { get; set; }

    // Defaults: random lowercase GUIDs (RowIds.RandomGuid).
    public Func<string, string>? NewId { get; set; }

    public Func<string, string>? NewSeedId { get; set; }

    // How long a write waits for another process's write to finish.
    public int BusyTimeoutMilliseconds { get; set; } = 5000;
}

// The engine's storage ports over one SQLite file, for local runs, demos and tests. Same behavior as the Azure SQL
// store through the shared base (RowStore). No ledger and no row-level security: the caller context is ignored.
// The schema is created on first use, or by EnsureSchema.
public sealed class SqliteProductStore : RowStore
{
    public override string StoreKind => "sqlite";

    public SqliteProductStore(SqliteStoreOptions options)
        : this(options ?? throw new ArgumentNullException(nameof(options)), Check(options))
    {
    }

    private SqliteProductStore(SqliteStoreOptions options, SqliteConnectionStringBuilder builder)
        : base(
            new RowStoreOptions
            {
                StoreId = string.IsNullOrEmpty(options.StoreId) ? DefaultStoreId(builder) : options.StoreId!,
                NewId = options.NewId ?? RowIds.RandomGuid,
                NewSeedId = options.NewSeedId ?? RowIds.RandomGuid,
            },
            new SqliteRowDatabase(builder, options.BusyTimeoutMilliseconds, EntityCatalog.Embedded),
            EntityCatalog.Embedded)
    {
    }

    private static SqliteConnectionStringBuilder Check(SqliteStoreOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new ArgumentException("The SQLite store needs a connection string, such as \"Data Source=data/product.db\".", nameof(options));
        }

        if (options.BusyTimeoutMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "The busy timeout is zero or more milliseconds.");
        }

        var builder = new SqliteConnectionStringBuilder(options.ConnectionString);
        if (string.IsNullOrEmpty(builder.DataSource))
        {
            throw new ArgumentException("The SQLite connection string names no Data Source.", nameof(options));
        }

        return builder;
    }

    private static string DefaultStoreId(SqliteConnectionStringBuilder builder)
    {
        string name = builder.Mode == SqliteOpenMode.Memory ? builder.DataSource : Path.GetFileName(builder.DataSource);
        return "sqlite:" + (string.IsNullOrEmpty(name) ? "product" : name);
    }
}
