using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Snapshots;
using Product.Engine.Runtime;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Json;

namespace Product.Mcp.Core.Backend.InProcess;

// What the answer of a record call reads besides the record itself: the rows (for principal labels), the configuration
// in force (for status labels, route meanings and settings), the time, and the caller as the entry knows it. The
// record_deployment and record_deployment_correction tools build it from their composed call; the record endpoint host
// builds it from its own request.
public sealed class RecordAnswerContext
{
    public RecordAnswerContext(
        IRecordReader rows,
        ConfigurationSnapshot? snapshot,
        DateTime asOfUtc,
        string callerObjectId,
        string? callerClientAppId,
        string callerActorKind,
        bool isDryRun = false)
    {
        Rows = rows ?? throw new ArgumentNullException(nameof(rows));
        Snapshot = snapshot;
        AsOfUtc = asOfUtc;
        CallerObjectId = string.IsNullOrEmpty(callerObjectId) ? throw new ArgumentException("A caller has an object ID.", nameof(callerObjectId)) : callerObjectId;
        CallerClientAppId = callerClientAppId;
        CallerActorKind = string.IsNullOrEmpty(callerActorKind) ? throw new ArgumentException("A caller has an actor kind.", nameof(callerActorKind)) : callerActorKind;
        IsDryRun = isDryRun;
    }

    public IRecordReader Rows { get; }

    // The configuration in force. Null only when none is: labels then fall back to the meaning words.
    public ConfigurationSnapshot? Snapshot { get; }

    public DateTime AsOfUtc { get; }

    // The caller's Entra object ID, named when a row carries no performer (the engine always stamps one).
    public string CallerObjectId { get; }

    // The client app ID from the validated token (azp). performedBy.clientApp names it only when it is a GUID.
    public string? CallerClientAppId { get; }

    // The actor kind the entry gives the caller, used when a row carries none: ai-client-for-person on MCP, pipeline on
    // the record endpoint.
    public string CallerActorKind { get; }

    // validateOnly: a record that would be created has no ID yet, and nothing reports created.
    public bool IsDryRun { get; }
}

// The data of record_deployment and record_deployment_correction (document 24, tools 27 and 48), built from the engine's
// outcome and the stored record. The MCP tools answer through these functions, and the record endpoint for pipelines
// (mcp/contracts/record-endpoint.json) builds the same data with them, so the two answers come from one place and the
// record endpoint's responses validate against its response schemas.
public static class RecordEndpointAnswers
{
    // The data of record_deployment, and of the record endpoint's deployment response. payload is the engine's answer to
    // the record call (it names artifactVersionId); codeApprovalKind is the report's, for the AI-only code approval flag.
    public static JsonTreeNode DeploymentData(RecordAnswerContext context, StoredRow record, bool created, JsonTreeNode? payload, string? codeApprovalKind)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (record == null)
        {
            throw new ArgumentNullException(nameof(record));
        }

        bool wouldCreate = context.IsDryRun && created;
        return Merge(
            WriteResult(context, record, created),
            ("deploymentRecordId", wouldCreate ? JsonTreeNode.Null : JsonTreeNode.FromString(record.Id)),
            ("artifactVersionId", wouldCreate ? JsonTreeNode.Null : Js.Str(Js.Text(payload, "artifactVersionId") ?? record.Text("artifactVersion"))),
            ("deploymentTargetId", Js.Str(record.Text("deploymentTarget"))),
            ("changeRequestId", Js.Str(record.Text("changeRequest"))),
            ("route", Route(context.Snapshot, record)),
            ("flags", Flags(context.Snapshot, record, codeApprovalKind)),
            ("gateLevelApplied", JsonTreeNode.FromString(record.Text("gateLevelApplied") ?? "record")));
    }

    // The same, from a runtime outcome of EngineRuntime.RecordDeployment (the record endpoint host's call).
    public static JsonTreeNode DeploymentData(RecordAnswerContext context, RuntimeOutcome outcome, StoredRow record, string? codeApprovalKind)
    {
        if (outcome == null)
        {
            throw new ArgumentNullException(nameof(outcome));
        }

        return DeploymentData(context, record, outcome.Created, outcome.Payload, codeApprovalKind);
    }

    // The data of record_deployment_correction, and of the record endpoint's correction response. correctsRecordId names
    // the corrected record when the new record does not (it always does: Corrects Record is written in its insert).
    public static JsonTreeNode CorrectionData(RecordAnswerContext context, StoredRow record, bool created, string correctsRecordId)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (record == null)
        {
            throw new ArgumentNullException(nameof(record));
        }

        bool wouldCreate = context.IsDryRun && created;
        return Merge(
            WriteResult(context, record, created),
            ("deploymentRecordId", wouldCreate ? JsonTreeNode.Null : JsonTreeNode.FromString(record.Id)),
            ("correctsRecordId", JsonTreeNode.FromString(record.Text("correctsRecord") ?? correctsRecordId)),
            ("flags", Flags(context.Snapshot, record, null)),
            ("gateLevelApplied", JsonTreeNode.FromString(record.Text("gateLevelApplied") ?? "record")));
    }

    // The same, from a runtime outcome of EngineRuntime.RecordDeployment.
    public static JsonTreeNode CorrectionData(RecordAnswerContext context, RuntimeOutcome outcome, StoredRow record, string correctsRecordId)
    {
        if (outcome == null)
        {
            throw new ArgumentNullException(nameof(outcome));
        }

        return CorrectionData(context, record, outcome.Created, correctsRecordId);
    }

    // { key, meaning } of the record's route (C36), as the engine set it.
    internal static JsonTreeNode Route(ConfigurationSnapshot? snapshot, StoredRow record)
    {
        string key = record.Text("deploymentRoute") ?? "other";
        string meaning = ConfigRow(snapshot, "deployment-route", key)?.EngineMeaning ?? "other";
        return Js.Obj(("key", JsonTreeNode.FromString(key)), ("meaning", JsonTreeNode.FromString(meaning)));
    }

    // The engine's flags, plus the AI-only code approval flag the shipped default sets.
    // WORKING DEFAULT (open design question 7): an AI-only code approval never counts as authorization.
    internal static JsonTreeNode Flags(ConfigurationSnapshot? snapshot, StoredRow record, string? codeApprovalKind)
    {
        SnapshotRow? handling = ConfigRow(snapshot, "setting", EngineKeys.DefaultAiOnlyCodeApprovalHandling);
        string? setting = handling != null && handling.IsActive ? Js.Text(handling.Columns, "value") : null;
        bool aiOnly = record.Flag("isAiOnlyCodeApproval") || (codeApprovalKind == "ai-only" && setting != "ignore");
        return Js.Obj(
            ("outsideWindow", JsonTreeNode.FromBoolean(record.Flag("isOutsideWindow"))),
            ("otherPath", JsonTreeNode.FromBoolean(record.Flag("isOtherPath"))),
            ("missingApproval", JsonTreeNode.FromBoolean(record.Flag("isMissingApproval"))),
            ("aiOnlyCodeApproval", JsonTreeNode.FromBoolean(aiOnly)));
    }

    // The fields every write adds to data (document 24, section 3.4), read back from the record as the engine stamped it.
    private static List<(string, JsonTreeNode?)> WriteResult(RecordAnswerContext context, StoredRow record, bool created)
    {
        bool wouldCreate = created && context.IsDryRun;
        return new List<(string, JsonTreeNode?)>
        {
            ("recordId", wouldCreate ? JsonTreeNode.Null : JsonTreeNode.FromString(record.Id)),
            ("status", StatusValue(context.Snapshot, record, "result", "resultMeaning")),
            ("performedBy", PerformedBy(context, record)),
            ("isAiCreated", JsonTreeNode.FromBoolean(record.Flag("isAiCreated"))),
            ("engineStamp", Js.Obj(
                ("engineVersion", JsonTreeNode.FromString(record.Text("engineVersion") ?? EngineInfo.Version)),
                ("engineCheckedOn", JsonTreeNode.FromString(record.Text("engineCheckedOn") ?? CanonicalJson.FormatUtc(context.AsOfUtc))))),
            ("created", JsonTreeNode.FromBoolean(created && !context.IsDryRun)),
        };
    }

    // { label, meaning } of the record's status, or JSON null when it has none.
    private static JsonTreeNode StatusValue(ConfigurationSnapshot? snapshot, StoredRow row, string statusColumn, string meaningColumn)
    {
        SnapshotRow? status = ConfigRow(snapshot, "status", row.Text(statusColumn));
        string? meaning = row.Text(meaningColumn) ?? status?.EngineMeaning;
        if (meaning == null)
        {
            return JsonTreeNode.Null;
        }

        string? label = status == null ? null : Js.Text(status.Columns, "label");
        return Js.Obj(("label", JsonTreeNode.FromString(label ?? ToolScope.Labelize(meaning))), ("meaning", JsonTreeNode.FromString(meaning)));
    }

    // { actorKind, principal, onBehalfOf, clientApp } exactly as the engine stamped the record (document 24, section 4.5).
    private static JsonTreeNode PerformedBy(RecordAnswerContext context, StoredRow row)
    {
        string? clientApp = row.Text("clientApp");
        return Js.Obj(
            ("actorKind", JsonTreeNode.FromString(row.Text("performedByActorKind") ?? context.CallerActorKind)),
            ("principal", PrincipalRef(context.Rows, row.Text("performedByPrincipal") ?? context.CallerObjectId)),
            ("onBehalfOf", PrincipalRef(context.Rows, row.Text("onBehalfOf"))),
            ("clientApp", clientApp != null && ToolScope.IsGuid(context.CallerClientAppId)
                ? Js.Obj(("appId", JsonTreeNode.FromString(context.CallerClientAppId!)), ("label", JsonTreeNode.FromString(clientApp)))
                : JsonTreeNode.Null));
    }

    // { objectId, displayLabel, principalKind } from the Principal Reference, or JSON null. An erased principal shows the
    // erased label, never the old one.
    private static JsonTreeNode PrincipalRef(IRecordReader rows, string? objectId)
    {
        if (objectId == null)
        {
            return JsonTreeNode.Null;
        }

        StoredRow? row = rows.Query(RowQuery.For("principal-reference").Where("entraObjectId", objectId)).FirstOrDefault()
            ?? rows.Query(RowQuery.For("principal-reference")).FirstOrDefault(r => string.Equals(r.Text("entraObjectId"), objectId, StringComparison.OrdinalIgnoreCase));
        string label = row == null ? "Unknown principal" : row.Flag("isErased") ? EngineKeys.ErasedDisplayLabel : row.Text("displayLabel") ?? "Unknown principal";
        return Js.Obj(
            ("objectId", JsonTreeNode.FromString(objectId)),
            ("displayLabel", JsonTreeNode.FromString(label)),
            ("principalKind", Js.OptStr(row?.Text("principalKind"))));
    }

    private static SnapshotRow? ConfigRow(ConfigurationSnapshot? snapshot, string entity, string? key)
    {
        return snapshot != null && key != null && snapshot.TryGetRow(entity, key, out SnapshotRow? row) ? row : null;
    }

    private static JsonTreeNode Merge(List<(string, JsonTreeNode?)> members, params (string Name, JsonTreeNode? Value)[] more)
    {
        var all = new List<(string, JsonTreeNode?)>(members);
        foreach ((string name, JsonTreeNode? value) in more)
        {
            all.RemoveAll(m => m.Item1 == name);
            all.Add((name, value));
        }

        return Js.Obj(all.ToArray());
    }
}
