using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Product.Engine.Configuration.Composition;
using Product.Engine.Configuration.Json;
using Product.Engine.Configuration.Validation;
using Product.Engine.Core.Canonical;
using Product.Engine.Testing;
using Xunit;

namespace Product.Engine.Configuration.Tests;

// The replay tool of document 25, section 9. An auditor loads the version a result names, gives the same request
// and facts, and gets the same answer; the tool reports every difference. The records under fixtures/replay hold true
// records (no difference) and tampered ones (exactly the differences they expect).
public sealed class ReplayTests
{
    private static string ReplayFolder => Path.Combine(TestData.Root, "fixtures", ReplayTool.FolderName);

    [Fact]
    public void EveryReplayFixturePasses()
    {
        List<ReplayResult> results = ReplayTool.ForSourceRoot(TestData.Root).ReplayFolder(ReplayFolder).ToList();

        Assert.True(results.Count >= 14, "only " + results.Count + " replay records");
        Assert.All(results, r => Assert.True(r.Passed, r.File + ": " + string.Join(" | ", r.Problems.Concat(r.Differences.Select(d => d.ToString())))));
        Assert.Contains(results, r => r.Status == ReplayStatus.Same && r.ExpectedDifferences == null);
        foreach (string kind in ReplayTool.DifferenceKinds)
        {
            Assert.True(results.Any(r => r.ExpectedDifferences != null && r.ExpectedDifferences.Contains(kind)), "no replay fixture expects a " + kind + " difference");
        }

        foreach (string operation in new[] { "OP-01", "OP-02", "OP-03", "OP-04", "OP-05", "OP-08", "OP-14" })
        {
            Assert.Contains(results, r => r.Operation == operation && r.Status == ReplayStatus.Same);
        }
    }

    // Every evaluation fixture in src/ (OP-01 to OP-08 and OP-10 to OP-14), turned into a replay record, captured and
    // replayed, gives the same answer: same outcome, payload, reasons, trace, version, hash and engine version.
    [Fact]
    public void EveryEvaluationFixtureReplaysTheSame()
    {
        ReplayTool tool = ReplayTool.ForSourceRoot(TestData.Root);
        int replayed = 0;
        foreach (string folder in Directory.GetDirectories(Path.Combine(TestData.Root, "fixtures"), "op-*").OrderBy(f => f, StringComparer.Ordinal))
        {
            if (Path.GetFileName(folder) == "op-09")
            {
                continue;
            }

            foreach (string file in Directory.GetFiles(folder, "*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                JsonTreeNode record = ToRecord(CanonicalJson.Parse(File.ReadAllText(file)));
                JsonTreeNode captured = tool.Capture(record);
                ReplayResult result = tool.Replay(captured, Path.GetFileName(file));
                Assert.True(result.Status == ReplayStatus.Same, file + ": " + string.Join(" | ", result.Problems.Concat(result.Differences.Select(d => d.ToString()))));

                // The capture is deterministic: the same record gives the same bytes.
                Assert.Equal(JsonText.WriteIndented(captured, snapshotOrder: false), JsonText.WriteIndented(tool.Capture(record), snapshotOrder: false));
                replayed++;
            }
        }

        Assert.True(replayed > 400, "only " + replayed + " fixtures replayed");
    }

    // A record's stored result changed after the fact is caught, field by field.
    [Fact]
    public void ATamperedRecordIsReportedByPointer()
    {
        ReplayTool tool = ReplayTool.ForSourceRoot(TestData.Root);
        JsonTreeNode record = Read("op-08-ai-create-forced-proposed.json");
        JsonTreeNode recorded = JsonNodes.Get(record, "recorded")!;
        JsonTreeNode payload = JsonNodes.Get(recorded, "payload")!;
        JsonTreeNode columns = JsonNodes.With(JsonNodes.Get(payload, "setColumns")!, "isAiCreated", JsonTreeNode.False);
        JsonTreeNode tampered = JsonNodes.With(record, "recorded", JsonNodes.With(recorded, "payload", JsonNodes.With(payload, "setColumns", columns)));

        ReplayResult result = tool.Replay(tampered, "tampered");

        Assert.Equal(ReplayStatus.Different, result.Status);
        Assert.False(result.Passed);
        ReplayDifference difference = Assert.Single(result.Differences);
        Assert.Equal("payload", difference.Kind);
        Assert.Equal("/setColumns/isAiCreated", difference.Path);
        Assert.Equal("false", difference.Recorded);
        Assert.Equal("true", difference.Replayed);
    }

    // The caller may load another copy of the version, such as a C02 Content Json export or a built preset bundle: the
    // same content gives the same hash and the same answer; another preset is reported as another version.
    [Fact]
    public void AnExportedBundleOfTheSameVersionReplaysTheSame()
    {
        ReplayTool tool = ReplayTool.ForSourceRoot(TestData.Root);
        JsonTreeNode record = Read("op-04-enforce-stops.json");
        JsonTreeNode balanced = new ConfigurationComposer(TestData.Catalog).Compose(ContentSource.Preset("balanced"), ValidationMode.PresetBuild, new ValidationIssues()).Bundle;
        JsonTreeNode regulated = new ConfigurationComposer(TestData.Catalog).Compose(ContentSource.Preset("regulated"), ValidationMode.PresetBuild, new ValidationIssues()).Bundle;

        ReplayResult same = tool.Replay(record, "balanced export", new ReplayOptions { SnapshotBundle = balanced, SnapshotName = "balanced export" });
        ReplayResult other = tool.Replay(record, "regulated export", new ReplayOptions { SnapshotBundle = regulated, SnapshotName = "regulated export", VersionNumber = 4 });

        Assert.True(same.Status == ReplayStatus.Same, string.Join(" | ", same.Differences.Select(d => d.ToString())));
        Assert.Equal("balanced export", same.SnapshotSource);
        Assert.Contains("snapshot-hash", other.DifferenceKinds);
        Assert.Contains("configuration-version", other.DifferenceKinds);
    }

    // With ignoreMessages, a reworded reason is no difference; the code still is.
    [Fact]
    public void ReasonMessagesCanBeLeftOutOfTheComparison()
    {
        ReplayTool tool = ReplayTool.ForSourceRoot(TestData.Root);
        JsonTreeNode record = Read("op-14-number-alone.json");
        JsonTreeNode recorded = JsonNodes.Get(record, "recorded")!;
        JsonTreeNode reason = JsonNodes.Get(recorded, "reasons")!.Items[0];
        JsonTreeNode reworded = JsonNodes.With(record, "recorded", JsonNodes.With(recorded, "reasons", JsonTreeNode.CreateArray(JsonNodes.With(reason, "message", JsonTreeNode.FromString("Reworded.")))));

        Assert.Equal(new[] { "reasons" }, tool.Replay(reworded, "reworded").DifferenceKinds);
        Assert.Equal(ReplayStatus.Same, tool.Replay(reworded, "reworded", new ReplayOptions { IgnoreMessages = true }).Status);
    }

    // A record that does not match the schema, has no recorded result, sets the version in its context, or is named
    // after another case cannot be replayed.
    [Fact]
    public void AnUnusableRecordIsInvalid()
    {
        ReplayTool tool = ReplayTool.ForSourceRoot(TestData.Root);
        JsonTreeNode record = Read("op-04-enforce-stops.json");

        Assert.Equal(ReplayStatus.Invalid, tool.Replay(JsonNodes.Without(record, "recorded"), "no result").Status);
        Assert.Equal(ReplayStatus.Invalid, tool.Replay(JsonNodes.With(record, "unknownMember", JsonTreeNode.True), "unknown member").Status);
        Assert.Equal(ReplayStatus.Invalid, tool.Replay(JsonNodes.With(record, "operation", JsonTreeNode.FromString("OP-09")), "op-09").Status);
        JsonTreeNode context = JsonNodes.With(JsonNodes.Get(record, "context")!, "snapshotHash", JsonTreeNode.FromString(new string('a', 64)));
        Assert.Equal(ReplayStatus.Invalid, tool.Replay(JsonNodes.With(record, "context", context), "context hash").Status);
        Assert.Throws<ReplayRecordException>(() => tool.Capture(JsonNodes.Without(record, "request")));

        string folder = TestData.TempFolder();
        try
        {
            TestData.Write(folder, "another-name.json", File.ReadAllText(Path.Combine(ReplayFolder, "op-04-enforce-stops.json")));
            ReplayResult renamed = tool.ReplayFile(Path.Combine(folder, "another-name.json"));
            Assert.Equal(ReplayStatus.Invalid, renamed.Status);
            Assert.Contains(renamed.Problems, p => p.Contains("does not match the file name"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // The fixture runner skips replay folders, so fixtures/replay never runs as fixtures.
    [Fact]
    public void TheFixtureRunnerSkipsReplayRecords()
    {
        IReadOnlyList<FixtureResult> results = FixtureRunner.ForSourceRoot(TestData.Root).RunFolder(Path.Combine(TestData.Root, "fixtures"));

        Assert.DoesNotContain(results, r => r.File.StartsWith(ReplayTool.FolderName + "/", StringComparison.Ordinal));
    }

    private static JsonTreeNode Read(string name)
    {
        return CanonicalJson.Parse(File.ReadAllText(Path.Combine(ReplayFolder, name)));
    }

    // A fixture as a replay record: the same snapshot, the context without version or hash, and input plus facts as the
    // request.
    private static JsonTreeNode ToRecord(JsonTreeNode fixture)
    {
        JsonTreeNode context = JsonNodes.Without(JsonNodes.Without(JsonNodes.Get(fixture, "context")!, "configurationVersion"), "snapshotHash");
        JsonTreeNode input = JsonNodes.Get(fixture, "input") ?? JsonNodes.EmptyObject;
        JsonTreeNode facts = JsonNodes.Get(fixture, "facts") ?? JsonNodes.EmptyObject;
        return JsonTreeNode.CreateObject(
            JsonNodes.Member("schemaVersion", "1.0"),
            JsonNodes.Member("operation", JsonNodes.GetString(fixture, "operation")!),
            JsonNodes.Member("case", JsonNodes.GetString(fixture, "case")!),
            JsonNodes.Member("snapshot", JsonNodes.Get(fixture, "snapshot")!),
            JsonNodes.Member("context", context),
            JsonNodes.Member("request", JsonTreeNode.CreateObject(input.Members.Concat(facts.Members))));
    }
}
