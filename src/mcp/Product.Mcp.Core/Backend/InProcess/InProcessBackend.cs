using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Product.Engine.Configuration.Presets;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;
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

    // The host settings that are safe to show on the Admin page, read fresh for each get_admin_status call (document 24,
    // B24-18). Null: the read says the host gave none.
    public Func<HostFacts>? HostFacts { get; set; }

    // The approval outbox the host keeps, for the platform admin's retry (retry_approval_card; document 24, B24-22). Null:
    // the tool answers that this host keeps no outbox.
    public IApprovalOutboxPort? ApprovalOutbox { get; set; }
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

    // When each configuration version was published, by set, version and content hash: a Configuration Set Version row
    // is append-only (class A), so it is read once per process, not on every call (document 25, B25-23).
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime?> _publishedOn = new(StringComparer.Ordinal);

    // The oldest audit entry's moment, once read (VersionPublishedOn), and the write generation of the last read that
    // found none, so a store with no audit entry is not read again until this process writes.
    private DateTime? _oldestAuditOn;
    private long _oldestAuditMissedAt = -1;

    // How many write calls this process has started (wave 4b review): a write empties the list cache (CachedList) and lets
    // the oldest audit entry be read again.
    private long _writeGeneration;

    // The computed lists of get_data_gaps and list_data_uses, kept for their later pages (CachedList). At most
    // ListCacheSize entries; each lives ListCacheLife.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ListCacheEntry> _listCache = new(StringComparer.Ordinal);
    private const int ListCacheSize = 64;
    private static readonly TimeSpan ListCacheLife = TimeSpan.FromSeconds(60);

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
        var bindings = resolution.Trace.Where(t => t.Entity == "Role Group Binding")
            .Select(t => snapshot.Snapshot.TryGetRow("role-group-binding", t.Key, out SnapshotRow? binding) ? binding : null)
            .Where(b => b != null).Select(b => b!).ToList();

        // The labels of the caller's role groups, in one store read for all of them.
        IReadOnlyList<StoredRow> groupRows = _records.QueryIn("principal-reference", "entraObjectId", bindings.Select(b => Js.Text(b.Columns, "entraGroupObjectId")));
        foreach (SnapshotRow binding in bindings)
        {
            string role = Js.Text(binding.Columns, "roleKey")!;
            string group = Js.Text(binding.Columns, "entraGroupObjectId")!;
            string roleLabel = snapshot.Snapshot.TryGetRow("role", role, out SnapshotRow? roleRow) ? Js.Text(roleRow.Columns, "label") ?? role : role;
            roles.Add(new RoleGrant(role, roleLabel, group, groupRows.FirstOrDefault(r => r.Text("entraObjectId") == group)?.Text("displayLabel") ?? group));
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

    // On the MCP endpoint the caller is an AI client acting for the signed-in person (document 24, section 4.5). Through
    // the product's web app (the person entry) the caller is the person, with no On Behalf Of (document 22, section 2.4).
    // On either entry an agent user or agent identity keeps its kind and resolves to an actor that holds no role (RA-06).
    // The client app came from the validated azp claim, so it is verified.
    internal static CallerIdentity Identity(BackendCaller caller)
    {
        string clientApp = caller.ClientAppLabel ?? caller.ClientAppId;
        CallerEntry entry = caller.Entry == CallerEntry.Person ? CallerEntry.Person : CallerEntry.AiClient;
        return caller.PrincipalKind == PrincipalKind.User && entry == CallerEntry.AiClient
            ? CallerIdentity.AiClient(caller.ObjectId, caller.GroupObjectIds, clientApp, clientAppVerified: true)
            : new CallerIdentity(caller.ObjectId, caller.PrincipalKind, entry, caller.GroupObjectIds, clientApp, true);
    }

    private DateTime? PublishedOn(CompiledSnapshot snapshot)
    {
        string key = snapshot.ConfigurationSetKey + "|" + snapshot.VersionNumber.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + snapshot.ContentHash;
        if (_publishedOn.TryGetValue(key, out DateTime? known))
        {
            return known;
        }

        StoredRow? version = _records.Query(RowQuery.For("configuration-set-version").Where("configurationSet", snapshot.ConfigurationSetKey))
            .FirstOrDefault(r => r.Number("versionNumber") == snapshot.VersionNumber);
        DateTime? published = VersionPublishedOn(version);
        if (version != null && published.HasValue)
        {
            _publishedOn[key] = published;
        }

        return published;
    }

    // When a configuration version came into force (wave 4b verify round; document 21, section 3.6): its Published On, else its
    // Engine Checked On, else the moment of the oldest audit entry (a version 1 seeded before the seed wrote Published On:
    // nothing the product recorded comes before it). Null only for a store with neither, and the caller then answers now.
    // So "in force since" and the review due date stay the same across calls and restarts.
    internal DateTime? VersionPublishedOn(StoredRow? version)
    {
        return Js.ParseMoment(version?.Text("publishedOn")) ?? Js.ParseMoment(version?.Text("engineCheckedOn")) ?? OldestAuditOn();
    }

    // The oldest audit entry's moment, read once per process when found (entries are append-only, and a later one is
    // never older).
    private DateTime? OldestAuditOn()
    {
        if (_oldestAuditOn.HasValue)
        {
            return _oldestAuditOn;
        }

        long generation = System.Threading.Interlocked.Read(ref _writeGeneration);
        if (System.Threading.Interlocked.Read(ref _oldestAuditMissedAt) == generation)
        {
            return null;
        }

        DateTime? oldest = null;
        foreach (StoredRow entry in _records.Query(RowQuery.For("audit-entry")))
        {
            DateTime? on = Js.ParseMoment(entry.Text("occurredOn"));
            if (on.HasValue && (!oldest.HasValue || on.Value < oldest.Value))
            {
                oldest = on;
            }
        }

        if (oldest.HasValue)
        {
            _oldestAuditOn = oldest;
        }
        else
        {
            System.Threading.Interlocked.Exchange(ref _oldestAuditMissedAt, generation);
        }

        return oldest;
    }

    // A computed list for the pages of one query (wave 4b review; document 25, B25-23): get_data_gaps and list_data_uses work
    // out the whole list (every finding, every use with its OP-16 state) and then page it, which took 3.7 s and 2.4 s a
    // page at 2,000 applications. The first page (no skip token) always computes and keeps the list; a later page of the
    // same query (key: tool, caller, roles, configuration content hash and the input without top and skipToken) reads the
    // kept list when it is less than 60 seconds old and no write call started in this process since. So the pages of one
    // list are one consistent reading, at most a minute old, and a write here is seen at once.
    internal T CachedList<T>(string key, bool laterPage, DateTime now, Func<T> compute)
        where T : class
    {
        long generation = System.Threading.Interlocked.Read(ref _writeGeneration);
        if (laterPage && _listCache.TryGetValue(key, out ListCacheEntry? kept) && kept.Generation == generation && now >= kept.At && now - kept.At <= ListCacheLife && kept.Value is T value)
        {
            return value;
        }

        T computed = compute();
        if (_listCache.Count >= ListCacheSize)
        {
            foreach (string stale in _listCache.Where(p => p.Value.Generation != generation || now - p.Value.At > ListCacheLife || now < p.Value.At).Select(p => p.Key).ToList())
            {
                _listCache.TryRemove(stale, out _);
            }

            if (_listCache.Count >= ListCacheSize)
            {
                _listCache.Clear();
            }
        }

        _listCache[key] = new ListCacheEntry(computed, now, generation);
        return computed;
    }

    private sealed class ListCacheEntry
    {
        public ListCacheEntry(object value, DateTime at, long generation)
        {
            Value = value;
            At = at;
            Generation = generation;
        }

        public object Value { get; }

        public DateTime At { get; }

        public long Generation { get; }
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

        // A tool whose engine work runs in calls of its own before its answer is read (run_due_state_refresh: each item of
        // the refresh commits in its own transaction; move_data_references: an AI client's preview is the person's own dry
        // run). A refusal there is the tool's answer. A first step that has nothing to do for this call answers null.
        RuntimeOutcome? prepared = null;
        if (handler.Before != null)
        {
            try
            {
                prepared = handler.Before(this, session, invocation);
            }
            catch (ToolFailureException failure)
            {
                return ToolResult.Failure(failure.Error);
            }

            if (prepared != null && !prepared.Succeeded)
            {
                Reason? refusal = prepared.Reasons.FirstOrDefault(r => r.Severity == ReasonSeverity.Error);
                IEnumerable<string> refusalRules = refusal != null && ReasonCatalog.TryGet(refusal.Code, out ReasonCodeInfo? refusalInfo) ? refusalInfo.RuleIds : Enumerable.Empty<string>();
                return ToolResult.Failure(EngineErrorMap.Map(prepared, Contracts, session.ActorKind == "ai-client-for-person"), configurationSetVersion: prepared.ConfigurationVersion, refusalReasonCode: refusal?.Code, refusalRuleIds: refusalRules);
            }
        }

        // A write call empties the list cache, before it runs (a page read while it runs computes afresh) and after.
        if (!handler.ReadOnly)
        {
            System.Threading.Interlocked.Increment(ref _writeGeneration);
        }

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
                scope = new ToolScope(this, call, session, invocation, prepared);
                return handler.Run(scope);
            });
        }
        catch (ToolFailureException failure)
        {
            return ToolResult.Failure(failure.Error);
        }
        finally
        {
            if (!handler.ReadOnly)
            {
                System.Threading.Interlocked.Increment(ref _writeGeneration);
            }
        }

        if (!outcome.Succeeded)
        {
            Reason? first = outcome.Reasons.FirstOrDefault(r => r.Severity == ReasonSeverity.Error);
            IEnumerable<string> ruleIds = first != null && ReasonCatalog.TryGet(first.Code, out ReasonCodeInfo? info) ? info.RuleIds : Enumerable.Empty<string>();
            return ToolResult.Failure(EngineErrorMap.Map(outcome, Contracts, session.ActorKind == "ai-client-for-person"), configurationSetVersion: outcome.ConfigurationVersion, refusalReasonCode: first?.Code, refusalRuleIds: ruleIds);
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

        if (request.DryRun || (prepared != null && validateOnly))
        {
            warnings.Add(new ToolWarning("validateOnly was true: every rule ran and nothing was written.", "request.validate-only"));
        }

        var nextSteps = Js.Items(payload, "nextSteps").Select(s => new NextStep(Js.Text(s, "tool")!, Js.Member(s, "inputs") ?? Js.EmptyObject));
        var couldNotRead = (Js.Member(payload, "couldNotRead")?.Members ?? Array.Empty<KeyValuePair<string, JsonTreeNode>>())
            .Select(m => new KeyValuePair<string, int>(m.Key, m.Value.TryGetInt64(out long n) ? (int)n : 0));
        int? version = outcome.ConfigurationVersion.HasValue && outcome.ConfigurationVersion.Value != session.ConfigurationSetVersion ? outcome.ConfigurationVersion : null;
        return ToolResult.Success(data, Js.Text(payload, "summary") ?? invocation.Tool.Name + " answered.", Js.Text(payload, "recordUrl"), warnings, nextSteps, couldNotRead, version);
    }

    // ---- Refused calls ----

    // One Audit Entry, operation call-refused, in a transaction of its own, written by the engine as the caller's nested
    // write (document 26, section 9.4; document 25, B25-13). It holds the tool, the record the call named, the fixed error
    // code, the rule key, the engine's reason code and the rules behind it, never the arguments or a message.
    public void RecordRefusal(BackendSession session, RefusedCall refused)
    {
        if (session == null || refused == null)
        {
            return;
        }

        JsonTreeNode details = Js.Obj(
            ("tool", JsonTreeNode.FromString(refused.ToolName)),
            ("knownTool", JsonTreeNode.FromBoolean(refused.KnownTool)),
            ("code", JsonTreeNode.FromString(refused.Error.Code)),
            ("ruleKey", Js.OptStr(refused.Error.RuleKey)),
            ("field", Js.OptStr(refused.Error.Field)),
            ("reasonCode", Js.OptStr(refused.ReasonCode)),
            ("ruleIds", Js.Strs(refused.RuleIds)),
            // The engine's own words for a rule refusal, which name the rule, the domain and where the rule is set (G-25),
            // so the activity and audit pages say what the caller was told. Engine rule messages are built from the
            // configuration and the stored rows, never from the call's arguments; no other refusal keeps a message.
            ("message", refused.Error.Code == McpErrorCodes.RuleViolation && refused.ReasonCode != null ? JsonTreeNode.FromString(refused.Error.Message) : JsonTreeNode.Null),
            ("guideline", Js.OptStr(refused.Error.Guideline ?? GuidelineRefs.ForRefusal(refused.ReasonCode, refused.Error.Code, refused.KnownTool))),
            ("entry", JsonTreeNode.FromString(session.Caller.Entry == CallerEntry.Person ? "person" : "ai-client")));
        try
        {
            _runtime.RecordRefusedCall(Identity(session.Caller), refused.ToolName, refused.RowId, details, refused.CorrelationId);
        }
        catch (Exception e) when (!(e is OutOfMemoryException))
        {
            // The refusal already answered the caller. A store that cannot take the record loses this one event; the
            // server's telemetry still has the refused call.
        }
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
                ("publishedOn", Js.Utc(PublishedOn(snapshot) ?? VersionPublishedOn(null) ?? now)),
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
    public ToolHandler(bool readOnly, Func<ToolScope, JsonTreeNode> run, bool usesClientRequestId = true, Func<InProcessBackend, BackendSession, ToolInvocation, RuntimeOutcome?>? before = null)
    {
        ReadOnly = readOnly;
        Run = run;
        UsesClientRequestId = usesClientRequestId;
        Before = before;
    }

    // The tool's own engine work, run before its composed call (ToolScope.Prepared), or null for a composed tool. It
    // answers null when it has nothing to do for a call.
    public Func<InProcessBackend, BackendSession, ToolInvocation, RuntimeOutcome?>? Before { get; }

    // A read: the snapshot is loaded for reading and the handler writes nothing.
    public bool ReadOnly { get; }

    public Func<ToolScope, JsonTreeNode> Run { get; }

    // False for the record step tools, whose idempotency key is the Deployment Record's own upsert key.
    public bool UsesClientRequestId { get; }
}
