using System.Linq;
using Product.Engine.Core.Reasons;
using Product.Engine.Runtime.Ports;
using Xunit;

namespace Product.Engine.Runtime.Tests;

// The approver kind rule end to end on the in-memory store (document 22, O10: Approver Principal Kind is the responder's
// directory object type, copied from the Principal Reference). The runtime fetches the Principal Reference of the
// Approver for the guard, so an answer a runner reports as a user's never counts when the directory knows the responder
// as an agent user.
public sealed class ApproverPrincipalReferenceTests
{
    // A fictional agent user that answers an approval card, known to the directory as an agent user.
    private const string AgentUser = "00000000-0000-4000-8000-0000000004ab";

    [Fact]
    public void AnAgentUserReferenceKeepsTheDecisionProposed()
    {
        Harness h = Harness.Create();
        h.Store.Seed("principal-reference", "{\"principalKind\":\"agent-user\",\"entraObjectId\":\"" + AgentUser + "\",\"displayLabel\":\"Fixture agent-user 4ab\",\"isErased\":false}");
        string decision = h.IntakeDecision(tierTwo: true);
        string record = RuntimeJsonTestHelpers.Strings(Harness.Ok(h.Runtime.RequestApprovals(Harness.AiClientOf1, decision)).Payload, "approvalRecordIds").Single();

        // The runner says person and user; the directory says agent user.
        RuntimeOutcome answer = Harness.Ok(h.Answer(record, "approval-approved", AgentUser));
        Assert.True(answer.HasReason(ReasonCodes.ApprovalNonUserPrincipal), answer.ToString());

        StoredRow answered = h.Row("approval-record", record);
        Assert.Equal("recommendation-only", Harness.Text(answered, "outcomeMeaning"));
        Assert.Equal("agent-user", Harness.Text(answered, "approverPrincipalKind"));
        Assert.False(answered.Flag("countsTowardRequirement"));
        Assert.Equal("proposed", Harness.Text(h.Row("decision", decision), "statusMeaning"));
    }

    // The object ID is matched ignoring case: a runner that sends it in capitals still finds the reference.
    [Fact]
    public void TheReferenceIsFoundWhateverTheLetterCase()
    {
        Harness h = Harness.Create();
        h.Store.Seed("principal-reference", "{\"principalKind\":\"agent-user\",\"entraObjectId\":\"" + AgentUser + "\",\"displayLabel\":\"Fixture agent-user 4ab\",\"isErased\":false}");
        string decision = h.IntakeDecision(tierTwo: true);
        string record = RuntimeJsonTestHelpers.Strings(Harness.Ok(h.Runtime.RequestApprovals(Harness.AiClientOf1, decision)).Payload, "approvalRecordIds").Single();

        Harness.Ok(h.Answer(record, "approval-approved", AgentUser.ToUpperInvariant()));
        Assert.Equal("recommendation-only", Harness.Text(h.Row("approval-record", record), "outcomeMeaning"));
        Assert.Equal("agent-user", Harness.Text(h.Row("approval-record", record), "approverPrincipalKind"));
        Assert.Equal("proposed", Harness.Text(h.Row("decision", decision), "statusMeaning"));
    }

    // A user reference never upgrades a kind the runner reported: an agent user's answer stays recommendation only.
    [Fact]
    public void AUserReferenceNeverUpgradesTheRunnersKind()
    {
        Harness h = Harness.Create();
        string decision = h.IntakeDecision(tierTwo: true);
        string record = RuntimeJsonTestHelpers.Strings(Harness.Ok(h.Runtime.RequestApprovals(Harness.AiClientOf1, decision)).Payload, "approvalRecordIds").Single();

        Harness.Ok(h.Answer(record, "approval-approved", Harness.Person2, principalKind: "agent-user"));
        StoredRow answered = h.Row("approval-record", record);
        Assert.Equal("recommendation-only", Harness.Text(answered, "outcomeMeaning"));
        Assert.Equal("agent-user", Harness.Text(answered, "approverPrincipalKind"));
        Assert.Equal("proposed", Harness.Text(h.Row("decision", decision), "statusMeaning"));
    }

    // A user with a user reference counts, as before.
    [Fact]
    public void AUserWithAUserReferenceCounts()
    {
        Harness h = Harness.Create();
        string decision = h.IntakeDecision(tierTwo: true);
        string record = RuntimeJsonTestHelpers.Strings(Harness.Ok(h.Runtime.RequestApprovals(Harness.AiClientOf1, decision)).Payload, "approvalRecordIds").Single();

        RuntimeOutcome answer = Harness.Ok(h.Answer(record, "approval-approved", Harness.Person2));
        Assert.False(answer.HasReason(ReasonCodes.ApprovalNonUserPrincipal));
        StoredRow answered = h.Row("approval-record", record);
        Assert.Equal("approved", Harness.Text(answered, "outcomeMeaning"));
        Assert.Equal("user", Harness.Text(answered, "approverPrincipalKind"));
        Assert.True(answered.Flag("countsTowardRequirement"));
        Assert.Equal("accepted", Harness.Text(h.Row("decision", decision), "statusMeaning"));
    }

    // A responder the directory does not know keeps the runner's kind, as before the fact existed.
    [Fact]
    public void AResponderWithNoReferenceKeepsTheRunnersKind()
    {
        Harness h = Harness.Create();
        string decision = h.IntakeDecision(tierTwo: true);
        string record = RuntimeJsonTestHelpers.Strings(Harness.Ok(h.Runtime.RequestApprovals(Harness.AiClientOf1, decision)).Payload, "approvalRecordIds").Single();

        Harness.Ok(h.Answer(record, "approval-approved", "00000000-0000-4000-8000-0000000002ff"));
        StoredRow answered = h.Row("approval-record", record);
        Assert.Equal("approved", Harness.Text(answered, "outcomeMeaning"));
        Assert.Equal("user", Harness.Text(answered, "approverPrincipalKind"));
    }
}
