using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Model;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V25: an emergency rule exists where the change type allows authorization after and the level is advise or enforce.
// Without it, an emergency at enforce would stop, and at advise the check step could not say when approval is due.
public sealed class V25GatePolicyEmergencyRule : ValidationRule, IContentRule
{
    public static readonly V25GatePolicyEmergencyRule Instance = new();

    private V25GatePolicyEmergencyRule()
        : base("V25", ReasonCodes.GatePolicyEmergencyRule, "Emergencies at advise or enforce have an emergency rule")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        var after = new HashSet<string>(
            RowFacts.Active(context.Content, "change-type").Where(c => RowFacts.IsTrue(c, "allowsAuthorizationAfter")).Select(c => c.KeyPath),
            StringComparer.Ordinal);
        foreach (ContentRow row in RowFacts.Active(context.Content, "gate-policy"))
        {
            string? changeType = RowFacts.Text(row, "changeTypeKey");
            string? level = RowFacts.Text(row, "gateLevel");
            if (changeType == null || level == null || !after.Contains(changeType) || !EngineContract.GateLevels.TryParse(level, out GateLevel parsed))
            {
                continue;
            }

            if ((parsed == GateLevel.Advise || parsed == GateLevel.Enforce) && !RowFacts.Has(row, "emergencyRule"))
            {
                issues.Add(Issue(
                    "Gate Policy " + Q(row.KeyPath) + " is at " + Q(level) + " for change type " + Q(changeType) + ", which allows authorization after the change, but it has no emergency rule. "
                    + "Add an emergencyRule that says how an emergency passes and when its approval is due.",
                    row.Entity,
                    row.KeyPath,
                    row.Pointer));
            }
        }
    }
}
