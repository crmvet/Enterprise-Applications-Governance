using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Identity.Client;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Product.Azure.Host;
using Product.Engine.Core.Contract;
using Product.Mcp.Core.Backend;
using Xunit;

namespace Product.Azure.Tests;

// The on-behalf-of exchange on the production sign-in path: Microsoft Entra ID JWT bearer in the Production environment
// with a test signing key, Microsoft.Identity.Web token acquisition with a signed assertion file as the API app's
// credential, and a fake Entra token endpoint behind MSAL's HTTP client factory. The person's token goes only to the
// token endpoint, as the user assertion of a jwt-bearer grant with requested_token_use on_behalf_of and a client
// assertion, never a client secret; Microsoft Graph and the Power Platform API get only the new tokens. No tenant, no
// network.
public sealed class OnBehalfOfTests
{
    private const string PowerPlatformAdministrator = "11648597-926c-4cf3-9c36-bcebb0ba8dcc";
    private static readonly string Issuer = "https://login.microsoftonline.com/" + Personas.Tenant + "/v2.0";
    private static readonly RsaSecurityKey TenantKey = new(RSA.Create(2048)) { KeyId = "obo-test-tenant-key" };

    [Fact]
    public async Task GraphGetsOnlyTheExchangedTokenAndEntraGetsTheIncomingTokenAsTheUserAssertion()
    {
        using var scratch = new Scratch();
        var entra = new FakeEntra();
        RecordedHttpHandler graph = RecordedHttpHandler.Create();
        await using ProductTestHost host = ProductTestHost.Start(Production(scratch, entra, graph));
        string incoming = Token(Owen());

        JsonNode holders = await CallTool(host, incoming, "get_role_holders", "{\"roleKey\":\"platform-admin\"}");
        JsonNode again = await CallTool(host, incoming, "get_role_holders", "{\"roleKey\":\"platform-admin\"}");

        Assert.True(holders["ok"]!.GetValue<bool>(), holders.ToJsonString() + "\n" + host.Logs);
        Assert.True(again["ok"]!.GetValue<bool>(), again.ToJsonString());
        Assert.Equal(4, holders["data"]!["members"]!.AsArray().Count);

        // One exchange for Graph; the second call is served from the token cache.
        TokenRequest exchange = Assert.Single(entra.TokenRequests);
        Assert.Equal("https://login.microsoftonline.com/" + Personas.Tenant + "/oauth2/v2.0/token", exchange.Url);
        Assert.Equal("urn:ietf:params:oauth:grant-type:jwt-bearer", exchange.Form["grant_type"]);
        Assert.Equal("on_behalf_of", exchange.Form["requested_token_use"]);
        Assert.Equal(incoming, exchange.Form["assertion"]);
        Assert.Equal(Personas.Audience, exchange.Form["client_id"]);
        Assert.Equal("urn:ietf:params:oauth:client-assertion-type:jwt-bearer", exchange.Form["client_assertion_type"]);
        Assert.Equal(scratch.SignedAssertion, exchange.Form["client_assertion"]);
        Assert.False(exchange.Form.ContainsKey("client_secret"));
        Assert.Contains(DirectoryOptions.GraphScope, exchange.Form["scope"].Split(' '));

        // Graph saw only the exchanged token; the incoming token went nowhere but the token endpoint.
        Assert.NotEmpty(graph.Requests);
        Assert.All(graph.Requests, r => Assert.Equal("Bearer " + FakeEntra.GraphToken, r.Authorization));
        Assert.DoesNotContain(graph.Requests, r => (r.Authorization ?? string.Empty).Contains(incoming, StringComparison.Ordinal) || (r.Body ?? string.Empty).Contains(incoming, StringComparison.Ordinal));
        Assert.DoesNotContain(incoming, host.Logs.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(FakeEntra.GraphToken, host.Logs.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThePowerPlatformApiGetsItsOwnExchangedToken()
    {
        using var scratch = new Scratch();
        var entra = new FakeEntra();
        RecordedHttpHandler recorded = RecordedHttpHandler.Create();
        await using ProductTestHost host = ProductTestHost.Start(Production(scratch, entra, recorded));
        string incoming = Token(Owen());

        JsonNode gapCheck = await CallTool(host, incoming, "run_gap_check", "{}");

        Assert.True(gapCheck["ok"]!.GetValue<bool>(), gapCheck.ToJsonString() + "\n" + host.Logs);
        Assert.Equal("Power Platform administrator", gapCheck["data"]!["adminRoleUsed"]!.GetValue<string>());
        TokenRequest exchange = Assert.Single(entra.TokenRequests);
        Assert.Equal(incoming, exchange.Form["assertion"]);
        Assert.Contains(DirectoryOptions.PowerPlatformScope, exchange.Form["scope"].Split(' '));
        SeenRequest query = Assert.Single(recorded.Requests);
        Assert.Equal("Bearer " + FakeEntra.PowerPlatformToken, query.Authorization);
    }

    [Fact]
    public async Task MissingAdminConsentAnswersUpstreamUnavailableWithTheFix()
    {
        using var scratch = new Scratch();
        var entra = new FakeEntra { RefuseWithConsentRequired = true };
        RecordedHttpHandler graph = RecordedHttpHandler.Create();
        await using ProductTestHost host = ProductTestHost.Start(Production(scratch, entra, graph));

        JsonNode holders = await CallTool(host, Token(Owen()), "get_role_holders", "{\"roleKey\":\"platform-admin\"}");

        Assert.False(holders["ok"]!.GetValue<bool>());
        Assert.Equal("upstream_unavailable", holders["error"]!["code"]!.GetValue<string>());
        string message = holders["error"]!["message"]!.GetValue<string>();
        Assert.Contains("has not granted consent", message, StringComparison.Ordinal);
        Assert.Contains("GroupMember.Read.All", message, StringComparison.Ordinal);
        Assert.DoesNotContain("AADSTS65001", message, StringComparison.Ordinal);
        Assert.Single(entra.TokenRequests);
        Assert.Empty(graph.Requests);
    }

    [Fact]
    public async Task WithTheModeOffTheEntraPathIsUnchangedAndKeepsNoToken()
    {
        using var scratch = new Scratch();
        var entra = new FakeEntra();
        RecordedHttpHandler graph = RecordedHttpHandler.Create();
        HostSettings settings = Production(scratch, entra, graph);
        settings.Settings["Product:Directory:Mode"] = "Off";
        await using ProductTestHost host = ProductTestHost.Start(settings);

        JsonNode whoami = await CallTool(host, Token(Owen()), "whoami", "{}");
        JsonNode holders = await CallTool(host, Token(Owen()), "get_role_holders", "{\"roleKey\":\"platform-admin\"}");

        Assert.True(whoami["ok"]!.GetValue<bool>(), whoami.ToJsonString());
        Assert.Equal("upstream_unavailable", holders["error"]!["code"]!.GetValue<string>());
        Assert.False(host.Get<Microsoft.Extensions.Options.IOptionsMonitor<JwtBearerOptions>>().Get(ProductAuthenticationDefaults.EntraScheme).TokenValidationParameters.SaveSigninToken);
        Assert.Empty(entra.TokenRequests);
        Assert.Empty(graph.Requests);
    }

    [Fact]
    public void TheTokenSourceRefusesAnyoneButTheSignedInPersonBeforeAskingEntra()
    {
        var source = new OnBehalfOfTokenSource(new HttpContextAccessor(), NullLogger<OnBehalfOfTokenSource>.Instance);
        BackendCaller owen = new(Personas.OwenId, PrincipalKind.User, Personas.ClaudeCode);
        Assert.Contains("no signed-in request", source.ForUser(DownstreamApi.MicrosoftGraph, owen, CancellationToken.None).Problem, StringComparison.Ordinal);

        Dictionary<string, object> person = Owen();
        var cases = new (string Name, HttpContext Context, BackendCaller Caller, string Expected)[]
        {
            ("app-only", Context("Production", Principal(new Dictionary<string, object> { ["oid"] = Personas.RunnerId, ["sub"] = Personas.RunnerId, ["tid"] = Personas.Tenant, ["azp"] = "00000000-0000-4000-8000-000000000311", ["idtyp"] = "app", ["roles"] = "Approval.Write" })), new BackendCaller(Personas.RunnerId, PrincipalKind.User, "00000000-0000-4000-8000-000000000311"), "app-only caller"),
            ("development identity", Context("Development", Principal(With(person, ("product_dev_identity", "true")), ProductAuthenticationDefaults.DevelopmentScheme)), owen, "development identity"),
            ("another person", Context("Production", Principal(person)), new BackendCaller(Personas.PriyaId, PrincipalKind.User, Personas.ClaudeCode), "not the person signed in"),
            ("agent user", Context("Production", Principal(With(person, ("oid", Personas.AgentUserId)))), new BackendCaller(Personas.AgentUserId, PrincipalKind.AgentUser, Personas.ClaudeCode), "Agent identities, agent users and workloads never do"),
            ("password claims", Context("Production", Principal(With(person, ("xms_username", "fictional"), ("xms_password", "fictional")))), owen, "claims the product does not accept"),
            ("no bearer token", Context("Production", Principal(person), bearer: false), owen, "no bearer token"),
            ("demo persona", AsDemoPersona(Context("Demo", Principal(person))), new BackendCaller(Personas.NadiaId, PrincipalKind.User, Personas.WebApp), "A demo persona is fictional"),
        };

        foreach ((string name, HttpContext context, BackendCaller caller, string expected) in cases)
        {
            var scoped = new OnBehalfOfTokenSource(new HttpContextAccessor { HttpContext = context }, NullLogger<OnBehalfOfTokenSource>.Instance);
            DownstreamToken token = scoped.ForUser(DownstreamApi.MicrosoftGraph, caller, CancellationToken.None);
            Assert.False(token.Issued, name);
            Assert.True(token.Problem!.Contains(expected, StringComparison.Ordinal), name + ": " + token.Problem);
        }
    }

    [Fact]
    public void AnExplainedFailureNamesTheFixAndNeverQuotesEntra()
    {
        var consent = new MsalUiRequiredException("invalid_grant", "AADSTS65001: fictional consent text");
        var credential = new MsalServiceException("invalid_client", "AADSTS700213: fictional credential text");
        var stronger = new MsalUiRequiredException("invalid_grant", "AADSTS50076: fictional multifactor text");

        string forConsent = OnBehalfOfTokenSource.Explain(DownstreamApi.PowerPlatform, consent, CancellationToken.None);
        string forCredential = OnBehalfOfTokenSource.Explain(DownstreamApi.MicrosoftGraph, credential, CancellationToken.None);
        string forStronger = OnBehalfOfTokenSource.Explain(DownstreamApi.MicrosoftGraph, stronger, CancellationToken.None);
        string forTimeout = OnBehalfOfTokenSource.Explain(DownstreamApi.MicrosoftGraph, new OperationCanceledException(), CancellationToken.None);

        Assert.Contains("ResourceQuery.Resources.Read", forConsent, StringComparison.Ordinal);
        Assert.Contains("federated identity credential", forCredential, StringComparison.Ordinal);
        Assert.Contains("multifactor authentication", forStronger, StringComparison.Ordinal);
        Assert.Contains("in time", forTimeout, StringComparison.Ordinal);
        Assert.All(new[] { forConsent, forCredential, forStronger }, m => Assert.DoesNotContain("fictional", m, StringComparison.Ordinal));
    }

    private static HostSettings Production(Scratch scratch, FakeEntra entra, RecordedHttpHandler downstream)
    {
        var settings = new HostSettings { Environment = "Production", DevIdentity = false, Inventory = false };
        settings.Settings["Product:Auth:ClientId"] = Personas.Audience;
        settings.Settings["Product:Directory:Mode"] = "Live";
        settings.Settings["Product:Directory:Credential:SourceType"] = DirectoryCredentialOptions.FilePath;
        settings.Settings["Product:Directory:Credential:SignedAssertionFilePath"] = scratch.AssertionFile;
        settings.Services = services =>
        {
            services.PostConfigure<JwtBearerOptions>(ProductAuthenticationDefaults.EntraScheme, options =>
            {
                // The tenant's issuer metadata, in memory: its issuer and the test signing key.
                var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                configuration.SigningKeys.Add(TenantKey);
                options.Configuration = configuration;
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                options.TokenValidationParameters.ValidIssuers = new[] { Issuer };
            });
            services.AddProductDirectoryReads();
            services.Replace(ServiceDescriptor.Singleton<IMsalHttpClientFactory>(entra));
            services.AddHttpClient(DirectoryHttp.ClientName).ConfigurePrimaryHttpMessageHandler(() => downstream);
        };
        return settings;
    }

    private static async Task<JsonNode> CallTool(ProductTestHost host, string token, string tool, string argumentsJson)
    {
        string body = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"" + tool + "\",\"arguments\":" + argumentsJson + "}}";
        HttpCall call = await host.Send(HttpMethod.Post, "/mcp", null, body, r => r.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token));
        Assert.True(call.Status == 200, tool + " answered " + call + "\n" + host.Logs);
        return call.Json?["result"]?["structuredContent"] ?? throw new InvalidOperationException("No structured content from " + tool + ": " + call.Body);
    }

    // Owen, a platform admin, signed in through Claude Code, with the Power Platform Administrator directory role.
    private static Dictionary<string, object> Owen()
    {
        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["oid"] = Personas.OwenId,
            ["sub"] = "pairwise-subject-0213",
            ["tid"] = Personas.Tenant,
            ["azp"] = Personas.ClaudeCode,
            ["scp"] = "mcp.access api.access",
            ["groups"] = new[] { Personas.PlatformAdminGroup },
            ["wids"] = new[] { PowerPlatformAdministrator },
            ["idtyp"] = "user",
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

    private static string Token(Dictionary<string, object> claims)
    {
        DateTime expires = DateTime.UtcNow.AddMinutes(30);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Personas.Audience,
            IssuedAt = expires.AddMinutes(-60),
            NotBefore = expires.AddMinutes(-60),
            Expires = expires,
            Claims = claims,
            SigningCredentials = new SigningCredentials(TenantKey, SecurityAlgorithms.RsaSha256),
        });
    }

    private static ClaimsPrincipal Principal(Dictionary<string, object> claims, string authenticationType = "AuthenticationTypes.Federation")
    {
        var list = new List<Claim>();
        foreach (KeyValuePair<string, object> claim in claims)
        {
            if (claim.Value is string[] values)
            {
                list.AddRange(values.Select(v => new Claim(claim.Key, v)));
            }
            else
            {
                list.Add(new Claim(claim.Key, claim.Value.ToString()!));
            }
        }

        return new ClaimsPrincipal(new ClaimsIdentity(list, authenticationType, "name", "roles"));
    }

    // A request's context as the pipeline leaves it: the signed-in principal, the caller mapper, and a bearer header.
    private static HttpContext Context(string environment, ClaimsPrincipal user, bool bearer = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new FixedEnvironment(environment));
        services.AddSingleton<ICallerMapper, CallerMapper>();
        var context = new DefaultHttpContext { User = user, RequestServices = services.BuildServiceProvider() };
        if (bearer)
        {
            context.Request.Headers.Authorization = "Bearer fictional-incoming-token";
        }

        return context;
    }

    // The same request after the demo persona switch (D-39) put Nadia in place of the signed-in operator: the bearer
    // token is still the operator's own, so it must never be exchanged for the persona.
    private static HttpContext AsDemoPersona(HttpContext context)
    {
        CallerClaims signedIn = new CallerMapper(new FixedEnvironment("Demo")).Map(context.User)!;
        ProductRequest.SetCaller(context, signedIn.ForDemoPersona(new ProductDemoPersona
        {
            Name = "nadia",
            Label = "Nadia Ferreira, application owner",
            ObjectId = Personas.NadiaId,
            Groups = new[] { Personas.OwnerGroup },
        }));
        return context;
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

    // The API app's credential for the test: a signed assertion in a file (Microsoft.Identity.Web's SignedAssertionFilePath
    // source, as Kubernetes workload identity projects it). A fictional, unsigned JWT; the fake token endpoint only
    // checks that it arrives as the client assertion.
    private sealed class Scratch : IDisposable
    {
        public Scratch()
        {
            Folder = TestPaths.NewScratchFolder();
            AssertionFile = Path.Combine(Folder, "signed-assertion.jwt");
            DateTime expires = DateTime.UtcNow.AddHours(1);
            SignedAssertion = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = "https://fictional-issuer.example.org",
                Audience = "api://AzureADTokenExchange",
                Subject = new ClaimsIdentity(new[] { new Claim("sub", "fictional-workload") }),
                IssuedAt = expires.AddHours(-2),
                NotBefore = expires.AddHours(-2),
                Expires = expires,
            });
            File.WriteAllText(AssertionFile, SignedAssertion);
        }

        public string Folder { get; }

        public string AssertionFile { get; }

        public string SignedAssertion { get; }

        public void Dispose()
        {
            TestPaths.TryDelete(Folder);
        }
    }

    // One request to the fake token endpoint, with its form fields.
    private sealed class TokenRequest
    {
        public TokenRequest(string url, Dictionary<string, string> form)
        {
            Url = url;
            Form = form;
        }

        public string Url { get; }

        public Dictionary<string, string> Form { get; }
    }

    // Microsoft Entra ID's token endpoint as MSAL reaches it through its HTTP client factory: instance discovery and the
    // OIDC document for any authority, and an access token for each on-behalf-of request (a fictional opaque value per
    // resource). With RefuseWithConsentRequired it answers as Entra does when no admin consented (AADSTS65001).
    private sealed class FakeEntra : HttpMessageHandler, IMsalHttpClientFactory
    {
        public const string GraphToken = "issued-graph-token-0001";
        public const string PowerPlatformToken = "issued-power-platform-token-0001";

        private readonly ConcurrentQueue<TokenRequest> _tokenRequests = new();
        private readonly HttpClient _client;

        public FakeEntra()
        {
            _client = new HttpClient(this, disposeHandler: false);
        }

        public bool RefuseWithConsentRequired { get; set; }

        public IReadOnlyList<TokenRequest> TokenRequests => _tokenRequests.ToArray();

        public HttpClient GetHttpClient()
        {
            return _client;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.GetLeftPart(UriPartial.Path);
            if (request.Method == HttpMethod.Post && url.EndsWith("/oauth2/v2.0/token", StringComparison.Ordinal))
            {
                string body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
                Dictionary<string, string> form = body.Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Split('=', 2))
                    .ToDictionary(p => Uri.UnescapeDataString(p[0].Replace('+', ' ')), p => p.Length > 1 ? Uri.UnescapeDataString(p[1].Replace('+', ' ')) : string.Empty, StringComparer.Ordinal);
                _tokenRequests.Enqueue(new TokenRequest(url, form));
                if (RefuseWithConsentRequired)
                {
                    return Json(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\",\"error_description\":\"AADSTS65001: The user or administrator has not consented to use the application. Fictional.\",\"error_codes\":[65001],\"suberror\":\"consent_required\",\"correlation_id\":\"00000000-0000-4000-8000-0000000000e1\"}");
                }

                bool graph = form.TryGetValue("scope", out string? scope) && scope.Contains("graph.microsoft.com", StringComparison.Ordinal);
                string clientInfo = Base64Url("{\"uid\":\"" + Personas.OwenId + "\",\"utid\":\"" + Personas.Tenant + "\"}");
                return Json(HttpStatusCode.OK, "{\"token_type\":\"Bearer\",\"scope\":\"" + (graph ? DirectoryOptions.GraphScope : DirectoryOptions.PowerPlatformScope) + "\",\"expires_in\":3599,\"ext_expires_in\":3599,\"access_token\":\"" + (graph ? GraphToken : PowerPlatformToken) + "\",\"client_info\":\"" + clientInfo + "\"}");
            }

            if (url.EndsWith("/discovery/instance", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, "{\"tenant_discovery_endpoint\":\"https://login.microsoftonline.com/" + Personas.Tenant + "/v2.0/.well-known/openid-configuration\",\"api-version\":\"1.1\",\"metadata\":[{\"preferred_network\":\"login.microsoftonline.com\",\"preferred_cache\":\"login.windows.net\",\"aliases\":[\"login.microsoftonline.com\",\"login.windows.net\",\"login.microsoft.com\",\"sts.windows.net\"]}]}");
            }

            if (url.EndsWith("/.well-known/openid-configuration", StringComparison.Ordinal))
            {
                string authority = "https://login.microsoftonline.com/" + Personas.Tenant;
                return Json(HttpStatusCode.OK, "{\"token_endpoint\":\"" + authority + "/oauth2/v2.0/token\",\"authorization_endpoint\":\"" + authority + "/oauth2/v2.0/authorize\",\"issuer\":\"" + Issuer + "\"}");
            }

            return Json(HttpStatusCode.NotFound, "{\"error\":\"not_found\"}");
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string json)
        {
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }

        private static string Base64Url(string text)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}
