using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Runtime.Ports;

namespace Product.Engine.Runtime.Facts;

// The scope of one decision, as OP-02, OP-03 and OP-07 need it (document 25, sections 5.4, 5.5 and 5.9), built from the
// decision row, its subject, the subject's application, the application's portfolio path and owner assignments.
//
// Judgment calls of this build:
// - An intake decision takes its tier from the intake's latest Tier Assessment (O08 Computed Tier), its application kind
//   from the intake's Proposed Kind, and its portfolio path from the application the intake names, when it names one.
// - A change decision takes the change type, the planned target's stage and the application's tier, stage, kind and
//   portfolio path. A change type that requires a standard change model takes the OP-12 result too, which the runtime
//   adds to the OP-02 input (document 25, B25-14).
// - The proposer is the person a decision was created for (On Behalf Of), else its creator (document 25, section 5.5).
// - Principal references are Entra object IDs in the runtime's rows; the physical stores map them to O05 rows.
// - A tier decision about an application takes the tier at stake (document 25, B25-14): the highest of the tier it has,
//   the tier the decision names and the tier the latest answers computed. The runtime sets it with UseTier. Any other
//   decision about an intake request linked to an application takes the higher of the application's tier and the
//   intake's computed tier (security review 2026-11-24, B25-15): its effect lands on the application, so its approvals
//   are never those of a lower tier than the application has.
// - Who a record asks (document 25, B25-14): Ask resolves a role, an owner role with its fallback, or the proposed owners.
public sealed class DecisionScope
{
    // The day the scope was built for (yyyy-MM-dd), the moment the owner facts are read at.
    public string AsOfDay { get; private set; } = string.Empty;

    private DecisionScope(StoredRow decision)
    {
        Decision = decision;
        DecisionTypeKey = decision.Text("decisionType") ?? string.Empty;
        SubjectKind = decision.Text("subjectKind");
    }

    public StoredRow Decision { get; }

    public string DecisionId => Decision.Id;

    public string DecisionTypeKey { get; }

    public string? SubjectKind { get; }

    public StoredRow? Application { get; private set; }

    public StoredRow? Intake { get; private set; }

    public StoredRow? Change { get; private set; }

    public StoredRow? Draft { get; private set; }

    public StoredRow? Target { get; private set; }

    public StoredRow? LatestAssessment { get; private set; }

    public string? TierKey { get; private set; }

    public string? LifecycleStageKey { get; private set; }

    public string? ChangeTypeKey { get; private set; }

    public string? ApplicationKindKey { get; private set; }

    public string? TargetStageKey { get; private set; }

    // True for a decision about an intake request linked to an application already in the register (not a draft). OP-02
    // then never accepts it at the lowest tier with no card (security review 2026-11-24, document 25, B25-15).
    public bool SubjectApplicationRegistered => SubjectKind == "intake-request" && Application != null
        && Application.Text("statusMeaning") is string meaning && meaning != "draft";

    public IReadOnlyList<string> PortfolioPath { get; private set; } = Array.Empty<string>();

    // Current owner assignments of the application (End On empty or later than today).
    public IReadOnlyList<StoredRow> Owners { get; private set; } = Array.Empty<StoredRow>();

    public string? ProposerPrincipalId { get; private set; }

    public string? InstructingAdminPrincipalId { get; private set; }

    public IReadOnlyList<string> DeployerPrincipalIds { get; private set; } = Array.Empty<string>();

    // The Accountable Principal of each portfolio on the path, nearest first, with null where a portfolio names none.
    public IReadOnlyList<string?> PortfolioAccountablePrincipals { get; private set; } = Array.Empty<string?>();

    // The owner changes the decision proposes (O09 Proposed Changes Json, owners), in order.
    public IReadOnlyList<ProposedOwnerChange> ProposedOwnerChanges { get; private set; } = Array.Empty<ProposedOwnerChange>();

    // ---- The data taxonomy (docs/architecture/data-taxonomy.md) ----

    // The decision's data subject as the store holds it: a data use, a data mapping or a data location.
    public StoredRow? DataUse { get; private set; }

    public StoredRow? DataMapping { get; private set; }

    public StoredRow? DataLocation { get; private set; }

    // The data domain the decision is about: the subject domain of an ownership decision, or the domain of its use or
    // mapping.
    public string? DataDomainKey { get; private set; }

    // The location the data subject names: the subject location, or the location of the use or mapping.
    public string? DataLocationId { get; private set; }

    // OP-02's data scope fields, set by the runtime from the snapshot (EngineRuntime.Data).
    public string? ClassificationKey { get; private set; }

    public bool? AiUse { get; private set; }

    public string? DataDomainLevelKey { get; private set; }

    public string? DataTaxonomyChange { get; private set; }

    // The domains a configuration draft touches (DataTaxonomyDiff), for the touched-data-domains owner subject.
    public IReadOnlyList<string> TouchedDomainKeys { get; private set; } = Array.Empty<string>();

    // The data tree and the owner facts the data owner subjects resolve through (OP-15). Null outside the data taxonomy.
    public Core.Operations.DataTree? DataTree { get; private set; }

    public IReadOnlyList<Core.Operations.DataOwnerFact> DataOwners { get; private set; } = Array.Empty<Core.Operations.DataOwnerFact>();

    public Func<string, Core.Operations.DataPrincipalFact?> DataPrincipals { get; private set; } = _ => null;

    // The current owner assignments of the decision's data subject itself (a domain or a location), for the update-subject
    // effect of an ownership decision about it.
    public IReadOnlyList<StoredRow> DataSubjectOwners { get; private set; } = Array.Empty<StoredRow>();

    public bool IsDataSubject => SubjectKind == Core.Contract.EngineKeys.DataUseSubjectKind || SubjectKind == Core.Contract.EngineKeys.DataMappingSubjectKind
        || SubjectKind == Core.Contract.EngineKeys.DataDomainSubjectKind || SubjectKind == Core.Contract.EngineKeys.DataLocationSubjectKind;

    internal void UseDataScope(string? classificationKey, bool? aiUse, string? levelKey, string? change, IReadOnlyList<string> touched, Core.Operations.DataTree tree,
        IReadOnlyList<Core.Operations.DataOwnerFact> owners, Func<string, Core.Operations.DataPrincipalFact?> principals, IReadOnlyList<StoredRow> subjectOwners)
    {
        ClassificationKey = classificationKey;
        AiUse = aiUse;
        DataDomainLevelKey = levelKey;
        DataTaxonomyChange = change;
        TouchedDomainKeys = touched;
        DataTree = tree;
        DataOwners = owners;
        DataPrincipals = principals;
        DataSubjectOwners = subjectOwners;
    }

    // Plan: the decision, then its subject, then the subject's application and target, then owners and the portfolio,
    // then each portfolio ancestor. Usually three or four rounds.
    public static IEnumerable<FactPlan> Plan(string decisionId, FactSet facts)
    {
        yield return FactPlan.Of(FactQuery.ById("decision", "decision", decisionId));
        StoredRow? decision = facts.One("decision");
        if (decision == null)
        {
            yield break;
        }

        string? appId = decision.Text("subjectApplication");
        string? intakeId = decision.Text("subjectIntakeRequest");
        string? changeId = decision.Text("subjectChangeRequest");
        string? draftId = decision.Text("subjectConfigurationDraft");
        string? useId = decision.Text("subjectDataUse");
        string? mappingId = decision.Text("subjectDataMapping");
        string? locationId = decision.Text("subjectDataLocation");
        yield return FactPlan.Of(
            appId == null ? null : FactQuery.ById("application", "business-application", appId),
            intakeId == null ? null : FactQuery.ById("intake", "intake-request", intakeId),
            intakeId == null ? null : FactQuery.Where("assessments", RowQuery.For("tier-assessment").Where("intakeRequest", intakeId)),
            changeId == null ? null : FactQuery.ById("change", "change-request", changeId),
            changeId == null ? null : FactQuery.Where("deployments", RowQuery.For("deployment-record").Where("changeRequest", changeId)),
            draftId == null ? null : FactQuery.ById("draft", "configuration-draft", draftId),
            useId == null ? null : FactQuery.ById("data-use", "data-use", useId),
            mappingId == null ? null : FactQuery.ById("data-mapping", "data-mapping", mappingId),
            locationId == null ? null : FactQuery.ById("data-location", "data-location", locationId));

        appId ??= facts.One("intake")?.Text("businessApplication") ?? facts.One("change")?.Text("businessApplication") ?? facts.One("data-use")?.Text("businessApplication");
        string? targetId = facts.One("change")?.Text("plannedTarget");
        if (!facts.Has("application") || targetId != null)
        {
            yield return FactPlan.Of(
                appId == null || facts.Has("application") ? null : FactQuery.ById("application", "business-application", appId),
                targetId == null ? null : FactQuery.ById("target", "deployment-target", targetId));
        }

        foreach (FactPlan plan in ApplicationPlan(facts))
        {
            yield return plan;
        }
    }

    // The owners and portfolio path of the application already fetched as "application".
    public static IEnumerable<FactPlan> ApplicationPlan(FactSet facts)
    {
        StoredRow? app = facts.One("application");
        if (app == null)
        {
            yield break;
        }

        string? portfolio = app.Text("portfolio");
        yield return FactPlan.Of(
            FactQuery.Where("owners", RowQuery.For("owner-assignment").Where("businessApplication", app.Id)),
            portfolio == null ? null : FactQuery.ById("portfolio-0", "portfolio", portfolio));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int level = 0; level < 32; level++)
        {
            StoredRow? current = facts.One("portfolio-" + level);
            if (current == null || !seen.Add(current.Id))
            {
                yield break;
            }

            string? parent = current.Text("parentPortfolio");
            if (parent == null || seen.Contains(parent))
            {
                yield break;
            }

            yield return FactPlan.Of(FactQuery.ById("portfolio-" + (level + 1), "portfolio", parent));
        }
    }

    public static DecisionScope? Build(FactSet facts, DateTime asOfUtc)
    {
        StoredRow? decision = facts.One("decision");
        if (decision == null)
        {
            return null;
        }

        var scope = new DecisionScope(decision)
        {
            Application = facts.One("application"),
            Intake = facts.One("intake"),
            Change = facts.One("change"),
            Draft = facts.One("draft"),
            Target = facts.One("target"),
            DataUse = facts.One("data-use"),
            DataMapping = facts.One("data-mapping"),
            DataLocation = facts.One("data-location"),
        };
        scope.AsOfDay = asOfUtc.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        scope.DataDomainKey = decision.Text("subjectDataDomain") ?? scope.DataUse?.Text("dataDomain") ?? scope.DataMapping?.Text("dataDomain");
        scope.DataLocationId = scope.DataLocation?.Id ?? scope.DataUse?.Text("dataLocation") ?? scope.DataMapping?.Text("dataLocation");

        scope.LatestAssessment = facts.All("assessments")
            .Where(a => a.Text("computedTier") != null)
            .OrderBy(a => a.Text("engineCheckedOn") ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(a => a.Id, StringComparer.Ordinal)
            .LastOrDefault();

        StoredRow? app = scope.Application;
        scope.PortfolioPath = PortfolioPathOf(facts);
        scope.Owners = CurrentOwners(facts.All("owners"), asOfUtc);
        switch (scope.SubjectKind)
        {
            case "intake-request":
                scope.ApplicationKindKey = scope.Intake?.Text("proposedKind") ?? app?.Text("applicationKind");
                scope.TierKey = scope.LatestAssessment?.Text("computedTier") ?? app?.Text("tier");
                scope.LifecycleStageKey = app?.Text("lifecycleStage");
                break;
            case "change-request":
                scope.ChangeTypeKey = scope.Change?.Text("changeType");
                scope.TargetStageKey = scope.Target?.Text("targetStage");
                scope.ApplicationKindKey = app?.Text("applicationKind");
                scope.TierKey = app?.Text("tier");
                scope.LifecycleStageKey = app?.Text("lifecycleStage");
                break;
            case "configuration-draft":
                break;
            default:
                scope.ApplicationKindKey = app?.Text("applicationKind");
                scope.TierKey = app?.Text("tier");
                scope.LifecycleStageKey = app?.Text("lifecycleStage");
                break;
        }

        scope.PortfolioAccountablePrincipals = PortfolioPathRows(facts).Select(p => p.Text("accountablePrincipal")).ToArray();
        scope.ProposedOwnerChanges = ProposedOwnerChange.Read(decision);
        // The proposer is set once at create: the person an AI client drafted for, else the creator. onBehalfOf is
        // restamped on every write, so a nested acceptance would make the answerer the proposer (product review, major).
        scope.ProposerPrincipalId = decision.Text("draftedForPrincipal") ?? decision.Text("createdByPrincipal");
        scope.InstructingAdminPrincipalId = scope.Draft?.Text("instructedBy");
        scope.DeployerPrincipalIds = facts.All("deployments").Select(d => d.Text("deployedBy")).Where(p => p != null).Select(p => p!).Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        return scope;
    }

    // The tier at stake for a tier decision (document 25, B25-14), which the runtime works out with the tier ranks.
    internal void UseTier(string? tierKey)
    {
        TierKey = tierKey;
    }

    // Each person the decision proposes to make an owner (the successor, a new deputy), in the order named, once each, and
    // for an intake decision the business owner the request names when the accepted intake would assign them (B25-18).
    public IReadOnlyList<string> ProposedOwnerPrincipals()
    {
        var people = new List<string>();
        foreach (ProposedOwnerChange change in ProposedOwnerChanges)
        {
            if (change.AddObjectId != null && change.OwnerRoleKey != null && !people.Any(p => string.Equals(p, change.AddObjectId, StringComparison.OrdinalIgnoreCase)))
            {
                people.Add(change.AddObjectId);
            }
        }

        string? named = IntakeOwnerToConsent();
        if (named != null && !people.Any(p => string.Equals(p, named, StringComparison.OrdinalIgnoreCase)))
        {
            people.Add(named);
        }

        return people;
    }

    // The owner role a proposed owner takes: the first change that adds them, or the business owner role for the person an
    // intake names.
    public string? ProposedOwnerRole(string principalId)
    {
        string? role = ProposedOwnerChanges.FirstOrDefault(c => c.AddObjectId != null && c.OwnerRoleKey != null && string.Equals(c.AddObjectId, principalId, StringComparison.OrdinalIgnoreCase))?.OwnerRoleKey;
        return role ?? (string.Equals(IntakeOwnerToConsent(), principalId, StringComparison.OrdinalIgnoreCase) ? Core.Contract.EngineKeys.BusinessOwnerRole : null);
    }

    // The business owner an open intake request names, when accepting the intake would make them the application's
    // business owner (document 25, B25-10 and B25-18): the decision is an intake decision about the request, and the
    // request starts a new application, or registers a draft that has no current main business owner. That person
    // consents first, on an approval request asked by name. Null otherwise: a registered application changes owners only
    // through an ownership decision, and a draft that already has a business owner keeps it.
    public string? IntakeOwnerToConsent()
    {
        if (!string.Equals(DecisionTypeKey, Core.Contract.EngineKeys.IntakeDecisionType, StringComparison.Ordinal) || SubjectKind != "intake-request" || Intake == null)
        {
            return null;
        }

        string? named = Intake.Text("businessOwner");
        string? meaning = Intake.Text("statusMeaning");
        bool open = meaning == null || meaning == "draft" || meaning == "submitted" || meaning == "in-progress";
        if (named == null || !open)
        {
            return null;
        }

        if (Application != null)
        {
            string? appMeaning = Application.Text("statusMeaning");
            if (appMeaning != null && appMeaning != "draft")
            {
                return null;
            }

            if (Owners.Any(o => string.Equals(o.Text("ownerRole"), Core.Contract.EngineKeys.BusinessOwnerRole, StringComparison.Ordinal) && !o.Flag("isDeputy")))
            {
                return null;
            }
        }

        return named;
    }

    // Who a record for a requirement asks (document 25, B25-14; document 21, B21-9). A role is asked as it is. An owner
    // role is asked while the subject has a holder of it, unless a fallback owner role is configured and the decision
    // hands that role over (it ends a main holder's assignment, or starts a new main holder): then the holders of the
    // fallback owner role are asked, and after them the nearest portfolio's Accountable Principal, by name. When nobody is
    // found that way, the owner role's own holders are asked after all, and with no holder at all the record names the
    // owner role and waits. For a request with no application yet, the person the intake names as business owner holds
    // the business owner role (as for the Accept gate, document 25, B25-10), and is asked by name.
    public AskTarget Ask(string? roleKey, string? ownerRoleKey, string? fallbackOwnerRoleKey)
    {
        if (roleKey != null || ownerRoleKey == null)
        {
            return new AskTarget(roleKey, ownerRoleKey, null);
        }

        bool handedOver = fallbackOwnerRoleKey != null && HandsOver(ownerRoleKey);
        if (!handedOver && Holders(ownerRoleKey, out string? named))
        {
            return new AskTarget(null, ownerRoleKey, named);
        }

        if (fallbackOwnerRoleKey != null)
        {
            if (!HandsOver(fallbackOwnerRoleKey) && Holders(fallbackOwnerRoleKey, out string? fallbackNamed))
            {
                return new AskTarget(null, fallbackOwnerRoleKey, fallbackNamed);
            }

            string? accountable = PortfolioAccountablePrincipals.FirstOrDefault(p => !string.IsNullOrEmpty(p));
            if (accountable != null)
            {
                return new AskTarget(null, ownerRoleKey, accountable);
            }
        }

        return Holders(ownerRoleKey, out string? last) ? new AskTarget(null, ownerRoleKey, last) : new AskTarget(null, ownerRoleKey, null);
    }

    // True when the subject has a current holder of the owner role (a deputy too). named is the person to ask by name when
    // the holder is the business owner an open intake names for an application that has no business owner yet.
    private bool Holders(string ownerRoleKey, out string? named)
    {
        named = null;
        if (Owners.Any(o => string.Equals(o.Text("ownerRole"), ownerRoleKey, StringComparison.Ordinal) && o.Text("principal") != null))
        {
            return true;
        }

        string? intakeOwner = Intake?.Text("businessOwner");
        string? meaning = Intake?.Text("statusMeaning");
        bool open = meaning == null || meaning == "draft" || meaning == "submitted" || meaning == "in-progress";
        if (intakeOwner != null && open && string.Equals(ownerRoleKey, Core.Contract.EngineKeys.BusinessOwnerRole, StringComparison.Ordinal))
        {
            named = intakeOwner;
            return true;
        }

        return false;
    }

    // True when the decision ends a main holder's assignment of the owner role, or starts a new main holder of it.
    private bool HandsOver(string ownerRoleKey)
    {
        foreach (ProposedOwnerChange change in ProposedOwnerChanges)
        {
            if (change.EndAssignmentId != null)
            {
                StoredRow? ended = Owners.FirstOrDefault(o => string.Equals(o.Id, change.EndAssignmentId, StringComparison.OrdinalIgnoreCase));
                if (ended != null && !ended.Flag("isDeputy") && string.Equals(ended.Text("ownerRole"), ownerRoleKey, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            if (change.AddObjectId != null && !change.IsDeputy && string.Equals(change.OwnerRoleKey, ownerRoleKey, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // OP-02 input (document 25, section 5.4).
    public JsonTreeNode RequiredApprovalsInput()
    {
        return RuntimeJson.Object(
            ("decisionTypeKey", JsonTreeNode.FromString(DecisionTypeKey)),
            ("tierKey", RuntimeJson.OptionalText(TierKey)),
            ("lifecycleStageKey", RuntimeJson.OptionalText(LifecycleStageKey)),
            ("changeTypeKey", RuntimeJson.OptionalText(ChangeTypeKey)),
            ("applicationKindKey", RuntimeJson.OptionalText(ApplicationKindKey)),
            ("targetStageKey", RuntimeJson.OptionalText(TargetStageKey)),
            ("portfolioPath", RuntimeJson.Strings(PortfolioPath)),
            ("subjectApplicationRegistered", SubjectApplicationRegistered ? JsonTreeNode.True : null),
            ("subjectKind", RuntimeJson.OptionalText(SubjectKind)),
            ("classificationKey", RuntimeJson.OptionalText(ClassificationKey)),
            ("aiUse", AiUse.HasValue ? JsonTreeNode.FromBoolean(AiUse.Value) : null),
            ("dataDomainLevelKey", RuntimeJson.OptionalText(DataDomainLevelKey)),
            ("dataTaxonomyChange", RuntimeJson.OptionalText(DataTaxonomyChange)));
    }

    // The proposal right check's input (document 24, tool 17; B24-5): the OP-02 scope, the product roles of the person
    // behind the call, and the owner roles that person holds now on the decision's application (a deputy too, as for
    // "own apps", document 26, section 5.3).
    public JsonTreeNode ProposalRightInput(IEnumerable<string> roleKeys, string? personPrincipalId)
    {
        IEnumerable<string> ownerRoles = personPrincipalId == null
            ? Enumerable.Empty<string>()
            : Owners.Where(o => string.Equals(o.Text("principal"), personPrincipalId, StringComparison.OrdinalIgnoreCase) && o.Text("ownerRole") != null).Select(o => o.Text("ownerRole")!);
        return RuntimeJson.Object(
            ("decisionTypeKey", JsonTreeNode.FromString(DecisionTypeKey)),
            ("tierKey", RuntimeJson.OptionalText(TierKey)),
            ("lifecycleStageKey", RuntimeJson.OptionalText(LifecycleStageKey)),
            ("changeTypeKey", RuntimeJson.OptionalText(ChangeTypeKey)),
            ("applicationKindKey", RuntimeJson.OptionalText(ApplicationKindKey)),
            ("targetStageKey", RuntimeJson.OptionalText(TargetStageKey)),
            ("portfolioPath", RuntimeJson.Strings(PortfolioPath)),
            ("subjectKind", RuntimeJson.OptionalText(SubjectKind)),
            ("classificationKey", RuntimeJson.OptionalText(ClassificationKey)),
            ("aiUse", AiUse.HasValue ? JsonTreeNode.FromBoolean(AiUse.Value) : null),
            ("dataDomainLevelKey", RuntimeJson.OptionalText(DataDomainLevelKey)),
            ("roleKeys", RuntimeJson.Strings((roleKeys ?? Enumerable.Empty<string>()).Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal))),
            ("ownerRoleKeys", RuntimeJson.Strings(ownerRoles.Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal))));
    }

    // OP-03 scope and parties (document 25, section 5.5). Role holders and linked exception decisions are not fetched by
    // this build: a separation rule that names a role, or an exception decision, sees none.
    public JsonTreeNode SeparationParties()
    {
        var owners = Owners
            .Where(o => o.Text("ownerRole") != null && o.Text("principal") != null)
            .GroupBy(o => o.Text("ownerRole")!, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new KeyValuePair<string, JsonTreeNode>(g.Key, RuntimeJson.Strings(g.Select(o => o.Text("principal")!).Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal))));
        return RuntimeJson.Object(
            ("proposerPrincipalId", RuntimeJson.OptionalText(ProposerPrincipalId)),
            ("instructingAdminPrincipalId", RuntimeJson.OptionalText(InstructingAdminPrincipalId)),
            ("deployerPrincipalIds", RuntimeJson.Strings(DeployerPrincipalIds)),
            ("ownerRoleHolders", JsonTreeNode.CreateObject(owners)));
    }

    public IReadOnlyList<string> AssignedOwnerRoleKeys()
    {
        return Owners.Select(o => o.Text("ownerRole")).Where(k => k != null).Select(k => k!).Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<StoredRow> PortfolioPathRows(FactSet facts)
    {
        var path = new List<StoredRow>();
        for (int level = 0; level < 33; level++)
        {
            StoredRow? portfolio = facts.One("portfolio-" + level);
            if (portfolio == null || path.Any(p => p.Id == portfolio.Id))
            {
                break;
            }

            path.Add(portfolio);
        }

        return path;
    }

    internal static IReadOnlyList<string> PortfolioPathOf(FactSet facts)
    {
        var path = new List<string>();
        for (int level = 0; level < 33; level++)
        {
            StoredRow? portfolio = facts.One("portfolio-" + level);
            if (portfolio == null || path.Contains(portfolio.Id))
            {
                break;
            }

            path.Add(portfolio.Id);
        }

        return path;
    }

    // An owner assignment counts from Start On until End On (document 26, RA-02: End On empty or in the future).
    internal static IReadOnlyList<StoredRow> CurrentOwners(IEnumerable<StoredRow> owners, DateTime asOfUtc)
    {
        return owners.Where(o =>
        {
            DateTime? start = RuntimeJson.Utc(o.Columns, "startOn");
            DateTime? end = RuntimeJson.Utc(o.Columns, "endOn");
            return (!start.HasValue || start.Value <= asOfUtc) && (!end.HasValue || end.Value > asOfUtc);
        }).ToArray();
    }
}

// Who one Approval Record asks (O10 Required Role, Required Owner Role, Asked Principal; document 25, B25-14).
public sealed class AskTarget
{
    public AskTarget(string? roleKey, string? ownerRoleKey, string? principalId)
    {
        RoleKey = roleKey;
        OwnerRoleKey = ownerRoleKey;
        PrincipalId = string.IsNullOrEmpty(principalId) ? null : principalId;
    }

    public string? RoleKey { get; }

    public string? OwnerRoleKey { get; }

    // The one person asked by name, or null when the role's holders are asked.
    public string? PrincipalId { get; }
}

// One owner change of O09 Proposed Changes Json (document 25, B25-8).
public sealed class ProposedOwnerChange
{
    private ProposedOwnerChange(string? ownerRoleKey, string? addObjectId, string? endAssignmentId, bool isDeputy)
    {
        OwnerRoleKey = ownerRoleKey;
        AddObjectId = addObjectId;
        EndAssignmentId = endAssignmentId;
        IsDeputy = isDeputy;
    }

    public string? OwnerRoleKey { get; }

    public string? AddObjectId { get; }

    public string? EndAssignmentId { get; }

    public bool IsDeputy { get; }

    internal static IReadOnlyList<ProposedOwnerChange> Read(StoredRow decision)
    {
        JsonTreeNode? proposal = decision.Column("proposedChangesJson");
        if (proposal == null || proposal.Kind != JsonTreeKind.Object || !proposal.TryGetMember("owners", out JsonTreeNode? owners) || owners.Kind != JsonTreeKind.Array)
        {
            return Array.Empty<ProposedOwnerChange>();
        }

        return owners.Items
            .Where(o => o.Kind == JsonTreeKind.Object)
            .Select(o => new ProposedOwnerChange(RuntimeJson.Text(o, "ownerRoleKey"), RuntimeJson.Text(o, "addObjectId"), RuntimeJson.Text(o, "endAssignmentId"), RuntimeJson.Flag(o, "isDeputy")))
            .ToArray();
    }
}
