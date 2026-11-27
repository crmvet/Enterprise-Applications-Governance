using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;

namespace Product.Azure.Stores;

// The approval outbox: one row per Approval Record the engine opens, so the approval runner can pull the work and send a
// card (document 23, S1, step 4). A row holds IDs, states and times only: never a name, a statement or any other text a
// person wrote. The row store writes it in the same database transaction as the engine's rows (the commit hook in
// RowStore), so a committed pending Approval Record always has its row and a rolled back engine write leaves none. When
// the engine writes the record's final outcome, the same transaction closes the row.
//
// The engine knows nothing of the outbox: it is a host table (host.ApprovalOutbox on Azure SQL, ApprovalOutbox in
// SQLite) outside the entity catalog, so engine dumps and the parity tests with the in-memory store leave it out.

// The states of an outbox row.
public static class ApprovalOutboxStates
{
    // Waiting for the approval runner to claim it.
    public const string Pending = "pending";

    // Leased to one runner call until Claimed Until. A lease that runs out makes the row claimable again.
    public const string Claimed = "claimed";

    // A card went out (Card Ref). The runner now waits for the answer.
    public const string Sent = "sent";

    // The Approval Record holds an answer (approved, rejected or recommendation only).
    public const string Answered = "answered";

    // The Approval Record passed its Expires On, or the engine expired it.
    public const string Expired = "expired";

    // The engine cancelled the Approval Record, or its decision is no longer proposed, so no card is needed.
    public const string Cancelled = "cancelled";

    // The runner released the row as many times as the host allows, so it stopped trying. An admin looks at it.
    public const string Failed = "failed";

    public static readonly IReadOnlyList<string> All = new ReadOnlyCollection<string>(new[] { Pending, Claimed, Sent, Answered, Expired, Cancelled, Failed });

    // True when the Approval Record is settled for the outbox: answered, expired or cancelled. A settled row never
    // changes again.
    public static bool IsSettled(string state)
    {
        return state == Answered || state == Expired || state == Cancelled;
    }

    // True when the runner may still act on the row: pending, claimed or sent.
    public static bool IsOpen(string state)
    {
        return state == Pending || state == Claimed || state == Sent;
    }

    // The outbox state for an Approval Record's outcome meaning: answered, expired or cancelled; null while pending.
    public static string? ForOutcomeMeaning(string? outcomeMeaning)
    {
        if (!EngineContract.ApprovalOutcomeMeanings.TryParse(outcomeMeaning, out ApprovalOutcomeMeaning meaning))
        {
            return null;
        }

        switch (meaning)
        {
            case ApprovalOutcomeMeaning.Approved:
            case ApprovalOutcomeMeaning.Rejected:
            case ApprovalOutcomeMeaning.RecommendationOnly:
                return Answered;
            case ApprovalOutcomeMeaning.Expired:
                return Expired;
            case ApprovalOutcomeMeaning.Cancelled:
                return Cancelled;
            default:
                return null;
        }
    }
}

// One outbox row. Immutable: a change is a new item with the next row version, written with a compare and set on the
// version it was read with.
public sealed class ApprovalOutboxItem
{
    public ApprovalOutboxItem(
        string approvalRecordId,
        string decisionId,
        DateTime enqueuedOnUtc,
        string state,
        string? claimId,
        DateTime? claimedUntilUtc,
        int attempts,
        string? cardRef,
        string? runnerRunUrl,
        DateTime updatedOnUtc,
        long rowVersion)
    {
        if (string.IsNullOrEmpty(approvalRecordId))
        {
            throw new ArgumentException("An outbox row names its Approval Record.", nameof(approvalRecordId));
        }

        if (state == null || !ApprovalOutboxStates.All.Contains(state))
        {
            throw new ArgumentException("The outbox state \"" + state + "\" is not one of " + string.Join(", ", ApprovalOutboxStates.All) + ".", nameof(state));
        }

        ApprovalRecordId = approvalRecordId;
        DecisionId = decisionId ?? string.Empty;
        EnqueuedOnUtc = Utc(enqueuedOnUtc);
        State = state;
        ClaimId = string.IsNullOrEmpty(claimId) ? null : claimId;
        ClaimedUntilUtc = claimedUntilUtc.HasValue ? Utc(claimedUntilUtc.Value) : null;
        Attempts = attempts;
        CardRef = string.IsNullOrEmpty(cardRef) ? null : cardRef;
        RunnerRunUrl = string.IsNullOrEmpty(runnerRunUrl) ? null : runnerRunUrl;
        UpdatedOnUtc = Utc(updatedOnUtc);
        RowVersion = rowVersion;
    }

    public string ApprovalRecordId { get; }

    public string DecisionId { get; }

    // When the engine opened the Approval Record (its Requested On): the claim order.
    public DateTime EnqueuedOnUtc { get; }

    public string State { get; }

    // The lease of the runner call that claimed the row last.
    public string? ClaimId { get; }

    public DateTime? ClaimedUntilUtc { get; }

    // How many times a runner gave the row back (release, or a lease that ran out).
    public int Attempts { get; }

    // The runner's reference of the card it sent, such as the Power Automate approval ID.
    public string? CardRef { get; }

    // An https link to the runner's run that sent the card.
    public string? RunnerRunUrl { get; }

    public DateTime UpdatedOnUtc { get; }

    // Starts at 1 and grows by 1 on every change.
    public long RowVersion { get; }

    // The same row with these values and the next row version.
    public ApprovalOutboxItem Next(string state, DateTime updatedOnUtc, string? claimId, DateTime? claimedUntilUtc, int attempts, string? cardRef, string? runnerRunUrl)
    {
        return new ApprovalOutboxItem(ApprovalRecordId, DecisionId, EnqueuedOnUtc, state, claimId, claimedUntilUtc, attempts, cardRef, runnerRunUrl, updatedOnUtc, RowVersion + 1);
    }

    public override string ToString()
    {
        return "approval outbox " + ApprovalRecordId + " " + State + " (version " + RowVersion.ToString(CultureInfo.InvariantCulture) + ")";
    }

    // The text form the SQLite store keeps: sortable, seven fractional digits, UTC.
    public static string FormatUtc(DateTime value)
    {
        return Utc(value).ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
    }

    public static DateTime ParseUtc(string text)
    {
        return DateTime.ParseExact(text, "yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
    }

    private static DateTime Utc(DateTime value)
    {
        return value.Kind == DateTimeKind.Utc ? value : value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }
}

// An outbox write that goes into an engine commit: open a row for a new pending Approval Record, or settle the row of a
// record whose outcome became final. Both are idempotent: an open row is never opened twice and a settled row never
// changes.
public sealed class ApprovalOutboxWrite
{
    private ApprovalOutboxWrite(bool isEnqueue, string approvalRecordId, string decisionId, string state, DateTime onUtc)
    {
        IsEnqueue = isEnqueue;
        ApprovalRecordId = approvalRecordId;
        DecisionId = decisionId;
        State = state;
        OnUtc = onUtc.Kind == DateTimeKind.Utc ? onUtc : DateTime.SpecifyKind(onUtc, DateTimeKind.Utc);
    }

    // True for a new pending row; false for a settle.
    public bool IsEnqueue { get; }

    public string ApprovalRecordId { get; }

    // The record's Decision (enqueue only; empty for a settle).
    public string DecisionId { get; }

    // Pending for an enqueue; answered, expired or cancelled for a settle.
    public string State { get; }

    public DateTime OnUtc { get; }

    public static ApprovalOutboxWrite Enqueue(string approvalRecordId, string decisionId, DateTime requestedOnUtc)
    {
        return new ApprovalOutboxWrite(true, approvalRecordId, decisionId ?? string.Empty, ApprovalOutboxStates.Pending, requestedOnUtc);
    }

    public static ApprovalOutboxWrite Settle(string approvalRecordId, string state, DateTime onUtc)
    {
        if (!ApprovalOutboxStates.IsSettled(state))
        {
            throw new ArgumentException("A settle moves a row to answered, expired or cancelled.", nameof(state));
        }

        return new ApprovalOutboxWrite(false, approvalRecordId, string.Empty, state, onUtc);
    }

    public override string ToString()
    {
        return (IsEnqueue ? "enqueue " : "settle ") + ApprovalRecordId + " " + State;
    }
}

// The commit hook: the outbox writes an engine commit implies. It reads only the Approval Record changes of the commit
// and decides nothing about approvals: the engine already did.
public static class ApprovalOutboxHook
{
    public const string ApprovalRecordEntity = "approval-record";

    public static IReadOnlyList<ApprovalOutboxWrite> Plan(IReadOnlyList<RowChange> changes, DateTime nowUtc)
    {
        if (changes == null)
        {
            throw new ArgumentNullException(nameof(changes));
        }

        var writes = new List<ApprovalOutboxWrite>();
        foreach (RowChange change in changes)
        {
            if (change.Kind == RowChangeKind.Delete || change.Columns == null || !string.Equals(change.Entity.Key, ApprovalRecordEntity, StringComparison.Ordinal))
            {
                continue;
            }

            string? meaning = Text(change.Columns, "outcomeMeaning");
            string? decision = Text(change.Columns, "decision");
            if (change.Kind == RowChangeKind.Insert && string.Equals(meaning, "pending", StringComparison.Ordinal) && decision != null)
            {
                // A new request, or a restart (a restarted approval is a new row, document 22, O10).
                writes.Add(ApprovalOutboxWrite.Enqueue(change.Id, decision, Moment(change.Columns, "requestedOn") ?? nowUtc));
                continue;
            }

            string? settled = ApprovalOutboxStates.ForOutcomeMeaning(meaning);
            if (settled != null)
            {
                writes.Add(ApprovalOutboxWrite.Settle(change.Id, settled, nowUtc));
            }
        }

        return writes;
    }

    private static string? Text(JsonTreeNode columns, string name)
    {
        return columns.TryGetMember(name, out JsonTreeNode? value) && value.Kind == JsonTreeKind.String ? value.GetString() : null;
    }

    private static DateTime? Moment(JsonTreeNode columns, string name)
    {
        string? text = Text(columns, name);
        return text != null && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime value)
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : null;
    }
}

// The outbox rows a store keeps, read and changed outside engine transactions. Every change is a compare and set on the
// row version, so two runner calls never both win the same row.
public interface IApprovalOutboxStore
{
    // One row, or null.
    ApprovalOutboxItem? GetOutboxItem(string approvalRecordId);

    // Rows a runner may claim now: pending, or claimed with a lease that ran out, oldest Enqueued On first.
    IReadOnlyList<ApprovalOutboxItem> ClaimableOutboxItems(DateTime nowUtc, int limit);

    // Every row, oldest Enqueued On first, up to the limit. For admins and tests.
    IReadOnlyList<ApprovalOutboxItem> OutboxItems(int limit);

    // Writes next when the stored row still has the version next was made from (next.RowVersion - 1). False when
    // another call changed the row first, the row does not exist, or the row is settled: a settled row never changes
    // again, and the store refuses it whatever the caller checked.
    bool TrySetOutboxItem(ApprovalOutboxItem next);
}

// What an outbox change did.
public enum ApprovalOutboxResultKind
{
    // The change was made.
    Done,

    // The same change was made before, so nothing changed.
    Repeated,

    // No outbox row has this ID.
    NotFound,

    // The call's claim is not the row's current claim: another runner call claimed it, or the row moved on.
    StaleClaim,
}

public sealed class ApprovalOutboxResult
{
    public ApprovalOutboxResult(ApprovalOutboxResultKind kind, ApprovalOutboxItem? item)
    {
        Kind = kind;
        Item = item;
    }

    public ApprovalOutboxResultKind Kind { get; }

    // The row after the call, or null when it does not exist.
    public ApprovalOutboxItem? Item { get; }
}

// The outbox's state changes for the approval runner's routes. The host decides which rows to offer (it reads the
// Approval Records); this class only makes the leases and states safe under concurrent runner calls.
public sealed class ApprovalOutbox
{
    private const int Retries = 5;

    private readonly IApprovalOutboxStore _store;

    public ApprovalOutbox(IApprovalOutboxStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public ApprovalOutboxItem? Get(string approvalRecordId)
    {
        return string.IsNullOrEmpty(approvalRecordId) ? null : _store.GetOutboxItem(approvalRecordId);
    }

    public IReadOnlyList<ApprovalOutboxItem> Candidates(DateTime nowUtc, int limit)
    {
        return _store.ClaimableOutboxItems(nowUtc, Math.Max(1, limit));
    }

    // Leases a candidate to a new claim. A row whose earlier lease ran out counts that as one attempt, and a row that has
    // used its attempts becomes failed instead. Null when another call changed the row first or it is not claimable.
    public ApprovalOutboxItem? TryClaim(ApprovalOutboxItem candidate, string claimId, DateTime nowUtc, TimeSpan lease, int maxAttempts)
    {
        if (candidate == null)
        {
            throw new ArgumentNullException(nameof(candidate));
        }

        if (string.IsNullOrEmpty(claimId))
        {
            throw new ArgumentException("A claim has an ID.", nameof(claimId));
        }

        bool lapsed = candidate.State == ApprovalOutboxStates.Claimed;
        if (!(candidate.State == ApprovalOutboxStates.Pending || (lapsed && candidate.ClaimedUntilUtc.HasValue && candidate.ClaimedUntilUtc.Value <= nowUtc)))
        {
            return null;
        }

        int attempts = candidate.Attempts + (lapsed ? 1 : 0);
        if (attempts >= maxAttempts)
        {
            _store.TrySetOutboxItem(candidate.Next(ApprovalOutboxStates.Failed, nowUtc, null, null, attempts, candidate.CardRef, candidate.RunnerRunUrl));
            return null;
        }

        ApprovalOutboxItem next = candidate.Next(ApprovalOutboxStates.Claimed, nowUtc, claimId, nowUtc.Add(lease), attempts, null, null);
        return _store.TrySetOutboxItem(next) ? next : null;
    }

    // The runner sent the card for its claim. The same call again is a no-op; another claim, another card for the same
    // claim, or a row that moved on is a stale claim.
    public ApprovalOutboxResult MarkSent(string approvalRecordId, string claimId, string cardRef, string runnerRunUrl, DateTime nowUtc)
    {
        for (int i = 0; i < Retries; i++)
        {
            ApprovalOutboxItem? item = Get(approvalRecordId);
            if (item == null)
            {
                return new ApprovalOutboxResult(ApprovalOutboxResultKind.NotFound, null);
            }

            if (!string.Equals(item.ClaimId, claimId, StringComparison.Ordinal))
            {
                return new ApprovalOutboxResult(ApprovalOutboxResultKind.StaleClaim, item);
            }

            if (item.State == ApprovalOutboxStates.Sent)
            {
                bool same = string.Equals(item.CardRef, cardRef, StringComparison.Ordinal) && string.Equals(item.RunnerRunUrl, runnerRunUrl, StringComparison.Ordinal);
                return new ApprovalOutboxResult(same ? ApprovalOutboxResultKind.Repeated : ApprovalOutboxResultKind.StaleClaim, item);
            }

            if (item.State != ApprovalOutboxStates.Claimed)
            {
                return new ApprovalOutboxResult(ApprovalOutboxResultKind.StaleClaim, item);
            }

            ApprovalOutboxItem next = item.Next(ApprovalOutboxStates.Sent, nowUtc, item.ClaimId, item.ClaimedUntilUtc, item.Attempts, cardRef, runnerRunUrl);
            if (_store.TrySetOutboxItem(next))
            {
                return new ApprovalOutboxResult(ApprovalOutboxResultKind.Done, next);
            }
        }

        return new ApprovalOutboxResult(ApprovalOutboxResultKind.StaleClaim, Get(approvalRecordId));
    }

    // The runner gives its claim back (it could not send the card, or the card timed out): the row is pending again with
    // one more attempt, or failed once it has used its attempts.
    public ApprovalOutboxResult Release(string approvalRecordId, string claimId, DateTime nowUtc, int maxAttempts)
    {
        for (int i = 0; i < Retries; i++)
        {
            ApprovalOutboxItem? item = Get(approvalRecordId);
            if (item == null)
            {
                return new ApprovalOutboxResult(ApprovalOutboxResultKind.NotFound, null);
            }

            if (!string.Equals(item.ClaimId, claimId, StringComparison.Ordinal) || (item.State != ApprovalOutboxStates.Claimed && item.State != ApprovalOutboxStates.Sent))
            {
                return new ApprovalOutboxResult(ApprovalOutboxResultKind.StaleClaim, item);
            }

            int attempts = item.Attempts + 1;
            string state = attempts >= maxAttempts ? ApprovalOutboxStates.Failed : ApprovalOutboxStates.Pending;
            ApprovalOutboxItem next = item.Next(state, nowUtc, null, null, attempts, item.CardRef, item.RunnerRunUrl);
            if (_store.TrySetOutboxItem(next))
            {
                return new ApprovalOutboxResult(ApprovalOutboxResultKind.Done, next);
            }
        }

        return new ApprovalOutboxResult(ApprovalOutboxResultKind.StaleClaim, Get(approvalRecordId));
    }

    // A platform admin sends a stuck card again (retry_approval_card; document 24, B24-22): a failed row, or a row sent
    // before sentBefore with no answer, goes back to pending with its attempts at zero, so the runner claims it again on
    // its next pull. The card reference and run link stay for history, as a release keeps them. Done when the row moved;
    // NotFound when there is none; StaleClaim when it is in another state, or another call changed it first.
    public ApprovalOutboxResult Retry(string approvalRecordId, DateTime nowUtc, DateTime sentBefore)
    {
        for (int i = 0; i < Retries; i++)
        {
            ApprovalOutboxItem? item = Get(approvalRecordId);
            if (item == null)
            {
                return new ApprovalOutboxResult(ApprovalOutboxResultKind.NotFound, null);
            }

            bool retryable = item.State == ApprovalOutboxStates.Failed || (item.State == ApprovalOutboxStates.Sent && item.UpdatedOnUtc < sentBefore);
            if (!retryable)
            {
                return new ApprovalOutboxResult(ApprovalOutboxResultKind.StaleClaim, item);
            }

            ApprovalOutboxItem next = item.Next(ApprovalOutboxStates.Pending, nowUtc, null, null, 0, item.CardRef, item.RunnerRunUrl);
            if (_store.TrySetOutboxItem(next))
            {
                return new ApprovalOutboxResult(ApprovalOutboxResultKind.Done, next);
            }
        }

        return new ApprovalOutboxResult(ApprovalOutboxResultKind.StaleClaim, Get(approvalRecordId));
    }

    // Settles a row the host found settled: its record expired, or its decision is no longer proposed. A row that is
    // settled already stays as it is. False when the row does not exist.
    public bool Settle(string approvalRecordId, string state, DateTime nowUtc)
    {
        if (!ApprovalOutboxStates.IsSettled(state))
        {
            throw new ArgumentException("A settle moves a row to answered, expired or cancelled.", nameof(state));
        }

        for (int i = 0; i < Retries; i++)
        {
            ApprovalOutboxItem? item = Get(approvalRecordId);
            if (item == null)
            {
                return false;
            }

            if (ApprovalOutboxStates.IsSettled(item.State))
            {
                return true;
            }

            if (_store.TrySetOutboxItem(item.Next(state, nowUtc, item.ClaimId, item.ClaimedUntilUtc, item.Attempts, item.CardRef, item.RunnerRunUrl)))
            {
                return true;
            }
        }

        return false;
    }
}
