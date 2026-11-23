using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Product.Engine.Core.Canonical;

namespace Product.Engine.Configuration.Json;

// Reads JSON files strictly and writes indented JSON that is byte-stable across runs.
public static class JsonText
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    // Reads a UTF-8 file and parses it with the core's strict parser (RFC 8259, no repeated member names).
    // A byte order mark is skipped. Returns false with a plain error when the file does not parse.
    public static bool TryReadFile(string path, out JsonTreeNode? node, out string? error)
    {
        node = null;
        string text;
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            int start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            text = Utf8NoBom.GetString(bytes, start, bytes.Length - start);
        }
        catch (IOException e)
        {
            error = "cannot read the file: " + e.Message;
            return false;
        }
        catch (UnauthorizedAccessException e)
        {
            error = "cannot read the file: " + e.Message;
            return false;
        }
        catch (DecoderFallbackException)
        {
            error = "the file is not valid UTF-8";
            return false;
        }

        if (!CanonicalJson.TryParse(text, out JsonTreeNode? parsed, out string? parseError))
        {
            error = "does not parse: " + parseError;
            return false;
        }

        node = parsed;
        error = null;
        return true;
    }

    // Writes indented JSON with two spaces, "\n" line ends and a final newline.
    // Members come in ordinal order (the core keeps them sorted). Arrays of scalars stay on one line.
    // With snapshotOrder, the row arrays under "entities" are sorted by key, as the canonical snapshot form does.
    // The same value always gives the same bytes.
    public static string WriteIndented(JsonTreeNode node, bool snapshotOrder)
    {
        if (node == null)
        {
            throw new ArgumentNullException(nameof(node));
        }

        var sb = new StringBuilder();
        Write(sb, node, 0, snapshotOrder ? Place.Root : Place.Plain);
        sb.Append('\n');
        return sb.ToString();
    }

    // Writes the text as UTF-8 without a byte order mark.
    public static void WriteFile(string path, string text)
    {
        File.WriteAllText(path, text, Utf8NoBom);
    }

    private enum Place
    {
        Plain,
        Root,
        Entities,
        EntityRows,
    }

    private static void Write(StringBuilder sb, JsonTreeNode node, int depth, Place place)
    {
        switch (node.Kind)
        {
            case JsonTreeKind.Object:
                WriteObject(sb, node, depth, place);
                break;
            case JsonTreeKind.Array:
                WriteArray(sb, node, depth, place);
                break;
            default:
                // Scalars: the canonical text is already valid JSON.
                sb.Append(node.ToString());
                break;
        }
    }

    private static void WriteObject(StringBuilder sb, JsonTreeNode node, int depth, Place place)
    {
        if (node.Members.Count == 0)
        {
            sb.Append("{}");
            return;
        }

        sb.Append("{\n");
        for (int i = 0; i < node.Members.Count; i++)
        {
            KeyValuePair<string, JsonTreeNode> member = node.Members[i];
            Indent(sb, depth + 1);
            sb.Append(JsonTreeNode.FromString(member.Key).ToString());
            sb.Append(": ");

            Place child = Place.Plain;
            if (place == Place.Root && string.Equals(member.Key, "entities", StringComparison.Ordinal))
            {
                child = Place.Entities;
            }
            else if (place == Place.Entities)
            {
                child = Place.EntityRows;
            }

            Write(sb, member.Value, depth + 1, child);
            sb.Append(i < node.Members.Count - 1 ? ",\n" : "\n");
        }

        Indent(sb, depth);
        sb.Append('}');
    }

    private static void WriteArray(StringBuilder sb, JsonTreeNode node, int depth, Place place)
    {
        IReadOnlyList<JsonTreeNode> items = node.Items;
        if (items.Count == 0)
        {
            sb.Append("[]");
            return;
        }

        if (items.All(item => item.Kind != JsonTreeKind.Object && item.Kind != JsonTreeKind.Array))
        {
            sb.Append('[');
            sb.Append(string.Join(", ", items.Select(item => item.ToString())));
            sb.Append(']');
            return;
        }

        IEnumerable<JsonTreeNode> ordered = items;
        if (place == Place.EntityRows && items.All(IsKeyedRow))
        {
            // Same order as CanonicalJson's snapshot profile: by key, then by canonical text.
            ordered = items
                .OrderBy(item => JsonNodes.GetString(item, "key"), StringComparer.Ordinal)
                .ThenBy(item => item.ToString(), StringComparer.Ordinal);
        }

        sb.Append("[\n");
        JsonTreeNode[] list = ordered.ToArray();
        for (int i = 0; i < list.Length; i++)
        {
            Indent(sb, depth + 1);
            Write(sb, list[i], depth + 1, Place.Plain);
            sb.Append(i < list.Length - 1 ? ",\n" : "\n");
        }

        Indent(sb, depth);
        sb.Append(']');
    }

    private static bool IsKeyedRow(JsonTreeNode item)
    {
        return JsonNodes.GetString(item, "key") != null;
    }

    private static void Indent(StringBuilder sb, int depth)
    {
        sb.Append(' ', depth * 2);
    }
}
