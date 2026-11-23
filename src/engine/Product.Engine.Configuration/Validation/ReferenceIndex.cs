using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Product.Engine.Configuration.Json;
using Product.Engine.Configuration.Model;
using Product.Engine.Configuration.Schema;
using Product.Engine.Core.Canonical;

namespace Product.Engine.Configuration.Validation;

// One key reference from a configuration row to another row (document 21, section 8.3).
public sealed class RowReference
{
    internal RowReference(ContentRow source, string pointer, string target, string value)
    {
        Source = source;
        Pointer = pointer;
        Target = target;
        Value = value;
        Field = pointer.Length > source.Pointer.Length ? pointer.Substring(source.Pointer.Length + 1) : string.Empty;

        // "list-value:channel" points at a list value under the value list "channel".
        int colon = target.IndexOf(':');
        TargetEntity = colon < 0 ? target : target.Substring(0, colon);
        TargetParentKey = colon < 0 ? null : target.Substring(colon + 1);
    }

    // The row that holds the reference.
    public ContentRow Source { get; }

    // JSON pointer of the value in the bundle.
    public string Pointer { get; }

    // The field inside the row, such as "tierKey" or "emergencyRule/approverRoleKeys/0".
    public string Field { get; }

    // The x-references value, such as "tier" or "list-value:channel".
    public string Target { get; }

    public string TargetEntity { get; }

    public string? TargetParentKey { get; }

    // The key the reference holds.
    public string Value { get; }

    // The key path of the row it points at.
    public string TargetKeyPath => TargetParentKey == null ? Value : TargetParentKey + "/" + Value;

    public bool IsWildcard => string.Equals(Value, "*", StringComparison.Ordinal);

    // False for references to things outside the set: presets, portfolios and decisions (rows of other tables).
    public bool IsConfigurationTarget => ConfigurationEntities.TryGet(TargetEntity, out _);

    // The field name for messages: the top-level field, such as "tierKey".
    public string FieldName
    {
        get
        {
            int slash = Field.IndexOf('/');
            return slash < 0 ? Field : Field.Substring(0, slash);
        }
    }
}

// Every key reference in a content, found from the schema's x-references annotations.
// The schema already says, field by field, which entity a key points at, so the validator reads it from there
// instead of keeping a second list. Setting values typed "key:<entity>" are added from their value type.
public sealed class ReferenceIndex
{
    private readonly Dictionary<string, List<RowReference>> _byTarget = new(StringComparer.Ordinal);

    private ReferenceIndex(IList<RowReference> references)
    {
        All = new ReadOnlyCollection<RowReference>(references);
        foreach (RowReference reference in references)
        {
            string target = reference.TargetEntity + "\u0000" + reference.TargetKeyPath;
            if (!_byTarget.TryGetValue(target, out List<RowReference>? list))
            {
                list = new List<RowReference>();
                _byTarget.Add(target, list);
            }

            list.Add(reference);
        }
    }

    public static ReferenceIndex Empty { get; } = new(new List<RowReference>());

    // In bundle order.
    public IReadOnlyList<RowReference> All { get; }

    // The references that point at one row.
    public IEnumerable<RowReference> To(string entity, string keyPath)
    {
        return _byTarget.TryGetValue(entity + "\u0000" + keyPath, out List<RowReference>? list) ? list : Enumerable.Empty<RowReference>();
    }

    public static ReferenceIndex Build(ConfigurationContent content, JsonTreeNode bundle, SchemaResult shape)
    {
        var references = new List<RowReference>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (SchemaAnnotation annotation in shape.Annotations)
        {
            if (annotation.Keyword != "x-references" || annotation.Value.Kind != JsonTreeKind.String)
            {
                continue;
            }

            AddAt(content, bundle, annotation.InstancePath, annotation.Value.GetString(), references, seen);
        }

        // Settings typed "key:<entity>" or "key:list-value:<list>" hold a key in their value.
        foreach (ContentRow row in content.RowsFor("setting"))
        {
            string? valueType = JsonNodes.GetString(row.Node, "valueType");
            if (valueType == null || !valueType.StartsWith("key:", StringComparison.Ordinal))
            {
                continue;
            }

            AddAt(content, bundle, JsonPointer.Append(row.Pointer, "value"), valueType.Substring(4), references, seen);
        }

        return new ReferenceIndex(references);
    }

    private static void AddAt(ConfigurationContent content, JsonTreeNode bundle, string pointer, string target, List<RowReference> references, HashSet<string> seen)
    {
        if (!content.TryFindRowAt(pointer, out ContentRow? row) || !JsonPointer.TryResolve(bundle, pointer, out JsonTreeNode value))
        {
            return;
        }

        if (value.Kind == JsonTreeKind.String)
        {
            Add(row, pointer, target, value.GetString(), references, seen);
        }
        else if (value.Kind == JsonTreeKind.Array)
        {
            for (int i = 0; i < value.Items.Count; i++)
            {
                if (value.Items[i].Kind == JsonTreeKind.String)
                {
                    Add(row, JsonPointer.Append(pointer, i), target, value.Items[i].GetString(), references, seen);
                }
            }
        }
    }

    private static void Add(ContentRow row, string pointer, string target, string value, List<RowReference> references, HashSet<string> seen)
    {
        if (seen.Add(pointer))
        {
            references.Add(new RowReference(row, pointer, target, value));
        }
    }
}
