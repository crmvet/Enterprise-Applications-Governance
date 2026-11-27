using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Product.Azure.Host;
using Product.Azure.Stores;
using Product.Engine.Core.Contract;
using Product.Engine.Runtime.Callers;
using Product.Engine.Runtime.Ports;
using Xunit;

namespace Product.Azure.Tests;

// The REST API as the product's web app calls it: a token the web app asked for (its azp is one of
// Product:WebApp:ClientIds) is the person entry, so the engine runs as the person; every other client is an AI client
// acting for the person and passes the Known Client check, as on the MCP endpoint. The person reads and writes here
// (D-36), within the Tool Policy and the engine's guards; an AI client reads here and writes through the MCP endpoint.
public sealed class WebAppApiTests
{
    private const string Intake = "{\"title\":\"Field inspections app\",\"need\":\"Inspectors fill in paper forms on site and retype them at the office.\",\"proposedKindKey\":\"power-platform-app\"}";

    private const string RiskAcceptance = "{\"decisionTypeKey\":\"risk-acceptance\",\"subjectKind\":\"business-application\",\"subjectId\":\"" + Personas.InspectionScheduler
        + "\",\"statement\":\"Accept the paper checklist risk\",\"context\":\"Inspectors keep a paper copy while the app is offline\",\"proposedOutcomeKey\":\"accept\",\"reason\":\"Low impact\"}";

    // D-41, question 7, as the product owner changed it (data taxonomy slice 3): a data use whose governing rule is never is refused
    // when it is saved, for a person on the REST API too, and the refusal names the rule, the domain and where it is set.
    // The same person's use of data the rule allows is saved, confirmed, as the person's own act (D-36).
    [Fact]
    public async Task APersonsDataUseThatBreaksANeverRuleIsRefusedOnTheRestApi()
    {
        const string tree = "[{\"op\":\"upsert\",\"entity\":\"data-domain\",\"key\":\"field-safety\",\"values\":{\"label\":\"Field safety\",\"levelKey\":\"domain\",\"classificationKey\":\"restricted\",\"aiUseRule\":\"never\"}},"
            + "{\"op\":\"upsert\",\"entity\":\"data-domain\",\"key\":\"field-schedules\",\"values\":{\"label\":\"Field schedules\",\"levelKey\":\"domain\",\"classificationKey\":\"public\"}}]";
        await using ProductTestHost host = ProductTestHost.Start(new HostSettings { DraftOperations = tree });
        string use = "{\"applicationId\":\"" + Personas.InspectionScheduler + "\",\"domainKey\":\"field-safety\",\"access\":\"read\",\"purpose\":\"Summarize safety findings for inspectors.\",\"reachesAi\":true}";

        HttpCall refused = await host.Post("/api/save_data_use", Personas.NadiaWeb, use);
        Assert.True(refused.Status == 422, refused.ToString());
        Assert.Equal("rule_violation", refused.Json!["error"]!["code"]!.GetValue<string>());
        string message = refused.Json["error"]!["message"]!.GetValue<string>();
        Assert.Contains("Data rule never", message, StringComparison.Ordinal);
        Assert.Contains("Field safety", message, StringComparison.Ordinal);

        HttpCall saved = await host.Post("/api/save_data_use", Personas.NadiaWeb, use.Replace("field-safety", "field-schedules"));
        Assert.True(saved.Status == 200, saved.ToString());
        Assert.Equal("registered", saved.Json!["data"]!["status"]!["meaning"]!.GetValue<string>());
        Assert.Equal("person", saved.Json["data"]!["performedBy"]!["actorKind"]!.GetValue<string>());
        Assert.Equal("not-needed", saved.Json["data"]!["use"]!["state"]!.GetValue<string>());
    }

    [Fact]
    public async Task TheWebAppsSessionIsThePersonAndAnAiClientsIsTheAiClient()
    {
        await using ProductTestHost host = ProductTestHost.Start();

        HttpCall web = await host.Get("/api/me", Personas.NadiaWeb);
        Assert.True(web.Status == 200, web.ToString());
        Assert.Equal("person", web.Json!["actorKind"]!.GetValue<string>());
        Assert.Equal("Nadia Ferreira", web.Json["name"]!.GetValue<string>());
        Assert.Equal(Personas.NadiaId, web.Json["objectId"]!.GetValue<string>());
        Assert.Contains(web.Json["roles"]!.AsArray(), r => r!["roleKey"]!.GetValue<string>() == "application-owner");

        HttpCall ai = await host.Get("/api/me", Personas.Nadia);
        Assert.Equal(200, ai.Status);
        Assert.Equal("ai-client-for-person", ai.Json!["actorKind"]!.GetValue<string>());

        // The same roles on both entries: roles come from the groups and the configuration, never from the entry.
        Assert.Equal(Roles(web.Json), Roles(ai.Json));
    }

    [Fact]
    public async Task ThePagesReadThroughTheWebAppAndTheEngineStampsThePersonNotAnAiClient()
    {
        var contexts = new List<StoreCallContext>();
        var settings = new HostSettings { Services = services => services.AddSingleton<IStartupFilter>(new CallContextRecorder(contexts)) };
        await using ProductTestHost host = ProductTestHost.Start(settings);

        foreach ((string tool, string input) in new[]
        {
            ("get_portfolio_summary", "{}"),
            ("find_applications", "{\"text\":\"inspection\"}"),
            ("get_application_card", "{\"businessApplicationId\":\"" + Personas.InspectionScheduler + "\"}"),
            ("get_dependency_impact", "{\"businessApplicationId\":\"" + Personas.InspectionScheduler + "\"}"),
        })
        {
            HttpCall call = await host.Post("/api/" + tool, Personas.NadiaWeb, input);
            Assert.True(call.Status == 200 && call.Json!["ok"]!.GetValue<bool>(), tool + " answered " + call);
        }

        // SESSION_CONTEXT carries the person: the person entry, the actor kind person, no On Behalf Of, the web app.
        StoreCallContext person = contexts.Last();
        Assert.Equal(Personas.NadiaId, person.PrincipalId);
        Assert.Equal(CallerEntry.Person, person.Entry);
        Assert.Equal(ActorKindMeaning.Person, person.ActorKind);
        Assert.Null(person.OnBehalfOfId);
        Assert.Equal("Web app", person.ClientApp);
        Assert.Contains("application-owner", person.RoleKeys);

        // The same person through Claude Code is the AI client acting for her.
        HttpCall viaAi = await host.Post("/api/get_portfolio_summary", Personas.Nadia, "{}");
        Assert.Equal(200, viaAi.Status);
        StoreCallContext ai = contexts.Last();
        Assert.Equal(CallerEntry.AiClient, ai.Entry);
        Assert.Equal(ActorKindMeaning.AiClientForPerson, ai.ActorKind);
        Assert.Equal(Personas.NadiaId, ai.OnBehalfOfId);
    }

    [Fact]
    public async Task APersonFilesAndSubmitsAnIntakeInTheWebAppAsThePerson()
    {
        var contexts = new List<StoreCallContext>();
        var settings = new HostSettings { Services = services => services.AddSingleton<IStartupFilter>(new CallContextRecorder(contexts)) };
        await using ProductTestHost host = ProductTestHost.Start(settings);
        IRecordStore records = host.Get<IRecordStore>();

        // The draft is the person's own act: actor kind person, no On Behalf Of, not AI created, the web app verified.
        HttpCall draft = await host.Post("/api/save_intake_draft", Personas.PriyaWeb, Intake);
        Assert.True(draft.Status == 200 && draft.Json!["ok"]!.GetValue<bool>(), draft.ToString());
        JsonNode data = draft.Json!["data"]!;
        Assert.Equal("person", data["performedBy"]!["actorKind"]!.GetValue<string>());
        Assert.Null(data["performedBy"]!["onBehalfOf"]);
        Assert.False(data["isAiCreated"]!.GetValue<bool>());
        string intake = data["recordId"]!.GetValue<string>();
        StoredRow row = records.Get("intake-request", intake)!;
        Assert.Equal("person", row.Text("performedByActorKind"));
        Assert.Null(row.Text("onBehalfOf"));
        Assert.False(row.Flag("isAiCreated"));
        Assert.Equal("Web app", row.Text("clientApp"));
        Assert.True(row.Flag("clientAppVerified"));

        // SESSION_CONTEXT names the person on the write, as on a read.
        StoreCallContext written = contexts.Last();
        Assert.Equal(Personas.PriyaId, written.PrincipalId);
        Assert.Equal(CallerEntry.Person, written.Entry);
        Assert.Equal(ActorKindMeaning.Person, written.ActorKind);
        Assert.Null(written.OnBehalfOfId);

        // Submitted by the person, the intake decision stays proposed and approval cards start: only people answer them.
        HttpCall submitted = await host.Post("/api/submit_record", Personas.PriyaWeb, "{\"subjectKind\":\"intake-request\",\"subjectId\":\"" + intake + "\"}");
        Assert.True(submitted.Status == 200 && submitted.Json!["ok"]!.GetValue<bool>(), submitted.ToString());
        Assert.Equal("person", submitted.Json!["data"]!["performedBy"]!["actorKind"]!.GetValue<string>());
        Assert.NotEmpty(submitted.Json["data"]!["approvalsRequested"]!.AsArray());
        string decision = submitted.Json["data"]!["decisionId"]!.GetValue<string>();
        Assert.Equal("proposed", records.Get("decision", decision)!.Text("statusMeaning"));
        IReadOnlyList<StoredRow> approvals = records.Query(RowQuery.For("approval-record").Where("decision", decision));
        Assert.NotEmpty(approvals);
        Assert.All(approvals, r => Assert.Equal("pending", r.Text("outcomeMeaning")));
    }

    [Fact]
    public async Task AnAiClientStillWritesOnlyThroughTheMcpEndpointAndNoToolApproves()
    {
        await using ProductTestHost host = ProductTestHost.Start();
        string before = ((RowStore)host.Get<IProductStore>()).Dump();

        // The same person through Claude Code on the REST API: the write tools are served on the MCP endpoint.
        foreach ((string tool, string input) in new[]
        {
            ("save_intake_draft", Intake),
            ("submit_record", "{\"subjectKind\":\"intake-request\",\"subjectId\":\"00000000-0000-4000-9000-000000000001\"}"),
            ("save_decision_proposal", "{}"),
        })
        {
            HttpCall call = await host.Post("/api/" + tool, Personas.Priya, input);
            Assert.Equal(404, call.Status);
            Assert.Equal("not_found", call.Json!["error"]!["code"]!.GetValue<string>());
            Assert.Contains("MCP endpoint", call.Json["error"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);
        }

        // No approve tool on any entry, and the approval outcome route is closed to a person's token.
        Assert.Equal(404, (await host.Post("/api/approve_decision", Personas.OwenWeb, "{}")).Status);
        HttpCall outcome = await host.Post("/engine/v1/approval-records/00000000-0000-4000-9000-000000000001/outcome", Personas.OwenWeb, "{}");
        Assert.Equal(403, outcome.Status);
        Assert.Equal(before, ((RowStore)host.Get<IProductStore>()).Dump());
    }

    [Fact]
    public async Task APersonWithoutTheRoleOrTheRightIsRefusedAndNothingIsWritten()
    {
        await using ProductTestHost host = ProductTestHost.Start();
        IRecordStore records = host.Get<IRecordStore>();
        string intake = (await host.Post("/api/save_intake_draft", Personas.PriyaWeb, Intake)).Json!["data"]!["recordId"]!.GetValue<string>();
        string before = ((RowStore)host.Get<IProductStore>()).Dump();

        // The Tool Policy: a portfolio reader holds no role that may file an intake.
        HttpCall reader = await host.Post("/api/save_intake_draft", Personas.MateoWeb, Intake);
        Assert.True(reader.Status == 403, reader.ToString());
        Assert.Equal("not_authorized", reader.Json!["error"]!["code"]!.GetValue<string>());

        // Past the Tool Policy, the guards: only the intake's requester or creator submits it, and the platform admin is
        // neither.
        HttpCall notMine = await host.Post("/api/submit_record", Personas.OwenWeb, "{\"subjectKind\":\"intake-request\",\"subjectId\":\"" + intake + "\"}");
        Assert.True(notMine.Status == 403, notMine.ToString());
        Assert.Equal("not_authorized", notMine.Json!["error"]!["code"]!.GetValue<string>());

        // The engine's proposal right (decision.propose-not-allowed): Priya is the technical owner of Inspection Scheduler,
        // but in Balanced only its business owner holds a letter for a risk acceptance. The answer names who holds it.
        HttpCall noLetter = await host.Post("/api/save_decision_proposal", Personas.PriyaWeb, RiskAcceptance);
        Assert.True(noLetter.Status == 403, noLetter.ToString());
        Assert.Equal("not_authorized", noLetter.Json!["error"]!["code"]!.GetValue<string>());
        string message = noLetter.Json["error"]!["message"]!.GetValue<string>();
        Assert.Contains("risk-acceptance", message, StringComparison.Ordinal);
        Assert.Contains("business-owner", message, StringComparison.Ordinal);

        // Nothing written but the three refused writes, each recorded with the tool and the code (document 25, B25-13).
        Assert.Equal(StoreDumps.WithoutAudit(before), StoreDumps.WithoutAudit(host));
        Assert.Equal(new[] { "save_decision_proposal", "save_intake_draft", "submit_record" }, StoreDumps.Refusals(host).Select(r => r.Text("entityName")).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        Assert.All(StoreDumps.Refusals(host), r => Assert.Equal("person", r.Text("actorKind")));

        // Nadia, the business owner, holds the accountable letter: her proposal is recorded as proposed, as her own act.
        HttpCall owner = await host.Post("/api/save_decision_proposal", Personas.NadiaWeb, RiskAcceptance);
        Assert.True(owner.Status == 200 && owner.Json!["ok"]!.GetValue<bool>(), owner.ToString());
        Assert.Equal("person", owner.Json!["data"]!["performedBy"]!["actorKind"]!.GetValue<string>());
        Assert.Equal("proposed", owner.Json["data"]!["status"]!["meaning"]!.GetValue<string>());
        Assert.Equal("business-owner", owner.Json["data"]!["decider"]!["accountableOwnerRoleKey"]!.GetValue<string>());
        Assert.Equal("proposed", records.Get("decision", owner.Json["data"]!["recordId"]!.GetValue<string>())!.Text("statusMeaning"));
    }

    // D-38: a person answers the approval request they were asked on the product's decision page, through answer_approval
    // on the web app's entry. The engine records the answer only for the signed-in person the request asks (the token
    // names the responder), counts it as it counts a card's answer, and moves the decision; the outbox row settles in the
    // same transaction, so no card goes out for it.
    [Fact]
    public async Task APersonAnswersTheirApprovalOnTheWebPageAndTheEngineMovesTheDecision()
    {
        await using ProductTestHost host = ProductTestHost.Start();
        IRecordStore records = host.Get<IRecordStore>();
        (string decision, string record) = await SubmittedRiskAcceptance(host);
        string before = ((RowStore)host.Get<IProductStore>()).Dump();

        // Priya is Inspection Scheduler's technical owner, not its business owner: the request does not ask her.
        HttpCall notAsked = await host.Post("/api/answer_approval", Personas.PriyaWeb, Answer(record, "approve", "Looks fine"));
        Assert.True(notAsked.Status == 403, notAsked.ToString());
        Assert.Equal("not_authorized", notAsked.Json!["error"]!["code"]!.GetValue<string>());
        Assert.Contains("business-owner", notAsked.Json["error"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);

        // The risk acceptance rule asks for a typed reason, so an approval without one is refused, naming the field.
        HttpCall noReason = await host.Post("/api/answer_approval", Personas.NadiaWeb, Answer(record, "approve"));
        Assert.True(noReason.Status == 400, noReason.ToString());
        Assert.Equal("reason", noReason.Json!["error"]!["field"]!.GetValue<string>());

        // Nothing written but Priya's refused answer: a refusal by a rule is recorded, a missing input is not.
        Assert.Equal(StoreDumps.WithoutAudit(before), StoreDumps.WithoutAudit(host));
        StoredRow refusedAnswer = Assert.Single(StoreDumps.Refusals(host));
        Assert.Equal(Personas.PriyaId, refusedAnswer.Text("actor"));
        Assert.Equal(record, refusedAnswer.Text("rowId"));

        HttpCall answered = await host.Post("/api/answer_approval", Personas.NadiaWeb, Answer(record, "approve", "The paper copy covers the offline gap."));
        Assert.True(answered.Status == 200 && answered.Json!["ok"]!.GetValue<bool>(), answered.ToString());
        JsonNode data = answered.Json!["data"]!;
        Assert.Equal("approved", data["status"]!["meaning"]!.GetValue<string>());
        Assert.Equal("person", data["performedBy"]!["actorKind"]!.GetValue<string>());
        Assert.Null(data["performedBy"]!["onBehalfOf"]);
        Assert.Equal(Personas.NadiaId, data["performedBy"]!["principal"]!["objectId"]!.GetValue<string>());
        Assert.True(data["countsTowardRequirement"]!.GetValue<bool>());
        Assert.Equal("accepted", data["decisionStatus"]!["meaning"]!.GetValue<string>());

        StoredRow row = records.Get("approval-record", record)!;
        Assert.Equal(Personas.NadiaId, row.Text("approver"));
        Assert.Equal("person", row.Text("approverActorMeaning"));
        Assert.Equal("in-app", row.Text("channel"));
        Assert.Equal("The paper copy covers the offline gap.", row.Text("typedReason"));
        Assert.Equal("accepted", records.Get("decision", decision)!.Text("statusMeaning"));
        Assert.Equal(ApprovalOutboxStates.Answered, new ApprovalOutbox(host.Get<IProductStore>()).Get(record)!.State);

        // An answer is written once: the same answer again is a conflict, and nothing changes.
        string after = ((RowStore)host.Get<IProductStore>()).Dump();
        HttpCall again = await host.Post("/api/answer_approval", Personas.NadiaWeb, Answer(record, "approve", "Once more"));
        Assert.True(again.Status == 409, again.ToString());
        Assert.Equal(after, ((RowStore)host.Get<IProductStore>()).Dump());
    }

    // D-38: step-up sign-in is a setting, off by default and in the demo. With it on, an answer from a sign-in that does
    // not say when it happened is refused with step_up_required (401) and a claims challenge header, and nothing is
    // written; a check (validateOnly) writes nothing, so it needs no fresh sign-in. The web app signs the person in again.
    [Fact]
    public async Task WithStepUpOnAnAnswerNeedsAFreshSignIn()
    {
        var settings = new HostSettings();
        settings.Settings["Product:WebApp:StepUp:Mode"] = "FreshSignIn";
        settings.Settings["Product:WebApp:StepUp:MaxAgeMinutes"] = "10";
        await using ProductTestHost host = ProductTestHost.Start(settings);
        (string decision, string record) = await SubmittedRiskAcceptance(host);
        string before = ((RowStore)host.Get<IProductStore>()).Dump();

        HttpCall refused = await host.Post("/api/answer_approval", Personas.NadiaWeb, Answer(record, "approve", "The paper copy covers the offline gap."));
        Assert.True(refused.Status == 401, refused.ToString());
        Assert.Equal("step_up_required", refused.Json!["error"]!["code"]!.GetValue<string>());
        Assert.Contains("insufficient_claims", refused.Headers["WWW-Authenticate"], StringComparison.Ordinal);
        Assert.Equal(before, ((RowStore)host.Get<IProductStore>()).Dump());
        Assert.Equal("proposed", host.Get<IRecordStore>().Get("decision", decision)!.Text("statusMeaning"));

        HttpCall check = await host.Post("/api/answer_approval", Personas.NadiaWeb, "{\"approvalRecordId\":\"" + record + "\",\"answer\":\"approve\",\"reason\":\"Fine\",\"validateOnly\":true}");
        Assert.True(check.Status == 200, check.ToString());
        Assert.Equal(before, ((RowStore)host.Get<IProductStore>()).Dump());
    }

    // AI never approves: Claude Code acting for the business owner is refused answer_approval on the REST API and on the
    // MCP endpoint, which does not list it; an app-only token is refused before any tool runs. Nothing is written.
    [Fact]
    public async Task AnAiClientOrAWorkloadNeverAnswersAnApprovalOnAnyEntry()
    {
        await using ProductTestHost host = ProductTestHost.Start();
        (string decision, string record) = await SubmittedRiskAcceptance(host);
        string before = ((RowStore)host.Get<IProductStore>()).Dump();

        HttpCall rest = await host.Post("/api/answer_approval", Personas.Nadia, Answer(record, "approve", "Fine"));
        Assert.True(rest.Status == 403, rest.ToString());
        Assert.Equal("ai_cannot_do_this", rest.Json!["error"]!["code"]!.GetValue<string>());
        Assert.Contains("AI never approves", rest.Json["error"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);

        JsonNode mcp = await host.Call(Personas.Nadia, "answer_approval", Answer(record, "approve", "Fine"));
        Assert.Equal("ai_cannot_do_this", mcp["error"]!["code"]!.GetValue<string>());
        HttpCall list = await host.Mcp(Personas.AllRoles, "tools/list");
        Assert.DoesNotContain(list.Json!["result"]!["tools"]!.AsArray(), t => t!["name"]!.GetValue<string>() == "answer_approval");

        HttpCall runner = await host.Post("/api/answer_approval", Personas.Runner, Answer(record, "approve", "Fine"));
        Assert.True(runner.Status == 403, runner.ToString());

        // Nothing written but the two refused AI calls, on REST and on MCP; the app-only token never reached a tool.
        Assert.Equal(StoreDumps.WithoutAudit(before), StoreDumps.WithoutAudit(host));
        Assert.Equal(2, StoreDumps.Refusals(host).Count);
        Assert.All(StoreDumps.Refusals(host), r => Assert.Equal("ai-client-for-person", r.Text("actorKind")));
        Assert.Equal("pending", host.Get<IRecordStore>().Get("approval-record", record)!.Text("outcomeMeaning"));
        Assert.Equal("proposed", host.Get<IRecordStore>().Get("decision", decision)!.Text("statusMeaning"));
    }

    // The Approvals list and the decision page read through the REST API (document 24, tools 53 and 54). The request asks
    // Nadia, so the web app offers her answer_approval; Claude Code acting for her reads the same list with no next tool.
    // The decision page evaluates the rules while the decision is proposed and shows the stored records once it is
    // settled. Reads write nothing.
    [Fact]
    public async Task TheApprovalsListAndTheDecisionPageReadThroughTheWebApp()
    {
        await using ProductTestHost host = ProductTestHost.Start();
        (string decision, string record) = await SubmittedRiskAcceptance(host);
        string before = ((RowStore)host.Get<IProductStore>()).Dump();

        HttpCall mine = await host.Post("/api/list_approvals", Personas.NadiaWeb, "{\"view\":\"mine\"}");
        Assert.True(mine.Status == 200, mine.ToString());
        JsonNode item = Assert.Single(mine.Json!["data"]!["items"]!.AsArray(), i => i!["approvalRecordId"]!.GetValue<string>() == record)!;
        Assert.Equal("answer_approval", item["nextTool"]!.GetValue<string>());
        Assert.True(item["isOwnProposal"]!.GetValue<bool>());
        Assert.Equal("business-owner", item["askedOwnerRole"]!["key"]!.GetValue<string>());

        HttpCall viaAi = await host.Post("/api/list_approvals", Personas.Nadia, "{\"view\":\"mine\"}");
        Assert.True(viaAi.Status == 200, viaAi.ToString());
        Assert.Null(Assert.Single(viaAi.Json!["data"]!["items"]!.AsArray(), i => i!["approvalRecordId"]!.GetValue<string>() == record)!["nextTool"]);

        HttpCall page = await host.Post("/api/get_decision", Personas.NadiaWeb, "{\"decisionId\":\"" + decision + "\"}");
        Assert.True(page.Status == 200, page.ToString());
        JsonNode data = page.Json!["data"]!;
        Assert.Equal("proposed", data["decision"]!["status"]!["meaning"]!.GetValue<string>());
        Assert.Equal(Personas.NadiaId, data["decision"]!["proposedBy"]!["objectId"]!.GetValue<string>());
        Assert.False(data["decision"]!["isAiCreated"]!.GetValue<bool>());
        Assert.True(data["approvals"]!["evaluated"]!.GetValue<bool>());
        Assert.Equal("business-owner", data["approvals"]!["records"]![0]!["askedOwnerRole"]!["key"]!.GetValue<string>());
        Assert.Equal(before, ((RowStore)host.Get<IProductStore>()).Dump());

        HttpCall answered = await host.Post("/api/answer_approval", Personas.NadiaWeb, Answer(record, "approve", "The paper copy covers the offline gap."));
        Assert.True(answered.Status == 200, answered.ToString());
        JsonNode settled = (await host.Post("/api/get_decision", Personas.NadiaWeb, "{\"decisionId\":\"" + decision + "\"}")).Json!["data"]!;
        Assert.Equal("accepted", settled["decision"]!["status"]!["meaning"]!.GetValue<string>());
        Assert.False(settled["approvals"]!["evaluated"]!.GetValue<bool>());
        Assert.True(settled["approvals"]!["records"]![0]!["countsTowardRequirement"]!.GetValue<bool>());
        HttpCall answeredList = await host.Post("/api/list_approvals", Personas.NadiaWeb, "{\"view\":\"answered\"}");
        Assert.Equal("approved", Assert.Single(answeredList.Json!["data"]!["items"]!.AsArray(), i => i!["approvalRecordId"]!.GetValue<string>() == record)!["outcome"]!["meaning"]!.GetValue<string>());
    }

    // Nadia, Inspection Scheduler's business owner, proposes a risk acceptance in the web app and submits it: one pending
    // request, which asks the business owner (Balanced, risk-acceptance-approval, with a typed reason).
    private static async Task<(string Decision, string Record)> SubmittedRiskAcceptance(ProductTestHost host)
    {
        HttpCall proposed = await host.Post("/api/save_decision_proposal", Personas.NadiaWeb, RiskAcceptance);
        Assert.True(proposed.Status == 200, proposed.ToString());
        string decision = proposed.Json!["data"]!["recordId"]!.GetValue<string>();
        HttpCall submitted = await host.Post("/api/submit_record", Personas.NadiaWeb, "{\"subjectKind\":\"decision\",\"subjectId\":\"" + decision + "\"}");
        Assert.True(submitted.Status == 200, submitted.ToString());
        StoredRow record = Assert.Single(host.Get<IRecordStore>().Query(RowQuery.For("approval-record").Where("decision", decision)));
        Assert.Equal("business-owner", record.Text("requiredOwnerRole"));
        return (decision, record.Id);
    }

    private static string Answer(string record, string answer, string? reason = null)
    {
        return "{\"approvalRecordId\":\"" + record + "\",\"answer\":\"" + answer + "\"" + (reason == null ? string.Empty : ",\"reason\":\"" + reason + "\"") + "}";
    }

    [Fact]
    public async Task OnlyTheDeclaredWebAppIsThePersonAndAnAgentHoldsNoRoleOnAnyEntry()
    {
        await using ProductTestHost host = ProductTestHost.Start();

        // An AI client that is not a Known Client gets nothing, though the same person reads through the web app.
        HttpCall unknownClient = await host.Post("/api/get_portfolio_summary", Personas.PriyaOtherClient, "{}");
        Assert.Equal(403, unknownClient.Status);
        Assert.Equal("not_authorized", unknownClient.Json!["error"]!["code"]!.GetValue<string>());
        Assert.Equal(200, (await host.Post("/api/get_portfolio_summary", Personas.PriyaWeb, "{}")).Status);

        // An agent user signed in through the web app is still an agent: no role, no read.
        HttpCall agent = await host.Post("/api/get_portfolio_summary", Personas.AgentWeb, "{}");
        Assert.Equal(403, agent.Status);
        Assert.Equal("not_authorized", agent.Json!["error"]!["code"]!.GetValue<string>());
        HttpCall agentWhoAmI = await host.Post("/api/whoami", Personas.AgentWeb, "{}");
        Assert.Equal(403, agentWhoAmI.Status);

        // The web app's token is for the REST API only: the MCP endpoint wants its own scope.
        HttpCall mcp = await host.Mcp(Personas.NadiaWeb, "tools/call", "{\"name\":\"whoami\",\"arguments\":{}}");
        Assert.True(mcp.Status == 403, mcp.ToString());
    }

    [Fact]
    public async Task WhoamiThroughTheWebAppAnswersPersonWithTheSameRoles()
    {
        await using ProductTestHost host = ProductTestHost.Start();
        HttpCall web = await host.Post("/api/whoami", Personas.OwenWeb, "{}");
        Assert.True(web.Status == 200, web.ToString());
        Assert.Equal("person", web.Json!["data"]!["actorKind"]!.GetValue<string>());
        Assert.Equal("Web app", web.Json["data"]!["clientApp"]!["label"]!.GetValue<string>());

        JsonNode ai = await host.Call(Personas.Owen, "whoami");
        Assert.Equal("ai-client-for-person", ai["data"]!["actorKind"]!.GetValue<string>());
        Assert.Equal(
            ai["data"]!["productRoles"]!.AsArray().Select(r => r!["roleKey"]!.GetValue<string>()).OrderBy(k => k, StringComparer.Ordinal),
            web.Json["data"]!["productRoles"]!.AsArray().Select(r => r!["roleKey"]!.GetValue<string>()).OrderBy(k => k, StringComparer.Ordinal));
    }

    private static IEnumerable<string> Roles(JsonNode me)
    {
        return me["roles"]!.AsArray().Select(r => r!["roleKey"]!.GetValue<string>()).OrderBy(k => k, StringComparer.Ordinal).ToList();
    }

    // Records the store call context each request ran under (what the Azure SQL store writes to SESSION_CONTEXT).
    private sealed class CallContextRecorder : IStartupFilter
    {
        private readonly List<StoreCallContext> _contexts;

        public CallContextRecorder(List<StoreCallContext> contexts)
        {
            _contexts = contexts;
        }

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.Use(async (context, nextMiddleware) =>
                {
                    await nextMiddleware(context);
                    StoreCallContext? recorded = ProductRequest.CallContext(context);
                    if (recorded != null)
                    {
                        lock (_contexts)
                        {
                            _contexts.Add(recorded);
                        }
                    }
                });
                next(app);
            };
        }
    }
}
