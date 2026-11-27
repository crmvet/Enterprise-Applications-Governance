using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Product.Azure.Stores;
using Product.Engine.Runtime.Callers;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Envelope;

namespace Product.Azure.Host;

/// <summary>
/// Runs after authentication on every request: gives the request its correlation ID (also sent back in
/// <c>X-Correlation-Id</c>), maps the signed-in principal to the caller facts, and for a signed-in caller enters the
/// store call context the Azure SQL store writes to <c>SESSION_CONTEXT</c> on every connection it opens. In a demo
/// install, a demo operator's chosen persona (D-39, <see cref="ProductDemo"/>) replaces the operator's caller facts here,
/// before anything else reads them.
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

        CallerClaims? signedIn = services.GetRequiredService<ICallerMapper>().Map(context.User);
        ProductWebAppOptions webApp = services.GetRequiredService<IOptions<ProductWebAppOptions>>().Value;

        // The demo persona switch (D-39): in the Demo environment only, the person signed in to the web app may act as a
        // fictional persona. Everything after this line sees the persona: the Tool Policy, the engine and SESSION_CONTEXT.
        CallerClaims? caller = DemoPersonaSwitch.Apply(context, signedIn, webApp, services.GetRequiredService<IHostEnvironment>(), services.GetRequiredService<IOptions<ProductHostOptions>>().Value.McpPath, out McpError? refusal);
        ProductRequest.SetCaller(context, caller);
        if (caller?.DemoPersona is DemoPersonaUse demo)
        {
            // The store records only the persona and "demo operator" (D-39). The host log keeps who really acted, with the
            // correlation ID that ties it to the rows and audit entries of this request, so a write in a demo install
            // that several people can sign in to is never anonymous.
            services.GetRequiredService<ILoggerFactory>().CreateLogger("Product.Azure.Host.Demo").LogInformation(
                "Demo persona {Persona} acts for the signed-in operator {OperatorObjectId}; correlation {CorrelationId}.",
                demo.Name,
                demo.OperatorObjectId,
                correlationId);
        }
        if (refusal != null)
        {
            await HttpJson.WriteFailure(context, refusal, services.GetRequiredService<ContractSet>()).ConfigureAwait(false);
            return;
        }

        if (caller == null)
        {
            await _next(context);
            return;
        }

        // The resolution itself reads the store (the version probe, the Principal Reference), so it runs inside a first
        // context that names only the caller; the full context replaces it for the rest of the request. The person entry
        // (the product's own web app on the REST API) puts the person in SESSION_CONTEXT, not an AI client, with the web
        // app's Client App label ("Web app, demo operator" when a demo persona acts).
        bool person = CallerFacts.IsPersonEntry(caller, context.Request.Path, webApp);
        StoreCallContext full;
        CallerEntry entry = caller.IsAppOnly ? CallerEntry.Workload : person ? CallerEntry.Person : CallerEntry.AiClient;
        using (StoreCallContext.Enter(new StoreCallContext(caller.ObjectId, entry, null, correlationId)))
        {
            full = services.GetRequiredService<CallerFacts>().CallContext(caller, correlationId, person ? webApp.PersonLabel(caller) : null);
        }

        ProductRequest.SetCallContext(context, full);
        using (StoreCallContext.Enter(full))
        {
            await _next(context);
        }
    }
}
