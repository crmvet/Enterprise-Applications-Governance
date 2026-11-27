using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Json;

namespace Product.Mcp.Core.Backend.InProcess;

// Tools 87 and 88 (document 24, B24-18; the Configuration screen): the versions of the configuration in force with who
// accepted each and when, the drafts with their state, and one configuration draft as its record page shows it, with the
// diff, the rule errors the engine found, and the applications and gates it touches. Both are reads for any role:
// configuration rows, C02 versions and C46 drafts are read by all roles (document 26, section 5.3). Nothing here writes;
// a draft is submitted and withdrawn through submit_record and withdraw_record, and a person accepts it on the decision page.
internal static partial class ToolHandlers
{
    // ---- 87. list_configuration_changes ----

    // Versions newest first (all of them: a configuration set holds few versions), and drafts newest first, paged. Who
    // accepted a version: the people whose approval counted on its configuration change decision, with the day they
    // answered; a version the engine accepted with no card, or version 1 from the bootstrap, names no approver and says so.
    private static JsonTreeNode ListConfigurationChanges(ToolScope s)
    {
        string view = s.Text("view") ?? "all";
        var states = new HashSet<string>(s.Strings("draftStates"), StringComparer.Ordinal);
        int active = s.Call.Snapshot.VersionNumber;
        string setKey = s.Call.Snapshot.ConfigurationSetKey;
        var versionRows = s.Query("configuration-set-version", ("configurationSet", setKey)).OrderByDescending(v => v.Number("versionNumber") ?? 0).ToList();
        var draftRows = s.Rows.Query(RowQuery.For("configuration-draft"))
            .Where(d => states.Count == 0 || states.Contains(d.Text("draftStatus") ?? "draft"))
            .OrderByDescending(d => d.Text("submittedOn") ?? d.Text("engineCheckedOn") ?? string.Empty, StringComparer.Ordinal)
            .ThenByDescending(d => d.Id, StringComparer.Ordinal)
            .ToList();
        string? next = null;
        List<StoredRow> page = view == "versions" ? new List<StoredRow>() : s.Page(draftRows, out next);

        int open = s.Rows.Query(RowQuery.For("configuration-draft")).Count(d => d.Text("draftStatus") is "draft" or "submitted");
        JsonTreeNode data = Js.Obj(
            ("configurationSet", Js.Obj(
                ("key", JsonTreeNode.FromString(setKey)),
                ("activeVersion", JsonTreeNode.FromInt64(active)),
                ("contentHash", JsonTreeNode.FromString(s.Call.Snapshot.ContentHash)))),
            ("versions", Arr(view == "drafts" ? Enumerable.Empty<JsonTreeNode>() : versionRows.Select(v => VersionItem(s, v, active)))),
            ("drafts", Arr(page.Select(d => DraftItem(s, d)))),
            ("counts", Js.Obj(
                ("versions", JsonTreeNode.FromInt64(versionRows.Count)),
                ("drafts", JsonTreeNode.FromInt64(draftRows.Count)),
                ("openDrafts", JsonTreeNode.FromInt64(open)))),
            ("skipToken", Js.OptStr(next)));
        return s.Answer(data, "Version " + active + " is in force. " + Count(versionRows.Count, "version", "versions") + " in all; "
            + Count(open, "draft is", "drafts are") + " open. A draft changes nothing until a person accepts its decision.");
    }

    // One version: when it was published, by whose instruction, the decision that accepted it and the people whose answer
    // counted, and the change summary the engine wrote.
    private static JsonTreeNode VersionItem(ToolScope s, StoredRow v, int active)
    {
        long number = v.Number("versionNumber") ?? 0;
        StoredRow? decision = v.Text("changeDecision") is string decisionId ? s.Rows.Get("decision", decisionId) : null;
        List<JsonTreeNode> accepted = decision == null ? new List<JsonTreeNode>() : AcceptedBy(s, decision);
        string? draftId = v.Text("sourceDraft");
        return Js.Obj(
            ("version", JsonTreeNode.FromInt64(number)),
            ("isActive", JsonTreeNode.FromBoolean(number == active)),
            ("baseVersion", v.Number("baseVersionNumber") is long b ? JsonTreeNode.FromInt64(b) : JsonTreeNode.Null),
            ("publishedOn", ToolScope.MomentOrNull(v.Text("publishedOn") ?? v.Text("engineCheckedOn"))),
            ("contentHash", Js.Str(v.Text("contentHash"))),
            ("changeSummary", Js.Strs(VersionChangeLines(s, v))),
            ("instructedBy", s.PrincipalRef(v.Text("instructedBy"))),
            ("decision", DecisionRef(s, decision)),
            ("acceptedBy", Arr(accepted)),
            ("acceptedHow", JsonTreeNode.FromString(decision == null ? "bootstrap" : accepted.Count > 0 ? "approval" : decision.Text("statusMeaning") == "accepted" ? "engine" : "not-accepted")),
            ("sourceDraftId", draftId != null && ToolScope.IsGuid(draftId) ? JsonTreeNode.FromString(draftId) : JsonTreeNode.Null));
    }

    // One draft as a list row: its state, base version, what it changes, whether it validated, its dates and its decision.
    private static JsonTreeNode DraftItem(ToolScope s, StoredRow d)
    {
        JsonTreeNode? validation = d.Column("validationJson");
        var issues = Js.Items(validation, "issues");
        StoredRow? decision = d.Text("changeDecision") is string decisionId ? s.Rows.Get("decision", decisionId) : null;
        string state = d.Text("draftStatus") ?? "draft";
        JsonTreeNode? isValid = Js.Member(validation, "isValid");
        return Js.Obj(
            ("configurationDraftId", JsonTreeNode.FromString(d.Id)),
            ("recordNumber", Js.Str(d.Text("recordNumber"))),
            ("state", Js.Obj(("key", JsonTreeNode.FromString(state)), ("label", JsonTreeNode.FromString(DraftStateLabel(state))))),
            ("baseVersion", JsonTreeNode.FromInt64(d.Number("baseVersionNumber") ?? s.Call.Snapshot.VersionNumber)),
            ("isUpgradeDraft", JsonTreeNode.FromBoolean(d.Flag("isUpgradeDraft"))),
            ("changeSummary", Js.Strs(DraftChangeLines(s, d))),
            ("operationCount", JsonTreeNode.FromInt64(Js.Items(d.Column("changeJson"), "operations").Count)),
            ("isValid", isValid != null && (isValid.Kind == JsonTreeKind.True || isValid.Kind == JsonTreeKind.False) ? isValid : JsonTreeNode.Null),
            ("errorCount", JsonTreeNode.FromInt64(issues.Count(i => Js.Text(i, "severity") == "error"))),
            ("warningCount", JsonTreeNode.FromInt64(issues.Count(i => Js.Text(i, "severity") == "warning"))),
            ("instructedBy", s.PrincipalRef(d.Text("instructedBy"))),
            ("submittedOn", ToolScope.MomentOrNull(d.Text("submittedOn"))),
            ("expiresOn", ToolScope.MomentOrNull(d.Text("expiresOn"))),
            ("discardedOn", ToolScope.MomentOrNull(d.Text("discardedOn"))),
            ("discardReason", Js.Str(d.Text("discardReason"))),
            ("resultingVersion", d.Number("resultingVersion") is long r ? JsonTreeNode.FromInt64(r) : JsonTreeNode.Null),
            ("decision", DecisionRef(s, decision)),
            ("isAiCreated", JsonTreeNode.FromBoolean(d.Flag("isAiCreated") || DraftedByAi(s, d.Id))),
            ("performedBy", Stamp(s, d)),
            ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("configuration-draft", d.Id))));
    }

    // ---- 88. get_configuration_draft ----

    // One configuration draft as its record page shows it (/app/records/configuration-draft/<id>): the list row, the
    // rationale, the row diff against the configuration in force now, the rule errors and warnings the engine stored when
    // it validated the draft, and the impact (the applications each change touches, the gate levels that move, the gates
    // whose requirements change, the records in flight). Choice: the diff and the impact are computed again against the
    // version in force at the time of the read, as propose_configuration_change computed them, so a draft whose base has
    // moved shows what it would change now and says that its base moved; the stored validation stays the engine's word.
    private static JsonTreeNode GetConfigurationDraft(ToolScope s)
    {
        StoredRow d = s.RequireRow("configuration-draft", s.Text("configurationDraftId"), "configurationDraftId");
        JsonTreeNode operations = Arr(Js.Items(d.Column("changeJson"), "operations"));
        DraftImpact impact = DraftImpactOf(s, operations);
        JsonTreeNode? validation = d.Column("validationJson");
        JsonTreeNode Issue(JsonTreeNode i) => Js.Obj(
            ("entity", Js.Str(Js.Text(i, "entity") is string e ? EntityKey(e) : null)),
            ("key", Js.Str(Js.Text(i, "ref") is string r && KeyPathShape.IsMatch(r) ? r : null)),
            ("ruleKey", Js.Str(Js.Text(i, "ruleId"))),
            ("message", JsonTreeNode.FromString(Js.Text(i, "message") ?? "A rule reported this.")));
        var issues = Js.Items(validation, "issues");
        string state = d.Text("draftStatus") ?? "draft";
        long baseVersion = d.Number("baseVersionNumber") ?? s.Call.Snapshot.VersionNumber;
        bool baseMoved = state is "draft" or "submitted" && baseVersion != s.Call.Snapshot.VersionNumber;
        StoredRow? decision = d.Text("changeDecision") is string decisionId ? s.Rows.Get("decision", decisionId) : null;
        var applications = impact.Affected.Select(id => s.Rows.Get("business-application", id)).Where(a => a != null).Select(a => a!)
            .OrderBy(a => AppName(a), StringComparer.OrdinalIgnoreCase).ThenBy(a => a.Id, StringComparer.Ordinal).ToList();
        JsonTreeNode data = Js.Obj(
            ("draft", DraftItem(s, d)),
            ("proposalRationale", Js.Str(d.Text("proposalRationale"))),
            ("baseMoved", JsonTreeNode.FromBoolean(baseMoved)),
            ("activeVersion", JsonTreeNode.FromInt64(s.Call.Snapshot.VersionNumber)),
            ("diff", Arr(impact.Diff)),
            ("validation", Js.Obj(
                ("isValid", Js.Member(validation, "isValid") is JsonTreeNode v && (v.Kind == JsonTreeKind.True || v.Kind == JsonTreeKind.False) ? v : JsonTreeNode.Null),
                ("errors", Arr(issues.Where(i => Js.Text(i, "severity") == "error").Select(Issue))),
                ("warnings", Arr(issues.Where(i => Js.Text(i, "severity") == "warning").Select(Issue))))),
            ("impact", Js.Obj(
                ("applicationsAffected", JsonTreeNode.FromInt64(impact.Affected.Count)),
                ("applications", Arr(applications.Take(50).Select(a => Js.Obj(
                    ("businessApplicationId", JsonTreeNode.FromString(a.Id)),
                    ("name", JsonTreeNode.FromString(AppName(a))),
                    ("tier", s.LabelledKey("tier", a.Text("tier"))),
                    ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("business-application", a.Id))))))),
                ("byChange", Arr(impact.ByChange)),
                ("gateChanges", Arr(impact.GateChanges)),
                ("gatesTouched", Arr(impact.GatesTouched.Select(g => s.LabelledKey("gate", g)))),
                ("recordsInFlight", JsonTreeNode.FromInt64(impact.InFlight)),
                ("dataUses", state is "draft" or "submitted" && d.Column("changeJson") is JsonTreeNode change && change.Kind == JsonTreeKind.Object
                    ? DataUseImpactItems(s, s.Call.DataUseImpact(baseVersion, change))
                    : Js.EmptyArray))),
            ("decision", DecisionRef(s, decision)),
            ("approvals", decision == null ? JsonTreeNode.Null : ApprovalsSummary(s, decision.Id)),
            ("acceptedBy", Arr(decision == null ? Enumerable.Empty<JsonTreeNode>() : AcceptedBy(s, decision))));

        var steps = new List<NextStep>();
        if (state == "draft")
        {
            steps.Add(ToolScope.Next("submit_record", ("subjectKind", JsonTreeNode.FromString("configuration-draft")), ("subjectId", JsonTreeNode.FromString(d.Id))));
        }

        if (state is "draft" or "submitted")
        {
            steps.Add(ToolScope.Next("withdraw_record", ("subjectKind", JsonTreeNode.FromString("configuration-draft")), ("subjectId", JsonTreeNode.FromString(d.Id))));
        }

        string summary = "Configuration draft " + (d.Text("recordNumber") ?? d.Id) + " is " + DraftStateLabel(state).ToLowerInvariant() + ": "
            + Count(impact.Diff.Count, "row change", "row changes") + ", " + Count(impact.Affected.Count, "application touched", "applications touched")
            + (issues.Any(i => Js.Text(i, "severity") == "error") ? ", with rule errors to fix" : string.Empty)
            + (baseMoved ? "; its base version " + baseVersion + " is no longer the version in force" : string.Empty) + ".";
        return s.Answer(data, summary, s.RecordUrl("configuration-draft", d.Id), steps.ToArray());
    }

    // ---- Shared by the configuration reads ----

    // The engine writes a draft as a nested write for its caller, so the row carries no AI mark of its own; the call history
    // does: the first call that wrote the draft names its actor kind (document 25, B25-13).
    private static bool DraftedByAi(ToolScope s, string draftId)
    {
        StoredRow? first = s.Query("audit-entry", ("rowId", draftId)).Where(e => e.Text("operation") == "composed-call")
            .OrderBy(e => e.Text("occurredOn") ?? string.Empty, StringComparer.Ordinal).ThenBy(e => e.Id, StringComparer.Ordinal).FirstOrDefault();
        return first != null && AiActorKinds.Contains(first.Text("actorKind") ?? string.Empty, StringComparer.Ordinal);
    }

    // A stored date or moment as the day it names, such as 2026-11-22.
    private static string DayText(string? stored)
    {
        return Js.ParseMoment(stored)?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? stored ?? string.Empty;
    }

    private static string DraftStateLabel(string state)
    {
        return state switch
        {
            "draft" => "Draft",
            "submitted" => "Submitted",
            "activated" => "Activated",
            "discarded" => "Withdrawn or discarded",
            _ => ToolScope.Labelize(state),
        };
    }

    // A version's change in plain words: through the draft it came from (product review: no raw key in backticks), else the
    // engine's stored summary (the bootstrap version, or a draft that is gone).
    internal static IReadOnlyList<string> VersionChangeLines(ToolScope s, StoredRow version)
    {
        if (version.Text("sourceDraft") is string draftId && ToolScope.IsGuid(draftId) && s.Rows.Get("configuration-draft", draftId) is StoredRow draft)
        {
            IReadOnlyList<string> lines = DraftChangeLines(s, draft);
            if (lines.Count > 0)
            {
                return lines;
            }
        }

        return SummaryLines(version.Column("changeSummary")).ToList();
    }

    // The change summary lines: a text column split on new lines, or a JSON array of lines.
    private static IEnumerable<string> SummaryLines(JsonTreeNode? column)
    {
        if (column == null || column.Kind == JsonTreeKind.Null)
        {
            return Array.Empty<string>();
        }

        IEnumerable<string> lines = column.Kind == JsonTreeKind.Array
            ? column.Items.Where(i => i.Kind == JsonTreeKind.String).Select(i => i.GetString())
            : column.Kind == JsonTreeKind.String ? column.GetString().Split('\n') : Array.Empty<string>();
        return lines.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
    }

    // { decisionId, recordNumber, status, decidedOn, recordUrl }, or null.
    private static JsonTreeNode DecisionRef(ToolScope s, StoredRow? decision)
    {
        if (decision == null)
        {
            return JsonTreeNode.Null;
        }

        return Js.Obj(
            ("decisionId", JsonTreeNode.FromString(decision.Id)),
            ("recordNumber", Js.Str(decision.Text("recordNumber"))),
            ("decisionType", s.LabelledKey("decision-type", decision.Text("decisionType") ?? "unknown")),
            ("status", s.StatusValue(decision) is JsonTreeNode st && st.Kind != JsonTreeKind.Null ? st : ToolScope.StatusValue("proposed", null)),
            ("decidedOn", ToolScope.MomentOrNull(decision.Text("decidedOn"))),
            ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("decision", decision.Id))));
    }

    // The people whose approval counted on a decision, with the day each answered (an AI answer never counts).
    private static List<JsonTreeNode> AcceptedBy(ToolScope s, StoredRow decision)
    {
        return s.Query("approval-record", ("decision", decision.Id))
            .Where(r => r.Text("outcomeMeaning") == "approved" && r.Flag("countsTowardRequirement") && r.Text("approver") != null)
            .OrderBy(r => r.Text("respondedOn") ?? string.Empty, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal)
            .Select(r => Js.Obj(
                ("principal", s.PrincipalRef(r.Text("approver"))),
                ("answeredOn", ToolScope.MomentOrNull(r.Text("respondedOn"))),
                ("askedAs", r.Text("requiredRole") is string role ? s.LabelledKey("role", role) : r.Text("requiredOwnerRole") is string owner ? s.LabelledKey("owner-role", owner) : JsonTreeNode.Null)))
            .ToList();
    }
    // The live data uses and copies whose state the change would move (docs/architecture/data-taxonomy.md, Changing the taxonomy), as the record page
    // lists them with links: the application, the domain path and the state before and after.
    private static JsonTreeNode DataUseImpactItems(ToolScope s, JsonTreeNode rows)
    {
        var tree = new Product.Engine.Core.Operations.DataTree(s.Snapshot);
        return Arr(rows.Items.Select(r =>
        {
            string kind = Js.Text(r, "kind") ?? "data-use";
            string id = Js.Text(r, "id") ?? string.Empty;
            string domainKey = Js.Text(r, "domainKey") ?? string.Empty;
            StoredRow? app = Js.Text(r, "businessApplicationId") is string appId ? s.Rows.Get("business-application", appId) : null;
            return Js.Obj(
                ("kind", JsonTreeNode.FromString(kind)),
                ("id", JsonTreeNode.FromString(id)),
                ("recordNumber", Js.Str(Js.Text(r, "recordNumber"))),
                ("application", app == null ? JsonTreeNode.Null : Js.Obj(("businessApplicationId", JsonTreeNode.FromString(app.Id)), ("name", JsonTreeNode.FromString(AppName(app))))),
                ("domain", Js.Obj(("key", JsonTreeNode.FromString(domainKey)), ("path", JsonTreeNode.FromString(tree.Domain(domainKey) == null ? domainKey : tree.PathText(domainKey))))),
                ("stateBefore", JsonTreeNode.FromString(Js.Text(r, "stateBefore") ?? "needed")),
                ("stateAfter", JsonTreeNode.FromString(Js.Text(r, "stateAfter") ?? "needed")),
                ("breaksRule", JsonTreeNode.FromBoolean(Js.Flag(r, "breaksRule") == true)),
                ("needsNewDecision", JsonTreeNode.FromBoolean(Js.Flag(r, "needsNewDecision") == true)),
                ("recordUrl", JsonTreeNode.FromString(s.RecordUrl(kind, id))));
        }));
    }
}
