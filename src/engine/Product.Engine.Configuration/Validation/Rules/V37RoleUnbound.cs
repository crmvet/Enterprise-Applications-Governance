using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Model;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V37 (warning): every human role used by an approval rule has at least one binding in the working set.
// Not checked for presets, which never hold bindings (V44).
// A working set with no binding at all is still at bootstrap: version 1 has no bindings, and the client binding patch
// comes next (document 21, section 4.8, step 4). V37 waits until that patch has added the first binding, so it warns
// about the roles the patch left out instead of about every role.
public sealed class V37RoleUnbound : ValidationRule, IContentRule
{
    public static readonly V37RoleUnbound Instance = new();

    private V37RoleUnbound()
        : base("V37", ReasonCodes.RoleUnbound, "Approver roles have a group binding")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        if (context.IsPresetBuild)
        {
            return;
        }

        var bound = new HashSet<string>(
            RowFacts.Active(context.Content, "role-group-binding").Select(b => RowFacts.Text(b, "roleKey")).Where(k => k != null).Select(k => k!),
            StringComparer.Ordinal);
        if (bound.Count == 0)
        {
            return;
        }

        Dictionary<string, RoleInfo> roles = RoleInfo.All(context.Content);
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (ContentRow rule in RowFacts.Active(context.Content, "approval-rule"))
        {
            string? roleKey = RowFacts.Text(rule, "approverRoleKey");
            if (roleKey == null || bound.Contains(roleKey) || !roles.TryGetValue(roleKey, out RoleInfo? role) || role.IsWorkload || role.IsReadOnly || !reported.Add(roleKey))
            {
                // V30 reports workload and read-only approvers. V06 reports an unknown role.
                continue;
            }

            issues.Add(Issue(
                "Role " + Q(roleKey) + " approves under Approval Rule " + Q(rule.KeyPath) + " but has no Role Group Binding in the working set, so nobody can answer its cards. "
                + "Bind it to an Entra group in the client binding patch.",
                "role",
                roleKey,
                role.Row.Pointer));
        }
    }
}
