using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Product.Engine.Core.Canonical;
using Product.Mcp.Core.Json;

namespace Product.Azure.Host;

/// <summary>
/// Settings of the product's web app, bound from <c>Product:WebApp</c>: where its built files are, which client IDs are
/// the web app's own (the <c>azp</c> of its tokens), and what its sign-in asks for. None of them is a secret, and none is
/// product configuration.
/// </summary>
public sealed class ProductWebAppOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Product:WebApp";

    /// <summary>The path the web app is served under, on the same origin as the API.</summary>
    public const string BasePath = "/app";

    /// <summary>Serves the web app's files and <c>/app/config.json</c>. Off by default: every <c>/app</c> path answers 404.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The folder of the built web app (<c>npm run build:app</c> writes <c>src/ui/harness/dist/app</c>). Relative to the
    /// content root when the folder exists there, else to the application base directory. Default <c>wwwroot/app</c>,
    /// where the Functions package carries it.
    /// </summary>
    public string RootPath { get; set; } = "wwwroot/app";

    /// <summary>
    /// The web app's own client IDs. A delegated token whose <c>azp</c> is one of them is the person acting through the
    /// product's page (the person entry); any other client is an AI client acting for the person. Read on every host,
    /// whether or not this host serves the files.
    /// </summary>
    public string[] ClientIds { get; set; } = Array.Empty<string>();

    /// <summary>The tenant the web app signs people in to. Empty means <c>Product:Auth:TenantId</c>.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// The scope the web app asks for, such as <c>api://&lt;API client ID&gt;/api.access</c>. Empty means
    /// <c>api://</c> plus <c>Product:Auth:ClientId</c> plus <c>/</c> plus <c>Product:Auth:ApiScope</c>.
    /// </summary>
    public string ApiScope { get; set; } = string.Empty;

    /// <summary>
    /// True on a demo install whose store holds the fictional demo estate, seeded past the engine (D-33): the page shows a
    /// "Demo, fictional data" badge on every page. Never set it on a real install.
    /// </summary>
    public bool Demo { get; set; }

    /// <summary>The Client App label stamped on the rows a person writes through the web app.</summary>
    public string ClientLabel { get; set; } = "Web app";

    /// <summary>
    /// The step-up sign-in a person's approval answer needs (D-38; document 26, B26-9): Off by default and in the
    /// demo; FreshSignIn or AuthenticationContext on an install that wants it. Read by <c>answer_approval</c> only.
    /// </summary>
    public ProductStepUpOptions StepUp { get; set; } = new();

    /// <summary>
    /// The fictional personas the page offers when the local server's development identity is on (Development only).
    /// Each names a development identity persona whose client app ID is one of <see cref="ClientIds"/>.
    /// </summary>
    public List<ProductWebAppPersona> DevelopmentPersonas { get; set; } = new();

    /// <summary>
    /// The fictional personas a demo operator may act as (D-39), in the Demo environment only: the person signed in to
    /// the web app sends <see cref="ProductDemo.PersonaHeader"/> with a persona's name, and the request runs as that
    /// persona, stamped "demo operator". A host that names any outside the Demo environment refuses to start.
    /// </summary>
    public List<ProductDemoPersona> DemoPersonas { get; set; } = new();

    /// <summary>
    /// A JSON file that lists the demo personas instead (<c>{ "personas": [ ... ] }</c>), such as the fictional
    /// <c>seed/demo-personas.json</c> both hosts carry. Relative to the content root when the file is there, else to the
    /// application base directory. Read in the Demo environment only; refused anywhere else, as the inline list is.
    /// </summary>
    public string DemoPersonasFile { get; set; } = string.Empty;

    /// <summary>The personas read from <see cref="DemoPersonasFile"/> when the host started in the Demo environment.</summary>
    internal List<ProductDemoPersona> FileDemoPersonas { get; } = new();

    /// <summary>Why <see cref="DemoPersonasFile"/> could not be read, or null.</summary>
    internal string? DemoPersonasFileProblem { get; set; }

    /// <summary>Every demo persona: the inline list and the file's.</summary>
    public IReadOnlyList<ProductDemoPersona> AllDemoPersonas => DemoPersonas.Concat(FileDemoPersonas).ToList();

    /// <summary>The Client App label stamped on the rows a demo persona writes: the web app's label and "demo operator".</summary>
    public string DemoOperatorLabel => ClientLabel + ProductDemo.OperatorLabelSuffix;

    /// <summary>True when the client ID is one of the web app's own.</summary>
    public bool IsWebAppClient(string? clientAppId)
    {
        return !string.IsNullOrWhiteSpace(clientAppId) && ClientIds.Any(id => string.Equals(id?.Trim(), clientAppId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The demo persona with the name, compared without case, or null.</summary>
    public ProductDemoPersona? FindDemoPersona(string? name)
    {
        return string.IsNullOrWhiteSpace(name) ? null : AllDemoPersonas.FirstOrDefault(p => string.Equals(p.Name, name!.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The Client App label of a person's request through the web app: <see cref="DemoOperatorLabel"/> when a demo persona
    /// acts (D-39), else <see cref="ClientLabel"/>.
    /// </summary>
    public string PersonLabel(CallerClaims? caller)
    {
        return caller?.DemoPersona != null ? DemoOperatorLabel : ClientLabel;
    }
}

/// <summary>One development persona the page's sign-in offers.</summary>
public sealed class ProductWebAppPersona
{
    /// <summary>The development identity persona name, such as <c>nadia-web</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>What the page shows.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// A login of the test harnesses (a11y.mjs, screenshots.mjs), not a demo persona. The sign-in page lists it only with
    /// <c>?all=1</c>, so a presenter sees exactly the seventeen demo personas (product review, fix round 1; wave 4b). It still signs in.
    /// </summary>
    public bool Harness { get; set; }
}

/// <summary>Startup checks of <see cref="ProductWebAppOptions"/>.</summary>
public sealed class ProductWebAppOptionsValidator : IValidateOptions<ProductWebAppOptions>
{
    private readonly IHostEnvironment _environment;
    private readonly IOptions<ProductAuthOptions> _auth;

    /// <summary>Creates the validator.</summary>
    public ProductWebAppOptionsValidator(IHostEnvironment environment, IOptions<ProductAuthOptions> auth)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _auth = auth ?? throw new ArgumentNullException(nameof(auth));
    }

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ProductWebAppOptions options)
    {
        var failures = new List<string>();
        failures.AddRange((options.StepUp ?? new ProductStepUpOptions()).Problems());
        if (options.ClientIds.Any(id => !Guid.TryParse(id, out _)))
        {
            failures.Add("Product:WebApp:ClientIds holds the web app registration's client IDs, each a GUID.");
        }

        if (options.ClientIds.Any(id => string.Equals(id?.Trim(), _auth.Value.ClientId, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(id)))
        {
            failures.Add("Product:WebApp:ClientIds must not name the API's own client ID: the web app is a separate app registration.");
        }

        if (!string.IsNullOrWhiteSpace(options.TenantId) && !Guid.TryParse(options.TenantId, out _))
        {
            failures.Add("Product:WebApp:TenantId must be the tenant's ID (a GUID), or empty for Product:Auth:TenantId.");
        }

        if (!string.IsNullOrWhiteSpace(options.ApiScope) && (options.ApiScope.Any(char.IsWhiteSpace) || options.ApiScope.IndexOf('/') < 0))
        {
            failures.Add("Product:WebApp:ApiScope is one scope, such as api://<API client ID>/api.access.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientLabel))
        {
            failures.Add("Product:WebApp:ClientLabel must not be empty.");
        }

        if (options.Enabled)
        {
            if (string.IsNullOrWhiteSpace(options.RootPath))
            {
                failures.Add("Product:WebApp:RootPath names the folder of the built web app.");
            }

            if (!_environment.IsDevelopment() && options.ClientIds.Length == 0)
            {
                failures.Add("Product:WebApp:Enabled needs the web app registration's client ID in Product:WebApp:ClientIds outside Development.");
            }

            if (options.DevelopmentPersonas.Count > 0 && !_environment.IsDevelopment())
            {
                failures.Add("Product:WebApp:DevelopmentPersonas works only in the Development environment.");
            }
        }

        // The demo persona switch (D-39) works on the API whether or not this host serves the page, so its settings are
        // checked either way.
        failures.AddRange(ProductDemo.Problems(options, _environment));

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

/// <summary>
/// Serves the product's web app on the same origin as the API, on both hosts: the built files under <c>/app/</c>, the
/// page itself for every page route (<c>/app/records/...</c> among them), and <c>/app/config.json</c>, written from this
/// host's settings so one build serves any tenant. No secret is in it. The page and the configuration are never cached;
/// hashed assets are cached for a year. Every page answer carries a Content-Security-Policy that allows only this origin
/// and the Microsoft Entra sign-in host.
/// </summary>
public static class ProductWebApp
{
    /// <summary>The configuration the page reads at startup.</summary>
    public const string ConfigPath = ProductWebAppOptions.BasePath + "/config.json";

    private const string ImmutableCache = "public, max-age=31536000, immutable";
    private const string RevalidateCache = "no-cache";

    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    /// <summary>
    /// Registers the web app's options (section <c>Product:WebApp</c>, checked at startup). <c>AddProduct</c> calls it.
    /// </summary>
    public static IServiceCollection AddProductWebApp(this IServiceCollection services, IConfiguration configuration)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        // The demo persona file is read once the configuration is final (a test host may add settings until the service
        // provider is built), and only in the Demo environment.
        services.AddOptions<ProductWebAppOptions>()
            .Bind(configuration.GetSection(ProductWebAppOptions.SectionName))
            .PostConfigure<IHostEnvironment>(ProductDemo.LoadFile)
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ProductWebAppOptions>, ProductWebAppOptionsValidator>());
        return services;
    }

    /// <summary>
    /// Maps <c>GET /app</c> (a redirect to <c>/app/</c>), <c>GET /app/config.json</c> and <c>GET /app/{**path}</c>. With
    /// the web app switched off every one answers 404.
    /// </summary>
    public static IEndpointRouteBuilder MapProductWebApp(this IEndpointRouteBuilder endpoints)
    {
        if (endpoints == null)
        {
            throw new ArgumentNullException(nameof(endpoints));
        }

        endpoints.MapGet(ProductWebAppOptions.BasePath, HandleBase);
        endpoints.MapGet(ConfigPath, HandleConfig);
        endpoints.MapGet(ProductWebAppOptions.BasePath + "/{**path}", HandleFile);
        return endpoints;
    }

    /// <summary>
    /// The Content-Security-Policy of the page: scripts and styles from this origin only (inline styles are allowed,
    /// because the Radix primitives under the shadcn/ui components write a style tag at run time when a dialog or popover
    /// locks page scrolling; a per-request nonce would remove that), calls and frames to this origin and the sign-in host only, no plug-ins,
    /// and no framing by any other page.
    /// </summary>
    public static string ContentSecurityPolicy(string signInOrigin)
    {
        string signIn = string.IsNullOrWhiteSpace(signInOrigin) ? string.Empty : " " + signInOrigin;
        return "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self'; font-src 'self'; connect-src 'self'" + signIn
            + "; frame-src 'self'" + signIn + "; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
    }

    /// <summary>
    /// The page's runtime configuration (<c>src/ui/harness/src/app/config.ts</c>, schema version 1): the API on this
    /// origin, and Microsoft Entra sign-in with the web app's client ID and the API scope. In a local Development run with
    /// the development identity switched on and personas named, the development sign-in instead. In the Demo environment,
    /// <c>demoPersonas</c> lists the personas a demo operator may act as (D-39); everywhere else the list is empty.
    /// </summary>
    public static JsonTreeNode Config(ProductWebAppOptions options, ProductAuthOptions auth, IHostEnvironment environment, IConfiguration configuration)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (auth == null)
        {
            throw new ArgumentNullException(nameof(auth));
        }

        bool development = IsDevelopmentSignIn(options, environment, configuration);
        JsonTreeNode signIn = development
            ? Js.Obj(("mode", JsonTreeNode.FromString("development")))
            : Js.Obj(
                ("mode", JsonTreeNode.FromString("entra")),
                ("authority", JsonTreeNode.FromString(Authority(options, auth))),
                ("clientId", JsonTreeNode.FromString(options.ClientIds.FirstOrDefault()?.Trim() ?? string.Empty)),
                ("apiScope", JsonTreeNode.FromString(ApiScope(options, auth))));
        IEnumerable<ProductWebAppPersona> personas = development ? options.DevelopmentPersonas : Enumerable.Empty<ProductWebAppPersona>();

        // The demo personas (D-39) only in the Demo environment, by name and label: the page sends the name in
        // X-Product-Demo-Persona. Their fictional IDs stay on the server.
        IEnumerable<ProductDemoPersona> demoPersonas = ProductDemo.IsDemo(environment) ? options.AllDemoPersonas : Enumerable.Empty<ProductDemoPersona>();
        return Js.Obj(
            ("schemaVersion", JsonTreeNode.FromInt64(1)),
            ("apiBase", JsonTreeNode.FromString("/api")),
            ("auth", signIn),
            ("demo", JsonTreeNode.FromBoolean(options.Demo)),
            ("development", Js.Obj(("personas", Js.Arr(personas.Select(p => Js.Obj(
                ("name", JsonTreeNode.FromString(p.Name)),
                ("label", JsonTreeNode.FromString(p.Label)),
                ("harness", p.Harness ? JsonTreeNode.True : null))))))),
            ("demoPersonas", Js.Arr(demoPersonas.Select(p => Js.Obj(
                ("name", JsonTreeNode.FromString(p.Name)),
                ("label", JsonTreeNode.FromString(p.Label)))))));
    }

    /// <summary>The sign-in authority: the Entra instance and the tenant.</summary>
    public static string Authority(ProductWebAppOptions options, ProductAuthOptions auth)
    {
        string tenant = string.IsNullOrWhiteSpace(options.TenantId) ? auth.TenantId : options.TenantId;
        return auth.Instance.TrimEnd('/') + "/" + (string.IsNullOrWhiteSpace(tenant) ? "organizations" : tenant.Trim());
    }

    /// <summary>The scope the web app asks for.</summary>
    public static string ApiScope(ProductWebAppOptions options, ProductAuthOptions auth)
    {
        return string.IsNullOrWhiteSpace(options.ApiScope) ? "api://" + auth.ClientId + "/" + auth.ApiScope : options.ApiScope.Trim();
    }

    // The folder of the built web app, or null when it is not there.
    internal static string? Root(ProductWebAppOptions options, IHostEnvironment environment)
    {
        if (string.IsNullOrWhiteSpace(options.RootPath))
        {
            return null;
        }

        IEnumerable<string> candidates = Path.IsPathRooted(options.RootPath)
            ? new[] { options.RootPath }
            : new[] { Path.Combine(environment.ContentRootPath, options.RootPath), Path.Combine(AppContext.BaseDirectory, options.RootPath) };
        return candidates.Select(Path.GetFullPath).FirstOrDefault(c => File.Exists(Path.Combine(c, "index.html")));
    }

    private static bool IsDevelopmentSignIn(ProductWebAppOptions options, IHostEnvironment environment, IConfiguration configuration)
    {
        return environment.IsDevelopment()
            && configuration.GetValue<bool>("Product:DevIdentity:Enabled")
            && options.DevelopmentPersonas.Count > 0;
    }

    // GET /app redirects to /app/, the page's own address (the sign-in redirect URI). Routing also sends /app/ here,
    // and that is the page.
    private static Task HandleBase(HttpContext context)
    {
        if (!Options(context).Enabled)
        {
            return NotServed(context);
        }

        if (context.Request.Path.HasValue && context.Request.Path.Value!.EndsWith("/", StringComparison.Ordinal))
        {
            return HandleFile(context);
        }

        context.Response.StatusCode = StatusCodes.Status308PermanentRedirect;
        context.Response.Headers.Location = ProductWebAppOptions.BasePath + "/";
        return Task.CompletedTask;
    }

    private static Task HandleConfig(HttpContext context)
    {
        ProductWebAppOptions options = Options(context);
        if (!options.Enabled)
        {
            return NotServed(context);
        }

        IServiceProvider services = context.RequestServices;
        JsonTreeNode body = Config(options, services.GetRequiredService<IOptions<ProductAuthOptions>>().Value, services.GetRequiredService<IHostEnvironment>(), services.GetRequiredService<IConfiguration>());
        context.Response.Headers.CacheControl = "no-store";
        return HttpJson.Write(context, StatusCodes.Status200OK, body);
    }

    private static async Task HandleFile(HttpContext context)
    {
        ProductWebAppOptions options = Options(context);
        if (!options.Enabled)
        {
            await NotServed(context).ConfigureAwait(false);
            return;
        }

        IServiceProvider services = context.RequestServices;
        string? root = Root(options, services.GetRequiredService<IHostEnvironment>());
        if (root == null)
        {
            await NotServed(context, "The web app is switched on, but its built files are not in this deployment (Product:WebApp:RootPath).").ConfigureAwait(false);
            return;
        }

        string path = context.Request.RouteValues.TryGetValue("path", out object? value) ? value as string ?? string.Empty : string.Empty;
        string? file = Resolve(root, path);
        if (file != null && !string.Equals(Path.GetFileName(file), "index.html", StringComparison.OrdinalIgnoreCase))
        {
            await SendFile(context, file, path.StartsWith("assets/", StringComparison.Ordinal) ? ImmutableCache : RevalidateCache, page: false).ConfigureAwait(false);
            return;
        }

        // A path with a file extension that is not there is a missing file, not a page: the page is not served in its
        // place, so a broken script reference fails plainly.
        if (file == null && Path.HasExtension(path))
        {
            await NotServed(context, "No such file in the web app.").ConfigureAwait(false);
            return;
        }

        await SendFile(context, Path.Combine(root, "index.html"), "no-store", page: true).ConfigureAwait(false);
    }

    // The file a request path names inside the root, or null. Refuses anything outside the root, hidden names, and
    // config.json (written by the host, never read from disk).
    private static string? Resolve(string root, string path)
    {
        if (string.IsNullOrEmpty(path) || path.IndexOf('\\') >= 0 || path.IndexOf(':') >= 0 || path.IndexOf('\0') >= 0)
        {
            return null;
        }

        string[] parts = path.Split('/');
        if (parts.Any(p => p.Length == 0 || p == "." || p == ".." || p.StartsWith(".", StringComparison.Ordinal)))
        {
            return null;
        }

        string full = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
        string prefix = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ? root : root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(full)
            || string.Equals(Path.GetFileName(full), "config.json", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return full;
    }

    private static async Task SendFile(HttpContext context, string file, string cacheControl, bool page)
    {
        HttpResponse response = context.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = ContentTypes.TryGetContentType(file, out string? type) ? type : "application/octet-stream";
        response.Headers.CacheControl = cacheControl;
        if (page)
        {
            ProductAuthOptions auth = context.RequestServices.GetRequiredService<IOptions<ProductAuthOptions>>().Value;
            response.Headers.ContentSecurityPolicy = ContentSecurityPolicy(SignInOrigin(auth));
            response.Headers["Referrer-Policy"] = "no-referrer";
            response.Headers.XFrameOptions = "DENY";
        }

        response.ContentLength = new FileInfo(file).Length;
        await response.SendFileAsync(file, context.RequestAborted).ConfigureAwait(false);
    }

    private static string SignInOrigin(ProductAuthOptions auth)
    {
        return Uri.TryCreate(auth.Instance, UriKind.Absolute, out Uri? instance) && instance.Scheme == Uri.UriSchemeHttps
            ? instance.GetLeftPart(UriPartial.Authority)
            : string.Empty;
    }

    private static Task NotServed(HttpContext context, string message = "The web app is not served here.")
    {
        context.Response.Headers.CacheControl = "no-store";
        return HttpJson.Write(context, StatusCodes.Status404NotFound, Js.Obj(("error", JsonTreeNode.FromString(message))));
    }

    private static ProductWebAppOptions Options(HttpContext context)
    {
        return context.RequestServices.GetRequiredService<IOptions<ProductWebAppOptions>>().Value;
    }
}
