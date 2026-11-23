using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Runtime;
using Product.Engine.Runtime.Callers;
using Product.Engine.Testing.InMemory;
using Product.Mcp.Core.Backend;
using Product.Mcp.Core.Backend.InProcess;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Envelope;
using Product.Mcp.Core.Identity;
using Product.Mcp.Core.Json;
using Product.Mcp.Core.Protocol;
using Product.Mcp.Core.Resources;
using Product.Mcp.Testing;

namespace Product.Mcp.Core.Tests.Walkthrough;

// The fictional world of the scripted walkthrough (src/demo/walkthrough/README.md): the MCP server core in process on the
// in-memory store, Balanced plus the harness's client binding patch as version 1, the mock identity with named fictional
// people who hold their roles through mock group membership, two workload identities with Workload Grants, a small field
// operations estate and a fictional inventory page. No network, no listener, no sign-in. Every ID is a patterned
// placeholder and every name is fictional (the people and apps of the page harness fixtures, ui/harness/fixtures).
//
// WORKING DEFAULT (open design question 12): the approval runner and the release pipeline hold their workload roles
// through the Workload Grant rows of the harness's client binding patch.
internal sealed class WalkthroughWorld
{
    // Monday 23 November 2026, 09:00 UTC. The clock moves on as the story does.
    public static readonly DateTime Start = new(2026, 11, 23, 9, 0, 0, DateTimeKind.Utc);

    // People. The groups are the harness's role groups, so the client binding patch binds the roles.
    public const string Priya = "00000000-0000-4000-8000-000000000211";
    public const string Nadia = "00000000-0000-4000-8000-000000000212";
    public const string Owen = "00000000-0000-4000-8000-000000000213";
    public const string Mateo = "00000000-0000-4000-8000-000000000214";
    public const string Ines = "00000000-0000-4000-8000-000000000215";

    // Workloads: the approval runner holds approval-writer, the release pipeline deployment-recorder (harness grants).
    public const string Runner = McpHarness.Runner;
    public const string Pipeline = McpHarness.Pipeline;

    public const string TokenPriya = "mock-priya-castell";
    public const string TokenNadia = "mock-nadia-ferreira";
    public const string TokenOwen = "mock-owen-achterberg";
    public const string TokenMateo = "mock-mateo-brandt";
    public const string TokenPipeline = "mock-release-pipeline-app-only";

    // The estate before the story starts.
    public const string FieldOperations = "00000000-0000-4000-8000-000000000501";
    public const string InspectionScheduler = "00000000-0000-4000-8000-000000000511";
    public const string RoutePlanner = "00000000-0000-4000-8000-000000000512";
    public const string CrewRoster = "00000000-0000-4000-8000-000000000513";
    public const string CrewShiftMobile = "00000000-0000-4000-8000-000000000514";
    public const string FieldOpsTest = "00000000-0000-4000-8000-000000000521";
    public const string FieldOpsProd = "00000000-0000-4000-8000-000000000522";

    // Component external IDs in the Power Platform inventory (fictional).
    public const string SchedulerCanvas = "00000000-0000-0000-0000-000000000201";
    public const string RoutePlannerApp = "00000000-0000-0000-0000-000000000202";
    public const string FieldInspectionsCanvas = "00000000-0000-0000-0000-000000000209";
    public const string OvertimeCanvas = "00000000-0000-0000-0000-000000000210";

    public const string ClaudeCode = McpHarness.ClaudeCode;

    private int _rpcId;

    private WalkthroughWorld(InMemoryStore store, GuidIdRecordStore records, ManualClock clock, EngineRuntime runtime, McpServerCore server)
    {
        Store = store;
        Records = records;
        Clock = clock;
        Runtime = runtime;
        Server = server;
    }

    public InMemoryStore Store { get; }

    public GuidIdRecordStore Records { get; }

    public ManualClock Clock { get; }

    public EngineRuntime Runtime { get; }

    public McpServerCore Server { get; }

    public WalkthroughRecorder Recorder { get; } = new();

    public static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        [Priya] = "Priya Castell",
        [Nadia] = "Nadia Ferreira",
        [Owen] = "Owen Achterberg",
        [Mateo] = "Mateo Brandt",
        [Ines] = "Ines Varga",
        [Runner] = "Approval runner",
        [Pipeline] = "Field ops release pipeline",
        [McpHarness.RequesterGroup] = "Product requesters",
        [McpHarness.MakerGroup] = "Field ops makers",
        [McpHarness.OwnerGroup] = "Application owners",
        [McpHarness.ChangeManagerGroup] = "Change managers",
        [McpHarness.PlatformAdminGroup] = "Platform admins",
        [McpHarness.ConfigurationAdminGroup] = "Configuration admins",
        [McpHarness.ReaderGroup] = "Portfolio readers",
        [McpHarness.ArchitectGroup] = "Architects",
    };

    // Claude Code on the Known Client list (proposed C45), so its label reaches the stamped Client App column.
    private const string KnownClients = "[{\"op\":\"upsert\",\"entity\":\"known-client\",\"key\":\"claude-code\",\"values\":{\"label\":\"Claude Code\",\"clientIdentifier\":\"" + McpHarness.ClaudeCode + "\",\"isAiClient\":true,\"isAllowed\":true}}]";

    public static WalkthroughWorld Create()
    {
        var store = new InMemoryStore("walkthrough-store");
        var records = new GuidIdRecordStore(store);
        var clock = new ManualClock(Start);
        store.SeedVersion(McpHarness.Compile(1, KnownClients));
        var runtime = new EngineRuntime(records, store, McpHarness.Catalog, clock: clock);
        var backend = new InProcessBackend(runtime, records, store, McpHarness.Catalog, new InMemoryRateLimitStore(), ContractSet.Embedded, new InProcessBackendOptions
        {
            RecordUrlBase = "https://product.example.org/records",
            Inventory = new WalkthroughInventory(),
            Playbook = PlaybookIndex.FromFolder(Path.Combine(McpHarness.Root, "content", "playbook")),
        });
        McpServerCore server = new(ContractSet.Embedded, Tokens(), backend, new RecordingTelemetry(), new McpServerOptions
        {
            ResourceUrl = "https://mcp.example.org/mcp",
            AuthorizationServer = "https://login.microsoftonline.com/" + McpHarness.Tenant + "/v2.0",
            TelemetrySalt = McpHarness.HostTelemetrySalt,
            ValidateOutputs = true,
            RethrowInternalErrors = true,
            CorrelationIds = McpServerOptions.Sequential(),
            UtcNow = () => clock.UtcNow,
        });
        var world = new WalkthroughWorld(store, records, clock, runtime, server);
        world.SeedDirectory();
        world.SeedEstate();
        return world;
    }

    // The mock identity: opaque "mock-" values mapped to claims. Roles come from group membership only.
    private static MockTokenValidator Tokens()
    {
        DateTime expires = new(2026, 11, 30, 0, 0, 0, DateTimeKind.Utc);
        return new MockTokenValidator(new TokenAudience(McpHarness.Audience, McpHarness.Tenant))
            .Add(TokenPriya, new MockToken(Priya, McpHarness.Audience, McpHarness.Tenant, ClaudeCode, expires) { GroupObjectIds = new[] { McpHarness.RequesterGroup, McpHarness.MakerGroup } })
            .Add(TokenNadia, new MockToken(Nadia, McpHarness.Audience, McpHarness.Tenant, ClaudeCode, expires) { GroupObjectIds = new[] { McpHarness.OwnerGroup } })
            .Add(TokenOwen, new MockToken(Owen, McpHarness.Audience, McpHarness.Tenant, ClaudeCode, expires) { GroupObjectIds = new[] { McpHarness.PlatformAdminGroup } })
            .Add(TokenMateo, new MockToken(Mateo, McpHarness.Audience, McpHarness.Tenant, ClaudeCode, expires) { GroupObjectIds = new[] { McpHarness.ReaderGroup } })
            .Add(TokenPipeline, new MockToken(Pipeline, McpHarness.Audience, McpHarness.Tenant, ClaudeCode, expires) { PrincipalKind = PrincipalKind.ServicePrincipal, IsAppOnly = true });
    }

    // The groups people belong to, for the engine-path calls (the same claims the tokens carry).
    public static IReadOnlyList<string> GroupsOf(string person)
    {
        return person == Priya ? new[] { McpHarness.RequesterGroup, McpHarness.MakerGroup }
            : person == Nadia ? new[] { McpHarness.OwnerGroup }
            : person == Owen ? new[] { McpHarness.PlatformAdminGroup }
            : person == Mateo ? new[] { McpHarness.ReaderGroup }
            : Array.Empty<string>();
    }

    // ---- Seeding: test setup, never an engine write ----

    private void SeedDirectory()
    {
        int n = 0;
        foreach (KeyValuePair<string, PrincipalKind> principal in Principals())
        {
            n++;
            Store.Seed("principal-reference", Js.Obj(
                ("principalKind", JsonTreeNode.FromString(EngineContract.PrincipalKinds.ToWire(principal.Value))),
                ("entraObjectId", JsonTreeNode.FromString(principal.Key)),
                ("displayLabel", JsonTreeNode.FromString(Names[principal.Key])),
                ("isErased", JsonTreeNode.False)), id: "00000000-0000-4000-8000-000000000" + (700 + n).ToString(CultureInfo.InvariantCulture));
        }
    }

    private static IEnumerable<KeyValuePair<string, PrincipalKind>> Principals()
    {
        foreach (string group in new[] { McpHarness.RequesterGroup, McpHarness.MakerGroup, McpHarness.OwnerGroup, McpHarness.ChangeManagerGroup, McpHarness.PlatformAdminGroup, McpHarness.ConfigurationAdminGroup, McpHarness.ReaderGroup, McpHarness.ArchitectGroup })
        {
            yield return new KeyValuePair<string, PrincipalKind>(group, PrincipalKind.Group);
        }

        foreach (string person in new[] { Priya, Nadia, Owen, Mateo, Ines })
        {
            yield return new KeyValuePair<string, PrincipalKind>(person, PrincipalKind.User);
        }

        yield return new KeyValuePair<string, PrincipalKind>(Runner, PrincipalKind.ServicePrincipal);
        yield return new KeyValuePair<string, PrincipalKind>(Pipeline, PrincipalKind.ServicePrincipal);
    }

    private void SeedEstate()
    {
        Store.Seed("portfolio", "{\"name\":\"Field operations\",\"level\":\"department\",\"isActive\":true}", id: FieldOperations);
        SeedApp(InspectionScheduler, "Inspection Scheduler", "APP-0101", "Plan site inspections and send checklists to inspectors", "tier-2", "2025-03-03");
        SeedApp(RoutePlanner, "Route Planner", "APP-0102", "Plan the daily routes of the field crews", "tier-1", "2025-06-02");
        SeedApp(CrewRoster, "Crew Roster", "APP-0103", "Keep the crew shifts and skills", "tier-2", "2025-01-13");
        SeedApp(CrewShiftMobile, "Crew Shift Mobile", "APP-0104", "Show each crew member their shifts on the phone", "tier-1", "2025-09-01");
        SeedOwner(InspectionScheduler, "business-owner", Nadia, "551");
        SeedOwner(InspectionScheduler, "technical-owner", Priya, "552");
        SeedOwner(RoutePlanner, "business-owner", Ines, "553");
        SeedOwner(CrewRoster, "business-owner", Ines, "554");
        SeedOwner(CrewShiftMobile, "business-owner", Ines, "555");
        Store.Seed("deployment-target", "{\"targetKind\":\"power-platform-environment\",\"targetStage\":\"test\",\"changePath\":\"pipeline-only\",\"linkKind\":\"power-platform-resource\","
            + "\"externalId\":\"field-ops-test\",\"labelAsSeen\":\"Field Ops Test\"}", id: FieldOpsTest);
        Store.Seed("deployment-target", "{\"targetKind\":\"power-platform-environment\",\"targetStage\":\"production\",\"changePath\":\"pipeline-only\",\"linkKind\":\"power-platform-resource\","
            + "\"externalId\":\"field-ops-prod\",\"labelAsSeen\":\"Field Ops Prod\"}", id: FieldOpsProd);
        Store.Seed("technical-component", "{\"businessApplication\":\"" + InspectionScheduler + "\",\"componentKind\":\"canvas-app\",\"linkKind\":\"power-platform-resource\","
            + "\"externalId\":\"" + SchedulerCanvas + "\",\"url\":\"https://make.powerapps.com/environments/field-ops-prod/apps/201\",\"labelAsSeen\":\"Inspection Scheduler\"}", id: "00000000-0000-4000-8000-000000000531");
        Store.Seed("technical-component", "{\"businessApplication\":\"" + RoutePlanner + "\",\"componentKind\":\"model-driven-app\",\"linkKind\":\"power-platform-resource\","
            + "\"externalId\":\"" + RoutePlannerApp + "\",\"url\":\"https://make.powerapps.com/environments/field-ops-prod/apps/202\",\"labelAsSeen\":\"Route Planner\"}", id: "00000000-0000-4000-8000-000000000532");
        Store.Seed("dependency", "{\"fromApplication\":\"" + RoutePlanner + "\",\"toApplication\":\"" + InspectionScheduler + "\",\"dependencyType\":\"reads-data-from\"}", id: "00000000-0000-4000-8000-000000000561");
        Store.Seed("dependency", "{\"fromApplication\":\"" + CrewRoster + "\",\"toApplication\":\"" + InspectionScheduler + "\",\"dependencyType\":\"calls-api-of\"}", id: "00000000-0000-4000-8000-000000000562");
        Store.Seed("dependency", "{\"fromApplication\":\"" + CrewShiftMobile + "\",\"toApplication\":\"" + CrewRoster + "\",\"dependencyType\":\"reads-data-from\"}", id: "00000000-0000-4000-8000-000000000563");
    }

    private void SeedApp(string id, string name, string number, string purpose, string tier, string since)
    {
        Store.Seed("business-application", "{\"name\":\"" + name + "\",\"recordNumber\":\"" + number + "\",\"purpose\":\"" + purpose + "\",\"applicationKind\":\"power-platform-app\","
            + "\"portfolio\":\"" + FieldOperations + "\",\"status\":\"app-registered\",\"statusMeaning\":\"registered\",\"tier\":\"" + tier + "\",\"lifecycleStage\":\"operate\","
            + "\"stageSince\":\"" + since + "T00:00:00Z\",\"buildPath\":\"citizen\",\"usesAi\":false,\"configurationSetVersion\":1}", id: id);
    }

    private void SeedOwner(string app, string role, string person, string suffix)
    {
        Store.Seed("owner-assignment", "{\"businessApplication\":\"" + app + "\",\"ownerRole\":\"" + role + "\",\"principal\":\"" + person + "\",\"isDeputy\":false,\"startOn\":\"2025-01-06\"}", id: "00000000-0000-4000-8000-000000000" + suffix);
    }

    // ---- Calls, each recorded ----

    // One MCP request as Claude Code sends it: a JSON-RPC message with the person's bearer token.
    public McpResponse Mcp(string step, string actor, string token, string method, string? parametersJson)
    {
        int id = ++_rpcId;
        var request = McpRequest.Call(id, method, parametersJson == null ? null : Js.Parse(parametersJson), "Bearer " + token);
        McpResponse response = Server.Handle(request);
        Recorder.AddMcp(step, actor, token, request, response, Clock.UtcNow);
        return response;
    }

    // A tools/call; returns the envelope (structured content), or null when the answer is not an envelope.
    public JsonTreeNode? Call(string step, string actor, string token, string tool, string argumentsJson)
    {
        McpResponse response = Mcp(step, actor, token, "tools/call", "{\"name\":\"" + tool + "\",\"arguments\":" + argumentsJson + "}");
        return Js.Member(response.Result, "structuredContent");
    }

    // An engine-path call that is not MCP (the approval runner's write, the record endpoint's engine entry, a write at
    // the engine's table door). The request is what the caller sends; the answer is the runtime outcome.
    public RuntimeOutcome Engine(string step, string actor, string channel, string entryPoint, CallerIdentity caller, JsonNode request, Func<RuntimeOutcome> call, Func<RuntimeOutcome, McpError>? map = null)
    {
        RuntimeOutcome outcome = call();
        Recorder.AddEngine(step, actor, channel, entryPoint, caller, request, outcome, Clock.UtcNow, map == null || outcome.Succeeded ? null : map(outcome));
        return outcome;
    }
}

// The inventory page the gap check reads as the platform admin: two registered apps, the new app, and one app nobody
// registered. Fictional, no network.
internal sealed class WalkthroughInventory : IInventoryReader
{
    public DirectoryAnswer<InventoryPage> Query(BackendCaller asAdmin, string estateKey, IReadOnlyList<string>? environmentIds, string? skipToken)
    {
        return DirectoryAnswer<InventoryPage>.Of(new InventoryPage(
            new[]
            {
                new InventoryItem(WalkthroughWorld.SchedulerCanvas, "microsoft.powerapps/canvasapps", "Inspection Scheduler", "Field Ops Prod", "canvas-app"),
                new InventoryItem(WalkthroughWorld.RoutePlannerApp, "microsoft.powerapps/apps", "Route Planner", "Field Ops Prod", "model-driven-app"),
                new InventoryItem(WalkthroughWorld.FieldInspectionsCanvas, "microsoft.powerapps/canvasapps", "Field Inspections", "Field Ops Prod", "canvas-app"),
                new InventoryItem(WalkthroughWorld.OvertimeCanvas, "microsoft.powerapps/canvasapps", "Crew Overtime Tracker", "Field Ops Prod", "canvas-app", WalkthroughWorld.Priya),
            },
            null,
            "Power Platform administrator"));
    }
}

// Every exchange of the walk, in order, as JSON: the request and the answer as sent, with the outcome digest (ok, the
// fixed error code, the reason codes) that the test compares with the checked-in transcript.
internal sealed class WalkthroughRecorder
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public List<JsonObject> Exchanges { get; } = new();

    public void AddMcp(string step, string actor, string token, McpRequest request, McpResponse response, DateTime at)
    {
        JsonNode? body = response.Body == null ? null : JsonNode.Parse(Js.Write(response.Body));
        JsonNode? envelope = body?["result"]?["structuredContent"];
        string method = Js.Text(request.Message, "method")!;
        var reasons = new List<string>();
        string? errorCode = null;
        bool? ok = null;
        if (envelope != null)
        {
            ok = envelope["ok"]?.GetValue<bool>();
            errorCode = envelope["error"]?["code"]?.GetValue<string>();
            foreach (JsonNode? warning in envelope["warnings"]?.AsArray() ?? new JsonArray())
            {
                if (warning?["code"]?.GetValue<string>() is string code)
                {
                    reasons.Add(code);
                }
            }
        }
        else if (body?["error"] != null)
        {
            ok = false;
            // An HTTP-level refusal (such as an app-only token) carries the envelope in the JSON-RPC error data.
            errorCode = body["error"]!["data"]?["error"]?["code"]?.GetValue<string>() ?? "jsonrpc " + body["error"]!["code"]!.ToJsonString();
        }
        else if (body?["result"] != null)
        {
            ok = true;
        }

        if (method == "tools/list" && body?["result"]?["tools"] is JsonArray tools)
        {
            // The published schemas are long; the transcript keeps each tool's name, title, annotations and the size of
            // its two schemas. The full list is what tools/list returns (mcp/contracts/tools).
            var slim = new JsonArray();
            foreach (JsonNode? tool in tools)
            {
                slim.Add(new JsonObject
                {
                    ["name"] = tool!["name"]!.GetValue<string>(),
                    ["title"] = tool["title"]?.DeepClone(),
                    ["annotations"] = tool["annotations"]?.DeepClone(),
                    ["schemaCharacters"] = (tool["inputSchema"]?.ToJsonString().Length ?? 0) + (tool["outputSchema"]?.ToJsonString().Length ?? 0),
                });
            }

            body["result"]!["tools"] = slim;
            body["result"]!["note"] = "inputSchema, outputSchema and description of each tool are left out of this transcript; schemaCharacters gives their size.";
        }

        var headers = new JsonObject { ["Authorization"] = "Bearer " + token };
        Exchanges.Add(new JsonObject
        {
            ["step"] = step,
            ["actor"] = actor,
            ["channel"] = "mcp",
            ["entryPoint"] = method == "tools/call" ? "tools/call " + Js.Text(Js.Member(request.Message, "params"), "name") : method,
            ["at"] = at.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["request"] = new JsonObject { ["headers"] = headers, ["body"] = JsonNode.Parse(Js.Write(request.Message)) },
            ["response"] = new JsonObject { ["httpStatus"] = response.HttpStatus, ["body"] = body },
            ["outcome"] = Digest(ok, errorCode, reasons),
        });
    }

    public void AddEngine(string step, string actor, string channel, string entryPoint, CallerIdentity caller, JsonNode request, RuntimeOutcome outcome, DateTime at, McpError? mapped = null)
    {
        var reasons = new JsonArray();
        foreach (Reason reason in outcome.Reasons)
        {
            reasons.Add(new JsonObject
            {
                ["code"] = reason.Code,
                ["severity"] = reason.Severity.ToString().ToLowerInvariant(),
                ["message"] = reason.Message,
                ["entity"] = reason.Entity,
                ["ref"] = reason.Ref,
            });
        }

        var writes = new JsonArray();
        foreach (AppliedWrite write in outcome.Writes)
        {
            writes.Add(new JsonObject
            {
                ["entity"] = write.Entity,
                ["operation"] = EngineContract.WriteOperations.ToWire(write.Operation),
                ["rowId"] = write.RowId,
                ["effect"] = write.Effect.HasValue ? EngineContract.EffectKinds.ToWire(write.Effect.Value) : null,
                ["columns"] = JsonNode.Parse(Js.Write(write.Columns)),
            });
        }

        var answer = new JsonObject
        {
            ["outcome"] = outcome.Outcome,
            ["succeeded"] = outcome.Succeeded,
            ["rowId"] = outcome.RowId,
            ["created"] = outcome.Created,
            ["configurationVersion"] = outcome.ConfigurationVersion,
            ["reasons"] = reasons,
            ["writes"] = writes,
            ["payload"] = outcome.Payload == null ? null : JsonNode.Parse(Js.Write(outcome.Payload)),
        };
        if (mapped != null)
        {
            answer["mcpError"] = JsonNode.Parse(Js.Write(mapped.ToJson()));
        }

        Exchanges.Add(new JsonObject
        {
            ["step"] = step,
            ["actor"] = actor,
            ["channel"] = channel,
            ["entryPoint"] = entryPoint,
            ["at"] = at.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["request"] = new JsonObject
            {
                ["caller"] = new JsonObject
                {
                    ["principalId"] = caller.PrincipalId,
                    ["principalKind"] = EngineContract.PrincipalKinds.ToWire(caller.PrincipalKind),
                    ["entry"] = caller.Entry.ToString(),
                    ["groups"] = new JsonArray(caller.GroupObjectIds.Select(g => (JsonNode)JsonValue.Create(g)!).ToArray()),
                    ["clientApp"] = caller.ClientApp,
                },
                ["body"] = request,
            },
            ["response"] = answer,
            ["outcome"] = Digest(outcome.Succeeded, mapped?.Code, outcome.Reasons.Select(r => r.Code)),
        });
    }

    // A seed between steps: test setup the runner writes straight into the in-memory store, never an engine write.
    public void AddSeed(string step, string why, JsonNode rows, DateTime at)
    {
        Exchanges.Add(new JsonObject
        {
            ["step"] = step,
            ["actor"] = "the runner (test setup)",
            ["channel"] = "seed",
            ["entryPoint"] = "InMemoryStore.Seed and Tamper",
            ["at"] = at.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["request"] = new JsonObject { ["why"] = why, ["rows"] = rows },
            ["response"] = null,
            ["outcome"] = Digest(true, null, Array.Empty<string>()),
        });
    }

    private static JsonObject Digest(bool? ok, string? errorCode, IEnumerable<string> reasons)
    {
        return new JsonObject
        {
            ["ok"] = ok,
            ["errorCode"] = errorCode,
            ["reasonCodes"] = new JsonArray(reasons.Distinct(StringComparer.Ordinal).OrderBy(r => r, StringComparer.Ordinal).Select(r => (JsonNode)JsonValue.Create(r)!).ToArray()),
        };
    }

    public static string Write(JsonNode node)
    {
        return node.ToJsonString(Pretty).Replace("\r\n", "\n") + "\n";
    }
}
