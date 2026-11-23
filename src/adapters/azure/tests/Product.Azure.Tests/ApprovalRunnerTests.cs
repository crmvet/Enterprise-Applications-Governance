using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Product.Azure.Host;
using Product.Azure.Stores;
using Product.Engine.Core.Canonical;
using Product.Engine.Runtime.Ports;
using Xunit;

namespace Product.Azure.Tests;

// The approval runner end to end over HTTP, with a fake runner that plays the Power Automate flows: the engine opens an
// Approval Record and its outbox row in one commit, the runner claims it with a lease, reports the card as sent, and
// posts the person's answer with an Idempotency-Key; the engine records and counts it. The runner never approves: only
// an answer from a user who holds the role counts, the routes serve only the workload with the approval writer grant,
// and no tool approves.
public sealed class ApprovalRunnerTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(900);

    [Fact]
    public async Task ASubmitOpensTheApprovalRecordAndItsOutboxRowInOneCommit()
    {
        await using ProductTestHost host = RunnerHost.Start();
        (string approvalId, string decisionId) = await SubmitIntake(host);

        ApprovalOutboxItem item = Outbox(host).Get(approvalId)!;
        StoredRow record = Records(host).Get("approval-record", approvalId)!;
        Assert.Equal(ApprovalOutboxStates.Pending, item.State);
        Assert.Equal(decisionId, item.DecisionId);
        Assert.Equal(RecordEndpoints.Moment(record.Text("requestedOn")), item.EnqueuedOnUtc);
        Assert.Equal(0, item.Attempts);
        Assert.Null(item.ClaimId);

        // One outbox row for each pending Approval Record, and none of it in the engine's rows.
        IReadOnlyList<StoredRow> pending = Records(host).Query(RowQuery.For("approval-record").Where("outcomeMeaning", "pending"));
        Assert.Equal(pending.Select(r => r.Id).OrderBy(i => i, StringComparer.Ordinal), host.Get<IProductStore>().OutboxItems(100).Select(i => i.ApprovalRecordId).OrderBy(i => i, StringComparer.Ordinal));
        Assert.DoesNotContain("outbox", Dump(host), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheRunnerPullsPendingRequestsWithALeaseThatRunsOut()
    {
        await using ProductTestHost host = RunnerHost.Start();
        (string approvalId, string decisionId) = await SubmitIntake(host);
        var runner = new FakeApprovalRunner(host);

        RunnerItem item = Assert.Single(await runner.Claim());
        Assert.Equal(approvalId, item.ApprovalRecordId);
        Assert.Equal(decisionId, item.DecisionId);
        Assert.StartsWith("Approval requested: ", item.Json["title"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Contains("Field inspections app", item.Json["title"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal("https://product.example.org/records/decision/" + decisionId, item.Json["recordUrl"]!.GetValue<string>());
        Assert.Equal("role", item.AskKind);
        Assert.Equal("platform-admin", item.Json["ask"]!["roleKey"]!.GetValue<string>());
        Assert.Equal(new[] { Personas.PlatformAdminGroup }, item.AskedGroups);
        Assert.Empty(item.AskedPrincipals);
        Assert.Equal("person", item.Hint("approverActorKind"));
        Assert.Equal("user", item.Hint("approverPrincipalKind"));
        Assert.Equal("approval-approved", item.Hint("approvedOutcome"));
        Assert.Equal("approval-rejected", item.Hint("rejectedOutcome"));
        Assert.Equal("teams", item.Json["channel"]!.GetValue<string>());
        Assert.False(item.Json["isConsultation"]!.GetValue<bool>());
        Assert.True(item.Json.AsObject().ContainsKey("expiresOn"));
        Assert.Equal(RecordEndpoints.Moment(Records(host).Get("approval-record", approvalId)!.Text("requestedOn")), RecordEndpoints.Moment(item.Json["requestedOn"]!.GetValue<string>()));
        string details = item.Json["detailsMarkdown"]!.GetValue<string>();
        Assert.Contains("An AI client drafted this decision", details, StringComparison.Ordinal);
        Assert.Contains(new[] { Personas.OwenId, Personas.AllRolesId }, id => FakeApprovalRunner.AskedUsers(item).Contains(id));
        Assert.DoesNotContain(Personas.AgentUserId, FakeApprovalRunner.AskedUsers(item));

        ApprovalOutboxItem claimed = Outbox(host).Get(approvalId)!;
        Assert.Equal(ApprovalOutboxStates.Claimed, claimed.State);
        Assert.Equal(item.ClaimId, claimed.ClaimId);
        Assert.Equal(host.Clock.UtcNow.Add(Lease), claimed.ClaimedUntilUtc);

        // Leased: a second claim gets nothing until the lease runs out on the clock, then the item comes back under a
        // new claim, with the lapsed lease counted as an attempt.
        Assert.Empty(await runner.Claim());
        host.Clock.Advance(Lease - TimeSpan.FromSeconds(1));
        Assert.Empty(await runner.Claim());
        host.Clock.Advance(TimeSpan.FromSeconds(1));
        RunnerItem again = Assert.Single(await runner.Claim(leaseSeconds: 60));
        Assert.Equal(approvalId, again.ApprovalRecordId);
        Assert.NotEqual(item.ClaimId, again.ClaimId);
        Assert.Equal(1, Outbox(host).Get(approvalId)!.Attempts);
        Assert.Equal(host.Clock.UtcNow.AddSeconds(60), Outbox(host).Get(approvalId)!.ClaimedUntilUtc);
    }

    [Fact]
    public async Task AnOwnerRoleCardAsksTheCurrentOwnerAndTheOwnersAnswerCounts()
    {
        await using ProductTestHost host = RunnerHost.Start();
        JsonNode draft = await host.Call(Personas.Priya, "save_change_request_draft", "{\"businessApplicationId\":\"" + Personas.InspectionScheduler + "\",\"changeTypeKey\":\"normal\","
            + "\"description\":\"Release 3.0 of Inspection Scheduler to production.\",\"plannedTargetId\":\"" + Personas.FieldOpsProd + "\",\"title\":\"Inspection Scheduler 3.0 [see notes](https://notes.example)\","
            + "\"windowStart\":\"2026-11-26T06:00:00Z\",\"windowEnd\":\"2026-11-26T08:00:00Z\"}");
        Assert.True(draft["ok"]!.GetValue<bool>(), draft.ToJsonString() + "\n" + host.Logs);
        JsonNode submitted = await host.Call(Personas.Priya, "submit_record", "{\"subjectKind\":\"change-request\",\"subjectId\":\"" + draft["data"]!["recordId"]!.GetValue<string>() + "\"}");
        Assert.True(submitted["ok"]!.GetValue<bool>(), submitted.ToJsonString());
        JsonNode card = Assert.Single(submitted["data"]!["approvalsRequested"]!.AsArray())!;
        Assert.Equal("business-owner", card["requiredOwnerRoleKey"]!.GetValue<string>());

        var runner = new FakeApprovalRunner(host);
        RunnerItem item = Assert.Single(await runner.Claim());
        Assert.Equal("owner-role", item.AskKind);
        Assert.Equal("business-owner", item.Json["ask"]!["roleKey"]!.GetValue<string>());
        Assert.Equal(new[] { Personas.NadiaId }, item.AskedPrincipals);
        Assert.Empty(item.AskedGroups);
        Assert.Equal(card["channelKey"]!.GetValue<string>(), item.Json["channel"]!.GetValue<string>());
        Assert.Equal(card["expiresOn"]!.GetValue<string>(), item.Json["expiresOn"]!.GetValue<string>());

        // The change request's title, written through an AI client, cannot put a link on the card.
        Assert.DoesNotContain("](https://notes.example)", item.Json["detailsMarkdown"]!.GetValue<string>(), StringComparison.Ordinal);

        Assert.Equal(200, (await runner.Sent(item)).Status);
        HttpCall answer = await runner.Answer(item, Personas.NadiaId, approve: true, host.Clock.UtcNow.AddHours(1));
        Assert.True(answer.Status == 200, answer + "\n" + host.Logs);
        Assert.True(answer.Json!["data"]!["countsTowardRequirement"]!.GetValue<bool>(), answer.Body);
        Assert.True(Records(host).Get("approval-record", item.ApprovalRecordId)!.Flag("approverHoldsRequiredRole"));
    }

    [Fact]
    public async Task ASentReportFromAStaleClaimIsAConflictAndARepeatIsANoOp()
    {
        await using ProductTestHost host = RunnerHost.Start();
        (string approvalId, _) = await SubmitIntake(host);
        var runner = new FakeApprovalRunner(host);
        RunnerItem first = Assert.Single(await runner.Claim(leaseSeconds: 30));
        host.Clock.Advance(TimeSpan.FromSeconds(31));
        RunnerItem second = Assert.Single(await runner.Claim());

        HttpCall stale = await runner.Sent(first);
        Assert.Equal(409, stale.Status);
        Assert.Equal("conflict", stale.Json!["error"]!["code"]!.GetValue<string>());

        HttpCall sent = await runner.Sent(second, cardRef: "approval-card-7");
        Assert.True(sent.Status == 200, sent.ToString());
        Assert.False(sent.Json!["data"]!["repeated"]!.GetValue<bool>());
        ApprovalOutboxItem item = Outbox(host).Get(approvalId)!;
        Assert.Equal(ApprovalOutboxStates.Sent, item.State);
        Assert.Equal("approval-card-7", item.CardRef);
        Assert.Equal(FakeApprovalRunner.RunUrl(second), item.RunnerRunUrl);

        HttpCall repeated = await runner.Sent(second, cardRef: "approval-card-7");
        Assert.Equal(200, repeated.Status);
        Assert.True(repeated.Json!["data"]!["repeated"]!.GetValue<bool>());
        Assert.Equal(item.RowVersion, Outbox(host).Get(approvalId)!.RowVersion);

        HttpCall otherCard = await runner.Sent(second, cardRef: "approval-card-8");
        Assert.Equal(409, otherCard.Status);

        // A sent item is never claimed again, and the closed body and the run link are checked.
        host.Clock.Advance(TimeSpan.FromHours(2));
        Assert.Empty(await runner.Claim());
        HttpCall plainHttp = await host.Post(FakeApprovalRunner.SentPath(approvalId), Personas.Runner, "{\"claimId\":\"" + second.ClaimId + "\",\"cardRef\":\"x\",\"runnerRunUrl\":\"http://flows.example/run\"}");
        Assert.Equal(400, plainHttp.Status);
        HttpCall extra = await host.Post(FakeApprovalRunner.SentPath(approvalId), Personas.Runner, "{\"claimId\":\"" + second.ClaimId + "\",\"cardRef\":\"x\",\"runnerRunUrl\":\"https://flows.example/run\",\"outcome\":\"approval-approved\"}");
        Assert.Equal(400, extra.Status);
        Assert.Equal("validation", extra.Json!["error"]!["code"]!.GetValue<string>());
        HttpCall unknown = await host.Post(FakeApprovalRunner.SentPath("00000000-0000-4000-9000-0000000000ff"), Personas.Runner, "{\"claimId\":\"c\",\"cardRef\":\"x\",\"runnerRunUrl\":\"https://flows.example/run\"}");
        Assert.Equal(404, unknown.Status);
    }

    [Fact]
    public async Task TheOutcomeIsPostedWithTheApproversIdentityAndASecondPostReplaysTheFirst()
    {
        await using ProductTestHost host = RunnerHost.Start();
        (string approvalId, string decisionId) = await SubmitIntake(host);
        var runner = new FakeApprovalRunner(host);
        RunnerItem item = Assert.Single(await runner.Claim());
        Assert.Equal(200, (await runner.Sent(item)).Status);

        DateTime respondedOn = host.Clock.UtcNow.AddMinutes(30);
        HttpCall answer = await runner.Answer(item, Personas.OwenId, approve: true, respondedOn);
        Assert.True(answer.Status == 200, answer + "\n" + host.Logs);
        Assert.Equal("approved", answer.Json!["data"]!["outcome"]!["meaning"]!.GetValue<string>());
        Assert.True(answer.Json["data"]!["countsTowardRequirement"]!.GetValue<bool>());
        Assert.Equal(decisionId, answer.Json["data"]!["decisionId"]!.GetValue<string>());

        StoredRow record = Records(host).Get("approval-record", approvalId)!;
        Assert.Equal(Personas.OwenId, record.Text("approver"));
        Assert.Equal("person", record.Text("approverActorMeaning"));
        Assert.Equal("user", record.Text("approverPrincipalKind"));
        Assert.True(record.Flag("approverHoldsRequiredRole"));
        Assert.Equal(FakeApprovalRunner.RunUrl(item), record.Text("flowRunUrl"));
        Assert.Equal(ApprovalOutboxStates.Answered, Outbox(host).Get(approvalId)!.State);

        // The Idempotency-Key is the Audit Entry Request Key, so the same answer again returns the first result and
        // writes nothing: no new Audit Entry, no changed row.
        string key = approvalId + ":" + Personas.OwenId + ":" + respondedOn.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Single(Records(host).Query(RowQuery.For("audit-entry").Where("requestKey", key)));
        int audits = Records(host).Query(RowQuery.For("audit-entry")).Count;
        string before = Dump(host);
        HttpCall replay = await runner.Answer(item, Personas.OwenId, approve: true, respondedOn);
        Assert.True(replay.Status == 200, replay.ToString());
        Assert.Contains("recorded before", replay.Json!["summary"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal("approved", replay.Json["data"]!["outcome"]!["meaning"]!.GetValue<string>());
        Assert.Equal(audits, Records(host).Query(RowQuery.For("audit-entry")).Count);
        Assert.Equal(before, Dump(host));

        // A key that names another answer than the body carries is refused before anything runs.
        HttpCall wrongKey = await host.Post(FakeApprovalRunner.OutcomePath(approvalId), Personas.Runner, Answer(Personas.OwenId, respondedOn), r => r.Headers.TryAddWithoutValidation(ApprovalOutcomeEndpoint.IdempotencyHeader, approvalId + ":" + Personas.NadiaId + ":2026-11-23T09:30:00Z"));
        Assert.Equal(400, wrongKey.Status);
        Assert.Equal(before, Dump(host));
    }

    [Fact]
    public async Task AReusedKeyWithAnotherBodyAndADifferentFinalAnswerAreConflicts()
    {
        await using ProductTestHost host = RunnerHost.Start();
        (string approvalId, _) = await SubmitIntake(host);
        var runner = new FakeApprovalRunner(host);
        RunnerItem item = Assert.Single(await runner.Claim());
        DateTime respondedOn = host.Clock.UtcNow.AddMinutes(20);
        Assert.Equal(200, (await runner.Answer(item, Personas.OwenId, approve: true, respondedOn)).Status);
        string before = Dump(host);

        // The same key (record, responder, time) with another body: the engine refuses the reused key.
        HttpCall reused = await runner.Answer(item, Personas.OwenId, approve: true, respondedOn, typedReason: "Changed my mind about the wording.");
        Assert.Equal(409, reused.Status);
        Assert.Equal("conflict", reused.Json!["error"]!["code"]!.GetValue<string>());
        HttpCall flipped = await runner.Answer(item, Personas.OwenId, approve: false, respondedOn);
        Assert.Equal(409, flipped.Status);

        // Another person's answer to the same request (a card sent again after a release, say): a conflict before the
        // engine runs, never a second outcome and never an AI refusal.
        HttpCall other = await runner.Answer(item, Personas.AllRolesId, approve: false, respondedOn.AddMinutes(5));
        Assert.Equal(409, other.Status);
        Assert.Equal("conflict", other.Json!["error"]!["code"]!.GetValue<string>());
        Assert.Contains("already holds the final outcome approved", other.Json["error"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(before, Dump(host));
        Assert.Equal(Personas.OwenId, Records(host).Get("approval-record", approvalId)!.Text("approver"));
    }

    [Fact]
    public async Task AnAiOrAgentAsApproverNeverCounts()
    {
        await using ProductTestHost host = RunnerHost.Start();
        var runner = new FakeApprovalRunner(host);
        DateTime respondedOn = host.Clock.UtcNow.AddMinutes(10);

        // The directory records the responder as an agent user; the runner reports a user: the engine stores the
        // directory's kind and the answer as recommendation only.
        host.Get<IStoreSeeder>().Seed("principal-reference", CanonicalJson.Parse("{\"principalKind\":\"agent-user\",\"entraObjectId\":\"" + Personas.AgentUserId + "\",\"displayLabel\":\"Owners agent\",\"isErased\":false}"));
        (string agentAnswered, string agentDecision) = await SubmitIntake(host, "Fleet fuel log");
        RunnerItem agentItem = (await runner.Claim()).Single(i => i.ApprovalRecordId == agentAnswered);
        HttpCall byAgent = await runner.Answer(agentItem, Personas.AgentUserId, approve: true, respondedOn);
        AssertRecommendationOnly(host, byAgent, agentAnswered, agentDecision, "agent-user");

        // The runner itself reports an agent identity, or an AI client acting for the person: recommendation only.
        (string identityAnswered, string identityDecision) = await SubmitIntake(host, "Depot key register");
        RunnerItem identityItem = (await runner.Claim()).Single(i => i.ApprovalRecordId == identityAnswered);
        AssertRecommendationOnly(host, await runner.Answer(identityItem, Personas.OwenId, approve: true, respondedOn, principalKind: "agent-identity"), identityAnswered, identityDecision, "agent-identity");

        (string aiAnswered, string aiDecision) = await SubmitIntake(host, "Toolbox talk sign-in");
        RunnerItem aiItem = (await runner.Claim()).Single(i => i.ApprovalRecordId == aiAnswered);
        HttpCall byAi = await runner.Answer(aiItem, Personas.OwenId, approve: true, respondedOn, actorKind: "ai-client");
        AssertRecommendationOnly(host, byAi, aiAnswered, aiDecision, "user");
        Assert.Contains(byAi.Json!["warnings"]!.AsArray(), w => w!["code"]?.GetValue<string>() == "approval.ai-actor-not-counted");
    }

    [Fact]
    public async Task ExpiredRequestsAreSkippedAndLateAnswersAreConflicts()
    {
        await using ProductTestHost host = RunnerHost.Start();
        (string approvalId, _) = await SubmitIntake(host);
        DateTime expires = host.Clock.UtcNow.AddDays(5);
        SetExpiresOn(host, approvalId, expires);
        var runner = new FakeApprovalRunner(host);
        RunnerItem item = Assert.Single(await runner.Claim(leaseSeconds: 60));
        Assert.Equal(expires, RecordEndpoints.Moment(item.Json["expiresOn"]!.GetValue<string>()));

        // A late answer: refused before the engine runs, the record stays pending, the outbox row is expired.
        string before = Dump(host);
        HttpCall late = await runner.Answer(item, Personas.OwenId, approve: true, expires.AddMinutes(1));
        Assert.Equal(409, late.Status);
        Assert.Equal("conflict", late.Json!["error"]!["code"]!.GetValue<string>());
        Assert.Equal(before, Dump(host));
        Assert.Equal("pending", Records(host).Get("approval-record", approvalId)!.Text("outcomeMeaning"));
        Assert.Equal(ApprovalOutboxStates.Expired, Outbox(host).Get(approvalId)!.State);

        // A request whose record passed Expires On is never offered: the claim settles it as expired.
        (string second, _) = await SubmitIntake(host, "Van mileage claims");
        SetExpiresOn(host, second, host.Clock.UtcNow.AddDays(2));
        host.Clock.UtcNow = host.Clock.UtcNow.AddDays(2);
        Assert.Empty(await runner.Claim());
        Assert.Equal(ApprovalOutboxStates.Expired, Outbox(host).Get(second)!.State);
    }

    [Fact]
    public async Task AReleaseCountsAnAttemptAndTheThirdMakesTheRequestFailed()
    {
        await using ProductTestHost host = RunnerHost.Start(maxAttempts: 3);
        (string approvalId, _) = await SubmitIntake(host);
        var runner = new FakeApprovalRunner(host);

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            RunnerItem item = Assert.Single(await runner.Claim());
            if (attempt == 2)
            {
                Assert.Equal(200, (await runner.Sent(item)).Status);
            }

            HttpCall released = await runner.Release(item, attempt == 1 ? "no-approvers" : "timeout");
            Assert.True(released.Status == 200, released.ToString());
            Assert.Equal(attempt, released.Json!["data"]!["attempts"]!.GetValue<int>());
            Assert.Equal(attempt < 3 ? ApprovalOutboxStates.Pending : ApprovalOutboxStates.Failed, released.Json["data"]!["state"]!.GetValue<string>());

            // The same release again is stale: the claim is gone.
            Assert.Equal(409, (await runner.Release(item)).Status);
        }

        Assert.Empty(await runner.Claim());
        Assert.Equal(ApprovalOutboxStates.Failed, Outbox(host).Get(approvalId)!.State);

        HttpCall badReason = await host.Post(FakeApprovalRunner.ReleasePath(approvalId), Personas.Runner, "{\"claimId\":\"c\",\"reason\":\"the approver said so on the phone\"}");
        Assert.Equal(400, badReason.Status);
        Assert.Equal("reason", badReason.Json!["error"]!["field"]!.GetValue<string>());

        // A failed request still takes the answer of a card that went out earlier: the engine records it.
        DateTime respondedOn = host.Clock.UtcNow.AddMinutes(5);
        HttpCall answer = await host.Post(FakeApprovalRunner.OutcomePath(approvalId), Personas.Runner, Answer(Personas.OwenId, respondedOn));
        Assert.Equal(200, answer.Status);
        Assert.Equal(ApprovalOutboxStates.Answered, Outbox(host).Get(approvalId)!.State);
    }

    [Fact]
    public async Task OnlyTheApprovalRunnerUsesTheRunnerRoutes()
    {
        await using ProductTestHost host = RunnerHost.Start();
        (string approvalId, _) = await SubmitIntake(host);
        var routes = new[]
        {
            FakeApprovalRunner.ClaimsPath,
            FakeApprovalRunner.SentPath(approvalId),
            FakeApprovalRunner.ReleasePath(approvalId),
        };
        string body = "{\"claimId\":\"c\",\"cardRef\":\"x\",\"runnerRunUrl\":\"https://flows.example/run\"}";
        ApprovalOutboxItem before = Outbox(host).Get(approvalId)!;

        foreach (string route in routes)
        {
            Assert.Equal(401, (await host.Post(route, null, "{}")).Status);

            // A maker through an AI client, a platform admin, an agent user and a pipeline: refused by the route's policy.
            foreach (Persona persona in new[] { Personas.Priya, Personas.Owen, Personas.Agent, Personas.Pipeline })
            {
                HttpCall refused = await host.Post(route, persona, route == FakeApprovalRunner.ClaimsPath ? "{}" : body);
                Assert.True(refused.Status == 403, persona.Name + " on " + route + ": " + refused);
                Assert.Equal("not_authorized", refused.Json!["error"]!["code"]!.GetValue<string>());
            }

            // The Approval.Write app role is not enough: the workload must hold a current approval writer Workload Grant.
            foreach (Persona persona in new[] { Personas.UnknownWorkload, Personas.PipelineAsRunner })
            {
                HttpCall refused = await host.Post(route, persona, route == FakeApprovalRunner.ClaimsPath ? "{}" : body);
                Assert.True(refused.Status == 403, persona.Name + " on " + route + ": " + refused);
                Assert.Equal("not_authorized", refused.Json!["error"]!["code"]!.GetValue<string>());
            }
        }

        // A late answer from a workload that is not the runner changes nothing, not even the outbox row.
        SetExpiresOn(host, approvalId, host.Clock.UtcNow.AddDays(1));
        HttpCall lateByOther = await host.Post(FakeApprovalRunner.OutcomePath(approvalId), Personas.UnknownWorkload, Answer(Personas.OwenId, host.Clock.UtcNow.AddDays(2)));
        Assert.Equal(403, lateByOther.Status);

        Assert.Equal(before.RowVersion, Outbox(host).Get(approvalId)!.RowVersion);
        Assert.Single(await new FakeApprovalRunner(host).Claim());
    }

    [Fact]
    public async Task NoToolApprovesAndTheRunnerRoutesAreOffUntilSwitchedOn()
    {
        await using (ProductTestHost host = RunnerHost.Start(enabled: false))
        {
            (string approvalId, string decisionId) = await SubmitIntake(host);
            HttpCall off = await new FakeApprovalRunner(host).ClaimCall();
            Assert.Equal(404, off.Status);
            Assert.Equal("not_found", off.Json!["error"]!["code"]!.GetValue<string>());

            // The outbox fills either way, so switching the routes on later finds the work.
            Assert.Equal(ApprovalOutboxStates.Pending, Outbox(host).Get(approvalId)!.State);

            HttpCall list = await host.Mcp(Personas.AllRoles, "tools/list");
            string[] tools = list.Json!["result"]!["tools"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()).ToArray();
            Assert.DoesNotContain(tools, n => n.Contains("approve", StringComparison.Ordinal) || n.Contains("outcome", StringComparison.Ordinal) || n.Contains("claim_approval", StringComparison.Ordinal));
            JsonNode approve = await host.Call(Personas.Owen, "approve_decision", "{\"decisionId\":\"" + decisionId + "\"}");
            Assert.Equal("not_found", approve["error"]!["code"]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task TheOutboxAndAClaimSurviveARestart()
    {
        string folder = TestPaths.NewScratchFolder();
        try
        {
            string file = Path.Combine(folder, "product.db");
            string approvalId;
            RunnerItem item;
            await using (ProductTestHost first = RunnerHost.Start(new HostSettings { DatabaseFile = file, SequentialIds = false }))
            {
                (approvalId, _) = await SubmitIntake(first);
                item = Assert.Single(await new FakeApprovalRunner(first).Claim());
            }

            SqliteConnection.ClearAllPools();
            await using (ProductTestHost second = RunnerHost.Start(new HostSettings { DatabaseFile = file, SequentialIds = false, Seed = false }))
            {
                ApprovalOutboxItem stored = Outbox(second).Get(approvalId)!;
                Assert.Equal(ApprovalOutboxStates.Claimed, stored.State);
                Assert.Equal(item.ClaimId, stored.ClaimId);

                var runner = new FakeApprovalRunner(second);
                Assert.Empty(await runner.Claim());
                Assert.Equal(200, (await runner.Sent(item)).Status);
                Assert.Equal(200, (await runner.Answer(item, Personas.OwenId, approve: true, second.Clock.UtcNow.AddMinutes(3))).Status);
                Assert.Equal(ApprovalOutboxStates.Answered, Outbox(second).Get(approvalId)!.State);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TestPaths.TryDelete(folder);
        }
    }

    [Fact]
    public async Task TheClaimChecksItsBodyAndCapsTheCount()
    {
        await using ProductTestHost host = RunnerHost.Start(maxClaim: 2);
        var runner = new FakeApprovalRunner(host);
        for (int i = 0; i < 3; i++)
        {
            await SubmitIntake(host, "Shift handover notes " + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        Assert.Equal(400, (await runner.ClaimCall("{\"max\":0}")).Status);
        Assert.Equal(400, (await runner.ClaimCall("{\"max\":\"ten\"}")).Status);
        Assert.Equal(400, (await runner.ClaimCall("{\"leaseSeconds\":5}")).Status);
        Assert.Equal(400, (await runner.ClaimCall("{\"approver\":\"" + Personas.OwenId + "\"}")).Status);
        Assert.Equal(400, (await host.Post(FakeApprovalRunner.ClaimsPath, Personas.Runner, "[]")).Status);

        // The host's cap wins over a larger max, and the oldest requests come first.
        IReadOnlyList<RunnerItem> items = await runner.Claim(max: 50);
        Assert.Equal(2, items.Count);
        Assert.Single(await runner.Claim(max: 50));
        Assert.Empty(await runner.Claim(max: 50));
    }

    [Fact]
    public void TextAPersonWroteCannotAddMarkdownToTheCard()
    {
        string escaped = ApprovalRequestCards.Markdown("See [the form](https://phish.example/x) and ![img](https://x.example/a.png)\n# Approve now\n*urgent* <b>bold</b>", 1000);
        Assert.DoesNotContain("](", escaped, StringComparison.Ordinal);
        Assert.Contains("\\[the form\\]\\(https://phish\\.example/x\\)", escaped, StringComparison.Ordinal);
        Assert.Contains("\\!\\[img\\]", escaped, StringComparison.Ordinal);
        Assert.Contains("\\# Approve now", escaped, StringComparison.Ordinal);
        Assert.Contains("\\*urgent\\*", escaped, StringComparison.Ordinal);
        Assert.Contains("\\<b\\>", escaped, StringComparison.Ordinal);
        Assert.Equal(1000, ApprovalRequestCards.Markdown(new string('a', 5000), 1000).Length);
        Assert.Equal("One line and another", ApprovalRequestCards.Plain("One line\r\n\tand   another\u0007"));
    }

    // ---- Helpers ----

    // The triage rule of the demo configuration names no Timeout Days, so the engine opens its record with no Expires
    // On. The test gives the record one, as a rule with a timeout would (seeding stands in for that configuration).
    private static void SetExpiresOn(ProductTestHost host, string approvalId, DateTime expiresOnUtc)
    {
        StoredRow record = Records(host).Get("approval-record", approvalId)!;
        JsonNode columns = JsonNode.Parse(CanonicalJson.Write(record.Columns, CanonicalProfile.Request))!;
        columns["expiresOn"] = expiresOnUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
        host.Get<IStoreSeeder>().Replace("approval-record", approvalId, CanonicalJson.Parse(columns.ToJsonString()));
    }

    private static void AssertRecommendationOnly(ProductTestHost host, HttpCall call, string approvalId, string decisionId, string storedKind)
    {
        Assert.True(call.Status == 200, call + "\n" + host.Logs);
        Assert.Equal("recommendation-only", call.Json!["data"]!["outcome"]!["meaning"]!.GetValue<string>());
        Assert.False(call.Json["data"]!["countsTowardRequirement"]!.GetValue<bool>());
        Assert.False(call.Json["data"]!["decisionAccepted"]!.GetValue<bool>());
        StoredRow record = Records(host).Get("approval-record", approvalId)!;
        Assert.Equal("recommendation-only", record.Text("outcomeMeaning"));
        Assert.Equal(storedKind, record.Text("approverPrincipalKind"));
        Assert.False(record.Flag("countsTowardRequirement"));
        Assert.Equal("proposed", Records(host).Get("decision", decisionId)!.Text("statusMeaning"));
        Assert.Equal(ApprovalOutboxStates.Answered, Outbox(host).Get(approvalId)!.State);
    }

    // Priya, through Claude Code, files and submits an intake; the engine opens one triage card for a platform admin.
    private static async Task<(string ApprovalId, string DecisionId)> SubmitIntake(ProductTestHost host, string title = "Field inspections app")
    {
        JsonNode draft = await host.Call(Personas.Priya, "save_intake_draft", "{\"title\":\"" + title + "\",\"need\":\"Inspectors fill in paper forms on site and retype them at the office.\",\"proposedKindKey\":\"power-platform-app\"}");
        Assert.True(draft["ok"]!.GetValue<bool>(), draft.ToJsonString() + "\n" + host.Logs);
        string intake = draft["data"]!["recordId"]!.GetValue<string>();
        JsonNode submitted = await host.Call(Personas.Priya, "submit_record", "{\"subjectKind\":\"intake-request\",\"subjectId\":\"" + intake + "\"}");
        Assert.True(submitted["ok"]!.GetValue<bool>(), submitted.ToJsonString());
        JsonNode card = Assert.Single(submitted["data"]!["approvalsRequested"]!.AsArray())!;
        return (card["approvalRecordId"]!.GetValue<string>(), submitted["data"]!["decisionId"]!.GetValue<string>());
    }

    private static string Answer(string approver, DateTime respondedOn)
    {
        return "{\"outcome\":\"approval-approved\",\"approver\":\"" + approver + "\",\"approverActorKind\":\"person\",\"approverPrincipalKind\":\"user\",\"respondedOn\":\""
            + respondedOn.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture) + "\",\"approverHoldsRequiredRole\":true,\"channel\":\"outlook\"}";
    }

    private static ApprovalOutbox Outbox(ProductTestHost host)
    {
        return new ApprovalOutbox(host.Get<IProductStore>());
    }

    private static IRecordStore Records(ProductTestHost host)
    {
        return host.Get<IRecordStore>();
    }

    private static string Dump(ProductTestHost host)
    {
        return ((RowStore)host.Get<IProductStore>()).Dump();
    }
}
