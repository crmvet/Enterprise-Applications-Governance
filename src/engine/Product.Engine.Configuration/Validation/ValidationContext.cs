using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Model;
using Product.Engine.Configuration.Schema;
using Product.Engine.Core.Canonical;

namespace Product.Engine.Configuration.Validation;

// A row named by entity key and key path, such as a row an operational record uses (V08 facts).
public sealed class RowIdentity : IEquatable<RowIdentity>
{
    public RowIdentity(string entity, string keyPath)
    {
        Entity = entity ?? throw new ArgumentNullException(nameof(entity));
        KeyPath = keyPath ?? throw new ArgumentNullException(nameof(keyPath));
    }

    public string Entity { get; }

    public string KeyPath { get; }

    public bool Equals(RowIdentity? other)
    {
        return other != null
            && string.Equals(Entity, other.Entity, StringComparison.Ordinal)
            && string.Equals(KeyPath, other.KeyPath, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as RowIdentity);
    }

    public override int GetHashCode()
    {
        return unchecked((StringComparer.Ordinal.GetHashCode(Entity) * 397) ^ StringComparer.Ordinal.GetHashCode(KeyPath));
    }
}

// What the content rules see: the composed content, its bundle form, its shape check and its references,
// plus the active version for the rules that compare (V08).
public sealed class ValidationContext
{
    internal ValidationContext(
        ValidationMode mode,
        ConfigurationContent content,
        JsonTreeNode bundle,
        SchemaResult shape,
        ConfigurationContent? active,
        ReferenceIndex? activeReferences,
        IEnumerable<RowIdentity>? rowsInUse,
        ValidationFacts? facts = null,
        int? baseVersionNumber = null)
    {
        Mode = mode;
        Content = content;
        Bundle = bundle;
        Shape = shape;
        References = ReferenceIndex.Build(content, bundle, shape);
        Active = active;
        ActiveReferences = activeReferences;
        RowsInUse = new HashSet<RowIdentity>(rowsInUse ?? Enumerable.Empty<RowIdentity>());
        Facts = facts ?? ValidationFacts.None;
        BaseVersionNumber = baseVersionNumber ?? Facts.BaseVersionNumber;
    }

    public ValidationMode Mode { get; }

    public bool IsPresetBuild => Mode == ValidationMode.PresetBuild;

    // The composed content: wildcards expanded, overlays and patches applied.
    public ConfigurationContent Content { get; }

    // The content as a bundle (manifest plus entities).
    public JsonTreeNode Bundle { get; }

    // The schema check of the bundle. Its annotations feed the reference index.
    public SchemaResult Shape { get; }

    public ReferenceIndex References { get; }

    // The active version, when there is one to compare with.
    public ConfigurationContent? Active { get; }

    public ReferenceIndex? ActiveReferences { get; }

    // Rows that operational records use (facts the runtime fetched; document 25, section 3.3).
    public ISet<RowIdentity> RowsInUse { get; }

    // The other facts: validation date, version numbers, principal kinds, the binding's size limit.
    public ValidationFacts Facts { get; }

    // The active version the draft started from: the facts, or else the draft patch's manifest (V42).
    public int? BaseVersionNumber { get; }
}
