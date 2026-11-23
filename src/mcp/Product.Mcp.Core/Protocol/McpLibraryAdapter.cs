using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Product.Engine.Core.Canonical;
using Product.Mcp.Core.Json;

namespace Product.Mcp.Core.Protocol;

// The thin adapter between an MCP library and the server core. The MCP library sits behind it until one is chosen.
// The core speaks JSON-RPC messages; a library brings the transport, session handling and its own types. Whatever
// library is chosen plugs in here, and nothing else in the core changes.
//
// No MCP library is referenced yet: PassThroughMcpAdapter is the stub. It reads one streamable HTTP POST to the endpoint
// and hands the message straight to McpServerCore. A host (Azure Functions or Container Apps) maps its HTTP request to
// McpHttpRequest and writes McpHttpResponse back.
public interface IMcpLibraryAdapter
{
    // The library behind the adapter, for logs and the setup guide.
    string LibraryName { get; }

    McpHttpResponse Handle(McpHttpRequest request);
}

// One HTTP request to the MCP endpoint: the two headers the core reads and the body text. Never logged.
public sealed class McpHttpRequest
{
    public McpHttpRequest(string body, string? authorization = null, string? toolsets = null)
    {
        Body = body ?? string.Empty;
        Authorization = authorization;
        Toolsets = toolsets;
    }

    public string Body { get; }

    public string? Authorization { get; }

    public string? Toolsets { get; }
}

public sealed class McpHttpResponse
{
    public McpHttpResponse(int status, string? body, IDictionary<string, string>? headers = null)
    {
        Status = status;
        Body = body;
        Headers = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(headers ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase));
    }

    public int Status { get; }

    // JSON text, or null for 202 Accepted (a notification).
    public string? Body { get; }

    public IReadOnlyDictionary<string, string> Headers { get; }
}

// The stub until an MCP library is chosen: parses the body with the engine's JSON reader and calls the core. A body
// that does not parse is the JSON-RPC parse error; a batch (a JSON array) is refused, because the protocol version the
// core answers has no batches.
public sealed class PassThroughMcpAdapter : IMcpLibraryAdapter
{
    private readonly McpServerCore _core;

    public PassThroughMcpAdapter(McpServerCore core)
    {
        _core = core ?? throw new ArgumentNullException(nameof(core));
    }

    public string LibraryName => "none (pass-through stub until an MCP library is chosen)";

    public McpHttpResponse Handle(McpHttpRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (!CanonicalJson.TryParse(request.Body, out JsonTreeNode? message, out _))
        {
            return Error(JsonRpcCodes.ParseError, "The body is not JSON.");
        }

        if (message.Kind != JsonTreeKind.Object)
        {
            return Error(JsonRpcCodes.InvalidRequest, "Send one JSON-RPC message per request; batches are not served.");
        }

        McpResponse response = _core.Handle(new McpRequest(message, request.Authorization, request.Toolsets));
        return new McpHttpResponse(response.HttpStatus, response.Body == null ? null : Js.Write(response.Body), new Dictionary<string, string>(response.Headers, StringComparer.OrdinalIgnoreCase));
    }

    private static McpHttpResponse Error(int code, string message)
    {
        JsonTreeNode body = Js.Obj(
            ("jsonrpc", JsonTreeNode.FromString("2.0")),
            ("id", JsonTreeNode.Null),
            ("error", Js.Obj(("code", JsonTreeNode.FromInt64(code)), ("message", JsonTreeNode.FromString(message)))));
        return new McpHttpResponse(400, Js.Write(body));
    }
}
