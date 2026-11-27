using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Operations;
using Product.Engine.Core.Reasons;
using Product.Engine.Runtime.Callers;
using Product.Engine.Runtime.Facts;
using Product.Engine.Runtime.Ports;
using Product.Engine.Runtime.Snapshots;

namespace Product.Engine.Runtime;

public sealed partial class EngineRuntime
{
    // <prefix>_RequestApprovals (document 25, section 11.4): OP-02 for the decision, then the writes. For an entry of the
    // locked list the engine accepts the decision with no card (an accept-decision effect); otherwise it creates the
    // pending Approval Records for the approval runner to send: one per required response, so Minimum Count records per
    // counting requirement, the first step only of a sequential group, and one per consultation (document 23, S1 step 3).
    //
    // WORKING DEFAULT (open design question 8): the engine accepts with no card only what OP-02 returns as engine-accept
    // for an entry of the locked list.
    public RuntimeOutcome RequestApprovals(CallerIdentity caller, string decisionId, string? idempotencyKey = null)
    {
        if (string.IsNullOrEmpty(decisionId))
        {
            throw new ArgumentException("The request names its decision.", nameof(decisionId));
        }

        return Run(caller, SnapshotPurpose.Write, call =>
        {
            JsonTreeNode parameters = RuntimeJson.Object(("api", JsonTreeNode.FromString("request-approvals")), ("decisionId", JsonTreeNode.FromString(decisionId)));
            CallAnswer? replay = ApiReplay(call, idempotencyKey, parameters, out string? requestHash);
            if (replay != null)
            {
                return replay;
            }

            JsonTreeNode payload = RequestApprovalsIn(call, decisionId);
            if (idempotencyKey != null)
            {
                WriteAudit(call, call.Snapshot, "Decision", decisionId, "approvals-requested", payload, idempotencyKey, requestHash);
            }

            return new CallAnswer(RuntimeOutcome.Allowed, decisionId, false, payload);
        });
    }

    // <prefix>_GetApprovalStatus: OP-03 for the decision, with OP-02 recomputed under the decision's stamped version.
    public RuntimeOutcome GetApprovalStatus(CallerIdentity caller, string decisionId)
    {
        return Run(caller, SnapshotPurpose.Read, call =>
        {
            DecisionScope scope = Scope(call, decisionId);
            CompiledSnapshot stamped = StampedSnapshot(call, scope.Decision);
            EvaluationResult<JsonTreeNode?> required = RequiredApprovals(call, scope, stamped);
            if (required.Outcome == "engine-accept")
            {
                return new CallAnswer(required.Outcome, decisionId, false, required.Payload);
            }

            EvaluationResult<JsonTreeNode?> status = ApprovalStatus(call, scope, stamped, required.Payload!, out _);
            return new CallAnswer(status.Outcome, decisionId, false, status.Payload);
        });
    }

    // <prefix>_GetRequiredApprovals for a recorded decision: OP-02 over the decision's scope.
    public RuntimeOutcome GetRequiredApprovals(CallerIdentity caller, string decisionId)
    {
        return Run(caller, SnapshotPurpose.Read, call =>
        {
            DecisionScope scope = Scope(call, decisionId);
            EvaluationResult<JsonTreeNode?> required = RequiredApprovals(call, scope, StampedSnapshot(call, scope.Decision));
            return new CallAnswer(required.Outcome, decisionId, false, required.Payload);
        });
    }

    // <prefix>_GetGateReadiness: OP-07 for an application and a gate, with OP-06 and the tier answers' triggers fetched.
    public RuntimeOutcome GetGateReadiness(CallerIdentity caller, string applicationId, string gateKey)
    {
        return Run(caller, SnapshotPurpose.Read, call =>
        {
            FactSet facts = FactSet.Run(call.Tx, f => ApplicationFacts(f, applicationId));
            call.FactRounds += facts.Rounds;
            StoredRow app = facts.One("application") ?? throw CallFailure.Of(RuntimeOutcome.NotFound, ReasonCodes.RefNotFound, "Business Application " + applicationId + " does not exist.", "Business Application", applicationId);
            var subject = new GateSubject(app, null, app.Text("applicationKind"), app.Text("tier"), DecisionScope.CurrentOwners(facts.All("owners"), call.AsOfUtc), null);
            EvaluationResult<JsonTreeNode?> readiness = GateReadiness(call, subject, gateKey);
            return new CallAnswer(readiness.Outcome, applicationId, false, readiness.Payload);
        });
    }

    private static IEnumerable<FactPlan> ApplicationFacts(FactSet facts, string applicationId)
    {
        yield return FactPlan.Of(FactQuery.ById("application", "business-application", applicationId));
        foreach (FactPlan plan in DecisionScope.ApplicationPlan(facts))
        {
            yield return plan;
        }
    }

    // The request, inside a call: also used by the submit of a configuration draft. again asks a requested decision once
    // more, from the start (ReaskApprovals): its pending records are cancelled first, and a decision with an answer on
    // record is refused, since that answer was given to the question as it stood before.
    private JsonTreeNode RequestApprovalsIn(RuntimeCall call, string decisionId, bool again = false)
    {
        DecisionScope scope = Scope(call, decisionId);
        string? meaning = scope.Decision.Text("statusMeaning");
        if (meaning != "proposed")
        {
            throw CallFailure.Of(OperationOutcomes.Refused, ReasonCodes.DecisionFinal, "The Decision is " + (meaning ?? "without a status") + ", so it takes no approval request.", "Decision", decisionId);
        }

        // Effects check the target's state before writing (document 25, section 8): a decision with records is requested.
        IReadOnlyList<StoredRow> existing = Records(call, decisionId);
        if (again)
        {
            if (existing.Any(r => r.Text("outcomeMeaning") is string outcome && outcome != "pending" && outcome != "cancelled" && outcome != "expired"))
            {
                throw CallFailure.Of(OperationOutcomes.Refused, ReasonCodes.ApprovalOutcomeFinal, "A request on this Decision is answered already, so it is not asked again from the start. Withdraw the decision and propose a new one.", "Decision", decisionId);
            }

            CancelPendingRecords(call, decisionId, null);
            scope = Scope(call, decisionId);
        }
        else if (existing.Count > 0)
        {
            return RuntimeJson.Object(
                ("outcome", JsonTreeNode.FromString("already-requested")),
                ("requestedCount", JsonTreeNode.FromInt64(0)),
                ("approvalRecordIds", RuntimeJson.Strings(existing.Select(r => r.Id))));
        }

        CompiledSnapshot stamped = StampedSnapshot(call, scope.Decision);
        EvaluationResult<JsonTreeNode?> required = RequiredApprovals(call, scope, stamped);
        JsonTreeNode op02 = required.Payload!;
        if (required.Outcome == "engine-accept")
        {
            if (Items(op02, "requirements").Any())
            {
                // Document 25, B25-18: the engine accepts with no card only once the person it would make an owner consents
                // (the business owner an intake names). Their request opens now; the engine accepts when it counts.
                IReadOnlyList<string> consents = OpenApprovalRecords(call, decisionId, ConsentsOnly(op02), null, null);
                call.AddReasons(new[] { ReasonCatalog.Create(ReasonCodes.DecisionConsentWaiting, "Decision", decisionId) });
                return RuntimeJson.Object(
                    ("outcome", JsonTreeNode.FromString(required.Outcome)),
                    ("requestedCount", JsonTreeNode.FromInt64(consents.Count)),
                    ("approvalRecordIds", RuntimeJson.Strings(consents)),
                    ("accepted", JsonTreeNode.False),
                    ("waitingForConsent", JsonTreeNode.True));
            }

            bool accepted = AcceptDecision(call, scope, EngineAcceptance(op02));
            return RuntimeJson.Object(
                ("outcome", JsonTreeNode.FromString(required.Outcome)),
                ("requestedCount", JsonTreeNode.FromInt64(0)),
                ("approvalRecordIds", JsonTreeNode.CreateArray()),
                ("accepted", JsonTreeNode.FromBoolean(accepted)));
        }

        IReadOnlyList<string> ids = OpenApprovalRecords(call, decisionId, op02, null, null);
        return RuntimeJson.Object(
            ("outcome", JsonTreeNode.FromString(required.Outcome)),
            ("requestedCount", JsonTreeNode.FromInt64(ids.Count)),
            ("approvalRecordIds", RuntimeJson.Strings(ids)));
    }

    // The recompute-approvals effect (document 23, S1 steps 8 and 9; document 26, section 5, step 7), also run by the
    // approval refresh: pending records past Expires On get the expired outcome first; then OP-03 for the decision and
    // Counts Toward Requirement written per record. When satisfied the decision is accepted (G-17), and when the approvals
    // are rejected it is rejected; either way the records still pending are cancelled, so no card stays open for a settled
    // decision. While the approvals are pending or expired, the engine opens what the requirements still need: a restart
    // after an expired record or an answer that does not count, and the next step of a sequential group.
    private void RecomputeApprovals(RuntimeCall call, string decisionId)
    {
        DecisionScope scope = Scope(call, decisionId);
        if (scope.Decision.Text("statusMeaning") != "proposed")
        {
            return;
        }

        CompiledSnapshot stamped = StampedSnapshot(call, scope.Decision);
        EvaluationResult<JsonTreeNode?> required = RequiredApprovals(call, scope, stamped);
        bool engineAccept = required.Outcome == "engine-accept";
        if (engineAccept && !Items(required.Payload!, "requirements").Any())
        {
            return;
        }

        // An engine-accepted decision that waits for a consent (B25-18) counts only the consents; the engine accepts it on
        // its own basis once they are given.
        JsonTreeNode requiredPayload = engineAccept ? ConsentsOnly(required.Payload!) : required.Payload!;
        ExpireDueRecords(call, Records(call, decisionId));
        EvaluationResult<JsonTreeNode?> status = ApprovalStatus(call, scope, stamped, requiredPayload, out IReadOnlyList<StoredRow> records);
        JsonTreeNode counts = status.Payload != null && status.Payload.TryGetMember("countsByRecord", out JsonTreeNode? c) ? c : RuntimeJson.Empty;
        foreach (StoredRow record in records)
        {
            bool countsNow = RuntimeJson.Flag(counts, record.Id);
            if (record.Flag("countsTowardRequirement") != countsNow)
            {
                GuardedWrite(call, new WriteSpec("approval-record", WriteOperation.Update)
                {
                    RowId = record.Id,
                    After = RuntimeJson.Object(("countsTowardRequirement", JsonTreeNode.FromBoolean(countsNow))),
                    Nested = true,
                    Effect = EffectKind.RecomputeApprovals,
                });
            }
        }

        switch (status.Outcome)
        {
            case "satisfied":
                if (AcceptDecision(call, scope, engineAccept ? EngineAcceptance(required.Payload!) : new WriteAcceptance(WriteAcceptance.ApprovalsSatisfied, approvalStatusOutcome: "satisfied")))
                {
                    CancelPendingRecords(call, decisionId, EffectKind.RecomputeApprovals);
                }

                break;
            case "rejected":
                // Decided On is when the decision was accepted or rejected (document 22, O09), so the rejection stamps it
                // as the acceptance does.
                GuardedWrite(call, new WriteSpec("decision", WriteOperation.Update)
                {
                    RowId = decisionId,
                    After = RuntimeJson.Object(
                        ("status", JsonTreeNode.FromString(DefaultStatus(call.Snapshot, "decision", "rejected"))),
                        ("decidedOn", JsonTreeNode.FromUtcDateTime(call.AsOfUtc))),
                    Nested = true,
                    Effect = EffectKind.RecomputeApprovals,
                });
                CancelPendingRecords(call, decisionId, EffectKind.RecomputeApprovals);
                break;
            default:
                OpenApprovalRecords(call, decisionId, requiredPayload, status.Payload, EffectKind.RecomputeApprovals);
                break;
        }
    }

    // The engine's own acceptance for an entry of the locked list, from OP-02's acceptance basis.
    private static WriteAcceptance EngineAcceptance(JsonTreeNode op02)
    {
        JsonTreeNode basis = op02.TryGetMember("acceptanceBasis", out JsonTreeNode? b) ? b : RuntimeJson.Empty;
        EngineAcceptanceCondition condition = EngineContract.EngineAcceptanceConditions.Parse(RuntimeJson.Text(basis, "condition") ?? string.Empty);
        return new WriteAcceptance(WriteAcceptance.EngineAccept, requiredApprovalsOutcome: "engine-accept", condition: condition);
    }

    // OP-02's payload with its consultations left out: an engine-accepted decision that waits for a consent opens and
    // counts only the consent (B25-18), as the engine's fast path opens no consultation.
    private static JsonTreeNode ConsentsOnly(JsonTreeNode op02)
    {
        return RuntimeJson.With(op02, "consultations", JsonTreeNode.CreateArray());
    }

    // The accept-decision write (G-17), with the OP-07 payload when the decision type is a gate's (G-14). While the gate
    // is not ready the decision stays proposed and the call goes on: the approval answer that triggered it is kept.
    private bool AcceptDecision(RuntimeCall call, DecisionScope scope, WriteAcceptance acceptance)
    {
        JsonTreeNode? readiness = null;
        string? gate = GateOf(call.Snapshot, scope.DecisionTypeKey);
        if (gate != null)
        {
            var subject = new GateSubject(scope.Application, scope.Intake, scope.ApplicationKindKey, scope.TierKey, scope.Owners, scope.LatestAssessment);
            readiness = GateReadiness(call, subject, gate).Payload;
        }

        // The subject's facts for the update-subject effect, only when the gate lets the decision through: the new row IDs
        // are taken from the store, and a decision that stays proposed takes none.
        bool gateHolds = readiness != null && readiness.TryGetMember("ready", out JsonTreeNode? ready) && ready.Kind == JsonTreeKind.False;
        WriteStep? step = GuardedWrite(call, new WriteSpec("decision", WriteOperation.Update)
        {
            RowId = scope.DecisionId,
            After = RuntimeJson.Object(("status", JsonTreeNode.FromString(DefaultStatus(call.Snapshot, "decision", "accepted")))),
            Nested = true,
            Effect = EffectKind.AcceptDecision,
            Acceptance = acceptance,
            GateReadiness = readiness,
            Subject = gateHolds ? null : SubjectFactsOf(call, scope),
            Tolerated = new[] { ReasonCodes.GateNotReady },
        });

        // An accepted decision can be what another decision's gate waited for, such as the data-use decision a design
        // gate needs: the application's held gate decisions settle now, in the same call.
        if (step != null && scope.Application != null)
        {
            SettleHeldGateDecisions(call, new[] { scope.Application.Id });
        }

        return step != null;
    }

    // A gate's decision whose approvals are all in stays proposed while its gate is not ready (G-14). It settles once the
    // gate turns ready (wave 3 verification, major: a held design decision stayed proposed after the data-use decision
    // its gate waited for was accepted). The engine looks again at an application's held gate decisions when another
    // decision of the application is accepted, at the end of each write call that touched the application's rows, and in
    // the daily due-state refresh. A decision that was requested (it holds an Approval Record), whose approvals OP-03
    // finds satisfied and whose gate OP-07 finds ready, is accepted by the recompute, exactly as the last answer would
    // have accepted it. A decision nobody requested yet is left for its proposer to submit.
    private void SettleHeldGateDecisions(RuntimeCall call, IEnumerable<string> applicationIds)
    {
        if (call.SettlingGates)
        {
            return;
        }

        call.SettlingGates = true;
        try
        {
            foreach (string appId in applicationIds.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id => id, StringComparer.Ordinal).ToList())
            {
                // Accepting one can make the next one's gate ready, so the step runs until a round accepts nothing.
                for (int round = 0; round < 8; round++)
                {
                    List<string> settles = HeldGateCandidates(call, appId).Where(d => HeldGateDecisionSettles(call, d)).Select(d => d.Id).ToList();
                    if (settles.Count == 0)
                    {
                        break;
                    }

                    foreach (string decisionId in settles)
                    {
                        RecomputeApprovals(call, decisionId);
                    }
                }
            }
        }
        finally
        {
            call.SettlingGates = false;
        }
    }

    // The proposed decisions of a gate's decision type: of one application, or of every application when applicationId is
    // null. In ID order.
    private static List<StoredRow> HeldGateCandidates(RuntimeCall call, string? applicationId)
    {
        RowQuery query = RowQuery.For("decision").Where("statusMeaning", "proposed");
        if (applicationId != null)
        {
            query = query.Where("subjectApplication", applicationId);
        }

        return call.Tx.Query(query)
            .Where(d => d.Text("subjectApplication") != null && d.Text("decisionType") is string type && GateOf(call.Snapshot, type) != null)
            .OrderBy(d => d.Id, StringComparer.Ordinal)
            .ToList();
    }

    // True when a proposed gate decision would be accepted now: it was requested, OP-03 finds its approvals satisfied and
    // OP-07 finds its gate ready. A read: the reasons these operations gather are dropped, so a call that settles nothing
    // says nothing about it.
    private bool HeldGateDecisionSettles(RuntimeCall call, StoredRow decision)
    {
        if (Records(call, decision.Id).Count == 0)
        {
            return false;
        }

        int mark = call.Reasons.Count;
        try
        {
            DecisionScope scope = Scope(call, decision.Id);
            string? gate = GateOf(call.Snapshot, scope.DecisionTypeKey);
            if (gate == null || scope.ApplicationKindKey == null)
            {
                return false;
            }

            CompiledSnapshot stamped = StampedSnapshot(call, scope.Decision);
            EvaluationResult<JsonTreeNode?> required = RequiredApprovals(call, scope, stamped);
            bool engineAccept = required.Outcome == "engine-accept";
            if (engineAccept && !Items(required.Payload!, "requirements").Any())
            {
                return false;
            }

            EvaluationResult<JsonTreeNode?> status = ApprovalStatus(call, scope, stamped, engineAccept ? ConsentsOnly(required.Payload!) : required.Payload!, out _);
            if (status.Outcome != "satisfied")
            {
                return false;
            }

            var subject = new GateSubject(scope.Application, scope.Intake, scope.ApplicationKindKey, scope.TierKey, scope.Owners, scope.LatestAssessment);
            JsonTreeNode? readiness = GateReadiness(call, subject, gate).Payload;
            return readiness != null && readiness.TryGetMember("ready", out JsonTreeNode? ready) && ready.Kind == JsonTreeKind.True;
        }
        catch (CallFailure)
        {
            return false;
        }
        finally
        {
            if (call.Reasons.Count > mark)
            {
                call.Reasons.RemoveRange(mark, call.Reasons.Count - mark);
            }
        }
    }

    // The applications whose rows a call wrote, for the settle step at the end of a write call: the rows a gate reads
    // (decisions, reviews, document links, owner assignments, tier assessments, data uses and the application itself).
    private static IReadOnlyList<string> GateFactApplications(RuntimeCall call)
    {
        var ids = new List<string>();
        foreach (AppliedWrite write in call.Writes.ToList())
        {
            if (write.Operation == WriteOperation.Delete || Array.IndexOf(GateFactEntities, write.Entity) < 0)
            {
                continue;
            }

            StoredRow? row = call.Tx.Get(write.Entity, write.RowId);
            string? appId = write.Entity == "business-application" ? write.RowId : row?.Text("businessApplication") ?? row?.Text("subjectApplication");
            if (appId != null && !ids.Contains(appId, StringComparer.OrdinalIgnoreCase))
            {
                ids.Add(appId);
            }
        }

        return ids;
    }

    private static readonly string[] GateFactEntities = { "business-application", "data-use", "decision", "document-link", "owner-assignment", "review", "tier-assessment" };

    // What the update-subject effect reads (document 25, B25-8): the decision's subject and its application as the store
    // holds them, the application's current owners, the latest tier assessment for a tier decision, the due reviews for
    // a move to a retired stage, and the IDs the rows it creates take.
    private static SubjectFacts? SubjectFactsOf(RuntimeCall call, DecisionScope scope)
    {
        StoredRow decision = scope.Decision;
        string type = scope.DecisionTypeKey;
        string? outcome = decision.Text("outcome");
        if (outcome == null || scope.SubjectKind == "configuration-draft")
        {
            return null;
        }

        StoredRow? app = scope.Application;
        bool tier = type == EngineKeys.TierDecisionType && app != null && scope.SubjectKind != "intake-request";
        bool retiring = app != null && RetiringOutcome(call.Snapshot, type, outcome);
        FactSet facts = FactSet.Run(call.Tx, f => SubjectFactsPlan(f, app, tier, retiring));
        call.FactRounds += facts.Rounds;

        StoredRow? assessment = scope.SubjectKind == "intake-request" ? scope.LatestAssessment
            : !tier ? null
            : LatestAssessment(facts);

        // New rows, in the order the effect takes their IDs: the application an accepted intake creates, then the business
        // owner assignment of the person the intake names; each owner assignment an ownership decision starts.
        var newRows = new List<string>();
        bool consented = false;
        if (type == EngineKeys.IntakeDecisionType && outcome == EngineKeys.IntakeAcceptOutcome && scope.Intake != null)
        {
            if (scope.Intake.Text("businessApplication") == null)
            {
                newRows.Add("business-application");
            }

            if (scope.Intake.Text("businessOwner") is string named)
            {
                consented = IntakeOwnerConsented(call, scope.Intake, named);
                if (consented)
                {
                    newRows.Add("owner-assignment");
                }
            }
        }
        else if (type == EngineKeys.OwnershipDecisionType && decision.Column("proposedChangesJson") is JsonTreeNode proposal
            && proposal.Kind == JsonTreeKind.Object && proposal.TryGetMember("owners", out JsonTreeNode? owners) && owners.Kind == JsonTreeKind.Array)
        {
            newRows.AddRange(owners.Items.Where(o => RuntimeJson.Text(o, "addObjectId") != null).Select(_ => "owner-assignment"));
        }

        List<string> ids = newRows.Select(entity => call.Tx.NewId(entity)).ToList();

        return new SubjectFacts(
            RowFact(app),
            RowFact(scope.Intake),
            RowFact(scope.Change),
            RowFact(assessment),
            (type == EngineKeys.OwnershipDecisionType && (scope.SubjectKind == EngineKeys.DataDomainSubjectKind || scope.SubjectKind == EngineKeys.DataLocationSubjectKind)
                ? scope.DataSubjectOwners
                : scope.Owners).Select(o => RowFact(o)!),
            facts.All("due-reviews").Select(r => r.Id).OrderBy(r => r, StringComparer.Ordinal),
            ids,
            consented);
    }

    // True when the business owner an intake names accepted the role (document 25, B25-18): an Approval Record on a
    // decision about the intake asks them by name for the business owner role, holds approved, and counts. The Accept gate
    // counts them, and the update-subject effect assigns them, only then.
    private static bool IntakeOwnerConsented(RuntimeCall call, StoredRow intake, string named)
    {
        FactSet facts = FactSet.Run(call.Tx, f => IntakeConsentPlan(f, intake.Id));
        call.FactRounds += facts.Rounds;
        int decisions = facts.All("intake-all-decisions").Count;
        return Enumerable.Range(0, decisions).SelectMany(i => facts.All("intake-consent-" + i)).Any(r =>
            string.Equals(r.Text("askedPrincipal"), named, StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.Text("requiredOwnerRole"), EngineKeys.BusinessOwnerRole, StringComparison.Ordinal)
            && r.Text("outcomeMeaning") == "approved"
            && r.Flag("countsTowardRequirement"));
    }

    private static IEnumerable<FactPlan> IntakeConsentPlan(FactSet facts, string intakeId)
    {
        yield return FactPlan.Of(FactQuery.Where("intake-all-decisions", RowQuery.For("decision").Where("subjectIntakeRequest", intakeId)));
        IReadOnlyList<StoredRow> decisions = facts.All("intake-all-decisions");
        if (decisions.Count > 0)
        {
            yield return FactPlan.Of(decisions.Select((d, i) => FactQuery.Where("intake-consent-" + i, RowQuery.For("approval-record").Where("decision", d.Id))).ToArray());
        }
    }

    private static IEnumerable<FactPlan> SubjectFactsPlan(FactSet facts, StoredRow? app, bool tier, bool retiring)
    {
        yield return FactPlan.Of(
            tier ? FactQuery.Where("app-assessments", RowQuery.For("tier-assessment").Where("businessApplication", app!.Id)) : null,
            tier ? FactQuery.Where("app-intakes", RowQuery.For("intake-request").Where("businessApplication", app!.Id)) : null,
            retiring ? FactQuery.Where("due-reviews", RowQuery.For("review").Where("businessApplication", app!.Id).Where("statusMeaning", "due")) : null);
        IReadOnlyList<StoredRow> intakes = facts.All("app-intakes");
        if (intakes.Count > 0)
        {
            yield return FactPlan.Of(intakes.Select((intake, i) => FactQuery.Where("intake-assessments-" + i, RowQuery.For("tier-assessment").Where("intakeRequest", intake.Id))).ToArray());
        }
    }

    // True when the outcome's C19 Next Lifecycle Stage means retired (document 23, P09 step 7).
    private static bool RetiringOutcome(CompiledSnapshot snapshot, string type, string outcome)
    {
        if (!snapshot.Snapshot.TryGetEntity("decision-outcome", out Core.Snapshots.SnapshotEntity? outcomes)
            || !snapshot.Snapshot.TryGetEntity("lifecycle-stage", out Core.Snapshots.SnapshotEntity? stages))
        {
            return false;
        }

        Core.Snapshots.SnapshotRow? row = outcomes.Rows.FirstOrDefault(r => r.KeyPath == type + "/" + outcome);
        string? next = row != null && row.TryGetColumn("nextLifecycleStageKey", out JsonTreeNode? value) && value.Kind == JsonTreeKind.String ? value.GetString() : null;
        return next != null && stages.Rows.Any(s => s.KeyPath == next && s.EngineMeaning == "retired");
    }

    // A stored row as the core reads it: its columns with its ID.
    private static JsonTreeNode? RowFact(StoredRow? row)
    {
        return row == null ? null : RuntimeJson.With(row.Columns, "id", JsonTreeNode.FromString(row.Id));
    }

    private DecisionScope Scope(RuntimeCall call, string decisionId)
    {
        FactSet facts = FactSet.Run(call.Tx, f => DecisionScope.Plan(decisionId, f));
        call.FactRounds += facts.Rounds;
        DecisionScope scope = DecisionScope.Build(facts, call.AsOfUtc)
            ?? throw CallFailure.Of(RuntimeOutcome.NotFound, ReasonCodes.RefNotFound, "Decision " + decisionId + " does not exist.", "Decision", decisionId);
        if (scope.Application != null && scope.SubjectKind != "configuration-draft")
        {
            if (scope.DecisionTypeKey == EngineKeys.TierDecisionType)
            {
                scope.UseTier(TierAtStake(call, scope));
            }
            else if (scope.SubjectKind == "intake-request")
            {
                scope.UseTier(Highest(call.Snapshot, scope.Application.Text("tier"), scope.LatestAssessment?.Text("computedTier")));
            }
        }

        // The data taxonomy's scope fields and owner facts (EngineRuntime.Data; docs/architecture/data-taxonomy.md, Who answers and Approval rules).
        ApplyDataScope(call, scope);
        return scope;
    }

    // The tier at stake for a tier decision about an application (document 25, B25-14; P02 steps 4 and 5): the highest
    // ranked of the tier the application has, the tier the decision names (Proposed Changes Json tierKey) and the tier its
    // latest answers computed. So raising an application to tier 3, confirming a computed tier 3, and lowering it from
    // tier 3 all take the tier 3 approvals and separation rules, never the lower tier's.
    private static string? TierAtStake(RuntimeCall call, DecisionScope scope)
    {
        StoredRow app = scope.Application!;
        var candidates = new List<string>();
        if (app.Text("tier") is string current)
        {
            candidates.Add(current);
        }

        if (scope.Decision.Column("proposedChangesJson") is JsonTreeNode proposal && proposal.Kind == JsonTreeKind.Object && RuntimeJson.Text(proposal, "tierKey") is string named)
        {
            candidates.Add(named);
        }

        FactSet facts = FactSet.Run(call.Tx, f => SubjectFactsPlan(f, app, true, false));
        call.FactRounds += facts.Rounds;
        if (LatestAssessment(facts)?.Text("computedTier") is string computed)
        {
            candidates.Add(computed);
        }

        return Highest(call.Snapshot, candidates.ToArray());
    }

    // The highest ranked of the tiers given (nulls skipped), or null when none is given.
    private static string? Highest(CompiledSnapshot snapshot, params string?[] tiers)
    {
        string? best = null;
        long bestRank = long.MinValue;
        foreach (string? key in tiers)
        {
            if (key == null)
            {
                continue;
            }

            long rank = TierRank(snapshot, key) ?? long.MinValue;
            if (best == null || rank > bestRank)
            {
                best = key;
                bestRank = rank;
            }
        }

        return best;
    }

    // C08 Rank of an active tier, or null.
    private static long? TierRank(CompiledSnapshot snapshot, string tierKey)
    {
        if (!snapshot.Snapshot.TryGetEntity("tier", out Core.Snapshots.SnapshotEntity? tiers))
        {
            return null;
        }

        Core.Snapshots.SnapshotRow? row = tiers.ActiveRows.FirstOrDefault(r => r.KeyPath == tierKey);
        return row != null && row.TryGetColumn("rank", out JsonTreeNode? rank) && rank.TryGetInt64(out long value) ? value : null;
    }

    // The latest computed tier assessment among the application's own and its intake requests' (as SubjectFactsPlan fetched
    // them).
    private static StoredRow? LatestAssessment(FactSet facts)
    {
        return facts.All("app-assessments").Concat(Enumerable.Range(0, facts.All("app-intakes").Count).SelectMany(i => facts.All("intake-assessments-" + i)))
            .Where(a => a.Text("computedTier") != null)
            .OrderBy(a => a.Text("engineCheckedOn") ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(a => a.Id, StringComparer.Ordinal)
            .LastOrDefault();
    }

    // The version a row was stamped with (Configuration Set Version on create). OP-03 recomputes OP-02 with the
    // decision's stamped version (document 25, section 5.5), so a later version never changes a running decision's bar.
    private CompiledSnapshot StampedSnapshot(RuntimeCall call, StoredRow row)
    {
        long? stamped = row.Number("configurationSetVersion");
        if (!stamped.HasValue || stamped.Value == call.Snapshot.VersionNumber)
        {
            return call.Snapshot;
        }

        SnapshotLoad load = Cache.LoadVersion(_configuration, call.Snapshot.ConfigurationSetKey, (int)stamped.Value);
        return load.Snapshot ?? throw new CallFailure(OperationOutcomes.Unavailable, new[] { load.Problem! });
    }

    private static EvaluationResult<JsonTreeNode?> RequiredApprovals(RuntimeCall call, DecisionScope scope, CompiledSnapshot snapshot)
    {
        JsonTreeNode input = scope.RequiredApprovalsInput();

        // A decision stamped with a version that lacks a data scope key (one from before the data taxonomy's rows) is
        // matched without it, so its approvals stay those of the version it was proposed under.
        foreach ((string field, string entity) in new[] { ("classificationKey", "data-classification"), ("dataDomainLevelKey", "data-domain-level") })
        {
            string? key = RuntimeJson.Text(input, field);
            if (key != null && !snapshot.Snapshot.TryGetRow(entity, key, out _))
            {
                input = RuntimeJson.Without(input, new[] { field });
            }
        }

        JsonTreeNode? standardChange = StandardChangeFact(call, scope, snapshot);
        if (standardChange != null)
        {
            input = RuntimeJson.With(input, "standardChange", standardChange);
        }

        EvaluationResult<JsonTreeNode?> result = Evaluate(call, EngineOperation.ResolveRequiredApprovals, snapshot, input);
        if (!IsAnswer(EngineOperation.ResolveRequiredApprovals, result.Outcome))
        {
            throw CallFailure.From(result);
        }

        call.AddReasons(result.Reasons);
        return WithProposedOwners(result, scope);
    }

    // OP-12 for a change authorization whose change type requires a standard change model (document 25, sections 5.4 and
    // 5.13; B25-14): the facts the model conditions test (the application's tier rank, the planned target's stage, the
    // main application plus its affected applications, the component kinds it holds, the window), and each active model
    // of the type with the status of the decision that approved it. OP-02 then accepts the change through the model, or
    // refuses it as not standard. Null for any other decision.
    private static JsonTreeNode? StandardChangeFact(RuntimeCall call, DecisionScope scope, CompiledSnapshot snapshot)
    {
        StoredRow? change = scope.Change;
        string? changeType = scope.ChangeTypeKey;
        if (change == null || changeType == null || !snapshot.Snapshot.TryGetEntity("change-type", out Core.Snapshots.SnapshotEntity? types))
        {
            return null;
        }

        Core.Snapshots.SnapshotRow? type = types.ActiveRows.FirstOrDefault(r => r.KeyPath == changeType);
        if (type == null || !type.TryGetColumn("requiresStandardModel", out JsonTreeNode? flag) || flag.Kind != JsonTreeKind.True)
        {
            return null;
        }

        List<Core.Snapshots.SnapshotRow> models = snapshot.Snapshot.TryGetEntity("standard-change-model", out Core.Snapshots.SnapshotEntity? modelEntity)
            ? modelEntity.ActiveRows.Where(m => m.TryGetColumn("changeTypeKey", out JsonTreeNode? k) && k.Kind == JsonTreeKind.String && k.GetString() == changeType).OrderBy(m => m.KeyPath, StringComparer.Ordinal).ToList()
            : new List<Core.Snapshots.SnapshotRow>();
        string? Approving(Core.Snapshots.SnapshotRow model) => model.TryGetColumn("approvedByDecisionId", out JsonTreeNode? d) && d.Kind == JsonTreeKind.String ? d.GetString() : null;

        string? appId = scope.Application?.Id;
        FactSet facts = FactSet.Run(call.Tx, f => new[]
        {
            FactPlan.Of(new[]
            {
                FactQuery.Where("affected", RowQuery.For("affected-application").Where("changeRequest", change.Id)),
                appId == null ? null : FactQuery.Where("components", RowQuery.For("technical-component").Where("businessApplication", appId)),
            }.Concat(models.Select(m => Approving(m)).Where(d => d != null).Distinct(StringComparer.Ordinal).Select(d => FactQuery.ById("approving " + d, "decision", d!))).ToArray()),
        });
        call.FactRounds += facts.Rounds;

        int affected = 1 + facts.All("affected").Count(a => !string.Equals(a.Text("businessApplication"), appId, StringComparison.OrdinalIgnoreCase));
        string[] kinds = facts.All("components").Where(c => c.Text("retiredOn") == null).Select(c => c.Text("componentKind")).Where(k => k != null).Select(k => k!)
            .Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        DateTime? start = RuntimeJson.Utc(change.Columns, "windowStart");
        DateTime? end = RuntimeJson.Utc(change.Columns, "windowEnd");
        long? rank = scope.TierKey == null ? null : TierRank(snapshot, scope.TierKey);
        JsonTreeNode input = RuntimeJson.Object(
            ("changeTypeKey", JsonTreeNode.FromString(changeType)),
            ("standardChangeModelKey", RuntimeJson.OptionalText(change.Text("standardChangeModel"))),
            ("change", RuntimeJson.Object(
                ("tierRank", rank.HasValue ? JsonTreeNode.FromInt64(rank.Value) : null),
                ("targetStageKeys", scope.TargetStageKey == null ? null : RuntimeJson.Strings(new[] { scope.TargetStageKey })),
                ("affectedApplicationCount", JsonTreeNode.FromInt64(affected)),
                ("componentKindKeys", kinds.Length == 0 ? null : RuntimeJson.Strings(kinds)),
                ("windowMinutes", start.HasValue && end.HasValue ? JsonTreeNode.FromInt64((long)Math.Max(0, (end.Value - start.Value).TotalMinutes)) : null))),
            ("models", JsonTreeNode.CreateArray(models.Select(m => RuntimeJson.Object(
                ("modelKey", JsonTreeNode.FromString(m.KeyPath)),
                ("approvedByDecisionStatus", JsonTreeNode.FromString(Approving(m) is string d ? facts.One("approving " + d)?.Text("statusMeaning") ?? "proposed" : "proposed")))))));
        EvaluationResult<JsonTreeNode?> result = Evaluate(call, EngineOperation.EvaluateStandardChange, snapshot, input);
        if (!IsAnswer(EngineOperation.EvaluateStandardChange, result.Outcome) || result.Payload == null)
        {
            throw CallFailure.From(result);
        }

        call.AddReasons(result.Reasons.Where(r => r.Severity != ReasonSeverity.Error));
        return result.Payload;
    }

    // A requirement that asks the proposed owners (C32 Approver Proposed Owners; document 25, B25-14) needs one approval
    // from each person the decision proposes to make an owner, so its Minimum Count becomes that number, and one person
    // counts once (OP-03). A decision that names nobody new skips the requirement.
    private static EvaluationResult<JsonTreeNode?> WithProposedOwners(EvaluationResult<JsonTreeNode?> result, DecisionScope scope)
    {
        JsonTreeNode? payload = result.Payload;
        bool touched(JsonTreeNode r) => RuntimeJson.Text(r, "ownerSubject") == "touched-data-domains";
        if (payload == null || !Items(payload, "requirements").Any(r => RuntimeJson.Flag(r, "approverProposedOwners") || touched(r)))
        {
            return result;
        }

        int named = scope.ProposedOwnerPrincipals().Count;
        var requirements = new List<JsonTreeNode>();
        foreach (JsonTreeNode requirement in Items(payload, "requirements"))
        {
            if (touched(requirement))
            {
                // One approval from each acting data owner of the domains the draft touches (docs/architecture/data-taxonomy.md, Approval rules).
                int owners = TouchedOwners(scope, RuntimeJson.Text(requirement, "approverOwnerRoleKey") ?? EngineKeys.DataOwnerRole).Count;
                requirements.Add(RuntimeJson.With(requirement, "minimumCount", JsonTreeNode.FromInt64(Math.Max(1, owners))));
            }
            else if (!RuntimeJson.Flag(requirement, "approverProposedOwners"))
            {
                requirements.Add(requirement);
            }
            else if (named > 0)
            {
                requirements.Add(RuntimeJson.With(requirement, "minimumCount", JsonTreeNode.FromInt64(named)));
            }
        }

        JsonTreeNode adjusted = RuntimeJson.With(payload, "requirements", JsonTreeNode.CreateArray(requirements));
        return new EvaluationResult<JsonTreeNode?>(result.Outcome, adjusted, result.Reasons, result.Trace, result.ConfigurationVersion, result.SnapshotHash, result.EngineVersion);
    }

    private static EvaluationResult<JsonTreeNode?> ApprovalStatus(RuntimeCall call, DecisionScope scope, CompiledSnapshot snapshot, JsonTreeNode requiredPayload, out IReadOnlyList<StoredRow> records)
    {
        records = Records(call, scope.DecisionId);
        JsonTreeNode input = RuntimeJson.Object(
            ("requiredApprovals", JsonTreeNode.CreateArray(Items(requiredPayload, "requirements").Concat(Items(requiredPayload, "consultations")))),
            ("records", JsonTreeNode.CreateArray(records.Select(ApprovalFact).Where(f => f != null).Select(f => f!))),
            ("scope", RuntimeJson.Object(
                ("decisionTypeKey", JsonTreeNode.FromString(scope.DecisionTypeKey)),
                ("tierKey", RuntimeJson.OptionalText(scope.TierKey)),
                ("targetStageKey", RuntimeJson.OptionalText(scope.TargetStageKey)))),
            ("parties", scope.SeparationParties()),
            ("acceptedExceptionDecisionTypeKeys", JsonTreeNode.CreateArray()));
        EvaluationResult<JsonTreeNode?> result = Evaluate(call, EngineOperation.EvaluateApprovalStatus, snapshot, input);
        if (!IsAnswer(EngineOperation.EvaluateApprovalStatus, result.Outcome))
        {
            throw CallFailure.From(result);
        }

        call.AddReasons(result.Reasons);
        return result;
    }

    private static IReadOnlyList<StoredRow> Records(RuntimeCall call, string decisionId)
    {
        FactSet facts = FactSet.Run(call.Tx, f => new[] { FactPlan.Of(FactQuery.Where("records", RowQuery.For("approval-record").Where("decision", decisionId))) });
        call.FactRounds += facts.Rounds;
        return facts.All("records");
    }

    // One ApprovalFact (document 25, section 5.5) from an O10 row. The approval runner's record states whether the
    // responder held the required role when answering (Approver Holds Required Role, a column this build proposes:
    // document 22 has no column for it).
    private static JsonTreeNode? ApprovalFact(StoredRow record)
    {
        string? ruleKey = record.Text("approvalRule") ?? record.Text("decisionRight");
        string? outcome = record.Text("outcomeMeaning");
        if (ruleKey == null || outcome == null)
        {
            return null;
        }

        return RuntimeJson.Object(
            ("approvalRecordId", JsonTreeNode.FromString(record.Id)),
            ("ruleKey", JsonTreeNode.FromString(ruleKey)),
            ("outcome", JsonTreeNode.FromString(outcome)),
            ("responderPrincipalId", RuntimeJson.OptionalText(record.Text("approver"))),
            ("responderActorKind", RuntimeJson.OptionalText(record.Text("approverActorMeaning"))),
            ("responderPrincipalKind", RuntimeJson.OptionalText(record.Text("approverPrincipalKind"))),
            ("responderHoldsRequiredRole", JsonTreeNode.FromBoolean(record.Flag("approverHoldsRequiredRole"))),
            ("hasTypedReason", JsonTreeNode.FromBoolean(record.Text("typedReason") != null)),
            ("respondedOnUtc", RuntimeJson.OptionalText(record.Text("respondedOn"))),
            ("expiresOnUtc", RuntimeJson.OptionalText(record.Text("expiresOn"))));
    }

    // The gate whose Gate Decision Type is this decision type (G-14), first by key when several.
    private static string? GateOf(CompiledSnapshot snapshot, string decisionTypeKey)
    {
        if (!snapshot.Snapshot.TryGetEntity("gate", out Core.Snapshots.SnapshotEntity? gates))
        {
            return null;
        }

        return gates.ActiveRows
            .Where(g => g.TryGetColumn("gateDecisionTypeKey", out JsonTreeNode? t) && t.Kind == JsonTreeKind.String && t.GetString() == decisionTypeKey)
            .Select(g => g.KeyPath)
            .OrderBy(k => k, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    // OP-07 for a subject (document 25, section 5.9), with its facts: accepted decision types of the application and the
    // intake, done review types, current owner roles, the OP-06 result and the tier answers' triggered requirements.
    private EvaluationResult<JsonTreeNode?> GateReadiness(RuntimeCall call, GateSubject subject, string gateKey)
    {
        if (subject.ApplicationKindKey == null)
        {
            throw CallFailure.Of(OperationOutcomes.InvalidInput, ReasonCodes.InputRequired, "Gate " + gateKey + " needs the subject's application kind, and the subject has none.", "Gate", gateKey);
        }

        string? appId = subject.Application?.Id;
        string? intakeId = subject.Intake?.Id;
        FactSet facts = FactSet.Run(call.Tx, f => new[]
        {
            FactPlan.Of(
                appId == null ? null : FactQuery.Where("app-decisions", RowQuery.For("decision").Where("subjectApplication", appId).Where("statusMeaning", "accepted")),
                intakeId == null ? null : FactQuery.Where("intake-decisions", RowQuery.For("decision").Where("subjectIntakeRequest", intakeId).Where("statusMeaning", "accepted")),
                appId == null ? null : FactQuery.Where("reviews", RowQuery.For("review").Where("businessApplication", appId).Where("statusMeaning", "done")),
                appId == null ? null : FactQuery.Where("links", RowQuery.For("document-link").Where("businessApplication", appId))),
        });
        call.FactRounds += facts.Rounds;

        IEnumerable<string> accepted = facts.All("app-decisions").Concat(facts.All("intake-decisions")).Select(d => d.Text("decisionType")).Where(t => t != null).Select(t => t!);
        IEnumerable<string> reviews = facts.All("reviews").Select(r => r.Text("reviewType")).Where(t => t != null).Select(t => t!);
        JsonTreeNode? documentation = Documentation(call, subject, facts.All("links"));
        IReadOnlyList<string> triggered = TriggeredRequirements(call, subject);

        JsonTreeNode input = RuntimeJson.Object(
            ("gateKey", JsonTreeNode.FromString(gateKey)),
            ("tierKey", RuntimeJson.OptionalText(subject.TierKey)),
            ("applicationKindKey", JsonTreeNode.FromString(subject.ApplicationKindKey)),
            ("acceptedDecisionTypeKeys", RuntimeJson.Strings(accepted.Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal))),
            ("documentation", documentation),
            ("doneReviewTypeKeys", RuntimeJson.Strings(reviews.Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal))),
            ("assignedOwnerRoleKeys", RuntimeJson.Strings(AssignedOwnerRoles(call, subject))),
            ("triggeredGateRequirementKeys", RuntimeJson.Strings(triggered)),
            ("dataUses", DataUseCounts(call, subject.Application)));
        EvaluationResult<JsonTreeNode?> result = Evaluate(call, EngineOperation.EvaluateGateReadiness, call.Snapshot, input);
        if (!IsAnswer(EngineOperation.EvaluateGateReadiness, result.Outcome))
        {
            throw CallFailure.From(result);
        }

        call.AddReasons(result.Reasons.Where(r => r.Severity != ReasonSeverity.Error));
        return result;
    }

    // The owner roles the subject has now: the application's current owner assignments, and the business owner an open
    // intake request names (O07 Business Owner; document 25, B25-10) once they consented (B25-18). The person the requester
    // named counts for the Accept gate before the application exists; the accepted intake then makes them its business
    // owner.
    private static IEnumerable<string> AssignedOwnerRoles(RuntimeCall call, GateSubject subject)
    {
        IEnumerable<string> roles = subject.Owners.Select(o => o.Text("ownerRole")).Where(k => k != null).Select(k => k!);
        string? meaning = subject.Intake?.Text("statusMeaning");
        if (subject.Intake?.Text("businessOwner") is string named && (meaning == null || meaning == "draft" || meaning == "submitted" || meaning == "in-progress")
            && IntakeOwnerConsented(call, subject.Intake, named))
        {
            roles = roles.Concat(new[] { EngineKeys.BusinessOwnerRole });
        }

        return roles.Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal);
    }

    // OP-06 for the application, when it has a lifecycle stage. Missing is computed, never stored (28, issue 16).
    private static JsonTreeNode? Documentation(RuntimeCall call, GateSubject subject, IReadOnlyList<StoredRow> links)
    {
        string? stage = subject.Application?.Text("lifecycleStage");
        if (subject.Application == null || stage == null)
        {
            return null;
        }

        JsonTreeNode input = RuntimeJson.Object(
            ("applicationKindKey", JsonTreeNode.FromString(subject.ApplicationKindKey!)),
            ("tierKey", RuntimeJson.OptionalText(subject.TierKey)),
            ("lifecycleStageKey", JsonTreeNode.FromString(stage)),
            ("links", JsonTreeNode.CreateArray(links.Select(DocumentFact).Where(f => f != null).Select(f => f!))),
            ("isRetired", !ApplicationIsLive(call.Snapshot!, subject.Application) ? JsonTreeNode.True : null));
        EvaluationResult<JsonTreeNode?> result = Evaluate(call, EngineOperation.EvaluateDocumentationCompleteness, call.Snapshot, input);
        return IsAnswer(EngineOperation.EvaluateDocumentationCompleteness, result.Outcome) ? result.Payload : null;
    }

    private static JsonTreeNode? DocumentFact(StoredRow link)
    {
        string? type = link.Text("documentType");
        string? status = link.Text("statusMeaning");
        if (type == null || status == null)
        {
            return null;
        }

        return RuntimeJson.Object(
            ("documentLinkId", JsonTreeNode.FromString(link.Id)),
            ("documentTypeKey", JsonTreeNode.FromString(type)),
            ("requirementKey", RuntimeJson.OptionalText(link.Text("documentationRequirement"))),
            ("status", JsonTreeNode.FromString(status)),
            ("hasUrl", JsonTreeNode.FromBoolean(link.Text("url") != null)),
            ("lastCheckedOnUtc", AsUtcText(link.Columns, "lastCheckedOn")),
            ("lastUpdatedOnUtc", AsUtcText(link.Columns, "lastUpdatedOn")));
    }

    // C10 Triggers Json of the subject's latest tier answers, through OP-01 (a proposal of this build: triggered rows apply).
    private static IReadOnlyList<string> TriggeredRequirements(RuntimeCall call, GateSubject subject)
    {
        StoredRow? assessment = subject.LatestAssessment;
        JsonTreeNode? answers = assessment?.Column("answersJson");
        if (assessment == null || answers == null || answers.Kind != JsonTreeKind.Array || subject.ApplicationKindKey == null)
        {
            return Array.Empty<string>();
        }

        EvaluationResult<JsonTreeNode?> result = Evaluate(call, EngineOperation.EvaluateTier, call.Snapshot, RuntimeJson.Object(("applicationKindKey", JsonTreeNode.FromString(subject.ApplicationKindKey)), ("answers", answers)));
        return IsAnswer(EngineOperation.EvaluateTier, result.Outcome) ? RuntimeJson.StringList(result.Payload, "triggeredGateRequirementKeys") : Array.Empty<string>();
    }

    private static JsonTreeNode? AsUtcText(JsonTreeNode row, string column)
    {
        DateTime? value = RuntimeJson.Utc(row, column);
        return value.HasValue ? JsonTreeNode.FromUtcDateTime(value.Value) : null;
    }

    private static IEnumerable<JsonTreeNode> Items(JsonTreeNode node, string member)
    {
        return node.TryGetMember(member, out JsonTreeNode? array) && array.Kind == JsonTreeKind.Array ? array.Items : Enumerable.Empty<JsonTreeNode>();
    }

    // An engine API call with an idempotency key (document 25, section 8): the same request again returns the first result
    // and writes nothing; another request with that key is refused.
    private static CallAnswer? ApiReplay(RuntimeCall call, string? idempotencyKey, JsonTreeNode parameters, out string? requestHash)
    {
        requestHash = null;
        if (idempotencyKey == null)
        {
            return null;
        }

        requestHash = CanonicalHash.ComputeRequestHash(parameters);
        FactSet facts = FactSet.Run(call.Tx, f => new[] { FactPlan.Of(FactQuery.Where("prior-request", RowQuery.For("audit-entry").Where("requestKey", idempotencyKey))) });
        call.FactRounds += facts.Rounds;
        StoredRow? prior = facts.One("prior-request");
        if (prior == null)
        {
            return null;
        }

        if (string.Equals(prior.Text("requestHash"), requestHash, StringComparison.Ordinal))
        {
            return new CallAnswer(RuntimeOutcome.Allowed, prior.Text("rowId"), false, prior.Column("changedValuesJson"), isReplay: true);
        }

        throw CallFailure.Of(
            RuntimeOutcome.Conflict,
            ReasonCodes.RequestIdempotencyKeyReused,
            "The idempotency key \"" + idempotencyKey + "\" was used before with a different request, so this call is refused. Use a new key for a new request.",
            null,
            idempotencyKey);
    }

    // What OP-07 reads about a subject.
    private sealed class GateSubject
    {
        public GateSubject(StoredRow? application, StoredRow? intake, string? applicationKindKey, string? tierKey, IReadOnlyList<StoredRow> owners, StoredRow? latestAssessment)
        {
            Application = application;
            Intake = intake;
            ApplicationKindKey = applicationKindKey;
            TierKey = tierKey;
            Owners = owners;
            LatestAssessment = latestAssessment;
        }

        public StoredRow? Application { get; }

        public StoredRow? Intake { get; }

        public string? ApplicationKindKey { get; }

        public string? TierKey { get; }

        public IReadOnlyList<StoredRow> Owners { get; }

        public StoredRow? LatestAssessment { get; }
    }
}
