using System;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Identity;
using Product.Mcp.Core.Json;
using Product.Mcp.Core.Protocol;
using Product.Mcp.Testing;
using Xunit;

namespace Product.Mcp.Core.Tests;

// Refusals return the fixed error codes of document 24, section 3.5, through the whole server core.
// The token checks of hop 3 answer at the HTTP layer: 401 with the protected resource metadata link, or 403.
public sealed class RefusalTests
{
    [Theory]
    [InlineData(null, 401)]
    [InlineData("mock-unknown", 401)]
    [InlineData(McpHarness.TokenExpired, 401)]
    [InlineData(McpHarness.TokenOtherAudience, 401)]
    [InlineData(McpHarness.TokenOtherTenant, 401)]
    [InlineData(McpHarness.TokenNoScope, 403)]
    [InlineData(McpHarness.TokenAppOnly, 403)]
    public void TheTokenCheckAnswersAtTheHttpLayer(string? token, int status)
    {
        McpHarness h = McpHarness.Create();
        McpResponse response = h.Rpc(token, "tools/call", "{\"name\":\"whoami\",\"arguments\":{}}");
        Assert.Equal(status, response.HttpStatus);
        Assert.Contains("resource_metadata=\"https://mcp.example.org/.well-known/oauth-protected-resource/mcp\"", response.Headers["WWW-Authenticate"], StringComparison.Ordinal);
        JsonTreeNode envelope = Js.Member(Js.Member(response.Error, "data"), "error")!;
        Assert.Equal(status == 401 ? McpErrorCodes.NotSignedIn : McpErrorCodes.NotAuthorized, Js.Text(envelope, "code"));
        if (status == 403)
        {
            Assert.Contains("insufficient_scope", response.Headers["WWW-Authenticate"], StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheMockIdentityNeverAcceptsARealToken()
    {
        MockTokenValidator tokens = McpHarness.Tokens();
        Assert.Throws<ArgumentException>(() => tokens.Add("eyJhbGciOiJSUzI1NiJ9.e30.sig", new MockToken(McpHarness.Owner, McpHarness.Audience, McpHarness.Tenant, McpHarness.ClaudeCode, McpHarness.AsOf.AddHours(1))));
        Assert.Equal(TokenValidationStatus.Invalid, tokens.Validate("Bearer eyJhbGciOiJSUzI1NiJ9.e30.sig", McpHarness.AsOf).Status);
        Assert.Equal(TokenValidationStatus.Missing, tokens.Validate("Basic abc", McpHarness.AsOf).Status);
        Assert.Equal(TokenValidationStatus.Valid, tokens.Validate("Bearer " + McpHarness.TokenOwner, McpHarness.AsOf).Status);
    }

    [Fact]
    public void ProtocolBasicsAnswer()
    {
        McpHarness h = McpHarness.Create();
        McpResponse init = h.Rpc(McpHarness.TokenOwner, "initialize", "{\"protocolVersion\":\"2025-06-18\"}");
        Assert.Equal("2025-06-18", Js.Text(init.Result, "protocolVersion"));
        Assert.NotNull(Js.Member(Js.Member(init.Result, "capabilities"), "tools"));
        Assert.Equal(200, h.Rpc(McpHarness.TokenOwner, "ping").HttpStatus);
        Assert.Equal(JsonRpcCodes.MethodNotFound, Js.Number(h.Rpc(McpHarness.TokenOwner, "prompts/list").Error, "code"));
        McpResponse notification = h.Server.Handle(new McpRequest(Js.Parse("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}"), "Bearer " + McpHarness.TokenOwner));
        Assert.Equal(202, notification.HttpStatus);
        Assert.Null(notification.Body);
        JsonTreeNode metadata = h.Server.ProtectedResourceMetadata();
        Assert.Equal("https://mcp.example.org/mcp", Js.Text(metadata, "resource"));
        Assert.Equal("mcp.access", Js.Strings(metadata, "scopes_supported").Single());
    }

    [Fact]
    public void ValidationNamesTheField()
    {
        McpHarness h = McpHarness.Create();
        JsonTreeNode missing = h.Fails(McpHarness.TokenOwner, "save_intake_draft", "{\"title\":\"Only a title\"}", McpErrorCodes.Validation);
        Assert.Equal("arguments", McpHarness.Text(missing, "error.field"));
        JsonTreeNode unknown = h.Fails(McpHarness.TokenOwner, "find_applications", "{\"colour\":\"blue\"}", McpErrorCodes.Validation);
        Assert.Contains("colour", McpHarness.Text(unknown, "error.message"), StringComparison.Ordinal);
        JsonTreeNode nested = h.Fails(McpHarness.TokenOwner, "answer_tier_questions", "{\"intakeRequestId\":\"" + McpHarness.Expense + "\",\"answers\":[{\"questionKey\":\"Data Change\",\"answerKey\":\"x\"}]}", McpErrorCodes.Validation);
        Assert.Equal("answers[0].questionKey", McpHarness.Text(nested, "error.field"));
        h.Fails(McpHarness.TokenOwner, "save_intake_draft", "{\"title\":\"x\",\"need\":\"y\",\"proposedKindKey\":\"no-such-kind\"}", McpErrorCodes.Validation);
        h.Fails(McpHarness.TokenOwner, "get_my_queue", "{\"skipToken\":\"bm90LWEtdG9rZW4\"}", McpErrorCodes.Validation);
        JsonTreeNode run = McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "run_gap_check", "{}"));
        foreach (string token in new[] { "not base64 at all", "bm90LWEtbnVtYmVy", "OTl8" })
        {
            JsonTreeNode refused = h.Fails(McpHarness.TokenAdmin, "run_gap_check", "{\"gapCheckRunId\":\"" + McpHarness.Text(run, "gapCheckRunId") + "\",\"skipToken\":\"" + token + "\"}", McpErrorCodes.Validation);
            Assert.Equal("skipToken", McpHarness.Text(refused, "error.field"));
        }
    }

    [Fact]
    public void UnknownAndLaterToolsAreNotFound()
    {
        McpHarness h = McpHarness.Create();
        h.Fails(McpHarness.TokenOwner, "approve_decision", "{}", McpErrorCodes.NotFound);
        h.Fails(McpHarness.TokenAdmin, "check_change_approval", "{\"businessApplicationId\":\"" + McpHarness.Expense + "\",\"deploymentTargetId\":\"" + McpHarness.Production + "\"}", McpErrorCodes.NotFound);
        h.Fails(McpHarness.TokenReader, "get_application_card", "{\"businessApplicationId\":\"00000000-0000-4000-8000-000000000999\"}", McpErrorCodes.NotFound);
    }

    [Fact]
    public void RelationsAreCheckedBeyondTheRole()
    {
        McpHarness h = McpHarness.Create();

        // The configuration admin's intake: the owner may not submit or withdraw it.
        JsonTreeNode theirs = h.Ok(McpHarness.TokenConfigAdmin, "save_intake_draft", "{\"title\":\"Fixture config intake\",\"need\":\"A need\"}");
        string id = McpHarness.Text(McpHarness.Data(theirs), "recordId");
        h.Fails(McpHarness.TokenOwner, "submit_record", "{\"subjectKind\":\"intake-request\",\"subjectId\":\"" + id + "\"}", McpErrorCodes.NotAuthorized);
        h.Fails(McpHarness.TokenOwner, "withdraw_record", "{\"subjectKind\":\"intake-request\",\"subjectId\":\"" + id + "\",\"reason\":\"x\"}", McpErrorCodes.NotAuthorized);
        h.Fails(McpHarness.TokenOwner, "save_intake_draft", "{\"intakeRequestId\":\"" + id + "\",\"title\":\"Mine now\",\"need\":\"x\"}", McpErrorCodes.NotAuthorized);
    }

    [Fact]
    public void AReusedClientRequestIdWithAnotherRequestIsAConflict()
    {
        McpHarness h = McpHarness.Create();
        h.Ok(McpHarness.TokenOwner, "save_intake_draft", "{\"title\":\"First\",\"need\":\"A need\",\"clientRequestId\":\"req-1\"}");
        h.Fails(McpHarness.TokenOwner, "save_intake_draft", "{\"title\":\"Second\",\"need\":\"Another need\",\"clientRequestId\":\"req-1\"}", McpErrorCodes.Conflict);
    }

    [Fact]
    public void ALockedSettingNeverChanges()
    {
        McpHarness h = McpHarness.Create();
        JsonTreeNode refused = h.Fails(McpHarness.TokenConfigAdmin, "propose_configuration_change", "{\"baseVersionNumber\":1,\"proposalRationale\":\"Let the AI approve\",\"operations\":[{\"op\":\"upsert\",\"entity\":\"setting\",\"key\":\"ai-approval-lock\",\"values\":{\"value\":false}}]}", McpErrorCodes.Locked);
        Assert.Equal("ai-approval-lock", McpHarness.Text(refused, "error.ruleKey"));
        Assert.Empty(h.Store.Rows("configuration-draft"));
    }

    [Fact]
    public void ARuleRefusesWithRuleViolation()
    {
        McpHarness h = McpHarness.Create();
        h.Fails(McpHarness.TokenOwner, "save_link", "{\"linkCategory\":\"delivery\",\"subjectKind\":\"business-application\",\"subjectId\":\"" + McpHarness.Expense + "\",\"linkKindKey\":\"evidence-url\",\"url\":\"https://example.org/x\",\"label\":\"x\"}", McpErrorCodes.RuleViolation);
        h.Fails(McpHarness.TokenOwner, "save_link", "{\"linkCategory\":\"delivery\",\"subjectKind\":\"business-application\",\"subjectId\":\"" + McpHarness.Expense + "\",\"linkKindKey\":\"github-issue\",\"url\":\"https://example.org/not-github\",\"label\":\"x\"}", McpErrorCodes.RuleViolation);
        h.Fails(McpHarness.TokenOwner, "link_component", "{\"businessApplicationId\":\"" + McpHarness.Expense + "\",\"componentKindKey\":\"cloud-flow\",\"externalId\":\"flow-2\",\"url\":\"https://make.powerautomate.com/flows/2?token=abc\"}", McpErrorCodes.RuleViolation);
    }

    [Fact]
    public void LiveReadsWithoutAnUpstreamAreUpstreamUnavailable()
    {
        McpHarness h = McpHarness.Create(withPorts: false);
        h.Fails(McpHarness.TokenOwner, "get_role_holders", "{\"roleKey\":\"change-manager\"}", McpErrorCodes.UpstreamUnavailable);
        h.Fails(McpHarness.TokenAdmin, "run_gap_check", "{}", McpErrorCodes.UpstreamUnavailable);
        h.Fails(McpHarness.TokenAdmin, "run_owner_check", "{}", McpErrorCodes.UpstreamUnavailable);
        h.Fails(McpHarness.TokenAdmin, "check_role_bindings", "{}", McpErrorCodes.UpstreamUnavailable);

        // The fallback still works: the admin passes the list in.
        h.Ok(McpHarness.TokenAdmin, "compare_inventory_list", Scenarios.InventoryArguments());

        McpHarness down = McpHarness.Create();
        down.Directory.Down = true;
        down.Inventory.Down = true;
        down.Fails(McpHarness.TokenOwner, "get_role_holders", "{\"roleKey\":\"change-manager\"}", McpErrorCodes.UpstreamUnavailable);
        down.Fails(McpHarness.TokenAdmin, "run_gap_check", "{}", McpErrorCodes.UpstreamUnavailable);
        Assert.Empty(down.Store.Rows("gap-check-run"));
    }

    [Fact]
    public void NoConfigurationVersionIsConfigurationUnavailable()
    {
        McpHarness h = McpHarness.Create(seedVersion: false);
        JsonTreeNode refused = h.Fails(McpHarness.TokenOwner, "whoami", "{}", McpErrorCodes.ConfigurationUnavailable);
        Assert.False(string.IsNullOrEmpty(McpHarness.Text(refused, "correlationId")));
        Assert.NotNull(h.Rpc(McpHarness.TokenOwner, "tools/list", "{}").Error);
    }
}
