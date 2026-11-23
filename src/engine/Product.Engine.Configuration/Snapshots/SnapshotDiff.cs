using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Json;
using Product.Engine.Configuration.Model;
using Product.Engine.Core.Canonical;

namespace Product.Engine.Configuration.Snapshots;

// The engine's diff and change summary (document 21, section 4.3): plain lines of what changed between two contents.
// The approval card shows these lines, never only the AI's description.
public static class SnapshotDiff
{
    // Fields that are not configuration content: who changed a row is not a change of the rule.
    private static readonly HashSet<string> IgnoredFields = new(StringComparer.Ordinal) { "origin" };

    // One line per changed row, sorted by entity key, then key path.
    public static IReadOnlyList<string> Summarize(ConfigurationContent before, ConfigurationContent after)
    {
        if (before == null)
        {
            throw new ArgumentNullException(nameof(before));
        }

        if (after == null)
        {
            throw new ArgumentNullException(nameof(after));
        }

        Dictionary<string, ContentRow> old = Index(before);
        Dictionary<string, ContentRow> now = Index(after);
        var lines = new List<KeyValuePair<string, string>>();

        foreach (KeyValuePair<string, ContentRow> pair in now)
        {
            ContentRow row = pair.Value;
            if (!old.TryGetValue(pair.Key, out ContentRow? previous))
            {
                lines.Add(Line(pair.Key, "Added " + row.LogicalName + " `" + row.KeyPath + "`."));
                continue;
            }

            string? change = Describe(previous, row);
            if (change != null)
            {
                lines.Add(Line(pair.Key, change));
            }
        }

        foreach (KeyValuePair<string, ContentRow> pair in old)
        {
            if (!now.ContainsKey(pair.Key))
            {
                lines.Add(Line(pair.Key, "Removed " + pair.Value.LogicalName + " `" + pair.Value.KeyPath + "`."));
            }
        }

        return lines.OrderBy(l => l.Key, StringComparer.Ordinal).Select(l => l.Value).ToList();
    }

    // The rows that differ between two contents, as entity key, key path and kind of change, sorted by entity key,
    // then key path (ordinal). Same comparison as Summarize: origin is ignored, and a parent row is compared without
    // its child array, because each child is a row of its own. Rebase reads it to find overlaps (document 21, section 4.5).
    public static IReadOnlyList<RowChange> Changes(ConfigurationContent before, ConfigurationContent after)
    {
        if (before == null)
        {
            throw new ArgumentNullException(nameof(before));
        }

        if (after == null)
        {
            throw new ArgumentNullException(nameof(after));
        }

        Dictionary<string, ContentRow> old = Index(before);
        Dictionary<string, ContentRow> now = Index(after);
        var changes = new List<RowChange>();
        foreach (KeyValuePair<string, ContentRow> pair in now)
        {
            ContentRow row = pair.Value;
            if (!old.TryGetValue(pair.Key, out ContentRow? previous))
            {
                changes.Add(new RowChange(row.Entity, row.KeyPath, RowChangeKind.Added));
                continue;
            }

            if (previous.IsActive && !row.IsActive)
            {
                changes.Add(new RowChange(row.Entity, row.KeyPath, RowChangeKind.Retired));
            }
            else if (!previous.IsActive && row.IsActive)
            {
                changes.Add(new RowChange(row.Entity, row.KeyPath, RowChangeKind.Reactivated));
            }
            else if (ChangedFields(previous, row).Any())
            {
                changes.Add(new RowChange(row.Entity, row.KeyPath, RowChangeKind.Changed));
            }
        }

        foreach (KeyValuePair<string, ContentRow> pair in old)
        {
            if (!now.ContainsKey(pair.Key))
            {
                changes.Add(new RowChange(pair.Value.Entity, pair.Value.KeyPath, RowChangeKind.Removed));
            }
        }

        return changes
            .OrderBy(c => c.Entity, StringComparer.Ordinal)
            .ThenBy(c => c.KeyPath, StringComparer.Ordinal)
            .ToList();
    }

    private static string? Describe(ContentRow before, ContentRow after)
    {
        string[] changed = ChangedFields(before, after).ToArray();
        string name = after.LogicalName + " `" + after.KeyPath + "`";
        bool retired = before.IsActive && !after.IsActive;
        bool reactivated = !before.IsActive && after.IsActive;
        string[] others = changed.Where(f => f != "isActive").ToArray();

        if (retired || reactivated)
        {
            string verb = retired ? "Retired " : "Reactivated ";
            return others.Length == 0 ? verb + name + "." : verb + name + " and changed " + string.Join(", ", others) + ".";
        }

        return others.Length == 0 ? null : "Changed " + name + ": " + string.Join(", ", others) + ".";
    }

    private static IEnumerable<string> ChangedFields(ContentRow before, ContentRow after)
    {
        ConfigurationEntities.TryGet(after.Entity, out ConfigurationEntity? info);
        IEnumerable<string> names = before.Node.Members.Select(m => m.Key)
            .Union(after.Node.Members.Select(m => m.Key), StringComparer.Ordinal)
            .Where(n => !IgnoredFields.Contains(n) && (info?.ChildField == null || n != info.ChildField))
            .OrderBy(n => n, StringComparer.Ordinal);

        foreach (string name in names)
        {
            JsonTreeNode? a = JsonNodes.Get(before.Node, name);
            JsonTreeNode? b = JsonNodes.Get(after.Node, name);
            if (name == "isActive")
            {
                if (JsonNodes.IsActive(before.Node) != JsonNodes.IsActive(after.Node))
                {
                    yield return name;
                }

                continue;
            }

            if (a == null || b == null || !a.Equals(b))
            {
                yield return name;
            }
        }
    }

    private static Dictionary<string, ContentRow> Index(ConfigurationContent content)
    {
        var index = new Dictionary<string, ContentRow>(StringComparer.Ordinal);
        foreach (ContentRow row in content.Rows)
        {
            if (row.Key == null)
            {
                continue;
            }

            string id = row.Entity + "\u0000" + row.KeyPath;
            if (!index.ContainsKey(id))
            {
                index.Add(id, row);
            }
        }

        return index;
    }

    private static KeyValuePair<string, string> Line(string sortKey, string text)
    {
        return new KeyValuePair<string, string>(sortKey, text);
    }
}

// How a row differs between two contents.
public enum RowChangeKind
{
    Added,
    Changed,
    Retired,
    Reactivated,

    // The row is gone. Never valid between two versions (V08), but the diff still says so.
    Removed,
}

// One row that differs between two contents: its entity key, its key path ("parent/child" for a child) and how.
public sealed class RowChange
{
    public RowChange(string entity, string keyPath, RowChangeKind kind)
    {
        Entity = entity ?? throw new ArgumentNullException(nameof(entity));
        KeyPath = keyPath ?? throw new ArgumentNullException(nameof(keyPath));
        Kind = kind;
    }

    // The entity key, such as "approval-rule".
    public string Entity { get; }

    public string KeyPath { get; }

    public RowChangeKind Kind { get; }

    // "added", "changed", "retired", "reactivated" or "removed".
    public string KindWire => KindToWire(Kind);

    public static string KindToWire(RowChangeKind kind)
    {
        switch (kind)
        {
            case RowChangeKind.Added:
                return "added";
            case RowChangeKind.Retired:
                return "retired";
            case RowChangeKind.Reactivated:
                return "reactivated";
            case RowChangeKind.Removed:
                return "removed";
            default:
                return "changed";
        }
    }

    public JsonTreeNode ToJson()
    {
        return JsonTreeNode.CreateObject(
            JsonNodes.Member("entity", Entity),
            JsonNodes.Member("key", KeyPath),
            JsonNodes.Member("change", KindWire));
    }

    public override string ToString()
    {
        return Entity + " " + KeyPath + " (" + KindWire + ")";
    }
}
