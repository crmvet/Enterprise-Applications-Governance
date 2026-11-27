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

    // The data taxonomy's requirement kinds (docs/architecture/data-taxonomy.md, Gate requirements).
    public const string GateRequirementDataUsesUndeclared = "gate.requirement-data-uses-undeclared";
    public const string GateRequirementDataUsesUndecided = "gate.requirement-data-uses-undecided";

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

    // The update-subject effect (document 25, B25-8): a proposed change that can no longer apply when the decision is
    // accepted, such as an owner assignment that already ended, is skipped and named.
    public const string SubjectChangeSkipped = "subject.change-skipped";

    // A review ends done only with Done On, Reviewer and an accepted Outcome Decision of its review type's decision type
    // (document 22, O21; document 25, B25-12).
    public const string ReviewDoneIncomplete = "review.done-incomplete";
    public const string ReviewOutcomeNotAccepted = "review.outcome-not-accepted";

    // Document 22, section 2.3, class F: a row frozen once final (RA-05 names no code).
    public const string RecordFinal = "record.final";

    // Document 22, O04 rules: a dependency points to exactly one of another application or a component, never to its own
    // application (document 25, B25-13).
    public const string DependencyTargetInvalid = "dependency.target-invalid";
    public const string ActorAgentWriteRefused = "actor.agent-write-refused";
    public const string LinkForbiddenUrl = "link.forbidden-url";
    public const string PrincipalErasureNotAllowed = "principal.erasure-not-allowed";
    public const string PrincipalErasureFinal = "principal.erasure-final";

    // G-06 "only once": the approval runner's different second answer for a record that already holds a final outcome.
    // A conflict, not a refusal of who wrote it.
    public const string ApprovalOutcomeFinal = "approval.outcome-final";

    // Document 22, section 8.1: a proposed decision is withdrawn only by its proposer or its decider.
    public const string DecisionWithdrawNotAllowed = "decision.withdraw-not-allowed";

    // ---- The data taxonomy's guard rules, G-18 to G-25 (docs/architecture/data-taxonomy.md) ----
    public const string DataDomainRetired = "data.domain-retired";
    public const string DataUseDuplicate = "data.use-duplicate";
    public const string DataLocationDuplicate = "data.location-duplicate";
    public const string DataLevelNotDeclarable = "data.level-not-declarable";
    public const string DataOwnerSubject = "data.owner-subject";
    public const string DataDecisionCannotAllow = "data.decision-cannot-allow";
    public const string AiDataRowDraftOnly = "ai.data-row-draft-only";
    public const string LinkSecret = "link.secret";
    public const string DataCopyOrigin = "data.copy-origin";
    public const string LocationDepth = "location.depth";
    public const string LocationStage = "location.stage";

    // D-41, question 7, as the product owner changed it: a use or copy whose rule is never is refused when it is saved.
    public const string DataRuleNever = "data.rule-never";

    // ---- The data taxonomy's validation rules, V49 to V60 (docs/architecture/data-taxonomy.md, Validation rules) ----
    public const string DataDomainParent = "data.domain-parent";
    public const string DataDomainLevel = "data.domain-level";
    public const string DataLevelRank = "data.level-rank";
    public const string DataClassification = "data.classification";
    public const string DataClassificationLower = "data.classification-lower";
    public const string DataDomainInUse = "data.domain-in-use";
    public const string DataDecisionUnrouted = "data.decision-unrouted";
    public const string DataOwnerSubjectRule = "data.owner-subject-rule";
    public const string DataOwnershipAccountable = "data.ownership-accountable";
    public const string DataPlatform = "data.platform";
    public const string DataLoosens = "data.loosens";
    public const string DataPresetTenant = "data.preset-tenant";

    // ---- The data checks DC-01 to DC-14 (OP-15 to OP-17; docs/architecture/data-taxonomy.md, Data checks) ----
    public const string DataNoOwner = "data.no-owner";
    public const string DataOwnOwnerMissing = "data.own-owner-missing";
    public const string DataNoDeputy = "data.no-deputy";
    public const string DataOwnerInactive = "data.owner-inactive";
    public const string DataDecisionMissing = "data.decision-missing";
    public const string DataNoOneToAsk = "data.no-one-to-ask";
    public const string DataBreaksRule = "data.breaks-rule";
    public const string DataCopyOutsideProduction = "data.copy-outside-production";
    public const string DataSensitivityBelowData = "data.sensitivity-below-data";
    public const string DataUndeclared = "data.undeclared";
    public const string DataMappingMissing = "data.mapping-missing";
    public const string DataLocationUnowned = "data.location-unowned";
    public const string DataLocationStale = "data.location-stale";
    public const string DataRetentionOpen = "data.retention-open";
    public const string DataRetiredDomainRows = "data.retired-domain-rows";

    // ---- The proposal right check: proposed codes ----
    // Document 24, tool 17, and the Tool Policy row save-decision-proposal: only a holder of a responsible, accountable or
    // consulted Decision Right (C31) for the decision type in its scope proposes it (judgment call, B24-5).
    public const string DecisionProposeNotAllowed = "decision.propose-not-allowed";

    // ---- A person's own answer to an approval (D-38): proposed codes ----
    // Document 26, AN-01 and AN-02 as D-38 rewrites them: a person answers their own Approval Record on the product's
    // decision page, and the token proves who answered. The approval answer check refuses, before anything is written,
    // every answer the person may not give.
    public const string ApprovalAnswerPersonOnly = "approval.answer-person-only";
    public const string ApprovalAnswerNotAsked = "approval.answer-not-asked";
    public const string ApprovalAnswerSeparation = "approval.answer-separation";
    public const string ApprovalAnswerAlreadyGiven = "approval.answer-already-given";
    public const string ApprovalAnswerExpired = "approval.answer-expired";
    public const string ApprovalAnswerClosed = "approval.answer-closed";
    public const string ApprovalAnswerReasonRequired = "approval.answer-reason-required";

    // ---- Engine runtime: proposed codes ----
    // WORKING DEFAULT (open design question 12): document 26, RA-07. An app-only caller counts only with a current
    // Workload Grant, and only for that grant's workload role.
    public const string ActorWorkloadGrantMissing = "actor.workload-grant-missing";

    // The approval refresh (expiry, restarts and cancellations) is the approval runner's call only.
    public const string ApprovalRefreshNotAllowed = "approval.refresh-not-allowed";

    // "Ask again" on an approval request (document 25, B25-24): only the decision's proposer or a platform admin, as a
    // person, asks a request again; and only a request that expired, or one in the last 48 hours of its time.
    public const string ApprovalRenewNotAllowed = "approval.renew-not-allowed";
    public const string ApprovalNotRenewable = "approval.not-renewable";

    // The due-state refresh (document 23, S2; document 25, B25-18): only the engine's own scheduled work or
    // a person holding the platform admin role runs it; an item that fails is reported and the others go on.
    public const string DueStateRefreshNotAllowed = "due-state.refresh-not-allowed";
    public const string DueStateItemFailed = "due-state.item-failed";

    // A decision the engine accepts with no card waits for the consent of the person it would make an owner (document 25,
    // B25-18): the business owner an intake names.
    public const string DecisionConsentWaiting = "decision.consent-waiting";

    // A deployment is recorded only to a target in service (document 24, B24-18; document 25, B25-19): a retired target keeps
    // its records and takes no new one.
    public const string DeploymentTargetRetired = "deployment.target-retired";

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
