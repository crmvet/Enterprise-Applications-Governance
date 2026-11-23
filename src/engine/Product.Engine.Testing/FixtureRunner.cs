using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Product.Engine.Configuration.Activation;
using Product.Engine.Configuration.Composition;
using Product.Engine.Configuration.Drafts;
using Product.Engine.Configuration.Json;
using Product.Engine.Configuration.Patches;
using Product.Engine.Configuration.Presets;
using Product.Engine.Configuration.Schema;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Configuration.Validation;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Operations;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Testing;

// Runs fixture files (document 25, section 10). Needs no test framework, so the engine
// command line runs the same fixtures as the test project, on any machine.
//
// Each fixture is checked against fixture.schema.json first. Then its operation runs and the result is compared
// with "expected": the outcome word, the payload (subset or exact), the reasons (exact or includes) and the rule
// trace (includes or exact). OP-09, rebase, activate and bootstrap run through the configuration assembly; OP-01 to OP-08
// and OP-10 to OP-14 run through the core's OperationJson, the same wire path an adapter uses. An operation that is not
// built is skipped.
public sealed class FixtureRunner
{
    // The outcome words that mean a case breaks something, and the words that mean it passes, per operation.
    // A failing case must report every rule in its ruleIds as an error; a passing case must report none of them.
    private static readonly Dictionary<string, KeyValuePair<string[], string[]>> CaseOutcomes = new(StringComparer.Ordinal)
    {
        { "OP-09", new KeyValuePair<string[], string[]>(new[] { "invalid" }, new[] { "valid" }) },
        { "rebase", new KeyValuePair<string[], string[]>(new[] { RebaseOutcome.Conflict, RebaseOutcome.Invalid }, new[] { RebaseOutcome.SameBase, RebaseOutcome.Rebased }) },
        { "activate", new KeyValuePair<string[], string[]>(new[] { ActivationOutcome.Discarded }, new[] { ActivationOutcome.Activated }) },
        { "bootstrap", new KeyValuePair<string[], string[]>(new[] { ActivationOutcome.Invalid }, new[] { ActivationOutcome.Bootstrapped }) },

        // OP-08: a refused case must report a refusal of every guard rule it lists (the rule comes from the reason
        // catalog); an allowed case must report none of them.
        { "OP-08", new KeyValuePair<string[], string[]>(new[] { "refused" }, new[] { "allowed" }) },
    };

    // The evaluation operations this runner runs through OperationJson, by fixture operation name.
    private static readonly Dictionary<string, EngineOperation> EvaluationOperations = new(StringComparer.Ordinal)
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
    };

    private readonly ConfigurationValidator _validator;
    private readonly DraftRebaser _rebaser;
    private readonly ConfigurationComposer _composer;
    private readonly ConfigurationActivator _activator;

    public FixtureRunner(PresetCatalog catalog)
    {
        _validator = new ConfigurationValidator(catalog ?? throw new ArgumentNullException(nameof(catalog)));
        _rebaser = new DraftRebaser(catalog);
        _composer = new ConfigurationComposer(catalog);
        _activator = new ConfigurationActivator(catalog);
    }

    // A runner over the shipped presets and the fixture-only presets of a src/ folder.
    public static FixtureRunner ForSourceRoot(string root)
    {
        return new FixtureRunner(PresetCatalog.Load(new[] { SourceRoot.PresetsFolder(root), SourceRoot.FixturePresetsFolder(root) }));
    }

    // Every *.json under the folder, in ordinal path order. Folders named "presets" hold preset files, and folders named
    // "replay" hold replay records (ReplayTool, document 25, section 9), not fixtures.
    public IReadOnlyList<FixtureResult> RunFolder(string folder)
    {
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException("No fixture folder at " + folder + ".");
        }

        var results = new List<FixtureResult>();
        foreach (string file in FindFixtures(folder))
        {
            if (IsConfigurationFile(file))
            {
                continue;
            }

            results.Add(RunFile(file, Relative(folder, file)));
        }

        return results;
    }

    // A bundle, patch or preset file next to fixtures (as in config/schema/examples) is not a fixture.
    private static bool IsConfigurationFile(string file)
    {
        if (!JsonText.TryReadFile(file, out JsonTreeNode? node, out _) || node!.Kind != JsonTreeKind.Object)
        {
            return false;
        }

        return !JsonNodes.HasMember(node, "operation")
            && (JsonNodes.HasMember(node, "manifest") || JsonNodes.HasMember(node, "entity") || JsonNodes.HasMember(node, "presetKey"));
    }

    // fixtures/<operation>/<case>.json, or the example form <operation>-<case>.fixture.json.
    private static bool CaseMatchesFile(string path, string caseName)
    {
        string name = Path.GetFileName(path);
        string stem = name.EndsWith(".fixture.json", StringComparison.Ordinal)
            ? name.Substring(0, name.Length - ".fixture.json".Length)
            : Path.GetFileNameWithoutExtension(path);
        return string.Equals(stem, caseName, StringComparison.Ordinal) || stem.EndsWith("-" + caseName, StringComparison.Ordinal);
    }

    public FixtureResult RunFile(string path, string? displayName = null)
    {
        string name = displayName ?? path;
        if (!JsonText.TryReadFile(path, out JsonTreeNode? fixture, out string? error))
        {
            return new FixtureResult(name, null, null, FixtureStatus.Failed, new[] { "the file " + error });
        }

        string? operation = JsonNodes.GetString(fixture!, "operation");
        string? caseName = JsonNodes.GetString(fixture!, "case");
        SchemaResult shape = FixtureSchemas.Set.Evaluate(fixture!, FixtureSchemas.FixtureId);
        if (!shape.IsValid)
        {
            return new FixtureResult(name, operation, caseName, FixtureStatus.Failed, shape.Issues.Take(10).Select(i => "fixture schema: " + i));
        }

        if (!CaseMatchesFile(path, caseName!))
        {
            return new FixtureResult(name, operation, caseName, FixtureStatus.Failed, new[] { "the case \"" + caseName + "\" does not match the file name \"" + Path.GetFileName(path) + "\"" });
        }

        try
        {
            switch (operation)
            {
                case "OP-09":
                    return RunValidate(name, fixture!, caseName!);
                case "rebase":
                    return RunRebase(name, fixture!, caseName!);
                case "activate":
                    return RunActivate(name, fixture!, caseName!);
                case "bootstrap":
                    return RunBootstrap(name, fixture!, caseName!);
                case string op when EvaluationOperations.TryGetValue(op, out EngineOperation evaluation) && OperationJson.IsBuilt(evaluation):
                    return RunEvaluation(name, fixture!, caseName!, op, evaluation);
                default:
                    return new FixtureResult(name, operation, caseName, FixtureStatus.Skipped, new[] { operation + " is not built yet" });
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return new FixtureResult(name, operation, caseName, FixtureStatus.Failed, new[] { "the operation threw " + e.GetType().Name + ": " + e.Message });
        }
    }

    // OP-09: validate the fixture's snapshot and compare the ValidationReport with "expected".
    private FixtureResult RunValidate(string name, JsonTreeNode fixture, string caseName)
    {
        JsonTreeNode snapshot = JsonNodes.Get(fixture, "snapshot")!;
        JsonTreeNode input = JsonNodes.Get(fixture, "input") ?? JsonNodes.EmptyObject;
        JsonTreeNode? facts = JsonNodes.Get(fixture, "facts");

        ValidationMode mode = ParseMode(JsonNodes.GetString(input, "mode"));
        var actualReasons = new List<ActualReason>();
        string outcomeWord;
        JsonTreeNode payload;
        string snapshotHash;

        JsonTreeNode? activeNode = JsonNodes.Get(input, "activeSnapshot");
        ContentSource? active = activeNode == null ? null : ToSource(activeNode, caseName, "input.activeSnapshot");
        ContentSource? candidate = ToSource(snapshot, caseName, "snapshot");

        // A raw patch document is a draft: OP-09 checks the active snapshot with the patch applied.
        // This is how a fixture states a broken patch that the fixture schema would refuse as inline operations.
        JsonTreeNode? raw = JsonNodes.Get(snapshot, "raw");
        if (raw != null && IsPatchDocument(raw))
        {
            candidate = active?.WithPatch(ReadRawPatch(raw, caseName + " snapshot.raw"));
        }

        if (candidate == null || (activeNode != null && active == null))
        {
            outcomeWord = "invalid-input";
            ReasonCodeInfo info = ReasonCatalog.Get(ReasonCodes.InputRequired);
            actualReasons.Add(new ActualReason(info.Code, info.DefaultSeverity, null, null, null, "OP-09 needs a configuration to check: a preset, a bundle, a raw document, or a raw patch with input.activeSnapshot as its base."));
            payload = JsonTreeNode.CreateObject(JsonNodes.Member("isValid", JsonTreeNode.False));
            snapshotHash = CanonicalHash.ComputeContentHash(JsonTreeNode.Null);
        }
        else
        {
            int versionNumber = TryGetInt(snapshot, "versionNumber") ?? 1;
            var request = new ValidationRequest(candidate, mode, active, RowsInUse(facts), versionNumber: versionNumber, facts: ValidationFactsOf(fixture, input, activeNode, facts));
            ValidationOutcome outcome = _validator.Validate(request);
            outcomeWord = outcome.Outcome;
            payload = outcome.Report.ToJson();
            snapshotHash = outcome.Report.ContentHash;
            foreach (ValidationIssue issue in outcome.Report.Issues)
            {
                actualReasons.Add(new ActualReason(issue.Code, issue.Severity, issue.Entity, issue.Ref, issue.RuleId, issue.Message));
            }

            if (outcome.InputProblem != null)
            {
                ValidationIssue problem = outcome.InputProblem;
                actualReasons.Add(new ActualReason(problem.Code, problem.Severity, problem.Entity, problem.Ref, problem.RuleId, problem.Message));
            }
        }

        return Judge(name, "OP-09", fixture, snapshot, caseName, outcomeWord, payload, snapshotHash, actualReasons);
    }

    // Rebase (document 21, section 4.5): "snapshot" is the active version, input.baseSnapshot the version the draft
    // started from, input.draft the draft patch. The facts are the OP-09 facts the revalidation needs.
    private FixtureResult RunRebase(string name, JsonTreeNode fixture, string caseName)
    {
        JsonTreeNode snapshot = JsonNodes.Get(fixture, "snapshot")!;
        JsonTreeNode input = JsonNodes.Get(fixture, "input") ?? JsonNodes.EmptyObject;
        JsonTreeNode? facts = JsonNodes.Get(fixture, "facts");
        JsonTreeNode? baseNode = JsonNodes.Get(input, "baseSnapshot");
        JsonTreeNode? draftNode = JsonNodes.Get(input, "draft");

        ContentSource? active = ToSource(snapshot, caseName, "snapshot");
        ContentSource? baseVersion = baseNode == null ? null : ToSource(baseNode, caseName, "input.baseSnapshot");
        var actualReasons = new List<ActualReason>();
        string outcomeWord;
        JsonTreeNode payload;
        string snapshotHash;

        if (active == null || baseVersion == null || draftNode == null || draftNode.Kind != JsonTreeKind.Object)
        {
            outcomeWord = RebaseOutcome.InvalidInput;
            ReasonCodeInfo info = ReasonCatalog.Get(ReasonCodes.InputRequired);
            actualReasons.Add(new ActualReason(info.Code, info.DefaultSeverity, null, null, null, "Rebase needs the active version (snapshot), the base version (input.baseSnapshot) and the draft patch (input.draft)."));
            payload = JsonNodes.EmptyObject;
            snapshotHash = CanonicalHash.ComputeContentHash(JsonTreeNode.Null);
        }
        else
        {
            var request = new RebaseRequest(
                baseVersion,
                TryGetInt(baseNode!, "versionNumber") ?? 1,
                active,
                TryGetInt(snapshot, "versionNumber") ?? 1,
                ReadRawPatch(draftNode, caseName + " input.draft"),
                ValidationFactsOf(fixture, input, null, facts),
                RowsInUse(facts));
            RebaseOutcome outcome = _rebaser.Rebase(request);
            outcomeWord = outcome.Outcome;
            payload = outcome.ToJson();
            snapshotHash = outcome.ActiveContentHash;
            foreach (ValidationIssue issue in outcome.Reasons)
            {
                actualReasons.Add(new ActualReason(issue.Code, issue.Severity, issue.Entity, issue.Ref, issue.RuleId, issue.Message));
            }

            if (outcome.InputProblem != null)
            {
                ValidationIssue problem = outcome.InputProblem;
                actualReasons.Add(new ActualReason(problem.Code, problem.Severity, problem.Entity, problem.Ref, problem.RuleId, problem.Message));
            }
        }

        return Judge(name, "rebase", fixture, snapshot, caseName, outcomeWord, payload, snapshotHash, actualReasons);
    }

    // Activation (document 25, section 6.4): "snapshot" is the active version, input.baseSnapshot the version the draft
    // started from, input.draft the draft patch, plus the draft's and the decision's state. The facts are the OP-09 facts
    // the revalidation reads; context.asOfUtc is the validation date (V28). The actor must be engine-nested.
    private FixtureResult RunActivate(string name, JsonTreeNode fixture, string caseName)
    {
        JsonTreeNode snapshot = JsonNodes.Get(fixture, "snapshot")!;
        JsonTreeNode input = JsonNodes.Get(fixture, "input") ?? JsonNodes.EmptyObject;
        JsonTreeNode? facts = JsonNodes.Get(fixture, "facts");
        JsonTreeNode baseNode = JsonNodes.Get(input, "baseSnapshot")!;
        ContentSource active = ToSource(snapshot, caseName, "snapshot")!;
        ContentSource baseVersion = ToSource(baseNode, caseName, "input.baseSnapshot")!;
        ComposedConfiguration composedActive = _composer.Compose(active, ValidationMode.Draft, new ValidationIssues());
        string activeHash = CanonicalHash.ComputeContentHash(composedActive.Bundle);

        ValidationFacts given = ValidationFactsOf(fixture, input, null, facts);
        var request = new ActivationRequest(
            JsonNodes.GetString(input, "configurationDraftId")!,
            EngineContract.DraftStatusMeanings.Parse(JsonNodes.GetString(input, "draftStatus")),
            JsonNodes.GetString(input, "changeDecisionId")!,
            EngineContract.DecisionStatusMeanings.Parse(JsonNodes.GetString(input, "changeDecisionStatus")),
            baseVersion,
            TryGetInt(baseNode, "versionNumber") ?? 1,
            active,
            TryGetInt(snapshot, "versionNumber") ?? 1,
            ReadRawPatch(JsonNodes.Get(input, "draft")!, caseName + " input.draft"),
            TryGetInt(input, "resultingVersionNumber"),
            RowsInUse(facts),
            given.ApprovalRulesVersionNumber,
            given.PrincipalKinds,
            facts == null ? null : TryGetInt(facts, "snapshotCharacterLimit"),
            JsonNodes.GetString(input, "instructedByPrincipalId"));
        EvaluationContext context = BuildContext(JsonNodes.Get(fixture, "context")!, snapshot, activeHash, caseName);
        ActivationOutcome outcome = _activator.Activate(context, request);
        return Judge(name, "activate", fixture, snapshot, caseName, outcome.Outcome, outcome.ToJson(), activeHash, ActivationReasons(outcome));
    }

    // The bootstrap of version 1 (document 21, section 4.8): no version is in force ("snapshot": { "none": true }); the
    // input names the preset and overlays of the apply operation and the two platform facts.
    private FixtureResult RunBootstrap(string name, JsonTreeNode fixture, string caseName)
    {
        JsonTreeNode snapshot = JsonNodes.Get(fixture, "snapshot")!;
        JsonTreeNode input = JsonNodes.Get(fixture, "input") ?? JsonNodes.EmptyObject;
        string noHash = CanonicalHash.ComputeContentHash(JsonTreeNode.Null);
        var request = new BootstrapRequest(
            JsonNodes.GetString(input, "presetKey")!,
            StringList(JsonNodes.Get(input, "overlays")),
            JsonNodes.Get(input, "noVersionExists")?.Kind == JsonTreeKind.True,
            JsonNodes.Get(input, "hasInstallRights")?.Kind == JsonTreeKind.True);
        EvaluationContext context = BuildContext(JsonNodes.Get(fixture, "context")!, snapshot, noHash, caseName);
        ActivationOutcome outcome = _activator.Bootstrap(context, request);
        return Judge(name, "bootstrap", fixture, snapshot, caseName, outcome.Outcome, outcome.ToJson(), noHash, ActivationReasons(outcome));
    }

    // The issues carry their rule IDs; the activation's own reasons come after them.
    private static List<ActualReason> ActivationReasons(ActivationOutcome outcome)
    {
        var reasons = outcome.Issues.Select(i => new ActualReason(i.Code, i.Severity, i.Entity, i.Ref, i.RuleId, i.Message)).ToList();
        reasons.AddRange(outcome.Reasons.Skip(outcome.Issues.Count).Select(r => new ActualReason(r.Code, r.Severity, r.Entity, r.Ref, null, r.Message)));
        return reasons;
    }

    // The guard rule a reason code maps to in the catalog, such as G-06 for approval.outcome-writer.
    private static string? GuardRuleId(string code)
    {
        return ReasonCatalog.TryGet(code, out ReasonCodeInfo? info) ? info.RuleIds.FirstOrDefault(id => id.StartsWith("G-", StringComparison.Ordinal)) : null;
    }

    // An evaluation operation (OP-01 to OP-08, OP-10 to OP-14): the fixture's snapshot is composed as a working set and compiled, as
    // activation would leave it in force. It is not validated first, so a fixture can hold a broken configuration and
    // prove that the engine refuses and reports it. "facts" (what the adapter fetches) and "input" (what the caller
    // sends) together make the operation's wire input; the fixture schema keeps each field on its side.
    private FixtureResult RunEvaluation(string name, JsonTreeNode fixture, string caseName, string operation, EngineOperation evaluation)
    {
        JsonTreeNode snapshot = JsonNodes.Get(fixture, "snapshot")!;
        JsonTreeNode input = JsonNodes.Get(fixture, "input") ?? JsonNodes.EmptyObject;
        JsonTreeNode facts = JsonNodes.Get(fixture, "facts") ?? JsonNodes.EmptyObject;
        int versionNumber = TryGetInt(snapshot, "versionNumber") ?? 1;

        string[] both = input.Members.Select(m => m.Key).Intersect(facts.Members.Select(m => m.Key), StringComparer.Ordinal).ToArray();
        if (both.Length > 0)
        {
            return new FixtureResult(name, operation, caseName, FixtureStatus.Failed, new[] { "input and facts both give " + string.Join(", ", both) });
        }

        JsonTreeNode merged = JsonTreeNode.CreateObject(input.Members.Concat(facts.Members));
        CompiledSnapshot? compiled = null;
        string snapshotHash = CanonicalHash.ComputeContentHash(JsonTreeNode.Null);
        if (JsonNodes.Get(snapshot, "none") == null)
        {
            ContentSource? source = ToSource(snapshot, caseName, "snapshot");
            if (source == null)
            {
                return new FixtureResult(name, operation, caseName, FixtureStatus.Failed, new[] { "the fixture needs a snapshot: a preset, a bundle or none" });
            }

            ComposedConfiguration composed = _composer.Compose(source, ValidationMode.Draft, new ValidationIssues());
            if (!composed.IsComplete)
            {
                return new FixtureResult(name, operation, caseName, FixtureStatus.Failed, new[] { "the snapshot does not compose" });
            }

            try
            {
                compiled = SnapshotCompiler.Compile(composed.Bundle, versionNumber);
                snapshotHash = compiled.ContentHash;
            }
            catch (SnapshotCompileException e)
            {
                // A snapshot the engine cannot read: it refuses, and no broken version comes into force (section 6.3).
                var refused = new List<ActualReason> { new ActualReason(e.Code, ReasonSeverity.Error, null, null, null, e.Message) };
                return Judge(name, operation, fixture, snapshot, caseName, OperationOutcomes.Unavailable, JsonTreeNode.Null, snapshotHash, refused);
            }
        }

        EvaluationContext context = BuildContext(JsonNodes.Get(fixture, "context")!, snapshot, snapshotHash, caseName);
        EvaluationResult<JsonTreeNode?> result = OperationJson.Evaluate(evaluation, compiled?.Snapshot, context, merged);
        bool guard = evaluation == EngineOperation.GuardWrite;
        List<ActualReason> reasons = result.Reasons.Select(r => new ActualReason(r.Code, r.Severity, r.Entity, r.Ref, guard ? GuardRuleId(r.Code) : null, r.Message)).ToList();
        return Judge(name, operation, fixture, snapshot, caseName, result.Outcome, result.Payload ?? JsonTreeNode.Null, snapshotHash, reasons, result.Trace);
    }

    // Compares an answer with the fixture's "expected": outcome, payload, reasons, trace and covered rules.
    private static FixtureResult Judge(string name, string operation, JsonTreeNode fixture, JsonTreeNode snapshot, string caseName, string outcomeWord, JsonTreeNode payload, string snapshotHash, List<ActualReason> actualReasons, IReadOnlyList<RuleTrace>? actualTrace = null)
    {
        JsonTreeNode expected = JsonNodes.Get(fixture, "expected")!;

        // The result an adapter would return (document 25, section 5.1).
        EvaluationContext context = BuildContext(JsonNodes.Get(fixture, "context")!, snapshot, snapshotHash, caseName);
        EvaluationResult<JsonTreeNode> result = EvaluationResult.Create(
            context,
            outcomeWord,
            payload,
            actualReasons.Select(r => new Reason(r.Code, r.Severity, r.Message, r.Entity, r.Ref)));

        var problems = new List<string>();
        string? expectedOutcome = JsonNodes.GetString(expected, "outcome");
        if (!string.Equals(expectedOutcome, result.Outcome, StringComparison.Ordinal))
        {
            problems.Add("outcome: expected " + expectedOutcome + ", got " + result.Outcome);
        }

        JsonTreeNode? expectedPayload = JsonNodes.Get(expected, "payload");
        if (expectedPayload != null)
        {
            bool exact = JsonNodes.GetString(expected, "payloadMatch") == "exact";
            JsonMatch.Matches(expectedPayload, result.Payload, exact, "/payload", problems);
        }

        JsonTreeNode? expectedReasons = JsonNodes.Get(expected, "reasons");
        if (expectedReasons != null)
        {
            bool includes = JsonNodes.GetString(expected, "reasonsMatch") == "includes";
            MatchReasons(expectedReasons, actualReasons, includes, problems);
        }

        JsonTreeNode? expectedTrace = JsonNodes.Get(expected, "trace");
        if (expectedTrace != null)
        {
            bool exact = JsonNodes.GetString(expected, "traceMatch") == "exact";
            MatchTrace(expectedTrace, actualTrace ?? Array.Empty<RuleTrace>(), exact, problems);
        }

        string[] actualRuleIds = actualReasons.Where(r => r.RuleId != null).Select(r => r.RuleId!).Distinct().OrderBy(id => id, StringComparer.Ordinal).ToArray();
        string[] covered = StringList(JsonNodes.Get(fixture, "ruleIds"));
        CheckCoveredRules(operation, expectedOutcome, covered, actualReasons, problems);

        if (problems.Count > 0)
        {
            problems.AddRange(actualReasons.Take(12).Select(r => "  actual: " + r));
            if (actualReasons.Count > 12)
            {
                problems.Add("  actual: ... and " + (actualReasons.Count - 12) + " more");
            }
        }

        string[] actualCodes = actualReasons.Select(r => r.Code).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToArray();
        return new FixtureResult(name, operation, caseName, problems.Count == 0 ? FixtureStatus.Passed : FixtureStatus.Failed, problems, result.Outcome, actualRuleIds, covered, actualCodes);
    }

    // A failing case must report every rule it says it covers. A passing case must report no error for them.
    private static void CheckCoveredRules(string operation, string? expectedOutcome, string[] covered, List<ActualReason> actual, List<string> problems)
    {
        if (!CaseOutcomes.TryGetValue(operation, out KeyValuePair<string[], string[]> words))
        {
            return;
        }

        bool failing = expectedOutcome != null && words.Key.Contains(expectedOutcome, StringComparer.Ordinal);
        bool passing = expectedOutcome != null && words.Value.Contains(expectedOutcome, StringComparer.Ordinal);
        foreach (string ruleId in covered)
        {
            bool reported = actual.Any(r => r.RuleId == ruleId && r.Severity == ReasonSeverity.Error);
            if (failing && !reported)
            {
                problems.Add("ruleIds: the case covers " + ruleId + ", but no " + ruleId + " error was reported");
            }
            else if (passing && reported)
            {
                problems.Add("ruleIds: the case says " + ruleId + " passes, but it reported an error");
            }
        }
    }

    // Trace rows are matched by entity, key and role. includes (the default): at least these rows; exact: these rows
    // and no others, in any order.
    private static void MatchTrace(JsonTreeNode expected, IReadOnlyList<RuleTrace> actual, bool exact, List<string> problems)
    {
        var unmatched = actual.ToList();
        foreach (JsonTreeNode want in expected.Items)
        {
            RuleTrace? hit = unmatched.FirstOrDefault(t =>
                string.Equals(t.Entity, JsonNodes.GetString(want, "entity"), StringComparison.Ordinal)
                && string.Equals(t.Key, JsonNodes.GetString(want, "key"), StringComparison.Ordinal)
                && string.Equals(EngineContract.RuleTraceRoles.ToWire(t.Role), JsonNodes.GetString(want, "role"), StringComparison.Ordinal));
            if (hit == null)
            {
                problems.Add("trace: expected " + want + ", not in the trace (" + string.Join("; ", actual.Select(t => t.ToString())) + ")");
                continue;
            }

            unmatched.Remove(hit);
        }

        if (exact)
        {
            foreach (RuleTrace extra in unmatched)
            {
                problems.Add("trace: not expected: " + extra);
            }
        }
    }

    private static void MatchReasons(JsonTreeNode expected, List<ActualReason> actual, bool includes, List<string> problems)
    {
        var unmatched = new List<ActualReason>(actual);
        foreach (JsonTreeNode want in expected.Items)
        {
            ActualReason? hit = unmatched.FirstOrDefault(r => r.Matches(want));
            if (hit == null)
            {
                problems.Add("reasons: expected " + want + ", not reported");
                continue;
            }

            unmatched.Remove(hit);
        }

        if (!includes)
        {
            foreach (ActualReason extra in unmatched)
            {
                problems.Add("reasons: not expected: " + extra);
            }
        }
    }

    // The fixture's snapshot (or activeSnapshot) as a content source. Null for "none".
    internal static ContentSource? ToSource(JsonTreeNode snapshot, string caseName, string field)
    {
        ContentSource source;
        string where = caseName + " " + field;
        string? presetKey = JsonNodes.GetString(snapshot, "presetKey");
        JsonTreeNode? bundle = JsonNodes.Get(snapshot, "bundle");
        JsonTreeNode? raw = JsonNodes.Get(snapshot, "raw");
        if (presetKey != null)
        {
            source = ContentSource.Preset(presetKey, StringList(JsonNodes.Get(snapshot, "overlays")));
        }
        else if (bundle != null)
        {
            source = ContentSource.Bundle(bundle, where + ".bundle");
        }
        else if (raw != null)
        {
            source = ContentSource.Raw(raw, where + ".raw");
        }
        else
        {
            return null;
        }

        JsonTreeNode? operations = JsonNodes.Get(snapshot, "operations");
        if (operations != null)
        {
            source = source.WithPatch(ConfigurationPatch.FromOperations(operations, PatchKind.Draft, where + ".operations"));
        }

        return source;
    }

    private static bool IsPatchDocument(JsonTreeNode raw)
    {
        JsonTreeNode? manifest = JsonNodes.Get(raw, "manifest");
        return manifest != null && JsonNodes.GetString(manifest, "kind") == "patch";
    }

    // A client binding patch is read as one when it matches its schema. Anything else is a draft patch,
    // so V01 checks it against the draft patch schema.
    private static ConfigurationPatch ReadRawPatch(JsonTreeNode raw, string where)
    {
        bool isBinding = ConfigurationSchemas.Set.Evaluate(raw, ConfigurationSchemas.ClientBindingPatch).IsValid;
        return ConfigurationPatch.FromDocument(raw, isBinding ? PatchKind.ClientBinding : PatchKind.Draft, where);
    }

    private static ValidationMode ParseMode(string? mode)
    {
        switch (mode)
        {
            case "draft":
                return ValidationMode.Draft;
            case "activation":
                return ValidationMode.Activation;
            default:
                return ValidationMode.PresetBuild;
        }
    }

    // facts.rowsInUse: [ { "entity": "...", "key": "..." } ], the rows operational records use (V08).
    private static IEnumerable<RowIdentity> RowsInUse(JsonTreeNode? facts)
    {
        JsonTreeNode? rows = facts == null ? null : JsonNodes.Get(facts, "rowsInUse");
        if (rows == null || rows.Kind != JsonTreeKind.Array)
        {
            yield break;
        }

        foreach (JsonTreeNode row in rows.Items)
        {
            string? entity = JsonNodes.GetString(row, "entity");
            string? key = JsonNodes.GetString(row, "key");
            if (entity != null && key != null)
            {
                yield return new RowIdentity(entity, key);
            }
        }
    }

    // The other OP-09 facts (document 25, section 5.11):
    // - the validation date is context.asOfUtc (V28);
    // - the base version is input.baseVersionNumber, else the raw patch's manifest (V42);
    // - the active version is input.activeSnapshot.versionNumber (default 1), else the base version (V34, V42);
    // - facts.isRebased, facts.approvalRulesVersionNumber (V34), facts.principals as [{ "objectId", "principalKind" }] (V35)
    //   and facts.snapshotCharacterLimit (V41).
    private static ValidationFacts ValidationFactsOf(JsonTreeNode fixture, JsonTreeNode input, JsonTreeNode? activeNode, JsonTreeNode? facts)
    {
        JsonTreeNode context = JsonNodes.Get(fixture, "context")!;
        DateTime asOf = DateTime.Parse(JsonNodes.GetString(context, "asOfUtc")!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        int? baseVersion = TryGetInt(input, "baseVersionNumber");
        int? activeVersion = activeNode != null ? TryGetInt(activeNode, "versionNumber") ?? 1 : baseVersion;
        JsonTreeNode noFacts = JsonNodes.EmptyObject;
        return new ValidationFacts(
            validatedOn: asOf,
            baseVersionNumber: baseVersion,
            activeVersionNumber: activeVersion,
            isRebased: JsonNodes.Get(facts ?? noFacts, "isRebased")?.Kind == JsonTreeKind.True,
            approvalRulesVersionNumber: TryGetInt(facts ?? noFacts, "approvalRulesVersionNumber"),
            principalKinds: Principals(facts),
            snapshotCharacterLimit: TryGetInt(facts ?? noFacts, "snapshotCharacterLimit"));
    }

    // facts.principals: [ { "objectId": "...", "principalKind": "group" } ], the kinds of directory objects (V35).
    private static IEnumerable<KeyValuePair<string, PrincipalKind>> Principals(JsonTreeNode? facts)
    {
        JsonTreeNode? rows = facts == null ? null : JsonNodes.Get(facts, "principals");
        if (rows == null || rows.Kind != JsonTreeKind.Array)
        {
            yield break;
        }

        foreach (JsonTreeNode row in rows.Items)
        {
            string? id = JsonNodes.GetString(row, "objectId");
            if (id != null && EngineContract.PrincipalKinds.TryParse(JsonNodes.GetString(row, "principalKind"), out PrincipalKind kind))
            {
                yield return new KeyValuePair<string, PrincipalKind>(id, kind);
            }
        }
    }

    internal static EvaluationContext BuildContext(JsonTreeNode context, JsonTreeNode snapshot, string snapshotHash, string caseName)
    {
        DateTime asOf = DateTime.Parse(JsonNodes.GetString(context, "asOfUtc")!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        int version = TryGetInt(context, "configurationVersion") ?? TryGetInt(snapshot, "versionNumber") ?? 1;
        string hash = JsonNodes.GetString(context, "snapshotHash") ?? snapshotHash;
        string correlation = JsonNodes.GetString(context, "correlationId") ?? "fixture-" + caseName;
        return new EvaluationContext(version, hash, asOf, BuildActor(JsonNodes.Get(context, "actor")), correlation, JsonNodes.GetString(context, "clientApp"));
    }

    // Fixtures use fictional IDs only. Without an actor, the engine itself is the actor.
    internal static ActorInfo BuildActor(JsonTreeNode? actor)
    {
        if (actor == null)
        {
            return new ActorInfo("fixture-engine", PrincipalKind.ServicePrincipal, ActorKindMeaning.Engine);
        }

        return new ActorInfo(
            JsonNodes.GetString(actor, "principalId")!,
            EngineContract.PrincipalKinds.Parse(JsonNodes.GetString(actor, "principalKind")),
            EngineContract.ActorKindMeanings.Parse(JsonNodes.GetString(actor, "actorKind")),
            StringList(JsonNodes.Get(actor, "roleKeys")),
            JsonNodes.GetString(actor, "onBehalfOfPrincipalId"),
            JsonNodes.Get(actor, "isEngineNested")?.Kind == JsonTreeKind.True);
    }

    internal static int? TryGetInt(JsonTreeNode node, string name)
    {
        JsonTreeNode? value = JsonNodes.Get(node, name);
        return value != null && value.TryGetInt64(out long number) && number >= int.MinValue && number <= int.MaxValue ? (int)number : null;
    }

    internal static string[] StringList(JsonTreeNode? node)
    {
        return node == null || node.Kind != JsonTreeKind.Array
            ? Array.Empty<string>()
            : node.Items.Where(i => i.Kind == JsonTreeKind.String).Select(i => i.GetString()).ToArray();
    }

    private static IEnumerable<string> FindFixtures(string folder)
    {
        var files = new List<string>();
        var stack = new Stack<string>();
        stack.Push(folder);
        while (stack.Count > 0)
        {
            string dir = stack.Pop();
            files.AddRange(Directory.GetFiles(dir, "*.json"));
            foreach (string sub in Directory.GetDirectories(dir))
            {
                string subName = Path.GetFileName(sub);
                if (!string.Equals(subName, "presets", StringComparison.Ordinal) && !string.Equals(subName, ReplayTool.FolderName, StringComparison.Ordinal)
                    && !string.Equals(subName, "bin", StringComparison.Ordinal) && !string.Equals(subName, "obj", StringComparison.Ordinal))
                {
                    stack.Push(sub);
                }
            }
        }

        return files.OrderBy(f => Relative(folder, f), StringComparer.Ordinal);
    }

    private static string Relative(string folder, string file)
    {
        string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(file);
        string relative = full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full.Substring(root.Length) : full;
        return relative.Replace('\\', '/');
    }

    // A reason the operation returned, with its rule ID, for matching against an expected reason.
    private sealed class ActualReason
    {
        public ActualReason(string code, ReasonSeverity severity, string? entity, string? reference, string? ruleId, string message)
        {
            Code = code;
            Severity = severity;
            Entity = entity;
            Ref = reference;
            RuleId = ruleId;
            Message = message;
        }

        public string Code { get; }

        public ReasonSeverity Severity { get; }

        public string? Entity { get; }

        public string? Ref { get; }

        public string? RuleId { get; }

        public string Message { get; }

        // Code must match. Severity, entity, ref and ruleId must match when the expected reason gives them.
        public bool Matches(JsonTreeNode expected)
        {
            return string.Equals(JsonNodes.GetString(expected, "code"), Code, StringComparison.Ordinal)
                && Optional(expected, "severity", ValidationIssue.SeverityWire(Severity))
                && Optional(expected, "entity", Entity)
                && Optional(expected, "ref", Ref)
                && Optional(expected, "ruleId", RuleId);
        }

        public override string ToString()
        {
            return ValidationIssue.SeverityWire(Severity) + " " + Code + (RuleId == null ? string.Empty : " (" + RuleId + ")") + ": " + Message;
        }

        private static bool Optional(JsonTreeNode expected, string field, string? actual)
        {
            string? want = JsonNodes.GetString(expected, field);
            return want == null || string.Equals(want, actual, StringComparison.Ordinal);
        }
    }
}
