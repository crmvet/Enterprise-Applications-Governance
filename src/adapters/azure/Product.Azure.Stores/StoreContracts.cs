using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Runtime.Callers;
using Product.Engine.Runtime.Ports;

namespace Product.Azure.Stores;

// The contract between the Azure adapter's stores and its hosts. The hosts bind one IProductStore as every port the
// engine runtime and the MCP server core need; the SQLite and Azure SQL stores implement it through the shared row
// store base. Nothing here holds a rule: the engine decides, the store keeps rows (document 25, sections 3.2 and 12.3).

// One store: the engine's two storage ports, the rate limit counter, the approval outbox the approval runner pulls from
// (ApprovalOutbox.cs), and seeding for local runs and tests.
public interface IProductStore : IRecordStore, IConfigurationStore, ICounterStore, IApprovalOutboxStore, IStoreSeeder, IDisposable
{
    // Creates or upgrades the schema where this store may do so. The SQLite store creates its tables. The Azure SQL
    // store only checks that the migrations it needs are applied and throws when they are not: the app identity never
    // runs DDL; a deployment identity runs the migrations.
    void EnsureSchema();

    // A cheap reachability check for the readiness probe. Never throws.
    bool CanConnect();
}

// The counter behind Tool Policy Calls Per Person Per Hour, kept in the store and never in a server's memory (document
// 24, section 2.3). The host adapts it to the MCP core's IRateLimitStore.
public interface ICounterStore
{
    // Adds one call to the counter for the key in the window that starts at windowStartUtc and returns the count after
    // the increment, in one atomic statement. A counter of an earlier window starts again at one.
    long Increment(string counterKey, DateTime windowStartUtc, TimeSpan windowLength);
}

// Rows written outside the engine: local runs, demos, tests and the first install of a demo. Never an engine write and
// never a product path; the hosts expose it only through the local server's seed command and the tests.
public interface IStoreSeeder
{
    // True when a configuration set is in force with a version (the version probe answers).
    bool HasInForceVersion { get; }

    // A row that exists before the engine runs, such as a Principal Reference or a registered application. Returns its
    // ID; without one, RowStoreOptions.NewSeedId makes it.
    string Seed(string entity, JsonTreeNode columns, string? id = null, string? key = null);

    // Replaces a stored row's columns: only for test setup that stands in for an engine effect not built yet.
    void Replace(string entity, string id, JsonTreeNode columns);

    // A version as an earlier activation left it in force, with the same rows as InMemoryStore.SeedVersion in
    // Product.Engine.Testing: the working set in force at the snapshot's version, the Configuration Set Version row
    // with Content Json and Content Hash, and the live configuration rows equal to the snapshot.
    void SeedVersion(CompiledSnapshot snapshot);
}

// Settings every row store takes. The provider options carry these plus the connection.
public sealed class RowStoreOptions
{
    // The store's identity, part of the snapshot cache key (document 25, section 6.3). Required. Never a local path or
    // a secret: for example "sqlite:product.db" or "azuresql:<server>/<database>".
    public string StoreId { get; set; } = string.Empty;

    // Makes the ID of a row the engine creates (IStoreTransaction.NewId), from the entity key. IDs are lowercase GUIDs
    // in the "D" format, because the MCP contracts type operational IDs as GUIDs.
    public Func<string, string> NewId { get; set; } = RowIds.RandomGuid;

    // Makes the ID of a seeded row that was given none.
    public Func<string, string> NewSeedId { get; set; } = RowIds.RandomGuid;
}

// Row ID sources.
public static class RowIds
{
    public static string RandomGuid(string entity)
    {
        return Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture);
    }

    // Repeatable GUIDs: the prefix (the first 24 characters of a GUID) and a running number in 12 hex digits. With the
    // default prefix it gives the same IDs as GuidIdRecordStore in Product.Mcp.Testing, so a run over HTTP can be
    // compared with the same run in process.
    public static Func<string, string> Sequential(string prefix = "00000000-0000-4000-9000-")
    {
        if (prefix == null || prefix.Length != 24 || prefix[23] != '-')
        {
            throw new ArgumentException("The prefix is the first 24 characters of a GUID, ending with a hyphen.", nameof(prefix));
        }

        long next = 0;
        return entity => prefix + Interlocked.Increment(ref next).ToString("x12", CultureInfo.InvariantCulture);
    }

    // The entity key and a running number shared across entities ("tier-000012"), as InMemoryStore names the rows it
    // seeds. Tests only, so seeded configuration rows carry the same IDs as in process.
    public static Func<string, string> EntityCounter()
    {
        long next = 0;
        return entity => entity + "-" + Interlocked.Increment(ref next).ToString("D6", CultureInfo.InvariantCulture);
    }
}

// The caller of the current request, as the host resolved it after sign-in. The Azure SQL store writes it to
// SESSION_CONTEXT, read-only, on every connection it opens, so row-level security and the database audit can read it
// (document 22, section 11.1; document 25, section 12.3). The SQLite store ignores it. Ambient for the request's async
// flow, because the storage ports carry no caller.
public sealed class StoreCallContext
{
    private static readonly AsyncLocal<StoreCallContext?> Ambient = new();

    public StoreCallContext(
        string principalId,
        CallerEntry? entry,
        ActorKindMeaning? actorKind,
        string correlationId,
        IEnumerable<string>? roleKeys = null,
        string? onBehalfOfId = null,
        string? clientApp = null,
        bool clientAppVerified = false)
    {
        if (string.IsNullOrEmpty(principalId))
        {
            throw new ArgumentException("A call context names its principal.", nameof(principalId));
        }

        if (string.IsNullOrEmpty(correlationId))
        {
            throw new ArgumentException("A call context carries the request's correlation ID.", nameof(correlationId));
        }

        PrincipalId = principalId;
        Entry = entry;
        ActorKind = actorKind;
        CorrelationId = correlationId;
        RoleKeys = new ReadOnlyCollection<string>((roleKeys ?? Enumerable.Empty<string>()).Distinct(StringComparer.Ordinal).OrderBy(r => r, StringComparer.Ordinal).ToList());
        OnBehalfOfId = string.IsNullOrEmpty(onBehalfOfId) ? null : onBehalfOfId;
        ClientApp = string.IsNullOrEmpty(clientApp) ? null : clientApp;
        ClientAppVerified = clientAppVerified;
    }

    // The caller's Entra object ID (oid), or the seed or migration identity's label for setup work.
    public string PrincipalId { get; }

    // How the call arrived. Null for setup work (seed, migrations), which only a deployment identity runs.
    public CallerEntry? Entry { get; }

    // The actor kind the engine resolves for this caller from the configuration in force, or null when no version is
    // in force yet or the caller resolves to no actor.
    public ActorKindMeaning? ActorKind { get; }

    // The product role keys the caller holds in the configuration in force.
    public IReadOnlyList<string> RoleKeys { get; }

    public string? OnBehalfOfId { get; }

    public string? ClientApp { get; }

    public bool ClientAppVerified { get; }

    // The request's correlation ID: the same value in the envelope, the Audit Entry, telemetry and SESSION_CONTEXT.
    public string CorrelationId { get; }

    // The actor kind as the engine contract writes it ("ai-client-for-person"), or null.
    public string? ActorKindWire => ActorKind.HasValue ? EngineContract.ActorKindMeanings.ToWire(ActorKind.Value) : null;

    public static StoreCallContext? Current => Ambient.Value;

    // Sets the context for the rest of this async flow; disposing the result restores the one before.
    public static IDisposable Enter(StoreCallContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        StoreCallContext? before = Ambient.Value;
        Ambient.Value = context;
        return new Restore(before);
    }

    private sealed class Restore : IDisposable
    {
        private readonly StoreCallContext? _before;
        private bool _done;

        public Restore(StoreCallContext? before)
        {
            _before = before;
        }

        public void Dispose()
        {
            if (!_done)
            {
                Ambient.Value = _before;
                _done = true;
            }
        }
    }
}
