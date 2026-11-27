using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Json;
using Product.Mcp.Core.Protocol;
using Xunit;

namespace Product.Mcp.Core.Tests;

// Completion and record fixes (document 24, B24-10; document 23, B23-10): a person completes a review once its outcome
// decision is accepted and closes an authorized change with its result; the post-change review follows the change type;
// the AI-only code approval flag is stored; claims carry their link, a new application's claim starts an intake, and a
// claim's ownership decision is submitted; My work lists reviews and documents by role.
public sealed class ReviewChangeClaimToolTests
{
    private const string AcceptedDesign = "00000000-0000-4000-8000-000000000621";
    private const string ProposedDesign = "00000000-0000-4000-8000-000000000622";

    // ---- complete_review and get_review ----

    // The technical owner completes the architecture review of their application on its page once the design decision is
    // accepted: the review is done, they are its reviewer, the evidence is on it, and the Design gate it feeds through its
    // outcome decision is met. Before that, a proposed outcome holds it, and get_review says why.
    [Fact]
    public void AReviewIsCompletedOnceItsOutcomeIsAccepted()
    {
        McpHarness h = McpHarness.Create();
        string review = Scenarios.ArchitectureReview(h);
        McpServerCore web = h.PersonServer();

        JsonTreeNode noOutcome = h.Fails(McpHarness.TokenOwnerWeb, "complete_review", "{\"reviewId\":\"" + review + "\"}", McpErrorCodes.RuleViolation, server: web);
        Assert.Contains("no outcome decision", McpHarness.Text(noOutcome, "error.message"));

        SeedDesign(h, ProposedDesign, "decision-proposed", "proposed");
        h.Store.Seed("review", Js.With(h.Store.Get("review", review)!.Columns, ("outcomeDecision", JsonTreeNode.FromString(ProposedDesign))), id: review);
        JsonTreeNode waiting = McpHarness.Data(h.Ok(McpHarness.TokenOwnerWeb, "get_review", "{\"reviewId\":\"" + review + "\"}", server: web));
        Assert.Equal("false", McpHarness.Text(waiting, "canComplete"));
        Assert.Contains("proposed", McpHarness.Text(waiting, "cannotComplete"));
        Assert.Equal("lead", McpHarness.Text(waiting, "yourPart"));
        h.Fails(McpHarness.TokenOwnerWeb, "complete_review", "{\"reviewId\":\"" + review + "\"}", McpErrorCodes.RuleViolation, server: web);

        SeedDesign(h, AcceptedDesign, "decision-accepted", "accepted");
        JsonTreeNode done = McpHarness.Data(h.Ok(McpHarness.TokenOwnerWeb, "complete_review", "{\"reviewId\":\"" + review + "\",\"outcomeDecisionId\":\"" + AcceptedDesign
            + "\",\"evidence\":[{\"linkKindKey\":\"evidence-url\",\"url\":\"https://example.org/evidence/adr-2\",\"label\":\"ADR 2: ledger calls\"}]}", server: web));
        Assert.Equal("done", McpHarness.Text(done, "status.meaning"));
        Assert.Equal("person", McpHarness.Text(done, "performedBy.actorKind"));
        Assert.Equal(McpHarness.Owner, McpHarness.Text(done, "reviewer.objectId"));
        Assert.Equal("2026-11-22", McpHarness.Text(done, "doneOn"));
        Assert.Equal("null", McpHarness.Text(done, "nextDueOn"));
        JsonTreeNode design = Assert.Single(Js.Items(done, "gates"), g => McpHarness.Text(g, "gateKey") == "design");
        Assert.Equal("gate-decision", McpHarness.Text(design, "feedsBy"));
        Assert.Equal("true", McpHarness.Text(design, "met"));

        StoredRow row = h.Store.Get("review", review)!;
        Assert.Equal("done", row.Text("statusMeaning"));
        Assert.Equal(AcceptedDesign, row.Text("outcomeDecision"));
        Assert.Equal("person", row.Text("performedByActorKind"));

        JsonTreeNode read = McpHarness.Data(h.Ok(McpHarness.TokenReader, "get_review", "{\"reviewId\":\"" + review + "\"}"));
        Assert.Equal("done", McpHarness.Text(read, "review.status.meaning"));
        Assert.Equal("ADR 2: ledger calls", McpHarness.Text(Assert.Single(Js.Items(read, "evidence")), "label"));
        Assert.Equal(AcceptedDesign, McpHarness.Text(read, "outcomeDecision.decisionId"));
        Assert.Equal("false", McpHarness.Text(read, "canComplete"));

        // Done once: it is not completed again.
        h.Fails(McpHarness.TokenOwnerWeb, "complete_review", "{\"reviewId\":\"" + review + "\"}", McpErrorCodes.RuleViolation, server: web);
    }

    // An owner check of a tier 1 application gets its next due date from the cadence (owner-check-tier-1, 365 days) when
    // the business owner, its lead owner role, completes it.
    [Fact]
    public void AnOwnerCheckGetsItsNextDueDateFromTheCadence()
    {
        McpHarness h = McpHarness.Create();
        h.Store.Seed("decision", "{\"decisionType\":\"ownership\",\"subjectKind\":\"business-application\",\"subjectApplication\":\"" + McpHarness.Expense
            + "\",\"statement\":\"Keep the owners of the expense app\",\"outcome\":\"assign\",\"status\":\"decision-accepted\",\"statusMeaning\":\"accepted\",\"decidedOn\":\"2026-11-20\"}", id: AcceptedDesign);
        const string review = "00000000-0000-4000-8000-000000000623";
        h.Store.Seed("review", "{\"businessApplication\":\"" + McpHarness.Expense + "\",\"reviewType\":\"owner-check\",\"status\":\"review-due\",\"statusMeaning\":\"due\",\"trigger\":\"cadence\",\"dueOn\":\"2026-11-30\",\"outcomeDecision\":\"" + AcceptedDesign + "\"}", id: review);

        JsonTreeNode before = McpHarness.Data(h.Ok(McpHarness.TokenOwnerWeb, "get_review", "{\"reviewId\":\"" + review + "\"}", server: h.PersonServer()));
        Assert.Equal("owner-check-tier-1", McpHarness.Text(before, "cadence.cadenceKey"));
        Assert.Equal("2027-11-22", McpHarness.Text(before, "nextDueIfDoneToday"));

        JsonTreeNode done = McpHarness.Data(h.Ok(McpHarness.TokenOwnerWeb, "complete_review", "{\"reviewId\":\"" + review + "\",\"doneOn\":\"2026-11-21\"}", server: h.PersonServer()));
        Assert.Equal("2027-11-21", McpHarness.Text(done, "nextDueOn"));
        Assert.Equal("2027-11-21", h.Store.Get("review", review)!.Text("nextDueOn"));
    }

    // A person whose review it is not is refused, and an AI client is never served the tool.
    [Fact]
    public void OnlyThePersonWhoseReviewItIsCompletesIt()
    {
        McpHarness h = McpHarness.Create();
        string review = Scenarios.ReviewWithAcceptedOutcome(h);
        h.Fails(McpHarness.TokenReaderWeb, "complete_review", "{\"reviewId\":\"" + review + "\"}", McpErrorCodes.NotAuthorized, server: h.PersonServer());
        h.Fails(McpHarness.TokenOwner, "complete_review", "{\"reviewId\":\"" + review + "\"}", McpErrorCodes.AiCannotDoThis);
        Assert.Equal("due", h.Store.Get("review", review)!.Text("statusMeaning"));
    }

    // ---- complete_change, record_change_result ----

    // An authorized change closes with its result and leaves the open part of the schedule; a succeeded normal change
    // needs no post-change review. A change still waiting for its authorization does not close.
    [Fact]
    public void AnAuthorizedChangeClosesWithItsResult()
    {
        McpHarness h = McpHarness.Create();
        McpServerCore web = h.PersonServer();
        string change = Scenarios.ScheduledChange(h);
        Assert.Equal("true", McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenReader, "get_change_request", "{\"changeRequestId\":\"" + change + "\"}")), "closable"));

        JsonTreeNode closed = McpHarness.Data(h.Ok(McpHarness.TokenOwnerWeb, "complete_change", "{\"changeRequestId\":\"" + change + "\",\"resultKey\":\"succeeded\"}", server: web));
        Assert.Equal("completed", McpHarness.Text(closed, "status.meaning"));
        Assert.Equal("Closed", McpHarness.Text(closed, "status.label"));
        Assert.Equal("succeeded", McpHarness.Text(closed, "result.key"));
        Assert.Equal("null", McpHarness.Text(closed, "postChangeReviewId"));
        JsonTreeNode schedule = McpHarness.Data(h.Ok(McpHarness.TokenReader, "get_change_schedule", "{\"from\":\"2026-11-21\",\"to\":\"2026-11-21\",\"statusMeanings\":[\"in-progress\"]}"));
        Assert.Empty(Js.Items(schedule, "items"));
        h.Fails(McpHarness.TokenOwner, "record_change_result", "{\"changeRequestId\":\"" + change + "\",\"resultKey\":\"failed\"}", McpErrorCodes.RuleViolation);

        string waiting = Scenarios.Change(h);
        h.Ok(McpHarness.TokenOwner, "submit_record", "{\"subjectKind\":\"change-request\",\"subjectId\":\"" + waiting + "\"}");
        Assert.Equal("false", McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenReader, "get_change_request", "{\"changeRequestId\":\"" + waiting + "\"}")), "closable"));
        JsonTreeNode refused = h.Fails(McpHarness.TokenOwnerWeb, "complete_change", "{\"changeRequestId\":\"" + waiting + "\",\"resultKey\":\"succeeded\"}", McpErrorCodes.RuleViolation, server: web);
        Assert.Contains("authorization is not accepted yet", McpHarness.Text(refused, "error.message"));
    }

    // An emergency (Requires Review After, Review Due Days 5) opens its post-change review on any result, due five working
    // days later (from Sunday 2026-11-22: Friday 2026-11-27), once: closing it afterwards opens no second review. A failed
    // normal change opens one due in 14 days, since its type sets no Review Due Days.
    [Fact]
    public void ThePostChangeReviewFollowsTheChangeType()
    {
        McpHarness h = McpHarness.Create();
        const string emergency = "00000000-0000-4000-8000-000000000624";
        h.Store.Seed("change-request", "{\"title\":\"Restore the approval flow\",\"recordNumber\":\"CHG-0042\",\"businessApplication\":\"" + McpHarness.Expense + "\",\"changeType\":\"emergency\",\"description\":\"The flow stopped\","
            + "\"mode\":\"built-in\",\"windowStart\":\"2026-11-21T06:00:00Z\",\"windowEnd\":\"2026-11-21T08:00:00Z\",\"status\":\"change-submitted\",\"statusMeaning\":\"submitted\"}", id: emergency);
        JsonTreeNode reported = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "record_change_result", "{\"changeRequestId\":\"" + emergency + "\",\"resultKey\":\"succeeded\"}"));
        string review = McpHarness.Text(reported, "postChangeReviewId");
        StoredRow opened = h.Store.Get("review", review)!;
        Assert.Equal("post-change", opened.Text("reviewType"));
        Assert.Equal("2026-11-27", opened.Text("dueOn"));
        Assert.Contains("CHG-0042", opened.Text("description"));

        // An emergency may close before its authorization follows (Allows Authorization After).
        JsonTreeNode closed = McpHarness.Data(h.Ok(McpHarness.TokenOwnerWeb, "complete_change", "{\"changeRequestId\":\"" + emergency + "\"}", server: h.PersonServer()));
        Assert.Equal("completed", McpHarness.Text(closed, "status.meaning"));
        Assert.Equal("null", McpHarness.Text(closed, "postChangeReviewId"));
        JsonTreeNode read = McpHarness.Data(h.Ok(McpHarness.TokenReader, "get_change_request", "{\"changeRequestId\":\"" + emergency + "\"}"));
        Assert.Equal(review, McpHarness.Text(Assert.Single(Js.Items(read, "postChangeReviews")), "reviewId"));

        string normal = Scenarios.ScheduledChange(h);
        JsonTreeNode failed = McpHarness.Data(h.Ok(McpHarness.TokenOwnerWeb, "complete_change", "{\"changeRequestId\":\"" + normal + "\",\"resultKey\":\"failed\"}", server: h.PersonServer()));
        Assert.Equal("2026-12-06", h.Store.Get("review", McpHarness.Text(failed, "postChangeReviewId"))!.Text("dueOn"));
    }

    // A change authorization recorded as accepted outside the engine (the seeded demo estate) holds no approval request.
    // Its reads say so (settledBy recorded) and list nothing as missing, instead of evaluating the rules again.
    [Fact]
    public void ASeededAuthorizationReadsAsRecordedNotMissing()
    {
        McpHarness h = McpHarness.Create();
        const string decision = "00000000-0000-4000-8000-000000000626";
        const string change = "00000000-0000-4000-8000-000000000627";
        h.Store.Seed("decision", "{\"decisionType\":\"change-authorization\",\"subjectKind\":\"change-request\",\"subjectChangeRequest\":\"" + change + "\",\"subjectApplication\":\"" + McpHarness.Expense
            + "\",\"statement\":\"Authorize the change: Add an approval step\",\"outcome\":\"authorize\",\"status\":\"decision-accepted\",\"statusMeaning\":\"accepted\",\"decidedOn\":\"2026-11-10\"}", id: decision);
        h.Store.Seed("change-request", "{\"title\":\"Add an approval step\",\"businessApplication\":\"" + McpHarness.Expense + "\",\"changeType\":\"normal\",\"description\":\"Adds a manager step\",\"mode\":\"built-in\","
            + "\"windowStart\":\"2026-11-21T06:00:00Z\",\"windowEnd\":\"2026-11-21T08:00:00Z\",\"status\":\"change-in-progress\",\"statusMeaning\":\"in-progress\",\"authorizationDecision\":\"" + decision + "\"}", id: change);

        JsonTreeNode status = h.Ok(McpHarness.TokenReader, "get_approval_status", "{\"subjectKind\":\"change-request\",\"subjectId\":\"" + change + "\"}");
        JsonTreeNode data = McpHarness.Data(status);
        Assert.Equal("recorded", McpHarness.Text(data, "settledBy"));
        Assert.Empty(Js.Items(data, "missing"));
        Assert.Equal("true", McpHarness.Text(data, "ready"));
        Assert.DoesNotContain("missing", McpHarness.Text(status, "summary"));
        Assert.Equal("recorded", McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenReader, "get_change_request", "{\"changeRequestId\":\"" + change + "\"}")), "authorization.settledBy"));
        Assert.Equal("recorded", McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenReader, "get_decision", "{\"decisionId\":\"" + decision + "\"}")), "approvals.settledBy"));
    }

    // ---- The AI-only code approval flag ----

    // The flag OP-04 computes is stored on the deployment record like the other flags, so the record and flaggedOnly show
    // it after the call, not only in the call's answer.
    [Fact]
    public void AnAiOnlyCodeApprovalIsStoredAndFlagged()
    {
        McpHarness h = McpHarness.Create();
        string arguments = Scenarios.DeploymentArguments("run-ai-1", McpHarness.Production).TrimEnd('}') + ",\"codeApprovalKind\":\"ai-only\"}";
        JsonTreeNode recorded = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "record_deployment", arguments));
        StoredRow record = h.Store.Get("deployment-record", McpHarness.Text(recorded, "deploymentRecordId"))!;
        Assert.True(record.Flag("isAiOnlyCodeApproval"));
        Assert.Equal("true", McpHarness.Text(recorded, "flags.aiOnlyCodeApproval"));

        JsonTreeNode flagged = McpHarness.Data(h.Ok(McpHarness.TokenReader, "get_deployments", "{\"flaggedOnly\":true}"));
        JsonTreeNode item = Assert.Single(Js.Items(flagged, "items"), i => McpHarness.Text(i, "deploymentRecordId") == record.Id);
        Assert.Equal("true", McpHarness.Text(item, "flags.aiOnlyCodeApproval"));
    }

    // ---- Claims ----

    // An item the admin's own list gives a link for keeps it on the claim, and on the component a claim for a new
    // application creates. That claim starts a draft intake request for the new draft application, naming the claimant
    // as business owner; no ownership decision is opened, because the intake's triage decides.
    [Fact]
    public void ANewApplicationsClaimStartsAnIntakeAndKeepsTheLink()
    {
        McpHarness h = McpHarness.Create();
        JsonTreeNode compared = McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "compare_inventory_list", "{\"estateKey\":\"power-platform-inventory\",\"items\":[{\"externalId\":\"00000000-0000-0000-0000-000000000104\","
            + "\"componentKindKey\":\"canvas-app\",\"sourceType\":\"microsoft.powerapps/canvasapps\",\"labelAsSeen\":\"Mileage log\",\"targetAsSeen\":\"Fixture production\",\"suggestedClaimantObjectId\":\"" + McpHarness.Owner + "\",\"url\":\"https://make.powerapps.com/environments/fixture/apps/104\"}],"
            + "\"sourceNote\":\"An export from the admin's own tool\"}"));
        string claim = McpHarness.Text(Js.Items(compared, "newClaims")[0], "claimId");
        Assert.Equal("https://make.powerapps.com/environments/fixture/apps/104", h.Store.Get("claim", claim)!.Text("url"));

        JsonTreeNode claimed = McpHarness.Data(h.Ok(McpHarness.TokenOwnerWeb, "claim_resource", "{\"claimId\":\"" + claim + "\",\"newApplication\":{\"name\":\"Fixture mileage log\",\"purpose\":\"Log business miles\",\"applicationKindKey\":\"power-platform-app\"}}", server: h.PersonServer()));
        Assert.Equal("true", McpHarness.Text(claimed, "applicationCreated"));
        Assert.Equal("null", McpHarness.Text(claimed, "ownershipDecisionId"));
        string app = McpHarness.Text(claimed, "businessApplicationId");
        StoredRow intake = h.Store.Get("intake-request", McpHarness.Text(claimed, "intakeRequestId"))!;
        Assert.Equal("draft", intake.Text("statusMeaning"));
        Assert.Equal(app, intake.Text("businessApplication"));
        Assert.Equal(McpHarness.Owner, intake.Text("requester"));
        Assert.Equal(McpHarness.Owner, intake.Text("businessOwner"));
        Assert.Contains("Mileage log", intake.Text("need"));
        StoredRow component = h.Store.Get("technical-component", McpHarness.Text(claimed, "technicalComponentId"))!;
        Assert.Equal("https://make.powerapps.com/environments/fixture/apps/104", component.Text("url"));
        Assert.Equal(app, component.Text("businessApplication"));

        JsonTreeNode listed = Assert.Single(Js.Items(McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "list_claims", "{\"statusMeanings\":[\"claimed\"]}")), "items"));
        Assert.Equal(intake.Id, McpHarness.Text(listed, "intakeRequestId"));
        Assert.Equal(app, McpHarness.Text(listed, "businessApplicationId"));
    }

    // A person who holds no owner role on the application claims an item for it: the ownership decision about the claim
    // is submitted at once, so its card asks the application's business owner. Through an AI client the same claim leaves
    // the decision proposed, for the person to submit.
    [Fact]
    public void AClaimsOwnershipDecisionIsSubmittedForAPerson()
    {
        McpHarness h = McpHarness.Create();
        string claim = Scenarios.Claim(h);
        JsonTreeNode claimed = McpHarness.Data(h.Ok(McpHarness.TokenAdminWeb, "claim_resource", "{\"claimId\":\"" + claim + "\",\"businessApplicationId\":\"" + McpHarness.Expense + "\"}", server: h.PersonServer()));
        Assert.Equal("true", McpHarness.Text(claimed, "decisionSubmitted"));
        string decision = McpHarness.Text(claimed, "ownershipDecisionId");
        Assert.NotEmpty(Js.Items(claimed, "approvalsRequested"));
        Assert.NotEmpty(h.Store.Query(RowQuery.For("approval-record").Where("decision", decision)));

        McpHarness ai = McpHarness.Create();
        string other = Scenarios.Claim(ai);
        JsonTreeNode envelope = ai.Ok(McpHarness.TokenAdmin, "claim_resource", "{\"claimId\":\"" + other + "\",\"businessApplicationId\":\"" + McpHarness.Expense + "\"}");
        JsonTreeNode data = McpHarness.Data(envelope);
        Assert.Equal("false", McpHarness.Text(data, "decisionSubmitted"));
        Assert.Empty(ai.Store.Query(RowQuery.For("approval-record").Where("decision", McpHarness.Text(data, "ownershipDecisionId"))));
        Assert.Contains(Js.Items(envelope, "warnings"), w => McpHarness.Text(w, "code") == "claim.decision-not-submitted");
        Assert.Equal("submit_record", McpHarness.Text(Assert.Single(Js.Items(envelope, "nextSteps")), "tool"));
    }

    // ---- My work by role ----

    // A missing document goes to the application's technical owner, the default document owner: once someone else holds
    // that role on the travel planner, its business owner no longer finds the missing document, and the technical owner
    // does. The review of an application goes to the holder of its review type's lead owner role in the same way.
    [Fact]
    public void DocumentsAndReviewsGoToTheirOwnersByRole()
    {
        McpHarness h = McpHarness.Create();
        bool Missing(string token) => Js.Items(McpHarness.Data(h.Ok(token, "get_my_queue", "{\"kinds\":[\"document\"]}")), "items")
            .Any(i => McpHarness.Text(i, "status.meaning") == "missing" && McpHarness.Text(i, "subjectId") == McpHarness.Travel);
        Assert.True(Missing(McpHarness.TokenOwner));
        Assert.False(Missing(McpHarness.TokenDeputy));

        h.Store.Seed("owner-assignment", "{\"businessApplication\":\"" + McpHarness.Travel + "\",\"ownerRole\":\"technical-owner\",\"principal\":\"" + McpHarness.Deputy + "\",\"isDeputy\":false,\"startOn\":\"2026-02-01\"}", id: "00000000-0000-4000-8000-000000000625");
        Assert.False(Missing(McpHarness.TokenOwner));
        Assert.True(Missing(McpHarness.TokenDeputy));

        h.Ok(McpHarness.TokenOwner, "request_architecture_review", "{\"businessApplicationId\":\"" + McpHarness.Travel + "\",\"reason\":\"A new booking API\",\"dueOn\":\"2026-11-30\"}");
        bool Review(string token) => Js.Items(McpHarness.Data(h.Ok(token, "get_my_queue", "{\"kinds\":[\"review\"]}")), "items").Any(i => McpHarness.Text(i, "businessApplicationId") == McpHarness.Travel);
        Assert.False(Review(McpHarness.TokenOwner));
        Assert.True(Review(McpHarness.TokenDeputy));
        Assert.True(Review(McpHarness.TokenArchitect));
    }

    private static void SeedDesign(McpHarness h, string id, string status, string meaning)
    {
        h.Store.Seed("decision", "{\"decisionType\":\"design\",\"subjectKind\":\"business-application\",\"subjectApplication\":\"" + McpHarness.Expense
            + "\",\"statement\":\"Call the ledger through its published API\",\"outcome\":\"approve\",\"status\":\"" + status + "\",\"statusMeaning\":\"" + meaning + "\"}", id: id);
    }
}
