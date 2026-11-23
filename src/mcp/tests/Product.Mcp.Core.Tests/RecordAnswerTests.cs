using System;
using System.Linq;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Runtime;
using Product.Engine.Runtime.Callers;
using Product.Engine.Runtime.Ports;
using Product.Engine.Runtime.Snapshots;
using Product.Mcp.Core.Backend.InProcess;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Json;
using Xunit;

namespace Product.Mcp.Core.Tests;

// The public mapper for the record tools' data (RecordEndpointAnswers). The MCP tools record_deployment and
// record_deployment_correction answer through it, and the record endpoint for pipelines builds its answer with it, so
// both come from one function: the mapper reproduces the tool's data from the stored record, and a pipeline's record,
// mapped the same way, validates against the record endpoint's response schemas.
public sealed class RecordAnswerTests
{
    private const string DeploymentResponse = "record-endpoint/deployment.response.schema.json";
    private const string CorrectionResponse = "record-endpoint/deployment-correction.response.schema.json";

    [Fact]
    public void TheMapperGivesTheToolsData()
    {
        McpHarness h = McpHarness.Create();
        JsonTreeNode deployment = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "record_deployment", Scenarios.DeploymentArguments("run-answer")));
        StoredRow record = h.Store.Get("deployment-record", McpHarness.Text(deployment, "deploymentRecordId"))!;
        RecordAnswerContext asTheTool = Context(h, McpHarness.Owner, McpHarness.ClaudeCode, "ai-client-for-person");
        Assert.Equal(Js.Write(deployment), Js.Write(RecordEndpointAnswers.DeploymentData(asTheTool, record, created: true, payload: null, codeApprovalKind: null)));

        JsonTreeNode correction = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "record_deployment_correction", "{\"correctsDeploymentRecordId\":\"" + record.Id + "\",\"idempotencyKey\":\"run-answer-fix\",\"reason\":\"The version was wrong\",\"artifactVersion\":\"1.0.1\"}"));
        StoredRow corrected = h.Store.Get("deployment-record", McpHarness.Text(correction, "deploymentRecordId"))!;
        Assert.Equal(Js.Write(correction), Js.Write(RecordEndpointAnswers.CorrectionData(asTheTool, corrected, created: true, correctsRecordId: record.Id)));

        // The same key again: the tool returns the first record with created false, and so does the mapper.
        JsonTreeNode again = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "record_deployment", Scenarios.DeploymentArguments("run-answer")));
        Assert.Equal(Js.Write(again), Js.Write(RecordEndpointAnswers.DeploymentData(asTheTool, record, created: false, payload: null, codeApprovalKind: null)));
    }

    // The walkthrough's record step on the harness: the release pipeline records a run as its workload identity, as the
    // record endpoint host calls the engine, and the mapped answer is the record endpoint's response.
    [Fact]
    public void APipelinesRecordMapsToTheRecordEndpointResponses()
    {
        McpHarness h = McpHarness.Create();
        CallerIdentity pipeline = CallerIdentity.Workload(McpHarness.Pipeline, clientApp: "Fixture release pipeline");
        var report = new DeploymentReport("fixture-release-118-prod", McpHarness.Expense, "Expense_managed", "1.0.0", new DateTime(2026, 11, 22, 8, 44, 0, DateTimeKind.Utc))
        {
            ArtifactKindKey = "solution",
            TargetId = McpHarness.Production,
            RunUrl = "https://devops.example.org/fixture/_build/results?buildId=118",
            StartedOnUtc = new DateTime(2026, 11, 22, 8, 31, 0, DateTimeKind.Utc),
            ResultMeaning = "succeeded",
            CodeApprovalKind = CodeApprovalKind.Person,
        };
        RuntimeOutcome recorded = h.Runtime.RecordDeployment(pipeline, report);
        Assert.True(recorded.Succeeded, recorded.ToString());
        Assert.True(recorded.Created);

        StoredRow record = h.Store.Get("deployment-record", recorded.RowId!)!;
        RecordAnswerContext asTheEndpoint = Context(h, McpHarness.Pipeline, null, "pipeline");
        JsonTreeNode data = RecordEndpointAnswers.DeploymentData(asTheEndpoint, recorded, record, "person");
        Assert.Equal("pipeline", McpHarness.Text(data, "performedBy.actorKind"));
        Assert.Equal("pipeline-step", McpHarness.Text(data, "route.meaning"));
        Assert.Equal(false, Js.Flag(data, "isAiCreated"));
        Assert.Equal(true, Js.Flag(data, "created"));
        Assert.Equal(Js.Text(recorded.Payload, "artifactVersionId") ?? record.Text("artifactVersion"), McpHarness.Text(data, "artifactVersionId"));
        Valid(Envelope(h, data, record.Id), DeploymentResponse);

        // The same overload with the runtime outcome gives what the record-based one gives.
        Assert.Equal(Js.Write(data), Js.Write(RecordEndpointAnswers.DeploymentData(asTheEndpoint, record, recorded.Created, recorded.Payload, "person")));

        // A retry with the same key returns the first record: created false.
        RuntimeOutcome retry = h.Runtime.RecordDeployment(pipeline, report);
        Assert.False(retry.Created);
        Assert.Equal(false, Js.Flag(RecordEndpointAnswers.DeploymentData(asTheEndpoint, retry, record, "person"), "created"));

        // The correction: a new record that names the old one.
        var fix = new DeploymentReport("fixture-release-118-prod-fix", McpHarness.Expense, "Expense_managed", "1.0.1", report.FinishedOnUtc)
        {
            ArtifactKindKey = "solution",
            TargetId = McpHarness.Production,
            ResultMeaning = "succeeded",
            CorrectsRecordId = record.Id,
            Description = "The version was wrong",
        };
        RuntimeOutcome correctedOutcome = h.Runtime.RecordDeployment(pipeline, fix);
        Assert.True(correctedOutcome.Succeeded, correctedOutcome.ToString());
        StoredRow corrected = h.Store.Get("deployment-record", correctedOutcome.RowId!)!;
        JsonTreeNode correction = RecordEndpointAnswers.CorrectionData(asTheEndpoint, correctedOutcome, corrected, record.Id);
        Assert.Equal(record.Id, McpHarness.Text(correction, "correctsRecordId"));
        Assert.Equal("pipeline", McpHarness.Text(correction, "performedBy.actorKind"));
        Valid(Envelope(h, correction, corrected.Id), CorrectionResponse);
    }

    // validateOnly: a record that would be created has no ID yet, and nothing reports created.
    [Fact]
    public void ADryRunNamesNoNewRecord()
    {
        McpHarness h = McpHarness.Create();
        JsonTreeNode dry = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "record_deployment", Scenarios.DeploymentArguments("run-dry").TrimEnd('}') + ",\"validateOnly\":true}"));
        Assert.Equal("null", McpHarness.Text(dry, "deploymentRecordId"));
        Assert.Equal("null", McpHarness.Text(dry, "recordId"));
        Assert.Equal(false, Js.Flag(dry, "created"));
        Assert.Empty(h.Store.Rows("deployment-record"));
    }

    private static RecordAnswerContext Context(McpHarness h, string caller, string? clientAppId, string actorKind)
    {
        CompiledSnapshot snapshot = h.Runtime.Cache.Load(h.Store, SnapshotPurpose.Read, h.Clock.UtcNow).Snapshot!;
        return new RecordAnswerContext(h.Records, snapshot.Snapshot, h.Clock.UtcNow, caller, clientAppId, actorKind);
    }

    // The success envelope the record endpoint sends.
    private static JsonTreeNode Envelope(McpHarness h, JsonTreeNode data, string recordId)
    {
        return Js.Obj(
            ("ok", JsonTreeNode.True),
            ("summary", JsonTreeNode.FromString("The deployment is recorded as reported.")),
            ("data", data),
            ("links", Js.Obj(("recordUrl", JsonTreeNode.FromString("https://product.example.org/records/deployment-record/" + recordId)))),
            ("asOf", Js.Utc(h.Clock.UtcNow)),
            ("configurationSetVersion", JsonTreeNode.FromInt64(1)),
            ("couldNotRead", Js.EmptyArray),
            ("warnings", Js.EmptyArray),
            ("untrustedFields", Js.EmptyArray),
            ("nextSteps", Js.EmptyArray),
            ("correlationId", JsonTreeNode.FromString("record-0001")));
    }

    private static void Valid(JsonTreeNode envelope, string schema)
    {
        var check = ContractSet.Embedded.Schemas.Evaluate(envelope, schema);
        Assert.True(check.IsValid, schema + ": " + string.Join(" | ", check.Issues.Take(5).Select(i => i.ToString())) + "\n" + Js.Write(envelope));
    }
}
