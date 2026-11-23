using System;
using System.Linq;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Reasons;
using Xunit;

namespace Product.Engine.Runtime.Tests;

// Reads through the runtime (the snapshot in force, the caller's roles, planned facts) and the delete policy of document
// 22, section 2.5 through the in-memory store.
public sealed class ReadAndDeleteTests
{
    // OP-07 for the estate's application: the accept gate needs a business owner, which the application has.
    [Fact]
    public void GateReadinessFetchesTheApplicationsFacts()
    {
        Harness h = Harness.Create();
        RuntimeOutcome accept = h.Runtime.GetGateReadiness(Harness.PersonTwo, h.ApplicationId, "accept");
        Assert.Equal("ready", accept.Outcome);
        Assert.True(RuntimeJsonTestHelpers.Flag(accept.Payload, "ready"));
        Assert.Empty(accept.Writes);

        RuntimeOutcome missing = h.Runtime.GetGateReadiness(Harness.PersonTwo, "business-application-999999", "accept");
        Assert.Equal("not-found", missing.Outcome);
        Assert.True(missing.HasReason(ReasonCodes.RefNotFound));
    }

    // An operation whose input the caller sends whole runs under the snapshot in force, through the cache.
    [Fact]
    public void ACallerGivenInputEvaluatesUnderTheVersionInForce()
    {
        Harness h = Harness.Create();
        RuntimeOutcome window = h.Runtime.Evaluate(Harness.PersonOne, EngineOperation.CheckChangeWindow,
            CanonicalJson.Parse("{\"windowStartUtc\":\"2026-11-22T08:00:00Z\",\"windowEndUtc\":\"2026-11-22T12:00:00Z\",\"eventUtc\":\"2026-11-22T14:30:00Z\"}"));
        Assert.Equal("outside", window.Outcome);
        Assert.Equal(1, window.ConfigurationVersion);
        Assert.True(window.HasReason(ReasonCodes.WindowOutside));
        Assert.Throws<ArgumentException>(() => h.Runtime.Evaluate(Harness.PersonOne, EngineOperation.GuardWrite, CanonicalJson.Parse("{}")));
    }

    // A draft row is deleted only by its creator (document 22, section 2.5): the person behind the AI client may delete
    // the draft intake; another person may not; a submitted intake is never deleted.
    [Fact]
    public void OnlyTheCreatorDeletesADraftIntake()
    {
        Harness h = Harness.Create();
        string intake = Harness.Ok(h.Runtime.Write(Harness.AiClientOf1, WriteCommand.Create("intake-request", "{\"requester\":\"" + Harness.Person1 + "\",\"title\":\"Fixture\",\"need\":\"Fixture need\"}"))).RowId!;

        RuntimeOutcome other = h.Runtime.Write(Harness.PersonTwo, WriteCommand.Delete("intake-request", intake));
        Assert.Equal("refused", other.Outcome);
        Assert.True(other.HasReason(ReasonCodes.RecordDeleteNotAllowed));
        Assert.NotNull(h.Store.Get("intake-request", intake));

        RuntimeOutcome own = Harness.Ok(h.Runtime.Write(Harness.PersonOne, WriteCommand.Delete("intake-request", intake)));
        Assert.Null(h.Store.Get("intake-request", intake));
        Assert.Equal(Core.Contract.WriteOperation.Delete, own.Writes.Single().Operation);

        string submitted = Harness.Ok(h.Runtime.Write(Harness.PersonOne, WriteCommand.Create("intake-request", "{\"requester\":\"" + Harness.Person1 + "\",\"title\":\"Fixture\",\"need\":\"Fixture need\",\"status\":\"intake-submitted\"}"))).RowId!;
        RuntimeOutcome never = h.Runtime.Write(Harness.PersonOne, WriteCommand.Delete("intake-request", submitted));
        Assert.Equal("refused", never.Outcome);
    }

    // A write to a row that does not exist is not found, never a create.
    [Fact]
    public void AnUpdateOfAMissingRowIsNotFound()
    {
        Harness h = Harness.Create();
        RuntimeOutcome outcome = h.Runtime.Write(Harness.PersonOne, WriteCommand.Update("intake-request", "intake-request-999999", "{\"title\":\"Nothing\"}"));
        Assert.Equal("not-found", outcome.Outcome);
        Assert.True(outcome.HasReason(ReasonCodes.RefNotFound));
    }
}
