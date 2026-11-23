using System;
using System.Collections.Generic;
using Product.Engine.Configuration.Model;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V35 (identity): role group bindings point only to principal references of kind group (D-11), and never bind a
// workload role. Workload roles reach workload identities only through Workload Grants (C42).
// So a grant gives only a workload role, and only to a service principal or a managed identity: agent identities and
// agent users never hold a grant (document 26, section 3.4).
// The principal kind of a directory object is a fact from its Principal Reference row (O05). Without it the kind is
// not checked here; the write guard (G-12) checks it when the binding is written.
// WORKING DEFAULT (open design question 12): pipelines and the approval runner hold their workload role directly,
// as a recorded and expiring grant. People reach roles only through groups.
public sealed class V35RoleBindingTargets : ValidationRule, IContentRule
{
    public static readonly V35RoleBindingTargets Instance = new();

    private V35RoleBindingTargets()
        : base("V35", ReasonCodes.RoleBindingNotGroup, "Groups for people, grants for workloads")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        Dictionary<string, RoleInfo> roles = RoleInfo.All(context.Content);
        IReadOnlyDictionary<string, PrincipalKind> kinds = context.Facts.PrincipalKinds;

        foreach (ContentRow binding in RowFacts.Active(context.Content, "role-group-binding"))
        {
            string? roleKey = RowFacts.Text(binding, "roleKey");
            if (roleKey != null && roles.TryGetValue(roleKey, out RoleInfo? role) && role.IsWorkload)
            {
                issues.Add(Issue(
                    "Role Group Binding " + Q(binding.KeyPath) + " binds Role " + Q(roleKey) + ", a workload role. Workload roles reach workload identities only through a Workload Grant, "
                    + "never through a group: retire the binding and add a grant.",
                    binding.Entity,
                    binding.KeyPath,
                    binding.Pointer));
            }

            string? groupId = RowFacts.Text(binding, "entraGroupObjectId");
            if (groupId != null && kinds.TryGetValue(groupId, out PrincipalKind kind) && kind != PrincipalKind.Group)
            {
                issues.Add(Issue(
                    "Role Group Binding " + Q(binding.KeyPath) + " points to " + Q(groupId) + ", which is a " + EngineContract.PrincipalKinds.ToWire(kind)
                    + ", not a group. Roles reach people only through Entra groups (D-11): point it to a group.",
                    binding.Entity,
                    binding.KeyPath,
                    binding.Pointer));
            }
        }

        foreach (ContentRow grant in RowFacts.Active(context.Content, "workload-grant"))
        {
            string? roleKey = RowFacts.Text(grant, "workloadRoleKey");
            if (roleKey != null && roles.TryGetValue(roleKey, out RoleInfo? role) && !role.IsWorkload)
            {
                issues.Add(Issue(
                    "Workload Grant " + Q(grant.KeyPath) + " gives Role " + Q(roleKey) + ", which is not a workload role. People get roles only through group bindings: "
                    + "grant a workload role, or bind this role to a group instead.",
                    grant.Entity,
                    grant.KeyPath,
                    grant.Pointer));
            }

            string? principal = RowFacts.Text(grant, "principalObjectId");
            if (principal != null && kinds.TryGetValue(principal, out PrincipalKind kind) && kind != PrincipalKind.ServicePrincipal && kind != PrincipalKind.ManagedIdentity)
            {
                issues.Add(Issue(
                    "Workload Grant " + Q(grant.KeyPath) + " names " + Q(principal) + ", which is a " + EngineContract.PrincipalKinds.ToWire(kind)
                    + ". Only a service principal or a managed identity holds a workload grant, and agent identities never do.",
                    grant.Entity,
                    grant.KeyPath,
                    grant.Pointer));
            }
        }
    }
}
