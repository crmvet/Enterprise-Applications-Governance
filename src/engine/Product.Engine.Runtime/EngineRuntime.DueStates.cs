using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Operations;
using Product.Engine.Core.Reasons;
using Product.Engine.Runtime.Callers;
using Product.Engine.Runtime.Facts;
using Product.Engine.Runtime.Ports;
using Product.Engine.Runtime.Snapshots;

namespace Product.Engine.Runtime;

// What started one due-state refresh and how it runs (document 23, S2; document 25, B25-18).
public sealed class DueStateRefreshRequest
{
    // The host's timer (the Azure Functions timer trigger, the schedule a host setting).
    public const string TimerTrigger = "timer";

    // A platform admin's "run it now" on the web app (run_due_state_refresh).
    public const string PersonTrigger = "person";

    // The local server's refresh-due-states command.
    public const string CommandTrigger = "command";

    // The most items one run handles; the rest wait for the next run, which a rerun picks up because every item is
    // idempotent.
    public const int DefaultMaxItems = 500;

    private static readonly string[] Triggers = { TimerTrigger, PersonTrigger, CommandTrigger };

    public DueStateRefreshRequest(string trigger, bool dryRun = false, int maxItems = DefaultMaxItems)
    {
        if (Array.IndexOf(Triggers, trigger) < 0)
        {
            throw new ArgumentException("A refresh is started by timer, person or command.", nameof(trigger));
        }

        if (maxItems < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxItems), "A run handles at least one item.");
        }

        Trigger = trigger;
        DryRun = dryRun;
        MaxItems = maxItems;
    }

    public string Trigger { get; }

    // validateOnly: every item runs its rules and writes, then rolls back. The report says what would happen.
    public bool DryRun { get; }

    public int MaxItems { get; }

    public string? CorrelationId { get; set; }
}

public sealed partial class EngineRuntime
{
    // The operations of the Audit Entries the refresh writes (document 23, S2 step 5: "operation reminder sent").
    public const string ReminderSentOperation = "reminder-sent";
    public const string FlagNoticeSentOperation = "flag-notice-sent";
    public const string DueStateRefreshedOperation = "due-state-refreshed";
    public const string DueStateRunOperation = "due-state-refresh-run";

    // How far back a flagged deployment still gets its notice: a run that missed days catches up one week (B25-18).
    private const int FlagNoticeDays = 7;

    // The daily due-state refresh (document 23, S2 and S1 step 9; P04, P06, P07; document 25, B25-18).
    // It runs as the engine: the timer's caller is the engine's own identity (CallerIdentity.Engine), and a platform
    // admin's "run it now" runs as that person; either way every row the refresh changes is an engine-nested write that
    // OP-08 judges, and every change leaves an Audit Entry. It is idempotent: a rerun on the same day changes and sends
    // nothing new.
    //
    // 1. A plan (one read call): what is due today.
    // 2. Each item in its own call, so one item that fails is reported and the others still commit:
    //    - approvals: a decision with a pending Approval Record past its Expires On, or one no longer proposed that still
    //      holds a pending record, gets the approval refresh (expired, cancelled, restarts), and each record it expires
    //      sends its escalation (S1 step 9);
    //    - reviews: a done review whose Next Due On has come opens the next review of its type (O21 due, trigger cadence);
    //    - documents: a current Document Link past its Stale On becomes stale (P07);
    //    - notices: each reminder, repeat and escalation OP-11 lists for today (reviews, documents, approvals, claims), a
    //      review's first overdue day, and a flag at gate level flag or above (S2 step 4), one Audit Entry each, keyed by
    //      the item and the step, so a rerun never sends twice.
    // 3. One Audit Entry for the run, when it changed something or a person ran it.
    //
    // The payload is the run's report. The outcome is allowed even when items failed: their reasons are warnings.
    public RuntimeOutcome RefreshDueStates(CallerIdentity caller, DueStateRefreshRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var items = new List<DueItem>();
        bool more = false;
        RuntimeOutcome plan = Run(caller, SnapshotPurpose.Read, call =>
        {
            RequireRefreshActor(call);
            items.AddRange(PlanDueItems(call, request, out more));
            return new CallAnswer(RuntimeOutcome.Allowed);
        }, request.CorrelationId);
        if (!plan.Succeeded)
        {
            return plan;
        }

        var report = new DueStateReport(request);
        var reasons = new List<Reason>();
        ProcessDueItems(caller, request, items, report, reasons);

        // A review the run opened can already be past its due date (its done review's Next Due On came while no run
        // ran). A second plan, once, gives it its overdue notice in the same run, so a rerun the same day finds nothing
        // to do (e2e step 14: the second run of the day sent the overdue notice). A dry run plans once.
        if (!request.DryRun && report.ReviewsOpenedCount > 0)
        {
            var again = new List<DueItem>();
            RuntimeOutcome second = Run(caller, SnapshotPurpose.Read, call =>
            {
                again.AddRange(PlanDueItems(call, request, out _).Where(i => i.Kind == DueItemKind.Overdue || i.Kind == DueItemKind.Reminder));
                return new CallAnswer(RuntimeOutcome.Allowed);
            }, request.CorrelationId);
            if (second.Succeeded)
            {
                ProcessDueItems(caller, request, again, report, reasons);
            }
        }

        report.More = more;
        JsonTreeNode payload = report.ToJson();
        if (!request.DryRun && (report.Changed || request.Trigger == DueStateRefreshRequest.PersonTrigger))
        {
            Run(caller, SnapshotPurpose.Write, call =>
            {
                WriteAudit(call, call.Snapshot, "Due-State Refresh", null, DueStateRunOperation, RuntimeJson.Object(
                    ("trigger", JsonTreeNode.FromString(request.Trigger)),
                    ("counts", report.CountsJson()),
                    ("noticeKinds", report.NoticeKindsJson()),
                    ("reviewsOpened", report.ReviewsJson()),
                    ("documentsStale", report.DocumentsJson()),
                    ("more", JsonTreeNode.FromBoolean(more))), null, null);
                return new CallAnswer(RuntimeOutcome.Allowed);
            }, request.CorrelationId);
        }

        return new RuntimeOutcome(RuntimeOutcome.Allowed, reasons, plan.ConfigurationVersion, plan.SnapshotHash, payload: payload);
    }

    // Each item in its own call: one that fails is reported, and the others still commit. An item already done this run
    // (a notice keyed by the item and the step) changes nothing the second time.
    private void ProcessDueItems(CallerIdentity caller, DueStateRefreshRequest request, IEnumerable<DueItem> items, DueStateReport report, List<Reason> reasons)
    {
        foreach (DueItem item in items)
        {
            RuntimeOutcome outcome = Run(caller, SnapshotPurpose.Write, call =>
            {
                call.DryRun = request.DryRun;
                JsonTreeNode? done = RunDueItem(call, item, request);
                return new CallAnswer(RuntimeOutcome.Allowed, item.RowId, false, done, dryRun: request.DryRun);
            }, request.CorrelationId);

            if (!outcome.Succeeded)
            {
                Reason? first = outcome.Reasons.FirstOrDefault(r => r.Severity == ReasonSeverity.Error) ?? outcome.Reasons.FirstOrDefault();
                report.Failed(item, first);
                reasons.Add(new Reason(
                    ReasonCodes.DueStateItemFailed,
                    ReasonSeverity.Warning,
                    "The due-state refresh could not finish " + item.Describe() + (first == null ? "." : ": " + first.Message),
                    item.EntityName,
                    item.RowId));
                continue;
            }

            report.Done(item, outcome.Payload);
        }
    }

    // Only the engine's own scheduled work, or a person holding the platform admin role (a fixed key, open design
    // question 1), runs the refresh. An AI client, an agent, a pipeline and the approval runner are refused.
    private static void RequireRefreshActor(RuntimeCall call)
    {
        ActorInfo actor = call.Actor;
        bool engine = actor.ActorKind == ActorKindMeaning.Engine && call.Caller.Entry == CallerEntry.Engine;
        bool admin = actor.ActorKind == ActorKindMeaning.Person && actor.PrincipalKind == PrincipalKind.User && actor.HoldsRole(EngineKeys.PlatformAdminRole);
        if (engine || admin)
        {
            return;
        }

        throw CallFailure.Of(
            OperationOutcomes.Refused,
            ReasonCodes.DueStateRefreshNotAllowed,
            "Only the engine's daily refresh, or a person holding the " + EngineKeys.PlatformAdminRole + " role in the web app, runs the due-state refresh. The caller is "
            + EngineContract.ActorKindMeanings.ToWire(actor.ActorKind) + ".",
            "Role",
            EngineKeys.PlatformAdminRole);
    }

    // ---- The plan ----

    private IReadOnlyList<DueItem> PlanDueItems(RuntimeCall call, DueStateRefreshRequest request, out bool more)
    {
        DateTime today = call.AsOfUtc.Date;
        FactSet facts = FactSet.Run(call.Tx, f => new[]
        {
            FactPlan.Of(
                FactQuery.Where("pending", RowQuery.For("approval-record").Where("outcomeMeaning", "pending")),
                FactQuery.Where("reviews", RowQuery.For("review")),
                FactQuery.Where("links", RowQuery.For("document-link")),
                FactQuery.Where("claims", RowQuery.For("claim").Where("statusMeaning", "open")),
                FactQuery.Where("deployments", RowQuery.For("deployment-record")),
                FactQuery.Where("applications", RowQuery.For("business-application")),
                FactQuery.Where("owners", RowQuery.For("owner-assignment"))),
        });
        call.FactRounds += facts.Rounds;
        var apps = facts.All("applications").ToDictionary(a => a.Id, a => a, StringComparer.OrdinalIgnoreCase);
        var owners = DecisionScope.CurrentOwners(facts.All("owners"), call.AsOfUtc)
            .Where(o => o.Text("businessApplication") != null)
            .GroupBy(o => o.Text("businessApplication")!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<StoredRow>)g.ToList(), StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<StoredRow> OwnersOf(string? appId) => appId != null && owners.TryGetValue(appId, out IReadOnlyList<StoredRow>? list) ? list : Array.Empty<StoredRow>();

        var state = new List<DueItem>();
        var notices = new List<DueItem>();

        // Approvals: the decisions the approval refresh has work for, as FindDueApprovals lists them.
        List<IGrouping<string, StoredRow>> byDecision = facts.All("pending").Where(r => r.Text("decision") != null)
            .GroupBy(r => r.Text("decision")!, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
        FactSet decisions = FactSet.Run(call.Tx, f => new[] { FactPlan.Of(byDecision.Select(g => FactQuery.ById("decision " + g.Key, "decision", g.Key))) });
        call.FactRounds += decisions.Rounds;
        foreach (IGrouping<string, StoredRow> group in byDecision)
        {
            StoredRow? decision = decisions.One("decision " + group.Key);
            if (decision == null)
            {
                continue;
            }

            bool proposed = decision.Text("statusMeaning") == "proposed";
            if (!proposed || group.Any(r => IsDue(r, call.AsOfUtc)))
            {
                state.Add(DueItem.Approvals(decision.Id));
            }

            if (!proposed)
            {
                continue;
            }

            // Reminders and escalation for each counting request still within its time (a consultation never counts and
            // the decision does not wait for it, so it gets none). Its clock starts at Requested On, and a step on that
            // day is not sent: the card is the first message.
            string? appId = decision.Text("subjectApplication") ?? null;
            foreach (StoredRow record in group.Where(r => !r.Flag("isConsultation") && !IsDue(r, call.AsOfUtc)).OrderBy(r => r.Id, StringComparer.Ordinal))
            {
                DateTime? requested = RuntimeJson.Utc(record.Columns, "requestedOn");
                if (!requested.HasValue || requested.Value.Date >= today)
                {
                    continue;
                }

                string? recordApp = appId ?? ApplicationOfDecision(call, decision);
                foreach (ReminderStep step in TodaysSteps(call, "approval", requested.Value.Date, null, null))
                {
                    notices.Add(DueItem.Reminder(
                        "approval-record", record.Id, "Approval Record", "approval",
                        step, requested.Value.Date,
                        ApprovalRecipients(call, record, decision, OwnersOf(recordApp), recordApp, step.Escalated),
                        "outcomeMeaning", new[] { "pending" }, recordApp));
                }
            }
        }

        // Gate decisions held with every approval in (G-14) whose gate is ready now, such as once an owner named from a
        // later day starts: the approval refresh accepts them. Only those that would settle, so a rerun finds nothing.
        var heldPlanned = new HashSet<string>(state.Where(i => i.Kind == DueItemKind.Approvals).Select(i => i.RowId), StringComparer.Ordinal);
        foreach (StoredRow held in HeldGateCandidates(call, null))
        {
            if (!heldPlanned.Contains(held.Id) && HeldGateDecisionSettles(call, held))
            {
                state.Add(DueItem.Approvals(held.Id));
            }
        }

        // Reviews: open the next one on its Next Due On; remind and escalate the due ones; tell the lead owner on the first
        // overdue day.
        IReadOnlyList<StoredRow> reviews = facts.All("reviews");
        foreach (StoredRow review in reviews.OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            string? appId = review.Text("businessApplication");
            StoredRow? app = appId != null && apps.TryGetValue(appId, out StoredRow? found) ? found : null;
            string? type = review.Text("reviewType");
            string meaning = review.Text("statusMeaning") ?? "due";
            if (app == null || type == null || !ApplicationIsLive(call.Snapshot, app))
            {
                continue;
            }

            if (meaning == "done")
            {
                DateTime? next = RuntimeJson.Utc(review.Columns, "nextDueOn");
                if (next.HasValue && next.Value.Date <= today && !NextReviewExists(reviews, review, next.Value.Date))
                {
                    state.Add(DueItem.OpenReview(review.Id, next.Value.Date));
                }

                continue;
            }

            if (meaning != "due")
            {
                continue;
            }

            DateTime? due = RuntimeJson.Utc(review.Columns, "dueOn");
            if (!due.HasValue)
            {
                continue;
            }

            string? tier = app.Text("tier");
            foreach (ReminderStep step in TodaysSteps(call, EngineKeys.ReviewSubjectKind, due.Value.Date, type, tier))
            {
                notices.Add(DueItem.Reminder("review", review.Id, "Review", EngineKeys.ReviewSubjectKind, step, due.Value.Date,
                    ReviewRecipients(call, type, app, OwnersOf(app.Id), step.Escalated), "statusMeaning", new[] { "due" }, app.Id));
            }

            if (due.Value.Date < today)
            {
                notices.Add(DueItem.Overdue(review.Id, due.Value.Date, ReviewRecipients(call, type, app, OwnersOf(app.Id), false), app.Id));
            }
        }

        // Documents: a current link past its Stale On becomes stale; a stale one, or one turning stale today, gets the
        // document reminders counted from its Stale On.
        foreach (StoredRow link in facts.All("links").OrderBy(l => l.Id, StringComparer.Ordinal))
        {
            string? appId = link.Text("businessApplication");
            StoredRow? app = appId != null && apps.TryGetValue(appId, out StoredRow? found) ? found : null;
            string meaning = link.Text("statusMeaning") ?? "draft";
            DateTime? staleOn = StaleOnOf(call.Snapshot, link);
            if (app == null || !ApplicationIsLive(call.Snapshot, app) || !staleOn.HasValue || (meaning != "current" && meaning != "stale"))
            {
                continue;
            }

            if (meaning == "current" && staleOn.Value.Date < today)
            {
                state.Add(DueItem.StaleDocument(link.Id, staleOn.Value.Date));
            }

            if (meaning == "stale" || staleOn.Value.Date < today)
            {
                foreach (ReminderStep step in TodaysSteps(call, "document", staleOn.Value.Date, null, null))
                {
                    notices.Add(DueItem.Reminder("document-link", link.Id, "Document Link", "document", step, staleOn.Value.Date,
                        DocumentRecipients(link, OwnersOf(app.Id), step.Escalated), "statusMeaning", new[] { "current", "stale" }, app.Id));
                }
            }
        }

        // Claims: an open claim reminds its suggested claimant, counted from the day it was found; a step on that day is
        // not sent, because the suggestion is the first message.
        foreach (StoredRow claim in facts.All("claims").OrderBy(c => c.Id, StringComparer.Ordinal))
        {
            DateTime? found = RuntimeJson.Utc(claim.Columns, "createdOn") ?? RuntimeJson.Utc(claim.Columns, "engineCheckedOn");
            if (!found.HasValue || found.Value.Date >= today)
            {
                continue;
            }

            foreach (ReminderStep step in TodaysSteps(call, "claim", found.Value.Date, null, null))
            {
                string? claimant = claim.Text("suggestedClaimant");
                DueRecipients to = step.Escalated || claimant == null
                    ? DueRecipients.Of(null, new[] { EngineKeys.PlatformAdminRole }, null)
                    : DueRecipients.Of(new[] { claimant }, null, null);
                notices.Add(DueItem.Reminder("claim", claim.Id, "Claim", "claim", step, found.Value.Date, to, "statusMeaning", new[] { "open" }, null));
            }
        }

        // Flags (S2 step 4): a deployment flagged at gate level flag, advise or enforce tells the default-flag-notify list,
        // once, within a week of the deployment.
        foreach (StoredRow record in facts.All("deployments").OrderBy(d => d.Id, StringComparer.Ordinal))
        {
            string? level = record.Text("gateLevelApplied");
            bool flagged = record.Flag("isOutsideWindow") || record.Flag("isOtherPath") || record.Flag("isMissingApproval") || record.Flag("isAiOnlyCodeApproval");
            DateTime? finished = RuntimeJson.Utc(record.Columns, "finishedOn");
            if (!flagged || (level != "flag" && level != "advise" && level != "enforce") || !finished.HasValue || finished.Value.Date < today.AddDays(-FlagNoticeDays))
            {
                continue;
            }

            string? appId = record.Text("businessApplication");
            notices.Add(DueItem.Flag(record.Id, finished.Value.Date, FlagRecipients(call.Snapshot, OwnersOf(appId)), appId));
        }

        // Notices already sent are dropped here, so a rerun plans nothing new; the item checks its key again before it
        // writes.
        var keys = notices.Select(n => n.RequestKey!).Distinct(StringComparer.Ordinal).ToList();
        FactSet sent = FactSet.Run(call.Tx, f => new[] { FactPlan.Of(keys.Select(k => FactQuery.Where("sent " + k, RowQuery.For("audit-entry").Where("requestKey", k)))) });
        call.FactRounds += sent.Rounds;
        var planned = state.Concat(notices.Where(n => sent.All("sent " + n.RequestKey).Count == 0).GroupBy(n => n.RequestKey!, StringComparer.Ordinal).Select(g => g.First())).ToList();
        more = planned.Count > request.MaxItems;
        return planned.Take(request.MaxItems).ToList();
    }

    // Today's steps of OP-11 for one item (document 25, OP-11): remind, repeat or escalate, with the rule that caused each.
    private static IEnumerable<ReminderStep> TodaysSteps(RuntimeCall call, string subjectKind, DateTime dueOn, string? reviewTypeKey, string? tierKey)
    {
        bool cadence = reviewTypeKey != null && tierKey != null
            && call.Snapshot.Snapshot.TryGetRow("review-type", reviewTypeKey, out _) && call.Snapshot.Snapshot.TryGetRow("tier", tierKey, out _);
        var request = new DueRequest(subjectKind, DateTime.SpecifyKind(dueOn, DateTimeKind.Utc), null, cadence ? reviewTypeKey : null, cadence ? tierKey : null);
        EvaluationResult<DueResult?> result = DueDatesOperation.Evaluate(call.Snapshot.Snapshot, call.Context(nested: true), request);
        return result.Outcome == "computed" && result.Payload != null ? result.Payload.Actions : Enumerable.Empty<ReminderStep>();
    }

    // A live application: not archived, and not at a stage whose meaning is retired.
    private static bool ApplicationIsLive(CompiledSnapshot snapshot, StoredRow app)
    {
        if (app.Text("statusMeaning") == "archived")
        {
            return false;
        }

        string? stage = app.Text("lifecycleStage");
        return stage == null || !snapshot.Snapshot.TryGetRow("lifecycle-stage", stage, out Core.Snapshots.SnapshotRow? row) || row.EngineMeaning != "retired";
    }

    // True when the done review already has its successor: a due review of the same application and type, or any review of
    // them due on that date (O21 key Business Application + Review Type + Due On).
    private static bool NextReviewExists(IReadOnlyList<StoredRow> reviews, StoredRow done, DateTime nextDue)
    {
        string? app = done.Text("businessApplication");
        string? type = done.Text("reviewType");
        return reviews.Any(r => !string.Equals(r.Id, done.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.Text("businessApplication"), app, StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.Text("reviewType"), type, StringComparison.Ordinal)
            && (r.Text("statusMeaning") == "due" || RuntimeJson.Utc(r.Columns, "dueOn")?.Date == nextDue));
    }

    // O19 Stale On: Last Checked On, else Last Updated On, plus the C22 Freshness Days of its type; else the stored value.
    // A link with neither date nor a stored value is left alone (B25-18).
    private static DateTime? StaleOnOf(CompiledSnapshot snapshot, StoredRow link)
    {
        string? type = link.Text("documentType");
        long? days = type != null && snapshot.Snapshot.TryGetRow("document-type", type, out Core.Snapshots.SnapshotRow? row) && row.TryGetColumn("freshnessDays", out JsonTreeNode? value) && value.TryGetInt64(out long d) ? d : null;
        DateTime? from = RuntimeJson.Utc(link.Columns, "lastCheckedOn") ?? RuntimeJson.Utc(link.Columns, "lastUpdatedOn");
        return days.HasValue && from.HasValue ? from.Value.Date.AddDays(days.Value) : RuntimeJson.Utc(link.Columns, "staleOn");
    }

    private static string? ApplicationOfDecision(RuntimeCall call, StoredRow decision)
    {
        string? direct = decision.Text("subjectApplication");
        if (direct != null)
        {
            return direct;
        }

        string? intake = decision.Text("subjectIntakeRequest");
        string? change = decision.Text("subjectChangeRequest");
        return intake != null ? call.Tx.Get("intake-request", intake)?.Text("businessApplication")
            : change != null ? call.Tx.Get("change-request", change)?.Text("businessApplication")
            : null;
    }

    // ---- Who a notice goes to (document 23, S2 steps 2 to 4) ----

    // An approval request: the person it asks by name, else the current holders of the owner role it asks, else the role.
    // Escalated: the owner role's deputies, else the accountable letter of the decision type (C31).
    private static DueRecipients ApprovalRecipients(RuntimeCall call, StoredRow record, StoredRow decision, IReadOnlyList<StoredRow> owners, string? appId, bool escalated)
    {
        string? asked = record.Text("askedPrincipal");
        string? ownerRole = record.Text("requiredOwnerRole");
        string? role = record.Text("requiredRole");
        if (!escalated)
        {
            if (asked != null)
            {
                return DueRecipients.Of(new[] { asked }, null, ownerRole);
            }

            if (ownerRole != null)
            {
                return DueRecipients.Of(Holders(owners, ownerRole, deputy: false), null, ownerRole);
            }

            return DueRecipients.Of(null, role == null ? null : new[] { role }, null);
        }

        if (ownerRole != null)
        {
            IReadOnlyList<string> deputies = Holders(owners, ownerRole, deputy: true);
            if (deputies.Count > 0)
            {
                return DueRecipients.Of(deputies, null, ownerRole, toDeputies: true);
            }
        }

        string? tier = appId == null ? null : call.Tx.Get("business-application", appId)?.Text("tier");
        return Accountable(call.Snapshot, decision.Text("decisionType"), tier, owners);
    }

    // A review: the holders of its type's lead owner role (C24 Default Owner Role), else every current owner. Escalated:
    // that role's deputies, else the accountable letter of the decision type the review records (C24 Produces Decision
    // Type).
    private static DueRecipients ReviewRecipients(RuntimeCall call, string reviewType, StoredRow app, IReadOnlyList<StoredRow> owners, bool escalated)
    {
        Core.Snapshots.SnapshotRow? type = call.Snapshot.Snapshot.TryGetRow("review-type", reviewType, out Core.Snapshots.SnapshotRow? row) ? row : null;
        string? lead = Column(type, "defaultOwnerRoleKey");
        if (!escalated)
        {
            IReadOnlyList<string> holders = lead == null ? Array.Empty<string>() : Holders(owners, lead, deputy: false);
            return holders.Count > 0
                ? DueRecipients.Of(holders, null, lead)
                : DueRecipients.Of(owners.Where(o => !o.Flag("isDeputy")).Select(o => o.Text("principal")).Where(p => p != null).Select(p => p!).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), null, lead);
        }

        IReadOnlyList<string> deputies = lead == null ? Array.Empty<string>() : Holders(owners, lead, deputy: true);
        return deputies.Count > 0
            ? DueRecipients.Of(deputies, null, lead, toDeputies: true)
            : Accountable(call.Snapshot, Column(type, "producesDecisionTypeKey"), app.Text("tier"), owners);
    }

    // A document: its Document Owner, else the technical owner. Escalated: the technical owner's deputies, else the
    // business owner (a document records no decision type, so there is no accountable letter to fall back on).
    private static DueRecipients DocumentRecipients(StoredRow link, IReadOnlyList<StoredRow> owners, bool escalated)
    {
        if (!escalated)
        {
            string? documentOwner = link.Text("documentOwner");
            return documentOwner != null
                ? DueRecipients.Of(new[] { documentOwner }, null, null)
                : DueRecipients.Of(Holders(owners, EngineKeys.TechnicalOwnerRole, deputy: false), null, EngineKeys.TechnicalOwnerRole);
        }

        IReadOnlyList<string> deputies = Holders(owners, EngineKeys.TechnicalOwnerRole, deputy: true);
        return deputies.Count > 0
            ? DueRecipients.Of(deputies, null, EngineKeys.TechnicalOwnerRole, toDeputies: true)
            : DueRecipients.Of(Holders(owners, EngineKeys.BusinessOwnerRole, deputy: false), null, EngineKeys.BusinessOwnerRole);
    }

    // A flag: the default-flag-notify setting's owner roles (their current holders on the application) and roles. A gate
    // policy row's own Flag Notify list is not read: the record does not keep which row applied (B25-18).
    private static DueRecipients FlagRecipients(CompiledSnapshot snapshot, IReadOnlyList<StoredRow> owners)
    {
        JsonTreeNode? value = snapshot.Snapshot.TryGetRow("setting", EngineKeys.DefaultFlagNotify, out Core.Snapshots.SnapshotRow? row) && row.IsActive && row.TryGetColumn("value", out JsonTreeNode? v) ? v : null;
        IReadOnlyList<string> ownerRoles = RuntimeJson.StringList(value, "ownerRoleKeys");
        IReadOnlyList<string> roles = RuntimeJson.StringList(value, "roleKeys");
        var people = ownerRoles.SelectMany(r => Holders(owners, r, deputy: false)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return DueRecipients.Of(people, roles, null);
    }

    // The C31 accountable letter of a decision type at a tier: its role, or its owner role's current holders on the
    // application (and the fallback owner role's when nobody holds it).
    private static DueRecipients Accountable(CompiledSnapshot snapshot, string? decisionType, string? tier, IReadOnlyList<StoredRow> owners)
    {
        if (decisionType == null || !snapshot.Snapshot.TryGetEntity("decision-right", out Core.Snapshots.SnapshotEntity? rights))
        {
            return DueRecipients.Of(null, null, null);
        }

        Core.Snapshots.SnapshotRow? accountable = rights.ActiveRows
            .Where(r => Column(r, "decisionTypeKey") == decisionType && Column(r, "letter") == "accountable" && (Column(r, "tierKey") == null || Column(r, "tierKey") == tier))
            .OrderBy(r => Column(r, "tierKey") == null ? 1 : 0)
            .ThenBy(r => r.KeyPath, StringComparer.Ordinal)
            .FirstOrDefault();
        if (accountable == null)
        {
            return DueRecipients.Of(null, null, null);
        }

        string? role = Column(accountable, "roleKey");
        if (role != null)
        {
            return DueRecipients.Of(null, new[] { role }, null);
        }

        string? ownerRole = Column(accountable, "ownerRoleKey");
        string? fallback = Column(accountable, "fallbackOwnerRoleKey");
        IReadOnlyList<string> holders = ownerRole == null ? Array.Empty<string>() : Holders(owners, ownerRole, deputy: false);
        if (holders.Count == 0 && fallback != null)
        {
            return DueRecipients.Of(Holders(owners, fallback, deputy: false), null, fallback);
        }

        return DueRecipients.Of(holders, null, ownerRole);
    }

    private static IReadOnlyList<string> Holders(IReadOnlyList<StoredRow> owners, string ownerRole, bool deputy)
    {
        return owners.Where(o => string.Equals(o.Text("ownerRole"), ownerRole, StringComparison.Ordinal) && o.Flag("isDeputy") == deputy)
            .Select(o => o.Text("principal")).Where(p => p != null).Select(p => p!)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.Ordinal).ToList();
    }

    private static string? Column(Core.Snapshots.SnapshotRow? row, string column)
    {
        return row != null && row.TryGetColumn(column, out JsonTreeNode? value) && value.Kind == JsonTreeKind.String && value.GetString().Length > 0 ? value.GetString() : null;
    }

    // ---- One item ----

    private JsonTreeNode? RunDueItem(RuntimeCall call, DueItem item, DueStateRefreshRequest request)
    {
        switch (item.Kind)
        {
            case DueItemKind.Approvals:
                return RefreshDueApprovals(call, item, request);
            case DueItemKind.OpenReview:
                return OpenNextReview(call, item, request);
            case DueItemKind.StaleDocument:
                return MarkDocumentStale(call, item, request);
            default:
                return SendNotice(call, item, request);
        }
    }

    // The approval refresh of one decision (as RefreshApprovals), then the escalation of each record it expired.
    private JsonTreeNode? RefreshDueApprovals(RuntimeCall call, DueItem item, DueStateRefreshRequest request)
    {
        StoredRow? decision = call.Tx.Get("decision", item.RowId);
        if (decision == null)
        {
            return null;
        }

        if (decision.Text("statusMeaning") == "proposed")
        {
            RecomputeApprovals(call, decision.Id);
        }
        else
        {
            CancelPendingRecords(call, decision.Id, null);
        }

        JsonTreeNode refreshed = RefreshPayload(call, decision.Id);
        IReadOnlyList<string> expired = RuntimeJson.StringList(refreshed, "expiredRecordIds");
        if (call.Writes.Count > 0)
        {
            WriteAudit(call, call.Snapshot, "Decision", decision.Id, DueStateRefreshedOperation, RuntimeJson.Merge(WrittenRows(call), RuntimeJson.Object(("trigger", JsonTreeNode.FromString(request.Trigger)))), null, null);
        }

        // S1 step 9: at Expires On the engine asks S2 to escalate to the deputy (C26). One escalation per expired record.
        var escalations = new List<JsonTreeNode>();
        string? appId = ApplicationOfDecision(call, decision);
        IReadOnlyList<StoredRow> owners = appId == null ? Array.Empty<StoredRow>() : DecisionScope.CurrentOwners(call.Tx.Query(RowQuery.For("owner-assignment").Where("businessApplication", appId)), call.AsOfUtc);
        foreach (string id in expired)
        {
            StoredRow? record = call.Tx.Get("approval-record", id);
            if (record == null || record.Flag("isConsultation"))
            {
                continue;
            }

            string key = "due-state|expired|approval-record|" + id;
            if (call.Tx.Query(RowQuery.For("audit-entry").Where("requestKey", key)).Count > 0)
            {
                continue;
            }

            DueRecipients to = ApprovalRecipients(call, record, decision, owners, appId, escalated: true);
            JsonTreeNode notice = NoticeJson("expired", "approval", "approval-record", id, appId, null, null, true, ChannelFor(call.Snapshot, "approval"), RuntimeJson.Utc(record.Columns, "expiresOn")?.Date, to, request.Trigger);
            WriteAudit(call, call.Snapshot, "Approval Record", id, ReminderSentOperation, notice, key, null);
            escalations.Add(notice);
        }

        return RuntimeJson.Object(("approvals", refreshed), ("notices", JsonTreeNode.CreateArray(escalations)));
    }

    // The next review of a done one (document 23, S2 step 1; P04; document 22, O21): the same application and type,
    // status due, trigger cadence, due on the done review's Next Due On. The record number comes from the store.
    private JsonTreeNode? OpenNextReview(RuntimeCall call, DueItem item, DueStateRefreshRequest request)
    {
        StoredRow? done = call.Tx.Get("review", item.RowId);
        string? app = done?.Text("businessApplication");
        string? type = done?.Text("reviewType");
        if (done == null || app == null || type == null || done.Text("statusMeaning") != "done")
        {
            return null;
        }

        IReadOnlyList<StoredRow> siblings = call.Tx.Query(RowQuery.For("review").Where("businessApplication", app).Where("reviewType", type));
        if (NextReviewExists(siblings, done, item.DueOn!.Value))
        {
            return null;
        }

        string from = done.Text("recordNumber") ?? done.Id;
        WriteStep step = GuardedWrite(call, new WriteSpec("review", WriteOperation.Create)
        {
            After = RuntimeJson.Object(
                ("businessApplication", JsonTreeNode.FromString(app)),
                ("reviewType", JsonTreeNode.FromString(type)),
                ("status", JsonTreeNode.FromString(DefaultStatus(call.Snapshot, "review", "due"))),
                ("trigger", JsonTreeNode.FromString("cadence")),
                ("dueOn", JsonTreeNode.FromString(item.DueOn!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))),
                ("description", JsonTreeNode.FromString("Opened by the due-state refresh on its due date, from " + from + " (cadence)."))),
            Nested = true,
        })!;
        WriteAudit(call, call.Snapshot, "Review", step.RowId, DueStateRefreshedOperation, RuntimeJson.Merge(WrittenRows(call), RuntimeJson.Object(
            ("trigger", JsonTreeNode.FromString(request.Trigger)),
            ("fromReviewId", JsonTreeNode.FromString(done.Id)))), OpenReviewKey(done.Id, item.DueOn!.Value), null);
        StoredRow opened = call.Tx.Get("review", step.RowId)!;
        return RuntimeJson.Object(
            ("reviewId", JsonTreeNode.FromString(step.RowId)),
            ("recordNumber", RuntimeJson.OptionalText(opened.Text("recordNumber"))),
            ("businessApplicationId", JsonTreeNode.FromString(app)),
            ("reviewTypeKey", JsonTreeNode.FromString(type)),
            ("dueOn", JsonTreeNode.FromString(item.DueOn!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))),
            ("fromReviewId", JsonTreeNode.FromString(done.Id)));
    }

    // The request key of the audit entry that opens the next review, in the same transaction as the review: the unique
    // index on Request Key refuses a second run that opens the same review at the same time (security review, minor).
    private static string OpenReviewKey(string doneReviewId, DateTime dueOn)
    {
        return "due-state|open-review|" + doneReviewId + "|" + dueOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    // A current Document Link past its Stale On becomes stale (P07 step 7).
    private JsonTreeNode? MarkDocumentStale(RuntimeCall call, DueItem item, DueStateRefreshRequest request)
    {
        StoredRow? link = call.Tx.Get("document-link", item.RowId);
        if (link == null || link.Text("statusMeaning") != "current")
        {
            return null;
        }

        DateTime? staleOn = StaleOnOf(call.Snapshot, link);
        if (!staleOn.HasValue || staleOn.Value.Date >= call.AsOfUtc.Date)
        {
            return null;
        }

        GuardedWrite(call, new WriteSpec("document-link", WriteOperation.Update)
        {
            RowId = link.Id,
            After = RuntimeJson.Object(("status", JsonTreeNode.FromString(DefaultStatus(call.Snapshot, "document-link", "stale")))),
            Nested = true,
        });
        WriteAudit(call, call.Snapshot, "Document Link", link.Id, DueStateRefreshedOperation, RuntimeJson.Merge(WrittenRows(call), RuntimeJson.Object(("trigger", JsonTreeNode.FromString(request.Trigger)))), null, null);
        return RuntimeJson.Object(
            ("documentLinkId", JsonTreeNode.FromString(link.Id)),
            ("businessApplicationId", RuntimeJson.OptionalText(link.Text("businessApplication"))),
            ("documentTypeKey", RuntimeJson.OptionalText(link.Text("documentType"))),
            ("staleOn", JsonTreeNode.FromString(staleOn.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))));
    }

    // One notice: an Audit Entry keyed by the item and its step, written once. The subject must still be in the state
    // the notice is about (a request still pending, a review still due).
    private JsonTreeNode? SendNotice(RuntimeCall call, DueItem item, DueStateRefreshRequest request)
    {
        StoredRow? subject = call.Tx.Get(item.Entity, item.RowId);
        if (subject == null || call.Tx.Query(RowQuery.For("audit-entry").Where("requestKey", item.RequestKey!)).Count > 0)
        {
            return null;
        }

        if (item.StateColumn != null && Array.IndexOf(item.StateMeanings, subject.Text(item.StateColumn) ?? string.Empty) < 0)
        {
            return null;
        }

        string operation = item.Kind == DueItemKind.Flag ? FlagNoticeSentOperation : ReminderSentOperation;
        JsonTreeNode notice = NoticeJson(item.Notice!, item.SubjectKind!, item.Entity, item.RowId, item.ApplicationId, item.RuleKey, item.ReminderNumber, item.Escalated,
            item.ChannelKey ?? ChannelFor(call.Snapshot, item.SubjectKind!), item.DueOn, item.Recipients!, request.Trigger);
        WriteAudit(call, call.Snapshot, item.EntityName, item.RowId, operation, notice, item.RequestKey, null);
        return RuntimeJson.Object(("notices", JsonTreeNode.CreateArray(new[] { notice })));
    }

    // The channel of a reminder rule of the subject kind, else the reminder-channel setting.
    private static string? ChannelFor(CompiledSnapshot snapshot, string subjectKind)
    {
        string? rule = snapshot.Snapshot.TryGetEntity("reminder-rule", out Core.Snapshots.SnapshotEntity? rules)
            ? rules.ActiveRows.Where(r => Column(r, "subjectKind") == subjectKind).OrderBy(r => r.KeyPath, StringComparer.Ordinal).Select(r => Column(r, "channelKey")).FirstOrDefault(c => c != null)
            : null;
        return rule ?? SettingText(snapshot, EngineKeys.ReminderChannel);
    }

    // What an Audit Entry of a notice holds: IDs and keys only, never a label (document 22, section 2.7).
    private static JsonTreeNode NoticeJson(string notice, string subjectKind, string entity, string rowId, string? appId, string? ruleKey, int? reminderNumber, bool escalated, string? channel, DateTime? dueOn, DueRecipients to, string trigger)
    {
        return RuntimeJson.Object(
            ("notice", JsonTreeNode.FromString(notice)),
            ("subjectKind", JsonTreeNode.FromString(subjectKind)),
            ("entity", JsonTreeNode.FromString(entity)),
            ("rowId", JsonTreeNode.FromString(rowId)),
            ("businessApplicationId", RuntimeJson.OptionalText(appId)),
            ("ruleKey", RuntimeJson.OptionalText(ruleKey)),
            ("reminderNumber", reminderNumber.HasValue ? JsonTreeNode.FromInt64(reminderNumber.Value) : null),
            ("escalated", JsonTreeNode.FromBoolean(escalated)),
            ("channelKey", RuntimeJson.OptionalText(channel)),
            ("dueOn", dueOn.HasValue ? JsonTreeNode.FromString(dueOn.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) : null),
            ("to", to.ToJson()),
            ("trigger", JsonTreeNode.FromString(trigger)));
    }

    // ---- The plan's items ----

    private enum DueItemKind
    {
        Approvals,
        OpenReview,
        StaleDocument,
        Reminder,
        Overdue,
        Flag,
    }

    private sealed class DueItem
    {
        private DueItem(DueItemKind kind, string entity, string rowId, string entityName)
        {
            Kind = kind;
            Entity = entity;
            RowId = rowId;
            EntityName = entityName;
        }

        public DueItemKind Kind { get; }

        public string Entity { get; }

        public string RowId { get; }

        public string EntityName { get; }

        public DateTime? DueOn { get; private set; }

        public string? Notice { get; private set; }

        public string? SubjectKind { get; private set; }

        public string? RuleKey { get; private set; }

        public int? ReminderNumber { get; private set; }

        public bool Escalated { get; private set; }

        public string? ChannelKey { get; private set; }

        public DueRecipients? Recipients { get; private set; }

        public string? RequestKey { get; private set; }

        public string? ApplicationId { get; private set; }

        public string? StateColumn { get; private set; }

        public string[] StateMeanings { get; private set; } = Array.Empty<string>();

        public static DueItem Approvals(string decisionId) => new(DueItemKind.Approvals, "decision", decisionId, "Decision");

        public static DueItem OpenReview(string reviewId, DateTime nextDue) => new(DueItemKind.OpenReview, "review", reviewId, "Review") { DueOn = nextDue };

        public static DueItem StaleDocument(string linkId, DateTime staleOn) => new(DueItemKind.StaleDocument, "document-link", linkId, "Document Link") { DueOn = staleOn };

        public static DueItem Reminder(string entity, string rowId, string entityName, string subjectKind, ReminderStep step, DateTime dueOn, DueRecipients to, string stateColumn, string[] stateMeanings, string? appId)
        {
            return new DueItem(DueItemKind.Reminder, entity, rowId, entityName)
            {
                Notice = EngineContract.ReminderActions.ToWire(step.Action),
                SubjectKind = subjectKind,
                RuleKey = step.RuleKey,
                ReminderNumber = step.ReminderNumber,
                Escalated = step.Escalated,
                ChannelKey = step.ChannelKey,
                DueOn = dueOn,
                Recipients = to,
                RequestKey = "due-state|reminder|" + entity + "|" + rowId + "|" + step.RuleKey + "|" + step.ReminderNumber.ToString(CultureInfo.InvariantCulture),
                StateColumn = stateColumn,
                StateMeanings = stateMeanings,
                ApplicationId = appId,
            };
        }

        public static DueItem Overdue(string reviewId, DateTime dueOn, DueRecipients to, string? appId)
        {
            return new DueItem(DueItemKind.Overdue, "review", reviewId, "Review")
            {
                Notice = "overdue",
                SubjectKind = EngineKeys.ReviewSubjectKind,
                DueOn = dueOn,
                Recipients = to,
                RequestKey = "due-state|overdue|review|" + reviewId,
                StateColumn = "statusMeaning",
                StateMeanings = new[] { "due" },
                ApplicationId = appId,
            };
        }

        public static DueItem Flag(string deploymentId, DateTime finishedOn, DueRecipients to, string? appId)
        {
            return new DueItem(DueItemKind.Flag, "deployment-record", deploymentId, "Deployment Record")
            {
                Notice = "flag",
                SubjectKind = "deployment",
                DueOn = finishedOn,
                Recipients = to,
                RequestKey = "due-state|flag|deployment-record|" + deploymentId,
                ApplicationId = appId,
            };
        }

        public string Describe()
        {
            switch (Kind)
            {
                case DueItemKind.Approvals:
                    return "the approval requests of decision " + RowId;
                case DueItemKind.OpenReview:
                    return "the next review after review " + RowId;
                case DueItemKind.StaleDocument:
                    return "the stale document link " + RowId;
                default:
                    return "the " + Notice + " notice for " + EntityName + " " + RowId;
            }
        }
    }

    // Who a notice goes to: people by Entra object ID (owner holders resolved now), product roles (their bound groups are
    // read live when the message goes out), and the owner role the people hold, when they are its holders or deputies.
    private sealed class DueRecipients
    {
        private DueRecipients(IReadOnlyList<string> principalIds, IReadOnlyList<string> roleKeys, string? ownerRoleKey, bool toDeputies)
        {
            PrincipalIds = principalIds;
            RoleKeys = roleKeys;
            OwnerRoleKey = ownerRoleKey;
            ToDeputies = toDeputies;
        }

        public IReadOnlyList<string> PrincipalIds { get; }

        public IReadOnlyList<string> RoleKeys { get; }

        public string? OwnerRoleKey { get; }

        public bool ToDeputies { get; }

        public static DueRecipients Of(IEnumerable<string>? principals, IEnumerable<string>? roles, string? ownerRoleKey, bool toDeputies = false)
        {
            return new DueRecipients(
                (principals ?? Enumerable.Empty<string>()).Where(p => !string.IsNullOrEmpty(p)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.Ordinal).ToList(),
                (roles ?? Enumerable.Empty<string>()).Where(r => !string.IsNullOrEmpty(r)).Distinct(StringComparer.Ordinal).OrderBy(r => r, StringComparer.Ordinal).ToList(),
                ownerRoleKey,
                toDeputies);
        }

        public JsonTreeNode ToJson()
        {
            return RuntimeJson.Object(
                ("principalIds", RuntimeJson.Strings(PrincipalIds)),
                ("roleKeys", RuntimeJson.Strings(RoleKeys)),
                ("ownerRoleKey", RuntimeJson.OptionalText(OwnerRoleKey)),
                ("toDeputies", JsonTreeNode.FromBoolean(ToDeputies)));
        }
    }

    // The run's report, gathered item by item.
    private sealed class DueStateReport
    {
        private readonly DueStateRefreshRequest _request;
        private readonly List<JsonTreeNode> _reviews = new();
        private readonly List<JsonTreeNode> _documents = new();
        private readonly List<JsonTreeNode> _notices = new();
        private readonly List<JsonTreeNode> _failed = new();
        private readonly List<string> _expired = new();
        private readonly List<string> _cancelled = new();
        private readonly List<string> _opened = new();
        private int _decisions;

        public DueStateReport(DueStateRefreshRequest request)
        {
            _request = request;
        }

        public bool More { get; set; }

        public bool Changed => _reviews.Count + _documents.Count + _notices.Count + _expired.Count + _cancelled.Count + _opened.Count > 0;

        public int ReviewsOpenedCount => _reviews.Count;

        // The reviews the run opened and the documents it turned stale, by ID, for the run's audit entry (the Admin page
        // lists what the last runs did: product review, story step 14).
        public JsonTreeNode ReviewsJson()
        {
            return RuntimeJson.Strings(_reviews.Select(r => RuntimeJson.Text(r, "reviewId")).Where(id => id != null).Select(id => id!));
        }

        public JsonTreeNode DocumentsJson()
        {
            return RuntimeJson.Strings(_documents.Select(d => RuntimeJson.Text(d, "documentLinkId")).Where(id => id != null).Select(id => id!));
        }

        public void Done(DueItem item, JsonTreeNode? payload)
        {
            if (payload == null || payload.Kind != JsonTreeKind.Object)
            {
                return;
            }

            switch (item.Kind)
            {
                case DueItemKind.Approvals:
                    JsonTreeNode? approvals = payload.TryGetMember("approvals", out JsonTreeNode? a) ? a : null;
                    _decisions++;
                    _expired.AddRange(RuntimeJson.StringList(approvals, "expiredRecordIds"));
                    _cancelled.AddRange(RuntimeJson.StringList(approvals, "cancelledRecordIds"));
                    _opened.AddRange(RuntimeJson.StringList(approvals, "openedRecordIds"));
                    break;
                case DueItemKind.OpenReview:
                    _reviews.Add(payload);
                    break;
                case DueItemKind.StaleDocument:
                    _documents.Add(payload);
                    break;
            }

            if (payload.TryGetMember("notices", out JsonTreeNode? notices) && notices.Kind == JsonTreeKind.Array)
            {
                _notices.AddRange(notices.Items);
            }
        }

        public void Failed(DueItem item, Reason? reason)
        {
            _failed.Add(RuntimeJson.Object(
                ("entity", JsonTreeNode.FromString(item.Entity)),
                ("rowId", JsonTreeNode.FromString(item.RowId)),
                ("what", JsonTreeNode.FromString(item.Describe())),
                ("code", RuntimeJson.OptionalText(reason?.Code)),
                ("message", RuntimeJson.OptionalText(reason?.Message))));
        }

        // How many notices of each kind the run sent (remind, repeat, escalate, overdue, expired, flag), for the run's total
        // on the Admin page (wave 4b review): each notice once, whatever its escalated flag.
        public JsonTreeNode NoticeKindsJson()
        {
            return JsonTreeNode.CreateObject(_notices
                .Select(n => RuntimeJson.Text(n, "notice"))
                .Where(k => k != null)
                .GroupBy(k => k!, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new KeyValuePair<string, JsonTreeNode>(g.Key, JsonTreeNode.FromInt64(g.Count()))));
        }

        public JsonTreeNode CountsJson()
        {
            int Count(string notice) => _notices.Count(n => RuntimeJson.Text(n, "notice") == notice);
            return RuntimeJson.Object(
                ("reviewsOpened", JsonTreeNode.FromInt64(_reviews.Count)),
                ("reviewsOverdue", JsonTreeNode.FromInt64(Count("overdue"))),
                ("documentsStale", JsonTreeNode.FromInt64(_documents.Count)),
                ("decisionsRefreshed", JsonTreeNode.FromInt64(_decisions)),
                ("approvalsExpired", JsonTreeNode.FromInt64(_expired.Count)),
                ("approvalsCancelled", JsonTreeNode.FromInt64(_cancelled.Count)),
                ("approvalsOpened", JsonTreeNode.FromInt64(_opened.Count)),
                ("reminders", JsonTreeNode.FromInt64(Count("remind") + Count("repeat"))),
                ("escalations", JsonTreeNode.FromInt64(_notices.Count(n => RuntimeJson.Flag(n, "escalated")))),
                ("flagNotices", JsonTreeNode.FromInt64(Count("flag"))),
                ("failed", JsonTreeNode.FromInt64(_failed.Count)));
        }

        public JsonTreeNode ToJson()
        {
            return RuntimeJson.Object(
                ("trigger", JsonTreeNode.FromString(_request.Trigger)),
                ("dryRun", JsonTreeNode.FromBoolean(_request.DryRun)),
                ("counts", CountsJson()),
                ("reviewsOpened", JsonTreeNode.CreateArray(_reviews)),
                ("documentsStale", JsonTreeNode.CreateArray(_documents)),
                ("approvals", RuntimeJson.Object(
                    ("decisions", JsonTreeNode.FromInt64(_decisions)),
                    ("expiredRecordIds", RuntimeJson.Strings(_expired)),
                    ("cancelledRecordIds", RuntimeJson.Strings(_cancelled)),
                    ("openedRecordIds", RuntimeJson.Strings(_opened)))),
                ("notices", JsonTreeNode.CreateArray(_notices)),
                ("failed", JsonTreeNode.CreateArray(_failed)),
                ("more", JsonTreeNode.FromBoolean(More)));
        }
    }
}
