using System;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Json;
using Xunit;

namespace Product.Mcp.Core.Tests;

// The reads of the web app's screens (document 24, tools 53 to 59) and the get_my_queue changes (D-36): what each returns from real store rows, that rows a read visibility setting hides are counted in couldNotRead
// and never shown, that a record the caller may not read answers not_found, that skipToken pages the same query only, and
// that every read reports the AI marks exactly as the engine stamped them (null for seeded rows).
public sealed class ScreenReadTests
{
    private const string Owner = McpHarness.TokenOwner;
    private const string Admin = McpHarness.TokenAdmin;
    private const string Reader = McpHarness.TokenReader;
    private const string Architect = McpHarness.TokenArchitect;
    private const string Deputy = McpHarness.TokenDeputy;

    // ---- get_my_queue ----

    // Reviews are role scoped: the architect may propose the design decision an architecture review records (design-a in
    // Balanced), so every architecture review is in the architect's queue, on an application the architect does not own.
    // The owner finds it as its technical owner, the review type's lead owner role. A reader holds no letter and owns
    // nothing, so it is not in the reader's queue.
    [Fact]
    public void TheArchitectFindsEveryArchitectureReviewAndTheOwnerFindsTheReviewOfTheirApp()
    {
        McpHarness h = McpHarness.Create();
        string review = Scenarios.ArchitectureReview(h);

        JsonTreeNode architect = Assert.Single(Js.Items(McpHarness.Data(h.Ok(Architect, "get_my_queue", "{\"kinds\":[\"review\"]}")), "items"));
        Assert.Equal(review, McpHarness.Text(architect, "subjectId"));
        Assert.Equal("2026-11-30", McpHarness.Text(architect, "dueOn"));
        Assert.Contains("design decision", McpHarness.Text(architect, "why"));
        Assert.Equal("save_decision_proposal", McpHarness.Text(architect, "nextTool"));

        JsonTreeNode owner = Assert.Single(Js.Items(McpHarness.Data(h.Ok(Owner, "get_my_queue", "{\"kinds\":[\"review\"]}")), "items"));
        Assert.Equal("You are its technical owner, and this review is due.", McpHarness.Text(owner, "why"));
        Assert.Equal("true", McpHarness.Text(owner, "isAiCreated"));
        Assert.Equal("ai-client-for-person", McpHarness.Text(owner, "performedBy.actorKind"));
        Assert.Equal(McpHarness.Owner, McpHarness.Text(owner, "performedBy.onBehalfOf.objectId"));

        Assert.Empty(Js.Items(McpHarness.Data(h.Ok(Reader, "get_my_queue", "{\"kinds\":[\"review\"]}")), "items"));
        Assert.Empty(Js.Items(McpHarness.Data(h.Ok(Admin, "get_my_queue", "{\"kinds\":[\"review\"]}")), "items"));
    }

    // Documents: a required document with no link is missing (computed by OP-06, never stored); a current link checked too
    // long ago is stale by date; an AI client's "current" on a type that needs a person is stored as draft and waits for
    // the person's confirmation.
    [Fact]
    public void TheQueueListsMissingDateStaleAndDraftDocumentsOfOwnedApps()
    {
        McpHarness h = McpHarness.Create();
        h.Store.Seed("document-link", "{\"businessApplication\":\"" + McpHarness.Travel + "\",\"documentType\":\"wiki-page\",\"documentationRequirement\":\"wiki-page-all\",\"linkKind\":\"ado-wiki-page\","
            + "\"url\":\"https://devops.example.org/fixture/wiki/travel\",\"status\":\"document-current\",\"statusMeaning\":\"current\",\"documentOwner\":\"" + McpHarness.Owner + "\","
            + "\"lastUpdatedOn\":\"2025-01-02\",\"lastCheckedOn\":\"2025-01-02\"}", id: "00000000-0000-4000-8000-000000000542");
        JsonTreeNode before = McpHarness.Data(h.Ok(Owner, "get_my_queue", "{\"kinds\":[\"document\"]}"));
        var items = Js.Items(before, "items");
        JsonTreeNode missing = Assert.Single(items, i => McpHarness.Text(i, "status.meaning") == "missing");
        Assert.Equal("business-application", McpHarness.Text(missing, "subjectKind"));
        Assert.Equal(McpHarness.Travel, McpHarness.Text(missing, "subjectId"));
        Assert.Equal("record_document_link", McpHarness.Text(missing, "nextTool"));
        Assert.Equal("null", McpHarness.Text(missing, "performedBy"));
        JsonTreeNode stale = Assert.Single(items, i => McpHarness.Text(i, "status.meaning") == "stale");
        Assert.Equal("00000000-0000-4000-8000-000000000542", McpHarness.Text(stale, "subjectId"));
        Assert.Equal("2026-01-02", McpHarness.Text(stale, "dueOn"));
        Assert.Equal("null", McpHarness.Text(stale, "performedBy"));

        // The AI client says the travel runbook is current; the runbook needs a person, so it is stored as draft.
        h.Ok(Owner, "record_document_link", "{\"businessApplicationId\":\"" + McpHarness.Travel + "\",\"documentTypeKey\":\"runbook\",\"url\":\"https://devops.example.org/fixture/wiki/travel-runbook\",\"statusMeaning\":\"current\"}");
        var after = Js.Items(McpHarness.Data(h.Ok(Owner, "get_my_queue", "{\"kinds\":[\"document\"]}")), "items");
        Assert.DoesNotContain(after, i => McpHarness.Text(i, "status.meaning") == "missing");
        JsonTreeNode draft = Assert.Single(after, i => McpHarness.Text(i, "status.meaning") == "draft");
        Assert.Contains("waits for confirmation", McpHarness.Text(draft, "title"));
        Assert.Equal("confirm_document_checked", McpHarness.Text(draft, "nextTool"));
        Assert.Equal("true", McpHarness.Text(draft, "isAiCreated"));
    }

    // An AI client's draft says so, and a decision the AI client proposed but nobody submitted waits as a draft until it is
    // submitted; then it is an approval request for the person who must answer it.
    [Fact]
    public void AiDraftsAndUnsubmittedProposalsWaitForThePerson()
    {
        McpHarness h = McpHarness.Create();
        string intake = Scenarios.Intake(h);
        JsonTreeNode proposed = h.Ok(Owner, "save_decision_proposal", "{\"decisionTypeKey\":\"retirement\",\"subjectKind\":\"business-application\",\"subjectId\":\"" + McpHarness.Travel
            + "\",\"statement\":\"Retire the travel planner\",\"context\":\"Replaced\",\"proposedOutcomeKey\":\"complete\",\"reason\":\"A duplicate\"}");
        string decision = McpHarness.Text(McpHarness.Data(proposed), "recordId");

        var drafts = Js.Items(McpHarness.Data(h.Ok(Owner, "get_my_queue", "{\"kinds\":[\"draft\"]}")), "items");
        JsonTreeNode intakeDraft = Assert.Single(drafts, i => McpHarness.Text(i, "subjectId") == intake);
        Assert.Equal("true", McpHarness.Text(intakeDraft, "isAiCreated"));
        Assert.Contains("An AI client drafted this for you", McpHarness.Text(intakeDraft, "why"));
        Assert.Equal(McpHarness.Owner, McpHarness.Text(intakeDraft, "performedBy.onBehalfOf.objectId"));
        Assert.False(string.IsNullOrEmpty(McpHarness.Text(intakeDraft, "performedBy.clientAppLabel")));
        JsonTreeNode decisionDraft = Assert.Single(drafts, i => McpHarness.Text(i, "subjectId") == decision);
        Assert.Equal("decision", McpHarness.Text(decisionDraft, "subjectKind"));
        Assert.Equal("submit_record", McpHarness.Text(decisionDraft, "nextTool"));

        h.Ok(Owner, "submit_record", "{\"subjectKind\":\"decision\",\"subjectId\":\"" + decision + "\"}");
        JsonTreeNode queue = McpHarness.Data(h.Ok(Owner, "get_my_queue", "{\"kinds\":[\"draft\",\"approval\"]}"));
        Assert.DoesNotContain(Js.Items(queue, "items"), i => McpHarness.Text(i, "kind") == "draft" && McpHarness.Text(i, "subjectId") == decision);
        JsonTreeNode approval = Assert.Single(Js.Items(queue, "items"), i => McpHarness.Text(i, "kind") == "approval");
        Assert.Equal(decision, McpHarness.Text(approval, "subjectId"));
        Assert.Equal("null", McpHarness.Text(approval, "nextTool"));
    }

    // ---- list_intake_requests and get_intake_request ----

    // intake-visibility "own" (Balanced): the deputy, a requester, sees only their own requests and those they are part of.
    // The owner's request for a new application is counted in couldNotRead and never shown, and its page answers
    // not_found. The platform admin sees it in triage.
    [Fact]
    public void IntakeReadsHideOtherPeoplesRequestsAndCountThem()
    {
        McpHarness h = McpHarness.Create();
        string intake = Scenarios.SubmittedIntake(h, linked: false);

        JsonTreeNode deputy = h.Ok(Deputy, "list_intake_requests", "{}");
        Assert.Empty(Js.Items(McpHarness.Data(deputy), "items"));
        JsonTreeNode hidden = Assert.Single(Js.Items(deputy, "couldNotRead"));
        Assert.Equal("intake-request", McpHarness.Text(hidden, "kind"));
        Assert.Equal("1", McpHarness.Text(hidden, "count"));
        h.Fails(Deputy, "get_intake_request", "{\"intakeRequestId\":\"" + intake + "\"}", McpErrorCodes.NotFound);

        // Security review: a hidden request is never tested against a filter, so a search for a word in it, a status or
        // a view counts nothing. Only the whole, unfiltered list says how many are hidden.
        string title = h.Store.Rows("intake-request").Single(r => r.Id == intake).Text("title")!;
        string word = title.Split(' ').First(w => w.Length > 3).ToLowerInvariant();
        foreach (string filtered in new[] { "{\"text\":\"" + word + "\"}", "{\"statusMeanings\":[\"submitted\"]}", "{\"view\":\"triage\"}" })
        {
            JsonTreeNode searched = h.Ok(Deputy, "list_intake_requests", filtered);
            Assert.Empty(Js.Items(McpHarness.Data(searched), "items"));
            Assert.Empty(Js.Items(searched, "couldNotRead"));
            Assert.DoesNotContain("not shown to you", McpHarness.Text(searched, "summary"), StringComparison.Ordinal);
        }

        JsonTreeNode admin = h.Ok(Admin, "list_intake_requests", "{\"view\":\"triage\"}");
        Assert.Empty(Js.Items(admin, "couldNotRead"));
        JsonTreeNode item = Assert.Single(Js.Items(McpHarness.Data(admin), "items"));
        Assert.Equal(intake, McpHarness.Text(item, "intakeRequestId"));
        Assert.Equal("submitted", McpHarness.Text(item, "status.meaning"));
        string computed = h.Store.Rows("tier-assessment").Single().Text("computedTier")!;
        Assert.Equal(computed, McpHarness.Text(item, "suggestedTier.key"));
        Assert.Equal("proposed", McpHarness.Text(item, "triageDecision.status.meaning"));
        Assert.Equal("true", McpHarness.Text(item, "isAiCreated"));
        Assert.Empty(Js.Items(McpHarness.Data(h.Ok(Admin, "list_intake_requests", "{\"view\":\"drafts\"}")), "items"));
        Assert.Single(Js.Items(McpHarness.Data(h.Ok(Owner, "list_intake_requests", "{\"view\":\"mine\",\"text\":\"travel\"}")), "items"));
        Assert.Empty(Js.Items(McpHarness.Data(h.Ok(Owner, "list_intake_requests", "{\"view\":\"mine\",\"text\":\"payroll\"}")), "items"));

        JsonTreeNode page = h.Ok(Owner, "get_intake_request", "{\"intakeRequestId\":\"" + intake + "\"}");
        JsonTreeNode data = McpHarness.Data(page);
        Assert.Equal("Replace the shared spreadsheet", McpHarness.Text(data, "intake.need"));
        Assert.Equal(computed, McpHarness.Text(Assert.Single(Js.Items(data, "tierAssessments")), "computedTier.key"));
        JsonTreeNode triage = Assert.Single(Js.Items(data, "decisions"));
        Assert.Equal("true", McpHarness.Text(triage, "isTriage"));
        Assert.Contains("intake.need", Js.Strings(page, "untrustedFields"));
        Assert.Equal("get_decision", McpHarness.Text(Js.Items(page, "nextSteps")[0], "tool"));
    }

    // Fix round 1 (product review): the auditor reads everything (document 26, section 3.2), intake requests and the decisions about
    // them included, so her decision log hides nothing. Without the role, the reader's log counts the hidden triage
    // decision and the request's page answers not_found.
    [Fact]
    public void TheAuditorReadsOtherPeoplesIntakeRequestsAndTheirDecisions()
    {
        McpHarness plain = McpHarness.Create();
        string hidden = Scenarios.SubmittedIntake(plain, linked: false);
        Assert.Contains(Js.Items(plain.Ok(McpHarness.TokenReader, "get_decision_log", "{}"), "couldNotRead"), c => McpHarness.Text(c, "kind") == "decision");
        plain.Fails(McpHarness.TokenReader, "get_intake_request", "{\"intakeRequestId\":\"" + hidden + "\"}", McpErrorCodes.NotFound);

        McpHarness h = McpHarness.Create("[" + McpHarness.BindingOperation("auditor", McpHarness.ReaderGroup) + "]");
        string intake = Scenarios.SubmittedIntake(h, linked: false);
        Assert.Empty(Js.Items(h.Ok(McpHarness.TokenReader, "get_decision_log", "{}"), "couldNotRead"));
        Assert.Empty(Js.Items(h.Ok(McpHarness.TokenReader, "list_intake_requests", "{}"), "couldNotRead"));
        Assert.Equal(intake, McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenReader, "get_intake_request", "{\"intakeRequestId\":\"" + intake + "\"}")), "intake.intakeRequestId"));
    }

    // ---- list_approvals and get_decision ----

    [Fact]
    public void ListApprovalsServesEachViewAndNeverOffersAnAiClientTheAnswer()
    {
        McpHarness h = McpHarness.Create();
        string record = Scenarios.PendingRetirementApproval(h);
        Scenarios.SubmittedIntake(h, linked: false);

        JsonTreeNode mine = Assert.Single(Js.Items(McpHarness.Data(h.Ok(Owner, "list_approvals", "{}")), "items"));
        Assert.Equal(record, McpHarness.Text(mine, "approvalRecordId"));
        Assert.Equal("business-owner", McpHarness.Text(mine, "askedOwnerRole.key"));
        Assert.Equal("true", McpHarness.Text(mine, "isOwnProposal"));
        Assert.Equal("true", McpHarness.Text(mine, "decision.isAiCreated"));
        Assert.Equal("Fixture travel planner", McpHarness.Text(mine, "subject.label"));
        Assert.Equal("null", McpHarness.Text(mine, "nextTool"));
        JsonTreeNode web = Assert.Single(Js.Items(McpHarness.Data(h.Ok(McpHarness.TokenOwnerWeb, "list_approvals", "{\"view\":\"mine\"}", server: h.PersonServer())), "items"));
        Assert.Equal("answer_approval", McpHarness.Text(web, "nextTool"));

        // asked-by-me: the retirement the owner proposed, and the intake the owner requested with its platform admin card
        // and the three consultation cards Balanced opens on an intake (the architect, the PMO and the security reviewer).
        Assert.Equal(5, Js.Items(McpHarness.Data(h.Ok(Owner, "list_approvals", "{\"view\":\"asked-by-me\"}")), "items").Count);
        Assert.Single(Js.Items(McpHarness.Data(h.Ok(Owner, "list_approvals", "{\"view\":\"asked-by-me\",\"decisionTypeKeys\":[\"retirement\"]}")), "items"));

        // The platform admin is asked the intake card, and sees both requests in open.
        JsonTreeNode adminMine = Assert.Single(Js.Items(McpHarness.Data(h.Ok(Admin, "list_approvals", "{\"view\":\"mine\"}")), "items"));
        Assert.Equal("intake-request", McpHarness.Text(adminMine, "subject.subjectKind"));

        // A decision page reads only the requests on its own decision.
        string intakeDecision = McpHarness.Text(adminMine, "decision.decisionId");
        Assert.Single(Js.Items(McpHarness.Data(h.Ok(Admin, "list_approvals", "{\"view\":\"mine\",\"decisionId\":\"" + intakeDecision + "\"}")), "items"));
        Assert.Empty(Js.Items(McpHarness.Data(h.Ok(Admin, "list_approvals", "{\"view\":\"mine\",\"decisionId\":\"00000000-0000-4000-8000-00000000dead\"}")), "items"));
        // Open: the retirement card, the intake card and the intake's three consultation cards.
        Assert.Equal(5, Js.Items(McpHarness.Data(h.Ok(Admin, "list_approvals", "{\"view\":\"open\"}")), "items").Count);

        // The deputy may not read the owner's intake request, so its card is counted, never shown.
        JsonTreeNode deputy = h.Ok(Deputy, "list_approvals", "{\"view\":\"open\"}");
        Assert.Single(Js.Items(McpHarness.Data(deputy), "items"));
        Assert.Equal("approval-record", McpHarness.Text(Assert.Single(Js.Items(deputy, "couldNotRead")), "kind"));

        h.Ok(McpHarness.TokenOwnerWeb, "answer_approval", "{\"approvalRecordId\":\"" + record + "\",\"answer\":\"approve\"}", server: h.PersonServer());
        Assert.Empty(Js.Items(McpHarness.Data(h.Ok(Owner, "list_approvals", "{}")), "items"));
        JsonTreeNode answered = Assert.Single(Js.Items(McpHarness.Data(h.Ok(Owner, "list_approvals", "{\"view\":\"answered\"}")), "items"));
        Assert.Equal("approved", McpHarness.Text(answered, "outcome.meaning"));
        Assert.Equal(McpHarness.Owner, McpHarness.Text(answered, "approver.objectId"));
        Assert.Equal("null", McpHarness.Text(answered, "nextTool"));
    }

    // A proposed decision: the engine evaluates the rules now, and each request says whom it asks. Once a person's answer
    // settles it, the page shows the records as the engine stored them, without evaluating again.
    [Fact]
    public void GetDecisionShowsTheApprovalStateLiveWhileProposedAndAsStoredOnceSettled()
    {
        McpHarness h = McpHarness.Create();
        string decision = Scenarios.SubmittedRetirement(h);

        JsonTreeNode proposed = McpHarness.Data(h.Ok(Reader, "get_decision", "{\"decisionId\":\"" + decision + "\"}"));
        Assert.Equal("proposed", McpHarness.Text(proposed, "decision.status.meaning"));
        Assert.Equal(McpHarness.Owner, McpHarness.Text(proposed, "decision.proposedBy.objectId"));
        Assert.Equal("true", McpHarness.Text(proposed, "decision.isAiCreated"));
        Assert.Equal("Fixture travel planner", McpHarness.Text(proposed, "subject.label"));
        Assert.Equal(McpHarness.Travel, McpHarness.Text(proposed, "subject.businessApplicationId"));
        Assert.Equal("true", McpHarness.Text(proposed, "approvals.evaluated"));
        Assert.Equal("false", McpHarness.Text(proposed, "approvals.ready"));
        JsonTreeNode request = Assert.Single(Js.Items(Js.Member(proposed, "approvals"), "records"));
        Assert.Equal("business-owner", McpHarness.Text(request, "askedOwnerRole.key"));
        Assert.Equal("pending", McpHarness.Text(request, "outcome.meaning"));
        Assert.Single(Js.Items(Js.Member(proposed, "approvals"), "missing"));

        string record = McpHarness.Text(request, "approvalRecordId");
        h.Ok(McpHarness.TokenOwnerWeb, "answer_approval", "{\"approvalRecordId\":\"" + record + "\",\"answer\":\"approve\"}", server: h.PersonServer());
        JsonTreeNode settled = McpHarness.Data(h.Ok(Reader, "get_decision", "{\"decisionId\":\"" + decision + "\"}"));
        Assert.Equal("accepted", McpHarness.Text(settled, "decision.status.meaning"));
        Assert.Equal("false", McpHarness.Text(settled, "approvals.evaluated"));
        Assert.Equal("true", McpHarness.Text(settled, "approvals.ready"));
        JsonTreeNode answered = Assert.Single(Js.Items(Js.Member(settled, "approvals"), "records"));
        Assert.Equal("approved", McpHarness.Text(answered, "outcome.meaning"));
        Assert.Equal("true", McpHarness.Text(answered, "countsTowardRequirement"));

        // The intake decision of a request the deputy may not read answers not_found. The deputy owns the expense app, so
        // a request about it is theirs to read (D-41, B24-14); a request for a new application is not.
        string hiddenIntake = Scenarios.SubmittedIntake(h, linked: false);
        string hiddenTriage = h.Store.Rows("intake-request").Single(r => r.Id == hiddenIntake).Text("triageDecision")!;
        h.Fails(Deputy, "get_decision", "{\"decisionId\":\"" + hiddenTriage + "\"}", McpErrorCodes.NotFound);
        string intake = Scenarios.SubmittedIntake(h);
        string triage = h.Store.Rows("intake-request").Single(r => r.Id == intake).Text("triageDecision")!;
        h.Ok(Deputy, "get_decision", "{\"decisionId\":\"" + triage + "\"}");
        JsonTreeNode intakeDecision = McpHarness.Data(h.Ok(Admin, "get_decision", "{\"decisionId\":\"" + triage + "\"}"));
        Assert.Equal("intake-request", McpHarness.Text(intakeDecision, "subject.subjectKind"));

        // QA: the intake decision is the accept gate's decision, so it is accepted only once the gate is ready (G-14).
        // This request is about the expense app, which has a business owner: the gate is ready.
        Assert.Equal("accept", McpHarness.Text(intakeDecision, "gate.gateKey"));
        Assert.Equal("true", McpHarness.Text(intakeDecision, "gate.ready"));
        Assert.Empty(Js.Items(Js.Member(intakeDecision, "gate"), "unmet"));

        // A request for a new application has no business owner assigned, and the page reads why its decision waits.
        JsonTreeNode draft = h.Ok(McpHarness.TokenOwner, "save_intake_draft", "{\"title\":\"Weather alerts\",\"need\":\"Warn crews before storms\",\"proposedKindKey\":\"power-platform-app\"}");
        string newApp = McpHarness.Text(McpHarness.Data(draft), "recordId");
        h.Ok(McpHarness.TokenOwner, "answer_tier_questions", "{\"intakeRequestId\":\"" + newApp + "\",\"answers\":" + Scenarios.TierAnswers + "}");
        h.Ok(McpHarness.TokenOwner, "submit_record", "{\"subjectKind\":\"intake-request\",\"subjectId\":\"" + newApp + "\"}");
        string newTriage = h.Store.Rows("intake-request").Single(r => r.Id == newApp).Text("triageDecision")!;
        JsonTreeNode held = McpHarness.Data(h.Ok(Admin, "get_decision", "{\"decisionId\":\"" + newTriage + "\"}"));
        Assert.Equal("false", McpHarness.Text(held, "gate.ready"));
        JsonTreeNode unmet = Assert.Single(Js.Items(Js.Member(held, "gate"), "unmet"));
        Assert.Equal("owner-assigned", McpHarness.Text(unmet, "requirementKind"));
        Assert.Equal("business-owner", McpHarness.Text(unmet, "requiredOwnerRoleKey"));

        // A settled decision, and a decision whose type no gate takes, carry no gate.
        Assert.Equal("null", McpHarness.Text(settled, "gate"));
        Assert.Equal("null", McpHarness.Text(proposed, "gate"));
    }

    // QA: a decision about a review names the review, not its application. The review records the decision as its
    // Outcome Decision, the decision log finds it by the review, and the context holds no review ID.
    [Fact]
    public void ADecisionAboutAReviewNamesTheReview()
    {
        McpHarness h = McpHarness.Create();
        string review = Scenarios.ArchitectureReview(h);
        JsonTreeNode saved = h.Ok(McpHarness.TokenArchitect, "save_decision_proposal",
            "{\"decisionTypeKey\":\"design\",\"subjectKind\":\"review\",\"subjectId\":\"" + review + "\",\"statement\":\"Use the ledger API.\",\"context\":\"The review looked at the integration.\","
            + "\"proposedOutcomeKey\":\"approve\",\"reason\":\"It is the supported way in.\",\"optionsConsidered\":[{\"option\":\"A file drop\",\"whyNot\":\"No audit trail.\"}]}");
        string decision = McpHarness.Text(McpHarness.Data(saved), "recordId");

        Assert.Equal(decision, h.Store.Rows("review").Single(r => r.Id == review).Text("outcomeDecision"));
        JsonTreeNode detail = McpHarness.Data(h.Ok(Reader, "get_decision", "{\"decisionId\":\"" + decision + "\"}"));
        Assert.Equal("review", McpHarness.Text(detail, "subject.subjectKind"));
        Assert.Equal(review, McpHarness.Text(detail, "subject.subjectId"));
        Assert.Equal(McpHarness.Expense, McpHarness.Text(detail, "subject.businessApplicationId"));
        Assert.EndsWith("/review/" + review, McpHarness.Text(detail, "subject.recordUrl"), StringComparison.Ordinal);
        Assert.Equal("The review looked at the integration.", McpHarness.Text(detail, "decision.context"));

        JsonTreeNode log = McpHarness.Data(h.Ok(Reader, "get_decision_log", "{\"subjectKind\":\"review\",\"subjectId\":\"" + review + "\"}"));
        Assert.Equal(decision, McpHarness.Text(Assert.Single(Js.Items(log, "items")), "decisionId"));
    }

    // ---- list_reviews ----

    [Fact]
    public void ListReviewsCountsByStateAndKnowsWhoseTurnItIs()
    {
        McpHarness h = McpHarness.Create();
        string architecture = Scenarios.ArchitectureReview(h);
        h.Store.Seed("review", "{\"businessApplication\":\"" + McpHarness.Travel + "\",\"reviewType\":\"owner-check\",\"status\":\"review-due\",\"statusMeaning\":\"due\",\"trigger\":\"cadence\",\"dueOn\":\"2026-11-01\"}", id: "00000000-0000-4000-8000-000000000571");

        JsonTreeNode due = McpHarness.Data(h.Ok(Architect, "list_reviews", "{}"));
        Assert.Equal(2, Js.Items(due, "items").Count);
        Assert.Equal("1", McpHarness.Text(due, "counts.overdue"));
        Assert.Equal("2", McpHarness.Text(due, "counts.due"));
        JsonTreeNode overdue = Js.Items(due, "items")[0];
        Assert.Equal("true", McpHarness.Text(overdue, "isOverdue"));
        Assert.Equal("false", McpHarness.Text(overdue, "isMine"));
        Assert.Equal("null", McpHarness.Text(overdue, "performedBy"));
        Assert.Equal(McpHarness.Owner, McpHarness.Text(overdue, "leadOwner.objectId"));
        JsonTreeNode mine = Assert.Single(Js.Items(McpHarness.Data(h.Ok(Architect, "list_reviews", "{\"mineOnly\":true}")), "items"));
        Assert.Equal(architecture, McpHarness.Text(mine, "reviewId"));
        Assert.Equal("design", McpHarness.Text(mine, "producesDecisionType.key"));
        Assert.Equal("true", McpHarness.Text(mine, "isAiCreated"));

        Assert.Single(Js.Items(McpHarness.Data(h.Ok(Reader, "list_reviews", "{\"state\":\"overdue\"}")), "items"));
        Assert.Single(Js.Items(McpHarness.Data(h.Ok(Reader, "list_reviews", "{\"dueWithinDays\":0}")), "items"));
        Assert.Single(Js.Items(McpHarness.Data(h.Ok(Reader, "list_reviews", "{\"reviewTypeKeys\":[\"architecture\"]}")), "items"));
        Assert.Empty(Js.Items(McpHarness.Data(h.Ok(Reader, "list_reviews", "{\"state\":\"done\"}")), "items"));
    }

    // ---- list_portfolios and list_owner_assignments ----

    [Fact]
    public void ListPortfoliosCountsTheApplicationsAndOpenDecisions()
    {
        McpHarness h = McpHarness.Create();
        Scenarios.SubmittedRetirement(h);
        JsonTreeNode item = Assert.Single(Js.Items(McpHarness.Data(h.Ok(Reader, "list_portfolios", "{}")), "items"));
        Assert.Equal(McpHarness.Portfolio, McpHarness.Text(item, "portfolioId"));
        Assert.Equal("2", McpHarness.Text(item, "counts.applications"));
        Assert.Equal("1", McpHarness.Text(item, "counts.openDecisions"));
        Assert.Equal(McpHarness.Portfolio, McpHarness.Text(Assert.Single(Js.Items(item, "path")), "portfolioId"));
    }

    [Fact]
    public void ListOwnerAssignmentsFindsPeopleRolesAndLeaversAndPagesTheSameQueryOnly()
    {
        McpHarness h = McpHarness.Create();
        JsonTreeNode all = McpHarness.Data(h.Ok(Reader, "list_owner_assignments", "{}"));
        Assert.Equal(4, Js.Items(all, "items").Count);
        Assert.Equal("2", McpHarness.Text(all, "applicationsInScope"));
        JsonTreeNode business = Assert.Single(Js.Items(all, "coverage"), c => McpHarness.Text(c, "ownerRoleKey") == "business-owner");
        Assert.Equal("2", McpHarness.Text(business, "applicationsHeld"));
        Assert.Equal("1", McpHarness.Text(business, "applicationsWithDeputy"));

        JsonTreeNode deputy = Assert.Single(Js.Items(McpHarness.Data(h.Ok(Reader, "list_owner_assignments", "{\"principalObjectId\":\"" + McpHarness.Deputy + "\"}")), "items"));
        Assert.Equal("true", McpHarness.Text(deputy, "isDeputy"));
        Assert.Equal("current", McpHarness.Text(deputy, "state"));
        Assert.Equal(2, Js.Items(McpHarness.Data(h.Ok(Reader, "list_owner_assignments", "{\"ownerRoleKeys\":[\"business-owner\"],\"businessApplicationId\":\"" + McpHarness.Expense + "\"}")), "items").Count);

        // A recorded leave makes the owner a leaver; a pending business owner card on the travel planner is what moves.
        Assert.Empty(Js.Items(McpHarness.Data(h.Ok(Admin, "list_owner_assignments", "{\"leaversOnly\":true}")), "items"));
        h.Ok(Owner, "record_owner_leave", "{\"principalObjectId\":\"" + McpHarness.Owner + "\",\"leavesOn\":\"2026-12-31\",\"kind\":\"leave\"}");
        Scenarios.PendingRetirementApproval(h);
        var leavers = Js.Items(McpHarness.Data(h.Ok(Admin, "list_owner_assignments", "{\"leaversOnly\":true}")), "items");
        Assert.Equal(3, leavers.Count);
        Assert.All(leavers, l => Assert.Equal("2026-12-31", McpHarness.Text(l, "holder.leavesOn")));
        JsonTreeNode travel = Assert.Single(leavers, l => McpHarness.Text(l, "businessApplicationId") == McpHarness.Travel);
        Assert.Equal("1", McpHarness.Text(travel, "pendingApprovalCount"));

        // A leave date is personal data: only a platform admin (the owner check's audience) lists who is leaving or reads
        // another person's leave date. The holder reads their own.
        h.Fails(Reader, "list_owner_assignments", "{\"leaversOnly\":true}", McpErrorCodes.NotAuthorized);
        var seenByReader = Js.Items(McpHarness.Data(h.Ok(Reader, "list_owner_assignments", "{\"principalObjectId\":\"" + McpHarness.Owner + "\"}")), "items");
        Assert.NotEmpty(seenByReader);
        Assert.All(seenByReader, l => Assert.Equal("null", McpHarness.Text(l, "holder.leavesOn")));
        var seenByHolder = Js.Items(McpHarness.Data(h.Ok(Owner, "list_owner_assignments", "{\"principalObjectId\":\"" + McpHarness.Owner + "\"}")), "items");
        Assert.All(seenByHolder, l => Assert.Equal("2026-12-31", McpHarness.Text(l, "holder.leavesOn")));

        // skipToken pages the same query, and a token from another query is refused.
        JsonTreeNode first = McpHarness.Data(h.Ok(Reader, "list_owner_assignments", "{\"top\":3}"));
        Assert.Equal(3, Js.Items(first, "items").Count);
        string token = McpHarness.Text(first, "skipToken");
        JsonTreeNode second = McpHarness.Data(h.Ok(Reader, "list_owner_assignments", "{\"top\":3,\"skipToken\":\"" + token + "\"}"));
        Assert.Single(Js.Items(second, "items"));
        Assert.Equal(string.Empty, McpHarness.Text(second, "skipToken"));
        h.Fails(Admin, "list_owner_assignments", "{\"top\":3,\"leaversOnly\":true,\"skipToken\":\"" + token + "\"}", McpErrorCodes.Validation);
    }

    // QA: a leave reports every owner check that covers it, and says which were already due. A check the cadence planned
    // for later is brought forward to today, so no check falls after the person has gone. The due date never carries the
    // leave date (security review 2026-11-24): any role reads a review's due date, and the leave date is HR data.
    [Fact]
    public void ALeaveReusesAnOwnerCheckAlreadyDueAndBringsItForward()
    {
        McpHarness h = McpHarness.Create();
        h.Store.Seed("review", "{\"businessApplication\":\"" + McpHarness.Travel + "\",\"reviewType\":\"owner-check\",\"status\":\"review-due\",\"statusMeaning\":\"due\",\"trigger\":\"cadence\",\"dueOn\":\"2027-03-01\"}", id: "00000000-0000-4000-8000-000000000572");
        JsonTreeNode data = McpHarness.Data(h.Ok(Owner, "record_owner_leave", "{\"principalObjectId\":\"" + McpHarness.Owner + "\",\"leavesOn\":\"2026-12-31\",\"kind\":\"leave\"}"));
        var all = Js.Items(data, "ownerCheckReviewIds").Select(i => i.GetString()).ToList();
        string reused = Assert.Single(Js.Items(data, "reusedOwnerCheckReviewIds")).GetString();
        Assert.Equal("00000000-0000-4000-8000-000000000572", reused);
        Assert.Contains(reused, all);
        Assert.True(all.Count >= 2, "the leaving owner holds roles on both fixture apps");
        Assert.Equal(McpHarness.AsOf.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), h.Store.Rows("review").Single(r => r.Id == reused).Text("dueOn"));
        Assert.DoesNotContain(h.Store.Rows("review"), r => r.Text("dueOn") == "2026-12-31");
    }

    // ---- performedBy and isAiCreated on the existing reads ----

    [Fact]
    public void ExistingReadsReportTheAiMarksAsStampedAndNullForSeededRows()
    {
        McpHarness h = McpHarness.Create();
        JsonTreeNode seeded = Assert.Single(Js.Items(McpHarness.Data(h.Ok(Reader, "find_applications", "{\"text\":\"expense\"}")), "items"));
        Assert.Equal("false", McpHarness.Text(seeded, "isAiCreated"));
        Assert.Equal("null", McpHarness.Text(seeded, "performedBy"));
        Assert.Equal("null", McpHarness.Text(McpHarness.Data(h.Ok(Reader, "get_application_card", "{\"businessApplicationId\":\"" + McpHarness.Expense + "\",\"sections\":[\"decisions\"]}")), "application.performedBy"));

        h.Ok(Owner, "save_application_draft", "{\"name\":\"Fixture leave tracker\",\"purpose\":\"Track team leave\",\"applicationKindKey\":\"power-platform-app\",\"portfolioId\":\"" + McpHarness.Portfolio + "\"}");
        JsonTreeNode drafted = Assert.Single(Js.Items(McpHarness.Data(h.Ok(Reader, "find_applications", "{\"text\":\"leave tracker\",\"registrationMeanings\":[\"draft\"]}")), "items"));
        Assert.Equal("true", McpHarness.Text(drafted, "isAiCreated"));
        Assert.Equal("ai-client-for-person", McpHarness.Text(drafted, "performedBy.actorKind"));
        Assert.Equal(McpHarness.Owner, McpHarness.Text(drafted, "performedBy.onBehalfOf.objectId"));

        string decision = Scenarios.SubmittedRetirement(h);
        JsonTreeNode logged = Assert.Single(Js.Items(McpHarness.Data(h.Ok(Reader, "get_decision_log", "{\"businessApplicationId\":\"" + McpHarness.Travel + "\"}")), "items"));
        Assert.Equal(decision, McpHarness.Text(logged, "decisionId"));
        Assert.Equal(McpHarness.Owner, McpHarness.Text(logged, "proposedBy.objectId"));
        Assert.Equal("true", McpHarness.Text(logged, "isAiCreated"));

        string change = Scenarios.Change(h);
        JsonTreeNode card = McpHarness.Data(h.Ok(Reader, "get_change_request", "{\"changeRequestId\":\"" + change + "\"}"));
        Assert.Equal("true", McpHarness.Text(card, "change.isAiCreated"));
        Assert.Equal(McpHarness.Owner, McpHarness.Text(card, "change.createdBy.objectId"));
    }
}
