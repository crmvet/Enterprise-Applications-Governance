using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Product.Engine.Configuration.Activation;
using Product.Engine.Configuration.Composition;
using Product.Engine.Configuration.Model;
using Product.Engine.Configuration.Patches;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Operations;
using Product.Engine.Core.Snapshots;
using Product.Engine.Testing;
using Product.Engine.Testing.Invariants;
using Xunit;

namespace Product.Engine.Configuration.Tests;

// The OP-08 write guard (document 25, section 5.10), activation (section 6.4) and the bootstrap of version 1
// (document 21, section 4.8). The fixtures under fixtures/op-08, fixtures/activate and fixtures/bootstrap hold the cases;
// these tests add the invariants of document 25, section 10 over every shipped selection: AI rows start as draft or
// proposed, no write outside activation changes a configuration row, outcomes come only from the approval writer, an
// approval counts only from a person, every activation write passes the guard only inside activation, and version 1
// comes from the bootstrap for every selection.
public sealed class WriteGuardTests
{
    private static readonly DateTime AsOf = OperationTestData.AsOf;

    private static readonly string[] Positive = { "accepted", "registered", "approved", "completed" };

    // ---- Fixtures ----

    [Theory]
    [InlineData("activate")]
    [InlineData("bootstrap")]
    public void ActivationAndBootstrapHavePassingAndRefusingFixtures(string folder)
    {
        List<FixtureResult> results = FixtureRunner.ForSourceRoot(TestData.Root).RunFolder(Path.Combine(TestData.Root, "fixtures", folder)).ToList();

        Assert.All(results, r => Assert.True(r.Status == FixtureStatus.Passed, r.File + ": " + string.Join(" | ", r.Problems)));
        Assert.Contains(results, r => r.ActualOutcome == "activated" || r.ActualOutcome == "bootstrapped");
        Assert.Contains(results, r => r.ActualOutcome == "refused");
    }

    // Every guard rule G-01 to G-17 has a fixture that covers it, and every rule but G-09 a refused one that reports it.
    // G-09 replaces caller values with an info reason; its refusals use the proposed engine.effect-only code.
    [Fact]
    public void EveryGuardRuleHasFixtures()
    {
        List<FixtureResult> results = FixtureRunner.ForSourceRoot(TestData.Root).RunFolder(Path.Combine(TestData.Root, "fixtures", "op-08")).ToList();
        for (int i = 1; i <= 17; i++)
        {
            string rule = "G-" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
            Assert.Contains(results, r => r.CoveredRuleIds.Contains(rule));
            if (rule != "G-09")
            {
                Assert.Contains(results, r => r.ActualOutcome == "refused" && r.CoveredRuleIds.Contains(rule) && r.ActualRuleIds.Contains(rule));
            }
        }
    }

    // ---- Invariants over every shipped selection ----

    // G-08: whatever status an AI client names, a row it creates starts as draft or proposed and is marked AI created.
    // An AI update never reaches accepted, registered, approved or completed.
    [Fact]
    [Invariant("INV-02")]
    public void AiCreatedRowsStartAsDraftOrProposedInEveryShippedSelection()
    {
        var entities = new Dictionary<string, string> { { "decision", "proposed" }, { "business-application", "draft" }, { "intake-request", "draft" }, { "change-request", "draft" }, { "document-link", "draft" } };
        int checkedRows = 0;
        foreach (KeyValuePair<string, string[]> selection in OperationTestData.ShippedSelections())
        {
            CompiledSnapshot snapshot = OperationTestData.Snapshot(selection.Key, null, selection.Value);
            string documentKind = snapshot.Snapshot.TryGetEntity("link-kind", out SnapshotEntity? kinds)
                ? kinds.Rows.First(r => r.IsActive && Text(r, "linkCategory") == "document").KeyPath
                : throw new InvalidOperationException("No link kinds.");
            foreach (KeyValuePair<string, string> entity in entities)
            {
                foreach (SnapshotRow status in Statuses(snapshot, entity.Key))
                {
                    string extra = entity.Key == "document-link" ? ",\"linkKind\":\"" + documentKind + "\",\"url\":\"https://example.org/doc/1\"" : string.Empty;
                    EvaluationResult<WritePlan?> created = Guard(snapshot, AiClient("requester"), entity.Key, WriteOperation.Create, null, "{\"status\":\"" + status.KeyPath + "\"" + extra + "}");
                    Assert.True(created.Outcome == "allowed", selection.Key + " " + entity.Key + " " + status.KeyPath + ": " + string.Join(" | ", created.Reasons));
                    Assert.Equal(entity.Value, Member(created.Payload!.SetColumns, "statusMeaning"));
                    Assert.Equal("true", Member(created.Payload.SetColumns, "isAiCreated"));

                    if (Array.IndexOf(Positive, status.EngineMeaning) >= 0)
                    {
                        EvaluationResult<WritePlan?> updated = Guard(snapshot, AiClient("requester"), entity.Key, WriteOperation.Update, "{\"statusMeaning\":\"" + entity.Value + "\"}", "{\"status\":\"" + status.KeyPath + "\"" + extra + "}");
                        Assert.Equal("refused", updated.Outcome);
                        Assert.Contains(updated.Reasons, r => r.Code == "ai.status-not-allowed");
                    }

                    checkedRows++;
                }
            }
        }

        Assert.True(checkedRows > 100, "only " + checkedRows + " statuses checked");
    }

    // RA-03 (WORKING DEFAULT (open design question 9)): over every shipped selection, every
    // document type and every status with the meaning current, an AI client's update to current is refused exactly when
    // the type has Current Needs Person, and a person's update is allowed.
    [Fact]
    public void AnAiClientNeverMarksAGateRelevantDocumentCurrentInEveryShippedSelection()
    {
        int refusedCount = 0;
        int allowedCount = 0;
        foreach (KeyValuePair<string, string[]> selection in OperationTestData.ShippedSelections())
        {
            CompiledSnapshot snapshot = OperationTestData.Snapshot(selection.Key, null, selection.Value);
            SnapshotRow[] types = snapshot.Snapshot.TryGetEntity("document-type", out SnapshotEntity? found) ? found.Rows.Where(r => r.IsActive).ToArray() : Array.Empty<SnapshotRow>();
            Assert.NotEmpty(types);
            foreach (SnapshotRow type in types)
            {
                bool needsPerson = type.TryGetColumn("currentNeedsPerson", out JsonTreeNode? flag) && flag.Kind == JsonTreeKind.True;
                foreach (SnapshotRow status in Statuses(snapshot, "document-link").Where(s => s.EngineMeaning == "current"))
                {
                    string before = "{\"statusMeaning\":\"draft\",\"documentType\":\"" + type.KeyPath + "\"}";
                    string after = "{\"status\":\"" + status.KeyPath + "\"}";
                    string label = selection.Key + " " + type.KeyPath + " " + status.KeyPath;
                    EvaluationResult<WritePlan?> byAi = Guard(snapshot, AiClient("application-owner"), "document-link", WriteOperation.Update, before, after);
                    if (needsPerson)
                    {
                        Assert.True(byAi.Outcome == "refused", label + " by an AI client: " + byAi.Outcome);
                        Assert.Contains(byAi.Reasons, r => r.Code == "ai.status-not-allowed");
                        refusedCount++;
                    }
                    else
                    {
                        Assert.True(byAi.Outcome == "allowed", label + " by an AI client: " + string.Join(" | ", byAi.Reasons));
                        allowedCount++;
                    }

                    Assert.True(Guard(snapshot, Person("application-owner"), "document-link", WriteOperation.Update, before, after).Outcome == "allowed", label + " by a person");
                }
            }
        }

        Assert.True(refusedCount > 5 && allowedCount > 5, refusedCount + " refused, " + allowedCount + " allowed");
    }

    // G-01: no person, role, tool or engine effect other than activation writes a configuration row; activation writes
    // but never deletes one (G-04). Over every shipped selection.
    [Fact]
    [Invariant("INV-10")]
    public void NoWriteOutsideActivationChangesAConfigurationRow()
    {
        string[] entities = ConfigurationEntities.All.Select(e => e.Key).Concat(new[] { "configuration-set", "preset" }).ToArray();
        var actors = new List<ActorInfo>
        {
            Person("configuration-admin"),
            Person(),
            AiClient("configuration-admin"),
            new("fixture-pipeline-1", PrincipalKind.ServicePrincipal, ActorKindMeaning.Pipeline, new[] { "deployment-recorder" }),
            Runner(),
            new("fixture-runner-1", PrincipalKind.ServicePrincipal, ActorKindMeaning.ApprovalRunner, new[] { "approval-writer" }, isEngineNested: true),
        };
        EffectKind[] otherEffects = { EffectKind.UpdateSubject, EffectKind.SupersedeDecision, EffectKind.AcceptDecision, EffectKind.RecomputeApprovals, EffectKind.WriteAuditEntry };

        int checkedWrites = 0;
        foreach (KeyValuePair<string, string[]> selection in OperationTestData.ShippedSelections())
        {
            string preset = selection.Key + string.Concat(selection.Value.Select(o => "+" + o));
            CompiledSnapshot snapshot = OperationTestData.Snapshot(selection.Key, null, selection.Value);
            foreach (string entity in entities)
            {
                foreach (WriteOperation op in new[] { WriteOperation.Create, WriteOperation.Update, WriteOperation.Delete })
                {
                    foreach (ActorInfo actor in actors)
                    {
                        AssertRefusedOutsideActivation(snapshot, actor, entity, op, null);
                    }

                    foreach (EffectKind effect in otherEffects)
                    {
                        AssertRefusedOutsideActivation(snapshot, Nested(), entity, op, effect);
                    }

                    // Inside activation: written, never deleted.
                    EvaluationResult<WritePlan?> inside = Guard(snapshot, Nested(), entity, op, op == WriteOperation.Create ? null : "{\"key\":\"some-row\",\"origin\":\"shipped\"}", op == WriteOperation.Delete ? null : "{\"label\":\"A label\"}", EffectKind.ActivateConfiguration);
                    if (op == WriteOperation.Delete)
                    {
                        Assert.Equal("refused", inside.Outcome);
                        Assert.Contains(inside.Reasons, r => r.Code == "config.in-use");
                    }
                    else
                    {
                        Assert.True(inside.Outcome == "allowed", preset + " " + entity + " " + op + ": " + string.Join(" | ", inside.Reasons));
                    }

                    checkedWrites += actors.Count + otherEffects.Length + 1;
                }
            }
        }

        Assert.True(checkedWrites > 22 * 1000, "only " + checkedWrites + " writes checked");
    }

    // G-06: an approval outcome comes only from the approval runner, a workload principal holding the approval writer
    // role, in its own name. Every other combination of actor kind, principal kind, role and marker is refused.
    [Fact]
    public void ApprovalOutcomesComeOnlyFromTheApprovalWriter()
    {
        foreach (string preset in new[] { "starter", "balanced", "regulated", "microsoft-coe-baseline" })
        {
            CompiledSnapshot snapshot = OperationTestData.Snapshot(preset);
            int allowed = 0;
            foreach (ActorKindMeaning actorKind in EngineContract.ActorKindMeanings.Values)
            {
                foreach (PrincipalKind principalKind in EngineContract.PrincipalKinds.Values)
                {
                    foreach (bool holdsRole in new[] { true, false })
                    {
                        foreach (bool nested in new[] { true, false })
                        {
                            var actor = new ActorInfo("fixture-caller-1", principalKind, actorKind, holdsRole ? new[] { "approval-writer" } : null, "fixture-person-2", nested);
                            EvaluationResult<WritePlan?> result = Guard(snapshot, actor, "approval-record", WriteOperation.Update, Pending, Answer("person", "user"), nested ? EffectKind.RecomputeApprovals : null);
                            bool writer = actorKind == ActorKindMeaning.ApprovalRunner && holdsRole && !nested
                                && (principalKind == PrincipalKind.ServicePrincipal || principalKind == PrincipalKind.ManagedIdentity);
                            if (writer)
                            {
                                Assert.Equal("allowed", result.Outcome);
                                allowed++;
                            }
                            else
                            {
                                Assert.Equal("refused", result.Outcome);
                            }
                        }
                    }
                }
            }

            Assert.Equal(2, allowed);
        }
    }

    // G-06 and the model rule of O10: the engine never writes a responder column. Over every shipped selection, every
    // responder column, alone or with every outcome the selection has (or none), on a create or an update, in every
    // effect or in none, an engine-nested write is refused with approval.outcome-writer. A create without an outcome is
    // invalid input, because Outcome is required.
    [Fact]
    public void TheEngineNeverWritesAResponderColumn()
    {
        string[] responder = { "approver", "approverActorKind", "approverActorMeaning", "approverPrincipalKind", "typedReason", "respondedOn", "channel", "flowRunUrl", "approverHoldsRequiredRole" };
        int checkedWrites = 0;
        foreach (KeyValuePair<string, string[]> selection in OperationTestData.ShippedSelections())
        {
            CompiledSnapshot snapshot = OperationTestData.Snapshot(selection.Key, null, selection.Value);
            string?[] outcomes = Statuses(snapshot, "approval-record").Select(s => (string?)s.KeyPath).Concat(new string?[] { null }).ToArray();
            foreach (EffectKind? effect in NestedEffects())
            {
                foreach (WriteOperation op in new[] { WriteOperation.Create, WriteOperation.Update })
                {
                    foreach (string column in responder)
                    {
                        foreach (string? outcome in outcomes)
                        {
                            string after = "{\"" + column + "\":\"fixture-value\"" + (outcome == null ? string.Empty : ",\"outcome\":\"" + outcome + "\"") + "}";
                            EvaluationResult<WritePlan?> result = Guard(snapshot, Nested(), "approval-record", op, op == WriteOperation.Create ? null : Pending, after, effect);
                            string label = selection.Key + " " + op + " " + column + " " + (outcome ?? "no outcome") + " " + (effect?.ToString() ?? "no effect");
                            if (op == WriteOperation.Create && outcome == null)
                            {
                                Assert.True(result.Outcome == "invalid-input", label + ": " + result.Outcome);
                                Assert.Contains(result.Reasons, r => r.Code == "input.required");
                            }
                            else
                            {
                                Assert.True(result.Outcome == "refused", label + ": " + result.Outcome);
                                Assert.Contains(result.Reasons, r => r.Code == "approval.outcome-writer");
                            }

                            checkedWrites++;
                        }
                    }
                }
            }
        }

        Assert.True(checkedWrites > 10000, "only " + checkedWrites + " writes checked");
    }

    // G-06 and the model rule of O10: the engine writes the request and closes it, nothing else. Over every shipped
    // selection and every effect (or none), an engine-nested create is allowed only with an outcome of meaning pending;
    // an update of a pending record only to expired or cancelled; and on a final record the same outcome again is a no-op
    // while any other is refused (G-06 "only once", class F).
    [Fact]
    public void TheEngineCreatesApprovalRecordsPendingAndClosesThemOnlyOnce()
    {
        string[] engineCloses = { "expired", "cancelled" };
        int checkedWrites = 0;
        foreach (KeyValuePair<string, string[]> selection in OperationTestData.ShippedSelections())
        {
            CompiledSnapshot snapshot = OperationTestData.Snapshot(selection.Key, null, selection.Value);
            SnapshotRow[] statuses = Statuses(snapshot, "approval-record").ToArray();
            Assert.Contains(statuses, s => s.EngineMeaning == "pending");
            foreach (EffectKind? effect in NestedEffects())
            {
                foreach (SnapshotRow status in statuses)
                {
                    string label = selection.Key + " " + status.KeyPath + " " + (effect?.ToString() ?? "no effect");
                    string after = "{\"decision\":\"fixture-decision-1\",\"outcome\":\"" + status.KeyPath + "\",\"requestedOn\":\"2026-11-22T09:00:00Z\",\"expiresOn\":\"2026-12-02T09:00:00Z\"}";
                    EvaluationResult<WritePlan?> created = Guard(snapshot, Nested(), "approval-record", WriteOperation.Create, null, after, effect);
                    if (status.EngineMeaning == "pending")
                    {
                        Assert.True(created.Outcome == "allowed", label + " create: " + string.Join(" | ", created.Reasons));
                        Assert.Equal("pending", Member(created.Payload!.SetColumns, "outcomeMeaning"));
                        Assert.Equal("engine", Member(created.Payload.SetColumns, "performedByActorKind"));
                    }
                    else
                    {
                        Assert.True(created.Outcome == "refused", label + " create: " + created.Outcome);
                        Assert.Contains(created.Reasons, r => r.Code == "approval.outcome-writer");
                    }

                    string close = "{\"outcome\":\"" + status.KeyPath + "\"}";
                    EvaluationResult<WritePlan?> fromPending = Guard(snapshot, Nested(), "approval-record", WriteOperation.Update, Pending, close, effect);
                    bool closes = Array.IndexOf(engineCloses, status.EngineMeaning) >= 0;
                    Assert.True(fromPending.Outcome == (closes ? "allowed" : "refused"), label + " from pending: " + fromPending.Outcome);
                    if (closes)
                    {
                        Assert.False(fromPending.Payload!.IsNoOp);
                        Assert.Equal(status.EngineMeaning, Member(fromPending.Payload.SetColumns, "outcomeMeaning"));
                    }

                    foreach (SnapshotRow final in statuses.Where(s => s.EngineMeaning != "pending"))
                    {
                        string before = "{\"outcomeMeaning\":\"" + final.EngineMeaning + "\",\"outcome\":\"" + final.KeyPath + "\",\"decision\":\"fixture-decision-1\"}";
                        EvaluationResult<WritePlan?> again = Guard(snapshot, Nested(), "approval-record", WriteOperation.Update, before, close, effect);
                        string againLabel = label + " on " + final.KeyPath;
                        if (closes && final.KeyPath == status.KeyPath)
                        {
                            Assert.True(again.Outcome == "allowed", againLabel + ": " + string.Join(" | ", again.Reasons));
                            Assert.True(again.Payload!.IsNoOp, againLabel + " is not a no-op");
                            Assert.Empty(again.Payload.Effects);
                        }
                        else
                        {
                            Assert.True(again.Outcome == "refused", againLabel + ": " + again.Outcome);
                            Assert.Contains(again.Reasons, r => r.Code == "approval.outcome-writer");
                        }

                        checkedWrites++;
                    }

                    checkedWrites += 2;
                }
            }
        }

        Assert.True(checkedWrites > 3000, "only " + checkedWrites + " writes checked");
    }

    // The approver kind rule (document 22, section 2.4, rule 4): an approved or rejected answer counts only from a person
    // signed in as a user. Every other answer is stored as recommendation only, in every shipped selection.
    [Fact]
    [Invariant("INV-01")]
    public void AnApprovalCountsOnlyFromAPersonInEveryShippedSelection()
    {
        foreach (KeyValuePair<string, string[]> selection in OperationTestData.ShippedSelections())
        {
            CompiledSnapshot snapshot = OperationTestData.Snapshot(selection.Key, null, selection.Value);
            Assert.True(snapshot.Snapshot.TryGetEntity("actor-kind", out SnapshotEntity? actorKinds));
            foreach (SnapshotRow actorKind in actorKinds!.Rows.Where(r => r.IsActive))
            {
                foreach (string principalKind in EngineContract.PrincipalKinds.WireValues)
                {
                    foreach (string outcome in new[] { "approval-approved", "approval-rejected" })
                    {
                        EvaluationResult<WritePlan?> result = Guard(snapshot, Runner(), "approval-record", WriteOperation.Update, Pending, Answer(actorKind.KeyPath, principalKind, outcome));
                        Assert.Equal("allowed", result.Outcome);
                        bool person = actorKind.EngineMeaning == "person" && principalKind == "user";
                        string meaning = Member(result.Payload!.SetColumns, "outcomeMeaning")!;
                        if (person)
                        {
                            Assert.Equal(outcome == "approval-approved" ? "approved" : "rejected", meaning);
                            Assert.Null(Member(result.Payload.SetColumns, "countsTowardRequirement"));
                        }
                        else
                        {
                            Assert.Equal("recommendation-only", meaning);
                            Assert.Equal("false", Member(result.Payload.SetColumns, "countsTowardRequirement"));
                        }
                    }
                }
            }
        }
    }

    // The approver kind rule reads the responder's Principal Reference (document 22, O10: Approver Principal Kind is copied
    // from it). A reference that is not a user stores its kind and the answer as recommendation only; a user reference
    // never upgrades another kind from the runner; user and user counts; with no reference the runner's kind stands.
    [Theory]
    [InlineData("user", "agent-user", "recommendation-only", "agent-user")]
    [InlineData("agent-user", "user", "recommendation-only", null)]
    [InlineData("user", "user", "approved", null)]
    [InlineData("user", null, "approved", null)]
    public void TheApproverKindComesFromThePrincipalReference(string input, string? reference, string meaning, string? copiedKind)
    {
        CompiledSnapshot snapshot = OperationTestData.Snapshot();
        PrincipalReferenceFact? fact = reference == null ? null : new PrincipalReferenceFact(EngineContract.PrincipalKinds.Parse(reference));
        var request = new WriteRequest("approval-record", WriteOperation.Update, "fixture-row-1", CanonicalJson.Parse(Answer("person", input)), CanonicalJson.Parse(Pending), approverPrincipalReference: fact);
        EvaluationResult<WritePlan?> result = WriteGuardOperation.Evaluate(snapshot.Snapshot, Context(snapshot, Runner()), request);

        Assert.Equal("allowed", result.Outcome);
        JsonTreeNode set = result.Payload!.SetColumns;
        Assert.Equal(meaning, Member(set, "outcomeMeaning"));
        Assert.Equal(copiedKind, Member(set, "approverPrincipalKind"));
        if (meaning == "recommendation-only")
        {
            Assert.Equal("false", Member(set, "countsTowardRequirement"));
            Reason warning = Assert.Single(result.Reasons);
            Assert.Equal("approval.non-user-principal", warning.Code);
            Assert.Equal(ReasonSeverity.Warning, warning.Severity);
            Assert.Equal(copiedKind ?? input, warning.Ref);
        }
        else
        {
            Assert.Null(Member(set, "countsTowardRequirement"));
            Assert.Empty(result.Reasons);
        }

        // The same request through the wire path gives the same plan.
        string wire = "{\"entity\":\"approval-record\",\"operation\":\"update\",\"rowId\":\"fixture-row-1\",\"after\":" + Answer("person", input) + ",\"before\":" + Pending
            + (reference == null ? string.Empty : ",\"approverPrincipalReference\":{\"principalKind\":\"" + reference + "\",\"isErased\":false}") + "}";
        EvaluationResult<JsonTreeNode?> viaWire = OperationJson.Evaluate(EngineOperation.GuardWrite, snapshot.Snapshot, Context(snapshot, Runner()), CanonicalJson.Parse(wire));
        Assert.Equal(OperationTestData.Canonical(result.Payload.ToJson()), OperationTestData.Canonical(viaWire.Payload));

        // Without the fact the guard behaves as it did before the fact existed: it judges the runner's kind alone.
        var without = new WriteRequest("approval-record", WriteOperation.Update, "fixture-row-1", CanonicalJson.Parse(Answer("person", input)), CanonicalJson.Parse(Pending));
        EvaluationResult<WritePlan?> plain = WriteGuardOperation.Evaluate(snapshot.Snapshot, Context(snapshot, Runner()), without);
        Assert.Equal(input == "user" ? "approved" : "recommendation-only", Member(plain.Payload!.SetColumns, "outcomeMeaning"));
        Assert.Null(Member(plain.Payload.SetColumns, "approverPrincipalKind"));
    }

    // The fact's wire form is checked like every other input: an unknown principal kind or member is invalid input.
    [Fact]
    public void AMalformedPrincipalReferenceFactIsInvalidInput()
    {
        CompiledSnapshot snapshot = OperationTestData.Snapshot();
        foreach (string fact in new[] { "{\"principalKind\":\"robot\"}", "{\"principalKind\":\"user\",\"label\":\"x\"}", "{\"isErased\":true}" })
        {
            string wire = "{\"entity\":\"approval-record\",\"operation\":\"update\",\"rowId\":\"fixture-row-1\",\"after\":" + Answer("person", "user") + ",\"before\":" + Pending + ",\"approverPrincipalReference\":" + fact + "}";
            EvaluationResult<JsonTreeNode?> result = OperationJson.Evaluate(EngineOperation.GuardWrite, snapshot.Snapshot, Context(snapshot, Runner()), CanonicalJson.Parse(wire));
            Assert.True(result.Outcome == "invalid-input", fact + ": " + result.Outcome);
        }
    }

    // Every write activation plans passes the guard as an engine-nested write of its own effect kind, and is refused
    // without the marker: configuration writes happen only inside activation.
    [Fact]
    public void EveryActivationWritePassesTheGuardOnlyInsideActivation()
    {
        string draft = "{\"schemaVersion\":\"1.0\",\"manifest\":{\"kind\":\"patch\",\"baseVersionNumber\":1},\"operations\":["
            + "{\"op\":\"upsert\",\"entity\":\"tier\",\"key\":\"tier-3\",\"values\":{\"label\":\"Tier 3: Critical\"}},"
            + "{\"op\":\"retire\",\"entity\":\"vocabulary-mapping\",\"key\":\"" + FirstVocabularyMapping() + "\"}]}";
        ActivationOutcome outcome = Activate("balanced", draft, AsOf);
        Assert.True(outcome.Outcome == ActivationOutcome.Activated, string.Join(" | ", outcome.Reasons));
        Assert.Equal(2, outcome.VersionNumber);
        Assert.Equal(2, outcome.RowWrites.Count);

        CompiledSnapshot active = OperationTestData.Snapshot("balanced");
        foreach (PlannedEffect effect in outcome.Effects)
        {
            string entity = effect.Entity.ToLowerInvariant().Replace(' ', '-');
            string? before = null;
            if (effect.Operation == WriteOperation.Update)
            {
                before = entity == "configuration-draft" ? "{\"draftStatus\":\"submitted\"}" : "{\"key\":\"" + (effect.Key ?? "some-row") + "\",\"origin\":\"shipped\"}";
            }

            string after = CanonicalJson.Write(effect.Columns, CanonicalProfile.Snapshot);
            EvaluationResult<WritePlan?> inside = Guard(active, Nested(), entity, effect.Operation, before, after, effect.Kind, effect.RowId ?? "fixture-new-row", setKind: true);
            Assert.True(inside.Outcome == "allowed", effect.Entity + " " + effect.Kind + ": " + string.Join(" | ", inside.Reasons));

            EvaluationResult<WritePlan?> outside = Guard(active, Person("configuration-admin"), entity, effect.Operation, before, after, null, effect.RowId ?? "fixture-new-row", setKind: true);
            Assert.Equal("refused", outside.Outcome);
        }
    }

    // Activation is deterministic and replay-safe (document 25, sections 8 and 9).
    [Fact]
    public void ActivationIsDeterministicAndReplaySafe()
    {
        string draft = "{\"schemaVersion\":\"1.0\",\"manifest\":{\"kind\":\"patch\",\"baseVersionNumber\":1},\"operations\":[{\"op\":\"upsert\",\"entity\":\"tier\",\"key\":\"tier-3\",\"values\":{\"label\":\"Tier 3: Critical\"}}]}";
        ActivationOutcome first = Activate("balanced", draft, AsOf);
        ActivationOutcome second = Activate("balanced", draft, AsOf);
        Assert.Equal(first.Snapshot!.ContentHash, second.Snapshot!.ContentHash);
        Assert.Equal(OperationTestData.Canonical(first.ToJson()), OperationTestData.Canonical(second.ToJson()));

        ActivationOutcome replay = Activate("balanced", draft, AsOf, DraftStatusMeaning.Activated, resulting: 2);
        Assert.Equal(ActivationOutcome.AlreadyActivated, replay.Outcome);
        Assert.Equal(2, replay.VersionNumber);
        Assert.Empty(replay.Effects);
    }

    // The bootstrap of version 1 for every shipped selection: version 1's hash is the hash of the selection compiled as a
    // working set, and the accepted decision it records passes the guard under the bootstrap basis, only while no
    // version exists.
    [Fact]
    public void BootstrapCreatesVersionOneForEveryShippedSelection()
    {
        var activator = new ConfigurationActivator(OperationTestData.Shipped);
        foreach (KeyValuePair<string, string[]> selection in OperationTestData.ShippedSelections())
        {
            ActivationOutcome outcome = activator.Bootstrap(Context(null, Person()), new BootstrapRequest(selection.Key, selection.Value, noVersionExists: true, hasInstallRights: true));
            Assert.True(outcome.Outcome == ActivationOutcome.Bootstrapped, selection.Key + "+" + string.Join("+", selection.Value) + ": " + string.Join(" | ", outcome.Reasons));
            Assert.Equal(1, outcome.VersionNumber);
            CompiledSnapshot compiled = OperationTestData.Snapshot(selection.Key, null, selection.Value);
            Assert.Equal(compiled.ContentHash, outcome.Snapshot!.ContentHash);
            Assert.Equal(compiled.Snapshot.Entities.Sum(e => e.Rows.Count), outcome.RowWrites.Count);

            PlannedEffect decision = outcome.Effects.Single(e => e.Entity == "Decision");
            string after = CanonicalJson.Write(decision.Columns, CanonicalProfile.Snapshot);
            var bootstrapBasis = new WriteAcceptance(WriteAcceptance.Bootstrap, noVersionExists: true);
            var request = new WriteRequest("decision", WriteOperation.Create, null, CanonicalJson.Parse(after), null, EffectKind.ActivateConfiguration, acceptance: bootstrapBasis);
            EvaluationResult<WritePlan?> accepted = WriteGuardOperation.Evaluate(outcome.Snapshot.Snapshot, Context(outcome.Snapshot, NestedPerson()), request);
            Assert.True(accepted.Outcome == "allowed", string.Join(" | ", accepted.Reasons));

            var lateBasis = new WriteAcceptance(WriteAcceptance.Bootstrap, noVersionExists: false);
            var late = new WriteRequest("decision", WriteOperation.Create, null, CanonicalJson.Parse(after), null, EffectKind.ActivateConfiguration, acceptance: lateBasis);
            Assert.Equal("refused", WriteGuardOperation.Evaluate(outcome.Snapshot.Snapshot, Context(outcome.Snapshot, NestedPerson()), late).Outcome);
        }
    }

    // Erasure clears the label and keeps the evidence: the audit entry holds IDs, never the label (document 22,
    // section 2.7, item 4).
    [Fact]
    public void ErasureAuditHoldsIdsNeverLabels()
    {
        CompiledSnapshot snapshot = OperationTestData.Snapshot();
        EvaluationResult<WritePlan?> result = Guard(snapshot, Person("platform-admin"), "principal-reference", WriteOperation.Update, "{\"isErased\":false,\"displayLabel\":\"Fixture Person Seven\"}", "{\"isErased\":true}");

        Assert.Equal("allowed", result.Outcome);
        Assert.Equal(EngineKeys.ErasedDisplayLabel, Member(result.Payload!.SetColumns, "displayLabel"));
        Assert.DoesNotContain("Fixture Person Seven", OperationTestData.Canonical(result.Payload.ToJson()));
        PlannedEffect audit = Assert.Single(result.Payload.Effects);
        Assert.Equal(EffectKind.WriteAuditEntry, audit.Kind);
    }

    // The typed API and the wire path give the same answer.
    [Fact]
    public void TypedRequestAndWireAgree()
    {
        CompiledSnapshot snapshot = OperationTestData.Snapshot();
        string wire = "{\"entity\":\"decision\",\"operation\":\"create\",\"after\":{\"decisionType\":\"design\",\"status\":\"decision-accepted\"}}";
        EvaluationContext context = Context(snapshot, AiClient("architect"));
        EvaluationResult<JsonTreeNode?> viaWire = OperationJson.Evaluate(EngineOperation.GuardWrite, snapshot.Snapshot, context, CanonicalJson.Parse(wire));
        EvaluationResult<WritePlan?> typed = WriteGuardOperation.Evaluate(snapshot.Snapshot, context, new WriteRequest("decision", WriteOperation.Create, after: CanonicalJson.Parse("{\"decisionType\":\"design\",\"status\":\"decision-accepted\"}")));

        Assert.Equal(viaWire.Outcome, typed.Outcome);
        Assert.Equal(OperationTestData.Canonical(viaWire.Payload), OperationTestData.Canonical(typed.Payload!.ToJson()));
        Assert.Equal(OperationTestData.Codes(viaWire.Reasons), OperationTestData.Codes(typed.Reasons));
    }

    // ---- History classes and columns from the logical model ----
    //
    // The oracle is model/logical-model.json itself, read here from the file and turned into column names by this test's
    // own conversion, so the guard's compiled-in copy is checked against the source, not against itself.

    // Every column a write names is a column of its entity in the logical model, in its exact lowerCamelCase form. Each
    // model column passes the column check, for a create and an update, from a person and from the engine; the same name
    // with a capital first letter, and a name the model does not hold, are invalid input (input.unknown-column) before
    // any rule runs. Live configuration rows (the bundle's field names, written only inside activation) are not checked.
    [Fact]
    public void EveryWriteNamesOnlyColumnsOfTheLogicalModel()
    {
        CompiledSnapshot snapshot = OperationTestData.Snapshot();
        int checkedWrites = 0;
        foreach (ModelEntityFacts entity in ModelEntities.Value)
        {
            EvaluationResult<WritePlan?> probe = Guard(snapshot, Person(), entity.Key, WriteOperation.Update, "{}", "{\"description\":\"x\"}");
            if (entity.Id.StartsWith("L", StringComparison.Ordinal))
            {
                Assert.Contains(probe.Reasons, r => r.Code == "input.unknown-key");
                continue;
            }

            Assert.DoesNotContain(probe.Reasons, r => r.Code == "input.unknown-key");
            if (!entity.ChecksColumns)
            {
                continue;
            }

            foreach (string column in entity.Columns)
            {
                foreach (WriteOperation op in new[] { WriteOperation.Create, WriteOperation.Update })
                {
                    foreach (ActorInfo actor in new[] { Person("architect"), Nested() })
                    {
                        string label = entity.Key + " " + op + " " + column;
                        EvaluationResult<WritePlan?> known = Guard(snapshot, actor, entity.Key, op, op == WriteOperation.Create ? null : "{}", "{\"" + column + "\":\"fixture-value\"}");
                        Assert.True(!known.Reasons.Any(r => r.Code == "input.unknown-column"), label + ": " + string.Join(" | ", known.Reasons));

                        foreach (string unknown in new[] { char.ToUpperInvariant(column[0]) + column.Substring(1), column + "Fixture", "fixtureUnknownColumn" })
                        {
                            EvaluationResult<WritePlan?> refused = Guard(snapshot, actor, entity.Key, op, op == WriteOperation.Create ? null : "{}", "{\"" + unknown + "\":\"fixture-value\"}");
                            Assert.True(refused.Outcome == "invalid-input", label + " as " + unknown + ": " + refused.Outcome);
                            Assert.Equal("input.unknown-column", Assert.Single(refused.Reasons).Code);
                            checkedWrites++;
                        }

                        checkedWrites++;
                    }
                }
            }
        }

        Assert.True(checkedWrites > 5000, "only " + checkedWrites + " writes checked");
    }

    // Class A (C02, O20, O24 in the model): inserted once, never updated, never deleted, by every writer, the engine
    // included, in every effect or in none.
    [Fact]
    public void AppendOnlyEntitiesOfTheModelRefuseEveryUpdateAndDelete()
    {
        CompiledSnapshot snapshot = OperationTestData.Snapshot();
        ModelEntityFacts[] appendOnly = ModelEntities.Value.Where(e => e.HistoryClass == "A" && !e.Id.StartsWith("L", StringComparison.Ordinal)).ToArray();
        Assert.Equal(new[] { "C02", "O20", "O24" }, appendOnly.Select(e => e.Id).ToArray());
        int checkedWrites = 0;
        foreach (ModelEntityFacts entity in appendOnly)
        {
            foreach ((ActorInfo actor, EffectKind? effect) in Writers())
            {
                foreach (WriteOperation op in new[] { WriteOperation.Update, WriteOperation.Delete })
                {
                    EvaluationResult<WritePlan?> result = Guard(snapshot, actor, entity.Key, op, "{\"createdByPrincipal\":\"fixture-person-1\",\"createdOn\":\"2026-11-22T08:59:00Z\"}", op == WriteOperation.Delete ? null : "{\"description\":\"changed\"}", effect);
                    Assert.True(result.Outcome == "refused", entity.Key + " " + op + " by " + Describe(actor, effect) + ": " + result.Outcome);
                    checkedWrites++;
                }
            }
        }

        Assert.True(checkedWrites > 50, "only " + checkedWrites + " writes checked");
    }

    // Class F (C46, O08, O09, O10, O13, O14, O22 in the model): once a row is final, every column of the model, from
    // every writer, the engine included, in every effect or in none, is refused, but the documented transitions: the
    // supersede-decision effect sets Superseded By on a Decision (document 22, section 8.1), and the engine writes
    // Counts Toward Requirement on an Approval Record when the outcome is written (section 8.2). The final state of each
    // entity is the note of the entity in the model; a new class F entity fails this test until its final state is here.
    [Fact]
    public void FinalRowsOfEveryClassFEntityRefuseEveryWriteButTheDocumentedOnes()
    {
        var finalState = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["configuration-draft"] = "{\"draftStatus\":\"activated\"}",
            ["tier-assessment"] = "{\"tierDecision\":\"fixture-decision-5\"}",
            ["decision"] = "{\"statusMeaning\":\"accepted\",\"status\":\"decision-accepted\",\"decisionType\":\"design\"}",
            ["approval-record"] = "{\"outcomeMeaning\":\"approved\",\"outcome\":\"approval-approved\",\"decision\":\"fixture-decision-1\",\"approverHoldsRequiredRole\":true}",
            ["artifact-version"] = "{\"digest\":\"sha256:1\",\"sourceCommit\":\"abc\",\"sourceRepositoryUrl\":\"https://example.org/repo\"}",
            ["deployment-record"] = "{\"resultMeaning\":\"succeeded\",\"result\":\"deployment-succeeded\"}",
            ["gap-check-run"] = "{\"finishedOn\":\"2026-11-22T08:30:00Z\"}",
        };
        ModelEntityFacts[] freeze = ModelEntities.Value.Where(e => e.HistoryClass == "F" && !e.Id.StartsWith("L", StringComparison.Ordinal)).ToArray();
        Assert.Equal(finalState.Keys.OrderBy(k => k, StringComparer.Ordinal), freeze.Select(e => e.Key).OrderBy(k => k, StringComparer.Ordinal));

        CompiledSnapshot snapshot = OperationTestData.Snapshot();
        int checkedWrites = 0;
        int transitions = 0;
        foreach (ModelEntityFacts entity in freeze)
        {
            foreach (string column in entity.Columns)
            {
                string value = column == "draftStatus" ? "\"draft\"" : column == "countsTowardRequirement" ? "true" : "\"fixture-value\"";
                foreach ((ActorInfo actor, EffectKind? effect) in Writers())
                {
                    bool documented = actor.IsEngineNested
                        && ((entity.Key == "decision" && column == "supersededBy" && effect == EffectKind.SupersedeDecision)
                            || (entity.Key == "approval-record" && column == "countsTowardRequirement"));
                    EvaluationResult<WritePlan?> result = Guard(snapshot, actor, entity.Key, WriteOperation.Update, finalState[entity.Key], "{\"" + column + "\":" + value + "}", effect);
                    string label = entity.Key + " " + column + " by " + Describe(actor, effect);
                    if (documented)
                    {
                        Assert.True(result.Outcome == "allowed", label + ": " + string.Join(" | ", result.Reasons));
                        transitions++;
                    }
                    else
                    {
                        Assert.True(result.Outcome == "refused", label + ": " + result.Outcome + " " + string.Join(" | ", result.Reasons));
                    }

                    checkedWrites++;
                }
            }
        }

        Assert.True(transitions > 5, "only " + transitions + " documented transitions checked");
        Assert.True(checkedWrites > 2000, "only " + checkedWrites + " writes checked");

        // An update with no columns would still restamp a final row, so it is refused from
        // every writer too, the supersede-decision effect included (it may set only Superseded By).
        foreach (ModelEntityFacts entity in freeze)
        {
            foreach ((ActorInfo actor, EffectKind? effect) in Writers())
            {
                EvaluationResult<WritePlan?> empty = Guard(snapshot, actor, entity.Key, WriteOperation.Update, finalState[entity.Key], "{}", effect);
                Assert.True(empty.Outcome == "refused", entity.Key + " empty update by " + Describe(actor, effect) + ": " + empty.Outcome + " " + string.Join(" | ", empty.Reasons));
            }
        }
    }

    // Id, Created On, Created By, Modified On and Modified By are platform-supplied (document
    // 22, section 2.1.1). For every entity whose columns are checked, every such column, written on a create or an update
    // by a person or by the engine, is either refused with the write or dropped: it is listed in removedColumns, never in
    // setColumns, so it never reaches the store.
    [Fact]
    public void PlatformColumnsNeverReachTheStore()
    {
        CompiledSnapshot snapshot = OperationTestData.Snapshot();
        string[] platform = { "id", "createdOn", "createdBy", "modifiedOn", "modifiedBy" };
        int dropped = 0;
        int checkedWrites = 0;
        foreach (ModelEntityFacts entity in ModelEntities.Value.Where(e => e.ChecksColumns && !e.Id.StartsWith("L", StringComparison.Ordinal)))
        {
            Assert.True(platform.All(c => entity.Columns.Contains(c, StringComparer.Ordinal)), entity.Key + " lacks a platform column in the model");
            foreach (string column in platform)
            {
                foreach (WriteOperation op in new[] { WriteOperation.Create, WriteOperation.Update })
                {
                    foreach (ActorInfo actor in new[] { Person("platform-admin"), Nested() })
                    {
                        EvaluationResult<WritePlan?> result = Guard(snapshot, actor, entity.Key, op, op == WriteOperation.Create ? null : "{}", "{\"" + column + "\":\"2099-01-01T00:00:00Z\"}");
                        string label = entity.Key + " " + op + " " + column + " by " + Describe(actor, null);
                        Assert.True(result.Outcome != "allowed" || result.Payload!.RemovedColumns.Contains(column), label + ": allowed without dropping it");
                        Assert.True(result.Payload == null || !result.Payload.SetColumns.TryGetMember(column, out _), label + ": the plan sets it");
                        dropped += result.Outcome == "allowed" ? 1 : 0;
                        checkedWrites++;
                    }
                }
            }
        }

        Assert.True(dropped > 50, "only " + dropped + " writes reached the drop");
        Assert.True(checkedWrites > 200, "only " + checkedWrites + " writes checked");
    }

    // The model's fill-once columns (O13 Digest, Source Commit, Source Repository Url): filled once while empty, the same
    // value again changes nothing, another value is refused.
    [Fact]
    public void FillOnceColumnsOfTheModelFillOnceWhileEmpty()
    {
        CompiledSnapshot snapshot = OperationTestData.Snapshot();
        ModelEntityFacts[] withFillOnce = ModelEntities.Value.Where(e => e.FillOnce.Length > 0 && !e.Id.StartsWith("L", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(withFillOnce);
        foreach (ModelEntityFacts entity in withFillOnce)
        {
            foreach (string column in entity.FillOnce)
            {
                string label = entity.Key + " " + column;
                Assert.True(Guard(snapshot, Person("application-owner"), entity.Key, WriteOperation.Update, "{}", "{\"" + column + "\":\"a\"}").Outcome == "allowed", label + " while empty");
                Assert.True(Guard(snapshot, Person("application-owner"), entity.Key, WriteOperation.Update, "{\"" + column + "\":null}", "{\"" + column + "\":\"a\"}").Outcome == "allowed", label + " while null");
                Assert.True(Guard(snapshot, Person("application-owner"), entity.Key, WriteOperation.Update, "{\"" + column + "\":\"a\"}", "{\"" + column + "\":\"a\"}").Outcome == "allowed", label + " with the same value");
                EvaluationResult<WritePlan?> changed = Guard(snapshot, Nested(), entity.Key, WriteOperation.Update, "{\"" + column + "\":\"a\"}", "{\"" + column + "\":\"b\"}");
                Assert.True(changed.Outcome == "refused", label + " with another value: " + changed.Outcome);
                Assert.Contains(changed.Reasons, r => r.Code == "record.final");
            }
        }
    }

    // ---- Helpers ----

    // Every kind of writer: people with and without roles, an AI client, the approval runner in its own name, a pipeline
    // and an agent user, and the engine in every effect and in none.
    private static IEnumerable<(ActorInfo Actor, EffectKind? Effect)> Writers()
    {
        yield return (Person(), null);
        yield return (Person("architect"), null);
        yield return (Person("configuration-admin"), null);
        yield return (Person("platform-admin"), null);
        yield return (Person("application-owner"), null);
        yield return (AiClient("configuration-admin"), null);
        yield return (Runner(), null);
        yield return (new ActorInfo("fixture-pipeline-1", PrincipalKind.ServicePrincipal, ActorKindMeaning.Pipeline, new[] { "deployment-recorder" }), null);
        yield return (new ActorInfo("fixture-agent-1", PrincipalKind.AgentUser, ActorKindMeaning.AiClientForPerson, new[] { "architect" }, "fixture-person-1"), null);
        foreach (EffectKind? effect in NestedEffects())
        {
            yield return (Nested(), effect);
        }
    }

    private static string Describe(ActorInfo actor, EffectKind? effect)
    {
        return EngineContract.ActorKindMeanings.ToWire(actor.ActorKind) + (actor.IsEngineNested ? " nested " + (effect?.ToString() ?? "no effect") : string.Empty)
            + (actor.RoleKeys.Count == 0 ? string.Empty : " (" + string.Join(", ", actor.RoleKeys) + ")");
    }

    // One entity of model/logical-model.json: its ID, key, history class, columns (with its column sets) and fill-once
    // columns, as lowerCamelCase field names.
    private sealed class ModelEntityFacts
    {
        public ModelEntityFacts(string id, string key, string historyClass, string kind, string[] columns, string[] fillOnce)
        {
            Id = id;
            Key = key;
            HistoryClass = historyClass;
            Kind = kind;
            Columns = columns;
            FillOnce = fillOnce;
        }

        public string Id { get; }

        public string Key { get; }

        public string HistoryClass { get; }

        public string Kind { get; }

        public string[] Columns { get; }

        public string[] FillOnce { get; }

        // Operational entities, C02 and C46. Live configuration rows carry the bundle's field names.
        public bool ChecksColumns => Kind == "operational" || Id == "C02" || Id == "C46";
    }

    private static readonly Lazy<IReadOnlyList<ModelEntityFacts>> ModelEntities = new(LoadModel);

    private static IReadOnlyList<ModelEntityFacts> LoadModel()
    {
        JsonTreeNode root = CanonicalJson.Parse(File.ReadAllText(Path.Combine(TestData.Root, "model", "logical-model.json")));
        var sets = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (JsonTreeNode set in Items(root, "columnSets"))
        {
            sets[Member(set, "name")!] = Items(set, "attributes").Select(a => FieldName(Member(a, "name")!)).ToArray();
        }

        var entities = new List<ModelEntityFacts>();
        foreach (JsonTreeNode entity in Items(root, "entities"))
        {
            string[] columns = Items(entity, "columnSets").SelectMany(s => sets[s.GetString()])
                .Concat(Items(entity, "attributes").Select(a => FieldName(Member(a, "name")!)))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            string[] fillOnce = Items(entity, "attributes").Where(a => Member(a, "fillOnce") == "true").Select(a => FieldName(Member(a, "name")!)).ToArray();
            string name = Member(entity, "name")!;
            entities.Add(new ModelEntityFacts(Member(entity, "id")!, name.ToLowerInvariant().Replace(' ', '-'), Member(entity, "historyClass")!, Member(entity, "kind")!, columns, fillOnce));
        }

        return entities;
    }

    // The contract layer's field name of a logical name (document 20, section 3.3): "Is AI Created" is "isAiCreated".
    private static string FieldName(string logicalName)
    {
        string[] words = logicalName.Split(' ');
        return string.Concat(words.Select((w, i) => i == 0 ? w.ToLowerInvariant() : char.ToUpperInvariant(w[0]) + w.Substring(1).ToLowerInvariant()));
    }

    private static IReadOnlyList<JsonTreeNode> Items(JsonTreeNode node, string member)
    {
        return node.TryGetMember(member, out JsonTreeNode? value) && value.Kind == JsonTreeKind.Array ? value.Items : Array.Empty<JsonTreeNode>();
    }

    private static string FirstVocabularyMapping()
    {
        CompiledSnapshot snapshot = OperationTestData.Snapshot();
        Assert.True(snapshot.Snapshot.TryGetEntity("vocabulary-mapping", out SnapshotEntity? mappings));
        return mappings!.Rows.First(r => r.IsActive).KeyPath;
    }

    private const string Pending = "{\"outcomeMeaning\":\"pending\",\"outcome\":\"approval-pending\",\"decision\":\"fixture-decision-1\"}";

    // Every effect an engine-nested write can belong to, and none (the engine's own operations, such as the request).
    private static IEnumerable<EffectKind?> NestedEffects()
    {
        return new EffectKind?[] { null }.Concat(EngineContract.EffectKinds.Values.Select(e => (EffectKind?)e));
    }

    private static string Answer(string actorKind, string principalKind, string outcome = "approval-approved")
    {
        return "{\"outcome\":\"" + outcome + "\",\"approver\":\"fixture-person-2\",\"approverActorKind\":\"" + actorKind + "\",\"approverPrincipalKind\":\"" + principalKind + "\"}";
    }

    private static void AssertRefusedOutsideActivation(CompiledSnapshot snapshot, ActorInfo actor, string entity, WriteOperation op, EffectKind? effect)
    {
        EvaluationResult<WritePlan?> result = Guard(snapshot, actor, entity, op, op == WriteOperation.Create ? null : "{\"key\":\"some-row\",\"origin\":\"shipped\"}", op == WriteOperation.Delete ? null : "{\"label\":\"A label\"}", effect);
        Assert.True(result.Outcome == "refused", entity + " " + op + " by " + EngineContract.ActorKindMeanings.ToWire(actor.ActorKind) + ": " + result.Outcome);
        Assert.Contains(result.Reasons, r => r.Code == "config.activation-only" || r.Code == "actor.agent-write-refused");
    }

    private static ActivationOutcome Activate(string preset, string draftJson, DateTime asOf, DraftStatusMeaning status = DraftStatusMeaning.Submitted, int? resulting = null)
    {
        var activator = new ConfigurationActivator(OperationTestData.Shipped);
        ConfigurationPatch draft = ConfigurationPatch.FromDocument(CanonicalJson.Parse(draftJson), PatchKind.Draft, "test draft");
        var request = new ActivationRequest(
            "fixture-draft-1",
            status,
            "fixture-decision-7",
            DecisionStatusMeaning.Accepted,
            ContentSource.Preset(preset),
            1,
            ContentSource.Preset(preset),
            1,
            draft,
            resulting,
            approvalRulesVersionNumber: 1,
            instructedByPrincipalId: "fixture-person-1");
        CompiledSnapshot active = OperationTestData.Snapshot(preset);
        return activator.Activate(new EvaluationContext(1, active.ContentHash, asOf, Nested(), "test-correlation"), request);
    }

    private static EvaluationResult<WritePlan?> Guard(CompiledSnapshot snapshot, ActorInfo actor, string entity, WriteOperation op, string? before, string? after, EffectKind? effect = null, string? rowId = null, bool setKind = true)
    {
        bool configuration = ConfigurationEntities.TryGet(entity, out _) || entity == "configuration-set" || entity == "preset" || entity == "configuration-set-version";
        var request = new WriteRequest(
            entity,
            op,
            rowId ?? (op == WriteOperation.Create ? null : "fixture-row-1"),
            after == null ? null : CanonicalJson.Parse(after),
            before == null ? null : CanonicalJson.Parse(before),
            effect,
            configurationSetKind: configuration && setKind ? ConfigurationSetKind.Working : null,
            bindingPrincipalKind: entity == "role-group-binding" ? PrincipalKind.Group : null);
        return WriteGuardOperation.Evaluate(snapshot.Snapshot, Context(snapshot, actor), request);
    }

    private static EvaluationContext Context(CompiledSnapshot? snapshot, ActorInfo actor)
    {
        return snapshot == null
            ? new EvaluationContext(1, CanonicalHash.ComputeContentHash(JsonTreeNode.Null), AsOf, actor, "test-correlation")
            : new EvaluationContext(snapshot.VersionNumber, snapshot.ContentHash, AsOf, actor, "test-correlation");
    }

    private static IEnumerable<SnapshotRow> Statuses(CompiledSnapshot snapshot, string entity)
    {
        return snapshot.Snapshot.TryGetEntity("status", out SnapshotEntity? statuses)
            ? statuses.Rows.Where(r => r.IsActive && Text(r, "appliesToEntity") == entity && r.EngineMeaning != "missing")
            : Enumerable.Empty<SnapshotRow>();
    }

    private static string? Text(SnapshotRow row, string column)
    {
        return row.TryGetColumn(column, out JsonTreeNode? value) && value.Kind == JsonTreeKind.String ? value.GetString() : null;
    }

    // A member of a JSON object as text: the string itself, or the canonical JSON of any other value. Null when absent.
    private static string? Member(JsonTreeNode node, string name)
    {
        if (!node.TryGetMember(name, out JsonTreeNode? value))
        {
            return null;
        }

        return value.Kind == JsonTreeKind.String ? value.GetString() : CanonicalJson.Write(value, CanonicalProfile.Snapshot);
    }

    private static ActorInfo Person(string? role = null)
    {
        return new ActorInfo("fixture-person-1", PrincipalKind.User, ActorKindMeaning.Person, role == null ? null : new[] { role });
    }

    private static ActorInfo NestedPerson()
    {
        return new ActorInfo("fixture-person-1", PrincipalKind.User, ActorKindMeaning.Person, isEngineNested: true);
    }

    private static ActorInfo AiClient(string role)
    {
        return new ActorInfo("fixture-person-1", PrincipalKind.User, ActorKindMeaning.AiClientForPerson, new[] { role }, "fixture-person-1");
    }

    private static ActorInfo Runner()
    {
        return new ActorInfo("fixture-runner-1", PrincipalKind.ServicePrincipal, ActorKindMeaning.ApprovalRunner, new[] { "approval-writer" }, "fixture-person-2");
    }

    private static ActorInfo Nested()
    {
        return new ActorInfo("fixture-runner-1", PrincipalKind.ServicePrincipal, ActorKindMeaning.ApprovalRunner, new[] { "approval-writer" }, "fixture-person-2", isEngineNested: true);
    }
}
