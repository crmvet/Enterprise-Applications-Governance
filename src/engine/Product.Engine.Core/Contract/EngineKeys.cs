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
