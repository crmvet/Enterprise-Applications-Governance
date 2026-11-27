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
using Product.Engine.Runtime.Callers;
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
/// contracts as the MCP endpoint, plus the caller's session and the service's facts. A token the web app itself asked
/// for (its <c>azp</c> is one of <c>Product:WebApp:ClientIds</c>) is the person entry: the engine runs as the person,
/// with no On Behalf Of, and the person reads and writes here (D-36), and answers the approval requests they were asked
/// (D-38). Any other client is an AI client acting for the person, as on the MCP endpoint, and must pass the Known Client
/// check; it reads here and writes through the MCP endpoint, and it never answers an approval. In a demo install the
/// person may act as a fictional demo persona (D-39, <see cref="ProductDemo"/>): the caller middleware has then already
/// put the persona in place of the person, and everything here runs as the persona.
/// </summary>
public static class ApiEndpoints
{
    /// <summary>
    /// <c>POST /api/{tool}</c>: one tool call with the JSON body as its arguments, through the same MCP server core and
    /// in-process backend as the MCP endpoint (Tool Policy, roles, rate limit, input schema, envelope), with the
    /// <c>api.access</c> scope. The body of the answer is the tool's envelope; the status is 200 when it succeeded, else
    /// the fixed error code's status. On the person entry every tool the Tool Policy gives the person is served, write
    /// tools included (D-36): the person does their work in the web app, and the Tool Policy roles and the engine's guards
    /// check each write exactly as they check an AI client's on the MCP endpoint. The person answers an approval request
    /// they were asked here too, through <c>answer_approval</c> (D-38), a tool served only on the person entry; the
    /// engine checks that the signed-in person is the one the request asks. An AI client is served the read tools here
    /// and writes through the MCP endpoint, so a write tool answers not_found for it, and <c>answer_approval</c> answers
    /// ai_cannot_do_this: AI never approves.
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
        bool person = PersonEntry(context);
        McpServerCore core = McpInvoker.CreateCore(context, auth.ApiScope, person);

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

        // An AI client writes through the MCP endpoint, so here it gets the read tools only. The person entry gets the write
        // tools too, and the core and the engine check them as on the MCP endpoint (D-36). The refusal comes only once the
        // caller is known to be signed in, so a missing token still answers 401. A tool served only to a person
        // (answer_approval, D-38) goes on to the core, which refuses an AI client with ai_cannot_do_this: AI never approves.
        bool known = contracts.TryGetTool(tool, out ToolContract contract);
        if (!person && known && IsWrite(contract) && !contract.IsPersonOnly && SignedIn(context, auth.ApiScope))
        {
            string refusal = "The tool " + contract.Name + " is served on the MCP endpoint. On the REST API an AI client calls the read tools; a person writes through the product's web app.";
            await HttpJson.WriteFailure(context, new McpError(McpErrorCodes.NotFound, refusal, "tool"), contracts).ConfigureAwait(false);
            return;
        }

        // Step-up sign-in for a person's approval answer (D-38: a setting, off in the demo; document 26, B26-9). With
        // the setting on, an answer from a sign-in older than the setting allows, or without the authentication context it
        // names, records nothing and answers step_up_required, which the web app meets by signing the person in again. A
        // check (validateOnly) writes nothing, so it needs no fresh sign-in.
        if (person && known && contract.IsPersonOnly && string.Equals(contract.Name, "answer_approval", StringComparison.Ordinal) && Js.Flag(arguments, "validateOnly") != true)
        {
            ProductStepUpOptions stepUp = services.GetRequiredService<IOptions<ProductWebAppOptions>>().Value.StepUp ?? new ProductStepUpOptions();
            StepUpResult check = StepUpCheck.Evaluate(context.User, stepUp, DateTime.UtcNow);
            if (!check.Satisfied)
            {
                await HttpJson.WriteFailure(context, new McpError(McpErrorCodes.StepUpRequired, check.Message!, claimsChallenge: check.ClaimsChallenge), contracts).ConfigureAwait(false);
                return;
            }
        }

        JsonTreeNode message = Js.Obj(
            ("jsonrpc", JsonTreeNode.FromString("2.0")),
            ("id", JsonTreeNode.FromInt64(1)),
            ("method", JsonTreeNode.FromString("tools/call")),
            ("params", Js.Obj(("name", JsonTreeNode.FromString(tool)), ("arguments", arguments))));
        McpResponse response;
        using (AnsweringApproval(context, person, known ? contract : null, arguments))
        {
            response = McpInvoker.Invoke(core, McpInvoker.Request(message, context), context);
        }
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
    /// <c>GET /api/me</c>: the signed-in person's name, the actor kind of this entry (<c>person</c> through the web app,
    /// <c>ai-client-for-person</c> for any other client), the product roles, each with the group that grants it, and the
    /// configuration in force. Roles come only from the configuration's Role Group Bindings and the token's groups. The
    /// name is the person's Principal Reference label, else the token's <c>name</c> claim. When a demo operator acts as a
    /// demo persona (D-39), every field is the persona's, and <c>demoPersona</c> names it; otherwise it is null.
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
        CallerEntry entry = PersonEntry(context) ? CallerEntry.Person : CallerEntry.AiClient;
        var backendCaller = new BackendCaller(entry, caller.ObjectId, facts.DelegatedKindOf(caller, caller.ObjectId), caller.ClientAppId, caller.GroupObjectIds, caller.DirectoryRoleIds);
        SessionResult opened = services.GetRequiredService<InProcessBackend>().OpenSession(backendCaller, ProductRequest.CorrelationId(context));
        if (opened.Session == null)
        {
            return HttpJson.WriteFailure(context, opened.Error!, contracts);
        }

        BackendSession session = opened.Session;

        // A demo persona (D-39) is named by its Principal Reference, else its label; never by the operator's own token.
        DemoPersonaUse? demo = caller.DemoPersona;
        string? name = facts.DisplayLabel(caller.ObjectId) ?? (demo != null ? demo.Label : context.User.FindFirst("name")?.Value);
        JsonTreeNode body = Js.Obj(
            ("objectId", JsonTreeNode.FromString(caller.ObjectId)),
            ("name", Js.Str(string.IsNullOrWhiteSpace(name) ? null : name)),
            ("actorKind", JsonTreeNode.FromString(session.ActorKind)),
            ("demoPersona", demo == null ? JsonTreeNode.Null : Js.Obj(
                ("name", JsonTreeNode.FromString(demo.Name)),
                ("label", JsonTreeNode.FromString(demo.Label)))),
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

    // True when the request is the person acting through the product's own web app (CallerFacts.IsPersonEntry).
    private static bool PersonEntry(HttpContext context)
    {
        ProductWebAppOptions webApp = context.RequestServices.GetRequiredService<IOptions<ProductWebAppOptions>>().Value;
        return CallerFacts.IsPersonEntry(ProductRequest.Caller(context), context.Request.Path, webApp);
    }

    // A person's own answer to an approval request (answer_approval on the person entry, D-38) names the Approval Record
    // it answers in the store call context, for this request only. The Azure SQL store writes it to SESSION_CONTEXT, and
    // row-level security lets this request, and no other person's request, write the records of that record's decision
    // stamped with this caller: the answer on the record named and the recompute the engine runs on that decision. The ID
    // is the caller's argument, not yet checked here: the engine's guard decides; the predicate is the backstop. Null
    // otherwise.
    private static IDisposable? AnsweringApproval(HttpContext context, bool person, ToolContract? tool, JsonTreeNode arguments)
    {
        if (!person || tool == null || !tool.IsPersonOnly || !string.Equals(tool.Name, "answer_approval", StringComparison.Ordinal))
        {
            return null;
        }

        string? id = Js.Text(arguments, "approvalRecordId");
        StoreCallContext? current = StoreCallContext.Current;
        if (current == null || string.IsNullOrEmpty(id) || id!.Length > 64)
        {
            return null;
        }

        return StoreCallContext.Enter(current.WithAnsweredApproval(id));
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
