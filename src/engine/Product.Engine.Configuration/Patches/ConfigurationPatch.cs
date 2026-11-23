using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Product.Engine.Configuration.Json;
using Product.Engine.Configuration.Schema;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;

namespace Product.Engine.Configuration.Patches;

// Where a patch comes from. It decides the schema V01 uses and whether touched rows become origin local.
public enum PatchKind
{
    // An AI draft, a configuration file, a revert or an upgrade draft (document 21, sections 4 and 5).
    Draft,

    // The client binding patch: role group bindings, workload grants and portfolio-scoped rows (document 21, section 6.8).
    // WORKING DEFAULT (open design question 12): workload roles reach workload identities only through Workload Grant
    // rows in this patch. People reach roles only through group bindings.
    ClientBinding,

    // A full preset written as changes to its parent (document 21, section 8.4).
    PresetExtends,

    // An overlay preset, applied after a full preset (document 21, section 6.7).
    Overlay,
}

// One upsert or retire operation by entity and key (document 21, section 8.2).
public sealed class ConfigurationOperation
{
    internal ConfigurationOperation(PatchOperation op, string entity, string key, JsonTreeNode? values, string pointer)
    {
        Op = op;
        Entity = entity;
        Key = key;
        Values = values;
        Pointer = pointer;
    }

    public PatchOperation Op { get; }

    // The entity key. A child entity (list-value, tier-answer-option, decision-outcome) takes a "parent/child" key.
    public string Entity { get; }

    // The key, or "parent/child" for a child entity.
    public string Key { get; }

    // The upsert values. Null for a retire.
    public JsonTreeNode? Values { get; }

    // Where the operation sits in its document, such as "/operations/2".
    public string Pointer { get; }
}

// An ordered list of operations (document 21, section 8.2). Later operations win over earlier ones.
public sealed class ConfigurationPatch
{
    private ConfigurationPatch(PatchKind kind, JsonTreeNode? document, string where, IList<ConfigurationOperation> operations, bool isInline)
    {
        Kind = kind;
        Document = document;
        Where = where;
        Operations = new ReadOnlyCollection<ConfigurationOperation>(operations);
        IsInline = isInline;
    }

    public PatchKind Kind { get; }

    // The whole patch document, or the bare operations array for inline operations.
    public JsonTreeNode? Document { get; }

    // A name for messages, such as a file name.
    public string Where { get; }

    public IReadOnlyList<ConfigurationOperation> Operations { get; }

    // True for a bare list of operations, such as the inline overrides of a fixture.
    public bool IsInline { get; }

    // The schema the whole document must match (V01).
    public string DocumentSchema
    {
        get
        {
            switch (Kind)
            {
                case PatchKind.ClientBinding:
                    return ConfigurationSchemas.ClientBindingPatch;
                case PatchKind.PresetExtends:
                case PatchKind.Overlay:
                    return ConfigurationSchemas.PresetPatch;
                default:
                    return ConfigurationSchemas.DraftPatch;
            }
        }
    }

    // The schema each inline operation must match (V01).
    public string OperationSchema => Kind == PatchKind.PresetExtends || Kind == PatchKind.Overlay
        ? ConfigurationSchemas.PresetOperation
        : ConfigurationSchemas.DraftOperation;

    // Reads a patch document: { "schemaVersion", "manifest", "operations": [...] }.
    // Reading is lenient: an operation without a known op, an entity or a key is left out, and V01 reports it.
    public static ConfigurationPatch FromDocument(JsonTreeNode document, PatchKind kind, string where)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        JsonTreeNode? operations = JsonNodes.Get(document, "operations");
        return new ConfigurationPatch(kind, document, where, ReadOperations(operations, JsonPointer.Append(JsonPointer.Root, "operations")), isInline: false);
    }

    // Reads a bare array of operations.
    public static ConfigurationPatch FromOperations(JsonTreeNode operations, PatchKind kind, string where)
    {
        if (operations == null)
        {
            throw new ArgumentNullException(nameof(operations));
        }

        return new ConfigurationPatch(kind, operations, where, ReadOperations(operations, JsonPointer.Root), isInline: true);
    }

    private static List<ConfigurationOperation> ReadOperations(JsonTreeNode? operations, string pointer)
    {
        var list = new List<ConfigurationOperation>();
        if (operations == null || operations.Kind != JsonTreeKind.Array)
        {
            return list;
        }

        for (int i = 0; i < operations.Items.Count; i++)
        {
            JsonTreeNode item = operations.Items[i];
            string? op = JsonNodes.GetString(item, "op");
            string? entity = JsonNodes.GetString(item, "entity");
            string? key = JsonNodes.GetString(item, "key");
            if (op == null || entity == null || key == null || !EngineContract.PatchOperations.TryParse(op, out PatchOperation parsed))
            {
                continue;
            }

            JsonTreeNode? values = JsonNodes.Get(item, "values");
            if (values != null && values.Kind != JsonTreeKind.Object)
            {
                values = null;
            }

            list.Add(new ConfigurationOperation(parsed, entity, key, parsed == PatchOperation.Retire ? null : values ?? JsonNodes.EmptyObject, JsonPointer.Append(pointer, i)));
        }

        return list;
    }
}
