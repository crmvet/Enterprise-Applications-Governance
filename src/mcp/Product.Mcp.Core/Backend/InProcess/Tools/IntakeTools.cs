using System;
using System.Globalization;
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

        HashSet<string> keyWords = KeyWords(words);
        foreach (StoredRow app in s.Rows.Query(RowQuery.For("business-application")).Where(a => a.Text("statusMeaning") != "archived"))
        {
            double boost = kind != null && app.Text("applicationKind") == kind ? 0.1 : 0.0;
            Offer(app, "name", NameShare(keyWords, KeyWords(Words(AppName(app)))) + boost);
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

    // The share of the candidate name's words that the query holds (product review 2026-11-24): a long request that names
    // "crew" once still finds Crew Roster, where the share of the query's words would be tiny. Key words only, so a word
    // every request holds ("app", "for") never makes a match.
    private static double NameShare(HashSet<string> query, HashSet<string> name)
    {
        return query.Count == 0 || name.Count == 0 ? 0 : name.Count(query.Contains) / (double)name.Count;
    }

    // Words without the common ones, each in a simple singular form ("inspections" and "inspection" are one word).
    private static HashSet<string> KeyWords(HashSet<string> words)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (string word in words)
        {
            if (Array.IndexOf(CommonWords, word) >= 0)
            {
                continue;
            }

            keys.Add(word.Length > 3 && word.EndsWith("s", StringComparison.Ordinal) && !word.EndsWith("ss", StringComparison.Ordinal) ? word.Substring(0, word.Length - 1) : word);
        }

        return keys;
    }

    private static readonly string[] CommonWords =
    {
        "a", "an", "and", "app", "apps", "application", "applications", "are", "as", "at", "be", "by", "can", "do", "for", "from", "have", "i", "in", "is",
        "it", "me", "my", "need", "new", "of", "on", "or", "our", "so", "system", "that", "the", "their", "them", "they", "this", "to", "tool", "us",
        "want", "we", "with", "would", "you", "your",
    };

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

        // The data taxonomy's hint before the person answers (docs/architecture/data-taxonomy.md): for an application, or
        // an intake linked to one, the highest answer its declared data implies prefills the question as a suggestion. A
        // person answers; the hint never sets the answer.
        string? appId = s.Text("businessApplicationId");
        if (appId != null)
        {
            s.RequireRow("business-application", appId, "businessApplicationId");
        }
        else if (s.Text("intakeRequestId") is string intakeId)
        {
            StoredRow intake = s.RequireRow("intake-request", intakeId, "intakeRequestId");
            if (!CanReadIntake(s, intake))
            {
                throw s.NotFound("Intake Request", intakeId, "intakeRequestId");
            }

            appId = intake.Text("businessApplication");
        }

        JsonTreeNode implied = HighestImpliedAnswer(s, appId, out string? impliedQuestion, out _, out _);
        string hint = impliedQuestion == null ? string.Empty : " The data it declares suggests " + Js.Text(implied, "label") + " for " + impliedQuestion + "; a person answers.";
        return s.Answer(Js.Obj(("questions", Arr(questions)), ("impliedAnswer", implied)), questions.Count + " tier questions apply" + (kind == null ? "." : " to " + s.Label("application-kind", kind) + ".") + hint);
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
            // A request is linked only to an application the requester already owns, or to a draft they created for it
            // (security review 2026-11-24, document 24, B24-14): otherwise its acceptance could register someone else's
            // application and make the requester's choice its business owner with no person deciding.
            StoredRow linked = RequireApplication(s);
            bool ownDraft = linked.Text("statusMeaning") == "draft" && Same(linked.Text("createdByPrincipal"), s.PersonId);
            if (!ownDraft && !s.OwnsApplication(linked.Id))
            {
                throw s.Refused(McpErrorCodes.NotAuthorized, "A request is linked only to an application you hold an owner role on, or to a draft application you created. Leave businessApplicationId out to request a new application.");
            }
        }

        string? portfolio = s.Text("portfolioId");
        if (portfolio != null)
        {
            s.RequireRow("portfolio", portfolio, "portfolioId");
        }

        RequireListValue(s, "horizon", s.Text("horizonKey"), "horizonKey");
        string? businessOwner = BusinessOwnerOf(s);
        if (businessOwner != null && Same(businessOwner, s.PersonId))
        {
            // The requester never names themselves (security review 2026-11-24, B24-14): the business owner the request
            // names answers its priority by name and becomes the owner of what it starts, so it is someone else.
            throw s.Invalid("businessOwnerObjectId", "Name someone else as the business owner. The person who requests an application does not also decide its priority as its business owner.");
        }
        var columns = new List<(string, JsonTreeNode?)>
        {
            ("title", JsonTreeNode.FromString(s.RequireText("title"))),
            ("need", JsonTreeNode.FromString(s.RequireText("need"))),
            ("proposedKind", Js.OptStr(kind)),
            ("businessApplication", Js.OptStr(app)),
            ("expectedBenefit", Js.OptStr(s.Text("expectedBenefit"))),
            ("usesAi", Js.OptBool(s.Flag("usesAi"))),
            ("horizon", Js.OptStr(s.Text("horizonKey"))),
            ("businessOwner", Js.OptStr(businessOwner)),
            ("portfolio", Js.OptStr(portfolio)),
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

    // The business owner the requester names (O07 Business Owner, a proposal; document 25, B25-10): a person the store
    // knows, by the object ID of their Principal Reference, which find_people finds by name. The label shown is always
    // the reference's, never one a caller typed, so a request never misnames a person. A person the owner check found
    // disabled or deleted, or one already leaving, is refused: they could not own what the request starts.
    private static string? BusinessOwnerOf(ToolScope s)
    {
        string? objectId = s.Text("businessOwnerObjectId");
        return objectId == null ? null : KnownBusinessOwner(s, objectId, "businessOwnerObjectId");
    }

    // The same checks for a business owner named in another field (claim_resource's new application, B23-10).
    private static string KnownBusinessOwner(ToolScope s, string objectId, string field)
    {
        StoredRow person = s.Principal(objectId) ?? throw s.Invalid(field, "No person with object ID " + objectId + " is known to the product. Find the person with find_people.");
        if (person.Text("principalKind") != "user" || person.Flag("isErased"))
        {
            throw s.Invalid(field, "The business owner is a person signed in as a user; " + objectId + " is not.");
        }

        string? state = person.Text("directoryState");
        if (state == "disabled" || state == "deleted" || (person.Text("leavesOn") is string leaves && string.CompareOrdinal(leaves, s.Today) <= 0))
        {
            throw s.Invalid(field, (person.Text("displayLabel") ?? objectId) + " has left or is disabled in the directory, so they cannot be the business owner. Name someone else.");
        }

        return person.Text("entraObjectId") ?? objectId;
    }

    // ---- 60. score_intake_request ----

    // The PMO's demand fields on a request on the table (document 23, P01 step 8): Score, Size Band, Horizon and Expected
    // Benefit, written as the caller reports them. The request keeps its status; priority is a decision, proposed with
    // save_decision_proposal (type priority) and answered by its accountable holder on the decision page (document 24,
    // B24-9). A draft is the requester's to edit (save_intake_draft), and a closed request is not scored again.
    private static JsonTreeNode ScoreIntakeRequest(ToolScope s)
    {
        s.RequireAiState("as-reported");
        StoredRow intake = s.RequireRow("intake-request", s.Text("intakeRequestId"), "intakeRequestId");
        if (!CanReadIntake(s, intake))
        {
            throw s.NotFound("Intake Request", intake.Id, "intakeRequestId");
        }

        string meaning = intake.Text("statusMeaning") ?? "draft";
        if (!OpenDemandMeanings.Contains(meaning, StringComparer.Ordinal))
        {
            throw s.Refused(McpErrorCodes.RuleViolation, meaning == "draft"
                ? "The intake request is still a draft. The PMO scores a request once it is submitted."
                : "The intake request is " + meaning + ", so it is no longer on the table to score.");
        }

        RequireListValue(s, "size-band", s.Text("sizeBandKey"), "sizeBandKey");
        RequireListValue(s, "horizon", s.Text("horizonKey"), "horizonKey");
        JsonTreeNode? score = Js.Member(s.Args, "score");
        bool clearScore = Js.Flag(s.Args, "clearScore") == true;
        if (score == null && !clearScore && s.Text("sizeBandKey") == null && s.Text("horizonKey") == null && s.Text("expectedBenefit") == null)
        {
            throw s.Invalid("score", "Give a score, a size band, a horizon or the expected benefit to record.");
        }

        var columns = new List<(string, JsonTreeNode?)>
        {
            ("score", clearScore ? JsonTreeNode.Null : score),
            ("sizeBand", Js.OptStr(s.Text("sizeBandKey"))),
            ("horizon", Js.OptStr(s.Text("horizonKey"))),
            ("expectedBenefit", Js.OptStr(s.Text("expectedBenefit"))),
        };
        ComposedWrite write = s.Update("intake-request", intake.Id, Js.Obj(columns.ToArray()));
        StoredRow written = s.Rows.Get("intake-request", intake.Id)!;
        decimal? recorded = Score(written);
        JsonTreeNode data = ToolScope.Merge(
            s.WriteResult("intake-request", write),
            ("score", recorded.HasValue ? JsonTreeNode.FromDecimal(recorded.Value) : JsonTreeNode.Null),
            ("sizeBand", ListValue(s, "size-band", written.Text("sizeBand"))),
            ("horizon", ListValue(s, "horizon", written.Text("horizon"))),
            ("priority", PriorityOf(s, written)));
        return s.Answer(
            data,
            "The demand fields of " + (intake.Text("title") ?? intake.Id) + " are recorded, as reported. Priority is a decision: propose it, and its accountable holder answers on the decision page.",
            s.RecordUrl("intake-request", intake.Id),
            ToolScope.Next("save_decision_proposal", ("decisionTypeKey", JsonTreeNode.FromString("priority")), ("subjectKind", JsonTreeNode.FromString("intake-request")), ("subjectId", JsonTreeNode.FromString(intake.Id))));
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

            // The answers of a submitted request set the tier its decisions are asked at, so they are fixed once it is
            // submitted (security review 2026-11-24, B24-14). Withdraw it and start a new one to answer again.
            if ((intake.Text("statusMeaning") ?? "draft") != "draft")
            {
                throw s.Refused(McpErrorCodes.RuleViolation, "The intake request is " + intake.Text("statusMeaning") + ", so its tier answers are fixed. Withdraw it and start a new one to answer again.");
            }

            kind = intake.Text("proposedKind") ?? (intake.Text("businessApplication") is string linked ? s.Rows.Get("business-application", linked)?.Text("applicationKind") : null);
        }
        else
        {
            StoredRow app = RequireApplication(s);
            RequireOwnerRelation(s, app.Id);
            kind = app.Text("applicationKind");

            // A tier decision already out for approval keeps the answers its approvers read (security review
            // 2026-11-24, B24-14): new answers wait until it is decided or withdrawn.
            if (s.Query("decision", ("subjectApplication", app.Id), ("decisionType", EngineKeys.TierDecisionType), ("statusMeaning", "proposed")).Any(d => s.Query("approval-record", ("decision", d.Id)).Count > 0))
            {
                throw s.Refused(McpErrorCodes.RuleViolation, "A tier decision about " + AppName(app) + " is waiting for its approvers, so the tier answers stay as they are until it is decided or withdrawn.");
            }
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

        // The data taxonomy (docs/architecture/data-taxonomy.md, Data checks; DC-09): the highest answer the application's declared data implies, as a hint. A
        // person answers; the hint never sets the answer.
        string? hintApp = appId ?? (intakeId == null ? null : s.Rows.Get("intake-request", intakeId)?.Text("businessApplication"));
        JsonTreeNode implied = HighestImpliedAnswer(s, hintApp, out string? impliedQuestion, out string? impliedAnswer, out string? impliedDomain);
        if (impliedQuestion != null && impliedAnswer != null)
        {
            string? given = s.Items("answers").Where(a => Js.Text(a, "questionKey") == impliedQuestion).Select(a => Js.Text(a, "answerKey")).LastOrDefault();
            var tree = new DataTree(s.Snapshot);
            if (given != null && tree.AnswerRank(impliedQuestion, given) is int givenRank && tree.AnswerRank(impliedQuestion, impliedAnswer) is int impliedRank && givenRank < impliedRank)
            {
                // Labels, never option keys (wave 4b QA walk): "the answer Internal to Data sensitivity is lower than Restricted".
                s.Call.Warn(Product.Engine.Core.Reasons.ReasonCodes.DataSensitivityBelowData, "DC-09: the answer " + s.Label("tier-answer-option", impliedQuestion + "/" + given) + " to " + s.Label("tier-question", impliedQuestion) + " is lower than " + s.Label("tier-answer-option", impliedQuestion + "/" + impliedAnswer) + ", which " + tree.PathText(impliedDomain!) + " implies. A person checks the answer.", "Tier Question", impliedQuestion);
            }
        }

        JsonTreeNode data = ToolScope.Merge(
            s.WriteResult(written, write.Created, hasStatus: false),
            ("tierAssessmentId", s.Call.IsDryRun ? JsonTreeNode.Null : JsonTreeNode.FromString(write.RowId)),
            ("computedTier", Js.Obj(("key", JsonTreeNode.FromString(tierKey)), ("label", JsonTreeNode.FromString(s.Label("tier", tierKey))), ("rank", JsonTreeNode.FromInt64(Js.Number(s.ConfigRow("tier", tierKey)?.Columns, "rank") ?? Js.Number(tier.Payload, "computedTierRank") ?? 1)))),
            ("drivingAnswers", Js.Strs(Js.Items(tier.Payload, "decisiveAnswers").Select(a => Js.Text(a, "questionKey")).Where(k => k != null).Select(k => k!).Distinct(StringComparer.Ordinal))),
            ("triggers", Arr(triggers)),
            ("impliedAnswer", implied),
            ("note", JsonTreeNode.FromString("A suggestion. The tier is set only by an accepted tier decision.")));
        IReadOnlyList<string> missing = Js.Strings(tier.Payload, "missingQuestionKeys");
        if (missing.Count > 0)
        {
            s.Call.Warn("tier.incomplete", "Unanswered questions: " + string.Join(", ", missing) + ". The tier shown is what the given answers imply.", "Tier Question", missing[0]);
        }

        return s.Answer(data, "The answers suggest " + s.Label("tier", tierKey) + ". This is a suggestion; only an accepted tier decision sets the tier.");
    }

    // The highest tier answer the application's open data uses imply (C47 Implied Answer, ranked by the tier each answer
    // implies), or JSON null when it has none.
    private static JsonTreeNode HighestImpliedAnswer(ToolScope s, string? applicationId, out string? question, out string? answer, out string? domain)
    {
        question = null;
        answer = null;
        domain = null;
        if (applicationId == null)
        {
            return JsonTreeNode.Null;
        }

        var tree = new DataTree(s.Snapshot);
        int best = int.MinValue;
        foreach (StoredRow use in s.Query("data-use", ("businessApplication", applicationId)).Where(OpenOn(s)).OrderBy(u => u.Id, StringComparer.Ordinal))
        {
            string? key = use.Text("dataDomain");
            if (key == null || tree.Domain(key) == null)
            {
                continue;
            }

            (string? q, string? a) = tree.ImpliedAnswer(key);
            int rank = tree.AnswerRank(q, a) ?? int.MinValue;
            if (q != null && a != null && rank > best)
            {
                best = rank;
                question = q;
                answer = a;
                domain = key;
            }
        }

        return ImpliedAnswerValue(s, tree, question, answer, domain);
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
            StoredRow request = s.RequireRow("intake-request", intake, "intakeRequestId");
            if (!Same(request.Text("requester"), s.PersonId) && !s.HoldsRole("platform-admin"))
            {
                throw s.Refused(McpErrorCodes.NotAuthorized, "Only the requester of the intake request links a draft application to it.");
            }
        }

        foreach (JsonTreeNode owner in proposedOwners)
        {
            RequireApplicationOwnerRole(s, s.RequireKey("owner-role", Js.Text(owner, "ownerRoleKey"), "proposedOwners.ownerRoleKey"), "proposedOwners.ownerRoleKey");
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
            ("holdsNoBusinessData", Js.OptBool(s.Flag("holdsNoBusinessData"))),
        };

        ComposedWrite write;
        bool flagOnly = false;
        string? appId = s.Text("businessApplicationId");
        if (appId == null)
        {
            columns.Add(("status", JsonTreeNode.FromString(s.StatusFor("business-application", "draft"))));
            write = s.Create("business-application", Js.Obj(columns.ToArray()));
        }
        else
        {
            StoredRow app = RequireApplication(s);
            flagOnly = app.Text("statusMeaning") != "draft" && s.Flag("holdsNoBusinessData") != null;
            if (flagOnly)
            {
                // A registered application: only Holds No Business Data changes here (data design review: DC-10's fix had
                // no tool). A person who owns the application, or the platform admin, sets it; an AI client never does,
                // because it changes what the data-uses-declared gate requirement reads (D-36).
                if (s.IsAiCaller)
                {
                    throw s.Refused(McpErrorCodes.AiCannotDoThis, "An AI client does not mark a registered application as holding no business data. A person who owns it does, on its page.", "holdsNoBusinessData");
                }

                if (!s.OwnsApplication(app.Id) && !s.HoldsRole("platform-admin"))
                {
                    throw s.Refused(McpErrorCodes.NotAuthorized, "Only an owner of the application or the platform admin marks it as holding no business data.");
                }

                if (!string.Equals(AppName(app), name, StringComparison.Ordinal))
                {
                    throw s.Invalid("name", "On a registered application only Holds No Business Data changes here. Give its name as it is.");
                }

                // Nothing else rides on the flag (wave 4b review): components, proposed owners and the intake link of a
                // registered application have their own tools.
                string? extra = s.Items("components").Count > 0 ? "components" : proposedOwners.Count > 0 ? "proposedOwners" : intake != null ? "intakeRequestId" : null;
                if (extra != null)
                {
                    throw s.Invalid(extra, "On a registered application only Holds No Business Data changes here. Leave out " + extra + ": a component is linked with link_component, an owner is proposed with propose_owner_change.");
                }

                write = s.Update("business-application", app.Id, Js.Obj(("holdsNoBusinessData", JsonTreeNode.FromBoolean(s.Flag("holdsNoBusinessData") == true))));
            }
            else
            {
                if (app.Text("statusMeaning") != "draft" || !Same(app.Text("createdByPrincipal"), s.PersonId))
                {
                    throw s.Refused(McpErrorCodes.RuleViolation, "Only a draft application you created can be edited here.");
                }

                write = s.Update("business-application", app.Id, Js.Obj(columns.ToArray()));
            }
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
        // only by the update-subject effect of that decision (OP-08), so the proposal travels on the decision, in Proposed
        // Changes Json (document 25, B25-8).
        string? decisionId = null;
        var approvals = new List<JsonTreeNode>();
        if (proposedOwners.Count > 0)
        {
            JsonTreeNode changes = Arr(proposedOwners.Select(o => Js.Obj(
                ("ownerRoleKey", JsonTreeNode.FromString(Js.Text(o, "ownerRoleKey")!)),
                ("addObjectId", JsonTreeNode.FromString(Js.Text(o, "objectId")!)),
                ("isDeputy", Js.Flag(o, "isDeputy") == true ? JsonTreeNode.True : null))));
            decisionId = s.Create("decision", Js.Obj(
                ("decisionType", JsonTreeNode.FromString(EngineKeys.OwnershipDecisionType)),
                ("subjectKind", JsonTreeNode.FromString("business-application")),
                ("subjectApplication", JsonTreeNode.FromString(applicationId)),
                ("statement", JsonTreeNode.FromString(OwnerChangeStatement(s, name, changes.Items, Array.Empty<StoredRow>()))),
                ("context", JsonTreeNode.FromString(OwnerChangeSentences(s, changes.Items, Array.Empty<StoredRow>()))),
                ("outcome", Js.OptStr(OwnershipOutcome(s, "assign"))),
                ("proposedChangesJson", Js.Obj(("owners", changes))),
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

        string answer = flagOnly
            ? name + (s.Flag("holdsNoBusinessData") == true ? " is marked as holding no business data" : " is marked as holding business data") + ". It stays registered."
            : name + " is saved as a draft application" + (decisionId == null ? "." : ", with a proposed ownership decision.");
        return s.Answer(data, answer, id == null ? null : s.RecordUrl("business-application", id), next.ToArray());
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

        foreach (JsonTreeNode item in Js.Items(required.Payload, "requirements"))
        {
            // The owner role the submit will ask, after its fallback (product review, fix round 1): "Check first" on Propose
            // priority names the business owner the request names, as the submit asks, when no sponsor is named.
            JsonTreeNode requirement = item;
            if (Js.Text(item, "approverOwnerRoleKey") is string ownerRole && Js.Text(item, "fallbackOwnerRoleKey") != null
                && s.Call.AskOf(decisionId, item).OwnerRoleKey is string asked && asked != ownerRole)
            {
                requirement = Js.With(item, ("approverOwnerRoleKey", JsonTreeNode.FromString(asked)));
            }

            JsonTreeNode? needed = ApprovalNeeded(s, decisionTypeKey, requirement);
            if (needed != null)
            {
                yield return needed;
            }
        }

        // The roles it consults, too (fix round 1: "Check first" on Propose autonomy level named the security reviewer
        // only, and the submit then also consulted the AI lead).
        foreach (JsonTreeNode consultation in Js.Items(required.Payload, "consultations"))
        {
            if (ApprovalNeeded(s, decisionTypeKey, consultation) is JsonTreeNode needed)
            {
                yield return Js.With(needed, ("isConsultation", JsonTreeNode.True));
            }
        }
    }

    // An owner role names where it can be held (C28 Subject Kinds, data taxonomy, D-37): one held only on data domains or
    // data locations, such as the data steward, is not an owner role of an application. No list means an application
    // only, as every owner role was before the data taxonomy. The engine refuses it too from data taxonomy slice 2 (G-19).
    internal static bool HeldOnApplications(SnapshotRow role)
    {
        IReadOnlyList<string> kinds = Js.Strings(role.Columns, "subjectKinds");
        return kinds.Count == 0 || kinds.Contains("business-application", StringComparer.Ordinal);
    }

    private static void RequireApplicationOwnerRole(ToolScope s, SnapshotRow role, string field)
    {
        if (!HeldOnApplications(role))
        {
            throw s.Invalid(field, field + " \"" + role.KeyPath + "\" is an owner role of " + string.Join(" and ", Js.Strings(role.Columns, "subjectKinds"))
                + ", not of an application. Choose an owner role an application can have, such as business-owner or technical-owner.");
        }
    }

    // The relation of a tool whose default roles include an owner relation (tools.json): a caller who holds one of the
    // Tool Policy's other roles passes; otherwise the caller must hold an owner role on the application. Judgment call:
    // application-owner in a row's roles stands for the owner relation, so it is not a pass by itself.
    private static void RequireOwnerRelation(ToolScope s, string applicationId, params string[] relationRoles)
    {
        bool ownerRelation = s.Tool.Relations.Any(r => r == "owner-of-application" || r == "technical-owner-of-application" || r == "owner-of-subject-application" || r == "intake-requester");
        if (!ownerRelation)
        {
            return;
        }

        s.Session.ToolPolicy.TryGet(s.Tool.Name, out Policy.ToolPolicyRow? row);
        IEnumerable<string> direct = (row?.AllowedRoleKeys ?? s.Tool.DefaultRoleKeys).Where(r => r != "application-owner" && !relationRoles.Contains(r, StringComparer.Ordinal));
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

            retiredOn = component.Text("retiredOn") ?? s.PersonToday;
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
                    s.Update("technical-component", old.Id, Js.Obj(("retiredOn", JsonTreeNode.FromString(s.PersonToday))));
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
        int subjects = new[] { s.Text("businessApplicationId"), s.Text("dataDomainKey"), s.Text("dataLocationId") }.Count(v => v != null);
        if (subjects != 1)
        {
            throw s.Invalid("businessApplicationId", "Name one subject: businessApplicationId, dataDomainKey or dataLocationId.");
        }

        if (s.Text("businessApplicationId") == null)
        {
            return ProposeDataOwnerChange(s);
        }

        StoredRow app = RequireApplication(s);
        // The data owner and the data governance lead in this tool's roles stand for the data subjects (D-41, question 4):
        // for an application, the owner relation still applies to them.
        RequireOwnerRelation(s, app.Id, "data-owner", "data-governance-lead");
        IReadOnlyList<StoredRow> owners = s.CurrentOwners(app.Id);
        foreach (JsonTreeNode change in s.Items("changes"))
        {
            RequireApplicationOwnerRole(s, s.RequireKey("owner-role", Js.Text(change, "ownerRoleKey"), "changes.ownerRoleKey"), "changes.ownerRoleKey");
            if (Js.Text(change, "addObjectId") != null && Js.Text(change, "ownerRoleKey") == EngineKeys.DataOwnerRole
                && new DataTree(s.Snapshot).Mode == DataTaxonomyMode.DomainsRequired)
            {
                // data-taxonomy-mode domains-required (docs/architecture/data-taxonomy.md, Levels and the mode; data design review): the data owner sits on the data
                // domains, never on an application. Ending an existing app-level data owner stays open, for clean-up.
                throw s.Invalid("changes.ownerRoleKey", "This install requires data domains, so the data owner is named on each data domain, not on the application. Declare the application's data uses instead; the domains' data owners decide about them.");
            }

            string? end = Js.Text(change, "endAssignmentId");
            if (end != null && !owners.Any(o => o.Id == end))
            {
                throw s.Invalid("changes.endAssignmentId", "endAssignmentId " + end + " is not a current owner assignment of this application.");
            }
        }

        // The changes travel on the decision in Proposed Changes Json, the form the update-subject effect applies once the
        // decision is accepted (document 25, B25-8): the outcome is assign when the proposal starts a holder, else end.
        IReadOnlyList<JsonTreeNode> proposed = s.Items("changes");
        bool assigns = proposed.Any(c => Js.Text(c, "addObjectId") != null);
        ComposedWrite decision = s.Create("decision", Js.Obj(
            ("decisionType", JsonTreeNode.FromString(EngineKeys.OwnershipDecisionType)),
            ("subjectKind", JsonTreeNode.FromString("business-application")),
            ("subjectApplication", JsonTreeNode.FromString(app.Id)),
            ("statement", JsonTreeNode.FromString(OwnerChangeStatement(s, AppName(app), proposed, owners))),
            ("context", JsonTreeNode.FromString(OwnerChangeSentences(s, proposed, owners))),
            ("reason", JsonTreeNode.FromString(s.RequireText("reason"))),
            ("outcome", Js.OptStr(OwnershipOutcome(s, assigns ? "assign" : "end"))),
            ("proposedChangesJson", Js.Obj(("owners", Arr(proposed)))),
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
            "The owner change is proposed. Nobody is asked yet: submit it, and the people it names answer on its decision page.",
            s.Call.IsDryRun ? null : s.RecordUrl("decision", decision.RowId),
            s.Call.IsDryRun ? Array.Empty<NextStep>() : new[] { ToolScope.Next("submit_record", ("subjectKind", JsonTreeNode.FromString("decision")), ("subjectId", JsonTreeNode.FromString(decision.RowId))) });
    }

    // An owner change of a data domain or a data location (docs/architecture/data-taxonomy.md, Appointing owners and Changing the taxonomy; slice 3): an ownership
    // decision whose subject is the domain or the location, routed by the C31 and C32 rows of Appointing owners (a top-level
    // domain asks the data governance lead, a lower one the parent domain's data owner, a location the platform admin;
    // each proposed owner accepts). Its acceptance writes the owner assignment with that subject (G-19). Who proposes:
    // the platform admin and the data governance lead; for a domain, a data owner of the domain or of a domain above it;
    // for a location, its technical owner (or its parent's).
    private static JsonTreeNode ProposeDataOwnerChange(ToolScope s)
    {
        var tree = new DataTree(s.Snapshot);
        string? domainKey = s.Text("dataDomainKey");
        StoredRow? location = s.Text("dataLocationId") is string locationId ? s.RequireRow("data-location", locationId, "dataLocationId") : null;
        OwnerSubjectKind kind = domainKey != null ? OwnerSubjectKind.DataDomain : OwnerSubjectKind.DataLocation;
        string subjectLabel;
        List<StoredRow> owners;
        bool mayPropose = s.HoldsRole("platform-admin") || s.HoldsRole("data-governance-lead");
        if (domainKey != null)
        {
            SnapshotRow domain = tree.Domain(domainKey) ?? throw s.NotFound("Data Domain", domainKey, "dataDomainKey");
            if (!domain.IsActive)
            {
                throw s.Invalid("dataDomainKey", tree.PathText(domainKey) + " is retired, so it takes no new owners.");
            }

            subjectLabel = tree.PathText(domainKey);
            owners = CurrentSubjectOwners(s, "subjectDataDomain", domainKey);
            mayPropose = mayPropose || tree.Chain(domainKey).Any(n => CurrentSubjectOwners(s, "subjectDataDomain", n.KeyPath)
                .Any(o => o.Text("ownerRole") == EngineKeys.DataOwnerRole && Same(o.Text("principal"), s.PersonId)));
        }
        else
        {
            if (!IsOpenDataRow(location!, s))
            {
                throw s.Invalid("dataLocationId", (location!.Text("labelAsSeen") ?? location.Id) + " is retired, so it takes no new owners.");
            }

            subjectLabel = location!.Text("labelAsSeen") ?? location.Id;
            owners = CurrentSubjectOwners(s, "subjectDataLocation", location.Id);
            mayPropose = mayPropose || IsTechnicalOwnerOf(s, location);
        }

        if (!mayPropose)
        {
            throw s.Refused(McpErrorCodes.NotAuthorized, "An owner change of " + subjectLabel + " is proposed by the platform admin, the data governance lead, or "
                + (domainKey != null ? "a data owner of the domain or of a domain above it." : "the location's technical owner."));
        }

        foreach (JsonTreeNode change in s.Items("changes"))
        {
            SnapshotRow role = s.RequireKey("owner-role", Js.Text(change, "ownerRoleKey"), "changes.ownerRoleKey");
            if (!DataTree.CanHold(role, kind))
            {
                throw s.Invalid("changes.ownerRoleKey", "changes.ownerRoleKey \"" + role.KeyPath + "\" is not held on a " + (domainKey != null ? "data domain" : "data location")
                    + ". Choose an owner role such as " + (domainKey != null ? "data-owner or data-steward." : "technical-owner."));
            }

            string? end = Js.Text(change, "endAssignmentId");
            if (end != null && !owners.Any(o => o.Id == end))
            {
                throw s.Invalid("changes.endAssignmentId", "endAssignmentId " + end + " is not a current owner assignment of " + subjectLabel + ".");
            }
        }

        IReadOnlyList<JsonTreeNode> proposed = s.Items("changes");
        bool assigns = proposed.Any(c => Js.Text(c, "addObjectId") != null);
        ComposedWrite decision = s.Create("decision", Js.Obj(
            ("decisionType", JsonTreeNode.FromString(EngineKeys.OwnershipDecisionType)),
            ("subjectKind", JsonTreeNode.FromString(domainKey != null ? EngineKeys.DataDomainSubjectKind : EngineKeys.DataLocationSubjectKind)),
            (domainKey != null ? "subjectDataDomain" : "subjectDataLocation", JsonTreeNode.FromString(domainKey ?? location!.Id)),
            ("statement", JsonTreeNode.FromString(OwnerChangeStatement(s, subjectLabel, proposed, owners))),
            ("context", JsonTreeNode.FromString(OwnerChangeSentences(s, proposed, owners))),
            ("reason", JsonTreeNode.FromString(s.RequireText("reason"))),
            ("outcome", Js.OptStr(OwnershipOutcome(s, assigns ? "assign" : "end"))),
            ("proposedChangesJson", Js.Obj(("owners", Arr(proposed)))),
            ("status", JsonTreeNode.FromString(s.StatusFor("decision", "proposed")))));
        JsonTreeNode data = ToolScope.Merge(
            s.WriteResult("decision", decision),
            ("decisionId", s.Call.IsDryRun ? JsonTreeNode.Null : JsonTreeNode.FromString(decision.RowId)),
            ("pendingAssignments", Js.EmptyArray),
            ("approvalsNeeded", Arr(ApprovalsNeeded(s, decision.RowId, "ownership"))));
        return s.Answer(
            data,
            "The owner change of " + subjectLabel + " is proposed. The approvers and each proposed owner answer once it is submitted.",
            s.Call.IsDryRun ? null : s.RecordUrl("decision", decision.RowId),
            s.Call.IsDryRun ? Array.Empty<NextStep>() : new[] { ToolScope.Next("submit_record", ("subjectKind", JsonTreeNode.FromString("decision")), ("subjectId", JsonTreeNode.FromString(decision.RowId))) });
    }

    // The statement of an owner change in plain words (product review 2026-11-24): one change names who takes or leaves
    // which role ("Make Kai Ostrowski deputy business owner of Inspection Scheduler."); several say how many.
    private static string OwnerChangeStatement(ToolScope s, string appName, IReadOnlyList<JsonTreeNode> changes, IReadOnlyList<StoredRow> owners)
    {
        // The change in words, one clause per change, never a count (QA9): "Make Nadia Ferreira business owner and end
        // Wren Abernathy as business owner of Claims Triage Agent."
        var clauses = new List<string>();
        foreach (JsonTreeNode change in changes)
        {
            string role = OwnerRoleWords(s, change, owners);
            string? ended = EndedHolder(s, change, owners);
            if (Js.Text(change, "addObjectId") is string add)
            {
                clauses.Add((clauses.Count == 0 ? "Make " : "make ") + s.DisplayLabel(add) + " " + role);
            }
            else if (ended != null)
            {
                clauses.Add((clauses.Count == 0 ? "End " : "end ") + ended + " as " + role);
            }
        }

        if (clauses.Count == 0)
        {
            return "Change the owners of " + appName + ".";
        }

        string joined = clauses.Count == 1 ? clauses[0] : string.Join(", ", clauses.Take(clauses.Count - 1)) + " and " + clauses[clauses.Count - 1];
        return joined + " of " + appName + ".";
    }

    // The context of an owner change: one sentence per change, never JSON or object IDs. Dates read as the pages show
    // them (19 Dec 2026).
    private static string OwnerChangeSentences(ToolScope s, IReadOnlyList<JsonTreeNode> changes, IReadOnlyList<StoredRow> owners)
    {
        var sentences = new List<string>();
        foreach (JsonTreeNode change in changes)
        {
            string role = OwnerRoleWords(s, change, owners);
            string? ended = EndedHolder(s, change, owners);
            string? add = Js.Text(change, "addObjectId") is string id ? s.DisplayLabel(id) : null;
            string from = Js.Text(change, "startOn") is string start ? " from " + DayWords(start) : string.Empty;
            if (add != null && ended != null)
            {
                sentences.Add(add + " takes over as " + role + " from " + ended + from + ".");
            }
            else if (add != null)
            {
                sentences.Add(add + " becomes " + role + from + ".");
            }
            else if (ended != null)
            {
                sentences.Add(ended + " stops being " + role + (from.Length == 0 ? string.Empty : " on" + from.Substring(" from".Length)) + ".");
            }
        }

        return sentences.Count == 0 ? "No owner change." : string.Join(" ", sentences);
    }

    // A yyyy-MM-dd day as the pages write it: 19 Dec 2026.
    private static string DayWords(string day)
    {
        return DateTime.TryParseExact(day.Length >= 10 ? day.Substring(0, 10) : day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed)
            ? parsed.ToString("d MMM yyyy", CultureInfo.InvariantCulture)
            : day;
    }

    // "business owner", or "deputy business owner" for a deputy change. An end item takes the deputy flag of the
    // assignment it ends, which the item itself does not carry (QA9).
    private static string OwnerRoleWords(ToolScope s, JsonTreeNode change, IReadOnlyList<StoredRow> owners)
    {
        string role = s.Label("owner-role", Js.Text(change, "ownerRoleKey")).ToLowerInvariant();
        bool deputy = Js.Flag(change, "isDeputy") == true;
        if (Js.Text(change, "addObjectId") == null && EndedRow(s, change, owners) is StoredRow ended)
        {
            deputy = ended.Flag("isDeputy");
        }

        return deputy ? "deputy " + role : role;
    }

    private static StoredRow? EndedRow(ToolScope s, JsonTreeNode change, IReadOnlyList<StoredRow> owners)
    {
        string? end = Js.Text(change, "endAssignmentId");
        return end == null ? null : owners.FirstOrDefault(o => o.Id == end) ?? s.Rows.Get("owner-assignment", end);
    }

    // The person whose assignment a change ends.
    private static string? EndedHolder(ToolScope s, JsonTreeNode change, IReadOnlyList<StoredRow> owners)
    {
        return EndedRow(s, change, owners)?.Text("principal") is string principal ? s.DisplayLabel(principal) : null;
    }

    // The ownership outcome a proposal names, when the configuration in force holds it active.
    private static string? OwnershipOutcome(ToolScope s, string outcome)
    {
        return s.ConfigRow("decision-outcome", EngineKeys.OwnershipDecisionType + "/" + outcome) is SnapshotRow row && row.IsActive ? outcome : null;
    }

    // The owner roles a person holds on data domains and data locations (O06 with a data subject; docs/architecture/data-taxonomy.md,
    // Appointing owners), with whether each has a deputy on the same node. No owner check review opens for them: the finding is DC-04,
    // and its fix is propose_owner_change on the domain or the location.
    private static List<JsonTreeNode> AffectedDataSubjects(ToolScope s, IEnumerable<StoredRow> assignments)
    {
        var tree = new DataTree(s.Snapshot);
        var list = new List<JsonTreeNode>();
        foreach (StoredRow assignment in assignments.Where(a => !a.Flag("isDeputy") && (a.Text("subjectDataDomain") != null || a.Text("subjectDataLocation") != null)).OrderBy(a => a.Id, StringComparer.Ordinal))
        {
            string role = assignment.Text("ownerRole") ?? string.Empty;
            string? domain = assignment.Text("subjectDataDomain");
            string? location = domain == null ? assignment.Text("subjectDataLocation") : null;
            List<StoredRow> peers = domain != null ? CurrentSubjectOwners(s, "subjectDataDomain", domain) : CurrentSubjectOwners(s, "subjectDataLocation", location!);
            StoredRow? locationRow = location == null ? null : s.Rows.Get("data-location", location);
            list.Add(Js.Obj(
                ("subjectKind", JsonTreeNode.FromString(domain != null ? EngineKeys.DataDomainSubjectKind : EngineKeys.DataLocationSubjectKind)),
                ("dataDomainKey", Js.Str(domain)),
                ("dataLocationId", Js.Str(location)),
                ("label", JsonTreeNode.FromString(domain != null ? (tree.Domain(domain) == null ? domain : tree.PathText(domain)) : locationRow?.Text("labelAsSeen") ?? location!)),
                ("ownerRoleKey", JsonTreeNode.FromString(role)),
                ("hasDeputy", JsonTreeNode.FromBoolean(peers.Any(o => o.Text("ownerRole") == role && o.Flag("isDeputy"))))));
        }

        return list;
    }

    // ---- 46. record_owner_leave ----

    private static JsonTreeNode RecordOwnerLeave(ToolScope s)
    {
        s.RequireAiState("as-reported");
        string person = s.RequireText("principalObjectId");
        string leavesOn = s.RequireText("leavesOn");
        StoredRow principal = s.Principal(person) ?? throw s.NotFound("Principal Reference", person, "principalObjectId");
        var assignments = s.Rows.Query(RowQuery.For("owner-assignment")).Where(o => Same(o.Text("principal"), person) && o.Text("endOn") == null).ToList();

        // The owners and the reviews of the person's applications in two key set reads, not one each per application
        // (wave 4b review: 1,346 reads and 3.3 s at 2,000 applications). A write call has no read cache, so they are read
        // once here and grouped; the leave changes no owner assignment, so they stay true for the whole call.
        List<string> leaverApps = assignments.Select(a => a.Text("businessApplication")).Where(a => a != null).Select(a => a!).Distinct(StringComparer.Ordinal).ToList();
        DateTime ownersDay = s.Now.Date;
        Dictionary<string, List<StoredRow>> ownersOf = s.Rows.QueryIn("owner-assignment", "businessApplication", leaverApps)
            .Where(r => (Js.ParseMoment(r.Text("startOn")) ?? DateTime.MinValue).Date <= ownersDay)
            .Where(r => r.Text("endOn") == null || (Js.ParseMoment(r.Text("endOn")) ?? DateTime.MaxValue).Date > ownersDay)
            .GroupBy(r => r.Text("businessApplication")!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        Dictionary<string, List<StoredRow>> reviewsOf = s.Rows.QueryIn("review", "businessApplication", leaverApps)
            .GroupBy(r => r.Text("businessApplication")!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.Id, StringComparer.Ordinal).ToList(), StringComparer.Ordinal);
        IReadOnlyList<StoredRow> OwnersOf(string app) => ownersOf.TryGetValue(app, out List<StoredRow>? found) ? found : (IReadOnlyList<StoredRow>)Array.Empty<StoredRow>();
        bool self = Same(person, s.PersonId);
        bool ownerOfTheirApp = leaverApps.Any(app => OwnersOf(app).Any(o => Same(o.Text("principal"), s.PersonId)));
        // The data taxonomy (docs/architecture/data-taxonomy.md, Appointing owners): the data governance lead records the leave of a person who owns data domains or
        // data locations, as an application's owner records the leave of a fellow owner.
        bool dataLead = s.HoldsRole("data-governance-lead") && assignments.Any(a => a.Text("subjectDataDomain") != null || a.Text("subjectDataLocation") != null);
        if (!self && !ownerOfTheirApp && !dataLead && !s.HoldsRole("platform-admin"))
        {
            throw s.Refused(McpErrorCodes.NotAuthorized, "Only the leaving person, an owner of an application the person owns, the data governance lead for data owners, or a platform admin records a leave.");
        }

        ComposedWrite write = s.Call.RecordPrincipalFacts(principal.Id, leavesOn, null);
        string reviewType = s.ConfigRow("review-type", "owner-check") is SnapshotRow r && r.IsActive ? "owner-check"
            : s.ActiveRows("review-type").Where(t => Js.Text(t.Columns, "producesDecisionTypeKey") == "ownership").Select(t => t.KeyPath).FirstOrDefault()
            ?? throw s.Call.Fail("unavailable", "meaning.missing", "No active review type produces ownership decisions, so no owner check review can open.", "Review Type", null);
        var reviewIds = new List<string>();
        var reusedIds = new List<string>();
        var affected = new List<JsonTreeNode>();
        foreach (IGrouping<string, StoredRow> byApp in assignments.Where(a => a.Text("businessApplication") != null).GroupBy(a => a.Text("businessApplication")!, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            IReadOnlyList<StoredRow> owners = OwnersOf(byApp.Key);
            foreach (StoredRow assignment in byApp.Where(a => !a.Flag("isDeputy")))
            {
                string role = assignment.Text("ownerRole") ?? string.Empty;
                affected.Add(Js.Obj(
                    ("businessApplicationId", JsonTreeNode.FromString(byApp.Key)),
                    ("ownerRoleKey", JsonTreeNode.FromString(role)),
                    ("hasDeputy", JsonTreeNode.FromBoolean(owners.Any(o => o.Text("ownerRole") == role && o.Flag("isDeputy"))))));
            }

            // An owner check already due covers the leave. A leave makes it due now: a check planned by the cadence for
            // later is brought forward to today. The leave date itself stays on the person's reference only (platform
            // admin, document 24): any role reads a review's due date, so it never carries the leave date (security
            // review 2026-11-24, B24-14).
            StoredRow? open = (reviewsOf.TryGetValue(byApp.Key, out List<StoredRow>? appReviews) ? appReviews : new List<StoredRow>())
                .FirstOrDefault(r => r.Text("reviewType") == reviewType && r.Text("statusMeaning") == "due");
            string reviewId;
            if (open != null)
            {
                reviewId = open.Id;
                if (open.Text("dueOn") is not string due || string.CompareOrdinal(due, s.PersonToday) > 0)
                {
                    s.Update("review", open.Id, Js.Obj(("dueOn", JsonTreeNode.FromString(s.PersonToday))));
                }

                reusedIds.Add(reviewId);
            }
            else
            {
                reviewId = s.Create("review", Js.Obj(
                    ("businessApplication", JsonTreeNode.FromString(byApp.Key)),
                    ("reviewType", JsonTreeNode.FromString(reviewType)),
                    ("status", JsonTreeNode.FromString(s.StatusFor("review", "due"))),
                    ("trigger", JsonTreeNode.FromString("owner-check")),
                    ("dueOn", JsonTreeNode.FromString(s.PersonToday)))).RowId;
            }

            if (!s.Call.IsDryRun)
            {
                reviewIds.Add(reviewId);
            }
        }

        int pending = s.Query("approval-record", ("askedPrincipal", person), ("outcomeMeaning", "pending")).Count;
        List<JsonTreeNode> affectedData = AffectedDataSubjects(s, assignments);
        JsonTreeNode data = ToolScope.Merge(
            s.WriteResult("principal-reference", write, hasStatus: false),
            ("affected", Arr(affected)),
            ("affectedData", Arr(affectedData)),
            ("ownerCheckReviewIds", Ids(reviewIds)),
            ("reusedOwnerCheckReviewIds", Ids(reusedIds)),
            ("pendingApprovals", JsonTreeNode.FromInt64(pending)));
        string roles = affected.Count == 1 ? "1 owner role is affected" : affected.Count + " owner roles are affected";
        string dataRoles = affectedData.Count == 0 ? string.Empty : ", and " + Count(affectedData.Count, "owner role on data", "owner roles on data") + " (" + string.Join(", ", affectedData.Select(a => Js.Text(a, "label")).Distinct(StringComparer.Ordinal)) + ")";
        NextStep[] next = affected.Count > 0
            ? new[] { ToolScope.Next("propose_owner_change", ("businessApplicationId", JsonTreeNode.FromString(Js.Text(affected[0], "businessApplicationId")!))) }
            : affectedData.Count > 0
                ? new[] { ToolScope.Next("propose_owner_change", Js.Text(affectedData[0], "dataDomainKey") is string domain ? ("dataDomainKey", JsonTreeNode.FromString(domain)) : ("dataLocationId", JsonTreeNode.FromString(Js.Text(affectedData[0], "dataLocationId")!))) }
                : Array.Empty<NextStep>();
        return s.Answer(
            data,
            "The leave on " + leavesOn + " is recorded, as reported. " + roles + dataRoles + "; a successor needs an accepted ownership decision. Pending approvals move to the deputy.",
            null,
            next);
    }
}
