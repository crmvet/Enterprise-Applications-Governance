using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Model;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V22: for each decision type (and each active tier, where it carries a tier), the resolved decision right group
// holds exactly one accountable. The most specific group replaces broader ones (document 21, section 10.3),
// so every group must hold exactly one accountable on its own. And each decision type needs a group that applies
// whatever the other scope fields are: one with no scope, or one scoped only to the tier.
public sealed class V22DecisionRightAccountable : ValidationRule, IContentRule
{
    public static readonly V22DecisionRightAccountable Instance = new();

    private V22DecisionRightAccountable()
        : base("V22", ReasonCodes.DecisionRightAccountable, "One accountable per decision right group")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        ConfigurationContent content = context.Content;
        List<RuleGroup> groups = RuleGroups.DecisionRights(content);
        string accountable = EngineContract.DecisionRightLetters.ToWire(DecisionRightLetter.Accountable);

        foreach (RuleGroup group in groups)
        {
            ContentRow[] holders = group.Rows.Where(r => string.Equals(RowFacts.Text(r, "letter"), accountable, StringComparison.Ordinal)).ToArray();
            if (holders.Length == 1)
            {
                continue;
            }

            ContentRow pointAt = holders.Length > 1 ? holders[1] : group.Rows[0];
            string found = holders.Length == 0 ? "no accountable row" : holders.Length + " accountable rows (" + string.Join(", ", holders.Select(h => Q(h.KeyPath))) + ")";
            string fix = holders.Length == 0 ? "add one accountable row to the group" : "keep one and retire the others";
            issues.Add(Issue(
                "the Decision Right group of " + group.Describe() + " (rows " + group.RowList() + ") holds " + found + ". The most specific group replaces broader ones, so each group needs exactly one accountable: " + fix + ".",
                "decision-right",
                pointAt.KeyPath,
                pointAt.Pointer));
        }

        List<string> tiers = RowFacts.ActiveKeys(content, "tier");
        foreach (DecisionTypeInfo type in DecisionTypeInfo.Active(content))
        {
            RuleGroup[] typeGroups = groups.Where(g => string.Equals(g.DecisionTypeKey, type.Key, StringComparison.Ordinal)).ToArray();
            if (!type.CarriesTier)
            {
                if (!typeGroups.Any(g => g.Specificity == 0))
                {
                    issues.Add(Issue(
                        "Decision Type " + Q(type.Key) + " has no Decision Right group without a scope, so it has no accountable holder. Add an accountable row with no scope.",
                        type.Row.Entity,
                        type.Key,
                        type.Row.Pointer));
                }

                continue;
            }

            foreach (string tier in tiers)
            {
                bool covered = typeGroups.Any(g => g.UsesOnly("tierKey") && (g.Value("tierKey") == null || string.Equals(g.Value("tierKey"), tier, StringComparison.Ordinal)));
                if (!covered)
                {
                    issues.Add(Issue(
                        "no Decision Right group gives Decision Type " + Q(type.Key) + " an accountable holder at tier " + Q(tier)
                        + ". Add an accountable row with no scope, or one scoped only to that tier.",
                        type.Row.Entity,
                        type.Key,
                        type.Row.Pointer));
                }
            }
        }
    }
}
