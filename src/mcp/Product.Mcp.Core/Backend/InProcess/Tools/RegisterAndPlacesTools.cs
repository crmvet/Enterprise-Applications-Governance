using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Snapshots;
using Product.Engine.Runtime;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Json;

namespace Product.Mcp.Core.Backend.InProcess;

// The register and places tools of the remaining screens (D-36; document 24, B24-11): tool 68
// get_tier_assessment (the latest tier answers of an application or request, with the driving answers and what the tier
// needs), 69 list_documents (the portfolio's documentation list: missing, stale, draft, current), 70
// list_deployment_targets and 71 save_deployment_target (Places: a real install registers its targets, so record_deployment
// knows them), 72 list_gap_check_runs (the run history), 73 save_dependency (the dependency map's write) and 74
// save_portfolio (the portfolio tree's write). save_deployment_target and save_portfolio are served only to a person: a
// target's stage and change path, and a portfolio's place and accountable person, decide which rules apply.
internal static partial class ToolHandlers
{
    // ---- 68. get_tier_assessment ----

    // The latest tier assessment of an application or an intake request, or one assessment by ID: every answer with the
    // tier it implies and whether it drives the result (OP-01 run again, read only, over the stored answers), the reviews
    // and gate requirements the answers trigger, the tier decision and whether the assessment is frozen, the tier the
    // application holds now, the Microsoft vocabulary for the computed tier, what the tier needs (documents and review
    // cadences), and the older assessments. Any role reads it.
    private static JsonTreeNode GetTierAssessment(ToolScope s)
    {
        string? appId = s.Text("businessApplicationId");
        string? intakeId = s.Text("intakeRequestId");
        string? assessmentId = s.Text("tierAssessmentId");
        if (new[] { appId, intakeId, assessmentId }.Count(v => v != null) != 1)
        {
            throw s.Invalid("businessApplicationId", "Name exactly one of businessApplicationId, intakeRequestId and tierAssessmentId.");
        }

        StoredRow? app = appId == null ? null : RequireApplication(s);
        StoredRow? intake = null;
        if (intakeId != null)
        {
            intake = s.RequireRow("intake-request", intakeId, "intakeRequestId");
            if (!CanReadIntake(s, intake))
            {
                throw s.NotFound("Intake Request", intake.Id, "intakeRequestId");
            }
        }

        List<StoredRow> all;
        StoredRow? chosen;
        if (assessmentId != null)
        {
            chosen = s.RequireRow("tier-assessment", assessmentId, "tierAssessmentId");
            if (chosen.Text("intakeRequest") is string ofIntake && s.Rows.Get("intake-request", ofIntake) is StoredRow hidden && !CanReadIntake(s, hidden))
            {
                throw s.NotFound("Tier Assessment", chosen.Id, "tierAssessmentId");
            }

            all = chosen.Text("businessApplication") is string a ? s.Query("tier-assessment", ("businessApplication", a)).ToList()
                : chosen.Text("intakeRequest") is string i ? s.Query("tier-assessment", ("intakeRequest", i)).ToList()
                : new List<StoredRow> { chosen };
            app = chosen.Text("businessApplication") is string appOf ? s.Rows.Get("business-application", appOf) : null;
            intake = chosen.Text("intakeRequest") is string intakeOf ? s.Rows.Get("intake-request", intakeOf) : null;
        }
        else
        {
            all = app != null ? s.Query("tier-assessment", ("businessApplication", app.Id)).ToList() : s.Query("tier-assessment", ("intakeRequest", intake!.Id)).ToList();

            // An application registered from an intake request keeps its answers on the request: they are the application's
            // assessment until it is assessed again.
            if (app != null && all.Count == 0)
            {
                foreach (StoredRow request in s.Query("intake-request", ("businessApplication", app.Id)))
                {
                    // A request the caller may not read lends no answers (security review 2026-11-24, B24-14): its ID
                    // would otherwise show through intakeRequestId.
                    if (CanReadIntake(s, request))
                    {
                        all.AddRange(s.Query("tier-assessment", ("intakeRequest", request.Id)));
                    }
                }
            }

            chosen = NewestFirst(all).FirstOrDefault();
        }

        string? kind = app?.Text("applicationKind") ?? intake?.Text("proposedKind");
        string? appliedTier = app?.Text("tier");
        if (chosen == null)
        {
            JsonTreeNode none = Js.Obj(
                ("assessment", JsonTreeNode.Null),
                ("answers", Js.EmptyArray),
                ("missingQuestionKeys", Js.EmptyArray),
                ("triggers", Js.Obj(("reviewTypes", Js.EmptyArray), ("gateRequirementKeys", Js.EmptyArray))),
                ("appliedTier", s.LabelledKey("tier", appliedTier)),
                ("differsFromApplied", JsonTreeNode.False),
                ("vocabulary", Js.EmptyArray),
                ("tierNeeds", Js.Obj(("documents", Js.EmptyArray), ("reviews", Js.EmptyArray))),
                ("history", Js.EmptyArray));
            return s.Answer(none, "No tier assessment is recorded yet. Answer the tier questions to get one.", null,
                ToolScope.Next("get_tier_questions", ("applicationKindKey", Js.OptStr(kind))));
        }

        JsonTreeNode stored = chosen.Column("answersJson") is JsonTreeNode answersJson && answersJson.Kind == JsonTreeKind.Array ? answersJson : Js.EmptyArray;
        var answerList = stored.Items.Select(a => (Question: Js.Text(a, "questionKey") ?? Js.Text(a, "question"), Answer: Js.Text(a, "answerKey") ?? Js.Text(a, "answer")))
            .Where(a => a.Question != null && a.Answer != null).ToList();
        string computed = chosen.Text("computedTier") ?? "unknown";
        IReadOnlyList<string> driving = Array.Empty<string>();
        IReadOnlyList<string> missing = Array.Empty<string>();
        var reviewTriggers = new List<string>();
        var gateTriggers = new List<string>();
        if (kind != null)
        {
            JsonTreeNode input = Js.Obj(
                ("applicationKindKey", JsonTreeNode.FromString(kind)),
                ("answers", Arr(answerList.Select(a => Js.Obj(("questionKey", JsonTreeNode.FromString(a.Question!)), ("answerKey", JsonTreeNode.FromString(a.Answer!)))))));
            EvaluationResult<JsonTreeNode?> tier = s.Call.Evaluate(EngineOperation.EvaluateTier, input);
            if (tier.Payload != null)
            {
                driving = Js.Items(tier.Payload, "decisiveAnswers").Select(a => Js.Text(a, "questionKey")).Where(k => k != null).Select(k => k!).Distinct(StringComparer.Ordinal).ToList();
                missing = Js.Strings(tier.Payload, "missingQuestionKeys");
                reviewTriggers.AddRange(Js.Strings(tier.Payload, "triggeredReviewTypeKeys"));
                gateTriggers.AddRange(Js.Strings(tier.Payload, "triggeredGateRequirementKeys"));
            }
        }

        JsonTreeNode answers = Arr(answerList.Select(a =>
        {
            SnapshotRow? question = s.ConfigRow("tier-question", a.Question);
            SnapshotRow? option = s.AllRows("tier-answer-option").FirstOrDefault(o => o.ParentKey == a.Question && o.Key == a.Answer);
            return Js.Obj(
                ("questionKey", JsonTreeNode.FromString(a.Question!)),
                ("question", JsonTreeNode.FromString(Js.Text(question?.Columns, "questionText") ?? Js.Text(question?.Columns, "label") ?? a.Question!)),
                ("answerKey", JsonTreeNode.FromString(a.Answer!)),
                ("answer", JsonTreeNode.FromString(Js.Text(option?.Columns, "answerText") ?? Js.Text(option?.Columns, "label") ?? a.Answer!)),
                ("impliedTier", s.LabelledKey("tier", Js.Text(option?.Columns, "impliedTierKey"))),
                ("isDriving", JsonTreeNode.FromBoolean(driving.Contains(a.Question!, StringComparer.Ordinal))));
        }));
        StoredRow? decision = chosen.Text("tierDecision") is string decisionId ? s.Rows.Get("decision", decisionId) : null;
        JsonTreeNode vocabulary = Arr(s.ActiveRows("vocabulary-mapping")
            .Where(v => Js.Text(v.Columns, "sourceEntity") == "tier" && Js.Text(v.Columns, "sourceKey") == computed)
            .OrderBy(v => v.KeyPath, StringComparer.Ordinal)
            .Select(v => Js.Obj(
                ("vocabulary", ListValue(s, "vocabulary", Js.Text(v.Columns, "vocabularyKey"))),
                ("term", JsonTreeNode.FromString(Js.Text(v.Columns, "term") ?? string.Empty)),
                ("sourceUrl", Js.Text(v.Columns, "sourceUrl") is string url && url.StartsWith("https://", StringComparison.Ordinal) ? JsonTreeNode.FromString(url) : JsonTreeNode.Null))));
        JsonTreeNode needs = Js.Obj(
            ("documents", Arr(s.ActiveRows("documentation-requirement")
                .Where(r => (Js.Text(r.Columns, "tierKey") is not string t || t == computed) && (Js.Text(r.Columns, "applicationKindKey") is not string k || k == kind))
                .OrderBy(r => r.KeyPath, StringComparer.Ordinal)
                .Select(r => Js.Obj(
                    ("documentType", s.LabelledKey("document-type", Js.Text(r.Columns, "documentTypeKey") ?? "unknown")),
                    ("fromStage", s.LabelledKey("lifecycle-stage", Js.Text(r.Columns, "fromStageKey"))))))),
            ("reviews", Arr(s.ActiveRows("review-cadence")
                .Where(r => Js.Text(r.Columns, "tierKey") == computed)
                .OrderBy(r => r.KeyPath, StringComparer.Ordinal)
                .Select(r => Js.Obj(
                    ("reviewType", s.LabelledKey("review-type", Js.Text(r.Columns, "reviewTypeKey") ?? "unknown")),
                    ("intervalDays", JsonTreeNode.FromInt64(Js.Number(r.Columns, "intervalDays") ?? 0)))))));
        JsonTreeNode data = Js.Obj(
            ("assessment", Js.Obj(
                ("tierAssessmentId", JsonTreeNode.FromString(chosen.Id)),
                ("intakeRequestId", Js.Str(chosen.Text("intakeRequest"))),
                ("businessApplicationId", Js.Str(chosen.Text("businessApplication") ?? app?.Id)),
                ("computedTier", Js.Obj(
                    ("key", JsonTreeNode.FromString(computed)),
                    ("label", JsonTreeNode.FromString(s.Label("tier", computed))),
                    ("rank", JsonTreeNode.FromInt64(Js.Number(s.ConfigRow("tier", computed)?.Columns, "rank") ?? 0)))),
                ("assessedBy", s.PrincipalRef(chosen.Text("assessedBy"))),
                ("tierDecision", decision == null ? JsonTreeNode.Null : Js.Obj(
                    ("decisionId", JsonTreeNode.FromString(decision.Id)),
                    ("outcome", DecisionOutcome(s, decision)),
                    ("status", s.StatusValue(decision) is JsonTreeNode st && st.Kind != JsonTreeKind.Null ? st : ToolScope.StatusValue("proposed", null)),
                    ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("decision", decision.Id))))),
                ("isFrozen", JsonTreeNode.FromBoolean(chosen.Text("tierDecision") != null)),
                ("isAiCreated", AiCreated(chosen)),
                ("performedBy", Stamp(s, chosen)),
                ("configurationSetVersion", Version(chosen)))),
            ("answers", answers),
            ("missingQuestionKeys", Js.Strs(missing)),
            ("triggers", Js.Obj(
                ("reviewTypes", Arr(reviewTriggers.Distinct(StringComparer.Ordinal).Select(k => s.LabelledKey("review-type", k)))),
                ("gateRequirementKeys", Js.Strs(gateTriggers.Distinct(StringComparer.Ordinal))))),
            ("appliedTier", s.LabelledKey("tier", appliedTier)),
            ("differsFromApplied", JsonTreeNode.FromBoolean(appliedTier != null && appliedTier != computed)),
            ("vocabulary", vocabulary),
            ("tierNeeds", needs),
            ("history", Arr(NewestFirst(all).Where(a => a.Id != chosen.Id).Take(10).Select(a => Js.Obj(
                ("tierAssessmentId", JsonTreeNode.FromString(a.Id)),
                ("computedTier", s.LabelledKey("tier", a.Text("computedTier"))),
                ("assessedBy", s.PrincipalRef(a.Text("assessedBy"))),
                ("tierDecisionId", Js.Str(a.Text("tierDecision"))),
                ("isAiCreated", AiCreated(a)))))));
        string drivingText = driving.Count == 0 ? string.Empty : " Driven by: " + string.Join(", ", driving.Select(k => s.Label("tier-question", k))) + ".";
        return s.Answer(
            data,
            "The answers imply " + s.Label("tier", computed) + "." + drivingText + (decision == null ? " No tier decision has used them yet; only an accepted tier decision sets the tier." : string.Empty),
            null,
            decision == null && app != null ? new[] { ToolScope.Next("save_decision_proposal", ("decisionTypeKey", JsonTreeNode.FromString("tier")), ("subjectKind", JsonTreeNode.FromString("business-application")), ("subjectId", JsonTreeNode.FromString(app.Id))) } : Array.Empty<NextStep>());
    }

    // ---- 69. list_documents ----

    // The documentation list across the portfolio (document 24, B24-11): every required document of the applications in
    // scope with its state (missing is computed by OP-06, never stored), and every other document link on them. state
    // narrows it to missing, stale, draft or current; mineOnly keeps the documents the person owns, and the missing
    // documents of applications they own. counts are for the same filters across the states.
    private static JsonTreeNode ListDocuments(ToolScope s)
    {
        string state = s.Text("state") ?? "all";
        IReadOnlyList<string> types = s.Strings("documentTypeKeys");
        string? appFilter = s.Text("businessApplicationId");
        if (appFilter != null)
        {
            RequireApplication(s);
        }

        // One document link by its ID (the document record page): its own row, on its application, whatever the
        // application's registration. A link of another application than businessApplicationId names matches nothing.
        StoredRow? oneLink = s.Text("documentLinkId") is string linkId ? s.RequireRow("document-link", linkId, "documentLinkId") : null;
        if (oneLink != null && appFilter == null)
        {
            appFilter = oneLink.Text("businessApplication");
        }

        string? portfolio = s.Text("portfolioId");
        if (portfolio != null)
        {
            s.RequireRow("portfolio", portfolio, "portfolioId");
        }

        bool mineOnly = s.Flag("mineOnly") ?? false;
        var apps = s.Rows.Query(RowQuery.For("business-application"))
            .Where(a => a.Text("statusMeaning") == "registered" || Same(a.Id, appFilter))
            .Where(a => appFilter == null || Same(a.Id, appFilter))
            .Where(a => portfolio == null || PortfolioPath(s, a.Text("portfolio")).Contains(portfolio, StringComparer.Ordinal))
            .OrderBy(AppName, StringComparer.OrdinalIgnoreCase).ThenBy(a => a.Id, StringComparer.Ordinal)
            .ToList();
        var rows = new List<(StoredRow App, string Type, string State, StoredRow? Link, string? Requirement, string? FromStage, string? StaleOn)>();
        foreach (StoredRow app in apps)
        {
            List<DocumentRow> required = DocumentStatus(s, app);
            var covered = new HashSet<string>(StringComparer.Ordinal);
            foreach (DocumentRow r in required)
            {
                StoredRow? link = r.DocumentLinkId == null ? null : s.Rows.Get("document-link", r.DocumentLinkId);
                if (link != null)
                {
                    covered.Add(link.Id);
                }

                rows.Add((app, r.DocumentTypeKey, DocumentState(r.Meaning), link, r.RequirementKey, r.FromStageKey, r.StaleOn));
            }

            foreach (StoredRow link in s.Query("document-link", ("businessApplication", app.Id)).Where(l => !covered.Contains(l.Id)))
            {
                rows.Add((app, link.Text("documentType") ?? "unknown", DocumentState(link.Text("statusMeaning")), link, null, null, link.Text("staleOn")));
            }
        }

        var cache = new Dictionary<string, bool>(StringComparer.Ordinal);
        bool OwnsApp(string id) => cache.TryGetValue(id, out bool owns) ? owns : cache[id] = s.OwnsApplication(id);
        var scoped = rows.Where(r => (types.Count == 0 || types.Contains(r.Type, StringComparer.Ordinal))
            && (oneLink == null || Same(r.Link?.Id, oneLink.Id))
            && (!mineOnly || (r.Link != null ? Same(r.Link.Text("documentOwner"), s.PersonId) : OwnsApp(r.App.Id)))).ToList();
        var ordered = scoped.Where(r => state == "all" || r.State == state)
            .OrderBy(r => StateRank(r.State)).ThenBy(r => AppName(r.App), StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Type, StringComparer.Ordinal).ThenBy(r => r.Link?.Id ?? string.Empty, StringComparer.Ordinal)
            .ToList();
        var page = s.Page(ordered, out string? next);
        JsonTreeNode items = Arr(page.Select(r => Js.Obj(
            ("businessApplicationId", JsonTreeNode.FromString(r.App.Id)),
            ("applicationName", JsonTreeNode.FromString(AppName(r.App))),
            ("documentType", s.LabelledKey("document-type", r.Type)),
            ("state", JsonTreeNode.FromString(r.State)),
            ("documentLinkId", Js.Str(r.Link?.Id)),
            ("url", r.Link?.Text("url") is string url && url.StartsWith("https://", StringComparison.Ordinal) ? JsonTreeNode.FromString(url) : JsonTreeNode.Null),
            ("owner", s.PrincipalRef(r.Link?.Text("documentOwner"))),
            ("lastCheckedOn", ToolScope.DateOrNull(r.Link?.Text("lastCheckedOn"))),
            ("staleOn", ToolScope.DateOrNull(r.StaleOn)),
            ("requirementKey", Js.Str(r.Requirement)),
            ("requiredFrom", s.LabelledKey("lifecycle-stage", r.FromStage)),
            ("isAiCreated", AiCreated(r.Link)),
            ("performedBy", Stamp(s, r.Link)),
            ("recordUrl", JsonTreeNode.FromString(r.Link == null ? s.RecordUrl("business-application", r.App.Id) : s.RecordUrl("document-link", r.Link.Id))))));
        JsonTreeNode counts = Js.Obj(
            ("missing", JsonTreeNode.FromInt64(scoped.Count(r => r.State == "missing"))),
            ("stale", JsonTreeNode.FromInt64(scoped.Count(r => r.State == "stale"))),
            ("draft", JsonTreeNode.FromInt64(scoped.Count(r => r.State == "draft"))),
            ("current", JsonTreeNode.FromInt64(scoped.Count(r => r.State == "current"))));
        return s.Answer(
            Js.Obj(("items", items), ("counts", counts), ("totalCount", JsonTreeNode.FromInt64(ordered.Count)), ("skipToken", Js.OptStr(next))),
            Count(ordered.Count, "document", "documents") + " across " + Count(apps.Count, "application", "applications") + "; " + scoped.Count(r => r.State == "missing") + " missing and " + scoped.Count(r => r.State == "stale") + " stale. Missing is computed from configuration, never stored.");
    }

    private static string DocumentState(string? meaning)
    {
        return meaning switch
        {
            "missing" or "stale" or "draft" or "current" => meaning!,
            null => "missing",
            _ => "other",
        };
    }

    private static int StateRank(string state)
    {
        return state switch
        {
            "missing" => 0,
            "stale" => 1,
            "draft" => 2,
            "current" => 3,
            _ => 4,
        };
    }

    // ---- 70. list_deployment_targets ----

    // The places deployments land (O15; document 24, B24-11; the Places screen): each target with its kind, stage, change
    // path and link, when it was last seen, the latest version of each application deployed there (the 2021 story 3:
    // which version runs where), the pipelines mapped to it, and how many of its records are flagged. External IDs and
    // URLs are restricted read (document 26, section 6.2): a caller who holds only the reader role gets them as null.
    private static JsonTreeNode ListDeploymentTargets(ToolScope s)
    {
        IReadOnlyList<string> kinds = s.Strings("targetKindKeys");
        IReadOnlyList<string> stages = s.Strings("targetStageKeys");
        string state = s.Text("state") ?? "all";
        string? text = s.Text("text")?.ToLowerInvariant();
        bool ids = MayReadToolIds(s);
        var targets = s.Rows.Query(RowQuery.For("deployment-target"))
            .Where(t => kinds.Count == 0 || kinds.Contains(t.Text("targetKind") ?? string.Empty, StringComparer.Ordinal))
            .Where(t => stages.Count == 0 || stages.Contains(t.Text("targetStage") ?? string.Empty, StringComparer.Ordinal))
            .Where(t => state == "all" || (state == "retired") == (t.Text("retiredOn") != null))
            .Where(t => text == null || ((t.Text("labelAsSeen") ?? string.Empty) + " " + (t.Text("name") ?? string.Empty) + " " + (ids ? t.Text("externalId") ?? string.Empty : string.Empty)).ToLowerInvariant().Contains(text))
            .OrderBy(t => Js.Number(s.ConfigRow("target-stage", t.Text("targetStage"))?.Columns, "rank") ?? long.MaxValue)
            .ThenBy(t => t.Text("labelAsSeen") ?? string.Empty, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.Id, StringComparer.Ordinal)
            .ToList();
        var page = s.Page(targets, out string? next);
        JsonTreeNode items = Arr(page.Select(t => TargetItem(s, t, ids)));
        return s.Answer(Js.Obj(("items", items), ("totalCount", JsonTreeNode.FromInt64(targets.Count)), ("skipToken", Js.OptStr(next))),
            Count(targets.Count, "deployment target", "deployment targets") + ", by stage" + (state == "all" ? ", retired ones included" : string.Empty) + ". record_deployment takes only a registered target in service; save_deployment_target registers one or retires it.");
    }

    private static JsonTreeNode TargetItem(ToolScope s, StoredRow t, bool ids)
    {
        var records = s.Query("deployment-record", ("deploymentTarget", t.Id));
        var corrected = new HashSet<string>(records.Select(r => r.Text("correctsRecord")).Where(c => c != null).Select(c => c!), StringComparer.Ordinal);
        var latest = records.Where(r => !corrected.Contains(r.Id))
            .GroupBy(r => r.Text("businessApplication") ?? string.Empty, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(r => r.Text("finishedOn") ?? string.Empty, StringComparer.Ordinal).ThenByDescending(r => r.Id, StringComparer.Ordinal).First())
            .OrderBy(r => s.Rows.Get("business-application", r.Text("businessApplication") ?? string.Empty) is StoredRow app ? AppName(app) : string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var pipelines = s.Query("pipeline-map-entry", ("deploymentTarget", t.Id)).Select(e => e.Text("pipelineReference")).Where(p => p != null).Select(p => p!).Distinct(StringComparer.Ordinal)
            .Select(p => s.Rows.Get("pipeline-reference", p)).Where(p => p != null).Select(p => p!).OrderBy(p => p.Text("labelAsSeen") ?? p.Id, StringComparer.OrdinalIgnoreCase).ToList();
        string? url = t.Text("url");
        return Js.Obj(
            ("deploymentTargetId", JsonTreeNode.FromString(t.Id)),
            ("label", JsonTreeNode.FromString(t.Text("labelAsSeen") ?? t.Text("name") ?? t.Id)),
            ("targetKind", ListValue(s, "target-kind", t.Text("targetKind"))),
            ("targetStage", Js.Obj(
                ("key", JsonTreeNode.FromString(t.Text("targetStage") ?? "unknown")),
                ("label", JsonTreeNode.FromString(s.Label("target-stage", t.Text("targetStage")))),
                ("rank", JsonTreeNode.FromInt64(Js.Number(s.ConfigRow("target-stage", t.Text("targetStage"))?.Columns, "rank") ?? 0)))),
            ("changePath", Js.Obj(
                ("key", JsonTreeNode.FromString(t.Text("changePath") ?? "unknown")),
                ("label", JsonTreeNode.FromString(s.Label("change-path", t.Text("changePath")))),
                ("allowedRoutes", Js.Strs(Js.Strings(s.ConfigRow("change-path", t.Text("changePath"))?.Columns, "allowedRoutes"))))),
            ("linkKind", s.LabelledKey("link-kind", t.Text("linkKind") ?? "unknown")),
            ("externalId", ids ? Js.Str(t.Text("externalId")) : JsonTreeNode.Null),
            ("url", ids && url != null && url.StartsWith("https://", StringComparison.Ordinal) ? JsonTreeNode.FromString(url) : JsonTreeNode.Null),
            ("lastSeenOn", ToolScope.MomentOrNull(t.Text("lastSeenOn"))),
            ("retiredOn", ToolScope.DateOrNull(t.Text("retiredOn"))),
            ("retiredReason", Js.Str(t.Text("retiredReason"))),
            ("applications", Arr(latest.Select(r =>
            {
                StoredRow? app = s.Rows.Get("business-application", r.Text("businessApplication") ?? string.Empty);
                StoredRow? artifact = s.Rows.Get("artifact-version", r.Text("artifactVersion") ?? string.Empty);
                return Js.Obj(
                    ("businessApplicationId", Js.Str(app?.Id)),
                    ("name", JsonTreeNode.FromString(app == null ? string.Empty : AppName(app))),
                    ("artifactName", JsonTreeNode.FromString(artifact?.Text("artifactName") ?? string.Empty)),
                    ("artifactVersion", JsonTreeNode.FromString(artifact?.Text("version") ?? string.Empty)),
                    ("result", s.StatusValue(r, "result", "resultMeaning")),
                    ("finishedOn", ToolScope.MomentOrNull(r.Text("finishedOn"))),
                    ("deploymentRecordId", JsonTreeNode.FromString(r.Id)));
            }))),
            ("pipelines", Arr(pipelines.Select(p => Js.Obj(
                ("pipelineReferenceId", JsonTreeNode.FromString(p.Id)),
                ("label", JsonTreeNode.FromString(p.Text("labelAsSeen") ?? p.Text("externalId") ?? p.Id)),
                ("url", ids && (p.Text("url") ?? string.Empty).StartsWith("https://", StringComparison.Ordinal) ? JsonTreeNode.FromString(p.Text("url")!) : JsonTreeNode.Null))))),
            ("deploymentCount", JsonTreeNode.FromInt64(records.Count)),
            ("flaggedCount", JsonTreeNode.FromInt64(records.Count(IsFlagged))),

            // The data locations at this target (O25 Deployment Target), so Places links to the Data page's locations as the
            // Locations tab links back (docs/architecture/data-taxonomy.md).
            ("dataLocations", Arr(s.Query("data-location", ("deploymentTarget", t.Id)).Where(l => l.Text("retiredOn") == null)
                .OrderBy(l => l.Text("labelAsSeen") ?? l.Id, StringComparer.OrdinalIgnoreCase).ThenBy(l => l.Id, StringComparer.Ordinal)
                .Select(l => Js.Obj(
                    ("dataLocationId", JsonTreeNode.FromString(l.Id)),
                    ("label", JsonTreeNode.FromString(l.Text("labelAsSeen") ?? (ids ? l.Text("externalId") : null) ?? l.Id)),
                    ("platform", s.LabelledKey("data-platform", l.Text("dataPlatform"))),
                    ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("data-location", l.Id))))))),
            ("isAiCreated", AiCreated(t)),
            ("performedBy", Stamp(s, t)),
            ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("deployment-target", t.Id))));
    }

    // External IDs and URLs of components, targets and pipelines are restricted read: every role but the reader role
    // (document 26, section 6.2).
    private static bool MayReadToolIds(ToolScope s)
    {
        return s.Session.RoleKeys.Any(r => r != "reader");
    }

    // ---- 71. save_deployment_target ----

    // Registers a place deployments land, or changes one (O15, class M; document 24, B24-11). Served only to a person: the
    // stage decides the gate level a deployment there meets and the change path decides which routes are flagged, so a
    // person states them (D-36). The platform admin holds the default (document 26, section 5.3). The link kind must be a
    // Microsoft resource kind, checked by OP-08 with its URL pattern; Link Kind and External Id are the business key, so a
    // second target with the same pair is refused and the first is named.
    private static JsonTreeNode SaveDeploymentTarget(ToolScope s)
    {
        if (s.IsAiCaller)
        {
            throw s.Refused(McpErrorCodes.AiCannotDoThis, "A person registers a deployment target in the product's web app: its stage and change path decide which rules apply.");
        }

        string? existingId = s.Text("deploymentTargetId");
        StoredRow? existing = existingId == null ? null : s.RequireRow("deployment-target", existingId, "deploymentTargetId");
        bool? retire = s.Flag("retire");
        if (retire.HasValue)
        {
            return RetireDeploymentTarget(s, existing, retire.Value);
        }

        if (existing?.Text("retiredOn") != null)
        {
            throw s.Refused(McpErrorCodes.RuleViolation, "The target " + (existing.Text("labelAsSeen") ?? existing.Id) + " is retired, so it stays as it was when it retired. Return it to service first (retire false), or register the target that replaced it.");
        }

        string? kind = s.Text("targetKindKey");
        string? stage = s.Text("targetStageKey");
        string? path = s.Text("changePathKey");
        string? linkKind = s.Text("linkKindKey");
        string? externalId = s.Text("externalId")?.Trim();
        string? label = s.Text("labelAsSeen")?.Trim();
        if (existing == null)
        {
            foreach ((string? value, string field) in new[] { (kind, "targetKindKey"), (stage, "targetStageKey"), (path, "changePathKey"), (linkKind, "linkKindKey"), (externalId, "externalId"), (label, "labelAsSeen") })
            {
                if (string.IsNullOrEmpty(value))
                {
                    throw s.Invalid(field, field + " is required to register a target.");
                }
            }
        }

        RequireListValue(s, "target-kind", kind, "targetKindKey");
        if (stage != null)
        {
            s.RequireKey("target-stage", stage, "targetStageKey");
        }

        if (path != null)
        {
            s.RequireKey("change-path", path, "changePathKey");
        }

        if (linkKind != null)
        {
            s.RequireKey("link-kind", linkKind, "linkKindKey");
        }

        string keyKind = linkKind ?? existing?.Text("linkKind") ?? string.Empty;
        string keyId = externalId ?? existing?.Text("externalId") ?? string.Empty;
        StoredRow? same = s.Query("deployment-target", ("externalId", keyId)).FirstOrDefault(t => t.Text("linkKind") == keyKind && (existing == null || t.Id != existing.Id));
        if (same != null)
        {
            throw s.Invalid("externalId", "A target with this link kind and external ID is already registered: " + (same.Text("labelAsSeen") ?? same.Id) + ". Change that target instead.");
        }

        var columns = new List<(string, JsonTreeNode?)>
        {
            ("targetKind", Js.OptStr(kind)),
            ("targetStage", Js.OptStr(stage)),
            ("changePath", Js.OptStr(path)),
            ("linkKind", Js.OptStr(linkKind)),
            ("externalId", Js.OptStr(externalId)),
            ("url", Js.OptStr(s.Text("url"))),
            ("labelAsSeen", Js.OptStr(label)),
            ("name", Js.OptStr(label)),
            ("description", Js.OptStr(s.Text("description"))),
            ("lastSeenOn", Js.Utc(s.Now)),
        };
        ComposedWrite write = existing == null
            ? s.Create("deployment-target", Js.Obj(columns.ToArray()))
            : s.Update("deployment-target", existing.Id, Js.Obj(columns.ToArray()));
        StoredRow written = s.Rows.Get("deployment-target", write.RowId)!;
        bool changesRules = existing != null && (stage != null && stage != existing.Text("targetStage") || path != null && path != existing.Text("changePath"));
        if (changesRules && s.Query("deployment-record", ("deploymentTarget", existing!.Id)).Count > 0)
        {
            s.Call.Warn("target.rules-changed", "The target's stage or change path changed. Deployments recorded before keep the flags and gate level they were recorded with; new ones meet the new stage and path.", "Deployment Target", existing.Id);
        }

        JsonTreeNode data = ToolScope.Merge(
            s.WriteResult(written, write.Created, hasStatus: false),
            ("deploymentTargetId", s.Call.IsDryRun && write.Created ? JsonTreeNode.Null : JsonTreeNode.FromString(written.Id)),
            ("target", TargetItem(s, written, true)));
        return s.Answer(
            data,
            // "can now be recorded" only where it is new (wave 4b QA walk): a changed target took deployments before.
            existing == null ? "The deployment target " + label + " is registered. Deployments to it can now be recorded." : "The deployment target " + (written.Text("labelAsSeen") ?? written.Id) + " is changed.",
            s.Call.IsDryRun && write.Created ? null : s.RecordUrl("deployment-target", written.Id));
    }

    // Retires a target, or returns a retired one to service (document 24, B24-18; the Places screen). A retired target stays
    // in the register with its deployment records, pipelines and history; record_deployment refuses a new deployment to it
    // with a reason (deployment.target-retired), and map_pipeline refuses to map a pipeline to it. Choice: retiring takes
    // today's date and a reason, and nothing else changes in the same call, so the record says plainly when and why. A
    // retired target may come back into service (retire false), because its link kind and external ID stay its business key
    // and a second target with the same pair is refused: bringing it back is the only way to use that environment again.
    private static JsonTreeNode RetireDeploymentTarget(ToolScope s, StoredRow? existing, bool retire)
    {
        if (existing == null)
        {
            throw s.Invalid("deploymentTargetId", "deploymentTargetId names the target to " + (retire ? "retire" : "return to service") + ". A new target is registered in service.");
        }

        foreach (string field in new[] { "targetKindKey", "targetStageKey", "changePathKey", "linkKindKey", "externalId", "url", "labelAsSeen", "description" })
        {
            if (Js.Has(s.Args, field))
            {
                throw s.Invalid(field, (retire ? "Retiring" : "Returning to service") + " changes nothing else on the target. Save " + field + " in a call of its own.");
            }
        }

        string label = existing.Text("labelAsSeen") ?? existing.Id;
        bool isRetired = existing.Text("retiredOn") != null;
        string? reason = s.Text("retiredReason")?.Trim();
        if (retire && string.IsNullOrEmpty(reason))
        {
            throw s.Invalid("retiredReason", "retiredReason is required to retire a target: say why, such as the environment that replaced it.");
        }

        if (!retire && reason != null)
        {
            throw s.Invalid("retiredReason", "retiredReason belongs to retiring a target, not to returning it to service.");
        }

        if (retire == isRetired)
        {
            throw s.Refused(McpErrorCodes.RuleViolation, "The target " + label + (isRetired ? " is already retired, on " + DayText(existing.Text("retiredOn")) + "." : " is in service already."));
        }

        ComposedWrite write = s.Update("deployment-target", existing.Id, Js.Obj(
            ("retiredOn", retire ? JsonTreeNode.FromString(s.PersonToday) : JsonTreeNode.Null),
            ("retiredReason", retire ? JsonTreeNode.FromString(reason!) : JsonTreeNode.Null)));
        StoredRow written = s.Rows.Get("deployment-target", write.RowId)!;
        if (retire)
        {
            int pipelines = s.Query("pipeline-map-entry", ("deploymentTarget", existing.Id)).Count;
            int planned = s.Query("change-request", ("plannedTarget", existing.Id)).Count(c => c.Text("statusMeaning") is "draft" or "submitted" or "in-progress");
            if (pipelines > 0)
            {
                s.Call.Warn("target.retired-pipelines", Count(pipelines, "pipeline map entry still points", "pipeline map entries still point") + " at the target. They stay as history; map the pipelines to the target that replaced it.", "Deployment Target", existing.Id);
            }

            if (planned > 0)
            {
                s.Call.Warn("target.retired-planned", Count(planned, "open change still plans", "open changes still plan") + " to deploy to the target. A deployment to it will be refused; change the planned target.", "Deployment Target", existing.Id);
            }
        }

        JsonTreeNode data = ToolScope.Merge(
            s.WriteResult(written, false, hasStatus: false),
            ("deploymentTargetId", JsonTreeNode.FromString(written.Id)),
            ("target", TargetItem(s, written, true)));
        return s.Answer(
            data,
            retire
                ? "The deployment target " + label + " is retired. Its deployment records stay, and a new deployment to it is refused with the reason."
                : "The deployment target " + label + " is back in service. Deployments to it can be recorded again.",
            s.RecordUrl("deployment-target", written.Id));
    }

    // ---- 72. list_gap_check_runs ----

    // The gap check's run history (O22; the Gap check screen): who ran it, with which admin role, over which estates,
    // when it started and finished, and what it found. claim-visibility "admins-and-claimant" keeps runs to the platform
    // admin, the AI lead and the auditor; anyone else counts them in couldNotRead (document 26, section 5.3).
    private static JsonTreeNode ListGapCheckRuns(ToolScope s)
    {
        string? runBy = s.Text("runByObjectId");
        string? runId = s.Text("gapCheckRunId");
        DateTime? from = Js.ParseMoment(s.Text("from"));
        DateTime? to = Js.ParseMoment(s.Text("to"));
        bool restricted = s.Setting("claim-visibility") == "admins-and-claimant" && !(s.HoldsRole("platform-admin") || s.HoldsRole("ai-lead") || s.HoldsRole("auditor"));

        // One run by its ID (the run's record page) reads that row only; claim-visibility still applies.
        var runs = runId != null
            ? new List<StoredRow> { s.RequireRow("gap-check-run", runId, "gapCheckRunId") }
            : s.Rows.Query(RowQuery.For("gap-check-run")).ToList();
        if (restricted)
        {
            s.HideFromCaller("gap-check-run", runs.Count);
            runs.Clear();
        }

        var ordered = runs
            .Where(r => runBy == null || Same(r.Text("runBy"), runBy))
            .Where(r =>
            {
                DateTime? started = Js.ParseMoment(r.Text("startedOn"));
                return (!from.HasValue || (started.HasValue && started.Value.Date >= from.Value.Date)) && (!to.HasValue || (started.HasValue && started.Value.Date <= to.Value.Date));
            })
            .OrderByDescending(r => r.Text("startedOn") ?? string.Empty, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
        var page = s.Page(ordered, out string? next);
        JsonTreeNode items = Arr(page.Select(r =>
        {
            var claims = s.Query("claim", ("lastSeenRun", r.Id));
            return Js.Obj(
                ("gapCheckRunId", JsonTreeNode.FromString(r.Id)),
                ("runBy", s.PrincipalRef(r.Text("runBy"))),
                ("adminRoleUsed", Js.Str(r.Text("adminRoleUsed"))),
                ("estatesCovered", Arr(Js.Strings(r.Columns, "estatesCovered").Select(k => ListValue(s, "estate", k)))),
                ("startedOn", ToolScope.MomentOrNull(r.Text("startedOn"))),
                ("finishedOn", ToolScope.MomentOrNull(r.Text("finishedOn"))),
                ("state", JsonTreeNode.FromString(r.Text("finishedOn") == null ? "not-finished" : "finished")),
                ("itemsSeen", r.Number("itemsSeen") is long seen ? JsonTreeNode.FromInt64(seen) : JsonTreeNode.Null),
                ("newClaims", r.Number("newClaims") is long added ? JsonTreeNode.FromInt64(added) : JsonTreeNode.Null),
                ("vanishedClaims", r.Number("vanishedClaims") is long vanished ? JsonTreeNode.FromInt64(vanished) : JsonTreeNode.Null),
                // Open claims last seen in the run, apart by finding (wave 4b review): an item no application holds, and a
                // registered component that went, so the first matches the "Not registered" count.
                ("openClaimsLastSeen", JsonTreeNode.FromInt64(claims.Count(c => c.Text("statusMeaning") == "open" && c.Text("findingKind") != "vanished"))),
                ("openVanishedLastSeen", JsonTreeNode.FromInt64(claims.Count(c => c.Text("statusMeaning") == "open" && c.Text("findingKind") == "vanished"))),
                ("isAiCreated", AiCreated(r)),
                ("performedBy", Stamp(s, r)),
                ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("gap-check-run", r.Id))));
        }));
        return s.Answer(Js.Obj(("items", items), ("totalCount", JsonTreeNode.FromInt64(ordered.Count)), ("skipToken", Js.OptStr(next))),
            restricted ? "Gap check runs are shown to the platform admin, the AI lead and the auditor (claim-visibility)." : Count(ordered.Count, "gap check run", "gap check runs") + ", newest first.");
    }

    // ---- 73. save_dependency ----

    // Records that an application depends on another application or on a component, changes it, or ends it (O04, class
    // E; document 24, B24-11; the dependency map's write). As reported: a dependency is a fact the caller states, stamped
    // with the actor kind. One open dependency per From Application, target and Dependency Type (the model's business key):
    // a save for a pair that is already linked changes that link. Ended On ends it, and an ended dependency never changes
    // again (OP-08, B25-13). The owner of the From Application, an architect or the platform admin writes it.
    private static JsonTreeNode SaveDependency(ToolScope s)
    {
        s.RequireAiState("as-reported");
        string? dependencyId = s.Text("dependencyId");
        StoredRow? existing = dependencyId == null ? null : s.RequireRow("dependency", dependencyId, "dependencyId");
        string fromId = existing?.Text("fromApplication") ?? s.Text("fromApplicationId") ?? throw s.Invalid("fromApplicationId", "fromApplicationId is required for a new dependency.");
        if (existing != null && s.Text("fromApplicationId") is string given && !Same(given, fromId))
        {
            throw s.Invalid("fromApplicationId", "A dependency keeps its From Application. End this one and record a new one instead.");
        }

        StoredRow from = s.RequireRow("business-application", fromId, "fromApplicationId");
        RequireOwnerRelation(s, from.Id);
        string? toApp = s.Text("toApplicationId");
        string? toComponent = s.Text("technicalComponentId");
        if (toApp != null)
        {
            s.RequireRow("business-application", toApp, "toApplicationId");
        }

        if (toComponent != null)
        {
            s.RequireRow("technical-component", toComponent, "technicalComponentId");
        }

        string? type = s.Text("dependencyTypeKey");
        if (type != null)
        {
            s.RequireKey("dependency-type", type, "dependencyTypeKey");
        }

        RequireListValue(s, "criticality", s.Text("criticalityKey"), "criticalityKey");
        if (existing == null)
        {
            if (type == null)
            {
                throw s.Invalid("dependencyTypeKey", "dependencyTypeKey is required for a new dependency.");
            }

            // The business key: one open dependency per From Application, target and type. A save for a pair already
            // linked changes that link instead of adding a second one.
            existing = s.Query("dependency", ("fromApplication", from.Id))
                .FirstOrDefault(d => d.Text("endedOn") == null && d.Text("dependencyType") == type && string.Equals(d.Text("toApplication"), toApp, StringComparison.OrdinalIgnoreCase) && string.Equals(d.Text("toComponent"), toComponent, StringComparison.OrdinalIgnoreCase)
                    && (toApp != null || toComponent != null));
        }

        string? endedOn = s.Text("endedOn");
        var columns = new List<(string, JsonTreeNode?)>
        {
            ("toApplication", toApp == null ? null : JsonTreeNode.FromString(toApp)),
            ("toComponent", toComponent == null ? null : JsonTreeNode.FromString(toComponent)),
            ("dependencyType", Js.OptStr(type)),
            ("criticality", Js.OptStr(s.Text("criticalityKey"))),
            ("notes", Js.OptStr(s.Text("notes"))),
            ("endedOn", Js.OptStr(endedOn)),
        };
        ComposedWrite write;
        if (existing == null)
        {
            columns.Add(("fromApplication", JsonTreeNode.FromString(from.Id)));
            columns.Add(("name", JsonTreeNode.FromString(AppName(from) + " " + LowerFirst(s.Label("dependency-type", type)))));
            write = s.Create("dependency", Js.Obj(columns.ToArray()));
        }
        else
        {
            // A change of target replaces the other end, so the guard judges the row as it would stand.
            if (toApp != null && existing.Text("toComponent") != null)
            {
                columns.Add(("toComponent", JsonTreeNode.Null));
            }

            if (toComponent != null && existing.Text("toApplication") != null)
            {
                columns.Add(("toApplication", JsonTreeNode.Null));
            }

            write = s.Update("dependency", existing.Id, Js.Obj(columns.ToArray()));
        }

        StoredRow written = s.Rows.Get("dependency", write.RowId)!;
        StoredRow? target = written.Text("toApplication") is string ta ? s.Rows.Get("business-application", ta) : null;
        StoredRow? component = written.Text("toComponent") is string tc ? s.Rows.Get("technical-component", tc) : null;
        JsonTreeNode data = ToolScope.Merge(
            s.WriteResult(written, write.Created, hasStatus: false),
            ("dependencyId", s.Call.IsDryRun && write.Created ? JsonTreeNode.Null : JsonTreeNode.FromString(written.Id)),
            ("fromApplication", Js.Obj(("businessApplicationId", JsonTreeNode.FromString(from.Id)), ("name", JsonTreeNode.FromString(AppName(from))))),
            ("to", Js.Obj(
                ("businessApplicationId", Js.Str(target?.Id)),
                ("technicalComponentId", Js.Str(component?.Id)),
                ("name", JsonTreeNode.FromString(target != null ? AppName(target) : component?.Text("labelAsSeen") ?? component?.Text("externalId") ?? string.Empty)))),
            ("dependencyType", s.LabelledKey("dependency-type", written.Text("dependencyType") ?? "unknown")),
            ("criticality", ListValue(s, "criticality", written.Text("criticality"))),
            ("endedOn", ToolScope.DateOrNull(written.Text("endedOn"))));
        string what = AppName(from) + " " + LowerFirst(s.Label("dependency-type", written.Text("dependencyType"))) + " " + Js.Text(Js.Member(data, "to"), "name");
        return s.Answer(
            data,
            (endedOn != null ? "The dependency is ended: " : write.Created ? "The dependency is recorded, as reported: " : "The dependency is changed, as reported: ") + what + ".",
            s.RecordUrl("business-application", from.Id),
            ToolScope.Next("get_dependency_impact", ("businessApplicationId", JsonTreeNode.FromString(from.Id)), ("direction", JsonTreeNode.FromString("dependencies"))));
    }

    // ---- 74. save_portfolio ----

    // Creates or changes a portfolio (O01, class M; document 24, B24-11). Served only to a person: a portfolio's place in
    // the tree scopes approval rules, decision rights and portfolio access, and its accountable person answers for it. The
    // platform admin creates, moves (parent), names the accountable person and retires (Is Active); the PMO edits the
    // name, description and level of an existing portfolio (document 26, section 5.3: "PMO for its own fields"). A
    // portfolio never sits inside itself or its own children, and two siblings never share a name (the business key).
    private static JsonTreeNode SavePortfolio(ToolScope s)
    {
        if (s.IsAiCaller)
        {
            throw s.Refused(McpErrorCodes.AiCannotDoThis, "A person changes a portfolio in the product's web app: its place in the tree decides which rules apply.");
        }

        bool admin = s.HoldsRole("platform-admin");
        string? portfolioId = s.Text("portfolioId");
        StoredRow? existing = portfolioId == null ? null : s.RequireRow("portfolio", portfolioId, "portfolioId");
        string? parent = s.Text("parentPortfolioId");
        bool clearParent = s.Flag("clearParent") == true;
        string? accountable = s.Text("accountablePrincipalObjectId");
        bool clearAccountable = s.Flag("clearAccountable") == true;
        bool? isActive = s.Flag("isActive");
        bool adminField = existing == null || parent != null || clearParent || accountable != null || clearAccountable || isActive.HasValue;
        if (adminField && !admin)
        {
            throw s.Refused(McpErrorCodes.NotAuthorized, existing == null
                ? "Only the platform admin creates a portfolio."
                : "Only the platform admin moves a portfolio, names its accountable person or retires it. The PMO edits its name, description and level.");
        }

        string? name = s.Text("name")?.Trim();
        string? level = s.Text("levelKey");
        if (existing == null && (string.IsNullOrEmpty(name) || level == null))
        {
            throw s.Invalid(string.IsNullOrEmpty(name) ? "name" : "levelKey", "A new portfolio needs a name and a level.");
        }

        RequireListValue(s, "portfolio-level", level, "levelKey");
        if (parent != null)
        {
            StoredRow parentRow = s.RequireRow("portfolio", parent, "parentPortfolioId");
            if (parentRow.Column("isActive") is JsonTreeNode active && active.Kind == JsonTreeKind.False)
            {
                throw s.Invalid("parentPortfolioId", (parentRow.Text("name") ?? parent) + " is retired, so nothing new goes inside it.");
            }

            if (existing != null && PortfolioPath(s, parent).Contains(existing.Id, StringComparer.OrdinalIgnoreCase))
            {
                throw s.Refused(McpErrorCodes.RuleViolation, "A portfolio never sits inside itself or one of its own children (document 22, O01: no cycles).");
            }
        }

        string? accountableId = accountable == null ? null : AccountablePerson(s, accountable);
        string? newParent = clearParent ? null : parent ?? existing?.Text("parentPortfolio");
        string? newName = name ?? existing?.Text("name");
        StoredRow? sibling = s.Rows.Query(RowQuery.For("portfolio")).FirstOrDefault(p => (existing == null || p.Id != existing.Id)
            && string.Equals(p.Text("name"), newName, StringComparison.OrdinalIgnoreCase)
            && (newParent == null ? p.Text("parentPortfolio") == null : Same(p.Text("parentPortfolio"), newParent)));
        if (sibling != null)
        {
            throw s.Invalid("name", "Another portfolio in the same place is already named " + newName + ". Two siblings never share a name.");
        }

        var columns = new List<(string, JsonTreeNode?)>
        {
            ("name", Js.OptStr(name)),
            ("description", Js.OptStr(s.Text("description"))),
            ("level", Js.OptStr(level)),
            ("parentPortfolio", clearParent ? JsonTreeNode.Null : Js.OptStr(parent)),
            ("accountablePrincipal", clearAccountable ? JsonTreeNode.Null : Js.OptStr(accountableId)),
            ("isActive", isActive.HasValue ? JsonTreeNode.FromBoolean(isActive.Value) : existing == null ? JsonTreeNode.True : null),
        };
        bool wasActive = existing == null || !(existing.Column("isActive") is JsonTreeNode before && before.Kind == JsonTreeKind.False);
        ComposedWrite write = existing == null ? s.Create("portfolio", Js.Obj(columns.ToArray())) : s.Update("portfolio", existing.Id, Js.Obj(columns.ToArray()));
        StoredRow written = s.Rows.Get("portfolio", write.RowId)!;
        // Retire and Bring back say what they did, not only "saved" (wave 4b QA walk).
        string done = write.Created ? "created" : isActive == false && wasActive ? "retired" : isActive == true && !wasActive ? "back in service" : "saved";
        if (isActive == false)
        {
            int apps = s.Rows.Query(RowQuery.For("business-application")).Count(a => a.Text("statusMeaning") != "archived" && Same(a.Text("portfolio"), written.Id));
            int children = s.Query("portfolio", ("parentPortfolio", written.Id)).Count(p => !(p.Column("isActive") is JsonTreeNode c && c.Kind == JsonTreeKind.False));
            if (apps > 0 || children > 0)
            {
                s.Call.Warn("portfolio.retired-not-empty", "The portfolio is retired, and " + Count(apps, "application", "applications") + " and " + Count(children, "active child portfolio", "active child portfolios") + " still sit in it. Move them to keep their rules clear.", "Portfolio", written.Id);
            }
        }

        JsonTreeNode? activeNode = written.Column("isActive");
        JsonTreeNode data = ToolScope.Merge(
            s.WriteResult(written, write.Created, hasStatus: false),
            ("portfolioId", s.Call.IsDryRun && write.Created ? JsonTreeNode.Null : JsonTreeNode.FromString(written.Id)),
            ("portfolio", Js.Obj(
                ("name", JsonTreeNode.FromString(written.Text("name") ?? written.Id)),
                ("level", ListValue(s, "portfolio-level", written.Text("level"))),
                ("parentPortfolioId", Js.Str(written.Text("parentPortfolio"))),
                ("path", Arr(PortfolioPath(s, written.Id).AsEnumerable().Reverse().Select(id => Js.Obj(("portfolioId", JsonTreeNode.FromString(id)), ("name", JsonTreeNode.FromString(s.Rows.Get("portfolio", id)?.Text("name") ?? id)))))),
                ("accountablePrincipal", s.PrincipalRef(written.Text("accountablePrincipal"))),
                ("isActive", JsonTreeNode.FromBoolean(!(activeNode != null && activeNode.Kind == JsonTreeKind.False))))));
        return s.Answer(
            data,
            "The portfolio " + (written.Text("name") ?? written.Id) + " is " + done + ". Approval rules and decision rights scoped to a portfolio apply to everything inside it.",
            s.Call.IsDryRun && write.Created ? null : s.RecordUrl("portfolio", written.Id));
    }

    // The person who answers for a portfolio: someone the store knows, signed in as a user, not erased, not disabled or
    // deleted, not past a leave date. The label shown is always the store's.
    private static string AccountablePerson(ToolScope s, string objectId)
    {
        const string Field = "accountablePrincipalObjectId";
        StoredRow person = s.Principal(objectId) ?? throw s.Invalid(Field, "No person with object ID " + objectId + " is known to the product. Find the person with find_people.");
        if (person.Text("principalKind") != "user" || person.Flag("isErased"))
        {
            throw s.Invalid(Field, "The accountable person is a person signed in as a user; " + objectId + " is not.");
        }

        string? state = person.Text("directoryState");
        if (state == "disabled" || state == "deleted" || (person.Text("leavesOn") is string leaves && string.CompareOrdinal(leaves, s.Today) <= 0))
        {
            throw s.Invalid(Field, (person.Text("displayLabel") ?? objectId) + " has left or is disabled in the directory, so they cannot answer for the portfolio. Name someone else.");
        }

        return person.Text("entraObjectId") ?? objectId;
    }

    // ---- The deployment record as get_deployments and get_evidence list it ----

    private static JsonTreeNode DeploymentItem(ToolScope s, StoredRow r)
    {
        StoredRow? application = s.Rows.Get("business-application", r.Text("businessApplication") ?? string.Empty);
        StoredRow? targetRow = s.Rows.Get("deployment-target", r.Text("deploymentTarget") ?? string.Empty);
        StoredRow? artifact = s.Rows.Get("artifact-version", r.Text("artifactVersion") ?? string.Empty);
        return Js.Obj(
            ("deploymentRecordId", JsonTreeNode.FromString(r.Id)),
            ("businessApplicationId", Js.Str(application?.Id)),
            ("applicationName", Js.Str(application == null ? null : AppName(application))),
            ("deploymentTargetId", Js.Str(targetRow?.Id)),
            ("targetLabel", Js.Str(targetRow?.Text("labelAsSeen"))),
            ("targetStageKey", Js.Str(targetRow?.Text("targetStage"))),
            ("artifactName", JsonTreeNode.FromString(artifact?.Text("artifactName") ?? string.Empty)),
            ("artifactVersion", JsonTreeNode.FromString(artifact?.Text("version") ?? string.Empty)),
            ("result", s.StatusValue(r, "result", "resultMeaning")),
            ("finishedOn", ToolScope.MomentOrNull(r.Text("finishedOn"))),
            ("route", Route(s, r)),
            ("flags", RecordFlags(s, r, null)),
            ("gateLevelApplied", JsonTreeNode.FromString(r.Text("gateLevelApplied") ?? "record")),
            ("runUrl", Js.Str(r.Text("runUrl"))),
            ("correctsRecordId", Js.Str(r.Text("correctsRecord"))),
            ("changeRequestId", Js.Str(r.Text("changeRequest"))),
            ("pipelineReferenceId", Js.Str(r.Text("pipelineReference"))),
            ("versionBefore", Js.Str(r.Text("versionBefore"))),
            ("versionAfter", Js.Str(r.Text("versionAfter"))),
            ("startedByAiClient", Js.Str(r.Text("startedByAiClient"))),
            ("isAiCreated", AiCreated(r)),
            ("performedBy", Stamp(s, r)),
            ("configurationSetVersion", Version(r)),
            ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("deployment-record", r.Id))));
    }
}
