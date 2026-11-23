using System;
using System.Data.Common;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Product.Azure.Stores;
using Product.Azure.Stores.Sqlite;
using Product.Azure.Stores.SqlServer;

namespace Product.Azure.Server;

/// <summary>
/// The local server's store, chosen by <c>Product:Store:Provider</c>: <c>sqlite</c> (the default in Development) over
/// one file, or <c>sqlserver</c> (the default elsewhere) over Azure SQL Database with the connection string
/// <c>ConnectionStrings:Product</c>, which uses a managed identity or the signed-in developer's Entra credential and
/// never a password.
/// </summary>
public static class ServerStore
{
    /// <summary>The provider name of the SQLite store.</summary>
    public const string Sqlite = "sqlite";

    /// <summary>The provider name of the Azure SQL store.</summary>
    public const string SqlServer = "sqlserver";

    /// <summary>The provider the configuration names, after defaults.</summary>
    public static string Provider(IConfiguration configuration, IHostEnvironment environment)
    {
        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        if (environment == null)
        {
            throw new ArgumentNullException(nameof(environment));
        }

        string? provider = configuration["Product:Store:Provider"];
        return string.IsNullOrWhiteSpace(provider) ? (environment.IsDevelopment() ? Sqlite : SqlServer) : provider!.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Opens the store the configuration names and makes sure its schema is ready: the SQLite store creates its tables;
    /// the Azure SQL store only checks that the migrations it needs were applied by a deployment identity.
    /// </summary>
    public static IProductStore Create(IConfiguration configuration, IHostEnvironment environment)
    {
        string provider = Provider(configuration, environment);
        IProductStore store;
        switch (provider)
        {
            case Sqlite:
                store = new SqliteProductStore(new SqliteStoreOptions { ConnectionString = SqliteConnectionString(configuration, environment) });
                break;
            case SqlServer:
                string? connection = configuration.GetConnectionString("Product");
                if (string.IsNullOrWhiteSpace(connection))
                {
                    throw new InvalidOperationException("Product:Store:Provider is sqlserver, but ConnectionStrings:Product is not set. Set it in configuration (an app setting), never in a committed file.");
                }

                CheckNoPassword(connection!, environment);
                store = new SqlServerProductStore(new SqlServerStoreOptions { ConnectionString = connection! });
                break;
            default:
                throw new InvalidOperationException("Product:Store:Provider must be sqlite or sqlserver, not " + provider + ".");
        }

        try
        {
            store.EnsureSchema();
        }
        catch
        {
            store.Dispose();
            throw;
        }

        return store;
    }

    /// <summary>
    /// Outside Development the Azure SQL connection uses a Microsoft Entra method (a managed identity, or the signed-in
    /// developer's credential), never a SQL password: a connection string that carries one stops the server, as the Azure
    /// Functions app does. In Development a local SQL Server container with a password is allowed.
    /// </summary>
    public static void CheckNoPassword(string connectionString, IHostEnvironment environment)
    {
        if (environment == null)
        {
            throw new ArgumentNullException(nameof(environment));
        }

        if (environment.IsDevelopment() || string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var builder = new DbConnectionStringBuilder();
        try
        {
            builder.ConnectionString = connectionString;
        }
        catch (ArgumentException)
        {
            // Not a well-formed connection string: the store reports it when it connects.
            return;
        }

        foreach (string key in new[] { "Password", "Pwd" })
        {
            if (builder.TryGetValue(key, out object? value) && value is string text && text.Length > 0)
            {
                throw new InvalidOperationException("ConnectionStrings:Product carries a password. Outside Development use Authentication=Active Directory Managed Identity (or Active Directory Default) instead.");
            }
        }
    }

    /// <summary>
    /// The SQLite connection string: <c>Product:Store:Sqlite:ConnectionString</c>, or a file named product.db in a data
    /// folder under the content root. The file's folder is created when it is missing.
    /// </summary>
    public static string SqliteConnectionString(IConfiguration configuration, IHostEnvironment environment)
    {
        string? configured = configuration["Product:Store:Sqlite:ConnectionString"];
        string connection = string.IsNullOrWhiteSpace(configured)
            ? "Data Source=" + Path.Combine(environment.ContentRootPath, "data", "product.db")
            : configured!;
        var builder = new SqliteConnectionStringBuilder(connection);
        if (!string.IsNullOrEmpty(builder.DataSource) && !string.Equals(builder.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            string? folder = Path.GetDirectoryName(Path.GetFullPath(builder.DataSource));
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }
        }

        return connection;
    }
}
