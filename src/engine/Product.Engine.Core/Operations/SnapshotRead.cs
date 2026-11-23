using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Reasons;
using Product.Engine.Core.Snapshots;

namespace Product.Engine.Core.Operations;

// Small readers over snapshot rows for the operations. The snapshot in force passed OP-09, so these readers only
// check what an operation needs to answer at all; anything else that is broken stops the operation with an
// unavailable reason (document 25, rule 10). They never fall back to a built-in value.
internal static class SnapshotRead
{
    // The logical name of an entity key, such as "Approval Rule" for "approval-rule" (document 20 names).
    public static string LogicalName(string entityKey)
    {
        return string.Join(" ", entityKey.Split('-').Select(part => part.Length == 0 ? part : char.ToUpperInvariant(part[0]) + part.Substring(1)));
    }

    public static IEnumerable<SnapshotRow> Rows(ConfigurationSnapshot snapshot, string entityKey)
    {
        return snapshot.TryGetEntity(entityKey, out SnapshotEntity? entity) ? entity.Rows : Enumerable.Empty<SnapshotRow>();
    }

    public static IEnumerable<SnapshotRow> ActiveRows(ConfigurationSnapshot snapshot, string entityKey)
    {
        return Rows(snapshot, entityKey).Where(row => row.IsActive);
    }

    public static SnapshotRow? Find(ConfigurationSnapshot snapshot, string entityKey, string? keyPath)
    {
        return keyPath != null && snapshot.TryGetRow(entityKey, keyPath, out SnapshotRow? row) ? row : null;
    }

    public static SnapshotRow? FindActive(ConfigurationSnapshot snapshot, string entityKey, string? keyPath)
    {
        SnapshotRow? row = Find(snapshot, entityKey, keyPath);
        return row != null && row.IsActive ? row : null;
    }

    // An input key must name a row of the entity in the snapshot. A retired row still exists: old records keep
    // pointing at it. Use RequireActive where only a row in force makes sense.
    public static SnapshotRow RequireExisting(ConfigurationSnapshot snapshot, string entityKey, string key, string field)
    {
        return Find(snapshot, entityKey, key)
            ?? throw OperationStop.Input(
                ReasonCodes.InputUnknownKey,
                field + " is \"" + key + "\", which is not a " + LogicalName(entityKey) + " in the configuration in force.",
                key);
    }

    public static SnapshotRow RequireActive(ConfigurationSnapshot snapshot, string entityKey, string key, string field)
    {
        SnapshotRow row = RequireExisting(snapshot, entityKey, key, field);
        if (!row.IsActive)
        {
            throw OperationStop.Input(
                ReasonCodes.InputUnknownKey,
                field + " is \"" + key + "\", a " + LogicalName(entityKey) + " that is retired in the configuration in force.",
                key);
        }

        return row;
    }

    public static string? Text(SnapshotRow row, string column)
    {
        return row.TryGetColumn(column, out JsonTreeNode? value) && value.Kind == JsonTreeKind.String ? value.GetString() : null;
    }

    // True only for a JSON true. Absent, null or false are false.
    public static bool Flag(SnapshotRow row, string column)
    {
        return row.TryGetColumn(column, out JsonTreeNode? value) && value.Kind == JsonTreeKind.True;
    }

    public static bool HasValue(SnapshotRow row, string column)
    {
        return row.TryGetColumn(column, out JsonTreeNode? value) && value.Kind != JsonTreeKind.Null;
    }

    // A whole number column. Null when absent or null. A value of another shape is reported as broken.
    public static int? Int(SnapshotRow row, string entityKey, string column)
    {
        if (!row.TryGetColumn(column, out JsonTreeNode? value) || value.Kind == JsonTreeKind.Null)
        {
            return null;
        }

        if (!value.TryGetInt64(out long number) || number < int.MinValue || number > int.MaxValue)
        {
            throw OperationStop.Broken(
                ReasonCodes.ValueRange,
                LogicalName(entityKey) + " " + Quote(row.KeyPath) + " has a " + column + " that is not a whole number, so the configuration is broken.",
                LogicalName(entityKey),
                row.KeyPath);
        }

        return (int)number;
    }

    // A list of strings column. Empty when absent or null.
    public static IReadOnlyList<string> Strings(SnapshotRow row, string column)
    {
        if (!row.TryGetColumn(column, out JsonTreeNode? value) || value.Kind != JsonTreeKind.Array)
        {
            return Array.Empty<string>();
        }

        return value.Items.Where(item => item.Kind == JsonTreeKind.String).Select(item => item.GetString()).ToArray();
    }

    public static JsonTreeNode? Node(SnapshotRow row, string column)
    {
        return row.TryGetColumn(column, out JsonTreeNode? value) && value.Kind != JsonTreeKind.Null ? value : null;
    }

    // A list of strings inside a JSON object, such as reviewTypeKeys in Triggers Json.
    public static IReadOnlyList<string> Strings(JsonTreeNode? node, string member)
    {
        if (node == null || !node.TryGetMember(member, out JsonTreeNode? value) || value.Kind != JsonTreeKind.Array)
        {
            return Array.Empty<string>();
        }

        return value.Items.Where(item => item.Kind == JsonTreeKind.String).Select(item => item.GetString()).ToArray();
    }

    // The value of an active C04 Setting row without a portfolio. A missing setting is a broken configuration:
    // V38 requires every setting, and the engine never falls back to a built-in value (document 21, rule 8).
    public static JsonTreeNode RequireSetting(ConfigurationSnapshot snapshot, string settingKey, Func<JsonTreeNode, bool> isWellFormed, string expected)
    {
        SnapshotRow? row = FindActive(snapshot, "setting", settingKey);
        JsonTreeNode? value = row == null ? null : Node(row, "value");
        if (value == null)
        {
            throw OperationStop.Broken(
                ReasonCodes.SettingRequired,
                "The setting " + Quote(settingKey) + " is missing or empty in the configuration in force, so the engine cannot answer. Add it through a configuration change.",
                "Setting",
                settingKey);
        }

        if (!isWellFormed(value))
        {
            throw OperationStop.Broken(
                ReasonCodes.SettingRequired,
                "The setting " + Quote(settingKey) + " must hold " + expected + ", so the configuration is broken.",
                "Setting",
                settingKey);
        }

        return value;
    }

    public static string Quote(string value)
    {
        return "`" + value + "`";
    }
}
