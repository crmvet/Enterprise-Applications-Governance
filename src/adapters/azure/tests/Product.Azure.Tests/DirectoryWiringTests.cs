using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Product.Azure.Host;
using Product.Mcp.Core.Backend;
using Xunit;

namespace Product.Azure.Tests;

// The product registers the directory reads itself (AddProduct calls AddProductDirectoryReads), so a host turns them on
// with settings only: Product:Directory:Mode. These hosts register nothing for the directory, and the readers answer
// from the recorded, fictional Microsoft Graph and Power Platform responses (no token exchange, no network).
public sealed class DirectoryWiringTests
{
    [Fact]
    public async Task OffByDefaultTheDirectoryToolsAnswerUpstreamUnavailable()
    {
        await using ProductTestHost host = ProductTestHost.Start(new HostSettings { Inventory = false });
        Assert.Null(host.Services.GetService<IDirectoryReader>());
        Assert.Null(host.Services.GetService<IInventoryReader>());
        JsonNode holders = await host.Call(Personas.Owen, "get_role_holders", "{\"roleKey\":\"platform-admin\"}");
        Assert.Equal("upstream_unavailable", holders["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task TheModeSettingAloneServesTheReadsOnTheMcpEndpointAndTheWebApp()
    {
        var settings = new HostSettings { Inventory = false };
        settings.Settings["Product:Directory:Mode"] = "Recorded";
        settings.Settings["Product:Directory:RecordedPath"] = RecordedHttpHandler.Folder;
        await using ProductTestHost host = ProductTestHost.Start(settings);

        Assert.IsType<GraphDirectoryReader>(host.Services.GetService<IDirectoryReader>());
        JsonNode viaAi = await host.Call(Personas.Owen, "get_role_holders", "{\"roleKey\":\"platform-admin\"}");
        Assert.True(viaAi["ok"]!.GetValue<bool>(), viaAi.ToJsonString());

        HttpCall viaWeb = await host.Post("/api/get_role_holders", Personas.OwenWeb, "{\"roleKey\":\"platform-admin\"}");
        Assert.True(viaWeb.Status == 200, viaWeb.ToString());
        Assert.Equal(viaAi["data"]!["members"]!.AsArray().Count, viaWeb.Json!["data"]!["members"]!.AsArray().Count);

        // The approval runner and the agent never reach the readers.
        Assert.Equal(403, (await host.Post("/api/get_role_holders", Personas.AgentWeb, "{\"roleKey\":\"platform-admin\"}")).Status);
    }
}
