using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;
using Product.Engine.Core.Snapshots;

namespace Product.Engine.Core.Operations;

// A decision about a data use, a copy mapping or a retention, as the adapter passes it (a proposal of this build). The
// judged values are what the decision was decided under: the classification rank and the governing rule the runtime
// computes from the configuration version the decision stamped (docs/architecture/data-taxonomy.md, The decision state, re-decide).
public sealed class DataDecisionFact
{
    internal static readonly string[] Members =
    {
        "decisionId", "decisionTypeKey", "subjectKind", "subjectId", "statusMeaning", "outcomeKey", "decidedOn", "judgedClassificationRank", "judgedRule", "approvalsGiven",
    };

    public DataDecisionFact(string decisionId, string decisionTypeKey, string? subjectKind, string? subjectId, DecisionStatusMeaning status, string? outcomeKey, string? decidedOn, int? judgedClassificationRank, DataHandlingRule? judgedRule, int? approvalsGiven)
    {
        DecisionId = decisionId;
        DecisionTypeKey = decisionTypeKey;
        SubjectKind = subjectKind;
        SubjectId = subjectId;
        Status = status;
        OutcomeKey = outcomeKey;
        DecidedOn = decidedOn;
        JudgedClassificationRank = judgedClassificationRank;
        JudgedRule = judgedRule;
        ApprovalsGiven = approvalsGiven;
    }

    public string DecisionId { get; }

    public string DecisionTypeKey { get; }

    public string? SubjectKind { get; }

    public string? SubjectId { get; }

    public DecisionStatusMeaning Status { get; }

    public string? OutcomeKey { get; }

    public string? DecidedOn { get; }

    public int? JudgedClassificationRank { get; }

    public DataHandlingRule? JudgedRule { get; }

    // For a proposed decision: the approvals that count so far (OP-03), for "1 of 2 approvals".
    public int? ApprovalsGiven { get; }

    internal static DataDecisionFact FromWire(WireObject wire)
    {
        return new DataDecisionFact(
            wire.RequiredString("decisionId"),
            wire.RequiredKey("decisionTypeKey"),
            wire.OptionalKey("subjectKind"),
            wire.OptionalString("subjectId"),
            wire.Contract("statusMeaning", EngineContract.DecisionStatusMeanings),
            wire.OptionalKey("outcomeKey"),
            wire.OptionalString("decidedOn"),
            wire.OptionalInt("judgedClassificationRank"),
            wire.Has("judgedRule") ? wire.Contract("judgedRule", EngineContract.DataHandlingRules) : null,
            wire.OptionalInt("approvalsGiven"));
    }
}

// What a data use or a copy mapping is, as OP-16 judges it.
public sealed class DataSubject
{
    public DataSubject(string subjectKind, string domainKey, bool reachesAi, bool isDeidentified, DataOrigin? origin, string? locationId, string? locationStageKey, string? applicationId, ApprovalScopeRequest? appScope)
    {
        SubjectKind = subjectKind;
        DomainKey = domainKey;
        ReachesAi = reachesAi;
        IsDeidentified = isDeidentified;
        Origin = origin;
        LocationId = locationId;
        LocationStageKey = locationStageKey;
        ApplicationId = applicationId;
        AppScope = appScope;
    }

    // data-use or data-mapping.
    public string SubjectKind { get; }

    public string DomainKey { get; }

    public bool ReachesAi { get; }

    public bool IsDeidentified { get; }

    // For a mapping: its data origin.
    public DataOrigin? Origin { get; }

    public string? LocationId { get; }

    // The effective target stage of the location (a child takes its parent's).
    public string? LocationStageKey { get; }

    public string? ApplicationId { get; }

    // The application's scope for OP-02 (tier, stage, kind, portfolio path). Null for a mapping.
    public ApprovalScopeRequest? AppScope { get; }

    public bool IsMapping => SubjectKind == EngineKeys.DataMappingSubjectKind;
}

// One approver OP-16 resolved: a product role, or an owner role with the person who answers for it now.
public sealed class DataApprover
{
    internal DataApprover(string requirementKey, string? roleKey, string? ownerRoleKey, OwnerSubject? ownerSubject, string? principalId, string? inheritedFromKey, bool isConsultation, bool noOneToAsk, int minimumCount)
    {
        RequirementKey = requirementKey;
        RoleKey = roleKey;
        OwnerRoleKey = ownerRoleKey;
        OwnerSubject = ownerSubject;
        PrincipalId = principalId;
        InheritedFromKey = inheritedFromKey;
        IsConsultation = isConsultation;
        NoOneToAsk = noOneToAsk;
        MinimumCount = minimumCount;
    }

    public string RequirementKey { get; }

    public string? RoleKey { get; }

    public string? OwnerRoleKey { get; }

    public OwnerSubject? OwnerSubject { get; }

    // The person asked by name for an owner role, or null for a role (its group's members answer).
    public string? PrincipalId { get; }

    public string? InheritedFromKey { get; }

    public bool IsConsultation { get; }

    public bool NoOneToAsk { get; }

    public int MinimumCount { get; }

    internal JsonTreeNode ToJson()
    {
        return new WireWriter()
            .Add("requirementKey", RequirementKey)
            .Add("roleKey", RoleKey)
            .Add("ownerRoleKey", OwnerRoleKey)
            .Add("ownerSubject", OwnerSubject.HasValue ? EngineContract.OwnerSubjects.ToWire(OwnerSubject.Value) : null)
            .Add("principalId", PrincipalId)
            .Add("inheritedFrom", InheritedFromKey)
            .Add("isConsultation", IsConsultation)
            .Add("noOneToAsk", NoOneToAsk)
            .Build();
    }
}

// The OP-16 answer for one use or mapping (docs/architecture/data-taxonomy.md, The decision state). The state is computed, never stored.
public sealed class DataUseEvaluation : IOperationPayload
{
    internal DataUseEvaluation(DataSubject subject, string path, EffectiveClassification? classification, EffectiveDataRule? governingRule, DataUseState state, bool decisionNeeded, IReadOnlyList<DataApprover> approvers, DataDecisionFact? decision, (string? QuestionKey, string? AnswerKey) impliedAnswer)
    {
        Subject = subject;
        Path = path;
        Classification = classification;
        GoverningRule = governingRule;
        State = state;
        DecisionNeeded = decisionNeeded;
        Approvers = approvers;
        Decision = decision;
        ImpliedAnswer = impliedAnswer;
    }

    public DataSubject Subject { get; }

    public string Path { get; }

    public EffectiveClassification? Classification { get; }

    // Null for a mapping no rule governs (not a copy, or a copy in production).
    public EffectiveDataRule? GoverningRule { get; }

    public DataUseState State { get; }

    public bool DecisionNeeded { get; }

    public IReadOnlyList<DataApprover> Approvers { get; }

    // The decision the state comes from: the accepted one, else the proposed one.
    public DataDecisionFact? Decision { get; }

    public (string? QuestionKey, string? AnswerKey) ImpliedAnswer { get; }

    public int ApprovalsNeeded => Approvers.Where(a => !a.IsConsultation).Sum(a => a.MinimumCount);

    public bool NoOneToAsk => Approvers.Any(a => !a.IsConsultation && a.NoOneToAsk);

    // The data owner who answers for the domain now, for "my work" and the principal scope of OP-17.
    public string? DataOwnerPrincipalId { get; internal set; }

    public JsonTreeNode ToJson()
    {
        return new WireWriter()
            .Add("state", EngineContract.DataUseStates.ToWire(State))
            .Add("subjectKind", Subject.SubjectKind)
            .Add("domainKey", Subject.DomainKey)
            .Add("path", Path)
            .Add("classification", Classification?.ToJson())
            .Add("governingRule", GoverningRule?.ToJson())
            .Add("decisionTypeKey", EngineKeys.DataUseDecisionType)
            .Add("decisionNeeded", DecisionNeeded)
            .Add("approvers", WireWriter.Array(Approvers.Select(a => a.ToJson())))
            .Add("approvalsNeeded", ApprovalsNeeded)
            .Add("approvalsGiven", Decision != null && Decision.Status == DecisionStatusMeaning.Proposed ? Decision.ApprovalsGiven ?? 0 : (int?)null)
            .Add("decisionId", Decision?.DecisionId)
            .Add("impliedAnswer", ImpliedAnswer.AnswerKey == null ? null : new WireWriter().Add("questionKey", ImpliedAnswer.QuestionKey).Add("answerKey", ImpliedAnswer.AnswerKey).Build())
            .Build();
    }
}

// The rule OP-16 applies, shared with OP-17 and the runtime (docs/architecture/data-taxonomy.md, The decision state; the choices follow D-41).
// Precedence: breaks rule, then an accepted decision (allowed, denied, re-decide), then no one to ask, then proposed,
// then needed. A rule of allowed needs no decision. "No one to ask" means an owner role that counts resolves to nobody
// who can act; a product role is answered by its Entra group, which the engine cannot see, so V37 warns about an unbound
// role instead.
public static class DataUseRules
{
    public static DataUseEvaluation Evaluate(
        DataTree tree,
        DataSubject subject,
        IReadOnlyList<DataOwnerFact> owners,
        Func<string, DataPrincipalFact?> principal,
        IEnumerable<DataDecisionFact> decisions,
        string day,
        List<Reason>? reasons = null)
    {
        string domainKey = subject.DomainKey;
        EffectiveClassification? classification = tree.Classification(domainKey);
        EffectiveDataRule? rule;
        bool deidentified;
        if (subject.IsMapping)
        {
            bool copy = (subject.Origin == DataOrigin.ProductionCopy || subject.Origin == DataOrigin.MaskedCopy) && !tree.IsProductionStage(subject.LocationStageKey);
            rule = copy ? tree.Rule(domainKey, DataRuleName.Copy) : null;
            deidentified = subject.Origin == DataOrigin.MaskedCopy;
        }
        else
        {
            rule = tree.GoverningRuleForUse(domainKey, subject.ReachesAi);
            deidentified = subject.IsDeidentified;
        }

        bool breaks = rule != null && (rule.Value == DataHandlingRule.Never || (rule.Value == DataHandlingRule.MaskedOnly && !deidentified));
        bool needed = rule != null && !breaks && (rule.Value == DataHandlingRule.Decision || rule.Value == DataHandlingRule.MaskedOnly);
        IReadOnlyList<DataApprover> approvers = needed ? Approvers(tree, subject, classification, owners, principal, day, reasons) : Array.Empty<DataApprover>();

        List<DataDecisionFact> mine = decisions.Where(d => d.DecisionTypeKey == EngineKeys.DataUseDecisionType).ToList();
        DataDecisionFact? accepted = mine.Where(d => d.Status == DecisionStatusMeaning.Accepted)
            .OrderBy(d => d.DecidedOn ?? string.Empty, StringComparer.Ordinal).ThenBy(d => d.DecisionId, StringComparer.Ordinal).LastOrDefault();
        DataDecisionFact? proposed = mine.Where(d => d.Status == DecisionStatusMeaning.Proposed).OrderBy(d => d.DecisionId, StringComparer.Ordinal).FirstOrDefault();

        DataUseState state;
        DataDecisionFact? from = null;
        if (breaks)
        {
            state = DataUseState.BreaksRule;
        }
        else if (!needed)
        {
            state = DataUseState.NotNeeded;
        }
        else if (accepted != null)
        {
            from = accepted;
            if (accepted.OutcomeKey != EngineKeys.DataUseAllowOutcome)
            {
                state = DataUseState.Denied;
            }
            else
            {
                bool tighter = (accepted.JudgedClassificationRank.HasValue && classification != null && classification.Rank > accepted.JudgedClassificationRank.Value)
                    || (accepted.JudgedRule.HasValue && rule!.Value > accepted.JudgedRule.Value);
                state = tighter ? DataUseState.ReDecide : DataUseState.Allowed;
            }
        }
        else if (approvers.Any(a => !a.IsConsultation && a.NoOneToAsk))
        {
            state = DataUseState.NoOneToAsk;
            from = proposed;
        }
        else if (proposed != null)
        {
            state = DataUseState.Proposed;
            from = proposed;
        }
        else
        {
            state = DataUseState.Needed;
        }

        var result = new DataUseEvaluation(subject, tree.PathText(domainKey), classification, rule, state, needed, approvers, from, tree.ImpliedAnswer(domainKey));
        if (tree.Snapshot != null)
        {
            SnapshotRow? dataOwner = SnapshotRead.FindActive(tree.Snapshot, "owner-role", EngineKeys.DataOwnerRole);
            if (dataOwner != null && DataTree.CanHold(dataOwner, OwnerSubjectKind.DataDomain))
            {
                result.DataOwnerPrincipalId = tree.ResolveOnDomain(EngineKeys.DataOwnerRole, domainKey, owners, principal, day).ActingPrincipalId;
            }
        }

        return result;
    }

    // OP-02 for a data-use decision about this subject, then each owner role resolved through OP-15.
    private static IReadOnlyList<DataApprover> Approvers(DataTree tree, DataSubject subject, EffectiveClassification? classification, IReadOnlyList<DataOwnerFact> owners, Func<string, DataPrincipalFact?> principal, string day, List<Reason>? reasons)
    {
        ApprovalScopeRequest app = subject.AppScope ?? new ApprovalScopeRequest(EngineKeys.DataUseDecisionType);
        var request = new ApprovalScopeRequest(
            EngineKeys.DataUseDecisionType,
            app.TierKey,
            app.LifecycleStageKey,
            null,
            app.ApplicationKindKey,
            null,
            app.PortfolioPath,
            subjectKind: subject.SubjectKind,
            classificationKey: classification == null || SnapshotRead.FindActive(tree.Snapshot, "data-classification", classification.Key) == null ? null : classification.Key,
            aiUse: subject.IsMapping ? null : subject.ReachesAi);
        OperationAnswer<ApprovalScopeResult> answer = RequiredApprovalsOperation.Answer(tree.Snapshot, request);
        var approvers = new List<DataApprover>();
        foreach (RequiredApproval requirement in answer.Payload.Requirements.Concat(answer.Payload.Consultations))
        {
            if (requirement.ApproverProposedOwners)
            {
                continue;
            }

            if (requirement.ApproverOwnerRoleKey == null)
            {
                approvers.Add(new DataApprover(requirement.RequirementKey, requirement.ApproverRoleKey, null, null, null, null, requirement.IsConsultation, false, requirement.MinimumCount));
                continue;
            }

            OwnerResolution resolution = Resolve(tree, requirement.ApproverOwnerRoleKey, requirement.OwnerSubject, subject, owners, principal, day);
            if (resolution.ActingPrincipalId == null && requirement.FallbackOwnerRoleKey != null)
            {
                resolution = Resolve(tree, requirement.FallbackOwnerRoleKey, requirement.OwnerSubject, subject, owners, principal, day);
            }

            bool nobody = resolution.ActingPrincipalId == null;
            if (nobody && requirement.IsConsultation)
            {
                reasons?.Add(new Reason(
                    ReasonCodes.DataNoOneToAsk,
                    ReasonSeverity.Info,
                    "The consultation of the " + DataWords.Key(requirement.ApproverOwnerRoleKey) + " on " + tree.PathText(subject.DomainKey) + " resolves to nobody, so it is skipped.",
                    "Approval Rule",
                    requirement.RequirementKey));
                continue;
            }

            approvers.Add(new DataApprover(
                requirement.RequirementKey,
                null,
                resolution.ActingPrincipalId == null ? requirement.ApproverOwnerRoleKey : resolution.OwnerRoleKey,
                requirement.OwnerSubject,
                resolution.ActingPrincipalId,
                resolution.IsInherited ? resolution.SourceKey : null,
                requirement.IsConsultation,
                nobody,
                requirement.MinimumCount));
        }

        return approvers;
    }

    private static OwnerResolution Resolve(DataTree tree, string ownerRoleKey, OwnerSubject? where, DataSubject subject, IReadOnlyList<DataOwnerFact> owners, Func<string, DataPrincipalFact?> principal, string day)
    {
        switch (where ?? OwnerSubject.BusinessApplication)
        {
            case OwnerSubject.DataDomain:
            case OwnerSubject.TouchedDataDomains:
                return tree.ResolveOnDomain(ownerRoleKey, subject.DomainKey, owners, principal, day);
            case OwnerSubject.ParentDataDomain:
                string? parent = tree.ParentKey(subject.DomainKey);
                return parent == null
                    ? new OwnerResolution(ownerRoleKey, false, false, Array.Empty<DataOwnerFact>(), Array.Empty<DataOwnerFact>(), null, 0, null)
                    : tree.ResolveOnDomain(ownerRoleKey, parent, owners, principal, day);
            case OwnerSubject.DataLocation:
                return subject.LocationId == null
                    ? new OwnerResolution(ownerRoleKey, false, false, Array.Empty<DataOwnerFact>(), Array.Empty<DataOwnerFact>(), null, 0, null)
                    : tree.ResolveOnLocation(ownerRoleKey, subject.LocationId, owners, principal, day);
            default:
                return tree.ResolveOnApplication(ownerRoleKey, subject.ApplicationId, owners, principal, day);
        }
    }
}

// Shared wire readers of the data facts.
internal static class DataFacts
{
    public static IReadOnlyList<DataOwnerFact> Owners(WireObject wire)
    {
        return wire.Objects("ownerAssignments", false, DataOwnerFact.Members).Select(DataOwnerFact.FromWire).ToList();
    }

    public static Func<string, DataPrincipalFact?> Principals(WireObject wire)
    {
        var map = new Dictionary<string, DataPrincipalFact>(StringComparer.OrdinalIgnoreCase);
        foreach (WireObject item in wire.Objects("principals", false, DataPrincipalFact.Members))
        {
            DataPrincipalFact fact = DataPrincipalFact.FromWire(item);
            map[fact.PrincipalId] = fact;
        }

        return id => map.TryGetValue(id, out DataPrincipalFact? found) ? found : null;
    }

    public static IReadOnlyList<DataDecisionFact> Decisions(WireObject wire)
    {
        return wire.Objects("decisions", false, DataDecisionFact.Members).Select(DataDecisionFact.FromWire).ToList();
    }
}

// OP-15 Resolve data domain (docs/architecture/data-taxonomy.md, Who answers, Effective values on a domain and Engine): the path, level, effective classification and
// rules with their source, and the holder and deputies of each owner role held on domains, with "inherited from",
// directory state and the gaps DC-01 to DC-04 for this domain. A read: outcome resolved.
public sealed class DataDomainResult : IOperationPayload
{
    private readonly JsonTreeNode _json;

    internal DataDomainResult(JsonTreeNode json, IReadOnlyList<string> gaps)
    {
        _json = json;
        Gaps = gaps;
    }

    public IReadOnlyList<string> Gaps { get; }

    public JsonTreeNode ToJson()
    {
        return _json;
    }
}

public static class DataDomainOperation
{
    public const string Resolved = "resolved";

    public static EvaluationResult<DataDomainResult?> Evaluate(ConfigurationSnapshot snapshot, EvaluationContext context, JsonTreeNode input)
    {
        return OperationShell.Run(context, snapshot, needsSnapshot: true, () => Answer(snapshot, context, input));
    }

    private static OperationAnswer<DataDomainResult> Answer(ConfigurationSnapshot snapshot, EvaluationContext context, JsonTreeNode input)
    {
        var wire = new WireObject(input, "input", "domainKey", "ownerAssignments", "principals");
        string key = wire.RequiredKey("domainKey");
        var tree = new DataTree(snapshot);
        SnapshotRow domain = tree.RequireDomain(key, "input.domainKey");
        IReadOnlyList<DataOwnerFact> owners = DataFacts.Owners(wire);
        Func<string, DataPrincipalFact?> principal = DataFacts.Principals(wire);
        string day = DataTree.DayOf(context.AsOfUtc);
        var reasons = new List<Reason>();
        var trace = new List<RuleTrace> { new RuleTrace("Data Domain", key, RuleTraceRole.Matched) };

        List<OwnerResolution> resolutions = tree.OwnerRolesFor(OwnerSubjectKind.DataDomain)
            .Select(role => tree.ResolveOnDomain(role.KeyPath, key, owners, principal, day))
            .ToList();
        List<string> gaps = DomainGaps(tree, key, resolutions, principal, reasons);

        SnapshotRow? level = tree.LevelOf(key);
        EffectiveClassification? classification = tree.Classification(key);
        (string? retention, string? retentionOn) = tree.Retention(key);
        (string? question, string? answer) = tree.ImpliedAnswer(key);
        JsonTreeNode json = new WireWriter()
            .Add("domainKey", key)
            .Add("label", DataTree.Label(domain))
            .Add("isActive", domain.IsActive)
            .Add("path", WireWriter.Array(tree.Chain(key).Reverse().Select(n => new WireWriter().Add("key", n.KeyPath).Add("label", DataTree.Label(n)).Add("levelKey", SnapshotRead.Text(n, "levelKey")).Build())))
            .Add("pathText", tree.PathText(key))
            .Add("levelKey", SnapshotRead.Text(domain, "levelKey"))
            .Add("levelRank", level == null ? null : SnapshotRead.Int(level, "data-domain-level", "rank"))
            .Add("declarable", tree.IsDeclarable(key))
            .Add("ownerRequired", tree.OwnerRequired(key))
            .Add("classification", classification?.ToJson())
            .Add("rules", new WireWriter()
                .Add("use", tree.Rule(key, DataRuleName.Use).ToJson())
                .Add("ai", tree.Rule(key, DataRuleName.Ai).ToJson())
                .Add("copy", tree.Rule(key, DataRuleName.Copy).ToJson())
                .Build())
            .Add("retention", retention == null ? null : new WireWriter().Add("retentionClassKey", retention).Add("setOn", retentionOn).Build())
            .Add("impliedAnswer", answer == null ? null : new WireWriter().Add("questionKey", question).Add("answerKey", answer).Build())
            .Add("owners", WireWriter.Array(resolutions.Select(r => r.ToJson(principal))))
            .Add("gaps", gaps)
            .Build();
        return new OperationAnswer<DataDomainResult>(Resolved, new DataDomainResult(json, gaps), reasons, trace);
    }

    // DC-01 to DC-04 for one domain (docs/architecture/data-taxonomy.md, Data checks).
    internal static List<string> DomainGaps(DataTree tree, string key, IReadOnlyList<OwnerResolution> resolutions, Func<string, DataPrincipalFact?> principal, List<Reason> reasons)
    {
        var gaps = new List<string>();
        string path = tree.PathText(key);
        OwnerResolution? dataOwner = resolutions.FirstOrDefault(r => r.OwnerRoleKey == EngineKeys.DataOwnerRole);
        if (dataOwner != null && dataOwner.Holder == null)
        {
            gaps.Add(ReasonCodes.DataNoOwner);
            reasons.Add(new Reason(ReasonCodes.DataNoOwner, ReasonSeverity.Warning, "DC-01: " + path + " has no data owner of its own or above it. Propose one with an ownership decision.", "Data Domain", key));
        }
        else if (dataOwner != null && dataOwner.IsInherited && tree.OwnerRequired(key))
        {
            gaps.Add(ReasonCodes.DataOwnOwnerMissing);
            reasons.Add(new Reason(ReasonCodes.DataOwnOwnerMissing, ReasonSeverity.Warning, "DC-02: the level of " + path + " needs an owner of its own, and it has only the owner of " + tree.PathText(dataOwner.SourceKey!) + ". Propose one with an ownership decision.", "Data Domain", key));
        }

        foreach (OwnerResolution r in resolutions.Where(r => r.Holder != null))
        {
            string where = r.IsInherited ? " (inherited from " + tree.PathText(r.SourceKey!) + ")" : string.Empty;
            if (r.RequiresDeputy && r.Deputies.Count == 0 && !gaps.Contains(ReasonCodes.DataNoDeputy))
            {
                gaps.Add(ReasonCodes.DataNoDeputy);
                reasons.Add(new Reason(ReasonCodes.DataNoDeputy, ReasonSeverity.Warning, "DC-03: the " + DataWords.Key(r.OwnerRoleKey) + " of " + path + where + " has no deputy, and the owner role needs one. Propose a deputy with an ownership decision.", "Data Domain", r.SourceKey));
            }

            DataOwnerFact? inactive = r.Holders.Concat(r.Deputies).FirstOrDefault(o => principal(o.PrincipalId)?.IsInactiveOrLeaving == true);
            if (inactive != null && !gaps.Contains(ReasonCodes.DataOwnerInactive))
            {
                gaps.Add(ReasonCodes.DataOwnerInactive);
                reasons.Add(new Reason(ReasonCodes.DataOwnerInactive, ReasonSeverity.Warning, "DC-04: " + inactive.PrincipalId + ", " + (inactive.IsDeputy ? "deputy " : string.Empty) + DataWords.Key(r.OwnerRoleKey) + " of " + path + where + ", is disabled, deleted or leaving. Propose a successor with an ownership decision.", "Data Domain", r.SourceKey));
            }
        }

        return gaps;
    }
}

// OP-16 Evaluate data use (docs/architecture/data-taxonomy.md, The decision state and Engine): the governing rule and its source, the state, the
// decision type and the approvers through OP-02 and OP-15, and the warnings DC-09 and DC-11. The outcome is the state.
public static class DataUseOperation
{
    public static EvaluationResult<DataUseEvaluation?> Evaluate(ConfigurationSnapshot snapshot, EvaluationContext context, JsonTreeNode input)
    {
        return OperationShell.Run(context, snapshot, needsSnapshot: true, () => Answer(snapshot, context, input));
    }

    private static OperationAnswer<DataUseEvaluation> Answer(ConfigurationSnapshot snapshot, EvaluationContext context, JsonTreeNode input)
    {
        var wire = new WireObject(
            input,
            "input",
            "subjectKind", "domainKey", "reachesAi", "isDeidentified", "origin", "locationId", "locationStageKey", "applicationId",
            "tierKey", "lifecycleStageKey", "applicationKindKey", "portfolioPath",
            "ownerAssignments", "principals", "decisions", "mappedAtLocation", "sensitivityAnswerKey");
        string subjectKind = wire.OptionalKey("subjectKind") ?? EngineKeys.DataUseSubjectKind;
        if (subjectKind != EngineKeys.DataUseSubjectKind && subjectKind != EngineKeys.DataMappingSubjectKind)
        {
            throw OperationStop.Input(ReasonCodes.InputMalformed, "input.subjectKind must be data-use or data-mapping.");
        }

        var tree = new DataTree(snapshot);
        string domainKey = wire.RequiredKey("domainKey");
        tree.RequireDomain(domainKey, "input.domainKey");
        DataOrigin? origin = wire.Has("origin") ? wire.Contract("origin", EngineContract.DataOrigins) : null;
        if (subjectKind == EngineKeys.DataMappingSubjectKind && origin == null)
        {
            throw OperationStop.Input(ReasonCodes.InputRequired, "input.origin is required for a data mapping.");
        }

        string? stage = wire.OptionalKey("locationStageKey");
        if (stage != null)
        {
            SnapshotRead.RequireExisting(snapshot, "target-stage", stage, "input.locationStageKey");
        }

        var appScope = new ApprovalScopeRequest(
            EngineKeys.DataUseDecisionType,
            wire.OptionalKey("tierKey"),
            wire.OptionalKey("lifecycleStageKey"),
            null,
            wire.OptionalKey("applicationKindKey"),
            null,
            wire.Strings("portfolioPath"));
        var subject = new DataSubject(
            subjectKind,
            domainKey,
            wire.Bool("reachesAi", false),
            wire.Bool("isDeidentified", false),
            origin,
            wire.OptionalString("locationId"),
            stage,
            wire.OptionalString("applicationId"),
            appScope);
        var reasons = new List<Reason>();
        string day = DataTree.DayOf(context.AsOfUtc);
        DataUseEvaluation evaluation = DataUseRules.Evaluate(tree, subject, DataFacts.Owners(wire), DataFacts.Principals(wire), DataFacts.Decisions(wire), day, reasons);
        var trace = new List<RuleTrace> { new RuleTrace("Data Domain", domainKey, RuleTraceRole.Matched) };
        if (evaluation.GoverningRule?.SetOnDomainKey is string setOn)
        {
            trace.Add(new RuleTrace("Data Domain", setOn, RuleTraceRole.Won));
        }
        else if (evaluation.GoverningRule?.ClassificationKey is string fromClass)
        {
            trace.Add(new RuleTrace("Data Classification", fromClass, RuleTraceRole.Won));
        }

        reasons.AddRange(StateReasons(tree, evaluation));
        bool? mapped = wire.OptionalBool("mappedAtLocation");
        if (subject.LocationId != null && mapped == false && !subject.IsMapping)
        {
            reasons.Add(new Reason(ReasonCodes.DataMappingMissing, ReasonSeverity.Info, "DC-11: " + evaluation.Path + " has no mapping at the location this use names. Map that data at the location.", "Data Use", domainKey));
        }

        string? answer = wire.OptionalKey("sensitivityAnswerKey");
        if (answer != null && Below(tree, evaluation.ImpliedAnswer, answer))
        {
            reasons.Add(new Reason(ReasonCodes.DataSensitivityBelowData, ReasonSeverity.Warning, "DC-09: the application's sensitivity answer " + DataWords.Key(answer) + " is lower than " + DataWords.Key(evaluation.ImpliedAnswer.AnswerKey!) + ", which " + evaluation.Path + " implies. Answer the tier question again.", "Business Application", subject.ApplicationId));
        }

        return new OperationAnswer<DataUseEvaluation>(EngineContract.DataUseStates.ToWire(evaluation.State), evaluation, reasons, trace);
    }

    // True when the answer ranks below the implied answer of the same question.
    internal static bool Below(DataTree tree, (string? QuestionKey, string? AnswerKey) implied, string answer)
    {
        int? need = tree.AnswerRank(implied.QuestionKey, implied.AnswerKey);
        int? have = tree.AnswerRank(implied.QuestionKey, answer);
        return need.HasValue && have.HasValue && have.Value < need.Value;
    }

    // The reason that names the state's finding (DC-05 to DC-08), in one sentence with the rule, the path and the fix.
    internal static IEnumerable<Reason> StateReasons(DataTree tree, DataUseEvaluation e)
    {
        string entity = e.Subject.IsMapping ? "Data Mapping" : "Data Use";
        string rule = e.GoverningRule == null ? string.Empty : tree.DescribeRule(e.GoverningRule, e.Subject.DomainKey);
        if (e.Subject.IsMapping && e.GoverningRule != null && e.State != DataUseState.NotNeeded && e.State != DataUseState.Allowed)
        {
            yield return new Reason(ReasonCodes.DataCopyOutsideProduction, ReasonSeverity.Warning, "DC-08: a copy of " + e.Path + " sits outside production, and " + rule + ". " + (e.State == DataUseState.BreaksRule ? "Mask or remove the copy in its own tool, then end or change the mapping." : "Propose a copy decision."), entity, e.Subject.DomainKey);
            yield break;
        }

        switch (e.State)
        {
            case DataUseState.Needed:
            case DataUseState.ReDecide:
                yield return new Reason(ReasonCodes.DataDecisionMissing, ReasonSeverity.Warning, "DC-05: " + rule + ", so this use of " + e.Path + (e.State == DataUseState.ReDecide ? " needs a new decision, because the rule or the classification tightened since it was allowed." : " needs a data-use decision. Propose one."), entity, e.Subject.DomainKey);
                break;
            case DataUseState.NoOneToAsk:
                yield return new Reason(ReasonCodes.DataNoOneToAsk, ReasonSeverity.Warning, "DC-06: this use of " + e.Path + " needs a decision, but an approver it needs resolves to nobody. Propose a data owner with an ownership decision.", entity, e.Subject.DomainKey);
                break;
            case DataUseState.BreaksRule:
                yield return new Reason(ReasonCodes.DataBreaksRule, ReasonSeverity.Warning, "DC-07: " + rule + ", so no decision can allow this use of " + e.Path + ". End the use, or change the rule where it is set.", entity, e.Subject.DomainKey);
                break;
        }
    }
}

// One finding of OP-17 (docs/architecture/data-taxonomy.md, Data checks).
public sealed class DataFinding
{
    public DataFinding(string check, string code, string severity, string subjectKind, string subjectId, string message, string? domainKey = null, string? applicationId = null, string? locationId = null, string? principalId = null, string? ownerRoleKey = null, bool? isDeputy = null)
    {
        OwnerRoleKey = ownerRoleKey;
        IsDeputy = isDeputy;
        Check = check;
        Code = code;
        Severity = severity;
        SubjectKind = subjectKind;
        SubjectId = subjectId;
        Message = message;
        DomainKey = domainKey;
        ApplicationId = applicationId;
        LocationId = locationId;
        PrincipalId = principalId;
    }

    // DC-01 to DC-14.
    public string Check { get; }

    public string Code { get; }

    // high, medium or low (the finding's weight, not the reason severity).
    public string Severity { get; }

    public string SubjectKind { get; }

    public string SubjectId { get; }

    public string Message { get; }

    public string? DomainKey { get; }

    public string? ApplicationId { get; }

    public string? LocationId { get; }

    public string? PrincipalId { get; }

    // For DC-03 and DC-04: the owner role the finding is about, and whether the person is its deputy (product review: the
    // pages parsed the role from the message and dropped "deputy").
    public string? OwnerRoleKey { get; }

    public bool? IsDeputy { get; }

    internal JsonTreeNode ToJson()
    {
        return new WireWriter()
            .Add("check", Check)
            .Add("code", Code)
            .Add("severity", Severity)
            .Add("subjectKind", SubjectKind)
            .Add("subjectId", SubjectId)
            .Add("domainKey", DomainKey)
            .Add("applicationId", ApplicationId)
            .Add("locationId", LocationId)
            .Add("principalId", PrincipalId)
            .Add("ownerRoleKey", OwnerRoleKey)
            .Add("isDeputy", IsDeputy.HasValue ? (IsDeputy.Value ? JsonTreeNode.True : JsonTreeNode.False) : null)
            .Add("message", Message)
            .Build();
    }
}

public sealed class DataChecksResult : IOperationPayload
{
    public static readonly IReadOnlyList<string> Checks = new ReadOnlyCollection<string>(Enumerable.Range(1, 14).Select(i => "DC-" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture)).ToArray());

    public DataChecksResult(IEnumerable<DataFinding> findings)
    {
        Findings = new ReadOnlyCollection<DataFinding>(findings.ToList());
    }

    public IReadOnlyList<DataFinding> Findings { get; }

    public int Count(string check) => Findings.Count(f => f.Check == check);

    public JsonTreeNode ToJson()
    {
        return new WireWriter()
            .Add("counts", JsonTreeNode.CreateObject(Checks.Select(c => new KeyValuePair<string, JsonTreeNode>(c, JsonTreeNode.FromInt64(Count(c))))))
            .Add("findings", WireWriter.Array(Findings.Select(f => f.ToJson())))
            .Build();
    }
}

// OP-17 Run data checks (docs/architecture/data-taxonomy.md, Engine and Data checks): DC-01 to DC-14 over the facts in scope. DC-15 comes later.
// The scope narrows the findings: one domain (with or without its subtree), one application, one location, one person,
// or everything. Choices of this build (D-41): a child location takes its parent's technical owner and freshness,
// so DC-12 judges top-level locations; a location never seen counts as stale; DC-08 lists a copy outside production
// until it is allowed; a finding about a use names the domain's acting data owner, for the person scope.
public static class DataChecksOperation
{
    public const string Checked = "checked";

    private static readonly string[] LocationMembers = { "locationId", "parentLocationId", "targetStageKey", "statusMeaning", "retiredOn", "lastSeenOn", "label" };
    private static readonly string[] MappingMembers = { "mappingId", "domainKey", "locationId", "origin", "statusMeaning", "endedOn" };
    private static readonly string[] UseMembers = { "useId", "recordNumber", "applicationId", "domainKey", "locationId", "access", "reachesAi", "isDeidentified", "statusMeaning", "endedOn" };
    private static readonly string[] AppMembers =
    {
        "applicationId", "label", "tierKey", "lifecycleStageKey", "applicationKindKey", "portfolioPath", "usesAi", "holdsNoBusinessData", "hasDataDependency",
        "sensitivityAnswerKey", "isRetiring", "statusMeaning",
    };

    public static EvaluationResult<DataChecksResult?> Evaluate(ConfigurationSnapshot snapshot, EvaluationContext context, JsonTreeNode input)
    {
        return OperationShell.Run(context, snapshot, needsSnapshot: true, () => Answer(snapshot, context, input));
    }

    private sealed class Location
    {
        public string Id = string.Empty;
        public string? Parent;
        public string? Stage;
        public string? Status;
        public string? RetiredOn;
        public string? LastSeenOn;
        public string? Label;
    }

    private sealed class Mapping
    {
        public string Id = string.Empty;
        public string Domain = string.Empty;
        public string Location = string.Empty;
        public DataOrigin Origin;
        public string? Status;
        public string? EndedOn;
    }

    private sealed class Use
    {
        public string Id = string.Empty;
        public string? Number;
        public string App = string.Empty;
        public string Domain = string.Empty;
        public string? Location;
        public DataAccess Access;
        public bool ReachesAi;
        public bool Deidentified;
        public string? Status;
        public string? EndedOn;
    }

    private sealed class App
    {
        public string Id = string.Empty;
        public string? Label;
        public ApprovalScopeRequest Scope = new ApprovalScopeRequest(EngineKeys.DataUseDecisionType);
        public bool UsesAi;
        public bool HoldsNoBusinessData;
        public bool HasDataDependency;
        public string? SensitivityAnswer;
        public bool IsRetiring;
        public string? Status;
    }

    private static OperationAnswer<DataChecksResult> Answer(ConfigurationSnapshot snapshot, EvaluationContext context, JsonTreeNode input)
    {
        var wire = new WireObject(input, "input", "scope", "ownerAssignments", "principals", "locations", "mappings", "uses", "applications", "decisions");
        WireObject? scope = wire.OptionalObject("scope", "domainKey", "includeSubtree", "applicationId", "locationId", "principalId");
        var tree = new DataTree(snapshot);
        string day = DataTree.DayOf(context.AsOfUtc);
        IReadOnlyList<DataOwnerFact> owners = DataFacts.Owners(wire);
        Func<string, DataPrincipalFact?> principal = DataFacts.Principals(wire);
        IReadOnlyList<DataDecisionFact> decisions = DataFacts.Decisions(wire);

        List<Location> locations = wire.Objects("locations", false, LocationMembers).Select(w => new Location
        {
            Id = w.RequiredString("locationId"), Parent = w.OptionalString("parentLocationId"), Stage = w.OptionalKey("targetStageKey"), Status = w.OptionalString("statusMeaning"),
            RetiredOn = w.OptionalString("retiredOn"), LastSeenOn = w.OptionalString("lastSeenOn"), Label = w.OptionalString("label"),
        }).ToList();
        List<Mapping> mappings = wire.Objects("mappings", false, MappingMembers).Select(w => new Mapping
        {
            Id = w.RequiredString("mappingId"), Domain = w.RequiredKey("domainKey"), Location = w.RequiredString("locationId"), Origin = w.Contract("origin", EngineContract.DataOrigins),
            Status = w.OptionalString("statusMeaning"), EndedOn = w.OptionalString("endedOn"),
        }).ToList();
        List<Use> uses = wire.Objects("uses", false, UseMembers).Select(w => new Use
        {
            Id = w.RequiredString("useId"), Number = w.OptionalString("recordNumber"), App = w.RequiredString("applicationId"), Domain = w.RequiredKey("domainKey"), Location = w.OptionalString("locationId"),
            Access = w.Contract("access", EngineContract.DataAccesses), ReachesAi = w.Bool("reachesAi", false), Deidentified = w.Bool("isDeidentified", false),
            Status = w.OptionalString("statusMeaning"), EndedOn = w.OptionalString("endedOn"),
        }).ToList();
        List<App> apps = wire.Objects("applications", false, AppMembers).Select(w => new App
        {
            Id = w.RequiredString("applicationId"), Label = w.OptionalString("label"),
            Scope = new ApprovalScopeRequest(EngineKeys.DataUseDecisionType, w.OptionalKey("tierKey"), w.OptionalKey("lifecycleStageKey"), null, w.OptionalKey("applicationKindKey"), null, w.Strings("portfolioPath")),
            UsesAi = w.Bool("usesAi", false), HoldsNoBusinessData = w.Bool("holdsNoBusinessData", false), HasDataDependency = w.Bool("hasDataDependency", false),
            SensitivityAnswer = w.OptionalKey("sensitivityAnswerKey"), IsRetiring = w.Bool("isRetiring", false), Status = w.OptionalString("statusMeaning"),
        }).ToList();

        var findings = new List<DataFinding>();
        if (tree.Mode != DataTaxonomyMode.Off)
        {
            DomainChecks(tree, owners, principal, day, findings);
            LocationChecks(tree, snapshot, locations, owners, principal, day, context.AsOfUtc, findings);
            UseChecks(tree, uses, mappings, locations, apps, owners, principal, decisions, day, findings);
            MappingChecks(tree, mappings, locations, owners, principal, decisions, day, findings);
            ApplicationChecks(tree, uses, mappings, apps, decisions, day, findings);
            RetiredDomainRows(tree, uses, mappings, owners, day, findings);
        }

        IEnumerable<DataFinding> scoped = Scope(tree, scope, findings);
        var trace = new List<RuleTrace>();
        if (tree.Mode == DataTaxonomyMode.Off)
        {
            trace.Add(new RuleTrace("Setting", EngineKeys.DataTaxonomyModeSetting, RuleTraceRole.Matched));
        }

        return new OperationAnswer<DataChecksResult>(Checked, new DataChecksResult(scoped), null, trace);
    }

    private static IEnumerable<DataFinding> Scope(DataTree tree, WireObject? scope, List<DataFinding> findings)
    {
        if (scope == null)
        {
            return findings;
        }

        string? domain = scope.OptionalKey("domainKey");
        string? app = scope.OptionalString("applicationId");
        string? location = scope.OptionalString("locationId");
        string? person = scope.OptionalString("principalId");
        IEnumerable<DataFinding> result = findings;
        if (domain != null)
        {
            tree.RequireDomain(domain, "input.scope.domainKey");
            var keys = new HashSet<string>(scope.Bool("includeSubtree", false) ? tree.Subtree(domain) : new[] { domain }, StringComparer.Ordinal);
            result = result.Where(f => f.DomainKey != null && keys.Contains(f.DomainKey));
        }

        if (app != null)
        {
            result = result.Where(f => f.ApplicationId != null && string.Equals(f.ApplicationId, app, StringComparison.OrdinalIgnoreCase));
        }

        if (location != null)
        {
            result = result.Where(f => f.LocationId != null && string.Equals(f.LocationId, location, StringComparison.OrdinalIgnoreCase));
        }

        if (person != null)
        {
            result = result.Where(f => f.PrincipalId != null && string.Equals(f.PrincipalId, person, StringComparison.OrdinalIgnoreCase));
        }

        return result.ToList();
    }

    // DC-01 to DC-04 on the active domains: a gap is reported on the node that holds the role, once.
    private static void DomainChecks(DataTree tree, IReadOnlyList<DataOwnerFact> owners, Func<string, DataPrincipalFact?> principal, string day, List<DataFinding> findings)
    {
        IReadOnlyList<SnapshotRow> roles = tree.OwnerRolesFor(OwnerSubjectKind.DataDomain);
        bool dataOwnerOnDomains = roles.Any(r => r.KeyPath == EngineKeys.DataOwnerRole);
        foreach (SnapshotRow domain in tree.ActiveDomains)
        {
            string key = domain.KeyPath;
            string path = tree.PathText(key);
            if (dataOwnerOnDomains)
            {
                OwnerResolution owner = tree.ResolveOnDomain(EngineKeys.DataOwnerRole, key, owners, principal, day);
                if (owner.Holder == null)
                {
                    findings.Add(new DataFinding("DC-01", ReasonCodes.DataNoOwner, "high", EngineKeys.DataDomainSubjectKind, key, path + " has no data owner of its own or above it. Propose one with an ownership decision.", key));
                }
                else if (owner.IsInherited && tree.OwnerRequired(key))
                {
                    findings.Add(new DataFinding("DC-02", ReasonCodes.DataOwnOwnerMissing, "medium", EngineKeys.DataDomainSubjectKind, key, "The level of " + path + " needs an owner of its own; it has only the owner of " + tree.PathText(owner.SourceKey!) + ".", key, principalId: owner.Holder.PrincipalId));
                }
            }

            foreach (SnapshotRow role in roles)
            {
                OwnerResolution r = tree.ResolveOnDomain(role.KeyPath, key, owners, principal, day);
                if (r.Holder == null || r.IsInherited)
                {
                    continue;
                }

                HolderChecks(r, path, EngineKeys.DataDomainSubjectKind, key, key, null, principal, findings);
            }
        }
    }

    // DC-03 and DC-04 for the node or location that holds the role.
    private static void HolderChecks(OwnerResolution r, string label, string subjectKind, string subjectId, string? domainKey, string? locationId, Func<string, DataPrincipalFact?> principal, List<DataFinding> findings)
    {
        if (r.RequiresDeputy && r.Deputies.Count == 0)
        {
            findings.Add(new DataFinding("DC-03", ReasonCodes.DataNoDeputy, "medium", subjectKind, subjectId, "The " + DataWords.Key(r.OwnerRoleKey) + " of " + label + " has no deputy, and the owner role needs one.", domainKey, locationId: locationId, principalId: r.Holder!.PrincipalId, ownerRoleKey: r.OwnerRoleKey, isDeputy: false));
        }

        foreach (DataOwnerFact person in r.Holders.Concat(r.Deputies).Where(o => principal(o.PrincipalId)?.IsInactiveOrLeaving == true))
        {
            findings.Add(new DataFinding("DC-04", ReasonCodes.DataOwnerInactive, "high", subjectKind, subjectId, (person.IsDeputy ? "The deputy " : "The ") + DataWords.Key(r.OwnerRoleKey) + " of " + label + " is disabled, deleted or leaving. Propose a successor with an ownership decision.", domainKey, locationId: locationId, principalId: person.PrincipalId, ownerRoleKey: r.OwnerRoleKey, isDeputy: person.IsDeputy));
        }
    }

    // DC-12 on registered top-level locations; DC-03 and DC-04 for the roles held on any location.
    private static void LocationChecks(DataTree tree, ConfigurationSnapshot snapshot, List<Location> locations, IReadOnlyList<DataOwnerFact> owners, Func<string, DataPrincipalFact?> principal, string day, DateTime asOf, List<DataFinding> findings)
    {
        int staleDays = 180;
        SnapshotRow? setting = SnapshotRead.FindActive(snapshot, "setting", EngineKeys.DataLocationStaleDays);
        if (setting != null && SnapshotRead.Node(setting, "value") is JsonTreeNode value && value.TryGetInt64(out long days) && days > 0)
        {
            staleDays = (int)Math.Min(days, 36500);
        }

        string staleBefore = DataTree.DayOf(asOf.AddDays(-staleDays));
        IReadOnlyList<SnapshotRow> roles = tree.OwnerRolesFor(OwnerSubjectKind.DataLocation);
        foreach (Location location in locations.Where(l => l.RetiredOn == null && l.Status != "archived").OrderBy(l => l.Id, StringComparer.Ordinal))
        {
            string label = location.Label ?? location.Id;
            foreach (SnapshotRow role in roles)
            {
                OwnerResolution r = tree.ResolveOnLocation(role.KeyPath, location.Id, owners, principal, day);
                if (r.Holder != null)
                {
                    HolderChecks(r, label, EngineKeys.DataLocationSubjectKind, location.Id, null, location.Id, principal, findings);
                }
            }

            if (location.Parent != null || location.Status != "registered")
            {
                continue;
            }

            bool technical = roles.Any(r => r.KeyPath == EngineKeys.TechnicalOwnerRole);
            if (technical && tree.ResolveOnLocation(EngineKeys.TechnicalOwnerRole, location.Id, owners, principal, day).Holder == null)
            {
                findings.Add(new DataFinding("DC-12", ReasonCodes.DataLocationUnowned, "medium", EngineKeys.DataLocationSubjectKind, location.Id, label + " has no technical owner. Propose one with an ownership decision.", locationId: location.Id));
            }

            if (location.LastSeenOn == null || string.CompareOrdinal(DataTree.Day(location.LastSeenOn), staleBefore) < 0)
            {
                findings.Add(new DataFinding("DC-12", ReasonCodes.DataLocationStale, "medium", EngineKeys.DataLocationSubjectKind, location.Id, label + (location.LastSeenOn == null ? " was never confirmed" : " was last seen on " + DataWords.Day(DataTree.Day(location.LastSeenOn))) + ", more than " + staleDays + " days ago. Confirm it is still there, or retire it.", locationId: location.Id));
            }
        }
    }

    private static string? StageOf(List<Location> locations, string? id)
    {
        Location? location = locations.FirstOrDefault(l => string.Equals(l.Id, id, StringComparison.OrdinalIgnoreCase));
        if (location == null)
        {
            return null;
        }

        return location.Stage ?? (location.Parent == null ? null : locations.FirstOrDefault(l => string.Equals(l.Id, location.Parent, StringComparison.OrdinalIgnoreCase))?.Stage);
    }

    // Open on the day of the check: not archived, and no End On or an End On after that day.
    private static bool Open(string? status, string? endedOn, string day)
    {
        return status != "archived" && DataTree.IsOpenOn(endedOn, day);
    }

    // DC-05, DC-06, DC-07 and DC-11 on open uses. DC-07 also reads drafts, so a draft that breaks a rule shows at once.
    private static void UseChecks(DataTree tree, List<Use> uses, List<Mapping> mappings, List<Location> locations, List<App> apps, IReadOnlyList<DataOwnerFact> owners, Func<string, DataPrincipalFact?> principal, IReadOnlyList<DataDecisionFact> decisions, string day, List<DataFinding> findings)
    {
        foreach (Use use in uses.Where(u => Open(u.Status, u.EndedOn, day) && tree.IsActiveDomain(u.Domain)).OrderBy(u => u.Id, StringComparer.Ordinal))
        {
            App? app = apps.FirstOrDefault(a => string.Equals(a.Id, use.App, StringComparison.OrdinalIgnoreCase));
            var subject = new DataSubject(EngineKeys.DataUseSubjectKind, use.Domain, use.ReachesAi, use.Deidentified, null, use.Location, StageOf(locations, use.Location), use.App, app?.Scope);
            DataUseEvaluation e = DataUseRules.Evaluate(tree, subject, owners, principal, decisions.Where(d => SubjectIs(d, EngineKeys.DataUseSubjectKind, use.Id)), day);
            string name = (use.Number ?? use.Id) + " (" + (app?.Label ?? use.App) + ", " + e.Path + ")";
            bool registered = use.Status == "registered";
            foreach (Reason reason in DataUseOperation.StateReasons(tree, e))
            {
                string check = reason.Code == ReasonCodes.DataDecisionMissing ? "DC-05" : reason.Code == ReasonCodes.DataNoOneToAsk ? "DC-06" : "DC-07";
                if (check != "DC-07" && !registered)
                {
                    continue;
                }

                string severity = check == "DC-05" ? (use.ReachesAi ? "high" : "medium") : "high";
                findings.Add(new DataFinding(check, reason.Code, severity, EngineKeys.DataUseSubjectKind, use.Id, name + ": " + reason.Message.Substring(reason.Message.IndexOf(':') + 2), use.Domain, use.App, use.Location, e.DataOwnerPrincipalId));
            }

            if (registered && use.Location != null && !mappings.Any(m => Open(m.Status, m.EndedOn, day) && m.Domain == use.Domain && string.Equals(m.Location, use.Location, StringComparison.OrdinalIgnoreCase)))
            {
                findings.Add(new DataFinding("DC-11", ReasonCodes.DataMappingMissing, "low", EngineKeys.DataUseSubjectKind, use.Id, name + " names a location where " + e.Path + " has no mapping. Map that data at the location.", use.Domain, use.App, use.Location, e.DataOwnerPrincipalId));
            }
        }
    }

    // DC-08 on open registered copies outside production.
    private static void MappingChecks(DataTree tree, List<Mapping> mappings, List<Location> locations, IReadOnlyList<DataOwnerFact> owners, Func<string, DataPrincipalFact?> principal, IReadOnlyList<DataDecisionFact> decisions, string day, List<DataFinding> findings)
    {
        foreach (Mapping mapping in mappings.Where(m => Open(m.Status, m.EndedOn, day) && m.Status == "registered" && tree.IsActiveDomain(m.Domain)).OrderBy(m => m.Id, StringComparer.Ordinal))
        {
            var subject = new DataSubject(EngineKeys.DataMappingSubjectKind, mapping.Domain, false, false, mapping.Origin, mapping.Location, StageOf(locations, mapping.Location), null, null);
            DataUseEvaluation e = DataUseRules.Evaluate(tree, subject, owners, principal, decisions.Where(d => SubjectIs(d, EngineKeys.DataMappingSubjectKind, mapping.Id)), day);
            foreach (Reason reason in DataUseOperation.StateReasons(tree, e).Where(r => r.Code == ReasonCodes.DataCopyOutsideProduction))
            {
                findings.Add(new DataFinding("DC-08", reason.Code, "high", EngineKeys.DataMappingSubjectKind, mapping.Id, reason.Message.Substring(reason.Message.IndexOf(':') + 2), mapping.Domain, null, mapping.Location, e.DataOwnerPrincipalId));
            }
        }
    }

    // DC-09, DC-10 and DC-13 per application.
    private static void ApplicationChecks(DataTree tree, List<Use> uses, List<Mapping> mappings, List<App> apps, IReadOnlyList<DataDecisionFact> decisions, string day, List<DataFinding> findings)
    {
        foreach (App app in apps.Where(a => a.Status != "archived").OrderBy(a => a.Id, StringComparer.Ordinal))
        {
            string name = app.Label ?? app.Id;
            List<Use> open = uses.Where(u => Open(u.Status, u.EndedOn, day) && string.Equals(u.App, app.Id, StringComparison.OrdinalIgnoreCase)).ToList();
            List<Use> registered = open.Where(u => u.Status == "registered" && tree.IsActiveDomain(u.Domain)).ToList();

            // DC-09: the highest implied answer of the application's registered uses, against its latest answer.
            (string? Question, string? Answer, int Rank, string Domain)? highest = null;
            foreach (Use use in registered)
            {
                (string? q, string? a) = tree.ImpliedAnswer(use.Domain);
                int? rank = tree.AnswerRank(q, a);
                if (rank.HasValue && (highest == null || rank.Value > highest.Value.Rank))
                {
                    highest = (q, a, rank.Value, use.Domain);
                }
            }

            if (highest != null && app.SensitivityAnswer != null)
            {
                int? have = tree.AnswerRank(highest.Value.Question, app.SensitivityAnswer);
                if (have.HasValue && have.Value < highest.Value.Rank)
                {
                    findings.Add(new DataFinding("DC-09", ReasonCodes.DataSensitivityBelowData, "medium", EngineKeys.ApplicationSubjectKind, app.Id, name + " answers " + DataWords.Key(app.SensitivityAnswer) + " to the sensitivity question, and " + tree.PathText(highest.Value.Domain) + " implies " + DataWords.Key(highest.Value.Answer!) + ". Answer the tier question again.", highest.Value.Domain, app.Id));
                }
            }

            // DC-10: data moves or AI is used, and nothing is declared.
            if ((app.HasDataDependency || app.UsesAi) && !app.HoldsNoBusinessData && open.Count == 0)
            {
                findings.Add(new DataFinding("DC-10", ReasonCodes.DataUndeclared, "low", EngineKeys.ApplicationSubjectKind, app.Id, name + (app.HasDataDependency ? " reads or writes data of another application" : " uses AI") + " and declares no data use. Declare its data uses, or mark it as holding no business data.", null, app.Id));
            }

            // DC-13: a retiring application's write uses on a system of record need an accepted retention decision.
            if (app.IsRetiring)
            {
                foreach (Use use in registered.Where(u => u.Access != DataAccess.Read && u.Location != null))
                {
                    bool systemOfRecord = mappings.Any(m => Open(m.Status, m.EndedOn, day) && m.Domain == use.Domain && string.Equals(m.Location, use.Location, StringComparison.OrdinalIgnoreCase) && m.Origin == DataOrigin.SystemOfRecord);
                    bool decided = decisions.Any(d => d.DecisionTypeKey == EngineKeys.DataRetentionDecisionType && d.Status == DecisionStatusMeaning.Accepted && SubjectIs(d, EngineKeys.DataUseSubjectKind, use.Id));
                    if (systemOfRecord && !decided)
                    {
                        findings.Add(new DataFinding("DC-13", ReasonCodes.DataRetentionOpen, "high", EngineKeys.DataUseSubjectKind, use.Id, name + " is retiring and writes " + tree.PathText(use.Domain) + " on its system of record without an accepted retention decision. Propose a data-retention decision.", use.Domain, app.Id, use.Location));
                    }
                }
            }
        }
    }

    // DC-14: an open use or mapping, or a current owner assignment, on a domain that is retired or gone.
    private static void RetiredDomainRows(DataTree tree, List<Use> uses, List<Mapping> mappings, IReadOnlyList<DataOwnerFact> owners, string day, List<DataFinding> findings)
    {
        foreach (Use use in uses.Where(u => Open(u.Status, u.EndedOn, day) && !tree.IsActiveDomain(u.Domain)).OrderBy(u => u.Id, StringComparer.Ordinal))
        {
            findings.Add(new DataFinding("DC-14", ReasonCodes.DataRetiredDomainRows, "high", EngineKeys.DataUseSubjectKind, use.Id, "Data use " + (use.Number ?? use.Id) + " points at " + DataWords.Key(use.Domain) + ", which is retired or gone. Move it to an active domain.", use.Domain, use.App, use.Location));
        }

        foreach (Mapping mapping in mappings.Where(m => Open(m.Status, m.EndedOn, day) && !tree.IsActiveDomain(m.Domain)).OrderBy(m => m.Id, StringComparer.Ordinal))
        {
            findings.Add(new DataFinding("DC-14", ReasonCodes.DataRetiredDomainRows, "high", EngineKeys.DataMappingSubjectKind, mapping.Id, "Data mapping " + mapping.Id + " points at " + DataWords.Key(mapping.Domain) + ", which is retired or gone. Move it to an active domain.", mapping.Domain, null, mapping.Location));
        }

        foreach (DataOwnerFact owner in owners.Where(o => o.DataDomainKey != null && o.IsCurrent(day) && !tree.IsActiveDomain(o.DataDomainKey)).OrderBy(o => o.AssignmentId, StringComparer.Ordinal))
        {
            findings.Add(new DataFinding("DC-14", ReasonCodes.DataRetiredDomainRows, "high", "owner-assignment", owner.AssignmentId, "Owner assignment " + owner.AssignmentId + " points at " + DataWords.Key(owner.DataDomainKey!) + ", which is retired or gone. End it with an ownership decision.", owner.DataDomainKey, principalId: owner.PrincipalId));
        }
    }

    private static bool SubjectIs(DataDecisionFact decision, string kind, string id)
    {
        return (decision.SubjectKind == null || decision.SubjectKind == kind) && decision.SubjectId != null && string.Equals(decision.SubjectId, id, StringComparison.OrdinalIgnoreCase);
    }
}

// Keys and days in the words people read (product review: raw keys and ISO dates reached the pages).
internal static class DataWords
{
    // "data-owner" gives "data owner".
    public static string Key(string? key)
    {
        return string.IsNullOrEmpty(key) ? "none" : key!.Replace('-', ' ');
    }

    // A yyyy-MM-dd day as the pages write it: 2 May 2026.
    public static string Day(string day)
    {
        return DateTime.TryParseExact(day, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime parsed)
            ? parsed.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture)
            : day;
    }
}
