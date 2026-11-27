using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;
using Product.Engine.Core.Snapshots;

namespace Product.Engine.Core.Operations;

// What the update-subject effect reads about an accepted decision's subject (document 25, B25-8): the rows the store
// holds now, each a JSON object of its columns with its "id". The adapter fetches them when it accepts a decision, as it
// fetches the OP-07 payload for a gate decision. The core makes no random values, so the adapter also passes the IDs a
// new row may take (a new application for an accepted intake, a new owner assignment). A proposal of this build.
public sealed class SubjectFacts
{
    internal static readonly string[] WireMembers = { "application", "intake", "changeRequest", "tierAssessment", "owners", "dueReviewIds", "newRowIds", "intakeOwnerConsented" };

    public SubjectFacts(
        JsonTreeNode? application = null,
        JsonTreeNode? intake = null,
        JsonTreeNode? changeRequest = null,
        JsonTreeNode? tierAssessment = null,
        IEnumerable<JsonTreeNode>? owners = null,
        IEnumerable<string>? dueReviewIds = null,
        IEnumerable<string>? newRowIds = null,
        bool intakeOwnerConsented = false)
    {
        Application = RequireRow(application, nameof(application));
        Intake = RequireRow(intake, nameof(intake));
        ChangeRequest = RequireRow(changeRequest, nameof(changeRequest));
        TierAssessment = RequireRow(tierAssessment, nameof(tierAssessment));
        Owners = new ReadOnlyCollection<JsonTreeNode>((owners ?? Enumerable.Empty<JsonTreeNode>()).Select(o => RequireRow(o, nameof(owners))!).ToList());
        DueReviewIds = new ReadOnlyCollection<string>((dueReviewIds ?? Enumerable.Empty<string>()).ToList());
        NewRowIds = new ReadOnlyCollection<string>((newRowIds ?? Enumerable.Empty<string>()).ToList());
        IntakeOwnerConsented = intakeOwnerConsented;
    }

    // The application the decision changes: the subject application, or the application the subject intake or change
    // request names. Null when there is none.
    public JsonTreeNode? Application { get; }

    // The subject intake request, for an intake decision.
    public JsonTreeNode? Intake { get; }

    // The subject change request, for a change authorization decision.
    public JsonTreeNode? ChangeRequest { get; }

    // The latest tier assessment of the subject (of the intake, or of the application and its intakes), for a tier
    // decision.
    public JsonTreeNode? TierAssessment { get; }

    // The current owner assignments of the application (End On empty or later than now).
    public IReadOnlyList<JsonTreeNode> Owners { get; }

    // The reviews of the application still due, for a decision that moves it to a retired stage.
    public IReadOnlyList<string> DueReviewIds { get; }

    // IDs the adapter gave the rows the effect may create, used in order.
    public IReadOnlyList<string> NewRowIds { get; }

    // True when the business owner the subject intake names accepted the role: their approval record, asked by name on a
    // decision about the intake, is approved and counts (document 25, B25-18). Absent reads as false, so the effect never
    // makes a person an owner who did not consent.
    public bool IntakeOwnerConsented { get; }

    internal static SubjectFacts? FromJson(WireObject? wire)
    {
        if (wire == null)
        {
            return null;
        }

        try
        {
            return new SubjectFacts(
                wire.OptionalRawObject("application"),
                wire.OptionalRawObject("intake"),
                wire.OptionalRawObject("changeRequest"),
                wire.OptionalRawObject("tierAssessment"),
                wire.RawObjects("owners"),
                wire.Strings("dueReviewIds"),
                wire.Strings("newRowIds"),
                wire.Bool("intakeOwnerConsented", false));
        }
        catch (ArgumentException e)
        {
            throw OperationStop.Input(ReasonCodes.InputMalformed, wire.Where + ": " + e.Message);
        }
    }

    private static JsonTreeNode? RequireRow(JsonTreeNode? row, string name)
    {
        if (row == null)
        {
            return null;
        }

        if (row.Kind != JsonTreeKind.Object || !row.TryGetMember("id", out JsonTreeNode? id) || id.Kind != JsonTreeKind.String || id.GetString().Length == 0)
        {
            throw new ArgumentException(name + " must be a stored row: a JSON object of its columns with its \"id\".", name);
        }

        return row;
    }
}

public static partial class WriteGuardOperation
{
    // The update-subject effect (document 25, section 5.10 and B25-8): when a decision is accepted, the engine writes its
    // effect on the subject record in the same transaction, as engine-nested writes of the update-subject effect, each
    // naming the decision as its basis (Tier Decision, Stage Decision, Autonomy Decision, Ownership Decision, Triage
    // Decision, Authorization Decision). A rejected or withdrawn decision changes nothing but itself.
    //
    // The design choices, recorded in document 25, B25-8:
    // - Any outcome's C19 Next Lifecycle Stage moves the application there (document 21, C19). A stage that means retired
    //   also cancels the application's due reviews (document 23, P09 step 7).
    // - Intake: accept registers the linked application (a draft becomes registered) or creates a new registered one from
    //   the request, at the outcome's Next Lifecycle Stage, else at the first stage after the Accept gate (a linked
    //   application only moves forward), and completes the request; redirect completes it; decline rejects it; park keeps
    //   it open. Each names the decision as the request's Triage Decision. The business owner the request names becomes
    //   the business owner of an application the effect creates or a draft it registers, when it has none (B25-10); an
    //   application already in the register changes owners only through an ownership decision (B25-15). The request's
    //   portfolio becomes the new application's (B25-15).
    // - A Next Lifecycle Stage moves an application forward only (B25-15).
    // - Change authorization: authorize moves the change request to in progress (Balanced labels it Scheduled: the record
    //   status meanings are fixed, and none means authorized), reject moves it to rejected.
    // - Tier: the tier the proposal names, or for confirm the tier the latest assessment computed; the assessment takes
    //   the decision as its Tier Decision.
    // - Autonomy level: agree stores the level the proposal names.
    // - Ownership: each proposed change ends the named assignment and starts the new holder, on the change's Start On
    //   when it is later than today. A deputy covers the new main holder of its role, or the current one. Deputies of an
    //   ended holder move to the new holder. A role with Maximum Holders keeps that many main holders: the earliest
    //   current holders end.
    //
    // WORKING DEFAULT (open design question 1): the decision types and outcomes are fixed keys (EngineKeys).
    private sealed partial class GuardRun
    {
        private static readonly string[] OpenRecordMeanings = { "draft", "submitted", "in-progress" };

        // Plans the subject's writes after an accepted decision (Accept, G-17). The writes come after the decision's own
        // effects and before the Audit Entry that lists them.
        private void UpdateSubject(WriteAcceptance acceptance)
        {
            string? decisionId = _request.RowId;
            string? type = Text(Column("decisionType"), null);
            string? outcome = Text(Column("outcome"), null);
            if (decisionId == null || type == null || outcome == null || acceptance.Basis == WriteAcceptance.Bootstrap
                || string.Equals(Text(Column("subjectKind"), null), "configuration-draft", StringComparison.Ordinal))
            {
                return;
            }

            SnapshotRow? outcomeRow = SnapshotRead.Find(_snapshot, "decision-outcome", type + "/" + outcome);
            string? nextStage = outcomeRow == null ? null : SnapshotRead.Text(outcomeRow, "nextLifecycleStageKey");
            bool typed = type == EngineKeys.IntakeDecisionType || type == EngineKeys.ChangeAuthorizationDecisionType || type == EngineKeys.TierDecisionType
                || type == EngineKeys.OwnershipDecisionType || type == EngineKeys.AutonomyLevelDecisionType;
            if (!typed && nextStage == null)
            {
                return;
            }

            SubjectFacts facts = _request.Subject
                ?? throw OperationStop.Input(
                    ReasonCodes.InputRequired,
                    "facts.subject is required to accept a " + Quote(type) + " decision with outcome " + Quote(outcome) + ": its acceptance changes the subject record (the update-subject effect).");
            if (outcomeRow != null)
            {
                _trace.Add(new RuleTrace("Decision Outcome", outcomeRow.KeyPath, RuleTraceRole.Matched));
            }

            SubjectApplicationIsTheDecisions(facts);
            var plan = new SubjectPlan(decisionId, facts);
            switch (type)
            {
                case EngineKeys.IntakeDecisionType:
                    IntakeChanges(plan, outcome, nextStage);
                    break;
                case EngineKeys.ChangeAuthorizationDecisionType:
                    ChangeAuthorizationChanges(plan, outcome);
                    break;
                case EngineKeys.TierDecisionType:
                    TierChanges(plan, outcome);
                    break;
                case EngineKeys.AutonomyLevelDecisionType:
                    AutonomyChanges(plan, outcome);
                    break;
                case EngineKeys.OwnershipDecisionType:
                    OwnershipChanges(plan);
                    break;
            }

            if (type != EngineKeys.IntakeDecisionType && nextStage != null)
            {
                StageChanges(plan, nextStage);
            }

            EmitSubjectChanges(plan);
        }

        // The application the facts name is the decision's own (security review 2026-11-24, document 25, B25-15): its
        // Subject Application, else the application its subject intake or change request names. An adapter that builds
        // the facts wrongly could otherwise make an accepted decision change another application.
        private void SubjectApplicationIsTheDecisions(SubjectFacts facts)
        {
            if (facts.Application == null)
            {
                return;
            }

            string appId = RowIdOf(facts.Application);
            string? expected = Text(Column("subjectApplication"), null) ?? Text(facts.Intake, "businessApplication") ?? Text(facts.ChangeRequest, "businessApplication");
            if (expected == null || !string.Equals(expected, appId, StringComparison.OrdinalIgnoreCase))
            {
                throw OperationStop.Input(
                    ReasonCodes.InputMalformed,
                    "facts.subject.application is " + appId + ", which is not the decision's application" + (expected == null ? " (the decision names none)." : " " + expected + "."),
                    appId);
            }
        }

        // ---- Intake (document 23, P01 step 7) ----

        private void IntakeChanges(SubjectPlan plan, string outcome, string? nextStage)
        {
            JsonTreeNode intake = plan.Facts.Intake
                ?? throw OperationStop.Input(ReasonCodes.InputRequired, "facts.subject.intake is required to accept an intake decision.");
            string intakeId = RowIdOf(intake);
            string? subject = Text(Column("subjectIntakeRequest"), null);
            if (subject != null && !string.Equals(subject, intakeId, StringComparison.OrdinalIgnoreCase))
            {
                throw OperationStop.Input(ReasonCodes.InputMalformed, "facts.subject.intake is " + intakeId + ", which is not the decision's subject intake request " + subject + ".");
            }

            plan.SubjectEntity = "Intake Request";
            plan.SubjectId = intakeId;
            string? meaning = Text(intake, "statusMeaning");
            if (meaning != null && Array.IndexOf(OpenRecordMeanings, meaning) < 0)
            {
                Skip("The intake request is " + meaning + ", so the accepted decision does not change it.", intakeId);
                return;
            }

            var columns = new List<(string Name, JsonTreeNode Value)> { ("triageDecision", JsonTreeNode.FromString(plan.DecisionId)) };
            switch (outcome)
            {
                case EngineKeys.IntakeAcceptOutcome:
                    string? appId = RegisterFromIntake(plan, intake, nextStage);
                    if (appId != null && Text(intake, "businessApplication") == null)
                    {
                        columns.Add(("businessApplication", JsonTreeNode.FromString(appId)));
                    }

                    if (appId != null && (string.Equals(appId, plan.NewApplicationId, StringComparison.Ordinal) || plan.RegisteredDraftId == appId))
                    {
                        IntakeBusinessOwner(plan, intake, appId);
                    }
                    else if (appId != null && Text(intake, "businessOwner") is string named)
                    {
                        // An application already in the register changes owners only through an ownership decision
                        // (security review 2026-11-24, B25-15), so the person the request names is not assigned here.
                        Skip("The application is already in the register, so " + named + ", whom the intake names, is not made its business owner. An ownership decision changes the owners.", named);
                    }

                    columns.Add(("status", JsonTreeNode.FromString(DefaultStatus("intake-request", "completed"))));
                    break;
                case EngineKeys.IntakeRedirectOutcome:
                    columns.Add(("status", JsonTreeNode.FromString(DefaultStatus("intake-request", "completed"))));
                    break;
                case EngineKeys.IntakeDeclineOutcome:
                    columns.Add(("status", JsonTreeNode.FromString(DefaultStatus("intake-request", "rejected"))));
                    break;
            }

            plan.Later.Add(new PlannedEffect(EffectKind.UpdateSubject, "Intake Request", WriteOperation.Update, intakeId, null, Columns(columns.ToArray())));
        }

        // Registers the application an accepted intake is for: the linked one, or a new one from the request. Returns its
        // ID, or null when no application can be registered.
        private string? RegisterFromIntake(SubjectPlan plan, JsonTreeNode intake, string? nextStage)
        {
            string? target = ActiveStage(nextStage) ?? StageAfterGate(EngineKeys.IntakeDecisionType);
            string? linked = Text(intake, "businessApplication");
            if (linked != null)
            {
                JsonTreeNode? app = plan.Facts.Application;
                if (app == null || !string.Equals(RowIdOf(app), linked, StringComparison.OrdinalIgnoreCase))
                {
                    throw OperationStop.Input(ReasonCodes.InputRequired, "facts.subject.application is required: the intake request names application " + linked + ".");
                }

                string? appMeaning = Text(app, "statusMeaning");
                if (string.Equals(appMeaning, "draft", StringComparison.Ordinal) || appMeaning == null)
                {
                    plan.App(linked)["status"] = JsonTreeNode.FromString(DefaultStatus("business-application", "registered"));
                    plan.RegisteredDraftId = linked;
                    if (Text(app, "portfolio") == null && Text(intake, "portfolio") is string draftPortfolio)
                    {
                        plan.App(linked)["portfolio"] = JsonTreeNode.FromString(draftPortfolio);
                    }
                }
                else if (string.Equals(appMeaning, "archived", StringComparison.Ordinal))
                {
                    Skip("The application is archived, so the accepted intake does not register it again.", linked);
                }

                int? from = StageRank(Text(app, "lifecycleStage"));
                int? to = StageRank(target);
                if (target != null && to.HasValue && (!from.HasValue || to.Value > from.Value))
                {
                    plan.App(linked)["lifecycleStage"] = JsonTreeNode.FromString(target);
                    plan.App(linked)["stageDecision"] = JsonTreeNode.FromString(plan.DecisionId);
                }

                return linked;
            }

            string? kind = Text(intake, "proposedKind");
            if (kind == null)
            {
                Skip("The intake request names no application kind, so no application is created from it.", RowIdOf(intake));
                return null;
            }

            string newId = plan.NextNewRowId() ?? throw OperationStop.Input(ReasonCodes.InputRequired, "facts.subject.newRowIds must give an ID for the application the accepted intake creates.");
            string title = Text(intake, "title") ?? RowIdOf(intake);
            Dictionary<string, JsonTreeNode> columns = plan.App(newId);
            plan.NewApplicationId = newId;
            columns["name"] = JsonTreeNode.FromString(title);
            columns["purpose"] = JsonTreeNode.FromString(Text(intake, "need") ?? title);
            columns["applicationKind"] = JsonTreeNode.FromString(kind);
            columns["status"] = JsonTreeNode.FromString(DefaultStatus("business-application", "registered"));
            if (Text(intake, "portfolio") is string portfolio)
            {
                columns["portfolio"] = JsonTreeNode.FromString(portfolio);
            }

            if (intake.TryGetMember("usesAi", out JsonTreeNode? usesAi) && (usesAi.Kind == JsonTreeKind.True || usesAi.Kind == JsonTreeKind.False))
            {
                columns["usesAi"] = usesAi;
            }

            if (target != null)
            {
                columns["lifecycleStage"] = JsonTreeNode.FromString(target);
                columns["stageDecision"] = JsonTreeNode.FromString(plan.DecisionId);
            }
            else
            {
                columns["lifecycleStage"] = JsonTreeNode.FromString(FirstStage());
            }

            return newId;
        }

        // The business owner the intake names becomes the application's business owner (document 25, B25-10), on the
        // accepted intake as its basis, when the application has no current main holder of the role. A linked application
        // that already has one keeps it: changing it is an ownership decision, so the change is skipped with a warning.
        private void IntakeBusinessOwner(SubjectPlan plan, JsonTreeNode intake, string appId)
        {
            string? person = Text(intake, "businessOwner");
            if (person == null)
            {
                return;
            }

            string role = EngineKeys.BusinessOwnerRole;
            if (SnapshotRead.FindActive(_snapshot, "owner-role", role) == null)
            {
                Skip("Owner role " + Quote(role) + " is not active in the configuration in force, so the business owner the intake names is not assigned.", role);
                return;
            }

            List<JsonTreeNode> holders = plan.Facts.Owners
                .Where(o => string.Equals(Text(o, "businessApplication") ?? appId, appId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(Text(o, "ownerRole"), role, StringComparison.Ordinal) && !Flag(o, "isDeputy"))
                .ToList();
            if (holders.Any(o => string.Equals(Text(o, "principal"), person, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            if (holders.Count > 0)
            {
                Skip("The application already has a business owner, so " + person + ", whom the intake names, is not assigned. An ownership decision changes the owner.", person);
                return;
            }

            // Document 25, B25-18: the named person becomes an owner only with their consent, given on their own approval
            // request about the intake. Without it the application is registered with no business owner, and an ownership
            // decision names one.
            if (!plan.Facts.IntakeOwnerConsented)
            {
                Skip(person + ", whom the intake names as business owner, has not accepted the role, so they are not assigned. An ownership decision names the business owner.", person);
                return;
            }

            string id = plan.NextNewRowId() ?? throw OperationStop.Input(ReasonCodes.InputRequired, "facts.subject.newRowIds must give an ID for the business owner assignment the accepted intake starts.");
            plan.Later.Add(new PlannedEffect(EffectKind.UpdateSubject, "Owner Assignment", WriteOperation.Create, id, null, Columns(
                ("businessApplication", JsonTreeNode.FromString(appId)),
                ("ownerRole", JsonTreeNode.FromString(role)),
                ("principal", JsonTreeNode.FromString(person)),
                ("isDeputy", JsonTreeNode.False),
                ("startOn", JsonTreeNode.FromString(_context.AsOfUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))),
                ("ownershipDecision", JsonTreeNode.FromString(plan.DecisionId)))));
        }

        // ---- Change authorization (document 23, P03 step 4) ----

        private void ChangeAuthorizationChanges(SubjectPlan plan, string outcome)
        {
            JsonTreeNode change = plan.Facts.ChangeRequest
                ?? throw OperationStop.Input(ReasonCodes.InputRequired, "facts.subject.changeRequest is required to accept a change authorization decision.");
            string changeId = RowIdOf(change);
            string? subject = Text(Column("subjectChangeRequest"), null);
            if (subject != null && !string.Equals(subject, changeId, StringComparison.OrdinalIgnoreCase))
            {
                throw OperationStop.Input(ReasonCodes.InputMalformed, "facts.subject.changeRequest is " + changeId + ", which is not the decision's subject change request " + subject + ".");
            }

            plan.SubjectEntity = "Change Request";
            plan.SubjectId = changeId;
            string? meaning = Text(change, "statusMeaning");
            var columns = new List<(string Name, JsonTreeNode Value)>();
            if (!string.Equals(Text(change, "authorizationDecision"), plan.DecisionId, StringComparison.OrdinalIgnoreCase))
            {
                columns.Add(("authorizationDecision", JsonTreeNode.FromString(plan.DecisionId)));
            }

            string? to = outcome == EngineKeys.ChangeAuthorizeOutcome ? "in-progress" : outcome == EngineKeys.ChangeRejectOutcome ? "rejected" : null;
            if (to != null && !string.Equals(meaning, to, StringComparison.Ordinal))
            {
                bool open = meaning == null || Array.IndexOf(OpenRecordMeanings, meaning) >= 0;
                if (open)
                {
                    columns.Add(("status", JsonTreeNode.FromString(DefaultStatus("change-request", to))));
                }
                else
                {
                    Skip("The change request is " + meaning + ", so the accepted authorization does not move it.", changeId);
                }
            }

            if (columns.Count > 0)
            {
                plan.Later.Add(new PlannedEffect(EffectKind.UpdateSubject, "Change Request", WriteOperation.Update, changeId, null, Columns(columns.ToArray())));
            }
        }

        // ---- Tier and autonomy level (document 23, P02 steps 6 and 8) ----

        private void TierChanges(SubjectPlan plan, string outcome)
        {
            JsonTreeNode? app = plan.Facts.Application;
            if (app == null)
            {
                Skip("The tier decision has no application to set the tier on.", plan.DecisionId);
                return;
            }

            string appId = RowIdOf(app);
            JsonTreeNode? assessment = plan.Facts.TierAssessment;
            string? tier = Text(ProposedChanges(), "tierKey");
            if (tier == null && outcome == EngineKeys.TierConfirmOutcome)
            {
                tier = Text(assessment, "computedTier");
            }

            if (tier == null)
            {
                Skip("The tier decision names no tier, and no tier assessment gives one for " + Quote(outcome) + ", so the tier does not change.", appId);
                return;
            }

            if (SnapshotRead.FindActive(_snapshot, "tier", tier) == null)
            {
                Skip("Tier " + Quote(tier) + " is not an active tier in the configuration in force, so the tier does not change.", tier);
                return;
            }

            plan.App(appId)["tier"] = JsonTreeNode.FromString(tier);
            plan.App(appId)["tierDecision"] = JsonTreeNode.FromString(plan.DecisionId);
            if (assessment != null && Text(assessment, "tierDecision") == null)
            {
                plan.Later.Add(new PlannedEffect(EffectKind.UpdateSubject, "Tier Assessment", WriteOperation.Update, RowIdOf(assessment), null, Columns(("tierDecision", JsonTreeNode.FromString(plan.DecisionId)))));
            }
        }

        private void AutonomyChanges(SubjectPlan plan, string outcome)
        {
            if (outcome != EngineKeys.AutonomyAgreeOutcome)
            {
                return;
            }

            JsonTreeNode? app = plan.Facts.Application;
            string? level = Text(ProposedChanges(), "autonomyLevelKey");
            if (app == null || level == null)
            {
                Skip("The autonomy level decision names " + (app == null ? "no application" : "no autonomy level") + ", so no level is stored.", plan.DecisionId);
                return;
            }

            if (SnapshotRead.FindActive(_snapshot, "list-value", EngineKeys.AutonomyLevelList + "/" + level) == null)
            {
                Skip("Autonomy level " + Quote(level) + " is not an active value of the " + EngineKeys.AutonomyLevelList + " list, so no level is stored.", level);
                return;
            }

            string appId = RowIdOf(app);
            plan.App(appId)["agreedAutonomyLevel"] = JsonTreeNode.FromString(level);
            plan.App(appId)["autonomyDecision"] = JsonTreeNode.FromString(plan.DecisionId);
        }

        // ---- Ownership (document 23, P08 steps 4 and 5) ----

        // The data taxonomy (docs/architecture/data-taxonomy.md, Appointing owners; G-19): an ownership decision about a data domain or a data location writes owner
        // assignments with that subject, its Subject Kind set; facts.subject.owners are then that subject's current rows.
        private void OwnershipChanges(SubjectPlan plan)
        {
            JsonTreeNode? app = plan.Facts.Application;
            JsonTreeNode? proposal = ProposedChanges();
            IReadOnlyList<JsonTreeNode> changes = proposal != null && proposal.TryGetMember("owners", out JsonTreeNode? list) && list.Kind == JsonTreeKind.Array ? list.Items : Array.Empty<JsonTreeNode>();
            string? subjectKind = Text(Column("subjectKind"), null);
            string? dataSubject = subjectKind == EngineKeys.DataDomainSubjectKind ? Text(Column("subjectDataDomain"), null)
                : subjectKind == EngineKeys.DataLocationSubjectKind ? Text(Column("subjectDataLocation"), null)
                : null;
            bool onData = dataSubject != null;
            if ((!onData && app == null) || changes.Count == 0)
            {
                Skip("The ownership decision names " + (!onData && app == null ? "no application" : "no owner changes") + ", so no owner assignment changes.", plan.DecisionId);
                return;
            }

            string appId = onData ? dataSubject! : RowIdOf(app!);
            string subjectColumn = subjectKind == EngineKeys.DataDomainSubjectKind ? "subjectDataDomain" : subjectKind == EngineKeys.DataLocationSubjectKind ? "subjectDataLocation" : "businessApplication";
            plan.SubjectEntity ??= subjectKind == EngineKeys.DataDomainSubjectKind ? "Data Domain" : subjectKind == EngineKeys.DataLocationSubjectKind ? "Data Location" : "Business Application";
            plan.SubjectId ??= appId;
            string today = _context.AsOfUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            List<JsonTreeNode> current = plan.Facts.Owners.Where(o => onData
                ? string.Equals(Text(o, subjectColumn), appId, StringComparison.OrdinalIgnoreCase)
                : string.Equals(Text(o, "businessApplication") ?? appId, appId, StringComparison.OrdinalIgnoreCase)).ToList();
            var ended = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var endNamesDate = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var newMain = new Dictionary<string, string>(StringComparer.Ordinal);
            var adds = new List<(string Id, string Role, string Principal, bool Deputy, string StartOn)>();

            foreach (JsonTreeNode change in changes)
            {
                string? role = Text(change, "ownerRoleKey");
                string effective = Later(today, Text(change, "startOn"));
                string? end = Text(change, "endAssignmentId");
                if (end != null)
                {
                    JsonTreeNode? row = current.FirstOrDefault(o => string.Equals(RowIdOf(o), end, StringComparison.OrdinalIgnoreCase));
                    if (row == null || ended.ContainsKey(RowIdOf(row)))
                    {
                        Skip("Owner assignment " + end + " is no longer a current owner of the application, so it is not ended again.", end);
                    }
                    else
                    {
                        ended[RowIdOf(row)] = effective;
                        if (Text(change, "startOn") != null)
                        {
                            endNamesDate.Add(RowIdOf(row));
                        }
                    }
                }

                string? add = Text(change, "addObjectId");
                if (add == null || role == null)
                {
                    continue;
                }

                bool deputy = change.TryGetMember("isDeputy", out JsonTreeNode? flag) && flag.Kind == JsonTreeKind.True;
                if (SnapshotRead.FindActive(_snapshot, "owner-role", role) == null)
                {
                    Skip("Owner role " + Quote(role) + " is not active in the configuration in force, so " + add + " is not assigned to it.", role);
                    continue;
                }

                bool holds = current.Any(o => !ended.ContainsKey(RowIdOf(o)) && string.Equals(Text(o, "ownerRole"), role, StringComparison.Ordinal)
                    && string.Equals(Text(o, "principal"), add, StringComparison.OrdinalIgnoreCase) && Flag(o, "isDeputy") == deputy)
                    || adds.Any(a => a.Role == role && string.Equals(a.Principal, add, StringComparison.OrdinalIgnoreCase) && a.Deputy == deputy);
                if (holds)
                {
                    Skip(add + " already holds " + Quote(role) + (deputy ? " as deputy" : string.Empty) + " on the application, so no second assignment starts.", add);
                    continue;
                }

                string id = plan.NextNewRowId() ?? throw OperationStop.Input(ReasonCodes.InputRequired, "facts.subject.newRowIds must give an ID for each owner assignment the accepted ownership decision starts.");
                adds.Add((id, role, add, deputy, Later(today, Text(change, "startOn"))));
                if (!deputy && !newMain.ContainsKey(role))
                {
                    newMain[role] = id;
                }
            }

            // A role with Maximum Holders keeps that many main holders: the earliest current holders end when new ones start.
            foreach (KeyValuePair<string, string> role in newMain)
            {
                int? maximum = SnapshotRead.Int(SnapshotRead.FindActive(_snapshot, "owner-role", role.Key)!, "owner-role", "maximumHolders");
                if (!maximum.HasValue)
                {
                    continue;
                }

                string startOn = adds.First(a => a.Id == role.Value).StartOn;
                List<JsonTreeNode> staying = current
                    .Where(o => !ended.ContainsKey(RowIdOf(o)) && !Flag(o, "isDeputy") && string.Equals(Text(o, "ownerRole"), role.Key, StringComparison.Ordinal))
                    .OrderBy(o => Text(o, "startOn") ?? string.Empty, StringComparer.Ordinal)
                    .ThenBy(RowIdOf, StringComparer.Ordinal)
                    .ToList();
                int count = staying.Count + adds.Count(a => a.Role == role.Key && !a.Deputy);
                foreach (JsonTreeNode old in staying.Take(Math.Max(0, count - maximum.Value)))
                {
                    ended[RowIdOf(old)] = startOn;
                }
            }

            // A successor never leaves a role empty (document 25, B25-20): when this decision starts a new main holder of
            // a role later than today, an assignment of that role it ends with no date of its own ends on the new
            // holder's start date, so the outgoing holder (and their deputy) stay until the successor starts.
            foreach (string endId in ended.Keys.ToList())
            {
                if (endNamesDate.Contains(endId))
                {
                    continue;
                }

                JsonTreeNode? row = current.FirstOrDefault(o => string.Equals(RowIdOf(o), endId, StringComparison.OrdinalIgnoreCase));
                string? endedRole = row == null ? null : Text(row, "ownerRole");
                if (endedRole != null && newMain.TryGetValue(endedRole, out string? successor))
                {
                    string successorStart = adds.First(a => a.Id == successor).StartOn;
                    if (string.CompareOrdinal(successorStart, ended[endId]) > 0)
                    {
                        ended[endId] = successorStart;
                    }
                }
            }

            foreach (KeyValuePair<string, string> end in ended.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                plan.Later.Add(new PlannedEffect(EffectKind.UpdateSubject, "Owner Assignment", WriteOperation.Update, end.Key, null, Columns(("endOn", JsonTreeNode.FromString(end.Value)))));
            }

            foreach ((string id, string role, string principal, bool deputy, string startOn) in adds)
            {
                string? covers = null;
                if (deputy)
                {
                    covers = newMain.TryGetValue(role, out string? main) ? main
                        : current.Where(o => !ended.ContainsKey(RowIdOf(o)) && !Flag(o, "isDeputy") && string.Equals(Text(o, "ownerRole"), role, StringComparison.Ordinal))
                            .Select(RowIdOf).OrderBy(i => i, StringComparer.Ordinal).FirstOrDefault();
                    if (covers == null)
                    {
                        Skip(principal + " starts as deputy " + Quote(role) + " with no main holder to cover yet.", principal);
                    }
                }

                var assignment = new List<(string Name, JsonTreeNode Value)>
                {
                    (subjectColumn, JsonTreeNode.FromString(appId)),
                    ("ownerRole", JsonTreeNode.FromString(role)),
                    ("principal", JsonTreeNode.FromString(principal)),
                    ("isDeputy", JsonTreeNode.FromBoolean(deputy)),
                    ("deputyFor", covers == null ? JsonTreeNode.Null : JsonTreeNode.FromString(covers)),
                    ("startOn", JsonTreeNode.FromString(startOn)),
                    ("ownershipDecision", JsonTreeNode.FromString(plan.DecisionId)),
                };
                if (onData)
                {
                    assignment.Add(("subjectKind", JsonTreeNode.FromString(subjectKind!)));
                }

                plan.Later.Add(new PlannedEffect(EffectKind.UpdateSubject, "Owner Assignment", WriteOperation.Create, id, null, Columns(assignment.ToArray())));
            }

            // The deputies of an ended holder cover the new holder of the role.
            foreach (JsonTreeNode deputyRow in current.Where(o => Flag(o, "isDeputy") && !ended.ContainsKey(RowIdOf(o))).OrderBy(RowIdOf, StringComparer.Ordinal))
            {
                string? covered = Text(deputyRow, "deputyFor");
                string? role = Text(deputyRow, "ownerRole");
                if (covered != null && role != null && ended.ContainsKey(covered) && newMain.TryGetValue(role, out string? successor))
                {
                    plan.Later.Add(new PlannedEffect(EffectKind.UpdateSubject, "Owner Assignment", WriteOperation.Update, RowIdOf(deputyRow), null, Columns(("deputyFor", JsonTreeNode.FromString(successor)))));
                }
            }
        }

        // ---- The C19 Next Lifecycle Stage (document 21, C19; document 23, P09 step 7) ----

        private void StageChanges(SubjectPlan plan, string nextStage)
        {
            JsonTreeNode? app = plan.Facts.Application;
            SnapshotRow? stage = SnapshotRead.FindActive(_snapshot, "lifecycle-stage", nextStage);
            if (app == null || stage == null)
            {
                Skip(app == null ? "The decision has no application, so the stage does not move." : "Lifecycle stage " + Quote(nextStage) + " is not active in the configuration in force, so the stage does not move.", plan.DecisionId);
                return;
            }

            string appId = RowIdOf(app);
            plan.SubjectEntity ??= "Business Application";
            plan.SubjectId ??= appId;
            _trace.Add(new RuleTrace("Lifecycle Stage", stage.KeyPath, RuleTraceRole.Matched));
            // An application only moves forward (D-41, 2026-11-24, B25-15): a gate decision accepted for an application
            // already past that stage leaves its stage as it is.
            int? from = StageRank(Text(app, "lifecycleStage"));
            int? to = StageRank(nextStage);
            if (from.HasValue && to.HasValue && to.Value < from.Value)
            {
                Skip("The application is already at a later stage than " + Quote(nextStage) + ", so the stage does not move back.", appId);
                return;
            }

            if (!string.Equals(Text(app, "lifecycleStage"), nextStage, StringComparison.Ordinal))
            {
                plan.App(appId)["lifecycleStage"] = JsonTreeNode.FromString(nextStage);
                plan.App(appId)["stageDecision"] = JsonTreeNode.FromString(plan.DecisionId);
            }

            // A retired application keeps its history but nothing stays due (document 23, P09 step 7).
            if (string.Equals(stage.EngineMeaning, "retired", StringComparison.Ordinal))
            {
                string cancelled = DefaultStatus("review", "cancelled");
                foreach (string review in plan.Facts.DueReviewIds.OrderBy(r => r, StringComparer.Ordinal))
                {
                    plan.Later.Add(new PlannedEffect(EffectKind.UpdateSubject, "Review", WriteOperation.Update, review, null, Columns(("status", JsonTreeNode.FromString(cancelled)))));
                }
            }
        }

        // ---- The plan ----

        // The application write first (a new application before the request that names it), then the other rows, then one
        // Audit Entry that names the decision and every row it changed.
        private void EmitSubjectChanges(SubjectPlan plan)
        {
            var writes = new List<PlannedEffect>();
            foreach (KeyValuePair<string, Dictionary<string, JsonTreeNode>> app in plan.Applications)
            {
                bool create = string.Equals(app.Key, plan.NewApplicationId, StringComparison.Ordinal);
                writes.Add(new PlannedEffect(
                    EffectKind.UpdateSubject,
                    "Business Application",
                    create ? WriteOperation.Create : WriteOperation.Update,
                    app.Key,
                    null,
                    JsonTreeNode.CreateObject(app.Value.OrderBy(c => c.Key, StringComparer.Ordinal))));
            }

            writes.AddRange(plan.Later);
            if (writes.Count == 0)
            {
                return;
            }

            _effects.AddRange(writes);
            string subjectEntity = plan.SubjectEntity ?? "Business Application";
            string? subjectId = plan.SubjectId ?? plan.Applications.Select(a => a.Key).FirstOrDefault() ?? writes[0].RowId;
            JsonTreeNode changes = JsonTreeNode.CreateArray(writes.Select(w => Columns(
                ("entity", JsonTreeNode.FromString(w.Entity)),
                ("operation", JsonTreeNode.FromString(EngineContract.WriteOperations.ToWire(w.Operation))),
                ("rowId", w.RowId == null ? JsonTreeNode.Null : JsonTreeNode.FromString(w.RowId)),
                ("columns", w.Columns))));
            _effects.Add(Audit("subject-updated", subjectEntity, subjectId, Columns(("basisDecision", JsonTreeNode.FromString(plan.DecisionId)), ("changes", changes))));
        }

        private sealed class SubjectPlan
        {
            private int _nextNew;

            public SubjectPlan(string decisionId, SubjectFacts facts)
            {
                DecisionId = decisionId;
                Facts = facts;
            }

            public string DecisionId { get; }

            public SubjectFacts Facts { get; }

            public string? SubjectEntity { get; set; }

            public string? SubjectId { get; set; }

            public string? NewApplicationId { get; set; }

            // The linked draft application the accepted intake registers, if any.
            public string? RegisteredDraftId { get; set; }

            // The application columns to write, per application ID, in first-touched order.
            public List<KeyValuePair<string, Dictionary<string, JsonTreeNode>>> Applications { get; } = new();

            // Every other row write, in order.
            public List<PlannedEffect> Later { get; } = new();

            public Dictionary<string, JsonTreeNode> App(string id)
            {
                foreach (KeyValuePair<string, Dictionary<string, JsonTreeNode>> app in Applications)
                {
                    if (string.Equals(app.Key, id, StringComparison.OrdinalIgnoreCase))
                    {
                        return app.Value;
                    }
                }

                var columns = new Dictionary<string, JsonTreeNode>(StringComparer.Ordinal);
                Applications.Add(new KeyValuePair<string, Dictionary<string, JsonTreeNode>>(id, columns));
                return columns;
            }

            public string? NextNewRowId()
            {
                return _nextNew < Facts.NewRowIds.Count ? Facts.NewRowIds[_nextNew++] : null;
            }
        }

        // ---- Proposed Changes Json (document 22, O09, a proposal of this build) ----

        // The decision's proposed changes, as stored. Null when it holds none.
        private JsonTreeNode? ProposedChanges()
        {
            JsonTreeNode? value = Column("proposedChangesJson");
            if (value == null || value.Kind == JsonTreeKind.Null)
            {
                return null;
            }

            if (value.Kind != JsonTreeKind.Object)
            {
                throw OperationStop.Input(ReasonCodes.InputMalformed, "proposedChangesJson must be a JSON object.");
            }

            return value;
        }

        // A caller's Proposed Changes Json holds only what the decision type changes, with keys of the configuration in
        // force: owners for ownership, tierKey for tier, autonomyLevelKey for autonomy level. Anything else is invalid
        // input, so the engine never stores a change it would not apply, or one an approver could not read.
        private void ProposedChangesShape()
        {
            if (Nested || !_after.TryGetMember("proposedChangesJson", out JsonTreeNode? value) || value.Kind == JsonTreeKind.Null)
            {
                return;
            }

            string type = Text(Column("decisionType"), null) ?? throw OperationStop.Input(ReasonCodes.InputRequired, "decisionType (in input.after or facts.before) is required when a Decision names proposedChangesJson.");
            const string Path = "input.after.proposedChangesJson";
            if (value.Kind != JsonTreeKind.Object)
            {
                throw OperationStop.Input(ReasonCodes.InputMalformed, Path + " must be a JSON object.");
            }

            string? allowed = type switch
            {
                EngineKeys.OwnershipDecisionType => "owners",
                EngineKeys.TierDecisionType => "tierKey",
                EngineKeys.AutonomyLevelDecisionType => "autonomyLevelKey",
                _ => null,
            };
            foreach (KeyValuePair<string, JsonTreeNode> member in value.Members)
            {
                if (!string.Equals(member.Key, allowed, StringComparison.Ordinal))
                {
                    throw OperationStop.Input(ReasonCodes.InputMalformed, Path + " names \"" + member.Key + "\", which a " + Quote(type) + " decision does not change" + (allowed == null ? "." : "; it takes only \"" + allowed + "\"."), member.Key);
                }
            }

            if (value.TryGetMember("tierKey", out JsonTreeNode? tier))
            {
                RequireActiveKey(tier, "tier", null, Path + ".tierKey");
            }

            if (value.TryGetMember("autonomyLevelKey", out JsonTreeNode? level))
            {
                RequireActiveKey(level, "list-value", EngineKeys.AutonomyLevelList + "/", Path + ".autonomyLevelKey");
            }

            if (value.TryGetMember("owners", out JsonTreeNode? owners))
            {
                if (owners.Kind != JsonTreeKind.Array || owners.Items.Count == 0 || owners.Items.Count > 100)
                {
                    throw OperationStop.Input(ReasonCodes.InputMalformed, Path + ".owners must be a list of 1 to 100 owner changes.");
                }

                for (int i = 0; i < owners.Items.Count; i++)
                {
                    string at = Path + ".owners[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                    var change = new WireObject(owners.Items[i], at, "ownerRoleKey", "addObjectId", "endAssignmentId", "isDeputy", "startOn");
                    RequireActiveKey(owners.Items[i].TryGetMember("ownerRoleKey", out JsonTreeNode? role) ? role : JsonTreeNode.Null, "owner-role", null, at + ".ownerRoleKey");
                    if (change.OptionalString("addObjectId") == null && change.OptionalString("endAssignmentId") == null)
                    {
                        throw OperationStop.Input(ReasonCodes.InputMalformed, at + " needs addObjectId, endAssignmentId or both.");
                    }

                    change.OptionalBool("isDeputy");
                    string? startOn = change.OptionalString("startOn");
                    if (startOn != null && !DateTime.TryParseExact(startOn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    {
                        throw OperationStop.Input(ReasonCodes.InputMalformed, at + ".startOn must be a date such as 2026-12-01.");
                    }
                }
            }
        }

        // Exception Standard and Exception Review On belong to an exception decision only (document 22, O09, proposals;
        // document 23, P04 step 6; document 24, B24-9): another decision type does not depart from a standard, so a
        // caller's value there is invalid input, as is a review date that is not a date.
        private void ExceptionShape()
        {
            if (Nested)
            {
                return;
            }

            bool standard = _after.TryGetMember("exceptionStandard", out JsonTreeNode? standardValue) && standardValue.Kind != JsonTreeKind.Null;
            bool reviewOn = _after.TryGetMember("exceptionReviewOn", out JsonTreeNode? reviewValue) && reviewValue.Kind != JsonTreeKind.Null;
            if (!standard && !reviewOn)
            {
                return;
            }

            string type = Text(Column("decisionType"), null) ?? throw OperationStop.Input(ReasonCodes.InputRequired, "decisionType (in input.after or facts.before) is required when a Decision names exceptionStandard or exceptionReviewOn.");
            if (!string.Equals(type, EngineKeys.ExceptionDecisionType, StringComparison.Ordinal))
            {
                throw OperationStop.Input(
                    ReasonCodes.InputMalformed,
                    "input.after names " + (standard ? "exceptionStandard" : "exceptionReviewOn") + ", which only an " + Quote(EngineKeys.ExceptionDecisionType) + " decision records; this one is " + Quote(type) + ".",
                    standard ? "exceptionStandard" : "exceptionReviewOn");
            }

            if (standard && (standardValue!.Kind != JsonTreeKind.String || standardValue.GetString().Trim().Length == 0))
            {
                throw OperationStop.Input(ReasonCodes.InputMalformed, "input.after.exceptionStandard must be the standard the exception departs from, in words.", "exceptionStandard");
            }

            if (reviewOn && (reviewValue!.Kind != JsonTreeKind.String || !DateTime.TryParseExact(reviewValue.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)))
            {
                throw OperationStop.Input(ReasonCodes.InputMalformed, "input.after.exceptionReviewOn must be a date such as 2027-03-01.", "exceptionReviewOn");
            }
        }

        private void RequireActiveKey(JsonTreeNode value, string entityKey, string? prefix, string where)
        {
            if (value.Kind != JsonTreeKind.String || !ConfigurationKey.IsValid(value.GetString()))
            {
                throw OperationStop.Input(ReasonCodes.InputMalformed, where + " must be a configuration key.");
            }

            string key = value.GetString();
            if (SnapshotRead.FindActive(_snapshot, entityKey, (prefix ?? string.Empty) + key) == null)
            {
                throw OperationStop.Input(ReasonCodes.InputUnknownKey, where + " is \"" + key + "\", which is not an active " + SnapshotRead.LogicalName(entityKey) + " in the configuration in force.", key);
            }
        }

        // ---- Helpers ----

        private void Skip(string message, string? reference)
        {
            Warn(ReasonCodes.SubjectChangeSkipped, message, reference);
        }

        private static string RowIdOf(JsonTreeNode row)
        {
            return Text(row, "id")!;
        }

        // The later of two dates (yyyy-MM-dd), the second optional.
        private static string Later(string today, string? date)
        {
            return date != null && string.CompareOrdinal(date, today) > 0 ? date : today;
        }

        private string? ActiveStage(string? key)
        {
            return key != null && SnapshotRead.FindActive(_snapshot, "lifecycle-stage", key) != null ? key : null;
        }

        private int? StageRank(string? key)
        {
            SnapshotRow? row = key == null ? null : SnapshotRead.Find(_snapshot, "lifecycle-stage", key);
            return row == null ? null : SnapshotRead.Int(row, "lifecycle-stage", "stageRank");
        }

        // The first active stage ranked after the After Stage of the gate whose decision type this is.
        private string? StageAfterGate(string decisionType)
        {
            SnapshotRow? gate = SnapshotRead.ActiveRows(_snapshot, "gate")
                .Where(g => string.Equals(SnapshotRead.Text(g, "gateDecisionTypeKey"), decisionType, StringComparison.Ordinal))
                .OrderBy(g => g.KeyPath, StringComparer.Ordinal)
                .FirstOrDefault();
            int? after = gate == null ? null : StageRank(SnapshotRead.Text(gate, "afterStageKey"));
            if (!after.HasValue)
            {
                return null;
            }

            return SnapshotRead.ActiveRows(_snapshot, "lifecycle-stage")
                .Select(r => (Key: r.KeyPath, Rank: SnapshotRead.Int(r, "lifecycle-stage", "stageRank")))
                .Where(r => r.Rank.HasValue && r.Rank.Value > after.Value)
                .OrderBy(r => r.Rank!.Value)
                .ThenBy(r => r.Key, StringComparer.Ordinal)
                .Select(r => r.Key)
                .FirstOrDefault();
        }

        private string FirstStage()
        {
            return SnapshotRead.ActiveRows(_snapshot, "lifecycle-stage")
                .OrderBy(r => SnapshotRead.Int(r, "lifecycle-stage", "stageRank") ?? int.MaxValue)
                .ThenBy(r => r.KeyPath, StringComparer.Ordinal)
                .Select(r => r.KeyPath)
                .FirstOrDefault()
                ?? throw OperationStop.Broken(ReasonCodes.StageMeaningCoverage, "The configuration in force has no active Lifecycle Stage, so a new application cannot start.", "Lifecycle Stage", null);
        }
    }
}
