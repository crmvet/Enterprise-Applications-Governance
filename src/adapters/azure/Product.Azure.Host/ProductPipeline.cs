using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
    /// Adds the product's pipeline: routing, authentication, the caller context (correlation ID, caller facts, store call
    /// context), the SDK gate when the SDK transport is chosen, authorization and the product's endpoints.
    /// </summary>
    public static IApplicationBuilder UseProductPipeline(this IApplicationBuilder app)
    {
        if (app == null)
        {
            throw new ArgumentNullException(nameof(app));
        }

        ProductHostOptions host = app.ApplicationServices.GetRequiredService<IOptions<ProductHostOptions>>().Value;
        app.Use(ResponseDefaults);
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
        endpoints.MapPost("/api/{tool}", Delegate(ApiEndpoints.HandleTool));
        return endpoints;
    }

    private static RequestDelegate Delegate(RequestDelegate handler)
    {
        return handler;
    }

    // Every answer is JSON or an event stream for an API client, never a page: browsers must not sniff another type, and
    // no cache may keep an answer that carries a caller's data. A route that may be cached (the protected resource
    // metadata) sets its own Cache-Control, which is kept.
    private static Task ResponseDefaults(HttpContext context, RequestDelegate next)
    {
        context.Response.OnStarting(() =>
        {
            IHeaderDictionary headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            if (!headers.ContainsKey("Cache-Control"))
            {
                headers["Cache-Control"] = "no-store";
            }

            return Task.CompletedTask;
        });
        return next(context);
    }
}
