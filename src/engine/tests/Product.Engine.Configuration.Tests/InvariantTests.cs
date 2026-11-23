using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Product.Engine.Configuration.Activation;
using Product.Engine.Configuration.Composition;
using Product.Engine.Configuration.Json;
using Product.Engine.Configuration.Patches;
using Product.Engine.Configuration.Presets;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Configuration.Validation;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Operations;
using Product.Engine.Core.Snapshots;
using Product.Engine.Testing;
using Product.Engine.Testing.Invariants;
using Xunit;

namespace Product.Engine.Configuration.Tests;

// The invariants of document 25, section 10 (DesignInvariants, INV-01 to INV-11), each a generated test over every
// shipped selection: every full preset alone and with each allowed overlay combination (DesignInvariants.Selections, 22
// today). Where an earlier slice already generated an invariant over every selection, that test carries the [Invariant]
// mark too (OperationTests, WriteGuardTests); EveryDesignInvariantHasAGeneratedTest proves that no invariant is left out.
//
// Every value here is fictional. The tests read the shipped presets in src/ and nothing else.
public sealed class InvariantTests
{
    private static readonly DateTime AsOf = OperationTestData.AsOf;
    private static readonly DateTime Answered = AsOf.AddHours(-1);
    private static readonly DateTime Expires = AsOf.AddDays(5);
    private const string Proposer = "fixture-proposer";

    // ---- The catalog ----

    // Every invariant of document 25, section 10 has at least one generated test, and every mark names a known invariant.
    [Fact]
    public void EveryDesignInvariantHasAGeneratedTest()
    {
        var marks = typeof(InvariantTests).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .SelectMany(m => m.GetCustomAttributes<InvariantAttribute>().Select(a => new { a.Id, Test = m.DeclaringType!.Name + "." + m.Name, IsTest = m.GetCustomAttributes<FactAttribute>().Any() }))
            .ToList();

        Assert.Equal(11, DesignInvariants.All.Count);
        Assert.Equal(DesignInvariants.All.Select(i => i.Id), Enumerable.Range(1, 11).Select(n => "INV-" + n.ToString("00", CultureInfo.InvariantCulture)));
        Assert.All(marks, m => Assert.True(m.IsTest, m.Test + " is marked but is not a test"));
        Assert.All(marks, m => DesignInvariants.Get(m.Id));
        foreach (DesignInvariant invariant in DesignInvariants.All)
        {
            Assert.True(marks.Any(m => m.Id == invariant.Id), invariant + ": no generated test");
            Assert.True(marks.Any(m => m.Id == invariant.Id && m.Test.StartsWith(nameof(InvariantTests) + ".", StringComparison.Ordinal) || m.Id == invariant.Id && IsOverEverySelection(m.Test)), invariant + ": no test over every shipped selection");
        }
    }

    // The generator is the set build-presets validates: 4 full presets and 18 overlay combinations.
    [Fact]
    public void TheGeneratorGivesEveryShippedSelection()
    {
        PresetSelection[] selections = Selections().ToArray();

        Assert.Equal(22, selections.Length);
        Assert.Equal(4, selections.Count(s => s.Overlays.Length == 0));
        Assert.Equal(OperationTestData.ShippedSelections().Select(s => s.Key + "|" + string.Join("+", s.Value)), selections.Select(s => s.PresetKey + "|" + string.Join("+", s.Overlays)));
    }

    // ---- INV-01 an AI or workload actor never counts toward any approval ----

    // Over every shipped selection and every approval scope OP-02 answers with cards: an approved answer from every actor
    // kind and principal kind but a person signed in as a user, holding the role, with a typed reason, in time, never
    // counts and never satisfies a requirement; people satisfy the same requirements (the control), and a requirement
    // that only AI or workload actors answered keeps the decision pending.
    [Fact]
    [Invariant("INV-01")]
    public void AnAiOrWorkloadActorNeverCountsInEveryShippedSelection()
    {
        int scopes = 0;
        int records = 0;
        foreach (PresetSelection selection in Selections())
        {
            CompiledSnapshot compiled = Snapshot(selection);
            foreach (ApprovalScope scope in Scopes(compiled))
            {
                List<RequiredApproval> counting = scope.Requirements;
                string at = selection + " " + scope;

                List<ApprovalFact> machines = counting.SelectMany(r => MachineAnswers(r.RequirementKey)).ToList();
                EvaluationResult<ApprovalStatusResult?> alone = Status(compiled, scope, machines);
                Assert.True(alone.Payload != null, at + ": " + string.Join(" | ", alone.Reasons));
                Assert.NotEqual("satisfied", alone.Outcome);
                Assert.All(machines, m => Assert.False(alone.Payload!.CountsByRecord[m.ApprovalRecordId], at + " counted " + m.ApprovalRecordId));
                Assert.All(alone.Payload!.Requirements, s => Assert.Empty(s.CountingRecordIds));
                records += machines.Count;

                List<ApprovalFact> people = counting.SelectMany(r => PeopleAnswers(r)).ToList();
                EvaluationResult<ApprovalStatusResult?> control = Status(compiled, scope, people.Concat(machines));
                Assert.True(control.Outcome == "satisfied", at + " control: " + control.Outcome + " " + string.Join(" | ", control.Reasons));
                Assert.All(machines, m => Assert.False(control.Payload!.CountsByRecord[m.ApprovalRecordId], at + " counted " + m.ApprovalRecordId + " next to people"));

                foreach (RequiredApproval onlyMachines in counting)
                {
                    IEnumerable<ApprovalFact> mixed = people.Where(p => p.RuleKey != onlyMachines.RequirementKey).Concat(MachineAnswers(onlyMachines.RequirementKey));
                    EvaluationResult<ApprovalStatusResult?> result = Status(compiled, scope, mixed);
                    Assert.True(result.Outcome == "pending", at + " with only AI or workload answers for " + onlyMachines.RequirementKey + ": " + result.Outcome);
                    Assert.Equal(ApprovalOverall.Pending, result.Payload!.Requirements.Single(s => s.Requirement.RequirementKey == onlyMachines.RequirementKey).State);
                }

                scopes++;
            }
        }

        Assert.True(scopes > 500, "only " + scopes + " scopes");
        Assert.True(records > 10000, "only " + records + " AI or workload answers");
    }

    // ---- INV-02 no decision outside the locked list is accepted without a person's card ----

    // Over every shipped selection, every decision type and every status with the meaning accepted: no person, role, AI
    // client, agent, pipeline or approval runner sets a Decision accepted, created or updated, and no engine effect but
    // accept-decision does (a create may be allowed only as proposed: G-08 and the person's draft rule store it so). The accept-decision effect is allowed only when OP-03 said satisfied (a person's card) or
    // when OP-02 said engine-accept for an entry of the locked list; every other basis is refused.
    [Fact]
    [Invariant("INV-02")]
    public void NoDecisionIsAcceptedWithoutACardOrTheLockedListInEveryShippedSelection()
    {
        int refused = 0;
        int engineAccepted = 0;
        foreach (PresetSelection selection in Selections())
        {
            CompiledSnapshot compiled = Snapshot(selection);
            ConfigurationSnapshot snapshot = compiled.Snapshot;
            SnapshotRow[] accepted = Statuses(snapshot, "decision").Where(s => s.EngineMeaning == "accepted").ToArray();
            string proposed = Statuses(snapshot, "decision").First(s => s.EngineMeaning == "proposed").KeyPath;
            Assert.NotEmpty(accepted);
            string[] roles = Active(snapshot, "role").Select(r => r.Key).ToArray();
            IReadOnlyList<EngineAcceptanceEntry> locked = EngineAcceptance.ShippedEntries;
            foreach (SnapshotRow type in Active(snapshot, "decision-type"))
            {
                foreach (SnapshotRow status in accepted)
                {
                    string before = "{\"statusMeaning\":\"proposed\",\"status\":\"" + proposed + "\",\"decisionType\":\"" + type.Key + "\",\"createdByPrincipal\":\"fixture-person-1\"}";
                    string create = "{\"decisionType\":\"" + type.Key + "\",\"status\":\"" + status.KeyPath + "\"}";
                    string update = "{\"status\":\"" + status.KeyPath + "\"}";
                    string at = selection + " " + type.Key + " " + status.KeyPath;

                    foreach ((ActorInfo actor, EffectKind? effect) in Callers(roles))
                    {
                        Assert.False(Accepts(Guard(compiled, actor, "decision", WriteOperation.Create, null, create, effect)), at + " created accepted by " + Describe(actor, effect));
                        Assert.False(Accepts(Guard(compiled, actor, "decision", WriteOperation.Update, before, update, effect)), at + " set accepted by " + Describe(actor, effect));
                        refused += 2;
                    }

                    // A gate decision also needs its gate ready (G-14): with the gate not ready nothing is accepted.
                    string? gate = Active(snapshot, "gate").Where(g => Text(g, "gateDecisionTypeKey") == type.Key).Select(g => g.KeyPath).FirstOrDefault();
                    JsonTreeNode? ready = gate == null ? null : CanonicalJson.Parse("{\"gateKey\":\"" + gate + "\",\"ready\":true}");
                    JsonTreeNode? notReady = gate == null ? null : CanonicalJson.Parse("{\"gateKey\":\"" + gate + "\",\"ready\":false}");
                    foreach (WriteAcceptance basis in Bases())
                    {
                        if (notReady != null)
                        {
                            Assert.False(Accepts(Guard(compiled, Nested(), "decision", WriteOperation.Update, before, update, EffectKind.AcceptDecision, basis, notReady)), at + " accepted while its gate is not ready");
                        }

                        EvaluationResult<WritePlan?> result = Guard(compiled, Nested(), "decision", WriteOperation.Update, before, update, EffectKind.AcceptDecision, basis, ready);
                        bool card = basis.Basis == WriteAcceptance.ApprovalsSatisfied && basis.ApprovalStatusOutcome == "satisfied";
                        bool listed = basis.Basis == WriteAcceptance.EngineAccept && basis.RequiredApprovalsOutcome == "engine-accept"
                            && locked.Any(e => e.DecisionTypeKey == type.Key && basis.Condition.HasValue && e.Condition == basis.Condition.Value);
                        if (Accepts(result))
                        {
                            Assert.True(card || listed, at + " accepted on " + basis.Basis + " " + (basis.ApprovalStatusOutcome ?? basis.RequiredApprovalsOutcome) + " " + basis.Condition);
                            engineAccepted += listed ? 1 : 0;
                        }
                        else if (!card && !listed)
                        {
                            Assert.Contains(result.Reasons, r => r.Code == "decision.accept-by-engine-only");
                            refused++;
                        }
                    }
                }
            }
        }

        Assert.True(refused > 5000, "only " + refused + " refusals");
        Assert.True(engineAccepted >= 22, "only " + engineAccepted + " locked-list acceptances");
    }

    // ---- INV-03 a consultation never counts ----

    // Over every shipped selection and approval scope: the consultations OP-02 gives never count, even answered perfectly
    // by a person; and any counting requirement passed to OP-03 as a consultation (through the wire form, as an adapter
    // would send it) stops counting its answers. With no counting requirement left, OP-03 refuses the input rather than
    // satisfying a decision without a person.
    [Fact]
    [Invariant("INV-03")]
    public void AConsultationNeverCountsInEveryShippedSelection()
    {
        int consultations = 0;
        int forged = 0;
        foreach (PresetSelection selection in Selections())
        {
            CompiledSnapshot compiled = Snapshot(selection);
            foreach (ApprovalScope scope in Scopes(compiled))
            {
                string at = selection + " " + scope;
                foreach (RequiredApproval consultation in scope.Consultations)
                {
                    var answer = Person("c-" + consultation.RequirementKey, consultation.RequirementKey, "fixture-consulted-1");
                    EvaluationResult<ApprovalStatusResult?> result = Status(compiled, scope, new[] { answer });
                    Assert.False(result.Payload!.CountsByRecord[answer.ApprovalRecordId], at + " counted the consultation " + consultation.RequirementKey);
                    Assert.Contains(result.Reasons, r => r.Code == "approval.consultation-not-counted" && r.Ref == answer.ApprovalRecordId);
                    Assert.NotEqual("satisfied", result.Outcome);
                    consultations++;
                }

                foreach (RequiredApproval marked in scope.Requirements)
                {
                    string requirements = string.Join(",", scope.Requirements.Concat(scope.Consultations)
                        .Select(r => OperationTestData.Canonical(r.RequirementKey == marked.RequirementKey ? JsonNodes.With(r.ToJson(), "isConsultation", JsonTreeNode.True) : r.ToJson())));
                    List<ApprovalFact> people = scope.Requirements.SelectMany(r => PeopleAnswers(r)).ToList();
                    string wire = "{\"requiredApprovals\":[" + requirements + "],\"records\":[" + string.Join(",", people.Select(RecordJson)) + "],\"scope\":" + ScopeJson(scope)
                        + ",\"parties\":{\"proposerPrincipalId\":\"" + Proposer + "\"}}";
                    EvaluationResult<JsonTreeNode?> result = OperationJson.Evaluate(EngineOperation.EvaluateApprovalStatus, compiled.Snapshot, Context(compiled), CanonicalJson.Parse(wire));
                    if (scope.Requirements.Count == 1)
                    {
                        Assert.True(result.Outcome == "invalid-input", at + " with its only requirement as a consultation: " + result.Outcome);
                        Assert.Contains(result.Reasons, r => r.Code == "input.required");
                    }
                    else
                    {
                        JsonTreeNode counts = JsonNodes.Get(result.Payload!, "countsByRecord")!;
                        foreach (ApprovalFact answer in people.Where(p => p.RuleKey == marked.RequirementKey))
                        {
                            Assert.True(JsonNodes.Get(counts, answer.ApprovalRecordId)!.Kind == JsonTreeKind.False, at + " counted " + answer.ApprovalRecordId + " of a consultation");
                        }

                        Assert.Contains(result.Reasons, r => r.Code == "approval.consultation-not-counted");
                    }

                    forged++;
                }
            }
        }

        Assert.True(consultations > 0, "no shipped consultation was checked");
        Assert.True(forged > 1000, "only " + forged + " requirements checked as consultations");
    }

    // ---- INV-04 a separation refusal never counts ----

    // Over every shipped selection and approval scope, with one person who is the proposer, the instructing admin, a
    // deployer and the holder of every role and owner role: whenever OP-03 refuses an answer for separation, that answer
    // does not count; whenever a separation rule with enforcement refuse wins for the scope, every answer of that person
    // is refused and the decision is not satisfied. An exception-decision rule without its accepted exception never
    // counts either.
    [Fact]
    [Invariant("INV-04")]
    public void ASeparationRefusalNeverCountsInEveryShippedSelection()
    {
        const string Everyone = "fixture-person-everywhere";
        int refusedScopes = 0;
        int refusedAnswers = 0;
        foreach (PresetSelection selection in Selections())
        {
            CompiledSnapshot compiled = Snapshot(selection);
            ConfigurationSnapshot snapshot = compiled.Snapshot;
            var parties = new SeparationFacts(
                Everyone,
                Everyone,
                new[] { Everyone },
                Active(snapshot, "owner-role").ToDictionary(r => r.Key, r => (IReadOnlyList<string>)new[] { Everyone }, StringComparer.Ordinal),
                Active(snapshot, "role").ToDictionary(r => r.Key, r => (IReadOnlyList<string>)new[] { Everyone }, StringComparer.Ordinal));
            foreach (ApprovalScope scope in Scopes(compiled))
            {
                string at = selection + " " + scope;
                List<ApprovalFact> answers = scope.Requirements.Select((r, i) => Person("s-" + i.ToString(CultureInfo.InvariantCulture), r.RequirementKey, Everyone)).ToList();
                var request = new ApprovalStatusRequest(scope.Requirements.Concat(scope.Consultations), answers, scope.DecisionTypeKey, scope.TierKey, scope.TargetStageKey, parties);
                EvaluationResult<ApprovalStatusResult?> result = ApprovalStatusOperation.Evaluate(snapshot, Context(compiled), request);
                Assert.True(result.Payload != null, at + ": " + string.Join(" | ", result.Reasons));

                string[] winners = result.Trace.Where(t => t.Entity == "Separation Rule" && t.Role == RuleTraceRole.Won).Select(t => t.Key).ToArray();
                string?[] enforcements = winners.Select(k => snapshot.TryGetRow("separation-rule", k, out SnapshotRow? row) ? Text(row, "enforcement") : null).ToArray();
                foreach (ApprovalFact answer in answers)
                {
                    string[] codes = result.Reasons.Where(r => r.Ref == answer.ApprovalRecordId).Select(r => r.Code).ToArray();
                    bool counts = result.Payload!.CountsByRecord[answer.ApprovalRecordId];
                    if (codes.Contains("approval.separation-refused") || codes.Contains("approval.separation-exception-missing"))
                    {
                        Assert.False(counts, at + " counted " + answer.ApprovalRecordId + " after a separation refusal");
                    }

                    if (enforcements.Contains("refuse"))
                    {
                        Assert.Contains("approval.separation-refused", codes);
                        Assert.False(counts, at + " counted " + answer.ApprovalRecordId + " under a winning refuse rule");
                        refusedAnswers++;
                    }
                }

                if (enforcements.Contains("refuse"))
                {
                    Assert.NotEqual("satisfied", result.Outcome);
                    refusedScopes++;
                }
            }
        }

        Assert.True(refusedScopes > 200, "only " + refusedScopes + " scopes with a winning refuse rule");
        Assert.True(refusedAnswers > refusedScopes, "only " + refusedAnswers + " refused answers");
    }

    // ---- INV-05 a recorded external change number alone never authorizes ----

    // Over every shipped selection: OP-14 in linked mode with an external change number and no fact, or only facts that
    // can never count (no evidence, AI written, an agent principal, a person on the pipeline's path, an attestation off
    // the card or by someone without the role, a grant dated later), never authorizes and warns number-only; and OP-04,
    // given that result or none, reports the approval missing for every tier, change type that needs authorization,
    // target stage and mode, wherever the gate is not off.
    [Fact]
    [Invariant("INV-05")]
    public void AnExternalChangeNumberAloneNeverAuthorizesInEveryShippedSelection()
    {
        int gates = 0;
        int evaluations = 0;
        foreach (PresetSelection selection in Selections())
        {
            CompiledSnapshot compiled = Snapshot(selection);
            EvaluationContext context = Context(compiled);
            foreach (AuthorizationFact[] facts in new[] { Array.Empty<AuthorizationFact>() }.Concat(NonCountingFacts().Select(f => new[] { f })))
            {
                var request = new LinkedAuthorizationRequest(ChangeManagementMode.Linked, true, facts);
                EvaluationResult<LinkedAuthorizationResult?> result = LinkedAuthorizationOperation.Evaluate(compiled.Snapshot, context, request);
                string at = selection + " " + (facts.Length == 0 ? "no fact" : facts[0].FactId);
                Assert.True(result.Outcome == "not-authorized", at + ": " + result.Outcome);
                Assert.False(result.Payload!.Authorized, at);
                Assert.Equal(AuthorizationBasis.None, result.Payload.Basis);
                Assert.Contains(result.Reasons, r => r.Code == "authorization.number-only");
                evaluations++;
            }

            JsonTreeNode numberOnly = OperationJson.Evaluate(EngineOperation.EvaluateLinkedAuthorization, compiled.Snapshot, context,
                CanonicalJson.Parse("{\"changeManagementMode\":\"linked\",\"hasExternalChangeNumber\":true}")).Payload!;
            foreach (string tier in Active(compiled.Snapshot, "tier").Select(r => r.Key))
            {
                foreach (SnapshotRow type in Active(compiled.Snapshot, "change-type").Where(t => Flag(t, "requiresAuthorization")))
                {
                    foreach (string stage in Active(compiled.Snapshot, "target-stage").Select(r => r.Key))
                    {
                        foreach (string mode in new[] { "check", "record" })
                        {
                            foreach (JsonTreeNode? linked in new[] { numberOnly, null })
                            {
                                string change = "{\"changeManagementMode\":\"linked\",\"hasExternalChangeNumber\":true" + (linked == null ? string.Empty : ",\"linkedAuthorization\":" + OperationTestData.Canonical(linked))
                                    + ",\"windowStartUtc\":\"2026-11-20T14:00:00Z\",\"windowEndUtc\":\"2026-11-20T18:00:00Z\"}";
                                JsonTreeNode gate = CanonicalJson.Parse("{\"mode\":\"" + mode + "\",\"tierKey\":\"" + tier + "\",\"changeTypeKey\":\"" + type.Key + "\",\"targetStageKey\":\"" + stage + "\",\"portfolioPath\":[],"
                                    + "\"routeKey\":\"pipeline-step\",\"eventUtc\":\"2026-11-20T15:00:00Z\",\"allowedRouteKeys\":[\"pipeline-step\"],\"codeApprovalKind\":\"person\",\"change\":" + change + "}");
                                EvaluationResult<JsonTreeNode?> result = OperationJson.Evaluate(EngineOperation.EvaluateGateDecision, compiled.Snapshot, context, gate);
                                string at = selection + " " + tier + " " + type.Key + " " + stage + " " + mode + (linked == null ? " without OP-14" : " with OP-14");
                                Assert.True(result.Payload != null, at + ": " + string.Join(" | ", result.Reasons));
                                bool off = JsonNodes.GetString(result.Payload!, "gateLevel") == "off";
                                bool missing = JsonNodes.Get(result.Payload!, "isMissingApproval")!.Kind == JsonTreeKind.True;
                                Assert.True(missing == !off, at + ": missing approval " + missing + " at gate level " + JsonNodes.GetString(result.Payload!, "gateLevel"));
                                gates++;
                            }
                        }
                    }
                }
            }
        }

        Assert.True(evaluations >= 22 * 10, "only " + evaluations + " OP-14 evaluations");
        Assert.True(gates > 1000, "only " + gates + " gate evaluations");
    }

    // ---- INV-06 a missing gate policy never falls back to a level ----

    // Over every shipped selection: with every gate policy retired, every tier, change type, target stage and mode is
    // unavailable with gate-policy.missing and no payload; and with any one gate policy retired, its own combination is
    // either gate-policy.missing or decided by another active gate policy row that the trace names as the winner and
    // whose level the answer reports. No answer ever carries a level without a gate policy row behind it.
    [Fact]
    [Invariant("INV-06")]
    public void AMissingGatePolicyNeverFallsBackInEveryShippedSelection()
    {
        int missing = 0;
        int retiredOne = 0;
        foreach (PresetSelection selection in Selections())
        {
            CompiledSnapshot compiled = Snapshot(selection);
            ConfigurationSnapshot none = WithInactive(compiled.Snapshot, "gate-policy", _ => true);
            foreach (string tier in Active(compiled.Snapshot, "tier").Select(r => r.Key))
            {
                foreach (string changeType in Active(compiled.Snapshot, "change-type").Select(r => r.Key))
                {
                    foreach (string stage in Active(compiled.Snapshot, "target-stage").Select(r => r.Key))
                    {
                        foreach (GateMode mode in new[] { GateMode.Check, GateMode.Record })
                        {
                            EvaluationResult<GateDecision?> result = GateDecisionOperation.Evaluate(none, Context(compiled), Gate(mode, tier, changeType, stage));
                            string at = selection + " " + tier + " " + changeType + " " + stage + " " + mode;
                            Assert.True(result.Outcome == OperationOutcomes.Unavailable, at + " with no gate policy: " + result.Outcome);
                            Assert.Null(result.Payload);
                            Assert.Equal(new[] { "gate-policy.missing" }, OperationTestData.Codes(result.Reasons));
                            missing++;
                        }
                    }
                }
            }

            foreach (SnapshotRow policy in Active(compiled.Snapshot, "gate-policy"))
            {
                ConfigurationSnapshot without = WithInactive(compiled.Snapshot, "gate-policy", r => r.KeyPath == policy.KeyPath);
                foreach (GateMode mode in new[] { GateMode.Check, GateMode.Record })
                {
                    GateRequest request = Gate(mode, Text(policy, "tierKey")!, Text(policy, "changeTypeKey")!, Text(policy, "targetStageKey")!);
                    EvaluationResult<GateDecision?> result = GateDecisionOperation.Evaluate(without, Context(compiled), request);
                    string at = selection + " without " + policy.KeyPath + " " + mode;
                    if (result.Payload == null)
                    {
                        Assert.True(result.Outcome == OperationOutcomes.Unavailable, at + ": " + result.Outcome);
                        Assert.Equal(new[] { "gate-policy.missing" }, OperationTestData.Codes(result.Reasons));
                    }
                    else
                    {
                        RuleTrace winner = Assert.Single(result.Trace, t => t.Entity == "Gate Policy" && t.Role == RuleTraceRole.Won);
                        Assert.NotEqual(policy.KeyPath, winner.Key);
                        Assert.True(without.TryGetRow("gate-policy", winner.Key, out SnapshotRow? other) && other.IsActive, at + " won by " + winner.Key);
                        Assert.Equal(Text(other!, "gateLevel"), EngineContract.GateLevels.ToWire(result.Payload.GateLevel));
                    }

                    retiredOne++;
                }
            }
        }

        Assert.True(missing > 1000, "only " + missing + " combinations checked with no gate policy");
        Assert.True(retiredOne > 200, "only " + retiredOne + " single retirements checked");
    }

    // ---- INV-07 a more serious answer never lowers the tier ----

    // Over every shipped selection and every application kind with tier questions: for every question, with every other
    // answer at its lowest and again at its highest, moving that answer to a more serious option never lowers the
    // computed rank, and the rank is the highest rank the answers imply.
    [Fact]
    [Invariant("INV-07")]
    public void AMoreSeriousAnswerNeverLowersTheTierInEveryShippedSelection()
    {
        int evaluations = 0;
        foreach (PresetSelection selection in Selections())
        {
            CompiledSnapshot compiled = Snapshot(selection);
            ConfigurationSnapshot snapshot = compiled.Snapshot;
            foreach (string kind in Active(snapshot, "application-kind").Select(k => k.Key))
            {
                List<SnapshotRow> questions = Active(snapshot, "tier-question").Where(q => AppliesTo(q, kind)).ToList();
                if (questions.Count == 0)
                {
                    continue;
                }

                Dictionary<string, List<SnapshotRow>> options = questions.ToDictionary(
                    q => q.Key,
                    q => Active(snapshot, "tier-answer-option").Where(o => o.ParentKey == q.Key).OrderBy(o => ImpliedRank(snapshot, o)).ToList(),
                    StringComparer.Ordinal);
                foreach (bool othersHigh in new[] { false, true })
                {
                    Dictionary<string, SnapshotRow> others = options.ToDictionary(p => p.Key, p => othersHigh ? p.Value[p.Value.Count - 1] : p.Value[0], StringComparer.Ordinal);
                    foreach (SnapshotRow question in questions)
                    {
                        int previous = int.MinValue;
                        foreach (SnapshotRow option in options[question.Key])
                        {
                            var answers = others.Select(p => new TierAnswer(p.Key, p.Key == question.Key ? option.Key : p.Value.Key)).ToList();
                            EvaluationResult<TierResult?> result = TierOperation.Evaluate(snapshot, Context(compiled), new TierRequest(kind, answers));
                            string at = selection + " " + kind + " " + question.Key + "/" + option.Key + (othersHigh ? " (others high)" : " (others low)");
                            Assert.True(result.Outcome == "computed", at + ": " + result.Outcome + " " + string.Join(" | ", result.Reasons));
                            int rank = result.Payload!.ComputedTierRank!.Value;
                            int expected = answers.Select(a => ImpliedRank(snapshot, options[a.QuestionKey].First(o => o.Key == a.AnswerKey))).Max();
                            Assert.True(rank == expected, at + ": rank " + rank + ", expected " + expected);
                            Assert.True(rank >= previous, at + " lowered the tier");
                            previous = rank;
                            evaluations++;
                        }
                    }
                }
            }
        }

        Assert.True(evaluations > 1000, "only " + evaluations + " evaluations");
    }

    // ---- INV-09 OP-09 rejects any snapshot with the AI lock off ----

    // Over every shipped selection, in preset build, draft and activation mode: the AI approval lock set to false, retired,
    // or given a value that is not true is invalid with setting.ai-approval-lock-off (V33); and activation discards a
    // draft that turns it off, so no version with the lock off ever comes into force. The same selection untouched is
    // valid, so the refusal comes from the lock alone.
    [Fact]
    [Invariant("INV-09")]
    public void OpNineRejectsEverySnapshotWithTheAiLockOffInEveryShippedSelection()
    {
        string[] lockOff =
        {
            "[{\"op\":\"upsert\",\"entity\":\"setting\",\"key\":\"ai-approval-lock\",\"values\":{\"value\":false}}]",
            "[{\"op\":\"retire\",\"entity\":\"setting\",\"key\":\"ai-approval-lock\"}]",
            "[{\"op\":\"upsert\",\"entity\":\"setting\",\"key\":\"ai-approval-lock\",\"values\":{\"value\":\"true\"}}]",
            "[{\"op\":\"upsert\",\"entity\":\"setting\",\"key\":\"ai-approval-lock\",\"values\":{\"value\":0}}]",
        };
        var validator = new ConfigurationValidator(OperationTestData.Shipped);
        var activator = new ConfigurationActivator(OperationTestData.Shipped);
        int refusals = 0;
        foreach (PresetSelection selection in Selections())
        {
            ContentSource active = ContentSource.Preset(selection.PresetKey, selection.Overlays);
            Assert.Equal("valid", validator.Validate(new ValidationRequest(active, ValidationMode.PresetBuild)).Outcome);
            foreach (string operations in lockOff)
            {
                ContentSource candidate = active.WithPatch(ConfigurationPatch.FromOperations(CanonicalJson.Parse(operations), PatchKind.Draft, "lock off"));
                foreach (ValidationMode mode in new[] { ValidationMode.PresetBuild, ValidationMode.Draft, ValidationMode.Activation })
                {
                    ValidationOutcome outcome = validator.Validate(new ValidationRequest(candidate, mode, mode == ValidationMode.PresetBuild ? null : active, versionNumber: mode == ValidationMode.PresetBuild ? 1 : 2, facts: new ValidationFacts(validatedOn: AsOf)));
                    string at = selection + " " + mode + " " + operations;
                    Assert.True(outcome.Outcome == "invalid", at + ": " + outcome.Outcome);
                    Assert.Null(outcome.Snapshot);
                    Assert.Contains(outcome.Report.Issues, i => i.Code == "setting.ai-approval-lock-off" || (i.RuleId == "V01" && operations.Contains("\"value\":")));
                    refusals++;
                }

                string draft = "{\"schemaVersion\":\"1.0\",\"manifest\":{\"kind\":\"patch\",\"baseVersionNumber\":1},\"operations\":" + operations + "}";
                ActivationOutcome activation = activator.Activate(
                    new EvaluationContext(1, Snapshot(selection).ContentHash, AsOf, Nested(), "invariant-correlation"),
                    new ActivationRequest("fixture-draft-1", DraftStatusMeaning.Submitted, "fixture-decision-7", DecisionStatusMeaning.Accepted, active, 1, active, 1,
                        ConfigurationPatch.FromDocument(CanonicalJson.Parse(draft), PatchKind.Draft, "lock off draft"), null, approvalRulesVersionNumber: 1, instructedByPrincipalId: "fixture-person-1"));
                Assert.True(activation.Outcome != ActivationOutcome.Activated && activation.Snapshot == null, selection + " activated a draft that turns the lock off: " + activation.Outcome);
                refusals++;
            }
        }

        Assert.True(refusals >= 22 * 4 * 4, "only " + refusals + " refusals");
    }

    // ---- INV-11 the same input gives the same hash ----

    // Over every shipped selection: two compilations from fresh preset reads give the same Content Hash and the same
    // canonical text; the bundle written as text and read back, with or without every object's members reversed, gives
    // the same hash; one changed value gives another hash; and a recorded OP-02 and OP-04 answer replays against the
    // version with no difference (the replay tool of section 9).
    [Fact]
    [Invariant("INV-11")]
    public void TheSameInputGivesTheSameHashInEveryShippedSelection()
    {
        PresetCatalog fresh = PresetCatalog.Load(new[] { SourceRoot.PresetsFolder(TestData.Root) });
        ReplayTool replay = ReplayTool.ForSourceRoot(TestData.Root);
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (PresetSelection selection in Selections())
        {
            ContentSource source = ContentSource.Preset(selection.PresetKey, selection.Overlays);
            ComposedConfiguration first = new ConfigurationComposer(OperationTestData.Shipped).Compose(source, ValidationMode.Draft, new ValidationIssues());
            ComposedConfiguration second = new ConfigurationComposer(fresh).Compose(source, ValidationMode.Draft, new ValidationIssues());
            CompiledSnapshot a = SnapshotCompiler.Compile(first.Bundle, 1);
            CompiledSnapshot b = SnapshotCompiler.Compile(second.Bundle, 1);
            Assert.Equal(a.ContentHash, b.ContentHash);
            Assert.Equal(a.CanonicalText, b.CanonicalText);
            Assert.Equal(Snapshot(selection).ContentHash, a.ContentHash);

            string written = JsonText.WriteIndented(a.ContentJson, snapshotOrder: true);
            Assert.Equal(a.ContentHash, CanonicalHash.ComputeContentHash(CanonicalJson.Parse(written)));
            Assert.Equal(a.ContentHash, CanonicalHash.ComputeContentHash(CanonicalJson.Parse(Reversed(a.ContentJson))));
            Assert.True(hashes.Add(a.ContentHash), selection + " has the hash of another selection");

            ContentSource changed = source.WithPatch(ConfigurationPatch.FromOperations(CanonicalJson.Parse("[{\"op\":\"upsert\",\"entity\":\"tier\",\"key\":\"" + Active(a.Snapshot, "tier").First().Key + "\",\"values\":{\"label\":\"A changed label\"}}]"), PatchKind.Draft, "one change"));
            CompiledSnapshot c = SnapshotCompiler.Compile(new ConfigurationComposer(OperationTestData.Shipped).Compose(changed, ValidationMode.Draft, new ValidationIssues()).Bundle, 1);
            Assert.NotEqual(a.ContentHash, c.ContentHash);

            foreach (JsonTreeNode record in Records(selection, a.Snapshot))
            {
                JsonTreeNode captured = replay.Capture(record);
                Assert.Equal(a.ContentHash, JsonNodes.GetString(JsonNodes.Get(captured, "recorded")!, "snapshotHash"));
                ReplayResult result = replay.Replay(captured, selection + " " + JsonNodes.GetString(record, "operation"));
                Assert.True(result.Status == ReplayStatus.Same, selection + ": " + string.Join(" | ", result.Differences.Select(d => d.ToString()).Concat(result.Problems)));
            }
        }

        Assert.Equal(22, hashes.Count);
    }

    // ---- Generators and helpers ----

    private static IEnumerable<PresetSelection> Selections()
    {
        return DesignInvariants.Selections(OperationTestData.Shipped);
    }

    private static CompiledSnapshot Snapshot(PresetSelection selection)
    {
        return OperationTestData.Snapshot(selection.PresetKey, null, selection.Overlays);
    }

    // The tests of earlier slices that already run over every shipped selection.
    private static bool IsOverEverySelection(string test)
    {
        string[] overEverySelection =
        {
            "OperationTests.NoDecisionOutsideTheLockedListIsAcceptedWithoutACard",
            "OperationTests.EnforceNeverStopsForWindowOrPathAlone",
            "WriteGuardTests.AiCreatedRowsStartAsDraftOrProposedInEveryShippedSelection",
            "WriteGuardTests.AnApprovalCountsOnlyFromAPersonInEveryShippedSelection",
            "WriteGuardTests.NoWriteOutsideActivationChangesAConfigurationRow",
        };
        return overEverySelection.Contains(test, StringComparer.Ordinal);
    }

    // One OP-02 scope with cards: a decision type, a tier (or none) and, for change authorization, a change type and a
    // target stage, with the requirements and consultations OP-02 returned. Scopes with the same requirements and the same
    // separation scope (type, tier, stage) are checked once.
    private sealed class ApprovalScope
    {
        public ApprovalScope(string decisionTypeKey, string? tierKey, string? changeTypeKey, string? targetStageKey, ApprovalScopeResult result)
        {
            DecisionTypeKey = decisionTypeKey;
            TierKey = tierKey;
            ChangeTypeKey = changeTypeKey;
            TargetStageKey = targetStageKey;
            Requirements = result.Requirements.ToList();
            Consultations = result.Consultations.ToList();
        }

        public string DecisionTypeKey { get; }

        public string? TierKey { get; }

        public string? ChangeTypeKey { get; }

        public string? TargetStageKey { get; }

        public List<RequiredApproval> Requirements { get; }

        public List<RequiredApproval> Consultations { get; }

        public string Identity => DecisionTypeKey + "|" + TierKey + "|" + TargetStageKey + "|" + string.Join(",", Requirements.Concat(Consultations).Select(r => r.RequirementKey + (r.IsConsultation ? "?" : string.Empty)));

        public override string ToString()
        {
            return DecisionTypeKey + " " + (TierKey ?? "-") + " " + (ChangeTypeKey ?? "-") + " " + (TargetStageKey ?? "-");
        }
    }

    private static IEnumerable<ApprovalScope> Scopes(CompiledSnapshot compiled)
    {
        ConfigurationSnapshot snapshot = compiled.Snapshot;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        List<string?> tiers = Active(snapshot, "tier").Select(t => (string?)t.Key).Concat(new string?[] { null }).ToList();
        foreach (SnapshotRow type in Active(snapshot, "decision-type"))
        {
            bool isChange = type.Key == EngineKeys.ChangeAuthorizationDecisionType;
            string?[] changeTypes = isChange ? Active(snapshot, "change-type").Select(c => (string?)c.Key).ToArray() : new string?[] { null };
            string?[] stages = isChange ? Active(snapshot, "target-stage").Select(s => (string?)s.Key).ToArray() : new string?[] { null };
            foreach (string? tier in tiers)
            {
                foreach (string? changeType in changeTypes)
                {
                    foreach (string? stage in stages)
                    {
                        var request = new ApprovalScopeRequest(type.Key, tier, null, changeType, "power-platform-app", stage, null, new StandardChangeFact(false));
                        EvaluationResult<ApprovalScopeResult?> result = RequiredApprovalsOperation.Evaluate(snapshot, Context(compiled), request);
                        if (result.Outcome != "required")
                        {
                            continue;
                        }

                        var scope = new ApprovalScope(type.Key, tier, changeType, stage, result.Payload!);
                        if (seen.Add(scope.Identity))
                        {
                            yield return scope;
                        }
                    }
                }
            }
        }
    }

    private static EvaluationResult<ApprovalStatusResult?> Status(CompiledSnapshot compiled, ApprovalScope scope, IEnumerable<ApprovalFact> records)
    {
        var request = new ApprovalStatusRequest(scope.Requirements.Concat(scope.Consultations), records, scope.DecisionTypeKey, scope.TierKey, scope.TargetStageKey, new SeparationFacts(Proposer));
        return ApprovalStatusOperation.Evaluate(compiled.Snapshot, Context(compiled), request);
    }

    // An approved answer under a requirement from every actor kind and principal kind but a person signed in as a user,
    // each holding the role, with a typed reason, in time.
    private static IEnumerable<ApprovalFact> MachineAnswers(string requirementKey)
    {
        int n = 0;
        foreach (ActorKindMeaning actor in EngineContract.ActorKindMeanings.Values)
        {
            foreach (PrincipalKind principal in EngineContract.PrincipalKinds.Values)
            {
                if (actor == ActorKindMeaning.Person && principal == PrincipalKind.User)
                {
                    continue;
                }

                n++;
                string id = "m-" + requirementKey + "-" + n.ToString(CultureInfo.InvariantCulture);
                yield return new ApprovalFact(id, requirementKey, ApprovalOutcomeMeaning.Approved, "fixture-machine-" + n.ToString(CultureInfo.InvariantCulture), actor, principal, true, true, Answered, Expires);
            }
        }
    }

    // As many people as the requirement's Minimum Count, each a person signed in as a user who holds the role.
    private static IEnumerable<ApprovalFact> PeopleAnswers(RequiredApproval requirement)
    {
        for (int i = 1; i <= requirement.MinimumCount; i++)
        {
            string n = i.ToString(CultureInfo.InvariantCulture);
            yield return Person("p-" + requirement.RequirementKey + "-" + n, requirement.RequirementKey, "fixture-person-" + requirement.RequirementKey + "-" + n);
        }
    }

    private static ApprovalFact Person(string id, string requirementKey, string principal)
    {
        return new ApprovalFact(id, requirementKey, ApprovalOutcomeMeaning.Approved, principal, ActorKindMeaning.Person, PrincipalKind.User, true, true, Answered, Expires);
    }

    private static string RecordJson(ApprovalFact record)
    {
        return "{\"approvalRecordId\":\"" + record.ApprovalRecordId + "\",\"ruleKey\":\"" + record.RuleKey + "\",\"outcome\":\"approved\",\"responderPrincipalId\":\"" + record.ResponderPrincipalId
            + "\",\"responderActorKind\":\"person\",\"responderPrincipalKind\":\"user\",\"responderHoldsRequiredRole\":true,\"hasTypedReason\":true,\"respondedOnUtc\":\"2026-11-22T08:00:00Z\",\"expiresOnUtc\":\"2026-11-27T09:00:00Z\"}";
    }

    private static string ScopeJson(ApprovalScope scope)
    {
        return "{\"decisionTypeKey\":\"" + scope.DecisionTypeKey + "\"" + (scope.TierKey == null ? string.Empty : ",\"tierKey\":\"" + scope.TierKey + "\"")
            + (scope.TargetStageKey == null ? string.Empty : ",\"targetStageKey\":\"" + scope.TargetStageKey + "\"") + "}";
    }

    // Facts that can never count toward a linked authorization (document 25, OP-14 rules 2 to 4, and a fact dated after the evaluation moment).
    private static IEnumerable<AuthorizationFact> NonCountingFacts()
    {
        DateTime at = new(2026, 11, 21, 10, 0, 0, DateTimeKind.Utc);
        yield return new AuthorizationFact("itsm-no-evidence", AuthorizationFactSourceKind.ItsmStep, AuthorizationState.Authorized, false, at, ActorKindMeaning.Pipeline, PrincipalKind.ServicePrincipal);
        yield return new AuthorizationFact("itsm-by-ai-client", AuthorizationFactSourceKind.ItsmStep, AuthorizationState.Authorized, true, at, ActorKindMeaning.AiClientForPerson, PrincipalKind.User);
        yield return new AuthorizationFact("itsm-agent-principal", AuthorizationFactSourceKind.ItsmStep, AuthorizationState.Authorized, true, at, ActorKindMeaning.Pipeline, PrincipalKind.AgentIdentity);
        yield return new AuthorizationFact("itsm-by-person", AuthorizationFactSourceKind.ItsmStep, AuthorizationState.Authorized, true, at, ActorKindMeaning.Person, PrincipalKind.User);
        yield return new AuthorizationFact("attestation-off-card", AuthorizationFactSourceKind.Attestation, AuthorizationState.Authorized, true, at, ActorKindMeaning.Person, PrincipalKind.User, true, false);
        yield return new AuthorizationFact("attestation-without-role", AuthorizationFactSourceKind.Attestation, AuthorizationState.Authorized, true, at, ActorKindMeaning.Person, PrincipalKind.User, false, true);
        yield return new AuthorizationFact("attestation-by-ai-client", AuthorizationFactSourceKind.Attestation, AuthorizationState.Authorized, true, at, ActorKindMeaning.AiClientForPerson, PrincipalKind.User, true, true);
        yield return new AuthorizationFact("attestation-by-agent-user", AuthorizationFactSourceKind.Attestation, AuthorizationState.Authorized, true, at, ActorKindMeaning.Person, PrincipalKind.AgentUser, true, true);
        yield return new AuthorizationFact("attestation-by-agent-identity", AuthorizationFactSourceKind.Attestation, AuthorizationState.Authorized, true, at, ActorKindMeaning.AgentIdentity, PrincipalKind.AgentIdentity, true, true);
        yield return new AuthorizationFact("itsm-dated-later", AuthorizationFactSourceKind.ItsmStep, AuthorizationState.Authorized, true, AsOf.AddMinutes(1), ActorKindMeaning.Pipeline, PrincipalKind.ServicePrincipal);
    }

    private static GateRequest Gate(GateMode mode, string tier, string changeType, string stage)
    {
        var change = new ChangeFact(ChangeManagementMode.BuiltIn, approvalOverall: ApprovalOverall.Satisfied);
        return new GateRequest(mode, tier, stage, "pipeline-step", new[] { "pipeline-step" }, new DateTime(2026, 11, 20, 15, 0, 0, DateTimeKind.Utc), changeType, null, change, CodeApprovalKind.Person);
    }

    // The same snapshot (same hash and version) with the chosen rows of one entity made inactive, as a retire would leave
    // them. Built in memory, so every row of every selection can be retired one at a time without a compile.
    private static ConfigurationSnapshot WithInactive(ConfigurationSnapshot snapshot, string entityName, Func<SnapshotRow, bool> retire)
    {
        IEnumerable<SnapshotEntity> entities = snapshot.Entities.Select(e => e.Name != entityName
            ? e
            : new SnapshotEntity(e.Name, e.Rows.Select(r => retire(r) ? new SnapshotRow(r.Key, false, r.Origin, r.Columns, r.EngineMeaning, r.ParentKey) : r)));
        return new ConfigurationSnapshot(snapshot.SchemaVersion, snapshot.ConfigurationSetKey, snapshot.VersionNumber, snapshot.ContentHash, entities);
    }

    // Every caller that is not the accept-decision effect: people with each role of the selection, AI clients, an agent
    // user, a pipeline, the approval runner, and the engine in every other effect or in none.
    private static IEnumerable<(ActorInfo Actor, EffectKind? Effect)> Callers(string[] roles)
    {
        yield return (new ActorInfo("fixture-person-1", PrincipalKind.User, ActorKindMeaning.Person), null);
        foreach (string role in roles)
        {
            yield return (new ActorInfo("fixture-person-1", PrincipalKind.User, ActorKindMeaning.Person, new[] { role }), null);
            yield return (new ActorInfo("fixture-person-1", PrincipalKind.User, ActorKindMeaning.AiClientForPerson, new[] { role }, "fixture-person-1"), null);
        }

        yield return (new ActorInfo("fixture-agent-1", PrincipalKind.AgentUser, ActorKindMeaning.AiClientForPerson, roles, "fixture-person-1"), null);
        yield return (new ActorInfo("fixture-pipeline-1", PrincipalKind.ServicePrincipal, ActorKindMeaning.Pipeline, new[] { "deployment-recorder" }), null);
        yield return (new ActorInfo("fixture-runner-1", PrincipalKind.ServicePrincipal, ActorKindMeaning.ApprovalRunner, new[] { "approval-writer" }, "fixture-person-2"), null);
        yield return (Nested(), null);
        foreach (EffectKind effect in EngineContract.EffectKinds.Values.Where(e => e != EffectKind.AcceptDecision))
        {
            yield return (Nested(), effect);
        }
    }

    // Every acceptance basis the accept-decision effect can carry, the true ones and the false ones.
    private static IEnumerable<WriteAcceptance> Bases()
    {
        foreach (string outcome in new[] { "satisfied", "pending", "rejected", "expired" })
        {
            yield return new WriteAcceptance(WriteAcceptance.ApprovalsSatisfied, approvalStatusOutcome: outcome);
        }

        foreach (string outcome in new[] { "engine-accept", "required" })
        {
            foreach (EngineAcceptanceCondition condition in new[] { EngineAcceptanceCondition.LowestTier, EngineAcceptanceCondition.StandardModel })
            {
                yield return new WriteAcceptance(WriteAcceptance.EngineAccept, requiredApprovalsOutcome: outcome, condition: condition);
            }
        }

        yield return new WriteAcceptance(WriteAcceptance.EngineAccept, requiredApprovalsOutcome: "engine-accept");
        yield return new WriteAcceptance(WriteAcceptance.Bootstrap, noVersionExists: true);
    }

    private static EvaluationResult<WritePlan?> Guard(CompiledSnapshot compiled, ActorInfo actor, string entity, WriteOperation op, string? before, string? after, EffectKind? effect, WriteAcceptance? acceptance = null, JsonTreeNode? gateReadiness = null)
    {
        var request = new WriteRequest(entity, op, op == WriteOperation.Create ? null : "fixture-decision-1", after == null ? null : CanonicalJson.Parse(after), before == null ? null : CanonicalJson.Parse(before), effect, acceptance: acceptance, gateReadiness: gateReadiness);
        return WriteGuardOperation.Evaluate(compiled.Snapshot, new EvaluationContext(compiled.VersionNumber, compiled.ContentHash, AsOf, actor, "invariant-correlation"), request);
    }

    // The write is allowed and stores the meaning accepted.
    private static bool Accepts(EvaluationResult<WritePlan?> result)
    {
        return result.Outcome == "allowed" && result.Payload!.SetColumns.TryGetMember("statusMeaning", out JsonTreeNode? meaning)
            && meaning.Kind == JsonTreeKind.String && meaning.GetString() == "accepted";
    }

    private static string Describe(ActorInfo actor, EffectKind? effect)
    {
        return EngineContract.ActorKindMeanings.ToWire(actor.ActorKind) + (actor.IsEngineNested ? " nested " + (effect?.ToString() ?? "no effect") : string.Empty)
            + (actor.RoleKeys.Count == 0 ? string.Empty : " (" + string.Join(", ", actor.RoleKeys) + ")");
    }

    private static ActorInfo Nested()
    {
        return new ActorInfo("fixture-runner-1", PrincipalKind.ServicePrincipal, ActorKindMeaning.ApprovalRunner, new[] { "approval-writer" }, "fixture-person-2", isEngineNested: true);
    }

    private static EvaluationContext Context(CompiledSnapshot compiled)
    {
        return OperationTestData.Context(compiled);
    }

    // Two recorded calls for the replay: OP-02 for the first decision type that is not change authorization at its
    // highest tier, and OP-04 for the first gate policy row.
    private static IEnumerable<JsonTreeNode> Records(PresetSelection selection, ConfigurationSnapshot snapshot)
    {
        string snapshotJson = "{\"presetKey\":\"" + selection.PresetKey + "\"" + (selection.Overlays.Length == 0 ? string.Empty : ",\"overlays\":[" + string.Join(",", selection.Overlays.Select(o => "\"" + o + "\"")) + "]") + "}";
        string context = "{\"asOfUtc\":\"2026-11-22T09:00:00Z\",\"correlationId\":\"invariant-replay\"}";
        string type = Active(snapshot, "decision-type").First(t => t.Key != EngineKeys.ChangeAuthorizationDecisionType).Key;
        string tier = Active(snapshot, "tier").OrderByDescending(t => Rank(t)).First().Key;
        yield return CanonicalJson.Parse("{\"schemaVersion\":\"1.0\",\"operation\":\"OP-02\",\"case\":\"invariant-op-02\",\"snapshot\":" + snapshotJson + ",\"context\":" + context
            + ",\"request\":{\"decisionTypeKey\":\"" + type + "\",\"tierKey\":\"" + tier + "\"}}");

        SnapshotRow policy = Active(snapshot, "gate-policy").First();
        yield return CanonicalJson.Parse("{\"schemaVersion\":\"1.0\",\"operation\":\"OP-04\",\"case\":\"invariant-op-04\",\"snapshot\":" + snapshotJson + ",\"context\":" + context
            + ",\"request\":{\"mode\":\"check\",\"tierKey\":\"" + Text(policy, "tierKey") + "\",\"changeTypeKey\":\"" + Text(policy, "changeTypeKey") + "\",\"targetStageKey\":\"" + Text(policy, "targetStageKey")
            + "\",\"routeKey\":\"pipeline-step\",\"allowedRouteKeys\":[\"pipeline-step\"],\"eventUtc\":\"2026-11-20T15:00:00Z\",\"change\":{\"changeManagementMode\":\"built-in\",\"approvalOverall\":\"pending\"}}}");
    }

    // JSON text with every object's members in reverse ordinal order, so a reader that kept text order would differ.
    private static string Reversed(JsonTreeNode node)
    {
        var sb = new StringBuilder();
        WriteReversed(sb, node);
        return sb.ToString();
    }

    private static void WriteReversed(StringBuilder sb, JsonTreeNode node)
    {
        switch (node.Kind)
        {
            case JsonTreeKind.Object:
                sb.Append('{');
                bool firstMember = true;
                foreach (KeyValuePair<string, JsonTreeNode> member in node.Members.Reverse())
                {
                    if (!firstMember)
                    {
                        sb.Append(',');
                    }

                    sb.Append(CanonicalJson.Write(JsonTreeNode.FromString(member.Key), CanonicalProfile.Snapshot)).Append(':');
                    WriteReversed(sb, member.Value);
                    firstMember = false;
                }

                sb.Append('}');
                break;
            case JsonTreeKind.Array:
                sb.Append('[');
                for (int i = 0; i < node.Items.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }

                    WriteReversed(sb, node.Items[i]);
                }

                sb.Append(']');
                break;
            default:
                sb.Append(CanonicalJson.Write(node, CanonicalProfile.Snapshot));
                break;
        }
    }

    private static IEnumerable<SnapshotRow> Active(ConfigurationSnapshot snapshot, string entity)
    {
        return snapshot.TryGetEntity(entity, out SnapshotEntity? e) ? e.ActiveRows : Enumerable.Empty<SnapshotRow>();
    }

    private static IEnumerable<SnapshotRow> Statuses(ConfigurationSnapshot snapshot, string entity)
    {
        return Active(snapshot, "status").Where(r => Text(r, "appliesToEntity") == entity && r.EngineMeaning != "missing");
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

    private static int Rank(SnapshotRow tier)
    {
        return tier.TryGetColumn("rank", out JsonTreeNode? rank) && rank.TryGetInt64(out long value) ? (int)value : 0;
    }

    private static int ImpliedRank(ConfigurationSnapshot snapshot, SnapshotRow option)
    {
        Assert.True(snapshot.TryGetRow("tier", Text(option, "impliedTierKey")!, out SnapshotRow? tier));
        return Rank(tier!);
    }
}
