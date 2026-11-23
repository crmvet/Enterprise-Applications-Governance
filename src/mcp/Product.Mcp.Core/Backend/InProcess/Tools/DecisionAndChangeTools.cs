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
        string subjectKind = s.RequireText("subjectKind");
        string subjectId = s.RequireText("subjectId");
        var subjectColumns = new List<(string, JsonTreeNode?)>();
        string? applicationId;
        if (subjectKind == "review")
        {
            // O09 has no review column: the decision points at the review's application (judgment call).
            StoredRow review = s.RequireRow("review", subjectId, "subjectId");
            applicationId = review.Text("businessApplication");
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

        var columns = new List<(string, JsonTreeNode?)>(subjectColumns)
        {
            ("decisionType", JsonTreeNode.FromString(type)),
            ("subjectKind", JsonTreeNode.FromString(subjectKind)),
            ("statement", JsonTreeNode.FromString(s.RequireText("statement"))),
            ("context", JsonTreeNode.FromString(s.RequireText("context") + (subjectKind == "review" ? "\nReview: " + subjectId : string.Empty))),
            ("optionsConsidered", s.Items("optionsConsidered").Count == 0 ? null : JsonTreeNode.FromString(Js.Write(Arr(s.Items("optionsConsidered"))))),
            ("outcome", JsonTreeNode.FromString(outcome)),
            ("reason", JsonTreeNode.FromString(s.RequireText("reason"))),
            ("confidence", Js.OptStr(s.Text("confidenceKey"))),
            ("supersedes", Js.OptStr(s.Text("supersedesDecisionId"))),
            ("amends", Js.OptStr(s.Text("amendsDecisionId"))),
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
                throw s.Refused(McpErrorCodes.RuleViolation, "The decision is already on approval cards. Withdraw it and propose a new one.");
            }

            write = s.Update("decision", decisionId, Js.Obj(columns.ToArray()));
        }

        var evidence = new List<string>();
        foreach (JsonTreeNode item in s.Items("evidence"))
        {
            evidence.Add(Evidence(s, "decision", "subjectDecision", write.RowId, item));
        }

        JsonTreeNode data = ToolScope.Merge(
            s.WriteResult("decision", write),
            ("decider", Decider(s, type, applicationId)),
            ("approvalsNeeded", Arr(ApprovalsNeeded(s, write.RowId, type))),
            ("evidenceLinkIds", Ids(s.Call.IsDryRun ? Array.Empty<string>() : evidence.ToArray())));
        string? id = Js.Text(data, "recordId");
        return s.Answer(data, "The decision is recorded as proposed. People accept or reject it on approval cards once it is submitted.", id == null ? null : s.RecordUrl("decision", id),
            id == null ? Array.Empty<NextStep>() : new[] { ToolScope.Next("submit_record", ("subjectKind", JsonTreeNode.FromString("decision")), ("subjectId", JsonTreeNode.FromString(id))) });
    }

    // The accountable holder of a decision type (C31 letter accountable): the most specific matching row's role or owner
    // role, and the person when an owner role has a holder on the subject's application. The caller never sets it.
    private static JsonTreeNode Decider(ToolScope s, string type, string? applicationId)
    {
        StoredRow? app = applicationId == null ? null : s.Rows.Get("business-application", applicationId);
        SnapshotRow? right = s.ActiveRows("decision-right")
            .Where(r => Js.Text(r.Columns, "decisionTypeKey") == type && Js.Text(r.Columns, "letter") == "accountable")
            .Where(r => Js.Text(r.Columns, "tierKey") is not string t || t == app?.Text("tier"))
            .OrderByDescending(r => (Js.Text(r.Columns, "tierKey") == null ? 0 : 1) + (Js.Text(r.Columns, "changeTypeKey") == null ? 0 : 1))
            .ThenBy(r => r.KeyPath, StringComparer.Ordinal)
            .FirstOrDefault()
            ?? throw s.Call.Fail("unavailable", "decision-right.accountable", "The decision type " + type + " has no accountable decision right in the configuration in force.", "Decision Right", type);
        string? ownerRole = Js.Text(right.Columns, "ownerRoleKey");
        string? role = Js.Text(right.Columns, "roleKey");
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
        string mainEntity = entity;
        string mainId = id;
        bool decisionSubject = false;

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
                    engineAccepted = EngineAcceptance(s, decisionId);
                    requested = s.Call.RequestApprovals(decisionId);
                }
                else
                {
                    s.Call.Warn("change.linked-mode", "In linked mode the client's ITSM tool authorizes the change, so no built-in approval card starts. A recorded number alone never counts as authorization.", "Change Request", id);
                }

                break;
            case "decision":
                RequireCreator(s, subject, "createdByPrincipal");
                if (subject.Text("statusMeaning") != "proposed")
                {
                    throw s.Refused(McpErrorCodes.RuleViolation, "Only a proposed decision is submitted; this one is " + subject.Text("statusMeaning") + ".");
                }

                decisionId = id;
                decisionSubject = true;
                engineAccepted = EngineAcceptance(s, decisionId);
                requested = s.Call.RequestApprovals(decisionId);
                break;
            default:
                RequireCreator(s, subject, "instructedBy");
                requested = s.Call.SubmitConfigurationDraft(id);
                decisionId = Js.Text(requested, "decisionId");
                break;
        }

        var approvals = Js.Strings(requested, "approvalRecordIds").Select(r => s.Rows.Get("approval-record", r)).Where(r => r != null).Select(r => ApprovalRequested(s, r!)).ToList();
        StoredRow main = s.Rows.Get(mainEntity, mainId)!;
        List<(string, JsonTreeNode?)> result = mainEntity == "configuration-draft"
            ? s.WriteResult(main, false, hasStatus: true, statusColumn: "draftStatus", meaningColumn: "draftStatus")
            : s.WriteResult(main, false);
        if (decisionSubject)
        {
            // The caller's act leaves the decision proposed. An acceptance the engine makes for the locked list is its own
            // act, reported in engineAccepted (no tool output ever holds accepted).
            result.RemoveAll(m => m.Item1 == "status");
            result.Add(("status", ToolScope.StatusValue("proposed", s.Label("status", s.StatusFor("decision", "proposed")))));
        }

        JsonTreeNode data = ToolScope.Merge(
            result,
            ("subjectKind", JsonTreeNode.FromString(kind)),
            ("subjectId", JsonTreeNode.FromString(id)),
            ("decisionId", Js.Str(decisionId == null || (s.Call.IsDryRun && decisionId != id) ? null : decisionId)),
            ("approvalsRequested", Arr(s.Call.IsDryRun ? Array.Empty<JsonTreeNode>() : approvals)),
            ("standardChangeApplied", JsonTreeNode.Null),
            ("engineAccepted", engineAccepted ?? JsonTreeNode.Null));
        string summary = engineAccepted != null
            ? "Submitted. The engine accepted the decision under the locked list, with no card."
            : "Submitted. " + approvals.Count + " approval card(s) start; only people answer them.";
        return s.Answer(data, summary, s.RecordUrl(mainEntity, mainId), ToolScope.Next("get_approval_status", ("subjectKind", JsonTreeNode.FromString(kind == "configuration-draft" ? "decision" : kind)), ("subjectId", JsonTreeNode.FromString(kind == "configuration-draft" ? decisionId ?? id : id))));
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

        return s.Create("decision", Js.Obj(
            ("decisionType", JsonTreeNode.FromString(type)),
            ("subjectKind", JsonTreeNode.FromString(kind)),
            (subjectColumn, JsonTreeNode.FromString(subject.Id)),
            ("subjectApplication", Js.OptStr(subject.Text("businessApplication"))),
            ("statement", JsonTreeNode.FromString((kind == "intake-request" ? "Accept the intake request: " : "Authorize the change: ") + (subject.Text("title") ?? subject.Id))),
            ("status", JsonTreeNode.FromString(s.StatusFor("decision", "proposed"))))).RowId;
    }

    // { decisionId, condition } when OP-02 answers engine-accept for the decision (the locked list), else null.
    private static JsonTreeNode? EngineAcceptance(ToolScope s, string decisionId)
    {
        EvaluationResult<JsonTreeNode?> required = s.Call.RequiredApprovals(decisionId);
        if (required.Outcome != "engine-accept")
        {
            return null;
        }

        string condition = Js.Text(Js.Member(required.Payload, "acceptanceBasis"), "condition") ?? "lowest-tier";
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
            case "application-draft":
                RequireCreator(s, subject, "createdByPrincipal");
                if (subject.Text("statusMeaning") != "draft")
                {
                    throw s.Refused(McpErrorCodes.RuleViolation, "Only a draft application is withdrawn here; this one is " + subject.Text("statusMeaning") + ".");
                }

                write = s.Update(entity, id, Js.Obj(("status", JsonTreeNode.FromString(s.StatusFor(entity, "archived")))));
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
        return s.Answer(data, "Withdrawn. " + cancelled.Count + " pending approval card(s) are cancelled.", s.RecordUrl(entity, id));
    }

    // ---- 20. get_decision_log ----

    private static JsonTreeNode GetDecisionLog(ToolScope s)
    {
        string? subjectKind = s.Text("subjectKind");
        string? subjectId = s.Text("subjectId");
        string? app = s.Text("businessApplicationId");
        IReadOnlyList<string> types = s.Strings("decisionTypeKeys");
        IReadOnlyList<string> meanings = s.Strings("statusMeanings");
        DateTime? from = Js.ParseMoment(s.Text("from"));
        DateTime? to = Js.ParseMoment(s.Text("to"));
        int hidden = 0;
        var rows = new List<StoredRow>();
        foreach (StoredRow d in s.Rows.Query(RowQuery.For("decision")))
        {
            string kind = d.Text("subjectKind") ?? string.Empty;
            string? id = SubjectIdOf(d);
            if ((subjectKind != null && (kind != subjectKind || !Same(id, subjectId)))
                || (app != null && !Same(d.Text("subjectApplication"), app))
                || (types.Count > 0 && !types.Contains(d.Text("decisionType") ?? string.Empty, StringComparer.Ordinal))
                || (meanings.Count > 0 && !meanings.Contains(d.Text("statusMeaning") ?? string.Empty, StringComparer.Ordinal)))
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
        JsonTreeNode items = Arr(page.Select(d => Js.Obj(
            ("decisionId", JsonTreeNode.FromString(d.Id)),
            ("decisionType", s.LabelledKey("decision-type", d.Text("decisionType") ?? "unknown")),
            ("subjectKind", JsonTreeNode.FromString(d.Text("subjectKind") ?? "business-application")),
            ("subjectId", JsonTreeNode.FromString(SubjectIdOf(d) ?? d.Id)),
            ("statement", JsonTreeNode.FromString(d.Text("statement") ?? string.Empty)),
            ("reason", JsonTreeNode.FromString(d.Text("reason") ?? string.Empty)),
            ("outcome", d.Text("outcome") == null ? JsonTreeNode.Null : Js.Obj(("key", JsonTreeNode.FromString(d.Text("outcome")!)), ("label", JsonTreeNode.FromString(s.Label("decision-outcome", d.Text("decisionType") + "/" + d.Text("outcome")))))),
            ("status", s.StatusValue(d)),
            ("decider", s.PrincipalRef(d.Text("decider"))),
            ("decidedOn", ToolScope.MomentOrNull(d.Text("decidedOn"))),
            ("supersedes", Js.Str(d.Text("supersedes"))),
            ("supersededBy", Js.Str(d.Text("supersededBy"))),
            ("amends", Js.Str(d.Text("amends"))),
            ("approvalsSummary", ApprovalsSummary(s, d.Id)),
            ("configurationSetVersion", JsonTreeNode.FromInt64(d.Number("configurationSetVersion") ?? s.Session.ConfigurationSetVersion)),
            ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("decision", d.Id))))));
        return s.Answer(Js.Obj(("items", items), ("skipToken", Js.OptStr(next))), ordered.Count + " decisions match.");
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

        EvaluationResult<JsonTreeNode?>? status = s.Call.ApprovalStatus(decisionId, out EvaluationResult<JsonTreeNode?> required, out IReadOnlyList<StoredRow> records);
        var requirements = Js.Items(required.Payload, "requirements").Concat(Js.Items(required.Payload, "consultations")).ToList();
        JsonTreeNode? counts = Js.Member(status?.Payload, "countsByRecord");
        var states = Js.Items(status?.Payload, "requirements").ToDictionary(r => Js.Text(r, "requirementKey") ?? string.Empty, r => r, StringComparer.Ordinal);
        var requiredList = new List<JsonTreeNode>();
        var missing = new List<JsonTreeNode>();
        foreach (JsonTreeNode requirement in requirements)
        {
            string key = Js.Text(requirement, "ruleKey") ?? Js.Text(requirement, "decisionRightKey") ?? "accountable";
            string? role = Js.Text(requirement, "approverRoleKey");
            string? ownerRole = Js.Text(requirement, "approverOwnerRoleKey");
            if ((role == null) == (ownerRole == null))
            {
                continue;
            }

            SnapshotRow? rule = s.ConfigRow("approval-rule", Js.Text(requirement, "ruleKey"));
            long minimum = Js.Number(requirement, "minimumCount") ?? (rule == null ? null : Js.Number(rule.Columns, "minimumCount")) ?? 1;
            bool consultation = Js.Flag(requirement, "isConsultation") == true;
            int received = records.Count(r => (r.Text("approvalRule") ?? r.Text("decisionRight")) == key && counts != null && Js.Flag(counts, r.Id) == true);
            requiredList.Add(Js.Obj(
                ("approvalRuleKey", JsonTreeNode.FromString(key)),
                ("requiredRoleKey", Js.OptStr(role)),
                ("requiredOwnerRoleKey", Js.OptStr(ownerRole)),
                ("approvalMode", JsonTreeNode.FromString(Js.Text(requirement, "approvalMode") ?? (rule == null ? null : Js.Text(rule.Columns, "approvalMode")) ?? "first-to-respond")),
                ("minimumCount", JsonTreeNode.FromInt64(minimum)),
                ("received", JsonTreeNode.FromInt64(received)),
                ("isConsultation", JsonTreeNode.FromBoolean(consultation))));
            if (!consultation && received < minimum && Js.Text(states.TryGetValue(key, out JsonTreeNode? state) ? state : null, "state") != "satisfied")
            {
                missing.Add(Js.Obj(("approvalRuleKey", JsonTreeNode.FromString(key)), ("requiredRoleKey", Js.OptStr(role)), ("requiredOwnerRoleKey", Js.OptStr(ownerRole)), ("stillNeeded", JsonTreeNode.FromInt64(Math.Max(1, minimum - received)))));
            }
        }

        JsonTreeNode recordList = Arr(records.Select(r => Js.Obj(
            ("approvalRecordId", JsonTreeNode.FromString(r.Id)),
            ("approvalRuleKey", JsonTreeNode.FromString(r.Text("approvalRule") ?? r.Text("decisionRight") ?? "accountable")),
            ("approver", s.PrincipalRef(r.Text("approver"))),
            ("approverActorKind", Js.Str(r.Text("approverActorMeaning") ?? r.Text("approverActorKind"))),
            ("approverPrincipalKind", Js.Str(r.Text("approverPrincipalKind"))),
            ("outcome", s.StatusValue(r, "outcome", "outcomeMeaning") is JsonTreeNode o && o.Kind != JsonTreeKind.Null ? o : ToolScope.StatusValue("pending", null)),
            ("typedReason", Js.Str(r.Text("typedReason"))),
            ("respondedOn", ToolScope.MomentOrNull(r.Text("respondedOn"))),
            ("countsTowardRequirement", JsonTreeNode.FromBoolean(counts != null ? Js.Flag(counts, r.Id) == true : r.Flag("countsTowardRequirement"))),
            ("refusedBySeparationRuleKey", JsonTreeNode.Null))));
        bool ready = status == null ? required.Outcome == "engine-accept" : status.Outcome == "satisfied";
        JsonTreeNode data = Js.Obj(("decisionId", JsonTreeNode.FromString(decisionId)), ("required", Arr(requiredList)), ("records", recordList), ("missing", Arr(missing)), ("ready", JsonTreeNode.FromBoolean(ready)));
        return s.Answer(data, ready ? "The approvals the rules require are in." : missing.Count + " approval(s) are still missing. Only a person's answer on a card counts.", s.RecordUrl("decision", decisionId));
    }

    private static string? IntakeDecisionOf(ToolScope s, StoredRow intake)
    {
        if (!CanReadIntake(s, intake))
        {
            throw s.NotFound("Intake Request", intake.Id, "subjectId");
        }

        return intake.Text("triageDecision") ?? s.Query("decision", ("subjectIntakeRequest", intake.Id)).OrderByDescending(d => d.Id, StringComparer.Ordinal).FirstOrDefault()?.Id;
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
        string due = s.Text("dueOn") ?? s.Today;
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
                ("externalChangeUrl", Js.Str(change.Text("externalChangeUrl"))))),
            ("authorization", Js.Obj(
                ("decisionId", Js.Str(decisionId)),
                ("status", decision == null ? JsonTreeNode.Null : s.StatusValue(decision)),
                ("approvalsSummary", ApprovalsSummary(s, decisionId)),
                ("linkedSource", Js.Str(linkedSource)))),
            ("affected", Arr(s.Query("affected-application", ("changeRequest", change.Id)).Select(a => Js.Obj(
                ("businessApplicationId", JsonTreeNode.FromString(a.Text("businessApplication") ?? string.Empty)),
                ("name", JsonTreeNode.FromString(s.Rows.Get("business-application", a.Text("businessApplication") ?? string.Empty) is StoredRow other ? AppName(other) : string.Empty)),
                ("impactRoleKey", Js.Str(a.Text("impactRole"))))))),
            ("deployments", Arr(deployments)),
            ("result", ListValue(s, "change-result", change.Text("result"))),
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
        var rows = s.Rows.Query(RowQuery.For("change-request")).Where(c =>
        {
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
                ("authorized", JsonTreeNode.FromBoolean(decision != null && s.Rows.Get("decision", decision)?.Text("statusMeaning") == "accepted")));
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
        // Only the Result, as reported (document 24, tool 26). The change's status is not the caller's to move: an AI
        // client never completes a record (OP-08, G-08).
        ComposedWrite write = s.Update("change-request", change.Id, Js.Obj(("result", JsonTreeNode.FromString(result))));
        var evidence = s.Items("evidence").Select(e => Evidence(s, "change-request", "subjectChangeRequest", change.Id, e)).ToList();

        // The engine opens a post-change review when the change type requires one or the result failed (document 20, C20).
        SnapshotRow? type = s.ConfigRow("change-type", change.Text("changeType"));
        bool failed = result == "failed" || result == "rolled-back" || result == "partly-succeeded";
        string? reviewId = null;
        if ((Js.Flag(type?.Columns, "requiresPostChangeReview") == true || failed) && s.ConfigRow("review-type", "post-change") is SnapshotRow postChange && postChange.IsActive && change.Text("businessApplication") is string app)
        {
            reviewId = s.Create("review", Js.Obj(
                ("businessApplication", JsonTreeNode.FromString(app)),
                ("reviewType", JsonTreeNode.FromString("post-change")),
                ("status", JsonTreeNode.FromString(s.StatusFor("review", "due"))),
                ("trigger", JsonTreeNode.FromString("request")),
                ("dueOn", Js.Date(s.Now.AddDays(14))),
                ("description", JsonTreeNode.FromString("Post-change review of " + (change.Text("title") ?? change.Id))))).RowId;
        }

        JsonTreeNode data = ToolScope.Merge(
            s.WriteResult("change-request", write),
            ("result", ListValue(s, "change-result", result)),
            ("postChangeReviewId", Js.Str(s.Call.IsDryRun ? null : reviewId)),
            ("evidenceLinkIds", Ids(s.Call.IsDryRun ? Array.Empty<string>() : evidence.ToArray())));
        return s.Answer(data, "The change result is recorded as reported" + (reviewId == null ? "." : ", and a post-change review is open."), s.RecordUrl("change-request", change.Id));
    }
}
