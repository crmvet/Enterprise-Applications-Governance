using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Product.Engine.Configuration.Json;
using Product.Engine.Configuration.Model;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V44: a shipped preset holds no secrets, no tenant identifiers (no GUID values, no tenant host names),
// no role group bindings and no portfolio scopes (document 21, rule 11 and section 6.8).
// It runs in preset-build mode only. A client's working set holds its bindings, so it is not checked there.
public sealed class V44PresetTenantData : ValidationRule, IContentRule
{
    public static readonly V44PresetTenantData Instance = new();

    // Entities that hold client facts only. They come from the client binding patch, never from a preset.
    // WORKING DEFAULT (open design question 12): workload roles reach workload identities only through
    // Workload Grant rows, and those rows are client facts like the role group bindings.
    private static readonly Dictionary<string, string> ClientOnlyEntities = new(StringComparer.Ordinal)
    {
        { "role-group-binding", "Role group bindings are client facts: put them in the client binding patch" },
        { "workload-grant", "Workload grants are client facts: put them in the client binding patch" },
        { "portfolio-access", "Portfolio access rows are client facts: put them in the client binding patch" },
    };

    // Fields that scope a row to one portfolio, or narrow a setting for one portfolio (document 21, section 4.1).
    private static readonly HashSet<string> PortfolioFields = new(StringComparer.Ordinal) { "portfolioId", "narrowsSettingKey" };

    private static readonly Regex Guid = new(
        "[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}",
        RegexOptions.CultureInvariant);

    // Host names that belong to one tenant or one environment. Public documentation hosts are fine.
    // The list grows as new tenant host shapes appear.
    private static readonly Regex TenantHost = new(
        @"(?:^|[^A-Za-z0-9.-])((?:[A-Za-z0-9-]+\.)+(?:crm[0-9]*\.dynamics\.com|sharepoint\.com|onmicrosoft\.com|powerappsportals\.com|environment\.api\.powerplatform\.com))(?![A-Za-z0-9-])",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    // Text that only a secret or a credential holds.
    private static readonly Regex SecretText = new(
        @"(AccountKey=|SharedAccessKey=|SharedAccessSignature=|[?&]sig=|password=|pwd=|client_secret|-----BEGIN |eyJ[A-Za-z0-9_-]{8,}\.eyJ[A-Za-z0-9_-]{8,}\.)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    // Field names that hold secrets or directory identifiers.
    private static readonly Regex SecretField = new(
        "(secret|password|passwd|apikey|accesskey|connectionstring|privatekey|credentialvalue)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private V44PresetTenantData()
        : base("V44", ReasonCodes.PresetTenantData, "Shipped presets hold no tenant data")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        if (!context.IsPresetBuild)
        {
            return;
        }

        foreach (KeyValuePair<string, string> entity in ClientOnlyEntities)
        {
            foreach (ContentRow row in context.Content.RowsFor(entity.Key))
            {
                issues.Add(Issue(
                    "the shipped preset holds " + row.LogicalName + " " + Q(row.KeyPath) + ". " + entity.Value + ".",
                    row.Entity,
                    row.KeyPath,
                    row.Pointer));
            }
        }

        Scan(context, context.Bundle, JsonPointer.Root, issues);
    }

    private void Scan(ValidationContext context, JsonTreeNode node, string pointer, ValidationIssues issues)
    {
        switch (node.Kind)
        {
            case JsonTreeKind.Object:
                foreach (KeyValuePair<string, JsonTreeNode> member in node.Members)
                {
                    string here = JsonPointer.Append(pointer, member.Key);
                    if (PortfolioFields.Contains(member.Key) && member.Value.Kind != JsonTreeKind.Null)
                    {
                        Report(context, here, member.Key, "scopes a row to one portfolio. Portfolio scopes are client facts: put them in the client binding patch", issues);
                    }
                    else if (member.Key.EndsWith("ObjectId", StringComparison.Ordinal) && member.Value.Kind != JsonTreeKind.Null)
                    {
                        Report(context, here, member.Key, "holds a directory object ID, which is a tenant identifier. Remove it", issues);
                    }
                    else if (SecretField.IsMatch(member.Key))
                    {
                        Report(context, here, member.Key, "looks like it holds a secret. Shipped presets hold no secrets: remove it", issues);
                    }

                    Scan(context, member.Value, here, issues);
                }

                break;
            case JsonTreeKind.Array:
                for (int i = 0; i < node.Items.Count; i++)
                {
                    Scan(context, node.Items[i], JsonPointer.Append(pointer, i), issues);
                }

                break;
            case JsonTreeKind.String:
                string text = node.GetString();
                string field = LastToken(pointer);
                if (Guid.IsMatch(text))
                {
                    Report(context, pointer, field, "holds a GUID value (" + Guid.Match(text).Value + "). Shipped presets hold no tenant identifiers: remove it or use a key", issues);
                }

                Match host = TenantHost.Match(text);
                if (host.Success)
                {
                    Report(context, pointer, field, "holds the tenant host name " + Q(host.Groups[1].Value) + ". Shipped presets hold no tenant host names: remove it", issues);
                }

                if (SecretText.IsMatch(text))
                {
                    Report(context, pointer, field, "looks like a secret or a credential. Shipped presets hold no secrets: remove it", issues);
                }

                break;
        }
    }

    private void Report(ValidationContext context, string pointer, string field, string problem, ValidationIssues issues)
    {
        if (context.Content.TryFindRowAt(pointer, out ContentRow? row))
        {
            issues.Add(Issue(row.LogicalName + " " + Q(row.KeyPath) + " field " + Q(field) + " " + problem + ".", row.Entity, row.KeyPath, pointer));
            return;
        }

        issues.Add(Issue("the preset field " + pointer + " " + problem + ".", null, null, pointer));
    }

    private static string LastToken(string pointer)
    {
        IReadOnlyList<string> tokens = JsonPointer.Split(pointer);
        string last = tokens.Count == 0 ? string.Empty : tokens[tokens.Count - 1];

        // Inside an array the field name is the token before the index.
        return tokens.Count > 1 && last.All(char.IsDigit) ? tokens[tokens.Count - 2] : last;
    }
}
