using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Model;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V16: the engine meaning of a status, stage or route that any operational record uses never changes;
// add a new row instead. It compares the active version with the candidate for the rows the facts say records use.
// Actor kinds carry an engine meaning too and are stamped on every record, so they are checked the same way.
public sealed class V16MeaningChangedInUse : ValidationRule, IContentRule
{
    public static readonly V16MeaningChangedInUse Instance = new();

    private static readonly HashSet<string> MeaningEntities = new(StringComparer.Ordinal) { "status", "lifecycle-stage", "deployment-route", "actor-kind" };

    private V16MeaningChangedInUse()
        : base("V16", ReasonCodes.MeaningChangedInUse, "Meanings of rows in use never change")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        if (context.Active == null)
        {
            return;
        }

        foreach (RowIdentity used in context.RowsInUse
            .Where(r => MeaningEntities.Contains(r.Entity))
            .OrderBy(r => r.Entity, StringComparer.Ordinal)
            .ThenBy(r => r.KeyPath, StringComparer.Ordinal))
        {
            // A removed row is V08's case.
            if (!context.Active.TryFindRow(used.Entity, used.KeyPath, out ContentRow? before)
                || !context.Content.TryFindRow(used.Entity, used.KeyPath, out ContentRow? after))
            {
                continue;
            }

            string? was = RowFacts.Text(before, "engineMeaning");
            string? now = RowFacts.Text(after, "engineMeaning");
            if (was == null || now == null || string.Equals(was, now, StringComparison.Ordinal))
            {
                continue;
            }

            issues.Add(Issue(
                after.LogicalName + " " + Q(after.KeyPath) + " is used by operational records, and its engine meaning changes from " + Q(was) + " to " + Q(now)
                + ". Records keep the meaning they were judged by: keep this row's meaning, and add a new row with the new meaning instead.",
                after.Entity,
                after.KeyPath,
                after.Pointer));
        }
    }
}
