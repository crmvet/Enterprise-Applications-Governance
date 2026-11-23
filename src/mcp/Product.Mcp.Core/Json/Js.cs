using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Product.Engine.Core.Canonical;

namespace Product.Mcp.Core.Json;

// Small helpers over the engine's JSON model (JsonTreeNode), so the server core needs no JSON library of its own.
// Objects are canonical: members sorted by name, never duplicated.
public static class Js
{
    public static readonly JsonTreeNode EmptyObject = JsonTreeNode.CreateObject();

    public static readonly JsonTreeNode EmptyArray = JsonTreeNode.CreateArray();

    // An object from name and value pairs. A C# null value leaves the member out; JsonTreeNode.Null writes JSON null.
    public static JsonTreeNode Obj(params (string Name, JsonTreeNode? Value)[] members)
    {
        return JsonTreeNode.CreateObject(members.Where(m => m.Value != null).Select(m => new KeyValuePair<string, JsonTreeNode>(m.Name, m.Value!)));
    }

    public static JsonTreeNode Obj(IEnumerable<KeyValuePair<string, JsonTreeNode>> members)
    {
        return JsonTreeNode.CreateObject(members);
    }

    // A string, or JSON null for a null value.
    public static JsonTreeNode Str(string? value)
    {
        return value == null ? JsonTreeNode.Null : JsonTreeNode.FromString(value);
    }

    // A string, or no member at all for a null value.
    public static JsonTreeNode? OptStr(string? value)
    {
        return value == null ? null : JsonTreeNode.FromString(value);
    }

    public static JsonTreeNode Int(long value)
    {
        return JsonTreeNode.FromInt64(value);
    }

    public static JsonTreeNode Bool(bool value)
    {
        return JsonTreeNode.FromBoolean(value);
    }

    public static JsonTreeNode? OptBool(bool? value)
    {
        return value.HasValue ? JsonTreeNode.FromBoolean(value.Value) : null;
    }

    public static JsonTreeNode Arr(IEnumerable<JsonTreeNode> items)
    {
        return JsonTreeNode.CreateArray(items);
    }

    public static JsonTreeNode Strs(IEnumerable<string> values)
    {
        return JsonTreeNode.CreateArray(values.Select(JsonTreeNode.FromString));
    }

    public static JsonTreeNode Utc(DateTime value)
    {
        return JsonTreeNode.FromUtcDateTime(value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }

    public static JsonTreeNode Date(DateTime value)
    {
        return JsonTreeNode.FromString(value.ToString("yyyy'-'MM'-'dd", CultureInfo.InvariantCulture));
    }

    public static JsonTreeNode? Member(JsonTreeNode? node, string name)
    {
        return node != null && node.Kind == JsonTreeKind.Object && node.TryGetMember(name, out JsonTreeNode? value) ? value : null;
    }

    public static bool Has(JsonTreeNode? node, string name)
    {
        JsonTreeNode? value = Member(node, name);
        return value != null && value.Kind != JsonTreeKind.Null;
    }

    // A non-empty string member, or null.
    public static string? Text(JsonTreeNode? node, string name)
    {
        JsonTreeNode? value = Member(node, name);
        return value != null && value.Kind == JsonTreeKind.String && value.GetString().Length > 0 ? value.GetString() : null;
    }

    public static bool? Flag(JsonTreeNode? node, string name)
    {
        JsonTreeNode? value = Member(node, name);
        return value == null ? null : value.Kind == JsonTreeKind.True ? true : value.Kind == JsonTreeKind.False ? false : null;
    }

    public static long? Number(JsonTreeNode? node, string name)
    {
        JsonTreeNode? value = Member(node, name);
        return value != null && value.Kind == JsonTreeKind.Number && value.TryGetInt64(out long number) ? number : null;
    }

    public static IReadOnlyList<JsonTreeNode> Items(JsonTreeNode? node, string name)
    {
        JsonTreeNode? value = Member(node, name);
        return value != null && value.Kind == JsonTreeKind.Array ? value.Items : Array.Empty<JsonTreeNode>();
    }

    public static IReadOnlyList<string> Strings(JsonTreeNode? node, string name)
    {
        return Items(node, name).Where(i => i.Kind == JsonTreeKind.String).Select(i => i.GetString()).ToList();
    }

    // A UTC moment from an ISO text member (a date reads as its midnight UTC), or null.
    public static DateTime? Moment(JsonTreeNode? node, string name)
    {
        return ParseMoment(Text(node, name));
    }

    public static DateTime? ParseMoment(string? text)
    {
        if (text == null)
        {
            return null;
        }

        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime value)
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : null;
    }

    // Replaces or adds members.
    public static JsonTreeNode With(JsonTreeNode obj, params (string Name, JsonTreeNode? Value)[] members)
    {
        var map = obj.Members.ToDictionary(m => m.Key, m => m.Value, StringComparer.Ordinal);
        foreach ((string name, JsonTreeNode? value) in members)
        {
            if (value == null)
            {
                map.Remove(name);
            }
            else
            {
                map[name] = value;
            }
        }

        return JsonTreeNode.CreateObject(map);
    }

    public static JsonTreeNode Without(JsonTreeNode obj, params string[] names)
    {
        return JsonTreeNode.CreateObject(obj.Members.Where(m => !names.Contains(m.Key, StringComparer.Ordinal)));
    }

    // Compact JSON text with sorted members (the engine's request profile).
    public static string Write(JsonTreeNode node)
    {
        return CanonicalJson.Write(node, CanonicalProfile.Request);
    }

    public static JsonTreeNode Parse(string text)
    {
        return CanonicalJson.Parse(text);
    }
}
