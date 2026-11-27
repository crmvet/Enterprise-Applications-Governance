using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Product.Azure.Host;
using Product.Azure.Stores;

namespace Product.Azure.Tests;

// A test host with the approval runner's routes switched on: the local server on the in-memory test server with the
// runner's settings (section Product:ApprovalRunner) given here. The product registers the runner and maps its routes
// itself (AddProduct and MapProduct), so the tests run through the same wiring as a deployed host.
internal static class RunnerHost
{
    // The runner tests follow one triage card through the outbox. Balanced also opens three consultation cards on an
    // intake (the architect, the PMO and the security reviewer; document 21, B21-9), which the engine and MCP tests
    // cover, so the runner's host retires them for its own runs.
    public const string WithoutIntakeConsultations =
        "{\"op\":\"retire\",\"entity\":\"approval-rule\",\"key\":\"intake-consult-architect\"},"
        + "{\"op\":\"retire\",\"entity\":\"approval-rule\",\"key\":\"intake-consult-pmo\"},"
        + "{\"op\":\"retire\",\"entity\":\"approval-rule\",\"key\":\"intake-consult-security\"}";

    public static ProductTestHost Start(HostSettings? settings = null, bool enabled = true, int maxAttempts = 3, int maxClaim = 10, int leaseSeconds = 900)
    {
        settings ??= new HostSettings();
        string? more = settings.BindingOperations?.Trim();
        settings.BindingOperations = more == null || more.Length < 3
            ? "[" + WithoutIntakeConsultations + "]"
            : "[" + WithoutIntakeConsultations + "," + more.Substring(1);
        settings.Settings[ApprovalRunnerOptions.SectionName + ":Enabled"] = enabled ? "true" : "false";
        settings.Settings[ApprovalRunnerOptions.SectionName + ":MaxAttempts"] = maxAttempts.ToString(CultureInfo.InvariantCulture);
        settings.Settings[ApprovalRunnerOptions.SectionName + ":MaxClaim"] = maxClaim.ToString(CultureInfo.InvariantCulture);
        settings.Settings[ApprovalRunnerOptions.SectionName + ":DefaultLeaseSeconds"] = leaseSeconds.ToString(CultureInfo.InvariantCulture);
        return ProductTestHost.Start(settings);
    }
}

// One claimed item, as the claims route returned it.
internal sealed class RunnerItem
{
    public RunnerItem(JsonNode json)
    {
        Json = json;
    }

    public JsonNode Json { get; }

    public string ApprovalRecordId => Json["approvalRecordId"]!.GetValue<string>();

    public string ClaimId => Json["claimId"]!.GetValue<string>();

    public string DecisionId => Json["decisionId"]!.GetValue<string>();

    public string AskKind => Json["ask"]!["kind"]!.GetValue<string>();

    public IReadOnlyList<string> AskedPrincipals => Json["ask"]!["principals"]!.AsArray().Select(p => p!.GetValue<string>()).ToList();

    public IReadOnlyList<string> AskedGroups => Json["ask"]!["groups"]!.AsArray().Select(p => p!.GetValue<string>()).ToList();

    public string Hint(string name)
    {
        return Json["answerHints"]![name]!.GetValue<string>();
    }
}

// Plays the approval runner's flows (src/adapters/azure/flows) over HTTP with the runner persona's app-only identity:
// pull claims, report a card as sent, give an item back, and post a person's answer with the Idempotency-Key the flow
// sends. A fictional directory stands in for the Office 365 Groups connector: it lists the members of the role groups,
// users only, as the send-card-and-wait flow keeps them.
internal sealed class FakeApprovalRunner
{
    public const string ClaimsPath = "/engine/v1/approval-runner/claims";

    // The fictional directory: who is in each role group (src/adapters/azure/tests/Product.Azure.Tests/Infrastructure/Personas.cs).
    private static readonly Persona[] People = { Personas.Priya, Personas.Nadia, Personas.Owen, Personas.Mateo, Personas.AllRoles, Personas.Agent };

    private readonly ProductTestHost _host;
    private int _cards;

    public FakeApprovalRunner(ProductTestHost host, Persona? persona = null)
    {
        _host = host;
        Persona = persona ?? Personas.Runner;
    }

    public Persona Persona { get; }

    public static string SentPath(string id)
    {
        return "/engine/v1/approval-runner/items/" + id + "/sent";
    }

    public static string ReleasePath(string id)
    {
        return "/engine/v1/approval-runner/items/" + id + "/release";
    }

    public static string OutcomePath(string id)
    {
        return "/engine/v1/approval-records/" + id + "/outcome";
    }

    // The users a card asks: the owner role's principals as the claim gives them, or the role groups' members, users
    // only.
    public static IReadOnlyList<string> AskedUsers(RunnerItem item)
    {
        var users = new List<string>(item.AskedPrincipals);
        foreach (string group in item.AskedGroups)
        {
            users.AddRange(People.Where(p => p.PrincipalKind == "user" && p.Groups.Contains(group, StringComparer.OrdinalIgnoreCase)).Select(p => p.ObjectId));
        }

        return users.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public Task<HttpCall> ClaimCall(string body = "{}")
    {
        return _host.Post(ClaimsPath, Persona, body);
    }

    public async Task<IReadOnlyList<RunnerItem>> Claim(int? max = null, int? leaseSeconds = null)
    {
        var members = new List<string>();
        if (max.HasValue)
        {
            members.Add("\"max\":" + max.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (leaseSeconds.HasValue)
        {
            members.Add("\"leaseSeconds\":" + leaseSeconds.Value.ToString(CultureInfo.InvariantCulture));
        }

        HttpCall call = await ClaimCall("{" + string.Join(",", members) + "}");
        if (call.Status != 200)
        {
            throw new InvalidOperationException("The claim answered " + call + "\n" + _host.Logs);
        }

        return call.Json!["data"]!["items"]!.AsArray().Select(i => new RunnerItem(i!)).ToList();
    }

    // The card went out: the flow's Create an approval gave the approval's name, and the run link is the flow run's.
    public Task<HttpCall> Sent(RunnerItem item, string? claimId = null, string? cardRef = null)
    {
        string card = cardRef ?? "approval-card-" + (++_cards).ToString("D4", CultureInfo.InvariantCulture);
        return _host.Post(SentPath(item.ApprovalRecordId), Persona, Body(
            ("claimId", claimId ?? item.ClaimId),
            ("cardRef", card),
            ("runnerRunUrl", RunUrl(item))));
    }

    public Task<HttpCall> Release(RunnerItem item, string reason = "timeout", string? claimId = null)
    {
        return _host.Post(ReleasePath(item.ApprovalRecordId), Persona, Body(("claimId", claimId ?? item.ClaimId), ("reason", reason)));
    }

    // A person's answer as the flow posts it: the outcome and actor kind keys from the claim's answer hints, the
    // responder's object ID, whether the responder was among the users the card asked, and the Idempotency-Key.
    public Task<HttpCall> Answer(
        RunnerItem item,
        string responderId,
        bool approve,
        DateTime respondedOnUtc,
        string principalKind = "user",
        string? actorKind = null,
        string? typedReason = null,
        bool withKey = true,
        string channel = "outlook")
    {
        string respondedOn = respondedOnUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        bool holds = AskedUsers(item).Contains(responderId, StringComparer.OrdinalIgnoreCase);
        var body = new JsonObject
        {
            ["outcome"] = approve ? item.Hint("approvedOutcome") : item.Hint("rejectedOutcome"),
            ["approver"] = responderId,
            ["approverPrincipalKind"] = principalKind,
            ["approverActorKind"] = actorKind ?? item.Hint("approverActorKind"),
            ["respondedOn"] = respondedOn,
            ["channel"] = channel,
            ["typedReason"] = typedReason,
            ["approverHoldsRequiredRole"] = holds,
            ["flowRunUrl"] = RunUrl(item),
        };
        string key = item.ApprovalRecordId + ":" + responderId + ":" + respondedOn;
        return _host.Post(OutcomePath(item.ApprovalRecordId), Persona, body.ToJsonString(), withKey ? r => r.Headers.TryAddWithoutValidation(ApprovalOutcomeEndpoint.IdempotencyHeader, key) : null);
    }

    public static string RunUrl(RunnerItem item)
    {
        return "https://make.powerautomate.example/environments/approvals/flows/send-card-and-wait/runs/" + item.ApprovalRecordId.Replace("-", string.Empty, StringComparison.Ordinal);
    }

    private static string Body(params (string Name, string Value)[] members)
    {
        var body = new JsonObject();
        foreach ((string name, string value) in members)
        {
            body[name] = value;
        }

        return body.ToJsonString();
    }
}
