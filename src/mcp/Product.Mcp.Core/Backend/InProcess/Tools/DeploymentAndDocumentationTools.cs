using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Operations;
using Product.Engine.Core.Snapshots;
using Product.Engine.Runtime;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Json;

namespace Product.Mcp.Core.Backend.InProcess;

// Deployment toolset (tools 27 to 30 and 48) and documentation toolset (tools 32 to 34) of document 24, sections 6.7 and
// 6.8. Tool 31, check_change_approval, is Later (D-03).
internal static partial class ToolHandlers
{
    // ---- 27. record_deployment ----

    // WORKING DEFAULT (open design question 12): pipelines send the same contract to the record endpoint under a
    // Workload Grant; on MCP the owner's AI tool reports it. WORKING DEFAULT (open design question 7): an AI-only code
    // approval never counts as authorization, and the shipped default flags it.
    private static JsonTreeNode RecordDeployment(ToolScope s)
    {
        s.RequireAiState("as-reported");
        string? changeId = s.Text("changeRequestId");
        StoredRow? change = changeId == null ? null : s.RequireRow("change-request", changeId, "changeRequestId");
        string appId = s.Text("businessApplicationId") ?? change?.Text("businessApplication")
            ?? throw s.Invalid("businessApplicationId", "businessApplicationId is required when no change request names the application.");
        StoredRow app = s.RequireRow("business-application", appId, "businessApplicationId");
        RequireOwnerRelation(s, app.Id);
        string kind = s.Text("artifactKindKey") ?? "other";
        RequireListValue(s, "artifact-kind", kind, "artifactKindKey");
        if (s.Text("pipelineReferenceId") is string pipeline)
        {
            s.RequireRow("pipeline-reference", pipeline, "pipelineReferenceId");
        }

        string codeApproval = s.Text("codeApprovalKind") ?? "unknown";
        var report = new DeploymentReport(s.RequireText("idempotencyKey"), app.Id, s.RequireText("artifactName"), s.RequireText("artifactVersion"), Js.ParseMoment(s.RequireText("finishedOn")) ?? s.Now)
        {
            ArtifactKindKey = kind,
            Digest = s.Text("digest"),
            SourceCommit = s.Text("sourceCommit"),
            TargetId = s.Text("deploymentTargetId"),
            TargetExternalId = s.Text("targetExternalId"),
            ChangeRequestId = changeId,
            PipelineReferenceId = s.Text("pipelineReferenceId"),
            RunUrl = s.Text("runUrl"),
            StartedOnUtc = Js.ParseMoment(s.Text("startedOn")),
            ResultMeaning = s.RequireText("resultMeaning"),
            VersionBefore = s.Text("versionBefore"),
            VersionAfter = s.Text("versionAfter"),
            StartedByAiClient = s.Session.Caller.ClientAppLabel,
            CodeApprovalKind = EngineContract.CodeApprovalKinds.TryParse(codeApproval, out CodeApprovalKind parsed) ? parsed : CodeApprovalKind.Unknown,
        };
        ComposedWrite write = s.Call.RecordDeployment(report, out JsonTreeNode? payload);
        StoredRow record = s.Rows.Get("deployment-record", write.RowId)!;
        JsonTreeNode data = RecordEndpointAnswers.DeploymentData(RecordAnswers(s), record, write.Created, payload, codeApproval);
        return s.Answer(data, write.Created ? "The deployment is recorded as reported. The engine set the route and the flags." : "This idempotency key was recorded before; the first record is returned and nothing new was written.", s.Call.IsDryRun && write.Created ? null : s.RecordUrl("deployment-record", record.Id));
    }

    // What the record answers read from this call: its rows and snapshot, its time and the session's caller. The record
    // endpoint for pipelines builds the same data through RecordEndpointAnswers with its own caller.
    private static RecordAnswerContext RecordAnswers(ToolScope s)
    {
        return new RecordAnswerContext(s.Rows, s.Snapshot, s.Now, s.PersonId, s.Session.Caller.ClientAppId, s.Session.ActorKind, s.Call.IsDryRun);
    }

    private static JsonTreeNode Route(ToolScope s, StoredRow record)
    {
        return RecordEndpointAnswers.Route(s.Snapshot, record);
    }

    // The engine's flags, plus the AI-only code approval flag the shipped default sets (question 7).
    private static JsonTreeNode RecordFlags(ToolScope s, StoredRow record, string? codeApproval)
    {
        return RecordEndpointAnswers.Flags(s.Snapshot, record, codeApproval);
    }

    // ---- 48. record_deployment_correction ----

    private static JsonTreeNode RecordDeploymentCorrection(ToolScope s)
    {
        s.RequireAiState("as-reported");
        StoredRow old = s.RequireRow("deployment-record", s.Text("correctsDeploymentRecordId"), "correctsDeploymentRecordId");
        StoredRow app = s.RequireRow("business-application", old.Text("businessApplication"), "correctsDeploymentRecordId");
        RequireOwnerRelation(s, app.Id);
        StoredRow? artifact = s.Rows.Get("artifact-version", old.Text("artifactVersion") ?? string.Empty);
        string? changeId = s.Text("changeRequestId") ?? old.Text("changeRequest");
        if (s.Text("changeRequestId") is string newChange)
        {
            s.RequireRow("change-request", newChange, "changeRequestId");
        }

        var report = new DeploymentReport(
            s.RequireText("idempotencyKey"),
            app.Id,
            artifact?.Text("artifactName") ?? "unknown",
            s.Text("artifactVersion") ?? artifact?.Text("version") ?? "unknown",
            Js.ParseMoment(s.Text("finishedOn")) ?? Js.ParseMoment(old.Text("finishedOn")) ?? s.Now)
        {
            ArtifactKindKey = artifact?.Text("artifactKind") ?? "other",
            TargetId = old.Text("deploymentTarget"),
            ChangeRequestId = changeId,
            PipelineReferenceId = old.Text("pipelineReference"),
            RunUrl = s.Text("runUrl") ?? old.Text("runUrl"),
            StartedOnUtc = Js.ParseMoment(old.Text("startedOn")),
            ResultMeaning = s.Text("resultMeaning") ?? old.Text("resultMeaning") ?? "succeeded",
            VersionBefore = old.Text("versionBefore"),
            VersionAfter = old.Text("versionAfter"),
            StartedByAiClient = s.Session.Caller.ClientAppLabel,

            // A deployment record is final at insert (class F; document 22, section 8.3): the correction is a new record
            // whose Corrects Record names the old one, written in the insert. The old record is never edited.
            CorrectsRecordId = old.Id,
            Description = s.RequireText("reason"),
        };
        ComposedWrite write = s.Call.RecordDeployment(report, out _);
        StoredRow record = s.Rows.Get("deployment-record", write.RowId)!;
        JsonTreeNode data = RecordEndpointAnswers.CorrectionData(RecordAnswers(s), record, write.Created, old.Id);
        return s.Answer(data, "The correction is recorded as a new deployment record; the old record stays.", s.Call.IsDryRun && write.Created ? null : s.RecordUrl("deployment-record", record.Id));
    }

    // ---- 28. get_deployments ----

    // What runs where, and the Deployments list (document 24, B24-11): the latest record per application and target, or
    // the history with latestOnly false. Filters: application, target, change request, result, dates, any flag
    // (flaggedOnly) or named flags (flagKeys). counts are for the same filters, before paging.
    private static JsonTreeNode GetDeployments(ToolScope s)
    {
        string? app = s.Text("businessApplicationId");
        string? target = s.Text("deploymentTargetId");
        string? change = s.Text("changeRequestId");
        bool latest = s.Flag("latestOnly") ?? true;
        bool flagged = s.Flag("flaggedOnly") ?? false;
        IReadOnlyList<string> flagKeys = s.Strings("flagKeys");
        IReadOnlyList<string> results = s.Strings("resultMeanings");
        DateTime? from = Js.ParseMoment(s.Text("from"));
        DateTime? to = Js.ParseMoment(s.Text("to"));
        bool HasFlag(StoredRow r, string key) => key switch
        {
            "outside-window" => r.Flag("isOutsideWindow"),
            "other-path" => r.Flag("isOtherPath"),
            "missing-approval" => r.Flag("isMissingApproval"),
            "ai-only-code-approval" => r.Flag("isAiOnlyCodeApproval"),
            _ => false,
        };

        HashSet<string>? inPortfolio = PortfolioApplications(s, s.Text("portfolioId"), "portfolioId");
        var all = s.Rows.Query(RowQuery.For("deployment-record")).ToList();
        var corrected = new HashSet<string>(all.Select(r => r.Text("correctsRecord")).Where(c => c != null).Select(c => c!), StringComparer.Ordinal);
        IEnumerable<StoredRow> rows = all
            .Where(r => app == null || Same(r.Text("businessApplication"), app))
            .Where(r => inPortfolio == null || inPortfolio.Contains(r.Text("businessApplication") ?? string.Empty))
            .Where(r => target == null || Same(r.Text("deploymentTarget"), target))
            .Where(r => change == null || Same(r.Text("changeRequest"), change))
            .Where(r => results.Count == 0 || results.Contains(r.Text("resultMeaning") ?? string.Empty, StringComparer.Ordinal))
            .Where(r => !latest || !corrected.Contains(r.Id))
            .Where(r =>
            {
                DateTime? finished = Js.ParseMoment(r.Text("finishedOn"));
                return (!from.HasValue || (finished.HasValue && finished.Value.Date >= from.Value.Date)) && (!to.HasValue || (finished.HasValue && finished.Value.Date <= to.Value.Date));
            });
        if (latest)
        {
            rows = rows.GroupBy(r => (r.Text("businessApplication") ?? string.Empty) + "|" + (r.Text("deploymentTarget") ?? string.Empty), StringComparer.Ordinal)
                .Select(g => g.OrderByDescending(r => r.Text("finishedOn") ?? string.Empty, StringComparer.Ordinal).ThenByDescending(r => r.Id, StringComparer.Ordinal).First());
        }

        var inScope = rows.ToList();
        var ordered = inScope
            .Where(r => !flagged || IsFlagged(r))
            .Where(r => flagKeys.Count == 0 || flagKeys.Any(k => HasFlag(r, k)))
            .OrderByDescending(r => r.Text("finishedOn") ?? string.Empty, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
        List<StoredRow> page = s.Page(ordered, out string? next);
        JsonTreeNode items = Arr(page.Select(r => DeploymentItem(s, r)));
        JsonTreeNode counts = Js.Obj(
            ("records", JsonTreeNode.FromInt64(inScope.Count)),
            ("flagged", JsonTreeNode.FromInt64(inScope.Count(IsFlagged))),
            ("outsideWindow", JsonTreeNode.FromInt64(inScope.Count(r => HasFlag(r, "outside-window")))),
            ("otherPath", JsonTreeNode.FromInt64(inScope.Count(r => HasFlag(r, "other-path")))),
            ("missingApproval", JsonTreeNode.FromInt64(inScope.Count(r => HasFlag(r, "missing-approval")))),
            ("aiOnlyCodeApproval", JsonTreeNode.FromInt64(inScope.Count(r => HasFlag(r, "ai-only-code-approval")))));
        return s.Answer(Js.Obj(("items", items), ("counts", counts), ("totalCount", JsonTreeNode.FromInt64(ordered.Count)), ("skipToken", Js.OptStr(next))), ordered.Count + " deployment records match.");
    }

    // ---- 29. map_pipeline ----

    private static JsonTreeNode MapPipeline(ToolScope s)
    {
        s.RequireAiState("link");
        SnapshotRow kind = s.RequireKey("link-kind", s.Text("linkKindKey"), "linkKindKey");
        if (Js.Text(kind.Columns, "linkCategory") != "pipeline")
        {
            throw s.Invalid("linkKindKey", "linkKindKey must be a link kind of the pipeline category.");
        }

        var entries = s.Items("entries").ToList();
        foreach (JsonTreeNode entry in entries)
        {
            StoredRow app = s.RequireRow("business-application", Js.Text(entry, "businessApplicationId"), "entries.businessApplicationId");
            StoredRow target = s.RequireRow("deployment-target", Js.Text(entry, "deploymentTargetId"), "entries.deploymentTargetId");
            if (target.Text("retiredOn") is string retiredOn)
            {
                // A retired target takes no pipeline mapping (document 24, B24-18); its old entries stay as history.
                throw s.Refused(McpErrorCodes.RuleViolation, "The deployment target " + (target.Text("labelAsSeen") ?? target.Id) + " was retired on " + DayText(retiredOn) + ", so no pipeline is mapped to it. Map the pipeline to the target that replaced it.");
            }

            RequireOwnerRelation(s, app.Id);
        }

        var columns = Js.Obj(
            ("linkKind", JsonTreeNode.FromString(kind.KeyPath)),
            ("externalId", JsonTreeNode.FromString(s.RequireText("externalId"))),
            ("url", JsonTreeNode.FromString(s.RequireText("url"))),
            ("labelAsSeen", JsonTreeNode.FromString(s.Text("labelAsSeen") ?? s.RequireText("externalId"))),
            ("lastSeenOn", Js.Utc(s.Now)));
        string? existing = s.Text("pipelineReferenceId");
        ComposedWrite write = existing == null
            ? s.Create("pipeline-reference", Js.With(columns, ("isMoved", JsonTreeNode.False)))
            : s.Update("pipeline-reference", s.RequireRow("pipeline-reference", existing, "pipelineReferenceId").Id, columns);
        var written = new List<JsonTreeNode>();
        foreach (JsonTreeNode entry in entries)
        {
            string app = Js.Text(entry, "businessApplicationId")!;
            string target = Js.Text(entry, "deploymentTargetId")!;
            long order = Js.Number(entry, "stageOrder") ?? 1;
            StoredRow? same = s.Query("pipeline-map-entry", ("pipelineReference", write.RowId), ("businessApplication", app), ("deploymentTarget", target)).FirstOrDefault();
            string id = same == null
                ? s.Create("pipeline-map-entry", Js.Obj(("pipelineReference", JsonTreeNode.FromString(write.RowId)), ("businessApplication", JsonTreeNode.FromString(app)), ("deploymentTarget", JsonTreeNode.FromString(target)), ("stageOrder", JsonTreeNode.FromInt64(order)))).RowId
                : s.Update("pipeline-map-entry", same.Id, Js.Obj(("stageOrder", JsonTreeNode.FromInt64(order)))).RowId;
            written.Add(Js.Obj(("pipelineMapEntryId", s.Call.IsDryRun && same == null ? JsonTreeNode.Null : JsonTreeNode.FromString(id)), ("businessApplicationId", JsonTreeNode.FromString(app)), ("deploymentTargetId", JsonTreeNode.FromString(target)), ("stageOrder", JsonTreeNode.FromInt64(order))));
        }

        JsonTreeNode data = ToolScope.Merge(
            s.WriteResult("pipeline-reference", write, hasStatus: false),
            ("pipelineReferenceId", s.Call.IsDryRun && write.Created ? JsonTreeNode.Null : JsonTreeNode.FromString(write.RowId)),
            ("entries", Arr(written)));
        return s.Answer(data, "The pipeline is mapped to " + Count(entries.Count, "application and target pair", "application and target pairs") + ". The product never runs the pipeline.");
    }

    // ---- 30. get_pipeline_map ----

    // With an application, the pipelines mapped to it; with a pipeline, that one; with neither, every pipeline the product
    // holds (the Places screen's pipeline list, wave 4), a pipeline mapped to no target included, with no entries.
    private static JsonTreeNode GetPipelineMap(ToolScope s)
    {
        string? app = s.Text("businessApplicationId");
        string? one = s.Text("pipelineReferenceId");
        IEnumerable<string> pipelineIds;
        if (app != null)
        {
            RequireApplication(s);
            pipelineIds = s.Query("pipeline-map-entry", ("businessApplication", app)).Select(e => e.Text("pipelineReference")).Where(p => p != null).Select(p => p!).Distinct(StringComparer.Ordinal);
        }
        else if (one != null)
        {
            pipelineIds = new[] { s.RequireRow("pipeline-reference", one, "pipelineReferenceId").Id };
        }
        else
        {
            s.Call.PrefetchAll("pipeline-map-entry");
            s.Call.PrefetchAll("deployment-target");
            pipelineIds = s.Rows.Query(RowQuery.For("pipeline-reference")).Select(p => p.Id).ToList();
        }

        var pipelines = new List<JsonTreeNode>();
        foreach (string id in pipelineIds.OrderBy(p => p, StringComparer.Ordinal))
        {
            StoredRow? pipeline = s.Rows.Get("pipeline-reference", id);
            if (pipeline == null || !(pipeline.Text("url") ?? string.Empty).StartsWith("https://", StringComparison.Ordinal))
            {
                continue;
            }

            var entries = s.Query("pipeline-map-entry", ("pipelineReference", id)).OrderBy(e => e.Number("stageOrder") ?? 0).Select(e =>
            {
                StoredRow? application = s.Rows.Get("business-application", e.Text("businessApplication") ?? string.Empty);
                StoredRow? target = s.Rows.Get("deployment-target", e.Text("deploymentTarget") ?? string.Empty);
                return Js.Obj(
                    ("businessApplicationId", JsonTreeNode.FromString(e.Text("businessApplication") ?? string.Empty)),
                    ("applicationName", JsonTreeNode.FromString(application == null ? string.Empty : AppName(application))),
                    ("deploymentTargetId", JsonTreeNode.FromString(e.Text("deploymentTarget") ?? string.Empty)),
                    ("targetLabel", Js.Str(target?.Text("labelAsSeen"))),
                    ("targetStageKey", Js.Str(target?.Text("targetStage"))),
                    ("stageOrder", JsonTreeNode.FromInt64(e.Number("stageOrder") ?? 0)));
            });
            pipelines.Add(Js.Obj(
                ("pipelineReferenceId", JsonTreeNode.FromString(pipeline.Id)),
                ("linkKind", s.LabelledKey("link-kind", pipeline.Text("linkKind") ?? "unknown")),
                ("externalId", JsonTreeNode.FromString(pipeline.Text("externalId") ?? string.Empty)),
                ("label", Js.Str(pipeline.Text("labelAsSeen"))),
                ("url", JsonTreeNode.FromString(pipeline.Text("url")!)),
                ("isMoved", JsonTreeNode.FromBoolean(pipeline.Flag("isMoved"))),
                ("movedToUrl", Js.Str(pipeline.Text("movedToUrl"))),
                ("lastSeenOn", ToolScope.MomentOrNull(pipeline.Text("lastSeenOn"))),
                ("entries", Arr(entries))));
        }

        int unmapped = pipelines.Count(p => Js.Items(p, "entries").Count == 0);
        return s.Answer(
            Js.Obj(("pipelines", Arr(pipelines))),
            app == null && one == null
                ? Count(pipelines.Count, "pipeline is", "pipelines are") + " known" + (unmapped == 0 ? "." : "; " + Count(unmapped, "is", "are") + " mapped to no target.")
                : Count(pipelines.Count, "pipeline is", "pipelines are") + " mapped.");
    }

    // ---- 32. get_documentation_status (OP-06) ----

    private static JsonTreeNode GetDocumentationStatus(ToolScope s)
    {
        List<StoredRow> apps;
        if (s.Text("businessApplicationId") != null)
        {
            apps = new List<StoredRow> { RequireApplication(s) };
        }
        else
        {
            apps = s.Query("owner-assignment", ("principal", s.PersonId)).Select(o => o.Text("businessApplication")).Where(a => a != null).Select(a => a!).Distinct(StringComparer.Ordinal)
                .Where(a => s.OwnsApplication(a)).Select(a => s.Rows.Get("business-application", a)).Where(a => a != null).Select(a => a!).OrderBy(AppName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        IReadOnlyList<string> meanings = s.Strings("statusMeanings");
        List<StoredRow> page = s.Page(apps, out string? next);
        var result = new List<JsonTreeNode>();
        foreach (StoredRow app in page)
        {
            List<DocumentRow> rows = DocumentStatus(s, app);
            var links = s.Query("document-link", ("businessApplication", app.Id)).Where(l => meanings.Count == 0 || meanings.Contains(l.Text("statusMeaning") ?? string.Empty, StringComparer.Ordinal)).ToList();
            result.Add(Js.Obj(
                ("businessApplicationId", JsonTreeNode.FromString(app.Id)),
                ("name", JsonTreeNode.FromString(AppName(app))),
                ("required", Arr(rows.Where(r => r.RequirementKey != null).Select(r => Js.Obj(("documentTypeKey", JsonTreeNode.FromString(r.DocumentTypeKey)), ("fromStageKey", JsonTreeNode.FromString(r.FromStageKey)), ("requirementKey", JsonTreeNode.FromString(r.RequirementKey!)))))),
                ("documents", Arr(links.Where(l => (l.Text("url") ?? string.Empty).StartsWith("https://", StringComparison.Ordinal)).Select(l => Js.Obj(
                    ("documentLinkId", JsonTreeNode.FromString(l.Id)),
                    ("documentTypeKey", JsonTreeNode.FromString(l.Text("documentType") ?? "unknown")),
                    ("url", JsonTreeNode.FromString(l.Text("url")!)),
                    ("linkKindKey", Js.Str(l.Text("linkKind"))),
                    ("status", s.StatusValue(l)),
                    ("owner", s.PrincipalRef(l.Text("documentOwner"))),
                    ("lastUpdatedOn", ToolScope.DateOrNull(l.Text("lastUpdatedOn"))),
                    ("lastCheckedOn", ToolScope.DateOrNull(l.Text("lastCheckedOn"))),
                    ("staleOn", rows.FirstOrDefault(r => r.DocumentLinkId == l.Id)?.StaleOn is string stale ? ToolScope.DateOrNull(stale) : ToolScope.DateOrNull(l.Text("staleOn"))))))),
                ("missing", Js.Strs(rows.Where(r => r.Meaning == "missing").Select(r => r.DocumentTypeKey).Distinct(StringComparer.Ordinal))),
                ("stale", Js.Strs(rows.Where(r => r.Meaning == "stale" && r.DocumentLinkId != null).Select(r => r.DocumentLinkId!).Distinct(StringComparer.Ordinal)))));
        }

        int missing = result.Sum(a => Js.Items(a, "missing").Count);
        return s.Answer(Js.Obj(("applications", Arr(result)), ("skipToken", Js.OptStr(next))), Count(missing, "required document is", "required documents are") + " missing across " + Count(result.Count, "application", "applications") + ". Missing is computed from configuration, never stored.");
    }

    // One required document of an application and its status, from OP-06 (missing is computed, never stored).
    private sealed class DocumentRow
    {
        public string DocumentTypeKey { get; set; } = string.Empty;

        public string? RequirementKey { get; set; }

        public string FromStageKey { get; set; } = string.Empty;

        public string Meaning { get; set; } = "missing";

        public string? DocumentLinkId { get; set; }

        public string? StaleOn { get; set; }

        public JsonTreeNode Card { get; set; } = JsonTreeNode.Null;
    }

    private static List<DocumentRow> DocumentStatus(ToolScope s, StoredRow app)
    {
        var rows = new List<DocumentRow>();
        string? stage = app.Text("lifecycleStage");
        string? kind = app.Text("applicationKind");
        if (stage == null || kind == null)
        {
            return rows;
        }

        var links = s.Query("document-link", ("businessApplication", app.Id));
        JsonTreeNode input = Js.Obj(
            ("applicationKindKey", JsonTreeNode.FromString(kind)),
            ("tierKey", Js.OptStr(app.Text("tier"))),
            ("lifecycleStageKey", JsonTreeNode.FromString(stage)),
            ("links", Arr(links.Where(l => l.Text("documentType") != null && l.Text("statusMeaning") != null).Select(l => Js.Obj(
                ("documentLinkId", JsonTreeNode.FromString(l.Id)),
                ("documentTypeKey", JsonTreeNode.FromString(l.Text("documentType")!)),
                ("requirementKey", Js.OptStr(l.Text("documentationRequirement"))),
                ("status", JsonTreeNode.FromString(l.Text("statusMeaning")!)),
                ("hasUrl", JsonTreeNode.FromBoolean(l.Text("url") != null)),
                ("lastCheckedOnUtc", Js.Moment(l.Columns, "lastCheckedOn") is DateTime c ? Js.Utc(c) : null),
                ("lastUpdatedOnUtc", Js.Moment(l.Columns, "lastUpdatedOn") is DateTime u ? Js.Utc(u) : null))))),
            // A retired application (its stage means retired, or its registration is archived) owes no document; its links
            // stay as history (wave 4b review).
            ("isRetired", IsRetiredApplication(s, app) ? JsonTreeNode.True : null));
        EvaluationResult<JsonTreeNode?> result = s.Call.Evaluate(EngineOperation.EvaluateDocumentationCompleteness, input);
        if (result.Payload == null)
        {
            return rows;
        }

        foreach (JsonTreeNode item in Js.Items(result.Payload, "items"))
        {
            string type = Js.Text(item, "documentTypeKey") ?? "unknown";
            string? requirement = Js.Text(item, "requirementKey");
            string meaning = Js.Text(item, "status") ?? "missing";
            string? linkId = Js.Text(item, "documentLinkId");
            StoredRow? link = linkId == null ? null : links.FirstOrDefault(l => l.Id == linkId);
            string from = Js.Text(s.ConfigRow("documentation-requirement", requirement)?.Columns, "fromStageKey") ?? stage;
            string? staleOn = Js.Text(item, "staleOnUtc");
            rows.Add(new DocumentRow
            {
                DocumentTypeKey = type,
                RequirementKey = requirement,
                FromStageKey = from,
                Meaning = meaning,
                DocumentLinkId = linkId,
                StaleOn = staleOn,
                Card = Js.Obj(
                    ("documentType", s.LabelledKey("document-type", type)),
                    ("status", link == null ? ToolScope.StatusValue(meaning, null) : s.StatusValue(link)),
                    ("documentLinkId", Js.Str(linkId)),
                    ("url", link?.Text("url") is string url && url.StartsWith("https://", StringComparison.Ordinal) ? JsonTreeNode.FromString(url) : JsonTreeNode.Null),
                    ("linkKindKey", Js.Str(link?.Text("linkKind"))),
                    ("lastCheckedOn", ToolScope.DateOrNull(link?.Text("lastCheckedOn"))),
                    ("staleOn", ToolScope.DateOrNull(staleOn))),
            });
        }

        return rows;
    }

    // ---- 33. record_document_link ----

    // WORKING DEFAULT (open design question 9): a "current" from an AI client on a document type with Current Needs
    // Person = yes is stored as draft until the person confirms on a confirmation card.
    private static JsonTreeNode RecordDocumentLink(ToolScope s)
    {
        s.RequireAiState("link");
        StoredRow app = RequireApplication(s);
        RequireOwnerRelation(s, app.Id);
        SnapshotRow type = s.RequireKey("document-type", s.Text("documentTypeKey"), "documentTypeKey");
        string? existing = s.Text("documentLinkId");
        StoredRow? stored = existing == null ? null : s.RequireRow("document-link", existing, "documentLinkId");

        // An update that names no link kind keeps the kind the link was recorded with, so confirming or editing a link
        // never checks its stored address against another kind's pattern. A new link takes the type's default.
        string? linkKind = s.Text("linkKindKey") ?? stored?.Text("linkKind") ?? Js.Text(type.Columns, "defaultLinkKindKey");
        if (linkKind == null)
        {
            throw s.Invalid("linkKindKey", "The document type has no default link kind, so linkKindKey is required.");
        }

        s.RequireKey("link-kind", linkKind, "linkKindKey");
        string meaning = s.Text("statusMeaning") ?? "draft";
        bool needsPerson = Js.Flag(type.Columns, "currentNeedsPerson") == true;
        bool cardNeeded = meaning == "current" && needsPerson && s.IsAiCaller;
        if (cardNeeded)
        {
            meaning = "draft";
            s.Call.Warn("confirmation-card.not-sent", "This document type needs a person to mark it current. It is stored as draft until the person confirms on a confirmation card; the approval runner sends the card once it is connected.", "Document Link", null);
        }

        string owner = s.Text("documentOwnerObjectId") ?? s.PersonId;
        string? requirement = s.ActiveRows("documentation-requirement").Where(r => Js.Text(r.Columns, "documentTypeKey") == type.KeyPath && (Js.Text(r.Columns, "tierKey") is not string t || t == app.Text("tier"))).Select(r => r.KeyPath).OrderBy(k => k, StringComparer.Ordinal).FirstOrDefault();

        // One link meets a documentation requirement of an application (the store's key). A second new link for it is
        // refused with the link to update, never sent to the store to fail there (fix round 2: "Link a document" on an
        // application that had its runbook answered HTTP 500).
        if (stored == null && requirement != null)
        {
            StoredRow? taken = s.Rows.Query(RowQuery.For("document-link")).FirstOrDefault(l => Same(l.Text("businessApplication"), app.Id) && l.Text("documentationRequirement") == requirement);
            if (taken != null)
            {
                // A person reads the sentence and opens the record from the link beside it; an AI client also gets the ID in
                // the words, and the record's link (wave 4b QA walk).
                throw s.Refused(
                    McpErrorCodes.RuleViolation,
                    (app.Text("name") ?? "The application") + " already has its " + s.Label("document-type", type.KeyPath).ToLowerInvariant() + " linked. Update that link instead"
                        + (s.IsAiCaller ? " (documentLinkId " + taken.Id + ")" : string.Empty) + ": one link meets the requirement, and its history stays on one record.",
                    recordUrl: s.RecordUrl("document-link", taken.Id));
            }
        }
        var columns = Js.Obj(
            ("businessApplication", JsonTreeNode.FromString(app.Id)),
            ("documentType", JsonTreeNode.FromString(type.KeyPath)),
            ("documentationRequirement", Js.OptStr(requirement)),
            ("linkKind", JsonTreeNode.FromString(linkKind)),
            ("url", JsonTreeNode.FromString(s.RequireText("url"))),
            ("status", JsonTreeNode.FromString(s.StatusFor("document-link", meaning))),
            ("documentOwner", JsonTreeNode.FromString(owner)),
            ("lastUpdatedOn", Js.OptStr(s.Text("lastUpdatedOn"))));
        ComposedWrite write = stored == null
            ? s.Create("document-link", columns)
            : s.Update("document-link", stored.Id, Js.Without(columns, "businessApplication"));
        StoredRow link = s.Rows.Get("document-link", write.RowId)!;
        JsonTreeNode data = ToolScope.Merge(
            s.WriteResult(link, write.Created),
            ("documentLinkId", s.Call.IsDryRun && write.Created ? JsonTreeNode.Null : JsonTreeNode.FromString(link.Id)),
            ("staleOn", StaleOn(s, type, link)),
            ("confirmationCardSent", JsonTreeNode.False));
        return s.Answer(data, "The document link is recorded with status " + meaning + ", as reported. The product never writes the document.", s.Call.IsDryRun && write.Created ? null : s.RecordUrl("document-link", link.Id));
    }

    // Stale On from the document type's freshness, counted from the last check, else the last update (OP-06's rule).
    private static JsonTreeNode StaleOn(ToolScope s, SnapshotRow type, StoredRow link)
    {
        long? days = Js.Number(type.Columns, "freshnessDays");
        DateTime? from = Js.ParseMoment(link.Text("lastCheckedOn")) ?? Js.ParseMoment(link.Text("lastUpdatedOn"));
        return days.HasValue && from.HasValue ? Js.Date(from.Value.AddDays(days.Value)) : JsonTreeNode.Null;
    }

    // ---- 34. confirm_document_checked ----

    private static JsonTreeNode ConfirmDocumentChecked(ToolScope s)
    {
        s.RequireAiState("as-reported");
        StoredRow link = s.RequireRow("document-link", s.Text("documentLinkId"), "documentLinkId");
        string app = link.Text("businessApplication") ?? string.Empty;
        if (!Same(link.Text("documentOwner"), s.PersonId))
        {
            RequireOwnerRelation(s, app);
        }

        SnapshotRow? type = s.ConfigRow("document-type", link.Text("documentType"));
        bool stillTrue = s.Flag("stillTrue") ?? false;
        bool needsPerson = Js.Flag(type?.Columns, "currentNeedsPerson") == true;
        string meaning = stillTrue ? (needsPerson && s.IsAiCaller ? link.Text("statusMeaning") ?? "draft" : "current") : "stale";
        if (stillTrue && needsPerson && s.IsAiCaller)
        {
            s.Call.Warn("confirmation-card.not-sent", "This document type needs a person to confirm it is still true. The status stays as it was until the person confirms on a confirmation card.", "Document Link", link.Id);
        }

        ComposedWrite write = s.Update("document-link", link.Id, Js.Obj(
            ("lastCheckedOn", JsonTreeNode.FromString(s.PersonToday)),
            ("status", JsonTreeNode.FromString(s.StatusFor("document-link", meaning == "missing" ? "draft" : meaning)))));
        StoredRow written = s.Rows.Get("document-link", write.RowId)!;
        JsonTreeNode data = ToolScope.Merge(
            s.WriteResult(written, false),
            ("lastCheckedOn", JsonTreeNode.FromString(s.PersonToday)),
            ("staleOn", type == null ? JsonTreeNode.Null : StaleOn(s, type, written)),
            ("confirmationCardSent", JsonTreeNode.False));
        return s.Answer(data, stillTrue ? "The check is recorded, as reported." : "The document is marked stale, as reported.", s.RecordUrl("document-link", link.Id));
    }
}
