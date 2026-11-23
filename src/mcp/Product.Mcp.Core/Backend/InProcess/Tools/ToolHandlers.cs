using System;
using System.Collections.Generic;

namespace Product.Mcp.Core.Backend.InProcess;

// Every R1 tool of document 24, section 6.1 on the in-process backend, by name. Read tools run as read-only composed
// calls; write tools as write calls (validateOnly a dry run, clientRequestId the idempotency key). The record step tools
// use the Deployment Record's own idempotency key instead. check_change_approval is Later (D-03) and has no handler.
internal static partial class ToolHandlers
{
    public static readonly IReadOnlyDictionary<string, ToolHandler> All = new Dictionary<string, ToolHandler>(StringComparer.Ordinal)
    {
        // Session
        ["whoami"] = Read(WhoAmI),
        ["get_my_queue"] = Read(GetMyQueue),
        ["get_role_holders"] = Read(GetRoleHolders),

        // Portfolio
        ["find_applications"] = Read(FindApplications),
        ["get_application_owners"] = Read(GetApplicationOwners),
        ["get_application_card"] = Read(GetApplicationCard),
        ["get_dependency_impact"] = Read(GetDependencyImpact),
        ["get_portfolio_summary"] = Read(GetPortfolioSummary),
        ["get_requirements"] = Read(GetRequirements),

        // Intake
        ["check_duplicates"] = Read(CheckDuplicates),
        ["get_tier_questions"] = Read(GetTierQuestions),
        ["save_intake_draft"] = Write(SaveIntakeDraft),
        ["answer_tier_questions"] = Write(AnswerTierQuestions),
        ["save_application_draft"] = Write(SaveApplicationDraft),
        ["link_component"] = Write(LinkComponent),
        ["propose_owner_change"] = Write(ProposeOwnerChange),
        ["record_owner_leave"] = Write(RecordOwnerLeave),

        // Decisions
        ["save_decision_proposal"] = Write(SaveDecisionProposal),
        ["submit_record"] = Write(SubmitRecord),
        ["withdraw_record"] = Write(WithdrawRecord),
        ["get_decision_log"] = Read(GetDecisionLog),
        ["get_approval_status"] = Read(GetApprovalStatus),
        ["request_architecture_review"] = Write(RequestArchitectureReview),

        // Change
        ["save_change_request_draft"] = Write(SaveChangeRequestDraft),
        ["check_standard_change"] = Read(CheckStandardChange),
        ["get_change_request"] = Read(GetChangeRequest),
        ["get_change_schedule"] = Read(GetChangeSchedule),
        ["record_change_result"] = Write(RecordChangeResult),

        // Deployment
        ["record_deployment"] = new ToolHandler(false, RecordDeployment, usesClientRequestId: false),
        ["record_deployment_correction"] = new ToolHandler(false, RecordDeploymentCorrection, usesClientRequestId: false),
        ["get_deployments"] = Read(GetDeployments),
        ["map_pipeline"] = Write(MapPipeline),
        ["get_pipeline_map"] = Read(GetPipelineMap),

        // Documentation
        ["get_documentation_status"] = Read(GetDocumentationStatus),
        ["record_document_link"] = Write(RecordDocumentLink),
        ["confirm_document_checked"] = Write(ConfirmDocumentChecked),

        // Gap check
        ["run_gap_check"] = Write(RunGapCheck),
        ["compare_inventory_list"] = Write(CompareInventoryList),
        ["run_owner_check"] = Write(RunOwnerCheck),
        ["list_claims"] = Read(ListClaims),
        ["claim_resource"] = Write(ClaimResource),
        ["dismiss_claim"] = Write(DismissClaim),

        // Configuration
        ["get_configuration"] = Read(GetConfiguration),
        ["explain_rules"] = Read(ExplainRules),
        ["get_configuration_health"] = Read(GetConfigurationHealth),
        ["list_presets"] = Read(ListPresets),
        ["propose_configuration_change"] = Write(ProposeConfigurationChange),
        ["export_configuration"] = Read(ExportConfiguration),
        ["check_role_bindings"] = Read(CheckRoleBindings),

        // Links
        ["save_link"] = Write(SaveLink),
    };

    private static ToolHandler Read(Func<ToolScope, Product.Engine.Core.Canonical.JsonTreeNode> run)
    {
        return new ToolHandler(true, run);
    }

    private static ToolHandler Write(Func<ToolScope, Product.Engine.Core.Canonical.JsonTreeNode> run)
    {
        return new ToolHandler(false, run);
    }
}
