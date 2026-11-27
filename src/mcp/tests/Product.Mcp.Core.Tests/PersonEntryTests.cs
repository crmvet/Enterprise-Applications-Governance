using System;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Runtime.Callers;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Backend;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Json;
using Product.Mcp.Core.Protocol;
using Xunit;

namespace Product.Mcp.Core.Tests;

// The person entry (document 22, section 2.4; document 24, section 4.5): the product's own web app calls the same tools
// through the same core as the person, not as an AI client. Only the web app's client IDs, which the host declares, may
// use it; the Known Client check and the AI-only gates do not apply; roles, the rate limit and the input schema do. The
// MCP endpoint's answers do not change, and nothing a person does through it approves.
public sealed class PersonEntryTests
{
    private const string Intake = "{\"title\":\"Fixture travel requests\",\"need\":\"Replace the shared spreadsheet\",\"proposedKindKey\":\"power-platform-app\"}";

    [Fact]
    public void TheDefaultEntryStillAnswersAsAnAiClient()
    {
        Assert.Equal(CallerEntry.AiClient, new McpServerOptions().Entry);
        McpHarness h = McpHarness.Create();
        JsonTreeNode me = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "whoami"));
        Assert.Equal("ai-client-for-person", McpHarness.Text(me, "actorKind"));
        Assert.Equal(CallerEntry.AiClient, new BackendCaller(McpHarness.Owner, PrincipalKind.User, McpHarness.ClaudeCode).Entry);
    }

    [Fact]
    public void TheWebAppAnswersAsThePersonWithTheSameRoles()
    {
        McpHarness h = McpHarness.Create();
        McpServerCore web = h.PersonServer();
        JsonTreeNode asPerson = McpHarness.Data(h.Ok(McpHarness.TokenOwnerWeb, "whoami", server: web));
        JsonTreeNode asAi = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "whoami"));

        Assert.Equal("person", McpHarness.Text(asPerson, "actorKind"));
        Assert.Equal(McpHarness.WebApp, McpHarness.Text(asPerson, "clientApp.appId"));
        Assert.Equal(McpHarness.WebAppLabel, McpHarness.Text(asPerson, "clientApp.label"));
        Assert.Equal(Js.Write(Js.Member(asAi, "productRoles")!), Js.Write(Js.Member(asPerson, "productRoles")!));
        // The same tools, and on the person entry also the tools served only to a person: answer_approval (D-38),
        // complete_change and complete_review (B24-10), and "Ask again" (renew_approval_request, B24-22).
        string[] personOnly = { "answer_approval", "complete_change", "complete_review", "renew_approval_request" };
        Assert.Equal(Js.Strings(asAi, "tools").Concat(personOnly).OrderBy(n => n, StringComparer.Ordinal), Js.Strings(asPerson, "tools").OrderBy(n => n, StringComparer.Ordinal));
        Assert.All(personOnly, t => Assert.DoesNotContain(t, Js.Strings(asAi, "tools")));
    }

    [Fact]
    public void APersonsIntakeIsThePersonsOwnAct()
    {
        McpHarness h = McpHarness.Create();
        JsonTreeNode mine = McpHarness.Data(h.Ok(McpHarness.TokenOwnerWeb, "save_intake_draft", Intake, server: h.PersonServer()));
        Assert.Equal("person", McpHarness.Text(mine, "performedBy.actorKind"));
        Assert.Equal("null", McpHarness.Text(mine, "performedBy.onBehalfOf"));
        Assert.Equal(false, Js.Flag(mine, "isAiCreated"));
        StoredRow row = h.Store.Get("intake-request", McpHarness.Text(mine, "recordId"))!;
        Assert.Equal("person", row.Text("performedByActorKind"));
        Assert.Null(row.Text("onBehalfOf"));
        Assert.False(row.Flag("isAiCreated"));
        Assert.Equal(McpHarness.WebAppLabel, row.Text("clientApp"));
        Assert.True(row.Flag("clientAppVerified"));

        // The same call on the MCP endpoint is an AI client's, for the person.
        JsonTreeNode drafted = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "save_intake_draft", Intake));
        Assert.Equal("ai-client-for-person", McpHarness.Text(drafted, "performedBy.actorKind"));
        Assert.Equal(McpHarness.Owner, McpHarness.Text(drafted, "performedBy.onBehalfOf.objectId"));
        Assert.Equal(true, Js.Flag(drafted, "isAiCreated"));
        Assert.True(h.Store.Get("intake-request", McpHarness.Text(drafted, "recordId"))!.Flag("isAiCreated"));
    }

    [Fact]
    public void AnotherClientOnThePersonEntryIsRefusedAndNeverCounted()
    {
        // A limit of one call an hour: a refused call that was counted would make the next real call rate limited.
        McpHarness h = McpHarness.Create(PolicyTests.PolicyOperations((tool, values) => tool == "save_intake_draft" ? Js.With(values, ("callsPerPersonPerHour", JsonTreeNode.FromInt64(1))) : values));
        McpServerCore web = h.PersonServer();
        string before = h.Store.Dump();

        JsonTreeNode refused = h.Fails(McpHarness.TokenOwner, "save_intake_draft", Intake, McpErrorCodes.NotAuthorized, server: web);
        Assert.Contains("web app", McpHarness.Text(refused, "error.message"), StringComparison.Ordinal);
        h.Fails(McpHarness.TokenOtherClient, "save_intake_draft", Intake, McpErrorCodes.NotAuthorized, server: web);
        Assert.NotNull(h.Rpc(McpHarness.TokenOwner, "tools/list", "{}").Result);
        McpResponse list = web.Handle(McpRequest.Call(3, "tools/list", Js.EmptyObject, "Bearer " + McpHarness.TokenOwner));
        Assert.Equal(McpErrorCodes.NotAuthorized, Js.Text(Js.Member(Js.Member(list.Error, "data"), "error"), "code"));
        Assert.Equal(before, h.Store.Dump());

        h.Ok(McpHarness.TokenOwnerWeb, "save_intake_draft", Intake, server: web);
        h.Fails(McpHarness.TokenOwnerWeb, "save_intake_draft", Intake, McpErrorCodes.RateLimited, server: web);
    }

    [Fact]
    public void AnAgentUserOnThePersonEntryHoldsNoRoleAndNeverWrites()
    {
        McpHarness h = McpHarness.Create();
        McpServerCore web = h.PersonServer();
        string before = h.Store.Dump();
        h.Fails(McpHarness.TokenAgentWeb, "whoami", "{}", McpErrorCodes.NotAuthorized, server: web);
        h.Fails(McpHarness.TokenAgentWeb, "save_intake_draft", Intake, McpErrorCodes.NotAuthorized, server: web);
        Assert.Equal(before, h.Store.Dump());

        // Past the core's role check, the engine still refuses the agent user's write (RA-06).
        var agent = new BackendCaller(CallerEntry.Person, McpHarness.AgentUser, PrincipalKind.AgentUser, McpHarness.WebApp, new[] { McpHarness.OwnerGroup });
        BackendSession session = h.Backend.OpenSession(agent, "test-agent").Session!;
        Assert.Equal("agent-identity", session.ActorKind);
        Assert.Empty(session.RoleKeys);
        ToolContract tool = ContractSet.Embedded.Tools.Single(t => t.Name == "save_intake_draft");
        ToolResult result = h.Backend.Invoke(session, new ToolInvocation(tool, Js.Parse(Intake), "test-agent", new[] { tool.Name }, new[] { tool.ToolsetKey }));
        Assert.False(result.Ok);
        Assert.Equal(before, h.Store.Dump());
    }

    [Fact]
    public void TheKnownClientListLimitsAiClientsAndNotTheWebApp()
    {
        // A Known Client list that names only Claude Code: the web app is not on it.
        string clients = "[{\"op\":\"upsert\",\"entity\":\"known-client\",\"key\":\"claude-code\",\"values\":{\"label\":\"Claude Code\",\"clientIdentifier\":\"" + McpHarness.ClaudeCode + "\",\"isAiClient\":true,\"isAllowed\":true}}]";
        McpHarness h = McpHarness.Create(clients);
        h.Ok(McpHarness.TokenOwnerWeb, "whoami", server: h.PersonServer());
        h.Fails(McpHarness.TokenOwnerWeb, "whoami", "{}", McpErrorCodes.NotAuthorized);
        h.Ok(McpHarness.TokenOwner, "whoami");
    }

    // No tool takes an approve verb on either entry. The person answers an approval request they were asked through
    // answer_approval (D-38), which the person entry lists and the MCP endpoint never does.
    [Fact]
    public void ThePersonEntryServesNoApproveToolAndAnswersOnlyThroughAnswerApproval()
    {
        McpHarness h = McpHarness.Create();
        McpServerCore web = h.PersonServer();
        foreach (string token in new[] { McpHarness.TokenOwnerWeb, McpHarness.TokenReaderWeb })
        {
            McpResponse response = web.Handle(McpRequest.Call(4, "tools/list", Js.EmptyObject, "Bearer " + token));
            string[] names = Js.Items(response.Result, "tools").Select(t => Js.Text(t, "name")!).ToArray();
            Assert.NotEmpty(names);
            Assert.DoesNotContain(names, n => new[] { "approve", "accept", "reject", "authorize", "activate" }.Any(v => n.StartsWith(v + "_", StringComparison.Ordinal)));
            Assert.Contains("answer_approval", names);
        }

        h.Fails(McpHarness.TokenOwnerWeb, "approve_decision", "{}", McpErrorCodes.NotFound, server: web);
    }

    [Fact]
    public void ADecisionProposedAsAcceptedIsStoredAsProposed()
    {
        McpHarness h = McpHarness.Create();
        McpServerCore web = h.PersonServer();

        // A risk acceptance proposing the accept outcome, by the business owner (who holds its accountable letter): the
        // decision is still only proposed.
        JsonTreeNode accept = McpHarness.Data(h.Ok(McpHarness.TokenOwnerWeb, "save_decision_proposal", "{\"decisionTypeKey\":\"risk-acceptance\",\"subjectKind\":\"business-application\",\"subjectId\":\"" + McpHarness.Travel
            + "\",\"statement\":\"Accept the booking risk\",\"context\":\"A supplier outage\",\"proposedOutcomeKey\":\"accept\",\"reason\":\"Low impact\"}", server: web));
        Assert.Equal("proposed", McpHarness.Text(accept, "status.meaning"));
        Assert.Equal("person", McpHarness.Text(accept, "performedBy.actorKind"));
        Assert.Equal("proposed", h.Store.Get("decision", McpHarness.Text(accept, "recordId"))!.Text("statusMeaning"));

        // An intake decision is the platform admin's to propose in Balanced (intake-a); submitting the intake opens it.
        // The engine refuses the requester on this entry as on the MCP endpoint (document 24, tool 17; B24-5).
        string intake = McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenOwnerWeb, "save_intake_draft", Intake, server: web)), "recordId");
        h.Fails(McpHarness.TokenOwnerWeb, "save_decision_proposal", "{\"decisionTypeKey\":\"intake\",\"subjectKind\":\"intake-request\",\"subjectId\":\"" + intake
            + "\",\"statement\":\"Accept the travel requests intake\",\"context\":\"A shared spreadsheet today\",\"proposedOutcomeKey\":\"accept\",\"reason\":\"Clear need\"}", McpErrorCodes.NotAuthorized, server: web);

        // No input carries a status, on this entry too.
        h.Fails(McpHarness.TokenOwnerWeb, "save_decision_proposal", "{\"decisionTypeKey\":\"retirement\",\"subjectKind\":\"business-application\",\"subjectId\":\"" + McpHarness.Travel
            + "\",\"statement\":\"Retire the travel planner\",\"context\":\"Replaced\",\"proposedOutcomeKey\":\"complete\",\"reason\":\"A duplicate\",\"status\":\"accepted\"}", McpErrorCodes.Validation, server: web);

        // Submitted by the person, a decision that needs cards stays proposed until people answer them.
        JsonTreeNode retire = McpHarness.Data(h.Ok(McpHarness.TokenOwnerWeb, "save_decision_proposal", "{\"decisionTypeKey\":\"retirement\",\"subjectKind\":\"business-application\",\"subjectId\":\"" + McpHarness.Travel
            + "\",\"statement\":\"Retire the travel planner\",\"context\":\"Replaced\",\"proposedOutcomeKey\":\"complete\",\"reason\":\"A duplicate\"}", server: web));
        string decision = McpHarness.Text(retire, "recordId");
        JsonTreeNode submitted = McpHarness.Data(h.Ok(McpHarness.TokenOwnerWeb, "submit_record", "{\"subjectKind\":\"decision\",\"subjectId\":\"" + decision + "\"}", server: web));
        Assert.Equal("proposed", McpHarness.Text(submitted, "status.meaning"));
        Assert.Equal("null", McpHarness.Text(submitted, "engineAccepted"));
        Assert.NotEmpty(Js.Items(submitted, "approvalsRequested"));
        Assert.Equal("proposed", h.Store.Get("decision", decision)!.Text("statusMeaning"));
        Assert.All(h.Store.Query(RowQuery.For("approval-record").Where("decision", decision)), r => Assert.Equal("pending", r.Text("outcomeMeaning")));
    }

    // QA 2026-11-24: a person who records a deployment by hand in the web app gets the person-in-app route, and the answer
    // passes the output schema (it allowed only ai-tool before, so the person saw HTTP 500 for a write that happened).
    [Fact]
    public void APersonRecordsADeploymentByHandAndTheAnswerPassesItsSchema()
    {
        McpHarness h = McpHarness.Create();
        McpServerCore web = h.PersonServer();
        JsonTreeNode check = McpHarness.Data(h.Ok(McpHarness.TokenOwnerWeb, "record_deployment", Scenarios.DeploymentArguments("by-hand").TrimEnd('}') + ",\"validateOnly\":true}", server: web));
        Assert.Equal("person-in-app", McpHarness.Text(check, "route.meaning"));
        JsonTreeNode recorded = McpHarness.Data(h.Ok(McpHarness.TokenOwnerWeb, "record_deployment", Scenarios.DeploymentArguments("by-hand"), server: web));
        Assert.Equal("person-in-app", McpHarness.Text(recorded, "route.meaning"));
        Assert.NotNull(h.Store.Get("deployment-record", McpHarness.Text(recorded, "deploymentRecordId")));
    }

    [Fact]
    public void TheServerAndTheCallerRefuseAWrongEntry()
    {
        McpHarness h = McpHarness.Create();
        Assert.Throws<ArgumentException>(() => McpHarness.NewServer(h.Backend, new RecordingTelemetry(), h.Clock, configure: o => o.Entry = CallerEntry.Workload));
        Assert.Throws<ArgumentException>(() => McpHarness.NewServer(h.Backend, new RecordingTelemetry(), h.Clock, configure: o => o.Entry = CallerEntry.Person));
        Assert.Throws<ArgumentException>(() => McpHarness.NewServer(h.Backend, new RecordingTelemetry(), h.Clock, configure: o =>
        {
            McpHarness.PersonEntry(o);
            o.PersonClientAppIds = new[] { " " };
        }));
        Assert.Throws<ArgumentException>(() => McpHarness.NewServer(h.Backend, new RecordingTelemetry(), h.Clock, configure: o =>
        {
            McpHarness.PersonEntry(o);
            o.PersonClientLabel = string.Empty;
        }));
        Assert.Throws<ArgumentException>(() => new BackendCaller(CallerEntry.Workload, McpHarness.Pipeline, PrincipalKind.ServicePrincipal, McpHarness.ClaudeCode));
        Assert.Equal(CallerEntry.Person, new BackendCaller(CallerEntry.Person, McpHarness.Owner, PrincipalKind.User, McpHarness.WebApp).Entry);
    }
}
