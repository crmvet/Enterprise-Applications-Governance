namespace Product.Engine.Core.Reasons;

// Every stable reason code the engine returns (document 25, sections 5 and 7).
// Codes are lowercase and dot-separated. A code is never reused once retired. Labels may be translated, codes never change.
// ReasonCatalog holds the severity, category, source and plain message of each code. A test keeps the two in step.
// Codes that document 25 does not name yet are marked proposed in the catalog.
public static class ReasonCodes
{
    // ---- OP-03 approval status, and G-06 ----
    public const string ApprovalNotApproved = "approval.not-approved";
    public const string ApprovalAiActorNotCounted = "approval.ai-actor-not-counted";
    public const string ApprovalWorkloadNotCounted = "approval.workload-not-counted";
    public const string ApprovalNonUserPrincipal = "approval.non-user-principal";
    public const string ApprovalNotRoleHolder = "approval.not-role-holder";
    public const string ApprovalReasonMissing = "approval.reason-missing";
    public const string ApprovalExpired = "approval.expired";
    public const string ApprovalConsultationNotCounted = "approval.consultation-not-counted";
    public const string ApprovalSeparationRefused = "approval.separation-refused";
    public const string ApprovalSeparationExceptionMissing = "approval.separation-exception-missing";
    public const string ApprovalSeparationFlagged = "approval.separation-flagged";
    public const string ApprovalOutcomeWriter = "approval.outcome-writer";
    public const string ApprovalNoRequirement = "approval.no-requirement";
    public const string ApprovalSequenceWaiting = "approval.sequence-waiting";
    public const string ApprovalResponderAlreadyCounted = "approval.responder-already-counted";

    // ---- OP-02 required approvals ----
    public const string RuleGroupAmbiguous = "rule-group.ambiguous";
    public const string ApprovalRuleZeroCount = "approval-rule.zero-count";
    public const string DecisionRightAccountable = "decision-right.accountable";
    public const string StandardModelDoesNotQualify = "standard-model.does-not-qualify";

    // WORKING DEFAULT (open design question 8): engine acceptance only for the locked list.
    public const string DecisionEngineAccepted = "decision.engine-accepted";

    // ---- OP-01 tier ----
    public const string TierIncomplete = "tier.incomplete";
    public const string TierProposedLower = "tier.proposed-lower";
    public const string TierNoApplicableQuestions = "tier.no-applicable-questions";

    // ---- OP-04 gate decision ----
    public const string GatePolicyMissing = "gate-policy.missing";
    public const string EngineUnavailable = "engine.unavailable";
    public const string GateOutsideWindow = "gate.outside-window";
    public const string GateOtherPath = "gate.other-path";
    public const string GateMissingApproval = "gate.missing-approval";

    // WORKING DEFAULT (open design question 7): an AI-only code approval never counts and is flagged by default.
    public const string GateAiOnlyCodeApproval = "gate.ai-only-code-approval";
    public const string GateEmergencyException = "gate.emergency-exception";

    // ---- OP-05 change window ----
    public const string WindowOutside = "window.outside";
    public const string WindowNone = "window.none";

    // ---- OP-06 documentation completeness ----
    public const string DocumentationMissing = "documentation.missing";
    public const string DocumentationDraft = "documentation.draft";
    public const string DocumentationStale = "documentation.stale";

    // ---- OP-07 gate readiness, and G-14 ----
    public const string GateNotReady = "gate.not-ready";
    public const string GateRequirementDecisionMissing = "gate.requirement-decision-missing";
    public const string GateRequirementDocumentNotCurrent = "gate.requirement-document-not-current";
    public const string GateRequirementReviewNotDone = "gate.requirement-review-not-done";
    public const string GateRequirementOwnerMissing = "gate.requirement-owner-missing";

    // ---- OP-08 write guard, G-01 to G-17 ----
    public const string ConfigActivationOnly = "config.activation-only";
    public const string ConfigPresetReadOnly = "config.preset-read-only";
    public const string ConfigKeyImmutable = "config.key-immutable";
    public const string ConfigInUse = "config.in-use";
    public const string DecisionFinal = "decision.final";
    public const string AuditAppendOnly = "audit.append-only";
    public const string ConfigVersionImmutable = "config.version-immutable";
    public const string AiStatusNotAllowed = "ai.status-not-allowed";
    public const string EngineColumnOverwritten = "engine.column-overwritten";
    public const string StatusUnknown = "status.unknown";
    public const string LinkWrongCategory = "link.wrong-category";
    public const string LinkUrlPattern = "link.url-pattern";
    public const string RoleBindingNotGroup = "role.binding-not-group";
    public const string RegisterDecisionRequired = "register.decision-required";
    public const string RequestIdempotencyKeyReused = "request.idempotency-key-reused";
    public const string ConfigNotConfigurationAdmin = "config.not-configuration-admin";
    public const string ConfigDraftFrozen = "config.draft-frozen";

    // WORKING DEFAULT (open design question 8): no caller sets accepted. Only the accept-decision effect does.
    public const string DecisionAcceptByEngineOnly = "decision.accept-by-engine-only";

    // ---- OP-08 write guard: proposed codes ----
    public const string EngineEffectOnly = "engine.effect-only";
    public const string AiForcedDraft = "ai.forced-draft";
    public const string RecordDeleteNotAllowed = "record.delete-not-allowed";
    public const string RecordAppendOnly = "record.append-only";

    // Document 22, section 2.3, class F: a row frozen once final (RA-05 names no code).
    public const string RecordFinal = "record.final";
    public const string ActorAgentWriteRefused = "actor.agent-write-refused";
    public const string LinkForbiddenUrl = "link.forbidden-url";
    public const string PrincipalErasureNotAllowed = "principal.erasure-not-allowed";
    public const string PrincipalErasureFinal = "principal.erasure-final";

    // G-06 "only once": the approval runner's different second answer for a record that already holds a final outcome.
    // A conflict, not a refusal of who wrote it.
    public const string ApprovalOutcomeFinal = "approval.outcome-final";

    // Document 22, section 8.1: a proposed decision is withdrawn only by its proposer or its decider.
    public const string DecisionWithdrawNotAllowed = "decision.withdraw-not-allowed";

    // ---- Engine runtime: proposed codes ----
    // WORKING DEFAULT (open design question 12): document 26, RA-07. An app-only caller counts only with a current
    // Workload Grant, and only for that grant's workload role.
    public const string ActorWorkloadGrantMissing = "actor.workload-grant-missing";

    // The approval refresh (expiry, restarts and cancellations) is the approval runner's call only.
    public const string ApprovalRefreshNotAllowed = "approval.refresh-not-allowed";

    // ---- Activation and the bootstrap of version 1: proposed codes ----
    public const string ActivationDiscarded = "activation.discarded";
    public const string ActivationAlreadyActivated = "activation.already-activated";
    public const string BootstrapVersionExists = "bootstrap.version-exists";
    public const string BootstrapNotAllowed = "bootstrap.not-allowed";

    // ---- OP-09 validation, V01 to V44 ----
    public const string ShapeSchema = "shape.schema";
    public const string KeyFormat = "key.format";
    public const string KeyDuplicate = "key.duplicate";
    public const string KeyReused = "key.reused";
    public const string KeyImmutable = "key.immutable";
    public const string RefUnresolved = "ref.unresolved";
    public const string RefInactive = "ref.inactive";
    public const string RefInUse = "ref.in-use";
    public const string RefWildcard = "ref.wildcard";
    public const string RefExactlyOne = "ref.exactly-one";
    public const string TierNoneActive = "tier.none-active";
    public const string TierRankDuplicate = "tier.rank-duplicate";
    public const string TierQuestionOptions = "tier.question-options";
    public const string StageMeaningCoverage = "stage.meaning-coverage";
    public const string StageRankDuplicate = "stage.rank-duplicate";
    public const string MeaningMissing = "meaning.missing";
    public const string MeaningChangedInUse = "meaning.changed-in-use";
    public const string TaxonomyMinimum = "taxonomy.minimum";
    public const string DecisionTypeOutcome = "decision-type.outcome";
    public const string ApprovalRuleTierCoverage = "approval-rule.tier-coverage";
    public const string ApprovalRuleCoverage = "approval-rule.coverage";
    public const string ApprovalRuleChangeCoverage = "approval-rule.change-coverage";
    public const string GatePolicyCoverage = "gate-policy.coverage";
    public const string GatePolicyFailMode = "gate-policy.fail-mode";
    public const string GatePolicyEmergencyRule = "gate-policy.emergency-rule";
    public const string ApprovalRuleSequence = "approval-rule.sequence";
    public const string ApprovalRuleConsultationOnly = "approval-rule.consultation-only";
    public const string ValueRange = "value.range";
    public const string CadenceCoverage = "cadence.coverage";
    public const string ApprovalRuleWorkloadApprover = "approval-rule.workload-approver";
    public const string DecisionRightAiLetter = "decision-right.ai-letter";
    public const string RoleConfigAdminMissing = "role.config-admin-missing";
    public const string SettingLockedChanged = "setting.locked-changed";
    public const string SettingAiApprovalLockOff = "setting.ai-approval-lock-off";
    public const string ConfigProtectedChange = "config.protected-change";
    public const string SeparationSharedGroup = "separation.shared-group";
    public const string RoleUnbound = "role.unbound";
    public const string SettingRequired = "setting.required";
    public const string JsonSchema = "json.schema";
    public const string PatternInvalid = "pattern.invalid";
    public const string SnapshotSize = "snapshot.size";
    public const string ConfigBaseMoved = "config.base-moved";
    public const string PresetConflict = "preset.conflict";
    public const string PresetTenantData = "preset.tenant-data";

    // ---- OP-09 engine checks beyond V01 to V44 ----
    public const string SnapshotSchemaUnsupported = "snapshot.schema-unsupported";
    public const string RoleConfigAdmin = "role.config-admin";
    public const string StandardModelType = "standard-model.type";
    public const string StandardModelConditionKind = "standard-model.condition-kind";
    public const string VocabularySource = "vocabulary.source";

    // ---- OP-10 impact ----
    public const string ImpactCycleDetected = "impact.cycle-detected";
    public const string ImpactDepthCapped = "impact.depth-capped";

    // ---- OP-11 due dates and reminders ----
    public const string ReminderNoRule = "reminder.no-rule";
    public const string ReviewNoCadence = "review.no-cadence";

    // ---- OP-12 standard change ----
    public const string StandardModelNotApproved = "standard-model.not-approved";
    public const string StandardModelConditionNotMet = "standard-model.condition-not-met";
    public const string StandardModelConditionAsReported = "standard-model.condition-as-reported";
    public const string StandardModelQualifies = "standard-model.qualifies";
    public const string StandardModelNoCandidate = "standard-model.no-candidate";

    // ---- OP-13 gap check comparison ----
    public const string GapCheckItemIgnored = "gap-check.item-ignored";
    public const string GapCheckAsReported = "gap-check.as-reported";
    public const string GapCheckPartialRun = "gap-check.partial-run";

    // ---- OP-14 linked-mode authorization ----
    public const string AuthorizationNoEvidence = "authorization.no-evidence";
    public const string AuthorizationAiNotCounted = "authorization.ai-not-counted";
    public const string AuthorizationNotAttester = "authorization.not-attester";
    public const string AuthorizationWrongSource = "authorization.wrong-source";
    public const string AuthorizationNumberOnly = "authorization.number-only";
    public const string AuthorizationRevoked = "authorization.revoked";

    // A fact dated after the evaluation moment never authorizes.
    public const string AuthorizationReportedLater = "authorization.reported-later";

    // ---- Error model, section 7 ----
    public const string InputRequired = "input.required";
    public const string InputMalformed = "input.malformed";
    public const string InputUnknownKey = "input.unknown-key";

    // OP-08: a write names a column that is not in the entity's column list in the logical model.
    public const string InputUnknownColumn = "input.unknown-column";
    public const string InputContractValueUnknown = "input.contract-value-unknown";
    public const string InputJsonInvalid = "input.json-invalid";
    public const string RefNotFound = "ref.not-found";
    public const string SnapshotHashMismatch = "snapshot.hash-mismatch";
}
