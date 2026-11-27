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

// A person's own answer to an Approval Record, as the product's decision page sends it (D-38). It names no responder:
// the responder is the caller, the signed-in person the token names.
public sealed class ApprovalAnswerCommand
{
    // The channel a person's answer on the product's own page is recorded with (the channel value list, C38: in the app).
    public const string InAppChannel = "in-app";

    public ApprovalAnswerCommand(string approvalRecordId, bool approve, string? reason = null, string? channel = null)
    {
        if (string.IsNullOrEmpty(approvalRecordId))
        {
            throw new ArgumentException("An answer names its Approval Record.", nameof(approvalRecordId));
        }

        ApprovalRecordId = approvalRecordId;
        Approve = approve;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason;
        Channel = string.IsNullOrEmpty(channel) ? InAppChannel : channel!;
    }

    public string ApprovalRecordId { get; }

    // True to approve, false to reject.
    public bool Approve { get; }

    // The typed reason, or null. A rejection needs one, and so does an approval where the rule asks for one.
    public string? Reason { get; }

    public string Channel { get; }

    // The request's correlation ID, when the caller has one.
    public string? CorrelationId { get; set; }
}

public sealed partial class EngineRuntime
{
    // A person's own answer to an Approval Record on the product's decision page (D-38; document 26, AN-01 and AN-02 as
    // D-38 rewrites them). The responder is the caller, the signed-in person the token names; nothing in the command
    // names another. One call: the approval answer check for that person under the version the decision was stamped with,
    // then the answer through OP-08 (G-06, the person path), then the recompute the approval runner's answer starts too
    // (OP-03 counts it, the decision is accepted or rejected when the rule is met, the records still pending are
    // cancelled, and a requirement the answer did not meet is asked again). A refusal writes nothing. The payload holds
    // the record, its outcome and whether it counts, the decision's status, the records the recompute opened and
    // cancelled, and the check's own answer.
    public RuntimeOutcome AnswerApproval(CallerIdentity caller, ApprovalAnswerCommand command)
    {
        if (command == null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        return Run(caller, SnapshotPurpose.Write, call => new CallAnswer(RuntimeOutcome.Allowed, command.ApprovalRecordId, false, AnswerApprovalIn(call, command)), command.CorrelationId);
    }

    internal JsonTreeNode ComposedAnswerApproval(RuntimeCall call, ApprovalAnswerCommand command)
    {
        return AnswerApprovalIn(call, command);
    }

    // The approval answer check for the person behind the call on one record, read only (the pre-check of list_approvals
    // and get_decision; D-38, B24-9): would this person's approval count, judged now, exactly as answer_approval judges
    // it? The answer is approve with a typed reason, so the check judges who may answer, not the reason. Nothing is
    // written and nothing ends the call: a refusal is the result's outcome and reasons.
    internal EvaluationResult<ApprovalAnswerResult?> ComposedApprovalAnswerCheck(RuntimeCall call, string approvalRecordId, bool forThePerson = false)
    {
        return AnswerCheck(call, approvalRecordId, approve: true, hasReason: true, out _, forThePerson);
    }

    private EvaluationResult<ApprovalAnswerResult?> AnswerCheck(RuntimeCall call, string id, bool approve, bool hasReason, out string decisionId, bool forThePerson = false)
    {
        StoredRow record = call.Tx.Get("approval-record", id)
            ?? throw CallFailure.Of(RuntimeOutcome.NotFound, ReasonCodes.RefNotFound, "Approval Record " + id + " does not exist.", "Approval Record", id);
        decisionId = record.Text("decision")
            ?? throw CallFailure.Of(OperationOutcomes.InvalidInput, ReasonCodes.InputRequired, "Approval Record " + id + " names no decision.", "Approval Record", id);
        DecisionScope scope = Scope(call, decisionId);
        CompiledSnapshot stamped = StampedSnapshot(call, scope.Decision);
        IReadOnlyList<StoredRow> records = Records(call, decisionId);

        // The person's owner roles now on the decision's application, a deputy's included (document 26, RA-02).
        string person = call.Actor.PrincipalId;
        IEnumerable<string> ownerRoles = scope.Owners
            .Where(o => o.Text("ownerRole") != null && o.Text("principal") is string principal && string.Equals(principal, person, StringComparison.OrdinalIgnoreCase))
            .Select(o => o.Text("ownerRole")!);
        IEnumerable<JsonTreeNode> earlier = records
            .Where(r => r.Text("approver") != null && (r.Text("approvalRule") ?? r.Text("decisionRight")) != null && r.Text("outcomeMeaning") != null)
            .Select(r => RuntimeJson.Object(
                ("approvalRecordId", JsonTreeNode.FromString(r.Id)),
                ("ruleKey", JsonTreeNode.FromString((r.Text("approvalRule") ?? r.Text("decisionRight"))!)),
                ("responderPrincipalId", JsonTreeNode.FromString(r.Text("approver")!)),
                ("outcome", JsonTreeNode.FromString(r.Text("outcomeMeaning")!)),
                ("countsTowardRequirement", JsonTreeNode.FromBoolean(r.Flag("countsTowardRequirement")))));
        JsonTreeNode input = RuntimeJson.Object(
            ("approvalRecordId", JsonTreeNode.FromString(id)),
            ("answer", JsonTreeNode.FromString(approve ? ApprovalAnswerRequest.Approve : ApprovalAnswerRequest.Reject)),
            ("hasReason", JsonTreeNode.FromBoolean(hasReason)),
            ("record", RuntimeJson.Object(
                ("outcome", RuntimeJson.OptionalText(record.Text("outcomeMeaning"))),
                ("approvalRuleKey", RuntimeJson.OptionalText(record.Text("approvalRule"))),
                ("decisionRightKey", RuntimeJson.OptionalText(record.Text("decisionRight"))),
                ("requiredRoleKey", RuntimeJson.OptionalText(record.Text("requiredRole"))),
                ("requiredOwnerRoleKey", RuntimeJson.OptionalText(record.Text("requiredOwnerRole"))),
                ("askedPrincipalId", RuntimeJson.OptionalText(record.Text("askedPrincipal"))),
                ("isConsultation", JsonTreeNode.FromBoolean(record.Flag("isConsultation"))),
                ("expiresOnUtc", AsUtcText(record.Columns, "expiresOn")))),
            ("decisionStatus", RuntimeJson.OptionalText(scope.Decision.Text("statusMeaning"))),
            ("scope", RuntimeJson.Object(
                ("decisionTypeKey", JsonTreeNode.FromString(scope.DecisionTypeKey)),
                ("tierKey", RuntimeJson.OptionalText(scope.TierKey)),
                ("targetStageKey", RuntimeJson.OptionalText(scope.TargetStageKey)))),
            ("parties", scope.SeparationParties()),
            ("acceptedExceptionDecisionTypeKeys", JsonTreeNode.CreateArray()),
            ("ownerRoleKeys", RuntimeJson.Strings(ownerRoles.Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal))),
            ("earlierAnswers", JsonTreeNode.CreateArray(earlier)));

        // For the person an AI client acts for (a read-only pre-check, never an answer): the same check with the person as
        // the actor, so a separation rule that keeps the person from answering shows behind "AI never approves".
        EvaluationContext context = call.Context(stamped);
        if (forThePerson && call.Actor.ActorKind == ActorKindMeaning.AiClientForPerson)
        {
            var asPerson = new ActorInfo(call.Actor.OnBehalfOfPrincipalId ?? call.Actor.PrincipalId, PrincipalKind.User, ActorKindMeaning.Person, call.Actor.RoleKeys);
            context = new EvaluationContext(context.ConfigurationVersion, context.SnapshotHash, context.AsOfUtc, asPerson, context.CorrelationId, context.ClientApp);
        }

        return ApprovalAnswerOperation.Evaluate(stamped.Snapshot, context, input);
    }

    private JsonTreeNode AnswerApprovalIn(RuntimeCall call, ApprovalAnswerCommand command)
    {
        string id = command.ApprovalRecordId;
        string person = call.Actor.PrincipalId;
        EvaluationResult<ApprovalAnswerResult?> result = AnswerCheck(call, id, command.Approve, command.Reason != null, out string decisionId);
        JsonTreeNode check = result.Payload?.ToJson() ?? JsonTreeNode.Null;
        call.Results["approval-answer"] = RuntimeJson.Object(
            ("outcome", JsonTreeNode.FromString(result.Outcome)),
            ("payload", check),
            ("configurationVersion", JsonTreeNode.FromInt64(result.ConfigurationVersion)));
        if (result.Outcome != ApprovalAnswerOperation.Allowed)
        {
            throw CallFailure.From(result.Outcome, result.Reasons);
        }

        call.AddReasons(result.Reasons);

        // The person's own answer, through OP-08 with the check's basis (G-06, the person path). The guard writes Responded
        // On and Approver Holds Required Role itself and plans the recompute, as for the approval runner's answer.
        var columns = new List<(string, JsonTreeNode?)>
        {
            ("outcome", JsonTreeNode.FromString(DefaultStatus(call.Snapshot, "approval-record", command.Approve ? "approved" : "rejected"))),
            ("approver", JsonTreeNode.FromString(person)),
            ("approverActorKind", JsonTreeNode.FromString(ActorKindKey(call.Snapshot, ActorKindMeaning.Person))),
            ("approverPrincipalKind", JsonTreeNode.FromString(EngineContract.PrincipalKinds.ToWire(call.Actor.PrincipalKind))),
            ("respondedOn", JsonTreeNode.FromUtcDateTime(call.AsOfUtc)),
            ("channel", JsonTreeNode.FromString(command.Channel)),
        };
        if (command.Reason != null)
        {
            columns.Add(("typedReason", JsonTreeNode.FromString(command.Reason)));
        }

        GuardedWrite(call, new WriteSpec("approval-record", WriteOperation.Update)
        {
            RowId = id,
            After = RuntimeJson.Object(columns.ToArray()),
            ApprovalAnswer = new ApprovalAnswerBasis(result.Outcome, person, id, command.Approve ? "approved" : "rejected"),
        });

        StoredRow answered = call.Tx.Get("approval-record", id)!;
        List<AppliedWrite> others = call.Writes.Where(w => w.Entity == "approval-record" && w.RowId != id).ToList();
        return RuntimeJson.Object(
            ("approvalRecordId", JsonTreeNode.FromString(id)),
            ("outcome", RuntimeJson.OptionalText(answered.Text("outcomeMeaning"))),
            ("countsTowardRequirement", JsonTreeNode.FromBoolean(answered.Flag("countsTowardRequirement"))),
            ("decisionId", JsonTreeNode.FromString(decisionId)),
            ("decisionStatus", RuntimeJson.OptionalText(call.Tx.Get("decision", decisionId)?.Text("statusMeaning"))),
            ("openedRecordIds", RuntimeJson.Strings(others.Where(w => w.Operation == WriteOperation.Create).Select(w => w.RowId).Distinct(StringComparer.Ordinal))),
            ("cancelledRecordIds", RuntimeJson.Strings(others
                .Where(w => w.Operation == WriteOperation.Update && RuntimeJson.Text(w.Columns, "outcomeMeaning") == "cancelled")
                .Select(w => w.RowId)
                .Distinct(StringComparer.Ordinal))),
            ("check", check));
    }

    // The C38 Actor Kind key the configuration gives a meaning: the row whose key is the meaning's own word, else the only
    // active row with that meaning, else the first by key.
    private static string ActorKindKey(CompiledSnapshot snapshot, ActorKindMeaning meaning)
    {
        string wire = EngineContract.ActorKindMeanings.ToWire(meaning);
        List<Core.Snapshots.SnapshotRow> rows = snapshot.Snapshot.TryGetEntity("actor-kind", out Core.Snapshots.SnapshotEntity? kinds)
            ? kinds.ActiveRows.Where(r => r.EngineMeaning == wire).OrderBy(r => r.KeyPath, StringComparer.Ordinal).ToList()
            : new List<Core.Snapshots.SnapshotRow>();
        if (rows.Count == 0)
        {
            throw CallFailure.Of(OperationOutcomes.Unavailable, ReasonCodes.MeaningMissing, "No active Actor Kind has the meaning " + wire + ", so the engine cannot record a person's answer.", "Actor Kind", null);
        }

        return (rows.FirstOrDefault(r => r.KeyPath == wire) ?? rows[0]).KeyPath;
    }

    // The approval part of the due-state refresh (document 23, S1 step 9 and S2 step 1), for one decision, in one call.
    // While the decision is proposed it runs the recompute: pending records past Expires On get the expired outcome, OP-03
    // counts, the decision settles, and the engine opens the restarts and the next sequential step the requirements still
    // need. For a decision that is no longer proposed it cancels any record still pending. Every write is the engine's
    // own (G-06: expired, cancelled, a new pending record, Counts Toward Requirement), so the call never answers an
    // approval, and a call with nothing to do writes nothing.
    //
    // Only the approval runner calls it: it is the workload that already polls for approval work (document 23, B23-6).
    // The payload lists the records expired, cancelled and opened, and the decision's status meaning afterwards.
    public RuntimeOutcome RefreshApprovals(CallerIdentity caller, string decisionId)
    {
        if (string.IsNullOrEmpty(decisionId))
        {
            throw new ArgumentException("The refresh names its decision.", nameof(decisionId));
        }

        return Run(caller, SnapshotPurpose.Write, call =>
        {
            RequireApprovalRunner(call);
            StoredRow decision = call.Tx.Get("decision", decisionId)
                ?? throw CallFailure.Of(RuntimeOutcome.NotFound, ReasonCodes.RefNotFound, "Decision " + decisionId + " does not exist.", "Decision", decisionId);
            if (decision.Text("statusMeaning") == "proposed")
            {
                RecomputeApprovals(call, decisionId);
            }
            else
            {
                CancelPendingRecords(call, decisionId, null);
            }

            return new CallAnswer(RuntimeOutcome.Allowed, decisionId, false, RefreshPayload(call, decisionId));
        });
    }

    // The decisions the approval refresh has work for, in ID order: those with a pending Approval Record at or past its
    // Expires On, and those no longer proposed that still hold a pending record. At most maxDecisions IDs; "more" says
    // whether others wait. A read: it writes nothing. Only the approval runner calls it, as RefreshApprovals.
    public RuntimeOutcome FindDueApprovals(CallerIdentity caller, int maxDecisions = 100)
    {
        if (maxDecisions < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDecisions), "At least one decision is asked for.");
        }

        return Run(caller, SnapshotPurpose.Read, call =>
        {
            RequireApprovalRunner(call);
            FactSet pending = FactSet.Run(call.Tx, f => new[] { FactPlan.Of(FactQuery.Where("pending", RowQuery.For("approval-record").Where("outcomeMeaning", "pending"))) });
            call.FactRounds += pending.Rounds;
            List<IGrouping<string, StoredRow>> byDecision = pending.All("pending")
                .Where(r => r.Text("decision") != null)
                .GroupBy(r => r.Text("decision")!, StringComparer.Ordinal)
                .ToList();

            FactSet decisions = FactSet.Run(call.Tx, f => new[] { FactPlan.Of(byDecision.Select(g => FactQuery.ById("decision " + g.Key, "decision", g.Key))) });
            call.FactRounds += decisions.Rounds;
            List<string> due = byDecision
                .Where(g =>
                {
                    StoredRow? row = decisions.One("decision " + g.Key);
                    return row != null && (row.Text("statusMeaning") != "proposed" || g.Any(r => IsDue(r, call.AsOfUtc)));
                })
                .Select(g => g.Key)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();

            JsonTreeNode payload = RuntimeJson.Object(
                ("decisionIds", RuntimeJson.Strings(due.Take(maxDecisions))),
                ("more", JsonTreeNode.FromBoolean(due.Count > maxDecisions)));
            return new CallAnswer(RuntimeOutcome.Allowed, null, false, payload);
        });
    }

    // Opens the pending Approval Records a proposed decision still needs (document 23, S1 step 3: one record per required
    // response). Each counting requirement that is neither satisfied nor rejected gets records until its counting
    // approvals and its open records together reach Minimum Count, whatever the approval mode (document 25, section 5.5:
    // everyone must approve is satisfied at Minimum Count too). A sequential requirement opens only when every earlier
    // sequential requirement is satisfied, in the order OP-03 counts them (sequence, then key), so its cards go out one
    // step at a time. A consultation gets one record at the first request and is never restarted. status is OP-03's
    // payload, or null at the first request, when the decision has no record yet. Records are created in OP-02's order.
    private IReadOnlyList<string> OpenApprovalRecords(RuntimeCall call, string decisionId, JsonTreeNode op02, JsonTreeNode? status, EffectKind? effect)
    {
        // Who each record asks (document 25, B25-14) comes from the decision's scope as it stands now.
        DecisionScope scope = Scope(call, decisionId);
        IReadOnlyList<StoredRow> existing = status == null ? Array.Empty<StoredRow>() : Records(call, decisionId);
        var states = new Dictionary<string, JsonTreeNode>(StringComparer.Ordinal);
        foreach (JsonTreeNode state in Items(status ?? RuntimeJson.Empty, "requirements"))
        {
            string? key = RuntimeJson.Text(state, "requirementKey");
            if (key != null)
            {
                states[key] = state;
            }
        }

        List<JsonTreeNode> counting = Items(op02, "requirements").Where(r => !RuntimeJson.Flag(r, "isConsultation")).ToList();
        var needed = new long[counting.Count];
        bool earlierSequentialSatisfied = true;
        IEnumerable<int> order = Enumerable.Range(0, counting.Count)
            .OrderBy(i => RuntimeJson.Number(counting[i], "sequence") ?? 0)
            .ThenBy(i => RequirementKey(counting[i]), StringComparer.Ordinal);
        foreach (int i in order)
        {
            JsonTreeNode requirement = counting[i];
            states.TryGetValue(RequirementKey(requirement), out JsonTreeNode? state);
            string current = RuntimeJson.Text(state, "state") ?? "pending";
            bool sequential = RuntimeJson.Text(requirement, "approvalMode") == EngineContract.ApprovalModes.ToWire(ApprovalMode.Sequential);
            bool waiting = sequential && !earlierSequentialSatisfied;
            if (sequential && current != "satisfied")
            {
                earlierSequentialSatisfied = false;
            }

            if (waiting || current == "satisfied" || current == "rejected")
            {
                continue;
            }

            long held = RuntimeJson.StringList(state, "countingRecordIds").Count + RuntimeJson.StringList(state, "pendingRecordIds").Count;
            needed[i] = Math.Max(0, (RuntimeJson.Number(requirement, "minimumCount") ?? 1) - held);
        }

        string pending = DefaultStatus(call.Snapshot, "approval-record", "pending");
        var ids = new List<string>();
        for (int i = 0; i < counting.Count; i++)
        {
            if (needed[i] == 0)
            {
                continue;
            }

            if (RuntimeJson.Text(counting[i], "ownerSubject") == "touched-data-domains")
            {
                // One record per acting data owner of a touched domain who has none yet (docs/architecture/data-taxonomy.md, Approval rules).
                states.TryGetValue(RequirementKey(counting[i]), out JsonTreeNode? touchedState);
                var touchedHeld = new HashSet<string>(RuntimeJson.StringList(touchedState, "countingRecordIds").Concat(RuntimeJson.StringList(touchedState, "pendingRecordIds")), StringComparer.Ordinal);
                string role = RuntimeJson.Text(counting[i], "approverOwnerRoleKey") ?? EngineKeys.DataOwnerRole;
                IReadOnlyList<string> people = TouchedOwners(scope, role);
                foreach (string person in people.Where(p => !existing.Any(r => touchedHeld.Contains(r.Id) && string.Equals(r.Text("askedPrincipal"), p, StringComparison.OrdinalIgnoreCase))))
                {
                    ids.Add(OpenApprovalRecord(call, decisionId, counting[i], new AskTarget(null, role, person), pending, effect));
                }

                if (people.Count == 0 && !existing.Any(r => touchedHeld.Contains(r.Id)))
                {
                    if (status == null)
                    {
                        throw NoOneToAsk(counting[i], "the domains this change touches have no data owner");
                    }

                    ids.Add(OpenApprovalRecord(call, decisionId, counting[i], new AskTarget(null, role, null), pending, effect));
                }

                continue;
            }

            if (RuntimeJson.Flag(counting[i], "approverProposedOwners"))
            {
                // One record per proposed owner who has no pending record and no counting approval for it yet.
                states.TryGetValue(RequirementKey(counting[i]), out JsonTreeNode? state);
                var held = new HashSet<string>(RuntimeJson.StringList(state, "countingRecordIds").Concat(RuntimeJson.StringList(state, "pendingRecordIds")), StringComparer.Ordinal);
                foreach (string person in scope.ProposedOwnerPrincipals())
                {
                    bool asked = existing.Any(r => held.Contains(r.Id) && string.Equals(r.Text("askedPrincipal"), person, StringComparison.OrdinalIgnoreCase));
                    if (!asked)
                    {
                        ids.Add(OpenApprovalRecord(call, decisionId, counting[i], new AskTarget(null, scope.ProposedOwnerRole(person), person), pending, effect));
                    }
                }

                continue;
            }

            for (long n = 0; n < needed[i]; n++)
            {
                AskTarget target = AskOf(scope, counting[i]);
                if (status == null && target.PrincipalId == null && IsDataOwnerSubject(RuntimeJson.Text(counting[i], "ownerSubject")))
                {
                    // Never a counting record that names nobody for a data owner subject (security review, major): the
                    // submit is refused (DC-06), and the person proposes a data owner first.
                    throw NoOneToAsk(counting[i], "the " + (RuntimeJson.Text(counting[i], "approverOwnerRoleKey") ?? EngineKeys.DataOwnerRole).Replace('-', ' ') + " it asks has no holder now");
                }

                ids.Add(OpenApprovalRecord(call, decisionId, counting[i], target, pending, effect));
            }
        }

        if (status == null)
        {
            foreach (JsonTreeNode consultation in Items(op02, "consultations"))
            {
                if (RuntimeJson.Text(consultation, "ownerSubject") == "touched-data-domains")
                {
                    string role = RuntimeJson.Text(consultation, "approverOwnerRoleKey") ?? EngineKeys.DataOwnerRole;
                    foreach (string person in TouchedOwners(scope, role))
                    {
                        ids.Add(OpenApprovalRecord(call, decisionId, consultation, new AskTarget(null, role, person), pending, effect));
                    }

                    continue;
                }

                AskTarget ask = AskOf(scope, consultation);
                if (ask.PrincipalId == null && ask.RoleKey == null && RuntimeJson.Text(consultation, "ownerSubject") != null)
                {
                    // A consultation of a data owner subject with no holder is skipped (OP-02; docs/architecture/data-taxonomy.md, Engine).
                    continue;
                }

                ids.Add(OpenApprovalRecord(call, decisionId, consultation, ask, pending, effect));
            }
        }

        return ids;
    }

    private static bool IsDataOwnerSubject(string? ownerSubject)
    {
        return ownerSubject == "data-domain" || ownerSubject == "parent-data-domain" || ownerSubject == "data-location" || ownerSubject == "touched-data-domains";
    }

    private static CallFailure NoOneToAsk(JsonTreeNode requirement, string why)
    {
        string? rule = RuntimeJson.Text(requirement, "ruleKey");
        return CallFailure.Of(
            OperationOutcomes.Refused,
            ReasonCodes.DataNoOneToAsk,
            "DC-06: nobody can answer this decision now, because " + why + ". Propose a data owner first, then submit the decision.",
            "Approval Rule",
            rule);
    }

    // Who a record for a role or owner role requirement asks: the role, or the owner role with its fallback.
    private static AskTarget AskOf(DecisionScope scope, JsonTreeNode requirement)
    {
        AskTarget? data = DataAsk(scope, requirement);
        if (data != null)
        {
            return data;
        }

        return scope.Ask(
            RuntimeJson.Text(requirement, "approverRoleKey"),
            RuntimeJson.Text(requirement, "approverOwnerRoleKey"),
            RuntimeJson.Text(requirement, "fallbackOwnerRoleKey"));
    }

    // One pending Approval Record for a requirement or consultation, as the engine's nested create (G-06). ask names the
    // role, the owner role and the person asked by name (Asked Principal), as the scope resolved them.
    private string OpenApprovalRecord(RuntimeCall call, string decisionId, JsonTreeNode requirement, AskTarget ask, string pendingStatus, EffectKind? effect)
    {
        long? timeout = RuntimeJson.Number(requirement, "timeoutDays");
        JsonTreeNode after = RuntimeJson.Object(
            ("decision", JsonTreeNode.FromString(decisionId)),
            ("approvalRule", RuntimeJson.OptionalText(RuntimeJson.Text(requirement, "ruleKey"))),
            ("decisionRight", RuntimeJson.OptionalText(RuntimeJson.Text(requirement, "decisionRightKey"))),
            ("requiredRole", RuntimeJson.OptionalText(ask.RoleKey)),
            ("requiredOwnerRole", RuntimeJson.OptionalText(ask.OwnerRoleKey)),
            ("askedPrincipal", RuntimeJson.OptionalText(ask.PrincipalId)),
            ("isConsultation", JsonTreeNode.FromBoolean(RuntimeJson.Flag(requirement, "isConsultation"))),
            ("outcome", JsonTreeNode.FromString(pendingStatus)),
            ("requestedOn", JsonTreeNode.FromUtcDateTime(call.AsOfUtc)),
            ("expiresOn", timeout.HasValue ? JsonTreeNode.FromUtcDateTime(call.AsOfUtc.AddDays(timeout.Value)) : null),
            ("countsTowardRequirement", JsonTreeNode.False));
        WriteStep step = GuardedWrite(call, new WriteSpec("approval-record", WriteOperation.Create) { After = after, Nested = true, Effect = effect })!;
        return step.RowId;
    }

    // The expired outcome on every pending record at or past its Expires On (document 22, section 8.2: the engine writes
    // expired while the record is pending). OP-03 reads such a record as expired already, so the write changes no count;
    // it closes the card's outbox row, and the recompute then opens the restart. Returns the expired records' IDs.
    private IReadOnlyList<string> ExpireDueRecords(RuntimeCall call, IReadOnlyList<StoredRow> records)
    {
        List<StoredRow> due = records.Where(r => IsDue(r, call.AsOfUtc)).ToList();
        if (due.Count == 0)
        {
            return Array.Empty<string>();
        }

        string expired = DefaultStatus(call.Snapshot, "approval-record", "expired");
        foreach (StoredRow record in due)
        {
            GuardedWrite(call, new WriteSpec("approval-record", WriteOperation.Update)
            {
                RowId = record.Id,
                After = RuntimeJson.Object(("outcome", JsonTreeNode.FromString(expired))),
                Nested = true,
                Effect = EffectKind.RecomputeApprovals,
            });
        }

        return due.Select(r => r.Id).ToList();
    }

    // The cancelled outcome on every pending record of the decision (document 22, section 8.2), as engine-nested writes
    // that OP-08 judges (G-06: only a pending record, only once). Returns the cancelled records' IDs.
    private IReadOnlyList<string> CancelPendingRecords(RuntimeCall call, string decisionId, EffectKind? effect)
    {
        List<StoredRow> open = Records(call, decisionId).Where(r => r.Text("outcomeMeaning") == "pending").ToList();
        if (open.Count == 0)
        {
            return Array.Empty<string>();
        }

        string cancelled = DefaultStatus(call.Snapshot, "approval-record", "cancelled");
        foreach (StoredRow record in open)
        {
            GuardedWrite(call, new WriteSpec("approval-record", WriteOperation.Update)
            {
                RowId = record.Id,
                After = RuntimeJson.Object(("outcome", JsonTreeNode.FromString(cancelled))),
                Nested = true,
                Effect = effect,
            });
        }

        return open.Select(r => r.Id).ToList();
    }

    // How long before its Expires On a pending request may be asked again early (document 25, B25-24): the demo kit's
    // refresh renews every story request that would lapse within two days, and a person may do the same.
    internal static readonly TimeSpan RenewWindow = TimeSpan.FromHours(48);

    // "Ask again" (ComposedCall.RenewApprovalRequest; document 25, B25-24). The steps: the request and its decision; who
    // asks (the proposer or a platform admin, as a person); whether the request may be asked again (expired, or pending in
    // its last 48 hours, and not asked again already); the engine's own writes (expired or canceled on the old request,
    // then the recompute opens the restart, or the consultation is opened again); and nobody asked for a data owner
    // subject refuses the whole call (DC-06), as a first request does.
    internal JsonTreeNode ComposedRenewApprovalRequest(RuntimeCall call, string approvalRecordId)
    {
        StoredRow record = call.Tx.Get("approval-record", approvalRecordId)
            ?? throw CallFailure.Of(RuntimeOutcome.NotFound, ReasonCodes.RefNotFound, "Approval Record " + approvalRecordId + " does not exist.", "Approval Record", approvalRecordId);
        string decisionId = record.Text("decision")
            ?? throw CallFailure.Of(OperationOutcomes.InvalidInput, ReasonCodes.InputRequired, "Approval Record " + approvalRecordId + " names no decision.", "Approval Record", approvalRecordId);
        DecisionScope scope = Scope(call, decisionId);
        string number = scope.Decision.Text("recordNumber") ?? decisionId;
        string? status = scope.Decision.Text("statusMeaning");
        if (status != "proposed")
        {
            throw CallFailure.Of(OperationOutcomes.Refused, ReasonCodes.DecisionFinal, "Decision " + number + " is " + (status ?? "without a status") + ", so its requests are not asked again.", "Decision", decisionId);
        }

        RequireRenewer(call, scope.Decision, number);

        string? outcome = record.Text("outcomeMeaning");
        DateTime? expiresOn = RuntimeJson.Utc(record.Columns, "expiresOn");
        bool pending = outcome == "pending";
        bool lapsed = outcome == "expired" || (pending && expiresOn.HasValue && call.AsOfUtc >= expiresOn.Value);
        bool early = pending && !lapsed && expiresOn.HasValue && expiresOn.Value - call.AsOfUtc <= RenewWindow;
        // Only the newest record of an ask is asked again (wave 4b review; D-41, document 25, B25-24): the newest record of the
        // same request asked after this one, in any outcome. Asked after: requested later, or at the same moment while this
        // one is closed and that one pending (the close and the restart of one call). Two records asked together, both
        // pending or both closed, are two answers of one rule, not one asked again as the other.
        IReadOnlyList<StoredRow> records = Records(call, decisionId);
        string requestedOn = record.Text("requestedOn") ?? string.Empty;
        StoredRow? newest = records
            .Where(r => r.Id != record.Id && SameRequest(r, record))
            .Where(r =>
            {
                int order = string.CompareOrdinal(r.Text("requestedOn") ?? string.Empty, requestedOn);
                return order > 0 || (order == 0 && !pending && r.Text("outcomeMeaning") == "pending");
            })
            .OrderByDescending(r => r.Text("requestedOn") ?? string.Empty, StringComparer.Ordinal)
            .ThenByDescending(r => r.Text("outcomeMeaning") == "pending" ? 1 : 0)
            .ThenByDescending(r => r.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (newest != null)
        {
            if (newest.Text("outcomeMeaning") == "pending" && !IsDue(newest, call.AsOfUtc))
            {
                // Asked again already (by a person, or by the daily refresh's restart): nothing to write.
                return RenewPayload(record, decisionId, false, outcome, new[] { newest.Id }, RuntimeJson.Utc(newest.Columns, "expiresOn"));
            }

            string? newestOutcome = newest.Text("outcomeMeaning");
            bool askable = newestOutcome == "expired" || newestOutcome == "pending";
            throw CallFailure.Of(
                OperationOutcomes.Refused,
                ReasonCodes.ApprovalNotRenewable,
                "Approval request " + approvalRecordId + " was asked again as " + newest.Id + (askable ? "; ask that one again." : ", which is " + (newestOutcome == "cancelled" ? "canceled" : newestOutcome ?? "closed") + ", so nothing is asked again."),
                "Approval Record",
                approvalRecordId);
        }

        if (!lapsed && !early)
        {
            string why = pending
                ? "is pending until " + (expiresOn.HasValue ? expiresOn.Value.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " UTC" : "it is answered") + ", which is more than 48 hours away"
                : "is " + (outcome == "cancelled" ? "canceled" : outcome ?? "closed");
            throw CallFailure.Of(OperationOutcomes.Refused, ReasonCodes.ApprovalNotRenewable, "Approval request " + approvalRecordId + " of decision " + number + " " + why + ". Only an expired request, or one in the last 48 hours of its time, is asked again.", "Approval Record", approvalRecordId);
        }

        int before = call.Writes.Count;
        if (pending)
        {
            // The old request closes first, as the engine's own write: expired when its time came, canceled when it is
            // asked again early (G-06: only a pending record, only once).
            GuardedWrite(call, new WriteSpec("approval-record", WriteOperation.Update)
            {
                RowId = record.Id,
                After = RuntimeJson.Object(("outcome", JsonTreeNode.FromString(DefaultStatus(call.Snapshot, "approval-record", lapsed ? "expired" : "cancelled")))),
                Nested = true,
            });
        }

        if (record.Flag("isConsultation"))
        {
            // A consultation is never restarted by the recompute, so it is opened again from OP-02, as the first request
            // opened it.
            CompiledSnapshot stamped = StampedSnapshot(call, scope.Decision);
            EvaluationResult<JsonTreeNode?> required = RequiredApprovals(call, Scope(call, decisionId), stamped);
            JsonTreeNode? consultation = Items(required.Payload ?? RuntimeJson.Empty, "consultations")
                .FirstOrDefault(c => RequirementKey(c) == (record.Text("approvalRule") ?? record.Text("decisionRight") ?? string.Empty));
            if (consultation != null)
            {
                DecisionScope now = Scope(call, decisionId);
                AskTarget ask = AskOf(now, consultation);
                if (ask.PrincipalId != null || ask.RoleKey != null || RuntimeJson.Text(consultation, "ownerSubject") == null)
                {
                    OpenApprovalRecord(call, decisionId, consultation, ask, DefaultStatus(call.Snapshot, "approval-record", "pending"), null);
                }
            }
        }
        else
        {
            RecomputeApprovals(call, decisionId);
        }

        List<string> opened = call.Writes.Skip(before)
            .Where(w => w.Entity == "approval-record" && w.Operation == WriteOperation.Create)
            .Select(w => w.RowId).Distinct(StringComparer.Ordinal).ToList();
        if (opened.Count == 0)
        {
            throw CallFailure.Of(OperationOutcomes.Refused, ReasonCodes.ApprovalNotRenewable, "Decision " + number + " needs no new answer for this request: the requirement it asked is met, or nobody holds the role it asks now.", "Approval Record", approvalRecordId);
        }

        foreach (string id in opened)
        {
            StoredRow? fresh = call.Tx.Get("approval-record", id);
            if (fresh != null && fresh.Text("askedPrincipal") == null && fresh.Text("requiredRole") == null && IsDataDecision(scope.Decision))
            {
                // Never a counting record that names nobody for a data owner subject (docs/architecture/data-taxonomy.md).
                throw CallFailure.Of(OperationOutcomes.Refused, ReasonCodes.DataNoOneToAsk, "DC-06: nobody can answer this request now, because the " + (fresh.Text("requiredOwnerRole") ?? EngineKeys.DataOwnerRole).Replace('-', ' ') + " it asks has no holder. Propose a data owner first, then ask again.", "Approval Record", approvalRecordId);
            }
        }

        StoredRow first = call.Tx.Get("approval-record", opened[0])!;
        return RenewPayload(record, decisionId, true, outcome, opened, RuntimeJson.Utc(first.Columns, "expiresOn"));
    }

    // The proposer of the decision (the admin who instructed a configuration draft, else the person an AI client drafted
    // for, else its creator), or a holder of the platform admin role, signed in as themselves (D-36: a person decides to
    // ask again; an AI client is never served the tool).
    private static void RequireRenewer(RuntimeCall call, StoredRow decision, string number)
    {
        ActorInfo actor = call.Actor;
        if (actor.ActorKind != ActorKindMeaning.Person)
        {
            throw CallFailure.Of(OperationOutcomes.Refused, ReasonCodes.ApprovalRenewNotAllowed, "A person asks an approval request again, in the web app. The caller is " + EngineContract.ActorKindMeanings.ToWire(actor.ActorKind) + ".", "Decision", decision.Id);
        }

        string? proposer = decision.Text("subjectConfigurationDraft") is string draftId ? call.Tx.Get("configuration-draft", draftId)?.Text("instructedBy") : null;
        proposer ??= decision.Text("draftedForPrincipal") ?? decision.Text("createdByPrincipal");
        if (actor.HoldsRole(EngineKeys.PlatformAdminRole) || (proposer != null && string.Equals(proposer, actor.PrincipalId, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        throw CallFailure.Of(OperationOutcomes.Refused, ReasonCodes.ApprovalRenewNotAllowed, "Only the person who proposed decision " + number + ", or a platform admin, asks its requests again.", "Decision", decision.Id);
    }

    // The same request: the same rule or decision right, the same consultation flag, and the same role or owner role.
    private static bool SameRequest(StoredRow a, StoredRow b)
    {
        return string.Equals(a.Text("approvalRule"), b.Text("approvalRule"), StringComparison.Ordinal)
            && string.Equals(a.Text("decisionRight"), b.Text("decisionRight"), StringComparison.Ordinal)
            && a.Flag("isConsultation") == b.Flag("isConsultation")
            && string.Equals(a.Text("requiredRole"), b.Text("requiredRole"), StringComparison.Ordinal)
            && string.Equals(a.Text("requiredOwnerRole"), b.Text("requiredOwnerRole"), StringComparison.Ordinal);
    }

    private static bool IsDataDecision(StoredRow decision)
    {
        return decision.Text("subjectKind") is "data-use" or "data-mapping" or "data-domain" or "data-location";
    }

    private static JsonTreeNode RenewPayload(StoredRow record, string decisionId, bool renewed, string? previousOutcome, IEnumerable<string> opened, DateTime? expiresOn)
    {
        return RuntimeJson.Object(
            ("approvalRecordId", JsonTreeNode.FromString(record.Id)),
            ("decisionId", JsonTreeNode.FromString(decisionId)),
            ("renewed", JsonTreeNode.FromBoolean(renewed)),
            ("previousOutcome", RuntimeJson.OptionalText(previousOutcome)),
            ("openedRecordIds", RuntimeJson.Strings(opened)),
            ("expiresOn", expiresOn.HasValue ? JsonTreeNode.FromUtcDateTime(expiresOn.Value) : JsonTreeNode.Null));
    }

    // A pending record whose Expires On has come (OP-03 judges the same moment as expired).
    private static bool IsDue(StoredRow record, DateTime asOfUtc)
    {
        if (record.Text("outcomeMeaning") != "pending")
        {
            return false;
        }

        DateTime? expiresOn = RuntimeJson.Utc(record.Columns, "expiresOn");
        return expiresOn.HasValue && asOfUtc >= expiresOn.Value;
    }

    private static string RequirementKey(JsonTreeNode requirement)
    {
        return RuntimeJson.Text(requirement, "ruleKey") ?? RuntimeJson.Text(requirement, "decisionRightKey") ?? string.Empty;
    }

    // The approval refresh is the approval runner's: an app-only workload principal holding the approval writer role
    // through a current Workload Grant (document 26, RA-07), as the guard's G-06 reads it.
    private static void RequireApprovalRunner(RuntimeCall call)
    {
        ActorInfo actor = call.Actor;
        bool workload = actor.PrincipalKind == PrincipalKind.ServicePrincipal || actor.PrincipalKind == PrincipalKind.ManagedIdentity;
        if (actor.ActorKind == ActorKindMeaning.ApprovalRunner && workload && actor.HoldsRole(EngineKeys.ApprovalWriterRole))
        {
            return;
        }

        throw CallFailure.Of(
            OperationOutcomes.Refused,
            ReasonCodes.ApprovalRefreshNotAllowed,
            "Only the approval runner, holding a current " + EngineKeys.ApprovalWriterRole + " workload grant, runs the approval refresh. The caller is "
            + EngineContract.ActorKindMeanings.ToWire(actor.ActorKind) + ".",
            "Workload Grant",
            EngineKeys.ApprovalWriterRole);
    }

    // What a refresh did, from the call's own writes.
    private static JsonTreeNode RefreshPayload(RuntimeCall call, string decisionId)
    {
        List<AppliedWrite> records = call.Writes.Where(w => w.Entity == "approval-record").ToList();
        IEnumerable<string> Updated(string meaning) => records
            .Where(w => w.Operation == WriteOperation.Update && RuntimeJson.Text(w.Columns, "outcomeMeaning") == meaning)
            .Select(w => w.RowId)
            .Distinct(StringComparer.Ordinal);

        return RuntimeJson.Object(
            ("decisionId", JsonTreeNode.FromString(decisionId)),
            ("decisionStatus", RuntimeJson.OptionalText(call.Tx.Get("decision", decisionId)?.Text("statusMeaning"))),
            ("expiredRecordIds", RuntimeJson.Strings(Updated("expired"))),
            ("cancelledRecordIds", RuntimeJson.Strings(Updated("cancelled"))),
            ("openedRecordIds", RuntimeJson.Strings(records.Where(w => w.Operation == WriteOperation.Create).Select(w => w.RowId))));
    }
}
