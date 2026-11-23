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
        ["find_applications"] = (Reader, h => "{\"text\":\"expense\"}"),
        ["get_application_owners"] = (Reader, h => "{\"businessApplicationId\":\"" + McpHarness.Expense + "\"}"),
        ["get_application_card"] = (Reader, h => "{\"businessApplicationId\":\"" + McpHarness.Expense + "\"}"),
        ["get_dependency_impact"] = (Reader, h => "{\"businessApplicationId\":\"" + McpHarness.Expense + "\",\"direction\":\"dependants\"}"),
        ["get_portfolio_summary"] = (Reader, h => "{\"includeOwnerGaps\":true}"),
        ["get_requirements"] = (Reader, h => "{\"businessApplicationId\":\"" + McpHarness.Expense + "\"}"),
        ["check_duplicates"] = (Owner, h => "{\"text\":\"expense approvals for the finance team\"}"),
        ["get_tier_questions"] = (Owner, h => "{\"applicationKindKey\":\"agent\"}"),
        ["save_intake_draft"] = (Owner, h => IntakeArguments()),
        ["answer_tier_questions"] = (Owner, h => "{\"intakeRequestId\":\"" + Scenarios.Intake(h) + "\",\"answers\":" + Scenarios.TierAnswers + "}"),
        ["save_application_draft"] = (Owner, h => "{\"name\":\"Fixture leave tracker\",\"purpose\":\"Track team leave\",\"applicationKindKey\":\"power-platform-app\",\"portfolioId\":\"" + McpHarness.Portfolio
            + "\",\"proposedOwners\":[{\"ownerRoleKey\":\"business-owner\",\"objectId\":\"" + McpHarness.Owner + "\"}],\"components\":[{\"componentKindKey\":\"canvas-app\",\"externalId\":\"00000000-0000-0000-0000-000000000109\",\"url\":\"https://make.powerapps.com/environments/fixture/apps/109\"}]}"),
        ["link_component"] = (Owner, h => "{\"businessApplicationId\":\"" + McpHarness.Expense + "\",\"componentKindKey\":\"cloud-flow\",\"externalId\":\"flow-1\",\"url\":\"https://make.powerautomate.com/environments/fixture/flows/1\"}"),
        ["propose_owner_change"] = (Owner, h => "{\"businessApplicationId\":\"" + McpHarness.Expense + "\",\"changes\":[{\"ownerRoleKey\":\"data-owner\",\"addObjectId\":\"" + McpHarness.Deputy + "\"}],\"reason\":\"Name a data owner\"}"),
        ["record_owner_leave"] = (Owner, h => "{\"principalObjectId\":\"" + McpHarness.Owner + "\",\"leavesOn\":\"2026-12-31\",\"kind\":\"leave\"}"),
        ["save_decision_proposal"] = (Owner, h => "{\"decisionTypeKey\":\"retirement\",\"subjectKind\":\"business-application\",\"subjectId\":\"" + McpHarness.Travel
            + "\",\"statement\":\"Retire the travel planner\",\"context\":\"The travel suite replaces it\",\"proposedOutcomeKey\":\"retire\",\"reason\":\"A duplicate\",\"evidence\":[{\"linkKindKey\":\"evidence-url\",\"url\":\"https://example.org/evidence/1\",\"label\":\"Usage report\"}]}"),
        ["submit_record"] = (Owner, h => "{\"subjectKind\":\"intake-request\",\"subjectId\":\"" + Scenarios.Intake(h) + "\"}"),
        ["withdraw_record"] = (Owner, h => "{\"subjectKind\":\"intake-request\",\"subjectId\":\"" + Scenarios.Intake(h) + "\",\"reason\":\"No longer needed\"}"),
        ["get_decision_log"] = (Reader, h => "{}"),
        ["get_approval_status"] = (Owner, h => "{\"subjectKind\":\"intake-request\",\"subjectId\":\"" + Scenarios.SubmittedIntake(h) + "\"}"),
        ["request_architecture_review"] = (Owner, h => "{\"businessApplicationId\":\"" + McpHarness.Expense + "\",\"reason\":\"A new integration with the ledger\"}"),
        ["save_change_request_draft"] = (Owner, h => Scenarios.ChangeArguments()),
        ["check_standard_change"] = (Owner, h => "{\"changeRequestId\":\"" + Scenarios.Change(h) + "\"}"),
        ["get_change_request"] = (Reader, h => "{\"changeRequestId\":\"" + Scenarios.Change(h) + "\"}"),
        ["get_change_schedule"] = (Reader, h => { Scenarios.Change(h); return "{\"from\":\"2026-11-27\",\"to\":\"2026-11-29\"}"; }),
        ["record_change_result"] = (Owner, h => "{\"changeRequestId\":\"" + Scenarios.Change(h) + "\",\"resultKey\":\"failed\"}"),
        ["record_deployment"] = (Owner, h => Scenarios.DeploymentArguments("run-1")),
        ["record_deployment_correction"] = (Owner, h => "{\"correctsDeploymentRecordId\":\"" + Scenarios.Deployment(h) + "\",\"idempotencyKey\":\"run-1-fix\",\"reason\":\"The version was wrong\",\"artifactVersion\":\"1.0.1\"}"),
        ["get_deployments"] = (Reader, h => { Scenarios.Deployment(h); return "{}"; }),
        ["map_pipeline"] = (Owner, h => Scenarios.PipelineArguments()),
        ["get_pipeline_map"] = (Reader, h => { Scenarios.Pipeline(h); return "{\"businessApplicationId\":\"" + McpHarness.Expense + "\"}"; }),
        ["get_documentation_status"] = (Owner, h => "{\"businessApplicationId\":\"" + McpHarness.Expense + "\"}"),
        ["record_document_link"] = (Owner, h => "{\"businessApplicationId\":\"" + McpHarness.Expense + "\",\"documentTypeKey\":\"runbook\",\"url\":\"https://devops.example.org/fixture/wiki/expense-runbook\",\"statusMeaning\":\"current\"}"),
        ["confirm_document_checked"] = (Owner, h => "{\"documentLinkId\":\"" + McpHarness.WikiLink + "\",\"stillTrue\":true}"),
        ["run_gap_check"] = (Admin, h => "{}"),
        ["compare_inventory_list"] = (Admin, h => Scenarios.InventoryArguments()),
        ["run_owner_check"] = (Admin, h => { h.Directory.States[McpHarness.Deputy] = new Backend.DirectoryUserState("disabled"); return "{}"; }),
        ["list_claims"] = (Admin, h => { Scenarios.Claim(h); return "{}"; }),
        ["claim_resource"] = (Owner, h => "{\"claimId\":\"" + Scenarios.Claim(h) + "\",\"businessApplicationId\":\"" + McpHarness.Expense + "\"}"),
        ["dismiss_claim"] = (Admin, h => "{\"claimId\":\"" + Scenarios.Claim(h) + "\",\"reason\":\"A personal test app\"}"),
        ["get_configuration"] = (Reader, h => "{\"entity\":\"tier\"}"),
        ["explain_rules"] = (Owner, h => "{\"subjectKind\":\"intake-request\",\"subjectId\":\"" + Scenarios.SubmittedIntake(h) + "\"}"),
        ["get_configuration_health"] = (Reader, h => "{}"),
        ["list_presets"] = (Reader, h => "{}"),
        ["propose_configuration_change"] = (McpHarness.TokenConfigAdmin, h => Scenarios.ProposalArguments()),
        ["export_configuration"] = (Admin, h => "{}"),
        ["check_role_bindings"] = (Admin, h => "{}"),
        ["save_link"] = (Owner, h => "{\"linkCategory\":\"delivery\",\"subjectKind\":\"business-application\",\"subjectId\":\"" + McpHarness.Expense
            + "\",\"linkKindKey\":\"ado-work-item\",\"externalId\":\"4711\",\"url\":\"https://devops.example.org/fixture/project/_workitems/edit/4711\",\"label\":\"Work item 4711\"}"),
    };

    public static IEnumerable<object[]> R1Tools()
    {
        return ContractSet.Embedded.Tools.Where(t => t.IsR1).Select(t => new object[] { t.Name });
    }

    [Fact]
    public void EveryR1ToolHasACaseAndAHandler()
    {
        var r1 = ContractSet.Embedded.Tools.Where(t => t.IsR1).Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.Equal(50, r1.Count);
        Assert.Equal(r1, Cases.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList());
        Assert.Equal(r1, ToolHandlers.All.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList());
        Assert.False(ToolHandlers.All.ContainsKey("check_change_approval"));
    }

    [Theory]
    [MemberData(nameof(R1Tools))]
    public void EveryR1ToolAnswersWithItsContract(string tool)
    {
        McpHarness h = McpHarness.Create();
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

    // No output of the write tools holds the value approved, accepted, active or activated, whatever the tool.
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

    public static string Intake(McpHarness h, string title = "Fixture travel requests")
    {
        JsonTreeNode envelope = h.Ok(McpHarness.TokenOwner, "save_intake_draft", "{\"title\":\"" + title + "\",\"need\":\"Replace the shared spreadsheet\",\"proposedKindKey\":\"power-platform-app\",\"businessApplicationId\":\"" + McpHarness.Expense + "\"}");
        return McpHarness.Text(McpHarness.Data(envelope), "recordId");
    }

    public static string SubmittedIntake(McpHarness h)
    {
        string intake = Intake(h);
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

    public static string ProposalArguments()
    {
        return "{\"baseVersionNumber\":1,\"proposalRationale\":\"Lower the largest page\",\"operations\":[{\"op\":\"upsert\",\"entity\":\"setting\",\"key\":\"mcp-page-size-max\",\"values\":{\"value\":50}}]}";
    }
}
