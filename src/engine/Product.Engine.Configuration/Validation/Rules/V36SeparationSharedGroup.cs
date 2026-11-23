using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Json;
using Product.Engine.Configuration.Model;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V36 (identity): roles named together in a roles-in-different-groups separation rule bind to different groups.
// One Entra group carrying both roles would let one person hold both (D-20, "require a different person").
public sealed class V36SeparationSharedGroup : ValidationRule, IContentRule
{
    public static readonly V36SeparationSharedGroup Instance = new();

    private V36SeparationSharedGroup()
        : base("V36", ReasonCodes.SeparationSharedGroup, "Separated roles bind to different groups")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        var groupsByRole = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (ContentRow binding in RowFacts.Active(context.Content, "role-group-binding"))
        {
            string? role = RowFacts.Text(binding, "roleKey");
            string? group = RowFacts.Text(binding, "entraGroupObjectId");
            if (role == null || group == null)
            {
                continue;
            }

            if (!groupsByRole.TryGetValue(role, out HashSet<string>? groups))
            {
                // Directory object IDs compare without case.
                groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                groupsByRole.Add(role, groups);
            }

            groups.Add(group);
        }

        string differentGroups = EngineContract.SeparationRuleKinds.ToWire(SeparationRuleKind.RolesInDifferentGroups);
        foreach (ContentRow rule in RowFacts.Active(context.Content, "separation-rule"))
        {
            JsonTreeNode? roleKeys = JsonNodes.Get(rule.Node, "roleKeys");
            if (!string.Equals(RowFacts.Text(rule, "ruleKind"), differentGroups, StringComparison.Ordinal) || roleKeys == null || roleKeys.Kind != JsonTreeKind.Array)
            {
                continue;
            }

            string[] keys = roleKeys.Items.Where(i => i.Kind == JsonTreeKind.String).Select(i => i.GetString()).Distinct(StringComparer.Ordinal).ToArray();
            for (int i = 0; i < keys.Length; i++)
            {
                for (int j = i + 1; j < keys.Length; j++)
                {
                    if (!groupsByRole.TryGetValue(keys[i], out HashSet<string>? first) || !groupsByRole.TryGetValue(keys[j], out HashSet<string>? second))
                    {
                        continue;
                    }

                    string? shared = first.FirstOrDefault(second.Contains);
                    if (shared == null)
                    {
                        continue;
                    }

                    issues.Add(Issue(
                        "Separation Rule " + Q(rule.KeyPath) + " keeps Role " + Q(keys[i]) + " and Role " + Q(keys[j]) + " in different groups, but both bind to group " + Q(shared)
                        + ". Bind them to different groups.",
                        rule.Entity,
                        rule.KeyPath,
                        rule.Pointer));
                }
            }
        }
    }
}
