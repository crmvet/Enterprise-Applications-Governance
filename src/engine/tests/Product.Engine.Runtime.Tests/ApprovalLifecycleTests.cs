using System;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;
using Product.Engine.Runtime.Callers;
using Product.Engine.Runtime.Ports;
using Xunit;

namespace Product.Engine.Runtime.Tests;

// The life of a decision's Approval Records (document 23, S1 steps 3, 8 and 9; document 22, section 8.2): one pending
// record per required response, a sequential group one step at a time, one person counted once, the records still
// pending cancelled when the decision settles, and the approval refresh that expires a record past Expires On and
// restarts it with a new one. Every record write here is the engine's own; only the approval runner answers.
public sealed class ApprovalLifecycleTests
{
    // Balanced's acceptance rule (the business owner, everyone must approve) with Minimum Count 2 and a five-day timeout.
    private const string TwoOwners = "[{\"op\":\"upsert\",\"entity\":\"approval-rule\",\"key\":\"acceptance-approval\",\"values\":{\"minimumCount\":2,\"timeoutDays\":5}}]";

    // For a tier 1 application, the business owner accepts first, then a platform admin (a more specific group).
    private const string TwoSteps = "[{\"op\":\"upsert\",\"entity\":\"approval-rule\",\"key\":\"acceptance-step-one\",\"values\":{\"label\":\"The business owner accepts a tier 1 application first\","
        + "\"decisionTypeKey\":\"acceptance\",\"tierKey\":\"tier-1\",\"approverOwnerRoleKey\":\"business-owner\",\"approvalMode\":\"sequential\",\"sequence\":1,\"timeoutDays\":5}},"
        + "{\"op\":\"upsert\",\"entity\":\"approval-rule\",\"key\":\"acceptance-step-two\",\"values\":{\"label\":\"Then a platform admin accepts it\","
        + "\"decisionTypeKey\":\"acceptance\",\"tierKey\":\"tier-1\",\"approverRoleKey\":\"platform-admin\",\"approvalMode\":\"sequential\",\"sequence\":2,\"timeoutDays\":5}}]";

    // Balanced's acceptance rule with a five-day timeout, and a requester consulted.
    private const string OneOwnerAndAConsultation = "[{\"op\":\"upsert\",\"entity\":\"approval-rule\",\"key\":\"acceptance-approval\",\"values\":{\"timeoutDays\":5}},"
        + "{\"op\":\"upsert\",\"entity\":\"approval-rule\",\"key\":\"acceptance-consult-requesters\",\"values\":{\"label\":\"A requester is consulted\","
        + "\"decisionTypeKey\":\"acceptance\",\"approverRoleKey\":\"requester\",\"approvalMode\":\"first-to-respond\",\"isConsultation\":true}}]";

    // ---- One record per required response ----

    // A rule with Minimum Count 2 opens two pending records, and the decision is accepted only after two different
    // people approve. A second approval from the same person does not count, so the engine opens a third record for it.
    [Fact]
    public void AMinimumCountOfTwoNeedsTwoDifferentPeople()
    {
        Harness h = Harness.Create(TwoOwners);
        string decision = AcceptanceDecision(h);
        RuntimeOutcome request = Harness.Ok(h.Runtime.RequestApprovals(Harness.AiClientOf1, decision));
        string[] records = RuntimeJsonTestHelpers.Strings(request.Payload, "approvalRecordIds");
        Assert.Equal(2, records.Length);
        Assert.Equal(2, RuntimeJsonTestHelpers.Member(request.Payload, "requestedCount")!.TryGetInt64(out long count) ? count : 0);
        Assert.All(records, r => Assert.Equal("acceptance-approval", Harness.Text(h.Row("approval-record", r), "approvalRule")));
        Assert.All(records, r => Assert.Equal("pending", Harness.Text(h.Row("approval-record", r), "outcomeMeaning")));
        Assert.All(records, r => Assert.Equal("2026-11-27T09:00:00Z", Harness.Text(h.Row("approval-record", r), "expiresOn")));

        // The first approval counts, and the second card is still out: nothing new opens.
        RuntimeOutcome first = Harness.Ok(h.Answer(records[0], "approval-approved", Harness.Person2));
        Assert.Equal("pending", RuntimeJsonTestHelpers.Text(first.Results["OP-03"], "outcome"));
        Assert.Empty(Opened(first));
        Assert.Equal("proposed", Harness.Text(h.Row("decision", decision), "statusMeaning"));

        // The same person answers the second card too: it does not count, and the engine restarts it with a new record.
        RuntimeOutcome again = Harness.Ok(h.Answer(records[1], "approval-approved", Harness.Person2));
        Assert.True(again.HasReason(ReasonCodes.ApprovalResponderAlreadyCounted), again.ToString());
        Assert.False(h.Row("approval-record", records[1]).Flag("countsTowardRequirement"));
        string restarted = Assert.Single(Opened(again));
        Assert.Equal(EffectKind.RecomputeApprovals, again.Writes.Single(w => w.RowId == restarted).Effect);
        Assert.Equal("pending", Harness.Text(h.Row("approval-record", restarted), "outcomeMeaning"));
        Assert.Equal("acceptance-approval", Harness.Text(h.Row("approval-record", restarted), "approvalRule"));
        Assert.Equal("proposed", Harness.Text(h.Row("decision", decision), "statusMeaning"));

        // A second person answers the restarted card: two different people, so the decision is accepted.
        RuntimeOutcome second = Harness.Ok(h.Answer(restarted, "approval-approved", Harness.Person3));
        Assert.Equal("satisfied", RuntimeJsonTestHelpers.Text(second.Results["OP-03"], "outcome"));
        Assert.Equal("accepted", Harness.Text(h.Row("decision", decision), "statusMeaning"));
        Assert.True(h.Row("approval-record", records[0]).Flag("countsTowardRequirement"));
        Assert.True(h.Row("approval-record", restarted).Flag("countsTowardRequirement"));
        Assert.Equal(3, h.Store.Rows("approval-record").Count);
    }

    // A sequential group opens one step at a time: the second step's record opens in the same call that satisfies the
    // first step, and the decision is accepted only after the last step.
    [Fact]
    public void ASequentialGroupOpensOneStepAtATime()
    {
        Harness h = Harness.Create(TwoSteps);
        string decision = AcceptanceDecision(h);
        string stepOne = Assert.Single(RuntimeJsonTestHelpers.Strings(Harness.Ok(h.Runtime.RequestApprovals(Harness.AiClientOf1, decision)).Payload, "approvalRecordIds"));
        Assert.Equal("acceptance-step-one", Harness.Text(h.Row("approval-record", stepOne), "approvalRule"));
        Assert.Single(h.Store.Rows("approval-record"));

        RuntimeOutcome first = Harness.Ok(h.Answer(stepOne, "approval-approved", Harness.Person2));
        string stepTwo = Assert.Single(Opened(first));
        Assert.Equal("acceptance-step-two", Harness.Text(h.Row("approval-record", stepTwo), "approvalRule"));
        Assert.Equal("platform-admin", Harness.Text(h.Row("approval-record", stepTwo), "requiredRole"));
        Assert.Equal("pending", Harness.Text(h.Row("approval-record", stepTwo), "outcomeMeaning"));
        Assert.Equal("proposed", Harness.Text(h.Row("decision", decision), "statusMeaning"));

        Harness.Ok(h.Answer(stepTwo, "approval-approved", Harness.Person3));
        Assert.Equal("accepted", Harness.Text(h.Row("decision", decision), "statusMeaning"));
        Assert.Equal(2, h.Store.Rows("approval-record").Count);
    }

    // ---- Settling cancels what is still pending ----

    // A counting rejection rejects the decision, and the card still out for the same requirement is cancelled in the same
    // call, so the approval runner sends nothing more for a settled decision.
    [Fact]
    public void ARejectionCancelsTheRecordsStillPending()
    {
        Harness h = Harness.Create(TwoOwners);
        string decision = AcceptanceDecision(h);
        string[] records = RuntimeJsonTestHelpers.Strings(Harness.Ok(h.Runtime.RequestApprovals(Harness.AiClientOf1, decision)).Payload, "approvalRecordIds");

        RuntimeOutcome rejected = Harness.Ok(h.Answer(records[0], "approval-rejected", Harness.Person2, typedReason: "Duplicate of an existing app"));
        Assert.Equal("rejected", Harness.Text(h.Row("decision", decision), "statusMeaning"));

        // Decided On is when the decision was accepted or rejected (document 22, O09), so the pages show the day, not
        // "Waiting".
        Assert.Equal(CanonicalJson.FormatUtc(h.Clock.UtcNow), Harness.Text(h.Row("decision", decision), "decidedOn"));
        Assert.Equal("cancelled", Harness.Text(h.Row("approval-record", records[1]), "outcomeMeaning"));
        Assert.Equal("approval-cancelled", Harness.Text(h.Row("approval-record", records[1]), "outcome"));
        Assert.Contains(rejected.Writes, w => w.RowId == records[1] && w.Effect == EffectKind.RecomputeApprovals && w.IsEngineNested);
        Assert.Empty(Opened(rejected));
    }

    // An acceptance cancels a consultation card nobody answered. A consultation is never restarted.
    [Fact]
    public void AnAcceptanceCancelsAnOpenConsultation()
    {
        Harness h = Harness.Create(OneOwnerAndAConsultation);
        string decision = AcceptanceDecision(h);
        string[] records = RuntimeJsonTestHelpers.Strings(Harness.Ok(h.Runtime.RequestApprovals(Harness.AiClientOf1, decision)).Payload, "approvalRecordIds");
        Assert.Equal(2, records.Length);
        string counting = records.Single(r => !h.Row("approval-record", r).Flag("isConsultation"));
        string consultation = records.Single(r => h.Row("approval-record", r).Flag("isConsultation"));

        Harness.Ok(h.Answer(counting, "approval-approved", Harness.Person2));
        Assert.Equal("accepted", Harness.Text(h.Row("decision", decision), "statusMeaning"));
        Assert.Equal("cancelled", Harness.Text(h.Row("approval-record", consultation), "outcomeMeaning"));
        Assert.Equal(2, h.Store.Rows("approval-record").Count);
    }

    // ---- Restarts ----

    // An AI client's answer is stored as recommendation only and never counts (AI never approves). The requirement
    // still needs a person, so the engine opens a new record for it in the same call; a person's answer there accepts.
    [Fact]
    public void AnAnswerThatDoesNotCountIsRestartedAtOnce()
    {
        Harness h = Harness.Create();
        string decision = h.IntakeDecision(tierTwo: true);
        string record = Assert.Single(RuntimeJsonTestHelpers.Strings(Harness.Ok(h.Runtime.RequestApprovals(Harness.AiClientOf1, decision)).Payload, "approvalRecordIds"));

        RuntimeOutcome ai = Harness.Ok(h.Answer(record, "approval-approved", Harness.Person2, actorKind: "ai-client"));
        Assert.Equal("recommendation-only", Harness.Text(h.Row("approval-record", record), "outcomeMeaning"));
        string restarted = Assert.Single(Opened(ai));
        StoredRow fresh = h.Row("approval-record", restarted);
        Assert.Equal("pending", Harness.Text(fresh, "outcomeMeaning"));
        Assert.Equal("intake-a", Harness.Text(fresh, "decisionRight"));
        Assert.Equal("platform-admin", Harness.Text(fresh, "requiredRole"));
        Assert.Equal("engine", Harness.Text(fresh, "performedByActorKind"));
        Assert.Equal("proposed", Harness.Text(h.Row("decision", decision), "statusMeaning"));

        Harness.Ok(h.Answer(restarted, "approval-approved", Harness.Person2));
        Assert.Equal("accepted", Harness.Text(h.Row("decision", decision), "statusMeaning"));
    }

    // The refresh finds a pending record at its Expires On, writes the expired outcome and opens a new pending record
    // for the same requirement with a new Expires On. A second refresh has nothing to do and writes nothing.
    [Fact]
    public void TheRefreshExpiresARecordAndRestartsIt()
    {
        Harness h = Harness.Create(OneOwnerAndAConsultation);
        string decision = AcceptanceDecision(h);
        string[] records = RuntimeJsonTestHelpers.Strings(Harness.Ok(h.Runtime.RequestApprovals(Harness.AiClientOf1, decision)).Payload, "approvalRecordIds");
        string counting = records.Single(r => !h.Row("approval-record", r).Flag("isConsultation"));
        string consultation = records.Single(r => h.Row("approval-record", r).Flag("isConsultation"));

        // Not due yet: nothing is found, and a refresh writes nothing.
        Assert.Empty(RuntimeJsonTestHelpers.Strings(Harness.Ok(h.Runtime.FindDueApprovals(Harness.ApprovalRunner)).Payload, "decisionIds"));
        string before = h.Store.Dump();
        RuntimeOutcome early = Harness.Ok(h.Runtime.RefreshApprovals(Harness.ApprovalRunner, decision));
        Assert.Empty(early.Writes);
        Assert.Equal(before, h.Store.Dump());

        // Five days later the record is due.
        h.Clock.Advance(TimeSpan.FromDays(5));
        RuntimeOutcome due = Harness.Ok(h.Runtime.FindDueApprovals(Harness.ApprovalRunner));
        Assert.Equal(new[] { decision }, RuntimeJsonTestHelpers.Strings(due.Payload, "decisionIds"));
        Assert.False(RuntimeJsonTestHelpers.Flag(due.Payload, "more"));
        Assert.Empty(due.Writes);

        RuntimeOutcome refresh = Harness.Ok(h.Runtime.RefreshApprovals(Harness.ApprovalRunner, decision));
        Assert.Equal(new[] { counting }, RuntimeJsonTestHelpers.Strings(refresh.Payload, "expiredRecordIds"));
        string restarted = Assert.Single(RuntimeJsonTestHelpers.Strings(refresh.Payload, "openedRecordIds"));
        Assert.Empty(RuntimeJsonTestHelpers.Strings(refresh.Payload, "cancelledRecordIds"));
        Assert.Equal("proposed", RuntimeJsonTestHelpers.Text(refresh.Payload, "decisionStatus"));

        StoredRow expired = h.Row("approval-record", counting);
        Assert.Equal("expired", Harness.Text(expired, "outcomeMeaning"));
        Assert.Equal("approval-expired", Harness.Text(expired, "outcome"));
        Assert.False(expired.Flag("countsTowardRequirement"));
        Assert.Null(expired.Text("approver"));
        StoredRow fresh = h.Row("approval-record", restarted);
        Assert.Equal("acceptance-approval", Harness.Text(fresh, "approvalRule"));
        Assert.Equal("2026-11-27T09:00:00Z", Harness.Text(fresh, "requestedOn"));
        Assert.Equal("2026-12-02T09:00:00Z", Harness.Text(fresh, "expiresOn"));

        // The consultation has no timeout: it stays pending and is never restarted.
        Assert.Equal("pending", Harness.Text(h.Row("approval-record", consultation), "outcomeMeaning"));

        // Nothing is due any more, and a second refresh writes nothing.
        Assert.Empty(RuntimeJsonTestHelpers.Strings(Harness.Ok(h.Runtime.FindDueApprovals(Harness.ApprovalRunner)).Payload, "decisionIds"));
        string after = h.Store.Dump();
        Assert.Empty(Harness.Ok(h.Runtime.RefreshApprovals(Harness.ApprovalRunner, decision)).Writes);
        Assert.Equal(after, h.Store.Dump());

        // The restarted record is answered as any other, and the decision is accepted; the consultation is cancelled.
        Harness.Ok(h.Answer(restarted, "approval-approved", Harness.Person2));
        Assert.Equal("accepted", Harness.Text(h.Row("decision", decision), "statusMeaning"));
        Assert.Equal("cancelled", Harness.Text(h.Row("approval-record", consultation), "outcomeMeaning"));
    }

    // The recompute after an answer also expires the other records of the decision that are past Expires On, and
    // restarts them, so an answer never leaves an expired card counted as open.
    [Fact]
    public void AnAnswersRecomputeExpiresTheOtherDueRecords()
    {
        Harness h = Harness.Create(TwoOwners);
        string decision = AcceptanceDecision(h);
        string[] records = RuntimeJsonTestHelpers.Strings(Harness.Ok(h.Runtime.RequestApprovals(Harness.AiClientOf1, decision)).Payload, "approvalRecordIds");

        // The answer was given in time (Responded On is the request day); it reaches the engine after Expires On.
        h.Clock.Advance(TimeSpan.FromDays(6));
        RuntimeOutcome answer = Harness.Ok(h.Answer(records[0], "approval-approved", Harness.Person2));
        Assert.True(h.Row("approval-record", records[0]).Flag("countsTowardRequirement"));
        Assert.Equal("expired", Harness.Text(h.Row("approval-record", records[1]), "outcomeMeaning"));
        string restarted = Assert.Single(Opened(answer));
        Assert.Equal("2026-12-03T09:00:00Z", Harness.Text(h.Row("approval-record", restarted), "expiresOn"));
        Assert.Equal("proposed", Harness.Text(h.Row("decision", decision), "statusMeaning"));
    }

    // A record left pending on a decision that is no longer proposed (written before the engine cancelled on settling)
    // is found by the refresh and cancelled; the decision itself does not change.
    [Fact]
    public void TheRefreshCancelsARecordLeftPendingOnASettledDecision()
    {
        Harness h = Harness.Create();
        string decision = h.IntakeDecision(tierTwo: true);
        string record = Assert.Single(RuntimeJsonTestHelpers.Strings(Harness.Ok(h.Runtime.RequestApprovals(Harness.AiClientOf1, decision)).Payload, "approvalRecordIds"));
        Harness.Ok(h.Answer(record, "approval-approved", Harness.Person2));
        Assert.Equal("accepted", Harness.Text(h.Row("decision", decision), "statusMeaning"));
        string stray = h.Store.Seed("approval-record", "{\"decision\":\"" + decision + "\",\"decisionRight\":\"intake-a\",\"requiredRole\":\"platform-admin\",\"isConsultation\":false,"
            + "\"outcome\":\"approval-pending\",\"outcomeMeaning\":\"pending\",\"requestedOn\":\"2026-11-22T09:00:00Z\",\"countsTowardRequirement\":false}");
        string accepted = CanonicalJson.Write(h.Row("decision", decision).Columns, CanonicalProfile.Request);

        Assert.Equal(new[] { decision }, RuntimeJsonTestHelpers.Strings(Harness.Ok(h.Runtime.FindDueApprovals(Harness.ApprovalRunner)).Payload, "decisionIds"));
        RuntimeOutcome refresh = Harness.Ok(h.Runtime.RefreshApprovals(Harness.ApprovalRunner, decision));
        Assert.Equal(new[] { stray }, RuntimeJsonTestHelpers.Strings(refresh.Payload, "cancelledRecordIds"));
        Assert.Equal("accepted", RuntimeJsonTestHelpers.Text(refresh.Payload, "decisionStatus"));
        Assert.Equal("cancelled", Harness.Text(h.Row("approval-record", stray), "outcomeMeaning"));
        Assert.Equal(accepted, CanonicalJson.Write(h.Row("decision", decision).Columns, CanonicalProfile.Request));
        Assert.Empty(RuntimeJsonTestHelpers.Strings(Harness.Ok(h.Runtime.FindDueApprovals(Harness.ApprovalRunner)).Payload, "decisionIds"));
    }

    // FindDueApprovals returns at most the number asked for, in ID order, and says when more wait.
    [Fact]
    public void FindDueApprovalsPagesInIdOrder()
    {
        Harness h = Harness.Create(TwoOwners);
        string one = AcceptanceDecision(h);
        string two = AcceptanceDecision(h);
        Harness.Ok(h.Runtime.RequestApprovals(Harness.AiClientOf1, one));
        Harness.Ok(h.Runtime.RequestApprovals(Harness.AiClientOf1, two));
        h.Clock.Advance(TimeSpan.FromDays(7));

        RuntimeOutcome first = Harness.Ok(h.Runtime.FindDueApprovals(Harness.ApprovalRunner, maxDecisions: 1));
        string[] expected = new[] { one, two }.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { expected[0] }, RuntimeJsonTestHelpers.Strings(first.Payload, "decisionIds"));
        Assert.True(RuntimeJsonTestHelpers.Flag(first.Payload, "more"));
        Assert.Equal(expected, RuntimeJsonTestHelpers.Strings(Harness.Ok(h.Runtime.FindDueApprovals(Harness.ApprovalRunner)).Payload, "decisionIds"));
        Assert.Throws<ArgumentOutOfRangeException>(() => h.Runtime.FindDueApprovals(Harness.ApprovalRunner, maxDecisions: 0));
    }

    // ---- The approval runner's second answer ----

    // A different second answer from the approval runner for a record that already holds a final outcome is a conflict
    // with the stored answer (approval.outcome-final, the runtime outcome conflict), not a refusal of the writer;
    // responder columns sent without an outcome stay a write the runner never makes (approval.outcome-writer, refused).
    // Nothing changes either way.
    [Fact]
    public void ARunnersSecondDifferentAnswerIsAConflictWithTheStoredAnswer()
    {
        Harness h = Harness.Create();
        string decision = h.IntakeDecision(tierTwo: true);
        string record = Assert.Single(RuntimeJsonTestHelpers.Strings(Harness.Ok(h.Runtime.RequestApprovals(Harness.AiClientOf1, decision)).Payload, "approvalRecordIds"));
        Harness.Ok(h.Answer(record, "approval-approved", Harness.Person2));
        string after = h.Store.Dump();

        RuntimeOutcome flipped = h.Answer(record, "approval-rejected", Harness.Person2, typedReason: "Changed my mind");
        Assert.Equal("conflict", flipped.Outcome);
        Assert.True(flipped.HasReason(ReasonCodes.ApprovalOutcomeFinal), flipped.ToString());
        Assert.False(flipped.HasReason(ReasonCodes.ApprovalOutcomeWriter), flipped.ToString());
        Assert.Equal(after, h.Store.Dump());

        RuntimeOutcome partial = h.Runtime.Write(Harness.ApprovalRunner, WriteCommand.Update("approval-record", record, "{\"approverHoldsRequiredRole\":false}"));
        Assert.Equal("refused", partial.Outcome);
        Assert.True(partial.HasReason(ReasonCodes.ApprovalOutcomeWriter), partial.ToString());
        Assert.Equal(after, h.Store.Dump());
    }

    // ---- Who refreshes ----

    // Only the approval runner refreshes approvals: a person, an AI client, a pipeline with another grant and an agent
    // identity are refused, and nothing is written.
    [Fact]
    public void OnlyTheApprovalRunnerRefreshes()
    {
        Harness h = Harness.Create(TwoOwners);
        string decision = AcceptanceDecision(h);
        Harness.Ok(h.Runtime.RequestApprovals(Harness.AiClientOf1, decision));
        h.Clock.Advance(TimeSpan.FromDays(6));
        string before = h.Store.Dump();

        CallerIdentity[] others =
        {
            Harness.PersonTwo,
            Harness.AiClientOf1,
            Harness.BuildPipeline,
            CallerIdentity.Workload(Harness.AgentIdentity, PrincipalKind.AgentIdentity),
        };
        foreach (CallerIdentity caller in others)
        {
            RuntimeOutcome refresh = h.Runtime.RefreshApprovals(caller, decision);
            Assert.True(refresh.Outcome == "refused", refresh.ToString());
            Assert.Empty(refresh.Writes);
            RuntimeOutcome find = h.Runtime.FindDueApprovals(caller);
            Assert.True(find.Outcome == "refused", find.ToString());
            Assert.Null(find.Payload);
            Assert.Equal(before, h.Store.Dump());
        }

        foreach (CallerIdentity caller in others.Take(3))
        {
            Assert.True(h.Runtime.RefreshApprovals(caller, decision).HasReason(ReasonCodes.ApprovalRefreshNotAllowed));
            Assert.True(h.Runtime.FindDueApprovals(caller).HasReason(ReasonCodes.ApprovalRefreshNotAllowed));
        }

        Assert.Equal("not-found", h.Runtime.RefreshApprovals(Harness.ApprovalRunner, "00000000-0000-4000-9999-000000000000").Outcome);
        Assert.Equal(before, h.Store.Dump());
    }

    // A person proposes accepting the harness application (tier 1), a decision type that requires approval.
    private static string AcceptanceDecision(Harness h)
    {
        return Harness.Ok(h.Runtime.Write(Harness.PersonOne, WriteCommand.Create("decision",
            "{\"decisionType\":\"acceptance\",\"subjectKind\":\"business-application\",\"subjectApplication\":\"" + h.ApplicationId + "\",\"statement\":\"Accept the fixture app\",\"status\":\"decision-proposed\"}"))).RowId!;
    }

    // The IDs of the Approval Records a call created.
    private static string[] Opened(RuntimeOutcome outcome)
    {
        return outcome.Writes.Where(w => w.Entity == "approval-record" && w.Operation == WriteOperation.Create).Select(w => w.RowId).ToArray();
    }
}
