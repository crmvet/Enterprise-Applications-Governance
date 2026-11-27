using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Product.Mcp.Core.Backend.InProcess;

// The host settings that are safe to show a platform admin on the Admin page (document 24, B24-18): where the service
// runs, whether it is the demo, how a person signs in again to answer an approval, when the daily refresh runs, the store
// and its schema version, and the approval outbox. No URL, key, connection string, client ID or tenant ID: those stay in
// the host's configuration. The host builds a fresh value per read (InProcessBackendOptions.HostFacts); without one,
// get_admin_status says the host gave no facts.
public sealed class HostFacts
{
    // The hosting environment's name, such as Production, Development or Demo.
    public string EnvironmentName { get; set; } = string.Empty;

    // True on a Demo install (D-39): the demo persona switch and the demo guide are on.
    public bool IsDemo { get; set; }

    // True when the install says it holds the fictional demo estate (Product:WebApp:Demo, the badge every page shows). With
    // the Demo environment, or a local Development run, it serves the demo's own reads (get_demo_freshness; B24-22).
    public bool HoldsDemoEstate { get; set; }

    // The step-up sign-in mode for an approval answer (D-38): off, fresh-sign-in or authentication-context.
    public string StepUpMode { get; set; } = "off";

    // The hour of the day, in UTC, the daily due-state refresh runs; null when the daily timer is off.
    public int? DueStateRefreshHourUtc { get; set; }

    // The store kind: azure-sql, sqlite or in-memory.
    public string? StoreKind { get; set; }

    // The schema version the store is at, when it has one (Azure SQL); the host does not start below the version it needs.
    public int? SchemaVersion { get; set; }

    // The schema version this build needs.
    public int? RequiredSchemaVersion { get; set; }

    // The approval outbox the runner pulls from, when the host keeps one.
    public OutboxFacts? Outbox { get; set; }
}

// The approval outbox as the Admin page shows it: rows by state, and the rows that need a person's eye (failed, or sent
// long ago and still waiting).
public sealed class OutboxFacts
{
    public OutboxFacts(IReadOnlyDictionary<string, int> counts, IEnumerable<OutboxAttention> attention)
    {
        Counts = counts ?? throw new ArgumentNullException(nameof(counts));
        Attention = new ReadOnlyCollection<OutboxAttention>(new List<OutboxAttention>(attention ?? Array.Empty<OutboxAttention>()));
    }

    public IReadOnlyDictionary<string, int> Counts { get; }

    public IReadOnlyList<OutboxAttention> Attention { get; }
}

public sealed class OutboxAttention
{
    public OutboxAttention(string approvalRecordId, string decisionId, string state, int attempts, DateTime enqueuedOnUtc, DateTime updatedOnUtc)
    {
        ApprovalRecordId = approvalRecordId ?? throw new ArgumentNullException(nameof(approvalRecordId));
        DecisionId = decisionId ?? string.Empty;
        State = state ?? throw new ArgumentNullException(nameof(state));
        Attempts = attempts;
        EnqueuedOnUtc = enqueuedOnUtc;
        UpdatedOnUtc = updatedOnUtc;
    }

    public string ApprovalRecordId { get; }

    public string DecisionId { get; }

    public string State { get; }

    public int Attempts { get; }

    public DateTime EnqueuedOnUtc { get; }

    public DateTime UpdatedOnUtc { get; }
}

// The approval outbox a host keeps (document 24, B24-22): one row by its Approval Record, and the changes a platform admin
// asks for from the Admin page. The engine knows nothing of the outbox (a host table), so the host gives this port; without
// it, retry_approval_card answers that this host keeps no outbox.
public interface IApprovalOutboxPort
{
    // The row of one Approval Record, or null when the outbox has none.
    OutboxAttention? Get(string approvalRecordId);

    // Puts a failed row, or a row sent longer ago than the host's long-sent age, back to pending with its attempts at zero,
    // so the approval runner sends the card again. False when another call changed the row first or it may not be retried.
    bool Retry(string approvalRecordId, DateTime nowUtc);

    // Closes a row whose Approval Record is settled (answered, expired or canceled): the state is answered, expired or
    // cancelled. False when the row does not exist.
    bool Settle(string approvalRecordId, string state, DateTime nowUtc);

    // How long a sent card may wait for its answer before a person may send it again.
    TimeSpan LongSent { get; }
}
