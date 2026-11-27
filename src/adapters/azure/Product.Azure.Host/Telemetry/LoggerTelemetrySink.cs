using System;
using Microsoft.Extensions.Logging;
using Product.Azure.Stores;
using Product.Mcp.Core.Policy;
using Product.Mcp.Core.Telemetry;

namespace Product.Azure.Host;

/// <summary>
/// Writes each MCP telemetry event as one structured log event: method, tool, outcome, latency, client label, caller hash,
/// correlation ID, configuration version and the request's database round trips. The event type has only those fields, so a token or typed text cannot reach
/// the log through it. With OpenTelemetry and Azure Monitor configured, the log reaches Application Insights.
/// </summary>
public sealed partial class LoggerTelemetrySink : ITelemetrySink
{
    private readonly ILogger<LoggerTelemetrySink> _logger;

    /// <summary>Creates the sink.</summary>
    public LoggerTelemetrySink(ILogger<LoggerTelemetrySink> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public void Track(TelemetryEvent telemetryEvent)
    {
        if (telemetryEvent == null)
        {
            return;
        }

        Write(_logger, telemetryEvent.Operation, telemetryEvent.ToolName, telemetryEvent.Outcome, telemetryEvent.LatencyMilliseconds, telemetryEvent.ClientApp, telemetryEvent.CallerHash, telemetryEvent.CorrelationId, telemetryEvent.ConfigurationSetVersion, StoreRoundTrips.Current);
    }

    // StoreRoundTrips: the database round trips the request made so far (wave 4b review), a number only.
    [LoggerMessage(EventId = 4100, EventName = "McpRequest", Level = LogLevel.Information, Message = "MCP {Operation} {Tool} answered {Outcome} in {LatencyMs} ms; client {ClientApp}; caller {CallerHash}; correlation {CorrelationId}; configuration version {ConfigurationSetVersion}; store round trips {StoreRoundTrips}")]
    private static partial void Write(ILogger logger, string operation, string? tool, string outcome, long latencyMs, string? clientApp, string? callerHash, string correlationId, int? configurationSetVersion, int? storeRoundTrips);
}

/// <summary>The MCP core's rate limit port over the store's counter, so the count lives in the store and survives restarts.</summary>
public sealed class CounterRateLimitStore : IRateLimitStore
{
    private readonly ICounterStore _counters;

    /// <summary>Creates the adapter.</summary>
    public CounterRateLimitStore(ICounterStore counters)
    {
        _counters = counters ?? throw new ArgumentNullException(nameof(counters));
    }

    /// <inheritdoc />
    public long Increment(string counterKey, DateTime windowStartUtc, TimeSpan windowLength)
    {
        return _counters.Increment(counterKey, windowStartUtc, windowLength);
    }
}
