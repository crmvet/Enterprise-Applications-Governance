using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Product.Engine.Configuration.Model;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Snapshots;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Json;

namespace Product.Mcp.Core.Backend.InProcess;

// Tool 91, get_measures (document 24, B24-18; the Measures and slide-back signals screen; playbook chapters 17 and 19;
// guideline G24): the nine default measures and the ten slide-back signals, computed from the records the product keeps,
// over equal time windows that end today, each drilling to the rows behind it. Any role reads it: every number comes from
// rows every role reads already, a drill row is an ID, a label and a link, and an intake the caller may not read is left
// out and counted (intake-visibility).
//
// Choices (document 24, B24-18):
// - Flow measures (lead time, decisions on time, standard change share, deployments on record, AI work accepted) are
//   counted per window by the date of the event that ends them. Coverage measures (owner coverage, documents current)
//   read the records as they are now, because the store keeps no history of a state, so only the newest window holds a
//   value. Share registered reads each window's latest gap check run.
// - Targets and thresholds are configuration rows (document 24, B24-22): the settings measure-targets and
//   signal-thresholds (MeasureSettings), changed through a configuration change like any rule. A measure with no target
//   answers target null and targetSource not-set; a signal whose numbers the setting leaves out keeps the playbook's
//   default, marked product-default. The baseline is the oldest window with a value, as chapter 19 says: measure first,
//   then agree a target.
// - A signal links to the decisions it led to (B24-22): a decision proposed with slideBackSignalKey names the signal, and
//   each signal lists the latest of them, so "every signal that fired has a recorded decision" can be read on the page.
// - Two signals are read outside the product (environment requests in the ticket tool, rebuild projects in architecture
//   decisions), so they answer null with where to read them, never a guess.
internal static partial class ToolHandlers
{
    private const int DefaultWindowDays = 30;
    private const int DefaultWindows = 6;

    // The decisions a signal lists, newest first.
    private const int SignalDecisionsShown = 5;

    // ---- 91. get_measures ----

    private static JsonTreeNode GetMeasures(ToolScope s)
    {
        int windowDays = (int)(Js.Number(s.Args, "windowDays") ?? DefaultWindowDays);
        int count = (int)(Js.Number(s.Args, "windows") ?? DefaultWindows);
        string? drillMeasure = s.Text("measureKey");
        long? drillSignal = Js.Number(s.Args, "signalNumber");
        var w = new MeasureWindows(s.Now.Date, windowDays, count);
        var ctx = new MeasureContext(s, w);

        // The owners and the document links of every application in two store calls, not two per application (QA speed,
        // fix round 1: 4,049 reads and 15 s at 2,000 applications). The owner gaps and the documents read them per
        // application from the call's read cache.
        s.Call.PrefetchAll("owner-assignment");
        s.Call.PrefetchAll("document-link");

        // The people the drill rows name, and the decisions and requests the intake visibility test reads, each in one
        // read (wave 4b review: 219 reads at 2,000 applications, 164 of them one decision's requests at a time).
        s.Call.PrefetchAll("principal-reference");
        s.Call.PrefetchAll("approval-record");
        s.Call.Prefetch("decision", "subjectIntakeRequest", ctx.Rows("intake-request").Select(i => i.Id));

        var measures = new List<Measure>
        {
            LeadTime(ctx),
            ShareRegistered(ctx),
            OwnerCoverage(ctx),
            DecisionsOnTime(ctx),
            StandardChangeShare(ctx),
            DeploymentsOnRecord(ctx),
            DocumentsCurrent(ctx),
            AiWorkAccepted(ctx),
        };
        List<Signal> signals = Signals(ctx);
        int firing = signals.Count(g => g.Firing == true);
        JsonTreeNode? targets = s.SettingJson(MeasureSettings.TargetsSetting);
        var slideBack = new Measure("slide-back-signals", "Slide-back signals", "count", "coverage", "platform-admin", "G24", "quiet",
            "The ten signals of chapter 17, each with its threshold. The value counts the signals that fire now.");
        slideBack.Now = firing;
        slideBack.Rows.AddRange(signals.Where(g => g.Firing == true).Select(g => new DrillRow("signal", g.Number.ToString(CultureInfo.InvariantCulture), g.Number + ". " + g.Label, null, g.Value, g.Threshold)));
        measures.Add(slideBack);

        if (drillMeasure != null && measures.All(m => m.Key != drillMeasure))
        {
            throw s.Invalid("measureKey", "measureKey \"" + drillMeasure + "\" is not one of: " + string.Join(", ", measures.Select(m => m.Key)) + ".");
        }

        JsonTreeNode rows = JsonTreeNode.Null;
        IReadOnlyList<DrillRow>? drill = drillMeasure != null ? measures.Single(m => m.Key == drillMeasure).Rows
            : drillSignal.HasValue ? signals.SingleOrDefault(g => g.Number == drillSignal.Value)?.Rows ?? throw s.Invalid("signalNumber", "signalNumber runs from 1 to 10.")
            : null;
        if (drill != null)
        {
            List<DrillRow> page = s.Page(drill, out string? next);
            rows = Js.Obj(
                ("measureKey", Js.Str(drillMeasure)),
                ("signalNumber", drillSignal.HasValue ? JsonTreeNode.FromInt64(drillSignal.Value) : JsonTreeNode.Null),
                ("items", Arr(page.Select(r => r.Json(s)))),
                ("totalCount", JsonTreeNode.FromInt64(drill.Count)),
                ("skipToken", Js.OptStr(next)));
        }

        s.HideFromCaller("intake-request", ctx.HiddenIntakes);
        JsonTreeNode data = Js.Obj(
            ("asOf", Js.Date(s.Now)),
            ("windowDays", JsonTreeNode.FromInt64(windowDays)),
            ("windows", Arr(Enumerable.Range(0, count).Select(i => Js.Obj(("index", JsonTreeNode.FromInt64(i)), ("from", Js.Date(w.From(i))), ("to", Js.Date(w.To(i))))))),
            ("measures", Arr(measures.Select(m => m.Json(s, w, TargetOf(targets, m.Key))))),
            ("signals", Arr(signals.Select(g => g.Json(s)))),
            ("firingCount", JsonTreeNode.FromInt64(firing)),
            ("rows", rows));
        string summary = measures.Count + " measures over " + count + " windows of " + windowDays + " days; " + Count(firing, "slide-back signal fires", "slide-back signals fire") + " now."
            + " Read them in pairs: lead time with deployments on record, standard changes with failed changes, AI work accepted with decisions on time.";
        return s.Answer(data, summary);
    }

    // A measure's target from the measure-targets setting, or null when the install has not agreed one.
    private static decimal? TargetOf(JsonTreeNode? targets, string measureKey)
    {
        JsonTreeNode? value = Js.Member(targets, measureKey);
        return value != null && value.Kind == JsonTreeKind.Number && value.TryGetDecimal(out decimal target) ? target : null;
    }

    // A signal's threshold numbers: the signal-thresholds setting's numbers where it gives them, else the playbook's
    // defaults. fromConfiguration says whether the setting gave every number the signal takes.
    private static Dictionary<string, decimal> Thresholds(ToolScope s, string signalKey, out bool fromConfiguration)
    {
        var numbers = new Dictionary<string, decimal>(StringComparer.Ordinal);
        fromConfiguration = false;
        if (!MeasureSettings.ThresholdDefaults.TryGetValue(signalKey, out IReadOnlyDictionary<string, decimal>? defaults))
        {
            return numbers;
        }

        JsonTreeNode? configured = Js.Member(s.SettingJson(MeasureSettings.ThresholdsSetting), signalKey);
        int given = 0;
        foreach (KeyValuePair<string, decimal> parameter in defaults)
        {
            JsonTreeNode? value = Js.Member(configured, parameter.Key);
            if (value != null && value.Kind == JsonTreeKind.Number && value.TryGetDecimal(out decimal number))
            {
                numbers[parameter.Key] = number;
                given++;
            }
            else
            {
                numbers[parameter.Key] = parameter.Value;
            }
        }

        fromConfiguration = given == defaults.Count;
        return numbers;
    }

    // The decisions recorded about a signal (Slide Back Signal on O09), newest first: those the caller may read.
    private static List<StoredRow> SignalDecisions(MeasureContext c, string signalKey)
    {
        return c.Rows("decision").Where(d => d.Text("slideBackSignal") == signalKey && !HidesIntake(c.S, d))
            .OrderByDescending(d => d.Text("createdOn") ?? d.Text("decidedOn") ?? string.Empty, StringComparer.Ordinal).ThenByDescending(d => d.Id, StringComparer.Ordinal).ToList();
    }

    // ---- The measures ----

    // Days from an intake's submission to its application's first production deployment on record, the median per window
    // by the deployment's date. Submitted on: the submit call in the call history, else the first approval request of the
    // intake's decisions.
    private static Measure LeadTime(MeasureContext c)
    {
        var m = new Measure("lead-time", "Lead time from idea to production", "days", "flow", "pmo", "G24", "down",
            "Days from an intake's submission to its application's first production deployment on record; the median of the window.");
        var perWindow = Enumerable.Range(0, c.W.Count).Select(_ => new List<decimal>()).ToArray();
        foreach (StoredRow intake in c.Rows("intake-request"))
        {
            string? app = intake.Text("businessApplication");
            if (app == null || intake.Text("statusMeaning") is "draft" or "withdrawn" or "cancelled" or "rejected")
            {
                continue;
            }

            if (!CanReadIntake(c.S, intake))
            {
                c.HiddenIntakes++;
                continue;
            }

            DateTime? submitted = c.SubmittedOn(intake);
            DateTime? live = c.FirstProductionDeployment(app);
            if (!submitted.HasValue || !live.HasValue || live.Value < submitted.Value)
            {
                continue;
            }

            decimal days = Math.Round((decimal)(live.Value - submitted.Value).TotalDays, 1);
            int at = c.W.IndexOf(live.Value);
            if (at >= 0)
            {
                perWindow[at].Add(days);
                m.Rows.Add(new DrillRow("intake-request", intake.Id, (intake.Text("recordNumber") ?? string.Empty) + " " + (intake.Text("title") ?? string.Empty) + ", " + AppNameOf(c.S, app), "intake-request", days, "submitted " + submitted.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ", in production " + live.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
            }
        }

        for (int i = 0; i < c.W.Count; i++)
        {
            m.Values[i] = Median(perWindow[i]);
            m.Denominators[i] = perWindow[i].Count;
        }

        return m;
    }

    // Of the items each window's latest gap check run saw, the share that belong to a registered application, were claimed,
    // or were dismissed with a reason: items seen minus the claims of that run still open now.
    private static Measure ShareRegistered(MeasureContext c)
    {
        var m = new Measure("share-registered", "Share registered", "percent", "flow", "platform-admin", "G1", "up",
            "Of the items the window's last gap check run saw, the share that are registered, claimed or dismissed with a reason.");
        var runs = c.Rows("gap-check-run").Where(r => r.Text("finishedOn") != null).OrderBy(r => r.Text("finishedOn"), StringComparer.Ordinal).ToList();
        var claims = c.Rows("claim");
        StoredRow? latest = null;
        for (int i = 0; i < c.W.Count; i++)
        {
            StoredRow? run = runs.LastOrDefault(r => c.W.IndexOf(Js.ParseMoment(r.Text("finishedOn"))) == i);
            if (run == null)
            {
                continue;
            }

            latest = run;
            long seen = run.Number("itemsSeen") ?? 0;
            int open = claims.Count(cl => cl.Text("statusMeaning") == "open" && (cl.Text("lastSeenRun") == run.Id || cl.Text("gapCheckRun") == run.Id));
            if (seen > 0)
            {
                m.Numerators[i] = Math.Max(0, seen - open);
                m.Denominators[i] = seen;
            }
        }

        if (latest != null)
        {
            foreach (StoredRow claim in claims.Where(cl => cl.Text("statusMeaning") == "open" && (cl.Text("lastSeenRun") == latest.Id || cl.Text("gapCheckRun") == latest.Id)).Where(cl => CanReadClaim(c.S, cl)))
            {
                m.Rows.Add(new DrillRow("claim", claim.Id, claim.Text("labelAsSeen") ?? claim.Text("externalId") ?? claim.Id, "claim", null, "open since the gap check found it"));
            }
        }

        return m;
    }

    // Registered applications where every owner role is held, with its deputy where the role needs one, and no holder's
    // account is off: the one owner gap count (OwnerGapsOf) read the other way round. Now only.
    private static Measure OwnerCoverage(MeasureContext c)
    {
        var m = new Measure("owner-coverage", "Owner coverage", "percent", "coverage", "pmo", "G2", "up",
            "Registered applications with every owner role held by a person with a named deputy where the role needs one.");
        var apps = c.Rows("business-application").Where(a => a.Text("statusMeaning") == "registered").ToList();
        var gaps = OwnerGapsOf(c.S, apps).GroupBy(g => g.App.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        m.SetNow(apps.Count - gaps.Count, apps.Count);
        foreach (StoredRow app in apps.Where(a => gaps.ContainsKey(a.Id)).OrderBy(a => AppName(a), StringComparer.OrdinalIgnoreCase))
        {
            m.Rows.Add(new DrillRow("business-application", app.Id, AppName(app), "business-application", gaps[app.Id].Count, string.Join(", ", gaps[app.Id].Select(g => c.S.Label("owner-role", g.Role) + ": no " + g.Missing))));
        }

        return m;
    }

    // Approval requests answered within their rule's Timeout Days, by the day of the answer. A request with no timeout on
    // its rule is on time until its Expires On; with neither, it is on time.
    private static Measure DecisionsOnTime(MeasureContext c)
    {
        var m = new Measure("decisions-on-time", "Decisions on time", "percent", "flow", "pmo", "G4", "up",
            "Approval requests answered within the agreed number of days, by the day of the answer.");
        foreach (StoredRow record in c.Rows("approval-record").Where(r => !r.Flag("isConsultation") && r.Text("outcomeMeaning") is "approved" or "rejected"))
        {
            DateTime? requested = Js.ParseMoment(record.Text("requestedOn"));
            DateTime? answered = Js.ParseMoment(record.Text("respondedOn"));
            int at = c.W.IndexOf(answered);
            if (!requested.HasValue || at < 0)
            {
                continue;
            }

            long? timeout = Js.Number(c.S.ConfigRow("approval-rule", record.Text("approvalRule"))?.Columns, "timeoutDays");
            DateTime? due = timeout.HasValue ? requested.Value.Date.AddDays(timeout.Value) : Js.ParseMoment(record.Text("expiresOn"));
            bool onTime = !due.HasValue || answered!.Value.Date <= due.Value.Date;
            m.Tick(at, onTime);
            if (!onTime && record.Text("decision") is string decisionId && c.S.Rows.Get("decision", decisionId) is StoredRow decision && !HidesIntake(c.S, decision))
            {
                m.Rows.Add(new DrillRow("decision", decision.Id, NumberedLabel(c.S, decision), "decision", Math.Round((decimal)(answered!.Value - requested.Value).TotalDays, 1), "answered after " + (timeout.HasValue ? timeout.Value + " days" : "it expired")));
            }
        }

        return m;
    }

    // Changes that went through a standard change model, out of every change past draft, by its window start (or its last
    // write). Paired with the share of finished changes that failed.
    private static Measure StandardChangeShare(MeasureContext c)
    {
        var m = new Measure("standard-change-share", "Standard change share", "percent", "flow", "change-manager", "G12", "up",
            "Changes that went through a standard change model, out of all changes; read it beside the share of changes that failed.");
        m.SecondaryLabel = "Failed changes";
        var failedNum = new long[c.W.Count];
        var failedDen = new long[c.W.Count];
        foreach (StoredRow change in c.Rows("change-request").Where(r => r.Text("statusMeaning") is "submitted" or "in-progress" or "completed"))
        {
            int at = c.W.IndexOf(Js.ParseMoment(change.Text("windowStart")) ?? Js.ParseMoment(change.Text("engineCheckedOn")));
            if (at < 0)
            {
                continue;
            }

            m.Tick(at, c.IsStandard(change));
            if (!c.IsStandard(change))
            {
                m.Rows.Add(new DrillRow("change-request", change.Id, NumberedLabel(c.S, change), "change-request", null, c.S.Label("change-type", change.Text("changeType")) + " change"));
            }

            if (change.Text("statusMeaning") == "completed")
            {
                failedDen[at]++;
                if (change.Text("result") == "failed")
                {
                    failedNum[at]++;
                }
            }
        }

        int last = c.W.Count - 1;
        m.SecondaryValue = Share(failedNum[last], failedDen[last]);
        m.SecondaryNumerator = failedNum[last];
        m.SecondaryDenominator = failedDen[last];
        return m;
    }

    // Production deployments that came through the expected route with their approval (none of the four flags), out of all
    // production deployments, by the day each finished. A correction is not a deployment of its own.
    private static Measure DeploymentsOnRecord(MeasureContext c)
    {
        var m = new Measure("deployments-on-record", "Deployments on record", "percent", "flow", "change-manager", "G9", "up",
            "Production deployments that came through the expected route with their approval, out of all production deployments.");
        foreach (StoredRow record in c.ProductionDeployments())
        {
            int at = c.W.IndexOf(Js.ParseMoment(record.Text("finishedOn")));
            if (at < 0)
            {
                continue;
            }

            m.Tick(at, !IsFlagged(record));
            if (IsFlagged(record))
            {
                var flags = new List<string>();
                if (record.Flag("isOtherPath")) flags.Add("other path");
                if (record.Flag("isMissingApproval")) flags.Add("missing approval");
                if (record.Flag("isOutsideWindow")) flags.Add("outside the window");
                if (record.Flag("isAiOnlyCodeApproval")) flags.Add("AI-only code approval");
                string app = AppNameOf(c.S, record.Text("businessApplication"));
                m.Rows.Add(new DrillRow("deployment-record", record.Id, app + ", " + (Js.ParseMoment(record.Text("finishedOn"))?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty), "deployment-record", null, string.Join(", ", flags)));
            }
        }

        return m;
    }

    // Required documents that are current, out of all required documents of registered applications. Now only.
    private static Measure DocumentsCurrent(MeasureContext c)
    {
        var m = new Measure("documents-current", "Documents current", "percent", "coverage", "platform-admin", "G20", "up",
            "Required documents that are current, out of all required documents for registered applications.");
        (int current, int all, List<DrillRow> rows) = c.Documents();
        m.SetNow(current, all);
        m.Rows.AddRange(rows);
        return m;
    }

    // AI drafts a person took forward, out of AI drafts taken forward (by a person or, where the Tool Policy lets it, by the
    // AI client), by the day each was drafted. Paired with the drafts left waiting past the draft-expiry-days setting.
    private static Measure AiWorkAccepted(MeasureContext c)
    {
        int expiry = (int)(c.S.SettingNumber("draft-expiry-days") ?? 30);
        var m = new Measure("ai-work-accepted", "AI work accepted", "percent", "flow", "ai-lead", "G17", "up",
            "AI drafts a person took forward, out of AI drafts taken forward; beside it, the AI drafts left waiting longer than " + expiry + " days.");
        m.SecondaryLabel = "AI drafts waiting more than " + expiry + " days";
        int stale = 0;
        foreach (AiDraft draft in c.AiDrafts())
        {
            int at = c.W.IndexOf(draft.DraftedOn);
            if (draft.Waiting && draft.DraftedOn.HasValue && (c.S.Now - draft.DraftedOn.Value).TotalDays > expiry)
            {
                stale++;
                m.Rows.Add(new DrillRow(draft.Row.Entity, draft.Row.Id, RecordLabel(c.S, draft.Row), draft.Row.Entity, null, "waiting since " + draft.DraftedOn.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
            }

            if (at < 0 || !(draft.ByPerson || draft.ByAi))
            {
                continue;
            }

            m.Tick(at, draft.ByPerson);
        }

        m.SecondaryValue = stale;
        m.SecondaryUnit = "count";
        return m;
    }

    // ---- The ten slide-back signals (chapter 17) ----

    private static List<Signal> Signals(MeasureContext c)
    {
        ToolScope s = c.S;
        int last = c.W.Count - 1;
        var signals = new List<Signal>();

        // 1. Lower-tier normal changes wait for a meeting; the standard share falls; the emergency share rises.
        {
            Dictionary<string, decimal> t1 = Thresholds(s, "changes-wait-for-a-meeting", out bool configured1);
            decimal emergencyShareLimit = t1["emergencySharePercent"];
            decimal standardShareDropPoints = t1["standardShareDropPoints"];
            var g = new Signal(1, "changes-wait-for-a-meeting", "Changes wait for a meeting; the standard share falls; the emergency share rises", "change-manager", 9,
                "Emergency changes above " + Plain(emergencyShareLimit) + "% of the window's changes, or the standard share down " + Plain(standardShareDropPoints) + " points on the window before") { FromConfiguration = configured1 };
            var counts = new (int All, int Standard, int Emergency)[c.W.Count];
            foreach (StoredRow change in c.Rows("change-request").Where(r => r.Text("statusMeaning") is "submitted" or "in-progress" or "completed"))
            {
                int at = c.W.IndexOf(Js.ParseMoment(change.Text("windowStart")) ?? Js.ParseMoment(change.Text("engineCheckedOn")));
                if (at < 0)
                {
                    continue;
                }

                counts[at].All++;
                if (c.IsStandard(change)) counts[at].Standard++;
                if (c.IsEmergency(change))
                {
                    counts[at].Emergency++;
                    if (at == last)
                    {
                        g.Rows.Add(new DrillRow("change-request", change.Id, NumberedLabel(s, change), "change-request", null, "emergency change"));
                    }
                }
            }

            decimal? emergency = Share(counts[last].Emergency, counts[last].All);
            decimal? standardNow = Share(counts[last].Standard, counts[last].All);
            decimal? standardBefore = last > 0 ? Share(counts[last - 1].Standard, counts[last - 1].All) : null;
            g.Value = emergency;
            g.Unit = "percent";
            g.Firing = counts[last].All == 0 ? false : emergency > emergencyShareLimit || (standardNow.HasValue && standardBefore.HasValue && standardBefore.Value - standardNow.Value >= standardShareDropPoints);
            signals.Add(g);
        }

        // 2. Environment requests by ticket rise; apps built in the default environment. Read outside the product.
        signals.Add(new Signal(2, "environments-by-ticket", "Environment requests by ticket rise; apps are built in the default environment", "platform-admin", 20,
            "Read in the ticket tool and the Power Platform inventory, outside the product") { ReadBy = "outside" });

        // 3. Inventory exported to a spreadsheet; the last gap check older than its cadence; open claims grow.
        {
            Dictionary<string, decimal> t3 = Thresholds(s, "gap-check-stale", out bool configured3);
            decimal staleGapCheckDays = t3["days"];
            var g = new Signal(3, "gap-check-stale", "The last gap check is older than its cadence, or open claims grow", "platform-admin", 5,
                "No gap check in " + Plain(staleGapCheckDays) + " days, or more claims opened than settled in the window") { FromConfiguration = configured3 };
            DateTime? lastRun = c.Rows("gap-check-run").Select(r => Js.ParseMoment(r.Text("finishedOn"))).Where(d => d.HasValue).Select(d => d.GetValueOrDefault()).OrderByDescending(d => d).Cast<DateTime?>().FirstOrDefault();
            // Opened: every claim a run opened in the window, whatever it is now; settled: every claim claimed or dismissed
            // in the window, by its Claimed On, or by the moment of its last dismiss_claim call (the claim keeps no dismissal
            // date, and a later run's sighting writes the row again). Wave 4b QA: "0 claims opened" on the day a gap check
            // opened 4, because a claim settled the same day dropped out of "opened".
            var claims = c.Rows("claim");
            var dismissedOn = c.Audit
                .Where(e => e.Text("operation") == "composed-call" && e.Text("entityName") == "dismiss_claim" && e.Text("rowId") != null)
                .GroupBy(e => e.Text("rowId")!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(e => Js.ParseMoment(e.Text("occurredOn"))).Max(), StringComparer.OrdinalIgnoreCase);
            int opened = claims.Count(cl => c.W.IndexOf(Js.ParseMoment(cl.Text("createdOn")) ?? Js.ParseMoment(cl.Text("lastSeenOn"))) == last);
            int settled = claims.Count(cl => cl.Text("statusMeaning") is "claimed" or "dismissed"
                && c.W.IndexOf(cl.Text("statusMeaning") == "claimed"
                    ? Js.ParseMoment(cl.Text("claimedOn")) ?? Js.ParseMoment(cl.Text("engineCheckedOn"))
                    : (dismissedOn.TryGetValue(cl.Id, out DateTime? dismissed) ? dismissed : null) ?? Js.ParseMoment(cl.Text("engineCheckedOn"))) == last);
            int age = lastRun.HasValue ? (int)(s.Now.Date - lastRun.Value.Date).TotalDays : -1;
            g.Value = lastRun.HasValue ? age : null;
            g.Unit = "days";
            g.Firing = !lastRun.HasValue || age > staleGapCheckDays || opened > settled;
            g.Note = !lastRun.HasValue ? "No gap check has run." : "Last gap check " + age + " days ago; " + Count(opened, "claim", "claims") + " opened and " + settled + " settled in the window.";
            foreach (StoredRow claim in claims.Where(cl => cl.Text("statusMeaning") == "open").Where(cl => CanReadClaim(s, cl)))
            {
                g.Rows.Add(new DrillRow("claim", claim.Id, claim.Text("labelAsSeen") ?? claim.Text("externalId") ?? claim.Id, "claim", null, "open claim"));
            }

            signals.Add(g);
        }

        // 4. The first register entry comes at go-live or as a gap check claim.
        {
            var g = new Signal(4, "registered-late", "Apps enter the register first as a gap check claim", "platform-admin", 3,
                "Any claim taken in the window: an app that ran before it was registered");
            foreach (StoredRow claim in c.Rows("claim").Where(cl => cl.Text("statusMeaning") == "claimed" && c.W.IndexOf(Js.ParseMoment(cl.Text("claimedOn"))) == last).Where(cl => CanReadClaim(s, cl)))
            {
                g.Rows.Add(new DrillRow("claim", claim.Id, claim.Text("labelAsSeen") ?? claim.Text("externalId") ?? claim.Id, "claim", null, "claimed " + (Js.ParseMoment(claim.Text("claimedOn"))?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty)));
            }

            g.Value = g.Rows.Count;
            g.Unit = "count";
            g.Firing = g.Rows.Count > 0;
            signals.Add(g);
        }

        // 5. A standards document changes but the configuration does not.
        {
            long reviewDays = s.SettingNumber("configuration-review-days") ?? 180;
            var g = new Signal(5, "configuration-not-kept", "The configuration is not kept with the standards", "configuration-admin", 1,
                "No configuration version in " + reviewDays + " days (the configuration-review-days setting)") { FromConfiguration = s.SettingNumber("configuration-review-days").HasValue };
            DateTime? published = c.Rows("configuration-set-version").Where(v => v.Text("configurationSet") == s.Call.Snapshot.ConfigurationSetKey)
                .Select(v => Js.ParseMoment(v.Text("publishedOn"))).Where(d => d.HasValue).Select(d => d.GetValueOrDefault()).OrderByDescending(d => d).Cast<DateTime?>().FirstOrDefault();
            int age = published.HasValue ? (int)(s.Now.Date - published.Value.Date).TotalDays : 0;
            g.Value = published.HasValue ? age : null;
            g.Unit = "days";
            g.Firing = published.HasValue && age > reviewDays;
            g.Note = published.HasValue ? "The version in force was published " + age + " days ago." : "No version carries a publish date.";
            signals.Add(g);
        }

        // 6. Unsanctioned AI tools; "no AI use" apps built with AI.
        {
            var g = new Signal(6, "unsanctioned-ai", "AI work outside the sanctioned clients, or apps that say no AI use but were built with AI", "ai-lead", 18,
                "Any refused call from a client the Known Client list does not hold, or any app with Uses AI no that holds AI-created rows");
            var known = new HashSet<string>(s.ActiveRows("known-client").Select(r => Js.Text(r.Columns, "label") ?? r.KeyPath), StringComparer.OrdinalIgnoreCase);
            if (known.Count > 0)
            {
                foreach (StoredRow entry in c.Audit.Where(e => e.Text("operation") == "call-refused" && c.W.IndexOf(Js.ParseMoment(e.Text("occurredOn"))) == last && e.Text("clientApp") is string client && !known.Contains(client)))
                {
                    g.Rows.Add(new DrillRow("audit-entry", entry.Id, "Refused call from " + entry.Text("clientApp"), null, null, entry.Text("entityName") ?? "a tool"));
                }
            }

            foreach (StoredRow app in c.Rows("business-application").Where(a => a.Text("statusMeaning") != "archived" && a.Column("usesAi") is JsonTreeNode uses && uses.Kind == JsonTreeKind.False))
            {
                if (app.Flag("isAiCreated") || c.AppsWithAiDrafts().Contains(app.Id))
                {
                    g.Rows.Add(new DrillRow("business-application", app.Id, AppName(app), "business-application", null, "Uses AI is no, and AI clients drafted its records"));
                }
            }

            g.Value = g.Rows.Count;
            g.Unit = "count";
            g.Firing = g.Rows.Count > 0;
            signals.Add(g);
        }

        // 7. A project to sync, copy or rebuild a Microsoft tool's job. Read outside the product.
        signals.Add(new Signal(7, "tool-rebuilt", "A project syncs, copies or rebuilds a Microsoft tool's job", "architect", 22,
            "Read in architecture decisions and project requests") { ReadBy = "outside" });

        // 8. An approval with no named human approver; a request for an "approve" tool.
        {
            var g = new Signal(8, "no-human-approver", "An approval with no named human approver, or an AI client asks to approve", "ai-lead", 11,
                "Any AI answer to an approval request, or any refused AI call to approve, accept or answer, in the window");
            foreach (StoredRow record in c.Rows("approval-record").Where(r => AiActorKinds.Contains(r.Text("approverActorMeaning") ?? r.Text("approverActorKind") ?? string.Empty, StringComparer.Ordinal) && c.W.IndexOf(Js.ParseMoment(r.Text("respondedOn"))) == last))
            {
                g.Rows.Add(new DrillRow("approval-record", record.Id, "AI answer on an approval request", "decision", null, "never counted") { UrlEntityId = record.Text("decision") });
            }

            foreach (StoredRow entry in c.Audit.Where(e => e.Text("operation") == "call-refused" && AiActorKinds.Contains(e.Text("actorKind") ?? string.Empty, StringComparer.Ordinal) && c.W.IndexOf(Js.ParseMoment(e.Text("occurredOn"))) == last))
            {
                string tool = entry.Text("entityName") ?? string.Empty;
                if (tool.Contains("approve") || tool.Contains("accept") || tool == "answer_approval")
                {
                    g.Rows.Add(new DrillRow("audit-entry", entry.Id, "Refused AI call to " + tool, null, null, entry.Text("clientApp") ?? "an AI client"));
                }
            }

            g.Value = g.Rows.Count;
            g.Unit = "count";
            g.Firing = g.Rows.Count > 0;
            signals.Add(g);
        }

        // 9. Missing or stale documents rise; AI "current" claims stay unconfirmed.
        {
            Dictionary<string, decimal> t9 = Thresholds(s, "documents-slipping", out bool configured9);
            decimal documentsSlippingLimit = t9["percent"];
            var g = new Signal(9, "documents-slipping", "Missing or stale documents rise, or AI \"current\" claims stay unconfirmed", "pmo", 12,
                "More than " + Plain(documentsSlippingLimit) + "% of required documents missing or stale, or any document an AI client called current that no person confirmed") { FromConfiguration = configured9 };
            (_, int all, List<DrillRow> rows) = c.Documents();
            decimal? slipping = all == 0 ? null : Share(rows.Count, all);
            var unconfirmed = c.Rows("document-link").Where(d => d.Flag("isAiCreated") && d.Text("statusMeaning") == "current" && d.Text("lastCheckedOn") == null).ToList();
            g.Rows.AddRange(rows);
            g.Rows.AddRange(unconfirmed.Select(d => new DrillRow("document-link", d.Id, AppNameOf(s, d.Text("businessApplication")) + ", " + s.Label("document-type", d.Text("documentType")), "document-link", null, "an AI client called it current; no person confirmed it")));
            g.Value = slipping;
            g.Unit = "percent";
            g.Firing = slipping > documentsSlippingLimit || unconfirmed.Count > 0;
            signals.Add(g);
        }

        // 10. Direct role assignments; expired workload grants; a pipeline recording as a person.
        {
            var g = new Signal(10, "access-drift", "Expired workload grants, or a pipeline records deployments as a person", "security-reviewer", 13,
                "Any expired workload grant still in force, or any deployment in the window recorded on the pipeline route by a person; direct role assignments are read by the role binding check");
            foreach (SnapshotRow grant in s.ActiveRows("workload-grant").Where(gr => !(Js.Moment(gr.Columns, "expiresOn") is DateTime e) || s.Now >= e))
            {
                g.Rows.Add(new DrillRow("workload-grant", grant.KeyPath, "Workload grant " + grant.KeyPath + " for " + s.DisplayLabel(Js.Text(grant.Columns, "principalObjectId") ?? string.Empty), null, null, "expired"));
            }

            foreach (StoredRow record in c.Rows("deployment-record").Where(r => r.Text("performedByActorKind") == "person" && c.IsPipelineRoute(r.Text("deploymentRoute")) && c.W.IndexOf(Js.ParseMoment(r.Text("finishedOn"))) == last))
            {
                g.Rows.Add(new DrillRow("deployment-record", record.Id, AppNameOf(s, record.Text("businessApplication")), "deployment-record", null, "recorded on the pipeline route by a person"));
            }

            g.Value = g.Rows.Count;
            g.Unit = "count";
            g.Firing = g.Rows.Count > 0;
            g.Note = "Direct role assignments need Microsoft Graph: run the role binding check (check_role_bindings).";
            signals.Add(g);
        }

        foreach (Signal signal in signals)
        {
            signal.Decisions.AddRange(SignalDecisions(c, signal.Key));
        }

        return signals;
    }

    // A threshold number as the sentence shows it: 20, or 12.5.
    private static string Plain(decimal value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    // ---- Shapes ----

    private static decimal? Share(long numerator, long denominator)
    {
        return denominator <= 0 ? null : Math.Round(100m * numerator / denominator, 1);
    }

    private static decimal? Median(List<decimal> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var sorted = values.OrderBy(v => v).ToList();
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : Math.Round((sorted[mid - 1] + sorted[mid]) / 2m, 1);
    }

    private static JsonTreeNode Num(decimal? value)
    {
        return value.HasValue ? JsonTreeNode.FromDecimal(value.Value) : JsonTreeNode.Null;
    }

    // Equal windows ending today: window count - 1 ends today, window 0 is the oldest.
    private sealed class MeasureWindows
    {
        private readonly DateTime _today;

        public MeasureWindows(DateTime today, int days, int count)
        {
            _today = today.Date;
            Days = days;
            Count = count;
        }

        public int Days { get; }

        public int Count { get; }

        public DateTime To(int index)
        {
            return _today.AddDays(-(long)(Count - 1 - index) * Days);
        }

        public DateTime From(int index)
        {
            return To(index).AddDays(1 - Days);
        }

        public int IndexOf(DateTime? moment)
        {
            if (!moment.HasValue)
            {
                return -1;
            }

            DateTime day = moment.Value.Date;
            if (day > _today || day < From(0))
            {
                return -1;
            }

            return Count - 1 - (int)((_today - day).TotalDays / Days);
        }
    }

    // The rows every measure reads, loaded once per call.
    private sealed class MeasureContext
    {
        private readonly Dictionary<string, IReadOnlyList<StoredRow>> _rows = new(StringComparer.Ordinal);
        private Dictionary<string, List<StoredRow>>? _deploymentsByApp;
        private List<AiDraft>? _drafts;
        private (int, int, List<DrillRow>)? _documents;
        private Dictionary<string, DateTime>? _submits;
        private HashSet<string>? _appsWithAiDrafts;

        public MeasureContext(ToolScope s, MeasureWindows w)
        {
            S = s;
            W = w;
        }

        public ToolScope S { get; }

        public MeasureWindows W { get; }

        public int HiddenIntakes { get; set; }

        public IReadOnlyList<StoredRow> Audit => Rows("audit-entry");

        public IReadOnlyList<StoredRow> Rows(string entity)
        {
            if (!_rows.TryGetValue(entity, out IReadOnlyList<StoredRow>? rows))
            {
                _rows[entity] = rows = S.Rows.Query(RowQuery.For(entity));
            }

            return rows;
        }

        public bool IsStandard(StoredRow change)
        {
            return Js.Flag(S.ConfigRow("change-type", change.Text("changeType"))?.Columns, "requiresStandardModel") == true || change.Text("standardChangeModel") != null;
        }

        public bool IsEmergency(StoredRow change)
        {
            return Js.Flag(S.ConfigRow("change-type", change.Text("changeType"))?.Columns, "allowsAuthorizationAfter") == true;
        }

        public bool IsPipelineRoute(string? routeKey)
        {
            string? meaning = Js.Text(S.ConfigRow("deployment-route", routeKey)?.Columns, "engineMeaning") ?? Js.Text(S.ConfigRow("deployment-route", routeKey)?.Columns, "routeMeaning");
            return meaning == "pipeline" || (meaning == null && routeKey != null && routeKey.Contains("pipeline"));
        }

        // Production deployments on record: a target whose stage is production, not a correction.
        public IEnumerable<StoredRow> ProductionDeployments()
        {
            var production = new HashSet<string>(S.ActiveRows("target-stage").Where(t => Js.Flag(t.Columns, "isProduction") == true).Select(t => t.KeyPath), StringComparer.Ordinal);
            var targets = new HashSet<string>(Rows("deployment-target").Where(t => production.Contains(t.Text("targetStage") ?? string.Empty)).Select(t => t.Id), StringComparer.Ordinal);
            return Rows("deployment-record").Where(r => r.Text("correctsRecord") == null && targets.Contains(r.Text("deploymentTarget") ?? string.Empty));
        }

        public DateTime? FirstProductionDeployment(string appId)
        {
            _deploymentsByApp ??= ProductionDeployments().Where(r => r.Text("resultMeaning") is "succeeded" or "partly-succeeded")
                .GroupBy(r => r.Text("businessApplication") ?? string.Empty, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            return _deploymentsByApp.TryGetValue(appId, out List<StoredRow>? records)
                ? records.Select(r => Js.ParseMoment(r.Text("finishedOn"))).Where(d => d.HasValue).Select(d => d.GetValueOrDefault()).OrderBy(d => d).Cast<DateTime?>().FirstOrDefault()
                : null;
        }

        public DateTime? SubmittedOn(StoredRow intake)
        {
            _submits ??= Audit.Where(e => e.Text("operation") == "composed-call" && e.Text("entityName") == "submit_record" && e.Text("rowId") != null && Js.ParseMoment(e.Text("occurredOn")).HasValue)
                .GroupBy(e => e.Text("rowId")!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Min(e => Js.ParseMoment(e.Text("occurredOn")).GetValueOrDefault()), StringComparer.OrdinalIgnoreCase);
            if (_submits.TryGetValue(intake.Id, out DateTime submit))
            {
                return submit;
            }

            var decisions = new HashSet<string>(Rows("decision").Where(d => Same(d.Text("subjectIntakeRequest"), intake.Id)).Select(d => d.Id), StringComparer.OrdinalIgnoreCase);
            return Rows("approval-record").Where(r => decisions.Contains(r.Text("decision") ?? string.Empty))
                .Select(r => Js.ParseMoment(r.Text("requestedOn"))).Where(d => d.HasValue).Select(d => d.GetValueOrDefault()).OrderBy(d => d).Cast<DateTime?>().FirstOrDefault();
        }

        // The required documents of registered applications: how many are current, how many there are, and the missing and
        // stale ones as drill rows.
        public (int Current, int All, List<DrillRow> Rows) Documents()
        {
            if (_documents.HasValue)
            {
                return _documents.Value;
            }

            int current = 0;
            int all = 0;
            var rows = new List<DrillRow>();
            foreach (StoredRow app in Rows("business-application").Where(a => a.Text("statusMeaning") == "registered"))
            {
                foreach (DocumentRow doc in DocumentStatus(S, app))
                {
                    all++;
                    if (doc.Meaning == "current")
                    {
                        current++;
                    }
                    else if (doc.Meaning is "missing" or "stale")
                    {
                        rows.Add(new DrillRow(doc.DocumentLinkId != null ? "document-link" : "business-application", doc.DocumentLinkId ?? app.Id, AppName(app) + ", " + S.Label("document-type", doc.DocumentTypeKey), doc.DocumentLinkId != null ? "document-link" : "business-application", null, doc.Meaning));
                    }
                }
            }

            _documents = (current, all, rows);
            return (current, all, rows);
        }

        // The applications whose records an AI client drafted.
        public HashSet<string> AppsWithAiDrafts()
        {
            _appsWithAiDrafts ??= new HashSet<string>(
                AiDrafts().Select(d => d.Row.Entity == "business-application" ? d.Row.Id : ApplicationOf(S, d.Row)).Where(id => id != null).Select(id => id!),
                StringComparer.OrdinalIgnoreCase);
            return _appsWithAiDrafts;
        }

        // The rows AI clients drafted that wait for, or went through, a person: who took each forward, from the call history
        // as get_ai_activity reads it (a submit call), else the last stamp.
        public List<AiDraft> AiDrafts()
        {
            if (_drafts != null)
            {
                return _drafts;
            }

            var submits = Audit.Where(e => e.Text("operation") == "composed-call" && SubmitTools.Contains(e.Text("entityName") ?? string.Empty, StringComparer.Ordinal) && e.Text("rowId") != null)
                .GroupBy(e => e.Text("rowId")!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderBy(e => e.Text("occurredOn") ?? string.Empty, StringComparer.Ordinal).First(), StringComparer.OrdinalIgnoreCase);
            var created = Audit.Where(e => e.Text("operation") == "composed-call" && AiActorKinds.Contains(e.Text("actorKind") ?? string.Empty, StringComparer.Ordinal) && e.Text("rowId") != null)
                .GroupBy(e => e.Text("rowId")!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(e => Js.ParseMoment(e.Text("occurredOn"))).Where(d => d.HasValue).Select(d => d.GetValueOrDefault()).DefaultIfEmpty(DateTime.MinValue).Min(), StringComparer.OrdinalIgnoreCase);
            _drafts = new List<AiDraft>();
            foreach (string entity in DraftableEntities)
            {
                foreach (StoredRow row in Rows(entity).Where(AiCreatedRow))
                {
                    if (entity == "intake-request" && !CanReadIntake(S, row))
                    {
                        continue;
                    }

                    string meaning = row.Text("statusMeaning") ?? row.Text("draftStatus") ?? string.Empty;
                    bool hasApprovals = entity == "decision" && Rows("approval-record").Any(r => Same(r.Text("decision"), row.Id));
                    bool waiting = meaning == "draft" || (meaning == "proposed" && !hasApprovals);
                    submits.TryGetValue(row.Id, out StoredRow? submit);
                    string? forwardKind = submit?.Text("actorKind") ?? (!waiting && meaning is not ("withdrawn" or "discarded" or "cancelled") && row.Text("performedByActorKind") == "person" ? "person" : null);
                    DateTime? draftedOn = created.TryGetValue(row.Id, out DateTime first) && first != DateTime.MinValue ? first : Js.ParseMoment(row.Text("createdOn")) ?? Js.ParseMoment(row.Text("engineCheckedOn"));
                    _drafts.Add(new AiDraft(row, draftedOn, waiting, forwardKind == "person", forwardKind != null && AiActorKinds.Contains(forwardKind, StringComparer.Ordinal)));
                }
            }

            return _drafts;
        }
    }

    private sealed class AiDraft
    {
        public AiDraft(StoredRow row, DateTime? draftedOn, bool waiting, bool byPerson, bool byAi)
        {
            Row = row;
            DraftedOn = draftedOn;
            Waiting = waiting;
            ByPerson = byPerson;
            ByAi = byAi;
        }

        public StoredRow Row { get; }

        public DateTime? DraftedOn { get; }

        public bool Waiting { get; }

        public bool ByPerson { get; }

        public bool ByAi { get; }
    }

    // One row behind a measure or a signal: what it is, a label, a link, and the number it adds.
    private sealed class DrillRow
    {
        public DrillRow(string entity, string id, string label, string? urlEntity, decimal? value, string? note)
        {
            Entity = entity;
            Id = id;
            Label = label;
            UrlEntity = urlEntity;
            Value = value;
            Note = note;
        }

        public string Entity { get; }

        public string Id { get; }

        public string Label { get; }

        public string? UrlEntity { get; }

        // The record the link opens when it is not the row itself (an approval record opens its decision).
        public string? UrlEntityId { get; set; }

        public decimal? Value { get; }

        public string? Note { get; }

        public JsonTreeNode Json(ToolScope s)
        {
            return Js.Obj(
                ("entity", JsonTreeNode.FromString(Entity)),
                ("recordId", JsonTreeNode.FromString(Id)),
                ("label", JsonTreeNode.FromString(Label.Trim())),
                ("value", Num(Value)),
                ("note", Js.Str(Note)),
                ("recordUrl", UrlEntity == null ? JsonTreeNode.Null : JsonTreeNode.FromString(s.RecordUrl(UrlEntity, UrlEntityId ?? Id))));
        }
    }

    private sealed class Measure
    {
        public Measure(string key, string label, string unit, string kind, string watcherRole, string guideline, string healthy, string definition)
        {
            Key = key;
            Label = label;
            Unit = unit;
            Kind = kind;
            WatcherRole = watcherRole;
            Guideline = guideline;
            Healthy = healthy;
            Definition = definition;
        }

        public string Key { get; }

        public string Label { get; }

        public string Unit { get; }

        public string Kind { get; }

        public string WatcherRole { get; }

        public string Guideline { get; }

        public string Healthy { get; }

        public string Definition { get; }

        // Per window: a value of its own (lead time), or a numerator over a denominator (a share).
        public Dictionary<int, decimal?> Values { get; } = new();

        public Dictionary<int, long> Numerators { get; } = new();

        public Dictionary<int, long> Denominators { get; } = new();

        // A count that is not per window (the slide-back signals firing now).
        public decimal? Now { get; set; }

        public string? SecondaryLabel { get; set; }

        public decimal? SecondaryValue { get; set; }

        public long? SecondaryNumerator { get; set; }

        public long? SecondaryDenominator { get; set; }

        public string SecondaryUnit { get; set; } = "percent";

        public List<DrillRow> Rows { get; } = new();

        // One event in a window: it counts in the denominator, and in the numerator when it hits.
        public void Tick(int at, bool hit)
        {
            Denominators[at] = (Denominators.TryGetValue(at, out long den) ? den : 0) + 1;
            if (hit)
            {
                Numerators[at] = (Numerators.TryGetValue(at, out long num) ? num : 0) + 1;
            }
        }

        // A coverage measure read now: the newest window only.
        public void SetNow(long numerator, long denominator)
        {
            NowNumerator = numerator;
            NowDenominator = denominator;
        }

        private long? NowNumerator { get; set; }

        private long? NowDenominator { get; set; }

        private (decimal? Value, long? Numerator, long? Denominator) At(int index, int last)
        {
            if (Now.HasValue || NowDenominator.HasValue)
            {
                return index == last ? (Now ?? Share(NowNumerator ?? 0, NowDenominator ?? 0), NowNumerator, NowDenominator) : (null, null, null);
            }

            if (Unit == "days")
            {
                Values.TryGetValue(index, out decimal? v);
                return (v, null, Denominators.TryGetValue(index, out long n) ? n : 0);
            }

            long num = Numerators.TryGetValue(index, out long a) ? a : 0;
            long den = Denominators.TryGetValue(index, out long b) ? b : 0;
            return (Share(num, den), num, den);
        }

        public JsonTreeNode Json(ToolScope s, MeasureWindows w, decimal? target)
        {
            int last = w.Count - 1;
            var trend = Enumerable.Range(0, w.Count).Select(i => (Index: i, Point: At(i, last))).ToList();
            var baseline = trend.FirstOrDefault(t => t.Point.Value.HasValue);
            (decimal? value, long? numerator, long? denominator) = At(last, last);
            return Js.Obj(
                ("key", JsonTreeNode.FromString(Key)),
                ("label", JsonTreeNode.FromString(Label)),
                ("definition", JsonTreeNode.FromString(Definition)),
                ("kind", JsonTreeNode.FromString(Kind)),
                ("unit", JsonTreeNode.FromString(Unit)),
                ("watcher", s.LabelledKey("role", WatcherRole)),
                ("guideline", JsonTreeNode.FromString(Guideline)),
                ("healthyDirection", JsonTreeNode.FromString(Healthy)),
                ("value", Num(value)),
                ("numerator", numerator.HasValue ? JsonTreeNode.FromInt64(numerator.Value) : JsonTreeNode.Null),
                ("denominator", denominator.HasValue ? JsonTreeNode.FromInt64(denominator.Value) : JsonTreeNode.Null),
                ("trend", Arr(trend.Select(t => Js.Obj(
                    ("index", JsonTreeNode.FromInt64(t.Index)),
                    ("value", Num(t.Point.Value)),
                    ("numerator", t.Point.Numerator.HasValue ? JsonTreeNode.FromInt64(t.Point.Numerator.Value) : JsonTreeNode.Null),
                    ("denominator", t.Point.Denominator.HasValue ? JsonTreeNode.FromInt64(t.Point.Denominator.Value) : JsonTreeNode.Null))))),
                ("baseline", baseline.Point.Value.HasValue ? Js.Obj(("index", JsonTreeNode.FromInt64(baseline.Index)), ("value", Num(baseline.Point.Value))) : JsonTreeNode.Null),
                ("target", Num(target)),
                ("targetSource", JsonTreeNode.FromString(target.HasValue ? "configuration" : "not-set")),
                ("targetMet", !target.HasValue || !value.HasValue ? JsonTreeNode.Null : JsonTreeNode.FromBoolean(Healthy is "down" or "quiet" ? value.Value <= target.Value : value.Value >= target.Value)),
                ("secondary", SecondaryLabel == null ? JsonTreeNode.Null : Js.Obj(
                    ("label", JsonTreeNode.FromString(SecondaryLabel)),
                    ("unit", JsonTreeNode.FromString(SecondaryUnit)),
                    ("value", Num(SecondaryValue)),
                    ("numerator", SecondaryNumerator.HasValue ? JsonTreeNode.FromInt64(SecondaryNumerator.Value) : JsonTreeNode.Null),
                    ("denominator", SecondaryDenominator.HasValue ? JsonTreeNode.FromInt64(SecondaryDenominator.Value) : JsonTreeNode.Null))),
                ("rowCount", JsonTreeNode.FromInt64(Rows.Count)));
        }
    }

    private sealed class Signal
    {
        public Signal(int number, string key, string label, string watcherRole, int chapter, string threshold)
        {
            Number = number;
            Key = key;
            Label = label;
            WatcherRole = watcherRole;
            Chapter = chapter;
            Threshold = threshold;
        }

        public int Number { get; }

        public string Key { get; }

        public string Label { get; }

        public string WatcherRole { get; }

        public int Chapter { get; }

        public string Threshold { get; }

        public string ReadBy { get; set; } = "product";

        public decimal? Value { get; set; }

        public string? Unit { get; set; }

        public bool? Firing { get; set; }

        public string? Note { get; set; }

        // True when the configuration gives this signal's threshold (signal-thresholds, or configuration-review-days).
        public bool FromConfiguration { get; set; }

        public List<DrillRow> Rows { get; } = new();

        // The decisions recorded about this signal, newest first (Slide Back Signal on O09).
        public List<StoredRow> Decisions { get; } = new();

        public JsonTreeNode Json(ToolScope s)
        {
            return Js.Obj(
                ("number", JsonTreeNode.FromInt64(Number)),
                ("key", JsonTreeNode.FromString(Key)),
                ("label", JsonTreeNode.FromString(Label)),
                ("watcher", s.LabelledKey("role", WatcherRole)),
                ("chapter", JsonTreeNode.FromInt64(Chapter)),
                ("threshold", JsonTreeNode.FromString(Threshold)),
                ("thresholdSource", JsonTreeNode.FromString(FromConfiguration ? "configuration" : "product-default")),
                ("readBy", JsonTreeNode.FromString(ReadBy)),
                ("value", Num(Value)),
                ("unit", Js.Str(Unit)),
                ("firing", Firing.HasValue ? JsonTreeNode.FromBoolean(Firing.Value) : JsonTreeNode.Null),
                ("note", Js.Str(Note ?? (ReadBy == "outside" ? "The product does not hold these records; read them where the threshold says." : null))),
                ("rowCount", JsonTreeNode.FromInt64(Rows.Count)),
                ("decisionCount", JsonTreeNode.FromInt64(Decisions.Count)),
                ("decisions", Arr(Decisions.Take(SignalDecisionsShown).Select(d => Js.Obj(
                    ("decisionId", JsonTreeNode.FromString(d.Id)),
                    ("recordNumber", Js.Str(d.Text("recordNumber"))),
                    ("statement", JsonTreeNode.FromString(d.Text("statement") ?? string.Empty)),
                    ("decisionType", s.LabelledKey("decision-type", d.Text("decisionType") ?? "unknown")),
                    ("status", s.StatusValue(d)),
                    ("decidedOn", ToolScope.MomentOrNull(d.Text("decidedOn"))),
                    ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("decision", d.Id))))))));
        }
    }
}
