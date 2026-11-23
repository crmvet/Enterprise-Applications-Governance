using System;
using System.Linq;
using Product.Engine.Configuration.Json;
using Product.Engine.Configuration.Model;
using Product.Engine.Configuration.Schema;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V12: tier ranks are unique whole numbers.
// Every tier needs a whole-number rank. Ranks must differ among active tiers. A retired tier keeps its rank
// so old records still resolve, and a new tier may take that rank (OP-01 compares active tiers only).
public sealed class V12TierRank : ValidationRule, IContentRule
{
    public static readonly V12TierRank Instance = new();

    private V12TierRank()
        : base("V12", ReasonCodes.TierRankDuplicate, "Tier ranks are unique whole numbers")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        var ranked = new System.Collections.Generic.List<Tuple<ContentRow, string>>();
        foreach (ContentRow row in context.Content.RowsFor("tier"))
        {
            JsonTreeNode? rank = JsonNodes.Get(row.Node, "rank");
            if (rank == null)
            {
                // V01 reports the missing required field.
                continue;
            }

            if (!JsonSchemaSet.IsInteger(rank))
            {
                issues.Add(Issue(
                    "Tier " + Q(row.KeyPath) + " has rank " + Q(rank.ToString()) + ", which is not a whole number. Use a whole number, such as 1 for the lowest tier.",
                    row.Entity,
                    row.KeyPath,
                    row.Pointer));
                continue;
            }

            if (row.IsActive)
            {
                ranked.Add(Tuple.Create(row, rank.GetNumberText()));
            }
        }

        foreach (var group in ranked.GroupBy(r => r.Item2, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            ContentRow[] rows = group.Select(r => r.Item1).ToArray();
            issues.Add(Issue(
                "active tiers " + string.Join(" and ", rows.Select(r => Q(r.KeyPath))) + " share rank " + group.Key + ". Give each active tier its own rank.",
                "tier",
                rows[1].KeyPath,
                rows[1].Pointer));
        }
    }
}
