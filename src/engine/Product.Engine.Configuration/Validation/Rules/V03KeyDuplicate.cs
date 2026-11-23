using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Model;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V03: keys are unique per entity in the set; child keys are unique within their parent.
// A key made by wildcard expansion counts too, so an explicit row cannot take an expanded key.
public sealed class V03KeyDuplicate : ValidationRule, IContentRule
{
    public static readonly V03KeyDuplicate Instance = new();

    private V03KeyDuplicate()
        : base("V03", ReasonCodes.KeyDuplicate, "Keys are unique")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        // Top-level rows group by entity; child rows group by entity and parent. KeyPath covers both.
        IEnumerable<IGrouping<string, ContentRow>> groups = context.Content.Rows
            .Where(row => row.Key != null)
            .GroupBy(row => row.Entity + "\u0000" + row.KeyPath, StringComparer.Ordinal);

        foreach (IGrouping<string, ContentRow> group in groups)
        {
            ContentRow[] rows = group.ToArray();
            if (rows.Length < 2)
            {
                continue;
            }

            ContentRow first = rows[0];
            string scope = first.ParentKey == null
                ? first.LogicalName + " key " + Q(first.Key)
                : first.LogicalName + " key " + Q(first.Key) + " under " + Q(first.ParentKey);
            issues.Add(Issue(
                scope + " is used by " + rows.Length + " rows (" + string.Join(", ", rows.Select(r => r.Pointer)) + "). Give each row its own key.",
                first.Entity,
                first.KeyPath,
                first.Pointer));
        }
    }
}
