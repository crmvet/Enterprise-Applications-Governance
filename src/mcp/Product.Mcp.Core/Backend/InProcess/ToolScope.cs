using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Product.Engine.Configuration.Presets;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Operations;
using Product.Engine.Core.Reasons;
using Product.Engine.Core.Snapshots;
using Product.Engine.Runtime;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Envelope;
using Product.Mcp.Core.Json;

namespace Product.Mcp.Core.Backend.InProcess;

// A failure a tool raises outside the engine's own outcomes: an upstream that did not answer, a relation the caller
// lacks, an AI write state the Tool Policy does not allow. It ends the call; nothing commits.
public sealed class ToolFailureException : Exception
{
    public ToolFailureException(McpError error)
        : base(error?.Message)
    {
        Error = error ?? throw new ArgumentNullException(nameof(error));
    }

    public McpError Error { get; }
}

// What one tool handler sees: the composed engine call (snapshot, rows, writes), the session, the arguments and the
// helpers every tool shares (labels, status values, principals, paging, the write result). One per call.
public sealed class ToolScope
{
    private readonly InProcessBackend _backend;
    private readonly List<KeyValuePair<string, int>> _couldNotRead = new();

    internal ToolScope(InProcessBackend backend, ComposedCall call, BackendSession session, ToolInvocation invocation, RuntimeOutcome? prepared = null)
    {
        _backend = backend;
        Call = call;
        Session = session;
        Invocation = invocation;
        Args = invocation.Arguments;
        Prepared = prepared;
    }

    // What a tool's own engine call returned before its read call, for a tool that runs one (ToolHandler.Before):
    // run_due_state_refresh, whose items each commit in their own transaction. Null for every other tool.
    public RuntimeOutcome? Prepared { get; }

    public ComposedCall Call { get; }

    public BackendSession Session { get; }

    public ToolInvocation Invocation { get; }

    public ToolContract Tool => Invocation.Tool;

    public JsonTreeNode Args { get; }

    public IRecordReader Rows => Call.Rows;

    public ConfigurationSnapshot Snapshot => Call.Snapshot.Snapshot;

    public DateTime Now => Call.AsOfUtc;

    public string Today => Now.ToString("yyyy'-'MM'-'dd", CultureInfo.InvariantCulture);

    // The person's own calendar day, for the date-only values a write stamps when the caller gives none: the day a review
    // is done, a document checked, a use or a mapping starts or ends, a component or a place retires, a review falls due
    // (wave 4, B24-21). The product's web app sends the person's local day with every request; it counts when it is the
    // UTC day or the day before or after, the range every time zone falls in. Any other value, or none (an AI client, a
    // script), gives the UTC day, as before. Reads keep the UTC day.
    public string PersonToday
    {
        get
        {
            string? local = Invocation.CallerLocalDay;
            if (local != null && local.Length == 10
                && DateTime.TryParseExact(local, "yyyy'-'MM'-'dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime day)
                && Math.Abs((day.Date - Now.Date).TotalDays) <= 1)
            {
                return local;
            }

            return Today;
        }
    }

    // The signed-in person (the AI client acts for them).
    public string PersonId => Session.Caller.ObjectId;

    public bool IsAiCaller => Session.ActorKind == "ai-client-for-person";

    public bool ValidateOnly => Js.Flag(Args, "validateOnly") == true;

    public PresetCatalog Catalog => _backend.Catalog;

    public InProcessBackendOptions Options => _backend.Options;

    // When the configuration version in force came into force (wave 4b verify round): the session's Published On, then the oldest
    // audit entry's moment, and only then now (InProcessBackend.VersionPublishedOn). Every page reads this one answer.
    public DateTime InForcePublishedOn => Session.PublishedOnUtc ?? _backend.VersionPublishedOn(null) ?? Now;

    // CanReadIntake's answer per intake request in this call (wave 4b review).
    internal Dictionary<string, bool> IntakeReadable { get; } = new(StringComparer.Ordinal);

    // A list this read works out whole and then pages, kept for the later pages of the same query (wave 4b review;
    // InProcessBackend.CachedList): the first page computes it, a later page reads it when it is under a minute old and
    // no write call started since.
    public T PagedList<T>(Func<T> compute)
        where T : class
    {
        string key = Tool.Name + "|" + Session.Caller.Entry + "|" + Session.Caller.ObjectId + "|" + string.Join(",", Session.RoleKeys) + "|" + Call.Snapshot.ContentHash + "|" + QueryHash();
        return _backend.CachedList(key, Js.Text(Args, "skipToken") != null, Now, compute);
    }

    // The same for one Configuration Set Version row: Published On, Engine Checked On, the oldest audit entry, now.
    public DateTime PublishedOnOf(StoredRow? version)
    {
        return _backend.VersionPublishedOn(version) ?? Now;
    }

    public ContractSet Contracts => _backend.Contracts;

    internal IReadOnlyList<KeyValuePair<string, int>> CouldNotRead => _couldNotRead;

    // ---- Arguments ----

    public string? Text(string name)
    {
        return Js.Text(Args, name);
    }

    public string RequireText(string name)
    {
        return Js.Text(Args, name) ?? throw Invalid(name, name + " is required.");
    }

    public bool? Flag(string name)
    {
        return Js.Flag(Args, name);
    }

    public IReadOnlyList<string> Strings(string name)
    {
        return Js.Strings(Args, name);
    }

    public IReadOnlyList<JsonTreeNode> Items(string name)
    {
        return Js.Items(Args, name);
    }

    // ---- Failures ----

    public Exception Invalid(string field, string message)
    {
        return Call.Fail(OperationOutcomes.InvalidInput, ReasonCodes.InputMalformed, message, null, field);
    }

    public Exception NotFound(string entityName, string id, string field)
    {
        return Call.Fail(RuntimeOutcome.NotFound, ReasonCodes.RefNotFound, entityName + " " + id + " does not exist, or you cannot see it.", entityName, field);
    }

    public Exception Refused(string code, string message, string? reference = null, string? recordUrl = null)
    {
        return new ToolFailureException(new McpError(code, message, ruleKey: reference, recordUrl: recordUrl));
    }

    public Exception Upstream(string what, string? problem)
    {
        return new ToolFailureException(new McpError(McpErrorCodes.UpstreamUnavailable, what + " did not answer" + (problem == null ? "." : ": " + problem)));
    }

    // Refuses an AI client when the Tool Policy's Max AI State does not reach the state this call would leave.
    public void RequireAiState(string state)
    {
        if (IsAiCaller && !Session.ToolPolicy.AiMayReach(Tool, state))
        {
            throw Refused(McpErrorCodes.AiCannotDoThis, "The Tool Policy lets an AI client go no further than " + Session.ToolPolicy.EffectiveMaxAiState(Tool) + " through " + Tool.Name + ", and this call would leave a record " + state + ".");
        }
    }

    public void HideFromCaller(string kind, int count)
    {
        if (count > 0)
        {
            _couldNotRead.Add(new KeyValuePair<string, int>(kind, count));
        }
    }

    // ---- Rows ----

    public StoredRow RequireRow(string entity, string? id, string field)
    {
        if (id == null)
        {
            throw Invalid(field, field + " is required.");
        }

        return Rows.Get(entity, id) ?? throw NotFound(EntityName(entity), id, field);
    }

    public IReadOnlyList<StoredRow> Query(string entity, params (string Column, string? Value)[] conditions)
    {
        RowQuery query = RowQuery.For(entity);
        foreach ((string column, string? value) in conditions)
        {
            query = query.Where(column, value);
        }

        return Rows.Query(query);
    }

    public ComposedWrite Create(string entity, JsonTreeNode after)
    {
        return Call.Write(new WriteCommand(entity, WriteOperation.Create, null, after));
    }

    public ComposedWrite Update(string entity, string rowId, JsonTreeNode after)
    {
        return Call.Write(new WriteCommand(entity, WriteOperation.Update, rowId, after));
    }

    // The current owner assignments of an application: started, not ended (O06).
    public IReadOnlyList<StoredRow> CurrentOwners(string applicationId, DateTime? asOf = null)
    {
        DateTime day = (asOf ?? Now).Date;
        return Query("owner-assignment", ("businessApplication", applicationId))
            .Where(r => (Js.ParseMoment(r.Text("startOn")) ?? DateTime.MinValue).Date <= day)
            .Where(r => r.Text("endOn") == null || (Js.ParseMoment(r.Text("endOn")) ?? DateTime.MaxValue).Date > day)
            .OrderBy(r => r.Text("ownerRole"), StringComparer.Ordinal)
            .ThenBy(r => r.Flag("isDeputy"))
            .ThenBy(r => r.Id, StringComparer.Ordinal)
            .ToList();
    }

    // True when the person holds a current owner role on the application.
    public bool OwnsApplication(string applicationId, string? ownerRoleKey = null)
    {
        return CurrentOwners(applicationId).Any(o => string.Equals(o.Text("principal"), PersonId, StringComparison.OrdinalIgnoreCase)
            && (ownerRoleKey == null || o.Text("ownerRole") == ownerRoleKey));
    }

    public bool HoldsRole(string roleKey)
    {
        return Session.RoleKeys.Contains(roleKey, StringComparer.Ordinal);
    }

    // ---- Configuration ----

    public SnapshotRow? ConfigRow(string entity, string? key)
    {
        return key != null && Snapshot.TryGetRow(entity, key, out SnapshotRow? row) ? row : null;
    }

    public IEnumerable<SnapshotRow> ActiveRows(string entity)
    {
        return Snapshot.TryGetEntity(entity, out SnapshotEntity? rows) ? rows.ActiveRows : Enumerable.Empty<SnapshotRow>();
    }

    public IEnumerable<SnapshotRow> AllRows(string entity)
    {
        return Snapshot.TryGetEntity(entity, out SnapshotEntity? rows) ? rows.Rows : Enumerable.Empty<SnapshotRow>();
    }

    // Refuses a key that is not an active row of the entity.
    public SnapshotRow RequireKey(string entity, string? key, string field)
    {
        SnapshotRow? row = ConfigRow(entity, key);
        if (key == null || row == null || !row.IsActive)
        {
            throw Invalid(field, field + " \"" + key + "\" is not an active " + EntityName(entity) + " key.");
        }

        return row;
    }

    public string Label(string entity, string? key)
    {
        SnapshotRow? row = ConfigRow(entity, key);
        return (row == null ? null : Js.Text(row.Columns, "label")) ?? key ?? string.Empty;
    }

    // { key, label }, or JSON null for a null key.
    public JsonTreeNode LabelledKey(string entity, string? key)
    {
        return key == null ? JsonTreeNode.Null : Js.Obj(("key", JsonTreeNode.FromString(key)), ("label", JsonTreeNode.FromString(Label(entity, key))));
    }

    // A stored configuration version of the set in force, through the snapshot cache (checked against its hash).
    public Product.Engine.Configuration.Snapshots.CompiledSnapshot LoadVersion(int versionNumber, string field)
    {
        if (versionNumber == Call.Snapshot.VersionNumber)
        {
            return Call.Snapshot;
        }

        Product.Engine.Runtime.Snapshots.SnapshotLoad load = _backend.Runtime.Cache.LoadVersion(_backend.Configuration, Call.Snapshot.ConfigurationSetKey, versionNumber);
        return load.Snapshot ?? throw Call.Fail(RuntimeOutcome.NotFound, ReasonCodes.RefNotFound, "Configuration Set Version " + versionNumber + " is not stored.", "Configuration Set Version", field);
    }

    public string? Setting(string key)
    {
        SnapshotRow? row = ConfigRow("setting", key);
        return row != null && row.IsActive ? Js.Text(row.Columns, "value") : null;
    }

    public long? SettingNumber(string key)
    {
        SnapshotRow? row = ConfigRow("setting", key);
        return row != null && row.IsActive ? Js.Number(row.Columns, "value") : null;
    }

    public JsonTreeNode? SettingJson(string key)
    {
        SnapshotRow? row = ConfigRow("setting", key);
        return row != null && row.IsActive ? Js.Member(row.Columns, "value") : null;
    }

    // The status the engine picks for a meaning: the default for it, or the only active one (as the runtime does).
    public string StatusFor(string entity, string meaning)
    {
        var rows = ActiveRows("status").Where(r => r.EngineMeaning == meaning && Js.Text(r.Columns, "appliesToEntity") == entity).ToList();
        SnapshotRow? pick = rows.Count == 1 ? rows[0] : rows.FirstOrDefault(r => Js.Flag(r.Columns, "isDefaultForMeaning") == true);
        return pick?.KeyPath ?? throw Call.Fail(OperationOutcomes.Unavailable, ReasonCodes.MeaningMissing, EntityName(entity) + " has no single active status for the meaning " + meaning + ".", "Status", null);
    }

    // { label, meaning } of a row's status, or JSON null when the row has none.
    public JsonTreeNode StatusValue(StoredRow row, string statusColumn = "status", string meaningColumn = "statusMeaning")
    {
        string? key = row.Text(statusColumn);
        SnapshotRow? status = ConfigRow("status", key);
        string? meaning = row.Text(meaningColumn) ?? status?.EngineMeaning;
        if (meaning == null)
        {
            return JsonTreeNode.Null;
        }

        return StatusValue(meaning, status == null ? null : Js.Text(status.Columns, "label"));
    }

    public static JsonTreeNode StatusValue(string meaning, string? label)
    {
        return Js.Obj(("label", JsonTreeNode.FromString(label ?? Labelize(meaning))), ("meaning", JsonTreeNode.FromString(meaning)));
    }

    // ---- Principals ----

    public StoredRow? Principal(string? objectId)
    {
        return objectId == null ? null : Query("principal-reference", ("entraObjectId", objectId)).FirstOrDefault()
            ?? Rows.Query(RowQuery.For("principal-reference")).FirstOrDefault(r => string.Equals(r.Text("entraObjectId"), objectId, StringComparison.OrdinalIgnoreCase));
    }

    // { objectId, displayLabel, principalKind }, or JSON null.
    public JsonTreeNode PrincipalRef(string? objectId)
    {
        if (objectId == null)
        {
            return JsonTreeNode.Null;
        }

        StoredRow? row = Principal(objectId);
        string label = row == null ? "Unknown principal" : row.Flag("isErased") ? EngineKeys.ErasedDisplayLabel : row.Text("displayLabel") ?? "Unknown principal";
        return Js.Obj(
            ("objectId", JsonTreeNode.FromString(objectId)),
            ("displayLabel", JsonTreeNode.FromString(label)),
            ("principalKind", Js.OptStr(row?.Text("principalKind"))));
    }

    public string DisplayLabel(string objectId)
    {
        return Js.Text(PrincipalRef(objectId), "displayLabel") ?? objectId;
    }

    // ---- Write results ----

    // The fields every write adds to data (document 24, section 3.4), read back from the row as the engine stamped it.
    public List<(string, JsonTreeNode?)> WriteResult(string entity, ComposedWrite write, bool hasStatus = true, string statusColumn = "status", string meaningColumn = "statusMeaning")
    {
        StoredRow row = Rows.Get(entity, write.RowId) ?? throw new InvalidOperationException(entity + " " + write.RowId + " was written but cannot be read back.");
        return WriteResult(row, write.Created, hasStatus, statusColumn, meaningColumn);
    }

    public List<(string, JsonTreeNode?)> WriteResult(StoredRow row, bool created, bool hasStatus = true, string statusColumn = "status", string meaningColumn = "statusMeaning")
    {
        bool wouldCreate = created && Call.IsDryRun;
        return new List<(string, JsonTreeNode?)>
        {
            ("recordId", wouldCreate ? JsonTreeNode.Null : JsonTreeNode.FromString(row.Id)),
            ("status", hasStatus ? StatusValue(row, statusColumn, meaningColumn) : JsonTreeNode.Null),
            ("performedBy", PerformedBy(row)),
            ("isAiCreated", JsonTreeNode.FromBoolean(row.Flag("isAiCreated"))),
            ("engineStamp", Js.Obj(
                ("engineVersion", JsonTreeNode.FromString(row.Text("engineVersion") ?? EngineInfo.Version)),
                ("engineCheckedOn", JsonTreeNode.FromString(row.Text("engineCheckedOn") ?? CanonicalJson.FormatUtc(Now))))),
            ("created", JsonTreeNode.FromBoolean(created && !Call.IsDryRun)),
        };
    }

    // { actorKind, principal, onBehalfOf, clientApp } exactly as the engine stamped the row (document 24, section 4.5).
    public JsonTreeNode PerformedBy(StoredRow row)
    {
        string? onBehalfOf = row.Text("onBehalfOf");
        string? clientApp = row.Text("clientApp");
        return Js.Obj(
            ("actorKind", JsonTreeNode.FromString(row.Text("performedByActorKind") ?? Session.ActorKind)),
            ("principal", PrincipalRef(row.Text("performedByPrincipal") ?? PersonId)),
            ("onBehalfOf", PrincipalRef(onBehalfOf)),
            ("clientApp", clientApp != null && IsGuid(Session.Caller.ClientAppId)
                ? Js.Obj(("appId", JsonTreeNode.FromString(Session.Caller.ClientAppId)), ("label", JsonTreeNode.FromString(clientApp)))
                : JsonTreeNode.Null));
    }

    // { actorKind, principal, onBehalfOf, clientApp } for the caller of this call, when no row records the write (a
    // preview that writes nothing): the AI client is named, as the engine would stamp it on a row (wave 3 verification:
    // a move preview named the person with no client).
    public JsonTreeNode CallerPerformedBy()
    {
        string? appId = Session.Caller.ClientAppId;
        return Js.Obj(
            ("actorKind", JsonTreeNode.FromString(Session.ActorKind)),
            ("principal", PrincipalRef(PersonId)),
            ("onBehalfOf", IsAiCaller ? PrincipalRef(PersonId) : JsonTreeNode.Null),
            ("clientApp", IsGuid(appId) ? Js.Obj(("appId", JsonTreeNode.FromString(appId!)), ("label", JsonTreeNode.FromString(Session.Caller.ClientAppLabel ?? appId!))) : JsonTreeNode.Null));
    }

    public static JsonTreeNode Merge(List<(string, JsonTreeNode?)> members, params (string Name, JsonTreeNode? Value)[] more)
    {
        var all = new List<(string, JsonTreeNode?)>(members);
        foreach ((string name, JsonTreeNode? value) in more)
        {
            all.RemoveAll(m => m.Item1 == name);
            all.Add((name, value));
        }

        return Js.Obj(all.ToArray());
    }

    // ---- Answers ----

    // The payload a handler returns: the data and the envelope parts the backend lifts out of it.
    public JsonTreeNode Answer(JsonTreeNode data, string summary, string? recordUrl = null, params NextStep[] nextSteps)
    {
        return Js.Obj(
            ("data", data),
            ("summary", JsonTreeNode.FromString(summary)),
            ("recordUrl", Js.OptStr(recordUrl)),
            ("recordId", Js.OptStr(Js.Text(data, "recordId"))),
            ("nextSteps", Js.Arr(nextSteps.Select(s => Js.Obj(("tool", JsonTreeNode.FromString(s.Tool)), ("inputs", s.Inputs))))),
            ("couldNotRead", Js.Obj(_couldNotRead.GroupBy(c => c.Key).Select(g => new KeyValuePair<string, JsonTreeNode>(g.Key, JsonTreeNode.FromInt64(g.Sum(c => c.Value)))))));
    }

    public static NextStep Next(string tool, params (string Name, JsonTreeNode? Value)[] inputs)
    {
        return new NextStep(tool, Js.Obj(inputs));
    }

    // The request's own base, when the host set one (a local run with no public base URL links to the address it serves,
    // document 24, B24-17), else the backend's configured base.
    public string RecordUrl(string entity, string? id)
    {
        return (Invocation.RecordUrlBase ?? Options.RecordUrlBase).TrimEnd('/') + "/" + entity + (id == null ? string.Empty : "/" + id);
    }

    // ---- Paging (document 24, section 3.3: skipToken encodes the query, not server state) ----

    public List<T> Page<T>(IReadOnlyList<T> items, out string? nextSkipToken)
    {
        long top = Js.Number(Args, "top") ?? 25;
        if (top > Session.PageSizeMax)
        {
            Call.Warn("page.size-capped", "top " + top + " is above the mcp-page-size-max setting, so the page holds " + Session.PageSizeMax + ".", "Setting", "mcp-page-size-max");
            top = Session.PageSizeMax;
        }

        int offset = 0;
        string? token = Js.Text(Args, "skipToken");
        string queryHash = QueryHash();
        if (token != null)
        {
            string[] parts = DecodeToken(token).Split('|');
            if (parts.Length != 3 || parts[0] != "v1" || parts[2] != queryHash || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out offset))
            {
                throw Invalid("skipToken", "skipToken does not belong to this query. Call again without it, or with the same inputs as the call that returned it.");
            }
        }

        List<T> page = items.Skip(offset).Take((int)top).ToList();
        int next = offset + page.Count;
        nextSkipToken = next < items.Count ? EncodeToken("v1|" + next.ToString(CultureInfo.InvariantCulture) + "|" + queryHash) : null;
        return page;
    }

    private string QueryHash()
    {
        JsonTreeNode query = Js.Without(Args, "top", "skipToken");
        using SHA256 sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(Tool.Name + "|" + Js.Write(query)));
        return string.Concat(hash.Take(8).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
    }

    private static string EncodeToken(string text)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string DecodeToken(string token)
    {
        try
        {
            string padded = token.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - (padded.Length % 4)) % 4);
            return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        }
        catch (FormatException)
        {
            return string.Empty;
        }
    }

    // ---- Small helpers ----

    public static string EntityName(string entityKey)
    {
        return string.Join(" ", entityKey.Split('-').Select(p => p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p.Substring(1)));
    }

    public static string Labelize(string meaning)
    {
        return meaning.Length == 0 ? meaning : char.ToUpperInvariant(meaning[0]) + meaning.Substring(1).Replace('-', ' ');
    }

    public static bool IsGuid(string? value)
    {
        return value != null && Guid.TryParseExact(value, "D", out _);
    }

    public static JsonTreeNode DateOrNull(string? text)
    {
        DateTime? moment = Js.ParseMoment(text);
        return moment.HasValue ? Js.Date(moment.Value) : JsonTreeNode.Null;
    }

    public static JsonTreeNode MomentOrNull(string? text)
    {
        DateTime? moment = Js.ParseMoment(text);
        return moment.HasValue ? Js.Utc(moment.Value) : JsonTreeNode.Null;
    }
}
