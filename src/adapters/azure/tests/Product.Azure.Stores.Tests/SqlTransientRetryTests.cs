using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.SqlClient;
using Product.Azure.Stores.SqlServer;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Runtime.Callers;
using Xunit;

namespace Product.Azure.Stores.Tests;

// The Azure SQL store's retries of transient failures (SqlTransientRetry): which failures, how long it waits, how often,
// and that a write is never retried once its commit may have happened. No database is needed: the waits are recorded,
// not slept, and a closed loopback port stands in for a database that cannot be reached.
public sealed class SqlTransientRetryTests
{
    // Nothing can listen at this address, so opening a connection fails within a second.
    private const string Unreachable = "Server=tcp:0.0.0.0,1;Database=product;Encrypt=True;Connect Timeout=1;ConnectRetryCount=0;Pooling=false";

    [Fact]
    public void ATransientFailureIsTriedAgainAfterAtLeastFiveSecondsThenTwiceAsLong()
    {
        var waits = new List<TimeSpan>();
        var retry = new SqlTransientRetry(sleep: waits.Add, random: () => 0, isTransient: e => e is TimeoutException);
        int attempts = 0;
        string result = retry.Run(() =>
        {
            attempts++;
            return attempts < 3 ? throw new TimeoutException("transient " + attempts) : "done";
        });

        Assert.Equal("done", result);
        Assert.Equal(3, attempts);
        Assert.Equal(new[] { TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) }, waits);
    }

    [Fact]
    public void TheWaitGrowsToSixtySecondsAtMostWithUpToAQuarterMoreAtRandom()
    {
        var low = new SqlTransientRetry(retries: 10, random: () => 0);
        Assert.Equal(new[] { 5, 10, 20, 40, 60, 60 }, Enumerable.Range(1, 6).Select(r => (int)low.DelayBefore(r).TotalSeconds));
        var high = new SqlTransientRetry(retries: 10, random: () => 1);
        Assert.Equal(TimeSpan.FromSeconds(6.25), high.DelayBefore(1));
        Assert.Equal(TimeSpan.FromSeconds(50), high.DelayBefore(4));
        Assert.Equal(TimeSpan.FromSeconds(60), high.DelayBefore(5));
        Assert.All(Enumerable.Range(1, 10), r => Assert.InRange(new SqlTransientRetry(retries: 10).DelayBefore(r), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60)));
        Assert.Equal(SqlTransientRetry.DefaultFirstDelay, new SqlTransientRetry().FirstDelay);
        Assert.Equal(3, new SqlTransientRetry().Retries);
        Assert.Throws<ArgumentOutOfRangeException>(() => low.DelayBefore(0));
    }

    [Fact]
    public void AFailureThatIsNotTransientGoesToTheCallerAtOnce()
    {
        var waits = new List<TimeSpan>();
        var retry = new SqlTransientRetry(sleep: waits.Add, isTransient: e => e is TimeoutException);
        int attempts = 0;
        Assert.Throws<InvalidOperationException>(() => retry.Run<int>(() =>
        {
            attempts++;
            throw new InvalidOperationException("not transient");
        }));
        Assert.Equal(1, attempts);
        Assert.Empty(waits);
    }

    [Fact]
    public void TheLastTransientFailureGoesToTheCallerAfterTheLastRetry()
    {
        var waits = new List<TimeSpan>();
        var retry = new SqlTransientRetry(retries: 2, firstDelay: TimeSpan.FromSeconds(7), sleep: waits.Add, random: () => 0, isTransient: e => e is TimeoutException);
        int attempts = 0;
        TimeoutException last = Assert.Throws<TimeoutException>(() => retry.Run(() =>
        {
            attempts++;
            throw new TimeoutException("attempt " + attempts);
        }));
        Assert.Equal("attempt 3", last.Message);
        Assert.Equal(new[] { TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(14) }, waits);

        var off = new SqlTransientRetry(retries: 0, sleep: waits.Add, isTransient: e => true);
        attempts = 0;
        Assert.Throws<TimeoutException>(() => off.Run(() =>
        {
            attempts++;
            throw new TimeoutException("once");
        }));
        Assert.Equal(1, attempts);
    }

    // A commit is retried only while the failure came before the transaction was told to commit.
    [Fact]
    public void AFailureTheCallerMarksAsNotRetryableIsNotTried()
    {
        var waits = new List<TimeSpan>();
        var retry = new SqlTransientRetry(sleep: waits.Add, isTransient: e => e is TimeoutException);
        bool committing = false;
        int attempts = 0;
        Assert.Throws<TimeoutException>(() => retry.Run(
            () =>
            {
                committing = false;
                attempts++;
                committing = true;
                throw new TimeoutException("during the commit");
            },
            _ => !committing));
        Assert.Equal(1, attempts);
        Assert.Empty(waits);
    }

    [Fact]
    public void TheTransientErrorsAreSqlClientsListPlusConnectionDrops()
    {
        foreach (int number in new[] { 40613, 40197, 40501, 40540, 40143, 49918, 49919, 49920, 10928, 10929, 4060, 4221, 1205, 233, 10060, 64, 10053, 10054 })
        {
            Assert.True(SqlTransientRetry.IsTransientNumber(number), number.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        // Login failures, duplicate keys, row-level security refusals and conversion errors are answers, not faults.
        foreach (int number in new[] { 18456, 2601, 2627, 33504, 547, 245, 0, -2 })
        {
            Assert.False(SqlTransientRetry.IsTransientNumber(number), number.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        Assert.All(SqlConfigurableRetryFactory.BaselineTransientErrors, n => Assert.True(SqlTransientRetry.IsTransientNumber(n)));
        Assert.False(SqlTransientRetry.IsTransient(new TimeoutException()));
        Assert.False(SqlTransientRetry.IsTransient(new InvalidOperationException()));
    }

    [Fact]
    public void TheStoreOptionsBoundTheRetries()
    {
        const string connection = "Server=tcp:contoso-fixture.database.windows.net,1433;Database=product;Authentication=Active Directory Default;Encrypt=True";
        using (new SqlServerProductStore(new SqlServerStoreOptions { ConnectionString = connection, TransientRetries = 0 }))
        using (new SqlServerProductStore(new SqlServerStoreOptions { ConnectionString = connection, TransientRetries = 10, TransientRetryFirstDelay = TimeSpan.FromSeconds(60) }))
        {
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => new SqlServerProductStore(new SqlServerStoreOptions { ConnectionString = connection, TransientRetries = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SqlServerProductStore(new SqlServerStoreOptions { ConnectionString = connection, TransientRetries = 11 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SqlServerProductStore(new SqlServerStoreOptions { ConnectionString = connection, TransientRetryFirstDelay = TimeSpan.FromSeconds(-1) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SqlServerProductStore(new SqlServerStoreOptions { ConnectionString = connection, TransientRetryFirstDelay = TimeSpan.FromSeconds(61) }));
        Assert.Equal(3, new SqlServerStoreOptions().TransientRetries);
        Assert.Equal(TimeSpan.FromSeconds(5), new SqlServerStoreOptions().TransientRetryFirstDelay);
    }

    // The store retries its reads, its commits, its counter's connection and its schema check through the policy: here
    // every failure to connect counts as transient, so each operation is tried twice and waits once before the failure
    // reaches the caller. Nothing listens at the address, so every attempt fails without a database.
    [Fact]
    public void TheStoreRetriesReadsCommitsAndTheSchemaCheckThroughThePolicy()
    {
        var waits = new List<TimeSpan>();
        var retry = new SqlTransientRetry(retries: 1, sleep: waits.Add, random: () => 0, isTransient: e => e is SqlException);
        using var database = new SqlServerRowDatabase(Unreachable, EntityCatalog.Embedded, 5, setSessionContext: true, retry);
        EntityInfo portfolio = EntityCatalog.Embedded.Get("portfolio");

        Assert.Throws<SqlException>(() => database.Get(portfolio, Guid.NewGuid().ToString("D")));
        Assert.Equal(new[] { TimeSpan.FromSeconds(5) }, waits);

        var context = new StoreCallContext("00000000-0000-4000-8000-000000000201", CallerEntry.Person, ActorKindMeaning.Person, "req-retry");
        RowChange insert = RowChange.Insert(portfolio, Guid.NewGuid().ToString("D"), null, CanonicalJson.Parse("{}"));
        Assert.Throws<SqlException>(() => database.Apply(new[] { insert }, context, RowStoreOperations.Write));
        Assert.Equal(2, waits.Count);

        Assert.Throws<SqlException>(() => database.Increment("calls:retry", new DateTime(2026, 11, 23, 9, 0, 0, DateTimeKind.Utc), TimeSpan.FromHours(1)));
        Assert.Equal(3, waits.Count);

        Assert.Throws<SqlException>(() => database.EnsureSchema(EntityCatalog.Embedded));
        Assert.Equal(4, waits.Count);

        // The readiness probe answers at once and never retries.
        Assert.False(database.CanConnect());
        Assert.Equal(4, waits.Count);
    }

    // With the store's own classification, a server that refuses the connection is not a transient fault of Azure SQL,
    // so the store does not wait before it answers.
    [Fact]
    public void ARefusedConnectionIsNotRetriedByDefault()
    {
        var waits = new List<TimeSpan>();
        using var database = new SqlServerRowDatabase(Unreachable, EntityCatalog.Embedded, 5, setSessionContext: true, new SqlTransientRetry(sleep: waits.Add));
        SqlException refused = Assert.Throws<SqlException>(() => database.Get(EntityCatalog.Embedded.Get("portfolio"), Guid.NewGuid().ToString("D")));
        Assert.False(SqlTransientRetry.IsTransient(refused), "error numbers " + string.Join(", ", refused.Errors.Cast<SqlError>().Select(e => e.Number)));
        Assert.Empty(waits);
    }
}
