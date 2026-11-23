using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;
using Product.Engine.Core.Snapshots;

namespace Product.Engine.Core.Operations;

// One document status as OP-06 returns it: the part of an OP-06 item that OP-07 reads.
public sealed class DocumentStatusFact
{
    public DocumentStatusFact(string documentTypeKey, DocumentStatusMeaning status, string? requirementKey = null)
    {
        if (!ConfigurationKey.IsValid(documentTypeKey))
        {
            throw new ArgumentException("A document type key must use the configuration key format.", nameof(documentTypeKey));
        }

        if (!Enum.IsDefined(typeof(DocumentStatusMeaning), status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        DocumentTypeKey = documentTypeKey;
        Status = status;
        RequirementKey = requirementKey;
    }

    public string DocumentTypeKey { get; }

    // The effective status OP-06 computed: missing, draft, current or stale.
    public DocumentStatusMeaning Status { get; }

    public string? RequirementKey { get; }

    public static IReadOnlyList<DocumentStatusFact> FromResult(DocumentationResult result)
    {
        if (result == null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        return result.Items.Select(item => new DocumentStatusFact(item.DocumentTypeKey, item.Status, item.RequirementKey)).ToList();
    }
}

// OP-07 input (document 25, section 5.9).
public sealed class ReadinessRequest
{
    public ReadinessRequest(
        string gateKey,
        string? tierKey,
        string applicationKindKey,
        IEnumerable<string>? acceptedDecisionTypeKeys = null,
        IEnumerable<DocumentStatusFact>? documentation = null,
        IEnumerable<string>? doneReviewTypeKeys = null,
        IEnumerable<string>? assignedOwnerRoleKeys = null,
        IEnumerable<string>? triggeredGateRequirementKeys = null)
    {
        if (!ConfigurationKey.IsValid(gateKey))
        {
            throw new ArgumentException("A gate key must use the configuration key format.", nameof(gateKey));
        }

        if (tierKey != null && !ConfigurationKey.IsValid(tierKey))
        {
            throw new ArgumentException("A tier key must use the configuration key format.", nameof(tierKey));
        }

        if (!ConfigurationKey.IsValid(applicationKindKey))
        {
            throw new ArgumentException("An application kind key must use the configuration key format.", nameof(applicationKindKey));
        }

        GateKey = gateKey;
        TierKey = tierKey;
        ApplicationKindKey = applicationKindKey;
        AcceptedDecisionTypeKeys = Keys(acceptedDecisionTypeKeys, nameof(acceptedDecisionTypeKeys));
        Documentation = documentation == null ? null : new ReadOnlyCollection<DocumentStatusFact>(documentation.ToList());
        DoneReviewTypeKeys = Keys(doneReviewTypeKeys, nameof(doneReviewTypeKeys));
        AssignedOwnerRoleKeys = Keys(assignedOwnerRoleKeys, nameof(assignedOwnerRoleKeys));
        TriggeredGateRequirementKeys = Keys(triggeredGateRequirementKeys, nameof(triggeredGateRequirementKeys));
    }

    // The lifecycle gate (C13).
    public string GateKey { get; }

    // Scope for C14 rows. Null while the subject has no tier: then only rows without a tier apply (a proposal of this
    // build; at the first gate an intake often has no tier yet).
    public string? TierKey { get; }

    // Scope for C14 rows.
    public string ApplicationKindKey { get; }

    // Decision types with an accepted decision for the subject (fetched).
    public IReadOnlyList<string> AcceptedDecisionTypeKeys { get; }

    // Document statuses (the OP-06 result, fetched or computed). Null when not fetched; needed only when a
    // document-current requirement applies.
    public IReadOnlyList<DocumentStatusFact>? Documentation { get; }

    // Review types done for the subject (fetched).
    public IReadOnlyList<string> DoneReviewTypeKeys { get; }

    // Owner roles with an active assignment for the subject (fetched). A proposal of this build: document 21 adds the
    // requirement kind owner-assigned (C14 Required Owner Role) after document 25 listed OP-07's input.
    public IReadOnlyList<string> AssignedOwnerRoleKeys { get; }

    // Gate requirements the subject's tier answers add (C10 Triggers Json, the OP-01 result). A proposal of this build:
    // a triggered row of this gate applies even when its tier or kind scope does not match, because the answer asks for it.
    public IReadOnlyList<string> TriggeredGateRequirementKeys { get; }

    public static ReadinessRequest FromJson(JsonTreeNode input)
    {
        var wire = new WireObject(
            input,
            "input",
            "gateKey",
            "tierKey",
            "applicationKindKey",
            "acceptedDecisionTypeKeys",
            "documentation",
            "doneReviewTypeKeys",
            "assignedOwnerRoleKeys",
            "triggeredGateRequirementKeys");

        List<DocumentStatusFact>? documentation = null;
        WireObject? result = wire.OptionalObject("documentation", "complete", "items", "missingCount", "draftCount", "staleCount", "staleOnByLink");
        if (result != null)
        {
            // The whole OP-06 payload may be passed as it came. Only the items decide; the other fields are checked
            // for their shape so a typo still fails.
            result.OptionalBool("complete");
            result.OptionalInt("missingCount");
            result.OptionalInt("draftCount");
            result.OptionalInt("staleCount");
            documentation = result
                .Objects("items", required: true, "requirementKey", "documentTypeKey", "status", "documentLinkId", "staleOnUtc")
                .Select(item =>
                {
                    item.OptionalString("documentLinkId");
                    item.OptionalUtc("staleOnUtc");
                    return new DocumentStatusFact(item.RequiredKey("documentTypeKey"), item.Contract("status", EngineContract.DocumentStatusMeanings), item.OptionalKey("requirementKey"));
                })
                .ToList();
        }

        return new ReadinessRequest(
            wire.RequiredKey("gateKey"),
            wire.OptionalKey("tierKey"),
            wire.RequiredKey("applicationKindKey"),
            wire.Keys("acceptedDecisionTypeKeys"),
            documentation,
            wire.Keys("doneReviewTypeKeys"),
            wire.Keys("assignedOwnerRoleKeys"),
            wire.Keys("triggeredGateRequirementKeys"));
    }

    private static IReadOnlyList<string> Keys(IEnumerable<string>? keys, string name)
    {
        string[] list = (keys ?? Enumerable.Empty<string>()).Distinct(StringComparer.Ordinal).ToArray();
        if (list.Any(key => !ConfigurationKey.IsValid(key)))
        {
            throw new ArgumentException("Every key must use the configuration key format.", name);
        }

        return new ReadOnlyCollection<string>(list);
    }
}

// One applicable Gate Requirement (C14) and whether it is met.
public sealed class ReadinessItem
{
    public ReadinessItem(string requirementKey, RequirementKind requirementKind, string requiredKey, bool met, string? reasonCode, string? guidelineKey, bool triggered)
    {
        RequirementKey = requirementKey;
        RequirementKind = requirementKind;
        RequiredKey = requiredKey;
        Met = met;
        ReasonCode = reasonCode;
        GuidelineKey = guidelineKey;
        Triggered = triggered;
    }

    public string RequirementKey { get; }

    public RequirementKind RequirementKind { get; }

    // The decision type, document type, review type or owner role the kind asks for.
    public string RequiredKey { get; }

    public bool Met { get; }

    // Why it is not met. Null when met.
    public string? ReasonCode { get; }

    public string? GuidelineKey { get; }

    // True when the row applies only because a tier answer triggered it.
    public bool Triggered { get; }

    internal JsonTreeNode ToJson()
    {
        return new WireWriter()
            .Add("requirementKey", RequirementKey)
            .Add("requirementKind", EngineContract.RequirementKinds.ToWire(RequirementKind))
            .Add(RequiredField(RequirementKind), RequiredKey)
            .Add("met", Met)
            .Add("reasonCode", ReasonCode)
            .Add("guidelineKey", GuidelineKey)
            .Add("triggered", Triggered)
            .Build();
    }

    // The C14 column that names the target of each requirement kind.
    internal static string RequiredField(RequirementKind kind)
    {
        switch (kind)
        {
            case RequirementKind.DecisionAccepted:
                return "requiredDecisionTypeKey";
            case RequirementKind.DocumentCurrent:
                return "requiredDocumentTypeKey";
            case RequirementKind.ReviewDone:
                return "requiredReviewTypeKey";
            default:
                return "requiredOwnerRoleKey";
        }
    }
}

// OP-07 output (document 25, section 5.9).
public sealed class ReadinessResult : IOperationPayload
{
    public ReadinessResult(string gateKey, IEnumerable<ReadinessItem> requirements)
    {
        GateKey = gateKey;
        Requirements = new ReadOnlyCollection<ReadinessItem>(requirements.ToList());
    }

    public string GateKey { get; }

    // True when every applicable requirement is met. True also when none applies.
    public bool Ready => Requirements.All(item => item.Met);

    // One per applicable C14 row, by requirement key.
    public IReadOnlyList<ReadinessItem> Requirements { get; }

    public JsonTreeNode ToJson()
    {
        return new WireWriter()
            .Add("gateKey", GateKey)
            .Add("ready", Ready)
            .Add("requirements", WireWriter.Array(Requirements.Select(item => item.ToJson())))
            .Build();
    }
}

// OP-07 Evaluate gate readiness (document 25, section 5.9; document 21, C14 and section 13.4).
// Applicable requirements are the active C14 rows of the gate whose optional tier and kind match, plus the rows of the
// gate that the subject's tier answers trigger. A decision-accepted requirement is met by an accepted decision of the
// required type; document-current by an effective status of current (OP-06); review-done by a done review of the
// required type; owner-assigned by an active assignment of the required owner role. A gate decision whose gate is not
// ready cannot be accepted: OP-08 refuses it with gate.not-ready (G-14).
//
// WORKING DEFAULT (open design question 1): the four requirement kinds are fixed engine meanings (document 20,
// section 2.4, with owner-assigned from document 21). WORKING DEFAULT (open design question 9): a document-current
// requirement trusts the OP-06 status, whose "current" for a gate-relevant type already needed a person's card.
public static class GateReadinessOperation
{
    private const string Requirement = "gate-requirement";

    public static EvaluationResult<ReadinessResult?> Evaluate(ConfigurationSnapshot snapshot, EvaluationContext context, ReadinessRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return OperationShell.Run(context, snapshot, needsSnapshot: true, () => Answer(snapshot, request));
    }

    public static EvaluationResult<ReadinessResult?> Evaluate(ConfigurationSnapshot snapshot, EvaluationContext context, JsonTreeNode input)
    {
        return OperationShell.Run(context, snapshot, needsSnapshot: true, () => Answer(snapshot, ReadinessRequest.FromJson(input)));
    }

    private static OperationAnswer<ReadinessResult> Answer(ConfigurationSnapshot snapshot, ReadinessRequest request)
    {
        SnapshotRead.RequireExisting(snapshot, "gate", request.GateKey, "input.gateKey");
        if (request.TierKey != null)
        {
            SnapshotRead.RequireExisting(snapshot, "tier", request.TierKey, "input.tierKey");
        }

        SnapshotRead.RequireExisting(snapshot, "application-kind", request.ApplicationKindKey, "input.applicationKindKey");
        var triggered = new HashSet<string>(StringComparer.Ordinal);
        foreach (string key in request.TriggeredGateRequirementKeys)
        {
            SnapshotRead.RequireExisting(snapshot, Requirement, key, "input.triggeredGateRequirementKeys");
            triggered.Add(key);
        }

        var items = new List<ReadinessItem>();
        var reasons = new List<Reason>();
        var trace = new List<RuleTrace> { new RuleTrace("Gate", request.GateKey, RuleTraceRole.Matched) };
        IEnumerable<SnapshotRow> ofGate = SnapshotRead.ActiveRows(snapshot, Requirement)
            .Where(row => string.Equals(SnapshotRead.Text(row, "gateKey"), request.GateKey, StringComparison.Ordinal))
            .OrderBy(row => row.Key, StringComparer.Ordinal);
        foreach (SnapshotRow row in ofGate)
        {
            bool byScope = Matches(row, "tierKey", request.TierKey) && Matches(row, "applicationKindKey", request.ApplicationKindKey);
            if (!byScope && !triggered.Contains(row.Key))
            {
                continue;
            }

            RequirementKind kind = KindOf(row);
            string requiredKey = RequiredKeyOf(row, kind);
            bool met = IsMet(row, kind, requiredKey, request);
            string? code = met ? null : UnmetCode(kind);
            items.Add(new ReadinessItem(row.Key, kind, requiredKey, met, code, SnapshotRead.Text(row, "guidelineKey"), !byScope));
            trace.Add(new RuleTrace("Gate Requirement", row.Key, RuleTraceRole.Required));
            if (code != null)
            {
                ReasonCodeInfo info = ReasonCatalog.Get(code);
                reasons.Add(new Reason(code, info.DefaultSeverity, UnmetMessage(row.Key, kind, requiredKey), "Gate Requirement", row.Key));
            }
        }

        var result = new ReadinessResult(request.GateKey, items);
        if (!result.Ready)
        {
            reasons.Add(ReasonCatalog.Create(ReasonCodes.GateNotReady, "Gate", request.GateKey));
        }

        return new OperationAnswer<ReadinessResult>(result.Ready ? "ready" : "not-ready", result, reasons, trace);
    }

    private static bool Matches(SnapshotRow row, string column, string? value)
    {
        string? scope = SnapshotRead.Text(row, column);
        return scope == null || string.Equals(scope, value, StringComparison.Ordinal);
    }

    private static RequirementKind KindOf(SnapshotRow row)
    {
        if (!EngineContract.RequirementKinds.TryParse(SnapshotRead.Text(row, "requirementKind"), out RequirementKind kind))
        {
            throw OperationStop.Broken(
                ReasonCodes.ShapeSchema,
                "Gate Requirement " + SnapshotRead.Quote(row.Key) + " has no known requirement kind, so the configuration is broken.",
                "Gate Requirement",
                row.Key);
        }

        return kind;
    }

    // V10: a gate requirement sets the one target its kind needs, and no other.
    private static string RequiredKeyOf(SnapshotRow row, RequirementKind kind)
    {
        string field = ReadinessItem.RequiredField(kind);
        string? key = SnapshotRead.Text(row, field);
        string[] others = new[] { RequirementKind.DecisionAccepted, RequirementKind.DocumentCurrent, RequirementKind.ReviewDone, RequirementKind.OwnerAssigned }
            .Where(other => other != kind && SnapshotRead.HasValue(row, ReadinessItem.RequiredField(other)))
            .Select(ReadinessItem.RequiredField)
            .ToArray();
        if (key == null || others.Length > 0)
        {
            throw OperationStop.Broken(
                ReasonCodes.RefExactlyOne,
                "Gate Requirement " + SnapshotRead.Quote(row.Key) + " of kind " + EngineContract.RequirementKinds.ToWire(kind) + " must set " + field + " and no other target, so the configuration is broken.",
                "Gate Requirement",
                row.Key);
        }

        return key;
    }

    private static bool IsMet(SnapshotRow row, RequirementKind kind, string requiredKey, ReadinessRequest request)
    {
        switch (kind)
        {
            case RequirementKind.DecisionAccepted:
                return request.AcceptedDecisionTypeKeys.Contains(requiredKey, StringComparer.Ordinal);
            case RequirementKind.ReviewDone:
                return request.DoneReviewTypeKeys.Contains(requiredKey, StringComparer.Ordinal);
            case RequirementKind.OwnerAssigned:
                return request.AssignedOwnerRoleKeys.Contains(requiredKey, StringComparer.Ordinal);
            default:
                // The document statuses must have been fetched or computed (OP-06): without them the engine cannot
                // tell, and it never guesses.
                if (request.Documentation == null)
                {
                    throw OperationStop.Input(
                        ReasonCodes.InputRequired,
                        "input.documentation (the OP-06 result) is required, because Gate Requirement " + SnapshotRead.Quote(row.Key) + " needs a current document.",
                        row.Key);
                }

                return request.Documentation.Any(document => string.Equals(document.DocumentTypeKey, requiredKey, StringComparison.Ordinal) && document.Status == DocumentStatusMeaning.Current);
        }
    }

    private static string UnmetCode(RequirementKind kind)
    {
        switch (kind)
        {
            case RequirementKind.DecisionAccepted:
                return ReasonCodes.GateRequirementDecisionMissing;
            case RequirementKind.DocumentCurrent:
                return ReasonCodes.GateRequirementDocumentNotCurrent;
            case RequirementKind.ReviewDone:
                return ReasonCodes.GateRequirementReviewNotDone;
            default:
                return ReasonCodes.GateRequirementOwnerMissing;
        }
    }

    private static string UnmetMessage(string requirementKey, RequirementKind kind, string requiredKey)
    {
        string what;
        switch (kind)
        {
            case RequirementKind.DecisionAccepted:
                what = "an accepted decision of type " + SnapshotRead.Quote(requiredKey);
                break;
            case RequirementKind.DocumentCurrent:
                what = "a current document of type " + SnapshotRead.Quote(requiredKey);
                break;
            case RequirementKind.ReviewDone:
                what = "a done review of type " + SnapshotRead.Quote(requiredKey);
                break;
            default:
                what = "an active assignment of owner role " + SnapshotRead.Quote(requiredKey);
                break;
        }

        return "Gate Requirement " + SnapshotRead.Quote(requirementKey) + " needs " + what + ", and the subject has none.";
    }
}
