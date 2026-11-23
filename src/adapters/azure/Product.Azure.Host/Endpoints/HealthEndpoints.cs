using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Product.Azure.Stores;
using Product.Engine.Core.Canonical;
using Product.Mcp.Core.Json;

namespace Product.Azure.Host;

/// <summary>Health settings, bound from <c>Product:Health</c>.</summary>
public sealed class ProductHealthOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Product:Health";

    /// <summary>
    /// How long the readiness probe waits for the store before it answers 503, in seconds. Default 5, from 1 to 60. A
    /// store's own connection retries must end within this time for a probe to see them succeed.
    /// </summary>
    public int ReadyTimeoutSeconds { get; set; } = 5;
}

/// <summary>Startup checks of <see cref="ProductHealthOptions"/>.</summary>
public sealed class ProductHealthOptionsValidator : IValidateOptions<ProductHealthOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ProductHealthOptions options)
    {
        return options != null && options.ReadyTimeoutSeconds >= 1 && options.ReadyTimeoutSeconds <= 60
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("Product:Health:ReadyTimeoutSeconds must be from 1 to 60.");
    }
}

/// <summary>
/// The readiness check of the store, with a time budget. While one check is still running, a new probe waits for that
/// same check instead of starting another, so a store that hangs never holds more than one thread for the probes. A
/// finished check is never reused: each probe after it asks the store again.
/// </summary>
public sealed class ProductReadiness
{
    private readonly IProductStore _store;
    private readonly object _gate = new();
    private Task<bool>? _running;

    /// <summary>Creates the check over the product's store.</summary>
    public ProductReadiness(IProductStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>True when the store answered within the budget.</summary>
    public async Task<bool> Check(TimeSpan budget)
    {
        Task<bool> probe;
        lock (_gate)
        {
            if (_running == null || _running.IsCompleted)
            {
                _running = Task.Run(() => _store.CanConnect());
            }

            probe = _running;
        }

        try
        {
            return await probe.WaitAsync(budget).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}

/// <summary>Health probes. Anonymous, status only, no data.</summary>
public static class HealthEndpoints
{
    /// <summary><c>GET /health/live</c>: the process answers.</summary>
    public static Task HandleLive(HttpContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.Response.Headers["Cache-Control"] = "no-store";
        return HttpJson.Write(context, StatusCodes.Status200OK, Js.Obj(("status", JsonTreeNode.FromString("live"))));
    }

    /// <summary>
    /// <c>GET /health/ready</c>: the store answers within <see cref="ProductHealthOptions.ReadyTimeoutSeconds"/>. 503 when
    /// it does not, or not in time; the answer names only the status, and the log says which.
    /// </summary>
    public static async Task HandleReady(HttpContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        IServiceProvider services = context.RequestServices;
        int seconds = services.GetRequiredService<IOptions<ProductHealthOptions>>().Value.ReadyTimeoutSeconds;
        ProductReadiness readiness = services.GetService<ProductReadiness>() ?? new ProductReadiness(services.GetRequiredService<IProductStore>());
        bool ready = await readiness.Check(TimeSpan.FromSeconds(Math.Max(1, seconds))).ConfigureAwait(false);
        if (!ready)
        {
            services.GetRequiredService<ILoggerFactory>().CreateLogger("Product.Azure.Host.Health")
                .LogWarning("The readiness probe {CorrelationId} answered 503: the store did not answer within {Seconds} seconds, or refused the check.", ProductRequest.CorrelationId(context), seconds);
        }

        context.Response.Headers["Cache-Control"] = "no-store";
        await HttpJson.Write(context, ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable, Js.Obj(("status", JsonTreeNode.FromString(ready ? "ready" : "unavailable")))).ConfigureAwait(false);
    }
}
