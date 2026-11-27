using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Json;

namespace Product.Mcp.Core.Backend.InProcess;

// Record search (tool 75; document 24, B24-12): the web app's global search names a record by its number, such as
// DEC-0123, as people do on cards, in email and in meetings. The engine gives the numbers (document 25, B25-9).
internal static partial class ToolHandlers
{
    // ---- 75. find_records ----

    // Finds the rows of the numbered entity the prefix names whose Record Number is the number given. The number is read
    // as people write it ("dec 123", "DEC0123") and looked up in the engine's own form (DEC-0123), so leading zeros never
    // matter. A row the caller may not read is never found: intake requests follow intake-visibility, as
    // list_intake_requests does; the other numbered rows are read by any role, as their list reads are.
    private static JsonTreeNode FindRecords(ToolScope s)
    {
        string text = s.RequireText("recordNumber").Trim();
        int split = 0;
        while (split < text.Length && char.IsLetter(text[split]))
        {
            split++;
        }

        string prefix = text.Substring(0, split).ToUpperInvariant();
        string digits = text.Substring(split).TrimStart(' ', '-');
        string? entity = RecordNumbers.Prefixes.FirstOrDefault(p => p.Value == prefix).Key;
        if (entity == null)
        {
            throw s.Invalid("recordNumber", "\"" + prefix + "\" is not a record number prefix. Use APP, IR, DEC, CHG, REV, CFG or DU.");
        }

        var found = new List<StoredRow>();
        if (long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out long number) && number > 0)
        {
            string canonical = RecordNumbers.Format(prefix, number);
            foreach (StoredRow row in s.Rows.Query(RowQuery.For(entity).Where(RecordNumbers.Column, canonical)))
            {
                // A decision about a request the caller may not read is hidden as the request is (security review
                // 2026-11-24, B24-14): its statement names the request's title, and decision numbers run in sequence.
                if ((entity != "intake-request" || CanReadIntake(s, row)) && (entity != "decision" || !HidesIntake(s, row)))
                {
                    found.Add(row);
                }
            }
        }

        string shown = number > 0 ? RecordNumbers.Format(prefix, number) : prefix + "-" + digits;
        var ordered = found.OrderBy(r => r.Id, StringComparer.Ordinal).Take(20).ToList();
        JsonTreeNode items = Arr(ordered.Select(row => Js.Obj(
            ("entity", JsonTreeNode.FromString(entity)),
            ("recordId", JsonTreeNode.FromString(row.Id)),
            ("recordNumber", JsonTreeNode.FromString(row.Text(RecordNumbers.Column) ?? shown)),
            ("label", JsonTreeNode.FromString(NumberedLabel(s, row))),
            ("status", NumberedStatus(s, row)),
            ("businessApplicationId", Js.Str(entity == "business-application" ? row.Id : entity == "configuration-draft" ? null : entity == "data-use" ? row.Text("businessApplication") : ApplicationOf(s, row))),
            ("recordUrl", JsonTreeNode.FromString(s.RecordUrl(entity, row.Id))))));
        string kind = EntityWords(entity);
        string summary = ordered.Count == 0
            ? "No " + kind + " you may read has the number " + shown + "."
            : ordered.Count == 1
                ? shown + " is " + (kind.StartsWith("a", StringComparison.Ordinal) || kind.StartsWith("i", StringComparison.Ordinal) ? "an " : "a ") + kind + ": " + NumberedLabel(s, ordered[0]) + "."
                : ordered.Count + " records have the number " + shown + ".";
        return s.Answer(Js.Obj(("items", items)), summary, ordered.Count == 1 ? s.RecordUrl(entity, ordered[0].Id) : null);
    }

    private static string EntityWords(string entity)
    {
        return entity switch
        {
            "business-application" => "application",
            "intake-request" => "intake request",
            "change-request" => "change request",
            "configuration-draft" => "configuration draft",
            "data-use" => "data use",
            _ => entity,
        };
    }

    // What a numbered record is, without its number: the application's name, the request's or change's title, the
    // decision's statement, the review's type and application, the draft's name.
    private static string NumberedLabel(ToolScope s, StoredRow row)
    {
        string? label = row.Entity switch
        {
            "business-application" => AppName(row),
            "intake-request" or "change-request" => row.Text("title"),
            "decision" => row.Text("statement") ?? s.Label("decision-type", row.Text("decisionType")),
            "review" => s.Label("review-type", row.Text("reviewType")) + (row.Text("businessApplication") is string a && s.Rows.Get("business-application", a) is StoredRow app ? ": " + AppName(app) : string.Empty),
            "configuration-draft" => ConfigurationDraftLabel(s, row),
            "data-use" => DataSubjectLabel(s, "data-use", row.Id, row, row.Text("businessApplication") is string useApp ? s.Rows.Get("business-application", useApp) : null),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(label) ? row.Text(RecordNumbers.Column) ?? row.Id : label!;
    }

    // The status of a numbered record. A configuration draft keeps its own draft status (draft, submitted, activated,
    // discarded, withdrawn) instead of a configured status row.
    private static JsonTreeNode NumberedStatus(ToolScope s, StoredRow row)
    {
        if (row.Entity == "configuration-draft")
        {
            return row.Text("draftStatus") is string draft ? ToolScope.StatusValue(draft, null) : JsonTreeNode.Null;
        }

        return s.StatusValue(row);
    }
}
