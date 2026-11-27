using System;
using System.IO;
using System.Linq;
using Product.Engine.Configuration.Composition;
using Product.Engine.Configuration.Json;
using Product.Engine.Configuration.Model;
using Product.Engine.Configuration.Presets;
using Product.Engine.Configuration.Validation;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Xunit;

namespace Product.Engine.Configuration.Tests;

// Preset readers, extends, wildcard expansion, overlays, and byte-stable builds.
public sealed class PresetTests
{
    [Fact]
    public void BalancedLoadsAsAFullPresetWithoutProblems()
    {
        Assert.True(TestData.Catalog.TryGet("balanced", out PresetSource? balanced));
        Assert.Equal(PresetKind.Full, balanced.Kind);
        Assert.Null(balanced.Extends);
        Assert.Null(balanced.Patch);
        Assert.Empty(balanced.LoadIssues);
        // 34 entity files, and three the data taxonomy adds (D-37): classifications, domain levels and platforms.
        Assert.Equal(37, balanced.EntityFiles.Count);
        Assert.Empty(TestData.Catalog.Issues);
    }

    [Fact]
    public void TheFixtureOverlaysLoadAsOverlays()
    {
        Assert.True(TestData.Catalog.TryGet("fixture-overlay-a", out PresetSource? a));
        Assert.True(a.IsOverlay);
        Assert.Contains("fixture-overlay-b", a.ConflictsWith);
        Assert.NotNull(a.Patch);
        Assert.Empty(a.LoadIssues);
    }

    [Fact]
    public void BalancedBuildsIntoAFlatBundleWithWildcardsExpanded()
    {
        var issues = new ValidationIssues();
        ComposedConfiguration built = new ConfigurationComposer(TestData.Catalog).BuildPreset("balanced", issues);

        Assert.Empty(issues.Items);
        // 494 rows before the process alignment (document 21, B21-9) added 27, and 521 before the data taxonomy (D-37) added
        // 73: 4 classifications, 3 domain levels, 20 platforms, 5 data location link kinds, 4 lists with 25 values, 4 more
        // values in the vocabulary and tool lists, 4 label mappings, 2 settings, the data steward and the data governance lead.
        // Data taxonomy slice 2 added 53: the data-retention decision type with 4 outcomes, 18 approval rules, 17 decision
        // rights, 4 gate requirements and 9 statuses (draft, registered and archived for O25, O26 and O27). The leftovers
        // slice added 1: the consent of the business owner an intake names (document 25, B25-18). Wave 4 added 2: the
        // settings measure-targets and signal-thresholds (document 24, B24-22). Wave 4b added 1: the setting
        // review-request-due-days (document 24, B24-23).
        Assert.Equal(651, built.Content.Rows.Count);

        // Nine gate policy rows become 27: one per tier, change type and target stage (V23).
        ContentRow[] gates = built.Content.RowsFor("gate-policy").ToArray();
        Assert.Equal(27, gates.Length);
        Assert.DoesNotContain(gates, g => g.Node.ToString().Contains("\"*\""));

        // Explicit beats wildcard: emergencies in test come from gp-emergency-test, not from gp-test.
        Assert.True(built.Content.TryFindRow("gate-policy", "gp-emergency-test--tier-2", out ContentRow? emergencyTest));
        Assert.Equal("record", JsonNodes.GetString(emergencyTest.Node, "gateLevel"));
        Assert.False(built.Content.TryFindRow("gate-policy", "gp-test--tier-2--emergency", out _));
        Assert.True(built.Content.TryFindRow("gate-policy", "gp-test--tier-2--normal", out ContentRow? normalTest));
        Assert.Equal("flag", JsonNodes.GetString(normalTest.Node, "gateLevel"));

        // Review cadence: the two wildcard rows become one row per tier.
        Assert.True(built.Content.TryFindRow("review-cadence", "tier-check--tier-3", out _));
        Assert.Equal(12, built.Content.RowsFor("review-cadence").Count());
    }

    [Fact]
    public void TheBuiltBundleIsByteStableAndHashesCanonically()
    {
        var composer = new ConfigurationComposer(TestData.Catalog);
        ComposedConfiguration first = composer.BuildPreset("balanced", new ValidationIssues());
        ComposedConfiguration second = composer.BuildPreset("balanced", new ValidationIssues());

        string a = JsonText.WriteIndented(first.Bundle, snapshotOrder: true);
        string b = JsonText.WriteIndented(second.Bundle, snapshotOrder: true);
        Assert.Equal(a, b);

        // The indented file parses back to the same canonical content.
        Assert.Equal(CanonicalHash.ComputeContentHash(first.Bundle), CanonicalHash.ComputeContentHash(a));
        Assert.Equal(CanonicalHash.ComputeContentHash(first.Bundle), TestData.Balanced(ValidationMode.PresetBuild).Report.ContentHash);
    }

    [Fact]
    public void APresetWithExtendsChangesAWildcardRowBeforeExpansion()
    {
        string folder = TestData.TempFolder();
        try
        {
            string child = Path.Combine(folder, "fixture-strict-test");
            TestData.Write(child, "preset.json", "{\"schemaVersion\":\"1.0\",\"presetKey\":\"fixture-strict-test\",\"label\":\"Strict test\",\"presetKind\":\"full\",\"extends\":\"balanced\",\"conflictsWith\":[],\"areas\":[\"gates\"],\"productRelease\":\"0.1.0\",\"contentVersion\":\"1.0.0\"}");
            TestData.Write(child, "patch.json", "{\"schemaVersion\":\"1.0\",\"manifest\":{\"kind\":\"patch\",\"presetKey\":\"fixture-strict-test\",\"presetKind\":\"full\",\"extends\":\"balanced\"},\"operations\":[{\"op\":\"upsert\",\"entity\":\"gate-policy\",\"key\":\"gp-test\",\"values\":{\"gateLevel\":\"advise\",\"label\":\"Test: advise\"}}]}");

            PresetCatalog catalog = PresetCatalog.Load(new[] { Path.Combine(TestData.Root, "config", "presets"), folder });
            Assert.Empty(catalog.Presets.SelectMany(p => p.LoadIssues));

            var validator = new ConfigurationValidator(catalog);
            ValidationOutcome outcome = validator.Validate(new ValidationRequest(ContentSource.Preset("fixture-strict-test"), ValidationMode.PresetBuild));
            Assert.True(outcome.Report.IsValid, string.Join("\n", outcome.Report.Issues));

            // Every expanded test row of gp-test takes the child's level. Emergencies keep their own row.
            ContentRow[] test = outcome.Candidate!.Content.RowsFor("gate-policy").Where(r => r.Key!.StartsWith("gp-test--", StringComparison.Ordinal)).ToArray();
            Assert.Equal(6, test.Length);
            Assert.All(test, r => Assert.Equal("advise", JsonNodes.GetString(r.Node, "gateLevel")));
            Assert.Equal("fixture-strict-test", JsonNodes.GetString(JsonNodes.Get(outcome.Candidate.Bundle, "manifest")!, "presetKey"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AnExtendsLoopIsReported()
    {
        string folder = TestData.TempFolder();
        try
        {
            foreach (string pair in new[] { "loop-a:loop-b", "loop-b:loop-a" })
            {
                string key = pair.Split(':')[0];
                string parent = pair.Split(':')[1];
                string dir = Path.Combine(folder, key);
                TestData.Write(dir, "preset.json", "{\"schemaVersion\":\"1.0\",\"presetKey\":\"" + key + "\",\"label\":\"Loop\",\"presetKind\":\"full\",\"extends\":\"" + parent + "\",\"conflictsWith\":[],\"areas\":[\"gates\"],\"productRelease\":\"0.1.0\",\"contentVersion\":\"1.0.0\"}");
                TestData.Write(dir, "patch.json", "{\"schemaVersion\":\"1.0\",\"manifest\":{\"kind\":\"patch\",\"presetKey\":\"" + key + "\",\"presetKind\":\"full\",\"extends\":\"" + parent + "\"},\"operations\":[{\"op\":\"retire\",\"entity\":\"tier\",\"key\":\"tier-1\"}]}");
            }

            var validator = new ConfigurationValidator(PresetCatalog.Load(new[] { folder }));
            ValidationOutcome outcome = validator.Validate(new ValidationRequest(ContentSource.Preset("loop-a"), ValidationMode.PresetBuild));
            Assert.Equal("invalid", outcome.Outcome);
            Assert.Contains(outcome.Report.Issues, i => i.RuleId == "V06" && i.Message.Contains("loops"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AnOverlayWithEntityFilesIsAShapeProblem()
    {
        string folder = TestData.TempFolder();
        try
        {
            string dir = Path.Combine(folder, "fixture-bad-overlay");
            TestData.Write(dir, "preset.json", "{\"schemaVersion\":\"1.0\",\"presetKey\":\"fixture-bad-overlay\",\"label\":\"Bad\",\"presetKind\":\"overlay\",\"extends\":null,\"conflictsWith\":[],\"areas\":[\"people\"],\"productRelease\":\"0.1.0\",\"contentVersion\":\"1.0.0\"}");
            TestData.Write(dir, "tier.json", "{\"entity\":\"tier\",\"rows\":[{\"key\":\"tier-9\",\"label\":\"Tier 9\",\"rank\":9}]}");

            PresetSource preset = PresetCatalog.LoadFolder(dir);
            Assert.Contains(preset.LoadIssues, i => i.RuleId == "V01" && i.Message.Contains("needs patch.json"));
            Assert.Contains(preset.LoadIssues, i => i.RuleId == "V01" && i.Message.Contains("holds only patch.json"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AWrongEntityFileNameAndABadRowAreReportedWithTheirFile()
    {
        string folder = TestData.TempFolder();
        try
        {
            string dir = Path.Combine(folder, "fixture-small");
            TestData.Write(dir, "preset.json", "{\"schemaVersion\":\"1.0\",\"presetKey\":\"fixture-small\",\"label\":\"Small\",\"presetKind\":\"full\",\"extends\":null,\"conflictsWith\":[],\"areas\":[\"taxonomy\"],\"productRelease\":\"0.1.0\",\"contentVersion\":\"1.0.0\"}");
            TestData.Write(dir, "tiers.json", "{\"entity\":\"tier\",\"rows\":[{\"key\":\"tier-1\",\"label\":\"Tier 1\"}]}");

            PresetSource preset = PresetCatalog.LoadFolder(dir);
            Assert.Contains(preset.LoadIssues, i => i.Message.Contains("must be named `tier.json`"));
            ValidationIssue missingRank = Assert.Single(preset.LoadIssues, i => i.Message.Contains("\"rank\""));
            Assert.Equal("Tier", missingRank.Entity);
            Assert.Equal("tier-1", missingRank.Ref);
            Assert.Contains("fixture-small/tiers.json", missingRank.Message);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void OverlaysApplyInOrderAndAreRecordedInTheManifest()
    {
        ValidationOutcome outcome = TestData.Balanced(ValidationMode.Draft, overlays: new[] { "fixture-overlay-a", "fixture-overlay-c" });
        Assert.Equal("valid", outcome.Outcome);
        Assert.True(outcome.Candidate!.Content.TryFindRow("guideline", "g1", out ContentRow? g1));
        Assert.Equal("Fixture overlay A", JsonNodes.GetString(g1.Node, "chapter"));

        JsonTreeNode manifest = JsonNodes.Get(outcome.Candidate.Bundle, "manifest")!;
        Assert.Equal("balanced", JsonNodes.GetString(manifest, "basedOnPresetKey"));
        string[] applied = JsonNodes.Get(manifest, "appliedPresets")!.Items.Select(i => JsonNodes.GetString(i, "presetKey")!).ToArray();
        Assert.Equal(new[] { "balanced", "fixture-overlay-a", "fixture-overlay-c" }, applied);
    }

    [Fact]
    public void AnUnknownPresetOrAnOverlayAsTheFullPresetIsInvalidInput()
    {
        ValidationOutcome unknown = TestData.Validator.Validate(new ValidationRequest(ContentSource.Preset("no-such-preset"), ValidationMode.PresetBuild));
        Assert.Equal("invalid-input", unknown.Outcome);
        Assert.Equal("input.unknown-key", unknown.InputProblem!.Code);

        ValidationOutcome overlay = TestData.Validator.Validate(new ValidationRequest(ContentSource.Preset("fixture-overlay-a"), ValidationMode.PresetBuild));
        Assert.Equal("invalid-input", overlay.Outcome);
        Assert.Equal("input.malformed", overlay.InputProblem!.Code);
    }
}
