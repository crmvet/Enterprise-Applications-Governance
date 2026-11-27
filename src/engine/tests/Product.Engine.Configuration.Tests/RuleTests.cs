using System;
using System.Linq;
using Product.Engine.Configuration.Composition;
using Product.Engine.Configuration.Patches;
using Product.Engine.Configuration.Validation;
using Product.Engine.Configuration.Validation.Rules;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;
using Xunit;

namespace Product.Engine.Configuration.Tests;

// One passing and one failing case per rule of the first group (V01 to V12, V43, V44).
// The passing case is Balanced, which must stay clean for every rule. RuleTestsV13ToV42 holds the rest.
public sealed class RuleTests
{
    private static readonly Lazy<ValidationOutcome> CleanBuild = new(() => TestData.Balanced(ValidationMode.PresetBuild));
    private static readonly Lazy<ValidationOutcome> CleanWorkingSet = new(() => TestData.Balanced(ValidationMode.Draft));

    private static ValidationOutcome Draft(string operationsJson)
    {
        return TestData.Balanced(ValidationMode.Draft, operationsJson);
    }

    private static ValidationOutcome RawPatch(string operationsJson)
    {
        string patch = "{\"schemaVersion\":\"1.0\",\"manifest\":{\"kind\":\"patch\",\"baseVersionNumber\":1,\"summary\":\"Test\"},\"operations\":" + operationsJson + "}";
        return TestData.BalancedWithPatch(patch, PatchKind.Draft);
    }

    private static void AssertFails(ValidationOutcome outcome, string ruleId, string? entity = null, string? reference = null)
    {
        Assert.Equal("invalid", outcome.Outcome);
        ValidationIssue[] hits = TestData.Issues(outcome, ruleId).ToArray();
        Assert.True(hits.Length > 0, "expected a " + ruleId + " issue, got: " + string.Join(" | ", outcome.Report.Issues));
        Assert.All(hits, h => Assert.StartsWith(ruleId + ": ", h.Message));
        if (entity != null || reference != null)
        {
            Assert.Contains(hits, h => (entity == null || h.Entity == entity) && (reference == null || h.Ref == reference));
        }
    }

    [Fact]
    public void BalancedIsCleanForEveryRuleInBothModes()
    {
        Assert.Equal("valid", CleanBuild.Value.Outcome);
        Assert.Empty(CleanBuild.Value.Report.Issues);
        Assert.Equal("valid", CleanWorkingSet.Value.Outcome);
        Assert.Empty(CleanWorkingSet.Value.Report.Issues);
    }

    [Fact]
    public void EveryRuleUsesItsCatalogCodeAndRuleId()
    {
        // Every rule of document 21, section 9, and the data taxonomy's V49 to V60 (docs/architecture/data-taxonomy.md,
        // Validation rules), in rule ID order. V45 to V48 are engine checks with no rule class.
        string[] ids = ConfigurationValidator.Rules.Select(r => r.RuleId).ToArray();
        Assert.Equal(Enumerable.Range(1, 44).Concat(Enumerable.Range(49, 12)).Select(n => "V" + n.ToString("00", System.Globalization.CultureInfo.InvariantCulture)).ToArray(), ids);

        // V29, V37, V53 and V59 are warnings. V24, V41 and V50 are errors that can also warn.
        string[] warnings = { "V29", "V37", "V53", "V59" };
        foreach (ValidationRule rule in ConfigurationValidator.Rules)
        {
            Assert.Contains(rule.RuleId, ReasonCatalog.Get(rule.Code).RuleIds);
            Assert.Equal(warnings.Contains(rule.RuleId) ? ReasonSeverity.Warning : ReasonSeverity.Error, rule.Severity);
        }
    }

    [Fact]
    public void V01AnUnknownFieldFails()
    {
        ValidationOutcome outcome = RawPatch("[{\"op\":\"upsert\",\"entity\":\"tier\",\"key\":\"tier-1\",\"values\":{\"colour\":\"green\"}}]");
        AssertFails(outcome, "V01", "Tier", "tier-1");
        Assert.Contains(TestData.Issues(outcome, "V01"), i => i.Message.Contains("unknown field \"colour\""));
    }

    [Fact]
    public void V01AMissingRequiredFieldInANewRowFails()
    {
        // The upsert values are fine on their own. The new row lacks the rank every tier needs.
        AssertFails(Draft("[{\"op\":\"upsert\",\"entity\":\"tier\",\"key\":\"tier-4\",\"values\":{\"label\":\"Tier 4\"}}]"), "V01", "Tier", "tier-4");
    }

    [Fact]
    public void V02ABadKeyFailsWithASuggestion()
    {
        ValidationOutcome outcome = RawPatch("[{\"op\":\"upsert\",\"entity\":\"tier\",\"key\":\"Tier 4\",\"values\":{\"label\":\"Tier 4\",\"rank\":4}}]");
        AssertFails(outcome, "V02", "Tier", "Tier 4");
        Assert.Contains(TestData.Issues(outcome, "V02"), i => i.Message.Contains("`tier-4`"));
    }

    [Fact]
    public void V02AnExpandedKeyThatIsTooLongFails()
    {
        string longKey = "gp-" + new string('x', 52);
        ValidationOutcome outcome = TestData.Balanced(
            ValidationMode.Draft,
            "[{\"op\":\"retire\",\"entity\":\"gate-policy\",\"key\":\"gp-development--tier-1--standard\"},"
            + "{\"op\":\"upsert\",\"entity\":\"gate-policy\",\"key\":\"" + longKey + "\",\"values\":{\"label\":\"Long\",\"tierKey\":\"tier-1\",\"changeTypeKey\":\"*\",\"targetStageKey\":\"development\",\"gateLevel\":\"off\"}}]");
        AssertFails(outcome, "V02", "Gate Policy", longKey + "--standard");
    }

    [Fact]
    public void V03ARepeatedKeyFails()
    {
        JsonTreeNode bundle = CanonicalJson.Parse("{\"schemaVersion\":\"1.0\",\"manifest\":{\"kind\":\"bundle\"},\"entities\":{\"tier\":[{\"key\":\"tier-1\",\"label\":\"A\",\"rank\":1},{\"key\":\"tier-1\",\"label\":\"B\",\"rank\":2}]}}");
        ValidationOutcome outcome = TestData.Validator.Validate(new ValidationRequest(ContentSource.Bundle(bundle, "test bundle"), ValidationMode.Draft));
        AssertFails(outcome, "V03", "Tier", "tier-1");
    }

    [Fact]
    public void V03AnExplicitRowCannotTakeAnExpandedKey()
    {
        // gp-x expands to gp-x--tier-1 for tier 1. An explicit row for tier 2 already holds that key, so the keys clash.
        JsonTreeNode bundle = CanonicalJson.Parse(
            "{\"schemaVersion\":\"1.0\",\"manifest\":{\"kind\":\"bundle\"},\"entities\":{"
            + "\"tier\":[{\"key\":\"tier-1\",\"label\":\"Tier 1\",\"rank\":1},{\"key\":\"tier-2\",\"label\":\"Tier 2\",\"rank\":2}],"
            + "\"change-type\":[{\"key\":\"normal\",\"label\":\"Normal\",\"requiresAuthorization\":true}],"
            + "\"target-stage\":[{\"key\":\"test\",\"label\":\"Test\",\"rank\":1}],"
            + "\"gate-policy\":[{\"key\":\"gp-x\",\"label\":\"Every tier\",\"tierKey\":\"*\",\"changeTypeKey\":\"normal\",\"targetStageKey\":\"test\",\"gateLevel\":\"flag\"},"
            + "{\"key\":\"gp-x--tier-1\",\"label\":\"Tier 2 only\",\"tierKey\":\"tier-2\",\"changeTypeKey\":\"normal\",\"targetStageKey\":\"test\",\"gateLevel\":\"record\"}]}}");
        ValidationOutcome outcome = TestData.Validator.Validate(new ValidationRequest(ContentSource.Bundle(bundle, "test bundle"), ValidationMode.Draft));
        AssertFails(outcome, "V03", "Gate Policy", "gp-x--tier-1");
    }

    [Fact]
    public void V04ReusingARetiredKeyFailsAndReactivatingPasses()
    {
        AssertFails(Draft("[{\"op\":\"upsert\",\"entity\":\"guideline\",\"key\":\"g11\",\"values\":{\"rule\":\"Something new.\"}}]"), "V04", "Guideline", "g11");
        AssertFails(Draft("[{\"op\":\"retire\",\"entity\":\"tier\",\"key\":\"tier-3\"},{\"op\":\"upsert\",\"entity\":\"tier\",\"key\":\"tier-3\",\"values\":{\"rank\":7}}]"), "V04", "Tier", "tier-3");

        Assert.Empty(TestData.Issues(Draft("[{\"op\":\"upsert\",\"entity\":\"guideline\",\"key\":\"g11\",\"values\":{\"isActive\":true}}]"), "V04"));
        Assert.Empty(TestData.Issues(Draft("[{\"op\":\"upsert\",\"entity\":\"guideline\",\"key\":\"g11\",\"values\":{\"description\":\"Still retired.\"}}]"), "V04"));
    }

    [Fact]
    public void V05ChangingAKeyFails()
    {
        AssertFails(RawPatch("[{\"op\":\"upsert\",\"entity\":\"tier\",\"key\":\"tier-1\",\"values\":{\"key\":\"tier-one\"}}]"), "V05", "Tier", "tier-1");
        AssertFails(RawPatch("[{\"op\":\"upsert\",\"entity\":\"list-value\",\"key\":\"channel/teams\",\"values\":{\"key\":\"chat\"}}]"), "V05", "List Value", "channel/teams");
    }

    [Fact]
    public void V06AnOrphanReferenceFails()
    {
        AssertFails(Draft("[{\"op\":\"upsert\",\"entity\":\"approval-rule\",\"key\":\"change-auth-owner\",\"values\":{\"tierKey\":\"tier-9\"}}]"), "V06", "Approval Rule", "change-auth-owner");
    }

    [Fact]
    public void V06ReferencesInsideJsonFieldsAndListValuesResolve()
    {
        // The emergency rule's approver roles sit inside a JSON field. The reminder channel is a list value.
        AssertFails(Draft("[{\"op\":\"upsert\",\"entity\":\"gate-policy\",\"key\":\"gp-emergency-prod-tier-3\",\"values\":{\"emergencyRule\":{\"passWithoutApproval\":true,\"approvalDueWorkingDays\":2,\"approverRoleKeys\":[\"no-such-role\"]}}}]"), "V06", "Gate Policy", "gp-emergency-prod-tier-3");
        AssertFails(Draft("[{\"op\":\"upsert\",\"entity\":\"setting\",\"key\":\"reminder-channel\",\"values\":{\"value\":\"pager\"}}]"), "V06", "Setting", "reminder-channel");
        AssertFails(Draft("[{\"op\":\"upsert\",\"entity\":\"setting\",\"key\":\"unlinked-deployment-change-type\",\"values\":{\"value\":\"no-such-type\"}}]"), "V06", "Setting", "unlinked-deployment-change-type");
    }

    [Fact]
    public void V06TheReferenceIndexSeesEveryShippedReference()
    {
        ValidationOutcome outcome = CleanWorkingSet.Value;
        Assert.Equal("valid", outcome.Outcome);
        var shape = Schema.ConfigurationSchemas.Set.Evaluate(outcome.Candidate!.Bundle, outcome.Candidate.Profile);
        ReferenceIndex index = ReferenceIndex.Build(outcome.Candidate.Content, outcome.Candidate.Bundle, shape);

        // A count over the preset source files found 358 references. Expansion adds the expanded rows' references.
        Assert.True(index.All.Count >= 358, "found " + index.All.Count);
        Assert.Contains(index.All, r => r.Source.Entity == "tier-answer-option" && r.FieldName == "impliedTierKey");
        Assert.Contains(index.All, r => r.TargetParentKey == "channel");
    }

    [Fact]
    public void V07AReferenceToARetiredRowFails()
    {
        AssertFails(Draft("[{\"op\":\"retire\",\"entity\":\"document-type\",\"key\":\"runbook\"}]"), "V07", "Documentation Requirement", "runbook-tier-2");
        AssertFails(Draft("[{\"op\":\"retire\",\"entity\":\"value-list\",\"key\":\"channel\"}]"), "V07", "Setting", "reminder-channel");
    }

    [Fact]
    public void V08RemovingARowInUseFails()
    {
        ContentSource active = ContentSource.Preset("balanced").WithPatch(ConfigurationPatch.FromOperations(
            CanonicalJson.Parse("[{\"op\":\"upsert\",\"entity\":\"party\",\"key\":\"fixture-vendor\",\"values\":{\"label\":\"Vendor\"}},"
                + "{\"op\":\"upsert\",\"entity\":\"owner-role\",\"key\":\"fixture-vendor-contact\",\"values\":{\"label\":\"Vendor contact\",\"partyKey\":\"fixture-vendor\"}}]"),
            PatchKind.Draft,
            "active"));
        ValidationOutcome outcome = TestData.Validator.Validate(new ValidationRequest(ContentSource.Preset("balanced"), ValidationMode.Draft, active));
        AssertFails(outcome, "V08", "Party", "fixture-vendor");
        Assert.DoesNotContain(TestData.Issues(outcome, "V08"), i => i.Ref == "fixture-vendor-contact");

        ValidationOutcome byRecord = TestData.Validator.Validate(new ValidationRequest(ContentSource.Preset("balanced"), ValidationMode.Draft, active, new[] { new RowIdentity("owner-role", "fixture-vendor-contact") }));
        AssertFails(byRecord, "V08", "Owner Role", "fixture-vendor-contact");
    }

    [Fact]
    public void V09EqualExplicitnessFails()
    {
        AssertFails(Draft("[{\"op\":\"upsert\",\"entity\":\"gate-policy\",\"key\":\"gp-extra\",\"values\":{\"label\":\"Extra\",\"tierKey\":\"tier-1\",\"changeTypeKey\":\"normal\",\"targetStageKey\":\"test\",\"gateLevel\":\"record\"}}]"), "V09", "Gate Policy", "gp-extra");
    }

    [Fact]
    public void V09AWildcardOutsideTheScopeFieldsFails()
    {
        AssertFails(RawPatch("[{\"op\":\"upsert\",\"entity\":\"approval-rule\",\"key\":\"change-auth-owner\",\"values\":{\"tierKey\":\"*\"}}]"), "V09", "Approval Rule", "change-auth-owner");
    }

    [Fact]
    public void V09AWildcardRowAfterRetiringTheRowsItReplacesTakesEffect()
    {
        // Retire the six expanded rows of gp-test, then add a wildcard row: it wins those combinations again.
        string retire = string.Join(",", new[] { "tier-1", "tier-2", "tier-3" }
            .SelectMany(t => new[] { "standard", "normal" }.Select(c => "{\"op\":\"retire\",\"entity\":\"gate-policy\",\"key\":\"gp-test--" + t + "--" + c + "\"}")));
        ValidationOutcome outcome = Draft("[" + retire + ",{\"op\":\"upsert\",\"entity\":\"gate-policy\",\"key\":\"gp-test-record\",\"values\":{\"label\":\"Test: record\",\"tierKey\":\"*\",\"changeTypeKey\":\"*\",\"targetStageKey\":\"test\",\"gateLevel\":\"record\"}}]");
        Assert.Empty(TestData.Issues(outcome, "V09"));
        Assert.True(outcome.Candidate!.Content.TryFindRow("gate-policy", "gp-test-record--tier-2--normal", out var row));
        Assert.Equal("local", Json.JsonNodes.GetString(row.Node, "origin"));
    }

    [Fact]
    public void V10ExactlyOneApproverAndHolderAndTarget()
    {
        AssertFails(Draft("[{\"op\":\"upsert\",\"entity\":\"approval-rule\",\"key\":\"change-auth-owner\",\"values\":{\"approverRoleKey\":\"change-manager\"}}]"), "V10", "Approval Rule", "change-auth-owner");
        AssertFails(Draft("[{\"op\":\"upsert\",\"entity\":\"decision-right\",\"key\":\"fixture-supports\",\"values\":{\"label\":\"AI drafts\",\"decisionTypeKey\":\"intake\",\"letter\":\"supports\",\"ownerRoleKey\":\"business-owner\"}}]"), "V10", "Decision Right", "fixture-supports");
        AssertFails(Draft("[{\"op\":\"upsert\",\"entity\":\"gate-requirement\",\"key\":\"fixture-requirement\",\"values\":{\"label\":\"Two targets\",\"gateKey\":\"go-live\",\"requirementKind\":\"document-current\",\"requiredDocumentTypeKey\":\"runbook\",\"requiredReviewTypeKey\":\"owner-check\"}}]"), "V10", "Gate Requirement", "fixture-requirement");
    }

    [Fact]
    public void V11NoActiveTierFails()
    {
        AssertFails(Draft("[{\"op\":\"retire\",\"entity\":\"tier\",\"key\":\"tier-1\"},{\"op\":\"retire\",\"entity\":\"tier\",\"key\":\"tier-2\"},{\"op\":\"retire\",\"entity\":\"tier\",\"key\":\"tier-3\"}]"), "V11", "Tier");
    }

    [Fact]
    public void V12RanksAreUniqueWholeNumbers()
    {
        AssertFails(Draft("[{\"op\":\"upsert\",\"entity\":\"tier\",\"key\":\"tier-3\",\"values\":{\"rank\":2}}]"), "V12", "Tier", "tier-3");
        AssertFails(Draft("[{\"op\":\"upsert\",\"entity\":\"tier\",\"key\":\"tier-3\",\"values\":{\"rank\":2.5}}]"), "V12", "Tier", "tier-3");
    }

    [Fact]
    public void V43ConflictingOverlaysFailAndCompatibleOnesPass()
    {
        AssertFails(TestData.Balanced(ValidationMode.PresetBuild, overlays: new[] { "fixture-overlay-b", "fixture-overlay-a" }), "V43", null, "fixture-overlay-a");
        Assert.Equal("valid", TestData.Balanced(ValidationMode.PresetBuild, overlays: new[] { "fixture-overlay-b", "fixture-overlay-c" }).Outcome);
    }

    [Fact]
    public void V44TenantDataInAPresetFails()
    {
        // A role group binding with a directory group ID.
        AssertFails(
            TestData.Balanced(ValidationMode.PresetBuild, "[{\"op\":\"upsert\",\"entity\":\"role-group-binding\",\"key\":\"admins\",\"values\":{\"label\":\"Admins\",\"roleKey\":\"configuration-admin\",\"entraGroupObjectId\":\"00000000-0000-4000-8000-000000000001\",\"bindingMethod\":\"entra-app-role-assignment\",\"bindingDetail\":\"Group\"}}]"),
            "V44",
            "Role Group Binding",
            "admins");

        // A portfolio scope.
        AssertFails(
            TestData.Balanced(ValidationMode.PresetBuild, "[{\"op\":\"upsert\",\"entity\":\"approval-rule\",\"key\":\"go-live-owner\",\"values\":{\"portfolioId\":\"fixture-portfolio\"}}]"),
            "V44",
            "Approval Rule",
            "go-live-owner");

        // A tenant host name. The host is built in two parts so a scan for tenant host names does not flag this test line.
        string host = "example-tenant" + ".crm4" + ".dynamics.com";
        AssertFails(
            TestData.Balanced(ValidationMode.PresetBuild, "[{\"op\":\"upsert\",\"entity\":\"guideline\",\"key\":\"g2\",\"values\":{\"hubUrl\":\"https://" + host + "/main.aspx\"}}]"),
            "V44",
            "Guideline",
            "g2");

        // A GUID in free text.
        AssertFails(
            TestData.Balanced(ValidationMode.PresetBuild, "[{\"op\":\"upsert\",\"entity\":\"tier\",\"key\":\"tier-1\",\"values\":{\"description\":\"Environment 00000000-0000-4000-8000-00000000000a only.\"}}]"),
            "V44",
            "Tier",
            "tier-1");
    }

    [Fact]
    public void V44DoesNotApplyToAWorkingSet()
    {
        ValidationOutcome outcome = TestData.Balanced(ValidationMode.Draft, "[{\"op\":\"upsert\",\"entity\":\"approval-rule\",\"key\":\"go-live-owner\",\"values\":{\"portfolioId\":\"fixture-portfolio\"}}]");
        Assert.Empty(TestData.Issues(outcome, "V44"));
    }

    [Fact]
    public void MessagesNameTheRuleTheEntityTheKeyAndTheFix()
    {
        ValidationIssue issue = Assert.Single(TestData.Issues(Draft("[{\"op\":\"upsert\",\"entity\":\"approval-rule\",\"key\":\"change-auth-owner\",\"values\":{\"tierKey\":\"tier-9\"}}]"), "V06"));
        Assert.Equal("V06: Approval Rule `change-auth-owner` field `tierKey` points to Tier `tier-9`, which is not in the set. Add that row or fix the key.", issue.Message);
    }

    [Fact]
    public void KeySuggestionsUseTheKeyFormat()
    {
        Assert.Equal("tier-4", V02KeyFormat.Suggest("Tier 4"));
        Assert.Equal("k-4-tier", V02KeyFormat.Suggest("4 Tier"));
        Assert.True(ConfigurationKey.IsValid(V02KeyFormat.Suggest(new string('A', 90))));
    }
}
