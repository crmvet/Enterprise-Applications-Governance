using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Json;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;

namespace Product.Engine.Configuration.Model;

// The mutable form of a content while presets, overlays and patches are applied.
// Rows stay JSON objects. Child rows stay nested in their parent row.
internal sealed class ContentBuilder
{
    private readonly SortedDictionary<string, List<JsonTreeNode>> _entities = new(StringComparer.Ordinal);
    private readonly HashSet<string> _touched = new(StringComparer.Ordinal);

    public ContentBuilder()
    {
    }

    public ContentBuilder(ConfigurationContent content)
    {
        foreach (KeyValuePair<string, IReadOnlyList<JsonTreeNode>> entity in content.EntityLists)
        {
            _entities[entity.Key] = entity.Value.ToList();
        }
    }

    public IEnumerable<string> EntityKeys => _entities.Keys;

    public ContentBuilder Clone()
    {
        var copy = new ContentBuilder();
        foreach (KeyValuePair<string, List<JsonTreeNode>> entity in _entities)
        {
            copy._entities[entity.Key] = new List<JsonTreeNode>(entity.Value);
        }

        copy._touched.UnionWith(_touched);
        return copy;
    }

    // The row list of an entity, created empty when missing.
    public List<JsonTreeNode> Rows(string entity)
    {
        if (!_entities.TryGetValue(entity, out List<JsonTreeNode>? rows))
        {
            rows = new List<JsonTreeNode>();
            _entities.Add(entity, rows);
        }

        return rows;
    }

    public bool HasEntity(string entity)
    {
        return _entities.ContainsKey(entity);
    }

    // The index of the first row with this key, or -1.
    public int IndexOf(string entity, string key)
    {
        if (!_entities.TryGetValue(entity, out List<JsonTreeNode>? rows))
        {
            return -1;
        }

        for (int i = 0; i < rows.Count; i++)
        {
            if (string.Equals(JsonNodes.GetString(rows[i], "key"), key, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    // Records that a client change touched a row, so it becomes origin local (document 21, section 3.2).
    public void MarkTouched(string entity, string keyPath)
    {
        _touched.Add(entity + "\u0000" + keyPath);
    }

    public bool IsTouched(string entity, string keyPath)
    {
        return _touched.Contains(entity + "\u0000" + keyPath);
    }

    // The immutable content. With stampOrigin, every row gets an origin (working sets only):
    // a row a client change touched is local; any other row keeps its origin, or becomes shipped.
    public ConfigurationContent Freeze(bool stampOrigin)
    {
        var lists = new List<KeyValuePair<string, IEnumerable<JsonTreeNode>>>();
        foreach (KeyValuePair<string, List<JsonTreeNode>> entity in _entities)
        {
            IEnumerable<JsonTreeNode> rows = entity.Value;
            if (stampOrigin)
            {
                rows = entity.Value.Select(row => StampOrigin(entity.Key, row)).ToList();
            }

            lists.Add(new KeyValuePair<string, IEnumerable<JsonTreeNode>>(entity.Key, rows));
        }

        return new ConfigurationContent(lists);
    }

    private JsonTreeNode StampOrigin(string entity, JsonTreeNode row)
    {
        if (row.Kind != JsonTreeKind.Object)
        {
            return row;
        }

        string? key = JsonNodes.GetString(row, "key");
        JsonTreeNode stamped = WithOrigin(row, key != null && IsTouched(entity, key));

        if (ConfigurationEntities.TryGet(entity, out ConfigurationEntity? info)
            && info.ChildField != null
            && stamped.TryGetMember(info.ChildField, out JsonTreeNode? children)
            && children.Kind == JsonTreeKind.Array)
        {
            var stampedChildren = new List<JsonTreeNode>();
            foreach (JsonTreeNode child in children.Items)
            {
                string? childKey = JsonNodes.GetString(child, "key");
                bool touched = key != null && childKey != null && IsTouched(info.ChildKey!, key + "/" + childKey);
                stampedChildren.Add(child.Kind == JsonTreeKind.Object ? WithOrigin(child, touched) : child);
            }

            stamped = JsonNodes.With(stamped, info.ChildField, JsonTreeNode.CreateArray(stampedChildren));
        }

        return stamped;
    }

    private static JsonTreeNode WithOrigin(JsonTreeNode row, bool touched)
    {
        if (touched)
        {
            return JsonNodes.With(row, "origin", JsonTreeNode.FromString(Wire(Origin.Local)));
        }

        return JsonNodes.HasMember(row, "origin") ? row : JsonNodes.With(row, "origin", JsonTreeNode.FromString(Wire(Origin.Shipped)));
    }

    private static string Wire(Origin origin)
    {
        return EngineContract.Origins.ToWire(origin);
    }
}
