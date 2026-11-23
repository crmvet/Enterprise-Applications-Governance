using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Product.Azure.Stores;
using Product.Engine.Configuration.Presets;
using Product.Engine.Runtime;
using Product.Engine.Runtime.Ports;
using Product.Engine.Runtime.Snapshots;
using Product.Mcp.Core;
using Product.Mcp.Core.Backend;
using Product.Mcp.Core.Backend.InProcess;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Policy;
using Product.Mcp.Core.Resources;
using Product.Mcp.Core.Telemetry;

namespace Product.Azure.Host;

/// <summary>Service registration for both hosts (the local server and the Azure Functions app).</summary>
public static class ProductServiceCollectionExtensions
{
    // The scheme the policy scheme forwards to when Entra sign-in is not configured.
    private const string UnconfiguredScheme = "ProductEntraNotConfigured";

    /// <summary>
    /// Registers the product: its options (sections <c>Product</c> and <c>Product:Auth</c>), the MCP contracts, the shipped
    /// presets, the playbook index, the engine runtime and its snapshot cache, the in-process backend, the MCP server
    /// options, telemetry, the caller mapping, Entra sign-in with app roles under the product's policy scheme, the REST
    /// authorization policies, the web app's settings, the approval runner's routes and outbox
    /// (<see cref="ApprovalRunnerRegistration.AddProductApprovalRunner"/>), the live directory reads
    /// (<see cref="DirectoryRegistration.AddProductDirectoryReads"/>, off unless <c>Product:Directory:Mode</c> says
    /// otherwise) and, when chosen, the MCP SDK transport. A store is registered separately with
    /// <see cref="AddProductStore"/>. A live read port (<see cref="IDirectoryReader"/>, <see cref="IInventoryReader"/>)
    /// registered after this call wins over the directory reads' own.
    /// </summary>
    public static IServiceCollection AddProduct(this IServiceCollection services, IConfiguration configuration)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        services.AddOptions<ProductHostOptions>().Bind(configuration.GetSection(ProductHostOptions.SectionName)).ValidateOnStart();
        services.AddOptions<ProductAuthOptions>().Bind(configuration.GetSection(ProductAuthOptions.SectionName)).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ProductHostOptions>, ProductOptionsValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ProductAuthOptions>, ProductOptionsValidator>());

        // Nothing here reads the configuration's values: every setting is read from the options when it is used, so a
        // host (or a test) may change the configuration until the service provider is built.
        services.TryAddSingleton(_ => ContractSet.Embedded);
        services.TryAddSingleton(sp => PresetCatalog.Load(new[] { FromBase(sp.GetRequiredService<IOptions<ProductHostOptions>>().Value.PresetsPath) }));
        services.TryAddSingleton(sp => PlaybookIndex.FromFolder(FromBase(sp.GetRequiredService<IOptions<ProductHostOptions>>().Value.PlaybookPath)));
        services.TryAddSingleton<IEngineClock>(Product.Engine.Runtime.Ports.SystemClock.Instance);
        services.TryAddSingleton<SnapshotCache>();
        services.TryAddSingleton<IRequestCorrelation, RequestCorrelation>();
        services.TryAddSingleton(sp => new EngineRuntime(
            sp.GetRequiredService<IRecordStore>(),
            sp.GetRequiredService<IConfigurationStore>(),
            sp.GetRequiredService<PresetCatalog>(),
            sp.GetRequiredService<SnapshotCache>(),
            sp.GetRequiredService<IEngineClock>(),
            EngineCorrelation()));
        services.TryAddSingleton<IRateLimitStore>(sp => new CounterRateLimitStore(sp.GetRequiredService<ICounterStore>()));
        services.TryAddSingleton(sp => new InProcessBackend(
            sp.GetRequiredService<EngineRuntime>(),
            sp.GetRequiredService<IRecordStore>(),
            sp.GetRequiredService<IConfigurationStore>(),
            sp.GetRequiredService<PresetCatalog>(),
            sp.GetRequiredService<IRateLimitStore>(),
            sp.GetRequiredService<ContractSet>(),
            new InProcessBackendOptions
            {
                RecordUrlBase = RecordUrlBase(sp.GetRequiredService<IOptions<ProductHostOptions>>().Value),
                Playbook = sp.GetRequiredService<PlaybookIndex>(),
                Directory = sp.GetService<IDirectoryReader>(),
                Inventory = sp.GetService<IInventoryReader>(),
            }));
        services.TryAddSingleton(sp => McpOptions(
            sp.GetRequiredService<IOptions<ProductHostOptions>>().Value,
            sp.GetRequiredService<IOptions<ProductAuthOptions>>().Value,
            sp.GetRequiredService<IHostEnvironment>(),
            sp.GetRequiredService<IEngineClock>(),
            sp.GetRequiredService<IRequestCorrelation>()));
        services.TryAddSingleton<ITelemetrySink, LoggerTelemetrySink>();
        services.TryAddSingleton<ICallerMapper, CallerMapper>();
        services.TryAddSingleton(sp => new CallerFacts(sp.GetRequiredService<IRecordStore>(), sp.GetRequiredService<IConfigurationStore>(), sp.GetRequiredService<EngineRuntime>()));
        services.AddRouting();

        AddSignIn(services);
        services.AddAuthorization(options =>
        {
            foreach ((string name, AuthorizationPolicy policy) in ProductPolicyBuilder.Policies())
            {
                options.AddPolicy(name, policy);
            }
        });
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthorizationHandler, ProductCallerHandler>());
        services.Replace(ServiceDescriptor.Singleton<IAuthorizationMiddlewareResultHandler, ProductAuthorizationResultHandler>());

        // The SDK transport's services are always registered; MapProduct maps it only when Product:Mcp:Transport is sdk.
        services.AddProductMcpSdk();
        services.AddProductWebApp(configuration);
        services.AddProductApprovalRunner(configuration);
        services.AddProductDirectoryReads();
        services.AddProductHardening(configuration);
        return services;
    }

    /// <summary>
    /// Registers one store as every port the product needs: <see cref="IProductStore"/>, <see cref="IRecordStore"/>,
    /// <see cref="IConfigurationStore"/>, <see cref="ICounterStore"/> and <see cref="IStoreSeeder"/>, one singleton.
    /// </summary>
    public static IServiceCollection AddProductStore(this IServiceCollection services, Func<IServiceProvider, IProductStore> factory)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (factory == null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        services.AddSingleton(factory);
        services.AddSingleton<IRecordStore>(sp => sp.GetRequiredService<IProductStore>());
        services.AddSingleton<IConfigurationStore>(sp => sp.GetRequiredService<IProductStore>());
        services.AddSingleton<ICounterStore>(sp => sp.GetRequiredService<IProductStore>());
        services.AddSingleton<IStoreSeeder>(sp => sp.GetRequiredService<IProductStore>());
        return services;
    }

    // Entra ID JWT bearer (Microsoft.Identity.Web) behind the product's policy scheme. The policy scheme forwards to the
    // first scheme selector that claims the request, else to Entra when a tenant and client ID are configured, else to a
    // scheme that refuses every bearer token (a local run without a tenant; outside Development the options validator
    // fails startup first). The Entra settings are read from ProductAuthOptions when the scheme is first used.
    private static void AddSignIn(IServiceCollection services)
    {
        AuthenticationBuilder builder = services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = ProductAuthenticationDefaults.PolicyScheme;
                options.DefaultChallengeScheme = ProductAuthenticationDefaults.PolicyScheme;
                options.DefaultForbidScheme = ProductAuthenticationDefaults.PolicyScheme;
            })
            .AddPolicyScheme(ProductAuthenticationDefaults.PolicyScheme, "Product sign-in", options =>
            {
                options.ForwardDefaultSelector = context =>
                {
                    foreach (IProductSchemeSelector selector in context.RequestServices.GetServices<IProductSchemeSelector>())
                    {
                        string? scheme = selector.SelectScheme(context);
                        if (!string.IsNullOrEmpty(scheme))
                        {
                            return scheme;
                        }
                    }

                    return context.RequestServices.GetRequiredService<IOptions<ProductAuthOptions>>().Value.IsConfigured
                        ? ProductAuthenticationDefaults.EntraScheme
                        : UnconfiguredScheme;
                };
            })
            .AddScheme<AuthenticationSchemeOptions, UnconfiguredEntraHandler>(UnconfiguredScheme, null);

        builder.AddMicrosoftIdentityWebApi(
            jwt =>
            {
                jwt.MapInboundClaims = false;
                jwt.SaveToken = false;
            },
            identity =>
            {
            },
            ProductAuthenticationDefaults.EntraScheme,
            subscribeToJwtBearerMiddlewareDiagnosticsEvents: false);

        services.AddOptions<MicrosoftIdentityOptions>(ProductAuthenticationDefaults.EntraScheme)
            .Configure<IOptions<ProductAuthOptions>>((identity, auth) =>
            {
                identity.Instance = auth.Value.Instance;
                identity.TenantId = auth.Value.TenantId;
                identity.ClientId = auth.Value.ClientId;
            });
        services.AddOptions<JwtBearerOptions>(ProductAuthenticationDefaults.EntraScheme)
            .Configure<IOptions<ProductAuthOptions>>((jwt, auth) =>
            {
                jwt.MapInboundClaims = false;
                jwt.SaveToken = false;
                jwt.TokenValidationParameters.ValidAudiences = new[] { auth.Value.EffectiveAudience, auth.Value.ClientId }
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                jwt.TokenValidationParameters.NameClaimType = "name";
                jwt.TokenValidationParameters.RoleClaimType = ProductClaimTypes.Roles;
            });
    }

    private static McpServerOptions McpOptions(ProductHostOptions host, ProductAuthOptions auth, IHostEnvironment environment, IEngineClock clock, IRequestCorrelation correlation)
    {
        return new McpServerOptions
        {
            ServerName = host.ServerName,
            ResourceUrl = string.IsNullOrWhiteSpace(host.PublicBaseUrl) ? "http://localhost" + "/" + host.McpPath.TrimStart('/') : host.McpResourceUrl,
            AuthorizationServer = auth.AuthorizationServer,
            Scope = auth.McpScope,
            TelemetrySalt = string.IsNullOrEmpty(host.TelemetrySalt) ? null : host.TelemetrySalt,
            ValidateOutputs = host.ValidateOutputs ?? environment.IsDevelopment(),
            UtcNow = () => CallerFacts.Utc(clock.UtcNow),

            // Replaced per request by the request's own correlation ID (McpInvoker).
            CorrelationIds = correlation.Next,
        };
    }

    private static string RecordUrlBase(ProductHostOptions host)
    {
        // The MCP contracts type every record link as an https URL, so a local run without a public URL links to
        // https://localhost.
        return string.IsNullOrWhiteSpace(host.PublicBaseUrl) && string.IsNullOrWhiteSpace(host.RecordUrlBase) ? "https://localhost/app/records" : host.EffectiveRecordUrlBase;
    }

    // The engine's correlation ID for a call: the request's, from the store call context, so the Audit Entry carries the
    // same value as the envelope and telemetry. A call outside a request (tests, the seed command) gets its own.
    private static Func<string> EngineCorrelation()
    {
        long calls = 0;
        return () => StoreCallContext.Current?.CorrelationId ?? "call-" + Interlocked.Increment(ref calls).ToString("D6", CultureInfo.InvariantCulture);
    }

    private static string FromBase(string path)
    {
        return Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
    }
}

/// <summary>
/// Startup checks of the product options. Outside Development: a public HTTPS base URL and the Entra tenant and client
/// ID are required. Everywhere: a telemetry salt, when set, has at least 16 characters, and the limits are sane.
/// </summary>
public sealed class ProductOptionsValidator : IValidateOptions<ProductHostOptions>, IValidateOptions<ProductAuthOptions>
{
    private readonly IHostEnvironment _environment;

    /// <summary>Creates the validator.</summary>
    public ProductOptionsValidator(IHostEnvironment environment)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ProductHostOptions options)
    {
        var failures = new List<string>();
        if (!string.IsNullOrEmpty(options.TelemetrySalt) && options.TelemetrySalt!.Length < McpServerOptions.MinimumTelemetrySaltLength)
        {
            failures.Add("Product:TelemetrySalt must be at least " + McpServerOptions.MinimumTelemetrySaltLength + " characters, or unset.");
        }

        if (!string.IsNullOrWhiteSpace(options.PublicBaseUrl) && !Uri.TryCreate(options.PublicBaseUrl, UriKind.Absolute, out _))
        {
            failures.Add("Product:PublicBaseUrl must be an absolute URL.");
        }

        if (!_environment.IsDevelopment())
        {
            if (!Uri.TryCreate(options.PublicBaseUrl, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                failures.Add("Product:PublicBaseUrl is required outside Development and must be an https URL.");
            }
        }

        if (!string.IsNullOrWhiteSpace(options.RecordUrlBase)
            && (!Uri.TryCreate(options.RecordUrlBase, UriKind.Absolute, out Uri? records) || records.Scheme != Uri.UriSchemeHttps))
        {
            failures.Add("Product:RecordUrlBase must be an https URL: the MCP contracts type every record link as https.");
        }

        if (string.IsNullOrWhiteSpace(options.McpPath) || !options.McpPath.StartsWith("/", StringComparison.Ordinal))
        {
            failures.Add("Product:McpPath must start with a slash.");
        }

        if (options.MaxRequestBytes < 1024)
        {
            failures.Add("Product:MaxRequestBytes must be at least 1024.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ProductAuthOptions options)
    {
        var failures = new List<string>();
        if (!_environment.IsDevelopment() && !options.IsConfigured)
        {
            failures.Add("Product:Auth:TenantId and Product:Auth:ClientId are required outside Development.");
        }

        if (!string.IsNullOrWhiteSpace(options.TenantId) && !Guid.TryParse(options.TenantId, out _))
        {
            failures.Add("Product:Auth:TenantId must be the tenant's ID (a GUID).");
        }

        if (string.IsNullOrWhiteSpace(options.McpScope) || string.IsNullOrWhiteSpace(options.ApiScope))
        {
            failures.Add("Product:Auth:McpScope and Product:Auth:ApiScope must not be empty.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
