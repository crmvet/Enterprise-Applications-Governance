using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;
using Product.Engine.Core.Snapshots;

namespace Product.Engine.Core.Operations;

// What an accept-decision write rests on (G-17). The runtime fills it from the OP-03 or OP-02 result it just computed,
// or from the bootstrap of version 1.
public sealed class WriteAcceptance
{
    public const string ApprovalsSatisfied = "approvals-satisfied";
    public const string EngineAccept = "engine-accept";
    public const string Bootstrap = "bootstrap";

    private static readonly string[] Bases = { ApprovalsSatisfied, EngineAccept, Bootstrap };

    public WriteAcceptance(string basis, string? approvalStatusOutcome = null, string? requiredApprovalsOutcome = null, EngineAcceptanceCondition? condition = null, bool noVersionExists = false)
    {
        if (Array.IndexOf(Bases, basis) < 0)
        {
            throw new ArgumentException("The basis is approvals-satisfied, engine-accept or bootstrap.", nameof(basis));
        }

        Basis = basis;
        ApprovalStatusOutcome = approvalStatusOutcome;
        RequiredApprovalsOutcome = requiredApprovalsOutcome;
        Condition = condition;
        NoVersionExists = noVersionExists;
    }

    // approvals-satisfied (OP-03 said satisfied), engine-accept (OP-02 returned engine-accept for a locked-list entry)
    // or bootstrap (version 1, document 21, section 4.8).
    public string Basis { get; }

    // The OP-03 outcome word, for approvals-satisfied.
    public string? ApprovalStatusOutcome { get; }

    // The OP-02 outcome word, for engine-accept.
    public string? RequiredApprovalsOutcome { get; }

    // The engine acceptance condition OP-02 applied, for engine-accept.
    public EngineAcceptanceCondition? Condition { get; }

    // For bootstrap: no Configuration Set Version exists yet.
    public bool NoVersionExists { get; }

    internal static WriteAcceptance? FromJson(WireObject? wire)
    {
        if (wire == null)
        {
            return null;
        }

        string basis = wire.RequiredString("basis");
        if (Array.IndexOf(Bases, basis) < 0)
        {
            throw OperationStop.Input(ReasonCodes.InputContractValueUnknown, wire.Where + ".basis is \"" + basis + "\", which is not one of " + string.Join(", ", Bases) + ".");
        }

        EngineAcceptanceCondition? condition = wire.Has("condition") ? wire.Contract("condition", EngineContract.EngineAcceptanceConditions) : null;
        return new WriteAcceptance(basis, wire.OptionalString("approvalStatusOutcome"), wire.OptionalString("requiredApprovalsOutcome"), condition, wire.Bool("noVersionExists", false));
    }
}

// OP-08 input (document 25, section 5.10, WriteRequest), with the facts the adapter fetches.
public sealed class WriteRequest
{
    public WriteRequest(
        string entity,
        WriteOperation operation,
        string? rowId = null,
        JsonTreeNode? after = null,
        JsonTreeNode? before = null,
        EffectKind? nestedEffect = null,
        string? idempotencyKey = null,
        ConfigurationSetKind? configurationSetKind = null,
        PrincipalKind? bindingPrincipalKind = null,
        WriteAcceptance? acceptance = null,
        JsonTreeNode? gateReadiness = null,
        string? priorRequestHash = null)
    {
        if (string.IsNullOrEmpty(entity))
        {
            throw new ArgumentException("A write names its entity.", nameof(entity));
        }

        if (after != null && after.Kind != JsonTreeKind.Object)
        {
            throw new ArgumentException("After is a JSON object of columns.", nameof(after));
        }

        if (before != null && before.Kind != JsonTreeKind.Object)
        {
            throw new ArgumentException("Before is a JSON object of columns.", nameof(before));
        }

        Entity = entity;
        Operation = operation;
        RowId = string.IsNullOrEmpty(rowId) ? null : rowId;
        After = after;
        Before = before;
        NestedEffect = nestedEffect;
        IdempotencyKey = string.IsNullOrEmpty(idempotencyKey) ? null : idempotencyKey;
        ConfigurationSetKind = configurationSetKind;
        BindingPrincipalKind = bindingPrincipalKind;
        Acceptance = acceptance;
        GateReadiness = gateReadiness;
        PriorRequestHash = string.IsNullOrEmpty(priorRequestHash) ? null : priorRequestHash;
    }

    // ---- What the caller sends ----

    // The entity key, such as "decision" or "tier" (kebab-case of the logical name).
    public string Entity { get; }

    public WriteOperation Operation { get; }

    // The row, for update and delete. On create, the ID the adapter gives the new row, when it knows it.
    public string? RowId { get; }

    // The changed columns and their new values (lowerCamelCase; configuration references as keys). Null for delete.
    public JsonTreeNode? After { get; }

    // A proposal of this build: the engine effect a nested write belongs to (activate-configuration, accept-decision,
    // and so on). The adapter sets it next to the engine-nested marker (document 25, section 11.2). Without it G-01
    // could not tell an activation write from another engine write.
    public EffectKind? NestedEffect { get; }

    public string? IdempotencyKey { get; }

    // ---- What the adapter fetches ----

    // The current values of the columns the rules need (a pre-image on Dataverse, the row on Azure).
    public JsonTreeNode? Before { get; }

    // For a configuration row or version: the kind of its configuration set (G-02).
    public ConfigurationSetKind? ConfigurationSetKind { get; }

    // For a Role Group Binding: the principal kind of the directory object it names (G-12).
    public PrincipalKind? BindingPrincipalKind { get; }

    // For an accept-decision write: what the acceptance rests on (G-17).
    public WriteAcceptance? Acceptance { get; }

    // For accepting a gate decision: the OP-07 payload of that gate, passed on unchanged (G-14).
    public JsonTreeNode? GateReadiness { get; }

    // The Request Hash stored with this idempotency key, when the key was used before (G-15; document 25, section 8).
    public string? PriorRequestHash { get; }

    // The Request Hash of this request: SHA-256 over the canonical form of what the caller sends.
    public string RequestHash()
    {
        var members = new List<KeyValuePair<string, JsonTreeNode>>
        {
            new("entity", JsonTreeNode.FromString(Entity)),
            new("operation", JsonTreeNode.FromString(EngineContract.WriteOperations.ToWire(Operation))),
        };
        if (RowId != null)
        {
            members.Add(new KeyValuePair<string, JsonTreeNode>("rowId", JsonTreeNode.FromString(RowId)));
        }

        if (After != null)
        {
            members.Add(new KeyValuePair<string, JsonTreeNode>("after", After));
        }

        if (NestedEffect.HasValue)
        {
            members.Add(new KeyValuePair<string, JsonTreeNode>("nestedEffect", JsonTreeNode.FromString(EngineContract.EffectKinds.ToWire(NestedEffect.Value))));
        }

        return CanonicalHash.ComputeRequestHash(JsonTreeNode.CreateObject(members));
    }

    public static WriteRequest FromJson(JsonTreeNode input)
    {
        var wire = new WireObject(
            input,
            "input",
            "entity",
            "operation",
            "rowId",
            "after",
            "nestedEffect",
            "idempotencyKey",
            "before",
            "configurationSetKind",
            "bindingPrincipalKind",
            "acceptance",
            "gateReadiness",
            "priorRequestHash");

        EffectKind? nested = wire.Has("nestedEffect") ? wire.Contract("nestedEffect", EngineContract.EffectKinds) : null;
        ConfigurationSetKind? setKind = wire.Has("configurationSetKind") ? wire.Contract("configurationSetKind", EngineContract.ConfigurationSetKinds) : null;
        PrincipalKind? bindingKind = wire.Has("bindingPrincipalKind") ? wire.Contract("bindingPrincipalKind", EngineContract.PrincipalKinds) : null;
        WriteAcceptance? acceptance = WriteAcceptance.FromJson(wire.OptionalObject("acceptance", "basis", "approvalStatusOutcome", "requiredApprovalsOutcome", "condition", "noVersionExists"));

        return new WriteRequest(
            wire.RequiredKey("entity"),
            wire.Contract("operation", EngineContract.WriteOperations),
            wire.OptionalString("rowId"),
            wire.OptionalRawObject("after"),
            wire.OptionalRawObject("before"),
            nested,
            wire.OptionalString("idempotencyKey"),
            setKind,
            bindingKind,
            acceptance,
            wire.OptionalRawObject("gateReadiness"),
            wire.OptionalString("priorRequestHash"));
    }
}

// A follow-on write the adapter applies in the same transaction, as an engine-nested write of its kind (document 25,
// section 5.10, Effect). Operation and Key are proposals: an effect can create a row, and a configuration row is named by
// its key, never by an ID.
public sealed class PlannedEffect
{
    public PlannedEffect(EffectKind kind, string entity, WriteOperation operation, string? rowId, string? key, JsonTreeNode columns)
    {
        Kind = kind;
        Entity = entity;
        Operation = operation;
        RowId = rowId;
        Key = key;
        Columns = columns ?? throw new ArgumentNullException(nameof(columns));
    }

    public EffectKind Kind { get; }

    // The logical entity to change, such as "Decision".
    public string Entity { get; }

    public WriteOperation Operation { get; }

    public string? RowId { get; }

    public string? Key { get; }

    // The logical columns and values (lowerCamelCase).
    public JsonTreeNode Columns { get; }

    public JsonTreeNode ToJson()
    {
        return new WireWriter()
            .Add("kind", EngineContract.EffectKinds.ToWire(Kind))
            .Add("entity", Entity)
            .Add("operation", EngineContract.WriteOperations.ToWire(Operation))
            .Add("rowId", RowId)
            .Add("key", Key)
            .Add("columns", Columns)
            .Build();
    }
}

// One refusing reason of a write plan, with the guard rule it maps to.
public sealed class WriteRefusal
{
    public WriteRefusal(string code, string? ruleId)
    {
        Code = code;
        RuleId = ruleId;
    }

    public string Code { get; }

    // G-01 to G-17, from the reason catalog. Null for a proposed code.
    public string? RuleId { get; }

    internal JsonTreeNode ToJson()
    {
        return new WireWriter().Add("code", Code).Add("ruleId", RuleId).Build();
    }
}

// OP-08 output (document 25, section 5.10, WritePlan).
public sealed class WritePlan : IOperationPayload
{
    public WritePlan(
        bool allowed,
        string requestHash,
        bool isReplay,
        bool isNoOp,
        IEnumerable<WriteRefusal> refusals,
        JsonTreeNode setColumns,
        IEnumerable<string> removedColumns,
        IEnumerable<PlannedEffect> effects)
    {
        Allowed = allowed;
        RequestHash = requestHash;
        IsReplay = isReplay;
        IsNoOp = isNoOp;
        Refusals = new ReadOnlyCollection<WriteRefusal>(refusals.ToList());
        SetColumns = setColumns;
        RemovedColumns = new ReadOnlyCollection<string>(removedColumns.ToList());
        Effects = new ReadOnlyCollection<PlannedEffect>(effects.ToList());
    }

    // False when any rule refused.
    public bool Allowed { get; }

    // The Request Hash the adapter stores with the idempotency key in the same transaction (section 8).
    public string RequestHash { get; }

    // True when the idempotency key was used before with this same request: the adapter returns the first result and
    // writes nothing.
    public bool IsReplay { get; }

    // True when the write changes nothing the guard cares about, such as the same approval outcome written again or an
    // erasure of a principal already erased. A proposal: section 8 calls these no-ops.
    public bool IsNoOp { get; }

    // The refusing reasons, in the order of the result's reasons (errors first).
    public IReadOnlyList<WriteRefusal> Refusals { get; }

    // Stamps and engine values to write in the same operation (G-08, G-09, G-10).
    public JsonTreeNode SetColumns { get; }

    // Caller values in columns the engine computes, which the adapter must not write. A proposal of this build.
    public IReadOnlyList<string> RemovedColumns { get; }

    public IReadOnlyList<PlannedEffect> Effects { get; }

    public JsonTreeNode ToJson()
    {
        return new WireWriter()
            .Add("allowed", Allowed)
            .Add("requestHash", RequestHash)
            .Add("isReplay", IsReplay)
            .Add("isNoOp", IsNoOp)
            .Add("refusals", WireWriter.Array(Refusals.Select(r => r.ToJson())))
            .Add("setColumns", SetColumns)
            .Add("removedColumns", RemovedColumns)
            .Add("effects", WireWriter.Array(Effects.Select(e => e.ToJson())))
            .Build();
    }
}

// OP-08 Guard a write (document 25, section 5.10). Every write, from any caller, passes through this rule set: G-01 to
// G-17, the approver kind rule (an approval counts only from a person, AN-05 in document 26), the erasure effect
// (document 22, section 2.7; document 26, section 9.5), the delete policy (document 22, section 2.5) and RA-06 (agent
// principals never write). The rules are fixed engine behavior over engine-contract meanings; the values they compare
// come from the snapshot in force. Every failed rule is listed, errors first, as OP-03 does.
//
// WORKING DEFAULT (open design question 1): the entity facts in WriteGuardEntities and the fixed keys in EngineKeys.
// WORKING DEFAULT (open design question 8): G-17 accepts only after OP-03 satisfied, for a locked-list entry, or for
// the bootstrap of version 1.
// WORKING DEFAULT (open design question 9): an erasure is a person's act; an AI client cannot perform one.
// WORKING DEFAULT (open design question 12): the approval runner writes outcomes as the holder of the approval writer
// workload role, a service principal or managed identity with a workload grant.
public static class WriteGuardOperation
{
    public static EvaluationResult<WritePlan?> Evaluate(ConfigurationSnapshot snapshot, EvaluationContext context, WriteRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return OperationShell.Run(context, snapshot, needsSnapshot: true, () => new GuardRun(snapshot, context, request).Run());
    }

    public static EvaluationResult<WritePlan?> Evaluate(ConfigurationSnapshot snapshot, EvaluationContext context, JsonTreeNode input)
    {
        return OperationShell.Run(context, snapshot, needsSnapshot: true, () => new GuardRun(snapshot, context, WriteRequest.FromJson(input)).Run());
    }

    private sealed class GuardRun
    {
        private static readonly string[] PositiveMeanings = { "accepted", "registered", "approved", "completed" };
        private static readonly TimeSpan PatternTimeout = TimeSpan.FromSeconds(1);

        private readonly ConfigurationSnapshot _snapshot;
        private readonly EvaluationContext _context;
        private readonly WriteRequest _request;
        private readonly ActorInfo _actor;
        private readonly JsonTreeNode _after;
        private readonly JsonTreeNode _before;
        private readonly List<Reason> _reasons = new();
        private readonly SortedDictionary<string, JsonTreeNode> _set = new(StringComparer.Ordinal);
        private readonly SortedSet<string> _removed = new(StringComparer.Ordinal);
        private readonly List<PlannedEffect> _effects = new();
        private readonly List<RuleTrace> _trace = new();
        private readonly HashSet<string> _handled = new(StringComparer.Ordinal);
        private GuardEntity _entity = null!;
        private bool _noOp;

        public GuardRun(ConfigurationSnapshot snapshot, EvaluationContext context, WriteRequest request)
        {
            _snapshot = snapshot;
            _context = context;
            _request = request;
            _actor = context.Actor;
            _after = request.After ?? GuardJson.Empty;
            _before = request.Before ?? GuardJson.Empty;
        }

        private bool Nested => _actor.IsEngineNested;

        private EffectKind? Effect => _request.NestedEffect;

        private WriteOperation Op => _request.Operation;

        private string EntityName => _entity.LogicalName;

        public OperationAnswer<WritePlan> Run()
        {
            if (!WriteGuardEntities.TryGet(_request.Entity, out GuardEntity? entity))
            {
                throw OperationStop.Input(ReasonCodes.InputUnknownKey, "input.entity is \"" + _request.Entity + "\", which is not a product entity the guard knows.", _request.Entity);
            }

            _entity = entity;
            CheckShape();
            CheckColumns();
            string hash = _request.RequestHash();

            // G-15: a reused idempotency key. The same request again is a replay; another request is refused.
            if (_request.PriorRequestHash != null)
            {
                if (string.Equals(_request.PriorRequestHash, hash, StringComparison.Ordinal))
                {
                    return Answer(hash, isReplay: true);
                }

                Refuse(
                    ReasonCodes.RequestIdempotencyKeyReused,
                    "The idempotency key " + SnapshotRead.Quote(_request.IdempotencyKey!) + " was used before with a different request, so this write is refused. Use a new key for a new request.",
                    _request.IdempotencyKey);
                return Answer(hash, isReplay: false);
            }

            // Document 26, RA-06: an agent identity or agent user never writes in the default configuration.
            if (_actor.PrincipalKind == PrincipalKind.AgentIdentity || _actor.PrincipalKind == PrincipalKind.AgentUser || _actor.ActorKind == ActorKindMeaning.AgentIdentity)
            {
                Refuse(
                    ReasonCodes.ActorAgentWriteRefused,
                    "The caller is " + EngineContract.PrincipalKinds.ToWire(_actor.PrincipalKind) + " acting as " + EngineContract.ActorKindMeanings.ToWire(_actor.ActorKind)
                    + ", and an agent identity or agent user never writes, so a person must make this change.",
                    null);
            }

            if (AppendOnly())
            {
                return Answer(hash, isReplay: false);
            }

            switch (_entity.Kind)
            {
                case GuardEntityKind.Configuration:
                    ConfigurationRow();
                    break;
                case GuardEntityKind.ConfigurationSetVersion:
                    ConfigurationVersion();
                    break;
                case GuardEntityKind.ConfigurationDraft:
                    ConfigurationDraft();
                    break;
                case GuardEntityKind.AuditEntry:
                    AuditEntry();
                    break;
                default:
                    Operational();
                    break;
            }

            if (!HasRefusal && !_noOp && Op != WriteOperation.Delete)
            {
                PlatformColumns();
                Stamps();
            }

            return Answer(hash, isReplay: false);
        }

        private bool HasRefusal => _reasons.Any(r => r.Severity == ReasonSeverity.Error);

        // ---- Request shape ----

        private void CheckShape()
        {
            if (Op == WriteOperation.Delete)
            {
                if (_request.After != null)
                {
                    throw OperationStop.Input(ReasonCodes.InputMalformed, "input.after must be absent for a delete.");
                }
            }
            else if (_request.After == null)
            {
                throw OperationStop.Input(ReasonCodes.InputRequired, "input.after is required for a " + EngineContract.WriteOperations.ToWire(Op) + ".");
            }

            if (Op != WriteOperation.Create && _request.RowId == null)
            {
                throw OperationStop.Input(ReasonCodes.InputRequired, "input.rowId is required for a " + EngineContract.WriteOperations.ToWire(Op) + ".");
            }

            if (_request.NestedEffect.HasValue && !Nested)
            {
                throw OperationStop.Input(ReasonCodes.InputMalformed, "input.nestedEffect is given, but the actor is not engine-nested. Only the engine's own writes name an effect.");
            }

            if (_request.PriorRequestHash != null && _request.IdempotencyKey == null)
            {
                throw OperationStop.Input(ReasonCodes.InputRequired, "input.idempotencyKey is required when facts give a prior request hash.");
            }

            if (_request.PriorRequestHash != null && !CanonicalHash.IsHash(_request.PriorRequestHash))
            {
                throw OperationStop.Input(ReasonCodes.InputMalformed, "priorRequestHash must be a lowercase SHA-256 hash.");
            }
        }

        // Every column a write names is a column of its entity in the logical model (model/logical-model.json, compiled
        // into the engine), in the contract layer's exact lowerCamelCase form (document 20, section 3.3). An unknown name,
        // such as Status for status or RespondedOn for respondedOn, is refused before any rule runs, so it can never pass
        // G-06, G-08 or G-17 unjudged in a store that matches names ignoring case. Live configuration rows carry the
        // bundle's field names instead and change only inside activation (G-01), so they are not checked here.
        private void CheckColumns()
        {
            if (_request.After == null)
            {
                return;
            }

            if (!_entity.ChecksColumns)
            {
                CaseVariantColumns();
                return;
            }

            string[] unknown = _after.Members.Select(m => m.Key).Where(k => !_entity.Model.HasColumn(k)).ToArray();
            if (unknown.Length == 0)
            {
                return;
            }

            string[] near = unknown
                .Select(k => _entity.Model.Columns.FirstOrDefault(c => string.Equals(c, k, StringComparison.OrdinalIgnoreCase)))
                .Where(c => c != null)
                .Select(c => c!)
                .ToArray();
            throw OperationStop.Input(
                ReasonCodes.InputUnknownColumn,
                "input.after names " + string.Join(", ", unknown.Select(k => "\"" + k + "\"")) + ", which " + (unknown.Length == 1 ? "is not a column" : "are not columns") + " of "
                + EntityName + " in the logical model" + (near.Length == 0 ? string.Empty : " (the model's names are case-sensitive: " + string.Join(", ", near) + ")")
                + ". Write the exact lowerCamelCase names of the model's columns.",
                unknown[0]);
        }

        // Live configuration rows carry the bundle's field names, so they are not checked against the model's list. A
        // name that is one of the model's columns (or Key or Origin, which G-03 reads) in another case is still refused:
        // "Key" for "key" would pass G-03 unjudged in a store that matches names ignoring case. No
        // bundle field name differs from a model column only by case.
        private void CaseVariantColumns()
        {
            string[] read = { "key", "origin" };
            string[] variants = _after.Members.Select(m => m.Key)
                .Where(k => !_entity.Model.HasColumn(k) && Array.IndexOf(read, k) < 0)
                .Where(k => read.Concat(_entity.Model.Columns).Any(c => string.Equals(c, k, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            if (variants.Length == 0)
            {
                return;
            }

            throw OperationStop.Input(
                ReasonCodes.InputUnknownColumn,
                "input.after names " + string.Join(", ", variants.Select(k => "\"" + k + "\"")) + ", which differ" + (variants.Length == 1 ? "s" : string.Empty) + " only by case from a column of "
                + EntityName + ". Names are case-sensitive: write the exact lowerCamelCase name.",
                variants[0]);
        }

        // ---- History class A (document 22, section 2.3; the class comes from the logical model) ----

        // Inserted once, never updated, never deleted, by any writer, the engine included: C02, O20 and O24 today. C02 and
        // O24 keep their G-07 codes.
        private bool AppendOnly()
        {
            if (_entity.HistoryClass != HistoryClass.AppendOnly || Op == WriteOperation.Create)
            {
                return false;
            }

            switch (_entity.Kind)
            {
                case GuardEntityKind.ConfigurationSetVersion:
                    Refuse(ReasonCodes.ConfigVersionImmutable, "A Configuration Set Version never changes and is never deleted once created. A change is a new version.", _request.RowId);
                    break;
                case GuardEntityKind.AuditEntry:
                    Refuse(ReasonCodes.AuditAppendOnly, "Audit entries are never updated or deleted.", _request.RowId);
                    break;
                default:
                    if (Op == WriteOperation.Delete)
                    {
                        Refuse(ReasonCodes.RecordDeleteNotAllowed, EntityName + " rows are written once and never deleted.", _request.RowId);
                    }
                    else
                    {
                        Refuse(ReasonCodes.RecordAppendOnly, "This " + EntityName + " is written once and never changes. Add a new " + EntityName + " instead.", _request.RowId);
                    }

                    break;
            }

            return true;
        }

        // ---- History class F (document 22, section 2.3; the class comes from the logical model) ----

        // Editable only while in its draft state, frozen once final, for every writer, the engine included (document 26,
        // RA-05). O09, O10 and C46 judge their final state and their documented transitions in their own rules (G-05,
        // G-06, G-16). Every other class F entity is final at insert (O14; O13 apart from its fill-once columns), or once
        // its final column holds a value (O08 Tier Decision, O22 Finished On). A fill-once column of the model can be
        // written once while it is empty, and never changed after that, final row or not.
        //
        // The adapter passes the row as it is (a pre-image on Dataverse, the row on Azure). A final column or a fill-once
        // column that the facts leave out reads as empty.
        private bool FrozenRow()
        {
            if (_entity.HistoryClass != HistoryClass.FreezeOnFinal || Op != WriteOperation.Update
                || _entity.FinalWhen == FinalWhen.MeaningFinal || _entity.FinalWhen == FinalWhen.DraftStatus)
            {
                return false;
            }

            bool final = _entity.FinalWhen == FinalWhen.AtInsert || HasValue(_before, _entity.FinalColumn!);

            // An update with no columns still restamps the row (Performed By, Engine Checked On), so it changes a final
            // row too.
            if (final && _after.Members.Count == 0)
            {
                string when = _entity.FinalWhen == FinalWhen.AtInsert ? "is final once written" : "is final since its " + _entity.FinalColumn + " was set";
                Refuse(ReasonCodes.RecordFinal, "This " + EntityName + " " + when + ", and an update with no columns would still change its stamps, so it is refused.", _request.RowId);
                return true;
            }

            var frozen = new List<string>();
            var filled = new List<string>();
            foreach (KeyValuePair<string, JsonTreeNode> member in _after.Members)
            {
                if (_entity.Model.IsFillOnce(member.Key))
                {
                    if (_before.TryGetMember(member.Key, out JsonTreeNode? old) && HasValue(_before, member.Key) && !old.Equals(member.Value))
                    {
                        filled.Add(member.Key);
                    }

                    continue;
                }

                if (final)
                {
                    frozen.Add(member.Key);
                }
            }

            if (filled.Count > 0)
            {
                Refuse(ReasonCodes.RecordFinal, EntityName + " " + string.Join(", ", filled) + " " + (filled.Count == 1 ? "is" : "are") + " filled once and never changed after that.", _request.RowId);
            }

            if (frozen.Count > 0)
            {
                string since = _entity.FinalWhen == FinalWhen.AtInsert ? "is final once written" : "is final since its " + _entity.FinalColumn + " was set";
                string fillable = _entity.Model.FillOnceColumns.Count == 0 ? string.Empty : " Only " + string.Join(", ", _entity.Model.FillOnceColumns) + " can still be filled once while empty.";
                Refuse(
                    ReasonCodes.RecordFinal,
                    "This " + EntityName + " " + since + ", so it never changes (" + string.Join(", ", frozen) + " given)." + fillable
                    + (string.Equals(_entity.Key, "deployment-record", StringComparison.Ordinal) ? " Record a correcting deployment record that names it in Corrects Record instead." : string.Empty),
                    _request.RowId);
            }

            return filled.Count > 0 || frozen.Count > 0;
        }

        private static bool HasValue(JsonTreeNode node, string member)
        {
            return node.TryGetMember(member, out JsonTreeNode? value) && value.Kind != JsonTreeKind.Null && !(value.Kind == JsonTreeKind.String && value.GetString().Length == 0);
        }

        // ---- Configuration rows, C01 and C03 to C45 (G-01 to G-04, G-12) ----

        private void ConfigurationRow()
        {
            ConfigurationSetKind setKind = RequireSetKind();

            // G-02: rows of a preset never change, not even inside activation. Seed staging will name its own effect.
            if (setKind == ConfigurationSetKind.Preset)
            {
                Refuse(ReasonCodes.ConfigPresetReadOnly, "This " + EntityName + " row belongs to a preset, and rows of a preset never change. Change the working set through a configuration change instead.", Text(_before, "key"));
            }

            // G-01: only inside activation, as engine-nested writes. Whoever calls: a configuration admin, a tool, a system administrator.
            bool inActivation = Nested && Effect == EffectKind.ActivateConfiguration;
            if (!inActivation)
            {
                Refuse(
                    ReasonCodes.ConfigActivationOnly,
                    EntityName + " rows change only inside activation, after people accept a configuration change. Propose the change with propose_configuration_change instead.",
                    Text(_before, "key") ?? Text(_after, "key"));
            }

            // G-04: a configuration row is never deleted; activation retires it (Is Active no). Every live row is in the
            // active version's snapshot, so it is always referenced by a version.
            if (Op == WriteOperation.Delete)
            {
                Refuse(ReasonCodes.ConfigInUse, EntityName + " " + Quote(Text(_before, "key") ?? _request.RowId!) + " is part of a configuration version, so it is never deleted. Retire it in a configuration change instead.", Text(_before, "key"));
                return;
            }

            // G-03: a key never changes.
            if (Op == WriteOperation.Update && _after.TryGetMember("key", out JsonTreeNode? newKey))
            {
                string? oldKey = Text(_before, "key") ?? throw OperationStop.Input(ReasonCodes.InputRequired, "facts.before.key is required to change a configuration row's columns.");
                if (newKey.Kind != JsonTreeKind.String || !string.Equals(newKey.GetString(), oldKey, StringComparison.Ordinal))
                {
                    Refuse(ReasonCodes.ConfigKeyImmutable, EntityName + " " + Quote(oldKey) + " cannot change its key. Retire it and add a row with the new key.", oldKey);
                }
            }

            // G-03: inside activation, a changed shipped row becomes local, unless the row as activated names its origin.
            if (Op == WriteOperation.Update && inActivation
                && string.Equals(Text(_before, "origin"), EngineContract.Origins.ToWire(Origin.Shipped), StringComparison.Ordinal)
                && !_after.TryGetMember("origin", out _)
                && _after.Members.Any(m => !string.Equals(m.Key, "key", StringComparison.Ordinal)))
            {
                Set("origin", JsonTreeNode.FromString(EngineContract.Origins.ToWire(Origin.Local)));
            }

            // G-12: a Role Group Binding points only to a group (D-11). Validated at activation too (V35).
            if (string.Equals(_entity.Key, "role-group-binding", StringComparison.Ordinal))
            {
                PrincipalKind kind = _request.BindingPrincipalKind ?? throw OperationStop.Input(ReasonCodes.InputRequired, "facts.bindingPrincipalKind is required for a Role Group Binding.");
                if (!ContractFacts.CanBeRoleBindingTarget(kind))
                {
                    Refuse(ReasonCodes.RoleBindingNotGroup, "The binding names a " + EngineContract.PrincipalKinds.ToWire(kind) + ", and a role binds only to an Entra group (D-11).", Text(_after, "key") ?? Text(_before, "key"));
                }
            }
        }

        // ---- C02 Configuration Set Version (G-01, G-02, G-07) ----

        // Only a create reaches this point: class A (AppendOnly) refuses every update and delete.
        private void ConfigurationVersion()
        {
            ConfigurationSetKind setKind = RequireSetKind();
            if (setKind == ConfigurationSetKind.Preset)
            {
                Refuse(ReasonCodes.ConfigPresetReadOnly, "A preset's version is staged with the release's seed, never created here.", null);
            }

            if (!(Nested && Effect == EffectKind.ActivateConfiguration))
            {
                Refuse(ReasonCodes.ConfigActivationOnly, "A Configuration Set Version is created only inside activation, after people accept a configuration change.", null);
            }
        }

        // ---- C46 Configuration Draft (G-16) ----

        private void ConfigurationDraft()
        {
            if (Op == WriteOperation.Delete)
            {
                Refuse(ReasonCodes.RecordDeleteNotAllowed, "A Configuration Draft is never deleted. Withdraw it, and it stays as evidence.", _request.RowId);
                return;
            }

            string? beforeStatus = Op == WriteOperation.Create ? null : Text(_before, "draftStatus") ?? throw OperationStop.Input(ReasonCodes.InputRequired, "facts.before.draftStatus is required to change a Configuration Draft.");
            string? afterStatus = Text(_after, "draftStatus");
            string draft = EngineContract.DraftStatusMeanings.ToWire(DraftStatusMeaning.Draft);
            string submitted = EngineContract.DraftStatusMeanings.ToWire(DraftStatusMeaning.Submitted);
            string activated = EngineContract.DraftStatusMeanings.ToWire(DraftStatusMeaning.Activated);
            string discarded = EngineContract.DraftStatusMeanings.ToWire(DraftStatusMeaning.Discarded);
            if (afterStatus != null && !EngineContract.DraftStatusMeanings.TryParse(afterStatus, out _))
            {
                throw OperationStop.Input(ReasonCodes.InputContractValueUnknown, "input.after.draftStatus is \"" + afterStatus + "\", which is not one of " + string.Join(", ", EngineContract.DraftStatusMeanings.WireValues) + ".");
            }

            // A stored status outside the contract list (such as "Activated") cannot read as not final.
            if (beforeStatus != null && !EngineContract.DraftStatusMeanings.TryParse(beforeStatus, out _))
            {
                throw OperationStop.Input(ReasonCodes.InputContractValueUnknown, "facts.before.draftStatus is \"" + beforeStatus + "\", which is not one of " + string.Join(", ", EngineContract.DraftStatusMeanings.WireValues) + ".");
            }

            // A submitted draft is frozen; an activated or discarded draft is final.
            if (beforeStatus == activated || beforeStatus == discarded)
            {
                Refuse(ReasonCodes.ConfigDraftFrozen, "The Configuration Draft is " + beforeStatus + ", so it never changes again.", _request.RowId);
                return;
            }

            if (!Nested)
            {
                // The caller writes only the patch and the rationale; every other column is the engine's.
                EffectOnlyColumns();
                if (beforeStatus == submitted)
                {
                    Refuse(ReasonCodes.ConfigDraftFrozen, "The Configuration Draft is submitted and frozen. Withdraw it and start a new draft to change it.", _request.RowId);
                }

                RequireConfigurationAdmin();
                return;
            }

            if (Effect == null)
            {
                // The engine's draft operations (propose, submit, withdraw) act for the caller: create, edit, submit or
                // withdraw, only for a person who holds a role with Can Change Configuration.
                bool allowedMove = (beforeStatus == null && (afterStatus == null || afterStatus == draft))
                    || (beforeStatus == draft && (afterStatus == null || afterStatus == draft || afterStatus == submitted || afterStatus == discarded))
                    || (beforeStatus == submitted && afterStatus == discarded && OnlyColumns(WriteGuardEntities.DraftOutcomeColumns));
                if (!allowedMove)
                {
                    Refuse(ReasonCodes.ConfigDraftFrozen, "A Configuration Draft moves from " + (beforeStatus ?? "new") + " to " + (afterStatus ?? beforeStatus ?? draft) + " only as an engine effect, or not at all.", _request.RowId);
                }

                RequireConfigurationAdmin();
                return;
            }

            // An engine effect: activation (submitted to activated), or the discard that follows a rejection, an
            // expiry or a rebase conflict. It writes only the outcome columns.
            bool effectMove = beforeStatus == submitted
                && ((afterStatus == activated && Effect == EffectKind.ActivateConfiguration) || afterStatus == discarded)
                && OnlyColumns(WriteGuardEntities.DraftOutcomeColumns);
            if (!effectMove)
            {
                Refuse(ReasonCodes.ConfigDraftFrozen, "An engine effect writes only the outcome columns of a submitted Configuration Draft: activated by activation, or discarded.", _request.RowId);
            }
        }

        // G-16: the person behind the call holds an active role with Can Change Configuration.
        private void RequireConfigurationAdmin()
        {
            bool person = _actor.ActorKind == ActorKindMeaning.Person || (_actor.ActorKind == ActorKindMeaning.AiClientForPerson && _actor.OnBehalfOfPrincipalId != null);
            string[] adminRoles = SnapshotRead.ActiveRows(_snapshot, "role")
                .Where(r => SnapshotRead.Flag(r, "canChangeConfiguration") && !SnapshotRead.Flag(r, "isWorkloadRole") && !SnapshotRead.Flag(r, "isReadOnly"))
                .Select(r => r.KeyPath)
                .ToArray();
            string? held = adminRoles.FirstOrDefault(_actor.HoldsRole);
            if (!person || !ContractFacts.CanApprove(_actor.PrincipalKind) || held == null)
            {
                Refuse(
                    ReasonCodes.ConfigNotConfigurationAdmin,
                    "Only a person signed in as a user who holds a role with Can Change Configuration (" + (adminRoles.Length == 0 ? "none is active" : string.Join(", ", adminRoles)) + ") can work on a Configuration Draft, directly or through an AI client acting for them.",
                    _request.RowId);
                return;
            }

            _trace.Add(new RuleTrace("Role", held, RuleTraceRole.Matched));
        }

        // ---- O24 Audit Entry (G-07) ----

        // Only a create reaches this point: class A (AppendOnly) refuses every update and delete.
        private void AuditEntry()
        {
            if (!Nested)
            {
                Refuse(ReasonCodes.EngineEffectOnly, "Only the engine writes an Audit Entry, as an effect of the write it records.", null);
            }
        }

        // ---- Operational rows, O01 to O23 ----

        private void Operational()
        {
            if (Op == WriteOperation.Delete)
            {
                Delete();
                return;
            }

            if (FrozenRow())
            {
                return;
            }

            if (string.Equals(_entity.Key, "principal-reference", StringComparison.Ordinal) && PrincipalReference())
            {
                return;
            }

            if (_entity.EffectOnlyRows != null && (!Nested || (_entity.EffectOnlyRows.Length > 0 && (Effect == null || Array.IndexOf(_entity.EffectOnlyRows, Effect.Value) < 0))))
            {
                Refuse(
                    ReasonCodes.EngineEffectOnly,
                    EntityName + " rows are written only by the engine" + (_entity.EffectOnlyRows.Length > 0 ? ", as an effect of an accepted decision" : string.Empty) + ".",
                    _request.RowId);
                return;
            }

            string? newMeaning = StatusAndMeaning(out string? statusKey);
            newMeaning = AiStatus(newMeaning, statusKey);

            switch (_entity.Key)
            {
                case "decision":
                    Decision(newMeaning);
                    break;
                case "approval-record":
                    ApprovalRecord(newMeaning, statusKey);
                    break;
                case "business-application":
                    BusinessApplication(newMeaning);
                    break;
                case "document-link":
                    AiDocumentCurrent(newMeaning, statusKey);
                    break;
                case "gap-check-run":
                    EstatesCovered();
                    break;
            }

            Links();
            EffectOnlyColumns();
            ProtectionRules();
            EngineComputedColumns();
        }

        // G-10: a status written must be an active C05 row of this entity. The engine writes its meaning copy.
        private string? StatusAndMeaning(out string? statusKey)
        {
            statusKey = null;
            if (_entity.StatusColumn == null || !_after.TryGetMember(_entity.StatusColumn, out JsonTreeNode? value))
            {
                return null;
            }

            _handled.Add(_entity.StatusColumn);
            if (value.Kind != JsonTreeKind.String)
            {
                Refuse(ReasonCodes.StatusUnknown, EntityName + " " + _entity.StatusColumn + " must name a configured status.", null);
                return null;
            }

            statusKey = value.GetString();
            SnapshotRow? row = SnapshotRead.Find(_snapshot, "status", statusKey);
            if (row == null || !row.IsActive || !string.Equals(SnapshotRead.Text(row, "appliesToEntity"), _entity.Key, StringComparison.Ordinal) || row.EngineMeaning == null)
            {
                string why = row == null ? "is not a status in the configuration in force"
                    : !row.IsActive ? "is retired, and new writes cannot pick a retired status"
                    : "is a status of " + (SnapshotRead.Text(row, "appliesToEntity") ?? "another entity");
                Refuse(ReasonCodes.StatusUnknown, "The status " + Quote(statusKey) + " " + why + ", so it cannot be written on " + EntityName + ".", statusKey);
                return null;
            }

            _trace.Add(new RuleTrace("Status", statusKey, RuleTraceRole.Matched));
            Set(_entity.MeaningColumn!, JsonTreeNode.FromString(row.EngineMeaning));
            return row.EngineMeaning;
        }

        // G-08: an AI actor never sets accepted, registered, approved or completed. Rows it creates start as draft or
        // proposed, whatever status it named, and Is AI Created is set in the stamps.
        private string? AiStatus(string? newMeaning, string? statusKey)
        {
            if (Nested || !ContractFacts.IsAiActor(_actor.ActorKind))
            {
                return newMeaning;
            }

            if (Op == WriteOperation.Create && _entity.DraftMeaning != null)
            {
                string forced = DefaultStatus(_entity.Key, _entity.DraftMeaning);
                if (!string.Equals(statusKey, forced, StringComparison.Ordinal))
                {
                    Info(
                        ReasonCodes.AiForcedDraft,
                        "An AI actor created this " + EntityName + ", so the engine set its status to " + Quote(forced) + " (" + _entity.DraftMeaning + ")"
                        + (statusKey == null ? string.Empty : " instead of " + Quote(statusKey)) + ". A person confirms it later.",
                        forced);
                    Set(_entity.StatusColumn!, JsonTreeNode.FromString(forced));
                    Set(_entity.MeaningColumn!, JsonTreeNode.FromString(_entity.DraftMeaning));
                }

                return _entity.DraftMeaning;
            }

            if (newMeaning != null && Array.IndexOf(PositiveMeanings, newMeaning) >= 0)
            {
                Refuse(
                    ReasonCodes.AiStatusNotAllowed,
                    "An AI actor cannot set " + EntityName + " to " + Quote(statusKey!) + " (" + newMeaning + "). A person must confirm it.",
                    statusKey);
            }

            return newMeaning;
        }

        // ---- O19 Document Link (RA-03) ----

        // Document 26, RA-03: marking a gate-relevant document current needs a person's confirmation card. An AI actor
        // never sets current on a Document Link whose document type has Current Needs Person (C22); the approval runner
        // writes the status from the person's card. The MCP tool stores such a "current" as draft; this rule holds for
        // every other AI path too, such as Microsoft's Dataverse MCP server (document 22, section 2.4). A create is
        // already forced to draft (G-08).
        //
        // WORKING DEFAULT (open design question 9): which AI-drafted acts need a person's confirmation card.
        private void AiDocumentCurrent(string? newMeaning, string? statusKey)
        {
            if (Nested || !ContractFacts.IsAiActor(_actor.ActorKind) || Op != WriteOperation.Update || !string.Equals(newMeaning, "current", StringComparison.Ordinal))
            {
                return;
            }

            string typeKey = Text(Column("documentType"), null)
                ?? throw OperationStop.Input(ReasonCodes.InputRequired, "documentType (in input.after or facts.before) is required when an AI actor marks a Document Link current.");
            SnapshotRow type = SnapshotRead.Find(_snapshot, "document-type", typeKey)
                ?? throw OperationStop.Input(ReasonCodes.InputUnknownKey, "documentType is \"" + typeKey + "\", which is not a Document Type in the configuration in force.", typeKey);
            if (SnapshotRead.Flag(type, "currentNeedsPerson"))
            {
                _trace.Add(new RuleTrace("Document Type", type.KeyPath, RuleTraceRole.Matched));
                Refuse(
                    ReasonCodes.AiStatusNotAllowed,
                    "An AI actor cannot mark this Document Link " + Quote(statusKey!) + " (current): document type " + Quote(type.KeyPath)
                    + " needs a person to confirm it (Current Needs Person), on a confirmation card.",
                    statusKey);
            }
        }

        // ---- O22 Gap Check Run (document 22, sections 2.2 and 9.1) ----

        // Estates Covered holds keys of the estates the run read, never a copy of the inventory (document 22, section 2.2,
        // item 5, and section 9.1). A value that is not a list of distinct keys is invalid input.
        // Whether each key must be an active value of the estate list is still open: run_owner_check writes
        // "entra-owners", which no preset's estate list holds.
        private void EstatesCovered()
        {
            if (!_after.TryGetMember("estatesCovered", out JsonTreeNode? estates))
            {
                return;
            }

            bool keys = estates.Kind == JsonTreeKind.Array
                && estates.Items.All(i => i.Kind == JsonTreeKind.String && ConfigurationKey.IsValid(i.GetString()))
                && estates.Items.Select(i => i.GetString()).Distinct(StringComparer.Ordinal).Count() == estates.Items.Count;
            if (!keys)
            {
                throw OperationStop.Input(ReasonCodes.InputMalformed, "input.after.estatesCovered must be a list of distinct estate keys, such as [\"power-platform-inventory\"]. It never holds inventory items or their properties.", "estatesCovered");
            }
        }

        // ---- O09 Decision (G-05, G-14, G-17) ----

        private void Decision(string? newMeaning)
        {
            string? beforeMeaning = Text(_before, "statusMeaning");
            if (Op == WriteOperation.Update)
            {
                if (beforeMeaning == null)
                {
                    throw OperationStop.Input(ReasonCodes.InputRequired, "facts.before.statusMeaning is required to change a Decision.");
                }

                // A meaning outside the contract list (such as "Accepted" from an adapter's mapping) cannot read as not
                // final: the facts are broken.
                if (!EngineContract.DecisionStatusMeanings.TryParse(beforeMeaning, out DecisionStatusMeaning before))
                {
                    throw OperationStop.Input(ReasonCodes.InputContractValueUnknown, "facts.before.statusMeaning is \"" + beforeMeaning + "\", which is not one of " + string.Join(", ", EngineContract.DecisionStatusMeanings.WireValues) + ".");
                }

                // G-05: a final decision never changes. On Dataverse the engine may set only Superseded By (document 22, section 8.1).
                if (ContractFacts.IsFinal(before))
                {
                    bool supersedeOnly = Nested && Effect == EffectKind.SupersedeDecision && _after.TryGetMember("supersededBy", out _) && OnlyColumns(new[] { "supersededBy" });
                    if (!supersedeOnly)
                    {
                        Refuse(ReasonCodes.DecisionFinal, "The Decision is " + beforeMeaning + ", so it never changes. Record a new decision that supersedes it.", _request.RowId);
                    }

                    _handled.Add("supersededBy");
                    return;
                }
            }

            switch (newMeaning)
            {
                case "accepted":
                    Accept();
                    break;
                case "rejected":
                    if (!(Nested && Effect == EffectKind.RecomputeApprovals))
                    {
                        Refuse(ReasonCodes.EngineEffectOnly, "A Decision becomes rejected only when the engine finds its approvals rejected.", _request.RowId);
                    }

                    break;
                case "superseded":
                    Refuse(ReasonCodes.EngineEffectOnly, "Superseded is derived from the later decision and never written as a status.", _request.RowId);
                    break;
                case "withdrawn":
                    if (!Nested && Op == WriteOperation.Update)
                    {
                        RequireProposerOrDecider();
                    }

                    break;
            }
        }

        // Document 22, section 8.1: a proposed decision is withdrawn by its proposer or its decider, before acceptance
        // (document 26, section 5.3: "Proposer while Proposed. Status: engine only"). The proposer is the principal that
        // created the row (Created By Principal never changes); the person behind the call is the person, or the person an
        // AI client acts for.
        private void RequireProposerOrDecider()
        {
            string proposer = Text(_before, "createdByPrincipal") ?? throw OperationStop.Input(ReasonCodes.InputRequired, "facts.before.createdByPrincipal (the proposer) is required to withdraw a Decision.");
            string? decider = Text(_before, "decider");
            string? person = _actor.ActorKind == ActorKindMeaning.Person ? _actor.PrincipalId
                : _actor.ActorKind == ActorKindMeaning.AiClientForPerson ? _actor.OnBehalfOfPrincipalId
                : null;
            bool allowed = person != null
                && (string.Equals(person, proposer, StringComparison.OrdinalIgnoreCase) || (decider != null && string.Equals(person, decider, StringComparison.OrdinalIgnoreCase)));
            if (!allowed)
            {
                Refuse(ReasonCodes.DecisionWithdrawNotAllowed, "Only the person who proposed this Decision, or its decider, can withdraw it, directly or through an AI client acting for them.", _request.RowId);
            }
        }

        // G-17: a decision is accepted only by an accept-decision effect, after OP-03 says satisfied, or when OP-02
        // returns engine-accept for a locked-list entry, or by the bootstrap of version 1. Then G-14 for a gate decision.
        private void Accept()
        {
            bool bootstrapWrite = Op == WriteOperation.Create && Effect == EffectKind.ActivateConfiguration;
            if (!Nested || !(Effect == EffectKind.AcceptDecision || bootstrapWrite))
            {
                Refuse(
                    ReasonCodes.DecisionAcceptByEngineOnly,
                    "No caller sets a Decision to accepted. The engine accepts it when its approvals are satisfied, or for an entry of the locked list.",
                    _request.RowId);
                return;
            }

            WriteAcceptance acceptance = _request.Acceptance ?? throw OperationStop.Input(ReasonCodes.InputRequired, "facts.acceptance is required to accept a Decision.");
            string? decisionType = Text(Column("decisionType"), null);
            string? refusal = null;
            switch (acceptance.Basis)
            {
                case WriteAcceptance.ApprovalsSatisfied:
                    if (Effect != EffectKind.AcceptDecision || acceptance.ApprovalStatusOutcome != "satisfied")
                    {
                        refusal = "OP-03 says the approvals are " + (acceptance.ApprovalStatusOutcome ?? "unknown") + ", not satisfied";
                    }

                    break;
                case WriteAcceptance.EngineAccept:
                    refusal = EngineAcceptRefusal(acceptance, decisionType);
                    break;
                default:
                    if (!bootstrapWrite || !acceptance.NoVersionExists || !string.Equals(decisionType, EngineKeys.ConfigurationChangeDecisionType, StringComparison.Ordinal))
                    {
                        refusal = "the bootstrap accepts only the configuration change decision that creates version 1, when no version exists";
                    }

                    break;
            }

            if (refusal != null)
            {
                Refuse(ReasonCodes.DecisionAcceptByEngineOnly, "The Decision cannot be accepted: " + refusal + ".", _request.RowId);
                return;
            }

            GateReadinessCheck(decisionType);
            if (HasRefusal)
            {
                return;
            }

            Set("decidedOn", JsonTreeNode.FromUtcDateTime(_context.AsOfUtc));
            _handled.Add("decidedOn");
            _effects.Add(Audit("decision-accepted", "Decision", _request.RowId, JsonTreeNode.CreateObject(new KeyValuePair<string, JsonTreeNode>("basis", JsonTreeNode.FromString(acceptance.Basis)))));

            // A configuration change or gate policy change decision accepted gives the activation (section 6.4). The
            // bootstrap write is itself part of the activation of version 1.
            if (string.Equals(Text(Column("subjectKind"), null), "configuration-draft", StringComparison.Ordinal) && acceptance.Basis != WriteAcceptance.Bootstrap)
            {
                string draftId = Text(Column("subjectConfigurationDraft"), null) ?? throw OperationStop.Input(ReasonCodes.InputRequired, "subjectConfigurationDraft is required on a Decision about a configuration draft.");
                _effects.Add(new PlannedEffect(
                    EffectKind.ActivateConfiguration,
                    "Configuration Draft",
                    WriteOperation.Update,
                    draftId,
                    null,
                    Columns(("changeDecision", _request.RowId == null ? JsonTreeNode.Null : JsonTreeNode.FromString(_request.RowId)))));
            }

            string? supersedes = Text(Column("supersedes"), null);
            if (supersedes != null && _request.RowId != null)
            {
                _effects.Add(new PlannedEffect(EffectKind.SupersedeDecision, "Decision", WriteOperation.Update, supersedes, null, Columns(("supersededBy", JsonTreeNode.FromString(_request.RowId)))));
            }
        }

        private string? EngineAcceptRefusal(WriteAcceptance acceptance, string? decisionType)
        {
            if (Effect != EffectKind.AcceptDecision || acceptance.RequiredApprovalsOutcome != "engine-accept" || !acceptance.Condition.HasValue)
            {
                return "OP-02 did not return engine-accept with an engine acceptance condition";
            }

            if (decisionType == null)
            {
                throw OperationStop.Input(ReasonCodes.InputRequired, "decisionType is required to accept a Decision without a card.");
            }

            IReadOnlyList<EngineAcceptanceEntry> locked = RequiredApprovalsOperation.LockedList(_snapshot);
            bool listed = locked.Any(e => string.Equals(e.DecisionTypeKey, decisionType, StringComparison.Ordinal) && e.Condition == acceptance.Condition.Value);
            if (!listed)
            {
                return "decision type " + Quote(decisionType) + " with " + EngineContract.EngineAcceptanceConditions.ToWire(acceptance.Condition.Value) + " is not on the locked list "
                    + SnapshotRead.Quote(EngineKeys.EngineAcceptableDecisionTypes);
            }

            _trace.Add(new RuleTrace("Setting", EngineKeys.EngineAcceptableDecisionTypes, RuleTraceRole.Matched));
            return null;
        }

        // G-14: a gate decision cannot be accepted while OP-07 says the gate is not ready.
        private void GateReadinessCheck(string? decisionType)
        {
            if (decisionType == null)
            {
                return;
            }

            string[] gates = SnapshotRead.ActiveRows(_snapshot, "gate")
                .Where(g => string.Equals(SnapshotRead.Text(g, "gateDecisionTypeKey"), decisionType, StringComparison.Ordinal))
                .Select(g => g.KeyPath)
                .ToArray();
            if (gates.Length == 0)
            {
                return;
            }

            JsonTreeNode readiness = _request.GateReadiness
                ?? throw OperationStop.Input(ReasonCodes.InputRequired, "facts.gateReadiness (the OP-07 payload) is required to accept a gate decision of type " + Quote(decisionType) + ".");
            string? gateKey = Text(readiness, "gateKey");
            if (gateKey == null || Array.IndexOf(gates, gateKey) < 0 || !readiness.TryGetMember("ready", out JsonTreeNode? ready) || (ready.Kind != JsonTreeKind.True && ready.Kind != JsonTreeKind.False))
            {
                throw OperationStop.Input(ReasonCodes.InputMalformed, "facts.gateReadiness must be the OP-07 payload of gate " + string.Join(" or ", gates.Select(Quote)) + ", with gateKey and ready.");
            }

            _trace.Add(new RuleTrace("Gate", gateKey, RuleTraceRole.Matched));
            if (ready.Kind == JsonTreeKind.False)
            {
                Refuse(ReasonCodes.GateNotReady, "Gate " + Quote(gateKey) + " is not ready (OP-07), so its gate decision cannot be accepted.", gateKey);
            }
        }

        // ---- O10 Approval Record (G-06, class F and the approver kind rule) ----

        // Who writes which O10 column (document 22, section 8.2 and the rule of O10; document 25, G-06; document 26,
        // section 5.3, RA-05 and section 6.2):
        // - the engine creates the record with the request columns and the pending outcome, writes expired or cancelled on
        //   a pending record, and computes Counts Toward Requirement;
        // - the approval runner writes its answer once, on a pending record: the outcome, the responder columns and
        //   Approver Holds Required Role (a proposed column, the runner's record of the responder's role);
        // - no other caller writes any O10 column, and nothing changes once Outcome Meaning leaves pending (class F), but
        //   Counts Toward Requirement, which the engine computes when the outcome is written, and the same outcome again,
        //   a no-op.
        // A caller's stamps and the columns G-09 computes (Outcome Meaning, Approver Actor Meaning, Counts Toward
        // Requirement) are replaced or dropped, as on every entity.
        //
        // WORKING DEFAULT (open design question 12): the approval runner is a workload principal holding the approval
        // writer role through a workload grant.
        private void ApprovalRecord(string? newMeaning, string? statusKey)
        {
            if (Op == WriteOperation.Create && !Nested)
            {
                Refuse(ReasonCodes.EngineEffectOnly, "Approval Records are created by the engine from the approval rules, never by a caller.", null);
                return;
            }

            string[] answer = AnswerColumns().Where(c => _after.TryGetMember(c, out _)).ToArray();
            foreach (string column in answer)
            {
                _handled.Add(column);
            }

            if (Nested)
            {
                EngineApprovalWrite(answer, newMeaning, statusKey);
                return;
            }

            CallerApprovalWrite(answer, newMeaning, statusKey);
        }

        // The approval runner's columns: the outcome, the responder columns and Approver Holds Required Role.
        private static IEnumerable<string> AnswerColumns()
        {
            return WriteGuardEntities.ApprovalOutcomeColumns.Concat(new[] { WriteGuardEntities.ApproverHoldsRequiredRole });
        }

        // A caller's update (only an update reaches this point: a caller never creates an Approval Record).
        private void CallerApprovalWrite(string[] answer, string? newMeaning, string? statusKey)
        {
            // Every other O10 column is the engine's: the request columns, and the common Name and Description. Stamps and
            // the columns G-09 computes are left to the generic rules, which replace or drop them.
            var computed = new HashSet<string>(WriteGuardEntities.StampColumns.Concat(_entity.EngineComputed).Concat(new[] { WriteGuardEntities.ClientAppVerified, _entity.MeaningColumn! }), StringComparer.Ordinal);
            string[] engineColumns = _after.Members.Select(m => m.Key).Where(k => !_handled.Contains(k) && !computed.Contains(k)).ToArray();
            foreach (string column in engineColumns)
            {
                _handled.Add(column);
            }

            bool writer = IsApprovalWriter(out string why);
            if (engineColumns.Length > 0)
            {
                Refuse(
                    ReasonCodes.EngineEffectOnly,
                    "Approval Record " + string.Join(", ", engineColumns) + (engineColumns.Length == 1 ? " is" : " are") + " written only by the engine, when it creates the record from the approval rules"
                    + (writer ? ". The approval runner writes only its answer." : "."),
                    _request.RowId);
            }

            if (answer.Length == 0)
            {
                if (engineColumns.Length == 0)
                {
                    // Only stamps or columns the engine computes: still a write to the record (its stamps would change), and
                    // no caller makes one without an answer.
                    foreach (KeyValuePair<string, JsonTreeNode> member in _after.Members)
                    {
                        _handled.Add(member.Key);
                    }

                    if (writer)
                    {
                        Refuse(ReasonCodes.ApprovalOutcomeWriter, "The approval runner writes an Approval Record only with its answer, which names the outcome.", _request.RowId);
                    }
                    else
                    {
                        Refuse(ReasonCodes.EngineEffectOnly, "No caller writes an Approval Record: the engine writes the request and computes what counts, and the approval runner writes the answer.", _request.RowId);
                    }
                }

                return;
            }

            string beforeMeaning = Text(_before, "outcomeMeaning")
                ?? throw OperationStop.Input(ReasonCodes.InputRequired, "facts.before.outcomeMeaning is required to write an approval outcome.");

            if (!writer)
            {
                Refuse(ReasonCodes.ApprovalOutcomeWriter, "Only the approval runner, holding the approval writer workload role, records an approval outcome: " + why + ".", _request.RowId);
                return;
            }

            if (newMeaning != null && newMeaning != "approved" && newMeaning != "rejected" && newMeaning != "recommendation-only")
            {
                Refuse(ReasonCodes.ApprovalOutcomeWriter, "The approval runner writes approved, rejected or recommendation only; " + newMeaning + " is the engine's.", statusKey);
                return;
            }

            // G-06 "only once" and class F: the same outcome again is a no-op; anything else after a final outcome, with an
            // outcome or without one, is refused.
            if (beforeMeaning != "pending")
            {
                string? beforeKey = Text(_before, "outcome");
                bool same = statusKey != null && (beforeKey != null ? string.Equals(beforeKey, statusKey, StringComparison.Ordinal) : string.Equals(beforeMeaning, newMeaning, StringComparison.Ordinal));
                if (same && !HasRefusal)
                {
                    _noOp = true;
                    return;
                }

                if (!same)
                {
                    Refuse(ReasonCodes.ApprovalOutcomeWriter, "The Approval Record already has the final outcome " + beforeMeaning + ". The answer is written only once, together with its outcome, and never changes.", _request.RowId);
                }

                return;
            }

            if (statusKey == null || newMeaning == null)
            {
                throw OperationStop.Input(ReasonCodes.InputRequired, "input.after.outcome is required with the responder columns.");
            }

            // The approver kind rule (document 22, section 2.4, rule 4; document 26, AN-03 and AN-05): an answer counts
            // only from a person signed in as a user. Any other answer is stored as recommendation only.
            string actorKindKey = Text(_after, "approverActorKind") ?? throw OperationStop.Input(ReasonCodes.InputRequired, "input.after.approverActorKind is required with an answer.");
            if (Text(_after, "approver") == null)
            {
                throw OperationStop.Input(ReasonCodes.InputRequired, "input.after.approver is required with an answer.");
            }

            string principalText = Text(_after, "approverPrincipalKind") ?? throw OperationStop.Input(ReasonCodes.InputRequired, "input.after.approverPrincipalKind is required with an answer.");
            if (!EngineContract.PrincipalKinds.TryParse(principalText, out PrincipalKind principalKind))
            {
                throw OperationStop.Input(ReasonCodes.InputContractValueUnknown, "input.after.approverPrincipalKind is \"" + principalText + "\", which is not one of " + string.Join(", ", EngineContract.PrincipalKinds.WireValues) + ".");
            }

            SnapshotRow actorRow = SnapshotRead.RequireActive(_snapshot, "actor-kind", actorKindKey, "input.after.approverActorKind");
            if (!EngineContract.ActorKindMeanings.TryParse(actorRow.EngineMeaning, out ActorKindMeaning actorMeaning))
            {
                throw OperationStop.Broken(ReasonCodes.MeaningMissing, "Actor Kind " + Quote(actorKindKey) + " has no engine meaning, so the configuration is broken.", "Actor Kind", actorKindKey);
            }

            Set("approverActorMeaning", JsonTreeNode.FromString(EngineContract.ActorKindMeanings.ToWire(actorMeaning)));
            if ((newMeaning == "approved" || newMeaning == "rejected") && (!ContractFacts.IsPerson(actorMeaning) || !ContractFacts.CanApprove(principalKind)))
            {
                if (ContractFacts.IsAiActor(actorMeaning))
                {
                    Warn(ReasonCodes.ApprovalAiActorNotCounted, "The answer came from " + EngineContract.ActorKindMeanings.ToWire(actorMeaning) + ", and AI never approves, so it is stored as recommendation only.", actorKindKey);
                }
                else if (!ContractFacts.IsPerson(actorMeaning))
                {
                    Warn(ReasonCodes.ApprovalWorkloadNotCounted, "The answer came from " + EngineContract.ActorKindMeanings.ToWire(actorMeaning) + ", so it is stored as recommendation only.", actorKindKey);
                }

                if (!ContractFacts.CanApprove(principalKind))
                {
                    Warn(ReasonCodes.ApprovalNonUserPrincipal, "The responder is a " + principalText + ", and only a user principal can approve, so the answer is stored as recommendation only.", principalText);
                }

                string forced = DefaultStatus("approval-record", "recommendation-only");
                Set("outcome", JsonTreeNode.FromString(forced));
                Set("outcomeMeaning", JsonTreeNode.FromString("recommendation-only"));
                Set("countsTowardRequirement", JsonTreeNode.False);
            }

            string decisionId = Text(Column("decision"), null) ?? throw OperationStop.Input(ReasonCodes.InputRequired, "facts.before.decision (the Decision of the Approval Record) is required to write an outcome.");
            _effects.Add(new PlannedEffect(EffectKind.RecomputeApprovals, "Decision", WriteOperation.Update, decisionId, null, Columns()));
            _effects.Add(Audit("approval-outcome-written", "Approval Record", _request.RowId, Columns(("outcomeMeaning", _set.TryGetValue("outcomeMeaning", out JsonTreeNode? m) ? m : JsonTreeNode.FromString(newMeaning)))));
        }

        // The engine's own writes on an Approval Record (model rule of O10 in document 22; G-06 in document 25). The engine
        // writes the request: it creates the record with the pending outcome, at the first request and at every restart (a
        // restarted approval is a new row), in any effect or in none. After that it writes only expired or cancelled, and
        // only once, on a pending record: the same outcome again is a no-op, as it is for the approval runner. It never
        // writes a responder column, with or without an outcome: Approver, Approver Actor Kind, Approver Actor Meaning,
        // Approver Principal Kind, Typed Reason, Responded On, Channel, Flow Run Url and Approver Holds Required Role are the
        // approval runner's alone. It never names Outcome Meaning either: the guard writes it from the outcome (G-10). The
        // other columns (Decision, the rule and role columns, Asked Principal, Requested On, Expires On) are the engine's to
        // write while the record is pending. Once the record is final (class F) the engine writes only Counts Toward
        // Requirement, which it computes when the outcome is written.
        //
        // WORKING DEFAULT (open design question 1): the responder columns and the meanings pending, expired and cancelled
        // are fixed engine knowledge.
        private void EngineApprovalWrite(string[] answer, string? newMeaning, string? statusKey)
        {
            bool outcomeGiven = Array.IndexOf(answer, "outcome") >= 0;
            if (Op == WriteOperation.Create && !outcomeGiven)
            {
                throw OperationStop.Input(ReasonCodes.InputRequired, "input.after.outcome is required to create an Approval Record: the engine creates it with a pending outcome.");
            }

            var responder = answer.Where(c => !string.Equals(c, "outcome", StringComparison.Ordinal)).ToList();
            if (_after.TryGetMember("approverActorMeaning", out _))
            {
                _handled.Add("approverActorMeaning");
                responder.Add("approverActorMeaning");
            }

            if (responder.Count > 0)
            {
                Refuse(
                    ReasonCodes.ApprovalOutcomeWriter,
                    "The engine never writes who answered an Approval Record, or when, where or why (" + string.Join(", ", responder) + " given). Only the approval runner writes the responder columns, with the answer.",
                    _request.RowId);
                return;
            }

            if (_after.TryGetMember(_entity.MeaningColumn!, out _))
            {
                _handled.Add(_entity.MeaningColumn!);
                Refuse(
                    ReasonCodes.ApprovalOutcomeWriter,
                    "The engine writes an approval outcome only through Outcome; the guard writes Outcome Meaning from it (G-10), so a write never names Outcome Meaning.",
                    _request.RowId);
                return;
            }

            if (Op == WriteOperation.Create)
            {
                if (newMeaning != null && !string.Equals(newMeaning, "pending", StringComparison.Ordinal))
                {
                    Refuse(
                        ReasonCodes.ApprovalOutcomeWriter,
                        "The engine creates an Approval Record only with a pending outcome, never " + newMeaning + ". The approval runner writes the answer later, and the engine writes expired or cancelled on the pending record.",
                        statusKey);
                }

                return;
            }

            string beforeMeaning = Text(_before, "outcomeMeaning")
                ?? throw OperationStop.Input(ReasonCodes.InputRequired, "facts.before.outcomeMeaning is required to change an Approval Record.");
            bool final = !string.Equals(beforeMeaning, "pending", StringComparison.Ordinal);
            bool sameOutcome = false;
            if (outcomeGiven)
            {
                // An outcome that names no active status of this entity was already refused (G-10).
                if (newMeaning == null)
                {
                    return;
                }

                if (!string.Equals(newMeaning, "expired", StringComparison.Ordinal) && !string.Equals(newMeaning, "cancelled", StringComparison.Ordinal))
                {
                    Refuse(ReasonCodes.ApprovalOutcomeWriter, "The engine writes only expired or cancelled on an Approval Record. Only the approval runner writes an answer.", _request.RowId);
                    return;
                }

                if (final)
                {
                    // G-06 "only once" holds for the engine too.
                    string? beforeKey = Text(_before, "outcome");
                    sameOutcome = beforeKey != null ? string.Equals(beforeKey, statusKey, StringComparison.Ordinal) : string.Equals(beforeMeaning, newMeaning, StringComparison.Ordinal);
                    if (!sameOutcome)
                    {
                        Refuse(ReasonCodes.ApprovalOutcomeWriter, "The Approval Record already has the final outcome " + beforeMeaning + ", and an outcome is written only once, by the engine too.", _request.RowId);
                        return;
                    }
                }
            }

            if (!final)
            {
                return;
            }

            // An update with no columns would still restamp the final record.
            if (_after.Members.Count == 0)
            {
                Refuse(
                    ReasonCodes.RecordFinal,
                    "The Approval Record is final (" + beforeMeaning + "), and an update with no columns would still change its stamps, so it is refused. The engine writes only Counts Toward Requirement on a final record.",
                    _request.RowId);
                return;
            }

            // Class F: frozen once Outcome Meaning leaves pending. Only Counts Toward Requirement, computed when the outcome
            // is written, and the same outcome again remain.
            string[] frozen = _after.Members.Select(m => m.Key)
                .Where(k => !string.Equals(k, "countsTowardRequirement", StringComparison.Ordinal) && !(sameOutcome && string.Equals(k, "outcome", StringComparison.Ordinal)))
                .ToArray();
            if (frozen.Length > 0)
            {
                foreach (string column in frozen)
                {
                    _handled.Add(column);
                }

                Refuse(
                    ReasonCodes.RecordFinal,
                    "The Approval Record is final (" + beforeMeaning + "), so it never changes (" + string.Join(", ", frozen) + " given). The engine writes only Counts Toward Requirement on a final record.",
                    _request.RowId);
                return;
            }

            if (sameOutcome && !_after.TryGetMember("countsTowardRequirement", out _))
            {
                _noOp = true;
            }
        }

        // The approval runner: actor kind approval runner, a workload principal, holding the active approval writer
        // workload role, writing in its own name (not engine-nested).
        private bool IsApprovalWriter(out string why)
        {
            SnapshotRow? role = SnapshotRead.FindActive(_snapshot, "role", EngineKeys.ApprovalWriterRole);
            if (role == null || !SnapshotRead.Flag(role, "isWorkloadRole"))
            {
                why = "the configuration in force has no active workload role " + Quote(EngineKeys.ApprovalWriterRole);
                return false;
            }

            if (_actor.ActorKind != ActorKindMeaning.ApprovalRunner)
            {
                why = "the caller is " + EngineContract.ActorKindMeanings.ToWire(_actor.ActorKind);
                return false;
            }

            if (_actor.PrincipalKind != PrincipalKind.ServicePrincipal && _actor.PrincipalKind != PrincipalKind.ManagedIdentity)
            {
                why = "the caller is a " + EngineContract.PrincipalKinds.ToWire(_actor.PrincipalKind) + ", not a workload principal";
                return false;
            }

            if (!_actor.HoldsRole(EngineKeys.ApprovalWriterRole))
            {
                why = "the caller does not hold " + Quote(EngineKeys.ApprovalWriterRole);
                return false;
            }

            _trace.Add(new RuleTrace("Role", EngineKeys.ApprovalWriterRole, RuleTraceRole.Matched));
            why = string.Empty;
            return true;
        }

        // ---- O02 Business Application (G-13) ----

        private void BusinessApplication(string? newMeaning)
        {
            string[] touched = WriteGuardEntities.RegisterColumns.Where(c => _after.TryGetMember(c, out JsonTreeNode? v) && (Op == WriteOperation.Update || v.Kind != JsonTreeKind.Null)).ToArray();
            foreach (string column in touched)
            {
                _handled.Add(column);
            }

            bool asEffect = Nested && Effect == EffectKind.UpdateSubject;
            RegistrationStatus(newMeaning, asEffect);
            if (touched.Length > 0 && !asEffect)
            {
                Refuse(
                    ReasonCodes.RegisterDecisionRequired,
                    "Tier, Lifecycle Stage and their decisions change only as an effect of an accepted decision (" + string.Join(", ", touched) + " given). Record a tier or stage decision instead.",
                    _request.RowId);
                return;
            }

            if (asEffect)
            {
                if (_after.TryGetMember("tier", out JsonTreeNode? tier) && tier.Kind != JsonTreeKind.Null && Text(Column("tierDecision"), null) == null)
                {
                    throw OperationStop.Input(ReasonCodes.InputRequired, "tierDecision is required whenever Tier is set.");
                }

                if (_after.TryGetMember("lifecycleStage", out _) && !_after.TryGetMember("stageSince", out _))
                {
                    Set("stageSince", JsonTreeNode.FromUtcDateTime(_context.AsOfUtc));
                }

                return;
            }

            // A new application starts at the first lifecycle stage, set by the engine (document 22, O02: Stage Decision
            // is empty only at the first stage).
            if (Op == WriteOperation.Create)
            {
                SnapshotRow first = SnapshotRead.ActiveRows(_snapshot, "lifecycle-stage")
                    .OrderBy(r => SnapshotRead.Int(r, "lifecycle-stage", "stageRank") ?? int.MaxValue)
                    .ThenBy(r => r.KeyPath, StringComparer.Ordinal)
                    .FirstOrDefault()
                    ?? throw OperationStop.Broken(ReasonCodes.StageMeaningCoverage, "The configuration in force has no active Lifecycle Stage, so a new application cannot start.", "Lifecycle Stage", null);
                Set("lifecycleStage", JsonTreeNode.FromString(first.KeyPath));
                Set("stageSince", JsonTreeNode.FromUtcDateTime(_context.AsOfUtc));
                _trace.Add(new RuleTrace("Lifecycle Stage", first.KeyPath, RuleTraceRole.Matched));
            }
        }

        // Document 26, sections 5.3 and 6.2, and RA-04: the registration status is engine-written, set only from accepted
        // decisions. A write that is not an update-subject effect sets only draft: a new application starts as draft, a
        // draft stays draft, and a draft may be archived (withdraw_record withdraws a draft that way). Registered, and any
        // move away from registered or archived, come only as an effect of an accepted decision. An AI update to
        // registered is already refused by G-08, once.
        private void RegistrationStatus(string? newMeaning, bool asEffect)
        {
            if (newMeaning == null || asEffect || _reasons.Any(r => r.Code == ReasonCodes.AiStatusNotAllowed))
            {
                return;
            }

            string? beforeMeaning = null;
            if (Op == WriteOperation.Update)
            {
                beforeMeaning = Text(_before, "statusMeaning") ?? throw OperationStop.Input(ReasonCodes.InputRequired, "facts.before.statusMeaning is required to change a Business Application's status.");
                if (!EngineContract.RegistrationStatusMeanings.TryParse(beforeMeaning, out _))
                {
                    throw OperationStop.Input(ReasonCodes.InputContractValueUnknown, "facts.before.statusMeaning is \"" + beforeMeaning + "\", which is not one of " + string.Join(", ", EngineContract.RegistrationStatusMeanings.WireValues) + ".");
                }
            }

            bool fromDraft = beforeMeaning == null || string.Equals(beforeMeaning, "draft", StringComparison.Ordinal);
            bool allowed = fromDraft && (string.Equals(newMeaning, "draft", StringComparison.Ordinal) || (beforeMeaning != null && string.Equals(newMeaning, "archived", StringComparison.Ordinal)));
            if (!allowed)
            {
                Refuse(
                    ReasonCodes.EngineEffectOnly,
                    "A Business Application's registration status moves from " + (beforeMeaning ?? "new") + " to " + newMeaning
                    + " only as an effect of an accepted decision. Record the decision instead; a request sets only draft, or archives a draft.",
                    _request.RowId);
            }
        }

        // ---- O05 Principal Reference: the erasure effect ----

        // Returns true when the write took the erasure path (or was refused on it), so the generic rules do not run.
        private bool PrincipalReference()
        {
            bool beforeErased = Flag(_before, "isErased");
            bool erasing = _after.TryGetMember("isErased", out JsonTreeNode? flag) && flag.Kind == JsonTreeKind.True;
            bool restoring = flag != null && flag.Kind == JsonTreeKind.False;

            if (beforeErased && restoring)
            {
                Refuse(ReasonCodes.PrincipalErasureFinal, "The Principal Reference is erased and stays erased.", _request.RowId);
                return true;
            }

            // An erased principal keeps "former user", whatever a refresh later reads from the directory.
            if (beforeErased && _after.TryGetMember("displayLabel", out JsonTreeNode? label) && !(label.Kind == JsonTreeKind.String && label.GetString() == EngineKeys.ErasedDisplayLabel))
            {
                Info(ReasonCodes.EngineColumnOverwritten, "The Principal Reference is erased, so its display label stays " + Quote(EngineKeys.ErasedDisplayLabel) + ".", "displayLabel");
                Set("displayLabel", JsonTreeNode.FromString(EngineKeys.ErasedDisplayLabel));
                _handled.Add("displayLabel");
            }

            if (!erasing || Nested || Op != WriteOperation.Update)
            {
                return false;
            }

            if (beforeErased)
            {
                _noOp = true;
                return true;
            }

            // Document 26, section 9.5: erasure is performed by a platform admin, a person signed in as a user.
            SnapshotRow? admin = SnapshotRead.FindActive(_snapshot, "role", EngineKeys.PlatformAdminRole);
            if (_actor.ActorKind != ActorKindMeaning.Person || !ContractFacts.CanApprove(_actor.PrincipalKind) || admin == null || !_actor.HoldsRole(EngineKeys.PlatformAdminRole))
            {
                Refuse(
                    ReasonCodes.PrincipalErasureNotAllowed,
                    "Only a person signed in as a user who holds " + Quote(EngineKeys.PlatformAdminRole) + " can erase a principal" + (admin == null ? ", and the configuration in force has no such active role" : string.Empty) + ".",
                    _request.RowId);
                return true;
            }

            _trace.Add(new RuleTrace("Role", EngineKeys.PlatformAdminRole, RuleTraceRole.Matched));
            string[] extra = _after.Members.Select(m => m.Key).Where(k => k != "isErased" && k != "displayLabel" && k != "entraObjectId").ToArray();
            if (extra.Length > 0)
            {
                throw OperationStop.Input(ReasonCodes.InputMalformed, "An erasure writes only isErased, displayLabel and a tombstone entraObjectId, not " + string.Join(", ", extra) + ".");
            }

            if (_after.TryGetMember("displayLabel", out JsonTreeNode? given) && !(given.Kind == JsonTreeKind.String && given.GetString() == EngineKeys.ErasedDisplayLabel))
            {
                Info(ReasonCodes.EngineColumnOverwritten, "Erasure clears the display label to " + Quote(EngineKeys.ErasedDisplayLabel) + ".", "displayLabel");
            }

            // Where the client's policy requires it, the Entra Object Id is replaced by a tombstone value the adapter
            // generates (the core makes no random values). Without one, the object ID is kept (document 26, section 9.5).
            bool tombstone = false;
            if (_after.TryGetMember("entraObjectId", out JsonTreeNode? replacement))
            {
                string? old = Text(_before, "entraObjectId") ?? throw OperationStop.Input(ReasonCodes.InputRequired, "facts.before.entraObjectId is required to replace it with a tombstone.");
                if (replacement.Kind != JsonTreeKind.String || replacement.GetString().Length == 0 || string.Equals(replacement.GetString(), old, StringComparison.OrdinalIgnoreCase))
                {
                    throw OperationStop.Input(ReasonCodes.InputMalformed, "input.after.entraObjectId must be a new tombstone value, different from the current object ID.");
                }

                Set("entraObjectId", replacement);
                tombstone = true;
            }

            Set("displayLabel", JsonTreeNode.FromString(EngineKeys.ErasedDisplayLabel));
            Set("isErased", JsonTreeNode.True);

            // Audit entries hold IDs, never labels (document 22, section 2.7, item 4).
            _effects.Add(Audit("erasure-performed", "Principal Reference", _request.RowId, Columns(("isErased", JsonTreeNode.True), ("entraObjectIdReplaced", JsonTreeNode.FromBoolean(tombstone)))));
            return true;
        }

        // ---- Deletes (document 22, section 2.5) ----

        private void Delete()
        {
            string? decisionMeaning = string.Equals(_entity.Key, "decision", StringComparison.Ordinal) ? Text(_before, "statusMeaning") : null;
            if (decisionMeaning != null && !EngineContract.DecisionStatusMeanings.TryParse(decisionMeaning, out _))
            {
                throw OperationStop.Input(ReasonCodes.InputContractValueUnknown, "facts.before.statusMeaning is \"" + decisionMeaning + "\", which is not one of " + string.Join(", ", EngineContract.DecisionStatusMeanings.WireValues) + ".");
            }

            if (decisionMeaning != null && EngineContract.DecisionStatusMeanings.TryParse(decisionMeaning, out DecisionStatusMeaning meaning) && ContractFacts.IsFinal(meaning))
            {
                Refuse(ReasonCodes.DecisionFinal, "The Decision is final, so it never changes and is never deleted.", _request.RowId);
                return;
            }

            switch (_entity.Delete)
            {
                case GuardDeletePolicy.DraftOnly:
                    {
                        string? beforeMeaning = Text(_before, _entity.MeaningColumn!) ?? throw OperationStop.Input(ReasonCodes.InputRequired, "facts.before." + _entity.MeaningColumn + " is required to delete " + EntityName + ".");
                        if (beforeMeaning != "draft")
                        {
                            Refuse(ReasonCodes.RecordDeleteNotAllowed, "Only a draft " + EntityName + " can be deleted; this one is " + beforeMeaning + ". Withdraw, cancel or archive it instead.", _request.RowId);
                            return;
                        }

                        RequireCreator();
                        return;
                    }

                case GuardDeletePolicy.CreatorUndo:
                    {
                        if (!RequireCreator())
                        {
                            return;
                        }

                        DateTime createdOn = Utc(_before, "createdOn") ?? throw OperationStop.Input(ReasonCodes.InputRequired, "facts.before.createdOn is required to delete " + EntityName + ".");
                        JsonTreeNode minutesNode = SnapshotRead.RequireSetting(_snapshot, EngineKeys.LinkUndoMinutes, n => n.TryGetInt64(out long v) && v >= 0, "a whole number of minutes");
                        minutesNode.TryGetInt64(out long minutes);
                        _trace.Add(new RuleTrace("Setting", EngineKeys.LinkUndoMinutes, RuleTraceRole.Matched));

                        // A Created On later than now was never written by the platform, so the window cannot be counted
                        // from it.
                        if (createdOn > _context.AsOfUtc)
                        {
                            Refuse(ReasonCodes.RecordDeleteNotAllowed, "This " + EntityName + " has a Created On later than now, so its undo window cannot be counted. Mark it moved or archived instead.", _request.RowId);
                        }
                        else if (_context.AsOfUtc > createdOn.AddMinutes(minutes))
                        {
                            Refuse(ReasonCodes.RecordDeleteNotAllowed, "The undo window of " + minutes + " minutes (link-undo-minutes) for this " + EntityName + " has passed. Mark it moved or archived instead.", _request.RowId);
                        }

                        return;
                    }

                default:
                    Refuse(ReasonCodes.RecordDeleteNotAllowed, EntityName + " rows are never deleted. Withdraw, cancel, archive or end the row with a date instead.", _request.RowId);
                    return;
            }
        }

        // Only the creator deletes: a person, or an AI client acting for that person.
        private bool RequireCreator()
        {
            string creator = Text(_before, "createdByPrincipal") ?? throw OperationStop.Input(ReasonCodes.InputRequired, "facts.before.createdByPrincipal is required to delete " + EntityName + ".");
            string? person = _actor.ActorKind == ActorKindMeaning.Person ? _actor.PrincipalId
                : _actor.ActorKind == ActorKindMeaning.AiClientForPerson ? _actor.OnBehalfOfPrincipalId
                : null;
            if (person == null || !string.Equals(person, creator, StringComparison.OrdinalIgnoreCase))
            {
                Refuse(ReasonCodes.RecordDeleteNotAllowed, "Only the person who created this " + EntityName + " can delete it.", _request.RowId);
                return false;
            }

            return true;
        }

        // ---- Links (G-11) ----

        private void Links()
        {
            if (_entity.LinkCategory == null)
            {
                return;
            }

            bool kindGiven = _after.TryGetMember("linkKind", out _);
            bool urlGiven = _after.TryGetMember("url", out JsonTreeNode? url) && url.Kind == JsonTreeKind.String;
            if (!kindGiven && !urlGiven && Op != WriteOperation.Create)
            {
                return;
            }

            _handled.Add("linkKind");
            string? kindKey = Text(Column("linkKind"), null);
            if (kindKey == null)
            {
                throw OperationStop.Input(ReasonCodes.InputRequired, "linkKind is required on " + EntityName + ".");
            }

            string category = EngineContract.LinkCategories.ToWire(_entity.LinkCategory.Value);
            SnapshotRow? kind = SnapshotRead.FindActive(_snapshot, "link-kind", kindKey);
            if (kind == null)
            {
                Refuse(ReasonCodes.LinkWrongCategory, "Link kind " + Quote(kindKey) + " is not an active link kind in the configuration in force.", kindKey);
                return;
            }

            string? kindCategory = SnapshotRead.Text(kind, "linkCategory");
            if (!string.Equals(kindCategory, category, StringComparison.Ordinal))
            {
                Refuse(ReasonCodes.LinkWrongCategory, "Link kind " + Quote(kindKey) + " is a " + (kindCategory ?? "category-less") + " kind, and " + EntityName + " takes only " + category + " kinds.", kindKey);
                return;
            }

            _trace.Add(new RuleTrace("Link Kind", kindKey, RuleTraceRole.Matched));
            if (!urlGiven)
            {
                return;
            }

            string text = url!.GetString();
            string? pattern = SnapshotRead.Text(kind, "urlPattern");
            if (pattern != null && !Matches(pattern, text, RegexOptions.None, "Link Kind", kindKey))
            {
                Refuse(ReasonCodes.LinkUrlPattern, "The URL does not match the pattern of link kind " + Quote(kindKey) + ".", kindKey);
            }

            // Document 26, section 6.5 (SR-09): no secrets in links. Case is ignored, so the check refuses more, never less.
            JsonTreeNode forbidden = SnapshotRead.RequireSetting(_snapshot, EngineKeys.ForbiddenUrlPatterns, n => n.Kind == JsonTreeKind.Array && n.Items.All(i => i.Kind == JsonTreeKind.String), "a list of regular expressions");
            foreach (JsonTreeNode item in forbidden.Items)
            {
                if (Matches(item.GetString(), text, RegexOptions.IgnoreCase, "Setting", EngineKeys.ForbiddenUrlPatterns))
                {
                    Refuse(ReasonCodes.LinkForbiddenUrl, "The URL matches the forbidden pattern " + Quote(item.GetString()) + " (forbidden-url-patterns), so it may carry a secret. Link without the secret part.", EngineKeys.ForbiddenUrlPatterns);
                    break;
                }
            }
        }

        private static bool Matches(string pattern, string text, RegexOptions options, string entity, string key)
        {
            try
            {
                return new Regex(pattern, options | RegexOptions.CultureInvariant, PatternTimeout).IsMatch(text);
            }
            catch (ArgumentException)
            {
                throw OperationStop.Broken(ReasonCodes.PatternInvalid, entity + " " + Quote(key) + " holds a pattern that does not compile, so the configuration is broken.", entity, key);
            }
            catch (RegexMatchTimeoutException)
            {
                throw OperationStop.Broken(ReasonCodes.PatternInvalid, entity + " " + Quote(key) + " holds a pattern that takes too long to match, so the configuration is broken.", entity, key);
            }
        }

        // ---- Engine-owned columns (G-09) ----

        // Columns only an engine effect writes. A caller's value is refused.
        private void EffectOnlyColumns()
        {
            foreach (KeyValuePair<string, EffectKind[]?> column in _entity.EffectOnly)
            {
                if (_handled.Contains(column.Key) || !_after.TryGetMember(column.Key, out _))
                {
                    continue;
                }

                _handled.Add(column.Key);
                bool allowed = Nested && (column.Value == null || (Effect.HasValue && Array.IndexOf(column.Value, Effect.Value) >= 0));
                if (!allowed)
                {
                    Refuse(ReasonCodes.EngineEffectOnly, EntityName + " " + column.Key + " is written only by the engine, never from a request.", column.Key);
                }
            }
        }

        // C43 Column Protection Rule rows can add protection, never remove it (document 26, section 6.1).
        private void ProtectionRules()
        {
            foreach (SnapshotRow rule in SnapshotRead.ActiveRows(_snapshot, "column-protection-rule"))
            {
                string? column = SnapshotRead.Text(rule, "column");
                if (!string.Equals(SnapshotRead.Text(rule, "entity"), _entity.Key, StringComparison.Ordinal) || column == null || _handled.Contains(column) || !_after.TryGetMember(column, out _))
                {
                    continue;
                }

                string? kind = SnapshotRead.Text(rule, "protectionKind");
                if (kind == "engine-written" && !Nested)
                {
                    _handled.Add(column);
                    _trace.Add(new RuleTrace("Column Protection Rule", rule.KeyPath, RuleTraceRole.Matched));
                    Refuse(ReasonCodes.EngineEffectOnly, EntityName + " " + column + " is engine-written (Column Protection Rule " + Quote(rule.KeyPath) + "), so a request cannot set it.", column);
                }
                else if (kind == "approval-writer-only" && (Nested || !IsApprovalWriter(out _)))
                {
                    _handled.Add(column);
                    _trace.Add(new RuleTrace("Column Protection Rule", rule.KeyPath, RuleTraceRole.Matched));
                    Refuse(ReasonCodes.ApprovalOutcomeWriter, EntityName + " " + column + " is written only by the approval writer (Column Protection Rule " + Quote(rule.KeyPath) + ").", column);
                }
            }
        }

        // Columns the engine computes. A caller's value is removed (or replaced in the stamps) with an info reason. The
        // engine's own nested writes carry the computed values, so they pass.
        private void EngineComputedColumns()
        {
            if (Nested)
            {
                return;
            }

            IEnumerable<string> columns = _entity.EngineComputed.Concat(new[] { WriteGuardEntities.ClientAppVerified });
            if (_entity.MeaningColumn != null)
            {
                columns = columns.Concat(new[] { _entity.MeaningColumn });
            }

            foreach (string column in columns.Distinct(StringComparer.Ordinal))
            {
                if (!_after.TryGetMember(column, out JsonTreeNode? value) || _handled.Contains(column))
                {
                    continue;
                }

                _handled.Add(column);
                if (_set.TryGetValue(column, out JsonTreeNode? engineValue))
                {
                    if (!engineValue.Equals(value))
                    {
                        Info(ReasonCodes.EngineColumnOverwritten, "The engine replaced the value given for " + EntityName + " " + column + ".", column);
                    }
                }
                else
                {
                    _removed.Add(column);
                    Info(ReasonCodes.EngineColumnOverwritten, "The engine computes " + EntityName + " " + column + ", so the value given was dropped.", column);
                }
            }
        }

        // Document 22, section 2.1.1: Id, Created On, Created By, Modified On and Modified By are platform-supplied. No writer
        // sets them, the engine included, so a value given is dropped and listed, as G-09 drops a caller's stamps. The link
        // undo window counts from Created On, so a caller's value must never reach it. Live
        // configuration rows carry the bundle's field names and change only inside activation, so they are left alone.
        private void PlatformColumns()
        {
            if (!_entity.ChecksColumns)
            {
                return;
            }

            foreach (string column in WriteGuardEntities.PlatformColumns)
            {
                if (_after.TryGetMember(column, out _))
                {
                    _removed.Add(column);
                    Info(ReasonCodes.EngineColumnOverwritten, "The platform sets " + EntityName + " " + column + ", so the value given was dropped.", column);
                }
            }
        }

        // The common stamps of every engine-evaluated write (document 22, section 2.4; document 25, section 9).
        private void Stamps()
        {
            bool operational = _entity.Kind == GuardEntityKind.Operational || _entity.Kind == GuardEntityKind.AuditEntry;
            var stamps = new SortedDictionary<string, JsonTreeNode>(StringComparer.Ordinal)
            {
                { "performedByPrincipal", JsonTreeNode.FromString(_actor.PrincipalId) },
                { "performedByActorKind", JsonTreeNode.FromString(EngineContract.ActorKindMeanings.ToWire(Nested ? ActorKindMeaning.Engine : _actor.ActorKind)) },
                { "onBehalfOf", Nested ? JsonTreeNode.FromString(_actor.OnBehalfOfPrincipalId ?? _actor.PrincipalId) : (_actor.OnBehalfOfPrincipalId == null ? JsonTreeNode.Null : JsonTreeNode.FromString(_actor.OnBehalfOfPrincipalId)) },
                { "clientApp", _context.ClientApp == null ? JsonTreeNode.Null : JsonTreeNode.FromString(_context.ClientApp) },
                { "engineVersion", JsonTreeNode.FromString(EngineInfo.Version) },
                { "engineCheckedOn", JsonTreeNode.FromUtcDateTime(_context.AsOfUtc) },
            };
            if (Op == WriteOperation.Create)
            {
                stamps["createdByPrincipal"] = JsonTreeNode.FromString(_actor.PrincipalId);
                stamps["isAiCreated"] = JsonTreeNode.FromBoolean(!Nested && ContractFacts.IsAiActor(_actor.ActorKind));
                if (operational)
                {
                    stamps["configurationSetVersion"] = JsonTreeNode.FromInt64(_context.ConfigurationVersion);
                }
            }

            foreach (KeyValuePair<string, JsonTreeNode> stamp in stamps)
            {
                Set(stamp.Key, stamp.Value);
            }

            if (Nested)
            {
                return;
            }

            // A caller's value in a stamp column is replaced, or dropped when this write sets no such stamp.
            foreach (string column in WriteGuardEntities.StampColumns)
            {
                if (!_after.TryGetMember(column, out JsonTreeNode? value))
                {
                    continue;
                }

                if (!stamps.TryGetValue(column, out JsonTreeNode? engineValue))
                {
                    _removed.Add(column);
                    Info(ReasonCodes.EngineColumnOverwritten, "The engine sets " + column + ", so the value given was dropped.", column);
                }
                else if (!engineValue.Equals(value))
                {
                    Info(ReasonCodes.EngineColumnOverwritten, "The engine replaced the value given for " + column + ".", column);
                }
            }
        }

        // ---- Helpers ----

        private ConfigurationSetKind RequireSetKind()
        {
            return _request.ConfigurationSetKind ?? throw OperationStop.Input(ReasonCodes.InputRequired, "facts.configurationSetKind is required for a write to " + EntityName + ".");
        }

        // The C05 row the engine picks when it sets a meaning: the default for the meaning, or the only active one.
        private string DefaultStatus(string entityKey, string meaning)
        {
            SnapshotRow[] rows = SnapshotRead.ActiveRows(_snapshot, "status")
                .Where(r => string.Equals(SnapshotRead.Text(r, "appliesToEntity"), entityKey, StringComparison.Ordinal) && string.Equals(r.EngineMeaning, meaning, StringComparison.Ordinal))
                .ToArray();
            SnapshotRow[] defaults = rows.Where(r => SnapshotRead.Flag(r, "isDefaultForMeaning")).ToArray();
            SnapshotRow? pick = defaults.Length == 1 ? defaults[0] : rows.Length == 1 ? rows[0] : null;
            if (pick == null)
            {
                throw OperationStop.Broken(
                    ReasonCodes.MeaningMissing,
                    SnapshotRead.LogicalName(entityKey) + " has " + (rows.Length == 0 ? "no active status" : "no single default status") + " for the meaning " + meaning + ", so the engine cannot set it.",
                    "Status",
                    null);
            }

            return pick.KeyPath;
        }

        // A column's value from after when the write gives it, else from before.
        private JsonTreeNode? Column(string name)
        {
            if (_after.TryGetMember(name, out JsonTreeNode? value))
            {
                return value;
            }

            return _before.TryGetMember(name, out JsonTreeNode? old) ? old : null;
        }

        private bool OnlyColumns(IEnumerable<string> allowed)
        {
            var set = new HashSet<string>(allowed, StringComparer.Ordinal);
            return _after.Members.All(m => set.Contains(m.Key));
        }

        private void Set(string column, JsonTreeNode value)
        {
            _set[column] = value;
        }

        private void Refuse(string code, string message, string? reference)
        {
            _reasons.Add(new Reason(code, ReasonSeverity.Error, message, EntityNameOrNull(), reference));
        }

        private void Warn(string code, string message, string? reference)
        {
            _reasons.Add(new Reason(code, ReasonSeverity.Warning, message, EntityNameOrNull(), reference));
        }

        private void Info(string code, string message, string? reference)
        {
            _reasons.Add(new Reason(code, ReasonSeverity.Info, message, EntityNameOrNull(), reference));
        }

        private string? EntityNameOrNull()
        {
            return _entity == null ? null : EntityName;
        }

        private PlannedEffect Audit(string operation, string entityName, string? rowId, JsonTreeNode changedValues)
        {
            JsonTreeNode S(string? value) => value == null ? JsonTreeNode.Null : JsonTreeNode.FromString(value);
            return new PlannedEffect(
                EffectKind.WriteAuditEntry,
                "Audit Entry",
                WriteOperation.Create,
                null,
                null,
                Columns(
                    ("entityName", JsonTreeNode.FromString(entityName)),
                    ("rowId", S(rowId)),
                    ("operation", JsonTreeNode.FromString(operation)),
                    ("changedValuesJson", changedValues),
                    ("actor", JsonTreeNode.FromString(_actor.PrincipalId)),
                    ("actorKind", JsonTreeNode.FromString(EngineContract.ActorKindMeanings.ToWire(_actor.ActorKind))),
                    ("onBehalfOf", S(_actor.OnBehalfOfPrincipalId)),
                    ("clientApp", S(_context.ClientApp)),
                    ("occurredOn", JsonTreeNode.FromUtcDateTime(_context.AsOfUtc)),
                    ("correlationId", JsonTreeNode.FromString(_context.CorrelationId)),
                    ("requestKey", S(_request.IdempotencyKey))));
        }

        private OperationAnswer<WritePlan> Answer(string hash, bool isReplay)
        {
            bool allowed = !HasRefusal;
            List<Reason> ordered = _reasons.OrderBy(r => r, Reason.CanonicalOrder).ToList();
            IEnumerable<WriteRefusal> refusals = ordered.Where(r => r.Severity == ReasonSeverity.Error).Select(r => new WriteRefusal(r.Code, GuardRuleId(r.Code)));
            bool quiet = !allowed || isReplay || _noOp;
            JsonTreeNode set = quiet ? GuardJson.Empty : JsonTreeNode.CreateObject(_set);
            var plan = new WritePlan(
                allowed,
                hash,
                isReplay,
                allowed && _noOp,
                refusals,
                set,
                quiet ? Enumerable.Empty<string>() : _removed,
                quiet ? Enumerable.Empty<PlannedEffect>() : _effects);
            return new OperationAnswer<WritePlan>(allowed ? "allowed" : "refused", plan, ordered, _trace);
        }

        private static string? GuardRuleId(string code)
        {
            return ReasonCatalog.TryGet(code, out ReasonCodeInfo? info) ? info.RuleIds.FirstOrDefault(id => id.StartsWith("G-", StringComparison.Ordinal)) : null;
        }

        private static JsonTreeNode Columns(params (string Name, JsonTreeNode Value)[] columns)
        {
            return JsonTreeNode.CreateObject(columns.OrderBy(c => c.Name, StringComparer.Ordinal).Select(c => new KeyValuePair<string, JsonTreeNode>(c.Name, c.Value)));
        }

        private static string? Text(JsonTreeNode? node, string? member)
        {
            if (node == null)
            {
                return null;
            }

            JsonTreeNode? value = node;
            if (member != null && !node.TryGetMember(member, out value))
            {
                return null;
            }

            return value != null && value.Kind == JsonTreeKind.String && value.GetString().Length > 0 ? value.GetString() : null;
        }

        private static bool Flag(JsonTreeNode node, string member)
        {
            return node.TryGetMember(member, out JsonTreeNode? value) && value.Kind == JsonTreeKind.True;
        }

        private static DateTime? Utc(JsonTreeNode node, string member)
        {
            string? text = Text(node, member);
            if (text == null)
            {
                return null;
            }

            if (!DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out DateTime value) || !text.EndsWith("Z", StringComparison.Ordinal))
            {
                throw OperationStop.Input(ReasonCodes.InputMalformed, "facts.before." + member + " must be a UTC date-time such as 2026-11-22T09:00:00Z.");
            }

            return DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        private static string Quote(string value)
        {
            return SnapshotRead.Quote(value);
        }
    }
}

// Small JSON helpers for OP-08.
internal static class GuardJson
{
    public static readonly JsonTreeNode Empty = JsonTreeNode.CreateObject();
}
