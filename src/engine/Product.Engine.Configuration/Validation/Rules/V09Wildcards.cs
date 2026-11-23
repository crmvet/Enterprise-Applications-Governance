using System.Collections.Generic;
using Product.Engine.Configuration.Model;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V09: wildcards only where allowed; after expansion, no combination comes from two rows of equal explicitness.
//
// The wildcard expander reports the second half while it expands (document 21, section 8.4).
// The content check reports the first half: after expansion no "*" may remain in any key field,
// because only the scope fields of Review Cadence (C25) and Gate Policy (C33) expand.
public sealed class V09Wildcards : ValidationRule, IContentRule
{
    public static readonly V09Wildcards Instance = new();

    private V09Wildcards()
        : base("V09", ReasonCodes.RefWildcard, "Wildcards only where allowed, no equal overlaps")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        foreach (RowReference reference in context.References.All)
        {
            if (!reference.IsWildcard)
            {
                continue;
            }

            issues.Add(Issue(
                reference.Source.LogicalName + " " + Q(reference.Source.KeyPath) + " uses the wildcard " + Q("*") + " in " + Q(reference.FieldName) + ", where it is not allowed. Name one key instead.",
                reference.Source.Entity,
                reference.Source.KeyPath,
                reference.Pointer));
        }
    }

    // A wildcard row whose every combination is taken by more explicit rows. It would have no effect.
    // In a working set every shipped row is already expanded and fully explicit, so a new wildcard row only
    // takes effect where the rows it should replace are retired first.
    public void Shadowed(string entityKey, string key, int combinations, ValidationIssues issues)
    {
        string what = combinations == 0
            ? "covers no combination, because its scope names no active row"
            : "wins none of its " + combinations + " combinations, because more explicit rows cover all of them";
        issues.Add(Issue(
            ConfigurationEntities.LogicalNameOf(entityKey) + " wildcard row " + Q(key) + " " + what + ", so it would have no effect. Remove it, or retire the rows it should replace.",
            entityKey,
            key,
            null));
    }

    // Two rows with the same number of explicit scope fields produce the same combination.
    public void Overlap(string entityKey, string firstKey, string secondKey, IEnumerable<string> combination, ValidationIssues issues)
    {
        issues.Add(Issue(
            ConfigurationEntities.LogicalNameOf(entityKey) + " rows " + Q(firstKey) + " and " + Q(secondKey) + " both cover " + string.Join(", ", combination) + " with the same number of explicit fields. Add a row for that combination, or narrow one of them.",
            entityKey,
            secondKey,
            null));
    }
}
