using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;
using Product.Engine.Core.Snapshots;

namespace Product.Engine.Core.Operations;

// One Approval Record (O10) for the decision, as the adapter fetched it (document 25, section 5.5, ApprovalFact).
public sealed class ApprovalFact
{
    public ApprovalFact(
        string approvalRecordId,
        string ruleKey,
        ApprovalOutcomeMeaning outcome,
        string? responderPrincipalId = null,
        ActorKindMeaning? responderActorKind = null,
        PrincipalKind? responderPrincipalKind = null,
        bool responderHoldsRequiredRole = false,
        bool hasTypedReason = false,
        DateTime? respondedOnUtc = null,
        DateTime? expiresOnUtc = null)
    {
        if (string.IsNullOrEmpty(approvalRecordId))
        {
            throw new ArgumentException("An approval fact names its Approval Record.", nameof(approvalRecordId));
        }

        if (!ConfigurationKey.IsValid(ruleKey))
        {
            throw new ArgumentException("The rule key must use the configuration key format.", nameof(ruleKey));
        }

        if (respondedOnUtc.HasValue && respondedOnUtc.Value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Responded On is a UTC date-time.", nameof(respondedOnUtc));
        }

        if (expiresOnUtc.HasValue && expiresOnUtc.Value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Expires On is a UTC date-time.", nameof(expiresOnUtc));
        }

        bool answered = outcome == ApprovalOutcomeMeaning.Approved || outcome == ApprovalOutcomeMeaning.Rejected;
        if (answered && (string.IsNullOrEmpty(responderPrincipalId) || responderActorKind == null || responderPrincipalKind == null || respondedOnUtc == null))
        {
            throw new ArgumentException("An approved or rejected answer says who answered, as which actor and principal kind, and when.", nameof(outcome));
        }

        ApprovalRecordId = approvalRecordId;
        RuleKey = ruleKey;
        Outcome = outcome;
        ResponderPrincipalId = string.IsNullOrEmpty(responderPrincipalId) ? null : responderPrincipalId;
        ResponderActorKind = responderActorKind;
        ResponderPrincipalKind = responderPrincipalKind;
        ResponderHoldsRequiredRole = responderHoldsRequiredRole;
        HasTypedReason = hasTypedReason;
        RespondedOnUtc = respondedOnUtc;
        ExpiresOnUtc = expiresOnUtc;
    }

    public string ApprovalRecordId { get; }

    // The requirement that asked for it: an approval rule key, or the decision right key of an accountable card.
    public string RuleKey { get; }

    public ApprovalOutcomeMeaning Outcome { get; }

    // Who answered. Null while the record is pending.
    public string? ResponderPrincipalId { get; }

    public ActorKindMeaning? ResponderActorKind { get; }

    public PrincipalKind? ResponderPrincipalKind { get; }

    // From the approval runner's record: the responder held the required role or owner role when answering.
    public bool ResponderHoldsRequiredRole { get; }

    public bool HasTypedReason { get; }

    public DateTime? RespondedOnUtc { get; }

    // Null when the request never expires.
    public DateTime? ExpiresOnUtc { get; }

    public bool IsAnswer => Outcome == ApprovalOutcomeMeaning.Approved || Outcome == ApprovalOutcomeMeaning.Rejected;

    internal static readonly string[] WireFields =
    {
        "approvalRecordId", "ruleKey", "outcome", "responderPrincipalId", "responderActorKind", "responderPrincipalKind",
        "responderHoldsRequiredRole", "hasTypedReason", "respondedOnUtc", "expiresOnUtc",
    };

    // An approved or rejected answer must say who answered, as what, and when.
    internal static ApprovalFact FromJson(WireObject wire)
    {
        ApprovalOutcomeMeaning outcome = wire.Contract("outcome", EngineContract.ApprovalOutcomeMeanings);
        bool answered = outcome == ApprovalOutcomeMeaning.Approved || outcome == ApprovalOutcomeMeaning.Rejected;
        string? responder = answered ? wire.RequiredString("responderPrincipalId") : wire.OptionalString("responderPrincipalId");
        ActorKindMeaning? actorKind = answered || wire.Has("responderActorKind")
            ? wire.Contract("responderActorKind", EngineContract.ActorKindMeanings)
            : null;
        PrincipalKind? principalKind = answered || wire.Has("responderPrincipalKind")
            ? wire.Contract("responderPrincipalKind", EngineContract.PrincipalKinds)
            : null;
        DateTime? respondedOn = answered ? wire.RequiredUtc("respondedOnUtc") : wire.OptionalUtc("respondedOnUtc");
        return new ApprovalFact(
            wire.RequiredString("approvalRecordId"),
            wire.RequiredKey("ruleKey"),
            outcome,
            responder,
            actorKind,
            principalKind,
            wire.Bool("responderHoldsRequiredRole", false),
            wire.Bool("hasTypedReason", false),
            respondedOn,
            wire.OptionalUtc("expiresOnUtc"));
    }
}

// The people the separation rules compare with the responder (document 25, section 5.5, SeparationFacts).
public sealed class SeparationFacts
{
    public SeparationFacts(
        string? proposerPrincipalId = null,
        string? instructingAdminPrincipalId = null,
        IEnumerable<string>? deployerPrincipalIds = null,
        IDictionary<string, IReadOnlyList<string>>? ownerRoleHolders = null,
        IDictionary<string, IReadOnlyList<string>>? roleHolders = null)
    {
        ProposerPrincipalId = string.IsNullOrEmpty(proposerPrincipalId) ? null : proposerPrincipalId;
        InstructingAdminPrincipalId = string.IsNullOrEmpty(instructingAdminPrincipalId) ? null : instructingAdminPrincipalId;
        DeployerPrincipalIds = new ReadOnlyCollection<string>((deployerPrincipalIds ?? Enumerable.Empty<string>()).ToList());
        OwnerRoleHolders = Copy(ownerRoleHolders);
        RoleHolders = Copy(roleHolders);
    }

    // The person who proposed the decision. For an AI-drafted decision, the person it acted for.
    public string? ProposerPrincipalId { get; }

    // For a configuration change: the admin who instructed it.
    public string? InstructingAdminPrincipalId { get; }

    // For change authorization: the people who started deployments of the change, once known.
    public IReadOnlyList<string> DeployerPrincipalIds { get; }

    // Current holders of owner roles for the application (O06), by owner role key.
    public IReadOnlyDictionary<string, IReadOnlyList<string>> OwnerRoleHolders { get; }

    // Current holders of product roles that a matching separation rule names, by role key.
    public IReadOnlyDictionary<string, IReadOnlyList<string>> RoleHolders { get; }

    internal static SeparationFacts FromJson(WireObject? wire)
    {
        if (wire == null)
        {
            return new SeparationFacts();
        }

        return new SeparationFacts(
            wire.OptionalString("proposerPrincipalId"),
            wire.OptionalString("instructingAdminPrincipalId"),
            wire.Strings("deployerPrincipalIds"),
            wire.StringListMap("ownerRoleHolders").ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
            wire.StringListMap("roleHolders").ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal));
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Copy(IDictionary<string, IReadOnlyList<string>>? source)
    {
        var copy = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (source != null)
        {
            foreach (KeyValuePair<string, IReadOnlyList<string>> pair in source)
            {
                copy[pair.Key] = pair.Value.ToArray();
            }
        }

        return new ReadOnlyDictionary<string, IReadOnlyList<string>>(copy);
    }
}

// OP-03 input (document 25, section 5.5).
public sealed class ApprovalStatusRequest
{
    public ApprovalStatusRequest(
        IEnumerable<RequiredApproval> requiredApprovals,
        IEnumerable<ApprovalFact> records,
        string decisionTypeKey,
        string? tierKey = null,
        string? targetStageKey = null,
        SeparationFacts? parties = null,
        IEnumerable<string>? acceptedExceptionDecisionTypeKeys = null)
    {
        if (!ConfigurationKey.IsValid(decisionTypeKey))
        {
            throw new ArgumentException("The decision type key must use the configuration key format.", nameof(decisionTypeKey));
        }

        RequiredApprovals = new ReadOnlyCollection<RequiredApproval>((requiredApprovals ?? throw new ArgumentNullException(nameof(requiredApprovals))).ToList());
        Records = new ReadOnlyCollection<ApprovalFact>((records ?? throw new ArgumentNullException(nameof(records))).ToList());
        DecisionTypeKey = decisionTypeKey;
        TierKey = tierKey;
        TargetStageKey = targetStageKey;
        Parties = parties ?? new SeparationFacts();
        AcceptedExceptionDecisionTypeKeys = new ReadOnlyCollection<string>((acceptedExceptionDecisionTypeKeys ?? Enumerable.Empty<string>()).ToList());
    }

    // From OP-02, recomputed with the decision's stamped configuration version. Consultations are passed too.
    public IReadOnlyList<RequiredApproval> RequiredApprovals { get; }

    // One per Approval Record of the decision.
    public IReadOnlyList<ApprovalFact> Records { get; }

    // The scope that selects C40 Separation Rule rows.
    public string DecisionTypeKey { get; }

    public string? TierKey { get; }

    public string? TargetStageKey { get; }

    public SeparationFacts Parties { get; }

    // Decision types with an accepted exception decision linked to this decision (fetched).
    public IReadOnlyList<string> AcceptedExceptionDecisionTypeKeys { get; }

    public static ApprovalStatusRequest FromJson(JsonTreeNode input)
    {
        var wire = new WireObject(input, "input", "requiredApprovals", "records", "scope", "parties", "acceptedExceptionDecisionTypeKeys");
        WireObject scope = wire.OptionalObject("scope", "decisionTypeKey", "tierKey", "targetStageKey")
            ?? throw OperationStop.Input(ReasonCodes.InputRequired, "input.scope is required.");
        return new ApprovalStatusRequest(
            wire.Objects("requiredApprovals", required: true, RequiredApproval.WireFields).Select(RequiredApproval.FromJson),
            wire.Objects("records", required: false, ApprovalFact.WireFields).Select(ApprovalFact.FromJson),
            scope.RequiredKey("decisionTypeKey"),
            scope.OptionalKey("tierKey"),
            scope.OptionalKey("targetStageKey"),
            SeparationFacts.FromJson(wire.OptionalObject(
                "parties",
                "proposerPrincipalId",
                "instructingAdminPrincipalId",
                "deployerPrincipalIds",
                "ownerRoleHolders",
                "roleHolders")),
            wire.Keys("acceptedExceptionDecisionTypeKeys"));
    }
}

// A record that does not count, with every reason it fails.
public sealed class NonCountingRecord
{
    public NonCountingRecord(string approvalRecordId, IEnumerable<string> reasonCodes)
    {
        ApprovalRecordId = approvalRecordId;
        ReasonCodes = new ReadOnlyCollection<string>(reasonCodes.ToList());
    }

    public string ApprovalRecordId { get; }

    public IReadOnlyList<string> ReasonCodes { get; }

    internal JsonTreeNode ToJson()
    {
        return new WireWriter().Add("approvalRecordId", ApprovalRecordId).Add("reasonCodes", ReasonCodes).Build();
    }
}

// The state of one counting requirement.
public sealed class RequirementStatus
{
    public RequirementStatus(
        RequiredApproval requirement,
        ApprovalOverall state,
        IEnumerable<string> countingRecordIds,
        IEnumerable<string> pendingRecordIds,
        IEnumerable<NonCountingRecord> nonCountingRecords)
    {
        Requirement = requirement;
        State = state;
        CountingRecordIds = new ReadOnlyCollection<string>(countingRecordIds.ToList());
        PendingRecordIds = new ReadOnlyCollection<string>(pendingRecordIds.ToList());
        NonCountingRecords = new ReadOnlyCollection<NonCountingRecord>(nonCountingRecords.ToList());
    }

    public RequiredApproval Requirement { get; }

    public ApprovalOverall State { get; }

    // Records that pass the approver kind rule, approvals and rejections alike.
    public IReadOnlyList<string> CountingRecordIds { get; }

    // Records still waiting for an answer, before they expire.
    public IReadOnlyList<string> PendingRecordIds { get; }

    public IReadOnlyList<NonCountingRecord> NonCountingRecords { get; }

    internal JsonTreeNode ToJson()
    {
        return new WireWriter()
            .Add("requirementKey", Requirement.RequirementKey)
            .Add("source", EngineContract.ApprovalRequirementSources.ToWire(Requirement.Source))
            .Add("approvalMode", EngineContract.ApprovalModes.ToWire(Requirement.ApprovalMode))
            .Add("state", EngineContract.ApprovalOveralls.ToWire(State))
            .Add("countingRecordIds", CountingRecordIds)
            .Add("pendingRecordIds", PendingRecordIds)
            .Add("nonCountingRecords", WireWriter.Array(NonCountingRecords.Select(r => r.ToJson())))
            .Build();
    }
}

// A consultation and the answers it got. Shown, never counted.
public sealed class ConsultationStatus
{
    public ConsultationStatus(RequiredApproval consultation, IEnumerable<string> recordIds)
    {
        Consultation = consultation;
        RecordIds = new ReadOnlyCollection<string>(recordIds.ToList());
    }

    public RequiredApproval Consultation { get; }

    public IReadOnlyList<string> RecordIds { get; }

    internal JsonTreeNode ToJson()
    {
        return new WireWriter().Add("requirementKey", Consultation.RequirementKey).Add("recordIds", RecordIds).Build();
    }
}

// OP-03 output (document 25, section 5.5).
public sealed class ApprovalStatusResult : IOperationPayload
{
    public ApprovalStatusResult(
        ApprovalOverall overall,
        IEnumerable<RequirementStatus> requirements,
        IEnumerable<ConsultationStatus> consultations,
        IEnumerable<KeyValuePair<string, bool>> countsByRecord)
    {
        Overall = overall;
        Requirements = new ReadOnlyCollection<RequirementStatus>(requirements.ToList());
        Consultations = new ReadOnlyCollection<ConsultationStatus>(consultations.ToList());
        var counts = new SortedDictionary<string, bool>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, bool> pair in countsByRecord)
        {
            counts[pair.Key] = pair.Value;
        }

        CountsByRecord = new ReadOnlyDictionary<string, bool>(counts);
    }

    public ApprovalOverall Overall { get; }

    public IReadOnlyList<RequirementStatus> Requirements { get; }

    public IReadOnlyList<ConsultationStatus> Consultations { get; }

    // The value the engine writes into O10 Counts Toward Requirement, by record ID.
    public IReadOnlyDictionary<string, bool> CountsByRecord { get; }

    public JsonTreeNode ToJson()
    {
        return new WireWriter()
            .Add("overall", EngineContract.ApprovalOveralls.ToWire(Overall))
            .Add("requirements", WireWriter.Array(Requirements.Select(r => r.ToJson())))
            .Add("consultations", WireWriter.Array(Consultations.Select(c => c.ToJson())))
            .Add("countsByRecord", JsonTreeNode.CreateObject(CountsByRecord.Select(p => new KeyValuePair<string, JsonTreeNode>(p.Key, JsonTreeNode.FromBoolean(p.Value)))))
            .Build();
    }
}

// OP-03 Evaluate approval status, with the approver kind rule (document 25, section 5.5; sweep proposal).
// An answer counts only when it comes from a person, signed in as a user, who held the required role, typed a
// reason when the rule asks for one, answered before the request expired, is not a consultation, and passes every
// matching C40 Separation Rule (working default, 28 issue 5). AI never approves (D-02, D-10): the AI approval lock
// is a locked setting, and this rule never reads it, so no snapshot can switch the rule off.
public static class ApprovalStatusOperation
{
    private const string SeparationEntity = "separation-rule";
    private static readonly string[] SeparationScope = { "decisionTypeKey", "tierKey", "targetStageKey" };

    public static EvaluationResult<ApprovalStatusResult?> Evaluate(ConfigurationSnapshot snapshot, EvaluationContext context, ApprovalStatusRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return OperationShell.Run(context, snapshot, needsSnapshot: true, () => Answer(snapshot, context, request));
    }

    public static EvaluationResult<ApprovalStatusResult?> Evaluate(ConfigurationSnapshot snapshot, EvaluationContext context, JsonTreeNode input)
    {
        return OperationShell.Run(context, snapshot, needsSnapshot: true, () => Answer(snapshot, context, ApprovalStatusRequest.FromJson(input)));
    }

    private static OperationAnswer<ApprovalStatusResult> Answer(ConfigurationSnapshot snapshot, EvaluationContext context, ApprovalStatusRequest request)
    {
        DateTime asOf = context.AsOfUtc;
        SnapshotRow decisionType = SnapshotRead.RequireExisting(snapshot, "decision-type", request.DecisionTypeKey, "input.scope.decisionTypeKey");

        var requirementsByKey = new Dictionary<string, RequiredApproval>(StringComparer.Ordinal);
        foreach (RequiredApproval requirement in request.RequiredApprovals)
        {
            if (requirementsByKey.ContainsKey(requirement.RequirementKey))
            {
                throw OperationStop.Input(ReasonCodes.InputMalformed, "input.requiredApprovals names requirement " + SnapshotRead.Quote(requirement.RequirementKey) + " twice.", requirement.RequirementKey);
            }

            requirementsByKey.Add(requirement.RequirementKey, requirement);
        }

        List<RequiredApproval> counting = request.RequiredApprovals.Where(r => !r.IsConsultation)
            .OrderBy(r => r.Sequence ?? 0).ThenBy(r => r.RequirementKey, StringComparer.Ordinal).ToList();
        if (counting.Count == 0)
        {
            // Satisfied with no counting requirement would accept a decision without a person. OP-02 never gives that.
            throw OperationStop.Input(ReasonCodes.InputRequired, "input.requiredApprovals holds no counting requirement, so there is nothing a person could satisfy. Use OP-02 to resolve the requirements.");
        }

        var recordIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (ApprovalFact record in request.Records)
        {
            if (!recordIds.Add(record.ApprovalRecordId))
            {
                throw OperationStop.Input(ReasonCodes.InputMalformed, "input.records repeats the Approval Record " + record.ApprovalRecordId + ".", record.ApprovalRecordId);
            }
        }

        var trace = new List<RuleTrace>();
        List<SeparationCheck> separation = SeparationChecks(snapshot, request, decisionType, trace);

        var judged = new Dictionary<string, Judgement>(StringComparer.Ordinal);
        var reasons = new List<Reason>();
        foreach (ApprovalFact record in request.Records)
        {
            requirementsByKey.TryGetValue(record.RuleKey, out RequiredApproval? requirement);
            Judgement judgement = Judge(record, requirement, separation, request, asOf);
            judged.Add(record.ApprovalRecordId, judgement);
        }

        // Mode rules, requirement by requirement. A sequential requirement starts counting only when every earlier
        // sequential requirement is satisfied.
        var statuses = new List<RequirementStatus>();
        bool earlierSequentialSatisfied = true;
        foreach (RequiredApproval requirement in counting)
        {
            List<ApprovalFact> records = request.Records.Where(r => string.Equals(r.RuleKey, requirement.RequirementKey, StringComparison.Ordinal)).ToList();
            bool waiting = requirement.ApprovalMode == ApprovalMode.Sequential && !earlierSequentialSatisfied;
            if (waiting)
            {
                foreach (ApprovalFact record in records.Where(r => judged[r.ApprovalRecordId].Counts))
                {
                    judged[record.ApprovalRecordId] = Judgement.Waiting();
                }
            }

            ApprovalOverall state = waiting ? ApprovalOverall.Pending : State(requirement, records, judged, asOf);
            if (requirement.ApprovalMode == ApprovalMode.Sequential && state != ApprovalOverall.Satisfied)
            {
                earlierSequentialSatisfied = false;
            }

            statuses.Add(new RequirementStatus(
                requirement,
                state,
                records.Where(r => judged[r.ApprovalRecordId].Counts).Select(r => r.ApprovalRecordId),
                records.Where(r => judged[r.ApprovalRecordId].IsOpen).Select(r => r.ApprovalRecordId),
                records.Where(r => judged[r.ApprovalRecordId].Codes.Count > 0)
                    .Select(r => new NonCountingRecord(r.ApprovalRecordId, judged[r.ApprovalRecordId].Codes))));
        }

        var consultations = request.RequiredApprovals.Where(r => r.IsConsultation)
            .OrderBy(r => r.RequirementKey, StringComparer.Ordinal)
            .Select(c => new ConsultationStatus(c, request.Records.Where(r => string.Equals(r.RuleKey, c.RequirementKey, StringComparison.Ordinal)).Select(r => r.ApprovalRecordId)))
            .ToList();

        foreach (ApprovalFact record in request.Records.OrderBy(r => r.ApprovalRecordId, StringComparer.Ordinal))
        {
            Judgement judgement = judged[record.ApprovalRecordId];
            foreach (string code in judgement.Codes)
            {
                reasons.Add(ReasonCatalog.Create(code, "Approval Record", record.ApprovalRecordId));
            }

            if (judgement.Flagged)
            {
                reasons.Add(ReasonCatalog.Create(ReasonCodes.ApprovalSeparationFlagged, "Approval Record", record.ApprovalRecordId));
            }
        }

        ApprovalOverall overall = statuses.Any(s => s.State == ApprovalOverall.Rejected) ? ApprovalOverall.Rejected
            : statuses.Any(s => s.State == ApprovalOverall.Expired) ? ApprovalOverall.Expired
            : statuses.All(s => s.State == ApprovalOverall.Satisfied) ? ApprovalOverall.Satisfied
            : ApprovalOverall.Pending;

        var result = new ApprovalStatusResult(
            overall,
            statuses,
            consultations,
            request.Records.Select(r => new KeyValuePair<string, bool>(r.ApprovalRecordId, judged[r.ApprovalRecordId].Counts)));
        return new OperationAnswer<ApprovalStatusResult>(EngineContract.ApprovalOveralls.ToWire(overall), result, reasons, trace);
    }

    // The approver kind rule for one record. Every failed test adds its reason code.
    private static Judgement Judge(ApprovalFact record, RequiredApproval? requirement, List<SeparationCheck> separation, ApprovalStatusRequest request, DateTime asOf)
    {
        if (requirement == null)
        {
            return Judgement.NotCounted(ReasonCodes.ApprovalNoRequirement);
        }

        if (requirement.IsConsultation)
        {
            return Judgement.NotCounted(ReasonCodes.ApprovalConsultationNotCounted);
        }

        switch (record.Outcome)
        {
            case ApprovalOutcomeMeaning.Pending:
                return record.ExpiresOnUtc.HasValue && asOf >= record.ExpiresOnUtc.Value
                    ? Judgement.NotCounted(ReasonCodes.ApprovalExpired, expired: true)
                    : Judgement.Open();
            case ApprovalOutcomeMeaning.Expired:
                return Judgement.NotCounted(ReasonCodes.ApprovalExpired, expired: true);
            case ApprovalOutcomeMeaning.Cancelled:
            case ApprovalOutcomeMeaning.RecommendationOnly:
                return Judgement.NotCounted(ReasonCodes.ApprovalNotApproved);
        }

        var codes = new List<string>();
        ActorKindMeaning actorKind = record.ResponderActorKind!.Value;
        if (!ContractFacts.IsPerson(actorKind))
        {
            codes.Add(ContractFacts.IsAiActor(actorKind) ? ReasonCodes.ApprovalAiActorNotCounted : ReasonCodes.ApprovalWorkloadNotCounted);
        }

        if (!ContractFacts.CanApprove(record.ResponderPrincipalKind!.Value))
        {
            codes.Add(ReasonCodes.ApprovalNonUserPrincipal);
        }

        if (!record.ResponderHoldsRequiredRole)
        {
            codes.Add(ReasonCodes.ApprovalNotRoleHolder);
        }

        if (requirement.RequiresTypedReason && !record.HasTypedReason)
        {
            codes.Add(ReasonCodes.ApprovalReasonMissing);
        }

        bool lateAnswer = record.ExpiresOnUtc.HasValue && record.RespondedOnUtc!.Value >= record.ExpiresOnUtc.Value;
        if (lateAnswer)
        {
            codes.Add(ReasonCodes.ApprovalExpired);
        }

        bool flagged = false;
        foreach (SeparationCheck check in separation)
        {
            if (!check.People(request.Parties).Contains(record.ResponderPrincipalId!, StringComparer.Ordinal))
            {
                continue;
            }

            switch (check.Enforcement)
            {
                case SeparationEnforcement.Refuse:
                    AddOnce(codes, ReasonCodes.ApprovalSeparationRefused);
                    break;
                case SeparationEnforcement.ExceptionDecision:
                    if (check.ExceptionDecisionTypeKey == null || !request.AcceptedExceptionDecisionTypeKeys.Contains(check.ExceptionDecisionTypeKey, StringComparer.Ordinal))
                    {
                        AddOnce(codes, ReasonCodes.ApprovalSeparationExceptionMissing);
                    }

                    break;
                case SeparationEnforcement.Flag:
                    flagged = true;
                    break;
            }
        }

        if (codes.Count > 0)
        {
            return new Judgement(false, false, codes, false, lateAnswer, record.Outcome);
        }

        return new Judgement(true, false, new List<string>(), flagged, false, record.Outcome);
    }

    private static void AddOnce(List<string> codes, string code)
    {
        if (!codes.Contains(code))
        {
            codes.Add(code);
        }
    }

    // Everyone must approve and sequential: satisfied when counting approvals reach Minimum Count and no counting
    // rejection exists. First to respond: the first counting answers decide, in time order. A requirement whose every
    // record has expired is expired; otherwise it is pending.
    private static ApprovalOverall State(RequiredApproval requirement, List<ApprovalFact> records, Dictionary<string, Judgement> judged, DateTime asOf)
    {
        List<ApprovalFact> countingAnswers = records.Where(r => judged[r.ApprovalRecordId].Counts)
            .OrderBy(r => r.RespondedOnUtc!.Value)
            .ThenBy(r => r.ApprovalRecordId, StringComparer.Ordinal)
            .ToList();

        if (requirement.ApprovalMode == ApprovalMode.FirstToRespond)
        {
            // In time order: a counting rejection before Minimum Count approvals decides rejected, and reaching the
            // count decides satisfied. With the default count of 1 the first counting answer decides. A count above 1
            // (the strict separation overlay's two change board members) needs that many approvals first.
            int approvals = 0;
            foreach (ApprovalFact answer in countingAnswers)
            {
                if (answer.Outcome == ApprovalOutcomeMeaning.Rejected)
                {
                    return ApprovalOverall.Rejected;
                }

                approvals++;
                if (approvals >= requirement.MinimumCount)
                {
                    return ApprovalOverall.Satisfied;
                }
            }
        }
        else
        {
            if (countingAnswers.Any(r => r.Outcome == ApprovalOutcomeMeaning.Rejected))
            {
                return ApprovalOverall.Rejected;
            }

            if (countingAnswers.Count(r => r.Outcome == ApprovalOutcomeMeaning.Approved) >= requirement.MinimumCount)
            {
                return ApprovalOverall.Satisfied;
            }
        }

        return records.Count > 0 && records.All(r => judged[r.ApprovalRecordId].IsExpired) ? ApprovalOverall.Expired : ApprovalOverall.Pending;
    }

    // The C40 approver-differs rows that apply to this scope: per subject (what must differ), the most specific
    // matching row wins (document 21, section 10.3). Rows about different subjects all apply. A row without a
    // decision type applies to every decision type that requires approval (document 21, C40).
    private static List<SeparationCheck> SeparationChecks(ConfigurationSnapshot snapshot, ApprovalStatusRequest request, SnapshotRow decisionType, List<RuleTrace> trace)
    {
        string approverDiffers = EngineContract.SeparationRuleKinds.ToWire(SeparationRuleKind.ApproverDiffers);
        bool requiresApproval = SnapshotRead.Flag(decisionType, "requiresApproval");
        var bySubject = new Dictionary<string, List<SnapshotRow>>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (SnapshotRow row in SnapshotRead.ActiveRows(snapshot, SeparationEntity))
        {
            if (!string.Equals(SnapshotRead.Text(row, "ruleKind"), approverDiffers, StringComparison.Ordinal))
            {
                continue;
            }

            string? type = SnapshotRead.Text(row, "decisionTypeKey");
            string? tier = SnapshotRead.Text(row, "tierKey");
            string? stage = SnapshotRead.Text(row, "targetStageKey");
            bool matches = (type == null ? requiresApproval : string.Equals(type, request.DecisionTypeKey, StringComparison.Ordinal))
                && (tier == null || string.Equals(tier, request.TierKey, StringComparison.Ordinal))
                && (stage == null || string.Equals(stage, request.TargetStageKey, StringComparison.Ordinal));
            if (!matches)
            {
                continue;
            }

            string subject = (SnapshotRead.Text(row, "mustDifferFrom") ?? string.Empty) + " "
                + (SnapshotRead.Text(row, "mustDifferFromOwnerRoleKey") ?? SnapshotRead.Text(row, "mustDifferFromRoleKey") ?? string.Empty);
            if (!bySubject.TryGetValue(subject, out List<SnapshotRow>? rows))
            {
                rows = new List<SnapshotRow>();
                bySubject.Add(subject, rows);
                order.Add(subject);
            }

            rows.Add(row);
        }

        var checks = new List<SeparationCheck>();
        foreach (string subject in order)
        {
            List<SnapshotRow> rows = bySubject[subject];
            int top = rows.Max(Specificity);
            SnapshotRow[] winners = rows.Where(r => Specificity(r) == top).ToArray();
            if (winners.Length > 1)
            {
                throw OperationStop.Broken(
                    ReasonCodes.RuleGroupAmbiguous,
                    "Separation Rules " + SnapshotRead.Quote(winners[0].KeyPath) + " and " + SnapshotRead.Quote(winners[1].KeyPath)
                    + " both apply to the same subject with the same specificity, so no single rule wins. The configuration is broken.",
                    "Separation Rule",
                    winners[1].KeyPath);
            }

            SnapshotRow winner = winners[0];
            trace.Add(new RuleTrace("Separation Rule", winner.KeyPath, RuleTraceRole.Won));
            foreach (SnapshotRow dropped in rows.Where(r => !ReferenceEquals(r, winner)))
            {
                trace.Add(new RuleTrace("Separation Rule", dropped.KeyPath, RuleTraceRole.Dropped));
            }

            checks.Add(SeparationCheck.From(winner, request));
        }

        return checks;
    }

    private static int Specificity(SnapshotRow row)
    {
        return SeparationScope.Count(column => SnapshotRead.Text(row, column) != null);
    }

    // One separation row in force for this decision.
    private sealed class SeparationCheck
    {
        private SeparationCheck(SnapshotRow row, SeparationSubject subject, string? subjectRoleKey, SeparationEnforcement enforcement, string? exceptionDecisionTypeKey)
        {
            Row = row;
            Subject = subject;
            SubjectRoleKey = subjectRoleKey;
            Enforcement = enforcement;
            ExceptionDecisionTypeKey = exceptionDecisionTypeKey;
        }

        public SnapshotRow Row { get; }

        public SeparationSubject Subject { get; }

        public string? SubjectRoleKey { get; }

        public SeparationEnforcement Enforcement { get; }

        public string? ExceptionDecisionTypeKey { get; }

        public static SeparationCheck From(SnapshotRow row, ApprovalStatusRequest request)
        {
            if (!EngineContract.SeparationSubjects.TryParse(SnapshotRead.Text(row, "mustDifferFrom"), out SeparationSubject subject)
                || !EngineContract.SeparationEnforcements.TryParse(SnapshotRead.Text(row, "enforcement"), out SeparationEnforcement enforcement))
            {
                throw OperationStop.Broken(
                    ReasonCodes.ShapeSchema,
                    "Separation Rule " + SnapshotRead.Quote(row.KeyPath) + " has no known subject or enforcement, so the configuration is broken.",
                    "Separation Rule",
                    row.KeyPath);
            }

            string? roleKey = subject == SeparationSubject.OwnerRoleHolder ? SnapshotRead.Text(row, "mustDifferFromOwnerRoleKey")
                : subject == SeparationSubject.RoleHolder ? SnapshotRead.Text(row, "mustDifferFromRoleKey")
                : null;
            if ((subject == SeparationSubject.OwnerRoleHolder || subject == SeparationSubject.RoleHolder) && roleKey == null)
            {
                throw OperationStop.Broken(
                    ReasonCodes.RefExactlyOne,
                    "Separation Rule " + SnapshotRead.Quote(row.KeyPath) + " compares with role holders but names no role, so the configuration is broken.",
                    "Separation Rule",
                    row.KeyPath);
            }

            // The proposer is always known for a decision. Without it the rule could never refuse a self-approval.
            if (subject == SeparationSubject.Proposer && request.Parties.ProposerPrincipalId == null)
            {
                throw OperationStop.Input(
                    ReasonCodes.InputRequired,
                    "input.parties.proposerPrincipalId is required: Separation Rule " + SnapshotRead.Quote(row.KeyPath) + " compares the responder with the proposer.");
            }

            return new SeparationCheck(row, subject, roleKey, enforcement, SnapshotRead.Text(row, "exceptionDecisionTypeKey"));
        }

        // The people this row says the responder must differ from.
        public IReadOnlyList<string> People(SeparationFacts parties)
        {
            switch (Subject)
            {
                case SeparationSubject.Proposer:
                    return parties.ProposerPrincipalId == null ? Array.Empty<string>() : new[] { parties.ProposerPrincipalId };
                case SeparationSubject.InstructingAdmin:
                    // WORKING DEFAULT (open design question 4): Balanced ships no instructing-admin row, so the instructing
                    // admin may accept their own AI's draft. Regulated and strict separation add one, so a second admin accepts.
                    return parties.InstructingAdminPrincipalId == null ? Array.Empty<string>() : new[] { parties.InstructingAdminPrincipalId };
                case SeparationSubject.Deployer:
                    return parties.DeployerPrincipalIds;
                case SeparationSubject.OwnerRoleHolder:
                    return parties.OwnerRoleHolders.TryGetValue(SubjectRoleKey!, out IReadOnlyList<string>? owners) ? owners : Array.Empty<string>();
                case SeparationSubject.RoleHolder:
                    return parties.RoleHolders.TryGetValue(SubjectRoleKey!, out IReadOnlyList<string>? holders) ? holders : Array.Empty<string>();
                default:
                    return Array.Empty<string>();
            }
        }
    }

    // What the approver kind rule says about one record.
    private sealed class Judgement
    {
        public Judgement(bool counts, bool isOpen, List<string> codes, bool flagged, bool isExpired, ApprovalOutcomeMeaning? outcome)
        {
            Counts = counts;
            IsOpen = isOpen;
            Codes = codes;
            Flagged = flagged;
            IsExpired = isExpired;
            Outcome = outcome;
        }

        // Passes every test and its requirement has started counting.
        public bool Counts { get; }

        // Pending and not expired yet.
        public bool IsOpen { get; }

        public List<string> Codes { get; }

        // Counts, with an "allow and flag" separation warning.
        public bool Flagged { get; }

        public bool IsExpired { get; }

        public ApprovalOutcomeMeaning? Outcome { get; }

        public static Judgement Open()
        {
            return new Judgement(false, true, new List<string>(), false, false, ApprovalOutcomeMeaning.Pending);
        }

        public static Judgement NotCounted(string code, bool expired = false)
        {
            return new Judgement(false, false, new List<string> { code }, false, expired, null);
        }

        public static Judgement Waiting()
        {
            return new Judgement(false, false, new List<string> { ReasonCodes.ApprovalSequenceWaiting }, false, false, null);
        }
    }
}
