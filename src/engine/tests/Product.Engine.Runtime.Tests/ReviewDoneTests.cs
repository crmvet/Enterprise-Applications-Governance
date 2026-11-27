using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;
using Xunit;

namespace Product.Engine.Runtime.Tests;

// A review set done (document 25, B25-12): the runtime fetches the outcome decision the review names (facts.reviewOutcome)
// for OP-08, which lets a person end the review only once that decision is accepted, and never lets an AI actor set done.
public sealed class ReviewDoneTests
{
    private const string Decision = "00000000-0000-4000-8000-000000000641";
    private const string Review = "00000000-0000-4000-8000-000000000642";

    [Fact]
    public void AReviewIsDoneOnlyOnceItsOutcomeDecisionIsAccepted()
    {
        Harness h = Harness.Create();
        h.Store.Seed("decision", "{\"decisionType\":\"design\",\"subjectKind\":\"business-application\",\"subjectApplication\":\"" + h.ApplicationId
            + "\",\"statement\":\"Keep the photos in the claims system\",\"outcome\":\"approve\",\"status\":\"decision-proposed\",\"statusMeaning\":\"proposed\"}", id: Decision);
        h.Store.Seed("review", "{\"businessApplication\":\"" + h.ApplicationId + "\",\"reviewType\":\"architecture\",\"status\":\"review-due\",\"statusMeaning\":\"due\",\"trigger\":\"request\",\"dueOn\":\"2026-11-30\"}", id: Review);
        string done = "{\"status\":\"review-done\",\"doneOn\":\"2026-11-22\",\"reviewer\":\"" + Harness.Person1 + "\",\"outcomeDecision\":\"" + Decision + "\"}";

        RuntimeOutcome proposed = h.Runtime.Write(Harness.PersonOne, WriteCommand.Update("review", Review, done));
        Assert.Equal("refused", proposed.Outcome);
        Assert.Contains(proposed.Reasons, r => r.Code == ReasonCodes.ReviewOutcomeNotAccepted && r.Ref == Decision);
        Assert.Equal("due", Harness.Text(h.Row("review", Review), "statusMeaning"));

        h.Store.Seed("decision", "{\"decisionType\":\"design\",\"subjectKind\":\"business-application\",\"subjectApplication\":\"" + h.ApplicationId
            + "\",\"statement\":\"Keep the photos in the claims system\",\"outcome\":\"approve\",\"status\":\"decision-accepted\",\"statusMeaning\":\"accepted\"}", id: Decision);
        RuntimeOutcome byAi = h.Runtime.Write(Harness.AiClientOf1, WriteCommand.Update("review", Review, done));
        Assert.Equal("refused", byAi.Outcome);
        Assert.Contains(byAi.Reasons, r => r.Code == ReasonCodes.AiStatusNotAllowed);

        Harness.Ok(h.Runtime.Write(Harness.PersonOne, WriteCommand.Update("review", Review, done)));
        Assert.Equal("done", Harness.Text(h.Row("review", Review), "statusMeaning"));
        Assert.Equal(Harness.Person1, Harness.Text(h.Row("review", Review), "reviewer"));

        // Done stays done.
        RuntimeOutcome reopened = h.Runtime.Write(Harness.PersonOne, WriteCommand.Update("review", Review, "{\"status\":\"review-due\"}"));
        Assert.Equal("refused", reopened.Outcome);
        Assert.Contains(reopened.Reasons, r => r.Code == ReasonCodes.RecordFinal);
    }
}
