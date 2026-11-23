using System;
using System.Linq;
using Product.Engine.Configuration.Model;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V18: every active decision type has at least one active outcome.
public sealed class V18DecisionTypeOutcome : ValidationRule, IContentRule
{
    public static readonly V18DecisionTypeOutcome Instance = new();

    private V18DecisionTypeOutcome()
        : base("V18", ReasonCodes.DecisionTypeOutcome, "Active decision types have an active outcome")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        foreach (ContentRow type in RowFacts.Active(context.Content, "decision-type"))
        {
            bool hasOutcome = context.Content.RowsFor("decision-outcome")
                .Any(o => o.Key != null && o.IsEffectivelyActive && string.Equals(o.ParentKey, type.Key, StringComparison.Ordinal));
            if (!hasOutcome)
            {
                issues.Add(Issue(
                    "Decision Type " + Q(type.KeyPath) + " has no active outcome, so no decision of this type could be recorded. Add an outcome, or set isActive true on one.",
                    type.Entity,
                    type.KeyPath,
                    type.Pointer));
            }
        }
    }
}
