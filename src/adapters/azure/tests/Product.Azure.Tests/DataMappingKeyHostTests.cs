using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Product.Azure.Tests;

// Wave 4b, fix round 1 (review; document 24, B24-24), on the SQLite store, which holds the store key O26 (data domain,
// data location and data origin among the mappings with no Ended On): a withdrawn draft mapping ends, so it frees its
// place, and a move onto a mapping that still holds the place is refused with the reason, never an internal error.
public sealed class DataMappingKeyHostTests
{
    private const string Tree = "[{\"op\":\"upsert\",\"entity\":\"data-domain\",\"key\":\"fix-source\",\"values\":{\"label\":\"Fixture source\",\"levelKey\":\"domain\",\"classificationKey\":\"public\"}},"
        + "{\"op\":\"upsert\",\"entity\":\"data-domain\",\"key\":\"fix-target\",\"values\":{\"label\":\"Fixture target\",\"levelKey\":\"domain\",\"classificationKey\":\"public\"}}]";

    // Passing: a draft mapping in the target domain is withdrawn, which ends it; moving the source domain's mapping onto
    // the same location and origin then goes ahead, in the preview and the write, with no conflict at the store.
    [Fact]
    public async Task AWithdrawnDraftMappingFreesItsPlaceForAMove()
    {
        await using ProductTestHost host = ProductTestHost.Start(new HostSettings { DraftOperations = Tree });
        string location = await Location(host);
        string draft = await AiDraftMapping(host, "fix-target", location);
        HttpCall withdrawn = await host.Post("/api/withdraw_record", Personas.OwenWeb, "{\"subjectKind\":\"data-mapping\",\"subjectId\":\"" + draft + "\",\"reason\":\"Not business data.\"}");
        Assert.True(withdrawn.Status == 200, withdrawn.ToString());
        await PersonMapping(host, "fix-source", location);

        string move = "{\"fromDomainKey\":\"fix-source\",\"toDomainKey\":\"fix-target\",\"include\":[\"mappings\"],\"reason\":\"Fixture merge\"";
        HttpCall preview = await host.Post("/api/move_data_references", Personas.OwenWeb, move + ",\"validateOnly\":true}");
        Assert.True(preview.Status == 200, preview.ToString());
        HttpCall moved = await host.Post("/api/move_data_references", Personas.OwenWeb, move + "}");
        Assert.True(moved.Status == 200, moved.ToString());
    }

    // Refusing: a mapping in the target domain still holds the place (an open one), so the move is refused in the preview
    // and the write, as a validation error on toDomainKey (400), never a 500 from the store's key.
    [Fact]
    public async Task AMoveOntoAMappingThatHoldsThePlaceIsRefusedOnSqlite()
    {
        await using ProductTestHost host = ProductTestHost.Start(new HostSettings { DraftOperations = Tree });
        string location = await Location(host);
        await PersonMapping(host, "fix-target", location);
        await PersonMapping(host, "fix-source", location);

        string move = "{\"fromDomainKey\":\"fix-source\",\"toDomainKey\":\"fix-target\",\"include\":[\"mappings\"],\"reason\":\"Fixture merge\"";
        foreach (string input in new[] { move + ",\"validateOnly\":true}", move + "}" })
        {
            HttpCall refused = await host.Post("/api/move_data_references", Personas.OwenWeb, input);
            Assert.True(refused.Status == 400, refused.ToString());
            Assert.Equal("validation", refused.Json!["error"]!["code"]!.GetValue<string>());
            Assert.Equal("toDomainKey", refused.Json["error"]!["field"]!.GetValue<string>());
        }
    }

    // A registered location the platform admin adds in the web app.
    private static async Task<string> Location(ProductTestHost host)
    {
        HttpCall saved = await host.Post("/api/save_data_location", Personas.OwenWeb, "{\"items\":[{\"platformKey\":\"sql-server\",\"targetStageKey\":\"production\",\"externalId\":\"FIXKEY01\",\"labelAsSeen\":\"Fixture key store\"}]}");
        Assert.True(saved.Status == 200, saved.ToString());
        return saved.Json!["data"]!["items"]![0]!["dataLocationId"]!.GetValue<string>();
    }

    // A mapping the platform admin confirms in the web app.
    private static async Task PersonMapping(ProductTestHost host, string domain, string location)
    {
        HttpCall saved = await host.Post("/api/save_data_mapping", Personas.OwenWeb, "{\"items\":[{\"domainKey\":\"" + domain + "\",\"locationId\":\"" + location + "\",\"origin\":\"system-of-record\"}]}");
        Assert.True(saved.Status == 200, saved.ToString());
    }

    // A draft mapping an AI client saves for the platform admin.
    private static async Task<string> AiDraftMapping(ProductTestHost host, string domain, string location)
    {
        JsonNode drafted = await host.Call(Personas.Owen, "save_data_mapping", "{\"items\":[{\"domainKey\":\"" + domain + "\",\"locationId\":\"" + location + "\",\"origin\":\"system-of-record\"}]}");
        Assert.True(drafted["ok"]!.GetValue<bool>(), drafted.ToJsonString());
        Assert.Equal("draft", drafted["data"]!["items"]![0]!["status"]!["meaning"]!.GetValue<string>());
        return drafted["data"]!["items"]![0]!["dataMappingId"]!.GetValue<string>();
    }
}
