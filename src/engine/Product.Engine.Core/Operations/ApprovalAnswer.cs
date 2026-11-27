using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;
using Product.Engine.Core.Snapshots;

namespace Product.Engine.Core.Operations;

// The Approval Record a person answers, as the adapter fetched it (O10): its state, what it asks for and when it expires.
public sealed class ApprovalAnswerRecord
{
    public ApprovalAnswerRecord(
        ApprovalOutcomeMeaning outcome,
        string? approvalRuleKey,
        string? decisionRightKey,
        string? requiredRoleKey,
        string? requiredOwnerRoleKey,
        bool isConsultation = false,
        DateTime? expiresOnUtc = null,
        string? askedPrincipalId = null)
    {
        if (approvalRuleKey == null && decisionRightKey == null)
        {
            throw new ArgumentException("An Approval Record names the approval rule or the decision right that asked for it.", nameof(approvalRuleKey));
        }

        if (requiredRoleKey != null && requiredOwnerRoleKey != null)
        {
            throw new ArgumentException("An Approval Record asks for at most one of a role and an owner role.", nameof(requiredRoleKey));
        }

        if (requiredRoleKey == null && requiredOwnerRoleKey == null && string.IsNullOrEmpty(askedPrincipalId))
        {
            throw new ArgumentException("An Approval Record asks for a role, an owner role or a person by name.", nameof(requiredRoleKey));
        }

        if (expiresOnUtc.HasValue && expiresOnUtc.Value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Expires On is a UTC date-time.", nameof(expiresOnUtc));
        }

        Outcome = outcome;
        ApprovalRuleKey = approvalRuleKey;
        DecisionRightKey = decisionRightKey;
        RequiredRoleKey = requiredRoleKey;
        RequiredOwnerRoleKey = requiredOwnerRoleKey;
        IsConsultation = isConsultation;
        ExpiresOnUtc = expiresOnUtc;
        AskedPrincipalId = string.IsNullOrEmpty(askedPrincipalId) ? null : askedPrincipalId;
    }

    // O10 Outcome Meaning.
    public ApprovalOutcomeMeaning Outcome { get; }

    // The approval rule (C32) that asked, or null for the accountable holder's card.
    public string? ApprovalRuleKey { get; }

    // The decision right (C31) of the accountable holder's card.
    public string? DecisionRightKey { get; }

    // The requirement the record belongs to, as OP-03 groups records: the rule key, else the decision right key.
    public string RequirementKey => ApprovalRuleKey ?? DecisionRightKey!;

    public string? RequiredRoleKey { get; }

    public string? RequiredOwnerRoleKey { get; }

    public bool IsConsultation { get; }

    public DateTime? ExpiresOnUtc { get; }

    // O10 Asked Principal: the one person the record asks by name (document 25, B25-14), such as the successor an ownership
    // decision names, or a portfolio's Accountable Principal when no owner role holder is left. Only that person answers it.
    public string? AskedPrincipalId { get; }

    internal static readonly string[] WireFields =
    {
        "outcome", "approvalRuleKey", "decisionRightKey", "requiredRoleKey", "requiredOwnerRoleKey", "askedPrincipalId", "isConsultation", "expiresOnUtc",
    };

    internal static ApprovalAnswerRecord FromJson(WireObject wire)
    {
        string? ruleKey = wire.OptionalKey("approvalRuleKey");
        string? rightKey = wire.OptionalKey("decisionRightKey");
        if (ruleKey == null && rightKey == null)
        {
            throw OperationStop.Input(ReasonCodes.InputRequired, wire.Where + " names neither approvalRuleKey nor decisionRightKey: an Approval Record names what asked for it.");
        }

        string? roleKey = wire.OptionalKey("requiredRoleKey");
        string? ownerRoleKey = wire.OptionalKey("requiredOwnerRoleKey");
        string? asked = wire.OptionalString("askedPrincipalId");
        if ((roleKey != null && ownerRoleKey != null) || (roleKey == null && ownerRoleKey == null && asked == null))
        {
            throw OperationStop.Input(ReasonCodes.InputMalformed, wire.Where + " must name exactly one of requiredRoleKey and requiredOwnerRoleKey, or askedPrincipalId.");
        }

        return new ApprovalAnswerRecord(
            wire.Contract("outcome", EngineContract.ApprovalOutcomeMeanings),
            ruleKey,
            rightKey,
            roleKey,
            ownerRoleKey,
            wire.Bool("isConsultation", false),
            wire.OptionalUtc("expiresOnUtc"),
            asked);
    }
}

// An earlier answer on the same decision, for the rule that one person counts once within a requirement.
public sealed class ApprovalAnswerEarlier
{
    public ApprovalAnswerEarlier(string approvalRecordId, string ruleKey, string? responderPrincipalId, ApprovalOutcomeMeaning outcome, bool countsTowardRequirement)
    {
        if (string.IsNullOrEmpty(approvalRecordId))
        {
            throw new ArgumentException("An earlier answer names its Approval Record.", nameof(approvalRecordId));
        }

        ApprovalRecordId = approvalRecordId;
        RuleKey = ruleKey ?? throw new ArgumentNullException(nameof(ruleKey));
        ResponderPrincipalId = string.IsNullOrEmpty(responderPrincipalId) ? null : responderPrincipalId;
        Outcome = outcome;
        CountsTowardRequirement = countsTowardRequirement;
    }

    public string ApprovalRecordId { get; }

    // The requirement it belongs to: the approval rule key, else the decision right key.
    public string RuleKey { get; }

    public string? ResponderPrincipalId { get; }

    public ApprovalOutcomeMeaning Outcome { get; }

    // O10 Counts Toward Requirement, as the engine last computed it.
    public bool CountsTowardRequirement { get; }

    internal static readonly string[] WireFields = { "approvalRecordId", "ruleKey", "responderPrincipalId", "outcome", "countsTowardRequirement" };

    internal static ApprovalAnswerEarlier FromJson(WireObject wire)
    {
        return new ApprovalAnswerEarlier(
            wire.RequiredString("approvalRecordId"),
            wire.RequiredKey("ruleKey"),
            wire.OptionalString("responderPrincipalId"),
            wire.Contract("outcome", EngineContract.ApprovalOutcomeMeanings),
            wire.Bool("countsTowardRequirement", false));
    }
}

// The input of the approval answer check (D-38): what the person sends (the record, approve or reject, and whether they
// typed a reason) and what the adapter fetches (the record, the decision's status and scope, the parties the separation
// rules compare, the owner roles the person holds now on the decision's application, and the decision's other answers).
// The responder is the context's actor: the signed-in person, never a value in the request.
public sealed class ApprovalAnswerRequest
{
    public const string Approve = "approve";
    public const string Reject = "reject";

    public ApprovalAnswerRequest(
        string approvalRecordId,
        string answer,
        bool hasReason,
        ApprovalAnswerRecord record,
        DecisionStatusMeaning decisionStatus,
        string decisionTypeKey,
        string? tierKey = null,
        string? targetStageKey = null,
        SeparationFacts? parties = null,
        IEnumerable<string>? acceptedExceptionDecisionTypeKeys = null,
        IEnumerable<string>? ownerRoleKeys = null,
        IEnumerable<ApprovalAnswerEarlier>? earlierAnswers = null)
    {
        if (string.IsNullOrEmpty(approvalRecordId))
        {
            throw new ArgumentException("The answer names its Approval Record.", nameof(approvalRecordId));
        }

        if (answer != Approve && answer != Reject)
        {
            throw new ArgumentException("The answer is approve or reject.", nameof(answer));
        }

        if (!ConfigurationKey.IsValid(decisionTypeKey))
        {
            throw new ArgumentException("The decision type key must use the configuration key format.", nameof(decisionTypeKey));
        }

        ApprovalRecordId = approvalRecordId;
        Answer = answer;
        HasReason = hasReason;
        Record = record ?? throw new ArgumentNullException(nameof(record));
        DecisionStatus = decisionStatus;
        DecisionTypeKey = decisionTypeKey;
        TierKey = tierKey;
        TargetStageKey = targetStageKey;
        Parties = parties ?? new SeparationFacts();
        AcceptedExceptionDecisionTypeKeys = new ReadOnlyCollection<string>((acceptedExceptionDecisionTypeKeys ?? Enumerable.Empty<string>()).ToList());
        OwnerRoleKeys = new ReadOnlyCollection<string>((ownerRoleKeys ?? Enumerable.Empty<string>()).Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList());
        EarlierAnswers = new ReadOnlyCollection<ApprovalAnswerEarlier>((earlierAnswers ?? Enumerable.Empty<ApprovalAnswerEarlier>()).ToList());
    }

    public string ApprovalRecordId { get; }

    // approve or reject.
    public string Answer { get; }

    // True when the person typed a reason.
    public bool HasReason { get; }

    public ApprovalAnswerRecord Record { get; }

    public DecisionStatusMeaning DecisionStatus { get; }

    public string DecisionTypeKey { get; }

    public string? TierKey { get; }

    public string? TargetStageKey { get; }

    public SeparationFacts Parties { get; }

    public IReadOnlyList<string> AcceptedExceptionDecisionTypeKeys { get; }

    // The owner roles (C28) the person holds now on the decision's application, a deputy's included.
    public IReadOnlyList<string> OwnerRoleKeys { get; }

    // The decision's other answered records.
    public IReadOnlyList<ApprovalAnswerEarlier> EarlierAnswers { get; }

    public static ApprovalAnswerRequest FromJson(JsonTreeNode input)
    {
        var wire = new WireObject(
            input,
            "input",
            "approvalRecordId",
            "answer",
            "hasReason",
            "record",
            "decisionStatus",
            "scope",
            "parties",
            "acceptedExceptionDecisionTypeKeys",
            "ownerRoleKeys",
            "earlierAnswers");
        string answer = wire.RequiredString("answer");
        if (answer != Approve && answer != Reject)
        {
            throw OperationStop.Input(ReasonCodes.InputContractValueUnknown, "input.answer is \"" + answer + "\", which is not one of approve, reject.", "answer");
        }

        WireObject record = wire.OptionalObject("record", ApprovalAnswerRecord.WireFields)
            ?? throw OperationStop.Input(ReasonCodes.InputRequired, "input.record is required: the Approval Record the person answers.");
        WireObject scope = wire.OptionalObject("scope", "decisionTypeKey", "tierKey", "targetStageKey")
            ?? throw OperationStop.Input(ReasonCodes.InputRequired, "input.scope is required.");
        return new ApprovalAnswerRequest(
            wire.RequiredString("approvalRecordId"),
            answer,
            wire.Bool("hasReason", false),
            ApprovalAnswerRecord.FromJson(record),
            wire.Contract("decisionStatus", EngineContract.DecisionStatusMeanings),
            scope.RequiredKey("decisionTypeKey"),
            scope.OptionalKey("tierKey"),
            scope.OptionalKey("targetStageKey"),
            SeparationFacts.FromJson(wire.OptionalObject("parties", "proposerPrincipalId", "instructingAdminPrincipalId", "deployerPrincipalIds", "ownerRoleHolders", "roleHolders")),
            wire.Keys("acceptedExceptionDecisionTypeKeys"),
            wire.Keys("ownerRoleKeys"),
            wire.Objects("earlierAnswers", required: false, ApprovalAnswerEarlier.WireFields).Select(ApprovalAnswerEarlier.FromJson));
    }
}

// The answer of the check: whether the person may give this answer, and what the engine records with it.
public sealed class ApprovalAnswerResult : IOperationPayload
{
    public ApprovalAnswerResult(
        bool allowed,
        string approvalRecordId,
        string requirementKey,
        string answer,
        string? heldRoleKey,
        string? heldOwnerRoleKey,
        bool isConsultation,
        bool requiresTypedReason,
        bool flagged,
        bool askedByName = false)
    {
        Allowed = allowed;
        ApprovalRecordId = approvalRecordId;
        RequirementKey = requirementKey;
        Answer = answer;
        HeldRoleKey = heldRoleKey;
        HeldOwnerRoleKey = heldOwnerRoleKey;
        IsConsultation = isConsultation;
        RequiresTypedReason = requiresTypedReason;
        Flagged = flagged;
        AskedByName = askedByName;
    }

    public bool Allowed { get; }

    public string ApprovalRecordId { get; }

    public string RequirementKey { get; }

    // approve or reject.
    public string Answer { get; }

    // The product role through which the person answers, when the record asks for one and the person holds it.
    public string? HeldRoleKey { get; }

    // The owner role through which the person answers, when the record asks for one and the person holds it now.
    public string? HeldOwnerRoleKey { get; }

    // A consultation answer is recorded and shown, and never counts (OP-03).
    public bool IsConsultation { get; }

    public bool RequiresTypedReason { get; }

    // True when an "allow and flag" separation rule names the person: the answer counts, with a warning.
    public bool Flagged { get; }

    // True when the record asks the person by name (Asked Principal), not through a role they hold.
    public bool AskedByName { get; }

    public JsonTreeNode ToJson()
    {
        return new WireWriter()
            .Add("allowed", Allowed)
            .Add("approvalRecordId", ApprovalRecordId)
            .Add("requirementKey", RequirementKey)
            .Add("answer", Answer)
            .Add("heldRoleKey", HeldRoleKey)
            .Add("heldOwnerRoleKey", HeldOwnerRoleKey)
            .Add("isConsultation", IsConsultation)
            .Add("requiresTypedReason", RequiresTypedReason)
            .Add("flagged", Flagged)
            .Add("askedByName", AskedByName ? JsonTreeNode.True : null)
            .Build();
    }
}

// The approval answer check (D-38; document 26, AN-01 and AN-02 as D-38 rewrites them). A person answers their own
// Approval Record on the product's decision page, and the token proves who answered: the responder is the context's
// actor. Before anything is written, the check refuses every answer the person may not give, so a refused answer leaves
// the record waiting and writes nothing. In this order:
//  1. the actor is a person signed in as a user (approval.answer-person-only): an AI client, an agent, a pipeline or the
//     approval runner never answers here, because AI never approves (D-02, D-10);
//  2. the decision is still proposed (approval.answer-closed);
//  3. the record is still pending (approval.answer-already-given for an answer, approval.answer-expired, or
//     approval.answer-closed for a cancelled record) and before its Expires On (approval.answer-expired);
//  4. the person holds the role the record asks for, now, or the owner role on the decision's application, now, a
//     deputy's included (approval.answer-not-asked; document 26, AN-03 and RA-02); a record that asks a person by name
//     (Asked Principal; document 25, B25-14) is answered by that person only, whatever roles they hold;
//  5. the person's earlier approval does not already count for the same requirement (approval.answer-already-given):
//     OP-03 counts one person once, so a second approval would not count;
//  6. no separation rule (C40) in force keeps the person's answer from counting (approval.answer-separation), judged
//     exactly as OP-03 judges it; an "allow and flag" rule lets the answer through with a warning. A consultation is
//     never counted, so separation does not apply to it;
//  7. a typed reason is given to reject, and to approve where the approval rule requires one (AN-08), else invalid input
//     (approval.answer-reason-required).
// Outcomes: allowed, refused, invalid-input, unavailable. The engine then writes the answer through OP-08 (G-06, the
// person path) and recomputes the decision with OP-03, exactly as for the approval runner's answer.
public static class ApprovalAnswerOperation
{
    public const string Allowed = "allowed";

    public static EvaluationResult<ApprovalAnswerResult?> Evaluate(ConfigurationSnapshot snapshot, EvaluationContext context, ApprovalAnswerRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return OperationShell.Run(context, snapshot, needsSnapshot: true, () => Answer(snapshot, context, request));
    }

    public static EvaluationResult<ApprovalAnswerResult?> Evaluate(ConfigurationSnapshot snapshot, EvaluationContext context, JsonTreeNode input)
    {
        return OperationShell.Run(context, snapshot, needsSnapshot: true, () => Answer(snapshot, context, ApprovalAnswerRequest.FromJson(input)));
    }

    private static OperationAnswer<ApprovalAnswerResult> Answer(ConfigurationSnapshot snapshot, EvaluationContext context, ApprovalAnswerRequest request)
    {
        ActorInfo actor = context.Actor;
        ApprovalAnswerRecord record = request.Record;
        string id = request.ApprovalRecordId;
        var trace = new List<RuleTrace>();
        SnapshotRead.RequireExisting(snapshot, "decision-type", request.DecisionTypeKey, "input.scope.decisionTypeKey");

        // The rule's typed reason, under the version the decision was stamped with (the snapshot given). The accountable
        // holder's card has no rule, so it asks for none.
        bool typedReason = false;
        bool proposedOwner = false;
        string? ownerSubject = null;
        if (record.ApprovalRuleKey != null)
        {
            SnapshotRow rule = SnapshotRead.RequireExisting(snapshot, "approval-rule", record.ApprovalRuleKey, "input.record.approvalRuleKey");
            typedReason = SnapshotRead.Flag(rule, "requiresTypedReason");
            proposedOwner = SnapshotRead.Flag(rule, "approverProposedOwners");
            ownerSubject = SnapshotRead.Text(rule, "ownerSubject");
            trace.Add(new RuleTrace("Approval Rule", rule.KeyPath, RuleTraceRole.Matched));
        }

        bool byName = record.AskedPrincipalId != null;
        ApprovalAnswerResult Result(bool allowed, string? role, string? ownerRole, bool flagged)
        {
            return new ApprovalAnswerResult(allowed, id, record.RequirementKey, request.Answer, role, ownerRole, record.IsConsultation, typedReason, flagged, byName && allowed);
        }

        OperationAnswer<ApprovalAnswerResult> Refuse(string code, string message, string? entity, string? reference)
        {
            return new OperationAnswer<ApprovalAnswerResult>(OperationOutcomes.Refused, Result(false, null, null, false), new[] { new Reason(code, ReasonSeverity.Error, message, entity, reference) }, trace);
        }

        // 1. A person signed in as a user. The token decides who answered; nothing in the request names the responder.
        if (!ContractFacts.IsPerson(actor.ActorKind) || !ContractFacts.CanApprove(actor.PrincipalKind))
        {
            string who = ContractFacts.IsAiActor(actor.ActorKind)
                ? "an AI client or an agent, and AI never approves (D-02, D-10)"
                : !ContractFacts.IsPerson(actor.ActorKind)
                    ? EngineContract.ActorKindMeanings.ToWire(actor.ActorKind) + ", not a person"
                    : "a " + EngineContract.PrincipalKinds.ToWire(actor.PrincipalKind) + ", not a user";
            return Refuse(
                ReasonCodes.ApprovalAnswerPersonOnly,
                "Only a person signed in as a user answers an approval, on the product's decision page. The caller is " + who + ". Nothing is recorded, and the request keeps waiting for a person.",
                "Approval Record",
                id);
        }

        // 2. The decision is still proposed.
        if (request.DecisionStatus != DecisionStatusMeaning.Proposed)
        {
            return Refuse(
                ReasonCodes.ApprovalAnswerClosed,
                "The decision is " + EngineContract.DecisionStatusMeanings.ToWire(request.DecisionStatus) + ", so its approval requests take no more answers.",
                "Decision",
                null);
        }

        // 3. The record is still pending, and before its Expires On.
        switch (record.Outcome)
        {
            case ApprovalOutcomeMeaning.Approved:
            case ApprovalOutcomeMeaning.Rejected:
            case ApprovalOutcomeMeaning.RecommendationOnly:
                return Refuse(
                    ReasonCodes.ApprovalAnswerAlreadyGiven,
                    "This approval request already has its answer (" + EngineContract.ApprovalOutcomeMeanings.ToWire(record.Outcome) + "). An answer is written once and never changes.",
                    "Approval Record",
                    id);
            case ApprovalOutcomeMeaning.Expired:
                return Refuse(ReasonCodes.ApprovalAnswerExpired, "This approval request expired, so it takes no answer. The engine asks again with a new request; answer that one.", "Approval Record", id);
            case ApprovalOutcomeMeaning.Cancelled:
                return Refuse(ReasonCodes.ApprovalAnswerClosed, "This approval request was canceled, so it takes no answer.", "Approval Record", id);
        }

        if (record.ExpiresOnUtc.HasValue && context.AsOfUtc >= record.ExpiresOnUtc.Value)
        {
            return Refuse(
                ReasonCodes.ApprovalAnswerExpired,
                "This approval request expired on " + CanonicalJson.FormatUtc(record.ExpiresOnUtc.Value) + ", so it takes no answer. The engine asks again with a new request; answer that one.",
                "Approval Record",
                id);
        }

        // 4. The person the record asks by name, or else the role or owner role the record asks for, held now.
        string? heldRole = null;
        string? heldOwnerRole = null;
        if (byName)
        {
            if (!string.Equals(ApprovalStatusOperation.PrincipalKey(record.AskedPrincipalId!), ApprovalStatusOperation.PrincipalKey(actor.PrincipalId), StringComparison.Ordinal))
            {
                return Refuse(
                    ReasonCodes.ApprovalAnswerNotAsked,
                    "This approval request asks one person by name, and the person signed in is someone else. Nothing is recorded.",
                    "Approval Record",
                    id);
            }
        }
        else if (ownerSubject != null && ownerSubject != "business-application")
        {
            // A rule whose owner sits on a data domain, its parent or a data location is answered only by the person it
            // names, never through the owner roles the person holds on the application (security review, major).
            return Refuse(
                ReasonCodes.ApprovalAnswerNotAsked,
                "This approval request asks the data owner of a data domain or location, and it names nobody, because that role has no holder now. Nobody answers it through the application's owner roles. Propose a data owner first.",
                "Approval Rule",
                record.ApprovalRuleKey);
        }
        else
        {
            heldRole = record.RequiredRoleKey != null && actor.HoldsRole(record.RequiredRoleKey) ? record.RequiredRoleKey : null;
            heldOwnerRole = record.RequiredOwnerRoleKey != null && request.OwnerRoleKeys.Contains(record.RequiredOwnerRoleKey, StringComparer.Ordinal) ? record.RequiredOwnerRoleKey : null;
        }

        if (!byName && heldRole == null && heldOwnerRole == null)
        {
            string asked = record.RequiredRoleKey != null
                ? "the role " + SnapshotRead.Quote(record.RequiredRoleKey) + ", which comes only through its Entra group"
                : "the owner role " + SnapshotRead.Quote(record.RequiredOwnerRoleKey!) + " on the decision's application, as its holder or deputy";
            return Refuse(
                ReasonCodes.ApprovalAnswerNotAsked,
                "This approval request asks for " + asked + ", and the person signed in does not hold it now. Nothing is recorded.",
                record.RequiredRoleKey != null ? "Role" : "Owner Role",
                record.RequiredRoleKey ?? record.RequiredOwnerRoleKey);
        }

        // 5. One person counts once within a requirement. On a data-use decision one person counts once across all of
        // its counting requirements, so the data owner who also holds the security reviewer role cannot meet the
        // "restricted data to AI needs two approvals" rule alone (security review, major; D-41).
        string person = ApprovalStatusOperation.PrincipalKey(actor.PrincipalId);
        bool oncePerDecision = string.Equals(request.DecisionTypeKey, EngineKeys.DataUseDecisionType, StringComparison.Ordinal);
        ApprovalAnswerEarlier? counted = record.IsConsultation
            ? null
            : request.EarlierAnswers.FirstOrDefault(a =>
                !string.Equals(a.ApprovalRecordId, id, StringComparison.Ordinal)
                && (oncePerDecision || string.Equals(a.RuleKey, record.RequirementKey, StringComparison.Ordinal))
                && a.Outcome == ApprovalOutcomeMeaning.Approved
                && a.CountsTowardRequirement
                && a.ResponderPrincipalId != null
                && string.Equals(ApprovalStatusOperation.PrincipalKey(a.ResponderPrincipalId), person, StringComparison.Ordinal));
        if (counted != null)
        {
            return Refuse(
                ReasonCodes.ApprovalAnswerAlreadyGiven,
                oncePerDecision
                    ? "You already approved this decision under another of its requests, and that approval counts. On a data-use decision one person counts once, so another person answers this request."
                    : "You already approved this requirement, and that approval counts. One person counts once, so another person answers this request.",
                "Approval Record",
                id);
        }

        // 6. The separation rules, as OP-03 judges a counting answer. A proposed owner's own yes is consent, so no
        // separation rule applies to it (document 25, B25-14).
        bool flagged = false;
        var reasons = new List<Reason>();
        if (!record.IsConsultation && !proposedOwner)
        {
            var scope = new ApprovalStatusRequest(
                Array.Empty<RequiredApproval>(),
                Array.Empty<ApprovalFact>(),
                request.DecisionTypeKey,
                request.TierKey,
                request.TargetStageKey,
                request.Parties,
                request.AcceptedExceptionDecisionTypeKeys);
            SeparationVerdict separation = ApprovalStatusOperation.Separation(snapshot, scope, actor.PrincipalId, trace);
            if (separation.Refuses)
            {
                // The message names each rule by its label, as a person reads it (wave 4b review): "The separation rule No
                // self-approval at tier 3 keeps you from answering...". The key stays in the reason's Ref.
                bool exception = separation.Codes.Contains(ReasonCodes.ApprovalSeparationExceptionMissing, StringComparer.Ordinal) && !separation.Codes.Contains(ReasonCodes.ApprovalSeparationRefused, StringComparer.Ordinal);
                string[] labels = separation.RuleKeys.Select(key => SnapshotRead.Find(snapshot, "separation-rule", key) is SnapshotRow rule && SnapshotRead.Text(rule, "label") is string label ? label : key).ToArray();
                return Refuse(
                    ReasonCodes.ApprovalAnswerSeparation,
                    (labels.Length == 1 ? "The separation rule " + labels[0] : "The separation rules " + string.Join(" and ", labels)) + " keeps you from answering this approval request"
                    + (exception ? " unless an accepted exception decision is on record" : string.Empty) + ", so your answer would not count. Another person who holds the role answers it.",
                    "Separation Rule",
                    separation.RuleKeys.FirstOrDefault());
            }

            if (separation.Flagged)
            {
                flagged = true;
                reasons.Add(ReasonCatalog.Create(ReasonCodes.ApprovalSeparationFlagged, "Approval Record", id));
            }
        }

        // 7. The typed reason.
        bool reasonRequired = request.Answer == ApprovalAnswerRequest.Reject || typedReason;
        if (reasonRequired && !request.HasReason)
        {
            return new OperationAnswer<ApprovalAnswerResult>(
                OperationOutcomes.InvalidInput,
                Result(false, heldRole, heldOwnerRole, flagged),
                new[]
                {
                    new Reason(
                        ReasonCodes.ApprovalAnswerReasonRequired,
                        ReasonSeverity.Error,
                        request.Answer == ApprovalAnswerRequest.Reject ? "A rejection takes a typed reason." : "Approval Rule " + SnapshotRead.Quote(record.ApprovalRuleKey!) + " asks for a typed reason with the answer.",
                        null,
                        "reason"),
                },
                trace);
        }

        if (record.IsConsultation)
        {
            reasons.Add(ReasonCatalog.Create(ReasonCodes.ApprovalConsultationNotCounted, "Approval Record", id));
        }

        return new OperationAnswer<ApprovalAnswerResult>(Allowed, Result(true, heldRole, heldOwnerRole, flagged), reasons, trace);
    }
}
