using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Product.Azure.Stores;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Runtime;
using Product.Engine.Runtime.Callers;
using Product.Engine.Runtime.Ports;
using Product.Engine.Runtime.Snapshots;
using Product.Mcp.Core.Backend.InProcess;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Envelope;
using Product.Mcp.Core.Json;

namespace Product.Azure.Host;

/// <summary>
/// The approval runner's routes over the approval outbox. The runner pulls: it claims pending items with a lease, sends
/// one approval card per item, reports the card as sent, and gives an item back when it cannot send or wait. The answer
/// goes to <c>POST /engine/v1/approval-records/{id}/outcome</c>, where the engine records and counts it.
/// <para>
/// Every route takes only an app-only token with the <c>Approval.Write</c> app role (the approval writer policy), and
/// each handler resolves the caller against the configuration in force: only a workload holding a current approval
/// writer Workload Grant is the approval runner. A person, an AI client, a pipeline and an agent identity get 403.
/// </para>
/// </summary>
public static class ApprovalRunnerEndpoints
{
    /// <summary>The members a claim may name.</summary>
    internal static readonly string[] ClaimMembers = { "max", "leaseSeconds" };

    /// <summary>The members of a sent report, all required.</summary>
    internal static readonly string[] SentMembers = { "claimId", "cardRef", "runnerRunUrl" };

    /// <summary>The members of a release, all required.</summary>
    internal static readonly string[] ReleaseMembers = { "claimId", "reason" };

    /// <summary>Why a runner gives an item back. A closed list, so nothing a person wrote reaches the log.</summary>
    internal static readonly string[] ReleaseReasons = { "timeout", "send-failed", "no-approvers", "outcome-refused", "other" };

    private const int MaxClaimIdLength = 64;
    private const int MaxCardRefLength = 400;
    private const int MaxRunUrlLength = 2048;

    // Candidates read per claim beyond the items it may return, so rows that settle on the way do not starve it.
    private const int CandidateSlack = 50;

    /// <summary><c>POST /engine/v1/approval-runner/claims</c>: leases up to <c>max</c> pending items.</summary>
    public static async Task HandleClaims(HttpContext context)
    {
        RunnerCall? call = await Begin(context, ClaimMembers, Array.Empty<string>()).ConfigureAwait(false);
        if (call == null)
        {
            return;
        }

        if (!WholeNumber(call.Body, "max", out long? maxValue) || (maxValue.HasValue && maxValue.Value < 1))
        {
            await call.Fail(new McpError(McpErrorCodes.Validation, "max is a whole number of 1 or more.", "max")).ConfigureAwait(false);
            return;
        }

        if (!WholeNumber(call.Body, "leaseSeconds", out long? leaseValue)
            || (leaseValue.HasValue && (leaseValue.Value < ApprovalRunnerOptions.MinLeaseSeconds || leaseValue.Value > ApprovalRunnerOptions.MaxLeaseSeconds)))
        {
            await call.Fail(new McpError(McpErrorCodes.Validation, "leaseSeconds is a whole number from " + ApprovalRunnerOptions.MinLeaseSeconds + " to " + ApprovalRunnerOptions.MaxLeaseSeconds + ".", "leaseSeconds")).ConfigureAwait(false);
            return;
        }

        int max = (int)Math.Min(maxValue ?? call.Options.MaxClaim, call.Options.MaxClaim);
        int lease = (int)(leaseValue ?? call.Options.DefaultLeaseSeconds);
        IServiceProvider services = context.RequestServices;
        EngineRuntime runtime = services.GetRequiredService<EngineRuntime>();
        IConfigurationStore configuration = services.GetRequiredService<IConfigurationStore>();
        IRecordStore records = services.GetRequiredService<IRecordStore>();
        CompiledSnapshot snapshot = call.Snapshot;

        // Step-up sign-in on (D-38; document 26, B26-17): a person answers only on the decision page, so each item tells the
        // runner to send a link-only notification there, never an answerable card.
        ProductStepUpOptions stepUp = services.GetService<IOptions<ProductWebAppOptions>>()?.Value.StepUp ?? new ProductStepUpOptions();
        bool linkOnly = stepUp.Mode != ProductStepUpMode.Off;
        var cards = new ApprovalRequestCards(
            records,
            snapshot,
            version => runtime.Cache.LoadVersion(configuration, snapshot.ConfigurationSetKey, version).Snapshot,
            services.GetRequiredService<InProcessBackend>().Options.RecordUrlBase,
            linkOnly);

        // First the engine brings the due approvals up to date, as the approval runner: it expires what passed its
        // Expires On, cancels what a settled decision still holds open, and opens the restarts. Its own commits settle
        // and add the outbox rows, so the claim below sees the result.
        var refresh = new ApprovalRefresh(runtime, call.Identity, call.Logger);
        refresh.Due(call.Options.MaxRefresh);

        var items = new List<JsonTreeNode>();
        int settled = 0;
        foreach (ApprovalOutboxItem candidate in call.Outbox.Candidates(call.Now, max + CandidateSlack))
        {
            if (items.Count >= max)
            {
                break;
            }

            StoredRow? record = records.Get("approval-record", candidate.ApprovalRecordId);
            StoredRow? decision = record == null ? null : records.Get("decision", record.Text("decision") ?? candidate.DecisionId);
            string? state = ApprovalRequestCards.SettledState(record, decision, call.Now);
            if (state != null)
            {
                // A record the engine still holds pending (the refresh above stopped at its batch, say) is brought up to
                // date by the engine now; its commit settles the row. The settle below changes nothing then, and stops
                // the card in any case.
                if (decision != null && string.Equals(record?.Text("outcomeMeaning"), "pending", StringComparison.Ordinal))
                {
                    refresh.Decision(decision.Id);
                }

                call.Outbox.Settle(candidate.ApprovalRecordId, state, call.Now);
                settled++;
                continue;
            }

            ApprovalOutboxItem? claimed = call.Outbox.TryClaim(candidate, NewClaimId(), call.Now, TimeSpan.FromSeconds(lease), call.Options.MaxAttempts);
            if (claimed != null)
            {
                items.Add(cards.Item(claimed, record!, decision!, call.Now));
            }
        }

        call.Logger.LogInformation(
            "Approval runner claimed {Claimed} item(s) for {LeaseSeconds} seconds; {Settled} item(s) needed no card. The refresh brought {Decisions} decision(s) up to date: {Expired} record(s) expired, {Cancelled} canceled, {Opened} opened.",
            items.Count,
            lease,
            settled,
            refresh.Decisions,
            refresh.Expired,
            refresh.Cancelled,
            refresh.Opened);
        JsonTreeNode data = Js.Obj(
            ("items", Js.Arr(items)),
            ("leaseSeconds", JsonTreeNode.FromInt64(lease)),
            ("delivery", JsonTreeNode.FromString(linkOnly ? ApprovalRequestCards.LinkOnlyDelivery : ApprovalRequestCards.CardDelivery)),
            ("refresh", refresh.Summary()));
        string summary = items.Count == 0
            ? "No approval request is waiting for a card."
            : items.Count.ToString(CultureInfo.InvariantCulture) + (items.Count == 1 ? " approval request is" : " approval requests are") + " claimed for " + lease.ToString(CultureInfo.InvariantCulture) + " seconds. "
                + (linkOnly
                    ? "Step-up sign-in is on: send each person a notification that links to the decision page, never an answerable card, then report it as sent. The answer comes from the page."
                    : "Send one card for each, then report it as sent.");
        await call.Succeed(data, summary).ConfigureAwait(false);
    }

    /// <summary><c>POST /engine/v1/approval-runner/items/{id}/sent</c>: the card for a claimed item went out.</summary>
    public static async Task HandleSent(HttpContext context)
    {
        RunnerCall? call = await Begin(context, SentMembers, SentMembers).ConfigureAwait(false);
        if (call == null)
        {
            return;
        }

        string id = RouteId(context);
        string claimId = Js.Text(call.Body, "claimId")!;
        string cardRef = Js.Text(call.Body, "cardRef")!;
        string runUrl = Js.Text(call.Body, "runnerRunUrl")!;
        McpError? problem = CheckId(id) ?? CheckClaimId(claimId);
        if (problem == null && cardRef.Length > MaxCardRefLength)
        {
            problem = new McpError(McpErrorCodes.Validation, "cardRef is at most " + MaxCardRefLength + " characters.", "cardRef");
        }

        if (problem == null && (runUrl.Length > MaxRunUrlLength || !Uri.TryCreate(runUrl, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps))
        {
            problem = new McpError(McpErrorCodes.Validation, "runnerRunUrl is an https URL of at most " + MaxRunUrlLength + " characters.", "runnerRunUrl");
        }

        if (problem != null)
        {
            await call.Fail(problem).ConfigureAwait(false);
            return;
        }

        ApprovalOutboxResult result = call.Outbox.MarkSent(id, claimId, cardRef, runUrl, call.Now);
        switch (result.Kind)
        {
            case ApprovalOutboxResultKind.NotFound:
                await call.Fail(new McpError(McpErrorCodes.NotFound, "No approval request " + id + " is in the outbox.", "id")).ConfigureAwait(false);
                return;
            case ApprovalOutboxResultKind.StaleClaim:
                await call.Fail(StaleClaim(id, result.Item)).ConfigureAwait(false);
                return;
        }

        bool repeated = result.Kind == ApprovalOutboxResultKind.Repeated;
        JsonTreeNode data = Js.Obj(
            ("approvalRecordId", JsonTreeNode.FromString(id)),
            ("state", JsonTreeNode.FromString(result.Item!.State)),
            ("repeated", JsonTreeNode.FromBoolean(repeated)));
        await call.Succeed(data, repeated ? "This card was reported as sent before, so nothing changed." : "The card is recorded as sent. Post the answer to the approval outcome endpoint when it comes.").ConfigureAwait(false);
    }

    /// <summary>
    /// <c>POST /engine/v1/approval-runner/items/{id}/release</c>: gives a claimed item back. It is pending again with one
    /// more attempt, or failed once it has used its attempts.
    /// </summary>
    public static async Task HandleRelease(HttpContext context)
    {
        RunnerCall? call = await Begin(context, ReleaseMembers, ReleaseMembers).ConfigureAwait(false);
        if (call == null)
        {
            return;
        }

        string id = RouteId(context);
        string claimId = Js.Text(call.Body, "claimId")!;
        string reason = Js.Text(call.Body, "reason")!;
        McpError? problem = CheckId(id) ?? CheckClaimId(claimId);
        if (problem == null && Array.IndexOf(ReleaseReasons, reason) < 0)
        {
            problem = new McpError(McpErrorCodes.Validation, "reason is one of " + string.Join(", ", ReleaseReasons) + ".", "reason");
        }

        if (problem != null)
        {
            await call.Fail(problem).ConfigureAwait(false);
            return;
        }

        ApprovalOutboxResult result = call.Outbox.Release(id, claimId, call.Now, call.Options.MaxAttempts);
        switch (result.Kind)
        {
            case ApprovalOutboxResultKind.NotFound:
                await call.Fail(new McpError(McpErrorCodes.NotFound, "No approval request " + id + " is in the outbox.", "id")).ConfigureAwait(false);
                return;
            case ApprovalOutboxResultKind.StaleClaim:
                await call.Fail(StaleClaim(id, result.Item)).ConfigureAwait(false);
                return;
        }

        ApprovalOutboxItem item = result.Item!;
        call.Logger.LogInformation("Approval runner released {ApprovalRecordId} ({Reason}); attempts {Attempts}, now {State}.", id, reason, item.Attempts, item.State);
        JsonTreeNode data = Js.Obj(
            ("approvalRecordId", JsonTreeNode.FromString(id)),
            ("state", JsonTreeNode.FromString(item.State)),
            ("attempts", JsonTreeNode.FromInt64(item.Attempts)));
        string summary = item.State == ApprovalOutboxStates.Failed
            ? "The request is given back and has used its attempts, so it is failed: an admin looks at it. No new card goes out for it."
            : "The request is given back and waits for the next claim.";
        await call.Succeed(data, summary).ConfigureAwait(false);
    }

    // ---- Shared ----

    private sealed class RunnerCall
    {
        private readonly HttpContext _context;
        private readonly ContractSet _contracts;

        public RunnerCall(HttpContext context, ContractSet contracts, JsonTreeNode body, CompiledSnapshot snapshot, DateTime now, ApprovalRunnerOptions options, ApprovalOutbox outbox, ILogger logger, CallerIdentity identity)
        {
            _context = context;
            _contracts = contracts;
            Body = body;
            Snapshot = snapshot;
            Now = now;
            Options = options;
            Outbox = outbox;
            Logger = logger;
            Identity = identity;
        }

        public JsonTreeNode Body { get; }

        // The caller as the engine sees it: the approval runner, resolved against the configuration in force.
        public CallerIdentity Identity { get; }

        public CompiledSnapshot Snapshot { get; }

        public DateTime Now { get; }

        public ApprovalRunnerOptions Options { get; }

        public ApprovalOutbox Outbox { get; }

        public ILogger Logger { get; }

        public Task Fail(McpError error)
        {
            return HttpJson.WriteFailure(_context, error, _contracts, Now, Snapshot.VersionNumber);
        }

        public Task Succeed(JsonTreeNode data, string summary)
        {
            return HttpJson.Write(_context, StatusCodes.Status200OK, Envelope.Success(data, summary, Now, Snapshot.VersionNumber, ProductRequest.CorrelationId(_context)));
        }
    }

    // The checks every runner route makes before it acts: an app-only caller, the routes switched on, a configuration in
    // force, the caller resolved as the approval runner, and a closed JSON body. Writes the refusal and returns null when
    // one fails.
    private static async Task<RunnerCall?> Begin(HttpContext context, string[] allowed, string[] required)
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
            await HttpJson.WriteFailure(context, new McpError(McpErrorCodes.NotAuthorized, NotRunner), contracts).ConfigureAwait(false);
            return null;
        }

        ApprovalRunnerOptions options = services.GetService<IOptions<ApprovalRunnerOptions>>()?.Value ?? new ApprovalRunnerOptions();
        if (!options.Enabled)
        {
            await HttpJson.WriteFailure(context, new McpError(McpErrorCodes.NotFound, "The approval runner's routes are switched off on this server (Product:ApprovalRunner:Enabled)."), contracts).ConfigureAwait(false);
            return null;
        }

        EngineRuntime runtime = services.GetRequiredService<EngineRuntime>();
        DateTime now = CallerFacts.Utc(runtime.Clock.UtcNow);
        CompiledSnapshot? snapshot = runtime.Cache.Load(services.GetRequiredService<IConfigurationStore>(), SnapshotPurpose.Read, now).Snapshot;
        if (snapshot == null)
        {
            await HttpJson.WriteFailure(context, new McpError(McpErrorCodes.ConfigurationUnavailable, "No configuration version is in force, so no Workload Grant names an approval runner yet."), contracts).ConfigureAwait(false);
            return null;
        }

        CallerIdentity identity = services.GetRequiredService<CallerFacts>().Identity(caller, "approval runner");
        CallerResolution resolution = CallerResolver.Resolve(snapshot.Snapshot, identity, now);
        if (resolution.Actor == null || resolution.Actor.ActorKind != ActorKindMeaning.ApprovalRunner)
        {
            await HttpJson.WriteFailure(context, new McpError(McpErrorCodes.NotAuthorized, NotRunner + (resolution.Refusal == null ? string.Empty : " " + resolution.Refusal.Message)), contracts, now, snapshot.VersionNumber).ConfigureAwait(false);
            return null;
        }

        string? text = await HttpJson.ReadBody(context, services.GetRequiredService<IOptions<ProductHostOptions>>().Value.MaxRequestBytes).ConfigureAwait(false);
        if (text == null || !HttpJson.IsJson(context.Request.ContentType) || !CanonicalJson.TryParse(text, out JsonTreeNode? body, out _) || body.Kind != JsonTreeKind.Object)
        {
            await HttpJson.WriteFailure(context, new McpError(McpErrorCodes.Validation, "The body is one JSON object, sent as application/json.", "body"), contracts, now, snapshot.VersionNumber).ConfigureAwait(false);
            return null;
        }

        foreach (KeyValuePair<string, JsonTreeNode> member in body.Members)
        {
            if (Array.IndexOf(allowed, member.Key) < 0)
            {
                await HttpJson.WriteFailure(context, new McpError(McpErrorCodes.Validation, "The member " + member.Key + " is not part of this request. It takes " + string.Join(", ", allowed) + ".", member.Key), contracts, now, snapshot.VersionNumber).ConfigureAwait(false);
                return null;
            }
        }

        foreach (string name in required)
        {
            if (Js.Text(body, name) is not string value || value.Length == 0)
            {
                await HttpJson.WriteFailure(context, new McpError(McpErrorCodes.Validation, name + " is required, as a string.", name), contracts, now, snapshot.VersionNumber).ConfigureAwait(false);
                return null;
            }
        }

        ApprovalOutbox outbox = services.GetService<ApprovalOutbox>() ?? new ApprovalOutbox(services.GetRequiredService<IProductStore>());
        ILogger logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ApprovalRunnerEndpoints).FullName!);
        return new RunnerCall(context, contracts, body, snapshot, now, options, outbox, logger, identity);
    }

    // The engine's approval refresh, run as the approval runner (the caller of the claim): the engine finds the decisions
    // with approval work due and, decision by decision, writes expired or cancelled and opens the restarts (document 23,
    // S1 step 9 and S2 step 1). Every write is the engine's own; the host only asks and counts. A refused or failed
    // refresh is logged with its reason code and left for the next claim: it never stops the claim.
    private sealed class ApprovalRefresh
    {
        private readonly EngineRuntime _runtime;
        private readonly CallerIdentity _runner;
        private readonly ILogger _logger;
        private readonly HashSet<string> _done = new(StringComparer.Ordinal);

        public ApprovalRefresh(EngineRuntime runtime, CallerIdentity runner, ILogger logger)
        {
            _runtime = runtime;
            _runner = runner;
            _logger = logger;
        }

        public int Decisions { get; private set; }

        public int Expired { get; private set; }

        public int Cancelled { get; private set; }

        public int Opened { get; private set; }

        // Refreshes up to max decisions the engine lists as due, in its order.
        public void Due(int max)
        {
            RuntimeOutcome found = _runtime.FindDueApprovals(_runner, max);
            if (!found.Succeeded)
            {
                _logger.LogWarning("The engine did not list the approvals due ({Outcome}, {Reason}).", found.Outcome, Code(found));
                return;
            }

            foreach (string decisionId in Strings(found.Payload, "decisionIds"))
            {
                Decision(decisionId);
            }

            if (Js.Member(found.Payload!, "more")?.Kind == JsonTreeKind.True)
            {
                _logger.LogInformation("More decisions have approval work due than one claim refreshes ({Max}); the next claim goes on.", max);
            }
        }

        // Refreshes one decision, once per claim.
        public void Decision(string decisionId)
        {
            if (!_done.Add(decisionId))
            {
                return;
            }

            RuntimeOutcome outcome = _runtime.RefreshApprovals(_runner, decisionId);
            if (!outcome.Succeeded)
            {
                _logger.LogWarning("The approval refresh of decision {DecisionId} did not run ({Outcome}, {Reason}).", decisionId, outcome.Outcome, Code(outcome));
                return;
            }

            Decisions++;
            Expired += Strings(outcome.Payload, "expiredRecordIds").Count;
            Cancelled += Strings(outcome.Payload, "cancelledRecordIds").Count;
            Opened += Strings(outcome.Payload, "openedRecordIds").Count;
        }

        public JsonTreeNode Summary()
        {
            return Js.Obj(
                ("decisions", JsonTreeNode.FromInt64(Decisions)),
                ("expiredRecords", JsonTreeNode.FromInt64(Expired)),
                ("cancelledRecords", JsonTreeNode.FromInt64(Cancelled)),
                ("openedRecords", JsonTreeNode.FromInt64(Opened)));
        }

        private static IReadOnlyList<string> Strings(JsonTreeNode? payload, string name)
        {
            JsonTreeNode? list = payload == null ? null : Js.Member(payload, name);
            return list == null || list.Kind != JsonTreeKind.Array
                ? Array.Empty<string>()
                : list.Items.Where(i => i.Kind == JsonTreeKind.String).Select(i => i.GetString()).ToList();
        }

        private static string Code(RuntimeOutcome outcome)
        {
            return outcome.Reasons.Count == 0 ? "no reason" : outcome.Reasons[0].Code;
        }
    }

    private const string NotRunner = "Only the approval runner uses this route: an app-only token with the Approval.Write app role, for a workload that holds a current approval writer Workload Grant. AI clients never approve.";

    // True when the caller resolves, against the configuration in force, as the approval runner: an app-only workload
    // holding a current approval writer Workload Grant, and not an agent identity.
    internal static bool IsApprovalRunner(IServiceProvider services, CallerClaims caller, DateTime nowUtc)
    {
        if (caller == null || !caller.IsAppOnly)
        {
            return false;
        }

        EngineRuntime runtime = services.GetRequiredService<EngineRuntime>();
        CompiledSnapshot? snapshot = runtime.Cache.Load(services.GetRequiredService<IConfigurationStore>(), SnapshotPurpose.Read, nowUtc).Snapshot;
        if (snapshot == null)
        {
            return false;
        }

        CallerResolution resolution = CallerResolver.Resolve(snapshot.Snapshot, services.GetRequiredService<CallerFacts>().Identity(caller, "approval runner"), nowUtc);
        return resolution.Actor != null && resolution.Actor.ActorKind == ActorKindMeaning.ApprovalRunner;
    }

    private static McpError StaleClaim(string id, ApprovalOutboxItem? item)
    {
        string state = item == null ? "gone" : item.State;
        return new McpError(McpErrorCodes.Conflict, "The claim is not the current claim of approval request " + id + " (it is " + state + " now): another runner call claimed it, its lease ran out and it was claimed again, or it moved on. Drop this card's work; do not send a second card.");
    }

    private static string RouteId(HttpContext context)
    {
        return context.Request.RouteValues.TryGetValue("id", out object? value) ? value as string ?? string.Empty : string.Empty;
    }

    private static McpError? CheckId(string id)
    {
        return string.IsNullOrWhiteSpace(id) || id.Length > RowStore.MaxIdLength ? new McpError(McpErrorCodes.Validation, "The route names the Approval Record by its ID.", "id") : null;
    }

    private static McpError? CheckClaimId(string claimId)
    {
        return claimId.Length > MaxClaimIdLength ? new McpError(McpErrorCodes.Validation, "claimId is at most " + MaxClaimIdLength + " characters, as the claim gave it.", "claimId") : null;
    }

    // True when the member is absent, null or a whole number; the value when it is a number.
    private static bool WholeNumber(JsonTreeNode body, string name, out long? value)
    {
        value = null;
        JsonTreeNode? member = Js.Member(body, name);
        if (member == null || member.Kind == JsonTreeKind.Null)
        {
            return true;
        }

        if (member.Kind != JsonTreeKind.Number)
        {
            return false;
        }

        value = Js.Number(body, name);
        return value.HasValue;
    }

    private static string NewClaimId()
    {
        return "claim-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
    }
}
