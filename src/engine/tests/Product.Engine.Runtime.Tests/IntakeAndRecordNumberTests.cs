using System.Linq;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Operations;
using Product.Engine.Core.Reasons;
using Product.Engine.Runtime.Ports;
using Xunit;

namespace Product.Engine.Runtime.Tests;

// Record numbers the engine gives the rows it creates (document 25, B25-9), the business owner an intake names (B25-10),
// and the approval answer check as a read-only pre-check (D-38; document 24, B24-9).
public sealed class IntakeAndRecordNumberTests
{
    // ---- Record numbers ----

    // A new row of a numbered entity takes the next number after the highest the store holds; a caller's own number is
    // dropped (G-09); the numbers go on per entity; and the engine's own creates (the application an accepted intake
    // registers) take one too.
    [Fact]
    public void NewRowsTakeTheNextNumberAfterTheHighestAndACallersNumberIsDropped()
    {
        Harness h = Harness.Create("[{\"op\":\"retire\",\"entity\":\"gate-requirement\",\"key\":\"accept-business-owner\"}]");
        h.Store.Seed("intake-request", "{\"requester\":\"" + Harness.Person1 + "\",\"title\":\"Seeded\",\"need\":\"Seeded\",\"recordNumber\":\"IR-0107\",\"status\":\"intake-draft\",\"statusMeaning\":\"draft\"}");
        h.Store.Seed("intake-request", "{\"requester\":\"" + Harness.Person1 + "\",\"title\":\"Other\",\"need\":\"Other\",\"recordNumber\":\"LEGACY-9\",\"status\":\"intake-draft\",\"statusMeaning\":\"draft\"}");

        RuntimeOutcome first = Harness.Ok(h.Runtime.Write(Harness.PersonOne, WriteCommand.Create("intake-request",
            "{\"requester\":\"" + Harness.Person1 + "\",\"title\":\"First\",\"need\":\"First\",\"recordNumber\":\"IR-9999\",\"status\":\"intake-draft\"}")));
        Assert.Equal("IR-0108", Harness.Text(h.Row("intake-request", first.RowId!), "recordNumber"));
        Assert.Contains(first.Reasons, r => r.Code == ReasonCodes.EngineColumnOverwritten && r.Ref == RecordNumbers.Column);

        RuntimeOutcome second = Harness.Ok(h.Runtime.Write(Harness.PersonOne, WriteCommand.Create("intake-request",
            "{\"requester\":\"" + Harness.Person1 + "\",\"title\":\"Second\",\"need\":\"Second\",\"status\":\"intake-draft\"}")));
        Assert.Equal("IR-0109", Harness.Text(h.Row("intake-request", second.RowId!), "recordNumber"));

        // A caller never changes a number once given.
        RuntimeOutcome edited = Harness.Ok(h.Runtime.Write(Harness.PersonOne, WriteCommand.Update("intake-request", first.RowId!, "{\"recordNumber\":\"IR-0001\",\"title\":\"First, edited\"}")));
        Assert.Equal("IR-0108", Harness.Text(h.Row("intake-request", first.RowId!), "recordNumber"));
        Assert.Contains(edited.Reasons, r => r.Code == ReasonCodes.EngineColumnOverwritten);

        // Decisions number on their own, and the application the accepted intake creates takes the next APP number.
        string decision = h.IntakeDecision(tierTwo: true, linkApplication: false);
        Assert.StartsWith("DEC-", Harness.Text(h.Row("decision", decision), "recordNumber"));
        string intake = Harness.Text(h.Row("decision", decision), "subjectIntakeRequest");
        Assert.Equal("IR-0110", Harness.Text(h.Row("intake-request", intake), "recordNumber"));
        Harness.Ok(h.Runtime.Write(Harness.PersonOne, WriteCommand.Update("decision", decision, "{\"outcome\":\"accept\"}")));
        string record = h.CountingRecord(Harness.Ok(h.Runtime.RequestApprovals(Harness.AiClientOf1, decision)).Payload);
        Harness.Ok(h.Answer(record, "approval-approved", Harness.Person2));
        string app = Harness.Text(h.Row("intake-request", intake), "businessApplication");
        Assert.Equal("APP-0002", Harness.Text(h.Row("business-application", app), "recordNumber"));
    }

    // A dry run takes no number, and a refused call leaves a gap rather than giving the number again: the store reserves
    // a number at once, as a sequence does, so two calls never share one.
    [Fact]
    public void ADryRunTakesNoNumberAndARefusedCallNeverGivesItsNumberAgain()
    {
        Harness h = Harness.Create();
        RuntimeOutcome dry = h.Runtime.Compose(Harness.PersonOne, new ComposeRequest("save_intake_draft") { DryRun = true }, call =>
        {
            call.Write(WriteCommand.Create("intake-request", "{\"requester\":\"" + Harness.Person1 + "\",\"title\":\"Dry\",\"need\":\"Dry\",\"status\":\"intake-draft\"}"));
            return null;
        });
        Assert.True(dry.Succeeded, dry.ToString());
        AppliedWrite dryWrite = Assert.Single(dry.Writes!, w => w.Entity == "intake-request");
        Assert.Null(RuntimeJsonTestHelpers.Text(dryWrite.Columns, "recordNumber"));

        using (IStoreTransaction tx = h.Store.Begin())
        {
            Assert.Equal(1, tx.NextRecordNumber("intake-request", "IR"));
        }

        RuntimeOutcome next = Harness.Ok(h.Runtime.Write(Harness.PersonOne, WriteCommand.Create("intake-request",
            "{\"requester\":\"" + Harness.Person1 + "\",\"title\":\"After the gap\",\"need\":\"Gap\",\"status\":\"intake-draft\"}")));
        Assert.Equal("IR-0002", Harness.Text(h.Row("intake-request", next.RowId!), "recordNumber"));
    }

    // ---- The business owner an intake names (B25-10) ----

    // Balanced's Accept gate asks for a business owner. The person the requester names on an intake for a new application
    // consents first, on a request asked by name (B25-18); the platform admin's approval alone leaves the decision
    // proposed. Once they consent the decision is accepted, and the engine makes them the business owner of the
    // application it registers, on the intake decision as its basis.
    [Fact]
    public void TheBusinessOwnerAnIntakeNamesConsentsThenPassesTheAcceptGateAndBecomesTheOwner()
    {
        Harness h = Harness.Create();
        string decision = h.IntakeDecision(tierTwo: true, linkApplication: false);
        string intake = Harness.Text(h.Row("decision", decision), "subjectIntakeRequest");
        Harness.Ok(h.Runtime.Write(Harness.PersonOne, WriteCommand.Update("intake-request", intake, "{\"businessOwner\":\"" + Harness.Person3 + "\"}")));
        Harness.Ok(h.Runtime.Write(Harness.PersonOne, WriteCommand.Update("decision", decision, "{\"outcome\":\"accept\"}")));
        var requested = Harness.Ok(h.Runtime.RequestApprovals(Harness.AiClientOf1, decision)).Payload;
        string record = h.CountingRecord(requested);
        string consent = h.ConsentRecord(requested);
        Assert.Equal(Harness.Person3, Harness.Text(h.Row("approval-record", consent), "askedPrincipal"));
        Assert.Equal("business-owner", Harness.Text(h.Row("approval-record", consent), "requiredOwnerRole"));

        Harness.Ok(h.Answer(record, "approval-approved", Harness.Person2));
        Assert.Equal("proposed", Harness.Text(h.Row("decision", decision), "statusMeaning"));
        Assert.DoesNotContain(h.Store.Rows("owner-assignment"), o => Harness.Text(o, "principal") == Harness.Person3);

        RuntimeOutcome answered = Harness.Ok(h.Answer(consent, "approval-approved", Harness.Person3));

        Assert.DoesNotContain(answered.Reasons, r => r.Code == ReasonCodes.GateNotReady);
        Assert.Equal("accepted", Harness.Text(h.Row("decision", decision), "statusMeaning"));
        string app = Harness.Text(h.Row("intake-request", intake), "businessApplication");
        StoredRow owner = h.Store.Rows("owner-assignment").Single(o => Harness.Text(o, "businessApplication") == app);
        Assert.Equal("business-owner", Harness.Text(owner, "ownerRole"));
        Assert.Equal(Harness.Person3, Harness.Text(owner, "principal"));
        Assert.Equal(decision, Harness.Text(owner, "ownershipDecision"));
        Assert.Equal("2026-11-22", Harness.Text(owner, "startOn"));
        Assert.Equal("engine", Harness.Text(owner, "performedByActorKind"));
    }

    // The named person declines the role: their consent is a required approval, so the intake decision is rejected, no
    // application is registered and nobody becomes its owner (B25-18).
    [Fact]
    public void TheBusinessOwnerAnIntakeNamesDeclinesAndTheDecisionIsRejected()
    {
        Harness h = Harness.Create();
        string decision = h.IntakeDecision(tierTwo: true, linkApplication: false, businessOwner: Harness.Person3);
        string intake = Harness.Text(h.Row("decision", decision), "subjectIntakeRequest");
        Harness.Ok(h.Runtime.Write(Harness.PersonOne, WriteCommand.Update("decision", decision, "{\"outcome\":\"accept\"}")));
        var requested = Harness.Ok(h.Runtime.RequestApprovals(Harness.AiClientOf1, decision)).Payload;
        Harness.Ok(h.Answer(h.CountingRecord(requested), "approval-approved", Harness.Person2));

        Harness.Ok(h.Answer(h.ConsentRecord(requested), "approval-rejected", Harness.Person3, typedReason: "I do not own this process."));

        Assert.Equal("rejected", Harness.Text(h.Row("decision", decision), "statusMeaning"));
        Assert.Null(h.Row("intake-request", intake).Text("businessApplication"));
        Assert.DoesNotContain(h.Store.Rows("owner-assignment"), o => Harness.Text(o, "principal") == Harness.Person3);
    }

    // Without a named business owner the Accept gate still holds the same intake: the decision stays proposed.
    [Fact]
    public void WithoutABusinessOwnerTheAcceptGateStillHoldsANewApplication()
    {
        Harness h = Harness.Create();
        string decision = h.IntakeDecision(tierTwo: true, linkApplication: false);
        Harness.Ok(h.Runtime.Write(Harness.PersonOne, WriteCommand.Update("decision", decision, "{\"outcome\":\"accept\"}")));
        string record = h.CountingRecord(Harness.Ok(h.Runtime.RequestApprovals(Harness.AiClientOf1, decision)).Payload);

        RuntimeOutcome answered = Harness.Ok(h.Answer(record, "approval-approved", Harness.Person2));

        Assert.Contains(answered.Reasons, r => r.Code == ReasonCodes.GateNotReady);
        Assert.Equal("proposed", Harness.Text(h.Row("decision", decision), "statusMeaning"));
    }

    // A linked application that already has a business owner keeps them: the person the intake names is skipped with a
    // warning, and an ownership decision is the way to change the owner.
    [Fact]
    public void ALinkedApplicationKeepsItsBusinessOwner()
    {
        Harness h = Harness.Create();
        string decision = h.IntakeDecision(tierTwo: true, linkApplication: true);
        string intake = Harness.Text(h.Row("decision", decision), "subjectIntakeRequest");
        Harness.Ok(h.Runtime.Write(Harness.PersonOne, WriteCommand.Update("intake-request", intake, "{\"businessOwner\":\"" + Harness.Person3 + "\"}")));
        Harness.Ok(h.Runtime.Write(Harness.PersonOne, WriteCommand.Update("decision", decision, "{\"outcome\":\"accept\"}")));
        int owners = h.Store.Rows("owner-assignment").Count;
        string record = h.CountingRecord(Harness.Ok(h.Runtime.RequestApprovals(Harness.AiClientOf1, decision)).Payload);

        RuntimeOutcome answered = Harness.Ok(h.Answer(record, "approval-approved", Harness.Person2));

        Assert.Equal("accepted", Harness.Text(h.Row("decision", decision), "statusMeaning"));
        Assert.Equal(owners, h.Store.Rows("owner-assignment").Count);
        Assert.Contains(answered.Reasons, r => r.Code == ReasonCodes.SubjectChangeSkipped && r.Ref == Harness.Person3);
    }

    // ---- The approval answer pre-check ----

    // The check a page runs before it shows Approve: the platform admin's card on an intake may be answered by the admin,
    // and not by the requester, who holds no platform admin role. Nothing is written either way.
    [Fact]
    public void TheAnswerCheckTellsWhoMayAnswerAndWritesNothing()
    {
        Harness h = Harness.Create();
        string decision = h.IntakeDecision(tierTwo: true, linkApplication: true);
        Harness.Ok(h.Runtime.Write(Harness.PersonOne, WriteCommand.Update("decision", decision, "{\"outcome\":\"accept\"}")));
        string record = h.CountingRecord(Harness.Ok(h.Runtime.RequestApprovals(Harness.AiClientOf1, decision)).Payload);
        string before = h.Store.Dump();

        EvaluationResult<ApprovalAnswerResult?>? admin = null;
        Assert.True(h.Runtime.Compose(Harness.PersonTwo, new ComposeRequest("get_decision") { ReadOnly = true }, call => { admin = call.ApprovalAnswerCheck(record); return null; }).Succeeded);
        Assert.Equal(ApprovalAnswerOperation.Allowed, admin!.Outcome);

        EvaluationResult<ApprovalAnswerResult?>? requester = null;
        Assert.True(h.Runtime.Compose(Harness.PersonOne, new ComposeRequest("get_decision") { ReadOnly = true }, call => { requester = call.ApprovalAnswerCheck(record); return null; }).Succeeded);
        Assert.Equal(OperationOutcomes.Refused, requester!.Outcome);
        Assert.Contains(requester.Reasons, r => r.Code == ReasonCodes.ApprovalAnswerNotAsked);
        Assert.Equal(before, h.Store.Dump());
    }
}
