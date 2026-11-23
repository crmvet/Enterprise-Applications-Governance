using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Configuration.Validation.Rules;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Operations;
using Product.Engine.Core.Snapshots;
using Product.Engine.Testing;
using Product.Engine.Testing.Invariants;
using Xunit;

namespace Product.Engine.Configuration.Tests;

// Operation tests, part 1: OP-01 to OP-05 (document 25, sections 5.3 to 5.7). The fixtures under fixtures/op-01 to op-05 hold
// the cases (part 2, OP-06, OP-07, OP-10 and OP-11, is in OperationPart2Tests; part 3, OP-12 to OP-14, in
// OperationPart3Tests); these tests add the invariants of document 25, section 10, generated over every shipped preset,
// and the typed API that adapters call.
public sealed class OperationTests
{
    private static readonly string[] ErrorOutcomes = { OperationOutcomes.InvalidInput, OperationOutcomes.Refused, OperationOutcomes.Unavailable };

    // ---- Fixtures ----

    [Theory]
    [InlineData("op-01")]
    [InlineData("op-02")]
    [InlineData("op-03")]
    [InlineData("op-04")]
    [InlineData("op-05")]
    [InlineData("op-06")]
    [InlineData("op-07")]
    [InlineData("op-08")]
    [InlineData("op-10")]
    [InlineData("op-11")]
    [InlineData("op-12")]
    [InlineData("op-13")]
    [InlineData("op-14")]
    public void EveryOperationHasPassingAndRefusingFixtures(string folder)
    {
        List<FixtureResult> results = FixtureRunner.ForSourceRoot(TestData.Root).RunFolder(Path.Combine(TestData.Root, "fixtures", folder)).ToList();

        Assert.All(results, r => Assert.True(r.Status == FixtureStatus.Passed, r.File + ": " + string.Join(" | ", r.Problems)));
        Assert.Contains(results, r => !ErrorOutcomes.Contains(r.ActualOutcome));
        Assert.Contains(results, r => ErrorOutcomes.Contains(r.ActualOutcome));
    }

    // The runner really judges operation fixtures: a wrong outcome, a wrong trace role and a fact on the wrong side
    // (the schema keeps input and facts apart) each fail.
    [Theory]
    [InlineData("\"outcome\": \"stop\"", "\"outcome\": \"allow\"", "outcome")]
    [InlineData("\"role\": \"won\"", "\"role\": \"dropped\"", "trace")]
    [InlineData("\"allowedRouteKeys\": [", "\"routeKey\": \"pipeline-step\", \"allowedRouteKeys\": [", "fixture schema")]
    public void AnOperationFixtureWithAWrongExpectationFails(string find, string replace, string problem)
    {
        string folder = TestData.TempFolder();
        try
        {
            string text = File.ReadAllText(Path.Combine(TestData.Root, "fixtures", "op-04", "portfolio-gate-policy-nearest-wins.json"));
            Assert.Contains(find, text);
            TestData.Write(folder, "portfolio-gate-policy-nearest-wins.json", text.Replace(find, replace));

            FixtureResult result = FixtureRunner.ForSourceRoot(TestData.Root).RunFile(Path.Combine(folder, "portfolio-gate-policy-nearest-wins.json"));

            Assert.Equal(FixtureStatus.Failed, result.Status);
            Assert.Contains(result.Problems, p => p.Contains(problem));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void OperationJsonRunsOnlyTheBuiltOperations()
    {
        EngineOperation[] built = Enum.GetValues(typeof(EngineOperation)).Cast<EngineOperation>().Where(OperationJson.IsBuilt).ToArray();
        Assert.Equal(
            new[]
            {
                EngineOperation.EvaluateTier, EngineOperation.ResolveRequiredApprovals, EngineOperation.EvaluateApprovalStatus, EngineOperation.EvaluateGateDecision,
                EngineOperation.CheckChangeWindow, EngineOperation.EvaluateDocumentationCompleteness, EngineOperation.EvaluateGateReadiness, EngineOperation.GuardWrite,
                EngineOperation.WalkImpact, EngineOperation.ComputeDueDates, EngineOperation.EvaluateStandardChange, EngineOperation.CompareGapCheckResult,
                EngineOperation.EvaluateLinkedAuthorization,
            },
            built);

        // OP-09 runs through the configuration assembly (ConfigurationValidator), never through OperationJson.
        Assert.Throws<NotSupportedException>(() => OperationJson.Evaluate(EngineOperation.ValidateConfigurationSet, null, OperationTestData.DatesOnlyContext(), JsonTreeNode.CreateObject()));
    }

    [Fact]
    public void NoSnapshotInForceIsUnavailable()
    {
        EvaluationResult<JsonTreeNode?> result = OperationJson.Evaluate(EngineOperation.EvaluateTier, null, OperationTestData.DatesOnlyContext(), OperationTestData.Json("{\"applicationKindKey\":\"agent\"}"));

        Assert.Equal(OperationOutcomes.Unavailable, result.Outcome);
        Assert.Null(result.Payload);
        Assert.Equal(new[] { "engine.unavailable" }, OperationTestData.Codes(result.Reasons));
    }

    [Fact]
    public void EveryOutcomeWordIsInTheCatalogOrAnErrorWord()
    {
        foreach (string folder in new[] { "op-01", "op-02", "op-03", "op-04", "op-05", "op-06", "op-07", "op-10", "op-11", "op-12", "op-13", "op-14" })
        {
            foreach (FixtureResult r in FixtureRunner.ForSourceRoot(TestData.Root).RunFolder(Path.Combine(TestData.Root, "fixtures", folder)))
            {
                OperationInfo info = OperationCatalog.All.First(o => o.Id == r.Operation);
                Assert.True(info.IsKnownOutcome(r.ActualOutcome) || ErrorOutcomes.Contains(r.ActualOutcome), r.File + " returned " + r.ActualOutcome);
            }
        }
    }

    // ---- OP-05 ----

    [Theory]
    [InlineData("2026-11-22T09:00:00Z", "inside", 0)]
    [InlineData("2026-11-22T08:00:00Z", "inside", 0)]
    [InlineData("2026-11-22T12:00:00Z", "after", 1)]
    [InlineData("2026-11-22T07:59:59Z", "before", 1)]
    [InlineData("2026-11-22T06:00:00Z", "before", 120)]
    [InlineData("2026-11-23T12:00:00Z", "after", 1440)]
    public void TheWindowStartIsInclusiveAndTheEndExclusive(string eventUtc, string state, int minutes)
    {
        var request = new WindowRequest(Utc("2026-11-22T08:00:00Z"), Utc("2026-11-22T12:00:00Z"), Utc(eventUtc));
        EvaluationResult<WindowResult?> result = ChangeWindowOperation.Evaluate(OperationTestData.DatesOnlyContext(), request);

        Assert.Equal(state, EngineContract.WindowStates.ToWire(result.Payload!.State));
        Assert.Equal(minutes, result.Payload.MinutesOutside);
        Assert.Equal(state == "inside" ? "inside" : "outside", result.Outcome);
    }

    [Theory]
    [InlineData("{\"eventUtc\":\"2026-11-22T09:00:00Z\",\"window\":\"x\"}", "input.malformed")]
    [InlineData("{\"eventUtc\":\"2026-11-22 09:00\"}", "input.malformed")]
    [InlineData("{\"eventUtc\":\"2026-11-22T09:00:00+01:00\"}", "input.malformed")]
    [InlineData("{}", "input.required")]
    [InlineData("{\"windowStartUtc\":\"2026-11-22T10:00:00Z\",\"windowEndUtc\":\"2026-11-22T09:00:00Z\",\"eventUtc\":\"2026-11-22T09:00:00Z\"}", "input.malformed")]
    public void BadWindowInputIsRefusedAsInput(string json, string code)
    {
        EvaluationResult<WindowResult?> result = ChangeWindowOperation.Evaluate(OperationTestData.DatesOnlyContext(), OperationTestData.Json(json));

        Assert.Equal(OperationOutcomes.InvalidInput, result.Outcome);
        Assert.Equal(new[] { code }, OperationTestData.Codes(result.Reasons));
    }

    [Fact]
    public void ALocalTimeIsNeverAccepted()
    {
        Assert.Throws<ArgumentException>(() => new WindowRequest(null, null, new DateTime(2026, 11, 22, 9, 0, 0, DateTimeKind.Local)));
    }

    // ---- OP-01 ----

    // Document 25, section 10: a more serious answer never lowers the tier. For every applicable question and
    // option, with every other answer at its lowest, the computed rank is the highest implied rank and never falls
    // when one answer moves to a more serious option.
    [Theory]
    [InlineData("balanced")]
    [InlineData("starter")]
    [InlineData("regulated")]
    [InlineData("microsoft-coe-baseline")]
    public void AMoreSeriousAnswerNeverLowersTheTier(string preset)
    {
        CompiledSnapshot compiled = OperationTestData.Snapshot(preset);
        ConfigurationSnapshot snapshot = compiled.Snapshot;
        foreach (string kind in new[] { "power-platform-app", "agent" })
        {
            List<SnapshotRow> questions = Active(snapshot, "tier-question").Where(q => AppliesTo(q, kind)).ToList();
            Dictionary<string, List<SnapshotRow>> options = questions.ToDictionary(
                q => q.Key,
                q => Active(snapshot, "tier-answer-option").Where(o => o.ParentKey == q.Key).OrderBy(o => Rank(snapshot, o)).ToList());
            Dictionary<string, string> lowest = options.ToDictionary(pair => pair.Key, pair => pair.Value[0].Key);

            foreach (SnapshotRow question in questions)
            {
                int previous = 0;
                foreach (SnapshotRow option in options[question.Key])
                {
                    var answers = lowest.Select(pair => new TierAnswer(pair.Key, pair.Key == question.Key ? option.Key : pair.Value)).ToList();
                    EvaluationResult<TierResult?> result = TierOperation.Evaluate(snapshot, OperationTestData.Context(compiled), new TierRequest(kind, answers));

                    Assert.Equal("computed", result.Outcome);
                    int rank = result.Payload!.ComputedTierRank!.Value;
                    int expected = answers.Select(a => Rank(snapshot, options[a.QuestionKey].First(o => o.Key == a.AnswerKey))).Max();
                    Assert.Equal(expected, rank);
                    Assert.True(rank >= previous, preset + " " + question.Key + "/" + option.Key + " lowered the tier");
                    previous = rank;
                }
            }
        }
    }

    [Fact]
    public void TheTypedAndTheWireRequestGiveTheSameResult()
    {
        CompiledSnapshot compiled = OperationTestData.Snapshot();
        var typed = new TierRequest("agent", new[] { new TierAnswer("agent-autonomy", "executes"), new TierAnswer("reach", "small-team") }, "tier-2");
        JsonTreeNode wire = OperationTestData.Json("{\"applicationKindKey\":\"agent\",\"proposedTierKey\":\"tier-2\",\"answers\":[{\"questionKey\":\"agent-autonomy\",\"answerKey\":\"executes\"},{\"questionKey\":\"reach\",\"answerKey\":\"small-team\"}]}");

        EvaluationResult<TierResult?> a = TierOperation.Evaluate(compiled.Snapshot, OperationTestData.Context(compiled), typed);
        EvaluationResult<TierResult?> b = TierOperation.Evaluate(compiled.Snapshot, OperationTestData.Context(compiled), wire);

        Assert.Equal("incomplete", a.Outcome);
        Assert.Equal(a.Outcome, b.Outcome);
        Assert.Equal(OperationTestData.Canonical(a.Payload!.ToJson()), OperationTestData.Canonical(b.Payload!.ToJson()));
        Assert.Equal(a.Reasons, b.Reasons);
        Assert.Equal(a.Trace, b.Trace);
        Assert.Equal(compiled.ContentHash, a.SnapshotHash);
        Assert.Equal(EngineInfo.Version, a.EngineVersion);
    }

    // ---- OP-02 ----

    // Document 25, section 10: no decision outside the locked list is accepted without a person's card. Over every
    // shipped selection, every active decision type, every tier (and none) and, for change authorization, every
    // change type and target stage: the answer is a card with at least one person to answer it, the locked list
    // acceptance, or the standard-model refusal. A shipped preset never gives an unavailable answer.
    [Fact]
    [Invariant("INV-02")]
    public void NoDecisionOutsideTheLockedListIsAcceptedWithoutACard()
    {
        int evaluations = 0;
        foreach (KeyValuePair<string, string[]> selection in OperationTestData.ShippedSelections())
        {
            CompiledSnapshot compiled = OperationTestData.Snapshot(selection.Key, null, selection.Value);
            ConfigurationSnapshot snapshot = compiled.Snapshot;
            string where = selection.Key + (selection.Value.Length == 0 ? string.Empty : "+" + string.Join("+", selection.Value));
            HashSet<string> workloadOrReadOnly = new(Active(snapshot, "role").Where(r => Flag(r, "isWorkloadRole") || Flag(r, "isReadOnly")).Select(r => r.Key), StringComparer.Ordinal);
            int lowestRank = Active(snapshot, "tier").Min(t => TierRank(t));
            List<string?> tiers = Active(snapshot, "tier").Select(t => (string?)t.Key).ToList();
            tiers.Add(null);

            foreach (SnapshotRow type in Active(snapshot, "decision-type"))
            {
                bool isChange = type.Key == EngineKeys.ChangeAuthorizationDecisionType;
                string?[] changeTypes = isChange ? Active(snapshot, "change-type").Select(c => c.Key).ToArray() : new string?[] { null };
                string?[] stages = isChange ? Active(snapshot, "target-stage").Select(s => s.Key).ToArray() : new string?[] { null };
                foreach (string? tier in tiers)
                {
                    foreach (string? changeType in changeTypes)
                    {
                        foreach (string? stage in stages)
                        {
                            var request = new ApprovalScopeRequest(type.Key, tier, null, changeType, "power-platform-app", stage, null, new StandardChangeFact(false));
                            EvaluationResult<ApprovalScopeResult?> result = RequiredApprovalsOperation.Evaluate(snapshot, OperationTestData.Context(compiled), request);
                            evaluations++;
                            string at = where + " " + type.Key + " " + (tier ?? "-") + " " + (changeType ?? "-") + " " + (stage ?? "-");

                            Assert.True(result.Outcome != OperationOutcomes.Unavailable && result.Outcome != OperationOutcomes.InvalidInput, at + ": " + string.Join(" | ", result.Reasons));
                            if (result.Outcome == "engine-accept")
                            {
                                Assert.True(type.Key == EngineKeys.IntakeDecisionType && tier != null && TierRank(snapshot, tier) == lowestRank, at + " was accepted without a card");
                                Assert.Empty(result.Payload!.Requirements);
                            }
                            else if (result.Outcome == OperationOutcomes.Refused)
                            {
                                Assert.True(isChange && changeType != null && Flag(Find(snapshot, "change-type", changeType), "requiresStandardModel"), at + " was refused");
                                Assert.Equal(new[] { "standard-model.does-not-qualify" }, OperationTestData.Codes(result.Reasons));
                            }
                            else
                            {
                                Assert.Equal("required", result.Outcome);
                                Assert.NotEmpty(result.Payload!.Requirements);
                                Assert.All(result.Payload.Requirements, r =>
                                {
                                    Assert.False(r.IsConsultation);
                                    Assert.True(r.MinimumCount >= 1, at);
                                    Assert.True((r.ApproverRoleKey == null) != (r.ApproverOwnerRoleKey == null), at);
                                    Assert.False(r.ApproverRoleKey != null && workloadOrReadOnly.Contains(r.ApproverRoleKey), at + " asks a workload or read-only role");
                                });
                            }
                        }
                    }
                }
            }
        }

        Assert.True(evaluations > 1000, "only " + evaluations + " evaluations");
    }

    // Document 25, OP-02 tests: a Minimum Count of 0 forced into a snapshot is refused at run time. The fixture schema
    // refuses a 0 in inline operations (rightly), so this case lives here, and OP-09 refuses it through V28.
    [Fact]
    public void AZeroMinimumCountForcedIntoASnapshotIsRefusedAtRunTime()
    {
        CompiledSnapshot compiled = OperationTestData.Snapshot("balanced", "[{\"op\":\"upsert\",\"entity\":\"approval-rule\",\"key\":\"change-auth-tier-3\",\"values\":{\"minimumCount\":0}}]");
        var request = new ApprovalScopeRequest("change-authorization", "tier-3", null, "normal", null, "production");

        EvaluationResult<ApprovalScopeResult?> result = RequiredApprovalsOperation.Evaluate(compiled.Snapshot, OperationTestData.Context(compiled), request);

        Assert.Equal(OperationOutcomes.Unavailable, result.Outcome);
        Assert.Null(result.Payload);
        Reason reason = Assert.Single(result.Reasons);
        Assert.Equal("approval-rule.zero-count", reason.Code);
        Assert.Equal("change-auth-tier-3", reason.Ref);
    }

    [Fact]
    public void TheEngineHonoursTheSameLockedListAsTheValidator()
    {
        Assert.Equal(
            WellKnown.ShippedEngineAcceptance.Select(pair => pair.Key + " " + pair.Value),
            EngineAcceptance.ShippedEntries.Select(entry => entry.DecisionTypeKey + " " + entry.ConditionWire));
    }

    [Fact]
    public void RequiredApprovalsFeedApprovalStatusThroughTheWireForm()
    {
        CompiledSnapshot compiled = OperationTestData.Snapshot();
        EvaluationContext context = OperationTestData.Context(compiled);
        EvaluationResult<ApprovalScopeResult?> scope = RequiredApprovalsOperation.Evaluate(compiled.Snapshot, context, new ApprovalScopeRequest("go-live", "tier-3"));
        Assert.Equal("required", scope.Outcome);

        string requirements = string.Join(",", scope.Payload!.Requirements.Select(r => OperationTestData.Canonical(r.ToJson())));
        string records = "{\"approvalRecordId\":\"r1\",\"ruleKey\":\"go-live-tier-3-owner\",\"outcome\":\"approved\",\"responderPrincipalId\":\"fixture-owner\","
            + "\"responderActorKind\":\"person\",\"responderPrincipalKind\":\"user\",\"responderHoldsRequiredRole\":true,\"respondedOnUtc\":\"2026-11-21T10:00:00Z\"},"
            + "{\"approvalRecordId\":\"r2\",\"ruleKey\":\"go-live-tier-3-security\",\"outcome\":\"approved\",\"responderPrincipalId\":\"fixture-security\","
            + "\"responderActorKind\":\"person\",\"responderPrincipalKind\":\"user\",\"responderHoldsRequiredRole\":true,\"hasTypedReason\":true,\"respondedOnUtc\":\"2026-11-21T11:00:00Z\"}";
        JsonTreeNode input = OperationTestData.Json("{\"requiredApprovals\":[" + requirements + "],\"records\":[" + records + "],\"scope\":{\"decisionTypeKey\":\"go-live\",\"tierKey\":\"tier-3\"},\"parties\":{\"proposerPrincipalId\":\"fixture-proposer\"}}");

        EvaluationResult<ApprovalStatusResult?> status = ApprovalStatusOperation.Evaluate(compiled.Snapshot, context, input);

        Assert.Equal("satisfied", status.Outcome);
        Assert.True(status.Payload!.CountsByRecord["r1"]);
        Assert.True(status.Payload.CountsByRecord["r2"]);
    }

    // ---- OP-03 ----

    // Document 25, section 10: an AI or workload actor never counts toward any approval. Only a person signed in as a
    // user counts, whatever else the record says.
    [Fact]
    public void OnlyAPersonSignedInAsAUserCounts()
    {
        CompiledSnapshot compiled = OperationTestData.Snapshot();
        RequiredApproval requirement = TierThreeChangeManager();
        foreach (ActorKindMeaning actor in Enum.GetValues(typeof(ActorKindMeaning)))
        {
            foreach (PrincipalKind principal in Enum.GetValues(typeof(PrincipalKind)))
            {
                var record = new ApprovalFact("r1", "change-auth-tier-3", ApprovalOutcomeMeaning.Approved, "fixture-responder", actor, principal, true, true, Utc("2026-11-21T10:00:00Z"), Utc("2026-11-26T10:00:00Z"));
                var request = new ApprovalStatusRequest(new[] { requirement }, new[] { record }, "change-authorization", "tier-3", "production", new SeparationFacts("fixture-proposer"));
                EvaluationResult<ApprovalStatusResult?> result = ApprovalStatusOperation.Evaluate(compiled.Snapshot, OperationTestData.Context(compiled), request);

                bool shouldCount = actor == ActorKindMeaning.Person && principal == PrincipalKind.User;
                Assert.Equal(shouldCount, result.Payload!.CountsByRecord["r1"]);
                Assert.Equal(shouldCount ? "satisfied" : "pending", result.Outcome);
            }
        }
    }

    [Fact]
    public void AnOwnerRoleHolderSeparationRuleRefusesTheHolder()
    {
        const string Operations = "[{\"op\":\"upsert\",\"entity\":\"separation-rule\",\"key\":\"data-owner-not-change-approver\",\"values\":{\"label\":\"The data owner does not authorize changes\","
            + "\"ruleKind\":\"approver-differs\",\"decisionTypeKey\":\"change-authorization\",\"mustDifferFrom\":\"owner-role-holder\",\"mustDifferFromOwnerRoleKey\":\"data-owner\",\"enforcement\":\"refuse\"}}]";
        CompiledSnapshot compiled = OperationTestData.Snapshot("balanced", Operations);
        var record = new ApprovalFact("r1", "change-auth-tier-3", ApprovalOutcomeMeaning.Approved, "fixture-data-owner", ActorKindMeaning.Person, PrincipalKind.User, true, true, Utc("2026-11-21T10:00:00Z"));
        var parties = new SeparationFacts("fixture-proposer", ownerRoleHolders: new Dictionary<string, IReadOnlyList<string>> { { "data-owner", new[] { "fixture-data-owner" } } });
        var request = new ApprovalStatusRequest(new[] { TierThreeChangeManager() }, new[] { record }, "change-authorization", "tier-3", "production", parties);

        EvaluationResult<ApprovalStatusResult?> result = ApprovalStatusOperation.Evaluate(compiled.Snapshot, OperationTestData.Context(compiled), request);

        Assert.Equal("pending", result.Outcome);
        Assert.Equal(new[] { "approval.separation-refused" }, OperationTestData.Codes(result.Reasons));
        Assert.Contains(result.Trace, t => t.Key == "data-owner-not-change-approver" && t.Role == RuleTraceRole.Won);
    }

    [Fact]
    public void ARoleHolderSeparationRuleCanFlag()
    {
        const string Operations = "[{\"op\":\"upsert\",\"entity\":\"separation-rule\",\"key\":\"architect-flagged\",\"values\":{\"label\":\"Flag an architect who authorizes a change\","
            + "\"ruleKind\":\"approver-differs\",\"decisionTypeKey\":\"change-authorization\",\"mustDifferFrom\":\"role-holder\",\"mustDifferFromRoleKey\":\"architect\",\"enforcement\":\"flag\"}}]";
        CompiledSnapshot compiled = OperationTestData.Snapshot("balanced", Operations);
        var record = new ApprovalFact("r1", "change-auth-tier-3", ApprovalOutcomeMeaning.Approved, "fixture-architect", ActorKindMeaning.Person, PrincipalKind.User, true, true, Utc("2026-11-21T10:00:00Z"));
        var parties = new SeparationFacts("fixture-proposer", roleHolders: new Dictionary<string, IReadOnlyList<string>> { { "architect", new[] { "fixture-architect" } } });
        var request = new ApprovalStatusRequest(new[] { TierThreeChangeManager() }, new[] { record }, "change-authorization", "tier-3", "production", parties);

        EvaluationResult<ApprovalStatusResult?> result = ApprovalStatusOperation.Evaluate(compiled.Snapshot, OperationTestData.Context(compiled), request);

        Assert.Equal("satisfied", result.Outcome);
        Assert.Equal(new[] { "approval.separation-flagged" }, OperationTestData.Codes(result.Reasons));
    }

    [Fact]
    public void AnAnsweredRecordMustSayWhoAnswered()
    {
        CompiledSnapshot compiled = OperationTestData.Snapshot();
        JsonTreeNode input = OperationTestData.Json("{\"requiredApprovals\":[" + OperationTestData.Canonical(TierThreeChangeManager().ToJson())
            + "],\"records\":[{\"approvalRecordId\":\"r1\",\"ruleKey\":\"change-auth-tier-3\",\"outcome\":\"approved\"}],\"scope\":{\"decisionTypeKey\":\"change-authorization\"}}");

        EvaluationResult<ApprovalStatusResult?> result = ApprovalStatusOperation.Evaluate(compiled.Snapshot, OperationTestData.Context(compiled), input);

        Assert.Equal(OperationOutcomes.InvalidInput, result.Outcome);
        Assert.Equal(new[] { "input.required" }, OperationTestData.Codes(result.Reasons));
        Assert.Throws<ArgumentException>(() => new ApprovalFact("r1", "change-auth-tier-3", ApprovalOutcomeMeaning.Approved));
    }

    // ---- OP-04 ----

    // Every shipped selection, every tier, change type and target stage, in both modes: an answer always comes, and
    // the verdict follows the ladder of document 25, section 5.6.
    [Fact]
    public void EveryShippedGateCombinationFollowsTheLadder()
    {
        foreach (KeyValuePair<string, string[]> selection in OperationTestData.ShippedSelections())
        {
            CompiledSnapshot compiled = OperationTestData.Snapshot(selection.Key, null, selection.Value);
            ConfigurationSnapshot snapshot = compiled.Snapshot;
            foreach (string tier in Active(snapshot, "tier").Select(r => r.Key))
            {
                foreach (string changeType in Active(snapshot, "change-type").Select(r => r.Key))
                {
                    foreach (string stage in Active(snapshot, "target-stage").Select(r => r.Key))
                    {
                        foreach (GateMode mode in new[] { GateMode.Check, GateMode.Record })
                        {
                            var change = new ChangeFact(ChangeManagementMode.BuiltIn, approvalOverall: ApprovalOverall.Pending);
                            var request = new GateRequest(mode, tier, stage, "ai-tool", new[] { "pipeline-step" }, Utc("2026-11-20T15:00:00Z"), changeType, null, change, CodeApprovalKind.AiOnly);
                            EvaluationResult<GateDecision?> result = GateDecisionOperation.Evaluate(snapshot, OperationTestData.Context(compiled), request);
                            string at = selection.Key + "+" + string.Join("+", selection.Value) + " " + tier + " " + changeType + " " + stage + " " + mode;

                            Assert.True(result.Payload != null, at + ": " + string.Join(" | ", result.Reasons));
                            GateDecision d = result.Payload!;
                            Assert.Equal(EngineContract.GateVerdicts.ToWire(d.Verdict), result.Outcome);
                            switch (d.GateLevel)
                            {
                                case GateLevel.Off:
                                    Assert.Equal(GateVerdict.NotApplicable, d.Verdict);
                                    Assert.False(d.IsOutsideWindow || d.IsOtherPath || d.IsMissingApproval || d.IsAiOnlyCodeApproval || d.Notify, at);
                                    break;
                                case GateLevel.Record:
                                case GateLevel.Flag:
                                    Assert.Equal(GateVerdict.Allow, d.Verdict);
                                    break;
                                case GateLevel.Advise:
                                    Assert.Equal(mode == GateMode.Check ? GateVerdict.Warn : GateVerdict.Allow, d.Verdict);
                                    break;
                                default:
                                    Assert.True(d.Verdict == GateVerdict.Stop || d.Verdict == GateVerdict.Allow || d.Verdict == GateVerdict.AllowWithException, at);
                                    Assert.True(mode == GateMode.Check || d.Verdict != GateVerdict.Stop, at + " stopped after the deploy");
                                    Assert.NotNull(d.FailMode);
                                    break;
                            }

                            Assert.Equal(mode == GateMode.Record && d.GateLevel != GateLevel.Off && d.GateLevel != GateLevel.Record, d.Notify);
                        }
                    }
                }
            }
        }
    }

    // Document 25, section 10: Enforce never stops for window or path alone. Every enforced combination of every
    // shipped selection, with an authorized change and a person's code approval, allows whatever the window and route.
    [Fact]
    [Invariant("INV-08")]
    public void EnforceNeverStopsForWindowOrPathAlone()
    {
        int enforced = 0;
        foreach (KeyValuePair<string, string[]> selection in OperationTestData.ShippedSelections())
        {
            CompiledSnapshot compiled = OperationTestData.Snapshot(selection.Key, null, selection.Value);
            foreach (SnapshotRow row in Active(compiled.Snapshot, "gate-policy").Where(r => Text(r, "gateLevel") == "enforce"))
            {
                foreach (bool outside in new[] { false, true })
                {
                    foreach (bool otherPath in new[] { false, true })
                    {
                        var change = new ChangeFact(ChangeManagementMode.BuiltIn, approvalOverall: ApprovalOverall.Satisfied, windowStartUtc: Utc("2026-11-20T08:00:00Z"), windowEndUtc: Utc(outside ? "2026-11-20T12:00:00Z" : "2026-11-20T18:00:00Z"));
                        var request = new GateRequest(GateMode.Check, Text(row, "tierKey")!, Text(row, "targetStageKey")!, otherPath ? "person-in-app" : "pipeline-step", new[] { "pipeline-step" }, Utc("2026-11-20T15:00:00Z"), Text(row, "changeTypeKey"), null, change, CodeApprovalKind.Person);
                        EvaluationResult<GateDecision?> result = GateDecisionOperation.Evaluate(compiled.Snapshot, OperationTestData.Context(compiled), request);

                        Assert.Equal("allow", result.Outcome);
                        Assert.Equal(outside, result.Payload!.IsOutsideWindow);
                        Assert.Equal(otherPath, result.Payload.IsOtherPath);
                        enforced++;
                    }
                }
            }
        }

        Assert.True(enforced > 0);
    }

    // Document 25, section 10: a missing gate policy never falls back to a level.
    [Theory]
    [InlineData("gp-development--tier-1--normal", "tier-1", "normal", "development")]
    [InlineData("gp-test--tier-2--standard", "tier-2", "standard", "test")]
    [InlineData("gp-prod-tier-3--normal", "tier-3", "normal", "production")]
    [InlineData("gp-emergency-prod-tier-2", "tier-2", "emergency", "production")]
    public void AMissingGatePolicyNeverFallsBack(string rowKey, string tier, string changeType, string stage)
    {
        CompiledSnapshot compiled = OperationTestData.Snapshot("balanced", "[{\"op\":\"retire\",\"entity\":\"gate-policy\",\"key\":\"" + rowKey + "\"}]");
        foreach (GateMode mode in new[] { GateMode.Check, GateMode.Record })
        {
            var request = new GateRequest(mode, tier, stage, "pipeline-step", new[] { "pipeline-step" }, Utc("2026-11-20T15:00:00Z"), changeType, null, new ChangeFact(ChangeManagementMode.BuiltIn, approvalOverall: ApprovalOverall.Satisfied));
            EvaluationResult<GateDecision?> result = GateDecisionOperation.Evaluate(compiled.Snapshot, OperationTestData.Context(compiled), request);

            Assert.Equal(OperationOutcomes.Unavailable, result.Outcome);
            Assert.Null(result.Payload);
            Assert.Equal(new[] { "gate-policy.missing" }, OperationTestData.Codes(result.Reasons));
        }
    }

    [Theory]
    [InlineData("2026-11-19T10:00:00Z", 2, "2026-11-23T10:00:00Z")]
    [InlineData("2026-11-21T10:00:00Z", 1, "2026-11-23T10:00:00Z")]
    [InlineData("2026-11-20T10:00:00Z", 5, "2026-11-27T10:00:00Z")]
    [InlineData("2026-11-20T10:00:00Z", 0, "2026-11-20T10:00:00Z")]
    public void TheFollowUpDueDateCountsWorkingDays(string eventUtc, int days, string dueUtc)
    {
        string operations = "[{\"op\":\"upsert\",\"entity\":\"gate-policy\",\"key\":\"gp-emergency-prod-tier-1\",\"values\":{\"emergencyRule\":{\"passWithoutApproval\":true,\"approvalDueWorkingDays\":" + days + "}}}]";
        CompiledSnapshot compiled = OperationTestData.Snapshot("regulated", operations);
        var request = new GateRequest(GateMode.Check, "tier-1", "production", "pipeline-step", new[] { "pipeline-step" }, Utc(eventUtc), "emergency", null, new ChangeFact(ChangeManagementMode.BuiltIn, approvalOverall: ApprovalOverall.Pending));

        EvaluationResult<GateDecision?> result = GateDecisionOperation.Evaluate(compiled.Snapshot, OperationTestData.Context(compiled), request);

        Assert.Equal("allow-with-exception", result.Outcome);
        Assert.Equal(Utc(dueUtc), result.Payload!.EmergencyFollowUpDueUtc);
    }

    [Fact]
    public void ABrokenWorkingDaysSettingIsReportedNotGuessed()
    {
        CompiledSnapshot compiled = OperationTestData.Snapshot("regulated", "[{\"op\":\"upsert\",\"entity\":\"setting\",\"key\":\"working-days\",\"values\":{\"value\":[\"someday\"]}}]");
        var request = new GateRequest(GateMode.Check, "tier-1", "production", "pipeline-step", new[] { "pipeline-step" }, Utc("2026-11-20T15:00:00Z"), "emergency", null, new ChangeFact(ChangeManagementMode.BuiltIn, approvalOverall: ApprovalOverall.Pending));

        EvaluationResult<GateDecision?> result = GateDecisionOperation.Evaluate(compiled.Snapshot, OperationTestData.Context(compiled), request);

        Assert.Equal(OperationOutcomes.Unavailable, result.Outcome);
        Assert.Equal(new[] { "setting.required" }, OperationTestData.Codes(result.Reasons));
    }

    [Fact]
    public void TheSameInputGivesTheSameAnswer()
    {
        CompiledSnapshot compiled = OperationTestData.Snapshot("regulated");
        JsonTreeNode input = OperationTestData.Json("{\"mode\":\"record\",\"tierKey\":\"tier-2\",\"changeTypeKey\":\"emergency\",\"targetStageKey\":\"production\",\"routeKey\":\"ai-tool\","
            + "\"allowedRouteKeys\":[\"pipeline-step\"],\"eventUtc\":\"2026-11-20T15:00:00Z\",\"codeApprovalKind\":\"ai-only\",\"change\":{\"changeManagementMode\":\"built-in\",\"approvalOverall\":\"pending\"}}");

        EvaluationResult<JsonTreeNode?> a = OperationJson.Evaluate(EngineOperation.EvaluateGateDecision, compiled.Snapshot, OperationTestData.Context(compiled), input);
        EvaluationResult<JsonTreeNode?> b = OperationJson.Evaluate(EngineOperation.EvaluateGateDecision, compiled.Snapshot, OperationTestData.Context(compiled), input);

        Assert.Equal(OperationTestData.Canonical(a.Payload), OperationTestData.Canonical(b.Payload));
        Assert.Equal(a.Reasons, b.Reasons);
        Assert.Equal(a.Trace, b.Trace);
    }

    // ---- Helpers ----

    private static RequiredApproval TierThreeChangeManager()
    {
        return new RequiredApproval(ApprovalRequirementSource.ApprovalRule, "change-auth-tier-3", null, "change-manager", null, false, ApprovalMode.FirstToRespond, null, 1, 5, true, null);
    }

    private static DateTime Utc(string text)
    {
        return DateTime.SpecifyKind(DateTime.Parse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal), DateTimeKind.Utc);
    }

    private static IEnumerable<SnapshotRow> Active(ConfigurationSnapshot snapshot, string entity)
    {
        return snapshot.TryGetEntity(entity, out SnapshotEntity? e) ? e.ActiveRows : Enumerable.Empty<SnapshotRow>();
    }

    private static SnapshotRow Find(ConfigurationSnapshot snapshot, string entity, string key)
    {
        Assert.True(snapshot.TryGetRow(entity, key, out SnapshotRow? row));
        return row!;
    }

    private static string? Text(SnapshotRow row, string column)
    {
        return row.TryGetColumn(column, out JsonTreeNode? value) && value.Kind == JsonTreeKind.String ? value.GetString() : null;
    }

    private static bool Flag(SnapshotRow row, string column)
    {
        return row.TryGetColumn(column, out JsonTreeNode? value) && value.Kind == JsonTreeKind.True;
    }

    private static bool AppliesTo(SnapshotRow question, string kind)
    {
        return !question.TryGetColumn("appliesToKindKeys", out JsonTreeNode? kinds) || kinds.Items.Count == 0 || kinds.Items.Any(k => k.GetString() == kind);
    }

    private static int TierRank(SnapshotRow tier)
    {
        long value = 0;
        Assert.True(tier.TryGetColumn("rank", out JsonTreeNode? rank) && rank.TryGetInt64(out value));
        return (int)value;
    }

    private static int TierRank(ConfigurationSnapshot snapshot, string tierKey)
    {
        return TierRank(Find(snapshot, "tier", tierKey));
    }

    private static int Rank(ConfigurationSnapshot snapshot, SnapshotRow option)
    {
        return TierRank(snapshot, Text(option, "impliedTierKey")!);
    }
}
