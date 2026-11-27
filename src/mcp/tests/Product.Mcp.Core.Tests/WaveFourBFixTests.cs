using System;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Json;
using Product.Mcp.Core.Protocol;
using Xunit;

namespace Product.Mcp.Core.Tests;

// Wave 4b, fix round 1 (document 24, B24-24 records each choice, D-41): a person's confirm takes an AI draft location or
// mapping out of draft and says so; marking a registered application as holding no business data changes nothing else
// and says what it did; a withdrawn draft mapping ends, so it frees its place; and the AI activity's parts add up.
public sealed class WaveFourBFixTests
{
    private const string Location = "{\"items\":[{\"platformKey\":\"sql-server\",\"targetStageKey\":\"production\",\"externalId\":\"FIXSQL02\",\"labelAsSeen\":\"Fixture key cabinet\"}]}";

    // ---- QA: a person confirms an AI draft location and mapping ----

    // Passing: the person's confirm takes the AI client's draft location out of draft, and the answer says so.
    [Fact]
    public void APersonsConfirmTakesAnAiDraftLocationOutOfDraft()
    {
        McpHarness h = McpHarness.Create(withData: true);
        McpServerCore web = h.PersonServer();
        string id = McpHarness.Text(Js.Items(McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "save_data_location", Location)), "items")[0], "dataLocationId");

        JsonTreeNode confirmed = h.Ok(McpHarness.TokenAdminWeb, "save_data_location", "{\"items\":[{\"dataLocationId\":\"" + id + "\",\"platformKey\":\"sql-server\",\"labelAsSeen\":\"Fixture key cabinet\",\"targetStageKey\":\"production\",\"externalId\":\"FIXSQL02\",\"confirm\":true}]}", server: web);
        Assert.Equal("registered", McpHarness.Text(Js.Items(McpHarness.Data(confirmed), "items")[0], "status.meaning"));
        Assert.Equal("1 location is changed. 1 draft is confirmed: no longer a draft.", McpHarness.Text(confirmed, "summary"));
        Assert.Equal("registered", h.Store.Get("data-location", id)!.Text("statusMeaning"));
    }

    // Refusing to confirm: a save without confirm keeps the draft a draft, and the answer says it waits.
    [Fact]
    public void ASaveWithoutConfirmKeepsTheAiDraftLocationADraft()
    {
        McpHarness h = McpHarness.Create(withData: true);
        McpServerCore web = h.PersonServer();
        string id = McpHarness.Text(Js.Items(McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "save_data_location", Location)), "items")[0], "dataLocationId");

        JsonTreeNode saved = h.Ok(McpHarness.TokenAdminWeb, "save_data_location", "{\"items\":[{\"dataLocationId\":\"" + id + "\",\"platformKey\":\"sql-server\",\"labelAsSeen\":\"Fixture key cabinet\",\"targetStageKey\":\"production\",\"externalId\":\"FIXSQL02\"}]}", server: web);
        Assert.Equal("1 location is changed. Drafts wait for confirmation.", McpHarness.Text(saved, "summary"));
        Assert.Equal("draft", h.Store.Get("data-location", id)!.Text("statusMeaning"));
    }

    // Passing: the person's confirm takes the AI client's draft mapping out of draft and stamps the day it was confirmed;
    // a save without confirm leaves it a draft.
    [Fact]
    public void APersonsConfirmTakesAnAiDraftMappingOutOfDraft()
    {
        McpHarness h = McpHarness.Create(withData: true);
        McpServerCore web = h.PersonServer();
        string item = "{\"domainKey\":\"payroll\",\"locationId\":\"" + DataWorld.PayrollLibrary + "\",\"containers\":[\"PAYHIST\"],\"origin\":\"replica\"";
        JsonTreeNode drafted = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "save_data_mapping", "{\"items\":[" + item + "}]}"));
        string id = McpHarness.Text(Js.Items(drafted, "items")[0], "dataMappingId");

        JsonTreeNode kept = h.Ok(McpHarness.TokenAdminWeb, "save_data_mapping", "{\"items\":[" + item + ",\"mappingId\":\"" + id + "\"}]}", server: web);
        Assert.Equal("1 mapping is saved. Drafts wait for confirmation.", McpHarness.Text(kept, "summary"));
        Assert.Equal("draft", h.Store.Get("data-mapping", id)!.Text("statusMeaning"));

        JsonTreeNode confirmed = h.Ok(McpHarness.TokenAdminWeb, "save_data_mapping", "{\"items\":[" + item + ",\"mappingId\":\"" + id + "\",\"confirm\":true}]}", server: web);
        Assert.Equal("1 mapping is saved. 1 draft is confirmed: no longer a draft.", McpHarness.Text(confirmed, "summary"));
        StoredRow row = h.Store.Get("data-mapping", id)!;
        Assert.Equal("registered", row.Text("statusMeaning"));
        Assert.False(string.IsNullOrEmpty(row.Text("confirmedOn")));
    }

    // ---- review: holds no business data on a registered application ----

    // Passing: the flag alone changes, and the answer says what it did, not "saved as a draft".
    [Fact]
    public void MarkingARegisteredApplicationSaysItStaysRegistered()
    {
        McpHarness h = McpHarness.Create();
        McpServerCore web = h.PersonServer();
        StoredRow app = h.Store.Get("business-application", McpHarness.Expense)!;
        JsonTreeNode marked = h.Ok(McpHarness.TokenAdminWeb, "save_application_draft", FlagInput(app, string.Empty), server: web);
        Assert.Equal(app.Text("name") + " is marked as holding no business data. It stays registered.", McpHarness.Text(marked, "summary"));
    }

    // Refusing: components ride on no flag change; nothing is written.
    [Fact]
    public void MarkingARegisteredApplicationRefusesComponents()
    {
        McpHarness h = McpHarness.Create();
        McpServerCore web = h.PersonServer();
        StoredRow app = h.Store.Get("business-application", McpHarness.Expense)!;
        string before = h.Store.Dump();
        JsonTreeNode refused = h.Fails(McpHarness.TokenAdminWeb, "save_application_draft", FlagInput(app, ",\"components\":[{\"componentKindKey\":\"canvas-app\",\"externalId\":\"fixture-extra\",\"url\":\"https://make.example.org/apps/fixture-extra\"}]"), McpErrorCodes.Validation, server: web);
        Assert.Equal("components", McpHarness.Text(refused, "error.field"));
        Assert.Equal(before, h.Store.Dump());
    }

    // ---- review: a withdrawn draft mapping ends ----

    // Passing: withdrawing a draft mapping archives it and ends it today, so it no longer holds the store key's place.
    [Fact]
    public void AWithdrawnDraftMappingEndsToday()
    {
        McpHarness h = McpHarness.Create(withData: true);
        McpServerCore web = h.PersonServer();
        JsonTreeNode drafted = McpHarness.Data(h.Ok(McpHarness.TokenOwner, "save_data_mapping", "{\"items\":[{\"domainKey\":\"payroll\",\"locationId\":\"" + DataWorld.PayrollLibrary + "\",\"origin\":\"replica\"}]}"));
        string id = McpHarness.Text(Js.Items(drafted, "items")[0], "dataMappingId");
        h.Ok(McpHarness.TokenOwnerWeb, "withdraw_record", "{\"subjectKind\":\"data-mapping\",\"subjectId\":\"" + id + "\",\"reason\":\"Not business data.\"}", server: web);
        StoredRow row = h.Store.Get("data-mapping", id)!;
        Assert.Equal("archived", row.Text("statusMeaning"));
        Assert.False(string.IsNullOrEmpty(row.Text("endedOn")));
    }

    // Refusing: a move whose mapping lands on a withdrawn draft that still holds the place (no end date, as before the
    // fix) is refused in the preview and the write, with the field toDomainKey, instead of meeting the store's key.
    [Fact]
    public void AMoveOntoAWithdrawnDraftWithNoEndDateIsRefused()
    {
        McpHarness h = McpHarness.Create(withData: true);
        McpServerCore web = h.PersonServer();
        h.Store.Seed("data-mapping", "{\"dataDomain\":\"travel-bookings\",\"dataLocation\":\"" + DataWorld.FinanceProd + "\",\"dataOrigin\":\"system-of-record\",\"status\":\"data-mapping-archived\",\"statusMeaning\":\"archived\",\"startOn\":\"2026-01-01\"}");
        string move = "{\"fromDomainKey\":\"expenses\",\"toDomainKey\":\"travel-bookings\",\"include\":[\"mappings\"],\"reason\":\"Fixture restructuring\"";
        foreach (string input in new[] { move + ",\"validateOnly\":true}", move + "}" })
        {
            JsonTreeNode refused = h.Fails(McpHarness.TokenDataLeadWeb, "move_data_references", input, McpErrorCodes.Validation, server: web);
            Assert.Equal("toDomainKey", McpHarness.Text(refused, "error.field"));
            Assert.Contains("a withdrawn draft mapping of", McpHarness.Text(refused, "error.message"), StringComparison.Ordinal);
        }
    }

    // ---- QA walk: the AI activity's parts add up to the drafts ----

    [Fact]
    public void TheAiActivityPartsAddUpToTheDrafts()
    {
        McpHarness h = McpHarness.Create(withData: true);
        McpServerCore web = h.PersonServer();
        Scenarios.Deployment(h);
        // An AI draft data mapping the person withdraws: it counts as withdrawn, not decided.
        string id = McpHarness.Text(Js.Items(McpHarness.Data(h.Ok(McpHarness.TokenOwner, "save_data_mapping", "{\"items\":[{\"domainKey\":\"payroll\",\"locationId\":\"" + DataWorld.PayrollLibrary + "\",\"origin\":\"replica\"}]}")), "items")[0], "dataMappingId");
        h.Ok(McpHarness.TokenOwnerWeb, "withdraw_record", "{\"subjectKind\":\"data-mapping\",\"subjectId\":\"" + id + "\",\"reason\":\"Not business data.\"}", server: web);

        JsonTreeNode data = McpHarness.Data(h.Ok(McpHarness.TokenAdmin, "get_ai_activity", "{\"view\":\"drafts\",\"top\":100}"));
        int Count(string name) => int.Parse(McpHarness.Text(data, "counts." + name), System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(Count("drafts"), Count("waiting") + Count("withdrawn") + Count("takenForwardByPerson") + Count("submittedByAi") + Count("recorded"));
        JsonTreeNode item = Js.Items(data, "items").Single(i => McpHarness.Text(i, "record.recordId") == id);
        Assert.Equal("withdrawn", McpHarness.Text(item, "state"));
    }

    private static string FlagInput(StoredRow app, string extra)
    {
        return "{\"businessApplicationId\":\"" + app.Id + "\",\"name\":\"" + app.Text("name") + "\",\"purpose\":\"" + app.Text("purpose") + "\",\"applicationKindKey\":\"" + app.Text("applicationKind") + "\",\"holdsNoBusinessData\":true" + extra + "}";
    }
}
