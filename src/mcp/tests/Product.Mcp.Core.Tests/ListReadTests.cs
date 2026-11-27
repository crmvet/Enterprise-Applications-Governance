using System;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Mcp.Core.Json;
using Product.Mcp.Core.Protocol;
using Xunit;

namespace Product.Mcp.Core.Tests;

// Wave 4, the list reads and speed slice (document 24, B24-21; document 25, B25-23): the list reads the pages fanned out
// for (tools 97 and 98 and the new filters), the read cache of a read call, the person's own day for date-only defaults,
// and the sign-in check before anything else on the MCP endpoint.
public sealed class ListReadTests
{
    private const string Reader = McpHarness.TokenReader;
    private const string Admin = McpHarness.TokenAdmin;

    // ---- 97. list_data_uses ----

    [Fact]
    public void TheDataUsesListSpansEveryDomainAndCountsEachState()
    {
        McpHarness h = McpHarness.Create(withData: true);
        string payroll = McpHarness.Text(McpHarness.Data(h.Ok(McpHarness.TokenOwner, "save_data_use", DataWorld.Use("payroll"))), "recordId");

        JsonTreeNode all = McpHarness.Data(h.Ok(Reader, "list_data_uses", "{}"));
        Assert.Equal("2", McpHarness.Text(all, "totalCount"));
        Assert.Equal(new[] { DataWorld.ExpenseReadsClaims, payroll }.OrderBy(i => i, StringComparer.Ordinal), Js.Items(all, "items").Select(i => McpHarness.Text(i, "dataUseId")).OrderBy(i => i, StringComparer.Ordinal));
        long counted = Js.Member(Js.Member(all, "counts"), "byState")!.Members.Sum(m => m.Value.TryGetInt64(out long n) ? n : 0);
        Assert.Equal(2, counted);

        // The domain filter takes the domains below it: finance holds both, operations neither.
        Assert.Equal("2", McpHarness.Text(McpHarness.Data(h.Ok(Reader, "list_data_uses", "{\"domainKey\":\"finance\"}")), "totalCount"));
        Assert.Equal("0", McpHarness.Text(McpHarness.Data(h.Ok(Reader, "list_data_uses", "{\"domainKey\":\"finance\",\"includeSubtree\":false}")), "totalCount"));
        Assert.Equal("0", McpHarness.Text(McpHarness.Data(h.Ok(Reader, "list_data_uses", "{\"domainKey\":\"operations\"}")), "totalCount"));
        h.Fails(Reader, "list_data_uses", "{\"domainKey\":\"no-such-domain\"}", "validation");

        // A state filter keeps the counts of the other filters, so each count opens its rows.
        string payrollState = McpHarness.Text(Js.Items(all, "items").Single(i => McpHarness.Text(i, "dataUseId") == payroll), "state");
        JsonTreeNode one = McpHarness.Data(h.Ok(Reader, "list_data_uses", "{\"states\":[\"" + payrollState + "\"]}"));
        Assert.Contains(payroll, Js.Items(one, "items").Select(i => McpHarness.Text(i, "dataUseId")));
        Assert.Equal("2", McpHarness.Text(one, "counts.all"));

        // One use by its ID, as its record page reads it; an ID with no row is not found.
        JsonTreeNode byId = McpHarness.Data(h.Ok(Reader, "list_data_uses", "{\"dataUseIds\":[\"" + DataWorld.ExpenseReadsClaims + "\"]}"));
        Assert.Equal(DataWorld.ExpenseReadsClaims, McpHarness.Text(Assert.Single(Js.Items(byId, "items")), "dataUseId"));
        h.Fails(Reader, "list_data_uses", "{\"dataUseIds\":[\"00000000-0000-4000-8000-0000000005ff\"]}", "not_found");

        // Words match the application, the domain path and the purpose.
        Assert.Equal("1", McpHarness.Text(McpHarness.Data(h.Ok(Reader, "list_data_uses", "{\"text\":\"approver\"}")), "totalCount"));
        Assert.Equal("1", McpHarness.Text(McpHarness.Data(h.Ok(Reader, "list_data_uses", "{\"accessKeys\":[\"read\"],\"businessApplicationId\":\"" + McpHarness.Expense + "\",\"domainKey\":\"expenses\"}")), "totalCount"));
    }

    // ---- 98. list_data_mappings ----

    [Fact]
    public void TheDataMappingsListSpansEveryDomainWithTheCopiesOutsideProduction()
    {
        McpHarness h = McpHarness.Create(withData: true);

        JsonTreeNode all = McpHarness.Data(h.Ok(Reader, "list_data_mappings", "{}"));
        Assert.Equal("4", McpHarness.Text(all, "totalCount"));
        var copies = Js.Items(all, "items").Where(i => Js.Member(i, "copyState") is JsonTreeNode c && c.Kind == JsonTreeKind.String).ToList();
        Assert.Equal(copies.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), McpHarness.Text(all, "counts.copies"));
        Assert.DoesNotContain(copies, c => McpHarness.Text(c, "dataMappingId") == DataWorld.ExpensesInProd);

        // A location's mappings, the copies by state, the origins, one mapping by its ID, and the payroll subtree.
        Assert.Equal("2", McpHarness.Text(McpHarness.Data(h.Ok(Reader, "list_data_mappings", "{\"dataLocationId\":\"" + DataWorld.PayrollLibrary + "\"}")), "totalCount"));
        foreach (string state in copies.Select(c => McpHarness.Text(c, "copyState")).Distinct())
        {
            Assert.Equal(copies.Count(c => McpHarness.Text(c, "copyState") == state).ToString(System.Globalization.CultureInfo.InvariantCulture), McpHarness.Text(McpHarness.Data(h.Ok(Reader, "list_data_mappings", "{\"copyStates\":[\"" + state + "\"]}")), "totalCount"));
        }

        Assert.Equal("1", McpHarness.Text(McpHarness.Data(h.Ok(Reader, "list_data_mappings", "{\"originKeys\":[\"masked-copy\"]}")), "totalCount"));
        Assert.Equal(DataWorld.ExpensesInProd, McpHarness.Text(Assert.Single(Js.Items(McpHarness.Data(h.Ok(Reader, "list_data_mappings", "{\"dataMappingIds\":[\"" + DataWorld.ExpensesInProd + "\"]}")), "items")), "dataMappingId"));
        Assert.Equal("2", McpHarness.Text(McpHarness.Data(h.Ok(Reader, "list_data_mappings", "{\"domainKey\":\"payroll\"}")), "totalCount"));
        Assert.Equal("1", McpHarness.Text(McpHarness.Data(h.Ok(Reader, "list_data_mappings", "{\"text\":\"paylib\",\"domainKey\":\"payroll\",\"includeSubtree\":false}")), "totalCount"));
    }

    // ---- The new filters of existing reads ----

    [Fact]
    public void TheApplicationsListFiltersByAiUseAndBuildPath()
    {
        McpHarness h = McpHarness.Create();
        string[] Names(string arguments) => Js.Items(McpHarness.Data(h.Ok(Reader, "find_applications", arguments)), "items").Select(i => McpHarness.Text(i, "name")).ToArray();

        // The expense app says it uses no AI; the travel planner has no answer, so it matches neither.
        Assert.Equal(new[] { "Fixture expense approvals" }, Names("{\"usesAi\":false}"));
        Assert.Empty(Names("{\"usesAi\":true}"));
        Assert.Equal(new[] { "Fixture expense approvals" }, Names("{\"buildPathKeys\":[\"citizen\"]}"));
        Assert.Empty(Names("{\"buildPathKeys\":[\"pro\",\"fusion\"]}"));
        Assert.Equal(2, Names("{}").Length);
    }

    [Fact]
    public void TheChangeScheduleAndTheDeploymentsFilterByPortfolio()
    {
        McpHarness h = McpHarness.Create();
        const string other = "00000000-0000-4000-8000-0000000005f1";
        h.Store.Seed("portfolio", "{\"name\":\"Fixture other\",\"level\":\"department\",\"isActive\":true}", id: other);
        Scenarios.Change(h);
        Scenarios.Deployment(h);
        const string window = "\"from\":\"2026-11-27\",\"to\":\"2026-11-29\"";

        Assert.Single(Js.Items(McpHarness.Data(h.Ok(Reader, "get_change_schedule", "{" + window + ",\"portfolioId\":\"" + McpHarness.Portfolio + "\"}")), "items"));
        Assert.Empty(Js.Items(McpHarness.Data(h.Ok(Reader, "get_change_schedule", "{" + window + ",\"portfolioId\":\"" + other + "\"}")), "items"));
        Assert.Equal("1", McpHarness.Text(McpHarness.Data(h.Ok(Reader, "get_deployments", "{\"portfolioId\":\"" + McpHarness.Portfolio + "\"}")), "totalCount"));
        Assert.Equal("0", McpHarness.Text(McpHarness.Data(h.Ok(Reader, "get_deployments", "{\"portfolioId\":\"" + other + "\"}")), "totalCount"));
        h.Fails(Reader, "get_deployments", "{\"portfolioId\":\"00000000-0000-4000-8000-0000000005f2\"}", "not_found");
    }

    [Fact]
    public void TheAuditTrailFiltersByTheClientAppItWentThrough()
    {
        McpHarness h = McpHarness.Create();
        Scenarios.SubmittedRetirement(h);
        JsonTreeNode all = McpHarness.Data(h.Ok(Admin, "get_audit_trail", "{\"top\":100}"));
        string client = Js.Items(all, "items").Select(i => McpHarness.Text(i, "clientApp")).First(c => c.Length > 0 && c != "null");
        int through = Js.Items(all, "items").Count(i => McpHarness.Text(i, "clientApp") == client);

        JsonTreeNode filtered = McpHarness.Data(h.Ok(Admin, "get_audit_trail", "{\"top\":100,\"clientApp\":\"" + client.ToUpperInvariant() + "\"}"));
        Assert.Equal(through.ToString(System.Globalization.CultureInfo.InvariantCulture), McpHarness.Text(filtered, "totalCount"));
        Assert.All(Js.Items(filtered, "items"), i => Assert.Equal(client, McpHarness.Text(i, "clientApp")));
        Assert.Equal("0", McpHarness.Text(McpHarness.Data(h.Ok(Admin, "get_audit_trail", "{\"clientApp\":\"No such client\"}")), "totalCount"));
    }

    [Fact]
    public void ADocumentLinkAndAGapCheckRunAreReadByTheirIds()
    {
        McpHarness h = McpHarness.Create();
        JsonTreeNode link = McpHarness.Data(h.Ok(Reader, "list_documents", "{\"documentLinkId\":\"" + McpHarness.WikiLink + "\"}"));
        Assert.Equal(McpHarness.WikiLink, McpHarness.Text(Assert.Single(Js.Items(link, "items")), "documentLinkId"));
        h.Fails(Reader, "list_documents", "{\"documentLinkId\":\"00000000-0000-4000-8000-0000000005ff\"}", "not_found");

        Scenarios.Claim(h);
        h.Ok(Admin, "run_gap_check", "{}");
        JsonTreeNode runs = McpHarness.Data(h.Ok(Admin, "list_gap_check_runs", "{}"));
        Assert.True(Js.Items(runs, "items").Count >= 1);
        string run = McpHarness.Text(Js.Items(runs, "items").Last(), "gapCheckRunId");
        JsonTreeNode one = McpHarness.Data(h.Ok(Admin, "list_gap_check_runs", "{\"gapCheckRunId\":\"" + run + "\"}"));
        Assert.Equal(run, McpHarness.Text(Assert.Single(Js.Items(one, "items")), "gapCheckRunId"));
    }

    [Fact]
    public void ThePipelineMapWithNoInputListsEveryPipelineAPipelineMappedToNoTargetToo()
    {
        McpHarness h = McpHarness.Create();
        string mapped = Scenarios.Pipeline(h);
        const string unmapped = "00000000-0000-4000-8000-0000000005e1";
        h.Store.Seed("pipeline-reference", "{\"linkKind\":\"azure-pipelines-pipeline\",\"externalId\":\"pipeline-9\",\"url\":\"https://devops.example.org/fixture/project/_build?definitionId=9\",\"labelAsSeen\":\"Fixture release 9\",\"isMoved\":false}", id: unmapped);

        JsonTreeNode envelope = h.Ok(Reader, "get_pipeline_map", "{}");
        JsonTreeNode data = McpHarness.Data(envelope);
        Assert.Equal(new[] { mapped, unmapped }.OrderBy(i => i, StringComparer.Ordinal), Js.Items(data, "pipelines").Select(p => McpHarness.Text(p, "pipelineReferenceId")));
        Assert.Empty(Js.Items(Js.Items(data, "pipelines").Single(p => McpHarness.Text(p, "pipelineReferenceId") == unmapped), "entries"));
        Assert.Equal("2 pipelines are known; 1 is mapped to no target.", Js.Text(envelope, "summary"));

        // One of the two inputs, or neither; never both.
        h.Fails(Reader, "get_pipeline_map", "{\"businessApplicationId\":\"" + McpHarness.Expense + "\",\"pipelineReferenceId\":\"" + mapped + "\"}", "validation");
        Assert.Single(Js.Items(McpHarness.Data(h.Ok(Reader, "get_pipeline_map", "{\"businessApplicationId\":\"" + McpHarness.Expense + "\"}")), "pipelines"));
    }

    // ---- The read cache of a read call (document 25, B25-23) ----

    // A read call asks the store about each row once and loads many rows in one store call: the portfolio summary of
    // the fixture estate grown to 60 applications makes as few store reads as that of 2, and answers as reading row by row
    // does.
    [Fact]
    public void AReadCallReadsEachRowOnceAndItsCostDoesNotGrowWithTheEstate()
    {
        long Reads(int extra, bool batch, out string answer)
        {
            McpHarness h = McpHarness.Create();
            h.Runtime.BatchReads = batch;
            for (int i = 0; i < extra; i++)
            {
                string app = "00000000-0000-4000-8000-0000000017" + i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
                h.Store.Seed("business-application", "{\"name\":\"Fixture app " + i + "\",\"recordNumber\":\"APP-" + (100 + i) + "\",\"purpose\":\"Fixture\",\"applicationKind\":\"power-platform-app\",\"portfolio\":\"" + McpHarness.Portfolio
                    + "\",\"status\":\"app-registered\",\"statusMeaning\":\"registered\",\"tier\":\"tier-2\",\"lifecycleStage\":\"operate\",\"stageSince\":\"2026-01-05T00:00:00Z\"}", id: app);
                h.Store.Seed("owner-assignment", "{\"businessApplication\":\"" + app + "\",\"ownerRole\":\"business-owner\",\"principal\":\"" + McpHarness.Owner + "\",\"isDeputy\":false,\"startOn\":\"2026-01-05\"}", id: "00000000-0000-4000-8000-0000000018" + i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture));
            }

            h.Ok(Reader, "whoami");
            int before = h.Store.TransactionReads;
            answer = Js.Write(McpHarness.Data(h.Ok(Reader, "get_portfolio_summary", "{\"includeOwnerGaps\":true,\"top\":100}")));
            return h.Store.TransactionReads - before;
        }

        long small = Reads(0, true, out string smallAnswer);
        long large = Reads(60, true, out string largeAnswer);
        long unbatched = Reads(60, false, out string unbatchedAnswer);
        Assert.Equal(small, large);
        Assert.True(large <= 25, "The summary made " + large + " store reads.");
        Assert.True(unbatched > 100, "Without the read cache the summary made " + unbatched + " store reads.");
        Assert.Equal(unbatchedAnswer, largeAnswer);
        Assert.NotEqual(smallAnswer, largeAnswer);
    }

    // ---- The person's own day (document 24, B24-21) ----

    // The web app sends the person's local day; a date-only value the server stamps by default is that day when it is
    // within a day of the UTC day, and the UTC day otherwise (and for an AI client, which sends none).
    [Theory]
    [InlineData("2026-11-21", "2026-11-21")]
    [InlineData("2026-11-23", "2026-11-23")]
    [InlineData("2026-11-19", "2026-11-22")]
    [InlineData("not-a-day", "2026-11-22")]
    [InlineData(null, "2026-11-22")]
    public void ADocumentCheckedTodayIsCheckedOnThePersonsOwnDay(string? localDay, string expected)
    {
        McpHarness h = McpHarness.Create();
        McpServerCore person = McpHarness.NewServer(h.Backend, h.Telemetry, h.Clock, configure: o =>
        {
            McpHarness.PersonEntry(o);
            o.CallerLocalDay = localDay;
        });
        JsonTreeNode data = McpHarness.Data(h.Ok(McpHarness.TokenOwnerWeb, "confirm_document_checked", "{\"documentLinkId\":\"" + McpHarness.WikiLink + "\",\"stillTrue\":true}", server: person));
        Assert.Equal(expected, McpHarness.Text(data, "lastCheckedOn"));
        Assert.Equal(expected, h.Store.Get("document-link", McpHarness.WikiLink)!.Text("lastCheckedOn"));
    }

    // ---- The sign-in comes first (wave 4 security follow-up) ----

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}")]
    [InlineData("{\"jsonrpc\":\"1.0\",\"id\":1,\"method\":\"tools/list\"}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}")]
    public void AnyMessageWithoutAValidTokenIsAnswered401WithTheChallenge(string message)
    {
        McpHarness h = McpHarness.Create();
        foreach (string? token in new[] { null, "Bearer not-a-known-token" })
        {
            McpResponse response = h.Server.Handle(new McpRequest(Js.Parse(message), token));
            Assert.Equal(401, response.HttpStatus);
            Assert.StartsWith("Bearer resource_metadata=", response.Headers["WWW-Authenticate"], StringComparison.Ordinal);
            Assert.Equal("not_signed_in", McpHarness.Text(Js.Member(Js.Member(response.Body, "error"), "data")!, "error.code"));
        }

        // Signed in, the same messages are judged as before: an empty object is not JSON-RPC, a notification gets no answer.
        Assert.Equal(200, h.Server.Handle(new McpRequest(Js.Parse("{}"), "Bearer " + McpHarness.TokenOwner)).HttpStatus);
        Assert.Equal(202, h.Server.Handle(new McpRequest(Js.Parse("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}"), "Bearer " + McpHarness.TokenOwner)).HttpStatus);
    }
}
