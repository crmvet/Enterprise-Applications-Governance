using System;
using System.Data.Common;
using Azure.Core;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Product.Azure.Functions;

// OpenTelemetry from the worker to Application Insights, only when the app setting APPLICATIONINSIGHTS_CONNECTION_STRING
// is present. host.json sets telemetryMode to OpenTelemetry for the Functions host itself.
//
// The template turns off local (key based) authentication on Application Insights, so ingestion needs a Microsoft
// Entra token. The host reads APPLICATIONINSIGHTS_AUTHENTICATION_STRING on its own; the worker's exporter needs a
// credential in code, which is the app's managed identity named by that same setting.
internal static class FunctionsTelemetry
{
    public static void Configure(IServiceCollection services, IConfiguration configuration)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        string? connectionString = configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        TokenCredential? credential = CredentialFor(configuration["APPLICATIONINSIGHTS_AUTHENTICATION_STRING"]);

        services.AddOpenTelemetry()
            .UseFunctionsWorkerDefaults()
            .UseAzureMonitorExporter(options =>
            {
                options.ConnectionString = connectionString;
                if (credential != null)
                {
                    options.Credential = credential;
                }
            });
    }

    // "Authorization=AAD" means the system-assigned identity; "Authorization=AAD;ClientId=<id>" a user-assigned one.
    // Anything else (or nothing) means key based ingestion, so no credential.
    internal static TokenCredential? CredentialFor(string? authenticationString)
    {
        if (string.IsNullOrWhiteSpace(authenticationString))
        {
            return null;
        }

        var parts = new DbConnectionStringBuilder();
        try
        {
            parts.ConnectionString = authenticationString;
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (!parts.TryGetValue("Authorization", out object? authorization) || !string.Equals(authorization as string, "AAD", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (parts.TryGetValue("ClientId", out object? clientId) && clientId is string id && id.Length > 0)
        {
            return new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(id));
        }

        return new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned);
    }
}
