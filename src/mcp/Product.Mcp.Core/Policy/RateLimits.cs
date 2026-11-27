using System;
using Product.Mcp.Core.Telemetry;

namespace Product.Mcp.Core.Policy;

// The counter behind Tool Policy Calls Per Person Per Hour (document 24, section 2.3; I-107). The counter lives in the
// store or in a gateway, never in the server's memory, so any instance answers any request and a
// restart does not reset it (document 24, section 5). The backend's store adapter implements this port.
public interface IRateLimitStore
{
    // Adds one call to the counter for the key in the window that starts at windowStartUtc and returns the count after
    // the increment. One atomic operation in the store.
    long Increment(string counterKey, DateTime windowStartUtc, TimeSpan windowLength);
}

// The rate limit check. Holds no state: the count comes from the store port on every call.
public static class RateLimiter
{
    public static readonly TimeSpan Window = TimeSpan.FromHours(1);

    // Null when the call may go on, else the seconds until the window ends (at least 1). A row without a limit never
    // counts. The key is a hash of the person and the tool, so the store never holds a raw object ID for it.
    public static int? Check(IRateLimitStore store, ToolPolicyRow? row, string personObjectId, string toolKey, DateTime utcNow)
    {
        if (store == null)
        {
            throw new ArgumentNullException(nameof(store));
        }

        if (row == null || !row.CallsPerPersonPerHour.HasValue)
        {
            return null;
        }

        DateTime windowStart = new DateTime(utcNow.Year, utcNow.Month, utcNow.Day, utcNow.Hour, 0, 0, DateTimeKind.Utc);
        string key = "tool-calls|" + toolKey + "|" + TelemetryEvent.HashCaller(personObjectId, "rate-limit");
        long count = store.Increment(key, windowStart, Window);
        if (count <= row.CallsPerPersonPerHour.Value)
        {
            return null;
        }

        int seconds = (int)Math.Ceiling((windowStart + Window - utcNow).TotalSeconds);
        return Math.Max(1, seconds);
    }

    // The most refused calls of one caller the audit ledger records in an hour (security review 2026-11-24, document 24,
    // B24-14). A refusal past it is still answered and still in the request logs; only the ledger stops taking more, so a
    // client that loops on refused calls cannot fill the append-only ledger.
    public const int RefusalsRecordedPerHour = 60;

    // True while the caller's refusals this hour stay within RefusalsRecordedPerHour. Counts this refusal.
    public static bool MayRecordRefusal(IRateLimitStore store, string personObjectId, DateTime utcNow)
    {
        if (store == null)
        {
            throw new ArgumentNullException(nameof(store));
        }

        DateTime windowStart = new DateTime(utcNow.Year, utcNow.Month, utcNow.Day, utcNow.Hour, 0, 0, DateTimeKind.Utc);
        string key = "refusals-recorded|" + TelemetryEvent.HashCaller(personObjectId, "rate-limit");
        return store.Increment(key, windowStart, Window) <= RefusalsRecordedPerHour;
    }
}
