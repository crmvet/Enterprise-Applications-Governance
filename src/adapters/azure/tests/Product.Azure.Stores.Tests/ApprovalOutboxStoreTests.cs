using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Runtime.Ports;
using Xunit;

namespace Product.Azure.Stores.Tests;

// The approval outbox in the row stores: the commit hook writes it in the same database transaction as the engine's rows
// (a pending Approval Record opens its row, a final outcome settles it, a rolled back or conflicting commit leaves
// nothing), seeding never touches it, engine dumps leave it out, and the runner's changes are compare and set on the row
// version. The same cases run on SQLite and, when PRODUCT_TEST_SQLSERVER is set, on Azure SQL.
public abstract class ApprovalOutboxStoreTests
{
    private static readonly DateTime Now = new(2026, 11, 23, 9, 0, 0, DateTimeKind.Utc);

    protected abstract StoreKind Kind { get; }

    [Fact]
    public void ACommittedPendingApprovalRecordOpensItsOutboxRow()
    {
        using RowStoreUnderTest store = Create();
        using IDisposable caller = TestCaller.Enter();
        string record = Id();
        string decision = Id();

        using (IStoreTransaction tx = store.Records.Begin())
        {
            tx.Create("approval-record", record, null, Pending(decision, Now));
            Assert.Null(store.Store.GetOutboxItem(record));
            tx.Commit();
        }

        ApprovalOutboxItem item = store.Store.GetOutboxItem(record)!;
        Assert.Equal(ApprovalOutboxStates.Pending, item.State);
        Assert.Equal(decision, item.DecisionId);
        Assert.Equal(Now, item.EnqueuedOnUtc);
        Assert.Equal(0, item.Attempts);
        Assert.Equal(1, item.RowVersion);
        Assert.Null(item.ClaimId);
        Assert.Contains(store.Store.ClaimableOutboxItems(Now, 1000), i => i.ApprovalRecordId == record);
        Assert.DoesNotContain("ApprovalOutbox", store.Dump(), StringComparison.Ordinal);
    }

    [Fact]
    public void ARolledBackOrConflictingCommitLeavesNoOutboxRow()
    {
        using RowStoreUnderTest store = Create();
        using IDisposable caller = TestCaller.Enter();
        string discarded = Id();
        using (IStoreTransaction tx = store.Records.Begin())
        {
            tx.Create("approval-record", discarded, null, Pending(Id(), Now));
        }

        Assert.Null(store.Store.GetOutboxItem(discarded));

        // The record and a portfolio change commit together. Another transaction changes the portfolio first, so the
        // commit conflicts: neither the record nor its outbox row is written.
        string portfolio = store.Seed("portfolio", "{\"level\":\"team\"}", Id());
        string conflicted = Id();
        using (IStoreTransaction first = store.Records.Begin())
        {
            first.Create("approval-record", conflicted, null, Pending(Id(), Now));
            first.Update("portfolio", portfolio, CanonicalJson.Parse("{\"level\":\"department\"}"));
            using (IStoreTransaction second = store.Records.Begin())
            {
                second.Update("portfolio", portfolio, CanonicalJson.Parse("{\"level\":\"division\"}"));
                second.Commit();
            }

            Assert.Throws<StoreConflictException>(() => first.Commit());
        }

        Assert.Null(store.Records.Get("approval-record", conflicted));
        Assert.Null(store.Store.GetOutboxItem(conflicted));
    }

    [Fact]
    public void AFinalOutcomeSettlesTheRowAndASettledRowNeverChanges()
    {
        using RowStoreUnderTest store = Create();
        string record = Id();
        using (TestCaller.Enter())
        {
            Commit(store, tx => tx.Create("approval-record", record, null, Pending(Id(), Now)));
        }

        using (TestCaller.Enter(ActorKindMeaning.ApprovalRunner))
        {
            Commit(store, tx => tx.Update("approval-record", record, CanonicalJson.Parse("{\"outcome\":\"approval-approved\",\"outcomeMeaning\":\"approved\"}")));
            ApprovalOutboxItem answered = store.Store.GetOutboxItem(record)!;
            Assert.Equal(ApprovalOutboxStates.Answered, answered.State);
            Assert.Equal(2, answered.RowVersion);

            // The engine's later write on the final record (Counts Toward Requirement) leaves the settled row as it is.
            Commit(store, tx => tx.Update("approval-record", record, CanonicalJson.Parse("{\"countsTowardRequirement\":true}")));
            Assert.Equal(answered.RowVersion, store.Store.GetOutboxItem(record)!.RowVersion);
        }

        Assert.False(new ApprovalOutbox(store.Store).Settle(Id(), ApprovalOutboxStates.Expired, Now));
        Assert.True(new ApprovalOutbox(store.Store).Settle(record, ApprovalOutboxStates.Expired, Now));
        Assert.Equal(ApprovalOutboxStates.Answered, store.Store.GetOutboxItem(record)!.State);

        // The store itself keeps a settled row: even a compare and set on its current version is refused.
        ApprovalOutboxItem settled = store.Store.GetOutboxItem(record)!;
        Assert.False(store.Store.TrySetOutboxItem(settled.Next(ApprovalOutboxStates.Pending, Now, null, null, 0, null, null)));
        Assert.Equal(ApprovalOutboxStates.Answered, store.Store.GetOutboxItem(record)!.State);
        Assert.Equal(settled.RowVersion, store.Store.GetOutboxItem(record)!.RowVersion);
        Assert.DoesNotContain(store.Store.ClaimableOutboxItems(Now.AddYears(1), 1000), i => i.ApprovalRecordId == record);
    }

    [Fact]
    public void ClaimsSentReportsAndReleasesAreCompareAndSetOnTheRowVersion()
    {
        using RowStoreUnderTest store = Create();
        using IDisposable caller = TestCaller.Enter();
        string record = Id();
        Commit(store, tx => tx.Create("approval-record", record, null, Pending(Id(), Now)));
        var runnerA = new ApprovalOutbox(store.Store);
        var runnerB = new ApprovalOutbox(store.Store);
        ApprovalOutboxItem candidate = runnerA.Get(record)!;

        // Two runner calls race for one candidate: one wins.
        ApprovalOutboxItem? won = runnerA.TryClaim(candidate, "claim-a", Now, TimeSpan.FromMinutes(5), 3);
        Assert.NotNull(won);
        Assert.Null(runnerB.TryClaim(candidate, "claim-b", Now, TimeSpan.FromMinutes(5), 3));
        Assert.DoesNotContain(store.Store.ClaimableOutboxItems(Now.AddMinutes(4), 1000), i => i.ApprovalRecordId == record);

        // The lease runs out: the row is claimable again, and the new claim counts the lapsed one as an attempt.
        ApprovalOutboxItem lapsed = store.Store.ClaimableOutboxItems(Now.AddMinutes(5), 1000).Single(i => i.ApprovalRecordId == record);
        ApprovalOutboxItem? again = runnerB.TryClaim(lapsed, "claim-b", Now.AddMinutes(5), TimeSpan.FromMinutes(5), 3);
        Assert.Equal(1, again!.Attempts);
        Assert.Equal(ApprovalOutboxResultKind.StaleClaim, runnerA.MarkSent(record, "claim-a", "card-1", "https://flows.example/run/1", Now).Kind);

        ApprovalOutboxResult sent = runnerB.MarkSent(record, "claim-b", "card-2", "https://flows.example/run/2", Now.AddMinutes(6));
        Assert.Equal(ApprovalOutboxResultKind.Done, sent.Kind);
        Assert.Equal(ApprovalOutboxStates.Sent, sent.Item!.State);
        Assert.Equal(ApprovalOutboxResultKind.Repeated, runnerB.MarkSent(record, "claim-b", "card-2", "https://flows.example/run/2", Now.AddMinutes(7)).Kind);
        Assert.Equal(ApprovalOutboxResultKind.StaleClaim, runnerB.MarkSent(record, "claim-b", "card-3", "https://flows.example/run/2", Now.AddMinutes(7)).Kind);

        // A release: pending again with one more attempt; the third attempt makes it failed.
        ApprovalOutboxResult released = runnerB.Release(record, "claim-b", Now.AddMinutes(8), 3);
        Assert.Equal(ApprovalOutboxStates.Pending, released.Item!.State);
        Assert.Equal(2, released.Item.Attempts);
        Assert.Null(released.Item.ClaimId);
        Assert.Equal("card-2", released.Item.CardRef);
        Assert.Equal(ApprovalOutboxResultKind.StaleClaim, runnerB.Release(record, "claim-b", Now.AddMinutes(8), 3).Kind);
        ApprovalOutboxItem last = runnerA.TryClaim(runnerA.Get(record)!, "claim-c", Now.AddMinutes(9), TimeSpan.FromMinutes(5), 3)!;
        Assert.Equal(ApprovalOutboxStates.Failed, runnerA.Release(record, last.ClaimId!, Now.AddMinutes(10), 3).Item!.State);
        Assert.DoesNotContain(store.Store.ClaimableOutboxItems(Now.AddYears(1), 1000), i => i.ApprovalRecordId == record);
        Assert.Equal(ApprovalOutboxResultKind.NotFound, runnerA.Release(Id(), "claim-c", Now, 3).Kind);

        // A row with a stale version is never written.
        ApprovalOutboxItem stored = store.Store.GetOutboxItem(record)!;
        Assert.False(store.Store.TrySetOutboxItem(new ApprovalOutboxItem(record, stored.DecisionId, stored.EnqueuedOnUtc, ApprovalOutboxStates.Pending, null, null, 0, null, null, Now, stored.RowVersion)));
        Assert.Equal(ApprovalOutboxStates.Failed, store.Store.GetOutboxItem(record)!.State);
    }

    [Fact]
    public void SeedingNeverTouchesTheOutbox()
    {
        using RowStoreUnderTest store = Create();
        using IDisposable caller = TestCaller.Enter();
        string record = store.Seed("approval-record", CanonicalJson.Write(Pending(Id(), Now), CanonicalProfile.Request), Id());
        Assert.Null(store.Store.GetOutboxItem(record));
    }

    [Fact]
    public void OutboxRowsHoldOnlyIdsStatesAndTimes()
    {
        Assert.Equal(new[] { "pending", "claimed", "sent", "answered", "expired", "cancelled", "failed" }, ApprovalOutboxStates.All);
        Assert.Throws<ArgumentException>(() => new ApprovalOutboxItem(Id(), Id(), Now, "approved", null, null, 0, null, null, Now, 1));
        Assert.Throws<ArgumentException>(() => ApprovalOutboxWrite.Settle(Id(), ApprovalOutboxStates.Pending, Now));
        Assert.Equal(ApprovalOutboxStates.Answered, ApprovalOutboxStates.ForOutcomeMeaning("recommendation-only"));
        Assert.Equal(ApprovalOutboxStates.Expired, ApprovalOutboxStates.ForOutcomeMeaning("expired"));
        Assert.Null(ApprovalOutboxStates.ForOutcomeMeaning("pending"));
    }

    private protected RowStoreUnderTest Create()
    {
        return Kind == StoreKind.Sqlite ? Stores.NewSqlite() : Stores.NewSqlServer();
    }

    protected static string Id()
    {
        return Guid.NewGuid().ToString("D");
    }

    protected static JsonTreeNode Pending(string decision, DateTime requestedOn)
    {
        return CanonicalJson.Parse("{\"decision\":\"" + decision + "\",\"approvalRule\":\"fixture-rule\",\"requiredRole\":\"platform-admin\",\"outcome\":\"approval-pending\","
            + "\"outcomeMeaning\":\"pending\",\"requestedOn\":\"" + CanonicalJson.FormatUtc(requestedOn) + "\",\"countsTowardRequirement\":false}");
    }

    private protected static void Commit(RowStoreUnderTest store, Action<IStoreTransaction> write)
    {
        using IStoreTransaction tx = store.Records.Begin();
        write(tx);
        tx.Commit();
    }
}

public sealed class SqliteApprovalOutboxTests : ApprovalOutboxStoreTests
{
    protected override StoreKind Kind => StoreKind.Sqlite;

    [Fact]
    public void TheOutboxSurvivesAReopenAndAnOlderFileGetsTheTableOnFirstUse()
    {
        string folder = Paths.NewScratchFolder();
        string file = Path.Combine(folder, "product.db");
        try
        {
            string record = Id();
            using (RowStoreUnderTest store = Stores.NewSqlite(file))
            using (TestCaller.Enter())
            {
                Commit(store, tx => tx.Create("approval-record", record, null, Pending(Id(), new DateTime(2026, 11, 23, 9, 0, 0, DateTimeKind.Utc))));
            }

            SqliteConnection.ClearAllPools();
            using (RowStoreUnderTest reopened = Stores.NewSqlite(file))
            {
                Assert.Equal(ApprovalOutboxStates.Pending, reopened.Store.GetOutboxItem(record)!.State);
            }

            // A file the first schema made has no outbox: the store adds it on first use and records version 2.
            SqliteConnection.ClearAllPools();
            using (var connection = new SqliteConnection("Data Source=" + file))
            {
                connection.Open();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "DROP TABLE \"ApprovalOutbox\"; DELETE FROM \"SchemaVersion\" WHERE \"Version\" > 1;";
                command.ExecuteNonQuery();
            }

            SqliteConnection.ClearAllPools();
            using (RowStoreUnderTest upgraded = Stores.NewSqlite(file))
            {
                Assert.False(upgraded.Store.CanConnect());
                Assert.Null(upgraded.Store.GetOutboxItem(record));
                Assert.True(upgraded.Store.CanConnect());
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Stores.TryDelete(folder);
        }
    }
}

[Trait("Store", "SqlServer")]
public sealed class SqlServerApprovalOutboxTests : ApprovalOutboxStoreTests
{
    protected override StoreKind Kind => StoreKind.SqlServer;
}
