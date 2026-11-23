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

// Intake toolset (tools 10 to 16 and 46) of document 24, section 6.4.
internal static partial class ToolHandlers
{
    // ---- 10. check_duplicates ----

    private static JsonTreeNode CheckDuplicates(ToolScope s)
    {
        HashSet<string> words = Words(s.RequireText("text"));
        var externalIds = new HashSet<string>(s.Strings("componentExternalIds"), StringComparer.OrdinalIgnoreCase);
        string? kind = s.Text("proposedKindKey");
        var matches = new Dictionary<string, (string Reason, double Score, string Name)>(StringComparer.Ordinal);
        void Offer(StoredRow app, string reason, double score)
        {
            if (score >= 0.3 && (!matches.TryGetValue(app.Id, out var best) || score > best.Score))
            {
                matches[app.Id] = (reason, Math.Round(Math.Min(1.0, score), 2), AppName(app));
            }
        }

        foreach (StoredRow app in s.Rows.Query(RowQuery.For("business-application")).Where(a => a.Text("statusMeaning") != "archived"))
        {
            double boost = kind != null && app.Text("applicationKind") == kind ? 0.1 : 0.0;
            Offer(app, "name", Overlap(words, Words(AppName(app))) + boost);
            Offer(app, "purpose", Overlap(words, Words(app.Text("purpose"))) * 0.9 + boost);
            if (externalIds.Count > 0 && s.Query("technical-component", ("businessApplication", app.Id)).Any(c => c.Text("externalId") is string id && externalIds.Contains(id)))
            {
                Offer(app, "component", 1.0);
            }
        }

        JsonTreeNode items = Arr(matches.OrderByDescending(m => m.Value.Score).ThenBy(m => m.Value.Name, StringComparer.OrdinalIgnoreCase).Take(10).Select(m => Js.Obj(
            ("businessApplicationId", JsonTreeNode.FromString(m.Key)),
            ("name", JsonTreeNode.FromString(m.Value.Name)),
            ("reason", JsonTreeNode.FromString(m.Value.Reason)),
            ("score", JsonTreeNode.FromDecimal((decimal)m.Value.Score)))));
        return s.Answer(Js.Obj(("matches", items)), matches.Count == 0 ? "No registered application looks like this." : matches.Count + " registered applications look similar. Consider reuse before a new intake.");
    }

    // The share of the query's words that the candidate holds.
    private static double Overlap(HashSet<string> query, HashSet<string> candidate)
    {
        return query.Count == 0 || candidate.Count == 0 ? 0 : query.Count(candidate.Contains) / (double)Math.Max(query.Count, Math.Min(candidate.Count, 6));
    }

    // ---- 11. get_tier_questions ----

    private static JsonTreeNode GetTierQuestions(ToolScope s)
    {
        string? kind = s.Text("applicationKindKey");
        if (kind != null)
        {
            s.RequireKey("application-kind", kind, "applicationKindKey");
        }

        var questions = new List<JsonTreeNode>();
        foreach (SnapshotRow question in s.ActiveRows("tier-question").OrderBy(q => Js.Number(q.Columns, "sortOrder") ?? 0).ThenBy(q => q.KeyPath, StringComparer.Ordinal))
        {
            IReadOnlyList<string> kinds = Js.Strings(question.Columns, "appliesToKindKeys");
            if (kinds.Count > 0 && (kind == null || !kinds.Contains(kind, StringComparer.Ordinal)))
            {
                continue;
            }

            var answers = s.ActiveRows("tier-answer-option").Where(a => a.ParentKey == question.KeyPath).OrderBy(a => a.KeyPath, StringComparer.Ordinal).Select(a => Js.Obj(
                ("answerKey", JsonTreeNode.FromString(a.Key)),
                ("text", JsonTreeNode.FromString(Js.Text(a.Columns, "answerText") ?? Js.Text(a.Columns, "label") ?? a.Key)),
                ("impliedTier", s.LabelledKey("tier", Js.Text(a.Columns, "impliedTierKey") ?? "unknown"))));
            questions.Add(Js.Obj(
                ("questionKey", JsonTreeNode.FromString(question.KeyPath)),
                ("text", JsonTreeNode.FromString(Js.Text(question.Columns, "questionText") ?? Js.Text(question.Columns, "label") ?? question.KeyPath)),
                ("helpText", Js.Str(Js.Text(question.Columns, "helpText"))),
                ("answers", Arr(answers))));
        }

        return s.Answer(Js.Obj(("questions", Arr(questions))), questions.Count + " tier questions apply" + (kind == null ? "." : " to " + s.Label("application-kind", kind) + "."));
    }

    // ---- 12. save_intake_draft ----

    private static JsonTreeNode SaveIntakeDraft(ToolScope s)
    {
        s.RequireAiState("draft");
        string? kind = s.Text("proposedKindKey");
        if (kind != null)
        {
            s.RequireKey("application-kind", kind, "proposedKindKey");
        }

        string? app = s.Text("businessApplicationId");
        if (app != null)
        {
            RequireApplication(s);
        }

        RequireListValue(s, "horizon", s.Text("horizonKey"), "horizonKey");
        var columns = new List<(string, JsonTreeNode?)>
        {
            ("title", JsonTreeNode.FromString(s.RequireText("title"))),
            ("need", JsonTreeNode.FromString(s.RequireText("need"))),
            ("proposedKind", Js.OptStr(kind)),
            ("businessApplication", Js.OptStr(app)),
            ("expectedBenefit", Js.OptStr(s.Text("expectedBenefit"))),
            ("usesAi", Js.OptBool(s.Flag("usesAi"))),
            ("horizon", Js.OptStr(s.Text("horizonKey"))),
        };

        ComposedWrite write;
        string? intakeId = s.Text("intakeRequestId");
        if (intakeId == null)
        {
            columns.Add(("requester", JsonTreeNode.FromString(s.PersonId)));
            columns.Add(("status", JsonTreeNode.FromString(s.StatusFor("intake-request", "draft"))));
            write = s.Create("intake-request", Js.Obj(columns.ToArray()));
        }
        else
        {
            StoredRow intake = s.RequireRow("intake-request", intakeId, "intakeRequestId");
            if (!Same(intake.Text("requester"), s.PersonId))
            {
                throw s.Refused(McpErrorCodes.NotAuthorized, "Only the requester edits an intake request.");
            }

            if (intake.Text("statusMeaning") != "draft")
            {
                throw s.Refused(McpErrorCodes.RuleViolation, "The intake request is " + intake.Text("statusMeaning") + ", so it is no longer a draft. Withdraw it and start a new one.");
            }

            write = s.Update("intake-request", intakeId, Js.Obj(columns.ToArray()));
        }

        JsonTreeNode data = Js.Obj(s.WriteResult("intake-request", write).ToArray());
        string? id = Js.Text(data, "recordId");
        return s.Answer(
            data,
            "The intake request is saved as a draft. It goes nowhere until it is submitted.",
            id == null ? null : s.RecordUrl("intake-request", id),
            ToolScope.Next("answer_tier_questions", ("intakeRequestId", Js.Str(id))),
            ToolScope.Next("submit_record", ("subjectKind", JsonTreeNode.FromString("intake-request")), ("subjectId", Js.Str(id))));
    }

    // ---- 13. answer_tier_questions ----

    private static JsonTreeNode AnswerTierQuestions(ToolScope s)
    {
        s.RequireAiState("draft");
        string? intakeId = s.Text("intakeRequestId");
        string? appId = s.Text("businessApplicationId");
        string? kind;
        if (intakeId != null)
        {
            StoredRow intake = s.RequireRow("intake-request", intakeId, "intakeRequestId");
            if (!Same(intake.Text("requester"), s.PersonId) && !s.HoldsRole("platform-admin"))
            {
                throw s.Refused(McpErrorCodes.NotAuthorized, "Only the requester of the intake request answers its tier questions.");
            }

            kind = intake.Text("proposedKind") ?? (intake.Text("businessApplication") is string linked ? s.Rows.Get("business-application", linked)?.Text("applicationKind") : null);
        }
        else
        {
            StoredRow app = RequireApplication(s);
            RequireOwnerRelation(s, app.Id);
            kind = app.Text("applicationKind");
        }

        if (kind == null)
        {
            throw s.Invalid("intakeRequestId", "The intake request names no proposed kind, so the questions that apply are unknown. Save the draft with proposedKindKey first.");
        }

        JsonTreeNode answers = Arr(s.Items("answers").Select(a => Js.Obj(("questionKey", JsonTreeNode.FromString(Js.Text(a, "questionKey")!)), ("answerKey", JsonTreeNode.FromString(Js.Text(a, "answerKey")!)))));
        EvaluationResult<JsonTreeNode?> tier = s.Call.Evaluate(EngineOperation.EvaluateTier, Js.Obj(("applicationKindKey", JsonTreeNode.FromString(kind)), ("answers", answers)));
        if (tier.Payload == null || Js.Text(tier.Payload, "computedTierKey") == null)
        {
            Reason? reason = tier.Reasons.FirstOrDefault(r => r.Severity == ReasonSeverity.Error);
            throw s.Call.Fail(tier.Outcome == "unavailable" ? "unavailable" : "invalid-input", reason?.Code ?? "input.malformed", reason?.Message ?? "The answers do not give a tier.", reason?.Entity, "answers");
        }

        ComposedWrite write = s.Create("tier-assessment", Js.Obj(
            ("intakeRequest", Js.OptStr(intakeId)),
            ("businessApplication", Js.OptStr(appId)),
            ("answersJson", answers),
            ("assessedBy", JsonTreeNode.FromString(s.PersonId))));
        StoredRow written = s.Rows.Get("tier-assessment", write.RowId)!;
        string tierKey = written.Text("computedTier") ?? Js.Text(tier.Payload, "computedTierKey")!;
        var triggers = Js.Strings(tier.Payload, "triggeredReviewTypeKeys").Select(k => Js.Obj(("reviewTypeKey", JsonTreeNode.FromString(k))))
            .Concat(Js.Strings(tier.Payload, "triggeredGateRequirementKeys").Select(k => Js.Obj(("gateRequirementKey", JsonTreeNode.FromString(k)))));
        JsonTreeNode data = ToolScope.Merge(
            s.WriteResult(written, write.Created, hasStatus: false),
            ("tierAssessmentId", s.Call.IsDryRun ? JsonTreeNode.Null : JsonTreeNode.FromString(write.RowId)),
            ("computedTier", Js.Obj(("key", JsonTreeNode.FromString(tierKey)), ("label", JsonTreeNode.FromString(s.Label("tier", tierKey))), ("rank", JsonTreeNode.FromInt64(Js.Number(s.ConfigRow("tier", tierKey)?.Columns, "rank") ?? Js.Number(tier.Payload, "computedTierRank") ?? 1)))),
            ("drivingAnswers", Js.Strs(Js.Items(tier.Payload, "decisiveAnswers").Select(a => Js.Text(a, "questionKey")).Where(k => k != null).Select(k => k!).Distinct(StringComparer.Ordinal))),
            ("triggers", Arr(triggers)),
            ("note", JsonTreeNode.FromString("A suggestion. The tier is set only by an accepted tier decision.")));
        IReadOnlyList<string> missing = Js.Strings(tier.Payload, "missingQuestionKeys");
        if (missing.Count > 0)
        {
            s.Call.Warn("tier.incomplete", "Unanswered questions: " + string.Join(", ", missing) + ". The tier shown is what the given answers imply.", "Tier Question", missing[0]);
        }

        return s.Answer(data, "The answers suggest " + s.Label("tier", tierKey) + ". This is a suggestion; only an accepted tier decision sets the tier.");
    }

    // ---- 14. save_application_draft ----

    private static JsonTreeNode SaveApplicationDraft(ToolScope s)
    {
        IReadOnlyList<JsonTreeNode> proposedOwners = s.Items("proposedOwners");
        s.RequireAiState(proposedOwners.Count > 0 ? "proposed" : "draft");
        s.RequireKey("application-kind", s.Text("applicationKindKey"), "applicationKindKey");
        string? portfolio = s.Text("portfolioId");
        if (portfolio != null)
        {
            s.RequireRow("portfolio", portfolio, "portfolioId");
        }

        RequireListValue(s, "build-path", s.Text("buildPathKey"), "buildPathKey");
        string? intake = s.Text("intakeRequestId");
        if (intake != null)
        {
            s.RequireRow("intake-request", intake, "intakeRequestId");
        }

        foreach (JsonTreeNode owner in proposedOwners)
        {
            s.RequireKey("owner-role", Js.Text(owner, "ownerRoleKey"), "proposedOwners.ownerRoleKey");
        }

        string name = s.RequireText("name");
        var duplicates = s.Rows.Query(RowQuery.For("business-application")).Where(a => a.Text("statusMeaning") != "archived" && string.Equals(AppName(a), name, StringComparison.OrdinalIgnoreCase) && a.Id != s.Text("businessApplicationId")).ToList();
        if (duplicates.Count > 0)
        {
            s.Call.Warn("duplicate.name", "An application named " + name + " is already in the register. Check it before registering a second one.", "Business Application", duplicates[0].Id);
        }

        var columns = new List<(string, JsonTreeNode?)>
        {
            ("name", JsonTreeNode.FromString(name)),
            ("purpose", JsonTreeNode.FromString(s.RequireText("purpose"))),
            ("applicationKind", JsonTreeNode.FromString(s.Text("applicationKindKey")!)),
            ("portfolio", Js.OptStr(portfolio)),
            ("buildPath", Js.OptStr(s.Text("buildPathKey"))),
            ("usesAi", Js.OptBool(s.Flag("usesAi"))),
        };

        ComposedWrite write;
        string? appId = s.Text("businessApplicationId");
        if (appId == null)
        {
            columns.Add(("status", JsonTreeNode.FromString(s.StatusFor("business-application", "draft"))));
            write = s.Create("business-application", Js.Obj(columns.ToArray()));
        }
        else
        {
            StoredRow app = RequireApplication(s);
            if (app.Text("statusMeaning") != "draft" || !Same(app.Text("createdByPrincipal"), s.PersonId))
            {
                throw s.Refused(McpErrorCodes.RuleViolation, "Only a draft application you created can be edited here.");
            }

            write = s.Update("business-application", app.Id, Js.Obj(columns.ToArray()));
        }

        string applicationId = write.RowId;
        if (intake != null)
        {
            s.Update("intake-request", intake, Js.Obj(("businessApplication", JsonTreeNode.FromString(applicationId))));
        }

        var components = new List<JsonTreeNode>();
        foreach (JsonTreeNode component in s.Items("components"))
        {
            string kind = s.RequireKey("component-kind", Js.Text(component, "componentKindKey"), "components.componentKindKey").KeyPath;
            ComposedWrite link = s.Create("technical-component", Js.Obj(
                ("businessApplication", JsonTreeNode.FromString(applicationId)),
                ("componentKind", JsonTreeNode.FromString(kind)),
                ("linkKind", JsonTreeNode.FromString(DefaultComponentLinkKind(s, kind))),
                ("externalId", JsonTreeNode.FromString(Js.Text(component, "externalId")!)),
                ("url", JsonTreeNode.FromString(Js.Text(component, "url")!)),
                ("labelAsSeen", JsonTreeNode.FromString(Js.Text(component, "labelAsSeen") ?? Js.Text(component, "externalId")!))));
            components.Add(Js.Obj(
                ("technicalComponentId", s.Call.IsDryRun ? JsonTreeNode.Null : JsonTreeNode.FromString(link.RowId)),
                ("componentKindKey", JsonTreeNode.FromString(kind)),
                ("externalId", JsonTreeNode.FromString(Js.Text(component, "externalId")!)),
                ("url", JsonTreeNode.FromString(Js.Text(component, "url")!))));
        }

        // Proposed owners become owners only when their ownership decision is accepted. Owner Assignment rows are written
        // only by the update-subject effect of that decision (OP-08), so the proposal travels on the decision.
        string? decisionId = null;
        var approvals = new List<JsonTreeNode>();
        if (proposedOwners.Count > 0)
        {
            decisionId = s.Create("decision", Js.Obj(
                ("decisionType", JsonTreeNode.FromString("ownership")),
                ("subjectKind", JsonTreeNode.FromString("business-application")),
                ("subjectApplication", JsonTreeNode.FromString(applicationId)),
                ("statement", JsonTreeNode.FromString("Name the owners of " + name + ".")),
                ("context", JsonTreeNode.FromString("Proposed owners: " + Js.Write(Arr(proposedOwners)))),
                ("status", JsonTreeNode.FromString(s.StatusFor("decision", "proposed"))))).RowId;
            approvals.AddRange(ApprovalsNeeded(s, decisionId, "ownership"));
        }

        // WORKING DEFAULT (open design question 9): registering an application needs the person's confirmation card
        // (confirmation-channel). The approval runner sends it; it is not connected to this backend yet, so no card is
        // sent and the application stays a draft either way.
        bool cardSent = false;
        if (s.IsAiCaller)
        {
            s.Call.Warn("confirmation-card.not-sent", "The application stays a draft until the person confirms it on a confirmation card. The approval runner sends the card once it is connected.", "Business Application", applicationId);
        }

        JsonTreeNode data = ToolScope.Merge(
            s.WriteResult("business-application", write),
            ("components", Arr(components)),
            ("ownershipDecisionId", decisionId == null || s.Call.IsDryRun ? JsonTreeNode.Null : JsonTreeNode.FromString(decisionId)),
            ("approvalsNeeded", Arr(approvals)),
            ("confirmationCardSent", JsonTreeNode.FromBoolean(cardSent)));
        string? id = Js.Text(data, "recordId");
        var next = new List<NextStep>();
        if (decisionId != null && !s.Call.IsDryRun)
        {
            next.Add(ToolScope.Next("submit_record", ("subjectKind", JsonTreeNode.FromString("decision")), ("subjectId", JsonTreeNode.FromString(decisionId))));
        }

        return s.Answer(data, name + " is saved as a draft application" + (decisionId == null ? "." : ", with a proposed ownership decision."), id == null ? null : s.RecordUrl("business-application", id), next.ToArray());
    }

    // The link kind of a new component: the kind's own default, else the first active Microsoft resource link kind.
    private static string DefaultComponentLinkKind(ToolScope s, string componentKind)
    {
        string? own = Js.Text(s.ConfigRow("component-kind", componentKind)?.Columns, "defaultLinkKindKey");
        if (own != null)
        {
            return own;
        }

        string category = componentKind == "repository" ? "document" : "microsoft-resource";
        return s.ActiveRows("link-kind").Where(l => Js.Text(l.Columns, "linkCategory") == category).Select(l => l.KeyPath).OrderBy(k => k, StringComparer.Ordinal).FirstOrDefault()
            ?? throw s.Call.Fail("unavailable", "meaning.missing", "No active link kind of category " + category + " exists for a component.", "Link Kind", null);
    }

    // The approvals a decision needs (OP-02 under its stamped version), as approvalNeeded rows.
    private static IEnumerable<JsonTreeNode> ApprovalsNeeded(ToolScope s, string decisionId, string decisionTypeKey)
    {
        EvaluationResult<JsonTreeNode?> required = s.Call.RequiredApprovals(decisionId);
        if (required.Payload == null)
        {
            yield break;
        }

        foreach (JsonTreeNode requirement in Js.Items(required.Payload, "requirements"))
        {
            JsonTreeNode? needed = ApprovalNeeded(s, decisionTypeKey, requirement);
            if (needed != null)
            {
                yield return needed;
            }
        }
    }

    // The relation of a tool whose default roles include an owner relation (tools.json): a caller who holds one of the
    // Tool Policy's other roles passes; otherwise the caller must hold an owner role on the application. Judgment call:
    // application-owner in a row's roles stands for the owner relation, so it is not a pass by itself.
    private static void RequireOwnerRelation(ToolScope s, string applicationId)
    {
        bool ownerRelation = s.Tool.Relations.Any(r => r == "owner-of-application" || r == "technical-owner-of-application" || r == "owner-of-subject-application" || r == "intake-requester");
        if (!ownerRelation)
        {
            return;
        }

        s.Session.ToolPolicy.TryGet(s.Tool.Name, out Policy.ToolPolicyRow? row);
        IEnumerable<string> direct = (row?.AllowedRoleKeys ?? s.Tool.DefaultRoleKeys).Where(r => r != "application-owner");
        if (direct.Any(s.HoldsRole))
        {
            return;
        }

        string? ownerRole = s.Tool.Relations.Contains("technical-owner-of-application") ? "technical-owner" : null;
        if (!s.OwnsApplication(applicationId, ownerRole))
        {
            throw s.Refused(McpErrorCodes.NotAuthorized, "You hold no " + (ownerRole ?? "owner") + " role on this application, and " + s.Tool.Name + " needs one.");
        }
    }

    // ---- 15. link_component ----

    private static JsonTreeNode LinkComponent(ToolScope s)
    {
        s.RequireAiState("as-reported");
        StoredRow app = RequireApplication(s);
        RequireOwnerRelation(s, app.Id);
        string outcome = s.Text("outcome") ?? "linked";
        string? claimId = s.Text("claimId");
        StoredRow? claim = claimId == null ? null : s.RequireRow("claim", claimId, "claimId");
        ComposedWrite main;
        string? retiredOn = null;
        if (outcome == "retired")
        {
            StoredRow component = s.RequireRow("technical-component", s.Text("technicalComponentId"), "technicalComponentId");
            if (!Same(component.Text("businessApplication"), app.Id))
            {
                throw s.Invalid("technicalComponentId", "The component belongs to another application.");
            }

            retiredOn = component.Text("retiredOn") ?? s.Today;
            main = s.Update("technical-component", component.Id, Js.Obj(("retiredOn", JsonTreeNode.FromString(retiredOn))));
        }
        else
        {
            string kind = s.RequireKey("component-kind", s.Text("componentKindKey"), "componentKindKey").KeyPath;
            string externalId = s.Text("externalId") ?? throw s.Invalid("externalId", "externalId is required to link or move a component.");
            string url = s.Text("url") ?? throw s.Invalid("url", "url is required to link or move a component.");
            string? linkKind = s.Text("linkKindKey");
            if (linkKind != null)
            {
                s.RequireKey("link-kind", linkKind, "linkKindKey");
            }

            string? target = s.Text("deploymentTargetId");
            if (target != null)
            {
                s.RequireRow("deployment-target", target, "deploymentTargetId");
            }

            string? replaces = null;
            if (outcome == "moved")
            {
                StoredRow old = s.RequireRow("technical-component", s.Text("replacesComponentId"), "replacesComponentId");
                replaces = old.Id;
                if (old.Text("retiredOn") == null)
                {
                    s.Update("technical-component", old.Id, Js.Obj(("retiredOn", JsonTreeNode.FromString(s.Today))));
                }
            }

            main = s.Create("technical-component", Js.Obj(
                ("businessApplication", JsonTreeNode.FromString(app.Id)),
                ("componentKind", JsonTreeNode.FromString(kind)),
                ("linkKind", JsonTreeNode.FromString(linkKind ?? DefaultComponentLinkKind(s, kind))),
                ("externalId", JsonTreeNode.FromString(externalId)),
                ("url", JsonTreeNode.FromString(url)),
                ("labelAsSeen", JsonTreeNode.FromString(s.Text("labelAsSeen") ?? externalId)),
                ("deploymentTarget", Js.OptStr(target)),
                ("replacesComponent", Js.OptStr(replaces)),
                ("sourceClaim", Js.OptStr(claimId))));
        }

        JsonTreeNode? claimStatus = null;
        if (claim != null)
        {
            s.Update("claim", claim.Id, Js.Obj(
                ("status", JsonTreeNode.FromString(s.StatusFor("claim", "claimed"))),
                ("claimedBy", JsonTreeNode.FromString(s.PersonId)),
                ("claimedOn", Js.Utc(s.Now)),
                ("resultingComponent", JsonTreeNode.FromString(main.RowId))));
            claimStatus = s.StatusValue(s.Rows.Get("claim", claim.Id)!);
        }

        JsonTreeNode data = ToolScope.Merge(
            s.WriteResult("technical-component", main, hasStatus: false),
            ("technicalComponentId", s.Call.IsDryRun && main.Created ? JsonTreeNode.Null : JsonTreeNode.FromString(main.RowId)),
            ("outcome", JsonTreeNode.FromString(outcome)),
            ("retiredOn", retiredOn == null ? null : JsonTreeNode.FromString(retiredOn)),
            ("claimStatus", claimStatus));
        return s.Answer(data, outcome == "retired" ? "The component is recorded as retired, as reported." : "The component is linked to " + AppName(app) + ".", s.RecordUrl("business-application", app.Id));
    }

    // ---- 16. propose_owner_change ----

    private static JsonTreeNode ProposeOwnerChange(ToolScope s)
    {
        s.RequireAiState("proposed");
        StoredRow app = RequireApplication(s);
        RequireOwnerRelation(s, app.Id);
        IReadOnlyList<StoredRow> owners = s.CurrentOwners(app.Id);
        foreach (JsonTreeNode change in s.Items("changes"))
        {
            s.RequireKey("owner-role", Js.Text(change, "ownerRoleKey"), "changes.ownerRoleKey");
            string? end = Js.Text(change, "endAssignmentId");
            if (end != null && !owners.Any(o => o.Id == end))
            {
                throw s.Invalid("changes.endAssignmentId", "endAssignmentId " + end + " is not a current owner assignment of this application.");
            }
        }

        ComposedWrite decision = s.Create("decision", Js.Obj(
            ("decisionType", JsonTreeNode.FromString("ownership")),
            ("subjectKind", JsonTreeNode.FromString("business-application")),
            ("subjectApplication", JsonTreeNode.FromString(app.Id)),
            ("statement", JsonTreeNode.FromString("Change the owners of " + AppName(app) + ".")),
            ("context", JsonTreeNode.FromString("Proposed changes: " + Js.Write(Arr(s.Items("changes"))))),
            ("reason", JsonTreeNode.FromString(s.RequireText("reason"))),
            ("status", JsonTreeNode.FromString(s.StatusFor("decision", "proposed")))));

        // pendingAssignments stays empty: Owner Assignment rows are written only by the update-subject effect of the
        // accepted decision (OP-08), so nothing is pending in O06 before acceptance.
        JsonTreeNode data = ToolScope.Merge(
            s.WriteResult("decision", decision),
            ("decisionId", s.Call.IsDryRun ? JsonTreeNode.Null : JsonTreeNode.FromString(decision.RowId)),
            ("pendingAssignments", Js.EmptyArray),
            ("approvalsNeeded", Arr(ApprovalsNeeded(s, decision.RowId, "ownership"))));
        return s.Answer(
            data,
            "The owner change is proposed. The holders confirm on approval cards once it is submitted.",
            s.Call.IsDryRun ? null : s.RecordUrl("decision", decision.RowId),
            s.Call.IsDryRun ? Array.Empty<NextStep>() : new[] { ToolScope.Next("submit_record", ("subjectKind", JsonTreeNode.FromString("decision")), ("subjectId", JsonTreeNode.FromString(decision.RowId))) });
    }

    // ---- 46. record_owner_leave ----

    private static JsonTreeNode RecordOwnerLeave(ToolScope s)
    {
        s.RequireAiState("as-reported");
        string person = s.RequireText("principalObjectId");
        string leavesOn = s.RequireText("leavesOn");
        StoredRow principal = s.Principal(person) ?? throw s.NotFound("Principal Reference", person, "principalObjectId");
        var assignments = s.Rows.Query(RowQuery.For("owner-assignment")).Where(o => Same(o.Text("principal"), person) && o.Text("endOn") == null).ToList();
        bool self = Same(person, s.PersonId);
        bool ownerOfTheirApp = assignments.Any(a => a.Text("businessApplication") is string app && s.OwnsApplication(app));
        if (!self && !ownerOfTheirApp && !s.HoldsRole("platform-admin"))
        {
            throw s.Refused(McpErrorCodes.NotAuthorized, "Only the leaving person, an owner of an application the person owns, or a platform admin records a leave.");
        }

        ComposedWrite write = s.Call.RecordPrincipalFacts(principal.Id, leavesOn, null);
        string reviewType = s.ConfigRow("review-type", "owner-check") is SnapshotRow r && r.IsActive ? "owner-check"
            : s.ActiveRows("review-type").Where(t => Js.Text(t.Columns, "producesDecisionTypeKey") == "ownership").Select(t => t.KeyPath).FirstOrDefault()
            ?? throw s.Call.Fail("unavailable", "meaning.missing", "No active review type produces ownership decisions, so no owner check review can open.", "Review Type", null);
        var reviewIds = new List<string>();
        var affected = new List<JsonTreeNode>();
        foreach (IGrouping<string, StoredRow> byApp in assignments.Where(a => a.Text("businessApplication") != null).GroupBy(a => a.Text("businessApplication")!, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            IReadOnlyList<StoredRow> owners = s.CurrentOwners(byApp.Key);
            foreach (StoredRow assignment in byApp.Where(a => !a.Flag("isDeputy")))
            {
                string role = assignment.Text("ownerRole") ?? string.Empty;
                affected.Add(Js.Obj(
                    ("businessApplicationId", JsonTreeNode.FromString(byApp.Key)),
                    ("ownerRoleKey", JsonTreeNode.FromString(role)),
                    ("hasDeputy", JsonTreeNode.FromBoolean(owners.Any(o => o.Text("ownerRole") == role && o.Flag("isDeputy"))))));
            }

            StoredRow? open = s.Query("review", ("businessApplication", byApp.Key), ("reviewType", reviewType), ("statusMeaning", "due")).FirstOrDefault();
            string reviewId = open?.Id ?? s.Create("review", Js.Obj(
                ("businessApplication", JsonTreeNode.FromString(byApp.Key)),
                ("reviewType", JsonTreeNode.FromString(reviewType)),
                ("status", JsonTreeNode.FromString(s.StatusFor("review", "due"))),
                ("trigger", JsonTreeNode.FromString("owner-check")),
                ("dueOn", JsonTreeNode.FromString(leavesOn)))).RowId;
            if (!s.Call.IsDryRun)
            {
                reviewIds.Add(reviewId);
            }
        }

        int pending = s.Query("approval-record", ("askedPrincipal", person), ("outcomeMeaning", "pending")).Count;
        JsonTreeNode data = ToolScope.Merge(
            s.WriteResult("principal-reference", write, hasStatus: false),
            ("affected", Arr(affected)),
            ("ownerCheckReviewIds", Ids(reviewIds)),
            ("pendingApprovals", JsonTreeNode.FromInt64(pending)));
        return s.Answer(
            data,
            "The leave on " + leavesOn + " is recorded, as reported. " + affected.Count + " owner roles are affected; a successor needs an accepted ownership decision.",
            null,
            affected.Count == 0 ? Array.Empty<NextStep>() : new[] { ToolScope.Next("propose_owner_change", ("businessApplicationId", JsonTreeNode.FromString(Js.Text(affected[0], "businessApplicationId")!))) });
    }
}
