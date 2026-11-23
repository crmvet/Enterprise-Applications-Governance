using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Product.Engine.Core.Canonical;
using Xunit;

namespace Product.Azure.Stores.Tests;

// The entity catalog is the logical model compiled into the engine core: the same entities, keys, tables, schemas and
// history types as model/generated/column-map.json, which the model generator writes from the same model.
public sealed class EntityCatalogTests
{
    [Fact]
    public void TheCatalogHoldsEveryFirstReleaseEntityWithATableOfItsOwn()
    {
        EntityCatalog catalog = EntityCatalog.Embedded;
        IEnumerable<string> expected = Enumerable.Range(1, 46).Where(n => n != 3).Select(n => "C" + n.ToString("D2"))
            .Concat(Enumerable.Range(1, 24).Select(n => "O" + n.ToString("D2")));
        Assert.Equal(expected, catalog.Entities.Select(e => e.ModelId));
        Assert.Equal(69, catalog.Entities.Count);
        Assert.False(catalog.TryGet("preset", out _));
        Assert.False(catalog.TryGet("data-area", out _));
        Assert.Contains("widget", Assert.Throws<InvalidOperationException>(() => catalog.Get("widget")).Message);
        Assert.Equal(catalog.Entities.Count, catalog.Entities.Select(e => e.Table).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void TablesSchemasAndHistoryTypesMatchTheColumnMap()
    {
        using JsonDocument map = JsonDocument.Parse(File.ReadAllText(Path.Combine(Paths.Source, "model", "generated", "column-map.json")));
        var checkedIds = new List<string>();
        foreach (JsonElement entity in map.RootElement.GetProperty("entities").EnumerateArray())
        {
            string id = entity.GetProperty("id").GetString()!;
            string name = entity.GetProperty("name").GetString()!;
            JsonElement azure = entity.GetProperty("azureSql");
            string? history = azure.GetProperty("history").ValueKind == JsonValueKind.String ? azure.GetProperty("history").GetString() : null;
            if (history == null || id == "C03")
            {
                Assert.False(EntityCatalog.Embedded.TryGet(EntityCatalog.EntityKey(name), out EntityInfo? none) && none.ModelId == id);
                continue;
            }

            EntityInfo info = EntityCatalog.Embedded.Get(EntityCatalog.EntityKey(name));
            Assert.Equal(id, info.ModelId);
            Assert.Equal(azure.GetProperty("table").GetString(), info.QualifiedTable);
            Assert.Equal(history, HistoryText(info.Storage));
            checkedIds.Add(id);
        }

        Assert.Equal(EntityCatalog.Embedded.Entities.Select(e => e.ModelId).OrderBy(i => i, StringComparer.Ordinal), checkedIds.OrderBy(i => i, StringComparer.Ordinal));
    }

    [Fact]
    public void HistoryTypesFollowTheHistoryClasses()
    {
        EntityCatalog catalog = EntityCatalog.Embedded;
        Assert.Equal(new[] { "C46", "O08", "O09", "O10" }, catalog.Entities.Where(e => e.Storage == EntityStorage.UpdatableLedger).Select(e => e.ModelId));
        Assert.Equal(new[] { "C02", "O14", "O20", "O24" }, catalog.Entities.Where(e => e.Storage == EntityStorage.AppendOnlyLedger).Select(e => e.ModelId));
        Assert.Equal(61, catalog.Entities.Count(e => e.Storage == EntityStorage.Temporal));
        Assert.Equal("audit", catalog.Get("audit-entry").Schema);
        Assert.Equal("config", catalog.Get("configuration-draft").Schema);
        Assert.Equal("ops", catalog.Get("approval-record").Schema);
        Assert.Equal("[ops].[ApprovalRecordHistory]", catalog.Get("approval-record").QualifiedHistoryTable);
        Assert.Null(catalog.Get("audit-entry").HistoryTable);
    }

    // Every entity a compiled snapshot holds has a table, so SeedVersion and activation never name a missing one.
    [Fact]
    public void EverySnapshotEntityHasATable()
    {
        foreach (string name in World.Version(1).Snapshot.Entities.Select(e => e.Name))
        {
            EntityInfo info = EntityCatalog.Embedded.Get(name);
            Assert.True(info.IsConfiguration, name);
        }
    }

    // Exactly the unique keys the in-memory store enforces, plus the version pair of C02.
    [Fact]
    public void TheUniqueKeysAreTheInMemoryStoresPlusTheVersionPair()
    {
        var unique = EntityCatalog.Embedded.Entities
            .SelectMany(e => e.UniqueColumns.Select(c => e.Key + "." + c.JsonName))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(new[] { "audit-entry.requestKey", "configuration-set-version.configurationSet", "configuration-set-version.versionNumber", "deployment-record.idempotencyKey" }, unique);
        Assert.All(EntityCatalog.Embedded.Entities, e => Assert.Equal(new[] { "PerformedByPrincipalId", "PerformedByActorKind" }, e.Promoted.Take(2).Select(p => p.SqlName)));
    }

    [Fact]
    public void PromotedValuesFollowTheStoredRowReaders()
    {
        EntityInfo set = EntityCatalog.Embedded.Get("configuration-set");
        PromotedColumn inForce = set.Promoted.Single(p => p.JsonName == "isInForce");
        PromotedColumn number = set.Promoted.Single(p => p.JsonName == "currentVersionNumber");
        Assert.Equal(true, inForce.ValueOf(CanonicalJson.Parse("{\"isInForce\":true}")));
        Assert.Equal(false, inForce.ValueOf(CanonicalJson.Parse("{\"isInForce\":1}")));
        Assert.Equal(3, number.ValueOf(CanonicalJson.Parse("{\"currentVersionNumber\":3}")));
        Assert.Null(number.ValueOf(CanonicalJson.Parse("{\"currentVersionNumber\":\"3\"}")));
        Assert.Null(number.ValueOf(CanonicalJson.Parse("{\"currentVersionNumber\":3000000000}")));

        PromotedColumn request = EntityCatalog.Embedded.Get("audit-entry").Promoted.Single(p => p.IsUnique);
        Assert.Null(request.ValueOf(CanonicalJson.Parse("{\"requestKey\":\"\"}")));
        Assert.Equal("k", request.ValueOf(CanonicalJson.Parse("{\"requestKey\":\"k\"}")));
        Assert.True(request.IsTooLong(CanonicalJson.Parse("{\"requestKey\":\"" + new string('k', 401) + "\"}")));
        Assert.Equal("nvarchar(400) COLLATE Latin1_General_BIN2", request.SqlType);
    }

    [Fact]
    public void AnEntityKeyIsTheLogicalNameInKebabCase()
    {
        Assert.Equal("approval-record", EntityCatalog.EntityKey("Approval Record"));
        Assert.Equal("tier", EntityCatalog.EntityKey("Tier"));
        Assert.Equal("configuration-set-version", EntityCatalog.EntityKey("Configuration  Set Version"));
    }

    private static string HistoryText(EntityStorage storage)
    {
        switch (storage)
        {
            case EntityStorage.Temporal:
                return "system-versioned temporal table";
            case EntityStorage.UpdatableLedger:
                return "updatable ledger table";
            default:
                return "append-only ledger table";
        }
    }
}
