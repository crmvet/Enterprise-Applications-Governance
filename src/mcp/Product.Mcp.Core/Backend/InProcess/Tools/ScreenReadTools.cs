using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Operations;
using Product.Engine.Core.Snapshots;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Json;

namespace Product.Mcp.Core.Backend.InProcess;

// The reads the web app's first screens need (D-36; document 24, tools 53 to 59): one decision with
// its approval state, the approval requests across decisions, the intake list and one intake request, the reviews
// across the portfolio, the portfolios, and the owner assignments across the portfolio. Each read is honest about what
// the caller cannot see: rows a read visibility setting hides are counted in couldNotRead, never shown, and a single
// record the caller may not read answers not_found. Lists page with skipToken, which encodes the query.
internal static partial class ToolHandlers
{
    private static readonly string[] OpenDemandMeanings = { "submitted", "in-progress", "completed" };

    // ---- 53. get_decision ----

    private static JsonTreeNode GetDecision(ToolScope s)
    {
        StoredRow d = s.RequireRow("decision", s.Text("decisionId"), "decisionId");
        if (d.Text("subjectKind") == "intake-request" && SubjectIdOf(d) is string intakeId && s.Rows.Get("intake-request", intakeId) is StoredRow intake && !CanReadIntake(s, intake))
        {
            throw s.NotFound("Decision", d.Id, "decisionId");
        }

        (string label, string? appId, string? appName) = DecisionSubjectLabel(s, d);
        string subjectKind = d.Text("subjectKind") ?? "business-application";
        string? reviewId = ReviewIdOf(s, d);
        string subjectId = SubjectIdOf(s, d) ?? d.Id;
        string subjectEntity = subjectKind == "review" && reviewId == null ? "business-application" : subjectKind;
        bool proposed = d.Text("statusMeaning") == "proposed";
        (JsonTreeNode Data, int Missing, bool Ready)? state = proposed ? ApprovalState(s, d.Id, detailed: true) : null;
        JsonTreeNode approvals = state?.Data ?? SettledApprovals(s, d);
        JsonTreeNode gate = proposed ? HoldingGate(s, d.Id) : JsonTreeNode.Null;

        JsonTreeNode decision = Js.Obj(
            ("decisionId", JsonTreeNode.FromString(d.Id)),
            ("recordNumber", Js.Str(d.Text("recordNumber"))),
            ("decisionType", s.LabelledKey("decision-type", d.Text("decisionType") ?? "unknown")),
            ("statement", JsonTreeNode.FromString(d.Text("statement") ?? string.Empty)),
            ("context", Js.Str(ContextOf(d))),
            ("optionsConsidered", Js.Str(d.Text("optionsConsidered"))),
            ("confidence", ListValue(s, "confidence", d.Text("confidence"))),
            ("outcome", DecisionOutcome(s, d)),
            ("reason", Js.Str(d.Text("reason"))),
            ("status", s.StatusValue(d) is JsonTreeNode st && st.Kind != JsonTreeKind.Null ? st : ToolScope.StatusValue("proposed", null)),
            ("decider", s.PrincipalRef(d.Text("decider"))),
            ("decidedOn", ToolScope.MomentOrNull(d.Text("decidedOn"))),
            ("supersedes", Js.Str(d.Text("supersedes"))),
            ("supersededBy", Js.Str(d.Text("supersededBy"))),
            ("amends", Js.Str(d.Text("amends"))),
            ("amendedBy", Ids(s.Query("decision", ("amends", d.Id)).Select(a => a.Id).OrderBy(a => a, StringComparer.Ordinal))),
            ("configurationSetVersion", JsonTreeNode.FromInt64(d.Number("configurationSetVersion") ?? s.Session.ConfigurationSetVersion)),
            ("proposedBy", s.PrincipalRef(ProposerOf(s, d))),
            ("exception", d.Text("exceptionStandard") == null && d.Text("exceptionReviewOn") == null ? JsonTreeNode.Null : Js.Obj(
                ("standard", Js.Str(d.Text("exceptionStandard"))),
                ("reviewOn", ToolScope.DateOrNull(d.Text("exceptionReviewOn"))))),
            ("slideBackSignal", SlideBackSignalRef(d.Text("slideBackSignal"))),
            ("proposedChanges", ProposedChangesView(s, d)),
            ("configurationChange", ConfigurationChangeView(s, d)),
            ("isAiCreated", AiCreated(d)),
            ("performedBy", Stamp(s, d)),
            ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("decision", d.Id))));
        JsonTreeNode subject = Js.Obj(
            ("subjectKind", JsonTreeNode.FromString(subjectKind)),
            ("subjectId", JsonTreeNode.FromString(subjectId)),
            ("label", JsonTreeNode.FromString(label)),
            ("businessApplicationId", Js.Str(appId)),
            ("applicationName", Js.Str(appName)),
            ("recordUrl", JsonTreeNode.FromString(s.RecordUrl(subjectEntity, subjectId))));
        JsonTreeNode evidence = Arr(s.Query("evidence-link", ("subjectDecision", d.Id))
            .Where(l => (l.Text("url") ?? string.Empty).StartsWith("https://", StringComparison.Ordinal))
            .OrderBy(l => l.Text("capturedOn") ?? string.Empty, StringComparer.Ordinal).ThenBy(l => l.Id, StringComparer.Ordinal)
            .Select(l => Js.Obj(
                ("evidenceLinkId", JsonTreeNode.FromString(l.Id)),
                ("linkKind", s.LabelledKey("link-kind", l.Text("linkKind") ?? "unknown")),
                ("url", JsonTreeNode.FromString(l.Text("url")!)),
                ("label", JsonTreeNode.FromString(l.Text("label") ?? string.Empty)),
                ("evidenceLevel", ListValue(s, "evidence-level", l.Text("evidenceLevel"))),
                ("capturedOn", ToolScope.MomentOrNull(l.Text("capturedOn"))))));

        JsonTreeNode data = Js.Obj(("decision", decision), ("subject", subject), ("approvals", approvals), ("evidence", evidence), ("gate", gate), ("data", DataDecisionView(s, d)));
        string status = d.Text("statusMeaning") ?? "proposed";
        bool held = gate.Kind == JsonTreeKind.Object && Js.Flag(gate, "ready") == false;
        string summary = proposed
            ? "The " + s.Label("decision-type", d.Text("decisionType")).ToLowerInvariant() + " decision is proposed. "
                + (Js.Flag(approvals, "ready") == true
                    ? "The approvals the rules require are in." + (held ? " The " + Js.Text(gate, "label") + " gate is not ready, so the decision cannot be accepted yet." : string.Empty)
                    : Count(Js.Items(approvals, "missing").Count, "approval is", "approvals are") + " still missing; only a person's answer counts.")
            : "The " + s.Label("decision-type", d.Text("decisionType")).ToLowerInvariant() + " decision is " + status + ".";
        return s.Answer(data, summary, s.RecordUrl("decision", d.Id),
            proposed ? new[] { ToolScope.Next("get_approval_status", ("subjectKind", JsonTreeNode.FromString("decision")), ("subjectId", JsonTreeNode.FromString(d.Id))) } : Array.Empty<NextStep>());
    }

    // What the decision writes once accepted, as people read it (Proposed Changes Json; document 25, B25-8; document 24,
    // B24-9): the owner changes with each person's label and the assignment they end, the tier, or the autonomy level.
    // Null when the decision proposes none of these.
    private static JsonTreeNode ProposedChangesView(ToolScope s, StoredRow d)
    {
        JsonTreeNode? proposal = d.Column("proposedChangesJson");
        if (proposal == null || proposal.Kind != JsonTreeKind.Object)
        {
            return JsonTreeNode.Null;
        }

        JsonTreeNode? owners = Js.Member(proposal, "owners");
        JsonTreeNode ownerChanges = Arr((owners != null && owners.Kind == JsonTreeKind.Array ? owners.Items : Array.Empty<JsonTreeNode>()).Select(o =>
        {
            string? end = Js.Text(o, "endAssignmentId");
            StoredRow? ended = end == null ? null : s.Rows.Get("owner-assignment", end);
            return Js.Obj(
                ("ownerRole", s.LabelledKey("owner-role", Js.Text(o, "ownerRoleKey"))),
                ("add", s.PrincipalRef(Js.Text(o, "addObjectId"))),
                ("isDeputy", JsonTreeNode.FromBoolean(Js.Flag(o, "isDeputy") == true)),
                ("startOn", ToolScope.DateOrNull(Js.Text(o, "startOn"))),
                ("endAssignmentId", Js.Str(end)),
                ("ends", ended == null ? JsonTreeNode.Null : s.PrincipalRef(ended.Text("principal"))),
                ("endsIsDeputy", JsonTreeNode.FromBoolean(ended != null && ended.Flag("isDeputy"))));
        }));
        return Js.Obj(
            ("owners", ownerChanges),
            ("tier", s.LabelledKey("tier", Js.Text(proposal, "tierKey"))),
            ("autonomyLevel", ListValue(s, EngineKeys.AutonomyLevelList, Js.Text(proposal, "autonomyLevelKey"))));
    }

    // What a data decision page shows (docs/architecture/data-taxonomy.md, Screens, decision page additions; slice 3): the domain path, the
    // classification, the governing rule and where it is set, the AI and de-identified flags, the use or copy itself, the
    // computed state with "1 of 2 approvals", who is asked (all approvers, so a person sees who else is asked), and the
    // configuration version the decision was stamped with. Null for a decision with no data subject.
    private static JsonTreeNode DataDecisionView(ToolScope s, StoredRow d)
    {
        string? kind = d.Text("subjectKind");
        string? domainKey = DataDomainOfDecision(s, d);
        bool dataKind = kind == EngineKeys.DataUseSubjectKind || kind == EngineKeys.DataMappingSubjectKind || kind == EngineKeys.DataDomainSubjectKind || kind == EngineKeys.DataLocationSubjectKind;
        if (!dataKind)
        {
            return JsonTreeNode.Null;
        }

        var tree = new DataTree(s.Snapshot);
        StoredRow? use = kind == EngineKeys.DataUseSubjectKind && d.Text("subjectDataUse") is string u ? s.Rows.Get("data-use", u) : null;
        StoredRow? mapping = kind == EngineKeys.DataMappingSubjectKind && d.Text("subjectDataMapping") is string m ? s.Rows.Get("data-mapping", m) : null;
        StoredRow? location = (use ?? mapping)?.Text("dataLocation") is string l ? s.Rows.Get("data-location", l)
            : kind == EngineKeys.DataLocationSubjectKind && d.Text("subjectDataLocation") is string dl ? s.Rows.Get("data-location", dl) : null;
        JsonTreeNode? evaluation = use != null || mapping != null ? s.Call.DataUse((use ?? mapping)!.Id)?.Payload : null;
        bool known = domainKey != null && tree.Domain(domainKey) != null;
        bool reachesAi = use?.Flag("reachesAi") == true;
        EffectiveDataRule? rule = !known ? null
            : use != null ? tree.GoverningRuleForUse(domainKey!, reachesAi)
            : Js.Member(evaluation, "governingRule") is JsonTreeNode g && g.Kind == JsonTreeKind.Object ? tree.Rule(domainKey!, DataRuleName.Copy) : null;
        return Js.Obj(
            ("subjectKind", JsonTreeNode.FromString(kind!)),
            ("domain", known ? DomainRef(tree, domainKey!) : JsonTreeNode.Null),
            ("location", location == null ? JsonTreeNode.Null : LocationBrief(s, location)),
            ("use", use == null ? JsonTreeNode.Null : Js.Obj(
                ("dataUseId", JsonTreeNode.FromString(use.Id)),
                ("recordNumber", Js.Str(use.Text("recordNumber"))),
                ("access", JsonTreeNode.FromString(use.Text("access") ?? "read")),
                ("purpose", JsonTreeNode.FromString(use.Text("purpose") ?? string.Empty)),
                ("aiServiceAsSeen", Js.Str(use.Text("aiServiceAsSeen"))),
                ("status", s.StatusValue(use) is JsonTreeNode st && st.Kind != JsonTreeKind.Null ? st : ToolScope.StatusValue("draft", null)))),
            ("mapping", mapping == null ? JsonTreeNode.Null : Js.Obj(
                ("dataMappingId", JsonTreeNode.FromString(mapping.Id)),
                ("origin", JsonTreeNode.FromString(mapping.Text("dataOrigin") ?? "system-of-record")),
                ("containers", Js.Strs(Containers(mapping))))),
            ("classification", known ? ClassificationValue(s, tree, domainKey!) : JsonTreeNode.Null),
            ("governingRule", rule == null ? JsonTreeNode.Null : GoverningRuleValue(s, tree, rule)),
            ("reachesAi", use == null ? JsonTreeNode.Null : JsonTreeNode.FromBoolean(reachesAi)),
            ("isDeidentified", use == null ? (mapping == null ? JsonTreeNode.Null : JsonTreeNode.FromBoolean(mapping.Text("dataOrigin") == "masked-copy")) : JsonTreeNode.FromBoolean(use.Flag("isDeidentified"))),
            ("state", Js.Str(Js.Text(evaluation, "state"))),
            ("approvalsNeeded", JsonTreeNode.FromInt64(Js.Number(evaluation, "approvalsNeeded") ?? 0)),
            ("approvalsGiven", Js.Number(evaluation, "approvalsGiven") is long given ? JsonTreeNode.FromInt64(given) : JsonTreeNode.Null),
            ("asked", Arr(Js.Items(evaluation, "approvers").Select(a => ApproverValue(s, tree, a)))),
            ("configurationSetVersion", JsonTreeNode.FromInt64(d.Number("configurationSetVersion") ?? s.Session.ConfigurationSetVersion)));
    }

    // The data facts an approval list item carries (slice 3): enough to read the card without opening it.
    private static JsonTreeNode DataDecisionBrief(ToolScope s, StoredRow d)
    {
        string? kind = d.Text("subjectKind");
        if (kind != EngineKeys.DataUseSubjectKind && kind != EngineKeys.DataMappingSubjectKind && kind != EngineKeys.DataDomainSubjectKind && kind != EngineKeys.DataLocationSubjectKind)
        {
            return JsonTreeNode.Null;
        }

        var tree = new DataTree(s.Snapshot);
        string? domainKey = DataDomainOfDecision(s, d);
        bool known = domainKey != null && tree.Domain(domainKey) != null;
        StoredRow? use = kind == EngineKeys.DataUseSubjectKind && d.Text("subjectDataUse") is string u ? s.Rows.Get("data-use", u) : null;
        EffectiveDataRule? rule = known && use != null ? tree.GoverningRuleForUse(domainKey!, use.Flag("reachesAi")) : known && kind == EngineKeys.DataMappingSubjectKind ? tree.Rule(domainKey!, DataRuleName.Copy) : null;
        return Js.Obj(
            ("subjectKind", JsonTreeNode.FromString(kind!)),
            ("domain", known ? DomainRef(tree, domainKey!) : JsonTreeNode.Null),
            ("classification", known && tree.Classification(domainKey!) is EffectiveClassification c ? s.LabelledKey("data-classification", c.Key) : JsonTreeNode.Null),
            ("rule", rule == null ? JsonTreeNode.Null : JsonTreeNode.FromString(EngineContract.DataHandlingRules.ToWire(rule.Value))),
            ("reachesAi", use == null ? JsonTreeNode.Null : JsonTreeNode.FromBoolean(use.Flag("reachesAi"))),
            ("isDeidentified", use == null ? JsonTreeNode.Null : JsonTreeNode.FromBoolean(use.Flag("isDeidentified"))));
    }

    private static StoredRow? ConfigurationDraftOf(ToolScope s, StoredRow d)
    {
        return d.Text("subjectConfigurationDraft") is string id ? s.Rows.Get("configuration-draft", id) : null;
    }

    // A configuration draft's change in plain words, against the version it was drafted on (so an activated change still
    // says what it changed, field by field), else the engine's stored summary (QA: keys in backticks, and the diff lost
    // after activation).
    internal static IReadOnlyList<string> DraftChangeLines(ToolScope s, StoredRow draft)
    {
        try
        {
            int baseVersion = (int)(draft.Number("baseVersionNumber") ?? s.Call.Snapshot.VersionNumber);
            IReadOnlyList<string> plain = Product.Engine.Configuration.Snapshots.ChangeWords.Describe(s.LoadVersion(baseVersion, "baseVersionNumber").Snapshot, draft.Column("changeJson"));
            if (plain.Count > 0)
            {
                return plain;
            }
        }
        catch (Exception e) when (!(e is OutOfMemoryException))
        {
            // The base version is not stored: the engine's summary below.
        }

        return (draft.Text("changeSummary") ?? string.Empty).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
    }

    // The slide-back signal a decision acts on, by number, key and label (document 24, B24-22), or null.
    internal static JsonTreeNode SlideBackSignalRef(string? signalKey)
    {
        int index = signalKey == null ? -1 : IndexOf(Product.Engine.Configuration.Model.MeasureSettings.SignalKeys, signalKey);
        return index < 0 ? JsonTreeNode.Null : Js.Obj(
            ("number", JsonTreeNode.FromInt64(index + 1)),
            ("key", JsonTreeNode.FromString(signalKey!)),
            ("label", JsonTreeNode.FromString(SignalLabels[index])));
    }

    private static int IndexOf(IReadOnlyList<string> list, string value)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (string.Equals(list[i], value, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    // The ten signals' short labels, in the order of MeasureSettings.SignalKeys (get_measures holds the long ones).
    private static readonly string[] SignalLabels =
    {
        "Changes wait for a meeting", "Environments by ticket", "Gap check stale", "Apps registered late", "Configuration not kept",
        "Unsanctioned AI", "A Microsoft tool rebuilt", "No human approver", "Documents slipping", "Access drift",
    };

    // Who proposed a decision, as set once when it was created: the admin who instructed a configuration draft, else the
    // person an AI client drafted it for, else its creator. Never onBehalfOf, which every later write restamps, so the
    // person whose answer accepted the decision never shows as its proposer (product review, major).
    internal static string? ProposerOf(ToolScope s, StoredRow d)
    {
        return ConfigurationDraftOf(s, d)?.Text("instructedBy") ?? d.Text("draftedForPrincipal") ?? d.Text("createdByPrincipal");
    }

    // What a configuration change or gate policy change decision changes (product review 2026-11-24): the draft's change
    // summary, the version it changes, and what OP-09 found when the draft was saved. Null for any other decision.
    private static JsonTreeNode ConfigurationChangeView(ToolScope s, StoredRow d)
    {
        string? draftId = d.Text("subjectConfigurationDraft");
        StoredRow? draft = draftId == null ? null : s.Rows.Get("configuration-draft", draftId);
        if (draft == null)
        {
            return JsonTreeNode.Null;
        }

        JsonTreeNode? validation = draft.Column("validationJson");
        IEnumerable<string> lines = DraftChangeLines(s, draft);
        IEnumerable<string> problems = Js.Items(validation, "issues").Select(i => Js.Text(i, "message")).Where(m => m != null).Select(m => m!);
        JsonTreeNode? isValid = Js.Member(validation, "isValid");
        return Js.Obj(
            ("configurationDraftId", JsonTreeNode.FromString(draft.Id)),
            ("changeSummary", Js.Strs(lines)),
            ("baseVersionNumber", draft.Number("baseVersionNumber") is long version ? JsonTreeNode.FromInt64(version) : JsonTreeNode.Null),
            ("isValid", isValid != null && (isValid.Kind == JsonTreeKind.True || isValid.Kind == JsonTreeKind.False) ? isValid : JsonTreeNode.Null),
            ("problems", Js.Strs(problems)));
    }

    // The approval answer pre-check for the caller on one record (D-38; document 24, B24-9): canAnswer is true only when
    // the record is pending, asks the caller, and the engine's approval answer check would take their approval now. When
    // the record asks the caller but the check refuses, cannotAnswer says why, with the separation rule that refuses, so
    // the page hides Approve and Reject and explains. A record that does not ask the caller has neither.
    private static (JsonTreeNode CanAnswer, JsonTreeNode CannotAnswer) AnswerCheck(ToolScope s, StoredRow record, string? applicationId)
    {
        if (record.Text("outcomeMeaning") != "pending" || !AskedOfMe(s, record, applicationId))
        {
            return (JsonTreeNode.False, JsonTreeNode.Null);
        }

        EvaluationResult<ApprovalAnswerResult?> check = s.Call.ApprovalAnswerCheck(record.Id);
        if (check.Outcome == ApprovalAnswerOperation.Allowed)
        {
            return (JsonTreeNode.True, JsonTreeNode.Null);
        }

        Reason? reason = check.Reasons.FirstOrDefault(r => r.Severity == ReasonSeverity.Error);
        string? ruleKey = reason?.Entity == "Separation Rule" ? reason.Ref : null;
        return (JsonTreeNode.False, Js.Obj(
            ("code", JsonTreeNode.FromString(reason?.Code ?? "approval.answer-not-asked")),
            ("message", JsonTreeNode.FromString(reason?.Message ?? "The engine would not take your answer on this request.")),
            ("separationRule", ruleKey == null ? JsonTreeNode.Null : s.LabelledKey("separation-rule", ruleKey))));
    }

    // Whether the person may answer a pending request that asks them, as the engine judges it for them now (wave 4b verify round):
    // the answer check of list_approvals and get_decision for a person's own call, and for an AI client's call the same
    // check for the person it acts for (its own stops at "AI never approves"). Separation says a separation rule refused.
    private static (bool May, bool Separation) PersonAnswer(ToolScope s, StoredRow record, string? applicationId, (JsonTreeNode CanAnswer, JsonTreeNode CannotAnswer)? known = null)
    {
        if (record.Text("outcomeMeaning") != "pending" || !AskedOfMe(s, record, applicationId))
        {
            return (false, false);
        }

        if (!s.IsAiCaller)
        {
            (JsonTreeNode can, JsonTreeNode cannot) = known ?? AnswerCheck(s, record, applicationId);
            return (can.Kind == JsonTreeKind.True, Js.Member(cannot, "separationRule") is JsonTreeNode rule && rule.Kind != JsonTreeKind.Null);
        }

        EvaluationResult<ApprovalAnswerResult?> check = s.Call.ApprovalAnswerCheck(record.Id, forThePerson: true);
        return (check.Outcome == ApprovalAnswerOperation.Allowed, check.Reasons.Any(r => r.Severity == ReasonSeverity.Error && r.Entity == "Separation Rule"));
    }

    // The open data uses of the decision's application that are not decided yet (not needed or allowed), each with its
    // state and the decision that would decide it, so the gate hold links to what blocks it (product review, step 6).
    private static JsonTreeNode OpenUsesOf(ToolScope s, string decisionId)
    {
        string? appId = s.Rows.Get("decision", decisionId) is StoredRow d ? ApplicationOf(s, d) : null;
        if (appId == null)
        {
            return Js.EmptyArray;
        }

        var tree = new Product.Engine.Core.Operations.DataTree(s.Snapshot);
        var items = new List<JsonTreeNode>();
        foreach (StoredRow use in s.Query("data-use", ("businessApplication", appId)).Where(OpenOn(s)).Where(u => u.Text("statusMeaning") == "registered").OrderBy(u => u.Text("recordNumber") ?? u.Id, StringComparer.Ordinal))
        {
            JsonTreeNode? evaluation = s.Call.DataUse(use.Id)?.Payload;
            string state = Js.Text(evaluation, "state") ?? "needed";
            if (state == "not-needed" || state == "allowed")
            {
                continue;
            }

            string? waiting = Js.Text(evaluation, "decisionId");
            items.Add(Js.Obj(
                ("dataUseId", JsonTreeNode.FromString(use.Id)),
                ("recordNumber", Js.Str(use.Text("recordNumber"))),
                ("label", JsonTreeNode.FromString((use.Text("dataDomain") is string domain && tree.Domain(domain) != null ? tree.PathText(domain) : "A data domain") + (use.Flag("reachesAi") ? ", with AI" : string.Empty))),
                ("state", JsonTreeNode.FromString(state)),
                ("decisionId", Js.Str(waiting)),
                ("recordUrl", JsonTreeNode.FromString(waiting != null ? s.RecordUrl("decision", waiting) : s.RecordUrl("data-use", use.Id)))));
        }

        return Arr(items);
    }

    // The gate whose gate decision type is this decision's type, as the accept step reads it (OP-07, G-14): the decision
    // is accepted only once the gate is ready, even after every approval is in. Null when no gate takes this type or the
    // subject has no application kind to read it with.
    private static JsonTreeNode HoldingGate(ToolScope s, string decisionId)
    {
        EvaluationResult<JsonTreeNode?>? readiness = s.Call.DecisionGateReadiness(decisionId);
        if (readiness?.Payload is not JsonTreeNode payload || Js.Text(payload, "gateKey") is not string gateKey)
        {
            return JsonTreeNode.Null;
        }

        var unmet = Js.Items(payload, "requirements").Where(r => Js.Flag(r, "met") != true && Js.Text(r, "requirementKey") != null).Select(r =>
        {
            string key = Js.Text(r, "requirementKey")!;
            string? guidelineKey = Js.Text(r, "guidelineKey");
            string? guideline = guidelineKey == null ? null : Js.Text(s.ConfigRow("guideline", guidelineKey)?.Columns, "number");
            return Js.Obj(
                ("requirementKey", JsonTreeNode.FromString(key)),
                ("label", JsonTreeNode.FromString(s.Label("gate-requirement", key))),
                ("requirementKind", Js.Str(Js.Text(r, "requirementKind"))),
                ("requiredOwnerRoleKey", Js.Str(Js.Text(r, "requiredOwnerRoleKey"))),
                ("guideline", Js.Str(guideline)),
                ("guidelineRef", GuidelineRef(s, guidelineKey)),
                ("openUses", Js.Text(r, "requirementKind") == "data-uses-decided" ? OpenUsesOf(s, decisionId) : Js.EmptyArray));
        });
        return Js.Obj(
            ("gateKey", JsonTreeNode.FromString(gateKey)),
            ("label", JsonTreeNode.FromString(s.Label("gate", gateKey))),
            ("ready", JsonTreeNode.FromBoolean(Js.Flag(payload, "ready") == true)),
            ("unmet", Arr(unmet)));
    }

    // The approval state of a proposed decision: OP-02 under the decision's stamped version and OP-03 over its records, as
    // get_approval_status reads it. detailed adds, on each record, the role it asks, the request and expiry dates and
    // whether it is a consultation (for the decision page).
    private static (JsonTreeNode Data, int Missing, bool Ready) ApprovalState(ToolScope s, string decisionId, bool detailed)
    {
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
            bool proposedOwners = Js.Flag(requirement, "approverProposedOwners") == true;
            if ((role == null ? 0 : 1) + (ownerRole == null ? 0 : 1) + (proposedOwners ? 1 : 0) != 1)
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
                ("asksProposedOwners", proposedOwners ? JsonTreeNode.True : null),
                ("approvalMode", JsonTreeNode.FromString(Js.Text(requirement, "approvalMode") ?? (rule == null ? null : Js.Text(rule.Columns, "approvalMode")) ?? "first-to-respond")),
                ("minimumCount", JsonTreeNode.FromInt64(minimum)),
                ("received", JsonTreeNode.FromInt64(received)),
                ("isConsultation", JsonTreeNode.FromBoolean(consultation))));
            if (!consultation && received < minimum && Js.Text(states.TryGetValue(key, out JsonTreeNode? state) ? state : null, "state") != "satisfied")
            {
                missing.Add(Js.Obj(("approvalRuleKey", JsonTreeNode.FromString(key)), ("requiredRoleKey", Js.OptStr(role)), ("requiredOwnerRoleKey", Js.OptStr(ownerRole)), ("asksProposedOwners", proposedOwners ? JsonTreeNode.True : null), ("stillNeeded", JsonTreeNode.FromInt64(Math.Max(1, minimum - received)))));
            }
        }

        string? applicationId = detailed && s.Rows.Get("decision", decisionId) is StoredRow decisionRow ? ApplicationOf(s, decisionRow) : null;
        JsonTreeNode recordList = Arr(records.Select(r => ApprovalRecordItem(s, r, counts != null ? Js.Flag(counts, r.Id) == true : r.Flag("countsTowardRequirement"), detailed, applicationId)));
        bool ready = status == null ? required.Outcome == "engine-accept" : status.Outcome == "satisfied";
        var members = new List<(string, JsonTreeNode?)>
        {
            ("decisionId", JsonTreeNode.FromString(decisionId)),
            ("required", Arr(requiredList)),
            ("records", recordList),
            ("missing", Arr(missing)),
            ("ready", JsonTreeNode.FromBoolean(ready)),
            ("settledBy", JsonTreeNode.Null),
        };
        if (detailed)
        {
            members.Add(("evaluated", JsonTreeNode.True));
        }

        return (Js.Obj(members.ToArray()), missing.Count, ready);
    }

    // A settled decision's approval records as the engine stored them when it settled (the recompute wrote Counts Toward
    // Requirement). The rules are not evaluated again: evaluated is false, and required and missing are empty.
    private static JsonTreeNode SettledApprovals(ToolScope s, StoredRow decision, bool detailed = true)
    {
        var records = s.Query("approval-record", ("decision", decision.Id)).OrderBy(r => r.Text("requestedOn") ?? string.Empty, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
        return Js.Obj(
            ("decisionId", JsonTreeNode.FromString(decision.Id)),
            ("required", Js.EmptyArray),
            ("records", Arr(records.Select(r => ApprovalRecordItem(s, r, r.Flag("countsTowardRequirement"), detailed, detailed ? ApplicationOf(s, decision) : null)))),
            ("missing", Js.EmptyArray),
            ("ready", JsonTreeNode.FromBoolean(decision.Text("statusMeaning") == "accepted")),
            ("settledBy", Js.Str(SettledBy(decision, records.Count))),
            ("evaluated", JsonTreeNode.False));
    }

    private static JsonTreeNode ApprovalRecordItem(ToolScope s, StoredRow r, bool counts, bool detailed, string? applicationId = null)
    {
        var members = new List<(string, JsonTreeNode?)>
        {
            ("approvalRecordId", JsonTreeNode.FromString(r.Id)),
            ("approvalRuleKey", JsonTreeNode.FromString(r.Text("approvalRule") ?? r.Text("decisionRight") ?? "accountable")),
            ("approver", s.PrincipalRef(r.Text("approver"))),
            ("approverActorKind", Js.Str(r.Text("approverActorMeaning") ?? r.Text("approverActorKind"))),
            ("approverPrincipalKind", Js.Str(r.Text("approverPrincipalKind"))),
            ("outcome", s.StatusValue(r, "outcome", "outcomeMeaning") is JsonTreeNode o && o.Kind != JsonTreeKind.Null ? o : ToolScope.StatusValue("pending", null)),
            ("typedReason", Js.Str(r.Text("typedReason"))),
            ("respondedOn", ToolScope.MomentOrNull(r.Text("respondedOn"))),
            ("countsTowardRequirement", JsonTreeNode.FromBoolean(counts)),
            ("refusedBySeparationRuleKey", JsonTreeNode.Null),
        };
        if (detailed)
        {
            members.Add(("askedRole", s.LabelledKey("role", r.Text("requiredRole"))));
            members.Add(("askedOwnerRole", s.LabelledKey("owner-role", r.Text("requiredOwnerRole"))));
            members.Add(("askedPrincipal", s.PrincipalRef(r.Text("askedPrincipal"))));
            members.Add(("isConsultation", JsonTreeNode.FromBoolean(r.Flag("isConsultation"))));
            members.Add(("requestedOn", ToolScope.MomentOrNull(r.Text("requestedOn"))));
            members.Add(("expiresOn", ToolScope.MomentOrNull(r.Text("expiresOn"))));
            (JsonTreeNode canAnswer, JsonTreeNode cannotAnswer) = AnswerCheck(s, r, applicationId);
            members.Add(("canAnswer", canAnswer));
            members.Add(("cannotAnswer", cannotAnswer));
        }

        return Js.Obj(members.ToArray());
    }

    // ---- 54. list_approvals ----

    // view mine: pending requests that ask the person (AskedOfMe), the ones the engine's answer check allows first;
    // answerableCount counts those (wave 4b verify round). asked-by-me: pending requests on decisions the
    // person proposed, or on the decision of an intake or change they requested; open: every pending request; answered:
    // the requests the person answered. Requests on an intake request the person may not read (intake-visibility) are
    // counted in couldNotRead, except in mine: a request that asks the person is theirs to see.
    private static JsonTreeNode ListApprovals(ToolScope s)
    {
        string view = s.Text("view") ?? "mine";
        IReadOnlyList<string> types = s.Strings("decisionTypeKeys");
        string? appFilter = s.Text("businessApplicationId");
        string? decisionFilter = s.Text("decisionId");
        string? portfolioFilter = s.Text("portfolioId");
        HashSet<string>? portfolioApps = PortfolioApplications(s, portfolioFilter, "portfolioId");
        List<StoredRow> source = (view == "answered"
            ? s.Query("approval-record", ("approver", s.PersonId)).Where(r => r.Text("outcomeMeaning") != "pending")
            : s.Query("approval-record", ("outcomeMeaning", "pending"))).ToList();

        // A few store calls, not two per request (QA speed, fix round 1: about 1,000 reads and 2.3 s at 2,000
        // applications, 85 round trips on the demo estate): the decisions, the requests and changes they are about, and
        // the owners of their applications, each in one key set read.
        s.Call.PrefetchIds("decision", source.Select(r => r.Text("decision")));
        List<StoredRow> decisionsRead = source.Select(r => s.Rows.Get("decision", r.Text("decision") ?? string.Empty)).Where(d => d != null).Select(d => d!).ToList();
        s.Call.PrefetchIds("intake-request", decisionsRead.Select(d => d.Text("subjectIntakeRequest")));
        s.Call.PrefetchIds("change-request", decisionsRead.Select(d => d.Text("subjectChangeRequest")));
        s.Call.Prefetch("owner-assignment", "businessApplication", decisionsRead.Select(d => ApplicationOf(s, d)));

        // view mine runs the answer check on every match (wave 4b verify round), which reads each decision's records and its
        // application: a few key set reads for them all, with what the items show.
        if (view == "mine")
        {
            PrefetchDecisionSubjects(s, decisionsRead);
        }

        int hidden = 0;
        var matches = new List<(StoredRow Record, StoredRow Decision, string? App)>();
        foreach (StoredRow record in source)
        {
            StoredRow? decision = s.Rows.Get("decision", record.Text("decision") ?? string.Empty);
            if (decision == null || (decisionFilter != null && !Same(decision.Id, decisionFilter)))
            {
                continue;
            }

            // A request on an intake the caller may not read is left out before any filter looks at it, and counted only
            // for an unfiltered list (as list_intake_requests does). A request that asks the person is theirs to see.
            if (view != "mine" && decision.Text("subjectKind") == "intake-request" && SubjectIdOf(decision) is string hiddenIntakeId && s.Rows.Get("intake-request", hiddenIntakeId) is StoredRow hiddenIntake && !CanReadIntake(s, hiddenIntake))
            {
                if (types.Count == 0 && appFilter == null && decisionFilter == null && portfolioFilter == null)
                {
                    hidden++;
                }

                continue;
            }

            if (types.Count > 0 && !types.Contains(decision.Text("decisionType") ?? string.Empty, StringComparer.Ordinal))
            {
                continue;
            }

            string? app = ApplicationOf(s, decision);
            if (appFilter != null && !Same(app, appFilter))
            {
                continue;
            }

            if (portfolioApps != null && !InPortfolioScope(s, portfolioFilter!, portfolioApps, app, decision.Text("subjectKind") == "intake-request" ? SubjectIdOf(decision) : decision.Text("subjectIntakeRequest")))
            {
                continue;
            }

            bool include = view switch
            {
                "mine" => AskedOfMe(s, record, app),
                "asked-by-me" => AskedByMe(s, decision),
                _ => true,
            };
            if (!include)
            {
                continue;
            }

            matches.Add((record, decision, app));
        }

        s.HideFromCaller("approval-record", hidden);

        // view mine (wave 4b verify round; D-41, document 24, tool 54): the engine's answer check runs on every match, so a request
        // a separation rule keeps the person from answering is still listed (the decision page reads it from here to say
        // why), but after the ones they may answer, and only those count as waiting for their answer (answerableCount and
        // the summary). For an AI client the check is judged for the person it acts for (PersonAnswer). The check runs once
        // a request; the items reuse it.
        var checks = new Dictionary<string, (JsonTreeNode CanAnswer, JsonTreeNode CannotAnswer)>(StringComparer.Ordinal);
        (JsonTreeNode CanAnswer, JsonTreeNode CannotAnswer) Check((StoredRow Record, StoredRow Decision, string? App) m)
        {
            if (!checks.TryGetValue(m.Record.Id, out var found))
            {
                checks[m.Record.Id] = found = AnswerCheck(s, m.Record, m.App);
            }

            return found;
        }

        bool mine = view == "mine";
        var verdicts = new Dictionary<string, (bool May, bool Separation)>(StringComparer.Ordinal);
        (bool May, bool Separation) Verdict((StoredRow Record, StoredRow Decision, string? App) m)
        {
            if (!verdicts.TryGetValue(m.Record.Id, out var found))
            {
                verdicts[m.Record.Id] = found = PersonAnswer(s, m.Record, m.App, s.IsAiCaller ? null : Check(m));
            }

            return found;
        }

        var ordered = (view == "answered"
            ? matches.OrderByDescending(m => m.Record.Text("respondedOn") ?? string.Empty, StringComparer.Ordinal)
            : matches.OrderBy(m => mine && !Verdict(m).May ? 1 : 0)
                .ThenBy(m => m.Record.Text("expiresOn") ?? "~", StringComparer.Ordinal).ThenBy(m => m.Record.Text("requestedOn") ?? "~", StringComparer.Ordinal))
            .ThenBy(m => m.Record.Id, StringComparer.Ordinal).ToList();
        int answerable = mine ? ordered.Count(m => Verdict(m).May) : 0;
        int separated = mine ? ordered.Count(m => !Verdict(m).May && Verdict(m).Separation) : 0;
        int otherwiseBlocked = mine ? ordered.Count - answerable - separated : 0;
        var page = s.Page(ordered, out string? next);
        var pendingByDecision = s.Query("approval-record", ("outcomeMeaning", "pending")).GroupBy(r => r.Text("decision") ?? string.Empty, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        JsonTreeNode items = Arr(page.Select(m =>
        {
            StoredRow r = m.Record;
            StoredRow d = m.Decision;
            (string label, string? appId, string? appName) = DecisionSubjectLabel(s, d);
            int pending = pendingByDecision.TryGetValue(d.Id, out int p) ? p : 0;
            bool asksMe = r.Text("outcomeMeaning") == "pending" && AskedOfMe(s, r, m.App);
            (JsonTreeNode canAnswer, JsonTreeNode cannotAnswer) = Check(m);
            return Js.Obj(
                ("approvalRecordId", JsonTreeNode.FromString(r.Id)),
                ("decision", Js.Obj(
                    ("decisionId", JsonTreeNode.FromString(d.Id)),
                    ("decisionType", s.LabelledKey("decision-type", d.Text("decisionType") ?? "unknown")),
                    ("statement", JsonTreeNode.FromString(d.Text("statement") ?? string.Empty)),
                    ("status", s.StatusValue(d) is JsonTreeNode st && st.Kind != JsonTreeKind.Null ? st : ToolScope.StatusValue("proposed", null)),
                    ("proposedBy", s.PrincipalRef(ProposerOf(s, d))),
                    ("isAiCreated", AiCreated(d)))),
                ("subject", Js.Obj(
                    ("subjectKind", JsonTreeNode.FromString(d.Text("subjectKind") ?? "business-application")),
                    ("subjectId", JsonTreeNode.FromString(SubjectIdOf(s, d) ?? d.Id)),
                    ("label", JsonTreeNode.FromString(label)),
                    ("businessApplicationId", Js.Str(appId)),
                    ("applicationName", Js.Str(appName)))),
                ("approvalRuleKey", JsonTreeNode.FromString(r.Text("approvalRule") ?? r.Text("decisionRight") ?? "accountable")),
                ("isConsultation", JsonTreeNode.FromBoolean(r.Flag("isConsultation"))),
                ("askedRole", s.LabelledKey("role", r.Text("requiredRole"))),
                ("askedOwnerRole", s.LabelledKey("owner-role", r.Text("requiredOwnerRole"))),
                ("askedPrincipal", s.PrincipalRef(r.Text("askedPrincipal"))),
                ("outcome", s.StatusValue(r, "outcome", "outcomeMeaning") is JsonTreeNode o && o.Kind != JsonTreeKind.Null ? o : ToolScope.StatusValue("pending", null)),
                ("approver", s.PrincipalRef(r.Text("approver"))),
                ("typedReason", Js.Str(r.Text("typedReason"))),
                ("countsTowardRequirement", JsonTreeNode.FromBoolean(r.Flag("countsTowardRequirement"))),
                ("requestedOn", ToolScope.MomentOrNull(r.Text("requestedOn"))),
                ("respondedOn", ToolScope.MomentOrNull(r.Text("respondedOn"))),
                ("expiresOn", ToolScope.MomentOrNull(r.Text("expiresOn"))),
                ("otherPendingCount", JsonTreeNode.FromInt64(Math.Max(0, pending - (r.Text("outcomeMeaning") == "pending" ? 1 : 0)))),
                ("isOwnProposal", JsonTreeNode.FromBoolean(Same(d.Text("createdByPrincipal"), s.PersonId))),
                ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("decision", d.Id))),
                ("data", DataDecisionBrief(s, d)),
                ("canAnswer", canAnswer),
                ("cannotAnswer", cannotAnswer),
                ("nextTool", asksMe && !s.IsAiCaller && canAnswer.Kind == JsonTreeKind.True ? JsonTreeNode.FromString("answer_approval") : JsonTreeNode.Null));
        }));
        string blockedText = (separated > 0 ? " " + Count(separated, "more asks", "more ask") + " you, but a separation rule keeps you from answering." : string.Empty)
            + (otherwiseBlocked > 0 ? " " + Count(otherwiseBlocked, "more asks", "more ask") + " you, but cannot be answered now." : string.Empty);
        string summary = view switch
        {
            "mine" => ordered.Count == 0 ? "No approval request waits for you."
                : answerable == 0 ? "No approval request waits for your answer." + blockedText
                : Count(answerable, "approval waits", "approvals wait") + " for your answer." + blockedText + " " + (s.IsAiCaller ? "The person answers on the decision page; an AI client never answers." : "Answer each on its decision page."),
            "asked-by-me" => Count(ordered.Count, "approval is", "approvals are") + " pending on what you proposed or requested.",
            "answered" => Count(ordered.Count, "approval", "approvals") + " you answered.",
            _ => Count(ordered.Count, "approval is", "approvals are") + " pending.",
        };
        return s.Answer(Js.Obj(("items", items), ("totalCount", JsonTreeNode.FromInt64(ordered.Count)), ("answerableCount", mine ? JsonTreeNode.FromInt64(answerable) : JsonTreeNode.Null), ("skipToken", Js.OptStr(next))), summary);
    }

    // The person proposed the decision, or requested the intake or created the change it decides.
    private static bool AskedByMe(ToolScope s, StoredRow decision)
    {
        if (Same(decision.Text("createdByPrincipal"), s.PersonId))
        {
            return true;
        }

        string? id = SubjectIdOf(decision);
        return decision.Text("subjectKind") switch
        {
            "intake-request" => id != null && s.Rows.Get("intake-request", id) is StoredRow intake && (Same(intake.Text("requester"), s.PersonId) || Same(intake.Text("createdByPrincipal"), s.PersonId)),
            "change-request" => id != null && s.Rows.Get("change-request", id) is StoredRow change && Same(change.Text("createdByPrincipal"), s.PersonId),
            _ => false,
        };
    }

    // ---- 55. list_intake_requests ----

    // view triage: submitted requests (the triage queue); mine: requests the person requested or created; drafts: drafts;
    // demand: requests on the table (submitted, in progress or completed), the highest score first; all: every request the
    // person may read. intake-visibility "own" hides other people's requests from anyone but the PMO, the platform admin
    // and the architect; they are counted in couldNotRead. The demand filters (size bands, horizons, priority, scored)
    // and orderBy serve the PMO's demand list (document 24, B24-9).
    private static JsonTreeNode ListIntakeRequests(ToolScope s)
    {
        string view = s.Text("view") ?? "all";
        string? text = s.Text("text")?.ToLowerInvariant();
        IReadOnlyList<string> meanings = s.Strings("statusMeanings");
        IReadOnlyList<string> sizeBands = s.Strings("sizeBandKeys");
        IReadOnlyList<string> horizons = s.Strings("horizonKeys");
        IReadOnlyList<string> priorities = s.Strings("priorityKeys");
        string? portfolio = s.Text("portfolioId");
        if (portfolio != null)
        {
            s.RequireRow("portfolio", portfolio, "portfolioId");
        }

        // Score and Expected Benefit are restricted reads (document 26, section 6.2; security review 2026-11-24): the PMO,
        // the platform admin and the auditor read them on every row, a requester on their own. For anyone else the score
        // filter and the score order do nothing, so neither says anything about a score they may not read.
        bool demandReader = DemandReader(s);
        bool? scored = demandReader ? s.Flag("scored") : null;
        string orderBy = s.Text("orderBy") ?? (view == "demand" ? "score" : "recent");
        if (!demandReader && orderBy == "score")
        {
            orderBy = "recent";
        }

        decimal? Score(StoredRow row) => demandReader ? ToolHandlers.Score(row) : null;

        // A request the caller may not read is never tested against the view, the status filter or the text, so the
        // count of hidden rows says nothing about what they hold. It is counted only for the whole, unfiltered list.
        bool unfiltered = view == "all" && text == null && meanings.Count == 0 && sizeBands.Count == 0 && horizons.Count == 0 && priorities.Count == 0 && scored == null && portfolio == null;
        int hidden = 0;
        var matches = new List<StoredRow>();
        foreach (StoredRow intake in s.Rows.Query(RowQuery.For("intake-request")))
        {
            if (!CanReadIntake(s, intake))
            {
                if (unfiltered)
                {
                    hidden++;
                }

                continue;
            }

            string meaning = intake.Text("statusMeaning") ?? "draft";
            bool mine = Same(intake.Text("requester"), s.PersonId) || Same(intake.Text("createdByPrincipal"), s.PersonId);
            bool inView = view switch
            {
                "triage" => meaning == "submitted",
                "mine" => mine,
                "drafts" => meaning == "draft",
                "demand" => OpenDemandMeanings.Contains(meaning, StringComparer.Ordinal),
                _ => true,
            };
            if (!inView || (meanings.Count > 0 && !meanings.Contains(meaning, StringComparer.Ordinal)))
            {
                continue;
            }

            if (text != null && !((intake.Text("title") ?? string.Empty) + " " + (intake.Text("need") ?? string.Empty) + " " + (intake.Text("recordNumber") ?? string.Empty)).ToLowerInvariant().Contains(text))
            {
                continue;
            }

            if ((sizeBands.Count > 0 && !sizeBands.Contains(intake.Text("sizeBand") ?? "none", StringComparer.Ordinal))
                || (horizons.Count > 0 && !horizons.Contains(intake.Text("horizon") ?? "none", StringComparer.Ordinal))
                || (priorities.Count > 0 && !priorities.Contains(PriorityKey(s, intake), StringComparer.Ordinal))
                || (scored.HasValue && (Score(intake) != null) != scored.Value)
                || (portfolio != null && !PortfolioPath(s, IntakePortfolio(s, intake)).Contains(portfolio, StringComparer.Ordinal)))
            {
                continue;
            }

            matches.Add(intake);
        }

        s.HideFromCaller("intake-request", hidden);
        List<string> sizeOrder = ListOrder(s, "size-band");
        List<string> horizonOrder = ListOrder(s, "horizon");
        List<string> priorityOrder = s.ActiveRows("decision-outcome").Where(r => r.KeyPath.StartsWith("priority/", StringComparison.Ordinal))
            .Select(r => r.KeyPath.Substring("priority/".Length)).Concat(new[] { "proposed", "none" }).ToList();
        int Rank(List<string> order, string? key) => key == null ? int.MaxValue : order.IndexOf(key) is int i && i >= 0 ? i : order.Count;
        IOrderedEnumerable<StoredRow> sorted = orderBy switch
        {
            "score" => matches.OrderBy(i => Score(i) == null ? 1 : 0).ThenByDescending(i => Score(i) ?? 0m),
            "size-band" => matches.OrderBy(i => Rank(sizeOrder, i.Text("sizeBand"))).ThenBy(i => Score(i) == null ? 1 : 0).ThenByDescending(i => Score(i) ?? 0m),
            "horizon" => matches.OrderBy(i => Rank(horizonOrder, i.Text("horizon"))).ThenBy(i => Score(i) == null ? 1 : 0).ThenByDescending(i => Score(i) ?? 0m),
            "priority" => matches.OrderBy(i => Rank(priorityOrder, PriorityKey(s, i))).ThenBy(i => Score(i) == null ? 1 : 0).ThenByDescending(i => Score(i) ?? 0m),
            "title" => matches.OrderBy(i => i.Text("title") ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            _ => matches.OrderByDescending(i => i.Text("engineCheckedOn") ?? string.Empty, StringComparer.Ordinal),
        };
        var ordered = sorted.ThenBy(i => i.Text("title") ?? string.Empty, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Id, StringComparer.Ordinal).ToList();
        List<StoredRow> page = s.Page(ordered, out string? next);
        JsonTreeNode items = Arr(page.Select(i => Js.Obj(IntakeSummary(s, i).ToArray())));
        string summary = Count(ordered.Count, "intake request", "intake requests") + " " + (view == "all" ? "you may read." : "in the " + view + " view.") + (hidden > 0 ? " " + Count(hidden, "other request is", "other requests are") + " not shown to you (intake-visibility)." : string.Empty);
        return s.Answer(Js.Obj(("items", items), ("totalCount", JsonTreeNode.FromInt64(ordered.Count)), ("skipToken", Js.OptStr(next))), summary);
    }

    // The priority a filter and the order read: the accepted priority decision's outcome key, "proposed" while the latest
    // priority decision waits, or "none".
    private static string PriorityKey(ToolScope s, StoredRow intake)
    {
        StoredRow? decision = PriorityDecision(s, intake);
        return decision == null ? "none"
            : decision.Text("statusMeaning") == "accepted" && decision.Text("outcome") is string outcome ? outcome
            : decision.Text("statusMeaning") == "proposed" ? "proposed"
            : "none";
    }

    // The order of a value list's values: as the list names them in the configuration in force, else by Sort Order, else
    // by key.
    private static List<string> ListOrder(ToolScope s, string list)
    {
        // The version's content keeps each value list with its values in the order the configuration lists them.
        JsonTreeNode? lists = Js.Member(Js.Member(s.Call.Snapshot.ContentJson, "entities"), "value-list");
        JsonTreeNode? row = lists == null || lists.Kind != JsonTreeKind.Array ? null : lists.Items.FirstOrDefault(l => Js.Text(l, "key") == list);
        JsonTreeNode? values = Js.Member(row, "values");
        if (values != null && values.Kind == JsonTreeKind.Array && values.Items.Count > 0)
        {
            return values.Items.Select(v => Js.Text(v, "key")).Where(k => k != null).Select(k => k!).ToList();
        }

        string prefix = list + "/";
        return s.ActiveRows("list-value").Where(r => r.KeyPath.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(r => Js.Number(r.Columns, "sortOrder") ?? long.MaxValue).ThenBy(r => r.KeyPath, StringComparer.Ordinal)
            .Select(r => r.KeyPath.Substring(prefix.Length)).ToList();
    }

    // The request's priority (document 23, P01 step 8): its latest priority decision that is not withdrawn, with its
    // outcome and status, or null when none was proposed. The outcome counts as the priority only once it is accepted;
    // a proposed one is shown as proposed.
    private static JsonTreeNode PriorityOf(ToolScope s, StoredRow intake)
    {
        StoredRow? decision = PriorityDecision(s, intake);
        if (decision == null)
        {
            return JsonTreeNode.Null;
        }

        return Js.Obj(
            ("decisionId", JsonTreeNode.FromString(decision.Id)),
            ("outcome", DecisionOutcome(s, decision)),
            ("status", s.StatusValue(decision) is JsonTreeNode st && st.Kind != JsonTreeKind.Null ? st : ToolScope.StatusValue("proposed", null)),
            ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("decision", decision.Id))));
    }

    private static StoredRow? PriorityDecision(ToolScope s, StoredRow intake)
    {
        return NewestFirst(s.Query("decision", ("subjectIntakeRequest", intake.Id), ("decisionType", "priority")))
            .FirstOrDefault(d => d.Text("statusMeaning") != "withdrawn");
    }

    private static decimal? Score(StoredRow intake)
    {
        JsonTreeNode? score = intake.Column("score");
        return score != null && score.Kind == JsonTreeKind.Number && score.TryGetDecimal(out decimal value) ? value : null;
    }

    // The PMO, the platform admin and the auditor read Score and Expected Benefit on every request (document 26, section
    // 6.2).
    private static bool DemandReader(ToolScope s)
    {
        return s.HoldsRole("pmo") || s.HoldsRole("platform-admin") || s.HoldsRole("auditor");
    }

    // A demand reader, or the request's own requester.
    private static bool CanReadDemandFields(ToolScope s, StoredRow intake)
    {
        return DemandReader(s) || Same(intake.Text("requester"), s.PersonId);
    }

    // The portfolio a request is in (document 24, B24-17): its own Portfolio (O07), else the portfolio of the application
    // it is linked to. The portfolio filter matches that portfolio and its sub-portfolios, as list_reviews does.
    private static string? IntakePortfolio(ToolScope s, StoredRow intake)
    {
        return intake.Text("portfolio") ?? (intake.Text("businessApplication") is string app ? s.Rows.Get("business-application", app)?.Text("portfolio") : null);
    }

    // The fields an intake list item and the intake page share.
    private static List<(string, JsonTreeNode?)> IntakeSummary(ToolScope s, StoredRow intake)
    {
        string? appId = intake.Text("businessApplication");
        StoredRow? app = appId == null ? null : s.Rows.Get("business-application", appId);
        StoredRow? assessment = NewestFirst(s.Query("tier-assessment", ("intakeRequest", intake.Id))).FirstOrDefault();
        string? triageId = intake.Text("triageDecision");
        StoredRow? triage = triageId == null ? null : s.Rows.Get("decision", triageId);
        decimal? score = CanReadDemandFields(s, intake) ? Score(intake) : null;
        JsonTreeNode? usesAi = intake.Column("usesAi");
        string? portfolioId = IntakePortfolio(s, intake);
        StoredRow? portfolioRow = portfolioId == null ? null : s.Rows.Get("portfolio", portfolioId);
        return new List<(string, JsonTreeNode?)>
        {
            ("intakeRequestId", JsonTreeNode.FromString(intake.Id)),
            ("recordNumber", Js.Str(intake.Text("recordNumber"))),
            ("title", JsonTreeNode.FromString(intake.Text("title") ?? string.Empty)),
            ("status", s.StatusValue(intake) is JsonTreeNode st && st.Kind != JsonTreeKind.Null ? st : ToolScope.StatusValue("draft", null)),
            ("requester", s.PrincipalRef(intake.Text("requester"))),
            ("proposedKind", s.LabelledKey("application-kind", intake.Text("proposedKind"))),
            ("businessApplicationId", Js.Str(appId)),
            ("applicationName", app == null ? JsonTreeNode.Null : JsonTreeNode.FromString(AppName(app))),
            ("duplicateOfId", Js.Str(intake.Text("duplicateOf"))),
            ("usesAi", usesAi != null && (usesAi.Kind == JsonTreeKind.True || usesAi.Kind == JsonTreeKind.False) ? usesAi : JsonTreeNode.Null),
            ("horizon", ListValue(s, "horizon", intake.Text("horizon"))),
            ("sizeBand", ListValue(s, "size-band", intake.Text("sizeBand"))),
            ("score", score.HasValue ? JsonTreeNode.FromDecimal(score.Value) : JsonTreeNode.Null),
            ("priority", PriorityOf(s, intake)),
            ("businessOwner", s.PrincipalRef(intake.Text("businessOwner"))),
            ("portfolio", portfolioRow == null ? JsonTreeNode.Null : Js.Obj(
                ("portfolioId", JsonTreeNode.FromString(portfolioRow.Id)),
                ("name", JsonTreeNode.FromString(portfolioRow.Text("name") ?? portfolioRow.Id)),
                ("fromApplication", JsonTreeNode.FromBoolean(intake.Text("portfolio") == null)))),
            ("suggestedTier", s.LabelledKey("tier", assessment?.Text("computedTier"))),
            ("triageDecision", triage == null ? JsonTreeNode.Null : DecisionBrief(s, triage)),
            ("decisions", Arr(NewestFirst(s.Query("decision", ("subjectIntakeRequest", intake.Id))).Take(5).Select(d => DecisionBrief(s, d)))),
            ("isAiCreated", AiCreated(intake)),
            ("performedBy", Stamp(s, intake)),
            ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("intake-request", intake.Id))),
        };
    }

    // ---- 56. get_intake_request ----

    private static JsonTreeNode GetIntakeRequest(ToolScope s)
    {
        StoredRow intake = s.RequireRow("intake-request", s.Text("intakeRequestId"), "intakeRequestId");
        if (!CanReadIntake(s, intake))
        {
            throw s.NotFound("Intake Request", intake.Id, "intakeRequestId");
        }

        string? duplicateId = intake.Text("duplicateOf");
        StoredRow? duplicate = duplicateId == null ? null : s.Rows.Get("business-application", duplicateId);
        List<(string, JsonTreeNode?)> fields = IntakeSummary(s, intake);
        fields.Add(("need", JsonTreeNode.FromString(intake.Text("need") ?? string.Empty)));
        fields.Add(("expectedBenefit", Js.Str(CanReadDemandFields(s, intake) ? intake.Text("expectedBenefit") : null)));
        fields.Add(("duplicateOfName", duplicate == null ? JsonTreeNode.Null : JsonTreeNode.FromString(AppName(duplicate))));
        string? triageId = intake.Text("triageDecision");
        JsonTreeNode assessments = Arr(NewestFirst(s.Query("tier-assessment", ("intakeRequest", intake.Id))).Select(a => Js.Obj(
            ("tierAssessmentId", JsonTreeNode.FromString(a.Id)),
            ("computedTier", s.LabelledKey("tier", a.Text("computedTier"))),
            ("assessedBy", s.PrincipalRef(a.Text("assessedBy"))),
            ("tierDecisionId", Js.Str(a.Text("tierDecision"))),
            ("isAiCreated", AiCreated(a)),
            ("performedBy", Stamp(s, a)))));
        JsonTreeNode decisions = Arr(NewestFirst(s.Query("decision", ("subjectIntakeRequest", intake.Id))).Select(d => Js.Obj(
            ("decisionId", JsonTreeNode.FromString(d.Id)),
            ("decisionType", s.LabelledKey("decision-type", d.Text("decisionType") ?? "unknown")),
            ("statement", JsonTreeNode.FromString(d.Text("statement") ?? string.Empty)),
            ("outcome", DecisionOutcome(s, d)),
            ("status", s.StatusValue(d) is JsonTreeNode st && st.Kind != JsonTreeKind.Null ? st : ToolScope.StatusValue("proposed", null)),
            ("decider", s.PrincipalRef(d.Text("decider"))),
            ("decidedOn", ToolScope.MomentOrNull(d.Text("decidedOn"))),
            ("approvalsSummary", ApprovalsSummary(s, d.Id)),
            ("isTriage", JsonTreeNode.FromBoolean(Same(d.Id, triageId))),
            ("isAiCreated", AiCreated(d)),
            ("performedBy", Stamp(s, d)),
            ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("decision", d.Id))))));
        JsonTreeNode links = Arr(s.Query("delivery-link", ("subjectIntakeRequest", intake.Id)).Select(l => (Category: "delivery", Row: l))
            .Concat(s.Query("evidence-link", ("subjectIntakeRequest", intake.Id)).Select(l => (Category: "evidence", Row: l)))
            .Where(l => (l.Row.Text("url") ?? string.Empty).StartsWith("https://", StringComparison.Ordinal))
            .OrderBy(l => l.Row.Id, StringComparer.Ordinal)
            .Select(l => Js.Obj(
                ("linkId", JsonTreeNode.FromString(l.Row.Id)),
                ("linkCategory", JsonTreeNode.FromString(l.Category)),
                ("linkKind", s.LabelledKey("link-kind", l.Row.Text("linkKind") ?? "unknown")),
                ("url", JsonTreeNode.FromString(l.Row.Text("url")!)),
                ("label", JsonTreeNode.FromString(l.Row.Text("label") ?? string.Empty)))));
        JsonTreeNode data = Js.Obj(("intake", Js.Obj(fields.ToArray())), ("tierAssessments", assessments), ("decisions", decisions), ("links", links));
        var next = new List<NextStep>();
        if (triageId != null)
        {
            next.Add(ToolScope.Next("get_decision", ("decisionId", JsonTreeNode.FromString(triageId))));
        }

        return s.Answer(data, "The intake request " + (intake.Text("title") ?? intake.Id) + " is " + (intake.Text("statusMeaning") ?? "draft") + ".", s.RecordUrl("intake-request", intake.Id), next.ToArray());
    }

    // ---- 57. list_reviews ----

    // state overdue: due with a due date before today; due: every due review (dueWithinDays narrows it to those due within
    // that many days, overdue included); done; cancelled; all. mineOnly keeps the reviews that are the person's to do, as
    // get_my_queue decides (ReviewIsMine). counts is for the same filters across the states.
    private static JsonTreeNode ListReviews(ToolScope s)
    {
        string state = s.Text("state") ?? "due";
        long? within = Js.Number(s.Args, "dueWithinDays");
        IReadOnlyList<string> types = s.Strings("reviewTypeKeys");
        string? appFilter = s.Text("businessApplicationId");
        string? portfolio = s.Text("portfolioId");
        if (portfolio != null)
        {
            s.RequireRow("portfolio", portfolio, "portfolioId");
        }

        bool mineOnly = s.Flag("mineOnly") ?? false;
        var cache = new Dictionary<string, string?>(StringComparer.Ordinal);
        var filtered = new List<(StoredRow Review, StoredRow? App, bool Mine, bool Overdue)>();
        foreach (StoredRow review in s.Rows.Query(RowQuery.For("review")))
        {
            string? appId = review.Text("businessApplication");
            StoredRow? app = appId == null ? null : s.Rows.Get("business-application", appId);
            if ((types.Count > 0 && !types.Contains(review.Text("reviewType") ?? string.Empty, StringComparer.Ordinal))
                || (appFilter != null && !Same(appId, appFilter))
                || (portfolio != null && !PortfolioPath(s, app?.Text("portfolio")).Contains(portfolio, StringComparer.Ordinal)))
            {
                continue;
            }

            bool mine = ReviewIsMine(s, review, app, cache);
            if (mineOnly && !mine)
            {
                continue;
            }

            bool overdue = review.Text("statusMeaning") == "due" && PastDue(s, review.Text("dueOn"));
            filtered.Add((review, app, mine, overdue));
        }

        var inState = filtered.Where(r =>
        {
            string meaning = r.Review.Text("statusMeaning") ?? "due";
            return state switch
            {
                "overdue" => r.Overdue,
                "due" => meaning == "due" && (!within.HasValue || (Js.ParseMoment(r.Review.Text("dueOn")) ?? DateTime.MaxValue).Date <= s.Now.Date.AddDays(within.Value)),
                "done" => meaning == "done",
                "cancelled" => meaning == "cancelled",
                _ => true,
            };
        });
        var ordered = (state == "done"
            ? inState.OrderByDescending(r => r.Review.Text("doneOn") ?? string.Empty, StringComparer.Ordinal)
            : inState.OrderBy(r => r.Review.Text("dueOn") ?? "~", StringComparer.Ordinal))
            .ThenBy(r => r.App == null ? string.Empty : AppName(r.App), StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Review.Id, StringComparer.Ordinal).ToList();
        var page = s.Page(ordered, out string? next);
        JsonTreeNode items = Arr(page.Select(r => ReviewItemJson(s, r.Review, r.App, r.Mine, r.Overdue)));
        JsonTreeNode counts = Js.Obj(
            ("overdue", JsonTreeNode.FromInt64(filtered.Count(r => r.Overdue))),
            ("due", JsonTreeNode.FromInt64(filtered.Count(r => r.Review.Text("statusMeaning") == "due"))),
            ("done", JsonTreeNode.FromInt64(filtered.Count(r => r.Review.Text("statusMeaning") == "done"))));
        return s.Answer(Js.Obj(("items", items), ("counts", counts), ("totalCount", JsonTreeNode.FromInt64(ordered.Count)), ("skipToken", Js.OptStr(next))),
            Count(ordered.Count, "review matches", "reviews match") + "; " + filtered.Count(r => r.Overdue) + " overdue in all.");
    }

    // One review as list_reviews and get_review give it.
    private static JsonTreeNode ReviewItemJson(ToolScope s, StoredRow review, StoredRow? app, bool mine, bool overdue)
    {
        SnapshotRow? type = s.ConfigRow("review-type", review.Text("reviewType"));
        string? leadRole = Js.Text(type?.Columns, "defaultOwnerRoleKey");
        StoredRow? portfolioRow = app?.Text("portfolio") is string p ? s.Rows.Get("portfolio", p) : null;
        return Js.Obj(
            ("reviewId", JsonTreeNode.FromString(review.Id)),
            ("recordNumber", Js.Str(review.Text("recordNumber"))),
            ("reviewType", s.LabelledKey("review-type", review.Text("reviewType") ?? "unknown")),
            ("businessApplicationId", Js.Str(review.Text("businessApplication"))),
            ("applicationName", app == null ? JsonTreeNode.Null : JsonTreeNode.FromString(AppName(app))),
            ("portfolio", portfolioRow == null ? JsonTreeNode.Null : Js.Obj(("portfolioId", JsonTreeNode.FromString(portfolioRow.Id)), ("name", JsonTreeNode.FromString(portfolioRow.Text("name") ?? portfolioRow.Id)))),
            ("status", s.StatusValue(review) is JsonTreeNode st && st.Kind != JsonTreeKind.Null ? st : ToolScope.StatusValue("due", null)),
            ("trigger", Js.Str(review.Text("trigger"))),
            ("dueOn", ToolScope.DateOrNull(review.Text("dueOn"))),
            ("doneOn", ToolScope.DateOrNull(review.Text("doneOn"))),
            ("nextDueOn", ToolScope.DateOrNull(review.Text("nextDueOn"))),
            ("isOverdue", JsonTreeNode.FromBoolean(overdue)),
            ("reviewer", s.PrincipalRef(review.Text("reviewer"))),
            ("leadOwnerRole", s.LabelledKey("owner-role", leadRole)),
            ("leadOwner", leadRole == null || app == null ? JsonTreeNode.Null : OwnerRef(s, s.CurrentOwners(app.Id), leadRole)),
            ("producesDecisionType", s.LabelledKey("decision-type", Js.Text(type?.Columns, "producesDecisionTypeKey"))),
            ("outcomeDecisionId", Js.Str(review.Text("outcomeDecision"))),
            ("isMine", JsonTreeNode.FromBoolean(mine)),
            ("isAiCreated", AiCreated(review)),
            ("performedBy", Stamp(s, review)),
            ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("review", review.Id))));
    }

    // ---- 63. get_review ----

    // One review for its record page (document 24, B24-10): the review as list_reviews gives it, with its description,
    // the evidence linked to it (O20), its outcome decision, the cadence that sets its next due date, the gates it feeds
    // and whether each is met, and whether the person may complete it now. Any role reads it.
    private static JsonTreeNode GetReview(ToolScope s)
    {
        StoredRow review = s.RequireRow("review", s.Text("reviewId"), "reviewId");
        StoredRow? app = review.Text("businessApplication") is string appId ? s.Rows.Get("business-application", appId) : null;
        string? role = ReviewRoleOf(s, review, app, new Dictionary<string, string?>(StringComparer.Ordinal));
        string meaning = review.Text("statusMeaning") ?? "due";
        bool overdue = meaning == "due" && PastDue(s, review.Text("dueOn"));
        StoredRow? outcome = review.Text("outcomeDecision") is string outcomeId ? s.Rows.Get("decision", outcomeId) : null;
        SnapshotRow? cadence = ReviewCadence(s, review, app);
        long? interval = Js.Number(cadence?.Columns, "intervalDays");

        // Whether complete_review would take the person's call now, and if not, why (the same checks, read only).
        string? cannot = meaning != "due" ? "The review is " + meaning + "."
            : role == null ? "This review is not yours to complete."
            : s.IsAiCaller ? "A person completes a review on its page in the web app; an AI client never marks a review done."
            : outcome == null ? "The review has no outcome decision yet. Propose it, submit it, and complete the review once it is accepted."
            : outcome.Text("statusMeaning") != "accepted" ? "The outcome decision is " + (outcome.Text("statusMeaning") ?? "not decided") + ". The review is completed once it is accepted."
            : null;
        var evidence = s.Query("evidence-link", ("subjectReview", review.Id))
            .OrderBy(e => e.Text("capturedOn") ?? string.Empty, StringComparer.Ordinal).ThenBy(e => e.Id, StringComparer.Ordinal)
            .Select(e => Js.Obj(
                ("evidenceLinkId", JsonTreeNode.FromString(e.Id)),
                ("linkKind", s.LabelledKey("link-kind", e.Text("linkKind") ?? "unknown")),
                ("url", (e.Text("url") ?? string.Empty).StartsWith("https://", StringComparison.Ordinal) ? JsonTreeNode.FromString(e.Text("url")!) : JsonTreeNode.Null),
                ("label", JsonTreeNode.FromString(e.Text("label") ?? string.Empty)),
                ("evidenceLevel", ListValue(s, "evidence-level", e.Text("evidenceLevel"))),
                ("capturedOn", ToolScope.MomentOrNull(e.Text("capturedOn"))),
                ("isAiCreated", AiCreated(e)),
                ("performedBy", Stamp(s, e))));
        JsonTreeNode data = Js.Obj(
            ("review", Js.With(ReviewItemJson(s, review, app, role != null, overdue), ("description", Js.Str(review.Text("description"))))),
            ("yourPart", Js.Str(role)),
            ("evidence", Arr(evidence)),
            ("outcomeDecision", outcome == null ? JsonTreeNode.Null : Js.Obj(
                ("decisionId", JsonTreeNode.FromString(outcome.Id)),
                ("recordNumber", Js.Str(outcome.Text("recordNumber"))),
                ("statement", JsonTreeNode.FromString(outcome.Text("statement") ?? string.Empty)),
                ("decisionType", s.LabelledKey("decision-type", outcome.Text("decisionType") ?? "unknown")),
                ("status", s.StatusValue(outcome)),
                ("decidedOn", ToolScope.DateOrNull(outcome.Text("decidedOn"))))),
            ("cadence", cadence == null ? JsonTreeNode.Null : Js.Obj(
                ("cadenceKey", JsonTreeNode.FromString(cadence.KeyPath)),
                ("intervalDays", JsonTreeNode.FromInt64(interval ?? 0)),
                ("leadDays", JsonTreeNode.FromInt64(Js.Number(cadence.Columns, "leadDays") ?? 0)))),
            ("nextDueIfDoneToday", interval.HasValue && meaning == "due" ? Js.Date(s.Now.Date.AddDays(interval.Value)) : JsonTreeNode.Null),
            ("gates", Arr(ReviewGates(s, review, app))),
            ("canComplete", JsonTreeNode.FromBoolean(cannot == null)),
            ("cannotComplete", Js.Str(cannot)));
        var next = new List<NextStep>();
        if (cannot == null)
        {
            next.Add(ToolScope.Next("complete_review", ("reviewId", JsonTreeNode.FromString(review.Id))));
        }
        else if (outcome != null)
        {
            next.Add(ToolScope.Next("get_decision", ("decisionId", JsonTreeNode.FromString(outcome.Id))));
        }

        return s.Answer(data, "The " + s.Label("review-type", review.Text("reviewType")).ToLowerInvariant() + (app == null ? string.Empty : " of " + AppName(app)) + " is " + meaning + ".", s.RecordUrl("review", review.Id), next.ToArray());
    }

    // ---- 58. list_portfolios ----

    private static JsonTreeNode ListPortfolios(ToolScope s)
    {
        string? parent = s.Text("parentPortfolioId");
        if (parent != null)
        {
            s.RequireRow("portfolio", parent, "parentPortfolioId");
        }

        bool includeInactive = s.Flag("includeInactive") ?? false;
        var portfolios = s.Rows.Query(RowQuery.For("portfolio"))
            .Where(p => includeInactive || !(p.Column("isActive") is JsonTreeNode active && active.Kind == JsonTreeKind.False))
            .Where(p => parent == null || Same(p.Text("parentPortfolio"), parent))
            .Select(p => (Row: p, Path: PortfolioPath(s, p.Id).AsEnumerable().Reverse().ToList()))
            .OrderBy(p => string.Join("/", p.Path.Select(id => s.Rows.Get("portfolio", id)?.Text("name") ?? id)), StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Row.Id, StringComparer.Ordinal)
            .ToList();
        var page = s.Page(portfolios, out string? next);
        var apps = s.Rows.Query(RowQuery.For("business-application")).Where(a => a.Text("statusMeaning") != "archived").ToList();
        var appPaths = apps.ToDictionary(a => a.Id, a => PortfolioPath(s, a.Text("portfolio")), StringComparer.Ordinal);
        var proposed = s.Query("decision", ("statusMeaning", "proposed"));
        var dueReviews = s.Query("review", ("statusMeaning", "due"));
        JsonTreeNode items = Arr(page.Select(p =>
        {
            var inside = apps.Where(a => appPaths[a.Id].Contains(p.Row.Id, StringComparer.Ordinal)).ToList();
            var ids = new HashSet<string>(inside.Select(a => a.Id), StringComparer.Ordinal);
            List<OwnerGapItem> gaps = OwnerGapsOf(s, inside);
            JsonTreeNode? isActive = p.Row.Column("isActive");
            return Js.Obj(
                ("portfolioId", JsonTreeNode.FromString(p.Row.Id)),
                ("name", JsonTreeNode.FromString(p.Row.Text("name") ?? p.Row.Id)),
                ("level", ListValue(s, "portfolio-level", p.Row.Text("level"))),
                ("parentPortfolioId", Js.Str(p.Row.Text("parentPortfolio"))),
                ("path", Arr(p.Path.Select(id => Js.Obj(("portfolioId", JsonTreeNode.FromString(id)), ("name", JsonTreeNode.FromString(s.Rows.Get("portfolio", id)?.Text("name") ?? id)))))),
                ("accountablePrincipal", s.PrincipalRef(p.Row.Text("accountablePrincipal"))),
                ("isActive", JsonTreeNode.FromBoolean(!(isActive != null && isActive.Kind == JsonTreeKind.False))),
                ("counts", Js.Obj(
                    ("applications", JsonTreeNode.FromInt64(inside.Count)),
                    ("applicationsWithOwnerGap", JsonTreeNode.FromInt64(gaps.Select(g => g.App.Id).Distinct(StringComparer.Ordinal).Count())),
                    ("ownerGaps", JsonTreeNode.FromInt64(gaps.Count)),
                    ("reviewsOverdue", JsonTreeNode.FromInt64(dueReviews.Count(r => ids.Contains(r.Text("businessApplication") ?? string.Empty) && PastDue(s, r.Text("dueOn"))))),
                    ("openDecisions", JsonTreeNode.FromInt64(proposed.Count(d => ids.Contains(d.Text("subjectApplication") ?? string.Empty)))))),
                ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("portfolio", p.Row.Id))));
        }));
        return s.Answer(Js.Obj(("items", items), ("totalCount", JsonTreeNode.FromInt64(portfolios.Count)), ("skipToken", Js.OptStr(next))),
            Count(portfolios.Count, "portfolio", "portfolios") + ". Counts include the applications of child portfolios. A person changes a portfolio with save_portfolio in the web app.");
    }

    // ---- 59. list_owner_assignments ----

    // Owner assignments across the portfolio, ended ones too (state), by person, owner role, application or portfolio.
    // leaversOnly keeps holders with a leave date, or whom the last owner check found disabled or deleted in Entra (O05).
    // coverage counts, per owner role, the applications in scope (registered, by the application and portfolio filters)
    // where the role has a current holder, a deputy, or a gap.
    private static JsonTreeNode ListOwnerAssignments(ToolScope s)
    {
        string? person = s.Text("principalObjectId");
        IReadOnlyList<string> roles = s.Strings("ownerRoleKeys");
        foreach (string role in roles)
        {
            s.RequireKey("owner-role", role, "ownerRoleKeys");
        }

        string? appFilter = s.Text("businessApplicationId");
        if (appFilter != null)
        {
            RequireApplication(s);
        }

        string? portfolio = s.Text("portfolioId");
        if (portfolio != null)
        {
            s.RequireRow("portfolio", portfolio, "portfolioId");
        }

        string state = s.Text("state") ?? "current";
        bool leaversOnly = s.Flag("leaversOnly") ?? false;

        // A leave date is personal (HR) data. Who is leaving, and when, is the owner check's question, which only a
        // platform admin asks (run_owner_check). Anyone else sees their own leave date and never anyone else's.
        bool mayReadLeave = s.HoldsRole("platform-admin");
        if (leaversOnly && !mayReadLeave)
        {
            throw s.Refused(McpErrorCodes.NotAuthorized, "Only a platform admin lists the people who are leaving. Owners see the owner gaps; a leave shows there once a successor is needed.");
        }

        bool ShowsLeave(StoredRow? holder) => holder != null && (mayReadLeave || Same(holder.Text("entraObjectId") ?? holder.Id, s.PersonId));
        DateTime today = s.Now.Date;
        var scopeApps = s.Rows.Query(RowQuery.For("business-application"))
            .Where(a => a.Text("statusMeaning") == "registered")
            .Where(a => appFilter == null || Same(a.Id, appFilter))
            .Where(a => portfolio == null || PortfolioPath(s, a.Text("portfolio")).Contains(portfolio, StringComparer.Ordinal))
            .ToDictionary(a => a.Id, a => a, StringComparer.Ordinal);
        // A few store calls, not one per application and one per request (QA speed, fix round 1: 2,523 reads and 11 s at
        // 2,000 applications): the owners of the applications in scope, the decisions of the pending requests, and the
        // people behind the assignments, each in one key set read.
        s.Call.Prefetch("owner-assignment", "businessApplication", scopeApps.Keys);
        IReadOnlyList<StoredRow> pendingRecords = s.Query("approval-record", ("outcomeMeaning", "pending"));
        s.Call.PrefetchIds("decision", pendingRecords.Where(r => r.Text("requiredOwnerRole") != null).Select(r => r.Text("decision")));
        var pending = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (StoredRow record in pendingRecords)
        {
            if (record.Text("requiredOwnerRole") is string ownerRole && s.Rows.Get("decision", record.Text("decision") ?? string.Empty) is StoredRow decision && ApplicationOf(s, decision) is string app)
            {
                string key = app + "|" + ownerRole;
                pending[key] = pending.TryGetValue(key, out int n) ? n + 1 : 1;
            }
        }

        string StateOf(StoredRow o)
        {
            DateTime? start = Js.ParseMoment(o.Text("startOn"));
            DateTime? end = Js.ParseMoment(o.Text("endOn"));
            if (end.HasValue && end.Value.Date <= today)
            {
                return "ended";
            }

            return start.HasValue && start.Value.Date <= today ? "current" : "upcoming";
        }

        bool Leaving(StoredRow? principal)
        {
            return principal != null && (principal.Text("leavesOn") != null || principal.Text("directoryState") == "disabled" || principal.Text("directoryState") == "deleted");
        }

        var matches = new List<(StoredRow Row, StoredRow App, string State, StoredRow? Holder)>();
        IReadOnlyList<StoredRow> source = person != null ? s.Query("owner-assignment", ("principal", person)) : s.Rows.Query(RowQuery.For("owner-assignment"));
        s.Call.Prefetch("principal-reference", "entraObjectId", source.Select(o => o.Text("principal")));

        // The items name other people too (deputies, the people a pending change names): all of them in one read.
        s.Call.PrefetchAll("principal-reference");
        foreach (StoredRow o in source)
        {
            if (!scopeApps.TryGetValue(o.Text("businessApplication") ?? string.Empty, out StoredRow? app)
                || (person != null && !Same(o.Text("principal"), person))
                || (roles.Count > 0 && !roles.Contains(o.Text("ownerRole") ?? string.Empty, StringComparer.Ordinal)))
            {
                continue;
            }

            string assignmentState = StateOf(o);
            if (state != "all" && assignmentState != state)
            {
                continue;
            }

            StoredRow? holder = s.Principal(o.Text("principal"));
            if (leaversOnly && !Leaving(holder))
            {
                continue;
            }

            matches.Add((o, app, assignmentState, holder));
        }

        var ordered = matches.OrderBy(m => AppName(m.App), StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Row.Text("ownerRole"), StringComparer.Ordinal)
            .ThenBy(m => m.Row.Flag("isDeputy")).ThenBy(m => m.Row.Text("startOn") ?? string.Empty, StringComparer.Ordinal).ThenBy(m => m.Row.Id, StringComparer.Ordinal).ToList();
        var page = s.Page(ordered, out string? next);
        JsonTreeNode items = Arr(page.Select(m => Js.With(OwnerAssignment(s, m.Row),
            ("businessApplicationId", JsonTreeNode.FromString(m.App.Id)),
            ("applicationName", JsonTreeNode.FromString(AppName(m.App))),
            ("state", JsonTreeNode.FromString(m.State)),
            ("holder", Js.Obj(
                ("directoryState", Js.Str(m.Holder?.Text("directoryState"))),
                ("directoryStateOn", ShowsLeave(m.Holder) ? ToolScope.MomentOrNull(m.Holder?.Text("directoryStateOn")) : JsonTreeNode.Null),
                ("leavesOn", ShowsLeave(m.Holder) ? ToolScope.DateOrNull(m.Holder?.Text("leavesOn")) : JsonTreeNode.Null))),
            ("pendingApprovalCount", JsonTreeNode.FromInt64(pending.TryGetValue(m.App.Id + "|" + m.Row.Text("ownerRole"), out int n) ? n : 0)),
            ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("business-application", m.App.Id))))));

        var current = scopeApps.Values.ToDictionary(a => a.Id, a => s.CurrentOwners(a.Id), StringComparer.Ordinal);
        // The one owner gap count (OwnerGapsOf, document 24, B24-17), so this page and the Overview and Portfolios agree.
        List<OwnerGapItem> gapItems = OwnerGapsOf(s, scopeApps.Values);
        // Coverage is per application, so it lists the owner roles an application can have (C28 Subject Kinds, D-37).
        JsonTreeNode coverage = Arr(s.ActiveRows("owner-role").Where(HeldOnApplications).Where(r => roles.Count == 0 || roles.Contains(r.KeyPath, StringComparer.Ordinal)).OrderBy(r => r.KeyPath, StringComparer.Ordinal).Select(r => Js.Obj(
            ("ownerRoleKey", JsonTreeNode.FromString(r.KeyPath)),
            ("label", JsonTreeNode.FromString(s.Label("owner-role", r.KeyPath))),
            ("requiresDeputy", JsonTreeNode.FromBoolean(Js.Flag(r.Columns, "requiresDeputy") == true)),
            ("applicationsHeld", JsonTreeNode.FromInt64(current.Count(c => c.Value.Any(o => o.Text("ownerRole") == r.KeyPath && !o.Flag("isDeputy"))))),
            ("applicationsWithDeputy", JsonTreeNode.FromInt64(current.Count(c => c.Value.Any(o => o.Text("ownerRole") == r.KeyPath && o.Flag("isDeputy"))))),
            ("gaps", JsonTreeNode.FromInt64(gapItems.Count(x => x.Role == r.KeyPath))))));
        int ownerGaps = gapItems.Count(g => roles.Count == 0 || roles.Contains(g.Role, StringComparer.Ordinal));
        return s.Answer(Js.Obj(("items", items), ("coverage", coverage), ("applicationsInScope", JsonTreeNode.FromInt64(scopeApps.Count)), ("ownerGaps", JsonTreeNode.FromInt64(ownerGaps)), ("totalCount", JsonTreeNode.FromInt64(ordered.Count)), ("skipToken", Js.OptStr(next))),
            Count(ordered.Count, "owner assignment matches", "owner assignments match") + " across " + Count(scopeApps.Count, "registered application", "registered applications") + ".",
            null,
            ToolScope.Next("get_portfolio_summary", ("includeOwnerGaps", JsonTreeNode.True)));
    }
}
