using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Product.Azure.Stores;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Runtime;
using Product.Engine.Runtime.Callers;
using Product.Mcp.Core.Json;

namespace Product.Azure.Host;

/// <summary>
/// The schedule of the engine's daily due-state refresh (document 23, S2; document 25, B25-18), section
/// <c>Product:DueStateRefresh</c>. Off when <see cref="HourUtc"/> is unset: then only a platform admin's
/// <c>run_due_state_refresh</c> on the web app, or the local server's <c>refresh-due-states</c> command, runs it.
/// </summary>
public sealed class ProductDueStateRefreshOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Product:DueStateRefresh";

    /// <summary>
    /// The hour of the day (UTC, 0 to 23) the refresh runs in. The Functions app's timer fires at five past every hour
    /// and runs the refresh in this hour only. Unset: the timer runs nothing.
    /// </summary>
    public int? HourUtc { get; set; }

    /// <summary>The most items one run handles; the next run goes on with the rest.</summary>
    public int MaxItems { get; set; } = DueStateRefreshRequest.DefaultMaxItems;
}

/// <summary>Checks <see cref="ProductDueStateRefreshOptions"/> at startup.</summary>
public sealed class ProductDueStateRefreshOptionsValidator : IValidateOptions<ProductDueStateRefreshOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ProductDueStateRefreshOptions options)
    {
        var failures = new List<string>();
        if (options.HourUtc.HasValue && (options.HourUtc.Value < 0 || options.HourUtc.Value > 23))
        {
            failures.Add("Product:DueStateRefresh:HourUtc is an hour of the day in UTC, 0 to 23, or unset to switch the daily refresh off.");
        }

        if (options.MaxItems < 1 || options.MaxItems > 10000)
        {
            failures.Add("Product:DueStateRefresh:MaxItems is 1 to 10000.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

/// <summary>
/// Runs the engine's due-state refresh as the engine itself (<see cref="CallerIdentity.Engine"/>): the Functions app's
/// timer trigger calls <see cref="RunIfDue"/> every hour, and the local server's <c>refresh-due-states</c> command calls
/// <see cref="Run"/>. The store's call context names the engine, so the Azure SQL session context carries the actor kind
/// engine for every row the refresh writes. Every change the refresh makes is the engine's own nested write with an
/// Audit Entry; the host only starts it and logs the counts.
/// </summary>
public sealed class DueStateRefreshRunner
{
    private readonly EngineRuntime _runtime;
    private readonly ProductDueStateRefreshOptions _options;
    private readonly ILogger _logger;

    /// <summary>Creates the runner.</summary>
    public DueStateRefreshRunner(EngineRuntime runtime, IOptions<ProductDueStateRefreshOptions> options, ILogger<DueStateRefreshRunner> logger)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>True when the schedule is on and <paramref name="utcNow"/> falls in its hour.</summary>
    public bool IsDue(DateTime utcNow)
    {
        return _options.HourUtc.HasValue && utcNow.ToUniversalTime().Hour == _options.HourUtc.Value;
    }

    /// <summary>The timer's call: runs the refresh when the schedule is on and this is its hour, else does nothing.</summary>
    public RuntimeOutcome? RunIfDue(DateTime utcNow)
    {
        if (!IsDue(utcNow))
        {
            return null;
        }

        return Run(DueStateRefreshRequest.TimerTrigger);
    }

    /// <summary>Runs the refresh now as the engine, whatever the schedule.</summary>
    public RuntimeOutcome Run(string trigger, bool dryRun = false)
    {
        return RunOnce(_runtime, trigger, dryRun, _options.MaxItems, _logger);
    }

    /// <summary>Runs the refresh once as the engine, inside a store call context that names it.</summary>
    public static RuntimeOutcome RunOnce(EngineRuntime runtime, string trigger, bool dryRun, int maxItems, ILogger? logger)
    {
        if (runtime == null)
        {
            throw new ArgumentNullException(nameof(runtime));
        }

        string correlationId = "due-state-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        RuntimeOutcome outcome;
        using (StoreCallContext.Enter(new StoreCallContext(EngineKeys.EnginePrincipalId, CallerEntry.Engine, ActorKindMeaning.Engine, correlationId, clientApp: EngineKeys.EngineClientApp, clientAppVerified: true)))
        {
            outcome = runtime.RefreshDueStates(CallerIdentity.Engine(), new DueStateRefreshRequest(trigger, dryRun, maxItems) { CorrelationId = correlationId });
        }

        if (logger != null)
        {
            if (outcome.Succeeded)
            {
                logger.LogInformation("The due-state refresh ({Trigger}, {CorrelationId}) ran: {Counts}.", trigger, correlationId, Js.Write(Js.Member(outcome.Payload, "counts") ?? Js.EmptyObject));
            }
            else
            {
                logger.LogWarning("The due-state refresh ({Trigger}, {CorrelationId}) did not run ({Outcome}, {Reason}).", trigger, correlationId, outcome.Outcome, outcome.Reasons.Count == 0 ? "no reason" : outcome.Reasons[0].Code);
            }
        }

        return outcome;
    }
}

/// <summary>Registers the due-state refresh's options and runner.</summary>
public static class DueStateRefreshRegistration
{
    /// <summary><c>AddProduct</c> calls it; calling it again adds nothing new.</summary>
    public static IServiceCollection AddProductDueStateRefresh(this IServiceCollection services, IConfiguration configuration)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        services.AddOptions<ProductDueStateRefreshOptions>().Bind(configuration.GetSection(ProductDueStateRefreshOptions.SectionName)).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ProductDueStateRefreshOptions>, ProductDueStateRefreshOptionsValidator>());
        services.TryAddSingleton<DueStateRefreshRunner>();
        return services;
    }
}
