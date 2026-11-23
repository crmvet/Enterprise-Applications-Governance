using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Model;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V31 (safety floor): supports rows name no holder; no AI-only construct holds accountable or responsible.
// Supports is the letter for AI drafting, so it never has a holder (V10 checks the same field shape).
// In configuration the non-human holders are the workload roles: no person holds them, so they never hold
// responsible or accountable. Document 21, section 5.2: an AI may hold no letter other than supports.
public sealed class V31DecisionRightAiLetter : ValidationRule, IContentRule
{
    public static readonly V31DecisionRightAiLetter Instance = new();

    private V31DecisionRightAiLetter()
        : base("V31", ReasonCodes.DecisionRightAiLetter, "AI only supports, never decides")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        Dictionary<string, RoleInfo> roles = RoleInfo.All(context.Content);
        string supports = EngineContract.DecisionRightLetters.ToWire(DecisionRightLetter.Supports);
        string responsible = EngineContract.DecisionRightLetters.ToWire(DecisionRightLetter.Responsible);
        string accountable = EngineContract.DecisionRightLetters.ToWire(DecisionRightLetter.Accountable);

        foreach (ContentRow right in RowFacts.Active(context.Content, "decision-right"))
        {
            string? letter = RowFacts.Text(right, "letter");
            if (string.Equals(letter, supports, StringComparison.Ordinal))
            {
                string[] holders = new[] { "ownerRoleKey", "roleKey" }.Where(f => RowFacts.Has(right, f)).ToArray();
                if (holders.Length > 0)
                {
                    issues.Add(Issue(
                        "Decision Right " + Q(right.KeyPath) + " has the letter supports, which only AI holds, and names a holder (" + string.Join(", ", holders.Select(Q)) + "). Remove the holder.",
                        right.Entity,
                        right.KeyPath,
                        right.Pointer));
                }

                continue;
            }

            if (!string.Equals(letter, responsible, StringComparison.Ordinal) && !string.Equals(letter, accountable, StringComparison.Ordinal))
            {
                continue;
            }

            string? roleKey = RowFacts.Text(right, "roleKey");
            if (roleKey != null && roles.TryGetValue(roleKey, out RoleInfo? role) && role.IsWorkload)
            {
                issues.Add(Issue(
                    "Decision Right " + Q(right.KeyPath) + " gives the letter " + Q(letter) + " to Role " + Q(roleKey) + ", a workload role that no person holds. "
                    + "No AI or workload holds responsible or accountable: name a person's role or an owner role.",
                    right.Entity,
                    right.KeyPath,
                    right.Pointer));
            }
        }
    }
}
