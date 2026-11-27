using System;
using Product.Azure.Stores;
using Product.Mcp.Core.Backend.InProcess;

namespace Product.Azure.Host;

/// <summary>
/// The approval outbox as the MCP core sees it for the platform admin's retry (<c>retry_approval_card</c>; document 24,
/// B24-22): one row by its Approval Record, a retry of a failed or long-sent row, and the close of a row whose request is
/// settled. The outbox is a host table the engine knows nothing of, so the host gives this port to the backend. Every change
/// is the outbox's own compare and set, so a retry never races the approval runner.
/// </summary>
public sealed class ApprovalOutboxPort : IApprovalOutboxPort
{
    private readonly ApprovalOutbox _outbox;

    public ApprovalOutboxPort(ApprovalOutbox outbox)
    {
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
    }

    /// <summary>A sent card older than this with no answer may be sent again, as the Admin page lists it.</summary>
    public TimeSpan LongSent => HostFactsReader.LongSent;

    public OutboxAttention? Get(string approvalRecordId)
    {
        ApprovalOutboxItem? item = _outbox.Get(approvalRecordId);
        return item == null ? null : new OutboxAttention(item.ApprovalRecordId, item.DecisionId, item.State, item.Attempts, item.EnqueuedOnUtc, item.UpdatedOnUtc);
    }

    public bool Retry(string approvalRecordId, DateTime nowUtc)
    {
        return _outbox.Retry(approvalRecordId, nowUtc, nowUtc - LongSent).Kind == ApprovalOutboxResultKind.Done;
    }

    public bool Settle(string approvalRecordId, string state, DateTime nowUtc)
    {
        return _outbox.Settle(approvalRecordId, state, nowUtc);
    }
}
