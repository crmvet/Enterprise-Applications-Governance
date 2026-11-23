using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V42 (concurrency): the draft's base version equals the active version at activation, or the draft was rebased and
// revalidated (document 21, section 4.5). It runs in activation mode only: a draft whose base moved may still be
// submitted, and activation then rebases it or refuses it. The versions are facts. Without them nothing is checked.
public sealed class V42BaseMoved : ValidationRule, IContentRule
{
    public static readonly V42BaseMoved Instance = new();

    private V42BaseMoved()
        : base("V42", ReasonCodes.ConfigBaseMoved, "Activation starts from the active version")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        int? baseVersion = context.BaseVersionNumber;
        int? active = context.Facts.ActiveVersionNumber;
        if (context.Mode != ValidationMode.Activation || !baseVersion.HasValue || !active.HasValue || context.Facts.IsRebased || baseVersion.Value == active.Value)
        {
            return;
        }

        issues.Add(Issue(
            "the draft starts from version " + baseVersion.Value + ", but version " + active.Value + " is active now. Rebase the draft on version " + active.Value
            + " and validate it again before activation. If an operation touches a row that changed in between, rebuild the draft instead.",
            null,
            null,
            null));
    }

    // The refusal when rebase finds an overlap (document 21, section 4.5; document 25, section 6.4: "refuses on overlap
    // (V42)"). The base moved and the draft cannot be rebased, so V42 stays broken. One issue per overlapping row.
    internal ValidationIssue Overlap(string message, string entityKey, string keyPath, string? location)
    {
        return Issue(message, entityKey, keyPath, location);
    }
}
