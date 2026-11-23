using Product.Engine.Configuration.Json;
using Product.Engine.Configuration.Model;
using Product.Engine.Configuration.Validation.Rules;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Checks;

// Engine check standard-model.condition-kind (document 25, sections 5.11 and 5.13, OP-12 rule 3): every condition in
// Conditions Json uses a condition kind the engine knows. The list is the engine contract list `condition-kind`
// (EngineContract.ConditionKinds), not the JSON Schema: V01 and V39 check the shape against the schema's copy of the
// list, and this check holds even if the two ever drift apart. OP-12 can only evaluate a kind it knows, so an unknown
// or missing kind would make the model never qualify without saying why.
// Only active models count. Conditions that are not an array are V01's and V39's finding.
// WORKING DEFAULT (open design question 1): the condition kinds are part of the short list of fixed engine meanings.
public sealed class StandardModelConditionKindCheck : EngineCheck, IContentRule
{
    public static readonly StandardModelConditionKindCheck Instance = new();

    private StandardModelConditionKindCheck()
        : base(ReasonCodes.StandardModelConditionKind, "Standard change conditions use known condition kinds")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        string known = string.Join(", ", EngineContract.ConditionKinds.WireValues);
        foreach (ContentRow model in RowFacts.Active(context.Content, "standard-change-model"))
        {
            JsonTreeNode? conditions = JsonNodes.Get(model.Node, "conditions");
            if (conditions == null || conditions.Kind != JsonTreeKind.Array)
            {
                continue;
            }

            for (int i = 0; i < conditions.Items.Count; i++)
            {
                JsonTreeNode condition = conditions.Items[i];
                string? kind = condition.Kind == JsonTreeKind.Object ? JsonNodes.GetString(condition, "conditionKind") : null;
                if (EngineContract.ConditionKinds.IsDefined(kind))
                {
                    continue;
                }

                string what = kind == null ? "has no condition kind" : "uses the condition kind " + Q(kind) + ", which the engine does not know";
                issues.Add(Issue(
                    "Standard Change Model " + Q(model.KeyPath) + " condition " + i + " " + what + ", so OP-12 could never evaluate it. "
                    + "Set conditionKind to one of " + known + ".",
                    "standard-change-model",
                    model.KeyPath,
                    model.Pointer + "/conditions/" + i));
            }
        }
    }
}
