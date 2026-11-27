using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Operations;
using Product.Engine.Core.Reasons;
using Product.Engine.Runtime.Callers;
using Product.Engine.Runtime.Facts;
using Product.Engine.Runtime.Ports;
using Product.Engine.Runtime.Snapshots;

namespace Product.Engine.Runtime;

// What a record step or an owner's AI tool reports about one deployment (document 24, tool 27 record_deployment; the
// record endpoint contract; document 25, section 11.4, <prefix>_RecordDeployment). The route and the flags are not here:
// the engine sets them.
public sealed class DeploymentReport
{
    public DeploymentReport(string idempotencyKey, string applicationId, string artifactName, string version, DateTime finishedOnUtc)
    {
        if (string.IsNullOrEmpty(idempotencyKey))
        {
            throw new ArgumentException("A deployment report carries its idempotency key.", nameof(idempotencyKey));
        }

        IdempotencyKey = idempotencyKey;
        ApplicationId = applicationId ?? throw new ArgumentNullException(nameof(applicationId));
        ArtifactName = artifactName ?? throw new ArgumentNullException(nameof(artifactName));
        Version = version ?? throw new ArgumentNullException(nameof(version));
        FinishedOnUtc = finishedOnUtc.Kind == DateTimeKind.Utc ? finishedOnUtc : throw new ArgumentException("Finished On is a UTC date-time.", nameof(finishedOnUtc));
    }

    public string IdempotencyKey { get; }

    public string ApplicationId { get; }

    public string ArtifactName { get; }

    public string Version { get; }

    public DateTime FinishedOnUtc { get; }

    public string? ArtifactKindKey { get; set; }

    public string? Digest { get; set; }

    public string? SourceCommit { get; set; }

    // One of these names the Deployment Target (O15): its row ID, or its External Id in its own tool.
    public string? TargetId { get; set; }

    public string? TargetExternalId { get; set; }

    public string? ChangeRequestId { get; set; }

    public string? PipelineReferenceId { get; set; }

    public string? RunUrl { get; set; }

    public DateTime? StartedOnUtc { get; set; }

    // The result as a deployment result meaning (succeeded, partly-succeeded, failed, not-deployed); the engine picks the
    // configured status for it.
    public string ResultMeaning { get; set; } = "succeeded";

    public string? VersionBefore { get; set; }

    public string? VersionAfter { get; set; }

    public string? StartedByAiClient { get; set; }

    // A correction (document 22, section 8.3): the earlier record this one corrects, and why. A deployment record is final
    // at insert (class F), so both travel in the insert; the earlier record is never edited.
    public string? CorrectsRecordId { get; set; }

    public string? Description { get; set; }

    // Who approved the code merge, as the step reports it. Evidence only, never counted toward an approval.
    // WORKING DEFAULT (open design question 7): OP-04 flags an AI-only code approval under the shipped gate default.
    public CodeApprovalKind CodeApprovalKind { get; set; } = CodeApprovalKind.Unknown;
}

public sealed partial class EngineRuntime
{
    // The record step (document 25, section 8: upsert by Idempotency Key; section 5.6: OP-04 in record mode). A replay of
    // the same key returns the existing record with created false and writes nothing. A workload caller needs the
    // deployment recorder role from a current workload grant (document 26, RA-07: only for that grant's workload role).
    public RuntimeOutcome RecordDeployment(CallerIdentity caller, DeploymentReport report)
    {
        if (report == null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        return Run(caller, SnapshotPurpose.Write, call => RecordDeploymentIn(call, report));
    }

    // The record step inside a call: also used by a composed call (the MCP tool record_deployment).
    private CallAnswer RecordDeploymentIn(RuntimeCall call, DeploymentReport report)
    {
        {
            ActorInfo actor = call.Actor;
            bool workload = actor.ActorKind == ActorKindMeaning.Pipeline || actor.ActorKind == ActorKindMeaning.ApprovalRunner;
            if (workload && !actor.HoldsRole(EngineKeys.DeploymentRecorderRole))
            {
                throw CallFailure.Of(
                    OperationOutcomes.Refused,
                    ReasonCodes.ActorWorkloadGrantMissing,
                    "The app-only caller holds no current workload grant of " + EngineKeys.DeploymentRecorderRole + ", so it cannot record a deployment.",
                    "Workload Grant",
                    EngineKeys.DeploymentRecorderRole);
            }

            FactSet facts = FactSet.Run(call.Tx, f => RecordFacts(f, report));
            call.FactRounds += facts.Rounds;
            StoredRow? existing = facts.One("existing");
            if (existing != null)
            {
                return new CallAnswer(RuntimeOutcome.Allowed, existing.Id, false, DeploymentPayload(existing, facts.One("artifact")?.Id ?? existing.Text("artifactVersion"), false));
            }

            StoredRow target = facts.One("target") ?? throw CallFailure.Of(
                RuntimeOutcome.NotFound,
                ReasonCodes.RefNotFound,
                "The deployment target " + (report.TargetId ?? report.TargetExternalId ?? "(none named)") + " is not registered.",
                "Deployment Target",
                report.TargetId ?? report.TargetExternalId);

            // A retired target keeps its records and takes no new one (document 24, B24-18; document 25, B25-19). A replay
            // of a record made before the target retired is answered above, from the record itself, and a deployment that
            // finished before the retirement day is still history, so a late report of it is taken.
            if (RuntimeJson.Text(target.Columns, "retiredOn") is string retiredOn
                && (!DateTime.TryParse(retiredOn, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out DateTime retiredDay)
                    || report.FinishedOnUtc.Date >= retiredDay.Date))
            {
                string? why = RuntimeJson.Text(target.Columns, "retiredReason");
                throw CallFailure.Of(
                    OperationOutcomes.Refused,
                    ReasonCodes.DeploymentTargetRetired,
                    "The deployment target " + (RuntimeJson.Text(target.Columns, "labelAsSeen") ?? target.Id) + " was retired on " + (DateTime.TryParse(retiredOn, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out DateTime day) ? day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : retiredOn)
                        + (string.IsNullOrWhiteSpace(why) ? string.Empty : " (" + why!.Trim().TrimEnd('.') + ")")
                        + ", so it takes no new deployment. Record the deployment on the target that is in service now.",
                    "Deployment Target",
                    target.Id);
            }

            if (facts.One("application") == null)
            {
                throw CallFailure.Of(RuntimeOutcome.NotFound, ReasonCodes.RefNotFound, "Business Application " + report.ApplicationId + " does not exist.", "Business Application", report.ApplicationId);
            }

            string routeKey = RouteFor(call.Snapshot, actor.ActorKind);

            // The artifact version, upserted by name plus version (document 24, tool 27), as the caller.
            string artifactId = facts.One("artifact")?.Id ?? GuardedWrite(call, new WriteSpec("artifact-version", WriteOperation.Create)
            {
                After = RuntimeJson.Object(
                    ("businessApplication", JsonTreeNode.FromString(report.ApplicationId)),
                    ("artifactKind", RuntimeJson.OptionalText(report.ArtifactKindKey)),
                    ("artifactName", JsonTreeNode.FromString(report.ArtifactName)),
                    ("version", JsonTreeNode.FromString(report.Version)),
                    ("digest", RuntimeJson.OptionalText(report.Digest)),
                    ("sourceCommit", RuntimeJson.OptionalText(report.SourceCommit))),
            })!.RowId;

            JsonTreeNode after = RuntimeJson.Object(
                ("businessApplication", JsonTreeNode.FromString(report.ApplicationId)),
                ("artifactVersion", JsonTreeNode.FromString(artifactId)),
                ("deploymentTarget", JsonTreeNode.FromString(target.Id)),
                ("changeRequest", RuntimeJson.OptionalText(report.ChangeRequestId)),
                ("pipelineReference", RuntimeJson.OptionalText(report.PipelineReferenceId)),
                ("deploymentRoute", JsonTreeNode.FromString(routeKey)),
                ("runUrl", RuntimeJson.OptionalText(report.RunUrl)),
                ("startedOn", report.StartedOnUtc.HasValue ? JsonTreeNode.FromUtcDateTime(DateTime.SpecifyKind(report.StartedOnUtc.Value, DateTimeKind.Utc)) : null),
                ("finishedOn", JsonTreeNode.FromUtcDateTime(report.FinishedOnUtc)),
                ("result", JsonTreeNode.FromString(DefaultStatus(call.Snapshot, "deployment-record", report.ResultMeaning))),
                ("versionBefore", RuntimeJson.OptionalText(report.VersionBefore)),
                ("versionAfter", RuntimeJson.OptionalText(report.VersionAfter)),
                ("deployedBy", JsonTreeNode.FromString(actor.PrincipalId)),
                ("startedByAiClient", RuntimeJson.OptionalText(report.StartedByAiClient)),
                ("correctsRecord", RuntimeJson.OptionalText(report.CorrectsRecordId)),
                ("description", RuntimeJson.OptionalText(report.Description)),
                ("idempotencyKey", JsonTreeNode.FromString(report.IdempotencyKey)));
            WriteStep step = GuardedWrite(call, new WriteSpec("deployment-record", WriteOperation.Create) { After = after, CodeApprovalKind = report.CodeApprovalKind })!;
            StoredRow written = call.Tx.Get("deployment-record", step.RowId)!;
            return new CallAnswer(RuntimeOutcome.Allowed, step.RowId, true, DeploymentPayload(written, artifactId, true));
        }
    }

    private static IEnumerable<FactPlan> RecordFacts(FactSet facts, DeploymentReport report)
    {
        yield return FactPlan.Of(
            FactQuery.Where("existing", RowQuery.For("deployment-record").Where("idempotencyKey", report.IdempotencyKey)),
            FactQuery.ById("application", "business-application", report.ApplicationId),
            report.TargetId != null ? FactQuery.ById("target", "deployment-target", report.TargetId)
                : report.TargetExternalId != null ? FactQuery.Where("target", RowQuery.For("deployment-target").Where("externalId", report.TargetExternalId)) : null,
            FactQuery.Where("artifact", RowQuery.For("artifact-version").Where("businessApplication", report.ApplicationId).Where("artifactName", report.ArtifactName).Where("version", report.Version)));
    }

    // The route the engine sets from the entry point (document 24, tool 27: "The engine sets the route"): a pipeline's
    // record step, the owner's AI tool, or a person in the app. The C36 row with that engine meaning, first by key.
    private static string RouteFor(CompiledSnapshot snapshot, ActorKindMeaning actorKind)
    {
        string meaning = actorKind == ActorKindMeaning.Pipeline || actorKind == ActorKindMeaning.ApprovalRunner ? "pipeline-step"
            : actorKind == ActorKindMeaning.AiClientForPerson ? "ai-tool"
            : actorKind == ActorKindMeaning.Person ? "person-in-app"
            : "other";
        string? key = snapshot.Snapshot.TryGetEntity("deployment-route", out Core.Snapshots.SnapshotEntity? routes)
            ? routes.ActiveRows.Where(r => r.EngineMeaning == meaning).Select(r => r.KeyPath).OrderBy(k => k, StringComparer.Ordinal).FirstOrDefault()
            : null;
        return key ?? throw CallFailure.Of(OperationOutcomes.Unavailable, ReasonCodes.MeaningMissing, "No active Deployment Route has the engine meaning " + meaning + ", so the engine cannot set the route.", "Deployment Route", null);
    }

    // O14 Gate Level Applied and the three flags (document 25, section 5.6, record mode). A missing gate policy refuses
    // the write, and the record step retries later with the same key (rule 1: no fallback).
    private JsonTreeNode DeploymentFlags(RuntimeCall call, JsonTreeNode row, CodeApprovalKind codeApprovalKind)
    {
        string? appId = RuntimeJson.Text(row, "businessApplication");
        string? targetId = RuntimeJson.Text(row, "deploymentTarget");
        string? changeId = RuntimeJson.Text(row, "changeRequest");
        FactSet facts = FactSet.Run(call.Tx, f => DeploymentPlan(f, appId, targetId, changeId));
        call.FactRounds += facts.Rounds;

        StoredRow app = facts.One("application") ?? throw CallFailure.Of(RuntimeOutcome.NotFound, ReasonCodes.RefNotFound, "The deployment's Business Application does not exist.", "Business Application", appId);
        StoredRow target = facts.One("target") ?? throw CallFailure.Of(RuntimeOutcome.NotFound, ReasonCodes.RefNotFound, "The deployment's target does not exist.", "Deployment Target", targetId);
        StoredRow? change = facts.One("change");

        IReadOnlyList<string> allowedRoutes = Array.Empty<string>();
        string? changePath = target.Text("changePath");
        if (changePath != null && call.Snapshot.Snapshot.TryGetRow("change-path", changePath, out Core.Snapshots.SnapshotRow? path) && path.TryGetColumn("allowedRouteKeys", out JsonTreeNode? routes) && routes.Kind == JsonTreeKind.Array)
        {
            allowedRoutes = routes.Items.Where(i => i.Kind == JsonTreeKind.String).Select(i => i.GetString()).ToArray();
        }

        JsonTreeNode? changeFact = null;
        if (change != null)
        {
            string mode = change.Text("mode") ?? SettingText(call.Snapshot, "change-management-mode") ?? "built-in";
            string? overall = mode == "built-in" ? AuthorizationOverall(call, change.Text("authorizationDecision")) : null;
            changeFact = RuntimeJson.Object(
                ("changeManagementMode", JsonTreeNode.FromString(mode)),
                ("approvalOverall", RuntimeJson.OptionalText(overall)),
                ("windowStartUtc", RuntimeJson.OptionalText(change.Text("windowStart"))),
                ("windowEndUtc", RuntimeJson.OptionalText(change.Text("windowEnd"))),
                ("hasExternalChangeNumber", JsonTreeNode.FromBoolean(change.Text("externalChangeNumber") != null)));
        }

        JsonTreeNode input = RuntimeJson.Object(
            ("mode", JsonTreeNode.FromString("record")),
            ("tierKey", RuntimeJson.OptionalText(app.Text("tier"))),
            ("changeTypeKey", RuntimeJson.OptionalText(change?.Text("changeType"))),
            ("targetStageKey", RuntimeJson.OptionalText(target.Text("targetStage"))),
            ("portfolioPath", RuntimeJson.Strings(DecisionScope.PortfolioPathOf(facts))),
            ("change", changeFact),
            ("routeKey", RuntimeJson.OptionalText(RuntimeJson.Text(row, "deploymentRoute"))),
            ("allowedRouteKeys", RuntimeJson.Strings(allowedRoutes)),
            ("eventUtc", RuntimeJson.OptionalText(RuntimeJson.Text(row, "finishedOn"))),
            ("codeApprovalKind", JsonTreeNode.FromString(EngineContract.CodeApprovalKinds.ToWire(codeApprovalKind))));
        EvaluationResult<JsonTreeNode?> result = Evaluate(call, EngineOperation.EvaluateGateDecision, call.Snapshot, input);
        if (!IsAnswer(EngineOperation.EvaluateGateDecision, result.Outcome))
        {
            throw CallFailure.From(result);
        }

        call.AddReasons(result.Reasons);
        JsonTreeNode payload = result.Payload!;
        return RuntimeJson.Object(
            ("gateLevelApplied", RuntimeJson.Text(RuntimeJson.Text(payload, "gateLevel"))),
            ("isOutsideWindow", JsonTreeNode.FromBoolean(RuntimeJson.Flag(payload, "isOutsideWindow"))),
            ("isOtherPath", JsonTreeNode.FromBoolean(RuntimeJson.Flag(payload, "isOtherPath"))),
            ("isMissingApproval", JsonTreeNode.FromBoolean(RuntimeJson.Flag(payload, "isMissingApproval"))),
            ("isAiOnlyCodeApproval", JsonTreeNode.FromBoolean(RuntimeJson.Flag(payload, "isAiOnlyCodeApproval"))));
    }

    private static IEnumerable<FactPlan> DeploymentPlan(FactSet facts, string? appId, string? targetId, string? changeId)
    {
        yield return FactPlan.Of(
            appId == null ? null : FactQuery.ById("application", "business-application", appId),
            targetId == null ? null : FactQuery.ById("target", "deployment-target", targetId),
            changeId == null ? null : FactQuery.ById("change", "change-request", changeId));
        foreach (FactPlan plan in DecisionScope.ApplicationPlan(facts))
        {
            yield return plan;
        }
    }

    // Built-in mode: the OP-03 overall result of the change's authorization decision. An accepted decision is satisfied
    // and a rejected one rejected; a proposed one is evaluated. No decision yet: unknown, so the approval is missing.
    private string? AuthorizationOverall(RuntimeCall call, string? decisionId)
    {
        if (decisionId == null)
        {
            return null;
        }

        DecisionScope scope = Scope(call, decisionId);
        switch (scope.Decision.Text("statusMeaning"))
        {
            case "accepted":
                return "satisfied";
            case "rejected":
                return "rejected";
            case "proposed":
                CompiledSnapshot stamped = StampedSnapshot(call, scope.Decision);
                EvaluationResult<JsonTreeNode?> required = RequiredApprovals(call, scope, stamped);
                return required.Outcome == "engine-accept" ? "satisfied" : ApprovalStatus(call, scope, stamped, required.Payload!, out _).Outcome;
            default:
                return null;
        }
    }

    private static JsonTreeNode DeploymentPayload(StoredRow record, string? artifactId, bool created)
    {
        return RuntimeJson.Object(
            ("deploymentRecordId", JsonTreeNode.FromString(record.Id)),
            ("artifactVersionId", RuntimeJson.Text(artifactId)),
            ("created", JsonTreeNode.FromBoolean(created)),
            ("routeKey", RuntimeJson.Text(record.Text("deploymentRoute"))),
            ("gateLevelApplied", RuntimeJson.Text(record.Text("gateLevelApplied"))),
            ("flags", RuntimeJson.Object(
                ("isOutsideWindow", JsonTreeNode.FromBoolean(record.Flag("isOutsideWindow"))),
                ("isOtherPath", JsonTreeNode.FromBoolean(record.Flag("isOtherPath"))),
                ("isMissingApproval", JsonTreeNode.FromBoolean(record.Flag("isMissingApproval"))),
                ("isAiOnlyCodeApproval", JsonTreeNode.FromBoolean(record.Flag("isAiOnlyCodeApproval"))))));
    }
}
