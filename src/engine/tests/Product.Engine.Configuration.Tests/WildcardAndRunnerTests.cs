using System;
using System.IO;
using System.Linq;
using Product.Engine.Configuration.Composition;
using Product.Engine.Configuration.Json;
using Product.Engine.Configuration.Model;
using Product.Engine.Configuration.Validation;
using Product.Engine.Core.Canonical;
using Product.Engine.Testing;
using Xunit;

namespace Product.Engine.Configuration.Tests;

// Wildcard expansion details, and the fixture runner over src/fixtures.
public sealed class WildcardAndRunnerTests
{
    private const string SmallSet = "{\"schemaVersion\":\"1.0\",\"manifest\":{\"kind\":\"bundle\"},\"entities\":{"
        + "\"tier\":[{\"key\":\"tier-1\",\"label\":\"Tier 1\",\"rank\":1},{\"key\":\"tier-2\",\"label\":\"Tier 2\",\"rank\":2},{\"key\":\"tier-0\",\"label\":\"Old\",\"rank\":0,\"isActive\":false}],"
        + "\"review-type\":[{\"key\":\"owner-check\",\"label\":\"Owner check\"},{\"key\":\"tier-check\",\"label\":\"Tier check\"}],"
        + "\"review-cadence\":[";

    private static ValidationOutcome Cadences(string rows)
    {
        JsonTreeNode bundle = CanonicalJson.Parse(SmallSet + rows + "]}}");
        return TestData.Validator.Validate(new ValidationRequest(ContentSource.Bundle(bundle, "test bundle"), ValidationMode.Draft));
    }

    [Fact]
    public void AWildcardExpandsOncePerActiveTarget()
    {
        ValidationOutcome outcome = Cadences("{\"key\":\"tc\",\"label\":\"Tier check\",\"reviewTypeKey\":\"tier-check\",\"tierKey\":\"*\",\"intervalDays\":365}");
        string[] keys = outcome.Candidate!.Content.RowsFor("review-cadence").Select(r => r.Key!).OrderBy(k => k, StringComparer.Ordinal).ToArray();

        // The retired tier-0 gets no row.
        Assert.Equal(new[] { "tc--tier-1", "tc--tier-2" }, keys);
        Assert.Empty(TestData.Issues(outcome, "V09"));
    }

    [Fact]
    public void AnExplicitRowBeatsAWildcardOnlyWithinItsScope()
    {
        ValidationOutcome outcome = Cadences(
            "{\"key\":\"oc\",\"label\":\"Owner check\",\"reviewTypeKey\":\"owner-check\",\"tierKey\":\"*\",\"intervalDays\":365},"
            + "{\"key\":\"oc-high\",\"label\":\"Owner check, tier 2\",\"reviewTypeKey\":\"owner-check\",\"tierKey\":\"tier-2\",\"intervalDays\":90},"
            + "{\"key\":\"tc\",\"label\":\"Tier check\",\"reviewTypeKey\":\"tier-check\",\"tierKey\":\"*\",\"intervalDays\":365}");
        string[] keys = outcome.Candidate!.Content.RowsFor("review-cadence").Select(r => r.Key!).OrderBy(k => k, StringComparer.Ordinal).ToArray();

        // owner-check at tier 2 comes from the explicit row. The tier check, another review type, still covers both tiers.
        Assert.Equal(new[] { "oc--tier-1", "oc-high", "tc--tier-1", "tc--tier-2" }, keys);
        Assert.Empty(TestData.Issues(outcome, "V09"));
    }

    [Fact]
    public void TwoEquallyExplicitRowsForOneCombinationFail()
    {
        ValidationOutcome outcome = Cadences(
            "{\"key\":\"oc-a\",\"label\":\"A\",\"reviewTypeKey\":\"owner-check\",\"tierKey\":\"*\",\"intervalDays\":365},"
            + "{\"key\":\"oc-b\",\"label\":\"B\",\"reviewTypeKey\":\"owner-check\",\"tierKey\":\"*\",\"intervalDays\":180}");
        ValidationIssue issue = Assert.Single(TestData.Issues(outcome, "V09"));
        Assert.Contains("`oc-a` and `oc-b`", issue.Message);
        Assert.Contains("tier `tier-1`", issue.Message);
    }

    [Fact]
    public void ARetiredWildcardRowExpandsIntoRetiredRowsSoItsKeysStayTaken()
    {
        ValidationOutcome outcome = Cadences("{\"key\":\"old\",\"label\":\"Old\",\"reviewTypeKey\":\"tier-check\",\"tierKey\":\"*\",\"intervalDays\":365,\"isActive\":false}");
        ContentRow[] rows = outcome.Candidate!.Content.RowsFor("review-cadence").ToArray();
        Assert.Equal(2, rows.Length);
        Assert.All(rows, r => Assert.False(r.IsActive));
    }

    [Fact]
    public void ExpandingAnExpandedSetChangesNothing()
    {
        ComposedConfiguration built = new ConfigurationComposer(TestData.Catalog).BuildPreset("balanced", new ValidationIssues());
        ValidationOutcome again = TestData.Validator.Validate(new ValidationRequest(ContentSource.Bundle(built.Bundle, "built"), ValidationMode.PresetBuild));
        Assert.Equal("valid", again.Outcome);
        Assert.Equal(CanonicalHash.ComputeContentHash(built.Bundle), again.Report.ContentHash);
    }

    [Fact]
    public void EveryFixtureInSrcPasses()
    {
        FixtureRunner runner = FixtureRunner.ForSourceRoot(TestData.Root);
        var results = runner.RunFolder(Path.Combine(TestData.Root, "fixtures")).ToList();

        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.True(r.Status == FixtureStatus.Passed, r.File + ": " + string.Join(" | ", r.Problems)));
    }

    [Fact]
    public void EveryRuleHasAPassingAndAFailingFixture()
    {
        FixtureRunner runner = FixtureRunner.ForSourceRoot(TestData.Root);
        var results = runner.RunFolder(Path.Combine(TestData.Root, "fixtures", "op-09")).ToList();
        foreach (ValidationRule rule in ConfigurationValidator.Rules)
        {
            string id = rule.RuleId;
            if (rule.Severity == Core.Contract.ReasonSeverity.Warning)
            {
                // A warning never makes a set invalid. Its failing case reports the warning; its passing case does not.
                Assert.True(results.Any(r => r.CoveredRuleIds.Contains(id) && r.ActualRuleIds.Contains(id)), "no fixture reports the warning " + id);
                Assert.True(results.Any(r => r.ActualOutcome == "valid" && r.CoveredRuleIds.Contains(id) && !r.ActualRuleIds.Contains(id)), "no passing fixture for " + id);
                continue;
            }

            Assert.True(results.Any(r => r.ActualOutcome == "invalid" && r.CoveredRuleIds.Contains(id) && r.ActualRuleIds.Contains(id)), "no failing fixture for " + id);
            Assert.True(results.Any(r => r.ActualOutcome == "valid" && (r.CoveredRuleIds.Contains(id) || r.Case == "balanced-valid")), "no passing fixture for " + id);
        }
    }

    [Fact]
    public void TheExampleFixturesRun()
    {
        FixtureRunner runner = FixtureRunner.ForSourceRoot(TestData.Root);
        var results = runner.RunFolder(Path.Combine(TestData.Root, "config", "schema", "examples")).ToList();

        // OP-02 is built, so its two examples run and pass like the OP-09 ones.
        Assert.Equal(4, results.Count);
        Assert.Equal(2, results.Count(r => r.Status == FixtureStatus.Passed && r.Operation == "OP-09"));
        Assert.All(results.Where(r => r.Operation == "OP-02"), r => Assert.True(r.Status == FixtureStatus.Passed, r.File + ": " + string.Join(" | ", r.Problems)));
        Assert.Equal(2, results.Count(r => r.Operation == "OP-02"));
    }

    [Fact]
    public void EveryOperationIsBuiltSoNoFixtureIsSkipped()
    {
        string folder = TestData.TempFolder();
        try
        {
            // OP-08 was the last operation built. A fixture of it now runs: this one is judged, and it fails,
            // because the schema needs an input (it used to be skipped as not built).
            TestData.Write(folder, "later.json", "{ \"schemaVersion\": \"1.0\", \"operation\": \"OP-08\", \"case\": \"later\", \"snapshot\": { \"presetKey\": \"balanced\" }, \"context\": { \"asOfUtc\": \"2026-11-22T09:00:00Z\" }, \"expected\": { \"outcome\": \"allowed\" } }");
            FixtureResult result = FixtureRunner.ForSourceRoot(TestData.Root).RunFile(Path.Combine(folder, "later.json"));
            Assert.Equal(FixtureStatus.Failed, result.Status);
            Assert.Contains(result.Problems, p => p.Contains("fixture schema"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }

        // EveryFixtureInSrcPasses demands Passed for every fixture, so a skipped one would fail there.
    }

    [Fact]
    public void AFixtureWithAWrongExpectationFails()
    {
        string folder = TestData.TempFolder();
        try
        {
            string text = File.ReadAllText(Path.Combine(TestData.Root, "fixtures", "op-09", "v12-duplicate-rank.json"))
                .Replace("\"case\": \"v12-duplicate-rank\"", "\"case\": \"wrong\"")
                .Replace("\"tier.rank-duplicate\"", "\"tier.none-active\"");
            TestData.Write(folder, "wrong.json", text);

            FixtureResult result = FixtureRunner.ForSourceRoot(TestData.Root).RunFile(Path.Combine(folder, "wrong.json"));
            Assert.Equal(FixtureStatus.Failed, result.Status);
            Assert.Contains(result.Problems, p => p.Contains("tier.none-active"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AFixtureThatBreaksTheFixtureSchemaFails()
    {
        string folder = TestData.TempFolder();
        try
        {
            TestData.Write(folder, "bad.json", "{\"schemaVersion\":\"1.0\",\"operation\":\"OP-09\",\"case\":\"bad\"}");
            FixtureResult result = FixtureRunner.ForSourceRoot(TestData.Root).RunFile(Path.Combine(folder, "bad.json"));
            Assert.Equal(FixtureStatus.Failed, result.Status);
            Assert.Contains(result.Problems, p => p.StartsWith("fixture schema:", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ThePrettyWriterIsStableJson()
    {
        JsonTreeNode node = CanonicalJson.Parse("{\"b\":[1,2],\"a\":{\"entities\":[]},\"entities\":{\"tier\":[{\"key\":\"z\"},{\"key\":\"a\"}]}}");
        string text = JsonText.WriteIndented(node, snapshotOrder: true);
        Assert.Equal(CanonicalJson.Write(node, CanonicalProfile.Snapshot), CanonicalJson.Write(CanonicalJson.Parse(text), CanonicalProfile.Snapshot));
        Assert.True(text.IndexOf("\"key\": \"a\"", StringComparison.Ordinal) < text.IndexOf("\"key\": \"z\"", StringComparison.Ordinal));
        Assert.EndsWith("\n", text);
        Assert.DoesNotContain("\r", text);
    }
}
