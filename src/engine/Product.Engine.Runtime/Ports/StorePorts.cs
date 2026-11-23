using System;
using System.Collections.Generic;
using Product.Engine.Core.Canonical;

namespace Product.Engine.Runtime.Ports;

// The storage ports of the engine runtime (document 25, sections 3.1 and 3.3). The Dataverse adapter implements them
// over IOrganizationService, the Azure adapter over Azure SQL, and InMemoryStore (in Product.Engine.Testing) for tests and
// the local harness. The runtime holds no rule a port could change: ports only read and write rows.

// Reads rows by ID, by key or by a typed query.
public interface IRecordReader
{
    // One row by ID, or null.
    StoredRow? Get(string entity, string id);

    // One configuration row by key path, or null.
    StoredRow? GetByKey(string entity, string key);

    // The rows that match, in a stable order (by ID, ordinal).
    IReadOnlyList<StoredRow> Query(RowQuery query);
}

// The operational and configuration rows (document 22). Every write goes through a transaction: one engine call is one
// transaction, with the guarded row write, its effects and its Audit Entry (document 25, sections 5.10 and 12.3).
public interface IRecordStore : IRecordReader
{
    // The store's identity: the organization ID on Dataverse, the database on Azure. Part of the snapshot cache key
    // (document 25, section 6.3).
    string StoreId { get; }

    IStoreTransaction Begin();
}

// One transaction. Reads see its own writes. Disposing it without Commit rolls it back.
public interface IStoreTransaction : IRecordReader, IDisposable
{
    // A new row ID. The store makes IDs; the engine core never does (document 25, section 3.2).
    string NewId(string entity);

    void Create(string entity, string id, string? key, JsonTreeNode columns);

    // Merges the columns given into the row: a column named is replaced, a JSON null clears it, others are kept.
    void Update(string entity, string id, JsonTreeNode columns);

    void Delete(string entity, string id);

    // Applies every write at once. A unique key violated by a concurrent writer throws StoreConflictException.
    void Commit();
}

// The in-force configuration version and its snapshot (document 25, section 6.3).
public interface IConfigurationStore
{
    string StoreId { get; }

    // The version probe: one indexed read of the in-force set's current version number (C01). Null when no version
    // exists yet.
    InForceVersion? ProbeInForceVersion();

    // One Configuration Set Version (C02): Content Json and Content Hash. Null when the version does not exist.
    StoredVersion? GetVersion(string configurationSetKey, int versionNumber);
}

// What the version probe returns.
public sealed class InForceVersion
{
    public InForceVersion(string configurationSetKey, int versionNumber)
    {
        if (string.IsNullOrEmpty(configurationSetKey))
        {
            throw new ArgumentException("The in-force set has a key.", nameof(configurationSetKey));
        }

        if (versionNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(versionNumber), "Version numbers start at 1.");
        }

        ConfigurationSetKey = configurationSetKey;
        VersionNumber = versionNumber;
    }

    public string ConfigurationSetKey { get; }

    public int VersionNumber { get; }
}

// One stored C02 row, as the snapshot cache reads it.
public sealed class StoredVersion
{
    public StoredVersion(string configurationSetKey, int versionNumber, JsonTreeNode contentJson, string contentHash)
    {
        ConfigurationSetKey = configurationSetKey ?? throw new ArgumentNullException(nameof(configurationSetKey));
        VersionNumber = versionNumber;
        ContentJson = contentJson ?? throw new ArgumentNullException(nameof(contentJson));
        ContentHash = contentHash ?? string.Empty;
    }

    public string ConfigurationSetKey { get; }

    public int VersionNumber { get; }

    // C02 Content Json.
    public JsonTreeNode ContentJson { get; }

    // C02 Content Hash, as stored. The cache checks it against the content before use.
    public string ContentHash { get; }
}

// A unique key held by another transaction that committed first (document 25, section 8: "the unique key makes the
// second insert fail inside its transaction; the caller retries and receives the first result").
public sealed class StoreConflictException : Exception
{
    public StoreConflictException(string message)
        : base(message)
    {
    }
}

// The time source of the runtime. The core reads no clock (document 25, section 3.2), so the runtime reads one here
// and passes it into every context as asOfUtc.
public interface IEngineClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemClock : IEngineClock
{
    public static readonly SystemClock Instance = new();

    public DateTime UtcNow => DateTime.UtcNow;
}
