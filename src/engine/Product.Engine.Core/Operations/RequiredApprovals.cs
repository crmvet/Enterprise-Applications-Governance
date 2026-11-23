using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;
using Product.Engine.Core.Snapshots;

namespace Product.Engine.Core.Operations;

// The part of an OP-12 result that OP-02 reads (document 25, sections 5.4 and 5.13). The wire reader takes the whole
// OP-12 payload as it came (failedConditions too, which OP-02 does not read), so the runtime passes it on unchanged.
public sealed class StandardChangeFact
{
    public StandardChangeFact(bool qualifies, string? modelKey = null, string? approvedByDecisionId = null)
    {
        if (modelKey != null && !ConfigurationKey.IsValid(modelKey))
        {
            throw new ArgumentException("A model key must use the configuration key format.", nameof(modelKey));
        }

        Qualifies = qualifies;
        ModelKey = modelKey;
        ApprovedByDecisionId = string.IsNullOrEmpty(approvedByDecisionId) ? null : approvedByDecisionId;
    }

    // True when one model's conditions all hold and its Approved By Decision is accepted.
    public bool Qualifies { get; }

    // The qualifying model (C21).
    public string? ModelKey { get; }

    // The decision that approved the model. OP-02 cites it as the acceptance basis.
    public string? ApprovedByDecisionId { get; }

    internal static StandardChangeFact? FromJson(WireObject? wire)
    {
        return wire == null ? null : new StandardChangeFact(wire.RequiredBool("qualifies"), wire.OptionalKey("modelKey"), wire.OptionalString("approvedByDecisionId"));
    }
}

// OP-02 input (document 25, section 5.4). Configuration references travel as keys, operational references as IDs.
public sealed class ApprovalScopeRequest
{
    public ApprovalScopeRequest(
        string decisionTypeKey,
        string? tierKey = null,
        string? lifecycleStageKey = null,
        string? changeTypeKey = null,
        string? applicationKindKey = null,
        string? targetStageKey = null,
        IEnumerable<string>? portfolioPath = null,
        StandardChangeFact? standardChange = null)
    {
        DecisionTypeKey = Key(decisionTypeKey ?? throw new ArgumentNullException(nameof(decisionTypeKey)), nameof(decisionTypeKey))!;
        TierKey = Key(tierKey, nameof(tierKey));
        LifecycleStageKey = Key(lifecycleStageKey, nameof(lifecycleStageKey));
        ChangeTypeKey = Key(changeTypeKey, nameof(changeTypeKey));
        ApplicationKindKey = Key(applicationKindKey, nameof(applicationKindKey));
        TargetStageKey = Key(targetStageKey, nameof(targetStageKey));
        PortfolioPath = new ReadOnlyCollection<string>((portfolioPath ?? Enumerable.Empty<string>()).Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).ToList());
        StandardChange = standardChange;
    }

    public string DecisionTypeKey { get; }

    public string? TierKey { get; }

    public string? LifecycleStageKey { get; }

    public string? ChangeTypeKey { get; }

    public string? ApplicationKindKey { get; }

    public string? TargetStageKey { get; }

    // The subject's portfolio and its ancestors, nearest first.
    public IReadOnlyList<string> PortfolioPath { get; }

    // For change authorization: whether the change fits an approved standard change model (OP-12).
    public StandardChangeFact? StandardChange { get; }

    public static ApprovalScopeRequest FromJson(JsonTreeNode input)
    {
        var wire = new WireObject(
            input,
            "input",
            "decisionTypeKey",
            "tierKey",
            "lifecycleStageKey",
            "changeTypeKey",
            "applicationKindKey",
            "targetStageKey",
            "portfolioPath",
            "standardChange");
        return new ApprovalScopeRequest(
            wire.RequiredKey("decisionTypeKey"),
            wire.OptionalKey("tierKey"),
            wire.OptionalKey("lifecycleStageKey"),
            wire.OptionalKey("changeTypeKey"),
            wire.OptionalKey("applicationKindKey"),
            wire.OptionalKey("targetStageKey"),
            wire.Strings("portfolioPath"),
            StandardChangeFact.FromJson(wire.OptionalObject("standardChange", "qualifies", "modelKey", "approvedByDecisionId", "failedConditions")));
    }

    private static string? Key(string? value, string name)
    {
        if (value == null)
        {
            return null;
        }

        if (!ConfigurationKey.IsValid(value))
        {
            throw new ArgumentException("A key must use the configuration key format.", name);
        }

        return value;
    }
}

// One approval or consultation a decision needs (document 25, section 5.4, RequiredApproval).
public sealed class RequiredApproval
{
    public RequiredApproval(
        ApprovalRequirementSource source,
        string? ruleKey,
        string? decisionRightKey,
        string? approverRoleKey,
        string? approverOwnerRoleKey,
        bool isConsultation,
        ApprovalMode approvalMode,
        int? sequence,
        int minimumCount,
        int? timeoutDays,
        bool requiresTypedReason,
        string? guidelineKey)
    {
        if (source == ApprovalRequirementSource.ApprovalRule && ruleKey == null)
        {
            throw new ArgumentException("A requirement from an approval rule names the rule.", nameof(ruleKey));
        }

        if (source == ApprovalRequirementSource.Accountable && decisionRightKey == null)
        {
            throw new ArgumentException("A requirement from the accountable letter names the decision right row.", nameof(decisionRightKey));
        }

        Source = source;
        RuleKey = ruleKey;
        DecisionRightKey = decisionRightKey;
        ApproverRoleKey = approverRoleKey;
        ApproverOwnerRoleKey = approverOwnerRoleKey;
        IsConsultation = isConsultation;
        ApprovalMode = approvalMode;
        Sequence = sequence;
        MinimumCount = minimumCount;
        TimeoutDays = timeoutDays;
        RequiresTypedReason = requiresTypedReason;
        GuidelineKey = guidelineKey;
    }

    public ApprovalRequirementSource Source { get; }

    // The Approval Rule, when the source is a rule.
    public string? RuleKey { get; }

    // The Decision Right row, when the source is accountable.
    public string? DecisionRightKey { get; }

    // The product role that must approve, when organization-wide.
    public string? ApproverRoleKey { get; }

    // The owner role that must approve, when relative to the application.
    public string? ApproverOwnerRoleKey { get; }

    // True for a consultation: its answer is a recommendation only and never counts.
    public bool IsConsultation { get; }

    public ApprovalMode ApprovalMode { get; }

    // Order among sequential rules.
    public int? Sequence { get; }

    // How many counting approvals are needed. At least 1: there is no zero waiver (V28).
    public int MinimumCount { get; }

    public int? TimeoutDays { get; }

    public bool RequiresTypedReason { get; }

    public string? GuidelineKey { get; }

    // The key approval records point at (ApprovalFact.ruleKey): the rule key, or the decision right key.
    public string RequirementKey => RuleKey ?? DecisionRightKey!;

    public JsonTreeNode ToJson()
    {
        return new WireWriter()
            .Add("source", EngineContract.ApprovalRequirementSources.ToWire(Source))
            .Add("ruleKey", RuleKey)
            .Add("decisionRightKey", DecisionRightKey)
            .Add("approverRoleKey", ApproverRoleKey)
            .Add("approverOwnerRoleKey", ApproverOwnerRoleKey)
            .Add("isConsultation", IsConsultation)
            .Add("approvalMode", EngineContract.ApprovalModes.ToWire(ApprovalMode))
            .Add("sequence", Sequence)
            .Add("minimumCount", MinimumCount)
            .Add("timeoutDays", TimeoutDays)
            .Add("requiresTypedReason", RequiresTypedReason)
            .Add("guidelineKey", GuidelineKey)
            .Build();
    }

    internal static readonly string[] WireFields =
    {
        "source", "ruleKey", "decisionRightKey", "approverRoleKey", "approverOwnerRoleKey", "isConsultation", "approvalMode",
        "sequence", "minimumCount", "timeoutDays", "requiresTypedReason", "guidelineKey",
    };

    // Reads one RequiredApproval as OP-02 wrote it. A minimum count below 1 is a broken configuration (V28).
    internal static RequiredApproval FromJson(WireObject wire)
    {
        ApprovalRequirementSource source = wire.Contract("source", EngineContract.ApprovalRequirementSources);
        string? ruleKey = wire.OptionalKey("ruleKey");
        string? rightKey = wire.OptionalKey("decisionRightKey");
        if (source == ApprovalRequirementSource.ApprovalRule && ruleKey == null)
        {
            throw OperationStop.Input(ReasonCodes.InputRequired, wire.Where + ".ruleKey is required for a requirement from an approval rule.");
        }

        if (source == ApprovalRequirementSource.Accountable && rightKey == null)
        {
            throw OperationStop.Input(ReasonCodes.InputRequired, wire.Where + ".decisionRightKey is required for a requirement from the accountable letter.");
        }

        int minimumCount = wire.OptionalInt("minimumCount") ?? 1;
        if (minimumCount < 1)
        {
            throw OperationStop.Broken(
                ReasonCodes.ApprovalRuleZeroCount,
                "Requirement " + SnapshotRead.Quote(ruleKey ?? rightKey!) + " has a minimum count of " + minimumCount + ", and there is no zero waiver. The configuration is broken.",
                "Approval Rule",
                ruleKey ?? rightKey);
        }

        return new RequiredApproval(
            source,
            ruleKey,
            rightKey,
            wire.OptionalKey("approverRoleKey"),
            wire.OptionalKey("approverOwnerRoleKey"),
            wire.Bool("isConsultation", false),
            wire.Contract("approvalMode", EngineContract.ApprovalModes),
            wire.OptionalInt("sequence"),
            minimumCount,
            wire.OptionalInt("timeoutDays"),
            wire.Bool("requiresTypedReason", false),
            wire.OptionalKey("guidelineKey"));
    }
}

// What the engine accepted without a card, and why (document 25, OP-02 rule 8).
// WORKING DEFAULT (open design question 8): only an entry of the locked list can be an acceptance basis.
public sealed class AcceptanceBasis
{
    public AcceptanceBasis(string decisionTypeKey, EngineAcceptanceCondition condition, string? standardChangeModelKey = null, string? approvedByDecisionId = null)
    {
        DecisionTypeKey = decisionTypeKey;
        Condition = condition;
        StandardChangeModelKey = standardChangeModelKey;
        ApprovedByDecisionId = approvedByDecisionId;
    }

    // The decision type of the locked list entry used.
    public string DecisionTypeKey { get; }

    // lowest-tier or standard-model.
    public EngineAcceptanceCondition Condition { get; }

    // For a standard change: the qualifying model.
    public string? StandardChangeModelKey { get; }

    // For a standard change: the decision that approved the model, which a person accepted once.
    public string? ApprovedByDecisionId { get; }

    public JsonTreeNode ToJson()
    {
        return new WireWriter()
            .Add("decisionTypeKey", DecisionTypeKey)
            .Add("condition", EngineContract.EngineAcceptanceConditions.ToWire(Condition))
            .Add("standardChangeModelKey", StandardChangeModelKey)
            .Add("approvedByDecisionId", ApprovedByDecisionId)
            .Build();
    }
}

// OP-02 output (document 25, section 5.4). The rule group trace travels in the result's trace.
public sealed class ApprovalScopeResult : IOperationPayload
{
    public ApprovalScopeResult(IEnumerable<RequiredApproval> requirements, IEnumerable<RequiredApproval> consultations, AcceptanceBasis? acceptanceBasis)
    {
        Requirements = new ReadOnlyCollection<RequiredApproval>(requirements.ToList());
        Consultations = new ReadOnlyCollection<RequiredApproval>(consultations.ToList());
        AcceptanceBasis = acceptanceBasis;
    }

    // The approvals that must be satisfied. Empty only for engine-accept.
    public IReadOnlyList<RequiredApproval> Requirements { get; }

    // The consultation cards to send. Their answers are stored as recommendation only.
    public IReadOnlyList<RequiredApproval> Consultations { get; }

    // For engine-accept: the locked list entry used.
    public AcceptanceBasis? AcceptanceBasis { get; }

    public JsonTreeNode ToJson()
    {
        return new WireWriter()
            .Add("requirements", WireWriter.Array(Requirements.Select(r => r.ToJson())))
            .Add("consultations", WireWriter.Array(Consultations.Select(r => r.ToJson())))
            .Add("acceptanceBasis", AcceptanceBasis?.ToJson())
            .Build();
    }
}

// OP-02 Resolve required approvals (document 25, section 5.4; document 21, section 10.3; working default, 28 issues 1
// and 2). The single most specific approval rule group wins and broader groups drop out. With no counting group the
// decision goes on one card to the holder of the accountable letter (C31). The engine accepts with no card only for
// an entry of the locked list, never through a count. There is no "none required" outcome.
public static class RequiredApprovalsOperation
{
    private const string ApprovalRuleEntity = "approval-rule";
    private const string DecisionRightEntity = "decision-right";

    public static EvaluationResult<ApprovalScopeResult?> Evaluate(ConfigurationSnapshot snapshot, EvaluationContext context, ApprovalScopeRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return OperationShell.Run(context, snapshot, needsSnapshot: true, () => Answer(snapshot, request));
    }

    public static EvaluationResult<ApprovalScopeResult?> Evaluate(ConfigurationSnapshot snapshot, EvaluationContext context, JsonTreeNode input)
    {
        return OperationShell.Run(context, snapshot, needsSnapshot: true, () => Answer(snapshot, ApprovalScopeRequest.FromJson(input)));
    }

    // Resolves without the shell, for OP-03 callers that recompute requirements. Throws OperationStop.
    internal static OperationAnswer<ApprovalScopeResult> Answer(ConfigurationSnapshot snapshot, ApprovalScopeRequest request)
    {
        SnapshotRow decisionType = SnapshotRead.RequireActive(snapshot, "decision-type", request.DecisionTypeKey, "input.decisionTypeKey");
        CheckScopeKey(snapshot, "tier", request.TierKey, "input.tierKey");
        CheckScopeKey(snapshot, "lifecycle-stage", request.LifecycleStageKey, "input.lifecycleStageKey");
        SnapshotRow? changeType = CheckScopeKey(snapshot, "change-type", request.ChangeTypeKey, "input.changeTypeKey");
        CheckScopeKey(snapshot, "application-kind", request.ApplicationKindKey, "input.applicationKindKey");
        CheckScopeKey(snapshot, "target-stage", request.TargetStageKey, "input.targetStageKey");

        IReadOnlyList<EngineAcceptanceEntry> lockedList = LockedList(snapshot);
        string typeKey = request.DecisionTypeKey;
        var trace = new List<RuleTrace>();

        // Rule 8, standard-model. It runs before group resolution, so a standard change never falls through to the
        // broader change authorization groups.
        if (HasEntry(lockedList, typeKey, EngineAcceptanceCondition.StandardModel) && changeType != null && SnapshotRead.Flag(changeType, "requiresStandardModel"))
        {
            return StandardChange(snapshot, request, changeType, trace);
        }

        var scope = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            { "tierKey", request.TierKey },
            { "lifecycleStageKey", request.LifecycleStageKey },
            { "changeTypeKey", request.ChangeTypeKey },
            { "applicationKindKey", request.ApplicationKindKey },
            { "targetStageKey", request.TargetStageKey },
        };

        List<ScopeGroup> ruleGroups = ScopeGroups.Build(snapshot, ApprovalRuleEntity, typeKey, ScopeGroups.ApprovalRuleScope);
        ScopeResolution rules = ScopeGroups.Resolve(ruleGroups, scope, request.PortfolioPath, ApprovalRuleEntity, typeKey);
        trace.AddRange(ScopeGroups.Trace(rules, ApprovalRuleEntity));

        var requirements = new List<RequiredApproval>();
        var consultations = new List<RequiredApproval>();
        if (rules.Winner != null)
        {
            foreach (SnapshotRow row in rules.Winner.Rows)
            {
                RequiredApproval approval = FromRule(row);
                (approval.IsConsultation ? consultations : requirements).Add(approval);
            }

            // Rule 9: a winning group of a type with Requires Approval holds at least one rule that is not a consultation (V27).
            if (requirements.Count == 0 && SnapshotRead.Flag(decisionType, "requiresApproval"))
            {
                throw OperationStop.Broken(
                    ReasonCodes.ApprovalRuleConsultationOnly,
                    "The winning Approval Rule group of decision type " + SnapshotRead.Quote(typeKey) + " (" + rules.Winner.Describe()
                    + ") holds only consultations, and the type requires approval. The configuration is broken.",
                    "Approval Rule",
                    rules.Winner.Rows[0].KeyPath);
            }
        }

        if (requirements.Count > 0)
        {
            return new OperationAnswer<ApprovalScopeResult>("required", new ApprovalScopeResult(Sorted(requirements), Sorted(consultations), null), null, trace);
        }

        // Rule 8, lowest-tier: only when no counting approval rule group matches.
        if (HasEntry(lockedList, typeKey, EngineAcceptanceCondition.LowestTier) && IsLowestTier(snapshot, request.TierKey))
        {
            trace.Add(new RuleTrace("Setting", EngineKeys.EngineAcceptableDecisionTypes, RuleTraceRole.Matched));
            trace.Add(new RuleTrace("Tier", request.TierKey!, RuleTraceRole.Matched));
            var basis = new AcceptanceBasis(typeKey, EngineAcceptanceCondition.LowestTier);
            return new OperationAnswer<ApprovalScopeResult>(
                "engine-accept",
                new ApprovalScopeResult(Array.Empty<RequiredApproval>(), Sorted(consultations), basis),
                new[] { ReasonCatalog.Create(ReasonCodes.DecisionEngineAccepted, "Decision Type", typeKey) },
                trace);
        }

        // Rule 7: one card to the holder of the accountable letter, resolved with the same rule.
        requirements.Add(Accountable(snapshot, request, scope, trace));
        return new OperationAnswer<ApprovalScopeResult>("required", new ApprovalScopeResult(requirements, Sorted(consultations), null), null, trace);
    }

    private static SnapshotRow? CheckScopeKey(ConfigurationSnapshot snapshot, string entityKey, string? key, string field)
    {
        return key == null ? null : SnapshotRead.RequireExisting(snapshot, entityKey, key, field);
    }

    // The locked setting engine-acceptable-decision-types. V33 keeps it equal to the shipped list. A snapshot whose
    // setting differs is broken: the engine honors only the list it ships, and never a longer one.
    internal static IReadOnlyList<EngineAcceptanceEntry> LockedList(ConfigurationSnapshot snapshot)
    {
        JsonTreeNode value = SnapshotRead.RequireSetting(
            snapshot,
            EngineKeys.EngineAcceptableDecisionTypes,
            node => node.Kind == JsonTreeKind.Array,
            "a list of decision types with an engine acceptance condition");

        var found = new List<string>();
        foreach (JsonTreeNode item in value.Items)
        {
            string? type = item.TryGetMember("decisionTypeKey", out JsonTreeNode? t) && t.Kind == JsonTreeKind.String ? t.GetString() : null;
            string? condition = item.TryGetMember("condition", out JsonTreeNode? c) && c.Kind == JsonTreeKind.String ? c.GetString() : null;
            found.Add((type ?? "?") + " with " + (condition ?? "?"));
        }

        string[] shipped = EngineAcceptance.ShippedEntries.Select(entry => entry.DecisionTypeKey + " with " + entry.ConditionWire).ToArray();
        if (found.Count != shipped.Length || shipped.Any(entry => !found.Contains(entry, StringComparer.Ordinal)))
        {
            throw OperationStop.Broken(
                ReasonCodes.SettingLockedChanged,
                "The locked setting " + SnapshotRead.Quote(EngineKeys.EngineAcceptableDecisionTypes) + " holds " + (found.Count == 0 ? "no entries" : string.Join(", ", found))
                + " instead of the shipped list, so the configuration is broken.",
                "Setting",
                EngineKeys.EngineAcceptableDecisionTypes);
        }

        return EngineAcceptance.ShippedEntries;
    }

    private static bool HasEntry(IReadOnlyList<EngineAcceptanceEntry> list, string decisionTypeKey, EngineAcceptanceCondition condition)
    {
        return list.Any(entry => entry.Condition == condition && string.Equals(entry.DecisionTypeKey, decisionTypeKey, StringComparison.Ordinal));
    }

    // The subject's tier has the lowest active rank.
    private static bool IsLowestTier(ConfigurationSnapshot snapshot, string? tierKey)
    {
        SnapshotRow? tier = SnapshotRead.FindActive(snapshot, "tier", tierKey);
        if (tier == null)
        {
            return false;
        }

        int? rank = SnapshotRead.Int(tier, "tier", "rank");
        int?[] ranks = SnapshotRead.ActiveRows(snapshot, "tier").Select(row => SnapshotRead.Int(row, "tier", "rank")).ToArray();
        return rank.HasValue && ranks.All(other => other.HasValue && other.Value >= rank.Value);
    }

    private static OperationAnswer<ApprovalScopeResult> StandardChange(ConfigurationSnapshot snapshot, ApprovalScopeRequest request, SnapshotRow changeType, List<RuleTrace> trace)
    {
        string changeTypeKey = changeType.KeyPath;
        StandardChangeFact fact = request.StandardChange
            ?? throw OperationStop.Input(
                ReasonCodes.InputRequired,
                "input.standardChange is required: change type " + SnapshotRead.Quote(changeTypeKey) + " requires a standard change model, so OP-12 must answer first.");

        trace.Add(new RuleTrace("Change Type", changeTypeKey, RuleTraceRole.Matched));
        SnapshotRow? model = fact.Qualifies ? SnapshotRead.FindActive(snapshot, "standard-change-model", fact.ModelKey) : null;
        bool fits = model != null
            && fact.ApprovedByDecisionId != null
            && string.Equals(SnapshotRead.Text(model, "changeTypeKey"), changeTypeKey, StringComparison.Ordinal);
        if (!fits)
        {
            string why = !fact.Qualifies
                ? "fits no approved standard change model"
                : "names model " + SnapshotRead.Quote(fact.ModelKey ?? "?") + ", which is not an active model of this change type with an approving decision";
            throw OperationStop.Refuse(
                ReasonCodes.StandardModelDoesNotQualify,
                "The change of type " + SnapshotRead.Quote(changeTypeKey) + " " + why + ", so it is not a standard change and needs another change type.",
                "Change Type",
                changeTypeKey);
        }

        trace.Add(new RuleTrace("Setting", EngineKeys.EngineAcceptableDecisionTypes, RuleTraceRole.Matched));
        trace.Add(new RuleTrace("Standard Change Model", model!.KeyPath, RuleTraceRole.Won));
        var basis = new AcceptanceBasis(request.DecisionTypeKey, EngineAcceptanceCondition.StandardModel, model.KeyPath, fact.ApprovedByDecisionId);
        return new OperationAnswer<ApprovalScopeResult>(
            "engine-accept",
            new ApprovalScopeResult(Array.Empty<RequiredApproval>(), Array.Empty<RequiredApproval>(), basis),
            new[] { ReasonCatalog.Create(ReasonCodes.DecisionEngineAccepted, "Decision Type", request.DecisionTypeKey) },
            trace);
    }

    private static RequiredApproval FromRule(SnapshotRow row)
    {
        string? roleKey = SnapshotRead.Text(row, "approverRoleKey");
        string? ownerRoleKey = SnapshotRead.Text(row, "approverOwnerRoleKey");
        if ((roleKey == null) == (ownerRoleKey == null))
        {
            throw OperationStop.Broken(
                ReasonCodes.RefExactlyOne,
                "Approval Rule " + SnapshotRead.Quote(row.KeyPath) + " must name exactly one of approverRoleKey and approverOwnerRoleKey, so the configuration is broken.",
                "Approval Rule",
                row.KeyPath);
        }

        int minimumCount = SnapshotRead.Int(row, ApprovalRuleEntity, "minimumCount") ?? 1;
        if (minimumCount < 1)
        {
            throw OperationStop.Broken(
                ReasonCodes.ApprovalRuleZeroCount,
                "Approval Rule " + SnapshotRead.Quote(row.KeyPath) + " has a minimum count of " + minimumCount + ", and there is no zero waiver. The configuration is broken.",
                "Approval Rule",
                row.KeyPath);
        }

        string? modeText = SnapshotRead.Text(row, "approvalMode");
        if (!EngineContract.ApprovalModes.TryParse(modeText, out ApprovalMode mode))
        {
            throw OperationStop.Broken(
                ReasonCodes.ShapeSchema,
                "Approval Rule " + SnapshotRead.Quote(row.KeyPath) + " has no known approval mode, so the configuration is broken.",
                "Approval Rule",
                row.KeyPath);
        }

        return new RequiredApproval(
            ApprovalRequirementSource.ApprovalRule,
            row.KeyPath,
            null,
            roleKey,
            ownerRoleKey,
            SnapshotRead.Flag(row, "isConsultation"),
            mode,
            SnapshotRead.Int(row, ApprovalRuleEntity, "sequence"),
            minimumCount,
            SnapshotRead.Int(row, ApprovalRuleEntity, "timeoutDays"),
            SnapshotRead.Flag(row, "requiresTypedReason"),
            SnapshotRead.Text(row, "guidelineKey"));
    }

    // Rule 7. The accountable holder's card is answered first to respond, by one person (Minimum Count 1).
    private static RequiredApproval Accountable(ConfigurationSnapshot snapshot, ApprovalScopeRequest request, Dictionary<string, string?> scope, List<RuleTrace> trace)
    {
        string typeKey = request.DecisionTypeKey;
        List<ScopeGroup> groups = ScopeGroups.Build(snapshot, DecisionRightEntity, typeKey, ScopeGroups.DecisionRightScope);
        ScopeResolution rights = ScopeGroups.Resolve(groups, scope, request.PortfolioPath, DecisionRightEntity, typeKey);
        if (rights.Winner == null)
        {
            throw OperationStop.Broken(
                ReasonCodes.DecisionRightAccountable,
                "No Decision Right group of decision type " + SnapshotRead.Quote(typeKey) + " matches this scope, so the decision has no accountable holder. The configuration is broken.",
                "Decision Right",
                typeKey);
        }

        string accountable = EngineContract.DecisionRightLetters.ToWire(DecisionRightLetter.Accountable);
        SnapshotRow[] holders = rights.Winner.Rows.Where(row => string.Equals(SnapshotRead.Text(row, "letter"), accountable, StringComparison.Ordinal)).ToArray();
        if (holders.Length != 1)
        {
            throw OperationStop.Broken(
                ReasonCodes.DecisionRightAccountable,
                "The winning Decision Right group of decision type " + SnapshotRead.Quote(typeKey) + " (" + rights.Winner.Describe() + ") holds "
                + holders.Length + " accountable rows instead of exactly one, so the configuration is broken.",
                "Decision Right",
                rights.Winner.Rows[0].KeyPath);
        }

        SnapshotRow holder = holders[0];
        string? roleKey = SnapshotRead.Text(holder, "roleKey");
        string? ownerRoleKey = SnapshotRead.Text(holder, "ownerRoleKey");
        if ((roleKey == null) == (ownerRoleKey == null))
        {
            throw OperationStop.Broken(
                ReasonCodes.RefExactlyOne,
                "Decision Right " + SnapshotRead.Quote(holder.KeyPath) + " must name exactly one of roleKey and ownerRoleKey, so the configuration is broken.",
                "Decision Right",
                holder.KeyPath);
        }

        trace.Add(new RuleTrace("Decision Right", holder.KeyPath, RuleTraceRole.Won));
        foreach (ScopeGroup dropped in rights.Dropped)
        {
            foreach (SnapshotRow row in dropped.Rows.Where(row => string.Equals(SnapshotRead.Text(row, "letter"), accountable, StringComparison.Ordinal)))
            {
                trace.Add(new RuleTrace("Decision Right", row.KeyPath, RuleTraceRole.Dropped));
            }
        }

        return new RequiredApproval(
            ApprovalRequirementSource.Accountable,
            null,
            holder.KeyPath,
            roleKey,
            ownerRoleKey,
            false,
            ApprovalMode.FirstToRespond,
            null,
            1,
            null,
            false,
            null);
    }

    private static List<RequiredApproval> Sorted(IEnumerable<RequiredApproval> approvals)
    {
        return approvals.OrderBy(a => a.Sequence ?? 0).ThenBy(a => a.RequirementKey, StringComparer.Ordinal).ToList();
    }
}
