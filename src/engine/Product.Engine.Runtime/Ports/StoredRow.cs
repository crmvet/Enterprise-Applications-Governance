using System;
using Product.Engine.Core.Canonical;

namespace Product.Engine.Runtime.Ports;

// One row as a store port returns it, in logical names (document 25, section 3.2: only adapters know physical names).
// Columns are the contract layer's lowerCamelCase forms (document 20, section 3.3). Configuration references travel as
// keys and operational references as IDs.
public sealed class StoredRow
{
    public StoredRow(string entity, string id, string? key, JsonTreeNode columns)
    {
        if (string.IsNullOrEmpty(entity))
        {
            throw new ArgumentException("A row names its entity.", nameof(entity));
        }

        if (string.IsNullOrEmpty(id))
        {
            throw new ArgumentException("A row has an ID.", nameof(id));
        }

        if (columns == null || columns.Kind != JsonTreeKind.Object)
        {
            throw new ArgumentException("A row's columns are a JSON object.", nameof(columns));
        }

        Entity = entity;
        Id = id;
        Key = string.IsNullOrEmpty(key) ? null : key;
        Columns = columns;
    }

    // The entity key, kebab-case of the logical name, such as "decision" or "tier".
    public string Entity { get; }

    // The row ID the store gave it.
    public string Id { get; }

    // For a configuration row, its key path (a child row's is "parent/child"). Null for an operational row.
    public string? Key { get; }

    public JsonTreeNode Columns { get; }

    public JsonTreeNode? Column(string name)
    {
        return Columns.TryGetMember(name, out JsonTreeNode? value) ? value : null;
    }

    // A non-empty string column, or null.
    public string? Text(string name)
    {
        JsonTreeNode? value = Column(name);
        return value != null && value.Kind == JsonTreeKind.String && value.GetString().Length > 0 ? value.GetString() : null;
    }

    public bool Flag(string name)
    {
        JsonTreeNode? value = Column(name);
        return value != null && value.Kind == JsonTreeKind.True;
    }

    public long? Number(string name)
    {
        JsonTreeNode? value = Column(name);
        return value != null && value.Kind == JsonTreeKind.Number && value.TryGetInt64(out long number) ? number : null;
    }

    public override string ToString()
    {
        return Entity + " " + (Key ?? Id);
    }
}
