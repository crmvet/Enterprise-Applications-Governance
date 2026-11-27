using System.Collections.Generic;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Json;
using Xunit;

namespace Product.Mcp.Core.Tests;

// The update-subject effect through the tools (document 25, B25-8): an accepted decision changes its subject record in
// the same call that accepts it, and the reads show it. The AI drafts and submits; a person answers on the web app's
// decision page (answer_approval) or the approval runner brings a card's answer; the engine accepts and applies.
public sealed class SubjectEffectToolTests
{
    // An owner change the successor and the business owner accept on the decision page assigns the new owner (Balanced
    // aligned with P08, document 21, B21-9): the successor's card asks them by name, and the business owner's card falls
    // back to the sponsor and the portfolio's accountable person, of whom the fixture has neither, so the business owner's
    // holders are asked. The old business owner's assignment ends, the successor holds the role, and the deputy now
    // covers the successor.
    [Fact]
    public void AnOwnerChangeAcceptedAssignsTheOwner()
    {
        McpHarness h = McpHarness.Create();
        JsonTreeNode proposed = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "propose_owner_change", "{\"businessApplicationId\":\"" + McpHarness.Expense + "\",\"changes\":[{\"ownerRoleKey\":\"business-owner\",\"addObjectId\":\""
            + McpHarness.Reader + "\",\"endAssignmentId\":\"" + McpHarness.OwnerAssignment + "\"}],\"reason\":\"The owner moves to the audit team in December.\"}"));
        string decision = McpHarness.Text(proposed, "decisionId");
        Assert.Equal("assign", h.Store.Get("decision", decision)!.Text("outcome"));
        JsonTreeNode submitted = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "submit_record", "{\"subjectKind\":\"decision\",\"subjectId\":\"" + decision + "\"}"));
        IReadOnlyList<JsonTreeNode> requested = Js.Items(submitted, "approvalsRequested");
        Assert.Equal(2, requested.Count);
        JsonTreeNode successorCard = Assert.Single(requested, r => McpHarness.Text(r, "askedPrincipal.objectId") == McpHarness.Reader);
        JsonTreeNode ownerCard = Assert.Single(requested, r => McpHarness.Text(r, "askedPrincipal") == "null");
        Assert.Equal("business-owner", McpHarness.Text(ownerCard, "requiredOwnerRoleKey"));

        // Nothing changes before the answers.
        Assert.Equal(new[] { McpHarness.Owner }, Holders(h, "business-owner", deputy: false));

        // The business owner cannot answer the successor's card: it asks the successor by name.
        h.Fails(McpHarness.TokenOwnerWeb, "answer_approval", "{\"approvalRecordId\":\"" + McpHarness.Text(successorCard, "approvalRecordId") + "\",\"answer\":\"approve\"}", McpErrorCodes.NotAuthorized, server: h.PersonServer());
        JsonTreeNode first = McpHarness.Data(h.Ok(McpHarness.TokenOwnerWeb, "answer_approval", "{\"approvalRecordId\":\"" + McpHarness.Text(ownerCard, "approvalRecordId") + "\",\"answer\":\"approve\"}", server: h.PersonServer()));
        Assert.Equal("proposed", McpHarness.Text(first, "decisionStatus.meaning"));
        JsonTreeNode answered = McpHarness.Data(h.Ok(McpHarness.TokenReaderWeb, "answer_approval", "{\"approvalRecordId\":\"" + McpHarness.Text(successorCard, "approvalRecordId") + "\",\"answer\":\"approve\"}", server: h.PersonServer()));
        Assert.Equal("accepted", McpHarness.Text(answered, "decisionStatus.meaning"));

        Assert.Equal(new[] { McpHarness.Reader }, Holders(h, "business-owner", deputy: false));
        JsonTreeNode owners = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "get_application_owners", "{\"businessApplicationId\":\"" + McpHarness.Expense + "\"}"));
        JsonTreeNode successor = Js.Items(owners, "owners").Single(o => McpHarness.Text(o, "ownerRoleKey") == "business-owner" && McpHarness.Text(o, "isDeputy") == "false");
        Assert.Equal(McpHarness.Reader, McpHarness.Text(successor, "principal.objectId"));
        Assert.Equal(decision, McpHarness.Text(successor, "ownershipDecisionId"));
        JsonTreeNode deputy = Js.Items(owners, "owners").Single(o => McpHarness.Text(o, "ownerRoleKey") == "business-owner" && McpHarness.Text(o, "isDeputy") == "true");
        Assert.Equal(McpHarness.Text(successor, "ownerAssignmentId"), McpHarness.Text(deputy, "deputyFor"));
        StoredRow ended = h.Store.Get("owner-assignment", McpHarness.OwnerAssignment)!;
        Assert.Equal("2026-11-22", ended.Text("endOn"));
        Assert.Equal("engine", ended.Text("performedByActorKind"));
        // The successor's answer was the last one, so the engine acted for the successor.
        Assert.Equal(McpHarness.Reader, ended.Text("onBehalfOf"));
    }

    // An intake for an application the maker registered as a draft (its business owner already named): the platform
    // admin accepts the triage on the decision page, and the engine registers the application, moves it past the Accept
    // gate and completes the request. The reads show it: the intake is completed, and the application is in the register.
    [Fact]
    public void AnIntakeAcceptedRegistersTheApp()
    {
        McpHarness h = McpHarness.Create();
        string app = McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenOwner, "save_application_draft", "{\"name\":\"Fixture visit planner\",\"purpose\":\"Plan store visits on the phone\","
            + "\"applicationKindKey\":\"power-platform-app\",\"portfolioId\":\"" + McpHarness.Portfolio + "\"}")), "recordId");
        Assert.Equal("draft", h.Store.Get("business-application", app)!.Text("statusMeaning"));

        // Test setup: the draft's business owner. Who answers the ownership card of an application with no owner yet is
        // not designed, so that decision is not part of this test.
        h.Store.Seed("owner-assignment", "{\"businessApplication\":\"" + app + "\",\"ownerRole\":\"business-owner\",\"principal\":\"" + McpHarness.Owner + "\",\"isDeputy\":false,\"startOn\":\"2026-11-20\"}", id: "00000000-0000-4000-8000-000000000590");

        string intake = McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenOwner, "save_intake_draft", "{\"title\":\"Visit planner\",\"need\":\"Plan store visits on the phone\",\"proposedKindKey\":\"power-platform-app\",\"businessApplicationId\":\"" + app + "\"}")), "recordId");
        h.Ok(McpHarness.TokenOwner, "answer_tier_questions", "{\"intakeRequestId\":\"" + intake + "\",\"answers\":" + Scenarios.TierAnswers + "}");
        JsonTreeNode submitted = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "submit_record", "{\"subjectKind\":\"intake-request\",\"subjectId\":\"" + intake + "\"}"));
        string decision = McpHarness.Text(submitted, "decisionId");
        Assert.Equal("accept", h.Store.Get("decision", decision)!.Text("outcome"));
        string record = McpHarness.Text(Assert.Single(Js.Items(submitted, "approvalsRequested"), r => McpHarness.Text(r, "isConsultation") == "false"), "approvalRecordId");

        JsonTreeNode answered = McpHarness.Data(h.Ok(McpHarness.TokenAdminWeb, "answer_approval", "{\"approvalRecordId\":\"" + record + "\",\"answer\":\"approve\"}", server: h.PersonServer()));
        Assert.Equal("accepted", McpHarness.Text(answered, "decisionStatus.meaning"));

        JsonTreeNode read = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "get_intake_request", "{\"intakeRequestId\":\"" + intake + "\"}"));
        Assert.Equal("completed", McpHarness.Text(read, "intake.status.meaning"));
        Assert.Equal(decision, McpHarness.Text(read, "intake.triageDecision.decisionId"));
        JsonTreeNode card = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "get_application_card", "{\"businessApplicationId\":\"" + app + "\"}"));
        Assert.Equal("registered", McpHarness.Text(card, "application.status.meaning"));
        Assert.Equal("prioritize-and-fund", McpHarness.Text(card, "application.stage.key"));
        JsonTreeNode found = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "find_applications", "{\"text\":\"visit planner\"}"));
        Assert.Contains(Js.Items(found, "items"), a => McpHarness.Text(a, "businessApplicationId") == app);
        StoredRow registered = h.Store.Get("business-application", app)!;
        Assert.Equal("engine", registered.Text("performedByActorKind"));
        Assert.Equal(decision, registered.Text("stageDecision"));
    }

    // A change the business owner's deputy rejects on the decision page: the decision is rejected and changes nothing
    // else, so the change request stays submitted, and both reads say its authorization was rejected. A change the owner
    // approves moves to in progress (Balanced: Scheduled), and the schedule shows it authorized.
    [Fact]
    public void ARejectedChangeShowsRejectedAndAnAuthorizedOneIsScheduled()
    {
        McpHarness h = McpHarness.Create();
        (string rejectedChange, string rejectedRecord) = SubmittedChange(h, "Remove the export");
        (string authorizedChange, string authorizedRecord) = SubmittedChange(h, "Add an approval step");

        h.Ok(McpHarness.TokenDeputyWeb, "answer_approval", "{\"approvalRecordId\":\"" + rejectedRecord + "\",\"answer\":\"reject\",\"reason\":\"Finance still uses the export.\"}", server: h.PersonServer());
        h.Ok(McpHarness.TokenOwnerWeb, "answer_approval", "{\"approvalRecordId\":\"" + authorizedRecord + "\",\"answer\":\"approve\"}", server: h.PersonServer());

        JsonTreeNode rejected = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "get_change_request", "{\"changeRequestId\":\"" + rejectedChange + "\"}"));
        Assert.Equal("rejected", McpHarness.Text(rejected, "authorization.status.meaning"));
        Assert.Equal("submitted", McpHarness.Text(rejected, "change.status.meaning"));
        JsonTreeNode authorized = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "get_change_request", "{\"changeRequestId\":\"" + authorizedChange + "\"}"));
        Assert.Equal("accepted", McpHarness.Text(authorized, "authorization.status.meaning"));
        Assert.Equal("in-progress", McpHarness.Text(authorized, "change.status.meaning"));

        JsonTreeNode schedule = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "get_change_schedule", "{\"from\":\"2026-11-27\",\"to\":\"2026-11-29\"}"));
        JsonTreeNode rejectedItem = Js.Items(schedule, "items").Single(i => McpHarness.Text(i, "changeRequestId") == rejectedChange);
        Assert.Equal("false", McpHarness.Text(rejectedItem, "authorized"));
        Assert.Equal("rejected", McpHarness.Text(rejectedItem, "authorizationStatus.meaning"));
        Assert.Equal("submitted", McpHarness.Text(rejectedItem, "status.meaning"));
        JsonTreeNode authorizedItem = Js.Items(schedule, "items").Single(i => McpHarness.Text(i, "changeRequestId") == authorizedChange);
        Assert.Equal("true", McpHarness.Text(authorizedItem, "authorized"));
        Assert.Equal("accepted", McpHarness.Text(authorizedItem, "authorizationStatus.meaning"));
        Assert.Equal("in-progress", McpHarness.Text(authorizedItem, "status.meaning"));
    }

    // A tier decision the platform admin proposes through Claude Code with a raised tier, answered by the approval runner
    // from the platform admin's card: the application takes the tier the decision names.
    [Fact]
    public void ARaisedTierAcceptedThroughTheRunnerSetsTheTier()
    {
        McpHarness h = McpHarness.Create();
        JsonTreeNode proposed = McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "save_decision_proposal", "{\"decisionTypeKey\":\"tier\",\"subjectKind\":\"business-application\",\"subjectId\":\"" + McpHarness.Expense
            + "\",\"statement\":\"Raise the expense app to tier 2\",\"context\":\"It now changes shared finance data\",\"proposedOutcomeKey\":\"raise\",\"tierKey\":\"tier-2\",\"reason\":\"Shared data\"}"));
        string decision = McpHarness.Text(proposed, "recordId");
        h.Ok(McpHarness.TokenAdmin, "submit_record", "{\"subjectKind\":\"decision\",\"subjectId\":\"" + decision + "\"}");
        StoredRow card = h.Store.Query(RowQuery.For("approval-record").Where("decision", decision)).Single();

        string answer = "{\"outcome\":\"approval-approved\",\"approver\":\"" + McpHarness.Admin + "\",\"approverActorKind\":\"person\",\"approverPrincipalKind\":\"user\",\"respondedOn\":\"2026-11-22T09:00:00Z\",\"approverHoldsRequiredRole\":true,\"channel\":\"teams\"}";
        Assert.True(h.Runtime.Write(Product.Engine.Runtime.Callers.CallerIdentity.Workload(McpHarness.Runner, clientApp: "approval runner"), Product.Engine.Runtime.WriteCommand.Update("approval-record", card.Id, answer)).Succeeded);

        Assert.Equal("accepted", h.Store.Get("decision", decision)!.Text("statusMeaning"));
        JsonTreeNode read = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "get_application_card", "{\"businessApplicationId\":\"" + McpHarness.Expense + "\"}"));
        Assert.Equal("tier-2", McpHarness.Text(read, "application.tier.key"));
        Assert.Equal(decision, McpHarness.Text(read, "application.tier.decisionId"));

        // tierKey belongs to a tier decision only.
        h.Fails(McpHarness.TokenOwner, "save_decision_proposal", "{\"decisionTypeKey\":\"retirement\",\"subjectKind\":\"business-application\",\"subjectId\":\"" + McpHarness.Expense
            + "\",\"statement\":\"Retire it\",\"context\":\"Fixture\",\"proposedOutcomeKey\":\"complete\",\"tierKey\":\"tier-2\",\"reason\":\"Fixture\"}", McpErrorCodes.Validation);
    }

    // A standard change that fits Balanced's shipped model, once a person approved the model, is accepted by the engine at
    // submit with no card and scheduled, and submit_record names the model and its approving decision (document 25, B25-14).
    // A change outside the model is refused as not standard.
    [Fact]
    public void AStandardChangeThatFitsTheApprovedModelIsScheduledWithNoCard()
    {
        const string Approval = "00000000-0000-4000-8000-0000000009b1";
        McpHarness h = McpHarness.Create("[{\"op\":\"upsert\",\"entity\":\"standard-change-model\",\"key\":\"routine-maintenance\",\"values\":{\"approvedByDecisionId\":\"" + Approval + "\"}}]");
        h.Store.Seed("decision", "{\"decisionType\":\"configuration-change\",\"subjectKind\":\"configuration-draft\",\"statement\":\"List the routine maintenance model\",\"status\":\"decision-accepted\",\"statusMeaning\":\"accepted\"}", id: Approval);

        string change = McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenOwner, "save_change_request_draft", Scenarios.ChangeArguments().Replace("\"normal\"", "\"standard\""))), "recordId");
        JsonTreeNode submitted = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "submit_record", "{\"subjectKind\":\"change-request\",\"subjectId\":\"" + change + "\"}"));
        Assert.Empty(Js.Items(submitted, "approvalsRequested"));
        Assert.Equal("standard-model", McpHarness.Text(submitted, "engineAccepted.condition"));
        Assert.Equal("routine-maintenance", McpHarness.Text(submitted, "standardChangeApplied.modelKey"));
        Assert.Equal(Approval, McpHarness.Text(submitted, "standardChangeApplied.approvedByDecisionId"));
        Assert.Equal("in-progress", h.Store.Get("change-request", change)!.Text("statusMeaning"));

        string tooLong = McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenOwner, "save_change_request_draft", Scenarios.ChangeArguments().Replace("\"normal\"", "\"standard\"").Replace("2026-11-28T08:00:00Z", "2026-11-28T14:00:00Z").Replace("Add an approval step", "A long standard change"))), "recordId");
        h.Fails(McpHarness.TokenOwner, "submit_record", "{\"subjectKind\":\"change-request\",\"subjectId\":\"" + tooLong + "\"}", McpErrorCodes.RuleViolation);
    }

    private static string[] Holders(McpHarness h, string role, bool deputy)
    {
        JsonTreeNode owners = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "get_application_owners", "{\"businessApplicationId\":\"" + McpHarness.Expense + "\"}"));
        return Js.Items(owners, "owners").Where(o => McpHarness.Text(o, "ownerRoleKey") == role && McpHarness.Text(o, "isDeputy") == (deputy ? "true" : "false")).Select(o => McpHarness.Text(o, "principal.objectId")).ToArray();
    }

    private static (string Change, string Record) SubmittedChange(McpHarness h, string title)
    {
        string change = McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenOwner, "save_change_request_draft", Scenarios.ChangeArguments().Replace("Add an approval step", title))), "recordId");
        JsonTreeNode submitted = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "submit_record", "{\"subjectKind\":\"change-request\",\"subjectId\":\"" + change + "\"}"));
        return (change, McpHarness.Text(Assert.Single(Js.Items(submitted, "approvalsRequested")), "approvalRecordId"));
    }
}
