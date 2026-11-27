using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Product.Engine.Configuration.Composition;
using Product.Engine.Configuration.Json;
using Product.Engine.Configuration.Presets;
using Product.Engine.Configuration.Schema;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Configuration.Validation;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Operations;

namespace Product.Engine.Testing;

// The replay tool of document 25, section 9: "Each result carries configurationVersion, snapshotHash and engineVersion.
// An auditor can load that version and the same facts and get the same answer. A replay tool in the testing assembly
// does exactly that."
//
// A replay record (config/schema/replay-record.schema.json) holds the snapshot version, the context, the request (what the
// caller sent and the facts the adapter fetched, as the operation's wire input) and the recorded result; for OP-08 the
// recorded payload is the write plan. The tool loads the version (the record's own, or a bundle the caller names, such as
// a C02 Content Json export), evaluates the request again through OperationJson, the one wire path every adapter uses,
// and reports every difference: the version and its hash, the engine version, the outcome, the payload (by JSON pointer),
// the reasons and the rule trace. It reads files only, holds no rule, and needs no test framework, so the engine command
// line runs it (replay and capture).
public sealed class ReplayTool
{
    // Folders with this name hold replay records. The fixture runner skips them.
    public const string FolderName = "replay";

    public static readonly IReadOnlyList<string> DifferenceKinds = new ReadOnlyCollection<string>(new[]
    {
        "snapshot-hash", "configuration-version", "engine-version", "outcome", "payload", "reasons", "trace",
    });

    // Differences beyond this many per kind are counted, not listed.
    private const int MaxListed = 25;

    private static readonly Dictionary<string, EngineOperation> Operations = new(StringComparer.Ordinal)
    {
        { "OP-01", EngineOperation.EvaluateTier },
        { "OP-02", EngineOperation.ResolveRequiredApprovals },
        { "OP-03", EngineOperation.EvaluateApprovalStatus },
        { "OP-04", EngineOperation.EvaluateGateDecision },
        { "OP-05", EngineOperation.CheckChangeWindow },
        { "OP-06", EngineOperation.EvaluateDocumentationCompleteness },
        { "OP-07", EngineOperation.EvaluateGateReadiness },
        { "OP-08", EngineOperation.GuardWrite },
        { "OP-10", EngineOperation.WalkImpact },
        { "OP-11", EngineOperation.ComputeDueDates },
        { "OP-12", EngineOperation.EvaluateStandardChange },
        { "OP-13", EngineOperation.CompareGapCheckResult },
        { "OP-14", EngineOperation.EvaluateLinkedAuthorization },
        { "OP-15", EngineOperation.ResolveDataDomain },
        { "OP-16", EngineOperation.EvaluateDataUse },
        { "OP-17", EngineOperation.RunDataChecks },
    };

    private readonly ConfigurationComposer _composer;

    public ReplayTool(PresetCatalog catalog)
    {
        _composer = new ConfigurationComposer(catalog ?? throw new ArgumentNullException(nameof(catalog)));
    }

    // A tool over the shipped presets and the fixture-only presets of a src/ folder.
    public static ReplayTool ForSourceRoot(string root)
    {
        return new ReplayTool(PresetCatalog.Load(new[] { SourceRoot.PresetsFolder(root), SourceRoot.FixturePresetsFolder(root) }));
    }

    // Every *.json under the folder, in ordinal path order.
    public IReadOnlyList<ReplayResult> ReplayFolder(string folder, ReplayOptions? options = null)
    {
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException("No replay folder at " + folder + ".");
        }

        string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories)
            .Select(f => new KeyValuePair<string, string>(f, Path.GetFullPath(f).Substring(root.Length).Replace('\\', '/')))
            .OrderBy(p => p.Value, StringComparer.Ordinal)
            .Select(p => ReplayFile(p.Key, p.Value, options))
            .ToList();
    }

    public ReplayResult ReplayFile(string path, string? displayName = null, ReplayOptions? options = null)
    {
        string name = displayName ?? path;
        if (!JsonText.TryReadFile(path, out JsonTreeNode? record, out string? error))
        {
            return ReplayResult.Invalid(name, null, null, "the file " + error);
        }

        string? caseName = JsonNodes.GetString(record!, "case");
        if (caseName != null && !CaseMatchesFile(path, caseName))
        {
            return ReplayResult.Invalid(name, JsonNodes.GetString(record!, "operation"), caseName, "the case \"" + caseName + "\" does not match the file name \"" + Path.GetFileName(path) + "\"");
        }

        return Replay(record!, name, options);
    }

    // Replays one record. A record that does not match the schema, or has no recorded result, is invalid.
    public ReplayResult Replay(JsonTreeNode record, string name, ReplayOptions? options = null)
    {
        ReplayOptions given = options ?? new ReplayOptions();
        string? operation = JsonNodes.GetString(record, "operation");
        string? caseName = JsonNodes.GetString(record, "case");
        string? problem = CheckShape(record);
        if (problem != null)
        {
            return ReplayResult.Invalid(name, operation, caseName, problem);
        }

        JsonTreeNode? recorded = JsonNodes.Get(record, "recorded");
        if (recorded == null)
        {
            return ReplayResult.Invalid(name, operation, caseName, "the record has no recorded result to compare with; run capture to make one");
        }

        Evaluated replayed;
        try
        {
            replayed = Evaluate(record, caseName!, given);
        }
        catch (ReplayRecordException e)
        {
            return ReplayResult.Invalid(name, operation, caseName, e.Message);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return ReplayResult.Invalid(name, operation, caseName, "the replay threw " + e.GetType().Name + ": " + e.Message);
        }

        List<ReplayDifference> differences = Compare(recorded, replayed.Result, given.IgnoreMessages);
        string[]? expected = JsonNodes.Get(record, "expectedDifferences") == null ? null : FixtureRunner.StringList(JsonNodes.Get(record, "expectedDifferences"));
        return new ReplayResult(name, operation, caseName, differences, expected, Recorded(replayed.Result), replayed.Source);
    }

    // Evaluates the request of a record and returns the record with its recorded result: a test vector the replay can
    // later check. Any recorded result or expected differences the record held are replaced.
    public JsonTreeNode Capture(JsonTreeNode record, ReplayOptions? options = null)
    {
        string? problem = CheckShape(record);
        if (problem != null)
        {
            throw new ReplayRecordException(problem);
        }

        Evaluated evaluated = Evaluate(record, JsonNodes.GetString(record, "case")!, options ?? new ReplayOptions());
        JsonTreeNode kept = JsonNodes.Without(JsonNodes.Without(record, "recorded"), "expectedDifferences");
        return JsonNodes.With(kept, "recorded", Recorded(evaluated.Result));
    }

    // The recorded block of a result, as an adapter would store it next to the request (document 25, section 5.1).
    public static JsonTreeNode Recorded(EvaluationResult<JsonTreeNode?> result)
    {
        if (result == null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        return JsonTreeNode.CreateObject(
            JsonNodes.Member("outcome", result.Outcome),
            JsonNodes.Member("configurationVersion", JsonTreeNode.FromInt64(result.ConfigurationVersion)),
            JsonNodes.Member("snapshotHash", result.SnapshotHash),
            JsonNodes.Member("engineVersion", result.EngineVersion),
            JsonNodes.Member("payload", result.Payload ?? JsonTreeNode.Null),
            JsonNodes.Member("reasons", JsonTreeNode.CreateArray(result.Reasons.Select(r => ReasonJson(r.Code, ValidationIssue.SeverityWire(r.Severity), r.Message, r.Entity, r.Ref)))),
            JsonNodes.Member("trace", JsonTreeNode.CreateArray(result.Trace.Select(t => TraceJson(t.Entity, t.Key, EngineContract.RuleTraceRoles.ToWire(t.Role))))));
    }

    // ---- Evaluation ----

    private Evaluated Evaluate(JsonTreeNode record, string caseName, ReplayOptions options)
    {
        string operation = JsonNodes.GetString(record, "operation")!;
        if (!Operations.TryGetValue(operation, out EngineOperation evaluation) || !OperationJson.IsBuilt(evaluation))
        {
            throw new ReplayRecordException(operation + " cannot be replayed: it is not built in this engine.");
        }

        JsonTreeNode snapshot = JsonNodes.Get(record, "snapshot")!;
        string source = Describe(snapshot);
        if (options.SnapshotBundle != null)
        {
            snapshot = JsonTreeNode.CreateObject(
                JsonNodes.Member("bundle", options.SnapshotBundle),
                JsonNodes.Member("versionNumber", JsonTreeNode.FromInt64(options.VersionNumber ?? FixtureRunner.TryGetInt(snapshot, "versionNumber") ?? 1)));
            source = options.SnapshotName ?? "the bundle given";
        }

        int versionNumber = FixtureRunner.TryGetInt(snapshot, "versionNumber") ?? 1;
        JsonTreeNode request = JsonNodes.Get(record, "request")!;
        JsonTreeNode contextNode = JsonNodes.Get(record, "context")!;
        CompiledSnapshot? compiled = null;
        string snapshotHash = CanonicalHash.ComputeContentHash(JsonTreeNode.Null);
        if (JsonNodes.Get(snapshot, "none") == null)
        {
            ContentSource contentSource = FixtureRunner.ToSource(snapshot, caseName, "snapshot")
                ?? throw new ReplayRecordException("The record names no snapshot version: a preset, a bundle or none.");
            ComposedConfiguration composed = _composer.Compose(contentSource, ValidationMode.Draft, new ValidationIssues());
            if (!composed.IsComplete)
            {
                throw new ReplayRecordException("The snapshot version (" + source + ") does not compose, so it cannot be loaded.");
            }

            try
            {
                compiled = SnapshotCompiler.Compile(composed.Bundle, versionNumber);
                snapshotHash = compiled.ContentHash;
            }
            catch (SnapshotCompileException e)
            {
                // The engine refuses a snapshot it cannot read (section 6.3), so the replayed answer is that refusal.
                EvaluationContext refusedContext = FixtureRunner.BuildContext(contextNode, snapshot, snapshotHash, caseName);
                var refused = EvaluationResult.Create<JsonTreeNode?>(refusedContext, OperationOutcomes.Unavailable, null, new[] { new Reason(e.Code, ReasonSeverity.Error, e.Message) });
                return new Evaluated(refused, source);
            }
        }

        EvaluationContext context = FixtureRunner.BuildContext(contextNode, snapshot, snapshotHash, caseName);
        return new Evaluated(OperationJson.Evaluate(evaluation, compiled?.Snapshot, context, request), source);
    }

    private static string Describe(JsonTreeNode snapshot)
    {
        if (JsonNodes.Get(snapshot, "none") != null)
        {
            return "no snapshot";
        }

        string? preset = JsonNodes.GetString(snapshot, "presetKey");
        string text = preset == null ? "an inline bundle" : "preset " + preset + string.Concat(FixtureRunner.StringList(JsonNodes.Get(snapshot, "overlays")).Select(o => "+" + o));
        if (JsonNodes.Get(snapshot, "operations") != null)
        {
            text += " with inline operations";
        }

        return text + ", version " + (FixtureRunner.TryGetInt(snapshot, "versionNumber") ?? 1);
    }

    private static string? CheckShape(JsonTreeNode record)
    {
        SchemaResult shape = ReplaySchemas.Set.Evaluate(record, ReplaySchemas.RecordId);
        return shape.IsValid ? null : "replay record schema: " + string.Join("; ", shape.Issues.Take(5).Select(i => i.ToString()));
    }

    // <case>.json, or a name that ends with -<case>.json.
    private static bool CaseMatchesFile(string path, string caseName)
    {
        string stem = Path.GetFileNameWithoutExtension(path);
        return string.Equals(stem, caseName, StringComparison.Ordinal) || stem.EndsWith("-" + caseName, StringComparison.Ordinal);
    }

    // ---- Comparison ----

    private static List<ReplayDifference> Compare(JsonTreeNode recorded, EvaluationResult<JsonTreeNode?> replayed, bool ignoreMessages)
    {
        var differences = new List<ReplayDifference>();
        string? recordedHash = JsonNodes.GetString(recorded, "snapshotHash");
        if (!string.Equals(recordedHash, replayed.SnapshotHash, StringComparison.Ordinal))
        {
            differences.Add(new ReplayDifference("snapshot-hash", null, recordedHash ?? "(none)", replayed.SnapshotHash, "the loaded version is not the one the result was made with"));
        }

        int? recordedVersion = FixtureRunner.TryGetInt(recorded, "configurationVersion");
        if (recordedVersion != replayed.ConfigurationVersion)
        {
            differences.Add(new ReplayDifference("configuration-version", null, recordedVersion?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "(none)", replayed.ConfigurationVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), "the loaded version number differs"));
        }

        string? recordedEngine = JsonNodes.GetString(recorded, "engineVersion");
        if (!string.Equals(recordedEngine, replayed.EngineVersion, StringComparison.Ordinal))
        {
            differences.Add(new ReplayDifference("engine-version", null, recordedEngine ?? "(none)", replayed.EngineVersion, "another engine build; a change in rule behavior is a new engine version, so other differences may come from it"));
        }

        string? recordedOutcome = JsonNodes.GetString(recorded, "outcome");
        if (!string.Equals(recordedOutcome, replayed.Outcome, StringComparison.Ordinal))
        {
            differences.Add(new ReplayDifference("outcome", null, recordedOutcome ?? "(none)", replayed.Outcome, null));
        }

        var payloadDifferences = new List<ReplayDifference>();
        DiffJson(JsonNodes.Get(recorded, "payload") ?? JsonTreeNode.Null, replayed.Payload ?? JsonTreeNode.Null, string.Empty, payloadDifferences);
        AddListed(differences, payloadDifferences);

        AddListed(differences, CompareReasons(JsonNodes.Get(recorded, "reasons"), replayed.Reasons, ignoreMessages));
        AddListed(differences, CompareTrace(JsonNodes.Get(recorded, "trace"), replayed.Trace));
        return differences;
    }

    private static void AddListed(List<ReplayDifference> differences, List<ReplayDifference> found)
    {
        differences.AddRange(found.Take(MaxListed));
        if (found.Count > MaxListed)
        {
            differences.Add(new ReplayDifference(found[0].Kind, null, string.Empty, string.Empty, "and " + (found.Count - MaxListed) + " more " + found[0].Kind + " difference(s)"));
        }
    }

    // A structural comparison: every member and item, by JSON pointer.
    private static void DiffJson(JsonTreeNode recorded, JsonTreeNode replayed, string path, List<ReplayDifference> differences)
    {
        if (recorded.Kind == JsonTreeKind.Object && replayed.Kind == JsonTreeKind.Object)
        {
            foreach (string name in recorded.Members.Select(m => m.Key).Union(replayed.Members.Select(m => m.Key), StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal))
            {
                string here = JsonPointer.Append(path, name);
                bool inRecorded = recorded.TryGetMember(name, out JsonTreeNode? a);
                bool inReplayed = replayed.TryGetMember(name, out JsonTreeNode? b);
                if (inRecorded && inReplayed)
                {
                    DiffJson(a!, b!, here, differences);
                }
                else
                {
                    differences.Add(new ReplayDifference("payload", JsonPointer.Display(here), inRecorded ? JsonPointer.Show(a!) : "(absent)", inReplayed ? JsonPointer.Show(b!) : "(absent)", null));
                }
            }

            return;
        }

        if (recorded.Kind == JsonTreeKind.Array && replayed.Kind == JsonTreeKind.Array)
        {
            int shared = Math.Min(recorded.Items.Count, replayed.Items.Count);
            for (int i = 0; i < shared; i++)
            {
                DiffJson(recorded.Items[i], replayed.Items[i], JsonPointer.Append(path, i), differences);
            }

            for (int i = shared; i < Math.Max(recorded.Items.Count, replayed.Items.Count); i++)
            {
                string here = JsonPointer.Display(JsonPointer.Append(path, i));
                differences.Add(i < recorded.Items.Count
                    ? new ReplayDifference("payload", here, JsonPointer.Show(recorded.Items[i]), "(absent)", null)
                    : new ReplayDifference("payload", here, "(absent)", JsonPointer.Show(replayed.Items[i]), null));
            }

            return;
        }

        if (!recorded.Equals(replayed))
        {
            differences.Add(new ReplayDifference("payload", JsonPointer.Display(path), JsonPointer.Show(recorded), JsonPointer.Show(replayed), null));
        }
    }

    // Reasons are a set in the canonical order (errors first, then code, entity, reference): each recorded reason must be
    // replayed and each replayed reason must be recorded. With ignoreMessages, the plain sentences are not compared
    // (labels may be translated later; codes never change, section 7).
    private static List<ReplayDifference> CompareReasons(JsonTreeNode? recordedReasons, IReadOnlyList<Reason> replayed, bool ignoreMessages)
    {
        var differences = new List<ReplayDifference>();
        List<string> left = (recordedReasons?.Items ?? Array.Empty<JsonTreeNode>())
            .Select(r => ReasonText(JsonNodes.GetString(r, "code"), JsonNodes.GetString(r, "severity"), ignoreMessages ? null : JsonNodes.GetString(r, "message"), JsonNodes.GetString(r, "entity"), JsonNodes.GetString(r, "ref")))
            .ToList();
        List<string> right = replayed
            .Select(r => ReasonText(r.Code, ValidationIssue.SeverityWire(r.Severity), ignoreMessages ? null : r.Message, r.Entity, r.Ref))
            .ToList();
        foreach (string reason in right.ToList())
        {
            if (left.Remove(reason))
            {
                right.Remove(reason);
            }
        }

        differences.AddRange(left.Select(r => new ReplayDifference("reasons", null, r, "(absent)", null)));
        differences.AddRange(right.Select(r => new ReplayDifference("reasons", null, "(absent)", r, null)));
        return differences;
    }

    // The trace keeps the order the operation built, which is stable (section 9), so it is compared row by row.
    private static List<ReplayDifference> CompareTrace(JsonTreeNode? recordedTrace, IReadOnlyList<RuleTrace> replayed)
    {
        var differences = new List<ReplayDifference>();
        IReadOnlyList<JsonTreeNode> left = recordedTrace?.Items ?? Array.Empty<JsonTreeNode>();
        int count = Math.Max(left.Count, replayed.Count);
        for (int i = 0; i < count; i++)
        {
            string? a = i < left.Count ? TraceText(JsonNodes.GetString(left[i], "entity"), JsonNodes.GetString(left[i], "key"), JsonNodes.GetString(left[i], "role")) : null;
            string? b = i < replayed.Count ? TraceText(replayed[i].Entity, replayed[i].Key, EngineContract.RuleTraceRoles.ToWire(replayed[i].Role)) : null;
            if (!string.Equals(a, b, StringComparison.Ordinal))
            {
                differences.Add(new ReplayDifference("trace", "/" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), a ?? "(absent)", b ?? "(absent)", null));
            }
        }

        return differences;
    }

    private static string ReasonText(string? code, string? severity, string? message, string? entity, string? reference)
    {
        return (severity ?? "?") + " " + (code ?? "?") + (entity == null ? string.Empty : " on " + entity) + (reference == null ? string.Empty : " " + reference) + (message == null ? string.Empty : ": " + message);
    }

    private static string TraceText(string? entity, string? key, string? role)
    {
        return (entity ?? "?") + " " + (key ?? "?") + " " + (role ?? "?");
    }

    private static JsonTreeNode ReasonJson(string code, string severity, string message, string? entity, string? reference)
    {
        var members = new List<KeyValuePair<string, JsonTreeNode>>
        {
            JsonNodes.Member("code", code),
            JsonNodes.Member("severity", severity),
            JsonNodes.Member("message", message),
        };
        if (entity != null)
        {
            members.Add(JsonNodes.Member("entity", entity));
        }

        if (reference != null)
        {
            members.Add(JsonNodes.Member("ref", reference));
        }

        return JsonTreeNode.CreateObject(members);
    }

    private static JsonTreeNode TraceJson(string entity, string key, string role)
    {
        return JsonTreeNode.CreateObject(JsonNodes.Member("entity", entity), JsonNodes.Member("key", key), JsonNodes.Member("role", role));
    }

    private sealed class Evaluated
    {
        public Evaluated(EvaluationResult<JsonTreeNode?> result, string source)
        {
            Result = result;
            Source = source;
        }

        public EvaluationResult<JsonTreeNode?> Result { get; }

        public string Source { get; }
    }
}

// What the caller of a replay may change: the snapshot version to load instead of the record's own, and whether the
// reason messages are compared.
public sealed class ReplayOptions
{
    // A whole bundle to load as the snapshot version, such as a C02 Content Json export or a built preset bundle.
    public JsonTreeNode? SnapshotBundle { get; set; }

    // How reports name that bundle, such as its file name.
    public string? SnapshotName { get; set; }

    // The version number to stamp on that bundle. Default: the record's, else 1.
    public int? VersionNumber { get; set; }

    // Compare reason codes, severities, entities and references, but not the plain sentences.
    public bool IgnoreMessages { get; set; }
}

// One difference between the recorded and the replayed result.
public sealed class ReplayDifference
{
    public ReplayDifference(string kind, string? path, string recorded, string replayed, string? note)
    {
        Kind = kind;
        Path = path;
        Recorded = recorded;
        Replayed = replayed;
        Note = note;
    }

    // One of ReplayTool.DifferenceKinds.
    public string Kind { get; }

    // A JSON pointer into the payload, or a trace row index. Null for the other kinds.
    public string? Path { get; }

    public string Recorded { get; }

    public string Replayed { get; }

    public string? Note { get; }

    public JsonTreeNode ToJson()
    {
        var members = new List<KeyValuePair<string, JsonTreeNode>>
        {
            JsonNodes.Member("kind", Kind),
            JsonNodes.Member("recorded", Recorded),
            JsonNodes.Member("replayed", Replayed),
        };
        if (Path != null)
        {
            members.Add(JsonNodes.Member("path", Path));
        }

        if (Note != null)
        {
            members.Add(JsonNodes.Member("note", Note));
        }

        return JsonTreeNode.CreateObject(members);
    }

    public override string ToString()
    {
        if (Recorded.Length == 0 && Replayed.Length == 0)
        {
            return Kind + ": " + Note;
        }

        return Kind + (Path == null ? string.Empty : " " + Path) + ": recorded " + Recorded + ", replayed " + Replayed + (Note == null ? string.Empty : " (" + Note + ")");
    }
}

public enum ReplayStatus
{
    Same,
    Different,
    Invalid,
}

// The replay of one record.
public sealed class ReplayResult
{
    public ReplayResult(string file, string? operation, string? caseName, IEnumerable<ReplayDifference> differences, string[]? expectedDifferences, JsonTreeNode? replayed, string? snapshotSource, IEnumerable<string>? problems = null)
    {
        File = file;
        Operation = operation;
        Case = caseName;
        Differences = new ReadOnlyCollection<ReplayDifference>(differences.ToList());
        ExpectedDifferences = expectedDifferences;
        Replayed = replayed;
        SnapshotSource = snapshotSource;
        Problems = new ReadOnlyCollection<string>((problems ?? Enumerable.Empty<string>()).ToList());
        Status = Problems.Count > 0 ? ReplayStatus.Invalid : Differences.Count == 0 ? ReplayStatus.Same : ReplayStatus.Different;
    }

    public string File { get; }

    public string? Operation { get; }

    public string? Case { get; }

    public ReplayStatus Status { get; }

    public IReadOnlyList<ReplayDifference> Differences { get; }

    // The difference kinds a fixture says the replay must report. Null for a record without expectedDifferences.
    public string[]? ExpectedDifferences { get; }

    // The replayed result in the recorded form. Null when the record is invalid.
    public JsonTreeNode? Replayed { get; }

    // Which snapshot version was loaded, in words.
    public string? SnapshotSource { get; }

    // Why the record could not be replayed.
    public IReadOnlyList<string> Problems { get; }

    // The kinds of the differences found, each once, in the order of ReplayTool.DifferenceKinds.
    public string[] DifferenceKinds => ReplayTool.DifferenceKinds.Where(k => Differences.Any(d => d.Kind == k)).ToArray();

    // A record without expectations passes with no difference; a fixture passes when it finds exactly the kinds it expects.
    public bool Passed
    {
        get
        {
            if (Status == ReplayStatus.Invalid)
            {
                return false;
            }

            string[] expected = (ExpectedDifferences ?? Array.Empty<string>()).OrderBy(k => k, StringComparer.Ordinal).ToArray();
            return expected.SequenceEqual(DifferenceKinds.OrderBy(k => k, StringComparer.Ordinal), StringComparer.Ordinal);
        }
    }

    public static ReplayResult Invalid(string file, string? operation, string? caseName, string problem)
    {
        return new ReplayResult(file, operation, caseName, Array.Empty<ReplayDifference>(), null, null, null, new[] { problem });
    }

    public JsonTreeNode ToJson()
    {
        var members = new List<KeyValuePair<string, JsonTreeNode>>
        {
            JsonNodes.Member("file", File),
            JsonNodes.Member("status", Status == ReplayStatus.Same ? "same" : Status == ReplayStatus.Different ? "different" : "invalid"),
            JsonNodes.Member("passed", JsonTreeNode.FromBoolean(Passed)),
            JsonNodes.Member("differences", JsonTreeNode.CreateArray(Differences.Select(d => d.ToJson()))),
            JsonNodes.Member("problems", JsonTreeNode.CreateArray(Problems.Select(JsonTreeNode.FromString))),
        };
        if (Operation != null)
        {
            members.Add(JsonNodes.Member("operation", Operation));
        }

        if (Case != null)
        {
            members.Add(JsonNodes.Member("case", Case));
        }

        if (ExpectedDifferences != null)
        {
            members.Add(JsonNodes.Member("expectedDifferences", JsonTreeNode.CreateArray(ExpectedDifferences.Select(JsonTreeNode.FromString))));
        }

        if (SnapshotSource != null)
        {
            members.Add(JsonNodes.Member("snapshot", SnapshotSource));
        }

        if (Replayed != null)
        {
            members.Add(JsonNodes.Member("replayed", Replayed));
        }

        return JsonTreeNode.CreateObject(members);
    }
}

// A record the replay cannot use: not built, no snapshot, or a snapshot that does not compose.
public sealed class ReplayRecordException : Exception
{
    public ReplayRecordException(string message)
        : base(message)
    {
    }
}

// The replay record schema (config/schema/replay-record.schema.json) with the fixture and configuration schemas it refers to.
public static class ReplaySchemas
{
    public const string RecordId = "replay-record.schema.json";

    private static readonly Lazy<JsonSchemaSet> LazySet = new(() => JsonSchemaSet.Create(
        ConfigurationSchemas.Documents.Concat(new[]
        {
            ConfigurationSchemas.LoadEmbedded(typeof(ReplaySchemas).Assembly, FixtureSchemas.FixtureId),
            ConfigurationSchemas.LoadEmbedded(typeof(ReplaySchemas).Assembly, RecordId),
        })));

    public static JsonSchemaSet Set => LazySet.Value;
}
