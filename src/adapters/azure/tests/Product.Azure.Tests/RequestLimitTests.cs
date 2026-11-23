using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Product.Azure.Host;
using Product.Azure.Stores;
using Xunit;

namespace Product.Azure.Tests;

// The request limits per signed-in caller: counted in the store per caller, route group and UTC minute; 429 with
// Retry-After and the rate_limited code over a limit; never a count for an anonymous or refused request.
public sealed class RequestLimitTests
{
    private const string Ping = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}";

    [Theory]
    [InlineData("core")]
    [InlineData("sdk")]
    public async Task TheMcpEndpointAnswers429WithRetryAfterOverTheLimit(string transport)
    {
        HostSettings settings = Limits(mcp: 3);
        settings.Settings["Product:Mcp:Transport"] = transport;
        await using ProductTestHost host = ProductTestHost.Start(settings);

        for (int i = 0; i < 3; i++)
        {
            HttpCall ok = await host.Post("/mcp", Personas.Priya, Ping, StreamableAccept);
            Assert.True(ok.Status == 200, transport + " call " + i + ": " + ok);
        }

        // The demo clock stands at 09:00:00, so the window ends in 60 seconds.
        HttpCall refused = await host.Post("/mcp", Personas.Priya, Ping, StreamableAccept);
        Assert.Equal(429, refused.Status);
        Assert.Equal("60", refused.Header("Retry-After"));
        JsonNode body = refused.Json!;
        Assert.Equal("2.0", body["jsonrpc"]!.GetValue<string>());
        Assert.Equal(-32600, body["error"]!["code"]!.GetValue<int>());
        JsonNode envelope = body["error"]!["data"]!;
        Assert.False(envelope["ok"]!.GetValue<bool>());
        Assert.Equal("rate_limited", envelope["error"]!["code"]!.GetValue<string>());
        Assert.Equal(60, envelope["error"]!["retryAfterSeconds"]!.GetValue<int>());
        Assert.Equal(refused.Header("X-Correlation-Id"), envelope["correlationId"]!.GetValue<string>());
        Assert.Contains("more than 3 requests a minute to the MCP endpoint", envelope["error"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);

        // Every path spelling the endpoint answers counts in the same group.
        Assert.Equal(429, (await host.Post("/MCP/", Personas.Priya, Ping, StreamableAccept)).Status);
    }

    [Fact]
    public async Task EveryRouteGroupHasItsOwnLimitAndAnswersTheFailureEnvelope()
    {
        HostSettings settings = Limits(api: 2, record: 2, approval: 2);
        settings.Settings["Product:ApprovalRunner:Enabled"] = "true";
        await using ProductTestHost host = ProductTestHost.Start(settings);

        // The REST API for the web app.
        Assert.Equal(200, (await host.Get("/api/me", Personas.NadiaWeb)).Status);
        Assert.Equal(200, (await host.Post("/api/find_applications", Personas.NadiaWeb, "{\"text\":\"crew\"}")).Status);
        AssertRefused(await host.Get("/api/info", Personas.NadiaWeb), "the REST API", 2);

        // The record endpoint: a refused body still counts, because the request reached the service signed in.
        Assert.Equal(400, (await host.Post("/api/deployments", Personas.Pipeline, "{}")).Status);
        Assert.Equal(400, (await host.Post("/api/deployments/corrections", Personas.Pipeline, "{}")).Status);
        AssertRefused(await host.Post("/api/deployments", Personas.Pipeline, "{}"), "the record endpoint", 2);

        // The approval runner's routes and the approval outcome endpoint.
        Assert.Equal(200, (await host.Post("/engine/v1/approval-runner/claims", Personas.Runner, "{}")).Status);
        Assert.Equal(404, (await host.Post("/engine/v1/approval-records/00000000-0000-4000-9000-0000000000ff/outcome", Personas.Runner, "{\"outcome\":\"approval-approved\",\"approver\":\"" + Personas.OwenId + "\",\"approverActorKind\":\"person\",\"approverPrincipalKind\":\"user\",\"respondedOn\":\"2026-11-23T09:00:00Z\",\"approverHoldsRequiredRole\":true,\"channel\":\"teams\"}")).Status);
        AssertRefused(await host.Post("/engine/v1/approval-runner/claims", Personas.Runner, "{}"), "the approval routes", 2);

        // The MCP endpoint keeps its own count for the same people.
        Assert.Equal(200, (await host.Post("/mcp", Personas.Nadia, Ping)).Status);
    }

    [Fact]
    public async Task OneCallersCountNeverLimitsAnotherCallerOrAnotherGroup()
    {
        await using ProductTestHost host = ProductTestHost.Start(Limits(mcp: 2, api: 2));

        Assert.Equal(200, (await host.Post("/mcp", Personas.Priya, Ping)).Status);
        Assert.Equal(200, (await host.Post("/mcp", Personas.Priya, Ping)).Status);
        Assert.Equal(429, (await host.Post("/mcp", Personas.Priya, Ping)).Status);

        Assert.Equal(200, (await host.Post("/mcp", Personas.Nadia, Ping)).Status);
        Assert.Equal(200, (await host.Post("/mcp", Personas.Owen, Ping)).Status);

        // The same person on the web app counts in the REST API group.
        Assert.Equal(200, (await host.Get("/api/me", Personas.PriyaWeb)).Status);
    }

    [Fact]
    public async Task ANewMinuteStartsANewCountAndRetryAfterCountsDownToIt()
    {
        await using ProductTestHost host = ProductTestHost.Start(Limits(mcp: 1));

        Assert.Equal(200, (await host.Post("/mcp", Personas.Priya, Ping)).Status);
        host.Clock.Advance(TimeSpan.FromSeconds(45.2));
        HttpCall refused = await host.Post("/mcp", Personas.Priya, Ping);
        Assert.Equal(429, refused.Status);
        Assert.Equal("15", refused.Header("Retry-After"));

        host.Clock.Advance(TimeSpan.FromSeconds(14.8));
        Assert.Equal(200, (await host.Post("/mcp", Personas.Priya, Ping)).Status);
        Assert.Equal(429, (await host.Post("/mcp", Personas.Priya, Ping)).Status);
    }

    [Fact]
    public async Task AnonymousRefusedAndAnonymousRouteRequestsAreNeverCounted()
    {
        await using ProductTestHost host = ProductTestHost.Start(Limits(mcp: 2, api: 2));

        for (int i = 0; i < 6; i++)
        {
            Assert.Equal(401, (await host.Post("/mcp", null, Ping)).Status);
            Assert.Equal(401, (await host.Get("/api/me")).Status);
            Assert.Equal(401, (await host.Post("/mcp", Personas.OtherTenantPriya, Ping)).Status);
            Assert.Equal(200, (await host.Get("/health/live", Personas.Priya)).Status);
            Assert.Equal(200, (await host.Get("/.well-known/oauth-protected-resource/mcp", Personas.Priya)).Status);
        }

        // Priya's own count in each group is untouched by all of that.
        Assert.Equal(1, host.Get<ICounterStore>().Increment(CallerRequestLimitMiddleware.CounterKey(RequestLimitGroup.Mcp, Personas.PriyaId), Minute(host), CallerRequestLimitMiddleware.Window));
        Assert.Equal(1, host.Get<ICounterStore>().Increment(CallerRequestLimitMiddleware.CounterKey(RequestLimitGroup.Api, Personas.PriyaId), Minute(host), CallerRequestLimitMiddleware.Window));
    }

    [Fact]
    public async Task TheCountLivesInTheStoreAndSurvivesARestart()
    {
        string folder = TestPaths.NewScratchFolder();
        try
        {
            string file = Path.Combine(folder, "product.db");
            HostSettings first = Limits(mcp: 2);
            first.DatabaseFile = file;
            await using (ProductTestHost host = ProductTestHost.Start(first))
            {
                Assert.Equal(200, (await host.Post("/mcp", Personas.Priya, Ping)).Status);
                Assert.Equal(200, (await host.Post("/mcp", Personas.Priya, Ping)).Status);
            }

            SqliteConnection.ClearAllPools();
            HostSettings second = Limits(mcp: 2);
            second.DatabaseFile = file;
            await using (ProductTestHost host = ProductTestHost.Start(second))
            {
                Assert.Equal(429, (await host.Post("/mcp", Personas.Priya, Ping)).Status);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TestPaths.TryDelete(folder);
        }
    }

    [Fact]
    public async Task LimitsSwitchOffWholeOrPerGroup()
    {
        HostSettings off = Limits(mcp: 1);
        off.Settings["Product:Limits:Enabled"] = "false";
        await using (ProductTestHost host = ProductTestHost.Start(off))
        {
            for (int i = 0; i < 4; i++)
            {
                Assert.Equal(200, (await host.Post("/mcp", Personas.Priya, Ping)).Status);
            }
        }

        await using (ProductTestHost host = ProductTestHost.Start(Limits(mcp: 0, api: 1)))
        {
            for (int i = 0; i < 4; i++)
            {
                Assert.Equal(200, (await host.Post("/mcp", Personas.Priya, Ping)).Status);
            }

            Assert.Equal(200, (await host.Get("/api/me", Personas.PriyaWeb)).Status);
            Assert.Equal(429, (await host.Get("/api/me", Personas.PriyaWeb)).Status);
        }
    }

    [Fact]
    public void ANegativeLimitFailsStartup()
    {
        HostSettings settings = Limits(record: -1);
        settings.Seed = false;
        Assert.Contains("Product:Limits:RecordPerMinute must be 0 (no limit) or more.", StartupFailures.Messages(settings), StringComparison.Ordinal);
    }

    [Fact]
    public void TheDefaultsLimitEveryGroup()
    {
        var defaults = new ProductLimitsOptions();
        Assert.True(defaults.Enabled);
        foreach (RequestLimitGroup group in new[] { RequestLimitGroup.Mcp, RequestLimitGroup.Api, RequestLimitGroup.Record, RequestLimitGroup.Approval })
        {
            Assert.True(defaults.LimitOf(group) > 0, group.ToString());
        }

        Assert.Null(defaults.LimitOf(RequestLimitGroup.None));
    }

    [Theory]
    [InlineData("/mcp", "/mcp", RequestLimitGroup.Mcp)]
    [InlineData("/MCP/", "/mcp", RequestLimitGroup.Mcp)]
    [InlineData("/mcp/sse", "/mcp", RequestLimitGroup.Mcp)]
    [InlineData("/tools/mcp", "/tools/mcp/", RequestLimitGroup.Mcp)]
    [InlineData("/mcpx", "/mcp", RequestLimitGroup.None)]
    [InlineData("/api/deployments", "/mcp", RequestLimitGroup.Record)]
    [InlineData("/API/Deployments/Corrections/", "/mcp", RequestLimitGroup.Record)]
    [InlineData("/api/deploymentsx", "/mcp", RequestLimitGroup.Api)]
    [InlineData("/api/me", "/mcp", RequestLimitGroup.Api)]
    [InlineData("/api/find_applications", "/mcp", RequestLimitGroup.Api)]
    [InlineData("/engine/v1/approval-runner/claims", "/mcp", RequestLimitGroup.Approval)]
    [InlineData("/engine/v1/approval-records/x/outcome", "/mcp", RequestLimitGroup.Approval)]
    [InlineData("/health/ready", "/mcp", RequestLimitGroup.None)]
    [InlineData("/app/", "/mcp", RequestLimitGroup.None)]
    [InlineData("/.well-known/oauth-protected-resource/mcp", "/mcp", RequestLimitGroup.None)]
    [InlineData("/", "/mcp", RequestLimitGroup.None)]
    public void EachPathBelongsToOneGroup(string path, string mcpPath, RequestLimitGroup expected)
    {
        Assert.Equal(expected, CallerRequestLimitMiddleware.Classify(new PathString(path), mcpPath));
    }

    [Fact]
    public void TheCounterKeyHoldsNoObjectIdAndDiffersPerGroup()
    {
        string[] keys = new[] { RequestLimitGroup.Mcp, RequestLimitGroup.Api, RequestLimitGroup.Record, RequestLimitGroup.Approval }
            .Select(g => CallerRequestLimitMiddleware.CounterKey(g, Personas.PriyaId))
            .ToArray();
        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.All(keys, k => Assert.DoesNotContain(Personas.PriyaId, k, StringComparison.OrdinalIgnoreCase));
        Assert.NotEqual(CallerRequestLimitMiddleware.CounterKey(RequestLimitGroup.Mcp, Personas.NadiaId), keys[0]);
    }

    internal static HostSettings Limits(int mcp = 1000, int api = 1000, int record = 1000, int approval = 1000)
    {
        var settings = new HostSettings();
        settings.Settings["Product:Limits:McpPerMinute"] = mcp.ToString(System.Globalization.CultureInfo.InvariantCulture);
        settings.Settings["Product:Limits:ApiPerMinute"] = api.ToString(System.Globalization.CultureInfo.InvariantCulture);
        settings.Settings["Product:Limits:RecordPerMinute"] = record.ToString(System.Globalization.CultureInfo.InvariantCulture);
        settings.Settings["Product:Limits:ApprovalPerMinute"] = approval.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return settings;
    }

    private static void AssertRefused(HttpCall call, string group, int limit)
    {
        Assert.True(call.Status == 429, call.ToString());
        Assert.Equal("60", call.Header("Retry-After"));
        Assert.False(call.Json!["ok"]!.GetValue<bool>());
        Assert.Equal("rate_limited", call.Json["error"]!["code"]!.GetValue<string>());
        Assert.Equal(60, call.Json["error"]!["retryAfterSeconds"]!.GetValue<int>());
        Assert.Contains("more than " + limit + " requests a minute to " + group, call.Json["error"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    private static DateTime Minute(ProductTestHost host)
    {
        DateTime now = host.Clock.UtcNow;
        return new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc);
    }

    private static void StreamableAccept(System.Net.Http.HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
    }
}
