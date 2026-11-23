using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Reasons;
using Product.Engine.Core.Snapshots;

namespace Product.Engine.Core.Operations;

// An operation payload that has a wire form (the payload of resultJson, document 25, section 11.4).
public interface IOperationPayload
{
    JsonTreeNode ToJson();
}

// What an operation body returns before it becomes an EvaluationResult.
internal sealed class OperationAnswer<TPayload>
    where TPayload : class
{
    public OperationAnswer(string outcome, TPayload payload, IEnumerable<Reason>? reasons = null, IEnumerable<RuleTrace>? trace = null)
    {
        Outcome = outcome;
        Payload = payload;
        Reasons = (reasons ?? Enumerable.Empty<Reason>()).ToList();
        Trace = (trace ?? Enumerable.Empty<RuleTrace>()).ToList();
    }

    public string Outcome { get; }

    public TPayload Payload { get; }

    public IReadOnlyList<Reason> Reasons { get; }

    public IReadOnlyList<RuleTrace> Trace { get; }
}

// The edge every operation runs through. It checks that the context names the snapshot it gets, runs the body,
// and turns an OperationStop into a result with no payload. Rule outcomes stay results, never exceptions
// (document 25, section 7).
internal static class OperationShell
{
    public static EvaluationResult<TPayload?> Run<TPayload>(EvaluationContext context, ConfigurationSnapshot? snapshot, bool needsSnapshot, Func<OperationAnswer<TPayload>> body)
        where TPayload : class
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (body == null)
        {
            throw new ArgumentNullException(nameof(body));
        }

        try
        {
            if (needsSnapshot)
            {
                CheckSnapshot(context, snapshot);
            }

            OperationAnswer<TPayload> answer = body();
            return EvaluationResult.Create<TPayload?>(context, answer.Outcome, answer.Payload, answer.Reasons, answer.Trace);
        }
        catch (OperationStop stop)
        {
            return EvaluationResult.Create<TPayload?>(context, stop.Outcome, null, new[] { stop.Reason });
        }
    }

    // The result carries the context's version and hash, so they must be the snapshot's (document 25, section 9: replay).
    private static void CheckSnapshot(EvaluationContext context, ConfigurationSnapshot? snapshot)
    {
        if (snapshot == null)
        {
            throw OperationStop.Broken(ReasonCodes.EngineUnavailable, "No configuration snapshot is in force, so the engine cannot answer.", null, null);
        }

        if (context.ConfigurationVersion != snapshot.VersionNumber || !string.Equals(context.SnapshotHash, snapshot.ContentHash, StringComparison.Ordinal))
        {
            throw OperationStop.Broken(
                ReasonCodes.SnapshotHashMismatch,
                "The context names configuration version " + context.ConfigurationVersion + " with another hash than the snapshot given (version "
                + snapshot.VersionNumber + "), so the engine cannot prove which rules applied.",
                null,
                null);
        }
    }
}
