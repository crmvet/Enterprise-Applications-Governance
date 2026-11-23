using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Product.Engine.Configuration.Composition;
using Product.Engine.Configuration.Json;
using Product.Engine.Configuration.Model;
using Product.Engine.Configuration.Patches;
using Product.Engine.Configuration.Presets;
using Product.Engine.Configuration.Schema;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Configuration.Validation.Checks;
using Product.Engine.Configuration.Validation.Rules;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation;

// What to check, and against what (document 25, section 5.11).
public sealed class ValidationRequest
{
    public ValidationRequest(
        ContentSource candidate,
        ValidationMode mode,
        ContentSource? active = null,
        IEnumerable<RowIdentity>? rowsInUse = null,
        string? configurationSetKey = null,
        int versionNumber = 1,
        ValidationFacts? facts = null)
    {
        if (versionNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(versionNumber), "Version numbers start at 1.");
        }

        Candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
        Mode = mode;
        Active = active;
        RowsInUse = new ReadOnlyCollection<RowIdentity>((rowsInUse ?? Enumerable.Empty<RowIdentity>()).ToList());
        ConfigurationSetKey = configurationSetKey;
        VersionNumber = versionNumber;
        Facts = facts ?? ValidationFacts.None;
    }

    // The configuration to check: a preset selection, a bundle or a raw document, plus patches.
    public ContentSource Candidate { get; }

    public ValidationMode Mode { get; }

    // The active version, for the diff and V08. When it is null and the candidate has patches,
    // the candidate without its patches stands in for the active version.
    public ContentSource? Active { get; }

    // Rows operational records use (V08).
    public IReadOnlyList<RowIdentity> RowsInUse { get; }

    // The working set's key for the snapshot. Null keeps the key a bundle's manifest gives, or "working-set".
    public string? ConfigurationSetKey { get; }

    // The version number the compiled snapshot carries.
    public int VersionNumber { get; }

    // Facts the configuration does not hold: the validation date, version numbers, principal kinds, the size limit.
    public ValidationFacts Facts { get; }
}

// The OP-09 answer: the outcome word, the report, the composed bundle and, for a valid working set, the compiled snapshot.
public sealed class ValidationOutcome
{
    internal ValidationOutcome(string outcome, ValidationReport report, ComposedConfiguration? candidate, CompiledSnapshot? snapshot, ValidationIssue? inputProblem)
    {
        Outcome = outcome;
        Report = report;
        Candidate = candidate;
        Snapshot = snapshot;
        InputProblem = inputProblem;
    }

    // "valid", "invalid" or "invalid-input" (OperationCatalog, OP-09; document 25, section 7).
    public string Outcome { get; }

    public ValidationReport Report { get; }

    public ComposedConfiguration? Candidate { get; }

    // The compiled snapshot of a valid working set. Null in preset-build mode and when there are errors.
    public CompiledSnapshot? Snapshot { get; }

    // Why the request could not be composed, for the invalid-input outcome.
    public ValidationIssue? InputProblem { get; }
}

// OP-09 Validate a configuration set (document 25, section 5.11; document 21, section 9).
// It runs every rule of document 21, section 9 (V01 to V44), then the engine checks beyond V44 that document 25,
// section 5.11 lists. snapshot.schema-unsupported, the fifth engine check, is the snapshot compiler's: a bundle with
// another schema major version fails V01 here, and the compiler refuses it when it loads a snapshot.
public sealed class ConfigurationValidator
{
    // The rules that check the composed content, in the order they run.
    // V01 runs on every source document and on the composed bundle. V04, V05 and V09's overlap check run while
    // patches apply and wildcards expand. V43 runs on the preset selection. V23 reads V09's findings, so it runs after.
    private static readonly IContentRule[] ContentRules =
    {
        V02KeyFormat.Instance,
        V03KeyDuplicate.Instance,
        V06ReferenceResolves.Instance,
        V07ReferenceActive.Instance,
        V08RetireNotRemove.Instance,
        V09Wildcards.Instance,
        V10ExactlyOne.Instance,
        V11ActiveTier.Instance,
        V12TierRank.Instance,
        V13TierQuestionOptions.Instance,
        V14LifecycleStages.Instance,
        V15StatusMeanings.Instance,
        V16MeaningChangedInUse.Instance,
        V17TaxonomyMinimum.Instance,
        V18DecisionTypeOutcome.Instance,
        V19ApprovalTierCoverage.Instance,
        V20ApprovalCoverage.Instance,
        V21ChangeAuthorizationCoverage.Instance,
        V22DecisionRightAccountable.Instance,
        V23GatePolicyCoverage.Instance,
        V24GatePolicyFailMode.Instance,
        V25GatePolicyEmergencyRule.Instance,
        V26RuleGroupAmbiguous.Instance,
        V27ApprovalRuleGroups.Instance,
        V28ValueRanges.Instance,
        V29CadenceCoverage.Instance,
        V30ApprovalRuleWorkloadApprover.Instance,
        V31DecisionRightAiLetter.Instance,
        V32ConfigurationAdmin.Instance,
        V33LockedSettings.Instance,
        V34ProtectedChange.Instance,
        V35RoleBindingTargets.Instance,
        V36SeparationSharedGroup.Instance,
        V37RoleUnbound.Instance,
        V38Settings.Instance,
        V39JsonFields.Instance,
        V40Patterns.Instance,
        V41SnapshotSize.Instance,
        V42BaseMoved.Instance,
        V44PresetTenantData.Instance,
    };

    // The engine checks beyond V01 to V44 (document 25, section 5.11). They carry a reason code and no V-ID.
    private static readonly EngineCheck[] EngineCheckList =
    {
        RoleConfigAdminCheck.Instance,
        StandardModelTypeCheck.Instance,
        StandardModelConditionKindCheck.Instance,
        VocabularySourceCheck.Instance,
    };

    private readonly ConfigurationComposer _composer;

    public ConfigurationValidator(PresetCatalog catalog)
    {
        _composer = new ConfigurationComposer(catalog);
    }

    public ConfigurationComposer Composer => _composer;

    // Every rule this validator runs, in rule ID order.
    public static IReadOnlyList<ValidationRule> Rules { get; } = new ReadOnlyCollection<ValidationRule>(new ValidationRule[]
    {
        V01SchemaShape.Instance,
        V02KeyFormat.Instance,
        V03KeyDuplicate.Instance,
        V04KeyReused.Instance,
        V05KeyImmutable.Instance,
        V06ReferenceResolves.Instance,
        V07ReferenceActive.Instance,
        V08RetireNotRemove.Instance,
        V09Wildcards.Instance,
        V10ExactlyOne.Instance,
        V11ActiveTier.Instance,
        V12TierRank.Instance,
        V13TierQuestionOptions.Instance,
        V14LifecycleStages.Instance,
        V15StatusMeanings.Instance,
        V16MeaningChangedInUse.Instance,
        V17TaxonomyMinimum.Instance,
        V18DecisionTypeOutcome.Instance,
        V19ApprovalTierCoverage.Instance,
        V20ApprovalCoverage.Instance,
        V21ChangeAuthorizationCoverage.Instance,
        V22DecisionRightAccountable.Instance,
        V23GatePolicyCoverage.Instance,
        V24GatePolicyFailMode.Instance,
        V25GatePolicyEmergencyRule.Instance,
        V26RuleGroupAmbiguous.Instance,
        V27ApprovalRuleGroups.Instance,
        V28ValueRanges.Instance,
        V29CadenceCoverage.Instance,
        V30ApprovalRuleWorkloadApprover.Instance,
        V31DecisionRightAiLetter.Instance,
        V32ConfigurationAdmin.Instance,
        V33LockedSettings.Instance,
        V34ProtectedChange.Instance,
        V35RoleBindingTargets.Instance,
        V36SeparationSharedGroup.Instance,
        V37RoleUnbound.Instance,
        V38Settings.Instance,
        V39JsonFields.Instance,
        V40Patterns.Instance,
        V41SnapshotSize.Instance,
        V42BaseMoved.Instance,
        V43PresetConflict.Instance,
        V44PresetTenantData.Instance,
    });

    // Every engine check this validator runs after the rules, in the order of document 25, section 5.11.
    public static IReadOnlyList<EngineCheck> EngineChecks { get; } = new ReadOnlyCollection<EngineCheck>(EngineCheckList);

    public ValidationOutcome Validate(ValidationRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var issues = new ValidationIssues();
        ComposedConfiguration candidate;
        ComposedConfiguration? active;
        try
        {
            candidate = _composer.Compose(request.Candidate, request.Mode, issues, request.ConfigurationSetKey);
            active = request.Active == null
                ? candidate.BeforePatches
                : _composer.Compose(request.Active, request.Mode, new ValidationIssues(), request.ConfigurationSetKey);
        }
        catch (ConfigurationInputException e)
        {
            return InputFailure(e.Code, e.Message);
        }

        if (candidate.IsComplete)
        {
            SchemaResult shape = ConfigurationSchemas.Set.Evaluate(candidate.Bundle, candidate.Profile);
            if (!candidate.IsRaw)
            {
                V01SchemaShape.Instance.Report(shape, candidate.Where, issues, V01SchemaShape.InContent(candidate.Content));
            }

            ReferenceIndex? activeReferences = null;
            if (active != null && active.IsComplete)
            {
                SchemaResult activeShape = ConfigurationSchemas.Set.Evaluate(active.Bundle, active.Profile);
                activeReferences = ReferenceIndex.Build(active.Content, active.Bundle, activeShape);
            }

            var context = new ValidationContext(
                request.Mode,
                candidate.Content,
                candidate.Bundle,
                shape,
                active != null && active.IsComplete ? active.Content : null,
                activeReferences,
                request.RowsInUse,
                request.Facts,
                request.Facts.BaseVersionNumber ?? BaseFromPatches(request.Candidate));
            foreach (IContentRule rule in ContentRules)
            {
                rule.Check(context, issues);
            }

            foreach (EngineCheck check in EngineCheckList)
            {
                ((IContentRule)check).Check(context, issues);
            }
        }

        IReadOnlyList<string> summary = active != null && active.IsComplete && candidate.IsComplete
            ? SnapshotDiff.Summarize(active.Content, candidate.Content)
            : Array.Empty<string>();
        string hash = CanonicalHash.ComputeContentHash(candidate.Bundle);
        var report = new ValidationReport(issues.Items, summary, hash);

        CompiledSnapshot? snapshot = null;
        if (report.IsValid && candidate.IsComplete && request.Mode != ValidationMode.PresetBuild)
        {
            snapshot = SnapshotCompiler.Compile(candidate.Bundle, request.VersionNumber, request.ConfigurationSetKey);
        }

        return new ValidationOutcome(report.IsValid ? "valid" : "invalid", report, candidate, snapshot, null);
    }

    // The base version a draft or client binding patch names in its manifest. The last such patch wins.
    private static int? BaseFromPatches(ContentSource source)
    {
        int? found = null;
        foreach (ConfigurationPatch patch in source.Patches)
        {
            if ((patch.Kind != PatchKind.Draft && patch.Kind != PatchKind.ClientBinding) || patch.Document == null)
            {
                continue;
            }

            JsonTreeNode? manifest = JsonNodes.Get(patch.Document, "manifest");
            JsonTreeNode? number = manifest == null ? null : JsonNodes.Get(manifest, "baseVersionNumber");
            if (number != null && number.TryGetInt64(out long value) && value >= 1 && value <= int.MaxValue)
            {
                found = (int)value;
            }
        }

        return found;
    }

    private static ValidationOutcome InputFailure(string code, string message)
    {
        ReasonCodeInfo info = ReasonCatalog.Get(code);
        var problem = new ValidationIssue(null, code, info.DefaultSeverity, message, null, null, null);
        var report = new ValidationReport(Enumerable.Empty<ValidationIssue>(), Enumerable.Empty<string>(), CanonicalHash.ComputeContentHash(JsonTreeNode.Null));
        return new ValidationOutcome("invalid-input", report, null, null, problem);
    }
}
