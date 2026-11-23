using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;
using Product.Engine.Core.Snapshots;

namespace Product.Engine.Core.Operations;

// One item the inventory returned (document 25, section 5.14, InventoryItem). Only the fields a claim stores
// (document 22, section 9.1): the engine keeps no copy of the inventory, so the wire reader refuses any other field.
public sealed class InventoryItem
{
    public InventoryItem(
        string externalId,
        string sourceType,
        string? componentKindKey,
        string estateKey,
        string labelAsSeen,
        string? targetAsSeen = null,
        string? suggestedClaimantObjectId = null)
    {
        if (string.IsNullOrEmpty(externalId) || string.IsNullOrEmpty(sourceType) || string.IsNullOrEmpty(labelAsSeen))
        {
            throw new ArgumentException("An inventory item has an External Id, a source type and a label as seen.");
        }

        if ((componentKindKey != null && !ConfigurationKey.IsValid(componentKindKey)) || !ConfigurationKey.IsValid(estateKey))
        {
            throw new ArgumentException("Component kind and estate keys must use the configuration key format.");
        }

        ExternalId = externalId;
        SourceType = sourceType;
        ComponentKindKey = componentKindKey;
        EstateKey = estateKey;
        LabelAsSeen = labelAsSeen;
        TargetAsSeen = string.IsNullOrEmpty(targetAsSeen) ? null : targetAsSeen;
        SuggestedClaimantObjectId = string.IsNullOrEmpty(suggestedClaimantObjectId) ? null : suggestedClaimantObjectId;
    }

    // The item's Microsoft ID, such as the inventory resource name.
    public string ExternalId { get; }

    // The tool's own type value, such as the inventory type.
    public string SourceType { get; }

    // The component kind (C16). When absent, the engine matches the source type to a C16 Source Type Hint.
    public string? ComponentKindKey { get; }

    // The estate (C07 estate) the item was read from.
    public string EstateKey { get; }

    public string LabelAsSeen { get; }

    public string? TargetAsSeen { get; }

    // The person Microsoft listed as owner at check time. A hint only (fit correction 8).
    public string? SuggestedClaimantObjectId { get; }

    internal static InventoryItem FromJson(WireObject wire)
    {
        return new InventoryItem(
            wire.RequiredString("externalId"),
            wire.RequiredString("sourceType"),
            wire.OptionalKey("componentKindKey"),
            wire.RequiredKey("estateKey"),
            wire.RequiredString("labelAsSeen"),
            wire.OptionalString("targetAsSeen"),
            wire.OptionalString("suggestedClaimantObjectId"));
    }
}

// A registered component in a covered estate and kind (O03), fetched by the adapter.
public sealed class RegisteredComponentFact
{
    public RegisteredComponentFact(string technicalComponentId, string? externalId, string componentKindKey, string estateKey, bool isRetired = false)
    {
        if (string.IsNullOrEmpty(technicalComponentId))
        {
            throw new ArgumentException("A component fact names the component.", nameof(technicalComponentId));
        }

        if (!ConfigurationKey.IsValid(componentKindKey) || !ConfigurationKey.IsValid(estateKey))
        {
            throw new ArgumentException("Component kind and estate keys must use the configuration key format.");
        }

        TechnicalComponentId = technicalComponentId;
        ExternalId = string.IsNullOrEmpty(externalId) ? null : externalId;
        ComponentKindKey = componentKindKey;
        EstateKey = estateKey;
        IsRetired = isRetired;
    }

    public string TechnicalComponentId { get; }

    // Null for a component held by URL only (O03): the gap check can never see it, so it never vanishes.
    public string? ExternalId { get; }

    public string ComponentKindKey { get; }

    public string EstateKey { get; }

    // True when a person confirmed the item is gone (O03 Retired On).
    public bool IsRetired { get; }

    internal static RegisteredComponentFact FromJson(WireObject wire)
    {
        return new RegisteredComponentFact(
            wire.RequiredString("technicalComponentId"),
            wire.OptionalString("externalId"),
            wire.RequiredKey("componentKindKey"),
            wire.RequiredKey("estateKey"),
            wire.Bool("isRetired", false));
    }
}

// An existing claim (O23) for the keys in play, fetched by the adapter.
public sealed class ExistingClaimFact
{
    public ExistingClaimFact(string claimId, string claimKey, ClaimStatusMeaning statusMeaning, FindingKind findingKind, int timesSeen, string? lastSeenRunId = null)
    {
        if (string.IsNullOrEmpty(claimId) || string.IsNullOrEmpty(claimKey))
        {
            throw new ArgumentException("A claim fact names the claim and its key.");
        }

        if (timesSeen < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(timesSeen), "A claim exists because a run saw it, so Times Seen is at least 1.");
        }

        ClaimId = claimId;
        ClaimKey = claimKey;
        StatusMeaning = statusMeaning;
        FindingKind = findingKind;
        TimesSeen = timesSeen;
        LastSeenRunId = string.IsNullOrEmpty(lastSeenRunId) ? null : lastSeenRunId;
    }

    public string ClaimId { get; }

    public string ClaimKey { get; }

    public ClaimStatusMeaning StatusMeaning { get; }

    public FindingKind FindingKind { get; }

    // O23 Times Seen. A proposal of this build: the plan writes absolute values, so a replayed write changes nothing.
    public int TimesSeen { get; }

    // O23 Last Seen Run. A proposal of this build: a run raises Times Seen once, however many pages see the item.
    public string? LastSeenRunId { get; }

    internal static ExistingClaimFact FromJson(WireObject wire)
    {
        int timesSeen = wire.OptionalInt("timesSeen") ?? throw OperationStop.Input(ReasonCodes.InputRequired, wire.Where + ".timesSeen is required.");
        if (timesSeen < 1)
        {
            throw OperationStop.Input(ReasonCodes.InputMalformed, wire.Where + ".timesSeen must be at least 1.");
        }

        return new ExistingClaimFact(
            wire.RequiredString("claimId"),
            wire.RequiredString("claimKey"),
            wire.Contract("statusMeaning", EngineContract.ClaimStatusMeanings),
            wire.Contract("findingKind", EngineContract.FindingKinds),
            timesSeen,
            wire.OptionalString("lastSeenRunId"));
    }
}

// OP-13 input (document 25, section 5.14).
public sealed class GapCompareRequest
{
    public GapCompareRequest(
        string gapCheckRunId,
        IEnumerable<string> coveredEstateKeys,
        IEnumerable<string>? coveredComponentKindKeys,
        bool isFinalPage,
        IEnumerable<InventoryItem> items,
        IEnumerable<RegisteredComponentFact>? components = null,
        IEnumerable<ExistingClaimFact>? claims = null,
        IEnumerable<string>? seenThisRun = null,
        bool isLiveQuery = false)
    {
        if (string.IsNullOrEmpty(gapCheckRunId))
        {
            throw new ArgumentException("A comparison belongs to a gap check run (O22).", nameof(gapCheckRunId));
        }

        string[] estates = (coveredEstateKeys ?? throw new ArgumentNullException(nameof(coveredEstateKeys))).Distinct(StringComparer.Ordinal).ToArray();
        if (estates.Length == 0 || estates.Any(key => !ConfigurationKey.IsValid(key)))
        {
            throw new ArgumentException("A run covers at least one estate, by key.", nameof(coveredEstateKeys));
        }

        string[]? kinds = coveredComponentKindKeys?.Distinct(StringComparer.Ordinal).ToArray();
        if (kinds != null && kinds.Any(key => !ConfigurationKey.IsValid(key)))
        {
            throw new ArgumentException("Component kind keys must use the configuration key format.", nameof(coveredComponentKindKeys));
        }

        ExistingClaimFact[] claimList = (claims ?? Enumerable.Empty<ExistingClaimFact>()).ToArray();
        if (claimList.Select(c => c.ClaimKey).Distinct(StringComparer.Ordinal).Count() != claimList.Length)
        {
            throw new ArgumentException("Claim Key is unique (O23), so no two claim facts share one.", nameof(claims));
        }

        GapCheckRunId = gapCheckRunId;
        CoveredEstateKeys = new ReadOnlyCollection<string>(estates);
        CoveredComponentKindKeys = kinds == null ? null : new ReadOnlyCollection<string>(kinds);
        IsFinalPage = isFinalPage;
        Items = new ReadOnlyCollection<InventoryItem>((items ?? throw new ArgumentNullException(nameof(items))).ToList());
        Components = new ReadOnlyCollection<RegisteredComponentFact>((components ?? Enumerable.Empty<RegisteredComponentFact>()).ToList());
        Claims = new ReadOnlyCollection<ExistingClaimFact>(claimList);
        SeenThisRun = new ReadOnlyCollection<string>((seenThisRun ?? Enumerable.Empty<string>()).Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).ToList());
        IsLiveQuery = isLiveQuery;
    }

    // The run (O22).
    public string GapCheckRunId { get; }

    // The estates the run covers (C07 estate).
    public IReadOnlyList<string> CoveredEstateKeys { get; }

    // The component kinds the run covers. Null means every active kind with Is Gap Check Covered yes (C16). When given,
    // each must be such a kind: a run can cover fewer kinds, for example the ones the admin's role can see, never more.
    public IReadOnlyList<string>? CoveredComponentKindKeys { get; }

    // True when this call carries the last page of every covered estate. Only then are vanished claims raised.
    public bool IsFinalPage { get; }

    // The items seen in this page.
    public IReadOnlyList<InventoryItem> Items { get; }

    // Registered components in covered estates and kinds (fetched).
    public IReadOnlyList<RegisteredComponentFact> Components { get; }

    // Existing claims for the item keys and components in play (fetched).
    public IReadOnlyList<ExistingClaimFact> Claims { get; }

    // Components already seen in earlier pages of this run (fetched).
    public IReadOnlyList<string> SeenThisRun { get; }

    // True only when the product's own server ran the live query as the admin, in the same process (document 25,
    // section 5.14, evidence level). A proposal of this build: the server sets it, and no tool exposes it, so the
    // caller of compare_inventory_list cannot raise the evidence level. Absent means as reported.
    public bool IsLiveQuery { get; }

    public static GapCompareRequest FromJson(JsonTreeNode input)
    {
        var wire = new WireObject(
            input,
            "input",
            "gapCheckRunId",
            "coveredEstateKeys",
            "coveredComponentKindKeys",
            "isFinalPage",
            "isLiveQuery",
            "items",
            "components",
            "claims",
            "seenThisRun");
        IReadOnlyList<string> estates = wire.Keys("coveredEstateKeys", required: true);
        if (estates.Count == 0)
        {
            throw OperationStop.Input(ReasonCodes.InputMalformed, "input.coveredEstateKeys must name at least one estate.");
        }

        IReadOnlyList<string>? kinds = wire.Has("coveredComponentKindKeys") ? wire.Keys("coveredComponentKindKeys") : null;
        IReadOnlyList<InventoryItem> items = wire.Objects(
                "items",
                required: false,
                "externalId",
                "sourceType",
                "componentKindKey",
                "estateKey",
                "labelAsSeen",
                "targetAsSeen",
                "suggestedClaimantObjectId")
            .Select(InventoryItem.FromJson)
            .ToList();
        IReadOnlyList<RegisteredComponentFact> components = wire.Objects("components", required: false, "technicalComponentId", "externalId", "componentKindKey", "estateKey", "isRetired")
            .Select(RegisteredComponentFact.FromJson)
            .ToList();
        var claims = new List<ExistingClaimFact>();
        foreach (WireObject claim in wire.Objects("claims", required: false, "claimId", "claimKey", "statusMeaning", "findingKind", "timesSeen", "lastSeenRunId"))
        {
            ExistingClaimFact fact = ExistingClaimFact.FromJson(claim);
            if (claims.Any(c => string.Equals(c.ClaimKey, fact.ClaimKey, StringComparison.Ordinal)))
            {
                throw OperationStop.Input(ReasonCodes.InputMalformed, "input.claims gives Claim Key " + SnapshotRead.Quote(fact.ClaimKey) + " twice, and a Claim Key is unique (O23).", fact.ClaimKey);
            }

            claims.Add(fact);
        }

        return new GapCompareRequest(
            wire.RequiredString("gapCheckRunId"),
            estates,
            kinds,
            wire.Bool("isFinalPage", false),
            items,
            components,
            claims,
            wire.Strings("seenThisRun"),
            wire.Bool("isLiveQuery", false));
    }
}

// A claim to create or update. The plan writes absolute values only, never a status: the engine leaves a claimed
// claim claimed and a dismissed claim dismissed (document 22, section 9.2).
public sealed class PlannedClaim
{
    internal PlannedClaim(
        string? claimId,
        string claimKey,
        FindingKind findingKind,
        string externalId,
        string sourceType,
        string componentKindKey,
        string estateKey,
        string? labelAsSeen,
        string? targetAsSeen,
        string? suggestedClaimantObjectId,
        string? technicalComponentId,
        string lastSeenRunId,
        DateTime lastSeenOnUtc,
        int timesSeen)
    {
        ClaimId = claimId;
        ClaimKey = claimKey;
        FindingKind = findingKind;
        ExternalId = externalId;
        SourceType = sourceType;
        ComponentKindKey = componentKindKey;
        EstateKey = estateKey;
        LabelAsSeen = labelAsSeen;
        TargetAsSeen = targetAsSeen;
        SuggestedClaimantObjectId = suggestedClaimantObjectId;
        TechnicalComponentId = technicalComponentId;
        LastSeenRunId = lastSeenRunId;
        LastSeenOnUtc = lastSeenOnUtc;
        TimesSeen = timesSeen;
    }

    // Set for an update of an existing claim. Null for a creation.
    public string? ClaimId { get; }

    public string ClaimKey { get; }

    public FindingKind FindingKind { get; }

    public string ExternalId { get; }

    public string SourceType { get; }

    public string ComponentKindKey { get; }

    public string EstateKey { get; }

    public string? LabelAsSeen { get; }

    public string? TargetAsSeen { get; }

    public string? SuggestedClaimantObjectId { get; }

    // Vanished claims: the registered component that was not seen (O23 Registered Component).
    public string? TechnicalComponentId { get; }

    public string LastSeenRunId { get; }

    public DateTime LastSeenOnUtc { get; }

    public int TimesSeen { get; }

    internal bool IsCreation => ClaimId == null;

    // A creation carries the stored item fields and starts open. An update carries only what a run changes.
    internal JsonTreeNode ToJson()
    {
        var writer = new WireWriter().Add("claimId", ClaimId).Add("claimKey", ClaimKey);
        if (IsCreation)
        {
            writer
                .Add("findingKind", EngineContract.FindingKinds.ToWire(FindingKind))
                .Add("statusMeaning", EngineContract.ClaimStatusMeanings.ToWire(ClaimStatusMeaning.Open))
                .Add("externalId", ExternalId)
                .Add("sourceType", SourceType)
                .Add("componentKindKey", ComponentKindKey)
                .Add("estateKey", EstateKey)
                .Add("labelAsSeen", LabelAsSeen)
                .Add("targetAsSeen", TargetAsSeen)
                .Add("suggestedClaimantObjectId", SuggestedClaimantObjectId);
        }

        return writer
            .Add("technicalComponentId", TechnicalComponentId)
            .Add("lastSeenRunId", LastSeenRunId)
            .Add("lastSeenOnUtc", LastSeenOnUtc)
            .Add("timesSeen", TimesSeen)
            .Build();
    }
}

// OP-13 output, the gap compare plan (document 25, section 5.14). The runtime applies each write through OP-08.
public sealed class GapComparePlan : IOperationPayload
{
    internal GapComparePlan(
        string gapCheckRunId,
        string evidenceLevel,
        IEnumerable<string> componentLastSeen,
        IEnumerable<PlannedClaim> newClaims,
        IEnumerable<PlannedClaim> updatedClaims,
        IEnumerable<PlannedClaim> vanishedClaims,
        int itemsSeen,
        int ignored)
    {
        GapCheckRunId = gapCheckRunId;
        EvidenceLevel = evidenceLevel;
        ComponentLastSeen = new ReadOnlyCollection<string>(componentLastSeen.ToList());
        NewClaims = new ReadOnlyCollection<PlannedClaim>(newClaims.ToList());
        UpdatedClaims = new ReadOnlyCollection<PlannedClaim>(updatedClaims.ToList());
        VanishedClaims = new ReadOnlyCollection<PlannedClaim>(vanishedClaims.ToList());
        ItemsSeen = itemsSeen;
        Ignored = ignored;
    }

    public string GapCheckRunId { get; }

    // The C07 evidence level key: read for the product's own live query, as-reported otherwise.
    public string EvidenceLevel { get; }

    // Components to stamp with Last Seen On, sorted by ID.
    public IReadOnlyList<string> ComponentLastSeen { get; }

    // Unregistered claims to create, sorted by Claim Key.
    public IReadOnlyList<PlannedClaim> NewClaims { get; }

    // Existing unregistered claims to update, sorted by Claim Key.
    public IReadOnlyList<PlannedClaim> UpdatedClaims { get; }

    // Vanished claims to create or update, sorted by Claim Key.
    public IReadOnlyList<PlannedClaim> VanishedClaims { get; }

    // Distinct items compared in this page.
    public int ItemsSeen { get; }

    // Items outside the covered estates or kinds.
    public int Ignored { get; }

    public JsonTreeNode ToJson()
    {
        return new WireWriter()
            .Add("gapCheckRunId", GapCheckRunId)
            .Add("evidenceLevel", EvidenceLevel)
            .Add("componentLastSeen", ComponentLastSeen)
            .Add("newClaims", WireWriter.Array(NewClaims.Select(c => c.ToJson())))
            .Add("updatedClaims", WireWriter.Array(UpdatedClaims.Select(c => c.ToJson())))
            .Add("vanishedClaims", WireWriter.Array(VanishedClaims.Select(c => c.ToJson())))
            .Add("counts", new WireWriter()
                .Add("itemsSeen", ItemsSeen)
                .Add("newClaims", NewClaims.Count)
                .Add("vanishedClaims", VanishedClaims.Count)
                .Add("ignored", Ignored)
                .Build())
            .Build();
    }
}

// OP-13 Compare a gap check result (document 25, section 5.14; document 22, section 9.2). Backs run_gap_check and
// compare_inventory_list (document 24, tools 35 and 36). Before this operation the comparison sat in the adapter; it is
// a rule, so it is in the core (working default, 28 issue 12). The engine never retires a component: a person
// confirms a vanished claim later. Its plan holds only what a claim stores, never a copy of the inventory.
//
// WORKING DEFAULT (open design question 1): the finding kinds (unregistered, vanished) and the claim status meanings
// (open, claimed, dismissed) are fixed engine meanings. Labels stay configurable.
public static class GapCompareOperation
{
    // Joins the parts of a Claim Key (document 22, O23: "joined with a separator").
    public const string ClaimKeySeparator = "|";

    private const string EvidenceRead = "read";
    private const string EvidenceAsReported = "as-reported";

    // The Claim Key: finding kind, source type and External Id, joined with the separator. Source type and External Id
    // are lowercased, because Microsoft resource types and IDs compare without case, so one item never gets two claims.
    public static string ClaimKey(FindingKind findingKind, string sourceType, string externalId)
    {
        return EngineContract.FindingKinds.ToWire(findingKind) + ClaimKeySeparator + sourceType.ToLowerInvariant() + ClaimKeySeparator + externalId.ToLowerInvariant();
    }

    public static EvaluationResult<GapComparePlan?> Evaluate(ConfigurationSnapshot snapshot, EvaluationContext context, GapCompareRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return OperationShell.Run(context, snapshot, needsSnapshot: true, () => Answer(snapshot, context.AsOfUtc, request));
    }

    public static EvaluationResult<GapComparePlan?> Evaluate(ConfigurationSnapshot snapshot, EvaluationContext context, JsonTreeNode input)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        return OperationShell.Run(context, snapshot, needsSnapshot: true, () => Answer(snapshot, context.AsOfUtc, GapCompareRequest.FromJson(input)));
    }

    private static OperationAnswer<GapComparePlan> Answer(ConfigurationSnapshot snapshot, DateTime asOf, GapCompareRequest request)
    {
        var trace = new List<RuleTrace>();

        // The covered estates must be values of the estate list.
        foreach (string estate in request.CoveredEstateKeys)
        {
            SnapshotRead.RequireExisting(snapshot, "list-value", "estate/" + estate, "input.coveredEstateKeys");
            trace.Add(new RuleTrace("List Value", "estate/" + estate, RuleTraceRole.Matched));
        }

        HashSet<string> estates = new(request.CoveredEstateKeys, StringComparer.Ordinal);
        HashSet<string> kinds = CoveredKinds(snapshot, request.CoveredComponentKindKeys);
        trace.AddRange(kinds.OrderBy(k => k, StringComparer.Ordinal).Select(k => new RuleTrace("Component Kind", k, RuleTraceRole.Matched)));

        Dictionary<string, ExistingClaimFact> claimsByKey = request.Claims.ToDictionary(c => c.ClaimKey, StringComparer.Ordinal);
        List<RegisteredComponentFact> registered = request.Components.Where(c => !c.IsRetired && c.ExternalId != null).ToList();

        var stamped = new SortedSet<string>(StringComparer.Ordinal);
        var newClaims = new SortedDictionary<string, PlannedClaim>(StringComparer.Ordinal);
        var updated = new SortedDictionary<string, PlannedClaim>(StringComparer.Ordinal);
        var compared = new HashSet<string>(StringComparer.Ordinal);
        var ignored = new SortedDictionary<string, IgnoredGroup>(StringComparer.Ordinal);
        int ignoredCount = 0;

        foreach (InventoryItem item in request.Items)
        {
            // Rule 5: items outside the covered estates or kinds are ignored and counted.
            string? hintProblem = null;
            string? kind = item.ComponentKindKey ?? KindFromHint(snapshot, item.SourceType, out hintProblem);
            IgnoredGroup? cause = !estates.Contains(item.EstateKey)
                ? new IgnoredGroup(IgnoredCause.Estate, item.EstateKey, null)
                : kind == null
                    ? new IgnoredGroup(IgnoredCause.Hint, item.SourceType, hintProblem)
                    : !kinds.Contains(kind)
                        ? new IgnoredGroup(IgnoredCause.Kind, kind, null)
                        : null;
            if (cause != null)
            {
                ignoredCount++;
                if (!ignored.TryGetValue(cause.SortKey, out IgnoredGroup? group))
                {
                    group = cause;
                    ignored[cause.SortKey] = group;
                }

                group.Count++;
                continue;
            }

            if (!compared.Add(item.SourceType.ToLowerInvariant() + ClaimKeySeparator + item.ExternalId.ToLowerInvariant()))
            {
                // The same item twice in one page is compared once.
                continue;
            }

            // Rule 1: a registered, not retired component with the same External Id is stamped. No claim.
            string[] matches = registered
                .Where(c => string.Equals(c.ExternalId, item.ExternalId, StringComparison.OrdinalIgnoreCase))
                .Select(c => c.TechnicalComponentId)
                .ToArray();
            if (matches.Length > 0)
            {
                stamped.UnionWith(matches);
                continue;
            }

            // Rules 2 and 3: unregistered. An existing claim with the key is updated whatever its status; otherwise a
            // new open claim, with the suggested claimant as a hint only.
            string claimKey = ClaimKey(FindingKind.Unregistered, item.SourceType, item.ExternalId);
            if (claimsByKey.TryGetValue(claimKey, out ExistingClaimFact? existing))
            {
                updated[claimKey] = Update(existing, request.GapCheckRunId, asOf, item.ExternalId, item.SourceType, kind!, item.EstateKey, null);
            }
            else
            {
                newClaims[claimKey] = new PlannedClaim(
                    null,
                    claimKey,
                    FindingKind.Unregistered,
                    item.ExternalId,
                    item.SourceType,
                    kind!,
                    item.EstateKey,
                    item.LabelAsSeen,
                    item.TargetAsSeen,
                    item.SuggestedClaimantObjectId,
                    null,
                    request.GapCheckRunId,
                    asOf,
                    1);
            }
        }

        // Rule 4 (sweep proposal): only on the final page, every registered, not retired component of a covered kind in
        // a covered estate that the run did not see gets a vanished claim. Partial runs never raise one.
        var vanished = new SortedDictionary<string, PlannedClaim>(StringComparer.Ordinal);
        var reasons = new List<Reason>();
        if (request.IsFinalPage)
        {
            HashSet<string> seen = new(request.SeenThisRun, StringComparer.Ordinal);
            seen.UnionWith(stamped);
            foreach (RegisteredComponentFact component in registered.Where(c => estates.Contains(c.EstateKey) && kinds.Contains(c.ComponentKindKey) && !seen.Contains(c.TechnicalComponentId)))
            {
                string sourceType = VanishedSourceType(component.ComponentKindKey);
                string claimKey = ClaimKey(FindingKind.Vanished, sourceType, component.ExternalId!);
                PlannedClaim planned = claimsByKey.TryGetValue(claimKey, out ExistingClaimFact? existing)
                    ? Update(existing, request.GapCheckRunId, asOf, component.ExternalId!, sourceType, component.ComponentKindKey, component.EstateKey, component.TechnicalComponentId)
                    : new PlannedClaim(null, claimKey, FindingKind.Vanished, component.ExternalId!, sourceType, component.ComponentKindKey, component.EstateKey, null, null, null, component.TechnicalComponentId, request.GapCheckRunId, asOf, 1);
                if (!vanished.ContainsKey(claimKey))
                {
                    vanished[claimKey] = planned;
                }
            }
        }
        else
        {
            reasons.Add(ReasonCatalog.Create(ReasonCodes.GapCheckPartialRun, "Gap Check Run", request.GapCheckRunId));
        }

        if (!request.IsLiveQuery)
        {
            reasons.Add(ReasonCatalog.Create(ReasonCodes.GapCheckAsReported, "Gap Check Run", request.GapCheckRunId));
        }

        reasons.AddRange(ignored.Values.Select(Ignored));

        var plan = new GapComparePlan(
            request.GapCheckRunId,
            request.IsLiveQuery ? EvidenceRead : EvidenceAsReported,
            stamped,
            newClaims.Values,
            updated.Values,
            vanished.Values,
            compared.Count,
            ignoredCount);
        return new OperationAnswer<GapComparePlan>("compared", plan, reasons, trace);
    }

    // An update keeps the claim's status. Times Seen rises once per run: a second page of the same run that sees the
    // item again only moves Last Seen On.
    private static PlannedClaim Update(ExistingClaimFact existing, string runId, DateTime asOf, string externalId, string sourceType, string kind, string estate, string? componentId)
    {
        bool sameRun = string.Equals(existing.LastSeenRunId, runId, StringComparison.Ordinal);
        return new PlannedClaim(
            existing.ClaimId,
            existing.ClaimKey,
            existing.FindingKind,
            externalId,
            sourceType,
            kind,
            estate,
            null,
            null,
            null,
            componentId,
            runId,
            asOf,
            sameRun ? existing.TimesSeen : existing.TimesSeen + 1);
    }

    // A vanished item was not seen, so it has no source type as seen. Its Claim Key uses the component kind key, which
    // never changes, so the same component always finds the same vanished claim (a proposal of this build).
    private static string VanishedSourceType(string componentKindKey)
    {
        return componentKindKey;
    }

    // The covered kinds: the active kinds with Is Gap Check Covered yes, or the given subset of them.
    private static HashSet<string> CoveredKinds(ConfigurationSnapshot snapshot, IReadOnlyList<string>? given)
    {
        HashSet<string> covered = new(
            SnapshotRead.ActiveRows(snapshot, "component-kind").Where(row => SnapshotRead.Flag(row, "isGapCheckCovered")).Select(row => row.KeyPath),
            StringComparer.Ordinal);
        if (given == null)
        {
            return covered;
        }

        foreach (string key in given)
        {
            SnapshotRead.RequireExisting(snapshot, "component-kind", key, "input.coveredComponentKindKeys");
            if (!covered.Contains(key))
            {
                throw OperationStop.Input(
                    ReasonCodes.InputMalformed,
                    "input.coveredComponentKindKeys names " + SnapshotRead.Quote(key) + ", which is not an active component kind the gap check covers (C16 Is Gap Check Covered). A run never covers more than the configuration.",
                    key);
            }
        }

        return new HashSet<string>(given, StringComparer.Ordinal);
    }

    // The component kind whose Source Type Hint equals the source type, ignoring case. Null when none or more than one
    // matches: the engine never picks one silently, so the item is ignored and the reason says why.
    private static string? KindFromHint(ConfigurationSnapshot snapshot, string sourceType, out string? problem)
    {
        string[] matches = SnapshotRead.ActiveRows(snapshot, "component-kind")
            .Where(row => string.Equals(SnapshotRead.Text(row, "sourceTypeHint"), sourceType, StringComparison.OrdinalIgnoreCase))
            .Select(row => row.KeyPath)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
        problem = matches.Length > 1 ? string.Join(",", matches) : null;
        return matches.Length == 1 ? matches[0] : null;
    }

    private static Reason Ignored(IgnoredGroup group)
    {
        string count = group.Count.ToString(CultureInfo.InvariantCulture) + (group.Count == 1 ? " item" : " items");
        switch (group.Cause)
        {
            case IgnoredCause.Estate:
                return Ignored(count + " from estate " + SnapshotRead.Quote(group.Value) + " ignored, because the run does not cover that estate.", "List Value", "estate/" + group.Value);
            case IgnoredCause.Kind:
                return Ignored(count + " of component kind " + SnapshotRead.Quote(group.Value) + " ignored, because the run does not cover that kind.", "Component Kind", group.Value);
            default:
                return Ignored(
                    group.Detail != null
                        ? count + " of source type " + SnapshotRead.Quote(group.Value) + " ignored, because the source type matches the hint of more than one component kind (" + group.Detail + ")."
                        : count + " of source type " + SnapshotRead.Quote(group.Value) + " ignored, because no component kind is given and no Source Type Hint matches it.",
                    "Component Kind",
                    group.Value);
        }
    }

    private static Reason Ignored(string message, string entity, string reference)
    {
        return new Reason(ReasonCodes.GapCheckItemIgnored, ReasonCatalog.Get(ReasonCodes.GapCheckItemIgnored).DefaultSeverity, message, entity, reference);
    }

    private enum IgnoredCause
    {
        Estate,
        Kind,
        Hint,
    }

    // Ignored items, grouped by cause and value, so a large page gives one reason per cause, not one per item.
    private sealed class IgnoredGroup
    {
        public IgnoredGroup(IgnoredCause cause, string value, string? detail)
        {
            Cause = cause;
            Value = value;
            Detail = detail;
        }

        public IgnoredCause Cause { get; }

        // The estate key, the kind key or the source type.
        public string Value { get; }

        // For a hint that matches several kinds: their keys.
        public string? Detail { get; }

        public int Count { get; set; }

        public string SortKey => ((int)Cause).ToString(CultureInfo.InvariantCulture) + ClaimKeySeparator + Value;
    }
}
