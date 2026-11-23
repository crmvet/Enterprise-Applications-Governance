using System;
using Product.Engine.Configuration.Model;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V24: fail mode is set at enforce (error if missing) and only at enforce (warning if set elsewhere).
// Fail mode applies only when the product cannot answer an enforce check (document 21, section 7.4).
public sealed class V24GatePolicyFailMode : ValidationRule, IContentRule
{
    public static readonly V24GatePolicyFailMode Instance = new();

    private V24GatePolicyFailMode()
        : base("V24", ReasonCodes.GatePolicyFailMode, "Fail mode only and always at enforce")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        string enforce = EngineContract.GateLevels.ToWire(GateLevel.Enforce);
        foreach (ContentRow row in RowFacts.Active(context.Content, "gate-policy"))
        {
            string? level = RowFacts.Text(row, "gateLevel");
            if (level == null)
            {
                continue;
            }

            bool atEnforce = string.Equals(level, enforce, StringComparison.Ordinal);
            bool hasFailMode = RowFacts.Has(row, "failMode");
            if (atEnforce && !hasFailMode)
            {
                issues.Add(Issue(
                    "Gate Policy " + Q(row.KeyPath) + " is at enforce and has no fail mode, so a check step would not know what to do when the product cannot answer. Set failMode to fail-open or fail-closed.",
                    row.Entity,
                    row.KeyPath,
                    row.Pointer));
            }
            else if (!atEnforce && hasFailMode)
            {
                issues.Add(Issue(
                    "Gate Policy " + Q(row.KeyPath) + " is at " + Q(level) + " and sets a fail mode, which applies only at enforce. Remove failMode.",
                    row.Entity,
                    row.KeyPath,
                    row.Pointer,
                    ReasonSeverity.Warning));
            }
        }
    }
}
