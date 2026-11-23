using System;
using System.IO;
using System.Text.Json.Nodes;
using Product.Engine.Core.Canonical;
using Product.Engine.Runtime;
using Product.Engine.Testing.InMemory;
using Product.Mcp.Core;
using Product.Mcp.Core.Backend.InProcess;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Identity;
using Product.Mcp.Core.Json;
using Product.Mcp.Core.Protocol;
using Product.Mcp.Core.Resources;
using Product.Mcp.Core.Telemetry;
using Product.Mcp.Testing;

namespace Product.Azure.Tests;

// The in-process run the HTTP host is compared with: the MCP server core and its in-process backend on the in-memory
// store, with the mock identity, exactly as the MCP core's walkthrough world builds it, seeded with the same demo seed
// as the HTTP host's SQLite file. No HTTP, no ASP.NET Core, no SQLite.
internal sealed class InProcessWorld
{
    private InProcessWorld(InMemoryStore store, GuidIdRecordStore records, ManualClock clock, EngineRuntime runtime, McpServerCore server)
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

    public static InProcessWorld Create()
    {
        var store = new InMemoryStore("walkthrough-store");
        var records = new GuidIdRecordStore(store);
        var clock = new ManualClock(DemoSeed.Start);
        DemoSeed.Into(store);
        var runtime = new EngineRuntime(records, store, DemoSeed.Catalog, clock: clock);
        var backend = new InProcessBackend(runtime, records, store, DemoSeed.Catalog, new InMemoryRateLimitStore(), ContractSet.Embedded, new InProcessBackendOptions
        {
            RecordUrlBase = "https://product.example.org/records",
            Inventory = new WalkthroughInventory(),
            Playbook = PlaybookIndex.FromFolder(Path.Combine(TestPaths.Source, "content", "playbook")),
        });
        var server = new McpServerCore(ContractSet.Embedded, Tokens(), backend, new NoTelemetry(), new McpServerOptions
        {
            ResourceUrl = "https://mcp.example.org/mcp",
            AuthorizationServer = "https://login.microsoftonline.com/" + Personas.Tenant + "/v2.0",
            TelemetrySalt = "fixture-host-telemetry-salt-0001",
            ValidateOutputs = true,
            RethrowInternalErrors = true,
            CorrelationIds = McpServerOptions.Sequential(),
            UtcNow = () => clock.UtcNow,
        });
        return new InProcessWorld(store, records, clock, runtime, server);
    }

    // One JSON-RPC message with the persona's mock bearer token. Returns the HTTP status and the body as JSON text.
    public (int Status, string? Body) Handle(Persona? persona, string message)
    {
        McpResponse response = Server.Handle(new McpRequest(CanonicalJson.Parse(message), persona == null ? null : "Bearer " + persona.MockToken));
        return (response.HttpStatus, response.Body == null ? null : Js.Write(response.Body));
    }

    public (int Status, string? Body) Handle(Persona? persona, JsonNode message)
    {
        return Handle(persona, message.ToJsonString());
    }

    private static MockTokenValidator Tokens()
    {
        DateTime expires = new(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        var tokens = new MockTokenValidator(new TokenAudience(Personas.Audience, Personas.Tenant));
        foreach (Persona persona in Personas.All)
        {
            tokens.Add(persona.MockToken, persona.Token(expires));
        }

        return tokens;
    }

    private sealed class NoTelemetry : ITelemetrySink
    {
        public void Track(TelemetryEvent telemetryEvent)
        {
        }
    }
}
