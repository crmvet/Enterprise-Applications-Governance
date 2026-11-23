using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Model;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V15: each status-bearing entity has at least one active status per engine meaning in its contract,
// and exactly one default per meaning. One active label for a meaning is its default by implication (C05).
// WORKING DEFAULT (open design question 1): the meanings per entity are the fixed engine meanings.
public sealed class V15StatusMeanings : ValidationRule, IContentRule
{
    public static readonly V15StatusMeanings Instance = new();

    private V15StatusMeanings()
        : base("V15", ReasonCodes.MeaningMissing, "Every status meaning has an active label and one default")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        ContentRow[] statuses = RowFacts.Active(context.Content, "status").ToArray();
        foreach (KeyValuePair<string, IContractList> entity in EngineContract.StatusMeaningListsByEntity.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            ContentRow[] forEntity = statuses.Where(s => string.Equals(RowFacts.Text(s, "appliesToEntity"), entity.Key, StringComparison.Ordinal)).ToArray();
            foreach (string meaning in entity.Value.WireValues)
            {
                ContentRow[] labels = forEntity.Where(s => string.Equals(RowFacts.Text(s, "engineMeaning"), meaning, StringComparison.Ordinal)).ToArray();
                if (labels.Length == 0)
                {
                    issues.Add(Issue(
                        "the set has no active Status for " + Q(entity.Key) + " with the engine meaning " + Q(meaning) + ". Add a status row for it, or set isActive true on one.",
                        "status",
                        null,
                        "/entities/status"));
                    continue;
                }

                if (labels.Length == 1)
                {
                    continue;
                }

                ContentRow[] defaults = labels.Where(s => RowFacts.IsTrue(s, "isDefaultForMeaning")).ToArray();
                if (defaults.Length == 1)
                {
                    continue;
                }

                string names = string.Join(", ", labels.Select(s => Q(s.KeyPath)));
                string problem = defaults.Length == 0
                    ? "none of them is marked isDefaultForMeaning"
                    : defaults.Length + " of them are marked isDefaultForMeaning";
                ContentRow pointAt = defaults.Length > 1 ? defaults[1] : labels[1];
                issues.Add(Issue(
                    "Statuses " + names + " of " + Q(entity.Key) + " share the engine meaning " + Q(meaning) + ", and " + problem + ". Mark exactly one as the default.",
                    "status",
                    pointAt.KeyPath,
                    pointAt.Pointer));
            }
        }
    }
}
