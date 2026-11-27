using System;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;
using Product.Engine.Runtime.Callers;
using Product.Engine.Runtime.Ports;
using Xunit;

namespace Product.Engine.Runtime.Tests;

// Full writes through the in-memory store adapter: an intake request, a decision on a
// card answered by a person, and a deployment record from a workload grant. Each runs plan, fetch and evaluate, OP-08,
// the row write and every effect as engine-nested writes, in one transaction.
public sealed class FullWriteTests
{
    // ---- An intake request ----

    // An AI client files an intake for the person: the status is forced to draft and the row is marked AI created
    // (G-08), with the stamps of every engine-evaluated write (document 22, section 2.4).
    [Fact]
    public void AnAiClientsIntakeRequestStartsAsDraftWithTheEngineStamps()
    {
        Harness h = Harness.Create();
        RuntimeOutcome outcome = Harness.Ok(h.Runtime.Write(Harness.AiClientOf1, WriteCommand.Create("intake-request",
            "{\"requester\":\"" + Harness.Person1 + "\",\"title\":\"Fixture travel requests\",\"need\":\"Replace the shared spreadsheet\",\"status\":\"intake-submitted\",\"isAiCreated\":false}")));

        Assert.True(outcome.Created);
        Assert.True(outcome.HasReason(ReasonCodes.AiForcedDraft));
        StoredRow row = h.Row("intake-request", outcome.RowId!);
        Assert.Equal("intake-draft", Harness.Text(row, "status"));
        Assert.Equal("draft", Harness.Text(row, "statusMeaning"));
        Assert.True(row.Flag("isAiCreated"));
        Assert.Equal("ai-client-for-person", Harness.Text(row, "performedByActorKind"));
        Assert.Equal(Harness.Person1, Harness.Text(row, "onBehalfOf"));
        Assert.Equal("Claude Code", Harness.Text(row, "clientApp"));
        Assert.Equal(1, row.Number("configurationSetVersion"));
        Assert.Equal("0.1.0", Harness.Text(row, "engineVersion"));
        Assert.Equal("2026-11-22T09:00:00Z", Harness.Text(row, "engineCheckedOn"));
        Assert.False(row.Flag("clientAppVerified"));
        Assert.Single(outcome.Writes);
    }

    // The person then submits it, and the tier assessment's Computed Tier comes from OP-01, never from the caller (G-09).
    [Fact]
    public void ThePersonSubmitsTheIntakeAndTheEngineComputesItsTier()
    {
        Harness h = Harness.Create();
        string intake = Harness.Ok(h.Runtime.Write(Harness.AiClientOf1, WriteCommand.Create("intake-request",
            "{\"requester\":\"" + Harness.Person1 + "\",\"title\":\"Fixture\",\"need\":\"Fixture need\",\"proposedKind\":\"power-platform-app\"}"))).RowId!;

        RuntimeOutcome submitted = Harness.Ok(h.Runtime.Write(Harness.PersonOne, WriteCommand.Update("intake-request", intake, "{\"status\":\"intake-submitted\"}")));
        Assert.Equal("submitted", Harness.Text(h.Row("intake-request", intake), "statusMeaning"));
        Assert.Equal("person", Harness.Text(h.Row("intake-request", intake), "performedByActorKind"));
        Assert.False(submitted.Created);

        RuntimeOutcome assessed = Harness.Ok(h.Runtime.Write(Harness.AiClientOf1, WriteCommand.Create("tier-assessment",
            "{\"intakeRequest\":\"" + intake + "\",\"answersJson\":" + Harness.Answers(tierTwo: true) + ",\"computedTier\":\"tier-3\",\"assessedBy\":\"" + Harness.Person1 + "\"}")));
        Assert.Equal("tier-2", Harness.Text(h.Row("tier-assessment", assessed.RowId!), "computedTier"));
        Assert.True(assessed.HasReason(ReasonCodes.EngineColumnOverwritten));
        Assert.Equal("computed", RuntimeJsonTestHelpers.Text(assessed.Results["OP-01"], "outcome"));
    }

    // A lowest-tier intake for a new application is on the locked list, so the engine accepts it with no card (OP-02
    // engine-accept; G-17), after the accept gate's readiness (G-14: Balanced's accept gate needs a business owner, whom
    // the request names). The named person consents first (B25-18): the request opens only their consent, and the engine
    // accepts the decision on its own basis once they give it. WORKING DEFAULT (open design question 8): the locked list
    // holds lowest-tier intake.
    [Fact]
    public void ALowestTierIntakeIsAcceptedByTheEngineOnceTheNamedOwnerConsents()
    {
        Harness h = Harness.Create();
        string decision = h.IntakeDecision(tierTwo: false, linkApplication: false, businessOwner: Harness.Person2);
        Assert.Equal("proposed", Harness.Text(h.Row("decision", decision), "statusMeaning"));

        RuntimeOutcome request = Harness.Ok(h.Runtime.RequestApprovals(Harness.AiClientOf1, decision));
        Assert.Equal("engine-accept", RuntimeJsonTestHelpers.Text(request.Payload, "outcome"));
        Assert.False(RuntimeJsonTestHelpers.Flag(request.Payload, "accepted"));
        Assert.True(RuntimeJsonTestHelpers.Flag(request.Payload, "waitingForConsent"));
        Assert.True(request.HasReason(ReasonCodes.DecisionConsentWaiting));
        string consent = h.ConsentRecord(request.Payload);
        Assert.Single(h.Store.Rows("approval-record"));
        Assert.Equal("proposed", Harness.Text(h.Row("decision", decision), "statusMeaning"));

        RuntimeOutcome answered = Harness.Ok(h.Answer(consent, "approval-approved", Harness.Person2));
        Assert.Equal("ready", RuntimeJsonTestHelpers.Text(answered.Results["OP-07"], "outcome"));

        StoredRow accepted = h.Row("decision", decision);
        Assert.Equal("accepted", Harness.Text(accepted, "statusMeaning"));
        Assert.Equal("2026-11-22T09:00:00Z", Harness.Text(accepted, "decidedOn"));
        Assert.Equal("engine", Harness.Text(accepted, "performedByActorKind"));
        StoredRow audit = h.Store.Rows("audit-entry").Single(a => Harness.Text(a, "operation") == "decision-accepted");
        Assert.Equal("engine-accept", RuntimeJsonTestHelpers.Text(audit.Column("changedValuesJson"), "basis"));
        Assert.Contains(answered.Writes, w => w.Entity == "decision" && w.Effect == EffectKind.AcceptDecision && w.IsEngineNested);
    }

    // Security review 2026-11-24 (document 25, B25-15): a lowest-tier intake linked to an application already in the
    // register is never accepted by the engine. It goes on a card to the accountable holder, so a person decides.
    [Fact]
    public void ALowestTierIntakeLinkedToARegisteredApplicationGoesToAPerson()
    {
        Harness h = Harness.Create();
        string decision = h.IntakeDecision(tierTwo: false);
        RuntimeOutcome request = Harness.Ok(h.Runtime.RequestApprovals(Harness.AiClientOf1, decision));
        Assert.Equal("required", RuntimeJsonTestHelpers.Text(request.Payload, "outcome"));
        Assert.False(request.HasReason(ReasonCodes.DecisionEngineAccepted));
        Assert.Equal("proposed", Harness.Text(h.Row("decision", decision), "statusMeaning"));
        Assert.NotEmpty(h.Store.Rows("approval-record"));
    }

    // Without a business owner the accept gate is not ready: the decision stays proposed and no card goes out.
    [Fact]
    public void AnIntakeWhoseGateIsNotReadyStaysProposed()
    {
        Harness h = Harness.Create();
        string decision = h.IntakeDecision(tierTwo: false, linkApplication: false);
        RuntimeOutcome request = Harness.Ok(h.Runtime.RequestApprovals(Harness.AiClientOf1, decision));
        Assert.False(RuntimeJsonTestHelpers.Flag(request.Payload, "accepted"));
        Assert.True(request.HasReason(ReasonCodes.GateNotReady), request.ToString());
        Assert.Equal("proposed", Harness.Text(h.Row("decision", decision), "statusMeaning"));
    }

    // ---- A decision on a card by a person ----

    // A tier 2 intake goes on one card to the holder of the accountable letter (OP-02 rule 7). The approval runner
    // records the platform admin's approval; the engine recomputes (OP-03), writes Counts Toward Requirement, and accepts
    // the decision, all as effects of the runner's write.
    [Fact]
    public void APersonsApprovalOnTheCardAcceptsTheDecision()
    {
        Harness h = Harness.Create();
        string decision = h.IntakeDecision(tierTwo: true);
        RuntimeOutcome request = Harness.Ok(h.Runtime.RequestApprovals(Harness.AiClientOf1, decision));
        Assert.Equal("required", RuntimeJsonTestHelpers.Text(request.Payload, "outcome"));
        string record = h.CountingRecord(request.Payload);
        StoredRow pending = h.Row("approval-record", record);
        Assert.Equal("pending", Harness.Text(pending, "outcomeMeaning"));
        Assert.Equal("intake-a", Harness.Text(pending, "decisionRight"));
        Assert.Equal("platform-admin", Harness.Text(pending, "requiredRole"));
        Assert.False(pending.Flag("countsTowardRequirement"));

        RuntimeOutcome answer = Harness.Ok(h.Answer(record, "approval-approved", Harness.Person2));
        Assert.Equal("satisfied", RuntimeJsonTestHelpers.Text(answer.Results["OP-03"], "outcome"));

        StoredRow answered = h.Row("approval-record", record);
        Assert.Equal("approved", Harness.Text(answered, "outcomeMeaning"));
        Assert.Equal("person", Harness.Text(answered, "approverActorMeaning"));
        Assert.True(answered.Flag("countsTowardRequirement"));

        // The engine's count was the last write on the row, so its stamps name the engine; the Audit Entry of the answer
        // names the approval runner, acting in its own name.
        Assert.Equal("engine", Harness.Text(answered, "performedByActorKind"));
        StoredRow written = h.Store.Rows("audit-entry").Single(a => Harness.Text(a, "operation") == "approval-outcome-written");
        Assert.Equal("approval-runner", Harness.Text(written, "actorKind"));
        Assert.Equal(Harness.Runner, Harness.Text(written, "actor"));

        StoredRow accepted = h.Row("decision", decision);
        Assert.Equal("accepted", Harness.Text(accepted, "statusMeaning"));
        // Effects run in plan order, depth first: the recompute (and the acceptance it causes, which cancels the three
        // consultation cards still pending: the architect, the PMO and the security reviewer) before the answer's own Audit
        // Entry.
        Assert.Equal(new[] { "decision-accepted", "approval-outcome-written" }, h.Store.Rows("audit-entry").Select(a => Harness.Text(a, "operation")).ToArray());
        Assert.Equal(3, h.Store.Rows("approval-record").Count(r => r.Flag("isConsultation") && Harness.Text(r, "outcomeMeaning") == "cancelled"));
        Assert.Equal(
            new[]
            {
                "update approval-record", "update approval-record (recompute-approvals)", "update decision (accept-decision)", "create audit-entry (write-audit-entry)",
                "update approval-record (recompute-approvals)", "update approval-record (recompute-approvals)", "update approval-record (recompute-approvals)", "create audit-entry (write-audit-entry)",
            },
            answer.Writes.Select(w => EngineContract.WriteOperations.ToWire(w.Operation) + " " + w.Entity + (w.Effect.HasValue ? " (" + EngineContract.EffectKinds.ToWire(w.Effect.Value) + ")" : string.Empty)).ToArray());
    }

    // An AI client's answer is kept as recommendation only and never counts (AI never approves): the decision stays
    // proposed. A person's rejection rejects it.
    [Fact]
    public void AnAiAnswerNeverCountsAndAPersonsRejectionRejects()
    {
        Harness h = Harness.Create();
        string decision = h.IntakeDecision(tierTwo: true);
        string record = h.CountingRecord(Harness.Ok(h.Runtime.RequestApprovals(Harness.AiClientOf1, decision)).Payload);

        RuntimeOutcome ai = Harness.Ok(h.Answer(record, "approval-approved", Harness.Person2, actorKind: "ai-client"));
        Assert.True(ai.HasReason(ReasonCodes.ApprovalAiActorNotCounted));
        Assert.Equal("recommendation-only", Harness.Text(h.Row("approval-record", record), "outcomeMeaning"));
        Assert.False(h.Row("approval-record", record).Flag("countsTowardRequirement"));
        Assert.Equal("proposed", Harness.Text(h.Row("decision", decision), "statusMeaning"));

        Harness other = Harness.Create();
        string second = other.IntakeDecision(tierTwo: true);
        string card = other.CountingRecord(Harness.Ok(other.Runtime.RequestApprovals(Harness.AiClientOf1, second)).Payload);
        Harness.Ok(other.Answer(card, "approval-rejected", Harness.Person2, typedReason: "Duplicate of an existing app"));
        Assert.Equal("rejected", Harness.Text(other.Row("decision", second), "statusMeaning"));
    }

    // Only the approval runner writes an outcome (G-06), and only once: a second, different answer is refused as a
    // conflict with the stored answer, and the refused call writes nothing.
    [Fact]
    public void OnlyTheApprovalRunnerAnswersAndOnlyOnce()
    {
        Harness h = Harness.Create();
        string decision = h.IntakeDecision(tierTwo: true);
        string record = h.CountingRecord(Harness.Ok(h.Runtime.RequestApprovals(Harness.AiClientOf1, decision)).Payload);

        string before = h.Store.Dump();
        RuntimeOutcome byPerson = h.Runtime.Write(Harness.PersonTwo, WriteCommand.Update("approval-record", record, "{\"outcome\":\"approval-approved\",\"approver\":\"" + Harness.Person2 + "\",\"approverActorKind\":\"person\",\"approverPrincipalKind\":\"user\"}"));
        Assert.Equal("refused", byPerson.Outcome);
        Assert.True(byPerson.HasReason(ReasonCodes.ApprovalOutcomeWriter));
        Assert.Equal(before, h.Store.Dump());

        Harness.Ok(h.Answer(record, "approval-approved", Harness.Person2));
        string after = h.Store.Dump();
        RuntimeOutcome again = h.Answer(record, "approval-rejected", Harness.Person2, typedReason: "Changed my mind");
        Assert.Equal("conflict", again.Outcome);
        Assert.Equal(after, h.Store.Dump());

        RuntimeOutcome same = Harness.Ok(h.Answer(record, "approval-approved", Harness.Person2));
        Assert.True(same.IsNoOp);
        Assert.Equal(after, h.Store.Dump());
    }

    // RA-05: once answered, an Approval Record is frozen in the store too. Nobody moves it to
    // another decision, changes its rule or sets the responder's role afterwards: not a person, not an AI client, not the
    // approval runner without an answer. The store stays as it was after each refusal.
    [Fact]
    public void AnAnsweredApprovalRecordNeverChanges()
    {
        Harness h = Harness.Create();
        string decision = h.IntakeDecision(tierTwo: true);
        string other = h.IntakeDecision(tierTwo: true);
        string record = h.CountingRecord(Harness.Ok(h.Runtime.RequestApprovals(Harness.AiClientOf1, decision)).Payload);
        Harness.Ok(h.Answer(record, "approval-approved", Harness.Person2));
        string after = h.Store.Dump();

        (CallerIdentity Caller, string Columns, string Code)[] attempts =
        {
            (Harness.PersonTwo, "{\"decision\":\"" + other + "\"}", ReasonCodes.EngineEffectOnly),
            (Harness.AiClientOf1, "{\"decision\":\"" + other + "\"}", ReasonCodes.EngineEffectOnly),
            (Harness.PersonTwo, "{\"approverHoldsRequiredRole\":false}", ReasonCodes.ApprovalOutcomeWriter),
            (Harness.ApprovalRunner, "{\"approverHoldsRequiredRole\":false}", ReasonCodes.ApprovalOutcomeWriter),
            (Harness.PersonTwo, "{\"countsTowardRequirement\":false}", ReasonCodes.EngineEffectOnly),
        };
        foreach ((CallerIdentity caller, string columns, string code) in attempts)
        {
            RuntimeOutcome outcome = h.Runtime.Write(caller, WriteCommand.Update("approval-record", record, columns));
            Assert.True(outcome.Outcome == "refused", columns + ": " + outcome);
            Assert.True(outcome.HasReason(code), columns + ": " + outcome);
            Assert.Equal(after, h.Store.Dump());
        }
    }

    // A column name the logical model does not hold, such as Status for status, is invalid input before
    // any rule runs, so an AI client's accepted status can never pass G-08 and G-17 unjudged. Nothing is written.
    [Fact]
    public void AnUnknownColumnNameIsInvalidInputAndWritesNothing()
    {
        Harness h = Harness.Create();
        string decision = h.IntakeDecision(tierTwo: true);
        string before = h.Store.Dump();
        RuntimeOutcome outcome = h.Runtime.Write(Harness.AiClientOf1, WriteCommand.Update("decision", decision, "{\"Status\":\"decision-accepted\"}"));
        Assert.Equal("invalid-input", outcome.Outcome);
        Assert.True(outcome.HasReason(ReasonCodes.InputUnknownColumn), outcome.ToString());
        Assert.Empty(outcome.Writes);
        Assert.Equal(before, h.Store.Dump());
    }

    // No caller sets accepted (G-17); the refused write leaves the store as it was.
    [Fact]
    public void NoCallerAcceptsADecisionAndARefusalWritesNothing()
    {
        Harness h = Harness.Create();
        string decision = h.IntakeDecision(tierTwo: true);
        string before = h.Store.Dump();
        RuntimeOutcome outcome = h.Runtime.Write(Harness.PersonTwo, WriteCommand.Update("decision", decision, "{\"status\":\"decision-accepted\"}"));
        Assert.Equal("refused", outcome.Outcome);
        Assert.True(outcome.HasReason(ReasonCodes.DecisionAcceptByEngineOnly));
        Assert.Empty(outcome.Writes);
        Assert.Equal(before, h.Store.Dump());
    }

    // ---- A deployment record from a workload grant ----

    // The build pipeline holds deployment-recorder through its Workload Grant (RA-07). The engine sets the route and
    // computes the gate level and flags with OP-04 in record mode: tier 1 production is flag, and a deployment with no
    // change request is a normal change missing its approval.
    [Fact]
    public void APipelineWithAGrantRecordsADeploymentAndTheEngineSetsTheFlags()
    {
        Harness h = Harness.Create();
        RuntimeOutcome outcome = Harness.Ok(h.Runtime.RecordDeployment(Harness.BuildPipeline, Report(h, "run-1001-prod")));

        Assert.True(outcome.Created);
        StoredRow record = h.Row("deployment-record", outcome.RowId!);
        Assert.Equal("pipeline-step", Harness.Text(record, "deploymentRoute"));
        Assert.Equal("flag", Harness.Text(record, "gateLevelApplied"));
        Assert.True(record.Flag("isMissingApproval"));
        Assert.False(record.Flag("isOtherPath"));
        Assert.False(record.Flag("isOutsideWindow"));
        Assert.Equal("deployment-succeeded", Harness.Text(record, "result"));
        Assert.Equal(Harness.Pipeline, Harness.Text(record, "deployedBy"));
        Assert.Equal("pipeline", Harness.Text(record, "performedByActorKind"));
        Assert.True(outcome.HasReason(ReasonCodes.GateMissingApproval));
        Assert.Single(h.Store.Rows("artifact-version"));
        Assert.Equal("flag", RuntimeJsonTestHelpers.Text(outcome.Payload, "gateLevelApplied"));
    }

    // Upsert by Idempotency Key (document 25, section 8): the same key again returns the record with created false and
    // writes nothing.
    [Fact]
    public void TheSameIdempotencyKeyReturnsTheFirstRecord()
    {
        Harness h = Harness.Create();
        RuntimeOutcome first = Harness.Ok(h.Runtime.RecordDeployment(Harness.BuildPipeline, Report(h, "run-1002-prod")));
        string dump = h.Store.Dump();
        RuntimeOutcome replay = Harness.Ok(h.Runtime.RecordDeployment(Harness.BuildPipeline, Report(h, "run-1002-prod")));
        Assert.False(replay.Created);
        Assert.Equal(first.RowId, replay.RowId);
        Assert.Empty(replay.Writes);
        Assert.Equal(dump, h.Store.Dump());
    }

    // A retired target keeps its records and takes no new deployment (document 24, B24-18; document 25, B25-19): the record
    // step refuses one that finished on or after the retirement day, with the reason, and writes nothing; a replay of a
    // record made before, and a late report of a deployment that finished before the retirement day, are still taken.
    [Fact]
    public void ARetiredTargetRefusesANewDeploymentAndKeepsItsHistory()
    {
        Harness h = Harness.Create();
        RuntimeOutcome before = Harness.Ok(h.Runtime.RecordDeployment(Harness.BuildPipeline, Report(h, "run-1101-prod")));
        StoredRow target = h.Row("deployment-target", h.TargetId);
        h.Store.Tamper("deployment-target", target.Id, RuntimeJsonTestHelpers.With(RuntimeJsonTestHelpers.With(target.Columns, "retiredOn", JsonTreeNode.FromString("2026-11-22")), "retiredReason", JsonTreeNode.FromString("Replaced by the new production environment.")));
        string dump = h.Store.Dump();

        RuntimeOutcome refused = h.Runtime.RecordDeployment(Harness.BuildPipeline, Report(h, "run-1102-prod"));
        Assert.Equal("refused", refused.Outcome);
        Assert.True(refused.HasReason(ReasonCodes.DeploymentTargetRetired));
        Assert.Contains("retired on 2026-11-22 (Replaced by the new production environment)", refused.Reasons.First(r => r.Code == ReasonCodes.DeploymentTargetRetired).Message, StringComparison.Ordinal);
        Assert.Equal(dump, h.Store.Dump());

        RuntimeOutcome replay = Harness.Ok(h.Runtime.RecordDeployment(Harness.BuildPipeline, Report(h, "run-1101-prod")));
        Assert.Equal(before.RowId, replay.RowId);
        DeploymentReport late = Report(h, "run-1103-prod");
        RuntimeOutcome taken = Harness.Ok(h.Runtime.RecordDeployment(Harness.BuildPipeline, new DeploymentReport(late.IdempotencyKey, late.ApplicationId, late.ArtifactName, late.Version, new DateTime(2026, 11, 21, 8, 30, 0, DateTimeKind.Utc))
        {
            TargetExternalId = late.TargetExternalId,
            ArtifactKindKey = late.ArtifactKindKey,
            CodeApprovalKind = CodeApprovalKind.Person,
        }));
        Assert.True(taken.Created);
    }

    // An expired grant admits no app-only token, a workload without the deployment recorder role cannot record, and a
    // workload with no grant is refused before anything is read.
    [Fact]
    public void AWorkloadWithoutACurrentDeploymentRecorderGrantIsRefused()
    {
        Harness h = Harness.Create();
        h.Clock.UtcNow = new DateTime(2027, 6, 30, 8, 0, 0, DateTimeKind.Utc);
        RuntimeOutcome expired = h.Runtime.RecordDeployment(Harness.BuildPipeline, Report(h, "run-1003-prod"));
        Assert.Equal("refused", expired.Outcome);
        Assert.True(expired.HasReason(ReasonCodes.ActorWorkloadGrantMissing));

        h.Clock.UtcNow = Harness.AsOf;
        RuntimeOutcome runner = h.Runtime.RecordDeployment(Harness.ApprovalRunner, Report(h, "run-1004-prod"));
        Assert.Equal("refused", runner.Outcome);
        Assert.True(runner.HasReason(ReasonCodes.ActorWorkloadGrantMissing));

        RuntimeOutcome none = h.Runtime.RecordDeployment(Harness.ReleasePipeline, Report(h, "run-1005-prod"));
        Assert.Equal("refused", none.Outcome);
        Assert.Empty(h.Store.Rows("deployment-record"));
    }

    // The owner's AI tool records a deployment too, by the ai-tool route; a pipeline-only target flags it as another path.
    [Fact]
    public void AnAiToolDeploymentOnAPipelineOnlyTargetIsFlaggedAsAnotherPath()
    {
        Harness h = Harness.Create();
        RuntimeOutcome outcome = Harness.Ok(h.Runtime.RecordDeployment(Harness.AiClientOf1, Report(h, "chat-0001")));
        StoredRow record = h.Row("deployment-record", outcome.RowId!);
        Assert.Equal("ai-tool", Harness.Text(record, "deploymentRoute"));
        Assert.True(record.Flag("isOtherPath"));
        Assert.Equal(Harness.Person1, Harness.Text(record, "deployedBy"));
    }

    // Flags are the engine's: a caller's values for them are dropped (G-09), and the computed ones are written.
    [Fact]
    public void ACallersFlagValuesAreDroppedAndTheEnginesWritten()
    {
        Harness h = Harness.Create();
        RuntimeOutcome outcome = Harness.Ok(h.Runtime.Write(Harness.BuildPipeline, WriteCommand.Create("deployment-record",
            "{\"businessApplication\":\"" + h.ApplicationId + "\",\"deploymentTarget\":\"" + h.TargetId + "\",\"deploymentRoute\":\"pipeline-step\",\"finishedOn\":\"2026-11-22T08:30:00Z\","
            + "\"result\":\"deployment-succeeded\",\"deployedBy\":\"" + Harness.Pipeline + "\",\"idempotencyKey\":\"table-write-1\",\"isMissingApproval\":false,\"gateLevelApplied\":\"off\"}")));
        StoredRow record = h.Row("deployment-record", outcome.RowId!);
        Assert.True(record.Flag("isMissingApproval"));
        Assert.Equal("flag", Harness.Text(record, "gateLevelApplied"));
        Assert.True(outcome.HasReason(ReasonCodes.EngineColumnOverwritten));
    }

    // ---- Idempotency on a table write (G-15) and agents ----

    [Fact]
    public void AReplayedWriteReturnsTheFirstResultAndAReusedKeyConflicts()
    {
        Harness h = Harness.Create();
        const string body = "{\"requester\":\"" + Harness.Person1 + "\",\"title\":\"Fixture\",\"need\":\"Fixture need\"}";
        RuntimeOutcome first = Harness.Ok(h.Runtime.Write(Harness.AiClientOf1, WriteCommand.Create("intake-request", body, "key-0001")));
        string dump = h.Store.Dump();

        RuntimeOutcome replay = Harness.Ok(h.Runtime.Write(Harness.AiClientOf1, WriteCommand.Create("intake-request", body, "key-0001")));
        Assert.True(replay.IsReplay);
        Assert.Equal(first.RowId, replay.RowId);
        Assert.Equal(dump, h.Store.Dump());

        RuntimeOutcome reused = h.Runtime.Write(Harness.AiClientOf1, WriteCommand.Create("intake-request", body.Replace("Fixture need", "Other need"), "key-0001"));
        Assert.Equal("conflict", reused.Outcome);
        Assert.True(reused.HasReason(ReasonCodes.RequestIdempotencyKeyReused));
        Assert.Equal(dump, h.Store.Dump());
    }

    [Fact]
    public void AnEngineApiCallReplaysByItsIdempotencyKey()
    {
        Harness h = Harness.Create();
        string decision = h.IntakeDecision(tierTwo: true);
        RuntimeOutcome first = Harness.Ok(h.Runtime.RequestApprovals(Harness.AiClientOf1, decision, "request-0001"));
        string dump = h.Store.Dump();
        RuntimeOutcome replay = Harness.Ok(h.Runtime.RequestApprovals(Harness.AiClientOf1, decision, "request-0001"));
        Assert.True(replay.IsReplay);
        Assert.Equal(RuntimeJsonTestHelpers.Strings(first.Payload, "approvalRecordIds"), RuntimeJsonTestHelpers.Strings(replay.Payload, "approvalRecordIds"));
        Assert.Equal(dump, h.Store.Dump());
    }

    [Fact]
    public void AnAgentIdentityNeverWrites()
    {
        Harness h = Harness.Create();
        RuntimeOutcome outcome = h.Runtime.Write(Product.Engine.Runtime.Callers.CallerIdentity.Workload(Harness.AgentIdentity, PrincipalKind.AgentIdentity),
            WriteCommand.Create("intake-request", "{\"requester\":\"" + Harness.Person1 + "\",\"title\":\"Fixture\",\"need\":\"Fixture need\"}"));
        Assert.Equal("refused", outcome.Outcome);
        Assert.True(outcome.HasReason(ReasonCodes.ActorAgentWriteRefused));
        Assert.Empty(h.Store.Rows("intake-request"));
    }

    // ---- Determinism ----

    // The same calls on two fresh stores leave the same rows, byte for byte (document 25, section 9).
    [Fact]
    public void TheSameCallsLeaveTheSameRows()
    {
        string Run()
        {
            Harness h = Harness.Create();
            string decision = h.IntakeDecision(tierTwo: true);
            string record = h.CountingRecord(Harness.Ok(h.Runtime.RequestApprovals(Harness.AiClientOf1, decision)).Payload);
            Harness.Ok(h.Answer(record, "approval-approved", Harness.Person2));
            Harness.Ok(h.Runtime.RecordDeployment(Harness.BuildPipeline, Report(h, "run-2001-prod")));
            return h.Store.Dump();
        }

        Assert.Equal(Run(), Run());
    }

    // ---- Plan, fetch, evaluate ----

    // OP-03 for an intake decision plans its facts in rounds: the decision, its subject and assessments, the application,
    // its owners and portfolio, then the records. The adapter batches each round.
    [Fact]
    public void ApprovalStatusPlansFetchesAndEvaluatesInAFewRounds()
    {
        Harness h = Harness.Create();
        string decision = h.IntakeDecision(tierTwo: true);
        Harness.Ok(h.Runtime.RequestApprovals(Harness.AiClientOf1, decision));

        RuntimeOutcome status = h.Runtime.GetApprovalStatus(Harness.PersonTwo, decision);
        Assert.Equal("pending", status.Outcome);
        Assert.Equal("pending", RuntimeJsonTestHelpers.Text(status.Payload, "overall"));
        Assert.InRange(status.FactRounds, 3, 6);
        Assert.Equal("required", RuntimeJsonTestHelpers.Text(status.Results["OP-02"], "outcome"));
    }

    private static DeploymentReport Report(Harness h, string key)
    {
        return new DeploymentReport(key, h.ApplicationId, "fixture-solution", "1.0.0", new DateTime(2026, 11, 22, 8, 30, 0, DateTimeKind.Utc))
        {
            TargetExternalId = "fixture-env-prod",
            ArtifactKindKey = "solution",
            RunUrl = "https://pipelines.example.test/runs/1001",
            CodeApprovalKind = CodeApprovalKind.Person,
        };
    }
}
