using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Mcp.Core.Backend.InProcess;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Json;
using Xunit;

namespace Product.Mcp.Core.Tests;

// One contract test per R1 tool (and resource), against the in-memory store. Each case calls the tool through the
// whole server core (token, session, policy, rate limit, input schema, backend, envelope) and the server checks the
// envelope against the tool's output schema before it returns (McpServerOptions.ValidateOutputs), so a passing case
// is a schema-valid answer from real store rows.
public sealed class ToolContractTests
{
    private const string Owner = McpHarness.TokenOwner;
    private const string Admin = McpHarness.TokenAdmin;
    private const string Reader = McpHarness.TokenReader;

    private static readonly Dictionary<string, (string Token, Func<McpHarness, string> Arguments)> Cases = new(StringComparer.Ordinal)
    {
        ["whoami"] = (Owner, h => "{}"),
        ["get_my_queue"] = (Owner, h => "{}"),
        ["get_role_holders"] = (Owner, h => "{\"roleKey\":\"change-manager\"}"),
        ["find_people"] = (Reader, h => "{\"text\":\"fixture use\"}"),
        ["find_records"] = (Reader, h => "{\"recordNumber\":\"APP-0001\"}"),
        ["list_teams"] = (Reader, h => { Scenarios.SubmittedIntake(h); return "{}"; }),
        ["get_team"] = (Reader, h => { Scenarios.SubmittedIntake(h); return "{\"teamId\":\"" + McpHarness.PlatformAdminGroup + "\"}"; }),
        ["get_demo_freshness"] = (Reader, h => { Scenarios.DemoHost(h); Scenarios.ExpiringRetirementApproval(h, 1); return "{}"; }),
        ["find_applications"] = (Reader, h => "{\"text\":\"expense\"}"),
        ["get_application_owners"] = (Reader, h => "{\"businessApplicationId\":\"" + McpHarness.Expense + "\"}"),
        ["get_application_card"] = (Reader, h => "{\"businessApplicationId\":\"" + McpHarness.Expense + "\"}"),
        ["get_dependency_impact"] = (Reader, h => "{\"businessApplicationId\":\"" + McpHarness.Expense + "\",\"direction\":\"dependants\"}"),
        ["get_portfolio_summary"] = (Reader, h => "{\"includeOwnerGaps\":true}"),
        ["get_requirements"] = (Reader, h => "{\"businessApplicationId\":\"" + McpHarness.Expense + "\"}"),
        ["list_reviews"] = (Reader, h => { Scenarios.ArchitectureReview(h); return "{\"state\":\"all\"}"; }),
        ["get_review"] = (Reader, h => "{\"reviewId\":\"" + Scenarios.ArchitectureReview(h) + "\"}"),
        ["list_portfolios"] = (Reader, h => "{}"),
        ["list_owner_assignments"] = (Reader, h => "{\"state\":\"all\"}"),
        ["get_ai_activity"] = (Owner, h => { Scenarios.Intake(h); return "{}"; }),
        ["get_measures"] = (Reader, h => { Scenarios.Deployment(h); Scenarios.SubmittedRetirement(h); return "{\"measureKey\":\"owner-coverage\"}"; }),
        ["list_agents"] = (Reader, h => "{}"),
        ["save_portfolio"] = (McpHarness.TokenAdminWeb, h => "{\"name\":\"Fixture operations\",\"levelKey\":\"portfolio\",\"parentPortfolioId\":\"" + McpHarness.Portfolio + "\"}"),
        ["check_duplicates"] = (Owner, h => "{\"text\":\"expense approvals for the finance team\"}"),
        ["get_tier_questions"] = (Owner, h => "{\"applicationKindKey\":\"agent\"}"),
        ["save_intake_draft"] = (Owner, h => IntakeArguments()),
        ["answer_tier_questions"] = (Owner, h => "{\"intakeRequestId\":\"" + Scenarios.Intake(h) + "\",\"answers\":" + Scenarios.TierAnswers + "}"),
        ["save_application_draft"] = (Owner, h => "{\"name\":\"Fixture leave tracker\",\"purpose\":\"Track team leave\",\"applicationKindKey\":\"power-platform-app\",\"portfolioId\":\"" + McpHarness.Portfolio
            + "\",\"proposedOwners\":[{\"ownerRoleKey\":\"business-owner\",\"objectId\":\"" + McpHarness.Owner + "\"}],\"components\":[{\"componentKindKey\":\"canvas-app\",\"externalId\":\"00000000-0000-0000-0000-000000000109\",\"url\":\"https://make.powerapps.com/environments/fixture/apps/109\"}]}"),
        ["link_component"] = (Owner, h => "{\"businessApplicationId\":\"" + McpHarness.Expense + "\",\"componentKindKey\":\"cloud-flow\",\"externalId\":\"flow-1\",\"url\":\"https://make.powerautomate.com/environments/fixture/flows/1\"}"),
        ["propose_owner_change"] = (Owner, h => "{\"businessApplicationId\":\"" + McpHarness.Expense + "\",\"changes\":[{\"ownerRoleKey\":\"data-owner\",\"addObjectId\":\"" + McpHarness.Deputy + "\"}],\"reason\":\"Name a data owner\"}"),
        ["record_owner_leave"] = (Owner, h => "{\"principalObjectId\":\"" + McpHarness.Owner + "\",\"leavesOn\":\"2026-12-31\",\"kind\":\"leave\"}"),
        ["list_intake_requests"] = (Owner, h => { Scenarios.SubmittedIntake(h); return "{\"view\":\"triage\"}"; }),
        ["get_intake_request"] = (Owner, h => "{\"intakeRequestId\":\"" + Scenarios.SubmittedIntake(h) + "\"}"),
        ["get_tier_assessment"] = (Owner, h => "{\"intakeRequestId\":\"" + Scenarios.SubmittedIntake(h) + "\"}"),
        ["save_dependency"] = (Owner, h => "{\"fromApplicationId\":\"" + McpHarness.Expense + "\",\"toApplicationId\":\"" + McpHarness.Travel + "\",\"dependencyTypeKey\":\"reads-data-from\",\"criticalityKey\":\"high\"}"),
        ["score_intake_request"] = (Admin, h => "{\"intakeRequestId\":\"" + Scenarios.SubmittedIntake(h) + "\",\"score\":72,\"sizeBandKey\":\"medium\",\"horizonKey\":\"now\"}"),
        ["save_decision_proposal"] = (Owner, h => "{\"decisionTypeKey\":\"retirement\",\"subjectKind\":\"business-application\",\"subjectId\":\"" + McpHarness.Travel
            + "\",\"statement\":\"Retire the travel planner\",\"context\":\"The travel suite replaces it\",\"proposedOutcomeKey\":\"complete\",\"reason\":\"A duplicate\",\"evidence\":[{\"linkKindKey\":\"evidence-url\",\"url\":\"https://example.org/evidence/1\",\"label\":\"Usage report\"}]}"),
        ["submit_record"] = (Owner, h => "{\"subjectKind\":\"intake-request\",\"subjectId\":\"" + Scenarios.Intake(h) + "\"}"),
        ["withdraw_record"] = (Owner, h => "{\"subjectKind\":\"intake-request\",\"subjectId\":\"" + Scenarios.Intake(h) + "\",\"reason\":\"No longer needed\"}"),
        ["get_decision_log"] = (Reader, h => "{}"),
        ["get_approval_status"] = (Owner, h => "{\"subjectKind\":\"intake-request\",\"subjectId\":\"" + Scenarios.SubmittedIntake(h) + "\"}"),
        ["request_architecture_review"] = (Owner, h => "{\"businessApplicationId\":\"" + McpHarness.Expense + "\",\"reason\":\"A new integration with the ledger\"}"),
        ["answer_approval"] = (McpHarness.TokenOwnerWeb, h => "{\"approvalRecordId\":\"" + Scenarios.PendingRetirementApproval(h) + "\",\"answer\":\"approve\"}"),
        ["complete_review"] = (McpHarness.TokenOwnerWeb, h => "{\"reviewId\":\"" + Scenarios.ReviewWithAcceptedOutcome(h) + "\",\"evidence\":[{\"linkKindKey\":\"evidence-url\",\"url\":\"https://example.org/evidence/adr-2\",\"label\":\"ADR 2\"}]}"),
        ["get_decision"] = (Reader, h => "{\"decisionId\":\"" + Scenarios.SubmittedRetirement(h) + "\"}"),
        ["list_approvals"] = (Owner, h => { Scenarios.PendingRetirementApproval(h); return "{\"view\":\"mine\"}"; }),
        ["get_audit_trail"] = (Admin, h => { Scenarios.SubmittedRetirement(h); return "{}"; }),
        ["request_security_review"] = (Owner, h => "{\"businessApplicationId\":\"" + McpHarness.Expense + "\",\"reason\":\"A new connector to the ledger\"}"),
        ["renew_approval_request"] = (McpHarness.TokenOwnerWeb, h => "{\"approvalRecordId\":\"" + Scenarios.ExpiringRetirementApproval(h, -1) + "\"}"),
        ["get_evidence"] = (Reader, h => { Scenarios.SubmittedRetirement(h); Scenarios.Deployment(h); return "{\"businessApplicationId\":\"" + McpHarness.Expense + "\"}"; }),
        ["save_change_request_draft"] = (Owner, h => Scenarios.ChangeArguments()),
        ["check_standard_change"] = (Owner, h => "{\"changeRequestId\":\"" + Scenarios.Change(h) + "\"}"),
        ["get_change_request"] = (Reader, h => "{\"changeRequestId\":\"" + Scenarios.Change(h) + "\"}"),
        ["get_change_schedule"] = (Reader, h => { Scenarios.Change(h); return "{\"from\":\"2026-11-27\",\"to\":\"2026-11-29\"}"; }),
        ["record_change_result"] = (Owner, h => "{\"changeRequestId\":\"" + Scenarios.Change(h) + "\",\"resultKey\":\"failed\"}"),
        ["complete_change"] = (McpHarness.TokenOwnerWeb, h => "{\"changeRequestId\":\"" + Scenarios.ScheduledChange(h) + "\",\"resultKey\":\"succeeded\"}"),
        ["record_deployment"] = (Owner, h => Scenarios.DeploymentArguments("run-1")),
        ["record_deployment_correction"] = (Owner, h => "{\"correctsDeploymentRecordId\":\"" + Scenarios.Deployment(h) + "\",\"idempotencyKey\":\"run-1-fix\",\"reason\":\"The version was wrong\",\"artifactVersion\":\"1.0.1\"}"),
        ["get_deployments"] = (Reader, h => { Scenarios.Deployment(h); return "{}"; }),
        ["map_pipeline"] = (Owner, h => Scenarios.PipelineArguments()),
        ["get_pipeline_map"] = (Reader, h => { Scenarios.Pipeline(h); return "{\"businessApplicationId\":\"" + McpHarness.Expense + "\"}"; }),
        ["list_deployment_targets"] = (Reader, h => { Scenarios.Deployment(h); Scenarios.Pipeline(h); return "{}"; }),
        ["save_deployment_target"] = (McpHarness.TokenAdminWeb, h => Scenarios.TargetArguments()),
        ["get_documentation_status"] = (Owner, h => "{\"businessApplicationId\":\"" + McpHarness.Expense + "\"}"),
        ["record_document_link"] = (Owner, h => "{\"businessApplicationId\":\"" + McpHarness.Expense + "\",\"documentTypeKey\":\"runbook\",\"url\":\"https://devops.example.org/fixture/wiki/expense-runbook\",\"statusMeaning\":\"current\"}"),
        ["list_documents"] = (Reader, h => "{\"state\":\"all\"}"),
        ["confirm_document_checked"] = (Owner, h => "{\"documentLinkId\":\"" + McpHarness.WikiLink + "\",\"stillTrue\":true}"),
        ["run_gap_check"] = (Admin, h => "{}"),
        ["compare_inventory_list"] = (Admin, h => Scenarios.InventoryArguments()),
        ["run_owner_check"] = (Admin, h => { h.Directory.States[McpHarness.Deputy] = new Backend.DirectoryUserState("disabled"); return "{}"; }),
        ["run_due_state_refresh"] = (McpHarness.TokenAdminWeb, h => "{}"),
        ["list_claims"] = (Admin, h => { Scenarios.Claim(h); return "{}"; }),
        ["list_gap_check_runs"] = (Admin, h => { Scenarios.Claim(h); return "{}"; }),
        ["claim_resource"] = (Owner, h => "{\"claimId\":\"" + Scenarios.Claim(h) + "\",\"businessApplicationId\":\"" + McpHarness.Expense + "\"}"),
        ["dismiss_claim"] = (Admin, h => "{\"claimId\":\"" + Scenarios.Claim(h) + "\",\"reason\":\"A personal test app\"}"),
        ["get_configuration"] = (Reader, h => "{\"entity\":\"tier\"}"),
        ["explain_rules"] = (Owner, h => "{\"subjectKind\":\"intake-request\",\"subjectId\":\"" + Scenarios.SubmittedIntake(h) + "\"}"),
        ["get_configuration_health"] = (Reader, h => "{}"),
        ["list_presets"] = (Reader, h => "{}"),
        ["propose_configuration_change"] = (McpHarness.TokenConfigAdmin, h => Scenarios.ProposalArguments()),
        ["export_configuration"] = (Admin, h => "{}"),
        ["check_role_bindings"] = (Admin, h => "{}"),
        ["list_configuration_changes"] = (Reader, h => { h.Ok(McpHarness.TokenConfigAdmin, "propose_configuration_change", Scenarios.ProposalArguments()); return "{}"; }),
        ["get_configuration_draft"] = (Reader, h => "{\"configurationDraftId\":\"" + McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenConfigAdmin, "propose_configuration_change", Scenarios.ProposalArguments())), "configurationDraftId") + "\"}"),
        ["get_role_access"] = (Reader, h => "{\"roleKey\":\"change-manager\"}"),
        ["list_guidelines"] = (Reader, h => "{}"),
        ["get_admin_status"] = (Admin, h => { Scenarios.Intake(h); return "{}"; }),
        ["retry_approval_card"] = (McpHarness.TokenAdminWeb, h => "{\"approvalRecordId\":\"" + Scenarios.FailedCard(h) + "\"}"),
        ["import_data_taxonomy"] = (McpHarness.TokenConfigAdmin, h => "{\"csv\":\"key,label,parent,classification,ai_rule\\nfixture-people,Fixture people,,confidential,\\nfixture-payroll,Fixture payroll,fixture-people,restricted,never\\n\",\"proposalRationale\":\"Load the fixture domain list\"}"),
        ["save_link"] = (Owner, h => "{\"linkCategory\":\"delivery\",\"subjectKind\":\"business-application\",\"subjectId\":\"" + McpHarness.Expense
            + "\",\"linkKindKey\":\"ado-work-item\",\"externalId\":\"4711\",\"url\":\"https://devops.example.org/fixture/project/_workitems/edit/4711\",\"label\":\"Work item 4711\"}"),

        // The data toolset (data taxonomy slice 3) runs in the data world (DataWorld).
        ["find_data_domains"] = (Reader, h => "{\"text\":\"pay\"}"),
        ["get_data_domain"] = (Reader, h => "{\"domainKey\":\"payroll\"}"),
        ["list_data_uses"] = (Reader, h => "{\"domainKey\":\"expenses\"}"),
        ["list_data_mappings"] = (Reader, h => "{}"),
        ["find_data_locations"] = (Reader, h => "{}"),
        ["resolve_data"] = (Owner, h => "{\"externalId\":\"PAYLIB\",\"container\":\"PAYMAST\"}"),
        ["check_data_use"] = (Owner, h => "{\"applicationId\":\"" + McpHarness.Expense + "\",\"domainKey\":\"expenses\",\"access\":\"write\",\"reachesAi\":true}"),
        ["get_data_gaps"] = (Admin, h => "{}"),
        ["save_data_use"] = (Owner, h => DataWorld.Use("travel-bookings", reachesAi: true)),
        ["save_data_location"] = (Admin, h => "{\"items\":[{\"platformKey\":\"azure-sql\",\"targetStageKey\":\"production\",\"externalId\":\"fixture-sql-01\",\"labelAsSeen\":\"Fixture SQL server\"}]}"),
        ["save_data_mapping"] = (Admin, h => "{\"items\":[{\"domainKey\":\"travel-bookings\",\"locationId\":\"" + DataWorld.FinanceProd + "\",\"containers\":[\"fx_trip\"],\"origin\":\"system-of-record\"}]}"),
        ["move_data_references"] = (Admin, h => "{\"fromDomainKey\":\"expenses\",\"toDomainKey\":\"travel-bookings\",\"include\":[\"mappings\",\"uses\"],\"reason\":\"Fixture move\",\"validateOnly\":true}"),
    };

    // The R1 tools an AI client is served. A tool served only to a person (answer_approval, D-38; complete_review and
    // complete_change, B24-10) has its own case below.
    public static IEnumerable<object[]> R1Tools()
    {
        return ContractSet.Embedded.Tools.Where(t => t.IsR1 && !t.IsPersonOnly).Select(t => new object[] { t.Name });
    }

    public static IEnumerable<object[]> PersonOnlyTools()
    {
        return ContractSet.Embedded.Tools.Where(t => t.IsR1 && t.IsPersonOnly).Select(t => new object[] { t.Name });
    }

    [Fact]
    public void EveryR1ToolHasACaseAndAHandler()
    {
        var r1 = ContractSet.Embedded.Tools.Where(t => t.IsR1).Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.Equal(101, r1.Count);
        Assert.Equal(new[] { "answer_approval", "complete_change", "complete_review", "renew_approval_request", "retry_approval_card", "run_due_state_refresh", "save_deployment_target", "save_portfolio" }, ContractSet.Embedded.Tools.Where(t => t.IsPersonOnly).Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(r1, Cases.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList());
        Assert.Equal(r1, ToolHandlers.All.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList());
        Assert.False(ToolHandlers.All.ContainsKey("check_change_approval"));
    }

    [Theory]
    [MemberData(nameof(R1Tools))]
    public void EveryR1ToolAnswersWithItsContract(string tool)
    {
        McpHarness h = McpHarness.Create(withData: ContractSet.Embedded.Tools.Single(t => t.Name == tool).ToolsetKey == "data");
        (string token, Func<McpHarness, string> arguments) = Cases[tool];
        JsonTreeNode envelope = h.Ok(token, tool, arguments(h));

        // The envelope parts every tool carries (document 24, section 3.4).
        Assert.False(string.IsNullOrEmpty(Js.Text(envelope, "summary")));
        Assert.False(string.IsNullOrEmpty(Js.Text(envelope, "correlationId")));
        Assert.Equal(1, Js.Number(envelope, "configurationSetVersion"));
        Assert.Equal("2026-11-22T09:00:00Z", Js.Text(envelope, "asOf"));
        ToolContract contract = ContractSet.Embedded.Tools.Single(t => t.Name == tool);
        if (contract.IsWrite)
        {
            JsonTreeNode data = McpHarness.Data(envelope);
            Assert.NotNull(Js.Member(data, "performedBy"));
            // The caller's own row is stamped AI client for a person; a row only the engine writes (a leave date on a
            // Principal Reference) is stamped engine, still on behalf of the person.
            Assert.Contains(McpHarness.Text(data, "performedBy.actorKind"), new[] { "ai-client-for-person", "engine" });
            string person = token == Admin ? McpHarness.Admin : token == McpHarness.TokenConfigAdmin ? McpHarness.ConfigAdmin : McpHarness.Owner;
            Assert.Equal(person, McpHarness.Text(data, "performedBy.onBehalfOf.objectId"));
            AssertNeverApprovedOrAccepted(tool, data);
        }
    }

    // A tool served only to a person answers on the person entry with its contract, stamped as the person's own act, and
    // an AI client is refused it. answer_approval is the one write tool whose output may hold approved: the person's own
    // answer (D-38). complete_review and complete_change hold done and completed, which an AI actor never sets (G-08).
    [Theory]
    [MemberData(nameof(PersonOnlyTools))]
    public void APersonOnlyToolAnswersWithItsContractOnThePersonEntry(string tool)
    {
        McpHarness h = McpHarness.Create();
        (string token, Func<McpHarness, string> arguments) = Cases[tool];
        JsonTreeNode envelope = h.Ok(token, tool, arguments(h), server: h.PersonServer());
        JsonTreeNode data = McpHarness.Data(envelope);
        if (tool == "run_due_state_refresh")
        {
            // Not a row write: the run's report, every row of it the engine's on behalf of the admin (B24-17).
            Assert.Equal("person", McpHarness.Text(data, "trigger"));
            h.Fails(Owner, tool, arguments(McpHarness.Create()), McpErrorCodes.AiCannotDoThis);
            return;
        }

        if (tool == "retry_approval_card")
        {
            // Not an engine row: the host's outbox row, and the call's Audit Entry names who asked (B24-22).
            Assert.Equal("person", McpHarness.Text(data, "performedBy.actorKind"));
            Assert.Equal("sent-again", McpHarness.Text(data, "action"));
            h.Fails(Admin, tool, arguments(McpHarness.Create()), McpErrorCodes.AiCannotDoThis);
            return;
        }

        Assert.Equal("person", McpHarness.Text(data, "performedBy.actorKind"));
        Assert.Equal("null", McpHarness.Text(data, "performedBy.onBehalfOf"));
        // A person-only write of a row with no status (a target, a portfolio) answers status null.
        string expected = tool == "answer_approval" ? "approved" : tool == "complete_review" ? "done" : tool == "complete_change" ? "completed" : tool == "renew_approval_request" ? "pending" : "null";
        Assert.Equal(expected, expected == "null" ? McpHarness.Text(data, "status") : McpHarness.Text(data, "status.meaning"));
        h.Fails(Owner, tool, arguments(McpHarness.Create()), McpErrorCodes.AiCannotDoThis);
    }

    // No output of the write tools an AI client is served holds the value approved, accepted, active or activated.
    internal static void AssertNeverApprovedOrAccepted(string tool, JsonTreeNode node)
    {
        foreach ((string path, string meaning) in Meanings(node, string.Empty))
        {
            Assert.False(meaning == "approved" || meaning == "accepted" || meaning == "activated" || meaning == "active", tool + " returned " + meaning + " at " + path);
        }
    }

    private static IEnumerable<(string Path, string Meaning)> Meanings(JsonTreeNode node, string path)
    {
        if (node.Kind == JsonTreeKind.Object)
        {
            foreach (KeyValuePair<string, JsonTreeNode> member in node.Members)
            {
                if ((member.Key == "meaning" || member.Key == "statusMeaning") && member.Value.Kind == JsonTreeKind.String)
                {
                    yield return (path + "." + member.Key, member.Value.GetString());
                }

                foreach ((string, string) inner in Meanings(member.Value, path + "." + member.Key))
                {
                    yield return inner;
                }
            }
        }
        else if (node.Kind == JsonTreeKind.Array)
        {
            foreach (JsonTreeNode item in node.Items)
            {
                foreach ((string, string) inner in Meanings(item, path + "[]"))
                {
                    yield return inner;
                }
            }
        }
    }

    private static string IntakeArguments()
    {
        return "{\"title\":\"Fixture travel requests\",\"need\":\"Replace the shared spreadsheet\",\"proposedKindKey\":\"power-platform-app\"}";
    }
}

// Set-up steps the cases share. Each runs real tools as the owner or the admin.
internal static class Scenarios
{
    public const string TierAnswers = "[{\"questionKey\":\"data-change\",\"answerKey\":\"as-user\"},{\"questionKey\":\"sensitivity\",\"answerKey\":\"none\"},{\"questionKey\":\"reach\",\"answerKey\":\"small-team\"},{\"questionKey\":\"failure-impact\",\"answerKey\":\"little\"}]";

    public static string Intake(McpHarness h, string title = "Fixture travel requests", bool linked = true, string? businessOwner = null)
    {
        JsonTreeNode envelope = h.Ok(McpHarness.TokenOwner, "save_intake_draft", "{\"title\":\"" + title + "\",\"need\":\"Replace the shared spreadsheet\",\"proposedKindKey\":\"power-platform-app\""
            + (linked ? ",\"businessApplicationId\":\"" + McpHarness.Expense + "\"" : string.Empty)
            + (businessOwner == null ? string.Empty : ",\"businessOwnerObjectId\":\"" + businessOwner + "\"") + "}");
        return McpHarness.Text(McpHarness.Data(envelope), "recordId");
    }

    // A retirement of the travel planner the owner proposed through Claude Code and submitted: its one pending card, for
    // the business owner.
    public static string PendingRetirementApproval(McpHarness h)
    {
        string decision = SubmittedRetirement(h);
        return h.Store.Query(Product.Engine.Runtime.Ports.RowQuery.For("approval-record").Where("decision", decision).Where("outcomeMeaning", "pending")).Single().Id;
    }

    // The same retirement, proposed through Claude Code and submitted: its decision ID.
    public static string SubmittedRetirement(McpHarness h)
    {
        JsonTreeNode proposed = h.Ok(McpHarness.TokenOwner, "save_decision_proposal", "{\"decisionTypeKey\":\"retirement\",\"subjectKind\":\"business-application\",\"subjectId\":\"" + McpHarness.Travel
            + "\",\"statement\":\"Retire the travel planner\",\"context\":\"Replaced\",\"proposedOutcomeKey\":\"complete\",\"reason\":\"A duplicate\"}");
        string decision = McpHarness.Text(McpHarness.Data(proposed), "recordId");
        h.Ok(McpHarness.TokenOwner, "submit_record", "{\"subjectKind\":\"decision\",\"subjectId\":\"" + decision + "\"}");
        return decision;
    }

    // An architecture review of the expense app, requested by its owner: its ID.
    public static string ArchitectureReview(McpHarness h)
    {
        JsonTreeNode envelope = h.Ok(McpHarness.TokenOwner, "request_architecture_review", "{\"businessApplicationId\":\"" + McpHarness.Expense + "\",\"reason\":\"A new integration with the ledger\",\"dueOn\":\"2026-11-30\"}");
        return McpHarness.Text(McpHarness.Data(envelope), "reviewId");
    }

    // An architecture review of the expense app whose design decision is accepted (seeded, as the engine would have
    // written it after the architect's answer): ready to complete.
    public static string ReviewWithAcceptedOutcome(McpHarness h)
    {
        const string decision = "00000000-0000-4000-8000-000000000611";
        const string review = "00000000-0000-4000-8000-000000000612";
        h.Store.Seed("decision", "{\"decisionType\":\"design\",\"subjectKind\":\"business-application\",\"subjectApplication\":\"" + McpHarness.Expense
            + "\",\"statement\":\"Call the ledger through its published API\",\"outcome\":\"approve\",\"status\":\"decision-accepted\",\"statusMeaning\":\"accepted\",\"decidedOn\":\"2026-11-21\"}", id: decision);
        h.Store.Seed("review", "{\"businessApplication\":\"" + McpHarness.Expense + "\",\"reviewType\":\"architecture\",\"status\":\"review-due\",\"statusMeaning\":\"due\",\"trigger\":\"request\",\"dueOn\":\"2026-11-30\",\"outcomeDecision\":\"" + decision + "\"}", id: review);
        return review;
    }

    // A normal change of the expense app whose authorization is accepted: in progress (Balanced: Scheduled), seeded as the
    // update-subject effect leaves it.
    public static string ScheduledChange(McpHarness h)
    {
        const string change = "00000000-0000-4000-8000-000000000613";
        h.Store.Seed("change-request", "{\"title\":\"Add an approval step\",\"businessApplication\":\"" + McpHarness.Expense + "\",\"changeType\":\"normal\",\"description\":\"Adds a manager step\","
            + "\"mode\":\"built-in\",\"windowStart\":\"2026-11-21T06:00:00Z\",\"windowEnd\":\"2026-11-21T08:00:00Z\",\"status\":\"change-in-progress\",\"statusMeaning\":\"in-progress\"}", id: change);
        return change;
    }

    public static string SubmittedIntake(McpHarness h, bool linked = true)
    {
        string intake = Intake(h, linked: linked);
        h.Ok(McpHarness.TokenOwner, "answer_tier_questions", "{\"intakeRequestId\":\"" + intake + "\",\"answers\":" + TierAnswers + "}");
        h.Ok(McpHarness.TokenOwner, "submit_record", "{\"subjectKind\":\"intake-request\",\"subjectId\":\"" + intake + "\"}");
        return intake;
    }

    public static string ChangeArguments()
    {
        return "{\"title\":\"Add an approval step\",\"businessApplicationId\":\"" + McpHarness.Expense + "\",\"changeTypeKey\":\"normal\",\"description\":\"Adds a manager step to the flow\","
            + "\"windowStart\":\"2026-11-28T06:00:00Z\",\"windowEnd\":\"2026-11-28T08:00:00Z\",\"plannedTargetId\":\"" + McpHarness.Production + "\"}";
    }

    public static string Change(McpHarness h)
    {
        return McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenOwner, "save_change_request_draft", ChangeArguments())), "recordId");
    }

    public static string DeploymentArguments(string key, string target = McpHarness.TestTarget)
    {
        return "{\"idempotencyKey\":\"" + key + "\",\"businessApplicationId\":\"" + McpHarness.Expense + "\",\"artifactName\":\"Expense solution\",\"artifactVersion\":\"1.0.0\","
            + "\"deploymentTargetId\":\"" + target + "\",\"finishedOn\":\"2026-11-22T08:30:00Z\",\"resultMeaning\":\"succeeded\"}";
    }

    public static string Deployment(McpHarness h)
    {
        return McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenOwner, "record_deployment", DeploymentArguments("run-1"))), "deploymentRecordId");
    }

    public static string PipelineArguments()
    {
        return "{\"linkKindKey\":\"azure-pipelines-pipeline\",\"externalId\":\"pipeline-7\",\"url\":\"https://devops.example.org/fixture/project/_build?definitionId=7\","
            + "\"entries\":[{\"businessApplicationId\":\"" + McpHarness.Expense + "\",\"deploymentTargetId\":\"" + McpHarness.Production + "\",\"stageOrder\":1}]}";
    }

    // A new production target, registered by the platform admin in the web app.
    public static string TargetArguments()
    {
        return "{\"targetKindKey\":\"power-platform-environment\",\"targetStageKey\":\"production\",\"changePathKey\":\"pipeline-only\",\"linkKindKey\":\"power-platform-resource\","
            + "\"externalId\":\"fixture-env-ops\",\"labelAsSeen\":\"Fixture operations production\"}";
    }

    public static string Pipeline(McpHarness h)
    {
        return McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenOwner, "map_pipeline", PipelineArguments())), "pipelineReferenceId");
    }

    public static string InventoryArguments()
    {
        return "{\"estateKey\":\"power-platform-inventory\",\"items\":[{\"externalId\":\"00000000-0000-0000-0000-000000000103\",\"componentKindKey\":\"canvas-app\",\"sourceType\":\"microsoft.powerapps/canvasapps\","
            + "\"labelAsSeen\":\"Holiday planner\",\"suggestedClaimantObjectId\":\"" + McpHarness.Owner + "\"}],\"sourceNote\":\"An export from the admin's own tool\"}";
    }

    public static string Claim(McpHarness h)
    {
        JsonTreeNode envelope = h.Ok(McpHarness.TokenAdmin, "compare_inventory_list", InventoryArguments());
        return McpHarness.Text(Js.Items(McpHarness.Data(envelope), "newClaims")[0], "claimId");
    }

    // The retirement's pending request with its Expires On moved to the harness date plus days (negative: it expired). The
    // Balanced retirement rule sets no timeout, so the request is given one as a rule with Timeout Days would.
    public static string ExpiringRetirementApproval(McpHarness h, int days)
    {
        string record = PendingRetirementApproval(h);
        Product.Engine.Runtime.Ports.StoredRow row = h.Store.Get("approval-record", record)!;
        h.Store.Tamper("approval-record", record, Js.With(row.Columns, ("expiresOn", JsonTreeNode.FromString(McpHarness.AsOf.AddDays(days).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture)))));
        return record;
    }

    // The harness host as a demo install that holds the demo estate (get_demo_freshness; B24-22).
    public static void DemoHost(McpHarness h)
    {
        h.Backend.Options.HostFacts = () => new HostFacts { EnvironmentName = "Demo", IsDemo = true, HoldsDemoEstate = true };
    }

    // The retirement's pending request, its card failed in the harness's outbox: the runner gave it back as often as it may.
    public static string FailedCard(McpHarness h)
    {
        string record = PendingRetirementApproval(h);
        h.Outbox.Rows[record] = new OutboxAttention(record, h.Store.Get("approval-record", record)!.Text("decision")!, "failed", 5, McpHarness.AsOf, McpHarness.AsOf);
        return record;
    }

    public static string ProposalArguments()
    {
        return "{\"baseVersionNumber\":1,\"proposalRationale\":\"Lower the largest page\",\"operations\":[{\"op\":\"upsert\",\"entity\":\"setting\",\"key\":\"mcp-page-size-max\",\"values\":{\"value\":50}}]}";
    }
}
