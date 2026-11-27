using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Reasons;
using Product.Engine.Core.Snapshots;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Envelope;
using Product.Mcp.Core.Json;

namespace Product.Mcp.Core.Backend.InProcess;

// The reads of the audit and evidence screens and of the AI activity screen (D-36; document 24,
// B24-11): tool 65 get_audit_trail (the product's own audit entries, O24, for the auditor and the platform admin), tool
// 66 get_evidence (an evidence pack for one application or one decision, with the configuration version stamped on each
// row) and tool 67 get_ai_activity (what AI clients did across the estate, the keynote screen). Each is honest about what
// the caller cannot see: hidden rows are counted in couldNotRead, lists page with skipToken.
internal static partial class ToolHandlers
{
    // The roles that read audit entries (document 26, sections 3.2 and 5.3: auditor and platform admin).
    private static readonly string[] AuditReaderRoles = { "auditor", "platform-admin" };

    // The roles that see the AI work of the whole estate (document 24, B24-11; document 26, B26-11). Anyone else sees the
    // AI work done on their own behalf.
    private static readonly string[] AiOverseerRoles = { "ai-lead", "security-reviewer", "platform-admin", "auditor" };

    // The operational entities an AI client creates rows in, through the tools it is served.
    private static readonly string[] AiCreatedEntities =
    {
        "intake-request", "business-application", "decision", "change-request", "tier-assessment", "technical-component", "dependency",
        "document-link", "delivery-link", "evidence-link", "deployment-record", "review", "claim", "gap-check-run", "configuration-draft",
        "data-use", "data-mapping", "data-location",
    };

    private static readonly string[] AiActorKinds = { "ai-client-for-person", "agent-identity" };

    // The entities whose rows wait in a draft or proposed state for a person to take them forward.
    // A data use, mapping or location an AI client drafts waits for a person to confirm it (G-21); the confirm takes it
    // forward (product review: the story's draft data use and mapping were missing from AI activity).
    private static readonly string[] DraftableEntities = { "intake-request", "business-application", "decision", "change-request", "document-link", "review", "configuration-draft", "data-use", "data-mapping", "data-location" };

    // The tools whose call takes a record forward (a person's request act) or takes it back.
    private static readonly string[] SubmitTools = { "submit_record" };
    private static readonly string[] WithdrawTools = { "withdraw_record" };

    // ---- 65. get_audit_trail ----

    // The audit entries of one record, of one person, of one application's records, or of everything (document 22, O24;
    // document 26, section 9). A composed call names its tool, a refused call its tool and the rule that refused it, a
    // product event its entity. The entries hold IDs, never labels: the labels come from the store at read time.
    private static JsonTreeNode GetAuditTrail(ToolScope s)
    {
        if (!AuditReaderRoles.Any(s.HoldsRole))
        {
            throw s.Refused(McpErrorCodes.NotAuthorized, "Only the auditor and the platform admin read audit entries (document 26, section 5.3).");
        }

        string? recordId = s.Text("recordId");
        string? person = s.Text("principalObjectId");
        string? appId = s.Text("businessApplicationId");
        if (appId != null)
        {
            RequireApplication(s);
        }

        IReadOnlyList<string> operations = s.Strings("operations");
        IReadOnlyList<string> actorKinds = s.Strings("actorKinds");
        string? clientApp = s.Text("clientApp")?.Trim();
        DateTime? from = Js.ParseMoment(s.Text("from"));
        DateTime? to = Js.ParseMoment(s.Text("to"));

        // A record's trail holds its own entries and, for a decision, those of its approval records.
        HashSet<string>? rows = null;
        if (recordId != null)
        {
            rows = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { recordId };
            foreach (StoredRow record in s.Query("approval-record", ("decision", recordId)))
            {
                rows.Add(record.Id);
            }
        }

        HashSet<string>? appRows = appId == null ? null : ApplicationRowIds(s, appId);
        var matches = new List<StoredRow>();
        foreach (StoredRow entry in s.Rows.Query(RowQuery.For("audit-entry")))
        {
            string? row = entry.Text("rowId");
            if ((rows != null && (row == null || !rows.Contains(row)))
                || (appRows != null && (row == null || !appRows.Contains(row)))
                || (person != null && !Same(entry.Text("actor"), person) && !Same(entry.Text("onBehalfOf"), person))
                || (operations.Count > 0 && !operations.Contains(entry.Text("operation") ?? string.Empty, StringComparer.Ordinal))
                || (actorKinds.Count > 0 && !actorKinds.Contains(entry.Text("actorKind") ?? string.Empty, StringComparer.Ordinal))
                || (clientApp != null && !string.Equals(entry.Text("clientApp"), clientApp, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            DateTime? when = Js.ParseMoment(entry.Text("occurredOn"));
            if ((from.HasValue && (!when.HasValue || when.Value.Date < from.Value.Date)) || (to.HasValue && (!when.HasValue || when.Value.Date > to.Value.Date)))
            {
                continue;
            }

            matches.Add(entry);
        }

        var ordered = matches.OrderByDescending(e => e.Text("occurredOn") ?? string.Empty, StringComparer.Ordinal).ThenBy(e => e.Id, StringComparer.Ordinal).ToList();
        List<StoredRow> page = s.Page(ordered, out string? next);
        JsonTreeNode items = Arr(page.Select(e => AuditItem(s, e)));
        string scope = recordId != null ? "the record" : person != null ? "the person" : appId != null ? "the application's records" : "the whole product";
        return s.Answer(
            Js.Obj(("items", items), ("totalCount", JsonTreeNode.FromInt64(ordered.Count)), ("skipToken", Js.OptStr(next))),
            ordered.Count + " audit entr" + (ordered.Count == 1 ? "y" : "ies") + " for " + scope + ", newest first. Entries hold IDs; the names shown come from the store today.");
    }

    // One audit entry as people read it: what happened, to which record, by whom, through which client, for whom.
    private static JsonTreeNode AuditItem(ToolScope s, StoredRow entry)
    {
        string operation = entry.Text("operation") ?? "unknown";
        string entityName = entry.Text("entityName") ?? string.Empty;
        string? rowId = entry.Text("rowId");
        JsonTreeNode? changed = entry.Column("changedValuesJson");
        bool isCall = operation == "composed-call" || operation == "call-refused";
        string? tool = isCall ? entityName : null;
        string? entity = AuditEntityOf(s, entityName, rowId, changed);
        StoredRow? record = entity == null || rowId == null ? null : s.Rows.Get(entity, rowId);
        if (HiddenRow(s, record))
        {
            // A request, a claim, or a decision about a request the caller may not read keeps its ID but shows no label
            // (security review 2026-11-24, B24-14).
            s.HideFromCaller(record!.Entity, 1);
            record = null;
        }

        return Js.Obj(
            ("auditEntryId", JsonTreeNode.FromString(entry.Id)),
            ("occurredOn", ToolScope.MomentOrNull(entry.Text("occurredOn"))),
            ("operation", JsonTreeNode.FromString(operation)),
            ("what", JsonTreeNode.FromString(AuditWhat(operation, tool))),
            ("tool", Js.Str(tool)),
            ("record", rowId == null ? JsonTreeNode.Null : Js.Obj(
                ("entity", Js.Str(entity)),
                ("recordId", JsonTreeNode.FromString(rowId)),
                ("label", Js.Str(record == null ? null : RecordLabel(s, record))),
                ("recordUrl", entity == null ? JsonTreeNode.Null : JsonTreeNode.FromString(s.RecordUrl(entity, rowId))))),
            ("actor", s.PrincipalRef(entry.Text("actor"))),
            ("actorKind", JsonTreeNode.FromString(entry.Text("actorKind") ?? "engine")),
            ("onBehalfOf", s.PrincipalRef(entry.Text("onBehalfOf"))),
            ("clientApp", Js.Str(entry.Text("clientApp"))),
            ("correlationId", JsonTreeNode.FromString(entry.Text("correlationId") ?? string.Empty)),
            ("statusAfter", Js.Str(StatusAfter(changed, rowId))),
            ("refusal", operation == "call-refused" ? RefusalView(s, changed) : JsonTreeNode.Null),
            ("hasRequestKey", JsonTreeNode.FromBoolean(entry.Text("requestKey") != null)));
    }

    // Plain words for an audit operation.
    private static string AuditWhat(string operation, string? tool)
    {
        return operation switch
        {
            "composed-call" => "Called " + (tool ?? "a tool"),
            "call-refused" => "Refused " + (tool ?? "a call"),
            "decision-accepted" => "Decision accepted",
            "approval-outcome-written" => "Approval request answered",
            "subject-updated" => "Accepted decision applied to its subject",
            "request-recorded" => "Request recorded",
            "configuration-activated" => "Configuration version activated",
            "configuration-activation-refused" => "Configuration activation refused",
            "erasure-performed" => "Person erased",
            _ => ToolScope.Labelize(operation),
        };
    }

    // The entity key of an entry's record: the logical name of a product event ("Approval Record"), else the row the call
    // wrote under that ID, else the entity of the record URL a keyed call's answer named.
    private static string? AuditEntityOf(ToolScope s, string entityName, string? rowId, JsonTreeNode? changed)
    {
        if (entityName.Length > 0 && (entityName.IndexOf(' ') >= 0 || char.IsUpper(entityName[0])))
        {
            return string.Join("-", entityName.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        }

        if (rowId == null)
        {
            return null;
        }

        JsonTreeNode? written = Js.Items(changed, "writes").FirstOrDefault(w => Same(Js.Text(w, "rowId"), rowId));
        if (Js.Flag(written, "byHost") == true)
        {
            // A host table's row (B24-22): the approval outbox keys its row by the Approval Record whose card it is, so the
            // entry opens that request's record; no other host table is named.
            return Js.Text(written, "entity") == "approval-outbox" ? "approval-record" : null;
        }

        if (Js.Text(written, "entity") is string fromWrites)
        {
            return fromWrites;
        }

        string? url = Js.Text(changed, "recordUrl");
        if (url != null)
        {
            string[] parts = url.TrimEnd('/').Split('/');
            if (parts.Length >= 2 && Same(parts[parts.Length - 1], rowId))
            {
                return parts[parts.Length - 2];
            }
        }

        return null;
    }

    // The status meaning a call left on its record: from the written rows of an unkeyed call, or from a keyed call's answer.
    private static string? StatusAfter(JsonTreeNode? changed, string? rowId)
    {
        JsonTreeNode? written = Js.Items(changed, "writes").FirstOrDefault(w => rowId != null && Same(Js.Text(w, "rowId"), rowId) && Js.Text(w, "statusMeaning") != null && Js.Flag(w, "byHost") != true);
        return Js.Text(written, "statusMeaning") ?? Js.Text(Js.Member(Js.Member(changed, "data"), "status"), "meaning");
    }

    // A refusal as the audit entry holds it: the engine's own words when the entry kept them (a rule refusal), else the
    // rule's plain words from the reason catalog, and the guideline it applies with its title (product review: the step
    // 12 chip opens Guidelines at that number).
    private static JsonTreeNode RefusalView(ToolScope s, JsonTreeNode? changed)
    {
        string? reasonCode = Js.Text(changed, "reasonCode");
        string? rule = Js.Text(changed, "message") ?? (reasonCode != null && ReasonCatalog.TryGet(reasonCode, out ReasonCodeInfo? info) ? info.Message : null);
        string code = Js.Text(changed, "code") ?? McpErrorCodes.RuleViolation;
        bool knownTool = Js.Flag(changed, "knownTool") != false;
        string? guideline = Js.Text(changed, "guideline") ?? GuidelineRefs.ForRefusal(reasonCode, code, knownTool);
        SnapshotRow? guidelineRow = guideline == null ? null : s.ConfigRow("guideline", guideline.ToLowerInvariant());
        string? title = guidelineRow == null ? null : GuidelineTitleOf(guidelineRow);
        return Js.Obj(
            ("code", JsonTreeNode.FromString(code)),
            ("ruleKey", Js.Str(Js.Text(changed, "ruleKey"))),
            ("reasonCode", Js.Str(reasonCode)),
            ("ruleIds", Js.Strs(Js.Strings(changed, "ruleIds"))),
            ("rule", JsonTreeNode.FromString(rule ?? RefusalWords(code, knownTool))),
            ("knownTool", JsonTreeNode.FromBoolean(knownTool)),
            ("guideline", Js.Str(guideline)),
            ("guidelineTitle", Js.Str(title)));
    }

    private static string RefusalWords(string code, bool knownTool)
    {
        return !knownTool ? "No tool has this name: the product serves no tool that approves, accepts or activates."
            : code == McpErrorCodes.AiCannotDoThis ? "AI never approves: only a person, or the approval runner for a person, sets this state."
            : code == McpErrorCodes.NotAuthorized ? "The caller holds no role, owner role or row access that this call needs."
            : code == McpErrorCodes.Locked ? "The call touches a locked setting or an engine contract value."
            : "A configuration rule refused the call.";
    }

    // A record's name as a list shows it.
    private static string RecordLabel(ToolScope s, StoredRow row)
    {
        switch (row.Entity)
        {
            case "data-use":
            {
                StoredRow? useApp = row.Text("businessApplication") is string useAppId ? s.Rows.Get("business-application", useAppId) : null;
                string path = row.Text("dataDomain") is string useDomain ? new Product.Engine.Core.Operations.DataTree(s.Snapshot).PathText(useDomain) : "a data domain";
                return (row.Text("recordNumber") is string number ? number + ": " : string.Empty) + (useApp == null ? "A data use" : AppName(useApp)) + " uses " + path;
            }

            case "data-mapping":
            {
                string path = row.Text("dataDomain") is string mapDomain ? new Product.Engine.Core.Operations.DataTree(s.Snapshot).PathText(mapDomain) : "A data domain";
                StoredRow? location = row.Text("dataLocation") is string locationId ? s.Rows.Get("data-location", locationId) : null;
                return path + " in " + (location?.Text("labelAsSeen") ?? "a data location");
            }

            case "business-application":
                return AppName(row);
            case "decision":
                return (row.Text("recordNumber") is string dn ? dn + ": " : string.Empty) + (row.Text("statement") ?? s.Label("decision-type", row.Text("decisionType")));
            case "intake-request":
            case "change-request":
                return (row.Text("recordNumber") is string n ? n + ": " : string.Empty) + (row.Text("title") ?? row.Id);
            case "review":
                return s.Label("review-type", row.Text("reviewType")) + (row.Text("businessApplication") is string a && s.Rows.Get("business-application", a) is StoredRow app ? ": " + AppName(app) : string.Empty);
            case "deployment-record":
                return DeploymentLabel(s, row) ?? "Deployment record";
            case "approval-record":
                return "Approval request" + (row.Text("decision") is string d && s.Rows.Get("decision", d) is StoredRow decision ? " on " + RecordLabel(s, decision) : string.Empty);
            case "tier-assessment":
                return "Tier answers: " + s.Label("tier", row.Text("computedTier"));
            case "document-link":
                return s.Label("document-type", row.Text("documentType"));
            case "dependency":
                return s.Label("dependency-type", row.Text("dependencyType"));
            case "portfolio":
            case "configuration-draft":
                return row.Text("name") ?? row.Id;
            default:
                return row.Text("labelAsSeen") ?? row.Text("label") ?? row.Text("name") ?? row.Text("title") ?? ToolScope.EntityName(row.Entity);
        }
    }

    // The IDs of an application and of the rows that belong to it, for an audit or activity filter.
    private static HashSet<string> ApplicationRowIds(ToolScope s, string appId)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { appId };
        foreach (string entity in new[] { "intake-request", "change-request", "deployment-record", "review", "document-link", "technical-component", "owner-assignment", "tier-assessment", "data-use" })
        {
            foreach (StoredRow row in s.Query(entity, ("businessApplication", appId)))
            {
                ids.Add(row.Id);
            }
        }

        foreach (StoredRow decision in s.Rows.Query(RowQuery.For("decision")).Where(d => Same(ApplicationOf(s, d), appId)))
        {
            ids.Add(decision.Id);
            foreach (StoredRow record in s.Query("approval-record", ("decision", decision.Id)))
            {
                ids.Add(record.Id);
            }
        }

        foreach (StoredRow dependency in s.Query("dependency", ("fromApplication", appId)))
        {
            ids.Add(dependency.Id);
        }

        return ids;
    }

    // ---- 66. get_evidence ----

    // An evidence pack for one application or one decision (document 24, B24-11; guideline G15): the decisions with every
    // approval request and answer (who, as what kind of actor, counted or not), the changes and deployments with their
    // flags, the reviews, the documents and the evidence links, each with the configuration version stamped on it, and
    // the version in force now. Any role reads it; a decision about an intake request the caller may not read is left out
    // and counted in couldNotRead.
    private static JsonTreeNode GetEvidence(ToolScope s)
    {
        string? appId = s.Text("businessApplicationId");
        string? decisionId = s.Text("decisionId");
        if ((appId == null) == (decisionId == null))
        {
            throw s.Invalid(appId == null ? "businessApplicationId" : "decisionId", "Name exactly one of businessApplicationId and decisionId.");
        }

        DateTime? from = Js.ParseMoment(s.Text("from"));
        DateTime? to = Js.ParseMoment(s.Text("to"));
        bool InWindow(string? when)
        {
            DateTime? at = Js.ParseMoment(when);
            return !at.HasValue || ((!from.HasValue || at.Value.Date >= from.Value.Date) && (!to.HasValue || at.Value.Date <= to.Value.Date));
        }

        List<StoredRow> decisions;
        List<StoredRow> changes;
        List<StoredRow> deployments;
        List<StoredRow> reviews;
        List<StoredRow> documents;
        JsonTreeNode subject;
        int hidden = 0;
        if (appId != null)
        {
            StoredRow app = RequireApplication(s);
            decisions = new List<StoredRow>();
            foreach (StoredRow d in s.Rows.Query(RowQuery.For("decision")).Where(d => Same(ApplicationOf(s, d), app.Id)))
            {
                if (d.Text("subjectKind") == "intake-request" && SubjectIdOf(d) is string intakeId && s.Rows.Get("intake-request", intakeId) is StoredRow intake && !CanReadIntake(s, intake))
                {
                    hidden++;
                    continue;
                }

                decisions.Add(d);
            }

            changes = s.Query("change-request", ("businessApplication", app.Id)).ToList();
            deployments = s.Query("deployment-record", ("businessApplication", app.Id)).ToList();
            reviews = s.Query("review", ("businessApplication", app.Id)).ToList();
            documents = s.Query("document-link", ("businessApplication", app.Id)).ToList();
            subject = Js.Obj(
                ("subjectKind", JsonTreeNode.FromString("business-application")),
                ("subjectId", JsonTreeNode.FromString(app.Id)),
                ("label", JsonTreeNode.FromString(AppName(app))),
                ("businessApplicationId", JsonTreeNode.FromString(app.Id)),
                ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("business-application", app.Id))));
        }
        else
        {
            StoredRow d = s.RequireRow("decision", decisionId, "decisionId");
            if (d.Text("subjectKind") == "intake-request" && SubjectIdOf(d) is string intakeId && s.Rows.Get("intake-request", intakeId) is StoredRow intake && !CanReadIntake(s, intake))
            {
                throw s.NotFound("Decision", d.Id, "decisionId");
            }

            decisions = new List<StoredRow> { d };
            changes = s.Query("change-request", ("authorizationDecision", d.Id)).ToList();
            if (d.Text("subjectKind") == "change-request" && SubjectIdOf(d) is string changeId && s.Rows.Get("change-request", changeId) is StoredRow change && !changes.Any(c => c.Id == change.Id))
            {
                changes.Add(change);
            }

            deployments = changes.SelectMany(c => s.Query("deployment-record", ("changeRequest", c.Id))).ToList();
            if (d.Text("subjectKind") == "deployment-record" && SubjectIdOf(d) is string recordId && s.Rows.Get("deployment-record", recordId) is StoredRow covered && !deployments.Any(r => r.Id == covered.Id))
            {
                deployments.Add(covered);
            }

            reviews = s.Query("review", ("outcomeDecision", d.Id)).ToList();
            documents = new List<StoredRow>();
            (string subjectLabel, string? subjectApp, _) = DecisionSubjectLabel(s, d);
            subject = Js.Obj(
                ("subjectKind", JsonTreeNode.FromString("decision")),
                ("subjectId", JsonTreeNode.FromString(d.Id)),
                ("label", JsonTreeNode.FromString((d.Text("recordNumber") is string n ? n + ": " : string.Empty) + (d.Text("statement") ?? subjectLabel))),
                ("businessApplicationId", Js.Str(subjectApp)),
                ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("decision", d.Id))));
        }

        s.HideFromCaller("decision", hidden);
        decisions = NewestFirst(decisions.Where(d => InWindow(d.Text("decidedOn") ?? d.Text("engineCheckedOn")))).ToList();
        changes = changes.Where(c => InWindow(c.Text("windowStart") ?? c.Text("engineCheckedOn"))).OrderByDescending(c => c.Text("windowStart") ?? string.Empty, StringComparer.Ordinal).ThenBy(c => c.Id, StringComparer.Ordinal).ToList();
        deployments = deployments.Where(r => InWindow(r.Text("finishedOn"))).OrderByDescending(r => r.Text("finishedOn") ?? string.Empty, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
        reviews = reviews.Where(r => InWindow(r.Text("doneOn") ?? r.Text("dueOn"))).OrderByDescending(r => r.Text("doneOn") ?? r.Text("dueOn") ?? string.Empty, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
        documents = documents.OrderBy(l => l.Text("documentType") ?? string.Empty, StringComparer.Ordinal).ThenBy(l => l.Id, StringComparer.Ordinal).ToList();

        const int Cap = 500;
        bool truncated = decisions.Count > Cap || changes.Count > Cap || deployments.Count > Cap || reviews.Count > Cap || documents.Count > Cap;
        var versions = new SortedSet<long>();
        void Note(StoredRow row)
        {
            if (row.Number("configurationSetVersion") is long v)
            {
                versions.Add(v);
            }
        }

        var evidence = new List<JsonTreeNode>();
        void EvidenceOf(string column, string id, string subjectKind)
        {
            foreach (StoredRow link in s.Query("evidence-link", (column, id)).OrderBy(l => l.Text("capturedOn") ?? string.Empty, StringComparer.Ordinal).ThenBy(l => l.Id, StringComparer.Ordinal))
            {
                Note(link);
                evidence.Add(Js.Obj(
                    ("evidenceLinkId", JsonTreeNode.FromString(link.Id)),
                    ("subjectKind", JsonTreeNode.FromString(subjectKind)),
                    ("subjectId", JsonTreeNode.FromString(id)),
                    ("linkKind", s.LabelledKey("link-kind", link.Text("linkKind") ?? "unknown")),
                    ("url", (link.Text("url") ?? string.Empty).StartsWith("https://", StringComparison.Ordinal) ? JsonTreeNode.FromString(link.Text("url")!) : JsonTreeNode.Null),
                    ("label", JsonTreeNode.FromString(link.Text("label") ?? string.Empty)),
                    ("evidenceLevel", ListValue(s, "evidence-level", link.Text("evidenceLevel"))),
                    ("capturedOn", ToolScope.MomentOrNull(link.Text("capturedOn"))),
                    ("isAiCreated", AiCreated(link)),
                    ("configurationSetVersion", Version(link))));
            }
        }

        JsonTreeNode decisionItems = Arr(decisions.Take(Cap).Select(d =>
        {
            Note(d);
            EvidenceOf("subjectDecision", d.Id, "decision");
            var approvals = s.Query("approval-record", ("decision", d.Id)).OrderBy(r => r.Text("requestedOn") ?? string.Empty, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
            foreach (StoredRow record in approvals)
            {
                Note(record);
                EvidenceOf("subjectApprovalRecord", record.Id, "approval-record");
            }

            return Js.Obj(
                ("decisionId", JsonTreeNode.FromString(d.Id)),
                ("recordNumber", Js.Str(d.Text("recordNumber"))),
                ("decisionType", s.LabelledKey("decision-type", d.Text("decisionType") ?? "unknown")),
                ("statement", JsonTreeNode.FromString(d.Text("statement") ?? string.Empty)),
                ("reason", Js.Str(d.Text("reason"))),
                ("outcome", DecisionOutcome(s, d)),
                ("status", s.StatusValue(d) is JsonTreeNode st && st.Kind != JsonTreeKind.Null ? st : ToolScope.StatusValue("proposed", null)),
                ("proposedBy", s.PrincipalRef(ProposerOf(s, d))),
                ("decider", s.PrincipalRef(d.Text("decider"))),
                ("decidedOn", ToolScope.MomentOrNull(d.Text("decidedOn"))),
                ("settledBy", Js.Str(d.Text("statusMeaning") == "proposed" ? null : SettledBy(d, approvals.Count))),
                ("approvals", Arr(approvals.Select(r => Js.Obj(
                    ("approvalRecordId", JsonTreeNode.FromString(r.Id)),
                    ("askedRole", s.LabelledKey("role", r.Text("requiredRole"))),
                    ("askedOwnerRole", s.LabelledKey("owner-role", r.Text("requiredOwnerRole"))),
                    ("isConsultation", JsonTreeNode.FromBoolean(r.Flag("isConsultation"))),
                    ("approver", s.PrincipalRef(r.Text("approver"))),
                    ("approverActorKind", Js.Str(r.Text("approverActorMeaning") ?? r.Text("approverActorKind"))),
                    ("outcome", s.StatusValue(r, "outcome", "outcomeMeaning") is JsonTreeNode o && o.Kind != JsonTreeKind.Null ? o : ToolScope.StatusValue("pending", null)),
                    ("typedReason", Js.Str(r.Text("typedReason"))),
                    ("requestedOn", ToolScope.MomentOrNull(r.Text("requestedOn"))),
                    ("respondedOn", ToolScope.MomentOrNull(r.Text("respondedOn"))),
                    ("countsTowardRequirement", JsonTreeNode.FromBoolean(r.Flag("countsTowardRequirement"))),
                    ("configurationSetVersion", Version(r)))))),
                ("isAiCreated", AiCreated(d)),
                ("performedBy", Stamp(s, d)),
                ("configurationSetVersion", Version(d)),
                ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("decision", d.Id))));
        }));
        JsonTreeNode changeItems = Arr(changes.Take(Cap).Select(c =>
        {
            Note(c);
            EvidenceOf("subjectChangeRequest", c.Id, "change-request");
            return Js.Obj(
                ("changeRequestId", JsonTreeNode.FromString(c.Id)),
                ("recordNumber", Js.Str(c.Text("recordNumber"))),
                ("title", JsonTreeNode.FromString(c.Text("title") ?? string.Empty)),
                ("changeType", s.LabelledKey("change-type", c.Text("changeType") ?? "unknown")),
                ("status", s.StatusValue(c)),
                ("result", ListValue(s, "change-result", c.Text("result"))),
                ("windowStart", ToolScope.MomentOrNull(c.Text("windowStart"))),
                ("windowEnd", ToolScope.MomentOrNull(c.Text("windowEnd"))),
                ("authorizationDecisionId", Js.Str(c.Text("authorizationDecision"))),
                ("isAiCreated", AiCreated(c)),
                ("performedBy", Stamp(s, c)),
                ("configurationSetVersion", Version(c)),
                ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("change-request", c.Id))));
        }));
        JsonTreeNode deploymentItems = Arr(deployments.Take(Cap).Select(r =>
        {
            Note(r);
            EvidenceOf("subjectDeploymentRecord", r.Id, "deployment-record");
            return DeploymentItem(s, r);
        }));
        JsonTreeNode reviewItems = Arr(reviews.Take(Cap).Select(r =>
        {
            Note(r);
            EvidenceOf("subjectReview", r.Id, "review");
            return Js.Obj(
                ("reviewId", JsonTreeNode.FromString(r.Id)),
                ("recordNumber", Js.Str(r.Text("recordNumber"))),
                ("reviewType", s.LabelledKey("review-type", r.Text("reviewType") ?? "unknown")),
                ("status", s.StatusValue(r) is JsonTreeNode st && st.Kind != JsonTreeKind.Null ? st : ToolScope.StatusValue("due", null)),
                ("dueOn", ToolScope.DateOrNull(r.Text("dueOn"))),
                ("doneOn", ToolScope.DateOrNull(r.Text("doneOn"))),
                ("reviewer", s.PrincipalRef(r.Text("reviewer"))),
                ("outcomeDecisionId", Js.Str(r.Text("outcomeDecision"))),
                ("configurationSetVersion", Version(r)),
                ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("review", r.Id))));
        }));
        JsonTreeNode documentItems = Arr(documents.Take(Cap).Select(l =>
        {
            Note(l);
            return Js.Obj(
                ("documentLinkId", JsonTreeNode.FromString(l.Id)),
                ("documentType", s.LabelledKey("document-type", l.Text("documentType") ?? "unknown")),
                ("status", s.StatusValue(l)),
                ("url", (l.Text("url") ?? string.Empty).StartsWith("https://", StringComparison.Ordinal) ? JsonTreeNode.FromString(l.Text("url")!) : JsonTreeNode.Null),
                ("owner", s.PrincipalRef(l.Text("documentOwner"))),
                ("lastCheckedOn", ToolScope.DateOrNull(l.Text("lastCheckedOn"))),
                ("isAiCreated", AiCreated(l)),
                ("configurationSetVersion", Version(l)),
                ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("document-link", l.Id))));
        }));

        int aiMarked = decisions.Count(AiCreatedRow) + changes.Count(AiCreatedRow) + deployments.Count(AiCreatedRow) + documents.Count(AiCreatedRow);
        JsonTreeNode data = Js.Obj(
            ("subject", subject),
            ("configurationInForce", Js.Obj(
                ("configurationSetVersion", JsonTreeNode.FromInt64(s.Session.ConfigurationSetVersion)),
                ("contentHash", JsonTreeNode.FromString(s.Session.ContentHash)))),
            ("versionsStamped", Arr(versions.Select(JsonTreeNode.FromInt64))),
            ("decisions", decisionItems),
            ("changes", changeItems),
            ("deployments", deploymentItems),
            ("reviews", reviewItems),
            ("documents", documentItems),
            ("evidenceLinks", Arr(evidence)),
            ("counts", Js.Obj(
                ("decisions", JsonTreeNode.FromInt64(decisions.Count)),
                ("approvalAnswers", JsonTreeNode.FromInt64(decisions.Sum(d => s.Query("approval-record", ("decision", d.Id)).Count(r => r.Text("outcomeMeaning") != "pending")))),
                ("changes", JsonTreeNode.FromInt64(changes.Count)),
                ("deployments", JsonTreeNode.FromInt64(deployments.Count)),
                ("flaggedDeployments", JsonTreeNode.FromInt64(deployments.Count(IsFlagged))),
                ("reviews", JsonTreeNode.FromInt64(reviews.Count)),
                ("documents", JsonTreeNode.FromInt64(documents.Count)),
                ("evidenceLinks", JsonTreeNode.FromInt64(evidence.Count)),
                ("aiCreated", JsonTreeNode.FromInt64(aiMarked)))),
            ("truncated", JsonTreeNode.FromBoolean(truncated)));
        string label = Js.Text(subject, "label") ?? string.Empty;
        return s.Answer(
            data,
            "The evidence pack of " + label + ": " + Count(decisions.Count, "decision", "decisions") + ", " + Count(changes.Count, "change", "changes") + ", " + Count(deployments.Count, "deployment", "deployments") + ", " + Count(reviews.Count, "review", "reviews") + ", "
            + Count(documents.Count, "document", "documents") + ", " + Count(evidence.Count, "evidence link", "evidence links") + ". Each row names the configuration version it was checked under; version " + s.Session.ConfigurationSetVersion + " is in force now.",
            Js.Text(subject, "recordUrl"));
    }

    private static JsonTreeNode Version(StoredRow row)
    {
        return row.Number("configurationSetVersion") is long v ? JsonTreeNode.FromInt64(v) : JsonTreeNode.Null;
    }

    private static bool AiCreatedRow(StoredRow row)
    {
        return row.Flag("isAiCreated");
    }

    private static bool IsFlagged(StoredRow record)
    {
        return record.Flag("isOutsideWindow") || record.Flag("isOtherPath") || record.Flag("isMissingApproval") || record.Flag("isAiOnlyCodeApproval");
    }

    // ---- 67. get_ai_activity ----

    // What AI clients did across the estate (document 24, B24-11; the keynote screen; guidelines G16, G17): the rows an AI
    // client created, whether they wait for a person, who took each forward and when, whether a person edited it first,
    // and what was withdrawn; the calls the product refused an AI client, with the rule; the approval answers an AI actor
    // gave, which count as a recommendation only; and the deployments flagged for an AI-only code approval. Counts by
    // client, by the person the AI acted for and by application. The AI lead, the security reviewer, the platform admin
    // and the auditor see the whole estate; anyone else sees the AI work done on their own behalf.
    //
    // The row stamps (Is AI Created, Created By Principal, the last write's actor and client) say who created a row and who
    // touched it last. The call history in the audit entries (one per composed call, document 25, B25-13) says who took a
    // draft forward and whether a person edited it first; a row with no recorded calls (seeded, or written before the call
    // history) answers null for those, and the last stamp stands in for who took it forward.
    private static JsonTreeNode GetAiActivity(ToolScope s)
    {
        bool overseer = AiOverseerRoles.Any(s.HoldsRole);
        string view = s.Text("view") ?? "all";
        string? forPerson = s.Text("onBehalfOfObjectId");
        if (!overseer && forPerson != null && !Same(forPerson, s.PersonId))
        {
            throw s.Refused(McpErrorCodes.NotAuthorized, "Only the AI lead, the security reviewer, the platform admin and the auditor read the AI work done for other people. You see the AI work done for you.");
        }

        bool mineOnly = !overseer || (s.Flag("mineOnly") ?? false);
        string? person = mineOnly ? s.PersonId : forPerson;
        string? client = s.Text("clientApp")?.ToLowerInvariant();
        string? appFilter = s.Text("businessApplicationId");
        if (appFilter != null)
        {
            RequireApplication(s);
        }

        // portfolioId (wave 4b review): the activities about an application of the portfolio or one below it, or about an
        // intake request in it that has no application yet, in the scope the counts and the items share.
        string? portfolioFilter = s.Text("portfolioId");
        HashSet<string>? portfolioApps = PortfolioApplications(s, portfolioFilter, "portfolioId");
        bool InPortfolio(Activity a) => portfolioApps == null || InPortfolioScope(s, portfolioFilter!, portfolioApps, a.AppId, a.IntakeId);

        DateTime? from = Js.ParseMoment(s.Text("from"));
        DateTime? to = Js.ParseMoment(s.Text("to"));

        // Under an application or a portfolio, a client's calls count only when the record a call named belongs there, as
        // the drafts and the refusals do (wave 4b review). A refused call that named no record has no application,
        // so it drops out under such a filter.
        bool CallInScope(StoredRow e)
        {
            if (appFilter == null && portfolioApps == null)
            {
                return true;
            }

            string? rowId = e.Text("rowId");
            string? entity = rowId == null ? null : AuditEntityOf(s, e.Text("entityName") ?? string.Empty, rowId, e.Column("changedValuesJson"));
            StoredRow? row = entity == null ? null : s.Rows.Get(entity, rowId!);
            if (row == null)
            {
                return false;
            }

            string? app = row.Entity == "business-application" ? row.Id : ApplicationOf(s, row) ?? (row.Entity == "dependency" ? row.Text("fromApplication") : null);
            string? intake = row.Entity == "intake-request" ? row.Id : row.Text("subjectIntakeRequest") ?? row.Text("intakeRequest");
            return (appFilter == null || Same(app, appFilter)) && (portfolioApps == null || InPortfolioScope(s, portfolioFilter!, portfolioApps, app, intake));
        }

        // Every AI activity the caller may read, at estate scope (wave 4b review: get_admin_status counts from the same list).
        AiActivityLog log = AiActivities(s, null);
        List<Activity> all = log.All;
        Dictionary<string, List<StoredRow>> calls = log.Calls;
        List<StoredRow> refusedCalls = log.RefusedCalls;

        // The scope every count shares: the person, the client, the application and the dates. The view picks the items.
        var scoped = all.Where(a =>
            (person == null || Same(a.ForPerson, person) || (a.Kind == "code-approval" && mineOnly && a.AppId != null && s.OwnsApplication(a.AppId)))
            && (client == null || (a.Client ?? string.Empty).ToLowerInvariant().Contains(client))
            && (appFilter == null || Same(a.AppId, appFilter))
            && InPortfolio(a)
            && (!from.HasValue || (a.On.HasValue && a.On.Value.Date >= from.Value.Date))
            && (!to.HasValue || (a.On.HasValue && a.On.Value.Date <= to.Value.Date))).ToList();
        IEnumerable<Activity> inView = view switch
        {
            "waiting" => scoped.Where(a => a.Kind == "draft" && a.State == "waiting"),
            "accepted" => scoped.Where(a => a.Kind == "draft" && a.ForwardByPerson),
            "withdrawn" => scoped.Where(a => a.Kind == "draft" && a.State == "withdrawn"),
            "refused" => scoped.Where(a => a.Kind == "refusal"),
            "answers" => scoped.Where(a => a.Kind == "answer"),
            "code-approvals" => scoped.Where(a => a.Kind == "code-approval"),
            "drafts" => scoped.Where(a => a.AiCreated),
            _ => scoped,
        };
        var ordered = inView.OrderBy(a => a.On.HasValue ? 0 : 1).ThenByDescending(a => a.On ?? DateTime.MinValue).ThenBy(a => a.Id, StringComparer.Ordinal).ToList();
        List<Activity> page = s.Page(ordered, out string? next);

        // The PMO reads the estate's counts, never its rows (D-41, product review: the Overview tile "AI drafts waiting"
        // reads as estate-wide, and the PMO answers for the whole portfolio). The rows stay the AI work done for them.
        bool estateCounts = !overseer && s.HoldsRole("pmo") && s.Flag("mineOnly") != true && forPerson == null;
        List<Activity> countBase = !estateCounts ? scoped : all.Where(a =>
            (client == null || (a.Client ?? string.Empty).ToLowerInvariant().Contains(client))
            && (appFilter == null || Same(a.AppId, appFilter))
            && InPortfolio(a)
            && (!from.HasValue || (a.On.HasValue && a.On.Value.Date >= from.Value.Date))
            && (!to.HasValue || (a.On.HasValue && a.On.Value.Date <= to.Value.Date))).ToList();
        var drafts = countBase.Where(a => a.AiCreated).ToList();
        // everything counts each activity once (wave 4b review): a deployment an AI client drafted and started is one
        // code-approval activity that is also a draft, so drafts + refused + answers + codeApprovals can be one more.
        JsonTreeNode counts = Js.Obj(
            ("everything", JsonTreeNode.FromInt64(countBase.Count)),
            ("drafts", JsonTreeNode.FromInt64(drafts.Count)),
            ("waiting", JsonTreeNode.FromInt64(drafts.Count(a => a.State == "waiting"))),
            ("takenForwardByPerson", JsonTreeNode.FromInt64(drafts.Count(a => a.ForwardByPerson))),
            ("submittedByAi", JsonTreeNode.FromInt64(drafts.Count(a => a.ForwardByAi))),
            ("editedByPersonFirst", JsonTreeNode.FromInt64(drafts.Count(a => a.EditedByPerson == true))),
            ("withdrawn", JsonTreeNode.FromInt64(drafts.Count(a => a.State == "withdrawn"))),
            ("recorded", JsonTreeNode.FromInt64(drafts.Count(a => a.State == "recorded"))),
            ("refused", JsonTreeNode.FromInt64(countBase.Count(a => a.Kind == "refusal"))),
            ("answers", JsonTreeNode.FromInt64(countBase.Count(a => a.Kind == "answer"))),
            ("codeApprovals", JsonTreeNode.FromInt64(countBase.Count(a => a.Kind == "code-approval"))));
        JsonTreeNode byClient = Arr(scoped.GroupBy(a => a.Client ?? string.Empty, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase).Take(20)
            .Select(g => Js.Obj(("clientApp", g.Key.Length == 0 ? JsonTreeNode.Null : JsonTreeNode.FromString(g.First().Client!)), ("count", JsonTreeNode.FromInt64(g.Count())))));
        JsonTreeNode byPerson = Arr(scoped.Where(a => a.ForPerson != null).GroupBy(a => a.ForPerson!, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Take(20)
            .Select(g => Js.Obj(("onBehalfOf", s.PrincipalRef(g.First().ForPerson)), ("count", JsonTreeNode.FromInt64(g.Count())))));
        JsonTreeNode byApplication = Arr(scoped.Where(a => a.AppId != null).GroupBy(a => a.AppId!, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Take(20)
            .Select(g => Js.Obj(
                ("businessApplicationId", JsonTreeNode.FromString(g.Key)),
                ("name", JsonTreeNode.FromString(s.Rows.Get("business-application", g.Key) is StoredRow app ? AppName(app) : g.Key)),
                ("count", JsonTreeNode.FromInt64(g.Count())))));
        JsonTreeNode knownClients = Arr(s.ActiveRows("known-client").OrderBy(r => r.KeyPath, StringComparer.Ordinal).Select(r => Js.Obj(
            ("key", JsonTreeNode.FromString(r.KeyPath)),
            ("label", JsonTreeNode.FromString(Js.Text(r.Columns, "label") ?? r.KeyPath)),
            ("isAiClient", JsonTreeNode.FromBoolean(Js.Flag(r.Columns, "isAiClient") == true)),
            ("isAllowed", JsonTreeNode.FromBoolean(Js.Flag(r.Columns, "isAllowed") != false)))));
        // The clients view (document 24, B24-18): per AI client, the calls it made that wrote or were refused (reads leave no
        // record), the rows it drafted, how many of them a person took forward, and its refused calls, in the same scope as
        // the counts. A client the Known Client list holds carries its key and whether it is allowed.
        var aiCalls = calls.Values.SelectMany(l => l).Concat(refusedCalls)
            .Where(e => AiActorKinds.Contains(e.Text("actorKind") ?? string.Empty, StringComparer.Ordinal) && e.Text("clientApp") != null)
            .Where(e => person == null || Same(e.Text("onBehalfOf") ?? e.Text("actor"), person))
            .Where(e => client == null || e.Text("clientApp")!.ToLowerInvariant().Contains(client))
            .Where(e => (!from.HasValue || (Js.ParseMoment(e.Text("occurredOn")) ?? DateTime.MinValue).Date >= from.Value.Date) && (!to.HasValue || (Js.ParseMoment(e.Text("occurredOn")) ?? DateTime.MaxValue).Date <= to.Value.Date))
            .Where(CallInScope)
            .Distinct()
            .ToList();
        var labels = aiCalls.Select(e => e.Text("clientApp")!).Concat(scoped.Where(a => a.Client != null).Select(a => a.Client!)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        JsonTreeNode clientsView = Arr(labels.Select(label =>
        {
            var mine = scoped.Where(a => string.Equals(a.Client, label, StringComparison.OrdinalIgnoreCase)).ToList();
            var made = aiCalls.Where(e => string.Equals(e.Text("clientApp"), label, StringComparison.OrdinalIgnoreCase)).ToList();
            DateTime? last = made.Select(e => Js.ParseMoment(e.Text("occurredOn"))).Concat(mine.Select(a => a.On)).Where(d => d.HasValue).Select(d => d.GetValueOrDefault()).OrderByDescending(d => d).Cast<DateTime?>().FirstOrDefault();
            SnapshotRow? knownRow = s.ActiveRows("known-client").FirstOrDefault(r => string.Equals(Js.Text(r.Columns, "label"), label, StringComparison.OrdinalIgnoreCase));
            return (Calls: made.Count, Json: Js.Obj(
                ("clientApp", JsonTreeNode.FromString(label)),
                ("knownClientKey", Js.Str(knownRow?.KeyPath)),
                ("isAllowed", knownRow == null ? JsonTreeNode.Null : JsonTreeNode.FromBoolean(Js.Flag(knownRow.Columns, "isAllowed") != false)),
                ("calls", JsonTreeNode.FromInt64(made.Count)),
                ("drafts", JsonTreeNode.FromInt64(mine.Count(a => a.AiCreated))),
                ("acceptedByPerson", JsonTreeNode.FromInt64(mine.Count(a => a.AiCreated && a.ForwardByPerson))),
                ("waiting", JsonTreeNode.FromInt64(mine.Count(a => a.AiCreated && a.State == "waiting"))),
                ("refused", JsonTreeNode.FromInt64(mine.Count(a => a.Kind == "refusal"))),
                ("lastActivityOn", last.HasValue ? Js.Utc(last.Value) : JsonTreeNode.Null)));
        }).OrderByDescending(c => c.Calls).Select(c => c.Json));
        // countsScope says whose work the counts cover, so a page that shows the PMO the estate's figures beside the rows
        // done for them says so (wave 3 verification: "Everything 29" stood above "No AI client has done anything here").
        JsonTreeNode data = Js.Obj(
            ("scope", JsonTreeNode.FromString(mineOnly ? "mine" : "estate")),
            ("countsScope", JsonTreeNode.FromString(estateCounts || !mineOnly ? "estate" : "mine")),
            ("counts", counts),
            ("byClient", byClient),
            ("byPerson", byPerson),
            ("byApplication", byApplication),
            ("knownClients", knownClients),
            ("clients", clientsView),
            ("items", Arr(page.Select(a => a.Json))),
            ("totalCount", JsonTreeNode.FromInt64(ordered.Count)),
            ("skipToken", Js.OptStr(next)));
        // The summary counts one scope at a time (wave 3 verification: the PMO's summary called the estate's draft counts
        // "for you" and mixed in the PMO's own empty refusals). "For you" counts the reader's own scope; the estate figures
        // the PMO's counts carry follow, named as the estate's.
        string who = mineOnly ? "AI work done for you: " : person != null ? "AI work done for " + s.DisplayLabel(person) + ": " : "AI work across the estate: ";
        string summary = who + AiFigures(scoped) + "."
            + (estateCounts ? " Across the estate, which the counts cover for the PMO: " + AiFigures(countBase) + "." : string.Empty)
            + " AI never approves.";
        return s.Answer(data, summary);
    }

    // The AI activity list (document 24, tool 67), at estate scope: every row an AI client created, every call the product
    // refused an AI client, every approval answer an AI actor gave, and every deployment flagged for an AI-only code
    // approval, one activity each, the rows the caller may not read left out and counted in couldNotRead. get_ai_activity
    // scopes and pages it; get_admin_status counts each AI client's drafts and refusals from it (wave 4b review; D-41), so the
    // Admin page and the AI activity page count the same rows. auditEntries: every audit entry, when the caller read them.
    private static AiActivityLog AiActivities(ToolScope s, IReadOnlyList<StoredRow>? auditEntries)
    {
        // The call history: every composed call and refused call, by the record it named.
        var calls = new Dictionary<string, List<StoredRow>>(StringComparer.OrdinalIgnoreCase);
        var refusedCalls = new List<StoredRow>();
        foreach (StoredRow entry in auditEntries ?? s.Rows.Query(RowQuery.For("audit-entry")))
        {
            string operation = entry.Text("operation") ?? string.Empty;
            if (operation == "call-refused")
            {
                refusedCalls.Add(entry);
            }
            else if (operation == "composed-call" && entry.Text("rowId") is string row)
            {
                if (!calls.TryGetValue(row, out List<StoredRow>? list))
                {
                    calls[row] = list = new List<StoredRow>();
                }

                list.Add(entry);
            }
        }

        foreach (List<StoredRow> list in calls.Values)
        {
            list.Sort((a, b) => string.CompareOrdinal(a.Text("occurredOn") ?? string.Empty, b.Text("occurredOn") ?? string.Empty));
        }

        var all = new List<Activity>();
        int hiddenIntakes = 0;
        int hiddenClaims = 0;
        foreach (string entity in AiCreatedEntities)
        {
            foreach (StoredRow row in s.Rows.Query(RowQuery.For(entity)).Where(AiCreatedRow))
            {
                if (entity == "intake-request" && !CanReadIntake(s, row))
                {
                    hiddenIntakes++;
                    continue;
                }

                if (entity == "claim" && !CanReadClaim(s, row))
                {
                    hiddenClaims++;
                    continue;
                }

                if (entity == "decision" && HidesIntake(s, row))
                {
                    s.HideFromCaller("decision", 1);
                    continue;
                }

                all.Add(DraftActivity(s, row, calls.TryGetValue(row.Id, out List<StoredRow>? history) ? history : null));
            }
        }

        foreach (StoredRow entry in refusedCalls.Where(e => AiActorKinds.Contains(e.Text("actorKind") ?? string.Empty, StringComparer.Ordinal)))
        {
            all.Add(RefusalActivity(s, entry));
        }

        foreach (StoredRow record in s.Rows.Query(RowQuery.For("approval-record")).Where(r => AiActorKinds.Contains(r.Text("approverActorMeaning") ?? r.Text("approverActorKind") ?? string.Empty, StringComparer.Ordinal)))
        {
            all.Add(AnswerActivity(s, record));
        }

        // One row per deployment (document 24, B24-17): a deployment an AI client recorded itself and that relies on an
        // AI-only code approval is one code-approval row that keeps the drafted facts (state recorded), never two rows.
        foreach (StoredRow record in s.Rows.Query(RowQuery.For("deployment-record")).Where(r => r.Flag("isAiOnlyCodeApproval")))
        {
            Activity? drafted = all.FirstOrDefault(a => a.Kind == "draft" && a.Id == record.Id);
            if (drafted != null)
            {
                all.Remove(drafted);
            }

            all.Add(CodeApprovalActivity(s, record, drafted));
        }

        s.HideFromCaller("intake-request", hiddenIntakes);
        s.HideFromCaller("claim", hiddenClaims);

        return new AiActivityLog(all, calls, refusedCalls);
    }

    private sealed class AiActivityLog
    {
        public AiActivityLog(List<Activity> all, Dictionary<string, List<StoredRow>> calls, List<StoredRow> refusedCalls)
        {
            All = all;
            Calls = calls;
            RefusedCalls = refusedCalls;
        }

        public List<Activity> All { get; }

        // The composed calls by the record they named, oldest first.
        public Dictionary<string, List<StoredRow>> Calls { get; }

        public List<StoredRow> RefusedCalls { get; }
    }

    // The figures of one scope, in words: the rows AI clients created and where they stand, the refused calls, the AI
    // answers and the AI-only code approvals.
    private static string AiFigures(IReadOnlyList<Activity> items)
    {
        var drafts = items.Where(a => a.AiCreated).ToList();
        return Count(drafts.Count, "row an AI client created", "rows AI clients created") + ", " + drafts.Count(a => a.State == "waiting") + " waiting for a person, "
            + drafts.Count(a => a.ForwardByPerson) + " taken forward by a person; " + Count(items.Count(a => a.Kind == "refusal"), "refused call", "refused calls") + "; "
            + Count(items.Count(a => a.Kind == "answer"), "AI answer", "AI answers") + ", a recommendation only; "
            + Count(items.Count(a => a.Kind == "code-approval"), "AI-only code approval", "AI-only code approvals");
    }

    // One line of the AI activity: its kind, when, the client, the person the AI acted for, the application, and the item.
    private sealed class Activity
    {
        public string Id { get; set; } = string.Empty;

        public string Kind { get; set; } = "draft";

        public DateTime? On { get; set; }

        public string? Client { get; set; }

        public string? ForPerson { get; set; }

        public string? AppId { get; set; }

        // The intake request the activity is about, for the portfolio scope of an intake with no application yet.
        public string? IntakeId { get; set; }

        public string? State { get; set; }

        public bool ForwardByPerson { get; set; }

        public bool ForwardByAi { get; set; }

        public bool? EditedByPerson { get; set; }

        // True for a row an AI client created: a draft, or a code-approval row for a deployment the AI client recorded.
        public bool AiCreated { get; set; }

        public JsonTreeNode Json { get; set; } = JsonTreeNode.Null;
    }

    private static Activity DraftActivity(ToolScope s, StoredRow row, List<StoredRow>? history)
    {
        StoredRow? created = history?.FirstOrDefault(e => AiActorKinds.Contains(e.Text("actorKind") ?? string.Empty, StringComparer.Ordinal));
        bool lastByAi = AiActorKinds.Contains(row.Text("performedByActorKind") ?? string.Empty, StringComparer.Ordinal);
        // The drafted by pair (document 22, B22-15) names the first AI client and person even after a person edits the row.
        string? client = row.Text("draftedByClientApp") ?? created?.Text("clientApp") ?? (lastByAi ? row.Text("clientApp") : null);
        string forPerson = row.Text("draftedForPrincipal") ?? row.Text("createdByPrincipal") ?? string.Empty;
        DateTime? draftedOn = Js.ParseMoment(created?.Text("occurredOn")) ?? (lastByAi ? Js.ParseMoment(row.Text("engineCheckedOn")) : null);
        // A record a person takes forward has a draft state; a fact, a link, a claim or a run an AI client reports is
        // simply recorded.
        string meaning = DraftableEntities.Contains(row.Entity, StringComparer.Ordinal) ? row.Text("statusMeaning") ?? row.Text("draftStatus") ?? string.Empty : string.Empty;
        bool hasApprovals = row.Entity == "decision" && s.Query("approval-record", ("decision", row.Id)).Count > 0;
        StoredRow? withdraw = history?.FirstOrDefault(e => WithdrawTools.Contains(e.Text("entityName") ?? string.Empty, StringComparer.Ordinal));
        // A data use, location or mapping that is withdrawn ends archived, as a retired one does: the withdraw call in its
        // history tells the two apart (wave 4b review).
        string state = meaning is "withdrawn" or "discarded" or "cancelled" || (meaning == "archived" && withdraw != null) ? "withdrawn"
            : meaning == "draft" || (meaning == "proposed" && !hasApprovals) ? "waiting"
            : meaning.Length == 0 ? "recorded"
            : meaning is "accepted" or "rejected" or "registered" or "completed" or "done" or "activated" or "archived" or "succeeded" or "failed" ? "decided"
            : "submitted";

        // Who took the draft forward: the first submit call in the history, else the last stamp when a person made it.
        StoredRow? submit = history?.FirstOrDefault(e => SubmitTools.Contains(e.Text("entityName") ?? string.Empty, StringComparer.Ordinal));
        bool forwardKnown = submit != null;
        string? forwardKind = submit?.Text("actorKind");
        string? forwardBy = submit?.Text("actor");
        string? forwardOn = submit?.Text("occurredOn");
        if (submit == null && (state == "submitted" || state == "decided") && row.Text("performedByActorKind") == "person")
        {
            forwardKind = "person";
            forwardBy = row.Text("performedByPrincipal");
            forwardOn = row.Text("engineCheckedOn");
        }

        // A person's own call on the record before it went forward (or since, while it still waits) is an edit before accept.
        bool? edited = history == null || history.Count == 0 ? null
            : history.Any(e => e.Text("actorKind") == "person"
                && !SubmitTools.Contains(e.Text("entityName") ?? string.Empty, StringComparer.Ordinal)
                && !WithdrawTools.Contains(e.Text("entityName") ?? string.Empty, StringComparer.Ordinal)
                && (submit == null || string.CompareOrdinal(e.Text("occurredOn") ?? string.Empty, submit.Text("occurredOn") ?? string.Empty) <= 0));
        string? appId = row.Entity == "business-application" ? row.Id : ApplicationOf(s, row) ?? (row.Entity == "dependency" ? row.Text("fromApplication") : null);
        StoredRow? app = appId == null ? null : s.Rows.Get("business-application", appId);
        JsonTreeNode json = Js.Obj(
            ("kind", JsonTreeNode.FromString("draft")),
            ("occurredOn", draftedOn.HasValue ? Js.Utc(draftedOn.Value) : JsonTreeNode.Null),
            ("clientApp", Js.Str(client)),
            ("onBehalfOf", s.PrincipalRef(forPerson.Length == 0 ? null : forPerson)),
            ("application", app == null ? JsonTreeNode.Null : Js.Obj(("businessApplicationId", JsonTreeNode.FromString(app.Id)), ("name", JsonTreeNode.FromString(AppName(app))))),
            ("record", Js.Obj(
                ("entity", JsonTreeNode.FromString(row.Entity)),
                ("recordId", JsonTreeNode.FromString(row.Id)),
                ("recordNumber", Js.Str(row.Text("recordNumber"))),
                ("label", JsonTreeNode.FromString(RecordLabel(s, row))),
                ("status", s.StatusValue(row) is JsonTreeNode st && st.Kind != JsonTreeKind.Null ? st : row.Entity == "deployment-record" ? s.StatusValue(row, "result", "resultMeaning") : JsonTreeNode.Null),
                ("recordUrl", JsonTreeNode.FromString(s.RecordUrl(row.Entity, row.Id))))),
            ("state", JsonTreeNode.FromString(state)),
            ("takenForward", forwardKind == null ? JsonTreeNode.Null : Js.Obj(
                ("actorKind", JsonTreeNode.FromString(forwardKind)),
                ("principal", s.PrincipalRef(forwardBy)),
                ("on", ToolScope.MomentOrNull(forwardOn)),
                ("fromHistory", JsonTreeNode.FromBoolean(forwardKnown)))),
            ("editedByPersonFirst", edited.HasValue ? JsonTreeNode.FromBoolean(edited.Value) : JsonTreeNode.Null),
            ("withdrawn", state != "withdrawn" ? JsonTreeNode.Null : Js.Obj(
                ("principal", s.PrincipalRef(withdraw?.Text("actor") ?? row.Text("performedByPrincipal"))),
                ("on", ToolScope.MomentOrNull(withdraw?.Text("occurredOn") ?? row.Text("engineCheckedOn"))))),
            ("decided", row.Entity != "decision" || row.Text("decidedOn") == null ? JsonTreeNode.Null : Js.Obj(
                ("principal", s.PrincipalRef(row.Text("decider"))),
                ("on", ToolScope.MomentOrNull(row.Text("decidedOn"))),
                ("outcome", DecisionOutcome(s, row)))),
            ("refusal", JsonTreeNode.Null),
            ("answer", JsonTreeNode.Null),
            ("deployment", JsonTreeNode.Null),
            ("hasCallHistory", JsonTreeNode.FromBoolean(history != null && history.Count > 0)));
        return new Activity
        {
            Id = row.Id,
            Kind = "draft",
            On = draftedOn,
            Client = client,
            ForPerson = forPerson.Length == 0 ? null : forPerson,
            AiCreated = true,
            AppId = appId,
            IntakeId = row.Entity == "intake-request" ? row.Id : row.Text("subjectIntakeRequest") ?? row.Text("intakeRequest"),
            State = state,
            // Where each draft stands now (wave 4b review): a draft taken forward and later withdrawn counts as withdrawn, so
            // waiting, withdrawn, taken forward, submitted by the AI client and recorded add up to the drafts.
            ForwardByPerson = forwardKind == "person" && state is "submitted" or "decided",
            ForwardByAi = forwardKind != null && AiActorKinds.Contains(forwardKind, StringComparer.Ordinal) && state is "submitted" or "decided",
            EditedByPerson = edited,
            Json = json,
        };
    }

    private static Activity RefusalActivity(ToolScope s, StoredRow entry)
    {
        JsonTreeNode? changed = entry.Column("changedValuesJson");
        string? rowId = entry.Text("rowId");
        string? entity = AuditEntityOf(s, entry.Text("entityName") ?? string.Empty, rowId, changed) ?? FindEntityOf(s, rowId);
        StoredRow? record = entity == null || rowId == null ? null : s.Rows.Get(entity, rowId);
        if (HiddenRow(s, record))
        {
            // The refused call named a record the caller may not read: the activity shows no record (security review
            // 2026-11-24, B24-14), so naming a hidden ID in a refused call never reveals it later.
            s.HideFromCaller(record!.Entity, 1);
            record = null;
        }

        string? appId = record == null ? null : record.Entity == "business-application" ? record.Id : ApplicationOf(s, record);
        StoredRow? app = appId == null ? null : s.Rows.Get("business-application", appId);
        string forPerson = entry.Text("onBehalfOf") ?? entry.Text("actor") ?? string.Empty;
        DateTime? on = Js.ParseMoment(entry.Text("occurredOn"));
        JsonTreeNode json = Js.Obj(
            ("kind", JsonTreeNode.FromString("refusal")),
            ("occurredOn", on.HasValue ? Js.Utc(on.Value) : JsonTreeNode.Null),
            ("clientApp", Js.Str(entry.Text("clientApp"))),
            ("onBehalfOf", s.PrincipalRef(forPerson.Length == 0 ? null : forPerson)),
            ("application", app == null ? JsonTreeNode.Null : Js.Obj(("businessApplicationId", JsonTreeNode.FromString(app.Id)), ("name", JsonTreeNode.FromString(AppName(app))))),
            ("record", record == null ? JsonTreeNode.Null : Js.Obj(
                ("entity", JsonTreeNode.FromString(record.Entity)),
                ("recordId", JsonTreeNode.FromString(record.Id)),
                ("recordNumber", Js.Str(record.Text("recordNumber"))),
                ("label", JsonTreeNode.FromString(RecordLabel(s, record))),
                ("status", s.StatusValue(record)),
                ("recordUrl", JsonTreeNode.FromString(s.RecordUrl(record.Entity, record.Id))))),
            ("state", JsonTreeNode.Null),
            ("takenForward", JsonTreeNode.Null),
            ("editedByPersonFirst", JsonTreeNode.Null),
            ("withdrawn", JsonTreeNode.Null),
            ("decided", JsonTreeNode.Null),
            ("refusal", Js.With(RefusalView(s, changed), ("tool", JsonTreeNode.FromString(entry.Text("entityName") ?? "unknown_tool")), ("auditEntryId", JsonTreeNode.FromString(entry.Id)))),
            ("answer", JsonTreeNode.Null),
            ("deployment", JsonTreeNode.Null),
            ("hasCallHistory", JsonTreeNode.True));
        return new Activity { Id = entry.Id, Kind = "refusal", On = on, Client = entry.Text("clientApp"), ForPerson = forPerson.Length == 0 ? null : forPerson, AppId = appId, IntakeId = record == null ? null : record.Entity == "intake-request" ? record.Id : record.Text("subjectIntakeRequest") ?? record.Text("intakeRequest"), Json = json };
    }

    private static Activity AnswerActivity(ToolScope s, StoredRow record)
    {
        StoredRow? decision = s.Rows.Get("decision", record.Text("decision") ?? string.Empty);
        if (HiddenRow(s, decision))
        {
            s.HideFromCaller("decision", 1);
            decision = null;
        }

        string? appId = decision == null ? null : ApplicationOf(s, decision);
        StoredRow? app = appId == null ? null : s.Rows.Get("business-application", appId);
        DateTime? on = Js.ParseMoment(record.Text("respondedOn")) ?? Js.ParseMoment(record.Text("engineCheckedOn"));
        string? forPerson = record.Text("onBehalfOf") ?? record.Text("askedPrincipal");
        JsonTreeNode json = Js.Obj(
            ("kind", JsonTreeNode.FromString("answer")),
            ("occurredOn", on.HasValue ? Js.Utc(on.Value) : JsonTreeNode.Null),
            ("clientApp", Js.Str(record.Text("clientApp"))),
            ("onBehalfOf", s.PrincipalRef(forPerson)),
            ("application", app == null ? JsonTreeNode.Null : Js.Obj(("businessApplicationId", JsonTreeNode.FromString(app.Id)), ("name", JsonTreeNode.FromString(AppName(app))))),
            ("record", decision == null ? JsonTreeNode.Null : Js.Obj(
                ("entity", JsonTreeNode.FromString("decision")),
                ("recordId", JsonTreeNode.FromString(decision.Id)),
                ("recordNumber", Js.Str(decision.Text("recordNumber"))),
                ("label", JsonTreeNode.FromString(RecordLabel(s, decision))),
                ("status", s.StatusValue(decision)),
                ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("decision", decision.Id))))),
            ("state", JsonTreeNode.Null),
            ("takenForward", JsonTreeNode.Null),
            ("editedByPersonFirst", JsonTreeNode.Null),
            ("withdrawn", JsonTreeNode.Null),
            ("decided", JsonTreeNode.Null),
            ("refusal", JsonTreeNode.Null),
            ("answer", Js.Obj(
                ("approvalRecordId", JsonTreeNode.FromString(record.Id)),
                ("approver", s.PrincipalRef(record.Text("approver"))),
                ("approverActorKind", Js.Str(record.Text("approverActorMeaning") ?? record.Text("approverActorKind"))),
                ("outcome", s.StatusValue(record, "outcome", "outcomeMeaning") is JsonTreeNode o && o.Kind != JsonTreeKind.Null ? o : ToolScope.StatusValue("pending", null)),
                ("countsTowardRequirement", JsonTreeNode.FromBoolean(record.Flag("countsTowardRequirement"))))),
            ("deployment", JsonTreeNode.Null),
            ("hasCallHistory", JsonTreeNode.False));
        return new Activity { Id = record.Id, Kind = "answer", On = on, Client = record.Text("clientApp"), ForPerson = forPerson, AppId = appId, IntakeId = decision?.Text("subjectIntakeRequest"), Json = json };
    }

    // drafted is the draft row of the same deployment when an AI client recorded it: its client, person and state stay on
    // the one row (B24-17).
    private static Activity CodeApprovalActivity(ToolScope s, StoredRow record, Activity? drafted = null)
    {
        string? appId = record.Text("businessApplication");
        StoredRow? app = appId == null ? null : s.Rows.Get("business-application", appId);
        DateTime? on = Js.ParseMoment(record.Text("finishedOn"));
        string? client = drafted?.Client ?? record.Text("startedByAiClient") ?? record.Text("clientApp");
        string? forPerson = drafted?.ForPerson ?? record.Text("onBehalfOf");
        JsonTreeNode json = Js.Obj(
            ("kind", JsonTreeNode.FromString("code-approval")),
            ("occurredOn", on.HasValue ? Js.Utc(on.Value) : JsonTreeNode.Null),
            ("clientApp", Js.Str(client)),
            ("onBehalfOf", s.PrincipalRef(forPerson)),
            ("application", app == null ? JsonTreeNode.Null : Js.Obj(("businessApplicationId", JsonTreeNode.FromString(app.Id)), ("name", JsonTreeNode.FromString(AppName(app))))),
            ("record", Js.Obj(
                ("entity", JsonTreeNode.FromString("deployment-record")),
                ("recordId", JsonTreeNode.FromString(record.Id)),
                ("recordNumber", JsonTreeNode.Null),
                ("label", JsonTreeNode.FromString(RecordLabel(s, record))),
                ("status", s.StatusValue(record, "result", "resultMeaning")),
                ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("deployment-record", record.Id))))),
            ("state", drafted == null ? JsonTreeNode.Null : JsonTreeNode.FromString(drafted.State ?? "recorded")),
            ("takenForward", JsonTreeNode.Null),
            ("editedByPersonFirst", JsonTreeNode.Null),
            ("withdrawn", JsonTreeNode.Null),
            ("decided", JsonTreeNode.Null),
            ("refusal", JsonTreeNode.Null),
            ("answer", JsonTreeNode.Null),
            ("deployment", Js.Obj(
                ("deploymentRecordId", JsonTreeNode.FromString(record.Id)),
                ("route", RecordEndpointAnswers.Route(s.Snapshot, record)),
                ("flags", RecordEndpointAnswers.Flags(s.Snapshot, record, null)),
                ("gateLevelApplied", JsonTreeNode.FromString(record.Text("gateLevelApplied") ?? "record")))),
            ("hasCallHistory", JsonTreeNode.FromBoolean(drafted != null && Js.Flag(drafted.Json, "hasCallHistory") == true)));
        return new Activity { Id = record.Id, Kind = "code-approval", On = on, Client = client, ForPerson = forPerson, AppId = appId, State = drafted?.State, AiCreated = drafted != null, Json = json };
    }

    // The entity of a row known only by its ID: the entities a refused call names a record of, tried in turn.
    private static string? FindEntityOf(ToolScope s, string? rowId)
    {
        if (rowId == null)
        {
            return null;
        }

        foreach (string entity in new[] { "approval-record", "decision", "intake-request", "change-request", "review", "claim", "document-link", "deployment-record", "business-application", "configuration-draft", "dependency", "deployment-target", "portfolio" })
        {
            if (s.Rows.Get(entity, rowId) != null)
            {
                return entity;
            }
        }

        return null;
    }
}
