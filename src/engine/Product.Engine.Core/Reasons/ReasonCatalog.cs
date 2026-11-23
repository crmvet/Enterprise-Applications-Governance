using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Product.Engine.Core.Contract;

namespace Product.Engine.Core.Reasons;

// The reason code catalog (document 25, sections 5 and 7). Published with each release.
// Every allow, flag and refuse outcome the operations produce has a stable code here.
// Codes named in document 25 carry its severity and rule IDs. Codes it does not name yet are marked proposed.
public static class ReasonCatalog
{
    private const ReasonSeverity Info = ReasonSeverity.Info;
    private const ReasonSeverity Warning = ReasonSeverity.Warning;
    private const ReasonSeverity Error = ReasonSeverity.Error;

    private const ReasonCategories Outcome = ReasonCategories.Outcome;
    private const ReasonCategories Validation = ReasonCategories.Validation;
    private const ReasonCategories InvalidInput = ReasonCategories.InvalidInput;
    private const ReasonCategories RefusedRule = ReasonCategories.RefusedRule;
    private const ReasonCategories RefusedRole = ReasonCategories.RefusedRole;
    private const ReasonCategories NotFound = ReasonCategories.NotFound;
    private const ReasonCategories Conflict = ReasonCategories.Conflict;
    private const ReasonCategories Unavailable = ReasonCategories.Unavailable;

    // Every entry, sorted by code (ordinal).
    public static readonly IReadOnlyList<ReasonCodeInfo> All = Build();

    private static readonly Dictionary<string, ReasonCodeInfo> ByCode = All.ToDictionary(info => info.Code, StringComparer.Ordinal);

    public static bool IsKnown(string? code)
    {
        return code != null && ByCode.ContainsKey(code);
    }

    public static bool TryGet(string? code, [NotNullWhen(true)] out ReasonCodeInfo? info)
    {
        if (code != null && ByCode.TryGetValue(code, out ReasonCodeInfo? found))
        {
            info = found;
            return true;
        }

        info = null;
        return false;
    }

    public static ReasonCodeInfo Get(string code)
    {
        if (TryGet(code, out ReasonCodeInfo? info))
        {
            return info;
        }

        throw new ArgumentException("The reason code is not in the catalog: " + code, nameof(code));
    }

    // Every entry that maps to a guard or validation rule ID, such as "G-06" or "V28".
    public static IReadOnlyList<ReasonCodeInfo> ForRule(string ruleId)
    {
        return All.Where(info => info.RuleIds.Contains(ruleId, StringComparer.Ordinal)).ToArray();
    }

    // A reason with the catalog's default severity and message.
    public static Reason Create(string code, string? entity = null, string? reference = null)
    {
        ReasonCodeInfo info = Get(code);
        return new Reason(info.Code, info.DefaultSeverity, info.Message, entity, reference);
    }

    // A reason with the catalog's message and a severity the operation chose.
    public static Reason Create(string code, ReasonSeverity severity, string? entity = null, string? reference = null)
    {
        ReasonCodeInfo info = Get(code);
        return new Reason(info.Code, severity, info.Message, entity, reference);
    }

    private static ReasonCodeInfo Named(string code, ReasonSeverity severity, ReasonCategories categories, string source, string ruleIds, string message, int? workingDefaultQuestion = null)
    {
        return new ReasonCodeInfo(code, severity, categories, source, SplitRuleIds(ruleIds), false, message, workingDefaultQuestion);
    }

    private static ReasonCodeInfo Proposed(string code, ReasonSeverity severity, ReasonCategories categories, string source, string message, int? workingDefaultQuestion = null)
    {
        return new ReasonCodeInfo(code, severity, categories, source, null, true, message, workingDefaultQuestion);
    }

    // A proposed code (not named in document 25) that a documented rule reports, so fixtures and the error map see its rule.
    private static ReasonCodeInfo ProposedForRule(string code, ReasonSeverity severity, ReasonCategories categories, string source, string ruleIds, string message)
    {
        return new ReasonCodeInfo(code, severity, categories, source, SplitRuleIds(ruleIds), true, message, null);
    }

    private static string[] SplitRuleIds(string ruleIds)
    {
        return ruleIds.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(id => id.Trim())
            .Where(id => id.Length > 0)
            .ToArray();
    }

    private static IReadOnlyList<ReasonCodeInfo> Build()
    {
        var entries = new List<ReasonCodeInfo>
        {
            // ---- OP-03 approval status (document 25, section 5.5), and G-06 ----
            Named(ReasonCodes.ApprovalNotApproved, Info, Outcome, "OP-03", "",
                "The answer is not an approval, so it does not count."),
            Named(ReasonCodes.ApprovalAiActorNotCounted, Warning, Outcome, "OP-03", "",
                "The answer came from an AI client or an agent identity, and AI never approves."),
            Named(ReasonCodes.ApprovalWorkloadNotCounted, Warning, Outcome, "OP-03", "",
                "The answer came from a pipeline, the approval runner or the engine, so it does not count."),
            Named(ReasonCodes.ApprovalNonUserPrincipal, Warning, Outcome, "OP-03", "",
                "Only a user principal can approve, so this answer does not count."),
            Named(ReasonCodes.ApprovalNotRoleHolder, Warning, Outcome, "OP-03", "",
                "The responder did not hold the required role or owner role when answering."),
            Named(ReasonCodes.ApprovalReasonMissing, Warning, Outcome, "OP-03", "",
                "The rule needs a typed reason and the answer has none."),
            Named(ReasonCodes.ApprovalExpired, Info, Outcome, "OP-03", "",
                "The answer came after the approval request expired."),
            Named(ReasonCodes.ApprovalConsultationNotCounted, Info, Outcome, "OP-03", "",
                "A consultation answer is a recommendation only and never counts."),
            Named(ReasonCodes.ApprovalSeparationRefused, Warning, Outcome, "OP-03", "",
                "A separation rule refuses this responder for this decision."),
            Named(ReasonCodes.ApprovalSeparationExceptionMissing, Warning, Outcome, "OP-03", "",
                "This responder counts only with an accepted exception decision linked to the decision."),
            Named(ReasonCodes.ApprovalSeparationFlagged, Warning, Outcome, "OP-03, OP-04", "",
                "The approval counts, but a separation rule flags the responder."),
            Named(ReasonCodes.ApprovalOutcomeWriter, Error, RefusedRole, "OP-08", "G-06",
                "Only the approval runner can record an approval outcome."),
            Proposed(ReasonCodes.ApprovalNoRequirement, Info, Outcome, "OP-03",
                "The answer belongs to no current requirement of the decision, so it does not count."),
            Proposed(ReasonCodes.ApprovalSequenceWaiting, Info, Outcome, "OP-03",
                "A sequential requirement counts only after the earlier requirements are satisfied."),
            Proposed(ReasonCodes.ApprovalResponderAlreadyCounted, Warning, Outcome, "OP-03",
                "The responder's earlier approval already counts for this requirement, and one person counts once."),

            // ---- OP-02 required approvals (section 5.4) ----
            Named(ReasonCodes.RuleGroupAmbiguous, Error, Validation | Unavailable, "OP-02, OP-09", "V26",
                "Two rule groups match this scope with the same specificity, so no single group wins."),
            Named(ReasonCodes.ApprovalRuleZeroCount, Error, Validation | Unavailable, "OP-02, OP-09", "V28",
                "A minimum count of 0 is not allowed, so every approval rule needs at least one approval."),
            Named(ReasonCodes.DecisionRightAccountable, Error, Validation | Unavailable, "OP-02, OP-09", "V22",
                "The decision right group must hold exactly one accountable holder."),
            Named(ReasonCodes.StandardModelDoesNotQualify, Error, Outcome | RefusedRule, "OP-02, OP-12", "",
                "The change fits no approved standard change model, so it needs another change type."),
            // WORKING DEFAULT (open design question 8): engine acceptance only for the locked list.
            Proposed(ReasonCodes.DecisionEngineAccepted, Info, Outcome, "OP-02",
                "The engine accepts this decision without a card because it fits the locked engine acceptance list.",
                workingDefaultQuestion: 8),

            // ---- OP-01 tier (section 5.3) ----
            Proposed(ReasonCodes.TierIncomplete, Info, Outcome, "OP-01",
                "Some applicable tier questions have no answer yet."),
            Proposed(ReasonCodes.TierProposedLower, Warning, Outcome, "OP-01",
                "The proposed tier is lower than the answers imply, so lowering it needs a tier decision with a reason."),
            Proposed(ReasonCodes.TierNoApplicableQuestions, Error, Unavailable, "OP-01",
                "No active tier question applies to the application kind, so no tier can be computed."),

            // ---- OP-04 gate decision (section 5.6) ----
            Named(ReasonCodes.GatePolicyMissing, Error, Unavailable, "OP-04", "",
                "No gate policy row covers this tier, change type and target stage, so the configuration is broken."),
            Named(ReasonCodes.EngineUnavailable, Error, Unavailable, "OP-04, section 7", "",
                "The engine cannot answer now, so apply the cached fail mode or retry later."),
            Proposed(ReasonCodes.GateOutsideWindow, Warning, Outcome, "OP-04",
                "The deployment is outside the change window."),
            Proposed(ReasonCodes.GateOtherPath, Warning, Outcome, "OP-04",
                "The deployment arrived by a route that the change path of the target does not allow."),
            Proposed(ReasonCodes.GateMissingApproval, Warning, Outcome, "OP-04",
                "The change needs authorization and the product does not know that it was authorized."),
            // WORKING DEFAULT (open design question 7): AI-only code approvals never count; the shipped default flags them.
            Proposed(ReasonCodes.GateAiOnlyCodeApproval, Warning, Outcome, "OP-04",
                "The only code approval came from an AI, and that never counts as authorization.",
                workingDefaultQuestion: 7),
            Proposed(ReasonCodes.GateEmergencyException, Warning, Outcome, "OP-04",
                "The emergency change may go ahead, and its authorization must follow by the due date."),

            // ---- OP-05 change window (section 5.7) ----
            Proposed(ReasonCodes.WindowOutside, Info, Outcome, "OP-05",
                "The moment is outside the change window."),
            Proposed(ReasonCodes.WindowNone, Info, Outcome, "OP-05",
                "The change request has no window."),

            // ---- OP-06 documentation completeness (section 5.8) ----
            Proposed(ReasonCodes.DocumentationMissing, Warning, Outcome, "OP-06",
                "A required document has no link."),
            Proposed(ReasonCodes.DocumentationDraft, Warning, Outcome, "OP-06",
                "A required document is still a draft."),
            Proposed(ReasonCodes.DocumentationStale, Warning, Outcome, "OP-06",
                "A required document is past its freshness date."),

            // ---- OP-07 gate readiness (section 5.9), and G-14 ----
            Named(ReasonCodes.GateNotReady, Error, Outcome | RefusedRule, "OP-07, OP-08", "G-14",
                "The gate is not ready, so the gate decision cannot be accepted."),
            Proposed(ReasonCodes.GateRequirementDecisionMissing, Warning, Outcome, "OP-07",
                "The gate needs an accepted decision of the required type."),
            Proposed(ReasonCodes.GateRequirementDocumentNotCurrent, Warning, Outcome, "OP-07",
                "The gate needs a current document of the required type."),
            Proposed(ReasonCodes.GateRequirementReviewNotDone, Warning, Outcome, "OP-07",
                "The gate needs a done review of the required type."),
            Proposed(ReasonCodes.GateRequirementOwnerMissing, Warning, Outcome, "OP-07",
                "The gate needs an active assignment of the required owner role."),

            // ---- OP-08 write guard, G-01 to G-17 (section 5.10) ----
            Named(ReasonCodes.ConfigActivationOnly, Error, RefusedRule, "OP-08", "G-01",
                "Configuration rows change only inside activation, so propose a configuration change instead."),
            Named(ReasonCodes.ConfigPresetReadOnly, Error, RefusedRule, "OP-08", "G-02",
                "Rows of a preset never change."),
            Named(ReasonCodes.ConfigKeyImmutable, Error, RefusedRule, "OP-08", "G-03",
                "The key of a configuration row never changes."),
            Named(ReasonCodes.ConfigInUse, Error, RefusedRule, "OP-08", "G-04",
                "This configuration row is in use, so retire it instead of deleting it."),
            Named(ReasonCodes.DecisionFinal, Error, RefusedRule, "OP-08", "G-05",
                "This decision is final and cannot change."),
            Named(ReasonCodes.AuditAppendOnly, Error, RefusedRule | Conflict, "OP-08, section 7", "G-07",
                "Audit entries are never updated or deleted."),
            Named(ReasonCodes.ConfigVersionImmutable, Error, RefusedRule, "OP-08", "G-07",
                "A configuration set version never changes once created."),
            Named(ReasonCodes.AiStatusNotAllowed, Error, RefusedRule, "OP-08", "G-08",
                "An AI actor cannot set this status, so a person must confirm it."),
            Named(ReasonCodes.EngineColumnOverwritten, Info, Outcome, "OP-08", "G-09",
                "The engine replaced a value in a column that only the engine sets."),
            Named(ReasonCodes.StatusUnknown, Error, RefusedRule, "OP-08", "G-10",
                "The status is not a configured status for this entity."),
            Named(ReasonCodes.LinkWrongCategory, Error, RefusedRule, "OP-08", "G-11",
                "The link kind belongs to another link category."),
            Named(ReasonCodes.LinkUrlPattern, Error, RefusedRule, "OP-08", "G-11",
                "The URL does not match the pattern of the link kind."),
            Named(ReasonCodes.RoleBindingNotGroup, Error, RefusedRule | Validation, "OP-08, OP-09", "G-12, V35",
                "A role group binding must point to a group."),
            Named(ReasonCodes.RegisterDecisionRequired, Error, RefusedRule, "OP-08", "G-13",
                "Tier and stage change only as an effect of an accepted decision."),
            Named(ReasonCodes.RequestIdempotencyKeyReused, Error, Conflict, "OP-08, section 8", "G-15",
                "This idempotency key was used before with a different request."),
            Named(ReasonCodes.ConfigNotConfigurationAdmin, Error, RefusedRole, "OP-08", "G-16",
                "Only a person whose role can change configuration can work on a configuration draft."),
            Named(ReasonCodes.ConfigDraftFrozen, Error, RefusedRule, "OP-08", "G-16",
                "A submitted configuration draft is frozen."),
            // WORKING DEFAULT (open design question 8): only the accept-decision effect sets accepted.
            Named(ReasonCodes.DecisionAcceptByEngineOnly, Error, RefusedRule, "OP-08", "G-17",
                "Only the engine accepts a decision, after the approvals are satisfied.",
                workingDefaultQuestion: 8),

            // ---- OP-08 write guard: proposed codes ----
            // G-09 replaces caller values in columns the engine computes in the same write. A column the engine sets
            // only as an effect of an accepted decision is refused instead (document 25, section 5.10 Tests; document 26,
            // section 6.1 and RA-12).
            Proposed(ReasonCodes.EngineEffectOnly, Error, RefusedRule, "OP-08",
                "This row or column changes only as an engine effect, never from a request."),
            Proposed(ReasonCodes.AiForcedDraft, Info, Outcome, "OP-08",
                "An AI actor created this row, so the engine set it to draft or proposed and marked it AI created."),
            Proposed(ReasonCodes.RecordDeleteNotAllowed, Error, RefusedRule, "OP-08",
                "This row is never deleted, so withdraw, cancel, archive or end it instead."),
            // Document 22, section 2.3, class A: an Evidence Link is inserted once and never updated.
            Proposed(ReasonCodes.RecordAppendOnly, Error, RefusedRule, "OP-08",
                "This row is written once and never changes, so add a new row instead."),
            // Document 22, section 2.3, class F, and document 26, RA-05: a row is frozen once final, for every writer, the
            // engine included. Only the documented transitions remain. O09 and O10 keep G-05 and G-06, C46 keeps G-16.
            Proposed(ReasonCodes.RecordFinal, Error, RefusedRule, "OP-08",
                "This row reached its final state, so it never changes again, and a new or correcting row is added instead."),
            // Document 26, RA-06: a caller whose subject is an agent identity or an agent user is refused for every write.
            Proposed(ReasonCodes.ActorAgentWriteRefused, Error, RefusedRole, "OP-08",
                "An agent identity or agent user may not write, so a person must make this change."),
            // Document 26, section 6.5 (SR-09): the forbidden-url-patterns setting.
            Proposed(ReasonCodes.LinkForbiddenUrl, Error, RefusedRule, "OP-08",
                "The URL carries a part that may hold a secret, such as a signature or a token."),
            // WORKING DEFAULT (open design question 1): the platform admin role performs an erasure.
            Proposed(ReasonCodes.PrincipalErasureNotAllowed, Error, RefusedRole, "OP-08",
                "Only a person holding the platform admin role can erase a principal.",
                workingDefaultQuestion: 1),
            Proposed(ReasonCodes.PrincipalErasureFinal, Error, RefusedRule, "OP-08",
                "An erased principal stays erased, so its label and erasure flag cannot be restored."),
            // Document 22, section 8.1, and document 26, section 5.3: withdrawn by the proposer or the decider.
            Proposed(ReasonCodes.DecisionWithdrawNotAllowed, Error, RefusedRole, "OP-08",
                "Only the person who proposed the decision, or its decider, can withdraw it."),
            // G-06 "only once", for the approval runner itself: a conflict with the stored answer, not a refusal of the writer.
            ProposedForRule(ReasonCodes.ApprovalOutcomeFinal, Error, Conflict, "OP-08", "G-06",
                "The Approval Record already holds a final outcome, and an answer is written once and never changes."),

            // ---- Engine runtime ----
            // Document 26, RA-07: app-only tokens are accepted only from principals in a current Workload Grant (C42).
            // WORKING DEFAULT (open design question 12): workload identities hold their role directly through a grant.
            Proposed(ReasonCodes.ActorWorkloadGrantMissing, Error, RefusedRole, "Runtime",
                "An app-only caller is accepted only with a current workload grant, and only for that grant's workload role.",
                workingDefaultQuestion: 12),
            Proposed(ReasonCodes.ApprovalRefreshNotAllowed, Error, RefusedRole, "Runtime",
                "Only the approval runner, holding a current approval writer grant, runs the approval refresh."),

            // ---- Activation and the bootstrap of version 1 (document 25, section 6.4; document 21, section 4.8) ----
            Proposed(ReasonCodes.ActivationDiscarded, Warning, Outcome, "Activation",
                "The accepted draft could not activate, so the engine discarded it and created no version."),
            Proposed(ReasonCodes.ActivationAlreadyActivated, Info, Outcome, "Activation",
                "The draft is already activated, so activation returns its version and changes nothing."),
            Proposed(ReasonCodes.BootstrapVersionExists, Error, RefusedRule, "Bootstrap",
                "A configuration version already exists, so version 1 cannot be bootstrapped again."),
            Proposed(ReasonCodes.BootstrapNotAllowed, Error, RefusedRole, "Bootstrap",
                "Only a person signed in as a user, with the platform's install rights, can bootstrap version 1."),

            // ---- OP-09 validation, V01 to V44 (section 5.11) ----
            // A code that also names an operation is reported there at run time as an unavailable answer: the snapshot
            // in force is broken, and the engine refuses and reports it instead of falling back (rule 10).
            Named(ReasonCodes.ShapeSchema, Error, Validation | Unavailable, "OP-09, OP-02, OP-03, OP-04", "V01",
                "The bundle or patch does not match the JSON Schema."),
            Named(ReasonCodes.KeyFormat, Error, Validation, "OP-09", "V02",
                "A key is not lowercase letters, digits and hyphens, 1 to 64 characters, starting with a letter."),
            Named(ReasonCodes.KeyDuplicate, Error, Validation, "OP-09", "V03",
                "A key is used twice in the same entity or under the same parent."),
            Named(ReasonCodes.KeyReused, Error, Validation, "OP-09", "V04",
                "A new row takes the key of a retired row."),
            Named(ReasonCodes.KeyImmutable, Error, Validation, "OP-09", "V05",
                "A patch changes the key of a row."),
            Named(ReasonCodes.RefUnresolved, Error, Validation | Unavailable, "OP-09, OP-06", "V06",
                "A key reference does not resolve to a row of the named entity."),
            Named(ReasonCodes.RefInactive, Error, Validation, "OP-09", "V07",
                "An active row refers to a retired row."),
            Named(ReasonCodes.RefInUse, Error, Validation, "OP-09", "V08",
                "A row in use is removed instead of retired."),
            Named(ReasonCodes.RefWildcard, Error, Validation, "OP-09", "V09",
                "A wildcard is used where it is not allowed, or two equally explicit rows give the same combination."),
            Named(ReasonCodes.RefExactlyOne, Error, Validation | Unavailable, "OP-09, OP-02, OP-03", "V10",
                "A field that needs exactly one value has none or more than one."),
            Named(ReasonCodes.TierNoneActive, Error, Validation, "OP-09", "V11",
                "The set has no active tier."),
            Named(ReasonCodes.TierRankDuplicate, Error, Validation | Unavailable, "OP-09, OP-01", "V12",
                "Two tiers share a rank, or a rank is not a whole number."),
            Named(ReasonCodes.TierQuestionOptions, Error, Validation | Unavailable, "OP-09, OP-01", "V13",
                "An active tier question has fewer than two active options, or implies an inactive tier."),
            Named(ReasonCodes.StageMeaningCoverage, Error, Validation | Unavailable, "OP-09, OP-08", "V14",
                "A lifecycle meaning has no active stage, or two stages share a stage number."),
            Named(ReasonCodes.StageRankDuplicate, Error, Validation, "OP-09", "V14",
                "Two lifecycle stages share a stage rank."),
            Named(ReasonCodes.MeaningMissing, Error, Validation | Unavailable, "OP-09, OP-08", "V15",
                "A status-bearing entity lacks an active status, or a default, for an engine meaning."),
            Named(ReasonCodes.MeaningChangedInUse, Error, Validation, "OP-09", "V16",
                "The engine meaning of a row in use has changed."),
            Named(ReasonCodes.TaxonomyMinimum, Error, Validation, "OP-09", "V17",
                "A required minimum row is missing, such as a change type, a target stage or a pipeline step route."),
            Named(ReasonCodes.DecisionTypeOutcome, Error, Validation, "OP-09", "V18",
                "An active decision type has no active outcome."),
            Named(ReasonCodes.ApprovalRuleTierCoverage, Error, Validation, "OP-09", "V19",
                "A decision type that needs approval has no approval rule at some active tier."),
            Named(ReasonCodes.ApprovalRuleCoverage, Error, Validation, "OP-09", "V20",
                "A decision type that needs approval has no approval rule, or a type without approval has a counting rule."),
            Named(ReasonCodes.ApprovalRuleChangeCoverage, Error, Validation, "OP-09", "V21",
                "A tier, change type and governed target stage has no change authorization rule group."),
            Named(ReasonCodes.GatePolicyCoverage, Error, Validation | Unavailable, "OP-09, OP-04", "V23",
                "A tier, change type and target stage does not resolve to exactly one gate policy."),
            Named(ReasonCodes.GatePolicyFailMode, Error, Validation, "OP-09", "V24",
                "A fail mode is missing at enforce, or is set at a level below enforce."),
            Named(ReasonCodes.GatePolicyEmergencyRule, Error, Validation | Unavailable, "OP-09, OP-04", "V25",
                "An emergency rule is missing where the change type allows authorization after at advise or enforce."),
            Named(ReasonCodes.ApprovalRuleSequence, Error, Validation, "OP-09", "V27",
                "Two sequential rules in one group share a sequence number."),
            Named(ReasonCodes.ApprovalRuleConsultationOnly, Error, Validation | Unavailable, "OP-09, OP-02", "V27",
                "A rule group of a decision type that needs approval holds only consultations."),
            Named(ReasonCodes.ValueRange, Error, Validation | Unavailable, "OP-09, OP-01 to OP-04", "V28",
                "A number is outside its allowed range."),
            Named(ReasonCodes.CadenceCoverage, Warning, Validation, "OP-09", "V29",
                "A review type has a cadence at some active tiers but not at all of them."),
            Named(ReasonCodes.ApprovalRuleWorkloadApprover, Error, Validation, "OP-09", "V30",
                "An approval rule names a workload role or a read-only role."),
            Named(ReasonCodes.DecisionRightAiLetter, Error, Validation, "OP-09", "V31",
                "A supports row names a holder, or an AI construct holds responsible or accountable."),
            Named(ReasonCodes.RoleConfigAdminMissing, Error, Validation, "OP-09", "V32",
                "No role can change configuration, or the configuration change decision type has no rule naming it."),
            Named(ReasonCodes.SettingLockedChanged, Error, Validation | Unavailable, "OP-09, OP-02", "V33",
                "A locked setting has changed."),
            Named(ReasonCodes.SettingAiApprovalLockOff, Error, Validation, "OP-09", "V33",
                "The AI approval lock is off, and it must stay on."),
            Named(ReasonCodes.ConfigProtectedChange, Error, Validation, "OP-09", "V34",
                "A protected change is not approved under the rules of the active version."),
            Named(ReasonCodes.SeparationSharedGroup, Error, Validation, "OP-09", "V36",
                "Two roles that a separation rule keeps apart bind to the same group."),
            Named(ReasonCodes.RoleUnbound, Warning, Validation, "OP-09", "V37",
                "A role that an approval rule uses has no group binding."),
            Named(ReasonCodes.SettingRequired, Error, Validation | Unavailable, "OP-09, OP-02, OP-04", "V38",
                "A required setting is missing, or its value does not match its value type."),
            Named(ReasonCodes.JsonSchema, Error, Validation | Unavailable, "OP-09, OP-12", "V39",
                "A JSON field does not match its sub-schema."),
            Named(ReasonCodes.PatternInvalid, Error, Validation | Unavailable, "OP-09, OP-08", "V40",
                "A pattern does not compile, or a source, hub or template URL does not use https."),
            Named(ReasonCodes.SnapshotSize, Error, Validation, "OP-09", "V41",
                "The snapshot is near or over the size limit of the binding."),
            Named(ReasonCodes.ConfigBaseMoved, Error, Validation, "OP-09", "V42",
                "The base version of the draft is not the active version, so rebase and revalidate it."),
            Named(ReasonCodes.PresetConflict, Error, Validation, "OP-09", "V43",
                "Two overlays that conflict are applied together."),
            Named(ReasonCodes.PresetTenantData, Error, Validation, "OP-09", "V44",
                "A shipped preset holds a secret, a tenant identifier, a role group binding or a portfolio scope."),

            // ---- OP-09 engine checks beyond V01 to V44 ----
            Named(ReasonCodes.SnapshotSchemaUnsupported, Error, Validation | Unavailable, "OP-09, section 6.2", "",
                "The snapshot schema major version is not one this engine reads."),
            Named(ReasonCodes.RoleConfigAdmin, Error, Validation, "OP-09", "",
                "Can Change Configuration is set on a role other than the configuration admin role."),
            Named(ReasonCodes.StandardModelType, Error, Validation | Outcome, "OP-09, OP-12", "",
                "A standard change model belongs to an emergency change type."),
            Named(ReasonCodes.StandardModelConditionKind, Error, Validation | Unavailable, "OP-09, OP-12", "",
                "A standard change model condition uses an unknown condition kind."),
            Named(ReasonCodes.VocabularySource, Warning, Validation, "OP-09", "",
                "A vocabulary mapping points to a row that does not exist."),

            // ---- OP-10 impact (section 5.12) ----
            Proposed(ReasonCodes.ImpactCycleDetected, Info, Outcome, "OP-10",
                "The dependency walk met an application twice."),
            Proposed(ReasonCodes.ImpactDepthCapped, Info, Outcome, "OP-10",
                "The dependency walk stopped at the maximum depth."),

            // ---- OP-11 due dates and reminders (section 5.12) ----
            Proposed(ReasonCodes.ReminderNoRule, Warning, Outcome, "OP-11",
                "No active reminder rule exists for this subject kind, so nobody is reminded."),
            Proposed(ReasonCodes.ReviewNoCadence, Info, Outcome, "OP-11",
                "The review type has no cadence at this tier, so no next due date follows from it."),

            // ---- OP-12 standard change (section 5.13) ----
            Named(ReasonCodes.StandardModelNotApproved, Warning, Outcome, "OP-12", "",
                "The approving decision of the model is not accepted, so the model never qualifies."),
            Proposed(ReasonCodes.StandardModelConditionNotMet, Info, Outcome, "OP-12",
                "A condition of the standard change model is not met."),
            Proposed(ReasonCodes.StandardModelConditionAsReported, Info, Outcome, "OP-12",
                "A stated condition counts as reported, because the engine cannot see the work itself."),
            Proposed(ReasonCodes.StandardModelQualifies, Info, Outcome, "OP-12",
                "The change fits an approved standard change model."),
            Proposed(ReasonCodes.StandardModelNoCandidate, Info, Outcome, "OP-12",
                "No active standard change model of this change type can be tried, so the change is not standard."),

            // ---- OP-13 gap check comparison (section 5.14) ----
            Proposed(ReasonCodes.GapCheckItemIgnored, Info, Outcome, "OP-13",
                "The item is outside the estates or component kinds this run covers, so it was ignored."),
            Proposed(ReasonCodes.GapCheckAsReported, Info, Outcome, "OP-13",
                "The inventory list came from outside the product, so its evidence level is as reported."),
            Proposed(ReasonCodes.GapCheckPartialRun, Info, Outcome, "OP-13",
                "Vanished components are found only when the final page of a run arrives."),

            // ---- OP-14 linked-mode authorization (section 5.15) ----
            Named(ReasonCodes.AuthorizationNoEvidence, Info, Outcome, "OP-14", "",
                "An authorization fact counts only with an evidence link."),
            Named(ReasonCodes.AuthorizationAiNotCounted, Warning, Outcome, "OP-14", "",
                "An authorization fact from an AI client or an agent identity never counts."),
            Named(ReasonCodes.AuthorizationNotAttester, Warning, Outcome, "OP-14", "",
                "The attester did not hold an attester role, so the attestation does not count."),
            Proposed(ReasonCodes.AuthorizationWrongSource, Warning, Outcome, "OP-14",
                "An ITSM step fact counts only from a pipeline, and an attestation only from a person on a confirmation card."),
            Proposed(ReasonCodes.AuthorizationNumberOnly, Warning, Outcome, "OP-14",
                "A recorded external change number alone never authorizes a change."),
            Proposed(ReasonCodes.AuthorizationRevoked, Info, Outcome, "OP-14",
                "A later fact says the change is not authorized or is cancelled."),
            // Rule 5 reads the latest fact; a fact dated after the evaluation moment cannot have been reported yet, so it
            // never authorizes. A removal dated later still removes: the engine never guesses in favor.
            Proposed(ReasonCodes.AuthorizationReportedLater, Warning, Outcome, "OP-14",
                "An authorization fact dated after the moment of the evaluation never authorizes the change."),

            // ---- Error model (section 7) ----
            Proposed(ReasonCodes.InputRequired, Error, InvalidInput, "section 7",
                "A required value is missing."),
            Proposed(ReasonCodes.InputMalformed, Error, InvalidInput, "section 7",
                "A value does not have the expected shape."),
            Proposed(ReasonCodes.InputUnknownKey, Error, InvalidInput, "section 7",
                "A configuration key does not exist in the configuration in force."),
            // OP-08 refuses a column name the logical model does not list for the entity, in its exact lowerCamelCase form.
            Proposed(ReasonCodes.InputUnknownColumn, Error, InvalidInput, "OP-08, section 7",
                "A write names a column that is not in the entity's column list in the logical model."),
            Proposed(ReasonCodes.InputContractValueUnknown, Error, InvalidInput, "section 7",
                "A value is not one of the fixed meanings of its engine contract list."),
            Proposed(ReasonCodes.InputJsonInvalid, Error, InvalidInput, "section 7, section 9",
                "The JSON text is not valid for canonical hashing."),
            Named(ReasonCodes.RefNotFound, Error, NotFound, "section 7", "",
                "A referenced row does not exist."),
            Proposed(ReasonCodes.SnapshotHashMismatch, Error, Unavailable, "section 6.3, section 7",
                "The snapshot content does not match its stored hash."),
        };

        ReasonCodeInfo[] sorted = entries.OrderBy(info => info.Code, StringComparer.Ordinal).ToArray();
        for (int i = 1; i < sorted.Length; i++)
        {
            if (string.Equals(sorted[i - 1].Code, sorted[i].Code, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The reason catalog repeats the code " + sorted[i].Code + ".");
            }
        }

        return new ReadOnlyCollection<ReasonCodeInfo>(sorted);
    }
}
