using System;
using System.Linq;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Reasons;
using Product.Engine.Testing.InMemory;
using Product.Engine.Runtime.Ports;
using Product.Engine.Runtime.Snapshots;
using Xunit;

namespace Product.Engine.Runtime.Tests;

// The snapshot cache with the version probe (document 25, section 6.3): one probe per call, one compile per version, a
// hash check before use, no fallback, and a version change in effect on the next call.
public sealed class SnapshotCacheTests
{
    [Fact]
    public void EveryCallProbesAndEachVersionCompilesOnce()
    {
        Harness h = Harness.Create();
        for (int i = 0; i < 5; i++)
        {
            RuntimeOutcome info = Harness.Ok(h.Runtime.GetEngineInfo(Harness.PersonOne));
            Assert.Equal(1, info.ConfigurationVersion);
            Assert.Equal(h.Version1.ContentHash, info.SnapshotHash);
        }

        Assert.Equal(5, h.Runtime.Cache.ProbeCount);
        Assert.Equal(5, h.Store.ProbeCount);
        Assert.Equal(1, h.Runtime.Cache.CompileCount);
        Assert.Equal(1, h.Store.VersionReadCount);
    }

    // Another instance activates version 2 (here seeded straight into the store): the next call here sees it, with no
    // cache flush. Version 1 stays cached for rows stamped with it.
    [Fact]
    public void AVersionChangeTakesEffectOnTheNextCall()
    {
        Harness h = Harness.Create();
        Assert.Equal(1, Harness.Ok(h.Runtime.GetEngineInfo(Harness.PersonOne)).ConfigurationVersion);

        CompiledSnapshot version2 = Harness.Compile(2, "[{\"op\":\"upsert\",\"entity\":\"setting\",\"key\":\"link-undo-minutes\",\"values\":{\"value\":10}}]");
        Assert.NotEqual(h.Version1.ContentHash, version2.ContentHash);
        h.Store.SeedVersion(version2);

        RuntimeOutcome next = Harness.Ok(h.Runtime.GetEngineInfo(Harness.PersonOne));
        Assert.Equal(2, next.ConfigurationVersion);
        Assert.Equal(version2.ContentHash, next.SnapshotHash);
        Assert.Equal(2, h.Runtime.Cache.CompileCount);

        SnapshotLoad stamped = h.Runtime.Cache.LoadVersion(h.Store, version2.ConfigurationSetKey, 1);
        Assert.True(stamped.FromCache);
        Assert.Equal(h.Version1.ContentHash, stamped.Snapshot!.ContentHash);
    }

    [Fact]
    public void NoVersionInForceIsUnavailableNeverAFallback()
    {
        var store = new InMemoryStore("empty-store");
        var runtime = new EngineRuntime(store, store, Harness.Catalog, clock: new ManualClock(Harness.AsOf));
        RuntimeOutcome outcome = runtime.GetEngineInfo(Harness.PersonOne);
        Assert.Equal("unavailable", outcome.Outcome);
        Assert.True(outcome.HasReason(ReasonCodes.EngineUnavailable), outcome.ToString());
        Assert.Null(outcome.ConfigurationVersion);
    }

    [Fact]
    public void AVersionThatDoesNotMatchItsHashIsUnavailable()
    {
        Harness h = Harness.Create();
        CompiledSnapshot version2 = Harness.Compile(2, "[{\"op\":\"upsert\",\"entity\":\"setting\",\"key\":\"link-undo-minutes\",\"values\":{\"value\":10}}]");
        h.Store.SeedVersion(version2);
        StoredRow row = h.Store.Rows("configuration-set-version").Single(r => r.Number("versionNumber") == 2);
        h.Store.Tamper("configuration-set-version", row.Id, RuntimeJsonTestHelpers.With(row.Columns, "contentHash", JsonTreeNode.FromString(h.Version1.ContentHash)));

        RuntimeOutcome outcome = h.Runtime.GetEngineInfo(Harness.PersonOne);
        Assert.Equal("unavailable", outcome.Outcome);
        Assert.True(outcome.HasReason(ReasonCodes.SnapshotHashMismatch), outcome.ToString());
    }

    [Fact]
    public void ASnapshotSchemaThisEngineDoesNotReadIsUnavailable()
    {
        Harness h = Harness.Create();
        JsonTreeNode content = RuntimeJsonTestHelpers.With(h.Version1.ContentJson, "schemaVersion", JsonTreeNode.FromString("2.0"));
        h.Store.Seed("configuration-set-version", RuntimeJsonTestHelpers.Object(
            ("configurationSet", JsonTreeNode.FromString(h.Version1.ConfigurationSetKey)),
            ("versionNumber", JsonTreeNode.FromInt64(2)),
            ("contentJson", content),
            ("contentHash", JsonTreeNode.FromString(CanonicalHash.ComputeContentHash(content)))));
        StoredRow set = h.Store.Rows("configuration-set").Single();
        h.Store.Tamper("configuration-set", set.Id, RuntimeJsonTestHelpers.With(set.Columns, "currentVersionNumber", JsonTreeNode.FromInt64(2)));

        RuntimeOutcome outcome = h.Runtime.GetEngineInfo(Harness.PersonOne);
        Assert.Equal("unavailable", outcome.Outcome);
        Assert.True(outcome.HasReason(ReasonCodes.SnapshotSchemaUnsupported), outcome.ToString());
    }

    // engine-read-cache-seconds lets reads reuse a recent probe; writes always probe (document 25, sections 6.3 and 6.5).
    [Fact]
    public void ReadsMayReuseARecentProbeAndWritesAlwaysProbe()
    {
        Harness h = Harness.Create("[{\"op\":\"upsert\",\"entity\":\"setting\",\"key\":\"engine-read-cache-seconds\",\"values\":{\"value\":60}}]");
        Harness.Ok(h.Runtime.GetEngineInfo(Harness.PersonOne));
        int probes = h.Runtime.Cache.ProbeCount;

        h.Clock.Advance(TimeSpan.FromSeconds(30));
        Harness.Ok(h.Runtime.GetEngineInfo(Harness.PersonOne));
        Assert.Equal(probes, h.Runtime.Cache.ProbeCount);

        Harness.Ok(h.Runtime.Write(Harness.AiClientOf1, WriteCommand.Create("intake-request", "{\"title\":\"Fixture\",\"need\":\"Fixture need\",\"requester\":\"" + Harness.Person1 + "\"}")));
        Assert.Equal(probes + 1, h.Runtime.Cache.ProbeCount);

        h.Clock.Advance(TimeSpan.FromSeconds(61));
        Harness.Ok(h.Runtime.GetEngineInfo(Harness.PersonOne));
        Assert.Equal(probes + 2, h.Runtime.Cache.ProbeCount);
    }

    // The store ID is part of the cache key: two stores with the same set key and version number never share a snapshot.
    [Fact]
    public void TwoStoresNeverShareACachedSnapshot()
    {
        var cache = new SnapshotCache();
        var a = new InMemoryStore("store-a");
        var b = new InMemoryStore("store-b");
        a.SeedVersion(Harness.Compile(1));
        b.SeedVersion(Harness.Compile(1, "[{\"op\":\"upsert\",\"entity\":\"setting\",\"key\":\"link-undo-minutes\",\"values\":{\"value\":10}}]"));

        SnapshotLoad fromA = cache.Load(a, SnapshotPurpose.Write, Harness.AsOf);
        SnapshotLoad fromB = cache.Load(b, SnapshotPurpose.Write, Harness.AsOf);
        Assert.Equal(1, fromA.Snapshot!.VersionNumber);
        Assert.Equal(1, fromB.Snapshot!.VersionNumber);
        Assert.NotEqual(fromA.Snapshot.ContentHash, fromB.Snapshot.ContentHash);
        Assert.Equal(2, cache.CompileCount);
    }
}
