using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Product.Engine.Core.Canonical;

namespace Product.Engine.Runtime;

// Small JSON helpers for the runtime. Columns are JSON objects in logical lowerCamelCase names.
internal static class RuntimeJson
{
    public static readonly JsonTreeNode Empty = JsonTreeNode.CreateObject();

    public static JsonTreeNode Object(params (string Name, JsonTreeNode? Value)[] members)
    {
        return JsonTreeNode.CreateObject(members.Where(m => m.Value != null).Select(m => new KeyValuePair<string, JsonTreeNode>(m.Name, m.Value!)));
    }

    public static JsonTreeNode Text(string? value)
    {
        return value == null ? JsonTreeNode.Null : JsonTreeNode.FromString(value);
    }

    public static JsonTreeNode? OptionalText(string? value)
    {
        return value == null ? null : JsonTreeNode.FromString(value);
    }

    public static JsonTreeNode Strings(IEnumerable<string> values)
    {
        return JsonTreeNode.CreateArray(values.Select(JsonTreeNode.FromString));
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

    public static JsonTreeNode Without(JsonTreeNode obj, IEnumerable<string> names)
    {
        var drop = new HashSet<string>(names, StringComparer.Ordinal);
        return JsonTreeNode.CreateObject(obj.Members.Where(m => !drop.Contains(m.Key)));
    }

    public static JsonTreeNode With(JsonTreeNode obj, string name, JsonTreeNode value)
    {
        return Merge(obj, JsonTreeNode.CreateObject(new KeyValuePair<string, JsonTreeNode>(name, value)));
    }

    public static string? Text(JsonTreeNode? node, string member)
    {
        if (node == null || !node.TryGetMember(member, out JsonTreeNode? value))
        {
            return null;
        }

        return value.Kind == JsonTreeKind.String && value.GetString().Length > 0 ? value.GetString() : null;
    }

    public static bool Flag(JsonTreeNode? node, string member)
    {
        return node != null && node.TryGetMember(member, out JsonTreeNode? value) && value.Kind == JsonTreeKind.True;
    }

    public static long? Number(JsonTreeNode? node, string member)
    {
        return node != null && node.TryGetMember(member, out JsonTreeNode? value) && value.TryGetInt64(out long number) ? number : null;
    }

    public static IReadOnlyList<string> StringList(JsonTreeNode? node, string member)
    {
        if (node == null || !node.TryGetMember(member, out JsonTreeNode? value) || value.Kind != JsonTreeKind.Array)
        {
            return Array.Empty<string>();
        }

        return value.Items.Where(i => i.Kind == JsonTreeKind.String).Select(i => i.GetString()).ToArray();
    }

    // A UTC date-time column written by the engine (ISO 8601 with Z), or a date (yyyy-MM-dd) read as midnight UTC.
    public static DateTime? Utc(JsonTreeNode? node, string member)
    {
        string? text = Text(node, member);
        if (text == null)
        {
            return null;
        }

        string[] formats = { "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", "yyyy-MM-dd" };
        if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime value))
        {
            return DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        return null;
    }

    public static string Canonical(JsonTreeNode node)
    {
        return CanonicalJson.Write(node, CanonicalProfile.Snapshot);
    }

    // An entity key from a logical name: "Audit Entry" becomes "audit-entry" (document 20, section 3.2).
    public static string EntityKey(string logicalName)
    {
        return logicalName.Trim().ToLowerInvariant().Replace(' ', '-');
    }
}
