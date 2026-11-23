using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Json;
using Product.Engine.Configuration.Model;
using Product.Engine.Configuration.Validation;
using Product.Engine.Configuration.Validation.Rules;
using Product.Engine.Core.Canonical;

namespace Product.Engine.Configuration.Wildcards;

// Expands wildcard rows (document 21, section 8.4).
//
// - "*" is allowed only in the scope fields of Review Cadence (tierKey) and Gate Policy (tierKey, changeTypeKey,
//   targetStageKey). A wildcard row becomes one row per active target row.
// - The expanded key is "<row key>--<target key>", with the target keys of several wildcards joined by "--"
//   in field order. Expanded keys must still pass V02, so wildcard rows keep short keys.
// - Explicit beats wildcard: a combination given by a row with more explicit scope fields is not produced
//   by a broader row. Two rows with the same number of explicit fields that produce the same combination are
//   a V09 error. Rows of other scopes (another review type, a portfolio) never compete.
// - A retired wildcard row expands into retired rows, so its keys stay taken. It never competes.
// Rows without a wildcard stay as they are. Running the expander twice changes nothing.
internal static class WildcardExpander
{
    public const string Wildcard = "*";
    public const string KeySeparator = "--";

    public static void Expand(ContentBuilder content, ValidationIssues issues)
    {
        foreach (ConfigurationEntity entity in ConfigurationEntities.All.Where(e => e.WildcardFields.Count > 0))
        {
            if (content.HasEntity(entity.Key))
            {
                ExpandEntity(content, entity, issues);
            }
        }
    }

    // True when the row holds "*" in one of its entity's wildcard fields.
    public static bool HasWildcard(ConfigurationEntity entity, JsonTreeNode row)
    {
        return entity.WildcardFields.Any(f => string.Equals(JsonNodes.GetString(row, f.Field), Wildcard, StringComparison.Ordinal));
    }

    private static void ExpandEntity(ContentBuilder content, ConfigurationEntity entity, ValidationIssues issues)
    {
        List<JsonTreeNode> rows = content.Rows(entity.Key);

        // Active target keys per wildcard field, in the order the target rows appear.
        var targets = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (WildcardField field in entity.WildcardFields)
        {
            targets[field.Field] = content.HasEntity(field.TargetEntity)
                ? content.Rows(field.TargetEntity)
                    .Where(r => r.Kind == JsonTreeKind.Object && JsonNodes.IsActive(r) && JsonNodes.GetString(r, "key") != null)
                    .Select(r => JsonNodes.GetString(r, "key")!)
                    .ToArray()
                : Array.Empty<string>();
        }

        // Every combination each row produces, and how explicit the row is.
        var plans = new List<RowPlan>();
        for (int i = 0; i < rows.Count; i++)
        {
            JsonTreeNode row = rows[i];
            if (row.Kind != JsonTreeKind.Object)
            {
                plans.Add(new RowPlan(i, row, null, false, 0, new List<string[]>()));
                continue;
            }

            string? key = JsonNodes.GetString(row, "key");
            bool wild = HasWildcard(entity, row);
            int explicitCount = entity.WildcardFields.Count(f => !string.Equals(JsonNodes.GetString(row, f.Field), Wildcard, StringComparison.Ordinal));
            plans.Add(new RowPlan(i, row, key, wild, explicitCount, Combinations(entity, row, targets)));
        }

        // Which active rows win each combination within a scope.
        var winners = new Dictionary<string, List<RowPlan>>(StringComparer.Ordinal);
        foreach (RowPlan plan in plans.Where(p => p.Key != null && JsonNodes.IsActive(p.Row)))
        {
            foreach (string[] combination in plan.Combinations)
            {
                string identity = Scope(entity, plan.Row) + "\u0001" + string.Join("\u0000", combination);
                if (!winners.TryGetValue(identity, out List<RowPlan>? best) || best[0].ExplicitCount < plan.ExplicitCount)
                {
                    winners[identity] = new List<RowPlan> { plan };
                }
                else if (best[0].ExplicitCount == plan.ExplicitCount)
                {
                    best.Add(plan);
                }
            }
        }

        // Report each pair of equally explicit rows once, with the first combination they share.
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, List<RowPlan>> pair in winners)
        {
            if (pair.Value.Count < 2)
            {
                continue;
            }

            for (int j = 1; j < pair.Value.Count; j++)
            {
                RowPlan first = pair.Value[0];
                RowPlan second = pair.Value[j];
                if (!reported.Add(first.Key + "\u0000" + second.Key))
                {
                    continue;
                }

                string[] combination = pair.Key.Substring(pair.Key.IndexOf('\u0001') + 1).Split('\u0000');
                V09Wildcards.Instance.Overlap(entity.Key, first.Key!, second.Key!, Describe(entity, combination), issues);
            }
        }

        var expanded = new List<JsonTreeNode>();
        foreach (RowPlan plan in plans)
        {
            if (!plan.IsWild || plan.Key == null)
            {
                expanded.Add(plan.Row);
                continue;
            }

            bool active = JsonNodes.IsActive(plan.Row);

            // An active wildcard row that wins no combination would vanish without a trace. Say so.
            if (active && !winners.Values.Any(best => best.Contains(plan)))
            {
                V09Wildcards.Instance.Shadowed(entity.Key, plan.Key, plan.Combinations.Count, issues);
            }
            foreach (string[] combination in plan.Combinations)
            {
                if (active)
                {
                    string identity = Scope(entity, plan.Row) + "\u0001" + string.Join("\u0000", combination);
                    if (!winners.TryGetValue(identity, out List<RowPlan>? best) || !best.Contains(plan))
                    {
                        continue;
                    }
                }

                JsonTreeNode row = plan.Row;
                var suffix = new List<string>();
                for (int f = 0; f < entity.WildcardFields.Count; f++)
                {
                    WildcardField field = entity.WildcardFields[f];
                    if (string.Equals(JsonNodes.GetString(plan.Row, field.Field), Wildcard, StringComparison.Ordinal))
                    {
                        row = JsonNodes.With(row, field.Field, JsonTreeNode.FromString(combination[f]));
                        suffix.Add(combination[f]);
                    }
                }

                string newKey = plan.Key + KeySeparator + string.Join(KeySeparator, suffix);
                expanded.Add(JsonNodes.With(row, "key", JsonTreeNode.FromString(newKey)));
                if (content.IsTouched(entity.Key, plan.Key))
                {
                    content.MarkTouched(entity.Key, newKey);
                }
            }
        }

        rows.Clear();
        rows.AddRange(expanded);
    }

    // Every combination of the wildcard fields a row covers. A field that is missing or not a string counts
    // as one explicit value, so a broken row still yields one combination and V01 reports the field.
    private static List<string[]> Combinations(ConfigurationEntity entity, JsonTreeNode row, Dictionary<string, string[]> targets)
    {
        var result = new List<string[]> { Array.Empty<string>() };
        foreach (WildcardField field in entity.WildcardFields)
        {
            JsonTreeNode? value = JsonNodes.Get(row, field.Field);
            string[] options;
            if (value != null && value.Kind == JsonTreeKind.String && value.GetString() == Wildcard)
            {
                options = targets[field.Field];
            }
            else
            {
                options = new[] { value == null ? string.Empty : value.Kind == JsonTreeKind.String ? value.GetString() : value.ToString() };
            }

            result = result.SelectMany(prefix => options.Select(option => prefix.Concat(new[] { option }).ToArray())).ToList();
        }

        return result;
    }

    // The fields outside the wildcard fields that keep two rows apart, such as the review type.
    private static string Scope(ConfigurationEntity entity, JsonTreeNode row)
    {
        return string.Join("\u0000", entity.GroupFields.Select(field => JsonNodes.Get(row, field)?.ToString() ?? string.Empty));
    }

    private static IEnumerable<string> Describe(ConfigurationEntity entity, string[] combination)
    {
        for (int i = 0; i < entity.WildcardFields.Count && i < combination.Length; i++)
        {
            WildcardField field = entity.WildcardFields[i];
            yield return ConfigurationEntities.LogicalNameOf(field.TargetEntity).ToLowerInvariant() + " `" + combination[i] + "`";
        }
    }

    private sealed class RowPlan
    {
        public RowPlan(int index, JsonTreeNode row, string? key, bool isWild, int explicitCount, List<string[]> combinations)
        {
            Index = index;
            Row = row;
            Key = key;
            IsWild = isWild;
            ExplicitCount = explicitCount;
            Combinations = combinations;
        }

        public int Index { get; }

        public JsonTreeNode Row { get; }

        public string? Key { get; }

        public bool IsWild { get; }

        public int ExplicitCount { get; }

        public List<string[]> Combinations { get; }
    }
}
