using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Product.Engine.Core.Canonical;

namespace Product.Mcp.Core.Contracts;

// One tool of the catalog (mcp/contracts/tools.json; document 24, section 6.1).
public sealed class ToolContract
{
    internal ToolContract(
        int number,
        string name,
        string toolsetKey,
        bool isWrite,
        string release,
        string description,
        IList<string> aiWriteStates,
        IList<string> defaultRoleKeys,
        IList<string> relations,
        JsonTreeNode annotations,
        string inputSchemaId,
        string outputSchemaId,
        IList<string> untrustedPaths,
        IList<string>? entries = null)
    {
        Number = number;
        Name = name;
        ToolsetKey = toolsetKey;
        IsWrite = isWrite;
        Release = release;
        Description = description;
        AiWriteStates = new ReadOnlyCollection<string>(aiWriteStates);
        DefaultRoleKeys = new ReadOnlyCollection<string>(defaultRoleKeys);
        Relations = new ReadOnlyCollection<string>(relations);
        Annotations = annotations;
        InputSchemaId = inputSchemaId;
        OutputSchemaId = outputSchemaId;
        UntrustedPaths = new ReadOnlyCollection<string>(untrustedPaths);
        Entries = new ReadOnlyCollection<string>(entries == null || entries.Count == 0 ? new[] { AiClientEntry, PersonEntry } : entries.ToArray());
    }

    // The entry values of tools.json: the MCP endpoint (and the REST API for an AI client), and the product's web app.
    public const string AiClientEntry = "ai-client";

    public const string PersonEntry = "person";

    // The stable tool number of document 24.
    public int Number { get; }

    // The tool name, lowercase snake_case, verb first.
    public string Name { get; }

    public string ToolsetKey { get; }

    // True for a write tool (access "write").
    public bool IsWrite { get; }

    // "R1" or "Later". Only R1 tools are served.
    public string Release { get; }

    public bool IsR1 => string.Equals(Release, "R1", StringComparison.Ordinal);

    // The shipped description: the input schema's description. A Tool Policy row may override it.
    public string Description { get; }

    // The AI write states the tool can leave a record in (section 6.0): the contract's cap.
    public IReadOnlyList<string> AiWriteStates { get; }

    // The default roles of tools.json, before Tool Policy.
    public IReadOnlyList<string> DefaultRoleKeys { get; }

    // The default relations, such as anyone-signed-in or owner-of-application.
    public IReadOnlyList<string> Relations { get; }

    public bool AnyoneSignedIn => Relations.Contains("anyone-signed-in");

    // The MCP tool annotations (readOnlyHint, destructiveHint, idempotentHint). Hints only.
    public JsonTreeNode Annotations { get; }

    // The schema IDs in the contract set, such as "tools/whoami.input.schema.json".
    public string InputSchemaId { get; }

    public string OutputSchemaId { get; }

    // The data paths the output schema marks x-untrusted, such as "items[].purpose".
    public IReadOnlyList<string> UntrustedPaths { get; }

    // The entries that serve the tool (tools.json entries; both when absent).
    public IReadOnlyList<string> Entries { get; }

    // True for a tool served only to a person in the product's web app, never to an AI client on any entry (D-38:
    // answer_approval). AI never approves.
    public bool IsPersonOnly => !Entries.Contains(AiClientEntry);

    // True when the entry serves the tool.
    public bool IsServedOn(Product.Engine.Runtime.Callers.CallerEntry entry)
    {
        return entry == Product.Engine.Runtime.Callers.CallerEntry.Person ? Entries.Contains(PersonEntry) : entry == Product.Engine.Runtime.Callers.CallerEntry.AiClient && Entries.Contains(AiClientEntry);
    }

    public override string ToString()
    {
        return Number + " " + Name;
    }
}

// One R1 resource (mcp/contracts/resources.json; document 24, section 6.12).
public sealed class ResourceContract
{
    internal ResourceContract(string name, string uriTemplate, string title, string description, string mimeType, string contentSchemaId)
    {
        Name = name;
        UriTemplate = uriTemplate;
        Title = title;
        Description = description;
        MimeType = mimeType;
        ContentSchemaId = contentSchemaId;
    }

    public string Name { get; }

    public string UriTemplate { get; }

    public string Title { get; }

    public string Description { get; }

    public string MimeType { get; }

    public string ContentSchemaId { get; }

    // True when the template has no variable part, so it is a concrete resource (configuration://in-force).
    public bool IsConcrete => UriTemplate.IndexOf('{') < 0;
}

// One toolset of section 2.1.
public sealed class ToolsetInfo
{
    internal ToolsetInfo(string key, string label)
    {
        Key = key;
        Label = label;
    }

    public string Key { get; }

    public string Label { get; }
}
