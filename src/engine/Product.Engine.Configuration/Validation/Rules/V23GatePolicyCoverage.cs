using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Model;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V23: every combination of active tier, change type and target stage resolves to exactly one gate policy row.
// Rows with a portfolio narrow a combination for one portfolio, so the rows without a portfolio must cover all.
// Two rows for one combination come from rows of equal explicitness, which V09 already reports while wildcards
// expand. V23 then stays quiet about that combination, so the cause is reported once.
public sealed class V23GatePolicyCoverage : ValidationRule, IContentRule
{
    public static readonly V23GatePolicyCoverage Instance = new();

    private V23GatePolicyCoverage()
        : base("V23", ReasonCodes.GatePolicyCoverage, "Every combination has exactly one gate policy")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        ConfigurationContent content = context.Content;
        Dictionary<string, List<ContentRow>> policies = GatePolicies.ByCombination(content);
        List<string> changeTypes = RowFacts.ActiveKeys(content, "change-type");
        List<string> stages = RowFacts.ActiveKeys(content, "target-stage");
        string[] overlapRefs = issues.Items.Where(i => i.RuleId == "V09" && i.Ref != null).Select(i => i.Ref!).ToArray();

        foreach (string tier in RowFacts.ActiveKeys(content, "tier"))
        {
            foreach (string changeType in changeTypes)
            {
                foreach (string stage in stages)
                {
                    if (!policies.TryGetValue(GatePolicies.Combination(tier, changeType, stage), out List<ContentRow>? rows))
                    {
                        rows = new List<ContentRow>();
                    }

                    int count = rows.Count;
                    if (count == 1)
                    {
                        continue;
                    }

                    if (count == 0)
                    {
                        issues.Add(Issue(
                            "no gate policy for " + GatePolicies.Describe(tier, changeType, stage) + ". Add a row or a wildcard row that covers it.",
                            "gate-policy",
                            null,
                            "/entities/gate-policy"));
                        continue;
                    }

                    if (rows.Any(r => overlapRefs.Any(k => string.Equals(r.KeyPath, k, StringComparison.Ordinal) || r.KeyPath.StartsWith(k + "--", StringComparison.Ordinal))))
                    {
                        continue;
                    }

                    issues.Add(Issue(
                        GatePolicies.Describe(tier, changeType, stage) + " resolves to " + count + " gate policy rows (" + string.Join(", ", rows.Select(r => Q(r.KeyPath)))
                        + "). Keep exactly one: retire the others.",
                        "gate-policy",
                        rows[1].KeyPath,
                        rows[1].Pointer));
                }
            }
        }
    }
}
