using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Json;
using Product.Engine.Configuration.Model;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V04: a new row never takes the key of a retired row of the same entity (document 20, section 3.2: keys are never reused).
//
// A patch upsert merges into the row with the same key, so "a new row" here means an upsert that gives a retired
// row new content. Two cases stay allowed: bringing the row back on purpose (values set isActive true), and
// editing only its label, description or sort order. Anything else would make old records point at a new meaning.
public sealed class V04KeyReused : ValidationRule
{
    public static readonly V04KeyReused Instance = new();

    private static readonly HashSet<string> TextOnlyFields = new(StringComparer.Ordinal) { "label", "description", "sortOrder", "isActive" };

    private V04KeyReused()
        : base("V04", ReasonCodes.KeyReused, "Retired keys are not reused")
    {
    }

    // Checks an upsert against the row it lands on. Returns false when it reports an issue.
    public bool CheckUpsert(string entityKey, string keyPath, JsonTreeNode existing, JsonTreeNode values, string? location, ValidationIssues issues)
    {
        if (JsonNodes.IsActive(existing))
        {
            return true;
        }

        JsonTreeNode? active = JsonNodes.Get(values, "isActive");
        if (active != null && active.Kind == JsonTreeKind.True)
        {
            return true;
        }

        if (values.Members.All(m => TextOnlyFields.Contains(m.Key)))
        {
            return true;
        }

        issues.Add(Issue(
            ConfigurationEntities.LogicalNameOf(entityKey) + " " + Q(keyPath) + " is retired, and this upsert gives its key new content. Pick a new key, or set isActive true to bring the retired row back.",
            entityKey,
            keyPath,
            location));
        return false;
    }
}
