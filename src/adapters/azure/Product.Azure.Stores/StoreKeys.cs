using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Model;

namespace Product.Azure.Stores;

// Which rows of an entity a store key covers.
public enum StoreKeyScope
{
    // Every row whose key parts all have a value.
    AllRows,

    // Live configuration rows that are not retired (isActive is not false). A retired row keeps its values so old
    // records still resolve, and an active row may take them again, as the engine's own checks of tier ranks (V12) and
    // stage ranks (V14) allow.
    ActiveRows,

    // Rows with no Retired On. The engine's own key of the entity counts only such rows (O03), and its flows register a
    // resource again after the component that held it was retired.
    CurrentRows,

    // Rows with no Ended On: an O26 Data Mapping that ended frees its domain, location and origin for a new mapping, as the
    // model's key says ("among rows with no Ended On").
    OpenRows,

    // Rows in service: no Retired On, and a status meaning other than archived. The model's O25 key counts the locations
    // not retired, and the engine counts a location out of service when either is set (the save's match on platform and
    // External Id, G-18's Url key, every read). A draft location that is withdrawn is archived with no Retired On, so it
    // frees its External Id for a location added again (wave 4b, fix round 2: the add answered HTTP 500 before).
    InServiceRows,
}

// One key the logical model asks the store to enforce (enforcement "store"), held by the database as a row of the store
// key table: the entity, the key's name, the SHA-256 of the key's value and the row ID, with the first three as the
// primary key. The store writes and removes those rows in the same database transaction as the row itself, so the
// database refuses a second row with the same value, in any transaction, at commit. A row whose key parts are not all
// set holds no value for that key, as a database unique index ignores nulls and a Dataverse alternate key ignores empty
// values.
public sealed class StoreKey
{
    // The key path of a live configuration row, which StoredRow.Key carries.
    public const string KeyPathPart = "(key path)";

    internal StoreKey(string entityKey, string name, IEnumerable<string> modelKeys, IEnumerable<string> parts, StoreKeyScope scope)
    {
        EntityKey = entityKey;
        Name = name;
        ModelKeys = new ReadOnlyCollection<string>(modelKeys.ToList());
        Parts = new ReadOnlyCollection<string>(parts.ToList());
        Scope = scope;
        if (Name.Length == 0 || Name.Length > MaxNameLength || Parts.Count == 0)
        {
            throw new InvalidOperationException("Store key \"" + Name + "\" of " + entityKey + " needs a name of 1 to " + MaxNameLength + " characters and at least one part.");
        }
    }

    // The longest key name the store key table holds (varchar(128)).
    public const int MaxNameLength = 128;

    public string EntityKey { get; }

    // The key's columns in logical names, without Configuration Set (the live configuration tables hold the rows of the
    // working set in force only), such as "Link Kind+External Id" or "Key".
    public string Name { get; }

    // The keys of the model it enforces, as "<entity ID> <columns joined by +>", such as "C08 Rank+Configuration Set".
    // A key path covers two keys of a child entity: C07 Key and C07 Value List+Key, since the path holds the list's key.
    public IReadOnlyList<string> ModelKeys { get; }

    // The row JSON members of the value, in order, or KeyPathPart.
    public IReadOnlyList<string> Parts { get; }

    public StoreKeyScope Scope { get; }

    public bool UsesKeyPath => Parts.Count == 1 && string.Equals(Parts[0], KeyPathPart, StringComparison.Ordinal);

    // The key's value in a row, or null when the row holds none: a part is absent, a JSON null or an empty string, or the
    // row is outside the key's scope. The text is the canonical JSON array of the parts. A string that is a GUID is
    // lowercased first: the engine compares Microsoft IDs that are GUIDs without regard to case (document 22, section 7).
    public StoreKeyValue? ValueOf(string? keyPath, JsonTreeNode columns)
    {
        if (columns == null)
        {
            throw new ArgumentNullException(nameof(columns));
        }

        if (columns.Kind != JsonTreeKind.Object || !InScope(columns))
        {
            return null;
        }

        var parts = new List<JsonTreeNode>();
        foreach (string part in Parts)
        {
            JsonTreeNode? value = string.Equals(part, KeyPathPart, StringComparison.Ordinal)
                ? (string.IsNullOrEmpty(keyPath) ? null : JsonTreeNode.FromString(keyPath!))
                : columns.TryGetMember(part, out JsonTreeNode? found) ? found : null;
            if (value == null || value.Kind == JsonTreeKind.Null || (value.Kind == JsonTreeKind.String && value.GetString().Length == 0))
            {
                return null;
            }

            parts.Add(value.Kind == JsonTreeKind.String ? JsonTreeNode.FromString(Normalize(value.GetString())) : value);
        }

        return new StoreKeyValue(this, parts);
    }

    public override string ToString()
    {
        return EntityKey + " " + Name;
    }

    private bool InScope(JsonTreeNode columns)
    {
        switch (Scope)
        {
            case StoreKeyScope.ActiveRows:
                return !(columns.TryGetMember("isActive", out JsonTreeNode? active) && active.Kind == JsonTreeKind.False);
            case StoreKeyScope.CurrentRows:
                return !HasValue(columns, "retiredOn");
            case StoreKeyScope.OpenRows:
                return !HasValue(columns, "endedOn");
            case StoreKeyScope.InServiceRows:
                return !HasValue(columns, "retiredOn")
                    && !(columns.TryGetMember("statusMeaning", out JsonTreeNode? meaning) && meaning.Kind == JsonTreeKind.String && string.Equals(meaning.GetString(), "archived", StringComparison.Ordinal));
            default:
                return true;
        }
    }

    // True when the member holds a value: not absent, not a JSON null and not an empty string.
    private static bool HasValue(JsonTreeNode columns, string member)
    {
        return columns.TryGetMember(member, out JsonTreeNode? value) && value.Kind != JsonTreeKind.Null && !(value.Kind == JsonTreeKind.String && value.GetString().Length == 0);
    }

    private static string Normalize(string text)
    {
        return text.Length == 36 && Guid.TryParseExact(text, "D", out _) ? text.ToLowerInvariant() : text;
    }
}

// A store key's value in one row.
public sealed class StoreKeyValue
{
    internal StoreKeyValue(StoreKey key, IReadOnlyList<JsonTreeNode> parts)
    {
        Key = key;
        Text = CanonicalJson.Write(JsonTreeNode.CreateArray(parts), CanonicalProfile.Request);
        Display = string.Join("/", parts.Select(p => p.Kind == JsonTreeKind.String ? p.GetString() : CanonicalJson.Write(p, CanonicalProfile.Request)));
        Hash = SHA256.HashData(Encoding.UTF8.GetBytes(Text));
    }

    public StoreKey Key { get; }

    // The canonical JSON array of the parts, such as ["tool/other"] or ["requester","00000000-0000-4000-8000-000000000101","entra-app-role-assignment"].
    public string Text { get; }

    // The parts joined with "/", for messages.
    public string Display { get; }

    // SHA-256 of Text in UTF-8: what the store key table holds, so the table holds no name or other text of the row.
    public byte[] Hash { get; }

    public bool SameValue(StoreKeyValue? other)
    {
        return other != null && Hash.SequenceEqual(other.Hash);
    }

    public override string ToString()
    {
        return Key + " " + Text;
    }
}

// The store keys of one row change: the keys whose stored value the change removes, and the values it adds. The
// database applies every removal of a commit before any addition, so a value that moves from one row to another in one
// transaction (two tiers that swap ranks, a component retired and registered again) never collides with itself.
public sealed class StoreKeyChanges
{
    private static readonly StoreKeyChanges NoChanges = new(false, Array.Empty<StoreKey>(), Array.Empty<StoreKeyValue>());

    private StoreKeyChanges(bool removesAll, IReadOnlyList<StoreKey> removed, IReadOnlyList<StoreKeyValue> added)
    {
        RemovesAll = removesAll;
        Removed = removed;
        Added = added;
    }

    // True when every stored key of the row goes: a delete, or an update whose earlier row the store did not read.
    public bool RemovesAll { get; }

    // The keys of an updated row whose value changed (empty when RemovesAll).
    public IReadOnlyList<StoreKey> Removed { get; }

    // The values to add: every value of an inserted row, the changed values of an updated row.
    public IReadOnlyList<StoreKeyValue> Added { get; }

    public bool IsEmpty => !RemovesAll && Removed.Count == 0 && Added.Count == 0;

    internal static StoreKeyChanges For(RowChange change)
    {
        IReadOnlyList<StoreKey> keys = change.Entity.StoreKeys;
        if (keys.Count == 0)
        {
            return NoChanges;
        }

        switch (change.Kind)
        {
            case RowChangeKind.Delete:
                return new StoreKeyChanges(true, Array.Empty<StoreKey>(), Array.Empty<StoreKeyValue>());
            case RowChangeKind.Insert:
                return new StoreKeyChanges(false, Array.Empty<StoreKey>(), Values(keys, change.Key, change.Columns!));
        }

        if (change.Before == null)
        {
            return new StoreKeyChanges(true, Array.Empty<StoreKey>(), Values(keys, change.Key, change.Columns!));
        }

        var removed = new List<StoreKey>();
        var added = new List<StoreKeyValue>();
        foreach (StoreKey key in keys)
        {
            StoreKeyValue? before = key.ValueOf(change.Before.Key, change.Before.Columns);
            StoreKeyValue? after = key.ValueOf(change.Key, change.Columns!);
            if (before == null && after == null || before != null && before.SameValue(after))
            {
                continue;
            }

            if (before != null)
            {
                removed.Add(key);
            }

            if (after != null)
            {
                added.Add(after);
            }
        }

        return removed.Count == 0 && added.Count == 0 ? NoChanges : new StoreKeyChanges(false, removed, added);
    }

    internal static IReadOnlyList<StoreKeyValue> Values(IReadOnlyList<StoreKey> keys, string? keyPath, JsonTreeNode columns)
    {
        var values = new List<StoreKeyValue>();
        foreach (StoreKey key in keys)
        {
            StoreKeyValue? value = key.ValueOf(keyPath, columns);
            if (value != null)
            {
                values.Add(value);
            }
        }

        return values;
    }
}

// Which keys of the logical model the stores hold, and how. Read once from the model compiled into the engine core
// (LogicalModel), so a store key the model adds stops the catalog until it is mapped here or held back on purpose:
// refuse more, never less.
public static class StoreKeyCatalog
{
    // The store keys of the model the stores do not hold in this release, each because a documented engine behavior
    // writes rows the key would refuse. The engine and MCP core changes, or the model changes, that would let the store
    // hold them are listed in the Azure adapter's ENGINE-CHANGES-NEEDED.md.
    public static readonly IReadOnlyDictionary<string, string> HeldBack = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["C02 Content Hash"] = "Content Json holds no version number, so a version that returns to earlier content (a retirement undone) has the hash of an earlier version.",
        ["O13 Artifact Name+Version"] = "The deployment record step looks an Artifact Version up by Business Application, Artifact Name and Version, so a second application that deploys the same artifact name and version gets a row of its own.",
        ["O21 Business Application+Review Type+Due On"] = "A change result opens a post-change review due 14 days later without looking for an open one, so two changes to one application finished on the same day open two reviews due on the same date.",
    });

    // The alternate keys of the configuration entities, by model key: the live row's JSON members (the configuration
    // bundle's field names, which differ from the logical names for references) or the key path. Configuration Set is
    // not a part: the live tables hold the rows of the working set in force only.
    private static readonly Dictionary<string, string[]> ConfigurationAlternates = new(StringComparer.Ordinal)
    {
        ["C07 Value List+Key+Configuration Set"] = new[] { StoreKey.KeyPathPart },
        ["C08 Rank+Configuration Set"] = new[] { "rank" },
        ["C11 Source Entity+Source Key+Vocabulary+Configuration Set"] = new[] { "sourceEntity", "sourceKey", "vocabularyKey" },
        ["C12 Stage Rank+Configuration Set"] = new[] { "stageRank" },
        ["C25 Review Type+Tier+Configuration Set"] = new[] { "reviewTypeKey", "tierKey" },
        ["C30 Role+Entra Group+Binding Method+Configuration Set"] = new[] { "roleKey", "entraGroupObjectId", "bindingMethod" },
        ["C33 Tier+Change Type+Target Stage+Portfolio+Configuration Set"] = new[] { "tierKey", "changeTypeKey", "targetStageKey", "portfolioId" },
        ["C39 Number+Version+Configuration Set"] = new[] { "number", "version" },
        ["C42 Principal+Workload Role+Configuration Set"] = new[] { "principalObjectId", "workloadRoleKey" },
        ["C47 Rank+Configuration Set"] = new[] { "rank" },
        ["C48 Rank+Configuration Set"] = new[] { "rank" },
    };

    // Operational keys whose scope is narrower than every row, by model key.
    private static readonly Dictionary<string, StoreKeyScope> OperationalScopes = new(StringComparer.Ordinal)
    {
        ["O03 Link Kind+External Id"] = StoreKeyScope.CurrentRows,
        ["O25 Data Platform+External Id"] = StoreKeyScope.InServiceRows,
        ["O26 Data Domain+Data Location+Data Origin"] = StoreKeyScope.OpenRows,
    };

    private const string ConfigurationSetColumn = "Configuration Set";

    // "<entity ID> <columns joined by +>".
    public static string Label(string entityId, IEnumerable<string> logicalColumns)
    {
        return entityId + " " + string.Join("+", logicalColumns);
    }

    // The store keys of one entity of the first release, without the keys its promoted unique columns already hold.
    internal static IReadOnlyList<StoreKey> For(LogicalEntity entity, string entityKey, IReadOnlyList<PromotedColumn> promoted)
    {
        string[] uniqueColumns = promoted.Where(p => p.IsUnique).Select(p => p.JsonName).ToArray();
        bool configurationRow = string.Equals(entity.Kind, "configuration", StringComparison.Ordinal);
        var keys = new List<StoreKey>();
        foreach (LogicalUniqueKey key in entity.UniqueKeys.Where(k => string.Equals(k.Enforcement, "store", StringComparison.Ordinal)))
        {
            string label = Label(entity.Id, key.Columns);
            if (uniqueColumns.Length > 0 && uniqueColumns.All(c => key.ContractColumns.Contains(c, StringComparer.Ordinal)))
            {
                // Held by the promoted unique columns (an index on the entity's own table): the same columns, or fewer.
                continue;
            }

            if (HeldBack.ContainsKey(label))
            {
                continue;
            }

            string[] parts;
            StoreKeyScope scope = StoreKeyScope.AllRows;
            string[] columns = key.Columns.Where(c => !configurationRow || !string.Equals(c, ConfigurationSetColumn, StringComparison.Ordinal)).ToArray();
            if (configurationRow && key.Columns.Contains(ConfigurationSetColumn, StringComparer.Ordinal))
            {
                if (columns.Length == 1 && string.Equals(columns[0], "Key", StringComparison.Ordinal))
                {
                    parts = new[] { StoreKey.KeyPathPart };
                }
                else if (ConfigurationAlternates.TryGetValue(label, out string[]? mapped))
                {
                    parts = mapped;
                    scope = mapped.Length == 1 && mapped[0] == StoreKey.KeyPathPart ? StoreKeyScope.AllRows : StoreKeyScope.ActiveRows;
                }
                else
                {
                    throw new InvalidOperationException("The logical model asks the store to enforce " + label + ", and the stores do not know the live row's fields for it. Map it in StoreKeyCatalog, or hold it back with its reason.");
                }
            }
            else
            {
                foreach (string column in key.ContractColumns)
                {
                    if (!entity.TryGetColumn(column, out _))
                    {
                        throw new InvalidOperationException("The logical model's key " + label + " names " + column + ", which " + entity.Id + " does not have.");
                    }
                }

                parts = key.ContractColumns.ToArray();
                scope = OperationalScopes.TryGetValue(label, out StoreKeyScope narrower) ? narrower : StoreKeyScope.AllRows;
            }

            // A key path covers every key that maps to it (C07 Key and Value List+Key): one stored value for both.
            string name = parts.Length == 1 && parts[0] == StoreKey.KeyPathPart ? "Key" : string.Join("+", columns);
            int same = keys.FindIndex(k => string.Equals(k.Name, name, StringComparison.Ordinal) || (k.UsesKeyPath && parts.Length == 1 && parts[0] == StoreKey.KeyPathPart));
            if (same >= 0)
            {
                StoreKey existing = keys[same];
                keys[same] = new StoreKey(entityKey, existing.Name, existing.ModelKeys.Concat(new[] { label }), existing.Parts, existing.Scope);
                continue;
            }

            keys.Add(new StoreKey(entityKey, name, new[] { label }, parts, scope));
        }

        return new ReadOnlyCollection<StoreKey>(keys);
    }
}
