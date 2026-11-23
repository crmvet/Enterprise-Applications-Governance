using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Product.Engine.Configuration.Schema;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Snapshots;
using Product.Engine.Runtime;
using Product.Engine.Runtime.Callers;
using Product.Engine.Runtime.Ports;
using Product.Engine.Runtime.Snapshots;
using Product.Mcp.Core.Backend.InProcess;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Envelope;
using Product.Mcp.Core.Json;

namespace Product.Azure.Host;

/// <summary>
/// The record endpoint for pipeline record steps (mcp/contracts/record-endpoint.json): an app-only token with the
/// <c>Deployment.Record</c> app role, the same contract as the MCP tools <c>record_deployment</c> and
/// <c>record_deployment_correction</c>, and the same envelope back. The engine records the run as the pipeline's workload
/// identity (actor kind pipeline, route pipeline-step) when it holds a current Workload Grant for the deployment
/// recorder role, and sets the route, the flags and the gate level itself. A retry with the same idempotency key returns
/// the first record (200); the same key with a different body is a conflict (409).
/// </summary>
public static class RecordEndpoints
{
    private const string DeploymentSchema = "record-endpoint/deployment.request.schema.json";
    private const string CorrectionSchema = "record-endpoint/deployment-correction.request.schema.json";
    private const string DeploymentResponseSchema = "record-endpoint/deployment.response.schema.json";
    private const string CorrectionResponseSchema = "record-endpoint/deployment-correction.response.schema.json";

    /// <summary><c>POST /api/deployments</c>: records one deployment.</summary>
    public static Task HandleDeployment(HttpContext context)
    {
        return Handle(context, correction: false);
    }

    /// <summary><c>POST /api/deployments/corrections</c>: records a correction as a new record that names the old one.</summary>
    public static Task HandleCorrection(HttpContext context)
    {
        return Handle(context, correction: true);
    }

    private static async Task Handle(HttpContext context, bool correction)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        IServiceProvider services = context.RequestServices;
        ContractSet contracts = services.GetRequiredService<ContractSet>();
        CallerClaims? caller = ProductRequest.Caller(context);
        if (caller == null || !caller.IsAppOnly)
        {
            await HttpJson.WriteFailure(context, new McpError(McpErrorCodes.NotAuthorized, "The record endpoint takes only an app-only token with the Deployment.Record app role."), contracts).ConfigureAwait(false);
            return;
        }

        string? text = await HttpJson.ReadBody(context, services.GetRequiredService<IOptions<ProductHostOptions>>().Value.MaxRequestBytes).ConfigureAwait(false);
        if (text == null || !HttpJson.IsJson(context.Request.ContentType) || !CanonicalJson.TryParse(text, out JsonTreeNode? body, out _) || body.Kind != JsonTreeKind.Object)
        {
            await HttpJson.WriteFailure(context, new McpError(McpErrorCodes.Validation, text == null ? "The request body is larger than this server reads." : "The body is one JSON object, sent as application/json.", "body"), contracts).ConfigureAwait(false);
            return;
        }

        SchemaResult check = contracts.Schemas.Evaluate(body, correction ? CorrectionSchema : DeploymentSchema);
        if (!check.IsValid)
        {
            SchemaIssue first = check.Issues[0];
            await HttpJson.WriteFailure(context, new McpError(McpErrorCodes.Validation, "The body does not match the request schema: " + first + ".", FieldName(first.InstancePath)), contracts).ConfigureAwait(false);
            return;
        }

        string key = Js.Text(body, "idempotencyKey")!;
        if (context.Request.Headers.TryGetValue("Idempotency-Key", out var header) && header.Count > 0 && !string.Equals(header.ToString(), key, StringComparison.Ordinal))
        {
            await HttpJson.WriteFailure(context, new McpError(McpErrorCodes.Validation, "The Idempotency-Key header must equal idempotencyKey in the body.", "Idempotency-Key"), contracts).ConfigureAwait(false);
            return;
        }

        EngineRuntime runtime = services.GetRequiredService<EngineRuntime>();
        IRecordStore records = services.GetRequiredService<IRecordStore>();
        DateTime now = CallerFacts.Utc(runtime.Clock.UtcNow);
        CompiledSnapshot? snapshot = runtime.Cache.Load(services.GetRequiredService<IConfigurationStore>(), SnapshotPurpose.Read, now).Snapshot;
        var warnings = new List<(string Message, string? Code, string? Ref)>();

        ReportBuild built = correction ? BuildCorrection(body, records) : BuildDeployment(body, records, warnings);
        if (built.Error != null)
        {
            await HttpJson.WriteFailure(context, built.Error, contracts, now, snapshot?.VersionNumber).ConfigureAwait(false);
            return;
        }

        DeploymentReport report = built.Report!;
        StoredRow? earlier = records.Query(RowQuery.For("deployment-record").Where("idempotencyKey", key)).FirstOrDefault();
        if (earlier != null && !SameRun(earlier, report, records, snapshot))
        {
            await HttpJson.WriteFailure(context, new McpError(McpErrorCodes.Conflict, "The idempotency key " + key + " was recorded before with a different body. Send a new key for a new run; a retry sends the same body."), contracts, now, snapshot?.VersionNumber).ConfigureAwait(false);
            return;
        }

        CallerIdentity identity = services.GetRequiredService<CallerFacts>().Identity(caller);
        RuntimeOutcome outcome = runtime.RecordDeployment(identity, report);
        if (!outcome.Succeeded)
        {
            await HttpJson.WriteFailure(context, ProductErrorMap.Map(outcome, contracts, aiCaller: false), contracts, now, outcome.ConfigurationVersion).ConfigureAwait(false);
            return;
        }

        snapshot = runtime.Cache.Load(services.GetRequiredService<IConfigurationStore>(), SnapshotPurpose.Read, now).Snapshot;
        StoredRow record = records.Get("deployment-record", outcome.RowId!) ?? throw new InvalidOperationException("The deployment record " + outcome.RowId + " was written but cannot be read back.");
        var reader = new RowFacts(records, snapshot, caller, now);
        List<(string, JsonTreeNode?)> result = reader.WriteResult(record, outcome.Created, "result", "resultMeaning");
        JsonTreeNode data = correction
            ? Merge(result,
                ("deploymentRecordId", JsonTreeNode.FromString(record.Id)),
                ("correctsRecordId", JsonTreeNode.FromString(record.Text("correctsRecord") ?? report.CorrectsRecordId!)),
                ("flags", reader.Flags(record, null)),
                ("gateLevelApplied", JsonTreeNode.FromString(record.Text("gateLevelApplied") ?? "record")))
            : Merge(result,
                ("deploymentRecordId", JsonTreeNode.FromString(record.Id)),
                ("artifactVersionId", Js.Str(Js.Text(outcome.Payload, "artifactVersionId") ?? record.Text("artifactVersion"))),
                ("deploymentTargetId", Js.Str(record.Text("deploymentTarget"))),
                ("changeRequestId", Js.Str(record.Text("changeRequest"))),
                ("route", reader.Route(record)),
                ("flags", reader.Flags(record, Js.Text(body, "codeApprovalKind"))),
                ("gateLevelApplied", JsonTreeNode.FromString(record.Text("gateLevelApplied") ?? "record")));

        string summary = correction
            ? outcome.Created ? "The correction is recorded as a new deployment record; the old record stays." : "This idempotency key was recorded before; the first correction is returned and nothing new was written."
            : outcome.Created ? "The deployment is recorded as reported. The engine set the route and the flags." : "This idempotency key was recorded before; the first record is returned and nothing new was written.";
        string recordUrl = services.GetRequiredService<InProcessBackend>().Options.RecordUrlBase.TrimEnd('/') + "/deployment-record/" + record.Id;
        JsonTreeNode envelope = Envelope.Success(data, summary, now, outcome.ConfigurationVersion ?? snapshot?.VersionNumber ?? 1, ProductRequest.CorrelationId(context), recordUrl, outcome.Reasons, warnings);
        CheckOutput(services, contracts, envelope, correction ? CorrectionResponseSchema : DeploymentResponseSchema);
        await HttpJson.Write(context, outcome.Created ? StatusCodes.Status201Created : StatusCodes.Status200OK, envelope).ConfigureAwait(false);
    }

    // ---- Reports ----

    private sealed class ReportBuild
    {
        public ReportBuild(DeploymentReport? report, McpError? error)
        {
            Report = report;
            Error = error;
        }

        public DeploymentReport? Report { get; }

        public McpError? Error { get; }
    }

    // The record step's report, as the walkthrough's record run and the record_deployment tool build it.
    private static ReportBuild BuildDeployment(JsonTreeNode body, IRecordReader records, List<(string, string?, string?)> warnings)
    {
        string? changeId = Js.Text(body, "changeRequestId");
        StoredRow? change = null;
        if (changeId != null)
        {
            change = records.Get("change-request", changeId);
            if (change == null)
            {
                return new ReportBuild(null, new McpError(McpErrorCodes.NotFound, "Change Request " + changeId + " does not exist.", "changeRequestId"));
            }
        }

        string? appId = Js.Text(body, "businessApplicationId") ?? change?.Text("businessApplication");
        if (appId == null)
        {
            return new ReportBuild(null, new McpError(McpErrorCodes.Validation, "businessApplicationId is required when no change request names the application.", "businessApplicationId"));
        }

        string? pipelineId = Js.Text(body, "pipelineReferenceId");
        string? pipelineExternal = Js.Text(body, "pipelineExternalId");
        if (pipelineId == null && pipelineExternal != null)
        {
            pipelineId = records.Query(RowQuery.For("pipeline-reference").Where("externalId", pipelineExternal)).FirstOrDefault()?.Id;
            if (pipelineId == null)
            {
                warnings.Add(("The pipeline " + pipelineExternal + " matches no Pipeline Reference yet, so the record names no pipeline. A platform admin maps it.", "pipeline.not-mapped", pipelineExternal));
            }
        }

        string codeApproval = Js.Text(body, "codeApprovalKind") ?? "unknown";
        var report = new DeploymentReport(Js.Text(body, "idempotencyKey")!, appId, Js.Text(body, "artifactName")!, Js.Text(body, "artifactVersion")!, Moment(Js.Text(body, "finishedOn"))!.Value)
        {
            ArtifactKindKey = Js.Text(body, "artifactKindKey") ?? "other",
            Digest = Js.Text(body, "digest"),
            SourceCommit = Js.Text(body, "sourceCommit"),
            TargetId = Js.Text(body, "deploymentTargetId"),
            TargetExternalId = Js.Text(body, "targetExternalId"),
            ChangeRequestId = changeId,
            PipelineReferenceId = pipelineId,
            RunUrl = Js.Text(body, "runUrl"),
            StartedOnUtc = Moment(Js.Text(body, "startedOn")),
            ResultMeaning = Js.Text(body, "resultMeaning") ?? "succeeded",
            VersionBefore = Js.Text(body, "versionBefore"),
            VersionAfter = Js.Text(body, "versionAfter"),
            StartedByAiClient = Js.Text(body, "startedByAiClient"),
            CodeApprovalKind = EngineContract.CodeApprovalKinds.TryParse(codeApproval, out CodeApprovalKind parsed) ? parsed : CodeApprovalKind.Unknown,
        };
        return new ReportBuild(report, null);
    }

    // The correction's report, as the record_deployment_correction tool builds it: fields left out are copied from the
    // corrected record, which is never edited.
    private static ReportBuild BuildCorrection(JsonTreeNode body, IRecordReader records)
    {
        string oldId = Js.Text(body, "correctsDeploymentRecordId")!;
        StoredRow? old = records.Get("deployment-record", oldId);
        if (old == null)
        {
            return new ReportBuild(null, new McpError(McpErrorCodes.NotFound, "Deployment Record " + oldId + " does not exist.", "correctsDeploymentRecordId"));
        }

        string? appId = old.Text("businessApplication");
        if (appId == null || records.Get("business-application", appId) == null)
        {
            return new ReportBuild(null, new McpError(McpErrorCodes.NotFound, "The corrected record's Business Application does not exist.", "correctsDeploymentRecordId"));
        }

        string? newChange = Js.Text(body, "changeRequestId");
        if (newChange != null && records.Get("change-request", newChange) == null)
        {
            return new ReportBuild(null, new McpError(McpErrorCodes.NotFound, "Change Request " + newChange + " does not exist.", "changeRequestId"));
        }

        StoredRow? artifact = records.Get("artifact-version", old.Text("artifactVersion") ?? string.Empty);
        var report = new DeploymentReport(
            Js.Text(body, "idempotencyKey")!,
            appId,
            artifact?.Text("artifactName") ?? "unknown",
            Js.Text(body, "artifactVersion") ?? artifact?.Text("version") ?? "unknown",
            Moment(Js.Text(body, "finishedOn")) ?? Moment(old.Text("finishedOn")) ?? DateTime.UtcNow)
        {
            ArtifactKindKey = artifact?.Text("artifactKind") ?? "other",
            TargetId = old.Text("deploymentTarget"),
            ChangeRequestId = newChange ?? old.Text("changeRequest"),
            PipelineReferenceId = old.Text("pipelineReference"),
            RunUrl = Js.Text(body, "runUrl") ?? old.Text("runUrl"),
            StartedOnUtc = Moment(old.Text("startedOn")),
            ResultMeaning = Js.Text(body, "resultMeaning") ?? old.Text("resultMeaning") ?? "succeeded",
            VersionBefore = old.Text("versionBefore"),
            VersionAfter = old.Text("versionAfter"),
            CorrectsRecordId = old.Id,
            Description = Js.Text(body, "reason"),
        };
        return new ReportBuild(report, null);
    }

    // True when an earlier record with the same idempotency key reports the same run, so the call is a retry.
    private static bool SameRun(StoredRow earlier, DeploymentReport report, IRecordReader records, CompiledSnapshot? snapshot)
    {
        if (!Same(earlier.Text("businessApplication"), report.ApplicationId))
        {
            return false;
        }

        if (report.CorrectsRecordId != null || earlier.Text("correctsRecord") != null)
        {
            return Same(earlier.Text("correctsRecord"), report.CorrectsRecordId) && string.Equals(earlier.Text("description"), report.Description, StringComparison.Ordinal);
        }

        if (report.TargetId != null && !Same(earlier.Text("deploymentTarget"), report.TargetId))
        {
            return false;
        }

        if (report.TargetId == null && report.TargetExternalId != null)
        {
            StoredRow? target = records.Get("deployment-target", earlier.Text("deploymentTarget") ?? string.Empty);
            if (target == null || !string.Equals(target.Text("externalId"), report.TargetExternalId, StringComparison.Ordinal))
            {
                return false;
            }
        }

        StoredRow? artifact = records.Get("artifact-version", earlier.Text("artifactVersion") ?? string.Empty);
        if (artifact != null && (!string.Equals(artifact.Text("artifactName"), report.ArtifactName, StringComparison.Ordinal) || !string.Equals(artifact.Text("version"), report.Version, StringComparison.Ordinal)))
        {
            return false;
        }

        DateTime? finished = Moment(earlier.Text("finishedOn"));
        if (finished.HasValue && finished.Value != report.FinishedOnUtc)
        {
            return false;
        }

        string? meaning = earlier.Text("resultMeaning") ?? StatusMeaning(snapshot, earlier.Text("result"));
        if (meaning != null && !string.Equals(meaning, report.ResultMeaning, StringComparison.Ordinal))
        {
            return false;
        }

        return (report.ChangeRequestId == null || Same(earlier.Text("changeRequest"), report.ChangeRequestId))
            && (report.RunUrl == null || string.Equals(earlier.Text("runUrl"), report.RunUrl, StringComparison.Ordinal));
    }

    private static string? StatusMeaning(CompiledSnapshot? snapshot, string? statusKey)
    {
        return snapshot != null && statusKey != null && snapshot.Snapshot.TryGetRow("status", statusKey, out SnapshotRow? row) ? row.EngineMeaning : null;
    }

    private static bool Same(string? a, string? b)
    {
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    internal static DateTime? Moment(string? text)
    {
        DateTime? value = Js.ParseMoment(text);
        return value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;
    }

    internal static JsonTreeNode Merge(List<(string, JsonTreeNode?)> members, params (string Name, JsonTreeNode? Value)[] more)
    {
        var all = new List<(string, JsonTreeNode?)>(members);
        foreach ((string name, JsonTreeNode? value) in more)
        {
            all.RemoveAll(m => m.Item1 == name);
            all.Add((name, value));
        }

        return Js.Obj(all.ToArray());
    }

    internal static void CheckOutput(IServiceProvider services, ContractSet contracts, JsonTreeNode envelope, string schemaId)
    {
        if (!services.GetRequiredService<Product.Mcp.Core.McpServerOptions>().ValidateOutputs)
        {
            return;
        }

        SchemaResult check = contracts.Schemas.Evaluate(envelope, schemaId);
        if (!check.IsValid)
        {
            throw new InvalidOperationException("The answer does not match " + schemaId + ": " + string.Join(" | ", check.Issues.Take(5)));
        }
    }

    // "/answers/0/questionKey" becomes "answers[0].questionKey"; the root is "body".
    internal static string FieldName(string instancePath)
    {
        if (string.IsNullOrEmpty(instancePath))
        {
            return "body";
        }

        var text = new System.Text.StringBuilder();
        foreach (string raw in instancePath.Split('/').Where(p => p.Length > 0))
        {
            string part = raw.Replace("~1", "/").Replace("~0", "~");
            if (part.All(char.IsDigit))
            {
                text.Append('[').Append(part).Append(']');
            }
            else
            {
                if (text.Length > 0)
                {
                    text.Append('.');
                }

                text.Append(part);
            }
        }

        return text.Length == 0 ? "body" : text.ToString();
    }
}

// Reads the parts of an answer from stored rows exactly as the MCP tools build them (the write result fields of
// document 24, section 3.4), for the routes that call the engine directly.
internal sealed class RowFacts
{
    private readonly IRecordReader _records;
    private readonly CompiledSnapshot? _snapshot;
    private readonly CallerClaims _caller;
    private readonly DateTime _now;

    public RowFacts(IRecordReader records, CompiledSnapshot? snapshot, CallerClaims caller, DateTime now)
    {
        _records = records;
        _snapshot = snapshot;
        _caller = caller;
        _now = now;
    }

    public List<(string, JsonTreeNode?)> WriteResult(StoredRow row, bool created, string statusColumn = "status", string meaningColumn = "statusMeaning")
    {
        return new List<(string, JsonTreeNode?)>
        {
            ("recordId", JsonTreeNode.FromString(row.Id)),
            ("status", StatusValue(row, statusColumn, meaningColumn)),
            ("performedBy", PerformedBy(row)),
            ("isAiCreated", JsonTreeNode.FromBoolean(row.Flag("isAiCreated"))),
            ("engineStamp", Js.Obj(
                ("engineVersion", JsonTreeNode.FromString(row.Text("engineVersion") ?? EngineInfo.Version)),
                ("engineCheckedOn", JsonTreeNode.FromString(row.Text("engineCheckedOn") ?? CanonicalJson.FormatUtc(_now))))),
            ("created", JsonTreeNode.FromBoolean(created)),
        };
    }

    // { label, meaning } of a row's status, or JSON null.
    public JsonTreeNode StatusValue(StoredRow row, string statusColumn, string meaningColumn)
    {
        string? key = row.Text(statusColumn);
        SnapshotRow? status = ConfigRow("status", key);
        string? meaning = row.Text(meaningColumn) ?? status?.EngineMeaning;
        if (meaning == null)
        {
            return JsonTreeNode.Null;
        }

        string? label = status == null ? null : Js.Text(status.Columns, "label");
        return Js.Obj(("label", JsonTreeNode.FromString(label ?? Labelize(meaning))), ("meaning", JsonTreeNode.FromString(meaning)));
    }

    // { actorKind, principal, onBehalfOf, clientApp } exactly as the engine stamped the row.
    public JsonTreeNode PerformedBy(StoredRow row)
    {
        string? onBehalfOf = row.Text("onBehalfOf");
        string? clientApp = row.Text("clientApp");
        return Js.Obj(
            ("actorKind", JsonTreeNode.FromString(row.Text("performedByActorKind") ?? (_caller.IsAppOnly ? "pipeline" : "ai-client-for-person"))),
            ("principal", PrincipalRef(row.Text("performedByPrincipal") ?? _caller.ObjectId)),
            ("onBehalfOf", PrincipalRef(onBehalfOf)),
            ("clientApp", clientApp != null && Guid.TryParse(_caller.ClientAppId, out _)
                ? Js.Obj(("appId", JsonTreeNode.FromString(_caller.ClientAppId)), ("label", JsonTreeNode.FromString(clientApp)))
                : JsonTreeNode.Null));
    }

    // { objectId, displayLabel, principalKind }, or JSON null.
    public JsonTreeNode PrincipalRef(string? objectId)
    {
        if (objectId == null)
        {
            return JsonTreeNode.Null;
        }

        StoredRow? row = _records.Query(RowQuery.For("principal-reference").Where("entraObjectId", objectId)).FirstOrDefault();
        string label = row == null ? "Unknown principal" : row.Flag("isErased") ? EngineKeys.ErasedDisplayLabel : row.Text("displayLabel") ?? "Unknown principal";
        return Js.Obj(
            ("objectId", JsonTreeNode.FromString(objectId)),
            ("displayLabel", JsonTreeNode.FromString(label)),
            ("principalKind", Js.OptStr(row?.Text("principalKind"))));
    }

    public JsonTreeNode Route(StoredRow record)
    {
        string key = record.Text("deploymentRoute") ?? "other";
        string meaning = ConfigRow("deployment-route", key)?.EngineMeaning ?? "other";
        return Js.Obj(("key", JsonTreeNode.FromString(key)), ("meaning", JsonTreeNode.FromString(meaning)));
    }

    // The engine's flags, plus the AI-only code approval flag the shipped default sets.
    public JsonTreeNode Flags(StoredRow record, string? codeApproval)
    {
        bool aiOnly = record.Flag("isAiOnlyCodeApproval")
            || (codeApproval == "ai-only" && Setting(EngineKeys.DefaultAiOnlyCodeApprovalHandling) != "ignore");
        return Js.Obj(
            ("outsideWindow", JsonTreeNode.FromBoolean(record.Flag("isOutsideWindow"))),
            ("otherPath", JsonTreeNode.FromBoolean(record.Flag("isOtherPath"))),
            ("missingApproval", JsonTreeNode.FromBoolean(record.Flag("isMissingApproval"))),
            ("aiOnlyCodeApproval", JsonTreeNode.FromBoolean(aiOnly)));
    }

    private SnapshotRow? ConfigRow(string entity, string? key)
    {
        return _snapshot != null && key != null && _snapshot.Snapshot.TryGetRow(entity, key, out SnapshotRow? row) ? row : null;
    }

    private string? Setting(string key)
    {
        SnapshotRow? row = ConfigRow("setting", key);
        return row != null && row.IsActive ? Js.Text(row.Columns, "value") : null;
    }

    private static string Labelize(string meaning)
    {
        string spaced = meaning.Replace('-', ' ');
        return spaced.Length == 0 ? meaning : char.ToUpperInvariant(spaced[0]) + spaced.Substring(1);
    }
}
