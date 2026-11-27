using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Product.Azure.Stores;
using Product.Engine.Core.Canonical;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Envelope;
using Product.Mcp.Core.Json;
using Product.Mcp.Core.Protocol;
using Product.Mcp.Core.Telemetry;

namespace Product.Azure.Host;

/// <summary>
/// Request limits per signed-in caller, bound from <c>Product:Limits</c>. Each limit counts the requests one caller sends
/// to one group of routes in a fixed UTC minute. The count lives in the store (<see cref="ICounterStore"/>), never in a
/// server's memory, so every instance sees the same count and a restart does not reset it. These limits sit in front of
/// the Tool Policy limit per person and tool, which the MCP server core still applies to every tool call.
/// </summary>
public sealed class ProductLimitsOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Product:Limits";

    /// <summary>Counts requests and answers 429 over a limit. Default on.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Requests a minute one caller may send to the MCP endpoint. 0 sets no limit for the group.</summary>
    public int McpPerMinute { get; set; } = 120;

    /// <summary>Requests a minute one caller may send to the REST API for the web app (<c>/api/...</c>). 0 sets no limit.</summary>
    public int ApiPerMinute { get; set; } = 300;

    /// <summary>Requests a minute one workload may send to the record endpoint (<c>/api/deployments...</c>). 0 sets no limit.</summary>
    public int RecordPerMinute { get; set; } = 60;

    /// <summary>
    /// Requests a minute the approval runner may send to its routes and the approval outcome endpoint
    /// (<c>/engine/v1/...</c>). 0 sets no limit.
    /// </summary>
    public int ApprovalPerMinute { get; set; } = 600;

    /// <summary>
    /// Requests a minute the operator of a demo install may send to the REST API and the MCP endpoint while a demo persona
    /// acts (D-39). The persona switch counts the operator, never the persona, and counts these requests apart from the
    /// operator's own. Default 1200: one person presenting fifteen personas reloads every page with each switch, and the
    /// demo director (refresh, check) sends its story as the operator in one burst; four times the per-person API limit
    /// covers both and still stops a page stuck in a loop within the minute. 0 keeps the group's own limit.
    /// </summary>
    public int DemoOperatorPerMinute { get; set; } = 1200;

    /// <summary>The limit of a route group after the switch: null when the group is not limited.</summary>
    public int? LimitOf(RequestLimitGroup group)
    {
        if (!Enabled)
        {
            return null;
        }

        int value = group switch
        {
            RequestLimitGroup.Mcp => McpPerMinute,
            RequestLimitGroup.Api => ApiPerMinute,
            RequestLimitGroup.Record => RecordPerMinute,
            RequestLimitGroup.Approval => ApprovalPerMinute,
            _ => 0,
        };
        return value > 0 ? value : null;
    }
}

/// <summary>The groups of routes a caller's requests are counted in.</summary>
public enum RequestLimitGroup
{
    /// <summary>Not counted: anonymous routes (health, metadata, the web app's files) and unknown paths.</summary>
    None,

    /// <summary>The MCP endpoint, every path the transport answers under it.</summary>
    Mcp,

    /// <summary>The REST API for the web app: <c>/api/me</c>, <c>/api/info</c> and <c>/api/{tool}</c>.</summary>
    Api,

    /// <summary>The record endpoint for pipelines: <c>/api/deployments</c> and <c>/api/deployments/corrections</c>.</summary>
    Record,

    /// <summary>The approval runner's routes and the approval outcome endpoint: everything under <c>/engine/v1/</c>.</summary>
    Approval,
}

/// <summary>Startup checks of <see cref="ProductLimitsOptions"/>: no limit is negative.</summary>
public sealed class ProductLimitsOptionsValidator : IValidateOptions<ProductLimitsOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ProductLimitsOptions options)
    {
        if (options == null)
        {
            return ValidateOptionsResult.Fail("Product:Limits is missing.");
        }

        var failures = new List<string>();
        foreach ((string key, int value) in new[]
        {
            ("McpPerMinute", options.McpPerMinute),
            ("ApiPerMinute", options.ApiPerMinute),
            ("RecordPerMinute", options.RecordPerMinute),
            ("ApprovalPerMinute", options.ApprovalPerMinute),
            ("DemoOperatorPerMinute", options.DemoOperatorPerMinute),
        })
        {
            if (value < 0)
            {
                failures.Add("Product:Limits:" + key + " must be 0 (no limit) or more.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

/// <summary>
/// Counts each signed-in caller's requests per route group and minute in the store, and answers 429 with
/// <c>Retry-After</c> once a group's limit is passed. It runs after the caller context, so only a caller whose token the
/// sign-in accepted, from the configured tenant, is counted: an anonymous request or one from another tenant never
/// writes to the store. The counter key is a hash of the caller's object ID and the group, so the store never holds a
/// raw object ID for it. The clock is the engine's.
/// </summary>
public sealed class CallerRequestLimitMiddleware
{
    /// <summary>The counter window.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    // The fixed salt of the counter key hash. It hides the object ID in the store; it is not a secret.
    private const string KeySalt = "http-request-limit";

    private readonly RequestDelegate _next;

    /// <summary>Creates the middleware.</summary>
    public CallerRequestLimitMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    /// <summary>Counts the request, then passes it on or answers 429.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        IServiceProvider services = context.RequestServices;
        CallerClaims? caller = ProductRequest.Caller(context);
        ProductLimitsOptions limits = services.GetRequiredService<IOptions<ProductLimitsOptions>>().Value;
        RequestLimitGroup group = Classify(context.Request.Path, services.GetRequiredService<IOptions<ProductHostOptions>>().Value.McpPath);
        int? limit = limits.LimitOf(group);
        if (caller == null || string.IsNullOrEmpty(caller.ObjectId) || !limit.HasValue || ForeignTenant(caller, services))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        DateTime now = Utc(services.GetRequiredService<IEngineClock>().UtcNow);
        DateTime windowStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc);
        // While a demo persona acts (D-39), the person who signed in is counted, so switching personas never resets the
        // limit. Those requests have their own count and the demo operator's limit (DemoOperatorPerMinute) on the REST API
        // and the MCP endpoint, so presenting many personas does not run into the per-person limit, and the operator's
        // own requests keep theirs.
        bool demoActs = caller.DemoPersona is DemoPersonaUse demo && demo.OperatorObjectId.Length > 0;
        string counted = demoActs ? caller.DemoPersona!.OperatorObjectId : caller.ObjectId;
        if (demoActs && (group == RequestLimitGroup.Api || group == RequestLimitGroup.Mcp) && limits.DemoOperatorPerMinute > 0)
        {
            limit = limits.DemoOperatorPerMinute;
        }

        long count = services.GetRequiredService<ICounterStore>().Increment(demoActs ? DemoCounterKey(group, counted) : CounterKey(group, counted), windowStart, Window);
        if (count <= limit.Value)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        int retryAfter = Math.Max(1, (int)Math.Ceiling((windowStart + Window - now).TotalSeconds));
        if (count == limit.Value + 1)
        {
            // Once per caller, group and window, so a flood does not flood the log too. No object ID, no token.
            services.GetRequiredService<ILoggerFactory>().CreateLogger("Product.Azure.Host.Limits")
                .LogWarning("A caller passed the limit of {Limit} requests a minute to the {Group} routes; request {CorrelationId} and the rest of the minute answer 429.", limit.Value, GroupLabel(group), ProductRequest.CorrelationId(context));
        }

        await Refuse(context, group, limit.Value, retryAfter).ConfigureAwait(false);
    }

    /// <summary>The route group of a request path. Paths compare without case, as routing does.</summary>
    public static RequestLimitGroup Classify(PathString path, string mcpPath)
    {
        string value = path.HasValue ? path.Value!.TrimEnd('/') : string.Empty;
        string mcp = "/" + (mcpPath ?? string.Empty).Trim('/');
        if (Under(value, mcp))
        {
            return RequestLimitGroup.Mcp;
        }

        if (Under(value, "/api/deployments"))
        {
            return RequestLimitGroup.Record;
        }

        if (Under(value, "/api"))
        {
            return RequestLimitGroup.Api;
        }

        return Under(value, "/engine/v1") ? RequestLimitGroup.Approval : RequestLimitGroup.None;
    }

    /// <summary>The store counter key of a caller and group: never the raw object ID.</summary>
    public static string CounterKey(RequestLimitGroup group, string objectId)
    {
        return "http-requests|" + GroupLabel(group) + "|" + TelemetryEvent.HashCaller(objectId, KeySalt);
    }

    /// <summary>The store counter key of a demo operator's requests as the demo personas, in one group.</summary>
    public static string DemoCounterKey(RequestLimitGroup group, string operatorObjectId)
    {
        return "http-requests|" + GroupLabel(group) + "-demo|" + TelemetryEvent.HashCaller(operatorObjectId, KeySalt);
    }

    // A caller from another tenant is refused further on and never counted, so it cannot use up the count of a person
    // whose object ID it carries.
    private static bool ForeignTenant(CallerClaims caller, IServiceProvider services)
    {
        string tenant = services.GetRequiredService<IOptions<ProductAuthOptions>>().Value.TenantId;
        return !string.IsNullOrWhiteSpace(tenant) && !string.Equals(caller.TenantId, tenant, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Under(string path, string prefix)
    {
        return path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
            || (path.Length > prefix.Length && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && path[prefix.Length] == '/');
    }

    private static string GroupLabel(RequestLimitGroup group)
    {
        return group switch
        {
            RequestLimitGroup.Mcp => "mcp",
            RequestLimitGroup.Api => "api",
            RequestLimitGroup.Record => "record",
            RequestLimitGroup.Approval => "approval",
            _ => "none",
        };
    }

    private static string GroupText(RequestLimitGroup group)
    {
        return group switch
        {
            RequestLimitGroup.Mcp => "the MCP endpoint",
            RequestLimitGroup.Api => "the REST API",
            RequestLimitGroup.Record => "the record endpoint",
            _ => "the approval routes",
        };
    }

    // The MCP endpoint answers a JSON-RPC error whose data is the failure envelope, as the core does for a sign-in
    // refusal; every other route answers the failure envelope itself.
    private static Task Refuse(HttpContext context, RequestLimitGroup group, int limit, int retryAfter)
    {
        var error = new McpError(
            McpErrorCodes.RateLimited,
            "This caller sent more than " + limit.ToString(CultureInfo.InvariantCulture) + " requests a minute to " + GroupText(group) + ". Wait " + retryAfter.ToString(CultureInfo.InvariantCulture) + " seconds, then call again.",
            retryAfterSeconds: retryAfter);
        if (group != RequestLimitGroup.Mcp)
        {
            ContractSet contracts = context.RequestServices.GetRequiredService<ContractSet>();
            return HttpJson.WriteFailure(context, error, contracts);
        }

        JsonTreeNode body = Js.Obj(
            ("jsonrpc", JsonTreeNode.FromString("2.0")),
            ("id", JsonTreeNode.Null),
            ("error", Js.Obj(
                ("code", JsonTreeNode.FromInt64(JsonRpcCodes.InvalidRequest)),
                ("message", JsonTreeNode.FromString(error.Message)),
                ("data", Envelope.Failure(error, ProductRequest.CorrelationId(context))))));
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Retry-After"] = retryAfter.ToString(CultureInfo.InvariantCulture),
        };
        return HttpJson.Write(context, StatusCodes.Status429TooManyRequests, body, headers);
    }

    private static DateTime Utc(DateTime value)
    {
        return value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value.ToUniversalTime(), DateTimeKind.Utc);
    }
}

/// <summary>Registration of the request limits and the health settings.</summary>
public static class ProductHardeningRegistration
{
    /// <summary>
    /// Binds <c>Product:Limits</c> (<see cref="ProductLimitsOptions"/>, checked at startup) and <c>Product:Health</c>
    /// (<see cref="ProductHealthOptions"/>, checked at startup), and registers the shared readiness check
    /// (<see cref="ProductReadiness"/>). <see cref="ProductPipeline.UseProductPipeline"/> adds the limit middleware after
    /// the caller context.
    /// </summary>
    public static IServiceCollection AddProductHardening(this IServiceCollection services, IConfiguration configuration)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        services.AddOptions<ProductLimitsOptions>().Bind(configuration.GetSection(ProductLimitsOptions.SectionName)).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ProductLimitsOptions>, ProductLimitsOptionsValidator>());
        services.AddOptions<ProductHealthOptions>().Bind(configuration.GetSection(ProductHealthOptions.SectionName)).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ProductHealthOptions>, ProductHealthOptionsValidator>());
        services.TryAddSingleton(sp => new ProductReadiness(sp.GetRequiredService<IProductStore>()));
        return services;
    }
}
