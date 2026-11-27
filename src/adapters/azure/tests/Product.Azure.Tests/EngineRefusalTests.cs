using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Product.Azure.Host;
using Product.Engine.Core;
using Product.Engine.Core.Contract;
using Product.Engine.Runtime;
using Product.Engine.Runtime.Callers;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Envelope;
using Xunit;

namespace Product.Azure.Tests;

// The engine's refusals through the HTTP host: a write the engine's write guard refuses comes back with its reason, an
// approval answered by an AI actor (or by anyone but the approval runner) is refused on every path, and the record
// endpoint takes only a pipeline's app-only token whose identity holds a Workload Grant.
public sealed class EngineRefusalTests
{
    [Fact]
    public async Task AWriteTheEngineGuardRefusesComesBackWithItsReason()
    {
        await using ProductTestHost host = ProductTestHost.Start();
        (string approvalId, _) = await SubmitIntake(host);

        // The approval runner writes the platform admin's answer: the engine counts it. It does not accept the intake
        // decision yet, because the accept gate also needs a business owner, and it says so in the warnings.
        string answer = Answer(Personas.OwenId, "2026-11-23T09:30:00Z", "approval-approved");
        HttpCall first = await host.Post("/engine/v1/approval-records/" + approvalId + "/outcome", Personas.Runner, answer);
        Assert.True(first.Status == 200, first.ToString() + "\n" + host.Logs);
        Assert.True(first.Json!["data"]!["countsTowardRequirement"]!.GetValue<bool>(), first.Body);
        Assert.Equal("approved", first.Json["data"]!["outcome"]!["meaning"]!.GetValue<string>());
        Assert.False(first.Json["data"]!["decisionAccepted"]!.GetValue<bool>(), first.Body);
        Assert.Contains(first.Json["warnings"]!.AsArray(), w => w!["code"]?.GetValue<string>() == "gate.not-ready");

        // A second, different answer to the same card: the card is final, so the engine's write guard refuses it.
        string changed = Answer(Personas.OwenId, "2026-11-23T09:45:00Z", "approval-rejected");
        string before = Dump(host);
        HttpCall second = await host.Post("/engine/v1/approval-records/" + approvalId + "/outcome", Personas.Runner, changed);
        Assert.False(second.Json!["ok"]!.GetValue<bool>(), second.Body);
        Assert.Equal(before, Dump(host));

        // The same write through the host's engine runtime names the guard's reason; the HTTP answer is its fixed code,
        // its HTTP status and its message.
        RuntimeOutcome outcome = host.Get<EngineRuntime>().Write(CallerIdentity.Workload(Personas.RunnerId, clientApp: "approval runner"), WriteCommand.Update("approval-record", approvalId, changed));
        Assert.False(outcome.Succeeded);
        Reason reason = outcome.Reasons.First(r => r.Severity == ReasonSeverity.Error);
        Assert.False(string.IsNullOrEmpty(reason.Code));
        McpError mapped = ProductErrorMap.Map(outcome, ContractSet.Embedded, aiCaller: false);
        TestContext.Current.TestOutputHelper?.WriteLine("Guard reason " + reason.Code + " answered as " + mapped.Code + " (HTTP " + second.Status + ").");
        Assert.Equal(mapped.Code, second.Json["error"]!["code"]!.GetValue<string>());
        Assert.Equal(reason.Message, second.Json["error"]!["message"]!.GetValue<string>());
        Assert.Equal(ContractSet.Embedded.Errors[mapped.Code].HttpStatus, second.Status);
        Assert.Equal(before, Dump(host));
    }

    [Fact]
    public async Task AnApprovalAnsweredByAnAiActorIsRefusedOnEveryPath()
    {
        await using ProductTestHost host = ProductTestHost.Start();
        (string approvalId, string decisionId) = await SubmitIntake(host);
        string answer = Answer(Personas.OwenId, "2026-11-23T09:30:00Z", "approval-approved");
        string before = Dump(host);

        // MCP: no tool approves.
        JsonNode approve = await host.Call(Personas.Owen, "approve_decision", "{\"decisionId\":\"" + decisionId + "\"}");
        Assert.Equal("not_found", approve["error"]!["code"]!.GetValue<string>());

        // The approval route takes only an app-only token with Approval.Write: a person through an AI client, an agent
        // user and a pipeline are refused by policy before the engine runs.
        foreach (Persona persona in new[] { Personas.Owen, Personas.Priya, Personas.Agent, Personas.Pipeline })
        {
            HttpCall refused = await host.Post("/engine/v1/approval-records/" + approvalId + "/outcome", persona, answer);
            Assert.True(refused.Status == 403, persona.Name + ": " + refused);
            Assert.Equal("not_authorized", refused.Json!["error"]!["code"]!.GetValue<string>());
        }

        // The engine's own table door, as an AI client for the platform admin: refused with the AI and outcome writer
        // reasons, mapped to ai_cannot_do_this.
        CallerIdentity ai = CallerIdentity.AiClient(Personas.OwenId, new[] { Personas.PlatformAdminGroup }, "Claude Code", clientAppVerified: true);
        RuntimeOutcome byAi = host.Get<EngineRuntime>().Write(ai, WriteCommand.Update("approval-record", approvalId, answer));
        Assert.False(byAi.Succeeded);
        Assert.Contains(byAi.Reasons, r => r.Code == "approval.outcome-writer");
        Assert.Contains(byAi.Reasons, r => r.Code == "ai.status-not-allowed");
        Assert.Equal("ai_cannot_do_this", ProductErrorMap.Map(byAi, ContractSet.Embedded, aiCaller: true).Code);
        RuntimeOutcome accept = host.Get<EngineRuntime>().Write(ai, WriteCommand.Update("decision", decisionId, "{\"status\":\"decision-accepted\"}"));
        Assert.Contains(accept.Reasons, r => r.Code == "decision.accept-by-engine-only");
        Assert.Contains(accept.Reasons, r => r.Code == "ai.status-not-allowed");

        // A workload with the Approval.Write app role but no approval-writer grant reaches the engine, which refuses it.
        HttpCall pipelineAsRunner = await host.Post("/engine/v1/approval-records/" + approvalId + "/outcome", Personas.PipelineAsRunner, answer);
        Assert.False(pipelineAsRunner.Json!["ok"]!.GetValue<bool>(), pipelineAsRunner.Body);
        Assert.Equal(403, pipelineAsRunner.Status);
        HttpCall unknown = await host.Post("/engine/v1/approval-records/" + approvalId + "/outcome", Personas.UnknownWorkload, answer);
        Assert.Equal(403, unknown.Status);
        Assert.Equal("not_authorized", unknown.Json!["error"]!["code"]!.GetValue<string>());

        // Nothing changed but the record of the refused MCP call, an audit entry in a transaction of its own (document 25,
        // B25-13): the card is still pending and the decision still proposed. The route and the table door are not the
        // server's tools, so only the call to approve_decision is recorded.
        Assert.Equal(StoreDumps.WithoutAudit(before), StoreDumps.WithoutAudit(host));
        StoredRow refusal = Assert.Single(StoreDumps.Refusals(host));
        Assert.Equal("approve_decision", refusal.Text("entityName"));
        Assert.Equal("ai-client-for-person", refusal.Text("actorKind"));
        Assert.Equal("pending", host.Get<IRecordStore>().Get("approval-record", approvalId)!.Text("outcomeMeaning"));
        Assert.Equal("proposed", host.Get<IRecordStore>().Get("decision", decisionId)!.Text("statusMeaning"));

        // The approval runner may, and the answer counts.
        HttpCall runner = await host.Post("/engine/v1/approval-records/" + approvalId + "/outcome", Personas.Runner, answer);
        Assert.Equal(200, runner.Status);
        Assert.Equal("approved", host.Get<IRecordStore>().Get("approval-record", approvalId)!.Text("outcomeMeaning"));
        Assert.True(host.Get<IRecordStore>().Get("approval-record", approvalId)!.Flag("countsTowardRequirement"));
    }

    [Fact]
    public async Task AnAnswerAnAiGaveNeverCountsEvenThroughTheApprovalRunner()
    {
        await using ProductTestHost host = ProductTestHost.Start();
        (string approvalId, string decisionId) = await SubmitIntake(host);

        // The approval runner relays an answer whose responder was an AI client acting for the platform admin.
        string fromAi = "{\"outcome\":\"approval-approved\",\"approver\":\"" + Personas.OwenId + "\",\"approverActorKind\":\"ai-client\",\"approverPrincipalKind\":\"user\",\"respondedOn\":\"2026-11-23T09:30:00Z\",\"approverHoldsRequiredRole\":true,\"channel\":\"teams\"}";
        HttpCall call = await host.Post("/engine/v1/approval-records/" + approvalId + "/outcome", Personas.Runner, fromAi);

        // Stored as a recommendation only, with the engine's reason as a warning: it does not count toward the
        // requirement, and the decision stays proposed.
        Assert.True(call.Status == 200, call.ToString());
        Assert.Equal("recommendation-only", call.Json!["data"]!["outcome"]!["meaning"]!.GetValue<string>());
        Assert.False(call.Json["data"]!["countsTowardRequirement"]!.GetValue<bool>());
        Assert.False(call.Json["data"]!["decisionAccepted"]!.GetValue<bool>());
        Assert.Contains(call.Json["warnings"]!.AsArray(), w => w!["code"]?.GetValue<string>() == "approval.ai-actor-not-counted");
        StoredRow card = host.Get<IRecordStore>().Get("approval-record", approvalId)!;
        Assert.False(card.Flag("countsTowardRequirement"));
        Assert.Equal("recommendation-only", card.Text("outcomeMeaning"));
        Assert.Equal("proposed", host.Get<IRecordStore>().Get("decision", decisionId)!.Text("statusMeaning"));
    }

    [Fact]
    public async Task TheApprovalRouteChecksItsClosedBody()
    {
        await using ProductTestHost host = ProductTestHost.Start();
        (string approvalId, _) = await SubmitIntake(host);

        HttpCall extra = await host.Post("/engine/v1/approval-records/" + approvalId + "/outcome", Personas.Runner, "{\"outcome\":\"approval-approved\",\"approver\":\"" + Personas.OwenId + "\",\"approverPrincipalKind\":\"user\",\"respondedOn\":\"2026-11-23T09:30:00Z\",\"actorKind\":\"approval-runner\"}");
        Assert.Equal(400, extra.Status);
        Assert.Equal("validation", extra.Json!["error"]!["code"]!.GetValue<string>());
        HttpCall missing = await host.Post("/engine/v1/approval-records/" + approvalId + "/outcome", Personas.Runner, "{\"outcome\":\"approval-approved\"}");
        Assert.Equal(400, missing.Status);
        HttpCall notFound = await host.Post("/engine/v1/approval-records/00000000-0000-4000-9000-0000000000ff/outcome", Personas.Runner, Answer(Personas.OwenId, "2026-11-23T09:30:00Z", "approval-approved"));
        Assert.Equal(404, notFound.Status);
    }

    [Fact]
    public async Task TheRecordEndpointRequiresTheWorkloadRoleAndAGrant()
    {
        await using ProductTestHost host = ProductTestHost.Start();
        string body = Deployment("release-200-prod", "2.0.0");

        Assert.Equal(401, (await host.Post("/api/deployments", null, body)).Status);
        foreach (Persona persona in new[] { Personas.Priya, Personas.Owen, Personas.Runner, Personas.Agent })
        {
            HttpCall refused = await host.Post("/api/deployments", persona, body);
            Assert.True(refused.Status == 403, persona.Name + ": " + refused);
            Assert.Equal("not_authorized", refused.Json!["error"]!["code"]!.GetValue<string>());
        }

        // An app-only identity with the app role but no Workload Grant: the engine refuses it.
        HttpCall noGrant = await host.Post("/api/deployments", Personas.UnknownWorkload, body);
        Assert.Equal(403, noGrant.Status);
        Assert.Equal("not_authorized", noGrant.Json!["error"]!["code"]!.GetValue<string>());
        Assert.Empty(host.Get<IRecordStore>().Query(RowQuery.For("deployment-record")));

        // The release pipeline: recorded, then the same body again is the first record, then the same key with another
        // body is a conflict.
        HttpCall created = await host.Post("/api/deployments", Personas.Pipeline, body);
        Assert.True(created.Status == 201, created.ToString() + "\n" + host.Logs);
        Assert.True(created.Json!["data"]!["created"]!.GetValue<bool>());
        Assert.Equal("pipeline-step", created.Json["data"]!["route"]!["meaning"]!.GetValue<string>());
        Assert.Equal("pipeline", created.Json["data"]!["performedBy"]!["actorKind"]!.GetValue<string>());
        string recordId = created.Json["data"]!["deploymentRecordId"]!.GetValue<string>();
        StoredRow record = host.Get<IRecordStore>().Get("deployment-record", recordId)!;
        Assert.Equal("pipeline", record.Text("performedByActorKind"));

        HttpCall replay = await host.Post("/api/deployments", Personas.Pipeline, body, r => r.Headers.TryAddWithoutValidation("Idempotency-Key", "release-200-prod"));
        Assert.Equal(200, replay.Status);
        Assert.False(replay.Json!["data"]!["created"]!.GetValue<bool>());
        Assert.Equal(recordId, replay.Json["data"]!["deploymentRecordId"]!.GetValue<string>());

        HttpCall conflict = await host.Post("/api/deployments", Personas.Pipeline, Deployment("release-200-prod", "2.0.1"));
        Assert.Equal(409, conflict.Status);
        Assert.Equal("conflict", conflict.Json!["error"]!["code"]!.GetValue<string>());

        HttpCall header = await host.Post("/api/deployments", Personas.Pipeline, body, r => r.Headers.TryAddWithoutValidation("Idempotency-Key", "another-key"));
        Assert.Equal(400, header.Status);
        HttpCall invalid = await host.Post("/api/deployments", Personas.Pipeline, "{\"idempotencyKey\":\"release-201-prod\"}");
        Assert.Equal(400, invalid.Status);
        Assert.Equal("validation", invalid.Json!["error"]!["code"]!.GetValue<string>());
        Assert.Single(host.Get<IRecordStore>().Query(RowQuery.For("deployment-record")));
    }

    // Priya, through Claude Code, files and submits an intake; the engine opens one triage card for a platform admin.
    private static async Task<(string ApprovalId, string DecisionId)> SubmitIntake(ProductTestHost host)
    {
        JsonNode draft = await host.Call(Personas.Priya, "save_intake_draft", "{\"title\":\"Field inspections app\",\"need\":\"Inspectors fill in paper forms on site and retype them at the office.\",\"proposedKindKey\":\"power-platform-app\"}");
        Assert.True(draft["ok"]!.GetValue<bool>(), draft.ToJsonString() + "\n" + host.Logs);
        string intake = draft["data"]!["recordId"]!.GetValue<string>();
        JsonNode submitted = await host.Call(Personas.Priya, "submit_record", "{\"subjectKind\":\"intake-request\",\"subjectId\":\"" + intake + "\"}");
        Assert.True(submitted["ok"]!.GetValue<bool>(), submitted.ToJsonString());
        // The triage card; Balanced also opens three consultation cards on an intake (document 21, B21-9).
        JsonNode card = Assert.Single(submitted["data"]!["approvalsRequested"]!.AsArray(), c => !c!["isConsultation"]!.GetValue<bool>())!;
        Assert.Equal("platform-admin", card["requiredRoleKey"]!.GetValue<string>());
        return (card["approvalRecordId"]!.GetValue<string>(), submitted["data"]!["decisionId"]!.GetValue<string>());
    }

    private static string Answer(string approver, string respondedOn, string outcome)
    {
        return "{\"outcome\":\"" + outcome + "\",\"approver\":\"" + approver + "\",\"approverActorKind\":\"person\",\"approverPrincipalKind\":\"user\",\"respondedOn\":\"" + respondedOn + "\",\"approverHoldsRequiredRole\":true,\"channel\":\"teams\"}";
    }

    private static string Deployment(string key, string version)
    {
        return "{\"idempotencyKey\":\"" + key + "\",\"businessApplicationId\":\"" + Personas.InspectionScheduler + "\",\"artifactName\":\"InspectionScheduler_managed\",\"artifactVersion\":\"" + version + "\",\"artifactKindKey\":\"solution\","
            + "\"deploymentTargetId\":\"" + Personas.FieldOpsProd + "\",\"startedOn\":\"2026-11-23T08:40:00Z\",\"finishedOn\":\"2026-11-23T08:52:00Z\",\"resultMeaning\":\"succeeded\",\"codeApprovalKind\":\"person\"}";
    }

    private static string Dump(ProductTestHost host)
    {
        return ((Product.Azure.Stores.RowStore)host.Get<Product.Azure.Stores.IProductStore>()).Dump();
    }
}
