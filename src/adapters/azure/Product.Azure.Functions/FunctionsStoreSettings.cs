using System;
using Microsoft.Extensions.Configuration;
using Product.Azure.Stores.SqlServer;

namespace Product.Azure.Functions;

// The Azure SQL store settings for the Functions app. The connection string comes from the app setting
// ConnectionStrings__Product, which the template writes with "Authentication=Active Directory Managed Identity", so the
// app connects as its own system-assigned managed identity and no password exists anywhere.
internal static class FunctionsStoreSettings
{
    public static SqlServerStoreOptions Read(IConfiguration configuration)
    {
        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        string connectionString = configuration.GetConnectionString(FunctionsHostGuard.ConnectionName) ?? string.Empty;
        return new SqlServerStoreOptions { ConnectionString = connectionString };
    }
}
