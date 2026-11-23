using System;
using Product.Engine.Configuration.Json;
using Product.Engine.Configuration.Model;
using Product.Engine.Configuration.Schema;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V40: URL and ID patterns compile as regular expressions; source, hub and template URLs use https.
// Patterns are the link kind URL and ID patterns and the forbidden URL patterns setting. They are read as
// ECMA-262 patterns, the same way the schema checker reads them. URLs are the vocabulary mapping source URL,
// the guideline hub and local URLs, the document type template URL, and the guideline hub base URL setting.
public sealed class V40Patterns : ValidationRule, IContentRule
{
    public static readonly V40Patterns Instance = new();

    private static readonly string[][] UrlFields =
    {
        new[] { "vocabulary-mapping", "sourceUrl" },
        new[] { "guideline", "hubUrl" },
        new[] { "guideline", "localUrl" },
        new[] { "document-type", "templateUrl" },
    };

    private V40Patterns()
        : base("V40", ReasonCodes.PatternInvalid, "Patterns compile and URLs use https")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        foreach (ContentRow kind in RowFacts.Active(context.Content, "link-kind"))
        {
            CheckPattern(kind, "urlPattern", RowFacts.Text(kind, "urlPattern"), issues);
            CheckPattern(kind, "idPattern", RowFacts.Text(kind, "idPattern"), issues);
        }

        foreach (string[] field in UrlFields)
        {
            foreach (ContentRow row in RowFacts.Active(context.Content, field[0]))
            {
                CheckUrl(row, field[1], RowFacts.Text(row, field[1]), issues);
            }
        }

        foreach (ContentRow setting in RowFacts.Active(context.Content, "setting"))
        {
            JsonTreeNode? value = JsonNodes.Get(setting.Node, "value");
            if (string.Equals(setting.KeyPath, WellKnown.GuidelineHubBaseUrl, StringComparison.Ordinal))
            {
                CheckUrl(setting, "value", value?.Kind == JsonTreeKind.String ? value.GetString() : null, issues);
            }
            else if (string.Equals(setting.KeyPath, WellKnown.ForbiddenUrlPatterns, StringComparison.Ordinal) && value?.Kind == JsonTreeKind.Array)
            {
                foreach (JsonTreeNode item in value.Items)
                {
                    CheckPattern(setting, "value", item.Kind == JsonTreeKind.String ? item.GetString() : null, issues);
                }
            }
        }
    }

    private void CheckPattern(ContentRow row, string field, string? pattern, ValidationIssues issues)
    {
        if (pattern == null || JsonSchemaSet.IsValidPattern(pattern))
        {
            return;
        }

        issues.Add(Issue(
            row.LogicalName + " " + Q(row.KeyPath) + " field " + Q(field) + " holds " + Q(pattern) + ", which does not compile as a regular expression. Fix the pattern.",
            row.Entity,
            row.KeyPath,
            row.Pointer + "/" + field));
    }

    private void CheckUrl(ContentRow row, string field, string? url, ValidationIssues issues)
    {
        if (url == null)
        {
            return;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) && string.Equals(parsed.Scheme, "https", StringComparison.Ordinal) && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        issues.Add(Issue(
            row.LogicalName + " " + Q(row.KeyPath) + " field " + Q(field) + " is " + Q(url) + ", which is not an https URL. Use an absolute https URL.",
            row.Entity,
            row.KeyPath,
            row.Pointer + "/" + field));
    }
}
