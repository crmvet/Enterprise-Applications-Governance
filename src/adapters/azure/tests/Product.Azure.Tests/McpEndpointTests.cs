using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Policy;
using Xunit;

namespace Product.Azure.Tests;

// The MCP endpoint over streamable HTTP: the handshake, the tool list per caller (the same as the MCP core gives in
// process), tool calls that read and write, resources, the transport rules, the REST facade for the web app, and an MCP
// client from the official SDK.
public sealed class McpEndpointTests
{
    private const string Initialize = "{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"claude-code\",\"version\":\"2.0.0\"}}";

    [Fact]
    public async Task InitializeAnswersTheProtocolAndTheServer()
    {
        await using ProductTestHost host = ProductTestHost.Start();
        InProcessWorld world = InProcessWorld.Create();

        HttpCall call = await host.Mcp(Personas.Priya, "initialize", Initialize);

        Assert.Equal(200, call.Status);
        Assert.Equal("2025-06-18", call.Json!["result"]!["protocolVersion"]!.GetValue<string>());
        Assert.Equal("product-mcp", call.Json["result"]!["serverInfo"]!["name"]!.GetValue<string>());
        (int status, string? body) = world.Handle(Personas.Priya, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":" + Initialize + "}");
        Assert.Equal(status, call.Status);
        Assert.Equal(JsonText.Canonical(body!), JsonText.Canonical(call.Json));
        Assert.False(string.IsNullOrEmpty(call.Header("X-Correlation-Id")));
    }

    [Fact]
    public async Task ToolsListGivesACallerWithEveryRoleEveryPublishedTool()
    {
        await using ProductTestHost host = ProductTestHost.Start();

        HttpCall call = await host.Mcp(Personas.AllRoles, "tools/list");

        Assert.Equal(200, call.Status);
        string[] listed = call.Json!["result"]!["tools"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        ToolPolicySet policy = ToolPolicySet.From(DemoSeed.Version.Snapshot, ContractSet.Embedded);
        string[] published = ContractSet.Embedded.Tools.Where(policy.IsServed).Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.NotEmpty(published);
        Assert.Equal(published, listed);
        Assert.DoesNotContain(listed, n => new[] { "approve", "accept", "reject", "authorize", "activate" }.Any(v => n.StartsWith(v + "_", StringComparison.Ordinal)));

        // Each listed tool carries its contract's schemas.
        foreach (JsonNode? tool in call.Json["result"]!["tools"]!.AsArray())
        {
            Assert.NotNull(tool!["inputSchema"]);
            Assert.NotNull(tool["outputSchema"]);
        }
    }

    [Fact]
    public async Task ToolsListPerCallerEqualsTheInProcessCore()
    {
        await using ProductTestHost host = ProductTestHost.Start();
        InProcessWorld world = InProcessWorld.Create();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (Persona persona in new[] { Personas.Priya, Personas.Nadia, Personas.Owen, Personas.Mateo, Personas.AllRoles, Personas.Agent })
        {
            HttpCall call = await host.Mcp(persona, "tools/list");
            (int status, string? body) = world.Handle(persona, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}");
            Assert.Equal(status, call.Status);
            Assert.Equal(JsonText.Canonical(body!), JsonText.Canonical(call.Json));
            counts[persona.Name] = call.Json!["result"]!["tools"]!.AsArray().Count;
        }

        // People with fewer roles see fewer tools; the agent user resolves to no role and sees only whoami.
        Assert.True(counts["t-mateo"] < counts["t-priya"], string.Join(", ", counts));
        Assert.True(counts["t-priya"] < counts["t-all-roles"], string.Join(", ", counts));
        Assert.Equal(1, counts["t-agent"]);
    }

    [Fact]
    public async Task AReadToolAndAWriteToolWorkOverHttp()
    {
        await using ProductTestHost host = ProductTestHost.Start();

        JsonNode found = await host.Call(Personas.Mateo, "find_applications", "{\"text\":\"inspection\"}");
        Assert.True(found["ok"]!.GetValue<bool>(), found.ToJsonString());
        Assert.Contains(Personas.InspectionScheduler, found["data"]!.ToJsonString(), StringComparison.Ordinal);

        JsonNode card = await host.Call(Personas.Mateo, "get_application_card", "{\"businessApplicationId\":\"" + Personas.RoutePlanner + "\"}");
        Assert.True(card["ok"]!.GetValue<bool>(), card.ToJsonString());
        Assert.Contains("Route Planner", card["data"]!.ToJsonString(), StringComparison.Ordinal);

        JsonNode draft = await host.Call(Personas.Priya, "save_intake_draft", "{\"title\":\"Crew lunch orders\",\"need\":\"Crews phone in lunch orders every morning; a simple app would save the dispatcher half an hour.\",\"proposedKindKey\":\"power-platform-app\",\"clientRequestId\":\"http-write-0001\"}");
        Assert.True(draft["ok"]!.GetValue<bool>(), draft.ToJsonString());
        Assert.True(draft["data"]!["created"]!.GetValue<bool>());
        Assert.Equal("ai-client-for-person", draft["data"]!["performedBy"]!["actorKind"]!.GetValue<string>());
        Assert.Equal("Claude Code", draft["data"]!["performedBy"]!["clientApp"]!["label"]!.GetValue<string>());
        string id = draft["data"]!["recordId"]!.GetValue<string>();
        StoredRow? row = host.Get<IRecordStore>().Get("intake-request", id);
        Assert.NotNull(row);
        Assert.Equal("Crew lunch orders", row!.Text("title"));

        // A reader may not write: the Tool Policy refuses before the engine runs.
        JsonNode refused = await host.Call(Personas.Mateo, "save_intake_draft", "{\"title\":\"Not allowed\",\"need\":\"A reader tries to file an intake request.\",\"proposedKindKey\":\"power-platform-app\"}");
        Assert.False(refused["ok"]!.GetValue<bool>());
        Assert.Equal("not_authorized", refused["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task ResourcesAreServedThroughTheSamePath()
    {
        await using ProductTestHost host = ProductTestHost.Start();
        InProcessWorld world = InProcessWorld.Create();

        foreach (string method in new[] { "resources/list", "resources/templates/list" })
        {
            HttpCall call = await host.Mcp(Personas.Priya, method);
            (int status, string? body) = world.Handle(Personas.Priya, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"" + method + "\"}");
            Assert.Equal(status, call.Status);
            Assert.Equal(JsonText.Canonical(body!), JsonText.Canonical(call.Json));
        }

        HttpCall read = await host.Mcp(Personas.Priya, "resources/read", "{\"uri\":\"configuration://in-force\"}");
        (int readStatus, string? readBody) = world.Handle(Personas.Priya, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"resources/read\",\"params\":{\"uri\":\"configuration://in-force\"}}");
        Assert.Equal(200, read.Status);
        Assert.Equal(readStatus, read.Status);
        Assert.Equal(JsonText.Canonical(readBody!), JsonText.Canonical(read.Json));
        Assert.NotNull(read.Json!["result"]!["contents"]![0]!["text"]);
    }

    [Fact]
    public async Task TheTransportRulesHold()
    {
        await using ProductTestHost host = ProductTestHost.Start();

        HttpCall notification = await host.Post("/mcp", Personas.Priya, "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
        Assert.Equal(202, notification.Status);
        Assert.Equal(string.Empty, notification.Body);

        HttpCall batch = await host.Post("/mcp", Personas.Priya, "[{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}]");
        Assert.Equal(400, batch.Status);

        HttpCall notJson = await host.Post("/mcp", Personas.Priya, "{not json");
        Assert.Equal(400, notJson.Status);
        Assert.Equal(-32700, notJson.Json!["error"]!["code"]!.GetValue<int>());

        HttpCall text = await host.Send(System.Net.Http.HttpMethod.Post, "/mcp", Personas.Priya, null, r => r.Content = new System.Net.Http.StringContent("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}", System.Text.Encoding.UTF8, "text/plain"));
        Assert.Equal(415, text.Status);

        HttpCall foreignOrigin = await host.Post("/mcp", Personas.Priya, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}", r => r.Headers.TryAddWithoutValidation("Origin", "https://attacker.example.net"));
        Assert.Equal(403, foreignOrigin.Status);

        HttpCall ping = await host.Mcp(Personas.Priya, "ping");
        Assert.Equal(200, ping.Status);

        HttpCall unknown = await host.Mcp(Personas.Priya, "sampling/createMessage", "{}");
        Assert.Equal(-32601, unknown.Json!["error"]!["code"]!.GetValue<int>());

        JsonNode approve = await host.Call(Personas.Priya, "approve_decision", "{\"decisionId\":\"00000000-0000-4000-9000-000000000001\"}");
        Assert.False(approve["ok"]!.GetValue<bool>());
        Assert.Equal("not_found", approve["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task TheRestFacadeServesReadToolsAndTheSession()
    {
        await using ProductTestHost host = ProductTestHost.Start();

        HttpCall find = await host.Post("/api/find_applications", Personas.Priya, "{\"text\":\"crew\"}");
        Assert.Equal(200, find.Status);
        Assert.True(find.Json!["ok"]!.GetValue<bool>(), find.Body);

        HttpCall write = await host.Post("/api/save_intake_draft", Personas.Priya, "{\"title\":\"Through REST\",\"need\":\"A write through the REST facade.\",\"proposedKindKey\":\"power-platform-app\"}");
        Assert.Equal(404, write.Status);
        Assert.Equal("not_found", write.Json!["error"]!["code"]!.GetValue<string>());
        Assert.Contains("MCP endpoint", write.Json["error"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);

        HttpCall unknown = await host.Post("/api/no_such_tool", Personas.Priya, "{}");
        Assert.Equal(404, unknown.Status);

        HttpCall me = await host.Get("/api/me", Personas.Priya);
        Assert.Equal(200, me.Status);
        string[] roles = me.Json!["roles"]!.AsArray().Select(r => r!["roleKey"]!.GetValue<string>()).OrderBy(r => r, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "maker", "requester" }, roles);
        Assert.Equal(1, me.Json["configurationSetVersion"]!.GetValue<int>());

        HttpCall info = await host.Get("/api/info", Personas.Priya);
        Assert.Equal(200, info.Status);
        Assert.Equal(DemoSeed.Version.ContentHash, info.Json!["configuration"]!["contentHash"]!.GetValue<string>());
        Assert.Equal("https://mcp.example.org/mcp", info.Json["mcpEndpoint"]!.GetValue<string>());

        HttpCall pipelineMe = await host.Get("/api/me", Personas.Pipeline);
        Assert.Equal(403, pipelineMe.Status);
        Assert.Equal("not_authorized", pipelineMe.Json!["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task TheOfficialSdkClientConnectsListsCallsAndReads()
    {
        await using ProductTestHost host = ProductTestHost.Start();
        await using McpClient client = await SdkMcpChannel.Connect(host, Personas.Priya, "2025-06-18");

        Assert.Equal("2025-06-18", client.NegotiatedProtocolVersion);
        Assert.Equal("product-mcp", client.ServerInfo?.Name);
        IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(tools, t => t.Name == "save_intake_draft");

        CallToolResult whoami = await client.CallToolAsync(new CallToolRequestParams { Name = "whoami" }, TestContext.Current.CancellationToken);
        JsonNode? who = JsonSerializer.SerializeToNode(whoami.StructuredContent);
        Assert.True(who!["ok"]!.GetValue<bool>(), who.ToJsonString());
        Assert.Equal("ai-client-for-person", who["data"]!["actorKind"]!.GetValue<string>());

        CallToolResult found = await client.CallToolAsync(new CallToolRequestParams { Name = "find_applications", Arguments = new Dictionary<string, JsonElement> { ["text"] = JsonSerializer.SerializeToElement("inspection") } }, TestContext.Current.CancellationToken);
        Assert.Contains(Personas.InspectionScheduler, JsonSerializer.Serialize(found.StructuredContent), StringComparison.Ordinal);

        ReadResourceResult resource = await client.ReadResourceAsync("configuration://in-force", cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEmpty(resource.Contents);
        Assert.IsType<TextResourceContents>(resource.Contents[0]);
    }

    [Fact]
    public async Task TheOfficialSdkClientWithItsDefaultProtocolFallsBackToTheHandshake()
    {
        await using ProductTestHost host = ProductTestHost.Start();
        await using McpClient client = await SdkMcpChannel.Connect(host, Personas.Priya, protocolVersion: null);

        Assert.Equal("2025-06-18", client.NegotiatedProtocolVersion);
        CallToolResult whoami = await client.CallToolAsync(new CallToolRequestParams { Name = "whoami" }, TestContext.Current.CancellationToken);
        Assert.True(JsonSerializer.SerializeToNode(whoami.StructuredContent)!["ok"]!.GetValue<bool>());
    }
}
