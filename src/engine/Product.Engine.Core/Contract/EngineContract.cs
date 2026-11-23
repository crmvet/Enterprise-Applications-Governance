using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Product.Engine.Core.Contract;

// Every fixed value list the engine knows, with its wire values.
// Wire values are kebab-case of the meaning, as in document 21, section 8.3: "in-progress", "fail-closed".
//
// WORKING DEFAULT (open design question 1)
// The engine meaning lists below are the short list of fixed meanings that question 1 asks about.
// Labels stay configurable. Only these meanings are fixed.
public static class EngineContract
{
    private const string Doc20 = "Document 20, section 2.4";
    private const string Doc21 = "Document 21, section 13.4";
    private const string Doc25Hand = "Document 25, section 15";
    private const string Doc25Records = "Document 25, section 5";

    // ---- Document 20, section 2.4: the engine contract ----

    public static readonly ContractList<DecisionStatusMeaning> DecisionStatusMeanings = new(
        "decision-status-meaning", "Decision status meaning", ContractGroup.EngineMeaning, Doc20,
        P(DecisionStatusMeaning.Proposed, "proposed"),
        P(DecisionStatusMeaning.Accepted, "accepted"),
        P(DecisionStatusMeaning.Rejected, "rejected"),
        P(DecisionStatusMeaning.Superseded, "superseded"),
        P(DecisionStatusMeaning.Withdrawn, "withdrawn"));

    public static readonly ContractList<RecordStatusMeaning> RecordStatusMeanings = new(
        "record-status-meaning", "Record status meaning", ContractGroup.EngineMeaning, Doc20,
        P(RecordStatusMeaning.Draft, "draft"),
        P(RecordStatusMeaning.Submitted, "submitted"),
        P(RecordStatusMeaning.InProgress, "in-progress"),
        P(RecordStatusMeaning.Completed, "completed"),
        P(RecordStatusMeaning.Rejected, "rejected"),
        P(RecordStatusMeaning.Withdrawn, "withdrawn"),
        P(RecordStatusMeaning.Cancelled, "cancelled"));

    public static readonly ContractList<RegistrationStatusMeaning> RegistrationStatusMeanings = new(
        "registration-status-meaning", "Registration status meaning", ContractGroup.EngineMeaning, Doc20,
        P(RegistrationStatusMeaning.Draft, "draft"),
        P(RegistrationStatusMeaning.Registered, "registered"),
        P(RegistrationStatusMeaning.Archived, "archived"));

    public static readonly ContractList<ApprovalOutcomeMeaning> ApprovalOutcomeMeanings = new(
        "approval-outcome-meaning", "Approval outcome meaning", ContractGroup.EngineMeaning, Doc20,
        P(ApprovalOutcomeMeaning.Pending, "pending"),
        P(ApprovalOutcomeMeaning.Approved, "approved"),
        P(ApprovalOutcomeMeaning.Rejected, "rejected"),
        P(ApprovalOutcomeMeaning.Expired, "expired"),
        P(ApprovalOutcomeMeaning.Cancelled, "cancelled"),
        P(ApprovalOutcomeMeaning.RecommendationOnly, "recommendation-only"));

    public static readonly ContractList<DocumentStatusMeaning> DocumentStatusMeanings = new(
        "document-status-meaning", "Document status meaning", ContractGroup.EngineMeaning, Doc20,
        P(DocumentStatusMeaning.Draft, "draft"),
        P(DocumentStatusMeaning.Current, "current"),
        P(DocumentStatusMeaning.Stale, "stale"),
        P(DocumentStatusMeaning.Missing, "missing"));

    public static readonly ContractList<ClaimStatusMeaning> ClaimStatusMeanings = new(
        "claim-status-meaning", "Claim status meaning", ContractGroup.EngineMeaning, Doc20,
        P(ClaimStatusMeaning.Open, "open"),
        P(ClaimStatusMeaning.Claimed, "claimed"),
        P(ClaimStatusMeaning.Dismissed, "dismissed"));

    public static readonly ContractList<DeploymentResultMeaning> DeploymentResultMeanings = new(
        "deployment-result-meaning", "Deployment result meaning", ContractGroup.EngineMeaning, Doc20,
        P(DeploymentResultMeaning.Succeeded, "succeeded"),
        P(DeploymentResultMeaning.PartlySucceeded, "partly-succeeded"),
        P(DeploymentResultMeaning.Failed, "failed"),
        P(DeploymentResultMeaning.NotDeployed, "not-deployed"));

    public static readonly ContractList<ReviewStatusMeaning> ReviewStatusMeanings = new(
        "review-status-meaning", "Review status meaning", ContractGroup.EngineMeaning, Doc20,
        P(ReviewStatusMeaning.Due, "due"),
        P(ReviewStatusMeaning.Done, "done"),
        P(ReviewStatusMeaning.Cancelled, "cancelled"));

    public static readonly ContractList<LifecycleMeaning> LifecycleMeanings = new(
        "lifecycle-meaning", "Lifecycle meaning", ContractGroup.EngineMeaning, Doc20,
        P(LifecycleMeaning.PreLive, "pre-live"),
        P(LifecycleMeaning.Live, "live"),
        P(LifecycleMeaning.Retired, "retired"));

    public static readonly ContractList<GateLevel> GateLevels = new(
        "gate-level", "Gate level", ContractGroup.EngineMeaning, Doc20,
        P(GateLevel.Off, "off"),
        P(GateLevel.Record, "record"),
        P(GateLevel.Flag, "flag"),
        P(GateLevel.Advise, "advise"),
        P(GateLevel.Enforce, "enforce"));

    public static readonly ContractList<FailMode> FailModes = new(
        "fail-mode", "Fail mode", ContractGroup.EngineMeaning, Doc20,
        P(FailMode.FailOpen, "fail-open"),
        P(FailMode.FailClosed, "fail-closed"));

    public static readonly ContractList<ApprovalMode> ApprovalModes = new(
        "approval-mode", "Approval mode", ContractGroup.EngineMeaning, Doc20,
        P(ApprovalMode.EveryoneMustApprove, "everyone-must-approve"),
        P(ApprovalMode.FirstToRespond, "first-to-respond"),
        P(ApprovalMode.Sequential, "sequential"));

    public static readonly ContractList<DecisionRightLetter> DecisionRightLetters = new(
        "decision-right-letter", "Decision right letter", ContractGroup.EngineMeaning, Doc20,
        P(DecisionRightLetter.Responsible, "responsible"),
        P(DecisionRightLetter.Accountable, "accountable"),
        P(DecisionRightLetter.Consulted, "consulted"),
        P(DecisionRightLetter.Informed, "informed"),
        P(DecisionRightLetter.Supports, "supports"));

    public static readonly ContractList<ActorKindMeaning> ActorKindMeanings = new(
        "actor-kind-meaning", "Actor kind meaning", ContractGroup.EngineMeaning, Doc20,
        P(ActorKindMeaning.Person, "person"),
        P(ActorKindMeaning.AiClientForPerson, "ai-client-for-person"),
        P(ActorKindMeaning.AgentIdentity, "agent-identity"),
        P(ActorKindMeaning.Pipeline, "pipeline"),
        P(ActorKindMeaning.ApprovalRunner, "approval-runner"),
        P(ActorKindMeaning.Engine, "engine"));

    public static readonly ContractList<PrincipalKind> PrincipalKinds = new(
        "principal-kind", "Principal kind", ContractGroup.EngineMeaning, Doc20,
        P(PrincipalKind.User, "user"),
        P(PrincipalKind.Group, "group"),
        P(PrincipalKind.AgentIdentity, "agent-identity"),
        P(PrincipalKind.AgentUser, "agent-user"),
        P(PrincipalKind.ServicePrincipal, "service-principal"),
        P(PrincipalKind.ManagedIdentity, "managed-identity"));

    public static readonly ContractList<DeploymentRouteMeaning> DeploymentRouteMeanings = new(
        "deployment-route-meaning", "Deployment route meaning", ContractGroup.EngineMeaning, Doc20,
        P(DeploymentRouteMeaning.PipelineStep, "pipeline-step"),
        P(DeploymentRouteMeaning.AiTool, "ai-tool"),
        P(DeploymentRouteMeaning.PersonInApp, "person-in-app"),
        P(DeploymentRouteMeaning.Other, "other"));

    public static readonly ContractList<LinkCategory> LinkCategories = new(
        "link-category", "Link category", ContractGroup.EngineMeaning, Doc20,
        P(LinkCategory.Delivery, "delivery"),
        P(LinkCategory.Pipeline, "pipeline"),
        P(LinkCategory.Document, "document"),
        P(LinkCategory.Evidence, "evidence"),
        P(LinkCategory.MicrosoftResource, "microsoft-resource"),
        P(LinkCategory.ExternalChange, "external-change"));

    public static readonly ContractList<ChangeManagementMode> ChangeManagementModes = new(
        "change-management-mode", "Change management mode", ContractGroup.EngineMeaning, Doc20,
        P(ChangeManagementMode.BuiltIn, "built-in"),
        P(ChangeManagementMode.Linked, "linked"));

    public static readonly ContractList<FindingKind> FindingKinds = new(
        "finding-kind", "Finding kind", ContractGroup.EngineMeaning, Doc20,
        P(FindingKind.Unregistered, "unregistered"),
        P(FindingKind.Vanished, "vanished"));

    // Owner assigned is added by document 21, section 13.4.
    public static readonly ContractList<RequirementKind> RequirementKinds = new(
        "requirement-kind", "Requirement kind", ContractGroup.EngineMeaning, Doc20 + "; owner assigned from " + Doc21,
        P(RequirementKind.DecisionAccepted, "decision-accepted"),
        P(RequirementKind.DocumentCurrent, "document-current"),
        P(RequirementKind.ReviewDone, "review-done"),
        P(RequirementKind.OwnerAssigned, "owner-assigned"));

    public static readonly ContractList<ConfigurationSetKind> ConfigurationSetKinds = new(
        "configuration-set-kind", "Configuration set kind", ContractGroup.EngineMeaning, Doc20,
        P(ConfigurationSetKind.Preset, "preset"),
        P(ConfigurationSetKind.Working, "working"));

    public static readonly ContractList<PresetKind> PresetKinds = new(
        "preset-kind", "Preset kind", ContractGroup.EngineMeaning, Doc20,
        P(PresetKind.Full, "full"),
        P(PresetKind.Overlay, "overlay"));

    public static readonly ContractList<DraftStatusMeaning> DraftStatusMeanings = new(
        "draft-status-meaning", "Draft status meaning", ContractGroup.EngineMeaning, Doc20,
        P(DraftStatusMeaning.Draft, "draft"),
        P(DraftStatusMeaning.Submitted, "submitted"),
        P(DraftStatusMeaning.Activated, "activated"),
        P(DraftStatusMeaning.Discarded, "discarded"));

    public static readonly ContractList<PatchOperation> PatchOperations = new(
        "patch-operation", "Patch operation", ContractGroup.EngineMeaning, Doc20,
        P(PatchOperation.Upsert, "upsert"),
        P(PatchOperation.Retire, "retire"));

    public static readonly ContractList<SeparationRuleKind> SeparationRuleKinds = new(
        "separation-rule-kind", "Separation rule kind", ContractGroup.EngineMeaning, Doc20,
        P(SeparationRuleKind.ApproverDiffers, "approver-differs"),
        P(SeparationRuleKind.RolesInDifferentGroups, "roles-in-different-groups"));

    public static readonly ContractList<SeparationSubject> SeparationSubjects = new(
        "separation-subject", "Separation subject", ContractGroup.EngineMeaning, Doc20,
        P(SeparationSubject.Proposer, "proposer"),
        P(SeparationSubject.InstructingAdmin, "instructing-admin"),
        P(SeparationSubject.Deployer, "deployer"),
        P(SeparationSubject.OwnerRoleHolder, "owner-role-holder"),
        P(SeparationSubject.RoleHolder, "role-holder"));

    public static readonly ContractList<SeparationEnforcement> SeparationEnforcements = new(
        "separation-enforcement", "Separation enforcement", ContractGroup.EngineMeaning, Doc20,
        P(SeparationEnforcement.Refuse, "refuse"),
        P(SeparationEnforcement.ExceptionDecision, "exception-decision"),
        P(SeparationEnforcement.Flag, "flag"));

    // WORKING DEFAULT (open design question 7): the shipped default is flag. AI-only code approvals never count.
    public static readonly ContractList<AiOnlyCodeApprovalHandling> AiOnlyCodeApprovalHandlings = new(
        "ai-only-code-approval-handling", "AI-only code approval handling", ContractGroup.EngineMeaning, Doc20,
        P(AiOnlyCodeApprovalHandling.Ignore, "ignore"),
        P(AiOnlyCodeApprovalHandling.Flag, "flag"),
        P(AiOnlyCodeApprovalHandling.Stop, "stop"));

    public static readonly ContractList<Origin> Origins = new(
        "origin", "Origin", ContractGroup.EngineMeaning, Doc20,
        P(Origin.Shipped, "shipped"),
        P(Origin.Local, "local"));

    // ---- Document 21, section 13.4: lists added by the configuration model ----

    // WORKING DEFAULT (open design question 8): the locked list of decisions the engine may accept without a card.
    public static readonly ContractList<EngineAcceptanceCondition> EngineAcceptanceConditions = new(
        "engine-acceptance-condition", "Engine acceptance condition", ContractGroup.ConfigurationModel, Doc21,
        P(EngineAcceptanceCondition.LowestTier, "lowest-tier"),
        P(EngineAcceptanceCondition.StandardModel, "standard-model"));

    public static readonly ContractList<SettingValueType> SettingValueTypes = new(
        "setting-value-type", "Setting value type", ContractGroup.ConfigurationModel, Doc21,
        P(SettingValueType.Text, "text"),
        P(SettingValueType.Number, "number"),
        P(SettingValueType.YesNo, "yes-no"),
        P(SettingValueType.Json, "json"),
        P(SettingValueType.Key, "key"),
        P(SettingValueType.Contract, "contract"));

    public static readonly ContractList<IntakeVisibility> IntakeVisibilities = new(
        "intake-visibility", "Intake visibility", ContractGroup.ConfigurationModel, Doc21 + "; owned by document 22",
        P(IntakeVisibility.Own, "own"),
        P(IntakeVisibility.All, "all"));

    public static readonly ContractList<ClaimVisibility> ClaimVisibilities = new(
        "claim-visibility", "Claim visibility", ContractGroup.ConfigurationModel, Doc21 + "; owned by document 22",
        P(ClaimVisibility.AdminsAndClaimant, "admins-and-claimant"),
        P(ClaimVisibility.All, "all"));

    public static readonly ContractList<RecordAccessMode> RecordAccessModes = new(
        "record-access-mode", "Record access mode", ContractGroup.ConfigurationModel, Doc21 + "; owned by document 26",
        P(RecordAccessMode.OpenRead, "open-read"),
        P(RecordAccessMode.PortfolioRead, "portfolio-read"));

    // ---- Document 25, section 15: lists the engine design asks document 20 to add ----

    public static readonly ContractList<ConditionKind> ConditionKinds = new(
        "condition-kind", "Condition kind", ContractGroup.EngineDesign, Doc25Hand + "; OP-12",
        P(ConditionKind.MaxTierRank, "max-tier-rank"),
        P(ConditionKind.TargetStagesIn, "target-stages-in"),
        P(ConditionKind.MaxAffectedApplications, "max-affected-applications"),
        P(ConditionKind.ComponentKindsWithin, "component-kinds-within"),
        P(ConditionKind.MaxWindowMinutes, "max-window-minutes"),
        P(ConditionKind.Stated, "stated"));

    public static readonly ContractList<AuthorizationFactSourceKind> AuthorizationFactSourceKinds = new(
        "authorization-fact-source-kind", "Authorization fact source kind", ContractGroup.EngineDesign, Doc25Hand + "; OP-14",
        P(AuthorizationFactSourceKind.ItsmStep, "itsm-step"),
        P(AuthorizationFactSourceKind.Attestation, "attestation"));

    // ---- Document 25, section 5: operation vocabularies ----

    public static readonly ContractList<ReasonSeverity> ReasonSeverities = new(
        "reason-severity", "Reason severity", ContractGroup.OperationVocabulary, Doc25Records + ".1",
        P(ReasonSeverity.Info, "info"),
        P(ReasonSeverity.Warning, "warning"),
        P(ReasonSeverity.Error, "error"));

    public static readonly ContractList<RuleTraceRole> RuleTraceRoles = new(
        "rule-trace-role", "Rule trace role", ContractGroup.OperationVocabulary, Doc25Records + ".1",
        P(RuleTraceRole.Matched, "matched"),
        P(RuleTraceRole.Won, "won"),
        P(RuleTraceRole.Dropped, "dropped"),
        P(RuleTraceRole.Required, "required"),
        P(RuleTraceRole.Skipped, "skipped"));

    public static readonly ContractList<WriteOperation> WriteOperations = new(
        "write-operation", "Write operation", ContractGroup.OperationVocabulary, Doc25Records + ".10, OP-08",
        P(WriteOperation.Create, "create"),
        P(WriteOperation.Update, "update"),
        P(WriteOperation.Delete, "delete"));

    public static readonly ContractList<GateMode> GateModes = new(
        "gate-mode", "Gate mode", ContractGroup.OperationVocabulary, Doc25Records + ".6, OP-04",
        P(GateMode.Check, "check"),
        P(GateMode.Record, "record"));

    public static readonly ContractList<GateVerdict> GateVerdicts = new(
        "gate-verdict", "Gate verdict", ContractGroup.OperationVocabulary, Doc25Records + ".6, OP-04",
        P(GateVerdict.NotApplicable, "not-applicable"),
        P(GateVerdict.Allow, "allow"),
        P(GateVerdict.Warn, "warn"),
        P(GateVerdict.Stop, "stop"),
        P(GateVerdict.AllowWithException, "allow-with-exception"));

    public static readonly ContractList<WindowState> WindowStates = new(
        "window-state", "Window state", ContractGroup.OperationVocabulary, Doc25Records + ".7, OP-05",
        P(WindowState.Inside, "inside"),
        P(WindowState.Before, "before"),
        P(WindowState.After, "after"),
        P(WindowState.NoWindow, "no-window"));

    public static readonly ContractList<CodeApprovalKind> CodeApprovalKinds = new(
        "code-approval-kind", "Code approval kind", ContractGroup.OperationVocabulary, Doc25Records + ".6, OP-04",
        P(CodeApprovalKind.Person, "person"),
        P(CodeApprovalKind.AiOnly, "ai-only"),
        P(CodeApprovalKind.None, "none"),
        P(CodeApprovalKind.Unknown, "unknown"));

    public static readonly ContractList<ApprovalRequirementSource> ApprovalRequirementSources = new(
        "approval-requirement-source", "Approval requirement source", ContractGroup.OperationVocabulary, Doc25Records + ".4, OP-02",
        P(ApprovalRequirementSource.ApprovalRule, "approval-rule"),
        P(ApprovalRequirementSource.Accountable, "accountable"));

    public static readonly ContractList<EffectKind> EffectKinds = new(
        "effect-kind", "Effect kind", ContractGroup.OperationVocabulary, Doc25Records + ".10, OP-08",
        P(EffectKind.UpdateSubject, "update-subject"),
        P(EffectKind.SupersedeDecision, "supersede-decision"),
        P(EffectKind.AcceptDecision, "accept-decision"),
        P(EffectKind.ActivateConfiguration, "activate-configuration"),
        P(EffectKind.RecomputeApprovals, "recompute-approvals"),
        P(EffectKind.WriteAuditEntry, "write-audit-entry"));

    public static readonly ContractList<ApprovalOverall> ApprovalOveralls = new(
        "approval-overall", "Approval overall", ContractGroup.OperationVocabulary, Doc25Records + ".5, OP-03",
        P(ApprovalOverall.Pending, "pending"),
        P(ApprovalOverall.Satisfied, "satisfied"),
        P(ApprovalOverall.Rejected, "rejected"),
        P(ApprovalOverall.Expired, "expired"));

    public static readonly ContractList<AuthorizationState> AuthorizationStates = new(
        "authorization-state", "Authorization state", ContractGroup.OperationVocabulary, Doc25Records + ".15, OP-14",
        P(AuthorizationState.Authorized, "authorized"),
        P(AuthorizationState.NotAuthorized, "not-authorized"),
        P(AuthorizationState.Cancelled, "cancelled"));

    public static readonly ContractList<AuthorizationBasis> AuthorizationBases = new(
        "authorization-basis", "Authorization basis", ContractGroup.OperationVocabulary, Doc25Records + ".15, OP-14",
        P(AuthorizationBasis.ItsmStep, "itsm-step"),
        P(AuthorizationBasis.Attestation, "attestation"),
        P(AuthorizationBasis.None, "none"));

    public static readonly ContractList<ImpactDirection> ImpactDirections = new(
        "impact-direction", "Impact direction", ContractGroup.OperationVocabulary, Doc25Records + ".12, OP-10",
        P(ImpactDirection.Dependants, "dependants"),
        P(ImpactDirection.Dependencies, "dependencies"),
        P(ImpactDirection.Both, "both"));

    public static readonly ContractList<ReminderAction> ReminderActions = new(
        "reminder-action", "Reminder action", ContractGroup.OperationVocabulary, Doc25Records + ".12, OP-11",
        P(ReminderAction.Remind, "remind"),
        P(ReminderAction.Repeat, "repeat"),
        P(ReminderAction.Escalate, "escalate"));

    // ---- Lookups. These fields come last because static fields initialize in text order. ----

    // Every list, sorted by name (ordinal).
    public static readonly IReadOnlyList<IContractList> All = BuildAll();

    // C05 Status rows name the entity they apply to (appliesToEntity, document 21, section 12).
    // This maps that entity key to the meaning list its status rows use.
    public static readonly IReadOnlyDictionary<string, IContractList> StatusMeaningListsByEntity =
        new ReadOnlyDictionary<string, IContractList>(new Dictionary<string, IContractList>(StringComparer.Ordinal)
        {
            { "decision", DecisionStatusMeanings },
            { "intake-request", RecordStatusMeanings },
            { "change-request", RecordStatusMeanings },
            { "business-application", RegistrationStatusMeanings },
            { "approval-record", ApprovalOutcomeMeanings },
            { "document-link", DocumentStatusMeanings },
            { "claim", ClaimStatusMeanings },
            { "deployment-record", DeploymentResultMeanings },
            { "review", ReviewStatusMeanings },
        });

    // Configuration entities other than C05 whose rows carry an engine meaning (document 21, section 3.1).
    public static readonly IReadOnlyDictionary<string, IContractList> EngineMeaningListsByEntity =
        new ReadOnlyDictionary<string, IContractList>(new Dictionary<string, IContractList>(StringComparer.Ordinal)
        {
            { "lifecycle-stage", LifecycleMeanings },
            { "deployment-route", DeploymentRouteMeanings },
            { "actor-kind", ActorKindMeanings },
        });

    private static readonly Dictionary<string, IContractList> ByName = All.ToDictionary(list => list.Name, StringComparer.Ordinal);

    // Finds a list by its name, for example the "gate-level" of a setting value type "contract:gate-level".
    public static bool TryGetList(string? name, [NotNullWhen(true)] out IContractList? list)
    {
        if (name != null && ByName.TryGetValue(name, out IContractList? found))
        {
            list = found;
            return true;
        }

        list = null;
        return false;
    }

    private static KeyValuePair<T, string> P<T>(T meaning, string wire)
        where T : struct, Enum
    {
        return new KeyValuePair<T, string>(meaning, wire);
    }

    private static IReadOnlyList<IContractList> BuildAll()
    {
        IContractList[] lists =
        {
            DecisionStatusMeanings, RecordStatusMeanings, RegistrationStatusMeanings, ApprovalOutcomeMeanings,
            DocumentStatusMeanings, ClaimStatusMeanings, DeploymentResultMeanings, ReviewStatusMeanings,
            LifecycleMeanings, GateLevels, FailModes, ApprovalModes, DecisionRightLetters, ActorKindMeanings,
            PrincipalKinds, DeploymentRouteMeanings, LinkCategories, ChangeManagementModes, FindingKinds,
            RequirementKinds, ConfigurationSetKinds, PresetKinds, DraftStatusMeanings, PatchOperations,
            SeparationRuleKinds, SeparationSubjects, SeparationEnforcements, AiOnlyCodeApprovalHandlings, Origins,
            EngineAcceptanceConditions, SettingValueTypes, IntakeVisibilities, ClaimVisibilities, RecordAccessModes,
            ConditionKinds, AuthorizationFactSourceKinds,
            ReasonSeverities, RuleTraceRoles, WriteOperations, GateModes, GateVerdicts, WindowStates,
            CodeApprovalKinds, ApprovalRequirementSources, EffectKinds, ApprovalOveralls, AuthorizationStates,
            AuthorizationBases, ImpactDirections, ReminderActions,
        };

        IContractList[] sorted = lists.OrderBy(list => list.Name, StringComparer.Ordinal).ToArray();
        for (int i = 1; i < sorted.Length; i++)
        {
            if (string.Equals(sorted[i - 1].Name, sorted[i].Name, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Two contract lists share the name " + sorted[i].Name + ".");
            }
        }

        return new ReadOnlyCollection<IContractList>(sorted);
    }
}
