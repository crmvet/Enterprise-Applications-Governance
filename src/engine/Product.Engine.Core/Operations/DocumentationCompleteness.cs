using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;
using Product.Engine.Core.Snapshots;

namespace Product.Engine.Core.Operations;

// One Document Link (O19) as the adapter fetches it (document 25, section 5.8, DocumentFact).
// A row exists only once a link has been recorded, so "missing" is never a stored status (working default, 28 issue 16).
public sealed class DocumentFact
{
    public DocumentFact(
        string documentLinkId,
        string documentTypeKey,
        DocumentStatusMeaning status,
        bool hasUrl = true,
        string? requirementKey = null,
        DateTime? lastCheckedOnUtc = null,
        DateTime? lastUpdatedOnUtc = null)
    {
        if (string.IsNullOrEmpty(documentLinkId))
        {
            throw new ArgumentException("A document link needs its row ID.", nameof(documentLinkId));
        }

        if (!ConfigurationKey.IsValid(documentTypeKey))
        {
            throw new ArgumentException("A document type key must use the configuration key format.", nameof(documentTypeKey));
        }

        if (requirementKey != null && !ConfigurationKey.IsValid(requirementKey))
        {
            throw new ArgumentException("A documentation requirement key must use the configuration key format.", nameof(requirementKey));
        }

        if (!Enum.IsDefined(typeof(DocumentStatusMeaning), status) || status == DocumentStatusMeaning.Missing)
        {
            throw new ArgumentException("A stored document status is draft, current or stale. Missing is computed, never stored.", nameof(status));
        }

        DocumentLinkId = documentLinkId;
        DocumentTypeKey = documentTypeKey;
        Status = status;
        HasUrl = hasUrl;
        RequirementKey = requirementKey;
        LastCheckedOnUtc = Utc(lastCheckedOnUtc, nameof(lastCheckedOnUtc));
        LastUpdatedOnUtc = Utc(lastUpdatedOnUtc, nameof(lastUpdatedOnUtc));
    }

    public string DocumentLinkId { get; }

    // Its document type (C22).
    public string DocumentTypeKey { get; }

    // The stored document status meaning: draft, current or stale.
    public DocumentStatusMeaning Status { get; }

    // Whether a link is recorded. O19 Url is required, so this is false only for a broken row.
    public bool HasUrl { get; }

    // The Documentation Requirement (C23) it meets, if set.
    public string? RequirementKey { get; }

    // When someone last confirmed the document is still true (O19 Last Checked On).
    public DateTime? LastCheckedOnUtc { get; }

    // When the owner says it last changed (O19 Last Updated On). A proposal of this build: document 25 picks "the
    // newest" link of a type, and this is the date that says which one is newest.
    public DateTime? LastUpdatedOnUtc { get; }

    internal static DocumentFact FromWire(WireObject wire)
    {
        string? statusText = wire.OptionalString("status");
        if (statusText == EngineContract.DocumentStatusMeanings.ToWire(DocumentStatusMeaning.Missing))
        {
            throw OperationStop.Input(
                ReasonCodes.InputMalformed,
                wire.Where + ".status is \"missing\". Missing is computed from configuration and never stored on a Document Link (28, issue 16).");
        }

        return new DocumentFact(
            wire.RequiredString("documentLinkId"),
            wire.RequiredKey("documentTypeKey"),
            wire.Contract("status", EngineContract.DocumentStatusMeanings),
            wire.Bool("hasUrl", true),
            wire.OptionalKey("requirementKey"),
            wire.OptionalUtc("lastCheckedOnUtc"),
            wire.OptionalUtc("lastUpdatedOnUtc"));
    }

    private static DateTime? Utc(DateTime? value, string name)
    {
        if (value.HasValue && value.Value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Every moment is a UTC date-time (DateTimeKind.Utc).", name);
        }

        return value;
    }
}

// OP-06 input (document 25, section 5.8).
public sealed class DocumentationRequest
{
    public DocumentationRequest(string applicationKindKey, string? tierKey, string lifecycleStageKey, IEnumerable<DocumentFact> links)
    {
        if (!ConfigurationKey.IsValid(applicationKindKey))
        {
            throw new ArgumentException("An application kind key must use the configuration key format.", nameof(applicationKindKey));
        }

        if (tierKey != null && !ConfigurationKey.IsValid(tierKey))
        {
            throw new ArgumentException("A tier key must use the configuration key format.", nameof(tierKey));
        }

        if (!ConfigurationKey.IsValid(lifecycleStageKey))
        {
            throw new ArgumentException("A lifecycle stage key must use the configuration key format.", nameof(lifecycleStageKey));
        }

        ApplicationKindKey = applicationKindKey;
        TierKey = tierKey;
        LifecycleStageKey = lifecycleStageKey;
        Links = new ReadOnlyCollection<DocumentFact>((links ?? throw new ArgumentNullException(nameof(links))).ToList());
    }

    // The application's kind (C15).
    public string ApplicationKindKey { get; }

    // The application's tier (C08). Null while the application has no tier yet: then only requirements without a
    // tier apply (a proposal of this build; document 25 lists the tier without saying it may be absent).
    public string? TierKey { get; }

    // The application's stage (C12).
    public string LifecycleStageKey { get; }

    // The application's Document Links (O19), fetched by the adapter.
    public IReadOnlyList<DocumentFact> Links { get; }

    public static DocumentationRequest FromJson(JsonTreeNode input)
    {
        var wire = new WireObject(input, "input", "applicationKindKey", "tierKey", "lifecycleStageKey", "links");
        IReadOnlyList<DocumentFact> links = wire
            .Objects("links", required: false, "documentLinkId", "documentTypeKey", "requirementKey", "status", "hasUrl", "lastCheckedOnUtc", "lastUpdatedOnUtc")
            .Select(DocumentFact.FromWire)
            .ToList();
        return new DocumentationRequest(wire.RequiredKey("applicationKindKey"), wire.OptionalKey("tierKey"), wire.RequiredKey("lifecycleStageKey"), links);
    }
}

// One applicable Documentation Requirement and the effective status of the document that meets it.
public sealed class DocumentationItem
{
    public DocumentationItem(string requirementKey, string documentTypeKey, DocumentStatusMeaning status, string? documentLinkId, DateTime? staleOnUtc)
    {
        RequirementKey = requirementKey;
        DocumentTypeKey = documentTypeKey;
        Status = status;
        DocumentLinkId = documentLinkId;
        StaleOnUtc = staleOnUtc;
    }

    public string RequirementKey { get; }

    public string DocumentTypeKey { get; }

    // The effective status: missing, draft, current or stale. Missing is computed here, never stored.
    public DocumentStatusMeaning Status { get; }

    // The best link (document 25, OP-06 rule 2). Null when no link of the type exists.
    public string? DocumentLinkId { get; }

    // When the best link becomes (or became) stale. Null when the type has no freshness or the link no check date.
    public DateTime? StaleOnUtc { get; }

    internal JsonTreeNode ToJson()
    {
        return new WireWriter()
            .Add("requirementKey", RequirementKey)
            .Add("documentTypeKey", DocumentTypeKey)
            .Add("status", EngineContract.DocumentStatusMeanings.ToWire(Status))
            .Add("documentLinkId", DocumentLinkId)
            .Add("staleOnUtc", StaleOnUtc)
            .Build();
    }
}

// OP-06 output (document 25, section 5.8).
public sealed class DocumentationResult : IOperationPayload
{
    public DocumentationResult(IEnumerable<DocumentationItem> items, IEnumerable<KeyValuePair<string, DateTime>> staleOnByLink)
    {
        Items = new ReadOnlyCollection<DocumentationItem>(items.ToList());
        var staleOn = new SortedDictionary<string, DateTime>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, DateTime> pair in staleOnByLink)
        {
            staleOn[pair.Key] = pair.Value;
        }

        StaleOnByLink = new ReadOnlyDictionary<string, DateTime>(staleOn);
    }

    // True when every applicable requirement is current. True also when no requirement applies.
    public bool Complete => Items.All(item => item.Status == DocumentStatusMeaning.Current);

    // One per applicable requirement, by requirement key.
    public IReadOnlyList<DocumentationItem> Items { get; }

    public int MissingCount => Items.Count(item => item.Status == DocumentStatusMeaning.Missing);

    public int DraftCount => Items.Count(item => item.Status == DocumentStatusMeaning.Draft);

    public int StaleCount => Items.Count(item => item.Status == DocumentStatusMeaning.Stale);

    // The value the engine writes into O19 Stale On, by link ID, for every link with a computable date (a proposal
    // of this build: document 25 says the engine writes Stale On, and links that are not the best one need it too).
    public IReadOnlyDictionary<string, DateTime> StaleOnByLink { get; }

    public JsonTreeNode ToJson()
    {
        return new WireWriter()
            .Add("complete", Complete)
            .Add("items", WireWriter.Array(Items.Select(item => item.ToJson())))
            .Add("missingCount", MissingCount)
            .Add("draftCount", DraftCount)
            .Add("staleCount", StaleCount)
            .Add("staleOnByLink", JsonTreeNode.CreateObject(StaleOnByLink.Select(p => new KeyValuePair<string, JsonTreeNode>(p.Key, JsonTreeNode.FromUtcDateTime(p.Value)))))
            .Build();
    }
}

// OP-06 Evaluate documentation completeness (document 25, section 5.8; document 23, P07).
// 1. Applicable requirements are the active C23 rows whose optional tier and kind match, and whose From Stage ranks
//    at or below the application's stage (C12 Stage Rank; display numbers are never logic).
// 2. The best link for a requirement is the newest link tied to it, else the newest link of the same document type.
// 3. Effective status: missing when there is no link or no URL; stale when the stored status is current and Last
//    Checked On plus the type's Freshness Days is before asOfUtc; otherwise the stored status.
// "Missing" is computed here from configuration every time, so no stored row can disagree with it (28, issue 16).
//
// WORKING DEFAULT (open design question 9): the stored status is trusted as given. For a document type with Current
// Needs Person (C22), the write path keeps an AI client's "current" out of the store until the person confirms it on a
// card (record_document_link stores it as draft, and OP-08 refuses an AI actor's "current" on every
// path), so this operation never reads Current Needs Person itself.
public static class DocumentationOperation
{
    private const string Requirement = "documentation-requirement";
    private const string DocumentType = "document-type";
    private const string Stage = "lifecycle-stage";

    public static EvaluationResult<DocumentationResult?> Evaluate(ConfigurationSnapshot snapshot, EvaluationContext context, DocumentationRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return OperationShell.Run(context, snapshot, needsSnapshot: true, () => Answer(snapshot, context, request));
    }

    public static EvaluationResult<DocumentationResult?> Evaluate(ConfigurationSnapshot snapshot, EvaluationContext context, JsonTreeNode input)
    {
        return OperationShell.Run(context, snapshot, needsSnapshot: true, () => Answer(snapshot, context, DocumentationRequest.FromJson(input)));
    }

    private static OperationAnswer<DocumentationResult> Answer(ConfigurationSnapshot snapshot, EvaluationContext context, DocumentationRequest request)
    {
        SnapshotRead.RequireExisting(snapshot, "application-kind", request.ApplicationKindKey, "input.applicationKindKey");
        if (request.TierKey != null)
        {
            SnapshotRead.RequireExisting(snapshot, "tier", request.TierKey, "input.tierKey");
        }

        SnapshotRow stage = SnapshotRead.RequireExisting(snapshot, Stage, request.LifecycleStageKey, "input.lifecycleStageKey");
        int stageRank = StageRank(stage);
        CheckLinks(snapshot, request.Links);

        List<SnapshotRow> applicable = SnapshotRead.ActiveRows(snapshot, Requirement)
            .Where(row => Matches(row, "tierKey", request.TierKey) && Matches(row, "applicationKindKey", request.ApplicationKindKey))
            .Where(row => StageRank(FromStage(snapshot, row)) <= stageRank)
            .OrderBy(row => row.Key, StringComparer.Ordinal)
            .ToList();

        var items = new List<DocumentationItem>();
        var reasons = new List<Reason>();
        var trace = new List<RuleTrace>();
        foreach (SnapshotRow requirement in applicable)
        {
            string documentTypeKey = DocumentTypeOf(requirement);
            SnapshotRow type = SnapshotRead.Find(snapshot, DocumentType, documentTypeKey)
                ?? throw OperationStop.Broken(
                    ReasonCodes.RefUnresolved,
                    "Documentation Requirement " + SnapshotRead.Quote(requirement.Key) + " names document type " + SnapshotRead.Quote(documentTypeKey) + ", which does not exist, so the configuration is broken.",
                    "Documentation Requirement",
                    requirement.Key);

            DocumentFact? best = BestLink(request.Links, requirement.Key, documentTypeKey);
            DateTime? staleOn = best == null ? null : StaleOn(snapshot, best);
            DocumentStatusMeaning status = EffectiveStatus(best, type, staleOn, context.AsOfUtc);
            items.Add(new DocumentationItem(requirement.Key, documentTypeKey, status, best?.DocumentLinkId, staleOn));
            trace.Add(new RuleTrace("Documentation Requirement", requirement.Key, RuleTraceRole.Required));
            Reason? reason = ItemReason(requirement.Key, documentTypeKey, status, best, type);
            if (reason != null)
            {
                reasons.Add(reason);
            }
        }

        var staleOnByLink = new List<KeyValuePair<string, DateTime>>();
        foreach (DocumentFact link in request.Links)
        {
            DateTime? staleOn = StaleOn(snapshot, link);
            if (staleOn.HasValue)
            {
                staleOnByLink.Add(new KeyValuePair<string, DateTime>(link.DocumentLinkId, staleOn.Value));
            }
        }

        var result = new DocumentationResult(items, staleOnByLink);
        return new OperationAnswer<DocumentationResult>(result.Complete ? "complete" : "incomplete", result, reasons, trace);
    }

    // Link IDs are unique, and every key a link names exists in the snapshot. A retired type or requirement still
    // exists: old links keep pointing at it.
    private static void CheckLinks(ConfigurationSnapshot snapshot, IReadOnlyList<DocumentFact> links)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < links.Count; i++)
        {
            DocumentFact link = links[i];
            string where = "input.links[" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]";
            if (!ids.Add(link.DocumentLinkId))
            {
                throw OperationStop.Input(ReasonCodes.InputMalformed, "input.links gives the document link " + SnapshotRead.Quote(link.DocumentLinkId) + " twice.", link.DocumentLinkId);
            }

            SnapshotRead.RequireExisting(snapshot, DocumentType, link.DocumentTypeKey, where + ".documentTypeKey");
            if (link.RequirementKey != null)
            {
                SnapshotRead.RequireExisting(snapshot, Requirement, link.RequirementKey, where + ".requirementKey");
            }
        }
    }

    // An optional scope column matches when it is empty or equals the request's value. A request without a value
    // matches only rows that leave the column empty.
    private static bool Matches(SnapshotRow row, string column, string? value)
    {
        string? scope = SnapshotRead.Text(row, column);
        return scope == null || string.Equals(scope, value, StringComparison.Ordinal);
    }

    private static string DocumentTypeOf(SnapshotRow requirement)
    {
        return SnapshotRead.Text(requirement, "documentTypeKey")
            ?? throw OperationStop.Broken(
                ReasonCodes.ShapeSchema,
                "Documentation Requirement " + SnapshotRead.Quote(requirement.Key) + " names no document type, so the configuration is broken.",
                "Documentation Requirement",
                requirement.Key);
    }

    private static SnapshotRow FromStage(ConfigurationSnapshot snapshot, SnapshotRow requirement)
    {
        string? key = SnapshotRead.Text(requirement, "fromStageKey");
        SnapshotRow? stage = SnapshotRead.Find(snapshot, Stage, key);
        if (key == null || stage == null)
        {
            throw OperationStop.Broken(
                ReasonCodes.RefUnresolved,
                "Documentation Requirement " + SnapshotRead.Quote(requirement.Key) + (key == null ? " names no From Stage" : " names From Stage " + SnapshotRead.Quote(key) + ", which does not exist")
                + ", so the configuration is broken.",
                "Documentation Requirement",
                requirement.Key);
        }

        return stage;
    }

    // C12 Stage Rank orders stages for logic (document 25, section 16). It is required, so a stage without one is broken.
    internal static int StageRank(SnapshotRow stage)
    {
        return SnapshotRead.Int(stage, Stage, "stageRank")
            ?? throw OperationStop.Broken(
                ReasonCodes.ShapeSchema,
                "Lifecycle Stage " + SnapshotRead.Quote(stage.Key) + " has no stage rank, so stages cannot be ordered. The configuration is broken.",
                "Lifecycle Stage",
                stage.Key);
    }

    // Rule 2: the links tied to the requirement first, else every link of the same document type; the newest wins.
    // A tie to a requirement of another document type does not count as a tie. Newest means the latest Last Updated
    // On, then the latest Last Checked On, then the smallest link ID, so the choice never depends on input order.
    private static DocumentFact? BestLink(IReadOnlyList<DocumentFact> links, string requirementKey, string documentTypeKey)
    {
        List<DocumentFact> ofType = links.Where(link => string.Equals(link.DocumentTypeKey, documentTypeKey, StringComparison.Ordinal)).ToList();
        List<DocumentFact> tied = ofType.Where(link => string.Equals(link.RequirementKey, requirementKey, StringComparison.Ordinal)).ToList();
        return (tied.Count > 0 ? tied : ofType)
            .OrderByDescending(link => link.LastUpdatedOnUtc ?? DateTime.MinValue)
            .ThenByDescending(link => link.LastCheckedOnUtc ?? DateTime.MinValue)
            .ThenBy(link => link.DocumentLinkId, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    // Stale On is Last Checked On plus the type's Freshness Days (document 23, P07 step 7). A link never checked
    // counts from Last Updated On (a proposal: recording a link is the first time someone vouched for it).
    // Null when the type has no freshness or the link has neither date.
    private static DateTime? StaleOn(ConfigurationSnapshot snapshot, DocumentFact link)
    {
        SnapshotRow? type = SnapshotRead.Find(snapshot, DocumentType, link.DocumentTypeKey);
        int? days = type == null ? null : SnapshotRead.Int(type, DocumentType, "freshnessDays");
        DateTime? basis = link.LastCheckedOnUtc ?? link.LastUpdatedOnUtc;
        if (!days.HasValue || !basis.HasValue)
        {
            return null;
        }

        if (days.Value < 1)
        {
            throw OperationStop.Broken(
                ReasonCodes.ValueRange,
                "Document Type " + SnapshotRead.Quote(link.DocumentTypeKey) + " has a freshnessDays below 1, so the configuration is broken.",
                "Document Type",
                link.DocumentTypeKey);
        }

        return (DateTime.MaxValue - basis.Value).TotalDays <= days.Value ? null : basis.Value.AddDays(days.Value);
    }

    // Rule 3. A current link of a type with freshness but no date at all cannot be shown to be fresh, so it counts as
    // stale (a proposal: the engine never assumes a document is still true).
    private static DocumentStatusMeaning EffectiveStatus(DocumentFact? best, SnapshotRow type, DateTime? staleOn, DateTime asOfUtc)
    {
        if (best == null || !best.HasUrl)
        {
            return DocumentStatusMeaning.Missing;
        }

        if (best.Status != DocumentStatusMeaning.Current)
        {
            return best.Status;
        }

        bool hasFreshness = SnapshotRead.HasValue(type, "freshnessDays");
        if (hasFreshness && (staleOn.HasValue ? staleOn.Value < asOfUtc : best.LastCheckedOnUtc == null && best.LastUpdatedOnUtc == null))
        {
            return DocumentStatusMeaning.Stale;
        }

        return DocumentStatusMeaning.Current;
    }

    private static Reason? ItemReason(string requirementKey, string documentTypeKey, DocumentStatusMeaning status, DocumentFact? best, SnapshotRow type)
    {
        string subject = "The required document of type " + SnapshotRead.Quote(documentTypeKey) + " (requirement " + SnapshotRead.Quote(requirementKey) + ")";
        switch (status)
        {
            case DocumentStatusMeaning.Missing:
                return Item(ReasonCodes.DocumentationMissing, best == null ? subject + " has no link." : subject + " has a link with no URL.", requirementKey);
            case DocumentStatusMeaning.Draft:
                return Item(ReasonCodes.DocumentationDraft, subject + " is still a draft.", requirementKey);
            case DocumentStatusMeaning.Stale:
                bool undated = best != null && best.Status == DocumentStatusMeaning.Current && SnapshotRead.HasValue(type, "freshnessDays")
                    && best.LastCheckedOnUtc == null && best.LastUpdatedOnUtc == null;
                return Item(ReasonCodes.DocumentationStale, undated ? subject + " has no check date, so it cannot be shown to be fresh." : subject + " is past its freshness date.", requirementKey);
            default:
                return null;
        }
    }

    private static Reason Item(string code, string message, string requirementKey)
    {
        ReasonCodeInfo info = ReasonCatalog.Get(code);
        return new Reason(code, info.DefaultSeverity, message, "Documentation Requirement", requirementKey);
    }
}
