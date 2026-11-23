using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Product.Engine.Configuration.Composition;
using Product.Engine.Configuration.Json;
using Product.Engine.Configuration.Model;
using Product.Engine.Configuration.Patches;
using Product.Engine.Configuration.Presets;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Configuration.Validation;
using Product.Engine.Configuration.Validation.Rules;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Drafts;

// What to rebase: a draft patch, the version it started from, and the version active now (document 21, section 4.5).
public sealed class RebaseRequest
{
    public RebaseRequest(
        ContentSource baseVersion,
        int baseVersionNumber,
        ContentSource activeVersion,
        int activeVersionNumber,
        ConfigurationPatch draft,
        ValidationFacts? facts = null,
        IEnumerable<RowIdentity>? rowsInUse = null,
        string? configurationSetKey = null)
    {
        if (baseVersionNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(baseVersionNumber), "Version numbers start at 1.");
        }

        if (activeVersionNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(activeVersionNumber), "Version numbers start at 1.");
        }

        BaseVersion = baseVersion ?? throw new ArgumentNullException(nameof(baseVersion));
        BaseVersionNumber = baseVersionNumber;
        ActiveVersion = activeVersion ?? throw new ArgumentNullException(nameof(activeVersion));
        ActiveVersionNumber = activeVersionNumber;
        Draft = draft ?? throw new ArgumentNullException(nameof(draft));
        Facts = facts ?? ValidationFacts.None;
        RowsInUse = new ReadOnlyCollection<RowIdentity>((rowsInUse ?? Enumerable.Empty<RowIdentity>()).ToList());
        ConfigurationSetKey = configurationSetKey;
    }

    // The version the draft started from (C46 Base Version Number): a C02 snapshot, or a preset selection in tests.
    public ContentSource BaseVersion { get; }

    public int BaseVersionNumber { get; }

    // The version in force now.
    public ContentSource ActiveVersion { get; }

    public int ActiveVersionNumber { get; }

    // The draft's patch (C46): upsert and retire operations by entity and key.
    public ConfigurationPatch Draft { get; }

    // The facts the revalidation needs: the validation date (V28), the version whose rules created the approvals (V34),
    // principal kinds (V35) and the size limit (V41). The rebase sets the version numbers and the rebased flag itself.
    public ValidationFacts Facts { get; }

    // Rows operational records use (V08, V16).
    public IReadOnlyList<RowIdentity> RowsInUse { get; }

    public string? ConfigurationSetKey { get; }
}

// One draft operation that touches a row that changed between the base version and the active version.
public sealed class RebaseOverlap
{
    internal RebaseOverlap(int operationIndex, ConfigurationOperation operation, RowChange row)
    {
        OperationIndex = operationIndex;
        Operation = operation;
        Row = row;
    }

    // The operation's position in the draft, from 0.
    public int OperationIndex { get; }

    public ConfigurationOperation Operation { get; }

    // The row that changed in between.
    public RowChange Row { get; }

    public JsonTreeNode ToJson()
    {
        return JsonTreeNode.CreateObject(
            JsonNodes.Member("operation", Operation.Pointer),
            JsonNodes.Member("op", EngineContract.PatchOperations.ToWire(Operation.Op)),
            JsonNodes.Member("entity", Operation.Entity),
            JsonNodes.Member("key", Operation.Key),
            JsonNodes.Member("changedRow", Row.ToJson()));
    }
}

// The rebase answer. Outcome words (a proposal; document 21, section 4.5 names the three cases):
// - same-base: the draft starts from the active version. Validated again; activation may go ahead.
// - rebased: the base moved, no operation overlaps, the operations were re-applied to the active version and the
//   result is valid. The admin sees the updated diff before activation.
// - invalid: no overlap, but the result (same base or rebased) fails OP-09, so it cannot activate.
// - conflict: an operation touches a row that changed in between. Refused (V42). The AI rebuilds the draft on the
//   active version. Nothing merges silently.
// - invalid-input: the request cannot be judged, such as a base version above the active one.
public sealed class RebaseOutcome
{
    internal RebaseOutcome(
        string outcome,
        int baseVersionNumber,
        int activeVersionNumber,
        IReadOnlyList<RowChange> changedRows,
        IReadOnlyList<RebaseOverlap> overlaps,
        JsonTreeNode? rebasedPatch,
        ValidationOutcome? validation,
        IReadOnlyList<string> previousChangeSummary,
        IReadOnlyList<ValidationIssue> reasons,
        ValidationIssue? inputProblem,
        string activeContentHash)
    {
        Outcome = outcome;
        BaseVersionNumber = baseVersionNumber;
        ActiveVersionNumber = activeVersionNumber;
        ChangedRows = changedRows;
        Overlaps = overlaps;
        RebasedPatch = rebasedPatch;
        Validation = validation;
        PreviousChangeSummary = previousChangeSummary;
        Reasons = reasons;
        InputProblem = inputProblem;
        ActiveContentHash = activeContentHash;
    }

    public const string SameBase = "same-base";
    public const string Rebased = "rebased";
    public const string Invalid = "invalid";
    public const string Conflict = "conflict";
    public const string InvalidInput = "invalid-input";

    public string Outcome { get; }

    // True for same-base and rebased: activation may write the version.
    public bool CanActivate => Outcome == SameBase || Outcome == Rebased;

    public int BaseVersionNumber { get; }

    public int ActiveVersionNumber { get; }

    // Every row that differs between the base version and the active version.
    public IReadOnlyList<RowChange> ChangedRows { get; }

    // The operations that touch a changed row. Not empty only for conflict.
    public IReadOnlyList<RebaseOverlap> Overlaps { get; }

    // The patch as it will be applied (C02 Change Json): the same operations, with the active version as the base.
    // Null for conflict and invalid-input.
    public JsonTreeNode? RebasedPatch { get; }

    // OP-09 over the active version with the draft applied, in activation mode. Null for conflict and invalid-input.
    public ValidationOutcome? Validation { get; }

    // The diff the admin saw on the draft: the base version against the base version with the draft applied.
    public IReadOnlyList<string> PreviousChangeSummary { get; }

    // The diff the admin sees now: the active version against the active version with the draft applied.
    public IReadOnlyList<string> ChangeSummary => Validation?.Report.ChangeSummary ?? Array.Empty<string>();

    // True when the updated diff differs from the one the admin saw.
    public bool DiffChanged => Validation != null && !PreviousChangeSummary.SequenceEqual(ChangeSummary, StringComparer.Ordinal);

    // The reasons: one V42 issue per overlap for conflict, else the revalidation's issues.
    public IReadOnlyList<ValidationIssue> Reasons { get; }

    public ValidationIssue? InputProblem { get; }

    // The content hash of the active version as composed, for the evaluation context.
    public string ActiveContentHash { get; }

    // The payload record.
    public JsonTreeNode ToJson()
    {
        var members = new List<KeyValuePair<string, JsonTreeNode>>
        {
            JsonNodes.Member("baseVersionNumber", JsonTreeNode.FromInt64(BaseVersionNumber)),
            JsonNodes.Member("activeVersionNumber", JsonTreeNode.FromInt64(ActiveVersionNumber)),
            JsonNodes.Member("canActivate", JsonTreeNode.FromBoolean(CanActivate)),
            JsonNodes.Member("changedRows", JsonTreeNode.CreateArray(ChangedRows.Select(r => r.ToJson()))),
            JsonNodes.Member("overlaps", JsonTreeNode.CreateArray(Overlaps.Select(o => o.ToJson()))),
        };
        if (RebasedPatch != null)
        {
            members.Add(JsonNodes.Member("patch", RebasedPatch));
        }

        if (Validation != null)
        {
            members.Add(JsonNodes.Member("previousChangeSummary", JsonTreeNode.CreateArray(PreviousChangeSummary.Select(JsonTreeNode.FromString))));
            members.Add(JsonNodes.Member("diffChanged", JsonTreeNode.FromBoolean(DiffChanged)));
            members.Add(JsonNodes.Member("validation", Validation.Report.ToJson()));
        }

        return JsonTreeNode.CreateObject(members);
    }
}

// Rebase (document 21, section 4.5; document 25, section 6.4, step 5). It runs inside activation:
// 1. Same base: the draft is validated again on the active version and may activate.
// 2. Different base, no overlap: the draft's operations are re-applied to the active version, the result is validated
//    again (activation mode, rebased), and the updated diff is returned for the admin to see before activation.
// 3. Different base, with overlap: refused with V42. Overlap means an operation on an entity and key that changed
//    between the two versions.
//
// What one operation touches (its footprint), so that nothing merges silently:
// - its own entity and key;
// - the rows a wildcard row of that key expanded into (key "<key>--...");
// - for a child operation ("parent/child"), its parent row too, because a child lives inside its parent row;
// - for an upsert of a parent row, the child rows its values name; for a retire, every child row, because a child
//   of a retired parent counts as retired.
// The extra rows are a judgment call beyond the literal rule; each makes the rebase refuse more, never merge more.
public sealed class DraftRebaser
{
    private readonly ConfigurationValidator _validator;

    public DraftRebaser(PresetCatalog catalog)
    {
        _validator = new ConfigurationValidator(catalog ?? throw new ArgumentNullException(nameof(catalog)));
    }

    public RebaseOutcome Rebase(RebaseRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        int baseNumber = request.BaseVersionNumber;
        int activeNumber = request.ActiveVersionNumber;
        ConfigurationPatch draft = request.Draft;

        if (draft.Kind != PatchKind.Draft && draft.Kind != PatchKind.ClientBinding)
        {
            return InputFailure(request, ReasonCodes.InputMalformed, "Only a draft patch or a client binding patch can be rebased, not a preset patch.");
        }

        int? named = ManifestBase(draft);
        if (named.HasValue && named.Value != baseNumber)
        {
            return InputFailure(request, ReasonCodes.InputMalformed, "The draft names base version " + named.Value + ", but the base snapshot is version " + baseNumber + ". Give the snapshot the draft started from.");
        }

        if (baseNumber > activeNumber)
        {
            return InputFailure(request, ReasonCodes.InputMalformed, "The draft starts from version " + baseNumber + ", which is above the active version " + activeNumber + ". A draft can only start from a version that was active.");
        }

        ConfigurationComposer composer = _validator.Composer;
        ComposedConfiguration baseVersion;
        ComposedConfiguration activeVersion;
        try
        {
            baseVersion = composer.Compose(request.BaseVersion, ValidationMode.Draft, new ValidationIssues(), request.ConfigurationSetKey);
            activeVersion = composer.Compose(request.ActiveVersion, ValidationMode.Draft, new ValidationIssues(), request.ConfigurationSetKey);
        }
        catch (ConfigurationInputException e)
        {
            return InputFailure(request, e.Code, e.Message);
        }

        if (!baseVersion.IsComplete || !activeVersion.IsComplete)
        {
            return InputFailure(request, ReasonCodes.InputMalformed, "The base version or the active version could not be composed.");
        }

        string activeHash = CanonicalHash.ComputeContentHash(activeVersion.Bundle);
        IReadOnlyList<RowChange> changed = SnapshotDiff.Changes(baseVersion.Content, activeVersion.Content);
        bool moved = baseNumber != activeNumber;
        if (!moved && changed.Count > 0)
        {
            return InputFailure(request, ReasonCodes.InputMalformed, "The base snapshot and the active snapshot are both version " + baseNumber + ", but their content differs in " + changed.Count + " row(s). One version has one content.");
        }

        List<RebaseOverlap> overlaps = moved ? FindOverlaps(draft, changed) : new List<RebaseOverlap>();
        if (overlaps.Count > 0)
        {
            var refusals = new ValidationIssues();
            foreach (RebaseOverlap overlap in overlaps)
            {
                refusals.Add(V42BaseMoved.Instance.Overlap(
                    "the draft starts from version " + baseNumber + ", and operation " + overlap.Operation.Pointer + " (" + EngineContract.PatchOperations.ToWire(overlap.Operation.Op)
                    + " " + ConfigurationEntities.LogicalNameOf(overlap.Operation.Entity) + " " + Q(overlap.Operation.Key) + ") touches "
                    + ConfigurationEntities.LogicalNameOf(overlap.Row.Entity) + " " + Q(overlap.Row.KeyPath) + ", which was " + overlap.Row.KindWire + " between version " + baseNumber
                    + " and version " + activeNumber + ". Nothing merges silently: rebuild the draft on version " + activeNumber + ".",
                    overlap.Row.Entity,
                    overlap.Row.KeyPath,
                    draft.Where + " " + overlap.Operation.Pointer));
            }

            return new RebaseOutcome(
                RebaseOutcome.Conflict,
                baseNumber,
                activeNumber,
                changed,
                overlaps,
                null,
                null,
                Array.Empty<string>(),
                refusals.Items.ToList(),
                null,
                activeHash);
        }

        // Re-apply the same operations to the active version. The patch as applied names the active version as its base.
        JsonTreeNode rebasedDocument = RebasedDocument(draft, activeNumber);
        ConfigurationPatch rebased = moved
            ? ConfigurationPatch.FromDocument(rebasedDocument, draft.Kind, draft.Where + " (rebased on version " + activeNumber + ")")
            : draft;

        // The diff the admin saw on the draft. With the same base it is the diff validation computes below.
        IReadOnlyList<string>? previous = null;
        if (moved)
        {
            ComposedConfiguration preview = composer.Compose(request.BaseVersion.WithPatch(draft), ValidationMode.Draft, new ValidationIssues(), request.ConfigurationSetKey);
            previous = preview.IsComplete ? SnapshotDiff.Summarize(baseVersion.Content, preview.Content) : Array.Empty<string>();
        }

        ValidationFacts given = request.Facts;
        var facts = new ValidationFacts(
            validatedOn: given.ValidatedOn,
            baseVersionNumber: activeNumber,
            activeVersionNumber: activeNumber,
            isRebased: moved,
            approvalRulesVersionNumber: given.ApprovalRulesVersionNumber,
            principalKinds: given.PrincipalKinds,
            snapshotCharacterLimit: given.SnapshotCharacterLimit);
        var validationRequest = new ValidationRequest(
            request.ActiveVersion.WithPatch(rebased),
            ValidationMode.Activation,
            request.ActiveVersion,
            request.RowsInUse,
            request.ConfigurationSetKey,
            versionNumber: activeNumber + 1,
            facts: facts);
        ValidationOutcome validation = _validator.Validate(validationRequest);

        string word = validation.Outcome == "valid"
            ? (moved ? RebaseOutcome.Rebased : RebaseOutcome.SameBase)
            : validation.Outcome == "invalid-input" ? RebaseOutcome.InvalidInput : RebaseOutcome.Invalid;
        return new RebaseOutcome(
            word,
            baseNumber,
            activeNumber,
            changed,
            overlaps,
            rebasedDocument,
            validation,
            previous ?? validation.Report.ChangeSummary,
            validation.Report.Issues,
            validation.InputProblem,
            activeHash);
    }

    // Every (operation, changed row) pair where the operation's footprint holds the row, by operation order, then row.
    private static List<RebaseOverlap> FindOverlaps(ConfigurationPatch draft, IReadOnlyList<RowChange> changed)
    {
        var overlaps = new List<RebaseOverlap>();
        for (int i = 0; i < draft.Operations.Count; i++)
        {
            ConfigurationOperation op = draft.Operations[i];
            foreach (RowChange row in changed)
            {
                if (Touches(op, row))
                {
                    overlaps.Add(new RebaseOverlap(i, op, row));
                }
            }
        }

        return overlaps;
    }

    private static bool Touches(ConfigurationOperation op, RowChange row)
    {
        if (!ConfigurationEntities.TryGet(op.Entity, out ConfigurationEntity? entity))
        {
            // V01 reports the unknown entity. Compare literally.
            return Same(op.Entity, row.Entity) && Same(op.Key, row.KeyPath);
        }

        if (entity.IsChild)
        {
            int slash = op.Key.IndexOf('/');
            string parentKey = slash < 0 ? op.Key : op.Key.Substring(0, slash);
            return (Same(row.Entity, op.Entity) && Same(row.KeyPath, op.Key))
                || (Same(row.Entity, entity.ParentKey) && Same(row.KeyPath, parentKey));
        }

        if (Same(row.Entity, op.Entity))
        {
            // The row itself, or a row a wildcard row with this key expanded into (document 21, section 8.4).
            return Same(row.KeyPath, op.Key) || row.KeyPath.StartsWith(op.Key + "--", StringComparison.Ordinal);
        }

        if (entity.ChildKey == null || !Same(row.Entity, entity.ChildKey) || !row.KeyPath.StartsWith(op.Key + "/", StringComparison.Ordinal))
        {
            return false;
        }

        if (op.Op == PatchOperation.Retire)
        {
            return true;
        }

        string childKey = row.KeyPath.Substring(op.Key.Length + 1);
        JsonTreeNode? children = op.Values == null ? null : JsonNodes.Get(op.Values, entity.ChildField!);
        return children != null
            && children.Kind == JsonTreeKind.Array
            && children.Items.Any(c => c.Kind == JsonTreeKind.Object && Same(JsonNodes.GetString(c, "key"), childKey));
    }

    // The draft document with the active version as its base. A bare list of operations gets a patch document around it.
    private static JsonTreeNode RebasedDocument(ConfigurationPatch draft, int activeNumber)
    {
        JsonTreeNode? document = draft.Document;
        if (draft.IsInline || document == null || document.Kind != JsonTreeKind.Object)
        {
            return JsonTreeNode.CreateObject(
                JsonNodes.Member("schemaVersion", "1.0"),
                JsonNodes.Member("manifest", JsonTreeNode.CreateObject(
                    JsonNodes.Member("kind", "patch"),
                    JsonNodes.Member("baseVersionNumber", JsonTreeNode.FromInt64(activeNumber)))),
                JsonNodes.Member("operations", document != null && document.Kind == JsonTreeKind.Array ? document : JsonNodes.EmptyArray));
        }

        JsonTreeNode manifest = JsonNodes.Get(document, "manifest") ?? JsonTreeNode.CreateObject(JsonNodes.Member("kind", "patch"));
        manifest = JsonNodes.With(manifest, "baseVersionNumber", JsonTreeNode.FromInt64(activeNumber));
        return JsonNodes.With(document, "manifest", manifest);
    }

    private static int? ManifestBase(ConfigurationPatch draft)
    {
        if (draft.IsInline || draft.Document == null || draft.Document.Kind != JsonTreeKind.Object)
        {
            return null;
        }

        JsonTreeNode? manifest = JsonNodes.Get(draft.Document, "manifest");
        JsonTreeNode? number = manifest == null ? null : JsonNodes.Get(manifest, "baseVersionNumber");
        return number != null && number.TryGetInt64(out long value) && value >= 1 && value <= int.MaxValue ? (int)value : null;
    }

    private static RebaseOutcome InputFailure(RebaseRequest request, string code, string message)
    {
        ReasonCodeInfo info = ReasonCatalog.Get(code);
        var problem = new ValidationIssue(null, code, info.DefaultSeverity, message, null, null, null);
        return new RebaseOutcome(
            RebaseOutcome.InvalidInput,
            request.BaseVersionNumber,
            request.ActiveVersionNumber,
            Array.Empty<RowChange>(),
            Array.Empty<RebaseOverlap>(),
            null,
            null,
            Array.Empty<string>(),
            Array.Empty<ValidationIssue>(),
            problem,
            CanonicalHash.ComputeContentHash(JsonTreeNode.Null));
    }

    private static bool Same(string? a, string? b)
    {
        return string.Equals(a, b, StringComparison.Ordinal);
    }

    private static string Q(string value)
    {
        return "`" + value + "`";
    }
}
