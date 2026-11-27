using System.Linq;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;
using Product.Engine.Runtime.Ports;
using Xunit;

namespace Product.Engine.Runtime.Tests;

// The update-subject effect (document 25, B25-8): when a decision is accepted, the engine writes its effect on the
// subject record in the same transaction, as engine-nested writes stamped by the engine, each naming the decision as its
// basis. The acceptance comes from the approval runner's answer (the card path) or from the engine itself for the locked
// list; a rejected decision changes nothing but itself.
public sealed class SubjectEffectTests
{
    // ---- Ownership ----

    // The business owner hands the role over (Balanced, aligned with P08; document 21, B21-9): the successor accepts on a
    // card that asks them by name, and the business owner's card falls back to the sponsor and then the portfolio's
    // accountable person; with neither on record, the business owner's own holders are asked. Once both cards are
    // approved the engine accepts the decision, ends the old assignment today and starts the successor's, in the last
    // runner call.
    [Fact]
    public void AnAcceptedOwnershipDecisionEndsTheOldOwnerAndStartsTheSuccessor()
    {
        Harness h = Harness.Create();
        string old = h.Store.Rows("owner-assignment").Single(o => Harness.Text(o, "businessApplication") == h.ApplicationId).Id;
        string decision = Harness.Ok(h.Runtime.Write(Harness.PersonOne, WriteCommand.Create("decision",
            "{\"decisionType\":\"ownership\",\"subjectKind\":\"business-application\",\"subjectApplication\":\"" + h.ApplicationId + "\",\"statement\":\"Hand the business owner role to person 3\","
            + "\"outcome\":\"assign\",\"status\":\"decision-proposed\",\"proposedChangesJson\":{\"owners\":[{\"ownerRoleKey\":\"business-owner\",\"addObjectId\":\"" + Harness.Person3 + "\",\"endAssignmentId\":\"" + old + "\"}]}}"))).RowId!;

        (string ownerCard, string successorCard) = OwnershipCards(h, decision);
        StoredRow successor0 = h.Row("approval-record", successorCard);
        Assert.Equal("ownership-proposed-owners", successor0.Text("approvalRule"));
        Assert.Equal("business-owner", successor0.Text("requiredOwnerRole"));
        StoredRow owner0 = h.Row("approval-record", ownerCard);
        Assert.Equal("ownership-business-owner", owner0.Text("approvalRule"));
        Assert.Equal("business-owner", owner0.Text("requiredOwnerRole"));
        Assert.Null(owner0.Text("askedPrincipal"));

        Harness.Ok(h.Answer(ownerCard, "approval-approved", Harness.Person1));
        Assert.Equal("proposed", Harness.Text(h.Row("decision", decision), "statusMeaning"));
        RuntimeOutcome answered = Harness.Ok(h.Answer(successorCard, "approval-approved", Harness.Person3));

        Assert.Equal("accepted", Harness.Text(h.Row("decision", decision), "statusMeaning"));
        StoredRow ended = h.Row("owner-assignment", old);
        Assert.Equal("2026-11-22", Harness.Text(ended, "endOn"));
        Assert.Equal("engine", Harness.Text(ended, "performedByActorKind"));
        StoredRow successor = h.Store.Rows("owner-assignment").Single(o => Harness.Text(o, "principal") == Harness.Person3);
        Assert.Equal("business-owner", Harness.Text(successor, "ownerRole"));
        Assert.Equal(decision, Harness.Text(successor, "ownershipDecision"));
        Assert.Equal("2026-11-22", Harness.Text(successor, "startOn"));
        Assert.False(successor.Flag("isDeputy"));
        Assert.Equal("engine", Harness.Text(successor, "performedByActorKind"));
        Assert.Contains(answered.Writes, w => w.Entity == "owner-assignment" && w.Effect == EffectKind.UpdateSubject && w.IsEngineNested);
        StoredRow audit = h.Store.Rows("audit-entry").Single(a => Harness.Text(a, "operation") == "subject-updated");
        Assert.Equal(decision, RuntimeJsonTestHelpers.Text(audit.Column("changedValuesJson"), "basisDecision"));
    }

    // A rejected ownership decision changes nothing but itself: the owner stays and no assignment starts.
    [Fact]
    public void ARejectedOwnershipDecisionChangesNoOwner()
    {
        Harness h = Harness.Create();
        string old = h.Store.Rows("owner-assignment").Single().Id;
        string decision = Harness.Ok(h.Runtime.Write(Harness.PersonOne, WriteCommand.Create("decision",
            "{\"decisionType\":\"ownership\",\"subjectKind\":\"business-application\",\"subjectApplication\":\"" + h.ApplicationId + "\",\"statement\":\"Hand the business owner role to person 3\","
            + "\"outcome\":\"assign\",\"status\":\"decision-proposed\",\"proposedChangesJson\":{\"owners\":[{\"ownerRoleKey\":\"business-owner\",\"addObjectId\":\"" + Harness.Person3 + "\",\"endAssignmentId\":\"" + old + "\"}]}}"))).RowId!;
        (string ownerCard, _) = OwnershipCards(h, decision);

        Harness.Ok(h.Answer(ownerCard, "approval-rejected", Harness.Person1, typedReason: "Not before the audit"));

        Assert.Equal("rejected", Harness.Text(h.Row("decision", decision), "statusMeaning"));
        Assert.Single(h.Store.Rows("owner-assignment"));
        Assert.Null(h.Row("owner-assignment", old).Text("endOn"));
        Assert.DoesNotContain(h.Store.Rows("audit-entry"), a => Harness.Text(a, "operation") == "subject-updated");
    }

    // ---- Intake ----

    // A lowest-tier intake for a draft application that has its business owner: the engine accepts it with no card (the
    // locked list), registers the application, moves it past the Accept gate, and completes the request.
    [Fact]
    public void AnIntakeTheEngineAcceptsRegistersTheApplication()
    {
        Harness h = Harness.Create();
        string app = h.Store.Seed("business-application", "{\"name\":\"Fixture visit planner\",\"purpose\":\"Plan store visits\",\"applicationKind\":\"power-platform-app\","
            + "\"status\":\"app-draft\",\"statusMeaning\":\"draft\",\"lifecycleStage\":\"idea\",\"stageSince\":\"2026-11-20T09:00:00Z\",\"configurationSetVersion\":1}");
        h.Store.Seed("owner-assignment", "{\"businessApplication\":\"" + app + "\",\"ownerRole\":\"business-owner\",\"principal\":\"" + Harness.Person1 + "\",\"isDeputy\":false,\"startOn\":\"2026-11-20\"}");
        string intake = h.Store.Seed("intake-request", "{\"requester\":\"" + Harness.Person1 + "\",\"title\":\"Visit planner\",\"need\":\"Plan store visits\",\"proposedKind\":\"power-platform-app\","
            + "\"businessApplication\":\"" + app + "\",\"status\":\"intake-submitted\",\"statusMeaning\":\"submitted\",\"configurationSetVersion\":1}");
        Harness.Ok(h.Runtime.Write(Harness.AiClientOf1, WriteCommand.Create("tier-assessment", "{\"intakeRequest\":\"" + intake + "\",\"answersJson\":" + Harness.Answers(tierTwo: false) + ",\"assessedBy\":\"" + Harness.Person1 + "\"}")));
        string decision = Harness.Ok(h.Runtime.Write(Harness.AiClientOf1, WriteCommand.Create("decision",
            "{\"decisionType\":\"intake\",\"subjectKind\":\"intake-request\",\"subjectIntakeRequest\":\"" + intake + "\",\"statement\":\"Accept the visit planner intake\",\"outcome\":\"accept\",\"status\":\"decision-proposed\"}"))).RowId!;

        RuntimeOutcome request = Harness.Ok(h.Runtime.RequestApprovals(Harness.AiClientOf1, decision));

        Assert.True(RuntimeJsonTestHelpers.Flag(request.Payload, "accepted"), request.ToString());
        StoredRow registered = h.Row("business-application", app);
        Assert.Equal("registered", Harness.Text(registered, "statusMeaning"));
        Assert.Equal("app-registered", Harness.Text(registered, "status"));
        Assert.Equal("prioritize-and-fund", Harness.Text(registered, "lifecycleStage"));
        Assert.Equal(decision, Harness.Text(registered, "stageDecision"));
        Assert.Equal("2026-11-22T09:00:00Z", Harness.Text(registered, "stageSince"));
        Assert.Equal("engine", Harness.Text(registered, "performedByActorKind"));
        StoredRow completed = h.Row("intake-request", intake);
        Assert.Equal("completed", Harness.Text(completed, "statusMeaning"));
        Assert.Equal(decision, Harness.Text(completed, "triageDecision"));
    }

    // An intake for a new application, with a configuration whose Accept gate asks for no business owner: the accepted
    // intake creates the application, registered, from the request, and links the request to it.
    [Fact]
    public void AnAcceptedIntakeForANewApplicationCreatesIt()
    {
        Harness h = Harness.Create("[{\"op\":\"retire\",\"entity\":\"gate-requirement\",\"key\":\"accept-business-owner\"}]");
        string decision = h.IntakeDecision(tierTwo: true, linkApplication: false);
        string intake = Harness.Text(h.Row("decision", decision), "subjectIntakeRequest");
        Harness.Ok(h.Runtime.Write(Harness.PersonOne, WriteCommand.Update("decision", decision, "{\"outcome\":\"accept\"}")));
        int before = h.Store.Rows("business-application").Count;
        string record = h.CountingRecord(Harness.Ok(h.Runtime.RequestApprovals(Harness.AiClientOf1, decision)).Payload);

        Harness.Ok(h.Answer(record, "approval-approved", Harness.Person2));

        Assert.Equal("accepted", Harness.Text(h.Row("decision", decision), "statusMeaning"));
        Assert.Equal(before + 1, h.Store.Rows("business-application").Count);
        string app = Harness.Text(h.Row("intake-request", intake), "businessApplication");
        StoredRow created = h.Row("business-application", app);
        Assert.Equal("registered", Harness.Text(created, "statusMeaning"));
        Assert.Equal("Fixture travel requests", Harness.Text(created, "name"));
        Assert.Equal("Replace the shared spreadsheet", Harness.Text(created, "purpose"));
        Assert.Equal("power-platform-app", Harness.Text(created, "applicationKind"));
        Assert.Equal("prioritize-and-fund", Harness.Text(created, "lifecycleStage"));
        Assert.Equal("engine", Harness.Text(created, "performedByActorKind"));
        Assert.Equal("completed", Harness.Text(h.Row("intake-request", intake), "statusMeaning"));
    }

    // ---- Change authorization ----

    // The business owner authorizes a normal change on the card: the change moves to in progress (Balanced: Scheduled).
    // Another change the owner rejects stays submitted, and only its decision says rejected.
    [Fact]
    public void AnAuthorizedChangeIsScheduledAndARejectedOneIsNotMoved()
    {
        Harness h = Harness.Create();
        (string authorized, string first) = SubmittedChange(h, "Add an approval step");
        (string rejected, string second) = SubmittedChange(h, "Remove the export");

        Harness.Ok(h.Answer(RecordOf(h, first), "approval-approved", Harness.Person1));
        Harness.Ok(h.Answer(RecordOf(h, second), "approval-rejected", Harness.Person1, typedReason: "Finance still uses the export"));

        Assert.Equal("accepted", Harness.Text(h.Row("decision", first), "statusMeaning"));
        StoredRow scheduled = h.Row("change-request", authorized);
        Assert.Equal("in-progress", Harness.Text(scheduled, "statusMeaning"));
        Assert.Equal("change-in-progress", Harness.Text(scheduled, "status"));
        Assert.Equal(first, Harness.Text(scheduled, "authorizationDecision"));
        Assert.Equal("engine", Harness.Text(scheduled, "performedByActorKind"));

        Assert.Equal("rejected", Harness.Text(h.Row("decision", second), "statusMeaning"));
        StoredRow notMoved = h.Row("change-request", rejected);
        Assert.Equal("submitted", Harness.Text(notMoved, "statusMeaning"));
        Assert.Equal("person", Harness.Text(notMoved, "performedByActorKind"));
    }

    // ---- Tier ----

    // A tier decision about the application, confirmed on the platform admin's card: the application takes the tier its
    // latest answers computed, and the assessment takes the decision as its Tier Decision (and is then final).
    [Fact]
    public void AConfirmedTierDecisionSetsTheComputedTier()
    {
        Harness h = Harness.Create();
        string assessment = Harness.Ok(h.Runtime.Write(Harness.PersonOne, WriteCommand.Create("tier-assessment",
            "{\"businessApplication\":\"" + h.ApplicationId + "\",\"answersJson\":" + Harness.Answers(tierTwo: true) + ",\"assessedBy\":\"" + Harness.Person1 + "\"}"))).RowId!;
        string decision = Harness.Ok(h.Runtime.Write(Harness.PersonOne, WriteCommand.Create("decision",
            "{\"decisionType\":\"tier\",\"subjectKind\":\"business-application\",\"subjectApplication\":\"" + h.ApplicationId + "\",\"statement\":\"Confirm tier 2\",\"outcome\":\"confirm\",\"status\":\"decision-proposed\"}"))).RowId!;
        string record = h.CountingRecord(Harness.Ok(h.Runtime.RequestApprovals(Harness.PersonOne, decision)).Payload);

        Harness.Ok(h.Answer(record, "approval-approved", Harness.Person2));

        StoredRow app = h.Row("business-application", h.ApplicationId);
        Assert.Equal("tier-2", Harness.Text(app, "tier"));
        Assert.Equal(decision, Harness.Text(app, "tierDecision"));
        Assert.Equal(decision, Harness.Text(h.Row("tier-assessment", assessment), "tierDecision"));
        RuntimeOutcome frozen = h.Runtime.Write(Harness.PersonOne, WriteCommand.Update("tier-assessment", assessment, "{\"answersJson\":" + Harness.Answers(tierTwo: false) + "}"));
        Assert.False(frozen.Succeeded);
        Assert.Contains(frozen.Reasons, r => r.Code == ReasonCodes.RecordFinal);
    }

    // ---- Retirement ----

    // The business owner approves a retirement: the application moves to the retired stage (the outcome's Next Lifecycle
    // Stage), and the reviews still due are cancelled.
    [Fact]
    public void AnAcceptedRetirementMovesTheStageAndCancelsDueReviews()
    {
        Harness h = Harness.Create();
        string review = h.Store.Seed("review", "{\"businessApplication\":\"" + h.ApplicationId + "\",\"reviewType\":\"owner-check\",\"status\":\"review-due\",\"statusMeaning\":\"due\",\"dueOn\":\"2026-12-15\"}");
        string decision = Harness.Ok(h.Runtime.Write(Harness.PersonOne, WriteCommand.Create("decision",
            "{\"decisionType\":\"retirement\",\"subjectKind\":\"business-application\",\"subjectApplication\":\"" + h.ApplicationId + "\",\"statement\":\"Retire the expense app\",\"outcome\":\"complete\",\"status\":\"decision-proposed\"}"))).RowId!;
        string record = h.CountingRecord(Harness.Ok(h.Runtime.RequestApprovals(Harness.PersonOne, decision)).Payload);

        Harness.Ok(h.Answer(record, "approval-approved", Harness.Person1));

        StoredRow app = h.Row("business-application", h.ApplicationId);
        Assert.Equal("retire", Harness.Text(app, "lifecycleStage"));
        Assert.Equal(decision, Harness.Text(app, "stageDecision"));
        Assert.Equal("cancelled", Harness.Text(h.Row("review", review), "statusMeaning"));
    }

    // ---- Helpers ----

    // The two counting cards of an ownership decision: the business owner's (or its fallback's) and the proposed owner's.
    internal static (string OwnerCard, string SuccessorCard) OwnershipCards(Harness h, string decision)
    {
        string[] records = RuntimeJsonTestHelpers.Strings(Harness.Ok(h.Runtime.RequestApprovals(Harness.PersonOne, decision)).Payload, "approvalRecordIds");
        Assert.Equal(2, records.Length);
        string successor = records.Single(r => h.Row("approval-record", r).Text("approvalRule") == "ownership-proposed-owners");
        return (records.Single(r => r != successor), successor);
    }

    private static (string Change, string Decision) SubmittedChange(Harness h, string title)
    {
        string change = Harness.Ok(h.Runtime.Write(Harness.PersonOne, WriteCommand.Create("change-request",
            "{\"title\":\"" + title + "\",\"businessApplication\":\"" + h.ApplicationId + "\",\"changeType\":\"normal\",\"status\":\"change-submitted\",\"mode\":\"built-in\","
            + "\"windowStart\":\"2026-11-28T06:00:00Z\",\"windowEnd\":\"2026-11-28T08:00:00Z\",\"plannedTarget\":\"" + h.TargetId + "\"}"))).RowId!;
        string decision = Harness.Ok(h.Runtime.Write(Harness.PersonOne, WriteCommand.Create("decision",
            "{\"decisionType\":\"change-authorization\",\"subjectKind\":\"change-request\",\"subjectChangeRequest\":\"" + change + "\",\"subjectApplication\":\"" + h.ApplicationId + "\","
            + "\"statement\":\"Authorize the change: " + title + "\",\"outcome\":\"authorize\",\"status\":\"decision-proposed\"}"))).RowId!;
        Harness.Ok(h.Runtime.Write(Harness.PersonOne, WriteCommand.Update("change-request", change, "{\"authorizationDecision\":\"" + decision + "\"}")));
        Harness.Ok(h.Runtime.RequestApprovals(Harness.PersonOne, decision));
        return (change, decision);
    }

    private static string RecordOf(Harness h, string decision)
    {
        return h.Store.Rows("approval-record").Single(r => Harness.Text(r, "decision") == decision && Harness.Text(r, "outcomeMeaning") == "pending").Id;
    }
}
