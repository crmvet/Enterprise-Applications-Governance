using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Product.Engine.Core.Canonical;
using Product.Mcp.Core.Json;

namespace Product.Mcp.Core.Protocol;

// One HTTP request to the MCP endpoint (streamable HTTP, one path, document 24, section 5): the headers the core reads and
// one JSON-RPC 2.0 message. Every request carries its own bearer token; the core keeps no session between requests.
public sealed class McpRequest
{
    public McpRequest(JsonTreeNode message, string? authorization = null, string? toolsetsHeader = null)
    {
        Message = message ?? throw new ArgumentNullException(nameof(message));
        Authorization = authorization;
        ToolsetsHeader = toolsetsHeader;
    }

    // The Authorization header, as sent. The core passes it to the token validator and never stores or forwards it.
    public string? Authorization { get; }

    // The X-Toolsets header, as sent.
    public string? ToolsetsHeader { get; }

    public JsonTreeNode Message { get; }

    public static McpRequest Call(int id, string method, JsonTreeNode? parameters, string? authorization, string? toolsetsHeader = null)
    {
        return new McpRequest(
            Js.Obj(("jsonrpc", JsonTreeNode.FromString("2.0")), ("id", JsonTreeNode.FromInt64(id)), ("method", JsonTreeNode.FromString(method)), ("params", parameters)),
            authorization,
            toolsetsHeader);
    }
}

// The HTTP answer: a status, the JSON-RPC response (none for a notification) and the headers the core sets.
public sealed class McpResponse
{
    public McpResponse(int httpStatus, JsonTreeNode? body, IDictionary<string, string>? headers = null)
    {
        HttpStatus = httpStatus;
        Body = body;
        Headers = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(headers ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase));
    }

    public int HttpStatus { get; }

    public JsonTreeNode? Body { get; }

    public IReadOnlyDictionary<string, string> Headers { get; }

    // The result member of a JSON-RPC response, or null for an error.
    public JsonTreeNode? Result => Js.Member(Body, "result");

    // The error member of a JSON-RPC response, or null.
    public JsonTreeNode? Error => Js.Member(Body, "error");
}

// JSON-RPC 2.0 error codes the core uses, and the MCP resource not found code.
public static class JsonRpcCodes
{
    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;

    // MCP: "Resource not found".
    public const int ResourceNotFound = -32002;
}
