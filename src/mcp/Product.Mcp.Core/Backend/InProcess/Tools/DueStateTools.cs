using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Runtime;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Json;

namespace Product.Mcp.Core.Backend.InProcess;

// Tool 86, run_due_state_refresh (document 23, S2; document 24, B24-17): a platform admin runs the
// engine's daily due-state refresh now, on the web app, as the demo of UC-08 shows it. The engine runs it every day on its
// own (the host's timer, a host setting); this tool runs the same refresh at once. Served only on the person entry: the
// engine refuses anyone but the engine itself and a person holding the platform admin role, and an AI client is never
// served it. Every item of the refresh commits in its own call before this tool reads the answer, so one item that fails
// is reported and the rest are kept; validateOnly runs every item and commits nothing.
internal static partial class ToolHandlers
{
    // ---- 86. run_due_state_refresh ----

    internal static RuntimeOutcome RunDueStateRefreshFirst(InProcessBackend backend, BackendSession session, ToolInvocation invocation)
    {
        bool dryRun = Js.Flag(invocation.Arguments, "validateOnly") == true;
        return backend.Runtime.RefreshDueStates(
            InProcessBackend.Identity(session.Caller),
            new DueStateRefreshRequest(DueStateRefreshRequest.PersonTrigger, dryRun) { CorrelationId = invocation.CorrelationId });
    }

    private static JsonTreeNode RunDueStateRefresh(ToolScope s)
    {
        RuntimeOutcome run = s.Prepared ?? throw new InvalidOperationException("run_due_state_refresh reads the refresh its first step ran.");
        s.Call.Report(run.Reasons);
        JsonTreeNode report = run.Payload ?? Js.EmptyObject;
        bool dryRun = Js.Flag(report, "dryRun") == true;
        JsonTreeNode counts = Js.Member(report, "counts") ?? Js.EmptyObject;

        var reviews = Js.Items(report, "reviewsOpened").Select(r =>
        {
            string? app = Js.Text(r, "businessApplicationId");
            string? id = Js.Text(r, "reviewId");
            return Js.Obj(
                ("reviewId", Js.Str(dryRun ? null : id)),
                ("recordNumber", Js.Str(Js.Text(r, "recordNumber"))),
                ("businessApplicationId", JsonTreeNode.FromString(app ?? string.Empty)),
                ("applicationName", JsonTreeNode.FromString(AppNameOf(s, app))),
                ("reviewType", s.LabelledKey("review-type", Js.Text(r, "reviewTypeKey") ?? "unknown")),
                ("dueOn", ToolScope.DateOrNull(Js.Text(r, "dueOn"))),
                ("fromReviewId", JsonTreeNode.FromString(Js.Text(r, "fromReviewId") ?? string.Empty)),
                ("recordUrl", dryRun || id == null ? JsonTreeNode.Null : JsonTreeNode.FromString(s.RecordUrl("review", id))));
        }).ToList();

        var documents = Js.Items(report, "documentsStale").Select(d =>
        {
            string? app = Js.Text(d, "businessApplicationId");
            string id = Js.Text(d, "documentLinkId") ?? string.Empty;
            return Js.Obj(
                ("documentLinkId", JsonTreeNode.FromString(id)),
                ("businessApplicationId", Js.Str(app)),
                ("applicationName", app == null ? JsonTreeNode.Null : JsonTreeNode.FromString(AppNameOf(s, app))),
                ("documentType", s.LabelledKey("document-type", Js.Text(d, "documentTypeKey") ?? "unknown")),
                ("staleOn", ToolScope.DateOrNull(Js.Text(d, "staleOn"))),
                ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("document-link", id))));
        }).ToList();

        var notices = Js.Items(report, "notices").Select(n => NoticeJson(s, n)).ToList();
        JsonTreeNode approvals = Js.Member(report, "approvals") ?? Js.EmptyObject;
        JsonTreeNode data = Js.Obj(
            ("trigger", JsonTreeNode.FromString(Js.Text(report, "trigger") ?? DueStateRefreshRequest.PersonTrigger)),
            ("dryRun", JsonTreeNode.FromBoolean(dryRun)),
            ("counts", counts),
            ("reviewsOpened", Arr(reviews)),
            ("documentsStale", Arr(documents)),
            ("approvals", Js.Obj(
                ("decisions", JsonTreeNode.FromInt64(Js.Number(approvals, "decisions") ?? 0)),
                ("expiredRecordIds", Js.Strs(Js.Strings(approvals, "expiredRecordIds"))),
                ("cancelledRecordIds", Js.Strs(Js.Strings(approvals, "cancelledRecordIds"))),
                ("openedRecordIds", Js.Strs(dryRun ? Array.Empty<string>() : Js.Strings(approvals, "openedRecordIds"))))),
            ("notices", Arr(notices)),
            ("failed", Arr(Js.Items(report, "failed"))),
            ("more", JsonTreeNode.FromBoolean(Js.Flag(report, "more") == true)));
        return s.Answer(data, RefreshSummary(counts, dryRun, Js.Flag(report, "more") == true));
    }

    // One notice as the page shows it: what, about which record, to whom (people by name, roles by label), by which rule.
    private static JsonTreeNode NoticeJson(ToolScope s, JsonTreeNode notice)
    {
        string entity = Js.Text(notice, "entity") ?? "unknown";
        string rowId = Js.Text(notice, "rowId") ?? string.Empty;
        StoredRow? row = s.Rows.Get(entity, rowId);
        string? app = Js.Text(notice, "businessApplicationId");
        string? decision = entity == "approval-record" ? row?.Text("decision") : null;
        JsonTreeNode to = Js.Member(notice, "to") ?? Js.EmptyObject;
        string? ownerRole = Js.Text(to, "ownerRoleKey");
        long? number = Js.Number(notice, "reminderNumber");
        return Js.Obj(
            ("notice", JsonTreeNode.FromString(Js.Text(notice, "notice") ?? "remind")),
            ("subjectKind", JsonTreeNode.FromString(Js.Text(notice, "subjectKind") ?? "review")),
            ("entity", JsonTreeNode.FromString(entity)),
            ("rowId", JsonTreeNode.FromString(rowId)),
            ("label", JsonTreeNode.FromString(row == null ? ToolScope.EntityName(entity) : RecordLabel(s, row))),
            ("businessApplicationId", Js.Str(app)),
            ("applicationName", app == null ? JsonTreeNode.Null : JsonTreeNode.FromString(AppNameOf(s, app))),
            ("ruleKey", Js.Str(Js.Text(notice, "ruleKey"))),
            ("reminderNumber", number.HasValue ? JsonTreeNode.FromInt64(number.Value) : JsonTreeNode.Null),
            ("escalated", JsonTreeNode.FromBoolean(Js.Flag(notice, "escalated") == true)),
            ("channel", ListValue(s, "channel", Js.Text(notice, "channelKey"))),
            ("dueOn", ToolScope.DateOrNull(Js.Text(notice, "dueOn"))),
            ("to", Js.Obj(
                ("people", Arr(Js.Strings(to, "principalIds").Select(s.PrincipalRef))),
                ("roles", Arr(Js.Strings(to, "roleKeys").Select(r => s.LabelledKey("role", r)))),
                ("ownerRole", s.LabelledKey("owner-role", ownerRole)),
                ("toDeputies", JsonTreeNode.FromBoolean(Js.Flag(to, "toDeputies") == true)))),
            ("recordUrl", JsonTreeNode.FromString(decision != null ? s.RecordUrl("decision", decision) : s.RecordUrl(entity, rowId))));
    }

    private static string AppNameOf(ToolScope s, string? appId)
    {
        return appId != null && s.Rows.Get("business-application", appId) is StoredRow app ? AppName(app) : appId ?? string.Empty;
    }

    private static string RefreshSummary(JsonTreeNode counts, bool dryRun, bool more)
    {
        long N(string name) => Js.Number(counts, name) ?? 0;
        var parts = new List<string>();
        void Add(long n, string one, string many)
        {
            if (n > 0)
            {
                parts.Add(Count((int)n, one, many));
            }
        }

        Add(N("reviewsOpened"), "review opened", "reviews opened");
        Add(N("reviewsOverdue"), "review marked overdue", "reviews marked overdue");
        Add(N("documentsStale"), "document marked stale", "documents marked stale");
        Add(N("approvalsExpired"), "approval request expired", "approval requests expired");
        Add(N("approvalsOpened"), "approval request asked again", "approval requests asked again");
        Add(N("approvalsCancelled"), "approval request canceled", "approval requests canceled");
        Add(N("reminders"), "reminder", "reminders");
        Add(N("escalations"), "escalation", "escalations");
        Add(N("flagNotices"), "flag notice", "flag notices");
        string head = dryRun ? "A dry run of the due-state refresh wrote nothing. It found: " : "The due-state refresh ran: ";
        string body = parts.Count == 0 ? (dryRun ? "nothing to do today." : "nothing was due today.") : string.Join(", ", parts) + ".";
        string failed = N("failed") > 0 ? " " + Count((int)N("failed"), "item", "items") + " could not finish; the warnings say why, and the next run tries again." : string.Empty;
        return head + body + failed + (more ? " More items wait for the next run." : string.Empty) + " The daily timer runs it too, on a host that turns it on.";
    }
}
