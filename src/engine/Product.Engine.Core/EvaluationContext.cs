using System;

namespace Product.Engine.Core;

// Passed to every operation (document 25, section 5.1).
// The core never reads a clock. Time arrives here as AsOfUtc.
public sealed class EvaluationContext
{
    public EvaluationContext(
        int configurationVersion,
        string snapshotHash,
        DateTime asOfUtc,
        ActorInfo actor,
        string correlationId,
        string? clientApp = null)
    {
        if (configurationVersion < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(configurationVersion), "A configuration version number is never negative.");
        }

        if (string.IsNullOrEmpty(snapshotHash))
        {
            throw new ArgumentException("The context needs the content hash of the snapshot in use.", nameof(snapshotHash));
        }

        // A local or unspecified time could mean different moments on different machines. Only UTC is accepted.
        if (asOfUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The moment must be a UTC date-time (DateTimeKind.Utc).", nameof(asOfUtc));
        }

        if (string.IsNullOrEmpty(correlationId))
        {
            throw new ArgumentException("The context needs a correlation ID.", nameof(correlationId));
        }

        ConfigurationVersion = configurationVersion;
        SnapshotHash = snapshotHash;
        AsOfUtc = asOfUtc;
        Actor = actor ?? throw new ArgumentNullException(nameof(actor));
        CorrelationId = correlationId;
        ClientApp = string.IsNullOrEmpty(clientApp) ? null : clientApp;
    }

    // The Configuration Set Version number the caller loaded.
    public int ConfigurationVersion { get; }

    // The Content Hash of that version. Proves which rules applied.
    public string SnapshotHash { get; }

    // The moment the rules apply at, in UTC.
    public DateTime AsOfUtc { get; }

    public ActorInfo Actor { get; }

    // Ties every event of one call together (O24 Correlation Id).
    public string CorrelationId { get; }

    // The MCP client or application the caller named, such as "Claude Code". Null when none.
    public string? ClientApp { get; }
}
