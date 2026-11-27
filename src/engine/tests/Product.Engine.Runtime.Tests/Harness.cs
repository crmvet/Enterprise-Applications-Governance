using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Composition;
using Product.Engine.Configuration.Patches;
using Product.Engine.Configuration.Presets;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Configuration.Validation;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Runtime;
using Product.Engine.Runtime.Callers;
using Product.Engine.Testing;
using Product.Engine.Testing.InMemory;
using Xunit;

namespace Product.Engine.Runtime.Tests;

// A fictional client on the in-memory store: Balanced plus a client binding patch (three group bindings and two workload
// grants) as version 1, as an earlier activation left it in force; a small directory of Principal References; and an
// estate of one portfolio, one registered application with a business owner, and one production target. Every ID is a
// patterned placeholder. Seeding is test setup, not an engine write.
//
// WORKING DEFAULT (open design question 12): the approval runner and the build pipeline hold their workload roles
// directly through Workload Grant rows of the client binding patch.
// WORKING DEFAULT (open design question 9): an AI client may file and submit an intake for the person (RA-03).
internal sealed class Harness
{
    public static readonly DateTime AsOf = new(2026, 11, 22, 9, 0, 0, DateTimeKind.Utc);

    public const string RequesterGroup = "00000000-0000-4000-8000-000000000101";
    public const string PlatformAdminGroup = "00000000-0000-4000-8000-000000000102";
    public const string ConfigurationAdminGroup = "00000000-0000-4000-8000-000000000103";

    // Person 1 requests and is a configuration admin; person 2 is a platform admin; person 3 holds no role.
    public const string Person1 = "00000000-0000-4000-8000-000000000201";
    public const string Person2 = "00000000-0000-4000-8000-000000000202";
    public const string Person3 = "00000000-0000-4000-8000-000000000203";

    public const string Runner = "00000000-0000-4000-8000-000000000301";
    public const string Pipeline = "00000000-0000-4000-8000-000000000302";
    public const string NewPipeline = "00000000-0000-4000-8000-000000000303";
    public const string AgentIdentity = "00000000-0000-4000-8000-000000000401";

    public const string GrantExpiry = "2027-06-30";

    private static readonly Lazy<PresetCatalog> LazyCatalog = new(() => PresetCatalog.Load(new[] { SourceRoot.PresetsFolder(SourceRoot.Require(AppContext.BaseDirectory)) }));

    private Harness(InMemoryStore store, ManualClock clock, EngineRuntime runtime, CompiledSnapshot version1)
    {
        Store = store;
        Clock = clock;
        Runtime = runtime;
        Version1 = version1;
    }

    public static PresetCatalog Catalog => LazyCatalog.Value;

    public InMemoryStore Store { get; }

    public ManualClock Clock { get; }

    public EngineRuntime Runtime { get; }

    public CompiledSnapshot Version1 { get; }

    public string PortfolioId { get; private set; } = string.Empty;

    public string ApplicationId { get; private set; } = string.Empty;

    public string TargetId { get; private set; } = string.Empty;

    // ---- Callers ----

    public static CallerIdentity AiClientOf1 => CallerIdentity.AiClient(Person1, new[] { RequesterGroup, ConfigurationAdminGroup }, "Claude Code");

    public static CallerIdentity PersonOne => CallerIdentity.Person(Person1, new[] { RequesterGroup, ConfigurationAdminGroup }, "Product page");

    public static CallerIdentity PersonTwo => CallerIdentity.Person(Person2, new[] { PlatformAdminGroup }, "Product page");

    public static CallerIdentity ApprovalRunner => CallerIdentity.Workload(Runner, clientApp: "approval runner");

    public static CallerIdentity BuildPipeline => CallerIdentity.Workload(Pipeline, clientApp: "build pipeline");

    public static CallerIdentity ReleasePipeline => CallerIdentity.Workload(NewPipeline, clientApp: "release pipeline");

    public static Harness Create(string? extraOperationsJson = null, string storeId = "fixture-store")
    {
        var store = new InMemoryStore(storeId);
        var clock = new ManualClock(AsOf);
        CompiledSnapshot version1 = Compile(1, extraOperationsJson);
        store.SeedVersion(version1);
        var runtime = new EngineRuntime(store, store, Catalog, clock: clock);
        var harness = new Harness(store, clock, runtime, version1);
        harness.SeedDirectory();
        harness.SeedEstate();
        return harness;
    }

    // Balanced plus the client binding patch, validated in activation mode on the harness date and compiled as a working
    // set: the version activation would leave in force.
    public static CompiledSnapshot Compile(int versionNumber, string? extraOperationsJson = null)
    {
        string operations = "[" + ClientBindingOperations + (extraOperationsJson == null ? string.Empty : "," + extraOperationsJson.Trim().TrimStart('[').TrimEnd(']')) + "]";
        ContentSource source = ContentSource.Preset("balanced").WithPatch(ConfigurationPatch.FromOperations(CanonicalJson.Parse(operations), PatchKind.ClientBinding, "client binding patch"));
        ValidationOutcome outcome = new ConfigurationValidator(Catalog).Validate(new ValidationRequest(
            source,
            ValidationMode.Activation,
            versionNumber: versionNumber,
            facts: new ValidationFacts(validatedOn: AsOf, principalKinds: DirectoryKinds())));
        Assert.True(outcome.Snapshot != null, "the harness version is invalid: " + string.Join(" | ", outcome.Report.Issues.Where(i => i.Severity == ReasonSeverity.Error).Select(i => i.Reason.Code + " " + i.Reason.Message)));
        return outcome.Snapshot!;
    }

    public static IEnumerable<KeyValuePair<string, PrincipalKind>> DirectoryKinds()
    {
        yield return new KeyValuePair<string, PrincipalKind>(RequesterGroup, PrincipalKind.Group);
        yield return new KeyValuePair<string, PrincipalKind>(PlatformAdminGroup, PrincipalKind.Group);
        yield return new KeyValuePair<string, PrincipalKind>(ConfigurationAdminGroup, PrincipalKind.Group);
        yield return new KeyValuePair<string, PrincipalKind>(Person1, PrincipalKind.User);
        yield return new KeyValuePair<string, PrincipalKind>(Person2, PrincipalKind.User);
        yield return new KeyValuePair<string, PrincipalKind>(Person3, PrincipalKind.User);
        yield return new KeyValuePair<string, PrincipalKind>(Runner, PrincipalKind.ServicePrincipal);
        yield return new KeyValuePair<string, PrincipalKind>(Pipeline, PrincipalKind.ServicePrincipal);
        yield return new KeyValuePair<string, PrincipalKind>(NewPipeline, PrincipalKind.ServicePrincipal);
        yield return new KeyValuePair<string, PrincipalKind>(AgentIdentity, PrincipalKind.AgentIdentity);
    }

    public static string Binding(string key, string role, string group)
    {
        return "{\"op\":\"upsert\",\"entity\":\"role-group-binding\",\"key\":\"" + key + "\",\"values\":{\"label\":\"" + key + "\",\"roleKey\":\"" + role
            + "\",\"entraGroupObjectId\":\"" + group + "\",\"bindingMethod\":\"entra-app-role-assignment\",\"bindingDetail\":\"App role " + role + "\"}}";
    }

    public static string Grant(string key, string role, string principal, string expiresOn)
    {
        return "{\"op\":\"upsert\",\"entity\":\"workload-grant\",\"key\":\"" + key + "\",\"values\":{\"label\":\"" + key + "\",\"principalObjectId\":\"" + principal
            + "\",\"workloadRoleKey\":\"" + role + "\",\"reason\":\"Fixture workload " + key + "\",\"usesFederatedCredential\":true,\"bindingDetail\":\"App role assignment " + key + "\",\"expiresOn\":\"" + expiresOn + "\"}}";
    }

    private static string ClientBindingOperations => string.Join(",", new[]
    {
        Binding("requesters", "requester", RequesterGroup),
        Binding("platform-admins", "platform-admin", PlatformAdminGroup),
        Binding("configuration-admins", "configuration-admin", ConfigurationAdminGroup),
        Grant("approval-runner", "approval-writer", Runner, GrantExpiry),
        Grant("build-pipeline", "deployment-recorder", Pipeline, GrantExpiry),
    });

    // ---- Seeding ----

    private void SeedDirectory()
    {
        foreach (KeyValuePair<string, PrincipalKind> principal in DirectoryKinds())
        {
            string kind = EngineContract.PrincipalKinds.ToWire(principal.Value);
            Store.Seed("principal-reference", "{\"principalKind\":\"" + kind + "\",\"entraObjectId\":\"" + principal.Key + "\",\"displayLabel\":\"Fixture " + kind + " "
                + principal.Key.Substring(principal.Key.Length - 3) + "\",\"isErased\":false}");
        }
    }

    private void SeedEstate()
    {
        PortfolioId = Store.Seed("portfolio", "{\"level\":\"department\",\"isActive\":true}");
        ApplicationId = Store.Seed("business-application", "{\"recordNumber\":\"APP-0001\",\"purpose\":\"Fixture expense approvals\",\"applicationKind\":\"power-platform-app\","
            + "\"portfolio\":\"" + PortfolioId + "\",\"status\":\"app-registered\",\"statusMeaning\":\"registered\",\"tier\":\"tier-1\",\"lifecycleStage\":\"operate\","
            + "\"stageSince\":\"2026-01-05T00:00:00Z\",\"configurationSetVersion\":1}");
        Store.Seed("owner-assignment", "{\"businessApplication\":\"" + ApplicationId + "\",\"ownerRole\":\"business-owner\",\"principal\":\"" + Person1 + "\",\"isDeputy\":false,\"startOn\":\"2026-01-05\"}");
        TargetId = Store.Seed("deployment-target", "{\"targetKind\":\"environment\",\"targetStage\":\"production\",\"changePath\":\"pipeline-only\",\"linkKind\":\"power-platform-resource\","
            + "\"externalId\":\"fixture-env-prod\",\"labelAsSeen\":\"Fixture production\"}");
    }

    // ---- Scenario helpers ----

    // Tier answers for a power platform app: every question answered at tier 1, or data change raised to tier 2.
    public static string Answers(bool tierTwo)
    {
        return "[{\"questionKey\":\"data-change\",\"answerKey\":\"" + (tierTwo ? "as-user" : "reads-only") + "\"},{\"questionKey\":\"sensitivity\",\"answerKey\":\"none\"},"
            + "{\"questionKey\":\"reach\",\"answerKey\":\"small-team\"},{\"questionKey\":\"failure-impact\",\"answerKey\":\"little\"}]";
    }

    // An AI client files an intake request for person 1 (linked to the estate's application when asked), a tier
    // assessment and an intake decision. Returns the decision ID.
    public string IntakeDecision(bool tierTwo, bool linkApplication = true, string? businessOwner = null)
    {
        RuntimeOutcome intake = Ok(Runtime.Write(AiClientOf1, WriteCommand.Create("intake-request",
            "{\"requester\":\"" + Person1 + "\",\"title\":\"Fixture travel requests\",\"need\":\"Replace the shared spreadsheet\",\"proposedKind\":\"power-platform-app\","
            + (linkApplication ? "\"businessApplication\":\"" + ApplicationId + "\"," : string.Empty)
            + (businessOwner == null ? string.Empty : "\"businessOwner\":\"" + businessOwner + "\",") + "\"status\":\"intake-submitted\"}")));
        Ok(Runtime.Write(AiClientOf1, WriteCommand.Create("tier-assessment",
            "{\"intakeRequest\":\"" + intake.RowId + "\",\"answersJson\":" + Answers(tierTwo) + ",\"assessedBy\":\"" + Person1 + "\"}")));
        RuntimeOutcome decision = Ok(Runtime.Write(AiClientOf1, WriteCommand.Create("decision",
            "{\"decisionType\":\"intake\",\"subjectKind\":\"intake-request\",\"subjectIntakeRequest\":\"" + intake.RowId + "\",\"statement\":\"Accept the travel requests intake\",\"status\":\"decision-proposed\"}")));
        return decision.RowId!;
    }

    // The approval runner records an answer on a card (document 26, section 5: the runner writes the outcome).
    public RuntimeOutcome Answer(string recordId, string outcome, string approver, string actorKind = "person", string principalKind = "user", string? typedReason = null)
    {
        string after = "{\"outcome\":\"" + outcome + "\",\"approver\":\"" + approver + "\",\"approverActorKind\":\"" + actorKind + "\",\"approverPrincipalKind\":\"" + principalKind
            + "\",\"respondedOn\":\"2026-11-22T09:00:00Z\",\"approverHoldsRequiredRole\":true,\"channel\":\"teams\"" + (typedReason == null ? string.Empty : ",\"typedReason\":\"" + typedReason + "\"") + "}";
        return Runtime.Write(ApprovalRunner, WriteCommand.Update("approval-record", recordId, after));
    }

    // The one counting record among those a request opened. Balanced also opens consultation cards (document 21, B21-9:
    // the architect, the PMO and the security reviewer on an intake), which never count.
    // A business owner the intake names consents on a record of their own (document 25, B25-18), which this leaves out.
    public string CountingRecord(JsonTreeNode? payload)
    {
        return Assert.Single(RuntimeJsonTestHelpers.Strings(payload, "approvalRecordIds"), id => !Row("approval-record", id).Flag("isConsultation") && !IsConsent(id));
    }

    // The consent record of the business owner an intake names (B25-18), asked by name.
    public string ConsentRecord(JsonTreeNode? payload)
    {
        return Assert.Single(RuntimeJsonTestHelpers.Strings(payload, "approvalRecordIds"), IsConsent);
    }

    private bool IsConsent(string id)
    {
        return Row("approval-record", id).Text("approvalRule") == "intake-business-owner-consent";
    }

    public Ports.StoredRow Row(string entity, string id)
    {
        return Store.Get(entity, id) ?? throw new InvalidOperationException(entity + " " + id + " is not stored.");
    }

    public static RuntimeOutcome Ok(RuntimeOutcome outcome)
    {
        Assert.True(outcome.Succeeded, outcome.ToString());
        return outcome;
    }

    public static string Text(Ports.StoredRow row, string column)
    {
        return row.Text(column) ?? string.Empty;
    }
}
