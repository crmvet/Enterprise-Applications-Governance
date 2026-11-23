using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Operations;

namespace Product.Engine.Runtime;

// One row write the runtime committed, in order: the caller's write first, then each effect as an engine-nested write
// (document 25, section 5.10). Effect is null for the caller's own write and for the engine's own operations (the
// approval request, the draft operations).
public sealed class AppliedWrite
{
    internal AppliedWrite(string entity, WriteOperation operation, string rowId, string? key, EffectKind? effect, bool isEngineNested, JsonTreeNode columns)
    {
        Entity = entity;
        Operation = operation;
        RowId = rowId;
        Key = key;
        Effect = effect;
        IsEngineNested = isEngineNested;
        Columns = columns;
    }

    public string Entity { get; }

    public WriteOperation Operation { get; }

    public string RowId { get; }

    public string? Key { get; }

    public EffectKind? Effect { get; }

    public bool IsEngineNested { get; }

    // The columns written: the caller's after, without removed columns, with the guard's stamps and the engine's values.
    public JsonTreeNode Columns { get; }

    public override string ToString()
    {
        return EngineContract.WriteOperations.ToWire(Operation) + " " + Entity + " " + (Key ?? RowId) + (Effect.HasValue ? " (" + EngineContract.EffectKinds.ToWire(Effect.Value) + ")" : string.Empty);
    }
}

// What one runtime call returns. The outcome word follows the error model of document 25, section 7: allowed for a
// committed write (or a replay), an operation's own outcome word for a read, else refused, invalid-input, not-found,
// conflict or unavailable. Nothing is written unless the outcome is allowed.
public sealed class RuntimeOutcome
{
    public const string Allowed = "allowed";
    public const string NotFound = "not-found";
    public const string Conflict = "conflict";

    internal RuntimeOutcome(
        string outcome,
        IEnumerable<Reason> reasons,
        int? configurationVersion,
        string? snapshotHash,
        string? rowId = null,
        bool created = false,
        bool isReplay = false,
        bool isNoOp = false,
        IEnumerable<AppliedWrite>? writes = null,
        JsonTreeNode? payload = null,
        IDictionary<string, JsonTreeNode>? results = null,
        int factRounds = 0)
    {
        Outcome = outcome;
        Reasons = new ReadOnlyCollection<Reason>(reasons.OrderBy(r => r, Reason.CanonicalOrder).ToList());
        ConfigurationVersion = configurationVersion;
        SnapshotHash = snapshotHash;
        RowId = rowId;
        Created = created;
        IsReplay = isReplay;
        IsNoOp = isNoOp;
        Writes = new ReadOnlyCollection<AppliedWrite>((writes ?? Enumerable.Empty<AppliedWrite>()).ToList());
        Payload = payload;
        Results = new ReadOnlyDictionary<string, JsonTreeNode>(new Dictionary<string, JsonTreeNode>(results ?? new Dictionary<string, JsonTreeNode>(), StringComparer.Ordinal));
        FactRounds = factRounds;
    }

    public string Outcome { get; }

    // Every reason of the call, errors first: the guard's, the operations' and the runtime's own.
    public IReadOnlyList<Reason> Reasons { get; }

    // The configuration version and snapshot hash the call ran under. Null when no version was in force.
    public int? ConfigurationVersion { get; }

    public string? SnapshotHash { get; }

    // The row the call wrote or found, such as the new Deployment Record.
    public string? RowId { get; }

    // For an upsert: true when the row was created, false when an existing row was returned.
    public bool Created { get; }

    // True when an idempotency key was used before with the same request: nothing was written (document 25, section 8).
    public bool IsReplay { get; }

    // True when the write changed nothing the guard cares about (WritePlan isNoOp).
    public bool IsNoOp { get; }

    public IReadOnlyList<AppliedWrite> Writes { get; }

    // The call's own answer, such as the record step's flags or the request's approval record IDs.
    public JsonTreeNode? Payload { get; }

    // The evaluation results the call computed, by operation ("OP-02", "OP-03", "OP-04", "activation" and so on), as
    // their wire payloads. The last result of each operation is kept.
    public IReadOnlyDictionary<string, JsonTreeNode> Results { get; }

    // How many rounds of fact reads the call needed in all (document 25, section 3.3).
    public int FactRounds { get; }

    // True for a committed write, a replay, a no-op, and any read answer (an operation's own outcome word). False for
    // refused, invalid-input, not-found, conflict and unavailable.
    public bool Succeeded => !IsFailure(Outcome);

    public bool HasReason(string code)
    {
        return Reasons.Any(r => string.Equals(r.Code, code, StringComparison.Ordinal));
    }

    public override string ToString()
    {
        return Outcome + (Reasons.Count == 0 ? string.Empty : ": " + string.Join(" | ", Reasons.Select(r => r.Code + " " + r.Message)));
    }

    internal static bool IsFailure(string outcome)
    {
        return outcome == OperationOutcomes.Refused || outcome == OperationOutcomes.InvalidInput || outcome == OperationOutcomes.Unavailable || outcome == NotFound || outcome == Conflict;
    }
}
