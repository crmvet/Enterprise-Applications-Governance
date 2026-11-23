using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Json;
using Product.Engine.Configuration.Model;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V10: exactly-one fields hold.
// A gate requirement sets the one target its kind needs; an approval rule names exactly one approver
// (owner role or role); a decision right names exactly one holder, or none for supports.
public sealed class V10ExactlyOne : ValidationRule, IContentRule
{
    public static readonly V10ExactlyOne Instance = new();

    // The target field each requirement kind needs (document 21, C14; the four kinds of the requirement-kind contract).
    // WORKING DEFAULT (open design question 1): requirement kinds are fixed engine meanings.
    private static readonly Dictionary<RequirementKind, string> TargetByKind = new()
    {
        { RequirementKind.DecisionAccepted, "requiredDecisionTypeKey" },
        { RequirementKind.DocumentCurrent, "requiredDocumentTypeKey" },
        { RequirementKind.ReviewDone, "requiredReviewTypeKey" },
        { RequirementKind.OwnerAssigned, "requiredOwnerRoleKey" },
    };

    private V10ExactlyOne()
        : base("V10", ReasonCodes.RefExactlyOne, "Exactly-one fields hold")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        foreach (ContentRow row in context.Content.RowsFor("approval-rule"))
        {
            CheckOneOf(row, "an approver", new[] { "approverOwnerRoleKey", "approverRoleKey" }, issues);
        }

        foreach (ContentRow row in context.Content.RowsFor("decision-right"))
        {
            string? letter = JsonNodes.GetString(row.Node, "letter");
            if (letter != null && EngineContract.DecisionRightLetters.TryParse(letter, out DecisionRightLetter parsed) && parsed == DecisionRightLetter.Supports)
            {
                string[] holders = Present(row, "ownerRoleKey", "roleKey");
                if (holders.Length > 0)
                {
                    issues.Add(Issue(
                        "Decision Right " + Q(row.KeyPath) + " has the letter supports and names a holder (" + string.Join(", ", holders) + "). A supports row names no holder: remove the holder.",
                        row.Entity,
                        row.KeyPath,
                        row.Pointer));
                }

                continue;
            }

            CheckOneOf(row, "a holder", new[] { "ownerRoleKey", "roleKey" }, issues);
        }

        foreach (ContentRow row in context.Content.RowsFor("gate-requirement"))
        {
            string? kind = JsonNodes.GetString(row.Node, "requirementKind");
            if (kind == null || !EngineContract.RequirementKinds.TryParse(kind, out RequirementKind parsed))
            {
                // V01 reports a missing or unknown kind.
                continue;
            }

            string needed = TargetByKind[parsed];
            if (!JsonNodes.HasMember(row.Node, needed))
            {
                issues.Add(Issue(
                    "Gate Requirement " + Q(row.KeyPath) + " of kind " + Q(kind) + " has no " + Q(needed) + ". Set it to the row the requirement checks.",
                    row.Entity,
                    row.KeyPath,
                    row.Pointer));
            }

            string[] extra = TargetByKind.Values.Where(field => !string.Equals(field, needed, StringComparison.Ordinal) && JsonNodes.HasMember(row.Node, field)).ToArray();
            if (extra.Length > 0)
            {
                issues.Add(Issue(
                    "Gate Requirement " + Q(row.KeyPath) + " of kind " + Q(kind) + " also sets " + string.Join(", ", extra.Select(Q)) + ", which that kind does not use. Remove " + (extra.Length == 1 ? "it" : "them") + ".",
                    row.Entity,
                    row.KeyPath,
                    row.Pointer));
            }
        }
    }

    private void CheckOneOf(ContentRow row, string what, string[] fields, ValidationIssues issues)
    {
        string[] present = Present(row, fields);
        if (present.Length == 1)
        {
            return;
        }

        string choice = string.Join(" and ", fields.Select(Q));
        string message = present.Length == 0
            ? row.LogicalName + " " + Q(row.KeyPath) + " names no " + what.Substring(what.IndexOf(' ') + 1) + ". Set exactly one of " + choice + "."
            : row.LogicalName + " " + Q(row.KeyPath) + " names more than one " + what.Substring(what.IndexOf(' ') + 1) + " (" + string.Join(", ", present.Select(Q)) + "). Keep exactly one of " + choice + ".";
        issues.Add(Issue(message, row.Entity, row.KeyPath, row.Pointer));
    }

    private static string[] Present(ContentRow row, params string[] fields)
    {
        return fields.Where(field => JsonNodes.HasMember(row.Node, field)).ToArray();
    }
}
