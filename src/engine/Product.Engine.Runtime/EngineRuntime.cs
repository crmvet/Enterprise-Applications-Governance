using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Product.Engine.Configuration.Activation;
using Product.Engine.Configuration.Presets;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Configuration.Validation;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Operations;
using Product.Engine.Core.Reasons;
using Product.Engine.Runtime.Callers;
using Product.Engine.Runtime.Ports;
using Product.Engine.Runtime.Snapshots;

namespace Product.Engine.Runtime;

// The engine runtime (document 25, sections 3.1 and 3.3). Orchestration only: it loads the snapshot in
// force through the configuration port (one version probe per call), resolves the caller's roles from it, asks what facts
// an operation needs, fetches them through the record port, evaluates with the pure core, and applies a write plan and
// its effects in one transaction, each effect again through OP-08 as an engine-nested write. It holds no rule: every
// rule is in the core (OP-01 to OP-14) or the configuration assembly (activation, the bootstrap, OP-09).
//
// The adapters bind it: plug-ins and custom APIs on Dataverse, Azure Functions over Azure SQL on Azure, InMemoryStore
// (in Product.Engine.Testing) in tests and the local harness. The runtime reads the clock through IEngineClock and makes
// no IDs: the store does.
public sealed partial class EngineRuntime
{
    private const int MaxEffectDepth = 16;

    private readonly IRecordStore _records;
    private readonly IConfigurationStore _configuration;
    private readonly ConfigurationActivator _activator;
    private readonly ConfigurationValidator _validator;
    private readonly Func<string> _correlationIds;
    private long _calls;

    public EngineRuntime(
        IRecordStore records,
        IConfigurationStore configuration,
        PresetCatalog catalog,
        SnapshotCache? cache = null,
        IEngineClock? clock = null,
        Func<string>? correlationIds = null)
    {
        _records = records ?? throw new ArgumentNullException(nameof(records));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        if (catalog == null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        _activator = new ConfigurationActivator(catalog);
        _validator = new ConfigurationValidator(catalog);
        Cache = cache ?? new SnapshotCache();
        Clock = clock ?? SystemClock.Instance;
        _correlationIds = correlationIds ?? NextCorrelationId;
    }

    public SnapshotCache Cache { get; }

    public IEngineClock Clock { get; }

    // True (the default): a read call reads through a cache of its own (CallReadCache, document 25, B25-23), so it asks the
    // store about each row once and a composed read can load many rows in one store call (ComposedCall.Prefetch). The
    // answers are the same either way; false reads every row from the store each time it is asked for, which the tests
    // use to show that.
    public bool BatchReads { get; set; } = true;

    // ---- The call edge ----

    // Runs one call: probe and load the snapshot, resolve the caller, open the transaction, run the body, and commit
    // only when the body allowed a write. A failure rolls everything back and returns its outcome; a unique key taken by a
    // concurrent call is retried once, so the retry sees the first result (document 25, section 8).
    private RuntimeOutcome Run(CallerIdentity caller, SnapshotPurpose purpose, Func<RuntimeCall, CallAnswer> body, string? correlationId = null)
    {
        if (caller == null)
        {
            throw new ArgumentNullException(nameof(caller));
        }

        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return RunOnce(caller, purpose, body, correlationId);
            }
            catch (StoreConflictException) when (attempt == 0)
            {
            }
        }
    }

    // A caller that already has a correlation ID for the request (the MCP server, document 24, section 3.4) passes it, so
    // the envelope, the Audit Entry and telemetry carry the same value.
    private RuntimeOutcome RunOnce(CallerIdentity caller, SnapshotPurpose purpose, Func<RuntimeCall, CallAnswer> body, string? correlationId)
    {
        DateTime now = Now();
        SnapshotLoad load = Cache.Load(_configuration, purpose, now);
        if (load.Snapshot == null)
        {
            return new RuntimeOutcome(OperationOutcomes.Unavailable, new[] { load.Problem! }, null, null);
        }

        CompiledSnapshot snapshot = load.Snapshot;
        CallerResolution resolution = CallerResolver.Resolve(snapshot.Snapshot, caller, now);
        if (resolution.Actor == null)
        {
            return new RuntimeOutcome(OperationOutcomes.Refused, new[] { resolution.Refusal! }, snapshot.VersionNumber, snapshot.ContentHash);
        }

        // A read call never commits, so its reads may be kept for the call (CallReadCache).
        using IStoreTransaction tx = purpose == SnapshotPurpose.Read && BatchReads ? new CallReadCache(_records.Begin()) : _records.Begin();
        var call = new RuntimeCall(tx, snapshot, resolution.Actor, caller, now, string.IsNullOrEmpty(correlationId) ? _correlationIds() : correlationId!);
        try
        {
            CallAnswer answer = body(call);
            // A dry run (validateOnly, document 24, section 3.3) runs every rule and every write, then rolls back.
            if (purpose == SnapshotPurpose.Write && answer.Outcome == RuntimeOutcome.Allowed && !answer.IsReplay && !answer.DryRun && call.Writes.Count > 0)
            {
                tx.Commit();
                foreach (Action after in call.AfterCommit)
                {
                    after();
                }
            }

            return new RuntimeOutcome(
                answer.Outcome,
                call.Reasons,
                snapshot.VersionNumber,
                snapshot.ContentHash,
                answer.RowId,
                answer.Created,
                answer.IsReplay,
                answer.IsNoOp,
                answer.Outcome == RuntimeOutcome.Allowed && purpose == SnapshotPurpose.Write ? call.Writes : null,
                answer.Payload,
                call.Results,
                call.FactRounds);
        }
        catch (CallFailure failure)
        {
            var reasons = call.Reasons.Where(r => r.Severity != ReasonSeverity.Error).Concat(failure.FailureReasons).ToList();
            return new RuntimeOutcome(failure.Outcome, reasons, snapshot.VersionNumber, snapshot.ContentHash, results: call.Results, factRounds: call.FactRounds);
        }
    }

    private DateTime Now()
    {
        DateTime now = Clock.UtcNow;
        return now.Kind == DateTimeKind.Utc ? now : DateTime.SpecifyKind(now.ToUniversalTime(), DateTimeKind.Utc);
    }

    private string NextCorrelationId()
    {
        return "call-" + Interlocked.Increment(ref _calls).ToString("D6", CultureInfo.InvariantCulture);
    }

    // ---- Helpers shared by the flows ----

    // The C05 status the engine picks for a meaning: the default for it, or the only active one (as OP-08 does).
    private static string DefaultStatus(CompiledSnapshot snapshot, string entity, string meaning)
    {
        var rows = snapshot.Snapshot.TryGetEntity("status", out Core.Snapshots.SnapshotEntity? statuses)
            ? statuses.ActiveRows.Where(r => r.EngineMeaning == meaning && r.TryGetColumn("appliesToEntity", out JsonTreeNode? e) && e.Kind == JsonTreeKind.String && e.GetString() == entity).ToList()
            : new List<Core.Snapshots.SnapshotRow>();
        var defaults = rows.Where(r => r.TryGetColumn("isDefaultForMeaning", out JsonTreeNode? d) && d.Kind == JsonTreeKind.True).ToList();
        Core.Snapshots.SnapshotRow? pick = defaults.Count == 1 ? defaults[0] : rows.Count == 1 ? rows[0] : null;
        if (pick == null)
        {
            throw CallFailure.Of(
                OperationOutcomes.Unavailable,
                ReasonCodes.MeaningMissing,
                SnapshotReadName(entity) + " has " + (rows.Count == 0 ? "no active status" : "no single default status") + " for the meaning " + meaning + ", so the engine cannot set it.",
                "Status",
                null);
        }

        return pick.KeyPath;
    }

    private static string SnapshotReadName(string entityKey)
    {
        return string.Join(" ", entityKey.Split('-').Select(p => p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p.Substring(1)));
    }

    private static string? SettingText(CompiledSnapshot snapshot, string key)
    {
        return snapshot.Snapshot.TryGetRow("setting", key, out Core.Snapshots.SnapshotRow? row) && row.IsActive && row.TryGetColumn("value", out JsonTreeNode? value) && value.Kind == JsonTreeKind.String
            ? value.GetString()
            : null;
    }

    private static long? SettingNumber(CompiledSnapshot snapshot, string key)
    {
        return snapshot.Snapshot.TryGetRow("setting", key, out Core.Snapshots.SnapshotRow? row) && row.IsActive && row.TryGetColumn("value", out JsonTreeNode? value) && value.TryGetInt64(out long number)
            ? number
            : null;
    }

    // The directory object kinds the store knows (O05 Principal Reference), for G-12 and V35.
    private static List<KeyValuePair<string, PrincipalKind>> PrincipalKinds(IRecordReader reader)
    {
        var kinds = new List<KeyValuePair<string, PrincipalKind>>();
        foreach (StoredRow row in reader.Query(RowQuery.For("principal-reference")))
        {
            string? objectId = row.Text("entraObjectId");
            if (objectId != null && EngineContract.PrincipalKinds.TryParse(row.Text("principalKind"), out PrincipalKind kind))
            {
                kinds.Add(new KeyValuePair<string, PrincipalKind>(objectId, kind));
            }
        }

        return kinds;
    }

    private static EvaluationResult<JsonTreeNode?> Evaluate(RuntimeCall call, EngineOperation operation, CompiledSnapshot? snapshot, JsonTreeNode input)
    {
        EvaluationResult<JsonTreeNode?> result = OperationJson.Evaluate(operation, snapshot?.Snapshot, call.Context(snapshot, nested: true), input);
        call.Record(OperationCatalog.Get(operation).Id, result);
        return result;
    }

    private static bool IsAnswer(EngineOperation operation, string outcome)
    {
        return OperationCatalog.Get(operation).IsKnownOutcome(outcome);
    }

    // What a call body returns when it does not fail.
    private sealed class CallAnswer
    {
        public CallAnswer(string outcome, string? rowId = null, bool created = false, JsonTreeNode? payload = null, bool isReplay = false, bool isNoOp = false, bool dryRun = false)
        {
            Outcome = outcome;
            RowId = rowId;
            Created = created;
            Payload = payload;
            IsReplay = isReplay;
            IsNoOp = isNoOp;
            DryRun = dryRun;
        }

        public string Outcome { get; }

        public string? RowId { get; }

        public bool Created { get; }

        public JsonTreeNode? Payload { get; }

        public bool IsReplay { get; }

        public bool IsNoOp { get; }

        // True when nothing may commit: the writes ran only to judge them.
        public bool DryRun { get; }
    }
}
