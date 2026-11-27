using System;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Operations;
using Product.Engine.Core.Reasons;
using Product.Engine.Runtime.Ports;
using Xunit;

namespace Product.Engine.Runtime.Tests;

// The composed call (EngineRuntime.Compose, added for the MCP server core): several guarded writes and the engine's
// own operations in one call with one snapshot, one actor and one transaction; validateOnly as a dry run; an idempotency
// key over the whole operation; the caller's correlation ID on the Audit Entry.
public sealed class ComposeTests
{
    private const string Intake = "{\"requester\":\"" + Harness.Person1 + "\",\"title\":\"Fixture composed intake\",\"need\":\"One call, two rows\",\"proposedKind\":\"power-platform-app\",\"status\":\"intake-draft\"}";

    [Fact]
    public void AComposedCallCommitsAllOrNothing()
    {
        Harness h = Harness.Create();
        RuntimeOutcome done = Harness.Ok(h.Runtime.Compose(Harness.AiClientOf1, new ComposeRequest("save_intake_draft"), call =>
        {
            ComposedWrite intake = call.Write(WriteCommand.Create("intake-request", Intake));
            call.Write(WriteCommand.Create("tier-assessment", "{\"intakeRequest\":\"" + intake.RowId + "\",\"answersJson\":" + Harness.Answers(false) + ",\"assessedBy\":\"" + Harness.Person1 + "\"}"));
            return RuntimeJsonTestHelpers.Object(("recordId", JsonTreeNode.FromString(intake.RowId)));
        }));
        Assert.Equal("allowed", done.Outcome);
        Assert.True(done.Created);
        Assert.Equal("tier-1", h.Store.Rows("tier-assessment").Single().Text("computedTier"));

        string before = h.Store.Dump();
        RuntimeOutcome refused = h.Runtime.Compose(Harness.AiClientOf1, new ComposeRequest("save_intake_draft"), call =>
        {
            call.Write(WriteCommand.Create("intake-request", Intake));
            call.Write(WriteCommand.Update("decision", "decision-missing", "{\"statement\":\"x\"}"));
            return null;
        });
        Assert.Equal(RuntimeOutcome.NotFound, refused.Outcome);
        Assert.Equal(before, h.Store.Dump());
    }

    [Fact]
    public void ADryRunJudgesEveryWriteAndCommitsNothing()
    {
        Harness h = Harness.Create();
        string before = h.Store.Dump();
        RuntimeOutcome dry = Harness.Ok(h.Runtime.Compose(Harness.AiClientOf1, new ComposeRequest("save_intake_draft") { DryRun = true }, call =>
        {
            Assert.True(call.IsDryRun);
            ComposedWrite intake = call.Write(WriteCommand.Create("intake-request", Intake));
            Assert.NotNull(call.Rows.Get("intake-request", intake.RowId));
            return null;
        }));
        Assert.Equal("allowed", dry.Outcome);
        Assert.Equal(before, h.Store.Dump());

        // The guard still refuses in a dry run: an AI client never accepts a decision (G-08).
        string decision = h.IntakeDecision(tierTwo: true);
        string withDecision = h.Store.Dump();
        RuntimeOutcome refused = h.Runtime.Compose(Harness.AiClientOf1, new ComposeRequest("submit_record") { DryRun = true }, call =>
        {
            call.Write(WriteCommand.Update("decision", decision, "{\"status\":\"decision-accepted\"}"));
            return null;
        });
        Assert.Equal(OperationOutcomes.Refused, refused.Outcome);
        Assert.Equal(withDecision, h.Store.Dump());
    }

    [Fact]
    public void AnIdempotencyKeyCoversTheWholeOperation()
    {
        Harness h = Harness.Create();
        var request = new ComposeRequest("save_intake_draft", CanonicalJson.Parse("{\"title\":\"Once\"}")) { IdempotencyKey = "key-1", CorrelationId = "mcp-000042" };
        Func<ComposedCall, JsonTreeNode?> body = call => RuntimeJsonTestHelpers.Object(("recordId", JsonTreeNode.FromString(call.Write(WriteCommand.Create("intake-request", Intake)).RowId)));
        RuntimeOutcome first = Harness.Ok(h.Runtime.Compose(Harness.AiClientOf1, request, body));
        string afterFirst = h.Store.Dump();
        RuntimeOutcome again = Harness.Ok(h.Runtime.Compose(Harness.AiClientOf1, request, body));
        Assert.True(again.IsReplay);
        Assert.Equal(first.Payload, again.Payload);
        Assert.Equal(afterFirst, h.Store.Dump());

        StoredRow audit = h.Store.Rows("audit-entry").Single(r => r.Text("requestKey") == "key-1");
        Assert.Equal("mcp-000042", audit.Text("correlationId"));
        Assert.Equal(first.RowId, audit.Text("rowId"));

        RuntimeOutcome other = h.Runtime.Compose(Harness.AiClientOf1, new ComposeRequest("save_intake_draft", CanonicalJson.Parse("{\"title\":\"Twice\"}")) { IdempotencyKey = "key-1" }, body);
        Assert.Equal(RuntimeOutcome.Conflict, other.Outcome);
        Assert.True(other.HasReason(ReasonCodes.RequestIdempotencyKeyReused), other.ToString());
    }

    // A person's composed write (the product's page, caller entry person) is the person's own act: the AI defaults of G-08
    // do not apply, so a submitted intake stays submitted, is not AI-created and names no On Behalf Of. The same write from
    // an AI client for the same person is forced to draft and marked AI-created.
    [Fact]
    public void APersonsComposedWriteIsNotDowngradedByTheAiDefaults()
    {
        string submitted = Intake.Replace("intake-draft", "intake-submitted");
        Harness h = Harness.Create();
        Func<ComposedCall, JsonTreeNode?> body = call => RuntimeJsonTestHelpers.Object(("recordId", JsonTreeNode.FromString(call.Write(WriteCommand.Create("intake-request", submitted)).RowId)));

        RuntimeOutcome person = Harness.Ok(h.Runtime.Compose(Harness.PersonOne, new ComposeRequest("save_intake_draft"), body));
        StoredRow mine = h.Row("intake-request", RuntimeJsonTestHelpers.Text(person.Payload, "recordId")!);
        Assert.Equal("submitted", Harness.Text(mine, "statusMeaning"));
        Assert.False(mine.Flag("isAiCreated"));
        Assert.Equal("person", Harness.Text(mine, "performedByActorKind"));
        Assert.Null(mine.Text("onBehalfOf"));
        Assert.False(person.HasReason(ReasonCodes.AiForcedDraft));

        RuntimeOutcome ai = Harness.Ok(h.Runtime.Compose(Harness.AiClientOf1, new ComposeRequest("save_intake_draft"), body));
        StoredRow drafted = h.Row("intake-request", RuntimeJsonTestHelpers.Text(ai.Payload, "recordId")!);
        Assert.Equal("draft", Harness.Text(drafted, "statusMeaning"));
        Assert.True(drafted.Flag("isAiCreated"));
        Assert.Equal("ai-client-for-person", Harness.Text(drafted, "performedByActorKind"));
        Assert.Equal(Harness.Person1, drafted.Text("onBehalfOf"));
        Assert.True(ai.HasReason(ReasonCodes.AiForcedDraft));
    }

    [Fact]
    public void AReadWritesNothing()
    {
        Harness h = Harness.Create();
        RuntimeOutcome read = h.Runtime.Compose(Harness.AiClientOf1, new ComposeRequest("find_applications") { ReadOnly = true }, call =>
        {
            Assert.Throws<InvalidOperationException>(() => call.Write(WriteCommand.Create("intake-request", Intake)));
            return RuntimeJsonTestHelpers.Object(("count", JsonTreeNode.FromInt64(call.Rows.Query(RowQuery.For("business-application")).Count)));
        });
        Assert.Equal("allowed", read.Outcome);
        Assert.Equal("1", RuntimeJsonTestHelpers.Member(read.Payload, "count")!.ToString());
        Assert.Throws<ArgumentException>(() => h.Runtime.Compose(Harness.AiClientOf1, new ComposeRequest("x") { ReadOnly = true, IdempotencyKey = "k" }, call => null));
    }

    [Fact]
    public void TheEnginesOwnWritesStayInsideTheGuard()
    {
        Harness h = Harness.Create();
        string decision = h.IntakeDecision(tierTwo: true);
        Harness.Ok(h.Runtime.RequestApprovals(Harness.AiClientOf1, decision));

        // Cancelling pending cards: engine-nested writes of the cancelled outcome, nothing else.
        RuntimeOutcome cancelled = Harness.Ok(h.Runtime.Compose(Harness.AiClientOf1, new ComposeRequest("withdraw_record"), call =>
            RuntimeJsonTestHelpers.Object(("cancelled", JsonTreeNode.CreateArray(call.CancelPendingApprovals(decision).Select(JsonTreeNode.FromString))))));
        StoredRow record = h.Store.Rows("approval-record").Single(r => !r.Flag("isConsultation"));
        Assert.Equal("cancelled", record.Text("outcomeMeaning"));
        Assert.Equal("engine", record.Text("performedByActorKind"));
        Assert.Null(record.Text("respondedOn"));

        // A second cancel finds nothing pending.
        RuntimeOutcome again = Harness.Ok(h.Runtime.Compose(Harness.AiClientOf1, new ComposeRequest("withdraw_record"), call =>
            RuntimeJsonTestHelpers.Object(("cancelled", JsonTreeNode.CreateArray(call.CancelPendingApprovals(decision).Select(JsonTreeNode.FromString))))));
        Assert.Empty(RuntimeJsonTestHelpers.Strings(again.Payload, "cancelled"));

        // A leave date as reported: only the engine writes Principal References.
        StoredRow person = h.Store.Rows("principal-reference").Single(r => r.Text("entraObjectId") == Harness.Person1);
        RuntimeOutcome caller = h.Runtime.Write(Harness.AiClientOf1, WriteCommand.Update("principal-reference", person.Id, "{\"leavesOn\":\"2026-12-31\"}"));
        Assert.Equal(OperationOutcomes.Refused, caller.Outcome);
        Harness.Ok(h.Runtime.Compose(Harness.AiClientOf1, new ComposeRequest("record_owner_leave"), call =>
        {
            call.RecordPrincipalFacts(person.Id, "2026-12-31", null);
            return null;
        }));
        Assert.Equal("2026-12-31", h.Store.Get("principal-reference", person.Id)!.Text("leavesOn"));
    }

    // The proposal right check (document 24, tool 17; B24-5) over a recorded decision's scope, as the person behind the
    // call: a product role holding a letter, or an owner role the person holds on the decision's application. A refusal
    // ends the call.
    [Fact]
    public void TheProposalRightCheckRunsAsThePersonBehindTheCall()
    {
        Harness h = Harness.Create();

        // Intake: the platform admin is accountable (Balanced intake-a); person 1 holds requester and configuration admin.
        string intake = h.IntakeDecision(tierTwo: true);
        RuntimeOutcome admin = Harness.Ok(h.Runtime.Compose(Harness.PersonTwo, new ComposeRequest("save_decision_proposal") { ReadOnly = true }, call => call.RequireProposalRight(intake)));
        Assert.Equal("intake-a", RuntimeJsonTestHelpers.Text(RuntimeJsonTestHelpers.Member(admin.Payload, "grantedBy"), "decisionRightKey"));
        RuntimeOutcome requester = h.Runtime.Compose(Harness.AiClientOf1, new ComposeRequest("save_decision_proposal") { ReadOnly = true }, call => call.RequireProposalRight(intake));
        Assert.Equal(OperationOutcomes.Refused, requester.Outcome);
        Assert.Equal("decision.propose-not-allowed", requester.Reasons.First(r => r.Severity == ReasonSeverity.Error).Code);

        // Risk acceptance on the estate's application: person 1 is its business owner (risk-acceptance-a), for an AI client
        // acting for them too; the platform admin holds no letter for it.
        string risk = Harness.Ok(h.Runtime.Write(Harness.AiClientOf1, WriteCommand.Create("decision",
            "{\"decisionType\":\"risk-acceptance\",\"subjectKind\":\"business-application\",\"subjectApplication\":\"" + h.ApplicationId + "\",\"statement\":\"Accept the supplier risk\",\"status\":\"decision-proposed\"}"))).RowId!;
        RuntimeOutcome owner = Harness.Ok(h.Runtime.Compose(Harness.AiClientOf1, new ComposeRequest("save_decision_proposal") { ReadOnly = true }, call => call.RequireProposalRight(risk)));
        Assert.Equal("risk-acceptance-a", RuntimeJsonTestHelpers.Text(RuntimeJsonTestHelpers.Member(owner.Payload, "grantedBy"), "decisionRightKey"));
        Assert.Equal(OperationOutcomes.Refused, h.Runtime.Compose(Harness.PersonTwo, new ComposeRequest("save_decision_proposal") { ReadOnly = true }, call => call.RequireProposalRight(risk)).Outcome);
    }
}
