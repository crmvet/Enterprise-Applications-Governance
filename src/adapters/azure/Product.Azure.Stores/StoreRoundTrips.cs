using System.Runtime.CompilerServices;
using System.Threading;

namespace Product.Azure.Stores;

// How many database round trips the current request made (wave 4b review): every call RowStore makes to its database (a
// read, a key set read, a commit, a counter) counts one. The host starts a count for each HTTP request
// (ProductPipeline), and the MCP request log line reports it, so a slow page shows whether it read row by row. Outside a
// request nothing is counted. It holds a number only, never a value or a key.
public static class StoreRoundTrips
{
    private static readonly AsyncLocal<StrongBox<int>?> Ambient = new();

    // The count of the current request, or null outside one.
    public static int? Current => Ambient.Value?.Value;

    // Starts a new count for the code that runs from here on (call it at the start of an async method, so the count ends
    // with it).
    public static void Begin()
    {
        Ambient.Value = new StrongBox<int>(0);
    }

    // One round trip.
    internal static void Add()
    {
        StrongBox<int>? box = Ambient.Value;
        if (box != null)
        {
            Interlocked.Increment(ref box.Value);
        }
    }
}
