using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Envelope;

namespace Product.Azure.Host;

/// <summary>The product's request pipeline and endpoint mapping, the same in both hosts.</summary>
public static class ProductPipeline
{
    /// <summary>
    /// Builds the whole pipeline as one request delegate, for a host that hands requests to it (the Azure Functions
    /// wrapper calls it from one catch-all HTTP function).
    /// </summary>
    public static RequestDelegate Build(IServiceProvider services)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        return new ApplicationBuilder(services).UseProductPipeline().Build();
    }

    /// <summary>
    /// Adds the product's pipeline: the response defaults and the failure guard, routing, authentication, the caller context
    /// (correlation ID, caller facts, store call context), the SDK gate when the SDK transport is chosen, authorization and
    /// the product's endpoints.
    /// </summary>
    public static IApplicationBuilder UseProductPipeline(this IApplicationBuilder app)
    {
        if (app == null)
        {
            throw new ArgumentNullException(nameof(app));
        }

        ProductHostOptions host = app.ApplicationServices.GetRequiredService<IOptions<ProductHostOptions>>().Value;
        bool strictTransport = StrictTransport(host, app.ApplicationServices.GetRequiredService<IHostEnvironment>());
        app.Use((context, next) => ResponseDefaults(context, next, strictTransport));
        app.Use(FailureGuard);
        app.UseRouting();
        app.UseAuthentication();
        app.UseMiddleware<CallerContextMiddleware>();
        if (host.Mcp.Transport == ProductMcpTransport.Sdk)
        {
            // By the endpoint routing chose, not by the path text: routing also sends /mcp/ and /MCP to the SDK.
            app.UseWhen(McpSdkTransport.IsSdkEndpoint, branch => branch.Use(McpSdkTransport.Gate));
        }

        app.UseAuthorization();
        app.UseEndpoints(endpoints => endpoints.MapProduct());
        return app;
    }

    /// <summary>
    /// Maps every product endpoint once:
    /// <list type="bullet">
    /// <item><c>POST /mcp</c>: the MCP endpoint (the core, or the SDK when chosen); <c>GET</c> and <c>DELETE</c> answer 405 with the core.</item>
    /// <item><c>GET /.well-known/oauth-protected-resource/mcp</c> and <c>/.well-known/oauth-protected-resource</c>: the protected resource metadata, anonymous.</item>
    /// <item><c>GET /health/live</c>, <c>GET /health/ready</c>: probes, anonymous, status only.</item>
    /// <item><c>GET /api/me</c>, <c>GET /api/info</c>: the web app's session and service facts (delegated, <c>api.access</c>).</item>
    /// <item><c>POST /api/deployments</c>, <c>POST /api/deployments/corrections</c>: the record endpoint (app-only, <c>Deployment.Record</c>).</item>
    /// <item><c>POST /engine/v1/approval-records/{id}/outcome</c>: the approval runner's write (app-only, <c>Approval.Write</c>).</item>
    /// <item>Every registered <see cref="IProductEndpointModule"/>, such as the approval runner's routes (app-only, <c>Approval.Write</c>).</item>
    /// <item><c>GET /app</c>, <c>/app/config.json</c>, <c>/app/{**path}</c>: the product's web app and its runtime configuration, anonymous (the page signs in itself); 404 unless <c>Product:WebApp:Enabled</c>.</item>
    /// <item><c>POST /api/{tool}</c>: the REST tool facade (delegated, <c>api.access</c>, checked by the core). Literal routes win over it.</item>
    /// </list>
    /// </summary>
    public static IEndpointRouteBuilder MapProduct(this IEndpointRouteBuilder endpoints)
    {
        if (endpoints == null)
        {
            throw new ArgumentNullException(nameof(endpoints));
        }

        ProductHostOptions host = endpoints.ServiceProvider.GetRequiredService<IOptions<ProductHostOptions>>().Value;
        string mcp = "/" + host.McpPath.Trim('/');

        if (host.Mcp.Transport == ProductMcpTransport.Sdk)
        {
            endpoints.MapMcp(mcp).WithMetadata(new McpSdkEndpointMetadata());
        }
        else
        {
            endpoints.MapPost(mcp, Delegate(McpEndpoint.HandlePost));
            endpoints.MapMethods(mcp, new[] { HttpMethods.Get, HttpMethods.Delete }, Delegate(McpEndpoint.HandleNotAllowed));
        }

        endpoints.MapGet("/.well-known/oauth-protected-resource" + mcp, Delegate(McpEndpoint.HandleMetadata));
        endpoints.MapGet("/.well-known/oauth-protected-resource", Delegate(McpEndpoint.HandleMetadata));
        endpoints.MapGet("/health/live", Delegate(HealthEndpoints.HandleLive));
        endpoints.MapGet("/health/ready", Delegate(HealthEndpoints.HandleReady));
        endpoints.MapGet("/api/me", Delegate(ApiEndpoints.HandleMe)).RequireAuthorization(ProductPolicies.ApiCaller);
        endpoints.MapGet("/api/info", Delegate(ApiEndpoints.HandleInfo)).RequireAuthorization(ProductPolicies.ApiCaller);
        endpoints.MapPost("/api/deployments", Delegate(RecordEndpoints.HandleDeployment)).RequireAuthorization(ProductPolicies.RecordWriter);
        endpoints.MapPost("/api/deployments/corrections", Delegate(RecordEndpoints.HandleCorrection)).RequireAuthorization(ProductPolicies.RecordWriter);
        endpoints.MapPost("/engine/v1/approval-records/{id}/outcome", Delegate(ApprovalOutcomeEndpoint.Handle)).RequireAuthorization(ProductPolicies.ApprovalWriter);
        endpoints.MapProductModules();
        endpoints.MapProductWebApp();
        endpoints.MapPost("/api/{tool}", Delegate(ApiEndpoints.HandleTool));
        return endpoints;
    }

    private static RequestDelegate Delegate(RequestDelegate handler)
    {
        return handler;
    }

    /// <summary>
    /// The Strict-Transport-Security value every answer carries outside Development when the public base URL is https:
    /// a browser that has seen the service once never loads the page, which keeps its sign-in in session storage, over
    /// plain HTTP. Not in Development, where a local run on http://127.0.0.1 must stay reachable.
    /// </summary>
    public const string StrictTransportSecurity = "max-age=31536000";

    internal static bool StrictTransport(ProductHostOptions host, IHostEnvironment environment)
    {
        return !environment.IsDevelopment()
            && Uri.TryCreate(host.PublicBaseUrl, UriKind.Absolute, out Uri? publicUrl)
            && publicUrl.Scheme == Uri.UriSchemeHttps;
    }

    // A server failure on any route: logged with the request's correlation ID (the whole exception in Development;
    // elsewhere its type and stack, never its message, which may quote request data or a store's internals, as the MCP
    // endpoint's failure callback does), and answered with a failure envelope that names only that ID. The answer is the
    // same in every environment, so no page of exception details is ever served. A response that already started cannot
    // be replaced: the failure then ends the request as before.
    private static async Task FailureGuard(HttpContext context, RequestDelegate next)
    {
        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The caller went away; there is no one to answer.
        }
        catch (Exception exception) when (exception is not OutOfMemoryException && !context.Response.HasStarted)
        {
            string correlationId = ProductRequest.CorrelationId(context);
            IServiceProvider services = context.RequestServices;
            ILogger logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Product.Azure.Host.Failures");
            if (services.GetRequiredService<IHostEnvironment>().IsDevelopment())
            {
                logger.LogError(exception, "The request {CorrelationId} to {Path} failed.", correlationId, context.Request.Path.Value);
            }
            else
            {
                logger.LogError("The request {CorrelationId} to {Path} failed with {ExceptionType} at {StackTrace}", correlationId, context.Request.Path.Value, exception.GetType().FullName, exception.StackTrace);
            }

            context.Response.Clear();
            context.Response.Headers[ProductRequest.CorrelationHeader] = correlationId;
            var error = new McpError(McpErrorCodes.UpstreamUnavailable, "The server failed on this request. Try again later; if it keeps failing, give an administrator the correlation ID " + correlationId + ".");
            await HttpJson.Write(context, StatusCodes.Status500InternalServerError, Envelope.Failure(error, correlationId)).ConfigureAwait(false);
        }
    }

    // Browsers must not sniff another type than the one sent, and no cache may keep an answer that carries a caller's
    // data. A route that may be cached (the protected resource metadata, the web app's hashed files) sets its own
    // Cache-Control, which is kept. The web app's page sets its own Content-Security-Policy (ProductWebApp).
    private static Task ResponseDefaults(HttpContext context, RequestDelegate next, bool strictTransport)
    {
        context.Response.OnStarting(() =>
        {
            IHeaderDictionary headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            if (strictTransport)
            {
                headers["Strict-Transport-Security"] = StrictTransportSecurity;
            }

            if (!headers.ContainsKey("Cache-Control"))
            {
                headers["Cache-Control"] = "no-store";
            }

            return Task.CompletedTask;
        });
        return next(context);
    }
}
