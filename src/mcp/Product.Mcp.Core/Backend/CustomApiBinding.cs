using System;
using System.Linq;

namespace Product.Mcp.Core.Backend;

// The custom API names of the Dataverse binding (document 24, section 9.1): the publisher prefix plus the tool name in
// PascalCase; reads are functions, writes are actions. The Dataverse backend calls them through the Web API as the person.
public static class CustomApiBinding
{
    public static string ApiName(string prefix, string toolName)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            throw new ArgumentException("A custom API name carries the publisher prefix.", nameof(prefix));
        }

        return prefix + "_" + string.Concat(toolName.Split('_').Where(p => p.Length > 0).Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1)));
    }

    public static string Binding(bool isWrite)
    {
        return isWrite ? "action" : "function";
    }
}
