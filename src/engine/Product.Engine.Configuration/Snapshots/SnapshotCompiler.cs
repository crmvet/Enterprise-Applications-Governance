using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Json;
using Product.Engine.Configuration.Model;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;
using Product.Engine.Core.Snapshots;

namespace Product.Engine.Configuration.Snapshots;

// A compiled configuration snapshot: Content Json, its canonical hash, its version number,
// and the core's immutable snapshot model built from it (document 25, sections 6.2 and 6.3).
public sealed class CompiledSnapshot
{
    internal CompiledSnapshot(JsonTreeNode contentJson, string contentHash, int versionNumber, ConfigurationSnapshot snapshot)
    {
        ContentJson = contentJson;
        ContentHash = contentHash;
        VersionNumber = versionNumber;
        Snapshot = snapshot;
    }

    // C02 Content Json: the snapshot bundle. It holds no version number, so a revert gets the same hash.
    public JsonTreeNode ContentJson { get; }

    // C02 Content Hash: SHA-256 over the canonical snapshot form (CanonicalHash).
    public string ContentHash { get; }

    // C02 Version Number.
    public int VersionNumber { get; }

    public string ConfigurationSetKey => Snapshot.ConfigurationSetKey;

    // The core's model, with one entity per configuration entity. Child rows sit in their own entity
    // (list-value, tier-answer-option, decision-outcome) with their parent key.
    public ConfigurationSnapshot Snapshot { get; }

    // The canonical text the hash is computed over.
    public string CanonicalText => CanonicalJson.Write(ContentJson, CanonicalProfile.Snapshot);
}

// The snapshot cannot be compiled. Code is snapshot.schema-unsupported or input.malformed.
public sealed class SnapshotCompileException : Exception
{
    public SnapshotCompileException(string code, string message, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
    }

    public string Code { get; }
}

// Compiles a snapshot bundle into the core's immutable model. Validate first (OP-09): the compiler only
// checks what the core model needs, such as the schema version, the key format and unique keys.
public static class SnapshotCompiler
{
    private static readonly HashSet<string> RowFields = new(StringComparer.Ordinal) { "key", "isActive", "origin", "engineMeaning" };

    public static CompiledSnapshot Compile(JsonTreeNode contentJson, int versionNumber, string? configurationSetKey = null)
    {
        if (contentJson == null)
        {
            throw new ArgumentNullException(nameof(contentJson));
        }

        string? schemaText = JsonNodes.GetString(contentJson, "schemaVersion");
        if (!SnapshotSchemaVersion.TryParse(schemaText, out SnapshotSchemaVersion? schemaVersion))
        {
            throw new SnapshotCompileException(ReasonCodes.InputMalformed, "The snapshot has no readable schemaVersion.");
        }

        if (!schemaVersion.IsSupported)
        {
            throw new SnapshotCompileException(ReasonCodes.SnapshotSchemaUnsupported, "This engine does not read snapshot schema " + schemaVersion + ".");
        }

        JsonTreeNode manifest = JsonNodes.Get(contentJson, "manifest") ?? JsonNodes.EmptyObject;
        string setKey = configurationSetKey ?? JsonNodes.GetString(manifest, "configurationSetKey") ?? Composition.ConfigurationComposer.DefaultConfigurationSetKey;
        string hash = CanonicalHash.ComputeContentHash(contentJson);
        ConfigurationContent content = ConfigurationContent.FromEntitiesNode(JsonNodes.Get(contentJson, "entities"));

        try
        {
            var rowsByEntity = new SortedDictionary<string, List<SnapshotRow>>(StringComparer.Ordinal);
            foreach (string entity in content.EntityKeys)
            {
                rowsByEntity[entity] = new List<SnapshotRow>();
                if (ConfigurationEntities.TryGet(entity, out ConfigurationEntity? info) && info.ChildKey != null && !rowsByEntity.ContainsKey(info.ChildKey))
                {
                    rowsByEntity[info.ChildKey] = new List<SnapshotRow>();
                }
            }

            foreach (ContentRow row in content.Rows)
            {
                if (!rowsByEntity.TryGetValue(row.Entity, out List<SnapshotRow>? list))
                {
                    list = new List<SnapshotRow>();
                    rowsByEntity.Add(row.Entity, list);
                }

                list.Add(ToSnapshotRow(row));
            }

            IEnumerable<SnapshotEntity> entities = rowsByEntity.Select(e => new SnapshotEntity(e.Key, e.Value));
            var snapshot = new ConfigurationSnapshot(schemaVersion, setKey, versionNumber, hash, entities);
            return new CompiledSnapshot(contentJson, hash, versionNumber, snapshot);
        }
        catch (ArgumentException e)
        {
            throw new SnapshotCompileException(ReasonCodes.InputMalformed, "The snapshot does not compile: " + e.Message, e);
        }
    }

    private static SnapshotRow ToSnapshotRow(ContentRow row)
    {
        string originText = JsonNodes.GetString(row.Node, "origin") ?? EngineContract.Origins.ToWire(Origin.Shipped);
        Origin origin = EngineContract.Origins.Parse(originText);

        ConfigurationEntities.TryGet(row.Entity, out ConfigurationEntity? info);
        IEnumerable<KeyValuePair<string, JsonTreeNode>> columns = row.Node.Members
            .Where(m => !RowFields.Contains(m.Key) && (info?.ChildField == null || m.Key != info.ChildField));

        return new SnapshotRow(
            row.Key ?? string.Empty,
            row.IsActive,
            origin,
            JsonTreeNode.CreateObject(columns),
            JsonNodes.GetString(row.Node, "engineMeaning"),
            row.ParentKey);
    }
}
