using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Operations;
using Product.Engine.Core.Snapshots;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Json;

namespace Product.Mcp.Core.Backend.InProcess;

// Session toolset (tools 1 to 3) and portfolio toolset (tools 4 to 9) of document 24, sections 6.2 and 6.3.
internal static partial class ToolHandlers
{
    // ---- 1. whoami ----

    // actorKind is the entry's: ai-client-for-person on the MCP endpoint, person through the product's web app.
    private static JsonTreeNode WhoAmI(ToolScope s)
    {
        if (!s.IsAiCaller && s.Session.ActorKind != "person")
        {
            throw s.Refused(McpErrorCodes.NotAuthorized, "An agent identity or agent user holds no product role and reads nothing here (document 26, RA-06).");
        }

        StoredRow? me = s.Principal(s.PersonId);
        BackendSession session = s.Session;
        JsonTreeNode data = Js.Obj(
            ("principal", Js.Obj(
                ("objectId", JsonTreeNode.FromString(s.PersonId)),
                ("displayLabel", JsonTreeNode.FromString(s.DisplayLabel(s.PersonId))),
                ("principalKind", JsonTreeNode.FromString(me?.Text("principalKind") ?? EngineContract.PrincipalKinds.ToWire(session.Caller.PrincipalKind))))),
            ("productRoles", Arr(session.Roles.OrderBy(r => r.RoleKey, StringComparer.Ordinal).ThenBy(r => r.GroupObjectId, StringComparer.Ordinal).Select(r => Js.Obj(
                ("roleKey", JsonTreeNode.FromString(r.RoleKey)),
                ("label", JsonTreeNode.FromString(r.Label)),
                ("viaGroup", Js.Obj(("objectId", JsonTreeNode.FromString(r.GroupObjectId)), ("displayLabel", JsonTreeNode.FromString(r.GroupLabel)))))))),
            ("actorKind", JsonTreeNode.FromString(s.IsAiCaller ? "ai-client-for-person" : "person")),
            ("clientApp", Js.Obj(("appId", JsonTreeNode.FromString(session.Caller.ClientAppId)), ("label", JsonTreeNode.FromString(session.Caller.ClientAppLabel ?? session.Caller.ClientAppId)))),
            ("configurationSet", Js.Obj(
                ("key", JsonTreeNode.FromString(session.ConfigurationSetKey)),
                ("version", JsonTreeNode.FromInt64(session.ConfigurationSetVersion)),
                ("publishedOn", Js.Utc(s.InForcePublishedOn)))),
            ("toolsets", Js.Strs(s.Invocation.VisibleToolsets)),
            ("tools", Js.Strs(s.Invocation.VisibleTools)));
        string summary = session.Roles.Count == 0
            ? "You are signed in but hold no product role. Ask for a role through its Entra group."
            : "You hold " + string.Join(", ", session.RoleKeys) + " and may use " + s.Invocation.VisibleTools.Count + " tools.";
        return s.Answer(data, summary);
    }

    // ---- 2. get_my_queue ----

    // What needs the person. Approvals they may answer now (asked of them, and the engine's answer check allows it, so a
    // request a separation rule keeps them from answering is left out); due reviews that are theirs to do, by role (ReviewRoleOf: the
    // review type's lead owner, a person who decides the decision it records, or a person whose role approves it);
    // documents missing, stale or waiting for confirmation, for their owners (DocumentIsMine: a link's Document Owner, and
    // for a missing document or a link with no owner the application's technical owner, or any owner when it has none;
    // missing and date-stale come from OP-06, computed when read, never stored); claims suggested to them; their drafts,
    // and decisions they proposed but have not submitted; owner gaps on their applications. Each item carries the page to
    // act on (recordUrl): the record where its action is (document 24, B24-10). Each says whether an AI client created
    // its record and who last wrote it, as the engine stamped it.
    private static JsonTreeNode GetMyQueue(ToolScope s)
    {
        var kinds = new HashSet<string>(s.Strings("kinds"), StringComparer.Ordinal);
        bool Want(string kind) => kinds.Count == 0 || kinds.Contains(kind);
        var items = new List<(int Order, string Due, JsonTreeNode Item)>();
        PrefetchMyQueue(s, Want);
        var myApps = s.Query("owner-assignment", ("principal", s.PersonId))
            .Select(o => o.Text("businessApplication")).Where(a => a != null).Select(a => a!).Distinct(StringComparer.Ordinal)
            .Where(a => s.OwnsApplication(a)).OrderBy(a => a, StringComparer.Ordinal).ToList();

        JsonTreeNode Item(string kind, string subjectKind, string subjectId, string title, string? app, JsonTreeNode dueOn, JsonTreeNode status, string why, string recordUrl, string? nextTool, StoredRow? stamped)
        {
            return Js.Obj(
                ("kind", JsonTreeNode.FromString(kind)),
                ("subjectKind", JsonTreeNode.FromString(subjectKind)),
                ("subjectId", JsonTreeNode.FromString(subjectId)),
                ("title", JsonTreeNode.FromString(title)),
                ("businessApplicationId", Js.Str(app)),
                ("dueOn", dueOn),
                ("status", status),
                ("why", JsonTreeNode.FromString(why)),
                ("recordUrl", JsonTreeNode.FromString(recordUrl)),
                ("nextTool", Js.Str(nextTool)),
                ("isAiCreated", AiCreated(stamped)),
                ("performedBy", Stamp(s, stamped)));
        }

        if (Want("approval"))
        {
            foreach (StoredRow record in s.Query("approval-record", ("outcomeMeaning", "pending")))
            {
                StoredRow? decision = s.Rows.Get("decision", record.Text("decision") ?? string.Empty);
                if (decision == null)
                {
                    continue;
                }

                string? app = ApplicationOf(s, decision);
                if (!AskedOfMe(s, record, app))
                {
                    continue;
                }

                // A request the engine's answer check refuses (a separation rule keeps the person from answering it) is not
                // work for the person, so the queue leaves it out (wave 4b verify round). The decision page still says why.
                if (!PersonAnswer(s, record, app).May)
                {
                    continue;
                }

                items.Add((0, record.Text("expiresOn") ?? "~", Item(
                    "approval", "decision", decision.Id, decision.Text("statement") ?? "Decision " + decision.Id, app,
                    ToolScope.DateOrNull(record.Text("expiresOn")), s.StatusValue(record, "outcome", "outcomeMeaning"),
                    s.IsAiCaller
                        ? "Waiting for the person's answer. The person answers on the decision page in the product's web app; an AI client never answers an approval."
                        : "Waiting for your answer. Answer it on the decision page.",
                    s.RecordUrl("decision", decision.Id), s.IsAiCaller ? null : "answer_approval", decision)));
            }
        }

        if (Want("review"))
        {
            var cache = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (StoredRow review in s.Query("review", ("statusMeaning", "due")))
            {
                string? app = review.Text("businessApplication");
                StoredRow? application = app == null ? null : s.Rows.Get("business-application", app);
                string? role = ReviewRoleOf(s, review, application, cache);
                if (role == null)
                {
                    continue;
                }

                SnapshotRow? reviewType = s.ConfigRow("review-type", review.Text("reviewType"));
                string type = s.Label("review-type", review.Text("reviewType"));
                string? produces = Js.Text(reviewType?.Columns, "producesDecisionTypeKey");
                string? lead = Js.Text(reviewType?.Columns, "defaultOwnerRoleKey");
                StoredRow? outcome = review.Text("outcomeDecision") is string outcomeId ? s.Rows.Get("decision", outcomeId) : null;
                string decision = s.Label("decision-type", produces).ToLowerInvariant();
                string why = role switch
                {
                    "lead" when lead != null && app != null && !s.CurrentOwners(app).Any(o => o.Text("ownerRole") == lead)
                        => "Nobody holds the " + s.Label("owner-role", lead).ToLowerInvariant() + " role on " + (application == null ? "the application" : AppName(application)) + ", so its owners are asked, and this review is due.",
                    "lead" => "You " + (lead == null ? "own the application" : "are its " + s.Label("owner-role", lead).ToLowerInvariant()) + ", and this review is due.",
                    "decides" => "This review is due, and you may propose the " + decision + " decision it records.",
                    _ => "This review is due, and your role approves the " + decision + " decision it records or the gate it feeds.",
                };
                string act = outcome?.Text("statusMeaning") == "accepted" ? "complete_review" : outcome == null ? "save_decision_proposal" : "get_decision";
                if (act == "complete_review")
                {
                    why += " Its outcome decision is accepted, so complete it.";
                }

                items.Add((1, review.Text("dueOn") ?? "~", Item(
                    "review", "review", review.Id, type + (application == null ? string.Empty : ": " + AppName(application)), app,
                    ToolScope.DateOrNull(review.Text("dueOn")), s.StatusValue(review), why,
                    s.RecordUrl("review", review.Id), s.IsAiCaller && act == "complete_review" ? null : act, review)));
            }
        }

        if (Want("document"))
        {
            var covered = new HashSet<string>(StringComparer.Ordinal);
            foreach (string app in myApps)
            {
                StoredRow? application = s.Rows.Get("business-application", app);
                if (application == null)
                {
                    continue;
                }

                foreach (DocumentRow row in DocumentStatus(s, application))
                {
                    string type = s.Label("document-type", row.DocumentTypeKey);
                    StoredRow? link = row.DocumentLinkId == null ? null : s.Rows.Get("document-link", row.DocumentLinkId);
                    if (link != null)
                    {
                        covered.Add(link.Id);
                    }

                    if (!DocumentIsMine(s, app, link))
                    {
                        continue;
                    }

                    if (row.Meaning == "missing")
                    {
                        items.Add((2, "~", Item(
                            "document", "business-application", app, AppName(application) + ": " + type + " is missing", app, JsonTreeNode.Null, ToolScope.StatusValue("missing", null),
                            "A document the configuration requires for an application you keep the documents of has no link yet. Link it on the application's page.", s.RecordUrl("business-application", app), "record_document_link", null)));
                    }
                    else if (row.Meaning == "stale" && link != null)
                    {
                        string? staleOn = row.StaleOn ?? StaleOnText(s, s.ConfigRow("document-type", link.Text("documentType")), link);
                        items.Add((2, staleOn ?? "~", Item(
                            "document", "document-link", link.Id, AppName(application) + ": " + type + " is stale", app, ToolScope.DateOrNull(staleOn), ToolScope.StatusValue("stale", null),
                            "A required document of an application you own is stale: it was last checked too long ago, or it was marked stale.", s.RecordUrl("document-link", link.Id), "confirm_document_checked", link)));
                    }
                    else if (row.Meaning == "draft" && link != null)
                    {
                        items.Add((2, "~", Item(
                            "document", "document-link", link.Id, AppName(application) + ": " + type + " waits for confirmation", app, JsonTreeNode.Null, s.StatusValue(link),
                            "A required document of an application you own is a draft. Check it and mark it current.", s.RecordUrl("document-link", link.Id), "confirm_document_checked", link)));
                    }
                }
            }

            // Links not covered above: any other link on an application the person owns, and any link they own as the
            // document owner.
            var links = myApps.SelectMany(a => s.Query("document-link", ("businessApplication", a)))
                .Concat(s.Query("document-link", ("documentOwner", s.PersonId)))
                .GroupBy(l => l.Id, StringComparer.Ordinal).Select(g => g.First())
                .Where(l => !covered.Contains(l.Id) && DocumentIsMine(s, l.Text("businessApplication"), l))
                .Where(l => !IsRetiredApplication(s, l.Text("businessApplication") is string linkApp ? s.Rows.Get("business-application", linkApp) : null))
                .OrderBy(l => l.Id, StringComparer.Ordinal).ToList();
            foreach (StoredRow link in links)
            {
                string? app = link.Text("businessApplication");
                StoredRow? application = app == null ? null : s.Rows.Get("business-application", app);
                string type = s.Label("document-type", link.Text("documentType"));
                string? staleOn = StaleOnText(s, s.ConfigRow("document-type", link.Text("documentType")), link);
                string meaning = link.Text("statusMeaning") ?? "draft";
                bool dateStale = meaning == "current" && Js.ParseMoment(staleOn) is DateTime due && due.Date < s.Now.Date;
                string name = application == null ? string.Empty : AppName(application) + ": ";
                if (meaning == "stale" || dateStale)
                {
                    items.Add((2, staleOn ?? "~", Item(
                        "document", "document-link", link.Id, name + type + " is stale", app, ToolScope.DateOrNull(staleOn), ToolScope.StatusValue("stale", null),
                        "A document you own, or one of an application you own, is stale.", s.RecordUrl("document-link", link.Id), "confirm_document_checked", link)));
                }
                else if (meaning == "draft")
                {
                    items.Add((2, "~", Item(
                        "document", "document-link", link.Id, name + type + " waits for confirmation", app, JsonTreeNode.Null, s.StatusValue(link),
                        "A document you own, or one of an application you own, is a draft. Check it and mark it current.", s.RecordUrl("document-link", link.Id), "confirm_document_checked", link)));
                }
            }
        }

        if (Want("owner-gap"))
        {
            foreach (string app in myApps)
            {
                StoredRow? application = s.Rows.Get("business-application", app);
                foreach ((string role, string missing) in OwnerGaps(s, application, s.CurrentOwners(app)))
                {
                    items.Add((5, "~", Item(
                        "owner-gap", "business-application", app, (application == null ? app : AppName(application)) + ": " + s.Label("owner-role", role) + " has no " + missing, app,
                        JsonTreeNode.Null, JsonTreeNode.Null, "An owner role on an application you own has no " + missing + ".", s.RecordUrl("business-application", app), "propose_owner_change", null)));
                }
            }
        }

        if (Want("data-gap"))
        {
            foreach (JsonTreeNode finding in MyDataFindings(s))
            {
                string subjectKind = Js.Text(finding, "subjectKind") ?? "data-domain";
                items.Add((6, "~", Item(
                    "data-gap", subjectKind, Js.Text(finding, "subjectId") ?? string.Empty, Js.Text(finding, "check") + ": " + (PathText(finding, "domain.path") ?? Js.Text(finding, "applicationName") ?? Js.Text(finding, "subjectId")),
                    Js.Text(finding, "businessApplicationId"), JsonTreeNode.Null, JsonTreeNode.Null, Js.Text(finding, "message") ?? "A data gap names you.",
                    Js.Text(finding, "recordUrl") ?? s.RecordUrl("data-domain", null), Js.Text(finding, "fixTool"), null)));
            }
        }

        if (Want("claim"))
        {
            foreach (StoredRow claim in s.Query("claim", ("suggestedClaimant", s.PersonId), ("statusMeaning", "open")))
            {
                items.Add((3, "~", Item(
                    "claim", "claim", claim.Id, claim.Text("labelAsSeen") ?? claim.Id, null, JsonTreeNode.Null, s.StatusValue(claim),
                    "The gap check suggests you as the owner of this item.", s.RecordUrl("claim", claim.Id), "claim_resource", claim)));
            }
        }

        if (Want("draft"))
        {
            foreach ((string entity, string column, string title) in new[] { ("intake-request", "requester", "title"), ("change-request", "createdByPrincipal", "title"), ("business-application", "createdByPrincipal", "name") })
            {
                foreach (StoredRow draft in s.Query(entity, (column, s.PersonId), ("statusMeaning", "draft")))
                {
                    // A draft application that an intake request carries (a claim for a new application) is acted on
                    // through that request, which registers it once triage accepts it.
                    StoredRow? carrier = entity == "business-application" ? s.Query("intake-request", ("businessApplication", draft.Id)).FirstOrDefault(i => i.Text("statusMeaning") is "draft" or "submitted" or "in-progress") : null;
                    if (carrier != null && Same(carrier.Text("requester"), s.PersonId) && carrier.Text("statusMeaning") == "draft")
                    {
                        continue;
                    }

                    if (carrier != null)
                    {
                        items.Add((4, "~", Item(
                            "draft", "intake-request", carrier.Id, draft.Text(title) ?? draft.Id, draft.Id, JsonTreeNode.Null, s.StatusValue(carrier),
                            "This draft application is registered through its intake request once triage accepts it.",
                            s.RecordUrl("intake-request", carrier.Id), null, draft)));
                        continue;
                    }

                    items.Add((4, "~", Item(
                        "draft", entity, draft.Id, draft.Text(title) ?? draft.Text("recordNumber") ?? draft.Id, entity == "business-application" ? draft.Id : draft.Text("businessApplication"),
                        JsonTreeNode.Null, s.StatusValue(draft),
                        draft.Flag("isAiCreated") ? "An AI client drafted this for you. It goes nowhere until it is submitted." : "Your draft is not submitted yet.",
                        s.RecordUrl(entity, draft.Id), entity == "business-application" ? "save_application_draft" : "submit_record", draft)));
                }
            }

            // Data uses, mappings and locations drafted for the person (an AI client's rows start as drafts, G-21): a person
            // confirms each on its page (data taxonomy slice 3).
            foreach (string entity in new[] { "data-use", "data-mapping", "data-location" })
            {
                foreach (StoredRow draft in s.Query(entity, ("createdByPrincipal", s.PersonId), ("statusMeaning", "draft")).Where(OpenOn(s)))
                {
                    string title = entity switch
                    {
                        "data-use" => DataSubjectLabel(s, entity, draft.Id, draft, draft.Text("businessApplication") is string a ? s.Rows.Get("business-application", a) : null),
                        "data-mapping" => DataSubjectLabel(s, entity, draft.Id, draft, null),
                        _ => draft.Text("labelAsSeen") ?? draft.Id,
                    };
                    items.Add((4, "~", Item(
                        "draft", entity, draft.Id, title, entity == "data-use" ? draft.Text("businessApplication") : null, JsonTreeNode.Null, s.StatusValue(draft),
                        draft.Flag("isAiCreated") ? "An AI client drafted this for you. Confirm it on its page; an AI client never confirms data rows." : "Your draft is not confirmed yet.",
                        s.RecordUrl(entity, draft.Id), entity == "data-use" ? "save_data_use" : entity == "data-mapping" ? "save_data_mapping" : "save_data_location", draft)));
                }
            }

            // A decision the person proposed (or an AI client proposed for them) that is not submitted: no approval request
            // exists yet, so nobody is asked until it is submitted.
            foreach (StoredRow decision in s.Query("decision", ("createdByPrincipal", s.PersonId), ("statusMeaning", "proposed")))
            {
                if (s.Query("approval-record", ("decision", decision.Id)).Count > 0)
                {
                    continue;
                }

                items.Add((4, "~", Item(
                    "draft", "decision", decision.Id, decision.Text("statement") ?? "Decision " + decision.Id, decision.Text("subjectApplication"),
                    JsonTreeNode.Null, s.StatusValue(decision),
                    decision.Flag("isAiCreated") ? "An AI client proposed this decision for you. Nobody is asked until it is submitted." : "You proposed this decision. Nobody is asked until it is submitted.",
                    s.RecordUrl("decision", decision.Id), "submit_record", decision)));
            }
        }

        var ordered = items.OrderBy(i => i.Order).ThenBy(i => i.Due, StringComparer.Ordinal).ThenBy(i => Js.Text(i.Item, "subjectId"), StringComparer.Ordinal).ThenBy(i => Js.Text(i.Item, "title"), StringComparer.Ordinal).Select(i => i.Item).ToList();
        List<JsonTreeNode> page = s.Page(ordered, out string? next);
        return s.Answer(Js.Obj(("items", Arr(page)), ("skipToken", Js.OptStr(next))), ordered.Count == 0 ? "Nothing needs you right now." : ordered.Count + " items need you.");
    }

    // Loads ahead what get_my_queue reads many times (document 25, B25-23), in a few store calls whatever the size of the
    // estate: the people behind the stamps and the portfolios; the decisions the pending approval records and the due
    // reviews name, with the intake and change requests they are about; then, for every application one of those rows or
    // the person's own owner assignments name, the application, its owners and its document links; and the intake
    // requests and approval records of the person's drafts. The items are built afterwards exactly as before.
    private static void PrefetchMyQueue(ToolScope s, Func<string, bool> want)
    {
        if (!s.Call.ReadsAreCached)
        {
            return;
        }

        s.Call.PrefetchAll("principal-reference");
        s.Call.PrefetchAll("portfolio");
        IReadOnlyList<StoredRow> none = Array.Empty<StoredRow>();
        IReadOnlyList<StoredRow> pending = want("approval") ? s.Query("approval-record", ("outcomeMeaning", "pending")) : none;
        IReadOnlyList<StoredRow> due = want("review") ? s.Query("review", ("statusMeaning", "due")) : none;
        s.Call.PrefetchIds("decision", pending.Select(r => r.Text("decision")).Concat(due.Select(r => r.Text("outcomeDecision"))));
        IReadOnlyList<StoredRow> decisions = s.Rows.GetMany("decision", pending.Select(r => r.Text("decision")));

        // The answer check of each request that asks the person reads its decision's records (wave 4b verify round): one key set
        // read for them all, not one a request.
        s.Call.Prefetch("approval-record", "decision", pending.Select(r => r.Text("decision")));
        s.Call.PrefetchIds("intake-request", decisions.Select(d => d.Text("subjectIntakeRequest") ?? d.Text("intakeRequest")));
        s.Call.PrefetchIds("change-request", decisions.Select(d => d.Text("subjectChangeRequest") ?? d.Text("changeRequest")));

        List<string?> mine = s.Query("owner-assignment", ("principal", s.PersonId)).Select(o => o.Text("businessApplication")).ToList();
        var apps = mine.Concat(decisions.Select(d => ApplicationOf(s, d))).Concat(due.Select(r => r.Text("businessApplication"))).ToList();
        s.Call.Prefetch("owner-assignment", "businessApplication", apps);
        s.Call.PrefetchIds("business-application", apps);
        if (want("document"))
        {
            s.Call.Prefetch("document-link", "businessApplication", mine);
        }

        if (want("draft"))
        {
            s.Call.Prefetch("intake-request", "businessApplication", s.Query("business-application", ("createdByPrincipal", s.PersonId), ("statusMeaning", "draft")).Select(a => a.Id));
            s.Call.Prefetch("approval-record", "decision", s.Query("decision", ("createdByPrincipal", s.PersonId), ("statusMeaning", "proposed")).Select(d => d.Id));
        }
    }

    // A document is the person's to keep (document 23, P07: the Document Owner, by default the technical owner): a link's
    // Document Owner; for a missing document, or a link that names no owner, the application's technical owner, or any of
    // its owners when no technical owner holds now.
    private static bool DocumentIsMine(ToolScope s, string? applicationId, StoredRow? link)
    {
        if (link?.Text("documentOwner") is string owner)
        {
            return Same(owner, s.PersonId);
        }

        if (applicationId == null)
        {
            return false;
        }

        IReadOnlyList<StoredRow> owners = s.CurrentOwners(applicationId);
        bool technical = owners.Any(o => o.Text("ownerRole") == "technical-owner");
        return owners.Any(o => Same(o.Text("principal"), s.PersonId) && (!technical || o.Text("ownerRole") == "technical-owner"));
    }

    // A document link's Stale On as text: the type's freshness from the last check, else the last update (OP-06's rule),
    // else the stored value.
    private static string? StaleOnText(ToolScope s, SnapshotRow? type, StoredRow link)
    {
        JsonTreeNode computed = type == null ? JsonTreeNode.Null : StaleOn(s, type, link);
        return computed.Kind == JsonTreeKind.String ? computed.GetString() : link.Text("staleOn");
    }

    // The data gaps that are the person's (docs/architecture/data-taxonomy.md, Data checks, "who sees it first"): findings that name them, such
    // as the domains they own; and for the platform admin and the data governance lead also the findings nobody owns yet
    // (DC-01, DC-06, DC-12, DC-14). Empty while the install has no data domain and no data row.
    private static List<JsonTreeNode> MyDataFindings(ToolScope s)
    {
        var tree = new Product.Engine.Core.Operations.DataTree(s.Snapshot);
        if (!HasDataTaxonomy(s, tree))
        {
            return new List<JsonTreeNode>();
        }

        bool steward = s.HoldsRole("platform-admin") || s.HoldsRole("data-governance-lead");
        List<JsonTreeNode> all = DataFindings(s, tree, steward ? null : Js.Obj(("principalId", JsonTreeNode.FromString(s.PersonId))));
        return all.Where(f => Same(PathText(f, "principal.objectId"), s.PersonId)
                || (steward && Js.Member(f, "principal") is JsonTreeNode p && p.Kind == JsonTreeKind.Null && Js.Text(f, "check") is "DC-01" or "DC-06" or "DC-12" or "DC-14"))
            .ToList();
    }

    // True when the install uses the data taxonomy: data-taxonomy-mode is not off, and a domain or a data row exists.
    private static bool HasDataTaxonomy(ToolScope s, Product.Engine.Core.Operations.DataTree tree)
    {
        return tree.Mode != DataTaxonomyMode.Off
            && (tree.Domains.Any() || s.Rows.Query(RowQuery.For("data-use")).Any() || s.Rows.Query(RowQuery.For("data-location")).Any());
    }

    // An application's data owners, derived from its open data uses (docs/architecture/data-taxonomy.md, Roles on data; question 3): the
    // effective data owner of each domain it uses, with "inherited from" and the uses that reach it.
    private static IEnumerable<JsonTreeNode> DerivedDataOwners(ToolScope s, string applicationId)
    {
        var tree = new Product.Engine.Core.Operations.DataTree(s.Snapshot);
        var uses = s.Query("data-use", ("businessApplication", applicationId)).Where(OpenOn(s)).Where(u => u.Text("dataDomain") is string k && tree.Domain(k) != null).ToList();
        List<string> domains = uses.Select(u => u.Text("dataDomain")!).Distinct(StringComparer.Ordinal).OrderBy(k => tree.PathText(k), StringComparer.OrdinalIgnoreCase).ToList();
        if (domains.Count == 0)
        {
            yield break;
        }

        IReadOnlyList<EvaluationResult<JsonTreeNode?>> resolved = s.Call.DataDomains(domains);
        for (int i = 0; i < domains.Count; i++)
        {
            JsonTreeNode owner = OwnerValue(s, tree, resolved[i].Payload, EngineKeys.DataOwnerRole);
            yield return Js.Obj(
                ("domain", DomainRef(tree, domains[i])),
                ("principal", owner.Kind == JsonTreeKind.Null ? JsonTreeNode.Null : Js.Member(owner, "principal")),
                ("inheritedFrom", owner.Kind == JsonTreeKind.Null ? JsonTreeNode.Null : Js.Member(owner, "inheritedFrom")),
                ("dataUseIds", Ids(uses.Where(u => u.Text("dataDomain") == domains[i]).Select(u => u.Id).OrderBy(u => u, StringComparer.Ordinal))));
        }
    }

    // The Data panel of Application 360 (docs/architecture/data-taxonomy.md, Screens): the open uses with their state, the derived data
    // owners, the findings about the application, the highest classification it uses, and the summary line.
    private static JsonTreeNode ApplicationDataSection(ToolScope s, StoredRow app)
    {
        var tree = new Product.Engine.Core.Operations.DataTree(s.Snapshot);
        var uses = s.Query("data-use", ("businessApplication", app.Id)).Where(OpenOn(s)).OrderBy(u => u.Text("recordNumber") ?? u.Id, StringComparer.Ordinal).ToList();
        List<JsonTreeNode> items = uses.Select(u => UseItem(s, tree, u)).ToList();
        List<JsonTreeNode> findings = HasDataTaxonomy(s, tree) ? DataFindings(s, tree, Js.Obj(("applicationId", JsonTreeNode.FromString(app.Id)))) : new List<JsonTreeNode>();
        Product.Engine.Core.Operations.EffectiveClassification? highest = uses.Select(u => u.Text("dataDomain")).Where(k => k != null && tree.Domain(k) != null)
            .Select(k => tree.Classification(k!)).Where(c => c != null).OrderByDescending(c => c!.Rank).FirstOrDefault();
        int reads = uses.Where(u => u.Text("access") == "read").Select(u => u.Text("dataDomain")).Distinct(StringComparer.Ordinal).Count();
        int writes = uses.Where(u => Writes(u.Text("access"))).Select(u => u.Text("dataDomain")).Distinct(StringComparer.Ordinal).Count();
        string summary = uses.Count == 0
            ? (app.Flag("holdsNoBusinessData") ? AppName(app) + " holds no business data." : AppName(app) + " declares no data use yet.")
            : "Reads " + Count(reads, "domain", "domains") + ", writes " + Count(writes, "domain", "domains") + "." + (highest == null ? string.Empty : " Highest class: " + s.Label("data-classification", highest.Key) + ".")
                + (findings.Any(f => Js.Text(f, "check") == "DC-09") ? " The sensitivity answer is below the data." : string.Empty);
        return Js.Obj(
            ("summary", JsonTreeNode.FromString(summary)),
            ("holdsNoBusinessData", app.Column("holdsNoBusinessData") is JsonTreeNode h && (h.Kind == JsonTreeKind.True || h.Kind == JsonTreeKind.False) ? h : JsonTreeNode.Null),
            ("highestClassification", highest == null ? JsonTreeNode.Null : s.LabelledKey("data-classification", highest.Key)),
            ("uses", Arr(items)),
            ("dataOwners", Arr(DerivedDataOwners(s, app.Id))),
            ("findings", Arr(findings)));
    }

    // The data gap counts of the portfolio summary: findings per check, for every finding, or for those about the
    // portfolio's applications when a portfolio is named. Zero while the install has no data taxonomy.
    private static JsonTreeNode PortfolioDataGaps(ToolScope s, HashSet<string>? appIds)
    {
        var tree = new Product.Engine.Core.Operations.DataTree(s.Snapshot);
        List<JsonTreeNode> findings = HasDataTaxonomy(s, tree) ? DataFindings(s, tree, null) : new List<JsonTreeNode>();
        if (appIds != null)
        {
            findings = findings.Where(f => Js.Text(f, "businessApplicationId") is string a && appIds.Contains(a)).ToList();
        }

        return Js.Obj(
            ("total", JsonTreeNode.FromInt64(findings.Count)),
            ("high", JsonTreeNode.FromInt64(findings.Count(f => Js.Text(f, "severity") == "high"))),
            ("counts", Js.Obj(DataChecksResult.Checks.Select(c => new KeyValuePair<string, JsonTreeNode>(c, JsonTreeNode.FromInt64(findings.Count(f => Js.Text(f, "check") == c)))))));
    }

    // ---- 3. get_role_holders ----

    private static JsonTreeNode GetRoleHolders(ToolScope s)
    {
        SnapshotRow role = s.RequireKey("role", s.Text("roleKey"), "roleKey");
        if (Js.Flag(role.Columns, "isWorkloadRole") == true)
        {
            throw s.Invalid("roleKey", "A workload role has no group; its holders are Workload Grants. Use check_role_bindings.");
        }

        IDirectoryReader directory = s.Options.Directory ?? throw s.Upstream("Microsoft Graph", "this install has no connection to the directory");
        var groups = new List<JsonTreeNode>();
        var members = new SortedDictionary<string, JsonTreeNode>(StringComparer.OrdinalIgnoreCase);
        var notPeople = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (SnapshotRow binding in s.ActiveRows("role-group-binding").Where(b => Js.Text(b.Columns, "roleKey") == role.KeyPath))
        {
            string group = Js.Text(binding.Columns, "entraGroupObjectId")!;
            DirectoryAnswer<DirectoryObject?> groupInfo = directory.Group(s.Session.Caller, group);
            DirectoryAnswer<IReadOnlyList<DirectoryObject>> answer = directory.GroupMembers(s.Session.Caller, group);
            if (!groupInfo.Answered || !answer.Answered)
            {
                throw s.Upstream("Microsoft Graph", groupInfo.Problem ?? answer.Problem);
            }

            groups.Add(Js.Obj(("objectId", JsonTreeNode.FromString(group)), ("displayLabel", JsonTreeNode.FromString(GroupLabel(s, directory, role.KeyPath, groupInfo.Value, group)))));
            foreach (DirectoryObject member in answer.Value)
            {
                members[member.ObjectId] = Js.Obj(("objectId", JsonTreeNode.FromString(member.ObjectId)), ("displayLabel", JsonTreeNode.FromString(member.DisplayLabel)));
                if (!string.Equals(member.PrincipalKind, "user", StringComparison.Ordinal))
                {
                    notPeople.Add(member.ObjectId);
                }
            }
        }

        // members lists every member Graph shows, as the contract says. Only people hold a product role through a group:
        // an agent user or agent identity holds none (RA-06), a workload holds a role only through a Workload Grant, and a
        // nested group is a group, not a person. The summary counts people only, and the note names the others.
        int people = members.Keys.Count(id => !notPeople.Contains(id));
        string? note = groups.Count == 0
            ? "No Entra group is bound to this role in the configuration in force."
            : notPeople.Count == 0
            ? null
            : notPeople.Count + (notPeople.Count == 1 ? " listed member is" : " listed members are") + " not a person (an agent user, an agent identity, a service principal or a nested group) and holds no product role through this binding. A nested group's own members are not listed.";
        // A demo install or a local run reads the holders from the demo personas, never from Entra, and says so.
        bool demo = directory is IDemoDirectory;
        JsonTreeNode data = Js.Obj(
            ("role", s.LabelledKey("role", role.KeyPath)),
            ("groups", Arr(groups)),
            ("members", Arr(members.Values)),
            ("readFrom", JsonTreeNode.FromString(demo ? "Demo personas" : "Entra, live")),
            ("note", note == null ? JsonTreeNode.Null : JsonTreeNode.FromString(note)));
        return s.Answer(data, people + (people == 1 ? " person holds " : " people hold ") + s.Label("role", role.KeyPath) + " through " + Count(groups.Count, "group", "groups") + ", " + (demo ? "read from the demo personas." : "read live from Entra.") + (notPeople.Count == 0 ? string.Empty : " " + Count(notPeople.Count, "other member is not a person.", "other members are not people.")));
    }

    // ---- 4. find_applications ----

    private static JsonTreeNode FindApplications(ToolScope s)
    {
        string? text = s.Text("text")?.ToLowerInvariant();
        IReadOnlyList<string> kinds = s.Strings("applicationKindKeys");
        IReadOnlyList<string> tiers = s.Strings("tierKeys");
        IReadOnlyList<string> stages = s.Strings("lifecycleStageKeys");
        IReadOnlyList<string> meanings = Js.Has(s.Args, "registrationMeanings") ? s.Strings("registrationMeanings") : new[] { "registered" };
        string? portfolio = s.Text("portfolioId");
        string? owner = s.Text("ownerObjectId");
        bool? usesAi = s.Flag("usesAi");
        IReadOnlyList<string> buildPaths = s.Strings("buildPathKeys");

        // Loaded ahead (document 25, B25-23): every application in one store call, the portfolios for the portfolio
        // filter, and the owners of every application for the owner filter, so the filters cost no store call each.
        IReadOnlyList<StoredRow> all = s.Rows.Query(RowQuery.For("business-application"));
        if (portfolio != null)
        {
            s.Call.PrefetchAll("portfolio");
        }

        if (owner != null)
        {
            s.Call.Prefetch("owner-assignment", "businessApplication", all.Select(a => a.Id));
        }

        var matches = new List<StoredRow>();
        foreach (StoredRow app in all)
        {
            if (!meanings.Contains(app.Text("statusMeaning") ?? string.Empty, StringComparer.Ordinal)
                || (kinds.Count > 0 && !kinds.Contains(app.Text("applicationKind") ?? string.Empty, StringComparer.Ordinal))
                || (tiers.Count > 0 && !tiers.Contains(app.Text("tier") ?? string.Empty, StringComparer.Ordinal))
                || (stages.Count > 0 && !stages.Contains(app.Text("lifecycleStage") ?? string.Empty, StringComparer.Ordinal))
                || (usesAi.HasValue && (app.Column("usesAi")?.Kind ?? JsonTreeKind.Null) != (usesAi.Value ? JsonTreeKind.True : JsonTreeKind.False))
                || (buildPaths.Count > 0 && !buildPaths.Contains(app.Text("buildPath") ?? string.Empty, StringComparer.Ordinal))
                || (portfolio != null && !PortfolioPath(s, app.Text("portfolio")).Contains(portfolio, StringComparer.Ordinal))
                || (owner != null && !s.CurrentOwners(app.Id).Any(o => Same(o.Text("principal"), owner))))
            {
                continue;
            }

            if (text != null && !(AppName(app) + " " + (app.Text("purpose") ?? string.Empty) + " " + (app.Text("recordNumber") ?? string.Empty)).ToLowerInvariant().Contains(text))
            {
                continue;
            }

            matches.Add(app);
        }

        var ordered = matches.OrderBy(a => AppName(a), StringComparer.OrdinalIgnoreCase).ThenBy(a => a.Id, StringComparer.Ordinal).ToList();
        List<StoredRow> page = s.Page(ordered, out string? next);

        // The page's owners and the people its owner fields and stamps name, in two store calls.
        s.Call.Prefetch("owner-assignment", "businessApplication", page.Select(a => a.Id));
        s.Call.PrefetchAll("principal-reference");
        JsonTreeNode items = Arr(page.Select(app =>
        {
            IReadOnlyList<StoredRow> owners = s.CurrentOwners(app.Id);
            return Js.Obj(
                ("businessApplicationId", JsonTreeNode.FromString(app.Id)),
                ("name", JsonTreeNode.FromString(AppName(app))),
                ("purpose", JsonTreeNode.FromString(app.Text("purpose") ?? string.Empty)),
                ("kind", s.LabelledKey("application-kind", app.Text("applicationKind") ?? "unknown")),
                ("tier", s.LabelledKey("tier", app.Text("tier"))),
                ("stage", s.LabelledKey("lifecycle-stage", app.Text("lifecycleStage"))),
                ("businessOwner", OwnerRef(s, owners, "business-owner")),
                ("technicalOwner", OwnerRef(s, owners, "technical-owner")),
                ("isAiCreated", AiCreated(app)),
                ("performedBy", Stamp(s, app)),
                ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("business-application", app.Id))));
        }));
        return s.Answer(Js.Obj(("items", items), ("totalCount", JsonTreeNode.FromInt64(ordered.Count)), ("skipToken", Js.OptStr(next))), ordered.Count + " applications match.");
    }

    // ---- 5. get_application_owners ----

    private static JsonTreeNode GetApplicationOwners(ToolScope s)
    {
        StoredRow app = RequireApplication(s);
        DateTime? asOf = Js.ParseMoment(s.Text("asOf"));
        bool deputies = s.Flag("includeDeputies") ?? true;
        IReadOnlyList<StoredRow> owners = s.CurrentOwners(app.Id, asOf);
        var components = s.Query("technical-component", ("businessApplication", app.Id)).Where(c => c.Text("retiredOn") == null).ToList();
        var checkedOn = components.Select(c => Js.ParseMoment(c.Text("ownerCheckedOn"))).Where(d => d.HasValue).Select(d => d!.Value).ToList();
        JsonTreeNode data = Js.Obj(
            ("owners", Arr(owners.Where(o => deputies || !o.Flag("isDeputy")).Select(o => OwnerAssignment(s, o)))),
            ("gaps", Arr(OwnerGaps(s, app, owners).Select(g => Js.Obj(("ownerRoleKey", JsonTreeNode.FromString(g.Role)), ("missing", JsonTreeNode.FromString(g.Missing)))))),
            ("microsoftOwnerDiffers", checkedOn.Count == 0 ? JsonTreeNode.Null : JsonTreeNode.FromBoolean(components.Any(c => c.Flag("ownerDiffers")))),
            ("ownerCheckedOn", checkedOn.Count == 0 ? JsonTreeNode.Null : Js.Utc(checkedOn.Max())),
            ("dataOwners", Arr(DerivedDataOwners(s, app.Id))));
        return s.Answer(data, AppName(app) + " has " + owners.Count + " owner assignments.", s.RecordUrl("business-application", app.Id), ToolScope.Next("propose_owner_change", ("businessApplicationId", JsonTreeNode.FromString(app.Id))));
    }

    // ---- 6. get_application_card ----

    private static JsonTreeNode GetApplicationCard(ToolScope s)
    {
        StoredRow app = RequireApplication(s);
        var sections = new HashSet<string>(s.Strings("sections"), StringComparer.Ordinal);
        bool Want(string section) => sections.Count == 0 || sections.Contains(section);
        string? tier = app.Text("tier");
        string? stage = app.Text("lifecycleStage");
        var members = new List<(string, JsonTreeNode?)>
        {
            ("application", Js.Obj(
                ("businessApplicationId", JsonTreeNode.FromString(app.Id)),
                ("name", JsonTreeNode.FromString(AppName(app))),
                ("purpose", JsonTreeNode.FromString(app.Text("purpose") ?? string.Empty)),
                ("status", s.StatusValue(app) is JsonTreeNode st && st.Kind != JsonTreeKind.Null ? st : ToolScope.StatusValue("draft", null)),
                ("kind", s.LabelledKey("application-kind", app.Text("applicationKind") ?? "unknown")),
                ("tier", tier == null ? JsonTreeNode.Null : Js.Obj(("key", JsonTreeNode.FromString(tier)), ("label", JsonTreeNode.FromString(s.Label("tier", tier))), ("decisionId", Js.Str(app.Text("tierDecision"))))),
                ("stage", stage == null ? JsonTreeNode.Null : Js.Obj(
                    ("key", JsonTreeNode.FromString(stage)),
                    ("label", JsonTreeNode.FromString(s.Label("lifecycle-stage", stage))),
                    ("since", ToolScope.MomentOrNull(app.Text("stageSince"))),
                    ("decisionId", Js.Str(app.Text("stageDecision"))))),
                ("buildPath", ListValue(s, "build-path", app.Text("buildPath"))),
                ("usesAi", app.Column("usesAi") is JsonTreeNode ai && (ai.Kind == JsonTreeKind.True || ai.Kind == JsonTreeKind.False) ? ai : JsonTreeNode.Null),
                ("agreedAutonomyLevel", ListValue(s, "autonomy-level", app.Text("agreedAutonomyLevel"))),
                ("nextReviewOn", ToolScope.DateOrNull(app.Text("nextReviewOn"))),
                ("portfolio", Arr(PortfolioPath(s, app.Text("portfolio")).AsEnumerable().Reverse().Select(id => Js.Obj(("portfolioId", JsonTreeNode.FromString(id)), ("name", JsonTreeNode.FromString(s.Rows.Get("portfolio", id)?.Text("name") ?? id)))))),
                ("isAiCreated", AiCreated(app)),
                ("performedBy", Stamp(s, app)))),
        };

        if (Want("owners"))
        {
            members.Add(("owners", Arr(s.CurrentOwners(app.Id).Select(o => OwnerAssignment(s, o)))));
        }

        if (Want("components"))
        {
            members.Add(("components", Arr(s.Query("technical-component", ("businessApplication", app.Id))
                .Where(c => c.Text("retiredOn") == null)
                .Select(c => Js.Obj(
                    ("technicalComponentId", JsonTreeNode.FromString(c.Id)),
                    ("kind", s.LabelledKey("component-kind", c.Text("componentKind") ?? "unknown")),
                    ("externalId", JsonTreeNode.FromString(c.Text("externalId") ?? string.Empty)),
                    ("url", (c.Text("url") ?? string.Empty).StartsWith("https://", StringComparison.Ordinal) ? JsonTreeNode.FromString(c.Text("url")!) : JsonTreeNode.Null),
                    ("labelAsSeen", Js.Str(c.Text("labelAsSeen"))),
                    ("lastSeenOn", ToolScope.MomentOrNull(c.Text("lastSeenOn"))),
                    // An open vanished claim names the component: the last gap check no longer saw it. Any reader of the
                    // card sees this fact; the claim itself follows claim-visibility (QA 2026-11-24: the retirement
                    // tracker said "still seen" to the PMO and "vanished" to the platform admin).
                    ("vanishedInGapCheck", JsonTreeNode.FromBoolean(s.Query("claim", ("registeredComponent", c.Id)).Any(k => k.Text("findingKind") == "vanished" && k.Text("statusMeaning") == "open"))))))));
        }

        if (Want("dependencies"))
        {
            var dependsOn = s.Query("dependency", ("fromApplication", app.Id)).Where(d => d.Text("endedOn") == null).ToList();
            var usedBy = s.Query("dependency", ("toApplication", app.Id)).Where(d => d.Text("endedOn") == null).ToList();
            IEnumerable<JsonTreeNode> top = dependsOn.Select(d => (Dir: "depends-on", Row: d)).Concat(usedBy.Select(d => (Dir: "used-by", Row: d))).Take(10).Select(e =>
            {
                string? otherApp = e.Dir == "depends-on" ? e.Row.Text("toApplication") : e.Row.Text("fromApplication");
                string? component = e.Dir == "depends-on" ? e.Row.Text("toComponent") : null;
                StoredRow? other = otherApp == null ? null : s.Rows.Get("business-application", otherApp);
                StoredRow? comp = component == null ? null : s.Rows.Get("technical-component", component);
                return Js.Obj(
                    ("direction", JsonTreeNode.FromString(e.Dir)),
                    ("businessApplicationId", Js.Str(otherApp)),
                    ("technicalComponentId", Js.Str(component)),
                    ("name", JsonTreeNode.FromString(other != null ? AppName(other) : comp?.Text("labelAsSeen") ?? otherApp ?? component ?? "unknown")),
                    ("dependencyType", s.LabelledKey("dependency-type", e.Row.Text("dependencyType") ?? "unknown")),
                    ("criticality", ListValue(s, "criticality", e.Row.Text("criticality"))));
            });
            members.Add(("dependencies", Js.Obj(("dependsOnCount", JsonTreeNode.FromInt64(dependsOn.Count)), ("usedByCount", JsonTreeNode.FromInt64(usedBy.Count)), ("top", Arr(top)))));
        }

        if (Want("decisions"))
        {
            members.Add(("decisions", Arr(s.Query("decision", ("subjectApplication", app.Id))
                .OrderByDescending(d => d.Text("decidedOn") ?? d.Text("engineCheckedOn") ?? string.Empty, StringComparer.Ordinal).ThenByDescending(d => d.Id, StringComparer.Ordinal).Take(5)
                .Select(d => Js.Obj(
                    ("decisionId", JsonTreeNode.FromString(d.Id)),
                    ("decisionType", s.LabelledKey("decision-type", d.Text("decisionType") ?? "unknown")),
                    ("statement", JsonTreeNode.FromString(d.Text("statement") ?? string.Empty)),
                    ("outcome", d.Text("outcome") == null ? JsonTreeNode.Null : Js.Obj(("key", JsonTreeNode.FromString(d.Text("outcome")!)), ("label", JsonTreeNode.FromString(s.Label("decision-outcome", d.Text("decisionType") + "/" + d.Text("outcome")))))),
                    ("status", s.StatusValue(d)),
                    ("decidedOn", ToolScope.MomentOrNull(d.Text("decidedOn"))),
                    ("isAiCreated", AiCreated(d)),
                    ("performedBy", Stamp(s, d)))))));
        }

        if (Want("changes"))
        {
            members.Add(("changes", Arr(s.Query("change-request", ("businessApplication", app.Id)).Where(IsOpen).Select(c => Js.Obj(
                ("changeRequestId", JsonTreeNode.FromString(c.Id)),
                ("title", JsonTreeNode.FromString(c.Text("title") ?? string.Empty)),
                ("changeType", s.LabelledKey("change-type", c.Text("changeType") ?? "unknown")),
                ("status", s.StatusValue(c)),
                ("windowStart", ToolScope.MomentOrNull(c.Text("windowStart"))),
                ("windowEnd", ToolScope.MomentOrNull(c.Text("windowEnd"))),
                ("isAiCreated", AiCreated(c)),
                ("performedBy", Stamp(s, c)))))));
        }

        if (Want("deployments"))
        {
            members.Add(("deployments", Arr(LatestPerTarget(s.Query("deployment-record", ("businessApplication", app.Id))).Select(d =>
            {
                StoredRow? target = s.Rows.Get("deployment-target", d.Text("deploymentTarget") ?? string.Empty);
                StoredRow? artifact = s.Rows.Get("artifact-version", d.Text("artifactVersion") ?? string.Empty);
                return Js.Obj(
                    ("deploymentRecordId", JsonTreeNode.FromString(d.Id)),
                    ("deploymentTargetId", JsonTreeNode.FromString(d.Text("deploymentTarget") ?? string.Empty)),
                    ("targetLabel", JsonTreeNode.FromString(target?.Text("labelAsSeen") ?? string.Empty)),
                    ("targetStageKey", JsonTreeNode.FromString(target?.Text("targetStage") ?? "unknown")),
                    ("artifactName", JsonTreeNode.FromString(artifact?.Text("artifactName") ?? string.Empty)),
                    ("artifactVersion", JsonTreeNode.FromString(artifact?.Text("version") ?? string.Empty)),
                    ("result", s.StatusValue(d, "result", "resultMeaning")),
                    ("finishedOn", ToolScope.MomentOrNull(d.Text("finishedOn"))),
                    ("route", Route(s, d)),
                    ("flags", RecordFlags(s, d, null)),
                    ("gateLevelApplied", JsonTreeNode.FromString(d.Text("gateLevelApplied") ?? "record")),
                    ("isAiCreated", AiCreated(d)),
                    ("performedBy", Stamp(s, d)));
            }))));
        }

        if (Want("documents"))
        {
            members.Add(("documents", Arr(DocumentStatus(s, app).Select(d => d.Card))));
        }

        if (Want("reviews"))
        {
            members.Add(("reviews", Arr(s.Query("review", ("businessApplication", app.Id)).Select(r => Js.Obj(
                ("reviewId", JsonTreeNode.FromString(r.Id)),
                ("reviewType", s.LabelledKey("review-type", r.Text("reviewType") ?? "unknown")),
                ("status", s.StatusValue(r)),
                ("dueOn", ToolScope.DateOrNull(r.Text("dueOn"))),
                ("doneOn", ToolScope.DateOrNull(r.Text("doneOn"))))))));
        }

        if (Want("data"))
        {
            members.Add(("data", ApplicationDataSection(s, app)));
        }

        if (Want("links"))
        {
            members.Add(("links", Arr(s.Query("delivery-link", ("subjectApplication", app.Id)).Where(l => (l.Text("url") ?? string.Empty).StartsWith("https://", StringComparison.Ordinal)).Select(l => Js.Obj(
                ("deliveryLinkId", JsonTreeNode.FromString(l.Id)),
                ("linkKind", s.LabelledKey("link-kind", l.Text("linkKind") ?? "unknown")),
                ("url", JsonTreeNode.FromString(l.Text("url")!)),
                ("label", JsonTreeNode.FromString(l.Text("label") ?? string.Empty)),
                ("lastSeenOn", ToolScope.MomentOrNull(l.Text("lastSeenOn"))))))));
        }

        return s.Answer(Js.Obj(members.ToArray()), "The card of " + AppName(app) + ".", s.RecordUrl("business-application", app.Id));
    }

    private static IEnumerable<StoredRow> LatestPerTarget(IEnumerable<StoredRow> records)
    {
        return records
            .GroupBy(r => r.Text("deploymentTarget") ?? string.Empty, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(r => r.Text("finishedOn") ?? string.Empty, StringComparer.Ordinal).ThenByDescending(r => r.Id, StringComparer.Ordinal).First())
            .OrderBy(r => r.Text("deploymentTarget"), StringComparer.Ordinal);
    }

    // ---- 7. get_dependency_impact (OP-10) ----

    // Choices (document 24, B24-22):
    // - The walk starts at an application, a technical component or a data location. From a component, the first ring is
    //   its own application (via part-of: the application is built from it), the applications with a dependency on it, and
    //   the applications whose open data uses name it as their AI component (via ai-component). From a data location, the
    //   first ring is the applications whose open data uses name the location or a location inside it (via data-use), and
    //   the application the location is (Hosted By Application, via hosts-location). From that ring OP-10 walks the
    //   dependants on, one hop less, so "what breaks if it retires?" has one answer for each kind of start.
    // - A component and a location depend on nothing the register holds, so their dependencies walk is empty.
    // - A node reached through a data use or part of carries no dependency type (dependencyTypeKey null) and says how it
    //   joined the walk (via). A location start also lists the data held there (its mappings) and the locations inside it.
    private static JsonTreeNode GetDependencyImpact(ToolScope s)
    {
        string? appId = s.Text("businessApplicationId");
        string? componentId = s.Text("technicalComponentId");
        string? locationId = s.Text("dataLocationId");
        StoredRow? rootApp = appId == null ? null : RequireApplication(s);
        StoredRow? rootComponent = componentId == null ? null : s.RequireRow("technical-component", componentId, "technicalComponentId");
        StoredRow? rootLocation = locationId == null ? null : s.RequireRow("data-location", locationId, "dataLocationId");
        IReadOnlyList<string> typeFilter = s.Strings("dependencyTypeKeys");
        bool includeRetired = s.Flag("includeRetired") ?? false;
        string direction = s.Text("direction") ?? "dependants";
        long depth = Js.Number(s.Args, "depth") ?? 3;
        long? maxSetting = s.SettingNumber("impact-max-depth");
        if (maxSetting.HasValue && depth > maxSetting.Value)
        {
            s.Call.Warn("impact.depth-capped", "depth " + depth + " is above the impact-max-depth setting, so the walk stops at " + maxSetting.Value + ".", "Setting", "impact-max-depth");
            depth = maxSetting.Value;
        }

        depth = Math.Max(1, depth);

        // Only dependency types with Counts For Impact = yes are followed (document 20, C17).
        var counted = new HashSet<string>(s.ActiveRows("dependency-type").Where(t => Js.Flag(t.Columns, "countsForImpact") == true).Select(t => t.KeyPath), StringComparer.Ordinal);
        var edgeRows = s.Rows.Query(RowQuery.For("dependency"))
            .Where(d => d.Text("endedOn") == null && d.Text("fromApplication") != null)
            .Where(d => counted.Contains(d.Text("dependencyType") ?? string.Empty) && (typeFilter.Count == 0 || typeFilter.Contains(d.Text("dependencyType") ?? string.Empty, StringComparer.Ordinal)))
            .Where(d => includeRetired || (!Retired(s, d.Text("fromApplication")) && !Retired(s, d.Text("toApplication"))))
            .ToList();

        var reached = new Dictionary<string, ImpactReach>(StringComparer.OrdinalIgnoreCase);
        void Keep(ImpactReach reach)
        {
            if (!reached.TryGetValue(reach.AppId, out ImpactReach? known) || reach.Depth < known.Depth)
            {
                reached[reach.AppId] = reach;
            }
        }

        bool truncated = false;
        JsonTreeNode locationFacts = JsonTreeNode.Null;
        if (rootLocation == null)
        {
            JsonTreeNode walk = WalkImpact(s, appId, componentId, direction, depth, edgeRows);
            truncated = Js.Flag(walk, "truncatedAtDepth") == true;
            foreach (JsonTreeNode node in Js.Items(walk, "reached"))
            {
                if (Js.Text(node, "applicationId") is string id)
                {
                    Keep(new ImpactReach(id, (int)(Js.Number(node, "depth") ?? 1), Js.Strings(node, "path").ToList(), Js.Strings(node, "dependencyTypeKeys").FirstOrDefault(), "dependency"));
                }
            }
        }

        // The first ring of a component or a location start, then the dependants of each, one hop less.
        var seeds = new List<(string AppId, string Via)>();
        if (rootComponent != null && direction != "dependencies")
        {
            if (rootComponent.Text("businessApplication") is string owner)
            {
                seeds.Add((owner, "part-of"));
            }

            foreach (StoredRow use in s.Query("data-use", ("aiComponent", rootComponent.Id)).Where(OpenOn(s)).Where(u => u.Text("businessApplication") != null).OrderBy(u => u.Id, StringComparer.Ordinal))
            {
                seeds.Add((use.Text("businessApplication")!, "ai-component"));
            }
        }

        if (rootLocation != null)
        {
            var places = new List<string> { rootLocation.Id };
            places.AddRange(s.Query("data-location", ("parentLocation", rootLocation.Id)).Select(l => l.Id));
            List<StoredRow> uses = places.SelectMany(p => s.Query("data-use", ("dataLocation", p))).Where(OpenOn(s)).Where(u => u.Text("businessApplication") != null)
                .OrderBy(u => u.Id, StringComparer.Ordinal).ToList();
            if (direction != "dependencies")
            {
                if (rootLocation.Text("hostedByApplication") is string host)
                {
                    seeds.Add((host, "hosts-location"));
                }

                seeds.AddRange(uses.Select(u => (u.Text("businessApplication")!, "data-use")));
            }

            var tree = new DataTree(s.Snapshot);
            var held = places.SelectMany(p => s.Query("data-mapping", ("dataLocation", p))).Where(OpenOn(s))
                .GroupBy(m => m.Text("dataDomain") ?? string.Empty, StringComparer.Ordinal).Where(g => g.Key.Length > 0).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => Js.Obj(
                    ("key", JsonTreeNode.FromString(g.Key)),
                    ("path", JsonTreeNode.FromString(tree.Domain(g.Key) == null ? g.Key : tree.PathText(g.Key))),
                    ("origins", Js.Strs(g.Select(m => m.Text("dataOrigin")).Where(o => o != null).Select(o => o!).Distinct(StringComparer.Ordinal).OrderBy(o => o, StringComparer.Ordinal)))))
                .ToList();
            locationFacts = Js.Obj(
                ("label", JsonTreeNode.FromString(rootLocation.Text("labelAsSeen") ?? rootLocation.Text("externalId") ?? rootLocation.Id)),
                ("platform", s.LabelledKey("data-platform", rootLocation.Text("dataPlatform"))),
                ("hostedBy", rootLocation.Text("hostedByApplication") is string hostId && s.Rows.Get("business-application", hostId) is StoredRow hostApp
                    ? Js.Obj(("businessApplicationId", JsonTreeNode.FromString(hostApp.Id)), ("name", JsonTreeNode.FromString(AppName(hostApp))))
                    : JsonTreeNode.Null),
                ("childLocationIds", Js.Strs(places.Skip(1).OrderBy(p => p, StringComparer.Ordinal))),
                ("domainsHeld", Arr(held)),
                ("dataUseCount", JsonTreeNode.FromInt64(uses.Count)));
        }

        string rootId = (componentId ?? locationId)!;
        foreach ((string seed, string via) in seeds.Distinct())
        {
            if (!includeRetired && Retired(s, seed))
            {
                continue;
            }

            Keep(new ImpactReach(seed, 1, new List<string> { rootId, seed }, null, via));
            if (depth > 1)
            {
                JsonTreeNode walk = WalkImpact(s, seed, null, "dependants", depth - 1, edgeRows);
                truncated |= Js.Flag(walk, "truncatedAtDepth") == true;
                foreach (JsonTreeNode node in Js.Items(walk, "reached"))
                {
                    if (Js.Text(node, "applicationId") is string id && !string.Equals(id, seed, StringComparison.OrdinalIgnoreCase))
                    {
                        var path = new List<string> { rootId };
                        path.AddRange(Js.Strings(node, "path"));
                        Keep(new ImpactReach(id, (int)(Js.Number(node, "depth") ?? 1) + 1, path, Js.Strings(node, "dependencyTypeKeys").FirstOrDefault(), "dependency"));
                    }
                }
            }
        }

        var byTier = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var byCriticality = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var nodes = new List<JsonTreeNode>();
        foreach (ImpactReach reach in reached.Values.ToList())
        {
            StoredRow? app = s.Rows.Get("business-application", reach.AppId);
            if (app == null || (appId != null && string.Equals(app.Id, appId, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            string? previous = reach.Path.Count >= 2 ? reach.Path[reach.Path.Count - 2] : null;
            StoredRow? edge = reach.Via != "dependency" ? null : edgeRows.FirstOrDefault(d => (Same(d.Text("fromApplication"), previous) && Same(d.Text("toApplication"), reach.AppId)) || (Same(d.Text("fromApplication"), reach.AppId) && Same(d.Text("toApplication"), previous))
                || (Same(d.Text("fromApplication"), reach.AppId) && Same(d.Text("toComponent"), previous)));
            string? criticality = edge?.Text("criticality");
            string tierKey = app.Text("tier") ?? string.Empty;
            byTier[tierKey] = byTier.TryGetValue(tierKey, out int t) ? t + 1 : 1;
            string critKey = criticality ?? string.Empty;
            byCriticality[critKey] = byCriticality.TryGetValue(critKey, out int c) ? c + 1 : 1;
            IReadOnlyList<StoredRow> owners = s.CurrentOwners(app.Id);
            string? typeKey = reach.Via == "dependency" ? reach.DependencyTypeKey ?? edge?.Text("dependencyType") ?? "unknown" : null;
            nodes.Add(Js.Obj(
                ("businessApplicationId", JsonTreeNode.FromString(app.Id)),
                ("name", JsonTreeNode.FromString(AppName(app))),
                ("tier", s.LabelledKey("tier", app.Text("tier"))),
                ("lifecycleMeaning", Js.Str(LifecycleMeaning(s, app.Text("lifecycleStage")))),
                ("businessOwner", OwnerRef(s, owners, "business-owner")),
                ("technicalOwner", OwnerRef(s, owners, "technical-owner")),
                ("dependencyTypeKey", Js.Str(typeKey)),
                ("via", JsonTreeNode.FromString(reach.Via)),
                ("criticalityKey", Js.Str(criticality)),
                ("depth", JsonTreeNode.FromInt64(reach.Depth)),
                ("path", Js.Strs(reach.Path))));
        }

        string rootName = rootApp != null ? AppName(rootApp)
            : rootComponent != null ? rootComponent.Text("labelAsSeen") ?? componentId!
            : rootLocation != null ? rootLocation.Text("labelAsSeen") ?? rootLocation.Text("externalId") ?? locationId!
            : appId ?? "unknown";
        JsonTreeNode data = Js.Obj(
            ("root", Js.Obj(
                ("businessApplicationId", Js.Str(appId)),
                ("technicalComponentId", Js.Str(componentId)),
                ("dataLocationId", Js.Str(locationId)),
                ("kind", JsonTreeNode.FromString(rootApp != null ? "application" : rootComponent != null ? "component" : "data-location")),
                ("name", JsonTreeNode.FromString(rootName)))),
            ("nodes", Arr(nodes)),
            ("dataLocation", locationFacts),
            ("counts", Js.Obj(
                ("byTier", Arr(byTier.Select(b => Bucket(b.Key.Length == 0 ? null : b.Key, b.Key.Length == 0 ? "No tier" : s.Label("tier", b.Key), b.Value)))),
                ("byCriticality", Arr(byCriticality.Select(b => Bucket(b.Key.Length == 0 ? null : b.Key, b.Key.Length == 0 ? "Not stated" : Js.Text(ListValue(s, "criticality", b.Key), "label") ?? b.Key, b.Value)))))),
            ("truncatedAtDepth", JsonTreeNode.FromBoolean(truncated)));
        return s.Answer(data, nodes.Count + (nodes.Count == 1 ? " application is" : " applications are") + " reached from " + rootName + ".");
    }

    // OP-10 from an application or a component over the counted edges: the walk's payload (reached, truncatedAtDepth).
    private static JsonTreeNode WalkImpact(ToolScope s, string? appId, string? componentId, string direction, long depth, List<StoredRow> edgeRows)
    {
        JsonTreeNode input = Js.Obj(
            ("startApplicationId", Js.OptStr(appId)),
            ("startComponentId", Js.OptStr(componentId)),
            ("direction", JsonTreeNode.FromString(direction)),
            ("maxDepth", JsonTreeNode.FromInt64(Math.Max(1, depth))),
            ("edges", Arr(edgeRows.Select(d => Js.Obj(
                ("fromId", JsonTreeNode.FromString(d.Text("fromApplication")!)),
                ("toApplicationId", Js.OptStr(d.Text("toApplication"))),
                ("toComponentId", d.Text("toApplication") == null ? Js.OptStr(d.Text("toComponent")) : null),
                ("dependencyTypeKey", JsonTreeNode.FromString(d.Text("dependencyType")!)))))));
        EvaluationResult<JsonTreeNode?> walk = s.Call.Evaluate(EngineOperation.WalkImpact, input);
        if (walk.Payload == null || walk.Outcome != "walked")
        {
            Reason? reason = walk.Reasons.FirstOrDefault(r => r.Severity == ReasonSeverity.Error);
            throw s.Call.Fail(walk.Outcome == "unavailable" ? "unavailable" : "invalid-input", reason?.Code ?? "input.malformed", reason?.Message ?? "The impact walk did not run.", reason?.Entity, reason?.Ref);
        }

        return walk.Payload;
    }

    // One application the walk reached: the shortest way there and how it joined the walk.
    private sealed class ImpactReach
    {
        public ImpactReach(string appId, int depth, List<string> path, string? dependencyTypeKey, string via)
        {
            AppId = appId;
            Depth = depth;
            Path = path;
            DependencyTypeKey = dependencyTypeKey;
            Via = via;
        }

        public string AppId { get; }

        public int Depth { get; }

        public List<string> Path { get; }

        public string? DependencyTypeKey { get; }

        public string Via { get; }
    }

    private static bool Retired(ToolScope s, string? appId)
    {
        StoredRow? app = appId == null ? null : s.Rows.Get("business-application", appId);
        return app != null && LifecycleMeaning(s, app.Text("lifecycleStage")) == "retired";
    }

    // ---- 8. get_portfolio_summary ----

    private static JsonTreeNode GetPortfolioSummary(ToolScope s)
    {
        string? portfolio = s.Text("portfolioId");
        if (portfolio != null)
        {
            s.RequireRow("portfolio", portfolio, "portfolioId");
        }

        string groupBy = s.Text("groupBy") ?? "tier";

        // Loaded ahead (document 25, B25-23): the portfolios and the people every application's path and owner gaps name,
        // then the owners and document links of every application in scope, each in one store call.
        s.Call.PrefetchAll("portfolio");
        s.Call.PrefetchAll("principal-reference");
        var apps = s.Rows.Query(RowQuery.For("business-application"))
            .Where(a => a.Text("statusMeaning") != "archived")
            .Where(a => portfolio == null || PortfolioPath(s, a.Text("portfolio")).Contains(portfolio, StringComparer.Ordinal))
            .ToList();
        s.Call.Prefetch("owner-assignment", "businessApplication", apps.Where(a => a.Text("statusMeaning") == "registered").Select(a => a.Id));
        s.Call.Prefetch("document-link", "businessApplication", apps.Select(a => a.Id));
        var buckets = apps.GroupBy(a => groupBy switch
        {
            "stage" => a.Text("lifecycleStage"),
            "kind" => a.Text("applicationKind"),
            "status" => a.Text("statusMeaning"),
            "portfolio" => a.Text("portfolio"),
            _ => a.Text("tier"),
        } ?? string.Empty, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => Bucket(
            // A portfolio group carries the Portfolio ID as its key (common.schema.json bucket), so a page can filter by it.
            g.Key.Length == 0 ? null : g.Key,
            g.Key.Length == 0 ? "Not set" : groupBy switch
            {
                "stage" => s.Label("lifecycle-stage", g.Key),
                "kind" => s.Label("application-kind", g.Key),
                "status" => ToolScope.Labelize(g.Key),
                "portfolio" => s.Rows.Get("portfolio", g.Key)?.Text("name") ?? g.Key,
                _ => s.Label("tier", g.Key),
            },
            g.Count())).ToList();

        // The one owner gap count (OwnerGapsOf, document 24, B24-17): registered applications, one gap per application and
        // role. The list carries the no-holder and no-deputy gaps; a holder whose account is off counts too, and the
        // Owners page lists that holder from the owner assignments.
        List<OwnerGapItem> gaps = OwnerGapsOf(s, apps.OrderBy(a => AppName(a), StringComparer.OrdinalIgnoreCase));
        int appsWithGaps = gaps.Select(g => g.App.Id).Distinct(StringComparer.Ordinal).Count();
        var gapList = new List<JsonTreeNode>();
        foreach (OwnerGapItem gap in gaps.Where(g => g.Listed))
        {
            StoredRow? portfolioRow = gap.App.Text("portfolio") == null ? null : s.Rows.Get("portfolio", gap.App.Text("portfolio")!);
            gapList.Add(Js.Obj(
                ("businessApplicationId", JsonTreeNode.FromString(gap.App.Id)),
                ("name", JsonTreeNode.FromString(AppName(gap.App))),
                ("portfolio", portfolioRow == null ? JsonTreeNode.Null : Js.Obj(("portfolioId", JsonTreeNode.FromString(portfolioRow.Id)), ("name", JsonTreeNode.FromString(portfolioRow.Text("name") ?? portfolioRow.Id)))),
                ("ownerRoleKey", JsonTreeNode.FromString(gap.Role)),
                ("missing", JsonTreeNode.FromString(gap.Missing)),
                ("holderDirectoryState", Js.Str(gap.HolderDirectoryState))));
        }

        var appIds = new HashSet<string>(apps.Select(a => a.Id), StringComparer.Ordinal);
        int overdue = s.Rows.Query(RowQuery.For("review").Where("statusMeaning", "due"))
            .Count(r => appIds.Contains(r.Text("businessApplication") ?? string.Empty) && PastDue(s, r.Text("dueOn")));
        int documents = apps.Sum(a => DocumentStatus(s, a).Count(d => d.Meaning == "missing" || d.Meaning == "stale"));

        // Judgment call: a draft is old when it is older than the draft-expiry-days setting (30 in Balanced).
        long days = s.SettingNumber("draft-expiry-days") ?? 30;
        // Under a portfolio, only the drafts about its applications count (product review, fix round 1: the tile stayed
        // estate-wide under a portfolio); a request for a new application belongs to no portfolio yet.
        int oldDrafts = new[] { "intake-request", "change-request", "business-application" }
            .SelectMany(e => s.Rows.Query(RowQuery.For(e).Where("statusMeaning", "draft")))
            .Where(r => portfolio == null || appIds.Contains((r.Entity == "business-application" ? r.Id : r.Text("businessApplication")) ?? string.Empty))
            .Count(r => (Js.ParseMoment(r.Text("createdOn")) ?? Js.ParseMoment(r.Text("engineCheckedOn")) ?? s.Now) < s.Now.AddDays(-days));

        // ownerGaps is the one owner gap count (B24-17): the list's gaps and the holders whose account is off.
        // applicationsWithOwnerGap is the number of applications with at least one, as list_portfolios gives it.
        var members = new List<(string, JsonTreeNode?)>
        {
            ("buckets", Arr(buckets)),
            ("ownerGaps", JsonTreeNode.FromInt64(gaps.Count)),
            ("applicationsWithOwnerGap", JsonTreeNode.FromInt64(appsWithGaps)),
            ("reviewsOverdue", JsonTreeNode.FromInt64(overdue)),
            ("documentsMissingOrStale", JsonTreeNode.FromInt64(documents)),
            ("draftsOlderThanDays", JsonTreeNode.FromInt64(oldDrafts)),
            ("dataGaps", PortfolioDataGaps(s, portfolio == null ? null : appIds)),
        };
        if (s.Flag("includeOwnerGaps") == true)
        {
            List<JsonTreeNode> page = s.Page(gapList, out string? next);
            members.Add(("ownerGapList", Arr(page)));
            members.Add(("skipToken", Js.OptStr(next)));
        }

        return s.Answer(Js.Obj(members.ToArray()), apps.Count + " applications, " + appsWithGaps + " with an owner gap (" + Count(gaps.Count, "owner role", "owner roles") + " with no holder, no deputy, or a holder whose account is off).");
    }

    // ---- 9. get_requirements ----

    private static JsonTreeNode GetRequirements(ToolScope s)
    {
        StoredRow? app = s.Text("businessApplicationId") == null ? null : RequireApplication(s);
        JsonTreeNode? scope = Js.Member(s.Args, "scope");
        string? tier = app?.Text("tier") ?? Js.Text(scope, "tierKey");
        string? kind = app?.Text("applicationKind") ?? Js.Text(scope, "applicationKindKey");
        string? stage = app?.Text("lifecycleStage") ?? Js.Text(scope, "lifecycleStageKey");
        string? changeType = Js.Text(scope, "changeTypeKey");
        string? portfolio = app?.Text("portfolio") ?? Js.Text(scope, "portfolioId");
        if (app == null)
        {
            s.RequireKey("tier", tier, "scope.tierKey");
            if (kind != null)
            {
                s.RequireKey("application-kind", kind, "scope.applicationKindKey");
            }

            if (changeType != null)
            {
                s.RequireKey("change-type", changeType, "scope.changeTypeKey");
            }
        }

        bool Applies(SnapshotRow row) => (Js.Text(row.Columns, "tierKey") is not string t || t == tier) && (Js.Text(row.Columns, "applicationKindKey") is not string k || k == kind);

        var gates = new List<JsonTreeNode>();
        bool skippedOwnerKind = false;
        foreach (SnapshotRow gate in s.ActiveRows("gate").OrderBy(g => g.KeyPath, StringComparer.Ordinal))
        {
            Dictionary<string, bool>? met = null;
            if (app != null)
            {
                EvaluationResult<JsonTreeNode?> readiness = s.Call.GateReadiness(app.Id, gate.KeyPath);
                met = Js.Items(readiness.Payload, "requirements").Where(r => Js.Text(r, "requirementKey") != null).ToDictionary(r => Js.Text(r, "requirementKey")!, r => Js.Flag(r, "met") == true, StringComparer.Ordinal);
            }

            var requirements = new List<JsonTreeNode>();
            foreach (SnapshotRow requirement in s.ActiveRows("gate-requirement").Where(r => Js.Text(r.Columns, "gateKey") == gate.KeyPath && Applies(r)).OrderBy(r => r.KeyPath, StringComparer.Ordinal))
            {
                string? requirementKind = Js.Text(requirement.Columns, "requirementKind");
                if (requirementKind != "decision-accepted" && requirementKind != "document-current" && requirementKind != "review-done"
                    && requirementKind != "data-uses-declared" && requirementKind != "data-uses-decided")
                {
                    skippedOwnerKind = true;
                    continue;
                }

                string? guidelineKey = Js.Text(requirement.Columns, "guidelineKey");
                string? guideline = guidelineKey == null ? null : Js.Text(s.ConfigRow("guideline", guidelineKey)?.Columns, "number");
                requirements.Add(Js.Obj(
                    ("requirementKind", JsonTreeNode.FromString(requirementKind)),
                    ("decisionTypeKey", requirementKind == "decision-accepted" || requirementKind == "data-uses-decided" ? Js.OptStr(Js.Text(requirement.Columns, "requiredDecisionTypeKey") ?? (requirementKind == "data-uses-decided" ? EngineKeys.DataUseDecisionType : null)) : null),
                    ("documentTypeKey", requirementKind == "document-current" ? Js.OptStr(Js.Text(requirement.Columns, "requiredDocumentTypeKey")) : null),
                    ("reviewTypeKey", requirementKind == "review-done" ? Js.OptStr(Js.Text(requirement.Columns, "requiredReviewTypeKey")) : null),
                    ("guideline", Js.Str(guideline)),
                    ("guidelineRef", GuidelineRef(s, guidelineKey)),
                    ("met", met == null ? null : JsonTreeNode.FromBoolean(met.TryGetValue(requirement.KeyPath, out bool m) && m))));
            }

            gates.Add(Js.Obj(("gateKey", JsonTreeNode.FromString(gate.KeyPath)), ("requirements", Arr(requirements))));
        }

        if (skippedOwnerKind)
        {
            s.Call.Warn("requirements.owner-assigned-not-listed", "Gate requirements that need an owner assigned are not listed here. The application's owner gaps show which owner roles have no holder.", "Gate Requirement", null);
        }

        // Approvals: OP-02 for each active decision type that applies to an application, so the single most specific rule
        // group wins (document 20, C32). A type whose default subject is a configuration draft, a data domain or a data
        // location (configuration change, gate policy change, a domain owner) is not about an application at a tier, so
        // "What Tier 2 needs" leaves it out (document 24, B24-17).
        var approvals = new List<JsonTreeNode>();
        List<string> path = PortfolioPath(s, portfolio);
        foreach (SnapshotRow type in s.ActiveRows("decision-type").Where(AppliesToApplication).OrderBy(t => t.KeyPath, StringComparer.Ordinal))
        {
            // A data-use decision is opened about each data use (D-37), so its approvals are resolved for that subject,
            // as the product opens it, not for the application (wave 3 verification: "What Tier 2 needs" named the
            // application's data owner for a use that goes to its domain's).
            string? defaultSubject = Js.Text(type.Columns, "defaultSubjectKind");
            string? subjectKind = type.KeyPath == EngineKeys.DataUseDecisionType ? EngineKeys.DataUseSubjectKind
                : defaultSubject == EngineKeys.DataUseSubjectKind || defaultSubject == EngineKeys.DataMappingSubjectKind ? defaultSubject : null;
            JsonTreeNode input = Js.Obj(
                ("subjectKind", Js.OptStr(subjectKind)),
                ("decisionTypeKey", JsonTreeNode.FromString(type.KeyPath)),
                ("tierKey", Js.OptStr(tier)),
                ("lifecycleStageKey", Js.OptStr(stage)),
                ("changeTypeKey", Js.OptStr(changeType)),
                ("applicationKindKey", Js.OptStr(kind)),
                ("portfolioPath", Js.Strs(path)));
            EvaluationResult<JsonTreeNode?> required = s.Call.Evaluate(EngineOperation.ResolveRequiredApprovals, input);
            if (required.Outcome != "required" || required.Payload == null)
            {
                continue;
            }

            foreach (JsonTreeNode requirement in Js.Items(required.Payload, "requirements"))
            {
                JsonTreeNode? needed = ApprovalNeeded(s, type.KeyPath, requirement);
                if (needed != null)
                {
                    approvals.Add(needed);
                }
            }
        }

        long rank = StageRank(s, stage);
        JsonTreeNode data = Js.Obj(
            ("gates", Arr(gates)),
            ("approvals", Arr(approvals)),
            ("documents", Arr(s.ActiveRows("documentation-requirement").Where(r => Applies(r) && (stage == null || StageRank(s, Js.Text(r.Columns, "fromStageKey")) <= rank)).OrderBy(r => r.KeyPath, StringComparer.Ordinal)
                .Select(r => Js.Obj(("documentTypeKey", JsonTreeNode.FromString(Js.Text(r.Columns, "documentTypeKey")!)), ("fromStageKey", JsonTreeNode.FromString(Js.Text(r.Columns, "fromStageKey") ?? "idea")))))),
            ("reviews", Arr(s.ActiveRows("review-cadence").Where(Applies).OrderBy(r => r.KeyPath, StringComparer.Ordinal)
                .Select(r => Js.Obj(("reviewTypeKey", JsonTreeNode.FromString(Js.Text(r.Columns, "reviewTypeKey")!)), ("intervalDays", JsonTreeNode.FromInt64(Js.Number(r.Columns, "intervalDays") ?? 0)))))),
            ("gatePolicy", Arr(s.ActiveRows("gate-policy").Where(r => Js.Text(r.Columns, "tierKey") == tier && (changeType == null || Js.Text(r.Columns, "changeTypeKey") == changeType)).OrderBy(r => r.KeyPath, StringComparer.Ordinal)
                .Select(r => Js.Obj(
                    ("targetStageKey", JsonTreeNode.FromString(Js.Text(r.Columns, "targetStageKey")!)),
                    ("changeTypeKey", JsonTreeNode.FromString(Js.Text(r.Columns, "changeTypeKey")!)),
                    ("gateLevel", JsonTreeNode.FromString(Js.Text(r.Columns, "gateLevel")!)),
                    ("failMode", JsonTreeNode.FromString(Js.Text(r.Columns, "failMode") ?? "fail-open")))))));
        return s.Answer(data, "What " + (app == null ? s.Label("tier", tier) : AppName(app)) + " needs: " + gates.Count + " gates, " + approvals.Count + " approvals.");
    }

    // A decision type that applies to an application: its default subject is the application or a record of it (an intake
    // request, a change request, a review, a data use or mapping), or it names none.
    private static readonly string[] ApplicationSubjectKinds = { "business-application", "intake-request", "change-request", "review", "data-use", "data-mapping" };

    private static bool AppliesToApplication(SnapshotRow type)
    {
        string? kind = Js.Text(type.Columns, "defaultSubjectKind");
        return kind == null || ApplicationSubjectKinds.Contains(kind, StringComparer.Ordinal);
    }

    // One OP-02 requirement as the contract's approvalNeeded, with the mode and count from the C32 row when the
    // requirement is an approval rule, and first to respond, 1, for an accountable card (document 25, section 5.4).
    private static JsonTreeNode? ApprovalNeeded(ToolScope s, string decisionTypeKey, JsonTreeNode requirement)
    {
        string? role = Js.Text(requirement, "approverRoleKey");
        string? ownerRole = Js.Text(requirement, "approverOwnerRoleKey");
        bool proposedOwners = Js.Flag(requirement, "approverProposedOwners") == true;
        if ((role == null ? 0 : 1) + (ownerRole == null ? 0 : 1) + (proposedOwners ? 1 : 0) != 1)
        {
            return null;
        }

        SnapshotRow? rule = s.ConfigRow("approval-rule", Js.Text(requirement, "ruleKey"));
        string mode = Js.Text(requirement, "approvalMode") ?? (rule == null ? null : Js.Text(rule.Columns, "approvalMode")) ?? "first-to-respond";
        long count = Js.Number(requirement, "minimumCount") ?? (rule == null ? null : Js.Number(rule.Columns, "minimumCount")) ?? 1;
        // An owner role held on a data domain or location says so, so a page never names the application's data owner for
        // a use that goes to its domain's (wave 3 verification).
        string? ownerSubject = ownerRole == null ? null : Js.Text(requirement, "ownerSubject");
        return Js.Obj(
            ("decisionTypeKey", JsonTreeNode.FromString(decisionTypeKey)),
            ("approverRoleKey", Js.OptStr(role)),
            ("approverOwnerRoleKey", Js.OptStr(ownerRole)),
            ("approverProposedOwners", proposedOwners ? JsonTreeNode.True : null),
            ("approvalMode", JsonTreeNode.FromString(mode)),
            ("minimumCount", JsonTreeNode.FromInt64(count)),
            ("ownerSubject", ownerSubject == null || ownerSubject == "business-application" ? null : JsonTreeNode.FromString(ownerSubject)));
    }
}
