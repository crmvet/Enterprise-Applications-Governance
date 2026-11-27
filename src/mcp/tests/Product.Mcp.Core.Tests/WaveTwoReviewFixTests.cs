using System;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Json;
using Product.Mcp.Core.Policy;
using Product.Mcp.Core.Protocol;
using Xunit;

namespace Product.Mcp.Core.Tests;

// The fixes of the wave 2 QA run, security review and product review (2026-11-24; document 24, B24-14; document 25,
// B25-15). Each case runs real tools through the server core against the in-memory store.
public sealed class WaveTwoReviewFixTests
{
    private const string Owner = McpHarness.TokenOwner;
    private const string Deputy = McpHarness.TokenDeputy;

    // ---- Who reads an intake request ----

    // The business owner a request names reads it, and a person with no part in it still does not (product review: the
    // people asked to answer a request's decisions were sent to a page that said "not found").
    [Fact]
    public void TheNamedBusinessOwnerReadsTheRequestAndAStrangerDoesNot()
    {
        McpHarness h = McpHarness.Create();
        string intake = Scenarios.Intake(h, linked: false, businessOwner: McpHarness.Reader);
        h.Ok(McpHarness.TokenReader, "get_intake_request", "{\"intakeRequestId\":\"" + intake + "\"}");
        h.Fails(Deputy, "get_intake_request", "{\"intakeRequestId\":\"" + intake + "\"}", McpErrorCodes.NotFound);
    }

    // A person asked by an approval record on a decision about the request reads the request and the decision: the
    // security reviewer's consultation on the triage decision.
    [Fact]
    public void APersonAskedOnTheRequestsDecisionReadsIt()
    {
        McpHarness h = McpHarness.Create();
        string intake = Scenarios.SubmittedIntake(h, linked: false);
        string triage = h.Store.Get("intake-request", intake)!.Text("triageDecision")!;
        h.Fails(Deputy, "get_decision", "{\"decisionId\":\"" + triage + "\"}", McpErrorCodes.NotFound);
        h.Ok(McpHarness.TokenAdmin, "get_decision", "{\"decisionId\":\"" + triage + "\"}");
        h.Ok(McpHarness.TokenAdmin, "get_intake_request", "{\"intakeRequestId\":\"" + intake + "\"}");
    }

    // ---- Linking a request to an application, and the named business owner ----

    // A request is linked only to an application the requester owns (security review, blocker 1). The deputy owns the
    // expense app as deputy business owner, and not the travel planner.
    [Fact]
    public void ARequestIsLinkedOnlyToAnApplicationTheRequesterOwns()
    {
        McpHarness h = McpHarness.Create();
        h.Fails(Deputy, "save_intake_draft", "{\"title\":\"Planner change\",\"need\":\"A new report\",\"businessApplicationId\":\"" + McpHarness.Travel + "\"}", McpErrorCodes.NotAuthorized);
        Assert.Empty(h.Store.Rows("intake-request"));
        h.Ok(Deputy, "save_intake_draft", "{\"title\":\"Expense change\",\"need\":\"A new report\",\"businessApplicationId\":\"" + McpHarness.Expense + "\"}");
    }

    // The requester never names themselves business owner (security review, major 1).
    [Fact]
    public void TheRequesterNeverNamesThemselvesBusinessOwner()
    {
        McpHarness h = McpHarness.Create();
        h.Fails(Owner, "save_intake_draft", "{\"title\":\"Own tool\",\"need\":\"Something\",\"businessOwnerObjectId\":\"" + McpHarness.Owner + "\"}", McpErrorCodes.Validation);
        Assert.Empty(h.Store.Rows("intake-request"));
    }

    // A submitted request keeps its tier answers (security review, major 5), so its tier never moves under its approvers.
    [Fact]
    public void ASubmittedRequestKeepsItsTierAnswers()
    {
        McpHarness h = McpHarness.Create();
        string intake = Scenarios.SubmittedIntake(h, linked: false);
        int before = h.Store.Rows("tier-assessment").Count;
        h.Fails(Owner, "answer_tier_questions", "{\"intakeRequestId\":\"" + intake + "\",\"answers\":" + Scenarios.TierAnswers + "}", McpErrorCodes.RuleViolation);
        Assert.Equal(before, h.Store.Rows("tier-assessment").Count);
    }

    // A decision that changes the application itself is about the application, never about a request (security review,
    // blocker 2): a tier decision about a request is invalid input, and nothing is written.
    [Fact]
    public void ATierDecisionAboutARequestIsRefused()
    {
        McpHarness h = McpHarness.Create();
        string intake = Scenarios.Intake(h);
        int decisions = h.Store.Rows("decision").Count;
        h.Fails(McpHarness.TokenAdmin, "save_decision_proposal", "{\"decisionTypeKey\":\"tier\",\"subjectKind\":\"intake-request\",\"subjectId\":\"" + intake
            + "\",\"statement\":\"Lower it\",\"context\":\"A test\",\"proposedOutcomeKey\":\"lower\",\"reason\":\"Low risk\",\"tierKey\":\"tier-1\"}", McpErrorCodes.Validation);
        Assert.Equal(decisions, h.Store.Rows("decision").Count);
    }

    // ---- find_records and the AI activity hide what a person may not read ----

    // A refused call that named a request the caller may not read never shows that request later (security review,
    // minor 1).
    [Fact]
    public void TheAiActivityNeverShowsARequestTheCallerMayNotRead()
    {
        McpHarness h = McpHarness.Create();
        string intake = Scenarios.Intake(h, title: "Secret layoffs planner", linked: false);
        h.Fails(Deputy, "save_intake_draft", "{\"intakeRequestId\":\"" + intake + "\",\"title\":\"x\",\"need\":\"y\"}", McpErrorCodes.NotAuthorized);
        JsonTreeNode activity = h.Ok(Deputy, "get_ai_activity", "{\"view\":\"refused\"}");
        Assert.DoesNotContain("Secret layoffs planner", Js.Write(activity), StringComparison.Ordinal);
    }

    // An AI client that loops on refused calls fills the audit ledger only up to the hourly budget (security review,
    // minor 2). Every call is still answered.
    [Fact]
    public void RefusedCallsFillTheLedgerOnlyUpToTheHourlyBudget()
    {
        McpHarness h = McpHarness.Create();
        for (int i = 0; i < RateLimiter.RefusalsRecordedPerHour + 5; i++)
        {
            h.Fails(Owner, "approve_everything", "{}", McpErrorCodes.NotFound);
        }

        Assert.Equal(RateLimiter.RefusalsRecordedPerHour, h.Store.Rows("audit-entry").Count(a => a.Text("operation") == "call-refused"));
    }

    // ---- Score and expected benefit are restricted reads ----

    [Fact]
    public void OnlyTheDemandReadersAndTheRequesterReadTheScore()
    {
        McpHarness h = McpHarness.Create();
        string intake = Scenarios.Intake(h, linked: false);
        h.Ok(Owner, "save_intake_draft", "{\"intakeRequestId\":\"" + intake + "\",\"title\":\"Fixture travel requests\",\"need\":\"Replace the shared spreadsheet\",\"expectedBenefit\":\"Two hours a week\"}");
        JsonTreeNode own = McpHarness.Data(h.Ok(Owner, "get_intake_request", "{\"intakeRequestId\":\"" + intake + "\"}"));
        Assert.Equal("Two hours a week", McpHarness.Text(own, "intake.expectedBenefit"));
        JsonTreeNode architect = McpHarness.Data(h.Ok(McpHarness.TokenArchitect, "get_intake_request", "{\"intakeRequestId\":\"" + intake + "\"}"));
        Assert.Equal("null", McpHarness.Text(architect, "intake.expectedBenefit"));
    }

    // ---- The duplicate check ----

    // A long request that names an application once still finds it (product review: the wizard sends the title and the
    // whole need, and the reuse step found nothing).
    [Fact]
    public void ALongRequestStillFindsTheApplicationItNames()
    {
        McpHarness h = McpHarness.Create();
        const string text = "Travel planner for the field team. Today people plan trips in a shared spreadsheet, copy the bookings by hand, and nobody can see who is away on which day or what it costs the department each month.";
        JsonTreeNode data = McpHarness.Data(h.Ok(Owner, "check_duplicates", "{\"text\":\"" + text + "\"}"));
        Assert.Contains(Js.Items(data, "matches"), m => McpHarness.Text(m, "businessApplicationId") == McpHarness.Travel);
    }

    // ---- Words people read on a decision ----

    // An owner change says what it does in words: no JSON and no object IDs in its statement or context (product review).
    [Fact]
    public void AnOwnerChangeIsWrittenInWords()
    {
        McpHarness h = McpHarness.Create();
        JsonTreeNode data = McpHarness.Data(h.Ok(Owner, "propose_owner_change", "{\"businessApplicationId\":\"" + McpHarness.Travel
            + "\",\"changes\":[{\"ownerRoleKey\":\"business-owner\",\"addObjectId\":\"" + McpHarness.Deputy + "\",\"isDeputy\":true}],\"reason\":\"Cover for leave\"}"));
        StoredRow decision = h.Store.Get("decision", McpHarness.Text(data, "decisionId"))!;
        Assert.DoesNotContain("{", decision.Text("context")!, StringComparison.Ordinal);
        Assert.DoesNotContain(McpHarness.Deputy, decision.Text("statement")! + decision.Text("context"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("deputy business owner", decision.Text("statement")!, StringComparison.Ordinal);
    }
}
