using Product.Engine.Configuration.Model;
using Product.Engine.Configuration.Validation.Rules;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Checks;

// Engine check standard-model.type (document 25, sections 5.11 and 5.13, OP-12 rule 1): standard change models belong
// to a change type that is not emergency. A model stands in for a person's authorization, so it must never apply to
// a change whose authorization may follow the change.
// The engine knows an emergency type by its behavior column, not by a key or a label: a change type is an emergency
// type when allowsAuthorizationAfter is true ("True when authorization may follow the change, as for emergencies",
// document 21, C20). V25 reads the same column for the emergency rule.
// Only active models count. A model whose change type is missing or retired is V06's and V07's finding.
// WORKING DEFAULT (open design question 8): a standard change model is how the engine accepts a change
// authorization without a card, so the locked list never reaches an emergency change.
public sealed class StandardModelTypeCheck : EngineCheck, IContentRule
{
    public static readonly StandardModelTypeCheck Instance = new();

    private StandardModelTypeCheck()
        : base(ReasonCodes.StandardModelType, "Standard change models are never for emergencies")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        ConfigurationContent content = context.Content;
        foreach (ContentRow model in RowFacts.Active(content, "standard-change-model"))
        {
            string? changeTypeKey = RowFacts.Text(model, "changeTypeKey");
            if (changeTypeKey == null || !content.TryFindRow("change-type", changeTypeKey, out ContentRow? changeType))
            {
                continue;
            }

            if (!RowFacts.IsTrue(changeType, "allowsAuthorizationAfter"))
            {
                continue;
            }

            issues.Add(Issue(
                "Standard Change Model " + Q(model.KeyPath) + " belongs to change type " + Q(changeTypeKey)
                + ", which allows authorization after the change, so it is an emergency type. A standard change model never stands in for the authorization of an emergency change. "
                + "Move the model to a change type that does not allow authorization after, or retire it.",
                "standard-change-model",
                model.KeyPath,
                model.Pointer + "/changeTypeKey"));
        }
    }
}
