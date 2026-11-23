using System;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Product.Engine.Configuration.Composition;
using Product.Engine.Configuration.Json;
using Product.Engine.Configuration.Patches;
using Product.Engine.Configuration.Presets;
using Product.Engine.Configuration.Schema;
using Product.Engine.Configuration.Validation;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Testing;

namespace Product.Engine.Cli;

// The engine command line: validate, build-presets, run-fixtures, replay, capture, rules.
// It reads files only (capture writes the one file it is told to). No sign-in, no tenant, no network.
// Exit codes: 0 clean, 1 validation errors, failed fixtures or replay differences, 2 bad usage or unreadable input.
internal static class Program
{
    private const int Clean = 0;
    private const int Problems = 1;
    private const int Usage = 2;

    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] == "help" || args[0] == "--help" || args[0] == "-h")
        {
            PrintHelp();
            return Clean;
        }

        try
        {
            var options = new Options(args.Skip(1));
            switch (args[0])
            {
                case "validate":
                    return Validate(options);
                case "build-presets":
                    return BuildPresets(options);
                case "run-fixtures":
                    return RunFixtures(options);
                case "replay":
                    return Replay(options);
                case "capture":
                    return Capture(options);
                case "rules":
                    return ListRules();
                default:
                    Console.Error.WriteLine("Unknown command: " + args[0] + ". Run with --help to see the commands.");
                    return Usage;
            }
        }
        catch (UsageException e)
        {
            Console.Error.WriteLine(e.Message);
            return Usage;
        }
        catch (DirectoryNotFoundException e)
        {
            Console.Error.WriteLine(e.Message);
            return Usage;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: Product.Engine.Cli <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  validate <preset-key | bundle.json> [--overlay <key>]... [--patch <file>]...");
        Console.WriteLine("           [--mode preset-build|draft|activation] [--version <n>] [--as-of <yyyy-MM-dd>] [--out <file>] [--json]");
        Console.WriteLine("      OP-09 over a preset with overlays and patches, or over a bundle file.");
        Console.WriteLine("      A patch may be a draft patch or a client binding patch.");
        Console.WriteLine("  build-presets [--out <dir>] [--check]");
        Console.WriteLine("      Builds every full preset into <dir>/<preset-key>.bundle.json (default config/build)");
        Console.WriteLine("      and validates every full preset alone and with each allowed overlay combination.");
        Console.WriteLine("      --check validates without writing.");
        Console.WriteLine("  run-fixtures <folder> [--filter <text>]");
        Console.WriteLine("      Runs every fixture file under the folder and reports pass or fail per fixture.");
        Console.WriteLine("  replay <record.json | folder> [--snapshot <bundle.json>] [--version <n>] [--ignore-messages] [--json]");
        Console.WriteLine("      Replays each recorded call (document 25, section 9): loads its snapshot version, or the bundle given,");
        Console.WriteLine("      evaluates the recorded request again and reports every difference from the recorded result or plan.");
        Console.WriteLine("  capture <record.json> [--out <file>] [--snapshot <bundle.json>] [--version <n>]");
        Console.WriteLine("      Evaluates the request of a replay record and writes the record with its recorded result.");
        Console.WriteLine("  rules");
        Console.WriteLine("      Lists the validation rules (V01 to V44) and the engine checks this engine runs.");
        Console.WriteLine();
        Console.WriteLine("Common options:");
        Console.WriteLine("  --root <src>      the src/ folder (default: found from the current folder)");
        Console.WriteLine("  --presets <dir>   another preset folder to read (repeatable)");
    }

    // ---- validate ----

    private static int Validate(Options options)
    {
        string root = options.Root();
        string target = options.Positional(0, "validate needs a preset key or a bundle file.");
        var roots = new List<string> { SourceRoot.PresetsFolder(root) };
        roots.AddRange(options.All("--presets"));
        PresetCatalog catalog = PresetCatalog.Load(roots);

        ContentSource source;
        bool presetBuildByDefault;
        if (target.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || File.Exists(target))
        {
            JsonTreeNode document = ReadJson(target);
            JsonTreeNode manifest = JsonNodes.Get(document, "manifest") ?? JsonNodes.EmptyObject;
            if (JsonNodes.GetString(manifest, "kind") == "patch")
            {
                throw new UsageException("A patch needs a base. Use: validate <preset-key> --patch " + target);
            }

            if (options.All("--overlay").Count > 0)
            {
                throw new UsageException("--overlay works with a preset key, not with a bundle file.");
            }

            source = ContentSource.Bundle(document, Path.GetFileName(target));
            presetBuildByDefault = JsonNodes.GetString(manifest, "presetKey") != null;
        }
        else
        {
            source = ContentSource.Preset(target, options.All("--overlay"));
            presetBuildByDefault = true;
        }

        foreach (string patchFile in options.All("--patch"))
        {
            source = source.WithPatch(ReadPatch(patchFile));
        }

        ValidationMode mode = ParseMode(options.Value("--mode"), presetBuildByDefault && options.All("--patch").Count == 0);
        int version = options.IntValue("--version") ?? 1;
        var validator = new ConfigurationValidator(catalog);

        // The engine core reads no clock. This host gives it the validation date (V28 workload grant expiry).
        var facts = new ValidationFacts(validatedOn: AsOf(options.Value("--as-of")));
        ValidationOutcome outcome = validator.Validate(new ValidationRequest(source, mode, versionNumber: version, facts: facts));

        string? outFile = options.Value("--out");
        if (outFile != null && outcome.Candidate != null)
        {
            JsonText.WriteFile(outFile, JsonText.WriteIndented(outcome.Candidate.Bundle, snapshotOrder: true));
        }

        if (options.Has("--json"))
        {
            Console.Write(JsonText.WriteIndented(outcome.Report.ToJson(), snapshotOrder: false));
            return outcome.Outcome == "valid" ? Clean : outcome.Outcome == "invalid-input" ? Usage : Problems;
        }

        Console.WriteLine("OP-09 validate " + Describe(target, options) + " (" + ModeWord(mode) + ")");
        Console.WriteLine("  rules checked: " + string.Join(", ", ConfigurationValidator.Rules.Select(r => r.RuleId)));
        Console.WriteLine("  engine checks: " + string.Join(", ", ConfigurationValidator.EngineChecks.Select(c => c.Code)));
        if (outcome.InputProblem != null)
        {
            Console.WriteLine("  " + outcome.InputProblem.Code + ": " + outcome.InputProblem.Message);
            Console.WriteLine("Result: invalid-input");
            return Usage;
        }

        Console.WriteLine("  content hash: " + outcome.Report.ContentHash);
        if (outcome.Candidate != null)
        {
            Console.WriteLine("  rows: " + outcome.Candidate.Content.Rows.Count + " in " + outcome.Candidate.Content.EntityKeys.Count() + " entities");
        }

        if (outcome.Snapshot != null)
        {
            Console.WriteLine("  compiled snapshot: set " + outcome.Snapshot.ConfigurationSetKey + ", version " + outcome.Snapshot.VersionNumber + ", " + outcome.Snapshot.Snapshot.Entities.Count + " entities");
        }

        foreach (string line in outcome.Report.ChangeSummary)
        {
            Console.WriteLine("  change: " + line);
        }

        foreach (ValidationIssue issue in outcome.Report.Issues)
        {
            Console.WriteLine("  " + ValidationIssue.SeverityWire(issue.Severity) + " " + issue.Code + ": " + issue.Message);
        }

        Console.WriteLine("Result: " + outcome.Outcome + " (" + outcome.Report.ErrorCount + " error(s), " + outcome.Report.WarningCount + " warning(s))");
        return outcome.Outcome == "valid" ? Clean : Problems;
    }

    // --as-of yyyy-MM-dd, or today in UTC.
    private static DateTime AsOf(string? text)
    {
        if (text == null)
        {
            return DateTime.UtcNow.Date;
        }

        if (!DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
        {
            throw new UsageException("--as-of needs a date such as 2026-11-22.");
        }

        return date;
    }

    private static ConfigurationPatch ReadPatch(string file)
    {
        JsonTreeNode document = ReadJson(file);

        // A client binding patch has its own schema. Anything else is checked as a draft patch.
        bool isBinding = ConfigurationSchemas.Set.Evaluate(document, ConfigurationSchemas.ClientBindingPatch).IsValid;
        return ConfigurationPatch.FromDocument(document, isBinding ? PatchKind.ClientBinding : PatchKind.Draft, Path.GetFileName(file));
    }

    // ---- build-presets ----

    private static int BuildPresets(Options options)
    {
        string root = options.Root();
        var roots = new List<string> { SourceRoot.PresetsFolder(root) };
        roots.AddRange(options.All("--presets"));
        PresetCatalog catalog = PresetCatalog.Load(roots);
        bool check = options.Has("--check");
        string outDir = options.Value("--out") ?? SourceRoot.BuildFolder(root);
        var validator = new ConfigurationValidator(catalog);
        int failures = 0;

        PresetSource[] fulls = catalog.FullPresets.ToArray();
        PresetSource[] overlays = catalog.Overlays.ToArray();
        Console.WriteLine("Presets: " + fulls.Length + " full, " + overlays.Length + " overlay(s), from " + string.Join(", ", roots));
        if (fulls.Length == 0)
        {
            Console.Error.WriteLine("No full preset found.");
            return Problems;
        }

        if (!check)
        {
            Directory.CreateDirectory(outDir);
        }

        foreach (PresetSource full in fulls)
        {
            ValidationOutcome outcome = validator.Validate(new ValidationRequest(ContentSource.Preset(full.Key), ValidationMode.PresetBuild));
            bool valid = outcome.Outcome == "valid";
            string written = string.Empty;
            string file = Path.Combine(outDir, full.Key + ".bundle.json");
            if (valid && !check && outcome.Candidate != null)
            {
                JsonText.WriteFile(file, JsonText.WriteIndented(outcome.Candidate.Bundle, snapshotOrder: true));
                written = ", wrote " + Path.GetFileName(file);
            }
            else if (!valid && !check && File.Exists(file))
            {
                // Only valid bundles ship. An old bundle of a preset that now fails must not stay behind.
                File.Delete(file);
                written = ", removed the old " + Path.GetFileName(file);
            }

            Console.WriteLine("  " + (valid ? "PASS" : "FAIL") + "  " + full.Key + ": " + Summary(outcome) + written);
            failures += Report(outcome, valid);
        }

        foreach (PresetSource full in fulls)
        {
            foreach (string[] combination in catalog.AllowedOverlayCombinations(full))
            {
                ValidationOutcome outcome = validator.Validate(new ValidationRequest(ContentSource.Preset(full.Key, combination), ValidationMode.PresetBuild));
                bool valid = outcome.Outcome == "valid";
                Console.WriteLine("  " + (valid ? "PASS" : "FAIL") + "  " + full.Key + " + " + string.Join(" + ", combination) + ": " + Summary(outcome));
                failures += Report(outcome, valid);
            }
        }

        Console.WriteLine(failures == 0 ? "Presets: all valid." : "Presets: " + failures + " selection(s) with errors.");
        return failures == 0 ? Clean : Problems;
    }

    private static int Report(ValidationOutcome outcome, bool valid)
    {
        if (outcome.InputProblem != null)
        {
            Console.WriteLine("        " + outcome.InputProblem.Message);
        }

        foreach (ValidationIssue issue in outcome.Report.Issues.Where(i => i.Severity == ReasonSeverity.Error).Take(25))
        {
            Console.WriteLine("        " + issue.Code + ": " + issue.Message);
        }

        return valid ? 0 : 1;
    }

    private static string Summary(ValidationOutcome outcome)
    {
        string rows = outcome.Candidate == null ? string.Empty : outcome.Candidate.Content.Rows.Count + " rows, ";
        return rows + outcome.Report.ErrorCount + " error(s), " + outcome.Report.WarningCount + " warning(s), hash " + outcome.Report.ContentHash.Substring(0, 12);
    }

    // ---- run-fixtures ----

    private static int RunFixtures(Options options)
    {
        string root = options.Root();
        string folder = options.Positional(0, "run-fixtures needs a folder, for example: run-fixtures fixtures");
        var roots = new List<string> { SourceRoot.PresetsFolder(root), SourceRoot.FixturePresetsFolder(root) };
        roots.AddRange(options.All("--presets"));
        var runner = new FixtureRunner(PresetCatalog.Load(roots));
        string? filter = options.Value("--filter");

        IReadOnlyList<FixtureResult> results = runner.RunFolder(folder);
        if (filter != null)
        {
            results = results.Where(r => r.File.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }

        foreach (FixtureResult result in results)
        {
            string word = result.Status == FixtureStatus.Passed ? "PASS" : result.Status == FixtureStatus.Failed ? "FAIL" : "SKIP";
            string rules = result.CoveredRuleIds.Count == 0 ? string.Empty : " [" + string.Join(", ", result.CoveredRuleIds) + "]";
            Console.WriteLine(word + "  " + result.File + "  " + (result.ActualOutcome ?? string.Empty) + rules);
            foreach (string problem in result.Problems)
            {
                Console.WriteLine("      " + problem);
            }
        }

        int passed = results.Count(r => r.Status == FixtureStatus.Passed);
        int failed = results.Count(r => r.Status == FixtureStatus.Failed);
        int skipped = results.Count(r => r.Status == FixtureStatus.Skipped);
        string[] covered = results.Where(r => r.Status == FixtureStatus.Passed && r.ActualOutcome == "invalid")
            .SelectMany(r => r.CoveredRuleIds).Distinct().OrderBy(id => id, StringComparer.Ordinal).ToArray();
        Console.WriteLine("Fixtures: " + passed + " passed, " + failed + " failed, " + skipped + " skipped.");
        if (covered.Length > 0)
        {
            Console.WriteLine("Rules with a passing broken case: " + string.Join(", ", covered));
        }

        if (results.Count == 0)
        {
            Console.Error.WriteLine("No fixture files found under " + folder + ".");
            return Problems;
        }

        return failed == 0 ? Clean : Problems;
    }

    // ---- replay and capture (document 25, section 9) ----

    private static int Replay(Options options)
    {
        string root = options.Root();
        string target = options.Positional(0, "replay needs a record file or a folder, for example: replay fixtures/replay");
        ReplayTool tool = new ReplayTool(Catalog(root, options));
        ReplayOptions replayOptions = ReplayOptionsOf(options);
        replayOptions.IgnoreMessages = options.Has("--ignore-messages");

        IReadOnlyList<ReplayResult> results;
        if (Directory.Exists(target))
        {
            results = tool.ReplayFolder(target, replayOptions);
        }
        else if (File.Exists(target))
        {
            results = new[] { tool.ReplayFile(target, Path.GetFileName(target), replayOptions) };
        }
        else
        {
            throw new UsageException("No record file or folder at " + target + ".");
        }

        if (options.Has("--json"))
        {
            Console.Write(JsonText.WriteIndented(JsonTreeNode.CreateArray(results.Select(r => r.ToJson())), snapshotOrder: false));
        }
        else
        {
            foreach (ReplayResult result in results)
            {
                string word = result.Passed ? "PASS" : "FAIL";
                string what = result.Status == ReplayStatus.Invalid ? "invalid"
                    : result.Status == ReplayStatus.Same ? "same answer"
                    : result.Differences.Count + " difference(s)" + (result.ExpectedDifferences == null ? string.Empty : result.Passed ? ", as expected" : ", expected " + (result.ExpectedDifferences.Length == 0 ? "none" : string.Join(", ", result.ExpectedDifferences)));
                Console.WriteLine(word + "  " + result.File + "  " + (result.Operation ?? "?") + "  " + what + (result.SnapshotSource == null ? string.Empty : "  [" + result.SnapshotSource + "]"));
                foreach (string problem in result.Problems)
                {
                    Console.WriteLine("      " + problem);
                }

                foreach (ReplayDifference difference in result.Differences)
                {
                    Console.WriteLine("      " + difference);
                }
            }

            Console.WriteLine("Replays: " + results.Count(r => r.Passed) + " passed, " + results.Count(r => !r.Passed) + " failed (engine " + EngineInfo.Version + ").");
        }

        if (results.Count == 0)
        {
            Console.Error.WriteLine("No replay records found under " + target + ".");
            return Problems;
        }

        return results.All(r => r.Passed) ? Clean : Problems;
    }

    private static int Capture(Options options)
    {
        string root = options.Root();
        string file = options.Positional(0, "capture needs a replay record file.");
        JsonTreeNode record = ReadJson(file);
        JsonTreeNode captured;
        try
        {
            captured = new ReplayTool(Catalog(root, options)).Capture(record, ReplayOptionsOf(options));
        }
        catch (ReplayRecordException e)
        {
            throw new UsageException(file + ": " + e.Message);
        }

        string text = JsonText.WriteIndented(captured, snapshotOrder: false);
        string? outFile = options.Value("--out");
        if (outFile == null)
        {
            Console.Write(text);
        }
        else
        {
            JsonText.WriteFile(outFile, text);
            Console.WriteLine("Captured " + JsonNodes.GetString(captured, "operation") + " " + JsonNodes.GetString(captured, "case") + " into " + outFile + ".");
        }

        return Clean;
    }

    private static PresetCatalog Catalog(string root, Options options)
    {
        var roots = new List<string> { SourceRoot.PresetsFolder(root), SourceRoot.FixturePresetsFolder(root) };
        roots.AddRange(options.All("--presets"));
        return PresetCatalog.Load(roots);
    }

    // --snapshot <bundle.json> loads that bundle (a C02 Content Json export or a built preset bundle) as the version.
    private static ReplayOptions ReplayOptionsOf(Options options)
    {
        var replayOptions = new ReplayOptions { VersionNumber = options.IntValue("--version") };
        string? bundleFile = options.Value("--snapshot");
        if (bundleFile != null)
        {
            replayOptions.SnapshotBundle = ReadJson(bundleFile);
            replayOptions.SnapshotName = Path.GetFileName(bundleFile) + ", version " + (replayOptions.VersionNumber?.ToString(CultureInfo.InvariantCulture) ?? "of the record");
        }

        return replayOptions;
    }

    // ---- rules ----

    private static int ListRules()
    {
        foreach (ValidationRule rule in ConfigurationValidator.Rules)
        {
            Console.WriteLine(rule.RuleId + "  " + rule.Code.PadRight(32) + " " + ValidationIssue.SeverityWire(rule.Severity).PadRight(8) + rule.Title);
        }

        foreach (EngineCheck check in ConfigurationValidator.EngineChecks)
        {
            Console.WriteLine("--   " + check.Code.PadRight(32) + " " + ValidationIssue.SeverityWire(check.Severity).PadRight(8) + check.Title);
        }

        Console.WriteLine("Engine " + EngineInfo.Version + ". Rows marked -- are the engine checks beyond V44 (document 25, section 5.11). They have no V-ID yet.");
        return Clean;
    }

    // ---- helpers ----

    private static JsonTreeNode ReadJson(string file)
    {
        if (!JsonText.TryReadFile(file, out JsonTreeNode? node, out string? error))
        {
            throw new UsageException(file + ": " + error);
        }

        return node!;
    }

    private static ValidationMode ParseMode(string? text, bool presetBuildByDefault)
    {
        switch (text)
        {
            case null:
                return presetBuildByDefault ? ValidationMode.PresetBuild : ValidationMode.Draft;
            case "preset-build":
                return ValidationMode.PresetBuild;
            case "draft":
                return ValidationMode.Draft;
            case "activation":
                return ValidationMode.Activation;
            default:
                throw new UsageException("--mode must be preset-build, draft or activation.");
        }
    }

    private static string ModeWord(ValidationMode mode)
    {
        return mode == ValidationMode.PresetBuild ? "preset-build" : mode == ValidationMode.Draft ? "draft" : "activation";
    }

    private static string Describe(string target, Options options)
    {
        string text = target;
        foreach (string overlay in options.All("--overlay"))
        {
            text += " + " + overlay;
        }

        foreach (string patch in options.All("--patch"))
        {
            text += " + patch " + Path.GetFileName(patch);
        }

        return text;
    }

    private sealed class UsageException : Exception
    {
        public UsageException(string message)
            : base(message)
        {
        }
    }

    // A small option parser: --name value pairs, repeatable names, flags, and positional arguments.
    private sealed class Options
    {
        private static readonly HashSet<string> Flags = new(StringComparer.Ordinal) { "--json", "--check", "--ignore-messages" };

        private readonly List<string> _positional = new();
        private readonly List<KeyValuePair<string, string>> _values = new();
        private readonly HashSet<string> _flags = new(StringComparer.Ordinal);

        public Options(IEnumerable<string> args)
        {
            string[] list = args.ToArray();
            for (int i = 0; i < list.Length; i++)
            {
                string arg = list[i];
                if (!arg.StartsWith("--", StringComparison.Ordinal))
                {
                    _positional.Add(arg);
                }
                else if (Flags.Contains(arg))
                {
                    _flags.Add(arg);
                }
                else if (i + 1 < list.Length)
                {
                    _values.Add(new KeyValuePair<string, string>(arg, list[++i]));
                }
                else
                {
                    throw new UsageException(arg + " needs a value.");
                }
            }
        }

        public string Positional(int index, string missing)
        {
            if (index >= _positional.Count)
            {
                throw new UsageException(missing);
            }

            return _positional[index];
        }

        public string? Value(string name)
        {
            return _values.Where(v => v.Key == name).Select(v => v.Value).LastOrDefault();
        }

        public int? IntValue(string name)
        {
            string? text = Value(name);
            if (text == null)
            {
                return null;
            }

            if (!int.TryParse(text, out int value) || value < 1)
            {
                throw new UsageException(name + " must be a whole number of 1 or more.");
            }

            return value;
        }

        public IReadOnlyList<string> All(string name)
        {
            return _values.Where(v => v.Key == name).Select(v => v.Value).ToList();
        }

        public bool Has(string flag)
        {
            return _flags.Contains(flag);
        }

        public string Root()
        {
            string? root = Value("--root");
            if (root != null)
            {
                return SourceRoot.Find(root) ?? throw new UsageException("--root " + root + " is not a src/ folder.");
            }

            return SourceRoot.Require();
        }
    }
}
