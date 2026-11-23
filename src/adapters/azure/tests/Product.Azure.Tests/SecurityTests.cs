using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Product.Azure.Host;
using Product.Azure.Server;
using Product.Azure.Server.DevIdentity;
using Product.Azure.Stores;
using Product.Engine.Core.Canonical;
using Product.Engine.Runtime.Ports;
using Xunit;

namespace Product.Azure.Tests;

// Attempts to get past sign-in: path variants of the MCP endpoint on both transports, browser preflights, the listen
// address checks of the development identity, and the startup checks outside Development.
public sealed class SecurityTests
{
    private const string Initialize = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"probe\",\"version\":\"1.0.0\"}}}";

    [Theory]
    [InlineData("core")]
    [InlineData("sdk")]
    public async Task EveryPathTheMcpEndpointAnswersRefusesAnAnonymousCaller(string transport)
    {
        var settings = new HostSettings();
        settings.Settings["Product:Mcp:Transport"] = transport;
        await using ProductTestHost host = ProductTestHost.Start(settings);

        foreach (string path in new[] { "/mcp", "/mcp/", "/MCP", "/Mcp/" })
        {
            HttpCall call = await host.Post(path, null, Initialize, StreamableAccept);
            Assert.True(call.Status == 401 || call.Status == 404 || call.Status == 405, transport + " " + path + ": " + call);
            if (call.Status == 401)
            {
                Assert.Contains("resource_metadata=", call.Header("WWW-Authenticate"), StringComparison.Ordinal);
            }

            HttpCall ping = await host.Post(path, null, "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"ping\"}", StreamableAccept);
            Assert.True(ping.Status == 401 || ping.Status == 404 || ping.Status == 405, transport + " " + path + " ping: " + ping);
        }
    }

    [Theory]
    [InlineData("core")]
    [InlineData("sdk")]
    public async Task EveryPathTheMcpEndpointAnswersChecksTheOrigin(string transport)
    {
        var settings = new HostSettings();
        settings.Settings["Product:Mcp:Transport"] = transport;
        await using ProductTestHost host = ProductTestHost.Start(settings);

        foreach (string path in new[] { "/mcp", "/mcp/", "/MCP/" })
        {
            HttpCall call = await host.Post(path, Personas.Priya, Initialize, r =>
            {
                StreamableAccept(r);
                r.Headers.TryAddWithoutValidation("Origin", "https://attacker.example.net");
            });
            Assert.True(call.Status == 403 || call.Status == 404 || call.Status == 405, transport + " " + path + ": " + call);
        }
    }

    [Theory]
    [InlineData("core")]
    [InlineData("sdk")]
    public async Task TheMcpEndpointOpensNoStreamOrSessionForAnyone(string transport)
    {
        var settings = new HostSettings();
        settings.Settings["Product:Mcp:Transport"] = transport;
        await using ProductTestHost host = ProductTestHost.Start(settings);

        foreach (string path in new[] { "/mcp", "/mcp/", "/mcp/sse", "/mcp/message", "/sse", "/message" })
        {
            HttpCall get = await host.Send(HttpMethod.Get, path, null, null, r => r.Headers.TryAddWithoutValidation("Accept", "text/event-stream"));
            Assert.True(get.Status == 401 || get.Status == 404 || get.Status == 405, transport + " GET " + path + ": " + get);
            HttpCall delete = await host.Send(HttpMethod.Delete, path, null);
            Assert.True(delete.Status == 401 || delete.Status == 404 || delete.Status == 405, transport + " DELETE " + path + ": " + delete);
        }
    }

    [Theory]
    [InlineData("core")]
    [InlineData("sdk")]
    public async Task ASignedInCallerCannotSendMoreThanTheBodyLimit(string transport)
    {
        var settings = new HostSettings();
        settings.Settings["Product:Mcp:Transport"] = transport;
        settings.Settings["Product:MaxRequestBytes"] = "4096";
        await using ProductTestHost host = ProductTestHost.Start(settings);

        string large = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"find_applications\",\"arguments\":{\"text\":\"" + new string('a', 8000) + "\"}}}";
        HttpCall call = await host.Post("/mcp", Personas.Priya, large, StreamableAccept);
        Assert.True(call.Status == 413, transport + ": " + call);
        Assert.Equal(200, (await host.Post("/mcp", Personas.Priya, "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"ping\"}", StreamableAccept)).Status);
    }

    [Fact]
    public async Task ABrowserPreflightIsNeverAllowed()
    {
        await using ProductTestHost host = ProductTestHost.Start();

        foreach (string path in new[] { "/mcp", "/api/me", "/api/find_applications", "/api/deployments", "/engine/v1/approval-records/00000000-0000-4000-9000-000000000001/outcome" })
        {
            HttpCall preflight = await host.Send(HttpMethod.Options, path, null, null, r =>
            {
                r.Headers.TryAddWithoutValidation("Origin", "https://attacker.example.net");
                r.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "POST");
                r.Headers.TryAddWithoutValidation("Access-Control-Request-Headers", "authorization, content-type, " + ProductTestHost.DevHeader);
            });
            Assert.True(preflight.Status >= 400, "OPTIONS " + path + ": " + preflight);
            Assert.Null(preflight.Header("Access-Control-Allow-Origin"));
            Assert.Null(preflight.Header("Access-Control-Allow-Headers"));
        }
    }

    [Fact]
    public void TheDevelopmentIdentityRefusesPortSettingsThatListenOnEveryAddress()
    {
        var environment = new TestEnvironment("Development");
        foreach (string key in new[] { "http_ports", "https_ports" })
        {
            Assert.Throws<InvalidOperationException>(() => DevIdentityGuard.Validate(environment, Configuration(("Product:DevIdentity:Enabled", "true"), (key, "8080"))));
        }

        DevIdentityGuard.Validate(environment, Configuration(("Product:DevIdentity:Enabled", "true"), ("http_ports", "8080"), ("urls", "http://127.0.0.1:5080")));
        foreach (string url in new[] { "http://localhost.attacker.example.net:5080", "http://127.0.0.1.nip.io:5080", "http://[::]:5080", "http://*:5080", "http://example.org:5080" })
        {
            Assert.Throws<InvalidOperationException>(() => DevIdentityGuard.Validate(environment, Configuration(("Product:DevIdentity:Enabled", "true"), ("urls", url))));
        }
    }

    [Fact]
    public void OutsideDevelopmentStartupNeedsEntraSignInAndAnHttpsBaseUrl()
    {
        var missingTenant = new HostSettings { Environment = "Production", DevIdentity = false, Seed = false };
        Exception noTenant = Assert.ThrowsAny<Exception>(() => StartAndCall(missingTenant, withTenant: false));
        Assert.Contains("Product:Auth:TenantId and Product:Auth:ClientId are required outside Development", Messages(noTenant), StringComparison.Ordinal);

        var plainHttp = new HostSettings { Environment = "Production", DevIdentity = false, Seed = false };
        plainHttp.Settings["Product:Auth:ClientId"] = Personas.Audience;
        plainHttp.Settings["Product:PublicBaseUrl"] = "http://mcp.example.org";
        Exception http = Assert.ThrowsAny<Exception>(() => StartAndCall(plainHttp, withTenant: true));
        Assert.Contains("must be an https URL", Messages(http), StringComparison.Ordinal);
    }

    [Fact]
    public void TheDevelopmentIdentityRefusesAServerThatTrustsForwardingHeaders()
    {
        var environment = new TestEnvironment("Development");
        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => DevIdentityGuard.Validate(environment, Configuration(("Product:DevIdentity:Enabled", "true"), ("urls", "http://127.0.0.1:5080"), ("FORWARDEDHEADERS_ENABLED", "true"))));
        Assert.Contains("FORWARDEDHEADERS_ENABLED", thrown.Message, StringComparison.Ordinal);
        DevIdentityGuard.Validate(environment, Configuration(("Product:DevIdentity:Enabled", "false"), ("FORWARDEDHEADERS_ENABLED", "true")));
    }

    [Fact]
    public async Task ThePersonaHeaderIsRefusedAfterAProxyRewroteTheRequest()
    {
        await using ProductTestHost host = ProductTestHost.Start();

        foreach (string header in new[] { "X-Original-For", "X-Original-Host", "X-Original-Proto", "X-Forwarded-Host", "X-Forwarded-Proto", "X-Real-IP" })
        {
            HttpCall call = await host.Post("/mcp", Personas.Priya, Initialize, r => r.Headers.TryAddWithoutValidation(header, "127.0.0.1"));
            Assert.True(call.Status == 401, header + ": " + call);
        }

        Assert.Equal(200, (await host.Post("/mcp", Personas.Priya, Initialize)).Status);
    }

    [Fact]
    public void DevelopmentClaimsCountOnlyOnThePrincipalTheDevelopmentSchemeSignedIn()
    {
        var claims = new[]
        {
            new Claim("oid", Personas.PriyaId), new Claim("tid", Personas.Tenant), new Claim("azp", Personas.ClaudeCode), new Claim("scp", "mcp.access"),
            new Claim(ProductClaimTypes.DevIdentity, "true"), new Claim(ProductClaimTypes.DevPrincipalKind, "agent-identity"),
        };
        var development = new CallerMapper(new TestEnvironment("Development"));

        CallerClaims fromToken = development.Map(new ClaimsPrincipal(new ClaimsIdentity(claims, ProductAuthenticationDefaults.EntraScheme)))!;
        Assert.False(fromToken.IsDevIdentity);
        Assert.Null(fromToken.DeclaredPrincipalKind);

        CallerClaims fromPersona = development.Map(new ClaimsPrincipal(new ClaimsIdentity(claims, ProductAuthenticationDefaults.DevelopmentScheme)))!;
        Assert.True(fromPersona.IsDevIdentity);
        Assert.Equal("agent-identity", fromPersona.DeclaredPrincipalKind);

        // Outside Development a principal with the marker is refused, whichever scheme signed it in.
        var production = new CallerMapper(new TestEnvironment("Production"));
        Assert.Null(production.Map(new ClaimsPrincipal(new ClaimsIdentity(claims, ProductAuthenticationDefaults.EntraScheme))));
        Assert.Null(production.Map(new ClaimsPrincipal(new ClaimsIdentity(claims, ProductAuthenticationDefaults.DevelopmentScheme))));
    }

    [Fact]
    public async Task EveryAnswerTellsBrowsersNotToSniffOrCacheIt()
    {
        await using ProductTestHost host = ProductTestHost.Start();

        foreach (HttpCall call in new[]
        {
            await host.Get("/api/me", Personas.Priya),
            await host.Get("/api/me"),
            await host.Mcp(Personas.Priya, "tools/list"),
            await host.Mcp(null, "tools/list"),
            await host.Post("/api/find_applications", Personas.Priya, "{\"text\":\"crew\"}"),
        })
        {
            Assert.Equal("nosniff", call.Header("X-Content-Type-Options"));
            Assert.Equal("no-store", call.Header("Cache-Control"));

            // A local run on http://127.0.0.1 is never pinned to https.
            Assert.Null(call.Header("Strict-Transport-Security"));
        }

        HttpCall metadata = await host.Get("/.well-known/oauth-protected-resource/mcp");
        Assert.Equal("nosniff", metadata.Header("X-Content-Type-Options"));
        Assert.Equal("public, max-age=3600", metadata.Header("Cache-Control"));
    }

    [Fact]
    public async Task AnAnswerFromAPrincipalRecordedAsAnAgentNeverCountsEvenWhenTheRunnerCallsItAUser()
    {
        await using ProductTestHost host = ProductTestHost.Start();
        (string approvalId, string decisionId) = await SubmitIntake(host);

        // The directory records the responder as an agent user (Entra Agent ID). The approval runner relays its click on
        // the card as a person's, with the principal kind user.
        host.Get<IStoreSeeder>().Seed("principal-reference", CanonicalJson.Parse("{\"principalKind\":\"agent-user\",\"entraObjectId\":\"" + Personas.AgentUserId + "\",\"displayLabel\":\"Owners agent\",\"isErased\":false}"));
        string answer = "{\"outcome\":\"approval-approved\",\"approver\":\"" + Personas.AgentUserId + "\",\"approverActorKind\":\"person\",\"approverPrincipalKind\":\"user\",\"respondedOn\":\"2026-11-23T09:30:00Z\",\"approverHoldsRequiredRole\":true,\"channel\":\"teams\"}";
        HttpCall call = await host.Post("/engine/v1/approval-records/" + approvalId + "/outcome", Personas.Runner, answer);

        Assert.True(call.Status == 200, call + "\n" + host.Logs);
        Assert.Equal("recommendation-only", call.Json!["data"]!["outcome"]!["meaning"]!.GetValue<string>());
        Assert.False(call.Json["data"]!["countsTowardRequirement"]!.GetValue<bool>());
        Assert.Contains(call.Json["warnings"]!.AsArray(), w => w!["code"]?.GetValue<string>() == "approval.non-user-principal");
        StoredRow card = host.Get<IRecordStore>().Get("approval-record", approvalId)!;
        Assert.Equal("agent-user", card.Text("approverPrincipalKind"));
        Assert.False(card.Flag("countsTowardRequirement"));
        Assert.Equal("proposed", host.Get<IRecordStore>().Get("decision", decisionId)!.Text("statusMeaning"));
    }

    [Fact]
    public void OutsideDevelopmentTheServerRefusesASqlPassword()
    {
        const string withPassword = "Server=tcp:sql.example.org,1433;Database=product;User ID=app;Password=placeholder-not-a-secret;Encrypt=True";
        const string managedIdentity = "Server=tcp:sql.example.org,1433;Database=product;Authentication=Active Directory Managed Identity;Encrypt=True";
        foreach (string environment in new[] { "Production", "Staging" })
        {
            Assert.Throws<InvalidOperationException>(() => ServerStore.CheckNoPassword(withPassword, new TestEnvironment(environment)));
            Assert.Throws<InvalidOperationException>(() => ServerStore.CheckNoPassword(withPassword.Replace("Password=", "Pwd=", StringComparison.Ordinal), new TestEnvironment(environment)));
            ServerStore.CheckNoPassword(managedIdentity, new TestEnvironment(environment));
        }

        ServerStore.CheckNoPassword(withPassword, new TestEnvironment("Development"));
    }

    [Fact]
    public void TheSeedCommandRunsInTheDemoEnvironmentForADemoInstall()
    {
        // D-33: a demo install seeds the fictional demo estate with the environment named Demo.
        string folder = TestPaths.NewScratchFolder();
        try
        {
            using var output = new StringWriter();
            int code = ServerCommands.Seed(new[] { "--environment=" + ServerCommands.DemoEnvironment, "--Product:Store:Provider=sqlite", "--Product:Store:Sqlite:ConnectionString=Data Source=" + Path.Combine(folder, "product.db") }, output);
            Assert.True(code == 0, output.ToString());
            Assert.Contains("Seeded ", output.ToString(), StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(folder, "product.db")));
        }
        finally
        {
            TestPaths.TryDelete(folder);
        }
    }

    [Fact]
    public void TheSeedCommandRunsOnlyInDevelopment()
    {
        string folder = TestPaths.NewScratchFolder();
        try
        {
            foreach (string environment in new[] { "Staging", "Test", "Production", "demo-sandbox" })
            {
                using var output = new StringWriter();
                int code = ServerCommands.Seed(new[] { "--environment=" + environment, "--Product:Store:Provider=sqlite", "--Product:Store:Sqlite:ConnectionString=Data Source=" + Path.Combine(folder, "product.db") }, output);
                Assert.Equal(1, code);
                Assert.Contains("runs only in Development", output.ToString(), StringComparison.Ordinal);
                Assert.False(File.Exists(Path.Combine(folder, "product.db")));
            }
        }
        finally
        {
            TestPaths.TryDelete(folder);
        }
    }

    // Priya, through Claude Code, files and submits an intake; the engine opens one triage card for a platform admin.
    private static async Task<(string ApprovalId, string DecisionId)> SubmitIntake(ProductTestHost host)
    {
        JsonNode draft = await host.Call(Personas.Priya, "save_intake_draft", "{\"title\":\"Field inspections app\",\"need\":\"Inspectors fill in paper forms on site and retype them at the office.\",\"proposedKindKey\":\"power-platform-app\"}");
        Assert.True(draft["ok"]!.GetValue<bool>(), draft.ToJsonString() + "\n" + host.Logs);
        string intake = draft["data"]!["recordId"]!.GetValue<string>();
        JsonNode submitted = await host.Call(Personas.Priya, "submit_record", "{\"subjectKind\":\"intake-request\",\"subjectId\":\"" + intake + "\"}");
        Assert.True(submitted["ok"]!.GetValue<bool>(), submitted.ToJsonString());
        JsonNode card = Assert.Single(submitted["data"]!["approvalsRequested"]!.AsArray())!;
        return (card["approvalRecordId"]!.GetValue<string>(), submitted["data"]!["decisionId"]!.GetValue<string>());
    }

    private static void StreamableAccept(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
    }

    private static void StartAndCall(HostSettings settings, bool withTenant)
    {
        if (!withTenant)
        {
            settings.Settings["Product:Auth:TenantId"] = string.Empty;
        }

        ProductTestHost host = ProductTestHost.Start(settings);
        try
        {
            host.Get("/health/live").GetAwaiter().GetResult();
        }
        finally
        {
            host.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static string Messages(Exception exception)
    {
        var messages = new List<string>();
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        if (exception is AggregateException aggregate)
        {
            foreach (Exception inner in aggregate.InnerExceptions)
            {
                messages.Add(Messages(inner));
            }
        }

        return string.Join(" | ", messages);
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] values)
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, string value) in values)
        {
            data[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(data).Build();
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public TestEnvironment(string name)
        {
            EnvironmentName = name;
        }

        public string EnvironmentName { get; set; }

        public string ApplicationName { get; set; } = "Product.Azure.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
