using System;
using Microsoft.Azure.Functions.Worker;
using Product.Azure.Host;

namespace Product.Azure.Functions;

/// <summary>
/// The engine's daily due-state refresh (document 23, S2; document 25, B25-18) as an Azure Functions
/// timer trigger. The timer fires at five past every hour; the host setting <c>Product:DueStateRefresh:HourUtc</c> picks
/// the hour it runs in, and with the setting unset it runs nothing. The refresh runs as the engine itself
/// (<see cref="DueStateRefreshRunner"/>), so every row it changes is the engine's own nested write with an Audit Entry,
/// and a second run on the same day changes and sends nothing new.
/// </summary>
public sealed class DueStateRefreshFunction
{
    /// <summary>Every hour at five past, so the run lands early in the hour the setting names.</summary>
    public const string Schedule = "0 5 * * * *";

    private readonly DueStateRefreshRunner _runner;

    /// <summary>Creates the function.</summary>
    public DueStateRefreshFunction(DueStateRefreshRunner runner)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    /// <summary>Runs the refresh when this is the hour the setting names.</summary>
    [Function("due-state-refresh")]
    public void Run([TimerTrigger(Schedule)] TimerInfo timer)
    {
        _runner.RunIfDue(DateTime.UtcNow);
    }
}
