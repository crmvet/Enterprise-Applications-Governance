using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Product.Azure.Tests;

// Wave 4b, fix round 2, on the SQLite store, which holds the store key of O25 (data platform and External Id among the
// locations in service). A draft location that is withdrawn is archived with no Retired On. Before this fix it kept its
// External Id at the store, so adding the same ID again passed the check and then failed with HTTP 500.
public sealed class DataLocationKeyHostTests
{
    private const string Add = "{\"items\":[{\"platformKey\":\"sql-server\",\"targetStageKey\":\"production\",\"externalId\":\"FIXLOC01\",\"labelAsSeen\":\"Fixture location\"}]";

    // Passing: an AI client drafts a location, the platform admin withdraws it in the web app, and adding the same
    // platform and External Id again is a new location, in the check and in the write. The withdrawn draft stays
    // archived with no Retired On, so it still reads as withdrawn.
    [Fact]
    public async Task AWithdrawnDraftLocationFreesItsExternalIdForANewLocation()
    {
        await using ProductTestHost host = ProductTestHost.Start(new HostSettings());
        JsonNode drafted = await host.Call(Personas.Owen, "save_data_location", Add + "}");
        Assert.True(drafted["ok"]!.GetValue<bool>(), drafted.ToJsonString());
        Assert.Equal("draft", drafted["data"]!["items"]![0]!["status"]!["meaning"]!.GetValue<string>());
        string draft = drafted["data"]!["items"]![0]!["dataLocationId"]!.GetValue<string>();

        HttpCall withdrawn = await host.Post("/api/withdraw_record", Personas.OwenWeb, "{\"subjectKind\":\"data-location\",\"subjectId\":\"" + draft + "\",\"reason\":\"Drafted by mistake.\"}");
        Assert.True(withdrawn.Status == 200, withdrawn.ToString());

        HttpCall check = await host.Post("/api/save_data_location", Personas.OwenWeb, Add + ",\"validateOnly\":true}");
        Assert.True(check.Status == 200, check.ToString());
        Assert.False(check.Json!["data"]!["items"]![0]!["matchedExisting"]!.GetValue<bool>());

        HttpCall added = await host.Post("/api/save_data_location", Personas.OwenWeb, Add + "}");
        Assert.True(added.Status == 200, added.ToString());
        JsonNode item = added.Json!["data"]!["items"]![0]!;
        Assert.True(item["created"]!.GetValue<bool>());
        Assert.NotEqual(draft, item["dataLocationId"]!.GetValue<string>());
        Assert.Equal("registered", item["status"]!["meaning"]!.GetValue<string>());

        HttpCall old = await host.Post("/api/find_data_locations", Personas.OwenWeb, "{\"locationId\":\"" + draft + "\"}");
        Assert.True(old.Status == 200, old.ToString());
        JsonNode oldItem = old.Json!["data"]!["items"]![0]!;
        Assert.Equal("archived", oldItem["status"]!["meaning"]!.GetValue<string>());
        Assert.Null(oldItem["retiredOn"]);
    }

    // The other side: a location in service still holds its External Id. Adding it again matches the one there and
    // changes it, in the check and in the write, never a second row and never a 500 from the store's key.
    [Fact]
    public async Task ALocationInServiceKeepsItsExternalIdAndIsMatchedAgain()
    {
        await using ProductTestHost host = ProductTestHost.Start(new HostSettings());
        HttpCall first = await host.Post("/api/save_data_location", Personas.OwenWeb, Add + "}");
        Assert.True(first.Status == 200, first.ToString());
        string id = first.Json!["data"]!["items"]![0]!["dataLocationId"]!.GetValue<string>();

        foreach (string input in new[] { Add + ",\"validateOnly\":true}", Add + "}" })
        {
            HttpCall again = await host.Post("/api/save_data_location", Personas.OwenWeb, input);
            Assert.True(again.Status == 200, again.ToString());
            JsonNode item = again.Json!["data"]!["items"]![0]!;
            Assert.True(item["matchedExisting"]!.GetValue<bool>());
            Assert.False(item["created"]!.GetValue<bool>());
            Assert.Equal(id, item["dataLocationId"]!.GetValue<string>());
        }
    }
}
