using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Core.Canonical;

namespace Product.Engine.Runtime.Tests;

internal static class RuntimeJsonTestHelpers
{
    public static JsonTreeNode Object(params (string Name, JsonTreeNode Value)[] members)
    {
        return JsonTreeNode.CreateObject(members.Select(m => new KeyValuePair<string, JsonTreeNode>(m.Name, m.Value)));
    }

    public static JsonTreeNode With(JsonTreeNode obj, string name, JsonTreeNode value)
    {
        return JsonTreeNode.CreateObject(obj.Members.Where(m => !string.Equals(m.Key, name, StringComparison.Ordinal)).Concat(new[] { new KeyValuePair<string, JsonTreeNode>(name, value) }));
    }

    public static string? Text(JsonTreeNode? node, string member)
    {
        return node != null && node.TryGetMember(member, out JsonTreeNode? value) && value.Kind == JsonTreeKind.String ? value.GetString() : null;
    }

    public static bool Flag(JsonTreeNode? node, string member)
    {
        return node != null && node.TryGetMember(member, out JsonTreeNode? value) && value.Kind == JsonTreeKind.True;
    }

    public static JsonTreeNode? Member(JsonTreeNode? node, string member)
    {
        return node != null && node.TryGetMember(member, out JsonTreeNode? value) ? value : null;
    }

    public static string[] Strings(JsonTreeNode? node, string member)
    {
        JsonTreeNode? array = Member(node, member);
        return array == null || array.Kind != JsonTreeKind.Array ? Array.Empty<string>() : array.Items.Where(i => i.Kind == JsonTreeKind.String).Select(i => i.GetString()).ToArray();
    }
}
