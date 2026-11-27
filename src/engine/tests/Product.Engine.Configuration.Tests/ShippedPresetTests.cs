using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Composition;
using Product.Engine.Configuration.Json;
using Product.Engine.Configuration.Presets;
using Product.Engine.Configuration.Validation;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Testing;
using Xunit;

namespace Product.Engine.Configuration.Tests;

// Every shipped full preset, alone and with each allowed overlay combination, passes OP-09.
// The selections come from PresetCatalog.AllowedOverlayCombinations, the same list build-presets checks.
// So these tests cover the preset build on every test target, also where the command line cannot run.
// Only config/presets is loaded here. The fixture-only presets stay out.
public sealed class ShippedPresetTests
{
    private static readonly Lazy<PresetCatalog> LazyShipped = new(() => PresetCatalog.Load(new[] { SourceRoot.PresetsFolder(TestData.Root) }));

    private static readonly ValidationFacts Facts = new(validatedOn: new DateTime(2026, 11, 22), baseVersionNumber: 1, activeVersionNumber: 1, approvalRulesVersionNumber: 1);

    private static readonly ValidationMode[] Modes = { ValidationMode.PresetBuild, ValidationMode.Draft, ValidationMode.Activation };

    private static PresetCatalog Shipped => LazyShipped.Value;

    // One row per selection: the full preset key and its overlay keys joined with "+" (empty for the preset alone).
    public static TheoryData<string, string> Selections()
    {
        var data = new TheoryData<string, string>();
        foreach (PresetSource full in Shipped.FullPresets)
        {
            data.Add(full.Key, string.Empty);
            foreach (string[] overlays in Shipped.AllowedOverlayCombinations(full))
            {
                data.Add(full.Key, string.Join("+", overlays));
            }
        }

        return data;
    }

    private static ValidationOutcome Validate(string full, IEnumerable<string> overlays, ValidationMode mode)
    {
        var validator = new ConfigurationValidator(Shipped);
        return validator.Validate(new ValidationRequest(ContentSource.Preset(full, overlays), mode, facts: Facts));
    }

    private static string[] Split(string overlays)
    {
        return overlays.Length == 0 ? new string[0] : overlays.Split('+');
    }

    [Fact]
    public void TheShippedCatalogHoldsFourFullPresetsAndFourOverlays()
    {
        Assert.Empty(Shipped.Issues);
        Assert.All(Shipped.Presets, p => Assert.True(p.LoadIssues.Count == 0, p.Key + ": " + string.Join(" | ", p.LoadIssues)));
        Assert.Equal(new[] { "balanced", "microsoft-coe-baseline", "regulated", "starter" }, Shipped.FullPresets.Select(p => p.Key).ToArray());
        Assert.Equal(new[] { "data-domains-starter", "linked-change-management", "small-team", "strict-separation" }, Shipped.Overlays.Select(p => p.Key).ToArray());

        // Balanced is canonical. The other three full presets are written as changes to it (document 21, section 6.2).
        Assert.All(Shipped.FullPresets.Where(p => p.Key != "balanced"), p => Assert.Equal("balanced", p.Extends));
    }

    [Fact]
    public void TheAllowedSelectionsLeaveOutEveryConflict()
    {
        string[][] selections = Shipped.FullPresets
            .SelectMany(full => new[] { new[] { full.Key } }.Concat(Shipped.AllowedOverlayCombinations(full).Select(o => new[] { full.Key }.Concat(o).ToArray())))
            .ToArray();

        // 4 presets alone, 11 overlay sets for each of Balanced, Starter and the CoE baseline, 7 for Regulated: 22 before the
        // data domains starter overlay (D-37), which conflicts with nothing, so it doubles every set.
        Assert.Equal(44, selections.Length);
        Assert.Equal(22, selections.Count(s => s.Contains("data-domains-starter")));

        // Small team never meets strict separation (document 21, sections 6.5 and 6.6; V43).
        Assert.DoesNotContain(selections, s => s.Contains("small-team") && s.Contains("strict-separation"));

        // Regulated lists small team in conflictsWith. A proposal, see regulated/preset.json.
        Assert.DoesNotContain(selections, s => s.Contains("regulated") && s.Contains("small-team"));

        // The examples of document 21, section 6.7 are all allowed.
        Assert.Contains(selections, s => s.SequenceEqual(new[] { "regulated", "strict-separation" }));
        Assert.Contains(selections, s => s.SequenceEqual(new[] { "starter", "small-team" }));
        Assert.Contains(selections, s => s.SequenceEqual(new[] { "microsoft-coe-baseline" }));
    }

    // The only warnings a shipped selection may give. The CoE baseline sets the quality review only at tier 2 and the
    // maturity review only at tier 3 (document 21, section 6.4), so V29 warns about both. A warning never blocks a build.
    private static string[] ExpectedWarnings(string full)
    {
        return full == "microsoft-coe-baseline"
            ? new[] { "V29 Review Type maturity-review", "V29 Review Type quality-review" }
            : new string[0];
    }

    [Theory]
    [MemberData(nameof(Selections))]
    public void EverySelectionHasNoErrorInEveryMode(string full, string overlays)
    {
        foreach (ValidationMode mode in Modes)
        {
            ValidationOutcome outcome = Validate(full, Split(overlays), mode);
            string where = full + (overlays.Length == 0 ? string.Empty : " + " + overlays.Replace("+", " + ")) + " in " + mode;
            Assert.True(outcome.InputProblem == null, where + ": " + outcome.InputProblem?.Message);

            ValidationIssue[] errors = outcome.Report.Issues.Where(i => i.Severity == ReasonSeverity.Error).ToArray();
            Assert.True(errors.Length == 0, where + ": " + string.Join(" | ", errors.Select(i => i.Message)));
            Assert.Equal("valid", outcome.Outcome);

            string[] warnings = outcome.Report.Issues
                .Where(i => i.Severity == ReasonSeverity.Warning)
                .Select(i => i.RuleId + " " + i.Entity + " " + i.Ref)
                .OrderBy(w => w, StringComparer.Ordinal)
                .ToArray();
            Assert.True(ExpectedWarnings(full).SequenceEqual(warnings), where + ": warnings " + string.Join(" | ", warnings));
        }
    }

    [Theory]
    [InlineData("balanced", "small-team+strict-separation", "strict-separation")]
    [InlineData("starter", "strict-separation+small-team", "small-team")]
    [InlineData("regulated", "small-team", "small-team")]
    public void AConflictingSelectionIsRefusedByV43(string full, string overlays, string named)
    {
        ValidationOutcome outcome = Validate(full, Split(overlays), ValidationMode.PresetBuild);

        Assert.Equal("invalid", outcome.Outcome);
        ValidationIssue[] hits = outcome.Report.Issues.Where(i => i.RuleId == "V43").ToArray();
        Assert.NotEmpty(hits);
        Assert.Contains(hits, h => h.Ref == named && h.Message.StartsWith("V43: ", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryFullPresetBuildsToAByteStableBundle()
    {
        var composer = new ConfigurationComposer(Shipped);
        foreach (PresetSource full in Shipped.FullPresets)
        {
            var issues = new ValidationIssues();
            ComposedConfiguration first = composer.BuildPreset(full.Key, issues);
            ComposedConfiguration second = composer.BuildPreset(full.Key, new ValidationIssues());
            Assert.True(issues.Items.Count == 0, full.Key + ": " + string.Join(" | ", issues.Items.Select(i => i.Message)));

            string a = JsonText.WriteIndented(first.Bundle, snapshotOrder: true);
            string b = JsonText.WriteIndented(second.Bundle, snapshotOrder: true);
            Assert.Equal(a, b);
            Assert.Equal(CanonicalHash.ComputeContentHash(first.Bundle), CanonicalHash.ComputeContentHash(a));

            // The built bundle names its own preset, and no wildcard survives the build.
            Assert.Equal(full.Key, JsonNodes.GetString(JsonNodes.Get(first.Bundle, "manifest")!, "presetKey"));
            Assert.DoesNotContain("\"*\"", a, StringComparison.Ordinal);
        }
    }
}
