using System;
using System.Linq;
using Product.Engine.Configuration.Model;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V13: each active tier question has at least two active answer options, and every implied tier is active.
// A question with one answer cannot tell tiers apart. V07 also reports an active answer that points at a retired
// tier. V13 says what it means for the tier questions.
public sealed class V13TierQuestionOptions : ValidationRule, IContentRule
{
    public static readonly V13TierQuestionOptions Instance = new();

    private V13TierQuestionOptions()
        : base("V13", ReasonCodes.TierQuestionOptions, "Tier questions have options and imply active tiers")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        foreach (ContentRow question in RowFacts.Active(context.Content, "tier-question"))
        {
            ContentRow[] options = context.Content.RowsFor("tier-answer-option")
                .Where(o => o.Key != null && o.IsEffectivelyActive && string.Equals(o.ParentKey, question.Key, StringComparison.Ordinal))
                .ToArray();
            if (options.Length < 2)
            {
                issues.Add(Issue(
                    "Tier Question " + Q(question.KeyPath) + " has " + options.Length + " active answer option" + (options.Length == 1 ? string.Empty : "s")
                    + ". An active question needs at least two: add an answer option, or retire the question.",
                    question.Entity,
                    question.KeyPath,
                    question.Pointer));
            }

            foreach (ContentRow option in options)
            {
                string? tier = RowFacts.Text(option, "impliedTierKey");
                if (tier != null && context.Content.TryFindRow("tier", tier, out ContentRow? tierRow) && !tierRow.IsActive)
                {
                    issues.Add(Issue(
                        "Tier Answer Option " + Q(option.KeyPath) + " implies Tier " + Q(tier) + ", which is retired. Point the answer at an active tier, or retire the answer.",
                        option.Entity,
                        option.KeyPath,
                        option.Pointer));
                }
            }
        }
    }
}
