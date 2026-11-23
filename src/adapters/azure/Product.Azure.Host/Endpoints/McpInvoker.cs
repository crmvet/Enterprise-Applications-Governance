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

namespace Product.Azure.Host;

// Builds the MCP server core for one request. The core is cheap to build (fields and a salt check) and keeps nothing
// between requests, so each request gets its own, with a token validator over the request's signed-in caller and the
// request's correlation ID. The caller's token never reaches the core: the validator answers from the claims ASP.NET Core
// already validated, and the Authorization header text is not passed on.
internal static class McpInvoker
{
    public static McpServerCore CreateCore(HttpContext context, string requiredScope)
    {
        IServiceProvider services = context.RequestServices;
        ProductAuthOptions auth = services.GetRequiredService<IOptions<ProductAuthOptions>>().Value;
        CallerClaims? caller = ProductRequest.Caller(context);
        CallerFacts facts = services.GetRequiredService<CallerFacts>();
        bool credential = context.Request.Headers.ContainsKey(HeaderNames.Authorization);
        var validator = new ClaimsTokenValidator(caller, credential, requiredScope, oid => facts.DelegatedKindOf(caller, oid), auth.TenantId);
        McpServerOptions options = PerRequest(services.GetRequiredService<McpServerOptions>(), ProductRequest.CorrelationId(context), ResourceUrl(context), requiredScope);
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

    private static McpServerOptions PerRequest(McpServerOptions shared, string correlationId, string resourceUrl, string scope)
    {
        return new McpServerOptions
        {
            ServerName = shared.ServerName,
            ResourceUrl = resourceUrl,
            AuthorizationServer = shared.AuthorizationServer,
            Scope = scope,
            TelemetrySalt = shared.TelemetrySalt,
            ValidateOutputs = shared.ValidateOutputs,

            // The host catches the failure itself (Invoke), so it can log it with the correlation ID; the client gets the
            // same JSON-RPC internal error the core would send.
            RethrowInternalErrors = true,
            UtcNow = shared.UtcNow,
            CorrelationIds = () => correlationId,
        };
    }

    // Runs one message through the core. With RethrowInternalErrors on the shared options (tests), a failure surfaces as
    // the exception. Otherwise a server failure is logged with the request's correlation ID (the full
    // exception in Development; elsewhere its type and stack, never its message, which may quote data) and answered as
    // the core answers it: a JSON-RPC internal error that names only the correlation ID.
    public static McpResponse Invoke(McpServerCore core, McpRequest request, HttpContext context)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            return core.Handle(request);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException && !context.RequestServices.GetRequiredService<McpServerOptions>().RethrowInternalErrors)
        {
            string correlationId = ProductRequest.CorrelationId(context);
            ILogger logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Product.Azure.Host.Mcp");
            if (context.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment())
            {
                logger.LogError(exception, "The MCP request {CorrelationId} failed.", correlationId);
            }
            else
            {
                logger.LogError("The MCP request {CorrelationId} failed with {ExceptionType} at {StackTrace}", correlationId, exception.GetType().FullName, exception.StackTrace);
            }

            string? method = Js.Text(request.Message, "method");
            context.RequestServices.GetRequiredService<ITelemetrySink>().Track(new TelemetryEvent(
                Array.IndexOf(KnownMethods, method) >= 0 ? method! : "(other)", null, "internal-error", clock.ElapsedMilliseconds, null, null, correlationId, null));
            JsonTreeNode body = Js.Obj(
                ("jsonrpc", JsonTreeNode.FromString("2.0")),
                ("id", Js.Member(request.Message, "id") ?? JsonTreeNode.Null),
                ("error", Js.Obj(
                    ("code", JsonTreeNode.FromInt64(JsonRpcCodes.InternalError)),
                    ("message", JsonTreeNode.FromString("The server failed on this request. Quote the correlation ID to the product's operators.")),
                    ("data", Js.Obj(("correlationId", JsonTreeNode.FromString(correlationId)))))));
            return new McpResponse(200, body);
        }
    }

    private static readonly string[] KnownMethods = { "initialize", "ping", "tools/list", "tools/call", "resources/list", "resources/templates/list", "resources/read" };
}
