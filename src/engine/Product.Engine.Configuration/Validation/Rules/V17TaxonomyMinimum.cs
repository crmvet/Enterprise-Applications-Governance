using System;
using System.Linq;
using Product.Engine.Configuration.Model;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V17: at least one active change type, one target stage, one deployment route with meaning pipeline step,
// one actor kind per actor meaning, and one link kind per link category.
public sealed class V17TaxonomyMinimum : ValidationRule, IContentRule
{
    public static readonly V17TaxonomyMinimum Instance = new();

    private V17TaxonomyMinimum()
        : base("V17", ReasonCodes.TaxonomyMinimum, "The minimum taxonomy rows exist")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        ConfigurationContent content = context.Content;
        RequireAny(content, "change-type", "Add one, such as `normal`.", issues);
        RequireAny(content, "target-stage", "Add one, such as `production`.", issues);

        string pipeline = EngineContract.DeploymentRouteMeanings.ToWire(DeploymentRouteMeaning.PipelineStep);
        RequireValue(content, "deployment-route", "engineMeaning", pipeline, "the engine meaning", issues);

        foreach (string meaning in EngineContract.ActorKindMeanings.WireValues)
        {
            RequireValue(content, "actor-kind", "engineMeaning", meaning, "the engine meaning", issues);
        }

        foreach (string category in EngineContract.LinkCategories.WireValues)
        {
            RequireValue(content, "link-kind", "linkCategory", category, "the link category", issues);
        }
    }

    private void RequireAny(ConfigurationContent content, string entity, string fix, ValidationIssues issues)
    {
        if (!RowFacts.Active(content, entity).Any())
        {
            issues.Add(Issue("the set has no active " + ConfigurationEntities.LogicalNameOf(entity) + ". " + fix, entity, null, "/entities/" + entity));
        }
    }

    private void RequireValue(ConfigurationContent content, string entity, string field, string value, string words, ValidationIssues issues)
    {
        if (!RowFacts.Active(content, entity).Any(r => string.Equals(RowFacts.Text(r, field), value, StringComparison.Ordinal)))
        {
            issues.Add(Issue(
                "the set has no active " + ConfigurationEntities.LogicalNameOf(entity) + " with " + words + " " + Q(value) + ". Add one, or set isActive true on one.",
                entity,
                null,
                "/entities/" + entity));
        }
    }
}
