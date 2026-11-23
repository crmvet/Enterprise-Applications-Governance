using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;

namespace Product.Engine.Core.Snapshots;

// The configuration snapshot: the in-memory form of one Configuration Set Version's Content Json
// (document 25, sections 6.1 and 6.2). It is the engine's only configuration input.
// The engine never reads live configuration rows. A change waits as a patch on a Configuration Draft (C46).
public sealed class ConfigurationSnapshot
{
    private readonly Dictionary<string, SnapshotEntity> _byName;

    public ConfigurationSnapshot(
        SnapshotSchemaVersion schemaVersion,
        string configurationSetKey,
        int versionNumber,
        string contentHash,
        IEnumerable<SnapshotEntity> entities)
    {
        if (schemaVersion == null)
        {
            throw new ArgumentNullException(nameof(schemaVersion));
        }

        // A reader checks this first and returns snapshot.schema-unsupported. This is the safety net.
        if (!schemaVersion.IsSupported)
        {
            throw new ArgumentException("This engine does not read snapshot schema " + schemaVersion + ".", nameof(schemaVersion));
        }

        if (!ConfigurationKey.IsValid(configurationSetKey))
        {
            throw new ArgumentException("A configuration set key must use the configuration key format.", nameof(configurationSetKey));
        }

        if (versionNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(versionNumber), "Version numbers start at 1.");
        }

        if (!CanonicalHash.IsHash(contentHash))
        {
            throw new ArgumentException("A content hash is 64 lowercase hex characters.", nameof(contentHash));
        }

        if (entities == null)
        {
            throw new ArgumentNullException(nameof(entities));
        }

        SnapshotEntity[] sorted = entities.OrderBy(entity => entity?.Name, StringComparer.Ordinal).ToArray();
        _byName = new Dictionary<string, SnapshotEntity>(StringComparer.Ordinal);
        foreach (SnapshotEntity entity in sorted)
        {
            if (entity == null)
            {
                throw new ArgumentException("A snapshot has no null entities.", nameof(entities));
            }

            if (_byName.ContainsKey(entity.Name))
            {
                throw new ArgumentException("The snapshot repeats the entity " + entity.Name + ".", nameof(entities));
            }

            _byName.Add(entity.Name, entity);
        }

        SchemaVersion = schemaVersion;
        ConfigurationSetKey = configurationSetKey;
        VersionNumber = versionNumber;
        ContentHash = contentHash;
        Entities = new ReadOnlyCollection<SnapshotEntity>(sorted);
    }

    public SnapshotSchemaVersion SchemaVersion { get; }

    // The configuration set's identity.
    public string ConfigurationSetKey { get; }

    // The C02 Version Number.
    public int VersionNumber { get; }

    // The C02 Content Hash: SHA-256 over the canonical form of Content Json (CanonicalHash.ComputeContentHash).
    public string ContentHash { get; }

    // One entry per configuration entity, sorted by entity key (ordinal).
    public IReadOnlyList<SnapshotEntity> Entities { get; }

    public bool TryGetEntity(string name, [NotNullWhen(true)] out SnapshotEntity? entity)
    {
        if (name != null && _byName.TryGetValue(name, out SnapshotEntity? found))
        {
            entity = found;
            return true;
        }

        entity = null;
        return false;
    }

    // Finds a row by entity key and key path. False when either is missing.
    public bool TryGetRow(string entityName, string keyPath, [NotNullWhen(true)] out SnapshotRow? row)
    {
        row = null;
        return TryGetEntity(entityName, out SnapshotEntity? entity) && entity.TryGetRow(keyPath, out row);
    }
}
