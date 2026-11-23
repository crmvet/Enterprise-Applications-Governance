using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Model;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V32 (safety floor): at least one active role with Can Change Configuration, and the configuration change decision
// type has an approval rule naming such a role. Document 21, section 5.4, floor 3 says every configuration change is
// accepted by such a person, so the gate policy change type is held to the same rule. The most specific group
// replaces broader ones, so every group of the two types needs a counting rule that names such a role.
// WORKING DEFAULT (open design question 4): a configuration admin accepts the engine's diff on one card, and may be
// the admin who instructed the change. A second admin comes only from a separation rule (Regulated, strict separation).
public sealed class V32ConfigurationAdmin : ValidationRule, IContentRule
{
    public static readonly V32ConfigurationAdmin Instance = new();

    private V32ConfigurationAdmin()
        : base("V32", ReasonCodes.RoleConfigAdminMissing, "A configuration admin accepts every configuration change")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        ConfigurationContent content = context.Content;
        var adminRoles = new HashSet<string>(
            RowFacts.Active(content, "role").Where(r => RowFacts.IsTrue(r, "canChangeConfiguration") && !RowFacts.IsTrue(r, "isWorkloadRole") && !RowFacts.IsTrue(r, "isReadOnly")).Select(r => r.KeyPath),
            StringComparer.Ordinal);
        if (adminRoles.Count == 0)
        {
            issues.Add(Issue(
                "the set has no active Role with canChangeConfiguration true, so nobody could accept a configuration change. Set it on the configuration admin role.",
                "role",
                null,
                "/entities/role"));
        }

        List<RuleGroup> groups = RuleGroups.ApprovalRules(content);
        foreach (string type in WellKnown.ConfigurationDecisionTypes)
        {
            RuleGroup[] typeGroups = groups.Where(g => string.Equals(g.DecisionTypeKey, type, StringComparison.Ordinal)).ToArray();
            if (typeGroups.Length == 0)
            {
                issues.Add(Issue(
                    "Decision Type " + Q(type) + " has no active Approval Rule, so no configuration admin would accept the change. Add a rule naming a role with canChangeConfiguration.",
                    "decision-type",
                    type,
                    null));
                continue;
            }

            foreach (RuleGroup group in typeGroups)
            {
                bool named = group.Rows.Any(r => !RowFacts.IsTrue(r, "isConsultation") && RowFacts.Text(r, "approverRoleKey") is string role && adminRoles.Contains(role));
                if (!named)
                {
                    issues.Add(Issue(
                        "the Approval Rule group of " + group.Describe() + " (" + group.RowList() + ") has no counting rule that names a role with canChangeConfiguration, "
                        + "so a configuration change could be accepted without a configuration admin. Add such a rule to the group.",
                        "approval-rule",
                        group.Rows[0].KeyPath,
                        group.Rows[0].Pointer));
                }
            }
        }
    }
}
