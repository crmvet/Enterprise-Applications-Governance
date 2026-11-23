using System;
using System.Diagnostics.CodeAnalysis;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;

namespace Product.Engine.Core.Snapshots;

// One configuration row in a snapshot (document 25, section 6.2).
// Rows carry their key, active flag, origin, engine meaning and extra columns.
// The configuration assembly builds rows from Content Json. The core only reads them.
public sealed class SnapshotRow
{
    public SnapshotRow(string key, bool isActive, Origin origin, JsonTreeNode columns, string? engineMeaning = null, string? parentKey = null)
    {
        if (!ConfigurationKey.IsValid(key))
        {
            throw new ArgumentException("A row key must use the configuration key format.", nameof(key));
        }

        if (parentKey != null && !ConfigurationKey.IsValid(parentKey))
        {
            throw new ArgumentException("A parent key must use the configuration key format.", nameof(parentKey));
        }

        if (!Enum.IsDefined(typeof(Origin), origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin));
        }

        if (columns == null)
        {
            throw new ArgumentNullException(nameof(columns));
        }

        if (columns.Kind != JsonTreeKind.Object)
        {
            throw new ArgumentException("A row's columns are a JSON object.", nameof(columns));
        }

        // The meaning is kept as its wire value. Which contract list it belongs to depends on the entity.
        if (engineMeaning != null && !WireFormat.IsKebabCase(engineMeaning))
        {
            throw new ArgumentException("An engine meaning is a kebab-case wire value.", nameof(engineMeaning));
        }

        Key = key;
        ParentKey = parentKey;
        IsActive = isActive;
        Origin = origin;
        EngineMeaning = engineMeaning;
        Columns = columns;
    }

    public string Key { get; }

    // For child entities (C07 List Value, C10 Tier Answer Option, C19 Decision Outcome) the key is unique
    // only within its parent (document 21, section 13.5). Null for other entities.
    public string? ParentKey { get; }

    // The key path of document 21, section 8.2: "key", or "parent/key" for a child row.
    public string KeyPath => ParentKey == null ? Key : ParentKey + "/" + Key;

    // False when the row is retired. Retired rows stay so old records still resolve.
    public bool IsActive { get; }

    public Origin Origin { get; }

    // The engine meaning wire value, on the entities that map labels to meanings. Null elsewhere.
    public string? EngineMeaning { get; }

    // Every other column, by its lowerCamelCase field name (document 21, section 8.3).
    public JsonTreeNode Columns { get; }

    public bool TryGetColumn(string name, [NotNullWhen(true)] out JsonTreeNode? value)
    {
        return Columns.TryGetMember(name, out value);
    }
}
