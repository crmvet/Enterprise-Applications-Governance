using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;
using Product.Engine.Core.Snapshots;
using Product.Engine.Runtime.Ports;

namespace Product.Engine.Runtime.Snapshots;

// What a call needs the snapshot for. Writes always probe the version (document 25, section 6.3: "no time-to-live by
// default for writes"); reads may reuse a recent probe for engine-read-cache-seconds.
public enum SnapshotPurpose
{
    Write,
    Read,
}

// The answer of a snapshot load: the compiled snapshot, or the reason the engine cannot answer (Unavailable, never a
// fallback, document 25, rule 10).
public sealed class SnapshotLoad
{
    private SnapshotLoad(CompiledSnapshot? snapshot, Reason? problem, bool fromCache, bool probed)
    {
        Snapshot = snapshot;
        Problem = problem;
        FromCache = fromCache;
        Probed = probed;
    }

    public CompiledSnapshot? Snapshot { get; }

    public Reason? Problem { get; }

    // True when the compiled snapshot came from the cache, with no read of C02.
    public bool FromCache { get; }

    // True when this load ran the version probe.
    public bool Probed { get; }

    internal static SnapshotLoad Found(CompiledSnapshot snapshot, bool fromCache, bool probed)
    {
        return new SnapshotLoad(snapshot, null, fromCache, probed);
    }

    internal static SnapshotLoad Unavailable(string code, string message, bool probed)
    {
        return new SnapshotLoad(null, new Reason(code, ReasonSeverity.Error, message, "Configuration Set Version", null), false, probed);
    }
}

// The snapshot cache with the version probe (document 25, section 6.3).
//
// 1. Which version is in force? One probe of the in-force set's current version number per call.
// 2. Cache hit? A thread-safe dictionary keyed by store ID, set key and version number. A version never changes once
//    created (G-07), so a hit is always right.
// 3. On a miss: read C02 Content Json, check it against Content Hash, compile it once, and swap it in atomically.
// 4. Every result carries the version and hash used.
//
// So an activated version takes effect on the next call anywhere: the probe sees the new number and loads it. The cache
// is only an optimization: a host that restarts loses it and reloads, and correctness never depends on it. Hosts keep one
// instance for the process (the plug-in's static cache on Dataverse, the Functions instance's cache on Azure).
public sealed class SnapshotCache
{
    private readonly ConcurrentDictionary<string, CompiledSnapshot> _snapshots = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ProbeMemo> _probes = new(StringComparer.Ordinal);
    private int _compiles;
    private int _probeCount;

    // How many snapshots this cache compiled (each version once per store, unless evicted).
    public int CompileCount => Volatile.Read(ref _compiles);

    // How many version probes this cache ran.
    public int ProbeCount => Volatile.Read(ref _probeCount);

    // The snapshot in force now. A write always probes. A read reuses the last probe of this store while it is younger
    // than the in-force snapshot's engine-read-cache-seconds setting (0, the shipped value, means probe every call).
    public SnapshotLoad Load(IConfigurationStore store, SnapshotPurpose purpose, DateTime nowUtc)
    {
        if (store == null)
        {
            throw new ArgumentNullException(nameof(store));
        }

        if (purpose == SnapshotPurpose.Read && _probes.TryGetValue(store.StoreId, out ProbeMemo? memo))
        {
            CompiledSnapshot? cached = Find(store.StoreId, memo.Version.ConfigurationSetKey, memo.Version.VersionNumber);
            int ttl = cached == null ? 0 : ReadCacheSeconds(cached.Snapshot);
            if (cached != null && ttl > 0 && nowUtc >= memo.ProbedOnUtc && (nowUtc - memo.ProbedOnUtc).TotalSeconds < ttl)
            {
                return SnapshotLoad.Found(cached, fromCache: true, probed: false);
            }
        }

        Interlocked.Increment(ref _probeCount);
        InForceVersion? version = store.ProbeInForceVersion();
        if (version == null)
        {
            _probes.TryRemove(store.StoreId, out _);
            return SnapshotLoad.Unavailable(ReasonCodes.EngineUnavailable, "No configuration version is in force, so the engine cannot answer. Bootstrap version 1 first.", probed: true);
        }

        _probes[store.StoreId] = new ProbeMemo(version, nowUtc);
        return LoadVersion(store, version.ConfigurationSetKey, version.VersionNumber, probed: true);
    }

    // One version by number, such as the version a decision was stamped with (OP-03 recomputes OP-02 with it, document
    // 25, section 5.5). No probe: a version never changes once created.
    public SnapshotLoad LoadVersion(IConfigurationStore store, string configurationSetKey, int versionNumber)
    {
        return LoadVersion(store, configurationSetKey, versionNumber, probed: false);
    }

    // Drops every cached snapshot and probe. Only an optimization is lost.
    public void Clear()
    {
        _snapshots.Clear();
        _probes.Clear();
    }

    private SnapshotLoad LoadVersion(IConfigurationStore store, string configurationSetKey, int versionNumber, bool probed)
    {
        if (store == null)
        {
            throw new ArgumentNullException(nameof(store));
        }

        CompiledSnapshot? hit = Find(store.StoreId, configurationSetKey, versionNumber);
        if (hit != null)
        {
            return SnapshotLoad.Found(hit, fromCache: true, probed);
        }

        StoredVersion? stored = store.GetVersion(configurationSetKey, versionNumber);
        if (stored == null)
        {
            return SnapshotLoad.Unavailable(
                ReasonCodes.EngineUnavailable,
                "Configuration version " + versionNumber + " of " + configurationSetKey + " is not stored, so the engine cannot answer.",
                probed);
        }

        // The content must match its stored hash, or the engine cannot prove which rules applied (document 25, section 7).
        string computed;
        try
        {
            computed = CanonicalHash.ComputeContentHash(stored.ContentJson);
        }
        catch (CanonicalJsonException)
        {
            return SnapshotLoad.Unavailable(ReasonCodes.SnapshotHashMismatch, "Configuration version " + versionNumber + " holds content that cannot be hashed, so the engine cannot answer.", probed);
        }

        if (!string.Equals(computed, stored.ContentHash, StringComparison.Ordinal))
        {
            return SnapshotLoad.Unavailable(ReasonCodes.SnapshotHashMismatch, "Configuration version " + versionNumber + " does not match its stored content hash, so the engine cannot answer.", probed);
        }

        CompiledSnapshot compiled;
        try
        {
            compiled = SnapshotCompiler.Compile(stored.ContentJson, versionNumber, configurationSetKey);
        }
        catch (SnapshotCompileException e)
        {
            string code = e.Code == ReasonCodes.SnapshotSchemaUnsupported ? ReasonCodes.SnapshotSchemaUnsupported : ReasonCodes.EngineUnavailable;
            return SnapshotLoad.Unavailable(code, "Configuration version " + versionNumber + " cannot be read: " + e.Message, probed);
        }

        Interlocked.Increment(ref _compiles);

        // Swap in atomically. When two calls compile the same version at once, both get the first one stored.
        CompiledSnapshot stored2 = _snapshots.GetOrAdd(Key(store.StoreId, configurationSetKey, versionNumber), compiled);
        return SnapshotLoad.Found(stored2, fromCache: false, probed);
    }

    private CompiledSnapshot? Find(string storeId, string setKey, int versionNumber)
    {
        return _snapshots.TryGetValue(Key(storeId, setKey, versionNumber), out CompiledSnapshot? snapshot) ? snapshot : null;
    }

    // The store ID is part of the key: whether one host process ever serves two environments is UNVERIFIED (document 25,
    // section 6.3), so the key never assumes it does not.
    private static string Key(string storeId, string setKey, int versionNumber)
    {
        return storeId + "|" + setKey + "|" + versionNumber.ToString(CultureInfo.InvariantCulture);
    }

    // engine-read-cache-seconds (document 25, section 6.5, a proposal). Absent, malformed or negative means 0.
    private static int ReadCacheSeconds(ConfigurationSnapshot snapshot)
    {
        if (snapshot.TryGetRow("setting", "engine-read-cache-seconds", out SnapshotRow? row) && row.IsActive
            && row.TryGetColumn("value", out JsonTreeNode? value) && value.TryGetInt64(out long seconds) && seconds > 0)
        {
            return seconds > int.MaxValue ? int.MaxValue : (int)seconds;
        }

        return 0;
    }

    private sealed class ProbeMemo
    {
        public ProbeMemo(InForceVersion version, DateTime probedOnUtc)
        {
            Version = version;
            ProbedOnUtc = probedOnUtc;
        }

        public InForceVersion Version { get; }

        public DateTime ProbedOnUtc { get; }
    }
}
