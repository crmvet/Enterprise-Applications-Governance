using System;
using Product.Engine.Configuration.Model;
using Product.Engine.Configuration.Schema;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// Where a JSON pointer lands, for an issue's entity and key.
public sealed class RowLocation
{
    public RowLocation(string? entityKey, string? keyPath)
    {
        EntityKey = entityKey;
        KeyPath = keyPath;
    }

    public string? EntityKey { get; }

    public string? KeyPath { get; }
}

// V01: the bundle or patch matches the JSON Schema: types, required fields, no unknown fields.
// It uses config/schema, embedded in this assembly. The same schema runs in the repo's JSON check.
public sealed class V01SchemaShape : ValidationRule
{
    public static readonly V01SchemaShape Instance = new();

    // A broken document can fail in many places. The first ones are enough to act on.
    private const int MaxIssuesPerDocument = 40;

    private V01SchemaShape()
        : base("V01", ReasonCodes.ShapeSchema, "Shape matches the JSON Schema")
    {
    }

    // Checks one document against one schema and reports each mismatch.
    // locate turns a JSON pointer into the entity and key of the row it sits in, when it sits in one.
    public SchemaResult CheckDocument(
        JsonTreeNode document,
        string schemaUri,
        string where,
        ValidationIssues issues,
        Func<string, RowLocation?>? locate = null,
        JsonSchemaSet? schemas = null)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        SchemaResult result = (schemas ?? ConfigurationSchemas.Set).Evaluate(document, schemaUri);
        Report(result, where, issues, locate);
        return result;
    }

    // Reports the issues of a schema result that was computed elsewhere.
    public void Report(SchemaResult result, string where, ValidationIssues issues, Func<string, RowLocation?>? locate = null)
    {
        int count = 0;
        foreach (SchemaIssue problem in result.Issues)
        {
            if (count == MaxIssuesPerDocument)
            {
                issues.Add(Issue("in " + where + ", " + (result.Issues.Count - count) + " more shape problems are not listed. Fix the ones above and check again.", null, null, where));
                break;
            }

            RowLocation? location = locate?.Invoke(problem.InstancePath);
            string path = problem.InstancePath.Length == 0 ? "the document" : problem.InstancePath;
            issues.Add(Issue(
                "in " + where + ", " + path + " " + problem.Message + ". Change it to match the schema.",
                location?.EntityKey,
                location?.KeyPath,
                where + " " + problem.InstancePath));
            count++;
        }
    }

    // Locates pointers inside a bundle's "entities" object.
    public static Func<string, RowLocation?> InContent(ConfigurationContent content)
    {
        return pointer => content.TryFindRowAt(pointer, out ContentRow? row) ? new RowLocation(row.Entity, row.Key == null ? null : row.KeyPath) : null;
    }
}
