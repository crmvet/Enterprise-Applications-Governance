using System;
using System.Linq;
using Product.Engine.Configuration.Model;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V20: decision types that require approval and carry no tier (configuration change, gate policy change)
// have at least one approval rule. A decision type without Requires Approval has only consultation rules, if any.
public sealed class V20ApprovalCoverage : ValidationRule, IContentRule
{
    public static readonly V20ApprovalCoverage Instance = new();

    private V20ApprovalCoverage()
        : base("V20", ReasonCodes.ApprovalRuleCoverage, "Approval rules match Requires Approval")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        ContentRow[] rules = RowFacts.Active(context.Content, "approval-rule").ToArray();
        foreach (DecisionTypeInfo type in DecisionTypeInfo.Active(context.Content))
        {
            ContentRow[] typeRules = rules.Where(r => string.Equals(RowFacts.Text(r, "decisionTypeKey"), type.Key, StringComparison.Ordinal)).ToArray();
            if (type.RequiresApproval && !type.CarriesTier && typeRules.Length == 0)
            {
                issues.Add(Issue(
                    "Decision Type " + Q(type.Key) + " requires approval and carries no tier, but it has no active Approval Rule. Add one, such as a rule naming the configuration admin.",
                    type.Row.Entity,
                    type.Key,
                    type.Row.Pointer));
            }

            if (type.RequiresApproval)
            {
                continue;
            }

            foreach (ContentRow rule in typeRules.Where(r => !RowFacts.IsTrue(r, "isConsultation")))
            {
                issues.Add(Issue(
                    "Approval Rule " + Q(rule.KeyPath) + " counts toward Decision Type " + Q(type.Key) + ", which does not require approval, so its one card goes to the accountable holder. "
                    + "Set isConsultation true on the rule, or set requiresApproval true on the decision type.",
                    rule.Entity,
                    rule.KeyPath,
                    rule.Pointer));
            }
        }
    }
}
