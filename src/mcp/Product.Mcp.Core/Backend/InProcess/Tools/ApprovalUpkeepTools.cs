using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Json;

namespace Product.Mcp.Core.Backend.InProcess;

// Tools 100 to 102 (document 24, B24-22): keeping approval requests alive.
// - 100 renew_approval_request: "Ask again" on an expired request, or one in the last 48 hours of its time. The proposer of
//   the decision or a platform admin, as a person in the web app; the engine's own writes (ComposedCall.RenewApprovalRequest).
// - 101 retry_approval_card: the platform admin sends a stuck approval card again, from the Admin page: a failed outbox row,
//   or one sent more than two days ago with no answer, goes back to pending, and the call's Audit Entry records it.
// - 102 get_demo_freshness: on a demo install, which of the story's approval requests expired or expire soon, so the Demo
//   guide can say "run demo refresh" before a demo meets an expired request.
internal static partial class ToolHandlers
{
    // The Demo guide's default look ahead: the demo kit's refresh renews what lapses within two days.
    private const int DemoFreshnessHours = 48;

    // ---- 100. renew_approval_request ----

    private static JsonTreeNode RenewApprovalRequest(ToolScope s)
    {
        string id = s.RequireText("approvalRecordId");
        StoredRow record = s.RequireRow("approval-record", id, "approvalRecordId");
        StoredRow? decision = record.Text("decision") is string decisionId ? s.Rows.Get("decision", decisionId) : null;
        if (decision == null || HidesIntake(s, decision))
        {
            throw s.NotFound("Approval Record", id, "approvalRecordId");
        }

        JsonTreeNode payload = s.Call.RenewApprovalRequest(id);
        bool renewed = Js.Flag(payload, "renewed") == true;
        IReadOnlyList<string> opened = Js.Strings(payload, "openedRecordIds");
        StoredRow? open = opened.Count == 0 ? null : s.Rows.Get("approval-record", opened[0]);
        StoredRow decided = s.Rows.Get("decision", decision.Id) ?? decision;
        string number = decided.Text("recordNumber") ?? decided.Id;
        string who = open == null ? "the same role" : AskedWords(s, open);
        DateTime? expiresOn = Js.ParseMoment(open?.Text("expiresOn"));
        // How long the request runs, never its UTC date: a date in words would be tomorrow's for a person in New York after
        // 7 pm (wave 4b QA walk). The request in data carries the moment, which a page shows in the person's own zone.
        string until = expiresOn.HasValue ? ExpiresIn(expiresOn.Value - s.Now) : string.Empty;
        JsonTreeNode data = Js.Obj(
            ("approvalRecordId", JsonTreeNode.FromString(id)),
            ("renewed", JsonTreeNode.FromBoolean(renewed)),
            ("previousOutcome", Js.Str(Js.Text(payload, "previousOutcome"))),
            ("decision", Js.Obj(
                ("decisionId", JsonTreeNode.FromString(decided.Id)),
                ("recordNumber", Js.Str(decided.Text("recordNumber"))),
                ("statement", JsonTreeNode.FromString(decided.Text("statement") ?? string.Empty)),
                ("decisionType", s.LabelledKey("decision-type", decided.Text("decisionType") ?? "unknown")),
                ("status", s.StatusValue(decided)),
                ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("decision", decided.Id))))),
            ("request", open == null ? JsonTreeNode.Null : RequestItem(s, open)),
            ("openedRecordIds", Js.Strs(s.Call.IsDryRun ? Array.Empty<string>() : opened)),
            ("status", open == null ? JsonTreeNode.Null : s.StatusValue(open, "outcome", "outcomeMeaning")),
            ("performedBy", s.CallerPerformedBy()),
            ("dryRun", JsonTreeNode.FromBoolean(s.Call.IsDryRun)));
        string summary = !renewed
            ? "Decision " + number + " already asks " + who + " again" + (until.Length == 0 ? string.Empty : "; that request expires " + until) + ". Nothing changed."
            : s.Call.IsDryRun
                ? "A dry run: decision " + number + " would ask " + who + " again. Nothing was written."
                : "Decision " + number + " asks " + who + " again." + (until.Length == 0 ? " The request does not expire." : " The new request expires " + until + ".");
        return s.Answer(data, summary, s.RecordUrl("decision", decided.Id));
    }

    // "in 2 days", "in 5 hours": the time left, in the words of any time zone.
    private static string ExpiresIn(TimeSpan left)
    {
        double hours = Math.Max(0, left.TotalHours);
        if (hours < 24)
        {
            int h = Math.Max(1, (int)Math.Round(hours));
            return "in " + h + (h == 1 ? " hour" : " hours");
        }

        int days = (int)Math.Round(hours / 24);
        return "in " + days + (days == 1 ? " day" : " days");
    }

    // One approval request: who it asks and its times.
    private static JsonTreeNode RequestItem(ToolScope s, StoredRow request)
    {
        return Js.Obj(
            ("approvalRecordId", JsonTreeNode.FromString(request.Id)),
            ("askedRole", request.Text("requiredRole") is string role ? s.LabelledKey("role", role) : JsonTreeNode.Null),
            ("askedOwnerRole", request.Text("requiredOwnerRole") is string ownerRole ? s.LabelledKey("owner-role", ownerRole) : JsonTreeNode.Null),
            ("askedPerson", s.PrincipalRef(request.Text("askedPrincipal"))),
            ("isConsultation", JsonTreeNode.FromBoolean(request.Flag("isConsultation"))),
            ("requestedOn", ToolScope.MomentOrNull(request.Text("requestedOn"))),
            ("expiresOn", ToolScope.MomentOrNull(request.Text("expiresOn"))));
    }

    // Who a request asks, in words: the person by name, else the role or owner role.
    private static string AskedWords(ToolScope s, StoredRow request)
    {
        if (request.Text("askedPrincipal") is string person)
        {
            return s.DisplayLabel(person);
        }

        return request.Text("requiredRole") is string role ? "the " + s.Label("role", role).ToLowerInvariant()
            : request.Text("requiredOwnerRole") is string owner ? "the " + s.Label("owner-role", owner).ToLowerInvariant()
            : "the same role";
    }

    // ---- 101. retry_approval_card ----

    private static JsonTreeNode RetryApprovalCard(ToolScope s)
    {
        if (!s.HoldsRole("platform-admin"))
        {
            throw s.Refused(McpErrorCodes.NotAuthorized, "Only a platform admin sends an approval card again.");
        }

        IApprovalOutboxPort port = s.Options.ApprovalOutbox
            ?? throw s.Upstream("The approval outbox", "this host keeps no approval outbox, so there is no card to send again");
        string id = s.RequireText("approvalRecordId");
        StoredRow record = s.RequireRow("approval-record", id, "approvalRecordId");
        OutboxAttention row = port.Get(id) ?? throw s.NotFound("Approval card", id, "approvalRecordId");
        StoredRow? decision = record.Text("decision") is string decisionId ? s.Rows.Get("decision", decisionId) : null;

        // A request whose answer is in, or whose decision moved on, needs no card: the row closes instead.
        string? outcome = record.Text("outcomeMeaning");
        DateTime? expiresOn = Js.ParseMoment(record.Text("expiresOn"));
        string? settled = outcome is "approved" or "rejected" or "recommendation-only" ? "answered"
            : outcome == "expired" ? "expired"
            : outcome == "cancelled" ? "cancelled"
            : decision != null && decision.Text("statusMeaning") != "proposed" ? "cancelled"
            : expiresOn.HasValue && s.Now >= expiresOn.Value ? "expired"
            : null;
        bool closedRow = row.State is "answered" or "expired" or "cancelled";
        string action;
        string after;
        if (closedRow)
        {
            throw s.Invalid("approvalRecordId", "The card of approval request " + id + " is closed (" + CardStateWords(row.State) + "), so there is nothing to send again.");
        }

        // The outbox row changes only once the engine has committed the Audit Entry of who asked (security review,
        // wave 4: the port commits at once, so a later failed commit left a changed card with no record of the ask).
        // A row that changed in between is reported as a warning; the audit entry then records an ask that found it moved.
        DateTime now = s.Now;
        Product.Engine.Runtime.ComposedCall call = s.Call;
        if (settled != null)
        {
            action = "closed";
            after = settled;
            call.AfterCommit(() =>
            {
                if (!port.Settle(id, settled, now))
                {
                    call.Warn("outbox.row-changed", "The card of approval request " + id + " changed while it was being closed, so it was left as it is. Read the Admin page again.", "Approval Outbox", id);
                }
            });
        }
        else if (row.State == "failed" || (row.State == "sent" && s.Now - row.UpdatedOnUtc > port.LongSent))
        {
            action = "sent-again";
            after = "pending";
            call.AfterCommit(() =>
            {
                if (!port.Retry(id, now))
                {
                    call.Warn("outbox.row-changed", "The card of approval request " + id + " changed while it was being sent again, so it was left as it is. Read the Admin page again.", "Approval Outbox", id);
                }
            });
        }
        else
        {
            throw s.Invalid("approvalRecordId", "The card of approval request " + id + " is " + CardStateWords(row.State) + ". Only a card that failed, or one sent more than " + (int)port.LongSent.TotalDays + " days ago with no answer, is sent again.");
        }

        s.Call.RecordHostChange("approval-outbox", id, after);
        JsonTreeNode data = Js.Obj(
            ("approvalRecordId", JsonTreeNode.FromString(id)),
            ("decision", decision == null ? JsonTreeNode.Null : Js.Obj(
                ("decisionId", JsonTreeNode.FromString(decision.Id)),
                ("recordNumber", Js.Str(decision.Text("recordNumber"))),
                ("statement", JsonTreeNode.FromString(decision.Text("statement") ?? string.Empty)),
                ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("decision", decision.Id))))),
            ("action", JsonTreeNode.FromString(action)),
            ("stateBefore", JsonTreeNode.FromString(row.State)),
            ("stateAfter", JsonTreeNode.FromString(after)),
            ("attemptsBefore", JsonTreeNode.FromInt64(row.Attempts)),
            ("performedBy", s.CallerPerformedBy()),
            ("dryRun", JsonTreeNode.FromBoolean(s.Call.IsDryRun)));
        string head = s.Call.IsDryRun ? "A dry run: nothing changed. " : string.Empty;
        string summary = head + (action == "closed"
            ? "The request needs no card any more (" + CardStateWords(after) + "), so its outbox row is closed."
            : "The card goes back to the approval runner, which sends it again on its next pull. The audit trail records who asked.");
        return s.Answer(data, summary, decision == null ? null : s.RecordUrl("decision", decision.Id));
    }

    private static string CardStateWords(string state)
    {
        return state switch
        {
            "pending" => "waiting for the approval runner",
            "claimed" => "being sent now",
            "sent" => "sent and waiting for its answer",
            "answered" => "answered",
            "expired" => "expired",
            "cancelled" => "canceled",
            "failed" => "failed",
            _ => state,
        };
    }

    // ---- 102. get_demo_freshness ----

    private static JsonTreeNode GetDemoFreshness(ToolScope s)
    {
        HostFacts? host = s.Options.HostFacts?.Invoke();
        bool demo = host != null && host.HoldsDemoEstate && (host.IsDemo || string.Equals(host.EnvironmentName, "Development", StringComparison.OrdinalIgnoreCase));
        if (!demo)
        {
            // Outside a demo install the read does not exist, as the approval runner's routes answer when they are off.
            throw s.Refused(McpErrorCodes.NotFound, "get_demo_freshness is served only on a demo install: the Demo environment, or a local run that holds the demo estate.");
        }

        int hours = (int)(Js.Number(s.Args, "withinHours") ?? DemoFreshnessHours);
        DateTime horizon = s.Now.AddHours(hours);
        List<StoredRow> requests = s.Query("approval-record").Where(r => r.Text("outcomeMeaning") is "pending" or "expired").ToList();
        var decisions = new Dictionary<string, StoredRow?>(StringComparer.OrdinalIgnoreCase);
        StoredRow? DecisionOf(StoredRow r)
        {
            string id = r.Text("decision") ?? string.Empty;
            if (!decisions.TryGetValue(id, out StoredRow? d))
            {
                decisions[id] = d = s.Rows.Get("decision", id);
            }

            return d;
        }

        var items = new List<(DateTime Expires, JsonTreeNode Item)>();
        int expired = 0;
        int soon = 0;
        foreach (StoredRow request in requests)
        {
            StoredRow? decision = DecisionOf(request);
            DateTime? expiresOn = Js.ParseMoment(request.Text("expiresOn"));
            if (decision == null || decision.Text("statusMeaning") != "proposed" || HidesIntake(s, decision) || !expiresOn.HasValue)
            {
                continue;
            }

            bool lapsed = request.Text("outcomeMeaning") == "expired" || s.Now >= expiresOn.Value;
            if (lapsed && requests.Any(r => r.Id != request.Id && Js.ParseMoment(r.Text("expiresOn")) is DateTime e && e > expiresOn.Value
                && Same(r.Text("decision"), request.Text("decision")) && SameAsk(r, request)))
            {
                // Asked again already (the daily refresh's restart, or a person's): the newest request of the ask stands
                // for it, so an ask asked again whose new request lapsed too is listed once (QA, fix round 1: 19 counted
                // for 18 asks, and refresh printed one ask twice).
                continue;
            }

            if (!lapsed && expiresOn.Value > horizon)
            {
                continue;
            }

            if (lapsed)
            {
                expired++;
            }
            else
            {
                soon++;
            }

            string? appId = ApplicationOf(s, decision);
            items.Add((expiresOn.Value, Js.Obj(
                ("approvalRecordId", JsonTreeNode.FromString(request.Id)),
                ("state", JsonTreeNode.FromString(lapsed ? "expired" : "expires-soon")),
                ("hoursLeft", JsonTreeNode.FromInt64((long)Math.Floor((expiresOn.Value - s.Now).TotalHours))),
                ("decision", Js.Obj(
                    ("decisionId", JsonTreeNode.FromString(decision.Id)),
                    ("recordNumber", Js.Str(decision.Text("recordNumber"))),
                    ("statement", JsonTreeNode.FromString(decision.Text("statement") ?? string.Empty)),
                    ("decisionType", s.LabelledKey("decision-type", decision.Text("decisionType") ?? "unknown")),
                    ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("decision", decision.Id))))),
                ("application", appId != null && s.Rows.Get("business-application", appId) is StoredRow app
                    ? Js.Obj(("businessApplicationId", JsonTreeNode.FromString(app.Id)), ("name", JsonTreeNode.FromString(AppName(app))))
                    : JsonTreeNode.Null),
                ("request", RequestItem(s, request)))));
        }

        JsonTreeNode data = Js.Obj(
            ("withinHours", JsonTreeNode.FromInt64(hours)),
            ("counts", Js.Obj(("expired", JsonTreeNode.FromInt64(expired)), ("expiresSoon", JsonTreeNode.FromInt64(soon)))),
            ("needsRefresh", JsonTreeNode.FromBoolean(expired + soon > 0)),
            ("items", Arr(items.OrderBy(i => i.Expires).Select(i => i.Item))),
            ("refreshWith", JsonTreeNode.FromString("demo refresh")),
            ("renewTool", JsonTreeNode.FromString("renew_approval_request")));
        string summary = expired + soon == 0
            ? "Every story approval request has more than " + hours + " hours left. The demo is fresh."
            : Count(expired, "story request has expired", "story requests have expired") + " and " + Count(soon, "expires", "expire") + " within " + hours + " hours. Run demo refresh before the demo.";
        return s.Answer(data, summary);
    }

    // The same ask: rule or decision right, consultation flag, role and owner role (an empty value matches an empty one).
    private static bool SameAsk(StoredRow a, StoredRow b)
    {
        static bool Equal(string? x, string? y) => string.Equals(x ?? string.Empty, y ?? string.Empty, StringComparison.Ordinal);
        return Equal(a.Text("approvalRule"), b.Text("approvalRule")) && Equal(a.Text("decisionRight"), b.Text("decisionRight"))
            && a.Flag("isConsultation") == b.Flag("isConsultation")
            && Equal(a.Text("requiredRole"), b.Text("requiredRole")) && Equal(a.Text("requiredOwnerRole"), b.Text("requiredOwnerRole"));
    }
}
