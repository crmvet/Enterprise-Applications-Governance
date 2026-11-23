using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Runtime;
using Product.Engine.Runtime.Callers;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Backend.InProcess;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Json;
using Product.Mcp.Core.Protocol;
using Xunit;
using W = Product.Mcp.Core.Tests.Walkthrough.WalkthroughWorld;

namespace Product.Mcp.Core.Tests.Walkthrough;

// The scripted walkthrough (src/demo/walkthrough/README.md): eight steps through the MCP server core as Claude Code
// would drive it, with the Balanced preset in force, fictional people and a recorder that keeps every request and
// answer. The test asserts every outcome the page states, then compares the outcome digest of each exchange with the
// checked-in transcript (or writes it when WALKTHROUGH_WRITE=1).
//
// WORKING DEFAULT (open design question 9): an AI client submits the intake and the change for the person.
// WORKING DEFAULT (open design question 8): tier answers are a suggestion; only an accepted decision sets the tier.
public sealed class WalkthroughTests
{
    private const string PriyaAi = "Priya Castell (maker) through Claude Code";
    private const string OwenAi = "Owen Achterberg (platform admin) through Claude Code";
    private const string MateoAi = "Mateo Brandt (reader) through Claude Code";
    private const string PipelineActor = "Field ops release pipeline (workload identity)";

    [Fact]
    public void TheWalkthroughStoryHolds()
    {
        W w = W.Create();
        var steps = new List<JsonObject>();
        int mark = 0;
        void Step(string id, string title, string who, string asked, string decided, string[] main, bool safety = false, params string[] notes)
        {
            int end = w.Recorder.Exchanges.Count;
            int[] shown = Enumerable.Range(mark, end - mark).Where(i => main.Any(m => w.Recorder.Exchanges[i]["entryPoint"]!.GetValue<string>().StartsWith(m, StringComparison.Ordinal))).ToArray();
            Assert.NotEmpty(shown);
            steps.Add(new JsonObject
            {
                ["id"] = id,
                ["title"] = title,
                ["who"] = who,
                ["asked"] = asked,
                ["decided"] = decided,
                ["safety"] = safety,
                ["notes"] = new JsonArray(notes.Select(n => (JsonNode)JsonValue.Create(n)!).ToArray()),
                ["exchanges"] = new JsonArray(Enumerable.Range(mark, end - mark).Select(i => (JsonNode)JsonValue.Create(i)!).ToArray()),
                ["main"] = new JsonArray(shown.Select(i => (JsonNode)JsonValue.Create(i)!).ToArray()),
            });
            mark = end;
        }

        // ---- Step 1: a maker asks for a new app; the AI files and submits the intake ----
        McpResponse init = w.Mcp("1", PriyaAi, W.TokenPriya, "initialize", "{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"claude-code\",\"version\":\"2.0.0\"}}");
        Assert.Equal(200, init.HttpStatus);
        McpResponse list = w.Mcp("1", PriyaAi, W.TokenPriya, "tools/list", null);
        string[] toolNames = Js.Items(list.Result, "tools").Select(t => Js.Text(t, "name")!).ToArray();
        Assert.Contains("save_intake_draft", toolNames);
        Assert.Contains("submit_record", toolNames);
        Assert.DoesNotContain(toolNames, n => new[] { "approve", "accept", "reject", "authorize", "activate" }.Any(v => n.StartsWith(v + "_", StringComparison.Ordinal)));
        Assert.DoesNotContain("run_gap_check", toolNames);
        JsonTreeNode duplicates = Ok(w.Call("1", PriyaAi, W.TokenPriya, "check_duplicates", "{\"text\":\"field inspections app for site inspectors\",\"proposedKindKey\":\"power-platform-app\"}"));
        Assert.Equal(W.InspectionScheduler, Js.Text(Js.Items(Data(duplicates), "matches")[0], "businessApplicationId"));
        JsonTreeNode draft = Ok(w.Call("1", PriyaAi, W.TokenPriya, "save_intake_draft", "{\"title\":\"Field inspections app\",\"need\":\"Inspectors fill in paper forms on site and retype them at the office. They need one phone app that works offline and sends results to the register.\",\"proposedKindKey\":\"power-platform-app\",\"expectedBenefit\":\"About two hours saved per inspector each week, and no retyping errors\",\"clientRequestId\":\"priya-intake-0001\"}"));
        string intake = Text(Data(draft), "recordId");
        Assert.Equal("draft", Text(Data(draft), "status.meaning"));
        Assert.Equal("ai-client-for-person", Text(Data(draft), "performedBy.actorKind"));
        Assert.Equal("Claude Code", Text(Data(draft), "performedBy.clientApp.label"));
        Assert.Equal(true, Js.Flag(Data(draft), "isAiCreated"));
        JsonTreeNode submittedIntake = Ok(w.Call("1", PriyaAi, W.TokenPriya, "submit_record", "{\"subjectKind\":\"intake-request\",\"subjectId\":\"" + intake + "\",\"note\":\"Ready for triage\"}"));
        Assert.Equal("submitted", Text(Data(submittedIntake), "status.meaning"));
        Assert.Equal("null", Text(Data(submittedIntake), "engineAccepted"));
        JsonTreeNode triageCard = Assert.Single(Js.Items(Data(submittedIntake), "approvalsRequested"));
        Assert.Equal("platform-admin", Js.Text(triageCard, "requiredRoleKey"));
        Assert.Equal("proposed", w.Store.Get("decision", Text(Data(submittedIntake), "decisionId"))!.Text("statusMeaning"));
        Step(
            "1",
            "A maker asks for a new app, and the AI files the intake",
            "Priya Castell, a maker in field operations, asks Claude Code",
            "Our inspectors still fill in paper forms. Submit an intake for a new field inspections app for me.",
            "Claude Code listed " + toolNames.Length + " tools for Priya's roles (none of them can approve), checked for duplicates and found Inspection Scheduler, saved the intake as a draft stamped `ai-client-for-person` and AI-created, then submitted it, which the Tool Policy lets an AI client do for a person (AI May Submit, working default question 9). The engine moved the intake to submitted and opened one triage card for a platform admin (the intake decision right); it accepted nothing.",
            new[] { "tools/call submit_record" });

        // ---- Step 2: the tier is assessed and the app registered with owners ----
        JsonTreeNode tier = Ok(w.Call("2", PriyaAi, W.TokenPriya, "answer_tier_questions", "{\"intakeRequestId\":\"" + intake + "\",\"answers\":[{\"questionKey\":\"data-change\",\"answerKey\":\"as-user\"},{\"questionKey\":\"sensitivity\",\"answerKey\":\"internal\"},{\"questionKey\":\"reach\",\"answerKey\":\"department\"},{\"questionKey\":\"failure-impact\",\"answerKey\":\"team-slowed\"}]}"));
        Assert.Equal("tier-2", Text(Data(tier), "computedTier.key"));
        JsonTreeNode app = Ok(w.Call("2", PriyaAi, W.TokenPriya, "save_application_draft", "{\"name\":\"Field Inspections\",\"purpose\":\"Record site inspections on the phone, offline, and send the results to the register\",\"applicationKindKey\":\"power-platform-app\",\"portfolioId\":\"" + W.FieldOperations
            + "\",\"buildPathKey\":\"citizen\",\"intakeRequestId\":\"" + intake + "\",\"proposedOwners\":[{\"ownerRoleKey\":\"business-owner\",\"objectId\":\"" + W.Nadia + "\"},{\"ownerRoleKey\":\"technical-owner\",\"objectId\":\"" + W.Priya + "\"}],"
            + "\"components\":[{\"componentKindKey\":\"canvas-app\",\"externalId\":\"" + W.FieldInspectionsCanvas + "\",\"url\":\"https://make.powerapps.com/environments/field-ops-prod/apps/209\",\"labelAsSeen\":\"Field Inspections\"}]}"));
        string appId = Text(Data(app), "recordId");
        Assert.Equal("draft", Text(Data(app), "status.meaning"));
        Assert.Equal(false, Js.Flag(Data(app), "confirmationCardSent"));
        Assert.Contains(Js.Items(app, "warnings"), x => Js.Text(x, "code") == "confirmation-card.not-sent");
        Assert.Equal("proposed", w.Store.Get("decision", Text(Data(app), "ownershipDecisionId"))!.Text("statusMeaning"));
        Assert.Empty(w.Store.Query(RowQuery.For("owner-assignment").Where("businessApplication", appId)));
        Assert.Null(w.Store.Get("business-application", appId)!.Text("tier"));

        // The stand-in for the update-subject effect, which is not built yet: test setup.
        const string registered = "{\"status\":\"app-registered\",\"statusMeaning\":\"registered\",\"tier\":\"tier-2\",\"lifecycleStage\":\"build\",\"stageSince\":\"2026-11-23T09:00:00Z\",\"recordNumber\":\"APP-0105\"}";
        string businessOwner = "{\"businessApplication\":\"" + appId + "\",\"ownerRole\":\"business-owner\",\"principal\":\"" + W.Nadia + "\",\"isDeputy\":false,\"startOn\":\"2026-11-23\"}";
        string technicalOwner = "{\"businessApplication\":\"" + appId + "\",\"ownerRole\":\"technical-owner\",\"principal\":\"" + W.Priya + "\",\"isDeputy\":false,\"startOn\":\"2026-11-23\"}";
        w.Store.Tamper("business-application", appId, Js.Parse(registered));
        w.Store.Seed("owner-assignment", businessOwner, id: "00000000-0000-4000-8000-000000000556");
        w.Store.Seed("owner-assignment", technicalOwner, id: "00000000-0000-4000-8000-000000000557");
        w.Recorder.AddSeed(
            "2",
            "Stand-in for the update-subject effect, which is not built yet. In the design the person confirms the registration on a confirmation card, and the accepted tier and ownership decisions write these columns and rows. Here the runner writes them so the walk can go on. No engine write, no decision accepted.",
            new JsonObject
            {
                ["business-application " + appId] = JsonNode.Parse(registered),
                ["owner-assignment 00000000-0000-4000-8000-000000000556"] = JsonNode.Parse(businessOwner),
                ["owner-assignment 00000000-0000-4000-8000-000000000557"] = JsonNode.Parse(technicalOwner),
            },
            w.Clock.UtcNow);
        Step(
            "2",
            "The tier is assessed, and the app is registered with its owners",
            "Priya Castell asks Claude Code",
            "Answer the tier questions with me, then register the app with Nadia Ferreira as business owner and me as technical owner.",
            "OP-01 computed Tier 2 from the four answers, as a suggestion only: the tier is set by an accepted tier decision, never by an AI's answers. save_application_draft kept Field Inspections a draft, put the two owners on a proposed ownership decision instead of writing owner rows, and warned `confirmation-card.not-sent` (a warning of the MCP tool, not an engine reason code), because registration needs the person's confirmation card.",
            new[] { "tools/call save_application_draft", "InMemoryStore" },
            false,
            "Not built yet: the update-subject effect that writes the registration, the tier and the owner rows once those decisions are accepted. The runner writes them as test setup (the seed below), so the rest of the walk has a registered Tier 2 app with Nadia as business owner. The ownership decision itself stays proposed in this walk.");

        // ---- Step 3: the AI proposes the go-live and sends it for approval ----
        w.Clock.UtcNow = new DateTime(2026, 11, 24, 10, 0, 0, DateTimeKind.Utc);
        JsonTreeNode change = Ok(w.Call("3", PriyaAi, W.TokenPriya, "save_change_request_draft", "{\"title\":\"Go-live: Field Inspections 1.0 in Field Ops Prod\",\"businessApplicationId\":\"" + appId + "\",\"changeTypeKey\":\"normal\",\"description\":\"First release of Field Inspections to production for the north region inspectors.\",\"windowStart\":\"2026-11-26T06:00:00Z\",\"windowEnd\":\"2026-11-26T08:00:00Z\",\"plannedTargetId\":\"" + W.FieldOpsProd + "\"}"));
        string changeId = Text(Data(change), "recordId");
        Assert.Equal("draft", Text(Data(change), "status.meaning"));
        JsonTreeNode submitted = Ok(w.Call("3", PriyaAi, W.TokenPriya, "submit_record", "{\"subjectKind\":\"change-request\",\"subjectId\":\"" + changeId + "\",\"note\":\"Tested with two inspectors in Field Ops Test\"}"));
        string decision = Text(Data(submitted), "decisionId");
        JsonTreeNode ownerCard = Assert.Single(Js.Items(Data(submitted), "approvalsRequested"));
        Assert.Equal("business-owner", Js.Text(ownerCard, "requiredOwnerRoleKey"));
        Assert.Equal("proposed", w.Store.Get("decision", decision)!.Text("statusMeaning"));
        Assert.Equal("change-authorization", w.Store.Get("decision", decision)!.Text("decisionType"));
        Assert.True(w.Store.Get("decision", decision)!.Flag("isAiCreated"));
        JsonTreeNode pending = Ok(w.Call("3", PriyaAi, W.TokenPriya, "get_approval_status", "{\"subjectKind\":\"change-request\",\"subjectId\":\"" + changeId + "\"}"));
        Assert.Equal(false, Js.Flag(Data(pending), "ready"));
        Assert.NotEmpty(Js.Items(Data(pending), "missing"));
        StoredRow card = w.Store.Query(RowQuery.For("approval-record").Where("decision", decision)).Single();
        Assert.Equal("change-auth-owner", card.Text("approvalRule"));
        Step(
            "3",
            "The AI proposes the go-live and sends it for approval",
            "Priya Castell asks Claude Code",
            "Plan the go-live of Field Inspections 1.0 to production on Thursday between 06:00 and 08:00 UTC, and send it for approval.",
            "The AI drafted a normal change request with its window and submitted it. The engine created the change authorization decision in proposed, AI-created, and OP-02 found one requirement: rule `change-auth-owner`, the application's business owner, everyone must approve. One pending card now waits for Nadia Ferreira, and get_approval_status answers ready false with that approval missing.",
            new[] { "tools/call submit_record", "tools/call get_approval_status" });

        // ---- Step 4: the AI client tries to approve it (the safety moment) ----
        string before = w.Store.Dump();
        JsonTreeNode noTool = Fails(w.Call("4", PriyaAi, W.TokenPriya, "approve_decision", "{\"decisionId\":\"" + decision + "\"}"), McpErrorCodes.NotFound);
        CallerIdentity ai = CallerIdentity.AiClient(W.Priya, W.GroupsOf(W.Priya), "Claude Code", clientAppVerified: true);
        string cardWrite = "{\"outcome\":\"approval-approved\",\"approver\":\"" + W.Nadia + "\",\"approverActorKind\":\"person\",\"approverPrincipalKind\":\"user\",\"respondedOn\":\"2026-11-24T10:00:00Z\",\"approverHoldsRequiredRole\":true}";
        RuntimeOutcome tryCard = w.Engine("4", PriyaAi, "engine", "table write: Approval Record", ai, Target("approval-record", card.Id, cardWrite),
            () => w.Runtime.Write(ai, WriteCommand.Update("approval-record", card.Id, cardWrite)), o => EngineErrorMap.Map(o, ContractSet.Embedded, true));
        Assert.False(tryCard.Succeeded);
        Assert.Contains(tryCard.Reasons, r => r.Code == "approval.outcome-writer");
        Assert.Contains(tryCard.Reasons, r => r.Code == "ai.status-not-allowed");
        Assert.Equal(McpErrorCodes.AiCannotDoThis, EngineErrorMap.Map(tryCard, ContractSet.Embedded, true).Code);
        const string acceptWrite = "{\"status\":\"decision-accepted\"}";
        RuntimeOutcome tryAccept = w.Engine("4", PriyaAi, "engine", "table write: Decision", ai, Target("decision", decision, acceptWrite),
            () => w.Runtime.Write(ai, WriteCommand.Update("decision", decision, acceptWrite)), o => EngineErrorMap.Map(o, ContractSet.Embedded, true));
        Assert.False(tryAccept.Succeeded);
        Assert.Contains(tryAccept.Reasons, r => r.Code == "decision.accept-by-engine-only");
        Assert.Contains(tryAccept.Reasons, r => r.Code == "ai.status-not-allowed");
        Assert.Equal(McpErrorCodes.AiCannotDoThis, EngineErrorMap.Map(tryAccept, ContractSet.Embedded, true).Code);
        Assert.Equal(before, w.Store.Dump());
        Step(
            "4",
            "The AI client tries to approve it, and cannot",
            "Priya Castell asks Claude Code",
            "Nadia said yes in the hallway. Approve the go-live for her so we keep Thursday.",
            "No tool approves, so the server answers `not_found` for approve_decision. Then the same AI client, stamped `ai-client-for-person`, tries the engine's table door directly: writing the card's outcome is refused with `ai.status-not-allowed` and `approval.outcome-writer` (only the approval runner writes an answer), and writing the decision as accepted is refused with `ai.status-not-allowed` and `decision.accept-by-engine-only` (only the engine accepts, when counted approvals meet the rule). The server's own map turns both into the fixed code `ai_cannot_do_this`. The store is byte for byte what it was.",
            new[] { "tools/call approve_decision", "table write" },
            true,
            "The table door stands for any route that is not this server's tools, such as a generic table write. The engine refuses these writes whatever the tool or route, and in the design the AI approval lock ships locked.");

        // ---- Step 5: the business owner answers the card; the engine accepts the decision ----
        w.Clock.UtcNow = new DateTime(2026, 11, 24, 15, 20, 0, DateTimeKind.Utc);
        CallerIdentity runner = CallerIdentity.Workload(W.Runner, clientApp: "approval runner");
        string answer = "{\"outcome\":\"approval-approved\",\"approver\":\"" + W.Nadia + "\",\"approverActorKind\":\"person\",\"approverPrincipalKind\":\"user\",\"respondedOn\":\"2026-11-24T15:18:00Z\",\"approverHoldsRequiredRole\":true,\"channel\":\"teams\",\"typedReason\":\"Tested with two inspectors. Go ahead on Thursday morning.\"}";
        RuntimeOutcome answered = w.Engine("5", "Approval runner, for Nadia Ferreira's answer", "engine", "approval writer: Approval Record", runner, Target("approval-record", card.Id, answer),
            () => w.Runtime.Write(runner, WriteCommand.Update("approval-record", card.Id, answer)));
        Assert.True(answered.Succeeded, answered.ToString());
        Assert.Contains(answered.Writes, x => x.Entity == "decision" && x.Effect == EffectKind.AcceptDecision);
        StoredRow accepted = w.Store.Get("decision", decision)!;
        Assert.Equal("accepted", accepted.Text("statusMeaning"));
        Assert.Equal("engine", accepted.Text("performedByActorKind"));
        Assert.True(w.Store.Get("approval-record", card.Id)!.Flag("countsTowardRequirement"));
        JsonTreeNode ready = Ok(w.Call("5", PriyaAi, W.TokenPriya, "get_approval_status", "{\"subjectKind\":\"change-request\",\"subjectId\":\"" + changeId + "\"}"));
        Assert.Equal(true, Js.Flag(Data(ready), "ready"));
        Assert.Empty(Js.Items(Data(ready), "missing"));
        Step(
            "5",
            "The business owner answers the card, and the engine accepts the decision",
            "Nadia Ferreira, the business owner, answers the approval card in Teams",
            "Approve. Tested with two inspectors; go ahead on Thursday morning.",
            "The approval runner, which holds `approval-writer` through its Workload Grant, wrote Nadia's answer: a person, signed in as a user, holding the required owner role, with a typed reason. In the same transaction the engine recomputed the approvals (OP-03 satisfied), marked the answer as counting and accepted the decision itself (effect `accept-decision`, performed by `engine`). The AI's next read shows ready true; no AI wrote any of it.",
            new[] { "approval writer", "tools/call get_approval_status" },
            false,
            "Not connected yet: the approval runner itself. No card went to Teams. The runner makes the write the approval runner will make, as its workload identity, with Nadia's answer, and the engine does the rest.");

        // ---- Step 6: the pipeline records the deployment to production ----
        w.Clock.UtcNow = new DateTime(2026, 11, 26, 6, 50, 0, DateTimeKind.Utc);
        McpResponse appOnly = w.Mcp("6", "Field ops release pipeline (app-only token at the MCP door)", W.TokenPipeline, "tools/call", "{\"name\":\"record_deployment\",\"arguments\":{\"idempotencyKey\":\"field-ops-release-118-prod\",\"businessApplicationId\":\"" + appId + "\",\"artifactName\":\"FieldInspections_managed\",\"artifactVersion\":\"1.0.0\",\"deploymentTargetId\":\"" + W.FieldOpsProd + "\",\"finishedOn\":\"2026-11-26T06:44:00Z\",\"resultMeaning\":\"succeeded\"}}");
        Assert.Equal(403, appOnly.HttpStatus);
        Assert.Equal(McpErrorCodes.NotAuthorized, Js.Text(Js.Member(Js.Member(appOnly.Error, "data"), "error"), "code"));
        CallerIdentity pipeline = CallerIdentity.Workload(W.Pipeline, clientApp: "Field ops release pipeline");
        RuntimeOutcome inWindow = RecordRun(w, pipeline, appId, changeId, "118", "1.0.0", "2026-11-26T06:31:00Z", "2026-11-26T06:44:00Z", new DateTime(2026, 11, 26, 6, 50, 0, DateTimeKind.Utc));
        RuntimeOutcome lateRun = RecordRun(w, pipeline, appId, changeId, "121", "1.0.1", "2026-11-26T19:22:00Z", "2026-11-26T19:35:00Z", new DateTime(2026, 11, 26, 19, 40, 0, DateTimeKind.Utc));
        foreach (RuntimeOutcome run in new[] { inWindow, lateRun })
        {
            Assert.True(run.Succeeded, run.ToString());
            Assert.True(run.Created);
            StoredRow record = w.Store.Get("deployment-record", run.RowId!)!;
            Assert.Equal("pipeline-step", record.Text("deploymentRoute"));
            Assert.Equal("advise", record.Text("gateLevelApplied"));
            Assert.False(record.Flag("isMissingApproval"));
            Assert.False(record.Flag("isOtherPath"));
        }

        Assert.False(w.Store.Get("deployment-record", inWindow.RowId!)!.Flag("isOutsideWindow"));
        Assert.True(w.Store.Get("deployment-record", lateRun.RowId!)!.Flag("isOutsideWindow"));
        Assert.DoesNotContain(inWindow.Reasons, r => r.Code.StartsWith("gate.", StringComparison.Ordinal));
        Assert.Contains(lateRun.Reasons, r => r.Code == "gate.outside-window");
        Step(
            "6",
            "The pipeline records the deployment to production",
            "The field ops release pipeline reports after each run (no person, no AI)",
            "Record run 118: FieldInspections 1.0.0 to Field Ops Prod, finished 06:44 UTC. That evening, record run 121: the 1.0.1 fix, finished 19:35 UTC.",
            "At the MCP door the pipeline's app-only token is refused with HTTP 403 and `not_authorized`: pipelines do not use MCP. Through the record endpoint path, holding `deployment-recorder` through its Workload Grant, both runs are recorded with the route `pipeline-step` and gate level advise (Tier 2, production). Run 118 lies inside the authorized change window and is clean: no flag. Run 121 is recorded too, but flagged outside the window (`gate.outside-window`, isOutsideWindow true), because a record step reports and never stops a deployment.",
            new[] { "POST /api/deployments" },
            false,
            "Not built yet: the record endpoint's HTTP host, so no HTTP request is made. The runner checks the pipeline's body against the endpoint's request schema, then calls the engine entry the host will call (RecordDeployment) as the pipeline's workload identity.");

        // ---- Step 7: the gap check finds an app nobody registered ----
        w.Clock.UtcNow = new DateTime(2026, 11, 27, 8, 0, 0, DateTimeKind.Utc);
        Dictionary<string, int> rowsBefore = RowsByEntity(w.Store.Dump());
        string dumpBefore = w.Store.Dump();
        JsonTreeNode gap = Ok(w.Call("7", OwenAi, W.TokenOwen, "run_gap_check", "{}"));
        Assert.Equal(4, Js.Number(Data(gap), "itemsSeen"));
        Assert.Empty(Js.Items(Data(gap), "vanished"));
        JsonTreeNode claim = Assert.Single(Js.Items(Data(gap), "newClaims"));
        Assert.Equal("Crew Overtime Tracker", Js.Text(claim, "labelAsSeen"));
        Assert.Equal(3, Js.Number(Data(gap), "lastSeenUpdated"));
        Dictionary<string, int> rowsAfter = RowsByEntity(w.Store.Dump());
        var grew = rowsAfter.Where(e => !rowsBefore.TryGetValue(e.Key, out int n) || n != e.Value).ToDictionary(e => e.Key, e => e.Value - (rowsBefore.TryGetValue(e.Key, out int n) ? n : 0));
        Assert.Equal(1, grew["gap-check-run"]);
        Assert.Equal(1, grew["claim"]);
        Assert.Equal(new[] { "claim", "gap-check-run" }, grew.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain(rowsAfter.Keys, k => k.Contains("inventory", StringComparison.Ordinal));
        Assert.DoesNotContain(w.Store.Rows("claim").Single().Columns.Members, m => m.Key == "Crew Overtime Tracker");
        Assert.NotEqual(dumpBefore, w.Store.Dump());
        Step(
            "7",
            "The gap check finds an app nobody registered",
            "Owen Achterberg, platform admin, asks Claude Code",
            "Run the gap check on the Power Platform inventory.",
            "OP-13 compared the inventory page (4 apps) with the register: Inspection Scheduler, Route Planner and Field Inspections are registered components, so only their last seen dates moved; Crew Overtime Tracker is not, so the engine opened one claim with Priya Castell as the suggested claimant. The store gained exactly one gap check run and one claim, and no inventory rows: the product keeps links and claims, never a copy of the inventory.",
            new[] { "tools/call run_gap_check" });

        // ---- Step 8: who owns it, and what breaks if the old app retires ----
        w.Clock.UtcNow = new DateTime(2026, 11, 27, 11, 0, 0, DateTimeKind.Utc);
        string beforeReads = w.Store.Dump();
        JsonTreeNode owners = Ok(w.Call("8", MateoAi, W.TokenMateo, "get_application_owners", "{\"businessApplicationId\":\"" + appId + "\"}"));
        string ownersText = Js.Write(Data(owners));
        Assert.Contains("Nadia Ferreira", ownersText, StringComparison.Ordinal);
        Assert.Contains("Priya Castell", ownersText, StringComparison.Ordinal);
        Assert.Equal(2, Js.Items(Data(owners), "gaps").Count(g => Js.Text(g, "missing") == "deputy"));
        JsonTreeNode impact = Ok(w.Call("8", MateoAi, W.TokenMateo, "get_dependency_impact", "{\"businessApplicationId\":\"" + W.InspectionScheduler + "\",\"direction\":\"dependants\",\"depth\":3}"));
        string impactText = Js.Write(Data(impact));
        foreach (string dependant in new[] { W.RoutePlanner, W.CrewRoster, W.CrewShiftMobile })
        {
            Assert.Contains(dependant, impactText, StringComparison.Ordinal);
        }

        Assert.Equal(3, Js.Items(Data(impact), "nodes").Count);
        Assert.Equal(beforeReads, w.Store.Dump());

        Step(
            "8",
            "Who owns it, and what breaks if the old app retires?",
            "Mateo Brandt, a field ops planner with reader access, asks Claude Code",
            "Who owns Field Inspections now? And what breaks if Inspection Scheduler is retired once Field Inspections is live?",
            "Both are reads, so the engine wrote nothing. get_application_owners names Nadia Ferreira as business owner and Priya Castell as technical owner, and flags that neither role has a deputy. get_dependency_impact walks the dependants of Inspection Scheduler: Route Planner reads its data and Crew Roster calls its API, and Crew Shift Mobile depends on Crew Roster, one hop further. That list is what a retirement decision would have to answer for.",
            new[] { "tools/call get_application_owners", "tools/call get_dependency_impact" });

        // ---- The transcript ----
        var exchanges = new JsonArray();
        for (int i = 0; i < w.Recorder.Exchanges.Count; i++)
        {
            JsonObject x = w.Recorder.Exchanges[i];
            x["n"] = i;
            exchanges.Add(x);
        }

        JsonObject transcript = Transcript(w, steps, exchanges);
        string text = WalkthroughRecorder.Write(transcript);
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "walkthrough-transcript.json"), text);
        string path = Path.Combine(McpHarness.Root, "demo", "walkthrough", "transcript.json");
        if (Environment.GetEnvironmentVariable("WALKTHROUGH_WRITE") == "1")
        {
            File.WriteAllText(path, text);
            return;
        }

        Assert.True(File.Exists(path), "The checked-in transcript is missing: " + path + ". Run with WALKTHROUGH_WRITE=1 (src/demo/walkthrough/README.md).");
        JsonArray stored = JsonNode.Parse(File.ReadAllText(path))!["exchanges"]!.AsArray();
        for (int i = 0; i < Math.Max(stored.Count, exchanges.Count); i++)
        {
            string expected = i < stored.Count ? Digest(stored[i]!) : "(none)";
            string actual = i < exchanges.Count ? Digest(exchanges[i]!) : "(none)";
            Assert.True(expected == actual, "Exchange " + i + " of the walkthrough no longer matches src/demo/walkthrough/transcript.json.\n  transcript: " + expected + "\n  this run:   " + actual
                + "\nIf the change is intended, regenerate the transcript and the page (src/demo/walkthrough/README.md).");
        }
    }

    // One record step call as the record endpoint makes it: the body checked against the endpoint's request schema, then
    // the engine entry the endpoint host calls (EngineRuntime.RecordDeployment, <prefix>_RecordDeployment).
    private static RuntimeOutcome RecordRun(W w, CallerIdentity pipeline, string appId, string changeId, string build, string version, string started, string finished, DateTime now)
    {
        w.Clock.UtcNow = now;
        string key = "field-ops-release-" + build + "-prod";
        string runUrl = "https://devops.example.org/field-ops/_build/results?buildId=" + build;
        string body = "{\"idempotencyKey\":\"" + key + "\",\"businessApplicationId\":\"" + appId + "\",\"artifactName\":\"FieldInspections_managed\",\"artifactVersion\":\"" + version + "\",\"artifactKindKey\":\"solution\",\"deploymentTargetId\":\"" + W.FieldOpsProd
            + "\",\"changeRequestId\":\"" + changeId + "\",\"runUrl\":\"" + runUrl + "\",\"startedOn\":\"" + started + "\",\"finishedOn\":\"" + finished + "\",\"resultMeaning\":\"succeeded\",\"codeApprovalKind\":\"person\"}";
        var schema = ContractSet.Embedded.Schemas.Evaluate(Js.Parse(body), "record-endpoint/deployment.request.schema.json");
        Assert.True(schema.IsValid, string.Join(" | ", schema.Issues.Select(e => e.ToString())));
        var report = new DeploymentReport(key, appId, "FieldInspections_managed", version, Utc(finished))
        {
            ArtifactKindKey = "solution",
            TargetId = W.FieldOpsProd,
            ChangeRequestId = changeId,
            RunUrl = runUrl,
            StartedOnUtc = Utc(started),
            ResultMeaning = "succeeded",
            CodeApprovalKind = CodeApprovalKind.Person,
        };
        return w.Engine("6", PipelineActor, "record-endpoint", "POST /api/deployments (engine entry RecordDeployment)", pipeline, JsonNode.Parse(body)!, () => w.Runtime.RecordDeployment(pipeline, report));
    }

    private static JsonObject Transcript(W w, List<JsonObject> steps, JsonArray exchanges)
    {
        InForceVersion inForce = w.Store.ProbeInForceVersion()!;
        StoredVersion version = w.Store.GetVersion(inForce.ConfigurationSetKey, inForce.VersionNumber)!;
        return new JsonObject
        {
            ["title"] = "A walk through the product (fictional data)",
            ["description"] = "A scripted story through the product's MCP server core, run offline on the in-memory store with a mock sign-in and the Balanced preset: every request and answer, trimmed for reading. Design, not a product.",
            ["eyebrow"] = "Product design, step 2, a scripted walk, 2026-11-22",
            ["generatedBy"] = "src/mcp/tests/Product.Mcp.Core.Tests/Walkthrough/WalkthroughTests.cs",
            ["intro"] = "A scripted story through the product's MCP server core, run offline in one process: an in-memory store instead of a database, a mock sign-in instead of Entra, and the Balanced preset in force. Named, fictional people ask Claude Code for things, and every request and answer below is the real output of the code in src/, trimmed for reading. It is a design walk, not a product.",
            ["about"] = new JsonArray(
                "The runner is a test in the MCP test project. It sends what an MCP client sends: `initialize`, `tools/list`, then `tools/call` with JSON arguments and the person's bearer token. The token is a mock value: the mock identity maps it to a person and the groups the person is in, and the client binding patch of the configuration maps those groups to roles. Nothing reaches a network, and nothing listens on a port.",
                "Some calls are not MCP, because the caller is not an AI client: the approval runner writing the business owner's answer, the release pipeline recording deployments through the record endpoint path, and, in step 4, an AI client trying the engine's table door. One entry is a runner seed: a stand-in for an engine effect that is not built yet, marked as such.",
                "The test asserts every outcome this page states, and it compares the outcome of every exchange (ok, the fixed error code, the reason codes) with the checked-in transcript, so the page cannot quietly drift from the code. Each step shows its main calls with the request and the answer as JSON; its other calls are folded below them. Long fields are cut, and each box says what was left out."),
            ["configuration"] = new JsonObject
            {
                ["preset"] = "balanced",
                ["patches"] = new JsonArray("client binding patch of the MCP test harness (role groups, two Workload Grants)", "Known Client: Claude Code"),
                ["version"] = version.VersionNumber,
                ["contentHash"] = version.ContentHash,
            },
            ["people"] = new JsonArray(
                Person("Priya Castell", "Maker who asks for the app; its technical owner", "Mock groups Field ops makers and Product requesters, bound to the roles `maker` and `requester`"),
                Person("Nadia Ferreira", "Business owner who answers the approval card", "Mock group Application owners (role `application-owner`), and the business owner row of the app"),
                Person("Owen Achterberg", "Platform admin who runs the gap check", "Mock group Platform admins, bound to `platform-admin`"),
                Person("Mateo Brandt", "Field ops planner who asks who owns what", "Mock group Portfolio readers, bound to `reader`"),
                Person("Ines Varga", "Business owner of three older apps; does not act", "Owner rows only"),
                Person("Approval runner", "Workload that writes the answers given on cards", "Workload Grant `approval-runner` for the role `approval-writer` (app-only)"),
                Person("Field ops release pipeline", "Workload that records deployments", "Workload Grant `build-pipeline` for the role `deployment-recorder` (app-only)")),
            ["files"] = new JsonArray(
                "The runner: `src/mcp/tests/Product.Mcp.Core.Tests/Walkthrough/` (`WalkthroughWorld.cs` holds the world and the recorder, `WalkthroughTests.cs` the story). It runs in the test step of `src/tools/ci.mjs` in about a second.",
                "The transcript: `src/demo/walkthrough/transcript.json`, every exchange in full (tools/list keeps each tool's name, annotations and schema size). The page: `src/demo/walkthrough/render-page.mjs` renders it. How to run both again: `src/demo/walkthrough/README.md`.",
                "Stood in for, because not built yet: the update-subject effect (step 2 seeds the registration, the tier and the owner rows), the record endpoint's HTTP host (step 6 calls the engine entry the host will call) and the approval runner itself (step 5 makes its write)."),
            ["steps"] = new JsonArray(steps.Select(s => (JsonNode)s).ToArray()),
            ["exchanges"] = exchanges,
        };
    }

    // The request of a table write: the row it names and the columns it sends.
    private static JsonObject Target(string entity, string rowId, string afterJson)
    {
        return new JsonObject { ["operation"] = "update", ["entity"] = entity, ["rowId"] = rowId, ["after"] = JsonNode.Parse(afterJson) };
    }

    private static JsonObject Person(string name, string role, string how)
    {
        return new JsonObject { ["name"] = name, ["role"] = role, ["how"] = how };
    }

    private static string Digest(JsonNode exchange)
    {
        return exchange["step"] + " " + exchange["channel"] + " " + exchange["entryPoint"] + " " + exchange["outcome"]!.ToJsonString();
    }

    private static Dictionary<string, int> RowsByEntity(string dump)
    {
        return dump.Split('\n').Where(l => l.Length > 0).GroupBy(l => l.Substring(0, l.IndexOf(' '))).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
    }

    private static DateTime Utc(string text)
    {
        return DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
    }

    private static JsonTreeNode Ok(JsonTreeNode? envelope)
    {
        Assert.NotNull(envelope);
        Assert.True(Js.Flag(envelope, "ok") == true, Js.Write(envelope!));
        return envelope!;
    }

    private static JsonTreeNode Fails(JsonTreeNode? envelope, string code)
    {
        Assert.NotNull(envelope);
        Assert.True(Js.Flag(envelope, "ok") == false, Js.Write(envelope!));
        Assert.Equal(code, Js.Text(Js.Member(envelope, "error"), "code"));
        return envelope!;
    }

    private static JsonTreeNode Data(JsonTreeNode envelope)
    {
        return Js.Member(envelope, "data")!;
    }

    private static string Text(JsonTreeNode node, string path)
    {
        return McpHarness.Text(node, path);
    }
}
