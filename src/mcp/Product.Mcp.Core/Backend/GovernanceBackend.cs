using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Runtime.Callers;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Envelope;
using Product.Mcp.Core.Policy;

namespace Product.Mcp.Core.Backend;

// The backend port (document 24, sections 4.4 and 9). The server core talks to the product only
// through it:
// - Dataverse binding: one Web API call per tool to the custom API <prefix>_<ToolName>, as the person, with an
//   on-behalf-of token (CustomApiBinding names the APIs; that client is not built yet).
// - Azure binding: the engine in the same process (InProcess/InProcessBackend), over the runtime's storage ports.
// The core holds no rule and no row: roles, Tool Policy, Known Clients and every answer come from the backend per call.
public interface IGovernanceBackend
{
    // Opens one request: loads the configuration in force, resolves the caller's roles from it, and returns what the
    // core needs to filter tools. Fails with configuration_unavailable when no valid version is in force.
    SessionResult OpenSession(BackendCaller caller, string correlationId);

    // Runs one tool for the session's caller. The core has already checked the policy, the rate limit and the input
    // schema; the backend still checks every rule (the engine refuses what a client must not do, whatever the tool).
    ToolResult Invoke(BackendSession session, ToolInvocation invocation);

    // Reads one R1 resource. Null data with not_found for an unknown or hidden URI.
    ResourceResult ReadResource(BackendSession session, string uri, string correlationId);

    // The rate limit counter, kept in the backend's store, never in the server's memory.
    IRateLimitStore RateLimits { get; }
}

// The validated caller as the backend needs it. Never a token.
public sealed class BackendCaller
{
    // A caller on the MCP endpoint: an AI client acting for the signed-in person.
    public BackendCaller(string objectId, PrincipalKind principalKind, string clientAppId, IEnumerable<string>? groupObjectIds = null, IEnumerable<string>? directoryRoleIds = null)
        : this(CallerEntry.AiClient, objectId, principalKind, clientAppId, groupObjectIds, directoryRoleIds)
    {
    }

    // A caller on the given entry: AiClient (the MCP endpoint) or Person (the product's web app). Workload is refused: an
    // app-only caller never reaches the backend, it uses the record and approval outcome endpoints.
    public BackendCaller(CallerEntry entry, string objectId, PrincipalKind principalKind, string clientAppId, IEnumerable<string>? groupObjectIds = null, IEnumerable<string>? directoryRoleIds = null)
    {
        if (entry != CallerEntry.AiClient && entry != CallerEntry.Person)
        {
            throw new ArgumentException("The backend serves the AI client and person entries only. An app-only caller uses the record and approval outcome endpoints.", nameof(entry));
        }

        if (string.IsNullOrEmpty(objectId))
        {
            throw new ArgumentException("A caller has an object ID.", nameof(objectId));
        }

        Entry = entry;
        ObjectId = objectId;
        PrincipalKind = principalKind;
        ClientAppId = clientAppId ?? throw new ArgumentNullException(nameof(clientAppId));
        GroupObjectIds = new ReadOnlyCollection<string>((groupObjectIds ?? Enumerable.Empty<string>()).ToList());
        DirectoryRoleIds = new ReadOnlyCollection<string>((directoryRoleIds ?? Enumerable.Empty<string>()).ToList());
    }

    // How the call reached the product: AiClient on the MCP endpoint, Person through the web app. The actor kind comes
    // from it, never from a field the caller sends.
    public CallerEntry Entry { get; }

    public string ObjectId { get; }

    public PrincipalKind PrincipalKind { get; }

    public string ClientAppId { get; }

    public IReadOnlyList<string> GroupObjectIds { get; }

    public IReadOnlyList<string> DirectoryRoleIds { get; }

    // The client's label, set by the core: from the Known Client check once it passed on the MCP endpoint, or the host's
    // web app label on the person entry.
    public string? ClientAppLabel { get; internal set; }
}

// A product role the caller holds, with the binding that grants it (document 24, tool 1: viaGroup).
public sealed class RoleGrant
{
    public RoleGrant(string roleKey, string label, string groupObjectId, string groupLabel)
    {
        RoleKey = roleKey;
        Label = label;
        GroupObjectId = groupObjectId;
        GroupLabel = groupLabel;
    }

    public string RoleKey { get; }

    public string Label { get; }

    public string GroupObjectId { get; }

    public string GroupLabel { get; }
}

// What the core needs for one request, from the configuration in force.
public sealed class BackendSession
{
    public BackendSession(
        BackendCaller caller,
        string actorKind,
        IEnumerable<RoleGrant> roles,
        string configurationSetKey,
        int configurationSetVersion,
        string contentHash,
        DateTime? publishedOnUtc,
        ToolPolicySet toolPolicy,
        KnownClientCheck knownClients,
        int pageSizeMax,
        DateTime asOfUtc,
        object? state = null)
    {
        Caller = caller ?? throw new ArgumentNullException(nameof(caller));
        ActorKind = actorKind;
        Roles = new ReadOnlyCollection<RoleGrant>((roles ?? Enumerable.Empty<RoleGrant>()).ToList());
        RoleKeys = new ReadOnlyCollection<string>(Roles.Select(r => r.RoleKey).Distinct(StringComparer.Ordinal).OrderBy(r => r, StringComparer.Ordinal).ToList());
        ConfigurationSetKey = configurationSetKey;
        ConfigurationSetVersion = configurationSetVersion;
        ContentHash = contentHash;
        PublishedOnUtc = publishedOnUtc;
        ToolPolicy = toolPolicy ?? throw new ArgumentNullException(nameof(toolPolicy));
        KnownClients = knownClients ?? throw new ArgumentNullException(nameof(knownClients));
        PageSizeMax = pageSizeMax;
        AsOfUtc = asOfUtc;
        State = state;
    }

    public BackendCaller Caller { get; }

    // The actor kind the engine stamps for this caller on this entry point: ai-client-for-person on the MCP endpoint,
    // person on the web app's entry, agent-identity for an agent user or agent identity on either (it holds no role,
    // RA-06).
    public string ActorKind { get; }

    public IReadOnlyList<RoleGrant> Roles { get; }

    public IReadOnlyList<string> RoleKeys { get; }

    public string ConfigurationSetKey { get; }

    public int ConfigurationSetVersion { get; }

    public string ContentHash { get; }

    public DateTime? PublishedOnUtc { get; }

    public ToolPolicySet ToolPolicy { get; }

    public KnownClientCheck KnownClients { get; }

    // The mcp-page-size-max setting (document 24, section 3.3).
    public int PageSizeMax { get; }

    public DateTime AsOfUtc { get; }

    // Backend-specific state for the request (the in-process backend keeps nothing here between requests).
    public object? State { get; }
}

public sealed class SessionResult
{
    private SessionResult(BackendSession? session, McpError? error)
    {
        Session = session;
        Error = error;
    }

    public BackendSession? Session { get; }

    public McpError? Error { get; }

    public static SessionResult Opened(BackendSession session)
    {
        return new SessionResult(session ?? throw new ArgumentNullException(nameof(session)), null);
    }

    public static SessionResult Failed(McpError error)
    {
        return new SessionResult(null, error ?? throw new ArgumentNullException(nameof(error)));
    }
}

// One tool call as the core passes it on.
public sealed class ToolInvocation
{
    public ToolInvocation(ToolContract tool, JsonTreeNode arguments, string correlationId, IEnumerable<string> visibleTools, IEnumerable<string> visibleToolsets)
    {
        Tool = tool ?? throw new ArgumentNullException(nameof(tool));
        Arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
        CorrelationId = correlationId ?? throw new ArgumentNullException(nameof(correlationId));
        VisibleTools = new ReadOnlyCollection<string>(visibleTools.ToList());
        VisibleToolsets = new ReadOnlyCollection<string>(visibleToolsets.ToList());
    }

    public ToolContract Tool { get; }

    public JsonTreeNode Arguments { get; }

    public string CorrelationId { get; }

    // The tools and toolsets this request may see (roles, Tool Policy and X-Toolsets), for whoami.
    public IReadOnlyList<string> VisibleTools { get; }

    public IReadOnlyList<string> VisibleToolsets { get; }
}

// A warning of the envelope: { message, code?, ref? }.
public sealed class ToolWarning
{
    public ToolWarning(string message, string? code = null, string? reference = null)
    {
        Message = McpError.Clip(string.IsNullOrWhiteSpace(message) ? "Note." : message, 1000);
        Code = code;
        Ref = reference == null ? null : McpError.Clip(reference, 200);
    }

    public string Message { get; }

    public string? Code { get; }

    public string? Ref { get; }
}

// A suggested next tool with its key inputs.
public sealed class NextStep
{
    public NextStep(string tool, JsonTreeNode inputs)
    {
        Tool = tool;
        Inputs = inputs;
    }

    public string Tool { get; }

    public JsonTreeNode Inputs { get; }
}

// What a tool call returned: data, or a fixed error.
public sealed class ToolResult
{
    private ToolResult(
        JsonTreeNode? data,
        McpError? error,
        string summary,
        string? recordUrl,
        IEnumerable<ToolWarning>? warnings,
        IEnumerable<NextStep>? nextSteps,
        IEnumerable<KeyValuePair<string, int>>? couldNotRead,
        int? configurationSetVersion)
    {
        Data = data;
        Error = error;
        Summary = summary;
        RecordUrl = recordUrl;
        Warnings = new ReadOnlyCollection<ToolWarning>((warnings ?? Enumerable.Empty<ToolWarning>()).ToList());
        NextSteps = new ReadOnlyCollection<NextStep>((nextSteps ?? Enumerable.Empty<NextStep>()).Take(3).ToList());
        CouldNotRead = new ReadOnlyCollection<KeyValuePair<string, int>>((couldNotRead ?? Enumerable.Empty<KeyValuePair<string, int>>()).Where(c => c.Value > 0).ToList());
        ConfigurationSetVersion = configurationSetVersion;
    }

    public bool Ok => Error == null;

    public JsonTreeNode? Data { get; }

    public McpError? Error { get; }

    public string Summary { get; }

    public string? RecordUrl { get; }

    public IReadOnlyList<ToolWarning> Warnings { get; }

    public IReadOnlyList<NextStep> NextSteps { get; }

    // Rows or sections the caller could not read, by kind and count (never their content).
    public IReadOnlyList<KeyValuePair<string, int>> CouldNotRead { get; }

    // The version the answer ran under, when it differs from the session's (a version activated by this very call).
    public int? ConfigurationSetVersion { get; }

    public static ToolResult Success(
        JsonTreeNode data,
        string summary,
        string? recordUrl = null,
        IEnumerable<ToolWarning>? warnings = null,
        IEnumerable<NextStep>? nextSteps = null,
        IEnumerable<KeyValuePair<string, int>>? couldNotRead = null,
        int? configurationSetVersion = null)
    {
        return new ToolResult(data ?? throw new ArgumentNullException(nameof(data)), null, summary, recordUrl, warnings, nextSteps, couldNotRead, configurationSetVersion);
    }

    public static ToolResult Failure(McpError error, string? summary = null, int? configurationSetVersion = null)
    {
        return new ToolResult(null, error ?? throw new ArgumentNullException(nameof(error)), summary ?? error.Message, null, null, null, null, configurationSetVersion);
    }
}

// What a resource read returned: the JSON content, or a fixed error.
public sealed class ResourceResult
{
    private ResourceResult(JsonTreeNode? content, McpError? error)
    {
        Content = content;
        Error = error;
    }

    public JsonTreeNode? Content { get; }

    public McpError? Error { get; }

    public static ResourceResult Found(JsonTreeNode content)
    {
        return new ResourceResult(content ?? throw new ArgumentNullException(nameof(content)), null);
    }

    public static ResourceResult Failure(McpError error)
    {
        return new ResourceResult(null, error ?? throw new ArgumentNullException(nameof(error)));
    }
}
