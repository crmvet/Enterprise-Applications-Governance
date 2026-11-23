using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Model;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V21: for each active tier, each change type that requires authorization and each target stage whose gate level
// is record or higher, at least one change authorization rule group applies. At off no approvals are asked
// (document 21, section 7.3), so those combinations are skipped. A group counts only when it is scoped by tier,
// change type or target stage alone: a group narrowed to one application kind or portfolio leaves the others uncovered.
public sealed class V21ChangeAuthorizationCoverage : ValidationRule, IContentRule
{
    public static readonly V21ChangeAuthorizationCoverage Instance = new();

    private V21ChangeAuthorizationCoverage()
        : base("V21", ReasonCodes.ApprovalRuleChangeCoverage, "Governed changes have a change authorization group")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        ConfigurationContent content = context.Content;
        ContentRow[] changeTypes = RowFacts.Active(content, "change-type").Where(c => RowFacts.IsTrue(c, "requiresAuthorization")).ToArray();
        if (changeTypes.Length == 0)
        {
            return;
        }

        if (RowFacts.FindActive(content, "decision-type", WellKnown.ChangeAuthorization) == null)
        {
            issues.Add(Issue(
                "change types such as " + Q(changeTypes[0].KeyPath) + " require authorization, but the set has no active Decision Type " + Q(WellKnown.ChangeAuthorization)
                + ". Add it, with approval rules.",
                "decision-type",
                WellKnown.ChangeAuthorization,
                "/entities/decision-type"));
            return;
        }

        RuleGroup[] groups = RuleGroups.ApprovalRules(content)
            .Where(g => string.Equals(g.DecisionTypeKey, WellKnown.ChangeAuthorization, StringComparison.Ordinal) && g.UsesOnly("tierKey", "changeTypeKey", "targetStageKey"))
            .ToArray();
        Dictionary<string, List<ContentRow>> policies = GatePolicies.ByCombination(content);
        List<string> stages = RowFacts.ActiveKeys(content, "target-stage");
        foreach (string tier in RowFacts.ActiveKeys(content, "tier"))
        {
            foreach (ContentRow changeType in changeTypes)
            {
                foreach (string stage in stages)
                {
                    // V23 reports a combination with no gate policy, or with more than one.
                    if (!policies.TryGetValue(GatePolicies.Combination(tier, changeType.KeyPath, stage), out List<ContentRow>? rows) || rows.Count != 1)
                    {
                        continue;
                    }

                    string? level = RowFacts.Text(rows[0], "gateLevel");
                    if (level == null || !EngineContract.GateLevels.TryParse(level, out GateLevel parsed) || parsed == GateLevel.Off)
                    {
                        continue;
                    }

                    var record = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        { "tierKey", tier },
                        { "changeTypeKey", changeType.KeyPath },
                        { "targetStageKey", stage },
                    };
                    if (groups.Any(g => g.Matches(record)))
                    {
                        continue;
                    }

                    issues.Add(Issue(
                        "no change authorization Approval Rule group applies to " + GatePolicies.Describe(tier, changeType.KeyPath, stage) + ", where the gate level is " + Q(level)
                        + ". Add a rule for decision type " + Q(WellKnown.ChangeAuthorization) + " that covers it, or one with no scope.",
                        "approval-rule",
                        null,
                        "/entities/approval-rule"));
                }
            }
        }
    }
}
