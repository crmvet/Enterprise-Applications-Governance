using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Product.Engine.Configuration.Presets;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Snapshots;
using Product.Engine.Runtime;
using Product.Engine.Runtime.Callers;
using Product.Engine.Runtime.Ports;
using Product.Engine.Runtime.Snapshots;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Envelope;
using Product.Mcp.Core.Json;
using Product.Mcp.Core.Policy;
using Product.Mcp.Core.Resources;

namespace Product.Mcp.Core.Backend.InProcess;

public sealed class InProcessBackendOptions
{
    // Where links.recordUrl points: the product's page, such as https://<host>/app/records.
    public string RecordUrlBase { get; set; } = "https://localhost/records";

    // Microsoft Graph as the person. Null: get_role_holders, run_owner_check and check_role_bindings answer
    // upstream_unavailable.
    public IDirectoryReader? Directory { get; set; }

    // The Power Platform inventory as the admin. Null: run_gap_check answers upstream_unavailable, and
    // compare_inventory_list is the fallback.
    public IInventoryReader? Inventory { get; set; }

    public PlaybookIndex Playbook { get; set; } = PlaybookIndex.Empty;
}

// The backend of the Azure binding (document 24, sections 4.4, 8.3 and 9.2): the engine in the same process as the MCP
// endpoint, over the runtime's storage ports. Each tool is one composed engine operation (EngineRuntime.Compose): one
// snapshot, one actor, one transaction, every write through OP-08 with its effects, validateOnly as a dry run, and
// clientRequestId as the idempotency key. Nothing is kept between requests.
public sealed class InProcessBackend : IGovernanceBackend
{
    private readonly EngineRuntime _runtime;
    private readonly IRecordStore _records;
    private readonly IConfigurationStore _configuration;

    public InProcessBackend(
        EngineRuntime runtime,
        IRecordStore records,
        IConfigurationStore configuration,
        PresetCatalog catalog,
        IRateLimitStore rateLimits,
        ContractSet? contracts = null,
        InProcessBackendOptions? options = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _records = records ?? throw new ArgumentNullException(nameof(records));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        RateLimits = rateLimits ?? throw new ArgumentNullException(nameof(rateLimits));
        Contracts = contracts ?? ContractSet.Embedded;
        Options = options ?? new InProcessBackendOptions();
    }

    public IRateLimitStore RateLimits { get; }

    public PresetCatalog Catalog { get; }

    public ContractSet Contracts { get; }

    public InProcessBackendOptions Options { get; }

    internal EngineRuntime Runtime => _runtime;

    internal IConfigurationStore Configuration => _configuration;

    // ---- Session ----

    public SessionResult OpenSession(BackendCaller caller, string correlationId)
    {
        if (caller == null)
        {
            throw new ArgumentNullException(nameof(caller));
        }

        DateTime now = Utc(_runtime.Clock.UtcNow);
        SnapshotLoad load = _runtime.Cache.Load(_configuration, SnapshotPurpose.Read, now);
        if (load.Snapshot == null)
        {
            return SessionResult.Failed(new McpError(McpErrorCodes.ConfigurationUnavailable, load.Problem?.Message ?? "No valid configuration version is in force."));
        }

        CompiledSnapshot snapshot = load.Snapshot;
        CallerResolution resolution = CallerResolver.Resolve(snapshot.Snapshot, Identity(caller), now);
        if (resolution.Actor == null)
        {
            return SessionResult.Failed(new McpError(McpErrorCodes.NotAuthorized, resolution.Refusal?.Message ?? "The caller holds no role."));
        }

        var roles = new List<RoleGrant>();
        foreach (RuleTrace trace in resolution.Trace.Where(t => t.Entity == "Role Group Binding"))
        {
            if (snapshot.Snapshot.TryGetRow("role-group-binding", trace.Key, out SnapshotRow? binding))
            {
                string role = Js.Text(binding.Columns, "roleKey")!;
                string group = Js.Text(binding.Columns, "entraGroupObjectId")!;
                string roleLabel = snapshot.Snapshot.TryGetRow("role", role, out SnapshotRow? roleRow) ? Js.Text(roleRow.Columns, "label") ?? role : role;
                roles.Add(new RoleGrant(role, roleLabel, group, GroupLabel(group)));
            }
        }

        string actorKind = resolution.Actor.ActorKind == ActorKindMeaning.AiClientForPerson ? "ai-client-for-person" : EngineContract.ActorKindMeanings.ToWire(resolution.Actor.ActorKind);
        long pageMax = snapshot.Snapshot.TryGetRow("setting", "mcp-page-size-max", out SnapshotRow? pageRow) && pageRow.IsActive ? Js.Number(pageRow.Columns, "value") ?? 100 : 100;
        return SessionResult.Opened(new BackendSession(
            caller,
            actorKind,
            roles,
            snapshot.ConfigurationSetKey,
            snapshot.VersionNumber,
            snapshot.ContentHash,
            PublishedOn(snapshot),
            ToolPolicySet.From(snapshot.Snapshot, Contracts),
            KnownClientCheck.From(snapshot.Snapshot),
            (int)Math.Max(1, Math.Min(pageMax, 1000)),
            now));
    }

    // On the MCP endpoint the caller is an AI client acting for the signed-in person (document 24, section 4.5). An
    // agent user or agent identity keeps its kind and resolves to an actor that holds no role (RA-06).
    internal static CallerIdentity Identity(BackendCaller caller)
    {
        string clientApp = caller.ClientAppLabel ?? caller.ClientAppId;
        return caller.PrincipalKind == PrincipalKind.User
            ? CallerIdentity.AiClient(caller.ObjectId, caller.GroupObjectIds, clientApp, clientAppVerified: true)
            : new CallerIdentity(caller.ObjectId, caller.PrincipalKind, CallerEntry.AiClient, caller.GroupObjectIds, clientApp, true);
    }

    private string GroupLabel(string groupObjectId)
    {
        StoredRow? row = _records.Query(RowQuery.For("principal-reference").Where("entraObjectId", groupObjectId)).FirstOrDefault();
        return row?.Text("displayLabel") ?? groupObjectId;
    }

    private DateTime? PublishedOn(CompiledSnapshot snapshot)
    {
        StoredRow? version = _records.Query(RowQuery.For("configuration-set-version").Where("configurationSet", snapshot.ConfigurationSetKey))
            .FirstOrDefault(r => r.Number("versionNumber") == snapshot.VersionNumber);
        return Js.ParseMoment(version?.Text("publishedOn")) ?? Js.ParseMoment(version?.Text("engineCheckedOn"));
    }

    // ---- Tools ----

    public ToolResult Invoke(BackendSession session, ToolInvocation invocation)
    {
        if (session == null)
        {
            throw new ArgumentNullException(nameof(session));
        }

        if (invocation == null)
        {
            throw new ArgumentNullException(nameof(invocation));
        }

        if (!ToolHandlers.All.TryGetValue(invocation.Tool.Name, out ToolHandler? handler))
        {
            return ToolResult.Failure(new McpError(McpErrorCodes.NotFound, "The tool " + invocation.Tool.Name + " is not built on this backend.", "name"));
        }

        JsonTreeNode arguments = invocation.Arguments;
        bool validateOnly = Js.Flag(arguments, "validateOnly") == true;
        var request = new ComposeRequest(invocation.Tool.Name, Js.Without(arguments, "clientRequestId", "validateOnly"))
        {
            ReadOnly = handler.ReadOnly,
            DryRun = !handler.ReadOnly && validateOnly,
            IdempotencyKey = handler.ReadOnly || !handler.UsesClientRequestId ? null : Js.Text(arguments, "clientRequestId"),
            CorrelationId = invocation.CorrelationId,
        };

        RuntimeOutcome outcome;
        ToolScope? scope = null;
        try
        {
            outcome = _runtime.Compose(Identity(session.Caller), request, call =>
            {
                scope = new ToolScope(this, call, session, invocation);
                return handler.Run(scope);
            });
        }
        catch (ToolFailureException failure)
        {
            return ToolResult.Failure(failure.Error);
        }

        if (!outcome.Succeeded)
        {
            return ToolResult.Failure(EngineErrorMap.Map(outcome, Contracts, session.ActorKind == "ai-client-for-person"), configurationSetVersion: outcome.ConfigurationVersion);
        }

        JsonTreeNode payload = outcome.Payload ?? Js.EmptyObject;
        JsonTreeNode data = Js.Member(payload, "data") ?? Js.EmptyObject;
        if (outcome.IsReplay && Js.Member(data, "created") != null)
        {
            data = Js.With(data, ("created", JsonTreeNode.False));
        }

        var warnings = outcome.Reasons
            .Where(r => r.Severity == ReasonSeverity.Warning)
            .Select(r => new ToolWarning(r.Message, r.Code, r.Ref))
            .ToList();
        if (outcome.IsReplay)
        {
            warnings.Add(new ToolWarning("This clientRequestId was used before with the same request, so the first result is returned and nothing new was written.", "request.replayed"));
        }

        if (request.DryRun)
        {
            warnings.Add(new ToolWarning("validateOnly was true: every rule ran and nothing was written.", "request.validate-only"));
        }

        var nextSteps = Js.Items(payload, "nextSteps").Select(s => new NextStep(Js.Text(s, "tool")!, Js.Member(s, "inputs") ?? Js.EmptyObject));
        var couldNotRead = (Js.Member(payload, "couldNotRead")?.Members ?? Array.Empty<KeyValuePair<string, JsonTreeNode>>())
            .Select(m => new KeyValuePair<string, int>(m.Key, m.Value.TryGetInt64(out long n) ? (int)n : 0));
        int? version = outcome.ConfigurationVersion.HasValue && outcome.ConfigurationVersion.Value != session.ConfigurationSetVersion ? outcome.ConfigurationVersion : null;
        return ToolResult.Success(data, Js.Text(payload, "summary") ?? invocation.Tool.Name + " answered.", Js.Text(payload, "recordUrl"), warnings, nextSteps, couldNotRead, version);
    }

    // ---- Resources ----

    public ResourceResult ReadResource(BackendSession session, string uri, string correlationId)
    {
        if (session == null)
        {
            throw new ArgumentNullException(nameof(session));
        }

        DateTime now = session.AsOfUtc;
        SnapshotLoad load = _runtime.Cache.Load(_configuration, SnapshotPurpose.Read, Utc(_runtime.Clock.UtcNow));
        if (load.Snapshot == null)
        {
            return ResourceResult.Failure(new McpError(McpErrorCodes.ConfigurationUnavailable, load.Problem?.Message ?? "No valid configuration version is in force."));
        }

        CompiledSnapshot snapshot = load.Snapshot;
        if (uri == "configuration://in-force")
        {
            return ResourceResult.Found(Js.Obj(
                ("uri", JsonTreeNode.FromString(uri)),
                ("configurationSetKey", JsonTreeNode.FromString(snapshot.ConfigurationSetKey)),
                ("version", JsonTreeNode.FromInt64(snapshot.VersionNumber)),
                ("contentHash", JsonTreeNode.FromString(snapshot.ContentHash)),
                ("publishedOn", Js.Utc(PublishedOn(snapshot) ?? now)),
                ("asOf", Js.Utc(now))));
        }

        if (uri.StartsWith("guideline://G", StringComparison.Ordinal))
        {
            string number = uri.Substring("guideline://".Length);
            SnapshotRow? row = snapshot.Snapshot.TryGetEntity("guideline", out SnapshotEntity? guidelines)
                ? guidelines.Rows.Where(r => Js.Text(r.Columns, "number") == number).OrderByDescending(r => r.IsActive).ThenByDescending(r => Js.Text(r.Columns, "version"), StringComparer.Ordinal).FirstOrDefault()
                : null;
            if (row == null)
            {
                return ResourceResult.Failure(new McpError(McpErrorCodes.NotFound, "No guideline " + number + " is in the configuration in force.", "uri"));
            }

            return ResourceResult.Found(Js.Obj(
                ("uri", JsonTreeNode.FromString(uri)),
                ("number", JsonTreeNode.FromString(number)),
                ("version", JsonTreeNode.FromString(Js.Text(row.Columns, "version") ?? "1.0")),
                ("rule", JsonTreeNode.FromString(Js.Text(row.Columns, "rule") ?? Js.Text(row.Columns, "label") ?? number)),
                ("hubUrl", HttpsOrNull(Js.Text(row.Columns, "hubUrl"))),
                ("chapter", Js.Str(Js.Text(row.Columns, "chapter"))),
                ("isActive", JsonTreeNode.FromBoolean(row.IsActive)),
                ("isAdopted", JsonTreeNode.FromBoolean(Js.Flag(row.Columns, "isAdopted") ?? true)),
                ("localUrl", HttpsOrNull(Js.Text(row.Columns, "localUrl"))),
                ("configurationSetVersion", JsonTreeNode.FromInt64(snapshot.VersionNumber)),
                ("asOf", Js.Utc(now))));
        }

        if (uri.StartsWith("playbook://chapter/", StringComparison.Ordinal)
            && int.TryParse(uri.Substring("playbook://chapter/".Length), NumberStyles.None, CultureInfo.InvariantCulture, out int chapterNumber)
            && Options.Playbook.TryGet(chapterNumber, out PlaybookChapter chapter))
        {
            string? hub = snapshot.Snapshot.TryGetRow("setting", "guideline-hub-base-url", out SnapshotRow? hubRow) && hubRow.IsActive ? Js.Text(hubRow.Columns, "value") : null;
            string chapterKey = chapter.Number.ToString(CultureInfo.InvariantCulture);
            var numbers = snapshot.Snapshot.TryGetEntity("guideline", out SnapshotEntity? all)
                ? all.ActiveRows.Where(r => Js.Text(r.Columns, "chapter") is string c && (c == chapterKey || string.Equals(c, chapter.Title, StringComparison.OrdinalIgnoreCase) || c == chapter.Slug))
                    .Select(r => Js.Text(r.Columns, "number")!).Distinct(StringComparer.Ordinal).OrderBy(n => n.Length).ThenBy(n => n, StringComparer.Ordinal).ToList()
                : new List<string>();
            return ResourceResult.Found(Js.Obj(
                ("uri", JsonTreeNode.FromString(uri)),
                ("chapter", JsonTreeNode.FromInt64(chapter.Number)),
                ("title", JsonTreeNode.FromString(chapter.Title)),
                ("url", hub == null ? JsonTreeNode.Null : HttpsOrNull(hub.TrimEnd('/') + "/playbook/" + chapter.Slug)),
                ("guidelineNumbers", Js.Strs(numbers)),
                ("asOf", Js.Utc(now))));
        }

        return ResourceResult.Failure(new McpError(McpErrorCodes.NotFound, "No resource " + uri + " is served here.", "uri"));
    }

    private static JsonTreeNode HttpsOrNull(string? url)
    {
        return url != null && url.StartsWith("https://", StringComparison.Ordinal) && url.Length <= 2048 ? JsonTreeNode.FromString(url) : JsonTreeNode.Null;
    }

    internal static DateTime Utc(DateTime value)
    {
        return value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value.ToUniversalTime(), DateTimeKind.Utc);
    }
}

// One tool's implementation on this backend.
internal sealed class ToolHandler
{
    public ToolHandler(bool readOnly, Func<ToolScope, JsonTreeNode> run, bool usesClientRequestId = true)
    {
        ReadOnly = readOnly;
        Run = run;
        UsesClientRequestId = usesClientRequestId;
    }

    // A read: the snapshot is loaded for reading and the handler writes nothing.
    public bool ReadOnly { get; }

    public Func<ToolScope, JsonTreeNode> Run { get; }

    // False for the record step tools, whose idempotency key is the Deployment Record's own upsert key.
    public bool UsesClientRequestId { get; }
}
