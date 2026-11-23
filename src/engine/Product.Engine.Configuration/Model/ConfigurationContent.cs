using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Product.Engine.Configuration.Json;
using Product.Engine.Core.Canonical;

namespace Product.Engine.Configuration.Model;

// One configuration row inside a content, with where it sits.
public sealed class ContentRow
{
    internal ContentRow(string entity, string? key, string? parentKey, JsonTreeNode node, bool isParentActive, string pointer)
    {
        Entity = entity;
        Key = key;
        ParentKey = parentKey;
        Node = node;
        IsActive = JsonNodes.IsActive(node);
        IsParentActive = isParentActive;
        Pointer = pointer;
    }

    // The entity key. A child row carries its own entity key, such as "list-value".
    public string Entity { get; }

    // The row key, or null when the row has no string key (V01 and V02 report that).
    public string? Key { get; }

    // For a child row: the parent row's key. Null otherwise.
    public string? ParentKey { get; }

    // "key", or "parent/key" for a child row (document 21, section 8.2).
    public string KeyPath => ParentKey == null ? Key ?? string.Empty : ParentKey + "/" + (Key ?? string.Empty);

    public JsonTreeNode Node { get; }

    // The row's own active flag.
    public bool IsActive { get; }

    // False for a child row whose parent is retired.
    public bool IsParentActive { get; }

    // A child row of a retired parent counts as retired too.
    public bool IsEffectivelyActive => IsActive && IsParentActive;

    // Where the row sits in the bundle, for example "/entities/value-list/3/values/2".
    public string Pointer { get; }

    public string LogicalName => ConfigurationEntities.LogicalNameOf(Entity);
}

// The rows of a configuration set: one list per entity key, child rows nested in their parent row
// as in the bundle format (document 21, section 8.2). Immutable.
// Lists keep the order they were built in, so JSON pointers into the bundle match the row lists.
public sealed class ConfigurationContent
{
    private readonly SortedDictionary<string, IReadOnlyList<JsonTreeNode>> _entities;
    private readonly IReadOnlyList<ContentRow> _rows;
    private readonly Dictionary<string, ContentRow> _byPath;
    private readonly Dictionary<string, ContentRow> _byPointer;

    public ConfigurationContent(IEnumerable<KeyValuePair<string, IEnumerable<JsonTreeNode>>> entities)
    {
        if (entities == null)
        {
            throw new ArgumentNullException(nameof(entities));
        }

        _entities = new SortedDictionary<string, IReadOnlyList<JsonTreeNode>>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, IEnumerable<JsonTreeNode>> entity in entities)
        {
            _entities[entity.Key] = new ReadOnlyCollection<JsonTreeNode>(entity.Value.ToList());
        }

        var rows = new List<ContentRow>();
        _byPath = new Dictionary<string, ContentRow>(StringComparer.Ordinal);
        _byPointer = new Dictionary<string, ContentRow>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, IReadOnlyList<JsonTreeNode>> entity in _entities)
        {
            ConfigurationEntities.TryGet(entity.Key, out ConfigurationEntity? info);
            string entityPointer = JsonPointer.Append(JsonPointer.Append(JsonPointer.Root, "entities"), entity.Key);
            for (int i = 0; i < entity.Value.Count; i++)
            {
                JsonTreeNode node = entity.Value[i];
                if (node.Kind != JsonTreeKind.Object)
                {
                    continue;
                }

                string pointer = JsonPointer.Append(entityPointer, i);
                var row = new ContentRow(entity.Key, KeyOf(node), null, node, isParentActive: true, pointer);
                Add(rows, row);

                if (info?.ChildField == null || !node.TryGetMember(info.ChildField, out JsonTreeNode? children) || children.Kind != JsonTreeKind.Array)
                {
                    continue;
                }

                string childPointer = JsonPointer.Append(pointer, info.ChildField);
                for (int j = 0; j < children.Items.Count; j++)
                {
                    JsonTreeNode child = children.Items[j];
                    if (child.Kind != JsonTreeKind.Object)
                    {
                        continue;
                    }

                    Add(rows, new ContentRow(info.ChildKey!, KeyOf(child), row.Key ?? string.Empty, child, row.IsActive, JsonPointer.Append(childPointer, j)));
                }
            }
        }

        _rows = new ReadOnlyCollection<ContentRow>(rows);
    }

    public static ConfigurationContent Empty { get; } = new(Enumerable.Empty<KeyValuePair<string, IEnumerable<JsonTreeNode>>>());

    // Entity keys present, in ordinal order. An entity with an empty list is present.
    public IEnumerable<string> EntityKeys => _entities.Keys;

    // Every row, parents and children, in entity order and then list order.
    public IReadOnlyList<ContentRow> Rows => _rows;

    public bool HasEntity(string entity)
    {
        return _entities.ContainsKey(entity);
    }

    // The top-level rows of an entity as JSON, in list order. Empty when the entity is absent.
    public IReadOnlyList<JsonTreeNode> RowsOf(string entity)
    {
        return _entities.TryGetValue(entity, out IReadOnlyList<JsonTreeNode>? rows) ? rows : Array.Empty<JsonTreeNode>();
    }

    // The rows of one entity, children included when the entity is a child entity.
    public IEnumerable<ContentRow> RowsFor(string entity)
    {
        return _rows.Where(row => string.Equals(row.Entity, entity, StringComparison.Ordinal));
    }

    // Finds a row by entity key and key path. With repeated keys (a V03 error) the first one wins.
    public bool TryFindRow(string entity, string keyPath, [NotNullWhen(true)] out ContentRow? row)
    {
        return _byPath.TryGetValue(entity + "\u0000" + keyPath, out row);
    }

    // Finds the row that holds a JSON pointer into the bundle, such as "/entities/tier/0/rank".
    // The deepest row wins, so a pointer inside a child row gives the child.
    public bool TryFindRowAt(string pointer, [NotNullWhen(true)] out ContentRow? row)
    {
        row = null;
        IReadOnlyList<string> tokens = JsonPointer.Split(pointer);
        if (tokens.Count < 3 || tokens[0] != "entities")
        {
            return false;
        }

        string candidate = JsonPointer.Append(JsonPointer.Append(JsonPointer.Append(JsonPointer.Root, "entities"), tokens[1]), tokens[2]);
        if (!_byPointer.TryGetValue(candidate, out ContentRow? parent))
        {
            return false;
        }

        row = parent;
        if (tokens.Count >= 5)
        {
            string childPointer = JsonPointer.Append(JsonPointer.Append(candidate, tokens[3]), tokens[4]);
            if (_byPointer.TryGetValue(childPointer, out ContentRow? child))
            {
                row = child;
            }
        }

        return true;
    }

    // The "entities" object of a bundle: one array per entity key.
    public JsonTreeNode ToEntitiesNode()
    {
        return JsonTreeNode.CreateObject(_entities.Select(e => JsonNodes.Member(e.Key, JsonTreeNode.CreateArray(e.Value))));
    }

    // Reads the "entities" object of a bundle. Members that are not arrays are skipped (V01 reports them).
    public static ConfigurationContent FromEntitiesNode(JsonTreeNode? entities)
    {
        if (entities == null || entities.Kind != JsonTreeKind.Object)
        {
            return Empty;
        }

        return new ConfigurationContent(entities.Members
            .Where(m => m.Value.Kind == JsonTreeKind.Array)
            .Select(m => new KeyValuePair<string, IEnumerable<JsonTreeNode>>(m.Key, m.Value.Items)));
    }

    internal IEnumerable<KeyValuePair<string, IReadOnlyList<JsonTreeNode>>> EntityLists => _entities;

    private static string? KeyOf(JsonTreeNode node)
    {
        return JsonNodes.GetString(node, "key");
    }

    private void Add(List<ContentRow> rows, ContentRow row)
    {
        rows.Add(row);
        _byPointer[row.Pointer] = row;
        if (row.Key != null)
        {
            string path = row.Entity + "\u0000" + row.KeyPath;
            if (!_byPath.ContainsKey(path))
            {
                _byPath.Add(path, row);
            }
        }
    }
}
