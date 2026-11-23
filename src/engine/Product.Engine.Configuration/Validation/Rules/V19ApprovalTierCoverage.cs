using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Model;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V19: every tier has a person to ask. For each decision type whose subject carries a tier, and each active tier:
// with Requires Approval, at least one approval rule applies at that tier (scoped to it, or with no tier);
// without it, the resolved accountable holder is an owner role, or a role that is neither a workload role
// nor read-only, so the one card has a person to go to (28, issue 1).
// WORKING DEFAULT (open design question 8): outside the locked list, every decision goes on a person's card.
public sealed class V19ApprovalTierCoverage : ValidationRule, IContentRule
{
    public static readonly V19ApprovalTierCoverage Instance = new();

    private V19ApprovalTierCoverage()
        : base("V19", ReasonCodes.ApprovalRuleTierCoverage, "Every tier has a person to ask")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        ConfigurationContent content = context.Content;
        List<string> tiers = RowFacts.ActiveKeys(content, "tier");
        var activeTiers = new HashSet<string>(tiers, StringComparer.Ordinal);
        Dictionary<string, RoleInfo> roles = RoleInfo.All(content);
        ContentRow[] rules = RowFacts.Active(content, "approval-rule").ToArray();
        List<RuleGroup> rightGroups = RuleGroups.DecisionRights(content);
        string accountable = EngineContract.DecisionRightLetters.ToWire(DecisionRightLetter.Accountable);

        foreach (DecisionTypeInfo type in DecisionTypeInfo.Active(content).Where(t => t.CarriesTier))
        {
            if (type.RequiresApproval)
            {
                ContentRow[] typeRules = rules.Where(r => string.Equals(RowFacts.Text(r, "decisionTypeKey"), type.Key, StringComparison.Ordinal)).ToArray();
                foreach (string tier in tiers)
                {
                    bool covered = typeRules.Any(r => RowFacts.Text(r, "tierKey") == null || string.Equals(RowFacts.Text(r, "tierKey"), tier, StringComparison.Ordinal));
                    if (!covered)
                    {
                        issues.Add(Issue(
                            "Decision Type " + Q(type.Key) + " requires approval, but no active Approval Rule applies at tier " + Q(tier)
                            + ". Add a rule scoped to that tier, or one with no tier.",
                            type.Row.Entity,
                            type.Key,
                            type.Row.Pointer));
                    }
                }

                continue;
            }

            // Without approval rules the one card goes to the accountable holder of each group that can apply.
            foreach (RuleGroup group in rightGroups.Where(g => string.Equals(g.DecisionTypeKey, type.Key, StringComparison.Ordinal)))
            {
                string? tier = group.Value("tierKey");
                if (tier != null && !activeTiers.Contains(tier))
                {
                    continue;
                }

                ContentRow[] holders = group.Rows.Where(r => string.Equals(RowFacts.Text(r, "letter"), accountable, StringComparison.Ordinal)).ToArray();
                if (holders.Length != 1)
                {
                    // V22 reports a group without exactly one accountable.
                    continue;
                }

                string? roleKey = RowFacts.Text(holders[0], "roleKey");
                if (roleKey == null || !roles.TryGetValue(roleKey, out RoleInfo? role) || (!role.IsWorkload && !role.IsReadOnly))
                {
                    continue;
                }

                issues.Add(Issue(
                    "Decision Type " + Q(type.Key) + " has no approval rules, so its one card goes to the accountable holder. Decision Right " + Q(holders[0].KeyPath)
                    + " names Role " + Q(roleKey) + ", " + role.Kind + " that no person answers for. Name an owner role, or a role that is neither a workload role nor read-only.",
                    holders[0].Entity,
                    holders[0].KeyPath,
                    holders[0].Pointer));
            }
        }
    }
}
