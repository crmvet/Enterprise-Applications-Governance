using System;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;
using Product.Engine.Runtime.Callers;
using Product.Engine.Testing.InMemory;
using Product.Engine.Runtime.Ports;
using Xunit;

namespace Product.Engine.Runtime.Tests;

// A configuration change end to end (document 25, section 6.4; document 21, section 5.1) and the bootstrap of version 1
// (document 21, section 4.8), through the in-memory store. A version change takes effect on the next call.
public sealed class ConfigurationFlowTests
{
    private const string GrantPatch = "{\"schemaVersion\":\"1.0\",\"manifest\":{\"kind\":\"patch\",\"baseVersionNumber\":1},\"operations\":["
        + "{\"op\":\"upsert\",\"entity\":\"workload-grant\",\"key\":\"release-pipeline\",\"values\":{\"label\":\"release-pipeline\",\"principalObjectId\":\"" + Harness.NewPipeline + "\","
        + "\"workloadRoleKey\":\"deployment-recorder\",\"reason\":\"The release pipeline records production deployments.\",\"usesFederatedCredential\":true,"
        + "\"bindingDetail\":\"App role assignment release-pipeline\",\"expiresOn\":\"2027-09-30\"}}]}";

    // The admin's AI client proposes a grant for a new pipeline; the admin submits it; the approval runner records the
    // admin's approval on the card; the engine accepts the decision and activates version 2 in the same transaction. The
    // new pipeline is refused before and recorded after: the next call's probe sees version 2.
    //
    // WORKING DEFAULT (open design question 4): outside strict separation the instructing admin may accept their own AI's
    // draft on the one card that shows the engine's diff.
    [Fact]
    public void AnAcceptedConfigurationChangeActivatesAndTakesEffectOnTheNextCall()
    {
        Harness h = Harness.Create();
        RuntimeOutcome before = h.Runtime.RecordDeployment(Harness.ReleasePipeline, Report(h, "release-0001"));
        Assert.Equal("refused", before.Outcome);
        Assert.True(before.HasReason(ReasonCodes.ActorWorkloadGrantMissing));

        RuntimeOutcome proposed = Harness.Ok(h.Runtime.ProposeConfigurationChange(Harness.AiClientOf1, new ConfigurationProposal(CanonicalJson.Parse(GrantPatch), 1, "The release pipeline needs to record deployments.")));
        Assert.True(RuntimeJsonTestHelpers.Flag(proposed.Payload, "isValid"), proposed.ToString() + " " + RuntimeJsonTestHelpers.Member(proposed.Results["OP-09"], "payload"));
        string draft = proposed.RowId!;
        StoredRow draftRow = h.Row("configuration-draft", draft);
        Assert.Equal("draft", Harness.Text(draftRow, "draftStatus"));
        Assert.Equal(Harness.Person1, Harness.Text(draftRow, "instructedBy"));
        Assert.Null(h.Store.GetByKey("workload-grant", "release-pipeline"));

        RuntimeOutcome submitted = Harness.Ok(h.Runtime.SubmitConfigurationDraft(Harness.AiClientOf1, draft));
        string decision = submitted.RowId!;
        Assert.Equal("submitted", Harness.Text(h.Row("configuration-draft", draft), "draftStatus"));
        Assert.Equal("configuration-change", Harness.Text(h.Row("decision", decision), "decisionType"));
        string record = Assert.Single(RuntimeJsonTestHelpers.Strings(submitted.Payload, "approvalRecordIds"));
        Assert.Equal("configuration-change-approval", Harness.Text(h.Row("approval-record", record), "approvalRule"));

        // Still version 1: the draft changed no live row.
        Assert.Equal(1, Harness.Ok(h.Runtime.GetEngineInfo(Harness.PersonOne)).ConfigurationVersion);

        RuntimeOutcome answer = Harness.Ok(h.Answer(record, "approval-approved", Harness.Person1, typedReason: "Reviewed the diff: one grant for the release pipeline."));
        Assert.Equal(1, answer.ConfigurationVersion);
        Assert.Equal("activated", RuntimeJsonTestHelpers.Text(answer.Results["activation"], "outcome"));
        Assert.Equal(2,RuntimeJsonTestHelpers.Member(answer.Results["activation"], "versionNumber")!.TryGetInt64(out long number) ? number : 0);
        Assert.Equal("accepted", Harness.Text(h.Row("decision", decision), "statusMeaning"));
        Assert.Equal("activated", Harness.Text(h.Row("configuration-draft", draft), "draftStatus"));
        Assert.Equal(2, h.Row("configuration-draft", draft).Number("resultingVersion"));
        Assert.NotNull(h.Store.GetByKey("workload-grant", "release-pipeline"));
        Assert.Equal(2, h.Store.ProbeInForceVersion()!.VersionNumber);
        Assert.Contains(answer.Writes, w => w.Entity == "configuration-set-version" && w.Effect == EffectKind.ActivateConfiguration);
        Assert.Contains(h.Store.Rows("audit-entry"), a => Harness.Text(a, "operation") == "configuration-activated");

        RuntimeOutcome after = Harness.Ok(h.Runtime.RecordDeployment(Harness.ReleasePipeline, Report(h, "release-0001")));
        Assert.Equal(2, after.ConfigurationVersion);
        Assert.Equal(2, h.Row("deployment-record", after.RowId!).Number("configurationSetVersion"));
    }

    // An accepted draft that fails OP-09 at activation is discarded with its reason, and no version is created (document
    // 21, C46 Discard Reason). Here the grant runs longer than workload-grant-max-days from the activation date (V28).
    [Fact]
    public void AnAcceptedDraftThatFailsAtActivationIsDiscardedAndNoVersionIsCreated()
    {
        Harness h = Harness.Create();
        string tooLong = GrantPatch.Replace("2027-09-30", "2028-01-31");
        RuntimeOutcome proposed = Harness.Ok(h.Runtime.ProposeConfigurationChange(Harness.AiClientOf1, new ConfigurationProposal(CanonicalJson.Parse(tooLong), 1, "A long grant.")));
        RuntimeOutcome submitted = Harness.Ok(h.Runtime.SubmitConfigurationDraft(Harness.AiClientOf1, proposed.RowId!));
        string record = RuntimeJsonTestHelpers.Strings(submitted.Payload, "approvalRecordIds").Single();

        RuntimeOutcome answer = Harness.Ok(h.Answer(record, "approval-approved", Harness.Person1, typedReason: "Reviewed."));
        Assert.Equal("discarded", RuntimeJsonTestHelpers.Text(answer.Results["activation"], "outcome"));
        Assert.True(answer.HasReason(ReasonCodes.ActivationDiscarded), answer.ToString());
        Assert.Equal("accepted", Harness.Text(h.Row("decision", submitted.RowId!), "statusMeaning"));
        StoredRow draft = h.Row("configuration-draft", proposed.RowId!);
        Assert.Equal("discarded", Harness.Text(draft, "draftStatus"));
        Assert.Equal("invalid at activation", Harness.Text(draft, "discardReason"));
        Assert.Equal(1, h.Store.ProbeInForceVersion()!.VersionNumber);
        Assert.Null(h.Store.GetByKey("workload-grant", "release-pipeline"));
        Assert.Contains(h.Store.Rows("audit-entry"), a => Harness.Text(a, "operation") == "configuration-activation-refused");
    }

    // A decision keeps the rules it started under: after version 2 is in force, OP-03 still recomputes OP-02 with the
    // version the decision was stamped with (document 25, section 5.5), while the call itself runs under version 2.
    [Fact]
    public void ARunningDecisionKeepsTheVersionItWasStampedWith()
    {
        Harness h = Harness.Create();
        string decision = h.IntakeDecision(tierTwo: true);
        Harness.Ok(h.Runtime.RequestApprovals(Harness.AiClientOf1, decision));
        h.Store.SeedVersion(Harness.Compile(2, "[{\"op\":\"upsert\",\"entity\":\"setting\",\"key\":\"link-undo-minutes\",\"values\":{\"value\":10}}]"));

        RuntimeOutcome status = Harness.Ok(h.Runtime.GetApprovalStatus(Harness.PersonTwo, decision));
        Assert.Equal(2, status.ConfigurationVersion);
        Assert.Equal(1, RuntimeJsonTestHelpers.Member(status.Results["OP-02"], "configurationVersion")!.TryGetInt64(out long stamped) ? stamped : 0);
        Assert.Equal(1, RuntimeJsonTestHelpers.Member(status.Results["OP-03"], "configurationVersion")!.TryGetInt64(out long op03) ? op03 : 0);
    }

    // Only a person holding a role with Can Change Configuration works on a draft (G-16), and the draft operation writes no
    // live row.
    [Fact]
    public void OnlyAConfigurationAdminProposesAndNoLiveRowChanges()
    {
        Harness h = Harness.Create();
        string before = h.Store.Dump();
        RuntimeOutcome outcome = h.Runtime.ProposeConfigurationChange(Harness.PersonTwo, new ConfigurationProposal(CanonicalJson.Parse(GrantPatch), 1, "Not mine to propose."));
        Assert.Equal("refused", outcome.Outcome);
        Assert.True(outcome.HasReason(ReasonCodes.ConfigNotConfigurationAdmin), outcome.ToString());
        Assert.Equal(before, h.Store.Dump());

        RuntimeOutcome validateOnly = Harness.Ok(h.Runtime.ProposeConfigurationChange(Harness.AiClientOf1, new ConfigurationProposal(CanonicalJson.Parse(GrantPatch), 1, "Check only.") { ValidateOnly = true }));
        Assert.True(RuntimeJsonTestHelpers.Flag(validateOnly.Payload, "isValid"));
        Assert.Equal(before, h.Store.Dump());
    }

    // A direct write to a live configuration row is refused, whoever calls (G-01).
    [Fact]
    public void NoCallerWritesALiveConfigurationRow()
    {
        Harness h = Harness.Create();
        StoredRow tier = h.Store.GetByKey("tier", "tier-3")!;
        RuntimeOutcome outcome = h.Runtime.Write(Harness.AiClientOf1, WriteCommand.Update("tier", tier.Id, "{\"label\":\"Tier 3: Critical\"}"));
        Assert.Equal("refused", outcome.Outcome);
        Assert.True(outcome.HasReason(ReasonCodes.ConfigActivationOnly));
    }

    // The bootstrap of version 1 from a preset, by a person with install rights while no version exists. The next call
    // loads version 1; a second bootstrap is refused.
    [Fact]
    public void TheBootstrapCreatesVersionOneAndTheNextCallUsesIt()
    {
        var store = new InMemoryStore("bootstrap-store");
        var runtime = new EngineRuntime(store, store, Harness.Catalog, clock: new ManualClock(Harness.AsOf));
        CallerIdentity installer = CallerIdentity.AiClient(Harness.Person1, null, "Claude Code");

        RuntimeOutcome outcome = Harness.Ok(runtime.Bootstrap(installer, "balanced", null, hasInstallRights: true));
        Assert.Equal(1, outcome.ConfigurationVersion);
        Assert.Equal(1, store.ProbeInForceVersion()!.VersionNumber);
        StoredRow decision = store.Get("decision", outcome.RowId!)!;
        Assert.Equal("accepted", decision.Text("statusMeaning"));
        Assert.Equal("configuration-change", decision.Text("decisionType"));
        Assert.Equal(Harness.Person1, decision.Text("decider"));
        Assert.Contains(store.Rows("audit-entry"), a => a.Text("operation") == "decision-accepted");
        Assert.NotNull(store.GetByKey("tier", "tier-1"));

        RuntimeOutcome info = Harness.Ok(runtime.GetEngineInfo(CallerIdentity.Person(Harness.Person1)));
        Assert.Equal(1, info.ConfigurationVersion);
        Assert.Equal(outcome.SnapshotHash, info.SnapshotHash);

        RuntimeOutcome again = runtime.Bootstrap(installer, "balanced", null, hasInstallRights: true);
        Assert.Equal("refused", again.Outcome);
        Assert.True(again.HasReason(ReasonCodes.BootstrapVersionExists));
    }

    [Fact]
    public void OnlyAPersonWithInstallRightsBootstraps()
    {
        var store = new InMemoryStore("bootstrap-refusals");
        var runtime = new EngineRuntime(store, store, Harness.Catalog, clock: new ManualClock(Harness.AsOf));

        RuntimeOutcome noRights = runtime.Bootstrap(CallerIdentity.Person(Harness.Person1), "balanced", null, hasInstallRights: false);
        Assert.Equal("refused", noRights.Outcome);
        Assert.True(noRights.HasReason(ReasonCodes.BootstrapNotAllowed));

        RuntimeOutcome workload = runtime.Bootstrap(Harness.BuildPipeline, "balanced", null, hasInstallRights: true);
        Assert.Equal("refused", workload.Outcome);
        Assert.True(workload.HasReason(ReasonCodes.ActorWorkloadGrantMissing));
        Assert.Null(store.ProbeInForceVersion());
        Assert.Equal(0, store.CommitCount);
    }

    private static DeploymentReport Report(Harness h, string key)
    {
        return new DeploymentReport(key, h.ApplicationId, "fixture-solution", "2.0.0", new DateTime(2026, 11, 22, 8, 45, 0, DateTimeKind.Utc))
        {
            TargetExternalId = "fixture-env-prod",
            ArtifactKindKey = "solution",
        };
    }
}
