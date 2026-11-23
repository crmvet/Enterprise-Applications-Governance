using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Product.Azure.Host;
using Xunit;

namespace Product.Azure.Tests;

// The production sign-in path: Microsoft Entra ID JWT bearer through Microsoft.Identity.Web, in the Production
// environment, with the tenant's signing key replaced by a test key (the only change: the issuer metadata a real run
// downloads is given in memory, so nothing goes to the network). A token signs in only as its own object ID, a token
// that is expired, for another audience, unsigned, signed with another key, from another tenant or marked as a
// development identity is refused, and the caller kinds hold on every route.
public sealed class EntraSignInTests
{
    private const string Initialize = "{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"claude-code\",\"version\":\"2.0.0\"}}";
    private const string RunnerClient = "00000000-0000-4000-8000-000000000311";

    private static readonly string Issuer = "https://login.microsoftonline.com/" + Personas.Tenant + "/v2.0";
    private static readonly RsaSecurityKey TenantKey = new(RSA.Create(2048)) { KeyId = "tenant-key" };
    private static readonly RsaSecurityKey OtherKey = new(RSA.Create(2048)) { KeyId = "tenant-key" };

    [Fact]
    public void TheEntraSchemeValidatesTheTokenAsTheTenantsApiExpects()
    {
        var settings = Production();
        settings.Services = null;
        using var scope = new HostScope(settings);
        JwtBearerOptions jwt = scope.Host.Get<IOptionsMonitor<JwtBearerOptions>>().Get(ProductAuthenticationDefaults.EntraScheme);

        Assert.Contains(Personas.Tenant, jwt.Authority ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.False(jwt.SaveToken);
        Assert.False(jwt.MapInboundClaims);
        TokenValidationParameters parameters = jwt.TokenValidationParameters;
        Assert.True(parameters.ValidateIssuer);
        Assert.True(parameters.ValidateAudience);
        Assert.True(parameters.ValidateLifetime);
        Assert.True(parameters.RequireSignedTokens);
        Assert.True(parameters.RequireExpirationTime);
        Assert.Equal(new[] { Personas.Audience }, parameters.ValidAudiences);
        Assert.Equal("roles", parameters.RoleClaimType);
    }

    [Fact]
    public async Task ADelegatedTokenSignsInAsItsOwnObjectIdOnly()
    {
        await using ProductTestHost host = ProductTestHost.Start(Production());
        string token = Token(Delegated(Personas.PriyaId, Personas.RequesterGroup, Personas.MakerGroup));

        HttpCall me = await host.Send(HttpMethod.Get, "/api/me", null, null, Bearer(token));
        Assert.True(me.Status == 200, me + "\n" + host.Logs);
        Assert.Equal(Personas.PriyaId, me.Json!["objectId"]!.GetValue<string>());
        Assert.Equal(new[] { "maker", "requester" }, me.Json["roles"]!.AsArray().Select(r => r!["roleKey"]!.GetValue<string>()).OrderBy(r => r, StringComparer.Ordinal).ToArray());

        HttpCall whoami = await host.Send(HttpMethod.Post, "/mcp", null, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"whoami\",\"arguments\":{}}}", Bearer(token));
        Assert.Equal(200, whoami.Status);
        JsonNode who = whoami.Json!["result"]!["structuredContent"]!;
        Assert.True(who["ok"]!.GetValue<bool>(), who.ToJsonString());
        Assert.Equal("ai-client-for-person", who["data"]!["actorKind"]!.GetValue<string>());

        // The development identity header in Production names nobody: the token's caller is the caller.
        HttpCall withHeader = await host.Send(HttpMethod.Get, "/api/me", null, null, r =>
        {
            Bearer(token)(r);
            r.Headers.TryAddWithoutValidation(ProductTestHost.DevHeader, Personas.Owen.Name);
        });
        Assert.Equal(200, withHeader.Status);
        Assert.Equal(Personas.PriyaId, withHeader.Json!["objectId"]!.GetValue<string>());
    }

    [Fact]
    public async Task TokensThatAreNotTheTenantsValidTokensForThisApiAreRefused()
    {
        await using ProductTestHost host = ProductTestHost.Start(Production());
        Dictionary<string, object> priya = Delegated(Personas.PriyaId, Personas.RequesterGroup, Personas.MakerGroup);

        var refused = new Dictionary<string, string>
        {
            ["expired"] = Token(priya, expires: DateTime.UtcNow.AddMinutes(-10)),
            ["another audience"] = Token(priya, audience: "api://another-api"),
            ["another signing key"] = Token(priya, key: OtherKey),
            ["unsigned"] = new JsonWebTokenHandler().CreateToken(Descriptor(priya, null, DateTime.UtcNow.AddMinutes(30), Personas.Audience)),
            ["another tenant"] = Token(With(priya, ("tid", Personas.OtherTenant))),
            ["development marker"] = Token(With(priya, ("product_dev_identity", "true"), ("product_principal_kind", "user"))),
            ["no object ID"] = Token(Without(priya, "oid")),
            ["not a token"] = "not-a-token",
        };

        foreach (KeyValuePair<string, string> token in refused)
        {
            HttpCall me = await host.Send(HttpMethod.Get, "/api/me", null, null, Bearer(token.Value));
            Assert.True(me.Status == 401, token.Key + ": " + me);
            HttpCall mcp = await host.Send(HttpMethod.Post, "/mcp", null, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":" + Initialize + "}", Bearer(token.Value));
            Assert.True(mcp.Status == 401, token.Key + ": " + mcp);
            Assert.Contains("resource_metadata=", mcp.Header("WWW-Authenticate"), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AppOnlyAndDelegatedTokensReachOnlyTheirOwnRoutes()
    {
        await using ProductTestHost host = ProductTestHost.Start(Production());
        string runner = Token(AppOnly(Personas.RunnerId, RunnerClient, "Approval.Write"));
        string runnerWithoutIdtyp = Token(Without(AppOnly(Personas.RunnerId, RunnerClient, "Approval.Write"), "idtyp"));
        string ownerAssignedTheRole = Token(With(Delegated(Personas.OwenId, Personas.PlatformAdminGroup), ("roles", new[] { "Approval.Write", "Deployment.Record" })));

        foreach (string appOnly in new[] { runner, runnerWithoutIdtyp })
        {
            HttpCall mcp = await host.Send(HttpMethod.Post, "/mcp", null, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}", Bearer(appOnly));
            Assert.Equal(403, mcp.Status);
            Assert.Equal(403, (await host.Send(HttpMethod.Get, "/api/me", null, null, Bearer(appOnly))).Status);
            Assert.Equal(403, (await host.Send(HttpMethod.Post, "/api/find_applications", null, "{}", Bearer(appOnly))).Status);
            Assert.Equal(403, (await host.Send(HttpMethod.Post, "/api/deployments", null, "{}", Bearer(appOnly))).Status);
        }

        // A person who holds the app roles through a user assignment still has a delegated token: the approval and record
        // routes refuse it.
        string answer = "{\"outcome\":\"approval-approved\",\"approver\":\"" + Personas.OwenId + "\",\"approverActorKind\":\"person\",\"approverPrincipalKind\":\"user\",\"respondedOn\":\"2026-11-23T09:30:00Z\"}";
        HttpCall approval = await host.Send(HttpMethod.Post, "/engine/v1/approval-records/00000000-0000-4000-9000-000000000001/outcome", null, answer, Bearer(ownerAssignedTheRole));
        Assert.Equal(403, approval.Status);
        Assert.Equal("not_authorized", approval.Json!["error"]!["code"]!.GetValue<string>());
        Assert.Equal(403, (await host.Send(HttpMethod.Post, "/api/deployments", null, "{}", Bearer(ownerAssignedTheRole))).Status);
    }

    // Microsoft Entra Agent ID marks its tokens with facet claims (https://learn.microsoft.com/entra/agent-id/agent-token-claims):
    // xms_sub_fct names what the subject is and xms_act_fct what the client is, 11 for an agent identity and 13 for an
    // agent's user account. The token says so even when no Principal Reference records the principal: an agent holds
    // no role and no grant, and an agent client is never the person entry.
    [Fact]
    public async Task AnAgentsUserAccountHoldsNoRoleEvenWithNoPrincipalReference()
    {
        await using ProductTestHost host = ProductTestHost.Start(Production());
        const string agentAccount = "00000000-0000-4000-8000-000000000402";
        Dictionary<string, object> claims = Delegated(agentAccount, Personas.OwnerGroup, Personas.PlatformAdminGroup);

        // Without the facet the same claims are a person's, with the roles of both groups: the facet alone makes the difference.
        HttpCall asPerson = await host.Send(HttpMethod.Get, "/api/me", null, null, Bearer(Token(claims)));
        Assert.True(asPerson.Status == 200, asPerson + "\n" + host.Logs);
        Assert.NotEmpty(asPerson.Json!["roles"]!.AsArray());

        foreach (string facets in new[] { "13", "1 13" })
        {
            string token = Token(With(claims, ("xms_sub_fct", facets), ("xms_act_fct", "11")));
            HttpCall me = await host.Send(HttpMethod.Get, "/api/me", null, null, Bearer(token));
            Assert.True(me.Status == 200, facets + ": " + me);
            Assert.Equal("agent-identity", me.Json!["actorKind"]!.GetValue<string>());
            Assert.Empty(me.Json["roles"]!.AsArray());
            HttpCall read = await host.Send(HttpMethod.Post, "/api/get_portfolio_summary", null, "{}", Bearer(token));
            Assert.True(read.Status == 403, facets + ": " + read);
            HttpCall whoami = await host.Send(HttpMethod.Post, "/mcp", null, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"whoami\",\"arguments\":{}}}", Bearer(token));
            Assert.True(whoami.Status == 200, facets + ": " + whoami);
            Assert.Equal("not_authorized", whoami.Json!["result"]!["structuredContent"]!["error"]!["code"]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task AnAgentIdentityIsNeverTheApprovalRunnerEvenWithItsGrant()
    {
        HostSettings settings = Production();
        settings.Settings[ApprovalRunnerOptions.SectionName + ":Enabled"] = "true";
        await using ProductTestHost host = ProductTestHost.Start(settings);
        Dictionary<string, object> runner = AppOnly(Personas.RunnerId, RunnerClient, "Approval.Write");

        HttpCall claim = await host.Send(HttpMethod.Post, "/engine/v1/approval-runner/claims", null, "{}", Bearer(Token(runner)));
        Assert.True(claim.Status == 200, claim + "\n" + host.Logs);

        // The identity that holds the approval writer grant turns out to be an agent identity (the grant was recorded for
        // it by mistake, and no Principal Reference says what it is). Its own token says so.
        foreach ((string Name, object Value)[] facets in new[]
        {
            new (string, object)[] { ("xms_sub_fct", "11"), ("xms_act_fct", "11") },
            new (string, object)[] { ("xms_act_fct", "11") },
        })
        {
            string agent = Token(With(runner, facets));
            HttpCall agentClaim = await host.Send(HttpMethod.Post, "/engine/v1/approval-runner/claims", null, "{}", Bearer(agent));
            Assert.True(agentClaim.Status == 403, agentClaim + "\n" + host.Logs);
            Assert.Equal("not_authorized", agentClaim.Json!["error"]!["code"]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task AnAgentIdentityActingForAPersonIsNeverThePersonEntry()
    {
        await using ProductTestHost host = ProductTestHost.Start(Production());
        Dictionary<string, object> nadiaWeb = With(Delegated(Personas.NadiaId, Personas.OwnerGroup), ("azp", Personas.WebApp), ("scp", "api.access"));

        HttpCall person = await host.Send(HttpMethod.Get, "/api/me", null, null, Bearer(Token(nadiaWeb)));
        Assert.True(person.Status == 200, person + "\n" + host.Logs);
        Assert.Equal("person", person.Json!["actorKind"]!.GetValue<string>());

        // The same client ID, but the token says the client is an agent identity: never the person.
        string agentClient = Token(With(nadiaWeb, ("xms_act_fct", "11")));
        HttpCall agent = await host.Send(HttpMethod.Get, "/api/me", null, null, Bearer(agentClient));
        Assert.True(agent.Status == 200, agent.ToString());
        Assert.Equal("ai-client-for-person", agent.Json!["actorKind"]!.GetValue<string>());

        // As an AI client it must be a Known Client, and the web app's client ID is not one.
        HttpCall read = await host.Send(HttpMethod.Post, "/api/get_portfolio_summary", null, "{}", Bearer(agentClient));
        Assert.True(read.Status == 403, read.ToString());
    }

    private static HostSettings Production()
    {
        var settings = new HostSettings { Environment = "Production", DevIdentity = false };
        settings.Settings["Product:Auth:ClientId"] = Personas.Audience;
        settings.Services = services => services.PostConfigure<JwtBearerOptions>(ProductAuthenticationDefaults.EntraScheme, options =>
        {
            // The tenant's issuer metadata, in memory: its issuer and its signing key.
            var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
            configuration.SigningKeys.Add(TenantKey);
            options.Configuration = configuration;
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            options.TokenValidationParameters.ValidIssuers = new[] { Issuer };
        });
        return settings;
    }

    private static Dictionary<string, object> Delegated(string objectId, params string[] groups)
    {
        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["oid"] = objectId,
            ["sub"] = "pairwise-subject-" + objectId.Substring(objectId.Length - 4),
            ["tid"] = Personas.Tenant,
            ["azp"] = Personas.ClaudeCode,
            ["scp"] = "mcp.access api.access",
            ["groups"] = groups,
            ["ver"] = "2.0",
        };
    }

    private static Dictionary<string, object> AppOnly(string objectId, string clientId, params string[] roles)
    {
        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["oid"] = objectId,
            ["sub"] = objectId,
            ["tid"] = Personas.Tenant,
            ["azp"] = clientId,
            ["roles"] = roles,
            ["idtyp"] = "app",
            ["ver"] = "2.0",
        };
    }

    private static Dictionary<string, object> With(Dictionary<string, object> claims, params (string Name, object Value)[] more)
    {
        var copy = new Dictionary<string, object>(claims, StringComparer.Ordinal);
        foreach ((string name, object value) in more)
        {
            copy[name] = value;
        }

        return copy;
    }

    private static Dictionary<string, object> Without(Dictionary<string, object> claims, string name)
    {
        var copy = new Dictionary<string, object>(claims, StringComparer.Ordinal);
        copy.Remove(name);
        return copy;
    }

    private static string Token(Dictionary<string, object> claims, SecurityKey? key = null, DateTime? expires = null, string? audience = null)
    {
        return new JsonWebTokenHandler().CreateToken(Descriptor(claims, new SigningCredentials(key ?? TenantKey, SecurityAlgorithms.RsaSha256), expires ?? DateTime.UtcNow.AddMinutes(30), audience ?? Personas.Audience));
    }

    private static SecurityTokenDescriptor Descriptor(Dictionary<string, object> claims, SigningCredentials? credentials, DateTime expires, string audience)
    {
        return new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            IssuedAt = expires.AddMinutes(-60),
            NotBefore = expires.AddMinutes(-60),
            Expires = expires,
            Claims = claims,
            SigningCredentials = credentials,
        };
    }

    private static Action<HttpRequestMessage> Bearer(string token)
    {
        return request => request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
    }

    // A started host for a test that only reads its services.
    private sealed class HostScope : IDisposable
    {
        public HostScope(HostSettings settings)
        {
            settings.Seed = false;
            Host = ProductTestHost.Start(settings);
            _ = Host.Client;
            _ = Host.Services;
        }

        public ProductTestHost Host { get; }

        public void Dispose()
        {
            Host.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
