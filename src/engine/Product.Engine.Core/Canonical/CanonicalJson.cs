using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Product.Engine.Core.Canonical;

// Which canonical form to write.
public enum CanonicalProfile
{
    // For request payloads, patches and any JSON whose array order carries meaning. Array order is kept.
    Request,

    // For configuration snapshots (C02 Content Json, a bundle in the format of document 21, section 8.2).
    // The arrays that are the values of the top-level "entities" object hold rows. When every row in such an
    // array is an object with a string "key", the rows are sorted by key (ordinal), then by their canonical text.
    // No other array is reordered, so condition lists, patch operations and sequences keep their order.
    Snapshot,
}

// Deterministic, culture-invariant canonical JSON (document 25, sections 6.2 and 9).
//
// Input is strict RFC 8259 JSON text: no comments, no trailing commas, no byte order mark,
// no NaN or Infinity, no leading zeros, at most MaxDepth levels, and no repeated member names.
//
// The canonical form:
//   no insignificant whitespace;
//   object members sorted by name, ordinal on UTF-16 code units;
//   numbers in the exact decimal form of CanonicalNumber ("1.0" is written "1");
//   strings escape only the quote, the backslash and U+0000 to U+001F
//   (\b \t \n \f \r as short forms, other control characters as \u00xx in lowercase hex);
//   every other character is written as itself, and the bytes are UTF-8 without a byte order mark;
//   no Unicode normalization, so the result never depends on the runtime's Unicode tables;
//   lone surrogates are refused;
//   arrays keep their order, except entity rows in the Snapshot profile.
// Dates are strings. Writers put UTC date-times in the form of FormatUtc so equal moments give equal text.
public static class CanonicalJson
{
    // The deepest nesting accepted. Snapshots and requests need far less.
    public const int MaxDepth = 128;

    private const string EntitiesMember = "entities";
    private const string RowKeyMember = "key";

    public static JsonTreeNode Parse(string json)
    {
        if (json == null)
        {
            throw new ArgumentNullException(nameof(json));
        }

        return new Parser(json).ParseDocument();
    }

    public static bool TryParse(string? json, [NotNullWhen(true)] out JsonTreeNode? node, [NotNullWhen(false)] out string? error)
    {
        if (json == null)
        {
            node = null;
            error = "The JSON text is missing.";
            return false;
        }

        try
        {
            node = new Parser(json).ParseDocument();
            error = null;
            return true;
        }
        catch (CanonicalJsonException e)
        {
            node = null;
            error = e.Message;
            return false;
        }
    }

    // Parses JSON text and writes its canonical form.
    public static string Canonicalize(string json, CanonicalProfile profile)
    {
        return Write(Parse(json), profile);
    }

    public static string Write(JsonTreeNode node, CanonicalProfile profile)
    {
        if (node == null)
        {
            throw new ArgumentNullException(nameof(node));
        }

        var sb = new StringBuilder();
        WriteNode(sb, node, profile == CanonicalProfile.Snapshot ? Place.Root : Place.Plain);
        return sb.ToString();
    }

    // The UTF-8 bytes of canonical text, without a byte order mark. Throws on a lone surrogate.
    public static byte[] ToUtf8(string canonicalText)
    {
        if (canonicalText == null)
        {
            throw new ArgumentNullException(nameof(canonicalText));
        }

        return StrictUtf8.GetBytes(canonicalText);
    }

    // A UTC date-time in ISO 8601: "yyyy-MM-ddTHH:mm:ssZ", with a fraction of a second only when it is not zero,
    // and no trailing zeros in the fraction. For example "2026-11-22T09:30:00Z" or "2026-11-22T09:30:00.25Z".
    public static string FormatUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Only a UTC date-time (DateTimeKind.Utc) has a canonical form.", nameof(value));
        }

        string text = value.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss", CultureInfo.InvariantCulture);
        long fraction = value.Ticks % TimeSpan.TicksPerSecond;
        if (fraction != 0)
        {
            text += "." + fraction.ToString("0000000", CultureInfo.InvariantCulture).TrimEnd('0');
        }

        return text + "Z";
    }

    // The index of the first lone surrogate in the text, or -1 when the text is well-formed UTF-16.
    internal static int FindLoneSurrogate(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    i++;
                    continue;
                }

                return i;
            }

            if (char.IsLowSurrogate(c))
            {
                return i;
            }
        }

        return -1;
    }

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private enum Place
    {
        // Any value outside the snapshot rule.
        Plain,

        // The root of a snapshot.
        Root,

        // The "entities" object of a snapshot root.
        Entities,

        // An array that is a member value of the "entities" object.
        EntityRows,
    }

    private static void WriteNode(StringBuilder sb, JsonTreeNode node, Place place)
    {
        switch (node.Kind)
        {
            case JsonTreeKind.Null:
                sb.Append("null");
                break;
            case JsonTreeKind.True:
                sb.Append("true");
                break;
            case JsonTreeKind.False:
                sb.Append("false");
                break;
            case JsonTreeKind.Number:
                sb.Append(node.GetNumberText());
                break;
            case JsonTreeKind.String:
                WriteString(sb, node.GetString());
                break;
            case JsonTreeKind.Array:
                WriteArray(sb, node, place);
                break;
            case JsonTreeKind.Object:
                WriteObject(sb, node, place);
                break;
            default:
                throw new InvalidOperationException("Unknown JSON node kind.");
        }
    }

    private static void WriteObject(StringBuilder sb, JsonTreeNode node, Place place)
    {
        sb.Append('{');
        bool first = true;
        foreach (KeyValuePair<string, JsonTreeNode> member in node.Members)
        {
            if (!first)
            {
                sb.Append(',');
            }

            first = false;
            WriteString(sb, member.Key);
            sb.Append(':');

            Place childPlace = Place.Plain;
            if (place == Place.Root && string.Equals(member.Key, EntitiesMember, StringComparison.Ordinal))
            {
                childPlace = Place.Entities;
            }
            else if (place == Place.Entities)
            {
                childPlace = Place.EntityRows;
            }

            WriteNode(sb, member.Value, childPlace);
        }

        sb.Append('}');
    }

    private static void WriteArray(StringBuilder sb, JsonTreeNode node, Place place)
    {
        IReadOnlyList<JsonTreeNode> items = node.Items;
        // A node caches its own canonical text (JsonTreeNode.ToString), so nested arrays are written once.
        IEnumerable<string> texts = items.Select(item => item.ToString());

        if (place == Place.EntityRows && items.Count > 1 && items.All(IsKeyedRow))
        {
            // Rows sort by key. Child rows can share a key under different parents, so their text breaks the tie.
            texts = items
                .Select(item => new { Key = RowKey(item), Text = item.ToString() })
                .OrderBy(row => row.Key, StringComparer.Ordinal)
                .ThenBy(row => row.Text, StringComparer.Ordinal)
                .Select(row => row.Text);
        }

        sb.Append('[');
        sb.Append(string.Join(",", texts));
        sb.Append(']');
    }

    private static bool IsKeyedRow(JsonTreeNode item)
    {
        return item.Kind == JsonTreeKind.Object
            && item.TryGetMember(RowKeyMember, out JsonTreeNode? key)
            && key.Kind == JsonTreeKind.String;
    }

    private static string RowKey(JsonTreeNode item)
    {
        item.TryGetMember(RowKeyMember, out JsonTreeNode? key);
        return key!.GetString();
    }

    private static void WriteString(StringBuilder sb, string value)
    {
        sb.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\b':
                    sb.Append("\\b");
                    break;
                case '\f':
                    sb.Append("\\f");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append("\\u00");
                        sb.Append(HexDigit(c >> 4));
                        sb.Append(HexDigit(c & 0xF));
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        sb.Append('"');
    }

    private static char HexDigit(int value)
    {
        return (char)(value < 10 ? '0' + value : 'a' + (value - 10));
    }

    // Strict RFC 8259 recursive descent parser. Depth is capped at MaxDepth, so recursion stays shallow.
    private sealed class Parser
    {
        private readonly string _text;
        private int _pos;

        public Parser(string text)
        {
            _text = text;
        }

        public JsonTreeNode ParseDocument()
        {
            SkipWhitespace();
            if (_pos >= _text.Length)
            {
                throw Fail("The JSON text is empty.");
            }

            JsonTreeNode root = ParseValue(1);
            SkipWhitespace();
            if (_pos != _text.Length)
            {
                throw Fail("The JSON text has more content after the first value.");
            }

            return root;
        }

        private JsonTreeNode ParseValue(int depth)
        {
            if (depth > MaxDepth)
            {
                throw Fail("The JSON value nests deeper than " + MaxDepth.ToString(CultureInfo.InvariantCulture) + " levels.");
            }

            if (_pos >= _text.Length)
            {
                throw Fail("The JSON text ends where a value is expected.");
            }

            char c = _text[_pos];
            switch (c)
            {
                case '{':
                    return ParseObject(depth);
                case '[':
                    return ParseArray(depth);
                case '"':
                    return JsonTreeNode.StringUnchecked(ParseString());
                case 't':
                    ExpectLiteral("true");
                    return JsonTreeNode.True;
                case 'f':
                    ExpectLiteral("false");
                    return JsonTreeNode.False;
                case 'n':
                    ExpectLiteral("null");
                    return JsonTreeNode.Null;
                default:
                    if (c == '-' || (c >= '0' && c <= '9'))
                    {
                        return ParseNumber();
                    }

                    throw Fail("Unexpected character where a value is expected.");
            }
        }

        private JsonTreeNode ParseObject(int depth)
        {
            int start = _pos;
            _pos++;
            var members = new List<KeyValuePair<string, JsonTreeNode>>();
            var names = new HashSet<string>(StringComparer.Ordinal);

            SkipWhitespace();
            if (Peek() == '}')
            {
                _pos++;
                return JsonTreeNode.CreateObject(members);
            }

            while (true)
            {
                SkipWhitespace();
                if (Peek() != '"')
                {
                    throw Fail("An object member name must be a string in double quotes.");
                }

                int nameAt = _pos;
                string name = ParseString();
                if (!names.Add(name))
                {
                    throw new CanonicalJsonException("An object repeats the member name \"" + name + "\".", nameAt);
                }

                SkipWhitespace();
                if (Peek() != ':')
                {
                    throw Fail("A colon must follow an object member name.");
                }

                _pos++;
                SkipWhitespace();
                JsonTreeNode value = ParseValue(depth + 1);
                members.Add(new KeyValuePair<string, JsonTreeNode>(name, value));

                SkipWhitespace();
                char next = Peek();
                if (next == ',')
                {
                    _pos++;
                    continue;
                }

                if (next == '}')
                {
                    _pos++;
                    break;
                }

                throw Fail(_pos >= _text.Length ? "The object starting at character " + start.ToString(CultureInfo.InvariantCulture) + " is not closed." : "A comma or a closing brace must follow an object member.");
            }

            return JsonTreeNode.CreateObject(members);
        }

        private JsonTreeNode ParseArray(int depth)
        {
            int start = _pos;
            _pos++;
            var items = new List<JsonTreeNode>();

            SkipWhitespace();
            if (Peek() == ']')
            {
                _pos++;
                return JsonTreeNode.CreateArray(items);
            }

            while (true)
            {
                SkipWhitespace();
                items.Add(ParseValue(depth + 1));
                SkipWhitespace();
                char next = Peek();
                if (next == ',')
                {
                    _pos++;
                    continue;
                }

                if (next == ']')
                {
                    _pos++;
                    break;
                }

                throw Fail(_pos >= _text.Length ? "The array starting at character " + start.ToString(CultureInfo.InvariantCulture) + " is not closed." : "A comma or a closing bracket must follow an array item.");
            }

            return JsonTreeNode.CreateArray(items);
        }

        private string ParseString()
        {
            int start = _pos;
            _pos++;
            var sb = new StringBuilder();
            while (true)
            {
                if (_pos >= _text.Length)
                {
                    throw new CanonicalJsonException("A string is not closed.", start);
                }

                char c = _text[_pos];
                if (c == '"')
                {
                    _pos++;
                    break;
                }

                if (c < 0x20)
                {
                    throw Fail("A control character inside a string must be escaped.");
                }

                if (c != '\\')
                {
                    sb.Append(c);
                    _pos++;
                    continue;
                }

                _pos++;
                if (_pos >= _text.Length)
                {
                    throw new CanonicalJsonException("A string is not closed.", start);
                }

                char e = _text[_pos];
                switch (e)
                {
                    case '"':
                        sb.Append('"');
                        break;
                    case '\\':
                        sb.Append('\\');
                        break;
                    case '/':
                        sb.Append('/');
                        break;
                    case 'b':
                        sb.Append('\b');
                        break;
                    case 'f':
                        sb.Append('\f');
                        break;
                    case 'n':
                        sb.Append('\n');
                        break;
                    case 'r':
                        sb.Append('\r');
                        break;
                    case 't':
                        sb.Append('\t');
                        break;
                    case 'u':
                        sb.Append(ParseHex4());
                        continue;
                    default:
                        throw Fail("Unknown escape sequence in a string.");
                }

                _pos++;
            }

            string value = sb.ToString();
            int bad = FindLoneSurrogate(value);
            if (bad >= 0)
            {
                throw new CanonicalJsonException("A string holds a lone surrogate, which is not valid Unicode text.", start);
            }

            return value;
        }

        // Reads the four hex digits after "\u" and leaves the position after them.
        private char ParseHex4()
        {
            int digitsAt = _pos + 1;
            if (digitsAt + 4 > _text.Length)
            {
                throw Fail("A \\u escape needs four hex digits.");
            }

            int value = 0;
            for (int i = 0; i < 4; i++)
            {
                char h = _text[digitsAt + i];
                int digit;
                if (h >= '0' && h <= '9')
                {
                    digit = h - '0';
                }
                else if (h >= 'a' && h <= 'f')
                {
                    digit = h - 'a' + 10;
                }
                else if (h >= 'A' && h <= 'F')
                {
                    digit = h - 'A' + 10;
                }
                else
                {
                    throw new CanonicalJsonException("A \\u escape needs four hex digits.", digitsAt + i);
                }

                value = (value * 16) + digit;
            }

            _pos = digitsAt + 4;
            return (char)value;
        }

        private JsonTreeNode ParseNumber()
        {
            int start = _pos;
            while (_pos < _text.Length)
            {
                char c = _text[_pos];
                bool numberChar = (c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E';
                if (!numberChar)
                {
                    break;
                }

                _pos++;
            }

            string text = _text.Substring(start, _pos - start);
            if (!CanonicalNumber.TryCanonicalize(text, out string canonical, out string error))
            {
                throw new CanonicalJsonException(error, start);
            }

            return JsonTreeNode.NumberUnchecked(canonical);
        }

        private void ExpectLiteral(string literal)
        {
            if (string.CompareOrdinal(_text, _pos, literal, 0, literal.Length) != 0 || _pos + literal.Length > _text.Length)
            {
                throw Fail("Unknown literal. Only true, false and null are allowed.");
            }

            _pos += literal.Length;
        }

        private void SkipWhitespace()
        {
            while (_pos < _text.Length)
            {
                char c = _text[_pos];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
                {
                    _pos++;
                }
                else
                {
                    break;
                }
            }
        }

        private char Peek()
        {
            return _pos < _text.Length ? _text[_pos] : '\0';
        }

        private CanonicalJsonException Fail(string message)
        {
            return new CanonicalJsonException(message, _pos);
        }
    }
}
