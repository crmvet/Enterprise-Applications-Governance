using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Product.Azure.Stores;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Runtime;
using Product.Engine.Runtime.Callers;
using Product.Engine.Runtime.Ports;
using Product.Engine.Runtime.Snapshots;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Envelope;
using Product.Mcp.Core.Json;

namespace Product.Azure.Host;

/// <summary>
/// <c>POST /engine/v1/approval-records/{id}/outcome</c>: the approval runner writes the answer a person gave on an approval
/// card. Only an app-only token with the <c>Approval.Write</c> app role reaches it; delegated tokens, AI clients and
/// development personas without that role get 403. An answer whose responder is a workload (the posting identity itself,
/// or a Workload Grant holder) is refused, and for an owner role the product checks the responder against the
/// application's Owner Assignments itself. The engine then refuses every writer but the approval runner (the workload
/// holding a current approval writer grant) and any AI actor, stores an answer that did not come from a person signed in
/// as a user as recommendation only, and on a counted approval recomputes the decision and accepts it itself in the same
/// transaction. No tool approves.
/// <para>
/// The runner sends each answer with an <c>Idempotency-Key</c> header, <c>&lt;recordId&gt;:&lt;responderId&gt;:&lt;respondedOn&gt;</c>.
/// The engine stores it as the Audit Entry Request Key, so the same answer sent again returns the first result and
/// writes nothing, and the same key with another body is a conflict. Before the engine runs, the route answers 409 to an
/// answer given after the record's Expires On (and settles its outbox row as expired) and, when the key is sent, to a
/// different answer for a record that already holds a final outcome.
/// </para>
/// </summary>
public static class ApprovalOutcomeEndpoint
{
    /// <summary>The header that carries the answer's idempotency key.</summary>
    public const string IdempotencyHeader = "Idempotency-Key";

    private const int MaxKeyLength = 400;

    // Flow Run Url is nvarchar(2000) in the column map.
    private const int MaxRunUrlLength = 2000;

    private static readonly string[] Allowed =
    {
        "outcome", "approver", "approverPrincipalKind", "approverActorKind", "respondedOn", "channel", "typedReason", "approverHoldsRequiredRole", "flowRunUrl",
    };

    /// <summary>The members an approval answer may carry (a closed body).</summary>
    internal static IReadOnlyList<string> AllowedMembers => Allowed;

    /// <summary>Handles the approval outcome write.</summary>
    public static async Task Handle(HttpContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        IServiceProvider services = context.RequestServices;
        ContractSet contracts = services.GetRequiredService<ContractSet>();
        CallerClaims? caller = ProductRequest.Caller(context);
        if (caller == null || !caller.IsAppOnly)
        {
            await HttpJson.WriteFailure(context, new McpError(McpErrorCodes.NotAuthorized, "Only the approval runner's app-only token writes approval outcomes. AI clients never approve."), contracts).ConfigureAwait(false);
            return;
        }

        string id = context.Request.RouteValues.TryGetValue("id", out object? value) ? value as string ?? string.Empty : string.Empty;
        string? text = await HttpJson.ReadBody(context, services.GetRequiredService<IOptions<ProductHostOptions>>().Value.MaxRequestBytes).ConfigureAwait(false);
        if (text == null || !HttpJson.IsJson(context.Request.ContentType) || !CanonicalJson.TryParse(text, out JsonTreeNode? body, out _) || body.Kind != JsonTreeKind.Object)
        {
            await HttpJson.WriteFailure(context, new McpError(McpErrorCodes.Validation, "The body is one JSON object, sent as application/json.", "body"), contracts).ConfigureAwait(false);
            return;
        }

        McpError? problem = Check(id, body);
        if (problem != null)
        {
            await HttpJson.WriteFailure(context, problem, contracts).ConfigureAwait(false);
            return;
        }

        string? key = null;
        if (context.Request.Headers.TryGetValue(IdempotencyHeader, out var header) && header.Count > 0)
        {
            key = header.ToString();
            McpError? keyProblem = CheckKey(key, id, body);
            if (keyProblem != null)
            {
                await HttpJson.WriteFailure(context, keyProblem, contracts).ConfigureAwait(false);
                return;
            }
        }

        CallerFacts facts = services.GetRequiredService<CallerFacts>();
        EngineRuntime runtime = services.GetRequiredService<EngineRuntime>();
        IRecordStore records = services.GetRequiredService<IRecordStore>();
        DateTime now = CallerFacts.Utc(runtime.Clock.UtcNow);
        CompiledSnapshot? inForce = runtime.Cache.Load(services.GetRequiredService<IConfigurationStore>(), SnapshotPurpose.Read, now).Snapshot;
        McpError? workload = WorkloadResponder(Js.Text(body, "approver")!, caller, inForce);
        if (workload != null)
        {
            await HttpJson.WriteFailure(context, workload, contracts, now, inForce?.VersionNumber).ConfigureAwait(false);
            return;
        }

        body = WithRecordedPrincipalKind(body, facts);

        StoredRow? stored = records.Get("approval-record", id);
        if (stored != null)
        {
            body = WithOwnerRoleChecked(body, stored, records);
        }

        // The pre-checks change the outbox (a late answer settles its row), so they run only for the approval runner
        // itself. Any other caller goes straight to the engine, which refuses it as before.
        if (stored != null && ApprovalRunnerEndpoints.IsApprovalRunner(services, caller, now))
        {
            McpError? conflict = Conflict(stored, body, key, records, services, now);
            if (conflict != null)
            {
                await HttpJson.WriteFailure(context, conflict, contracts, now).ConfigureAwait(false);
                return;
            }
        }

        CallerIdentity identity = facts.Identity(caller, "approval runner");
        RuntimeOutcome outcome = runtime.Write(identity, new WriteCommand("approval-record", WriteOperation.Update, id, body, key));
        if (!outcome.Succeeded)
        {
            await HttpJson.WriteFailure(context, ProductErrorMap.Map(outcome, contracts, aiCaller: false), contracts, now, outcome.ConfigurationVersion).ConfigureAwait(false);
            return;
        }

        CompiledSnapshot? snapshot = runtime.Cache.Load(services.GetRequiredService<IConfigurationStore>(), SnapshotPurpose.Read, now).Snapshot;
        StoredRow row = records.Get("approval-record", id) ?? throw new InvalidOperationException("Approval Record " + id + " was written but cannot be read back.");
        var reader = new RowFacts(snapshot);
        bool accepted = outcome.Writes.Any(w => w.Entity == "decision" && w.Effect == EffectKind.AcceptDecision);
        JsonTreeNode data = Js.Obj(
            ("approvalRecordId", JsonTreeNode.FromString(row.Id)),
            ("outcome", reader.StatusValue(row, "outcome", "outcomeMeaning")),
            ("countsTowardRequirement", JsonTreeNode.FromBoolean(row.Flag("countsTowardRequirement"))),
            ("decisionAccepted", JsonTreeNode.FromBoolean(accepted)),
            ("decisionId", Js.Str(row.Text("decision"))));
        string summary = outcome.IsReplay
            ? "This answer was recorded before with the same Idempotency-Key, so the first result stands and nothing changed."
            : outcome.IsNoOp
            ? "The Approval Record already holds this outcome, so nothing changed."
            : accepted ? "The answer is recorded, and the engine accepted the decision." : "The answer is recorded.";
        JsonTreeNode envelope = Envelope.Success(data, summary, now, outcome.ConfigurationVersion ?? snapshot?.VersionNumber ?? 1, ProductRequest.CorrelationId(context), reasons: outcome.Reasons);
        await HttpJson.Write(context, StatusCodes.Status200OK, envelope).ConfigureAwait(false);
    }

    // The responder's principal kind is the one its Principal Reference records (document 22: Approver Principal Kind is
    // copied from the Principal Reference). When the reference records anything but a user (an agent user, an agent
    // identity, a service principal, a managed identity, a group), that kind replaces the one the runner sent, so the
    // engine stores the answer as recommendation only even when the runner reported an agent's click as a person's. A
    // responder with no Principal Reference keeps the runner's value, and a runner that already reports a non-user kind
    // is never upgraded.
    internal static JsonTreeNode WithRecordedPrincipalKind(JsonTreeNode body, CallerFacts facts)
    {
        string? approver = Js.Text(body, "approver");
        string? sent = Js.Text(body, "approverPrincipalKind");
        if (approver == null || sent == null)
        {
            return body;
        }

        string? recorded = facts.StoredPrincipalKind(approver);
        if (recorded == null || string.Equals(recorded, "user", StringComparison.Ordinal) || string.Equals(recorded, sent, StringComparison.Ordinal))
        {
            return body;
        }

        return WithMember(body, "approverPrincipalKind", JsonTreeNode.FromString(recorded));
    }

    // The responder of an approval answer is a person. The approval runner relays the answer and never gives it, and no
    // other workload gives one either: an answer that names the identity posting it, or any principal a Workload Grant of
    // the configuration in force names (current or not), is refused before anything is written, so the record keeps
    // waiting for a person's answer. Only a Principal Reference can tell other non-user responders apart; their answers
    // are stored as recommendation only (WithRecordedPrincipalKind).
    internal static McpError? WorkloadResponder(string approver, CallerClaims caller, CompiledSnapshot? inForce)
    {
        bool self = string.Equals(approver, caller.ObjectId, StringComparison.OrdinalIgnoreCase);
        bool grantHolder = inForce != null
            && inForce.Snapshot.TryGetEntity("workload-grant", out Product.Engine.Core.Snapshots.SnapshotEntity? grants)
            && grants.Rows.Any(g => g.TryGetColumn("principalObjectId", out JsonTreeNode? principal)
                && principal.Kind == JsonTreeKind.String
                && string.Equals(principal.GetString(), approver, StringComparison.OrdinalIgnoreCase));
        if (!self && !grantHolder)
        {
            return null;
        }

        return new McpError(
            McpErrorCodes.NotAuthorized,
            "The approver " + approver + " is a workload identity (" + (self ? "the identity that posts this answer" : "it holds a Workload Grant")
            + "). An approval answer comes only from a person, so nothing is recorded and the request keeps waiting.");
    }

    // For an owner role, whether the responder holds it is a fact the product keeps itself: the current Owner Assignments
    // of the decision's application on the day of the answer. The runner's Approver Holds Required Role stands only when
    // they agree; otherwise the answer is recorded as from someone who does not hold the role, and it does not count. A
    // product role's holders are the members of its Entra groups, which the runner reads; its value stands as sent.
    internal static JsonTreeNode WithOwnerRoleChecked(JsonTreeNode body, StoredRow stored, IRecordReader records)
    {
        string? ownerRole = stored.Text("requiredOwnerRole");
        if (ownerRole == null || Js.Member(body, "approverHoldsRequiredRole")?.Kind != JsonTreeKind.True)
        {
            return body;
        }

        string approver = Js.Text(body, "approver")!;
        DateTime responded = RecordEndpoints.Moment(Js.Text(body, "respondedOn"))!.Value;
        StoredRow? decision = stored.Text("decision") is string decisionId ? records.Get("decision", decisionId) : null;
        return decision != null && ApprovalRequestCards.HoldsOwnerRole(records, decision, ownerRole, approver, responded)
            ? body
            : WithMember(body, "approverHoldsRequiredRole", JsonTreeNode.False);
    }

    private static JsonTreeNode WithMember(JsonTreeNode body, string name, JsonTreeNode value)
    {
        return Js.Obj(body.Members.Select(m => new KeyValuePair<string, JsonTreeNode>(
            m.Key,
            string.Equals(m.Key, name, StringComparison.Ordinal) ? value : m.Value)));
    }

    // The two answers the route turns down before the engine runs (409 conflict). An answer given at or after the
    // record's Expires On is late: the record keeps waiting for a restart, and its outbox row is settled as expired so no
    // new card goes out for it. With an Idempotency-Key, an answer for a record that holds a final outcome passes on only
    // when it replays a recorded request (the engine returns the first result, or refuses a reused key) or is the very
    // same answer (approver, time and outcome); any other answer is a conflict, never a second outcome. Without the key
    // the engine's own refusal comes back, as before.
    private static McpError? Conflict(StoredRow stored, JsonTreeNode body, string? key, IRecordStore records, IServiceProvider services, DateTime now)
    {
        DateTime responded = RecordEndpoints.Moment(Js.Text(body, "respondedOn"))!.Value;
        string? meaning = stored.Text("outcomeMeaning");
        if (string.Equals(meaning, "pending", StringComparison.Ordinal))
        {
            DateTime? expires = RecordEndpoints.Moment(stored.Text("expiresOn"));
            if (expires.HasValue && responded >= expires.Value)
            {
                if (services.GetService<IProductStore>() is IApprovalOutboxStore store)
                {
                    new ApprovalOutbox(store).Settle(stored.Id, ApprovalOutboxStates.Expired, now);
                }

                return new McpError(
                    McpErrorCodes.Conflict,
                    "The answer was given at " + CanonicalJson.FormatUtc(responded) + ", after the Approval Record expired at " + CanonicalJson.FormatUtc(expires.Value)
                    + ", so it is not recorded. The record waits for a restart, and no new card goes out for this request.");
            }

            return null;
        }

        if (key == null || meaning == null)
        {
            return null;
        }

        if (records.Query(RowQuery.For("audit-entry").Where("requestKey", key)).Count > 0)
        {
            return null;
        }

        bool same = string.Equals(stored.Text("approver"), Js.Text(body, "approver"), StringComparison.OrdinalIgnoreCase)
            && RecordEndpoints.Moment(stored.Text("respondedOn")) == responded
            && string.Equals(stored.Text("outcome"), Js.Text(body, "outcome"), StringComparison.Ordinal);
        return same
            ? null
            : new McpError(
                McpErrorCodes.Conflict,
                "The Approval Record already holds the final outcome " + meaning + " from an earlier answer, so this answer is not recorded. An answer is written once and never changes.");
    }

    // The runner's key names the answer it carries: the route's record, the body's approver and the body's respondedOn,
    // exactly as sent. A key for another answer would replay the wrong result.
    private static McpError? CheckKey(string key, string id, JsonTreeNode body)
    {
        string[] parts = key.Split(new[] { ':' }, 3);
        bool matches = key.Length <= MaxKeyLength
            && parts.Length == 3
            && string.Equals(parts[0], id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(parts[1], Js.Text(body, "approver"), StringComparison.OrdinalIgnoreCase)
            && string.Equals(parts[2], Js.Text(body, "respondedOn"), StringComparison.Ordinal);
        return matches
            ? null
            : new McpError(McpErrorCodes.Validation, "The " + IdempotencyHeader + " header is <recordId>:<approver>:<respondedOn>, with the route's record ID and the body's approver and respondedOn exactly as sent, at most " + MaxKeyLength + " characters.", IdempotencyHeader);
    }

    // The closed body: known members only, with the types the approval runner sends. The engine checks the values.
    private static McpError? Check(string id, JsonTreeNode body)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64)
        {
            return new McpError(McpErrorCodes.Validation, "The route names the Approval Record by its ID.", "id");
        }

        foreach (KeyValuePair<string, JsonTreeNode> member in body.Members)
        {
            if (Array.IndexOf(Allowed, member.Key) < 0)
            {
                return new McpError(McpErrorCodes.Validation, "The member " + member.Key + " is not part of an approval answer.", member.Key);
            }
        }

        foreach (string required in new[] { "outcome", "approver", "approverPrincipalKind", "respondedOn" })
        {
            if (Js.Text(body, required) is not string text || text.Length == 0)
            {
                return new McpError(McpErrorCodes.Validation, required + " is required, as a string.", required);
            }
        }

        if (!Guid.TryParse(Js.Text(body, "approver"), out _))
        {
            return new McpError(McpErrorCodes.Validation, "approver is the responder's Entra object ID (a GUID).", "approver");
        }

        if (RecordEndpoints.Moment(Js.Text(body, "respondedOn")) == null)
        {
            return new McpError(McpErrorCodes.Validation, "respondedOn is a UTC date-time.", "respondedOn");
        }

        foreach (string optional in new[] { "approverActorKind", "channel", "typedReason", "flowRunUrl" })
        {
            JsonTreeNode? member = Js.Member(body, optional);
            if (member != null && member.Kind != JsonTreeKind.String && member.Kind != JsonTreeKind.Null)
            {
                return new McpError(McpErrorCodes.Validation, optional + " is a string.", optional);
            }
        }

        if ((Js.Text(body, "typedReason")?.Length ?? 0) > 2000)
        {
            return new McpError(McpErrorCodes.Validation, "typedReason is at most 2000 characters.", "typedReason");
        }

        // The run link is stored as a link to the flow run, so it is an https URL like the runner's sent report, with no
        // user information, never a javascript: or data: URL a page might later turn into a link.
        string? runUrl = Js.Text(body, "flowRunUrl");
        if (runUrl != null
            && (runUrl.Length > MaxRunUrlLength || !Uri.TryCreate(runUrl, UriKind.Absolute, out Uri? run) || run.Scheme != Uri.UriSchemeHttps || run.UserInfo.Length > 0))
        {
            return new McpError(McpErrorCodes.Validation, "flowRunUrl is an https URL of at most " + MaxRunUrlLength + " characters, with no user name or password.", "flowRunUrl");
        }

        JsonTreeNode? holds = Js.Member(body, "approverHoldsRequiredRole");
        if (holds != null && holds.Kind != JsonTreeKind.True && holds.Kind != JsonTreeKind.False)
        {
            return new McpError(McpErrorCodes.Validation, "approverHoldsRequiredRole is true or false.", "approverHoldsRequiredRole");
        }

        return null;
    }
}
