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
//   portfolio path. A change type that requires a standard change model needs the OP-12 result, which this build does
//   not plan yet, so OP-02 answers invalid input there.
// - The proposer is the person a decision was created for (On Behalf Of), else its creator (document 25, section 5.5).
// - Principal references are Entra object IDs in the runtime's rows; the physical stores map them to O05 rows.
public sealed class DecisionScope
{
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

    public IReadOnlyList<string> PortfolioPath { get; private set; } = Array.Empty<string>();

    // Current owner assignments of the application (End On empty or later than today).
    public IReadOnlyList<StoredRow> Owners { get; private set; } = Array.Empty<StoredRow>();

    public string? ProposerPrincipalId { get; private set; }

    public string? InstructingAdminPrincipalId { get; private set; }

    public IReadOnlyList<string> DeployerPrincipalIds { get; private set; } = Array.Empty<string>();

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
        yield return FactPlan.Of(
            appId == null ? null : FactQuery.ById("application", "business-application", appId),
            intakeId == null ? null : FactQuery.ById("intake", "intake-request", intakeId),
            intakeId == null ? null : FactQuery.Where("assessments", RowQuery.For("tier-assessment").Where("intakeRequest", intakeId)),
            changeId == null ? null : FactQuery.ById("change", "change-request", changeId),
            changeId == null ? null : FactQuery.Where("deployments", RowQuery.For("deployment-record").Where("changeRequest", changeId)),
            draftId == null ? null : FactQuery.ById("draft", "configuration-draft", draftId));

        appId ??= facts.One("intake")?.Text("businessApplication") ?? facts.One("change")?.Text("businessApplication");
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
        };

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

        scope.ProposerPrincipalId = decision.Text("onBehalfOf") ?? decision.Text("createdByPrincipal");
        scope.InstructingAdminPrincipalId = scope.Draft?.Text("instructedBy");
        scope.DeployerPrincipalIds = facts.All("deployments").Select(d => d.Text("deployedBy")).Where(p => p != null).Select(p => p!).Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        return scope;
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
            ("portfolioPath", RuntimeJson.Strings(PortfolioPath)));
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
