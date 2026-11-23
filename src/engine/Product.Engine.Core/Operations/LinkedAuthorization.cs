using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;
using Product.Engine.Core.Snapshots;

namespace Product.Engine.Core.Operations;

// One recorded authorization fact on a linked change request (document 25, section 5.15, AuthorizationFact). Stored as
// an Evidence Link (O20) on the change request.
public sealed class AuthorizationFact
{
    public AuthorizationFact(
        string factId,
        AuthorizationFactSourceKind sourceKind,
        AuthorizationState state,
        bool hasEvidenceUrl,
        DateTime reportedOnUtc,
        ActorKindMeaning actorKind,
        PrincipalKind principalKind,
        bool attesterHoldsRole = false,
        bool answeredOnConfirmationCard = false)
    {
        if (string.IsNullOrEmpty(factId))
        {
            throw new ArgumentException("A fact names its Evidence Link row.", nameof(factId));
        }

        if (reportedOnUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("A fact is reported in UTC.", nameof(reportedOnUtc));
        }

        FactId = factId;
        SourceKind = sourceKind;
        State = state;
        HasEvidenceUrl = hasEvidenceUrl;
        ReportedOnUtc = reportedOnUtc;
        ActorKind = actorKind;
        PrincipalKind = principalKind;
        AttesterHoldsRole = attesterHoldsRole;
        AnsweredOnConfirmationCard = answeredOnConfirmationCard;
    }

    // The Evidence Link row (O20).
    public string FactId { get; }

    // itsm-step (a client-built step, a workload principal) or attestation (a person on a confirmation card).
    public AuthorizationFactSourceKind SourceKind { get; }

    // Authorized, not authorized or cancelled, as the ITSM tool or the attester reports it.
    public AuthorizationState State { get; }

    // Whether the fact links to the ITSM record. A fact counts only with one.
    public bool HasEvidenceUrl { get; }

    public DateTime ReportedOnUtc { get; }

    // Who recorded it.
    public ActorKindMeaning ActorKind { get; }

    public PrincipalKind PrincipalKind { get; }

    // For an attestation: the person held an attester role when answering (supplied from the approval runner's record).
    public bool AttesterHoldsRole { get; }

    // For an attestation: it was answered on a confirmation card through the approval runner (document 25, rule 4).
    // A proposal of this build: document 25 names the card but lists no field for it, and without the field the engine
    // could not tell a card answer from a row any client wrote. Absent means no card, so the attestation never counts.
    public bool AnsweredOnConfirmationCard { get; }

    internal static AuthorizationFact FromJson(WireObject wire)
    {
        return new AuthorizationFact(
            wire.RequiredString("factId"),
            wire.Contract("sourceKind", EngineContract.AuthorizationFactSourceKinds),
            wire.Contract("state", EngineContract.AuthorizationStates),
            wire.Bool("hasEvidenceUrl", false),
            wire.RequiredUtc("reportedOnUtc"),
            wire.Contract("actorKind", EngineContract.ActorKindMeanings),
            wire.Contract("principalKind", EngineContract.PrincipalKinds),
            wire.Bool("attesterHoldsRole", false),
            wire.Bool("answeredOnConfirmationCard", false));
    }
}

// OP-14 input (document 25, section 5.15).
public sealed class LinkedAuthorizationRequest
{
    public LinkedAuthorizationRequest(ChangeManagementMode changeManagementMode, bool hasExternalChangeNumber, IEnumerable<AuthorizationFact>? facts = null, IEnumerable<string>? attesterRoleKeys = null)
    {
        AuthorizationFact[] list = (facts ?? Enumerable.Empty<AuthorizationFact>()).ToArray();
        if (list.Select(f => f.FactId).Distinct(StringComparer.Ordinal).Count() != list.Length)
        {
            throw new ArgumentException("Each fact is given once.", nameof(facts));
        }

        string[] roles = (attesterRoleKeys ?? Enumerable.Empty<string>()).Distinct(StringComparer.Ordinal).ToArray();
        if (roles.Any(key => !ConfigurationKey.IsValid(key)))
        {
            throw new ArgumentException("Role keys must use the configuration key format.", nameof(attesterRoleKeys));
        }

        ChangeManagementMode = changeManagementMode;
        HasExternalChangeNumber = hasExternalChangeNumber;
        Facts = new ReadOnlyCollection<AuthorizationFact>(list);
        AttesterRoleKeys = new ReadOnlyCollection<string>(roles);
    }

    // Built in or linked, as copied onto the change request.
    public ChangeManagementMode ChangeManagementMode { get; }

    // Whether the ITSM change number is recorded. Shown, never decisive.
    public bool HasExternalChangeNumber { get; }

    // The authorization facts recorded on the change request (fetched).
    public IReadOnlyList<AuthorizationFact> Facts { get; }

    // The roles allowed to attest: the holder of the accountable letter for change authorization in this scope (C31;
    // the change manager by default, 28 issue 4). A C29 role or a C28 owner role. With none, no attestation counts.
    public IReadOnlyList<string> AttesterRoleKeys { get; }

    public static LinkedAuthorizationRequest FromJson(JsonTreeNode input)
    {
        var wire = new WireObject(input, "input", "changeManagementMode", "hasExternalChangeNumber", "facts", "attesterRoleKeys");
        var facts = new List<AuthorizationFact>();
        foreach (WireObject fact in wire.Objects(
            "facts",
            required: false,
            "factId",
            "sourceKind",
            "state",
            "hasEvidenceUrl",
            "reportedOnUtc",
            "actorKind",
            "principalKind",
            "attesterHoldsRole",
            "answeredOnConfirmationCard"))
        {
            AuthorizationFact parsed = AuthorizationFact.FromJson(fact);
            if (facts.Any(f => string.Equals(f.FactId, parsed.FactId, StringComparison.Ordinal)))
            {
                throw OperationStop.Input(ReasonCodes.InputMalformed, "input.facts gives fact " + SnapshotRead.Quote(parsed.FactId) + " twice.", parsed.FactId);
            }

            facts.Add(parsed);
        }

        return new LinkedAuthorizationRequest(
            wire.Contract("changeManagementMode", EngineContract.ChangeManagementModes),
            wire.Bool("hasExternalChangeNumber", false),
            facts,
            wire.Keys("attesterRoleKeys"));
    }
}

// A fact that did not count, with the reason code of one failed test.
public sealed class IgnoredAuthorizationFact
{
    public IgnoredAuthorizationFact(string factId, string code)
    {
        FactId = factId;
        Code = code;
    }

    public string FactId { get; }

    public string Code { get; }

    internal JsonTreeNode ToJson()
    {
        return new WireWriter().Add("factId", FactId).Add("code", Code).Build();
    }
}

// OP-14 output (document 25, section 5.15). OP-04 reads authorized and basis from it.
public sealed class LinkedAuthorizationResult : IOperationPayload
{
    public LinkedAuthorizationResult(bool authorized, AuthorizationBasis basis, string? decidingFactId, IEnumerable<IgnoredAuthorizationFact> ignoredFacts)
    {
        Authorized = authorized;
        Basis = basis;
        DecidingFactId = decidingFactId;
        IgnoredFacts = new ReadOnlyCollection<IgnoredAuthorizationFact>(ignoredFacts.ToList());
    }

    // True only when the latest counting fact says authorized.
    public bool Authorized { get; }

    // itsm-step or attestation when authorized; none otherwise, which means not authorized (document 24, tool 24).
    public AuthorizationBasis Basis { get; }

    // The latest counting fact, whatever it says. Null when no fact counts.
    public string? DecidingFactId { get; }

    // One entry per failed test of each fact that did not count, sorted by fact ID, then the test order.
    public IReadOnlyList<IgnoredAuthorizationFact> IgnoredFacts { get; }

    public JsonTreeNode ToJson()
    {
        return new WireWriter()
            .Add("authorized", Authorized)
            .Add("basis", EngineContract.AuthorizationBases.ToWire(Basis))
            .Add("decidingFactId", DecidingFactId)
            .Add("ignoredFacts", WireWriter.Array(IgnoredFacts.Select(f => f.ToJson())))
            .Build();
    }
}

// OP-14 Evaluate linked-mode authorization (document 25, section 5.15). Backs the missing-approval flag in linked mode
// (OP-04) and the check step. Working default, 28 Q22: the client's ITSM tool reports the authorization state through
// a client-built step; where no step exists, the change manager attests on a confirmation card; a recorded external
// change number alone never counts (D-08).
//
// WORKING DEFAULT (open design question 12): an itsm-step fact counts only from a workload principal with actor kind
// pipeline. That workload would hold the proposed authorization-reporter role through a workload grant (C42).
// WORKING DEFAULT (open design question 1): the actor kinds, principal kinds and the two source kinds are fixed
// engine meanings.
public static class LinkedAuthorizationOperation
{
    private const string FactEntity = "Evidence Link";

    public static EvaluationResult<LinkedAuthorizationResult?> Evaluate(ConfigurationSnapshot snapshot, EvaluationContext context, LinkedAuthorizationRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return OperationShell.Run(context, snapshot, needsSnapshot: true, () => Answer(snapshot, context.AsOfUtc, request));
    }

    public static EvaluationResult<LinkedAuthorizationResult?> Evaluate(ConfigurationSnapshot snapshot, EvaluationContext context, JsonTreeNode input)
    {
        return OperationShell.Run(context, snapshot, needsSnapshot: true, () => Answer(snapshot, context.AsOfUtc, LinkedAuthorizationRequest.FromJson(input)));
    }

    // The tests of one fact, in a fixed order. Every failed test adds its code, as in OP-03.
    public static IReadOnlyList<string> FailedTests(AuthorizationFact fact, bool anyAttesterRole)
    {
        if (fact == null)
        {
            throw new ArgumentNullException(nameof(fact));
        }

        var codes = new List<string>();

        // Rules 3 and 4: never from an AI client or an agent identity. AI never attests.
        bool ai = ContractFacts.IsAiActor(fact.ActorKind) || fact.PrincipalKind == PrincipalKind.AgentIdentity || fact.PrincipalKind == PrincipalKind.AgentUser;
        if (ai)
        {
            codes.Add(ReasonCodes.AuthorizationAiNotCounted);
        }

        // Rule 2: a fact counts only with an evidence URL.
        if (!fact.HasEvidenceUrl)
        {
            codes.Add(ReasonCodes.AuthorizationNoEvidence);
        }

        if (fact.SourceKind == AuthorizationFactSourceKind.ItsmStep)
        {
            // Rule 3: an ITSM step fact counts only from a workload principal with actor kind pipeline.
            bool workload = fact.PrincipalKind == PrincipalKind.ServicePrincipal || fact.PrincipalKind == PrincipalKind.ManagedIdentity;
            if (!ai && (fact.ActorKind != ActorKindMeaning.Pipeline || !workload))
            {
                codes.Add(ReasonCodes.AuthorizationWrongSource);
            }
        }
        else
        {
            // Rule 4: an attestation counts only from a person signed in as a user, answered on a confirmation card
            // through the approval runner, holding an attester role.
            if (!ai && (!ContractFacts.IsPerson(fact.ActorKind) || !ContractFacts.CanApprove(fact.PrincipalKind) || !fact.AnsweredOnConfirmationCard))
            {
                codes.Add(ReasonCodes.AuthorizationWrongSource);
            }

            if (!fact.AttesterHoldsRole || !anyAttesterRole)
            {
                codes.Add(ReasonCodes.AuthorizationNotAttester);
            }
        }

        return codes;
    }

    private static OperationAnswer<LinkedAuthorizationResult> Answer(ConfigurationSnapshot snapshot, DateTime asOfUtc, LinkedAuthorizationRequest request)
    {
        var trace = new List<RuleTrace>();
        foreach (string key in request.AttesterRoleKeys)
        {
            SnapshotRow? role = SnapshotRead.FindActive(snapshot, "role", key);
            SnapshotRow? ownerRole = role == null ? SnapshotRead.FindActive(snapshot, "owner-role", key) : null;
            if (role == null && ownerRole == null)
            {
                throw OperationStop.Input(
                    ReasonCodes.InputUnknownKey,
                    "input.attesterRoleKeys names " + SnapshotRead.Quote(key) + ", which is not an active Role or Owner Role in the configuration in force.",
                    key);
            }

            trace.Add(new RuleTrace(role != null ? "Role" : "Owner Role", key, RuleTraceRole.Matched));
        }

        // Rule 1: built-in mode is not applicable here; OP-04 uses OP-03.
        if (request.ChangeManagementMode == ChangeManagementMode.BuiltIn)
        {
            return new OperationAnswer<LinkedAuthorizationResult>(
                "not-applicable",
                new LinkedAuthorizationResult(false, AuthorizationBasis.None, null, Array.Empty<IgnoredAuthorizationFact>()),
                Array.Empty<Reason>(),
                trace);
        }

        var reasons = new List<Reason>();
        var ignored = new List<IgnoredAuthorizationFact>();
        var counting = new List<AuthorizationFact>();
        foreach (AuthorizationFact fact in request.Facts.OrderBy(f => f.FactId, StringComparer.Ordinal))
        {
            var codes = new List<string>(FailedTests(fact, request.AttesterRoleKeys.Count > 0));

            // Rule 5 reads the latest fact by Reported On. A fact dated after the moment of this evaluation cannot have
            // been reported yet, so it never authorizes: a clock that runs ahead, or a forged date,
            // would otherwise outrank every cancellation. A removal dated later still removes, because the engine never
            // guesses in favor (as at the same moment, below).
            if (fact.ReportedOnUtc > asOfUtc && fact.State == AuthorizationState.Authorized)
            {
                codes.Add(ReasonCodes.AuthorizationReportedLater);
            }

            if (codes.Count == 0)
            {
                counting.Add(fact);
                continue;
            }

            foreach (string code in codes)
            {
                ignored.Add(new IgnoredAuthorizationFact(fact.FactId, code));
                reasons.Add(new Reason(code, ReasonCatalog.Get(code).DefaultSeverity, Explain(code, fact, request.AttesterRoleKeys.Count > 0, asOfUtc), FactEntity, fact.FactId));
            }
        }

        // Rule 5: the latest counting fact by Reported On decides. At the same moment, a fact that removes the
        // authorization wins over one that grants it, then the larger fact ID: the engine never guesses in favor.
        AuthorizationFact? deciding = counting
            .OrderBy(f => f.ReportedOnUtc)
            .ThenBy(f => f.State == AuthorizationState.Authorized ? 0 : 1)
            .ThenBy(f => f.FactId, StringComparer.Ordinal)
            .LastOrDefault();
        bool authorized = deciding != null && deciding.State == AuthorizationState.Authorized;
        AuthorizationBasis basis = !authorized
            ? AuthorizationBasis.None
            : deciding!.SourceKind == AuthorizationFactSourceKind.ItsmStep ? AuthorizationBasis.ItsmStep : AuthorizationBasis.Attestation;

        if (deciding != null && !authorized && counting.Any(f => f.State == AuthorizationState.Authorized && !ReferenceEquals(f, deciding)))
        {
            reasons.Add(new Reason(
                ReasonCodes.AuthorizationRevoked,
                ReasonCatalog.Get(ReasonCodes.AuthorizationRevoked).DefaultSeverity,
                "Fact " + SnapshotRead.Quote(deciding.FactId) + " says the change is " + EngineContract.AuthorizationStates.ToWire(deciding.State) + ", after an earlier fact said authorized, so the change is not authorized.",
                FactEntity,
                deciding.FactId));
        }

        // Rule 6: with no counting fact the change is not authorized, whatever the external change number says.
        if (deciding == null && request.HasExternalChangeNumber)
        {
            reasons.Add(ReasonCatalog.Create(ReasonCodes.AuthorizationNumberOnly, "Change Request", null));
        }

        var payload = new LinkedAuthorizationResult(authorized, basis, deciding?.FactId, ignored);
        return new OperationAnswer<LinkedAuthorizationResult>(authorized ? "authorized" : "not-authorized", payload, reasons, trace);
    }

    private static string Explain(string code, AuthorizationFact fact, bool anyAttesterRole, DateTime asOfUtc)
    {
        string who = "Fact " + SnapshotRead.Quote(fact.FactId);
        switch (code)
        {
            case ReasonCodes.AuthorizationReportedLater:
                return who + " is dated " + fact.ReportedOnUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture) + ", after the moment of this evaluation ("
                    + asOfUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture) + "), so it does not authorize the change.";
            case ReasonCodes.AuthorizationAiNotCounted:
                return who + " was recorded by " + EngineContract.ActorKindMeanings.ToWire(fact.ActorKind) + " (" + EngineContract.PrincipalKinds.ToWire(fact.PrincipalKind) + "), and an AI client or agent identity never authorizes a change.";
            case ReasonCodes.AuthorizationNoEvidence:
                return who + " has no evidence link to the ITSM record, so it does not count.";
            case ReasonCodes.AuthorizationWrongSource:
                return fact.SourceKind == AuthorizationFactSourceKind.ItsmStep
                    ? who + " is an ITSM step fact from " + EngineContract.ActorKindMeanings.ToWire(fact.ActorKind) + " (" + EngineContract.PrincipalKinds.ToWire(fact.PrincipalKind) + "); only a pipeline workload principal reports one."
                    : who + " is an attestation that did not come from a person signed in as a user on a confirmation card, so it does not count.";
            default:
                return anyAttesterRole
                    ? who + " is an attestation by a person who did not hold an attester role, so it does not count."
                    : who + " is an attestation, and no attester role is named for this change, so it does not count.";
        }
    }
}
