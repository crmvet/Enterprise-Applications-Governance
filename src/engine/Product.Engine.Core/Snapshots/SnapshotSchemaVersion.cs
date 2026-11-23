using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Product.Engine.Core.Snapshots;

// The version of the snapshot's own shape, "major.minor" such as "1.0" (document 25, section 6.2; document 21, C02).
// The engine refuses a major version it does not know (snapshot.schema-unsupported). A minor change only adds.
public sealed class SnapshotSchemaVersion : IEquatable<SnapshotSchemaVersion>
{
    public SnapshotSchemaVersion(int major, int minor)
    {
        if (major < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(major));
        }

        if (minor < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minor));
        }

        Major = major;
        Minor = minor;
    }

    public int Major { get; }

    public int Minor { get; }

    // True when this engine reads snapshots of this shape.
    public bool IsSupported => Major == EngineInfo.SupportedSnapshotSchemaMajor;

    // Parses "major.minor": two whole numbers without signs, spaces or leading zeros.
    public static bool TryParse(string? text, [NotNullWhen(true)] out SnapshotSchemaVersion? version)
    {
        version = null;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        string[] parts = text!.Split('.');
        if (parts.Length != 2 || !TryParsePart(parts[0], out int major) || !TryParsePart(parts[1], out int minor))
        {
            return false;
        }

        version = new SnapshotSchemaVersion(major, minor);
        return true;
    }

    public bool Equals(SnapshotSchemaVersion? other)
    {
        return other != null && Major == other.Major && Minor == other.Minor;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as SnapshotSchemaVersion);
    }

    public override int GetHashCode()
    {
        return unchecked((Major * 397) ^ Minor);
    }

    public override string ToString()
    {
        return Major.ToString(CultureInfo.InvariantCulture) + "." + Minor.ToString(CultureInfo.InvariantCulture);
    }

    private static bool TryParsePart(string part, out int value)
    {
        value = 0;
        if (part.Length == 0 || part.Length > 9 || (part.Length > 1 && part[0] == '0'))
        {
            return false;
        }

        foreach (char c in part)
        {
            if (c < '0' || c > '9')
            {
                return false;
            }
        }

        value = int.Parse(part, NumberStyles.None, CultureInfo.InvariantCulture);
        return true;
    }
}
