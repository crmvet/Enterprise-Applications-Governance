using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Product.Engine.Core.Canonical;

namespace Product.Engine.Runtime.Ports;

// A typed query a fact plan holds: one entity and column equality conditions (document 25, section 3.3: "a short list
// of typed queries"). An adapter turns it into one indexed select, or one Web API retrieve, and may batch several.
public sealed class RowQuery
{
    private RowQuery(string entity, IList<KeyValuePair<string, JsonTreeNode>> conditions)
    {
        Entity = entity;
        Conditions = new ReadOnlyCollection<KeyValuePair<string, JsonTreeNode>>(conditions);
    }

    public string Entity { get; }

    // Column and value pairs that must all hold. A JSON null matches an absent column too.
    public IReadOnlyList<KeyValuePair<string, JsonTreeNode>> Conditions { get; }

    public static RowQuery For(string entity)
    {
        if (string.IsNullOrEmpty(entity))
        {
            throw new ArgumentException("A query names its entity.", nameof(entity));
        }

        return new RowQuery(entity, new List<KeyValuePair<string, JsonTreeNode>>());
    }

    public RowQuery Where(string column, string? value)
    {
        return Where(column, value == null ? JsonTreeNode.Null : JsonTreeNode.FromString(value));
    }

    public RowQuery Where(string column, JsonTreeNode value)
    {
        if (string.IsNullOrEmpty(column))
        {
            throw new ArgumentException("A condition names its column.", nameof(column));
        }

        var conditions = Conditions.ToList();
        conditions.Add(new KeyValuePair<string, JsonTreeNode>(column, value ?? JsonTreeNode.Null));
        return new RowQuery(Entity, conditions);
    }

    public bool Matches(StoredRow row)
    {
        if (row == null || !string.Equals(row.Entity, Entity, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (KeyValuePair<string, JsonTreeNode> condition in Conditions)
        {
            JsonTreeNode actual = row.Column(condition.Key) ?? JsonTreeNode.Null;
            if (!actual.Equals(condition.Value))
            {
                return false;
            }
        }

        return true;
    }

    public override string ToString()
    {
        return Entity + (Conditions.Count == 0 ? string.Empty : " where " + string.Join(" and ", Conditions.Select(c => c.Key + " = " + c.Value)));
    }
}
