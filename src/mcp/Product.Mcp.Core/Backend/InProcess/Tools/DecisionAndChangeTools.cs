using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Operations;
using Product.Engine.Core.Snapshots;
using Product.Engine.Runtime;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Json;

namespace Product.Mcp.Core.Backend.InProcess;

// Decisions toolset (tools 17 to 21 and 47) and change toolset (tools 22 to 26) of document 24, sections 6.5 and 6.6.
internal static partial class ToolHandlers
{
    private static readonly Dictionary<string, (string Entity, string Column)> DecisionSubjects = new(StringComparer.Ordinal)
    {
        ["business-application"] = ("business-application", "subjectApplication"),
        ["intake-request"] = ("intake-request", "subjectIntakeRequest"),
        ["change-request"] = ("change-request", "subjectChangeRequest"),
        ["deployment-record"] = ("deployment-record", "subjectDeploymentRecord"),
        ["technical-component"] = ("technical-component", "subjectComponent"),
        ["claim"] = ("claim", "subjectClaim"),
        ["configuration-draft"] = ("configuration-draft", "subjectConfigurationDraft"),
        ["data-use"] = ("data-use", "subjectDataUse"),
        ["data-mapping"] = ("data-mapping", "subjectDataMapping"),
    };

    // ---- 17. save_decision_proposal ----

    private static JsonTreeNode SaveDecisionProposal(ToolScope s)
    {
        s.RequireAiState("proposed");
        string type = s.RequireKey("decision-type", s.Text("decisionTypeKey"), "decisionTypeKey").KeyPath;
        if (type == EngineKeys.ConfigurationChangeDecisionType || type == "gate-policy-change")
        {
            throw s.Invalid("decisionTypeKey", "Configuration and gate policy decisions start with propose_configuration_change, then submit_record.");
        }

        string outcome = s.RequireText("proposedOutcomeKey");
        if (s.ConfigRow("decision-outcome", type + "/" + outcome) is not SnapshotRow outcomeRow || !outcomeRow.IsActive)
        {
            throw s.Invalid("proposedOutcomeKey", "proposedOutcomeKey \"" + outcome + "\" is not an active outcome of the " + type + " decision type.");
        }

        RequireListValue(s, "confidence", s.Text("confidenceKey"), "confidenceKey");
        JsonTreeNode? proposedChanges = ProposedSubjectChanges(s, type);
        (string? exceptionStandard, string? exceptionReviewOn) = ExceptionFields(s, type);
        string subjectKind = s.RequireText("subjectKind");
        string subjectId = s.RequireText("subjectId");
        if (subjectKind != "business-application" && subjectKind != "review" && ChangesTheApplication(s, type))
        {
            // Security review 2026-11-24 (document 24, B24-14): a decision that sets an application's tier, autonomy
            // level, owners or stage is about the application (or a review of it), so its approvals and separation rules
            // are those of the application's own scope, never those a request's answers give.
            throw s.Invalid("subjectKind", "A " + s.Label("decision-type", type).ToLowerInvariant() + " decision changes the application itself, so its subject is the application (business-application) or a review of it, not " + subjectKind + ".");
        }

        RequireDataSubjectFits(s, type, subjectKind, subjectId);
        var subjectColumns = new List<(string, JsonTreeNode?)>();
        string? applicationId;
        StoredRow? subjectReview = null;
        if (subjectKind == "review")
        {
            // O09 has no review column: the decision points at the review's application, and the review names the
            // decision as its Outcome Decision (O21), set below once the decision exists.
            subjectReview = s.RequireRow("review", subjectId, "subjectId");
            applicationId = subjectReview.Text("businessApplication");
            subjectColumns.Add(("subjectApplication", Js.OptStr(applicationId)));
        }
        else
        {
            (string entity, string column) = DecisionSubjects[subjectKind];
            StoredRow subject = s.RequireRow(entity, subjectId, "subjectId");
            if (entity == "intake-request" && !CanReadIntake(s, subject))
            {
                throw s.NotFound("Intake Request", subjectId, "subjectId");
            }

            subjectColumns.Add((column, JsonTreeNode.FromString(subjectId)));
            applicationId = entity == "business-application" ? subjectId : ApplicationOf(s, subject);
            if (applicationId != null && entity != "business-application")
            {
                subjectColumns.Add(("subjectApplication", JsonTreeNode.FromString(applicationId)));
            }
        }

        foreach (string link in new[] { "supersedesDecisionId", "amendsDecisionId" })
        {
            if (s.Text(link) is string other)
            {
                s.RequireRow("decision", other, link);
            }
        }

        if (type == EngineKeys.TierDecisionType && outcome == EngineKeys.TierConfirmOutcome && proposedChanges == null && applicationId != null
            && ComputedTierOf(s, applicationId) is string computed)
        {
            proposedChanges = Js.Obj(("tierKey", JsonTreeNode.FromString(computed)));
        }

        var columns = new List<(string, JsonTreeNode?)>(subjectColumns)
        {
            ("decisionType", JsonTreeNode.FromString(type)),
            ("subjectKind", JsonTreeNode.FromString(subjectKind)),
            ("statement", JsonTreeNode.FromString(s.RequireText("statement"))),
            ("context", JsonTreeNode.FromString(s.RequireText("context"))),
            ("optionsConsidered", s.Items("optionsConsidered").Count == 0 ? null : JsonTreeNode.FromString(Js.Write(Arr(s.Items("optionsConsidered"))))),
            ("outcome", JsonTreeNode.FromString(outcome)),
            ("reason", JsonTreeNode.FromString(s.RequireText("reason"))),
            ("confidence", Js.OptStr(s.Text("confidenceKey"))),
            ("supersedes", Js.OptStr(s.Text("supersedesDecisionId"))),
            ("amends", Js.OptStr(s.Text("amendsDecisionId"))),
            ("proposedChangesJson", proposedChanges ?? (s.Text("decisionId") != null && (type == EngineKeys.TierDecisionType || type == EngineKeys.AutonomyLevelDecisionType) ? JsonTreeNode.Null : null)),
            ("exceptionStandard", Js.OptStr(exceptionStandard)),
            ("exceptionReviewOn", Js.OptStr(exceptionReviewOn)),

            // The slide-back signal this decision acts on (document 24, B24-22): the Measures page links the signal to it.
            ("slideBackSignal", Js.OptStr(s.Text("slideBackSignalKey"))),
        };

        ComposedWrite write;
        string? decisionId = s.Text("decisionId");
        if (decisionId == null)
        {
            columns.Add(("status", JsonTreeNode.FromString(s.StatusFor("decision", "proposed"))));
            write = s.Create("decision", Js.Obj(columns.ToArray()));
        }
        else
        {
            StoredRow decision = s.RequireRow("decision", decisionId, "decisionId");
            if (decision.Text("statusMeaning") != "proposed" || !Same(decision.Text("createdByPrincipal"), s.PersonId))
            {
                throw s.Refused(McpErrorCodes.RuleViolation, "Only a proposed decision you created can be edited.");
            }

            if (s.Query("approval-record", ("decision", decisionId)).Count > 0)
            {
                throw s.Refused(McpErrorCodes.RuleViolation, "People are already asked about the decision. Withdraw it and propose a new one.");
            }

            write = s.Update("decision", decisionId, Js.Obj(columns.ToArray()));
        }

        // The review records its outcome as this decision (O21 Outcome Decision), unless an outcome decision it names is
        // already accepted: then the new proposal is about the review too, and the accepted one stays the outcome.
        if (subjectReview != null && !s.Call.IsDryRun)
        {
            string? named = subjectReview.Text("outcomeDecision");
            StoredRow? current = named == null ? null : s.Rows.Get("decision", named);
            if (current == null || (current.Id != write.RowId && current.Text("statusMeaning") != "accepted"))
            {
                s.Update("review", subjectReview.Id, Js.Obj(("outcomeDecision", JsonTreeNode.FromString(write.RowId))));
            }
        }

        // Who may propose (document 24, tool 17; Tool Policy row save-decision-proposal): the engine checks the person
        // behind the call holds a responsible, accountable or consulted Decision Right (C31) for the decision type in the
        // decision's scope, on every entry (the AI client's and the person's). A refusal ends the call; nothing commits.
        JsonTreeNode proposalRight = s.Call.RequireProposalRight(write.RowId);

        var evidence = new List<string>();
        foreach (JsonTreeNode item in s.Items("evidence"))
        {
            evidence.Add(Evidence(s, "decision", "subjectDecision", write.RowId, item));
        }

        JsonTreeNode data = ToolScope.Merge(
            s.WriteResult("decision", write),
            ("decider", Decider(s, type, applicationId, proposalRight)),
            ("approvalsNeeded", Arr(ApprovalsNeeded(s, write.RowId, type))),
            ("evidenceLinkIds", Ids(s.Call.IsDryRun ? Array.Empty<string>() : evidence.ToArray())));
        string? id = Js.Text(data, "recordId");
        return s.Answer(data, "The decision is recorded as proposed. Nobody is asked yet: submit it, and the people it names answer on its decision page.", id == null ? null : s.RecordUrl("decision", id),
            id == null ? Array.Empty<NextStep>() : new[] { ToolScope.Next("submit_record", ("subjectKind", JsonTreeNode.FromString("decision")), ("subjectId", JsonTreeNode.FromString(id))) });
    }

    // The data taxonomy's subjects (docs/architecture/data-taxonomy.md, The decision state; slice 3): a data use takes a data-use decision (one per use) or a
    // data-retention decision (one per write use at retirement); a data mapping takes a data-use decision about a copy. No
    // other decision type takes them, and a data-retention decision is about a data use that writes. The engine refuses
    // an allow that the governing rule forbids (G-20).
    private static void RequireDataSubjectFits(ToolScope s, string type, string subjectKind, string subjectId)
    {
        bool dataSubject = subjectKind == EngineKeys.DataUseSubjectKind || subjectKind == EngineKeys.DataMappingSubjectKind;
        if (type == EngineKeys.DataRetentionDecisionType && subjectKind != EngineKeys.DataUseSubjectKind)
        {
            throw s.Invalid("subjectKind", "A data-retention decision is about one data use that writes (subjectKind data-use).");
        }

        if (!dataSubject)
        {
            if (type == EngineKeys.DataUseDecisionType && subjectKind == "business-application" && new Product.Engine.Core.Operations.DataTree(s.Snapshot).Mode == DataTaxonomyMode.DomainsRequired)
            {
                // domains-required (docs/architecture/data-taxonomy.md, Entities): every data-use decision is about a declared use.
                throw s.Invalid("subjectKind", "This install requires data domains, so a data-use decision is about one declared data use (subjectKind data-use), not about the whole application.");
            }

            return;
        }

        if (type != EngineKeys.DataUseDecisionType && type != EngineKeys.DataRetentionDecisionType)
        {
            throw s.Invalid("decisionTypeKey", "A decision about a " + subjectKind.Replace('-', ' ') + " is a data-use" + (subjectKind == EngineKeys.DataUseSubjectKind ? " or a data-retention" : string.Empty) + " decision, not " + type + ".");
        }

        if (subjectKind == EngineKeys.DataMappingSubjectKind && type != EngineKeys.DataUseDecisionType)
        {
            throw s.Invalid("decisionTypeKey", "A decision about a data mapping is a data-use decision about the copy.");
        }

        StoredRow row = s.RequireRow(subjectKind, subjectId, "subjectId");
        if (!IsOpenDataRow(row, s))
        {
            throw s.Refused(McpErrorCodes.RuleViolation, "The " + subjectKind.Replace('-', ' ') + " is ended or archived, so no new decision is about it.");
        }

        if (type == EngineKeys.DataRetentionDecisionType && !Writes(row.Text("access")))
        {
            throw s.Invalid("subjectId", "Data use " + (row.Text("recordNumber") ?? row.Id) + " only reads, so no retention decision is about it. Retention is decided for the uses that write.");
        }
    }

    // True for a decision type whose acceptance changes the application itself: tier, autonomy level, ownership, and a
    // type of application decisions (its Default Subject Kind is business-application) with an outcome that moves the
    // application to a Next Lifecycle Stage (go-live, retirement, design). Intake and priority decisions stay about the
    // request: their scope takes the linked application's tier too (document 25, B25-15).
    private static bool ChangesTheApplication(ToolScope s, string type)
    {
        if (type == EngineKeys.TierDecisionType || type == EngineKeys.AutonomyLevelDecisionType || type == EngineKeys.OwnershipDecisionType)
        {
            return true;
        }

        SnapshotRow? row = s.ConfigRow("decision-type", type);
        return Js.Text(row?.Columns, "defaultSubjectKind") is "business-application"
            && s.ActiveRows("decision-outcome").Any(o => o.KeyPath.StartsWith(type + "/", StringComparison.Ordinal) && Js.Text(o.Columns, "nextLifecycleStageKey") != null);
    }

    // The tier a confirm proposes, pinned when the decision is proposed (security review 2026-11-24, B24-14): the tier the
    // latest answers computed for the application (its own, or its intake requests'). The approvers read it on the
    // decision page, and acceptance applies exactly it, whatever answers come later.
    private static string? ComputedTierOf(ToolScope s, string applicationId)
    {
        IEnumerable<StoredRow> assessments = s.Query("tier-assessment", ("businessApplication", applicationId))
            .Concat(s.Query("intake-request", ("businessApplication", applicationId)).SelectMany(i => s.Query("tier-assessment", ("intakeRequest", i.Id))));
        return assessments.Where(a => a.Text("computedTier") != null)
            .OrderBy(a => a.Text("engineCheckedOn") ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(a => a.Id, StringComparer.Ordinal)
            .LastOrDefault()?.Text("computedTier");
    }

    // What a tier or autonomy level decision sets once accepted, in Proposed Changes Json (document 25, B25-8): the tier
    // a person proposes (above or below the computed one), or the autonomy level to agree. Each belongs to its own
    // decision type, and the engine checks the key again when the decision is written and when it is accepted.
    private static JsonTreeNode? ProposedSubjectChanges(ToolScope s, string type)
    {
        string? tier = s.Text("tierKey");
        string? level = s.Text("autonomyLevelKey");
        if (tier != null && type != EngineKeys.TierDecisionType)
        {
            throw s.Invalid("tierKey", "tierKey belongs to a tier decision, not to " + type + ".");
        }

        if (level != null && type != EngineKeys.AutonomyLevelDecisionType)
        {
            throw s.Invalid("autonomyLevelKey", "autonomyLevelKey belongs to an autonomy level decision, not to " + type + ".");
        }

        if (tier != null)
        {
            if (s.ConfigRow("tier", tier) is not SnapshotRow row || !row.IsActive)
            {
                throw s.Invalid("tierKey", "tierKey \"" + tier + "\" is not an active tier.");
            }

            return Js.Obj(("tierKey", JsonTreeNode.FromString(tier)));
        }

        if (level != null)
        {
            RequireListValue(s, EngineKeys.AutonomyLevelList, level, "autonomyLevelKey");
            return Js.Obj(("autonomyLevelKey", JsonTreeNode.FromString(level)));
        }

        return null;
    }

    // What an exception decision records besides its reason (document 23, P04 step 6; document 24, B24-9): the standard it
    // departs from and the date it is looked at again. Both are required for an exception and belong to no other decision
    // type. The review date is today or later: an exception reviewed in the past would already be overdue.
    private static (string? Standard, string? ReviewOn) ExceptionFields(ToolScope s, string type)
    {
        JsonTreeNode? exception = Js.Member(s.Args, "exception");
        bool isException = type == EngineKeys.ExceptionDecisionType;
        if (exception == null)
        {
            if (isException)
            {
                throw s.Invalid("exception", "An exception decision names the standard it departs from and the date to review it (exception.standard and exception.reviewOn).");
            }

            return (null, null);
        }

        if (!isException)
        {
            throw s.Invalid("exception", "exception belongs to an exception decision, not to " + type + ".");
        }

        string standard = Js.Text(exception, "standard") ?? throw s.Invalid("exception.standard", "Name the standard the exception departs from.");
        string reviewOn = Js.Text(exception, "reviewOn") ?? throw s.Invalid("exception.reviewOn", "Give the date the exception is reviewed again.");
        if (string.CompareOrdinal(reviewOn, s.PersonToday) < 0)
        {
            throw s.Invalid("exception.reviewOn", "The review date " + reviewOn + " has passed. An exception is reviewed on a date to come.");
        }

        return (standard.Trim(), reviewOn);
    }

    // The accountable holder of a decision type (C31 letter accountable) in the decision's scope: the accountable row of
    // the Decision Right group the engine's proposal right check resolved (the most specific matching group, as OP-02
    // resolves it), and the person when an owner role has a holder on the subject's application. The caller never sets it.
    private static JsonTreeNode Decider(ToolScope s, string type, string? applicationId, JsonTreeNode proposalRight)
    {
        JsonTreeNode right = Js.Items(proposalRight, "holders").FirstOrDefault(h => Js.Text(h, "letter") == "accountable")
            ?? throw s.Call.Fail("unavailable", "decision-right.accountable", "The decision type " + type + " has no accountable decision right in this scope in the configuration in force.", "Decision Right", type);
        string? ownerRole = Js.Text(right, "ownerRoleKey");
        string? role = Js.Text(right, "roleKey");
        JsonTreeNode principal = JsonTreeNode.Null;
        if (ownerRole != null && applicationId != null)
        {
            principal = OwnerRef(s, s.CurrentOwners(applicationId), ownerRole);
        }

        return Js.Obj(("accountableRoleKey", ownerRole == null ? Js.OptStr(role) : null), ("accountableOwnerRoleKey", Js.OptStr(ownerRole)), ("principal", principal));
    }

    // One O20 Evidence Link for a record (append-only).
    private static string Evidence(ToolScope s, string subjectKind, string subjectColumn, string subjectId, JsonTreeNode item)
    {
        string kind = s.RequireKey("link-kind", Js.Text(item, "linkKindKey"), "evidence.linkKindKey").KeyPath;
        return s.Create("evidence-link", Js.Obj(
            ("subjectKind", JsonTreeNode.FromString(subjectKind)),
            (subjectColumn, JsonTreeNode.FromString(subjectId)),
            ("linkKind", JsonTreeNode.FromString(kind)),
            ("url", JsonTreeNode.FromString(Js.Text(item, "url")!)),
            ("label", JsonTreeNode.FromString(Js.Text(item, "label")!)),
            ("evidenceLevel", JsonTreeNode.FromString("as-reported")),
            ("capturedOn", Js.Utc(s.Now)))).RowId;
    }

    // ---- 18. submit_record ----

    // WORKING DEFAULT (open design question 9): an AI client may submit for a person where the Tool Policy says so (the
    // core checked AI May Submit). WORKING DEFAULT (open design question 8): the engine accepts with no card only for
    // the locked list (OP-02 engine-accept).
    private static JsonTreeNode SubmitRecord(ToolScope s)
    {
        s.RequireAiState("submitted");
        string kind = s.RequireText("subjectKind");
        string id = s.RequireText("subjectId");
        string entity = kind == "configuration-draft" ? "configuration-draft" : kind == "decision" ? "decision" : kind;
        StoredRow subject = s.RequireRow(entity, id, "subjectId");
        string? decisionId = null;
        JsonTreeNode requested = Js.EmptyObject;
        JsonTreeNode? engineAccepted = null;
        JsonTreeNode? standardApplied = null;
        string mainEntity = entity;
        string mainId = id;
        bool decisionSubject = false;

        // A check (validateOnly) reports the record as it stays: a draft is still a draft (product review, fix round 1).
        JsonTreeNode statusBefore = kind == "configuration-draft"
            ? s.StatusValue(subject, "draftStatus", "draftStatus")
            : kind == "intake-request" || kind == "change-request" ? s.StatusValue(subject) : JsonTreeNode.Null;

        switch (kind)
        {
            case "intake-request":
            case "change-request":
                RequireCreator(s, subject, kind == "intake-request" ? "requester" : "createdByPrincipal");
                if (subject.Text("statusMeaning") != "draft")
                {
                    throw s.Refused(McpErrorCodes.RuleViolation, "Only a draft is submitted; this record is " + subject.Text("statusMeaning") + ".");
                }

                s.Update(entity, id, Js.Obj(("status", JsonTreeNode.FromString(s.StatusFor(entity, "submitted")))));
                bool linked = kind == "change-request" && subject.Text("mode") == "linked";
                if (!linked)
                {
                    decisionId = SubjectDecision(s, kind, subject);
                    s.Update(entity, id, Js.Obj((kind == "intake-request" ? "triageDecision" : "authorizationDecision", JsonTreeNode.FromString(decisionId))));
                    engineAccepted = EngineAcceptance(s, decisionId, out standardApplied);
                    requested = s.Call.RequestApprovals(decisionId);
                }
                else
                {
                    s.Call.Warn("change.linked-mode", "In linked mode the client's ITSM tool authorizes the change, so nobody is asked here. A recorded number alone never counts as authorization.", "Change Request", id);
                }

                break;
            case "data-use":
            case "data-mapping":
                decisionId = DataSubjectDecision(s, kind, subject);
                engineAccepted = EngineAcceptance(s, decisionId, out standardApplied);
                requested = s.Call.RequestApprovals(decisionId);
                break;
            case "decision":
                RequireCreator(s, subject, "createdByPrincipal");
                if (subject.Text("statusMeaning") != "proposed")
                {
                    throw s.Refused(McpErrorCodes.RuleViolation, "Only a proposed decision is submitted; this one is " + subject.Text("statusMeaning") + ".");
                }

                RequireDataDecisionSubmittable(s, subject);
                decisionId = id;
                decisionSubject = true;
                engineAccepted = EngineAcceptance(s, decisionId, out standardApplied);
                requested = s.Call.RequestApprovals(decisionId);
                break;
            default:
                RequireCreator(s, subject, "instructedBy");
                requested = s.Call.SubmitConfigurationDraft(id);
                decisionId = Js.Text(requested, "decisionId");
                break;
        }

        // An engine-accepted decision that waits for the consent of the person it makes an owner (document 25, B25-18): the
        // engine has not accepted it yet, so engineAccepted stays null and the consent request is listed.
        bool waitingForConsent = Js.Flag(requested, "waitingForConsent") == true;
        if (waitingForConsent)
        {
            engineAccepted = null;
            standardApplied = null;
        }

        var approvals = Js.Strings(requested, "approvalRecordIds").Select(r => s.Rows.Get("approval-record", r)).Where(r => r != null).Select(r => ApprovalRequested(s, r!)).ToList();
        StoredRow main = s.Rows.Get(mainEntity, mainId)!;
        List<(string, JsonTreeNode?)> result = mainEntity == "configuration-draft"
            ? s.WriteResult(main, false, hasStatus: true, statusColumn: "draftStatus", meaningColumn: "draftStatus")
            : s.WriteResult(main, false);
        if (decisionSubject || kind == "data-use" || kind == "data-mapping")
        {
            // The caller's act leaves the decision proposed. For a data use or a copy the decision is the record the
            // submit sends, so its status is reported, never the data row's own (security review, minor). An acceptance the engine makes for the locked list is its own
            // act, reported in engineAccepted (no tool output ever holds accepted).
            result.RemoveAll(m => m.Item1 == "status");
            result.Add(("status", ToolScope.StatusValue("proposed", s.Label("status", s.StatusFor("decision", "proposed")))));
        }
        else if (engineAccepted != null && (kind == "intake-request" || kind == "change-request") && main.Text("statusMeaning") != "submitted")
        {
            // The caller's act leaves the record submitted. The engine's acceptance under the locked list then applies the
            // decision to it (the update-subject effect: an intake completed, a standard change scheduled), its own act,
            // reported in engineAccepted and shown by the record's read.
            result.RemoveAll(m => m.Item1 == "status");
            result.Add(("status", ToolScope.StatusValue("submitted", s.Label("status", s.StatusFor(entity, "submitted")))));
        }

        bool dryRun = s.Call.IsDryRun;
        if (dryRun && statusBefore.Kind != JsonTreeKind.Null)
        {
            result.RemoveAll(m => m.Item1 == "status");
            result.Add(("status", statusBefore));
        }

        // A check lists the requests the submit would open, with no ID (nothing was stored), so the sheet and an AI client
        // read who would be asked from the same answer that says how many (product review, fix round 1).
        IEnumerable<JsonTreeNode> requestsShown = dryRun ? approvals.Select(a => Js.With(a, ("approvalRecordId", JsonTreeNode.Null))) : approvals;
        JsonTreeNode data = ToolScope.Merge(
            result,
            ("subjectKind", JsonTreeNode.FromString(kind)),
            ("subjectId", JsonTreeNode.FromString(id)),
            ("decisionId", Js.Str(decisionId == null || (dryRun && decisionId != id) ? null : decisionId)),
            ("approvalsRequested", Arr(requestsShown)),
            ("standardChangeApplied", standardApplied ?? JsonTreeNode.Null),
            ("engineAccepted", engineAccepted ?? JsonTreeNode.Null));
        int asked = approvals.Count(a => Js.Flag(a, "isConsultation") != true);
        int consulted = approvals.Count(a => Js.Flag(a, "isConsultation") == true);
        string summary = dryRun
            ? (engineAccepted != null
                ? "Would be submitted, and the engine would accept the decision under the locked list, with nobody to ask."
                : waitingForConsent
                ? "Would be submitted. The business owner the request names would be asked to accept the role, and the engine would then accept the decision."
                : "Would be submitted: " + Count(asked, "approval", "approvals") + " would be asked"
                    + (consulted > 0 ? ", and " + Count(consulted, "person", "people") + " consulted" : string.Empty) + ".")
                + " Nothing was stored (validateOnly)."
            : engineAccepted != null
            ? "Submitted. The engine accepted the decision under the locked list, with nobody to ask, and applied it to the record."
            : waitingForConsent
            ? "Submitted. The engine accepts the decision with no other approval request once the business owner the request names accepts the role. Their request is sent now, and only they answer it, on the decision page."
            : "Submitted. " + Count(asked, "approval is asked", "approvals are asked")
                + (consulted > 0 ? ", and " + Count(consulted, "person is consulted", "people are consulted") : string.Empty)
                + ". Only people answer, on the decision page.";
        bool viaDecision = kind == "configuration-draft" || kind == "data-use" || kind == "data-mapping";
        return s.Answer(data, summary, s.RecordUrl(mainEntity, mainId), ToolScope.Next("get_approval_status", ("subjectKind", JsonTreeNode.FromString(viaDecision ? "decision" : kind)), ("subjectId", JsonTreeNode.FromString(viaDecision ? decisionId ?? id : id))));
    }

    // The data limits on submitting a decision (docs/architecture/data-taxonomy.md, MCP tools and REST; slice 3): an AI client never submits a copy
    // decision or a retention decision (a person does), and a data-use decision waits until a person confirms its use.
    private static void RequireDataDecisionSubmittable(ToolScope s, StoredRow decision)
    {
        string? type = decision.Text("decisionType");
        string? kind = decision.Text("subjectKind");
        if (s.IsAiCaller && (type == EngineKeys.DataRetentionDecisionType || kind == EngineKeys.DataMappingSubjectKind))
        {
            throw s.Refused(McpErrorCodes.AiCannotDoThis, "An AI client never submits a " + (type == EngineKeys.DataRetentionDecisionType ? "retention" : "copy") + " decision. A person submits it in the product's page.", "subjectKind");
        }

        StoredRow? row = kind == EngineKeys.DataUseSubjectKind && decision.Text("subjectDataUse") is string use ? s.Rows.Get("data-use", use)
            : kind == EngineKeys.DataMappingSubjectKind && decision.Text("subjectDataMapping") is string mapping ? s.Rows.Get("data-mapping", mapping)
            : null;
        if (row != null && row.Text("statusMeaning") != "registered")
        {
            throw s.Refused(McpErrorCodes.RuleViolation, "The " + kind!.Replace('-', ' ') + " is a draft. A person confirms it first (D-36: AI drafts, a person accepts); then its decision goes to the approvers.");
        }

        // DC-06: a use or copy whose approver resolves to nobody is never sent (security review, major). Nobody may answer
        // its card through the application's owner roles instead.
        if (row != null && Js.Text(s.Call.DataUse(row.Id)?.Payload, "state") == "no-one-to-ask")
        {
            throw s.Refused(McpErrorCodes.RuleViolation, "The " + kind!.Replace('-', ' ') + " has no one to ask: its domain has no data owner now. Propose a data owner for its domain first, then submit the decision.");
        }
    }

    // The data decision to submit for a data use or a copy: its proposed decision with no approval request yet, else a new
    // one when the row needs a decision (OP-16), opened with the proposal right. The row must be confirmed.
    private static string DataSubjectDecision(ToolScope s, string kind, StoredRow row)
    {
        if (row.Text("statusMeaning") != "registered")
        {
            throw s.Refused(McpErrorCodes.RuleViolation, "The " + kind.Replace('-', ' ') + " is " + (row.Text("statusMeaning") ?? "a draft") + ". A person confirms it first (D-36: AI drafts, a person accepts); then its decision goes to the approvers.");
        }

        if (s.IsAiCaller && kind == EngineKeys.DataMappingSubjectKind)
        {
            throw s.Refused(McpErrorCodes.AiCannotDoThis, "An AI client never submits a copy decision. A person submits it in the product's page.", "subjectKind");
        }

        string column = kind == EngineKeys.DataUseSubjectKind ? "subjectDataUse" : "subjectDataMapping";
        StoredRow? proposed = s.Query("decision", (column, row.Id), ("decisionType", EngineKeys.DataUseDecisionType), ("statusMeaning", "proposed"))
            .Where(d => s.Query("approval-record", ("decision", d.Id)).Count == 0)
            .OrderBy(d => d.Id, StringComparer.Ordinal).FirstOrDefault();
        if (proposed != null)
        {
            RequireCreator(s, proposed, "createdByPrincipal");
            return proposed.Id;
        }

        var tree = new Product.Engine.Core.Operations.DataTree(s.Snapshot);
        JsonTreeNode? evaluation = s.Call.DataUse(row.Id)?.Payload;
        string state = Js.Text(evaluation, "state") ?? "needed";
        if (state != "needed" && state != "re-decide")
        {
            string what = kind == EngineKeys.DataUseSubjectKind ? "data use" : "copy";
            string why = state switch
            {
                "no-one-to-ask" => "The " + what + " needs a decision, but its domain has no data owner to ask, so no decision is submitted for it. Propose a data owner for its domain first.",
                "not-needed" => "The " + what + " needs no decision under the rules in force, so no decision is submitted for it.",
                "allowed" => "An accepted decision already allows the " + what + ", so no new decision is submitted for it.",
                "proposed" => "The " + what + "'s decision is already with the approvers, so no other decision is submitted for it.",
                "denied" => "A decision denied the " + what + ". Change the use, or propose a decision that supersedes the denial.",
                "breaks-rule" => "The " + what + " breaks a never rule, so no decision can allow it.",
                _ => "The " + what + " is in the state " + state.Replace('-', ' ') + ", so no decision is submitted for it.",
            };
            throw s.Refused(McpErrorCodes.RuleViolation, why);
        }

        StoredRow? app = kind == EngineKeys.DataUseSubjectKind && row.Text("businessApplication") is string appId ? s.Rows.Get("business-application", appId) : null;
        string domainKey = row.Text("dataDomain") ?? string.Empty;
        (string? decisionId, _, _) = OpenOrSendDataDecision(s, tree, row, kind, state, null, app,
            kind == EngineKeys.DataUseSubjectKind
                ? "Allow " + (app == null ? "the application" : AppName(app)) + " to " + AccessWords(row.Text("access") ?? "read") + " " + tree.PathText(domainKey) + LocationWords(s, row.Text("dataLocation")) + (row.Flag("reachesAi") ? ", with AI" : string.Empty) + "."
                : "Allow a " + (row.Text("dataOrigin") ?? "copy").Replace('-', ' ') + " of " + tree.PathText(domainKey) + LocationWords(s, row.Text("dataLocation")) + ", outside production.",
            row.Text("purpose") ?? string.Empty,
            true);
        return decisionId ?? throw s.Refused(McpErrorCodes.NotAuthorized, "You may not propose the decision about this " + kind.Replace('-', ' ') + ": the decision rights let the application's business owner, its technical owner or the platform admin propose it.");
    }

    // The record's requester or creator (document 24, tools 18 and 19).
    private static void RequireCreator(ToolScope s, StoredRow row, string column)
    {
        if (!Same(row.Text(column), s.PersonId) && !Same(row.Text("createdByPrincipal"), s.PersonId) && !Same(row.Text("onBehalfOf"), s.PersonId))
        {
            throw s.Refused(McpErrorCodes.NotAuthorized, "Only the record's requester or creator submits or withdraws it.");
        }
    }

    // The proposed intake or change authorization decision of a record, created when it has none.
    private static string SubjectDecision(ToolScope s, string kind, StoredRow subject)
    {
        string column = kind == "intake-request" ? "triageDecision" : "authorizationDecision";
        string? existing = subject.Text(column);
        if (existing != null && s.Rows.Get("decision", existing)?.Text("statusMeaning") == "proposed")
        {
            return existing;
        }

        string type = kind == "intake-request" ? EngineKeys.IntakeDecisionType : EngineKeys.ChangeAuthorizationDecisionType;
        string subjectColumn = kind == "intake-request" ? "subjectIntakeRequest" : "subjectChangeRequest";
        StoredRow? proposed = s.Query("decision", (subjectColumn, subject.Id), ("decisionType", type), ("statusMeaning", "proposed")).FirstOrDefault();
        if (proposed != null)
        {
            return proposed.Id;
        }

        // The decision the engine opens proposes the outcome its statement says (accept, authorize), so its acceptance
        // registers the application or schedules the change (the update-subject effect, document 25, B25-8). People who
        // want another outcome reject it and propose that one.
        string outcome = kind == "intake-request" ? EngineKeys.IntakeAcceptOutcome : EngineKeys.ChangeAuthorizeOutcome;
        bool outcomeActive = s.ConfigRow("decision-outcome", type + "/" + outcome) is SnapshotRow outcomeRow && outcomeRow.IsActive;
        return s.Create("decision", Js.Obj(
            ("decisionType", JsonTreeNode.FromString(type)),
            ("subjectKind", JsonTreeNode.FromString(kind)),
            (subjectColumn, JsonTreeNode.FromString(subject.Id)),
            ("subjectApplication", Js.OptStr(subject.Text("businessApplication"))),
            ("statement", JsonTreeNode.FromString((kind == "intake-request" ? "Accept the intake request: " : "Authorize the change: ") + (subject.Text("title") ?? subject.Id))),
            ("outcome", Js.OptStr(outcomeActive ? outcome : null)),
            ("status", JsonTreeNode.FromString(s.StatusFor("decision", "proposed"))))).RowId;
    }

    // { decisionId, condition } when OP-02 answers engine-accept for the decision (the locked list), else null. For a
    // standard change the engine accepts through a model, standardApplied names the model and the decision in which a
    // person approved it (document 25, B25-14).
    private static JsonTreeNode? EngineAcceptance(ToolScope s, string decisionId, out JsonTreeNode? standardApplied)
    {
        standardApplied = null;
        EvaluationResult<JsonTreeNode?> required = s.Call.RequiredApprovals(decisionId);
        if (required.Outcome != "engine-accept")
        {
            return null;
        }

        JsonTreeNode? basis = Js.Member(required.Payload, "acceptanceBasis");
        if (Js.Text(basis, "condition") == "standard-model" && Js.Text(basis, "standardChangeModelKey") is string model && Js.Text(basis, "approvedByDecisionId") is string approvedBy && ToolScope.IsGuid(approvedBy))
        {
            standardApplied = Js.Obj(("modelKey", JsonTreeNode.FromString(model)), ("approvedByDecisionId", JsonTreeNode.FromString(approvedBy)));
        }

        string condition = Js.Text(basis, "condition") ?? "lowest-tier";
        return Js.Obj(("decisionId", JsonTreeNode.FromString(decisionId)), ("condition", JsonTreeNode.FromString(condition == "standard-model" ? "standard-model" : "lowest-tier")));
    }

    private static JsonTreeNode ApprovalRequested(ToolScope s, StoredRow record)
    {
        SnapshotRow? rule = s.ConfigRow("approval-rule", record.Text("approvalRule"));
        string? role = record.Text("requiredRole");
        string? ownerRole = record.Text("requiredOwnerRole");
        return Js.Obj(
            ("approvalRecordId", JsonTreeNode.FromString(record.Id)),
            ("requiredRoleKey", ownerRole == null ? Js.OptStr(role ?? "platform-admin") : null),
            ("requiredOwnerRoleKey", Js.OptStr(ownerRole)),
            ("askedPrincipal", s.PrincipalRef(record.Text("askedPrincipal"))),
            ("approvalMode", JsonTreeNode.FromString((rule == null ? null : Js.Text(rule.Columns, "approvalMode")) ?? "first-to-respond")),
            ("expiresOn", ToolScope.MomentOrNull(record.Text("expiresOn"))),
            ("channelKey", JsonTreeNode.FromString(record.Text("channel") ?? s.Setting("reminder-channel") ?? "teams")),
            ("isConsultation", JsonTreeNode.FromBoolean(record.Flag("isConsultation"))));
    }

    // ---- 19. withdraw_record ----

    private static JsonTreeNode WithdrawRecord(ToolScope s)
    {
        s.RequireAiState("withdrawn");
        string kind = s.RequireText("subjectKind");
        string id = s.RequireText("subjectId");
        string reason = s.RequireText("reason");
        string entity = kind switch
        {
            "application-draft" => "business-application",
            _ => kind,
        };
        StoredRow subject = s.RequireRow(entity, id, "subjectId");
        var cancelled = new List<string>();
        string? withdrawnDecision = null;
        ComposedWrite write;
        switch (kind)
        {
            case "intake-request":
            case "change-request":
                RequireCreator(s, subject, kind == "intake-request" ? "requester" : "createdByPrincipal");
                if (!IsOpen(subject))
                {
                    throw s.Refused(McpErrorCodes.RuleViolation, "The record is " + subject.Text("statusMeaning") + ", so it can no longer be withdrawn.");
                }

                write = s.Update(entity, id, Js.Obj(("status", JsonTreeNode.FromString(s.StatusFor(entity, "withdrawn")))));
                string? linkedDecision = subject.Text(kind == "intake-request" ? "triageDecision" : "authorizationDecision");
                if (linkedDecision != null && s.Rows.Get("decision", linkedDecision)?.Text("statusMeaning") == "proposed")
                {
                    cancelled.AddRange(s.Call.CancelPendingApprovals(linkedDecision));
                    s.Update("decision", linkedDecision, Js.Obj(("status", JsonTreeNode.FromString(s.StatusFor("decision", "withdrawn"))), ("reason", JsonTreeNode.FromString(reason))));
                    withdrawnDecision = linkedDecision;
                }

                break;
            case "decision":
                RequireCreator(s, subject, "createdByPrincipal");
                if (subject.Text("statusMeaning") != "proposed")
                {
                    throw s.Refused(McpErrorCodes.RuleViolation, "The decision is " + subject.Text("statusMeaning") + ". An accepted decision is never withdrawn; a new decision supersedes it.");
                }

                cancelled.AddRange(s.Call.CancelPendingApprovals(id));
                write = s.Update("decision", id, Js.Obj(("status", JsonTreeNode.FromString(s.StatusFor("decision", "withdrawn")))));
                break;
            case "data-use":
            case "data-mapping":
            case "data-location":
                RequireCreator(s, subject, "createdByPrincipal");
                if (subject.Text("statusMeaning") != "draft")
                {
                    throw s.Refused(McpErrorCodes.RuleViolation, "Only a draft " + kind.Replace('-', ' ') + " is withdrawn; this one is " + subject.Text("statusMeaning") + ". End a confirmed one with its save tool (endOn, or retire for a location).");
                }

                // A withdrawn mapping also ends today, so it frees its place in the store's key (O26: domain, location and
                // origin among the rows with no Ended On) for a later move or a new mapping (wave 4b review).
                write = s.Update(entity, id, kind == "data-mapping"
                    ? Js.Obj(("status", JsonTreeNode.FromString(s.StatusFor(entity, "archived"))), ("endedOn", JsonTreeNode.FromString(s.Today)))
                    : Js.Obj(("status", JsonTreeNode.FromString(s.StatusFor(entity, "archived")))));
                string? dataColumn = kind == "data-use" ? "subjectDataUse" : kind == "data-mapping" ? "subjectDataMapping" : null;
                foreach (StoredRow open in dataColumn == null ? new List<StoredRow>() : s.Query("decision", (dataColumn, id), ("statusMeaning", "proposed")).ToList())
                {
                    cancelled.AddRange(s.Call.CancelPendingApprovals(open.Id));
                    s.Update("decision", open.Id, Js.Obj(("status", JsonTreeNode.FromString(s.StatusFor("decision", "withdrawn"))), ("reason", JsonTreeNode.FromString(reason))));
                    withdrawnDecision ??= open.Id;
                }

                break;
            case "application-draft":
                RequireCreator(s, subject, "createdByPrincipal");
                if (subject.Text("statusMeaning") != "draft")
                {
                    throw s.Refused(McpErrorCodes.RuleViolation, "Only a draft application is withdrawn here; this one is " + subject.Text("statusMeaning") + ".");
                }

                write = s.Update(entity, id, Js.Obj(("status", JsonTreeNode.FromString(s.StatusFor(entity, "archived")))));

                // The decisions about the draft go with it (wave 4b, found by the demo's S-22.19): its ownership decision,
                // and any other decision still proposed about it, are withdrawn and their open requests canceled, so nobody
                // is left asked to confirm the owners of an application that was withdrawn.
                foreach (StoredRow open in s.Query("decision", ("subjectApplication", id), ("statusMeaning", "proposed")).OrderBy(d => d.Id, StringComparer.Ordinal).ToList())
                {
                    cancelled.AddRange(s.Call.CancelPendingApprovals(open.Id));
                    s.Update("decision", open.Id, Js.Obj(("status", JsonTreeNode.FromString(s.StatusFor("decision", "withdrawn"))), ("reason", JsonTreeNode.FromString(reason))));
                    withdrawnDecision ??= open.Id;
                }

                break;
            default:
                RequireCreator(s, subject, "instructedBy");
                string? changeDecision = subject.Text("changeDecision");
                write = s.Call.DiscardConfigurationDraft(id, reason);
                if (changeDecision != null && s.Rows.Get("decision", changeDecision)?.Text("statusMeaning") == "proposed")
                {
                    cancelled.AddRange(s.Call.CancelPendingApprovals(changeDecision));
                    s.Update("decision", changeDecision, Js.Obj(("status", JsonTreeNode.FromString(s.StatusFor("decision", "withdrawn")))));
                    withdrawnDecision = changeDecision;
                }

                break;
        }

        StoredRow main = s.Rows.Get(entity, write.RowId)!;
        List<(string, JsonTreeNode?)> result = entity == "configuration-draft"
            ? s.WriteResult(main, false, true, "draftStatus", "draftStatus")
            : s.WriteResult(main, false);
        JsonTreeNode data = ToolScope.Merge(
            result,
            ("subjectKind", JsonTreeNode.FromString(kind)),
            ("subjectId", JsonTreeNode.FromString(id)),
            ("cancelledApprovals", Ids(cancelled)),
            ("withdrawnDecisionId", Js.Str(withdrawnDecision)));
        return s.Answer(data, "Withdrawn. " + (cancelled.Count == 0 ? "No approval was waiting." : Count(cancelled.Count, "waiting approval is canceled.", "waiting approvals are canceled.")), s.RecordUrl(entity, id));
    }

    // ---- 20. get_decision_log ----

    private static JsonTreeNode GetDecisionLog(ToolScope s)
    {
        string? subjectKind = s.Text("subjectKind");
        string? subjectId = s.Text("subjectId");
        string? app = s.Text("businessApplicationId");
        string? portfolio = s.Text("portfolioId");
        if (portfolio != null)
        {
            s.RequireRow("portfolio", portfolio, "portfolioId");
        }

        IReadOnlyList<string> types = s.Strings("decisionTypeKeys");
        IReadOnlyList<string> meanings = s.Strings("statusMeanings");
        // dataDomainKey (data taxonomy slice 3): decisions about the domain or the domains below it, their ownership, and
        // the uses and mappings that point at them.
        HashSet<string>? dataDomains = null;
        if (s.Text("dataDomainKey") is string dataDomain)
        {
            var tree = new Product.Engine.Core.Operations.DataTree(s.Snapshot);
            if (tree.Domain(dataDomain) == null)
            {
                throw s.NotFound("Data Domain", dataDomain, "dataDomainKey");
            }

            dataDomains = new HashSet<string>(tree.Subtree(dataDomain), StringComparer.Ordinal);
        }

        DateTime? from = Js.ParseMoment(s.Text("from"));
        DateTime? to = Js.ParseMoment(s.Text("to"));
        int hidden = 0;
        var rows = new List<StoredRow>();
        IReadOnlyList<StoredRow> decisions = s.Rows.Query(RowQuery.For("decision"));

        // The requests the visibility test reads, the applications a portfolio filter reads, and the data rows a domain
        // filter reads, each in one key set read (wave 4b review).
        s.Call.PrefetchIds("intake-request", decisions.Where(d => d.Text("subjectKind") == "intake-request").Select(d => SubjectIdOf(d)));
        if (portfolio != null)
        {
            s.Call.PrefetchAll("portfolio");
            s.Call.PrefetchIds("business-application", decisions.Select(d => d.Text("subjectApplication")));
        }

        if (dataDomains != null)
        {
            s.Call.PrefetchIds("data-use", decisions.Select(d => d.Text("subjectDataUse")));
            s.Call.PrefetchIds("data-mapping", decisions.Select(d => d.Text("subjectDataMapping")));
        }

        foreach (StoredRow d in decisions)
        {
            string kind = d.Text("subjectKind") ?? string.Empty;
            string? id = SubjectIdOf(s, d);
            if ((subjectKind != null && (kind != subjectKind || !Same(id, subjectId)))
                || (app != null && !Same(d.Text("subjectApplication"), app))
                || (portfolio != null && !(d.Text("subjectApplication") is string decisionApp && PortfolioPath(s, s.Rows.Get("business-application", decisionApp)?.Text("portfolio")).Contains(portfolio, StringComparer.Ordinal)))
                || (types.Count > 0 && !types.Contains(d.Text("decisionType") ?? string.Empty, StringComparer.Ordinal))
                || (meanings.Count > 0 && !meanings.Contains(d.Text("statusMeaning") ?? string.Empty, StringComparer.Ordinal))
                || (dataDomains != null && !(DataDomainOfDecision(s, d) is string decided && dataDomains.Contains(decided))))
            {
                continue;
            }

            DateTime? when = Js.ParseMoment(d.Text("decidedOn")) ?? Js.ParseMoment(d.Text("engineCheckedOn"));
            if ((from.HasValue && when.HasValue && when.Value.Date < from.Value.Date) || (to.HasValue && when.HasValue && when.Value.Date > to.Value.Date))
            {
                continue;
            }

            if (kind == "intake-request" && id != null && s.Rows.Get("intake-request", id) is StoredRow intake && !CanReadIntake(s, intake))
            {
                hidden++;
                continue;
            }

            rows.Add(d);
        }

        s.HideFromCaller("decision", hidden);
        var ordered = rows.OrderByDescending(d => d.Text("decidedOn") ?? d.Text("engineCheckedOn") ?? string.Empty, StringComparer.Ordinal).ThenByDescending(d => d.Id, StringComparer.Ordinal).ToList();
        List<StoredRow> page = s.Page(ordered, out string? next);
        PrefetchDecisionSubjects(s, page);
        JsonTreeNode items = Arr(page.Select(d => Js.Obj(
            ("decisionId", JsonTreeNode.FromString(d.Id)),
            ("decisionType", s.LabelledKey("decision-type", d.Text("decisionType") ?? "unknown")),
            ("subjectKind", JsonTreeNode.FromString(d.Text("subjectKind") ?? "business-application")),
            ("subjectId", JsonTreeNode.FromString(SubjectIdOf(s, d) ?? d.Id)),
            ("subjectLabel", JsonTreeNode.FromString(DecisionSubjectLabel(s, d).Label)),
            ("statement", JsonTreeNode.FromString(d.Text("statement") ?? string.Empty)),
            ("reason", JsonTreeNode.FromString(d.Text("reason") ?? string.Empty)),
            ("exception", d.Text("exceptionStandard") == null && d.Text("exceptionReviewOn") == null ? JsonTreeNode.Null : Js.Obj(
                ("standard", Js.Str(d.Text("exceptionStandard"))),
                ("reviewOn", ToolScope.DateOrNull(d.Text("exceptionReviewOn"))))),
            ("outcome", d.Text("outcome") == null ? JsonTreeNode.Null : Js.Obj(("key", JsonTreeNode.FromString(d.Text("outcome")!)), ("label", JsonTreeNode.FromString(s.Label("decision-outcome", d.Text("decisionType") + "/" + d.Text("outcome")))))),
            ("status", s.StatusValue(d)),
            ("decider", s.PrincipalRef(d.Text("decider"))),
            ("decidedOn", ToolScope.MomentOrNull(d.Text("decidedOn"))),
            ("supersedes", Js.Str(d.Text("supersedes"))),
            ("supersededBy", Js.Str(d.Text("supersededBy"))),
            ("amends", Js.Str(d.Text("amends"))),
            ("approvalsSummary", ApprovalsSummary(s, d.Id)),
            ("configurationSetVersion", JsonTreeNode.FromInt64(d.Number("configurationSetVersion") ?? s.Session.ConfigurationSetVersion)),
            ("proposedBy", s.PrincipalRef(ProposerOf(s, d))),
            ("ownerChanges", OwnerChangesOf(s, d)),
            ("isAiCreated", AiCreated(d)),
            ("performedBy", Stamp(s, d)),
            ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("decision", d.Id))))));
        return s.Answer(Js.Obj(("items", items), ("skipToken", Js.OptStr(next))), ordered.Count + " decisions match.");
    }

    // The owner changes an ownership decision proposes, by owner role key and deputy flag (an end takes the flag of the
    // assignment it ends), so a page matches a waiting change to a role by key, never by words (product review, major).
    private static JsonTreeNode OwnerChangesOf(ToolScope s, StoredRow d)
    {
        if (d.Text("decisionType") != EngineKeys.OwnershipDecisionType)
        {
            return Js.EmptyArray;
        }

        var items = new List<JsonTreeNode>();
        foreach (JsonTreeNode change in Js.Items(d.Column("proposedChangesJson"), "owners"))
        {
            string? role = Js.Text(change, "ownerRoleKey");
            if (role == null)
            {
                continue;
            }

            bool add = Js.Text(change, "addObjectId") != null;
            bool deputy = add ? Js.Flag(change, "isDeputy") == true
                : Js.Text(change, "endAssignmentId") is string end && s.Rows.Get("owner-assignment", end) is StoredRow ended && ended.Flag("isDeputy");
            items.Add(Js.Obj(("ownerRoleKey", JsonTreeNode.FromString(role)), ("isDeputy", JsonTreeNode.FromBoolean(deputy)), ("change", JsonTreeNode.FromString(add ? "add" : "end"))));
        }

        return Arr(items);
    }

    // The data domain a decision is about: its own domain subject, or the domain of the use or mapping it decides. Null for
    // a decision with no data subject.
    private static string? DataDomainOfDecision(ToolScope s, StoredRow decision)
    {
        return decision.Text("subjectDataDomain")
            ?? (decision.Text("subjectDataUse") is string use ? s.Rows.Get("data-use", use)?.Text("dataDomain") : null)
            ?? (decision.Text("subjectDataMapping") is string mapping ? s.Rows.Get("data-mapping", mapping)?.Text("dataDomain") : null);
    }

    // Decisions saved before a review named its outcome decision carried the review in their context, after this marker.
    private const string LegacyReviewMarker = "\nReview: ";

    // The subject row of a decision. A decision about a review is the review that names it as its Outcome Decision (O21),
    // else the review an older decision named in its context, else the review's application.
    private static string? SubjectIdOf(ToolScope s, StoredRow decision)
    {
        return decision.Text("subjectKind") == "review" ? ReviewIdOf(s, decision) ?? decision.Text("subjectApplication") : SubjectIdOf(decision);
    }

    private static string? ReviewIdOf(ToolScope s, StoredRow decision)
    {
        if (decision.Text("subjectKind") != "review")
        {
            return null;
        }

        if (s.Query("review", ("outcomeDecision", decision.Id)).FirstOrDefault() is StoredRow linked)
        {
            return linked.Id;
        }

        string? context = decision.Text("context");
        int at = context == null ? -1 : context.LastIndexOf(LegacyReviewMarker, StringComparison.Ordinal);
        if (at < 0)
        {
            return null;
        }

        string id = context!.Substring(at + LegacyReviewMarker.Length).Trim();
        return s.Rows.Get("review", id) != null ? id : null;
    }

    // The context as people wrote it, without the review marker older decisions carried, and without the JSON block older
    // ownership decisions carried ("Proposed changes: [...]" or "Proposed owners: [...]"): get_decision returns the
    // proposed changes in words of their own (product review 2026-11-24).
    private static string? ContextOf(StoredRow decision)
    {
        string? context = decision.Text("context");
        foreach (string marker in new[] { "Proposed changes: [", "Proposed owners: [" })
        {
            int json = context == null ? -1 : context.IndexOf(marker, StringComparison.Ordinal);
            if (json >= 0)
            {
                string kept = context!.Substring(0, json).Trim();
                context = kept.Length == 0 ? null : kept;
            }
        }

        if (context == null || decision.Text("subjectKind") != "review")
        {
            return context;
        }

        int at = context.LastIndexOf(LegacyReviewMarker, StringComparison.Ordinal);
        return at < 0 ? context : context.Substring(0, at);
    }

    private static string? SubjectIdOf(StoredRow decision)
    {
        return decision.Text("subjectKind") switch
        {
            "intake-request" => decision.Text("subjectIntakeRequest"),
            "change-request" => decision.Text("subjectChangeRequest"),
            "deployment-record" => decision.Text("subjectDeploymentRecord"),
            "technical-component" => decision.Text("subjectComponent"),
            "claim" => decision.Text("subjectClaim"),
            "configuration-draft" => decision.Text("subjectConfigurationDraft"),
            "data-use" => decision.Text("subjectDataUse"),
            "data-mapping" => decision.Text("subjectDataMapping"),
            "data-domain" => decision.Text("subjectDataDomain"),
            "data-location" => decision.Text("subjectDataLocation"),
            _ => decision.Text("subjectApplication"),
        };
    }

    private static JsonTreeNode ApprovalsSummary(ToolScope s, string? decisionId)
    {
        var records = decisionId == null ? new List<StoredRow>() : s.Query("approval-record", ("decision", decisionId)).ToList();
        int Count(string meaning) => records.Count(r => r.Text("outcomeMeaning") == meaning);
        return Js.Obj(
            ("required", JsonTreeNode.FromInt64(records.Count(r => !r.Flag("isConsultation")))),
            ("approved", JsonTreeNode.FromInt64(Count("approved"))),
            ("rejected", JsonTreeNode.FromInt64(Count("rejected"))),
            ("pending", JsonTreeNode.FromInt64(Count("pending"))));
    }

    // ---- 21. get_approval_status (OP-02 and OP-03) ----

    private static JsonTreeNode GetApprovalStatus(ToolScope s)
    {
        string kind = s.RequireText("subjectKind");
        string id = s.RequireText("subjectId");
        string? decisionId = kind switch
        {
            "decision" => s.RequireRow("decision", id, "subjectId").Id,
            "intake-request" => IntakeDecisionOf(s, s.RequireRow("intake-request", id, "subjectId")),
            _ => s.RequireRow("change-request", id, "subjectId").Text("authorizationDecision"),
        };
        if (decisionId == null)
        {
            JsonTreeNode empty = Js.Obj(("decisionId", JsonTreeNode.Null), ("required", Js.EmptyArray), ("records", Js.EmptyArray), ("missing", Js.EmptyArray), ("ready", JsonTreeNode.False));
            return s.Answer(empty, "No decision is waiting for approvals on this record yet. Submit it first.");
        }

        // A settled decision is not evaluated again (document 24, B24-10): its records show what the engine stored when it
        // settled, and nothing is missing. A decision recorded as settled outside the engine (the demo estate's seeded
        // authorizations, an import) holds no approval request, and settledBy says so instead of listing what is missing.
        StoredRow decision = s.RequireRow("decision", decisionId, "subjectId");
        if (decision.Text("statusMeaning") is string meaning && meaning != "proposed")
        {
            JsonTreeNode settled = Js.Without(SettledApprovals(s, decision, detailed: false), "evaluated");
            string by = Js.Text(settled, "settledBy") ?? "answers";
            return s.Answer(settled, "The decision is " + meaning + (by == "recorded" ? ". It was recorded as " + meaning + " outside the engine, so no approval request is on record." : by == "engine" ? ", by the engine under the locked list, with nobody to ask." : "."), s.RecordUrl("decision", decisionId));
        }

        (JsonTreeNode data, int missing, bool ready) = ApprovalState(s, decisionId, detailed: false);
        return s.Answer(data, ready ? "The approvals the rules require are in." : Count(missing, "approval is", "approvals are") + " still missing. Only a person's answer counts.", s.RecordUrl("decision", decisionId));
    }

    // How a settled decision was settled (document 24, B24-10): answers (it holds approval records), engine (the engine
    // accepted it under the locked list, with no card), person (its proposer or decider withdrew it), or recorded (no
    // approval record and no engine stamp: recorded as settled outside the engine, such as the seeded demo estate or an
    // import). Null while it is proposed.
    private static string? SettledBy(StoredRow decision, int recordCount)
    {
        string? meaning = decision.Text("statusMeaning");
        if (meaning == null || meaning == "proposed")
        {
            return null;
        }

        string? actor = decision.Text("performedByActorKind");
        return actor == null ? "recorded" : recordCount > 0 ? "answers" : actor == "engine" ? "engine" : "person";
    }

    private static string? IntakeDecisionOf(ToolScope s, StoredRow intake)
    {
        if (!CanReadIntake(s, intake))
        {
            throw s.NotFound("Intake Request", intake.Id, "subjectId");
        }

        return intake.Text("triageDecision") ?? s.Query("decision", ("subjectIntakeRequest", intake.Id)).OrderByDescending(d => d.Id, StringComparer.Ordinal).FirstOrDefault()?.Id;
    }

    // ---- 52. answer_approval ----

    // D-38: a person answers their own approval request on the product's decision page, and the token proves who answered.
    // The core serves the tool only on the person entry (an AI client never reaches it), and the engine checks the person
    // again: a person signed in as a user; the decision proposed; the record pending and not expired; the role, or the owner
    // role on the decision's application, held now; no earlier counting approval for the same requirement; no separation
    // rule against the person; a typed reason where one is needed. It then writes the answer and recomputes the decision,
    // as for an answer the approval runner brings from a card. A refusal writes nothing.
    private static JsonTreeNode AnswerApproval(ToolScope s)
    {
        string id = s.RequireText("approvalRecordId");
        string answer = s.RequireText("answer");
        s.RequireRow("approval-record", id, "approvalRecordId");
        JsonTreeNode result = s.Call.AnswerApproval(new ApprovalAnswerCommand(id, answer == "approve", s.Text("reason")));
        JsonTreeNode check = Js.Member(result, "check") ?? Js.EmptyObject;

        StoredRow record = s.Rows.Get("approval-record", id) ?? throw new InvalidOperationException("Approval Record " + id + " was answered but cannot be read back.");
        string decisionId = Js.Text(result, "decisionId")!;
        StoredRow decision = s.Rows.Get("decision", decisionId) ?? throw new InvalidOperationException("Decision " + decisionId + " cannot be read back.");
        bool counts = Js.Flag(result, "countsTowardRequirement") == true;
        bool consultation = Js.Flag(check, "isConsultation") == true;
        string? status = decision.Text("statusMeaning");
        // performedBy is the person's own answer, as the engine stamped that write. The recompute in the same transaction
        // then rewrote Counts Toward Requirement on the row as the engine's own write, so the row's last stamp says engine.
        JsonTreeNode performedBy = Js.With(s.PerformedBy(record), ("actorKind", JsonTreeNode.FromString("person")), ("principal", s.PrincipalRef(record.Text("approver"))), ("onBehalfOf", JsonTreeNode.Null));
        JsonTreeNode data = ToolScope.Merge(
            s.WriteResult(record, false, statusColumn: "outcome", meaningColumn: "outcomeMeaning"),
            ("performedBy", performedBy),
            ("approvalRecordId", JsonTreeNode.FromString(id)),
            ("decisionId", JsonTreeNode.FromString(decisionId)),
            ("answer", JsonTreeNode.FromString(answer)),
            ("answeredAs", Js.Obj(("roleKey", Js.Str(Js.Text(check, "heldRoleKey"))), ("ownerRoleKey", Js.Str(Js.Text(check, "heldOwnerRoleKey"))))),
            ("isConsultation", JsonTreeNode.FromBoolean(consultation)),
            ("countsTowardRequirement", JsonTreeNode.FromBoolean(counts)),
            ("decisionStatus", s.StatusValue(decision)),
            ("openedApprovalRecordIds", Ids(Js.Strings(result, "openedRecordIds").ToArray())),
            ("cancelledApprovalRecordIds", Ids(Js.Strings(result, "cancelledRecordIds").ToArray())));

        // Every approval in and the gate not ready: the gate holds the decision, not other answers (wave 3 verification).
        bool gateHolds = status == "proposed" && s.Call.Reasons.Any(r => r.Code == Product.Engine.Core.Reasons.ReasonCodes.GateNotReady);
        bool othersPending = s.Query("approval-record", ("decision", decisionId), ("outcomeMeaning", "pending")).Any(r => !r.Flag("isConsultation"));
        string what = answer == "approve" ? "Your approval" : "Your rejection";
        string summary = s.Call.IsDryRun
            ? what + " would be recorded" + (status == "accepted" ? ", and the engine would accept the decision." : status == "rejected" ? ", and the engine would reject the decision." : gateHolds ? ". Every approval would be in, and the gate still holds the decision." : ".") + " Nothing was written (validateOnly)."
            : consultation ? what + " is recorded as a consultation answer. It is shown as a recommendation and never counts."
            : status == "accepted" ? what + " is recorded and counts. The engine accepted the decision."
            : status == "rejected" ? what + " is recorded and counts. The engine rejected the decision."
            : counts && gateHolds ? what + " is recorded and counts. Every approval the rules require is in, and the gate is not ready yet, so the decision stays proposed. The engine accepts it as soon as the gate is ready; the warnings say what the gate still needs."
            : counts && othersPending ? what + " is recorded and counts. The decision still waits for other answers."
            : counts ? what + " is recorded and counts. The decision is not accepted yet; the warnings say why."
            : what + " is recorded but does not count yet; the warnings say why.";
        return s.Answer(data, summary, s.RecordUrl("decision", decisionId),
            ToolScope.Next("get_approval_status", ("subjectKind", JsonTreeNode.FromString("decision")), ("subjectId", JsonTreeNode.FromString(decisionId))));
    }

    // ---- 47. request_architecture_review ----

    private static JsonTreeNode RequestArchitectureReview(ToolScope s)
    {
        s.RequireAiState("submitted");
        StoredRow app = RequireApplication(s);
        RequireOwnerRelation(s, app.Id);
        if (s.Text("changeRequestId") is string change)
        {
            s.RequireRow("change-request", change, "changeRequestId");
        }

        string reviewType = s.RequireKey("review-type", "architecture", "reviewTypeKey").KeyPath;
        string due = s.Text("dueOn") ?? ReviewRequestDueOn(s);
        StoredRow? open = s.Query("review", ("businessApplication", app.Id), ("reviewType", reviewType), ("statusMeaning", "due")).FirstOrDefault();
        ComposedWrite write;
        bool existing = open != null;
        if (open != null)
        {
            write = new ComposedWrite(open.Id, false, false, true, null);
        }
        else
        {
            write = s.Create("review", Js.Obj(
                ("businessApplication", JsonTreeNode.FromString(app.Id)),
                ("reviewType", JsonTreeNode.FromString(reviewType)),
                ("status", JsonTreeNode.FromString(s.StatusFor("review", "due"))),
                ("trigger", JsonTreeNode.FromString("request")),
                ("dueOn", JsonTreeNode.FromString(due)),
                ("description", JsonTreeNode.FromString(s.RequireText("reason")))));
        }

        var evidence = existing ? new List<string>() : s.Items("evidence").Select(e => Evidence(s, "review", "subjectReview", write.RowId, e)).ToList();
        StoredRow review = s.Rows.Get("review", write.RowId)!;
        string assigned = Js.Text(s.ConfigRow("review-type", reviewType)?.Columns, "assignedRoleKey") ?? "architect";
        JsonTreeNode data = ToolScope.Merge(
            s.WriteResult(review, write.Created),
            ("reviewId", s.Call.IsDryRun && write.Created ? JsonTreeNode.Null : JsonTreeNode.FromString(review.Id)),
            ("reviewTypeKey", JsonTreeNode.FromString(reviewType)),
            ("trigger", JsonTreeNode.FromString(review.Text("trigger") ?? "request")),
            ("assignedRoleKey", JsonTreeNode.FromString(assigned)),
            ("dueOn", ToolScope.DateOrNull(review.Text("dueOn")) is JsonTreeNode d && d.Kind != JsonTreeKind.Null ? d : JsonTreeNode.FromString(due)),
            ("existingReviewReturned", JsonTreeNode.FromBoolean(existing)),
            ("evidenceLinkIds", Ids(s.Call.IsDryRun ? Array.Empty<string>() : evidence.ToArray())));
        return s.Answer(
            data,
            existing ? "An architecture review is already open for " + AppName(app) + "; it is returned and nothing new is written." : "The architecture review is requested. It is a request, never a decision.",
            s.Call.IsDryRun && write.Created ? null : s.RecordUrl("review", review.Id),
            ToolScope.Next("save_decision_proposal", ("decisionTypeKey", JsonTreeNode.FromString("design")), ("subjectKind", JsonTreeNode.FromString("business-application")), ("subjectId", JsonTreeNode.FromString(app.Id))),
            ToolScope.Next("record_document_link", ("businessApplicationId", JsonTreeNode.FromString(app.Id)), ("documentTypeKey", JsonTreeNode.FromString("architecture-decision-record"))));
    }

    // ---- 62. complete_review ----

    // P04 step 5 and the review lifecycle of document 22, O21 (document 23, B23-10; document 24, B24-10): a person whose
    // review it is (ReviewRoleOf) marks a due review done, with Done On, themselves as Reviewer and the accepted Outcome
    // Decision, and Next Due On from the cadence (C25 for the review type and the application's tier; none for an event
    // review such as architecture or post-change). The engine checks the outcome decision again (OP-08, B25-12) and an
    // AI actor never sets done (G-08), so the tool is served only on the person entry. Evidence links are added to the
    // review in the same call. The answer names the gates the review feeds and whether each is met now.
    private static JsonTreeNode CompleteReview(ToolScope s)
    {
        StoredRow review = s.RequireRow("review", s.Text("reviewId"), "reviewId");
        StoredRow? app = review.Text("businessApplication") is string appId ? s.Rows.Get("business-application", appId) : null;
        string meaning = review.Text("statusMeaning") ?? "due";
        if (meaning != "due")
        {
            throw s.Refused(McpErrorCodes.RuleViolation, "The review is " + meaning + ", so it is not completed again. A new review is opened instead.");
        }

        if (!ReviewIsMine(s, review, app, new Dictionary<string, string?>(StringComparer.Ordinal)))
        {
            throw s.Refused(McpErrorCodes.NotAuthorized, "This review is not yours to complete: its lead owner, a person who decides the decision it records, or a person whose role approves it completes it.");
        }

        // A review whose named outcome decision is accepted is completed with that decision only (security review
        // 2026-11-24, B24-14). The engine also refuses a decision another done review used, or one decided before this
        // review's cycle started (OP-08, B25-15).
        string? named = review.Text("outcomeDecision");
        string? given = s.Text("outcomeDecisionId");
        if (given != null && named != null && !Same(given, named) && s.Rows.Get("decision", named)?.Text("statusMeaning") == "accepted")
        {
            throw s.Refused(McpErrorCodes.RuleViolation, "The review already names its accepted outcome decision (" + (s.Rows.Get("decision", named)?.Text("recordNumber") ?? named) + "). It is completed with that decision.");
        }

        string? outcomeId = given ?? named;
        if (outcomeId == null)
        {
            throw s.Refused(McpErrorCodes.RuleViolation, "The review has no outcome decision yet. Propose it about the review (save_decision_proposal with subject kind review), submit it, and complete the review once it is accepted.");
        }

        StoredRow decision = s.RequireRow("decision", outcomeId, "outcomeDecisionId");
        if (decision.Text("statusMeaning") != "accepted")
        {
            throw s.Refused(McpErrorCodes.RuleViolation, "The outcome decision is " + (decision.Text("statusMeaning") ?? "not decided") + ". The review is completed once its outcome decision is accepted.");
        }

        // The person's own day (B24-21): a review done this evening in New York is done today, not tomorrow.
        DateTime today = Js.ParseMoment(s.PersonToday) ?? s.Now;
        DateTime doneOn = Js.ParseMoment(s.Text("doneOn")) ?? today;
        if (doneOn.Date > today.Date)
        {
            throw s.Invalid("doneOn", "doneOn is later than today. A review is done once it has been done.");
        }

        SnapshotRow? cadence = ReviewCadence(s, review, app);
        long? interval = Js.Number(cadence?.Columns, "intervalDays");
        DateTime? nextDue = interval.HasValue ? doneOn.Date.AddDays(interval.Value) : null;
        ComposedWrite write = s.Update("review", review.Id, Js.Obj(
            ("status", JsonTreeNode.FromString(s.StatusFor("review", "done"))),
            ("doneOn", Js.Date(doneOn.Date)),
            ("reviewer", JsonTreeNode.FromString(s.PersonId)),
            ("outcomeDecision", JsonTreeNode.FromString(decision.Id)),
            ("nextDueOn", nextDue.HasValue ? Js.Date(nextDue.Value) : null)));
        var evidence = s.Items("evidence").Select(e => Evidence(s, "review", "subjectReview", review.Id, e)).ToList();
        JsonTreeNode gates = Arr(ReviewGates(s, s.Rows.Get("review", review.Id) ?? review, app));
        JsonTreeNode data = ToolScope.Merge(
            s.WriteResult("review", write),
            ("reviewId", JsonTreeNode.FromString(review.Id)),
            ("doneOn", Js.Date(doneOn.Date)),
            ("reviewer", s.PrincipalRef(s.PersonId)),
            ("outcomeDecisionId", JsonTreeNode.FromString(decision.Id)),
            ("nextDueOn", nextDue.HasValue ? Js.Date(nextDue.Value) : JsonTreeNode.Null),
            ("gates", gates),
            ("evidenceLinkIds", Ids(s.Call.IsDryRun ? Array.Empty<string>() : evidence.ToArray())));
        int met = Js.Items(data, "gates").Count(g => Js.Flag(g, "met") == true);
        string summary = "The " + s.Label("review-type", review.Text("reviewType")).ToLowerInvariant() + (app == null ? string.Empty : " of " + AppName(app)) + " is done"
            + (nextDue.HasValue ? "; the next one is due " + nextDue.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + "." : ".")
            + (Js.Items(data, "gates").Count == 0 ? string.Empty : " It feeds " + Count(Js.Items(data, "gates").Count, "gate", "gates") + ", " + met + " met now.");
        return s.Answer(data, summary, s.RecordUrl("review", review.Id));
    }

    // The C25 cadence row of a review: its review type at its application's tier (a row with no tier applies to every tier).
    private static SnapshotRow? ReviewCadence(ToolScope s, StoredRow review, StoredRow? app)
    {
        string? type = review.Text("reviewType");
        string? tier = app?.Text("tier");
        return s.ActiveRows("review-cadence")
            .Where(r => Js.Text(r.Columns, "reviewTypeKey") == type && (Js.Text(r.Columns, "tierKey") is not string t || t == tier))
            .OrderBy(r => Js.Text(r.Columns, "tierKey") == null ? 1 : 0)
            .ThenBy(r => r.KeyPath, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    // The gates a review feeds on its application, two ways (document 23, B23-10):
    // - through a review-done requirement (C14) of the review's type, as OP-07 evaluates the gate now (triggered ones too);
    // - through its outcome decision, when the decision type the review records (C24) is a gate's Gate Decision Type: the
    //   architecture review records the design decision, which passes the Design gate once accepted. Balanced keeps no
    //   review-done requirement for the architecture type on the Design gate, because G-14 would then hold the design
    //   decision until the review is done, and the review is done only once that decision is accepted.
    // Each says whether it is met now.
    private static IEnumerable<JsonTreeNode> ReviewGates(ToolScope s, StoredRow review, StoredRow? app)
    {
        string? type = review.Text("reviewType");
        if (app == null || type == null)
        {
            yield break;
        }

        string? produces = Js.Text(s.ConfigRow("review-type", type)?.Columns, "producesDecisionTypeKey");
        StoredRow? outcome = review.Text("outcomeDecision") is string outcomeId ? s.Rows.Get("decision", outcomeId) : null;
        foreach (SnapshotRow gate in s.ActiveRows("gate").OrderBy(g => g.KeyPath, StringComparer.Ordinal))
        {
            string? guidelineOf(string? key) => key == null ? null : Js.Text(s.ConfigRow("guideline", key)?.Columns, "number");
            if (produces != null && Js.Text(gate.Columns, "gateDecisionTypeKey") == produces)
            {
                bool passed = outcome != null && outcome.Text("statusMeaning") == "accepted" && outcome.Text("decisionType") == produces;
                yield return Js.Obj(
                    ("gateKey", JsonTreeNode.FromString(gate.KeyPath)),
                    ("gateLabel", JsonTreeNode.FromString(s.Label("gate", gate.KeyPath))),
                    ("feedsBy", JsonTreeNode.FromString("gate-decision")),
                    ("requirementKey", JsonTreeNode.Null),
                    ("met", JsonTreeNode.FromBoolean(passed)),
                    ("guideline", JsonTreeNode.Null),
                    ("guidelineRef", JsonTreeNode.Null));
            }

            if (!s.ActiveRows("gate-requirement").Any(r => Js.Text(r.Columns, "gateKey") == gate.KeyPath && Js.Text(r.Columns, "requiredReviewTypeKey") == type))
            {
                continue;
            }

            EvaluationResult<JsonTreeNode?> readiness = s.Call.GateReadiness(app.Id, gate.KeyPath);
            foreach (JsonTreeNode item in Js.Items(readiness.Payload, "requirements").Where(r => Js.Text(r, "requirementKind") == "review-done" && Js.Text(r, "requiredReviewTypeKey") == type))
            {
                yield return Js.Obj(
                    ("gateKey", JsonTreeNode.FromString(gate.KeyPath)),
                    ("gateLabel", JsonTreeNode.FromString(s.Label("gate", gate.KeyPath))),
                    ("feedsBy", JsonTreeNode.FromString("requirement")),
                    ("requirementKey", JsonTreeNode.FromString(Js.Text(item, "requirementKey") ?? string.Empty)),
                    ("met", JsonTreeNode.FromBoolean(Js.Flag(item, "met") == true)),
                    ("guideline", Js.Str(guidelineOf(Js.Text(item, "guidelineKey")))),
                    ("guidelineRef", GuidelineRef(s, Js.Text(item, "guidelineKey"))));
            }
        }
    }

    // ---- 22. save_change_request_draft ----

    private static JsonTreeNode SaveChangeRequestDraft(ToolScope s)
    {
        s.RequireAiState("draft");
        StoredRow app = RequireApplication(s);
        RequireOwnerRelation(s, app.Id);
        SnapshotRow changeType = s.RequireKey("change-type", s.Text("changeTypeKey"), "changeTypeKey");
        string? model = s.Text("standardChangeModelKey");
        if (model != null)
        {
            s.RequireKey("standard-change-model", model, "standardChangeModelKey");
        }

        string? target = s.Text("plannedTargetId");
        StoredRow? targetRow = target == null ? null : s.RequireRow("deployment-target", target, "plannedTargetId");
        DateTime? start = Js.ParseMoment(s.Text("windowStart"));
        DateTime? end = Js.ParseMoment(s.Text("windowEnd"));
        if (start.HasValue && end.HasValue && end.Value <= start.Value)
        {
            throw s.Invalid("windowEnd", "The window ends at or before it starts.");
        }

        // The change management mode in force (C04 change-management-mode; D-08). Judgment call: the setting in force
        // applies; per-portfolio rows are not built.
        string mode = s.Setting("change-management-mode") ?? "built-in";
        if (mode == "linked" && (s.Text("externalChangeNumber") == null || s.Text("externalChangeUrl") == null))
        {
            throw s.Invalid("externalChangeNumber", "In linked mode the ITSM change number and URL are required.");
        }

        var affected = s.Items("affected").ToList();
        foreach (JsonTreeNode item in affected)
        {
            s.RequireRow("business-application", Js.Text(item, "businessApplicationId"), "affected.businessApplicationId");
            RequireListValue(s, "impact-role", Js.Text(item, "impactRoleKey"), "affected.impactRoleKey");
        }

        var columns = new List<(string, JsonTreeNode?)>
        {
            ("title", JsonTreeNode.FromString(s.RequireText("title"))),
            ("businessApplication", JsonTreeNode.FromString(app.Id)),
            ("changeType", JsonTreeNode.FromString(changeType.KeyPath)),
            ("standardChangeModel", Js.OptStr(model)),
            ("description", JsonTreeNode.FromString(s.RequireText("description"))),
            ("windowStart", start.HasValue ? Js.Utc(start.Value) : null),
            ("windowEnd", end.HasValue ? Js.Utc(end.Value) : null),
            ("plannedTarget", Js.OptStr(target)),
            ("externalChangeNumber", Js.OptStr(s.Text("externalChangeNumber"))),
            ("externalChangeUrl", Js.OptStr(s.Text("externalChangeUrl"))),
        };

        ComposedWrite write;
        string? changeId = s.Text("changeRequestId");
        if (changeId == null)
        {
            columns.Add(("mode", JsonTreeNode.FromString(mode)));
            columns.Add(("status", JsonTreeNode.FromString(s.StatusFor("change-request", "draft"))));
            write = s.Create("change-request", Js.Obj(columns.ToArray()));
        }
        else
        {
            StoredRow change = s.RequireRow("change-request", changeId, "changeRequestId");
            if (change.Text("statusMeaning") != "draft" || !Same(change.Text("createdByPrincipal"), s.PersonId))
            {
                throw s.Refused(McpErrorCodes.RuleViolation, "Only a draft change request you created is edited.");
            }

            write = s.Update("change-request", changeId, Js.Obj(columns.ToArray()));
        }

        var affectedIds = new List<string>();
        foreach (JsonTreeNode item in affected)
        {
            string other = Js.Text(item, "businessApplicationId")!;
            if (s.Query("affected-application", ("changeRequest", write.RowId), ("businessApplication", other)).Count == 0)
            {
                s.Create("affected-application", Js.Obj(("changeRequest", JsonTreeNode.FromString(write.RowId)), ("businessApplication", JsonTreeNode.FromString(other)), ("impactRole", JsonTreeNode.FromString(Js.Text(item, "impactRoleKey")!))));
            }

            affectedIds.Add(other);
        }

        string? targetStage = targetRow?.Text("targetStage");
        string? tier = app.Text("tier");
        SnapshotRow? policy = tier == null || targetStage == null ? null : s.ActiveRows("gate-policy").FirstOrDefault(r => Js.Text(r.Columns, "tierKey") == tier && Js.Text(r.Columns, "changeTypeKey") == changeType.KeyPath && Js.Text(r.Columns, "targetStageKey") == targetStage);
        var approvals = new List<JsonTreeNode>();
        EvaluationResult<JsonTreeNode?> required = s.Call.Evaluate(EngineOperation.ResolveRequiredApprovals, Js.Obj(
            ("decisionTypeKey", JsonTreeNode.FromString(EngineKeys.ChangeAuthorizationDecisionType)),
            ("tierKey", Js.OptStr(tier)),
            ("changeTypeKey", JsonTreeNode.FromString(changeType.KeyPath)),
            ("applicationKindKey", Js.OptStr(app.Text("applicationKind"))),
            ("targetStageKey", Js.OptStr(targetStage)),
            ("portfolioPath", Js.Strs(PortfolioPath(s, app.Text("portfolio"))))));
        if (required.Outcome == "required")
        {
            approvals.AddRange(Js.Items(required.Payload, "requirements").Select(r => ApprovalNeeded(s, EngineKeys.ChangeAuthorizationDecisionType, r)).Where(r => r != null).Select(r => r!));
        }

        JsonTreeNode data = ToolScope.Merge(
            s.WriteResult("change-request", write),
            ("mode", JsonTreeNode.FromString(s.Rows.Get("change-request", write.RowId)?.Text("mode") ?? mode)),
            ("affectedApplicationIds", Ids(affectedIds)),
            ("requirementsPreview", Js.Obj(
                ("tierKey", Js.Str(tier)),
                ("targetStageKey", Js.Str(targetStage)),
                ("requiresAuthorization", JsonTreeNode.FromBoolean(Js.Flag(changeType.Columns, "requiresAuthorization") ?? true)),
                ("gateLevel", Js.Str(policy == null ? null : Js.Text(policy.Columns, "gateLevel"))),
                ("approvals", Arr(approvals)))));
        string? id = Js.Text(data, "recordId");
        return s.Answer(data, "The change request is saved as a draft (" + mode + " mode).", id == null ? null : s.RecordUrl("change-request", id),
            id == null ? Array.Empty<NextStep>() : new[] { ToolScope.Next("check_standard_change", ("changeRequestId", JsonTreeNode.FromString(id))), ToolScope.Next("submit_record", ("subjectKind", JsonTreeNode.FromString("change-request")), ("subjectId", JsonTreeNode.FromString(id))) });
    }

    // ---- 23. check_standard_change (OP-12) ----

    private static JsonTreeNode CheckStandardChange(ToolScope s)
    {
        StoredRow change = s.RequireRow("change-request", s.Text("changeRequestId"), "changeRequestId");
        StoredRow? app = s.Rows.Get("business-application", change.Text("businessApplication") ?? string.Empty);
        StoredRow? target = change.Text("plannedTarget") == null ? null : s.Rows.Get("deployment-target", change.Text("plannedTarget")!);
        DateTime? start = Js.ParseMoment(change.Text("windowStart"));
        DateTime? end = Js.ParseMoment(change.Text("windowEnd"));
        int affected = 1 + s.Query("affected-application", ("changeRequest", change.Id)).Count(a => !Same(a.Text("businessApplication"), app?.Id));
        var kinds = app == null ? new List<string>() : s.Query("technical-component", ("businessApplication", app.Id)).Where(c => c.Text("retiredOn") == null).Select(c => c.Text("componentKind")).Where(k => k != null).Select(k => k!).Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var models = s.ActiveRows("standard-change-model").Where(m => Js.Text(m.Columns, "changeTypeKey") == change.Text("changeType")).Select(m =>
        {
            string? approving = Js.Text(m.Columns, "approvedByDecisionId");
            string status = approving == null ? "proposed" : s.Rows.Get("decision", approving)?.Text("statusMeaning") ?? "proposed";
            return Js.Obj(("modelKey", JsonTreeNode.FromString(m.KeyPath)), ("approvedByDecisionStatus", JsonTreeNode.FromString(status)));
        });
        JsonTreeNode input = Js.Obj(
            ("changeTypeKey", JsonTreeNode.FromString(change.Text("changeType") ?? "normal")),
            ("standardChangeModelKey", Js.OptStr(change.Text("standardChangeModel"))),
            ("change", Js.Obj(
                ("tierRank", app?.Text("tier") is string tier ? Js.Number(s.ConfigRow("tier", tier)?.Columns, "rank") is long rank ? JsonTreeNode.FromInt64(rank) : null : null),
                ("targetStageKeys", target?.Text("targetStage") is string stage ? Js.Strs(new[] { stage }) : null),
                ("affectedApplicationCount", JsonTreeNode.FromInt64(affected)),
                ("componentKindKeys", kinds.Count == 0 ? null : Js.Strs(kinds)),
                ("windowMinutes", start.HasValue && end.HasValue ? JsonTreeNode.FromInt64((long)Math.Max(0, (end.Value - start.Value).TotalMinutes)) : null))),
            ("models", Arr(models)));
        EvaluationResult<JsonTreeNode?> result = s.Call.Evaluate(EngineOperation.EvaluateStandardChange, input);
        if (result.Payload == null)
        {
            Reason? reason = result.Reasons.FirstOrDefault(r => r.Severity == ReasonSeverity.Error);
            throw s.Call.Fail(result.Outcome == "unavailable" ? "unavailable" : "invalid-input", reason?.Code ?? "input.malformed", reason?.Message ?? "The standard change check did not run.", reason?.Entity, reason?.Ref);
        }

        string? modelKey = Js.Text(result.Payload, "modelKey");
        string? approvedBy = Js.Text(result.Payload, "approvedByDecisionId") ?? (modelKey == null ? null : Js.Text(s.ConfigRow("standard-change-model", modelKey)?.Columns, "approvedByDecisionId"));
        bool qualifies = Js.Flag(result.Payload, "qualifies") == true;
        JsonTreeNode data = Js.Obj(
            ("qualifies", JsonTreeNode.FromBoolean(qualifies)),
            ("model", modelKey != null && ToolScope.IsGuid(approvedBy) ? Js.Obj(("key", JsonTreeNode.FromString(modelKey)), ("label", JsonTreeNode.FromString(s.Label("standard-change-model", modelKey))), ("approvedByDecisionId", JsonTreeNode.FromString(approvedBy!))) : JsonTreeNode.Null),
            ("failedConditions", Arr(Js.Items(result.Payload, "failedConditions").Where(c => Js.Text(c, "conditionKind") != null).Select(c => Js.Obj(("condition", JsonTreeNode.FromString(Js.Text(c, "conditionKind")!)), ("why", JsonTreeNode.FromString(Js.Text(c, "why") ?? "The condition is not met.")))))));
        return s.Answer(data, qualifies ? "The change fits the standard change model " + modelKey + "." : "The change does not fit a pre-approved standard change model.", s.RecordUrl("change-request", change.Id));
    }

    // ---- 24. get_change_request ----

    private static JsonTreeNode GetChangeRequest(ToolScope s)
    {
        StoredRow change = s.RequireRow("change-request", s.Text("changeRequestId"), "changeRequestId");
        string? decisionId = change.Text("authorizationDecision");
        StoredRow? decision = decisionId == null ? null : s.Rows.Get("decision", decisionId);
        string mode = change.Text("mode") ?? "built-in";
        string? linkedSource = null;
        if (mode == "linked")
        {
            // Linked-mode facts come from Evidence Links (OP-14); the record step does not read them yet.
            linkedSource = "none";
        }

        var deployments = s.Query("deployment-record", ("changeRequest", change.Id)).OrderBy(d => d.Text("finishedOn"), StringComparer.Ordinal).Select(d =>
        {
            StoredRow? target = s.Rows.Get("deployment-target", d.Text("deploymentTarget") ?? string.Empty);
            return Js.Obj(
                ("deploymentRecordId", JsonTreeNode.FromString(d.Id)),
                ("deploymentTargetId", JsonTreeNode.FromString(d.Text("deploymentTarget") ?? string.Empty)),
                ("targetLabel", JsonTreeNode.FromString(target?.Text("labelAsSeen") ?? string.Empty)),
                ("result", s.StatusValue(d, "result", "resultMeaning")),
                ("finishedOn", ToolScope.MomentOrNull(d.Text("finishedOn"))),
                ("flags", Flags(d)));
        });
        var links = s.Query("delivery-link", ("subjectChangeRequest", change.Id)).Select(l => (Id: l.Id, Category: "delivery", Row: l))
            .Concat(s.Query("evidence-link", ("subjectChangeRequest", change.Id)).Select(l => (Id: l.Id, Category: "evidence", Row: l)))
            .Where(l => (l.Row.Text("url") ?? string.Empty).StartsWith("https://", StringComparison.Ordinal))
            .Select(l => Js.Obj(
                ("linkId", JsonTreeNode.FromString(l.Id)),
                ("linkCategory", JsonTreeNode.FromString(l.Category)),
                ("linkKind", s.LabelledKey("link-kind", l.Row.Text("linkKind") ?? "unknown")),
                ("url", JsonTreeNode.FromString(l.Row.Text("url")!)),
                ("label", JsonTreeNode.FromString(l.Row.Text("label") ?? string.Empty))));
        JsonTreeNode data = Js.Obj(
            ("change", Js.Obj(
                ("changeRequestId", JsonTreeNode.FromString(change.Id)),
                ("title", JsonTreeNode.FromString(change.Text("title") ?? string.Empty)),
                ("description", JsonTreeNode.FromString(change.Text("description") ?? string.Empty)),
                ("businessApplicationId", JsonTreeNode.FromString(change.Text("businessApplication") ?? string.Empty)),
                ("changeType", s.LabelledKey("change-type", change.Text("changeType") ?? "unknown")),
                ("standardChangeModel", s.LabelledKey("standard-change-model", change.Text("standardChangeModel"))),
                ("status", s.StatusValue(change)),
                ("mode", JsonTreeNode.FromString(mode)),
                ("windowStart", ToolScope.MomentOrNull(change.Text("windowStart"))),
                ("windowEnd", ToolScope.MomentOrNull(change.Text("windowEnd"))),
                ("plannedTargetId", Js.Str(change.Text("plannedTarget"))),
                ("externalChangeNumber", Js.Str(change.Text("externalChangeNumber"))),
                ("externalChangeUrl", Js.Str(change.Text("externalChangeUrl"))),
                ("createdBy", s.PrincipalRef(change.Text("createdByPrincipal"))),
                ("isAiCreated", AiCreated(change)),
                ("performedBy", Stamp(s, change)))),
            ("authorization", Js.Obj(
                ("decisionId", Js.Str(decisionId)),
                ("status", decision == null ? JsonTreeNode.Null : s.StatusValue(decision)),
                ("approvalsSummary", ApprovalsSummary(s, decisionId)),
                ("settledBy", Js.Str(decision == null ? null : SettledBy(decision, s.Query("approval-record", ("decision", decision.Id)).Count))),
                ("linkedSource", Js.Str(linkedSource)))),
            ("affected", Arr(s.Query("affected-application", ("changeRequest", change.Id)).Select(a => Js.Obj(
                ("businessApplicationId", JsonTreeNode.FromString(a.Text("businessApplication") ?? string.Empty)),
                ("name", JsonTreeNode.FromString(s.Rows.Get("business-application", a.Text("businessApplication") ?? string.Empty) is StoredRow other ? AppName(other) : string.Empty)),
                ("impactRoleKey", Js.Str(a.Text("impactRole"))))))),
            ("deployments", Arr(deployments)),
            ("result", ListValue(s, "change-result", change.Text("result"))),
            ("closable", JsonTreeNode.FromBoolean(IsClosable(s, change))),
            ("postChangeReviews", Arr(PostChangeReviewsOf(s, change).Select(r => Js.Obj(
                ("reviewId", JsonTreeNode.FromString(r.Id)),
                ("status", s.StatusValue(r)),
                ("dueOn", ToolScope.DateOrNull(r.Text("dueOn"))),
                ("doneOn", ToolScope.DateOrNull(r.Text("doneOn"))))))),
            ("links", Arr(links)));
        return s.Answer(data, "The change " + (change.Text("title") ?? change.Id) + " is " + (change.Text("statusMeaning") ?? "unknown") + ".", s.RecordUrl("change-request", change.Id));
    }

    private static JsonTreeNode Flags(StoredRow deployment)
    {
        return Js.Obj(
            ("outsideWindow", JsonTreeNode.FromBoolean(deployment.Flag("isOutsideWindow"))),
            ("otherPath", JsonTreeNode.FromBoolean(deployment.Flag("isOtherPath"))),
            ("missingApproval", JsonTreeNode.FromBoolean(deployment.Flag("isMissingApproval"))),
            ("aiOnlyCodeApproval", JsonTreeNode.FromBoolean(deployment.Flag("isAiOnlyCodeApproval"))));
    }

    // ---- 25. get_change_schedule ----

    private static JsonTreeNode GetChangeSchedule(ToolScope s)
    {
        DateTime from = (Js.ParseMoment(s.RequireText("from")) ?? s.Now).Date;
        DateTime to = (Js.ParseMoment(s.RequireText("to")) ?? s.Now).Date.AddDays(1);
        if (to <= from)
        {
            throw s.Invalid("to", "to is before from.");
        }

        IReadOnlyList<string> targets = s.Strings("deploymentTargetIds");
        IReadOnlyList<string> stages = s.Strings("targetStageKeys");
        IReadOnlyList<string> meanings = s.Strings("statusMeanings");
        string? app = s.Text("businessApplicationId");
        HashSet<string>? inPortfolio = PortfolioApplications(s, s.Text("portfolioId"), "portfolioId");
        var rows = s.Rows.Query(RowQuery.For("change-request")).Where(c =>
        {
            if (inPortfolio != null && !inPortfolio.Contains(c.Text("businessApplication") ?? string.Empty))
            {
                return false;
            }

            DateTime? start = Js.ParseMoment(c.Text("windowStart"));
            DateTime? end = Js.ParseMoment(c.Text("windowEnd")) ?? start;
            if (!start.HasValue || start.Value >= to || (end ?? start).Value < from)
            {
                return false;
            }

            StoredRow? target = c.Text("plannedTarget") == null ? null : s.Rows.Get("deployment-target", c.Text("plannedTarget")!);
            return (app == null || Same(c.Text("businessApplication"), app))
                && (targets.Count == 0 || targets.Contains(c.Text("plannedTarget") ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                && (stages.Count == 0 || stages.Contains(target?.Text("targetStage") ?? string.Empty, StringComparer.Ordinal))
                && (meanings.Count == 0 || meanings.Contains(c.Text("statusMeaning") ?? string.Empty, StringComparer.Ordinal));
        }).OrderBy(c => c.Text("windowStart"), StringComparer.Ordinal).ThenBy(c => c.Id, StringComparer.Ordinal).ToList();
        List<StoredRow> page = s.Page(rows, out string? next);
        JsonTreeNode items = Arr(page.Select(c =>
        {
            StoredRow? target = c.Text("plannedTarget") == null ? null : s.Rows.Get("deployment-target", c.Text("plannedTarget")!);
            StoredRow? application = s.Rows.Get("business-application", c.Text("businessApplication") ?? string.Empty);
            string? decision = c.Text("authorizationDecision");
            StoredRow? authorization = decision == null ? null : s.Rows.Get("decision", decision);
            return Js.Obj(
                ("changeRequestId", JsonTreeNode.FromString(c.Id)),
                ("title", JsonTreeNode.FromString(c.Text("title") ?? string.Empty)),
                ("businessApplicationId", JsonTreeNode.FromString(c.Text("businessApplication") ?? string.Empty)),
                ("applicationName", JsonTreeNode.FromString(application == null ? string.Empty : AppName(application))),
                ("target", target == null ? JsonTreeNode.Null : Js.Obj(("deploymentTargetId", JsonTreeNode.FromString(target.Id)), ("label", JsonTreeNode.FromString(target.Text("labelAsSeen") ?? target.Id)), ("targetStageKey", Js.Str(target.Text("targetStage"))))),
                ("windowStart", ToolScope.MomentOrNull(c.Text("windowStart"))),
                ("windowEnd", ToolScope.MomentOrNull(c.Text("windowEnd"))),
                ("changeType", s.LabelledKey("change-type", c.Text("changeType") ?? "unknown")),
                ("status", s.StatusValue(c)),
                ("authorized", JsonTreeNode.FromBoolean(authorization?.Text("statusMeaning") == "accepted")),
                ("authorizationStatus", authorization == null ? JsonTreeNode.Null : s.StatusValue(authorization)));
        }));
        return s.Answer(Js.Obj(("items", items), ("skipToken", Js.OptStr(next))), rows.Count + " planned changes in the window. The schedule is read only: no booking and no freeze.");
    }

    // ---- 26. record_change_result ----

    private static JsonTreeNode RecordChangeResult(ToolScope s)
    {
        s.RequireAiState("as-reported");
        StoredRow change = s.RequireRow("change-request", s.Text("changeRequestId"), "changeRequestId");
        RequireOwnerRelation(s, change.Text("businessApplication") ?? string.Empty);
        string result = s.RequireText("resultKey");
        RequireListValue(s, "change-result", result, "resultKey");
        if (change.Text("statusMeaning") == "completed" && !Same(change.Text("result"), result))
        {
            throw s.Refused(McpErrorCodes.RuleViolation, "The change is closed with its result, so the result no longer changes.");
        }

        // Only the Result, as reported (document 24, tool 26). The change's status is not the caller's to move here: a
        // person closes the change with complete_change, and an AI client never completes a record (OP-08, G-08).
        string? previous = change.Text("result");
        ComposedWrite write = s.Update("change-request", change.Id, Js.Obj(("result", JsonTreeNode.FromString(result))));
        var evidence = s.Items("evidence").Select(e => Evidence(s, "change-request", "subjectChangeRequest", change.Id, e)).ToList();
        StoredRow? review = PostChangeReview(s, change, previous, result);

        JsonTreeNode data = ToolScope.Merge(
            s.WriteResult("change-request", write),
            ("result", ListValue(s, "change-result", result)),
            ("postChangeReviewId", Js.Str(s.Call.IsDryRun || review == null ? null : review.Id)),
            ("evidenceLinkIds", Ids(s.Call.IsDryRun ? Array.Empty<string>() : evidence.ToArray())));
        return s.Answer(
            data,
            "The change result is recorded as reported" + (review == null ? "." : ", and a post-change review is open, due " + review.Text("dueOn") + ".") + " A person closes the change on its page.",
            s.RecordUrl("change-request", change.Id));
    }

    // ---- 64. complete_change ----

    // P03 step 8 (document 23, B23-10): the change owner closes a scheduled change with its result. Closing is a person's
    // act (OP-08, G-08: an AI actor never sets completed), so the tool is served only on the person entry, as
    // answer_approval is. Only a change in progress (Balanced: Scheduled), that is, authorized, closes, and a submitted
    // change whose type Allows Authorization After (C20, an emergency: document 22, O11 Authorization Decision); a change
    // still waiting for its authorization, rejected, withdrawn or closed does not. The result is the one given, or the one
    // already recorded with record_change_result. A failed, rolled back or partly succeeded result, or any result of a
    // change type that Requires Review After (C20), opens the post-change review once (P03 step 9).
    private static JsonTreeNode CompleteChange(ToolScope s)
    {
        StoredRow change = s.RequireRow("change-request", s.Text("changeRequestId"), "changeRequestId");
        RequireOwnerRelation(s, change.Text("businessApplication") ?? string.Empty);
        string meaning = change.Text("statusMeaning") ?? "draft";
        if (!IsClosable(s, change))
        {
            string why = meaning switch
            {
                "completed" => "it is already closed",
                "submitted" => "its authorization is not accepted yet (only a change type that allows authorization after, such as an emergency, closes before it)",
                "draft" => "it is a draft, not yet submitted and authorized",
                _ => "it is " + meaning.Replace("-", " "),
            };
            throw s.Refused(McpErrorCodes.RuleViolation, "Only an authorized, scheduled change is closed, and " + why + ".");
        }

        string? previous = change.Text("result");
        string result = s.Text("resultKey") ?? previous
            ?? throw s.Invalid("resultKey", "resultKey is required: the change has no recorded result yet, and a closed change names how it ended.");
        RequireListValue(s, "change-result", result, "resultKey");
        ComposedWrite write = s.Update("change-request", change.Id, Js.Obj(
            ("status", JsonTreeNode.FromString(s.StatusFor("change-request", "completed"))),
            ("result", JsonTreeNode.FromString(result))));
        var evidence = s.Items("evidence").Select(e => Evidence(s, "change-request", "subjectChangeRequest", change.Id, e)).ToList();
        StoredRow? review = PostChangeReview(s, change, previous, result);
        JsonTreeNode data = ToolScope.Merge(
            s.WriteResult("change-request", write),
            ("result", ListValue(s, "change-result", result)),
            ("postChangeReviewId", Js.Str(s.Call.IsDryRun || review == null ? null : review.Id)),
            ("evidenceLinkIds", Ids(s.Call.IsDryRun ? Array.Empty<string>() : evidence.ToArray())));
        string summary = "The change " + (change.Text("title") ?? change.Id) + " is closed with the result " + s.Label("list-value", "change-result/" + result).ToLowerInvariant() + "."
            + (review == null ? string.Empty : " A post-change review is open, due " + review.Text("dueOn") + ".");
        return s.Answer(data, summary, s.RecordUrl("change-request", change.Id),
            review == null ? Array.Empty<NextStep>() : new[] { ToolScope.Next("get_review", ("reviewId", JsonTreeNode.FromString(review.Id))) });
    }

    // The post-change review of a change (P03 step 9; document 20, C20): a failed, rolled back or partly succeeded result,
    // or any result of a change type that Requires Review After (C20, as Balanced's emergency type), opens an O21 Review
    // of type post-change, due Review Due Days working days from today (C20; the working-days setting; 14 days when the
    // type sets none). It opens once per change: a new result opens one only when the earlier result needed none. The
    // review's Description names the change, so its page can link back.
    private static StoredRow? PostChangeReview(ToolScope s, StoredRow change, string? previousResult, string result)
    {
        SnapshotRow? type = s.ConfigRow("change-type", change.Text("changeType"));
        bool always = Js.Flag(type?.Columns, "requiresReviewAfter") == true;
        bool Needs(string? r) => r != null && (always || r == "failed" || r == "rolled-back" || r == "partly-succeeded");
        if (!Needs(result) || Needs(previousResult) || change.Text("businessApplication") is not string app
            || s.ConfigRow("review-type", "post-change") is not SnapshotRow postChange || !postChange.IsActive)
        {
            return null;
        }

        long? days = Js.Number(type?.Columns, "reviewDueDays");
        DateTime due = days.HasValue ? AddWorkingDays(s, s.Now.Date, days.Value) : s.Now.Date.AddDays(14);
        string name = ChangeReference(change) + " " + (change.Text("title") ?? string.Empty);
        string reviewId = s.Create("review", Js.Obj(
            ("businessApplication", JsonTreeNode.FromString(app)),
            ("reviewType", JsonTreeNode.FromString("post-change")),
            ("status", JsonTreeNode.FromString(s.StatusFor("review", "due"))),
            ("trigger", JsonTreeNode.FromString("request")),
            ("dueOn", Js.Date(due)),
            ("description", JsonTreeNode.FromString("Post-change review of " + name.Trim() + ", which ended " + result.Replace("-", " ") + ".")))).RowId;
        return s.Rows.Get("review", reviewId);
    }

    // True when complete_change would close the change: it is in progress (authorized and scheduled), or submitted with
    // a change type that Allows Authorization After (C20).
    private static bool IsClosable(ToolScope s, StoredRow change)
    {
        string meaning = change.Text("statusMeaning") ?? "draft";
        return meaning == "in-progress"
            || (meaning == "submitted" && Js.Flag(s.ConfigRow("change-type", change.Text("changeType"))?.Columns, "allowsAuthorizationAfter") == true);
    }

    // How a post-change review names its change in its Description: the record number, or "change <id>" for a change
    // that has none. O21 has no change column (document 22, B22-10), so get_change_request finds the reviews by it.
    private static string ChangeReference(StoredRow change)
    {
        return change.Text("recordNumber") ?? "change " + change.Id;
    }

    // The post-change reviews of a change: reviews of type post-change on its application whose Description names it.
    private static IEnumerable<StoredRow> PostChangeReviewsOf(ToolScope s, StoredRow change)
    {
        string reference = ChangeReference(change) + " ";
        return change.Text("businessApplication") is not string app
            ? Enumerable.Empty<StoredRow>()
            : s.Query("review", ("businessApplication", app), ("reviewType", "post-change"))
                .Where(r => (r.Text("description") ?? string.Empty).IndexOf(reference, StringComparison.Ordinal) >= 0)
                .OrderBy(r => r.Text("dueOn") ?? "~", StringComparer.Ordinal);
    }

    // Days counted as the working-days setting says (Monday to Friday as shipped), as OP-04 counts an emergency follow-up.
    private static DateTime AddWorkingDays(ToolScope s, DateTime from, long days)
    {
        var names = new Dictionary<string, DayOfWeek>(StringComparer.Ordinal)
        {
            ["monday"] = DayOfWeek.Monday,
            ["tuesday"] = DayOfWeek.Tuesday,
            ["wednesday"] = DayOfWeek.Wednesday,
            ["thursday"] = DayOfWeek.Thursday,
            ["friday"] = DayOfWeek.Friday,
            ["saturday"] = DayOfWeek.Saturday,
            ["sunday"] = DayOfWeek.Sunday,
        };
        JsonTreeNode? setting = s.SettingJson(EngineKeys.WorkingDays);
        var working = new HashSet<DayOfWeek>(setting != null && setting.Kind == JsonTreeKind.Array
            ? setting.Items.Where(i => i.Kind == JsonTreeKind.String && names.ContainsKey(i.GetString())).Select(i => names[i.GetString()])
            : new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday });
        if (working.Count == 0)
        {
            return from.AddDays(days);
        }

        DateTime due = from;
        for (long counted = 0; counted < days;)
        {
            due = due.AddDays(1);
            if (working.Contains(due.DayOfWeek))
            {
                counted++;
            }
        }

        return due;
    }
}
