using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Runtime.Ports;

namespace Product.Azure.Stores;

// The provider gateway under RowStore: one SQLite file or one Azure SQL database. It reads and writes stored rows and
// holds no rule: the overlay transaction, the merge of columns, the unique key messages and the seeding logic live once,
// in RowStore, so both providers behave the same. Synchronous, like the engine's ports.
public interface IRowDatabase : IDisposable
{
    // One committed row by ID, or null.
    StoredRowData? Get(EntityInfo entity, string id);

    // Every committed row with this key path, by ID (ordinal).
    IReadOnlyList<StoredRowData> GetByKey(EntityInfo entity, string key);

    // The committed rows of an entity whose string columns equal the values given, by ID (ordinal). The provider may
    // return more rows than match (a case-insensitive comparison, for example) but never fewer: RowStore always filters
    // the result with RowQuery.Matches. No condition returns every row of the entity.
    IReadOnlyList<StoredRowData> Query(EntityInfo entity, IReadOnlyList<KeyValuePair<string, string>> textConditions);

    // The first Configuration Set row (by ID) with Is In Force true and a key path, or null: the version probe.
    StoredRowData? ProbeInForce();

    // The Configuration Set Version rows of a set with this version number, by ID. May return more; RowStore filters.
    IReadOnlyList<StoredRowData> GetVersionRows(string configurationSetKey, int versionNumber);

    // Applies the changes in one database transaction. An update or a delete whose row no longer has the expected
    // version, and a unique key, store key or primary key taken by another row, throw StoreConflictException; nothing is
    // applied then. engineOperation is one of RowStoreOperations. The outbox writes (the commit hook, ApprovalOutboxHook)
    // go into the same transaction, before the row changes, so they commit or roll back with them. After the row changes
    // come the store keys (RowChange.StoreKeys): first every removal of the commit, then every addition, so a value that
    // moves between rows in one transaction never collides with itself.
    void Apply(IReadOnlyList<RowChange> changes, StoreCallContext? context, string engineOperation, IReadOnlyList<ApprovalOutboxWrite>? outbox = null);

    // ---- The approval outbox (ApprovalOutbox.cs): a host table outside the entity catalog ----

    // One outbox row, or null.
    ApprovalOutboxItem? OutboxGet(string approvalRecordId);

    // Pending rows and claimed rows whose lease ended at or before nowUtc, by Enqueued On then Approval Record ID.
    IReadOnlyList<ApprovalOutboxItem> OutboxClaimable(DateTime nowUtc, int limit);

    // Every row, by Enqueued On then Approval Record ID.
    IReadOnlyList<ApprovalOutboxItem> OutboxAll(int limit);

    // Replaces the row's changeable columns when its stored version is next.RowVersion - 1 and the stored row is not
    // settled (answered, expired or cancelled). False otherwise.
    bool OutboxSet(ApprovalOutboxItem next);

    // ICounterStore.Increment, in one atomic statement.
    long Increment(string counterKey, DateTime windowStartUtc, TimeSpan windowLength);

    // Creates the schema (SQLite) or checks that the migrations it needs are applied (Azure SQL).
    void EnsureSchema(EntityCatalog catalog);

    // Never throws.
    bool CanConnect();
}

// The engine operation a connection serves, written to SESSION_CONTEXT as EngineOperation by the Azure SQL store.
public static class RowStoreOperations
{
    // Committed reads.
    public const string Read = "read";

    // An engine transaction that writes no Configuration Set Version.
    public const string Write = "write";

    // An engine transaction that inserts a Configuration Set Version: the bootstrap of version 1 or an activation. Only
    // these may write the live configuration tables.
    public const string Activation = "activation";

    // IStoreSeeder: local runs, demos and tests, as a deployment identity. Never an engine write.
    public const string Seed = "seed";
}

// A stored row and its optimistic version.
public sealed class StoredRowData
{
    public StoredRowData(StoredRow row, long version)
    {
        Row = row ?? throw new ArgumentNullException(nameof(row));
        Version = version;
    }

    public StoredRow Row { get; }

    // Starts at 1 on insert and grows by 1 on every update.
    public long Version { get; }
}

public enum RowChangeKind
{
    Insert,
    Update,
    Delete,
}

// One row change of a commit. A row created and changed in the same transaction is one Insert.
public sealed class RowChange
{
    private StoreKeyChanges? _storeKeys;

    private RowChange(RowChangeKind kind, EntityInfo entity, string id, string? key, JsonTreeNode? columns, long expectedVersion, StoredRow? before = null)
    {
        Kind = kind;
        Entity = entity;
        Id = id;
        Key = key;
        Columns = columns;
        ExpectedVersion = expectedVersion;
        Before = before;
    }

    public RowChangeKind Kind { get; }

    public EntityInfo Entity { get; }

    public string Id { get; }

    public string? Key { get; }

    // The whole row after the change (Insert, Update), null for Delete.
    public JsonTreeNode? Columns { get; }

    // The version the transaction read (Update, Delete); 0 for Insert.
    public long ExpectedVersion { get; }

    // The committed row the transaction read at ExpectedVersion (Update), when the caller has it; null otherwise. It
    // lets the store change only the store keys whose value changed.
    public StoredRow? Before { get; }

    // The store keys this change removes and adds (StoreKeys.cs).
    public StoreKeyChanges StoreKeys => _storeKeys ??= StoreKeyChanges.For(this);

    public static RowChange Insert(EntityInfo entity, string id, string? key, JsonTreeNode columns)
    {
        return new RowChange(RowChangeKind.Insert, entity, id, key, columns ?? throw new ArgumentNullException(nameof(columns)), 0);
    }

    public static RowChange Update(EntityInfo entity, string id, string? key, JsonTreeNode columns, long expectedVersion, StoredRow? before = null)
    {
        return new RowChange(RowChangeKind.Update, entity, id, key, columns ?? throw new ArgumentNullException(nameof(columns)), expectedVersion, before);
    }

    public static RowChange Delete(EntityInfo entity, string id, long expectedVersion)
    {
        return new RowChange(RowChangeKind.Delete, entity, id, null, null, expectedVersion);
    }

    // The JSON text the database stores: canonical, array order kept, so it parses back to an equal tree.
    public string ColumnsText => Columns == null ? string.Empty : RowJson.Write(Columns);

    // The value of the entity's single-column or multi-column unique key, joined with U+001F, or null when any part is
    // empty (the unique index ignores such rows, as the in-memory store does).
    public string? UniqueKey => Columns == null ? null : RowJson.UniqueValue(Entity, Columns, "\u001f");

    public override string ToString()
    {
        return Kind + " " + Entity.Key + " " + Id;
    }
}

// JSON helpers shared by the stores.
public static class RowJson
{
    // The canonical text of a row's columns. Member order becomes canonical; array order is kept.
    public static string Write(JsonTreeNode columns)
    {
        return CanonicalJson.Write(columns, CanonicalProfile.Request);
    }

    public static JsonTreeNode Parse(string columnsText)
    {
        return CanonicalJson.Parse(columnsText);
    }

    // An object from members, leaving out members whose value is null (not JSON null).
    public static JsonTreeNode Object(params (string Name, JsonTreeNode? Value)[] members)
    {
        return JsonTreeNode.CreateObject(members.Where(m => m.Value != null).Select(m => new KeyValuePair<string, JsonTreeNode>(m.Name, m.Value!)));
    }

    // The members of b replace those of a with the same name.
    public static JsonTreeNode Merge(JsonTreeNode a, JsonTreeNode b)
    {
        var members = new SortedDictionary<string, JsonTreeNode>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, JsonTreeNode> member in a.Members)
        {
            members[member.Key] = member.Value;
        }

        foreach (KeyValuePair<string, JsonTreeNode> member in b.Members)
        {
            members[member.Key] = member.Value;
        }

        return JsonTreeNode.CreateObject(members);
    }

    // IStoreTransaction.Update: a column named is replaced, a JSON null clears it, others are kept.
    public static JsonTreeNode Patch(JsonTreeNode row, JsonTreeNode changes)
    {
        var merged = new SortedDictionary<string, JsonTreeNode>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, JsonTreeNode> member in row.Members)
        {
            merged[member.Key] = member.Value;
        }

        foreach (KeyValuePair<string, JsonTreeNode> member in changes.Members)
        {
            if (member.Value.Kind == JsonTreeKind.Null)
            {
                merged.Remove(member.Key);
            }
            else
            {
                merged[member.Key] = member.Value;
            }
        }

        return JsonTreeNode.CreateObject(merged);
    }

    // The value of the entity's unique key in these columns, its parts joined by the separator, or null.
    public static string? UniqueValue(EntityInfo entity, JsonTreeNode columns, string separator)
    {
        var parts = new List<string>();
        foreach (PromotedColumn column in entity.UniqueColumns)
        {
            object? value = column.ValueOf(columns);
            if (value == null)
            {
                return null;
            }

            parts.Add(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
        }

        return parts.Count == 0 ? null : string.Join(separator, parts);
    }
}
