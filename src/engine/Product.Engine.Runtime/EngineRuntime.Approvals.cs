using System;
using System.Collections.Generic;
using System.Linq;
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
            for (long n = 0; n < needed[i]; n++)
            {
                ids.Add(OpenApprovalRecord(call, decisionId, counting[i], pending, effect));
            }
        }

        if (status == null)
        {
            foreach (JsonTreeNode consultation in Items(op02, "consultations"))
            {
                ids.Add(OpenApprovalRecord(call, decisionId, consultation, pending, effect));
            }
        }

        return ids;
    }

    // One pending Approval Record for a requirement or consultation, as the engine's nested create (G-06).
    private string OpenApprovalRecord(RuntimeCall call, string decisionId, JsonTreeNode requirement, string pendingStatus, EffectKind? effect)
    {
        long? timeout = RuntimeJson.Number(requirement, "timeoutDays");
        JsonTreeNode after = RuntimeJson.Object(
            ("decision", JsonTreeNode.FromString(decisionId)),
            ("approvalRule", RuntimeJson.OptionalText(RuntimeJson.Text(requirement, "ruleKey"))),
            ("decisionRight", RuntimeJson.OptionalText(RuntimeJson.Text(requirement, "decisionRightKey"))),
            ("requiredRole", RuntimeJson.OptionalText(RuntimeJson.Text(requirement, "approverRoleKey"))),
            ("requiredOwnerRole", RuntimeJson.OptionalText(RuntimeJson.Text(requirement, "approverOwnerRoleKey"))),
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
