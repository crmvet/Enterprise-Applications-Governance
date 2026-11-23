using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Core.Operations;

// Reads one JSON object of an operation's wire form (the contract layer's lowerCamelCase fields, document 25,
// section 5). Strict: an unknown member, a wrong type or a malformed key stops the operation with an input reason,
// so a typo in a caller's request never turns into a silent default. A JSON null counts as absent.
internal sealed class WireObject
{
    private static readonly string[] UtcFormats =
    {
        "yyyy-MM-dd'T'HH:mm:ss'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
    };

    private readonly JsonTreeNode _node;

    public WireObject(JsonTreeNode? node, string where, params string[] allowed)
    {
        if (node == null || node.Kind != JsonTreeKind.Object)
        {
            throw OperationStop.Input(ReasonCodes.InputMalformed, where + " must be a JSON object.");
        }

        foreach (KeyValuePair<string, JsonTreeNode> member in node.Members)
        {
            if (Array.IndexOf(allowed, member.Key) < 0)
            {
                throw OperationStop.Input(ReasonCodes.InputMalformed, where + " has an unknown field \"" + member.Key + "\".");
            }
        }

        _node = node;
        Where = where;
    }

    public string Where { get; }

    public bool Has(string name)
    {
        return Get(name) != null;
    }

    public string? OptionalString(string name)
    {
        JsonTreeNode? value = Get(name);
        if (value == null)
        {
            return null;
        }

        if (value.Kind != JsonTreeKind.String || value.GetString().Length == 0)
        {
            throw Malformed(name, "a non-empty string");
        }

        return value.GetString();
    }

    public string RequiredString(string name)
    {
        return OptionalString(name) ?? throw Missing(name);
    }

    public string? OptionalKey(string name)
    {
        string? value = OptionalString(name);
        if (value != null && !ConfigurationKey.IsValid(value))
        {
            throw Malformed(name, "a configuration key (lowercase letters, digits and hyphens)");
        }

        return value;
    }

    public string RequiredKey(string name)
    {
        return OptionalKey(name) ?? throw Missing(name);
    }

    // A list of keys, distinct, in the order given. Empty when absent, unless required.
    public IReadOnlyList<string> Keys(string name, bool required = false)
    {
        IReadOnlyList<string> values = Strings(name, required);
        foreach (string value in values)
        {
            if (!ConfigurationKey.IsValid(value))
            {
                throw Malformed(name, "a list of configuration keys");
            }
        }

        return values;
    }

    // A list of non-empty strings, distinct, in the order given. Empty when absent, unless required.
    public IReadOnlyList<string> Strings(string name, bool required = false)
    {
        JsonTreeNode? value = Get(name);
        if (value == null)
        {
            if (required)
            {
                throw Missing(name);
            }

            return Array.Empty<string>();
        }

        if (value.Kind != JsonTreeKind.Array || value.Items.Any(item => item.Kind != JsonTreeKind.String || item.GetString().Length == 0))
        {
            throw Malformed(name, "a list of non-empty strings");
        }

        return new ReadOnlyCollection<string>(value.Items.Select(item => item.GetString()).Distinct(StringComparer.Ordinal).ToList());
    }

    public bool? OptionalBool(string name)
    {
        JsonTreeNode? value = Get(name);
        if (value == null)
        {
            return null;
        }

        if (value.Kind != JsonTreeKind.True && value.Kind != JsonTreeKind.False)
        {
            throw Malformed(name, "true or false");
        }

        return value.Kind == JsonTreeKind.True;
    }

    public bool Bool(string name, bool defaultValue)
    {
        return OptionalBool(name) ?? defaultValue;
    }

    public bool RequiredBool(string name)
    {
        return OptionalBool(name) ?? throw Missing(name);
    }

    public int? OptionalInt(string name)
    {
        JsonTreeNode? value = Get(name);
        if (value == null)
        {
            return null;
        }

        if (!value.TryGetInt64(out long number) || number < int.MinValue || number > int.MaxValue)
        {
            throw Malformed(name, "a whole number");
        }

        return (int)number;
    }

    // A UTC date-time in ISO 8601 with a Z, such as 2026-11-22T09:00:00Z. Time zones are a display concern.
    public DateTime? OptionalUtc(string name)
    {
        string? text = OptionalString(name);
        if (text == null)
        {
            return null;
        }

        if (!DateTime.TryParseExact(text, UtcFormats, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime value))
        {
            throw Malformed(name, "a UTC date-time such as 2026-11-22T09:00:00Z");
        }

        return DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    public DateTime RequiredUtc(string name)
    {
        return OptionalUtc(name) ?? throw Missing(name);
    }

    // A wire value of an engine contract list. Absent gives the default, or stops when there is none.
    public T Contract<T>(string name, ContractList<T> list, T? defaultValue = null)
        where T : struct, Enum
    {
        string? text = OptionalString(name);
        if (text == null)
        {
            return defaultValue ?? throw Missing(name);
        }

        if (!list.TryParse(text, out T value))
        {
            throw OperationStop.Input(
                ReasonCodes.InputContractValueUnknown,
                Where + "." + name + " is \"" + text + "\", which is not one of " + string.Join(", ", list.WireValues) + ".");
        }

        return value;
    }

    public WireObject? OptionalObject(string name, params string[] allowed)
    {
        JsonTreeNode? value = Get(name);
        return value == null ? null : new WireObject(value, Where + "." + name, allowed);
    }

    // A JSON object whose members the operation reads by name, such as the after and before column maps of OP-08 or
    // another operation's whole payload passed on unchanged. Null when absent.
    public JsonTreeNode? OptionalRawObject(string name)
    {
        JsonTreeNode? value = Get(name);
        if (value != null && value.Kind != JsonTreeKind.Object)
        {
            throw Malformed(name, "a JSON object");
        }

        return value;
    }

    // A list of objects. Empty when absent, unless required.
    public IReadOnlyList<WireObject> Objects(string name, bool required, params string[] allowed)
    {
        JsonTreeNode? value = Get(name);
        if (value == null)
        {
            if (required)
            {
                throw Missing(name);
            }

            return Array.Empty<WireObject>();
        }

        if (value.Kind != JsonTreeKind.Array)
        {
            throw Malformed(name, "a list of objects");
        }

        var items = new List<WireObject>();
        for (int i = 0; i < value.Items.Count; i++)
        {
            items.Add(new WireObject(value.Items[i], Where + "." + name + "[" + i.ToString(CultureInfo.InvariantCulture) + "]", allowed));
        }

        return new ReadOnlyCollection<WireObject>(items);
    }

    // An object whose members are lists of strings, such as owner role key to principal IDs.
    public IReadOnlyDictionary<string, IReadOnlyList<string>> StringListMap(string name)
    {
        var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        JsonTreeNode? value = Get(name);
        if (value == null)
        {
            return map;
        }

        if (value.Kind != JsonTreeKind.Object)
        {
            throw Malformed(name, "an object of lists");
        }

        foreach (KeyValuePair<string, JsonTreeNode> member in value.Members)
        {
            JsonTreeNode list = member.Value;
            if (list.Kind != JsonTreeKind.Array || list.Items.Any(item => item.Kind != JsonTreeKind.String || item.GetString().Length == 0))
            {
                throw Malformed(name + "." + member.Key, "a list of non-empty strings");
            }

            map[member.Key] = list.Items.Select(item => item.GetString()).Distinct(StringComparer.Ordinal).ToArray();
        }

        return map;
    }

    private JsonTreeNode? Get(string name)
    {
        return _node.TryGetMember(name, out JsonTreeNode? value) && value.Kind != JsonTreeKind.Null ? value : null;
    }

    private OperationStop Missing(string name)
    {
        return OperationStop.Input(ReasonCodes.InputRequired, Where + "." + name + " is required.");
    }

    private OperationStop Malformed(string name, string expected)
    {
        return OperationStop.Input(ReasonCodes.InputMalformed, Where + "." + name + " must be " + expected + ".");
    }
}

// Builds one JSON object of an operation's wire form. Null values are left out, so optional fields are absent.
internal sealed class WireWriter
{
    private readonly List<KeyValuePair<string, JsonTreeNode>> _members = new();

    public WireWriter Add(string name, string? value)
    {
        if (value != null)
        {
            _members.Add(new KeyValuePair<string, JsonTreeNode>(name, JsonTreeNode.FromString(value)));
        }

        return this;
    }

    public WireWriter Add(string name, bool value)
    {
        _members.Add(new KeyValuePair<string, JsonTreeNode>(name, JsonTreeNode.FromBoolean(value)));
        return this;
    }

    public WireWriter Add(string name, int? value)
    {
        if (value.HasValue)
        {
            _members.Add(new KeyValuePair<string, JsonTreeNode>(name, JsonTreeNode.FromInt64(value.Value)));
        }

        return this;
    }

    public WireWriter Add(string name, DateTime? value)
    {
        if (value.HasValue)
        {
            _members.Add(new KeyValuePair<string, JsonTreeNode>(name, JsonTreeNode.FromUtcDateTime(value.Value)));
        }

        return this;
    }

    public WireWriter Add(string name, JsonTreeNode? value)
    {
        if (value != null)
        {
            _members.Add(new KeyValuePair<string, JsonTreeNode>(name, value));
        }

        return this;
    }

    // A list of strings. Always written, so an empty list says "none" rather than "unknown".
    public WireWriter Add(string name, IEnumerable<string> values)
    {
        _members.Add(new KeyValuePair<string, JsonTreeNode>(name, JsonTreeNode.CreateArray(values.Select(JsonTreeNode.FromString))));
        return this;
    }

    public JsonTreeNode Build()
    {
        return JsonTreeNode.CreateObject(_members);
    }

    public static JsonTreeNode Array(IEnumerable<JsonTreeNode> items)
    {
        return JsonTreeNode.CreateArray(items);
    }
}
