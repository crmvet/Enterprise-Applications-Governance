using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;
using Product.Engine.Core.Snapshots;

namespace Product.Engine.Core.Operations;

// The part of an OP-14 result that OP-04 reads (document 25, sections 5.6 and 5.15). The wire reader takes the whole
// OP-14 payload as it came (decidingFactId and ignoredFacts too, which OP-04 does not read). A recorded external
// change number alone is never one.
public sealed class LinkedAuthorizationFact
{
    public LinkedAuthorizationFact(bool authorized, AuthorizationBasis basis)
    {
        Authorized = authorized;
        Basis = basis;
    }

    // True only when the latest counting fact says authorized.
    public bool Authorized { get; }

    public AuthorizationBasis Basis { get; }

    internal static LinkedAuthorizationFact? FromJson(WireObject? wire)
    {
        return wire == null ? null : new LinkedAuthorizationFact(wire.RequiredBool("authorized"), wire.Contract("basis", EngineContract.AuthorizationBases, AuthorizationBasis.None));
    }
}

// The linked change request's facts (document 25, section 5.6, ChangeFact).
public sealed class ChangeFact
{
    public ChangeFact(
        ChangeManagementMode changeManagementMode,
        bool? requiresAuthorization = null,
        bool? allowsAuthorizationAfter = null,
        ApprovalOverall? approvalOverall = null,
        LinkedAuthorizationFact? linkedAuthorization = null,
        DateTime? windowStartUtc = null,
        DateTime? windowEndUtc = null,
        bool deployerFlaggedBySeparation = false,
        bool hasExternalChangeNumber = false)
    {
        if ((windowStartUtc.HasValue && windowStartUtc.Value.Kind != DateTimeKind.Utc) || (windowEndUtc.HasValue && windowEndUtc.Value.Kind != DateTimeKind.Utc))
        {
            throw new ArgumentException("A change window is given in UTC date-times.");
        }

        ChangeManagementMode = changeManagementMode;
        RequiresAuthorization = requiresAuthorization;
        AllowsAuthorizationAfter = allowsAuthorizationAfter;
        ApprovalOverall = approvalOverall;
        LinkedAuthorization = linkedAuthorization;
        WindowStartUtc = windowStartUtc;
        WindowEndUtc = windowEndUtc;
        DeployerFlaggedBySeparation = deployerFlaggedBySeparation;
        HasExternalChangeNumber = hasExternalChangeNumber;
    }

    // Built in or linked, copied onto the change request at creation.
    public ChangeManagementMode ChangeManagementMode { get; }

    // From the change type (C20). Null reads the change type in the snapshot.
    public bool? RequiresAuthorization { get; }

    // From the change type (C20), true for emergencies. Null reads the change type in the snapshot.
    public bool? AllowsAuthorizationAfter { get; }

    // Built-in mode: the OP-03 overall result for the authorization decision. An engine-accepted standard change
    // passes satisfied. Null means not known, which never counts as authorized.
    public ApprovalOverall? ApprovalOverall { get; }

    // Linked mode: what OP-14 says.
    public LinkedAuthorizationFact? LinkedAuthorization { get; }

    public DateTime? WindowStartUtc { get; }

    public DateTime? WindowEndUtc { get; }

    // Record mode: OP-03, re-run with the deployers known, flagged a deployer who was a counting approver under an
    // "allow and flag" separation row (document 25, section 5.5). A proposal of this build: the runtime passes it.
    public bool DeployerFlaggedBySeparation { get; }

    // Whether the ITSM change number is recorded. Shown, never decisive: a recorded number alone never authorizes
    // (working default, 28 Q22; D-08). No rule reads it.
    public bool HasExternalChangeNumber { get; }

    internal static ChangeFact? FromJson(WireObject? wire)
    {
        if (wire == null)
        {
            return null;
        }

        ApprovalOverall? overall = wire.Has("approvalOverall") ? wire.Contract("approvalOverall", EngineContract.ApprovalOveralls) : null;
        return new ChangeFact(
            wire.Contract("changeManagementMode", EngineContract.ChangeManagementModes),
            wire.OptionalBool("requiresAuthorization"),
            wire.OptionalBool("allowsAuthorizationAfter"),
            overall,
            LinkedAuthorizationFact.FromJson(wire.OptionalObject("linkedAuthorization", "authorized", "basis", "decidingFactId", "ignoredFacts")),
            wire.OptionalUtc("windowStartUtc"),
            wire.OptionalUtc("windowEndUtc"),
            wire.Bool("deployerFlaggedBySeparation", false),
            wire.Bool("hasExternalChangeNumber", false));
    }
}

// OP-04 input (document 25, section 5.6).
public sealed class GateRequest
{
    public GateRequest(
        GateMode mode,
        string tierKey,
        string targetStageKey,
        string routeKey,
        IEnumerable<string> allowedRouteKeys,
        DateTime eventUtc,
        string? changeTypeKey = null,
        IEnumerable<string>? portfolioPath = null,
        ChangeFact? change = null,
        CodeApprovalKind codeApprovalKind = CodeApprovalKind.Unknown)
    {
        if (eventUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The event moment is a UTC date-time.", nameof(eventUtc));
        }

        Mode = mode;
        TierKey = tierKey ?? throw new ArgumentNullException(nameof(tierKey));
        TargetStageKey = targetStageKey ?? throw new ArgumentNullException(nameof(targetStageKey));
        RouteKey = routeKey ?? throw new ArgumentNullException(nameof(routeKey));
        AllowedRouteKeys = new ReadOnlyCollection<string>((allowedRouteKeys ?? throw new ArgumentNullException(nameof(allowedRouteKeys))).ToList());
        EventUtc = eventUtc;
        ChangeTypeKey = changeTypeKey;
        PortfolioPath = new ReadOnlyCollection<string>((portfolioPath ?? Enumerable.Empty<string>()).ToList());
        Change = change;
        CodeApprovalKind = codeApprovalKind;
    }

    // check (before the deploy) or record (after it).
    public GateMode Mode { get; }

    // The application's current tier.
    public string TierKey { get; }

    // The deployment target's stage (C34).
    public string TargetStageKey { get; }

    // The deployment route (C36) the record arrived by. The check step itself is the pipeline route.
    public string RouteKey { get; }

    // The routes the target's change path (C35) allows.
    public IReadOnlyList<string> AllowedRouteKeys { get; }

    // Planned start in check mode; finish time in record mode.
    public DateTime EventUtc { get; }

    // The change type, when a change request is linked. Without a change, the setting
    // unlinked-deployment-change-type names it (document 23, P05 step 4).
    public string? ChangeTypeKey { get; }

    // For portfolio-scoped gate policies, nearest first.
    public IReadOnlyList<string> PortfolioPath { get; }

    // The linked change request's facts. Null when none is linked.
    public ChangeFact? Change { get; }

    // Who approved the code merge, as the record step reports it. Evidence only, never counted.
    public CodeApprovalKind CodeApprovalKind { get; }

    public static GateRequest FromJson(JsonTreeNode input)
    {
        var wire = new WireObject(
            input,
            "input",
            "mode",
            "tierKey",
            "changeTypeKey",
            "targetStageKey",
            "portfolioPath",
            "change",
            "routeKey",
            "allowedRouteKeys",
            "eventUtc",
            "codeApprovalKind");
        return new GateRequest(
            wire.Contract("mode", EngineContract.GateModes),
            wire.RequiredKey("tierKey"),
            wire.RequiredKey("targetStageKey"),
            wire.RequiredKey("routeKey"),
            wire.Keys("allowedRouteKeys", required: true),
            wire.RequiredUtc("eventUtc"),
            wire.OptionalKey("changeTypeKey"),
            wire.Strings("portfolioPath"),
            ChangeFact.FromJson(wire.OptionalObject(
                "change",
                "changeManagementMode",
                "requiresAuthorization",
                "allowsAuthorizationAfter",
                "approvalOverall",
                "linkedAuthorization",
                "windowStartUtc",
                "windowEndUtc",
                "deployerFlaggedBySeparation",
                "hasExternalChangeNumber")),
            wire.Contract("codeApprovalKind", EngineContract.CodeApprovalKinds, CodeApprovalKind.Unknown));
    }
}

// OP-04 output (document 25, section 5.6, GateDecision).
public sealed class GateDecision : IOperationPayload
{
    public GateDecision(
        GateLevel gateLevel,
        GateVerdict verdict,
        bool isOutsideWindow,
        bool isOtherPath,
        bool isMissingApproval,
        bool isAiOnlyCodeApproval,
        bool notify,
        FailMode? failMode,
        DateTime? emergencyFollowUpDueUtc,
        string gatePolicyKey,
        string changeTypeKey,
        WindowState windowState,
        IEnumerable<string> notifyOwnerRoleKeys,
        IEnumerable<string> notifyRoleKeys)
    {
        GateLevel = gateLevel;
        Verdict = verdict;
        IsOutsideWindow = isOutsideWindow;
        IsOtherPath = isOtherPath;
        IsMissingApproval = isMissingApproval;
        IsAiOnlyCodeApproval = isAiOnlyCodeApproval;
        Notify = notify;
        FailMode = failMode;
        EmergencyFollowUpDueUtc = emergencyFollowUpDueUtc;
        GatePolicyKey = gatePolicyKey;
        ChangeTypeKey = changeTypeKey;
        WindowState = windowState;
        NotifyOwnerRoleKeys = new ReadOnlyCollection<string>(notifyOwnerRoleKeys.ToList());
        NotifyRoleKeys = new ReadOnlyCollection<string>(notifyRoleKeys.ToList());
    }

    public GateLevel GateLevel { get; }

    public GateVerdict Verdict { get; }

    // Flags for O14. At off no flag is stored, so all four are false.
    public bool IsOutsideWindow { get; }

    public bool IsOtherPath { get; }

    public bool IsMissingApproval { get; }

    public bool IsAiOnlyCodeApproval { get; }

    // Record mode only: whether flows should message the owner roles and roles of the policy's Flag Notify list,
    // or of the default-flag-notify setting when the row names none.
    public bool Notify { get; }

    // The policy's fail mode, for the check step to cache. Null below enforce, where the row has none (V24).
    public FailMode? FailMode { get; }

    // When the emergency's authorization must follow.
    public DateTime? EmergencyFollowUpDueUtc { get; }

    // The gate policy row that applied (a proposal of this build: the trace names it too).
    public string GatePolicyKey { get; }

    // The change type used to pick the row: the given one, or the unlinked deployment change type.
    public string ChangeTypeKey { get; }

    // Where the event falls against the change window (OP-05).
    public WindowState WindowState { get; }

    // Who flows tell when Notify is true. Empty otherwise.
    public IReadOnlyList<string> NotifyOwnerRoleKeys { get; }

    public IReadOnlyList<string> NotifyRoleKeys { get; }

    public JsonTreeNode ToJson()
    {
        return new WireWriter()
            .Add("gateLevel", EngineContract.GateLevels.ToWire(GateLevel))
            .Add("verdict", EngineContract.GateVerdicts.ToWire(Verdict))
            .Add("isOutsideWindow", IsOutsideWindow)
            .Add("isOtherPath", IsOtherPath)
            .Add("isMissingApproval", IsMissingApproval)
            .Add("isAiOnlyCodeApproval", IsAiOnlyCodeApproval)
            .Add("notify", Notify)
            .Add("failMode", FailMode.HasValue ? EngineContract.FailModes.ToWire(FailMode.Value) : null)
            .Add("emergencyFollowUpDueUtc", EmergencyFollowUpDueUtc)
            .Add("gatePolicyKey", GatePolicyKey)
            .Add("changeTypeKey", ChangeTypeKey)
            .Add("windowState", EngineContract.WindowStates.ToWire(WindowState))
            .Add("notifyOwnerRoleKeys", NotifyOwnerRoleKeys)
            .Add("notifyRoleKeys", NotifyRoleKeys)
            .Build();
    }
}

// OP-04 Evaluate gate decision (document 25, section 5.6; document 21, section 7). One operation serves both moments of
// the ladder (D-03): a check before a deploy (Advise and Enforce) and a record after it (Record and Flag).
// There is no fallback: a missing gate policy row is a broken configuration, and the engine refuses and reports it
// (working default, 28 issue 14). Window and path never stop a deployment.
public static class GateDecisionOperation
{
    private const string GatePolicyEntity = "gate-policy";

    public static EvaluationResult<GateDecision?> Evaluate(ConfigurationSnapshot snapshot, EvaluationContext context, GateRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return OperationShell.Run(context, snapshot, needsSnapshot: true, () => Answer(snapshot, request));
    }

    public static EvaluationResult<GateDecision?> Evaluate(ConfigurationSnapshot snapshot, EvaluationContext context, JsonTreeNode input)
    {
        return OperationShell.Run(context, snapshot, needsSnapshot: true, () => Answer(snapshot, GateRequest.FromJson(input)));
    }

    private static OperationAnswer<GateDecision> Answer(ConfigurationSnapshot snapshot, GateRequest request)
    {
        var trace = new List<RuleTrace>();
        var reasons = new List<Reason>();
        ChangeFact? change = request.Change;

        SnapshotRead.RequireExisting(snapshot, "tier", request.TierKey, "input.tierKey");
        SnapshotRead.RequireExisting(snapshot, "target-stage", request.TargetStageKey, "input.targetStageKey");
        SnapshotRead.RequireExisting(snapshot, "deployment-route", request.RouteKey, "input.routeKey");
        foreach (string allowed in request.AllowedRouteKeys)
        {
            SnapshotRead.RequireExisting(snapshot, "deployment-route", allowed, "input.allowedRouteKeys");
        }

        if (change != null)
        {
            ChangeWindowOperation.CheckWindow(change.WindowStartUtc, change.WindowEndUtc, "The change window of input.change");
        }

        SnapshotRow changeType = ChangeType(snapshot, request, trace);
        string changeTypeKey = changeType.KeyPath;

        // Rule 1: the C33 row for tier, change type and target stage. Portfolio rows first, nearest wins.
        SnapshotRow policy = ResolvePolicy(snapshot, request.TierKey, changeTypeKey, request.TargetStageKey, request.PortfolioPath, trace);
        GateLevel level = Contract(policy, "gateLevel", EngineContract.GateLevels) ?? throw BrokenRow(policy, "gateLevel");
        FailMode? failMode = Contract(policy, "failMode", EngineContract.FailModes);
        WindowResult window = ChangeWindowOperation.Check(change?.WindowStartUtc, change?.WindowEndUtc, request.EventUtc);

        if (level == GateLevel.Off)
        {
            // Off: not governed. No flag is stored and nobody is told (document 25, section 5.6).
            var off = new GateDecision(level, GateVerdict.NotApplicable, false, false, false, false, false, failMode, null, policy.KeyPath, changeTypeKey, window.State, Array.Empty<string>(), Array.Empty<string>());
            return new OperationAnswer<GateDecision>(EngineContract.GateVerdicts.ToWire(GateVerdict.NotApplicable), off, null, trace);
        }

        // Rule 2: the four flags.
        bool isOutsideWindow = window.IsOutside;
        bool isOtherPath = !request.AllowedRouteKeys.Contains(request.RouteKey, StringComparer.Ordinal);
        bool requiresAuthorization = change?.RequiresAuthorization ?? SnapshotRead.Flag(changeType, "requiresAuthorization");
        bool allowsAuthorizationAfter = change?.AllowsAuthorizationAfter ?? SnapshotRead.Flag(changeType, "allowsAuthorizationAfter");
        bool isMissingApproval = requiresAuthorization && !IsAuthorized(change);

        // WORKING DEFAULT (open design question 7): an AI-only code approval never counts as authorization, and the
        // shipped default handling flags it.
        AiOnlyCodeApprovalHandling handling = AiOnlyCodeApprovalHandling.Ignore;
        if (request.CodeApprovalKind == CodeApprovalKind.AiOnly)
        {
            handling = Contract(policy, "aiOnlyCodeApprovalHandling", EngineContract.AiOnlyCodeApprovalHandlings)
                ?? DefaultAiHandling(snapshot, trace);
        }

        bool isAiOnlyCodeApproval = handling != AiOnlyCodeApprovalHandling.Ignore;
        bool anyFlag = isOutsideWindow || isOtherPath || isMissingApproval || isAiOnlyCodeApproval;

        // Rule 5: an emergency whose authorization may follow gets a follow-up due date from the Emergency Rule Json.
        JsonTreeNode? emergencyRule = SnapshotRead.Node(policy, "emergencyRule");
        bool emergency = allowsAuthorizationAfter && isMissingApproval;
        DateTime? dueUtc = null;
        if (emergency && emergencyRule != null)
        {
            dueUtc = FollowUpDue(snapshot, policy, emergencyRule, request.EventUtc, trace);
        }

        // Rule 3: apply the level.
        GateVerdict verdict = Verdict(level, request.Mode, policy, anyFlag, isMissingApproval, handling, emergency, emergencyRule);
        bool notify = request.Mode == GateMode.Record && anyFlag && ContractFacts.LadderRank(level) >= ContractFacts.LadderRank(GateLevel.Flag);

        IReadOnlyList<string> notifyOwnerRoles = Array.Empty<string>();
        IReadOnlyList<string> notifyRoles = Array.Empty<string>();
        if (notify)
        {
            JsonTreeNode list = SnapshotRead.Node(policy, "flagNotify") ?? DefaultFlagNotify(snapshot, trace);
            notifyOwnerRoles = SnapshotRead.Strings(list, "ownerRoleKeys").Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToArray();
            notifyRoles = SnapshotRead.Strings(list, "roleKeys").Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        }

        AddFlag(reasons, isOutsideWindow, ReasonCodes.GateOutsideWindow, "Change Request", null);
        AddFlag(reasons, isOtherPath, ReasonCodes.GateOtherPath, "Deployment Route", request.RouteKey);
        AddFlag(reasons, isMissingApproval, ReasonCodes.GateMissingApproval, "Change Type", changeTypeKey);
        AddFlag(reasons, isAiOnlyCodeApproval, ReasonCodes.GateAiOnlyCodeApproval, "Gate Policy", policy.KeyPath);
        AddFlag(reasons, verdict == GateVerdict.AllowWithException, ReasonCodes.GateEmergencyException, "Gate Policy", policy.KeyPath);
        AddFlag(reasons, request.Mode == GateMode.Record && change != null && change.DeployerFlaggedBySeparation, ReasonCodes.ApprovalSeparationFlagged, "Deployment Record", null);

        var decision = new GateDecision(
            level,
            verdict,
            isOutsideWindow,
            isOtherPath,
            isMissingApproval,
            isAiOnlyCodeApproval,
            notify,
            failMode,
            dueUtc,
            policy.KeyPath,
            changeTypeKey,
            window.State,
            notifyOwnerRoles,
            notifyRoles);
        return new OperationAnswer<GateDecision>(EngineContract.GateVerdicts.ToWire(verdict), decision, reasons, trace);
    }

    // Check mode follows the ladder of document 25, section 5.6. Record mode never stops or warns: the deployment has
    // happened and the record is always written (fail soft, I-189); its flags carry the finding. Both modes record an
    // emergency exception at enforce.
    private static GateVerdict Verdict(
        GateLevel level,
        GateMode mode,
        SnapshotRow policy,
        bool anyFlag,
        bool isMissingApproval,
        AiOnlyCodeApprovalHandling handling,
        bool emergency,
        JsonTreeNode? emergencyRule)
    {
        if (level == GateLevel.Enforce)
        {
            bool aiStop = handling == AiOnlyCodeApprovalHandling.Stop;
            if (!aiStop && isMissingApproval && emergency)
            {
                // The emergency rule must exist here (V25). Without it the engine cannot say how the emergency passes.
                if (emergencyRule == null)
                {
                    throw OperationStop.Broken(
                        ReasonCodes.GatePolicyEmergencyRule,
                        "Gate Policy " + SnapshotRead.Quote(policy.KeyPath) + " enforces a change type that allows authorization after, but has no emergency rule. The configuration is broken.",
                        "Gate Policy",
                        policy.KeyPath);
                }

                if (emergencyRule.TryGetMember("passWithoutApproval", out JsonTreeNode? pass) && pass.Kind == JsonTreeKind.True)
                {
                    return GateVerdict.AllowWithException;
                }
            }

            if (mode == GateMode.Check && (aiStop || isMissingApproval))
            {
                return GateVerdict.Stop;
            }

            return GateVerdict.Allow;
        }

        if (level == GateLevel.Advise && mode == GateMode.Check && anyFlag)
        {
            return GateVerdict.Warn;
        }

        return GateVerdict.Allow;
    }

    // Built-in mode: the OP-03 result is satisfied. Linked mode: OP-14 says authorized. A recorded external change
    // number alone never counts (working default, 28 Q22; D-08). With no change linked there is no authorization.
    private static bool IsAuthorized(ChangeFact? change)
    {
        if (change == null)
        {
            return false;
        }

        return change.ChangeManagementMode == ChangeManagementMode.BuiltIn
            ? change.ApprovalOverall == ApprovalOverall.Satisfied
            : change.LinkedAuthorization != null && change.LinkedAuthorization.Authorized;
    }

    private static SnapshotRow ChangeType(ConfigurationSnapshot snapshot, GateRequest request, List<RuleTrace> trace)
    {
        if (request.ChangeTypeKey != null)
        {
            SnapshotRow given = SnapshotRead.RequireExisting(snapshot, "change-type", request.ChangeTypeKey, "input.changeTypeKey");
            trace.Add(new RuleTrace("Change Type", given.KeyPath, RuleTraceRole.Matched));
            return given;
        }

        if (request.Change != null)
        {
            throw OperationStop.Input(ReasonCodes.InputRequired, "input.changeTypeKey is required when a change request is linked.");
        }

        JsonTreeNode value = SnapshotRead.RequireSetting(
            snapshot,
            EngineKeys.UnlinkedDeploymentChangeType,
            node => node.Kind == JsonTreeKind.String && ConfigurationKey.IsValid(node.GetString()),
            "the key of a change type");
        trace.Add(new RuleTrace("Setting", EngineKeys.UnlinkedDeploymentChangeType, RuleTraceRole.Matched));
        SnapshotRow? row = SnapshotRead.Find(snapshot, "change-type", value.GetString());
        if (row == null)
        {
            throw OperationStop.Broken(
                ReasonCodes.SettingRequired,
                "The setting " + SnapshotRead.Quote(EngineKeys.UnlinkedDeploymentChangeType) + " names change type " + SnapshotRead.Quote(value.GetString()) + ", which does not exist. The configuration is broken.",
                "Setting",
                EngineKeys.UnlinkedDeploymentChangeType);
        }

        trace.Add(new RuleTrace("Change Type", row.KeyPath, RuleTraceRole.Matched));
        return row;
    }

    // The gate policy row for one combination. A row with a portfolio in the path wins over the row without one, and
    // the nearest portfolio wins. No row is gate-policy.missing; two rows for one place are gate-policy.coverage.
    private static SnapshotRow ResolvePolicy(ConfigurationSnapshot snapshot, string tierKey, string changeTypeKey, string targetStageKey, IReadOnlyList<string> portfolioPath, List<RuleTrace> trace)
    {
        List<SnapshotRow> rows = SnapshotRead.ActiveRows(snapshot, GatePolicyEntity)
            .Where(row => string.Equals(SnapshotRead.Text(row, "tierKey"), tierKey, StringComparison.Ordinal)
                && string.Equals(SnapshotRead.Text(row, "changeTypeKey"), changeTypeKey, StringComparison.Ordinal)
                && string.Equals(SnapshotRead.Text(row, "targetStageKey"), targetStageKey, StringComparison.Ordinal))
            .ToList();
        string place = "tier " + SnapshotRead.Quote(tierKey) + ", change type " + SnapshotRead.Quote(changeTypeKey) + " and target stage " + SnapshotRead.Quote(targetStageKey);

        List<SnapshotRow> candidates = new();
        for (int i = 0; i < portfolioPath.Count && candidates.Count == 0; i++)
        {
            candidates = rows.Where(row => string.Equals(SnapshotRead.Text(row, ScopeGroups.PortfolioColumn), portfolioPath[i], StringComparison.Ordinal)).ToList();
        }

        if (candidates.Count == 0)
        {
            candidates = rows.Where(row => !SnapshotRead.HasValue(row, ScopeGroups.PortfolioColumn)).ToList();
        }

        if (candidates.Count == 0)
        {
            throw OperationStop.Broken(
                ReasonCodes.GatePolicyMissing,
                "No gate policy row covers " + place + ", so the configuration is broken. There is no fallback level: the check step applies its cached fail mode, and a record is retried later.",
                "Gate Policy",
                null);
        }

        if (candidates.Count > 1)
        {
            throw OperationStop.Broken(
                ReasonCodes.GatePolicyCoverage,
                place + " resolves to " + candidates.Count + " gate policy rows (" + string.Join(", ", candidates.Select(row => SnapshotRead.Quote(row.KeyPath)))
                + "), so the configuration is broken.",
                "Gate Policy",
                candidates[1].KeyPath);
        }

        SnapshotRow winner = candidates[0];
        trace.Add(new RuleTrace("Gate Policy", winner.KeyPath, RuleTraceRole.Won));
        foreach (SnapshotRow row in rows.Where(row => !ReferenceEquals(row, winner)))
        {
            trace.Add(new RuleTrace("Gate Policy", row.KeyPath, RuleTraceRole.Dropped));
        }

        return winner;
    }

    private static AiOnlyCodeApprovalHandling DefaultAiHandling(ConfigurationSnapshot snapshot, List<RuleTrace> trace)
    {
        JsonTreeNode value = SnapshotRead.RequireSetting(
            snapshot,
            EngineKeys.DefaultAiOnlyCodeApprovalHandling,
            node => node.Kind == JsonTreeKind.String && EngineContract.AiOnlyCodeApprovalHandlings.IsDefined(node.GetString()),
            "ignore, flag or stop");
        trace.Add(new RuleTrace("Setting", EngineKeys.DefaultAiOnlyCodeApprovalHandling, RuleTraceRole.Matched));
        return EngineContract.AiOnlyCodeApprovalHandlings.Parse(value.GetString());
    }

    private static JsonTreeNode DefaultFlagNotify(ConfigurationSnapshot snapshot, List<RuleTrace> trace)
    {
        JsonTreeNode value = SnapshotRead.RequireSetting(
            snapshot,
            EngineKeys.DefaultFlagNotify,
            node => node.Kind == JsonTreeKind.Object,
            "an object with ownerRoleKeys and roleKeys");
        trace.Add(new RuleTrace("Setting", EngineKeys.DefaultFlagNotify, RuleTraceRole.Matched));
        return value;
    }

    // The follow-up due date: approvalDueWorkingDays working days after the event, counting the weekdays of the
    // working-days setting (Monday to Friday as shipped; holiday calendars later, a proposal).
    private static DateTime? FollowUpDue(ConfigurationSnapshot snapshot, SnapshotRow policy, JsonTreeNode emergencyRule, DateTime eventUtc, List<RuleTrace> trace)
    {
        if (!emergencyRule.TryGetMember("approvalDueWorkingDays", out JsonTreeNode? daysNode) || !daysNode.TryGetInt64(out long days) || days < 0 || days > 3650)
        {
            throw OperationStop.Broken(
                ReasonCodes.GatePolicyEmergencyRule,
                "The emergency rule of Gate Policy " + SnapshotRead.Quote(policy.KeyPath) + " has no usable approvalDueWorkingDays, so the configuration is broken.",
                "Gate Policy",
                policy.KeyPath);
        }

        HashSet<DayOfWeek> workingDays = WorkingDays(snapshot);
        trace.Add(new RuleTrace("Setting", EngineKeys.WorkingDays, RuleTraceRole.Matched));
        DateTime due = eventUtc;
        long counted = 0;
        while (counted < days)
        {
            due = due.AddDays(1);
            if (workingDays.Contains(due.DayOfWeek))
            {
                counted++;
            }
        }

        return due;
    }

    private static HashSet<DayOfWeek> WorkingDays(ConfigurationSnapshot snapshot)
    {
        var names = new Dictionary<string, DayOfWeek>(StringComparer.Ordinal)
        {
            { "monday", DayOfWeek.Monday },
            { "tuesday", DayOfWeek.Tuesday },
            { "wednesday", DayOfWeek.Wednesday },
            { "thursday", DayOfWeek.Thursday },
            { "friday", DayOfWeek.Friday },
            { "saturday", DayOfWeek.Saturday },
            { "sunday", DayOfWeek.Sunday },
        };
        JsonTreeNode value = SnapshotRead.RequireSetting(
            snapshot,
            EngineKeys.WorkingDays,
            node => node.Kind == JsonTreeKind.Array && node.Items.Count > 0 && node.Items.All(item => item.Kind == JsonTreeKind.String && names.ContainsKey(item.GetString())),
            "a non-empty list of weekday names such as monday");
        return new HashSet<DayOfWeek>(value.Items.Select(item => names[item.GetString()]));
    }

    private static T? Contract<T>(SnapshotRow row, string column, ContractList<T> list)
        where T : struct, Enum
    {
        string? text = SnapshotRead.Text(row, column);
        if (text == null)
        {
            return null;
        }

        return list.TryParse(text, out T value) ? value : throw BrokenRow(row, column);
    }

    private static OperationStop BrokenRow(SnapshotRow row, string column)
    {
        return OperationStop.Broken(
            ReasonCodes.ShapeSchema,
            "Gate Policy " + SnapshotRead.Quote(row.KeyPath) + " has no known " + column + ", so the configuration is broken.",
            "Gate Policy",
            row.KeyPath);
    }

    private static void AddFlag(List<Reason> reasons, bool isSet, string code, string entity, string? reference)
    {
        if (isSet)
        {
            reasons.Add(ReasonCatalog.Create(code, entity, reference));
        }
    }
}
