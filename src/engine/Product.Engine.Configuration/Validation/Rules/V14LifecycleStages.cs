using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Json;
using Product.Engine.Configuration.Model;
using Product.Engine.Configuration.Schema;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V14: at least one active lifecycle stage per lifecycle meaning (pre-live, live, retired);
// stage numbers and stage ranks are unique. Uniqueness is checked among active stages, as V12 does for tier ranks:
// a retired stage keeps its numbers so old records still resolve.
public sealed class V14LifecycleStages : ValidationRule, IContentRule
{
    public static readonly V14LifecycleStages Instance = new();

    private V14LifecycleStages()
        : base("V14", ReasonCodes.StageMeaningCoverage, "Stages cover each meaning, with unique numbers and ranks")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        ContentRow[] stages = RowFacts.Active(context.Content, "lifecycle-stage").ToArray();
        foreach (string meaning in EngineContract.LifecycleMeanings.WireValues)
        {
            if (!stages.Any(s => string.Equals(RowFacts.Text(s, "engineMeaning"), meaning, StringComparison.Ordinal)))
            {
                issues.Add(Issue(
                    "the set has no active Lifecycle Stage with the engine meaning " + Q(meaning) + ". Add one, or set isActive true on one.",
                    "lifecycle-stage",
                    null,
                    "/entities/lifecycle-stage"));
            }
        }

        Unique(stages, "stageNumber", "stage number", ReasonCodes.StageMeaningCoverage, issues);
        Unique(stages, "stageRank", "stage rank", ReasonCodes.StageRankDuplicate, issues);
    }

    private void Unique(ContentRow[] stages, string field, string words, string code, ValidationIssues issues)
    {
        var seen = new Dictionary<string, ContentRow>(StringComparer.Ordinal);
        foreach (ContentRow stage in stages)
        {
            JsonTreeNode? value = JsonNodes.Get(stage.Node, field);
            if (value == null || !JsonSchemaSet.IsInteger(value))
            {
                // V01 reports a missing or broken number.
                continue;
            }

            string number = value.GetNumberText();
            if (seen.TryGetValue(number, out ContentRow? first))
            {
                issues.Add(IssueWithCode(
                    code,
                    "active Lifecycle Stages " + Q(first.KeyPath) + " and " + Q(stage.KeyPath) + " share " + words + " " + number + ". Give each active stage its own " + words + ".",
                    stage.Entity,
                    stage.KeyPath,
                    stage.Pointer));
                continue;
            }

            seen.Add(number, stage);
        }
    }
}
