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
/// as a user as recommendation only, and recomputes the decision in the same transaction: it counts the answer (one
/// person once per requirement, and only an answer given before Expires On), accepts or rejects the decision, cancels
/// the records a settled decision still holds open, and opens a new record for a requirement the answer did not meet.
/// No tool approves.
/// <para>
/// The runner sends each answer with an <c>Idempotency-Key</c> header, <c>&lt;recordId&gt;:&lt;responderId&gt;:&lt;respondedOn&gt;</c>.
/// The engine stores it as the Audit Entry Request Key, so the same answer sent again returns the first result and
/// writes nothing, and the same key with another body is a conflict. A different outcome for a record that already holds
/// a final outcome (an answer, expired or cancelled) is the engine's conflict. The route itself answers 409 only to the
/// same outcome from another responder or at another time, which the engine would take as the same answer again: before
/// the engine runs, and again after an engine no-op, in case the first answer committed in between.
/// </para>
/// <para>
/// The approver is an Entra object ID in its 36-character form, stored in lower case, so no other spelling of one ID
/// passes the checks that tell people apart. An answer whose <c>respondedOn</c> is before the record's Requested On is
/// not an answer to that request and is refused before anything is written.
/// </para>
/// </summary>
public static class ApprovalOutcomeEndpoint
{
    /// <summary>The header that carries the answer's idempotency key.</summary>
    public const string IdempotencyHeader = "Idempotency-Key";

    private const int MaxKeyLength = 400;

    // Flow Run Url is nvarchar(2000) in the column map.
    private const int MaxRunUrlLength = 2000;

    // How far the clock of the service that recorded an answer may be behind the product's.
    private static readonly TimeSpan AnswerClockSkew = TimeSpan.FromMinutes(5);

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

        // The approver is stored in lower case, as Microsoft Entra ID issues object IDs and as the caller's token names the
        // proposer, so every later comparison of the stored answer sees one spelling of the person.
        body = WithMember(body, "approver", JsonTreeNode.FromString(Js.Text(body, "approver")!.ToLowerInvariant()));

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
            McpError? early = BeforeRequest(stored, body);
            if (early != null)
            {
                await HttpJson.WriteFailure(context, early, contracts, now).ConfigureAwait(false);
                return;
            }

            body = WithOwnerRoleChecked(body, stored, records);
        }

        // The pre-check runs only for the approval runner itself. Any other caller goes straight to the engine, which
        // refuses it.
        if (stored != null && ApprovalRunnerEndpoints.IsApprovalRunner(services, caller, now))
        {
            McpError? conflict = Conflict(stored, body, key, records);
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

        // The check before the engine read the record while it was pending, and another answer with the same outcome
        // committed before the engine's own read (two cards of one record answered at the same moment, say). The engine
        // compares outcomes only, so it took this answer as the same one again and wrote nothing. It is another
        // responder's answer, or one given at another time, and it is not the one that stands: the same conflict as above.
        if (outcome.IsNoOp && !outcome.IsReplay && ApprovalRunnerEndpoints.IsApprovalRunner(services, caller, now))
        {
            McpError? raced = Conflict(row, body, key, records);
            if (raced != null)
            {
                await HttpJson.WriteFailure(context, raced, contracts, now).ConfigureAwait(false);
                return;
            }
        }
        var reader = new RowFacts(snapshot);
        bool accepted = outcome.Writes.Any(w => w.Entity == "decision" && w.Effect == EffectKind.AcceptDecision);
        bool counts = row.Flag("countsTowardRequirement");
        StoredRow? decision = row.Text("decision") is string decisionId ? records.Get("decision", decisionId) : null;

        // What the engine's recompute did to the decision's other records in the same transaction: the records it opened
        // (a restart for a requirement this answer did not meet, or the next step of a sequential group) and the records it
        // cancelled because the decision settled. A replay or a no-op writes nothing, so both lists are empty then.
        string[] opened = outcome.Writes.Where(w => w.Entity == "approval-record" && w.Operation == WriteOperation.Create).Select(w => w.RowId).Distinct(StringComparer.Ordinal).ToArray();
        string[] cancelled = outcome.Writes
            .Where(w => w.Entity == "approval-record" && w.Operation == WriteOperation.Update && w.RowId != id && string.Equals(Js.Text(w.Columns, "outcomeMeaning"), "cancelled", StringComparison.Ordinal))
            .Select(w => w.RowId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        JsonTreeNode data = Js.Obj(
            ("approvalRecordId", JsonTreeNode.FromString(row.Id)),
            ("outcome", reader.StatusValue(row, "outcome", "outcomeMeaning")),
            ("countsTowardRequirement", JsonTreeNode.FromBoolean(counts)),
            ("decisionAccepted", JsonTreeNode.FromBoolean(accepted)),
            ("decisionId", Js.Str(row.Text("decision"))),
            ("decisionStatus", Js.Str(decision?.Text("statusMeaning"))),
            ("openedApprovalRecordIds", Js.Strs(opened)),
            ("cancelledApprovalRecordIds", Js.Strs(cancelled)));
        string summary = outcome.IsReplay
            ? "This answer was recorded before with the same Idempotency-Key, so the first result stands and nothing changed."
            : outcome.IsNoOp
            ? "The Approval Record already holds this outcome, so nothing changed."
            : accepted ? "The answer is recorded, and the engine accepted the decision."
            : string.Equals(decision?.Text("statusMeaning"), "rejected", StringComparison.Ordinal) ? "The answer is recorded, and the engine rejected the decision."
            : !counts && opened.Length > 0 ? "The answer is recorded but does not count, so the engine asks again with a new approval request."
            : counts && decision != null && string.Equals(decision.Text("statusMeaning"), "proposed", StringComparison.Ordinal)
                ? (records.Query(RowQuery.For("approval-record").Where("decision", decision.Id).Where("outcomeMeaning", "pending")).Count > 0
                    ? "The answer is recorded and counts. The decision still waits for other answers."
                    : "The answer is recorded and counts. The decision is not accepted yet; the warnings say why.")
            : "The answer is recorded.";
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

    // An answer is given after its request was made. The engine counts an answer only when its Responded On is before
    // Expires On, and the runner reports that time, so a Responded On before the record's Requested On (by more than a
    // few minutes of clock difference) is not an answer to this request: it is the shape of an old or late answer posted
    // again on the request that restarted it, which would otherwise count as given in time. Nothing is written.
    internal static McpError? BeforeRequest(StoredRow stored, JsonTreeNode body)
    {
        DateTime? requested = RecordEndpoints.Moment(stored.Text("requestedOn"));
        DateTime? responded = RecordEndpoints.Moment(Js.Text(body, "respondedOn"));
        if (!requested.HasValue || !responded.HasValue || responded.Value >= requested.Value - AnswerClockSkew)
        {
            return null;
        }

        return new McpError(
            McpErrorCodes.Validation,
            "respondedOn is before this approval request was made (" + stored.Text("requestedOn") + "), so it is not an answer to this request, and nothing is recorded.",
            "respondedOn");
    }

    private static JsonTreeNode WithMember(JsonTreeNode body, string name, JsonTreeNode value)
    {
        return Js.Obj(body.Members.Select(m => new KeyValuePair<string, JsonTreeNode>(
            m.Key,
            string.Equals(m.Key, name, StringComparison.Ordinal) ? value : m.Value)));
    }

    // The one answer the route turns down before the engine runs (409 conflict): for a record that holds a final outcome,
    // the same outcome from another responder or at another time. The engine compares outcomes only, so it would take
    // that answer as the same one again and answer that nothing changed; it is a second person's answer to a request
    // that already has its answer, and it is never recorded. Everything else goes to the engine, which decides: a pending
    // record takes the answer (an answer given at or after Expires On is kept but does not count, and the engine opens
    // a new request), a recorded Idempotency-Key returns the first result or refuses a reused key, the very same answer
    // is a no-op, and a different outcome is a conflict with the stored one (approval.outcome-final).
    private static McpError? Conflict(StoredRow stored, JsonTreeNode body, string? key, IRecordStore records)
    {
        string? meaning = stored.Text("outcomeMeaning");
        if (meaning == null || string.Equals(meaning, "pending", StringComparison.Ordinal) || !string.Equals(stored.Text("outcome"), Js.Text(body, "outcome"), StringComparison.Ordinal))
        {
            return null;
        }

        if (key != null && records.Query(RowQuery.For("audit-entry").Where("requestKey", key)).Count > 0)
        {
            return null;
        }

        bool same = string.Equals(stored.Text("approver"), Js.Text(body, "approver"), StringComparison.OrdinalIgnoreCase)
            && RecordEndpoints.Moment(stored.Text("respondedOn")) == RecordEndpoints.Moment(Js.Text(body, "respondedOn"));
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

        // Only the 36-character form with hyphens, the one Microsoft Entra ID and Microsoft Graph use. Every check that
        // tells people apart compares this ID (the Principal Reference that says an object is an agent, the Workload
        // Grants, the posting identity, the Owner Assignments, the count of one person once and the separation rules), so
        // another spelling of a GUID (no hyphens, braces, parentheses) would pass them all.
        string approverText = Js.Text(body, "approver")!;
        if (approverText.Length != 36 || !Guid.TryParseExact(approverText, "D", out _))
        {
            return new McpError(McpErrorCodes.Validation, "approver is the responder's Microsoft Entra object ID: a GUID of 36 characters with hyphens, as Microsoft Graph gives it.", "approver");
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
