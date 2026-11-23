using System.Linq;
using Product.Engine.Configuration.Model;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V11: at least one active tier. The engine never branches on a fixed tier, but it needs at least one (document 21, section 0).
public sealed class V11ActiveTier : ValidationRule, IContentRule
{
    public static readonly V11ActiveTier Instance = new();

    private V11ActiveTier()
        : base("V11", ReasonCodes.TierNoneActive, "At least one active tier")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        if (context.Content.RowsFor("tier").Any(row => row.IsActive))
        {
            return;
        }

        int retired = context.Content.RowsFor("tier").Count();
        string detail = retired == 0 ? "has no tier rows" : "has " + retired + " tier row(s), all retired";
        issues.Add(Issue(
            "the set " + detail + ", so it has no active tier. Add a tier, or set isActive true on one.",
            "tier",
            null,
            "/entities/tier"));
    }
}
