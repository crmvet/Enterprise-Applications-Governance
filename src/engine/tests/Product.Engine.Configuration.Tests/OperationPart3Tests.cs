using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Operations;
using Product.Engine.Core.Snapshots;
using Xunit;

namespace Product.Engine.Configuration.Tests;

// Operation tests, part 3: OP-12, OP-13 and OP-14 (document 25, sections 5.13 to 5.15). The fixtures under fixtures/op-12, op-13 and op-14
// hold the cases (OperationTests runs them). These tests add checks generated over every shipped preset selection,
// independent re-computations of the conditions, the claim lifecycle and the authorization fact, the hand-over of
// OP-12 to OP-02 and of OP-14 to OP-04 through the wire form, and the broken configurations the fixture schema rightly
// refuses as inline operations.
public sealed class OperationPart3Tests
{
    private const string Run1 = "test-run-1";
    private const string Estate = "power-platform-inventory";
    private const string Canvas = "microsoft.powerapps/canvasapps";

    private static readonly DateTime AsOf = OperationTestData.AsOf;

    private static readonly string ModelOperations = "[{\"op\":\"upsert\",\"entity\":\"standard-change-model\",\"key\":\"labels-only\",\"values\":{\"label\":\"Labels only\",\"changeTypeKey\":\"standard\","
        + "\"conditions\":[{\"conditionKind\":\"max-tier-rank\",\"value\":2},{\"conditionKind\":\"stated\",\"key\":\"labels-only\",\"label\":\"Labels only\"}],\"approvedByDecisionId\":\"test-decision-1\"}}]";

    // ---- OP-12 ----

    // Each of the three max kinds, for every limit and fact from 0 to 6: the change qualifies exactly when the fact is
    // at most the limit. A separate comparison here is the judge.
    [Theory]
    [InlineData("max-tier-rank", "tierRank")]
    [InlineData("max-affected-applications", "affectedApplicationCount")]
    [InlineData("max-window-minutes", "windowMinutes")]
    public void AMaxConditionQualifiesExactlyUpToItsLimit(string kind, string fact)
    {
        for (int limit = 0; limit <= 5; limit++)
        {
            CompiledSnapshot compiled = OperationTestData.Snapshot("balanced", Model("m", "{\"conditionKind\":\"" + kind + "\",\"value\":" + limit.ToString(CultureInfo.InvariantCulture) + "}"));
            for (int value = kind == "max-affected-applications" ? 1 : 0; value <= 6; value++)
            {
                JsonTreeNode input = OperationTestData.Json("{\"changeTypeKey\":\"standard\",\"change\":{\"" + fact + "\":" + value.ToString(CultureInfo.InvariantCulture) + "},"
                    + "\"models\":[{\"modelKey\":\"m\",\"approvedByDecisionStatus\":\"accepted\"}]}");
                EvaluationResult<StandardChangeResult?> result = StandardChangeOperation.Evaluate(compiled.Snapshot, OperationTestData.Context(compiled), input);

                Assert.Equal(value <= limit ? "qualifies" : "does-not-qualify", result.Outcome);
                Assert.Equal(value <= limit ? 0 : 1, result.Payload!.FailedConditions.Count);
            }
        }
    }

    // The two list kinds, for every subset of four keys against every non-empty allowed subset: the change qualifies
    // exactly when it states at least one key and every stated key is allowed.
    [Theory]
    [InlineData("target-stages-in", "targetStageKeys")]
    [InlineData("component-kinds-within", "componentKindKeys")]
    public void AListConditionQualifiesExactlyWhenEveryKeyIsAllowed(string kind, string fact)
    {
        string[] keys = { "k-a", "k-b", "k-c", "k-d" };
        for (int allowedMask = 1; allowedMask < 16; allowedMask++)
        {
            string[] allowed = Subset(keys, allowedMask);
            CompiledSnapshot compiled = OperationTestData.Snapshot("balanced", Model("m", "{\"conditionKind\":\"" + kind + "\",\"value\":" + JsonList(allowed) + "}"));
            for (int statedMask = 0; statedMask < 16; statedMask++)
            {
                string[] stated = Subset(keys, statedMask);
                JsonTreeNode input = OperationTestData.Json("{\"changeTypeKey\":\"standard\",\"change\":{\"" + fact + "\":" + JsonList(stated) + "},"
                    + "\"models\":[{\"modelKey\":\"m\",\"approvedByDecisionStatus\":\"accepted\"}]}");
                EvaluationResult<StandardChangeResult?> result = StandardChangeOperation.Evaluate(compiled.Snapshot, OperationTestData.Context(compiled), input);

                bool expected = stated.Length > 0 && stated.All(allowed.Contains);
                Assert.True(expected == (result.Outcome == "qualifies"), kind + " allowed " + string.Join(",", allowed) + " stated " + string.Join(",", stated) + ": " + result.Outcome);
            }
        }
    }

    // Document 25, section 5.13: the same input always picks the same model, whatever order the facts come in.
    [Fact]
    public void TheSameModelIsPickedWhateverTheFactOrder()
    {
        string operations = "[" + Upsert("c-model", "{\"conditionKind\":\"max-tier-rank\",\"value\":3}") + "," + Upsert("a-model", "{\"conditionKind\":\"max-tier-rank\",\"value\":3}") + ","
            + Upsert("b-model", "{\"conditionKind\":\"max-tier-rank\",\"value\":3}") + "]";
        CompiledSnapshot compiled = OperationTestData.Snapshot("balanced", operations);
        string[] models = { "c-model", "a-model", "b-model" };
        foreach (string[] order in Permutations(models))
        {
            var request = new StandardChangeRequest("standard", null, new StandardChangeFacts(tierRank: 1), order.Select(m => new ModelApprovalFact(m, DecisionStatusMeaning.Accepted)));
            EvaluationResult<StandardChangeResult?> result = StandardChangeOperation.Evaluate(compiled.Snapshot, OperationTestData.Context(compiled), request);

            // Balanced's own model (document 21, B21-9) has no approving decision here, so it is skipped.
            Assert.Equal("a-model", result.Payload!.ModelKey);
            Assert.Equal(new[] { "a-model:won", "b-model:dropped", "c-model:dropped", "routine-maintenance:skipped" }, result.Trace.Where(t => t.Entity == "Standard Change Model").Select(t => t.Key + ":" + EngineContract.RuleTraceRoles.ToWire(t.Role)).OrderBy(s => s, StringComparer.Ordinal).ToArray());
        }
    }

    // Over every shipped selection: the one model a preset ships (Balanced's routine-maintenance, document 21, B21-9) has no
    // approving decision until a person in the install approves it, so a standard change never qualifies until then, and
    // a type with no model has no candidate; an emergency type never qualifies; and with a client's model added, the OP-12 payload, passed on unchanged
    // through the wire form, makes OP-02 accept the standard change and cite the model's approving decision. A change
    // that does not qualify is refused by OP-02, never sent to a broader group.
    [Fact]
    public void TheStandardChangeResultFeedsRequiredApprovalsOnEveryShippedSelection()
    {
        int checkedSelections = 0;
        foreach (KeyValuePair<string, string[]> selection in OperationTestData.ShippedSelections())
        {
            string at = selection.Key + "+" + string.Join("+", selection.Value);
            CompiledSnapshot shipped = OperationTestData.Snapshot(selection.Key, null, selection.Value);
            foreach (SnapshotRow type in Active(shipped.Snapshot, "change-type"))
            {
                var none = new StandardChangeRequest(type.Key, null, new StandardChangeFacts(tierRank: 1, statedConditionKeys: new[] { "labels-only" }));
                EvaluationResult<StandardChangeResult?> nothing = StandardChangeOperation.Evaluate(shipped.Snapshot, OperationTestData.Context(shipped), none);
                Assert.Equal("does-not-qualify", nothing.Outcome);
                bool shippedModel = Active(shipped.Snapshot, "standard-change-model").Any(m => Text(m, "changeTypeKey") == type.Key);
                string expected = Flag(type, "allowsAuthorizationAfter") ? "standard-model.type" : shippedModel ? "standard-model.not-approved" : "standard-model.no-candidate";
                Assert.Equal(expected, Assert.Single(nothing.Reasons).Code);
            }

            SnapshotRow? standard = Active(shipped.Snapshot, "change-type").FirstOrDefault(t => t.Key == "standard");
            if (standard == null || !Flag(standard, "requiresStandardModel"))
            {
                continue;
            }

            CompiledSnapshot compiled = OperationTestData.Snapshot(selection.Key, ModelOperations, selection.Value);
            EvaluationContext context = OperationTestData.Context(compiled);
            foreach (int tierRank in new[] { 1, 2, 3 })
            {
                JsonTreeNode check = OperationTestData.Json("{\"changeTypeKey\":\"standard\",\"change\":{\"tierRank\":" + tierRank.ToString(CultureInfo.InvariantCulture)
                    + ",\"statedConditionKeys\":[\"labels-only\"]},\"models\":[{\"modelKey\":\"labels-only\",\"approvedByDecisionStatus\":\"accepted\"}]}");
                EvaluationResult<JsonTreeNode?> op12 = OperationJson.Evaluate(EngineOperation.EvaluateStandardChange, compiled.Snapshot, context, check);
                Assert.Equal(tierRank <= 2 ? "qualifies" : "does-not-qualify", op12.Outcome);

                string tier = "tier-" + tierRank.ToString(CultureInfo.InvariantCulture);
                JsonTreeNode scope = OperationTestData.Json("{\"decisionTypeKey\":\"change-authorization\",\"tierKey\":\"" + tier + "\",\"changeTypeKey\":\"standard\",\"targetStageKey\":\"production\","
                    + "\"portfolioPath\":[],\"standardChange\":" + OperationTestData.Canonical(op12.Payload) + "}");
                EvaluationResult<JsonTreeNode?> op02 = OperationJson.Evaluate(EngineOperation.ResolveRequiredApprovals, compiled.Snapshot, context, scope);
                if (tierRank <= 2)
                {
                    Assert.True(op02.Outcome == "engine-accept", at + " " + tier + ": " + op02.Outcome + " " + string.Join(" | ", op02.Reasons));
                    Assert.Contains("\"approvedByDecisionId\":\"test-decision-1\"", OperationTestData.Canonical(op02.Payload), StringComparison.Ordinal);
                }
                else
                {
                    Assert.Equal(OperationOutcomes.Refused, op02.Outcome);
                    Assert.Equal(new[] { "standard-model.does-not-qualify" }, OperationTestData.Codes(op02.Reasons));
                }
            }

            checkedSelections++;
        }

        Assert.True(checkedSelections > 5, "only " + checkedSelections + " selections have a standard change type");
    }

    // The typed request and the wire request give the same answer.
    [Fact]
    public void TheTypedAndTheWireStandardChangeRequestGiveTheSameResult()
    {
        CompiledSnapshot compiled = OperationTestData.Snapshot("balanced", ModelOperations);
        var typed = new StandardChangeRequest("standard", "labels-only", new StandardChangeFacts(tierRank: 3, statedConditionKeys: Array.Empty<string>()), new[] { new ModelApprovalFact("labels-only", DecisionStatusMeaning.Accepted) });
        JsonTreeNode wire = OperationTestData.Json("{\"changeTypeKey\":\"standard\",\"standardChangeModelKey\":\"labels-only\",\"change\":{\"tierRank\":3,\"statedConditionKeys\":[]},"
            + "\"models\":[{\"modelKey\":\"labels-only\",\"approvedByDecisionStatus\":\"accepted\"}]}");

        EvaluationResult<StandardChangeResult?> a = StandardChangeOperation.Evaluate(compiled.Snapshot, OperationTestData.Context(compiled), typed);
        EvaluationResult<JsonTreeNode?> b = OperationJson.Evaluate(EngineOperation.EvaluateStandardChange, compiled.Snapshot, OperationTestData.Context(compiled), wire);

        Assert.Equal(OperationTestData.Canonical(a.Payload!.ToJson()), OperationTestData.Canonical(b.Payload));
        Assert.Equal(a.Reasons, b.Reasons);
        Assert.Equal(2, a.Payload.FailedConditions.Count);
    }

    // A model the engine cannot read is a broken configuration: it refuses and reports, and never skips the condition
    // (document 25, rule 10). The fixture schema rightly refuses these models as inline operations.
    [Theory]
    [InlineData("{\"conditionKind\":\"max-file-size\",\"value\":3}", "standard-model.condition-kind")]
    [InlineData("{\"conditionKind\":\"max-tier-rank\",\"value\":\"two\"}", "json.schema")]
    [InlineData("{\"conditionKind\":\"max-tier-rank\",\"value\":-1}", "json.schema")]
    [InlineData("{\"conditionKind\":\"target-stages-in\",\"value\":[]}", "json.schema")]
    [InlineData("{\"conditionKind\":\"stated\",\"label\":\"No key\"}", "json.schema")]
    public void AnUnreadableConditionIsReportedNotSkipped(string condition, string code)
    {
        CompiledSnapshot compiled = OperationTestData.Snapshot("balanced", Model("m", condition));
        var request = new StandardChangeRequest("standard", null, new StandardChangeFacts(tierRank: 1), new[] { new ModelApprovalFact("m", DecisionStatusMeaning.Accepted) });

        EvaluationResult<StandardChangeResult?> result = StandardChangeOperation.Evaluate(compiled.Snapshot, OperationTestData.Context(compiled), request);

        Assert.Equal(OperationOutcomes.Unavailable, result.Outcome);
        Reason reason = Assert.Single(result.Reasons);
        Assert.Equal(code, reason.Code);
        Assert.Equal("m", reason.Ref);
    }

    [Fact]
    public void AModelWithNoConditionsOrAMissingTierIsReportedNotGuessed()
    {
        CompiledSnapshot empty = OperationTestData.Snapshot("balanced", "[{\"op\":\"upsert\",\"entity\":\"standard-change-model\",\"key\":\"m\",\"values\":{\"label\":\"M\",\"changeTypeKey\":\"standard\",\"conditions\":[],\"approvedByDecisionId\":\"d\"}}]");
        var request = new StandardChangeRequest("standard", null, new StandardChangeFacts(tierRank: 1), new[] { new ModelApprovalFact("m", DecisionStatusMeaning.Accepted) });
        EvaluationResult<StandardChangeResult?> a = StandardChangeOperation.Evaluate(empty.Snapshot, OperationTestData.Context(empty), request);
        Assert.Equal(OperationOutcomes.Unavailable, a.Outcome);
        Assert.Equal(new[] { "json.schema" }, OperationTestData.Codes(a.Reasons));

        CompiledSnapshot tier = OperationTestData.Snapshot("balanced", "[{\"op\":\"upsert\",\"entity\":\"standard-change-model\",\"key\":\"m\",\"values\":{\"label\":\"M\",\"changeTypeKey\":\"standard\",\"tierKey\":\"tier-9\","
            + "\"conditions\":[{\"conditionKind\":\"max-tier-rank\",\"value\":3}],\"approvedByDecisionId\":\"d\"}}]");
        EvaluationResult<StandardChangeResult?> b = StandardChangeOperation.Evaluate(tier.Snapshot, OperationTestData.Context(tier), request);
        Assert.Equal(OperationOutcomes.Unavailable, b.Outcome);
        Assert.Equal(new[] { "ref.unresolved" }, OperationTestData.Codes(b.Reasons));
    }

    // ---- OP-13 ----

    // The claim lifecycle, run against a small store over many generated runs. Each page's plan is applied the way the
    // runtime would apply it through OP-08. Afterwards: one claim per key; Times Seen equals the number of runs that saw
    // the item since its claim was created; a dismissed or claimed claim keeps its status; no registered item ever has
    // a claim; vanished claims only for components missing on the final page of a run; and a replayed page plan
    // changes nothing (absolute values only).
    [Fact]
    public void TheClaimLifecycleHoldsOverManyGeneratedRuns()
    {
        CompiledSnapshot compiled = OperationTestData.Snapshot("balanced");
        EvaluationContext context = OperationTestData.Context(compiled);
        uint seed = 20261122;
        for (int scenario = 0; scenario < 40; scenario++)
        {
            var store = new Dictionary<string, StoredClaim>(StringComparer.Ordinal);
            var registered = new[] { 1, 2, 3, 4 }.Select(n => new RegisteredComponentFact("component-" + n.ToString(CultureInfo.InvariantCulture), Id(n), "canvas-app", Estate)).ToList();
            var runsSeeing = new Dictionary<string, int>(StringComparer.Ordinal);
            var missingRuns = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int run = 1; run <= 5; run++)
            {
                string runId = "run-" + run.ToString(CultureInfo.InvariantCulture);
                var items = new List<InventoryItem>();
                for (int n = 1; n <= 12; n++)
                {
                    if (Next(ref seed) % 3 != 0)
                    {
                        items.Add(new InventoryItem(Id(n), Canvas, "canvas-app", Estate, "Item " + n.ToString(CultureInfo.InvariantCulture)));
                    }
                }

                // A person dismisses or claims some open claims between runs. The engine must never change that.
                foreach (StoredClaim claim in store.Values.Where(c => c.Status == ClaimStatusMeaning.Open))
                {
                    uint roll = Next(ref seed) % 5;
                    claim.Status = roll == 0 ? ClaimStatusMeaning.Dismissed : roll == 1 ? ClaimStatusMeaning.Claimed : ClaimStatusMeaning.Open;
                }

                Dictionary<string, ClaimStatusMeaning> before = store.ToDictionary(p => p.Key, p => p.Value.Status, StringComparer.Ordinal);
                List<List<InventoryItem>> pages = Pages(items, 1 + (int)(Next(ref seed) % 3));
                var seenThisRun = new List<string>();
                for (int page = 0; page < pages.Count; page++)
                {
                    bool final = page == pages.Count - 1;
                    var request = new GapCompareRequest(
                        runId,
                        new[] { Estate },
                        null,
                        final,
                        pages[page],
                        registered,
                        store.Values.Select(c => new ExistingClaimFact(c.Id, c.Key, c.Status, c.Kind, c.TimesSeen, c.LastSeenRunId)),
                        seenThisRun);
                    EvaluationResult<GapComparePlan?> result = GapCompareOperation.Evaluate(compiled.Snapshot, context, request);
                    Assert.Equal("compared", result.Outcome);
                    GapComparePlan plan = result.Payload!;
                    if (!final)
                    {
                        Assert.Empty(plan.VanishedClaims);
                    }

                    Apply(store, plan, replay: false);
                    string once = Snapshot(store);
                    Apply(store, plan, replay: true);
                    Assert.Equal(once, Snapshot(store));
                    seenThisRun.AddRange(plan.ComponentLastSeen);
                }

                foreach (int n in items.Select(i => int.Parse(i.ExternalId.Substring(i.ExternalId.Length - 4), CultureInfo.InvariantCulture)).Distinct())
                {
                    if (n > 4)
                    {
                        string key = GapCompareOperation.ClaimKey(FindingKind.Unregistered, Canvas, Id(n));
                        runsSeeing[key] = runsSeeing.TryGetValue(key, out int c) ? c + 1 : 1;
                    }
                }

                foreach (RegisteredComponentFact component in registered.Where(r => !items.Any(i => i.ExternalId == r.ExternalId)))
                {
                    string key = GapCompareOperation.ClaimKey(FindingKind.Vanished, "canvas-app", component.ExternalId!);
                    missingRuns[key] = missingRuns.TryGetValue(key, out int c) ? c + 1 : 1;
                }

                foreach (KeyValuePair<string, ClaimStatusMeaning> old in before)
                {
                    Assert.Equal(old.Value, store[old.Key].Status);
                }
            }

            Assert.Equal(runsSeeing.Count + missingRuns.Count, store.Count);
            foreach (KeyValuePair<string, int> pair in runsSeeing.Concat(missingRuns))
            {
                Assert.Equal(pair.Value, store[pair.Key].TimesSeen);
            }

            Assert.DoesNotContain(store.Values, c => c.Kind == FindingKind.Unregistered && registered.Any(r => c.Key.EndsWith(r.ExternalId!, StringComparison.Ordinal)));
        }
    }

    // The engine keeps no copy of the inventory: an item that carries any property a claim does not store is refused.
    [Theory]
    [InlineData("createdOn", "\"2026-01-01T00:00:00Z\"")]
    [InlineData("connectors", "[\"shared_sql\"]")]
    [InlineData("ownerDisplayName", "\"Someone\"")]
    public void AnItemWithAnInventoryPropertyIsRefused(string property, string value)
    {
        CompiledSnapshot compiled = OperationTestData.Snapshot("balanced");
        JsonTreeNode input = OperationTestData.Json("{\"gapCheckRunId\":\"r\",\"coveredEstateKeys\":[\"" + Estate + "\"],\"items\":[{\"externalId\":\"" + Id(9) + "\",\"sourceType\":\"" + Canvas
            + "\",\"componentKindKey\":\"canvas-app\",\"estateKey\":\"" + Estate + "\",\"labelAsSeen\":\"X\",\"" + property + "\":" + value + "}]}");

        EvaluationResult<JsonTreeNode?> result = OperationJson.Evaluate(EngineOperation.CompareGapCheckResult, compiled.Snapshot, OperationTestData.Context(compiled), input);

        Assert.Equal(OperationOutcomes.InvalidInput, result.Outcome);
        Assert.Equal(new[] { "input.malformed" }, OperationTestData.Codes(result.Reasons));
        Assert.Contains(property, result.Reasons[0].Message, StringComparison.Ordinal);
    }

    // Two kinds sharing one Source Type Hint: the engine never picks one silently, so the item is ignored and the reason
    // names both kinds.
    [Fact]
    public void ASourceTypeHintSharedByTwoKindsIsNeverGuessed()
    {
        CompiledSnapshot compiled = OperationTestData.Snapshot("balanced", "[{\"op\":\"upsert\",\"entity\":\"component-kind\",\"key\":\"canvas-app\",\"values\":{\"sourceTypeHint\":\"" + Canvas + "\"}},"
            + "{\"op\":\"upsert\",\"entity\":\"component-kind\",\"key\":\"code-app\",\"values\":{\"sourceTypeHint\":\"" + Canvas + "\"}}]");
        var request = new GapCompareRequest(Run1, new[] { Estate }, null, false, new[] { new InventoryItem(Id(9), Canvas, null, Estate, "X") });

        EvaluationResult<GapComparePlan?> result = GapCompareOperation.Evaluate(compiled.Snapshot, OperationTestData.Context(compiled), request);

        Assert.Equal(1, result.Payload!.Ignored);
        Assert.Empty(result.Payload.NewClaims);
        Reason ignored = Assert.Single(result.Reasons, r => r.Code == "gap-check.item-ignored");
        Assert.Contains("canvas-app,code-app", ignored.Message, StringComparison.Ordinal);
    }

    // The same page twice gives the same plan, byte for byte.
    [Fact]
    public void TheSamePageGivesTheSamePlan()
    {
        CompiledSnapshot compiled = OperationTestData.Snapshot("balanced");
        var request = new GapCompareRequest(
            Run1,
            new[] { Estate },
            null,
            true,
            new[] { new InventoryItem(Id(7), Canvas, "canvas-app", Estate, "Seven"), new InventoryItem(Id(5), Canvas, "canvas-app", Estate, "Five") },
            new[] { new RegisteredComponentFact("component-2", Id(2), "canvas-app", Estate), new RegisteredComponentFact("component-1", Id(1), "canvas-app", Estate) });

        EvaluationResult<GapComparePlan?> a = GapCompareOperation.Evaluate(compiled.Snapshot, OperationTestData.Context(compiled), request);
        EvaluationResult<GapComparePlan?> b = GapCompareOperation.Evaluate(compiled.Snapshot, OperationTestData.Context(compiled), request);

        Assert.Equal(OperationTestData.Canonical(a.Payload!.ToJson()), OperationTestData.Canonical(b.Payload!.ToJson()));
        Assert.Equal(new[] { GapCompareOperation.ClaimKey(FindingKind.Unregistered, Canvas, Id(5)), GapCompareOperation.ClaimKey(FindingKind.Unregistered, Canvas, Id(7)) }, a.Payload.NewClaims.Select(c => c.ClaimKey).ToArray());
        Assert.Equal(new[] { "component-1", "component-2" }, a.Payload.VanishedClaims.Select(c => c.TechnicalComponentId).ToArray());
    }

    // ---- OP-14 ----

    // Every set of up to three facts drawn from a pool of fourteen kinds of fact, in every order, with and without an
    // external change number: the answer equals a separate computation of rules 2 to 6; a recorded number alone never
    // authorizes; an AI-recorded fact never changes the answer; and the order of the facts never matters.
    [Fact]
    public void TheLinkedAuthorizationFollowsTheRulesForEveryFactCombination()
    {
        CompiledSnapshot compiled = OperationTestData.Snapshot("balanced", null, "linked-change-management");
        EvaluationContext context = OperationTestData.Context(compiled);
        List<Func<string, DateTime, AuthorizationFact>> pool = FactPool();
        DateTime[] times = { Utc("2026-11-21T10:00:00Z"), Utc("2026-11-21T11:00:00Z"), Utc("2026-11-21T11:00:00Z") };
        int evaluations = 0;
        foreach (int[] picks in Picks(pool.Count, 3))
        {
            AuthorizationFact[] facts = picks.Select((p, i) => pool[p]("f" + i.ToString(CultureInfo.InvariantCulture), times[i])).ToArray();
            foreach (bool number in new[] { false, true })
            {
                string? first = null;
                foreach (AuthorizationFact[] order in Permutations(facts))
                {
                    var request = new LinkedAuthorizationRequest(ChangeManagementMode.Linked, number, order, new[] { "change-manager" });
                    EvaluationResult<LinkedAuthorizationResult?> result = LinkedAuthorizationOperation.Evaluate(compiled.Snapshot, context, request);
                    LinkedAuthorizationResult payload = result.Payload!;

                    bool expected = ExpectedAuthorized(facts);
                    Assert.Equal(expected, payload.Authorized);
                    Assert.Equal(expected ? "authorized" : "not-authorized", result.Outcome);
                    Assert.Equal(expected, payload.Basis != AuthorizationBasis.None);
                    Assert.Equal(number && !facts.Any(Counts), result.Reasons.Any(r => r.Code == "authorization.number-only"));

                    string canonical = OperationTestData.Canonical(payload.ToJson()) + string.Join("|", result.Reasons.Select(r => r.Code + r.Ref));
                    first ??= canonical;
                    Assert.Equal(first, canonical);
                    evaluations++;
                }

                // Adding an AI-recorded fact, whatever it says and however late, never changes the answer.
                foreach (AuthorizationState state in new[] { AuthorizationState.Authorized, AuthorizationState.Cancelled })
                {
                    var ai = new AuthorizationFact("ai", AuthorizationFactSourceKind.Attestation, state, true, Utc("2026-11-22T08:00:00Z"), ActorKindMeaning.AiClientForPerson, PrincipalKind.User, true, true);
                    var request = new LinkedAuthorizationRequest(ChangeManagementMode.Linked, number, facts.Concat(new[] { ai }), new[] { "change-manager" });
                    Assert.Equal(ExpectedAuthorized(facts), LinkedAuthorizationOperation.Evaluate(compiled.Snapshot, context, request).Payload!.Authorized);

                    // A pipeline fact dated after the evaluation moment never authorizes, so an
                    // authorized one changes nothing and is listed; a cancellation dated later still removes the
                    // authorization, because the engine never guesses in favor.
                    var later = new AuthorizationFact("later", AuthorizationFactSourceKind.ItsmStep, state, true, context.AsOfUtc.AddSeconds(1), ActorKindMeaning.Pipeline, PrincipalKind.ServicePrincipal);
                    var withLater = new LinkedAuthorizationRequest(ChangeManagementMode.Linked, number, facts.Concat(new[] { later }), new[] { "change-manager" });
                    EvaluationResult<LinkedAuthorizationResult?> laterResult = LinkedAuthorizationOperation.Evaluate(compiled.Snapshot, context, withLater);
                    if (state == AuthorizationState.Authorized)
                    {
                        Assert.Equal(ExpectedAuthorized(facts), laterResult.Payload!.Authorized);
                        Assert.Contains(laterResult.Payload.IgnoredFacts, f => f.FactId == "later" && f.Code == "authorization.reported-later");
                    }
                    else
                    {
                        Assert.False(laterResult.Payload!.Authorized);
                        Assert.DoesNotContain(laterResult.Payload.IgnoredFacts, f => f.FactId == "later");
                    }
                }
            }
        }

        Assert.True(evaluations > 5000, "only " + evaluations + " evaluations");
    }

    // Over every shipped selection with the linked overlay, every tier and change type: the OP-14 result, passed on
    // unchanged through the wire form, decides OP-04's missing-approval flag. A change with only an external change
    // number is missing its approval wherever the gate looks (document 25, section 10: a recorded external change
    // number alone never authorizes).
    [Fact]
    public void TheLinkedAuthorizationResultFeedsTheGateOnEveryLinkedSelection()
    {
        int checkedCombinations = 0;
        foreach (KeyValuePair<string, string[]> selection in OperationTestData.ShippedSelections().Where(s => s.Value.Contains("linked-change-management")))
        {
            CompiledSnapshot compiled = OperationTestData.Snapshot(selection.Key, null, selection.Value);
            EvaluationContext context = OperationTestData.Context(compiled);
            JsonTreeNode numberOnly = OperationJson.Evaluate(EngineOperation.EvaluateLinkedAuthorization, compiled.Snapshot, context,
                OperationTestData.Json("{\"changeManagementMode\":\"linked\",\"hasExternalChangeNumber\":true,\"attesterRoleKeys\":[\"change-manager\"]}")).Payload!;
            JsonTreeNode authorized = OperationJson.Evaluate(EngineOperation.EvaluateLinkedAuthorization, compiled.Snapshot, context,
                OperationTestData.Json("{\"changeManagementMode\":\"linked\",\"hasExternalChangeNumber\":true,\"facts\":[{\"factId\":\"f1\",\"sourceKind\":\"itsm-step\",\"state\":\"authorized\",\"hasEvidenceUrl\":true,"
                + "\"reportedOnUtc\":\"2026-11-20T10:00:00Z\",\"actorKind\":\"pipeline\",\"principalKind\":\"service-principal\"}]}")).Payload!;
            foreach (string tier in Active(compiled.Snapshot, "tier").Select(r => r.Key))
            {
                foreach (SnapshotRow type in Active(compiled.Snapshot, "change-type").Where(t => Flag(t, "requiresAuthorization")))
                {
                    foreach (JsonTreeNode fact in new[] { numberOnly, authorized })
                    {
                        JsonTreeNode gate = OperationTestData.Json("{\"mode\":\"record\",\"tierKey\":\"" + tier + "\",\"changeTypeKey\":\"" + type.Key + "\",\"targetStageKey\":\"production\",\"portfolioPath\":[],"
                            + "\"routeKey\":\"pipeline-step\",\"eventUtc\":\"2026-11-20T15:00:00Z\",\"allowedRouteKeys\":[\"pipeline-step\"],\"change\":{\"changeManagementMode\":\"linked\",\"hasExternalChangeNumber\":true,"
                            + "\"linkedAuthorization\":" + OperationTestData.Canonical(fact) + ",\"windowStartUtc\":\"2026-11-20T14:00:00Z\",\"windowEndUtc\":\"2026-11-20T18:00:00Z\"}}");
                        EvaluationResult<JsonTreeNode?> result = OperationJson.Evaluate(EngineOperation.EvaluateGateDecision, compiled.Snapshot, context, gate);
                        string at = selection.Key + "+" + string.Join("+", selection.Value) + " " + tier + " " + type.Key;
                        Assert.True(result.Payload != null, at + ": " + string.Join(" | ", result.Reasons));

                        bool off = Member(result.Payload!, "gateLevel").GetString() == "off";
                        bool missing = Member(result.Payload!, "isMissingApproval").GetBoolean();
                        Assert.True(missing == (!off && ReferenceEquals(fact, numberOnly)), at + " missing " + missing);
                        checkedCombinations++;
                    }
                }
            }
        }

        Assert.True(checkedCombinations > 20, "only " + checkedCombinations + " combinations");
    }

    // ---- Helpers ----

    private static bool Counts(AuthorizationFact fact)
    {
        bool ai = fact.ActorKind == ActorKindMeaning.AiClientForPerson || fact.ActorKind == ActorKindMeaning.AgentIdentity
            || fact.PrincipalKind == PrincipalKind.AgentIdentity || fact.PrincipalKind == PrincipalKind.AgentUser;
        if (ai || !fact.HasEvidenceUrl)
        {
            return false;
        }

        return fact.SourceKind == AuthorizationFactSourceKind.ItsmStep
            ? fact.ActorKind == ActorKindMeaning.Pipeline && (fact.PrincipalKind == PrincipalKind.ServicePrincipal || fact.PrincipalKind == PrincipalKind.ManagedIdentity)
            : fact.ActorKind == ActorKindMeaning.Person && fact.PrincipalKind == PrincipalKind.User && fact.AnsweredOnConfirmationCard && fact.AttesterHoldsRole;
    }

    // Rules 5 and 6, worked out on their own: the latest counting fact decides, and at the same moment a removal wins.
    private static bool ExpectedAuthorized(IEnumerable<AuthorizationFact> facts)
    {
        AuthorizationFact[] counting = facts.Where(Counts).ToArray();
        if (counting.Length == 0)
        {
            return false;
        }

        DateTime latest = counting.Max(f => f.ReportedOnUtc);
        return counting.Where(f => f.ReportedOnUtc == latest).All(f => f.State == AuthorizationState.Authorized);
    }

    private static List<Func<string, DateTime, AuthorizationFact>> FactPool()
    {
        var pool = new List<Func<string, DateTime, AuthorizationFact>>();
        foreach (AuthorizationState state in new[] { AuthorizationState.Authorized, AuthorizationState.Cancelled, AuthorizationState.NotAuthorized })
        {
            pool.Add((id, at) => new AuthorizationFact(id, AuthorizationFactSourceKind.ItsmStep, state, true, at, ActorKindMeaning.Pipeline, PrincipalKind.ServicePrincipal));
            pool.Add((id, at) => new AuthorizationFact(id, AuthorizationFactSourceKind.Attestation, state, true, at, ActorKindMeaning.Person, PrincipalKind.User, true, true));
        }

        pool.Add((id, at) => new AuthorizationFact(id, AuthorizationFactSourceKind.ItsmStep, AuthorizationState.Authorized, false, at, ActorKindMeaning.Pipeline, PrincipalKind.ServicePrincipal));
        pool.Add((id, at) => new AuthorizationFact(id, AuthorizationFactSourceKind.ItsmStep, AuthorizationState.Authorized, true, at, ActorKindMeaning.AiClientForPerson, PrincipalKind.User));
        pool.Add((id, at) => new AuthorizationFact(id, AuthorizationFactSourceKind.ItsmStep, AuthorizationState.Authorized, true, at, ActorKindMeaning.Pipeline, PrincipalKind.AgentIdentity));
        pool.Add((id, at) => new AuthorizationFact(id, AuthorizationFactSourceKind.ItsmStep, AuthorizationState.Authorized, true, at, ActorKindMeaning.Person, PrincipalKind.User));
        pool.Add((id, at) => new AuthorizationFact(id, AuthorizationFactSourceKind.Attestation, AuthorizationState.Authorized, true, at, ActorKindMeaning.Person, PrincipalKind.User, false, true));
        pool.Add((id, at) => new AuthorizationFact(id, AuthorizationFactSourceKind.Attestation, AuthorizationState.Authorized, true, at, ActorKindMeaning.Person, PrincipalKind.User, true, false));
        pool.Add((id, at) => new AuthorizationFact(id, AuthorizationFactSourceKind.Attestation, AuthorizationState.Authorized, true, at, ActorKindMeaning.AiClientForPerson, PrincipalKind.User, true, true));
        pool.Add((id, at) => new AuthorizationFact(id, AuthorizationFactSourceKind.Attestation, AuthorizationState.Authorized, true, at, ActorKindMeaning.Person, PrincipalKind.AgentUser, true, true));
        return pool;
    }

    // Every list of 0 to max indexes below count, with repeats allowed, in non-decreasing order.
    private static IEnumerable<int[]> Picks(int count, int max)
    {
        yield return Array.Empty<int>();
        for (int a = 0; a < count; a++)
        {
            yield return new[] { a };
            for (int b = a; b < count; b++)
            {
                yield return new[] { a, b };
                if (max >= 3)
                {
                    for (int c = b; c < count; c++)
                    {
                        yield return new[] { a, b, c };
                    }
                }
            }
        }
    }

    private static IEnumerable<T[]> Permutations<T>(T[] items)
    {
        if (items.Length <= 1)
        {
            yield return items;
            yield break;
        }

        for (int i = 0; i < items.Length; i++)
        {
            T[] rest = items.Where((_, j) => j != i).ToArray();
            foreach (T[] tail in Permutations(rest))
            {
                yield return new[] { items[i] }.Concat(tail).ToArray();
            }
        }
    }

    private static string Model(string key, string condition)
    {
        return "[" + Upsert(key, condition) + "]";
    }

    private static string Upsert(string key, string condition)
    {
        return "{\"op\":\"upsert\",\"entity\":\"standard-change-model\",\"key\":\"" + key + "\",\"values\":{\"label\":\"Model " + key + "\",\"changeTypeKey\":\"standard\",\"conditions\":[" + condition
            + "],\"approvedByDecisionId\":\"test-decision-1\"}}";
    }

    private static string[] Subset(string[] keys, int mask)
    {
        return keys.Where((_, i) => (mask & (1 << i)) != 0).ToArray();
    }

    private static string JsonList(IEnumerable<string> keys)
    {
        return "[" + string.Join(",", keys.Select(k => "\"" + k + "\"")) + "]";
    }

    private static string Id(int n)
    {
        return "00000000-0000-0000-0000-00000000" + n.ToString("0000", CultureInfo.InvariantCulture);
    }

    private static List<List<InventoryItem>> Pages(List<InventoryItem> items, int count)
    {
        var pages = Enumerable.Range(0, count).Select(_ => new List<InventoryItem>()).ToList();
        for (int i = 0; i < items.Count; i++)
        {
            pages[i % count].Add(items[i]);
        }

        // An item seen on two pages of one run, as overlapping inventory pages can give.
        if (count > 1 && items.Count > 0)
        {
            pages[count - 1].Add(items[0]);
        }

        return pages;
    }

    // Applies a plan the way the runtime would. Claim Key is a unique key (O23), so a replayed creation lands on the row
    // the first one made, as an upsert with the same values. A first application must never plan a creation for a key
    // the engine was told exists.
    private static void Apply(Dictionary<string, StoredClaim> store, GapComparePlan plan, bool replay)
    {
        foreach (PlannedClaim claim in plan.NewClaims.Concat(plan.VanishedClaims.Where(c => c.ClaimId == null)))
        {
            if (store.TryGetValue(claim.ClaimKey, out StoredClaim? existing))
            {
                Assert.True(replay, "a second claim for " + claim.ClaimKey);
                existing.TimesSeen = claim.TimesSeen;
                existing.LastSeenRunId = claim.LastSeenRunId;
                continue;
            }

            store[claim.ClaimKey] = new StoredClaim("claim-" + store.Count.ToString(CultureInfo.InvariantCulture), claim.ClaimKey, claim.FindingKind)
            {
                TimesSeen = claim.TimesSeen,
                LastSeenRunId = claim.LastSeenRunId,
            };
        }

        foreach (PlannedClaim claim in plan.UpdatedClaims.Concat(plan.VanishedClaims.Where(c => c.ClaimId != null)))
        {
            StoredClaim stored = store[claim.ClaimKey];
            Assert.Equal(stored.Id, claim.ClaimId);
            stored.TimesSeen = claim.TimesSeen;
            stored.LastSeenRunId = claim.LastSeenRunId;
        }
    }

    private static string Snapshot(Dictionary<string, StoredClaim> store)
    {
        return string.Join(";", store.Values.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => c.Key + "=" + c.TimesSeen.ToString(CultureInfo.InvariantCulture) + "/" + c.LastSeenRunId + "/" + c.Status));
    }

    private static uint Next(ref uint seed)
    {
        seed = unchecked(seed * 1664525u + 1013904223u);
        return seed >> 8;
    }

    private static DateTime Utc(string text)
    {
        return DateTime.SpecifyKind(DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal), DateTimeKind.Utc);
    }

    private static IEnumerable<SnapshotRow> Active(ConfigurationSnapshot snapshot, string entity)
    {
        return snapshot.TryGetEntity(entity, out SnapshotEntity? e) ? e.ActiveRows : Enumerable.Empty<SnapshotRow>();
    }

    private static bool Flag(SnapshotRow row, string column)
    {
        return row.TryGetColumn(column, out JsonTreeNode? value) && value.Kind == JsonTreeKind.True;
    }

    private static string? Text(SnapshotRow row, string column)
    {
        return row.TryGetColumn(column, out JsonTreeNode? value) && value.Kind == JsonTreeKind.String ? value.GetString() : null;
    }

    private static JsonTreeNode Member(JsonTreeNode node, string name)
    {
        Assert.True(node.TryGetMember(name, out JsonTreeNode? value), "no member " + name);
        return value!;
    }

    private sealed class StoredClaim
    {
        public StoredClaim(string id, string key, FindingKind kind)
        {
            Id = id;
            Key = key;
            Kind = kind;
        }

        public string Id { get; }

        public string Key { get; }

        public FindingKind Kind { get; }

        public ClaimStatusMeaning Status { get; set; } = ClaimStatusMeaning.Open;

        public int TimesSeen { get; set; }

        public string? LastSeenRunId { get; set; }
    }
}
