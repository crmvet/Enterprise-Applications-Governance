using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Product.Engine.Core.Contract;

namespace Product.Engine.Core;

// The fourteen operations of the engine core (document 25, section 5.2).
// The number is the operation ID, so EvaluateTier is OP-01.
public enum EngineOperation
{
    EvaluateTier = 1,
    ResolveRequiredApprovals = 2,
    EvaluateApprovalStatus = 3,
    EvaluateGateDecision = 4,
    CheckChangeWindow = 5,
    EvaluateDocumentationCompleteness = 6,
    EvaluateGateReadiness = 7,
    GuardWrite = 8,
    ValidateConfigurationSet = 9,
    WalkImpact = 10,
    ComputeDueDates = 11,
    EvaluateStandardChange = 12,
    CompareGapCheckResult = 13,
    EvaluateLinkedAuthorization = 14,
}

// What one operation is and which outcome words it returns.
public sealed class OperationInfo
{
    internal OperationInfo(EngineOperation operation, string title, bool isWrite, bool outcomesProposed, params string[] outcomes)
    {
        foreach (string outcome in outcomes)
        {
            if (!WireFormat.IsKebabCase(outcome))
            {
                throw new ArgumentException("An outcome word is kebab-case: " + outcome, nameof(outcomes));
            }
        }

        Operation = operation;
        Id = "OP-" + ((int)operation).ToString("00", System.Globalization.CultureInfo.InvariantCulture);
        Title = title;
        IsWrite = isWrite;
        OutcomesProposed = outcomesProposed;
        Outcomes = new ReadOnlyCollection<string>(outcomes);
    }

    public EngineOperation Operation { get; }

    // "OP-01" to "OP-14".
    public string Id { get; }

    public string Title { get; }

    // True for OP-08, the only write rule. OP-13 returns a write plan but is a read.
    public bool IsWrite { get; }

    // True when document 25 lists no outcome words for the operation and these are proposed.
    public bool OutcomesProposed { get; }

    public IReadOnlyList<string> Outcomes { get; }

    public bool IsKnownOutcome(string? outcome)
    {
        return outcome != null && Outcomes.Contains(outcome, StringComparer.Ordinal);
    }
}

public static class OperationCatalog
{
    // Every operation, in ID order.
    public static readonly IReadOnlyList<OperationInfo> All = new ReadOnlyCollection<OperationInfo>(new[]
    {
        new OperationInfo(EngineOperation.EvaluateTier, "Evaluate tier", false, false, "computed", "incomplete"),

        // WORKING DEFAULT (open design question 8): engine-accept only for the locked list. No "none required" outcome.
        new OperationInfo(EngineOperation.ResolveRequiredApprovals, "Resolve required approvals", false, false, "required", "engine-accept"),
        new OperationInfo(EngineOperation.EvaluateApprovalStatus, "Evaluate approval status", false, false, "pending", "satisfied", "rejected", "expired"),

        // The gate verdict is the outcome word.
        new OperationInfo(EngineOperation.EvaluateGateDecision, "Evaluate gate decision", false, false, "not-applicable", "allow", "warn", "stop", "allow-with-exception"),
        new OperationInfo(EngineOperation.CheckChangeWindow, "Check change window", false, false, "inside", "outside", "no-window"),
        new OperationInfo(EngineOperation.EvaluateDocumentationCompleteness, "Evaluate documentation completeness", false, false, "complete", "incomplete"),
        new OperationInfo(EngineOperation.EvaluateGateReadiness, "Evaluate gate readiness", false, false, "ready", "not-ready"),

        // Document 25 gives OP-08 an allowed flag, OP-09 an isValid flag, and OP-10 and OP-11 no outcome words. These are proposed.
        new OperationInfo(EngineOperation.GuardWrite, "Guard a write", true, true, "allowed", "refused"),
        new OperationInfo(EngineOperation.ValidateConfigurationSet, "Validate a configuration set", false, true, "valid", "invalid"),
        new OperationInfo(EngineOperation.WalkImpact, "Walk impact", false, true, "walked"),
        new OperationInfo(EngineOperation.ComputeDueDates, "Compute due dates and reminders", false, true, "computed"),
        new OperationInfo(EngineOperation.EvaluateStandardChange, "Evaluate a standard change", false, false, "qualifies", "does-not-qualify"),
        new OperationInfo(EngineOperation.CompareGapCheckResult, "Compare a gap check result", false, false, "compared"),
        new OperationInfo(EngineOperation.EvaluateLinkedAuthorization, "Evaluate linked-mode authorization", false, false, "authorized", "not-authorized", "not-applicable"),
    });

    public static OperationInfo Get(EngineOperation operation)
    {
        int index = (int)operation - 1;
        if (index < 0 || index >= All.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(operation));
        }

        return All[index];
    }
}
