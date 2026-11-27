using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Operations;
using Product.Engine.Core.Snapshots;
using Xunit;

namespace Product.Engine.Configuration.Tests;

// The proposal right check (document 24, tool 17; Tool Policy row save-decision-proposal; B24-5): the person behind a
// call may propose a decision only when they hold a responsible, accountable or consulted Decision Right (C31) for the
// decision type in the decision's scope, through a product role or an owner role on the subject's application. The
// Decision Right groups resolve as OP-02 resolves the accountable letter: the most specific matching group wins.
public sealed class ProposalRightTests
{
    private static readonly string[] None = Array.Empty<string>();

    [Fact]
    public void BalancedLetsTheAccountableBusinessOwnerProposeARiskAcceptance()
    {
        CompiledSnapshot balanced = OperationTestData.Snapshot();

        EvaluationResult<ProposalRightResult?> owner = Check(balanced, "risk-acceptance", "tier-2", None, new[] { "business-owner" });
        Assert.Equal(ProposalRightsOperation.Allowed, owner.Outcome);
        Assert.True(owner.Payload!.Allowed);
        Assert.Equal("risk-acceptance-a", owner.Payload.GrantedBy!.DecisionRightKey);
        Assert.Equal(DecisionRightLetter.Accountable, owner.Payload.GrantedBy.Letter);
        Assert.Empty(owner.Reasons);

        // The architect role is in the Tool Policy row of save_decision_proposal, but holds no letter for this type.
        EvaluationResult<ProposalRightResult?> architect = Check(balanced, "risk-acceptance", "tier-2", new[] { "architect", "requester" }, None);
        Assert.Equal(OperationOutcomes.Refused, architect.Outcome);
        Assert.False(architect.Payload!.Allowed);
        Assert.Null(architect.Payload.GrantedBy);
        ProposalRightHolder holder = Assert.Single(architect.Payload.Holders);
        Assert.Equal("risk-acceptance-a", holder.DecisionRightKey);
        Assert.Equal("business-owner", holder.OwnerRoleKey);
        Reason reason = Assert.Single(architect.Reasons);
        Assert.Equal("decision.propose-not-allowed", reason.Code);
        Assert.Equal(ReasonSeverity.Error, reason.Severity);
        Assert.Equal("Decision Right", reason.Entity);
        Assert.Equal("risk-acceptance", reason.Ref);
        Assert.Contains("business-owner", reason.Message);

        // A product role is not an owner role: a role key spelled like the owner role grants nothing.
        Assert.Equal(OperationOutcomes.Refused, Check(balanced, "risk-acceptance", "tier-2", new[] { "business-owner" }, None).Outcome);
    }

    [Theory]
    [InlineData("priority", "pmo", "priority-r", DecisionRightLetter.Responsible)]
    [InlineData("portfolio", "pmo", "portfolio-r", DecisionRightLetter.Responsible)]
    [InlineData("retirement", "platform-admin", "retirement-r", DecisionRightLetter.Responsible)]
    [InlineData("tier", "platform-admin", "tier-a", DecisionRightLetter.Accountable)]
    [InlineData("design", "architect", "design-a", DecisionRightLetter.Accountable)]
    [InlineData("exception", "architect", "exception-a", DecisionRightLetter.Accountable)]
    [InlineData("autonomy-level", "ai-lead", "autonomy-level-c", DecisionRightLetter.Consulted)]
    [InlineData("autonomy-level", "security-reviewer", "autonomy-level-a", DecisionRightLetter.Accountable)]
    public void ARoleHoldingAProposingLetterMayPropose(string type, string role, string row, DecisionRightLetter letter)
    {
        EvaluationResult<ProposalRightResult?> result = Check(OperationTestData.Snapshot(), type, "tier-2", new[] { role }, None);

        Assert.Equal(ProposalRightsOperation.Allowed, result.Outcome);
        Assert.Equal(row, result.Payload!.GrantedBy!.DecisionRightKey);
        Assert.Equal(letter, result.Payload.GrantedBy.Letter);
    }

    // The granting row is chosen responsible first, then accountable, then consulted; every holder is listed in that order.
    [Fact]
    public void TheResponsibleRowGrantsBeforeTheAccountableRow()
    {
        EvaluationResult<ProposalRightResult?> result = Check(OperationTestData.Snapshot(), "priority", null, new[] { "pmo" }, new[] { "sponsor" });

        Assert.Equal("priority-r", result.Payload!.GrantedBy!.DecisionRightKey);
        Assert.Equal(new[] { "priority-r", "priority-a" }, result.Payload.Holders.Select(h => h.DecisionRightKey).ToArray());
    }

    // OP-02's rule: the single most specific matching group wins and broader groups drop out. At tier 3 the change manager
    // is accountable and the business owner consulted; at tier 1 only the business owner holds a letter; a tier 3 emergency
    // has its own group with only the change manager.
    [Fact]
    public void TheMostSpecificDecisionRightGroupDecidesWhoMayPropose()
    {
        CompiledSnapshot balanced = OperationTestData.Snapshot();

        EvaluationResult<ProposalRightResult?> tier3 = Check(balanced, "change-authorization", "tier-3", None, new[] { "business-owner" }, "normal");
        Assert.Equal(ProposalRightsOperation.Allowed, tier3.Outcome);
        Assert.Equal("change-auth-tier-3-c", tier3.Payload!.GrantedBy!.DecisionRightKey);
        Assert.Equal(new[] { "change-auth-tier-3-a", "change-auth-tier-3-c" }, tier3.Payload.Holders.Select(h => h.DecisionRightKey).ToArray());
        Assert.Contains(tier3.Trace, t => t.Key == "change-auth-a" && t.Role == RuleTraceRole.Dropped);
        Assert.Contains(tier3.Trace, t => t.Key == "change-auth-tier-3-a" && t.Role == RuleTraceRole.Won);
        Assert.Equal(ProposalRightsOperation.Allowed, Check(balanced, "change-authorization", "tier-3", new[] { "change-manager" }, None, "normal").Outcome);

        Assert.Equal(ProposalRightsOperation.Allowed, Check(balanced, "change-authorization", "tier-1", None, new[] { "business-owner" }, "normal").Outcome);
        Assert.Equal(OperationOutcomes.Refused, Check(balanced, "change-authorization", "tier-1", new[] { "change-manager" }, None, "normal").Outcome);

        EvaluationResult<ProposalRightResult?> emergency = Check(balanced, "change-authorization", "tier-3", None, new[] { "business-owner" }, "emergency");
        Assert.Equal(OperationOutcomes.Refused, emergency.Outcome);
        Assert.Equal(new[] { "change-auth-tier-3-emergency-a" }, emergency.Payload!.Holders.Select(h => h.DecisionRightKey).ToArray());
    }

    // A decision right scoped to a tier takes the type over at that tier, and only there.
    [Fact]
    public void ATierScopedGroupReplacesTheBroadGroupAtItsTier()
    {
        CompiledSnapshot scoped = OperationTestData.Snapshot("balanced", "[{\"op\":\"upsert\",\"entity\":\"decision-right\",\"key\":\"risk-acceptance-tier-3-a\",\"values\":{\"label\":\"Tier 3 risk acceptance\","
            + "\"decisionTypeKey\":\"risk-acceptance\",\"tierKey\":\"tier-3\",\"letter\":\"accountable\",\"roleKey\":\"security-reviewer\"}}]");

        Assert.Equal(OperationOutcomes.Refused, Check(scoped, "risk-acceptance", "tier-3", None, new[] { "business-owner" }).Outcome);
        Assert.Equal(ProposalRightsOperation.Allowed, Check(scoped, "risk-acceptance", "tier-3", new[] { "security-reviewer" }, None).Outcome);
        Assert.Equal(ProposalRightsOperation.Allowed, Check(scoped, "risk-acceptance", "tier-2", None, new[] { "business-owner" }).Outcome);
        Assert.Equal(OperationOutcomes.Refused, Check(scoped, "risk-acceptance", "tier-2", new[] { "security-reviewer" }, None).Outcome);
    }

    // Informed is told after, and supports names no holder (AI only): neither proposes. The Microsoft CoE baseline makes
    // the business owner informed on go-live and the product owner consulted.
    [Fact]
    public void InformedAndSupportsRowsNeverLetAnyonePropose()
    {
        CompiledSnapshot coe = OperationTestData.Snapshot("microsoft-coe-baseline");
        EvaluationResult<ProposalRightResult?> informed = Check(coe, "go-live", "tier-2", None, new[] { "business-owner" });
        Assert.Equal(OperationOutcomes.Refused, informed.Outcome);
        Assert.DoesNotContain(informed.Payload!.Holders, h => h.Letter == DecisionRightLetter.Informed);
        Assert.Equal(ProposalRightsOperation.Allowed, Check(coe, "go-live", "tier-2", None, new[] { "product-owner" }).Outcome);

        EvaluationResult<ProposalRightResult?> design = Check(OperationTestData.Snapshot(), "design", "tier-2", None, None);
        Assert.Equal(OperationOutcomes.Refused, design.Outcome);
        Assert.Equal(new[] { "design-a" }, design.Payload!.Holders.Select(h => h.DecisionRightKey).ToArray());
    }

    [Fact]
    public void AnUnknownDecisionTypeIsInvalidInput()
    {
        EvaluationResult<ProposalRightResult?> result = Check(OperationTestData.Snapshot(), "no-such-type", null, new[] { "platform-admin" }, None);

        Assert.Equal(OperationOutcomes.InvalidInput, result.Outcome);
        Assert.Null(result.Payload);
        Assert.Equal(new[] { "input.unknown-key" }, OperationTestData.Codes(result.Reasons));
    }

    // A decision type with no Decision Right group in force has nobody to propose or decide it: the engine refuses and
    // reports the broken configuration (document 25, rule 10), as OP-02 does for the accountable letter.
    [Fact]
    public void NoMatchingDecisionRightGroupIsABrokenConfiguration()
    {
        CompiledSnapshot broken = OperationTestData.Snapshot("balanced", "[{\"op\":\"retire\",\"entity\":\"decision-right\",\"key\":\"design-a\"},{\"op\":\"retire\",\"entity\":\"decision-right\",\"key\":\"design-s\"}]");

        EvaluationResult<ProposalRightResult?> result = Check(broken, "design", "tier-2", new[] { "architect" }, None);

        Assert.Equal(OperationOutcomes.Unavailable, result.Outcome);
        Assert.Null(result.Payload);
        Assert.Equal(new[] { "decision-right.accountable" }, OperationTestData.Codes(result.Reasons));
    }

    [Fact]
    public void TheWireFormAnswersAsTheTypedForm()
    {
        CompiledSnapshot balanced = OperationTestData.Snapshot();
        JsonTreeNode input = OperationTestData.Json("{\"decisionTypeKey\":\"retirement\",\"tierKey\":\"tier-2\",\"applicationKindKey\":\"power-platform-app\",\"portfolioPath\":[\"fixture-portfolio-1\"],"
            + "\"roleKeys\":[\"maker\"],\"ownerRoleKeys\":[\"business-owner\"]}");

        EvaluationResult<ProposalRightResult?> result = ProposalRightsOperation.Evaluate(balanced.Snapshot, OperationTestData.Context(balanced), input);

        Assert.Equal(ProposalRightsOperation.Allowed, result.Outcome);
        Assert.Equal("{\"allowed\":true,\"grantedBy\":{\"decisionRightKey\":\"retirement-a\",\"letter\":\"accountable\",\"ownerRoleKey\":\"business-owner\"},\"holders\":["
            + "{\"decisionRightKey\":\"retirement-r\",\"letter\":\"responsible\",\"roleKey\":\"platform-admin\"},{\"decisionRightKey\":\"retirement-a\",\"letter\":\"accountable\",\"ownerRoleKey\":\"business-owner\"}]}",
            OperationTestData.Canonical(result.Payload!.ToJson()));

        EvaluationResult<ProposalRightResult?> unknown = ProposalRightsOperation.Evaluate(balanced.Snapshot, OperationTestData.Context(balanced), OperationTestData.Json("{\"decisionTypeKey\":\"retirement\",\"proposer\":\"someone\"}"));
        Assert.Equal(OperationOutcomes.InvalidInput, unknown.Outcome);
        Assert.Equal(new[] { "input.malformed" }, OperationTestData.Codes(unknown.Reasons));
    }

    // Generated over every shipped preset selection, every decision type, every tier (and none) and, for change
    // authorization, every change type: the check always answers; exactly one accountable holder is listed; every listed
    // holder, alone, may propose; nobody with no role and no owner role may; and no informed or supports row is listed.
    [Fact]
    public void EveryShippedSelectionHasAnAccountableProposerForEveryScope()
    {
        int evaluations = 0;
        foreach (KeyValuePair<string, string[]> selection in OperationTestData.ShippedSelections())
        {
            CompiledSnapshot compiled = OperationTestData.Snapshot(selection.Key, null, selection.Value);
            ConfigurationSnapshot snapshot = compiled.Snapshot;
            string where = selection.Key + (selection.Value.Length == 0 ? string.Empty : "+" + string.Join("+", selection.Value));
            List<string?> tiers = Active(snapshot, "tier").Select(t => (string?)t.Key).Concat(new string?[] { null }).ToList();
            foreach (SnapshotRow type in Active(snapshot, "decision-type"))
            {
                string?[] changeTypes = type.Key == EngineKeys.ChangeAuthorizationDecisionType ? Active(snapshot, "change-type").Select(c => (string?)c.Key).ToArray() : new string?[] { null };
                foreach (string? tier in tiers)
                {
                    foreach (string? changeType in changeTypes)
                    {
                        string at = where + " " + type.Key + " " + (tier ?? "-") + " " + (changeType ?? "-");
                        EvaluationResult<ProposalRightResult?> nobody = Check(compiled, type.Key, tier, None, None, changeType);
                        evaluations++;
                        Assert.True(nobody.Outcome == OperationOutcomes.Refused, at + ": " + nobody.Outcome + " " + string.Join(" | ", nobody.Reasons));
                        IReadOnlyList<ProposalRightHolder> holders = nobody.Payload!.Holders;
                        Assert.Single(holders, h => h.Letter == DecisionRightLetter.Accountable);
                        Assert.All(holders, h => Assert.True(h.Letter != DecisionRightLetter.Informed && h.Letter != DecisionRightLetter.Supports, at));
                        foreach (ProposalRightHolder holder in holders)
                        {
                            EvaluationResult<ProposalRightResult?> alone = Check(compiled, type.Key, tier, holder.RoleKey == null ? None : new[] { holder.RoleKey }, holder.OwnerRoleKey == null ? None : new[] { holder.OwnerRoleKey }, changeType);
                            evaluations++;
                            Assert.True(alone.Outcome == ProposalRightsOperation.Allowed, at + " " + holder.Describe());
                        }
                    }
                }
            }
        }

        Assert.True(evaluations > 1000, "only " + evaluations + " evaluations");
    }

    private static EvaluationResult<ProposalRightResult?> Check(CompiledSnapshot compiled, string type, string? tier, string[] roles, string[] ownerRoles, string? changeType = null)
    {
        var scope = new ApprovalScopeRequest(type, tier, null, changeType, "power-platform-app");
        return ProposalRightsOperation.Evaluate(compiled.Snapshot, OperationTestData.Context(compiled), new ProposalRightRequest(scope, roles, ownerRoles));
    }

    private static IEnumerable<SnapshotRow> Active(ConfigurationSnapshot snapshot, string entity)
    {
        return snapshot.TryGetEntity(entity, out SnapshotEntity? e) ? e.ActiveRows : Enumerable.Empty<SnapshotRow>();
    }
}
