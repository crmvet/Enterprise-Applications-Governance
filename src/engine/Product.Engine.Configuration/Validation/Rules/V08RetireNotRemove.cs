using System.Linq;
using Product.Engine.Configuration.Model;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V08: a row referenced by any operational record or active configuration row can only be retired, never removed.
//
// It compares the active version with the candidate. A row of the active version that the candidate lacks
// was removed. It is in use when an active row of either version points to it, or when the facts say an
// operational record uses it. Patches cannot remove rows (there is no delete operation, document 21, rule 7),
// so this fires when a whole bundle replaces the active version, such as a re-applied preset or an import.
public sealed class V08RetireNotRemove : ValidationRule, IContentRule
{
    public static readonly V08RetireNotRemove Instance = new();

    private V08RetireNotRemove()
        : base("V08", ReasonCodes.RefInUse, "Rows in use are retired, not removed")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        if (context.Active == null)
        {
            return;
        }

        foreach (ContentRow removed in context.Active.Rows)
        {
            if (removed.Key == null || context.Content.TryFindRow(removed.Entity, removed.KeyPath, out _))
            {
                continue;
            }

            string? user = null;
            RowReference? byActive = context.ActiveReferences?.To(removed.Entity, removed.KeyPath).FirstOrDefault(r => r.Source.IsEffectivelyActive);
            RowReference? byCandidate = context.References.To(removed.Entity, removed.KeyPath).FirstOrDefault(r => r.Source.IsEffectivelyActive);
            if (byActive != null)
            {
                user = "active " + byActive.Source.LogicalName + " " + Q(byActive.Source.KeyPath);
            }
            else if (byCandidate != null)
            {
                user = "active " + byCandidate.Source.LogicalName + " " + Q(byCandidate.Source.KeyPath);
            }
            else if (context.RowsInUse.Contains(new RowIdentity(removed.Entity, removed.KeyPath)))
            {
                user = "an operational record";
            }

            if (user == null)
            {
                continue;
            }

            issues.Add(Issue(
                removed.LogicalName + " " + Q(removed.KeyPath) + " was removed, but " + user + " uses it. Rows in use are never removed: keep the row and retire it (isActive false) instead.",
                removed.Entity,
                removed.KeyPath,
                removed.Pointer));
        }
    }
}
