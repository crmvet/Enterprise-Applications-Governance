using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Product.Engine.Core.Contract;

// Configuration keys the engine operations name. Labels stay configurable. Keys never change (document 21, rule 7).
// Every value the operations compare still comes from the snapshot: these are only the row keys they look up.
//
// WORKING DEFAULT (open design question 1): these keys are part of the short list of fixed engine meanings.
public static class EngineKeys
{
    // ---- C04 settings the operations read (document 25, section 6.5; document 21, section 3.6) ----

    public const string EngineAcceptableDecisionTypes = "engine-acceptable-decision-types";
    public const string AiApprovalLock = "ai-approval-lock";
    public const string DefaultFlagNotify = "default-flag-notify";
    public const string DefaultAiOnlyCodeApprovalHandling = "default-ai-only-code-approval-handling";
    public const string WorkingDays = "working-days";
    public const string UnlinkedDeploymentChangeType = "unlinked-deployment-change-type";
    public const string ImpactMaxDepth = "impact-max-depth";
    public const string ReminderChannel = "reminder-channel";
    public const string LinkUndoMinutes = "link-undo-minutes";
    public const string ForbiddenUrlPatterns = "forbidden-url-patterns";

    // ---- C18 decision types the locked list names ----

    public const string IntakeDecisionType = "intake";
    public const string ChangeAuthorizationDecisionType = "change-authorization";

    // ---- The update-subject effect (document 25, B25-8) ----

    // WORKING DEFAULT (open design question 1): the decision types and outcomes whose acceptance changes the subject
    // record are fixed keys, like the locked list. The C19 Next Lifecycle Stage of any outcome moves the stage for every
    // decision type; these keys add the type's own change. A type or outcome a client adds changes only the stage.
    public const string TierDecisionType = "tier";
    public const string OwnershipDecisionType = "ownership";
    public const string AutonomyLevelDecisionType = "autonomy-level";

    // Intake outcomes (document 23, P01 step 7): accept registers, park keeps the request open, redirect and decline
    // close it.
    public const string IntakeAcceptOutcome = "accept";
    public const string IntakeParkOutcome = "park";
    public const string IntakeRedirectOutcome = "redirect";
    public const string IntakeDeclineOutcome = "decline";

    // Change authorization outcomes (document 23, P03 step 4).
    public const string ChangeAuthorizeOutcome = "authorize";
    public const string ChangeRejectOutcome = "reject";

    // The tier outcome that takes the tier the answers imply when the proposal names none (document 23, P02 step 4).
    public const string TierConfirmOutcome = "confirm";

    // The autonomy level outcome that stores the agreed level (document 23, P02 step 8).
    public const string AutonomyAgreeOutcome = "agree";

    // The C06 list that holds the autonomy levels (document 22, O02 Agreed Autonomy Level).
    public const string AutonomyLevelList = "autonomy-level";

    // The owner role an intake request's Business Owner stands for (document 22, O07 Business Owner, a proposal; document
    // 25, B25-10): it counts for an Accept gate requirement of this owner role, and the accepted intake makes the person
    // this owner of the application it registers, when the application has none.
    public const string BusinessOwnerRole = "business-owner";

    // The decision type whose proposal records the standard it departs from and a review date (document 22, O09
    // Exception Standard and Exception Review On, proposals; document 23, P04 step 6).
    public const string ExceptionDecisionType = "exception";

    // ---- OP-08 write guard and activation ----

    // WORKING DEFAULT (open design question 1): the decision type the bootstrap of version 1 records (document 21,
    // section 4.8: "recorded as a configuration change decision"). V32 already names the same key.
    public const string ConfigurationChangeDecisionType = "configuration-change";

    // WORKING DEFAULT (open design question 1), WORKING DEFAULT (open design question 12): the workload role the
    // approval runner holds through a workload grant (document 26, section 3.4). G-06 lets only its holder write an
    // approval outcome. The row must be active and flagged Is Workload Role, or nobody can write an outcome.
    public const string ApprovalWriterRole = "approval-writer";

    // WORKING DEFAULT (open design question 1), WORKING DEFAULT (open design question 12): the workload role a
    // pipeline's workload identity holds through a workload grant to record deployments (document 26, section 3.4). The
    // engine runtime names a caller holding it a pipeline, and the record step needs it (RA-07).
    public const string DeploymentRecorderRole = "deployment-recorder";

    // WORKING DEFAULT (open design question 1): the role that performs an erasure (document 26, sections 5.3 and 9.5:
    // "Erasure is a recorded decision performed by a platform admin").
    public const string PlatformAdminRole = "platform-admin";

    // The display label erasure leaves on a Principal Reference (document 22, section 2.7; document 26, section 9.5).
    public const string ErasedDisplayLabel = "former user";

    // ---- C26 Reminder Rule subject kinds (document 21, section 3.5) ----

    // WORKING DEFAULT (open design question 1): the four subject kinds are fixed words the engine knows, like the
    // engine meanings. Labels and the rules themselves stay configurable.
    // The subject kind whose due date can also come from a C25 Review Cadence (OP-11).
    public const string ReviewSubjectKind = "review";

    // The four subject kinds document 21 names for C26. The schema keeps the same list
    // (config-bundle.schema.json, contract.reminder-subject-kind), and a test keeps the two equal.
    public static readonly IReadOnlyList<string> ReminderSubjectKinds = new ReadOnlyCollection<string>(new[] { ReviewSubjectKind, "document", "approval", "claim" });

    // ---- The due-state refresh (document 23, S2; document 25, B25-18) ----

    // The engine's own identity in stamps and audit entries when it runs its scheduled work with no person behind it
    // (document 22, section 2.4: "The engine itself ... The engine's identity"). Its version digit is 0, so it is never
    // an Entra object ID, which is a random (version 4) GUID.
    public const string EnginePrincipalId = "00000000-0000-0000-0000-00000000e000";

    // The Client App label the engine stamps on the rows its scheduled work writes.
    public const string EngineClientApp = "Due-state refresh";

    // WORKING DEFAULT (open design question 1): a person runs the refresh now only with the platform admin role
    // (PlatformAdminRole), as the platform admin runs the owner check and the gap check.

    // ---- The data taxonomy (D-37, settled by D-41; docs/architecture/data-taxonomy.md) ----

    // WORKING DEFAULT (open design question 1): the data checks and the data-use state know these fixed keys, as G-17
    // knows the decision types. Labels stay configurable.
    public const string DataTaxonomyModeSetting = "data-taxonomy-mode";
    public const string DataLocationStaleDays = "data-location-stale-days";
    public const string DataOwnerRole = "data-owner";
    public const string DataStewardRole = "data-steward";
    public const string TechnicalOwnerRole = "technical-owner";
    public const string DataUseDecisionType = "data-use";
    public const string DataRetentionDecisionType = "data-retention";
    public const string DataUseAllowOutcome = "allow";
    public const string DataUseDenyOutcome = "deny";

    // O09 Subject Kind values of the data taxonomy's decisions (entity keys, as every subject kind).
    public const string DataUseSubjectKind = "data-use";
    public const string DataMappingSubjectKind = "data-mapping";
    public const string DataDomainSubjectKind = "data-domain";
    public const string DataLocationSubjectKind = "data-location";
    public const string ApplicationSubjectKind = "business-application";

    // C17 Dependency Type keys that mean data moves between two applications (DC-10).
    public static readonly IReadOnlyList<string> DataDependencyTypes = new ReadOnlyCollection<string>(new[] { "reads-data-from", "writes-data-to" });
}

// One entry of the locked setting engine-acceptable-decision-types: a decision type plus an engine acceptance condition.
public sealed class EngineAcceptanceEntry
{
    public EngineAcceptanceEntry(string decisionTypeKey, EngineAcceptanceCondition condition)
    {
        if (!ConfigurationKey.IsValid(decisionTypeKey))
        {
            throw new ArgumentException("A decision type key must use the configuration key format.", nameof(decisionTypeKey));
        }

        DecisionTypeKey = decisionTypeKey;
        Condition = condition;
    }

    public string DecisionTypeKey { get; }

    public EngineAcceptanceCondition Condition { get; }

    public string ConditionWire => EngineContract.EngineAcceptanceConditions.ToWire(Condition);
}

// The locked list of decisions the engine may accept without a person's card (document 21, section 3.6;
// document 25, OP-02 rule 8). V33 keeps the setting equal to this list; OP-02 refuses a snapshot whose setting differs.
//
// WORKING DEFAULT (open design question 8): lowest-tier intake and standard changes. Extending it needs a product release.
public static class EngineAcceptance
{
    public static readonly IReadOnlyList<EngineAcceptanceEntry> ShippedEntries = new ReadOnlyCollection<EngineAcceptanceEntry>(new[]
    {
        new EngineAcceptanceEntry(EngineKeys.IntakeDecisionType, EngineAcceptanceCondition.LowestTier),
        new EngineAcceptanceEntry(EngineKeys.ChangeAuthorizationDecisionType, EngineAcceptanceCondition.StandardModel),
    });
}
