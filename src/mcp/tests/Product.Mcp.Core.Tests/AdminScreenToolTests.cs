using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Json;
using Product.Mcp.Core.Protocol;
using Xunit;

namespace Product.Mcp.Core.Tests;

// The reads and writes of the screens still missing (document 24, B24-18): the Configuration page's versions, drafts and
// draft record; Roles and access; Guidelines and the guideline chips; Measures and slide-back signals; the Admin page;
// retiring a deployment target; the agents and a security review request; and the AI activity's clients view.
public sealed class AdminScreenToolTests
{
    private const string Agent = "00000000-0000-4000-8000-000000000513";

    // ---- 87 and 88: the configuration versions, drafts and one draft ----

    [Fact]
    public void TheConfigurationPageListsVersionsAndDraftsAndReadsOneDraftWithItsDiffAndImpact()
    {
        McpHarness h = McpHarness.Create();
        string draft = McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenConfigAdmin, "propose_configuration_change", Scenarios.ProposalArguments())), "configurationDraftId");

        JsonTreeNode list = McpHarness.Data(h.Ok(McpHarness.TokenReader, "list_configuration_changes", "{}"));
        Assert.Equal("1", McpHarness.Text(list, "configurationSet.activeVersion"));
        JsonTreeNode version = Assert.Single(Js.Items(list, "versions"));
        Assert.Equal("true", McpHarness.Text(version, "isActive"));
        JsonTreeNode item = Assert.Single(Js.Items(list, "drafts"));
        Assert.Equal(draft, McpHarness.Text(item, "configurationDraftId"));
        Assert.Equal("draft", McpHarness.Text(item, "state.key"));
        Assert.Equal("true", McpHarness.Text(item, "isAiCreated"));
        Assert.Equal("1", McpHarness.Text(list, "counts.openDrafts"));

        JsonTreeNode envelope = h.Ok(McpHarness.TokenReader, "get_configuration_draft", "{\"configurationDraftId\":\"" + draft + "\"}");
        JsonTreeNode read = McpHarness.Data(envelope);
        JsonTreeNode diff = Assert.Single(Js.Items(read, "diff"));
        Assert.Equal("setting", McpHarness.Text(diff, "entity"));
        Assert.Equal("mcp-page-size-max", McpHarness.Text(diff, "key"));
        Assert.Equal("updated", McpHarness.Text(diff, "op"));
        Assert.Equal("Lower the largest page", McpHarness.Text(read, "proposalRationale"));
        Assert.Equal("false", McpHarness.Text(read, "baseMoved"));
        Assert.Equal("null", McpHarness.Text(read, "decision"));
        Assert.Equal(new[] { "submit_record", "withdraw_record" }, Js.Items(envelope, "nextSteps").Select(s => Js.Text(s, "tool")).ToArray());

        // Submitted, the draft carries its decision; withdrawn, it is discarded and no longer open.
        h.Ok(McpHarness.TokenConfigAdmin, "submit_record", "{\"subjectKind\":\"configuration-draft\",\"subjectId\":\"" + draft + "\"}");
        JsonTreeNode submitted = McpHarness.Data(h.Ok(McpHarness.TokenReader, "get_configuration_draft", "{\"configurationDraftId\":\"" + draft + "\"}"));
        Assert.Equal("submitted", McpHarness.Text(submitted, "draft.state.key"));
        Assert.Equal("proposed", McpHarness.Text(submitted, "decision.status.meaning"));
        h.Ok(McpHarness.TokenConfigAdmin, "withdraw_record", "{\"subjectKind\":\"configuration-draft\",\"subjectId\":\"" + draft + "\",\"reason\":\"Not now\"}");
        JsonTreeNode withdrawn = McpHarness.Data(h.Ok(McpHarness.TokenReader, "list_configuration_changes", "{\"view\":\"drafts\",\"draftStates\":[\"draft\",\"submitted\"]}"));
        Assert.Empty(Js.Items(withdrawn, "drafts"));
        Assert.Empty(Js.Items(withdrawn, "versions"));
        Assert.Equal("0", McpHarness.Text(withdrawn, "counts.openDrafts"));
    }

    // Fix round 1 (product review): a configuration change is named in plain words wherever its subject shows (the decision page,
    // the Approvals list, the decision log, record search and the Versions view), never by the engine's stored summary
    // with a key in backticks ("Changed Setting `mcp-page-size-max`: value.").
    [Fact]
    public void AConfigurationChangeIsNamedInPlainWordsNeverByARawKeyInBackticks()
    {
        McpHarness h = McpHarness.Create();
        string draft = McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenConfigAdmin, "propose_configuration_change", Scenarios.ProposalArguments())), "configurationDraftId");
        Assert.Contains("`", h.Store.Get("configuration-draft", draft)!.Text("changeSummary"));
        h.Ok(McpHarness.TokenConfigAdmin, "submit_record", "{\"subjectKind\":\"configuration-draft\",\"subjectId\":\"" + draft + "\"}");
        string decisionId = McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenReader, "get_configuration_draft", "{\"configurationDraftId\":\"" + draft + "\"}")), "decision.decisionId");

        JsonTreeNode decision = McpHarness.Data(h.Ok(McpHarness.TokenReader, "get_decision", "{\"decisionId\":\"" + decisionId + "\"}"));
        string subject = McpHarness.Text(decision, "subject.label");
        Assert.False(string.IsNullOrEmpty(subject));
        Assert.DoesNotContain("`", subject);
        Assert.False(subject.EndsWith(".", StringComparison.Ordinal), subject);
        foreach ((string token, string tool, string args) in new[]
        {
            (McpHarness.TokenReader, "get_decision", "{\"decisionId\":\"" + decisionId + "\"}"),
            (McpHarness.TokenReader, "get_decision_log", "{}"),
            (McpHarness.TokenReader, "list_configuration_changes", "{}"),
            (McpHarness.TokenAdmin, "list_approvals", "{\"view\":\"open\"}"),
            (McpHarness.TokenReader, "find_records", "{\"recordNumber\":\"" + McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenReader, "get_configuration_draft", "{\"configurationDraftId\":\"" + draft + "\"}")), "draft.recordNumber") + "\"}"),
        })
        {
            JsonTreeNode envelope = h.Ok(token, tool, args);
            foreach (string text in Named(McpHarness.Data(envelope)))
            {
                Assert.DoesNotContain("`", text);
            }
        }

        // The words a page shows for a subject or a version: labels, statements and change summaries.
        static IEnumerable<string> Named(JsonTreeNode node)
        {
            if (node.Kind == JsonTreeKind.Object)
            {
                foreach (KeyValuePair<string, JsonTreeNode> m in node.Members)
                {
                    if ((m.Key == "label" || m.Key == "statement" || m.Key == "changeSummary" || m.Key == "summary") && m.Value.Kind == JsonTreeKind.String)
                    {
                        yield return m.Value.GetString();
                    }

                    foreach (string inner in Named(m.Value))
                    {
                        yield return inner;
                    }
                }
            }
            else if (node.Kind == JsonTreeKind.Array)
            {
                foreach (JsonTreeNode item in node.Items)
                {
                    foreach (string inner in Named(item))
                    {
                        yield return inner;
                    }
                }
            }
        }
    }

    // ---- 89. get_role_access ----

    [Fact]
    public void RoleAccessSaysWhoHoldsARoleThroughWhichGroupAndWhatItMayDo()
    {
        McpHarness h = McpHarness.Create();
        JsonTreeNode data = McpHarness.Data(h.Ok(McpHarness.TokenReader, "get_role_access", "{\"roleKey\":\"change-manager\"}"));
        JsonTreeNode role = Assert.Single(Js.Items(data, "roles"));
        Assert.Equal("change-manager", McpHarness.Text(role, "role.key"));
        Assert.Equal(McpHarness.ChangeManagerGroup, McpHarness.Text(Assert.Single(Js.Items(role, "bindings")), "group.objectId"));
        JsonTreeNode holder = Assert.Single(Js.Items(role, "holders"));
        Assert.Equal(McpHarness.Owner, McpHarness.Text(holder, "objectId"));
        Assert.Contains(Js.Items(role, "tools"), t => McpHarness.Text(t, "tool") == "save_change_request_draft" && McpHarness.Text(t, "access") == "write");
        Assert.DoesNotContain(Js.Items(role, "tools"), t => McpHarness.Text(t, "tool") == "propose_configuration_change");
        Assert.Equal("entra-live", McpHarness.Text(data, "holdersReadFrom"));
        Assert.Contains(Js.Items(data, "separationRules"), r => McpHarness.Text(r, "separationRule.key") == "no-self-approval-tier-3" && McpHarness.Text(r, "words").Contains("tier 3", StringComparison.OrdinalIgnoreCase));

        // Every role, without holders by default; a workload role lists its grants, never a group.
        JsonTreeNode all = McpHarness.Data(h.Ok(McpHarness.TokenReader, "get_role_access", "{}"));
        Assert.All(Js.Items(all, "roles"), r => Assert.Equal("null", McpHarness.Text(r, "holders")));
        JsonTreeNode recorder = Js.Items(all, "roles").Single(r => McpHarness.Text(r, "role.key") == "deployment-recorder");
        Assert.Equal("true", McpHarness.Text(recorder, "isWorkloadRole"));
        Assert.Single(Js.Items(recorder, "workloadGrants"));
        Assert.Empty(Js.Items(recorder, "tools"));

        // Graph down: the holders are not read, and the read says so.
        h.Directory.Down = true;
        JsonTreeNode down = h.Ok(McpHarness.TokenReader, "get_role_access", "{\"roleKey\":\"change-manager\"}");
        Assert.Equal("null", McpHarness.Text(Js.Items(McpHarness.Data(down), "roles")[0], "holders"));
        Assert.Contains(Js.Items(down, "couldNotRead"), c => Js.Text(c, "kind") == "role-holders");
    }

    // ---- 90. list_guidelines, and the guideline chips ----

    [Fact]
    public void TheGuidelinesComeWithTheirChaptersAndTheChipsReachRequirementsAndRefusals()
    {
        McpHarness h = McpHarness.Create();
        JsonTreeNode data = McpHarness.Data(h.Ok(McpHarness.TokenReader, "list_guidelines", "{}"));
        Assert.Equal(24, Js.Items(data, "guidelines").Count);
        JsonTreeNode g17 = Js.Items(data, "guidelines").Single(g => McpHarness.Text(g, "number") == "G17");
        Assert.Equal("Let AI draft and recommend; people approve", McpHarness.Text(g17, "title"));
        Assert.Contains(Js.Items(g17, "chapters"), c => c.GetNumberText() == "11");
        Assert.Contains(Js.Items(Js.Member(g17, "usedBy"), "refusalCodePrefixes"), p => p.GetString() == "ai.");
        Assert.Equal("false", McpHarness.Text(Js.Items(data, "guidelines").Single(g => McpHarness.Text(g, "number") == "G10"), "isActive"));
        Assert.Equal(22, Js.Items(data, "chapters").Count);
        Assert.Contains(Js.Items(data, "tracks"), t => McpHarness.Text(t, "track") == "IT");
        Assert.Equal("null", McpHarness.Text(data, "hubBaseUrl"));

        JsonTreeNode one = McpHarness.Data(h.Ok(McpHarness.TokenReader, "list_guidelines", "{\"number\":\"G2\"}"));
        Assert.Single(Js.Items(one, "guidelines"));
        Assert.Empty(Js.Items(one, "chapters"));
        h.Fails(McpHarness.TokenReader, "list_guidelines", "{\"number\":\"G99\"}", McpErrorCodes.NotFound);

        // A requirement carries its guideline as a chip.
        JsonTreeNode requirements = McpHarness.Data(h.Ok(McpHarness.TokenReader, "get_requirements", "{\"businessApplicationId\":\"" + McpHarness.Travel + "\"}"));
        JsonTreeNode chip = Js.Items(requirements, "gates").SelectMany(g => Js.Items(g, "requirements")).Select(r => Js.Member(r, "guidelineRef")).First(r => r != null && r.Kind == JsonTreeKind.Object)!;
        Assert.StartsWith("G", McpHarness.Text(chip, "number"));
        Assert.False(string.IsNullOrEmpty(McpHarness.Text(chip, "title")));

        // A refusal an AI rule makes carries G17: an AI client is never served a person-only tool.
        JsonTreeNode refused = h.Fails(McpHarness.TokenAdmin, "save_deployment_target", Scenarios.TargetArguments(), McpErrorCodes.AiCannotDoThis);
        Assert.Equal("G17", McpHarness.Text(refused, "error.guideline"));
    }

    // ---- 91. get_measures ----

    [Fact]
    public void TheMeasuresAndSignalsComeFromTheRecordsAndDrillToTheRows()
    {
        McpHarness h = McpHarness.Create();
        h.Ok(McpHarness.TokenOwner, "record_deployment", Scenarios.DeploymentArguments("run-prod", McpHarness.Production));

        JsonTreeNode data = McpHarness.Data(h.Ok(McpHarness.TokenReader, "get_measures", "{}"));
        Assert.Equal(6, Js.Items(data, "windows").Count);
        Assert.Equal("2026-11-22", McpHarness.Text(Js.Items(data, "windows").Last(), "to"));
        var measures = Js.Items(data, "measures");
        Assert.Equal(new[] { "lead-time", "share-registered", "owner-coverage", "decisions-on-time", "standard-change-share", "deployments-on-record", "documents-current", "ai-work-accepted", "slide-back-signals" },
            measures.Select(m => McpHarness.Text(m, "key")).ToArray());
        Assert.All(measures, m => Assert.Equal("not-set", McpHarness.Text(m, "targetSource")));

        // The production deployment came through no pipeline the record names, so it is flagged: 0 of 1 on record.
        JsonTreeNode deployments = measures.Single(m => McpHarness.Text(m, "key") == "deployments-on-record");
        Assert.Equal("1", McpHarness.Text(deployments, "denominator"));
        Assert.Equal("pmo", McpHarness.Text(measures.Single(m => McpHarness.Text(m, "key") == "owner-coverage"), "watcher.key"));

        var signals = Js.Items(data, "signals");
        Assert.Equal(10, signals.Count);
        Assert.All(signals.Where(g => McpHarness.Text(g, "readBy") == "outside"), g => Assert.Equal("null", McpHarness.Text(g, "firing")));
        Assert.Equal(new[] { "2", "7" }, signals.Where(g => McpHarness.Text(g, "readBy") == "outside").Select(g => McpHarness.Text(g, "number")).ToArray());
        Assert.Equal("true", McpHarness.Text(signals.Single(g => McpHarness.Text(g, "number") == "3"), "firing"));

        // Owner coverage drills to the applications with a gap.
        JsonTreeNode owners = McpHarness.Data(h.Ok(McpHarness.TokenReader, "get_measures", "{\"measureKey\":\"owner-coverage\"}"));
        JsonTreeNode rows = Js.Member(owners, "rows")!;
        Assert.Equal("owner-coverage", McpHarness.Text(rows, "measureKey"));
        Assert.All(Js.Items(rows, "items"), r => Assert.Equal("business-application", McpHarness.Text(r, "entity")));
        JsonTreeNode signal = Js.Member(McpHarness.Data(h.Ok(McpHarness.TokenReader, "get_measures", "{\"signalNumber\":3}")), "rows")!;
        Assert.Equal("3", McpHarness.Text(signal, "signalNumber"));
        h.Fails(McpHarness.TokenReader, "get_measures", "{\"measureKey\":\"apps-shipped\"}", McpErrorCodes.Validation);
    }

    // ---- 92. get_admin_status ----

    [Fact]
    public void TheAdminPageReadsTheToolPolicyTheClientsAndTheNoticesByRole()
    {
        McpHarness h = McpHarness.Create();
        Scenarios.Intake(h);
        h.Ok(McpHarness.TokenAdminWeb, "run_due_state_refresh", "{}", server: h.PersonServer());

        JsonTreeNode envelope = h.Ok(McpHarness.TokenAdmin, "get_admin_status", "{}");
        JsonTreeNode data = McpHarness.Data(envelope);
        Assert.Equal("null", McpHarness.Text(data, "host"));
        Assert.Equal("1", McpHarness.Text(data, "configuration.version"));
        Assert.Equal("shipped-seed", McpHarness.Text(data, "toolPolicy.source"));
        var rows = Js.Items(Js.Member(data, "toolPolicy"), "rows");
        Assert.Equal(ContractSet.Embedded.Tools.Count, rows.Count);
        JsonTreeNode answer = rows.Single(r => McpHarness.Text(r, "tool") == "answer_approval");
        Assert.Equal("read-only", McpHarness.Text(answer, "maxAiState"));
        Assert.Equal(new[] { "person" }, Js.Items(answer, "entries").Select(e => e.GetString()).ToArray());
        JsonTreeNode security = rows.Single(r => McpHarness.Text(r, "tool") == "request_security_review");
        Assert.Equal(new[] { "review-request" }, Js.Items(security, "aiMaySubmit").Select(e => e.GetString()).ToArray());
        JsonTreeNode client = Assert.Single(Js.Items(data, "clients"), c => McpHarness.Text(c, "calls") != "0" && McpHarness.Text(c, "drafts") != "0");
        Assert.Equal("false", McpHarness.Text(client, "isKnown"));
        JsonTreeNode action = Assert.Single(Js.Items(data, "actions"));
        Assert.Equal("run_due_state_refresh", McpHarness.Text(action, "tool"));
        Assert.Equal("true", McpHarness.Text(action, "allowed"));

        // The configuration admin reads the page, without the notices listed; the owner reads none of it.
        JsonTreeNode config = McpHarness.Data(h.Ok(McpHarness.TokenConfigAdmin, "get_admin_status", "{}"));
        Assert.Empty(Js.Items(Js.Member(config, "notices"), "items"));
        Assert.Equal("false", McpHarness.Text(Assert.Single(Js.Items(config, "actions")), "allowed"));
        h.Fails(McpHarness.TokenOwner, "get_admin_status", "{}", McpErrorCodes.NotAuthorized);
    }

    // ---- 71. save_deployment_target: retire a target ----

    [Fact]
    public void ARetiredTargetStaysInHistoryAndRefusesNewDeploymentsWithTheReason()
    {
        McpHarness h = McpHarness.Create();
        McpServerCore web = h.PersonServer();
        string before = McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenOwner, "record_deployment", Scenarios.DeploymentArguments("run-before", McpHarness.Production))), "deploymentRecordId");

        h.Fails(McpHarness.TokenAdminWeb, "save_deployment_target", "{\"deploymentTargetId\":\"" + McpHarness.Production + "\",\"retire\":true}", McpErrorCodes.Validation, server: web);
        JsonTreeNode retired = McpHarness.Data(h.Ok(McpHarness.TokenAdminWeb, "save_deployment_target", "{\"deploymentTargetId\":\"" + McpHarness.Production + "\",\"retire\":true,\"retiredReason\":\"Replaced by the operations production environment.\"}", server: web));
        Assert.Equal("2026-11-22", McpHarness.Text(retired, "target.retiredOn"));
        Assert.Equal("1", McpHarness.Text(retired, "target.deploymentCount"));
        Assert.NotNull(h.Store.Get("deployment-record", before));

        // A new deployment is refused with the reason; a late report of one that finished before the retirement day is taken.
        JsonTreeNode refused = h.Fails(McpHarness.TokenOwner, "record_deployment", Scenarios.DeploymentArguments("run-after", McpHarness.Production), McpErrorCodes.RuleViolation);
        Assert.Contains("retired on 2026-11-22", McpHarness.Text(refused, "error.message"));
        Assert.Contains("operations production environment", McpHarness.Text(refused, "error.message"));
        Assert.Equal("G9", McpHarness.Text(refused, "error.guideline"));
        string late = Scenarios.DeploymentArguments("run-late", McpHarness.Production).Replace("2026-11-22T08:30:00Z", "2026-11-20T08:30:00Z");
        h.Ok(McpHarness.TokenOwner, "record_deployment", late);

        // No pipeline maps to it, and it changes only by coming back into service.
        h.Fails(McpHarness.TokenOwner, "map_pipeline", Scenarios.PipelineArguments(), McpErrorCodes.RuleViolation);
        h.Fails(McpHarness.TokenAdminWeb, "save_deployment_target", "{\"deploymentTargetId\":\"" + McpHarness.Production + "\",\"labelAsSeen\":\"Renamed\"}", McpErrorCodes.RuleViolation, server: web);
        Assert.DoesNotContain(Js.Items(McpHarness.Data(h.Ok(McpHarness.TokenReader, "list_deployment_targets", "{\"state\":\"active\"}")), "items"), t => McpHarness.Text(t, "deploymentTargetId") == McpHarness.Production);
        Assert.Single(Js.Items(McpHarness.Data(h.Ok(McpHarness.TokenReader, "list_deployment_targets", "{\"state\":\"retired\"}")), "items"));

        JsonTreeNode back = McpHarness.Data(h.Ok(McpHarness.TokenAdminWeb, "save_deployment_target", "{\"deploymentTargetId\":\"" + McpHarness.Production + "\",\"retire\":false}", server: web));
        Assert.Equal("null", McpHarness.Text(back, "target.retiredOn"));
        h.Ok(McpHarness.TokenOwner, "record_deployment", Scenarios.DeploymentArguments("run-again", McpHarness.Production));
    }

    // ---- 93 and 94: the agents and a security review request ----

    [Fact]
    public void TheAgentsListSaysWhatEachAgentLacksAndASecurityReviewRequestOpensOnce()
    {
        McpHarness h = McpHarness.Create();
        h.Store.Seed("business-application", "{\"name\":\"Fixture supplier price agent\",\"recordNumber\":\"APP-0003\",\"purpose\":\"Watches supplier prices\",\"applicationKind\":\"agent\","
            + "\"portfolio\":\"" + McpHarness.Portfolio + "\",\"status\":\"app-registered\",\"statusMeaning\":\"registered\",\"tier\":\"tier-3\",\"lifecycleStage\":\"build\","
            + "\"usesAi\":true,\"configurationSetVersion\":1}", id: Agent);
        h.Store.Seed("owner-assignment", "{\"businessApplication\":\"" + Agent + "\",\"ownerRole\":\"business-owner\",\"principal\":\"" + McpHarness.Owner + "\",\"isDeputy\":false,\"startOn\":\"2026-11-01\"}", id: "00000000-0000-4000-8000-000000000555");

        JsonTreeNode list = McpHarness.Data(h.Ok(McpHarness.TokenReader, "list_agents", "{}"));
        Assert.Equal(new[] { "agent" }, Js.Items(list, "applicationKindKeys").Select(k => k.GetString()).ToArray());
        JsonTreeNode agent = Assert.Single(Js.Items(list, "items"));
        Assert.Equal(Agent, McpHarness.Text(agent, "businessApplicationId"));
        Assert.Equal("null", McpHarness.Text(agent, "autonomy.agreed"));
        Assert.Equal("true", McpHarness.Text(agent, "autonomy.requiredAtGoLive"));
        Assert.Equal("none", McpHarness.Text(agent, "securityReview.state"));
        Assert.Equal("true", McpHarness.Text(agent, "securityReview.requiredAtGoLive"));
        Assert.Equal("false", McpHarness.Text(agent, "goLive.ready"));
        var flags = Js.Items(agent, "flags").Select(f => McpHarness.Text(f, "kind")).ToList();
        Assert.Contains("autonomy-missing", flags);
        Assert.Contains("security-review-missing", flags);
        Assert.Equal("1", McpHarness.Text(list, "counts.autonomyMissing"));
        Assert.Empty(Js.Items(McpHarness.Data(h.Ok(McpHarness.TokenReader, "list_agents", "{\"autonomy\":\"agreed\"}")), "items"));

        // The owner's AI client asks for the security review: a request, never a decision. A second ask returns it.
        JsonTreeNode asked = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "request_security_review", "{\"businessApplicationId\":\"" + Agent + "\",\"reason\":\"It reads supplier prices and acts on them.\"}"));
        Assert.Equal("due", McpHarness.Text(asked, "status.meaning"));
        Assert.Equal("security-review", McpHarness.Text(asked, "reviewTypeKey"));
        Assert.Equal("true", McpHarness.Text(asked, "isAgent"));
        Assert.Equal("true", McpHarness.Text(asked, "isAiCreated"));
        JsonTreeNode again = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "request_security_review", "{\"businessApplicationId\":\"" + Agent + "\",\"reason\":\"Again\"}"));
        Assert.Equal("true", McpHarness.Text(again, "existingReviewReturned"));
        Assert.Equal(McpHarness.Text(asked, "reviewId"), McpHarness.Text(again, "reviewId"));
        JsonTreeNode after = Assert.Single(Js.Items(McpHarness.Data(h.Ok(McpHarness.TokenReader, "list_agents", "{}")), "items"));
        Assert.Equal("due", McpHarness.Text(after, "securityReview.state"));
        Assert.DoesNotContain(Js.Items(after, "flags"), f => McpHarness.Text(f, "kind") == "security-review-missing");

        // Someone who neither owns the app nor holds a reviewing role is refused.
        h.Fails(McpHarness.TokenReader, "request_security_review", "{\"businessApplicationId\":\"" + Agent + "\",\"reason\":\"Fixture\"}", McpErrorCodes.NotAuthorized);
    }

    // ---- 67. get_ai_activity: the clients view ----

    [Fact]
    public void TheAiActivityCountsWhatEachClientDidAndWhatAPersonTookForward()
    {
        McpHarness h = McpHarness.Create();
        Scenarios.SubmittedIntake(h);
        h.Fails(McpHarness.TokenOwner, "save_deployment_target", Scenarios.TargetArguments(), McpErrorCodes.AiCannotDoThis);

        JsonTreeNode data = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "get_ai_activity", "{}"));
        JsonTreeNode client = Assert.Single(Js.Items(data, "clients"));
        Assert.True((Js.Number(client, "calls") ?? 0) >= 3);
        Assert.True((Js.Number(client, "drafts") ?? 0) >= 1);
        Assert.Equal("1", McpHarness.Text(client, "refused"));
        Assert.Equal("null", McpHarness.Text(client, "knownClientKey"));
    }
}
