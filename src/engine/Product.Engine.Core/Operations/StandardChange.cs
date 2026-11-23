using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;
using Product.Engine.Core.Snapshots;

namespace Product.Engine.Core.Operations;

// The facts a standard change model's conditions test (document 25, section 5.13, StandardChangeFacts). The adapter
// reads them from the change request (O11), its main application (O02) and its affected applications (O12).
public sealed class StandardChangeFacts
{
    public StandardChangeFacts(
        int? tierRank = null,
        IEnumerable<string>? targetStageKeys = null,
        int? affectedApplicationCount = null,
        IEnumerable<string>? componentKindKeys = null,
        int? windowMinutes = null,
        IEnumerable<string>? statedConditionKeys = null)
    {
        if (affectedApplicationCount.HasValue && affectedApplicationCount.Value < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(affectedApplicationCount), "The count includes the main application, so it is at least 1.");
        }

        if (windowMinutes.HasValue && windowMinutes.Value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(windowMinutes), "A window length is not negative.");
        }

        TierRank = tierRank;
        TargetStageKeys = Keys(targetStageKeys, nameof(targetStageKeys));
        AffectedApplicationCount = affectedApplicationCount;
        ComponentKindKeys = Keys(componentKindKeys, nameof(componentKindKeys));
        WindowMinutes = windowMinutes;
        StatedConditionKeys = Keys(statedConditionKeys, nameof(statedConditionKeys));
    }

    // The rank of the main application's tier. Null when the application has none, which never meets a tier limit.
    public int? TierRank { get; }

    // The stages of the planned targets.
    public IReadOnlyList<string> TargetStageKeys { get; }

    // The main application plus Affected Applications (O12).
    public int? AffectedApplicationCount { get; }

    // The component kinds the change touches, as stated on the change request.
    public IReadOnlyList<string> ComponentKindKeys { get; }

    // The length of the change window. Null when the change has no window.
    public int? WindowMinutes { get; }

    // The stated conditions the requester confirmed on the change request.
    public IReadOnlyList<string> StatedConditionKeys { get; }

    internal static StandardChangeFacts FromJson(WireObject wire)
    {
        int? count = wire.OptionalInt("affectedApplicationCount");
        if (count.HasValue && count.Value < 1)
        {
            throw OperationStop.Input(ReasonCodes.InputMalformed, wire.Where + ".affectedApplicationCount must be at least 1: it counts the main application too.");
        }

        int? minutes = wire.OptionalInt("windowMinutes");
        if (minutes.HasValue && minutes.Value < 0)
        {
            throw OperationStop.Input(ReasonCodes.InputMalformed, wire.Where + ".windowMinutes must not be negative.");
        }

        return new StandardChangeFacts(
            wire.OptionalInt("tierRank"),
            wire.Keys("targetStageKeys"),
            count,
            wire.Keys("componentKindKeys"),
            minutes,
            wire.Keys("statedConditionKeys"));
    }

    private static IReadOnlyList<string> Keys(IEnumerable<string>? values, string name)
    {
        string[] keys = (values ?? Enumerable.Empty<string>()).Distinct(StringComparer.Ordinal).ToArray();
        if (keys.Any(key => !ConfigurationKey.IsValid(key)))
        {
            throw new ArgumentException("Keys must use the configuration key format.", name);
        }

        return new ReadOnlyCollection<string>(keys);
    }
}

// The status meaning of a candidate model's Approved By Decision (O09), fetched by the adapter.
public sealed class ModelApprovalFact
{
    public ModelApprovalFact(string modelKey, DecisionStatusMeaning approvedByDecisionStatus)
    {
        if (!ConfigurationKey.IsValid(modelKey))
        {
            throw new ArgumentException("A model key must use the configuration key format.", nameof(modelKey));
        }

        ModelKey = modelKey;
        ApprovedByDecisionStatus = approvedByDecisionStatus;
    }

    public string ModelKey { get; }

    public DecisionStatusMeaning ApprovedByDecisionStatus { get; }
}

// OP-12 input (document 25, section 5.13). Configuration references travel as keys.
public sealed class StandardChangeRequest
{
    public StandardChangeRequest(string changeTypeKey, string? standardChangeModelKey, StandardChangeFacts change, IEnumerable<ModelApprovalFact>? models = null)
    {
        if (!ConfigurationKey.IsValid(changeTypeKey))
        {
            throw new ArgumentException("A change type key must use the configuration key format.", nameof(changeTypeKey));
        }

        if (standardChangeModelKey != null && !ConfigurationKey.IsValid(standardChangeModelKey))
        {
            throw new ArgumentException("A model key must use the configuration key format.", nameof(standardChangeModelKey));
        }

        ModelApprovalFact[] list = (models ?? Enumerable.Empty<ModelApprovalFact>()).ToArray();
        if (list.Select(m => m.ModelKey).Distinct(StringComparer.Ordinal).Count() != list.Length)
        {
            throw new ArgumentException("Each model's approving decision status is given once.", nameof(models));
        }

        ChangeTypeKey = changeTypeKey;
        StandardChangeModelKey = standardChangeModelKey;
        Change = change ?? throw new ArgumentNullException(nameof(change));
        Models = new ReadOnlyCollection<ModelApprovalFact>(list);
    }

    // The change request's change type (C20).
    public string ChangeTypeKey { get; }

    // The model the requester names. When empty, every active model of the change type is tried.
    public string? StandardChangeModelKey { get; }

    public StandardChangeFacts Change { get; }

    // The status meaning of each candidate model's Approved By Decision (fetched).
    public IReadOnlyList<ModelApprovalFact> Models { get; }

    public static StandardChangeRequest FromJson(JsonTreeNode input)
    {
        var wire = new WireObject(input, "input", "changeTypeKey", "standardChangeModelKey", "change", "models");
        WireObject change = wire.OptionalObject(
                "change",
                "tierRank",
                "targetStageKeys",
                "affectedApplicationCount",
                "componentKindKeys",
                "windowMinutes",
                "statedConditionKeys")
            ?? throw OperationStop.Input(ReasonCodes.InputRequired, "input.change is required: the conditions test the change's facts.");

        var models = new List<ModelApprovalFact>();
        foreach (WireObject model in wire.Objects("models", required: false, "modelKey", "approvedByDecisionStatus"))
        {
            string key = model.RequiredKey("modelKey");
            if (models.Any(m => string.Equals(m.ModelKey, key, StringComparison.Ordinal)))
            {
                throw OperationStop.Input(ReasonCodes.InputMalformed, "input.models gives model " + SnapshotRead.Quote(key) + " twice.", key);
            }

            models.Add(new ModelApprovalFact(key, model.Contract("approvedByDecisionStatus", EngineContract.DecisionStatusMeanings)));
        }

        return new StandardChangeRequest(wire.RequiredKey("changeTypeKey"), wire.OptionalKey("standardChangeModelKey"), StandardChangeFacts.FromJson(change), models);
    }
}

// One condition that failed for one model.
public sealed class FailedCondition
{
    public FailedCondition(string modelKey, int? conditionIndex, ConditionKind conditionKind, string why)
    {
        ModelKey = modelKey;
        ConditionIndex = conditionIndex;
        ConditionKind = conditionKind;
        Why = why;
    }

    public string ModelKey { get; }

    // The position in the model's Conditions Json, from 0. Absent for the model's Tier column, which this build tests
    // as one more max-tier-rank condition (a proposal: C21 Tier is "the highest tier the model applies to").
    public int? ConditionIndex { get; }

    public ConditionKind ConditionKind { get; }

    // One plain sentence.
    public string Why { get; }

    internal JsonTreeNode ToJson()
    {
        return new WireWriter()
            .Add("modelKey", ModelKey)
            .Add("conditionIndex", ConditionIndex)
            .Add("conditionKind", EngineContract.ConditionKinds.ToWire(ConditionKind))
            .Add("why", Why)
            .Build();
    }
}

// OP-12 output (document 25, section 5.13). OP-02 reads qualifies, modelKey and approvedByDecisionId from it.
public sealed class StandardChangeResult : IOperationPayload
{
    public StandardChangeResult(bool qualifies, string? modelKey, string? approvedByDecisionId, IEnumerable<FailedCondition> failedConditions)
    {
        Qualifies = qualifies;
        ModelKey = modelKey;
        ApprovedByDecisionId = approvedByDecisionId;
        FailedConditions = new ReadOnlyCollection<FailedCondition>(failedConditions.ToList());
    }

    // True when one model's conditions are all met and its Approved By Decision is accepted.
    public bool Qualifies { get; }

    // The qualifying model.
    public string? ModelKey { get; }

    // The decision that approved the model; OP-02 cites it as the acceptance basis.
    public string? ApprovedByDecisionId { get; }

    // Every condition that failed, per model tried, by model key then condition order.
    public IReadOnlyList<FailedCondition> FailedConditions { get; }

    public JsonTreeNode ToJson()
    {
        return new WireWriter()
            .Add("qualifies", Qualifies)
            .Add("modelKey", ModelKey)
            .Add("approvedByDecisionId", ApprovedByDecisionId)
            .Add("failedConditions", WireWriter.Array(FailedConditions.Select(c => c.ToJson())))
            .Build();
    }
}

// OP-12 Evaluate a standard change (document 25, section 5.13). Backs check_standard_change (document 24, tool 23)
// and rule 8 of OP-02. An AI client never decides that a change is standard; the engine evaluates the model's
// conditions (working default, 28 issue 12).
//
// WORKING DEFAULT (open design question 8): a qualifying model lets OP-02 accept the change authorization without a
// card, under the locked list. If the answer is no, OP-12 still answers, but OP-02 stops using it.
// WORKING DEFAULT (open design question 1): the six condition kinds are a fixed engine contract list.
public static class StandardChangeOperation
{
    private const string ModelEntity = "standard-change-model";
    private const string ModelName = "Standard Change Model";

    public static EvaluationResult<StandardChangeResult?> Evaluate(ConfigurationSnapshot snapshot, EvaluationContext context, StandardChangeRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return OperationShell.Run(context, snapshot, needsSnapshot: true, () => Answer(snapshot, request));
    }

    public static EvaluationResult<StandardChangeResult?> Evaluate(ConfigurationSnapshot snapshot, EvaluationContext context, JsonTreeNode input)
    {
        return OperationShell.Run(context, snapshot, needsSnapshot: true, () => Answer(snapshot, StandardChangeRequest.FromJson(input)));
    }

    private static OperationAnswer<StandardChangeResult> Answer(ConfigurationSnapshot snapshot, StandardChangeRequest request)
    {
        SnapshotRow changeType = SnapshotRead.RequireExisting(snapshot, "change-type", request.ChangeTypeKey, "input.changeTypeKey");
        foreach (ModelApprovalFact fact in request.Models)
        {
            SnapshotRead.RequireExisting(snapshot, ModelEntity, fact.ModelKey, "input.models[].modelKey");
        }

        SnapshotRow? named = request.StandardChangeModelKey == null
            ? null
            : SnapshotRead.RequireExisting(snapshot, ModelEntity, request.StandardChangeModelKey, "input.standardChangeModelKey");

        var trace = new List<RuleTrace> { new RuleTrace("Change Type", changeType.KeyPath, RuleTraceRole.Matched) };
        var reasons = new List<Reason>();

        // Rule 1: an emergency change type never qualifies. An emergency is known by Allows Authorization After, the
        // column V25 reads, not by its key (the same reading as the engine check standard-model.type).
        if (SnapshotRead.Flag(changeType, "allowsAuthorizationAfter"))
        {
            reasons.Add(new Reason(
                ReasonCodes.StandardModelType,
                ReasonCatalog.Get(ReasonCodes.StandardModelType).DefaultSeverity,
                "Change type " + SnapshotRead.Quote(changeType.KeyPath) + " allows authorization after the change, as an emergency does, so no change of it is standard.",
                "Change Type",
                changeType.KeyPath));
            return DoesNotQualify(Array.Empty<FailedCondition>(), reasons, trace);
        }

        // Rule 1: the candidates. A named model is the only one tried; otherwise every active model of the change type.
        List<SnapshotRow> candidates;
        if (named != null)
        {
            string? namedType = SnapshotRead.Text(named, "changeTypeKey");
            if (!named.IsActive || !string.Equals(namedType, changeType.KeyPath, StringComparison.Ordinal))
            {
                string why = !named.IsActive ? "is retired" : "belongs to change type " + SnapshotRead.Quote(namedType ?? "?");
                reasons.Add(NoCandidate("The named model " + SnapshotRead.Quote(named.KeyPath) + " " + why + ", so it cannot make a change of type " + SnapshotRead.Quote(changeType.KeyPath) + " standard.", named.KeyPath));
                trace.Add(new RuleTrace(ModelName, named.KeyPath, RuleTraceRole.Skipped));
                return DoesNotQualify(Array.Empty<FailedCondition>(), reasons, trace);
            }

            candidates = new List<SnapshotRow> { named };
        }
        else
        {
            candidates = SnapshotRead.ActiveRows(snapshot, ModelEntity)
                .Where(row => string.Equals(SnapshotRead.Text(row, "changeTypeKey"), changeType.KeyPath, StringComparison.Ordinal))
                .OrderBy(row => row.KeyPath, StringComparer.Ordinal)
                .ToList();
            if (candidates.Count == 0)
            {
                reasons.Add(NoCandidate("Change type " + SnapshotRead.Quote(changeType.KeyPath) + " has no active standard change model, so no change of it is standard.", changeType.KeyPath));
                return DoesNotQualify(Array.Empty<FailedCondition>(), reasons, trace);
            }
        }

        var failed = new List<FailedCondition>();
        var qualifying = new List<SnapshotRow>();
        var notApproved = new List<Reason>();
        foreach (SnapshotRow model in candidates)
        {
            // Rule 3 runs first, so a broken model is reported even when it is not approved (document 25, rule 10).
            List<FailedCondition> modelFailures = Conditions(snapshot, model, request.Change);

            // Rule 2: the Approved By Decision must be accepted. Otherwise the model never qualifies.
            string? approvedBy = SnapshotRead.Text(model, "approvedByDecisionId");
            ModelApprovalFact? status = request.Models.FirstOrDefault(m => string.Equals(m.ModelKey, model.KeyPath, StringComparison.Ordinal));
            string? notApprovedWhy = approvedBy == null
                ? "names no approving decision"
                : status == null
                    ? "has no known status for its approving decision " + SnapshotRead.Quote(approvedBy) + ", and an unknown status is never accepted"
                    : status.ApprovedByDecisionStatus != DecisionStatusMeaning.Accepted
                        ? "has an approving decision " + SnapshotRead.Quote(approvedBy) + " that is " + EngineContract.DecisionStatusMeanings.ToWire(status.ApprovedByDecisionStatus) + ", not accepted"
                        : null;
            if (notApprovedWhy != null)
            {
                notApproved.Add(new Reason(
                    ReasonCodes.StandardModelNotApproved,
                    ReasonCatalog.Get(ReasonCodes.StandardModelNotApproved).DefaultSeverity,
                    "Model " + SnapshotRead.Quote(model.KeyPath) + " " + notApprovedWhy + ", so it never qualifies.",
                    ModelName,
                    model.KeyPath));
                trace.Add(new RuleTrace(ModelName, model.KeyPath, RuleTraceRole.Skipped));
                continue;
            }

            failed.AddRange(modelFailures);
            if (modelFailures.Count == 0)
            {
                qualifying.Add(model);
            }
            else
            {
                trace.Add(new RuleTrace(ModelName, model.KeyPath, RuleTraceRole.Skipped));
            }
        }

        if (qualifying.Count == 0)
        {
            reasons.AddRange(notApproved);
            reasons.AddRange(failed.Select(f => new Reason(
                ReasonCodes.StandardModelConditionNotMet,
                ReasonCatalog.Get(ReasonCodes.StandardModelConditionNotMet).DefaultSeverity,
                "Model " + SnapshotRead.Quote(f.ModelKey) + ": " + f.Why + ".",
                ModelName,
                f.ModelKey)));
            return DoesNotQualify(failed, reasons, trace);
        }

        // Rule 4: the named model wins; otherwise the first by key. Candidates are in key order already.
        SnapshotRow winner = qualifying[0];
        trace.Add(new RuleTrace(ModelName, winner.KeyPath, RuleTraceRole.Won));
        trace.AddRange(qualifying.Skip(1).Select(row => new RuleTrace(ModelName, row.KeyPath, RuleTraceRole.Dropped)));

        reasons.Add(new Reason(
            ReasonCodes.StandardModelQualifies,
            ReasonCatalog.Get(ReasonCodes.StandardModelQualifies).DefaultSeverity,
            "The change fits standard change model " + SnapshotRead.Quote(winner.KeyPath) + ", which decision " + SnapshotRead.Quote(SnapshotRead.Text(winner, "approvedByDecisionId")!) + " approved.",
            ModelName,
            winner.KeyPath));

        // A stated condition is met only as reported: the engine cannot see the work itself.
        foreach (ParsedCondition stated in Parse(winner).Where(c => c.Kind == ConditionKind.Stated))
        {
            reasons.Add(new Reason(
                ReasonCodes.StandardModelConditionAsReported,
                ReasonCatalog.Get(ReasonCodes.StandardModelConditionAsReported).DefaultSeverity,
                "Condition " + stated.Index.ToString(CultureInfo.InvariantCulture) + " of model " + SnapshotRead.Quote(winner.KeyPath) + " (" + SnapshotRead.Quote(stated.Key!) + ") is met as reported by the requester.",
                ModelName,
                winner.KeyPath));
        }

        var payload = new StandardChangeResult(true, winner.KeyPath, SnapshotRead.Text(winner, "approvedByDecisionId"), failed);
        return new OperationAnswer<StandardChangeResult>("qualifies", payload, reasons, trace);
    }

    private static OperationAnswer<StandardChangeResult> DoesNotQualify(IEnumerable<FailedCondition> failed, List<Reason> reasons, List<RuleTrace> trace)
    {
        return new OperationAnswer<StandardChangeResult>("does-not-qualify", new StandardChangeResult(false, null, null, failed), reasons, trace);
    }

    private static Reason NoCandidate(string message, string reference)
    {
        return new Reason(ReasonCodes.StandardModelNoCandidate, ReasonCatalog.Get(ReasonCodes.StandardModelNoCandidate).DefaultSeverity, message, ModelName, reference);
    }

    // Rule 3: every condition of the model, in order, plus the model's Tier column when it is set.
    private static List<FailedCondition> Conditions(ConfigurationSnapshot snapshot, SnapshotRow model, StandardChangeFacts change)
    {
        var failed = new List<FailedCondition>();
        string? tierKey = SnapshotRead.Text(model, "tierKey");
        if (tierKey != null)
        {
            SnapshotRow tier = SnapshotRead.Find(snapshot, "tier", tierKey)
                ?? throw OperationStop.Broken(
                    ReasonCodes.RefUnresolved,
                    ModelName + " " + SnapshotRead.Quote(model.KeyPath) + " names tier " + SnapshotRead.Quote(tierKey) + ", which does not exist. The configuration is broken.",
                    ModelName,
                    model.KeyPath);
            int limit = SnapshotRead.Int(tier, "tier", "rank")
                ?? throw OperationStop.Broken(
                    ReasonCodes.ShapeSchema,
                    "Tier " + SnapshotRead.Quote(tierKey) + " has no rank, so the engine cannot compare a change with model " + SnapshotRead.Quote(model.KeyPath) + ".",
                    "Tier",
                    tierKey);
            string? why = change.TierRank == null
                ? "the model applies up to tier " + SnapshotRead.Quote(tierKey) + " (rank " + Number(limit) + "), and the main application has no tier rank"
                : change.TierRank.Value > limit
                    ? "the model applies up to tier " + SnapshotRead.Quote(tierKey) + " (rank " + Number(limit) + "), and the main application's tier rank is " + Number(change.TierRank.Value)
                    : null;
            if (why != null)
            {
                failed.Add(new FailedCondition(model.KeyPath, null, ConditionKind.MaxTierRank, why));
            }
        }

        foreach (ParsedCondition condition in Parse(model))
        {
            string? why = Test(condition, change);
            if (why != null)
            {
                failed.Add(new FailedCondition(model.KeyPath, condition.Index, condition.Kind, why));
            }
        }

        return failed;
    }

    // Null when the condition is met; otherwise why not. A fact the change does not state never meets a condition.
    private static string? Test(ParsedCondition condition, StandardChangeFacts change)
    {
        switch (condition.Kind)
        {
            case ConditionKind.MaxTierRank:
                return change.TierRank == null
                    ? "the main application has no tier rank, so it cannot be shown to be at most " + Number(condition.Limit)
                    : change.TierRank.Value > condition.Limit
                        ? "the main application's tier rank is " + Number(change.TierRank.Value) + ", above the maximum of " + Number(condition.Limit)
                        : null;
            case ConditionKind.MaxAffectedApplications:
                return change.AffectedApplicationCount == null
                    ? "the change states no count of affected applications, so it cannot be shown to be at most " + Number(condition.Limit)
                    : change.AffectedApplicationCount.Value > condition.Limit
                        ? "the change affects " + Number(change.AffectedApplicationCount.Value) + " applications, more than " + Number(condition.Limit)
                        : null;
            case ConditionKind.MaxWindowMinutes:
                return change.WindowMinutes == null
                    ? "the change has no window, so its length cannot be shown to be at most " + Number(condition.Limit) + " minutes"
                    : change.WindowMinutes.Value > condition.Limit
                        ? "the change window lasts " + Number(change.WindowMinutes.Value) + " minutes, longer than " + Number(condition.Limit)
                        : null;
            case ConditionKind.TargetStagesIn:
                return Within(change.TargetStageKeys, condition.Allowed, "target stage", "the change names no target stage");
            case ConditionKind.ComponentKindsWithin:
                return Within(change.ComponentKindKeys, condition.Allowed, "component kind", "the change states no component kinds");
            case ConditionKind.Stated:
                return change.StatedConditionKeys.Contains(condition.Key!, StringComparer.Ordinal)
                    ? null
                    : "the requester did not confirm " + SnapshotRead.Quote(condition.Key!) + (condition.Label == null ? string.Empty : " (" + condition.Label + ")");
            default:
                throw new InvalidOperationException("No test for condition kind " + condition.Kind + ".");
        }
    }

    // Every stated key must be one of the allowed keys. An empty list cannot be shown to stay within them.
    private static string? Within(IReadOnlyList<string> stated, IReadOnlyList<string> allowed, string what, string whenEmpty)
    {
        if (stated.Count == 0)
        {
            return whenEmpty;
        }

        string[] outside = stated.Where(key => !allowed.Contains(key, StringComparer.Ordinal)).OrderBy(key => key, StringComparer.Ordinal).ToArray();
        return outside.Length == 0
            ? null
            : what + (outside.Length == 1 ? " " : "s ") + string.Join(", ", outside.Select(SnapshotRead.Quote)) + (outside.Length == 1 ? " is" : " are")
                + " not one of " + string.Join(", ", allowed.Select(SnapshotRead.Quote));
    }

    // Reads Conditions Json. The snapshot in force passed OP-09 (V39 and standard-model.condition-kind), so a model
    // the engine cannot read is a broken configuration: it refuses and reports, and never skips the condition.
    private static List<ParsedCondition> Parse(SnapshotRow model)
    {
        JsonTreeNode? conditions = SnapshotRead.Node(model, "conditions");
        if (conditions == null || conditions.Kind != JsonTreeKind.Array || conditions.Items.Count == 0)
        {
            throw Broken(model, "has no conditions, and a model with none would make every change standard");
        }

        var parsed = new List<ParsedCondition>();
        for (int i = 0; i < conditions.Items.Count; i++)
        {
            JsonTreeNode item = conditions.Items[i];
            string at = "condition " + i.ToString(CultureInfo.InvariantCulture);
            if (item.Kind != JsonTreeKind.Object || !item.TryGetMember("conditionKind", out JsonTreeNode? kindNode) || kindNode.Kind != JsonTreeKind.String)
            {
                throw Broken(model, "has a " + at + " without a condition kind");
            }

            if (!EngineContract.ConditionKinds.TryParse(kindNode.GetString(), out ConditionKind kind))
            {
                throw OperationStop.Broken(
                    ReasonCodes.StandardModelConditionKind,
                    ModelName + " " + SnapshotRead.Quote(model.KeyPath) + " has a " + at + " of kind " + SnapshotRead.Quote(kindNode.GetString())
                    + ", which is not one of " + string.Join(", ", EngineContract.ConditionKinds.WireValues) + ". The configuration is broken.",
                    ModelName,
                    model.KeyPath);
            }

            item.TryGetMember("value", out JsonTreeNode? value);
            switch (kind)
            {
                case ConditionKind.MaxTierRank:
                case ConditionKind.MaxAffectedApplications:
                case ConditionKind.MaxWindowMinutes:
                    if (value == null || !value.TryGetInt64(out long limit) || limit < 0 || limit > int.MaxValue)
                    {
                        throw Broken(model, "has a " + at + " whose value is not a whole number of at least 0");
                    }

                    parsed.Add(new ParsedCondition(i, kind, (int)limit, Array.Empty<string>(), null, null));
                    break;
                case ConditionKind.TargetStagesIn:
                case ConditionKind.ComponentKindsWithin:
                    if (value == null || value.Kind != JsonTreeKind.Array || value.Items.Count == 0 || value.Items.Any(v => v.Kind != JsonTreeKind.String))
                    {
                        throw Broken(model, "has a " + at + " whose value is not a list of keys");
                    }

                    parsed.Add(new ParsedCondition(i, kind, 0, value.Items.Select(v => v.GetString()).ToArray(), null, null));
                    break;
                default:
                    if (!item.TryGetMember("key", out JsonTreeNode? keyNode) || keyNode.Kind != JsonTreeKind.String || !ConfigurationKey.IsValid(keyNode.GetString()))
                    {
                        throw Broken(model, "has a stated " + at + " without a key");
                    }

                    string? label = item.TryGetMember("label", out JsonTreeNode? labelNode) && labelNode.Kind == JsonTreeKind.String ? labelNode.GetString() : null;
                    parsed.Add(new ParsedCondition(i, kind, 0, Array.Empty<string>(), keyNode.GetString(), label));
                    break;
            }
        }

        return parsed;
    }

    private static OperationStop Broken(SnapshotRow model, string what)
    {
        return OperationStop.Broken(
            ReasonCodes.JsonSchema,
            ModelName + " " + SnapshotRead.Quote(model.KeyPath) + " " + what + ". The configuration is broken.",
            ModelName,
            model.KeyPath);
    }

    private static string Number(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private sealed class ParsedCondition
    {
        public ParsedCondition(int index, ConditionKind kind, int limit, IReadOnlyList<string> allowed, string? key, string? label)
        {
            Index = index;
            Kind = kind;
            Limit = limit;
            Allowed = allowed;
            Key = key;
            Label = label;
        }

        public int Index { get; }

        public ConditionKind Kind { get; }

        // For the three max kinds.
        public int Limit { get; }

        // For target-stages-in and component-kinds-within.
        public IReadOnlyList<string> Allowed { get; }

        // For stated.
        public string? Key { get; }

        public string? Label { get; }
    }
}
