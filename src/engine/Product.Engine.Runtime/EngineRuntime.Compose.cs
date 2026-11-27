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

// A data use that is not saved yet, as check_data_use asks about it (docs/architecture/data-taxonomy.md, In the AI build loop and MCP tools and REST): the domain,
// the application or the intake request whose scope the decision would have, the location, and the AI and de-identified
// flags. OP-16 answers it exactly as it answers a saved use with no decision yet.
public sealed class DataUseQuestion
{
    public DataUseQuestion(string domainKey, string? applicationId, string? intakeRequestId, string? locationId, bool reachesAi, bool isDeidentified)
    {
        if (string.IsNullOrEmpty(domainKey))
        {
            throw new ArgumentException("A data use question names its domain.", nameof(domainKey));
        }

        DomainKey = domainKey;
        ApplicationId = applicationId;
        IntakeRequestId = intakeRequestId;
        LocationId = locationId;
        ReachesAi = reachesAi;
        IsDeidentified = isDeidentified;
    }

    public string DomainKey { get; }

    public string? ApplicationId { get; }

    public string? IntakeRequestId { get; }

    public string? LocationId { get; }

    public bool ReachesAi { get; }

    public bool IsDeidentified { get; }
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

    // ---- Loading ahead (document 25, B25-23) ----

    // A read call reads through a cache of its own (CallReadCache). These load, in one store call, rows the call will read
    // many times, so the later reads cost no store call: the rows whose column holds one of the values (the owners of
    // every application on a page), the rows with these IDs, or every row of an entity. The answers are the ones the
    // store would give. A write call reads from the store each time, so these do nothing there. Nulls and repeats are
    // ignored.
    public void Prefetch(string entity, string column, IEnumerable<string?> values)
    {
        if (_call.Tx is CallReadCache cache)
        {
            List<string> keys = KeySetReads.Distinct(values);
            if (keys.Count > 0)
            {
                cache.QueryIn(entity, column, keys);
            }
        }
    }

    public void PrefetchIds(string entity, IEnumerable<string?> ids)
    {
        if (_call.Tx is CallReadCache cache)
        {
            List<string> keys = KeySetReads.Distinct(ids);
            if (keys.Count > 0)
            {
                cache.GetMany(entity, keys);
            }
        }
    }

    public void PrefetchAll(string entity)
    {
        if (_call.Tx is CallReadCache cache)
        {
            cache.LoadAll(entity);
        }
    }

    // The data uses and mappings with these IDs and every fact OP-16 reads for each, so a list of them evaluates in a
    // few store calls (EngineRuntime.PrefetchDataFacts).
    public void PrefetchDataRows(IEnumerable<string?> dataUseOrMappingIds)
    {
        EngineRuntime.PrefetchDataRows(_call, dataUseOrMappingIds);
    }

    // True when this call's reads go through its read cache.
    public bool ReadsAreCached => _call.Tx is CallReadCache;

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

    // Asks a requested decision again from the start, of the people its scope names now (move_data_references, when the
    // rows move to a domain with another data owner): the engine cancels the pending Approval Records, then opens the
    // requests OP-02 needs now, consultations included, as a first request does. A decision with an answer on record is
    // refused (that answer was given to the question as it stood before), and so is one that has nobody to ask (DC-06).
    // Returns the request's payload, as RequestApprovals does.
    public JsonTreeNode ReaskApprovals(string decisionId)
    {
        RequireWrite();
        return _runtime.ComposedReaskApprovals(_call, decisionId);
    }

    // The engine withdraws a proposed data-use decision whose question no longer stands as it was asked: the rows moved
    // to a domain that answers to another data owner, after someone had answered, or to a domain with no data owner to
    // ask. Every pending Approval Record is cancelled, then the withdrawn status and the reason are written as the
    // engine's own nested writes, which OP-08 judges. Only a proposed data-use decision. Returns the cancelled records.
    public IReadOnlyList<string> WithdrawDataDecision(string decisionId, string reason)
    {
        RequireWrite();
        return _runtime.ComposedWithdrawDataDecision(_call, decisionId, reason);
    }

    // The engine opens and sends the data-use decision a moved row needs (move_data_references, when the rows move to a
    // domain with another data owner; wave 3 verification, major). A row declared again in the new domain, or one whose
    // earlier decision was withdrawn, has no decision, and the new data owner must be asked. The move itself was allowed
    // (its Tool Policy, and OP-08 on every moved row), so the decision it makes necessary is the engine's own nested
    // write, stamped as done for the person who moved the rows. No proposal right is asked of them, as none is for the
    // requests a re-ask opens: the data governance lead may move rows and proposes no data-use decision of her own
    // (docs/architecture/data-taxonomy.md). Only a confirmed, open data use or data mapping with no proposed data-use decision.
    // The decision is proposed with the allow outcome and its approvals are requested at once. Returns the decision's ID;
    // requested is the request's payload, as RequestApprovals returns it.
    public string OpenDataDecision(string subjectKind, string rowId, string statement, string context, out JsonTreeNode requested)
    {
        RequireWrite();
        return _runtime.ComposedOpenDataDecision(_call, subjectKind, rowId, statement, context, out requested);
    }

    // "Ask again" on an approval request (document 25, B25-24; renew_approval_request): the proposer of the decision, or a
    // platform admin, asks the same requirement of the same role once more. Only a proposed decision; only a request that
    // expired (its Expires On came, whether or not the daily refresh marked it), or a pending one within the last 48 hours
    // of its time, so a demo or a person never meets a request that lapses. The engine marks the old request expired (or
    // canceled, when it was still in time), then opens the new one through the recompute, as the daily refresh does after
    // an expiry, with the Timeout Days of the decision's own configuration version; a consultation is opened again as the
    // first request opened it. Every write is the engine's own nested write. A request already asked again answers with
    // the open one and writes nothing. Returns { approvalRecordId, decisionId, renewed, previousOutcome, openedRecordIds,
    // expiresOn }.
    public JsonTreeNode RenewApprovalRequest(string approvalRecordId)
    {
        RequireWrite();
        return _runtime.ComposedRenewApprovalRequest(_call, approvalRecordId);
    }

    // A change the tool made to a host table outside the engine's rows (the approval outbox row a platform admin retries):
    // the composed call's Audit Entry lists it, so the change is recorded though no engine row changed (B25-24). A dry run
    // records nothing.
    public void RecordHostChange(string entity, string rowId, string state)
    {
        RequireWrite();
        if (string.IsNullOrEmpty(entity) || string.IsNullOrEmpty(rowId) || string.IsNullOrEmpty(state))
        {
            throw new ArgumentException("A host change names its entity, its row and the state it left.");
        }

        if (!IsDryRun)
        {
            _call.HostChanges.Add((entity, rowId, state));
        }
    }

    // Runs the action once this call has committed, after the Audit Entry that records it (security review, wave 4). Never for a dry
    // run. The action may still add a warning through Warn, which the answer then carries.
    public void AfterCommit(Action action)
    {
        RequireWrite();
        if (action == null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        if (!IsDryRun)
        {
            _call.AfterCommit.Add(action);
        }
    }

    // OP-02 for a recorded decision, under the version the decision was stamped with.
    public EvaluationResult<JsonTreeNode?> RequiredApprovals(string decisionId)
    {
        return _runtime.ComposedRequiredApprovals(_call, decisionId);
    }

    // Who a requirement from RequiredApprovals would ask once the decision is submitted (product review, fix round 1): an owner
    // role with no holder hands over to its fallback, so the priority decision on a request for a new application asks
    // the business owner the request names when no sponsor is named. A preview names the same role the submit asks.
    public AskTarget AskOf(string decisionId, JsonTreeNode requirement)
    {
        if (requirement == null)
        {
            throw new ArgumentNullException(nameof(requirement));
        }

        return _runtime.ComposedAskOf(_call, decisionId, requirement);
    }

    // The proposal right check (document 24, tool 17; B24-5) for a decision this call recorded or edited: the person
    // behind the call must hold a responsible, accountable or consulted Decision Right (C31) for the decision type in the
    // decision's scope, resolved as OP-02 resolves the accountable letter, under the version the decision is stamped with.
    // A refusal (decision.propose-not-allowed) ends the call and nothing commits. Returns the check's payload.
    public JsonTreeNode RequireProposalRight(string decisionId)
    {
        return _runtime.ComposedRequireProposalRight(_call, decisionId);
    }

    // The same proposal right check for a scope the caller names (the wire form of ProposalRightRequest: the decision type,
    // the scope fields OP-02 reads, and the product and owner roles to test), under the snapshot in force. Read only:
    // nothing is recorded and nothing ends the call. A read uses it to find whose turn a record is, such as the review a
    // person may close because they may propose the decision the review records (get_my_queue, list_reviews).
    public EvaluationResult<ProposalRightResult?> ProposalRight(JsonTreeNode input)
    {
        if (input == null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        return ProposalRightsOperation.Evaluate(_call.Snapshot.Snapshot, _call.Context(nested: true), input);
    }

    // A person's own answer to an Approval Record inside this call (AnswerApproval; D-38): the approval answer check for
    // the signed-in person, then the answer through OP-08 and the recompute. A refusal ends the call and nothing commits.
    // Returns the answer's payload; the check's result is under Result("approval-answer").
    public JsonTreeNode AnswerApproval(ApprovalAnswerCommand command)
    {
        RequireWrite();
        if (command == null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        return _runtime.ComposedAnswerApproval(_call, command);
    }

    // The approval answer check for the person behind this call on one Approval Record, as a pre-check (D-38; document
    // 24, B24-9): whether their approval would be taken now, judged exactly as AnswerApproval judges it, with a typed
    // reason assumed. Read only: nothing is recorded and a refusal does not end the call. The page shows Approve and
    // Reject only when it is allowed, and otherwise says why (the reason names the separation rule that refuses).
    public EvaluationResult<ApprovalAnswerResult?> ApprovalAnswerCheck(string approvalRecordId)
    {
        return ApprovalAnswerCheck(approvalRecordId, forThePerson: false);
    }

    // forThePerson: judged for the person an AI client acts for, as if the person answered on the decision page (wave 4b,
    // V1). An AI client's own check always stops at "AI never approves", which hides whether a separation rule keeps the
    // person from answering; this one does not. For a person's own call it is the same check.
    public EvaluationResult<ApprovalAnswerResult?> ApprovalAnswerCheck(string approvalRecordId, bool forThePerson)
    {
        if (string.IsNullOrEmpty(approvalRecordId))
        {
            throw new ArgumentException("The check names its Approval Record.", nameof(approvalRecordId));
        }

        return _runtime.ComposedApprovalAnswerCheck(_call, approvalRecordId, forThePerson);
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

    // OP-07 for the gate whose gate decision type is this decision's type, over the decision's subject, exactly as the
    // accept-decision step runs it (G-14). Null when no gate takes this decision type.
    public EvaluationResult<JsonTreeNode?>? DecisionGateReadiness(string decisionId)
    {
        return _runtime.ComposedDecisionGateReadiness(_call, decisionId);
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

    // ---- The data taxonomy reads (data taxonomy slice 3; docs/architecture/data-taxonomy.md) ----

    // OP-15 for each domain named, the owner facts of every chain fetched once. Read only: nothing ends the call, and a
    // key that is not a domain gives OP-15's invalid-input result.
    public IReadOnlyList<EvaluationResult<JsonTreeNode?>> DataDomains(IReadOnlyList<string> domainKeys)
    {
        if (domainKeys == null)
        {
            throw new ArgumentNullException(nameof(domainKeys));
        }

        return _runtime.ComposedDataDomains(_call, domainKeys);
    }

    // OP-16 for one stored data use or data mapping, this call's own writes included. Null when neither exists.
    public EvaluationResult<JsonTreeNode?>? DataUse(string dataUseOrMappingId)
    {
        if (string.IsNullOrEmpty(dataUseOrMappingId))
        {
            throw new ArgumentException("The read names a data use or a data mapping.", nameof(dataUseOrMappingId));
        }

        return _runtime.ComposedDataUse(_call, dataUseOrMappingId);
    }

    // The effect of a configuration change on the live data uses and copies of the domains it touches (docs/architecture/data-taxonomy.md,
    // Changing the taxonomy): OP-16 under the configuration in force and under the change's preview, for each open row.
    public JsonTreeNode DataUseImpact(long baseVersionNumber, JsonTreeNode changeJson)
    {
        if (changeJson == null)
        {
            throw new ArgumentNullException(nameof(changeJson));
        }

        return _runtime.ComposedDataUseImpact(_call, baseVersionNumber, changeJson);
    }

    // OP-16 for a data use that is not saved yet, such as the question check_data_use answers before anything is built.
    public EvaluationResult<JsonTreeNode?> DataUseQuestion(DataUseQuestion question)
    {
        if (question == null)
        {
            throw new ArgumentNullException(nameof(question));
        }

        return _runtime.ComposedDataUseQuestion(_call, question);
    }

    // OP-17 over the store's data facts and this call's own writes, narrowed by the scope (domainKey, includeSubtree,
    // applicationId, locationId, principalId; null means everything).
    public EvaluationResult<JsonTreeNode?> DataChecks(JsonTreeNode? scope)
    {
        return _runtime.ComposedDataChecks(_call, scope);
    }

    // Adds the warnings and notes of a read to the call's answer (errors never: a read result that failed is the tool's
    // to handle).
    public void Report(IEnumerable<Reason> reasons)
    {
        if (reasons == null)
        {
            throw new ArgumentNullException(nameof(reasons));
        }

        _call.AddReasons(reasons.Where(r => r.Severity != ReasonSeverity.Error).ToList());
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

            call.DryRun = request.DryRun;
            var composed = new ComposedCall(this, call, request.ReadOnly, request.DryRun);
            JsonTreeNode payload = body(composed) ?? RuntimeJson.Empty;

            // A write can be what a held gate decision waited for (a done review, a document link, an owner, a decided
            // data use): the decisions of the applications the call touched settle now, in the same transaction.
            if (!request.ReadOnly && !request.DryRun && call.Writes.Count > 0)
            {
                SettleHeldGateDecisions(call, GateFactApplications(call));
            }

            string? rowId = RuntimeJson.Text(payload, "recordId");
            bool changed = call.Writes.Count > 0 || call.HostChanges.Count > 0;
            if (request.IdempotencyKey != null && requestHash != null && changed)
            {
                WriteAudit(call, call.Snapshot, request.ApiName, rowId ?? FirstHostRow(call), "composed-call", payload, request.IdempotencyKey, requestHash);
            }
            else if (!request.ReadOnly && !request.DryRun && changed)
            {
                // Every composed call that writes leaves one Audit Entry, with or without a key (document 26, section 9.4:
                // O24 is the full change log on Azure; document 25, B25-13). Without a key nothing is replayed from it, so
                // it holds the rows the call wrote, never the payload: no principal label reaches the ledger.
                WriteAudit(call, call.Snapshot, request.ApiName, rowId ?? FirstCallerRow(call) ?? FirstHostRow(call), "composed-call", WrittenRows(call), null, null);
            }

            bool created = call.Writes.Any(w => w.Operation == WriteOperation.Create && !w.IsEngineNested);
            return new CallAnswer(RuntimeOutcome.Allowed, rowId, created, payload, dryRun: request.DryRun);
        }, request.CorrelationId);
    }

    internal WriteStep ComposedGuardedWrite(RuntimeCall call, WriteSpec spec)
    {
        return GuardedWrite(call, spec)!;
    }

    // The rows a call wrote, as an unkeyed composed call's Audit Entry holds them: entity, operation, row ID and, when the
    // write set one, the status meaning it left. IDs and meanings only: no values, no labels (document 22, O24).
    private static JsonTreeNode WrittenRows(RuntimeCall call)
    {
        var rows = new List<JsonTreeNode>();
        foreach (AppliedWrite write in call.Writes.Where(w => w.Entity != "audit-entry"))
        {
            string? meaning = RuntimeJson.Text(write.Columns, "statusMeaning") ?? RuntimeJson.Text(write.Columns, "outcomeMeaning") ?? RuntimeJson.Text(write.Columns, "resultMeaning");
            rows.Add(RuntimeJson.Object(
                ("entity", JsonTreeNode.FromString(write.Entity)),
                ("operation", JsonTreeNode.FromString(EngineContract.WriteOperations.ToWire(write.Operation))),
                ("rowId", JsonTreeNode.FromString(write.RowId)),
                ("statusMeaning", RuntimeJson.Text(meaning)),
                ("byEngine", JsonTreeNode.FromBoolean(write.IsEngineNested))));
        }

        // A change to a host table (B25-24): the host's own row, by the person's request, never the engine's.
        foreach ((string entity, string rowId, string state) in call.HostChanges)
        {
            rows.Add(RuntimeJson.Object(
                ("entity", JsonTreeNode.FromString(entity)),
                ("operation", JsonTreeNode.FromString(EngineContract.WriteOperations.ToWire(WriteOperation.Update))),
                ("rowId", JsonTreeNode.FromString(rowId)),
                ("statusMeaning", JsonTreeNode.FromString(state)),
                ("byEngine", JsonTreeNode.False),
                ("byHost", JsonTreeNode.True)));
        }

        return RuntimeJson.Object(("writes", JsonTreeNode.CreateArray(rows)));
    }

    // The first row the caller itself wrote, for an Audit Entry whose payload names no record.
    private static string? FirstCallerRow(RuntimeCall call)
    {
        return call.Writes.FirstOrDefault(w => !w.IsEngineNested && w.Entity != "audit-entry")?.RowId;
    }

    // The first host row the call changed, for an Audit Entry of a call that wrote no engine row of its own.
    private static string? FirstHostRow(RuntimeCall call)
    {
        return call.HostChanges.Count == 0 ? null : call.HostChanges[0].RowId;
    }

    // Records a refused call as a product event in a transaction of its own (document 26, sections 9.2 and 9.4: "refused
    // writes are written by the API to O24 in a separate transaction"; document 25, B25-13): who, through which client,
    // which tool, on which record, and the refusal's code, rule and reason. The refused call itself wrote nothing. The
    // entry passes OP-08 as the engine's own nested write. Returns false when it could not be recorded (no configuration
    // in force, or a caller the configuration does not resolve); the refusal stands either way.
    public bool RecordRefusedCall(CallerIdentity caller, string apiName, string? rowId, JsonTreeNode details, string? correlationId = null)
    {
        if (caller == null)
        {
            throw new ArgumentNullException(nameof(caller));
        }

        if (string.IsNullOrEmpty(apiName))
        {
            throw new ArgumentException("A refused call names its operation.", nameof(apiName));
        }

        RuntimeOutcome outcome = Run(caller, SnapshotPurpose.Write, call =>
        {
            WriteAudit(call, call.Snapshot, apiName, rowId, "call-refused", details ?? RuntimeJson.Empty, null, null);
            return new CallAnswer(RuntimeOutcome.Allowed, rowId);
        }, correlationId);
        return outcome.Succeeded;
    }

    internal JsonTreeNode ComposedRequestApprovals(RuntimeCall call, string decisionId)
    {
        return RequestApprovalsIn(call, decisionId);
    }

    internal IReadOnlyList<string> ComposedCancelPendingApprovals(RuntimeCall call, string decisionId)
    {
        return CancelPendingRecords(call, decisionId, null);
    }

    internal JsonTreeNode ComposedReaskApprovals(RuntimeCall call, string decisionId)
    {
        return RequestApprovalsIn(call, decisionId, again: true);
    }

    internal IReadOnlyList<string> ComposedWithdrawDataDecision(RuntimeCall call, string decisionId, string reason)
    {
        StoredRow decision = call.Tx.Get("decision", decisionId)
            ?? throw CallFailure.Of(RuntimeOutcome.NotFound, ReasonCodes.RefNotFound, "Decision " + decisionId + " does not exist.", "Decision", decisionId);
        if (decision.Text("decisionType") != EngineKeys.DataUseDecisionType || decision.Text("statusMeaning") != "proposed")
        {
            throw CallFailure.Of(OperationOutcomes.Refused, ReasonCodes.DecisionFinal, "The engine withdraws only a proposed data-use decision; Decision " + decisionId + " is " + (decision.Text("statusMeaning") ?? "without a status") + ".", "Decision", decisionId);
        }

        IReadOnlyList<string> cancelled = CancelPendingRecords(call, decisionId, null);
        GuardedWrite(call, new WriteSpec("decision", WriteOperation.Update)
        {
            RowId = decisionId,
            After = RuntimeJson.Object(
                ("status", JsonTreeNode.FromString(DefaultStatus(call.Snapshot, "decision", "withdrawn"))),
                ("reason", JsonTreeNode.FromString(string.IsNullOrEmpty(reason) ? "Withdrawn by the engine: the question no longer stands as it was asked." : reason))),
            Nested = true,
        });
        return cancelled;
    }

    internal string ComposedOpenDataDecision(RuntimeCall call, string subjectKind, string rowId, string statement, string context, out JsonTreeNode requested)
    {
        bool mapping = subjectKind == EngineKeys.DataMappingSubjectKind;
        if (!mapping && subjectKind != EngineKeys.DataUseSubjectKind)
        {
            throw new ArgumentException("The engine opens a data-use decision about a data use or a data mapping only.", nameof(subjectKind));
        }

        if (string.IsNullOrEmpty(statement))
        {
            throw new ArgumentException("A data-use decision states what it allows.", nameof(statement));
        }

        string entity = mapping ? "data-mapping" : "data-use";
        string column = mapping ? "subjectDataMapping" : "subjectDataUse";
        StoredRow row = call.Tx.Get(entity, rowId)
            ?? throw CallFailure.Of(RuntimeOutcome.NotFound, ReasonCodes.RefNotFound, SnapshotReadName(entity) + " " + rowId + " does not exist.", SnapshotReadName(entity), rowId);
        if (row.Text("statusMeaning") != "registered" || !DataTree.IsOpenOn(row.Text("endedOn"), DataTree.DayOf(call.AsOfUtc)))
        {
            throw CallFailure.Of(OperationOutcomes.InvalidInput, ReasonCodes.InputMalformed, "The engine opens a data-use decision only for a confirmed, open " + SnapshotReadName(entity) + ", and " + rowId + " is not one.", SnapshotReadName(entity), rowId);
        }

        IReadOnlyList<StoredRow> open = call.Tx.Query(RowQuery.For("decision").Where(column, rowId).Where("decisionType", EngineKeys.DataUseDecisionType).Where("statusMeaning", "proposed"));
        if (open.Count > 0)
        {
            throw CallFailure.Of(OperationOutcomes.InvalidInput, ReasonCodes.InputMalformed, SnapshotReadName(entity) + " " + rowId + " has a proposed data-use decision already, so the engine opens no second one.", "Decision", open[0].Id);
        }

        string text = string.IsNullOrEmpty(context) ? statement : context;
        WriteStep decision = GuardedWrite(call, new WriteSpec("decision", WriteOperation.Create)
        {
            After = RuntimeJson.Object(
                ("decisionType", JsonTreeNode.FromString(EngineKeys.DataUseDecisionType)),
                ("subjectKind", JsonTreeNode.FromString(subjectKind)),
                (column, JsonTreeNode.FromString(rowId)),
                ("subjectApplication", mapping ? null : RuntimeJson.OptionalText(row.Text("businessApplication"))),
                ("statement", JsonTreeNode.FromString(statement)),
                ("context", JsonTreeNode.FromString(text)),
                ("reason", JsonTreeNode.FromString(text)),
                ("outcome", RuntimeJson.OptionalText(OutcomeIfActive(call.Snapshot, EngineKeys.DataUseDecisionType, EngineKeys.DataUseAllowOutcome))),
                ("status", JsonTreeNode.FromString(DefaultStatus(call.Snapshot, "decision", "proposed")))),
            Nested = true,
        })!;
        requested = RequestApprovalsIn(call, decision.RowId);
        return decision.RowId;
    }

    internal EvaluationResult<JsonTreeNode?> ComposedRequiredApprovals(RuntimeCall call, string decisionId)
    {
        DecisionScope scope = Scope(call, decisionId);
        return RequiredApprovals(call, scope, StampedSnapshot(call, scope.Decision));
    }

    // Who one requirement of the decision would ask, resolved as a submit resolves it (AskOf): the owner role after its
    // fallback, and the person asked by name. Read only.
    internal AskTarget ComposedAskOf(RuntimeCall call, string decisionId, JsonTreeNode requirement)
    {
        return AskOf(Scope(call, decisionId), requirement);
    }

    // The person behind the call is a person acting directly, or the person an AI client acts for. Any other actor has no
    // person, so no role or owner role of it counts. Roles are the caller's roles now (document 26, RA-01); owner roles are
    // the person's current owner assignments on the decision's application (RA-02).
    internal JsonTreeNode ComposedRequireProposalRight(RuntimeCall call, string decisionId)
    {
        DecisionScope scope = Scope(call, decisionId);
        CompiledSnapshot stamped = StampedSnapshot(call, scope.Decision);
        string? person = call.Actor.ActorKind == ActorKindMeaning.Person ? call.Actor.PrincipalId
            : call.Actor.ActorKind == ActorKindMeaning.AiClientForPerson ? call.Actor.OnBehalfOfPrincipalId ?? call.Actor.PrincipalId
            : null;
        JsonTreeNode input = scope.ProposalRightInput(person == null ? Array.Empty<string>() : call.Actor.RoleKeys, person);
        EvaluationResult<ProposalRightResult?> result = ProposalRightsOperation.Evaluate(stamped.Snapshot, call.Context(stamped, nested: true), input);
        JsonTreeNode payload = result.Payload?.ToJson() ?? JsonTreeNode.Null;
        call.Results["proposal-right"] = RuntimeJson.Object(
            ("outcome", JsonTreeNode.FromString(result.Outcome)),
            ("payload", payload),
            ("configurationVersion", JsonTreeNode.FromInt64(result.ConfigurationVersion)));
        if (result.Outcome != ProposalRightsOperation.Allowed)
        {
            throw CallFailure.From(result.Outcome, result.Reasons);
        }

        return payload;
    }

    internal EvaluationResult<JsonTreeNode?>? ComposedDecisionGateReadiness(RuntimeCall call, string decisionId)
    {
        DecisionScope scope = Scope(call, decisionId);
        string? gate = GateOf(call.Snapshot, scope.DecisionTypeKey);
        if (gate == null || scope.ApplicationKindKey == null)
        {
            return null;
        }

        var subject = new GateSubject(scope.Application, scope.Intake, scope.ApplicationKindKey, scope.TierKey, scope.Owners, scope.LatestAssessment);
        return GateReadiness(call, subject, gate);
    }

    internal EvaluationResult<JsonTreeNode?>? ComposedApprovalStatus(RuntimeCall call, string decisionId, out EvaluationResult<JsonTreeNode?> required, out IReadOnlyList<StoredRow> records)
    {
        DecisionScope scope = Scope(call, decisionId);
        CompiledSnapshot stamped = StampedSnapshot(call, scope.Decision);
        required = RequiredApprovals(call, scope, stamped);
        if (required.Outcome == "engine-accept")
        {
            if (Items(required.Payload!, "requirements").Any())
            {
                // Waiting for a consent (document 25, B25-18): the status of the consent alone.
                return ApprovalStatus(call, scope, stamped, ConsentsOnly(required.Payload!), out records);
            }

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
