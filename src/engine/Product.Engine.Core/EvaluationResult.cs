using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Product.Engine.Core.Contract;

namespace Product.Engine.Core;

// Returned by every operation (document 25, section 5.1).
// A rule outcome is a result, not an exception (section 7). "Not approved" and "stop" are normal answers.
public sealed class EvaluationResult<TPayload>
{
    public EvaluationResult(
        string outcome,
        TPayload payload,
        IEnumerable<Reason>? reasons,
        IEnumerable<RuleTrace>? trace,
        int configurationVersion,
        string snapshotHash,
        string engineVersion)
    {
        if (!WireFormat.IsKebabCase(outcome))
        {
            throw new ArgumentException("An outcome is one kebab-case word, such as engine-accept.", nameof(outcome));
        }

        if (configurationVersion < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(configurationVersion));
        }

        if (string.IsNullOrEmpty(snapshotHash))
        {
            throw new ArgumentException("A result carries the hash of the snapshot it used.", nameof(snapshotHash));
        }

        if (string.IsNullOrEmpty(engineVersion))
        {
            throw new ArgumentException("A result carries the engine version.", nameof(engineVersion));
        }

        Reason[] sortedReasons = (reasons ?? Enumerable.Empty<Reason>()).ToArray();
        foreach (Reason reason in sortedReasons)
        {
            if (reason == null)
            {
                throw new ArgumentException("A reason list has no null entries.", nameof(reasons));
            }
        }

        // Every list in a result has a stable order (document 25, section 9). LINQ OrderBy is a stable sort.
        sortedReasons = sortedReasons.OrderBy(reason => reason, Reason.CanonicalOrder).ToArray();

        // The trace keeps the order the operation built. Operations build it in a stable order.
        RuleTrace[] traceRows = (trace ?? Enumerable.Empty<RuleTrace>()).ToArray();
        foreach (RuleTrace row in traceRows)
        {
            if (row == null)
            {
                throw new ArgumentException("A trace has no null entries.", nameof(trace));
            }
        }

        Outcome = outcome;
        Payload = payload;
        Reasons = new ReadOnlyCollection<Reason>(sortedReasons);
        Trace = new ReadOnlyCollection<RuleTrace>(traceRows);
        ConfigurationVersion = configurationVersion;
        SnapshotHash = snapshotHash;
        EngineVersion = engineVersion;
    }

    // One outcome word per operation (OperationCatalog lists them).
    public string Outcome { get; }

    // The operation's own output record.
    public TPayload Payload { get; }

    // Why the outcome is what it is. Errors first. Empty when nothing needs saying.
    public IReadOnlyList<Reason> Reasons { get; }

    // The configuration rows that decided the outcome.
    public IReadOnlyList<RuleTrace> Trace { get; }

    // The configuration version actually used.
    public int ConfigurationVersion { get; }

    // The snapshot hash actually used.
    public string SnapshotHash { get; }

    // The engine library's semantic version.
    public string EngineVersion { get; }

    public bool HasErrors => Reasons.Any(reason => reason.Severity == ReasonSeverity.Error);
}

// Builds results that carry this engine's version and the context's configuration version and hash.
public static class EvaluationResult
{
    public static EvaluationResult<TPayload> Create<TPayload>(
        EvaluationContext context,
        string outcome,
        TPayload payload,
        IEnumerable<Reason>? reasons = null,
        IEnumerable<RuleTrace>? trace = null)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        return new EvaluationResult<TPayload>(
            outcome,
            payload,
            reasons,
            trace,
            context.ConfigurationVersion,
            context.SnapshotHash,
            EngineInfo.Version);
    }
}
