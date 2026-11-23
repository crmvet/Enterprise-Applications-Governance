using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;

namespace Product.Engine.Core.Canonical;

public enum JsonTreeKind
{
    Null,
    False,
    True,
    Number,
    String,
    Array,
    Object,
}

// An immutable JSON value owned by the engine core.
// The core may not use a JSON library (document 25, section 3.2), so it carries this small model of its own.
// Values are normalized when built:
//   numbers hold their canonical text (CanonicalNumber), never a double;
//   strings must be well-formed UTF-16 (no lone surrogates) and are never Unicode-normalized;
//   object members are sorted by name (ordinal, UTF-16 code units) and a repeated name is refused.
// Two nodes are equal when their canonical text is equal.
public sealed class JsonTreeNode : IEquatable<JsonTreeNode>
{
    public static readonly JsonTreeNode Null = new(JsonTreeKind.Null, null, null, null, 1);
    public static readonly JsonTreeNode True = new(JsonTreeKind.True, null, null, null, 1);
    public static readonly JsonTreeNode False = new(JsonTreeKind.False, null, null, null, 1);

    private static readonly IReadOnlyList<JsonTreeNode> NoItems = new ReadOnlyCollection<JsonTreeNode>(Array.Empty<JsonTreeNode>());
    private static readonly IReadOnlyList<KeyValuePair<string, JsonTreeNode>> NoMembers =
        new ReadOnlyCollection<KeyValuePair<string, JsonTreeNode>>(Array.Empty<KeyValuePair<string, JsonTreeNode>>());

    private readonly string? _scalar;
    private readonly IReadOnlyList<JsonTreeNode>? _items;
    private readonly IReadOnlyList<KeyValuePair<string, JsonTreeNode>>? _members;
    private string? _canonical;

    private JsonTreeNode(
        JsonTreeKind kind,
        string? scalar,
        IReadOnlyList<JsonTreeNode>? items,
        IReadOnlyList<KeyValuePair<string, JsonTreeNode>>? members,
        int depth)
    {
        Kind = kind;
        _scalar = scalar;
        _items = items;
        _members = members;
        Depth = depth;
    }

    public JsonTreeKind Kind { get; }

    // Nesting depth: 1 for a scalar or an empty container. Never above CanonicalJson.MaxDepth.
    public int Depth { get; }

    // The items of an array, in order. Empty for any other kind.
    public IReadOnlyList<JsonTreeNode> Items => _items ?? NoItems;

    // The members of an object, sorted by name (ordinal). Empty for any other kind.
    public IReadOnlyList<KeyValuePair<string, JsonTreeNode>> Members => _members ?? NoMembers;

    public static JsonTreeNode FromBoolean(bool value)
    {
        return value ? True : False;
    }

    public static JsonTreeNode FromString(string value)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        int bad = CanonicalJson.FindLoneSurrogate(value);
        if (bad >= 0)
        {
            throw new CanonicalJsonException("A string holds a lone surrogate, which is not valid Unicode text.");
        }

        return new JsonTreeNode(JsonTreeKind.String, value, null, null, 1);
    }

    public static JsonTreeNode FromInt64(long value)
    {
        return FromNumberText(value.ToString(CultureInfo.InvariantCulture));
    }

    // Decimal text is exact ("1.50" becomes 1.5), so the hash never depends on float formatting.
    public static JsonTreeNode FromDecimal(decimal value)
    {
        return FromNumberText(value.ToString(CultureInfo.InvariantCulture));
    }

    // A number from JSON number text, such as "12", "-0.5" or "1e3". Throws for text that is not one JSON number.
    public static JsonTreeNode FromNumberText(string text)
    {
        if (text == null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        if (!CanonicalNumber.TryCanonicalize(text, out string canonical, out string error))
        {
            throw new CanonicalJsonException(error);
        }

        return new JsonTreeNode(JsonTreeKind.Number, canonical, null, null, 1);
    }

    // A UTC date-time as an ISO 8601 string (CanonicalJson.FormatUtc).
    public static JsonTreeNode FromUtcDateTime(DateTime value)
    {
        return FromString(CanonicalJson.FormatUtc(value));
    }

    public static JsonTreeNode CreateArray(IEnumerable<JsonTreeNode> items)
    {
        if (items == null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        JsonTreeNode[] copy = items.ToArray();
        int depth = 1;
        foreach (JsonTreeNode item in copy)
        {
            if (item == null)
            {
                throw new ArgumentException("An array has no null entries. Use JsonTreeNode.Null.", nameof(items));
            }

            depth = Math.Max(depth, item.Depth + 1);
        }

        CheckDepth(depth);
        return new JsonTreeNode(JsonTreeKind.Array, null, new ReadOnlyCollection<JsonTreeNode>(copy), null, depth);
    }

    public static JsonTreeNode CreateArray(params JsonTreeNode[] items)
    {
        return CreateArray((IEnumerable<JsonTreeNode>)items);
    }

    // Members are sorted by name (ordinal). A repeated name is refused, because canonical JSON needs one value per name.
    public static JsonTreeNode CreateObject(IEnumerable<KeyValuePair<string, JsonTreeNode>> members)
    {
        if (members == null)
        {
            throw new ArgumentNullException(nameof(members));
        }

        KeyValuePair<string, JsonTreeNode>[] sorted = members.ToArray();
        int depth = 1;
        foreach (KeyValuePair<string, JsonTreeNode> member in sorted)
        {
            if (member.Key == null)
            {
                throw new ArgumentException("A member name is never null.", nameof(members));
            }

            if (member.Value == null)
            {
                throw new ArgumentException("A member value is never null. Use JsonTreeNode.Null.", nameof(members));
            }

            if (CanonicalJson.FindLoneSurrogate(member.Key) >= 0)
            {
                throw new CanonicalJsonException("A member name holds a lone surrogate, which is not valid Unicode text.");
            }

            depth = Math.Max(depth, member.Value.Depth + 1);
        }

        CheckDepth(depth);

        // Array.Sort is not stable, but names are unique after the check below, so order is fully defined.
        Array.Sort(sorted, (a, b) => string.CompareOrdinal(a.Key, b.Key));
        for (int i = 1; i < sorted.Length; i++)
        {
            if (string.Equals(sorted[i - 1].Key, sorted[i].Key, StringComparison.Ordinal))
            {
                throw new CanonicalJsonException("An object repeats the member name \"" + sorted[i].Key + "\".");
            }
        }

        return new JsonTreeNode(JsonTreeKind.Object, null, null, new ReadOnlyCollection<KeyValuePair<string, JsonTreeNode>>(sorted), depth);
    }

    public static JsonTreeNode CreateObject(params KeyValuePair<string, JsonTreeNode>[] members)
    {
        return CreateObject((IEnumerable<KeyValuePair<string, JsonTreeNode>>)members);
    }

    // The value of a string node.
    public string GetString()
    {
        if (Kind != JsonTreeKind.String)
        {
            throw new InvalidOperationException("The node is not a string.");
        }

        return _scalar!;
    }

    // The canonical text of a number node, such as "1.5" or "1e+21".
    public string GetNumberText()
    {
        if (Kind != JsonTreeKind.Number)
        {
            throw new InvalidOperationException("The node is not a number.");
        }

        return _scalar!;
    }

    public bool GetBoolean()
    {
        if (Kind == JsonTreeKind.True)
        {
            return true;
        }

        if (Kind == JsonTreeKind.False)
        {
            return false;
        }

        throw new InvalidOperationException("The node is not a boolean.");
    }

    // True when the node is a whole number that fits in a long.
    public bool TryGetInt64(out long value)
    {
        value = 0;
        if (Kind != JsonTreeKind.Number)
        {
            return false;
        }

        // Canonical text of a whole number up to 21 digits has no point and no exponent.
        string text = _scalar!;
        if (text.IndexOf('.') >= 0 || text.IndexOf('e') >= 0)
        {
            return false;
        }

        return long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    // True when the number fits in a decimal without losing digits.
    public bool TryGetDecimal(out decimal value)
    {
        value = 0;
        if (Kind != JsonTreeKind.Number)
        {
            return false;
        }

        if (!decimal.TryParse(_scalar, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal parsed))
        {
            return false;
        }

        // Decimal parsing rounds beyond 28 or 29 digits. Accept only an exact round trip.
        if (!string.Equals(FromDecimal(parsed).GetNumberText(), _scalar, StringComparison.Ordinal))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    // Finds an object member by name (ordinal). False for a missing name or a node that is not an object.
    public bool TryGetMember(string name, [NotNullWhen(true)] out JsonTreeNode? value)
    {
        value = null;
        if (_members == null || name == null)
        {
            return false;
        }

        int low = 0;
        int high = _members.Count - 1;
        while (low <= high)
        {
            int mid = low + ((high - low) / 2);
            int order = string.CompareOrdinal(_members[mid].Key, name);
            if (order == 0)
            {
                value = _members[mid].Value;
                return true;
            }

            if (order < 0)
            {
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return false;
    }

    public bool Equals(JsonTreeNode? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other != null
            && Kind == other.Kind
            && string.Equals(ToString(), other.ToString(), StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as JsonTreeNode);
    }

    public override int GetHashCode()
    {
        return StringComparer.Ordinal.GetHashCode(ToString());
    }

    // The canonical text with array order kept (CanonicalProfile.Request).
    public override string ToString()
    {
        // A race here only computes the same string twice.
        return _canonical ??= CanonicalJson.Write(this, CanonicalProfile.Request);
    }

    private static void CheckDepth(int depth)
    {
        if (depth > CanonicalJson.MaxDepth)
        {
            throw new CanonicalJsonException("The JSON value nests deeper than " + CanonicalJson.MaxDepth.ToString(CultureInfo.InvariantCulture) + " levels.");
        }
    }

    internal static JsonTreeNode StringUnchecked(string value)
    {
        return new JsonTreeNode(JsonTreeKind.String, value, null, null, 1);
    }

    internal static JsonTreeNode NumberUnchecked(string canonical)
    {
        return new JsonTreeNode(JsonTreeKind.Number, canonical, null, null, 1);
    }
}
