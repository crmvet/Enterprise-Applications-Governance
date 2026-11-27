using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Operations;
using Product.Engine.Core.Reasons;
using Product.Engine.Runtime.Callers;
using Product.Engine.Runtime.Ports;

namespace Product.Engine.Runtime;

// The state of one runtime call: one snapshot, one actor, one moment, one correlation ID and one transaction. Every
// write and effect of the call runs under the same snapshot (document 25, section 6.3: one version check per call).
internal sealed class RuntimeCall
{
    // The hash a context names before any version exists (the bootstrap of version 1). It is the hash of JSON null.
    internal static readonly string NoSnapshotHash = CanonicalHash.ComputeContentHash(JsonTreeNode.Null);

    public RuntimeCall(IStoreTransaction tx, CompiledSnapshot? snapshot, ActorInfo actor, CallerIdentity caller, DateTime asOfUtc, string correlationId)
    {
        Tx = tx;
        Snapshot = snapshot!;
        Actor = actor;
        NestedActor = new ActorInfo(actor.PrincipalId, actor.PrincipalKind, actor.ActorKind, actor.RoleKeys, actor.OnBehalfOfPrincipalId, isEngineNested: true);
        Caller = caller;
        AsOfUtc = asOfUtc;
        CorrelationId = correlationId;
    }

    public IStoreTransaction Tx { get; }

    // The snapshot in force for this call. Null only inside the bootstrap, before version 1 exists.
    public CompiledSnapshot Snapshot { get; set; }

    public ActorInfo Actor { get; }

    // The same identity with the engine-nested marker: effects run as the identity whose write started them (document
    // 25, section 5.10, "As the caller").
    public ActorInfo NestedActor { get; }

    public CallerIdentity Caller { get; }

    public DateTime AsOfUtc { get; }

    public string CorrelationId { get; }

    public List<AppliedWrite> Writes { get; } = new();

    // What the call changed outside the engine's rows, in a host table the host keeps (the approval outbox a platform admin
    // retries): entity, row ID and the state it left. The composed call's Audit Entry lists them beside its writes, so the
    // change is on the record though no engine row changed (document 25, B25-24).
    public List<(string Entity, string RowId, string State)> HostChanges { get; } = new();

    // What runs once the call's transaction has committed, in order: a change in a host table that must not happen when
    // the engine's own record of it is rolled back (security review: the outbox retry changed its row before
    // the Audit Entry of who asked was committed). Never run for a dry run, a replay or a failed call.
    public List<Action> AfterCommit { get; } = new();

    public List<Reason> Reasons { get; } = new();

    public Dictionary<string, JsonTreeNode> Results { get; } = new(StringComparer.Ordinal);

    public int FactRounds { get; set; }

    public int Depth { get; set; }

    // A dry run (validateOnly): every rule and write runs, then the transaction rolls back. It takes no record number.
    public bool DryRun { get; set; }

    // True while the engine settles gate decisions that waited for their gate (SettleHeldGateDecisions), so an acceptance
    // inside that step does not start the step again.
    public bool SettlingGates { get; set; }

    public EvaluationContext Context(CompiledSnapshot? snapshot = null, bool nested = false)
    {
        CompiledSnapshot? s = snapshot ?? Snapshot;
        return new EvaluationContext(
            s?.VersionNumber ?? 0,
            s?.ContentHash ?? NoSnapshotHash,
            AsOfUtc,
            nested ? NestedActor : Actor,
            CorrelationId,
            Caller.ClientApp);
    }

    public void Record(string name, EvaluationResult<JsonTreeNode?> result)
    {
        Results[name] = RuntimeJson.Object(
            ("outcome", JsonTreeNode.FromString(result.Outcome)),
            ("payload", result.Payload ?? JsonTreeNode.Null),
            ("configurationVersion", JsonTreeNode.FromInt64(result.ConfigurationVersion)));
    }

    public void AddReasons(IEnumerable<Reason> reasons)
    {
        foreach (Reason reason in reasons)
        {
            if (!Reasons.Contains(reason))
            {
                Reasons.Add(reason);
            }
        }
    }
}

// Ends a call with an outcome that writes nothing (document 25, section 7). The runtime catches it at the call's edge
// and rolls the transaction back.
internal sealed class CallFailure : Exception
{
    public CallFailure(string outcome, IEnumerable<Reason> reasons)
        : base(outcome)
    {
        Outcome = outcome;
        FailureReasons = reasons.ToList();
    }

    public string Outcome { get; }

    public IReadOnlyList<Reason> FailureReasons { get; }

    public static CallFailure Of(string outcome, string code, string message, string? entity = null, string? reference = null)
    {
        return new CallFailure(outcome, new[] { new Reason(code, ReasonSeverity.Error, message, entity, reference) });
    }

    // Maps an operation's error outcome word to the runtime's (they are the same words).
    public static CallFailure From(EvaluationResult<JsonTreeNode?> result)
    {
        return From(result.Outcome, result.Reasons);
    }

    // invalid-input and unavailable stay; a refusal is a conflict when a refusing code is in the Conflict category (a
    // reused idempotency key, document 25, section 7: HTTP 409), else refused.
    public static CallFailure From(string operationOutcome, IReadOnlyList<Reason> reasons)
    {
        if (operationOutcome == OperationOutcomes.InvalidInput || operationOutcome == OperationOutcomes.Unavailable)
        {
            return new CallFailure(operationOutcome, reasons);
        }

        bool conflict = reasons.Any(r => r.Severity == ReasonSeverity.Error && ReasonCatalog.TryGet(r.Code, out ReasonCodeInfo? info) && info.Categories.HasFlag(ReasonCategories.Conflict));
        return new CallFailure(conflict ? RuntimeOutcome.Conflict : OperationOutcomes.Refused, reasons);
    }
}

// What a guarded write did.
internal sealed class WriteStep
{
    public WriteStep(string rowId, bool created, bool isReplay, bool isNoOp, WritePlan? plan)
    {
        RowId = rowId;
        Created = created;
        IsReplay = isReplay;
        IsNoOp = isNoOp;
        Plan = plan;
    }

    public string RowId { get; }

    public bool Created { get; }

    public bool IsReplay { get; }

    public bool IsNoOp { get; }

    public WritePlan? Plan { get; }
}

// One write the runtime puts through OP-08: the caller's, or an engine-nested write of an effect.
internal sealed class WriteSpec
{
    public WriteSpec(string entity, WriteOperation operation)
    {
        Entity = entity;
        Operation = operation;
    }

    public string Entity { get; }

    public WriteOperation Operation { get; }

    public string? RowId { get; set; }

    // A configuration row is named by its key path.
    public string? Key { get; set; }

    public JsonTreeNode? After { get; set; }

    // True for the engine's own writes (the marker of document 25, section 11.2).
    public bool Nested { get; set; }

    // The effect a nested write belongs to (the nestedEffect proposal of this build). Null for the engine's own operations.
    public EffectKind? Effect { get; set; }

    public string? IdempotencyKey { get; set; }

    public WriteAcceptance? Acceptance { get; set; }

    public JsonTreeNode? GateReadiness { get; set; }

    // For a person's own approval answer: the approval answer check the runtime just ran for that person (G-06, D-38).
    public ApprovalAnswerBasis? ApprovalAnswer { get; set; }

    // For an accept-decision write: the decision's subject, for the update-subject effect (document 25, B25-8).
    public SubjectFacts? Subject { get; set; }

    // The snapshot to guard against, when not the call's (the bootstrap guards its writes against version 1).
    public CompiledSnapshot? Snapshot { get; set; }

    // For a deployment record: who approved the code merge, as the record step reports it (evidence only).
    public CodeApprovalKind CodeApprovalKind { get; set; } = CodeApprovalKind.Unknown;

    // Refusal codes that leave the row as it is instead of failing the call (the decision stays proposed while its gate
    // is not ready, G-14).
    public string[] Tolerated { get; set; } = Array.Empty<string>();
}
