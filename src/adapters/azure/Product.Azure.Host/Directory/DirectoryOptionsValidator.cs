using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Product.Azure.Host;

/// <summary>
/// Startup checks of the directory reads. There is never a client secret: outside Development, any configured client
/// secret fails startup, wherever it sits in the configuration. Recorded mode runs only in Development. Live mode needs
/// Microsoft Entra sign-in and a secretless credential (a managed identity, or a signed assertion file).
/// </summary>
public sealed class DirectoryOptionsValidator : IValidateOptions<DirectoryOptions>
{
    private static readonly Regex EstateKey = new("^[a-z][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant);

    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;

    /// <summary>Creates the validator.</summary>
    public DirectoryOptionsValidator(IHostEnvironment environment, IConfiguration configuration)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, DirectoryOptions options)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var failures = new List<string>();
        bool development = _environment.IsDevelopment();

        if (!development)
        {
            foreach (string key in SecretKeys(_configuration))
            {
                failures.Add("A client secret is configured at " + key + ". The product never uses a client secret: remove it and use the managed identity with a federated identity credential (Product:Directory:Credential).");
            }
        }

        if (options.Mode == DirectoryReadMode.Demo && !development && !ProductDemo.IsDemo(_environment))
        {
            failures.Add("Product:Directory:Mode is Demo, which reads the fictional demo personas and runs only in the Development or the Demo environment. Use Live or Off.");
        }

        if (!string.IsNullOrWhiteSpace(options.DemoInventoryFile) && !development && !ProductDemo.IsDemo(_environment))
        {
            failures.Add("Product:Directory:DemoInventoryFile names a fictional demo inventory, which is read only in the Development or the Demo environment. Remove it.");
        }

        if (options.Mode == DirectoryReadMode.Recorded && !development)
        {
            failures.Add("Product:Directory:Mode is Recorded, which serves fictional recorded responses and runs only in the Development environment. Use Live or Off.");
        }

        if (options.Mode == DirectoryReadMode.Recorded && string.IsNullOrWhiteSpace(options.RecordedPath))
        {
            failures.Add("Product:Directory:RecordedPath names no folder of recorded responses.");
        }

        if (options.Mode == DirectoryReadMode.Live)
        {
            if (string.IsNullOrWhiteSpace(_configuration["Product:Auth:TenantId"]) || string.IsNullOrWhiteSpace(_configuration["Product:Auth:ClientId"]))
            {
                failures.Add("Live directory reads exchange the signed-in person's Microsoft Entra token, so Product:Auth:TenantId and Product:Auth:ClientId are required.");
            }

            DirectoryCredentialOptions credential = options.Credential ?? new DirectoryCredentialOptions();
            if (string.Equals(credential.SourceType, DirectoryCredentialOptions.ManagedIdentity, StringComparison.OrdinalIgnoreCase))
            {
                bool named = !string.IsNullOrWhiteSpace(credential.ManagedIdentityClientId);
                if ((named || !development) && !Guid.TryParse(credential.ManagedIdentityClientId, out _))
                {
                    failures.Add("Product:Directory:Credential:ManagedIdentityClientId must be the client ID (a GUID) of the user-assigned managed identity that the API app registration's federated identity credential trusts.");
                }
            }
            else if (string.Equals(credential.SourceType, DirectoryCredentialOptions.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(credential.SignedAssertionFilePath))
                {
                    failures.Add("Product:Directory:Credential:SignedAssertionFilePath names no file.");
                }
            }
            else
            {
                failures.Add("Product:Directory:Credential:SourceType must be " + DirectoryCredentialOptions.ManagedIdentity + " or " + DirectoryCredentialOptions.FilePath + ". A client secret or any other credential is not accepted.");
            }
        }

        if (options.CallTimeoutSeconds < 1 || options.CallTimeoutSeconds > 30)
        {
            failures.Add("Product:Directory:CallTimeoutSeconds must be between 1 and 30.");
        }

        if (options.ToolBudgetSeconds < options.CallTimeoutSeconds || options.ToolBudgetSeconds > 120)
        {
            failures.Add("Product:Directory:ToolBudgetSeconds must be at least CallTimeoutSeconds and at most 120.");
        }

        if (options.MaxPages < 1 || options.MaxPages > 100)
        {
            failures.Add("Product:Directory:MaxPages must be between 1 and 100.");
        }

        if (options.MaxPrincipalLookups < 0 || options.MaxPrincipalLookups > 200)
        {
            failures.Add("Product:Directory:MaxPrincipalLookups must be between 0 and 200.");
        }

        if (options.InventoryPageSize < 1 || options.InventoryPageSize > 1000)
        {
            failures.Add("Product:Directory:InventoryPageSize must be between 1 and 1000.");
        }

        foreach (string estate in options.EffectiveInventoryEstateKeys.Where(k => !EstateKey.IsMatch(k)))
        {
            failures.Add("Product:Directory:InventoryEstateKeys holds " + estate + ", which is not a configuration key.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    // Every configuration key that holds a client secret: any key named ClientSecret (the Microsoft.Identity.Web and MSAL
    // spelling, in any section), and any ClientCredentials entry whose SourceType is ClientSecret. Only key names are
    // reported, never values.
    internal static IEnumerable<string> SecretKeys(IConfiguration configuration)
    {
        var found = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string?> entry in configuration.AsEnumerable())
        {
            if (string.IsNullOrWhiteSpace(entry.Value))
            {
                continue;
            }

            string last = entry.Key.Substring(entry.Key.LastIndexOf(':') + 1);
            if (string.Equals(last, "ClientSecret", StringComparison.OrdinalIgnoreCase))
            {
                found.Add(entry.Key);
            }
            else if (string.Equals(last, "SourceType", StringComparison.OrdinalIgnoreCase)
                && string.Equals(entry.Value!.Trim(), "ClientSecret", StringComparison.OrdinalIgnoreCase))
            {
                found.Add(entry.Key);
            }
        }

        return found;
    }
}
