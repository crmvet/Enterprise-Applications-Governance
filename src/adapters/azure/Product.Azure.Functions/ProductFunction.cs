using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Product.Mcp.Core.Contracts;

namespace Product.Azure.Functions;

/// <summary>The product's request pipeline, built once from the worker's services.</summary>
public sealed class ProductRequestPipeline
{
    /// <summary>Creates the holder.</summary>
    public ProductRequestPipeline(RequestDelegate handler)
    {
        Handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    /// <summary>The pipeline: routing, authentication, the caller context, authorization and the product's endpoints.</summary>
    public RequestDelegate Handler { get; }
}

/// <summary>
/// The one HTTP function of the app. Its route is <c>{*path}</c> and host.json sets the route prefix to empty, so every
/// path (<c>/mcp</c>, <c>/api/...</c>, <c>/.well-known/...</c>, <c>/health/...</c>) reaches the same pipeline as the local
/// server, which answers it completely. The function is anonymous at the Functions level: sign-in is the pipeline's
/// (Entra JWT bearer), as on the local server.
/// </summary>
public sealed class ProductFunction
{
    private readonly ProductRequestPipeline _pipeline;
    private readonly IServiceProvider _services;

    /// <summary>Creates the function.</summary>
    public ProductFunction(ProductRequestPipeline pipeline, IServiceProvider services)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>Hands the request to the product's pipeline, which writes the whole response.</summary>
    [Function("product")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", "delete", Route = "{*path}")] HttpRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        HttpContext context = request.HttpContext;

        // The worker's own routing matched this function; the product's routing must match the product's endpoint.
        context.SetEndpoint(null);
        context.Request.RouteValues.Clear();

        if (context.RequestServices?.GetService<ContractSet>() == null)
        {
            using IServiceScope scope = _services.CreateScope();
            context.RequestServices = scope.ServiceProvider;
            await _pipeline.Handler(context).ConfigureAwait(false);
        }
        else
        {
            await _pipeline.Handler(context).ConfigureAwait(false);
        }

        return new EmptyResult();
    }
}
