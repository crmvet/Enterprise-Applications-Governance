using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Composition;
using Product.Engine.Configuration.Patches;
using Product.Engine.Configuration.Validation;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Xunit;

namespace Product.Engine.Configuration.Tests;

// The second group of rules: V13 to V42. The fixtures under fixtures/op-09 hold one passing and one failing case per rule.
// These tests add the cases a fixture cannot state well: other branches of a rule, facts, modes and warnings.
public sealed class RuleTestsV13ToV42
{
    private const string Group1 = "00000000-0000-4000-8000-000000000101";
    private const string Group2 = "00000000-0000-4000-8000-000000000102";
    private const string Principal = "00000000-0000-4000-8000-000000000201";

    private static ValidationOutcome Draft(string operationsJson, ValidationFacts? facts = null, ValidationMode mode = ValidationMode.Draft)
    {
        ContentSource source = ContentSource.Preset("balanced").WithPatch(ConfigurationPatch.FromOperations(CanonicalJson.Parse(operationsJson), PatchKind.Draft, "test operations"));
        return TestData.Validator.Validate(new ValidationRequest(source, mode, facts: facts));
    }

    private static ValidationIssue[] Hits(ValidationOutcome outcome, string ruleId)
    {
        return TestData.Issues(outcome, ruleId).ToArray();
    }

    private static void AssertFails(ValidationOutcome outcome, string ruleId, string? entity = null, string? reference = null, string? code = null)
    {
        Assert.Equal("invalid", outcome.Outcome);
        ValidationIssue[] hits = Hits(outcome, ruleId);
        Assert.True(hits.Any(h => h.Severity == ReasonSeverity.Error), "expected a " + ruleId + " error, got: " + string.Join(" | ", outcome.Report.Issues));
        Assert.All(hits, h => Assert.StartsWith(ruleId + ": ", h.Message));
        Assert.Contains(hits, h => (entity == null || h.Entity == entity) && (reference == null || h.Ref == reference) && (code == null || h.Code == code));
    }

    private static string Binding(string key, string role, string group)
    {
        return "{\"op\":\"upsert\",\"entity\":\"role-group-binding\",\"key\":\"" + key + "\",\"values\":{\"label\":\"" + key + "\",\"roleKey\":\"" + role
            + "\",\"entraGroupObjectId\":\"" + group + "\",\"bindingMethod\":\"entra-app-role-assignment\",\"bindingDetail\":\"App role\"}}";
    }

    private static string Grant(string key, string role, string expiresOn)
    {
        return "{\"op\":\"upsert\",\"entity\":\"workload-grant\",\"key\":\"" + key + "\",\"values\":{\"label\":\"" + key + "\",\"principalObjectId\":\"" + Principal
            + "\",\"workloadRoleKey\":\"" + role + "\",\"reason\":\"Pipeline records deployments\",\"usesFederatedCredential\":true,\"bindingDetail\":\"App role assignment\",\"expiresOn\":\"" + expiresOn + "\"}}";
    }

    [Fact]
    public void BalancedHasNoFindingForAnyNewRuleInEveryMode()
    {
        var facts = new ValidationFacts(validatedOn: new DateTime(2026, 11, 22), baseVersionNumber: 1, activeVersionNumber: 1, approvalRulesVersionNumber: 1);
        foreach (ValidationMode mode in new[] { ValidationMode.PresetBuild, ValidationMode.Draft, ValidationMode.Activation })
        {
            ValidationOutcome outcome = TestData.Validator.Validate(new ValidationRequest(ContentSource.Preset("balanced"), mode, facts: facts));
            Assert.Equal("valid", outcome.Outcome);
            Assert.Empty(outcome.Report.Issues);
        }
    }

    [Fact]
    public void V13AnAnswerThatImpliesARetiredTierFails()
    {
        // A fourth tier is added and retired. An answer that points at it fails V13 (and V07).
        ValidationOutcome outcome = Draft("[{\"op\":\"upsert\",\"entity\":\"tier\",\"key\":\"tier-4\",\"values\":{\"label\":\"Tier 4\",\"rank\":4,\"isActive\":false}},"
            + "{\"op\":\"upsert\",\"entity\":\"tier-answer-option\",\"key\":\"reach/organization\",\"values\":{\"impliedTierKey\":\"tier-4\"}}]");
        AssertFails(outcome, "V13", "Tier Answer Option", "reach/organization");
    }

    [Fact]
    public void V14MissingMeaningAndDuplicateRankFail()
    {
        AssertFails(Draft("[{\"op\":\"retire\",\"entity\":\"lifecycle-stage\",\"key\":\"retire\"},{\"op\":\"upsert\",\"entity\":\"decision-outcome\",\"key\":\"retirement/retire\",\"values\":{\"isActive\":false}}]"), "V14", "Lifecycle Stage", code: "stage.meaning-coverage");
        AssertFails(Draft("[{\"op\":\"upsert\",\"entity\":\"lifecycle-stage\",\"key\":\"test\",\"values\":{\"stageRank\":5}}]"), "V14", "Lifecycle Stage", "test", "stage.rank-duplicate");
    }

    [Fact]
    public void V15AMissingMeaningAndTwoDefaultsFail()
    {
        AssertFails(Draft("[{\"op\":\"retire\",\"entity\":\"status\",\"key\":\"review-cancelled\"}]"), "V15", "Status");
        AssertFails(
            Draft("[{\"op\":\"upsert\",\"entity\":\"status\",\"key\":\"decision-approved\",\"values\":{\"label\":\"Approved\",\"appliesToEntity\":\"decision\",\"engineMeaning\":\"accepted\",\"isDefaultForMeaning\":true}},"
                + "{\"op\":\"upsert\",\"entity\":\"status\",\"key\":\"decision-accepted\",\"values\":{\"isDefaultForMeaning\":true}}]"),
            "V15",
            "Status");
    }

    [Fact]
    public void V16NeedsTheActiveVersionAndTheFacts()
    {
        string change = "[{\"op\":\"upsert\",\"entity\":\"deployment-route\",\"key\":\"other\",\"values\":{\"engineMeaning\":\"person-in-app\"}}]";
        Assert.Empty(Hits(Draft(change), "V16"));

        ContentSource source = ContentSource.Preset("balanced").WithPatch(ConfigurationPatch.FromOperations(CanonicalJson.Parse(change), PatchKind.Draft, "test"));
        ValidationOutcome outcome = TestData.Validator.Validate(new ValidationRequest(source, ValidationMode.Draft, rowsInUse: new[] { new RowIdentity("deployment-route", "other") }));
        AssertFails(outcome, "V16", "Deployment Route", "other");
    }

    [Fact]
    public void V17EveryActorMeaningNeedsAnActorKind()
    {
        AssertFails(Draft("[{\"op\":\"retire\",\"entity\":\"actor-kind\",\"key\":\"approval-runner\"}]"), "V17", "Actor Kind");
        AssertFails(Draft("[{\"op\":\"retire\",\"entity\":\"deployment-route\",\"key\":\"pipeline-step\"}]"), "V17", "Deployment Route");
    }

    [Fact]
    public void V19ATypeWithApprovalNeedsARuleAtEveryTier()
    {
        // The go-live rule with no tier now applies only at tier 1, so tier 2 has no rule.
        AssertFails(Draft("[{\"op\":\"upsert\",\"entity\":\"approval-rule\",\"key\":\"go-live-owner\",\"values\":{\"tierKey\":\"tier-1\"}}]"), "V19", "Decision Type", "go-live");

        // A workload role as the accountable holder of a type without approval rules also fails (and V31).
        AssertFails(Draft("[{\"op\":\"upsert\",\"entity\":\"decision-right\",\"key\":\"design-a\",\"values\":{\"roleKey\":\"approval-writer\"}}]"), "V19", "Decision Right", "design-a");
    }

    [Fact]
    public void V20ATypeWithApprovalAndNoTierNeedsARule()
    {
        ValidationOutcome outcome = Draft("[{\"op\":\"retire\",\"entity\":\"approval-rule\",\"key\":\"gate-policy-change-config-admin\"},{\"op\":\"retire\",\"entity\":\"approval-rule\",\"key\":\"gate-policy-change-change-manager\"}]");
        AssertFails(outcome, "V20", "Decision Type", "gate-policy-change");
        AssertFails(outcome, "V32", "Decision Type", "gate-policy-change");
    }

    [Fact]
    public void V21SkipsCombinationsAtOff()
    {
        // Production for tier 1 normal changes goes to off. Its group may then be missing.
        ValidationOutcome outcome = Draft(
            "[{\"op\":\"upsert\",\"entity\":\"approval-rule\",\"key\":\"change-auth-owner\",\"values\":{\"tierKey\":\"tier-3\"}},"
            + "{\"op\":\"upsert\",\"entity\":\"gate-policy\",\"key\":\"gp-test--tier-1--normal\",\"values\":{\"gateLevel\":\"off\"}}]");
        ValidationIssue[] hits = Hits(outcome, "V21");
        Assert.DoesNotContain(hits, h => h.Message.Contains("tier `tier-1`, change type `normal`, target stage `test`"));
        Assert.Contains(hits, h => h.Message.Contains("tier `tier-1`, change type `normal`, target stage `production`"));
    }

    [Fact]
    public void V22TwoAccountablesInOneGroupFail()
    {
        AssertFails(
            Draft("[{\"op\":\"upsert\",\"entity\":\"decision-right\",\"key\":\"go-live-a2\",\"values\":{\"label\":\"Second accountable\",\"decisionTypeKey\":\"go-live\",\"letter\":\"accountable\",\"roleKey\":\"architect\"}}]"),
            "V22",
            "Decision Right",
            "go-live-a2");

        // A scoped group with no accountable replaces the broader group, so it fails too.
        AssertFails(
            Draft("[{\"op\":\"upsert\",\"entity\":\"decision-right\",\"key\":\"go-live-tier-2-c\",\"values\":{\"label\":\"Tier 2 consulted\",\"decisionTypeKey\":\"go-live\",\"tierKey\":\"tier-2\",\"letter\":\"consulted\",\"roleKey\":\"architect\"}}]"),
            "V22",
            "Decision Right",
            "go-live-tier-2-c");
    }

    [Fact]
    public void V23AnAddedChangeTypeNeedsItsGateRows()
    {
        ValidationOutcome outcome = Draft("[{\"op\":\"upsert\",\"entity\":\"change-type\",\"key\":\"minor\",\"values\":{\"label\":\"Minor\",\"requiresAuthorization\":false}}]");
        Assert.Equal(9, Hits(outcome, "V23").Length);
        Assert.Contains(Hits(outcome, "V23"), h => h.Message == "V23: no gate policy for tier `tier-1`, change type `minor`, target stage `development`. Add a row or a wildcard row that covers it.");
    }

    [Fact]
    public void V23StaysQuietWhereV09ReportsTheOverlap()
    {
        ValidationOutcome outcome = Draft("[{\"op\":\"upsert\",\"entity\":\"gate-policy\",\"key\":\"gp-extra\",\"values\":{\"label\":\"Extra\",\"tierKey\":\"tier-1\",\"changeTypeKey\":\"normal\",\"targetStageKey\":\"test\",\"gateLevel\":\"record\"}}]");
        Assert.NotEmpty(Hits(outcome, "V09"));
        Assert.Empty(Hits(outcome, "V23"));
    }

    [Fact]
    public void V24AFailModeBelowEnforceIsOnlyAWarning()
    {
        ValidationOutcome outcome = Draft("[{\"op\":\"upsert\",\"entity\":\"gate-policy\",\"key\":\"gp-test--tier-1--normal\",\"values\":{\"failMode\":\"fail-open\"}}]");
        Assert.Equal("valid", outcome.Outcome);
        ValidationIssue issue = Assert.Single(Hits(outcome, "V24"));
        Assert.Equal(ReasonSeverity.Warning, issue.Severity);
        Assert.Equal("gate-policy.fail-mode", issue.Code);
    }

    [Fact]
    public void V26SeparationRulesForOneSubjectTie()
    {
        // Two proposer rules of equal specificity that both match a tier 3 production approval.
        ValidationOutcome outcome = Draft("[{\"op\":\"upsert\",\"entity\":\"separation-rule\",\"key\":\"no-self-approval-production\",\"values\":{\"label\":\"No self-approval in production\","
            + "\"ruleKind\":\"approver-differs\",\"targetStageKey\":\"production\",\"mustDifferFrom\":\"proposer\",\"enforcement\":\"flag\"}}]");
        AssertFails(outcome, "V26", "Separation Rule", "no-self-approval-production");

        // Another subject never ties.
        ValidationOutcome other = Draft("[{\"op\":\"upsert\",\"entity\":\"separation-rule\",\"key\":\"deployer-tier-3\",\"values\":{\"label\":\"Deployer at tier 3\","
            + "\"ruleKind\":\"approver-differs\",\"tierKey\":\"tier-3\",\"mustDifferFrom\":\"instructing-admin\",\"enforcement\":\"flag\"}}]");
        Assert.Empty(Hits(other, "V26"));
    }

    [Fact]
    public void V26DecisionRightGroupsTie()
    {
        ValidationOutcome outcome = Draft("[{\"op\":\"upsert\",\"entity\":\"decision-right\",\"key\":\"go-live-agent-a\",\"values\":{\"label\":\"Agents\",\"decisionTypeKey\":\"go-live\",\"applicationKindKey\":\"agent\",\"letter\":\"accountable\",\"roleKey\":\"ai-lead\"}},"
            + "{\"op\":\"upsert\",\"entity\":\"decision-right\",\"key\":\"go-live-tier-3-a\",\"values\":{\"label\":\"Tier 3\",\"decisionTypeKey\":\"go-live\",\"tierKey\":\"tier-3\",\"letter\":\"accountable\",\"roleKey\":\"security-reviewer\"}}]");
        AssertFails(outcome, "V26", "Decision Right", "go-live-tier-3-a");
    }

    [Fact]
    public void V27AGroupOfConsultationsOnlyFails()
    {
        ValidationOutcome outcome = Draft("[{\"op\":\"upsert\",\"entity\":\"approval-rule\",\"key\":\"go-live-owner\",\"values\":{\"isConsultation\":true}}]");
        AssertFails(outcome, "V27", "Approval Rule", "go-live-owner", "approval-rule.consultation-only");
    }

    [Fact]
    public void V28DayValuesAndTheGrantSetting()
    {
        AssertFails(Draft("[{\"op\":\"upsert\",\"entity\":\"reminder-rule\",\"key\":\"review-reminder\",\"values\":{\"repeatDays\":-1}}]"), "V28", "Reminder Rule", "review-reminder", "value.range");
        AssertFails(Draft("[{\"op\":\"upsert\",\"entity\":\"setting\",\"key\":\"workload-grant-max-days\",\"values\":{\"value\":0}}]"), "V28", "Setting", "workload-grant-max-days", "value.range");
    }

    [Fact]
    public void V28TheGrantWindowNeedsTheValidationDate()
    {
        string grant = "[" + Grant("pipeline-grant", "deployment-recorder", "2030-01-01") + "]";

        // Without the date the window is not checked in a draft. At activation the missing date is an error.
        Assert.Empty(Hits(Draft(grant), "V28"));
        AssertFails(Draft(grant, mode: ValidationMode.Activation), "V28", "Workload Grant", "pipeline-grant", "value.range");
        string lastDayAtActivation = "[" + Grant("pipeline-grant", "deployment-recorder", "2027-11-22") + "]";
        var activationFacts = new ValidationFacts(validatedOn: new DateTime(2026, 11, 22), baseVersionNumber: 1, activeVersionNumber: 1, approvalRulesVersionNumber: 1);
        Assert.Empty(Hits(Draft(lastDayAtActivation, activationFacts, ValidationMode.Activation), "V28"));

        // With it, a date more than 365 days away fails, and one on the last allowed day passes.
        AssertFails(Draft(grant, new ValidationFacts(validatedOn: new DateTime(2026, 11, 22))), "V28", "Workload Grant", "pipeline-grant", "value.range");
        string lastDay = "[" + Grant("pipeline-grant", "deployment-recorder", "2027-11-22") + "]";
        Assert.Empty(Hits(Draft(lastDay, new ValidationFacts(validatedOn: new DateTime(2026, 11, 22))), "V28"));
    }

    [Fact]
    public void V30AnEmergencyApproverMustBeHuman()
    {
        AssertFails(
            Draft("[{\"op\":\"upsert\",\"entity\":\"gate-policy\",\"key\":\"gp-emergency-prod-tier-1\",\"values\":{\"emergencyRule\":{\"passWithoutApproval\":true,\"approvalDueWorkingDays\":2,\"approverRoleKeys\":[\"auditor\"],\"reviewTypeKey\":\"post-change\"}}}]"),
            "V30",
            "Gate Policy",
            "gp-emergency-prod-tier-1");
    }

    [Fact]
    public void V31ASupportsRowWithAHolderFailsAsASafetyFloor()
    {
        ValidationOutcome outcome = Draft("[{\"op\":\"upsert\",\"entity\":\"decision-right\",\"key\":\"intake-s\",\"values\":{\"roleKey\":\"platform-admin\"}}]");
        AssertFails(outcome, "V31", "Decision Right", "intake-s");
        AssertFails(outcome, "V10", "Decision Right", "intake-s");
    }

    [Fact]
    public void V32AScopedGroupWithoutAnAdminFails()
    {
        // A portfolio group for configuration changes that only the change manager approves.
        ValidationOutcome outcome = Draft("[{\"op\":\"upsert\",\"entity\":\"approval-rule\",\"key\":\"configuration-change-finance\",\"values\":{\"label\":\"Finance configuration\","
            + "\"decisionTypeKey\":\"configuration-change\",\"portfolioId\":\"fixture-portfolio\",\"approverRoleKey\":\"change-manager\",\"approvalMode\":\"first-to-respond\"}}]");
        AssertFails(outcome, "V32", "Approval Rule", "configuration-change-finance");
    }

    [Fact]
    public void V33ALockedSettingCannotBeUnlockedOrChanged()
    {
        AssertFails(Draft("[{\"op\":\"upsert\",\"entity\":\"setting\",\"key\":\"ai-approval-lock\",\"values\":{\"isLocked\":false}}]"), "V33", "Setting", "ai-approval-lock", "setting.locked-changed");
        AssertFails(Draft("[{\"op\":\"retire\",\"entity\":\"setting\",\"key\":\"engine-acceptable-decision-types\"}]"), "V33", "Setting", "engine-acceptable-decision-types", "setting.locked-changed");

        // A client setting locked in the active version stays as it was.
        ContentSource active = ContentSource.Preset("balanced").WithPatch(ConfigurationPatch.FromOperations(
            CanonicalJson.Parse("[{\"op\":\"upsert\",\"entity\":\"setting\",\"key\":\"fixture-locked\",\"values\":{\"label\":\"Locked\",\"valueType\":\"number\",\"value\":3,\"isLocked\":true}}]"),
            PatchKind.Draft,
            "active"));
        ContentSource candidate = active.WithPatch(ConfigurationPatch.FromOperations(
            CanonicalJson.Parse("[{\"op\":\"upsert\",\"entity\":\"setting\",\"key\":\"fixture-locked\",\"values\":{\"value\":4}}]"),
            PatchKind.Draft,
            "candidate"));
        ValidationOutcome outcome = TestData.Validator.Validate(new ValidationRequest(candidate, ValidationMode.Draft, active));
        AssertFails(outcome, "V33", "Setting", "fixture-locked", "setting.locked-changed");
    }

    [Fact]
    public void V34FailsLoudlyWithoutFactsAndOnlyAtActivation()
    {
        string change = "[{\"op\":\"upsert\",\"entity\":\"role\",\"key\":\"configuration-admin\",\"values\":{\"duties\":\"Instructs and accepts configuration changes, and reviews them.\"}}]";
        Assert.Empty(Hits(Draft(change), "V34"));
        AssertFails(Draft(change, mode: ValidationMode.Activation), "V34", "Role", "configuration-admin");

        // A label change only is not protected.
        string label = "[{\"op\":\"upsert\",\"entity\":\"role\",\"key\":\"configuration-admin\",\"values\":{\"label\":\"Config admin\"}}]";
        Assert.Empty(Hits(Draft(label, mode: ValidationMode.Activation), "V34"));
    }

    [Fact]
    public void V35ChecksPrincipalKindsFromTheFacts()
    {
        var facts = new ValidationFacts(principalKinds: new[]
        {
            new KeyValuePair<string, PrincipalKind>(Group1, PrincipalKind.User),
            new KeyValuePair<string, PrincipalKind>(Principal, PrincipalKind.AgentIdentity),
        });
        ValidationOutcome outcome = Draft("[" + Binding("config-admins", "configuration-admin", Group1) + "," + Grant("agent-grant", "deployment-recorder", "2027-01-01") + "]", facts);
        AssertFails(outcome, "V35", "Role Group Binding", "config-admins");
        AssertFails(outcome, "V35", "Workload Grant", "agent-grant");

        // A grant for a human role fails too.
        AssertFails(Draft("[" + Grant("human-grant", "change-manager", "2027-01-01") + "]"), "V35", "Workload Grant", "human-grant");
    }

    [Fact]
    public void V37WarnsOnlyOnceBindingHasStarted()
    {
        Assert.Empty(Hits(TestData.Balanced(ValidationMode.Draft), "V37"));
        ValidationOutcome outcome = Draft("[" + Binding("config-admins", "configuration-admin", Group1) + "]");
        Assert.Equal("valid", outcome.Outcome);
        ValidationIssue[] hits = Hits(outcome, "V37");
        Assert.Equal(new[] { "architect", "change-board", "change-manager", "security-reviewer" }, hits.Select(h => h.Ref).OrderBy(r => r, StringComparer.Ordinal).ToArray());
        Assert.All(hits, h => Assert.Equal(ReasonSeverity.Warning, h.Severity));

        // Presets never hold bindings, so V37 never runs there.
        Assert.Empty(Hits(TestData.Balanced(ValidationMode.PresetBuild, "[" + Binding("config-admins", "configuration-admin", Group2) + "]"), "V37"));
    }

    [Fact]
    public void V38NarrowingRowsNarrowOnlyTheModeWithAPortfolio()
    {
        AssertFails(
            Draft("[{\"op\":\"upsert\",\"entity\":\"setting\",\"key\":\"draft-expiry-finance\",\"values\":{\"label\":\"Finance drafts\",\"valueType\":\"number\",\"value\":10,\"narrowsSettingKey\":\"draft-expiry-days\",\"portfolioId\":\"fixture-portfolio\"}}]"),
            "V38",
            "Setting",
            "draft-expiry-finance");
        AssertFails(Draft("[{\"op\":\"upsert\",\"entity\":\"setting\",\"key\":\"draft-expiry-days\",\"values\":{\"value\":null}}]"), "V38", "Setting", "draft-expiry-days");
        AssertFails(Draft("[{\"op\":\"upsert\",\"entity\":\"setting\",\"key\":\"intake-visibility\",\"values\":{\"value\":\"everyone\"}}]"), "V38", "Setting", "intake-visibility");
    }

    [Fact]
    public void V39ASettingValueMustMatchItsShape()
    {
        AssertFails(Draft("[{\"op\":\"upsert\",\"entity\":\"setting\",\"key\":\"working-days\",\"values\":{\"value\":[\"monday\",\"funday\"]}}]"), "V39", "Setting", "working-days");
    }

    [Fact]
    public void V40AnHttpUrlFails()
    {
        AssertFails(Draft("[{\"op\":\"upsert\",\"entity\":\"document-type\",\"key\":\"runbook\",\"values\":{\"templateUrl\":\"http://example.org/runbook\"}}]"), "V40", "Document Type", "runbook");
    }

    [Fact]
    public void V41WarnsAtEightyPercentAndFailsOverTheLimit()
    {
        ValidationOutcome clean = TestData.Balanced(ValidationMode.Draft);
        int size = CanonicalJson.Write(clean.Candidate!.Bundle, CanonicalProfile.Snapshot).Length;

        ValidationOutcome near = TestData.Validator.Validate(new ValidationRequest(ContentSource.Preset("balanced"), ValidationMode.Draft, facts: new ValidationFacts(snapshotCharacterLimit: size + (size / 10))));
        Assert.Equal("valid", near.Outcome);
        Assert.Equal(ReasonSeverity.Warning, Assert.Single(Hits(near, "V41")).Severity);

        ValidationOutcome over = TestData.Validator.Validate(new ValidationRequest(ContentSource.Preset("balanced"), ValidationMode.Draft, facts: new ValidationFacts(snapshotCharacterLimit: size - 1)));
        AssertFails(over, "V41");
    }

    [Fact]
    public void V42ARebasedDraftPassesAndADraftIsNotChecked()
    {
        string change = "[{\"op\":\"upsert\",\"entity\":\"tier\",\"key\":\"tier-1\",\"values\":{\"label\":\"Tier 1, low\"}}]";
        AssertFails(Draft(change, new ValidationFacts(baseVersionNumber: 3, activeVersionNumber: 4), ValidationMode.Activation), "V42");
        Assert.Empty(Hits(Draft(change, new ValidationFacts(baseVersionNumber: 3, activeVersionNumber: 4, isRebased: true), ValidationMode.Activation), "V42"));
        Assert.Empty(Hits(Draft(change, new ValidationFacts(baseVersionNumber: 3, activeVersionNumber: 4), ValidationMode.Draft), "V42"));
    }

    [Fact]
    public void V42ReadsTheBaseFromThePatchManifest()
    {
        string patch = "{\"schemaVersion\":\"1.0\",\"manifest\":{\"kind\":\"patch\",\"baseVersionNumber\":2,\"summary\":\"Label\"},\"operations\":[{\"op\":\"upsert\",\"entity\":\"tier\",\"key\":\"tier-1\",\"values\":{\"label\":\"Tier 1, low\"}}]}";
        ConfigurationPatch draft = ConfigurationPatch.FromDocument(CanonicalJson.Parse(patch), PatchKind.Draft, "test patch");
        ValidationOutcome outcome = TestData.Validator.Validate(new ValidationRequest(
            ContentSource.Preset("balanced").WithPatch(draft),
            ValidationMode.Activation,
            facts: new ValidationFacts(activeVersionNumber: 3)));
        AssertFails(outcome, "V42");
    }

    [Fact]
    public void NewRuleMessagesNameTheRuleTheEntityTheKeyAndTheFix()
    {
        ValidationIssue issue = Assert.Single(Hits(Draft("[{\"op\":\"upsert\",\"entity\":\"approval-rule\",\"key\":\"change-auth-tier-3\",\"values\":{\"approverRoleKey\":\"deployment-recorder\"}}]"), "V30"));
        Assert.Equal(
            "V30: Approval Rule `change-auth-tier-3` names Role `deployment-recorder` as approver. It is a workload role, held by a pipeline or the approval runner, and no AI or pipeline ever approves: name a human role.",
            issue.Message);
    }
}
