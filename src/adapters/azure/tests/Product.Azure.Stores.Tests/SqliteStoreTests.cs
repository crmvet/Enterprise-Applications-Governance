using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Product.Azure.Stores.Sqlite;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Runtime;
using Product.Engine.Runtime.Callers;
using Product.Engine.Runtime.Ports;
using Product.Engine.Testing.InMemory;
using Xunit;

namespace Product.Azure.Stores.Tests;

// What only the SQLite store does: the file outlives the process, the schema makes itself, and two stores on one file
// see each other's commits. Also the ambient caller context every store reads.
public sealed class SqliteStoreTests
{
    [Fact]
    public void RowsTheVersionAndCountersSurviveARestart()
    {
        string folder = Paths.NewScratchFolder();
        string file = Path.Combine(folder, "data", "product.db");
        try
        {
            string dump;
            string draft;
            string application;
            var clock = new ManualClock(World.AsOf);
            DateTime window = new(2026, 11, 22, 9, 0, 0, DateTimeKind.Utc);
            using (IDisposable caller = TestCaller.Enter())
            using (RowStoreUnderTest first = Stores.NewSqlite(file))
            {
                first.SeedVersion(World.Version(1));
                application = World.SeedDirectoryAndEstate(first);
                var runtime = new EngineRuntime(first.Records, first.Configuration, World.Catalog, clock: clock);
                draft = runtime.Write(World.AiClientOf1, WriteCommand.Create("intake-request",
                    "{\"requester\":\"" + World.Person1 + "\",\"title\":\"Fixture restart\",\"need\":\"Fixture need\"}", "client-request-restart")).RowId!;
                Assert.Equal(1, first.Increment("calls:restart", window, TimeSpan.FromHours(1)));
                dump = first.Dump();
            }

            using (IDisposable caller = TestCaller.Enter())
            using (RowStoreUnderTest second = Stores.NewSqlite(file, "00000000-0000-4000-9001-"))
            {
                Assert.Equal(dump, second.Dump());
                Assert.Equal(1, second.Configuration.ProbeInForceVersion()!.VersionNumber);
                Assert.Equal(World.Version(1).ContentHash, second.Configuration.GetVersion(World.Version(1).ConfigurationSetKey, 1)!.ContentHash);
                Assert.Equal(2, second.Increment("calls:restart", window, TimeSpan.FromHours(1)));

                var runtime = new EngineRuntime(second.Records, second.Configuration, World.Catalog, clock: clock);
                Assert.Equal(1, runtime.GetEngineInfo(World.PersonOne).ConfigurationVersion);
                RuntimeOutcome replay = runtime.Write(World.AiClientOf1, WriteCommand.Create("intake-request",
                    "{\"requester\":\"" + World.Person1 + "\",\"title\":\"Fixture restart\",\"need\":\"Fixture need\"}", "client-request-restart"));
                Assert.True(replay.IsReplay, replay.ToString());
                Assert.Equal(draft, replay.RowId);
                Assert.Equal(dump, second.Dump());

                RuntimeOutcome renamed = runtime.Write(World.PersonOne, WriteCommand.Update("intake-request", draft, "{\"title\":\"Fixture restart, renamed\"}"));
                Assert.True(renamed.Succeeded, renamed.ToString());
                Assert.NotNull(second.Records.Get("business-application", application));
            }

            using (RowStoreUnderTest third = Stores.NewSqlite(file, "00000000-0000-4000-9002-"))
            {
                Assert.Equal("Fixture restart, renamed", third.Records.Get("intake-request", draft)!.Text("title"));
            }
        }
        finally
        {
            Stores.TryDelete(folder);
        }
    }

    [Fact]
    public void TwoStoresOnOneFileSeeEachOthersCommitsAndConflicts()
    {
        string folder = Paths.NewScratchFolder();
        string file = Path.Combine(folder, "product.db");
        try
        {
            using RowStoreUnderTest a = Stores.NewSqlite(file);
            using RowStoreUnderTest b = Stores.NewSqlite(file, "00000000-0000-4000-9001-");
            string id = a.Seed("portfolio", "{\"level\":\"team\"}", "00000000-0000-4000-8000-00000000a001");
            Assert.Equal("team", b.Records.Get("portfolio", id)!.Text("level"));

            IStoreTransaction fromA = a.Records.Begin();
            IStoreTransaction fromB = b.Records.Begin();
            fromA.Update("portfolio", id, CanonicalJson.Parse("{\"level\":\"division\"}"));
            fromB.Update("portfolio", id, CanonicalJson.Parse("{\"level\":\"department\"}"));
            fromA.Commit();
            Assert.Throws<StoreConflictException>(() => fromB.Commit());
            Assert.Equal("division", b.Records.Get("portfolio", id)!.Text("level"));
            fromA.Dispose();
            fromB.Dispose();

            DateTime window = new(2026, 11, 22, 9, 0, 0, DateTimeKind.Utc);
            Parallel.For(0, 20, i => (i % 2 == 0 ? a : b).Increment("calls:shared", window, TimeSpan.FromHours(1)));
            Assert.Equal(21, a.Increment("calls:shared", window, TimeSpan.FromHours(1)));
        }
        finally
        {
            Stores.TryDelete(folder);
        }
    }

    [Fact]
    public void TheSchemaMakesItsFolderAndFileOnFirstUse()
    {
        string folder = Paths.NewScratchFolder();
        string file = Path.Combine(folder, "nested", "deeper", "product.db");
        try
        {
            using var store = new SqliteProductStore(new SqliteStoreOptions { ConnectionString = "Data Source=" + file });
            Assert.False(store.CanConnect());
            store.EnsureSchema();
            Assert.True(File.Exists(file));
            Assert.True(store.CanConnect());
            store.EnsureSchema();
            Assert.Equal("sqlite:product.db", store.StoreId);
            Assert.False(store.HasInForceVersion);
        }
        finally
        {
            Stores.TryDelete(folder);
        }
    }

    [Fact]
    public void ASharedInMemoryDatabaseLivesAsLongAsItsStore()
    {
        string name = "fixture-" + Guid.NewGuid().ToString("N");
        string connection = "Data Source=" + name + ";Mode=Memory;Cache=Shared";
        string id;
        using (var store = new SqliteProductStore(new SqliteStoreOptions { ConnectionString = connection }))
        {
            Assert.Equal("sqlite:" + name, store.StoreId);
            id = store.Seed("portfolio", CanonicalJson.Parse("{\"level\":\"team\"}"));
            Assert.NotNull(store.Get("portfolio", id));
            Assert.True(store.CanConnect());
        }

        using var again = new SqliteProductStore(new SqliteStoreOptions { ConnectionString = connection });
        Assert.Null(again.Get("portfolio", id));
    }

    [Fact]
    public void BadSettingsAreRefused()
    {
        Assert.Throws<ArgumentNullException>(() => new SqliteProductStore(null!));
        Assert.Throws<ArgumentException>(() => new SqliteProductStore(new SqliteStoreOptions()));
        Assert.Throws<ArgumentException>(() => new SqliteProductStore(new SqliteStoreOptions { ConnectionString = "Data Source=:memory:" }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SqliteProductStore(new SqliteStoreOptions { ConnectionString = "Data Source=x.db", BusyTimeoutMilliseconds = -1 }));
    }

    [Fact]
    public void ADisposedStoreRefusesWork()
    {
        RowStoreUnderTest store = Stores.NewSqlite();
        store.Seed("portfolio", "{}");
        store.Dispose();
        Assert.Throws<ObjectDisposedException>(() => store.Store.Begin());
        Assert.False(store.Store.CanConnect());
    }

    // ---- The ambient caller context ----

    [Fact]
    public async Task TheCallerContextFlowsThroughAsyncCodeAndIsRestored()
    {
        Assert.Null(StoreCallContext.Current);
        var outer = new StoreCallContext("outer", CallerEntry.Person, ActorKindMeaning.Person, "req-outer", new[] { "b", "a", "b" });
        Assert.Equal(new[] { "a", "b" }, outer.RoleKeys);
        Assert.Equal("person", outer.ActorKindWire);
        using (StoreCallContext.Enter(outer))
        {
            await Task.Yield();
            Assert.Same(outer, StoreCallContext.Current);
            Assert.Same(outer, await Task.Run(() => StoreCallContext.Current));
            using (StoreCallContext.Enter(new StoreCallContext("inner", CallerEntry.Workload, ActorKindMeaning.ApprovalRunner, "req-inner")))
            {
                Assert.Equal("inner", StoreCallContext.Current!.PrincipalId);
                Assert.Equal("approval-runner", StoreCallContext.Current.ActorKindWire);
            }

            Assert.Same(outer, StoreCallContext.Current);
        }

        Assert.Null(StoreCallContext.Current);
        Assert.Throws<ArgumentException>(() => new StoreCallContext(string.Empty, null, null, "req"));
        Assert.Throws<ArgumentException>(() => new StoreCallContext("p", null, null, string.Empty));
    }

    [Fact]
    public void RowIdsAreRepeatableWhereTestsNeedThem()
    {
        Func<string, string> sequential = RowIds.Sequential();
        Assert.Equal("00000000-0000-4000-9000-000000000001", sequential("decision"));
        Assert.Equal("00000000-0000-4000-9000-000000000002", sequential("tier"));
        Func<string, string> counter = RowIds.EntityCounter();
        Assert.Equal("tier-000001", counter("tier"));
        Assert.Equal("decision-000002", counter("decision"));
        Assert.Throws<ArgumentException>(() => RowIds.Sequential("short"));
        Assert.True(Guid.TryParse(RowIds.RandomGuid("decision"), out _));
        Assert.Equal(Enumerable.Range(0, 3).Count(), Enumerable.Range(0, 3).Select(_ => RowIds.RandomGuid("x")).Distinct().Count());
    }
}
