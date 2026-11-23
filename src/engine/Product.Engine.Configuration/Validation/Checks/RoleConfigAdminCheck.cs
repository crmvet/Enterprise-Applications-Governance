using System;
using Product.Engine.Configuration.Model;
using Product.Engine.Configuration.Validation.Rules;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Checks;

// Engine check role.config-admin (document 25, section 5.11): Can Change Configuration is set only on the
// configuration admin role. Document 20 (C29) and document 22 say "Yes only for the configuration admin role (D-10)",
// and document 26 (RR-03) names `configuration-admin` as the only role with it. V32 checks that such a role exists
// and accepts every configuration change; this check keeps the right from spreading to other roles.
// Only active roles count: a retired role grants nothing. A second person who must accept a configuration change
// (Regulated, strict separation) holds the same role through its group, never a second role with the flag.
// WORKING DEFAULT (open design question 1): the key `configuration-admin` is a fixed engine key.
// WORKING DEFAULT (open design question 4): a configuration admin accepts the engine's diff on one card.
public sealed class RoleConfigAdminCheck : EngineCheck, IContentRule
{
    public static readonly RoleConfigAdminCheck Instance = new();

    private RoleConfigAdminCheck()
        : base(ReasonCodes.RoleConfigAdmin, "Only the configuration admin role can change configuration")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        foreach (ContentRow role in RowFacts.Active(context.Content, "role"))
        {
            if (!RowFacts.IsTrue(role, "canChangeConfiguration") || string.Equals(role.KeyPath, WellKnown.ConfigurationAdminRole, StringComparison.Ordinal))
            {
                continue;
            }

            issues.Add(Issue(
                "Role " + Q(role.KeyPath) + " has canChangeConfiguration true, but only the configuration admin role " + Q(WellKnown.ConfigurationAdminRole)
                + " may change configuration. Set it to false on " + Q(role.KeyPath) + ", and give those people the configuration admin role through its group.",
                "role",
                role.KeyPath,
                role.Pointer + "/canChangeConfiguration"));
        }
    }
}
