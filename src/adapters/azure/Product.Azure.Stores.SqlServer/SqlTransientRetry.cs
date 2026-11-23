using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Data.SqlClient;

namespace Product.Azure.Stores.SqlServer;

// Retries of transient Azure SQL failures, as Microsoft's guidance for Azure SQL Database describes them: a database that
// moves during a failover, scaling or an update answers errors such as 40613 or 40197 for up to about a minute, and the
// client opens a fresh connection and tries again. The first retry waits at least 5 seconds, each later one twice as
// long, up to 60 seconds, plus up to a quarter more at random so many clients do not retry at once.
//
// What the store retries (SqlServerRowDatabase):
// - opening a connection and setting its session context, for every operation;
// - a whole read, on a fresh connection;
// - a whole commit (IRowDatabase.Apply) when the failure came before the transaction was told to commit: the server
//   rolled it back, so nothing was written. A failure during the commit itself is never retried, since the transaction
//   may have committed. The engine's own retry of a StoreConflictException then decides, as for any conflict.
// A counter increment and an outbox update run one statement each; only their connection open is retried.
public sealed class SqlTransientRetry
{
    // Microsoft's guidance: no retry sooner than 5 seconds after a transient failure, and no wait longer than 60.
    public static readonly TimeSpan DefaultFirstDelay = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(60);
    public const int DefaultRetries = 3;
    public const int MaxRetries = 10;

    // The error numbers SqlClient's own retry providers treat as transient (SqlConfigurableRetryFactory), plus the
    // connection drops the earlier transient fault handling block retried: 64, 10053 and 10054.
    public static readonly IReadOnlyCollection<int> TransientErrorNumbers = new SortedSet<int>(SqlConfigurableRetryFactory.BaselineTransientErrors.Concat(new[] { 64, 10053, 10054 }));

    private readonly Action<TimeSpan> _sleep;
    private readonly Func<double> _random;
    private readonly Func<Exception, bool> _isTransient;

    public SqlTransientRetry(int retries = DefaultRetries, TimeSpan? firstDelay = null, Action<TimeSpan>? sleep = null, Func<double>? random = null, Func<Exception, bool>? isTransient = null)
    {
        if (retries < 0 || retries > MaxRetries)
        {
            throw new ArgumentOutOfRangeException(nameof(retries), "Transient retries are 0 to " + MaxRetries + ".");
        }

        TimeSpan first = firstDelay ?? DefaultFirstDelay;
        if (first < TimeSpan.Zero || first > MaxDelay)
        {
            throw new ArgumentOutOfRangeException(nameof(firstDelay), "The first retry delay is 0 to 60 seconds.");
        }

        Retries = retries;
        FirstDelay = first;
        _sleep = sleep ?? Thread.Sleep;
        _random = random ?? Random.Shared.NextDouble;
        _isTransient = isTransient ?? IsTransient;
    }

    // How many times a failed attempt is tried again; 0 turns retries off.
    public int Retries { get; }

    public TimeSpan FirstDelay { get; }

    // True for a SqlException that carries a transient error number.
    public static bool IsTransient(Exception exception)
    {
        return exception is SqlException sql && sql.Errors.Cast<SqlError>().Any(e => IsTransientNumber(e.Number));
    }

    public static bool IsTransientNumber(int number)
    {
        return TransientErrorNumbers.Contains(number);
    }

    // The wait before retry number (1 for the first): FirstDelay times 2^(retry - 1), at most 60 seconds, plus up to a
    // quarter more at random (still at most 60 seconds).
    public TimeSpan DelayBefore(int retry)
    {
        if (retry < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(retry), "Retries count from 1.");
        }

        double baseMs = Math.Min(MaxDelay.TotalMilliseconds, FirstDelay.TotalMilliseconds * Math.Pow(2, Math.Min(retry - 1, 30)));
        double random = Math.Max(0, Math.Min(1, _random()));
        return TimeSpan.FromMilliseconds(Math.Min(MaxDelay.TotalMilliseconds, baseMs * (1 + 0.25 * random)));
    }

    // Runs the attempt, and runs it again after a wait while it throws a transient failure that retryable still allows,
    // up to Retries more times. Any other exception, and the last transient one, goes to the caller unchanged.
    public T Run<T>(Func<T> attempt, Func<Exception, bool>? retryable = null)
    {
        if (attempt == null)
        {
            throw new ArgumentNullException(nameof(attempt));
        }

        for (int retry = 1; ; retry++)
        {
            try
            {
                return attempt();
            }
            catch (Exception e) when (retry <= Retries && _isTransient(e) && (retryable == null || retryable(e)))
            {
                _sleep(DelayBefore(retry));
            }
        }
    }

    public void Run(Action attempt, Func<Exception, bool>? retryable = null)
    {
        if (attempt == null)
        {
            throw new ArgumentNullException(nameof(attempt));
        }

        Run<bool>(() =>
        {
            attempt();
            return true;
        }, retryable);
    }
}
