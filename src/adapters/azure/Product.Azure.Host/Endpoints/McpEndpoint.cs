using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Product.Engine.Core.Canonical;
using Product.Mcp.Core.Json;
using Product.Mcp.Core.Protocol;

namespace Product.Azure.Host;

/// <summary>
/// The MCP endpoint over streamable HTTP, stateless, answered by the MCP server core: every POST carries one JSON-RPC
/// message and its own bearer token, and the answer is one JSON body (202 with no body for a notification). There is no
/// server stream and no session, so GET and DELETE answer 405. The core does the token rules, the session from the
/// configuration in force, the Known Client check, Tool Policy, the rate limit, the input schema and the envelope; the
/// in-process backend runs each tool as one engine call.
/// </summary>
public static class McpEndpoint
{
    /// <summary>Handles a POST to the MCP endpoint.</summary>
    public static async Task HandlePost(HttpContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        McpHttpResponse? refused = await Precheck(context).ConfigureAwait(false);
        if (refused != null)
        {
            await Write(context, refused).ConfigureAwait(false);
            return;
        }

        string? body = await HttpJson.ReadBody(context, Options(context).MaxRequestBytes).ConfigureAwait(false);
        if (body == null)
        {
            await Write(context, RpcError(413, JsonRpcCodes.InvalidRequest, "The request body is larger than this server reads.")).ConfigureAwait(false);
            return;
        }

        McpServerCore core = McpInvoker.CreateCore(context, context.RequestServices.GetRequiredService<IOptions<ProductAuthOptions>>().Value.McpScope);
        await Write(context, Handle(core, body, context)).ConfigureAwait(false);
    }

    /// <summary>Answers GET and DELETE on the MCP endpoint: 405, with <c>Allow: POST</c>. The endpoint has no stream and no session.</summary>
    public static Task HandleNotAllowed(HttpContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
        context.Response.Headers["Allow"] = "POST";
        return Task.CompletedTask;
    }

    /// <summary>Serves the protected resource metadata (RFC 9728) of the MCP endpoint. Anonymous.</summary>
    public static Task HandleMetadata(HttpContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        McpServerCore core = McpInvoker.CreateCore(context, context.RequestServices.GetRequiredService<IOptions<ProductAuthOptions>>().Value.McpScope);
        context.Response.Headers["Cache-Control"] = "public, max-age=3600";
        return HttpJson.Write(context, StatusCodes.Status200OK, core.ProtectedResourceMetadata());
    }

    // The transport checks before the body is read: the Origin header and the media type.
    internal static Task<McpHttpResponse?> Precheck(HttpContext context)
    {
        if (!McpInvoker.OriginAllowed(context))
        {
            return Task.FromResult<McpHttpResponse?>(RpcError(StatusCodes.Status403Forbidden, JsonRpcCodes.InvalidRequest, "The request's Origin is not allowed to call this server."));
        }

        if (!HttpJson.IsJson(context.Request.ContentType))
        {
            return Task.FromResult<McpHttpResponse?>(RpcError(StatusCodes.Status415UnsupportedMediaType, JsonRpcCodes.InvalidRequest, "Send the JSON-RPC message as application/json."));
        }

        return Task.FromResult<McpHttpResponse?>(null);
    }

    // One message through the core's own JSON-RPC handling. The engine and the storage ports are synchronous, so the call
    // runs inline once the body has been read.
    // The same handling as the core's pass-through adapter (PassThroughMcpAdapter): a body that does not parse is the
    // JSON-RPC parse error, a batch is refused, and one message goes to the core with the X-Toolsets header. The host
    // runs the core itself so it can log a server failure with the correlation ID.
    internal static McpHttpResponse Handle(McpServerCore core, string body, HttpContext context)
    {
        if (!CanonicalJson.TryParse(body, out JsonTreeNode? message, out _))
        {
            return RpcError(StatusCodes.Status400BadRequest, JsonRpcCodes.ParseError, "The body is not JSON.");
        }

        if (message.Kind != JsonTreeKind.Object)
        {
            return RpcError(StatusCodes.Status400BadRequest, JsonRpcCodes.InvalidRequest, "Send one JSON-RPC message per request; batches are not served.");
        }

        McpResponse response = McpInvoker.Invoke(core, McpInvoker.Request(message, context), context);
        return new McpHttpResponse(response.HttpStatus, response.Body == null ? null : Js.Write(response.Body), new Dictionary<string, string>(response.Headers, StringComparer.OrdinalIgnoreCase));
    }

    internal static Task Write(HttpContext context, McpHttpResponse response)
    {
        return HttpJson.WriteText(context, response.Status, response.Body, response.Headers);
    }

    internal static McpHttpResponse RpcError(int status, int code, string message)
    {
        JsonTreeNode body = Js.Obj(
            ("jsonrpc", JsonTreeNode.FromString("2.0")),
            ("id", JsonTreeNode.Null),
            ("error", Js.Obj(("code", JsonTreeNode.FromInt64(code)), ("message", JsonTreeNode.FromString(message)))));
        return new McpHttpResponse(status, Js.Write(body));
    }

    private static ProductHostOptions Options(HttpContext context)
    {
        return context.RequestServices.GetRequiredService<IOptions<ProductHostOptions>>().Value;
    }
}
