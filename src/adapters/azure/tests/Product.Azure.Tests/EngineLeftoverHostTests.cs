using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Product.Azure.Host;
using Product.Azure.Stores;
using Product.Engine.Runtime.Ports;
using Xunit;

namespace Product.Azure.Tests;

// Wave 4, the engine leftovers in the host (document 24, B24-22): the host gives the backend its approval outbox, so a
// platform admin sends a failed card again through the REST API and the call's Audit Entry names them; and the demo's
// freshness read answers only on an install that holds the demo estate.
public sealed class EngineLeftoverHostTests
{
    [Fact]
    public async Task APlatformAdminSendsAFailedCardAgainThroughTheRestApi()
    {
        await using ProductTestHost host = RunnerHost.Start();
        JsonNode draft = await host.Call(Personas.Priya, "save_intake_draft", "{\"title\":\"Field inspections app\",\"need\":\"Inspectors fill in paper forms on site and retype them at the office.\",\"proposedKindKey\":\"power-platform-app\"}");
        Assert.True(draft["ok"]!.GetValue<bool>(), draft.ToJsonString() + "\n" + host.Logs);
        JsonNode submitted = await host.Call(Personas.Priya, "submit_record", "{\"subjectKind\":\"intake-request\",\"subjectId\":\"" + draft["data"]!["recordId"]!.GetValue<string>() + "\"}");
        Assert.True(submitted["ok"]!.GetValue<bool>(), submitted.ToJsonString());

        var store = (IApprovalOutboxStore)host.Get<IRecordStore>();
        ApprovalOutboxItem item = store.OutboxItems(100).First(i => i.State == ApprovalOutboxStates.Pending);

        // Pending: the runner's still, so there is nothing to send again. The answer proves the host gave the port.
        HttpCall pending = await host.Post("/api/retry_approval_card", Personas.OwenWeb, "{\"approvalRecordId\":\"" + item.ApprovalRecordId + "\"}");
        Assert.True(pending.Json?["error"]?["code"]?.GetValue<string>() == "validation", pending.ToString());

        // Failed after its attempts: sent again, with no attempts used.
        Assert.True(store.TrySetOutboxItem(item.Next(ApprovalOutboxStates.Failed, item.UpdatedOnUtc, null, null, 3, null, null)));
        HttpCall call = await host.Post("/api/retry_approval_card", Personas.OwenWeb, "{\"approvalRecordId\":\"" + item.ApprovalRecordId + "\"}");
        Assert.True(call.Status == 200 && call.Json!["ok"]!.GetValue<bool>(), call.ToString());
        Assert.Equal("sent-again", call.Json!["data"]!["action"]!.GetValue<string>());
        ApprovalOutboxItem after = store.GetOutboxItem(item.ApprovalRecordId)!;
        Assert.Equal(ApprovalOutboxStates.Pending, after.State);
        Assert.Equal(0, after.Attempts);

        StoredRow audit = host.Get<IRecordStore>().Query(RowQuery.For("audit-entry").Where("entityName", "retry_approval_card")).Single();
        Assert.Equal(Personas.OwenId, audit.Text("actor"));
        Assert.Equal("person", audit.Text("actorKind"));

        // A person without the platform admin role may not.
        HttpCall refused = await host.Post("/api/retry_approval_card", Personas.MateoWeb, "{\"approvalRecordId\":\"" + item.ApprovalRecordId + "\"}");
        Assert.Equal(403, refused.Status);
    }

    [Fact]
    public async Task TheDemoFreshnessReadAnswersOnlyWhereTheDemoEstateIs()
    {
        await using (ProductTestHost plain = ProductTestHost.Start())
        {
            HttpCall hidden = await plain.Post("/api/get_demo_freshness", Personas.MateoWeb, "{}");
            Assert.Equal("not_found", hidden.Json!["error"]!["code"]!.GetValue<string>());
        }

        var settings = new HostSettings();
        settings.Settings["Product:WebApp:Demo"] = "true";
        await using ProductTestHost demo = ProductTestHost.Start(settings);
        HttpCall call = await demo.Post("/api/get_demo_freshness", Personas.MateoWeb, "{}");
        Assert.True(call.Status == 200 && call.Json!["ok"]!.GetValue<bool>(), call.ToString());
        Assert.Equal(48, call.Json!["data"]!["withinHours"]!.GetValue<int>());
        Assert.False(call.Json!["data"]!["needsRefresh"]!.GetValue<bool>());
    }
}
