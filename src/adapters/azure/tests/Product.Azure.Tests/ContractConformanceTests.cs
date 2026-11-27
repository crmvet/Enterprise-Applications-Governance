using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using Product.Engine.Core.Canonical;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Json;
using Product.Mcp.Core.Policy;
using Xunit;

namespace Product.Azure.Tests;

// The MCP endpoint against the published contracts as they are on disk (src/mcp/contracts), not the copy compiled into
// the MCP core: every tool tools.json publishes for the first release and the Tool Policy serves is listed, by the same
// name, with the same input and output schemas (references inlined, as the contracts README says the server publishes
// them) and the same annotations, through the core's own transport and through the SDK server transport, on the wire
// and as the official SDK client reads it.
public sealed class ContractConformanceTests
{
    private static readonly Lazy<ContractSet> LazyDisk = new(LoadFromDisk);

    // The contracts on disk, loaded the way the core loads its embedded copy (paths relative to mcp/contracts).
    private static ContractSet Disk => LazyDisk.Value;

    private static string ContractsFolder => Path.Combine(TestPaths.Source, "mcp", "contracts");

    [Fact]
    public void TheContractsTheServerRunsAreTheFilesOnDisk()
    {
        using JsonDocument index = JsonDocument.Parse(File.ReadAllText(Path.Combine(ContractsFolder, "tools.json")));
        string[] indexed = index.RootElement.GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()!).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(102, indexed.Length);
        Assert.Equal(indexed, Disk.Tools.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(Disk.Tools.Select(t => t.Name), ContractSet.Embedded.Tools.Select(t => t.Name));

        foreach (ToolContract tool in Disk.Tools)
        {
            Assert.True(ContractSet.Embedded.TryGetTool(tool.Name, out ToolContract embedded), tool.Name);
            Assert.True(File.Exists(Path.Combine(ContractsFolder, tool.InputSchemaId)), tool.InputSchemaId);
            Assert.True(File.Exists(Path.Combine(ContractsFolder, tool.OutputSchemaId)), tool.OutputSchemaId);
            Assert.Equal(tool.Release, embedded.Release);
            Assert.Equal(tool.IsWrite, embedded.IsWrite);
            Assert.Equal(Text(Disk.PublishedInputSchema(tool)), Text(ContractSet.Embedded.PublishedInputSchema(embedded)));
            Assert.Equal(Text(Disk.PublishedOutputSchema(tool)), Text(ContractSet.Embedded.PublishedOutputSchema(embedded)));
            Assert.Equal(Text(tool.Annotations), Text(embedded.Annotations));
        }

        Assert.Equal(Disk.Resources.Select(r => r.UriTemplate), ContractSet.Embedded.Resources.Select(r => r.UriTemplate));
        Assert.Equal(Disk.Errors.Keys.OrderBy(k => k, StringComparer.Ordinal), ContractSet.Embedded.Errors.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("core")]
    [InlineData("sdk")]
    public async Task TheWireToolListHoldsEveryServedToolWithItsContract(string transport)
    {
        await using ProductTestHost host = ProductTestHost.Start(Transport(transport));

        JsonArray tools = await WireToolsList(host, Personas.AllRoles);

        AssertMatchesContracts(tools.Select(t => t!.AsObject()).ToList(), transport + " transport on the wire");
    }

    [Theory]
    [InlineData("core")]
    [InlineData("sdk")]
    public async Task TheOfficialSdkClientReadsEveryServedToolWithItsContract(string transport)
    {
        await using ProductTestHost host = ProductTestHost.Start(Transport(transport));
        await using McpClient client = await SdkMcpChannel.Connect(host, Personas.AllRoles, "2025-06-18", standaloneGetStream: transport != "sdk");

        IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        AssertMatchesContracts(tools.Select(t => JsonSerializer.SerializeToNode(t.ProtocolTool, McpJsonUtilities.DefaultOptions)!.AsObject()).ToList(), transport + " transport through the SDK client");
    }

    // The SDK server transport rebuilds each answer from the SDK's types; nothing of the core's tool list may be lost or
    // added on the way, for any caller.
    [Fact]
    public async Task TheSdkServerTransportListsExactlyWhatTheCoreLists()
    {
        await using ProductTestHost core = ProductTestHost.Start();
        await using ProductTestHost sdk = ProductTestHost.Start(Transport("sdk"));

        foreach (Persona persona in new[] { Personas.Priya, Personas.Nadia, Personas.Owen, Personas.Mateo, Personas.AllRoles, Personas.Agent })
        {
            JsonArray fromCore = await WireToolsList(core, persona);
            JsonArray fromSdk = await WireToolsList(sdk, persona);
            Assert.Equal(JsonText.Canonical(fromCore), JsonText.Canonical(fromSdk));
        }
    }

    // ---- Helpers ----

    private static void AssertMatchesContracts(IReadOnlyList<JsonObject> listed, string where)
    {
        ToolPolicySet policy = ToolPolicySet.From(DemoSeed.Version.Snapshot, Disk);
        // The MCP endpoint is the AI client entry: a tool served only to a person (answer_approval, D-38) is not listed.
        List<ToolContract> served = Disk.Tools.Where(t => policy.IsServed(t) && !t.IsPersonOnly).ToList();
        Assert.Equal(Disk.Tools.Count(t => t.IsR1 && !t.IsPersonOnly), served.Count);
        Assert.Equal(
            served.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal),
            listed.Select(t => t["name"]!.GetValue<string>()).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(listed.Count, listed.Select(t => t["name"]!.GetValue<string>()).Distinct(StringComparer.Ordinal).Count());

        foreach (JsonObject tool in listed)
        {
            string name = tool["name"]!.GetValue<string>();
            Assert.True(Disk.TryGetTool(name, out ToolContract contract), where + ": " + name + " is not in tools.json");
            Assert.True(Text(Disk.PublishedInputSchema(contract)) == JsonText.Canonical(tool["inputSchema"]), where + ": the input schema of " + name + " differs from " + contract.InputSchemaId);
            Assert.True(Text(Disk.PublishedOutputSchema(contract)) == JsonText.Canonical(tool["outputSchema"]), where + ": the output schema of " + name + " differs from " + contract.OutputSchemaId);
            Assert.True(Text(contract.Annotations) == JsonText.Canonical(tool["annotations"]), where + ": the annotations of " + name + " differ from tools.json");
            Assert.False(string.IsNullOrEmpty(tool["description"]?.GetValue<string>()), where + ": " + name + " has no description");
        }
    }

    // tools/list on the wire. The core's transport answers JSON; the SDK transport may answer one server-sent event.
    private static async Task<JsonArray> WireToolsList(ProductTestHost host, Persona persona)
    {
        HttpCall call = await host.Post("/mcp", persona, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}", r =>
        {
            r.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
            r.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2025-06-18");
        });
        Assert.True(call.Status == 200, "tools/list answered " + call.Status + ": " + call.Body);
        JsonNode body = JsonNode.Parse(DataOf(call))!;
        return body["result"]!["tools"]!.AsArray();
    }

    private static string DataOf(HttpCall call)
    {
        string? type = call.Header("Content-Type");
        if (type == null || !type.StartsWith("text/event-stream", StringComparison.OrdinalIgnoreCase))
        {
            return call.Body;
        }

        string[] data = call.Body.Replace("\r\n", "\n").Split('\n')
            .Where(l => l.StartsWith("data:", StringComparison.Ordinal))
            .Select(l => l.Substring("data:".Length).TrimStart())
            .ToArray();
        return Assert.Single(data);
    }

    private static HostSettings Transport(string transport)
    {
        var settings = new HostSettings();
        settings.Settings["Product:Mcp:Transport"] = transport;
        return settings;
    }

    private static string Text(JsonTreeNode node)
    {
        return JsonText.Canonical(Js.Write(node));
    }

    private static ContractSet LoadFromDisk()
    {
        string folder = ContractsFolder;
        return ContractSet.Load(Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories)
            .Select(f => new KeyValuePair<string, string>(Path.GetRelativePath(folder, f).Replace('\\', '/'), File.ReadAllText(f))));
    }
}
