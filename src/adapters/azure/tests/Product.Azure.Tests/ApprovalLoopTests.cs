using System;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Product.Azure.Host;
using Product.Azure.Stores;
using Product.Engine.Runtime.Ports;
using Xunit;

namespace Product.Azure.Tests;

// The whole approval loop through the product's own wiring (AddProduct and MapProduct, with only
// Product:ApprovalRunner:Enabled switched on): an AI client drafts and submits for a person, the engine opens the
// Approval Record and its outbox row, the runner pulls the request and sends a card, a person answers on the card, the
// runner posts the answer, and the engine records it with the approver's identity and accepts the decision. The web app
// then shows the person the result. No AI answer counts, and no AI caller reaches an approval route.
public sealed class ApprovalLoopTests
{
    [Fact]
    public async Task AnAiDraftsAPersonAnswersOnACardAndTheEngineRecordsTheApprover()
    {
        await using ProductTestHost host = RunnerHost.Start();
        IRecordStore records = host.Get<IRecordStore>();

        // 1. Priya asks Claude Code to draft and submit a change to Inspection Scheduler. The writes are stamped as an AI
        //    client acting for her, and the engine asks the application's business owner for the authorization.
        JsonNode draft = await host.Call(Personas.Priya, "save_change_request_draft", "{\"businessApplicationId\":\"" + Personas.InspectionScheduler + "\",\"changeTypeKey\":\"normal\","
            + "\"description\":\"Release 3.0 of Inspection Scheduler to production.\",\"plannedTargetId\":\"" + Personas.FieldOpsProd + "\",\"title\":\"Inspection Scheduler 3.0\","
            + "\"windowStart\":\"2026-11-26T06:00:00Z\",\"windowEnd\":\"2026-11-26T08:00:00Z\"}");
        Assert.True(draft["ok"]!.GetValue<bool>(), draft.ToJsonString() + "\n" + host.Logs);
        Assert.True(draft["data"]!["isAiCreated"]!.GetValue<bool>());
        Assert.Equal("ai-client-for-person", draft["data"]!["performedBy"]!["actorKind"]!.GetValue<string>());
        string change = draft["data"]!["recordId"]!.GetValue<string>();
        JsonNode submitted = await host.Call(Personas.Priya, "submit_record", "{\"subjectKind\":\"change-request\",\"subjectId\":\"" + change + "\"}");
        Assert.True(submitted["ok"]!.GetValue<bool>(), submitted.ToJsonString());
        string decisionId = submitted["data"]!["decisionId"]!.GetValue<string>();
        JsonNode card = Assert.Single(submitted["data"]!["approvalsRequested"]!.AsArray())!;
        Assert.Equal("business-owner", card["requiredOwnerRoleKey"]!.GetValue<string>());
        string approvalId = card["approvalRecordId"]!.GetValue<string>();
        Assert.Equal("proposed", records.Get("decision", decisionId)!.Text("statusMeaning"));
        Assert.Equal(ApprovalOutboxStates.Pending, new ApprovalOutbox(host.Get<IProductStore>()).Get(approvalId)!.State);

        // 2. The AI cannot answer: no tool approves, and neither Claude Code's token nor the web app's reaches the route.
        HttpCall list = await host.Mcp(Personas.AllRoles, "tools/list");
        Assert.DoesNotContain(list.Json!["result"]!["tools"]!.AsArray(), t => t!["name"]!.GetValue<string>().Contains("approve", StringComparison.Ordinal));
        string answerBody = Answer(Personas.NadiaId, host.Clock.UtcNow.AddMinutes(5), "person");
        Assert.Equal(403, (await host.Post(FakeApprovalRunner.OutcomePath(approvalId), Personas.Priya, answerBody)).Status);
        Assert.Equal(403, (await host.Post(FakeApprovalRunner.OutcomePath(approvalId), Personas.Nadia, answerBody)).Status);
        Assert.Equal(403, (await host.Post(FakeApprovalRunner.OutcomePath(approvalId), Personas.NadiaWeb, answerBody)).Status);
        Assert.Equal(403, (await host.Post(FakeApprovalRunner.ClaimsPath, Personas.Priya, "{}")).Status);
        Assert.Equal("pending", records.Get("approval-record", approvalId)!.Text("outcomeMeaning"));

        // 3. The runner pulls the request and sends the card to the business owner.
        var runner = new FakeApprovalRunner(host);
        RunnerItem item = Assert.Single(await runner.Claim());
        Assert.Equal(approvalId, item.ApprovalRecordId);
        Assert.Equal(new[] { Personas.NadiaId }, FakeApprovalRunner.AskedUsers(item));
        Assert.Equal(200, (await runner.Sent(item)).Status);

        // 4. Nadia approves on the card; the runner posts her answer with its Idempotency-Key.
        DateTime respondedOn = host.Clock.UtcNow.AddMinutes(42);
        HttpCall answered = await runner.Answer(item, Personas.NadiaId, approve: true, respondedOn);
        Assert.True(answered.Status == 200, answered + "\n" + host.Logs);
        Assert.True(answered.Json!["data"]!["countsTowardRequirement"]!.GetValue<bool>(), answered.Body);
        Assert.True(answered.Json["data"]!["decisionAccepted"]!.GetValue<bool>(), answered.Body);

        // 5. The record names the person who answered, as a person and a user, written by the approval runner; the engine
        //    accepted the decision itself in the same transaction.
        StoredRow record = records.Get("approval-record", approvalId)!;
        Assert.Equal(Personas.NadiaId, record.Text("approver"));
        Assert.Equal("person", record.Text("approverActorMeaning"));
        Assert.Equal("user", record.Text("approverPrincipalKind"));
        Assert.True(record.Flag("countsTowardRequirement"));
        Assert.Equal("accepted", records.Get("decision", decisionId)!.Text("statusMeaning"));
        Assert.Equal(ApprovalOutboxStates.Answered, new ApprovalOutbox(host.Get<IProductStore>()).Get(approvalId)!.State);

        // 6. Priya, on the web app, reads the outcome as herself: Nadia's answer counted and nothing is missing.
        HttpCall status = await host.Post("/api/get_approval_status", Personas.PriyaWeb, "{\"subjectKind\":\"change-request\",\"subjectId\":\"" + change + "\"}");
        Assert.True(status.Status == 200, status.ToString());
        Assert.Contains(Personas.NadiaId, status.Body, StringComparison.Ordinal);
        Assert.True(status.Json!["data"]!["ready"]!.GetValue<bool>(), status.Body);
        Assert.Contains(status.Json["data"]!["records"]!.AsArray(), r => r!["outcome"]!["meaning"]!.GetValue<string>() == "approved" && r["countsTowardRequirement"]!.GetValue<bool>());
    }

    [Fact]
    public async Task AnAnswerTheRunnerReportsFromAnAiNeverCounts()
    {
        await using ProductTestHost host = RunnerHost.Start();
        IRecordStore records = host.Get<IRecordStore>();
        JsonNode draft = await host.Call(Personas.Priya, "save_intake_draft", "{\"title\":\"Depot key register\",\"need\":\"Keys go missing.\",\"proposedKindKey\":\"power-platform-app\"}");
        string intake = draft["data"]!["recordId"]!.GetValue<string>();
        JsonNode submitted = await host.Call(Personas.Priya, "submit_record", "{\"subjectKind\":\"intake-request\",\"subjectId\":\"" + intake + "\"}");
        string decisionId = submitted["data"]!["decisionId"]!.GetValue<string>();

        var runner = new FakeApprovalRunner(host);
        RunnerItem item = Assert.Single(await runner.Claim());
        HttpCall byAi = await runner.Answer(item, Personas.OwenId, approve: true, host.Clock.UtcNow.AddMinutes(3), actorKind: "ai-client");
        Assert.True(byAi.Status == 200 || byAi.Status == 403, byAi.ToString());
        StoredRow record = records.Get("approval-record", item.ApprovalRecordId)!;
        Assert.False(record.Flag("countsTowardRequirement"));
        Assert.Equal("proposed", records.Get("decision", decisionId)!.Text("statusMeaning"));
    }

    private static string Answer(string approver, DateTime respondedOn, string actorKind)
    {
        return "{\"outcome\":\"approval-approved\",\"approver\":\"" + approver + "\",\"approverActorKind\":\"" + actorKind + "\",\"approverPrincipalKind\":\"user\",\"respondedOn\":\""
            + respondedOn.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture) + "\",\"approverHoldsRequiredRole\":true,\"channel\":\"outlook\"}";
    }
}
