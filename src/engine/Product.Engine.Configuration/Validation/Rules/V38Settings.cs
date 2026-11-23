using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Json;
using Product.Engine.Configuration.Model;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V38: every setting in document 21, section 3.6 exists exactly once without a portfolio, and every setting value
// matches its value type. A row with narrowsSettingKey narrows only change-management-mode, sets a portfolio and has
// the same value type (28, issue 15 and Q2). The engine never falls back to a built-in value (document 21, rule 8).
// A value may be null only where the setting says what null means: the hub URL until setup, and audit retention
// (forever on Dataverse). A setting a client added is not read by the engine, so its null is allowed.
public sealed class V38Settings : ValidationRule, IContentRule
{
    public static readonly V38Settings Instance = new();

    // Every setting of document 21, section 3.6, with its value type. Four rows rest on open questions:
    // WORKING DEFAULT (open design question 7): default-ai-only-code-approval-handling.
    // WORKING DEFAULT (open design question 8): engine-acceptable-decision-types.
    // WORKING DEFAULT (open design question 9): confirmation-channel.
    // WORKING DEFAULT (open design question 12): workload-grant-max-days.
    internal static readonly KeyValuePair<string, string>[] Required =
    {
        new("change-management-mode", "contract:change-management-mode"),
        new("ai-approval-lock", "yes-no"),
        new("engine-acceptable-decision-types", "json"),
        new("reminder-channel", "key:list-value:channel"),
        new("guideline-hub-base-url", "text"),
        new("draft-expiry-days", "number"),
        new("configuration-review-days", "number"),
        new("default-flag-notify", "json"),
        new("default-ai-only-code-approval-handling", "contract:ai-only-code-approval-handling"),
        new("confirmation-channel", "json"),
        new("unlinked-deployment-change-type", "key:change-type"),
        new("intake-visibility", "contract:intake-visibility"),
        new("claim-visibility", "contract:claim-visibility"),
        new("record-access-mode", "contract:record-access-mode"),
        new("agent-read-access", "yes-no"),
        new("principal-refresh-days", "number"),
        new("forbidden-url-patterns", "json"),
        new("audit-retention-days", "number"),
        new("link-undo-minutes", "number"),
        new("workload-grant-max-days", "number"),
        new("impact-max-depth", "number"),
        new("mcp-page-size-max", "number"),
        new("engine-read-cache-seconds", "number"),
        new("working-days", "json"),
    };

    private static readonly HashSet<string> Nullable = new(StringComparer.Ordinal) { "guideline-hub-base-url", "audit-retention-days" };

    private V38Settings()
        : base("V38", ReasonCodes.SettingRequired, "Every setting exists once, with the right value type")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        ContentRow[] settings = RowFacts.Active(context.Content, "setting").ToArray();
        ContentRow[] plain = settings.Where(s => !RowFacts.Has(s, "narrowsSettingKey") && !RowFacts.Has(s, "portfolioId")).ToArray();
        var known = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> spec in Required)
        {
            known.Add(spec.Key, spec.Value);
            ContentRow? row = plain.FirstOrDefault(s => string.Equals(s.KeyPath, spec.Key, StringComparison.Ordinal));
            if (row == null)
            {
                issues.Add(Issue(
                    "the set has no active Setting " + Q(spec.Key) + " without a portfolio. Every setting of document 21, section 3.6 exists once: add it with valueType " + Q(spec.Value) + ".",
                    "setting",
                    spec.Key,
                    "/entities/setting"));
                continue;
            }

            string? valueType = RowFacts.Text(row, "valueType");
            if (valueType != null && !string.Equals(valueType, spec.Value, StringComparison.Ordinal))
            {
                issues.Add(Issue(
                    "Setting " + Q(spec.Key) + " has valueType " + Q(valueType) + ", but it must be " + Q(spec.Value) + ". Fix the value type and the value.",
                    row.Entity,
                    row.KeyPath,
                    row.Pointer));
            }
        }

        foreach (ContentRow row in settings)
        {
            CheckValue(row, known.ContainsKey(row.KeyPath), issues);
            CheckNarrowing(row, settings, issues);
        }
    }

    private void CheckValue(ContentRow row, bool isKnown, ValidationIssues issues)
    {
        string? valueType = RowFacts.Text(row, "valueType");
        JsonTreeNode? value = JsonNodes.Get(row.Node, "value");
        if (valueType == null || value == null)
        {
            // V01 reports a missing required field.
            return;
        }

        if (value.Kind == JsonTreeKind.Null)
        {
            if (isKnown && !Nullable.Contains(row.KeyPath))
            {
                issues.Add(Issue(
                    "Setting " + Q(row.KeyPath) + " has no value (null). Only " + string.Join(" and ", Nullable.OrderBy(k => k, StringComparer.Ordinal).Select(Q)) + " may be null: set a value of type " + Q(valueType) + ".",
                    row.Entity,
                    row.KeyPath,
                    row.Pointer));
            }

            return;
        }

        string? problem = Mismatch(valueType, value);
        if (problem != null)
        {
            issues.Add(Issue("Setting " + Q(row.KeyPath) + " " + problem + ".", row.Entity, row.KeyPath, row.Pointer));
        }
    }

    // Null when the value fits the value type, else what is wrong and the fix.
    private static string? Mismatch(string valueType, JsonTreeNode value)
    {
        string kind = valueType;
        string? argument = null;
        int colon = valueType.IndexOf(':');
        if (colon >= 0)
        {
            kind = valueType.Substring(0, colon);
            argument = valueType.Substring(colon + 1);
        }

        switch (kind)
        {
            case "text":
                return value.Kind == JsonTreeKind.String ? null : "has valueType `text` but its value is not text. Give it a text value";
            case "number":
                return value.Kind == JsonTreeKind.Number ? null : "has valueType `number` but its value is not a number. Give it a number";
            case "yes-no":
                return value.Kind == JsonTreeKind.True || value.Kind == JsonTreeKind.False ? null : "has valueType `yes-no` but its value is not true or false. Give it true or false";
            case "json":
                return value.Kind == JsonTreeKind.Object || value.Kind == JsonTreeKind.Array ? null : "has valueType `json` but its value is not an object or an array. Give it an object or an array";
            case "key":
                return value.Kind == JsonTreeKind.String ? null : "has valueType `" + valueType + "` but its value is not a key. Give it the key of a row";
            case "contract":
                if (!EngineContract.TryGetList(argument, out IContractList? list))
                {
                    return "has valueType `" + valueType + "`, which names no engine contract list. Use a known contract name";
                }

                return value.Kind == JsonTreeKind.String && list.IsDefined(value.GetString())
                    ? null
                    : "has the value " + value + ", which is not one of " + string.Join(", ", list.WireValues.Select(w => "`" + w + "`")) + ". Use one of them";
            default:
                return "has the unknown valueType `" + valueType + "`. Use text, number, yes-no, json, key:<entity> or contract:<name>";
        }
    }

    private void CheckNarrowing(ContentRow row, ContentRow[] settings, ValidationIssues issues)
    {
        string? narrows = RowFacts.Text(row, "narrowsSettingKey");
        bool hasPortfolio = RowFacts.Has(row, "portfolioId");
        if (narrows == null)
        {
            if (hasPortfolio)
            {
                issues.Add(Issue(
                    "Setting " + Q(row.KeyPath) + " sets a portfolio but narrows no setting. Set narrowsSettingKey to " + Q(WellKnown.ChangeManagementMode) + ", or remove portfolioId.",
                    row.Entity,
                    row.KeyPath,
                    row.Pointer));
            }

            return;
        }

        if (!string.Equals(narrows, WellKnown.ChangeManagementMode, StringComparison.Ordinal))
        {
            issues.Add(Issue(
                "Setting " + Q(row.KeyPath) + " narrows " + Q(narrows) + ", but only " + Q(WellKnown.ChangeManagementMode) + " may be narrowed for a portfolio. Remove the row, or narrow that setting.",
                row.Entity,
                row.KeyPath,
                row.Pointer));
        }

        if (!hasPortfolio)
        {
            issues.Add(Issue(
                "Setting " + Q(row.KeyPath) + " narrows " + Q(narrows) + " but names no portfolio. Set portfolioId.",
                row.Entity,
                row.KeyPath,
                row.Pointer));
        }

        ContentRow? target = settings.FirstOrDefault(s => string.Equals(s.KeyPath, narrows, StringComparison.Ordinal));
        string? ownType = RowFacts.Text(row, "valueType");
        string? targetType = target == null ? null : RowFacts.Text(target, "valueType");
        if (ownType != null && targetType != null && !string.Equals(ownType, targetType, StringComparison.Ordinal))
        {
            issues.Add(Issue(
                "Setting " + Q(row.KeyPath) + " has valueType " + Q(ownType) + ", but the setting it narrows has " + Q(targetType) + ". Use the same value type.",
                row.Entity,
                row.KeyPath,
                row.Pointer));
        }
    }
}
