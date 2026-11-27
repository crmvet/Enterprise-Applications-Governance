using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Product.Azure.Host;
using Product.Azure.Stores;
using Product.Azure.Stores.Sqlite;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Telemetry;
using Xunit;

namespace Product.Azure.Tests;

// The database round trips of a request (wave 4b review): every call the row store makes to its database counts one, and the
// MCP request log line reports the count, so a slow page shows whether it read row by row. Outside a request nothing is
// counted.
public sealed class StoreRoundTripTests
{
    [Fact]
    public async Task EachDatabaseCallCountsOneAndTheRequestLogLineReportsIt()
    {
        await Task.Yield();
        string folder = TestPaths.NewScratchFolder();
        try
        {
            using var store = new SqliteProductStore(new SqliteStoreOptions { ConnectionString = ProductTestHost.ConnectionString(Path.Combine(folder, "product.db")), NewSeedId = RowIds.EntityCounter() });
            store.EnsureSchema();
            Assert.Null(StoreRoundTrips.Current);

            StoreRoundTrips.Begin();
            store.Get("business-application", "00000000-0000-4000-8000-000000000511");
            store.Query(RowQuery.For("business-application"));
            Assert.Equal(2, StoreRoundTrips.Current);

            var logger = new LineLogger();
            new LoggerTelemetrySink(logger).Track(new TelemetryEvent("tools/call", "find_applications", "ok", 12, "Web app", "caller-hash", "corr-1", 1));
            string line = Assert.Single(logger.Lines);
            Assert.EndsWith("configuration version 1; store round trips 2", line, StringComparison.Ordinal);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TestPaths.TryDelete(folder);
        }
    }

    // A logger that keeps each formatted line.
    private sealed class LineLogger : ILogger<LoggerTelemetrySink>
    {
        public List<string> Lines { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Lines.Add(formatter(state, exception));
        }
    }
}
