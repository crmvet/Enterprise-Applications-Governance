using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Product.Azure.Host;

namespace Product.Azure.Server.DevIdentity;

/// <summary>
/// The local development identity: a request names a fictional persona in a header, and the server signs it in with the
/// same claim types an Entra token carries, so everything after sign-in is the production path. It exists only in the
/// local server (the Azure Functions app does not reference this project) and works only when all three hold: the
/// environment is Development, <c>Product:DevIdentity:Enabled</c> is true (false in every committed file; set it on the
/// command line or in an environment variable for the run), and the request comes from a loopback address with a loopback
/// host name and no forwarding header. When the switch is on in any other environment, or the server listens on a
/// non-loopback address, startup fails.
/// </summary>
public sealed class DevIdentityOptions : AuthenticationSchemeOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Product:DevIdentity";

    /// <summary>The authentication scheme name.</summary>
    public const string SchemeName = ProductAuthenticationDefaults.DevelopmentScheme;

    /// <summary>The switch. False unless set for the run.</summary>
    public bool Enabled { get; set; }

    /// <summary>The header that names the persona.</summary>
    public string HeaderName { get; set; } = "X-Product-Dev-Identity";

    /// <summary>The fictional personas, by name.</summary>
    public Dictionary<string, DevPersona> Personas { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The persona with the name, compared without case, or null.</summary>
    public DevPersona? Find(string name)
    {
        return Personas.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
    }
}

/// <summary>One fictional persona: the claims an Entra token for it would carry.</summary>
public sealed class DevPersona
{
    /// <summary>The persona's object ID (<c>oid</c>).</summary>
    public string ObjectId { get; set; } = string.Empty;

    /// <summary>The tenant ID (<c>tid</c>).</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>The client app ID (<c>azp</c>), such as the Known Client ID of Claude Code in the demo configuration.</summary>
    public string ClientAppId { get; set; } = string.Empty;

    /// <summary>The engine principal kind: <c>user</c>, <c>agent-user</c> or <c>agent-identity</c> for a delegated persona.</summary>
    public string PrincipalKind { get; set; } = "user";

    /// <summary>The group object IDs (<c>groups</c>).</summary>
    public string[] Groups { get; set; } = Array.Empty<string>();

    /// <summary>The delegated scopes (<c>scp</c>). Ignored for an app-only persona.</summary>
    public string[] Scopes { get; set; } = { "mcp.access", "api.access" };

    /// <summary>True for an app-only persona (a pipeline or the approval runner).</summary>
    public bool AppOnly { get; set; }

    /// <summary>The app roles (<c>roles</c>), such as <c>Deployment.Record</c>.</summary>
    public string[] AppRoles { get; set; } = Array.Empty<string>();

    /// <summary>Directory role template IDs (<c>wids</c>).</summary>
    public string[] DirectoryRoleIds { get; set; } = Array.Empty<string>();
}

/// <summary>Signs a persona in, after checking the three conditions on every request.</summary>
public sealed class DevIdentityHandler : AuthenticationHandler<DevIdentityOptions>
{
    private readonly IHostEnvironment _environment;

    /// <summary>Creates the handler.</summary>
    public DevIdentityHandler(IOptionsMonitor<DevIdentityOptions> options, ILoggerFactory logger, UrlEncoder encoder, IHostEnvironment environment)
        : base(options, logger, encoder)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Options.HeaderName, out var values) || string.IsNullOrWhiteSpace(values.ToString()))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!_environment.IsDevelopment())
        {
            return Task.FromResult(AuthenticateResult.Fail("The development identity works only in the Development environment."));
        }

        if (!Options.Enabled)
        {
            return Task.FromResult(AuthenticateResult.Fail("The development identity is switched off (Product:DevIdentity:Enabled)."));
        }

        if (!DevIdentityGuard.IsLoopbackRequest(Context))
        {
            return Task.FromResult(AuthenticateResult.Fail("The development identity takes only loopback requests with a loopback host name and no forwarding header."));
        }

        DevPersona? persona = Options.Find(values.ToString().Trim());
        if (persona == null || string.IsNullOrWhiteSpace(persona.ObjectId) || string.IsNullOrWhiteSpace(persona.TenantId))
        {
            return Task.FromResult(AuthenticateResult.Fail("No development persona has that name."));
        }

        var claims = new List<Claim>
        {
            new(ProductClaimTypes.ObjectId, persona.ObjectId),
            new(ProductClaimTypes.Subject, persona.ObjectId),
            new(ProductClaimTypes.TenantId, persona.TenantId),
            new(ProductClaimTypes.AuthorizedParty, persona.ClientAppId),
            new(ProductClaimTypes.IdentityType, persona.AppOnly ? "app" : "user"),
            new(ProductClaimTypes.Version, "2.0"),
            new(ProductClaimTypes.DevIdentity, "true"),
            new(ProductClaimTypes.DevPrincipalKind, persona.PrincipalKind ?? "user"),
            new("name", values.ToString().Trim()),
        };
        if (!persona.AppOnly && persona.Scopes.Length > 0)
        {
            claims.Add(new Claim(ProductClaimTypes.Scope, string.Join(" ", persona.Scopes)));
        }

        claims.AddRange(persona.AppRoles.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => new Claim(ProductClaimTypes.Roles, r)));
        claims.AddRange(persona.Groups.Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => new Claim(ProductClaimTypes.Groups, g)));
        claims.AddRange(persona.DirectoryRoleIds.Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => new Claim(ProductClaimTypes.DirectoryRoles, w)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, DevIdentityOptions.SchemeName, "name", ProductClaimTypes.Roles));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, DevIdentityOptions.SchemeName)));
    }
}

/// <summary>Claims a request for the development identity when the switch is on and the request names a persona.</summary>
public sealed class DevIdentitySchemeSelector : IProductSchemeSelector
{
    private readonly IOptionsMonitor<DevIdentityOptions> _options;

    /// <summary>Creates the selector.</summary>
    public DevIdentitySchemeSelector(IOptionsMonitor<DevIdentityOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public string? SelectScheme(HttpContext context)
    {
        DevIdentityOptions options = _options.Get(DevIdentityOptions.SchemeName);
        return options.Enabled && context.Request.Headers.ContainsKey(options.HeaderName) ? DevIdentityOptions.SchemeName : null;
    }
}

/// <summary>The development identity's startup guard and request check.</summary>
public static class DevIdentityGuard
{
    // The headers a proxy adds, and the X-Original-* headers the forwarded headers middleware leaves behind after it
    // rewrote the remote address, host or scheme from them.
    private static readonly string[] ForwardingHeaders =
    {
        "X-Forwarded-For", "X-Forwarded-Host", "X-Forwarded-Proto", "X-Forwarded-Prefix", "Forwarded", "X-Real-IP",
        "X-Original-For", "X-Original-Host", "X-Original-Proto", "X-Original-Prefix",
    };

    /// <summary>
    /// Throws when the switch is on and the environment is not Development, or when the switch is on and the server is
    /// configured to listen on an address that is not loopback. Called before the host is built and again when the
    /// pipeline starts, with the final configuration.
    /// </summary>
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

        if (!configuration.GetValue<bool>(DevIdentityOptions.SectionName + ":Enabled"))
        {
            return;
        }

        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException("Product:DevIdentity:Enabled is true, but the environment is " + environment.EnvironmentName
                + ". The development identity runs only in the Development environment, on a loopback address. Turn the switch off.");
        }

        // ASPNETCORE_FORWARDEDHEADERS_ENABLED makes the host trust X-Forwarded-For from any sender and rewrite the remote
        // address from it: a server set up to run behind a proxy is not a local run.
        if (string.Equals(configuration["FORWARDEDHEADERS_ENABLED"], "true", StringComparison.OrdinalIgnoreCase) || string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_FORWARDEDHEADERS_ENABLED"), "true", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Product:DevIdentity:Enabled is true, but ASPNETCORE_FORWARDEDHEADERS_ENABLED makes the server trust forwarding headers. The development identity takes only direct loopback requests. Remove one of the two settings.");
        }

        foreach (string url in ConfiguredUrls(configuration))
        {
            if (!IsLoopbackUrl(url))
            {
                throw new InvalidOperationException("Product:DevIdentity:Enabled is true, but the server is configured to listen on " + url
                    + ". The development identity needs loopback addresses only, such as http://127.0.0.1:5080.");
            }
        }

        foreach (string? ports in new[] { configuration["http_ports"], configuration["https_ports"], Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS"), Environment.GetEnvironmentVariable("ASPNETCORE_HTTPS_PORTS") })
        {
            if (!string.IsNullOrWhiteSpace(ports) && ConfiguredUrls(configuration).Count == 0)
            {
                throw new InvalidOperationException("Product:DevIdentity:Enabled is true, but HTTP_PORTS or HTTPS_PORTS makes the server listen on every address. Pass --urls http://127.0.0.1:<port> instead.");
            }
        }
    }

    /// <summary>
    /// True when the request comes from a loopback address, names a loopback host and carries no forwarding header. A
    /// request with no remote address (an in-memory test server) is not loopback.
    /// </summary>
    public static bool IsLoopbackRequest(HttpContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        IPAddress? remote = context.Connection.RemoteIpAddress;
        if (remote == null || !IPAddress.IsLoopback(remote))
        {
            return false;
        }

        if (ForwardingHeaders.Any(h => context.Request.Headers.ContainsKey(h)))
        {
            return false;
        }

        return IsLoopbackHost(context.Request.Host.Host);
    }

    private static List<string> ConfiguredUrls(IConfiguration configuration)
    {
        var urls = new List<string>();
        foreach (string? value in new[] { configuration["urls"], configuration["ASPNETCORE_URLS"], Environment.GetEnvironmentVariable("ASPNETCORE_URLS") })
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                urls.AddRange(value!.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(u => u.Trim()));
            }
        }

        foreach (IConfigurationSection endpoint in configuration.GetSection("Kestrel:Endpoints").GetChildren())
        {
            string? url = endpoint["Url"];
            if (!string.IsNullOrWhiteSpace(url))
            {
                urls.Add(url!.Trim());
            }
        }

        return urls.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsLoopbackUrl(string url)
    {
        int scheme = url.IndexOf("://", StringComparison.Ordinal);
        string rest = scheme < 0 ? url : url.Substring(scheme + 3);
        string host;
        if (rest.StartsWith("[", StringComparison.Ordinal))
        {
            int close = rest.IndexOf(']');
            host = close < 0 ? rest : rest.Substring(0, close + 1);
        }
        else
        {
            int end = rest.IndexOfAny(new[] { ':', '/' });
            host = end < 0 ? rest : rest.Substring(0, end);
        }

        return IsLoopbackHost(host);
    }

    private static bool IsLoopbackHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        string h = host!.Trim();
        return string.Equals(h, "localhost", StringComparison.OrdinalIgnoreCase)
            || h == "127.0.0.1"
            || h == "[::1]"
            || h == "::1";
    }
}

/// <summary>Registration of the development identity in the local server.</summary>
public static class DevIdentityServiceCollectionExtensions
{
    /// <summary>
    /// Registers the development identity scheme, its selector and its startup checks. The scheme refuses every request
    /// unless all three conditions hold; the startup checks fail the host when the switch is on outside Development or on
    /// a non-loopback address.
    /// </summary>
    public static IServiceCollection AddDevIdentity(this IServiceCollection services, IConfiguration configuration)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        services.AddAuthentication().AddScheme<DevIdentityOptions, DevIdentityHandler>(DevIdentityOptions.SchemeName, null);
        services.AddOptions<DevIdentityOptions>(DevIdentityOptions.SchemeName).Bind(configuration.GetSection(DevIdentityOptions.SectionName));
        services.AddSingleton<IProductSchemeSelector, DevIdentitySchemeSelector>();
        services.AddSingleton<IStartupFilter, DevIdentityStartupCheck>();
        return services;
    }
}

// Runs the guard again when the pipeline starts, with the final configuration, so a setting added after the host was
// built (by a test host, for example) cannot slip past it.
internal sealed class DevIdentityStartupCheck : IStartupFilter
{
    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;

    public DevIdentityStartupCheck(IHostEnvironment environment, IConfiguration configuration)
    {
        _environment = environment;
        _configuration = configuration;
    }

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        DevIdentityGuard.Validate(_environment, _configuration);
        return next;
    }
}
