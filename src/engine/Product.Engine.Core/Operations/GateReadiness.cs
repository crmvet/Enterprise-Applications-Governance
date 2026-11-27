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

// What OP-07 reads about the subject's data uses (docs/architecture/data-taxonomy.md, Gate requirements), as the runtime counts them with OP-16.
// A proposal of this build: the counts, never the rows, so the gate stays one small read.
public sealed class DataUseReadinessFact
{
    public DataUseReadinessFact(int declaredCount, bool holdsNoBusinessData, int undecidedCount, int retentionOpenCount)
    {
        if (declaredCount < 0 || undecidedCount < 0 || retentionOpenCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(declaredCount), "Counts are never negative.");
        }

        DeclaredCount = declaredCount;
        HoldsNoBusinessData = holdsNoBusinessData;
        UndecidedCount = undecidedCount;
        RetentionOpenCount = retentionOpenCount;
    }

    // Open data uses (O27 not ended, draft or registered).
    public int DeclaredCount { get; }

    // O02 Holds No Business Data.
    public bool HoldsNoBusinessData { get; }

    // Open registered uses whose OP-16 state is not not-needed or allowed.
    public int UndecidedCount { get; }

    // Open write uses on a system of record without an accepted data-retention decision.
    public int RetentionOpenCount { get; }

    internal static DataUseReadinessFact? FromJson(WireObject? wire)
    {
        if (wire == null)
        {
            return null;
        }

        int Count(string name)
        {
            int value = wire.OptionalInt(name) ?? 0;
            if (value < 0)
            {
                throw OperationStop.Input(ReasonCodes.InputMalformed, wire.Where + "." + name + " must not be negative.");
            }

            return value;
        }

        return new DataUseReadinessFact(Count("declaredCount"), wire.Bool("holdsNoBusinessData", false), Count("undecidedCount"), Count("retentionOpenCount"));
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
        IEnumerable<string>? triggeredGateRequirementKeys = null,
        DataUseReadinessFact? dataUses = null)
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
        DataUses = dataUses;
    }

    // The subject's data uses, for the requirement kinds data-uses-declared and data-uses-decided (fetched). Null when not
    // fetched; needed only when such a requirement applies and data-taxonomy-mode is not off.
    public DataUseReadinessFact? DataUses { get; }

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
            "triggeredGateRequirementKeys",
            "dataUses");

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
            wire.Keys("triggeredGateRequirementKeys"),
            DataUseReadinessFact.FromJson(wire.OptionalObject("dataUses", "declaredCount", "holdsNoBusinessData", "undecidedCount", "retentionOpenCount")));
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
    public ReadinessItem(string requirementKey, RequirementKind requirementKind, string? requiredKey, bool met, string? reasonCode, string? guidelineKey, bool triggered)
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

    // The decision type, document type, review type or owner role the kind asks for. Null for data-uses-declared.
    public string? RequiredKey { get; }

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
            .Add(RequiredField(RequirementKind) ?? "requiredKey", RequiredKey)
            .Add("met", Met)
            .Add("reasonCode", ReasonCode)
            .Add("guidelineKey", GuidelineKey)
            .Add("triggered", Triggered)
            .Build();
    }

    // The C14 column that names the target of each requirement kind. Null for data-uses-declared, which has none.
    internal static string? RequiredField(RequirementKind kind)
    {
        switch (kind)
        {
            case RequirementKind.DecisionAccepted:
            case RequirementKind.DataUsesDecided:
                return "requiredDecisionTypeKey";
            case RequirementKind.DataUsesDeclared:
                return null;
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
            string? requiredKey = RequiredKeyOf(row, kind);
            bool met = IsMet(snapshot, row, kind, requiredKey, request);
            string? code = met ? null : UnmetCode(kind);
            items.Add(new ReadinessItem(row.Key, kind, requiredKey, met, code, SnapshotRead.Text(row, "guidelineKey"), !byScope));
            trace.Add(new RuleTrace("Gate Requirement", row.Key, RuleTraceRole.Required));
            if (code != null)
            {
                ReasonCodeInfo info = ReasonCatalog.Get(code);
                reasons.Add(new Reason(code, info.DefaultSeverity, UnmetMessage(snapshot, row, kind, requiredKey), "Gate Requirement", row.Key));
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

    // V10: a gate requirement sets the one target its kind needs, and no other. data-uses-declared sets none.
    private static string? RequiredKeyOf(SnapshotRow row, RequirementKind kind)
    {
        string? field = ReadinessItem.RequiredField(kind);
        string? key = field == null ? null : SnapshotRead.Text(row, field);
        string[] others = new[] { "requiredDecisionTypeKey", "requiredDocumentTypeKey", "requiredReviewTypeKey", "requiredOwnerRoleKey" }
            .Where(other => other != field && SnapshotRead.HasValue(row, other))
            .ToArray();
        if ((field != null && key == null) || others.Length > 0)
        {
            throw OperationStop.Broken(
                ReasonCodes.RefExactlyOne,
                "Gate Requirement " + SnapshotRead.Quote(row.Key) + " of kind " + EngineContract.RequirementKinds.ToWire(kind) + (field == null ? " sets no target" : " must set " + field + " and no other target") + ", so the configuration is broken.",
                "Gate Requirement",
                row.Key);
        }

        return key;
    }

    // D-41, question 3 (option A), as built after the data design review: with data domains on, "a data owner is named"
    // is answered by the data the application declares. An application with a declared use, or marked as holding no
    // business data, meets an owner-assigned data-owner requirement through the derived owners of its domains, so the
    // app-level data owner is only the route for an application that declares nothing (D-41).
    // Under domains-required the app-level data owner does not count at all, and propose_owner_change refuses a new one.
    private static bool DerivedDataOwner(ConfigurationSnapshot snapshot, string requiredKey, ReadinessRequest request)
    {
        if (!string.Equals(requiredKey, EngineKeys.DataOwnerRole, StringComparison.Ordinal) || request.DataUses == null)
        {
            return false;
        }

        DataTaxonomyMode mode = new DataTree(snapshot).Mode;
        return mode != DataTaxonomyMode.Off && (request.DataUses.DeclaredCount > 0 || request.DataUses.HoldsNoBusinessData);
    }

    private static bool IsMet(ConfigurationSnapshot snapshot, SnapshotRow row, RequirementKind kind, string? requiredKey, ReadinessRequest request)
    {
        if (kind == RequirementKind.DataUsesDeclared || kind == RequirementKind.DataUsesDecided)
        {
            return DataUsesMet(snapshot, row, kind, requiredKey, request);
        }

        switch (kind)
        {
            case RequirementKind.DecisionAccepted:
                return request.AcceptedDecisionTypeKeys.Contains(requiredKey!, StringComparer.Ordinal);
            case RequirementKind.ReviewDone:
                return request.DoneReviewTypeKeys.Contains(requiredKey!, StringComparer.Ordinal);
            case RequirementKind.OwnerAssigned:
                if (string.Equals(requiredKey, EngineKeys.DataOwnerRole, StringComparison.Ordinal) && new DataTree(snapshot).Mode == DataTaxonomyMode.DomainsRequired)
                {
                    return DerivedDataOwner(snapshot, requiredKey!, request);
                }

                return request.AssignedOwnerRoleKeys.Contains(requiredKey!, StringComparer.Ordinal) || DerivedDataOwner(snapshot, requiredKey!, request);
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

    // The data taxonomy's two requirement kinds (docs/architecture/data-taxonomy.md, Gate requirements), judged by
    // data-taxonomy-mode (a choice under D-41): with the mode off they are met, because the install keeps the behavior before data domains. With
    // domains-optional, an application-level data owner also meets data-uses-declared, as the fallback route of D-41,
    // question 3. With domains-required only a declared use or Holds No Business Data does. data-uses-decided is met when
    // no open registered use is anything but not needed or allowed (data-use), or when every write use on a system of
    // record has an accepted retention decision (data-retention).
    private static bool DataUsesMet(ConfigurationSnapshot snapshot, SnapshotRow row, RequirementKind kind, string? requiredKey, ReadinessRequest request)
    {
        DataTaxonomyMode mode = new DataTree(snapshot).Mode;
        if (mode == DataTaxonomyMode.Off)
        {
            return true;
        }

        DataUseReadinessFact facts = request.DataUses
            ?? throw OperationStop.Input(
                ReasonCodes.InputRequired,
                "input.dataUses (the counts of the subject's data uses) is required, because Gate Requirement " + SnapshotRead.Quote(row.Key) + " needs data uses " + (kind == RequirementKind.DataUsesDeclared ? "declared" : "decided") + ".",
                row.Key);
        if (kind == RequirementKind.DataUsesDeclared)
        {
            return facts.DeclaredCount > 0 || facts.HoldsNoBusinessData
                || (mode == DataTaxonomyMode.DomainsOptional && request.AssignedOwnerRoleKeys.Contains(EngineKeys.DataOwnerRole, StringComparer.Ordinal));
        }

        return string.Equals(requiredKey, EngineKeys.DataRetentionDecisionType, StringComparison.Ordinal) ? facts.RetentionOpenCount == 0 : facts.UndecidedCount == 0;
    }

    private static string UnmetCode(RequirementKind kind)
    {
        switch (kind)
        {
            case RequirementKind.DataUsesDeclared:
                return ReasonCodes.GateRequirementDataUsesUndeclared;
            case RequirementKind.DataUsesDecided:
                return ReasonCodes.GateRequirementDataUsesUndecided;
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

    // The unmet requirement in the words people read: the requirement's label and the label of what it needs, never a
    // key (product review: the design decision page quoted design-data-uses-decided-tier-2).
    private static string UnmetMessage(ConfigurationSnapshot snapshot, SnapshotRow row, RequirementKind kind, string? requiredKey)
    {
        string name = "The requirement \"" + (SnapshotRead.Text(row, "label") ?? row.Key) + "\"";
        string Label(string entity) => requiredKey == null ? "?" : (SnapshotRead.Find(snapshot, entity, requiredKey) is SnapshotRow target ? SnapshotRead.Text(target, "label") : null)?.ToLowerInvariant() ?? requiredKey.Replace('-', ' ');
        string what;
        switch (kind)
        {
            case RequirementKind.DataUsesDeclared:
                return name + " needs the application's data uses declared, or the application marked as holding no business data, and it has neither.";
            case RequirementKind.DataUsesDecided:
                return name + " needs every " + (requiredKey == EngineKeys.DataRetentionDecisionType ? "write use on a system of record to have an accepted retention decision" : "open data use to be decided (not needed or allowed)") + ", and at least one is not.";
            case RequirementKind.DecisionAccepted:
                what = "an accepted " + Label("decision-type") + " decision";
                break;
            case RequirementKind.DocumentCurrent:
                what = "a current " + Label("document-type");
                break;
            case RequirementKind.ReviewDone:
                what = "a done " + Label("review-type");
                break;
            default:
                what = "a holder of the " + Label("owner-role") + " role";
                break;
        }

        return name + " needs " + what + ", and the subject has none.";
    }
}
