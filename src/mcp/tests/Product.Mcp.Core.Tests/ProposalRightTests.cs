using Product.Engine.Core.Canonical;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Protocol;
using Xunit;

namespace Product.Mcp.Core.Tests;

// Who may propose a decision (document 24, tool 17; Tool Policy row save-decision-proposal; B24-5). The Tool Policy lets
// 11 roles call save_decision_proposal; the engine then checks the person behind the call holds a responsible,
// accountable or consulted Decision Right (C31) for the decision type in the decision's scope, through a product role or
// an owner role on the subject's application. The same check runs for an AI client on the MCP endpoint and for the
// person on the web app's entry. A refusal is not_authorized and writes nothing.
public sealed class ProposalRightTests
{
    // A third and a fourth application, owned by the deputy and not by the owner: tier 3 and tier 1.
    private const string Tier3App = "00000000-0000-4000-8000-000000000513";
    private const string Tier1App = "00000000-0000-4000-8000-000000000514";

    [Fact]
    public void TheAccountableBusinessOwnerMayProposeARiskAcceptance()
    {
        McpHarness h = McpHarness.Create();

        JsonTreeNode data = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "save_decision_proposal", Proposal("risk-acceptance", McpHarness.Travel, "accept")));

        Assert.Equal("proposed", McpHarness.Text(data, "status.meaning"));
        Assert.Equal("business-owner", McpHarness.Text(data, "decider.accountableOwnerRoleKey"));
        Assert.Equal("risk-acceptance", h.Store.Get("decision", McpHarness.Text(data, "recordId"))!.Text("decisionType"));
    }

    // The architect role passes the Tool Policy row, but holds no letter for a risk acceptance in Balanced: the engine
    // refuses, says who may propose, and nothing is written.
    [Fact]
    public void ARoleWithoutALetterIsRefusedAndNothingIsWritten()
    {
        McpHarness h = McpHarness.Create();

        JsonTreeNode envelope = h.Fails(McpHarness.TokenArchitect, "save_decision_proposal", Proposal("risk-acceptance", McpHarness.Travel, "accept"), McpErrorCodes.NotAuthorized);

        string message = McpHarness.Text(envelope, "error.message");
        Assert.Contains("risk-acceptance", message);
        Assert.Contains("business-owner", message);
        Assert.Empty(h.Store.Query(RowQuery.For("decision")));
        Assert.Empty(h.Store.Query(RowQuery.For("evidence-link")));

        // validateOnly runs every rule, so it is refused too.
        h.Fails(McpHarness.TokenArchitect, "save_decision_proposal", Proposal("risk-acceptance", McpHarness.Travel, "accept", ",\"validateOnly\":true"), McpErrorCodes.NotAuthorized);
        Assert.Empty(h.Store.Query(RowQuery.For("decision")));
    }

    // A role letter counts on every application: the platform admin is accountable for tiers and responsible for
    // retirement; the architect is accountable for designs and exceptions.
    [Fact]
    public void AProductRoleHoldingALetterMayProposeOnAnyApplication()
    {
        McpHarness h = McpHarness.Create();

        h.Ok(McpHarness.TokenAdmin, "save_decision_proposal", Proposal("tier", McpHarness.Travel, "confirm"));
        h.Ok(McpHarness.TokenAdmin, "save_decision_proposal", Proposal("retirement", McpHarness.Expense, "complete"));
        h.Ok(McpHarness.TokenArchitect, "save_decision_proposal", Proposal("design", McpHarness.Travel, "approve"));
        h.Ok(McpHarness.TokenArchitect, "save_decision_proposal", Proposal("exception", McpHarness.Expense, "grant", ",\"exception\":{\"standard\":\"Fixture standard\",\"reviewOn\":\"2027-01-15\"}"));

        // The owner holds requester, maker, application-owner and change-manager: none holds the tier letter.
        h.Fails(McpHarness.TokenOwner, "save_decision_proposal", Proposal("tier", McpHarness.Travel, "confirm"), McpErrorCodes.NotAuthorized);
        Assert.Equal(4, h.Store.Query(RowQuery.For("decision")).Count);
    }

    // An owner role counts only on the application where the person holds it now. A deputy holds it too, as for "own
    // apps" (document 26, section 5.3).
    [Fact]
    public void AnOwnerRoleCountsOnlyOnItsOwnApplication()
    {
        McpHarness h = McpHarness.Create();
        SeedApplications(h);

        h.Fails(McpHarness.TokenOwner, "save_decision_proposal", Proposal("risk-acceptance", Tier1App, "accept"), McpErrorCodes.NotAuthorized);
        h.Ok(McpHarness.TokenDeputy, "save_decision_proposal", Proposal("risk-acceptance", Tier1App, "accept"));
        h.Ok(McpHarness.TokenDeputy, "save_decision_proposal", Proposal("risk-acceptance", McpHarness.Expense, "accept"));
        h.Fails(McpHarness.TokenDeputy, "save_decision_proposal", Proposal("risk-acceptance", McpHarness.Travel, "accept"), McpErrorCodes.NotAuthorized);
    }

    // The scope comes from the subject's application, and the most specific Decision Right group wins: at tier 3 the
    // change manager is accountable for change authorization, at tier 1 only the business owner holds a letter. The
    // decider in the answer is the accountable row of the same group.
    [Fact]
    public void TheSubjectsTierPicksTheDecisionRightGroup()
    {
        McpHarness h = McpHarness.Create();
        SeedApplications(h);

        JsonTreeNode tier3 = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "save_decision_proposal", Proposal("change-authorization", Tier3App, "authorize")));
        Assert.Equal("change-manager", McpHarness.Text(tier3, "decider.accountableRoleKey"));
        h.Fails(McpHarness.TokenOwner, "save_decision_proposal", Proposal("change-authorization", Tier1App, "authorize"), McpErrorCodes.NotAuthorized);
        h.Ok(McpHarness.TokenDeputy, "save_decision_proposal", Proposal("change-authorization", Tier3App, "authorize"));

        // Below tier 3 the business owner decides: the emergency row (scoped to a change type) does not apply to a
        // decision with no change type.
        JsonTreeNode tier1 = McpHarness.Data(h.Ok(McpHarness.TokenDeputy, "save_decision_proposal", Proposal("change-authorization", Tier1App, "authorize")));
        Assert.Equal("business-owner", McpHarness.Text(tier1, "decider.accountableOwnerRoleKey"));
        Assert.Equal(string.Empty, McpHarness.Text(tier1, "decider.accountableRoleKey"));
    }

    // An edit is checked again: a proposal cannot be turned into a type its proposer holds no letter for.
    [Fact]
    public void AnEditThatChangesTheTypeIsCheckedAgain()
    {
        McpHarness h = McpHarness.Create();
        string decision = McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenOwner, "save_decision_proposal", Proposal("risk-acceptance", McpHarness.Travel, "accept"))), "recordId");

        h.Fails(McpHarness.TokenOwner, "save_decision_proposal", Proposal("tier", McpHarness.Travel, "confirm", ",\"decisionId\":\"" + decision + "\""), McpErrorCodes.NotAuthorized);

        Assert.Equal("risk-acceptance", h.Store.Get("decision", decision)!.Text("decisionType"));
    }

    // The person entry (the product's own web app) runs the same check: the person decides nothing more than an AI client
    // acting for them may propose.
    [Fact]
    public void ThePersonEntryRunsTheSameCheck()
    {
        McpHarness h = McpHarness.Create();
        McpServerCore web = h.PersonServer();

        JsonTreeNode refused = h.Fails(McpHarness.TokenOwnerWeb, "save_decision_proposal", Proposal("tier", McpHarness.Travel, "confirm"), McpErrorCodes.NotAuthorized, server: web);
        Assert.Contains("platform-admin", McpHarness.Text(refused, "error.message"));
        Assert.Empty(h.Store.Query(RowQuery.For("decision")));

        JsonTreeNode proposed = McpHarness.Data(h.Ok(McpHarness.TokenOwnerWeb, "save_decision_proposal", Proposal("go-live", McpHarness.Travel, "go"), server: web));
        Assert.Equal("person", McpHarness.Text(proposed, "performedBy.actorKind"));
        Assert.Single(h.Store.Query(RowQuery.For("decision")));
    }

    private static string Proposal(string type, string applicationId, string outcome, string extra = "")
    {
        return "{\"decisionTypeKey\":\"" + type + "\",\"subjectKind\":\"business-application\",\"subjectId\":\"" + applicationId + "\",\"statement\":\"Fixture " + type
            + " decision\",\"context\":\"Fixture context\",\"proposedOutcomeKey\":\"" + outcome + "\",\"reason\":\"Fixture reason\"" + extra + "}";
    }

    private static void SeedApplications(McpHarness h)
    {
        h.Store.Seed("business-application", "{\"name\":\"Fixture claims portal\",\"recordNumber\":\"APP-0003\",\"purpose\":\"Take insurance claims from customers\",\"applicationKind\":\"power-platform-app\","
            + "\"portfolio\":\"" + McpHarness.Portfolio + "\",\"status\":\"app-registered\",\"statusMeaning\":\"registered\",\"tier\":\"tier-3\",\"lifecycleStage\":\"operate\","
            + "\"stageSince\":\"2026-03-01T00:00:00Z\",\"configurationSetVersion\":1}", id: Tier3App);
        h.Store.Seed("business-application", "{\"name\":\"Fixture room booking\",\"recordNumber\":\"APP-0004\",\"purpose\":\"Book meeting rooms\",\"applicationKind\":\"power-platform-app\","
            + "\"portfolio\":\"" + McpHarness.Portfolio + "\",\"status\":\"app-registered\",\"statusMeaning\":\"registered\",\"tier\":\"tier-1\",\"lifecycleStage\":\"operate\","
            + "\"stageSince\":\"2026-03-01T00:00:00Z\",\"configurationSetVersion\":1}", id: Tier1App);
        h.Store.Seed("owner-assignment", "{\"businessApplication\":\"" + Tier3App + "\",\"ownerRole\":\"business-owner\",\"principal\":\"" + McpHarness.Deputy + "\",\"isDeputy\":false,\"startOn\":\"2026-03-01\"}",
            id: "00000000-0000-4000-8000-000000000555");
        h.Store.Seed("owner-assignment", "{\"businessApplication\":\"" + Tier1App + "\",\"ownerRole\":\"business-owner\",\"principal\":\"" + McpHarness.Deputy + "\",\"isDeputy\":false,\"startOn\":\"2026-03-01\"}",
            id: "00000000-0000-4000-8000-000000000556");
    }
}
