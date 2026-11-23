using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Product.Engine.Core.Canonical;
using Product.Mcp.Core.Json;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Product.Mcp.Core;
using Product.Mcp.Core.Backend.InProcess;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Policy;
using Product.Mcp.Core.Protocol;
using Product.Mcp.Core.Telemetry;
using Product.Engine.Runtime.Callers;

namespace Product.Azure.Host;

// Builds the MCP server core for one request. The core is cheap to build (fields and a salt check) and keeps nothing
// between requests, so each request gets its own, with a token validator over the request's signed-in caller and the
// request's correlation ID. The caller's token never reaches the core: the validator answers from the claims ASP.NET Core
// already validated, and the Authorization header text is not passed on.
//
// The entry: the MCP endpoint is always the AI client entry. The REST API is the person entry when the token's verified
// azp is one of the web app's own client IDs (Product:WebApp:ClientIds), and the AI client entry otherwise; on the person
// entry the core itself refuses any client that is not one of those IDs.
internal static class McpInvoker
{
    public static McpServerCore CreateCore(HttpContext context, string requiredScope, bool personEntry = false)
    {
        IServiceProvider services = context.RequestServices;
        ProductAuthOptions auth = services.GetRequiredService<IOptions<ProductAuthOptions>>().Value;
        CallerClaims? caller = ProductRequest.Caller(context);
        CallerFacts facts = services.GetRequiredService<CallerFacts>();
        bool credential = context.Request.Headers.ContainsKey(HeaderNames.Authorization);
        var validator = new ClaimsTokenValidator(caller, credential, requiredScope, oid => facts.DelegatedKindOf(caller, oid), auth.TenantId);
        McpServerOptions options = PerRequest(
            services.GetRequiredService<McpServerOptions>(),
            ProductRequest.CorrelationId(context),
            ResourceUrl(context),
            requiredScope,
            personEntry ? services.GetRequiredService<IOptions<ProductWebAppOptions>>().Value : null,
            FailureLogger(context));
        return new McpServerCore(
            services.GetRequiredService<ContractSet>(),
            validator,
            services.GetRequiredService<InProcessBackend>(),
            services.GetRequiredService<ITelemetrySink>(),
            options);
    }

    public static McpRequest Request(Product.Engine.Core.Canonical.JsonTreeNode message, HttpContext context)
    {
        return new McpRequest(message, authorization: null, toolsetsHeader: Toolsets(context));
    }

    // The X-Toolsets header, or null.
    public static string? Toolsets(HttpContext context)
    {
        string? toolsets = context.Request.Headers.TryGetValue(ToolsetSelection.HeaderName, out var values) ? values.ToString() : null;
        return string.IsNullOrEmpty(toolsets) ? null : toolsets;
    }

    // The MCP resource URL: the configured public URL, or in Development without one, the URL the request came to.
    public static string ResourceUrl(HttpContext context)
    {
        ProductHostOptions host = context.RequestServices.GetRequiredService<IOptions<ProductHostOptions>>().Value;
        if (!string.IsNullOrWhiteSpace(host.PublicBaseUrl))
        {
            return host.McpResourceUrl;
        }

        return context.Request.Scheme + "://" + context.Request.Host.ToUriComponent() + "/" + host.McpPath.TrimStart('/');
    }

    // The Origin check of streamable HTTP (DNS rebinding): no Origin header, an allowed origin, or in Development a
    // loopback origin.
    public static bool OriginAllowed(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(HeaderNames.Origin, out var values) || values.Count == 0 || string.IsNullOrEmpty(values.ToString()))
        {
            return true;
        }

        string origin = values.ToString().TrimEnd('/');
        ProductHostOptions host = context.RequestServices.GetRequiredService<IOptions<ProductHostOptions>>().Value;
        if (host.AllowedOrigins.Any(o => string.Equals(o.TrimEnd('/'), origin, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        IHostEnvironment environment = context.RequestServices.GetRequiredService<IHostEnvironment>();
        return environment.IsDevelopment()
            && Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri)
            && (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase));
    }

    private static McpServerOptions PerRequest(McpServerOptions shared, string correlationId, string resourceUrl, string scope, ProductWebAppOptions? personEntry, Action<Exception, string> onFailure)
    {
        var options = new McpServerOptions
        {
            ServerName = shared.ServerName,
            ResourceUrl = resourceUrl,
            AuthorizationServer = shared.AuthorizationServer,
            Scope = scope,
            TelemetrySalt = shared.TelemetrySalt,
            ValidateOutputs = shared.ValidateOutputs,
            RethrowInternalErrors = shared.RethrowInternalErrors,
            OnInternalFailure = onFailure,
            UtcNow = shared.UtcNow,
            CorrelationIds = () => correlationId,
        };
        if (personEntry != null)
        {
            options.Entry = CallerEntry.Person;
            options.PersonClientAppIds = personEntry.ClientIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).ToList();
            options.PersonClientLabel = personEntry.ClientLabel;
        }

        return options;
    }

    // The core's failure callback: logs a server failure with the request's correlation ID before the core answers it as
    // a JSON-RPC internal error that names only that ID. The full exception in Development; elsewhere its type and stack,
    // never its message, which may quote request data.
    private static Action<Exception, string> FailureLogger(HttpContext context)
    {
        IServiceProvider services = context.RequestServices;
        return (exception, correlationId) =>
        {
            ILogger logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Product.Azure.Host.Mcp");
            if (services.GetRequiredService<IHostEnvironment>().IsDevelopment())
            {
                logger.LogError(exception, "The MCP request {CorrelationId} failed.", correlationId);
            }
            else
            {
                logger.LogError("The MCP request {CorrelationId} failed with {ExceptionType} at {StackTrace}", correlationId, exception.GetType().FullName, exception.StackTrace);
            }
        };
    }

    // Runs one message through the core. The core answers every failure itself (a server failure as a JSON-RPC internal
    // error, after the failure callback logged it); with RethrowInternalErrors on the shared options (tests), a failure
    // surfaces as the exception.
    public static McpResponse Invoke(McpServerCore core, McpRequest request, HttpContext context)
    {
        if (core == null)
        {
            throw new ArgumentNullException(nameof(core));
        }

        return core.Handle(request);
    }
}
