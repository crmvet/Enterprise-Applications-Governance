using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Abstractions;
using Microsoft.Identity.Web;
using Product.Azure.Host;
using Product.Mcp.Core.Backend;
using Xunit;

namespace Product.Azure.Tests;

// The startup rules of the directory reads: never a client secret outside Development, the recorded mode only in
// Development, a secretless credential and Entra sign-in for live reads. Then what each mode binds: Off binds no reader
// (the four tools answer as before), Live binds the readers and the on-behalf-of exchange on the Entra scheme, Recorded
// serves the recordings to the development identity with no exchange and no network.
public sealed class DirectoryOptionsTests
{
    [Fact]
    public void AClientSecretAnywhereInTheConfigurationFailsStartupOutsideDevelopment()
    {
        Exception refused = StartFails(Production(("Product:Auth:ClientSecret", "fictional-value")));

        Assert.Contains("A client secret is configured at Product:Auth:ClientSecret", Message(refused), StringComparison.Ordinal);
        Assert.DoesNotContain("fictional-value", Message(refused), StringComparison.Ordinal);
    }

    [Fact]
    public void AClientSecretCredentialFailsStartupOutsideDevelopment()
    {
        Exception refused = StartFails(Production(("Product:Directory:Mode", "Live"), ("Product:Directory:Credential:SourceType", "ClientSecret")));

        Assert.Contains("A client secret is configured at Product:Directory:Credential:SourceType", Message(refused), StringComparison.Ordinal);
        Assert.Contains("A client secret or any other credential is not accepted", Message(refused), StringComparison.Ordinal);
    }

    [Fact]
    public void LiveReadsWithoutTheManagedIdentityClientIdFailStartupOutsideDevelopment()
    {
        Exception refused = StartFails(Production(("Product:Directory:Mode", "Live")));

        Assert.Contains("ManagedIdentityClientId must be the client ID (a GUID) of the user-assigned managed identity", Message(refused), StringComparison.Ordinal);
    }

    [Fact]
    public void TheRecordedModeFailsStartupOutsideDevelopment()
    {
        Exception refused = StartFails(Production(("Product:Directory:Mode", "Recorded"), ("Product:Directory:RecordedPath", RecordedHttpHandler.Folder)));

        Assert.Contains("runs only in the Development environment", Message(refused), StringComparison.Ordinal);
    }

    [Fact]
    public void TheValidatorFindsEverySecretAndAsksLiveReadsForEntraSignIn()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureAd:ClientSecret"] = "fictional",
            ["AzureAd:ClientCredentials:0:SourceType"] = "ClientSecret",
            ["Other:ClientSecret"] = string.Empty,
            ["Product:Directory:Credential:SourceType"] = "SignedAssertionFromManagedIdentity",
        }).Build();
        Assert.Equal(new[] { "AzureAd:ClientCredentials:0:SourceType", "AzureAd:ClientSecret" }, DirectoryOptionsValidator.SecretKeys(configuration).ToArray());

        var development = new DirectoryOptionsValidator(new FixedEnvironment(Environments.Development), configuration);
        ValidateOptionsResult live = development.Validate(null, new DirectoryOptions { Mode = DirectoryReadMode.Live });
        Assert.True(live.Failed);
        Assert.Contains(live.Failures, f => f.Contains("Product:Auth:TenantId and Product:Auth:ClientId are required", StringComparison.Ordinal));
        Assert.DoesNotContain(live.Failures, f => f.Contains("client secret is configured", StringComparison.Ordinal));

        ValidateOptionsResult limits = development.Validate(null, new DirectoryOptions { CallTimeoutSeconds = 0, ToolBudgetSeconds = 500, InventoryEstateKeys = new[] { "Not A Key" } });
        Assert.Equal(3, limits.Failures!.Count());
    }

    [Fact]
    public async Task ASecretlessLiveConfigurationStartsWithTheExchangeOnTheEntraScheme()
    {
        HostSettings settings = Production(
            ("Product:Directory:Mode", "Live"),
            ("Product:Directory:Credential:ManagedIdentityClientId", "00000000-0000-4000-8000-0000000000d1"));
        await using ProductTestHost host = ProductTestHost.Start(settings);

        MicrosoftIdentityOptions identity = host.Get<IOptionsMonitor<MicrosoftIdentityOptions>>().Get(ProductAuthenticationDefaults.EntraScheme);
        CredentialDescription credential = Assert.Single(identity.ClientCredentials!);
        Assert.Equal(CredentialSource.SignedAssertionFromManagedIdentity, credential.SourceType);
        Assert.Equal("00000000-0000-4000-8000-0000000000d1", credential.ManagedIdentityClientId);
        Assert.True(string.IsNullOrEmpty(identity.ClientSecret));

        JwtBearerOptions jwt = host.Get<IOptionsMonitor<JwtBearerOptions>>().Get(ProductAuthenticationDefaults.EntraScheme);
        Assert.True(jwt.TokenValidationParameters.SaveSigninToken);
        Assert.False(jwt.SaveToken);

        Assert.IsType<GraphDirectoryReader>(host.Get<IDirectoryReader>());
        Assert.IsType<PowerPlatformInventoryReader>(host.Get<IInventoryReader>());
        Assert.IsType<OnBehalfOfTokenSource>(host.Get<IDownstreamTokenSource>());
        using IServiceScope scope = host.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetService<IAuthorizationHeaderProvider>());
    }

    [Fact]
    public async Task TheModeOffBindsNoReaderAndTheToolsAnswerAsBefore()
    {
        var settings = new HostSettings { Inventory = false, Services = services => services.AddProductDirectoryReads().AddProductDirectoryReads() };
        await using ProductTestHost host = ProductTestHost.Start(settings);

        JsonNode holders = await host.Call(Personas.Owen, "get_role_holders", "{\"roleKey\":\"platform-admin\"}");
        JsonNode gapCheck = await host.Call(Personas.Owen, "run_gap_check");

        Assert.Equal("upstream_unavailable", holders["error"]!["code"]!.GetValue<string>());
        Assert.Contains("no Graph client is bound to this server", holders["error"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal("upstream_unavailable", gapCheck["error"]!["code"]!.GetValue<string>());
        Assert.Null(host.Services.GetService<IDirectoryReader>());
        Assert.Single(host.Services.GetServices<IValidateOptions<DirectoryOptions>>().OfType<DirectoryOptionsValidator>());
    }

    [Fact]
    public async Task TheRecordedModeServesTheRecordingsToTheDevelopmentIdentity()
    {
        var settings = new HostSettings { Inventory = false, Services = services => services.AddProductDirectoryReads() };
        settings.Settings["Product:Directory:Mode"] = "Recorded";
        settings.Settings["Product:Directory:RecordedPath"] = RecordedHttpHandler.Folder;
        await using ProductTestHost host = ProductTestHost.Start(settings);

        JsonNode holders = await host.Call(Personas.Owen, "get_role_holders", "{\"roleKey\":\"platform-admin\"}");
        JsonNode owners = await host.Call(Personas.Owen, "run_owner_check");

        Assert.True(holders["ok"]!.GetValue<bool>(), holders.ToJsonString());
        Assert.Equal(4, holders["data"]!["members"]!.AsArray().Count);
        Assert.True(owners["ok"]!.GetValue<bool>(), owners.ToJsonString());
        Assert.Equal(2, owners["data"]!["findings"]!.AsArray().Count);
        Assert.IsType<RecordedTokenSource>(host.Get<IDownstreamTokenSource>());
    }

    // The local server in Production with Entra sign-in configured and the directory reads registered.
    private static HostSettings Production(params (string Key, string Value)[] more)
    {
        var settings = new HostSettings { Environment = "Production", DevIdentity = false, Seed = false, Inventory = false };
        settings.Settings["Product:Auth:ClientId"] = Personas.Audience;
        foreach ((string key, string value) in more)
        {
            settings.Settings[key] = value;
        }

        settings.Services = services => services.AddProductDirectoryReads();
        return settings;
    }

    // A start that fails with only the in-memory test server's report that its service provider was already disposed says
    // nothing about the startup checks (StartupFailures gives the reason), so it is tried again, up to three times.
    private static Exception StartFails(HostSettings settings)
    {
        Exception failed = null!;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            string folder = TestPaths.NewScratchFolder();
            settings.DatabaseFile = Path.Combine(folder, "product.db");
            try
            {
                failed = Assert.ThrowsAny<Exception>(() =>
                {
                    ProductTestHost host = ProductTestHost.Start(settings);
                    host.DisposeAsync().AsTask().GetAwaiter().GetResult();
                });
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                TestPaths.TryDelete(folder);
            }

            if (!StartupFailures.OnlyDisposed(failed))
            {
                return failed;
            }
        }

        return failed;
    }

    // Every message in the exception chain, so a test can find the options failure inside the host's start error.
    private static string Message(Exception exception)
    {
        var messages = new List<string>();
        for (Exception? e = exception; e != null; e = e.InnerException)
        {
            messages.Add(e.Message);
            if (e is AggregateException aggregate)
            {
                messages.AddRange(aggregate.InnerExceptions.Select(i => i.Message));
            }
        }

        return string.Join("\n", messages);
    }

    private sealed class FixedEnvironment : IHostEnvironment
    {
        public FixedEnvironment(string name)
        {
            EnvironmentName = name;
        }

        public string EnvironmentName { get; set; }

        public string ApplicationName { get; set; } = "Product.Azure.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
