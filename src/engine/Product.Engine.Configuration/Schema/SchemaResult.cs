using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Product.Engine.Core.Canonical;

namespace Product.Engine.Configuration.Schema;

// One place where an instance does not match its schema.
public sealed class SchemaIssue
{
    public SchemaIssue(string instancePath, string message, string keywordPath)
    {
        InstancePath = instancePath ?? throw new ArgumentNullException(nameof(instancePath));
        Message = message ?? throw new ArgumentNullException(nameof(message));
        KeywordPath = keywordPath ?? throw new ArgumentNullException(nameof(keywordPath));
    }

    // JSON pointer into the instance, for example "/entities/tier/0".
    public string InstancePath { get; }

    // One plain phrase, for example "missing required field \"rank\"".
    public string Message { get; }

    // Where in the schema the check lives, for example "config-bundle.schema.json#/$defs/tier.seedRow/required".
    public string KeywordPath { get; }

    public override string ToString()
    {
        return (InstancePath.Length == 0 ? "(root)" : InstancePath) + ": " + Message;
    }
}

// An annotation keyword (one that starts with "x-") that applied at an instance location.
// The configuration schemas use x-references to say which entity a key field points at.
public sealed class SchemaAnnotation
{
    public SchemaAnnotation(string instancePath, string keyword, JsonTreeNode value)
    {
        InstancePath = instancePath ?? throw new ArgumentNullException(nameof(instancePath));
        Keyword = keyword ?? throw new ArgumentNullException(nameof(keyword));
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public string InstancePath { get; }

    public string Keyword { get; }

    public JsonTreeNode Value { get; }
}

// The result of checking one instance against one schema.
public sealed class SchemaResult
{
    public SchemaResult(IEnumerable<SchemaIssue> issues, IEnumerable<SchemaAnnotation> annotations)
    {
        Issues = new ReadOnlyCollection<SchemaIssue>(new List<SchemaIssue>(issues));
        Annotations = new ReadOnlyCollection<SchemaAnnotation>(new List<SchemaAnnotation>(annotations));
    }

    public bool IsValid => Issues.Count == 0;

    // In the order the checks found them.
    public IReadOnlyList<SchemaIssue> Issues { get; }

    // Annotations from every subschema that applied, without repeats.
    public IReadOnlyList<SchemaAnnotation> Annotations { get; }
}
