using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Operations;
using Product.Engine.Core.Snapshots;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Json;

namespace Product.Mcp.Core.Backend.InProcess;

// Helpers the tool handlers share: application facts, owners and their gaps, list values and record visibility.
internal static partial class ToolHandlers
{
    private static readonly string[] OpenRecordMeanings = { "draft", "submitted", "in-progress" };

    // The application's display name: Name, else the record number.
    private static string AppName(StoredRow app)
    {
        return app.Text("name") ?? app.Text("recordNumber") ?? app.Id;
    }

    private static StoredRow RequireApplication(ToolScope s, string field = "businessApplicationId")
    {
        return s.RequireRow("business-application", s.Text(field), field);
    }

    // A C07 list value: the column holds the value key, the list is fixed per column.
    private static JsonTreeNode ListValue(ToolScope s, string list, string? key)
    {
        if (key == null)
        {
            return JsonTreeNode.Null;
        }

        SnapshotRow? row = s.ConfigRow("list-value", list + "/" + key);
        return Js.Obj(("key", JsonTreeNode.FromString(key)), ("label", JsonTreeNode.FromString(row == null ? key : Js.Text(row.Columns, "label") ?? key)));
    }

    private static void RequireListValue(ToolScope s, string list, string? key, string field)
    {
        if (key == null)
        {
            return;
        }

        SnapshotRow? row = s.ConfigRow("list-value", list + "/" + key);
        if (row == null || !row.IsActive)
        {
            throw s.Invalid(field, field + " \"" + key + "\" is not an active value of the " + list + " list.");
        }
    }

    private static string? LifecycleMeaning(ToolScope s, string? stageKey)
    {
        return s.ConfigRow("lifecycle-stage", stageKey)?.EngineMeaning;
    }

    private static long StageRank(ToolScope s, string? stageKey)
    {
        SnapshotRow? row = s.ConfigRow("lifecycle-stage", stageKey);
        return row == null ? long.MaxValue : Js.Number(row.Columns, "stageRank") ?? long.MaxValue;
    }

    // The holder of an owner role, not a deputy, as a principalRef or null.
    private static JsonTreeNode OwnerRef(ToolScope s, IReadOnlyList<StoredRow> owners, string ownerRoleKey)
    {
        StoredRow? holder = owners.FirstOrDefault(o => o.Text("ownerRole") == ownerRoleKey && !o.Flag("isDeputy"));
        return holder == null ? JsonTreeNode.Null : s.PrincipalRef(holder.Text("principal"));
    }

    private static JsonTreeNode OwnerAssignment(ToolScope s, StoredRow row)
    {
        string role = row.Text("ownerRole") ?? string.Empty;
        return Js.Obj(
            ("ownerAssignmentId", JsonTreeNode.FromString(row.Id)),
            ("ownerRoleKey", JsonTreeNode.FromString(role)),
            ("label", JsonTreeNode.FromString(s.Label("owner-role", role))),
            ("principal", s.PrincipalRef(row.Text("principal"))),
            ("isDeputy", JsonTreeNode.FromBoolean(row.Flag("isDeputy"))),
            ("deputyFor", Js.Str(row.Text("deputyFor"))),
            ("startOn", ToolScope.DateOrNull(row.Text("startOn"))),
            ("endOn", ToolScope.DateOrNull(row.Text("endOn"))),
            ("ownershipDecisionId", Js.Str(row.Text("ownershipDecision"))));
    }

    // The owner roles a gate requires a holder for (owner-assigned gate requirements), and the roles that need a deputy
    // once held (C28 Requires Deputy). Judgment call: a gap is a required role with no holder, or a held role with
    // Requires Deputy and no deputy. Other roles may stay empty. A requirement scoped to a tier or a kind of application
    // makes a gap only for an application of that tier or kind (document 21, B21-9: Balanced names a data owner at tiers 2
    // and 3, so a tier 1 application with no data owner has no gap). With no application, every requirement counts.
    private static List<(string Role, string Missing)> OwnerGaps(ToolScope s, StoredRow? app, IReadOnlyList<StoredRow> owners)
    {
        bool Applies(SnapshotRow r) => app == null
            || ((Js.Text(r.Columns, "tierKey") is not string tier || tier == app.Text("tier"))
                && (Js.Text(r.Columns, "applicationKindKey") is not string kind || kind == app.Text("applicationKind")));
        var required = new SortedSet<string>(
            s.ActiveRows("gate-requirement").Where(r => Js.Text(r.Columns, "requirementKind") == "owner-assigned" && Applies(r)).Select(r => Js.Text(r.Columns, "requiredOwnerRoleKey")).Where(k => k != null).Select(k => k!),
            StringComparer.Ordinal);
        var gaps = new List<(string, string)>();
        foreach (SnapshotRow role in s.ActiveRows("owner-role").OrderBy(r => r.KeyPath, StringComparer.Ordinal))
        {
            bool held = owners.Any(o => o.Text("ownerRole") == role.KeyPath && !o.Flag("isDeputy"));
            bool deputy = owners.Any(o => o.Text("ownerRole") == role.KeyPath && o.Flag("isDeputy"));
            if (!held && required.Contains(role.KeyPath))
            {
                gaps.Add((role.KeyPath, "holder"));
            }
            else if (held && !deputy && Js.Flag(role.Columns, "requiresDeputy") == true)
            {
                gaps.Add((role.KeyPath, "deputy"));
            }
        }

        return gaps;
    }

    // One owner gap of one application and owner role (document 24, B24-17).
    private sealed class OwnerGapItem
    {
        public OwnerGapItem(StoredRow app, string role, string missing, string? holderDirectoryState, bool listed)
        {
            App = app;
            Role = role;
            Missing = missing;
            HolderDirectoryState = holderDirectoryState;
            Listed = listed;
        }

        public StoredRow App { get; }

        public string Role { get; }

        // holder or deputy, as the owner gap list names it.
        public string Missing { get; }

        // The main holder's account state the last owner check found, or null.
        public string? HolderDirectoryState { get; }

        // True for a gap the owner gap list carries (no holder, no deputy). False for a holder whose account is disabled or
        // deleted, which the list leaves out: the page lists that holder from the owner assignments, to propose a successor.
        public bool Listed { get; }
    }

    // The one owner gap count every read shares (document 24, B24-17), so Overview (get_portfolio_summary), Owners
    // (list_owner_assignments) and Portfolios (list_portfolios) show the same number for the same scope. It counts the
    // registered applications only (a draft is still being set up, and an archived application has no owners to find).
    // An owner role has a gap when a gate requires a holder and nobody holds it, when it requires a deputy and has none,
    // or when its main holder's account is disabled or deleted (the role is held on paper only). One gap per application
    // and role, whatever its reasons.
    private static List<OwnerGapItem> OwnerGapsOf(ToolScope s, IEnumerable<StoredRow> apps)
    {
        var gaps = new List<OwnerGapItem>();
        foreach (StoredRow app in apps.Where(a => a.Text("statusMeaning") == "registered"))
        {
            IReadOnlyList<StoredRow> owners = s.CurrentOwners(app.Id);
            List<(string Role, string Missing)> listed = OwnerGaps(s, app, owners);
            string? StateOf(string role)
            {
                StoredRow? holder = owners.FirstOrDefault(o => o.Text("ownerRole") == role && !o.Flag("isDeputy"));
                return holder == null ? null : s.Principal(holder.Text("principal"))?.Text("directoryState");
            }

            foreach ((string role, string missing) in listed)
            {
                gaps.Add(new OwnerGapItem(app, role, missing, StateOf(role), true));
            }

            foreach (string role in owners.Where(o => !o.Flag("isDeputy") && o.Text("ownerRole") != null).Select(o => o.Text("ownerRole")!).Distinct(StringComparer.Ordinal).OrderBy(r => r, StringComparer.Ordinal))
            {
                string? state = StateOf(role);
                if ((state == "disabled" || state == "deleted") && !listed.Any(l => l.Role == role))
                {
                    gaps.Add(new OwnerGapItem(app, role, "holder", state, false));
                }
            }
        }

        return gaps;
    }

    // The application a record belongs to: its own column, or through its intake request or change request.
    // A due date before the reader's own calendar day (wave 4b QA walk): a review due today is not overdue at 9 pm in New
    // York, when the UTC day has already turned. A caller with no day of its own (an AI client) reads the UTC day.
    private static bool PastDue(ToolScope s, string? dueOn)
    {
        DateTime? due = Js.ParseMoment(dueOn);
        return due.HasValue && string.CompareOrdinal(due.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), s.PersonToday) < 0;
    }

    private static string? ApplicationOf(ToolScope s, StoredRow row)
    {
        string? direct = row.Text("businessApplication") ?? row.Text("subjectApplication");
        if (direct != null)
        {
            return direct;
        }

        string? intake = row.Text("subjectIntakeRequest") ?? row.Text("intakeRequest");
        if (intake != null)
        {
            return s.Rows.Get("intake-request", intake)?.Text("businessApplication");
        }

        string? change = row.Text("subjectChangeRequest") ?? row.Text("changeRequest");
        return change == null ? null : s.Rows.Get("change-request", change)?.Text("businessApplication");
    }

    // intake-visibility (document 22; document 26, section 5.2). "own": the requester, plus PMO, platform admin,
    // architect and auditor (the auditor reads everything, document 26, section 3.2; product review, fix round 1). Any other value
    // reads as open. D-41 (2026-11-24, document 24, B24-14): "own" also lets in the people the
    // request involves, so an approver is never sent to a page that says "not found": the business owner the request
    // names, a current owner of the application it is linked to, and anyone an approval record on a decision about the
    // request asks now (by name, by a product role they hold, or by an owner role they hold) or was answered by.
    private static bool CanReadIntake(ToolScope s, StoredRow intake)
    {
        if (s.Setting("intake-visibility") != "own")
        {
            return true;
        }

        // One answer per request in a call (wave 4b review): a list asks it of the same request for each record about it.
        if (s.IntakeReadable.TryGetValue(intake.Id, out bool known))
        {
            return known;
        }

        bool readable = IntakeReadableNow(s, intake);
        s.IntakeReadable[intake.Id] = readable;
        return readable;
    }

    private static bool IntakeReadableNow(ToolScope s, StoredRow intake)
    {
        if (Same(intake.Text("requester"), s.PersonId) || Same(intake.Text("createdByPrincipal"), s.PersonId)
            || s.HoldsRole("pmo") || s.HoldsRole("platform-admin") || s.HoldsRole("architect") || s.HoldsRole("auditor")
            || Same(intake.Text("businessOwner"), s.PersonId))
        {
            return true;
        }

        // The AI lead and the security reviewer oversee every person's AI work (document 26, B26-11), so they read a
        // request an AI client drafted (fix round 1: the estate's AI activity counted fewer drafts than Priya's own).
        if ((s.HoldsRole("ai-lead") || s.HoldsRole("security-reviewer")) && intake.Flag("isAiCreated"))
        {
            return true;
        }

        string? appId = intake.Text("businessApplication");
        if (appId != null && s.OwnsApplication(appId))
        {
            return true;
        }

        foreach (StoredRow decision in s.Query("decision", ("subjectIntakeRequest", intake.Id)))
        {
            string? decisionApp = decision.Text("subjectApplication") ?? appId;
            if (s.Query("approval-record", ("decision", decision.Id)).Any(r => Same(r.Text("approver"), s.PersonId) || AskedOfMe(s, r, decisionApp)))
            {
                return true;
            }
        }

        return false;
    }

    // What a list of decisions shows, read ahead in a few key set reads (wave 4b review; document 25, B25-23): their
    // applications and subjects, the requests on them, the reviews that name them, the deployments' versions and targets,
    // the uses' locations, the portfolios and the people. get_decision_log read 148 rows one by one for a page of 100.
    private static void PrefetchDecisionSubjects(ToolScope s, IReadOnlyCollection<StoredRow> decisions)
    {
        if (!s.Call.ReadsAreCached || decisions.Count == 0)
        {
            return;
        }

        s.Call.PrefetchAll("principal-reference");
        s.Call.PrefetchAll("portfolio");
        s.Call.PrefetchIds("business-application", decisions.Select(d => d.Text("subjectApplication")));
        s.Call.Prefetch("approval-record", "decision", decisions.Select(d => d.Id));
        s.Call.Prefetch("review", "outcomeDecision", decisions.Where(d => d.Text("subjectKind") == "review").Select(d => d.Id));
        foreach (string entity in new[] { "intake-request", "change-request", "deployment-record", "technical-component", "claim", "configuration-draft", "data-use", "data-mapping", "data-location" })
        {
            s.Call.PrefetchIds(entity, decisions.Where(d => d.Text("subjectKind") == entity).Select(d => SubjectIdOf(d)));
        }

        IReadOnlyList<StoredRow> deployments = s.Rows.GetMany("deployment-record", decisions.Where(d => d.Text("subjectKind") == "deployment-record").Select(d => SubjectIdOf(d)));
        s.Call.PrefetchIds("artifact-version", deployments.Select(d => d.Text("artifactVersion")));
        s.Call.PrefetchIds("deployment-target", deployments.Select(d => d.Text("deploymentTarget")));
        IReadOnlyList<StoredRow> uses = s.Rows.GetMany("data-use", decisions.Where(d => d.Text("subjectKind") == "data-use").Select(d => SubjectIdOf(d)));
        IReadOnlyList<StoredRow> mappings = s.Rows.GetMany("data-mapping", decisions.Where(d => d.Text("subjectKind") == "data-mapping").Select(d => SubjectIdOf(d)));
        s.Call.PrefetchIds("data-location", uses.Concat(mappings).Select(r => r.Text("dataLocation")));
        s.Call.PrefetchIds("business-application", uses.Select(u => u.Text("businessApplication")));
    }

    // True for a decision about an intake request the caller may not read (intake-visibility): the decision is hidden as
    // the request is, wherever a read would show its statement or subject (security review 2026-11-24, B24-14).
    private static bool HidesIntake(ToolScope s, StoredRow decision)
    {
        string? intakeId = decision.Text("subjectIntakeRequest");
        return intakeId != null && s.Rows.Get("intake-request", intakeId) is StoredRow intake && !CanReadIntake(s, intake);
    }

    // True for a row of a kind a visibility setting covers (an intake request, a claim, or a decision about a request)
    // that the caller may not read.
    private static bool HiddenRow(ToolScope s, StoredRow? row)
    {
        return row != null && row.Entity switch
        {
            "intake-request" => !CanReadIntake(s, row),
            "claim" => !CanReadClaim(s, row),
            "decision" => HidesIntake(s, row),
            _ => false,
        };
    }

    // claim-visibility "admins-and-claimant": platform admin, AI lead, and the suggested claimant or the claimant.
    private static bool CanReadClaim(ToolScope s, StoredRow claim)
    {
        if (s.Setting("claim-visibility") != "admins-and-claimant")
        {
            return true;
        }

        return s.HoldsRole("platform-admin") || s.HoldsRole("ai-lead")
            || string.Equals(claim.Text("suggestedClaimant"), s.PersonId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(claim.Text("claimedBy"), s.PersonId, StringComparison.OrdinalIgnoreCase);
    }

    // The portfolio and its parents, nearest first (the portfolio path OP-02 reads).
    private static List<string> PortfolioPath(ToolScope s, string? portfolioId)
    {
        var path = new List<string>();
        while (portfolioId != null && !path.Contains(portfolioId))
        {
            path.Add(portfolioId);
            portfolioId = s.Rows.Get("portfolio", portfolioId)?.Text("parentPortfolio");
        }

        return path;
    }

    // The IDs of the applications in a portfolio, child portfolios included (the Timeline and the Changes list by
    // portfolio), or null when no portfolio is named. A portfolio that does not exist is refused on the field.
    private static HashSet<string>? PortfolioApplications(ToolScope s, string? portfolioId, string field)
    {
        if (portfolioId == null)
        {
            return null;
        }

        s.RequireRow("portfolio", portfolioId, field);
        s.Call.PrefetchAll("portfolio");
        return new HashSet<string>(
            s.Rows.Query(RowQuery.For("business-application")).Where(a => PortfolioPath(s, a.Text("portfolio")).Contains(portfolioId, StringComparer.OrdinalIgnoreCase)).Select(a => a.Id),
            StringComparer.OrdinalIgnoreCase);
    }

    // In a portfolio's scope (wave 4b review; the Overview's tiles under a portfolio): a record about an application of the
    // portfolio or one below it (apps, from PortfolioApplications), or, for an intake request with no application yet,
    // when the intake's own portfolio is that portfolio or one below it.
    private static bool InPortfolioScope(ToolScope s, string portfolioId, HashSet<string> apps, string? appId, string? intakeId)
    {
        if (appId != null)
        {
            return apps.Contains(appId);
        }

        return intakeId != null
            && s.Rows.Get("intake-request", intakeId)?.Text("portfolio") is string intakePortfolio
            && PortfolioPath(s, intakePortfolio).Contains(portfolioId, StringComparer.OrdinalIgnoreCase);
    }

    // The due date of a review a person requests without one (request_architecture_review, request_security_review; wave
    // 4b): the person's day plus the setting review-request-due-days (14 in Balanced), so a review asked today is not
    // already due today. 14 days when a configuration lacks the setting.
    private static string ReviewRequestDueOn(ToolScope s)
    {
        DateTime today = DateTime.ParseExact(s.PersonToday, "yyyy'-'MM'-'dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);
        long days = Math.Max(0, Math.Min(s.SettingNumber("review-request-due-days") ?? 14, 3650));
        return today.AddDays(days).ToString("yyyy'-'MM'-'dd", System.Globalization.CultureInfo.InvariantCulture);
    }

    // A retired application: its lifecycle stage means retired, or its registration is archived (the due-state refresh's
    // live test). It owes no document; its links stay as history (wave 4b review).
    private static bool IsRetiredApplication(ToolScope s, StoredRow? app)
    {
        return app != null && (app.Text("statusMeaning") == "archived" || s.ConfigRow("lifecycle-stage", app.Text("lifecycleStage"))?.EngineMeaning == "retired");
    }

    private static bool IsOpen(StoredRow row)
    {
        return OpenRecordMeanings.Contains(row.Text("statusMeaning") ?? string.Empty, StringComparer.Ordinal);
    }

    // Who made the row's last engine-checked write, exactly as the engine stamped it (document 24, section 4.5), for a
    // read: { actorKind, principal, onBehalfOf, clientAppLabel, stampedOn }. JSON null for a row the engine never stamped,
    // such as seeded or imported data. Unlike a write's performedBy, nothing here comes from the reader's own session.
    private static JsonTreeNode Stamp(ToolScope s, StoredRow? row)
    {
        string? kind = row?.Text("performedByActorKind");
        string? principal = row?.Text("performedByPrincipal");
        if (row == null || kind == null || principal == null)
        {
            return JsonTreeNode.Null;
        }

        return Js.Obj(
            ("actorKind", JsonTreeNode.FromString(kind)),
            ("principal", s.PrincipalRef(principal)),
            ("onBehalfOf", s.PrincipalRef(row.Text("onBehalfOf"))),
            ("clientAppLabel", Js.Str(row.Text("clientApp"))),
            ("stampedOn", ToolScope.MomentOrNull(row.Text("engineCheckedOn"))),
            ("draftedBy", DraftedBy(s, row)));
    }

    // The AI client that created the row and the person it acted for (document 22, B22-15: Drafted By Client App and
    // Drafted For Principal), kept when a person edits the draft later, so the AI mark still reads "Drafted by Claude Code
    // for Priya Castell". A row written before the pair existed falls back to its last stamp while an AI client wrote it
    // last, else to Created By Principal with no client. Null for a row no AI client created.
    private static JsonTreeNode DraftedBy(ToolScope s, StoredRow row)
    {
        string? client = row.Text("draftedByClientApp");
        string? person = row.Text("draftedForPrincipal");
        if (client == null && person == null)
        {
            if (!row.Flag("isAiCreated"))
            {
                return JsonTreeNode.Null;
            }

            bool lastByAi = AiActorKinds.Contains(row.Text("performedByActorKind") ?? string.Empty, StringComparer.Ordinal);
            client = lastByAi ? row.Text("clientApp") : null;
            person = lastByAi ? row.Text("onBehalfOf") ?? row.Text("createdByPrincipal") : row.Text("createdByPrincipal");
        }

        return Js.Obj(("clientAppLabel", Js.Str(client)), ("forPrincipal", s.PrincipalRef(person)));
    }

    // True for a row an AI client created (the engine's Is AI Created stamp). False for a seeded row, which has none.
    private static JsonTreeNode AiCreated(StoredRow? row)
    {
        return JsonTreeNode.FromBoolean(row != null && row.Flag("isAiCreated"));
    }

    // The approval requests a person may answer now (get_my_queue, list_approvals): the record asks them by name, or asks a
    // product role they hold, or an owner role they hold now on the decision's application (a deputy too). A record that
    // asks one person by name (a proposed owner, or a portfolio's Accountable Principal; document 25, B25-14) is that
    // person's only. The engine's answer check (AN-01) decides again, with the separation rules, when they answer.
    private static bool AskedOfMe(ToolScope s, StoredRow record, string? applicationId)
    {
        if (record.Text("askedPrincipal") is string asked)
        {
            return Same(asked, s.PersonId);
        }

        return (record.Text("requiredRole") is string role && s.HoldsRole(role))
            || (record.Text("requiredOwnerRole") is string ownerRole && applicationId != null && s.OwnsApplication(applicationId, ownerRole));
    }

    // A due review is the person's to do (document 24, B24-10), for one of three reasons:
    // (1) lead: they hold the review type's lead owner role (C24 Default Owner Role) on its application now, a deputy too;
    //     when the type names no lead role, or nobody holds it now, any owner role on the application counts;
    // (2) decides: through a product role they hold now, they may propose the decision the review records (C24 Produces
    //     Decision Type): a responsible, accountable or consulted Decision Right (C31) in the winning group for the
    //     application's scope, the engine's proposal right check (document 24, B24-5) run read only. In Balanced it gives
    //     every architecture review to the architect (design) and every benefit review to the PMO (portfolio);
    // (3) approves: an approval rule asks a role they hold for that decision, or for the gate decision of a gate the review
    //     type is required by (C14 review-done) in the application's scope, as OP-02 resolves it. In Balanced it gives the
    //     tier 3 security review to the security reviewer, whom the tier 3 go-live asks.
    // Null when the review is not theirs. The cache holds the answer of (2) and (3) per review type and scope.
    private static string? ReviewRoleOf(ToolScope s, StoredRow review, StoredRow? app, Dictionary<string, string?> cache)
    {
        string? reviewType = review.Text("reviewType");
        SnapshotRow? type = s.ConfigRow("review-type", reviewType);
        string? lead = Js.Text(type?.Columns, "defaultOwnerRoleKey");
        string? appId = review.Text("businessApplication");
        if (appId != null)
        {
            IReadOnlyList<StoredRow> owners = s.CurrentOwners(appId);
            bool leadHeld = lead != null && owners.Any(o => o.Text("ownerRole") == lead);
            if (owners.Any(o => Same(o.Text("principal"), s.PersonId) && (!leadHeld || o.Text("ownerRole") == lead)))
            {
                return "lead";
            }
        }

        if (app == null || reviewType == null || s.Session.RoleKeys.Count == 0)
        {
            return null;
        }

        string key = string.Join("|", reviewType, app.Text("tier"), app.Text("lifecycleStage"), app.Text("applicationKind"), app.Text("portfolio"));
        if (cache.TryGetValue(key, out string? role))
        {
            return role;
        }

        string? decisionType = Js.Text(type?.Columns, "producesDecisionTypeKey");
        List<string> path = PortfolioPath(s, app.Text("portfolio"));
        JsonTreeNode Scope(string typeKey, params (string, JsonTreeNode?)[] more)
        {
            var members = new List<(string, JsonTreeNode?)>
            {
                ("decisionTypeKey", JsonTreeNode.FromString(typeKey)),
                ("tierKey", Js.OptStr(app.Text("tier"))),
                ("lifecycleStageKey", Js.OptStr(app.Text("lifecycleStage"))),
                ("applicationKindKey", Js.OptStr(app.Text("applicationKind"))),
                ("portfolioPath", Js.Strs(path)),
            };
            members.AddRange(more);
            return Js.Obj(members.ToArray());
        }

        if (decisionType != null && s.Call.ProposalRight(Scope(decisionType, ("roleKeys", Js.Strs(s.Session.RoleKeys)))).Outcome == ProposalRightsOperation.Allowed)
        {
            role = "decides";
        }
        else
        {
            var asked = new List<string>();
            if (decisionType != null)
            {
                asked.Add(decisionType);
            }

            foreach (SnapshotRow requirement in s.ActiveRows("gate-requirement").Where(r => Js.Text(r.Columns, "requirementKind") == "review-done"
                && Js.Text(r.Columns, "requiredReviewTypeKey") == reviewType
                && (Js.Text(r.Columns, "tierKey") is not string t || t == app.Text("tier"))
                && (Js.Text(r.Columns, "applicationKindKey") is not string k || k == app.Text("applicationKind"))))
            {
                if (Js.Text(s.ConfigRow("gate", Js.Text(requirement.Columns, "gateKey"))?.Columns, "gateDecisionTypeKey") is string gateType && !asked.Contains(gateType, StringComparer.Ordinal))
                {
                    asked.Add(gateType);
                }
            }

            foreach (string typeKey in asked)
            {
                EvaluationResult<JsonTreeNode?> required = s.Call.Evaluate(EngineOperation.ResolveRequiredApprovals, Scope(typeKey));
                if (required.Outcome == "required" && Js.Items(required.Payload, "requirements").Any(r => Js.Text(r, "approverRoleKey") is string approver && s.HoldsRole(approver)))
                {
                    role = "approves";
                    break;
                }
            }
        }

        cache[key] = role;
        return role;
    }

    private static bool ReviewIsMine(ToolScope s, StoredRow review, StoredRow? app, Dictionary<string, string?> cache)
    {
        return ReviewRoleOf(s, review, app, cache) != null;
    }

    // What a decision is about, for a list or a page: the subject's label, and its application when it has one.
    private static (string Label, string? ApplicationId, string? ApplicationName) DecisionSubjectLabel(ToolScope s, StoredRow decision)
    {
        string kind = decision.Text("subjectKind") ?? "business-application";
        string? id = SubjectIdOf(s, decision);
        string? appId = decision.Text("subjectApplication");
        StoredRow? app = appId == null ? null : s.Rows.Get("business-application", appId);
        StoredRow? review = kind == "review" && ReviewIdOf(s, decision) is string reviewId ? s.Rows.Get("review", reviewId) : null;
        StoredRow? subject = id == null || kind == "review" ? null : kind switch
        {
            "intake-request" => s.Rows.Get("intake-request", id),
            "change-request" => s.Rows.Get("change-request", id),
            "deployment-record" => s.Rows.Get("deployment-record", id),
            "technical-component" => s.Rows.Get("technical-component", id),
            "claim" => s.Rows.Get("claim", id),
            "configuration-draft" => s.Rows.Get("configuration-draft", id),
            "data-use" => s.Rows.Get("data-use", id),
            "data-mapping" => s.Rows.Get("data-mapping", id),
            "data-location" => s.Rows.Get("data-location", id),
            "data-domain" => null,
            _ => app,
        };
        string label = kind switch
        {
            "intake-request" or "change-request" => subject?.Text("title") ?? subject?.Text("recordNumber") ?? id ?? string.Empty,
            "technical-component" or "claim" => subject?.Text("labelAsSeen") ?? subject?.Text("externalId") ?? id ?? string.Empty,
            "deployment-record" => DeploymentLabel(s, subject) ?? "Deployment record " + (subject?.Text("recordNumber") ?? id),
            "configuration-draft" => ConfigurationDraftLabel(s, subject) ?? "Configuration draft " + (subject?.Text("recordNumber") ?? id),
            "review" when review != null => s.Label("review-type", review.Text("reviewType")) + (app == null ? string.Empty : ": " + AppName(app)),
            "data-use" or "data-mapping" or "data-domain" or "data-location" => DataSubjectLabel(s, kind, id, subject, app),
            _ => app == null ? id ?? string.Empty : AppName(app),
        };
        return (label, appId, app == null ? null : AppName(app));
    }

    // A data subject as people know it (data taxonomy slice 3): a use as its application and the domain's path, a copy as
    // the path and the location, a domain as its path, a location as its name.
    private static string DataSubjectLabel(ToolScope s, string kind, string? id, StoredRow? subject, StoredRow? app)
    {
        var tree = new Product.Engine.Core.Operations.DataTree(s.Snapshot);
        string Path(string? key) => key == null ? string.Empty : tree.Domain(key) == null ? key : tree.PathText(key);
        StoredRow? location = kind == "data-location" ? subject : subject?.Text("dataLocation") is string l ? s.Rows.Get("data-location", l) : null;
        string where = location == null ? string.Empty : location.Text("labelAsSeen") ?? location.Id;
        return kind switch
        {
            "data-use" => (app == null ? string.Empty : AppName(app) + ": ") + Path(subject?.Text("dataDomain")) + (subject?.Flag("reachesAi") == true ? ", with AI" : string.Empty),
            "data-mapping" => Path(subject?.Text("dataDomain")) + (where.Length == 0 ? string.Empty : " in " + where),
            "data-domain" => Path(id),
            _ => where.Length == 0 ? id ?? string.Empty : where,
        };
    }

    // A configuration draft as people know it: the first line of its change in plain words (labels, not keys), and how
    // many more lines follow. "The AI use rule of Workplace / Visitor log changes from not set to never." The engine's
    // stored summary ("Changed Data Domain `visitor-log`: aiUseRule.") only when the change cannot be described (fix
    // round 1, product review: no raw key in backticks on Approvals, the decision page, Versions or the decision log).
    internal static string? ConfigurationDraftLabel(ToolScope s, StoredRow? draft)
    {
        if (draft == null)
        {
            return null;
        }

        string[] lines = DraftChangeLines(s, draft).Select(l => l.TrimEnd('.')).Where(l => l.Length > 0).ToArray();
        return lines.Length == 0 ? draft.Text("name") : lines.Length == 1 ? lines[0] : lines[0] + ", and " + (lines.Length - 1) + " more change" + (lines.Length == 2 ? string.Empty : "s");
    }

    // A deployment as people know it: the artifact and version, and where it went ("Invoice Matching 1.12.1 to
    // Production"). Null when the record names no artifact.
    private static string? DeploymentLabel(ToolScope s, StoredRow? record)
    {
        StoredRow? artifact = record == null ? null : s.Rows.Get("artifact-version", record.Text("artifactVersion") ?? string.Empty);
        if (artifact == null)
        {
            return null;
        }

        string what = ((artifact.Text("artifactName") ?? string.Empty) + " " + (artifact.Text("version") ?? string.Empty)).Trim();
        StoredRow? target = s.Rows.Get("deployment-target", record!.Text("deploymentTarget") ?? string.Empty);
        string? where = target?.Text("name") ?? target?.Text("labelAsSeen");
        return what.Length == 0 ? null : where == null ? "Deployment of " + what : "Deployment of " + what + " to " + where;
    }

    // { decisionId, decisionType, status, outcome, approvalsSummary } of one decision, for a list item.
    private static JsonTreeNode DecisionBrief(ToolScope s, StoredRow decision)
    {
        return Js.Obj(
            ("decisionId", JsonTreeNode.FromString(decision.Id)),
            ("decisionType", s.LabelledKey("decision-type", decision.Text("decisionType") ?? "unknown")),
            ("status", s.StatusValue(decision) is JsonTreeNode st && st.Kind != JsonTreeKind.Null ? st : ToolScope.StatusValue("proposed", null)),
            ("outcome", DecisionOutcome(s, decision)),
            ("approvalsSummary", ApprovalsSummary(s, decision.Id)));
    }

    private static JsonTreeNode DecisionOutcome(ToolScope s, StoredRow decision)
    {
        string? outcome = decision.Text("outcome");
        return outcome == null ? JsonTreeNode.Null : Js.Obj(("key", JsonTreeNode.FromString(outcome)), ("label", JsonTreeNode.FromString(s.Label("decision-outcome", decision.Text("decisionType") + "/" + outcome))));
    }

    // Newest first by the engine's last check, then by ID (seeded rows have no stamp and sort last).
    private static IOrderedEnumerable<StoredRow> NewestFirst(IEnumerable<StoredRow> rows)
    {
        return rows.OrderByDescending(r => r.Text("decidedOn") ?? r.Text("engineCheckedOn") ?? string.Empty, StringComparer.Ordinal).ThenByDescending(r => r.Id, StringComparer.Ordinal);
    }

    private static bool Same(string? a, string? b)
    {
        return a != null && b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    // A label inside a sentence: its first letter lower case and the rest as written, so "Calls API of" reads "calls API
    // of", never "calls api of" (fix round 2).
    private static string LowerFirst(string text)
    {
        return text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text.Substring(1);
    }

    // "1 person is asked", "2 people are asked": a count with the word in its own form, never "(s)".
    private static string Count(int count, string one, string many)
    {
        return count + " " + (count == 1 ? one : many);
    }

    private static JsonTreeNode Bucket(string? key, string label, int count)
    {
        return Js.Obj(("key", key == null ? JsonTreeNode.Null : JsonTreeNode.FromString(key)), ("label", JsonTreeNode.FromString(label)), ("count", JsonTreeNode.FromInt64(count)));
    }

    // The JSON array of a list of objects.
    private static JsonTreeNode Arr(IEnumerable<JsonTreeNode> items)
    {
        return Js.Arr(items);
    }

    private static JsonTreeNode Ids(IEnumerable<string> ids)
    {
        return Js.Strs(ids);
    }

    // Lowercase words of two letters or more, for the duplicate check.
    private static HashSet<string> Words(string? text)
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        if (text == null)
        {
            return words;
        }

        var current = new System.Text.StringBuilder();
        foreach (char c in text.ToLowerInvariant() + " ")
        {
            if (char.IsLetterOrDigit(c))
            {
                current.Append(c);
            }
            else
            {
                if (current.Length >= 2)
                {
                    words.Add(current.ToString());
                }

                current.Clear();
            }
        }

        return words;
    }
}
