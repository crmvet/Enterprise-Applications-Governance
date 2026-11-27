using System;
using System.Linq;
using Product.Engine.Configuration.Composition;
using Product.Engine.Configuration.Json;
using Product.Engine.Configuration.Model;
using Product.Engine.Configuration.Patches;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Configuration.Validation;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;
using Product.Engine.Core.Snapshots;
using Xunit;

namespace Product.Engine.Configuration.Tests;

// Patch apply by stable key, the client binding patch, the snapshot compiler and the diff.
public sealed class PatchAndSnapshotTests
{
    private const string BindingPatch = "{\"schemaVersion\":\"1.0\",\"manifest\":{\"kind\":\"patch\",\"baseVersionNumber\":1,\"summary\":\"Bind the configuration admins\"},"
        + "\"operations\":[{\"op\":\"upsert\",\"entity\":\"role-group-binding\",\"key\":\"configuration-admins\",\"values\":{\"label\":\"Configuration admins\","
        + "\"roleKey\":\"configuration-admin\",\"entraGroupObjectId\":\"00000000-0000-4000-8000-000000000001\",\"bindingMethod\":\"entra-app-role-assignment\","
        + "\"bindingDetail\":\"App role\"}}]}";

    [Fact]
    public void AnUpsertMergesAndKeepsFieldsItDoesNotName()
    {
        ValidationOutcome outcome = TestData.Balanced(ValidationMode.Draft, "[{\"op\":\"upsert\",\"entity\":\"tier\",\"key\":\"tier-2\",\"values\":{\"label\":\"Tier two\"}}]");
        Assert.Equal("valid", outcome.Outcome);
        Assert.True(outcome.Candidate!.Content.TryFindRow("tier", "tier-2", out ContentRow? tier));
        Assert.Equal("Tier two", JsonNodes.GetString(tier.Node, "label"));
        Assert.NotNull(JsonNodes.Get(tier.Node, "rank"));
        Assert.Equal(new[] { "Changed Tier `tier-2`: label." }, outcome.Report.ChangeSummary);
    }

    [Fact]
    public void AChildRowIsAddressedByItsKeyPath()
    {
        ValidationOutcome outcome = TestData.Balanced(
            ValidationMode.Draft,
            "[{\"op\":\"upsert\",\"entity\":\"list-value\",\"key\":\"channel/sms\",\"values\":{\"label\":\"Text message\"}},"
            + "{\"op\":\"retire\",\"entity\":\"list-value\",\"key\":\"channel/in-app\"}]");

        Assert.Equal("valid", outcome.Outcome);
        Assert.True(outcome.Candidate!.Content.TryFindRow("list-value", "channel/sms", out ContentRow? sms));
        Assert.Equal("channel", sms.ParentKey);
        Assert.True(outcome.Candidate.Content.TryFindRow("list-value", "channel/in-app", out ContentRow? inApp));
        Assert.False(inApp.IsActive);
        Assert.Contains("Added List Value `channel/sms`.", outcome.Report.ChangeSummary);
        Assert.Contains("Retired List Value `channel/in-app`.", outcome.Report.ChangeSummary);
    }

    [Fact]
    public void AChildArrayInUpsertValuesMergesByKeyAndDropsNothing()
    {
        ValidationOutcome outcome = TestData.Balanced(
            ValidationMode.Draft,
            "[{\"op\":\"upsert\",\"entity\":\"value-list\",\"key\":\"channel\",\"values\":{\"values\":[{\"key\":\"teams\",\"label\":\"Microsoft Teams chat\"}]}}]");

        Assert.Equal("valid", outcome.Outcome);
        ContentRow[] channels = outcome.Candidate!.Content.RowsFor("list-value").Where(r => r.ParentKey == "channel").ToArray();
        Assert.Equal(3, channels.Length);
        Assert.Equal("Microsoft Teams chat", JsonNodes.GetString(channels.Single(r => r.Key == "teams").Node, "label"));
    }

    [Fact]
    public void ARowAClientChangeTouchesBecomesLocal()
    {
        ValidationOutcome outcome = TestData.Balanced(ValidationMode.Draft, "[{\"op\":\"upsert\",\"entity\":\"tier\",\"key\":\"tier-3\",\"values\":{\"label\":\"Critical\"}}]");
        ConfigurationContent content = outcome.Candidate!.Content;
        Assert.True(content.TryFindRow("tier", "tier-3", out ContentRow? touched));
        Assert.True(content.TryFindRow("tier", "tier-1", out ContentRow? untouched));
        Assert.Equal("local", JsonNodes.GetString(touched.Node, "origin"));
        Assert.Equal("shipped", JsonNodes.GetString(untouched.Node, "origin"));
    }

    [Fact]
    public void APresetBuildCarriesNoOrigin()
    {
        ValidationOutcome outcome = TestData.Balanced(ValidationMode.PresetBuild);
        Assert.DoesNotContain(outcome.Candidate!.Content.Rows, r => JsonNodes.HasMember(r.Node, "origin"));
    }

    [Fact]
    public void AClientBindingPatchAppliesByStableKey()
    {
        // WORKING DEFAULT (open design question 12): people reach roles only through groups; workload grants are separate rows.
        ValidationOutcome outcome = TestData.BalancedWithPatch(BindingPatch, PatchKind.ClientBinding);
        Assert.True(outcome.Report.IsValid, string.Join("\n", outcome.Report.Issues));
        Assert.True(outcome.Candidate!.Content.TryFindRow("role-group-binding", "configuration-admins", out ContentRow? binding));
        Assert.Equal("local", JsonNodes.GetString(binding.Node, "origin"));
        Assert.Equal(new[] { "Added Role Group Binding `configuration-admins`." }, outcome.Report.ChangeSummary);

        // Applying the same patch twice gives the same content: upsert by key is idempotent.
        ConfigurationPatch patch = ConfigurationPatch.FromDocument(CanonicalJson.Parse(BindingPatch), PatchKind.ClientBinding, "twice");
        ValidationOutcome twice = TestData.Validator.Validate(new ValidationRequest(ContentSource.Preset("balanced").WithPatch(patch).WithPatch(patch), ValidationMode.Draft));
        Assert.Equal(outcome.Report.ContentHash, twice.Report.ContentHash);
    }

    [Fact]
    public void BalancedCompilesToAnImmutableSnapshot()
    {
        ValidationOutcome outcome = TestData.Validator.Validate(new ValidationRequest(ContentSource.Preset("balanced"), ValidationMode.Draft, configurationSetKey: "fixture-set", versionNumber: 3));
        CompiledSnapshot compiled = outcome.Snapshot!;

        Assert.Equal(3, compiled.VersionNumber);
        Assert.Equal("fixture-set", compiled.ConfigurationSetKey);
        Assert.Equal(outcome.Report.ContentHash, compiled.ContentHash);
        Assert.Equal(CanonicalHash.ComputeContentHash(compiled.CanonicalText), compiled.ContentHash);
        Assert.Equal(compiled.ContentHash, compiled.Snapshot.ContentHash);

        // 37 entities plus the three child entities, which the core model keeps apart. Balanced holds no data domains, so the
        // snapshot has no Data Domain entity.
        Assert.Equal(40, compiled.Snapshot.Entities.Count);
        Assert.True(compiled.Snapshot.TryGetRow("list-value", "channel/teams", out SnapshotRow? teams));
        Assert.Equal("channel", teams.ParentKey);
        Assert.Equal(Origin.Shipped, teams.Origin);
        Assert.True(compiled.Snapshot.TryGetRow("gate-policy", "gp-prod-tier-3--normal", out SnapshotRow? enforce));
        Assert.True(enforce.TryGetColumn("gateLevel", out JsonTreeNode? level));
        Assert.Equal("enforce", level.GetString());
        Assert.False(enforce.TryGetColumn("key", out _));
        Assert.True(compiled.Snapshot.TryGetRow("actor-kind", "person", out SnapshotRow? person));
        Assert.Equal("person", person.EngineMeaning);
    }

    [Fact]
    public void TheVersionNumberStaysOutOfTheHash()
    {
        // A revert reproduces the hash of the version it returns to.
        ValidationOutcome one = TestData.Validator.Validate(new ValidationRequest(ContentSource.Preset("balanced"), ValidationMode.Draft, versionNumber: 1));
        ValidationOutcome nine = TestData.Validator.Validate(new ValidationRequest(ContentSource.Preset("balanced"), ValidationMode.Draft, versionNumber: 9));
        Assert.Equal(one.Snapshot!.ContentHash, nine.Snapshot!.ContentHash);
        Assert.NotEqual(one.Snapshot.VersionNumber, nine.Snapshot.VersionNumber);
    }

    [Fact]
    public void ASnapshotOfAnUnknownSchemaMajorIsRefused()
    {
        JsonTreeNode bundle = CanonicalJson.Parse("{\"schemaVersion\":\"2.0\",\"manifest\":{\"kind\":\"bundle\"},\"entities\":{}}");
        SnapshotCompileException e = Assert.Throws<SnapshotCompileException>(() => SnapshotCompiler.Compile(bundle, 1, "fixture-set"));
        Assert.Equal(ReasonCodes.SnapshotSchemaUnsupported, e.Code);
    }

    [Fact]
    public void ASnapshotWithRepeatedKeysDoesNotCompile()
    {
        JsonTreeNode bundle = CanonicalJson.Parse("{\"schemaVersion\":\"1.0\",\"manifest\":{\"kind\":\"bundle\"},\"entities\":{\"tier\":[{\"key\":\"tier-1\",\"label\":\"A\",\"rank\":1},{\"key\":\"tier-1\",\"label\":\"B\",\"rank\":2}]}}");
        SnapshotCompileException e = Assert.Throws<SnapshotCompileException>(() => SnapshotCompiler.Compile(bundle, 1, "fixture-set"));
        Assert.Equal(ReasonCodes.InputMalformed, e.Code);
    }

    [Fact]
    public void AnInvalidWorkingSetHasNoCompiledSnapshot()
    {
        ValidationOutcome outcome = TestData.Balanced(ValidationMode.Draft, "[{\"op\":\"upsert\",\"entity\":\"tier\",\"key\":\"tier-2\",\"values\":{\"rank\":1}}]");
        Assert.Equal("invalid", outcome.Outcome);
        Assert.Null(outcome.Snapshot);
    }

    [Fact]
    public void TheReportMatchesItsSchema()
    {
        ValidationOutcome outcome = TestData.Balanced(ValidationMode.Draft, "[{\"op\":\"retire\",\"entity\":\"document-type\",\"key\":\"runbook\"}]");
        var result = Schema.ConfigurationSchemas.Set.Evaluate(outcome.Report.ToJson(), Schema.ConfigurationSchemas.ValidationReport);
        Assert.True(result.IsValid, string.Join("\n", result.Issues));
        Assert.False(outcome.Report.IsValid);
        Assert.Equal(outcome.Report.Issues.Count, outcome.Report.Reasons.Count());
    }

    [Fact]
    public void IssuesAreSortedErrorsFirstThenByCode()
    {
        ValidationOutcome outcome = TestData.Balanced(
            ValidationMode.Draft,
            "[{\"op\":\"retire\",\"entity\":\"document-type\",\"key\":\"runbook\"},{\"op\":\"upsert\",\"entity\":\"tier\",\"key\":\"tier-2\",\"values\":{\"rank\":1}}]");
        string[] codes = outcome.Report.Issues.Select(i => i.Code).ToArray();
        Assert.Equal(codes.OrderBy(c => c, StringComparer.Ordinal).ToArray(), codes);
    }
}
