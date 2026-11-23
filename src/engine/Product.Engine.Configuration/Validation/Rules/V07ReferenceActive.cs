using Product.Engine.Configuration.Model;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V07: references point to active rows, unless the referencing row is itself retired.
// A child row of a retired parent counts as retired, on both ends of the reference.
public sealed class V07ReferenceActive : ValidationRule, IContentRule
{
    public static readonly V07ReferenceActive Instance = new();

    private V07ReferenceActive()
        : base("V07", ReasonCodes.RefInactive, "References point to active rows")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        foreach (RowReference reference in context.References.All)
        {
            if (reference.IsWildcard || !reference.IsConfigurationTarget || !reference.Source.IsEffectivelyActive)
            {
                continue;
            }

            if (!context.Content.TryFindRow(reference.TargetEntity, reference.TargetKeyPath, out ContentRow? target) || target.IsEffectivelyActive)
            {
                continue;
            }

            issues.Add(Issue(
                "active " + reference.Source.LogicalName + " " + Q(reference.Source.KeyPath) + " field " + Q(reference.FieldName) + " points to retired " + V06ReferenceResolves.DescribeTarget(reference) + ". Point it to an active row, or retire " + Q(reference.Source.KeyPath) + " too.",
                reference.Source.Entity,
                reference.Source.KeyPath,
                reference.Pointer));
        }
    }
}
