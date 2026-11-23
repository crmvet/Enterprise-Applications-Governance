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

    private static JsonTreeNode WhoAmI(ToolScope s)
    {
        if (!s.IsAiCaller)
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
            ("actorKind", JsonTreeNode.FromString("ai-client-for-person")),
            ("clientApp", Js.Obj(("appId", JsonTreeNode.FromString(session.Caller.ClientAppId)), ("label", JsonTreeNode.FromString(session.Caller.ClientAppLabel ?? session.Caller.ClientAppId)))),
            ("configurationSet", Js.Obj(
                ("key", JsonTreeNode.FromString(session.ConfigurationSetKey)),
                ("version", JsonTreeNode.FromInt64(session.ConfigurationSetVersion)),
                ("publishedOn", Js.Utc(session.PublishedOnUtc ?? s.Now)))),
            ("toolsets", Js.Strs(s.Invocation.VisibleToolsets)),
            ("tools", Js.Strs(s.Invocation.VisibleTools)));
        string summary = session.Roles.Count == 0
            ? "You are signed in but hold no product role. Ask for a role through its Entra group."
            : "You hold " + string.Join(", ", session.RoleKeys) + " and may use " + s.Invocation.VisibleTools.Count + " tools.";
        return s.Answer(data, summary);
    }

    // ---- 2. get_my_queue ----

    private static JsonTreeNode GetMyQueue(ToolScope s)
    {
        var kinds = new HashSet<string>(s.Strings("kinds"), StringComparer.Ordinal);
        bool Want(string kind) => kinds.Count == 0 || kinds.Contains(kind);
        var items = new List<(int Order, string Due, JsonTreeNode Item)>();
        var myApps = s.Query("owner-assignment", ("principal", s.PersonId))
            .Select(o => o.Text("businessApplication")).Where(a => a != null).Select(a => a!).Distinct(StringComparer.Ordinal)
            .Where(a => s.OwnsApplication(a)).ToList();

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
                bool mine = Same(record.Text("askedPrincipal"), s.PersonId)
                    || (record.Text("requiredRole") is string role && s.HoldsRole(role))
                    || (record.Text("requiredOwnerRole") is string ownerRole && app != null && s.OwnsApplication(app, ownerRole));
                if (!mine)
                {
                    continue;
                }

                items.Add((0, record.Text("expiresOn") ?? "~", Js.Obj(
                    ("kind", JsonTreeNode.FromString("approval")),
                    ("subjectKind", JsonTreeNode.FromString("decision")),
                    ("subjectId", JsonTreeNode.FromString(decision.Id)),
                    ("title", JsonTreeNode.FromString(decision.Text("statement") ?? "Decision " + decision.Id)),
                    ("businessApplicationId", Js.Str(app)),
                    ("dueOn", ToolScope.DateOrNull(record.Text("expiresOn"))),
                    ("status", s.StatusValue(record, "outcome", "outcomeMeaning")),
                    ("why", JsonTreeNode.FromString("Waiting for your answer on the approval card. Answer it there; no tool answers it.")),
                    ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("decision", decision.Id))),
                    ("nextTool", JsonTreeNode.Null))));
            }
        }

        foreach (string app in myApps)
        {
            if (Want("review"))
            {
                foreach (StoredRow review in s.Query("review", ("businessApplication", app), ("statusMeaning", "due")))
                {
                    items.Add((1, review.Text("dueOn") ?? "~", Js.Obj(
                        ("kind", JsonTreeNode.FromString("review")),
                        ("subjectKind", JsonTreeNode.FromString("review")),
                        ("subjectId", JsonTreeNode.FromString(review.Id)),
                        ("title", JsonTreeNode.FromString(s.Label("review-type", review.Text("reviewType")) + " review")),
                        ("businessApplicationId", JsonTreeNode.FromString(app)),
                        ("dueOn", ToolScope.DateOrNull(review.Text("dueOn"))),
                        ("status", s.StatusValue(review)),
                        ("why", JsonTreeNode.FromString("A review of an application you own is due.")),
                        ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("review", review.Id))),
                        ("nextTool", JsonTreeNode.FromString("save_decision_proposal")))));
                }
            }

            if (Want("document"))
            {
                foreach (StoredRow link in s.Query("document-link", ("businessApplication", app), ("statusMeaning", "stale")))
                {
                    items.Add((2, link.Text("staleOn") ?? "~", Js.Obj(
                        ("kind", JsonTreeNode.FromString("document")),
                        ("subjectKind", JsonTreeNode.FromString("document-link")),
                        ("subjectId", JsonTreeNode.FromString(link.Id)),
                        ("title", JsonTreeNode.FromString(s.Label("document-type", link.Text("documentType")) + " is stale")),
                        ("businessApplicationId", JsonTreeNode.FromString(app)),
                        ("dueOn", ToolScope.DateOrNull(link.Text("staleOn"))),
                        ("status", s.StatusValue(link)),
                        ("why", JsonTreeNode.FromString("A document of an application you own is stale.")),
                        ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("document-link", link.Id))),
                        ("nextTool", JsonTreeNode.FromString("confirm_document_checked")))));
                }
            }

            if (Want("owner-gap"))
            {
                StoredRow? application = s.Rows.Get("business-application", app);
                foreach ((string role, string missing) in OwnerGaps(s, s.CurrentOwners(app)))
                {
                    items.Add((5, "~", Js.Obj(
                        ("kind", JsonTreeNode.FromString("owner-gap")),
                        ("subjectKind", JsonTreeNode.FromString("business-application")),
                        ("subjectId", JsonTreeNode.FromString(app)),
                        ("title", JsonTreeNode.FromString((application == null ? app : AppName(application)) + ": " + s.Label("owner-role", role) + " has no " + missing)),
                        ("businessApplicationId", JsonTreeNode.FromString(app)),
                        ("dueOn", JsonTreeNode.Null),
                        ("status", JsonTreeNode.Null),
                        ("why", JsonTreeNode.FromString("An owner role on an application you own has no " + missing + ".")),
                        ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("business-application", app))),
                        ("nextTool", JsonTreeNode.FromString("propose_owner_change")))));
                }
            }
        }

        if (Want("claim"))
        {
            foreach (StoredRow claim in s.Query("claim", ("suggestedClaimant", s.PersonId), ("statusMeaning", "open")))
            {
                items.Add((3, "~", Js.Obj(
                    ("kind", JsonTreeNode.FromString("claim")),
                    ("subjectKind", JsonTreeNode.FromString("claim")),
                    ("subjectId", JsonTreeNode.FromString(claim.Id)),
                    ("title", JsonTreeNode.FromString(claim.Text("labelAsSeen") ?? claim.Id)),
                    ("businessApplicationId", JsonTreeNode.Null),
                    ("dueOn", JsonTreeNode.Null),
                    ("status", s.StatusValue(claim)),
                    ("why", JsonTreeNode.FromString("The gap check suggests you as the owner of this item.")),
                    ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("claim", claim.Id))),
                    ("nextTool", JsonTreeNode.FromString("claim_resource")))));
            }
        }

        if (Want("draft"))
        {
            foreach ((string entity, string column, string title) in new[] { ("intake-request", "requester", "title"), ("change-request", "createdByPrincipal", "title"), ("business-application", "createdByPrincipal", "name") })
            {
                foreach (StoredRow draft in s.Query(entity, (column, s.PersonId), ("statusMeaning", "draft")))
                {
                    items.Add((4, "~", Js.Obj(
                        ("kind", JsonTreeNode.FromString("draft")),
                        ("subjectKind", JsonTreeNode.FromString(entity)),
                        ("subjectId", JsonTreeNode.FromString(draft.Id)),
                        ("title", JsonTreeNode.FromString(draft.Text(title) ?? draft.Text("recordNumber") ?? draft.Id)),
                        ("businessApplicationId", Js.Str(entity == "business-application" ? draft.Id : draft.Text("businessApplication"))),
                        ("dueOn", JsonTreeNode.Null),
                        ("status", s.StatusValue(draft)),
                        ("why", JsonTreeNode.FromString("Your draft is not submitted yet.")),
                        ("recordUrl", JsonTreeNode.FromString(s.RecordUrl(entity, draft.Id))),
                        ("nextTool", JsonTreeNode.FromString(entity == "business-application" ? "save_application_draft" : "submit_record")))));
                }
            }
        }

        var ordered = items.OrderBy(i => i.Order).ThenBy(i => i.Due, StringComparer.Ordinal).ThenBy(i => Js.Text(i.Item, "subjectId"), StringComparer.Ordinal).Select(i => i.Item).ToList();
        List<JsonTreeNode> page = s.Page(ordered, out string? next);
        return s.Answer(Js.Obj(("items", Arr(page)), ("skipToken", Js.OptStr(next))), ordered.Count == 0 ? "Nothing needs you right now." : ordered.Count + " items need you.");
    }

    // ---- 3. get_role_holders ----

    private static JsonTreeNode GetRoleHolders(ToolScope s)
    {
        SnapshotRow role = s.RequireKey("role", s.Text("roleKey"), "roleKey");
        if (Js.Flag(role.Columns, "isWorkloadRole") == true)
        {
            throw s.Invalid("roleKey", "A workload role has no group; its holders are Workload Grants. Use check_role_bindings.");
        }

        IDirectoryReader directory = s.Options.Directory ?? throw s.Upstream("Microsoft Graph", "no Graph client is bound to this server");
        var groups = new List<JsonTreeNode>();
        var members = new SortedDictionary<string, JsonTreeNode>(StringComparer.OrdinalIgnoreCase);
        foreach (SnapshotRow binding in s.ActiveRows("role-group-binding").Where(b => Js.Text(b.Columns, "roleKey") == role.KeyPath))
        {
            string group = Js.Text(binding.Columns, "entraGroupObjectId")!;
            DirectoryAnswer<DirectoryObject?> groupInfo = directory.Group(s.Session.Caller, group);
            DirectoryAnswer<IReadOnlyList<DirectoryObject>> answer = directory.GroupMembers(s.Session.Caller, group);
            if (!groupInfo.Answered || !answer.Answered)
            {
                throw s.Upstream("Microsoft Graph", groupInfo.Problem ?? answer.Problem);
            }

            groups.Add(Js.Obj(("objectId", JsonTreeNode.FromString(group)), ("displayLabel", JsonTreeNode.FromString(groupInfo.Value?.DisplayLabel ?? s.DisplayLabel(group)))));
            foreach (DirectoryObject member in answer.Value)
            {
                members[member.ObjectId] = Js.Obj(("objectId", JsonTreeNode.FromString(member.ObjectId)), ("displayLabel", JsonTreeNode.FromString(member.DisplayLabel)));
            }
        }

        JsonTreeNode data = Js.Obj(
            ("role", s.LabelledKey("role", role.KeyPath)),
            ("groups", Arr(groups)),
            ("members", Arr(members.Values)),
            ("readFrom", JsonTreeNode.FromString("Entra, live")),
            ("note", groups.Count == 0 ? JsonTreeNode.FromString("No Entra group is bound to this role in the configuration in force.") : JsonTreeNode.Null));
        return s.Answer(data, members.Count + " people hold " + s.Label("role", role.KeyPath) + " through " + groups.Count + " group(s), read live from Entra.");
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

        var matches = new List<StoredRow>();
        foreach (StoredRow app in s.Rows.Query(RowQuery.For("business-application")))
        {
            if (!meanings.Contains(app.Text("statusMeaning") ?? string.Empty, StringComparer.Ordinal)
                || (kinds.Count > 0 && !kinds.Contains(app.Text("applicationKind") ?? string.Empty, StringComparer.Ordinal))
                || (tiers.Count > 0 && !tiers.Contains(app.Text("tier") ?? string.Empty, StringComparer.Ordinal))
                || (stages.Count > 0 && !stages.Contains(app.Text("lifecycleStage") ?? string.Empty, StringComparer.Ordinal))
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
            ("gaps", Arr(OwnerGaps(s, owners).Select(g => Js.Obj(("ownerRoleKey", JsonTreeNode.FromString(g.Role)), ("missing", JsonTreeNode.FromString(g.Missing)))))),
            ("microsoftOwnerDiffers", checkedOn.Count == 0 ? JsonTreeNode.Null : JsonTreeNode.FromBoolean(components.Any(c => c.Flag("ownerDiffers")))),
            ("ownerCheckedOn", checkedOn.Count == 0 ? JsonTreeNode.Null : Js.Utc(checkedOn.Max())));
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
                ("nextReviewOn", ToolScope.DateOrNull(app.Text("nextReviewOn"))))),
        };

        if (Want("owners"))
        {
            members.Add(("owners", Arr(s.CurrentOwners(app.Id).Select(o => OwnerAssignment(s, o)))));
        }

        if (Want("components"))
        {
            members.Add(("components", Arr(s.Query("technical-component", ("businessApplication", app.Id))
                .Where(c => c.Text("retiredOn") == null && (c.Text("url") ?? string.Empty).StartsWith("https://", StringComparison.Ordinal))
                .Select(c => Js.Obj(
                    ("technicalComponentId", JsonTreeNode.FromString(c.Id)),
                    ("kind", s.LabelledKey("component-kind", c.Text("componentKind") ?? "unknown")),
                    ("externalId", JsonTreeNode.FromString(c.Text("externalId") ?? string.Empty)),
                    ("url", JsonTreeNode.FromString(c.Text("url")!)),
                    ("labelAsSeen", Js.Str(c.Text("labelAsSeen"))),
                    ("lastSeenOn", ToolScope.MomentOrNull(c.Text("lastSeenOn"))))))));
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
                    ("decidedOn", ToolScope.MomentOrNull(d.Text("decidedOn"))))))));
        }

        if (Want("changes"))
        {
            members.Add(("changes", Arr(s.Query("change-request", ("businessApplication", app.Id)).Where(IsOpen).Select(c => Js.Obj(
                ("changeRequestId", JsonTreeNode.FromString(c.Id)),
                ("title", JsonTreeNode.FromString(c.Text("title") ?? string.Empty)),
                ("changeType", s.LabelledKey("change-type", c.Text("changeType") ?? "unknown")),
                ("status", s.StatusValue(c)),
                ("windowStart", ToolScope.MomentOrNull(c.Text("windowStart"))),
                ("windowEnd", ToolScope.MomentOrNull(c.Text("windowEnd"))))))));
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
                    ("finishedOn", ToolScope.MomentOrNull(d.Text("finishedOn"))));
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

    private static JsonTreeNode GetDependencyImpact(ToolScope s)
    {
        string? appId = s.Text("businessApplicationId");
        string? componentId = s.Text("technicalComponentId");
        StoredRow? rootApp = appId == null ? null : RequireApplication(s);
        StoredRow? rootComponent = componentId == null ? null : s.RequireRow("technical-component", componentId, "technicalComponentId");
        IReadOnlyList<string> typeFilter = s.Strings("dependencyTypeKeys");
        bool includeRetired = s.Flag("includeRetired") ?? false;
        long depth = Js.Number(s.Args, "depth") ?? 3;
        long? maxSetting = s.SettingNumber("impact-max-depth");
        if (maxSetting.HasValue && depth > maxSetting.Value)
        {
            s.Call.Warn("impact.depth-capped", "depth " + depth + " is above the impact-max-depth setting, so the walk stops at " + maxSetting.Value + ".", "Setting", "impact-max-depth");
            depth = maxSetting.Value;
        }

        // Only dependency types with Counts For Impact = yes are followed (document 20, C17).
        var counted = new HashSet<string>(s.ActiveRows("dependency-type").Where(t => Js.Flag(t.Columns, "countsForImpact") == true).Select(t => t.KeyPath), StringComparer.Ordinal);
        var edgeRows = s.Rows.Query(RowQuery.For("dependency"))
            .Where(d => d.Text("endedOn") == null && d.Text("fromApplication") != null)
            .Where(d => counted.Contains(d.Text("dependencyType") ?? string.Empty) && (typeFilter.Count == 0 || typeFilter.Contains(d.Text("dependencyType") ?? string.Empty, StringComparer.Ordinal)))
            .Where(d => includeRetired || (!Retired(s, d.Text("fromApplication")) && !Retired(s, d.Text("toApplication"))))
            .ToList();
        JsonTreeNode input = Js.Obj(
            ("startApplicationId", Js.OptStr(appId)),
            ("startComponentId", Js.OptStr(componentId)),
            ("direction", JsonTreeNode.FromString(s.Text("direction") ?? "dependants")),
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

        var byTier = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var byCriticality = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var nodes = new List<JsonTreeNode>();
        foreach (JsonTreeNode reached in Js.Items(walk.Payload, "reached"))
        {
            string? id = Js.Text(reached, "applicationId");
            StoredRow? app = id == null ? null : s.Rows.Get("business-application", id);
            if (app == null)
            {
                continue;
            }

            IReadOnlyList<string> path = Js.Strings(reached, "path");
            string? previous = path.Count >= 2 ? path[path.Count - 2] : null;
            StoredRow? edge = edgeRows.FirstOrDefault(d => (Same(d.Text("fromApplication"), previous) && Same(d.Text("toApplication"), id)) || (Same(d.Text("fromApplication"), id) && Same(d.Text("toApplication"), previous)));
            string? criticality = edge?.Text("criticality");
            string tierKey = app.Text("tier") ?? string.Empty;
            byTier[tierKey] = byTier.TryGetValue(tierKey, out int t) ? t + 1 : 1;
            string critKey = criticality ?? string.Empty;
            byCriticality[critKey] = byCriticality.TryGetValue(critKey, out int c) ? c + 1 : 1;
            IReadOnlyList<StoredRow> owners = s.CurrentOwners(app.Id);
            nodes.Add(Js.Obj(
                ("businessApplicationId", JsonTreeNode.FromString(app.Id)),
                ("name", JsonTreeNode.FromString(AppName(app))),
                ("tier", s.LabelledKey("tier", app.Text("tier"))),
                ("lifecycleMeaning", Js.Str(LifecycleMeaning(s, app.Text("lifecycleStage")))),
                ("businessOwner", OwnerRef(s, owners, "business-owner")),
                ("technicalOwner", OwnerRef(s, owners, "technical-owner")),
                ("dependencyTypeKey", JsonTreeNode.FromString(Js.Strings(reached, "dependencyTypeKeys").FirstOrDefault() ?? edge?.Text("dependencyType") ?? "unknown")),
                ("criticalityKey", Js.Str(criticality)),
                ("depth", JsonTreeNode.FromInt64(Js.Number(reached, "depth") ?? 1)),
                ("path", Js.Strs(path))));
        }

        string rootName = rootApp != null ? AppName(rootApp) : rootComponent?.Text("labelAsSeen") ?? componentId ?? appId ?? "unknown";
        JsonTreeNode data = Js.Obj(
            ("root", Js.Obj(("businessApplicationId", Js.Str(appId)), ("technicalComponentId", Js.Str(componentId)), ("name", JsonTreeNode.FromString(rootName)))),
            ("nodes", Arr(nodes)),
            ("counts", Js.Obj(
                ("byTier", Arr(byTier.Select(b => Bucket(b.Key.Length == 0 ? null : b.Key, b.Key.Length == 0 ? "No tier" : s.Label("tier", b.Key), b.Value)))),
                ("byCriticality", Arr(byCriticality.Select(b => Bucket(b.Key.Length == 0 ? null : b.Key, b.Key.Length == 0 ? "Not stated" : Js.Text(ListValue(s, "criticality", b.Key), "label") ?? b.Key, b.Value)))))),
            ("truncatedAtDepth", JsonTreeNode.FromBoolean(Js.Flag(walk.Payload, "truncatedAtDepth") == true)));
        return s.Answer(data, nodes.Count + " applications are reached from " + rootName + ".");
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
        var apps = s.Rows.Query(RowQuery.For("business-application"))
            .Where(a => a.Text("statusMeaning") != "archived")
            .Where(a => portfolio == null || PortfolioPath(s, a.Text("portfolio")).Contains(portfolio, StringComparer.Ordinal))
            .ToList();
        var buckets = apps.GroupBy(a => groupBy switch
        {
            "stage" => a.Text("lifecycleStage"),
            "kind" => a.Text("applicationKind"),
            "status" => a.Text("statusMeaning"),
            "portfolio" => a.Text("portfolio"),
            _ => a.Text("tier"),
        } ?? string.Empty, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => Bucket(
            g.Key.Length == 0 || groupBy == "portfolio" ? null : g.Key,
            g.Key.Length == 0 ? "Not set" : groupBy switch
            {
                "stage" => s.Label("lifecycle-stage", g.Key),
                "kind" => s.Label("application-kind", g.Key),
                "status" => ToolScope.Labelize(g.Key),
                "portfolio" => s.Rows.Get("portfolio", g.Key)?.Text("name") ?? g.Key,
                _ => s.Label("tier", g.Key),
            },
            g.Count())).ToList();

        var gapList = new List<JsonTreeNode>();
        int appsWithGaps = 0;
        foreach (StoredRow app in apps.OrderBy(a => AppName(a), StringComparer.OrdinalIgnoreCase))
        {
            IReadOnlyList<StoredRow> owners = s.CurrentOwners(app.Id);
            var gaps = OwnerGaps(s, owners);
            if (gaps.Count > 0)
            {
                appsWithGaps++;
            }

            StoredRow? portfolioRow = app.Text("portfolio") == null ? null : s.Rows.Get("portfolio", app.Text("portfolio")!);
            foreach ((string role, string missing) in gaps)
            {
                StoredRow? holder = owners.FirstOrDefault(o => o.Text("ownerRole") == role && !o.Flag("isDeputy"));
                string? state = holder == null ? null : s.Principal(holder.Text("principal"))?.Text("directoryState");
                gapList.Add(Js.Obj(
                    ("businessApplicationId", JsonTreeNode.FromString(app.Id)),
                    ("name", JsonTreeNode.FromString(AppName(app))),
                    ("portfolio", portfolioRow == null ? JsonTreeNode.Null : Js.Obj(("portfolioId", JsonTreeNode.FromString(portfolioRow.Id)), ("name", JsonTreeNode.FromString(portfolioRow.Text("name") ?? portfolioRow.Id)))),
                    ("ownerRoleKey", JsonTreeNode.FromString(role)),
                    ("missing", JsonTreeNode.FromString(missing)),
                    ("holderDirectoryState", Js.Str(state))));
            }
        }

        var appIds = new HashSet<string>(apps.Select(a => a.Id), StringComparer.Ordinal);
        int overdue = s.Rows.Query(RowQuery.For("review").Where("statusMeaning", "due"))
            .Count(r => appIds.Contains(r.Text("businessApplication") ?? string.Empty) && (Js.ParseMoment(r.Text("dueOn")) ?? DateTime.MaxValue).Date < s.Now.Date);
        int documents = apps.Sum(a => DocumentStatus(s, a).Count(d => d.Meaning == "missing" || d.Meaning == "stale"));

        // Judgment call: a draft is old when it is older than the draft-expiry-days setting (30 in Balanced).
        long days = s.SettingNumber("draft-expiry-days") ?? 30;
        int oldDrafts = new[] { "intake-request", "change-request", "business-application" }
            .SelectMany(e => s.Rows.Query(RowQuery.For(e).Where("statusMeaning", "draft")))
            .Count(r => (Js.ParseMoment(r.Text("createdOn")) ?? Js.ParseMoment(r.Text("engineCheckedOn")) ?? s.Now) < s.Now.AddDays(-days));

        var members = new List<(string, JsonTreeNode?)>
        {
            ("buckets", Arr(buckets)),
            ("ownerGaps", JsonTreeNode.FromInt64(appsWithGaps)),
            ("reviewsOverdue", JsonTreeNode.FromInt64(overdue)),
            ("documentsMissingOrStale", JsonTreeNode.FromInt64(documents)),
            ("draftsOlderThanDays", JsonTreeNode.FromInt64(oldDrafts)),
        };
        if (s.Flag("includeOwnerGaps") == true)
        {
            List<JsonTreeNode> page = s.Page(gapList, out string? next);
            members.Add(("ownerGapList", Arr(page)));
            members.Add(("skipToken", Js.OptStr(next)));
        }

        return s.Answer(Js.Obj(members.ToArray()), apps.Count + " applications, " + appsWithGaps + " with an owner gap.");
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
                if (requirementKind != "decision-accepted" && requirementKind != "document-current" && requirementKind != "review-done")
                {
                    skippedOwnerKind = true;
                    continue;
                }

                string? guidelineKey = Js.Text(requirement.Columns, "guidelineKey");
                string? guideline = guidelineKey == null ? null : Js.Text(s.ConfigRow("guideline", guidelineKey)?.Columns, "number");
                requirements.Add(Js.Obj(
                    ("requirementKind", JsonTreeNode.FromString(requirementKind)),
                    ("decisionTypeKey", requirementKind == "decision-accepted" ? Js.OptStr(Js.Text(requirement.Columns, "requiredDecisionTypeKey")) : null),
                    ("documentTypeKey", requirementKind == "document-current" ? Js.OptStr(Js.Text(requirement.Columns, "requiredDocumentTypeKey")) : null),
                    ("reviewTypeKey", requirementKind == "review-done" ? Js.OptStr(Js.Text(requirement.Columns, "requiredReviewTypeKey")) : null),
                    ("guideline", Js.Str(guideline)),
                    ("met", met == null ? null : JsonTreeNode.FromBoolean(met.TryGetValue(requirement.KeyPath, out bool m) && m))));
            }

            gates.Add(Js.Obj(("gateKey", JsonTreeNode.FromString(gate.KeyPath)), ("requirements", Arr(requirements))));
        }

        if (skippedOwnerKind)
        {
            s.Call.Warn("requirements.owner-assigned-not-listed", "Owner-assigned gate requirements are not listed here; get_application_owners shows the owner gaps.", "Gate Requirement", null);
        }

        // Approvals: OP-02 for each active decision type, so the single most specific rule group wins (document 20, C32).
        var approvals = new List<JsonTreeNode>();
        List<string> path = PortfolioPath(s, portfolio);
        foreach (SnapshotRow type in s.ActiveRows("decision-type").OrderBy(t => t.KeyPath, StringComparer.Ordinal))
        {
            JsonTreeNode input = Js.Obj(
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

    // One OP-02 requirement as the contract's approvalNeeded, with the mode and count from the C32 row when the
    // requirement is an approval rule, and first to respond, 1, for an accountable card (document 25, section 5.4).
    private static JsonTreeNode? ApprovalNeeded(ToolScope s, string decisionTypeKey, JsonTreeNode requirement)
    {
        string? role = Js.Text(requirement, "approverRoleKey");
        string? ownerRole = Js.Text(requirement, "approverOwnerRoleKey");
        if ((role == null) == (ownerRole == null))
        {
            return null;
        }

        SnapshotRow? rule = s.ConfigRow("approval-rule", Js.Text(requirement, "ruleKey"));
        string mode = Js.Text(requirement, "approvalMode") ?? (rule == null ? null : Js.Text(rule.Columns, "approvalMode")) ?? "first-to-respond";
        long count = Js.Number(requirement, "minimumCount") ?? (rule == null ? null : Js.Number(rule.Columns, "minimumCount")) ?? 1;
        return Js.Obj(
            ("decisionTypeKey", JsonTreeNode.FromString(decisionTypeKey)),
            ("approverRoleKey", Js.OptStr(role)),
            ("approverOwnerRoleKey", Js.OptStr(ownerRole)),
            ("approvalMode", JsonTreeNode.FromString(mode)),
            ("minimumCount", JsonTreeNode.FromInt64(count)));
    }
}
