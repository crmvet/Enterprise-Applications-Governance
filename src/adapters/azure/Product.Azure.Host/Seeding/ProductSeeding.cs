using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Product.Azure.Stores;
using Product.Engine.Configuration.Composition;
using Product.Engine.Configuration.Patches;
using Product.Engine.Configuration.Presets;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Configuration.Validation;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Json;

namespace Product.Azure.Host.Seeding;

/// <summary>
/// Seeding for local runs, demos and tests: a first configuration version as an earlier activation would leave it in
/// force, and rows that exist before the engine runs. Never an engine write and never a product path: the local server's
/// <c>seed</c> command and the tests use it, and it refuses a store that already has a version in force. A real install
/// gets version 1 through the bootstrap, never through this class.
/// </summary>
public static class ProductSeeding
{
    /// <summary>
    /// Compiles version 1 from a shipped preset, its overlays and a seed patch, validated in activation mode on the given
    /// day, as the MCP test harness does. The seed patch is a JSON array of client binding operations, or an object with
    /// <c>bindingOperations</c> (client binding operations: role group bindings, workload grants) and
    /// <c>draftOperations</c> (other configuration rows, such as a Known Client). Throws when the version is not valid.
    /// </summary>
    public static CompiledSnapshot CompileVersion(
        PresetCatalog catalog,
        string presetKey,
        IEnumerable<string>? overlays,
        JsonTreeNode? bindingOperations,
        IEnumerable<KeyValuePair<string, PrincipalKind>>? principalKinds,
        DateTime validatedOnUtc)
    {
        if (catalog == null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        ContentSource source = ContentSource.Preset(presetKey, overlays?.ToList());
        foreach ((JsonTreeNode operations, PatchKind kind, string where) in Patches(bindingOperations))
        {
            source = source.WithPatch(ConfigurationPatch.FromOperations(operations, kind, where));
        }

        ValidationOutcome outcome = new ConfigurationValidator(catalog).Validate(new ValidationRequest(
            source,
            ValidationMode.Activation,
            versionNumber: 1,
            facts: new ValidationFacts(validatedOn: DateTime.SpecifyKind(validatedOnUtc, DateTimeKind.Utc), principalKinds: principalKinds?.ToList())));
        if (outcome.Snapshot == null)
        {
            string problems = string.Join(" | ", outcome.Report.Issues.Where(i => i.Severity == ReasonSeverity.Error).Take(10).Select(i => i.Code + " " + i.Message));
            string input = outcome.InputProblem == null ? string.Empty : outcome.InputProblem.Code + " " + outcome.InputProblem.Message;
            throw new InvalidOperationException("The seed version is not valid: " + (problems.Length > 0 ? problems : input));
        }

        return outcome.Snapshot;
    }

    /// <summary>
    /// Seeds the rows of a seed file, <c>{ "rows": [ { "entity", "id", "key", "columns" } ] }</c>, in order: a new row is
    /// seeded, a row that already exists has its columns replaced. Returns how many rows were written.
    /// </summary>
    public static int SeedRows(IStoreSeeder store, JsonTreeNode seedFile)
    {
        if (store == null)
        {
            throw new ArgumentNullException(nameof(store));
        }

        if (seedFile == null)
        {
            throw new ArgumentNullException(nameof(seedFile));
        }

        var reader = store as IRecordReader;
        int count = 0;
        foreach (JsonTreeNode row in Js.Items(seedFile, "rows"))
        {
            string entity = Js.Text(row, "entity") ?? throw new InvalidOperationException("A seed row names its entity.");
            string? id = Js.Text(row, "id");
            string? key = Js.Text(row, "key");
            JsonTreeNode columns = Js.Member(row, "columns") ?? throw new InvalidOperationException("The seed row " + entity + " " + (id ?? key) + " has no columns.");
            if (columns.Kind != JsonTreeKind.Object)
            {
                throw new InvalidOperationException("The columns of seed row " + entity + " " + (id ?? key) + " are a JSON object.");
            }

            if (id != null && reader?.Get(entity, id) != null)
            {
                store.Replace(entity, id, columns);
            }
            else
            {
                store.Seed(entity, columns, id, key);
            }

            count++;
        }

        return count;
    }

    /// <summary>
    /// Seeds a demo into an empty store: version 1 compiled from the preset, the overlays and the seed patch (principal
    /// kinds taken from the seed rows' Principal References), then the seed rows. Refuses a store that already has a
    /// version in force.
    /// </summary>
    public static SeedResult SeedDemo(
        IProductStore store,
        PresetCatalog catalog,
        string presetKey,
        IEnumerable<string>? overlays,
        JsonTreeNode? bindingOperations,
        JsonTreeNode? rows,
        DateTime validatedOnUtc)
    {
        if (store == null)
        {
            throw new ArgumentNullException(nameof(store));
        }

        if (store.HasInForceVersion)
        {
            throw new InvalidOperationException("The store already has a configuration version in force. Seeding only fills an empty store; a running product changes its configuration through activation.");
        }

        CompiledSnapshot snapshot = CompileVersion(catalog, presetKey, overlays, bindingOperations, PrincipalKinds(rows), validatedOnUtc);
        store.SeedVersion(snapshot);
        int seeded = rows == null ? 0 : SeedRows(store, rows);
        return new SeedResult(snapshot.ConfigurationSetKey, snapshot.VersionNumber, snapshot.ContentHash, seeded);
    }

    /// <summary>The principal kinds of the Principal Reference rows of a seed file, for validation facts.</summary>
    public static IReadOnlyList<KeyValuePair<string, PrincipalKind>> PrincipalKinds(JsonTreeNode? rows)
    {
        var kinds = new List<KeyValuePair<string, PrincipalKind>>();
        foreach (JsonTreeNode row in Js.Items(rows, "rows").Where(r => Js.Text(r, "entity") == "principal-reference"))
        {
            JsonTreeNode? columns = Js.Member(row, "columns");
            string? objectId = Js.Text(columns, "entraObjectId");
            if (objectId != null && EngineContract.PrincipalKinds.TryParse(Js.Text(columns, "principalKind"), out PrincipalKind kind))
            {
                kinds.Add(new KeyValuePair<string, PrincipalKind>(objectId, kind));
            }
        }

        return kinds;
    }

    /// <summary>Reads a JSON file (UTF-8) into the engine's JSON model.</summary>
    public static JsonTreeNode ReadJson(string path)
    {
        return CanonicalJson.Parse(File.ReadAllText(path, new UTF8Encoding(false, true)));
    }

    private static IEnumerable<(JsonTreeNode Operations, PatchKind Kind, string Where)> Patches(JsonTreeNode? seed)
    {
        if (seed == null || seed.Kind == JsonTreeKind.Null)
        {
            yield break;
        }

        if (seed.Kind == JsonTreeKind.Array)
        {
            yield return (seed, PatchKind.ClientBinding, "client binding patch");
            yield break;
        }

        JsonTreeNode? binding = Js.Member(seed, "bindingOperations") ?? Js.Member(seed, "operations");
        if (binding != null && binding.Kind == JsonTreeKind.Array)
        {
            yield return (binding, PatchKind.ClientBinding, "client binding patch");
        }

        JsonTreeNode? draft = Js.Member(seed, "draftOperations");
        if (draft != null && draft.Kind == JsonTreeKind.Array)
        {
            yield return (draft, PatchKind.Draft, "seed operations");
        }
    }
}

/// <summary>What a demo seed wrote.</summary>
public sealed class SeedResult
{
    /// <summary>Creates the result.</summary>
    public SeedResult(string configurationSetKey, int versionNumber, string contentHash, int rowsSeeded)
    {
        ConfigurationSetKey = configurationSetKey;
        VersionNumber = versionNumber;
        ContentHash = contentHash;
        RowsSeeded = rowsSeeded;
    }

    /// <summary>The configuration set in force.</summary>
    public string ConfigurationSetKey { get; }

    /// <summary>The version seeded (1).</summary>
    public int VersionNumber { get; }

    /// <summary>The content hash of the seeded version.</summary>
    public string ContentHash { get; }

    /// <summary>How many seed rows were written.</summary>
    public int RowsSeeded { get; }
}
