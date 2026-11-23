using System.Collections.Generic;
using Product.Engine.Configuration.Json;
using Product.Engine.Configuration.Model;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V30 (safety floor): approval rules name only roles that are neither workload roles nor read-only;
// no rule can make an AI or a pipeline an approver (document 21, section 5.4, floor 1).
// The approvers an emergency rule names (approverRoleKeys) authorize after the fact, so they are checked too.
// WORKING DEFAULT (open design question 12): workload roles are held by workload identities, never by people.
public sealed class V30ApprovalRuleWorkloadApprover : ValidationRule, IContentRule
{
    public static readonly V30ApprovalRuleWorkloadApprover Instance = new();

    private V30ApprovalRuleWorkloadApprover()
        : base("V30", ReasonCodes.ApprovalRuleWorkloadApprover, "Only human roles approve")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        Dictionary<string, RoleInfo> roles = RoleInfo.All(context.Content);
        foreach (ContentRow rule in RowFacts.Active(context.Content, "approval-rule"))
        {
            string? roleKey = RowFacts.Text(rule, "approverRoleKey");
            if (roleKey != null && roles.TryGetValue(roleKey, out RoleInfo? role) && (role.IsWorkload || role.IsReadOnly))
            {
                issues.Add(Issue("Approval Rule " + Q(rule.KeyPath) + " names Role " + Q(roleKey) + " as approver. " + Why(role), rule.Entity, rule.KeyPath, rule.Pointer));
            }
        }

        foreach (ContentRow policy in RowFacts.Active(context.Content, "gate-policy"))
        {
            JsonTreeNode? emergency = JsonNodes.Get(policy.Node, "emergencyRule");
            JsonTreeNode? approvers = emergency == null ? null : JsonNodes.Get(emergency, "approverRoleKeys");
            if (approvers == null || approvers.Kind != JsonTreeKind.Array)
            {
                continue;
            }

            foreach (JsonTreeNode item in approvers.Items)
            {
                if (item.Kind == JsonTreeKind.String && roles.TryGetValue(item.GetString(), out RoleInfo? role) && (role.IsWorkload || role.IsReadOnly))
                {
                    issues.Add(Issue(
                        "Gate Policy " + Q(policy.KeyPath) + " has an emergency rule that names Role " + Q(item.GetString()) + " as approver. " + Why(role),
                        policy.Entity,
                        policy.KeyPath,
                        policy.Pointer));
                }
            }
        }
    }

    private static string Why(RoleInfo role)
    {
        return role.IsWorkload
            ? "It is a workload role, held by a pipeline or the approval runner, and no AI or pipeline ever approves: name a human role."
            : "It is a read-only role, which never writes, so it cannot approve: name a role that can act.";
    }
}
