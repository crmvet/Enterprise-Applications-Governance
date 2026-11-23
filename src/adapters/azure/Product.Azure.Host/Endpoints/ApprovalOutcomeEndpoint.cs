using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
/// development personas without that role get 403. The engine then refuses every writer but the approval runner (the
/// workload holding a current approval writer grant) and any AI actor, stores an answer that did not come from a person
/// signed in as a user as recommendation only, and on a counted approval recomputes the decision and accepts it itself
/// in the same transaction. No tool approves.
/// </summary>
public static class ApprovalOutcomeEndpoint
{
    private static readonly string[] Allowed =
    {
        "outcome", "approver", "approverPrincipalKind", "approverActorKind", "respondedOn", "channel", "typedReason", "approverHoldsRequiredRole", "flowRunUrl",
    };

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

        CallerFacts facts = services.GetRequiredService<CallerFacts>();
        body = WithRecordedPrincipalKind(body, facts);

        EngineRuntime runtime = services.GetRequiredService<EngineRuntime>();
        IRecordStore records = services.GetRequiredService<IRecordStore>();
        DateTime now = CallerFacts.Utc(runtime.Clock.UtcNow);
        CallerIdentity identity = facts.Identity(caller, "approval runner");
        RuntimeOutcome outcome = runtime.Write(identity, new WriteCommand("approval-record", WriteOperation.Update, id, body));
        if (!outcome.Succeeded)
        {
            await HttpJson.WriteFailure(context, ProductErrorMap.Map(outcome, contracts, aiCaller: false), contracts, now, outcome.ConfigurationVersion).ConfigureAwait(false);
            return;
        }

        CompiledSnapshot? snapshot = runtime.Cache.Load(services.GetRequiredService<IConfigurationStore>(), SnapshotPurpose.Read, now).Snapshot;
        StoredRow row = records.Get("approval-record", id) ?? throw new InvalidOperationException("Approval Record " + id + " was written but cannot be read back.");
        var reader = new RowFacts(records, snapshot, caller, now);
        bool accepted = outcome.Writes.Any(w => w.Entity == "decision" && w.Effect == EffectKind.AcceptDecision);
        JsonTreeNode data = Js.Obj(
            ("approvalRecordId", JsonTreeNode.FromString(row.Id)),
            ("outcome", reader.StatusValue(row, "outcome", "outcomeMeaning")),
            ("countsTowardRequirement", JsonTreeNode.FromBoolean(row.Flag("countsTowardRequirement"))),
            ("decisionAccepted", JsonTreeNode.FromBoolean(accepted)),
            ("decisionId", Js.Str(row.Text("decision"))));
        string summary = outcome.IsNoOp
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

        return Js.Obj(body.Members.Select(m => new KeyValuePair<string, JsonTreeNode>(
            m.Key,
            string.Equals(m.Key, "approverPrincipalKind", StringComparison.Ordinal) ? JsonTreeNode.FromString(recorded) : m.Value)));
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

        JsonTreeNode? holds = Js.Member(body, "approverHoldsRequiredRole");
        if (holds != null && holds.Kind != JsonTreeKind.True && holds.Kind != JsonTreeKind.False)
        {
            return new McpError(McpErrorCodes.Validation, "approverHoldsRequiredRole is true or false.", "approverHoldsRequiredRole");
        }

        return null;
    }
}
