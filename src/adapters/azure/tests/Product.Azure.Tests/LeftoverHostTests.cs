using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Product.Azure.Host;
using Product.Engine.Core.Contract;
using Product.Engine.Runtime;
using Product.Engine.Runtime.Ports;
using Xunit;

namespace Product.Azure.Tests;

// The wave 2 leftovers in the host (document 24, B24-17; document 25, B25-18; document 26, B26-17): the daily due-state
// refresh as the engine, off unless its hour is set; the approval runner's link-only notification while step-up sign-in
// is on; and record links that follow the request's own address on a local run.
public sealed class LeftoverHostTests
{
    [Fact]
    public async Task TheDailyRefreshRunsOnlyInItsHourAsTheEngineAndIsOffWhenUnset()
    {
        await using (ProductTestHost off = ProductTestHost.Start())
        {
            DueStateRefreshRunner unset = off.Get<DueStateRefreshRunner>();
            Assert.False(unset.IsDue(new DateTime(2026, 11, 24, 2, 5, 0, DateTimeKind.Utc)));
            Assert.Null(unset.RunIfDue(new DateTime(2026, 11, 24, 2, 5, 0, DateTimeKind.Utc)));
        }

        var settings = new HostSettings();
        settings.Settings[ProductDueStateRefreshOptions.SectionName + ":HourUtc"] = "2";
        await using ProductTestHost host = ProductTestHost.Start(settings);
        DueStateRefreshRunner runner = host.Get<DueStateRefreshRunner>();
        Assert.False(runner.IsDue(new DateTime(2026, 11, 24, 3, 5, 0, DateTimeKind.Utc)));
        Assert.Null(runner.RunIfDue(new DateTime(2026, 11, 24, 3, 5, 0, DateTimeKind.Utc)));

        RuntimeOutcome? run = runner.RunIfDue(new DateTime(2026, 11, 24, 2, 5, 0, DateTimeKind.Utc));
        Assert.NotNull(run);
        Assert.True(run!.Succeeded, run.ToString() + host.Logs);
        Assert.Equal("timer", Product.Mcp.Core.Json.Js.Text(run.Payload, "trigger"));

        // A run that changed something leaves its own Audit Entry, as the engine.
        foreach (StoredRow entry in host.Get<IRecordStore>().Query(RowQuery.For("audit-entry")).Where(a => a.Text("operation") == EngineRuntime.DueStateRunOperation))
        {
            Assert.Equal(EngineKeys.EnginePrincipalId, entry.Text("actor"));
            Assert.Equal("engine", entry.Text("actorKind"));
        }
    }

    // The Admin page (document 24, B24-18): the host gives the settings that are safe to show, read fresh for each call,
    // and the approval outbox by state. Nothing secret: no URL, key, client ID or tenant ID.
    [Fact]
    public async Task TheAdminPageShowsTheHostSettingsThatAreSafeToShow()
    {
        var settings = new HostSettings();
        settings.Settings[ProductDueStateRefreshOptions.SectionName + ":HourUtc"] = "3";
        settings.Settings["Product:WebApp:StepUp:Mode"] = "FreshSignIn";
        settings.Settings["Product:WebApp:StepUp:MaxAgeMinutes"] = "10";
        await using ProductTestHost host = RunnerHost.Start(settings);
        await SubmitIntake(host);

        HttpCall call = await host.Post("/api/get_admin_status", Personas.OwenWeb, "{}");
        Assert.True(call.Status == 200 && call.Json!["ok"]!.GetValue<bool>(), call.ToString());
        JsonNode admin = call.Json!["data"]!["host"]!;
        Assert.Equal("Development", admin["environmentName"]!.GetValue<string>());
        Assert.False(admin["isDemo"]!.GetValue<bool>());
        Assert.Equal("fresh-sign-in", admin["stepUpMode"]!.GetValue<string>());
        Assert.Equal(3, admin["dueStateRefresh"]!["hourUtc"]!.GetValue<int>());
        Assert.Equal("Every day at 03:05 UTC.", admin["dueStateRefresh"]!["schedule"]!.GetValue<string>());
        Assert.Equal("sqlite", admin["store"]!["kind"]!.GetValue<string>());
        JsonNode outbox = call.Json!["data"]!["outbox"]!;
        Assert.True(outbox["counts"]!["pending"]!.GetValue<int>() >= 1, outbox.ToJsonString());
        Assert.DoesNotContain(Personas.Tenant, call.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Data Source", call.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnHourOutsideTheDayIsRefusedAtStartup()
    {
        var validator = new ProductDueStateRefreshOptionsValidator();
        Assert.True(validator.Validate(null, new ProductDueStateRefreshOptions { HourUtc = 24 }).Failed);
        Assert.True(validator.Validate(null, new ProductDueStateRefreshOptions { HourUtc = -1 }).Failed);
        Assert.True(validator.Validate(null, new ProductDueStateRefreshOptions { MaxItems = 0 }).Failed);
        Assert.True(validator.Validate(null, new ProductDueStateRefreshOptions()).Succeeded);
        Assert.True(validator.Validate(null, new ProductDueStateRefreshOptions { HourUtc = 0 }).Succeeded);
    }

    [Fact]
    public async Task WithStepUpOnTheRunnerIsToldToSendALinkOnlyNotification()
    {
        var settings = new HostSettings();
        settings.Settings["Product:WebApp:StepUp:Mode"] = "FreshSignIn";
        settings.Settings["Product:WebApp:StepUp:MaxAgeMinutes"] = "10";
        await using ProductTestHost host = RunnerHost.Start(settings);
        await SubmitIntake(host);

        RunnerItem item = Assert.Single(await new FakeApprovalRunner(host).Claim());
        Assert.Equal(ApprovalRequestCards.LinkOnlyDelivery, item.Json["delivery"]!.GetValue<string>());
        Assert.Contains("This message takes no answer", item.Json["detailsMarkdown"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.StartsWith("https://product.example.org/records/decision/", item.Json["recordUrl"]!.GetValue<string>(), StringComparison.Ordinal);

        // With step-up off (the demo), the item is an answerable card.
        await using ProductTestHost plain = RunnerHost.Start();
        await SubmitIntake(plain);
        RunnerItem card = Assert.Single(await new FakeApprovalRunner(plain).Claim());
        Assert.Equal(ApprovalRequestCards.CardDelivery, card.Json["delivery"]!.GetValue<string>());
    }

    [Fact]
    public async Task OnALocalRunWithNoPublicBaseUrlRecordLinksFollowTheRequestsAddress()
    {
        var settings = new HostSettings();
        settings.Settings["Product:PublicBaseUrl"] = string.Empty;
        settings.Settings["Product:RecordUrlBase"] = string.Empty;
        await using ProductTestHost host = ProductTestHost.Start(settings);

        JsonNode saved = await host.Call(Personas.Priya, "save_intake_draft", "{\"title\":\"Fixture local links\",\"need\":\"A local run opens its own records.\",\"proposedKindKey\":\"power-platform-app\"}");
        Assert.True(saved["ok"]!.GetValue<bool>(), saved.ToJsonString() + host.Logs);
        Assert.StartsWith("http://localhost/app/records/intake-request/", saved["links"]!["recordUrl"]!.GetValue<string>(), StringComparison.Ordinal);

        // With a public base URL configured, the configured record base stands.
        await using ProductTestHost configured = ProductTestHost.Start();
        JsonNode other = await configured.Call(Personas.Priya, "save_intake_draft", "{\"title\":\"Fixture configured links\",\"need\":\"The configured base stands.\",\"proposedKindKey\":\"power-platform-app\"}");
        Assert.StartsWith("https://product.example.org/records/intake-request/", other["links"]!["recordUrl"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    // The drafted by pair (document 22, B22-15) in the SQLite store: Claude Code drafts for Priya, Priya edits on the web
    // app, and the row still names the AI client and the person it drafted for, while the last stamp names Priya's edit.
    [Fact]
    public async Task TheStoreKeepsTheDraftedByPairAfterThePersonEdits()
    {
        await using ProductTestHost host = ProductTestHost.Start();
        JsonNode drafted = await host.Call(Personas.Priya, "save_intake_draft", "{\"title\":\"Fixture drafted pair\",\"need\":\"Claude Code drafts it.\",\"proposedKindKey\":\"power-platform-app\"}");
        Assert.True(drafted["ok"]!.GetValue<bool>(), drafted.ToJsonString() + host.Logs);
        string intake = drafted["data"]!["recordId"]!.GetValue<string>();
        string client = host.Get<IRecordStore>().Get("intake-request", intake)!.Text("clientApp")!;

        HttpCall edited = await host.Post("/api/save_intake_draft", Personas.PriyaWeb, "{\"intakeRequestId\":\"" + intake + "\",\"title\":\"Fixture drafted pair\",\"need\":\"Priya changed the need.\",\"proposedKindKey\":\"power-platform-app\"}");
        Assert.True(edited.Status == 200, edited.ToString() + host.Logs);

        StoredRow row = host.Get<IRecordStore>().Get("intake-request", intake)!;
        Assert.Equal("person", row.Text("performedByActorKind"));
        Assert.Equal(client, row.Text("draftedByClientApp"));
        Assert.Equal(Personas.PriyaId, row.Text("draftedForPrincipal"));
    }

    private static async Task SubmitIntake(ProductTestHost host)
    {
        JsonNode draft = await host.Call(Personas.Priya, "save_intake_draft", "{\"title\":\"Field inspections app\",\"need\":\"Inspectors fill in paper forms on site and retype them at the office.\",\"proposedKindKey\":\"power-platform-app\"}");
        Assert.True(draft["ok"]!.GetValue<bool>(), draft.ToJsonString() + "\n" + host.Logs);
        JsonNode submitted = await host.Call(Personas.Priya, "submit_record", "{\"subjectKind\":\"intake-request\",\"subjectId\":\"" + draft["data"]!["recordId"]!.GetValue<string>() + "\"}");
        Assert.True(submitted["ok"]!.GetValue<bool>(), submitted.ToJsonString());
    }
}
