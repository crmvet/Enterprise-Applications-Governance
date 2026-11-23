using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Json;
using Product.Mcp.Core.Policy;
using Product.Mcp.Core.Protocol;
using Xunit;

namespace Product.Mcp.Core.Tests;

// Tool Policy filtering per role (proposed C41), its rate limit counted in the store port, the X-Toolsets header and the
// Known Client check (proposed C45).
public sealed class PolicyTests
{
    // C41 rows in force: every shipped seed row, with the overrides given, plus the toolset value list C41 refers to.
    // WORKING DEFAULT (open design question 9): the seed's AI May Submit values stay unless a test overrides them.
    internal static string PolicyOperations(Func<string, JsonTreeNode, JsonTreeNode>? overrides = null, string? extra = null)
    {
        var operations = new List<string>
        {
            "{\"op\":\"upsert\",\"entity\":\"value-list\",\"key\":\"toolset\",\"values\":{\"label\":\"Toolset\",\"isExtensible\":false,\"values\":["
                + string.Join(",", ContractSet.Embedded.Toolsets.Select(t => "{\"key\":\"" + t.Key + "\",\"label\":\"" + t.Label + "\"}")) + "]}}",
        };
        foreach (JsonTreeNode row in ContractSet.Embedded.SeedPolicyRows)
        {
            string tool = Js.Text(row, "toolKey")!;
            JsonTreeNode values = Js.Without(row, "key");
            if (overrides != null)
            {
                values = overrides(tool, values);
            }

            operations.Add("{\"op\":\"upsert\",\"entity\":\"tool-policy\",\"key\":\"" + Js.Text(row, "key") + "\",\"values\":" + Js.Write(values) + "}");
        }

        if (extra != null)
        {
            operations.Add(extra);
        }

        return "[" + string.Join(",", operations) + "]";
    }

    private static List<string> ListTools(McpHarness h, string token, string? toolsets = null)
    {
        McpResponse response = h.Rpc(token, "tools/list", "{}", toolsets);
        Assert.Equal(200, response.HttpStatus);
        return Js.Items(response.Result, "tools").Select(t => Js.Text(t, "name")!).ToList();
    }

    [Fact]
    public void TheShippedSeedFiltersToolsByRole()
    {
        McpHarness h = McpHarness.Create();
        List<string> reader = ListTools(h, McpHarness.TokenReader);
        List<string> owner = ListTools(h, McpHarness.TokenOwner);
        List<string> configAdmin = ListTools(h, McpHarness.TokenConfigAdmin);
        List<string> admin = ListTools(h, McpHarness.TokenAdmin);

        // The reader reads only (record_owner_leave excepted: the leaving person may record their own leave).
        Assert.All(reader.Where(t => t != "record_owner_leave"), t => Assert.False(ContractSet.Embedded.Tools.Single(c => c.Name == t).IsWrite, t));
        Assert.Contains("save_intake_draft", owner);
        Assert.DoesNotContain("propose_configuration_change", owner);
        Assert.Contains("propose_configuration_change", configAdmin);
        Assert.Contains("run_gap_check", admin);
        Assert.DoesNotContain("run_gap_check", owner);
        Assert.DoesNotContain("check_change_approval", admin);

        // No role: only whoami (anyone signed in), and the other tools are refused, not hidden in silence.
        Assert.Equal(new[] { "whoami" }, ListTools(h, McpHarness.TokenNoRole));
        h.Ok(McpHarness.TokenNoRole, "whoami");
        h.Fails(McpHarness.TokenNoRole, "find_applications", "{}", McpErrorCodes.NotAuthorized);
        h.Fails(McpHarness.TokenReader, "save_intake_draft", "{\"title\":\"x\",\"need\":\"y\"}", McpErrorCodes.NotAuthorized);
        h.Fails(McpHarness.TokenOwner, "dismiss_claim", "{\"claimId\":\"" + McpHarness.Expense + "\",\"reason\":\"x\"}", McpErrorCodes.NotAuthorized);
    }

    [Fact]
    public void ToolsListPublishesSelfContainedSchemasAndAnnotations()
    {
        McpHarness h = McpHarness.Create();
        McpResponse response = h.Rpc(McpHarness.TokenAdmin, "tools/list", "{}");
        foreach (JsonTreeNode tool in Js.Items(response.Result, "tools"))
        {
            JsonTreeNode input = Js.Member(tool, "inputSchema")!;
            Assert.Equal("object", Js.Text(input, "type"));
            Assert.DoesNotContain(Refs(input), r => !r.StartsWith("#/", StringComparison.Ordinal));
            Assert.DoesNotContain("$comment", Js.Write(input));
            Assert.DoesNotContain("WORKING DEFAULT", Js.Write(input) + Js.Write(Js.Member(tool, "outputSchema")!));
            Assert.Equal(false, Js.Flag(Js.Member(tool, "annotations"), "destructiveHint"));
            Assert.False(string.IsNullOrEmpty(Js.Text(tool, "description")));
        }
    }

    private static IEnumerable<string> Refs(JsonTreeNode node)
    {
        if (node.Kind == JsonTreeKind.Object)
        {
            foreach (KeyValuePair<string, JsonTreeNode> member in node.Members)
            {
                if (member.Key == "$ref" && member.Value.Kind == JsonTreeKind.String)
                {
                    yield return member.Value.GetString();
                }

                foreach (string inner in Refs(member.Value))
                {
                    yield return inner;
                }
            }
        }
        else if (node.Kind == JsonTreeKind.Array)
        {
            foreach (string inner in node.Items.SelectMany(Refs))
            {
                yield return inner;
            }
        }
    }

    [Fact]
    public void ToolPolicyRowsInForceReplaceTheSeedAndOnlyTighten()
    {
        McpHarness h = McpHarness.Create(PolicyOperations((tool, values) => tool switch
        {
            "save_link" => Js.With(values, ("isEnabled", JsonTreeNode.False)),
            "save_intake_draft" => Js.With(values, ("maxAiState", JsonTreeNode.FromString("read-only"))),
            "save_application_draft" => Js.With(values, ("maxAiState", JsonTreeNode.FromString("draft"))),
            "submit_record" => Js.With(values, ("aiMaySubmit", Js.Obj(("intake-request", JsonTreeNode.False), ("change-request", JsonTreeNode.True), ("decision", JsonTreeNode.True), ("configuration-draft", JsonTreeNode.True)))),
            _ => values,
        }));

        // Disabled: hidden from every caller, and a call is not_found.
        Assert.DoesNotContain("save_link", ListTools(h, McpHarness.TokenOwner));
        h.Fails(McpHarness.TokenOwner, "save_link", "{\"linkCategory\":\"delivery\",\"subjectKind\":\"business-application\",\"subjectId\":\"" + McpHarness.Expense + "\",\"linkKindKey\":\"ado-work-item\",\"url\":\"https://devops.example.org/x\",\"label\":\"x\"}", McpErrorCodes.NotFound);

        // Max AI State read-only: the AI client may not write through the tool.
        h.Fails(McpHarness.TokenOwner, "save_intake_draft", "{\"title\":\"x\",\"need\":\"y\"}", McpErrorCodes.AiCannotDoThis);

        // Max AI State draft: a draft application passes, a proposed ownership decision does not.
        h.Ok(McpHarness.TokenOwner, "save_application_draft", "{\"name\":\"Fixture notes\",\"purpose\":\"Notes\",\"applicationKindKey\":\"power-platform-app\"}");
        h.Fails(McpHarness.TokenOwner, "save_application_draft", "{\"name\":\"Fixture notes 2\",\"purpose\":\"Notes\",\"applicationKindKey\":\"power-platform-app\",\"proposedOwners\":[{\"ownerRoleKey\":\"business-owner\",\"objectId\":\"" + McpHarness.Owner + "\"}]}", McpErrorCodes.AiCannotDoThis);

        // AI May Submit no for intake requests (question 9), yes for change requests.
        string change = Scenarios.Change(h);
        h.Fails(McpHarness.TokenOwner, "submit_record", "{\"subjectKind\":\"intake-request\",\"subjectId\":\"" + McpHarness.Expense + "\"}", McpErrorCodes.AiCannotDoThis);
        h.Ok(McpHarness.TokenOwner, "submit_record", "{\"subjectKind\":\"change-request\",\"subjectId\":\"" + change + "\"}");
    }

    [Fact]
    public void ConfigurationCannotWidenTheMaxAiState()
    {
        ToolContract intake = ContractSet.Embedded.Tools.Single(t => t.Name == "save_intake_draft");
        ToolContract submit = ContractSet.Embedded.Tools.Single(t => t.Name == "submit_record");
        var rows = ContractSet.Embedded.SeedPolicyRows.Select(r => Js.Text(r, "toolKey") == "save_intake_draft" ? Js.With(r, ("maxAiState", JsonTreeNode.FromString("submitted"))) : r).Select(ToolPolicyRow.FromColumns);
        ToolPolicySet policy = ToolPolicySet.FromRows(ToolPolicySource.Configuration, rows);
        Assert.Equal("draft", policy.EffectiveMaxAiState(intake));
        Assert.False(policy.AiMayReach(intake, "submitted"));
        Assert.True(policy.AiMayReach(submit, "submitted"));
        Assert.False(policy.AiMaySubmit(submit, "review-request"));
    }

    [Fact]
    public void TheRateLimitCountsInTheStorePortAndNeverInTheServer()
    {
        McpHarness h = McpHarness.Create(PolicyOperations((tool, values) => tool == "whoami" ? Js.With(values, ("callsPerPersonPerHour", JsonTreeNode.FromInt64(2))) : values));
        h.Ok(McpHarness.TokenOwner, "whoami");

        // A second server core over the same backend (another instance) continues the same count.
        McpServerCore other = McpHarness.NewServer(h.Backend, new RecordingTelemetry(), h.Clock);
        McpResponse second = other.Handle(McpRequest.Call(2, "tools/call", Js.Parse("{\"name\":\"whoami\",\"arguments\":{}}"), "Bearer " + McpHarness.TokenOwner));
        Assert.Equal(true, Js.Flag(Js.Member(second.Result, "structuredContent"), "ok"));

        JsonTreeNode limited = h.Fails(McpHarness.TokenOwner, "whoami", "{}", McpErrorCodes.RateLimited);
        long retry = Js.Number(Js.Member(limited, "error"), "retryAfterSeconds") ?? 0;
        Assert.InRange(retry, 1, 3600);

        // Another person has their own count; the next hour starts a new window.
        h.Ok(McpHarness.TokenAdmin, "whoami");
        h.Clock.Advance(TimeSpan.FromHours(1));
        h.Ok(McpHarness.TokenOwner, "whoami");
    }

    [Fact]
    public void XToolsetsNarrowsTheListAndTheCalls()
    {
        McpHarness h = McpHarness.Create();
        List<string> portfolio = ListTools(h, McpHarness.TokenOwner, "portfolio");
        Assert.Equal(6, portfolio.Count);
        Assert.All(portfolio, t => Assert.Equal("portfolio", ContractSet.Embedded.Tools.Single(c => c.Name == t).ToolsetKey));
        Assert.Contains("get_documentation_status", ListTools(h, McpHarness.TokenOwner, "portfolio, documentation"));

        McpResponse unknown = h.Rpc(McpHarness.TokenOwner, "tools/list", "{}", "portfolio,nope");
        Assert.Equal(JsonRpcCodes.InvalidParams, Js.Number(unknown.Error, "code"));

        JsonTreeNode outside = h.Fails(McpHarness.TokenOwner, "whoami", "{}", McpErrorCodes.NotFound, toolsets: "portfolio");
        Assert.Equal(ToolsetSelection.HeaderName, McpHarness.Text(outside, "error.field"));
        h.Fails(McpHarness.TokenOwner, "find_applications", "{}", McpErrorCodes.Validation, toolsets: "nope");
        h.Ok(McpHarness.TokenOwner, "find_applications", "{}", toolsets: "portfolio");
    }

    [Fact]
    public void TheKnownClientListIsAnAllowListOnceInForce()
    {
        // No C45 row in force: the check does not apply, and any client is labeled by its app ID.
        McpHarness open = McpHarness.Create();
        JsonTreeNode me = open.Ok(McpHarness.TokenOtherClient, "whoami");
        Assert.Equal(McpHarness.OtherClient, McpHarness.Text(McpHarness.Data(me), "clientApp.label"));

        string clients = "{\"op\":\"upsert\",\"entity\":\"known-client\",\"key\":\"claude-code\",\"values\":{\"label\":\"Claude Code\",\"clientIdentifier\":\"" + McpHarness.ClaudeCode + "\",\"isAiClient\":true,\"isAllowed\":true}},"
            + "{\"op\":\"upsert\",\"entity\":\"known-client\",\"key\":\"other-client\",\"values\":{\"label\":\"Other client\",\"clientIdentifier\":\"" + McpHarness.OtherClient + "\",\"isAiClient\":true,\"isAllowed\":false}}";
        McpHarness h = McpHarness.Create("[" + clients + "]");
        JsonTreeNode allowed = h.Ok(McpHarness.TokenOwner, "whoami");
        Assert.Equal("Claude Code", McpHarness.Text(McpHarness.Data(allowed), "clientApp.label"));
        h.Fails(McpHarness.TokenOtherClient, "whoami", "{}", McpErrorCodes.NotAuthorized);
        McpResponse list = h.Rpc(McpHarness.TokenOtherClient, "tools/list", "{}");
        Assert.NotNull(list.Error);

        // The label reaches the stamped row's Client App column.
        JsonTreeNode saved = h.Ok(McpHarness.TokenOwner, "save_intake_draft", "{\"title\":\"Fixture travel requests\",\"need\":\"Replace the spreadsheet\"}");
        Assert.Equal("Claude Code", McpHarness.Text(McpHarness.Data(saved), "performedBy.clientApp.label"));
        Assert.Equal("Claude Code", h.Store.Get("intake-request", McpHarness.Text(McpHarness.Data(saved), "recordId"))!.Text("clientApp"));
    }

    [Fact]
    public void AnAgentUserHoldsNoRoleAndReadsNothing()
    {
        McpHarness h = McpHarness.Create();
        Assert.Equal(new[] { "whoami" }, ListTools(h, McpHarness.TokenAgent));
        h.Fails(McpHarness.TokenAgent, "whoami", "{}", McpErrorCodes.NotAuthorized);
        h.Fails(McpHarness.TokenAgent, "find_applications", "{}", McpErrorCodes.NotAuthorized);
        h.Fails(McpHarness.TokenAgent, "save_intake_draft", "{\"title\":\"x\",\"need\":\"y\"}", McpErrorCodes.NotAuthorized);
    }
}
