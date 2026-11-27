using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Product.Azure.Host;
using Product.Mcp.Core.Backend;
using Xunit;

namespace Product.Azure.Tests;

// The gap check in a demo (wave 4b review). With the directory mode Demo (a local run) the inventory is the demo's fictional
// inventory: the register's own components, the items of the claims, and the items of the demo inventory file. So
// run_gap_check answers instead of upstream_unavailable, the first run opens a claim for each new item of the file, and a
// second run opens none. The mode Off still binds no reader (DirectoryOptionsTests).
public sealed class DemoInventoryTests
{
    private const string NewFlow = "demo-flow-expense-copy";

    [Fact]
    public async Task InTheDemoModeTheGapCheckRunsAndASecondRunOpensNoNewClaim()
    {
        await using ProductTestHost host = ProductTestHost.Start(DemoMode(InventoryFile()));
        Assert.IsType<DemoInventoryReader>(host.Services.GetService<IInventoryReader>());

        JsonNode first = await host.Call(Personas.Owen, "run_gap_check");
        Assert.True(first["ok"]!.GetValue<bool>(), first.ToJsonString());
        JsonNode data = first["data"]!;
        Assert.Equal(DemoInventoryReader.AdminRoleUsedMarker, data["adminRoleUsed"]!.GetValue<string>());
        Assert.False(data["more"]!.GetValue<bool>());
        Assert.Equal("read", data["evidenceLevelKey"]!.GetValue<string>());
        Assert.Equal(new[] { "Expense reminder (copy)" }, data["newClaims"]!.AsArray().Select(c => c!["labelAsSeen"]!.GetValue<string>()).ToArray());
        Assert.Equal(Personas.PriyaId, data["newClaims"]![0]!["suggestedClaimant"]!["objectId"]!.GetValue<string>());

        // Every registered component of a covered kind is in the demo's inventory, so nothing vanished.
        Assert.Empty(data["vanished"]!.AsArray());
        Assert.True(data["itemsSeen"]!.GetValue<int>() > 1, data.ToJsonString());

        JsonNode second = await host.Call(Personas.Owen, "run_gap_check");
        Assert.True(second["ok"]!.GetValue<bool>(), second.ToJsonString());
        Assert.Empty(second["data"]!["newClaims"]!.AsArray());
        Assert.Empty(second["data"]!["vanished"]!.AsArray());
        Assert.Equal(data["itemsSeen"]!.GetValue<int>(), second["data"]!["itemsSeen"]!.GetValue<int>());
    }

    // The demo inventory pages by its own token, so a run with more pages than one continues where it stopped, and the
    // environment filter, which the fictional items cannot honor, is refused in plain words. A component an earlier page
    // saw never vanishes on the last page (the run's seen list holds component row IDs, wave 4b).
    [Fact]
    public async Task TheDemoInventoryPagesAndRefusesAnEnvironmentFilter()
    {
        HostSettings settings = DemoMode(InventoryFile());
        settings.Settings["Product:Directory:InventoryPageSize"] = "2";
        await using ProductTestHost host = ProductTestHost.Start(settings);

        JsonNode first = await host.Call(Personas.Owen, "run_gap_check");
        Assert.True(first["ok"]!.GetValue<bool>(), first.ToJsonString());
        Assert.True(first["data"]!["more"]!.GetValue<bool>(), first.ToJsonString());
        int seen = first["data"]!["itemsSeen"]!.GetValue<int>();
        int newClaims = first["data"]!["newClaims"]!.AsArray().Count;
        JsonNode page = first;
        while (page["data"]!["more"]!.GetValue<bool>())
        {
            string next = "{\"gapCheckRunId\":\"" + page["data"]!["gapCheckRunId"]!.GetValue<string>() + "\",\"skipToken\":\"" + page["data"]!["skipToken"]!.GetValue<string>() + "\"}";
            page = await host.Call(Personas.Owen, "run_gap_check", next);
            Assert.True(page["ok"]!.GetValue<bool>(), page.ToJsonString());
            seen += page["data"]!["itemsSeen"]!.GetValue<int>();
            newClaims += page["data"]!["newClaims"]!.AsArray().Count;
        }

        Assert.Equal(1, newClaims);
        // The components the first page stamped count as seen on the last page (OP-13 takes their row IDs), so none vanished.
        Assert.True(page["data"]!["vanished"]!.AsArray().Count == 0, page.ToJsonString());
        Assert.True(seen > 2);

        JsonNode filtered = await host.Call(Personas.Owen, "run_gap_check", "{\"environmentIds\":[\"field-ops-prod\"]}");
        Assert.False(filtered["ok"]!.GetValue<bool>());
        Assert.Equal("upstream_unavailable", filtered["error"]!["code"]!.GetValue<string>());
        Assert.Contains("has no environments to choose from", filtered["error"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    // A file the setting names but the deployment lacks is reported on the run, never read half.
    [Fact]
    public async Task ANamedInventoryFileThatIsMissingIsReportedOnTheRun()
    {
        await using ProductTestHost host = ProductTestHost.Start(DemoMode(Path.Combine(TestPaths.NewScratchFolder(), "missing.json")));

        JsonNode run = await host.Call(Personas.Owen, "run_gap_check");

        Assert.Equal("upstream_unavailable", run["error"]!["code"]!.GetValue<string>());
        Assert.Contains("which is not in this deployment", run["error"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    // The file keeps only what a demo may show: https links and fictional object IDs.
    [Fact]
    public void TheFileKeepsOnlyHttpsLinksAndFictionalObjectIds()
    {
        (var items, var absent) = DemoInventoryReader.ReadFile("{\"items\":[{\"externalId\":\"a\",\"labelAsSeen\":\"A\",\"componentKindKey\":\"cloud-flow\",\"url\":\"http://flows.example.org/a\",\"suggestedClaimantObjectId\":\"11111111-2222-3333-4444-555555555555\"}],\"absent\":[\"b\"]}");

        InventoryItem item = Assert.Single(items);
        Assert.Equal("cloud-flow", item.SourceType);
        Assert.Null(item.Url);
        Assert.Null(item.SuggestedClaimantObjectId);
        Assert.Equal(new[] { "b" }, absent);
        Assert.Throws<InvalidOperationException>(() => DemoInventoryReader.ReadFile("{\"items\":[{\"externalId\":\"a\"}]}"));
    }

    // Wave 4b QA: with the directory mode Demo the owner check reads the demo's fictional directory, the states and leave
    // dates the store's principal references record, so "Check the owners" answers in a demo. The summary names the demo's
    // directory and never claims a live read of Entra; a disabled account the store records is a finding.
    [Fact]
    public async Task InTheDemoModeTheOwnerCheckReadsTheDemosFictionalDirectory()
    {
        await using ProductTestHost host = ProductTestHost.Start(DemoMode(InventoryFile()));
        Assert.IsType<DemoDirectoryReader>(host.Services.GetService<IDirectoryReader>());

        JsonNode check = await host.Call(Personas.Owen, "run_owner_check");
        Assert.True(check["ok"]!.GetValue<bool>(), check.ToJsonString());
        string summary = check["summary"]!.GetValue<string>();
        Assert.Contains("checked in the demo's fictional directory", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("Entra", summary, StringComparison.Ordinal);
        Assert.True(check["data"]!["principalsChecked"]!.GetValue<int>() > 0, check.ToJsonString());
        Assert.All(check["data"]!["findings"]!.AsArray(), f => Assert.False(f!["directoryState"]!.GetValue<string>() == "enabled" && f["leavesOn"] == null, "An enabled owner with no leave date is not a finding: " + f.ToJsonString()));
    }

    // A local run: Development, the development identity, the directory mode Demo, no test inventory.
    private static HostSettings DemoMode(string file)
    {
        var settings = new HostSettings { Inventory = false };
        settings.Settings["Product:Directory:Mode"] = "Demo";
        settings.Settings["Product:Directory:DemoInventoryFile"] = file;
        return settings;
    }

    // A demo inventory file with one item no application holds.
    private static string InventoryFile()
    {
        string file = Path.Combine(TestPaths.NewScratchFolder(), "demo-inventory.json");
        File.WriteAllText(file, "{\"items\":[{\"externalId\":\"" + NewFlow + "\",\"componentKindKey\":\"cloud-flow\",\"labelAsSeen\":\"Expense reminder (copy)\",\"targetAsSeen\":\"Default environment\",\"suggestedClaimantObjectId\":\"" + Personas.PriyaId + "\",\"url\":\"https://flows.example.org/default/" + NewFlow + "\"}]}");
        return file;
    }
}
