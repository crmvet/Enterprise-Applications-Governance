using System;
using System.IO;
using System.Linq;
using Product.Engine.Configuration.Composition;
using Product.Engine.Configuration.Json;
using Product.Engine.Configuration.Patches;
using Product.Engine.Configuration.Schema;
using Product.Engine.Configuration.Validation;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;
using Product.Engine.Testing;
using Xunit;

namespace Product.Engine.Configuration.Tests;

// The four engine checks of document 25, section 5.11 that OP-09 runs after V01 to V44.
// The fixtures under fixtures/op-09 hold one passing and one failing case per check. These tests add the branches a
// fixture cannot state well. snapshot.schema-unsupported, the fifth check, is the snapshot compiler's (SnapshotTests).
public sealed class EngineCheckTests
{
    private static ValidationOutcome Draft(string operationsJson, ValidationMode mode = ValidationMode.Draft)
    {
        ContentSource source = ContentSource.Preset("balanced").WithPatch(ConfigurationPatch.FromOperations(CanonicalJson.Parse(operationsJson), PatchKind.Draft, "test operations"));
        return TestData.Validator.Validate(new ValidationRequest(source, mode));
    }

    private static ValidationIssue[] Hits(ValidationOutcome outcome, string code)
    {
        return outcome.Report.Issues.Where(i => i.Code == code).ToArray();
    }

    private static string Model(string key, string changeType, string conditionsJson, bool active = true)
    {
        return "{\"op\":\"upsert\",\"entity\":\"standard-change-model\",\"key\":\"" + key + "\",\"values\":{\"label\":\"" + key + "\",\"changeTypeKey\":\"" + changeType
            + "\",\"conditions\":" + conditionsJson + (active ? string.Empty : ",\"isActive\":false") + "}}";
    }

    private static string Mapping(string key, string entity, string sourceKey, bool active = true)
    {
        return "{\"op\":\"upsert\",\"entity\":\"vocabulary-mapping\",\"key\":\"" + key + "\",\"values\":{\"label\":\"" + key + "\",\"sourceEntity\":\"" + entity
            + "\",\"sourceKey\":\"" + sourceKey + "\",\"vocabularyKey\":\"support-tier\",\"term\":\"Term\",\"sourceUrl\":\"https://example.org/terms\",\"checkedOn\":\"2026-11-22\""
            + (active ? string.Empty : ",\"isActive\":false") + "}}";
    }

    private const string KnownConditions = "[{\"conditionKind\":\"max-tier-rank\",\"value\":1}]";

    [Fact]
    public void TheValidatorRunsTheFourEngineChecksOfDocument25InOrder()
    {
        Assert.Equal(
            new[] { "role.config-admin", "standard-model.type", "standard-model.condition-kind", "vocabulary.source" },
            ConfigurationValidator.EngineChecks.Select(c => c.Code).ToArray());
        foreach (EngineCheck check in ConfigurationValidator.EngineChecks)
        {
            ReasonCodeInfo info = ReasonCatalog.Get(check.Code);
            Assert.Empty(info.RuleIds);
            Assert.False(info.IsProposed);
            Assert.Equal(info.DefaultSeverity, check.Severity);
            Assert.Equal(check.Code == "vocabulary.source" ? ReasonSeverity.Warning : ReasonSeverity.Error, check.Severity);
        }
    }

    [Fact]
    public void EveryShippedFullPresetPassesEveryEngineCheckInEveryMode()
    {
        foreach (string preset in new[] { "starter", "balanced", "regulated", "microsoft-coe-baseline" })
        {
            foreach (ValidationMode mode in new[] { ValidationMode.PresetBuild, ValidationMode.Draft })
            {
                ValidationOutcome outcome = TestData.Validator.Validate(new ValidationRequest(ContentSource.Preset(preset), mode));
                Assert.DoesNotContain(outcome.Report.Issues, i => i.RuleId == null);
            }
        }
    }

    [Fact]
    public void RoleConfigAdminNamesEveryOtherActiveRoleWithTheFlag()
    {
        ValidationOutcome outcome = Draft("[{\"op\":\"upsert\",\"entity\":\"role\",\"key\":\"platform-admin\",\"values\":{\"canChangeConfiguration\":true}},"
            + "{\"op\":\"upsert\",\"entity\":\"role\",\"key\":\"ai-lead\",\"values\":{\"canChangeConfiguration\":true}}]");
        ValidationIssue[] hits = Hits(outcome, "role.config-admin");

        Assert.Equal("invalid", outcome.Outcome);
        Assert.Equal(new[] { "ai-lead", "platform-admin" }, hits.Select(h => h.Ref).OrderBy(r => r, StringComparer.Ordinal).ToArray());
        Assert.All(hits, h =>
        {
            Assert.Null(h.RuleId);
            Assert.Equal("Role", h.Entity);
            Assert.StartsWith("role.config-admin: Role `", h.Message);
            Assert.Contains("`configuration-admin`", h.Message);
        });
    }

    [Fact]
    public void RoleConfigAdminIgnoresRetiredRolesAndTheFlagSetToFalse()
    {
        ValidationOutcome outcome = Draft("[{\"op\":\"upsert\",\"entity\":\"role\",\"key\":\"old-owner\",\"values\":{\"label\":\"Old owner\",\"partyKey\":\"it\",\"duties\":\"Retired.\",\"canChangeConfiguration\":true,\"isActive\":false}},"
            + "{\"op\":\"upsert\",\"entity\":\"role\",\"key\":\"platform-admin\",\"values\":{\"canChangeConfiguration\":false}}]");
        Assert.Empty(Hits(outcome, "role.config-admin"));
    }

    [Fact]
    public void MovingTheFlagToAnotherRoleFailsEvenWhenTheConfigurationAdminRoleLosesIt()
    {
        // The flag moves from the configuration admin role to the platform admin role. role.config-admin refuses the
        // platform admin role, because the configuration admin key is a fixed engine key (question 1).
        ValidationOutcome outcome = Draft("[{\"op\":\"upsert\",\"entity\":\"role\",\"key\":\"configuration-admin\",\"values\":{\"canChangeConfiguration\":false}},"
            + "{\"op\":\"upsert\",\"entity\":\"role\",\"key\":\"platform-admin\",\"values\":{\"canChangeConfiguration\":true}}]");
        ValidationIssue hit = Assert.Single(Hits(outcome, "role.config-admin"));
        Assert.Equal("platform-admin", hit.Ref);
    }

    [Fact]
    public void StandardModelTypeReadsTheBehaviourColumnNotTheKey()
    {
        // A client change type with another key that allows authorization after is an emergency type too.
        string urgent = "{\"op\":\"upsert\",\"entity\":\"change-type\",\"key\":\"urgent\",\"values\":{\"label\":\"Urgent\",\"requiresAuthorization\":true,\"allowsAuthorizationAfter\":true}}";
        ValidationIssue hit = Assert.Single(Hits(Draft("[" + urgent + "," + Model("urgent-fix", "urgent", KnownConditions) + "]"), "standard-model.type"));
        Assert.Equal("Standard Change Model", hit.Entity);
        Assert.Equal("urgent-fix", hit.Ref);
        Assert.Null(hit.RuleId);
        Assert.StartsWith("standard-model.type: ", hit.Message);

        // The normal type does not allow authorization after, so a model on it passes this check.
        Assert.Empty(Hits(Draft("[" + Model("normal-model", "normal", KnownConditions) + "]"), "standard-model.type"));
    }

    [Fact]
    public void StandardModelTypeSkipsRetiredModelsAndUnknownChangeTypes()
    {
        Assert.Empty(Hits(Draft("[" + Model("old-model", "emergency", KnownConditions, active: false) + "]"), "standard-model.type"));

        // An unknown change type is V06's finding, not this check's.
        ValidationOutcome unknown = Draft("[" + Model("lost-model", "no-such-type", KnownConditions) + "]");
        Assert.Empty(Hits(unknown, "standard-model.type"));
        Assert.Contains(unknown.Report.Issues, i => i.RuleId == "V06");
    }

    [Fact]
    public void ConditionKindNamesEachBadConditionByIndex()
    {
        ValidationOutcome outcome = Draft("[" + Model("odd-model", "standard", "[{\"conditionKind\":\"max-tier-rank\",\"value\":1},{\"value\":2},\"stated\",{\"conditionKind\":\"approved-by-ai\"}]") + "]");
        ValidationIssue[] hits = Hits(outcome, "standard-model.condition-kind");

        Assert.Equal(3, hits.Length);
        Assert.Contains(hits, h => h.Message.Contains("condition 1 has no condition kind"));
        Assert.Contains(hits, h => h.Message.Contains("condition 2 has no condition kind"));
        Assert.Contains(hits, h => h.Message.Contains("condition 3 uses the condition kind `approved-by-ai`"));
        Assert.All(hits, h => Assert.Contains("max-tier-rank, target-stages-in, max-affected-applications, component-kinds-within, max-window-minutes, stated", h.Message));
        Assert.All(hits, h => Assert.Equal("odd-model", h.Ref));
    }

    [Fact]
    public void ConditionKindSkipsRetiredModels()
    {
        Assert.Empty(Hits(Draft("[" + Model("old-model", "standard", "[{\"conditionKind\":\"approved-by-ai\"}]", active: false) + "]"), "standard-model.condition-kind"));
    }

    [Fact]
    public void TheSchemaListOfConditionKindsMatchesTheEngineContractList()
    {
        // V01 and V39 use the schema's copy of the list; the engine check uses the contract list. Keep them equal.
        JsonTreeNode bundleSchema = ConfigurationSchemas.Documents.Single(d => JsonNodes.GetString(d, "$id") == ConfigurationSchemas.BundleId);
        JsonTreeNode kinds = JsonNodes.Get(JsonNodes.Get(JsonNodes.Get(bundleSchema, "$defs")!, "contract.standard-change-condition-kind")!, "enum")!;
        Assert.Equal(EngineContract.ConditionKinds.WireValues.ToArray(), kinds.Items.Select(i => i.GetString()).ToArray());
    }

    [Fact]
    public void VocabularySourceWarnsAndNeverBlocks()
    {
        ValidationOutcome outcome = Draft("[" + Mapping("lost-term", "owner-role", "no-such-owner") + "]");
        ValidationIssue hit = Assert.Single(Hits(outcome, "vocabulary.source"));

        Assert.Equal("valid", outcome.Outcome);
        Assert.Equal(ReasonSeverity.Warning, hit.Severity);
        Assert.Equal("Vocabulary Mapping", hit.Entity);
        Assert.Equal("lost-term", hit.Ref);
        Assert.Contains("Owner Role `no-such-owner`", hit.Message);
    }

    [Fact]
    public void VocabularySourceSkipsRetiredMappingsAndAcceptsRetiredRows()
    {
        Assert.Empty(Hits(Draft("[" + Mapping("old-term", "tier", "tier-9", active: false) + "]"), "vocabulary.source"));

        // A retired tier still exists, so a mapping to it does not warn.
        ValidationOutcome retired = Draft("[{\"op\":\"upsert\",\"entity\":\"tier\",\"key\":\"tier-4\",\"values\":{\"label\":\"Tier 4\",\"rank\":4,\"isActive\":false}}," + Mapping("tier-4-term", "tier", "tier-4") + "]");
        Assert.Empty(Hits(retired, "vocabulary.source"));
    }

    [Fact]
    public void VocabularySourceWarnsAboutAnEntityThatIsNotAConfigurationEntity()
    {
        // The schema types sourceEntity as any entity key, an operational one too, so V01 passes it. This check is
        // the only finding, and a warning.
        ValidationOutcome outcome = Draft("[" + Mapping("odd-term", "business-application", "app-1") + "]");
        ValidationIssue hit = Assert.Single(outcome.Report.Issues);
        Assert.Equal("vocabulary.source", hit.Code);
        Assert.Contains("source entity `business-application`", hit.Message);
        Assert.Equal("valid", outcome.Outcome);
    }

    [Fact]
    public void EveryEngineCheckHasAPassingAndAFailingFixture()
    {
        FixtureRunner runner = FixtureRunner.ForSourceRoot(TestData.Root);
        var results = runner.RunFolder(Path.Combine(TestData.Root, "fixtures", "op-09")).ToList();
        Assert.All(results, r => Assert.Equal(FixtureStatus.Passed, r.Status));

        foreach (EngineCheck check in ConfigurationValidator.EngineChecks)
        {
            // Case names start with the check's code, dots written as dashes, as V-ID cases start with the rule ID.
            string prefix = check.Code.Replace('.', '-') + "-";
            var cases = results.Where(r => r.Case != null && r.Case.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            string failingOutcome = check.Severity == ReasonSeverity.Warning ? "valid" : "invalid";

            Assert.True(cases.Any(r => r.ActualOutcome == failingOutcome && r.ActualCodes.Contains(check.Code)), "no failing fixture for " + check.Code);
            Assert.True(cases.Any(r => r.ActualOutcome == "valid" && !r.ActualCodes.Contains(check.Code) && (r.Case!.EndsWith("-valid", StringComparison.Ordinal) || r.Case.EndsWith("-allowed", StringComparison.Ordinal))), "no passing fixture for " + check.Code);
        }
    }
}
