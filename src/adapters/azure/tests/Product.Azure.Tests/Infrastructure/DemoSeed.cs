using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Product.Azure.Host.Seeding;
using Product.Azure.Stores;
using Product.Engine.Configuration.Presets;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Core.Canonical;
using Product.Engine.Testing;
using Product.Engine.Testing.InMemory;
using Product.Mcp.Core.Json;

namespace Product.Azure.Tests;

// The fictional demo seed of the local server (src/adapters/azure/seed): Balanced plus the client binding patch as
// version 1, then the walkthrough estate. The same seed goes into the HTTP host's SQLite file and into the in-process
// world's in-memory store, so both start from the same rows.
internal static class DemoSeed
{
    // Monday 23 November 2026, 09:00 UTC, where the walkthrough starts.
    public static readonly DateTime Start = new(2026, 11, 23, 9, 0, 0, DateTimeKind.Utc);

    // The validation date of version 1, as the MCP test harness compiles it.
    public static readonly DateTime ValidatedOn = new(2026, 11, 22, 9, 0, 0, DateTimeKind.Utc);

    public const string Preset = "balanced";

    private static readonly Lazy<PresetCatalog> LazyCatalog = new(() => PresetCatalog.Load(new[] { SourceRoot.PresetsFolder(TestPaths.Source) }));
    private static readonly Lazy<JsonTreeNode> LazyBinding = new(() => ProductSeeding.ReadJson(TestPaths.DemoBinding));
    private static readonly Lazy<JsonTreeNode> LazyRows = new(() => ProductSeeding.ReadJson(TestPaths.WalkthroughWorld));
    private static readonly Lazy<CompiledSnapshot> LazyVersion = new(() => ProductSeeding.CompileVersion(Catalog, Preset, null, Binding, ProductSeeding.PrincipalKinds(Rows), ValidatedOn));

    public static PresetCatalog Catalog => LazyCatalog.Value;

    public static JsonTreeNode Binding => LazyBinding.Value;

    public static JsonTreeNode Rows => LazyRows.Value;

    // Version 1, compiled once.
    public static CompiledSnapshot Version => LazyVersion.Value;

    // Seeds the demo into a row store (the HTTP host's SQLite file) through the seeding the local server's seed command
    // uses. moreOperations, a JSON array of client binding operations, goes after the demo binding's own; draftOperations,
    // a JSON array of configuration operations (such as a data domain tree), goes in a draft patch after them.
    public static SeedResult Into(IProductStore store, string? moreOperations = null, string? draftOperations = null)
    {
        return ProductSeeding.SeedDemo(store, Catalog, Preset, null, BindingWith(moreOperations, draftOperations), Rows, ValidatedOn);
    }

    // The demo binding with more operations after its own.
    public static JsonTreeNode BindingWith(string? moreOperations, string? draftOperations = null)
    {
        if (string.IsNullOrEmpty(moreOperations) && string.IsNullOrEmpty(draftOperations))
        {
            return Binding;
        }

        JsonNode binding = JsonNode.Parse(CanonicalJson.Write(Binding, CanonicalProfile.Request))!;
        JsonArray operations = binding["bindingOperations"]!.AsArray();
        foreach (JsonNode? operation in string.IsNullOrEmpty(moreOperations) ? new JsonArray() : JsonNode.Parse(moreOperations)!.AsArray())
        {
            operations.Add(operation!.DeepClone());
        }

        if (!string.IsNullOrEmpty(draftOperations))
        {
            binding["draftOperations"] = JsonNode.Parse(draftOperations)!.AsArray();
        }

        return CanonicalJson.Parse(binding.ToJsonString());
    }

    // Seeds the same demo into an in-memory store (the in-process world).
    public static void Into(InMemoryStore store)
    {
        // Published On as the seed command writes it when no seed moment is given: the validation date (wave 4b verify round).
        store.SeedVersion(Version, ValidatedOn);
        foreach (JsonTreeNode row in Js.Items(Rows, "rows"))
        {
            store.Seed(Js.Text(row, "entity")!, Js.Member(row, "columns")!, Js.Text(row, "id"), Js.Text(row, "key"));
        }
    }

    public static IReadOnlyList<KeyValuePair<string, Product.Engine.Core.Contract.PrincipalKind>> PrincipalKinds => ProductSeeding.PrincipalKinds(Rows);
}
