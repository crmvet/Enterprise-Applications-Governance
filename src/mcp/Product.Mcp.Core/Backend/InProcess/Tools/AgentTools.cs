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

// Tools 93 and 94 (document 24, B24-18; the Agents screen; playbook chapter 11, D-20).
// - list_agents: every agent in one call, with its autonomy level (agreed, waiting or missing), owners and owner gaps,
//   its last security review, its go-live gate state, its flags and its data uses. Any role reads it, as find_applications
//   and get_application_card. This replaces B24-11's choice of no list_agents: the screen needed one call per agent and
//   five reads each, and the go-live gate state was in none of them.
// - request_security_review: opens the security review of an agent or an application, as request_architecture_review opens
//   an architecture review. A request, never a decision: it asks the security reviewer to work, decides nothing, and the
//   review is completed only once its outcome decision is accepted. An AI client may request it for the person it acts
//   for, as with architecture reviews (working default, open design question 9: AI May Submit is yes for review requests);
//   the Tool Policy row can tighten that to read-only.
internal static partial class ToolHandlers
{
    // ---- 93. list_agents ----

    // The application kinds that are agents: the kinds a gate requirement for the autonomy level decision is scoped to
    // (Balanced: agent), else the kind keyed agent. Read from the configuration, never from a fixed list (D-06).
    private static HashSet<string> AgentKinds(ToolScope s)
    {
        var kinds = new HashSet<string>(
            s.ActiveRows("gate-requirement").Where(r => Js.Text(r.Columns, "requiredDecisionTypeKey") == EngineKeys.AutonomyLevelDecisionType)
                .Select(r => Js.Text(r.Columns, "applicationKindKey")).Where(k => k != null && k != "*").Select(k => k!),
            StringComparer.Ordinal);
        if (kinds.Count == 0 && s.ConfigRow("application-kind", "agent") is SnapshotRow agent && agent.IsActive)
        {
            kinds.Add("agent");
        }

        return kinds;
    }

    private static JsonTreeNode ListAgents(ToolScope s)
    {
        HashSet<string> kinds = s.Strings("applicationKindKeys").Count > 0 ? new HashSet<string>(s.Strings("applicationKindKeys"), StringComparer.Ordinal) : AgentKinds(s);
        string? text = s.Text("text")?.ToLowerInvariant();
        IReadOnlyList<string> tiers = s.Strings("tierKeys");
        string autonomy = s.Text("autonomy") ?? "any";
        bool flaggedOnly = s.Flag("flaggedOnly") ?? false;
        string? portfolio = s.Text("portfolioId");
        if (portfolio != null)
        {
            s.RequireRow("portfolio", portfolio, "portfolioId");
        }

        string? goLiveGate = s.ActiveRows("gate-requirement").Where(r => Js.Text(r.Columns, "requiredDecisionTypeKey") == EngineKeys.AutonomyLevelDecisionType).Select(r => Js.Text(r.Columns, "gateKey")).FirstOrDefault(g => g != null)
            ?? (s.ConfigRow("gate", "go-live")?.IsActive == true ? "go-live" : null);
        bool ids = MayReadToolIds(s);
        var agents = new List<AgentRow>();
        foreach (StoredRow app in s.Rows.Query(RowQuery.For("business-application")))
        {
            if (!kinds.Contains(app.Text("applicationKind") ?? string.Empty) || app.Text("statusMeaning") == "archived"
                || (tiers.Count > 0 && !tiers.Contains(app.Text("tier") ?? string.Empty, StringComparer.Ordinal))
                || (portfolio != null && !PortfolioPath(s, app.Text("portfolio")).Contains(portfolio, StringComparer.Ordinal))
                || (text != null && !(AppName(app) + " " + (app.Text("purpose") ?? string.Empty) + " " + (app.Text("recordNumber") ?? string.Empty)).ToLowerInvariant().Contains(text)))
            {
                continue;
            }

            AgentRow agent = AgentOf(s, app, goLiveGate, ids);
            if ((autonomy == "agreed" && !agent.AutonomyAgreed) || (autonomy == "missing" && agent.AutonomyAgreed) || (flaggedOnly && agent.FlagCount == 0))
            {
                continue;
            }

            agents.Add(agent);
        }

        var ordered = agents.OrderByDescending(a => a.FlagCount).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ThenBy(a => a.Id, StringComparer.Ordinal).ToList();
        List<AgentRow> page = s.Page(ordered, out string? next);
        int missing = agents.Count(a => !a.AutonomyAgreed);
        int noReview = agents.Count(a => a.SecurityReviewMissing);
        int flagged = agents.Count(a => a.FlagCount > 0);
        JsonTreeNode data = Js.Obj(
            ("applicationKindKeys", Js.Strs(kinds.OrderBy(k => k, StringComparer.Ordinal))),
            ("goLiveGate", s.LabelledKey("gate", goLiveGate)),
            ("counts", Js.Obj(
                ("agents", JsonTreeNode.FromInt64(agents.Count)),
                ("autonomyMissing", JsonTreeNode.FromInt64(missing)),
                ("autonomyWaiting", JsonTreeNode.FromInt64(agents.Count(a => a.AutonomyWaiting))),
                ("securityReviewMissing", JsonTreeNode.FromInt64(noReview)),
                ("goLiveNotReady", JsonTreeNode.FromInt64(agents.Count(a => a.GoLiveReady == false))),
                ("flagged", JsonTreeNode.FromInt64(flagged)))),
            ("items", Arr(page.Select(a => a.Json))),
            ("totalCount", JsonTreeNode.FromInt64(ordered.Count)),
            ("skipToken", Js.OptStr(next)));
        return s.Answer(data, Count(agents.Count, "agent", "agents") + ": " + missing + " with no agreed autonomy level, " + noReview + " with a security review the go-live gate needs and lacks, " + flagged + " flagged.");
    }

    private sealed class AgentRow
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public bool AutonomyAgreed { get; set; }

        public bool AutonomyWaiting { get; set; }

        public bool SecurityReviewMissing { get; set; }

        public bool? GoLiveReady { get; set; }

        public int FlagCount { get; set; }

        public JsonTreeNode Json { get; set; } = JsonTreeNode.Null;
    }

    private static AgentRow AgentOf(ToolScope s, StoredRow app, string? goLiveGate, bool ids)
    {
        var flags = new List<JsonTreeNode>();
        void Flag(string kind, string label, string? recordEntity = null, string? recordId = null)
        {
            flags.Add(Js.Obj(
                ("kind", JsonTreeNode.FromString(kind)),
                ("label", JsonTreeNode.FromString(label)),
                ("recordUrl", recordEntity == null || recordId == null ? JsonTreeNode.Null : JsonTreeNode.FromString(s.RecordUrl(recordEntity, recordId)))));
        }

        // Autonomy: the agreed level on the application (written by an accepted autonomy level decision), a proposed decision
        // waiting for people, or neither.
        string? agreed = app.Text("agreedAutonomyLevel");
        var decisions = s.Query("decision", ("subjectApplication", app.Id));
        StoredRow? waiting = NewestFirst(decisions.Where(d => d.Text("decisionType") == EngineKeys.AutonomyLevelDecisionType && d.Text("statusMeaning") == "proposed")).FirstOrDefault();
        string? proposedLevel = waiting == null ? null : Js.Text(waiting.Column("proposedChangesJson"), "autonomyLevelKey");
        bool requiredAtGoLive = s.ActiveRows("gate-requirement").Any(r => Js.Text(r.Columns, "requiredDecisionTypeKey") == EngineKeys.AutonomyLevelDecisionType
            && (Js.Text(r.Columns, "applicationKindKey") is not string k || k == "*" || k == app.Text("applicationKind"))
            && (Js.Text(r.Columns, "tierKey") is not string t || t == "*" || t == app.Text("tier")));
        if (agreed == null)
        {
            Flag(waiting == null ? "autonomy-missing" : "autonomy-waiting", waiting == null ? "No autonomy level is agreed or proposed." : "An autonomy level waits for people to decide.", waiting == null ? null : "decision", waiting?.Id);
        }

        // Owners and owner gaps.
        IReadOnlyList<StoredRow> owners = s.CurrentOwners(app.Id);
        var gaps = OwnerGapsOf(s, new[] { app });
        foreach (var gap in gaps)
        {
            Flag("owner-gap", s.Label("owner-role", gap.Role) + ": no " + gap.Missing + ".");
        }

        // The security review: the latest of the review type the go-live gate requires at this tier, or security-review.
        string reviewType = s.ActiveRows("gate-requirement").Where(r => Js.Text(r.Columns, "requirementKind") == "review-done" && (goLiveGate == null || Js.Text(r.Columns, "gateKey") == goLiveGate))
            .Select(r => Js.Text(r.Columns, "requiredReviewTypeKey")).FirstOrDefault(k => k != null) ?? SecurityReviewType;
        bool reviewRequired = s.ActiveRows("gate-requirement").Any(r => Js.Text(r.Columns, "requirementKind") == "review-done" && Js.Text(r.Columns, "requiredReviewTypeKey") == reviewType
            && (goLiveGate == null || Js.Text(r.Columns, "gateKey") == goLiveGate)
            && (Js.Text(r.Columns, "tierKey") is not string t || t == "*" || t == app.Text("tier"))
            && (Js.Text(r.Columns, "applicationKindKey") is not string k || k == "*" || k == app.Text("applicationKind")));
        var reviews = s.Query("review", ("businessApplication", app.Id)).Where(r => r.Text("reviewType") == reviewType).ToList();
        StoredRow? lastDone = reviews.Where(r => r.Text("statusMeaning") == "done").OrderByDescending(r => r.Text("doneOn") ?? string.Empty, StringComparer.Ordinal).FirstOrDefault();
        StoredRow? open = reviews.Where(r => r.Text("statusMeaning") == "due").OrderBy(r => r.Text("dueOn") ?? string.Empty, StringComparer.Ordinal).FirstOrDefault();
        bool overdue = open != null && PastDue(s, open.Text("dueOn"));
        string reviewState = open != null ? (overdue ? "overdue" : "due") : lastDone != null ? "done" : "none";
        bool reviewMissing = reviewRequired && lastDone == null;
        if (reviewMissing && open == null)
        {
            Flag("security-review-missing", "The go-live gate needs a security review, and none is open or done.");
        }
        else if (overdue)
        {
            Flag("security-review-overdue", "The security review is overdue.", "review", open!.Id);
        }

        // The go-live gate (OP-07 read only).
        JsonTreeNode goLive = JsonTreeNode.Null;
        bool? ready = null;
        if (goLiveGate != null)
        {
            Product.Engine.Core.EvaluationResult<JsonTreeNode?> readiness = s.Call.GateReadiness(app.Id, goLiveGate);
            ready = Js.Flag(readiness.Payload, "ready") == true;
            goLive = Js.Obj(
                ("gate", s.LabelledKey("gate", goLiveGate)),
                ("ready", JsonTreeNode.FromBoolean(ready.Value)),
                ("unmet", Arr(Js.Items(readiness.Payload, "requirements").Where(r => Js.Flag(r, "met") != true && Js.Text(r, "requirementKey") != null).Select(r => Js.Obj(
                    ("requirementKey", JsonTreeNode.FromString(Js.Text(r, "requirementKey")!)),
                    ("label", JsonTreeNode.FromString(s.Label("gate-requirement", Js.Text(r, "requirementKey")))),
                    ("requirementKind", Js.Str(Js.Text(r, "requirementKind"))),
                    ("guidelineRef", GuidelineRef(s, Js.Text(r, "guidelineKey"))))))));
        }

        // Flagged-agent decisions: Microsoft or the AI lead flagged the agent; the decision says keep, reassign, consolidate or retire.
        StoredRow? flaggedDecision = NewestFirst(decisions.Where(d => d.Text("decisionType") == "flagged-agent")).FirstOrDefault();
        if (flaggedDecision?.Text("statusMeaning") == "proposed")
        {
            Flag("flagged-decision-open", "A flagged-agent decision waits for people to decide.", "decision", flaggedDecision.Id);
        }

        // Data uses: open uses of the agent, and how many reach an AI model.
        var uses = s.Query("data-use", ("businessApplication", app.Id)).Where(OpenOn(s)).ToList();
        JsonTreeNode components = Arr(s.Query("technical-component", ("businessApplication", app.Id)).Where(c => c.Text("retiredOn") == null)
            .OrderBy(c => c.Text("componentKind") ?? string.Empty, StringComparer.Ordinal).ThenBy(c => c.Id, StringComparer.Ordinal).Take(5)
            .Select(c => Js.Obj(
                ("componentKind", s.LabelledKey("component-kind", c.Text("componentKind"))),
                ("label", JsonTreeNode.FromString(c.Text("labelAsSeen") ?? c.Text("name") ?? (ids ? c.Text("externalId") : null) ?? "Unnamed component")),
                ("url", ids ? HttpsOrNull(c.Text("url")) : JsonTreeNode.Null))));

        JsonTreeNode json = Js.Obj(
            ("businessApplicationId", JsonTreeNode.FromString(app.Id)),
            ("recordNumber", Js.Str(app.Text("recordNumber"))),
            ("name", JsonTreeNode.FromString(AppName(app))),
            ("registration", s.StatusValue(app)),
            ("applicationKind", s.LabelledKey("application-kind", app.Text("applicationKind"))),
            ("tier", s.LabelledKey("tier", app.Text("tier"))),
            ("lifecycleStage", s.LabelledKey("lifecycle-stage", app.Text("lifecycleStage"))),
            ("autonomy", Js.Obj(
                ("agreed", ListValue(s, EngineKeys.AutonomyLevelList, agreed)),
                ("decisionId", Js.Str(app.Text("autonomyDecision"))),
                ("waiting", waiting == null ? JsonTreeNode.Null : Js.Obj(
                    ("decisionId", JsonTreeNode.FromString(waiting.Id)),
                    ("recordNumber", Js.Str(waiting.Text("recordNumber"))),
                    ("proposedLevel", ListValue(s, EngineKeys.AutonomyLevelList, proposedLevel)),
                    ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("decision", waiting.Id))))),
                ("requiredAtGoLive", JsonTreeNode.FromBoolean(requiredAtGoLive)))),
            ("owners", Arr(owners.Select(o => Js.Obj(
                ("ownerRole", s.LabelledKey("owner-role", o.Text("ownerRole"))),
                ("principal", s.PrincipalRef(o.Text("principal"))),
                ("isDeputy", JsonTreeNode.FromBoolean(o.Flag("isDeputy"))))))),
            ("ownerGaps", Arr(gaps.Select(g => Js.Obj(("ownerRole", s.LabelledKey("owner-role", g.Role)), ("missing", JsonTreeNode.FromString(g.Missing)))))),
            ("securityReview", Js.Obj(
                ("reviewType", s.LabelledKey("review-type", reviewType)),
                ("state", JsonTreeNode.FromString(reviewState)),
                ("requiredAtGoLive", JsonTreeNode.FromBoolean(reviewRequired)),
                ("lastDoneOn", ToolScope.DateOrNull(lastDone?.Text("doneOn"))),
                ("dueOn", ToolScope.DateOrNull(open?.Text("dueOn"))),
                ("reviewId", Js.Str(open?.Id ?? lastDone?.Id)),
                ("recordUrl", (open ?? lastDone) is StoredRow shown ? JsonTreeNode.FromString(s.RecordUrl("review", shown.Id)) : JsonTreeNode.Null))),
            ("goLive", goLive),
            ("flaggedDecision", DecisionRef(s, flaggedDecision)),
            ("flags", Arr(flags)),
            ("dataUses", Js.Obj(
                ("open", JsonTreeNode.FromInt64(uses.Count)),
                ("reachingAi", JsonTreeNode.FromInt64(uses.Count(u => u.Flag("reachesAi")))),
                ("drafts", JsonTreeNode.FromInt64(uses.Count(u => u.Text("statusMeaning") == "draft"))))),
            ("components", components),
            ("isAiCreated", AiCreated(app)),
            ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("business-application", app.Id))));
        return new AgentRow
        {
            Id = app.Id,
            Name = AppName(app),
            AutonomyAgreed = agreed != null,
            AutonomyWaiting = agreed == null && waiting != null,
            SecurityReviewMissing = reviewMissing,
            GoLiveReady = ready,
            FlagCount = flags.Count,
            Json = json,
        };
    }

    // ---- 94. request_security_review ----

    // Balanced's security review type. The go-live gate at tier 3 requires it done (gate requirement go-live-security-tier-3).
    private const string SecurityReviewType = "security-review";

    private static JsonTreeNode RequestSecurityReview(ToolScope s)
    {
        s.RequireAiState("submitted");
        StoredRow app = RequireApplication(s);
        RequireOwnerRelation(s, app.Id);
        if (s.Text("changeRequestId") is string change)
        {
            s.RequireRow("change-request", change, "changeRequestId");
        }

        string reviewType = s.RequireKey("review-type", SecurityReviewType, "reviewTypeKey").KeyPath;
        string due = s.Text("dueOn") ?? ReviewRequestDueOn(s);
        StoredRow? open = s.Query("review", ("businessApplication", app.Id), ("reviewType", reviewType), ("statusMeaning", "due")).FirstOrDefault();
        bool existing = open != null;
        ComposedWrite write = open != null
            ? new ComposedWrite(open.Id, false, false, true, null)
            : s.Create("review", Js.Obj(
                ("businessApplication", JsonTreeNode.FromString(app.Id)),
                ("reviewType", JsonTreeNode.FromString(reviewType)),
                ("status", JsonTreeNode.FromString(s.StatusFor("review", "due"))),
                ("trigger", JsonTreeNode.FromString("request")),
                ("dueOn", JsonTreeNode.FromString(due)),
                ("description", JsonTreeNode.FromString(s.RequireText("reason")))));
        var evidence = existing ? new List<string>() : s.Items("evidence").Select(e => Evidence(s, "review", "subjectReview", write.RowId, e)).ToList();
        StoredRow review = s.Rows.Get("review", write.RowId)!;
        string assigned = Js.Text(s.ConfigRow("review-type", reviewType)?.Columns, "defaultOwnerRoleKey") ?? "security-owner";
        string produces = Js.Text(s.ConfigRow("review-type", reviewType)?.Columns, "producesDecisionTypeKey") ?? "risk-acceptance";
        bool isAgent = AgentKinds(s).Contains(app.Text("applicationKind") ?? string.Empty);
        JsonTreeNode data = ToolScope.Merge(
            s.WriteResult(review, write.Created),
            ("reviewId", s.Call.IsDryRun && write.Created ? JsonTreeNode.Null : JsonTreeNode.FromString(review.Id)),
            ("reviewTypeKey", JsonTreeNode.FromString(reviewType)),
            ("trigger", JsonTreeNode.FromString(review.Text("trigger") ?? "request")),
            ("assignedOwnerRoleKey", JsonTreeNode.FromString(assigned)),
            ("producesDecisionTypeKey", JsonTreeNode.FromString(produces)),
            ("isAgent", JsonTreeNode.FromBoolean(isAgent)),
            ("dueOn", ToolScope.DateOrNull(review.Text("dueOn")) is JsonTreeNode d && d.Kind != JsonTreeKind.Null ? d : JsonTreeNode.FromString(due)),
            ("existingReviewReturned", JsonTreeNode.FromBoolean(existing)),
            ("evidenceLinkIds", Ids(s.Call.IsDryRun ? Array.Empty<string>() : evidence.ToArray())));
        return s.Answer(
            data,
            existing ? "A security review is already open for " + AppName(app) + "; it is returned and nothing new is written." : "The security review of " + AppName(app) + " is requested. It is a request, never a decision: the security reviewer records the outcome as a " + s.Label("decision-type", produces).ToLowerInvariant() + " decision.",
            s.Call.IsDryRun && write.Created ? null : s.RecordUrl("review", review.Id),
            ToolScope.Next("save_decision_proposal", ("decisionTypeKey", JsonTreeNode.FromString(produces)), ("subjectKind", JsonTreeNode.FromString("review")), ("subjectId", JsonTreeNode.FromString(review.Id))));
    }
}
