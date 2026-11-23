using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Product.Azure.Stores;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Runtime;
using Product.Engine.Runtime.Ports;
using Product.Engine.Runtime.Snapshots;
using Product.Mcp.Core.Backend;
using Product.Mcp.Core.Backend.InProcess;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Envelope;
using Product.Mcp.Core.Identity;
using Product.Mcp.Core.Json;
using Product.Mcp.Core.Protocol;

namespace Product.Azure.Host;

/// <summary>
/// The REST API for the product's web app (delegated tokens with the <c>api.access</c> scope): the same tools and
/// contracts as the MCP endpoint, plus the caller's session and the service's facts.
/// </summary>
public static class ApiEndpoints
{
    /// <summary>
    /// <c>POST /api/{tool}</c>: one tool call with the JSON body as its arguments, through the same MCP server core and
    /// in-process backend as the MCP endpoint (Tool Policy, roles, rate limit, input schema, envelope), with the
    /// <c>api.access</c> scope. The body of the answer is the tool's envelope; the status is 200 when it succeeded, else
    /// the fixed error code's status. This release serves read tools here; a write tool answers not_found, because a
    /// person's write needs the caller entry the MCP core does not take yet (ENGINE-CHANGES-NEEDED.md, item 1).
    /// </summary>
    public static async Task HandleTool(HttpContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        IServiceProvider services = context.RequestServices;
        ContractSet contracts = services.GetRequiredService<ContractSet>();
        ProductAuthOptions auth = services.GetRequiredService<IOptions<ProductAuthOptions>>().Value;
        string tool = context.Request.RouteValues.TryGetValue("tool", out object? value) ? value as string ?? string.Empty : string.Empty;
        McpServerCore core = McpInvoker.CreateCore(context, auth.ApiScope);

        string? text = await HttpJson.ReadBody(context, services.GetRequiredService<IOptions<ProductHostOptions>>().Value.MaxRequestBytes).ConfigureAwait(false);
        if (text == null)
        {
            await HttpJson.WriteFailure(context, new McpError(McpErrorCodes.Validation, "The request body is larger than this server reads.", "body"), contracts).ConfigureAwait(false);
            return;
        }

        JsonTreeNode arguments = Js.EmptyObject;
        if (!string.IsNullOrWhiteSpace(text))
        {
            if (!HttpJson.IsJson(context.Request.ContentType) || !CanonicalJson.TryParse(text, out JsonTreeNode? parsed, out _) || parsed.Kind != JsonTreeKind.Object)
            {
                await HttpJson.WriteFailure(context, new McpError(McpErrorCodes.Validation, "The body is the tool's arguments as one JSON object, sent as application/json.", "body"), contracts).ConfigureAwait(false);
                return;
            }

            arguments = parsed;
        }

        // A write tool is refused only once the caller is known to be signed in, so a missing token still answers 401.
        if (contracts.TryGetTool(tool, out ToolContract contract) && IsWrite(contract) && SignedIn(context, auth.ApiScope))
        {
            await HttpJson.WriteFailure(context, new McpError(McpErrorCodes.NotFound, "The tool " + contract.Name + " is served on the MCP endpoint in this release. The REST API serves the read tools.", "tool"), contracts).ConfigureAwait(false);
            return;
        }

        JsonTreeNode message = Js.Obj(
            ("jsonrpc", JsonTreeNode.FromString("2.0")),
            ("id", JsonTreeNode.FromInt64(1)),
            ("method", JsonTreeNode.FromString("tools/call")),
            ("params", Js.Obj(("name", JsonTreeNode.FromString(tool)), ("arguments", arguments))));
        McpResponse response = McpInvoker.Invoke(core, McpInvoker.Request(message, context), context);
        var headers = new Dictionary<string, string>(response.Headers, StringComparer.OrdinalIgnoreCase);

        JsonTreeNode? envelope = Js.Member(response.Result, "structuredContent");
        if (envelope != null)
        {
            bool ok = Js.Flag(envelope, "ok") == true;
            JsonTreeNode? error = Js.Member(envelope, "error");
            int status = ok ? StatusCodes.Status200OK : ProductErrorMap.HttpStatus(Js.Text(error, "code") ?? McpErrorCodes.RuleViolation, contracts);
            if (Js.Number(error, "retryAfterSeconds") is long retry)
            {
                headers["Retry-After"] = retry.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            await HttpJson.Write(context, status, envelope, headers).ConfigureAwait(false);
            return;
        }

        // A sign-in refusal (401, 403) or a server failure: the core's error data is the failure envelope.
        JsonTreeNode data = Js.Member(response.Error, "data") ?? Envelope.Failure(new McpError(McpErrorCodes.ConfigurationUnavailable, Js.Text(response.Error, "message") ?? "The server failed on this request."), ProductRequest.CorrelationId(context));
        int failureStatus = response.HttpStatus != StatusCodes.Status200OK ? response.HttpStatus : StatusCodes.Status500InternalServerError;
        await HttpJson.Write(context, failureStatus, data, headers).ConfigureAwait(false);
    }

    /// <summary>
    /// <c>GET /api/me</c>: the signed-in person's product roles, each with the group that grants it, and the configuration
    /// in force. Roles come only from the configuration's Role Group Bindings and the token's groups.
    /// </summary>
    public static Task HandleMe(HttpContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        IServiceProvider services = context.RequestServices;
        ContractSet contracts = services.GetRequiredService<ContractSet>();
        CallerClaims? caller = ProductRequest.Caller(context);
        if (caller == null)
        {
            return HttpJson.WriteFailure(context, new McpError(McpErrorCodes.NotSignedIn, "Sign in with a bearer token."), contracts);
        }

        CallerFacts facts = services.GetRequiredService<CallerFacts>();
        var backendCaller = new BackendCaller(caller.ObjectId, facts.DelegatedKindOf(caller, caller.ObjectId), caller.ClientAppId, caller.GroupObjectIds, caller.DirectoryRoleIds);
        SessionResult opened = services.GetRequiredService<InProcessBackend>().OpenSession(backendCaller, ProductRequest.CorrelationId(context));
        if (opened.Session == null)
        {
            return HttpJson.WriteFailure(context, opened.Error!, contracts);
        }

        BackendSession session = opened.Session;
        JsonTreeNode body = Js.Obj(
            ("objectId", JsonTreeNode.FromString(caller.ObjectId)),
            ("roles", Js.Arr(session.Roles.Select(r => Js.Obj(
                ("roleKey", JsonTreeNode.FromString(r.RoleKey)),
                ("label", JsonTreeNode.FromString(r.Label)),
                ("viaGroup", Js.Obj(("objectId", JsonTreeNode.FromString(r.GroupObjectId)), ("displayLabel", JsonTreeNode.FromString(r.GroupLabel)))))))),
            ("configurationSetKey", JsonTreeNode.FromString(session.ConfigurationSetKey)),
            ("configurationSetVersion", JsonTreeNode.FromInt64(session.ConfigurationSetVersion)),
            ("asOf", Js.Utc(session.AsOfUtc)),
            ("correlationId", JsonTreeNode.FromString(ProductRequest.CorrelationId(context))));
        return HttpJson.Write(context, StatusCodes.Status200OK, body);
    }

    /// <summary>
    /// <c>GET /api/info</c>: the service's facts for the web app: the engine version, the MCP endpoint and protocol, and
    /// the configuration version in force (or null before version 1).
    /// </summary>
    public static Task HandleInfo(HttpContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        IServiceProvider services = context.RequestServices;
        ProductHostOptions host = services.GetRequiredService<IOptions<ProductHostOptions>>().Value;
        EngineRuntime runtime = services.GetRequiredService<EngineRuntime>();
        DateTime now = CallerFacts.Utc(runtime.Clock.UtcNow);
        SnapshotLoad load = runtime.Cache.Load(services.GetRequiredService<IConfigurationStore>(), SnapshotPurpose.Read, now);
        CompiledSnapshot? snapshot = load.Snapshot;
        JsonTreeNode body = Js.Obj(
            ("serverName", JsonTreeNode.FromString(host.ServerName)),
            ("engineVersion", JsonTreeNode.FromString(EngineInfo.Version)),
            ("mcpEndpoint", JsonTreeNode.FromString(McpInvoker.ResourceUrl(context))),
            ("mcpTransport", JsonTreeNode.FromString(host.Mcp.Transport == ProductMcpTransport.Sdk ? "sdk" : "core")),
            ("mcpProtocolVersion", JsonTreeNode.FromString(McpServerCore.ProtocolVersion)),
            ("configuration", snapshot == null ? JsonTreeNode.Null : Js.Obj(
                ("configurationSetKey", JsonTreeNode.FromString(snapshot.ConfigurationSetKey)),
                ("versionNumber", JsonTreeNode.FromInt64(snapshot.VersionNumber)),
                ("contentHash", JsonTreeNode.FromString(snapshot.ContentHash)))),
            ("asOf", Js.Utc(now)),
            ("correlationId", JsonTreeNode.FromString(ProductRequest.CorrelationId(context))));
        return HttpJson.Write(context, StatusCodes.Status200OK, body);
    }

    private static bool IsWrite(ToolContract tool)
    {
        return tool.IsWrite || Js.Flag(tool.Annotations, "readOnlyHint") != true;
    }

    private static bool SignedIn(HttpContext context, string scope)
    {
        ProductAuthOptions auth = context.RequestServices.GetRequiredService<IOptions<ProductAuthOptions>>().Value;
        CallerClaims? caller = ProductRequest.Caller(context);
        CallerFacts facts = context.RequestServices.GetRequiredService<CallerFacts>();
        return new ClaimsTokenValidator(caller, context.Request.Headers.ContainsKey("Authorization"), scope, oid => facts.DelegatedKindOf(caller, oid), auth.TenantId)
            .Validate(null, DateTime.UtcNow).Status == TokenValidationStatus.Valid;
    }
}

/// <summary>Health probes. Anonymous, status only, no data.</summary>
public static class HealthEndpoints
{
    /// <summary><c>GET /health/live</c>: the process answers.</summary>
    public static Task HandleLive(HttpContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.Response.Headers["Cache-Control"] = "no-store";
        return HttpJson.Write(context, StatusCodes.Status200OK, Js.Obj(("status", JsonTreeNode.FromString("live"))));
    }

    /// <summary><c>GET /health/ready</c>: the store answers. 503 when it does not.</summary>
    public static Task HandleReady(HttpContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        bool ready = context.RequestServices.GetRequiredService<IProductStore>().CanConnect();
        context.Response.Headers["Cache-Control"] = "no-store";
        return HttpJson.Write(context, ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable, Js.Obj(("status", JsonTreeNode.FromString(ready ? "ready" : "unavailable"))));
    }
}
