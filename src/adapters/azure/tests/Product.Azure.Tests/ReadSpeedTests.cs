using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Product.Azure.Host.Seeding;
using Product.Engine.Core.Canonical;
using Product.Engine.Runtime;
using Product.Engine.Testing.InMemory;
using Product.Mcp.Core;
using Product.Mcp.Core.Backend.InProcess;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Identity;
using Product.Mcp.Core.Json;
using Product.Mcp.Core.Protocol;
using Product.Mcp.Core.Telemetry;
using Product.Mcp.Testing;
using Xunit;

namespace Product.Azure.Tests;

// The three heaviest reads of the web app (get_portfolio_summary for Overview, get_my_queue for My work, find_applications
// for the applications list) on the SQLite store, counting the database reads each makes: every read is one round trip
// on Azure SQL, so the count is what a page costs over a slow path.
public sealed class ReadSpeedTests
{
    // Wednesday 25 November 2026, 09:00 UTC: inside the demo estate's story weeks.
    private static readonly DateTime AsOf = new(2026, 11, 25, 9, 0, 0, DateTimeKind.Utc);

    // The reads the pages make, as the people who make them.
    internal static readonly (string Name, Persona Persona, string Tool, string Arguments)[] Reads =
    {
        ("Overview: get_portfolio_summary", Personas.Mateo, "get_portfolio_summary", "{\"includeOwnerGaps\":true}"),
        ("My work (owner): get_my_queue", Personas.Nadia, "get_my_queue", "{}"),
        ("My work (platform admin): get_my_queue", Personas.Owen, "get_my_queue", "{}"),
        ("Applications: find_applications", Personas.Priya, "find_applications", "{}"),
        ("Applications by owner: find_applications", Personas.Priya, "find_applications", "{\"ownerObjectId\":\"" + Personas.NadiaId + "\"}"),
    };

    // The other list reads the pages make, checked for the same answers with the batching on and off.
    private static readonly (Persona Persona, string Tool, string Arguments)[] MoreReads =
    {
        (Personas.Mateo, "get_portfolio_summary", "{\"portfolioId\":\"" + Personas.FieldOperations + "\",\"includeOwnerGaps\":true}"),
        (Personas.Nadia, "get_my_queue", "{\"kinds\":[\"approval\",\"review\"]}"),
        (Personas.Priya, "find_applications", "{\"text\":\"portal\",\"top\":50}"),
        (Personas.Priya, "find_applications", "{\"usesAi\":true,\"buildPathKeys\":[\"fusion\",\"pro\"]}"),
        (Personas.Owen, "list_data_uses", "{\"top\":100}"),
        (Personas.Owen, "list_data_uses", "{\"domainKey\":\"people\",\"states\":[\"allowed\",\"needed\"]}"),
        (Personas.Owen, "list_data_mappings", "{\"top\":100}"),
        (Personas.Owen, "get_data_domain", "{\"domainKey\":\"payroll\"}"),
        (Personas.Owen, "get_pipeline_map", "{}"),
        (Personas.Owen, "get_deployments", "{\"portfolioId\":\"" + Personas.FieldOperations + "\",\"latestOnly\":false,\"top\":100}"),
        (Personas.Owen, "get_change_schedule", "{\"portfolioId\":\"" + Personas.FieldOperations + "\",\"from\":\"2026-11-01\",\"to\":\"2027-01-31\",\"top\":100}"),
        (Personas.Owen, "list_documents", "{\"top\":100}"),
    };

    // The most database reads each heavy read may make (document 25, B25-23). The same ceiling holds on the demo estate
    // and on the estate grown to ten times its size: a read's cost no longer grows with the number of applications.
    private static readonly Dictionary<string, long> Ceilings = new(StringComparer.Ordinal)
    {
        ["Overview: get_portfolio_summary"] = 25,
        ["My work (owner): get_my_queue"] = 34,
        ["My work (platform admin): get_my_queue"] = 34,
        ["Applications: find_applications"] = 8,
        ["Applications by owner: find_applications"] = 8,
    };

    [Fact]
    public void TheHeavyReadsMakeAFewDatabaseReadsWhateverTheSizeOfTheEstate()
    {
        foreach (int size in new[] { 0, 480 })
        {
            using SpeedWorld world = SpeedWorld.Create(size);
            world.Call(Personas.Priya, "whoami", "{}");
            foreach ((string name, Persona persona, string tool, string arguments) in Reads)
            {
                int from = world.Store.Trace.Count;
                (long reads, double milliseconds) = world.Measure(persona, tool, arguments);
                string what = string.Join("; ", world.Store.Trace.Skip(from).GroupBy(t => t).Select(g => g.Count() == 1 ? g.Key : g.Key + " x" + g.Count()));
                TestContext.Current.TestOutputHelper?.WriteLine(world.Applications + " applications, " + name + ": " + reads + " database reads, " + milliseconds.ToString("F0", CultureInfo.InvariantCulture) + " ms: " + what);
                Assert.True(reads <= Ceilings[name], name + " made " + reads + " database reads on " + world.Applications + " applications; the ceiling is " + Ceilings[name] + ": " + what);
            }
        }
    }

    // The list reads of the other pages at 2,000 applications (wave 4b review): the Measures page, a person's owner
    // assignments, the approvals that wait for a person, the decision log, and the Data page's tabs, every page of each.
    // Each page stays under its ceiling of database reads; a later page of get_data_gaps and list_data_uses reads the list
    // the first page worked out (InProcessBackend.CachedList), so it costs a few reads, not the whole list again.
    private static readonly (string Name, Persona Persona, string Tool, string Arguments, long Ceiling)[] ListReads =
    {
        ("Measures: get_measures", Personas.Mateo, "get_measures", "{}", 30),
        ("Owners by person: list_owner_assignments", Personas.Owen, "list_owner_assignments", "{\"principalObjectId\":\"" + Personas.NadiaId + "\",\"top\":100}", 15),
        ("Approvals (mine): list_approvals", Personas.Nadia, "list_approvals", "{\"view\":\"mine\",\"top\":100}", 15),
        ("Decision log: get_decision_log", Personas.Owen, "get_decision_log", "{\"top\":100}", 18),
        ("Data uses: list_data_uses", Personas.Owen, "list_data_uses", "{\"top\":100}", 24),
        ("Data mapping: list_data_mappings", Personas.Owen, "list_data_mappings", "{\"top\":100}", 18),
        ("Data gaps: get_data_gaps", Personas.Owen, "get_data_gaps", "{\"top\":100}", 24),
        ("Data locations: find_data_locations", Personas.Owen, "find_data_locations", "{\"top\":100}", 16),
        ("Data domains: find_data_domains", Personas.Owen, "find_data_domains", "{\"top\":100}", 14),
    };

    [Fact]
    public void TheListReadsKeepAFewDatabaseReadsAPageAtTwoThousandApplications()
    {
        using SpeedWorld world = SpeedWorld.Create(2000);
        world.Call(Personas.Priya, "whoami", "{}");
        foreach ((string name, Persona persona, string tool, string arguments, long ceiling) in ListReads)
        {
            string? token = null;
            int page = 0;
            do
            {
                page++;
                string call = token == null ? arguments : arguments.Substring(0, arguments.Length - 1) + ",\"skipToken\":\"" + token + "\"}";
                int from = world.Store.Trace.Count;
                long before = world.Store.Reads;
                var clock = Stopwatch.StartNew();
                JsonTreeNode data = world.Call(persona, tool, call);
                clock.Stop();
                long reads = world.Store.Reads - before;
                string what = string.Join("; ", world.Store.Trace.Skip(from).GroupBy(t => t).Select(g => g.Count() == 1 ? g.Key : g.Key + " x" + g.Count()));
                TestContext.Current.TestOutputHelper?.WriteLine(world.Applications + " applications, " + name + " page " + page + ": " + reads + " database reads, " + clock.Elapsed.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture) + " ms: " + what);
                Assert.True(reads <= ceiling, name + " page " + page + " made " + reads + " database reads on " + world.Applications + " applications; the ceiling is " + ceiling + ": " + what);
                token = Js.Text(data, "skipToken");
            }
            while (token != null && page < 40);
        }
    }

    // The batched reads answer exactly as reading row by row does: every heavy read and list read, on the demo estate and
    // on the estate grown to 240 applications, with the read cache on and off, gives the same data.
    [Fact]
    public void TheBatchedReadsGiveTheSameAnswersAsReadingRowByRow()
    {
        foreach (int size in new[] { 0, 240 })
        {
            using SpeedWorld off = SpeedWorld.Create(size, runtime => runtime.BatchReads = false);
            using SpeedWorld on = SpeedWorld.Create(size);
            foreach ((Persona persona, string tool, string arguments) in Reads.Select(r => (r.Persona, r.Tool, r.Arguments)).Concat(MoreReads))
            {
                string unbatched = Js.Write(off.Call(persona, tool, arguments));
                string batched = Js.Write(on.Call(persona, tool, arguments));
                Assert.True(unbatched == batched, tool + " " + arguments + " on " + on.Applications + " applications answers differently with the read cache." + Environment.NewLine + "off: " + unbatched + Environment.NewLine + "on:  " + batched);
            }
        }
    }

    // Times the reads on the demo estate and on a generated estate of 2,000 applications, with the read batching of
    // document 25, B25-23 off (every read asks the store, as before wave 4) and on, and writes the database reads and the
    // times to the test output and, line by line, to the file PRODUCT_TIME_READS_OUT names (default: the scratch folder).
    // PRODUCT_TIME_READS_SIZES lists the estate sizes (0 is the demo estate as it is; default "0,2000"). Opt in with
    // PRODUCT_TIME_READS=1; the numbers go into src/adapters/azure/STATUS.md.
    [Fact]
    public void TimesTheHeavyReadsOnTheDemoEstateAndOnTwoThousandApplications()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("PRODUCT_TIME_READS") == "1", "Set PRODUCT_TIME_READS=1 to time the heavy reads.");
        string file = Environment.GetEnvironmentVariable("PRODUCT_TIME_READS_OUT") ?? Path.Combine(TestPaths.NewScratchFolder(), "read-speed.txt");
        int[] sizes = (Environment.GetEnvironmentVariable("PRODUCT_TIME_READS_SIZES") ?? "0,2000").Split(',').Select(t => int.Parse(t.Trim(), CultureInfo.InvariantCulture)).ToArray();
        File.WriteAllText(file, string.Empty);
        void Line(string text)
        {
            TestContext.Current.TestOutputHelper?.WriteLine(text);
            File.AppendAllText(file, text + Environment.NewLine);
        }

        foreach (int size in sizes)
        {
            foreach (bool batch in new[] { false, true })
            {
                using SpeedWorld world = SpeedWorld.Create(size, runtime => runtime.BatchReads = batch);
                Line(world.Applications + " applications, " + world.RowCount + " rows (seeded in " + world.SeedMilliseconds.ToString("F0", CultureInfo.InvariantCulture) + " ms), read batching " + (batch ? "on" : "off") + ":");

                // The first call loads the configuration snapshot into the runtime's cache; every read after it is warm.
                world.Call(Personas.Priya, "whoami", "{}");
                foreach ((string name, Persona persona, string tool, string arguments) in Reads)
                {
                    var runs = new List<(long Reads, double Milliseconds)>();
                    for (int i = 0; i < (size == 0 ? 5 : batch ? 3 : 1); i++)
                    {
                        runs.Add(world.Measure(persona, tool, arguments));
                    }

                    double median = runs.Select(r => r.Milliseconds).OrderBy(m => m).ElementAt(runs.Count / 2);
                    Line(string.Format(CultureInfo.InvariantCulture, "  {0}: {1} database reads, {2:F0} ms (median of {3})", name, runs[0].Reads, median, runs.Count));
                }
            }
        }
    }

    // The demo estate, or the demo estate grown to a number of applications, in a measured SQLite file, behind the MCP
    // server core and the in-process backend as the hosts build them.
    internal sealed class SpeedWorld : IDisposable
    {
        private readonly string _folder;
        private readonly McpServerCore _server;

        private SpeedWorld(string folder, MeasuredSqliteStore store, McpServerCore server, int applications, int rows, double seedMilliseconds)
        {
            _folder = folder;
            Store = store;
            _server = server;
            Applications = applications;
            RowCount = rows;
            SeedMilliseconds = seedMilliseconds;
        }

        public MeasuredSqliteStore Store { get; }

        public int Applications { get; }

        public int RowCount { get; }

        public double SeedMilliseconds { get; }

        // size 0 seeds the demo estate as it is.
        public static SpeedWorld Create(int size, Action<EngineRuntime>? configure = null)
        {
            string folder = TestPaths.NewScratchFolder();
            MeasuredSqliteStore store = MeasuredSqliteStore.Open(Path.Combine(folder, "product.db"));
            store.EnsureSchema();
            JsonTreeNode estate = ProductSeeding.ReadJson(Path.Combine(TestPaths.Adapter, "seed", "demo-estate.json"));
            JsonTreeNode rows = size == 0 ? estate : ScaledEstate.Rows(estate, size);
            var clock = Stopwatch.StartNew();
            store.SeedVersion(ProductSeeding.CompileVersion(DemoSeed.Catalog, DemoSeed.Preset, null, DemoSeed.Binding, ProductSeeding.PrincipalKinds(rows), DemoSeed.ValidatedOn));
            int seeded = store.SeedAll(rows);
            clock.Stop();
            var engineClock = new ManualClock(AsOf);
            var runtime = new EngineRuntime(store, store, DemoSeed.Catalog, clock: engineClock);
            configure?.Invoke(runtime);
            var backend = new InProcessBackend(runtime, store, store, DemoSeed.Catalog, new InMemoryRateLimitStore(), ContractSet.Embedded, new InProcessBackendOptions
            {
                RecordUrlBase = "https://product.example.org/records",
            });
            var server = new McpServerCore(ContractSet.Embedded, Tokens(), backend, new NoTelemetry(), new McpServerOptions
            {
                ResourceUrl = "https://mcp.example.org/mcp",
                AuthorizationServer = "https://login.microsoftonline.com/" + Personas.Tenant + "/v2.0",
                TelemetrySalt = "fixture-host-telemetry-salt-0001",
                ValidateOutputs = true,
                RethrowInternalErrors = true,
                UtcNow = () => engineClock.UtcNow,
            });
            int applications = Js.Items(rows, "rows").Count(r => Js.Text(r, "entity") == "business-application");
            return new SpeedWorld(folder, store, server, applications, seeded, clock.Elapsed.TotalMilliseconds);
        }

        // One tools/call; returns the envelope's data and fails the test on an error envelope.
        public JsonTreeNode Call(Persona persona, string tool, string arguments)
        {
            McpResponse response = _server.Handle(McpRequest.Call(1, "tools/call", Js.Obj(("name", JsonTreeNode.FromString(tool)), ("arguments", Js.Parse(arguments))), "Bearer " + persona.MockToken, null));
            Assert.Equal(200, response.HttpStatus);
            JsonTreeNode envelope = Js.Member(response.Result, "structuredContent") ?? throw new InvalidOperationException("No structured content: " + Js.Write(response.Body!));
            Assert.True(Js.Flag(envelope, "ok") == true, tool + " failed: " + Js.Write(envelope));
            return Js.Member(envelope, "data")!;
        }

        // One call's database reads and time.
        public (long Reads, double Milliseconds) Measure(Persona persona, string tool, string arguments)
        {
            long before = Store.Reads;
            var clock = Stopwatch.StartNew();
            Call(persona, tool, arguments);
            clock.Stop();
            return (Store.Reads - before, clock.Elapsed.TotalMilliseconds);
        }

        public void Dispose()
        {
            Store.Dispose();
            TestPaths.TryDelete(_folder);
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
}
