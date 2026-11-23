using System;
using System.Collections.Generic;
using System.Net;
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
using Product.Engine.Core.Contract;
using Product.Mcp.Core.Identity;
using Xunit;

namespace Product.Azure.Tests;

// Sign-in and the development identity: anonymous requests are refused on every product route, the development identity
// works only in Development with its switch on and only for loopback requests, startup fails when the switch is on in
// any other environment or on a non-loopback address, and the token rules of the MCP core hold over HTTP.
public sealed class AuthAndIdentityTests
{
    private const string Initialize = "{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"claude-code\",\"version\":\"2.0.0\"}}";

    [Fact]
    public async Task AnonymousRequestsAreRefusedOnEveryProductRoute()
    {
        await using ProductTestHost host = ProductTestHost.Start();

        HttpCall mcp = await host.Mcp(null, "initialize", Initialize);
        Assert.Equal(401, mcp.Status);
        Assert.Contains("resource_metadata=\"https://mcp.example.org/.well-known/oauth-protected-resource/mcp\"", mcp.Header("WWW-Authenticate"), StringComparison.Ordinal);
        Assert.Equal("not_signed_in", mcp.Json!["error"]!["data"]!["error"]!["code"]!.GetValue<string>());

        HttpCall list = await host.Mcp(null, "tools/list");
        Assert.Equal(401, list.Status);

        foreach (HttpCall call in new[]
        {
            await host.Get("/api/me"),
            await host.Get("/api/info"),
            await host.Post("/api/find_applications", null, "{}"),
            await host.Post("/api/deployments", null, "{}"),
            await host.Post("/api/deployments/corrections", null, "{}"),
            await host.Post("/engine/v1/approval-records/00000000-0000-4000-9000-000000000001/outcome", null, "{}"),
        })
        {
            Assert.Equal(401, call.Status);
            Assert.False(call.Json!["ok"]!.GetValue<bool>());
            Assert.Equal("not_signed_in", call.Json["error"]!["code"]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task HealthAndMetadataAreAnonymousAndCarryNoData()
    {
        await using ProductTestHost host = ProductTestHost.Start();

        HttpCall live = await host.Get("/health/live");
        Assert.Equal(200, live.Status);
        Assert.Equal("{\"status\":\"live\"}", JsonText.Canonical(live.Json));
        HttpCall ready = await host.Get("/health/ready");
        Assert.Equal(200, ready.Status);
        Assert.Equal("{\"status\":\"ready\"}", JsonText.Canonical(ready.Json));

        foreach (string path in new[] { "/.well-known/oauth-protected-resource/mcp", "/.well-known/oauth-protected-resource" })
        {
            HttpCall metadata = await host.Get(path);
            Assert.Equal(200, metadata.Status);
            Assert.Equal("https://mcp.example.org/mcp", metadata.Json!["resource"]!.GetValue<string>());
            Assert.Equal("https://login.microsoftonline.com/" + Personas.Tenant + "/v2.0", metadata.Json["authorization_servers"]![0]!.GetValue<string>());
            Assert.Equal("mcp.access", metadata.Json["scopes_supported"]![0]!.GetValue<string>());
        }

        HttpCall get = await host.Get("/mcp", Personas.Priya);
        Assert.Equal(405, get.Status);
        Assert.Equal("POST", get.Header("Allow"));
        HttpCall delete = await host.Send(HttpMethod.Delete, "/mcp", Personas.Priya);
        Assert.Equal(405, delete.Status);
    }

    [Fact]
    public async Task ThePersonaHeaderSignsInOnlyWhenTheSwitchIsOn()
    {
        await using ProductTestHost off = ProductTestHost.Start(new HostSettings { DevIdentity = false });
        HttpCall refused = await off.Mcp(Personas.Priya, "initialize", Initialize);
        Assert.Equal(401, refused.Status);
        HttpCall me = await off.Get("/api/me", Personas.Priya);
        Assert.Equal(401, me.Status);

        await using ProductTestHost on = ProductTestHost.Start();
        HttpCall accepted = await on.Mcp(Personas.Priya, "initialize", Initialize);
        Assert.Equal(200, accepted.Status);
        Assert.Equal("2025-06-18", accepted.Json!["result"]!["protocolVersion"]!.GetValue<string>());
    }

    [Fact]
    public async Task ThePersonaHeaderIsRefusedForRequestsThatAreNotLoopback()
    {
        await using ProductTestHost host = ProductTestHost.Start();

        host.RemoteAddress = IPAddress.Parse("203.0.113.7");
        Assert.Equal(401, (await host.Mcp(Personas.Priya, "initialize", Initialize)).Status);
        Assert.Equal(401, (await host.Get("/api/me", Personas.Priya)).Status);

        host.RemoteAddress = null;
        Assert.Equal(401, (await host.Mcp(Personas.Priya, "initialize", Initialize)).Status);

        host.RemoteAddress = IPAddress.Loopback;
        HttpCall forwarded = await host.Post("/mcp", Personas.Priya, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":" + Initialize + "}", r => r.Headers.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.7"));
        Assert.Equal(401, forwarded.Status);
        HttpCall forwardedStandard = await host.Post("/mcp", Personas.Priya, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":" + Initialize + "}", r => r.Headers.TryAddWithoutValidation("Forwarded", "for=203.0.113.7"));
        Assert.Equal(401, forwardedStandard.Status);
        HttpCall otherHost = await host.Post("/mcp", Personas.Priya, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":" + Initialize + "}", r => r.Headers.Host = "product.example.org");
        Assert.Equal(401, otherHost.Status);

        Assert.Equal(200, (await host.Mcp(Personas.Priya, "initialize", Initialize)).Status);
    }

    [Fact]
    public void StartupFailsWhenTheSwitchIsOnOutsideDevelopment()
    {
        foreach (string environment in new[] { "Production", "Staging" })
        {
            InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => ProductServer.Build(new[] { "--environment=" + environment, "--Product:DevIdentity:Enabled=true" }));
            Assert.Contains("Product:DevIdentity:Enabled is true", thrown.Message, StringComparison.Ordinal);
        }

        Exception throughHost = Assert.ThrowsAny<Exception>(() => ProductTestHost.Start(new HostSettings { Environment = "Production", DevIdentity = true, Seed = false }));
        Assert.Contains("Product:DevIdentity:Enabled is true", Messages(throughHost), StringComparison.Ordinal);
    }

    [Fact]
    public void StartupFailsWhenTheSwitchIsOnAndTheServerListensBeyondLoopback()
    {
        foreach (string urls in new[] { "http://0.0.0.0:5080", "http://+:5080", "http://127.0.0.1:5080;http://192.0.2.10:5080" })
        {
            InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => ProductServer.Build(new[] { "--environment=Development", "--Product:DevIdentity:Enabled=true", "--urls=" + urls }));
            Assert.Contains("loopback", thrown.Message, StringComparison.Ordinal);
        }

        var environment = new TestEnvironment("Development");
        DevIdentityGuard.Validate(environment, Configuration(("Product:DevIdentity:Enabled", "true"), ("urls", "http://127.0.0.1:5080;http://localhost:5081;http://[::1]:5082")));
        DevIdentityGuard.Validate(new TestEnvironment("Production"), Configuration(("Product:DevIdentity:Enabled", "false")));
        Assert.Throws<InvalidOperationException>(() => DevIdentityGuard.Validate(environment, Configuration(("Product:DevIdentity:Enabled", "true"), ("Kestrel:Endpoints:Http:Url", "http://0.0.0.0:5080"))));
    }

    [Fact]
    public async Task InProductionThePersonaHeaderIsIgnoredAndADevelopmentClaimIsRefused()
    {
        var settings = new HostSettings { Environment = "Production", DevIdentity = false };
        settings.Settings["Product:Auth:ClientId"] = Personas.Audience;
        await using ProductTestHost host = ProductTestHost.Start(settings);

        HttpCall mcp = await host.Mcp(Personas.Priya, "initialize", Initialize);
        Assert.Equal(401, mcp.Status);
        HttpCall me = await host.Get("/api/me", Personas.Priya);
        Assert.Equal(401, me.Status);

        var claims = new ClaimsIdentity(new[]
        {
            new Claim("oid", Personas.PriyaId), new Claim("tid", Personas.Tenant), new Claim("azp", Personas.ClaudeCode), new Claim("scp", "mcp.access"), new Claim("product_dev_identity", "true"),
        }, "test");
        Assert.Null(new CallerMapper(new TestEnvironment("Production")).Map(new ClaimsPrincipal(claims)));
        Assert.NotNull(new CallerMapper(new TestEnvironment("Development")).Map(new ClaimsPrincipal(claims)));
    }

    [Fact]
    public async Task TheMcpCoreTokenRulesHoldOverHttp()
    {
        await using ProductTestHost host = ProductTestHost.Start();

        HttpCall otherTenant = await host.Mcp(Personas.OtherTenantPriya, "initialize", Initialize);
        Assert.Equal(401, otherTenant.Status);
        Assert.Equal(ClaimsTokenValidator.OtherTenantReason, otherTenant.Json!["error"]!["data"]!["error"]!["message"]!.GetValue<string>());
        Assert.Equal(401, (await host.Get("/api/me", Personas.OtherTenantPriya)).Status);

        HttpCall appOnly = await host.Mcp(Personas.Pipeline, "tools/list");
        Assert.Equal(403, appOnly.Status);
        Assert.Equal("not_authorized", appOnly.Json!["error"]!["data"]!["error"]!["code"]!.GetValue<string>());
        Assert.Equal(ClaimsTokenValidator.AppOnlyReason, appOnly.Json["error"]!["data"]!["error"]!["message"]!.GetValue<string>());
        Assert.Contains("resource_metadata=", appOnly.Header("WWW-Authenticate"), StringComparison.Ordinal);

        JsonNode agent = await host.Call(Personas.Agent, "whoami");
        Assert.False(agent["ok"]!.GetValue<bool>());
        Assert.Equal("not_authorized", agent["error"]!["code"]!.GetValue<string>());

        JsonNode priya = await host.Call(Personas.Priya, "whoami");
        Assert.True(priya["ok"]!.GetValue<bool>());
        Assert.Equal("ai-client-for-person", priya["data"]!["actorKind"]!.GetValue<string>());
    }

    [Fact]
    public void TheClaimsValidatorAppliesTheCoreRulesToTheSignedInCaller()
    {
        Func<string, PrincipalKind> user = _ => PrincipalKind.User;
        var person = new CallerClaims(Personas.PriyaId, Personas.Tenant, Personas.ClaudeCode, isAppOnly: false, scopes: new[] { "mcp.access" }, groupObjectIds: new[] { Personas.MakerGroup });

        Assert.Equal(TokenValidationStatus.Missing, new ClaimsTokenValidator(null, false, "mcp.access", user, Personas.Tenant).Validate(null, DateTime.UtcNow).Status);
        Assert.Equal(TokenValidationStatus.Invalid, new ClaimsTokenValidator(null, true, "mcp.access", user, Personas.Tenant).Validate(null, DateTime.UtcNow).Status);
        Assert.Equal(TokenValidationStatus.Valid, new ClaimsTokenValidator(person, true, "mcp.access", user, Personas.Tenant).Validate("Bearer ignored", DateTime.UtcNow).Status);

        TokenValidationResult noScope = new ClaimsTokenValidator(person, true, "api.access", user, Personas.Tenant).Validate(null, DateTime.UtcNow);
        Assert.Equal(TokenValidationStatus.Forbidden, noScope.Status);
        Assert.Equal("The token does not carry the api.access scope.", noScope.Reason);

        var overage = new CallerClaims(Personas.PriyaId, Personas.Tenant, Personas.ClaudeCode, isAppOnly: false, scopes: new[] { "mcp.access" }, groupsOverage: true);
        Assert.Equal(ClaimsTokenValidator.GroupsOverageReason, new ClaimsTokenValidator(overage, true, "mcp.access", user, Personas.Tenant).Validate(null, DateTime.UtcNow).Reason);

        var otherTenant = new CallerClaims(Personas.PriyaId, Personas.OtherTenant, Personas.ClaudeCode, isAppOnly: false, scopes: new[] { "mcp.access" });
        Assert.Equal(ClaimsTokenValidator.OtherTenantReason, new ClaimsTokenValidator(otherTenant, true, "mcp.access", user, Personas.Tenant).Validate(null, DateTime.UtcNow).Reason);

        var workload = new CallerClaims(Personas.PipelineId, Personas.Tenant, Personas.ClaudeCode, isAppOnly: true, appRoles: new[] { "Deployment.Record" });
        Assert.Equal(ClaimsTokenValidator.AppOnlyReason, new ClaimsTokenValidator(workload, true, "mcp.access", user, Personas.Tenant).Validate(null, DateTime.UtcNow).Reason);
    }

    private static string Messages(Exception exception)
    {
        var messages = new List<string>();
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            messages.Add(current.Message);
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
