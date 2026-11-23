using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Product.Engine.Core.Contract;

namespace Product.Engine.Core.Snapshots;

// All rows of one configuration entity in a snapshot, indexed by key path.
public sealed class SnapshotEntity
{
    private readonly Dictionary<string, SnapshotRow> _byKeyPath;

    public SnapshotEntity(string name, IEnumerable<SnapshotRow> rows)
    {
        // Entity keys are kebab-case of the logical name, such as "approval-rule" (document 21, section 8.3).
        if (!ConfigurationKey.IsValid(name))
        {
            throw new ArgumentException("An entity key must use the configuration key format.", nameof(name));
        }

        if (rows == null)
        {
            throw new ArgumentNullException(nameof(rows));
        }

        // Rows in key path order, so every reader walks them the same way.
        SnapshotRow[] sorted = rows.OrderBy(row => row?.KeyPath, StringComparer.Ordinal).ToArray();
        _byKeyPath = new Dictionary<string, SnapshotRow>(StringComparer.Ordinal);
        foreach (SnapshotRow row in sorted)
        {
            if (row == null)
            {
                throw new ArgumentException("An entity has no null rows.", nameof(rows));
            }

            // A snapshot in force has passed V03. A repeated key here is a broken snapshot, never a silent pick.
            if (_byKeyPath.ContainsKey(row.KeyPath))
            {
                throw new ArgumentException("Entity " + name + " repeats the key " + row.KeyPath + ".", nameof(rows));
            }

            _byKeyPath.Add(row.KeyPath, row);
        }

        Name = name;
        Rows = new ReadOnlyCollection<SnapshotRow>(sorted);
    }

    // The entity key, such as "gate-policy".
    public string Name { get; }

    // Every row, active and retired, sorted by key path (ordinal).
    public IReadOnlyList<SnapshotRow> Rows { get; }

    public IEnumerable<SnapshotRow> ActiveRows => Rows.Where(row => row.IsActive);

    // Finds a row by key, or by "parent/key" for a child row.
    public bool TryGetRow(string keyPath, [NotNullWhen(true)] out SnapshotRow? row)
    {
        if (keyPath != null && _byKeyPath.TryGetValue(keyPath, out SnapshotRow? found))
        {
            row = found;
            return true;
        }

        row = null;
        return false;
    }
}
