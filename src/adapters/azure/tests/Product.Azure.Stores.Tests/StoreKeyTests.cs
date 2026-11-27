using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Product.Engine.Configuration.Composition;
using Product.Engine.Configuration.Model;
using Product.Engine.Configuration.Presets;
using Product.Engine.Configuration.Validation;
using Product.Engine.Core.Canonical;
using Product.Engine.Runtime.Ports;
using Xunit;

namespace Product.Azure.Stores.Tests;

// The store keys (StoreKeys.cs): which keys of the logical model the catalog maps and how, the value a row holds for
// each, what a row change removes and adds, that the shipped presets and the walkthrough estate hold no value twice, and
// how a SQLite file from an earlier build gets its keys. The behavior on every store is in StoreConformanceTests.
public sealed class StoreKeyTests
{
    private static EntityCatalog Catalog => EntityCatalog.Embedded;

    // ---- The catalog ----

    [Fact]
    public void EveryLiveConfigurationEntityIsKeyedByItsKeyPath()
    {
        EntityInfo[] live = Catalog.Entities.Where(e => e.IsConfiguration && e.Key != "configuration-set" && e.Key != "configuration-set-version" && e.Key != "configuration-draft").ToArray();
        Assert.Equal(46, live.Length);
        foreach (EntityInfo entity in live)
        {
            StoreKey key = entity.StoreKeys.Single(k => k.UsesKeyPath);
            Assert.Equal("Key", key.Name);
            Assert.Equal(StoreKeyScope.AllRows, key.Scope);
            Assert.Contains(entity.ModelId + " Key+Configuration Set", key.ModelKeys);
        }

        // The key path of a list value holds its list's key, so one value covers both keys of C07.
        Assert.Equal(new[] { "C07 Key+Configuration Set", "C07 Value List+Key+Configuration Set" }, Catalog.Get("list-value").StoreKeys.Single().ModelKeys);
    }

    [Fact]
    public void TheConfigurationAlternateKeysCountActiveRowsByTheBundlesFieldNames()
    {
        var expected = new Dictionary<string, (string Name, string[] Parts)>(StringComparer.Ordinal)
        {
            ["tier"] = ("Rank", new[] { "rank" }),
            ["vocabulary-mapping"] = ("Source Entity+Source Key+Vocabulary", new[] { "sourceEntity", "sourceKey", "vocabularyKey" }),
            ["lifecycle-stage"] = ("Stage Rank", new[] { "stageRank" }),
            ["review-cadence"] = ("Review Type+Tier", new[] { "reviewTypeKey", "tierKey" }),
            ["role-group-binding"] = ("Role+Entra Group+Binding Method", new[] { "roleKey", "entraGroupObjectId", "bindingMethod" }),
            ["gate-policy"] = ("Tier+Change Type+Target Stage+Portfolio", new[] { "tierKey", "changeTypeKey", "targetStageKey", "portfolioId" }),
            ["guideline"] = ("Number+Version", new[] { "number", "version" }),
            ["workload-grant"] = ("Principal+Workload Role", new[] { "principalObjectId", "workloadRoleKey" }),

            // The data taxonomy (D-37): classification and level ranks are unique among active rows, as tier ranks are.
            ["data-classification"] = ("Rank", new[] { "rank" }),
            ["data-domain-level"] = ("Rank", new[] { "rank" }),
        };

        StoreKey[] active = Catalog.Entities.SelectMany(e => e.StoreKeys).Where(k => k.Scope == StoreKeyScope.ActiveRows).ToArray();
        Assert.Equal(expected.Keys.OrderBy(k => k, StringComparer.Ordinal), active.Select(k => k.EntityKey).OrderBy(k => k, StringComparer.Ordinal));
        foreach (StoreKey key in active)
        {
            Assert.Equal(expected[key.EntityKey].Name, key.Name);
            Assert.Equal(expected[key.EntityKey].Parts, key.Parts);
            Assert.Single(key.ModelKeys);
            Assert.EndsWith("+Configuration Set", key.ModelKeys[0], StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheOperationalKeysUseTheModelsContractNames()
    {
        var expected = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["configuration-set"] = new[] { "Name:name", "Preset Key:presetKey" },
            ["configuration-draft"] = new[] { "Record Number:recordNumber" },
            ["portfolio"] = new[] { "Name+Parent Portfolio:name,parentPortfolio" },
            ["business-application"] = new[] { "Record Number:recordNumber" },
            ["technical-component"] = new[] { "Link Kind+External Id:linkKind,externalId" },
            ["principal-reference"] = new[] { "Entra Object Id:entraObjectId" },
            ["intake-request"] = new[] { "Record Number:recordNumber" },
            ["decision"] = new[] { "Record Number:recordNumber" },
            ["change-request"] = new[] { "Record Number:recordNumber" },
            ["affected-application"] = new[] { "Change Request+Business Application:changeRequest,businessApplication" },
            ["deployment-target"] = new[] { "Link Kind+External Id:linkKind,externalId" },
            ["pipeline-reference"] = new[] { "Link Kind+External Id:linkKind,externalId" },
            ["pipeline-map-entry"] = new[] { "Business Application+Pipeline Reference+Deployment Target:businessApplication,pipelineReference,deploymentTarget" },
            ["document-link"] = new[] { "Business Application+Documentation Requirement:businessApplication,documentationRequirement" },
            ["review"] = new[] { "Record Number:recordNumber" },
            ["claim"] = new[] { "Claim Key:claimKey" },

            // The data taxonomy (D-37; document 22, B22-14). A data use's other key (application, domain, location and AI)
            // counts an empty location as a value, which a store key cannot hold, so the engine checks it.
            ["data-location"] = new[] { "Data Platform+External Id:dataPlatform,externalId" },
            ["data-mapping"] = new[] { "Data Domain+Data Location+Data Origin:dataDomain,dataLocation,dataOrigin" },
            ["data-use"] = new[] { "Record Number:recordNumber" },
        };

        StoreKey[] keys = Catalog.Entities.SelectMany(e => e.StoreKeys).Where(k => !k.UsesKeyPath && k.Scope != StoreKeyScope.ActiveRows).ToArray();
        Assert.Equal(expected.Keys.OrderBy(k => k, StringComparer.Ordinal), keys.Select(k => k.EntityKey).Distinct().OrderBy(k => k, StringComparer.Ordinal));
        foreach (IGrouping<string, StoreKey> entity in keys.GroupBy(k => k.EntityKey))
        {
            Assert.Equal(expected[entity.Key], entity.Select(k => k.Name + ":" + string.Join(",", k.Parts)));
        }

        Assert.Equal(StoreKeyScope.CurrentRows, Catalog.Get("technical-component").StoreKeys.Single().Scope);
        Assert.Equal(StoreKeyScope.InServiceRows, Catalog.Get("data-location").StoreKeys.Single().Scope);
        Assert.Equal(StoreKeyScope.OpenRows, Catalog.Get("data-mapping").StoreKeys.Single().Scope);
        Assert.All(keys.Where(k => k.EntityKey != "technical-component" && k.EntityKey != "data-location" && k.EntityKey != "data-mapping"), k => Assert.Equal(StoreKeyScope.AllRows, k.Scope));

        // The keys the promoted unique columns hold, and the keys held back, are not in the table. A review keeps only its
        // record number (document 25, B25-9): its application, type and due date key stays held back.
        Assert.Empty(Catalog.Get("audit-entry").StoreKeys);
        Assert.Empty(Catalog.Get("deployment-record").StoreKeys);
        Assert.Empty(Catalog.Get("configuration-set-version").StoreKeys);
        Assert.Empty(Catalog.Get("artifact-version").StoreKeys);
        Assert.Equal(new[] { "Record Number" }, Catalog.Get("review").StoreKeys.Select(k => k.Name));
        Assert.Empty(Catalog.Get("approval-record").StoreKeys);
    }

    // ---- Values ----

    [Fact]
    public void AValueIsTheCanonicalArrayOfItsPartsAndItsHashIsTheSha256OfThatText()
    {
        StoreKey grant = Catalog.Get("workload-grant").StoreKeys.Single(k => !k.UsesKeyPath);
        StoreKeyValue value = grant.ValueOf("approval-runner", J("{\"workloadRoleKey\":\"approval-writer\",\"principalObjectId\":\"0000000A-0000-4000-8000-00000000030B\",\"isActive\":true}"))!;
        Assert.Equal("[\"0000000a-0000-4000-8000-00000000030b\",\"approval-writer\"]", value.Text);
        Assert.Equal("0000000a-0000-4000-8000-00000000030b/approval-writer", value.Display);
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(value.Text)), value.Hash);

        StoreKey path = Catalog.Get("list-value").StoreKeys.Single();
        Assert.Equal("[\"tool/other\"]", path.ValueOf("tool/other", J("{\"key\":\"other\"}"))!.Text);
        Assert.Null(path.ValueOf(null, J("{\"key\":\"other\"}")));
        Assert.Null(path.ValueOf(string.Empty, J("{}")));

        StoreKey rank = Catalog.Get("tier").StoreKeys.Single(k => !k.UsesKeyPath);
        Assert.Equal("[3]", rank.ValueOf("tier-3", J("{\"rank\":3}"))!.Text);
        Assert.False(rank.ValueOf("a", J("{\"rank\":3}"))!.SameValue(rank.ValueOf("b", J("{\"rank\":\"3\"}"))));
    }

    [Fact]
    public void ARowHoldsNoValueWhenAPartIsMissingOrTheRowIsOutsideTheKeysScope()
    {
        StoreKey link = Catalog.Get("document-link").StoreKeys.Single();
        Assert.Null(link.ValueOf(null, J("{\"businessApplication\":\"a\"}")));
        Assert.Null(link.ValueOf(null, J("{\"businessApplication\":\"a\",\"documentationRequirement\":null}")));
        Assert.Null(link.ValueOf(null, J("{\"businessApplication\":\"a\",\"documentationRequirement\":\"\"}")));
        Assert.NotNull(link.ValueOf(null, J("{\"businessApplication\":\"a\",\"documentationRequirement\":\"architecture-doc\"}")));

        StoreKey rank = Catalog.Get("tier").StoreKeys.Single(k => !k.UsesKeyPath);
        Assert.NotNull(rank.ValueOf("t", J("{\"rank\":1}")));
        Assert.NotNull(rank.ValueOf("t", J("{\"rank\":1,\"isActive\":true}")));
        Assert.Null(rank.ValueOf("t", J("{\"rank\":1,\"isActive\":false}")));

        // A retired row keeps its key path: the path never comes back as a new row.
        Assert.NotNull(Catalog.Get("tier").StoreKeys.Single(k => k.UsesKeyPath).ValueOf("t", J("{\"rank\":1,\"isActive\":false}")));

        StoreKey component = Catalog.Get("technical-component").StoreKeys.Single();
        Assert.NotNull(component.ValueOf(null, J("{\"linkKind\":\"power-platform-resource\",\"externalId\":\"x\"}")));
        Assert.NotNull(component.ValueOf(null, J("{\"linkKind\":\"power-platform-resource\",\"externalId\":\"x\",\"retiredOn\":null}")));
        Assert.Null(component.ValueOf(null, J("{\"linkKind\":\"power-platform-resource\",\"externalId\":\"x\",\"retiredOn\":\"2026-11-20\"}")));

        // A data location counts while in service: a retired one and a draft withdrawn (archived with no Retired On) hold
        // no value (wave 4b, fix round 2). A component that is archived keeps its key, as before.
        StoreKey location = Catalog.Get("data-location").StoreKeys.Single();
        Assert.NotNull(location.ValueOf(null, J("{\"dataPlatform\":\"sharepoint\",\"externalId\":\"x\",\"statusMeaning\":\"draft\"}")));
        Assert.NotNull(location.ValueOf(null, J("{\"dataPlatform\":\"sharepoint\",\"externalId\":\"x\",\"statusMeaning\":\"registered\",\"retiredOn\":null}")));
        Assert.Null(location.ValueOf(null, J("{\"dataPlatform\":\"sharepoint\",\"externalId\":\"x\",\"statusMeaning\":\"archived\"}")));
        Assert.Null(location.ValueOf(null, J("{\"dataPlatform\":\"sharepoint\",\"externalId\":\"x\",\"statusMeaning\":\"archived\",\"retiredOn\":\"2026-11-20\"}")));
        Assert.Null(location.ValueOf(null, J("{\"dataPlatform\":\"sharepoint\",\"externalId\":\"x\",\"statusMeaning\":\"registered\",\"retiredOn\":\"2026-11-20\"}")));
        Assert.NotNull(component.ValueOf(null, J("{\"linkKind\":\"power-platform-resource\",\"externalId\":\"x\",\"statusMeaning\":\"archived\"}")));
    }

    [Fact]
    public void AGuidPartComparesWithoutRegardToCaseAndOtherTextExactly()
    {
        StoreKey principal = Catalog.Get("principal-reference").StoreKeys.Single();
        StoreKeyValue lower = principal.ValueOf(null, J("{\"entraObjectId\":\"00000000-0000-4000-8000-0000000000ab\"}"))!;
        Assert.True(lower.SameValue(principal.ValueOf(null, J("{\"entraObjectId\":\"00000000-0000-4000-8000-0000000000AB\"}"))));

        StoreKey claim = Catalog.Get("claim").StoreKeys.Single();
        Assert.False(claim.ValueOf(null, J("{\"claimKey\":\"Abc\"}"))!.SameValue(claim.ValueOf(null, J("{\"claimKey\":\"abc\"}"))));
        Assert.False(claim.ValueOf(null, J("{\"claimKey\":\"abc\"}"))!.SameValue(claim.ValueOf(null, J("{\"claimKey\":\"abc \"}"))));
    }

    // ---- Changes ----

    [Fact]
    public void AnInsertAddsEveryValueADeleteRemovesEveryKeyAndAnUpdateOnlyWhatChanged()
    {
        EntityInfo tier = Catalog.Get("tier");
        JsonTreeNode before = J("{\"rank\":1,\"label\":\"One\"}");
        StoredRow committed = new("tier", "t1", "tier-1", before);

        StoreKeyChanges insert = RowChange.Insert(tier, "t1", "tier-1", before).StoreKeys;
        Assert.False(insert.RemovesAll);
        Assert.Equal(new[] { "Key", "Rank" }, insert.Added.Select(v => v.Key.Name));

        StoreKeyChanges delete = RowChange.Delete(tier, "t1", 1).StoreKeys;
        Assert.True(delete.RemovesAll);
        Assert.Empty(delete.Added);

        Assert.True(RowChange.Update(tier, "t1", "tier-1", J("{\"rank\":1,\"label\":\"Renamed\"}"), 1, committed).StoreKeys.IsEmpty);

        StoreKeyChanges rerank = RowChange.Update(tier, "t1", "tier-1", J("{\"rank\":2,\"label\":\"One\"}"), 1, committed).StoreKeys;
        Assert.Equal(new[] { "Rank" }, rerank.Removed.Select(k => k.Name));
        Assert.Equal("[2]", rerank.Added.Single().Text);

        StoreKeyChanges retire = RowChange.Update(tier, "t1", "tier-1", J("{\"rank\":1,\"isActive\":false}"), 1, committed).StoreKeys;
        Assert.Equal(new[] { "Rank" }, retire.Removed.Select(k => k.Name));
        Assert.Empty(retire.Added);

        // Without the row it read, an update replaces every key.
        StoreKeyChanges unknown = RowChange.Update(tier, "t1", "tier-1", J("{\"rank\":1}"), 1).StoreKeys;
        Assert.True(unknown.RemovesAll);
        Assert.Equal(2, unknown.Added.Count);

        Assert.True(RowChange.Insert(Catalog.Get("evidence-link"), "e1", null, J("{}")).StoreKeys.IsEmpty);
    }

    // ---- The shipped configuration and the walkthrough estate ----

    // Every full preset, alone and with every overlay combination its build allows, holds no store key value twice: an
    // activation or a seed of it never meets a store key.
    [Fact]
    public void NoShippedPresetSelectionHoldsAStoreKeyValueTwice()
    {
        PresetCatalog presets = World.Catalog;
        var validator = new ConfigurationValidator(presets);
        int selections = 0;
        foreach (PresetSource full in presets.FullPresets)
        {
            foreach (string[] overlays in new[] { Array.Empty<string>() }.Concat(presets.AllowedOverlayCombinations(full)))
            {
                ValidationOutcome outcome = validator.Validate(new ValidationRequest(ContentSource.Preset(full.Key, overlays), ValidationMode.PresetBuild));
                Assert.True(outcome.Candidate != null, full.Key + " + " + string.Join(" + ", overlays) + " does not compose.");
                var seen = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (ContentRow row in outcome.Candidate!.Content.Rows)
                {
                    EntityInfo entity = Catalog.Get(row.Entity);
                    foreach (StoreKeyValue value in StoreKeyChanges.Values(entity.StoreKeys, row.KeyPath, row.Node))
                    {
                        string slot = entity.Key + " " + value.Key.Name + " " + value.Text;
                        Assert.False(seen.TryGetValue(slot, out string? first), full.Key + " + " + string.Join(" + ", overlays) + ": " + slot + " is held by " + first + " and " + row.KeyPath + ".");
                        seen[slot] = row.KeyPath;
                    }
                }

                selections++;
            }
        }

        Assert.True(selections >= presets.FullPresets.Count(), "every full preset is checked");
    }

    // The fictional estate the local server seeds and the host tests use holds no value twice.
    [Fact]
    public void TheWalkthroughEstateSeedsIntoAStoreThatHoldsTheKeys()
    {
        using JsonDocument world = JsonDocument.Parse(File.ReadAllText(Path.Combine(Paths.Adapter, "seed", "walkthrough-world.json")));
        using RowStoreUnderTest store = Stores.NewSqlite();
        int seeded = 0;
        foreach (JsonElement row in world.RootElement.GetProperty("rows").EnumerateArray())
        {
            store.Seed(row.GetProperty("entity").GetString()!, row.GetProperty("columns").GetRawText(), row.TryGetProperty("id", out JsonElement id) ? id.GetString() : null);
            seeded++;
        }

        Assert.True(seeded > 20, seeded.ToString(System.Globalization.CultureInfo.InvariantCulture));
        string anyPrincipal = world.RootElement.GetProperty("rows").EnumerateArray().First(r => r.GetProperty("entity").GetString() == "principal-reference").GetProperty("columns").GetProperty("entraObjectId").GetString()!;
        Assert.Contains("entraObjectId", Assert.Throws<StoreConflictException>(() => store.Seed("principal-reference", "{\"principalKind\":\"user\",\"entraObjectId\":\"" + anyPrincipal.ToUpperInvariant() + "\",\"isErased\":false}")).Message);
    }

    // ---- Through the engine ----

    // A value the engine writes while another row holds it: the commit fails on the store key, the engine runs the call
    // once more (document 25, section 8) and, when the value is still taken, the conflict reaches the caller. Nothing of
    // either attempt is written. The engine gives record numbers itself (document 25, B25-9), so the test's store hands
    // out the same number twice, as a broken counter would, and the store key still refuses the second row.
    [Fact]
    public void AStoreKeyTakenWhileTheEngineWritesReachesTheCallerAfterTheEnginesOneRetry()
    {
        using IDisposable caller = TestCaller.Enter();
        using RowStoreUnderTest store = Stores.NewSqlite();
        store.SeedVersion(World.Version(1));
        World.SeedDirectoryAndEstate(store);
        var records = new CountingRecords(store.Records) { FixedRecordNumber = 7 };
        var runtime = new Product.Engine.Runtime.EngineRuntime(records, store.Configuration, World.Catalog, clock: new Product.Engine.Testing.InMemory.ManualClock(World.AsOf));
        string intake = "{\"requester\":\"" + World.Person1 + "\",\"title\":\"Fixture keyed intake\",\"need\":\"Fixture need\"}";

        Product.Engine.Runtime.RuntimeOutcome first = runtime.Write(World.PersonOne, Product.Engine.Runtime.WriteCommand.Create("intake-request", intake));
        Assert.True(first.RowId != null, first.ToString());
        int begunBefore = records.Begun;

        Assert.Throws<StoreConflictException>(() => runtime.Write(World.PersonOne, Product.Engine.Runtime.WriteCommand.Create("intake-request", intake.Replace("Fixture keyed intake", "Second"))));
        Assert.Equal(2, records.Begun - begunBefore);
        Assert.Equal(new[] { first.RowId }, store.Records.Query(RowQuery.For("intake-request").Where("recordNumber", "IR-0007")).Select(r => r.Id));
    }

    // Counts the transactions the engine begins.
    private sealed class CountingRecords : IRecordStore
    {
        private readonly IRecordStore _inner;

        public CountingRecords(IRecordStore inner)
        {
            _inner = inner;
        }

        public int Begun { get; private set; }

        // When set, every record number the engine asks for is this one.
        public long? FixedRecordNumber { get; set; }

        public string StoreId => _inner.StoreId;

        public StoredRow? Get(string entity, string id)
        {
            return _inner.Get(entity, id);
        }

        public StoredRow? GetByKey(string entity, string key)
        {
            return _inner.GetByKey(entity, key);
        }

        public IReadOnlyList<StoredRow> Query(RowQuery query)
        {
            return _inner.Query(query);
        }

        public IStoreTransaction Begin()
        {
            Begun++;
            IStoreTransaction tx = _inner.Begin();
            return FixedRecordNumber is long number ? new FixedNumbers(tx, number) : tx;
        }
    }

    // A transaction whose record numbers are always the same: a counter that gives a number twice.
    private sealed class FixedNumbers : IStoreTransaction
    {
        private readonly IStoreTransaction _inner;
        private readonly long _number;

        public FixedNumbers(IStoreTransaction inner, long number)
        {
            _inner = inner;
            _number = number;
        }

        public StoredRow? Get(string entity, string id) => _inner.Get(entity, id);

        public StoredRow? GetByKey(string entity, string key) => _inner.GetByKey(entity, key);

        public IReadOnlyList<StoredRow> Query(RowQuery query) => _inner.Query(query);

        public string NewId(string entity) => _inner.NewId(entity);

        public long NextRecordNumber(string entity, string prefix) => _number;

        public void Create(string entity, string id, string? key, Product.Engine.Core.Canonical.JsonTreeNode columns) => _inner.Create(entity, id, key, columns);

        public void Update(string entity, string id, Product.Engine.Core.Canonical.JsonTreeNode columns) => _inner.Update(entity, id, columns);

        public void Delete(string entity, string id) => _inner.Delete(entity, id);

        public void Commit() => _inner.Commit();

        public void Dispose() => _inner.Dispose();
    }

    // ---- A SQLite file from an earlier build ----

    [Fact]
    public void ASqliteFileFromAnEarlierBuildGetsItsStoreKeysOnFirstUse()
    {
        string folder = Paths.NewScratchFolder();
        string file = Path.Combine(folder, "product.db");
        try
        {
            string principal = "00000000-0000-4000-8000-" + Guid.NewGuid().ToString("N").Substring(0, 12);
            using (RowStoreUnderTest store = Stores.NewSqlite(file))
            {
                store.Seed("principal-reference", "{\"principalKind\":\"user\",\"entraObjectId\":\"" + principal + "\",\"isErased\":false}");
                store.Seed("business-application", "{\"recordNumber\":\"APP-0042\"}");
            }

            ToEarlierBuild(file);
            using (RowStoreUnderTest upgraded = Stores.NewSqlite(file, "00000000-0000-4000-9001-"))
            {
                upgraded.Store.EnsureSchema();
                Assert.True(upgraded.Store.CanConnect());
                Assert.Throws<StoreConflictException>(() => upgraded.Seed("principal-reference", "{\"principalKind\":\"user\",\"entraObjectId\":\"" + principal + "\",\"isErased\":false}", Guid.NewGuid().ToString("D")));
                Assert.Throws<StoreConflictException>(() => upgraded.Seed("business-application", "{\"recordNumber\":\"APP-0042\"}", Guid.NewGuid().ToString("D")));
                upgraded.Seed("business-application", "{\"recordNumber\":\"APP-0043\"}", Guid.NewGuid().ToString("D"));
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Stores.TryDelete(folder);
        }
    }

    // Wave 4b, fix round 2. A draft location that is withdrawn (archived, no Retired On) gives its External Id back in the
    // same commit, so the location added again is stored; a location in service still holds it.
    [Fact]
    public void AWithdrawnDraftLocationFreesItsExternalIdAndOneInServiceHoldsIt()
    {
        using RowStoreUnderTest store = Stores.NewSqlite();
        string draft = store.Seed("data-location", "{\"dataPlatform\":\"sharepoint\",\"externalId\":\"ext-0001\",\"statusMeaning\":\"draft\"}");
        Assert.Throws<StoreConflictException>(() => store.Seed("data-location", "{\"dataPlatform\":\"sharepoint\",\"externalId\":\"ext-0001\",\"statusMeaning\":\"registered\"}"));

        store.Replace("data-location", draft, "{\"dataPlatform\":\"sharepoint\",\"externalId\":\"ext-0001\",\"statusMeaning\":\"archived\"}");
        store.Seed("data-location", "{\"dataPlatform\":\"sharepoint\",\"externalId\":\"ext-0001\",\"statusMeaning\":\"registered\"}");
        Assert.Throws<StoreConflictException>(() => store.Seed("data-location", "{\"dataPlatform\":\"sharepoint\",\"externalId\":\"ext-0001\",\"statusMeaning\":\"draft\"}"));
    }

    // Version 4 builds the keys again, so a draft location withdrawn under version 3 (archived, its key row kept) frees
    // its External Id on first use.
    [Fact]
    public void ASqliteFileFromVersionThreeFreesTheExternalIdOfAWithdrawnDraftLocation()
    {
        string folder = Paths.NewScratchFolder();
        string file = Path.Combine(folder, "product.db");
        try
        {
            using (RowStoreUnderTest store = Stores.NewSqlite(file))
            {
                store.Seed("data-location", "{\"dataPlatform\":\"sharepoint\",\"externalId\":\"ext-0001\",\"statusMeaning\":\"draft\"}", "location-1");
            }

            // What version 3 left: the row archived, and its key row still there.
            SqliteConnection.ClearAllPools();
            Execute(file, "UPDATE \"Row\" SET \"Columns\" = '{\"dataPlatform\":\"sharepoint\",\"externalId\":\"ext-0001\",\"statusMeaning\":\"archived\"}' WHERE \"Id\" = 'location-1'; DELETE FROM \"SchemaVersion\"; INSERT INTO \"SchemaVersion\" (\"Version\", \"AppliedOn\") VALUES (3, '2026-11-25T09:00:00.000Z');");
            Assert.Equal(1L, Scalar(file, "SELECT COUNT(*) FROM \"RowKey\" WHERE \"Entity\" = 'data-location'"));
            Assert.Equal(3L, Scalar(file, "SELECT MAX(\"Version\") FROM \"SchemaVersion\""));

            using (RowStoreUnderTest upgraded = Stores.NewSqlite(file, "00000000-0000-4000-9002-"))
            {
                upgraded.Store.EnsureSchema();
                upgraded.Seed("data-location", "{\"dataPlatform\":\"sharepoint\",\"externalId\":\"ext-0001\",\"statusMeaning\":\"registered\"}", Guid.NewGuid().ToString("D"));
                Assert.Throws<StoreConflictException>(() => upgraded.Seed("data-location", "{\"dataPlatform\":\"sharepoint\",\"externalId\":\"ext-0001\",\"statusMeaning\":\"draft\"}", Guid.NewGuid().ToString("D")));
            }

            Assert.Equal(4L, Scalar(file, "SELECT MAX(\"Version\") FROM \"SchemaVersion\""));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Stores.TryDelete(folder);
        }
    }

    [Fact]
    public void ASqliteFileFromAnEarlierBuildThatHoldsAValueTwiceIsRefusedAndLeftAsItWas()
    {
        string folder = Paths.NewScratchFolder();
        string file = Path.Combine(folder, "product.db");
        try
        {
            using (RowStoreUnderTest store = Stores.NewSqlite(file))
            {
                store.Seed("claim", "{\"claimKey\":\"unregistered|app|fixture-1\"}", "claim-1");
                store.Seed("claim", "{\"claimKey\":\"unregistered|app|fixture-2\"}", "claim-2");
            }

            // An earlier build held no store key, so it could write the same claim key twice.
            ToEarlierBuild(file);
            Execute(file, "UPDATE \"Row\" SET \"Columns\" = '{\"claimKey\":\"unregistered|app|fixture-1\"}' WHERE \"Id\" = 'claim-2'");

            using (RowStoreUnderTest upgraded = Stores.NewSqlite(file))
            {
                InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => upgraded.Store.EnsureSchema());
                Assert.Contains("two rows with one value", refused.Message);
                Assert.Contains("claim claimKey \"unregistered|app|fixture-1\"", refused.Message);
                Assert.False(upgraded.Store.CanConnect());
            }

            SqliteConnection.ClearAllPools();
            Assert.Equal(2L, Scalar(file, "SELECT MAX(\"Version\") FROM \"SchemaVersion\""));
            Assert.Equal(0L, Scalar(file, "SELECT COUNT(*) FROM sqlite_master WHERE \"name\" = 'RowKey'"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Stores.TryDelete(folder);
        }
    }

    // ---- Helpers ----

    private static JsonTreeNode J(string json)
    {
        return CanonicalJson.Parse(json);
    }

    // Makes the file look as schema version 2 left it: no store key table.
    private static void ToEarlierBuild(string file)
    {
        SqliteConnection.ClearAllPools();
        Execute(file, "DROP TABLE \"RowKey\"; DELETE FROM \"SchemaVersion\"; INSERT INTO \"SchemaVersion\" (\"Version\", \"AppliedOn\") VALUES (2, '2026-11-22T09:00:00.000Z');");
        Assert.Equal(2L, Scalar(file, "SELECT MAX(\"Version\") FROM \"SchemaVersion\""));
    }

    private static void Execute(string file, string sql)
    {
        using var connection = new SqliteConnection("Data Source=" + file);
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long Scalar(string file, string sql)
    {
        SqliteConnection.ClearAllPools();
        using var connection = new SqliteConnection("Data Source=" + file);
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
