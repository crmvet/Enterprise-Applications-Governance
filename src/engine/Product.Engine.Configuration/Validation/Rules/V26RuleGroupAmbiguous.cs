using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Model;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V26: no two matching rule groups of equal specificity for any reachable combination (document 21, section 10.3).
// Two groups of one decision type tie when they set the same number of scope fields, no field they both set differs,
// and no group with more fields set covers the combination of both. Example: a tier 3 group and an emergency group
// both match a tier 3 emergency, so the set needs a group scoped to both.
// A combination is not reachable when it names a row that is missing or retired (V06 and V07 report those),
// or when it has a tier and the decision type's subject carries no tier.
// Separation rules are grouped by subject (what must differ), and each rule is its own group.
public sealed class V26RuleGroupAmbiguous : ValidationRule, IContentRule
{
    public static readonly V26RuleGroupAmbiguous Instance = new();

    private static readonly string[] SeparationScope = { "decisionTypeKey", "tierKey", "targetStageKey" };

    // The entity each key scope field points at. A portfolio is an operational row, so it is always reachable.
    private static readonly Dictionary<string, string> ScopeTargets = new(StringComparer.Ordinal)
    {
        { "decisionTypeKey", "decision-type" },
        { "tierKey", "tier" },
        { "lifecycleStageKey", "lifecycle-stage" },
        { "changeTypeKey", "change-type" },
        { "applicationKindKey", "application-kind" },
        { "targetStageKey", "target-stage" },
        { "classificationKey", "data-classification" },
        { "dataDomainLevelKey", "data-domain-level" },
    };

    private V26RuleGroupAmbiguous()
        : base("V26", ReasonCodes.RuleGroupAmbiguous, "No two rule groups tie")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        ConfigurationContent content = context.Content;
        var tiered = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (DecisionTypeInfo type in DecisionTypeInfo.Active(content))
        {
            if (!tiered.ContainsKey(type.Key))
            {
                tiered.Add(type.Key, type.CarriesTier);
            }
        }

        foreach (IGrouping<string, RuleGroup> byType in RuleGroups.ApprovalRules(content).Concat(RuleGroups.DecisionRights(content)).GroupBy(g => g.Entity + "\u0000" + g.DecisionTypeKey))
        {
            RuleGroup[] groups = byType.ToArray();
            bool carriesTier = !tiered.TryGetValue(groups[0].DecisionTypeKey, out bool value) || value;
            foreach (KeyValuePair<RuleGroup, RuleGroup> tie in Ties(content, groups, carriesTier, tiered))
            {
                RuleGroup a = tie.Key;
                RuleGroup b = tie.Value;
                string name = ConfigurationEntities.LogicalNameOf(a.Entity);
                issues.Add(Issue(
                    name + " groups of decision type " + Q(a.DecisionTypeKey) + " with " + RuleGroup.DescribeScope(a.Scope) + " (" + a.RowList() + ") and with "
                    + RuleGroup.DescribeScope(b.Scope) + " (" + b.RowList() + ") both match " + RuleGroup.DescribeScope(a.MergedWith(b).OrderBy(p => Array.IndexOf(RuleGroups.ApprovalScope, p.Key)))
                    + " with the same specificity, so no single group wins. Add a group scoped to both, or narrow one of them.",
                    a.Entity,
                    b.Rows[0].KeyPath,
                    b.Rows[0].Pointer));
            }
        }

        CheckSeparation(content, tiered, issues);
    }

    private void CheckSeparation(ConfigurationContent content, Dictionary<string, bool> tiered, ValidationIssues issues)
    {
        string approverDiffers = EngineContract.SeparationRuleKinds.ToWire(SeparationRuleKind.ApproverDiffers);
        var bySubject = new Dictionary<string, List<RuleGroup>>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (ContentRow row in RowFacts.Active(content, "separation-rule"))
        {
            if (!string.Equals(RowFacts.Text(row, "ruleKind"), approverDiffers, StringComparison.Ordinal))
            {
                continue;
            }

            string subject = (RowFacts.Text(row, "mustDifferFrom") ?? string.Empty)
                + " " + (RowFacts.Text(row, "mustDifferFromOwnerRoleKey") ?? RowFacts.Text(row, "mustDifferFromRoleKey") ?? string.Empty);
            subject = subject.Trim();
            var scope = SeparationScope
                .Where(f => RowFacts.Text(row, f) != null)
                .Select(f => new KeyValuePair<string, string>(f, RowFacts.Text(row, f)!))
                .ToList();
            var group = new RuleGroup("separation-rule", subject, scope);
            group.Rows.Add(row);
            if (!bySubject.TryGetValue(subject, out List<RuleGroup>? list))
            {
                list = new List<RuleGroup>();
                bySubject.Add(subject, list);
                order.Add(subject);
            }

            list.Add(group);
        }

        foreach (string subject in order)
        {
            foreach (KeyValuePair<RuleGroup, RuleGroup> tie in Ties(content, bySubject[subject].ToArray(), carriesTier: true, tiered))
            {
                RuleGroup a = tie.Key;
                RuleGroup b = tie.Value;
                issues.Add(Issue(
                    "Separation Rules " + a.RowList() + " and " + b.RowList() + " both say the approver must differ from " + Q(subject) + " for "
                    + RuleGroup.DescribeScope(a.MergedWith(b).OrderBy(p => Array.IndexOf(SeparationScope, p.Key)))
                    + " with the same specificity, so no single rule wins. Add a rule scoped to both, or narrow one of them.",
                    "separation-rule",
                    b.Rows[0].KeyPath,
                    b.Rows[0].Pointer));
            }
        }
    }

    // Each pair of groups that tie, once.
    private static IEnumerable<KeyValuePair<RuleGroup, RuleGroup>> Ties(ConfigurationContent content, RuleGroup[] groups, bool carriesTier, Dictionary<string, bool> tiered)
    {
        for (int i = 0; i < groups.Length; i++)
        {
            for (int j = i + 1; j < groups.Length; j++)
            {
                RuleGroup a = groups[i];
                RuleGroup b = groups[j];
                if (a.Specificity != b.Specificity || !a.CompatibleWith(b))
                {
                    continue;
                }

                Dictionary<string, string> merged = a.MergedWith(b);
                if (!Resolves(content, merged) || (merged.ContainsKey("tierKey") && !Reachable(merged, carriesTier, tiered)))
                {
                    continue;
                }

                if (groups.Any(c => c.Specificity > a.Specificity && c.Matches(merged)))
                {
                    continue;
                }

                yield return new KeyValuePair<RuleGroup, RuleGroup>(a, b);
            }
        }
    }

    // True when every key in the combination names an active row.
    private static bool Resolves(ConfigurationContent content, Dictionary<string, string> merged)
    {
        foreach (KeyValuePair<string, string> pair in merged)
        {
            if (ScopeTargets.TryGetValue(pair.Key, out string? entity) && RowFacts.FindActive(content, entity, pair.Value) == null)
            {
                return false;
            }
        }

        return true;
    }

    // A tier can only be part of a combination when the decision type carries a tier.
    private static bool Reachable(Dictionary<string, string> merged, bool carriesTier, Dictionary<string, bool> tiered)
    {
        if (merged.TryGetValue("decisionTypeKey", out string? type))
        {
            return !tiered.TryGetValue(type, out bool value) || value;
        }

        return carriesTier;
    }
}
