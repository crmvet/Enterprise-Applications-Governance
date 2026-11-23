using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Operations;
using Product.Engine.Core.Snapshots;
using Xunit;

namespace Product.Engine.Configuration.Tests;

// Operation tests, part 2: OP-06, OP-07, OP-10 and OP-11 (document 25, sections 5.8, 5.9 and 5.12). The fixtures under
// fixtures/op-06, op-07, op-10 and op-11 hold the cases (OperationTests runs them). These tests add checks generated
// over every shipped preset selection, independent re-computations of the walk and the reminder schedule, the typed
// API that adapters call, and the broken configurations the fixture schema rightly refuses as inline operations.
public sealed class OperationPart2Tests
{
    private static readonly DateTime AsOf = OperationTestData.AsOf;

    // ---- OP-06 ----

    // Working default, 28 issue 16: missing is computed, never stored.
    [Fact]
    public void AStoredMissingIsNeverAccepted()
    {
        Assert.Throws<ArgumentException>(() => new DocumentFact("l1", "wiki-page", DocumentStatusMeaning.Missing));
    }

    // Over every shipped selection, application kind, tier (and none) and stage: with no link, every applicable
    // requirement is computed as missing; with a fresh current link of every document type, the application is
    // complete. The applicable set is worked out here on its own. A shipped preset never gives an unavailable answer.
    [Fact]
    public void EveryShippedScopeComputesMissingAndCompletesWithFreshLinks()
    {
        int evaluations = 0;
        foreach (KeyValuePair<string, string[]> selection in OperationTestData.ShippedSelections())
        {
            CompiledSnapshot compiled = OperationTestData.Snapshot(selection.Key, null, selection.Value);
            ConfigurationSnapshot snapshot = compiled.Snapshot;
            EvaluationContext context = OperationTestData.Context(compiled);
            DocumentFact[] fresh = Rows(snapshot, "document-type")
                .Select((type, i) => new DocumentFact("l" + i.ToString(CultureInfo.InvariantCulture), type.Key, DocumentStatusMeaning.Current, lastCheckedOnUtc: AsOf.AddDays(-1)))
                .ToArray();
            List<string?> tiers = Active(snapshot, "tier").Select(t => (string?)t.Key).ToList();
            tiers.Add(null);
            foreach (string kind in Active(snapshot, "application-kind").Select(k => k.Key))
            {
                foreach (string? tier in tiers)
                {
                    foreach (SnapshotRow stage in Active(snapshot, "lifecycle-stage"))
                    {
                        string[] expected = Active(snapshot, "documentation-requirement")
                            .Where(r => Scope(r, "tierKey", tier) && Scope(r, "applicationKindKey", kind) && StageRank(snapshot, Text(r, "fromStageKey")!) <= StageRank(snapshot, stage.Key))
                            .Select(r => r.Key)
                            .OrderBy(k => k, StringComparer.Ordinal)
                            .ToArray();
                        string at = selection.Key + "+" + string.Join("+", selection.Value) + " " + kind + " " + (tier ?? "-") + " " + stage.Key;

                        EvaluationResult<DocumentationResult?> none = DocumentationOperation.Evaluate(snapshot, context, new DocumentationRequest(kind, tier, stage.Key, Array.Empty<DocumentFact>()));
                        Assert.True(none.Payload != null, at + ": " + string.Join(" | ", none.Reasons));
                        Assert.Equal(expected, none.Payload!.Items.Select(i => i.RequirementKey).ToArray());
                        Assert.All(none.Payload.Items, i => Assert.Equal(DocumentStatusMeaning.Missing, i.Status));
                        Assert.Equal(expected.Length == 0 ? "complete" : "incomplete", none.Outcome);
                        Assert.Equal(expected.Length, none.Reasons.Count(r => r.Code == "documentation.missing"));

                        EvaluationResult<DocumentationResult?> all = DocumentationOperation.Evaluate(snapshot, context, new DocumentationRequest(kind, tier, stage.Key, fresh));
                        Assert.Equal("complete", all.Outcome);
                        Assert.Empty(all.Reasons);
                        evaluations += 2;
                    }
                }
            }
        }

        Assert.True(evaluations > 5000, "only " + evaluations + " evaluations");
    }

    [Fact]
    public void TheTypedAndTheWireDocumentationRequestGiveTheSameResult()
    {
        CompiledSnapshot compiled = OperationTestData.Snapshot();
        EvaluationContext context = OperationTestData.Context(compiled);
        var typed = new DocumentationRequest("power-platform-app", "tier-3", "operate", new[]
        {
            new DocumentFact("w1", "wiki-page", DocumentStatusMeaning.Current, lastCheckedOnUtc: Utc("2025-10-01T08:00:00Z")),
            new DocumentFact("r1", "runbook", DocumentStatusMeaning.Draft, requirementKey: "runbook-tier-3", lastUpdatedOnUtc: Utc("2026-11-01T08:00:00Z")),
        });
        JsonTreeNode wire = OperationTestData.Json("{\"applicationKindKey\":\"power-platform-app\",\"tierKey\":\"tier-3\",\"lifecycleStageKey\":\"operate\",\"links\":["
            + "{\"documentLinkId\":\"w1\",\"documentTypeKey\":\"wiki-page\",\"status\":\"current\",\"lastCheckedOnUtc\":\"2025-10-01T08:00:00Z\"},"
            + "{\"documentLinkId\":\"r1\",\"documentTypeKey\":\"runbook\",\"requirementKey\":\"runbook-tier-3\",\"status\":\"draft\",\"lastUpdatedOnUtc\":\"2026-11-01T08:00:00Z\"}]}");

        EvaluationResult<DocumentationResult?> a = DocumentationOperation.Evaluate(compiled.Snapshot, context, typed);
        EvaluationResult<DocumentationResult?> b = DocumentationOperation.Evaluate(compiled.Snapshot, context, wire);

        Assert.Equal("incomplete", a.Outcome);
        Assert.Equal(OperationTestData.Canonical(a.Payload!.ToJson()), OperationTestData.Canonical(b.Payload!.ToJson()));
        Assert.Equal(a.Reasons, b.Reasons);
        Assert.Equal(a.Trace, b.Trace);
        Assert.Equal(new[] { "documentation.draft", "documentation.missing", "documentation.missing", "documentation.stale" }, OperationTestData.Codes(a.Reasons).OrderBy(c => c, StringComparer.Ordinal).ToArray());
    }

    // A stage the logic must order has no stage rank: the engine refuses and reports it (document 25, rule 10).
    [Fact]
    public void AStageWithoutRankIsReportedNotGuessed()
    {
        const string Operations = "[{\"op\":\"upsert\",\"entity\":\"lifecycle-stage\",\"key\":\"limbo\",\"values\":{\"label\":\"Limbo\",\"stageNumber\":11,\"engineMeaning\":\"pre-live\"}},"
            + "{\"op\":\"upsert\",\"entity\":\"documentation-requirement\",\"key\":\"limbo-wiki\",\"values\":{\"label\":\"Limbo wiki\",\"documentTypeKey\":\"wiki-page\",\"fromStageKey\":\"limbo\"}}]";
        CompiledSnapshot compiled = OperationTestData.Snapshot("balanced", Operations);

        EvaluationResult<DocumentationResult?> result = DocumentationOperation.Evaluate(compiled.Snapshot, OperationTestData.Context(compiled), new DocumentationRequest("power-platform-app", "tier-1", "build", Array.Empty<DocumentFact>()));

        Assert.Equal(OperationOutcomes.Unavailable, result.Outcome);
        Reason reason = Assert.Single(result.Reasons);
        Assert.Equal("shape.schema", reason.Code);
        Assert.Equal("limbo", reason.Ref);
    }

    [Fact]
    public void AFreshnessBelowOneIsReportedNotGuessed()
    {
        CompiledSnapshot compiled = OperationTestData.Snapshot("balanced", "[{\"op\":\"upsert\",\"entity\":\"document-type\",\"key\":\"wiki-page\",\"values\":{\"freshnessDays\":0}}]");
        var request = new DocumentationRequest("power-platform-app", "tier-1", "build", new[] { new DocumentFact("w1", "wiki-page", DocumentStatusMeaning.Current, lastCheckedOnUtc: AsOf) });

        EvaluationResult<DocumentationResult?> result = DocumentationOperation.Evaluate(compiled.Snapshot, OperationTestData.Context(compiled), request);

        Assert.Equal(OperationOutcomes.Unavailable, result.Outcome);
        Assert.Equal(new[] { "value.range" }, OperationTestData.Codes(result.Reasons));
    }

    // ---- OP-07 ----

    // Over every shipped selection, gate, tier (and none) and kind: with every fact met the gate is ready; with none,
    // it is not ready exactly when a requirement applies. The applicable set is worked out here on its own.
    [Fact]
    public void EveryShippedGateIsReadyWithEverythingAndNotReadyWithNothing()
    {
        int evaluations = 0;
        foreach (KeyValuePair<string, string[]> selection in OperationTestData.ShippedSelections())
        {
            CompiledSnapshot compiled = OperationTestData.Snapshot(selection.Key, null, selection.Value);
            ConfigurationSnapshot snapshot = compiled.Snapshot;
            EvaluationContext context = OperationTestData.Context(compiled);
            string[] decisions = Rows(snapshot, "decision-type").Select(r => r.Key).ToArray();
            string[] reviews = Rows(snapshot, "review-type").Select(r => r.Key).ToArray();
            string[] owners = Rows(snapshot, "owner-role").Select(r => r.Key).ToArray();
            DocumentStatusFact[] current = Rows(snapshot, "document-type").Select(r => new DocumentStatusFact(r.Key, DocumentStatusMeaning.Current)).ToArray();
            List<string?> tiers = Active(snapshot, "tier").Select(t => (string?)t.Key).ToList();
            tiers.Add(null);
            foreach (string gate in Active(snapshot, "gate").Select(g => g.Key))
            {
                foreach (string? tier in tiers)
                {
                    foreach (string kind in Active(snapshot, "application-kind").Select(k => k.Key))
                    {
                        string[] expected = Active(snapshot, "gate-requirement")
                            .Where(r => Text(r, "gateKey") == gate && Scope(r, "tierKey", tier) && Scope(r, "applicationKindKey", kind))
                            .Select(r => r.Key)
                            .OrderBy(k => k, StringComparer.Ordinal)
                            .ToArray();
                        string at = selection.Key + "+" + string.Join("+", selection.Value) + " " + gate + " " + (tier ?? "-") + " " + kind;

                        var nothing = new ReadinessRequest(gate, tier, kind, documentation: Array.Empty<DocumentStatusFact>());
                        EvaluationResult<ReadinessResult?> no = GateReadinessOperation.Evaluate(snapshot, context, nothing);
                        Assert.True(no.Payload != null, at + ": " + string.Join(" | ", no.Reasons));
                        Assert.Equal(expected, no.Payload!.Requirements.Select(r => r.RequirementKey).ToArray());
                        Assert.All(no.Payload.Requirements, r => Assert.False(r.Met, at));
                        Assert.Equal(expected.Length == 0 ? "ready" : "not-ready", no.Outcome);
                        Assert.Equal(expected.Length > 0, no.Reasons.Any(r => r.Code == "gate.not-ready"));

                        var everything = new ReadinessRequest(gate, tier, kind, decisions, current, reviews, owners);
                        EvaluationResult<ReadinessResult?> yes = GateReadinessOperation.Evaluate(snapshot, context, everything);
                        Assert.Equal("ready", yes.Outcome);
                        Assert.Empty(yes.Reasons);
                        evaluations += 2;
                    }
                }
            }
        }

        Assert.True(evaluations > 1000, "only " + evaluations + " evaluations");
    }

    // V10: a gate requirement sets the one target its kind needs. One with two targets is refused at run time.
    [Fact]
    public void ARequirementWithTwoTargetsIsReportedNotGuessed()
    {
        CompiledSnapshot compiled = OperationTestData.Snapshot("balanced", "[{\"op\":\"upsert\",\"entity\":\"gate-requirement\",\"key\":\"go-live-acceptance\",\"values\":{\"requiredDocumentTypeKey\":\"runbook\"}}]");

        EvaluationResult<ReadinessResult?> result = GateReadinessOperation.Evaluate(compiled.Snapshot, OperationTestData.Context(compiled), new ReadinessRequest("go-live", "tier-1", "power-platform-app", new[] { "acceptance" }));

        Assert.Equal(OperationOutcomes.Unavailable, result.Outcome);
        Reason reason = Assert.Single(result.Reasons);
        Assert.Equal("ref.exactly-one", reason.Code);
        Assert.Equal("go-live-acceptance", reason.Ref);
    }

    // OP-06 feeds OP-07: the OP-06 payload passes through the wire form unchanged (document 25, OP-07 "fetched or computed").
    [Fact]
    public void DocumentationFeedsGateReadinessThroughTheWireForm()
    {
        CompiledSnapshot compiled = OperationTestData.Snapshot();
        EvaluationContext context = OperationTestData.Context(compiled);
        var links = new[]
        {
            new DocumentFact("r1", "runbook", DocumentStatusMeaning.Current, requirementKey: "runbook-tier-3", lastCheckedOnUtc: AsOf.AddDays(-10)),
            new DocumentFact("a1", "architecture-decision-record", DocumentStatusMeaning.Current),
        };
        EvaluationResult<DocumentationResult?> docs = DocumentationOperation.Evaluate(compiled.Snapshot, context, new DocumentationRequest("power-platform-app", "tier-3", "change-and-release", links));
        Assert.Equal("incomplete", docs.Outcome);

        JsonTreeNode input = OperationTestData.Json("{\"gateKey\":\"go-live\",\"tierKey\":\"tier-3\",\"applicationKindKey\":\"power-platform-app\",\"acceptedDecisionTypeKeys\":[\"acceptance\"],"
            + "\"doneReviewTypeKeys\":[\"security-review\"],\"documentation\":" + OperationTestData.Canonical(docs.Payload!.ToJson()) + "}");
        EvaluationResult<JsonTreeNode?> ready = OperationJson.Evaluate(EngineOperation.EvaluateGateReadiness, compiled.Snapshot, context, input);

        // The wiki page is missing, but the go-live gate asks only for the runbook, which is current.
        Assert.Equal("ready", ready.Outcome);
    }

    // ---- OP-10 ----

    // Generated graphs: every reached node sits at its shortest distance, found here by a separate breadth-first
    // search; every path is a real chain of counted edges; truncation and loops agree with separate checks; and the
    // order of the edges never changes the answer.
    [Fact]
    public void TheWalkMatchesAnIndependentSearch()
    {
        CompiledSnapshot compiled = OperationTestData.Snapshot();
        EvaluationContext context = OperationTestData.Context(compiled);
        string[] types = { "reads-data-from", "calls-api-of", "hosted-on", "replaces" };
        uint seed = 20261122;
        int cycles = 0;
        int truncations = 0;
        for (int graph = 0; graph < 150; graph++)
        {
            int apps = 3 + (int)(Next(ref seed) % 10);
            int edgeCount = (int)(Next(ref seed) % (uint)(apps * 2));
            var edges = new List<DependencyEdge>();
            for (int i = 0; i < edgeCount; i++)
            {
                string from = App((int)(Next(ref seed) % (uint)apps));
                string type = types[Next(ref seed) % (uint)types.Length];
                edges.Add(Next(ref seed) % 6 == 0
                    ? new DependencyEdge(from, null, "fixture-component-" + (Next(ref seed) % 3).ToString(CultureInfo.InvariantCulture), type)
                    : new DependencyEdge(from, App((int)(Next(ref seed) % (uint)apps)), null, type));
            }

            foreach (ImpactDirection direction in new[] { ImpactDirection.Dependants, ImpactDirection.Dependencies })
            {
                int maxDepth = 1 + (int)(Next(ref seed) % 4);
                var request = new ImpactRequest(App(0), null, direction, maxDepth, edges);
                EvaluationResult<ImpactResult?> result = ImpactOperation.Evaluate(compiled.Snapshot, context, request);
                Assert.Equal("walked", result.Outcome);
                ImpactResult walk = result.Payload!;

                List<KeyValuePair<string, string>> counted = edges.Where(e => e.DependencyTypeKey != "replaces")
                    .Select(e => direction == ImpactDirection.Dependants
                        ? new KeyValuePair<string, string>(Node(e, to: true), Node(e, to: false))
                        : new KeyValuePair<string, string>(Node(e, to: false), Node(e, to: true)))
                    .ToList();
                Dictionary<string, int> distance = Distances("a:" + App(0), counted);

                var expected = distance.Where(p => p.Value >= 1 && p.Value <= maxDepth).Select(p => p.Key + "@" + p.Value).OrderBy(s => s, StringComparer.Ordinal).ToArray();
                var actual = walk.Reached.Select(n => (n.ComponentId != null ? "c:" : "a:") + n.NodeId() + "@" + n.Depth).OrderBy(s => s, StringComparer.Ordinal).ToArray();
                Assert.Equal(expected, actual);
                Assert.Equal(distance.Values.Any(d => d == maxDepth + 1), walk.TruncatedAtDepth);

                foreach (ImpactNode node in walk.Reached)
                {
                    Assert.Equal(direction, node.Direction);
                    Assert.Equal(node.Depth + 1, node.Path.Count);
                    Assert.Equal(App(0), node.Path[0]);
                    for (int i = 1; i < node.Path.Count; i++)
                    {
                        string fromKey = "a:" + node.Path[i - 1];
                        bool last = i == node.Path.Count - 1;
                        string toKey = (last && node.ComponentId != null ? "c:" : "a:") + node.Path[i];
                        Assert.Contains(counted, p => p.Key == fromKey && p.Value == toKey);
                    }
                }

                var seen = new HashSet<string>(walk.Reached.Select(n => (n.ComponentId != null ? "c:" : "a:") + n.NodeId())) { "a:" + App(0) };
                bool loop = HasLoop(seen, counted.Where(p => seen.Contains(p.Key) && seen.Contains(p.Value)).ToList());
                Assert.Equal(loop, walk.CycleDetected);
                cycles += loop ? 1 : 0;
                truncations += walk.TruncatedAtDepth ? 1 : 0;

                var reversed = new ImpactRequest(App(0), null, direction, maxDepth, Enumerable.Reverse(edges));
                ImpactResult again = ImpactOperation.Evaluate(compiled.Snapshot, context, reversed).Payload!;
                Assert.Equal(OperationTestData.Canonical(walk.ToJson()), OperationTestData.Canonical(again.ToJson()));
            }
        }

        Assert.True(cycles > 0 && truncations > 0, "the generated graphs must hold loops (" + cycles + ") and truncated walks (" + truncations + ")");
    }

    [Fact]
    public void TheShippedSettingCapsTheDepth()
    {
        CompiledSnapshot compiled = OperationTestData.Snapshot();
        var chain = Enumerable.Range(1, 9).Select(i => new DependencyEdge(App(i), App(i - 1), null, "calls-api-of")).ToList();

        ImpactResult walk = ImpactOperation.Evaluate(compiled.Snapshot, OperationTestData.Context(compiled), new ImpactRequest(App(0), null, ImpactDirection.Dependants, 50, chain)).Payload!;

        Assert.Equal(5, walk.MaxDepthApplied);
        Assert.Equal(5, walk.Reached.Count);
        Assert.True(walk.TruncatedAtDepth);
    }

    // ---- OP-11 ----

    // Every shipped selection, review type and tier: a done review gets the done date plus its cadence's interval, or
    // says it has no cadence. A shipped preset never gives an unavailable answer.
    [Fact]
    public void EveryShippedReviewCadenceGivesTheNextDueDate()
    {
        DateTime done = Utc("2026-11-01T10:00:00Z");
        foreach (KeyValuePair<string, string[]> selection in OperationTestData.ShippedSelections())
        {
            CompiledSnapshot compiled = OperationTestData.Snapshot(selection.Key, null, selection.Value);
            ConfigurationSnapshot snapshot = compiled.Snapshot;
            foreach (string reviewType in Active(snapshot, "review-type").Select(r => r.Key))
            {
                foreach (string tier in Active(snapshot, "tier").Select(t => t.Key))
                {
                    SnapshotRow? cadence = Active(snapshot, "review-cadence").SingleOrDefault(c => Text(c, "reviewTypeKey") == reviewType && Text(c, "tierKey") == tier);
                    EvaluationResult<DueResult?> result = DueDatesOperation.Evaluate(snapshot, OperationTestData.Context(compiled), new DueRequest("review", null, done, reviewType, tier));

                    Assert.Equal("computed", result.Outcome);
                    if (cadence == null)
                    {
                        Assert.Null(result.Payload!.NextDueOnUtc);
                        Assert.Equal(new[] { "review.no-cadence" }, OperationTestData.Codes(result.Reasons));
                    }
                    else
                    {
                        Assert.Equal(done.AddDays(Int(cadence, "intervalDays")), result.Payload!.NextDueOnUtc);
                        Assert.Equal(cadence.Key, result.Payload.CadenceKey);
                        Assert.Empty(result.Reasons);
                    }

                    Assert.Empty(result.Payload!.Actions);
                }
            }
        }
    }

    // Day by day around the due date, for each subject kind: one first reminder, one escalation on its day, numbers
    // that rise by one per action, the deputy from the escalation on, and a next action date that is the next action
    // day of the simulation.
    [Theory]
    [InlineData("review")]
    [InlineData("document")]
    [InlineData("approval")]
    [InlineData("claim")]
    public void TheReminderScheduleRemindsOnceAndEscalatesOnce(string subjectKind)
    {
        CompiledSnapshot compiled = OperationTestData.Snapshot();
        SnapshotRow rule = Active(compiled.Snapshot, "reminder-rule").Single(r => Text(r, "subjectKind") == subjectKind);
        DateTime due = Utc("2026-12-15T10:00:00Z");
        DateTime escalationDay = due.Date.AddDays(Int(rule, "escalateAfterDays"));
        DateTime firstDay = due.Date.AddDays(-Int(rule, "leadDays"));

        var days = new List<KeyValuePair<DateTime, DueResult>>();
        for (DateTime day = due.Date.AddDays(-40); day <= due.Date.AddDays(70); day = day.AddDays(1))
        {
            EvaluationResult<DueResult?> result = DueDatesOperation.Evaluate(compiled.Snapshot, OperationTestData.Context(compiled, day.AddHours(9)), new DueRequest(subjectKind, due));
            Assert.Equal("computed", result.Outcome);
            days.Add(new KeyValuePair<DateTime, DueResult>(day, result.Payload!));
        }

        List<KeyValuePair<DateTime, ReminderStep>> actions = days.Where(d => d.Value.Actions.Count > 0).Select(d => new KeyValuePair<DateTime, ReminderStep>(d.Key, Assert.Single(d.Value.Actions))).ToList();
        Assert.Equal(firstDay, Assert.Single(actions, a => a.Value.Action == ReminderAction.Remind).Key);
        Assert.Equal(escalationDay, Assert.Single(actions, a => a.Value.Action == ReminderAction.Escalate).Key);
        Assert.Equal(Enumerable.Range(1, actions.Count), actions.Select(a => a.Value.ReminderNumber));
        Assert.All(actions, a => Assert.Equal(a.Key >= escalationDay, a.Value.Escalated));
        Assert.All(actions, a => Assert.Equal(rule.Key, a.Value.RuleKey));

        for (int i = 0; i < days.Count; i++)
        {
            DateTime? nextInSimulation = actions.Select(a => (DateTime?)a.Key).FirstOrDefault(d => d > days[i].Key);
            if (nextInSimulation.HasValue)
            {
                Assert.Equal(nextInSimulation, days[i].Value.NextActionOnUtc);
            }

            Assert.Equal(days[i].Key.AddHours(9) > due, days[i].Value.IsOverdue);
        }
    }

    [Fact]
    public void ANegativeLeadIsReportedNotGuessed()
    {
        CompiledSnapshot compiled = OperationTestData.Snapshot("balanced", "[{\"op\":\"upsert\",\"entity\":\"reminder-rule\",\"key\":\"document-reminder\",\"values\":{\"leadDays\":-1}}]");

        EvaluationResult<DueResult?> result = DueDatesOperation.Evaluate(compiled.Snapshot, OperationTestData.Context(compiled), new DueRequest("document", AsOf));

        Assert.Equal(OperationOutcomes.Unavailable, result.Outcome);
        Assert.Equal(new[] { "value.range" }, OperationTestData.Codes(result.Reasons));
    }

    // The engine checks subject kinds against the same four words as the configuration schema.
    [Fact]
    public void TheEngineKnowsTheSameSubjectKindsAsTheSchema()
    {
        JsonTreeNode schema = CanonicalJson.Parse(File.ReadAllText(Path.Combine(TestData.Root, "config", "schema", "config-bundle.schema.json")));
        Assert.True(schema.TryGetMember("$defs", out JsonTreeNode? defs));
        Assert.True(defs!.TryGetMember("contract.reminder-subject-kind", out JsonTreeNode? list));
        Assert.True(list!.TryGetMember("enum", out JsonTreeNode? values));

        Assert.Equal(values!.Items.Select(v => v.GetString()).OrderBy(v => v, StringComparer.Ordinal), EngineKeys.ReminderSubjectKinds.OrderBy(v => v, StringComparer.Ordinal));
    }

    [Fact]
    public void TheSameDueInputGivesTheSameAnswer()
    {
        CompiledSnapshot compiled = OperationTestData.Snapshot("regulated");
        JsonTreeNode input = OperationTestData.Json("{\"subjectKind\":\"review\",\"reviewTypeKey\":\"owner-check\",\"tierKey\":\"tier-3\",\"dueOnUtc\":\"2026-11-15T12:00:00Z\"}");

        EvaluationResult<JsonTreeNode?> a = OperationJson.Evaluate(EngineOperation.ComputeDueDates, compiled.Snapshot, OperationTestData.Context(compiled), input);
        EvaluationResult<JsonTreeNode?> b = OperationJson.Evaluate(EngineOperation.ComputeDueDates, compiled.Snapshot, OperationTestData.Context(compiled), input);

        Assert.Equal("computed", a.Outcome);
        Assert.Equal(OperationTestData.Canonical(a.Payload), OperationTestData.Canonical(b.Payload));
        Assert.Equal(a.Reasons, b.Reasons);
        Assert.Equal(a.Trace, b.Trace);
    }

    // ---- Helpers ----

    private static string App(int i)
    {
        return "fixture-app-" + i.ToString("00", CultureInfo.InvariantCulture);
    }

    // A small linear congruential generator: the same graphs on every runtime.
    private static uint Next(ref uint seed)
    {
        seed = unchecked(seed * 1664525u + 1013904223u);
        return seed >> 8;
    }

    private static string Node(DependencyEdge edge, bool to)
    {
        if (!to)
        {
            return "a:" + edge.FromId;
        }

        return edge.ToApplicationId != null ? "a:" + edge.ToApplicationId : "c:" + edge.ToComponentId;
    }

    private static Dictionary<string, int> Distances(string start, List<KeyValuePair<string, string>> edges)
    {
        var distance = new Dictionary<string, int> { { start, 0 } };
        var queue = new Queue<string>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            string node = queue.Dequeue();
            foreach (KeyValuePair<string, string> edge in edges.Where(e => e.Key == node))
            {
                if (!distance.ContainsKey(edge.Value))
                {
                    distance[edge.Value] = distance[node] + 1;
                    queue.Enqueue(edge.Value);
                }
            }
        }

        return distance;
    }

    // Kahn's algorithm: keep removing nodes nothing points at; whatever remains sits on a loop.
    private static bool HasLoop(HashSet<string> nodes, List<KeyValuePair<string, string>> edges)
    {
        var remaining = new HashSet<string>(nodes);
        bool removed = true;
        while (removed)
        {
            removed = false;
            foreach (string node in remaining.ToList())
            {
                if (!edges.Any(e => e.Value == node && remaining.Contains(e.Key)))
                {
                    remaining.Remove(node);
                    removed = true;
                }
            }
        }

        return remaining.Count > 0;
    }

    private static DateTime Utc(string text)
    {
        return DateTime.SpecifyKind(DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal), DateTimeKind.Utc);
    }

    private static IEnumerable<SnapshotRow> Rows(ConfigurationSnapshot snapshot, string entity)
    {
        return snapshot.TryGetEntity(entity, out SnapshotEntity? e) ? e.Rows : Enumerable.Empty<SnapshotRow>();
    }

    private static IEnumerable<SnapshotRow> Active(ConfigurationSnapshot snapshot, string entity)
    {
        return Rows(snapshot, entity).Where(r => r.IsActive);
    }

    private static string? Text(SnapshotRow row, string column)
    {
        return row.TryGetColumn(column, out JsonTreeNode? value) && value.Kind == JsonTreeKind.String ? value.GetString() : null;
    }

    private static int Int(SnapshotRow row, string column)
    {
        long value = 0;
        Assert.True(row.TryGetColumn(column, out JsonTreeNode? node) && node.TryGetInt64(out value), row.Key + " has no " + column);
        return (int)value;
    }

    private static bool Scope(SnapshotRow row, string column, string? value)
    {
        string? scope = Text(row, column);
        return scope == null || scope == value;
    }

    private static int StageRank(ConfigurationSnapshot snapshot, string stageKey)
    {
        Assert.True(snapshot.TryGetRow("lifecycle-stage", stageKey, out SnapshotRow? stage));
        return Int(stage!, "stageRank");
    }
}

// Test-only view of a reached node's ID.
internal static class ImpactNodeExtensions
{
    public static string NodeId(this ImpactNode node)
    {
        return node.ApplicationId ?? node.ComponentId!;
    }
}
