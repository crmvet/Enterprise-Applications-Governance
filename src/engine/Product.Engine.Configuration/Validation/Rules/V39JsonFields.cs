using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Json;
using Product.Engine.Configuration.Model;
using Product.Engine.Configuration.Schema;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V39: JSON fields match their sub-schemas: emergency rule, conditions, triggers, flag notify, setting values.
// Each field is checked alone against its definition in config-bundle.schema.json, so the message names the row
// and the field. The bundle check (V01) covers the same shapes as part of the whole document.
// A json setting is checked against setting.rules, which holds the value shape of each known setting.
// WORKING DEFAULT (open design question 9): the confirmation-channel shape says which act counts as the person's.
// WORKING DEFAULT (open design question 8): the engine-acceptable-decision-types shape is the locked list.
public sealed class V39JsonFields : ValidationRule, IContentRule
{
    public static readonly V39JsonFields Instance = new();

    private const string Defs = ConfigurationSchemas.BundleId + "#/$defs/";

    // Entity, field and the definition it must match.
    private static readonly string[][] Fields =
    {
        new[] { "gate-policy", "emergencyRule", "emergencyRule" },
        new[] { "gate-policy", "flagNotify", "notify" },
        new[] { "standard-change-model", "conditions", "standardChangeConditions" },
        new[] { "tier-answer-option", "triggers", "triggers" },
    };

    private V39JsonFields()
        : base("V39", ReasonCodes.JsonSchema, "JSON fields match their sub-schemas")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        foreach (string[] field in Fields)
        {
            foreach (ContentRow row in RowFacts.Active(context.Content, field[0]))
            {
                JsonTreeNode? value = JsonNodes.Get(row.Node, field[1]);
                if (value == null)
                {
                    continue;
                }

                SchemaResult result = ConfigurationSchemas.Set.Evaluate(value, Defs + field[2]);
                Report(row, field[1], result.Issues, prefix: string.Empty, issues);
            }
        }

        foreach (ContentRow setting in RowFacts.Active(context.Content, "setting"))
        {
            JsonTreeNode? value = JsonNodes.Get(setting.Node, "value");
            if (!string.Equals(RowFacts.Text(setting, "valueType"), "json", StringComparison.Ordinal) || value == null || value.Kind == JsonTreeKind.Null)
            {
                continue;
            }

            // setting.rules picks the value shape by key and value type. Only the value is judged here.
            JsonTreeNode probe = JsonTreeNode.CreateObject(
                JsonNodes.Member("key", setting.KeyPath),
                JsonNodes.Member("valueType", "json"),
                JsonNodes.Member("value", value));
            SchemaResult result = ConfigurationSchemas.Set.Evaluate(probe, Defs + "setting.rules");
            Report(setting, "value", result.Issues.Where(i => i.InstancePath.StartsWith("/value", StringComparison.Ordinal)), prefix: "/value", issues);
        }
    }

    private void Report(ContentRow row, string field, IEnumerable<SchemaIssue> problems, string prefix, ValidationIssues issues)
    {
        foreach (SchemaIssue problem in problems.Take(10))
        {
            string inner = problem.InstancePath.Substring(Math.Min(prefix.Length, problem.InstancePath.Length));
            string where = inner.Length == 0 ? string.Empty : " at " + inner;
            issues.Add(Issue(
                row.LogicalName + " " + Q(row.KeyPath) + " field " + Q(field) + where + ": " + problem.Message + ". Change it to match its sub-schema.",
                row.Entity,
                row.KeyPath,
                row.Pointer + "/" + field + inner));
        }
    }
}
