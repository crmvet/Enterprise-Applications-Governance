using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Reasons;
using Product.Engine.Runtime.Callers;
using Product.Engine.Runtime.Ports;
using Xunit;

namespace Product.Engine.Runtime.Tests;

// Who an Approval Record asks (document 25, B25-14; document 21, B21-9; Balanced aligned with the process documents):
// each proposed owner by name, the business owner's card falling back to the sponsor and then the portfolio's accountable
// person when the business owner hands the role over, the business owner an intake names for a request with no
// application yet, and the tier at stake for a tier decision.
public sealed class ApproverRoutingTests
{
    // P08: when the business owner hands the role over, the sponsor answers the business owner's card.
    [Fact]
    public void TheSponsorAnswersWhenTheBusinessOwnerHandsTheRoleOver()
    {
        Harness h = Harness.Create();
        h.Store.Seed("owner-assignment", "{\"businessApplication\":\"" + h.ApplicationId + "\",\"ownerRole\":\"sponsor\",\"principal\":\"" + Harness.Person2 + "\",\"isDeputy\":false,\"startOn\":\"2026-01-05\"}");
        string decision = HandOver(h);

        (string ownerCard, string successorCard) = SubjectEffectTests.OwnershipCards(h, decision);
        StoredRow owner = h.Row("approval-record", ownerCard);
        Assert.Equal("sponsor", owner.Text("requiredOwnerRole"));
        Assert.Null(owner.Text("askedPrincipal"));
        Assert.Equal(Harness.Person3, h.Row("approval-record", successorCard).Text("askedPrincipal"));

        Harness.Ok(h.Answer(ownerCard, "approval-approved", Harness.Person2));
        Harness.Ok(h.Answer(successorCard, "approval-approved", Harness.Person3));
        Assert.Equal("accepted", Harness.Text(h.Row("decision", decision), "statusMeaning"));
    }

    // P08: with no sponsor, the portfolio's Accountable Principal is asked by name.
    [Fact]
    public void ThePortfoliosAccountablePersonIsAskedByNameWhenNoSponsorIsLeft()
    {
        Harness h = Harness.Create();
        StoredRow portfolio = h.Row("portfolio", h.PortfolioId);
        h.Store.Tamper("portfolio", h.PortfolioId, CanonicalJson.Parse("{\"level\":\"department\",\"isActive\":true,\"accountablePrincipal\":\"" + Harness.Person2 + "\"}"));
        Assert.NotNull(portfolio);
        string decision = HandOver(h);

        (string ownerCard, _) = SubjectEffectTests.OwnershipCards(h, decision);
        StoredRow owner = h.Row("approval-record", ownerCard);
        Assert.Equal("business-owner", owner.Text("requiredOwnerRole"));
        Assert.Equal(Harness.Person2, owner.Text("askedPrincipal"));
    }

    // A record that asks a person by name is that person's only: the business owner, who holds the role the successor
    // would take, is refused, and the successor, who holds no role at all, may answer. Nothing is written by the check.
    [Fact]
    public void OnlyThePersonARecordNamesMayAnswerIt()
    {
        Harness h = Harness.Create();
        string decision = HandOver(h);
        (_, string successorCard) = SubjectEffectTests.OwnershipCards(h, decision);
        string before = h.Store.Dump();

        RuntimeOutcome owner = h.Runtime.AnswerApproval(CallerIdentity.Person(Harness.Person1, null, "Product page"), new ApprovalAnswerCommand(successorCard, approve: true, reason: "Fine"));
        Assert.False(owner.Succeeded);
        Assert.True(owner.HasReason(ReasonCodes.ApprovalAnswerNotAsked), owner.ToString());
        Assert.Equal(before, h.Store.Dump());

        RuntimeOutcome successor = Harness.Ok(h.Runtime.AnswerApproval(CallerIdentity.Person(Harness.Person3, null, "Product page"), new ApprovalAnswerCommand(successorCard, approve: true, reason: "I take it on")));
        Assert.Equal("approved", RuntimeJsonTestHelpers.Text(successor.Payload, "outcome"));
        Assert.True(h.Row("approval-record", successorCard).Flag("countsTowardRequirement"));
    }

    // A proposed owner's own yes is consent, not a check on the proposal: the successor who proposed the decision on a tier
    // 3 application still counts, while no-self-approval-tier-3 keeps the business owner's card for someone else.
    [Fact]
    public void AProposedOwnersConsentIsNotSelfApproval()
    {
        Harness h = Harness.Create();
        Raise(h, "tier-3");
        string old = h.Store.Rows("owner-assignment").Single().Id;
        string decision = Harness.Ok(h.Runtime.Write(CallerIdentity.Person(Harness.Person3, null, "Product page"), WriteCommand.Create("decision",
            "{\"decisionType\":\"ownership\",\"subjectKind\":\"business-application\",\"subjectApplication\":\"" + h.ApplicationId + "\",\"statement\":\"Person 3 takes the business owner role\","
            + "\"outcome\":\"assign\",\"status\":\"decision-proposed\",\"proposedChangesJson\":{\"owners\":[{\"ownerRoleKey\":\"business-owner\",\"addObjectId\":\"" + Harness.Person3 + "\",\"endAssignmentId\":\"" + old + "\"}]}}"))).RowId!;
        (_, string successorCard) = SubjectEffectTests.OwnershipCards(h, decision);

        Harness.Ok(h.Answer(successorCard, "approval-approved", Harness.Person3));
        StoredRow answered = h.Row("approval-record", successorCard);
        Assert.True(answered.Flag("countsTowardRequirement"));
        Assert.Equal("proposed", Harness.Text(h.Row("decision", decision), "statusMeaning"));
    }

    // P01 step 8: the sponsor holds the priority card, and a request for a new application has no sponsor yet, so the card
    // asks the business owner the intake names, by name.
    [Fact]
    public void APriorityCardOfARequestWithNoApplicationAsksTheBusinessOwnerItNames()
    {
        Harness h = Harness.Create();
        string intake = Harness.Ok(h.Runtime.Write(Harness.AiClientOf1, WriteCommand.Create("intake-request",
            "{\"requester\":\"" + Harness.Person1 + "\",\"businessOwner\":\"" + Harness.Person3 + "\",\"title\":\"Fixture overtime tracker\",\"need\":\"Track crew overtime\",\"proposedKind\":\"power-platform-app\",\"status\":\"intake-submitted\"}"))).RowId!;
        string decision = Harness.Ok(h.Runtime.Write(Harness.PersonOne, WriteCommand.Create("decision",
            "{\"decisionType\":\"priority\",\"subjectKind\":\"intake-request\",\"subjectIntakeRequest\":\"" + intake + "\",\"statement\":\"Fund the overtime tracker\",\"outcome\":\"fund\",\"status\":\"decision-proposed\"}"))).RowId!;

        string record = Assert.Single(RuntimeJsonTestHelpers.Strings(Harness.Ok(h.Runtime.RequestApprovals(Harness.PersonOne, decision)).Payload, "approvalRecordIds"));
        StoredRow card = h.Row("approval-record", record);
        Assert.Equal("priority-a", card.Text("decisionRight"));
        Assert.Equal("business-owner", card.Text("requiredOwnerRole"));
        Assert.Equal(Harness.Person3, card.Text("askedPrincipal"));
    }

    // P02: a tier decision takes the tier at stake. Raising a tier 1 application to tier 3 asks the platform admin and the
    // security reviewer; confirming it at tier 2 asks the platform admin alone.
    [Theory]
    [InlineData("tier-3", new[] { "platform-admin", "security-reviewer" })]
    [InlineData("tier-2", new[] { "platform-admin" })]
    public void ATierDecisionTakesTheApprovalsOfTheTierAtStake(string proposedTier, string[] roles)
    {
        Harness h = Harness.Create();
        string decision = Harness.Ok(h.Runtime.Write(Harness.PersonOne, WriteCommand.Create("decision",
            "{\"decisionType\":\"tier\",\"subjectKind\":\"business-application\",\"subjectApplication\":\"" + h.ApplicationId + "\",\"statement\":\"Raise the tier\","
            + "\"outcome\":\"raise\",\"status\":\"decision-proposed\",\"proposedChangesJson\":{\"tierKey\":\"" + proposedTier + "\"}}"))).RowId!;

        string[] records = RuntimeJsonTestHelpers.Strings(Harness.Ok(h.Runtime.RequestApprovals(Harness.PersonOne, decision)).Payload, "approvalRecordIds");
        Assert.Equal(roles, records.Select(r => h.Row("approval-record", r).Text("requiredRole")).OrderBy(r => r).ToArray());
    }

    // P03 and Balanced's shipped model (document 21, B21-9): a standard change that fits an approved model is accepted by
    // the engine with no card, with the model as its basis, and the change is scheduled; one outside every model is
    // refused as not standard. The runtime gives OP-02 the OP-12 result (document 25, B25-14).
    [Theory]
    [InlineData("2026-11-28T08:00:00Z", true)]
    [InlineData("2026-11-28T12:00:00Z", false)]
    public void AStandardChangeIsAcceptedOnlyThroughAnApprovedModelItFits(string windowEnd, bool fits)
    {
        const string Approval = "00000000-0000-4000-8000-0000000009a1";
        Harness h = Harness.Create("[{\"op\":\"upsert\",\"entity\":\"standard-change-model\",\"key\":\"routine-maintenance\",\"values\":{\"approvedByDecisionId\":\"" + Approval + "\"}}]");
        h.Store.Seed("decision", "{\"decisionType\":\"configuration-change\",\"subjectKind\":\"configuration-draft\",\"statement\":\"List the routine maintenance model\",\"status\":\"decision-accepted\",\"statusMeaning\":\"accepted\"}", id: Approval);
        string change = Harness.Ok(h.Runtime.Write(Harness.PersonOne, WriteCommand.Create("change-request",
            "{\"title\":\"Rotate a connection\",\"businessApplication\":\"" + h.ApplicationId + "\",\"changeType\":\"standard\",\"status\":\"change-submitted\",\"mode\":\"built-in\","
            + "\"windowStart\":\"2026-11-28T06:00:00Z\",\"windowEnd\":\"" + windowEnd + "\",\"plannedTarget\":\"" + h.TargetId + "\"}"))).RowId!;
        string decision = Harness.Ok(h.Runtime.Write(Harness.PersonOne, WriteCommand.Create("decision",
            "{\"decisionType\":\"change-authorization\",\"subjectKind\":\"change-request\",\"subjectChangeRequest\":\"" + change + "\",\"subjectApplication\":\"" + h.ApplicationId + "\","
            + "\"statement\":\"Authorize the change: Rotate a connection\",\"outcome\":\"authorize\",\"status\":\"decision-proposed\"}"))).RowId!;
        Harness.Ok(h.Runtime.Write(Harness.PersonOne, WriteCommand.Update("change-request", change, "{\"authorizationDecision\":\"" + decision + "\"}")));

        RuntimeOutcome requested = h.Runtime.RequestApprovals(Harness.PersonOne, decision);
        if (fits)
        {
            Assert.True(requested.Succeeded, requested.ToString());
            Assert.Equal("engine-accept", RuntimeJsonTestHelpers.Text(requested.Payload, "outcome"));
            Assert.Equal("accepted", Harness.Text(h.Row("decision", decision), "statusMeaning"));
            Assert.Equal("in-progress", Harness.Text(h.Row("change-request", change), "statusMeaning"));
            Assert.Empty(h.Store.Rows("approval-record"));
        }
        else
        {
            Assert.False(requested.Succeeded);
            Assert.True(requested.HasReason(ReasonCodes.StandardModelDoesNotQualify), requested.ToString());
            Assert.Equal("proposed", Harness.Text(h.Row("decision", decision), "statusMeaning"));
        }
    }

    // ---- Helpers ----

    // An ownership decision that hands the business owner role from person 1 to person 3.
    private static string HandOver(Harness h)
    {
        string old = h.Store.Rows("owner-assignment").Single(o => Harness.Text(o, "ownerRole") == "business-owner").Id;
        return Harness.Ok(h.Runtime.Write(Harness.PersonOne, WriteCommand.Create("decision",
            "{\"decisionType\":\"ownership\",\"subjectKind\":\"business-application\",\"subjectApplication\":\"" + h.ApplicationId + "\",\"statement\":\"Hand the business owner role to person 3\","
            + "\"outcome\":\"assign\",\"status\":\"decision-proposed\",\"proposedChangesJson\":{\"owners\":[{\"ownerRoleKey\":\"business-owner\",\"addObjectId\":\"" + Harness.Person3 + "\",\"endAssignmentId\":\"" + old + "\"}]}}"))).RowId!;
    }

    // Sets the application's tier outside the engine, as a seed would.
    private static void Raise(Harness h, string tier)
    {
        StoredRow app = h.Row("business-application", h.ApplicationId);
        h.Store.Tamper("business-application", h.ApplicationId, CanonicalJson.Parse(
            "{\"recordNumber\":\"APP-0001\",\"purpose\":\"Fixture expense approvals\",\"applicationKind\":\"power-platform-app\",\"portfolio\":\"" + h.PortfolioId
            + "\",\"status\":\"app-registered\",\"statusMeaning\":\"registered\",\"tier\":\"" + tier + "\",\"lifecycleStage\":\"operate\",\"stageSince\":\"2026-01-05T00:00:00Z\",\"configurationSetVersion\":1}"));
        Assert.NotNull(app);
    }
}
