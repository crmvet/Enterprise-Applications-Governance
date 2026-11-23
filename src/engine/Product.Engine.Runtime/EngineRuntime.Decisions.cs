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

    // The request, inside a call: also used by the submit of a configuration draft.
    private JsonTreeNode RequestApprovalsIn(RuntimeCall call, string decisionId)
    {
        DecisionScope scope = Scope(call, decisionId);
        string? meaning = scope.Decision.Text("statusMeaning");
        if (meaning != "proposed")
        {
            throw CallFailure.Of(OperationOutcomes.Refused, ReasonCodes.DecisionFinal, "The Decision is " + (meaning ?? "without a status") + ", so it takes no approval request.", "Decision", decisionId);
        }

        // Effects check the target's state before writing (document 25, section 8): a decision with records is requested.
        IReadOnlyList<StoredRow> existing = Records(call, decisionId);
        if (existing.Count > 0)
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
            JsonTreeNode basis = op02.TryGetMember("acceptanceBasis", out JsonTreeNode? b) ? b : RuntimeJson.Empty;
            EngineAcceptanceCondition condition = EngineContract.EngineAcceptanceConditions.Parse(RuntimeJson.Text(basis, "condition") ?? string.Empty);
            bool accepted = AcceptDecision(call, scope, new WriteAcceptance(WriteAcceptance.EngineAccept, requiredApprovalsOutcome: "engine-accept", condition: condition));
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
        if (required.Outcome == "engine-accept")
        {
            return;
        }

        ExpireDueRecords(call, Records(call, decisionId));
        EvaluationResult<JsonTreeNode?> status = ApprovalStatus(call, scope, stamped, required.Payload!, out IReadOnlyList<StoredRow> records);
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
                if (AcceptDecision(call, scope, new WriteAcceptance(WriteAcceptance.ApprovalsSatisfied, approvalStatusOutcome: "satisfied")))
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
                OpenApprovalRecords(call, decisionId, required.Payload!, status.Payload, EffectKind.RecomputeApprovals);
                break;
        }
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

        WriteStep? step = GuardedWrite(call, new WriteSpec("decision", WriteOperation.Update)
        {
            RowId = scope.DecisionId,
            After = RuntimeJson.Object(("status", JsonTreeNode.FromString(DefaultStatus(call.Snapshot, "decision", "accepted")))),
            Nested = true,
            Effect = EffectKind.AcceptDecision,
            Acceptance = acceptance,
            GateReadiness = readiness,
            Tolerated = new[] { ReasonCodes.GateNotReady },
        });
        return step != null;
    }

    private DecisionScope Scope(RuntimeCall call, string decisionId)
    {
        FactSet facts = FactSet.Run(call.Tx, f => DecisionScope.Plan(decisionId, f));
        call.FactRounds += facts.Rounds;
        return DecisionScope.Build(facts, call.AsOfUtc)
            ?? throw CallFailure.Of(RuntimeOutcome.NotFound, ReasonCodes.RefNotFound, "Decision " + decisionId + " does not exist.", "Decision", decisionId);
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
        EvaluationResult<JsonTreeNode?> result = Evaluate(call, EngineOperation.ResolveRequiredApprovals, snapshot, scope.RequiredApprovalsInput());
        if (!IsAnswer(EngineOperation.ResolveRequiredApprovals, result.Outcome))
        {
            throw CallFailure.From(result);
        }

        call.AddReasons(result.Reasons);
        return result;
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
    private static EvaluationResult<JsonTreeNode?> GateReadiness(RuntimeCall call, GateSubject subject, string gateKey)
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
            ("assignedOwnerRoleKeys", RuntimeJson.Strings(subject.Owners.Select(o => o.Text("ownerRole")).Where(k => k != null).Select(k => k!).Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal))),
            ("triggeredGateRequirementKeys", RuntimeJson.Strings(triggered)));
        EvaluationResult<JsonTreeNode?> result = Evaluate(call, EngineOperation.EvaluateGateReadiness, call.Snapshot, input);
        if (!IsAnswer(EngineOperation.EvaluateGateReadiness, result.Outcome))
        {
            throw CallFailure.From(result);
        }

        call.AddReasons(result.Reasons.Where(r => r.Severity != ReasonSeverity.Error));
        return result;
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
            ("links", JsonTreeNode.CreateArray(links.Select(DocumentFact).Where(f => f != null).Select(f => f!))));
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
