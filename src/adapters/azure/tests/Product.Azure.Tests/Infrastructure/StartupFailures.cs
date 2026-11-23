using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Product.Azure.Tests;

// Starts a host that must fail its startup checks and returns every message of the failure.
//
// When a host fails to start, the in-memory test server sometimes reports that its service provider was already
// disposed instead of the startup failure itself: the test server and the program's own entry point both stop the host,
// and whichever reports first wins. That says nothing about the product, so a start that fails with only that report
// is tried again, up to three times. A start that does not fail, or that fails for another reason, ends at once.
internal static class StartupFailures
{
    public static string Messages(HostSettings settings)
    {
        string text = string.Empty;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            string folder = TestPaths.NewScratchFolder();
            settings.DatabaseFile = Path.Combine(folder, "product.db");
            try
            {
                Exception failed = Assert.ThrowsAny<Exception>(() =>
                {
                    ProductTestHost host = ProductTestHost.Start(settings);
                    try
                    {
                        host.Get("/health/live").GetAwaiter().GetResult();
                    }
                    finally
                    {
                        host.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    }
                });
                text = Text(failed);
                if (!OnlyDisposed(failed))
                {
                    return text;
                }
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                TestPaths.TryDelete(folder);
            }
        }

        return text;
    }

    // True when the start failed with only the disposed service provider report, not a startup check.
    internal static bool OnlyDisposed(Exception exception)
    {
        List<Exception> chain = Chain(exception).ToList();
        return chain.Any(e => e is ObjectDisposedException) && !chain.Any(e => e is Microsoft.Extensions.Options.OptionsValidationException);
    }

    private static string Text(Exception exception)
    {
        return string.Join(" | ", Chain(exception).Select(e => e.Message));
    }

    private static IEnumerable<Exception> Chain(Exception exception)
    {
        var seen = new HashSet<Exception>();
        var queue = new Queue<Exception>();
        queue.Enqueue(exception);
        while (queue.Count > 0)
        {
            Exception current = queue.Dequeue();
            if (!seen.Add(current))
            {
                continue;
            }

            yield return current;
            if (current is AggregateException aggregate)
            {
                foreach (Exception inner in aggregate.InnerExceptions)
                {
                    queue.Enqueue(inner);
                }
            }
            else if (current.InnerException != null)
            {
                queue.Enqueue(current.InnerException);
            }
        }
    }
}
