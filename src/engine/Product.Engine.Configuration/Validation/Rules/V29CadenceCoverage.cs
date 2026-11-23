using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Model;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V29 (warning): a review type with a cadence at one tier has a cadence at every active tier.
// A gap is allowed, but the approver should see it: applications at that tier would never be reviewed.
public sealed class V29CadenceCoverage : ValidationRule, IContentRule
{
    public static readonly V29CadenceCoverage Instance = new();

    private V29CadenceCoverage()
        : base("V29", ReasonCodes.CadenceCoverage, "Review cadences cover every tier")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        List<string> tiers = RowFacts.ActiveKeys(context.Content, "tier");
        var byType = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (ContentRow cadence in RowFacts.Active(context.Content, "review-cadence"))
        {
            string? type = RowFacts.Text(cadence, "reviewTypeKey");
            string? tier = RowFacts.Text(cadence, "tierKey");
            if (type == null || tier == null || !tiers.Contains(tier, StringComparer.Ordinal))
            {
                continue;
            }

            if (!byType.TryGetValue(type, out HashSet<string>? covered))
            {
                covered = new HashSet<string>(StringComparer.Ordinal);
                byType.Add(type, covered);
                order.Add(type);
            }

            covered.Add(tier);
        }

        foreach (string type in order)
        {
            string[] missing = tiers.Where(t => !byType[type].Contains(t)).ToArray();
            if (missing.Length == 0)
            {
                continue;
            }

            string[] covered = tiers.Where(t => byType[type].Contains(t)).ToArray();
            context.Content.TryFindRow("review-type", type, out ContentRow? typeRow);
            issues.Add(Issue(
                "Review Type " + Q(type) + " has a cadence at tier " + string.Join(", ", covered.Select(Q)) + " but not at tier " + string.Join(", ", missing.Select(Q))
                + ", so applications at " + (missing.Length == 1 ? "that tier" : "those tiers") + " are never reviewed. Add a cadence for each active tier, or a wildcard row.",
                "review-type",
                type,
                typeRow?.Pointer));
        }
    }
}
