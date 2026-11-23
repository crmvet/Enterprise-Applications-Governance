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

// One composed engine operation: the guarded writes and engine evaluations of one product operation, such as one MCP tool,
// in one call with one snapshot, one actor and one transaction (document 24, section 9.2: "one engine operation per
// tool"; document 25, section 3.3). The body reads through the call's transaction, so it sees its own writes.
public sealed class ComposeRequest
{
    public ComposeRequest(string apiName, JsonTreeNode? parameters = null)
    {
        if (string.IsNullOrEmpty(apiName))
        {
            throw new ArgumentException("A composed call names its operation.", nameof(apiName));
        }

        ApiName = apiName;
        Parameters = parameters ?? JsonTreeNode.CreateObject();
    }

    // The operation's name, such as "save_intake_draft". Part of the Request Hash of an idempotency key.
    public string ApiName { get; }

    // The operation's input as the caller sent it. The Request Hash covers the name and these parameters.
    public JsonTreeNode Parameters { get; }

    // A read: the snapshot is loaded for reading and no write is allowed.
    public bool ReadOnly { get; set; }

    // validateOnly (document 24, section 3.3): every rule and every write runs, then the transaction rolls back.
    public bool DryRun { get; set; }

    // The request's correlation ID, when the caller has one (the MCP server's envelope). Otherwise the runtime makes one.
    public string? CorrelationId { get; set; }

    // The caller's clientRequestId or idempotency key (document 25, section 8): the same request again returns the first
    // result and writes nothing; the same key with another request is a conflict.
    public string? IdempotencyKey { get; set; }
}

// What one guarded write of a composed call did.
public sealed class ComposedWrite
{
    public ComposedWrite(string rowId, bool created, bool isReplay, bool isNoOp, JsonTreeNode? plan)
    {
        RowId = rowId;
        Created = created;
        IsReplay = isReplay;
        IsNoOp = isNoOp;
        Plan = plan;
    }

    public string RowId { get; }

    public bool Created { get; }

    public bool IsReplay { get; }

    public bool IsNoOp { get; }

    // OP-08's write plan, as JSON (setColumns, removedColumns, effects and the rest).
    public JsonTreeNode? Plan { get; }
}

// The view a composed body has of its call. Every write goes through OP-08 with its effects, exactly as Write does; the
// engine's own operations (the approval request, a cancellation, the record step, the draft submit) run as they do in
// their own entry points. A refusal ends the call and rolls everything back.
public sealed class ComposedCall
{
    private readonly EngineRuntime _runtime;
    private readonly RuntimeCall _call;
    private readonly bool _readOnly;

    internal ComposedCall(EngineRuntime runtime, RuntimeCall call, bool readOnly, bool dryRun)
    {
        _runtime = runtime;
        _call = call;
        _readOnly = readOnly;
        IsDryRun = dryRun;
    }

    // Reads through the call's transaction: committed rows plus this call's own writes.
    public IRecordReader Rows => _call.Tx;

    // The snapshot in force for this call.
    public CompiledSnapshot Snapshot => _call.Snapshot;

    // The resolved actor: roles from the configuration in force (document 26, RA-01).
    public ActorInfo Actor => _call.Actor;

    public CallerIdentity Caller => _call.Caller;

    public DateTime AsOfUtc => _call.AsOfUtc;

    public string CorrelationId => _call.CorrelationId;

    public bool IsDryRun { get; }

    // The reasons gathered so far: the guard's warnings and infos, and the operations'.
    public IReadOnlyList<Reason> Reasons => _call.Reasons;

    // A caller's write, guarded by OP-08 with every effect it plans.
    public ComposedWrite Write(WriteCommand command)
    {
        if (command == null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        RequireWrite();
        WriteStep step = _runtime.ComposedGuardedWrite(_call, new WriteSpec(command.Entity, command.Operation)
        {
            RowId = command.RowId,
            After = command.After,
            IdempotencyKey = command.IdempotencyKey,
        });
        return new ComposedWrite(step.RowId, step.Created, step.IsReplay, step.IsNoOp, step.Plan?.ToJson());
    }

    // <prefix>_RequestApprovals inside this call: OP-02, then pending Approval Records or the engine's acceptance for the
    // locked list. Returns the request's payload (outcome, requestedCount, approvalRecordIds, accepted).
    public JsonTreeNode RequestApprovals(string decisionId)
    {
        RequireWrite();
        return _runtime.ComposedRequestApprovals(_call, decisionId);
    }

    // The engine writes the cancelled outcome on every pending Approval Record of the decision (document 24, tool 19;
    // document 22, section 8.2), as engine-nested writes that OP-08 judges (G-06: only a pending record, only once).
    // Returns the cancelled records' IDs.
    public IReadOnlyList<string> CancelPendingApprovals(string decisionId)
    {
        RequireWrite();
        return _runtime.ComposedCancelPendingApprovals(_call, decisionId);
    }

    // OP-02 for a recorded decision, under the version the decision was stamped with.
    public EvaluationResult<JsonTreeNode?> RequiredApprovals(string decisionId)
    {
        return _runtime.ComposedRequiredApprovals(_call, decisionId);
    }

    // OP-03 for a recorded decision, with OP-02 under its stamped version. Null when the decision is engine-accepted
    // (no card), together with OP-02's result in required.
    public EvaluationResult<JsonTreeNode?>? ApprovalStatus(string decisionId, out EvaluationResult<JsonTreeNode?> required, out IReadOnlyList<StoredRow> records)
    {
        return _runtime.ComposedApprovalStatus(_call, decisionId, out required, out records);
    }

    // OP-07 for an application and a gate, with OP-06 and the tier answers' triggers fetched, as GetGateReadiness does.
    public EvaluationResult<JsonTreeNode?> GateReadiness(string applicationId, string gateKey)
    {
        return _runtime.ComposedGateReadiness(_call, applicationId, gateKey);
    }

    // The record step inside this call (RecordDeployment). Returns the row and the payload.
    public ComposedWrite RecordDeployment(DeploymentReport report, out JsonTreeNode? payload)
    {
        RequireWrite();
        return _runtime.ComposedRecordDeployment(_call, report, out payload);
    }

    // submit_record for a configuration draft inside this call (SubmitConfigurationDraft).
    public JsonTreeNode SubmitConfigurationDraft(string configurationDraftId)
    {
        RequireWrite();
        return _runtime.ComposedSubmitConfigurationDraft(_call, configurationDraftId);
    }

    // propose_configuration_change inside this call (ProposeConfigurationChange): OP-09 on the preview in draft mode,
    // then the C46 draft written by the engine's draft operation unless the proposal is validate-only. Returns the
    // draft's row ID (null when validate-only) and the payload; the OP-09 report is under Result("OP-09").
    public string? ProposeConfigurationChange(ConfigurationProposal proposal, out JsonTreeNode? payload)
    {
        RequireWrite();
        if (proposal == null)
        {
            throw new ArgumentNullException(nameof(proposal));
        }

        return _runtime.ComposedProposeConfigurationChange(_call, proposal, out payload);
    }

    // The last result of an operation this call ran, by name ("OP-02", "OP-09", "activation"), as { outcome, payload }.
    public JsonTreeNode? Result(string name)
    {
        return _call.Results.TryGetValue(name, out JsonTreeNode? result) ? result : null;
    }

    // The engine records facts a caller reports about a directory object on its Principal Reference (O05): the leave date
    // and the account state (document 24, tools 46 and 49: "as reported"). O05 rows are written only by the engine
    // (OP-08), so this is an engine-nested write of the engine's own operation, with no effect. Only these three columns.
    public ComposedWrite RecordPrincipalFacts(string principalReferenceId, string? leavesOn, string? directoryState)
    {
        RequireWrite();
        var columns = new List<(string, JsonTreeNode?)>();
        if (leavesOn != null)
        {
            columns.Add(("leavesOn", JsonTreeNode.FromString(leavesOn)));
        }

        if (directoryState != null)
        {
            columns.Add(("directoryState", JsonTreeNode.FromString(directoryState)));
            columns.Add(("directoryStateOn", JsonTreeNode.FromUtcDateTime(_call.AsOfUtc)));
        }

        if (columns.Count == 0)
        {
            throw new ArgumentException("A principal fact names a leave date or a directory state.", nameof(leavesOn));
        }

        WriteStep step = _runtime.ComposedGuardedWrite(_call, new WriteSpec("principal-reference", WriteOperation.Update)
        {
            RowId = principalReferenceId,
            After = RuntimeJson.Object(columns.ToArray()),
            Nested = true,
        });
        return new ComposedWrite(step.RowId, step.Created, step.IsReplay, step.IsNoOp, step.Plan?.ToJson());
    }

    // withdraw_record for a configuration draft (document 24, tool 19; document 21, section 4.2): the engine's draft
    // operation discards the draft for its configuration admin, as an engine-nested write with no effect (G-16 allows
    // draft to discarded, and submitted to discarded with only the outcome columns).
    public ComposedWrite DiscardConfigurationDraft(string configurationDraftId, string reason)
    {
        RequireWrite();
        WriteStep step = _runtime.ComposedGuardedWrite(_call, new WriteSpec("configuration-draft", WriteOperation.Update)
        {
            RowId = configurationDraftId,
            After = RuntimeJson.Object(
                ("draftStatus", JsonTreeNode.FromString(EngineContract.DraftStatusMeanings.ToWire(DraftStatusMeaning.Discarded))),
                ("discardedOn", JsonTreeNode.FromUtcDateTime(_call.AsOfUtc)),
                ("discardReason", JsonTreeNode.FromString(reason ?? "Withdrawn by the person who instructed it."))),
            Nested = true,
        });
        return new ComposedWrite(step.RowId, step.Created, step.IsReplay, step.IsNoOp, step.Plan?.ToJson());
    }

    // Any read operation of the core, under the snapshot in force, as the caller (engine-nested context).
    public EvaluationResult<JsonTreeNode?> Evaluate(EngineOperation operation, JsonTreeNode input)
    {
        if (OperationCatalog.Get(operation).IsWrite)
        {
            throw new ArgumentException("A write goes through Write, never Evaluate.", nameof(operation));
        }

        return _runtime.ComposedEvaluate(_call, operation, input);
    }

    // A note for the caller that does not stop the call.
    public void Warn(string code, string message, string? entity = null, string? reference = null)
    {
        _call.AddReasons(new[] { new Reason(code, ReasonSeverity.Warning, message, entity, reference) });
    }

    // Ends the call with an outcome that writes nothing: throw the returned exception. The outcome is one of the runtime's
    // failure words (refused, invalid-input, not-found, conflict, unavailable).
    public Exception Fail(string outcome, string code, string message, string? entity = null, string? reference = null)
    {
        if (!RuntimeOutcome.IsFailure(outcome))
        {
            throw new ArgumentException("A failure outcome is refused, invalid-input, not-found, conflict or unavailable.", nameof(outcome));
        }

        return CallFailure.Of(outcome, code, message, entity, reference);
    }

    private void RequireWrite()
    {
        if (_readOnly)
        {
            throw new InvalidOperationException("A read-only composed call writes nothing.");
        }
    }
}

public sealed partial class EngineRuntime
{
    // Runs a composed operation in one call (see ComposedCall). The body returns the operation's payload. A replay of an
    // idempotency key returns the first payload and writes nothing; a dry run commits nothing.
    public RuntimeOutcome Compose(CallerIdentity caller, ComposeRequest request, Func<ComposedCall, JsonTreeNode?> body)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (body == null)
        {
            throw new ArgumentNullException(nameof(body));
        }

        if (request.ReadOnly && request.IdempotencyKey != null)
        {
            throw new ArgumentException("A read takes no idempotency key.", nameof(request));
        }

        SnapshotPurpose purpose = request.ReadOnly ? SnapshotPurpose.Read : SnapshotPurpose.Write;
        return Run(caller, purpose, call =>
        {
            string? requestHash = null;
            if (!request.ReadOnly && !request.DryRun)
            {
                JsonTreeNode parameters = RuntimeJson.Object(("api", JsonTreeNode.FromString(request.ApiName)), ("parameters", request.Parameters));
                CallAnswer? replay = ApiReplay(call, request.IdempotencyKey, parameters, out requestHash);
                if (replay != null)
                {
                    return replay;
                }
            }

            var composed = new ComposedCall(this, call, request.ReadOnly, request.DryRun);
            JsonTreeNode payload = body(composed) ?? RuntimeJson.Empty;
            string? rowId = RuntimeJson.Text(payload, "recordId");
            if (request.IdempotencyKey != null && requestHash != null && call.Writes.Count > 0)
            {
                WriteAudit(call, call.Snapshot, request.ApiName, rowId, "composed-call", payload, request.IdempotencyKey, requestHash);
            }

            bool created = call.Writes.Any(w => w.Operation == WriteOperation.Create && !w.IsEngineNested);
            return new CallAnswer(RuntimeOutcome.Allowed, rowId, created, payload, dryRun: request.DryRun);
        }, request.CorrelationId);
    }

    internal WriteStep ComposedGuardedWrite(RuntimeCall call, WriteSpec spec)
    {
        return GuardedWrite(call, spec)!;
    }

    internal JsonTreeNode ComposedRequestApprovals(RuntimeCall call, string decisionId)
    {
        return RequestApprovalsIn(call, decisionId);
    }

    internal IReadOnlyList<string> ComposedCancelPendingApprovals(RuntimeCall call, string decisionId)
    {
        return CancelPendingRecords(call, decisionId, null);
    }

    internal EvaluationResult<JsonTreeNode?> ComposedRequiredApprovals(RuntimeCall call, string decisionId)
    {
        DecisionScope scope = Scope(call, decisionId);
        return RequiredApprovals(call, scope, StampedSnapshot(call, scope.Decision));
    }

    internal EvaluationResult<JsonTreeNode?>? ComposedApprovalStatus(RuntimeCall call, string decisionId, out EvaluationResult<JsonTreeNode?> required, out IReadOnlyList<StoredRow> records)
    {
        DecisionScope scope = Scope(call, decisionId);
        CompiledSnapshot stamped = StampedSnapshot(call, scope.Decision);
        required = RequiredApprovals(call, scope, stamped);
        if (required.Outcome == "engine-accept")
        {
            records = Records(call, decisionId);
            return null;
        }

        return ApprovalStatus(call, scope, stamped, required.Payload!, out records);
    }

    internal EvaluationResult<JsonTreeNode?> ComposedGateReadiness(RuntimeCall call, string applicationId, string gateKey)
    {
        FactSet facts = FactSet.Run(call.Tx, f => ApplicationFacts(f, applicationId));
        call.FactRounds += facts.Rounds;
        StoredRow app = facts.One("application") ?? throw CallFailure.Of(RuntimeOutcome.NotFound, ReasonCodes.RefNotFound, "Business Application " + applicationId + " does not exist.", "Business Application", applicationId);
        var subject = new GateSubject(app, null, app.Text("applicationKind"), app.Text("tier"), DecisionScope.CurrentOwners(facts.All("owners"), call.AsOfUtc), null);
        return GateReadiness(call, subject, gateKey);
    }

    internal ComposedWrite ComposedRecordDeployment(RuntimeCall call, DeploymentReport report, out JsonTreeNode? payload)
    {
        CallAnswer answer = RecordDeploymentIn(call, report);
        payload = answer.Payload;
        return new ComposedWrite(answer.RowId!, answer.Created, answer.IsReplay, answer.IsNoOp, null);
    }

    internal string? ComposedProposeConfigurationChange(RuntimeCall call, ConfigurationProposal proposal, out JsonTreeNode? payload)
    {
        CallAnswer answer = ProposeConfigurationChangeIn(call, proposal);
        payload = answer.Payload;
        return answer.RowId;
    }

    internal JsonTreeNode ComposedSubmitConfigurationDraft(RuntimeCall call, string configurationDraftId)
    {
        return SubmitConfigurationDraftIn(call, configurationDraftId).Payload ?? RuntimeJson.Empty;
    }

    internal EvaluationResult<JsonTreeNode?> ComposedEvaluate(RuntimeCall call, EngineOperation operation, JsonTreeNode input)
    {
        return Evaluate(call, operation, call.Snapshot, input);
    }
}
