using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Runtime.Ports;
using Xunit;

namespace Product.Engine.Runtime.Tests;

// The call history in the audit entries (document 25, B25-13; document 26, section 9.4: O24 is the full change log on
// Azure): every composed call that writes leaves one Audit Entry, with or without an idempotency key, and a refused call
// is recorded as a product event in a transaction of its own.
public sealed class CallAuditTests
{
    private const string Intake = "{\"requester\":\"" + Harness.Person1 + "\",\"title\":\"Fixture call audit\",\"need\":\"One call, one entry\",\"proposedKind\":\"power-platform-app\",\"status\":\"intake-draft\"}";

    // A call with no key leaves one entry naming its tool, the record and the rows written, by ID and status meaning only:
    // no values and no labels. A dry run leaves none.
    [Fact]
    public void AComposedCallWithoutAKeyLeavesOneEntryOfTheRowsItWrote()
    {
        Harness h = Harness.Create();
        Harness.Ok(h.Runtime.Compose(Harness.AiClientOf1, new ComposeRequest("save_intake_draft") { DryRun = true }, call =>
            RuntimeJsonTestHelpers.Object(("recordId", JsonTreeNode.FromString(call.Write(WriteCommand.Create("intake-request", Intake)).RowId)))));
        Assert.Empty(h.Store.Rows("audit-entry"));

        RuntimeOutcome saved = Harness.Ok(h.Runtime.Compose(Harness.AiClientOf1, new ComposeRequest("save_intake_draft") { CorrelationId = "mcp-000077" }, call =>
            RuntimeJsonTestHelpers.Object(("recordId", JsonTreeNode.FromString(call.Write(WriteCommand.Create("intake-request", Intake)).RowId)))));

        StoredRow entry = Assert.Single(h.Store.Rows("audit-entry"));
        Assert.Equal("composed-call", entry.Text("operation"));
        Assert.Equal("save_intake_draft", entry.Text("entityName"));
        Assert.Equal(saved.RowId, entry.Text("rowId"));
        Assert.Equal("ai-client-for-person", entry.Text("actorKind"));
        Assert.Equal(Harness.Person1, entry.Text("onBehalfOf"));
        Assert.Equal("Claude Code", entry.Text("clientApp"));
        Assert.Equal("mcp-000077", entry.Text("correlationId"));
        Assert.Null(entry.Text("requestKey"));
        Assert.True(entry.Column("changedValuesJson")!.TryGetMember("writes", out JsonTreeNode? writes));
        JsonTreeNode written = Assert.Single(writes!.Items);
        Assert.Equal("intake-request", RuntimeJsonTestHelpers.Text(written, "entity"));
        Assert.Equal(saved.RowId, RuntimeJsonTestHelpers.Text(written, "rowId"));
        Assert.Equal("draft", RuntimeJsonTestHelpers.Text(written, "statusMeaning"));
        Assert.DoesNotContain("Fixture call audit", CanonicalJson.Write(entry.Column("changedValuesJson")!, CanonicalProfile.Snapshot));
    }

    // A refused call is recorded on its own: the refusal wrote nothing, so the entry is the only row the store gains. It
    // passes the guard as the engine's nested write, stamped as the caller.
    [Fact]
    public void ARefusedCallIsRecordedInATransactionOfItsOwn()
    {
        Harness h = Harness.Create();
        string before = h.Store.DumpWithout("audit-entry");
        JsonTreeNode details = RuntimeJsonTestHelpers.Object(("code", JsonTreeNode.FromString("ai_cannot_do_this")), ("reasonCode", JsonTreeNode.FromString("ai.status-not-allowed")));
        Assert.True(h.Runtime.RecordRefusedCall(Harness.AiClientOf1, "answer_approval", "00000000-0000-4000-8000-0000000009a1", details, "mcp-000078"));

        Assert.Equal(before, h.Store.DumpWithout("audit-entry"));
        StoredRow entry = Assert.Single(h.Store.Rows("audit-entry"));
        Assert.Equal("call-refused", entry.Text("operation"));
        Assert.Equal("answer_approval", entry.Text("entityName"));
        Assert.Equal("00000000-0000-4000-8000-0000000009a1", entry.Text("rowId"));
        Assert.Equal("ai-client-for-person", entry.Text("actorKind"));
        Assert.Equal(Harness.Person1, entry.Text("actor"));
        Assert.Equal("ai.status-not-allowed", RuntimeJsonTestHelpers.Text(entry.Column("changedValuesJson"), "reasonCode"));
        Assert.Equal("mcp-000078", entry.Text("correlationId"));
    }
}
