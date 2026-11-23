using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Product.Azure.Stores;
using Product.Engine.Core.Canonical;
using Product.Engine.Runtime.Ports;
using Xunit;

namespace Product.Azure.Tests;

// A rule that needs two answers, and the engine's own expiry and restarts, end to end over HTTP with the fake runner. The
// demo's change authorization rule (the application's business owner, everyone must approve, five days) gets Minimum
// Count 2, and Inspection Scheduler gets a second business owner, so a change to it needs two different owners. The
// engine opens one Approval Record per required answer, counts one person once per requirement, never counts an AI's
// answer, cancels what is still open when the decision settles, and expires and restarts a record past its Expires On
// when the runner's claim asks it to. The host only relays: it decides nothing about counts or expiry.
public sealed class TwoAnswerApprovalTests
{
    // Ines Varga, a fictional person of the walkthrough estate: the business owner of the other three applications.
    private const string InesId = "00000000-0000-4000-8000-000000000215";

    private const string TwoOwners = "[{\"op\":\"upsert\",\"entity\":\"approval-rule\",\"key\":\"change-auth-owner\",\"values\":{\"minimumCount\":2}}]";

    [Fact]
    public async Task ARuleNeedingTwoAnswersNeedsTwoDifferentPeopleAndNoAiAnswerCounts()
    {
        await using ProductTestHost host = Start();
        (string change, string decisionId, string[] approvals) = await SubmitChange(host);
        Assert.Equal(2, approvals.Length);
        DateTime start = host.Clock.UtcNow;

        // Two requests, one card each, both asking the two business owners.
        var runner = new FakeApprovalRunner(host);
        IReadOnlyList<RunnerItem> cards = await runner.Claim();
        Assert.Equal(approvals.OrderBy(i => i, StringComparer.Ordinal), cards.Select(c => c.ApprovalRecordId).OrderBy(i => i, StringComparer.Ordinal));
        foreach (RunnerItem card in cards)
        {
            Assert.Equal("owner-role", card.AskKind);
            Assert.Equal(new[] { Personas.NadiaId, InesId }, card.AskedPrincipals.OrderBy(i => i, StringComparer.Ordinal));
            Assert.Equal(200, (await runner.Sent(card)).Status);
        }

        // 1. Nadia approves the first card: it counts, and the decision still waits for a second person.
        HttpCall first = await runner.Answer(cards[0], Personas.NadiaId, approve: true, start.AddMinutes(10));
        Assert.True(first.Status == 200, first + "\n" + host.Logs);
        Assert.True(Data(first)["countsTowardRequirement"]!.GetValue<bool>(), first.Body);
        Assert.False(Data(first)["decisionAccepted"]!.GetValue<bool>());
        Assert.Equal("proposed", Data(first)["decisionStatus"]!.GetValue<string>());
        Assert.Empty(Data(first)["openedApprovalRecordIds"]!.AsArray());
        Assert.Contains("still waits", first.Json!["summary"]!.GetValue<string>(), StringComparison.Ordinal);

        // 2. Nadia approves the second card too: one person counts once per requirement, so it does not count, and the
        //    engine opens a new request for the second answer in the same transaction.
        HttpCall twice = await runner.Answer(cards[1], Personas.NadiaId, approve: true, start.AddMinutes(20));
        Assert.True(twice.Status == 200, twice + "\n" + host.Logs);
        Assert.False(Data(twice)["countsTowardRequirement"]!.GetValue<bool>());
        Assert.Contains(twice.Json!["warnings"]!.AsArray(), w => w!["code"]?.GetValue<string>() == "approval.responder-already-counted");
        Assert.Equal("proposed", Data(twice)["decisionStatus"]!.GetValue<string>());
        string third = Assert.Single(Data(twice)["openedApprovalRecordIds"]!.AsArray())!.GetValue<string>();

        // 3. The new card asks only the owner whose answer does not count yet.
        RunnerItem thirdCard = Assert.Single(await runner.Claim());
        Assert.Equal(third, thirdCard.ApprovalRecordId);
        Assert.Equal(new[] { InesId }, thirdCard.AskedPrincipals);
        Assert.Equal(200, (await runner.Sent(thirdCard)).Status);

        // 4. The runner reports the answer as an AI client's acting for Ines: stored as recommendation only, never counted,
        //    and the engine asks again.
        HttpCall byAi = await runner.Answer(thirdCard, InesId, approve: true, start.AddMinutes(30), actorKind: "ai-client");
        Assert.True(byAi.Status == 200, byAi + "\n" + host.Logs);
        Assert.Equal("recommendation-only", Data(byAi)["outcome"]!["meaning"]!.GetValue<string>());
        Assert.False(Data(byAi)["countsTowardRequirement"]!.GetValue<bool>());
        Assert.Contains(byAi.Json!["warnings"]!.AsArray(), w => w!["code"]?.GetValue<string>() == "approval.ai-actor-not-counted");
        Assert.Equal("proposed", Data(byAi)["decisionStatus"]!.GetValue<string>());
        string fourth = Assert.Single(Data(byAi)["openedApprovalRecordIds"]!.AsArray())!.GetValue<string>();

        // 5. Ines approves the fourth card herself: two different people, so the engine accepts the decision.
        RunnerItem fourthCard = Assert.Single(await runner.Claim());
        Assert.Equal(fourth, fourthCard.ApprovalRecordId);
        Assert.Equal(new[] { InesId }, fourthCard.AskedPrincipals);
        Assert.Equal(200, (await runner.Sent(fourthCard)).Status);
        HttpCall second = await runner.Answer(fourthCard, InesId, approve: true, start.AddMinutes(40));
        Assert.True(second.Status == 200, second + "\n" + host.Logs);
        Assert.True(Data(second)["countsTowardRequirement"]!.GetValue<bool>(), second.Body);
        Assert.True(Data(second)["decisionAccepted"]!.GetValue<bool>(), second.Body);
        Assert.Equal("accepted", Data(second)["decisionStatus"]!.GetValue<string>());

        // The records: Nadia's first and Ines's own answer count; Nadia's second and the AI's never do.
        IRecordStore records = host.Get<IRecordStore>();
        Assert.True(records.Get("approval-record", approvals.Single(a => a == cards[0].ApprovalRecordId))!.Flag("countsTowardRequirement"));
        Assert.False(records.Get("approval-record", cards[1].ApprovalRecordId)!.Flag("countsTowardRequirement"));
        Assert.False(records.Get("approval-record", third)!.Flag("countsTowardRequirement"));
        Assert.True(records.Get("approval-record", fourth)!.Flag("countsTowardRequirement"));
        Assert.Equal(4, records.Query(RowQuery.For("approval-record").Where("decision", decisionId)).Count);
        Assert.Equal("accepted", records.Get("decision", decisionId)!.Text("statusMeaning"));

        // Priya, on the web app, sees the change authorized by two people's answers, and the runner has nothing left.
        HttpCall status = await host.Post("/api/get_approval_status", Personas.PriyaWeb, "{\"subjectKind\":\"change-request\",\"subjectId\":\"" + change + "\"}");
        Assert.True(status.Status == 200, status.ToString());
        Assert.True(status.Json!["data"]!["ready"]!.GetValue<bool>(), status.Body);
        Assert.Equal(2, status.Json["data"]!["records"]!.AsArray().Count(r => r!["countsTowardRequirement"]!.GetValue<bool>()));
        Assert.Empty(await runner.Claim());
        var outbox = new ApprovalOutbox(host.Get<IProductStore>());
        Assert.All(new[] { cards[0].ApprovalRecordId, cards[1].ApprovalRecordId, third, fourth }, id => Assert.Equal(ApprovalOutboxStates.Answered, outbox.Get(id)!.State));
    }

    [Fact]
    public async Task ARejectionSettlesTheDecisionAndCancelsTheOtherCard()
    {
        await using ProductTestHost host = Start();
        (_, string decisionId, string[] approvals) = await SubmitChange(host);
        var runner = new FakeApprovalRunner(host);
        IReadOnlyList<RunnerItem> cards = await runner.Claim();
        Assert.Equal(2, cards.Count);

        // Nadia rejects: a counting rejection rejects the decision, and the engine cancels the other open request in the
        // same transaction, which settles its outbox row.
        HttpCall rejected = await runner.Answer(cards[0], Personas.NadiaId, approve: false, host.Clock.UtcNow.AddMinutes(5));
        Assert.True(rejected.Status == 200, rejected + "\n" + host.Logs);
        Assert.Equal("rejected", Data(rejected)["decisionStatus"]!.GetValue<string>());
        Assert.Equal(new[] { cards[1].ApprovalRecordId }, Data(rejected)["cancelledApprovalRecordIds"]!.AsArray().Select(i => i!.GetValue<string>()));
        Assert.Contains("rejected the decision", rejected.Json!["summary"]!.GetValue<string>(), StringComparison.Ordinal);
        IRecordStore records = host.Get<IRecordStore>();
        Assert.Equal("cancelled", records.Get("approval-record", cards[1].ApprovalRecordId)!.Text("outcomeMeaning"));
        Assert.Equal(ApprovalOutboxStates.Cancelled, new ApprovalOutbox(host.Get<IProductStore>()).Get(cards[1].ApprovalRecordId)!.State);

        // Ines answers the cancelled card anyway: the engine refuses it, and nothing changes.
        string before = Dump(host);
        HttpCall late = await runner.Answer(cards[1], InesId, approve: true, host.Clock.UtcNow.AddMinutes(6));
        Assert.Equal(409, late.Status);
        Assert.Equal("conflict", late.Json!["error"]!["code"]!.GetValue<string>());
        Assert.Contains("final outcome cancelled", late.Json["error"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(before, Dump(host));
        Assert.Equal("rejected", records.Get("decision", decisionId)!.Text("statusMeaning"));
        Assert.Empty(await runner.Claim());
        Assert.Contains(approvals, a => a == cards[1].ApprovalRecordId);
    }

    [Fact]
    public async Task AnExpiredRequestIsRefusedByTheEngineAndRestartedOnRecord()
    {
        await using ProductTestHost host = Start();
        (_, string decisionId, _) = await SubmitChange(host);
        DateTime start = host.Clock.UtcNow;
        var runner = new FakeApprovalRunner(host);
        IReadOnlyList<RunnerItem> cards = await runner.Claim();
        Assert.Equal(2, cards.Count);
        Assert.All(cards, c => Assert.Equal(start.AddDays(5), ExpiresOn(c)));
        foreach (RunnerItem card in cards)
        {
            Assert.Equal(200, (await runner.Sent(card)).Status);
        }

        HttpCall first = await runner.Answer(cards[0], Personas.NadiaId, approve: true, start.AddHours(1));
        Assert.True(Data(first)["countsTowardRequirement"]!.GetValue<bool>(), first.Body);

        // Five days on, the second request passed its Expires On. Before the claim the engine still holds it pending.
        host.Clock.UtcNow = start.AddDays(5);
        IRecordStore records = host.Get<IRecordStore>();
        Assert.Equal("pending", records.Get("approval-record", cards[1].ApprovalRecordId)!.Text("outcomeMeaning"));

        // The claim has the engine refresh the decision as the approval runner: the engine writes expired, which settles
        // the outbox row, and opens a restart with a new Expires On, which the same claim offers. The restart asks only
        // the owner whose answer does not count yet.
        HttpCall claim = await runner.ClaimCall();
        Assert.True(claim.Status == 200, claim + "\n" + host.Logs);
        JsonNode refresh = claim.Json!["data"]!["refresh"]!;
        Assert.Equal(1, refresh["decisions"]!.GetValue<int>());
        Assert.Equal(1, refresh["expiredRecords"]!.GetValue<int>());
        Assert.Equal(1, refresh["openedRecords"]!.GetValue<int>());
        Assert.Equal(0, refresh["cancelledRecords"]!.GetValue<int>());
        Assert.Equal("expired", records.Get("approval-record", cards[1].ApprovalRecordId)!.Text("outcomeMeaning"));
        Assert.Equal(ApprovalOutboxStates.Expired, new ApprovalOutbox(host.Get<IProductStore>()).Get(cards[1].ApprovalRecordId)!.State);
        RunnerItem restart = new(Assert.Single(claim.Json["data"]!["items"]!.AsArray())!);
        Assert.NotEqual(cards[1].ApprovalRecordId, restart.ApprovalRecordId);
        Assert.Equal(start.AddDays(10), ExpiresOn(restart));
        Assert.Equal(new[] { InesId }, restart.AskedPrincipals);
        Assert.Equal("pending", records.Get("approval-record", restart.ApprovalRecordId)!.Text("outcomeMeaning"));
        Assert.Equal(decisionId, records.Get("approval-record", restart.ApprovalRecordId)!.Text("decision"));

        // Ines answers the expired card, in time but too late to reach the product: the engine refuses it and nothing
        // changes. The record stays expired; her answer belongs on the restarted card.
        string before = Dump(host);
        HttpCall onExpired = await runner.Answer(cards[1], InesId, approve: true, start.AddDays(4));
        Assert.Equal(409, onExpired.Status);
        Assert.Equal("conflict", onExpired.Json!["error"]!["code"]!.GetValue<string>());
        Assert.Contains("final outcome expired", onExpired.Json["error"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(before, Dump(host));

        // On the restarted card her answer counts, and the decision is accepted.
        Assert.Equal(200, (await runner.Sent(restart)).Status);
        HttpCall answered = await runner.Answer(restart, InesId, approve: true, host.Clock.UtcNow.AddHours(2));
        Assert.True(answered.Status == 200, answered + "\n" + host.Logs);
        Assert.True(Data(answered)["decisionAccepted"]!.GetValue<bool>(), answered.Body);
        Assert.Equal(
            new[] { "approved", "approved", "expired" },
            records.Query(RowQuery.For("approval-record").Where("decision", decisionId)).Select(r => r.Text("outcomeMeaning")!).OrderBy(m => m, StringComparer.Ordinal));

        // A refresh later finds nothing to do.
        HttpCall quiet = await runner.ClaimCall();
        Assert.Equal(0, quiet.Json!["data"]!["refresh"]!["decisions"]!.GetValue<int>());
        Assert.Empty(quiet.Json["data"]!["items"]!.AsArray());
    }

    // ---- Helpers ----

    private static ProductTestHost Start()
    {
        ProductTestHost host = RunnerHost.Start(new HostSettings { BindingOperations = TwoOwners });
        host.Get<IStoreSeeder>().Seed("owner-assignment", CanonicalJson.Parse(
            "{\"businessApplication\":\"" + Personas.InspectionScheduler + "\",\"ownerRole\":\"business-owner\",\"principal\":\"" + InesId + "\",\"isDeputy\":false,\"startOn\":\"2025-01-06\"}"));
        return host;
    }

    // Priya, through Claude Code, drafts and submits a change to Inspection Scheduler. Its window is after the five-day
    // timeout, so the decision can still be accepted after a restart.
    private static async Task<(string Change, string DecisionId, string[] Approvals)> SubmitChange(ProductTestHost host)
    {
        JsonNode draft = await host.Call(Personas.Priya, "save_change_request_draft", "{\"businessApplicationId\":\"" + Personas.InspectionScheduler + "\",\"changeTypeKey\":\"normal\","
            + "\"description\":\"Release 3.1 of Inspection Scheduler to production.\",\"plannedTargetId\":\"" + Personas.FieldOpsProd + "\",\"title\":\"Inspection Scheduler 3.1\","
            + "\"windowStart\":\"2026-12-10T06:00:00Z\",\"windowEnd\":\"2026-12-10T08:00:00Z\"}");
        Assert.True(draft["ok"]!.GetValue<bool>(), draft.ToJsonString() + "\n" + host.Logs);
        string change = draft["data"]!["recordId"]!.GetValue<string>();
        JsonNode submitted = await host.Call(Personas.Priya, "submit_record", "{\"subjectKind\":\"change-request\",\"subjectId\":\"" + change + "\"}");
        Assert.True(submitted["ok"]!.GetValue<bool>(), submitted.ToJsonString());
        JsonArray requested = submitted["data"]!["approvalsRequested"]!.AsArray();
        Assert.All(requested, r => Assert.Equal("business-owner", r!["requiredOwnerRoleKey"]!.GetValue<string>()));
        return (change, submitted["data"]!["decisionId"]!.GetValue<string>(), requested.Select(r => r!["approvalRecordId"]!.GetValue<string>()).ToArray());
    }

    private static JsonNode Data(HttpCall call)
    {
        return call.Json!["data"]!;
    }

    private static DateTime? ExpiresOn(RunnerItem item)
    {
        return Product.Azure.Host.RecordEndpoints.Moment(item.Json["expiresOn"]!.GetValue<string>());
    }

    private static string Dump(ProductTestHost host)
    {
        return ((RowStore)host.Get<IProductStore>()).Dump();
    }
}
