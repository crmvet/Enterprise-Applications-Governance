using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Core.Canonical;

namespace Product.Engine.Configuration.Json;

// Small helpers over the core's immutable JSON model.
// JsonTreeNode never changes, so every edit returns a new node.
public static class JsonNodes
{
    public static readonly JsonTreeNode EmptyObject = JsonTreeNode.CreateObject();

    public static readonly JsonTreeNode EmptyArray = JsonTreeNode.CreateArray();

    public static KeyValuePair<string, JsonTreeNode> Member(string name, JsonTreeNode value)
    {
        return new KeyValuePair<string, JsonTreeNode>(name, value);
    }

    public static KeyValuePair<string, JsonTreeNode> Member(string name, string value)
    {
        return new KeyValuePair<string, JsonTreeNode>(name, JsonTreeNode.FromString(value));
    }

    // The object with one member set. Other members keep their value.
    public static JsonTreeNode With(JsonTreeNode obj, string name, JsonTreeNode value)
    {
        RequireObject(obj);
        var members = obj.Members.Where(m => !string.Equals(m.Key, name, StringComparison.Ordinal)).ToList();
        members.Add(Member(name, value));
        return JsonTreeNode.CreateObject(members);
    }

    // The object without one member.
    public static JsonTreeNode Without(JsonTreeNode obj, string name)
    {
        RequireObject(obj);
        if (!obj.TryGetMember(name, out _))
        {
            return obj;
        }

        return JsonTreeNode.CreateObject(obj.Members.Where(m => !string.Equals(m.Key, name, StringComparison.Ordinal)));
    }

    // The member as a string, or null when it is missing or not a string.
    public static string? GetString(JsonTreeNode obj, string name)
    {
        if (obj.Kind == JsonTreeKind.Object && obj.TryGetMember(name, out JsonTreeNode? value) && value.Kind == JsonTreeKind.String)
        {
            return value.GetString();
        }

        return null;
    }

    // The member, or null when it is missing or the node is not an object.
    public static JsonTreeNode? Get(JsonTreeNode obj, string name)
    {
        if (obj.Kind == JsonTreeKind.Object && obj.TryGetMember(name, out JsonTreeNode? value))
        {
            return value;
        }

        return null;
    }

    // A row is active unless it says isActive false (document 21, section 3.2: true by default in a seed).
    public static bool IsActive(JsonTreeNode row)
    {
        JsonTreeNode? value = Get(row, "isActive");
        return value == null || value.Kind != JsonTreeKind.False;
    }

    public static bool HasMember(JsonTreeNode obj, string name)
    {
        return obj.Kind == JsonTreeKind.Object && obj.TryGetMember(name, out _);
    }

    private static void RequireObject(JsonTreeNode obj)
    {
        if (obj == null)
        {
            throw new ArgumentNullException(nameof(obj));
        }

        if (obj.Kind != JsonTreeKind.Object)
        {
            throw new ArgumentException("The node is not a JSON object.", nameof(obj));
        }
    }
}
