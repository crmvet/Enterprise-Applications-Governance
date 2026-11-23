using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Product.Engine.Core.Contract;

namespace Product.Engine.Configuration.Validation;

// Facts OP-09 cannot read from the configuration itself. The runtime fetches them (document 25, section 3.3).
// The core never reads a clock, so the validation date is a fact too.
// Every fact is optional. A rule that needs a missing fact skips that part of its check,
// except V34 at activation, which fails loudly when it cannot prove the rules the approvals came from.
public sealed class ValidationFacts
{
    // Dataverse multiline text holds up to 1,048,576 characters (document 21, section 11.1, [T3]).
    public const int DataverseMultilineTextLimit = 1048576;

    public ValidationFacts(
        DateTime? validatedOn = null,
        int? baseVersionNumber = null,
        int? activeVersionNumber = null,
        bool isRebased = false,
        int? approvalRulesVersionNumber = null,
        IEnumerable<KeyValuePair<string, PrincipalKind>>? principalKinds = null,
        int? snapshotCharacterLimit = null)
    {
        if (snapshotCharacterLimit.HasValue && snapshotCharacterLimit.Value < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(snapshotCharacterLimit), "The limit must be at least 1.");
        }

        ValidatedOn = validatedOn?.Date;
        BaseVersionNumber = baseVersionNumber;
        ActiveVersionNumber = activeVersionNumber;
        IsRebased = isRebased;
        ApprovalRulesVersionNumber = approvalRulesVersionNumber;
        SnapshotCharacterLimit = snapshotCharacterLimit ?? DataverseMultilineTextLimit;

        // Directory object IDs compare without case.
        var kinds = new Dictionary<string, PrincipalKind>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, PrincipalKind> pair in principalKinds ?? new KeyValuePair<string, PrincipalKind>[0])
        {
            kinds[pair.Key] = pair.Value;
        }

        PrincipalKinds = new ReadOnlyDictionary<string, PrincipalKind>(kinds);
    }

    public static ValidationFacts None { get; } = new();

    // The day the set is validated (V28: a workload grant expires at most workload-grant-max-days after it).
    public DateTime? ValidatedOn { get; }

    // The active version the draft started from (V42). Null takes it from the draft patch's manifest.
    public int? BaseVersionNumber { get; }

    // The version in force now (V34, V42).
    public int? ActiveVersionNumber { get; }

    // True when activation re-applied the draft to the active version and is revalidating it (V42).
    public bool IsRebased { get; }

    // The version whose rules created the approval records of the change decision (V34).
    public int? ApprovalRulesVersionNumber { get; }

    // The principal kind of each directory object a binding or a grant names, from Principal Reference rows (V35).
    public IReadOnlyDictionary<string, PrincipalKind> PrincipalKinds { get; }

    // The most characters the binding can store for one snapshot (V41). Defaults to the Dataverse limit.
    public int SnapshotCharacterLimit { get; }
}
