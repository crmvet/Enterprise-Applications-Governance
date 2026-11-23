using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Product.Engine.Core.Contract;

namespace Product.Engine.Core.Operations;

// What kind of row a guarded write touches (document 22, section 2.3 history classes; document 25, G-01 to G-17).
internal enum GuardEntityKind
{
    // Live configuration rows C01 and C03 to C45. Written only inside activation (G-01).
    Configuration,

    // C02 Configuration Set Version. Created only inside activation, never changed (G-01, G-07).
    ConfigurationSetVersion,

    // C46 Configuration Draft (G-16).
    ConfigurationDraft,

    // O24 Audit Entry. Append-only (G-07).
    AuditEntry,

    // O01 to O23.
    Operational,
}

// When a class F row is final (document 22, section 2.3, and the note of each entity in the logical model). From then on
// it is frozen: only the documented transitions remain, such as Superseded By on a Decision.
internal enum FinalWhen
{
    // Final at insert: never updated (O14, "Class F, final at insert"; O13 apart from its fill-once columns). Also every
    // class F entity the guard names no draft state for, so a new class F entity in the model is frozen until its rule is
    // written: refuse more, never less.
    AtInsert,

    // Final once the column FinalColumn holds a value: O08 when Tier Decision is set, O22 when Finished On is set.
    ColumnSet,

    // Final by its meaning copy, judged by the entity's own rule: O09 Decision (G-05) and O10 Approval Record (G-06).
    MeaningFinal,

    // C46 Configuration Draft: frozen at submit, final when activated or discarded (G-16).
    DraftStatus,
}

// How a row of an entity may be deleted (document 22, section 2.5).
internal enum GuardDeletePolicy
{
    // Never deleted: archived, withdrawn, cancelled, superseded or ended with a date instead.
    Never,

    // Only while its meaning copy is draft, by its creator (a person, or an AI client for that person).
    DraftOnly,

    // By its creator within the link-undo-minutes setting (document 26, section 5.3).
    CreatorUndo,
}

// One entity the guard knows. The column names are the contract layer's lowerCamelCase forms of the logical names in
// document 22 (document 20, section 3.3). Configuration references travel as keys. The history class and the column list
// come from the logical model compiled into the engine (LogicalModelFacts), never from this file.
//
// WORKING DEFAULT (open design question 1): which column holds a status, which meaning a new AI row starts at, and
// which columns only the engine writes are fixed engine knowledge, like the meanings. Labels stay configurable, and a
// client can add protection through C43 Column Protection Rule rows, never remove it.
internal sealed class GuardEntity
{
    public GuardEntity(
        string key,
        string id,
        GuardEntityKind kind,
        GuardDeletePolicy delete = GuardDeletePolicy.Never,
        string? statusColumn = null,
        string? draftMeaning = null,
        LinkCategory? linkCategory = null,
        IEnumerable<string>? engineComputed = null,
        IDictionary<string, EffectKind[]?>? effectOnly = null,
        EffectKind[]? effectOnlyRows = null,
        FinalWhen finalWhen = FinalWhen.AtInsert,
        string? finalColumn = null)
    {
        Key = key;
        Id = id;
        Kind = kind;
        Delete = delete;
        StatusColumn = statusColumn;
        MeaningColumn = statusColumn == null ? null : statusColumn + "Meaning";
        DraftMeaning = draftMeaning;
        LinkCategory = linkCategory;
        EngineComputed = new ReadOnlyCollection<string>((engineComputed ?? Enumerable.Empty<string>()).ToList());
        EffectOnly = new ReadOnlyDictionary<string, EffectKind[]?>(new Dictionary<string, EffectKind[]?>(effectOnly ?? new Dictionary<string, EffectKind[]?>(), StringComparer.Ordinal));
        EffectOnlyRows = effectOnlyRows;
        FinalWhen = finalWhen;
        FinalColumn = finalColumn;

        // The model is the source of the history class and of the column list. A guard entity the model does not hold,
        // or holds under another ID, is a build error that the core tests catch.
        Model = LogicalModelFacts.Get(key);
        if (!string.Equals(Model.Id, id, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The guard names " + key + " as " + id + ", and the logical model as " + Model.Id + ".");
        }

        if ((finalWhen == FinalWhen.ColumnSet) != (finalColumn != null) || (finalColumn != null && !Model.HasColumn(finalColumn)))
        {
            throw new InvalidOperationException("The guard's final column of " + key + " is not a column of the logical model.");
        }
    }

    // The entity key, kebab-case of the logical name, such as "approval-record".
    public string Key { get; }

    // The document 20 ID, such as "O10".
    public string Id { get; }

    public string LogicalName => SnapshotRead.LogicalName(Key);

    public GuardEntityKind Kind { get; }

    public GuardDeletePolicy Delete { get; }

    // The column that holds a C05 status key: status, outcome (O10) or result (O14). Null when the entity has no status.
    public string? StatusColumn { get; }

    // The meaning copy the engine writes from the status row (document 22, section 2.1.4).
    public string? MeaningColumn { get; }

    // The meaning an AI-created row is forced to (G-08): draft, or proposed for a decision. Null when the entity has none.
    public string? DraftMeaning { get; }

    // The link category a link kind on this entity must have (G-11). Null when the entity holds no link kind.
    public LinkCategory? LinkCategory { get; }

    // Columns the engine computes (G-09). A caller's value is removed and reported; the runtime writes the engine's.
    public IReadOnlyList<string> EngineComputed { get; }

    // Columns only an engine effect writes, with the nested effects allowed to write them (null: any engine-nested
    // write). A caller's value is refused.
    public IReadOnlyDictionary<string, EffectKind[]?> EffectOnly { get; }

    // When set, rows are created and changed only by an engine-nested write of these effects (an empty list: any).
    public EffectKind[]? EffectOnlyRows { get; }

    // The entity in the logical model: its history class and its columns.
    public ModelEntity Model { get; }

    // Document 22, section 2.3, from the model.
    public HistoryClass HistoryClass => Model.HistoryClass;

    // For class F: when a row is final.
    public FinalWhen FinalWhen { get; }

    // For FinalWhen.ColumnSet: the column whose value makes the row final.
    public string? FinalColumn { get; }

    public bool IsConfiguration => Kind != GuardEntityKind.Operational && Kind != GuardEntityKind.AuditEntry;

    // Whether a write's columns are checked against the model's column list. Live configuration rows (C01, C03 to C45)
    // carry the field names of the configuration bundle (document 21, section 8.3), not the model's, and change only
    // inside activation (G-01) from a patch OP-09 validated against the closed bundle schema; every other entity is
    // checked.
    public bool ChecksColumns => Kind != GuardEntityKind.Configuration;
}

internal static class WriteGuardEntities
{
    // Common columns every engine-evaluated write stamps (document 22, section 2.1.1; working default, 28 issue 15).
    public static readonly IReadOnlyList<string> StampColumns = new ReadOnlyCollection<string>(new[]
    {
        "createdByPrincipal", "performedByPrincipal", "performedByActorKind", "onBehalfOf", "clientApp", "isAiCreated",
        "configurationSetVersion", "engineVersion", "engineCheckedOn",
    });

    // A common column the adapter fills (verified only when it came from a validated token claim, document 22,
    // section 2.4). The guard cannot know, so a caller's value is removed.
    public const string ClientAppVerified = "clientAppVerified";

    // Common columns the platform fills (document 22, section 2.1.1: Id, and Created On, Created By, Modified On and
    // Modified By, each "Platform-supplied"). No writer sets them, the engine included, so a value given is dropped.
    // Created On starts the link undo window, so a caller's value must never reach it.
    public static readonly IReadOnlyList<string> PlatformColumns = new ReadOnlyCollection<string>(new[]
    {
        "id", "createdOn", "createdBy", "modifiedOn", "modifiedBy",
    });

    // O10 columns only the approval runner writes (G-06; document 26, section 6.2; model rule of O10).
    public static readonly IReadOnlyList<string> ApprovalOutcomeColumns = new ReadOnlyCollection<string>(new[]
    {
        "outcome", "approver", "approverActorKind", "approverPrincipalKind", "typedReason", "respondedOn", "channel", "flowRunUrl",
    });

    // O10 Approver Holds Required Role: the approval runner's record of whether the responder held the required role when
    // answering (document 25, section 5.5: "from the approval runner's record"; a proposed column, model status proposal).
    // Only the approval runner writes it, with its answer; the engine never does.
    public const string ApproverHoldsRequiredRole = "approverHoldsRequiredRole";

    // O02 columns that change only as an effect of an accepted decision (G-13).
    public static readonly IReadOnlyList<string> RegisterColumns = new ReadOnlyCollection<string>(new[]
    {
        "tier", "tierDecision", "lifecycleStage", "stageDecision", "stageSince",
    });

    // C46 columns only the engine writes (document 21, section 3.3; document 26, section 6.2). The caller writes only
    // the patch and the rationale.
    public static readonly IReadOnlyList<string> DraftEngineColumns = new ReadOnlyCollection<string>(new[]
    {
        "recordNumber", "configurationSet", "draftStatus", "baseVersionNumber", "previewContentJson", "validationJson", "changeSummary",
        "appliedPresetsJson", "instructedBy", "changeDecision", "submittedOn", "expiresOn", "resultingVersion", "discardedOn", "discardReason",
    });

    // C46 outcome columns an engine effect may still write on a submitted draft (G-16: "only the engine writes its
    // outcome columns").
    public static readonly IReadOnlyList<string> DraftOutcomeColumns = new ReadOnlyCollection<string>(new[]
    {
        "draftStatus", "changeDecision", "submittedOn", "resultingVersion", "discardedOn", "discardReason",
    });

    // O05 columns only the engine writes; the platform admin's erasure is the one request that sets isErased.
    public static readonly IReadOnlyList<string> PrincipalIdentityColumns = new ReadOnlyCollection<string>(new[]
    {
        "principalKind", "entraObjectId", "isErased",
    });

    private static readonly EffectKind[] UpdateSubject = { EffectKind.UpdateSubject };

    private static readonly Dictionary<string, GuardEntity> ByKey = Build().ToDictionary(e => e.Key, StringComparer.Ordinal);

    public static bool TryGet(string key, [NotNullWhen(true)] out GuardEntity? entity)
    {
        return ByKey.TryGetValue(key, out entity);
    }

    public static IEnumerable<GuardEntity> All => ByKey.Values.OrderBy(e => e.Id, StringComparer.Ordinal);

    private static IEnumerable<GuardEntity> Build()
    {
        // ---- Configuration (C01 to C46) ----
        string[] configuration =
        {
            "configuration-set:C01", "preset:C03", "setting:C04", "status:C05", "value-list:C06", "list-value:C07", "tier:C08",
            "tier-question:C09", "tier-answer-option:C10", "vocabulary-mapping:C11", "lifecycle-stage:C12", "gate:C13",
            "gate-requirement:C14", "application-kind:C15", "component-kind:C16", "dependency-type:C17", "decision-type:C18",
            "decision-outcome:C19", "change-type:C20", "standard-change-model:C21", "document-type:C22",
            "documentation-requirement:C23", "review-type:C24", "review-cadence:C25", "reminder-rule:C26", "party:C27",
            "owner-role:C28", "role:C29", "role-group-binding:C30", "decision-right:C31", "approval-rule:C32", "gate-policy:C33",
            "target-stage:C34", "change-path:C35", "deployment-route:C36", "link-kind:C37", "actor-kind:C38", "guideline:C39",
            "separation-rule:C40", "tool-policy:C41", "workload-grant:C42", "column-protection-rule:C43", "portfolio-access:C44",
            "known-client:C45",
        };
        foreach (string pair in configuration)
        {
            string[] parts = pair.Split(':');
            yield return new GuardEntity(parts[0], parts[1], GuardEntityKind.Configuration);
        }

        yield return new GuardEntity("configuration-set-version", "C02", GuardEntityKind.ConfigurationSetVersion);
        yield return new GuardEntity(
            "configuration-draft",
            "C46",
            GuardEntityKind.ConfigurationDraft,
            effectOnly: DraftEngineColumns.ToDictionary(c => c, c => (EffectKind[]?)null),
            finalWhen: FinalWhen.DraftStatus);

        // ---- Operational (O01 to O24) ----
        yield return new GuardEntity("portfolio", "O01", GuardEntityKind.Operational);
        yield return new GuardEntity(
            "business-application",
            "O02",
            GuardEntityKind.Operational,
            GuardDeletePolicy.DraftOnly,
            statusColumn: "status",
            draftMeaning: "draft",
            effectOnly: new Dictionary<string, EffectKind[]?> { { "agreedAutonomyLevel", UpdateSubject }, { "autonomyDecision", UpdateSubject } });
        yield return new GuardEntity("technical-component", "O03", GuardEntityKind.Operational, linkCategory: Contract.LinkCategory.MicrosoftResource);
        yield return new GuardEntity("dependency", "O04", GuardEntityKind.Operational);
        yield return new GuardEntity(
            "principal-reference",
            "O05",
            GuardEntityKind.Operational,
            effectOnly: PrincipalIdentityColumns.ToDictionary(c => c, c => (EffectKind[]?)null),
            effectOnlyRows: Array.Empty<EffectKind>());
        yield return new GuardEntity("owner-assignment", "O06", GuardEntityKind.Operational, effectOnlyRows: UpdateSubject);
        yield return new GuardEntity("intake-request", "O07", GuardEntityKind.Operational, GuardDeletePolicy.DraftOnly, statusColumn: "status", draftMeaning: "draft");

        // Class F: frozen when Tier Decision is set (model note of O08).
        yield return new GuardEntity("tier-assessment", "O08", GuardEntityKind.Operational, engineComputed: new[] { "computedTier" }, finalWhen: FinalWhen.ColumnSet, finalColumn: "tierDecision");
        yield return new GuardEntity(
            "decision",
            "O09",
            GuardEntityKind.Operational,
            statusColumn: "status",
            draftMeaning: "proposed",
            effectOnly: new Dictionary<string, EffectKind[]?>
            {
                { "decidedOn", new[] { EffectKind.AcceptDecision, EffectKind.RecomputeApprovals } },
                { "supersededBy", new[] { EffectKind.SupersedeDecision } },
            },
            finalWhen: FinalWhen.MeaningFinal);
        yield return new GuardEntity(
            "approval-record",
            "O10",
            GuardEntityKind.Operational,
            statusColumn: "outcome",
            engineComputed: new[] { "countsTowardRequirement", "approverActorMeaning" },
            finalWhen: FinalWhen.MeaningFinal);
        yield return new GuardEntity("change-request", "O11", GuardEntityKind.Operational, GuardDeletePolicy.DraftOnly, statusColumn: "status", draftMeaning: "draft");
        yield return new GuardEntity("affected-application", "O12", GuardEntityKind.Operational);

        // Class F with fill-once columns: final at insert, apart from the columns the model marks fill-once.
        yield return new GuardEntity("artifact-version", "O13", GuardEntityKind.Operational);

        // Class F, final at insert: a wrong record is never edited, a correcting record is added (document 22, section 8.3).
        yield return new GuardEntity(
            "deployment-record",
            "O14",
            GuardEntityKind.Operational,
            statusColumn: "result",
            engineComputed: new[] { "gateLevelApplied", "isOutsideWindow", "isOtherPath", "isMissingApproval" });
        yield return new GuardEntity("deployment-target", "O15", GuardEntityKind.Operational, linkCategory: Contract.LinkCategory.MicrosoftResource);
        yield return new GuardEntity("pipeline-reference", "O16", GuardEntityKind.Operational, linkCategory: Contract.LinkCategory.Pipeline);
        yield return new GuardEntity("pipeline-map-entry", "O17", GuardEntityKind.Operational);
        yield return new GuardEntity("delivery-link", "O18", GuardEntityKind.Operational, GuardDeletePolicy.CreatorUndo, linkCategory: Contract.LinkCategory.Delivery);
        yield return new GuardEntity(
            "document-link",
            "O19",
            GuardEntityKind.Operational,
            GuardDeletePolicy.CreatorUndo,
            statusColumn: "status",
            draftMeaning: "draft",
            linkCategory: Contract.LinkCategory.Document,
            engineComputed: new[] { "staleOn" });

        // Class A in the model: inserted once, never updated, never deleted.
        yield return new GuardEntity("evidence-link", "O20", GuardEntityKind.Operational, linkCategory: Contract.LinkCategory.Evidence);
        yield return new GuardEntity("review", "O21", GuardEntityKind.Operational, statusColumn: "status");

        // Class F: frozen when Finished On is set (model note of O22).
        yield return new GuardEntity("gap-check-run", "O22", GuardEntityKind.Operational, finalWhen: FinalWhen.ColumnSet, finalColumn: "finishedOn");
        yield return new GuardEntity("claim", "O23", GuardEntityKind.Operational, statusColumn: "status");
        yield return new GuardEntity("audit-entry", "O24", GuardEntityKind.AuditEntry);
    }
}
