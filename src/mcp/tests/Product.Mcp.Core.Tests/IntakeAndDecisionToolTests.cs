using System;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Json;
using Product.Mcp.Core.Protocol;
using Xunit;

namespace Product.Mcp.Core.Tests;

// The intake, PMO and decision fixes through the tools (document 24, B24-9; document 25, B25-9 and B25-10): a business
// owner named on an intake, the PMO's demand fields and the demand list, people search, record numbers, the approval
// answer pre-check on list_approvals and get_decision, exception fields, and the proposed changes a decision page shows.
public sealed class IntakeAndDecisionToolTests
{
    private const string Tier3App = "00000000-0000-4000-8000-000000000517";

    // Fix round 1 (product review): "Check first" on a submit (validateOnly) says what would happen and lists who would be asked,
    // with no request IDs; the request stays a draft and nothing is stored. The real submit then asks exactly those. A
    // check of a record that is no longer a draft is refused, as the submit is.
    [Fact]
    public void ACheckedSubmitListsWhoWouldBeAskedAndLeavesTheDraftADraft()
    {
        McpHarness h = McpHarness.Create();
        string intake = Scenarios.Intake(h, linked: false);
        h.Ok(McpHarness.TokenOwner, "answer_tier_questions", "{\"intakeRequestId\":\"" + intake + "\",\"answers\":" + Scenarios.TierAnswers + "}");
        string before = h.Store.Dump();

        JsonTreeNode check = h.Ok(McpHarness.TokenOwner, "submit_record", "{\"subjectKind\":\"intake-request\",\"subjectId\":\"" + intake + "\",\"validateOnly\":true}");
        JsonTreeNode data = McpHarness.Data(check);
        Assert.Equal(before, h.Store.Dump());
        Assert.Equal("draft", McpHarness.Text(data, "status.meaning"));
        Assert.Equal("null", McpHarness.Text(data, "decisionId"));
        var wouldAsk = Js.Items(data, "approvalsRequested");
        Assert.NotEmpty(wouldAsk);
        Assert.All(wouldAsk, a => Assert.Equal("null", McpHarness.Text(a, "approvalRecordId")));
        string summary = McpHarness.Text(check, "summary");
        Assert.StartsWith("Would be submitted", summary, StringComparison.Ordinal);
        Assert.EndsWith("Nothing was stored (validateOnly).", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("Submitted.", summary, StringComparison.Ordinal);

        JsonTreeNode submitted = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "submit_record", "{\"subjectKind\":\"intake-request\",\"subjectId\":\"" + intake + "\"}"));
        Assert.Equal("submitted", McpHarness.Text(submitted, "status.meaning"));
        var asked = Js.Items(submitted, "approvalsRequested");
        Assert.Equal(
            wouldAsk.Select(a => McpHarness.Text(a, "requiredRoleKey") + "|" + McpHarness.Text(a, "requiredOwnerRoleKey") + "|" + McpHarness.Text(a, "isConsultation")).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            asked.Select(a => McpHarness.Text(a, "requiredRoleKey") + "|" + McpHarness.Text(a, "requiredOwnerRoleKey") + "|" + McpHarness.Text(a, "isConsultation")).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        Assert.All(asked, a => Assert.NotEqual("null", McpHarness.Text(a, "approvalRecordId")));

        h.Fails(McpHarness.TokenOwner, "submit_record", "{\"subjectKind\":\"intake-request\",\"subjectId\":\"" + intake + "\",\"validateOnly\":true}", McpErrorCodes.RuleViolation);
    }

    // Fix round 2: one link meets a documentation requirement of an application. A second new link for the same
    // requirement is refused with the link to update (it reached the store and answered HTTP 500 before); updating the
    // link by its documentLinkId passes, and a type with no requirement takes a new link.
    [Fact]
    public void ASecondLinkForARequirementIsRefusedWithTheLinkToUpdate()
    {
        McpHarness h = McpHarness.Create();
        // The refusal is recorded on the audit trail; no document link is written.
        static string Links(McpHarness harness) => string.Join("|", harness.Store.Dump().Split('\n').Where(l => l.StartsWith("document-link ", StringComparison.Ordinal)));
        string before = Links(h);
        JsonTreeNode refused = h.Fails(McpHarness.TokenOwner, "record_document_link", "{\"businessApplicationId\":\"" + McpHarness.Expense + "\",\"documentTypeKey\":\"wiki-page\",\"url\":\"https://devops.example.org/fixture/wiki/expense-2\",\"statusMeaning\":\"current\",\"lastUpdatedOn\":\"2026-11-01\"}", McpErrorCodes.RuleViolation);
        Assert.Contains("already has its wiki page linked", Js.Write(refused), StringComparison.Ordinal);
        Assert.Contains(McpHarness.WikiLink, Js.Write(refused), StringComparison.Ordinal);
        Assert.Equal(before, Links(h));

        JsonTreeNode updated = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "record_document_link", "{\"businessApplicationId\":\"" + McpHarness.Expense + "\",\"documentLinkId\":\"" + McpHarness.WikiLink + "\",\"documentTypeKey\":\"wiki-page\",\"url\":\"https://devops.example.org/fixture/wiki/expense-2\",\"statusMeaning\":\"current\",\"lastUpdatedOn\":\"2026-11-01\"}"));
        Assert.Equal(McpHarness.WikiLink, McpHarness.Text(updated, "documentLinkId"));

        // Fix round 2: a compared list names the items the gap check left out, instead of answering "0 items seen" alone.
        JsonTreeNode compared = h.Ok(McpHarness.TokenAdmin, "compare_inventory_list", "{\"estateKey\":\"power-platform-inventory\",\"items\":[{\"externalId\":\"no-kind-1\",\"labelAsSeen\":\"Depot keys reminder\"}],\"sourceNote\":\"A list with no kinds\"}");
        Assert.Contains(Js.Items(compared, "warnings"), w => Js.Text(w, "code") == "gap-check.items-left-out");
        Assert.StartsWith("0 items seen, 0 new claims, 0 vanished.", McpHarness.Text(compared, "summary"), StringComparison.Ordinal);
    }

    // A request for a new application, with its business owner named by the requester: the platform admin accepts the
    // triage card on the decision page, and the named person accepts the role on their own request (B25-18). Only then
    // does the Accept gate count the named owner, so the application is registered, and the engine makes the named person
    // its business owner. Without a named owner the gate holds the same request.
    [Fact]
    public void ANewAppIntakeWithANamedBusinessOwnerPassesTheAcceptGateAndAssignsTheOwner()
    {
        McpHarness h = McpHarness.Create();
        h.Fails(McpHarness.TokenOwner, "save_intake_draft", NewAppIntake("Unknown owner", "00000000-0000-4000-8000-0000000009ff"), McpErrorCodes.Validation);

        JsonTreeNode saved = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "save_intake_draft", NewAppIntake("Store visit planner", McpHarness.Deputy)));
        string intake = McpHarness.Text(saved, "recordId");
        JsonTreeNode draft = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "get_intake_request", "{\"intakeRequestId\":\"" + intake + "\"}"));
        Assert.Equal(McpHarness.Deputy, McpHarness.Text(draft, "intake.businessOwner.objectId"));
        Assert.StartsWith("Fixture user", McpHarness.Text(draft, "intake.businessOwner.displayLabel"));
        Assert.StartsWith("IR-", McpHarness.Text(draft, "intake.recordNumber"));

        h.Ok(McpHarness.TokenOwner, "answer_tier_questions", "{\"intakeRequestId\":\"" + intake + "\",\"answers\":" + Scenarios.TierAnswers + "}");
        JsonTreeNode submitted = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "submit_record", "{\"subjectKind\":\"intake-request\",\"subjectId\":\"" + intake + "\"}"));
        string decision = McpHarness.Text(submitted, "decisionId");
        JsonTreeNode gate = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "get_decision", "{\"decisionId\":\"" + decision + "\"}"));
        Assert.Equal("false", McpHarness.Text(gate, "gate.ready"));
        string record = McpHarness.Text(Assert.Single(Js.Items(submitted, "approvalsRequested"), r => McpHarness.Text(r, "isConsultation") == "false" && McpHarness.Text(r, "askedPrincipal") == "null"), "approvalRecordId");
        string consent = McpHarness.Text(Assert.Single(Js.Items(submitted, "approvalsRequested"), r => McpHarness.Text(r, "askedPrincipal.objectId") == McpHarness.Deputy), "approvalRecordId");
        h.Ok(McpHarness.TokenAdminWeb, "answer_approval", "{\"approvalRecordId\":\"" + record + "\",\"answer\":\"approve\"}", server: h.PersonServer());
        Assert.Equal("proposed", h.Store.Get("decision", decision)!.Text("statusMeaning"));
        h.Ok(McpHarness.TokenDeputyWeb, "answer_approval", "{\"approvalRecordId\":\"" + consent + "\",\"answer\":\"approve\"}", server: h.PersonServer());
        Assert.Equal("accepted", h.Store.Get("decision", decision)!.Text("statusMeaning"));
        Assert.StartsWith("DEC-", h.Store.Get("decision", decision)!.Text("recordNumber"));

        JsonTreeNode read = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "get_intake_request", "{\"intakeRequestId\":\"" + intake + "\"}"));
        Assert.Equal("completed", McpHarness.Text(read, "intake.status.meaning"));
        string app = McpHarness.Text(read, "intake.businessApplicationId");
        Assert.StartsWith("APP-", h.Store.Get("business-application", app)!.Text("recordNumber"));
        JsonTreeNode owners = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "get_application_owners", "{\"businessApplicationId\":\"" + app + "\"}"));
        JsonTreeNode owner = Assert.Single(Js.Items(owners, "owners"));
        Assert.Equal("business-owner", McpHarness.Text(owner, "ownerRoleKey"));
        Assert.Equal(McpHarness.Deputy, McpHarness.Text(owner, "principal.objectId"));
        Assert.Equal(decision, McpHarness.Text(owner, "ownershipDecisionId"));

        // The same request with no named owner stays proposed: the Accept gate holds it.
        string unnamed = McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenOwner, "save_intake_draft", NewAppIntake("Store visit planner two", null))), "recordId");
        h.Ok(McpHarness.TokenOwner, "answer_tier_questions", "{\"intakeRequestId\":\"" + unnamed + "\",\"answers\":" + Scenarios.TierAnswers + "}");
        JsonTreeNode heldSubmit = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "submit_record", "{\"subjectKind\":\"intake-request\",\"subjectId\":\"" + unnamed + "\"}"));
        string held = McpHarness.Text(heldSubmit, "decisionId");
        Assert.Equal("false", McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenOwner, "get_decision", "{\"decisionId\":\"" + held + "\"}")), "gate.ready"));
        h.Ok(McpHarness.TokenAdminWeb, "answer_approval", "{\"approvalRecordId\":\"" + McpHarness.Text(Assert.Single(Js.Items(heldSubmit, "approvalsRequested"), r => McpHarness.Text(r, "isConsultation") == "false"), "approvalRecordId") + "\",\"answer\":\"approve\"}", server: h.PersonServer());
        Assert.Equal("proposed", h.Store.Get("decision", held)!.Text("statusMeaning"));
    }

    // A person who left, or one the owner check found disabled, cannot be named as the business owner.
    [Fact]
    public void ALeaverCannotBeNamedTheBusinessOwner()
    {
        McpHarness h = McpHarness.Create();
        StoredRow reader = h.Store.Query(RowQuery.For("principal-reference").Where("entraObjectId", McpHarness.Reader)).Single();
        h.Store.Tamper("principal-reference", reader.Id, Js.With(reader.Columns, ("directoryState", JsonTreeNode.FromString("disabled"))));
        JsonTreeNode refused = h.Fails(McpHarness.TokenOwner, "save_intake_draft", NewAppIntake("Leaver", McpHarness.Reader), McpErrorCodes.Validation);
        Assert.Equal("businessOwnerObjectId", McpHarness.Text(refused, "error.field"));
    }

    // The PMO's demand fields: a draft is the requester's, so it is not scored; a request on the table is. The demand list
    // sorts by score (the highest first), by size band in the list's order, and by priority, and filters by size band,
    // priority and whether a score is recorded.
    [Fact]
    public void ThePmoScoresRequestsAndTheDemandListSortsAndFiltersThem()
    {
        McpHarness h = McpHarness.Create();
        string draft = Scenarios.Intake(h, "Fixture draft request");
        JsonTreeNode refused = h.Fails(McpHarness.TokenAdmin, "score_intake_request", "{\"intakeRequestId\":\"" + draft + "\",\"score\":50}", McpErrorCodes.RuleViolation);
        Assert.Contains("draft", McpHarness.Text(refused, "error.message"));

        string low = Submitted(h, "Fixture low request");
        string high = Submitted(h, "Fixture high request");
        string unscored = Submitted(h, "Fixture unscored request");
        JsonTreeNode scored = McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "score_intake_request", "{\"intakeRequestId\":\"" + low + "\",\"score\":40,\"sizeBandKey\":\"small\",\"horizonKey\":\"later\"}"));
        Assert.Equal("40", McpHarness.Text(scored, "score"));
        Assert.Equal("small", McpHarness.Text(scored, "sizeBand.key"));
        h.Ok(McpHarness.TokenAdmin, "score_intake_request", "{\"intakeRequestId\":\"" + high + "\",\"score\":80,\"sizeBandKey\":\"large\",\"horizonKey\":\"now\"}");
        h.Fails(McpHarness.TokenAdmin, "score_intake_request", "{\"intakeRequestId\":\"" + high + "\",\"sizeBandKey\":\"enormous\"}", McpErrorCodes.Validation);
        h.Fails(McpHarness.TokenOwner, "score_intake_request", "{\"intakeRequestId\":\"" + high + "\",\"score\":99}", McpErrorCodes.NotAuthorized);

        Assert.Equal(new[] { high, low, unscored }, Demand(h, "{\"view\":\"demand\"}"));
        Assert.Equal(new[] { low, high, unscored }, Demand(h, "{\"view\":\"demand\",\"orderBy\":\"size-band\"}"));
        Assert.Equal(new[] { high }, Demand(h, "{\"view\":\"demand\",\"sizeBandKeys\":[\"large\"]}"));
        Assert.Equal(new[] { unscored }, Demand(h, "{\"view\":\"demand\",\"scored\":false}"));

        // A proposed priority decision on the low request (test setup: the harness has no PMO or sponsor to propose it).
        string priority = h.Store.Seed("decision", "{\"decisionType\":\"priority\",\"subjectKind\":\"intake-request\",\"subjectIntakeRequest\":\"" + low + "\",\"statement\":\"Fund the low request\",\"outcome\":\"fund\","
            + "\"status\":\"decision-proposed\",\"statusMeaning\":\"proposed\",\"configurationSetVersion\":1}", id: "00000000-0000-4000-8000-000000000591");
        Assert.Equal(new[] { low }, Demand(h, "{\"view\":\"demand\",\"priorityKeys\":[\"proposed\"]}"));
        Assert.Equal(new[] { low, high, unscored }, Demand(h, "{\"view\":\"demand\",\"orderBy\":\"priority\"}"));
        JsonTreeNode item = Js.Items(McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "list_intake_requests", "{\"view\":\"demand\",\"priorityKeys\":[\"proposed\"]}")), "items").Single();
        Assert.Equal(priority, McpHarness.Text(item, "priority.decisionId"));
        Assert.Equal("fund", McpHarness.Text(item, "priority.outcome.key"));
        Assert.Equal("proposed", McpHarness.Text(item, "priority.status.meaning"));
    }

    // People search: every word starts a word of the name; erased people are never found; other kinds only when asked.
    [Fact]
    public void FindPeopleFindsByNameAndNeverAnErasedPerson()
    {
        McpHarness h = McpHarness.Create();
        JsonTreeNode found = McpHarness.Data(h.Ok(McpHarness.TokenReader, "find_people", "{\"text\":\"fix user\"}"));
        Assert.Contains(Js.Items(found, "items"), p => McpHarness.Text(p, "objectId") == McpHarness.Owner);
        Assert.All(Js.Items(found, "items"), p => Assert.Equal("user", McpHarness.Text(p, "principalKind")));

        StoredRow owner = h.Store.Query(RowQuery.For("principal-reference").Where("entraObjectId", McpHarness.Owner)).Single();
        h.Store.Tamper("principal-reference", owner.Id, Js.With(owner.Columns, ("isErased", JsonTreeNode.True)));
        JsonTreeNode again = McpHarness.Data(h.Ok(McpHarness.TokenReader, "find_people", "{\"text\":\"fix user\"}"));
        Assert.DoesNotContain(Js.Items(again, "items"), p => McpHarness.Text(p, "objectId") == McpHarness.Owner);

        JsonTreeNode groups = McpHarness.Data(h.Ok(McpHarness.TokenReader, "find_people", "{\"text\":\"fixture\",\"principalKinds\":[\"group\"]}"));
        Assert.NotEmpty(Js.Items(groups, "items"));
        Assert.All(Js.Items(groups, "items"), p => Assert.Equal("group", McpHarness.Text(p, "principalKind")));
        Assert.Equal("0", McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenReader, "find_people", "{\"text\":\"nobody here\"}")), "totalCount"));
    }

    // Balanced refuses self-approval at tier 3 (D-40). The deputy who proposed a go-live is asked through the owner role,
    // but the pre-check says the separation rule refuses, so the page hides Approve and names the rule; the business owner
    // may answer. get_decision says the same on the record.
    [Fact]
    public void TheListAndTheDecisionSayWhoMayAnswerAndWhichRuleRefuses()
    {
        McpHarness h = McpHarness.Create();
        SeedTier3(h);
        string decision = McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenDeputy, "save_decision_proposal", "{\"decisionTypeKey\":\"go-live\",\"subjectKind\":\"business-application\",\"subjectId\":\"" + Tier3App
            + "\",\"statement\":\"Go live\",\"context\":\"Fixture context\",\"proposedOutcomeKey\":\"go\",\"reason\":\"Fixture reason\"}")), "recordId");
        h.Ok(McpHarness.TokenDeputy, "submit_record", "{\"subjectKind\":\"decision\",\"subjectId\":\"" + decision + "\"}");
        McpServerCore web = h.PersonServer();

        JsonTreeNode deputy = Js.Items(McpHarness.Data(h.Ok(McpHarness.TokenDeputyWeb, "list_approvals", "{\"view\":\"mine\",\"decisionId\":\"" + decision + "\"}", server: web)), "items").Single();
        Assert.Equal("false", McpHarness.Text(deputy, "canAnswer"));
        Assert.Equal("approval.answer-separation", McpHarness.Text(deputy, "cannotAnswer.code"));
        Assert.Equal("no-self-approval-tier-3", McpHarness.Text(deputy, "cannotAnswer.separationRule.key"));
        Assert.Equal("null", McpHarness.Text(deputy, "nextTool"));

        JsonTreeNode owner = Js.Items(McpHarness.Data(h.Ok(McpHarness.TokenOwnerWeb, "list_approvals", "{\"view\":\"mine\",\"decisionId\":\"" + decision + "\"}", server: web)), "items").Single();
        Assert.Equal("true", McpHarness.Text(owner, "canAnswer"));
        Assert.Equal("null", McpHarness.Text(owner, "cannotAnswer"));
        Assert.Equal("answer_approval", McpHarness.Text(owner, "nextTool"));

        JsonTreeNode asDeputy = Js.Items(Js.Member(McpHarness.Data(h.Ok(McpHarness.TokenDeputyWeb, "get_decision", "{\"decisionId\":\"" + decision + "\"}", server: web)), "approvals"), "records").Single();
        Assert.Equal("false", McpHarness.Text(asDeputy, "canAnswer"));
        Assert.Equal("no-self-approval-tier-3", McpHarness.Text(asDeputy, "cannotAnswer.separationRule.key"));
        JsonTreeNode asOwner = Js.Items(Js.Member(McpHarness.Data(h.Ok(McpHarness.TokenOwnerWeb, "get_decision", "{\"decisionId\":\"" + decision + "\"}", server: web)), "approvals"), "records").Single();
        Assert.Equal("true", McpHarness.Text(asOwner, "canAnswer"));

        // A reader is not asked, so the record says neither.
        JsonTreeNode asReader = Js.Items(Js.Member(McpHarness.Data(h.Ok(McpHarness.TokenReader, "get_decision", "{\"decisionId\":\"" + decision + "\"}")), "approvals"), "records").Single();
        Assert.Equal("false", McpHarness.Text(asReader, "canAnswer"));
        Assert.Equal("null", McpHarness.Text(asReader, "cannotAnswer"));
    }

    // An exception decision names the standard it departs from and a review date to come; no other decision type takes
    // them. The decision page reads them back.
    [Fact]
    public void AnExceptionNamesItsStandardAndReviewDate()
    {
        McpHarness h = McpHarness.Create();
        string Exception(string extra) => "{\"decisionTypeKey\":\"exception\",\"subjectKind\":\"business-application\",\"subjectId\":\"" + McpHarness.Expense
            + "\",\"statement\":\"Grant an exception for the hotfix\",\"context\":\"Only an AI reviewer approved the code\",\"proposedOutcomeKey\":\"grant\",\"reason\":\"It stops duplicate payments\"" + extra + "}";
        h.Fails(McpHarness.TokenArchitect, "save_decision_proposal", Exception(string.Empty), McpErrorCodes.Validation);
        h.Fails(McpHarness.TokenArchitect, "save_decision_proposal", Exception(",\"exception\":{\"standard\":\"A person reviews code\",\"reviewOn\":\"2026-01-01\"}"), McpErrorCodes.Validation);
        h.Fails(McpHarness.TokenAdmin, "save_decision_proposal", "{\"decisionTypeKey\":\"tier\",\"subjectKind\":\"business-application\",\"subjectId\":\"" + McpHarness.Expense
            + "\",\"statement\":\"Confirm\",\"context\":\"Fixture\",\"proposedOutcomeKey\":\"confirm\",\"reason\":\"Fixture\",\"exception\":{\"standard\":\"Any\",\"reviewOn\":\"2027-01-01\"}}", McpErrorCodes.Validation);

        string decision = McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenArchitect, "save_decision_proposal", Exception(",\"exception\":{\"standard\":\"A person reviews the code of every release\",\"reviewOn\":\"2026-12-04\"}"))), "recordId");
        JsonTreeNode read = McpHarness.Data(h.Ok(McpHarness.TokenReader, "get_decision", "{\"decisionId\":\"" + decision + "\"}"));
        Assert.Equal("A person reviews the code of every release", McpHarness.Text(read, "decision.exception.standard"));
        Assert.Equal("2026-12-04", McpHarness.Text(read, "decision.exception.reviewOn"));
        Assert.StartsWith("DEC-", McpHarness.Text(read, "decision.recordNumber"));
    }

    // The decision page shows what acceptance writes: an owner change names the person who starts and the person whose
    // assignment ends.
    [Fact]
    public void GetDecisionShowsTheProposedOwnerChanges()
    {
        McpHarness h = McpHarness.Create();
        string decision = McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenOwner, "propose_owner_change", "{\"businessApplicationId\":\"" + McpHarness.Expense + "\",\"changes\":[{\"ownerRoleKey\":\"business-owner\",\"addObjectId\":\""
            + McpHarness.Reader + "\",\"endAssignmentId\":\"" + McpHarness.OwnerAssignment + "\"}],\"reason\":\"The owner moves to the audit team.\"}")), "decisionId");
        JsonTreeNode read = McpHarness.Data(h.Ok(McpHarness.TokenReader, "get_decision", "{\"decisionId\":\"" + decision + "\"}"));
        JsonTreeNode change = Js.Items(Js.Member(Js.Member(read, "decision"), "proposedChanges"), "owners").Single();
        Assert.Equal("business-owner", McpHarness.Text(change, "ownerRole.key"));
        Assert.Equal(McpHarness.Reader, McpHarness.Text(change, "add.objectId"));
        Assert.Equal(McpHarness.Owner, McpHarness.Text(change, "ends.objectId"));
        Assert.Equal("null", McpHarness.Text(read, "decision.exception"));
    }

    private static string NewAppIntake(string title, string? owner)
    {
        return "{\"title\":\"" + title + "\",\"need\":\"Plan store visits on the phone\",\"proposedKindKey\":\"power-platform-app\"" + (owner == null ? string.Empty : ",\"businessOwnerObjectId\":\"" + owner + "\"") + "}";
    }

    private static string Submitted(McpHarness h, string title)
    {
        string intake = Scenarios.Intake(h, title);
        h.Ok(McpHarness.TokenOwner, "answer_tier_questions", "{\"intakeRequestId\":\"" + intake + "\",\"answers\":" + Scenarios.TierAnswers + "}");
        h.Ok(McpHarness.TokenOwner, "submit_record", "{\"subjectKind\":\"intake-request\",\"subjectId\":\"" + intake + "\"}");
        return intake;
    }

    private static string[] Demand(McpHarness h, string arguments)
    {
        return Js.Items(McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "list_intake_requests", arguments)), "items").Select(i => McpHarness.Text(i, "intakeRequestId")).ToArray();
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
