using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Product.Engine.Configuration.Json;
using Product.Engine.Core.Canonical;

namespace Product.Engine.Configuration.Schema;

// A set of JSON Schemas (draft 2020-12) that refer to each other by $id.
//
// It checks the keywords the product's schemas use: type, enum, const, the string, number, array and object
// assertions, $ref, allOf, anyOf, oneOf, not, if, then, else, unevaluatedProperties and the formats date,
// date-time, uri, uuid and regex. An unknown keyword or format makes Create throw, so a schema never checks
// less than it says (document 21, rule 8: broken configuration fails loudly).
//
// The set is immutable after Create. Every $ref and pattern is resolved and compiled up front,
// so one set can serve many threads (document 25, section 6.3: the plug-in instance is shared).
public sealed class JsonSchemaSet
{
    private const int MaxDepth = 256;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private static readonly HashSet<string> SingleSchemaKeywords = new(StringComparer.Ordinal)
    {
        "additionalProperties", "unevaluatedProperties", "items", "not", "if", "then", "else", "propertyNames",
    };

    private static readonly HashSet<string> SchemaListKeywords = new(StringComparer.Ordinal) { "allOf", "anyOf", "oneOf" };

    private static readonly HashSet<string> SchemaMapKeywords = new(StringComparer.Ordinal) { "properties", "patternProperties", "$defs" };

    private static readonly HashSet<string> AssertionKeywords = new(StringComparer.Ordinal)
    {
        "type", "enum", "const", "required", "minLength", "maxLength", "pattern", "format",
        "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "multipleOf",
        "minItems", "maxItems", "uniqueItems", "minProperties", "maxProperties", "dependentRequired",
    };

    private static readonly HashSet<string> PlainKeywords = new(StringComparer.Ordinal)
    {
        "$schema", "$id", "$ref", "$comment", "title", "description", "default", "examples", "deprecated", "readOnly", "writeOnly",
    };

    private static readonly HashSet<string> KnownFormats = new(StringComparer.Ordinal) { "date", "date-time", "uri", "uuid", "regex" };

    private static readonly HashSet<string> KnownTypes = new(StringComparer.Ordinal)
    {
        "null", "boolean", "object", "array", "number", "string", "integer",
    };

    private static readonly Regex DateShape = new(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}\z", RegexOptions.CultureInvariant);

    private static readonly Regex DateTimeShape = new(
        @"^([0-9]{4})-([0-9]{2})-([0-9]{2})[Tt]([0-9]{2}):([0-9]{2}):([0-9]{2})(\.[0-9]+)?([Zz]|([+-])([0-9]{2}):([0-9]{2}))\z",
        RegexOptions.CultureInvariant);

    private static readonly Regex UuidShape = new(
        @"^(?:urn:uuid:)?[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}\z", RegexOptions.CultureInvariant);

    private static readonly Regex UriScheme = new(@"^[A-Za-z][A-Za-z0-9+.-]*:[^\s]*\z", RegexOptions.CultureInvariant);

    private readonly Dictionary<string, JsonTreeNode> _documents;
    private readonly Dictionary<string, Target> _refs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Regex> _patterns = new(StringComparer.Ordinal);

    private JsonSchemaSet(Dictionary<string, JsonTreeNode> documents)
    {
        _documents = documents;
    }

    // The $id values in the set, such as "config-bundle.schema.json".
    public IEnumerable<string> DocumentIds => _documents.Keys.OrderBy(id => id, StringComparer.Ordinal);

    // Builds a set. Each document needs a unique string $id. Throws when a schema uses an unknown keyword,
    // an unknown format, a $ref that does not resolve, or a pattern that does not compile.
    public static JsonSchemaSet Create(IEnumerable<JsonTreeNode> documents)
    {
        if (documents == null)
        {
            throw new ArgumentNullException(nameof(documents));
        }

        var byId = new Dictionary<string, JsonTreeNode>(StringComparer.Ordinal);
        foreach (JsonTreeNode document in documents)
        {
            string? id = JsonNodes.GetString(document, "$id");
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("Every schema in a set needs a string $id.", nameof(documents));
            }

            if (byId.ContainsKey(id!))
            {
                throw new ArgumentException("Two schemas share the $id " + id + ".", nameof(documents));
            }

            byId.Add(id!, document);
        }

        var set = new JsonSchemaSet(byId);
        foreach (KeyValuePair<string, JsonTreeNode> pair in byId.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            set.Prepare(pair.Value, pair.Key, pair.Key + "#", isRoot: true);
        }

        return set;
    }

    // True when the URI (a $id with an optional "#" pointer) names a schema in this set.
    public bool Contains(string schemaUri)
    {
        return TryResolve(null, schemaUri, out _);
    }

    // Checks an instance against the schema the URI names, such as "config-bundle.schema.json#/$defs/draftPatch".
    public SchemaResult Evaluate(JsonTreeNode instance, string schemaUri)
    {
        if (instance == null)
        {
            throw new ArgumentNullException(nameof(instance));
        }

        if (!TryResolve(null, schemaUri, out Target target))
        {
            throw new ArgumentException("The schema " + schemaUri + " is not in this set.", nameof(schemaUri));
        }

        var evaluator = new Evaluator(this);
        Outcome outcome = evaluator.Eval(target.Node, target.DocumentId, target.Location, instance, JsonPointer.Root);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var annotations = new List<SchemaAnnotation>();
        foreach (SchemaAnnotation annotation in outcome.Annotations ?? Enumerable.Empty<SchemaAnnotation>())
        {
            if (seen.Add(annotation.InstancePath + "\u0000" + annotation.Keyword + "\u0000" + annotation.Value))
            {
                annotations.Add(annotation);
            }
        }

        var issueKeys = new HashSet<string>(StringComparer.Ordinal);
        var issues = new List<SchemaIssue>();
        foreach (SchemaIssue issue in outcome.Issues ?? Enumerable.Empty<SchemaIssue>())
        {
            if (issueKeys.Add(issue.InstancePath + "\u0000" + issue.Message))
            {
                issues.Add(issue);
            }
        }

        return new SchemaResult(issues, annotations);
    }

    // Turns an ECMA-262 pattern into a .NET pattern with the same meaning for the shapes the schemas use.
    // In ECMA-262 "$" matches only at the end of the text. In .NET it also matches before a final newline,
    // so an unescaped "$" outside a character class becomes "\z".
    internal static string ToDotNetPattern(string pattern)
    {
        var sb = new StringBuilder(pattern.Length + 4);
        bool inClass = false;
        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            if (c == '\\' && i + 1 < pattern.Length)
            {
                sb.Append(c).Append(pattern[i + 1]);
                i++;
                continue;
            }

            if (inClass)
            {
                inClass = c != ']';
                sb.Append(c);
                continue;
            }

            if (c == '[')
            {
                inClass = true;
                sb.Append(c);
            }
            else if (c == '$')
            {
                sb.Append("\\z");
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    // True when the text compiles as a pattern. Used by the regex format.
    internal static bool IsValidPattern(string pattern)
    {
        try
        {
            _ = new Regex(ToDotNetPattern(pattern), RegexOptions.CultureInvariant, RegexTimeout);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private void Prepare(JsonTreeNode schema, string documentId, string location, bool isRoot)
    {
        if (schema.Kind == JsonTreeKind.True || schema.Kind == JsonTreeKind.False)
        {
            return;
        }

        if (schema.Kind != JsonTreeKind.Object)
        {
            throw new InvalidOperationException("The schema at " + location + " is not an object or a boolean.");
        }

        foreach (KeyValuePair<string, JsonTreeNode> member in schema.Members)
        {
            string name = member.Key;
            JsonTreeNode value = member.Value;
            string here = location + "/" + name;

            if (name.StartsWith("x-", StringComparison.Ordinal))
            {
                continue;
            }

            if (PlainKeywords.Contains(name))
            {
                if (name == "$id" && !isRoot)
                {
                    throw new InvalidOperationException("A nested $id is not supported (" + here + ").");
                }

                if (name == "$ref")
                {
                    if (value.Kind != JsonTreeKind.String)
                    {
                        throw new InvalidOperationException("$ref must be a string (" + here + ").");
                    }

                    if (!TryResolve(documentId, value.GetString(), out Target target))
                    {
                        throw new InvalidOperationException("The $ref " + value.GetString() + " does not resolve (" + here + ").");
                    }

                    _refs[documentId + "|" + value.GetString()] = target;
                }

                continue;
            }

            if (SingleSchemaKeywords.Contains(name))
            {
                Prepare(value, documentId, here, isRoot: false);
            }
            else if (SchemaListKeywords.Contains(name))
            {
                if (value.Kind != JsonTreeKind.Array || value.Items.Count == 0)
                {
                    throw new InvalidOperationException(name + " must be a non-empty array of schemas (" + here + ").");
                }

                for (int i = 0; i < value.Items.Count; i++)
                {
                    Prepare(value.Items[i], documentId, here + "/" + i.ToString(CultureInfo.InvariantCulture), isRoot: false);
                }
            }
            else if (SchemaMapKeywords.Contains(name))
            {
                if (value.Kind != JsonTreeKind.Object)
                {
                    throw new InvalidOperationException(name + " must be an object of schemas (" + here + ").");
                }

                foreach (KeyValuePair<string, JsonTreeNode> entry in value.Members)
                {
                    if (name == "patternProperties")
                    {
                        CompilePattern(entry.Key, here);
                    }

                    Prepare(entry.Value, documentId, JsonPointer.Append(here, entry.Key), isRoot: false);
                }
            }
            else if (AssertionKeywords.Contains(name))
            {
                CheckAssertion(name, value, here);
            }
            else
            {
                throw new InvalidOperationException("The keyword " + name + " is not supported (" + here + ").");
            }
        }
    }

    private void CheckAssertion(string name, JsonTreeNode value, string location)
    {
        switch (name)
        {
            case "pattern":
                if (value.Kind != JsonTreeKind.String)
                {
                    throw new InvalidOperationException("pattern must be a string (" + location + ").");
                }

                CompilePattern(value.GetString(), location);
                break;
            case "format":
                if (value.Kind != JsonTreeKind.String || !KnownFormats.Contains(value.GetString()))
                {
                    throw new InvalidOperationException("The format " + value + " is not supported (" + location + ").");
                }

                break;
            case "type":
                IEnumerable<JsonTreeNode> names = value.Kind == JsonTreeKind.Array ? value.Items : new[] { value };
                foreach (JsonTreeNode typeName in names)
                {
                    if (typeName.Kind != JsonTreeKind.String || !KnownTypes.Contains(typeName.GetString()))
                    {
                        throw new InvalidOperationException("The type " + typeName + " is not known (" + location + ").");
                    }
                }

                break;
            case "required":
            case "enum":
                if (value.Kind != JsonTreeKind.Array)
                {
                    throw new InvalidOperationException(name + " must be an array (" + location + ").");
                }

                break;
            case "dependentRequired":
                if (value.Kind != JsonTreeKind.Object)
                {
                    throw new InvalidOperationException("dependentRequired must be an object (" + location + ").");
                }

                break;
            case "const":
                break;
            default:
                if (value.Kind != JsonTreeKind.Number && value.Kind != JsonTreeKind.True && value.Kind != JsonTreeKind.False)
                {
                    throw new InvalidOperationException(name + " must be a number or a boolean (" + location + ").");
                }

                break;
        }
    }

    private void CompilePattern(string pattern, string location)
    {
        if (_patterns.ContainsKey(pattern))
        {
            return;
        }

        try
        {
            _patterns.Add(pattern, new Regex(ToDotNetPattern(pattern), RegexOptions.CultureInvariant, RegexTimeout));
        }
        catch (ArgumentException e)
        {
            throw new InvalidOperationException("The pattern " + pattern + " does not compile (" + location + "): " + e.Message, e);
        }
    }

    private bool TryResolve(string? baseDocumentId, string reference, out Target target)
    {
        target = default;
        if (reference == null)
        {
            return false;
        }

        int hash = reference.IndexOf('#');
        string documentPart = hash < 0 ? reference : reference.Substring(0, hash);
        string pointer = hash < 0 ? string.Empty : reference.Substring(hash + 1);
        string? documentId = documentPart.Length == 0 ? baseDocumentId : documentPart;
        if (documentId == null || !_documents.TryGetValue(documentId, out JsonTreeNode? root))
        {
            return false;
        }

        if (pointer.Length > 0 && pointer[0] != '/')
        {
            return false;
        }

        if (!JsonPointer.TryResolve(root, pointer, out JsonTreeNode node))
        {
            return false;
        }

        target = new Target(node, documentId, documentId + "#" + pointer);
        return true;
    }

    private Target ResolvePrepared(string documentId, string reference)
    {
        if (_refs.TryGetValue(documentId + "|" + reference, out Target target))
        {
            return target;
        }

        throw new InvalidOperationException("The $ref " + reference + " was not prepared.");
    }

    private Regex PatternFor(string pattern)
    {
        if (_patterns.TryGetValue(pattern, out Regex? regex))
        {
            return regex;
        }

        throw new InvalidOperationException("The pattern " + pattern + " was not prepared.");
    }

    private readonly struct Target
    {
        public Target(JsonTreeNode node, string documentId, string location)
        {
            Node = node;
            DocumentId = documentId;
            Location = location;
        }

        public JsonTreeNode Node { get; }

        public string DocumentId { get; }

        public string Location { get; }
    }

    // What one schema said about one instance location.
    private sealed class Outcome
    {
        public bool Valid { get; private set; } = true;

        public List<SchemaIssue>? Issues { get; private set; }

        // Property names of the instance object that this schema evaluated (for unevaluatedProperties).
        public HashSet<string>? Evaluated { get; private set; }

        public List<SchemaAnnotation>? Annotations { get; private set; }

        public int IssueCount => Issues?.Count ?? 0;

        public void Fail(string instancePath, string message, string keywordPath)
        {
            Valid = false;
            (Issues ??= new List<SchemaIssue>()).Add(new SchemaIssue(instancePath, message, keywordPath));
        }

        public void MarkEvaluated(string name)
        {
            (Evaluated ??= new HashSet<string>(StringComparer.Ordinal)).Add(name);
        }

        public void Annotate(string instancePath, string keyword, JsonTreeNode value)
        {
            (Annotations ??= new List<SchemaAnnotation>()).Add(new SchemaAnnotation(instancePath, keyword, value));
        }

        public void MergeIssues(Outcome other)
        {
            if (other.Valid)
            {
                return;
            }

            Valid = false;
            if (other.Issues != null)
            {
                (Issues ??= new List<SchemaIssue>()).AddRange(other.Issues);
            }
        }

        public void MergeEvaluated(Outcome other)
        {
            if (other.Evaluated == null)
            {
                return;
            }

            foreach (string name in other.Evaluated)
            {
                MarkEvaluated(name);
            }
        }

        public void MergeAnnotations(Outcome other)
        {
            if (other.Annotations != null)
            {
                (Annotations ??= new List<SchemaAnnotation>()).AddRange(other.Annotations);
            }
        }

        public void MergeAll(Outcome other)
        {
            MergeIssues(other);
            MergeEvaluated(other);
            MergeAnnotations(other);
        }
    }

    // One evaluation run. Not shared between threads.
    private sealed class Evaluator
    {
        private readonly JsonSchemaSet _set;
        private int _depth;

        public Evaluator(JsonSchemaSet set)
        {
            _set = set;
        }

        public Outcome Eval(JsonTreeNode schema, string documentId, string location, JsonTreeNode instance, string path)
        {
            var outcome = new Outcome();
            if (schema.Kind == JsonTreeKind.True)
            {
                return outcome;
            }

            if (schema.Kind == JsonTreeKind.False)
            {
                outcome.Fail(path, "is not allowed here", location);
                return outcome;
            }

            if (++_depth > MaxDepth)
            {
                _depth--;
                outcome.Fail(path, "nests deeper than the schema checker allows", location);
                return outcome;
            }

            try
            {
                EvalObject(schema, documentId, location, instance, path, outcome);
            }
            finally
            {
                _depth--;
            }

            return outcome;
        }

        private void EvalObject(JsonTreeNode schema, string documentId, string location, JsonTreeNode instance, string path, Outcome outcome)
        {
            foreach (KeyValuePair<string, JsonTreeNode> member in schema.Members)
            {
                if (member.Key.StartsWith("x-", StringComparison.Ordinal))
                {
                    outcome.Annotate(path, member.Key, member.Value);
                }
            }

            if (schema.TryGetMember("$ref", out JsonTreeNode? reference))
            {
                Target target = _set.ResolvePrepared(documentId, reference.GetString());
                outcome.MergeAll(Eval(target.Node, target.DocumentId, target.Location, instance, path));
            }

            CheckType(schema, instance, path, location, outcome);
            CheckEnumAndConst(schema, instance, path, location, outcome);

            switch (instance.Kind)
            {
                case JsonTreeKind.String:
                    CheckString(schema, instance.GetString(), path, location, outcome);
                    break;
                case JsonTreeKind.Number:
                    CheckNumber(schema, instance, path, location, outcome);
                    break;
                case JsonTreeKind.Array:
                    CheckArray(schema, documentId, location, instance, path, outcome);
                    break;
                case JsonTreeKind.Object:
                    CheckObject(schema, documentId, location, instance, path, outcome);
                    break;
            }

            ApplyInPlace(schema, documentId, location, instance, path, outcome);

            if (instance.Kind == JsonTreeKind.Object && schema.TryGetMember("unevaluatedProperties", out JsonTreeNode? unevaluated))
            {
                foreach (KeyValuePair<string, JsonTreeNode> property in instance.Members)
                {
                    if (outcome.Evaluated != null && outcome.Evaluated.Contains(property.Key))
                    {
                        continue;
                    }

                    CheckLeftoverProperty(unevaluated, documentId, location + "/unevaluatedProperties", property, path, outcome);
                }
            }
        }

        private void CheckType(JsonTreeNode schema, JsonTreeNode instance, string path, string location, Outcome outcome)
        {
            if (!schema.TryGetMember("type", out JsonTreeNode? type))
            {
                return;
            }

            IEnumerable<JsonTreeNode> names = type.Kind == JsonTreeKind.Array ? type.Items : new[] { type };
            string[] list = names.Select(n => n.GetString()).ToArray();
            if (list.Any(name => HasType(instance, name)))
            {
                return;
            }

            outcome.Fail(path, "must be " + string.Join(" or ", list.Select(Article)) + ", not " + Article(Describe(instance)), location + "/type");
        }

        private static void CheckEnumAndConst(JsonTreeNode schema, JsonTreeNode instance, string path, string location, Outcome outcome)
        {
            if (schema.TryGetMember("enum", out JsonTreeNode? options) && !options.Items.Any(option => option.Equals(instance)))
            {
                string shown = string.Join(", ", options.Items.Take(10).Select(JsonPointer.Show));
                if (options.Items.Count > 10)
                {
                    shown += ", ...";
                }

                outcome.Fail(path, "must be one of " + shown + ", not " + JsonPointer.Show(instance), location + "/enum");
            }

            if (schema.TryGetMember("const", out JsonTreeNode? constant) && !constant.Equals(instance))
            {
                outcome.Fail(path, "must be " + JsonPointer.Show(constant) + ", not " + JsonPointer.Show(instance), location + "/const");
            }
        }

        private void CheckString(JsonTreeNode schema, string value, string path, string location, Outcome outcome)
        {
            int length = CodePointLength(value);
            if (TryGetInt(schema, "minLength", out long minLength) && length < minLength)
            {
                outcome.Fail(path, minLength == 1 ? "must not be empty" : "must be at least " + minLength + " characters long", location + "/minLength");
            }

            if (TryGetInt(schema, "maxLength", out long maxLength) && length > maxLength)
            {
                outcome.Fail(path, "must be at most " + maxLength + " characters long", location + "/maxLength");
            }

            if (schema.TryGetMember("pattern", out JsonTreeNode? pattern))
            {
                bool matches;
                try
                {
                    matches = _set.PatternFor(pattern.GetString()).IsMatch(value);
                }
                catch (RegexMatchTimeoutException)
                {
                    matches = false;
                }

                if (!matches)
                {
                    outcome.Fail(path, JsonPointer.Show(JsonTreeNode.FromString(value)) + " does not match the pattern " + pattern.GetString(), location + "/pattern");
                }
            }

            if (schema.TryGetMember("format", out JsonTreeNode? format) && !MatchesFormat(format.GetString(), value))
            {
                outcome.Fail(path, JsonPointer.Show(JsonTreeNode.FromString(value)) + " is not a valid " + format.GetString(), location + "/format");
            }
        }

        private static void CheckNumber(JsonTreeNode schema, JsonTreeNode value, string path, string location, Outcome outcome)
        {
            if (schema.TryGetMember("minimum", out JsonTreeNode? minimum) && CompareNumbers(value, minimum) < 0)
            {
                outcome.Fail(path, "must be at least " + minimum.GetNumberText(), location + "/minimum");
            }

            if (schema.TryGetMember("maximum", out JsonTreeNode? maximum) && CompareNumbers(value, maximum) > 0)
            {
                outcome.Fail(path, "must be at most " + maximum.GetNumberText(), location + "/maximum");
            }

            if (schema.TryGetMember("exclusiveMinimum", out JsonTreeNode? exclusiveMinimum) && CompareNumbers(value, exclusiveMinimum) <= 0)
            {
                outcome.Fail(path, "must be more than " + exclusiveMinimum.GetNumberText(), location + "/exclusiveMinimum");
            }

            if (schema.TryGetMember("exclusiveMaximum", out JsonTreeNode? exclusiveMaximum) && CompareNumbers(value, exclusiveMaximum) >= 0)
            {
                outcome.Fail(path, "must be less than " + exclusiveMaximum.GetNumberText(), location + "/exclusiveMaximum");
            }

            if (schema.TryGetMember("multipleOf", out JsonTreeNode? multipleOf)
                && value.TryGetDecimal(out decimal dividend)
                && multipleOf.TryGetDecimal(out decimal divisor)
                && divisor != 0
                && dividend % divisor != 0)
            {
                outcome.Fail(path, "must be a multiple of " + multipleOf.GetNumberText(), location + "/multipleOf");
            }
        }

        private void CheckArray(JsonTreeNode schema, string documentId, string location, JsonTreeNode instance, string path, Outcome outcome)
        {
            IReadOnlyList<JsonTreeNode> items = instance.Items;
            if (TryGetInt(schema, "minItems", out long minItems) && items.Count < minItems)
            {
                outcome.Fail(path, minItems == 1 ? "must have at least one item" : "must have at least " + minItems + " items", location + "/minItems");
            }

            if (TryGetInt(schema, "maxItems", out long maxItems) && items.Count > maxItems)
            {
                outcome.Fail(path, maxItems == 0 ? "must be empty" : "must have at most " + maxItems + " items", location + "/maxItems");
            }

            if (schema.TryGetMember("uniqueItems", out JsonTreeNode? unique) && unique.Kind == JsonTreeKind.True)
            {
                var seen = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < items.Count; i++)
                {
                    string text = items[i].ToString();
                    if (seen.TryGetValue(text, out int first))
                    {
                        outcome.Fail(path, "repeats an item: items " + first + " and " + i + " are both " + JsonPointer.Show(items[i]), location + "/uniqueItems");
                        break;
                    }

                    seen.Add(text, i);
                }
            }

            if (schema.TryGetMember("items", out JsonTreeNode? itemSchema))
            {
                for (int i = 0; i < items.Count; i++)
                {
                    Outcome child = Eval(itemSchema, documentId, location + "/items", items[i], JsonPointer.Append(path, i));
                    outcome.MergeIssues(child);
                    outcome.MergeAnnotations(child);
                }
            }
        }

        private void CheckObject(JsonTreeNode schema, string documentId, string location, JsonTreeNode instance, string path, Outcome outcome)
        {
            if (schema.TryGetMember("required", out JsonTreeNode? required))
            {
                foreach (JsonTreeNode name in required.Items)
                {
                    if (!instance.TryGetMember(name.GetString(), out _))
                    {
                        outcome.Fail(path, "is missing the required field \"" + name.GetString() + "\"", location + "/required");
                    }
                }
            }

            if (schema.TryGetMember("dependentRequired", out JsonTreeNode? dependent))
            {
                foreach (KeyValuePair<string, JsonTreeNode> rule in dependent.Members)
                {
                    if (!instance.TryGetMember(rule.Key, out _))
                    {
                        continue;
                    }

                    foreach (JsonTreeNode name in rule.Value.Items)
                    {
                        if (!instance.TryGetMember(name.GetString(), out _))
                        {
                            outcome.Fail(path, "has the field \"" + rule.Key + "\", so it also needs \"" + name.GetString() + "\"", location + "/dependentRequired");
                        }
                    }
                }
            }

            if (TryGetInt(schema, "minProperties", out long minProperties) && instance.Members.Count < minProperties)
            {
                outcome.Fail(path, "must have at least " + minProperties + " field(s)", location + "/minProperties");
            }

            if (TryGetInt(schema, "maxProperties", out long maxProperties) && instance.Members.Count > maxProperties)
            {
                outcome.Fail(path, "must have at most " + maxProperties + " field(s)", location + "/maxProperties");
            }

            if (schema.TryGetMember("propertyNames", out JsonTreeNode? propertyNames))
            {
                foreach (KeyValuePair<string, JsonTreeNode> property in instance.Members)
                {
                    Outcome nameOutcome = Eval(propertyNames, documentId, location + "/propertyNames", JsonTreeNode.FromString(property.Key), path);
                    if (!nameOutcome.Valid)
                    {
                        outcome.Fail(path, "has a field name that is not allowed: \"" + property.Key + "\"", location + "/propertyNames");
                    }
                }
            }

            schema.TryGetMember("properties", out JsonTreeNode? properties);
            schema.TryGetMember("patternProperties", out JsonTreeNode? patternProperties);
            schema.TryGetMember("additionalProperties", out JsonTreeNode? additional);

            foreach (KeyValuePair<string, JsonTreeNode> property in instance.Members)
            {
                bool matched = false;
                if (properties != null && properties.TryGetMember(property.Key, out JsonTreeNode? propertySchema))
                {
                    matched = true;
                    outcome.MarkEvaluated(property.Key);
                    string propertyLocation = JsonPointer.Append(location + "/properties", property.Key);
                    if (propertySchema.Kind == JsonTreeKind.False)
                    {
                        outcome.Fail(path, "has the field \"" + property.Key + "\", which is not allowed here", propertyLocation);
                    }
                    else
                    {
                        Outcome child = Eval(propertySchema, documentId, propertyLocation, property.Value, JsonPointer.Append(path, property.Key));
                        outcome.MergeIssues(child);
                        outcome.MergeAnnotations(child);
                    }
                }

                if (patternProperties != null)
                {
                    foreach (KeyValuePair<string, JsonTreeNode> entry in patternProperties.Members)
                    {
                        if (!_set.PatternFor(entry.Key).IsMatch(property.Key))
                        {
                            continue;
                        }

                        matched = true;
                        outcome.MarkEvaluated(property.Key);
                        Outcome child = Eval(entry.Value, documentId, JsonPointer.Append(location + "/patternProperties", entry.Key), property.Value, JsonPointer.Append(path, property.Key));
                        outcome.MergeIssues(child);
                        outcome.MergeAnnotations(child);
                    }
                }

                if (!matched && additional != null)
                {
                    outcome.MarkEvaluated(property.Key);
                    CheckLeftoverProperty(additional, documentId, location + "/additionalProperties", property, path, outcome);
                }
            }
        }

        // additionalProperties and unevaluatedProperties: a false schema means the field is unknown here.
        private void CheckLeftoverProperty(JsonTreeNode schema, string documentId, string location, KeyValuePair<string, JsonTreeNode> property, string path, Outcome outcome)
        {
            outcome.MarkEvaluated(property.Key);
            if (schema.Kind == JsonTreeKind.False)
            {
                outcome.Fail(path, "has an unknown field \"" + property.Key + "\"", location);
                return;
            }

            Outcome child = Eval(schema, documentId, location, property.Value, JsonPointer.Append(path, property.Key));
            outcome.MergeIssues(child);
            outcome.MergeAnnotations(child);
        }

        private void ApplyInPlace(JsonTreeNode schema, string documentId, string location, JsonTreeNode instance, string path, Outcome outcome)
        {
            if (schema.TryGetMember("allOf", out JsonTreeNode? allOf))
            {
                for (int i = 0; i < allOf.Items.Count; i++)
                {
                    outcome.MergeAll(Eval(allOf.Items[i], documentId, location + "/allOf/" + i.ToString(CultureInfo.InvariantCulture), instance, path));
                }
            }

            if (schema.TryGetMember("anyOf", out JsonTreeNode? anyOf))
            {
                ApplyChoice(anyOf, documentId, location + "/anyOf", instance, path, outcome, exactlyOne: false);
            }

            if (schema.TryGetMember("oneOf", out JsonTreeNode? oneOf))
            {
                ApplyChoice(oneOf, documentId, location + "/oneOf", instance, path, outcome, exactlyOne: true);
            }

            if (schema.TryGetMember("not", out JsonTreeNode? not))
            {
                Outcome negated = Eval(not, documentId, location + "/not", instance, path);
                if (negated.Valid)
                {
                    outcome.Fail(path, "matches a shape that is not allowed here", location + "/not");
                }
            }

            if (schema.TryGetMember("if", out JsonTreeNode? condition))
            {
                Outcome test = Eval(condition, documentId, location + "/if", instance, path);
                if (test.Valid)
                {
                    outcome.MergeEvaluated(test);
                    outcome.MergeAnnotations(test);
                    if (schema.TryGetMember("then", out JsonTreeNode? then))
                    {
                        outcome.MergeAll(Eval(then, documentId, location + "/then", instance, path));
                    }
                }
                else if (schema.TryGetMember("else", out JsonTreeNode? otherwise))
                {
                    outcome.MergeAll(Eval(otherwise, documentId, location + "/else", instance, path));
                }
            }
        }

        private void ApplyChoice(JsonTreeNode choices, string documentId, string location, JsonTreeNode instance, string path, Outcome outcome, bool exactlyOne)
        {
            var results = new List<Outcome>();
            for (int i = 0; i < choices.Items.Count; i++)
            {
                results.Add(Eval(choices.Items[i], documentId, location + "/" + i.ToString(CultureInfo.InvariantCulture), instance, path));
            }

            List<int> valid = Enumerable.Range(0, results.Count).Where(i => results[i].Valid).ToList();
            foreach (int i in valid)
            {
                outcome.MergeEvaluated(results[i]);
                outcome.MergeAnnotations(results[i]);
            }

            if (valid.Count == 0)
            {
                // Every choice failed. If each failed in one way at one place, say so in one line.
                // Otherwise show the choice that came closest, so the message stays short and useful.
                bool simple = results.All(r => r.IssueCount == 1)
                    && results.Select(r => r.Issues![0].InstancePath).Distinct(StringComparer.Ordinal).Count() == 1;
                if (simple)
                {
                    string where = results[0].Issues![0].InstancePath;
                    string text = string.Join("; or ", results.Select(r => r.Issues![0].Message));
                    outcome.Fail(where, "fits none of the allowed shapes: " + text, location);
                }
                else
                {
                    Outcome best = results.OrderBy(r => r.IssueCount).First();
                    outcome.MergeIssues(best);
                    outcome.MergeEvaluated(best);
                }

                return;
            }

            if (exactlyOne && valid.Count > 1)
            {
                // The usual case is a choice between fields, such as one approver or the other. Name them.
                string[] fields = valid.Select(i => RequiredOnly(choices.Items[i])).Where(f => f != null).Select(f => f!).ToArray();
                if (fields.Length == valid.Count)
                {
                    outcome.Fail(path, "sets " + string.Join(" and ", fields.Select(f => "\"" + f + "\"")) + ", and may set only one of them", location);
                }
                else
                {
                    outcome.Fail(path, "fits more than one of the allowed shapes (choices " + string.Join(" and ", valid.Select(i => (i + 1).ToString(CultureInfo.InvariantCulture))) + "), and must fit exactly one", location);
                }
            }
        }

        // The field name when a schema is only { "required": [ "<field>" ] }. Null otherwise.
        private static string? RequiredOnly(JsonTreeNode schema)
        {
            if (schema.Kind != JsonTreeKind.Object || schema.Members.Count != 1 || !schema.TryGetMember("required", out JsonTreeNode? required))
            {
                return null;
            }

            return required.Items.Count == 1 && required.Items[0].Kind == JsonTreeKind.String ? required.Items[0].GetString() : null;
        }

        private static bool HasType(JsonTreeNode instance, string type)
        {
            switch (type)
            {
                case "null":
                    return instance.Kind == JsonTreeKind.Null;
                case "boolean":
                    return instance.Kind == JsonTreeKind.True || instance.Kind == JsonTreeKind.False;
                case "object":
                    return instance.Kind == JsonTreeKind.Object;
                case "array":
                    return instance.Kind == JsonTreeKind.Array;
                case "number":
                    return instance.Kind == JsonTreeKind.Number;
                case "string":
                    return instance.Kind == JsonTreeKind.String;
                case "integer":
                    return IsInteger(instance);
                default:
                    return false;
            }
        }

        private static string Describe(JsonTreeNode instance)
        {
            switch (instance.Kind)
            {
                case JsonTreeKind.Null:
                    return "null";
                case JsonTreeKind.True:
                case JsonTreeKind.False:
                    return "boolean";
                case JsonTreeKind.Object:
                    return "object";
                case JsonTreeKind.Array:
                    return "array";
                case JsonTreeKind.String:
                    return "string";
                default:
                    return IsInteger(instance) ? "integer" : "number";
            }
        }

        private static string Article(string type)
        {
            if (type == "null")
            {
                return "null";
            }

            return (type == "object" || type == "array" || type == "integer" ? "an " : "a ") + type;
        }

        private bool MatchesFormat(string format, string value)
        {
            switch (format)
            {
                case "date":
                    return DateShape.IsMatch(value)
                        && DateTime.TryParseExact(value, "yyyy'-'MM'-'dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
                case "date-time":
                    return IsDateTime(value);
                case "uuid":
                    return UuidShape.IsMatch(value);
                case "uri":
                    return UriScheme.IsMatch(value) && Uri.TryCreate(value, UriKind.Absolute, out _);
                case "regex":
                    return IsValidPattern(value);
                default:
                    return false;
            }
        }

        private static bool IsDateTime(string value)
        {
            Match m = DateTimeShape.Match(value);
            if (!m.Success)
            {
                return false;
            }

            int year = Part(m, 1);
            int month = Part(m, 2);
            int day = Part(m, 3);
            int hour = Part(m, 4);
            int minute = Part(m, 5);
            int second = Part(m, 6);
            if (year < 1 || month < 1 || month > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
            {
                return false;
            }

            if (hour > 23 || minute > 59 || second > 60)
            {
                return false;
            }

            if (m.Groups[9].Success)
            {
                int offsetHour = Part(m, 10);
                int offsetMinute = Part(m, 11);
                if (offsetHour > 23 || offsetMinute > 59)
                {
                    return false;
                }
            }

            return true;
        }

        private static int Part(Match m, int group)
        {
            return int.Parse(m.Groups[group].Value, NumberStyles.None, CultureInfo.InvariantCulture);
        }
    }

    // A whole number in JSON terms: 1 and 1.0 are integers. Canonical number text has no point for a whole number.
    internal static bool IsInteger(JsonTreeNode node)
    {
        if (node.Kind != JsonTreeKind.Number)
        {
            return false;
        }

        string text = node.GetNumberText();
        return text.IndexOf('.') < 0 && text.IndexOf("e-", StringComparison.Ordinal) < 0;
    }

    private static int CompareNumbers(JsonTreeNode a, JsonTreeNode b)
    {
        if (a.TryGetDecimal(out decimal x) && b.TryGetDecimal(out decimal y))
        {
            return x.CompareTo(y);
        }

        double dx = double.Parse(a.GetNumberText(), NumberStyles.Float, CultureInfo.InvariantCulture);
        double dy = double.Parse(b.GetNumberText(), NumberStyles.Float, CultureInfo.InvariantCulture);
        return dx.CompareTo(dy);
    }

    private static bool TryGetInt(JsonTreeNode schema, string name, out long value)
    {
        value = 0;
        return schema.TryGetMember(name, out JsonTreeNode? node) && node.TryGetInt64(out value);
    }

    // JSON Schema counts characters as Unicode code points, so a surrogate pair counts once.
    private static int CodePointLength(string value)
    {
        int count = 0;
        for (int i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                i++;
            }

            count++;
        }

        return count;
    }
}
