using System.Collections.Generic;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Runtime;
using Product.Engine.Runtime.Callers;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Json;
using Product.Mcp.Core.Protocol;
using Xunit;

namespace Product.Mcp.Core.Tests;

// A person answers an approval request they were asked, on the product's decision page (D-38; document 26, AN-01 and
// AN-02 as D-38 rewrites them). answer_approval is served only on the person entry, the web app: an AI client is never
// served it, and the engine refuses an answer from anyone but the signed-in person the request asks (the role, or the
// owner role on the decision's application, held now; no separation rule against them; the request pending and not
// expired). The answer is counted and the decision moved exactly as for an answer the approval runner brings from a card.
public sealed class AnswerApprovalTests
{
    // A tier 3 application: the owner is its business owner, and the deputy is the deputy business owner.
    private const string Tier3App = "00000000-0000-4000-8000-000000000517";

    [Fact]
    public void ThePersonAnswersOnTheWebAppAndTheEngineAcceptsTheDecision()
    {
        McpHarness h = McpHarness.Create();
        (string decision, string record) = SubmittedRetirement(h, McpHarness.TokenOwner, McpHarness.Travel);

        JsonTreeNode envelope = h.Ok(McpHarness.TokenOwnerWeb, "answer_approval", Answer(record, "approve"), server: h.PersonServer());
        JsonTreeNode data = McpHarness.Data(envelope);

        Assert.Equal(record, McpHarness.Text(data, "recordId"));
        Assert.Equal("approved", McpHarness.Text(data, "status.meaning"));
        Assert.Equal("person", McpHarness.Text(data, "performedBy.actorKind"));
        Assert.Equal(McpHarness.Owner, McpHarness.Text(data, "performedBy.principal.objectId"));
        Assert.Equal("null", McpHarness.Text(data, "performedBy.onBehalfOf"));
        Assert.Equal("false", McpHarness.Text(data, "isAiCreated"));
        Assert.Equal("business-owner", McpHarness.Text(data, "answeredAs.ownerRoleKey"));
        Assert.Equal("true", McpHarness.Text(data, "countsTowardRequirement"));
        Assert.Equal("accepted", McpHarness.Text(data, "decisionStatus.meaning"));
        Assert.Contains("accepted the decision", McpHarness.Text(envelope, "summary"));

        // The answer as the engine stored it: the token's person, as a person and a user, in the app, holding the role.
        StoredRow row = h.Store.Get("approval-record", record)!;
        Assert.Equal(McpHarness.Owner, row.Text("approver"));
        Assert.Equal("person", row.Text("approverActorMeaning"));
        Assert.Equal("user", row.Text("approverPrincipalKind"));
        Assert.Equal("in-app", row.Text("channel"));
        Assert.True(row.Flag("approverHoldsRequiredRole"));
        Assert.True(row.Flag("countsTowardRequirement"));
        Assert.Equal("accepted", h.Store.Get("decision", decision)!.Text("statusMeaning"));
        Assert.Contains(h.Store.Query(RowQuery.For("audit-entry")), a => a.Text("operation") == "approval-outcome-written" && a.Text("rowId") == record && a.Text("actor") == McpHarness.Owner && a.Text("actorKind") == "person");
    }

    // The deputy holds the owner role too, as for "own apps" (document 26, section 5.3), so the deputy may answer a
    // request that asks the business owner of an application they deputize on. A rejection takes a typed reason.
    [Fact]
    public void ADeputyRejectsWithAReasonAndTheEngineRejectsTheDecision()
    {
        McpHarness h = McpHarness.Create();
        (string decision, string record) = SubmittedRetirement(h, McpHarness.TokenOwner, McpHarness.Expense);

        JsonTreeNode data = McpHarness.Data(h.Ok(McpHarness.TokenDeputyWeb, "answer_approval", Answer(record, "reject", "Still used by the finance team."), server: h.PersonServer()));

        Assert.Equal("rejected", McpHarness.Text(data, "status.meaning"));
        Assert.Equal("rejected", McpHarness.Text(data, "decisionStatus.meaning"));
        StoredRow row = h.Store.Get("approval-record", record)!;
        Assert.Equal(McpHarness.Deputy, row.Text("approver"));
        Assert.Equal("Still used by the finance team.", row.Text("typedReason"));
        Assert.Equal("rejected", h.Store.Get("decision", decision)!.Text("statusMeaning"));
    }

    // AI never approves: the MCP endpoint does not list the tool, refuses it by name, and writes nothing; the engine refuses
    // an AI client's answer even when a caller goes around the server core.
    [Fact]
    public void AnAiClientIsNeverServedTheToolAndTheEngineRefusesItsAnswer()
    {
        McpHarness h = McpHarness.Create();
        (string decision, string record) = SubmittedRetirement(h, McpHarness.TokenOwner, McpHarness.Travel);
        McpServerCore web = h.PersonServer();

        Assert.DoesNotContain("answer_approval", ToolNames(h.Rpc(McpHarness.TokenOwner, "tools/list")));
        Assert.Contains("answer_approval", ToolNames(web.Handle(McpRequest.Call(7, "tools/list", null, "Bearer " + McpHarness.TokenOwnerWeb, null))));

        JsonTreeNode refused = h.Fails(McpHarness.TokenOwner, "answer_approval", Answer(record, "approve"), McpErrorCodes.AiCannotDoThis);
        Assert.Contains("AI never approves", McpHarness.Text(refused, "error.message"));

        RuntimeOutcome direct = h.Runtime.AnswerApproval(CallerIdentity.AiClient(McpHarness.Owner, new[] { McpHarness.OwnerGroup }, "Claude Code"), new ApprovalAnswerCommand(record, approve: true));
        Assert.False(direct.Succeeded);
        Assert.Contains(direct.Reasons, r => r.Code == "approval.answer-person-only");

        RuntimeOutcome runner = h.Runtime.AnswerApproval(CallerIdentity.Workload(McpHarness.Runner, clientApp: "approval runner"), new ApprovalAnswerCommand(record, approve: true));
        Assert.False(runner.Succeeded);

        Assert.Equal("pending", h.Store.Get("approval-record", record)!.Text("outcomeMeaning"));
        Assert.Equal("proposed", h.Store.Get("decision", decision)!.Text("statusMeaning"));
    }

    // Only the person the request asks answers it: the reader holds no business owner assignment on the travel planner.
    [Fact]
    public void APersonTheRequestDoesNotAskIsRefusedAndNothingIsWritten()
    {
        McpHarness h = McpHarness.Create();
        (string decision, string record) = SubmittedRetirement(h, McpHarness.TokenOwner, McpHarness.Travel);
        string before = Dump(h);

        JsonTreeNode refused = h.Fails(McpHarness.TokenReaderWeb, "answer_approval", Answer(record, "approve"), McpErrorCodes.NotAuthorized, server: h.PersonServer());
        Assert.Contains("business-owner", McpHarness.Text(refused, "error.message"));

        // validateOnly runs every check, so it is refused too.
        h.Fails(McpHarness.TokenReaderWeb, "answer_approval", Answer(record, "approve", extra: ",\"validateOnly\":true"), McpErrorCodes.NotAuthorized, server: h.PersonServer());

        // Nothing was written but the record of the refused answer: one audit entry (document 25, B25-13). The check
        // (validateOnly) is not an attempt, so it left none.
        Assert.Equal(StripAudit(before), h.Store.DumpWithout("audit-entry"));
        StoredRow refusal = Assert.Single(h.Store.Query(RowQuery.For("audit-entry")), a => a.Text("operation") == "call-refused");
        Assert.Equal("answer_approval", refusal.Text("entityName"));
        Assert.Equal(record, refusal.Text("rowId"));
        Assert.Equal(McpHarness.Reader, refusal.Text("actor"));
        Assert.Equal("person", refusal.Text("actorKind"));
        Assert.Equal("proposed", h.Store.Get("decision", decision)!.Text("statusMeaning"));
    }

    // Balanced refuses self-approval at tier 3 (D-40 keeps it): the deputy who proposed a go-live may not answer it, even
    // though the deputy holds the owner role. The business owner answers the first step; the engine opens the security
    // review step; the same answer again is a conflict; and a step past its Expires On takes no answer.
    [Fact]
    public void SeparationAlreadyAnsweredAndExpiryAreRefusedOnASequentialRule()
    {
        McpHarness h = McpHarness.Create();
        SeedTier3(h);
        McpServerCore web = h.PersonServer();
        JsonTreeNode proposed = h.Ok(McpHarness.TokenDeputy, "save_decision_proposal", Proposal("go-live", Tier3App, "go"));
        string decision = McpHarness.Text(McpHarness.Data(proposed), "recordId");
        h.Ok(McpHarness.TokenDeputy, "submit_record", "{\"subjectKind\":\"decision\",\"subjectId\":\"" + decision + "\"}");
        string owner = Assert.Single(Records(h, decision)).Id;

        JsonTreeNode separation = h.Fails(McpHarness.TokenDeputyWeb, "answer_approval", Answer(owner, "approve"), McpErrorCodes.RuleViolation, server: web);
        // The refusal names the rule by its label, never its key in backticks (wave 4b review).
        Assert.Contains("The separation rule No self-approval at tier 3 keeps you from answering", McpHarness.Text(separation, "error.message"));
        Assert.DoesNotContain("`", McpHarness.Text(separation, "error.message"));
        Assert.Equal("pending", h.Store.Get("approval-record", owner)!.Text("outcomeMeaning"));

        JsonTreeNode data = McpHarness.Data(h.Ok(McpHarness.TokenOwnerWeb, "answer_approval", Answer(owner, "approve"), server: web));
        Assert.Equal("proposed", McpHarness.Text(data, "decisionStatus.meaning"));
        string security = Assert.Single(Js.Strings(data, "openedApprovalRecordIds"));
        Assert.Equal("security-reviewer", h.Store.Get("approval-record", security)!.Text("requiredRole"));

        h.Fails(McpHarness.TokenOwnerWeb, "answer_approval", Answer(owner, "approve"), McpErrorCodes.Conflict, server: web);

        // Once the security step is past its Expires On, it takes no answer, and nothing is written.
        StoredRow step = h.Store.Get("approval-record", security)!;
        h.Store.Tamper("approval-record", security, Js.With(step.Columns, ("expiresOn", JsonTreeNode.FromString("2026-11-22T08:59:00Z"))));
        string before = Dump(h);
        JsonTreeNode expired = h.Fails(McpHarness.TokenOwnerWeb, "answer_approval", Answer(security, "approve", "Reviewed"), McpErrorCodes.Conflict, server: web);
        Assert.Contains("expired", McpHarness.Text(expired, "error.message"));
        Assert.Equal(before, Dump(h));
    }

    // A rejection without a reason never reaches the engine: the input schema needs it. A rule that asks for a typed
    // reason (risk acceptance in Balanced) needs one to approve, and the engine says which field.
    [Fact]
    public void ATypedReasonIsNeededToRejectAndWhereTheRuleAsks()
    {
        McpHarness h = McpHarness.Create();
        McpServerCore web = h.PersonServer();
        (_, string retirement) = SubmittedRetirement(h, McpHarness.TokenOwner, McpHarness.Travel);
        JsonTreeNode noReason = h.Fails(McpHarness.TokenOwnerWeb, "answer_approval", Answer(retirement, "reject"), McpErrorCodes.Validation, server: web);
        Assert.Contains("reason", McpHarness.Text(noReason, "error.message"));
        Assert.Equal("pending", h.Store.Get("approval-record", retirement)!.Text("outcomeMeaning"));

        JsonTreeNode proposed = h.Ok(McpHarness.TokenOwner, "save_decision_proposal", Proposal("risk-acceptance", McpHarness.Travel, "accept"));
        string decision = McpHarness.Text(McpHarness.Data(proposed), "recordId");
        h.Ok(McpHarness.TokenOwner, "submit_record", "{\"subjectKind\":\"decision\",\"subjectId\":\"" + decision + "\"}");
        string record = Assert.Single(Records(h, decision)).Id;
        JsonTreeNode ruleAsks = h.Fails(McpHarness.TokenOwnerWeb, "answer_approval", Answer(record, "approve"), McpErrorCodes.Validation, server: web);
        Assert.Equal("reason", McpHarness.Text(ruleAsks, "error.field"));

        h.Ok(McpHarness.TokenOwnerWeb, "answer_approval", Answer(record, "approve", "The residual risk is within the agreed appetite."), server: web);
        Assert.Equal("accepted", h.Store.Get("decision", decision)!.Text("statusMeaning"));
    }

    // validateOnly runs the check, the write and the recompute, then rolls back: the page can ask whether to show the buttons.
    [Fact]
    public void ValidateOnlyAnswersWithoutWriting()
    {
        McpHarness h = McpHarness.Create();
        (string decision, string record) = SubmittedRetirement(h, McpHarness.TokenOwner, McpHarness.Travel);
        string before = Dump(h);

        JsonTreeNode envelope = h.Ok(McpHarness.TokenOwnerWeb, "answer_approval", Answer(record, "approve", extra: ",\"validateOnly\":true"), server: h.PersonServer());

        Assert.Equal("accepted", McpHarness.Text(McpHarness.Data(envelope), "decisionStatus.meaning"));
        Assert.Contains("Nothing was written", McpHarness.Text(envelope, "summary"));
        Assert.Equal(before, Dump(h));
        Assert.Equal("proposed", h.Store.Get("decision", decision)!.Text("statusMeaning"));
    }

    // ---- Helpers ----

    private static (string Decision, string Record) SubmittedRetirement(McpHarness h, string token, string applicationId)
    {
        JsonTreeNode proposed = h.Ok(token, "save_decision_proposal", Proposal("retirement", applicationId, "complete"));
        string decision = McpHarness.Text(McpHarness.Data(proposed), "recordId");
        h.Ok(token, "submit_record", "{\"subjectKind\":\"decision\",\"subjectId\":\"" + decision + "\"}");
        return (decision, Assert.Single(Records(h, decision)).Id);
    }

    private static IReadOnlyList<StoredRow> Records(McpHarness h, string decision)
    {
        return h.Store.Query(RowQuery.For("approval-record").Where("decision", decision));
    }

    private static string Answer(string record, string answer, string? reason = null, string extra = "")
    {
        return "{\"approvalRecordId\":\"" + record + "\",\"answer\":\"" + answer + "\"" + (reason == null ? string.Empty : ",\"reason\":\"" + reason + "\"") + extra + "}";
    }

    private static string Proposal(string type, string applicationId, string outcome)
    {
        return "{\"decisionTypeKey\":\"" + type + "\",\"subjectKind\":\"business-application\",\"subjectId\":\"" + applicationId + "\",\"statement\":\"Fixture " + type
            + " decision\",\"context\":\"Fixture context\",\"proposedOutcomeKey\":\"" + outcome + "\",\"reason\":\"Fixture reason\"}";
    }

    private static List<string> ToolNames(McpResponse response)
    {
        Assert.Equal(200, response.HttpStatus);
        return Js.Items(response.Result, "tools").Select(t => Js.Text(t, "name")!).ToList();
    }

    // Every committed row, as canonical text.
    private static string Dump(McpHarness h)
    {
        return h.Store.Dump();
    }

    // A dump without its audit entries, to compare with DumpWithout("audit-entry").
    private static string StripAudit(string dump)
    {
        return string.Join("\n", dump.Split('\n').Where(line => !line.StartsWith("audit-entry ", System.StringComparison.Ordinal)));
    }

    private static void SeedTier3(McpHarness h)
    {
        h.Store.Seed("business-application", "{\"name\":\"Fixture claims triage\",\"recordNumber\":\"APP-0007\",\"purpose\":\"Triage insurance claims\",\"applicationKind\":\"power-platform-app\","
            + "\"portfolio\":\"" + McpHarness.Portfolio + "\",\"status\":\"app-registered\",\"statusMeaning\":\"registered\",\"tier\":\"tier-3\",\"lifecycleStage\":\"build\","
            + "\"stageSince\":\"2026-09-01T00:00:00Z\",\"configurationSetVersion\":1}", id: Tier3App);
        h.Store.Seed("owner-assignment", "{\"businessApplication\":\"" + Tier3App + "\",\"ownerRole\":\"business-owner\",\"principal\":\"" + McpHarness.Owner + "\",\"isDeputy\":false,\"startOn\":\"2026-09-01\"}",
            id: "00000000-0000-4000-8000-000000000571");
        h.Store.Seed("owner-assignment", "{\"businessApplication\":\"" + Tier3App + "\",\"ownerRole\":\"business-owner\",\"principal\":\"" + McpHarness.Deputy + "\",\"isDeputy\":true,\"deputyFor\":\"00000000-0000-4000-8000-000000000571\",\"startOn\":\"2026-09-01\"}",
            id: "00000000-0000-4000-8000-000000000572");
    }
}
