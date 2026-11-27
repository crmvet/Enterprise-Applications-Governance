using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Product.Engine.Core.Contract;
using Xunit;

namespace Product.Engine.Core.Tests;

// The engine contract lists (document 20, section 2.4; document 21, section 13.4; document 25, sections 5 and 15).
// WORKING DEFAULT (open design question 1): these tests pin the fixed meanings the recommended answer keeps.
public sealed class EngineContractTests
{
    // The 29 contracts of document 20, section 2.4, by their kebab-case names.
    private static readonly string[] Document20Names =
    {
        "decision-status-meaning", "record-status-meaning", "registration-status-meaning", "approval-outcome-meaning",
        "document-status-meaning", "claim-status-meaning", "deployment-result-meaning", "review-status-meaning",
        "lifecycle-meaning", "gate-level", "fail-mode", "approval-mode", "decision-right-letter", "actor-kind-meaning",
        "principal-kind", "deployment-route-meaning", "link-category", "change-management-mode", "finding-kind",
        "requirement-kind", "configuration-set-kind", "preset-kind", "draft-status-meaning", "patch-operation",
        "separation-rule-kind", "separation-subject", "separation-enforcement", "ai-only-code-approval-handling", "origin",
    };

    public static TheoryData<string> AllListNames()
    {
        var data = new TheoryData<string>();
        foreach (IContractList list in EngineContract.All)
        {
            data.Add(list.Name);
        }

        return data;
    }

    [Fact]
    public void Document20ListsAreAllPresentAndNoMore()
    {
        string[] engineMeanings = EngineContract.All
            .Where(list => list.Group == ContractGroup.EngineMeaning)
            .Select(list => list.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(Document20Names.OrderBy(name => name, StringComparer.Ordinal).ToArray(), engineMeanings);
    }

    [Fact]
    public void AddedListsAreGroupedBySource()
    {
        Assert.Equal(
            new[]
            {
                "claim-visibility", "data-access", "data-handling-rule", "data-origin", "data-taxonomy-change", "data-taxonomy-mode",
                "engine-acceptance-condition", "intake-visibility", "owner-subject", "owner-subject-kind", "record-access-mode", "setting-value-type",
            },
            NamesIn(ContractGroup.ConfigurationModel));
        Assert.Equal(
            new[] { "authorization-fact-source-kind", "condition-kind" },
            NamesIn(ContractGroup.EngineDesign));
        Assert.Equal(15, NamesIn(ContractGroup.OperationVocabulary).Length);
    }

    [Fact]
    public void ListNamesAreUniqueSortedAndKebabCase()
    {
        string[] names = EngineContract.All.Select(list => list.Name).ToArray();

        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(names.OrderBy(name => name, StringComparer.Ordinal).ToArray(), names);
        Assert.All(names, name => Assert.True(WireFormat.IsKebabCase(name), name));
    }

    [Fact]
    public void EveryPublicListFieldIsInTheCatalog()
    {
        IContractList[] fields = typeof(EngineContract)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => typeof(IContractList).IsAssignableFrom(field.FieldType))
            .Select(field => (IContractList)field.GetValue(null)!)
            .ToArray();

        Assert.Equal(fields.Length, EngineContract.All.Count);
        Assert.All(fields, list => Assert.Contains(list, EngineContract.All));
    }

    [Theory]
    [MemberData(nameof(AllListNames))]
    public void EveryWireValueRoundTrips(string listName)
    {
        Assert.True(EngineContract.TryGetList(listName, out IContractList? list));

        Assert.NotEmpty(list.WireValues);
        Assert.Equal(Enum.GetValues(list.MeaningType).Length, list.WireValues.Count);
        Assert.Equal(list.WireValues.Count, list.WireValues.Distinct(StringComparer.Ordinal).Count());
        foreach (string wire in list.WireValues)
        {
            Assert.True(WireFormat.IsKebabCase(wire), listName + ": " + wire);
            Assert.True(list.IsDefined(wire));
        }

        Assert.False(list.IsDefined(null));
        Assert.False(list.IsDefined(string.Empty));
        Assert.False(list.IsDefined("not-a-meaning-anywhere"));
    }

    [Fact]
    public void TypedListsParseAndFormatEveryMember()
    {
        AssertRoundTrip(EngineContract.DecisionStatusMeanings);
        AssertRoundTrip(EngineContract.RecordStatusMeanings);
        AssertRoundTrip(EngineContract.ApprovalOutcomeMeanings);
        AssertRoundTrip(EngineContract.GateLevels);
        AssertRoundTrip(EngineContract.ActorKindMeanings);
        AssertRoundTrip(EngineContract.PrincipalKinds);
        AssertRoundTrip(EngineContract.SeparationSubjects);
        AssertRoundTrip(EngineContract.ConditionKinds);
        AssertRoundTrip(EngineContract.GateVerdicts);
        AssertRoundTrip(EngineContract.EffectKinds);
    }

    // Wire values copied from the examples in document 21 (sections 8.2, 8.3 and 12) and document 25.
    [Theory]
    [InlineData("decision-status-meaning", "superseded")]
    [InlineData("record-status-meaning", "in-progress")]
    [InlineData("registration-status-meaning", "registered")]
    [InlineData("approval-outcome-meaning", "recommendation-only")]
    [InlineData("document-status-meaning", "missing")]
    [InlineData("claim-status-meaning", "dismissed")]
    [InlineData("deployment-result-meaning", "partly-succeeded")]
    [InlineData("deployment-result-meaning", "not-deployed")]
    [InlineData("review-status-meaning", "done")]
    [InlineData("lifecycle-meaning", "pre-live")]
    [InlineData("gate-level", "enforce")]
    [InlineData("fail-mode", "fail-closed")]
    [InlineData("approval-mode", "everyone-must-approve")]
    [InlineData("approval-mode", "first-to-respond")]
    [InlineData("decision-right-letter", "supports")]
    [InlineData("actor-kind-meaning", "ai-client-for-person")]
    [InlineData("actor-kind-meaning", "approval-runner")]
    [InlineData("principal-kind", "agent-user")]
    [InlineData("principal-kind", "managed-identity")]
    [InlineData("deployment-route-meaning", "person-in-app")]
    [InlineData("link-category", "microsoft-resource")]
    [InlineData("link-category", "external-change")]
    [InlineData("change-management-mode", "built-in")]
    [InlineData("finding-kind", "vanished")]
    [InlineData("requirement-kind", "owner-assigned")]
    [InlineData("configuration-set-kind", "working")]
    [InlineData("preset-kind", "overlay")]
    [InlineData("draft-status-meaning", "activated")]
    [InlineData("patch-operation", "retire")]
    [InlineData("separation-rule-kind", "roles-in-different-groups")]
    [InlineData("separation-subject", "instructing-admin")]
    [InlineData("separation-subject", "owner-role-holder")]
    [InlineData("separation-enforcement", "exception-decision")]
    [InlineData("ai-only-code-approval-handling", "stop")]
    [InlineData("origin", "local")]
    [InlineData("engine-acceptance-condition", "lowest-tier")]
    [InlineData("engine-acceptance-condition", "standard-model")]
    [InlineData("setting-value-type", "yes-no")]
    [InlineData("intake-visibility", "own")]
    [InlineData("claim-visibility", "admins-and-claimant")]
    [InlineData("record-access-mode", "open-read")]
    [InlineData("condition-kind", "component-kinds-within")]
    [InlineData("authorization-fact-source-kind", "itsm-step")]
    [InlineData("gate-verdict", "allow-with-exception")]
    [InlineData("window-state", "no-window")]
    [InlineData("code-approval-kind", "ai-only")]
    [InlineData("effect-kind", "activate-configuration")]
    [InlineData("impact-direction", "dependants")]
    public void DesignWireValuesParse(string listName, string wire)
    {
        Assert.True(EngineContract.TryGetList(listName, out IContractList? list), listName);
        Assert.True(list.IsDefined(wire), listName + ": " + wire);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Enforce")]
    [InlineData("ENFORCE")]
    [InlineData(" enforce")]
    [InlineData("enforce ")]
    [InlineData("en force")]
    [InlineData("enforce-")]
    [InlineData("enforced")]
    [InlineData("4")]
    public void ParsingIsStrictAndOrdinal(string? wire)
    {
        Assert.False(EngineContract.GateLevels.TryParse(wire, out GateLevel parsed));
        Assert.Equal(default(GateLevel), parsed);

        ContractValueException e = Assert.Throws<ContractValueException>(() => EngineContract.GateLevels.Parse(wire));
        Assert.Equal("gate-level", e.ContractName);
        Assert.Equal(wire, e.Value);
        Assert.Equal("input.contract-value-unknown", e.ReasonCode);
    }

    [Theory]
    [InlineData("approval flow")]
    [InlineData("approval-flow")]
    [InlineData("AI client for a person")]
    [InlineData("ai client for a person")]
    [InlineData("ai-client")]
    public void SpelledOutAndRetiredMeaningsDoNotParse(string wire)
    {
        // Document 21 uses "ai-client" as a row key and "ai-client-for-person" as the meaning. Only the meaning parses.
        // "Approval flow" is the retired name of the approval runner (28, issue 7).
        Assert.False(EngineContract.ActorKindMeanings.TryParse(wire, out _));
    }

    [Fact]
    public void TypedParseReturnsTheMeaning()
    {
        Assert.Equal(GateLevel.Enforce, EngineContract.GateLevels.Parse("enforce"));
        Assert.Equal(ActorKindMeaning.ApprovalRunner, EngineContract.ActorKindMeanings.Parse("approval-runner"));
        Assert.Equal(RecordStatusMeaning.InProgress, EngineContract.RecordStatusMeanings.Parse("in-progress"));
        Assert.Equal(PrincipalKind.ServicePrincipal, EngineContract.PrincipalKinds.Parse("service-principal"));
        Assert.Equal(DraftStatusMeaning.Submitted, EngineContract.DraftStatusMeanings.Parse("submitted"));
        Assert.Equal(PatchOperation.Upsert, EngineContract.PatchOperations.Parse("upsert"));
        Assert.Equal("fail-open", EngineContract.FailModes.ToWire(FailMode.FailOpen));
        Assert.Equal("ai-tool", EngineContract.DeploymentRouteMeanings.ToWire(DeploymentRouteMeaning.AiTool));
    }

    [Fact]
    public void FormattingAValueOutsideTheEnumIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EngineContract.GateLevels.ToWire((GateLevel)99));
    }

    [Fact]
    public void GateLevelsAreInLadderOrder()
    {
        Assert.Equal(new[] { "off", "record", "flag", "advise", "enforce" }, EngineContract.GateLevels.WireValues);

        int[] ranks = EngineContract.GateLevels.Values.Select(ContractFacts.LadderRank).ToArray();
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, ranks);
    }

    [Theory]
    [InlineData("change-management-mode")]
    [InlineData("ai-only-code-approval-handling")]
    [InlineData("intake-visibility")]
    [InlineData("claim-visibility")]
    [InlineData("record-access-mode")]
    public void SettingValueTypesOfDocument21Resolve(string contractName)
    {
        // Document 21, section 3.6 writes these value types as "contract:<name>".
        Assert.True(EngineContract.TryGetList(contractName, out _));
    }

    [Fact]
    public void UnknownListNamesDoNotResolve()
    {
        Assert.False(EngineContract.TryGetList(null, out _));
        Assert.False(EngineContract.TryGetList("Gate-Level", out _));
        Assert.False(EngineContract.TryGetList("gate level", out _));
    }

    [Theory]
    [InlineData("decision", "decision-status-meaning")]
    [InlineData("intake-request", "record-status-meaning")]
    [InlineData("change-request", "record-status-meaning")]
    [InlineData("business-application", "registration-status-meaning")]
    [InlineData("approval-record", "approval-outcome-meaning")]
    [InlineData("document-link", "document-status-meaning")]
    [InlineData("claim", "claim-status-meaning")]
    [InlineData("deployment-record", "deployment-result-meaning")]
    [InlineData("review", "review-status-meaning")]
    [InlineData("data-location", "registration-status-meaning")]
    [InlineData("data-mapping", "registration-status-meaning")]
    [InlineData("data-use", "registration-status-meaning")]
    public void StatusRowsMapToTheirMeaningList(string entityKey, string listName)
    {
        Assert.Equal(listName, EngineContract.StatusMeaningListsByEntity[entityKey].Name);
    }

    [Fact]
    public void OtherMeaningEntitiesMapToTheirList()
    {
        Assert.Equal(12, EngineContract.StatusMeaningListsByEntity.Count);
        Assert.Equal("lifecycle-meaning", EngineContract.EngineMeaningListsByEntity["lifecycle-stage"].Name);
        Assert.Equal("deployment-route-meaning", EngineContract.EngineMeaningListsByEntity["deployment-route"].Name);
        Assert.Equal("actor-kind-meaning", EngineContract.EngineMeaningListsByEntity["actor-kind"].Name);
    }

    [Fact]
    public void ActorKindsSplitIntoPersonAiAndWorkload()
    {
        foreach (ActorKindMeaning kind in EngineContract.ActorKindMeanings.Values)
        {
            int classes = (ContractFacts.IsPerson(kind) ? 1 : 0)
                + (ContractFacts.IsAiActor(kind) ? 1 : 0)
                + (ContractFacts.IsWorkloadActor(kind) ? 1 : 0);
            Assert.Equal(1, classes);
        }

        Assert.True(ContractFacts.IsAiActor(ActorKindMeaning.AiClientForPerson));
        Assert.True(ContractFacts.IsAiActor(ActorKindMeaning.AgentIdentity));
        Assert.True(ContractFacts.IsWorkloadActor(ActorKindMeaning.ApprovalRunner));
    }

    [Fact]
    public void OnlyAUserCanApproveAndOnlyAGroupHoldsARoleBinding()
    {
        Assert.Equal(new[] { PrincipalKind.User }, EngineContract.PrincipalKinds.Values.Where(ContractFacts.CanApprove).ToArray());
        Assert.Equal(new[] { PrincipalKind.Group }, EngineContract.PrincipalKinds.Values.Where(ContractFacts.CanBeRoleBindingTarget).ToArray());
    }

    [Fact]
    public void FinalDecisionStatusesMatchGuardRuleG05()
    {
        DecisionStatusMeaning[] final = EngineContract.DecisionStatusMeanings.Values.Where(ContractFacts.IsFinal).ToArray();

        Assert.Equal(
            new[] { DecisionStatusMeaning.Accepted, DecisionStatusMeaning.Rejected, DecisionStatusMeaning.Superseded, DecisionStatusMeaning.Withdrawn },
            final);
    }

    [Fact]
    public void MissingIsTheOnlyDocumentStatusNeverStored()
    {
        Assert.Equal(
            new[] { DocumentStatusMeaning.Missing },
            EngineContract.DocumentStatusMeanings.Values.Where(meaning => !ContractFacts.IsStoredOnRow(meaning)).ToArray());
    }

    [Fact]
    public void OnlyThePipelineStepRouteIsAPipeline()
    {
        Assert.Equal(
            new[] { DeploymentRouteMeaning.PipelineStep },
            EngineContract.DeploymentRouteMeanings.Values.Where(ContractFacts.IsPipeline).ToArray());
    }

    [Fact]
    public void AListThatMissesAnEnumMemberIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new ContractList<GateLevel>(
            "gate-level", "Gate level", ContractGroup.EngineMeaning, "test",
            new KeyValuePair<GateLevel, string>(GateLevel.Off, "off")));
    }

    [Fact]
    public void AListWithARepeatedOrBadWireValueIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new ContractList<FailMode>(
            "fail-mode", "Fail mode", ContractGroup.EngineMeaning, "test",
            new KeyValuePair<FailMode, string>(FailMode.FailOpen, "same"),
            new KeyValuePair<FailMode, string>(FailMode.FailClosed, "same")));
        Assert.Throws<ArgumentException>(() => new ContractList<FailMode>(
            "fail-mode", "Fail mode", ContractGroup.EngineMeaning, "test",
            new KeyValuePair<FailMode, string>(FailMode.FailOpen, "Fail Open"),
            new KeyValuePair<FailMode, string>(FailMode.FailClosed, "fail-closed")));
    }

    [Theory]
    [InlineData("tier-3", true)]
    [InlineData("a", true)]
    [InlineData("microsoft-coe-baseline", true)]
    [InlineData("x1-", true)]
    [InlineData("", false)]
    [InlineData("Tier-3", false)]
    [InlineData("3-tier", false)]
    [InlineData("-tier", false)]
    [InlineData("tier_3", false)]
    [InlineData("tier 3", false)]
    [InlineData("tier/3", false)]
    [InlineData("t\u00E9", false)]
    public void ConfigurationKeysFollowTheKeyFormat(string key, bool expected)
    {
        Assert.Equal(expected, ConfigurationKey.IsValid(key));
    }

    [Fact]
    public void ConfigurationKeysAreAtMost64Characters()
    {
        Assert.True(ConfigurationKey.IsValid("a" + new string('b', 63)));
        Assert.False(ConfigurationKey.IsValid("a" + new string('b', 64)));
        Assert.False(ConfigurationKey.IsValid(null));
    }

    [Theory]
    [InlineData("in-progress", true)]
    [InlineData("a1-b2", true)]
    [InlineData("in--progress", false)]
    [InlineData("in-", false)]
    [InlineData("-in", false)]
    [InlineData("In-progress", false)]
    [InlineData("in_progress", false)]
    [InlineData("1st", false)]
    public void WireValuesAreKebabCase(string value, bool expected)
    {
        Assert.Equal(expected, WireFormat.IsKebabCase(value));
    }

    private static string[] NamesIn(ContractGroup group)
    {
        return EngineContract.All.Where(list => list.Group == group).Select(list => list.Name).ToArray();
    }

    private static void AssertRoundTrip<T>(ContractList<T> list)
        where T : struct, Enum
    {
        foreach (T meaning in list.Values)
        {
            string wire = list.ToWire(meaning);
            Assert.True(list.TryParse(wire, out T parsed));
            Assert.Equal(meaning, parsed);
            Assert.Equal(meaning, list.Parse(wire));
        }

        Assert.Equal(Enum.GetValues(typeof(T)).Cast<T>().OrderBy(value => value).ToArray(), list.Values.OrderBy(value => value).ToArray());
    }
}
