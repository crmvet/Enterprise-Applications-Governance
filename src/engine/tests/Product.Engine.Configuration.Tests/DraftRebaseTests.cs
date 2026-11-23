using System;
using System.IO;
using System.Linq;
using Product.Engine.Configuration.Composition;
using Product.Engine.Configuration.Drafts;
using Product.Engine.Configuration.Json;
using Product.Engine.Configuration.Model;
using Product.Engine.Configuration.Patches;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Configuration.Validation;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Testing;
using Xunit;

namespace Product.Engine.Configuration.Tests;

// Draft rebase (document 21, section 4.5; document 25, section 6.4, step 5). The fixtures under
// fixtures/rebase hold the three cases of section 4.5 and the footprint rules. These tests add the branches a
// fixture cannot state well: wildcard expansions, a retired parent, a diff that changes, bad input and determinism.
public sealed class DraftRebaseTests
{
    private const string SmallBase = "{\"schemaVersion\":\"1.0\",\"manifest\":{\"kind\":\"bundle\"},\"entities\":{"
        + "\"tier\":[{\"key\":\"tier-1\",\"label\":\"Tier 1\",\"rank\":1},{\"key\":\"tier-2\",\"label\":\"Tier 2\",\"rank\":2}],"
        + "\"review-type\":[{\"key\":\"tier-check\",\"label\":\"Tier check\"}]";

    private static readonly DraftRebaser Rebaser = new(TestData.Catalog);

    private static ContentSource Balanced(string? operationsJson = null)
    {
        ContentSource source = ContentSource.Preset("balanced");
        return operationsJson == null ? source : source.WithPatch(ConfigurationPatch.FromOperations(CanonicalJson.Parse(operationsJson), PatchKind.Draft, "version operations"));
    }

    private static ConfigurationPatch DraftDocument(int baseVersion, string operationsJson)
    {
        string document = "{\"schemaVersion\":\"1.0\",\"manifest\":{\"kind\":\"patch\",\"baseVersionNumber\":" + baseVersion + "},\"operations\":" + operationsJson + "}";
        return ConfigurationPatch.FromDocument(CanonicalJson.Parse(document), PatchKind.Draft, "test draft");
    }

    private static ConfigurationPatch Inline(string operationsJson)
    {
        return ConfigurationPatch.FromOperations(CanonicalJson.Parse(operationsJson), PatchKind.Draft, "test operations");
    }

    private static RebaseOutcome Rebase(ContentSource baseVersion, int baseNumber, ContentSource active, int activeNumber, ConfigurationPatch draft, ValidationFacts? facts = null)
    {
        return Rebaser.Rebase(new RebaseRequest(baseVersion, baseNumber, active, activeNumber, draft, facts ?? new ValidationFacts(validatedOn: new DateTime(2026, 11, 22))));
    }

    private const string RenameTier2 = "[{\"op\":\"upsert\",\"entity\":\"tier\",\"key\":\"tier-2\",\"values\":{\"label\":\"Tier 2: Important\"}}]";
    private const string RenameTier3 = "[{\"op\":\"upsert\",\"entity\":\"tier\",\"key\":\"tier-3\",\"values\":{\"label\":\"Tier 3: Critical\"}}]";

    [Fact]
    public void SameBaseRevalidatesAndKeepsThePatch()
    {
        ConfigurationPatch draft = DraftDocument(4, RenameTier3);
        RebaseOutcome outcome = Rebase(Balanced(), 4, Balanced(), 4, draft);

        Assert.Equal(RebaseOutcome.SameBase, outcome.Outcome);
        Assert.True(outcome.CanActivate);
        Assert.Empty(outcome.ChangedRows);
        Assert.Equal(draft.Document, outcome.RebasedPatch);
        Assert.NotNull(outcome.Validation);
        Assert.Equal(new[] { "Changed Tier `tier-3`: label." }, outcome.ChangeSummary.ToArray());
        Assert.False(outcome.DiffChanged);

        // The revalidation is the activation check: a working set snapshot of the next version.
        Assert.NotNull(outcome.Validation!.Snapshot);
        Assert.Equal(5, outcome.Validation.Snapshot!.VersionNumber);
    }

    [Fact]
    public void NoOverlapReappliesTheSameOperationsOnTheActiveVersion()
    {
        ConfigurationPatch draft = DraftDocument(1, RenameTier3);
        RebaseOutcome outcome = Rebase(Balanced(), 1, Balanced(RenameTier2), 2, draft);

        Assert.Equal(RebaseOutcome.Rebased, outcome.Outcome);
        Assert.Empty(outcome.Overlaps);
        RowChange changed = Assert.Single(outcome.ChangedRows);
        Assert.Equal("tier", changed.Entity);
        Assert.Equal("tier-2", changed.KeyPath);

        // The patch as applied names version 2 and holds the same operations.
        JsonTreeNode patch = outcome.RebasedPatch!;
        Assert.Equal(JsonTreeNode.FromInt64(2), JsonNodes.Get(JsonNodes.Get(patch, "manifest")!, "baseVersionNumber"));
        Assert.Equal(JsonNodes.Get(draft.Document!, "operations"), JsonNodes.Get(patch, "operations"));

        // The rebased candidate holds both changes: version 2's and the draft's.
        ConfigurationContent content = outcome.Validation!.Candidate!.Content;
        Assert.True(content.TryFindRow("tier", "tier-2", out ContentRow? tier2));
        Assert.True(content.TryFindRow("tier", "tier-3", out ContentRow? tier3));
        Assert.Equal("Tier 2: Important", JsonNodes.GetString(tier2!.Node, "label"));
        Assert.Equal("Tier 3: Critical", JsonNodes.GetString(tier3!.Node, "label"));

        // V42 passes, because the draft was rebased and revalidated.
        Assert.DoesNotContain(outcome.Validation.Report.Issues, i => i.RuleId == "V42");
    }

    [Fact]
    public void AnOverlapRefusesWithV42AndValidatesNothing()
    {
        RebaseOutcome outcome = Rebase(Balanced(), 1, Balanced(RenameTier3.Replace("Critical", "High")), 2, DraftDocument(1, RenameTier3));

        Assert.Equal(RebaseOutcome.Conflict, outcome.Outcome);
        Assert.False(outcome.CanActivate);
        Assert.Null(outcome.Validation);
        Assert.Null(outcome.RebasedPatch);
        RebaseOverlap overlap = Assert.Single(outcome.Overlaps);
        Assert.Equal(0, overlap.OperationIndex);
        ValidationIssue reason = Assert.Single(outcome.Reasons);
        Assert.Equal("V42", reason.RuleId);
        Assert.Equal("config.base-moved", reason.Code);
        Assert.Equal("Tier", reason.Entity);
        Assert.Equal("tier-3", reason.Ref);
        Assert.StartsWith("V42: ", reason.Message);
        Assert.Contains("between version 1 and version 2", reason.Message);
    }

    [Fact]
    public void ARowAWildcardExpandedIntoBelongsToTheWildcardKey()
    {
        // The base expands wildcard row tc into tc--tier-1 and tc--tier-2. Version 2 changed tc--tier-2.
        // A draft that writes the wildcard row tc again touches its expansions, so it overlaps.
        JsonTreeNode bundle = CanonicalJson.Parse(SmallBase + ",\"review-cadence\":[{\"key\":\"tc\",\"label\":\"Tier check\",\"reviewTypeKey\":\"tier-check\",\"tierKey\":\"*\",\"intervalDays\":365}]}}");
        ContentSource baseVersion = ContentSource.Bundle(bundle, "base");
        ContentSource active = baseVersion.WithPatch(Inline("[{\"op\":\"upsert\",\"entity\":\"review-cadence\",\"key\":\"tc--tier-2\",\"values\":{\"intervalDays\":90}}]"));
        RebaseOutcome outcome = Rebase(baseVersion, 1, active, 2, Inline("[{\"op\":\"upsert\",\"entity\":\"review-cadence\",\"key\":\"tc\",\"values\":{\"label\":\"Tier check\",\"reviewTypeKey\":\"tier-check\",\"tierKey\":\"*\",\"intervalDays\":180}}]"));

        Assert.Equal(RebaseOutcome.Conflict, outcome.Outcome);
        Assert.Equal("tc--tier-2", Assert.Single(outcome.Overlaps).Row.KeyPath);
    }

    [Fact]
    public void RetiringAParentTouchesEveryChildRow()
    {
        string addPartners = "[{\"op\":\"upsert\",\"entity\":\"tier-answer-option\",\"key\":\"reach/partners\",\"values\":{\"label\":\"Partners\",\"answerText\":\"Partner organizations\",\"impliedTierKey\":\"tier-3\"}}]";
        RebaseOutcome outcome = Rebase(Balanced(), 1, Balanced(addPartners), 2, DraftDocument(1, "[{\"op\":\"retire\",\"entity\":\"tier-question\",\"key\":\"reach\"}]"));

        Assert.Equal(RebaseOutcome.Conflict, outcome.Outcome);
        RebaseOverlap overlap = Assert.Single(outcome.Overlaps);
        Assert.Equal("tier-answer-option", overlap.Row.Entity);
        Assert.Equal("reach/partners", overlap.Row.KeyPath);
        Assert.Equal(RowChangeKind.Added, overlap.Row.Kind);
    }

    [Fact]
    public void UpsertingAParentWithoutChildrenDoesNotTouchItsChildren()
    {
        string addPartners = "[{\"op\":\"upsert\",\"entity\":\"tier-answer-option\",\"key\":\"reach/partners\",\"values\":{\"label\":\"Partners\",\"answerText\":\"Partner organizations\",\"impliedTierKey\":\"tier-3\"}}]";
        RebaseOutcome outcome = Rebase(Balanced(), 1, Balanced(addPartners), 2, DraftDocument(1, "[{\"op\":\"upsert\",\"entity\":\"tier-question\",\"key\":\"reach\",\"values\":{\"label\":\"Who uses it\"}}]"));

        Assert.Equal(RebaseOutcome.Rebased, outcome.Outcome);
        ConfigurationContent content = outcome.Validation!.Candidate!.Content;
        Assert.True(content.TryFindRow("tier-answer-option", "reach/partners", out _));
    }

    [Fact]
    public void TheUpdatedDiffIsShownWhenItDiffersFromTheOneTheAdminSaw()
    {
        // Version 2 added tier-3. The draft adds a wildcard cadence, which now expands to three tiers, not two.
        string cadences = ",\"review-cadence\":[]}}";
        ContentSource baseVersion = ContentSource.Bundle(CanonicalJson.Parse(SmallBase + cadences), "base");
        ContentSource active = ContentSource.Bundle(CanonicalJson.Parse(SmallBase.Replace("{\"key\":\"tier-2\",\"label\":\"Tier 2\",\"rank\":2}]", "{\"key\":\"tier-2\",\"label\":\"Tier 2\",\"rank\":2},{\"key\":\"tier-3\",\"label\":\"Tier 3\",\"rank\":3}]") + cadences), "active");
        RebaseOutcome outcome = Rebase(baseVersion, 1, active, 2, Inline("[{\"op\":\"upsert\",\"entity\":\"review-cadence\",\"key\":\"rc\",\"values\":{\"label\":\"Tier check\",\"reviewTypeKey\":\"tier-check\",\"tierKey\":\"*\",\"intervalDays\":365}}]"));

        // The small set fails other rules, so it cannot activate. The diff is still computed and compared.
        Assert.NotEqual(RebaseOutcome.Conflict, outcome.Outcome);
        Assert.True(outcome.DiffChanged);
        Assert.Equal(2, outcome.PreviousChangeSummary.Count);
        Assert.Equal(3, outcome.ChangeSummary.Count);
        Assert.Contains("Added Review Cadence `rc--tier-3`.", outcome.ChangeSummary);
    }

    [Fact]
    public void ARebasedProtectedChangeStillNeedsApprovalsUnderTheActiveRules()
    {
        // The draft changes who may change configuration. Its approvals came from version 1's rules.
        // After the rebase onto version 2, V34 refuses it, so a rebase never carries a protected change past the active rules.
        ConfigurationPatch draft = DraftDocument(1, "[{\"op\":\"upsert\",\"entity\":\"approval-rule\",\"key\":\"configuration-change-approval\",\"values\":{\"requiresTypedReason\":false}}]");
        RebaseOutcome old = Rebase(Balanced(), 1, Balanced(RenameTier2), 2, draft, new ValidationFacts(validatedOn: new DateTime(2026, 11, 22), approvalRulesVersionNumber: 1));
        Assert.Equal(RebaseOutcome.Invalid, old.Outcome);
        Assert.Contains(old.Reasons, r => r.RuleId == "V34");

        RebaseOutcome current = Rebase(Balanced(), 1, Balanced(RenameTier2), 2, draft, new ValidationFacts(validatedOn: new DateTime(2026, 11, 22), approvalRulesVersionNumber: 2));
        Assert.Equal(RebaseOutcome.Rebased, current.Outcome);
    }

    [Fact]
    public void InlineOperationsGetAPatchDocumentNamingTheActiveVersion()
    {
        RebaseOutcome outcome = Rebase(Balanced(), 1, Balanced(RenameTier2), 3, Inline(RenameTier3));

        Assert.Equal(RebaseOutcome.Rebased, outcome.Outcome);
        JsonTreeNode manifest = JsonNodes.Get(outcome.RebasedPatch!, "manifest")!;
        Assert.Equal("patch", JsonNodes.GetString(manifest, "kind"));
        Assert.Equal(JsonTreeNode.FromInt64(3), JsonNodes.Get(manifest, "baseVersionNumber"));
        Assert.Equal(CanonicalJson.Parse(RenameTier3), JsonNodes.Get(outcome.RebasedPatch!, "operations"));
    }

    [Fact]
    public void BadRequestsAreInvalidInput()
    {
        // The draft names another base than the snapshot given.
        Assert.Equal(RebaseOutcome.InvalidInput, Rebase(Balanced(), 1, Balanced(RenameTier2), 2, DraftDocument(2, RenameTier3)).Outcome);

        // The base is above the active version.
        Assert.Equal(RebaseOutcome.InvalidInput, Rebase(Balanced(), 3, Balanced(), 2, DraftDocument(3, RenameTier3)).Outcome);

        // Two contents claim the same version number.
        RebaseOutcome twoContents = Rebase(Balanced(), 2, Balanced(RenameTier2), 2, DraftDocument(2, RenameTier3));
        Assert.Equal(RebaseOutcome.InvalidInput, twoContents.Outcome);
        Assert.Equal("input.malformed", twoContents.InputProblem!.Code);

        // A preset patch is never rebased.
        ConfigurationPatch overlay = ConfigurationPatch.FromOperations(CanonicalJson.Parse(RenameTier3), PatchKind.Overlay, "overlay");
        Assert.Equal(RebaseOutcome.InvalidInput, Rebase(Balanced(), 1, Balanced(), 2, overlay).Outcome);

        // An unknown preset cannot be composed.
        Assert.Equal(RebaseOutcome.InvalidInput, Rebase(ContentSource.Preset("no-such-preset"), 1, Balanced(), 2, DraftDocument(1, RenameTier3)).Outcome);
    }

    [Fact]
    public void TheSameRequestGivesTheSameAnswer()
    {
        string first = CanonicalJson.Write(Rebase(Balanced(), 1, Balanced(RenameTier2), 2, DraftDocument(1, RenameTier3)).ToJson(), CanonicalProfile.Request);
        string second = CanonicalJson.Write(Rebase(Balanced(), 1, Balanced(RenameTier2), 2, DraftDocument(1, RenameTier3)).ToJson(), CanonicalProfile.Request);
        Assert.Equal(first, second);

        string conflictA = CanonicalJson.Write(Rebase(Balanced(), 1, Balanced(RenameTier3.Replace("Critical", "High")), 2, DraftDocument(1, RenameTier3)).ToJson(), CanonicalProfile.Request);
        string conflictB = CanonicalJson.Write(Rebase(Balanced(), 1, Balanced(RenameTier3.Replace("Critical", "High")), 2, DraftDocument(1, RenameTier3)).ToJson(), CanonicalProfile.Request);
        Assert.Equal(conflictA, conflictB);
    }

    [Fact]
    public void TheRowDiffIgnoresOriginAndReportsEachKindOfChange()
    {
        var composer = new ConfigurationComposer(TestData.Catalog);
        ConfigurationContent before = composer.Compose(Balanced(), ValidationMode.Draft, new ValidationIssues()).Content;
        ConfigurationContent after = composer.Compose(
            Balanced("[{\"op\":\"upsert\",\"entity\":\"tier\",\"key\":\"tier-2\",\"values\":{\"label\":\"Tier 2\"}},"
                + "{\"op\":\"upsert\",\"entity\":\"tier\",\"key\":\"tier-3\",\"values\":{\"label\":\"Tier 3: Critical\"}},"
                + "{\"op\":\"retire\",\"entity\":\"approval-rule\",\"key\":\"gate-policy-change-change-manager\"},"
                + "{\"op\":\"upsert\",\"entity\":\"tier-answer-option\",\"key\":\"reach/partners\",\"values\":{\"label\":\"Partners\",\"answerText\":\"Partner organizations\",\"impliedTierKey\":\"tier-3\"}}]"),
            ValidationMode.Draft,
            new ValidationIssues()).Content;

        // tier-2 was upserted with its own label: origin changes, content does not, so it is not a change.
        Assert.Equal(
            new[] { "approval-rule gate-policy-change-change-manager (retired)", "tier tier-3 (changed)", "tier-answer-option reach/partners (added)" },
            SnapshotDiff.Changes(before, after).Select(c => c.ToString()).ToArray());
    }

    [Fact]
    public void EveryRebaseOutcomeHasAFixture()
    {
        FixtureRunner runner = FixtureRunner.ForSourceRoot(TestData.Root);
        var results = runner.RunFolder(Path.Combine(TestData.Root, "fixtures", "rebase")).ToList();

        Assert.All(results, r => Assert.True(r.Status == FixtureStatus.Passed, r.File + ": " + string.Join(" | ", r.Problems)));
        foreach (string word in new[] { RebaseOutcome.SameBase, RebaseOutcome.Rebased, RebaseOutcome.Invalid, RebaseOutcome.Conflict, RebaseOutcome.InvalidInput })
        {
            Assert.Contains(results, r => r.ActualOutcome == word);
        }

        // Every conflict is a V42 refusal.
        Assert.All(results.Where(r => r.ActualOutcome == RebaseOutcome.Conflict), r => Assert.Equal(new[] { "V42" }, r.ActualRuleIds.ToArray()));
    }
}
