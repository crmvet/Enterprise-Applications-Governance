using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Composition;
using Product.Engine.Configuration.Patches;
using Product.Engine.Configuration.Presets;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Configuration.Validation;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Testing;

namespace Product.Engine.Configuration.Tests;

// Snapshots and contexts for the operation tests. A snapshot is a shipped preset, with overlays and inline
// draft operations, composed as a working set and compiled, as activation leaves it in force. Fictional IDs only.
internal static class OperationTestData
{
    public static readonly DateTime AsOf = new(2026, 11, 22, 9, 0, 0, DateTimeKind.Utc);

    private static readonly Lazy<PresetCatalog> LazyShipped = new(() => PresetCatalog.Load(new[] { SourceRoot.PresetsFolder(TestData.Root) }));

    private static readonly Dictionary<string, CompiledSnapshot> Cache = new(StringComparer.Ordinal);

    public static PresetCatalog Shipped => LazyShipped.Value;

    public static CompiledSnapshot Snapshot(string preset = "balanced", string? operationsJson = null, params string[] overlays)
    {
        string cacheKey = preset + "|" + string.Join("+", overlays) + "|" + (operationsJson ?? string.Empty);
        lock (Cache)
        {
            if (Cache.TryGetValue(cacheKey, out CompiledSnapshot? cached))
            {
                return cached;
            }
        }

        ContentSource source = ContentSource.Preset(preset, overlays);
        if (operationsJson != null)
        {
            source = source.WithPatch(ConfigurationPatch.FromOperations(CanonicalJson.Parse(operationsJson), PatchKind.Draft, "test operations"));
        }

        ComposedConfiguration composed = new ConfigurationComposer(TestData.Catalog).Compose(source, ValidationMode.Draft, new ValidationIssues());
        CompiledSnapshot compiled = SnapshotCompiler.Compile(composed.Bundle, 1);
        lock (Cache)
        {
            Cache[cacheKey] = compiled;
        }

        return compiled;
    }

    public static EvaluationContext Context(CompiledSnapshot snapshot, DateTime? asOf = null)
    {
        return new EvaluationContext(snapshot.VersionNumber, snapshot.ContentHash, asOf ?? AsOf, Engine(), "test-correlation");
    }

    // OP-05 reads no snapshot. Any well-formed hash will do.
    public static EvaluationContext DatesOnlyContext()
    {
        return new EvaluationContext(1, CanonicalHash.ComputeContentHash(JsonTreeNode.Null), AsOf, Engine(), "test-correlation");
    }

    public static JsonTreeNode Json(string text)
    {
        return CanonicalJson.Parse(text);
    }

    public static string Canonical(JsonTreeNode? node)
    {
        return node == null ? "null" : CanonicalJson.Write(node, CanonicalProfile.Snapshot);
    }

    public static string[] Codes(IEnumerable<Reason> reasons)
    {
        return reasons.Select(reason => reason.Code).ToArray();
    }

    // Every shipped full preset alone and with each allowed overlay combination.
    public static IEnumerable<KeyValuePair<string, string[]>> ShippedSelections()
    {
        foreach (PresetSource full in Shipped.FullPresets)
        {
            yield return new KeyValuePair<string, string[]>(full.Key, Array.Empty<string>());
            foreach (string[] overlays in Shipped.AllowedOverlayCombinations(full))
            {
                yield return new KeyValuePair<string, string[]>(full.Key, overlays);
            }
        }
    }

    private static ActorInfo Engine()
    {
        return new ActorInfo("fixture-engine", PrincipalKind.ServicePrincipal, ActorKindMeaning.Engine);
    }
}
