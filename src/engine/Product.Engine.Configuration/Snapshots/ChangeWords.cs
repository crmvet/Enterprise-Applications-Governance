using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Product.Engine.Configuration.Model;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Operations;
using Product.Engine.Core.Snapshots;

namespace Product.Engine.Configuration.Snapshots;

// A configuration change in the words people read (product review and QA, wave 3): each operation of a draft names the
// row by its label (a data domain by its path), and a changed field by its words, with the value before and after:
// "The AI use rule of Workplace / Visitor log changes from decision to never." The engine's change summary
// (SnapshotDiff.Summarize) stays as it is, the stored record of what changed; this is how the decision statement, the
// approval card and the pages say it. Labels come from the configuration in force.
public static class ChangeWords
{
    private static readonly HashSet<string> Skipped = new(StringComparer.Ordinal) { "origin", "label" };

    // One sentence per operation of the draft's change document ({ operations: [...] }). Unknown shapes are skipped.
    public static IReadOnlyList<string> Describe(ConfigurationSnapshot inForce, JsonTreeNode? changeJson)
    {
        if (inForce == null)
        {
            throw new ArgumentNullException(nameof(inForce));
        }

        var lines = new List<string>();
        if (changeJson == null || !changeJson.TryGetMember("operations", out JsonTreeNode? operations) || operations.Kind != JsonTreeKind.Array)
        {
            return lines;
        }

        var tree = new DataTree(inForce);
        foreach (JsonTreeNode op in operations.Items)
        {
            string? entity = Text(op, "entity");
            string? key = Text(op, "key");
            if (entity == null || key == null)
            {
                continue;
            }

            string what = ConfigurationEntities.TryGet(entity, out ConfigurationEntity? info) ? info.LogicalName.ToLowerInvariant() : entity.Replace('-', ' ');
            SnapshotRow? row = Find(inForce, entity, key);
            JsonTreeNode? values = op.TryGetMember("values", out JsonTreeNode? v) && v.Kind == JsonTreeKind.Object ? v : null;
            string label = entity == "data-domain" && tree.Domain(key) != null ? tree.PathText(key)
                : (row == null ? null : Text(row.Columns, "label")) ?? Text(values, "label") ?? Humanize(key);

            // A role group binding is named by its role, as the Roles page shows it: "the group binding of Reader", never
            // "the role group binding reader group" (wave 4b QA walk).
            string? roleKey = entity == "role-group-binding" ? (row == null ? null : Text(row.Columns, "roleKey")) ?? Text(values, "roleKey") : null;
            string? roleLabel = roleKey == null ? null : (Find(inForce, "role", roleKey) is SnapshotRow role ? Text(role.Columns, "label") : null) ?? Humanize(roleKey);
            if (roleLabel != null)
            {
                label = "the group binding of " + roleLabel;
            }

            string subject = roleLabel != null ? label : "the " + what + " " + label;
            if (Text(op, "op") == "retire")
            {
                lines.Add("Retire " + subject + ".");
                continue;
            }

            if (row == null || !row.IsActive)
            {
                lines.Add((row == null ? "Add " : "Bring back ") + subject + ".");
                continue;
            }

            var changes = new List<(string Field, string Before, string After)>();
            foreach (KeyValuePair<string, JsonTreeNode> member in values?.Members ?? Array.Empty<KeyValuePair<string, JsonTreeNode>>())
            {
                if (Skipped.Contains(member.Key))
                {
                    continue;
                }

                string before = ValueWords(row.Columns.TryGetMember(member.Key, out JsonTreeNode? was) ? was : null);
                string after = ValueWords(member.Value);
                if (before != after)
                {
                    changes.Add((FieldWords(member.Key), before, after));
                }
            }

            if (values != null && Text(values, "label") is string newLabel && newLabel != Text(row.Columns, "label"))
            {
                changes.Add(("label", roleLabel != null ? Text(row.Columns, "label") ?? "not set" : label, newLabel));
            }

            if (changes.Count == 0)
            {
                // The row is proposed again as it is (a prefilled draft nobody edited): say so, rather than "Change".
                lines.Add("Keep " + subject + " as it is.");
            }
            else if (changes.Count == 1)
            {
                lines.Add("The " + changes[0].Field + " of " + label + " changes from " + changes[0].Before + " to " + changes[0].After + ".");
            }
            else
            {
                lines.Add("Change " + subject + ": " + string.Join(", ", changes.Select(c => "the " + c.Field + " from " + c.Before + " to " + c.After)) + ".");
            }
        }

        return lines;
    }

    // "aiUseRule" gives "AI use rule", "classificationKey" gives "classification".
    public static string FieldWords(string field)
    {
        string name = field.EndsWith("Key", StringComparison.Ordinal) && field.Length > 3 ? field.Substring(0, field.Length - 3) : field;
        var words = new StringBuilder();
        foreach (char c in name)
        {
            if (char.IsUpper(c) && words.Length > 0)
            {
                words.Append(' ');
            }

            words.Append(char.ToLowerInvariant(c));
        }

        return words.ToString().Replace("ai ", "AI ").Replace(" ai", " AI").Replace("id ", "ID ").Replace(" id", " ID");
    }

    private static string ValueWords(JsonTreeNode? value)
    {
        if (value == null || value.Kind == JsonTreeKind.Null)
        {
            return "not set";
        }

        switch (value.Kind)
        {
            case JsonTreeKind.String:
                return Humanize(value.GetString());
            case JsonTreeKind.True:
                return "yes";
            case JsonTreeKind.False:
                return "no";
            case JsonTreeKind.Number:
                return value.TryGetInt64(out long n) ? n.ToString(CultureInfo.InvariantCulture) : "a number";
            default:
                return "a new value";
        }
    }

    // A key in words: "masked-only" gives "masked only".
    private static string Humanize(string text)
    {
        return text.Replace('-', ' ');
    }

    private static SnapshotRow? Find(ConfigurationSnapshot snapshot, string entity, string key)
    {
        return snapshot.TryGetEntity(entity, out SnapshotEntity? rows) ? rows!.Rows.FirstOrDefault(r => string.Equals(r.KeyPath, key, StringComparison.Ordinal)) : null;
    }

    private static string? Text(JsonTreeNode? node, string name)
    {
        return node != null && node.Kind == JsonTreeKind.Object && node.TryGetMember(name, out JsonTreeNode? value) && value.Kind == JsonTreeKind.String ? value.GetString() : null;
    }
}
