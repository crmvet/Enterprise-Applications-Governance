using Product.Engine.Configuration.Model;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V06: no orphan references. Every ...Key and ...Keys value resolves to a row of the named entity in the same set.
// The target entity of each field comes from the schema's x-references annotation.
// The same rule reports a patch that retires a row that is not there, and a preset that names an unknown preset.
public sealed class V06ReferenceResolves : ValidationRule, IContentRule
{
    public static readonly V06ReferenceResolves Instance = new();

    private V06ReferenceResolves()
        : base("V06", ReasonCodes.RefUnresolved, "References resolve")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        foreach (RowReference reference in context.References.All)
        {
            if (reference.IsWildcard || !reference.IsConfigurationTarget)
            {
                continue;
            }

            if (context.Content.TryFindRow(reference.TargetEntity, reference.TargetKeyPath, out _))
            {
                continue;
            }

            issues.Add(Issue(
                reference.Source.LogicalName + " " + Q(reference.Source.KeyPath) + " field " + Q(reference.FieldName) + " points to " + DescribeTarget(reference) + ", which is not in the set. Add that row or fix the key.",
                reference.Source.Entity,
                reference.Source.KeyPath,
                reference.Pointer));
        }
    }

    // A patch or preset names a row or preset that does not exist.
    public void Missing(string message, string? entityKey, string? keyPath, string? location, ValidationIssues issues)
    {
        issues.Add(Issue(message, entityKey, keyPath, location));
    }

    internal static string DescribeTarget(RowReference reference)
    {
        if (reference.TargetParentKey != null)
        {
            return "list value " + Q(reference.Value) + " of value list " + Q(reference.TargetParentKey);
        }

        return ConfigurationEntities.LogicalNameOf(reference.TargetEntity) + " " + Q(reference.Value);
    }
}
