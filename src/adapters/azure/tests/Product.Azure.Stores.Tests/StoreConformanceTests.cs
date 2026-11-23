using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Snapshots;
using Product.Engine.Runtime.Ports;
using Xunit;

namespace Product.Azure.Stores.Tests;

// The storage port contract, run the same way against every store: InMemoryStore (the reference the engine tests use),
// the SQLite store and, when PRODUCT_TEST_SQLSERVER is set, the Azure SQL store. A case marked "row stores" checks what
// only a database store does: optimistic versions, the model's entity list and append-only entities.
//
// Every case uses fresh IDs and markers, so the Azure SQL cases can share one database.
public abstract class StoreConformanceTests
{
    protected abstract StoreKind Kind { get; }

    // ---- Transactions ----

    [Fact]
    public void ATransactionReadsItsOwnWritesAndCommittedReadsDoNot()
    {
        using IStoreUnderTest store = Stores.Create(Kind);
        using IDisposable caller = TestCaller.Enter();
        string id = Id();
        string marker = Marker();
        string key = "fixture-key-" + marker;
        JsonTreeNode columns = J("{\"level\":\"department\",\"marker\":\"" + marker + "\"}");

        using (IStoreTransaction tx = store.Records.Begin())
        {
            tx.Create("portfolio", id, key, columns);
            Assert.Equal(columns, tx.Get("portfolio", id)!.Columns);
            Assert.Equal(id, tx.GetByKey("portfolio", key)!.Id);
            Assert.Equal(id, Assert.Single(tx.Query(RowQuery.For("portfolio").Where("marker", marker))).Id);

            Assert.Null(store.Records.Get("portfolio", id));
            Assert.Null(store.Records.GetByKey("portfolio", key));
            Assert.Empty(store.Records.Query(RowQuery.For("portfolio").Where("marker", marker)));
            tx.Commit();
        }

        StoredRow row = store.Records.Get("portfolio", id)!;
        Assert.Equal(columns, row.Columns);
        Assert.Equal(key, row.Key);
        Assert.Equal("portfolio", row.Entity);
        Assert.Equal(id, store.Records.GetByKey("portfolio", key)!.Id);
    }

    [Fact]
    public void DisposingWithoutCommitDiscardsEveryWrite()
    {
        using IStoreUnderTest store = Stores.Create(Kind);
        using IDisposable caller = TestCaller.Enter();
        string id = Id();
        string existing = store.Seed("portfolio", "{\"level\":\"team\"}", Id());

        using (IStoreTransaction tx = store.Records.Begin())
        {
            tx.Create("portfolio", id, null, J("{\"level\":\"department\"}"));
            tx.Update("portfolio", existing, J("{\"level\":\"division\"}"));
        }

        Assert.Null(store.Records.Get("portfolio", id));
        Assert.Equal("team", store.Records.Get("portfolio", existing)!.Text("level"));

        using IStoreTransaction again = store.Records.Begin();
        again.Create("portfolio", id, null, J("{\"level\":\"department\"}"));
        again.Commit();
        Assert.NotNull(store.Records.Get("portfolio", id));
    }

    [Fact]
    public void CreatingAnExistingRowOrChangingAMissingRowThrows()
    {
        using IStoreUnderTest store = Stores.Create(Kind);
        using IDisposable caller = TestCaller.Enter();
        string id = store.Seed("portfolio", "{\"level\":\"team\"}", Id());
        string missing = Id();

        using IStoreTransaction tx = store.Records.Begin();
        Assert.Throws<InvalidOperationException>(() => tx.Create("portfolio", id, null, J("{}")));
        Assert.Throws<InvalidOperationException>(() => tx.Update("portfolio", missing, J("{\"level\":\"team\"}")));
        Assert.Throws<InvalidOperationException>(() => tx.Delete("portfolio", missing));

        tx.Create("portfolio", missing, null, J("{}"));
        Assert.Throws<InvalidOperationException>(() => tx.Create("portfolio", missing, null, J("{}")));
    }

    [Fact]
    public void AnUpdateMergesColumnsAJsonNullClearsOneAndTheKeyStays()
    {
        using IStoreUnderTest store = Stores.Create(Kind);
        using IDisposable caller = TestCaller.Enter();
        string key = "fixture-key-" + Marker();
        string id = store.Seed("portfolio", "{\"a\":1,\"b\":\"x\",\"c\":true}", Id(), key);
        JsonTreeNode expected = J("{\"a\":1,\"c\":false,\"d\":[2,1]}");

        using (IStoreTransaction tx = store.Records.Begin())
        {
            tx.Update("portfolio", id, J("{\"b\":null,\"c\":false,\"d\":[2,1]}"));
            StoredRow pending = tx.Get("portfolio", id)!;
            Assert.Equal(expected, pending.Columns);
            Assert.Equal(key, pending.Key);
            tx.Update("portfolio", id, J("{\"e\":\"second\"}"));
            tx.Commit();
        }

        StoredRow row = store.Records.Get("portfolio", id)!;
        Assert.Equal(J("{\"a\":1,\"c\":false,\"d\":[2,1],\"e\":\"second\"}"), row.Columns);
        Assert.Equal(key, row.Key);
    }

    [Fact]
    public void ADeleteHidesTheRowInTheTransactionAndRemovesItAtCommit()
    {
        using IStoreUnderTest store = Stores.Create(Kind);
        using IDisposable caller = TestCaller.Enter();
        string marker = Marker();
        string key = "fixture-key-" + marker;
        string id = store.Seed("portfolio", "{\"marker\":\"" + marker + "\"}", Id(), key);

        using (IStoreTransaction tx = store.Records.Begin())
        {
            tx.Delete("portfolio", id);
            Assert.Null(tx.Get("portfolio", id));
            Assert.Null(tx.GetByKey("portfolio", key));
            Assert.Empty(tx.Query(RowQuery.For("portfolio").Where("marker", marker)));
            Assert.Throws<InvalidOperationException>(() => tx.Delete("portfolio", id));
            Assert.Throws<InvalidOperationException>(() => tx.Update("portfolio", id, J("{\"level\":\"team\"}")));
            Assert.NotNull(store.Records.Get("portfolio", id));
            tx.Commit();
        }

        Assert.Null(store.Records.Get("portfolio", id));
        Assert.Null(store.Records.GetByKey("portfolio", key));
    }

    [Fact]
    public void ARowDeletedAndCreatedAgainInOneTransactionHoldsTheNewColumns()
    {
        using IStoreUnderTest store = Stores.Create(Kind);
        using IDisposable caller = TestCaller.Enter();
        string id = store.Seed("portfolio", "{\"level\":\"team\"}", Id(), "fixture-old-" + Marker());
        string newKey = "fixture-new-" + Marker();

        using (IStoreTransaction tx = store.Records.Begin())
        {
            tx.Delete("portfolio", id);
            tx.Create("portfolio", id, newKey, J("{\"level\":\"division\"}"));
            tx.Commit();
        }

        StoredRow row = store.Records.Get("portfolio", id)!;
        Assert.Equal("division", row.Text("level"));
        Assert.Equal(newKey, row.Key);
    }

    [Fact]
    public void ARowCreatedAndChangedInOneTransactionCommitsOnce()
    {
        using IStoreUnderTest store = Stores.Create(Kind);
        using IDisposable caller = TestCaller.Enter();
        string audit = Id();
        string portfolio = Id();

        using (IStoreTransaction tx = store.Records.Begin())
        {
            // An Audit Entry is append-only: changing it before the commit is still one insert.
            tx.Create("audit-entry", audit, null, J("{\"operation\":\"fixture\",\"requestKey\":\"" + Marker() + "\"}"));
            tx.Update("audit-entry", audit, J("{\"correlationId\":\"req-1\"}"));
            tx.Create("portfolio", portfolio, null, J("{\"level\":\"team\"}"));
            tx.Delete("portfolio", portfolio);
            tx.Commit();
        }

        Assert.Equal("req-1", store.Records.Get("audit-entry", audit)!.Text("correlationId"));
        Assert.Null(store.Records.Get("portfolio", portfolio));
    }

    [Fact]
    public void WritesAfterCommitOrDisposeThrow()
    {
        using IStoreUnderTest store = Stores.Create(Kind);
        using IDisposable caller = TestCaller.Enter();
        IStoreTransaction committed = store.Records.Begin();
        committed.Create("portfolio", Id(), null, J("{}"));
        committed.Commit();
        Assert.Throws<InvalidOperationException>(() => committed.Create("portfolio", Id(), null, J("{}")));
        Assert.Throws<InvalidOperationException>(() => committed.Commit());
        committed.Dispose();

        IStoreTransaction disposed = store.Records.Begin();
        disposed.Dispose();
        Assert.Throws<InvalidOperationException>(() => disposed.Create("portfolio", Id(), null, J("{}")));
        Assert.Throws<InvalidOperationException>(() => disposed.Commit());
    }

    [Fact]
    public void ACommitWithNoWritesChangesNothing()
    {
        using IStoreUnderTest store = Stores.Create(Kind);
        using IDisposable caller = TestCaller.Enter();
        string id = store.Seed("portfolio", "{\"level\":\"team\"}", Id());
        using IStoreTransaction tx = store.Records.Begin();
        Assert.NotNull(tx.Get("portfolio", id));
        tx.Commit();
        Assert.Equal("team", store.Records.Get("portfolio", id)!.Text("level"));
    }

    [Fact]
    public void NewIdsAreDistinctGuids()
    {
        using IStoreUnderTest store = Stores.Create(Kind);
        using IStoreTransaction tx = store.Records.Begin();
        var ids = Enumerable.Range(0, 50).Select(_ => tx.NewId("decision")).ToList();
        Assert.Equal(50, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, id => Assert.True(Guid.TryParseExact(id, "D", out _) && id == id.ToLowerInvariant(), id));
    }

    // ---- Queries ----

    [Fact]
    public void AQueryMatchesAJsonNullAsAnAbsentColumnAndComparesValuesAsJson()
    {
        using IStoreUnderTest store = Stores.Create(Kind);
        using IDisposable caller = TestCaller.Enter();
        string m = Marker();
        string both = store.Seed("portfolio", "{\"marker\":\"" + m + "\",\"n\":5,\"flag\":true}", Id());
        string noFlag = store.Seed("portfolio", "{\"marker\":\"" + m + "\",\"n\":6}", Id());
        string falseFlag = store.Seed("portfolio", "{\"marker\":\"" + m + "\",\"n\":5,\"flag\":false,\"note\":null}", Id());
        string text = store.Seed("portfolio", "{\"marker\":\"" + m + "\",\"n\":\"5\",\"flag\":\"true\"}", Id());

        RowQuery all = RowQuery.For("portfolio").Where("marker", m);
        Assert.Equal(Sorted(both, noFlag, falseFlag, text), Ids(store.Records.Query(all)));
        Assert.Equal(Sorted(noFlag), Ids(store.Records.Query(all.Where("flag", JsonTreeNode.Null))));
        Assert.Equal(Sorted(both, noFlag, falseFlag, text), Ids(store.Records.Query(all.Where("note", (string?)null))));
        Assert.Equal(Sorted(falseFlag), Ids(store.Records.Query(all.Where("flag", JsonTreeNode.False))));
        Assert.Equal(Sorted(both, falseFlag), Ids(store.Records.Query(all.Where("n", JsonTreeNode.FromInt64(5)))));
        Assert.Equal(Sorted(text), Ids(store.Records.Query(all.Where("n", "5"))));
        Assert.Equal(Sorted(both), Ids(store.Records.Query(all.Where("flag", JsonTreeNode.True))));
        Assert.Equal(Sorted(text), Ids(store.Records.Query(all.Where("flag", "true"))));
        Assert.Equal(Sorted(both), Ids(store.Records.Query(all.Where("n", JsonTreeNode.FromInt64(5)).Where("flag", JsonTreeNode.True))));
        Assert.Empty(store.Records.Query(all.Where("marker", m.ToUpperInvariant())));
        Assert.Empty(store.Records.Query(RowQuery.For("portfolio").Where("marker", m + " ")));
    }

    [Fact]
    public void QueryResultsAreInOrdinalIdOrderInsideAndOutsideATransaction()
    {
        using IStoreUnderTest store = Stores.Create(Kind);
        using IDisposable caller = TestCaller.Enter();
        string m = Marker();
        string prefix = Guid.NewGuid().ToString("N").Substring(0, 30) + "-";
        foreach (string suffix in new[] { "a", "_", "B", "0" })
        {
            store.Seed("portfolio", "{\"marker\":\"" + m + "\"}", prefix + suffix);
        }

        RowQuery query = RowQuery.For("portfolio").Where("marker", m);
        Assert.Equal(new[] { prefix + "0", prefix + "B", prefix + "_", prefix + "a" }, Ids(store.Records.Query(query)));

        using IStoreTransaction tx = store.Records.Begin();
        tx.Create("portfolio", prefix + "A", null, J("{\"marker\":\"" + m + "\"}"));
        tx.Update("portfolio", prefix + "_", J("{\"marker\":\"other\"}"));
        Assert.Equal(new[] { prefix + "0", prefix + "A", prefix + "B", prefix + "a" }, Ids(tx.Query(query)));
    }

    [Fact]
    public void AQuerySeesARowAPendingUpdateMakesMatch()
    {
        using IStoreUnderTest store = Stores.Create(Kind);
        using IDisposable caller = TestCaller.Enter();
        string m = Marker();
        string id = store.Seed("portfolio", "{\"marker\":\"before-" + m + "\"}", Id());
        using IStoreTransaction tx = store.Records.Begin();
        tx.Update("portfolio", id, J("{\"marker\":\"" + m + "\"}"));
        Assert.Equal(id, Assert.Single(tx.Query(RowQuery.For("portfolio").Where("marker", m))).Id);
        Assert.Empty(tx.Query(RowQuery.For("portfolio").Where("marker", "before-" + m)));
    }

    [Fact]
    public void AQueryByAValueLongerThanTheDatabaseNarrowsByStillMatchesExactly()
    {
        using IStoreUnderTest store = Stores.Create(Kind);
        using IDisposable caller = TestCaller.Enter();
        string m = Marker();
        string longText = new string('x', 5000) + m;
        string match = store.Seed("portfolio", "{\"marker\":\"" + m + "\",\"text\":\"" + longText + "\"}", Id());
        store.Seed("portfolio", "{\"marker\":\"" + m + "\",\"text\":\"" + longText + "y\"}", Id());
        Assert.Equal(match, Assert.Single(store.Records.Query(RowQuery.For("portfolio").Where("text", longText))).Id);
    }

    [Fact]
    public void ColumnsRoundTripExactly()
    {
        using IStoreUnderTest store = Stores.Create(Kind);
        using IDisposable caller = TestCaller.Enter();
        string json = "{\"text\":\"Zo\u00eb \\\"quoted\\\" \\\\ back\\nline\\ttab \ud83d\ude00 \\u0001\",\"n\":12.50,\"big\":12345678901234567890,\"neg\":-0.5,"
            + "\"exp\":1e3,\"arr\":[3,1,2,{\"z\":1,\"a\":[]}],\"obj\":{\"b\":1,\"a\":{}},\"t\":true,\"f\":false,\"nul\":null,\"empty\":\"\"}";
        JsonTreeNode expected = J(json);
        string seeded = store.Seed("portfolio", json, Id());
        Assert.Equal(expected, store.Records.Get("portfolio", seeded)!.Columns);
        Assert.Equal(CanonicalJson.Write(expected, CanonicalProfile.Request), CanonicalJson.Write(store.Records.Get("portfolio", seeded)!.Columns, CanonicalProfile.Request));

        string created = Id();
        using (IStoreTransaction tx = store.Records.Begin())
        {
            tx.Create("portfolio", created, null, expected);
            tx.Commit();
        }

        Assert.Equal(expected, store.Records.Get("portfolio", created)!.Columns);
    }

    // ---- Unique keys ----

    [Theory]
    [InlineData("audit-entry", "requestKey")]
    [InlineData("deployment-record", "idempotencyKey")]
    public void AUniqueKeyTakenByAnEarlierCommitThrowsAConflict(string entity, string column)
    {
        using IStoreUnderTest store = Stores.Create(Kind);
        using IDisposable caller = TestCaller.Enter();
        string value = "fixture-" + Marker();
        IStoreTransaction first = store.Records.Begin();
        IStoreTransaction second = store.Records.Begin();
        string firstId = Id();
        string secondId = Id();
        first.Create(entity, firstId, null, J("{\"" + column + "\":\"" + value + "\"}"));
        second.Create(entity, secondId, null, J("{\"" + column + "\":\"" + value + "\"}"));
        first.Commit();

        StoreConflictException conflict = Assert.Throws<StoreConflictException>(() => second.Commit());
        Assert.Equal(entity + " " + column + " \"" + value + "\" is taken by a transaction that committed first.", conflict.Message);
        second.Dispose();
        first.Dispose();
        Assert.NotNull(store.Records.Get(entity, firstId));
        Assert.Null(store.Records.Get(entity, secondId));

        // The same value on other rows is free once it differs, and an empty value is never a key.
        using IStoreTransaction third = store.Records.Begin();
        third.Create(entity, Id(), null, J("{\"" + column + "\":\"" + value + "-2\"}"));
        third.Create(entity, Id(), null, J("{\"" + column + "\":\"\"}"));
        third.Create(entity, Id(), null, J("{\"" + column + "\":\"\"}"));
        third.Commit();
    }

    // ---- Keys and the version probe ----

    // A key path matches only itself: Azure SQL ignores trailing spaces when it compares strings, so the store compares
    // the keys it reads back exactly, as the in-memory store does.
    [Fact]
    public void AKeyPathMatchesOnlyItself()
    {
        using IStoreUnderTest store = Stores.Create(Kind);
        using IDisposable caller = TestCaller.Enter();
        string key = "fixture-key-" + Marker();
        string bare = store.Seed("portfolio", "{\"level\":\"team\"}", Id(), key);
        string spaced = store.Seed("portfolio", "{\"level\":\"division\"}", Id(), key + " ");

        Assert.Equal(bare, store.Records.GetByKey("portfolio", key)!.Id);
        Assert.Equal(spaced, store.Records.GetByKey("portfolio", key + " ")!.Id);
        Assert.Null(store.Records.GetByKey("portfolio", key + "  "));
        using IStoreTransaction tx = store.Records.Begin();
        Assert.Equal(bare, tx.GetByKey("portfolio", key)!.Id);
        Assert.Equal(spaced, tx.GetByKey("portfolio", key + " ")!.Id);
    }

    // The probe reads the first working set in force (by ID) and answers only when its version number is 1 or more.
    [Fact]
    public void TheProbeAnswersOnlyForASetInForceWithAVersion()
    {
        using IStoreUnderTest store = Stores.Create(Kind);
        Assert.SkipWhen(Kind == StoreKind.SqlServer, "The shared Azure SQL database holds the sets of other tests.");
        using IDisposable caller = TestCaller.Enter();
        Assert.Null(store.Configuration.ProbeInForceVersion());

        store.Seed("configuration-set", "{\"setKind\":\"working\",\"isInForce\":false,\"currentVersionNumber\":3}", "00000000-0000-4000-a000-000000000001", "fixture-not-in-force");
        store.Seed("configuration-set", "{\"setKind\":\"working\",\"isInForce\":true}", "00000000-0000-4000-a000-000000000002");
        Assert.Null(store.Configuration.ProbeInForceVersion());

        store.Seed("configuration-set", "{\"setKind\":\"working\",\"isInForce\":true,\"currentVersionNumber\":0}", "00000000-0000-4000-a000-000000000003", "fixture-no-version");
        Assert.Null(store.Configuration.ProbeInForceVersion());

        store.Replace("configuration-set", "00000000-0000-4000-a000-000000000003", "{\"setKind\":\"working\",\"isInForce\":true,\"currentVersionNumber\":2}");
        InForceVersion probe = store.Configuration.ProbeInForceVersion()!;
        Assert.Equal("fixture-no-version", probe.ConfigurationSetKey);
        Assert.Equal(2, probe.VersionNumber);
        Assert.Null(store.Configuration.GetVersion("fixture-no-version", 2));
    }

    // ---- Seeding ----

    [Fact]
    public void SeedingMakesIdsAndReplacesARowWithTheSameId()
    {
        using IStoreUnderTest store = Stores.Create(Kind);
        using IDisposable caller = TestCaller.Enter();
        string made = store.Seed("portfolio", "{\"level\":\"team\"}");
        Assert.False(string.IsNullOrEmpty(made));
        Assert.Equal("team", store.Records.Get("portfolio", made)!.Text("level"));

        string given = Id();
        string key = "fixture-key-" + Marker();
        Assert.Equal(given, store.Seed("portfolio", "{\"level\":\"team\"}", given, key));
        store.Seed("portfolio", "{\"level\":\"division\"}", given, key);
        StoredRow row = store.Records.Get("portfolio", given)!;
        Assert.Equal(J("{\"level\":\"division\"}"), row.Columns);
        Assert.Equal(key, row.Key);
    }

    [Fact]
    public void ReplaceChangesTheColumnsAndKeepsTheKey()
    {
        using IStoreUnderTest store = Stores.Create(Kind);
        using IDisposable caller = TestCaller.Enter();
        string key = "fixture-key-" + Marker();
        string id = store.Seed("portfolio", "{\"level\":\"team\",\"extra\":1}", Id(), key);
        store.Replace("portfolio", id, "{\"level\":\"division\"}");
        StoredRow row = store.Records.Get("portfolio", id)!;
        Assert.Equal(J("{\"level\":\"division\"}"), row.Columns);
        Assert.Equal(key, row.Key);
    }

    [Fact]
    public void ASeededVersionAnswersTheProbeAndTheVersionReadAndALaterOneReplacesTheLiveRows()
    {
        using IStoreUnderTest store = Stores.Create(Kind);
        using IDisposable caller = TestCaller.Enter();
        int first = FirstVersionNumber();
        CompiledSnapshot one = World.Version(first);
        CompiledSnapshot two = World.Version(first + 1);

        store.SeedVersion(one);
        InForceVersion probe = store.Configuration.ProbeInForceVersion()!;
        Assert.Equal(one.ConfigurationSetKey, probe.ConfigurationSetKey);
        Assert.Equal(first, probe.VersionNumber);
        StoredVersion version = store.Configuration.GetVersion(one.ConfigurationSetKey, first)!;
        Assert.Equal(one.ContentHash, version.ContentHash);
        Assert.Equal(one.ContentJson, version.ContentJson);
        Assert.Null(store.Configuration.GetVersion(one.ConfigurationSetKey, first + 1));
        Assert.Null(store.Configuration.GetVersion("no-such-set", first));

        SnapshotEntity tiers = one.Snapshot.Entities.Single(e => e.Name == "tier");
        SnapshotRow tier = tiers.Rows.First();
        StoredRow live = store.Records.GetByKey("tier", tier.KeyPath)!;
        Assert.Equal(tier.Key, live.Text("key"));
        Assert.True(live.Flag("isActive") == tier.IsActive);
        Assert.NotNull(live.Text("origin"));

        store.SeedVersion(two);
        Assert.Equal(first + 1, store.Configuration.ProbeInForceVersion()!.VersionNumber);
        Assert.Equal(one.ContentHash, store.Configuration.GetVersion(one.ConfigurationSetKey, first)!.ContentHash);
        Assert.Equal(two.ContentHash, store.Configuration.GetVersion(two.ConfigurationSetKey, first + 1)!.ContentHash);
        Assert.Equal(tiers.Rows.Count, store.Records.Query(RowQuery.For("tier")).Count(r => r.Key != null));
        Assert.Single(store.Records.Query(RowQuery.For("configuration-set")), s => s.Key == one.ConfigurationSetKey);
    }

    // ---- Counters ----

    [Fact]
    public void ACounterCountsInItsWindowAndStartsAgainInTheNext()
    {
        using IStoreUnderTest store = Stores.Create(Kind);
        string key = "calls:" + Marker();
        DateTime window = new(2026, 11, 22, 9, 0, 0, DateTimeKind.Utc);
        TimeSpan hour = TimeSpan.FromHours(1);
        Assert.Equal(1, store.Increment(key, window, hour));
        Assert.Equal(2, store.Increment(key, window, hour));
        Assert.Equal(3, store.Increment(key, window, hour));
        Assert.Equal(1, store.Increment(key, window.AddHours(1), hour));
        Assert.Equal(2, store.Increment(key, window.AddHours(1), hour));
        Assert.Equal(1, store.Increment(key + ":other", window, hour));
    }

    [Fact]
    public void ConcurrentIncrementsGiveEveryCountOnce()
    {
        using IStoreUnderTest store = Stores.Create(Kind);
        string key = "calls:" + Marker();
        DateTime window = new(2026, 11, 22, 10, 0, 0, DateTimeKind.Utc);
        var counts = new ConcurrentBag<long>();
        Parallel.For(0, 24, new ParallelOptions { MaxDegreeOfParallelism = 8 }, _ => counts.Add(store.Increment(key, window, TimeSpan.FromHours(1))));
        Assert.Equal(Enumerable.Range(1, 24).Select(i => (long)i), counts.OrderBy(c => c));
    }

    // ---- Row stores only ----

    [Fact]
    public void RowStoresRefuseAStaleUpdateOrDelete()
    {
        using IStoreUnderTest store = Stores.Create(Kind);
        Assert.SkipUnless(store.IsRowStore, "The in-memory store keeps no row versions: the last commit wins.");
        using IDisposable caller = TestCaller.Enter();
        string id = store.Seed("portfolio", "{\"level\":\"team\"}", Id());

        IStoreTransaction first = store.Records.Begin();
        IStoreTransaction second = store.Records.Begin();
        IStoreTransaction third = store.Records.Begin();
        first.Update("portfolio", id, J("{\"level\":\"division\"}"));
        second.Update("portfolio", id, J("{\"level\":\"department\"}"));
        third.Delete("portfolio", id);
        first.Commit();
        Assert.Throws<StoreConflictException>(() => second.Commit());
        Assert.Throws<StoreConflictException>(() => third.Commit());
        Assert.Equal("division", store.Records.Get("portfolio", id)!.Text("level"));

        // A retry reads the new version and succeeds, as the engine's one retry does.
        using IStoreTransaction retry = store.Records.Begin();
        retry.Update("portfolio", id, J("{\"level\":\"department\"}"));
        retry.Commit();
        Assert.Equal("department", store.Records.Get("portfolio", id)!.Text("level"));
        second.Dispose();
        third.Dispose();
        first.Dispose();
    }

    [Fact]
    public void RowStoresRefuseASecondCreateOfOneId()
    {
        using IStoreUnderTest store = Stores.Create(Kind);
        Assert.SkipUnless(store.IsRowStore, "The in-memory store lets the last commit win.");
        using IDisposable caller = TestCaller.Enter();
        string id = Id();
        IStoreTransaction first = store.Records.Begin();
        IStoreTransaction second = store.Records.Begin();
        first.Create("portfolio", id, null, J("{\"level\":\"team\"}"));
        second.Create("portfolio", id, null, J("{\"level\":\"division\"}"));
        first.Commit();
        Assert.Contains("committed first", Assert.Throws<StoreConflictException>(() => second.Commit()).Message);
        Assert.Equal("team", store.Records.Get("portfolio", id)!.Text("level"));
        first.Dispose();
        second.Dispose();
    }

    [Fact]
    public void RowStoresApplyAllOrNothing()
    {
        using IStoreUnderTest store = Stores.Create(Kind);
        Assert.SkipUnless(store.IsRowStore, "The in-memory store checks unique keys before it applies anything, so it has no partial commit to test.");
        using IDisposable caller = TestCaller.Enter();
        string value = "fixture-" + Marker();
        store.Seed("audit-entry", "{\"requestKey\":\"" + value + "\"}", Id());
        string portfolio = Id();
        string changed = store.Seed("portfolio", "{\"level\":\"team\"}", Id());

        using IStoreTransaction tx = store.Records.Begin();
        tx.Create("portfolio", portfolio, null, J("{\"level\":\"team\"}"));
        tx.Update("portfolio", changed, J("{\"level\":\"division\"}"));
        tx.Create("audit-entry", Id(), null, J("{\"requestKey\":\"" + value + "\"}"));
        Assert.Throws<StoreConflictException>(() => tx.Commit());
        Assert.Null(store.Records.Get("portfolio", portfolio));
        Assert.Equal("team", store.Records.Get("portfolio", changed)!.Text("level"));
    }

    [Fact]
    public void RowStoresReadNothingOfAnUnknownEntityAndWriteNone()
    {
        using IStoreUnderTest store = Stores.Create(Kind);
        Assert.SkipUnless(store.IsRowStore, "The in-memory store keeps rows of any entity name.");
        using IDisposable caller = TestCaller.Enter();
        Assert.Null(store.Records.Get("widget", Id()));
        Assert.Null(store.Records.GetByKey("widget", "a"));
        Assert.Empty(store.Records.Query(RowQuery.For("widget")));
        using IStoreTransaction tx = store.Records.Begin();
        Assert.Null(tx.Get("widget", Id()));
        Assert.Contains("widget", Assert.Throws<InvalidOperationException>(() => tx.Create("widget", Id(), null, J("{}"))).Message);
        Assert.Throws<InvalidOperationException>(() => store.Seed("widget", "{}"));

        // C03 Preset lives inside C01, and later entities have no table yet.
        Assert.Throws<InvalidOperationException>(() => tx.Create("preset", Id(), null, J("{}")));
        Assert.Throws<InvalidOperationException>(() => tx.Create("data-area", Id(), null, J("{}")));
    }

    [Fact]
    public void RowStoresNeverChangeACommittedAppendOnlyRow()
    {
        using IStoreUnderTest store = Stores.Create(Kind);
        Assert.SkipUnless(store.IsRowStore, "The in-memory store leaves history classes to the engine's guard.");
        using IDisposable caller = TestCaller.Enter();
        string audit = Id();
        using (IStoreTransaction tx = store.Records.Begin())
        {
            tx.Create("audit-entry", audit, null, J("{\"operation\":\"fixture\"}"));
            tx.Commit();
        }

        using IStoreTransaction later = store.Records.Begin();
        Assert.Contains("append-only", Assert.Throws<InvalidOperationException>(() => later.Update("audit-entry", audit, J("{\"operation\":\"changed\"}"))).Message);
        Assert.Contains("append-only", Assert.Throws<InvalidOperationException>(() => later.Delete("audit-entry", audit)).Message);
        Assert.Throws<InvalidOperationException>(() => later.Update("deployment-record", Id(), J("{}")));
    }

    // Seeding stands in for rows that exist before the engine runs, but it never changes what Azure SQL keeps in an
    // append-only ledger table, so a local run fails where Azure SQL would.
    [Fact]
    public void RowStoresNeverReseedOrReplaceACommittedAppendOnlyRow()
    {
        using IStoreUnderTest store = Stores.Create(Kind);
        Assert.SkipUnless(store.IsRowStore, "The in-memory store leaves history classes to the engine's guard.");
        using IDisposable caller = TestCaller.Enter();
        string audit = store.Seed("audit-entry", "{\"operation\":\"fixture\"}", Id());
        string record = store.Seed("deployment-record", "{\"idempotencyKey\":\"fixture-" + Marker() + "\"}", Id());

        Assert.Contains("append-only", Assert.Throws<InvalidOperationException>(() => store.Seed("audit-entry", "{\"operation\":\"changed\"}", audit)).Message);
        Assert.Contains("append-only", Assert.Throws<InvalidOperationException>(() => store.Replace("audit-entry", audit, "{\"operation\":\"changed\"}")).Message);
        Assert.Contains("append-only", Assert.Throws<InvalidOperationException>(() => store.Replace("deployment-record", record, "{}")).Message);
        Assert.Equal("fixture", store.Records.Get("audit-entry", audit)!.Text("operation"));

        // A row of an updatable ledger table may still be replaced: its history is kept.
        string decision = store.Seed("decision", "{\"statusMeaning\":\"proposed\"}", Id());
        store.Replace("decision", decision, "{\"statusMeaning\":\"submitted\"}");
        Assert.Equal("submitted", store.Records.Get("decision", decision)!.Text("statusMeaning"));
    }

    // C02 is versioned by its set and number: a second version with the same pair is a conflict for the transaction that
    // commits second, as Azure SQL's unique index makes it. The in-memory store leaves this to the engine's activation.
    [Fact]
    public void RowStoresRefuseASecondVersionWithTheSameSetAndNumber()
    {
        using IStoreUnderTest store = Stores.Create(Kind);
        Assert.SkipUnless(store.IsRowStore, "The in-memory store keeps no unique key on Configuration Set Version.");
        using IDisposable caller = TestCaller.Enter();
        string set = "fixture-set-" + Marker();
        IStoreTransaction first = store.Records.Begin();
        IStoreTransaction second = store.Records.Begin();
        first.Create("configuration-set-version", Id(), null, J("{\"configurationSet\":\"" + set + "\",\"versionNumber\":1,\"contentHash\":\"a\"}"));
        second.Create("configuration-set-version", Id(), null, J("{\"configurationSet\":\"" + set + "\",\"versionNumber\":1,\"contentHash\":\"b\"}"));
        first.Commit();

        StoreConflictException conflict = Assert.Throws<StoreConflictException>(() => second.Commit());
        Assert.Equal("configuration-set-version configurationSet and versionNumber \"" + set + "/1\" is taken by a transaction that committed first.", conflict.Message);
        first.Dispose();
        second.Dispose();

        // The next number is free, and the same number in another set is too.
        using IStoreTransaction next = store.Records.Begin();
        next.Create("configuration-set-version", Id(), null, J("{\"configurationSet\":\"" + set + "\",\"versionNumber\":2,\"contentHash\":\"c\"}"));
        next.Create("configuration-set-version", Id(), null, J("{\"configurationSet\":\"" + set + "-other\",\"versionNumber\":1,\"contentHash\":\"d\"}"));
        next.Commit();
        Assert.Equal(2, store.Records.Query(RowQuery.For("configuration-set-version").Where("configurationSet", set)).Count);
    }

    // The in-memory store checks a unique key only against committed rows. A database index also refuses the same value
    // twice in one transaction. The engine puts a request key on the caller's own write only (nested effects carry none),
    // so a call writes one Audit Entry with it, and both stores give the engine the same answers; this case pins the
    // database behavior should that ever change.
    [Fact]
    public void RowStoresRefuseAUniqueKeyRepeatedInOneTransaction()
    {
        using IStoreUnderTest store = Stores.Create(Kind);
        Assert.SkipUnless(store.IsRowStore, "The in-memory store checks unique keys only against committed rows.");
        using IDisposable caller = TestCaller.Enter();
        string value = "fixture-" + Marker();
        string firstId = Id();
        using IStoreTransaction tx = store.Records.Begin();
        tx.Create("audit-entry", firstId, null, J("{\"requestKey\":\"" + value + "\"}"));
        tx.Create("audit-entry", Id(), null, J("{\"requestKey\":\"" + value + "\"}"));
        Assert.Throws<StoreConflictException>(() => tx.Commit());
        Assert.Null(store.Records.Get("audit-entry", firstId));
    }

    [Fact]
    public void RowStoresHoldOnlyIdsAndKeysAzureSqlCanStore()
    {
        using IStoreUnderTest store = Stores.Create(Kind);
        Assert.SkipUnless(store.IsRowStore, "The in-memory store takes any ID.");
        using IDisposable caller = TestCaller.Enter();
        string tooLong = new string('a', 37);
        Assert.Null(store.Records.Get("portfolio", tooLong));
        using IStoreTransaction tx = store.Records.Begin();
        Assert.Throws<InvalidOperationException>(() => tx.Create("portfolio", tooLong, null, J("{}")));
        Assert.Throws<InvalidOperationException>(() => tx.Create("portfolio", "has space", null, J("{}")));
        Assert.Throws<InvalidOperationException>(() => tx.Create("portfolio", Id(), new string('k', 451), J("{}")));
        Assert.Throws<InvalidOperationException>(() => tx.Update("portfolio", tooLong, J("{}")));
        tx.Create("audit-entry", Id(), null, J("{\"requestKey\":\"" + new string('r', 401) + "\"}"));
        Assert.Throws<InvalidOperationException>(() => tx.Commit());
    }

    // ---- Store keys (StoreKeys.cs): the other keys the logical model asks the store to enforce ----

    // A value taken by a row another transaction committed first is a conflict for the second, which writes nothing. The
    // same value in a different row is free once it differs, and a row with an empty value holds no key.
    [Theory]
    [InlineData("principal-reference", "entraObjectId")]
    [InlineData("business-application", "recordNumber")]
    [InlineData("claim", "claimKey")]
    [InlineData("configuration-draft", "recordNumber")]
    public void RowStoresRefuseAStoreKeyValueAnotherRowHolds(string entity, string column)
    {
        using IStoreUnderTest store = Stores.Create(Kind);
        Assert.SkipUnless(store.IsRowStore, "The in-memory store holds no store key; the model gives them to the database.");
        using IDisposable caller = TestCaller.Enter();
        string value = "fixture-" + Marker();
        IStoreTransaction first = store.Records.Begin();
        IStoreTransaction second = store.Records.Begin();
        string firstId = Id();
        string secondId = Id();
        first.Create(entity, firstId, null, J("{\"" + column + "\":\"" + value + "\"}"));
        second.Create(entity, secondId, null, J("{\"" + column + "\":\"" + value + "\",\"other\":1}"));
        first.Commit();

        StoreConflictException conflict = Assert.Throws<StoreConflictException>(() => second.Commit());
        Assert.Equal(entity + " " + column + " \"" + value + "\" is taken by another row.", conflict.Message);
        second.Dispose();
        first.Dispose();
        Assert.NotNull(store.Records.Get(entity, firstId));
        Assert.Null(store.Records.Get(entity, secondId));

        using IStoreTransaction third = store.Records.Begin();
        third.Create(entity, Id(), null, J("{\"" + column + "\":\"" + value + "-2\"}"));
        third.Create(entity, Id(), null, J("{\"" + column + "\":\"\"}"));
        third.Create(entity, Id(), null, J("{\"" + column + "\":\"\"}"));
        third.Create(entity, Id(), null, J("{}"));
        third.Commit();
    }

    [Fact]
    public void RowStoresRefuseAStoreKeyValueTwiceInOneTransactionAndCompareGuidsWithoutCase()
    {
        using IStoreUnderTest store = Stores.Create(Kind);
        Assert.SkipUnless(store.IsRowStore, "The in-memory store holds no store key.");
        using IDisposable caller = TestCaller.Enter();
        string objectId = Guid.NewGuid().ToString("D");
        string firstId = Id();
        using (IStoreTransaction tx = store.Records.Begin())
        {
            tx.Create("principal-reference", firstId, null, J("{\"entraObjectId\":\"" + objectId + "\"}"));
            tx.Create("principal-reference", Id(), null, J("{\"entraObjectId\":\"" + objectId.ToUpperInvariant() + "\"}"));
            Assert.Contains("is taken by another row", Assert.Throws<StoreConflictException>(() => tx.Commit()).Message);
        }

        Assert.Null(store.Records.Get("principal-reference", firstId));
        store.Seed("principal-reference", "{\"entraObjectId\":\"" + objectId.ToUpperInvariant() + "\"}", Id());
        Assert.Throws<StoreConflictException>(() => store.Seed("principal-reference", "{\"entraObjectId\":\"" + objectId + "\"}", Id()));
    }

    // A deleted row, or a row whose value changed, frees the value for another row.
    [Fact]
    public void RowStoresFreeAStoreKeyValueWhenItsRowIsDeletedOrChanged()
    {
        using IStoreUnderTest store = Stores.Create(Kind);
        Assert.SkipUnless(store.IsRowStore, "The in-memory store holds no store key.");
        using IDisposable caller = TestCaller.Enter();
        string number = "IR-" + Marker();
        string first = Id();
        string second = Id();
        Commit(store, tx => tx.Create("intake-request", first, null, J("{\"recordNumber\":\"" + number + "\",\"title\":\"Fixture\"}")));
        Commit(store, tx => tx.Delete("intake-request", first));
        Commit(store, tx => tx.Create("intake-request", second, null, J("{\"recordNumber\":\"" + number + "\"}")));

        // A change that leaves the value alone keeps it; one that changes it frees the old value.
        Commit(store, tx => tx.Update("intake-request", second, J("{\"title\":\"Renamed\"}")));
        Assert.Throws<StoreConflictException>(() => Commit(store, tx => tx.Create("intake-request", Id(), null, J("{\"recordNumber\":\"" + number + "\"}"))));
        Commit(store, tx => tx.Update("intake-request", second, J("{\"recordNumber\":\"" + number + "-b\"}")));
        Commit(store, tx => tx.Create("intake-request", Id(), null, J("{\"recordNumber\":\"" + number + "\"}")));
        Assert.Throws<StoreConflictException>(() => Commit(store, tx => tx.Create("intake-request", Id(), null, J("{\"recordNumber\":\"" + number + "-b\"}"))));

        // Clearing the value frees it too.
        Commit(store, tx => tx.Update("intake-request", second, J("{\"recordNumber\":null}")));
        Commit(store, tx => tx.Create("intake-request", Id(), null, J("{\"recordNumber\":\"" + number + "-b\"}")));
    }

    // The database removes every value a commit gives up before it adds any, so values that move between rows in one
    // transaction never collide with themselves: two rows that swap values, and a value moving from a deleted row.
    [Fact]
    public void RowStoresLetStoreKeyValuesMoveBetweenRowsInOneTransaction()
    {
        using IStoreUnderTest store = Stores.Create(Kind);
        Assert.SkipUnless(store.IsRowStore, "The in-memory store holds no store key.");
        using IDisposable caller = TestCaller.Enter();
        string a = "APP-" + Marker();
        string b = "APP-" + Marker();
        string tail = Guid.NewGuid().ToString("D").Substring(1);
        string low = "a" + tail;
        string high = "b" + tail;
        string lowest = "0" + tail;
        Commit(store, tx =>
        {
            tx.Create("business-application", low, null, J("{\"recordNumber\":\"" + a + "\"}"));
            tx.Create("business-application", high, null, J("{\"recordNumber\":\"" + b + "\"}"));
        });

        // The update of the lower ID comes first in the commit and takes the value the other row still holds.
        Commit(store, tx =>
        {
            tx.Update("business-application", low, J("{\"recordNumber\":\"" + b + "\"}"));
            tx.Update("business-application", high, J("{\"recordNumber\":\"" + a + "\"}"));
        });
        Assert.Equal(b, store.Records.Get("business-application", low)!.Text("recordNumber"));
        Assert.Equal(a, store.Records.Get("business-application", high)!.Text("recordNumber"));

        // The new row comes first in the commit and takes the value of the row deleted in it.
        Commit(store, tx =>
        {
            tx.Delete("business-application", high);
            tx.Create("business-application", lowest, null, J("{\"recordNumber\":\"" + a + "\"}"));
        });
        Assert.Equal(a, store.Records.Get("business-application", lowest)!.Text("recordNumber"));
        Assert.Null(store.Records.Get("business-application", high));
    }

    // A live configuration row's key path is unique in its table, retired rows included: a retired row keeps its path and
    // comes back by reactivation, never as a new row (V08).
    [Fact]
    public void RowStoresHoldOneLiveConfigurationRowPerKeyPath()
    {
        using IStoreUnderTest store = Stores.Create(Kind);
        Assert.SkipUnless(store.IsRowStore, "The in-memory store holds no store key.");
        using IDisposable caller = TestCaller.Enter();
        string path = "fixture-" + Marker();
        string first = store.Seed("change-type", "{\"key\":\"" + path + "\",\"isActive\":true}", Id(), path);
        StoreConflictException conflict = Assert.Throws<StoreConflictException>(() => store.Seed("change-type", "{\"key\":\"" + path + "\",\"isActive\":true}", Id(), path));
        Assert.Equal("change-type key \"" + path + "\" is taken by another row.", conflict.Message);

        store.Replace("change-type", first, "{\"key\":\"" + path + "\",\"isActive\":false}");
        Assert.Throws<StoreConflictException>(() => store.Seed("change-type", "{\"key\":\"" + path + "\",\"isActive\":true}", Id(), path));
        store.Seed("change-type", "{\"key\":\"" + path + "\",\"isActive\":true}", Id(), path + "-2");

        // The key path of a list value holds its list's key: the same value key in another list is a different path.
        store.Seed("list-value", "{\"key\":\"other\"}", Id(), path + "/other");
        store.Seed("list-value", "{\"key\":\"other\"}", Id(), path + "-2/other");
        Assert.Throws<StoreConflictException>(() => store.Seed("list-value", "{\"key\":\"other\"}", Id(), path + "/other"));
    }

    // An alternate key of a configuration entity counts active rows only: a retired tier keeps its rank, and an active
    // tier may take it, as V12 allows; two active tiers may not share it.
    [Fact]
    public void RowStoresCountOnlyActiveRowsForAConfigurationAlternateKey()
    {
        using IStoreUnderTest store = Stores.Create(Kind);
        Assert.SkipUnless(store.IsRowStore, "The in-memory store holds no store key.");
        using IDisposable caller = TestCaller.Enter();
        int rank = 100_000 + Math.Abs(Guid.NewGuid().GetHashCode() % 1_000_000_000);
        string path = "fixture-" + Marker();
        string retired = store.Seed("tier", "{\"key\":\"" + path + "-a\",\"rank\":" + rank + ",\"isActive\":false}", Id(), path + "-a");
        string active = store.Seed("tier", "{\"key\":\"" + path + "-b\",\"rank\":" + rank + ",\"isActive\":true}", Id(), path + "-b");
        StoreConflictException conflict = Assert.Throws<StoreConflictException>(() => store.Seed("tier", "{\"key\":\"" + path + "-c\",\"rank\":" + rank + "}", Id(), path + "-c"));
        Assert.Equal("tier rank \"" + rank + "\" is taken by another row.", conflict.Message);
        Assert.Throws<StoreConflictException>(() => store.Replace("tier", retired, "{\"key\":\"" + path + "-a\",\"rank\":" + rank + ",\"isActive\":true}"));

        // Retiring the active tier frees the rank for the other.
        store.Replace("tier", active, "{\"key\":\"" + path + "-b\",\"rank\":" + rank + ",\"isActive\":false}");
        store.Replace("tier", retired, "{\"key\":\"" + path + "-a\",\"rank\":" + rank + ",\"isActive\":true}");

        // A binding's role, group and method, compared as the bundle names them.
        string group = Guid.NewGuid().ToString("D");
        store.Seed("role-group-binding", "{\"key\":\"" + path + "-x\",\"roleKey\":\"requester\",\"entraGroupObjectId\":\"" + group + "\",\"bindingMethod\":\"entra-app-role-assignment\"}", Id(), path + "-x");
        store.Seed("role-group-binding", "{\"key\":\"" + path + "-y\",\"roleKey\":\"requester\",\"entraGroupObjectId\":\"" + group + "\",\"bindingMethod\":\"entra-group-team\"}", Id(), path + "-y");
        Assert.Throws<StoreConflictException>(() => store.Seed("role-group-binding", "{\"key\":\"" + path + "-z\",\"roleKey\":\"requester\",\"entraGroupObjectId\":\"" + group.ToUpperInvariant() + "\",\"bindingMethod\":\"entra-app-role-assignment\"}", Id(), path + "-z"));
    }

    // A technical component's link kind and external ID count current components only: a resource registered again after
    // its component was retired, or moved with the same ID, is a new component.
    [Fact]
    public void RowStoresCountOnlyCurrentComponentsForTheirLinkKindAndExternalId()
    {
        using IStoreUnderTest store = Stores.Create(Kind);
        Assert.SkipUnless(store.IsRowStore, "The in-memory store holds no store key.");
        using IDisposable caller = TestCaller.Enter();
        string external = "fixture-resource-" + Marker();
        string columns = "\"linkKind\":\"power-platform-resource\",\"externalId\":\"" + external + "\"";
        string old = Id();
        Commit(store, tx => tx.Create("technical-component", old, null, J("{" + columns + "}")));
        Assert.Throws<StoreConflictException>(() => Commit(store, tx => tx.Create("technical-component", Id(), null, J("{" + columns + "}"))));

        string moved = Id();
        Commit(store, tx =>
        {
            tx.Update("technical-component", old, J("{\"retiredOn\":\"2026-11-20\"}"));
            tx.Create("technical-component", moved, null, J("{" + columns + ",\"replacesComponent\":\"" + old + "\"}"));
        });
        Assert.Throws<StoreConflictException>(() => Commit(store, tx => tx.Create("technical-component", Id(), null, J("{" + columns + "}"))));
        Assert.Throws<StoreConflictException>(() => Commit(store, tx => tx.Update("technical-component", old, J("{\"retiredOn\":null}"))));
    }

    // A key with an optional part holds nothing for a row that leaves it empty: a document link without a requirement,
    // and a top-level portfolio (no parent).
    [Fact]
    public void RowStoresIgnoreAStoreKeyWhoseOptionalPartIsEmpty()
    {
        using IStoreUnderTest store = Stores.Create(Kind);
        Assert.SkipUnless(store.IsRowStore, "The in-memory store holds no store key.");
        using IDisposable caller = TestCaller.Enter();
        string app = Id();
        Commit(store, tx =>
        {
            tx.Create("document-link", Id(), null, J("{\"businessApplication\":\"" + app + "\",\"url\":\"https://example.com/a\"}"));
            tx.Create("document-link", Id(), null, J("{\"businessApplication\":\"" + app + "\",\"url\":\"https://example.com/b\"}"));
            tx.Create("document-link", Id(), null, J("{\"businessApplication\":\"" + app + "\",\"documentationRequirement\":\"architecture-doc\"}"));
        });
        Assert.Throws<StoreConflictException>(() => Commit(store, tx => tx.Create("document-link", Id(), null, J("{\"businessApplication\":\"" + app + "\",\"documentationRequirement\":\"architecture-doc\"}"))));

        string name = "Fixture portfolio " + Marker();
        string parent = Id();
        store.Seed("portfolio", "{\"name\":\"" + name + "\"}", Id());
        store.Seed("portfolio", "{\"name\":\"" + name + "\"}", Id());
        store.Seed("portfolio", "{\"name\":\"" + name + "\",\"parentPortfolio\":\"" + parent + "\"}", Id());
        Assert.Throws<StoreConflictException>(() => store.Seed("portfolio", "{\"name\":\"" + name + "\",\"parentPortfolio\":\"" + parent + "\"}", Id()));
    }

    // The keys held back (StoreKeyCatalog.HeldBack) take repeats, because documented engine behavior writes them.
    [Fact]
    public void RowStoresTakeRepeatsOfTheStoreKeysHeldBack()
    {
        using IStoreUnderTest store = Stores.Create(Kind);
        Assert.SkipUnless(store.IsRowStore, "The in-memory store holds no store key.");
        using IDisposable caller = TestCaller.Enter();
        string marker = Marker();
        Commit(store, tx =>
        {
            tx.Create("artifact-version", Id(), null, J("{\"businessApplication\":\"" + Id() + "\",\"artifactName\":\"Fixture " + marker + "\",\"version\":\"1.0.0\"}"));
            tx.Create("artifact-version", Id(), null, J("{\"businessApplication\":\"" + Id() + "\",\"artifactName\":\"Fixture " + marker + "\",\"version\":\"1.0.0\"}"));
            tx.Create("review", Id(), null, J("{\"businessApplication\":\"" + marker + "\",\"reviewType\":\"post-change\",\"dueOn\":\"2026-12-07\"}"));
            tx.Create("review", Id(), null, J("{\"businessApplication\":\"" + marker + "\",\"reviewType\":\"post-change\",\"dueOn\":\"2026-12-07\"}"));
        });

        string set = "fixture-set-" + marker;
        Commit(store, tx =>
        {
            tx.Create("configuration-set-version", Id(), null, J("{\"configurationSet\":\"" + set + "\",\"versionNumber\":1,\"contentHash\":\"" + marker + "\"}"));
            tx.Create("configuration-set-version", Id(), null, J("{\"configurationSet\":\"" + set + "\",\"versionNumber\":3,\"contentHash\":\"" + marker + "\"}"));
        });
    }

    // ---- Helpers ----

    private static void Commit(IStoreUnderTest store, Action<IStoreTransaction> write)
    {
        using IStoreTransaction tx = store.Records.Begin();
        write(tx);
        tx.Commit();
    }

    protected static JsonTreeNode J(string json)
    {
        return CanonicalJson.Parse(json);
    }

    protected static string Id()
    {
        return Guid.NewGuid().ToString("D");
    }

    protected static string Marker()
    {
        return Guid.NewGuid().ToString("N");
    }

    // The first version number a test seeds. A fresh store starts at 1; the shared Azure SQL database keeps every
    // Configuration Set Version, so each test there takes numbers no other test has used.
    private int FirstVersionNumber()
    {
        return Kind == StoreKind.SqlServer ? 1000 + (Math.Abs(Guid.NewGuid().GetHashCode()) % 1_000_000) * 2 : 1;
    }

    private static string[] Ids(IEnumerable<StoredRow> rows)
    {
        return rows.Select(r => r.Id).ToArray();
    }

    private static string[] Sorted(params string[] ids)
    {
        return ids.OrderBy(i => i, StringComparer.Ordinal).ToArray();
    }
}

public sealed class InMemoryConformanceTests : StoreConformanceTests
{
    protected override StoreKind Kind => StoreKind.InMemory;
}

public sealed class SqliteConformanceTests : StoreConformanceTests
{
    protected override StoreKind Kind => StoreKind.Sqlite;
}

// Skipped unless PRODUCT_TEST_SQLSERVER is set. One collection, so the cases run one after another on the database.
[Collection(SqlServerCollection.Name)]
public sealed class SqlServerConformanceTests : StoreConformanceTests
{
    protected override StoreKind Kind => StoreKind.SqlServer;
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlServerCollection
{
    public const string Name = "Azure SQL database";
}
