using System;
using System.Collections.Generic;

namespace Product.Mcp.Core.Backend.InProcess;

// Every R1 tool of document 24, section 6.1 on the in-process backend, by name (answer_approval, tool 52, is served only on
// the person entry, D-38; tools 53 to 59 are the reads of the web app's screens, in ScreenReadTools.cs; tool 60 is the
// PMO's demand fields and tool 61 the people search, B24-9; tools 62 and 64, complete_review and complete_change, are
// served only on the person entry like answer_approval, and tool 63 is the review read, B24-10; tools 65 to 74 are the
// reads and writes of the remaining screens, B24-11: the audit trail, the evidence pack, the AI activity, the tier
// assessment, the documentation list, the places, the gap check runs, and the dependency and portfolio writes, of which
// save_deployment_target and save_portfolio are served only on the person entry; tool 75 is the record search by
// number, B24-12; tools 76 to 85 are the data toolset of the data taxonomy, B24-16: six reads and four writes, of which
// move_data_references lets an AI client only preview; tool 86, run_due_state_refresh, is a platform admin's run of the
// daily due-state refresh, served only on the person entry, B24-17: its items commit in calls of their own before its
// read call; tools 87 to 94 are the reads and the one write of the screens still missing, B24-18: the configuration
// versions and drafts, one configuration draft, role access, the guidelines, the measures, the Admin page, the agents and
// a security review request; tools 95 and 96 are the teams and committees, B24-20, in TeamTools.cs; tools 97 and 98 are the
// data uses and mappings across domains, B24-21, in DataReadTools.cs; tool 99 is the data taxonomy import, in ImportTools.cs,
// and tools 100 to 102 keep approval requests alive, B24-22, in ApprovalUpkeepTools.cs: "Ask again" and the outbox retry are
// served only on the person entry, and the demo's freshness read only on a demo install). Read tools run as
// read-only composed calls; write tools as write calls (validateOnly a dry run, clientRequestId the idempotency key). The
// record step tools use the Deployment Record's own idempotency key instead. check_change_approval is Later (D-03) and has
// no handler.
internal static partial class ToolHandlers
{
    public static readonly IReadOnlyDictionary<string, ToolHandler> All = new Dictionary<string, ToolHandler>(StringComparer.Ordinal)
    {
        // Session
        ["whoami"] = Read(WhoAmI),
        ["get_my_queue"] = Read(GetMyQueue),
        ["get_role_holders"] = Read(GetRoleHolders),
        ["find_people"] = Read(FindPeople),
        ["find_records"] = Read(FindRecords),
        ["list_teams"] = Read(ListTeams),
        ["get_team"] = Read(GetTeam),
        ["get_demo_freshness"] = Read(GetDemoFreshness),

        // Portfolio
        ["find_applications"] = Read(FindApplications),
        ["get_application_owners"] = Read(GetApplicationOwners),
        ["get_application_card"] = Read(GetApplicationCard),
        ["get_dependency_impact"] = Read(GetDependencyImpact),
        ["get_portfolio_summary"] = Read(GetPortfolioSummary),
        ["get_requirements"] = Read(GetRequirements),
        ["list_reviews"] = Read(ListReviews),
        ["get_review"] = Read(GetReview),
        ["get_ai_activity"] = Read(GetAiActivity),
        ["get_measures"] = Read(GetMeasures),
        ["list_agents"] = Read(ListAgents),
        ["save_portfolio"] = Write(SavePortfolio),
        ["list_portfolios"] = Read(ListPortfolios),
        ["list_owner_assignments"] = Read(ListOwnerAssignments),

        // Intake
        ["check_duplicates"] = Read(CheckDuplicates),
        ["get_tier_questions"] = Read(GetTierQuestions),
        ["save_intake_draft"] = Write(SaveIntakeDraft),
        ["answer_tier_questions"] = Write(AnswerTierQuestions),
        ["save_application_draft"] = Write(SaveApplicationDraft),
        ["link_component"] = Write(LinkComponent),
        ["propose_owner_change"] = Write(ProposeOwnerChange),
        ["record_owner_leave"] = Write(RecordOwnerLeave),
        ["list_intake_requests"] = Read(ListIntakeRequests),
        ["get_intake_request"] = Read(GetIntakeRequest),
        ["score_intake_request"] = Write(ScoreIntakeRequest),
        ["get_tier_assessment"] = Read(GetTierAssessment),
        ["save_dependency"] = Write(SaveDependency),

        // Decisions
        ["save_decision_proposal"] = Write(SaveDecisionProposal),
        ["submit_record"] = Write(SubmitRecord),
        ["withdraw_record"] = Write(WithdrawRecord),
        ["get_decision_log"] = Read(GetDecisionLog),
        ["get_approval_status"] = Read(GetApprovalStatus),
        ["request_architecture_review"] = Write(RequestArchitectureReview),
        ["complete_review"] = Write(CompleteReview),
        ["answer_approval"] = Write(AnswerApproval),
        ["get_decision"] = Read(GetDecision),
        ["list_approvals"] = Read(ListApprovals),
        ["get_audit_trail"] = Read(GetAuditTrail),
        ["get_evidence"] = Read(GetEvidence),
        ["request_security_review"] = Write(RequestSecurityReview),
        ["renew_approval_request"] = Write(RenewApprovalRequest),

        // Change
        ["save_change_request_draft"] = Write(SaveChangeRequestDraft),
        ["check_standard_change"] = Read(CheckStandardChange),
        ["get_change_request"] = Read(GetChangeRequest),
        ["get_change_schedule"] = Read(GetChangeSchedule),
        ["record_change_result"] = Write(RecordChangeResult),
        ["complete_change"] = Write(CompleteChange),

        // Deployment
        ["record_deployment"] = new ToolHandler(false, RecordDeployment, usesClientRequestId: false),
        ["record_deployment_correction"] = new ToolHandler(false, RecordDeploymentCorrection, usesClientRequestId: false),
        ["get_deployments"] = Read(GetDeployments),
        ["map_pipeline"] = Write(MapPipeline),
        ["get_pipeline_map"] = Read(GetPipelineMap),
        ["list_deployment_targets"] = Read(ListDeploymentTargets),
        ["save_deployment_target"] = Write(SaveDeploymentTarget),

        // Documentation
        ["get_documentation_status"] = Read(GetDocumentationStatus),
        ["record_document_link"] = Write(RecordDocumentLink),
        ["confirm_document_checked"] = Write(ConfirmDocumentChecked),
        ["list_documents"] = Read(ListDocuments),

        // Gap check
        ["run_gap_check"] = Write(RunGapCheck),
        ["compare_inventory_list"] = Write(CompareInventoryList),
        ["run_owner_check"] = Write(RunOwnerCheck),
        ["list_claims"] = Read(ListClaims),
        ["list_gap_check_runs"] = Read(ListGapCheckRuns),
        ["claim_resource"] = Write(ClaimResource),
        ["dismiss_claim"] = Write(DismissClaim),
        ["run_due_state_refresh"] = new ToolHandler(true, RunDueStateRefresh, usesClientRequestId: false, before: RunDueStateRefreshFirst),

        // Configuration
        ["get_configuration"] = Read(GetConfiguration),
        ["explain_rules"] = Read(ExplainRules),
        ["get_configuration_health"] = Read(GetConfigurationHealth),
        ["list_presets"] = Read(ListPresets),
        ["propose_configuration_change"] = Write(ProposeConfigurationChange),
        ["export_configuration"] = Read(ExportConfiguration),
        ["check_role_bindings"] = Read(CheckRoleBindings),
        ["list_configuration_changes"] = Read(ListConfigurationChanges),
        ["get_configuration_draft"] = Read(GetConfigurationDraft),
        ["get_role_access"] = Read(GetRoleAccess),
        ["list_guidelines"] = Read(ListGuidelines),
        ["get_admin_status"] = Read(GetAdminStatus),
        ["retry_approval_card"] = Write(RetryApprovalCard),
        ["import_data_taxonomy"] = Write(ImportDataTaxonomy),

        // Links
        ["save_link"] = Write(SaveLink),

        // Data (tools 76 to 85, data taxonomy slice 3; DataReadTools.cs and DataWriteTools.cs)
        ["find_data_domains"] = Read(FindDataDomains),
        ["get_data_domain"] = Read(GetDataDomain),
        ["list_data_uses"] = Read(ListDataUses),
        ["list_data_mappings"] = Read(ListDataMappings),
        ["find_data_locations"] = Read(FindDataLocations),
        ["resolve_data"] = Read(ResolveData),
        ["check_data_use"] = Read(CheckDataUse),
        ["get_data_gaps"] = Read(GetDataGaps),
        ["save_data_use"] = Write(SaveDataUse),
        ["save_data_location"] = Write(SaveDataLocation),
        ["save_data_mapping"] = Write(SaveDataMapping),
        ["move_data_references"] = new ToolHandler(false, MoveDataReferences, before: MovePreviewAsPerson),
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
