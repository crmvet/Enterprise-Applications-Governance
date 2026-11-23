using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Product.Azure.Stores;
using Product.Engine.Runtime.Ports;
using Xunit;

namespace Product.Azure.Tests;

// When an answer was given decides whether it counts: the engine never counts an answer given at or after Expires On.
// The runner reports that time, so an answer that says it was given before its own request was made is not an answer to
// that request. It is the shape of an old or late answer posted again on the request that restarted it, which would
// otherwise count as given in time.
public sealed class AnswerTimeTests
{
    [Fact]
    public async Task AnAnswerGivenBeforeItsRequestWasMadeIsRefused()
    {
        await using ProductTestHost host = RunnerHost.Start();
        DateTime start = host.Clock.UtcNow;
        string decisionId = await SubmitChange(host);
        var runner = new FakeApprovalRunner(host);
        RunnerItem first = Assert.Single(await runner.Claim());
        Assert.Equal(200, (await runner.Sent(first)).Status);

        // Six days on, nobody answered in time: the claim's refresh expires the request and opens a restart.
        host.Clock.UtcNow = start.AddDays(6);
        RunnerItem restart = Assert.Single(await runner.Claim());
        Assert.NotEqual(first.ApprovalRecordId, restart.ApprovalRecordId);
        Assert.Equal(200, (await runner.Sent(restart)).Status);
        IRecordStore records = host.Get<IRecordStore>();
        string before = ((RowStore)host.Get<IProductStore>()).Dump();

        // An answer on the restart that says it was given on day one, five days before the restart was requested.
        HttpCall backdated = await runner.Answer(restart, Personas.NadiaId, approve: true, start.AddDays(1));
        Assert.True(backdated.Status == 400, backdated + "\n" + host.Logs);
        Assert.Equal("respondedOn", backdated.Json!["error"]!["field"]!.GetValue<string>());
        Assert.Equal(before, ((RowStore)host.Get<IProductStore>()).Dump());
        Assert.Equal("pending", records.Get("approval-record", restart.ApprovalRecordId)!.Text("outcomeMeaning"));
        Assert.Equal("proposed", records.Get("decision", decisionId)!.Text("statusMeaning"));

        // Her answer given after the restart was requested counts.
        HttpCall answered = await runner.Answer(restart, Personas.NadiaId, approve: true, host.Clock.UtcNow.AddHours(1));
        Assert.True(answered.Status == 200, answered + "\n" + host.Logs);
        Assert.True(answered.Json!["data"]!["countsTowardRequirement"]!.GetValue<bool>(), answered.Body);
    }

    // Priya, through Claude Code, drafts and submits a change to Inspection Scheduler; the engine asks its business owner,
    // with the demo's five days to answer.
    private static async Task<string> SubmitChange(ProductTestHost host)
    {
        JsonNode draft = await host.Call(Personas.Priya, "save_change_request_draft", "{\"businessApplicationId\":\"" + Personas.InspectionScheduler + "\",\"changeTypeKey\":\"normal\","
            + "\"description\":\"Release 3.1 of Inspection Scheduler to production.\",\"plannedTargetId\":\"" + Personas.FieldOpsProd + "\",\"title\":\"Inspection Scheduler 3.1\","
            + "\"windowStart\":\"2026-12-10T06:00:00Z\",\"windowEnd\":\"2026-12-10T08:00:00Z\"}");
        Assert.True(draft["ok"]!.GetValue<bool>(), draft.ToJsonString() + "\n" + host.Logs);
        JsonNode submitted = await host.Call(Personas.Priya, "submit_record", "{\"subjectKind\":\"change-request\",\"subjectId\":\"" + draft["data"]!["recordId"]!.GetValue<string>() + "\"}");
        Assert.True(submitted["ok"]!.GetValue<bool>(), submitted.ToJsonString());
        JsonArray requested = submitted["data"]!["approvalsRequested"]!.AsArray();
        Assert.Single(requested);
        return submitted["data"]!["decisionId"]!.GetValue<string>();
    }
}
