using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Product.Azure.Tests;

// What an MCP client saw for one message: the HTTP status and a view of the answer that both sides of a comparison can
// give. The raw channel sees the whole JSON-RPC body; the SDK channel sees what the official SDK client hands its caller.
internal sealed class McpSeen
{
    public McpSeen(int status, JsonNode? view, JsonObject digest)
    {
        Status = status;
        View = view;
        Digest = digest;
    }

    public int Status { get; }

    public JsonNode? View { get; }

    public JsonObject Digest { get; }
}

// An MCP client channel to the HTTP host.
internal interface IMcpChannel : IAsyncDisposable
{
    string Name { get; }

    Task<McpSeen> Send(Persona persona, JsonNode message);

    // The same view of an answer the MCP core gave in process (the full JSON-RPC body), for comparison.
    McpSeen ViewOf(int status, JsonNode? body, string method);
}

// Raw streamable HTTP: the JSON-RPC message as the transcript holds it, POSTed to /mcp with the persona header. The view
// is the whole answer body.
internal sealed class RawMcpChannel : IMcpChannel
{
    private readonly ProductTestHost _host;

    public RawMcpChannel(ProductTestHost host)
    {
        _host = host;
    }

    public string Name => "raw HTTP";

    public async Task<McpSeen> Send(Persona persona, JsonNode message)
    {
        HttpCall call = await _host.Post("/mcp", persona, message.ToJsonString());
        return ViewOf(call.Status, call.Json, message["method"]!.GetValue<string>());
    }

    public McpSeen ViewOf(int status, JsonNode? body, string method)
    {
        return new McpSeen(status, body, JsonText.McpDigest(body));
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}

// The official MCP C# SDK client (ModelContextProtocol) over streamable HTTP, one client per persona, as a third-party
// MCP client such as Claude Code would connect: initialize on connect, then tools/list and tools/call. The view is what
// the SDK returns: the negotiated protocol and server name, the tool names, a tool's structured content, a resource's
// text.
internal sealed class SdkMcpChannel : IMcpChannel
{
    private readonly ProductTestHost _host;
    private readonly string? _protocolVersion;
    private readonly bool _standaloneGetStream;
    private readonly Dictionary<string, McpClient> _clients = new(StringComparer.Ordinal);

    public SdkMcpChannel(ProductTestHost host, string? protocolVersion = "2025-06-18", bool standaloneGetStream = true)
    {
        _host = host;
        _protocolVersion = protocolVersion;
        _standaloneGetStream = standaloneGetStream;
    }

    public string Name => "MCP SDK client";

    public static async Task<McpClient> Connect(ProductTestHost host, Persona persona, string? protocolVersion, bool standaloneGetStream = true)
    {
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri("http://localhost/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
                Name = persona.Name,
                AdditionalHeaders = new Dictionary<string, string> { [ProductTestHost.DevHeader] = persona.Name },
                EnableStandaloneGetStream = standaloneGetStream,
            },
            host.Client,
            loggerFactory: null,
            ownsHttpClient: false);
        return await McpClient.CreateAsync(
            transport,
            new McpClientOptions { ProtocolVersion = protocolVersion, ClientInfo = new Implementation { Name = "claude-code", Version = "2.0.0" } },
            loggerFactory: null,
            TestContext.Current.CancellationToken);
    }

    public async Task<McpSeen> Send(Persona persona, JsonNode message)
    {
        string method = message["method"]!.GetValue<string>();
        McpClient client;
        try
        {
            client = await Client(persona);
        }
        catch (Exception exception) when (exception is HttpRequestException || exception is McpException)
        {
            // The SDK refuses to go on when the server refuses the handshake (an app-only token, for example).
            int status = exception is HttpRequestException http && http.StatusCode.HasValue ? (int)http.StatusCode.Value : 0;
            return new McpSeen(status, null, JsonText.Digest(false, "refused at connect", Array.Empty<string>()));
        }

        switch (method)
        {
            case "initialize":
                return new McpSeen(200, InitializeView(client.NegotiatedProtocolVersion, client.ServerInfo?.Name, client.ServerInstructions), JsonText.Digest(true, null, Array.Empty<string>()));
            case "tools/list":
                IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
                return new McpSeen(200, new JsonArray(tools.Select(t => (JsonNode)JsonValue.Create(t.Name)!).ToArray()), JsonText.Digest(true, null, Array.Empty<string>()));
            case "tools/call":
                JsonNode parameters = message["params"]!;
                var request = new CallToolRequestParams
                {
                    Name = parameters["name"]!.GetValue<string>(),
                    Arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(parameters["arguments"]?.ToJsonString() ?? "{}"),
                };
                CallToolResult result = await client.CallToolAsync(request, TestContext.Current.CancellationToken);
                JsonNode? structured = result.StructuredContent == null ? null : JsonSerializer.SerializeToNode(result.StructuredContent);
                return new McpSeen(200, structured, JsonText.McpDigest(new JsonObject { ["result"] = new JsonObject { ["structuredContent"] = structured?.DeepClone() } }));
            default:
                throw new InvalidOperationException("The SDK channel does not replay " + method + ".");
        }
    }

    public McpSeen ViewOf(int status, JsonNode? body, string method)
    {
        if (status != 200)
        {
            return new McpSeen(status, null, JsonText.Digest(false, "refused at connect", Array.Empty<string>()));
        }

        JsonNode? result = body?["result"];
        switch (method)
        {
            case "initialize":
                return new McpSeen(status, InitializeView(result?["protocolVersion"]?.GetValue<string>(), result?["serverInfo"]?["name"]?.GetValue<string>(), result?["instructions"]?.GetValue<string>()), JsonText.McpDigest(body));
            case "tools/list":
                return new McpSeen(status, new JsonArray(result!["tools"]!.AsArray().Select(t => (JsonNode)JsonValue.Create(t!["name"]!.GetValue<string>())!).ToArray()), JsonText.McpDigest(body));
            default:
                return new McpSeen(status, result?["structuredContent"]?.DeepClone(), JsonText.McpDigest(body));
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (McpClient client in _clients.Values)
        {
            await client.DisposeAsync();
        }

        _clients.Clear();
    }

    private static JsonObject InitializeView(string? protocolVersion, string? serverName, string? instructions)
    {
        return new JsonObject { ["protocolVersion"] = protocolVersion, ["serverName"] = serverName, ["instructions"] = instructions };
    }

    private async Task<McpClient> Client(Persona persona)
    {
        if (!_clients.TryGetValue(persona.Name, out McpClient? client))
        {
            client = await Connect(_host, persona, _protocolVersion, _standaloneGetStream);
            _clients[persona.Name] = client;
        }

        return client;
    }
}
