using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Runtime;
using Product.Engine.Runtime.Callers;
using Product.Engine.Testing.InMemory;
using Xunit;

namespace Product.Azure.Stores.Tests;

// The same engine calls on the in-memory store and on a row store give the same answers and leave the same rows: every
// outcome, reason, row ID, write and payload, and the canonical dump of every committed row. The calls cover the
// engine's store paths: seeded and bootstrapped versions, guarded creates and updates with effects, an engine-accepted
// decision, approvals on a card through the approval runner, a configuration change activated in the runner's
// transaction, deployment records with an idempotent replay and a conflicting retry, idempotent API writes, and deletes.
public sealed class EngineParityTests
{
    [Fact]
    public void TheSameCallsGiveTheSameAnswersAndRowsInMemoryAndInSqlite()
    {
        using IStoreUnderTest memory = Stores.Create(StoreKind.InMemory);
        using IStoreUnderTest sqlite = Stores.Create(StoreKind.Sqlite);
        Scenario expected = Scenario.Run(memory, 1);
        Scenario actual = Scenario.Run(sqlite, 1);

        Assert.Equal(expected.Steps.Count, actual.Steps.Count);
        for (int i = 0; i < expected.Steps.Count; i++)
        {
            Assert.Equal(expected.Steps[i], actual.Steps[i]);
        }

        Assert.Equal(expected.Dump, actual.Dump);
        string dumpTo = Environment.GetEnvironmentVariable("PRODUCT_DUMP_STEPS") ?? string.Empty;
        if (dumpTo.Length > 0)
        {
            System.IO.File.WriteAllLines(dumpTo, actual.Steps);
        }
    }

    [Fact]
    public void TheBootstrapOfVersionOneWritesTheSameRowsInMemoryAndInSqlite()
    {
        using IStoreUnderTest memory = Stores.Create(StoreKind.InMemory);
        using IStoreUnderTest sqlite = Stores.Create(StoreKind.Sqlite);
        Scenario expected = Scenario.Bootstrap(memory);
        Scenario actual = Scenario.Bootstrap(sqlite);
        Assert.Equal(expected.Steps, actual.Steps);
        Assert.Equal(expected.Dump, actual.Dump);
        Assert.StartsWith("bootstrap: allowed", actual.Steps[1]);
    }

    // On the shared Azure SQL database row IDs are random and other runs leave rows, so this compares the outcome words
    // and reason codes with the in-memory run, not the IDs or the dump.
    [Fact]
    [Trait("Store", "SqlServer")]
    public void TheSameCallsGiveTheSameOutcomesOnAzureSql()
    {
        using IStoreUnderTest memory = Stores.Create(StoreKind.InMemory);
        Scenario expected = Scenario.Run(memory, 1);
        using IStoreUnderTest sql = Stores.Create(StoreKind.SqlServer);
        int version = 1000 + (Math.Abs(Guid.NewGuid().GetHashCode()) % 1_000_000) * 4;
        Scenario actual = Scenario.Run(sql, version);
        Assert.Equal(expected.Outcomes, actual.Outcomes);
    }

    private sealed class Scenario
    {
        private const string GrantPatchTemplate = "{\"schemaVersion\":\"1.0\",\"manifest\":{\"kind\":\"patch\",\"baseVersionNumber\":BASE},\"operations\":["
            + "{\"op\":\"upsert\",\"entity\":\"workload-grant\",\"key\":\"release-pipeline\",\"values\":{\"label\":\"release-pipeline\",\"principalObjectId\":\"" + World.NewPipeline + "\","
            + "\"workloadRoleKey\":\"deployment-recorder\",\"reason\":\"The release pipeline records production deployments.\",\"usesFederatedCredential\":true,"
            + "\"bindingDetail\":\"App role assignment release-pipeline\",\"expiresOn\":\"2027-09-30\"}}]}";

        private readonly List<string> _steps = new();
        private readonly List<string> _outcomes = new();

        private Scenario(IStoreUnderTest store)
        {
            Store = store;
            Clock = new ManualClock(World.AsOf);
            Runtime = new EngineRuntime(store.Records, store.Configuration, World.Catalog, clock: Clock);
        }

        public IReadOnlyList<string> Steps => _steps;

        // Outcome words and reason codes only, for stores whose IDs differ.
        public IReadOnlyList<string> Outcomes => _outcomes;

        public string Dump { get; private set; } = string.Empty;

        private IStoreUnderTest Store { get; }

        private ManualClock Clock { get; }

        private EngineRuntime Runtime { get; }

        public static Scenario Run(IStoreUnderTest store, int versionNumber)
        {
            using IDisposable caller = TestCaller.Enter();
            var s = new Scenario(store);
            store.SeedVersion(World.Version(versionNumber));
            string app = World.SeedDirectoryAndEstate(store);

            // A lowest-tier intake, accepted by the engine with no card.
            string intake = s.Step("intake", s.Runtime.Write(World.AiClientOf1, WriteCommand.Create("intake-request",
                "{\"requester\":\"" + World.Person1 + "\",\"title\":\"Fixture travel requests\",\"need\":\"Replace the shared spreadsheet\",\"proposedKind\":\"power-platform-app\","
                + "\"businessApplication\":\"" + app + "\",\"status\":\"intake-submitted\"}"))).RowId!;
            s.Step("assessment", s.Runtime.Write(World.AiClientOf1, WriteCommand.Create("tier-assessment",
                "{\"intakeRequest\":\"" + intake + "\",\"answersJson\":" + Answers(false) + ",\"assessedBy\":\"" + World.Person1 + "\"}")));
            string accepted = s.Step("decision", s.Runtime.Write(World.AiClientOf1, WriteCommand.Create("decision",
                "{\"decisionType\":\"intake\",\"subjectKind\":\"intake-request\",\"subjectIntakeRequest\":\"" + intake + "\",\"statement\":\"Accept the travel requests intake\",\"status\":\"decision-proposed\"}"))).RowId!;
            s.Step("engine-accept", s.Runtime.RequestApprovals(World.AiClientOf1, accepted));
            s.Clock.Advance(TimeSpan.FromMinutes(10));

            // A tier 2 intake on a card the approval runner answers for the platform admin.
            string intake2 = s.Step("intake-2", s.Runtime.Write(World.AiClientOf1, WriteCommand.Create("intake-request",
                "{\"requester\":\"" + World.Person1 + "\",\"title\":\"Fixture field audits\",\"need\":\"Replace paper forms\",\"proposedKind\":\"power-platform-app\","
                + "\"businessApplication\":\"" + app + "\",\"status\":\"intake-submitted\"}"))).RowId!;
            s.Step("assessment-2", s.Runtime.Write(World.AiClientOf1, WriteCommand.Create("tier-assessment",
                "{\"intakeRequest\":\"" + intake2 + "\",\"answersJson\":" + Answers(true) + ",\"assessedBy\":\"" + World.Person1 + "\"}")));
            string carded = s.Step("decision-2", s.Runtime.Write(World.AiClientOf1, WriteCommand.Create("decision",
                "{\"decisionType\":\"intake\",\"subjectKind\":\"intake-request\",\"subjectIntakeRequest\":\"" + intake2 + "\",\"statement\":\"Accept the field audits intake\",\"status\":\"decision-proposed\"}"))).RowId!;
            RuntimeOutcome requested = s.Step("request", s.Runtime.RequestApprovals(World.AiClientOf1, carded));
            foreach (string record in Strings(requested.Payload, "approvalRecordIds"))
            {
                s.Step("answer", Answer(s.Runtime, record, World.Person2, "Reviewed the field audits intake."));
                s.Step("answer-again", Answer(s.Runtime, record, World.Person2, "Reviewed the field audits intake."));
            }

            s.Step("status", s.Runtime.GetApprovalStatus(World.PersonTwo, carded));
            s.Clock.Advance(TimeSpan.FromMinutes(10));

            // A configuration change: the admin's AI client proposes, the admin submits, the runner records the admin's
            // approval, and the engine activates the next version in the same transaction.
            string patch = GrantPatchTemplate.Replace("BASE", versionNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
            RuntimeOutcome before = s.Step("deploy-before", s.Runtime.RecordDeployment(World.ReleasePipeline, Report(app, "release-0001", "2.0.0")));
            Assert.Equal("refused", before.Outcome);
            string draft = s.Step("propose", s.Runtime.ProposeConfigurationChange(World.AiClientOf1, new ConfigurationProposal(CanonicalJson.Parse(patch), versionNumber, "The release pipeline needs to record deployments."))).RowId!;
            RuntimeOutcome submitted = s.Step("submit", s.Runtime.SubmitConfigurationDraft(World.AiClientOf1, draft));
            foreach (string record in Strings(submitted.Payload, "approvalRecordIds"))
            {
                s.Step("approve-change", Answer(s.Runtime, record, World.Person1, "Reviewed the diff: one grant for the release pipeline."));
            }

            RuntimeOutcome info = s.Step("info", s.Runtime.GetEngineInfo(World.PersonOne));
            Assert.Equal(versionNumber + 1, info.ConfigurationVersion);
            s.Clock.Advance(TimeSpan.FromMinutes(10));

            // Deployment records from the new pipeline: created, replayed, and refused when the same key carries other facts.
            s.Step("deploy", s.Runtime.RecordDeployment(World.ReleasePipeline, Report(app, "release-0001", "2.0.0")));
            s.Step("deploy-replay", s.Runtime.RecordDeployment(World.ReleasePipeline, Report(app, "release-0001", "2.0.0")));
            s.Step("deploy-changed", s.Runtime.RecordDeployment(World.ReleasePipeline, Report(app, "release-0001", "2.0.1")));
            s.Step("deploy-2", s.Runtime.RecordDeployment(World.ReleasePipeline, Report(app, "release-0002", "2.1.0")));

            // An idempotent API write, replayed and reused with another body; then deletes by another person and by the
            // creator.
            string intakeJson = "{\"requester\":\"" + World.Person1 + "\",\"title\":\"Fixture draft\",\"need\":\"Fixture need\"}";
            string drafted = s.Step("draft", s.Runtime.Write(World.AiClientOf1, WriteCommand.Create("intake-request", intakeJson, "client-request-0001"))).RowId!;
            s.Step("draft-replay", s.Runtime.Write(World.AiClientOf1, WriteCommand.Create("intake-request", intakeJson, "client-request-0001")));
            s.Step("draft-reused", s.Runtime.Write(World.AiClientOf1, WriteCommand.Create("intake-request", intakeJson.Replace("Fixture draft", "Other"), "client-request-0001")));
            s.Step("update", s.Runtime.Write(World.PersonOne, WriteCommand.Update("intake-request", drafted, "{\"title\":\"Fixture draft, renamed\"}")));
            s.Step("delete-other", s.Runtime.Write(World.PersonTwo, WriteCommand.Delete("intake-request", drafted)));
            s.Step("delete-own", s.Runtime.Write(World.PersonOne, WriteCommand.Delete("intake-request", drafted)));
            s.Step("update-missing", s.Runtime.Write(World.PersonOne, WriteCommand.Update("intake-request", drafted, "{\"title\":\"Gone\"}")));
            s.Step("status-final", s.Runtime.GetApprovalStatus(World.PersonTwo, carded));

            s.Dump = store.Dump();
            return s;
        }

        public static Scenario Bootstrap(IStoreUnderTest store)
        {
            using IDisposable caller = TestCaller.Enter();
            var s = new Scenario(store);
            foreach (KeyValuePair<string, PrincipalKind> principal in World.DirectoryKinds())
            {
                store.Seed("principal-reference", "{\"principalKind\":\"" + EngineContract.PrincipalKinds.ToWire(principal.Value) + "\",\"entraObjectId\":\"" + principal.Key + "\"}");
            }

            s.Step("before", s.Runtime.GetEngineInfo(World.PersonOne));
            s.Step("bootstrap", s.Runtime.Bootstrap(World.PersonOne, "balanced", null, hasInstallRights: true));
            s.Step("again", s.Runtime.Bootstrap(World.PersonOne, "balanced", null, hasInstallRights: true));
            s.Step("info", s.Runtime.GetEngineInfo(World.PersonOne));
            s.Step("intake", s.Runtime.Write(World.PersonOne, WriteCommand.Create("intake-request",
                "{\"requester\":\"" + World.Person1 + "\",\"title\":\"Fixture after bootstrap\",\"need\":\"Fixture need\"}")));
            s.Dump = store.Dump();
            return s;
        }

        private RuntimeOutcome Step(string name, RuntimeOutcome outcome)
        {
            string reasons = string.Join(",", outcome.Reasons.Select(r => r.Code).OrderBy(c => c, StringComparer.Ordinal));
            _outcomes.Add(name + ": " + outcome.Outcome + " [" + reasons + "]");
            _steps.Add(name + ": " + outcome.Outcome
                + " row=" + (outcome.RowId ?? "-")
                + " created=" + outcome.Created
                + " replay=" + outcome.IsReplay
                + " noop=" + outcome.IsNoOp
                + " version=" + (outcome.ConfigurationVersion?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-")
                + " hash=" + (outcome.SnapshotHash ?? "-")
                + " reasons=[" + reasons + "]"
                + " writes=[" + string.Join(",", outcome.Writes.Select(w => w.Entity + ":" + w.Operation + ":" + w.RowId + ":" + w.Effect)) + "]"
                + " payload=" + (outcome.Payload == null ? "-" : CanonicalJson.Write(outcome.Payload, CanonicalProfile.Request)));
            return outcome;
        }

        private static RuntimeOutcome Answer(EngineRuntime runtime, string record, string approver, string reason)
        {
            return runtime.Write(World.ApprovalRunner, WriteCommand.Update("approval-record", record,
                "{\"outcome\":\"approval-approved\",\"approver\":\"" + approver + "\",\"approverActorKind\":\"person\",\"approverPrincipalKind\":\"user\","
                + "\"respondedOn\":\"2026-11-22T09:00:00Z\",\"approverHoldsRequiredRole\":true,\"channel\":\"teams\",\"typedReason\":\"" + reason + "\"}"));
        }

        private static DeploymentReport Report(string application, string key, string version)
        {
            return new DeploymentReport(key, application, "fixture-solution", version, new DateTime(2026, 11, 22, 8, 45, 0, DateTimeKind.Utc))
            {
                TargetExternalId = "fixture-env-prod",
                ArtifactKindKey = "solution",
            };
        }

        private static string Answers(bool tierTwo)
        {
            return "[{\"questionKey\":\"data-change\",\"answerKey\":\"" + (tierTwo ? "as-user" : "reads-only") + "\"},{\"questionKey\":\"sensitivity\",\"answerKey\":\"none\"},"
                + "{\"questionKey\":\"reach\",\"answerKey\":\"small-team\"},{\"questionKey\":\"failure-impact\",\"answerKey\":\"little\"}]";
        }

        private static IReadOnlyList<string> Strings(JsonTreeNode? payload, string member)
        {
            return payload != null && payload.TryGetMember(member, out JsonTreeNode? items) && items.Kind == JsonTreeKind.Array
                ? items.Items.Where(i => i.Kind == JsonTreeKind.String).Select(i => i.GetString()).ToList()
                : Array.Empty<string>();
        }
    }
}
