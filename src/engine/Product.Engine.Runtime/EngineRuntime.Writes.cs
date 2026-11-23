using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Model;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Operations;
using Product.Engine.Core.Reasons;
using Product.Engine.Runtime.Callers;
using Product.Engine.Runtime.Facts;
using Product.Engine.Runtime.Ports;
using Product.Engine.Runtime.Snapshots;

namespace Product.Engine.Runtime;

// A caller's row write, as a table message brings it (Create, Update or Delete on Dataverse; a resource endpoint on Azure).
public sealed class WriteCommand
{
    public WriteCommand(string entity, WriteOperation operation, string? rowId = null, JsonTreeNode? after = null, string? idempotencyKey = null)
    {
        if (string.IsNullOrEmpty(entity))
        {
            throw new ArgumentException("A write names its entity key.", nameof(entity));
        }

        Entity = entity;
        Operation = operation;
        RowId = string.IsNullOrEmpty(rowId) ? null : rowId;
        After = after;
        IdempotencyKey = string.IsNullOrEmpty(idempotencyKey) ? null : idempotencyKey;
    }

    // The entity key, such as "intake-request".
    public string Entity { get; }

    public WriteOperation Operation { get; }

    public string? RowId { get; }

    // The changed columns (lowerCamelCase). Null for a delete.
    public JsonTreeNode? After { get; }

    public string? IdempotencyKey { get; }

    public static WriteCommand Create(string entity, string afterJson, string? idempotencyKey = null)
    {
        return new WriteCommand(entity, WriteOperation.Create, null, CanonicalJson.Parse(afterJson), idempotencyKey);
    }

    public static WriteCommand Update(string entity, string rowId, string afterJson, string? idempotencyKey = null)
    {
        return new WriteCommand(entity, WriteOperation.Update, rowId, CanonicalJson.Parse(afterJson), idempotencyKey);
    }

    public static WriteCommand Delete(string entity, string rowId)
    {
        return new WriteCommand(entity, WriteOperation.Delete, rowId);
    }
}

public sealed partial class EngineRuntime
{
    // A caller's write: OP-08 with the facts the runtime fetches, the engine's computed values (PreOperation), the row
    // write and every effect, as engine-nested writes, in one transaction (document 25, sections 5.10 and 11.3).
    public RuntimeOutcome Write(CallerIdentity caller, WriteCommand command)
    {
        if (command == null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        return Run(caller, SnapshotPurpose.Write, call =>
        {
            WriteStep step = GuardedWrite(call, new WriteSpec(command.Entity, command.Operation)
            {
                RowId = command.RowId,
                After = command.After,
                IdempotencyKey = command.IdempotencyKey,
            })!;
            return new CallAnswer(RuntimeOutcome.Allowed, step.RowId, step.Created, step.Plan?.ToJson(), step.IsReplay, step.IsNoOp);
        });
    }

    // Puts one write through OP-08 and applies it. Returns null only when the guard refused with a tolerated code.
    private WriteStep? GuardedWrite(RuntimeCall call, WriteSpec spec)
    {
        CompiledSnapshot snapshot = spec.Snapshot ?? call.Snapshot;
        IStoreTransaction tx = call.Tx;
        bool configurationRow = IsConfigurationRow(spec.Entity);

        // Which row: an update or delete names it by ID, or a configuration row by key; a create gets a new ID.
        string? rowId = spec.RowId;
        StoredRow? before = null;
        if (spec.Operation != WriteOperation.Create)
        {
            before = rowId != null ? tx.Get(spec.Entity, rowId) : spec.Key != null ? tx.GetByKey(spec.Entity, spec.Key) : null;
            if (before == null)
            {
                throw CallFailure.Of(RuntimeOutcome.NotFound, ReasonCodes.RefNotFound, SnapshotReadName(spec.Entity) + " " + (rowId ?? spec.Key ?? "(none named)") + " does not exist.", SnapshotReadName(spec.Entity), rowId ?? spec.Key);
            }

            rowId = before.Id;
        }
        else
        {
            rowId ??= tx.NewId(spec.Entity);
        }

        // Plan and fetch the facts OP-08 needs (document 25, section 5.10: what the adapter fetches).
        string? bindingGroup = spec.Entity == "role-group-binding" ? RuntimeJson.Text(spec.After, "entraGroupObjectId") ?? before?.Text("entraGroupObjectId") : null;
        string? approver = spec.Entity == "approval-record" && spec.Operation == WriteOperation.Update && !spec.Nested ? RuntimeJson.Text(spec.After, "approver") : null;
        FactSet facts = FactSet.Run(tx, f => WriteFactsPlan(f, spec, snapshot, configurationRow, bindingGroup, approver));
        call.FactRounds += facts.Rounds;

        ConfigurationSetKind? setKind = null;
        if (configurationRow || spec.Entity == "configuration-set-version")
        {
            string? kindText = spec.Entity == "configuration-set"
                ? before?.Text("setKind") ?? RuntimeJson.Text(spec.After, "setKind")
                : facts.One("set")?.Text("setKind");
            if (EngineContract.ConfigurationSetKinds.TryParse(kindText, out ConfigurationSetKind parsed))
            {
                setKind = parsed;
            }
        }

        PrincipalKind? bindingKind = EngineContract.PrincipalKinds.TryParse(facts.One("binding-principal")?.Text("principalKind"), out PrincipalKind kind) ? kind : null;
        StoredRow? prior = facts.One("prior-request");
        PrincipalReferenceFact? approverReference = approver == null ? null : ApproverReference(facts, approver);

        // A caller's create names no row ID unless the caller gave one, so the Request Hash of a retry is the same request
        // (G-15). The engine's own creates name the ID the store gave, so their audit entries can point at the row.
        string? requestRowId = spec.Operation == WriteOperation.Create && !spec.Nested ? spec.RowId : rowId;
        var request = new WriteRequest(
            spec.Entity,
            spec.Operation,
            requestRowId,
            spec.After,
            before?.Columns,
            spec.Nested ? spec.Effect : null,
            spec.IdempotencyKey,
            setKind,
            bindingKind,
            spec.Acceptance,
            spec.GateReadiness,
            prior?.Text("requestHash"),
            approverReference);

        EvaluationContext context = call.Context(snapshot, spec.Nested);
        EvaluationResult<WritePlan?> result = WriteGuardOperation.Evaluate(snapshot.Snapshot, context, request);
        if (!spec.Nested || result.Outcome != "allowed")
        {
            call.Record("OP-08", new EvaluationResult<JsonTreeNode?>(result.Outcome, result.Payload?.ToJson(), result.Reasons, result.Trace, result.ConfigurationVersion, result.SnapshotHash, result.EngineVersion));
        }

        if (result.Outcome != "allowed")
        {
            if (result.Outcome == "refused" && spec.Tolerated.Length > 0
                && result.Reasons.Where(r => r.Severity == ReasonSeverity.Error).All(r => spec.Tolerated.Contains(r.Code, StringComparer.Ordinal)))
            {
                call.AddReasons(result.Reasons.Select(r => r.Severity == ReasonSeverity.Error ? new Reason(r.Code, ReasonSeverity.Warning, r.Message, r.Entity, r.Ref) : r));
                return null;
            }

            throw CallFailure.From(result.Outcome, result.Reasons);
        }

        if (!spec.Nested)
        {
            call.AddReasons(result.Reasons);
        }

        WritePlan plan = result.Payload!;
        if (plan.IsReplay)
        {
            // The same request with the same key: return the first result and write nothing.
            return new WriteStep(prior?.Text("rowId") ?? rowId, false, true, false, plan);
        }

        if (plan.IsNoOp)
        {
            return new WriteStep(rowId, false, false, true, plan);
        }

        // The engine's computed values, as PreOperation sets them (document 25, section 11.3, step 4). A nested write
        // already carries the engine's values.
        JsonTreeNode after = spec.After ?? RuntimeJson.Empty;
        JsonTreeNode computed = spec.Nested || spec.Operation == WriteOperation.Delete
            ? RuntimeJson.Empty
            : ComputeEngineColumns(call, spec, before == null ? after : RuntimeJson.Merge(before.Columns, after));

        JsonTreeNode columns = RuntimeJson.Merge(RuntimeJson.Merge(RuntimeJson.Without(after, plan.RemovedColumns), plan.SetColumns), computed);
        if (spec.Operation == WriteOperation.Create && !configurationRow && spec.Entity != "configuration-set-version" && spec.Entity != "configuration-draft" && !columns.TryGetMember("clientAppVerified", out _))
        {
            // The adapter fills Client App Verified: true only when the client came from a validated token claim.
            columns = RuntimeJson.With(columns, "clientAppVerified", JsonTreeNode.FromBoolean(call.Caller.ClientAppVerified && call.Caller.ClientApp != null));
        }

        switch (spec.Operation)
        {
            case WriteOperation.Create:
                tx.Create(spec.Entity, rowId, spec.Key, columns);
                break;
            case WriteOperation.Update:
                tx.Update(spec.Entity, rowId, columns);
                break;
            default:
                tx.Delete(spec.Entity, rowId);
                break;
        }

        call.Writes.Add(new AppliedWrite(spec.Entity, spec.Operation, rowId, spec.Key ?? before?.Key, spec.Nested ? spec.Effect : null, spec.Nested, columns));

        // Effects in order, each again through OP-08 as an engine-nested write. With an idempotency key, the Request Hash
        // goes into the first Audit Entry of the write, or into an Audit Entry of its own (document 25, section 8).
        bool requestRecorded = false;
        foreach (PlannedEffect effect in plan.Effects)
        {
            PlannedEffect planned = effect;
            if (!requestRecorded && spec.IdempotencyKey != null && !spec.Nested && effect.Kind == EffectKind.WriteAuditEntry)
            {
                planned = new PlannedEffect(effect.Kind, effect.Entity, effect.Operation, effect.RowId, effect.Key, RuntimeJson.With(effect.Columns, "requestHash", JsonTreeNode.FromString(plan.RequestHash)));
                requestRecorded = true;
            }

            RunEffect(call, planned, snapshot);
        }

        if (!requestRecorded && spec.IdempotencyKey != null && !spec.Nested)
        {
            WriteAudit(call, snapshot, SnapshotReadName(spec.Entity), rowId, "request-recorded", RuntimeJson.Empty, spec.IdempotencyKey, plan.RequestHash);
        }

        return new WriteStep(rowId, spec.Operation == WriteOperation.Create, false, false, plan);
    }

    private static IEnumerable<FactPlan> WriteFactsPlan(FactSet facts, WriteSpec spec, CompiledSnapshot snapshot, bool configurationRow, string? bindingGroup, string? approver)
    {
        bool needsSet = (configurationRow && spec.Entity != "configuration-set") || spec.Entity == "configuration-set-version";
        yield return FactPlan.Of(
            needsSet ? FactQuery.ByKey("set", "configuration-set", snapshot.ConfigurationSetKey) : null,
            bindingGroup == null ? null : FactQuery.Where("binding-principal", RowQuery.For("principal-reference").Where("entraObjectId", bindingGroup)),
            spec.IdempotencyKey == null || spec.Nested ? null : FactQuery.Where("prior-request", RowQuery.For("audit-entry").Where("requestKey", spec.IdempotencyKey)),
            approver == null ? null : FactQuery.Where(ApproverFacts[0], RowQuery.For("principal-reference").Where("entraObjectId", approver)));

        // An Entra object ID is a GUID, which the runner may send in another letter case than the store holds. When the
        // exact value finds no Principal Reference, the lower and upper case forms are read in one more round.
        if (approver != null && facts.All(ApproverFacts[0]).Count == 0)
        {
            string lower = approver.ToLowerInvariant();
            string upper = approver.ToUpperInvariant();
            yield return FactPlan.Of(
                string.Equals(lower, approver, StringComparison.Ordinal) ? null : FactQuery.Where(ApproverFacts[1], RowQuery.For("principal-reference").Where("entraObjectId", lower)),
                string.Equals(upper, approver, StringComparison.Ordinal) || string.Equals(upper, lower, StringComparison.Ordinal) ? null : FactQuery.Where(ApproverFacts[2], RowQuery.For("principal-reference").Where("entraObjectId", upper)));
        }
    }

    // The names of the approver's Principal Reference facts: the exact object ID, then its lower and upper case forms.
    private static readonly string[] ApproverFacts = { "approver-reference", "approver-reference-lower", "approver-reference-upper" };

    // The Principal Reference of an approval outcome's Approver, matched on the Entra object ID ignoring case (G-06, the
    // approver kind rule; document 22, O10 Approver Principal Kind). Null when the directory holds none. Should two
    // references name the same object, one that is not a user wins: refuse more, never less.
    private static PrincipalReferenceFact? ApproverReference(FactSet facts, string approver)
    {
        PrincipalReferenceFact? found = null;
        foreach (StoredRow row in ApproverFacts.SelectMany(name => facts.All(name)))
        {
            if (!string.Equals(row.Text("entraObjectId"), approver, StringComparison.OrdinalIgnoreCase)
                || !EngineContract.PrincipalKinds.TryParse(row.Text("principalKind"), out PrincipalKind kind))
            {
                continue;
            }

            if (found == null || (ContractFacts.CanApprove(found.PrincipalKind) && !ContractFacts.CanApprove(kind)))
            {
                found = new PrincipalReferenceFact(kind, row.Flag("isErased"));
            }
        }

        return found;
    }

    // Live configuration rows (C01, C03 to C45) are named by key. C02, C46 and the operational rows by ID.
    private static bool IsConfigurationRow(string entity)
    {
        return entity == "configuration-set" || (ConfigurationEntities.TryGet(entity, out _) && entity != "configuration-draft" && entity != "configuration-set-version");
    }

    // One effect of a write plan (document 25, section 5.10, Effect).
    private void RunEffect(RuntimeCall call, PlannedEffect effect, CompiledSnapshot snapshot)
    {
        if (++call.Depth > MaxEffectDepth)
        {
            throw new InvalidOperationException("Effects nest deeper than " + MaxEffectDepth + " levels; a planned effect loops.");
        }

        try
        {
            switch (effect.Kind)
            {
                case EffectKind.RecomputeApprovals:
                case EffectKind.AcceptDecision:
                    RecomputeApprovals(call, effect.RowId ?? throw new InvalidOperationException("A recompute-approvals effect names its decision."));
                    break;
                case EffectKind.ActivateConfiguration when effect.Entity == "Configuration Draft" && effect.Operation == WriteOperation.Update && effect.Key == null && effect.RowId != null
                    && effect.Columns.TryGetMember("changeDecision", out JsonTreeNode? decision) && decision.Kind == JsonTreeKind.String:
                    Activate(call, effect.RowId, decision.GetString());
                    break;
                default:
                    ApplyPlannedWrite(call, effect, snapshot, null);
                    break;
            }
        }
        finally
        {
            call.Depth--;
        }
    }

    // An effect that is one row write: an Audit Entry, the supersede and update-subject writes, and every write of an
    // activation or of the bootstrap. It passes OP-08 as an engine-nested write of its own effect kind.
    private WriteStep? ApplyPlannedWrite(RuntimeCall call, PlannedEffect effect, CompiledSnapshot snapshot, WriteAcceptance? acceptance)
    {
        string entity = RuntimeJson.EntityKey(effect.Entity);
        JsonTreeNode columns = effect.Columns;
        if (entity == "configuration-set-version" && effect.Operation == WriteOperation.Create && !columns.TryGetMember("configurationSet", out _))
        {
            // C02 Configuration Set is required (document 22). The activator plans the version; the runtime names its set.
            columns = RuntimeJson.With(columns, "configurationSet", JsonTreeNode.FromString(snapshot.ConfigurationSetKey));
        }

        return GuardedWrite(call, new WriteSpec(entity, effect.Operation)
        {
            RowId = effect.RowId,
            Key = effect.Key,
            After = effect.Operation == WriteOperation.Delete ? null : columns,
            Nested = true,
            Effect = effect.Kind,
            Acceptance = acceptance,
            Snapshot = snapshot,
        });
    }

    // An Audit Entry the runtime itself records: the request of an engine API call with an idempotency key.
    private void WriteAudit(RuntimeCall call, CompiledSnapshot snapshot, string entityName, string? rowId, string operation, JsonTreeNode changedValues, string? requestKey, string? requestHash)
    {
        ActorInfo actor = call.Actor;
        JsonTreeNode columns = RuntimeJson.Object(
            ("entityName", JsonTreeNode.FromString(entityName)),
            ("rowId", RuntimeJson.Text(rowId)),
            ("operation", JsonTreeNode.FromString(operation)),
            ("changedValuesJson", changedValues),
            ("actor", JsonTreeNode.FromString(actor.PrincipalId)),
            ("actorKind", JsonTreeNode.FromString(EngineContract.ActorKindMeanings.ToWire(actor.ActorKind))),
            ("onBehalfOf", RuntimeJson.Text(actor.OnBehalfOfPrincipalId)),
            ("clientApp", RuntimeJson.Text(call.Caller.ClientApp)),
            ("occurredOn", JsonTreeNode.FromUtcDateTime(call.AsOfUtc)),
            ("correlationId", JsonTreeNode.FromString(call.CorrelationId)),
            ("requestKey", RuntimeJson.Text(requestKey)),
            ("requestHash", RuntimeJson.Text(requestHash)));
        GuardedWrite(call, new WriteSpec("audit-entry", WriteOperation.Create) { After = columns, Nested = true, Effect = EffectKind.WriteAuditEntry, Snapshot = snapshot });
    }

    // The columns the engine computes on a caller's write (document 25, section 11.3, step 4; G-09): O08 Computed Tier from
    // OP-01, and the O14 gate level and flags from OP-04 in record mode. O10 Counts Toward Requirement is written by the
    // recompute (OP-03), and O19 Stale On is computed when read (OP-06), not stored by this build.
    private JsonTreeNode ComputeEngineColumns(RuntimeCall call, WriteSpec spec, JsonTreeNode row)
    {
        switch (spec.Entity)
        {
            case "deployment-record" when spec.Operation == WriteOperation.Create:
                return DeploymentFlags(call, row, spec.CodeApprovalKind);
            case "tier-assessment" when spec.After != null && spec.After.TryGetMember("answersJson", out _):
                return ComputedTier(call, row);
            default:
                return RuntimeJson.Empty;
        }
    }

    // O08 Computed Tier: OP-01 over the answers, for the application kind of the intake or application assessed.
    private JsonTreeNode ComputedTier(RuntimeCall call, JsonTreeNode row)
    {
        string? intakeId = RuntimeJson.Text(row, "intakeRequest");
        string? appId = RuntimeJson.Text(row, "businessApplication");
        FactSet facts = FactSet.Run(call.Tx, f => new[]
        {
            FactPlan.Of(
                intakeId == null ? null : FactQuery.ById("intake", "intake-request", intakeId),
                appId == null ? null : FactQuery.ById("application", "business-application", appId)),
        });
        call.FactRounds += facts.Rounds;
        string? kind = facts.One("intake")?.Text("proposedKind") ?? facts.One("application")?.Text("applicationKind");
        if (kind == null)
        {
            throw CallFailure.Of(OperationOutcomes.InvalidInput, ReasonCodes.InputRequired, "A Tier Assessment needs the application kind of the intake request or application it assesses.", "Tier Assessment", null);
        }

        JsonTreeNode answers = row.TryGetMember("answersJson", out JsonTreeNode? given) && given.Kind == JsonTreeKind.Array ? given : JsonTreeNode.CreateArray();
        EvaluationResult<JsonTreeNode?> result = Evaluate(call, EngineOperation.EvaluateTier, call.Snapshot, RuntimeJson.Object(("applicationKindKey", JsonTreeNode.FromString(kind)), ("answers", answers)));
        if (!IsAnswer(EngineOperation.EvaluateTier, result.Outcome))
        {
            throw CallFailure.From(result);
        }

        call.AddReasons(result.Reasons);
        return RuntimeJson.Object(("computedTier", RuntimeJson.Text(RuntimeJson.Text(result.Payload, "computedTierKey"))));
    }
}
