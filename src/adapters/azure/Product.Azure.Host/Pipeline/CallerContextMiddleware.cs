using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Product.Azure.Stores;
using Product.Engine.Runtime.Callers;

namespace Product.Azure.Host;

/// <summary>
/// Runs after authentication on every request: gives the request its correlation ID (also sent back in
/// <c>X-Correlation-Id</c>), maps the signed-in principal to the caller facts, and for a signed-in caller enters the
/// store call context the Azure SQL store writes to <c>SESSION_CONTEXT</c> on every connection it opens.
/// </summary>
internal sealed class CallerContextMiddleware
{
    private readonly RequestDelegate _next;

    public CallerContextMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        IServiceProvider services = context.RequestServices;
        string correlationId = services.GetRequiredService<IRequestCorrelation>().Next();
        ProductRequest.SetCorrelationId(context, correlationId);
        context.Response.Headers[ProductRequest.CorrelationHeader] = correlationId;

        CallerClaims? caller = services.GetRequiredService<ICallerMapper>().Map(context.User);
        ProductRequest.SetCaller(context, caller);
        if (caller == null)
        {
            await _next(context);
            return;
        }

        // The resolution itself reads the store (the version probe, the Principal Reference), so it runs inside a first
        // context that names only the caller; the full context replaces it for the rest of the request.
        StoreCallContext full;
        CallerEntry entry = caller.IsAppOnly ? CallerEntry.Workload : CallerEntry.AiClient;
        using (StoreCallContext.Enter(new StoreCallContext(caller.ObjectId, entry, null, correlationId)))
        {
            full = services.GetRequiredService<CallerFacts>().CallContext(caller, correlationId);
        }

        ProductRequest.SetCallContext(context, full);
        using (StoreCallContext.Enter(full))
        {
            await _next(context);
        }
    }
}
