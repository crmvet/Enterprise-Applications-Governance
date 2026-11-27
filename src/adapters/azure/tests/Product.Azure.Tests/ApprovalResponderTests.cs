using System;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Product.Azure.Host;
using Product.Azure.Stores;
using Product.Engine.Core.Canonical;
using Product.Engine.Runtime.Ports;
using Xunit;

namespace Product.Azure.Tests;

// Who may be the responder of an approval answer. The approval runner relays a person's click on a card; it never
// answers itself, and no other workload answers either. An owner role's answer counts only from someone who holds that
// owner role on the application, whatever the runner reports. Each attempt is refused or downgraded before anything
// counts.
public sealed class ApprovalResponderTests
{
    [Fact]
    public async Task TheApprovalRunnerCannotAnswerAsItselfWithOrWithoutAPrincipalReference()
    {
        await using ProductTestHost host = ProductTestHost.Start();
        (string approvalId, string decisionId) = await SubmitIntake(host);
        string before = Dump(host);

        // The runner names its own object ID as the person who approved.
        HttpCall recorded = await host.Post(OutcomePath(approvalId), Personas.Runner, Answer(Personas.RunnerId, "2026-11-23T09:30:00Z", holds: true));
        AssertWorkloadRefused(host, recorded, approvalId, decisionId, before);

        // The same with no Principal Reference for the runner: nothing in the store says it is a workload, but the token
        // that posts the answer is the runner's own.
        ForgetPrincipalReference(host, Personas.RunnerId);
        before = Dump(host);
        HttpCall unrecorded = await host.Post(OutcomePath(approvalId), Personas.Runner, Answer(Personas.RunnerId, "2026-11-23T09:31:00Z", holds: true));
        AssertWorkloadRefused(host, unrecorded, approvalId, decisionId, before);
    }

    [Fact]
    public async Task AHolderOfAWorkloadGrantIsNeverTheApprover()
    {
        await using ProductTestHost host = ProductTestHost.Start();
        ForgetPrincipalReference(host, Personas.PipelineId);
        (string approvalId, string decisionId) = await SubmitIntake(host);
        string before = Dump(host);

        // The release pipeline holds a Workload Grant: it is a workload, so it never answers an approval, even when no
        // Principal Reference records its kind and the runner reports it as a user.
        HttpCall call = await host.Post(OutcomePath(approvalId), Personas.Runner, Answer(Personas.PipelineId, "2026-11-23T09:30:00Z", holds: true));
        AssertWorkloadRefused(host, call, approvalId, decisionId, before);
    }

    [Fact]
    public async Task AnOwnerRoleAnswerCountsOnlyFromSomeoneWhoHoldsTheOwnerRole()
    {
        await using ProductTestHost host = ProductTestHost.Start();
        (string approvalId, string decisionId) = await SubmitChangeRequest(host);

        // Owen is a platform admin, not an owner of Inspection Scheduler. The runner reports that he holds the owner
        // role the card asks for; the product checks the Owner Assignments itself and records that he does not.
        string respondedOn = host.Clock.UtcNow.AddHours(1).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        HttpCall call = await host.Post(OutcomePath(approvalId), Personas.Runner, Answer(Personas.OwenId, respondedOn, holds: true));

        Assert.True(call.Status == 200, call + "\n" + host.Logs);
        Assert.False(call.Json!["data"]!["countsTowardRequirement"]!.GetValue<bool>(), call.Body);
        Assert.False(call.Json["data"]!["decisionAccepted"]!.GetValue<bool>(), call.Body);
        StoredRow record = host.Get<IRecordStore>().Get("approval-record", approvalId)!;
        Assert.Equal(Personas.OwenId, record.Text("approver"));
        Assert.False(record.Flag("approverHoldsRequiredRole"));
        Assert.False(record.Flag("countsTowardRequirement"));
        Assert.Equal("proposed", host.Get<IRecordStore>().Get("decision", decisionId)!.Text("statusMeaning"));
    }

    [Fact]
    public async Task TheCurrentOwnersAnswerStillCounts()
    {
        await using ProductTestHost host = ProductTestHost.Start();
        (string approvalId, _) = await SubmitChangeRequest(host);

        string respondedOn = host.Clock.UtcNow.AddHours(1).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        HttpCall call = await host.Post(OutcomePath(approvalId), Personas.Runner, Answer(Personas.NadiaId, respondedOn, holds: true));

        Assert.True(call.Status == 200, call + "\n" + host.Logs);
        Assert.True(call.Json!["data"]!["countsTowardRequirement"]!.GetValue<bool>(), call.Body);
        Assert.True(host.Get<IRecordStore>().Get("approval-record", approvalId)!.Flag("approverHoldsRequiredRole"));
    }

    [Fact]
    public async Task TheRunLinkOfAnAnswerIsAnHttpsUrl()
    {
        await using ProductTestHost host = ProductTestHost.Start();
        (string approvalId, _) = await SubmitIntake(host);
        string before = Dump(host);

        // The run link is stored on the Approval Record as a link to the flow run. Only an https URL is a run link.
        foreach (string link in new[] { "javascript:alert(1)", "data:text/html,x", "http://flows.example/run/1", "https://user:pass@127.0.0.1/run/1", "/runs/1", "https://flows.example/" + new string('a', 2000) })
        {
            string answer = Answer(Personas.OwenId, "2026-11-23T09:30:00Z", holds: true).TrimEnd('}') + ",\"flowRunUrl\":\"" + link + "\"}";
            HttpCall call = await host.Post(OutcomePath(approvalId), Personas.Runner, answer);
            Assert.True(call.Status == 400, link + ": " + call);
            Assert.Equal("validation", call.Json!["error"]!["code"]!.GetValue<string>());
        }

        Assert.Equal(before, Dump(host));
        HttpCall valid = await host.Post(OutcomePath(approvalId), Personas.Runner, Answer(Personas.OwenId, "2026-11-23T09:30:00Z", holds: true).TrimEnd('}') + ",\"flowRunUrl\":\"https://flows.example/run/1\"}");
        Assert.True(valid.Status == 200, valid + "\n" + host.Logs);
    }

    // Security review 2026-11-24 (D-38, B26-9): with step-up sign-in on, a card only links to the decision page, so the
    // runner's relay of a person's answer is refused and nothing is written. The fresh sign-in cannot be skipped through
    // Teams or Outlook.
    [Fact]
    public async Task WithStepUpOnTheRunnerCannotRelayAPersonsAnswer()
    {
        var settings = new HostSettings();
        settings.Settings["Product:WebApp:StepUp:Mode"] = "FreshSignIn";
        settings.Settings["Product:WebApp:StepUp:MaxAgeMinutes"] = "10";
        await using ProductTestHost host = ProductTestHost.Start(settings);
        (string approvalId, string decisionId) = await SubmitIntake(host);
        string before = Dump(host);

        HttpCall call = await host.Post(OutcomePath(approvalId), Personas.Runner, Answer(Personas.OwenId, "2026-11-23T09:30:00Z", holds: true));
        Assert.True(call.Status >= 400 && call.Status < 500, call.ToString() + host.Logs);
        Assert.Equal("rule_violation", call.Json!["error"]!["code"]!.GetValue<string>());
        Assert.Contains("decision page", call.Json["error"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(before, Dump(host));
        Assert.Equal("pending", host.Get<IRecordStore>().Get("approval-record", approvalId)!.Text("outcomeMeaning"));
        Assert.Equal("proposed", host.Get<IRecordStore>().Get("decision", decisionId)!.Text("statusMeaning"));
    }

    // ---- Helpers ----

    private static void AssertWorkloadRefused(ProductTestHost host, HttpCall call, string approvalId, string decisionId, string before)
    {
        Assert.True(call.Status == 403, call + "\n" + host.Logs);
        Assert.Equal("not_authorized", call.Json!["error"]!["code"]!.GetValue<string>());
        Assert.Contains("only from a person", call.Json["error"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(before, Dump(host));
        Assert.Equal("pending", host.Get<IRecordStore>().Get("approval-record", approvalId)!.Text("outcomeMeaning"));
        Assert.Equal("proposed", host.Get<IRecordStore>().Get("decision", decisionId)!.Text("statusMeaning"));
        Assert.Equal(ApprovalOutboxStates.Pending, new ApprovalOutbox(host.Get<IProductStore>()).Get(approvalId)!.State);
    }

    // Points the Principal Reference of an object ID at another, unused object ID, as if the reference was never recorded.
    private static void ForgetPrincipalReference(ProductTestHost host, string objectId)
    {
        StoredRow reference = host.Get<IRecordStore>().Query(RowQuery.For("principal-reference").Where("entraObjectId", objectId)).Single();
        JsonNode columns = JsonNode.Parse(CanonicalJson.Write(reference.Columns, CanonicalProfile.Request))!;
        columns["entraObjectId"] = "00000000-0000-4000-8000-0000000009" + objectId.Substring(objectId.Length - 2);
        host.Get<IStoreSeeder>().Replace("principal-reference", reference.Id, CanonicalJson.Parse(columns.ToJsonString()));
        Assert.Empty(host.Get<IRecordStore>().Query(RowQuery.For("principal-reference").Where("entraObjectId", objectId)));
    }

    private static string OutcomePath(string approvalId)
    {
        return "/engine/v1/approval-records/" + approvalId + "/outcome";
    }

    private static string Answer(string approver, string respondedOn, bool holds)
    {
        return "{\"outcome\":\"approval-approved\",\"approver\":\"" + approver + "\",\"approverActorKind\":\"person\",\"approverPrincipalKind\":\"user\",\"respondedOn\":\""
            + respondedOn + "\",\"approverHoldsRequiredRole\":" + (holds ? "true" : "false") + ",\"channel\":\"teams\"}";
    }

    // Priya, through Claude Code, files and submits an intake; the engine opens one triage card for a platform admin.
    private static async Task<(string ApprovalId, string DecisionId)> SubmitIntake(ProductTestHost host)
    {
        JsonNode draft = await host.Call(Personas.Priya, "save_intake_draft", "{\"title\":\"Field inspections app\",\"need\":\"Inspectors fill in paper forms on site and retype them at the office.\",\"proposedKindKey\":\"power-platform-app\"}");
        Assert.True(draft["ok"]!.GetValue<bool>(), draft.ToJsonString() + "\n" + host.Logs);
        JsonNode submitted = await host.Call(Personas.Priya, "submit_record", "{\"subjectKind\":\"intake-request\",\"subjectId\":\"" + draft["data"]!["recordId"]!.GetValue<string>() + "\"}");
        Assert.True(submitted["ok"]!.GetValue<bool>(), submitted.ToJsonString());
        // The triage card; Balanced also opens three consultation cards on an intake (document 21, B21-9).
        JsonNode card = Assert.Single(submitted["data"]!["approvalsRequested"]!.AsArray(), c => !c!["isConsultation"]!.GetValue<bool>())!;
        return (card["approvalRecordId"]!.GetValue<string>(), submitted["data"]!["decisionId"]!.GetValue<string>());
    }

    // Priya, through Claude Code, drafts and submits a change to Inspection Scheduler; the engine asks its business owner.
    private static async Task<(string ApprovalId, string DecisionId)> SubmitChangeRequest(ProductTestHost host)
    {
        JsonNode draft = await host.Call(Personas.Priya, "save_change_request_draft", "{\"businessApplicationId\":\"" + Personas.InspectionScheduler + "\",\"changeTypeKey\":\"normal\","
            + "\"description\":\"Release 3.0 of Inspection Scheduler to production.\",\"plannedTargetId\":\"" + Personas.FieldOpsProd + "\",\"title\":\"Inspection Scheduler 3.0\","
            + "\"windowStart\":\"2026-11-26T06:00:00Z\",\"windowEnd\":\"2026-11-26T08:00:00Z\"}");
        Assert.True(draft["ok"]!.GetValue<bool>(), draft.ToJsonString() + "\n" + host.Logs);
        JsonNode submitted = await host.Call(Personas.Priya, "submit_record", "{\"subjectKind\":\"change-request\",\"subjectId\":\"" + draft["data"]!["recordId"]!.GetValue<string>() + "\"}");
        Assert.True(submitted["ok"]!.GetValue<bool>(), submitted.ToJsonString());
        JsonNode card = Assert.Single(submitted["data"]!["approvalsRequested"]!.AsArray())!;
        Assert.Equal("business-owner", card["requiredOwnerRoleKey"]!.GetValue<string>());
        return (card["approvalRecordId"]!.GetValue<string>(), submitted["data"]!["decisionId"]!.GetValue<string>());
    }

    private static string Dump(ProductTestHost host)
    {
        return ((RowStore)host.Get<IProductStore>()).Dump();
    }
}
