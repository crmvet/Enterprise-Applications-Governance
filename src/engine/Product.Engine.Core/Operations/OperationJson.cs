using System;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Snapshots;

namespace Product.Engine.Core.Operations;

// One parsing path for every caller (document 25, section 11.4: every response carries the full EvaluationResult).
// Takes an operation's input in its wire form and returns the result with the payload in its wire form.
// The fixture runner uses it, and adapters can map custom API parameters and endpoint bodies through it.
public static class OperationJson
{
    // True for the operations this build evaluates through OperationJson.
    public static bool IsBuilt(EngineOperation operation)
    {
        switch (operation)
        {
            case EngineOperation.EvaluateTier:
            case EngineOperation.ResolveRequiredApprovals:
            case EngineOperation.EvaluateApprovalStatus:
            case EngineOperation.EvaluateGateDecision:
            case EngineOperation.CheckChangeWindow:
            case EngineOperation.EvaluateDocumentationCompleteness:
            case EngineOperation.EvaluateGateReadiness:
            case EngineOperation.GuardWrite:
            case EngineOperation.WalkImpact:
            case EngineOperation.ComputeDueDates:
            case EngineOperation.EvaluateStandardChange:
            case EngineOperation.CompareGapCheckResult:
            case EngineOperation.EvaluateLinkedAuthorization:
            case EngineOperation.ResolveDataDomain:
            case EngineOperation.EvaluateDataUse:
            case EngineOperation.RunDataChecks:
                return true;
            default:
                return false;
        }
    }

    // True when the operation reads the configuration snapshot. OP-05 reads dates only.
    public static bool ReadsSnapshot(EngineOperation operation)
    {
        return operation != EngineOperation.CheckChangeWindow;
    }

    public static EvaluationResult<JsonTreeNode?> Evaluate(EngineOperation operation, ConfigurationSnapshot? snapshot, EvaluationContext context, JsonTreeNode input)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (input == null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        switch (operation)
        {
            case EngineOperation.EvaluateTier:
                return ToJson(TierOperation.Evaluate(snapshot!, context, input));
            case EngineOperation.ResolveRequiredApprovals:
                return ToJson(RequiredApprovalsOperation.Evaluate(snapshot!, context, input));
            case EngineOperation.EvaluateApprovalStatus:
                return ToJson(ApprovalStatusOperation.Evaluate(snapshot!, context, input));
            case EngineOperation.EvaluateGateDecision:
                return ToJson(GateDecisionOperation.Evaluate(snapshot!, context, input));
            case EngineOperation.CheckChangeWindow:
                return ToJson(ChangeWindowOperation.Evaluate(context, input));
            case EngineOperation.EvaluateDocumentationCompleteness:
                return ToJson(DocumentationOperation.Evaluate(snapshot!, context, input));
            case EngineOperation.EvaluateGateReadiness:
                return ToJson(GateReadinessOperation.Evaluate(snapshot!, context, input));
            case EngineOperation.GuardWrite:
                return ToJson(WriteGuardOperation.Evaluate(snapshot!, context, input));
            case EngineOperation.WalkImpact:
                return ToJson(ImpactOperation.Evaluate(snapshot!, context, input));
            case EngineOperation.ComputeDueDates:
                return ToJson(DueDatesOperation.Evaluate(snapshot!, context, input));
            case EngineOperation.EvaluateStandardChange:
                return ToJson(StandardChangeOperation.Evaluate(snapshot!, context, input));
            case EngineOperation.CompareGapCheckResult:
                return ToJson(GapCompareOperation.Evaluate(snapshot!, context, input));
            case EngineOperation.EvaluateLinkedAuthorization:
                return ToJson(LinkedAuthorizationOperation.Evaluate(snapshot!, context, input));
            case EngineOperation.ResolveDataDomain:
                return ToJson(DataDomainOperation.Evaluate(snapshot!, context, input));
            case EngineOperation.EvaluateDataUse:
                return ToJson(DataUseOperation.Evaluate(snapshot!, context, input));
            case EngineOperation.RunDataChecks:
                return ToJson(DataChecksOperation.Evaluate(snapshot!, context, input));
            default:
                throw new NotSupportedException(OperationCatalog.Get(operation).Id + " is not built yet.");
        }
    }

    private static EvaluationResult<JsonTreeNode?> ToJson<TPayload>(EvaluationResult<TPayload?> result)
        where TPayload : class, IOperationPayload
    {
        return new EvaluationResult<JsonTreeNode?>(
            result.Outcome,
            result.Payload?.ToJson(),
            result.Reasons,
            result.Trace,
            result.ConfigurationVersion,
            result.SnapshotHash,
            result.EngineVersion);
    }
}
