using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Product.Engine.Configuration.Composition;
using Product.Engine.Configuration.Drafts;
using Product.Engine.Configuration.Json;
using Product.Engine.Configuration.Model;
using Product.Engine.Configuration.Patches;
using Product.Engine.Configuration.Presets;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Configuration.Validation;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Operations;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Activation;

// What to activate: the accepted decision, its submitted draft, and the versions it started from and replaces
// (document 25, section 6.4, step 5; document 21, sections 4.4 and 4.5). The runtime fetches every value.
public sealed class ActivationRequest
{
    public ActivationRequest(
        string configurationDraftId,
        DraftStatusMeaning draftStatus,
        string changeDecisionId,
        DecisionStatusMeaning changeDecisionStatus,
        ContentSource baseVersion,
        int baseVersionNumber,
        ContentSource activeVersion,
        int activeVersionNumber,
        ConfigurationPatch draft,
        int? resultingVersionNumber = null,
        IEnumerable<RowIdentity>? rowsInUse = null,
        int? approvalRulesVersionNumber = null,
        IEnumerable<KeyValuePair<string, PrincipalKind>>? principalKinds = null,
        int? snapshotCharacterLimit = null,
        string? instructedByPrincipalId = null,
        string? configurationSetKey = null)
    {
        if (string.IsNullOrEmpty(configurationDraftId))
        {
            throw new ArgumentException("Activation names the Configuration Draft.", nameof(configurationDraftId));
        }

        if (string.IsNullOrEmpty(changeDecisionId))
        {
            throw new ArgumentException("Activation names the configuration change decision.", nameof(changeDecisionId));
        }

        ConfigurationDraftId = configurationDraftId;
        DraftStatus = draftStatus;
        ChangeDecisionId = changeDecisionId;
        ChangeDecisionStatus = changeDecisionStatus;
        BaseVersion = baseVersion ?? throw new ArgumentNullException(nameof(baseVersion));
        BaseVersionNumber = baseVersionNumber;
        ActiveVersion = activeVersion ?? throw new ArgumentNullException(nameof(activeVersion));
        ActiveVersionNumber = activeVersionNumber;
        Draft = draft ?? throw new ArgumentNullException(nameof(draft));
        ResultingVersionNumber = resultingVersionNumber;
        RowsInUse = new ReadOnlyCollection<RowIdentity>((rowsInUse ?? Enumerable.Empty<RowIdentity>()).ToList());
        ApprovalRulesVersionNumber = approvalRulesVersionNumber;
        PrincipalKinds = principalKinds?.ToList() ?? new List<KeyValuePair<string, PrincipalKind>>();
        SnapshotCharacterLimit = snapshotCharacterLimit;
        InstructedByPrincipalId = instructedByPrincipalId;
        ConfigurationSetKey = configurationSetKey;
    }

    public string ConfigurationDraftId { get; }

    // C46 Draft Status now. Only a submitted draft activates; an activated one is a replay.
    public DraftStatusMeaning DraftStatus { get; }

    // The C02 version an activated draft created (C46 Resulting Version), for the replay answer.
    public int? ResultingVersionNumber { get; }

    // The configuration change or gate policy change decision (C46 Change Decision).
    public string ChangeDecisionId { get; }

    // Its status meaning now. Activation follows only an accepted decision.
    public DecisionStatusMeaning ChangeDecisionStatus { get; }

    public ContentSource BaseVersion { get; }

    public int BaseVersionNumber { get; }

    public ContentSource ActiveVersion { get; }

    public int ActiveVersionNumber { get; }

    public ConfigurationPatch Draft { get; }

    public IReadOnlyList<RowIdentity> RowsInUse { get; }

    // The version whose rules created the approval records of the decision (V34).
    public int? ApprovalRulesVersionNumber { get; }

    // The principal kinds of the directory objects bindings and grants name (V35).
    public IReadOnlyList<KeyValuePair<string, PrincipalKind>> PrincipalKinds { get; }

    public int? SnapshotCharacterLimit { get; }

    // The admin who instructed the change (C46 Instructed By).
    public string? InstructedByPrincipalId { get; }

    public string? ConfigurationSetKey { get; }
}

// The bootstrap of version 1 (document 21, section 4.8): the person who runs the apply operation with the chosen full
// preset and overlays. The runtime fetches the two platform facts.
public sealed class BootstrapRequest
{
    public BootstrapRequest(
        string presetKey,
        IEnumerable<string>? overlays,
        bool noVersionExists,
        bool hasInstallRights,
        IEnumerable<KeyValuePair<string, PrincipalKind>>? principalKinds = null,
        int? snapshotCharacterLimit = null,
        string? configurationSetKey = null)
    {
        if (!ConfigurationKey.IsValid(presetKey))
        {
            throw new ArgumentException("A preset key uses the configuration key format.", nameof(presetKey));
        }

        PresetKey = presetKey;
        Overlays = new ReadOnlyCollection<string>((overlays ?? Enumerable.Empty<string>()).ToList());
        NoVersionExists = noVersionExists;
        HasInstallRights = hasInstallRights;
        PrincipalKinds = principalKinds?.ToList() ?? new List<KeyValuePair<string, PrincipalKind>>();
        SnapshotCharacterLimit = snapshotCharacterLimit;
        ConfigurationSetKey = configurationSetKey;
    }

    public string PresetKey { get; }

    public IReadOnlyList<string> Overlays { get; }

    // True when the working set has no Configuration Set Version yet (C01 has no current version number).
    public bool NoVersionExists { get; }

    // A proposal: the caller holds the platform's install rights (the environment's system administrator role on
    // Dataverse; the identity the deployment named on Azure). No product role is bound before version 1 exists, so the
    // engine cannot read one; the adapter states it from the platform.
    public bool HasInstallRights { get; }

    public IReadOnlyList<KeyValuePair<string, PrincipalKind>> PrincipalKinds { get; }

    public int? SnapshotCharacterLimit { get; }

    public string? ConfigurationSetKey { get; }
}

// One live configuration row that activation writes, as an engine-nested write (document 21, section 4.4). A row is
// never deleted: a row the new version no longer holds is retired.
public sealed class LiveRowWrite
{
    internal LiveRowWrite(string entity, string keyPath, string change, JsonTreeNode values)
    {
        Entity = entity;
        KeyPath = keyPath;
        Change = change;
        Values = values;
    }

    public string Entity { get; }

    public string KeyPath { get; }

    // create, update, retire or reactivate.
    public string Change { get; }

    // The row as the new version holds it, without its child rows (each child is a row of its own).
    public JsonTreeNode Values { get; }

    public JsonTreeNode ToJson()
    {
        return JsonTreeNode.CreateObject(
            JsonNodes.Member("entity", Entity),
            JsonNodes.Member("key", KeyPath),
            JsonNodes.Member("change", Change));
    }
}

// The answer of activation or of the bootstrap. Outcome words (a proposal, like rebase's):
// - activated: the version was created; Effects hold every write, in order, for one transaction.
// - bootstrapped: version 1 was created by the bootstrap rule.
// - already-activated: the draft was activated before; nothing changes (document 25, section 8).
// - discarded: the accepted draft cannot activate (a rebase conflict, or it fails OP-09 at activation); the draft is
//   discarded with its reason and no version is created.
// - invalid: the bootstrap's preset selection fails OP-09.
// - refused: this is not an activation, or the bootstrap rule does not apply.
// - invalid-input: the request cannot be judged.
public sealed class ActivationOutcome
{
    public const string Activated = "activated";
    public const string Bootstrapped = "bootstrapped";
    public const string AlreadyActivated = "already-activated";
    public const string Discarded = "discarded";
    public const string Invalid = "invalid";
    public const string Refused = "refused";
    public const string InvalidInput = "invalid-input";

    internal ActivationOutcome(
        string outcome,
        int? versionNumber,
        IReadOnlyList<ValidationIssue> issues,
        IReadOnlyList<Reason> reasons,
        CompiledSnapshot? snapshot = null,
        RebaseOutcome? rebase = null,
        ValidationReport? report = null,
        IReadOnlyList<LiveRowWrite>? rowWrites = null,
        IReadOnlyList<PlannedEffect>? effects = null,
        IReadOnlyList<string>? changeSummary = null,
        string? discardReason = null,
        JsonTreeNode? appliedPresets = null)
    {
        Outcome = outcome;
        VersionNumber = versionNumber;
        Issues = issues;
        Reasons = reasons;
        Snapshot = snapshot;
        Rebase = rebase;
        Report = report;
        RowWrites = rowWrites ?? Array.Empty<LiveRowWrite>();
        Effects = effects ?? Array.Empty<PlannedEffect>();
        ChangeSummary = changeSummary ?? Array.Empty<string>();
        DiscardReason = discardReason;
        AppliedPresets = appliedPresets;
    }

    public string Outcome { get; }

    // The new version's number (activated, bootstrapped), or the version an activated draft created (already-activated).
    public int? VersionNumber { get; }

    // The OP-09 issues of the revalidation, and V42 for a conflict.
    public IReadOnlyList<ValidationIssue> Issues { get; }

    // Every reason: the issues, plus the activation's own.
    public IReadOnlyList<Reason> Reasons { get; }

    // The new version: C02 Content Json, Content Hash and Version Number.
    public CompiledSnapshot? Snapshot { get; }

    public RebaseOutcome? Rebase { get; }

    public ValidationReport? Report { get; }

    public IReadOnlyList<LiveRowWrite> RowWrites { get; }

    // The writes of one transaction, in order: live rows, the C02 version, the C01 current version number, the C46 draft
    // and the Audit Entry. Each goes through OP-08 as an engine-nested write of its effect kind.
    public IReadOnlyList<PlannedEffect> Effects { get; }

    // C02 Change Summary: the engine's diff, never the AI's text.
    public IReadOnlyList<string> ChangeSummary { get; }

    // rebase conflict or invalid at activation, for a discarded draft (C46 Discard Reason).
    public string? DiscardReason { get; }

    public JsonTreeNode? AppliedPresets { get; }

    public JsonTreeNode ToJson()
    {
        var members = new List<KeyValuePair<string, JsonTreeNode>>();
        if (VersionNumber.HasValue)
        {
            members.Add(JsonNodes.Member("versionNumber", JsonTreeNode.FromInt64(VersionNumber.Value)));
        }

        if (Snapshot != null)
        {
            members.Add(JsonNodes.Member("contentHash", Snapshot.ContentHash));
        }

        if (Rebase != null)
        {
            members.Add(JsonNodes.Member("baseVersionNumber", JsonTreeNode.FromInt64(Rebase.BaseVersionNumber)));
            members.Add(JsonNodes.Member("activeVersionNumber", JsonTreeNode.FromInt64(Rebase.ActiveVersionNumber)));
            members.Add(JsonNodes.Member("rebaseOutcome", Rebase.Outcome));
            members.Add(JsonNodes.Member("isRebased", JsonTreeNode.FromBoolean(Rebase.Outcome == RebaseOutcome.Rebased)));
            members.Add(JsonNodes.Member("diffChanged", JsonTreeNode.FromBoolean(Rebase.DiffChanged)));
            members.Add(JsonNodes.Member("overlaps", JsonTreeNode.CreateArray(Rebase.Overlaps.Select(o => o.ToJson()))));
        }

        if (AppliedPresets != null)
        {
            members.Add(JsonNodes.Member("appliedPresets", AppliedPresets));
        }

        if (DiscardReason != null)
        {
            members.Add(JsonNodes.Member("discardReason", DiscardReason));
        }

        members.Add(JsonNodes.Member("changeSummary", JsonTreeNode.CreateArray(ChangeSummary.Select(JsonTreeNode.FromString))));
        members.Add(JsonNodes.Member("rowWriteCount", JsonTreeNode.FromInt64(RowWrites.Count)));
        members.Add(JsonNodes.Member("rowWrites", JsonTreeNode.CreateArray(RowWrites.Select(w => w.ToJson()))));
        members.Add(JsonNodes.Member("effects", JsonTreeNode.CreateArray(Effects.Where(e => e.Kind != EffectKind.ActivateConfiguration || !IsLiveRow(e)).Select(EffectSummary))));
        if (Report != null)
        {
            members.Add(JsonNodes.Member("errorCount", JsonTreeNode.FromInt64(Report.ErrorCount)));
            members.Add(JsonNodes.Member("warningCount", JsonTreeNode.FromInt64(Report.WarningCount)));
        }

        return JsonTreeNode.CreateObject(members);
    }

    // Live row writes are listed in rowWrites. The other effects are shown with their columns, except the snapshot
    // itself (contentJson), which the compiled snapshot carries.
    private static bool IsLiveRow(PlannedEffect effect)
    {
        return effect.Key != null && effect.Entity != "Configuration Set";
    }

    private static JsonTreeNode EffectSummary(PlannedEffect effect)
    {
        JsonTreeNode columns = JsonTreeNode.CreateObject(effect.Columns.Members.Where(m => m.Key != "contentJson" && m.Key != "validationJson" && m.Key != "changeJson"));
        return new PlannedEffect(effect.Kind, effect.Entity, effect.Operation, effect.RowId, effect.Key, columns).ToJson();
    }
}

// Activation (document 25, section 6.4, step 5) and the bootstrap of version 1 (document 21, section 4.8).
//
// Activation is not callable: it has no custom API, endpoint or tool. It runs only when OP-08 returns an
// activate-configuration effect for an accepted configuration change or gate policy change decision, as an engine-nested
// write. Inside one transaction it rebases the patch if the active version moved (refused on overlap, V42), runs OP-09
// again in activation mode with the validation date (V28: a workload grant never takes effect unchecked), writes the
// changed live rows, creates the C02 row, sets the C01 current version number, marks the draft activated and writes an
// Audit Entry. The core reads no clock, so the context's asOfUtc is the validation date and the published-on date.
//
// WORKING DEFAULT (open design question 8): the bootstrap of version 1 is the one acceptance outside the locked list
// with no card. It is engine contract, not a setting (document 21, section 4.8).
// WORKING DEFAULT (open design question 4): the accepted decision behind an activation came from one card showing
// the engine's diff.
public sealed class ConfigurationActivator
{
    private readonly ConfigurationValidator _validator;
    private readonly DraftRebaser _rebaser;

    public ConfigurationActivator(PresetCatalog catalog)
    {
        _validator = new ConfigurationValidator(catalog ?? throw new ArgumentNullException(nameof(catalog)));
        _rebaser = new DraftRebaser(catalog);
    }

    public ActivationOutcome Activate(EvaluationContext context, ActivationRequest request)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        // Activation runs only as the engine-nested effect of an accepted decision (document 21, safety floor 4).
        if (!context.Actor.IsEngineNested)
        {
            return Stop(ActivationOutcome.Refused, ReasonCodes.ConfigActivationOnly, "Activation is not callable: it runs only as the engine's effect of an accepted configuration change decision.");
        }

        if (request.DraftStatus == DraftStatusMeaning.Activated)
        {
            if (!request.ResultingVersionNumber.HasValue)
            {
                return Stop(ActivationOutcome.InvalidInput, ReasonCodes.InputRequired, "An activated draft names the version it created (resultingVersionNumber).");
            }

            Reason replay = new(ReasonCodes.ActivationAlreadyActivated, ReasonSeverity.Info, "Configuration Draft " + Q(request.ConfigurationDraftId) + " already created version " + request.ResultingVersionNumber.Value + ", so activation changes nothing.", "Configuration Draft", request.ConfigurationDraftId);
            return new ActivationOutcome(ActivationOutcome.AlreadyActivated, request.ResultingVersionNumber, Array.Empty<ValidationIssue>(), new[] { replay });
        }

        if (request.ChangeDecisionStatus != DecisionStatusMeaning.Accepted)
        {
            return Stop(ActivationOutcome.Refused, ReasonCodes.ConfigActivationOnly, "The change decision is " + EngineContract.DecisionStatusMeanings.ToWire(request.ChangeDecisionStatus) + ", and activation follows only an accepted decision.");
        }

        if (request.DraftStatus != DraftStatusMeaning.Submitted)
        {
            return Stop(ActivationOutcome.Refused, ReasonCodes.ConfigActivationOnly, "The Configuration Draft is " + EngineContract.DraftStatusMeanings.ToWire(request.DraftStatus) + ", and only a submitted draft activates.");
        }

        // The validation date is the activation moment, always: V28 judges every workload grant against it.
        var facts = new ValidationFacts(
            validatedOn: context.AsOfUtc,
            approvalRulesVersionNumber: request.ApprovalRulesVersionNumber,
            principalKinds: request.PrincipalKinds,
            snapshotCharacterLimit: request.SnapshotCharacterLimit);
        RebaseOutcome rebase = _rebaser.Rebase(new RebaseRequest(
            request.BaseVersion,
            request.BaseVersionNumber,
            request.ActiveVersion,
            request.ActiveVersionNumber,
            request.Draft,
            facts,
            request.RowsInUse,
            request.ConfigurationSetKey));

        if (rebase.Outcome == RebaseOutcome.InvalidInput)
        {
            ValidationIssue problem = rebase.InputProblem ?? Issue(ReasonCodes.InputMalformed, "The draft cannot be rebased.");
            return new ActivationOutcome(ActivationOutcome.InvalidInput, null, new[] { problem }, new[] { problem.Reason }, rebase: rebase);
        }

        if (!rebase.CanActivate)
        {
            string reason = rebase.Outcome == RebaseOutcome.Conflict ? "rebase conflict" : "invalid at activation";
            return Discard(context, request, rebase, reason);
        }

        ValidationOutcome validation = rebase.Validation!;
        CompiledSnapshot snapshot = validation.Snapshot!;
        ComposedConfiguration active = _validator.Composer.Compose(request.ActiveVersion, ValidationMode.Draft, new ValidationIssues(), request.ConfigurationSetKey);
        List<LiveRowWrite> writes = RowWrites(active.Content, validation.Candidate!.Content);
        int number = request.ActiveVersionNumber + 1;
        string setKey = snapshot.ConfigurationSetKey;

        var effects = new List<PlannedEffect>();
        effects.AddRange(writes.Select(LiveEffect));
        effects.Add(new PlannedEffect(
            EffectKind.ActivateConfiguration,
            "Configuration Set Version",
            WriteOperation.Create,
            null,
            null,
            Columns(
                ("versionNumber", JsonTreeNode.FromInt64(number)),
                ("baseVersionNumber", JsonTreeNode.FromInt64(request.ActiveVersionNumber)),
                ("sourceDraft", JsonTreeNode.FromString(request.ConfigurationDraftId)),
                ("changeJson", rebase.RebasedPatch ?? JsonTreeNode.Null),
                ("contentJson", snapshot.ContentJson),
                ("contentHash", JsonTreeNode.FromString(snapshot.ContentHash)),
                ("snapshotSchemaVersion", JsonTreeNode.FromString(JsonNodes.GetString(snapshot.ContentJson, "schemaVersion") ?? string.Empty)),
                ("validationJson", validation.Report.ToJson()),
                ("changeSummary", JsonTreeNode.FromString(string.Join("\n", rebase.ChangeSummary))),
                ("publishedOn", JsonTreeNode.FromUtcDateTime(context.AsOfUtc)),
                ("instructedBy", Text(request.InstructedByPrincipalId)),
                ("changeDecision", JsonTreeNode.FromString(request.ChangeDecisionId)))));
        effects.Add(new PlannedEffect(EffectKind.ActivateConfiguration, "Configuration Set", WriteOperation.Update, null, setKey, Columns(("currentVersionNumber", JsonTreeNode.FromInt64(number)))));
        effects.Add(new PlannedEffect(
            EffectKind.ActivateConfiguration,
            "Configuration Draft",
            WriteOperation.Update,
            request.ConfigurationDraftId,
            null,
            Columns(("draftStatus", JsonTreeNode.FromString(EngineContract.DraftStatusMeanings.ToWire(DraftStatusMeaning.Activated))), ("resultingVersion", JsonTreeNode.FromInt64(number)))));
        effects.Add(Audit(context, "configuration-activated", "Configuration Set Version", null, Columns(
            ("versionNumber", JsonTreeNode.FromInt64(number)),
            ("contentHash", JsonTreeNode.FromString(snapshot.ContentHash)),
            ("isRebased", JsonTreeNode.FromBoolean(rebase.Outcome == RebaseOutcome.Rebased)),
            ("changeDecision", JsonTreeNode.FromString(request.ChangeDecisionId)))));

        IReadOnlyList<ValidationIssue> issues = rebase.Reasons;
        return new ActivationOutcome(
            ActivationOutcome.Activated,
            number,
            issues,
            issues.Select(i => i.Reason).ToList(),
            snapshot,
            rebase,
            validation.Report,
            writes,
            effects,
            rebase.ChangeSummary);
    }

    public ActivationOutcome Bootstrap(EvaluationContext context, BootstrapRequest request)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (!request.NoVersionExists)
        {
            return Stop(ActivationOutcome.Refused, ReasonCodes.BootstrapVersionExists, "The working set already has a Configuration Set Version, so every change now follows a configuration change decision, not the bootstrap.");
        }

        // The decider is a person signed in as a user: directly, or behind an AI client acting for them (document 21,
        // section 4.8: "that person as decider and the engine or AI client as performer").
        ActorInfo actor = context.Actor;
        string? decider = actor.ActorKind == ActorKindMeaning.Person ? actor.PrincipalId
            : actor.ActorKind == ActorKindMeaning.AiClientForPerson ? actor.OnBehalfOfPrincipalId
            : null;
        if (decider == null || !ContractFacts.CanApprove(actor.PrincipalKind) || actor.IsEngineNested)
        {
            return Stop(ActivationOutcome.Refused, ReasonCodes.BootstrapNotAllowed, "The caller is " + EngineContract.ActorKindMeanings.ToWire(actor.ActorKind) + " (" + EngineContract.PrincipalKinds.ToWire(actor.PrincipalKind) + "); version 1 is accepted only by a person signed in as a user.");
        }

        if (!request.HasInstallRights)
        {
            return Stop(ActivationOutcome.Refused, ReasonCodes.BootstrapNotAllowed, "The caller does not hold the platform's install rights, so it cannot bootstrap version 1.");
        }

        var facts = new ValidationFacts(validatedOn: context.AsOfUtc, principalKinds: request.PrincipalKinds, snapshotCharacterLimit: request.SnapshotCharacterLimit);
        ValidationOutcome validation = _validator.Validate(new ValidationRequest(
            ContentSource.Preset(request.PresetKey, request.Overlays),
            ValidationMode.Activation,
            null,
            null,
            request.ConfigurationSetKey,
            versionNumber: 1,
            facts: facts));
        if (validation.Outcome == "invalid-input")
        {
            ValidationIssue problem = validation.InputProblem ?? Issue(ReasonCodes.InputMalformed, "The preset selection cannot be composed.");
            return new ActivationOutcome(ActivationOutcome.InvalidInput, null, new[] { problem }, new[] { problem.Reason });
        }

        if (!validation.Report.IsValid || validation.Snapshot == null)
        {
            return new ActivationOutcome(ActivationOutcome.Invalid, null, validation.Report.Issues, validation.Report.Reasons.ToList(), report: validation.Report);
        }

        CompiledSnapshot snapshot = validation.Snapshot;
        ComposedConfiguration composed = validation.Candidate!;
        List<LiveRowWrite> writes = RowWrites(ConfigurationContent.Empty, composed.Content);
        JsonTreeNode applied = JsonTreeNode.CreateArray(composed.Presets.Select(p => JsonTreeNode.CreateObject(
            JsonNodes.Member("presetKey", p.Key),
            JsonNodes.Member("contentVersion", p.ContentVersion == null ? JsonTreeNode.Null : JsonTreeNode.FromString(p.ContentVersion)))));
        IReadOnlyList<string> summary = BootstrapSummary(request, composed);
        string acceptedStatus = DecisionStatus(snapshot, "accepted");

        var effects = new List<PlannedEffect>
        {
            new(EffectKind.ActivateConfiguration, "Configuration Set", WriteOperation.Create, null, snapshot.ConfigurationSetKey, Columns(
                ("setKind", JsonTreeNode.FromString(EngineContract.ConfigurationSetKinds.ToWire(ConfigurationSetKind.Working))),
                ("basedOnPresetKey", JsonTreeNode.FromString(request.PresetKey)),
                ("isInForce", JsonTreeNode.True),
                ("currentVersionNumber", JsonTreeNode.FromInt64(1)))),
        };
        effects.AddRange(writes.Select(LiveEffect));
        effects.Add(new PlannedEffect(
            EffectKind.ActivateConfiguration,
            "Configuration Set Version",
            WriteOperation.Create,
            null,
            null,
            Columns(
                ("versionNumber", JsonTreeNode.FromInt64(1)),
                ("contentJson", snapshot.ContentJson),
                ("contentHash", JsonTreeNode.FromString(snapshot.ContentHash)),
                ("snapshotSchemaVersion", JsonTreeNode.FromString(JsonNodes.GetString(snapshot.ContentJson, "schemaVersion") ?? string.Empty)),
                ("validationJson", validation.Report.ToJson()),
                ("changeSummary", JsonTreeNode.FromString(string.Join("\n", summary))),
                ("appliedPresetsJson", applied),
                ("publishedOn", JsonTreeNode.FromUtcDateTime(context.AsOfUtc)),
                ("instructedBy", JsonTreeNode.FromString(decider)))));

        // The configuration change decision, accepted directly by that person (document 21, section 4.8). It passes OP-08
        // as an engine-nested create of the activate-configuration effect with the bootstrap acceptance basis (G-17).
        effects.Add(new PlannedEffect(
            EffectKind.ActivateConfiguration,
            "Decision",
            WriteOperation.Create,
            null,
            null,
            Columns(
                ("decisionType", JsonTreeNode.FromString(EngineKeys.ConfigurationChangeDecisionType)),
                ("status", JsonTreeNode.FromString(acceptedStatus)),
                ("decider", JsonTreeNode.FromString(decider)),
                ("statement", JsonTreeNode.FromString("Decided: version 1 of the working set, from preset " + request.PresetKey + (request.Overlays.Count == 0 ? string.Empty : " with " + string.Join(", ", request.Overlays)) + ".")))));
        effects.Add(Audit(context, "configuration-activated", "Configuration Set Version", null, Columns(
            ("versionNumber", JsonTreeNode.FromInt64(1)),
            ("contentHash", JsonTreeNode.FromString(snapshot.ContentHash)),
            ("bootstrap", JsonTreeNode.True))));

        return new ActivationOutcome(
            ActivationOutcome.Bootstrapped,
            1,
            validation.Report.Issues,
            validation.Report.Reasons.ToList(),
            snapshot,
            null,
            validation.Report,
            writes,
            effects,
            summary,
            appliedPresets: applied);
    }

    // The accepted draft cannot activate. It is discarded with its reason, kept as evidence, and no version is created
    // (document 21, C46 Discard Reason; section 4.5: nothing merges silently).
    private static ActivationOutcome Discard(EvaluationContext context, ActivationRequest request, RebaseOutcome rebase, string reason)
    {
        IReadOnlyList<ValidationIssue> issues = rebase.Reasons;
        var reasons = issues.Select(i => i.Reason).ToList();
        reasons.Add(new Reason(
            ReasonCodes.ActivationDiscarded,
            ReasonSeverity.Warning,
            "Configuration Draft " + Q(request.ConfigurationDraftId) + " was accepted but cannot activate (" + reason + "), so it is discarded and no version is created. Rebuild the change on version " + request.ActiveVersionNumber + ".",
            "Configuration Draft",
            request.ConfigurationDraftId));
        var effects = new List<PlannedEffect>
        {
            new(EffectKind.ActivateConfiguration, "Configuration Draft", WriteOperation.Update, request.ConfigurationDraftId, null, Columns(
                ("draftStatus", JsonTreeNode.FromString(EngineContract.DraftStatusMeanings.ToWire(DraftStatusMeaning.Discarded))),
                ("discardReason", JsonTreeNode.FromString(reason)),
                ("discardedOn", JsonTreeNode.FromUtcDateTime(context.AsOfUtc)))),
            Audit(context, "configuration-activation-refused", "Configuration Draft", request.ConfigurationDraftId, Columns(
                ("discardReason", JsonTreeNode.FromString(reason)),
                ("changeDecision", JsonTreeNode.FromString(request.ChangeDecisionId)))),
        };
        return new ActivationOutcome(ActivationOutcome.Discarded, null, issues, reasons, null, rebase, rebase.Validation?.Report, null, effects, rebase.ChangeSummary, reason);
    }

    // The live row writes that turn one content into the next, by entity and key path (ordinal). A row the new content
    // no longer holds is retired, never deleted (G-04).
    private static List<LiveRowWrite> RowWrites(ConfigurationContent before, ConfigurationContent after)
    {
        var writes = new List<LiveRowWrite>();
        foreach (RowChange change in SnapshotDiff.Changes(before, after))
        {
            switch (change.Kind)
            {
                case RowChangeKind.Added:
                    writes.Add(new LiveRowWrite(change.Entity, change.KeyPath, "create", Values(after, change)));
                    break;
                case RowChangeKind.Changed:
                    writes.Add(new LiveRowWrite(change.Entity, change.KeyPath, "update", Values(after, change)));
                    break;
                case RowChangeKind.Retired:
                    writes.Add(new LiveRowWrite(change.Entity, change.KeyPath, "retire", Values(after, change)));
                    break;
                case RowChangeKind.Reactivated:
                    writes.Add(new LiveRowWrite(change.Entity, change.KeyPath, "reactivate", Values(after, change)));
                    break;
                default:
                    before.TryFindRow(change.Entity, change.KeyPath, out ContentRow? old);
                    JsonTreeNode retired = JsonNodes.With(WithoutChildren(change.Entity, old!.Node), "isActive", JsonTreeNode.False);
                    writes.Add(new LiveRowWrite(change.Entity, change.KeyPath, "retire", retired));
                    break;
            }
        }

        return writes;
    }

    private static JsonTreeNode Values(ConfigurationContent content, RowChange change)
    {
        content.TryFindRow(change.Entity, change.KeyPath, out ContentRow? row);
        return WithoutChildren(change.Entity, row!.Node);
    }

    private static JsonTreeNode WithoutChildren(string entity, JsonTreeNode node)
    {
        if (!ConfigurationEntities.TryGet(entity, out ConfigurationEntity? info) || info.ChildField == null)
        {
            return node;
        }

        return JsonTreeNode.CreateObject(node.Members.Where(m => !string.Equals(m.Key, info.ChildField, StringComparison.Ordinal)));
    }

    private static PlannedEffect LiveEffect(LiveRowWrite write)
    {
        return new PlannedEffect(
            EffectKind.ActivateConfiguration,
            ConfigurationEntities.LogicalNameOf(write.Entity),
            write.Change == "create" ? WriteOperation.Create : WriteOperation.Update,
            null,
            write.KeyPath,
            write.Values);
    }

    // Version 1's change summary: the presets applied and the rows each entity starts with. The diff against nothing
    // would list every row; this keeps the summary readable (a judgment call).
    private static IReadOnlyList<string> BootstrapSummary(BootstrapRequest request, ComposedConfiguration composed)
    {
        var lines = new List<string>
        {
            "Version 1 from preset `" + request.PresetKey + "`" + (request.Overlays.Count == 0 ? string.Empty : " with overlays " + string.Join(", ", request.Overlays.Select(o => "`" + o + "`"))) + ", accepted under the bootstrap rule.",
        };
        foreach (IGrouping<string, ContentRow> group in composed.Content.Rows.GroupBy(r => r.Entity).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            lines.Add("Added " + group.Count() + " " + ConfigurationEntities.LogicalNameOf(group.Key) + " row(s).");
        }

        return lines;
    }

    // The C05 decision status the engine picks for a meaning: the default for it, or the only active one.
    private static string DecisionStatus(CompiledSnapshot snapshot, string meaning)
    {
        var rows = snapshot.Snapshot.TryGetEntity("status", out Core.Snapshots.SnapshotEntity? entity)
            ? entity.Rows.Where(r => r.IsActive && r.EngineMeaning == meaning && r.TryGetColumn("appliesToEntity", out JsonTreeNode? e) && e.Kind == JsonTreeKind.String && e.GetString() == "decision").ToList()
            : new List<Core.Snapshots.SnapshotRow>();
        var defaults = rows.Where(r => r.TryGetColumn("isDefaultForMeaning", out JsonTreeNode? d) && d.Kind == JsonTreeKind.True).ToList();
        return (defaults.Count == 1 ? defaults[0] : rows.Count == 1 ? rows[0] : rows.OrderBy(r => r.KeyPath, StringComparer.Ordinal).First()).KeyPath;
    }

    private static PlannedEffect Audit(EvaluationContext context, string operation, string entityName, string? rowId, JsonTreeNode changedValues)
    {
        ActorInfo actor = context.Actor;
        return new PlannedEffect(
            EffectKind.WriteAuditEntry,
            "Audit Entry",
            WriteOperation.Create,
            null,
            null,
            Columns(
                ("entityName", JsonTreeNode.FromString(entityName)),
                ("rowId", Text(rowId)),
                ("operation", JsonTreeNode.FromString(operation)),
                ("changedValuesJson", changedValues),
                ("actor", JsonTreeNode.FromString(actor.PrincipalId)),
                ("actorKind", JsonTreeNode.FromString(EngineContract.ActorKindMeanings.ToWire(actor.ActorKind))),
                ("onBehalfOf", Text(actor.OnBehalfOfPrincipalId)),
                ("clientApp", Text(context.ClientApp)),
                ("occurredOn", JsonTreeNode.FromUtcDateTime(context.AsOfUtc)),
                ("correlationId", JsonTreeNode.FromString(context.CorrelationId))));
    }

    private static ActivationOutcome Stop(string outcome, string code, string message)
    {
        ValidationIssue issue = Issue(code, message);
        return new ActivationOutcome(outcome, null, new[] { issue }, new[] { issue.Reason });
    }

    private static ValidationIssue Issue(string code, string message)
    {
        return new ValidationIssue(null, code, ReasonCatalog.Get(code).DefaultSeverity, message, null, null, null);
    }

    private static JsonTreeNode Columns(params (string Name, JsonTreeNode Value)[] columns)
    {
        return JsonTreeNode.CreateObject(columns.OrderBy(c => c.Name, StringComparer.Ordinal).Select(c => new KeyValuePair<string, JsonTreeNode>(c.Name, c.Value)));
    }

    private static JsonTreeNode Text(string? value)
    {
        return value == null ? JsonTreeNode.Null : JsonTreeNode.FromString(value);
    }

    private static string Q(string value)
    {
        return "`" + value + "`";
    }
}
