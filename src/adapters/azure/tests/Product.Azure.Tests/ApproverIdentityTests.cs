using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Product.Azure.Stores;
using Product.Engine.Core.Canonical;
using Product.Engine.Runtime.Ports;
using Xunit;

namespace Product.Azure.Tests;

// The approver of an answer is named by a Microsoft Entra object ID, which is a GUID. A GUID can be spelled several ways
// (in capitals, without hyphens, in braces), and every check that tells people apart compares the ID: the Principal
// Reference that says an object is an agent, the Workload Grants and the posting identity that say it is a workload,
// the Owner Assignments, the count of one person once and the separation rules. The approval outcome endpoint takes
// only the 36-character form and stores it in lower case, so no spelling of an ID slips past one of those checks.
public sealed class ApproverIdentityTests
{
    // Each a spelling of an ID that the Principal Reference or the Workload Grants name in the 36-character form.
    public static TheoryData<string, string> OtherSpellings => new()
    {
        { "an agent user, without hyphens", Personas.AgentUserId.Replace("-", string.Empty, StringComparison.Ordinal) },
        { "an agent user, in braces", "{" + Personas.AgentUserId + "}" },
        { "the approval runner itself, without hyphens", Personas.RunnerId.Replace("-", string.Empty, StringComparison.Ordinal) },
        { "the release pipeline, in parentheses", "(" + Personas.PipelineId + ")" },
    };

    [Theory]
    [MemberData(nameof(OtherSpellings))]
    public async Task AnApproverIdSpelledAnotherWayIsRefusedSoNoAgentOrWorkloadCounts(string who, string approver)
    {
        await using ProductTestHost host = ProductTestHost.Start();
        RecordAgentUser(host);
        (string approvalId, string decisionId) = await SubmitIntake(host);
        string before = Dump(host);

        // The triage card asks a platform admin, a product role, so the runner's Approver Holds Required Role stands. With
        // the ID spelled another way no Principal Reference, Workload Grant or posting identity matches it, and the
        // answer would count as a person's.
        HttpCall call = await host.Post(OutcomePath(approvalId), Personas.Runner, Answer(approver, Moment(host, 1)));

        Assert.True(call.Status == 400, who + ": " + call + "\n" + host.Logs);
        Assert.Equal("validation", call.Json!["error"]!["code"]!.GetValue<string>());
        Assert.Equal("approver", call.Json["error"]!["field"]!.GetValue<string>());
        Assert.Equal(before, Dump(host));
        Assert.Equal("pending", host.Get<IRecordStore>().Get("approval-record", approvalId)!.Text("outcomeMeaning"));
        Assert.Equal("proposed", host.Get<IRecordStore>().Get("decision", decisionId)!.Text("statusMeaning"));
    }

    [Fact]
    public async Task TheAgentUsersAnswerInTheUsualFormIsARecommendationOnly()
    {
        await using ProductTestHost host = ProductTestHost.Start();
        RecordAgentUser(host);
        (string approvalId, string decisionId) = await SubmitIntake(host);

        HttpCall call = await host.Post(OutcomePath(approvalId), Personas.Runner, Answer(Personas.AgentUserId, Moment(host, 1)));

        Assert.True(call.Status == 200, call + "\n" + host.Logs);
        Assert.Equal("recommendation-only", call.Json!["data"]!["outcome"]!["meaning"]!.GetValue<string>());
        Assert.False(call.Json["data"]!["countsTowardRequirement"]!.GetValue<bool>());
        Assert.Equal("proposed", host.Get<IRecordStore>().Get("decision", decisionId)!.Text("statusMeaning"));
    }

    [Fact]
    public async Task ASelfApprovalIsRefusedHoweverTheProposersIdIsSpelled()
    {
        // Balanced refuses a proposer's own approval at tier 3, and Inspection Scheduler becomes a tier 3 application, so
        // a change to it asks a change manager. Priya proposes the change through Claude Code, and the runner reports that
        // she answered the card and holds the role.
        await using ProductTestHost host = ProductTestHost.Start();
        StoredRow application = host.Get<IRecordStore>().Get("business-application", Personas.InspectionScheduler)!;
        JsonNode columns = JsonNode.Parse(CanonicalJson.Write(application.Columns, CanonicalProfile.Request))!;
        columns["tier"] = "tier-3";
        host.Get<IStoreSeeder>().Replace("business-application", application.Id, CanonicalJson.Parse(columns.ToJsonString()));
        (string approvalId, string decisionId) = await SubmitChangeRequest(host);
        string before = Dump(host);

        // Her ID without hyphens is refused before anything is written.
        HttpCall spelled = await host.Post(OutcomePath(approvalId), Personas.Runner, Answer(Personas.PriyaId.Replace("-", string.Empty, StringComparison.Ordinal), Moment(host, 1), Reason));
        Assert.True(spelled.Status == 400, spelled + "\n" + host.Logs);
        Assert.Equal("approver", spelled.Json!["error"]!["field"]!.GetValue<string>());
        Assert.Equal(before, Dump(host));

        // In the usual form the separation rule refuses it: recorded, never counted, and the decision waits.
        HttpCall call = await host.Post(OutcomePath(approvalId), Personas.Runner, Answer(Personas.PriyaId, Moment(host, 2), Reason));
        Assert.True(call.Status == 200, call + "\n" + host.Logs);
        Assert.False(call.Json!["data"]!["countsTowardRequirement"]!.GetValue<bool>(), call.Body);
        Assert.False(call.Json["data"]!["decisionAccepted"]!.GetValue<bool>(), call.Body);
        Assert.Contains(call.Json["warnings"]!.AsArray(), w => w!["code"]?.GetValue<string>() == "approval.separation-refused");
        Assert.Equal("proposed", host.Get<IRecordStore>().Get("decision", decisionId)!.Text("statusMeaning"));
    }

    [Fact]
    public async Task AnApproverIdInCapitalsIsStoredInLowerCase()
    {
        // A fictional person whose object ID has letters in it, as most do. Microsoft Entra issues object IDs in lower
        // case; a runner that sends capitals gets them stored in lower case, so every later comparison sees one spelling.
        const string Responder = "0f1e2d3c-4b5a-4968-8776-a5b4c3d2e1f0";
        await using ProductTestHost host = ProductTestHost.Start();
        (string approvalId, _) = await SubmitIntake(host);

        HttpCall call = await host.Post(OutcomePath(approvalId), Personas.Runner, Answer(Responder.ToUpperInvariant(), Moment(host, 1)), r => r.Headers.TryAddWithoutValidation("Idempotency-Key", approvalId + ":" + Responder.ToUpperInvariant() + ":" + Moment(host, 1)));

        Assert.True(call.Status == 200, call + "\n" + host.Logs);
        Assert.Equal(Responder, host.Get<IRecordStore>().Get("approval-record", approvalId)!.Text("approver"));

        // The runner sends the same answer again with the same key: the first result stands.
        HttpCall again = await host.Post(OutcomePath(approvalId), Personas.Runner, Answer(Responder.ToUpperInvariant(), Moment(host, 1)), r => r.Headers.TryAddWithoutValidation("Idempotency-Key", approvalId + ":" + Responder.ToUpperInvariant() + ":" + Moment(host, 1)));
        Assert.True(again.Status == 200, again + "\n" + host.Logs);
        Assert.Contains("first result stands", again.Json!["summary"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASecondPersonsSameAnswerThatRacesTheFirstIsAConflict()
    {
        // The route reads the record before the engine runs. Here that read sees the record still pending, and Owen's
        // answer commits before the engine's own read, as when two cards of one record are answered at the same moment.
        await using ProductTestHost host = ProductTestHost.Start(new HostSettings
        {
            Services = services => services.Replace(ServiceDescriptor.Singleton<IRecordStore>(sp => new StaleOnce((IRecordStore)sp.GetRequiredService<IProductStore>()))),
        });
        var records = (StaleOnce)host.Get<IRecordStore>();
        (string approvalId, string decisionId) = await SubmitIntake(host);
        StoredRow pending = records.Get("approval-record", approvalId)!;

        HttpCall owen = await host.Post(OutcomePath(approvalId), Personas.Runner, Answer(Personas.OwenId, Moment(host, 1)));
        Assert.True(owen.Status == 200, owen + "\n" + host.Logs);
        string before = Dump(host);

        records.Stale = pending;
        HttpCall second = await host.Post(OutcomePath(approvalId), Personas.Runner, Answer(Personas.AllRolesId, Moment(host, 2)));

        Assert.True(second.Status == 409, second + "\n" + host.Logs);
        Assert.Equal("conflict", second.Json!["error"]!["code"]!.GetValue<string>());
        Assert.Null(records.Stale);
        Assert.Equal(before, Dump(host));
        Assert.Equal(Personas.OwenId, records.Get("approval-record", approvalId)!.Text("approver"));
        Assert.Equal(decisionId, records.Get("approval-record", approvalId)!.Text("decision"));
    }

    // ---- Helpers ----

    // The fictional agent user of the personas, recorded as what it is.
    private static void RecordAgentUser(ProductTestHost host)
    {
        host.Get<IStoreSeeder>().Seed("principal-reference", CanonicalJson.Parse(
            "{\"principalKind\":\"agent-user\",\"entraObjectId\":\"" + Personas.AgentUserId + "\",\"displayLabel\":\"Fictional agent user\",\"isErased\":false}"));
    }

    private static string Moment(ProductTestHost host, int hours)
    {
        return host.Clock.UtcNow.AddHours(hours).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
    }

    private static string OutcomePath(string approvalId)
    {
        return "/engine/v1/approval-records/" + approvalId + "/outcome";
    }

    private const string Reason = "The rollback plan is tested and the window is agreed.";

    private static string Answer(string approver, string respondedOn, string? typedReason = null)
    {
        return "{\"outcome\":\"approval-approved\",\"approver\":\"" + approver + "\",\"approverActorKind\":\"person\",\"approverPrincipalKind\":\"user\",\"respondedOn\":\""
            + respondedOn + "\",\"approverHoldsRequiredRole\":true,\"channel\":\"teams\"" + (typedReason == null ? string.Empty : ",\"typedReason\":\"" + typedReason + "\"") + "}";
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
        Assert.Equal("platform-admin", card["requiredRoleKey"]!.GetValue<string>());
        return (card["approvalRecordId"]!.GetValue<string>(), submitted["data"]!["decisionId"]!.GetValue<string>());
    }

    // Priya, through Claude Code, drafts and submits a change to Inspection Scheduler, a tier 3 application here; the
    // engine asks a change manager.
    private static async Task<(string ApprovalId, string DecisionId)> SubmitChangeRequest(ProductTestHost host)
    {
        JsonNode draft = await host.Call(Personas.Priya, "save_change_request_draft", "{\"businessApplicationId\":\"" + Personas.InspectionScheduler + "\",\"changeTypeKey\":\"normal\","
            + "\"description\":\"Release 3.0 of Inspection Scheduler to production.\",\"plannedTargetId\":\"" + Personas.FieldOpsProd + "\",\"title\":\"Inspection Scheduler 3.0\","
            + "\"windowStart\":\"2026-11-26T06:00:00Z\",\"windowEnd\":\"2026-11-26T08:00:00Z\"}");
        Assert.True(draft["ok"]!.GetValue<bool>(), draft.ToJsonString() + "\n" + host.Logs);
        JsonNode submitted = await host.Call(Personas.Priya, "submit_record", "{\"subjectKind\":\"change-request\",\"subjectId\":\"" + draft["data"]!["recordId"]!.GetValue<string>() + "\"}");
        Assert.True(submitted["ok"]!.GetValue<bool>(), submitted.ToJsonString());
        JsonNode card = Assert.Single(submitted["data"]!["approvalsRequested"]!.AsArray())!;
        Assert.True(card["requiredRoleKey"]?.GetValue<string>() == "change-manager", submitted.ToJsonString());
        return (card["approvalRecordId"]!.GetValue<string>(), submitted["data"]!["decisionId"]!.GetValue<string>());
    }

    private static string Dump(ProductTestHost host)
    {
        return ((RowStore)host.Get<IProductStore>()).Dump();
    }

    // The record store, except that the first read of the armed Approval Record by its ID returns the copy taken earlier.
    private sealed class StaleOnce : IRecordStore
    {
        private readonly IRecordStore _inner;

        public StaleOnce(IRecordStore inner)
        {
            _inner = inner;
        }

        public StoredRow? Stale { get; set; }

        public string StoreId => _inner.StoreId;

        public IStoreTransaction Begin()
        {
            return _inner.Begin();
        }

        public StoredRow? Get(string entity, string id)
        {
            StoredRow? stale = Stale;
            if (stale != null && entity == "approval-record" && id == stale.Id)
            {
                Stale = null;
                return stale;
            }

            return _inner.Get(entity, id);
        }

        public StoredRow? GetByKey(string entity, string key)
        {
            return _inner.GetByKey(entity, key);
        }

        public IReadOnlyList<StoredRow> Query(RowQuery query)
        {
            return _inner.Query(query);
        }
    }
}
