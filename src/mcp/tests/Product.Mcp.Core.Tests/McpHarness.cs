using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Product.Engine.Configuration.Composition;
using Product.Engine.Configuration.Patches;
using Product.Engine.Configuration.Presets;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Configuration.Validation;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Runtime;
using Product.Engine.Testing.InMemory;
using Product.Mcp.Core;
using Product.Mcp.Core.Backend;
using Product.Mcp.Core.Backend.InProcess;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Identity;
using Product.Mcp.Core.Json;
using Product.Mcp.Core.Protocol;
using Product.Mcp.Core.Resources;
using Product.Mcp.Core.Telemetry;
using Product.Mcp.Testing;
using Xunit;

namespace Product.Mcp.Core.Tests;

// A fictional client for the MCP server core on the in-memory store: Balanced plus a client binding
// patch as version 1, as an earlier activation left it in force; a directory of Principal References; an estate of one
// portfolio, two registered applications with owners, two targets, a component, a dependency and a document link; the
// in-process backend (the Azure shape); a mock identity; and a recording telemetry sink. Every ID is a patterned
// placeholder and every name is fictional. Seeding is test setup, not an engine write.
//
// WORKING DEFAULT (open design question 12): the approval runner and the build pipeline hold their workload roles
// through Workload Grant rows of the client binding patch.
internal sealed class McpHarness
{
    public static readonly DateTime AsOf = new(2026, 11, 22, 9, 0, 0, DateTimeKind.Utc);

    public const string Tenant = "00000000-0000-4000-8000-0000000000aa";
    public const string Audience = "00000000-0000-4000-8000-0000000000ab";
    public const string ClaudeCode = "00000000-0000-4000-8000-0000000000ac";
    public const string OtherClient = "00000000-0000-4000-8000-0000000000ad";

    public const string RequesterGroup = "00000000-0000-4000-8000-000000000101";
    public const string MakerGroup = "00000000-0000-4000-8000-000000000102";
    public const string OwnerGroup = "00000000-0000-4000-8000-000000000103";
    public const string ChangeManagerGroup = "00000000-0000-4000-8000-000000000104";
    public const string PlatformAdminGroup = "00000000-0000-4000-8000-000000000105";
    public const string ConfigurationAdminGroup = "00000000-0000-4000-8000-000000000106";
    public const string ReaderGroup = "00000000-0000-4000-8000-000000000107";
    public const string ArchitectGroup = "00000000-0000-4000-8000-000000000108";

    // The owner requests, makes, owns and manages changes; the admin is a platform admin; the configuration admin; a
    // reader; a person with no role; the owner's deputy (no role); an agent user.
    public const string Owner = "00000000-0000-4000-8000-000000000201";
    public const string Admin = "00000000-0000-4000-8000-000000000202";
    public const string ConfigAdmin = "00000000-0000-4000-8000-000000000203";
    public const string Reader = "00000000-0000-4000-8000-000000000204";
    public const string NoRole = "00000000-0000-4000-8000-000000000205";
    public const string Deputy = "00000000-0000-4000-8000-000000000206";
    public const string Architect = "00000000-0000-4000-8000-000000000207";
    public const string Runner = "00000000-0000-4000-8000-000000000301";
    public const string Pipeline = "00000000-0000-4000-8000-000000000302";
    public const string AgentUser = "00000000-0000-4000-8000-000000000401";

    public const string Portfolio = "00000000-0000-4000-8000-000000000501";
    public const string Expense = "00000000-0000-4000-8000-000000000511";
    public const string Travel = "00000000-0000-4000-8000-000000000512";
    public const string Production = "00000000-0000-4000-8000-000000000521";
    public const string TestTarget = "00000000-0000-4000-8000-000000000522";
    public const string ExpenseApp = "00000000-0000-4000-8000-000000000531";
    public const string WikiLink = "00000000-0000-4000-8000-000000000541";
    public const string OwnerAssignment = "00000000-0000-4000-8000-000000000551";

    public const string TokenOwner = "mock-owner";
    public const string TokenAdmin = "mock-admin";
    public const string TokenConfigAdmin = "mock-config-admin";
    public const string TokenReader = "mock-reader";
    public const string TokenNoRole = "mock-no-role";
    public const string TokenArchitect = "mock-architect";
    public const string TokenAgent = "mock-agent-user";
    public const string TokenOtherClient = "mock-owner-other-client";
    public const string TokenExpired = "mock-expired";
    public const string TokenOtherAudience = "mock-other-audience";
    public const string TokenOtherTenant = "mock-other-tenant";
    public const string TokenNoScope = "mock-no-scope";
    public const string TokenAppOnly = "mock-app-only";

    private static readonly Lazy<string> LazyRoot = new(FindRoot);
    private static readonly Lazy<PresetCatalog> LazyCatalog = new(() => PresetCatalog.Load(new[] { Path.Combine(Root, "config", "presets") }));

    private McpHarness(InMemoryStore store, GuidIdRecordStore records, ManualClock clock, EngineRuntime runtime, InProcessBackend backend, McpServerCore server, RecordingTelemetry telemetry, FakeDirectory directory, FakeInventory inventory)
    {
        Store = store;
        Records = records;
        Clock = clock;
        Runtime = runtime;
        Backend = backend;
        Server = server;
        Telemetry = telemetry;
        Directory = directory;
        Inventory = inventory;
    }

    public static string Root => LazyRoot.Value;

    public static PresetCatalog Catalog => LazyCatalog.Value;

    public InMemoryStore Store { get; }

    public GuidIdRecordStore Records { get; }

    public ManualClock Clock { get; }

    public EngineRuntime Runtime { get; }

    public InProcessBackend Backend { get; }

    public McpServerCore Server { get; }

    public RecordingTelemetry Telemetry { get; }

    public FakeDirectory Directory { get; }

    public FakeInventory Inventory { get; }

    // Every envelope the harness received, by tool, for checks across the whole suite.
    public List<(string Tool, JsonTreeNode Envelope)> Envelopes { get; } = new();

    public static McpHarness Create(string? extraOperationsJson = null, bool withPorts = true, bool seedVersion = true)
    {
        var store = new InMemoryStore("mcp-fixture-store");
        var records = new GuidIdRecordStore(store);
        var clock = new ManualClock(AsOf);
        if (seedVersion)
        {
            store.SeedVersion(Compile(1, extraOperationsJson));
        }

        var runtime = new EngineRuntime(records, store, Catalog, clock: clock);
        var directory = new FakeDirectory();
        var inventory = new FakeInventory();
        var backend = new InProcessBackend(runtime, records, store, Catalog, new InMemoryRateLimitStore(), ContractSet.Embedded, new InProcessBackendOptions
        {
            RecordUrlBase = "https://product.example.org/records",
            Directory = withPorts ? directory : null,
            Inventory = withPorts ? inventory : null,
            Playbook = PlaybookIndex.FromFolder(Path.Combine(Root, "content", "playbook")),
        });
        var telemetry = new RecordingTelemetry();
        McpServerCore server = NewServer(backend, telemetry, clock);
        var harness = new McpHarness(store, records, clock, runtime, backend, server, telemetry, directory, inventory);
        harness.SeedDirectory();
        harness.SeedEstate();
        return harness;
    }

    // The harness is the host in these tests, so it sets the telemetry salt as a host reads it from its configuration.
    // The value is a fictional test setting, never a product default.
    public const string HostTelemetrySalt = "fixture-host-telemetry-salt-0001";

    public static McpServerCore NewServer(IGovernanceBackend backend, ITelemetrySink telemetry, ManualClock clock, string? telemetrySalt = HostTelemetrySalt)
    {
        return new McpServerCore(ContractSet.Embedded, Tokens(), backend, telemetry, new McpServerOptions
        {
            ResourceUrl = "https://mcp.example.org/mcp",
            AuthorizationServer = "https://login.microsoftonline.com/" + Tenant + "/v2.0",
            TelemetrySalt = telemetrySalt,
            ValidateOutputs = true,
            RethrowInternalErrors = true,
            CorrelationIds = McpServerOptions.Sequential(),
            UtcNow = () => clock.UtcNow,
        });
    }

    public static MockTokenValidator Tokens()
    {
        DateTime expires = AsOf.AddHours(8);
        return new MockTokenValidator(new TokenAudience(Audience, Tenant))
            .Add(TokenOwner, new MockToken(Owner, Audience, Tenant, ClaudeCode, expires) { GroupObjectIds = new[] { RequesterGroup, MakerGroup, OwnerGroup, ChangeManagerGroup } })
            .Add(TokenAdmin, new MockToken(Admin, Audience, Tenant, ClaudeCode, expires) { GroupObjectIds = new[] { PlatformAdminGroup } })
            .Add(TokenConfigAdmin, new MockToken(ConfigAdmin, Audience, Tenant, ClaudeCode, expires) { GroupObjectIds = new[] { ConfigurationAdminGroup, RequesterGroup } })
            .Add(TokenReader, new MockToken(Reader, Audience, Tenant, ClaudeCode, expires) { GroupObjectIds = new[] { ReaderGroup } })
            .Add(TokenNoRole, new MockToken(NoRole, Audience, Tenant, ClaudeCode, expires))
            .Add(TokenArchitect, new MockToken(Architect, Audience, Tenant, ClaudeCode, expires) { GroupObjectIds = new[] { ArchitectGroup } })
            .Add(TokenAgent, new MockToken(AgentUser, Audience, Tenant, ClaudeCode, expires) { PrincipalKind = PrincipalKind.AgentUser, GroupObjectIds = new[] { OwnerGroup } })
            .Add(TokenOtherClient, new MockToken(Owner, Audience, Tenant, OtherClient, expires) { GroupObjectIds = new[] { RequesterGroup, MakerGroup, OwnerGroup, ChangeManagerGroup } })
            .Add(TokenExpired, new MockToken(Owner, Audience, Tenant, ClaudeCode, AsOf.AddMinutes(-1)))
            .Add(TokenOtherAudience, new MockToken(Owner, "00000000-0000-4000-8000-0000000000ff", Tenant, ClaudeCode, expires))
            .Add(TokenOtherTenant, new MockToken(Owner, Audience, "00000000-0000-4000-8000-0000000000fe", ClaudeCode, expires))
            .Add(TokenNoScope, new MockToken(Owner, Audience, Tenant, ClaudeCode, expires) { Scopes = new[] { "User.Read" } })
            .Add(TokenAppOnly, new MockToken(Pipeline, Audience, Tenant, ClaudeCode, expires) { PrincipalKind = PrincipalKind.ServicePrincipal, IsAppOnly = true });
    }

    // Balanced plus the client binding patch (and any extra draft operations), validated in activation mode on the
    // harness date and compiled: the version activation would leave in force.
    public static CompiledSnapshot Compile(int versionNumber, string? extraOperationsJson = null)
    {
        ContentSource source = ContentSource.Preset("balanced").WithPatch(ConfigurationPatch.FromOperations(CanonicalJson.Parse("[" + ClientBindingOperations + "]"), PatchKind.ClientBinding, "client binding patch"));
        if (extraOperationsJson != null)
        {
            source = source.WithPatch(ConfigurationPatch.FromOperations(CanonicalJson.Parse(extraOperationsJson), PatchKind.Draft, "test operations"));
        }

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
        foreach (string group in new[] { RequesterGroup, MakerGroup, OwnerGroup, ChangeManagerGroup, PlatformAdminGroup, ConfigurationAdminGroup, ReaderGroup, ArchitectGroup })
        {
            yield return new KeyValuePair<string, PrincipalKind>(group, PrincipalKind.Group);
        }

        foreach (string person in new[] { Owner, Admin, ConfigAdmin, Reader, NoRole, Deputy, Architect })
        {
            yield return new KeyValuePair<string, PrincipalKind>(person, PrincipalKind.User);
        }

        yield return new KeyValuePair<string, PrincipalKind>(Runner, PrincipalKind.ServicePrincipal);
        yield return new KeyValuePair<string, PrincipalKind>(Pipeline, PrincipalKind.ServicePrincipal);
        yield return new KeyValuePair<string, PrincipalKind>(AgentUser, PrincipalKind.AgentUser);
    }

    private static string Binding(string role, string group)
    {
        return "{\"op\":\"upsert\",\"entity\":\"role-group-binding\",\"key\":\"" + role + "-group\",\"values\":{\"label\":\"" + role + " group\",\"roleKey\":\"" + role
            + "\",\"entraGroupObjectId\":\"" + group + "\",\"bindingMethod\":\"entra-app-role-assignment\",\"bindingDetail\":\"App role " + role + "\"}}";
    }

    private static string Grant(string key, string role, string principal)
    {
        return "{\"op\":\"upsert\",\"entity\":\"workload-grant\",\"key\":\"" + key + "\",\"values\":{\"label\":\"" + key + "\",\"principalObjectId\":\"" + principal
            + "\",\"workloadRoleKey\":\"" + role + "\",\"reason\":\"Fixture workload " + key + "\",\"usesFederatedCredential\":true,\"bindingDetail\":\"App role assignment " + key + "\",\"expiresOn\":\"2027-06-30\"}}";
    }

    private static string ClientBindingOperations => string.Join(",", new[]
    {
        Binding("requester", RequesterGroup),
        Binding("maker", MakerGroup),
        Binding("application-owner", OwnerGroup),
        Binding("change-manager", ChangeManagerGroup),
        Binding("platform-admin", PlatformAdminGroup),
        Binding("configuration-admin", ConfigurationAdminGroup),
        Binding("reader", ReaderGroup),
        Binding("architect", ArchitectGroup),
        Grant("approval-runner", "approval-writer", Runner),
        Grant("build-pipeline", "deployment-recorder", Pipeline),
    });

    // ---- Seeding ----

    private void SeedDirectory()
    {
        int n = 0;
        foreach (KeyValuePair<string, PrincipalKind> principal in DirectoryKinds())
        {
            n++;
            string kind = EngineContract.PrincipalKinds.ToWire(principal.Value);
            Store.Seed("principal-reference", Js.Obj(
                ("principalKind", JsonTreeNode.FromString(kind)),
                ("entraObjectId", JsonTreeNode.FromString(principal.Key)),
                ("displayLabel", JsonTreeNode.FromString("Fixture " + kind + " " + principal.Key.Substring(principal.Key.Length - 3))),
                ("isErased", JsonTreeNode.False)), id: "00000000-0000-4000-8000-000000000" + (600 + n).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private void SeedEstate()
    {
        Store.Seed("portfolio", "{\"name\":\"Fixture finance\",\"level\":\"department\",\"isActive\":true}", id: Portfolio);
        Store.Seed("business-application", "{\"name\":\"Fixture expense approvals\",\"recordNumber\":\"APP-0001\",\"purpose\":\"Approve expense claims for the finance team\",\"applicationKind\":\"power-platform-app\","
            + "\"portfolio\":\"" + Portfolio + "\",\"status\":\"app-registered\",\"statusMeaning\":\"registered\",\"tier\":\"tier-1\",\"lifecycleStage\":\"operate\","
            + "\"stageSince\":\"2026-01-05T00:00:00Z\",\"buildPath\":\"citizen\",\"usesAi\":false,\"configurationSetVersion\":1}", id: Expense);
        Store.Seed("business-application", "{\"name\":\"Fixture travel planner\",\"recordNumber\":\"APP-0002\",\"purpose\":\"Plan and book business travel\",\"applicationKind\":\"power-platform-app\","
            + "\"portfolio\":\"" + Portfolio + "\",\"status\":\"app-registered\",\"statusMeaning\":\"registered\",\"tier\":\"tier-2\",\"lifecycleStage\":\"operate\","
            + "\"stageSince\":\"2026-02-01T00:00:00Z\",\"configurationSetVersion\":1}", id: Travel);
        Store.Seed("owner-assignment", "{\"businessApplication\":\"" + Expense + "\",\"ownerRole\":\"business-owner\",\"principal\":\"" + Owner + "\",\"isDeputy\":false,\"startOn\":\"2026-01-05\"}", id: OwnerAssignment);
        Store.Seed("owner-assignment", "{\"businessApplication\":\"" + Expense + "\",\"ownerRole\":\"business-owner\",\"principal\":\"" + Deputy + "\",\"isDeputy\":true,\"deputyFor\":\"" + OwnerAssignment + "\",\"startOn\":\"2026-01-05\"}", id: "00000000-0000-4000-8000-000000000552");
        Store.Seed("owner-assignment", "{\"businessApplication\":\"" + Expense + "\",\"ownerRole\":\"technical-owner\",\"principal\":\"" + Owner + "\",\"isDeputy\":false,\"startOn\":\"2026-01-05\"}", id: "00000000-0000-4000-8000-000000000553");
        Store.Seed("owner-assignment", "{\"businessApplication\":\"" + Travel + "\",\"ownerRole\":\"business-owner\",\"principal\":\"" + Owner + "\",\"isDeputy\":false,\"startOn\":\"2026-02-01\"}", id: "00000000-0000-4000-8000-000000000554");
        Store.Seed("deployment-target", "{\"targetKind\":\"power-platform-environment\",\"targetStage\":\"production\",\"changePath\":\"pipeline-only\",\"linkKind\":\"power-platform-resource\","
            + "\"externalId\":\"fixture-env-prod\",\"labelAsSeen\":\"Fixture production\"}", id: Production);
        Store.Seed("deployment-target", "{\"targetKind\":\"power-platform-environment\",\"targetStage\":\"test\",\"changePath\":\"pipeline-only\",\"linkKind\":\"power-platform-resource\","
            + "\"externalId\":\"fixture-env-test\",\"labelAsSeen\":\"Fixture test\"}", id: TestTarget);
        Store.Seed("technical-component", "{\"businessApplication\":\"" + Expense + "\",\"componentKind\":\"canvas-app\",\"linkKind\":\"power-platform-resource\","
            + "\"externalId\":\"00000000-0000-0000-0000-000000000101\",\"url\":\"https://make.powerapps.com/environments/fixture/apps/101\",\"labelAsSeen\":\"Expense app\"}", id: ExpenseApp);
        Store.Seed("dependency", "{\"fromApplication\":\"" + Travel + "\",\"toApplication\":\"" + Expense + "\",\"dependencyType\":\"reads-data-from\"}", id: "00000000-0000-4000-8000-000000000561");
        Store.Seed("document-link", "{\"businessApplication\":\"" + Expense + "\",\"documentType\":\"wiki-page\",\"documentationRequirement\":\"wiki-page-all\",\"linkKind\":\"ado-wiki-page\","
            + "\"url\":\"https://devops.example.org/fixture/wiki/expense\",\"status\":\"document-current\",\"statusMeaning\":\"current\",\"documentOwner\":\"" + Owner + "\","
            + "\"lastUpdatedOn\":\"2026-06-01\",\"lastCheckedOn\":\"2026-06-01\"}", id: WikiLink);
    }

    // ---- Calls ----

    // A tools/call; returns the envelope (structured content). The server checks it against the output schema.
    public JsonTreeNode Call(string token, string tool, string argumentsJson = "{}", string? toolsets = null)
    {
        McpResponse response = Server.Handle(McpRequest.Call(1, "tools/call", Js.Obj(("name", JsonTreeNode.FromString(tool)), ("arguments", Js.Parse(argumentsJson))), "Bearer " + token, toolsets));
        Assert.Equal(200, response.HttpStatus);
        JsonTreeNode envelope = Js.Member(response.Result, "structuredContent") ?? throw new InvalidOperationException("No structured content: " + Js.Write(response.Body!));
        Envelopes.Add((tool, envelope));
        return envelope;
    }

    public JsonTreeNode Ok(string token, string tool, string argumentsJson = "{}", string? toolsets = null)
    {
        JsonTreeNode envelope = Call(token, tool, argumentsJson, toolsets);
        Assert.True(Js.Flag(envelope, "ok") == true, tool + " failed: " + Js.Write(envelope));
        return envelope;
    }

    public JsonTreeNode Fails(string token, string tool, string argumentsJson, string code, string? toolsets = null)
    {
        JsonTreeNode envelope = Call(token, tool, argumentsJson, toolsets);
        Assert.True(Js.Flag(envelope, "ok") == false, tool + " should fail with " + code + ": " + Js.Write(envelope));
        Assert.Equal(code, Js.Text(Js.Member(envelope, "error"), "code"));
        return envelope;
    }

    public McpResponse Rpc(string? token, string method, string? parametersJson = null, string? toolsets = null)
    {
        return Server.Handle(McpRequest.Call(7, method, parametersJson == null ? null : Js.Parse(parametersJson), token == null ? null : "Bearer " + token, toolsets));
    }

    public static JsonTreeNode Data(JsonTreeNode envelope)
    {
        return Js.Member(envelope, "data")!;
    }

    public static string Text(JsonTreeNode node, string path)
    {
        JsonTreeNode? current = node;
        foreach (string part in path.Split('.'))
        {
            current = Js.Member(current, part);
        }

        return current == null ? string.Empty : current.Kind == JsonTreeKind.String ? current.GetString() : Js.Write(current);
    }

    private static string FindRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "mcp", "contracts", "tools.json")) && System.IO.Directory.Exists(Path.Combine(dir.FullName, "config", "presets")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Cannot find src/ above " + AppContext.BaseDirectory + ".");
    }
}

// Telemetry the tests can read back.
internal sealed class RecordingTelemetry : ITelemetrySink
{
    public List<TelemetryEvent> Events { get; } = new();

    public void Track(TelemetryEvent telemetryEvent)
    {
        Events.Add(telemetryEvent);
    }
}

// A recorded Graph for tests: groups, members, users' state and direct assignments. No network.
internal sealed class FakeDirectory : IDirectoryReader
{
    public bool Down { get; set; }

    public Dictionary<string, DirectoryUserState> States { get; } = new(StringComparer.OrdinalIgnoreCase);

    public DirectoryAnswer<IReadOnlyList<DirectoryObject>> GroupMembers(BackendCaller asPerson, string groupObjectId)
    {
        if (Down)
        {
            return DirectoryAnswer<IReadOnlyList<DirectoryObject>>.Unavailable("Graph answered 503.");
        }

        IReadOnlyList<DirectoryObject> members = groupObjectId == McpHarness.ChangeManagerGroup || groupObjectId == McpHarness.OwnerGroup
            ? new[] { new DirectoryObject(McpHarness.Owner, "Fixture user 201") }
            : groupObjectId == McpHarness.PlatformAdminGroup ? new[] { new DirectoryObject(McpHarness.Admin, "Fixture user 202") } : Array.Empty<DirectoryObject>();
        return DirectoryAnswer<IReadOnlyList<DirectoryObject>>.Of(members);
    }

    public DirectoryAnswer<DirectoryObject?> Group(BackendCaller asPerson, string groupObjectId)
    {
        return Down ? DirectoryAnswer<DirectoryObject?>.Unavailable("Graph answered 503.") : DirectoryAnswer<DirectoryObject?>.Of(new DirectoryObject(groupObjectId, "Fixture group " + groupObjectId.Substring(groupObjectId.Length - 3), "group"));
    }

    public DirectoryAnswer<DirectoryUserState> UserState(BackendCaller asPerson, string objectId)
    {
        if (Down)
        {
            return DirectoryAnswer<DirectoryUserState>.Unavailable("Graph answered 503.");
        }

        return DirectoryAnswer<DirectoryUserState>.Of(States.TryGetValue(objectId, out DirectoryUserState? state) ? state : new DirectoryUserState("enabled"));
    }

    public DirectoryAnswer<IReadOnlyList<DirectAssignment>> DirectAssignments(BackendCaller asPerson)
    {
        return Down
            ? DirectoryAnswer<IReadOnlyList<DirectAssignment>>.Unavailable("Graph answered 503.")
            : DirectoryAnswer<IReadOnlyList<DirectAssignment>>.Of(new[] { new DirectAssignment(new DirectoryObject(McpHarness.Pipeline, "Fixture build pipeline", "service-principal"), "deployment-recorder", "app role assignment on the server's app") });
    }
}

// A recorded Power Platform inventory for tests: one registered app and one unregistered app. No network.
internal sealed class FakeInventory : IInventoryReader
{
    public bool Down { get; set; }

    public DirectoryAnswer<InventoryPage> Query(BackendCaller asAdmin, string estateKey, IReadOnlyList<string>? environmentIds, string? skipToken)
    {
        if (Down)
        {
            return DirectoryAnswer<InventoryPage>.Unavailable("The inventory answered 503.");
        }

        return DirectoryAnswer<InventoryPage>.Of(new InventoryPage(
            new[]
            {
                new InventoryItem("00000000-0000-0000-0000-000000000101", "microsoft.powerapps/canvasapps", "Expense app", "Fixture production", "canvas-app"),
                new InventoryItem("00000000-0000-0000-0000-000000000102", "microsoft.powerapps/canvasapps", "Budget sheet", "Fixture production", "canvas-app", McpHarness.Owner),
            },
            null,
            "Power Platform administrator"));
    }
}
