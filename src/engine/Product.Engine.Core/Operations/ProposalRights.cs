using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;
using Product.Engine.Core.Snapshots;

namespace Product.Engine.Core.Operations;

// One Decision Right row (C31) that lets its holder propose a decision: the row's key and letter, and the holder, a
// product role (C29) or an owner role (C28) on the subject's application.
public sealed class ProposalRightHolder : IOperationPayload
{
    public ProposalRightHolder(string decisionRightKey, DecisionRightLetter letter, string? roleKey, string? ownerRoleKey)
    {
        if (string.IsNullOrEmpty(decisionRightKey))
        {
            throw new ArgumentException("A holder names its decision right row.", nameof(decisionRightKey));
        }

        if ((roleKey == null) == (ownerRoleKey == null))
        {
            throw new ArgumentException("A holder is exactly one of a role and an owner role.", nameof(roleKey));
        }

        DecisionRightKey = decisionRightKey;
        Letter = letter;
        RoleKey = roleKey;
        OwnerRoleKey = ownerRoleKey;
    }

    public string DecisionRightKey { get; }

    public DecisionRightLetter Letter { get; }

    // The product role that holds the letter, or null when an owner role does.
    public string? RoleKey { get; }

    // The owner role on the subject's application that holds the letter, or null when a product role does.
    public string? OwnerRoleKey { get; }

    // Such as: role "pmo" (responsible), or owner role "business-owner" (accountable).
    public string Describe()
    {
        return (RoleKey != null ? "role " + SnapshotRead.Quote(RoleKey) : "owner role " + SnapshotRead.Quote(OwnerRoleKey!)) + " (" + EngineContract.DecisionRightLetters.ToWire(Letter) + ")";
    }

    public JsonTreeNode ToJson()
    {
        return new WireWriter()
            .Add("decisionRightKey", DecisionRightKey)
            .Add("letter", EngineContract.DecisionRightLetters.ToWire(Letter))
            .Add("roleKey", RoleKey)
            .Add("ownerRoleKey", OwnerRoleKey)
            .Build();
    }
}

// The input of the proposal right check: the decision's scope (the same fields OP-02 reads), the product roles the
// person behind the call holds now, and the owner roles the person holds now on the subject's application.
public sealed class ProposalRightRequest
{
    public ProposalRightRequest(ApprovalScopeRequest scope, IEnumerable<string>? roleKeys = null, IEnumerable<string>? ownerRoleKeys = null)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        RoleKeys = Keys(roleKeys, nameof(roleKeys));
        OwnerRoleKeys = Keys(ownerRoleKeys, nameof(ownerRoleKeys));
    }

    public ApprovalScopeRequest Scope { get; }

    public IReadOnlyList<string> RoleKeys { get; }

    public IReadOnlyList<string> OwnerRoleKeys { get; }

    public static ProposalRightRequest FromJson(JsonTreeNode input)
    {
        var wire = new WireObject(
            input,
            "input",
            "decisionTypeKey",
            "tierKey",
            "lifecycleStageKey",
            "changeTypeKey",
            "applicationKindKey",
            "targetStageKey",
            "portfolioPath",
            "roleKeys",
            "ownerRoleKeys",
            "subjectKind",
            "classificationKey",
            "aiUse",
            "dataDomainLevelKey");
        var scope = new ApprovalScopeRequest(
            wire.RequiredKey("decisionTypeKey"),
            wire.OptionalKey("tierKey"),
            wire.OptionalKey("lifecycleStageKey"),
            wire.OptionalKey("changeTypeKey"),
            wire.OptionalKey("applicationKindKey"),
            wire.OptionalKey("targetStageKey"),
            wire.Strings("portfolioPath"),
            subjectKind: wire.OptionalKey("subjectKind"),
            classificationKey: wire.OptionalKey("classificationKey"),
            aiUse: wire.OptionalBool("aiUse"),
            dataDomainLevelKey: wire.OptionalKey("dataDomainLevelKey"));
        return new ProposalRightRequest(scope, wire.Keys("roleKeys"), wire.Keys("ownerRoleKeys"));
    }

    private static IReadOnlyList<string> Keys(IEnumerable<string>? keys, string name)
    {
        string[] list = (keys ?? Enumerable.Empty<string>()).Where(k => !string.IsNullOrEmpty(k)).Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        if (list.Any(k => !ConfigurationKey.IsValid(k)))
        {
            throw new ArgumentException("A role key must use the configuration key format.", name);
        }

        return new ReadOnlyCollection<string>(list);
    }
}

// The answer: whether the person may propose, the row that lets them, and every row that lets someone propose in this
// scope (so a refusal can say who may).
public sealed class ProposalRightResult : IOperationPayload
{
    public ProposalRightResult(bool allowed, ProposalRightHolder? grantedBy, IEnumerable<ProposalRightHolder> holders)
    {
        Allowed = allowed;
        GrantedBy = grantedBy;
        Holders = new ReadOnlyCollection<ProposalRightHolder>((holders ?? throw new ArgumentNullException(nameof(holders))).ToList());
    }

    public bool Allowed { get; }

    public ProposalRightHolder? GrantedBy { get; }

    // Responsible first, then accountable, then consulted; by row key within a letter.
    public IReadOnlyList<ProposalRightHolder> Holders { get; }

    public JsonTreeNode ToJson()
    {
        return new WireWriter()
            .Add("allowed", Allowed)
            .Add("grantedBy", GrantedBy?.ToJson())
            .Add("holders", WireWriter.Array(Holders.Select(h => h.ToJson())))
            .Build();
    }
}

// Who may propose a decision (document 24, tool 17, default roles "Holders of R or C for the decision type (C31)";
// Tool Policy row save-decision-proposal: "The engine also checks the caller is a holder of the R or C letter"). The
// check resolves the Decision Right groups of the decision type with the rule OP-02 uses for the accountable letter
// (document 25, OP-02 rule 7): the single most specific group whose scope matches wins, and broader groups drop out.
// The person may propose when they hold a letter that names a person who takes part before the decision: a product role
// they hold now, or an owner role they hold now on the subject's application (a deputy holds it too, as for "own apps",
// document 26, section 5.3).
//
// Judgment call of this build (document 24, B24-5): the letters that may propose are responsible, accountable and
// consulted. The design names R and C. The accountable letter is added because the shipped presets give most decision
// types only an accountable row: with R or C alone, nobody could propose a risk acceptance, a go-live, a data use, an
// exception or a tier in Balanced. The accountable holder answers for the outcome, so it may put a decision on the
// table; whether its own answer then counts is the separation rules' question (C40; D-40 keeps Balanced as it is).
// Informed (told after) never proposes, and supports names no holder (AI only, V31).
public static class ProposalRightsOperation
{
    public const string Allowed = "allowed";

    private const string DecisionRightEntity = "decision-right";

    // The letters whose holders may propose, in the order a granting row is chosen.
    public static readonly IReadOnlyList<DecisionRightLetter> ProposingLetters = new ReadOnlyCollection<DecisionRightLetter>(new[]
    {
        DecisionRightLetter.Responsible,
        DecisionRightLetter.Accountable,
        DecisionRightLetter.Consulted,
    });

    // Outcomes: allowed; refused (decision.propose-not-allowed, with the holders in the payload); invalid-input for an
    // unknown or retired decision type; unavailable when no Decision Right group matches the scope or two tie.
    public static EvaluationResult<ProposalRightResult?> Evaluate(ConfigurationSnapshot snapshot, EvaluationContext context, ProposalRightRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return OperationShell.Run(context, snapshot, needsSnapshot: true, () => Answer(snapshot, request));
    }

    public static EvaluationResult<ProposalRightResult?> Evaluate(ConfigurationSnapshot snapshot, EvaluationContext context, JsonTreeNode input)
    {
        return OperationShell.Run(context, snapshot, needsSnapshot: true, () => Answer(snapshot, ProposalRightRequest.FromJson(input)));
    }

    private static OperationAnswer<ProposalRightResult> Answer(ConfigurationSnapshot snapshot, ProposalRightRequest request)
    {
        ApprovalScopeRequest scope = request.Scope;
        string typeKey = scope.DecisionTypeKey;
        SnapshotRead.RequireActive(snapshot, "decision-type", typeKey, "input.decisionTypeKey");
        Dictionary<string, string?> columns = RequiredApprovalsOperation.ScopeColumns(scope);

        List<ScopeGroup> groups = ScopeGroups.Build(snapshot, DecisionRightEntity, typeKey, ScopeGroups.DecisionRightScope);
        ScopeResolution rights = ScopeGroups.Resolve(groups, columns, scope.PortfolioPath, DecisionRightEntity, typeKey);
        if (rights.Winner == null)
        {
            throw OperationStop.Broken(
                ReasonCodes.DecisionRightAccountable,
                "No Decision Right group of decision type " + SnapshotRead.Quote(typeKey) + " matches this scope, so nobody may propose or decide it. The configuration is broken.",
                "Decision Right",
                typeKey);
        }

        List<RuleTrace> trace = ScopeGroups.Trace(rights, DecisionRightEntity).ToList();
        List<ProposalRightHolder> holders = rights.Winner.Rows
            .Select(Holder)
            .Where(h => h != null)
            .Select(h => h!)
            .OrderBy(h => Rank(h.Letter))
            .ThenBy(h => h.DecisionRightKey, StringComparer.Ordinal)
            .ToList();
        ProposalRightHolder? granted = holders.FirstOrDefault(h =>
            (h.RoleKey != null && request.RoleKeys.Contains(h.RoleKey, StringComparer.Ordinal))
            || (h.OwnerRoleKey != null && request.OwnerRoleKeys.Contains(h.OwnerRoleKey, StringComparer.Ordinal)));
        var result = new ProposalRightResult(granted != null, granted, holders);
        if (granted != null)
        {
            return new OperationAnswer<ProposalRightResult>(Allowed, result, null, trace);
        }

        string who = holders.Count == 0 ? "No row of the winning group (" + rights.Winner.Describe() + ") names one" : "They are held by " + string.Join(", ", holders.Select(h => h.Describe()));
        var refusal = new Reason(
            ReasonCodes.DecisionProposeNotAllowed,
            ReasonSeverity.Error,
            "Only a holder of a responsible, accountable or consulted Decision Right for decision type " + SnapshotRead.Quote(typeKey)
            + " in this scope can propose it, and the person behind this call holds none. " + who + ".",
            "Decision Right",
            typeKey);
        return new OperationAnswer<ProposalRightResult>(OperationOutcomes.Refused, result, new[] { refusal }, trace);
    }

    // A row whose letter proposes and that names exactly one holder; null for any other row.
    private static ProposalRightHolder? Holder(SnapshotRow row)
    {
        if (!EngineContract.DecisionRightLetters.TryParse(SnapshotRead.Text(row, "letter"), out DecisionRightLetter letter) || !ProposingLetters.Contains(letter))
        {
            return null;
        }

        string? roleKey = SnapshotRead.Text(row, "roleKey");
        string? ownerRoleKey = SnapshotRead.Text(row, "ownerRoleKey");
        if ((roleKey == null) == (ownerRoleKey == null))
        {
            throw OperationStop.Broken(
                ReasonCodes.RefExactlyOne,
                "Decision Right " + SnapshotRead.Quote(row.KeyPath) + " must name exactly one of roleKey and ownerRoleKey, so the configuration is broken.",
                "Decision Right",
                row.KeyPath);
        }

        return new ProposalRightHolder(row.KeyPath, letter, roleKey, ownerRoleKey);
    }

    private static int Rank(DecisionRightLetter letter)
    {
        for (int i = 0; i < ProposingLetters.Count; i++)
        {
            if (ProposingLetters[i] == letter)
            {
                return i;
            }
        }

        return ProposingLetters.Count;
    }
}
