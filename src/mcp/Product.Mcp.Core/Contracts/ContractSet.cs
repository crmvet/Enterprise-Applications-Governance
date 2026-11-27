using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Product.Engine.Configuration.Schema;
using Product.Engine.Core.Canonical;
using Product.Mcp.Core.Json;

namespace Product.Mcp.Core.Contracts;

// The MCP contracts (mcp/contracts/), loaded once: the tool catalog, the fixed error codes, the R1 resources, the Tool
// Policy seed rows and every input, output and resource schema, compiled into one schema set.
//
// The contract schemas have no $id and refer to each other by relative file paths. The loader gives each its path as
// $id and rewrites every $ref to that form, so the engine's schema checker resolves them (mcp/contracts/README.md:
// "The server inlines the refs into $defs before it publishes a tool schema").
public sealed class ContractSet
{
    private const string ResourcePrefix = ".Contracts/";

    private static readonly Lazy<ContractSet> LazyEmbedded = new(() => Load(ReadEmbedded(typeof(ContractSet).Assembly)));

    private readonly Dictionary<string, JsonTreeNode> _schemas;
    private readonly Dictionary<string, ToolContract> _toolsByName;
    private readonly Dictionary<string, JsonTreeNode> _published = new(StringComparer.Ordinal);
    private readonly object _publishGate = new();

    private ContractSet(
        Dictionary<string, JsonTreeNode> schemas,
        JsonSchemaSet schemaSet,
        IList<ToolContract> tools,
        IList<ToolsetInfo> toolsets,
        IList<ResourceContract> resources,
        IDictionary<string, ErrorCodeInfo> errors,
        IList<JsonTreeNode> seedPolicyRows,
        IList<string> humanRoleKeys)
    {
        _schemas = schemas;
        Schemas = schemaSet;
        Tools = new ReadOnlyCollection<ToolContract>(tools);
        _toolsByName = tools.ToDictionary(t => t.Name, StringComparer.Ordinal);
        Toolsets = new ReadOnlyCollection<ToolsetInfo>(toolsets);
        Resources = new ReadOnlyCollection<ResourceContract>(resources);
        Errors = new ReadOnlyDictionary<string, ErrorCodeInfo>(new Dictionary<string, ErrorCodeInfo>(errors, StringComparer.Ordinal));
        SeedPolicyRows = new ReadOnlyCollection<JsonTreeNode>(seedPolicyRows);
        HumanRoleKeys = new ReadOnlyCollection<string>(humanRoleKeys);
    }

    // The contracts embedded in this assembly at build time.
    public static ContractSet Embedded => LazyEmbedded.Value;

    // Every tool of the catalog, by number. Only R1 tools are served (IsR1).
    public IReadOnlyList<ToolContract> Tools { get; }

    public IReadOnlyList<ToolsetInfo> Toolsets { get; }

    public IReadOnlyList<ResourceContract> Resources { get; }

    public IReadOnlyDictionary<string, ErrorCodeInfo> Errors { get; }

    // The 52 Tool Policy seed rows (proposed C41) of tool-policy.seed.json.
    public IReadOnlyList<JsonTreeNode> SeedPolicyRows { get; }

    // The 14 human role keys of document 21's seed.
    public IReadOnlyList<string> HumanRoleKeys { get; }

    public JsonSchemaSet Schemas { get; }

    public bool TryGetTool(string? name, out ToolContract tool)
    {
        if (name != null && _toolsByName.TryGetValue(name, out ToolContract? found))
        {
            tool = found;
            return true;
        }

        tool = null!;
        return false;
    }

    public bool IsToolset(string key)
    {
        return Toolsets.Any(t => string.Equals(t.Key, key, StringComparison.Ordinal));
    }

    public SchemaResult ValidateInput(ToolContract tool, JsonTreeNode arguments)
    {
        return Schemas.Evaluate(arguments, tool.InputSchemaId);
    }

    // Checks a whole envelope against the tool's output schema (success with typed data, or failure).
    public SchemaResult ValidateOutput(ToolContract tool, JsonTreeNode envelope)
    {
        return Schemas.Evaluate(envelope, tool.OutputSchemaId);
    }

    public SchemaResult ValidateResource(ResourceContract resource, JsonTreeNode content)
    {
        return Schemas.Evaluate(content, resource.ContentSchemaId);
    }

    // The input schema as MCP clients see it: self-contained, with every referenced definition copied into $defs, and
    // without $comment notes, which are for the product's own authors.
    public JsonTreeNode PublishedInputSchema(ToolContract tool)
    {
        return Published(tool.InputSchemaId);
    }

    public JsonTreeNode PublishedOutputSchema(ToolContract tool)
    {
        return Published(tool.OutputSchemaId);
    }

    // Loads a contract set from (path, text) pairs, paths relative to mcp/contracts with forward slashes.
    public static ContractSet Load(IEnumerable<KeyValuePair<string, string>> files)
    {
        if (files == null)
        {
            throw new ArgumentNullException(nameof(files));
        }

        var documents = new Dictionary<string, JsonTreeNode>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> file in files)
        {
            documents[file.Key.Replace('\\', '/')] = CanonicalJson.Parse(file.Value);
        }

        JsonTreeNode toolsIndex = Require(documents, "tools.json");
        JsonTreeNode errorsIndex = Require(documents, "errors.json");
        JsonTreeNode resourcesIndex = Require(documents, "resources.json");
        JsonTreeNode seed = Require(documents, "tool-policy.seed.json");

        // The schema documents the server checks against. The meta schemas of the index files are for the JSON check only.
        var schemas = new Dictionary<string, JsonTreeNode>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, JsonTreeNode> document in documents.Where(d => IsServedSchema(d.Key)))
        {
            schemas[document.Key] = Normalize(document.Value, document.Key, isRoot: true);
        }

        JsonSchemaSet schemaSet = JsonSchemaSet.Create(schemas.Values);

        var tools = new List<ToolContract>();
        foreach (JsonTreeNode row in Js.Items(toolsIndex, "tools"))
        {
            string name = Js.Text(row, "name") ?? throw new InvalidOperationException("A tool in tools.json has no name.");
            string input = Js.Text(row, "inputSchema") ?? throw new InvalidOperationException("Tool " + name + " has no input schema.");
            string output = Js.Text(row, "outputSchema") ?? throw new InvalidOperationException("Tool " + name + " has no output schema.");
            if (!schemas.ContainsKey(input) || !schemas.ContainsKey(output))
            {
                throw new InvalidOperationException("Tool " + name + " names a schema that is not in the contract set.");
            }

            JsonTreeNode roles = Js.Member(row, "defaultRoles") ?? Js.EmptyObject;
            tools.Add(new ToolContract(
                (int)(Js.Number(row, "number") ?? 0),
                name,
                Js.Text(row, "toolset") ?? string.Empty,
                string.Equals(Js.Text(row, "access"), "write", StringComparison.Ordinal),
                Js.Text(row, "release") ?? "Later",
                Js.Text(schemas[input], "description") ?? Js.Text(row, "question") ?? name,
                Js.Strings(row, "aiWriteStates").ToList(),
                Js.Strings(roles, "roleKeys").ToList(),
                Js.Strings(roles, "relations").ToList(),
                Js.Member(row, "annotations") ?? Js.EmptyObject,
                input,
                output,
                UntrustedPaths(schemas, output),
                Js.Strings(row, "entries").ToList()));
        }

        var toolsets = Js.Items(toolsIndex, "toolsets").Select(t => new ToolsetInfo(Js.Text(t, "key")!, Js.Text(t, "label") ?? Js.Text(t, "key")!)).ToList();

        var errors = new Dictionary<string, ErrorCodeInfo>(StringComparer.Ordinal);
        foreach (JsonTreeNode code in Js.Items(errorsIndex, "codes"))
        {
            string key = Js.Text(code, "code")!;
            errors[key] = new ErrorCodeInfo(key, (int)(Js.Number(code, "httpStatus") ?? 500), Js.Flag(code, "retryable") ?? false, Js.Strings(code, "engineReasons").ToList());
        }

        if (errors.Count != McpErrorCodes.All.Count || McpErrorCodes.All.Any(c => !errors.ContainsKey(c)))
        {
            throw new InvalidOperationException("errors.json does not hold exactly the " + McpErrorCodes.All.Count + " fixed error codes of document 24, section 3.5.");
        }

        var resources = new List<ResourceContract>();
        foreach (JsonTreeNode resource in Js.Items(resourcesIndex, "resources"))
        {
            string schema = Js.Text(resource, "contentSchema")!;
            if (!schemas.ContainsKey(schema))
            {
                throw new InvalidOperationException("Resource " + Js.Text(resource, "name") + " names a schema that is not in the contract set.");
            }

            resources.Add(new ResourceContract(
                Js.Text(resource, "name")!,
                Js.Text(resource, "uriTemplate")!,
                Js.Text(resource, "title") ?? Js.Text(resource, "name")!,
                Js.Text(resource, "description") ?? string.Empty,
                Js.Text(resource, "mimeType") ?? "application/json",
                schema));
        }

        return new ContractSet(
            schemas,
            schemaSet,
            tools.OrderBy(t => t.Number).ToList(),
            toolsets,
            resources,
            errors,
            Js.Items(seed, "rows").ToList(),
            Js.Strings(toolsIndex, "humanRoleKeys").ToList());
    }

    // Reads every contract file embedded under ".Contracts/".
    public static IEnumerable<KeyValuePair<string, string>> ReadEmbedded(Assembly assembly)
    {
        if (assembly == null)
        {
            throw new ArgumentNullException(nameof(assembly));
        }

        foreach (string name in assembly.GetManifestResourceNames().OrderBy(n => n, StringComparer.Ordinal))
        {
            int at = name.IndexOf(ResourcePrefix, StringComparison.Ordinal);
            if (at < 0)
            {
                continue;
            }

            using Stream stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException("The contract " + name + " cannot be read.");
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
            yield return new KeyValuePair<string, string>(name.Substring(at + ResourcePrefix.Length), reader.ReadToEnd());
        }
    }

    private static bool IsServedSchema(string path)
    {
        return path.EndsWith(".schema.json", StringComparison.Ordinal)
            && (path == "common.schema.json" || path == "envelope.schema.json"
                || path.StartsWith("tools/", StringComparison.Ordinal)
                || path.StartsWith("resources/", StringComparison.Ordinal)
                || path.StartsWith("record-endpoint/", StringComparison.Ordinal));
    }

    private static JsonTreeNode Require(Dictionary<string, JsonTreeNode> documents, string path)
    {
        return documents.TryGetValue(path, out JsonTreeNode? document) ? document : throw new InvalidOperationException("The contract file " + path + " is missing.");
    }

    // Gives a schema its path as $id and rewrites each relative $ref to the target's path.
    private static JsonTreeNode Normalize(JsonTreeNode node, string documentId, bool isRoot)
    {
        switch (node.Kind)
        {
            case JsonTreeKind.Object:
                var members = new List<KeyValuePair<string, JsonTreeNode>>();
                foreach (KeyValuePair<string, JsonTreeNode> member in node.Members)
                {
                    if (member.Key == "$ref" && member.Value.Kind == JsonTreeKind.String)
                    {
                        members.Add(new KeyValuePair<string, JsonTreeNode>("$ref", JsonTreeNode.FromString(ResolveReference(documentId, member.Value.GetString()))));
                    }
                    else if (member.Key == "examples" || member.Key == "enum" || member.Key == "const" || member.Key == "default")
                    {
                        members.Add(member);
                    }
                    else if (member.Key != "$id")
                    {
                        members.Add(new KeyValuePair<string, JsonTreeNode>(member.Key, Normalize(member.Value, documentId, isRoot: false)));
                    }
                }

                if (isRoot)
                {
                    members.Add(new KeyValuePair<string, JsonTreeNode>("$id", JsonTreeNode.FromString(documentId)));
                }

                return JsonTreeNode.CreateObject(members);
            case JsonTreeKind.Array:
                return JsonTreeNode.CreateArray(node.Items.Select(i => Normalize(i, documentId, isRoot: false)));
            default:
                return node;
        }
    }

    // "../common.schema.json#/$defs/key" from "tools/x.schema.json" becomes "common.schema.json#/$defs/key".
    internal static string ResolveReference(string documentId, string reference)
    {
        int hash = reference.IndexOf('#');
        string documentPart = hash < 0 ? reference : reference.Substring(0, hash);
        string pointer = hash < 0 ? string.Empty : reference.Substring(hash);
        if (documentPart.Length == 0)
        {
            return reference;
        }

        var segments = new List<string>();
        int slash = documentId.LastIndexOf('/');
        if (slash > 0)
        {
            segments.AddRange(documentId.Substring(0, slash).Split('/'));
        }

        foreach (string part in documentPart.Split('/'))
        {
            if (part == "." || part.Length == 0)
            {
                continue;
            }

            if (part == "..")
            {
                if (segments.Count == 0)
                {
                    throw new InvalidOperationException("The $ref " + reference + " in " + documentId + " leaves the contract folder.");
                }

                segments.RemoveAt(segments.Count - 1);
            }
            else
            {
                segments.Add(part);
            }
        }

        return string.Join("/", segments) + pointer;
    }

    // The data paths an output schema marks x-untrusted (document 24, section 3.4: untrustedFields).
    private static List<string> UntrustedPaths(Dictionary<string, JsonTreeNode> schemas, string outputSchemaId)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        JsonTreeNode root = schemas[outputSchemaId];
        JsonTreeNode? data = Js.Member(Js.Member(root, "$defs"), "data");
        if (data != null)
        {
            WalkUntrusted(schemas, data, outputSchemaId, string.Empty, found, new HashSet<string>(StringComparer.Ordinal), 0);
        }

        return found.ToList();
    }

    private static void WalkUntrusted(Dictionary<string, JsonTreeNode> schemas, JsonTreeNode schema, string documentId, string path, SortedSet<string> found, HashSet<string> seen, int depth)
    {
        if (schema.Kind != JsonTreeKind.Object || depth > 40)
        {
            return;
        }

        if (Js.Flag(schema, "x-untrusted") == true && path.Length > 0)
        {
            found.Add(path);
        }

        string? reference = Js.Text(schema, "$ref");
        if (reference != null && seen.Add(reference + "|" + path) && TryResolvePointer(schemas, documentId, reference, out JsonTreeNode target, out string targetDocument))
        {
            WalkUntrusted(schemas, target, targetDocument, path, found, seen, depth + 1);
        }

        foreach (string list in new[] { "allOf", "anyOf", "oneOf" })
        {
            foreach (JsonTreeNode item in Js.Items(schema, list))
            {
                WalkUntrusted(schemas, item, documentId, path, found, seen, depth + 1);
            }
        }

        foreach (string branch in new[] { "then", "else" })
        {
            JsonTreeNode? sub = Js.Member(schema, branch);
            if (sub != null)
            {
                WalkUntrusted(schemas, sub, documentId, path, found, seen, depth + 1);
            }
        }

        JsonTreeNode? properties = Js.Member(schema, "properties");
        if (properties != null && properties.Kind == JsonTreeKind.Object)
        {
            foreach (KeyValuePair<string, JsonTreeNode> property in properties.Members)
            {
                WalkUntrusted(schemas, property.Value, documentId, path.Length == 0 ? property.Key : path + "." + property.Key, found, seen, depth + 1);
            }
        }

        JsonTreeNode? items = Js.Member(schema, "items");
        if (items != null)
        {
            WalkUntrusted(schemas, items, documentId, path + "[]", found, seen, depth + 1);
        }
    }

    private static bool TryResolvePointer(Dictionary<string, JsonTreeNode> schemas, string documentId, string reference, out JsonTreeNode target, out string targetDocument)
    {
        target = JsonTreeNode.Null;
        int hash = reference.IndexOf('#');
        targetDocument = hash <= 0 ? (hash == 0 ? documentId : reference) : reference.Substring(0, hash);
        if (!schemas.TryGetValue(targetDocument, out JsonTreeNode? node))
        {
            return false;
        }

        string pointer = hash < 0 ? string.Empty : reference.Substring(hash + 1);
        foreach (string raw in pointer.Split('/').Where(p => p.Length > 0))
        {
            string part = raw.Replace("~1", "/").Replace("~0", "~");
            JsonTreeNode? next = Js.Member(node, part);
            if (next == null)
            {
                return false;
            }

            node = next;
        }

        target = node;
        return true;
    }

    // ---- Publication: refs inlined into $defs ----

    private JsonTreeNode Published(string schemaId)
    {
        lock (_publishGate)
        {
            if (_published.TryGetValue(schemaId, out JsonTreeNode? done))
            {
                return done;
            }

            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            var pending = new Queue<string>();
            var definitions = new SortedDictionary<string, JsonTreeNode>(StringComparer.Ordinal);
            JsonTreeNode root = _schemas[schemaId];
            JsonTreeNode body = Inline(root, schemaId, schemaId, names, pending, isRoot: true);
            while (pending.Count > 0)
            {
                string reference = pending.Dequeue();
                if (!TryResolvePointer(_schemas, schemaId, reference, out JsonTreeNode target, out string targetDocument))
                {
                    throw new InvalidOperationException("The $ref " + reference + " does not resolve while publishing " + schemaId + ".");
                }

                definitions[names[reference]] = Inline(target, targetDocument, schemaId, names, pending, isRoot: false);
            }

            var rootDefs = (Js.Member(body, "$defs")?.Members ?? Array.Empty<KeyValuePair<string, JsonTreeNode>>()).ToDictionary(m => m.Key, m => m.Value, StringComparer.Ordinal);
            foreach (KeyValuePair<string, JsonTreeNode> definition in definitions)
            {
                rootDefs[definition.Key] = definition.Value;
            }

            JsonTreeNode published = rootDefs.Count == 0 ? Js.Without(body, "$defs") : Js.With(body, ("$defs", JsonTreeNode.CreateObject(rootDefs)));
            _published[schemaId] = published;
            return published;
        }
    }

    // Copies a schema node, pointing every $ref into the published document's $defs, and drops $id and $comment.
    private static JsonTreeNode Inline(JsonTreeNode node, string documentId, string rootId, Dictionary<string, string> names, Queue<string> pending, bool isRoot)
    {
        switch (node.Kind)
        {
            case JsonTreeKind.Object:
                var members = new List<KeyValuePair<string, JsonTreeNode>>();
                foreach (KeyValuePair<string, JsonTreeNode> member in node.Members)
                {
                    if (member.Key == "$id" || member.Key == "$comment" || (member.Key == "$schema" && !isRoot))
                    {
                        continue;
                    }

                    if (member.Key == "$ref" && member.Value.Kind == JsonTreeKind.String)
                    {
                        string reference = member.Value.GetString();
                        string absolute = reference.StartsWith("#", StringComparison.Ordinal) ? documentId + reference : reference;
                        if (absolute.StartsWith(rootId + "#/$defs/", StringComparison.Ordinal))
                        {
                            members.Add(new KeyValuePair<string, JsonTreeNode>("$ref", JsonTreeNode.FromString(absolute.Substring(rootId.Length))));
                            continue;
                        }

                        if (!names.TryGetValue(absolute, out string? name))
                        {
                            name = DefinitionName(absolute);
                            names[absolute] = name;
                            pending.Enqueue(absolute);
                        }

                        members.Add(new KeyValuePair<string, JsonTreeNode>("$ref", JsonTreeNode.FromString("#/$defs/" + name)));
                        continue;
                    }

                    bool data = member.Key == "examples" || member.Key == "enum" || member.Key == "const" || member.Key == "default";
                    members.Add(data ? member : new KeyValuePair<string, JsonTreeNode>(member.Key, Inline(member.Value, documentId, rootId, names, pending, isRoot: false)));
                }

                return JsonTreeNode.CreateObject(members);
            case JsonTreeKind.Array:
                return JsonTreeNode.CreateArray(node.Items.Select(i => Inline(i, documentId, rootId, names, pending, isRoot: false)));
            default:
                return node;
        }
    }

    // "common.schema.json#/$defs/key" becomes "common.key"; a root pointer becomes the file's stem.
    private static string DefinitionName(string absolute)
    {
        int hash = absolute.IndexOf('#');
        string document = hash < 0 ? absolute : absolute.Substring(0, hash);
        string pointer = hash < 0 ? string.Empty : absolute.Substring(hash + 1);
        string stem = document.Replace(".schema.json", string.Empty).Replace('/', '.');
        string tail = string.Join(".", pointer.Split('/').Where(p => p.Length > 0 && p != "$defs"));
        return tail.Length == 0 ? stem : stem + "." + tail;
    }
}
