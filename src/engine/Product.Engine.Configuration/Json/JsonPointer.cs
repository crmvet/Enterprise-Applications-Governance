using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Product.Engine.Core.Canonical;

namespace Product.Engine.Configuration.Json;

// JSON Pointer helpers (RFC 6901). Paths in validation messages use this form, for example "/entities/tier/0/rank".
public static class JsonPointer
{
    public const string Root = "";

    // Appends one member name, escaping "~" and "/".
    public static string Append(string pointer, string token)
    {
        if (token == null)
        {
            throw new ArgumentNullException(nameof(token));
        }

        return pointer + "/" + token.Replace("~", "~0").Replace("/", "~1");
    }

    // Appends one array index.
    public static string Append(string pointer, int index)
    {
        return pointer + "/" + index.ToString(CultureInfo.InvariantCulture);
    }

    // Splits a pointer into its unescaped tokens. The root pointer has no tokens.
    public static IReadOnlyList<string> Split(string pointer)
    {
        var tokens = new List<string>();
        if (string.IsNullOrEmpty(pointer))
        {
            return tokens;
        }

        if (pointer[0] != '/')
        {
            throw new ArgumentException("A JSON pointer starts with a slash.", nameof(pointer));
        }

        foreach (string raw in pointer.Substring(1).Split('/'))
        {
            tokens.Add(raw.Replace("~1", "/").Replace("~0", "~"));
        }

        return tokens;
    }

    // Finds the value a pointer names. False when any step is missing.
    public static bool TryResolve(JsonTreeNode root, string pointer, out JsonTreeNode value)
    {
        value = root;
        foreach (string token in Split(pointer))
        {
            if (value.Kind == JsonTreeKind.Object)
            {
                if (!value.TryGetMember(token, out JsonTreeNode? member))
                {
                    return false;
                }

                value = member;
            }
            else if (value.Kind == JsonTreeKind.Array)
            {
                if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out int index) || index >= value.Items.Count)
                {
                    return false;
                }

                value = value.Items[index];
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    // Shows the root pointer as "(root)" in messages, so a message never ends in an empty path.
    public static string Display(string pointer)
    {
        return string.IsNullOrEmpty(pointer) ? "(root)" : pointer;
    }

    // A short display form of a JSON value for messages: strings in quotes, long text cut.
    public static string Show(JsonTreeNode value)
    {
        string text = value.Kind == JsonTreeKind.String ? "\"" + value.GetString() + "\"" : value.ToString();
        if (text.Length > 60)
        {
            var sb = new StringBuilder(text, 0, 57, 60);
            sb.Append("...");
            text = sb.ToString();
        }

        return text;
    }
}
