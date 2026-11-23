using System;
using System.Data.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Product.Azure.Functions;

// Startup checks for the Functions app. Each one fails startup with a plain message instead of letting the app run
// in a shape the template never deploys.
internal static class FunctionsHostGuard
{
    internal const string ConnectionName = "Product";

    public static void Validate(IHostEnvironment environment, IConfiguration configuration)
    {
        if (environment == null)
        {
            throw new ArgumentNullException(nameof(environment));
        }

        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        // The development identity exists only in the local server. A switch here would do nothing, so it is refused
        // rather than ignored.
        if (configuration.GetValue<bool>("Product:DevIdentity:Enabled"))
        {
            throw new InvalidOperationException("Product:DevIdentity:Enabled is set, but the Azure Functions app has no development identity. Remove the setting; sign-in here is Microsoft Entra ID only.");
        }

        // The Functions app always uses Azure SQL. The SQLite store is for local runs and tests.
        string? provider = configuration["Product:Store:Provider"];
        if (!string.IsNullOrEmpty(provider) && !string.Equals(provider, "sqlserver", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Product:Store:Provider is '" + provider + "', but the Azure Functions app uses Azure SQL only. Remove the setting.");
        }

        string connectionString = configuration.GetConnectionString(ConnectionName) ?? string.Empty;
        if (connectionString.Trim().Length == 0)
        {
            throw new InvalidOperationException("The connection string 'Product' is not set. Set the app setting ConnectionStrings__Product to the Azure SQL connection string that uses the app's managed identity.");
        }

        // No secrets in settings: outside Development the connection must use a Microsoft Entra method, never a SQL
        // password.
        if (!environment.IsDevelopment() && CarriesPassword(connectionString))
        {
            throw new InvalidOperationException("The connection string 'Product' carries a password. Use Authentication=Active Directory Managed Identity with the app's managed identity instead.");
        }
    }

    internal static bool CarriesPassword(string connectionString)
    {
        var builder = new DbConnectionStringBuilder();
        try
        {
            builder.ConnectionString = connectionString;
        }
        catch (ArgumentException)
        {
            // Not a well-formed connection string: let the store report it when it connects.
            return false;
        }

        foreach (string key in new[] { "Password", "Pwd" })
        {
            if (builder.TryGetValue(key, out object? value) && value is string text && text.Length > 0)
            {
                return true;
            }
        }

        return false;
    }
}
