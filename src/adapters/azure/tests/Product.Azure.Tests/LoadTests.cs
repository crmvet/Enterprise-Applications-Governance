using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Product.Engine.Runtime.Ports;
using Xunit;

namespace Product.Azure.Tests;

// Many callers at once on one host over one SQLite file: the counts stay exact, writes do not collide, duplicates of one
// deployment record one row, and nothing answers a server failure. The sizes are small so the check stays quick; the
// timings go to the test output and are not asserted.
public sealed class LoadTests
{
    private const string Ping = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}";

    [Fact]
    public async Task ConcurrentRequestsAgainstOneLimitAreCountedExactly()
    {
        await using ProductTestHost host = ProductTestHost.Start(RequestLimitTests.Limits(mcp: 25));

        HttpCall[] answers = await Task.WhenAll(Enumerable.Range(0, 60).Select(_ => host.Post("/mcp", Personas.Priya, Ping)));

        Assert.Equal(25, answers.Count(a => a.Status == 200));
        Assert.Equal(35, answers.Count(a => a.Status == 429));
        Assert.All(answers.Where(a => a.Status == 429), a => Assert.Equal("60", a.Header("Retry-After")));
    }

    [Fact]
    public async Task ManyCallersReadAndWriteAtOnceWithNoServerFailure()
    {
        await using ProductTestHost host = ProductTestHost.Start();
        int before = host.Get<IRecordStore>().Query(RowQuery.For("intake-request")).Count;

        var work = new List<Func<Task<(string Name, HttpCall Call)>>>();
        for (int i = 0; i < 12; i++)
        {
            string n = i.ToString("D2", CultureInfo.InvariantCulture);
            work.Add(() => Tool(host, Personas.Priya, "save_intake_draft", "{\"title\":\"Load intake " + n + "\",\"need\":\"Crews retype site forms at the office; this draft " + n + " checks writes at once.\",\"proposedKindKey\":\"power-platform-app\"}"));
            work.Add(() => Tool(host, Personas.Priya, "find_applications", "{\"text\":\"crew\"}"));
            work.Add(() => Tool(host, Personas.Nadia, "get_application_card", "{\"businessApplicationId\":\"" + Personas.InspectionScheduler + "\"}"));
            work.Add(() => Tool(host, Personas.Mateo, "get_portfolio_summary", "{}"));
            work.Add(() => Rest(host, Personas.NadiaWeb, "get_portfolio_summary", "{\"includeOwnerGaps\":true}"));
            work.Add(() => Rest(host, Personas.PriyaWeb, "find_applications", "{\"text\":\"inspection\"}"));
            work.Add(async () => ("me", await host.Get("/api/me", Personas.OwenWeb)));
        }

        var clock = Stopwatch.StartNew();
        var timed = await Task.WhenAll(work.Select(async w =>
        {
            var one = Stopwatch.StartNew();
            (string name, HttpCall call) = await w();
            return (Name: name, Call: call, Milliseconds: one.Elapsed.TotalMilliseconds);
        }));
        clock.Stop();

        foreach (var answer in timed)
        {
            Assert.True(answer.Call.Status < 500, answer.Name + ": " + answer.Call + "\n" + host.Logs);
            Assert.True(answer.Call.Status == 200, answer.Name + ": " + answer.Call);
            JsonNode body = answer.Call.Json!;
            JsonNode envelope = body["result"]?["structuredContent"] ?? body;
            if (answer.Name != "me")
            {
                Assert.True(envelope["ok"]!.GetValue<bool>(), answer.Name + ": " + answer.Call);
            }
        }

        // Every draft was written once, with its own ID.
        IReadOnlyList<StoredRow> intakes = host.Get<IRecordStore>().Query(RowQuery.For("intake-request"));
        Assert.Equal(before + 12, intakes.Count);
        string[] drafts = timed.Where(t => t.Name == "save_intake_draft").Select(t => t.Call.Json!["result"]!["structuredContent"]!["data"]!["recordId"]!.GetValue<string>()).ToArray();
        Assert.Equal(12, drafts.Distinct(StringComparer.Ordinal).Count());
        Report(timed.Select(t => t.Milliseconds).ToArray(), clock.Elapsed);
    }

    [Fact]
    public async Task ConcurrentDuplicatesOfOneDeploymentRecordOneRow()
    {
        await using ProductTestHost host = ProductTestHost.Start();
        string body = "{\"idempotencyKey\":\"release-300-prod\",\"businessApplicationId\":\"" + Personas.InspectionScheduler + "\",\"artifactName\":\"InspectionScheduler_managed\",\"artifactVersion\":\"3.0.0\",\"artifactKindKey\":\"solution\","
            + "\"deploymentTargetId\":\"" + Personas.FieldOpsProd + "\",\"startedOn\":\"2026-11-23T08:40:00Z\",\"finishedOn\":\"2026-11-23T08:52:00Z\",\"resultMeaning\":\"succeeded\",\"codeApprovalKind\":\"person\"}";

        HttpCall[] answers = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => host.Post("/api/deployments", Personas.Pipeline, body, r => r.Headers.TryAddWithoutValidation("Idempotency-Key", "release-300-prod"))));

        Assert.All(answers, a => Assert.True(a.Status == 200 || a.Status == 201, a + "\n" + host.Logs));
        Assert.Equal(1, answers.Count(a => a.Status == 201));
        string[] ids = answers.Select(a => a.Json!["data"]!["deploymentRecordId"]!.GetValue<string>()).Distinct(StringComparer.Ordinal).ToArray();
        Assert.Single(ids);
        Assert.Single(host.Get<IRecordStore>().Query(RowQuery.For("deployment-record")));
    }

    private static async Task<(string Name, HttpCall Call)> Tool(ProductTestHost host, Persona persona, string tool, string arguments)
    {
        HttpCall call = await host.Mcp(persona, "tools/call", "{\"name\":\"" + tool + "\",\"arguments\":" + arguments + "}");
        return (tool, call);
    }

    private static async Task<(string Name, HttpCall Call)> Rest(ProductTestHost host, Persona persona, string tool, string arguments)
    {
        HttpCall call = await host.Post("/api/" + tool, persona, arguments);
        return ("rest " + tool, call);
    }

    private static void Report(double[] milliseconds, TimeSpan total)
    {
        Array.Sort(milliseconds);
        double Percentile(double p) => milliseconds[Math.Min(milliseconds.Length - 1, (int)Math.Ceiling(p * milliseconds.Length) - 1)];
        TestContext.Current.TestOutputHelper?.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "{0} requests at once in {1:F0} ms; per request p50 {2:F0} ms, p95 {3:F0} ms, max {4:F0} ms.",
            milliseconds.Length,
            total.TotalMilliseconds,
            Percentile(0.50),
            Percentile(0.95),
            milliseconds[^1]));
    }
}
