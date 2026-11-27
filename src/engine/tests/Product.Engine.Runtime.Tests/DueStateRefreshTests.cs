using System;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;
using Product.Engine.Runtime.Callers;
using Product.Engine.Runtime.Ports;
using Xunit;

namespace Product.Engine.Runtime.Tests;

// The daily due-state refresh (document 23, S2 and S1 step 9; document 25, B25-18): the engine's own
// identity, or a platform admin running it now, opens the next review on its Next Due On, expires approval requests past
// their Expires On with the escalation to the deputy, moves a document past its Stale On to stale, sends today's
// reminders and escalations and the flag notices, audits every change, and changes nothing on a second run.
public sealed class DueStateRefreshTests
{
    private static DueStateRefreshRequest Timer(bool dryRun = false) => new(DueStateRefreshRequest.TimerTrigger, dryRun);

    [Fact]
    public void TheTimerOpensTheNextReviewOnItsNextDueOnOnceAsTheEngine()
    {
        Harness h = Harness.Create();
        string done = h.Store.Seed("review", "{\"businessApplication\":\"" + h.ApplicationId + "\",\"reviewType\":\"owner-check\",\"status\":\"review-done\",\"statusMeaning\":\"done\","
            + "\"trigger\":\"cadence\",\"dueOn\":\"2025-11-20\",\"doneOn\":\"2025-11-22\",\"reviewer\":\"" + Harness.Person1 + "\",\"nextDueOn\":\"2026-11-22\",\"recordNumber\":\"REV-0001\"}");

        RuntimeOutcome run = Harness.Ok(h.Runtime.RefreshDueStates(CallerIdentity.Engine(), Timer()));

        StoredRow opened = h.Store.Rows("review").Single(r => r.Id != done);
        Assert.Equal("due", opened.Text("statusMeaning"));
        Assert.Equal("cadence", opened.Text("trigger"));
        Assert.Equal("2026-11-22", opened.Text("dueOn"));
        Assert.StartsWith("REV-", opened.Text("recordNumber"));
        Assert.Equal("engine", opened.Text("performedByActorKind"));
        Assert.Equal(EngineKeys.EnginePrincipalId, opened.Text("performedByPrincipal"));
        Assert.Equal(EngineKeys.EngineClientApp, opened.Text("clientApp"));
        Assert.Equal(1, Count(run, "reviewsOpened"));
        Assert.Contains(h.Store.Rows("audit-entry"), a => a.Text("operation") == EngineRuntime.DueStateRefreshedOperation && a.Text("rowId") == opened.Id);
        Assert.Contains(h.Store.Rows("audit-entry"), a => a.Text("operation") == EngineRuntime.DueStateRunOperation);

        // The run plans again once it opened a review (document 25, B25-20), so it also sends the new review's reminder for
        // today (its cadence repeats every seven days from fourteen days before its due date) in the same run.
        JsonTreeNode reminder = Assert.Single(Notices(run));
        Assert.Equal(opened.Id, RuntimeJsonTestHelpers.Text(reminder, "rowId"));
        Assert.Equal(new[] { Harness.Person1 }, RuntimeJsonTestHelpers.Strings(Member(reminder, "to"), "principalIds"));

        // A second run on the same day opens and sends nothing.
        RuntimeOutcome again = Harness.Ok(h.Runtime.RefreshDueStates(CallerIdentity.Engine(), Timer()));
        Assert.Equal(0, Count(again, "reviewsOpened"));
        Assert.Equal(2, h.Store.Rows("review").Count);
        Assert.Empty(Notices(again));

        // Idempotent: a third run changes and sends nothing.
        int audits = h.Store.Rows("audit-entry").Count;
        RuntimeOutcome third = Harness.Ok(h.Runtime.RefreshDueStates(CallerIdentity.Engine(), Timer()));
        Assert.Empty(Notices(third));
        Assert.Equal(2, h.Store.Rows("review").Count);
        Assert.Equal(audits, h.Store.Rows("audit-entry").Count);
    }

    [Fact]
    public void AnApprovalRequestIsRemindedEscalatedToTheDeputyAndExpiredWithItsRestart()
    {
        Harness h = Harness.Create();
        h.Store.Seed("owner-assignment", "{\"businessApplication\":\"" + h.ApplicationId + "\",\"ownerRole\":\"business-owner\",\"principal\":\"" + Harness.Person3 + "\",\"isDeputy\":true,\"startOn\":\"2026-01-05\"}");
        string decision = Harness.Ok(h.Runtime.Write(Harness.PersonTwo, WriteCommand.Create("decision",
            "{\"decisionType\":\"risk-acceptance\",\"subjectKind\":\"business-application\",\"subjectApplication\":\"" + h.ApplicationId + "\",\"statement\":\"Accept the fixture risk\",\"status\":\"decision-proposed\"}"))).RowId!;
        string record = Assert.Single(RuntimeJsonTestHelpers.Strings(Harness.Ok(h.Runtime.RequestApprovals(Harness.PersonTwo, decision)).Payload, "approvalRecordIds"));

        // Day 2: Balanced's approval reminder repeats every two days, to the business owner the request asks.
        h.Clock.Advance(TimeSpan.FromDays(2));
        RuntimeOutcome day2 = Harness.Ok(h.Runtime.RefreshDueStates(CallerIdentity.Engine(), Timer()));
        JsonTreeNode remind = Assert.Single(Notices(day2));
        Assert.Equal("repeat", RuntimeJsonTestHelpers.Text(remind, "notice"));
        Assert.Equal(record, RuntimeJsonTestHelpers.Text(remind, "rowId"));
        Assert.Equal(new[] { Harness.Person1 }, RuntimeJsonTestHelpers.Strings(Member(remind, "to"), "principalIds"));
        Assert.False(RuntimeJsonTestHelpers.Flag(remind, "escalated"));
        StoredRow sent = h.Store.Rows("audit-entry").Single(a => a.Text("operation") == EngineRuntime.ReminderSentOperation);
        Assert.Equal(record, sent.Text("rowId"));
        Assert.Empty(Notices(Harness.Ok(h.Runtime.RefreshDueStates(CallerIdentity.Engine(), Timer()))));

        // Day 5: escalated to the deputy business owner.
        h.Clock.Advance(TimeSpan.FromDays(3));
        JsonTreeNode escalate = Assert.Single(Notices(Harness.Ok(h.Runtime.RefreshDueStates(CallerIdentity.Engine(), Timer()))));
        Assert.Equal("escalate", RuntimeJsonTestHelpers.Text(escalate, "notice"));
        Assert.True(RuntimeJsonTestHelpers.Flag(escalate, "escalated"));
        Assert.Equal(new[] { Harness.Person3 }, RuntimeJsonTestHelpers.Strings(Member(escalate, "to"), "principalIds"));
        Assert.True(RuntimeJsonTestHelpers.Flag(Member(escalate, "to"), "toDeputies"));

        // Day 10, at Expires On: expired, asked again, and the expiry escalated (S1 step 9).
        h.Clock.Advance(TimeSpan.FromDays(5));
        RuntimeOutcome day10 = Harness.Ok(h.Runtime.RefreshDueStates(CallerIdentity.Engine(), Timer()));
        Assert.Equal("expired", h.Row("approval-record", record).Text("outcomeMeaning"));
        Assert.Equal(1, Count(day10, "approvalsExpired"));
        Assert.Equal(1, Count(day10, "approvalsOpened"));
        Assert.Contains(Notices(day10), n => RuntimeJsonTestHelpers.Text(n, "notice") == "expired" && RuntimeJsonTestHelpers.Text(n, "rowId") == record);
        Assert.Equal("proposed", h.Row("decision", decision).Text("statusMeaning"));
        Assert.Contains(h.Store.Rows("approval-record"), r => r.Id != record && r.Text("outcomeMeaning") == "pending");
        Assert.Contains(h.Store.Rows("audit-entry"), a => a.Text("operation") == EngineRuntime.DueStateRefreshedOperation && a.Text("rowId") == decision);
    }

    [Fact]
    public void ACurrentDocumentPastItsStaleOnBecomesStaleAndAFlagTellsTheOwnersOnce()
    {
        Harness h = Harness.Create();
        string link = h.Store.Seed("document-link", "{\"businessApplication\":\"" + h.ApplicationId + "\",\"documentType\":\"wiki-page\",\"linkKind\":\"ado-wiki-page\","
            + "\"url\":\"https://dev.example.org/wiki/1\",\"status\":\"document-current\",\"statusMeaning\":\"current\",\"lastCheckedOn\":\"2025-11-01\"}");
        string deployment = h.Store.Seed("deployment-record", "{\"businessApplication\":\"" + h.ApplicationId + "\",\"deploymentTarget\":\"" + h.TargetId + "\",\"finishedOn\":\"2026-11-21T10:00:00Z\","
            + "\"gateLevelApplied\":\"flag\",\"isOutsideWindow\":true,\"isOtherPath\":false,\"isMissingApproval\":false,\"isAiOnlyCodeApproval\":false}");

        RuntimeOutcome run = Harness.Ok(h.Runtime.RefreshDueStates(CallerIdentity.Engine(), Timer()));

        Assert.Equal("stale", h.Row("document-link", link).Text("statusMeaning"));
        Assert.Equal("engine", h.Row("document-link", link).Text("performedByActorKind"));
        Assert.Equal(1, Count(run, "documentsStale"));
        JsonTreeNode flag = Assert.Single(Notices(run), n => RuntimeJsonTestHelpers.Text(n, "notice") == "flag");
        Assert.Equal(deployment, RuntimeJsonTestHelpers.Text(flag, "rowId"));
        Assert.Equal(new[] { Harness.Person1 }, RuntimeJsonTestHelpers.Strings(Member(flag, "to"), "principalIds"));
        Assert.Contains("change-manager", RuntimeJsonTestHelpers.Strings(Member(flag, "to"), "roleKeys"));
        Assert.Contains(h.Store.Rows("audit-entry"), a => a.Text("operation") == EngineRuntime.FlagNoticeSentOperation && a.Text("rowId") == deployment);

        RuntimeOutcome again = Harness.Ok(h.Runtime.RefreshDueStates(CallerIdentity.Engine(), Timer()));
        Assert.Empty(Notices(again));
        Assert.Equal(0, Count(again, "documentsStale"));
    }

    [Fact]
    public void OnlyTheEngineOrAPlatformAdminRunsItAndADryRunWritesNothing()
    {
        Harness h = Harness.Create();
        h.Store.Seed("review", "{\"businessApplication\":\"" + h.ApplicationId + "\",\"reviewType\":\"owner-check\",\"status\":\"review-done\",\"statusMeaning\":\"done\","
            + "\"dueOn\":\"2025-11-20\",\"doneOn\":\"2025-11-22\",\"reviewer\":\"" + Harness.Person1 + "\",\"nextDueOn\":\"2026-11-20\"}");

        Assert.True(h.Runtime.RefreshDueStates(Harness.AiClientOf1, new DueStateRefreshRequest(DueStateRefreshRequest.PersonTrigger)).HasReason(ReasonCodes.DueStateRefreshNotAllowed));
        Assert.True(h.Runtime.RefreshDueStates(Harness.PersonOne, new DueStateRefreshRequest(DueStateRefreshRequest.PersonTrigger)).HasReason(ReasonCodes.DueStateRefreshNotAllowed));
        Assert.True(h.Runtime.RefreshDueStates(Harness.ApprovalRunner, Timer()).HasReason(ReasonCodes.DueStateRefreshNotAllowed));

        int rows = h.Store.Rows("review").Count + h.Store.Rows("audit-entry").Count;
        RuntimeOutcome dry = Harness.Ok(h.Runtime.RefreshDueStates(Harness.PersonTwo, new DueStateRefreshRequest(DueStateRefreshRequest.PersonTrigger, dryRun: true)));
        Assert.Equal(1, Count(dry, "reviewsOpened"));
        Assert.Equal(rows, h.Store.Rows("review").Count + h.Store.Rows("audit-entry").Count);

        // A platform admin's run: every row is the engine's, on behalf of the admin who started it.
        Harness.Ok(h.Runtime.RefreshDueStates(Harness.PersonTwo, new DueStateRefreshRequest(DueStateRefreshRequest.PersonTrigger)));
        StoredRow opened = h.Store.Rows("review").Single(r => r.Text("statusMeaning") == "due");
        Assert.Equal("engine", opened.Text("performedByActorKind"));
        Assert.Equal(Harness.Person2, opened.Text("onBehalfOf"));
        StoredRow runEntry = h.Store.Rows("audit-entry").Single(a => a.Text("operation") == EngineRuntime.DueStateRunOperation);
        Assert.Equal(Harness.Person2, runEntry.Text("actor"));
    }

    private static int Count(RuntimeOutcome outcome, string name)
    {
        JsonTreeNode? value = Member(Member(outcome.Payload, "counts"), name);
        return value != null && value.TryGetInt64(out long n) ? (int)n : -1;
    }

    private static JsonTreeNode[] Notices(RuntimeOutcome outcome)
    {
        JsonTreeNode? notices = Member(outcome.Payload, "notices");
        return notices == null || notices.Kind != JsonTreeKind.Array ? Array.Empty<JsonTreeNode>() : notices.Items.ToArray();
    }

    private static JsonTreeNode? Member(JsonTreeNode? node, string name)
    {
        return node != null && node.Kind == JsonTreeKind.Object && node.TryGetMember(name, out JsonTreeNode? value) ? value : null;
    }
}
