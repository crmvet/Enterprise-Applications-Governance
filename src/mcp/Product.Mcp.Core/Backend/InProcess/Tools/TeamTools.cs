using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Snapshots;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Json;

namespace Product.Mcp.Core.Backend.InProcess;

// Tools 95 and 96 (document 24, B24-20): the teams and committees.
// - A team is a group the install already knows: a Principal Reference (O05) of kind group, or a group a Role Group Binding
//   (C30) of the configuration in force names. No new entity: membership stays in Entra, and the product stores links.
// - Its kinds are derived on every read, never stored. A committee is a group bound to a product role that answers
//   decisions (an Approval Rule asks the role for its answer, or a Decision Right gives it the responsible, accountable
//   or consulted letter). A support team holds a current support owner or technical owner assignment (O06). A role group
//   is any other bound group. A group that is none of these is "other". A group can be more than one kind.
// - Members come from the directory reader the install already has (InProcessBackendOptions.Directory): the demo personas
//   on a demo install and on a local run with the development identity, Microsoft Graph at read time as the signed-in
//   person when the directory reads are on (never stored), and otherwise none: the read then says membership is managed
//   in Entra and names the group.
// - Both tools are reads for every product role, under Tool Policy, and an AI client may call them as reads.
internal static partial class ToolHandlers
{
    private static readonly string[] TeamKindOrder = { "committee", "support-team", "role-group", "other" };

    // The decision right letters that make a role one that answers decisions (supports and informed do not).
    private static readonly string[] DecidingLetters = { "responsible", "accountable", "consulted" };

    // The owner roles that make a group a support team (document 24, B24-20).
    private static readonly string[] SupportOwnerRoleKeys = { "support-owner", "technical-owner" };

    // The latest answers a team page lists.
    private const int RecentAnswerLimit = 20;

    // ---- 95. list_teams ----

    private static JsonTreeNode ListTeams(ToolScope s)
    {
        string? kind = s.Text("kind");
        string? text = s.Text("text")?.Trim().ToLowerInvariant();
        string? roleKey = s.Text("roleKey");
        if (roleKey != null)
        {
            s.RequireKey("role", roleKey, "roleKey");
        }

        string? member = s.Text("memberObjectId");
        TeamWorld world = TeamWorld.Read(s);
        IDirectoryReader? directory = s.Options.Directory;
        int notRead = 0;

        // Each team's members are read at most once per call.
        var read = new Dictionary<string, DirectoryAnswer<IReadOnlyList<DirectoryObject>>>(StringComparer.OrdinalIgnoreCase);
        DirectoryAnswer<IReadOnlyList<DirectoryObject>> MembersOf(IDirectoryReader reader, string group)
        {
            if (!read.TryGetValue(group, out DirectoryAnswer<IReadOnlyList<DirectoryObject>>? answer))
            {
                answer = reader.GroupMembers(s.Session.Caller, group);
                read[group] = answer;
                if (!answer.Answered)
                {
                    notRead++;
                }
            }

            return answer;
        }

        // The person page's "member of": the teams the person is in, in one directory read for the person where the
        // directory can (security review, wave 4: not one Graph call per team). With no directory nothing can be said, and the answer
        // says so.
        HashSet<string>? memberOf = null;
        if (member != null && directory != null)
        {
            DirectoryAnswer<IReadOnlyList<string>> groups = directory.MemberGroups(s.Session.Caller, member, world.Teams.Select(t => t.ObjectId).ToList(), out int missed);
            notRead += groups.Answered ? missed : 1;
            memberOf = new HashSet<string>(groups.Answered ? groups.Value : Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        }

        var matches = new List<TeamFacts>();
        foreach (TeamFacts team in world.Teams)
        {
            if (roleKey != null && !team.RoleKeys.Contains(roleKey, StringComparer.Ordinal))
            {
                continue;
            }

            if (text != null && text.Length > 0 && !(team.Label + " " + string.Join(" ", team.RoleKeys.Select(r => s.Label("role", r)))).ToLowerInvariant().Contains(text))
            {
                continue;
            }

            if (member != null && (memberOf == null || !memberOf.Contains(team.ObjectId)))
            {
                continue;
            }

            matches.Add(team);
        }

        var counts = Js.Obj(
            ("all", JsonTreeNode.FromInt64(matches.Count)),
            ("committee", JsonTreeNode.FromInt64(matches.Count(t => t.Kinds.Contains("committee")))),
            ("supportTeam", JsonTreeNode.FromInt64(matches.Count(t => t.Kinds.Contains("support-team")))),
            ("roleGroup", JsonTreeNode.FromInt64(matches.Count(t => t.Kinds.Contains("role-group")))),
            ("other", JsonTreeNode.FromInt64(matches.Count(t => t.Kinds.Contains("other")))));
        var ordered = matches.Where(t => kind == null || t.Kinds.Contains(kind))
            .OrderBy(t => Array.IndexOf(TeamKindOrder, t.Kinds[0]))
            .ThenBy(t => t.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.ObjectId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        List<TeamFacts> page = s.Page(ordered, out string? next);

        // Member counts are read for the rows of this page only: one directory call per row, as the role holders are read
        // for one role at a time (B24-18, choice 5).
        var items = new List<JsonTreeNode>();
        foreach (TeamFacts team in page)
        {
            ResolveLabel(s, team);
            JsonTreeNode memberCount = JsonTreeNode.Null;
            if (directory != null)
            {
                DirectoryAnswer<IReadOnlyList<DirectoryObject>> answer = MembersOf(directory, team.ObjectId);
                if (answer.Answered)
                {
                    memberCount = JsonTreeNode.FromInt64(answer.Value.Count(m => string.Equals(m.PrincipalKind, "user", StringComparison.Ordinal)));
                }
            }

            List<StoredRow> apps = TeamApplications(s, team);
            items.Add(Js.Obj(
                ("teamId", JsonTreeNode.FromString(team.ObjectId)),
                ("team", Js.Obj(("objectId", JsonTreeNode.FromString(team.ObjectId)), ("displayLabel", JsonTreeNode.FromString(team.Label)))),
                ("kinds", Js.Strs(team.Kinds)),
                ("roles", Arr(team.RoleKeys.Select(r => s.LabelledKey("role", r)))),
                ("directoryState", Js.Str(team.Reference?.Text("directoryState"))),
                ("memberCount", memberCount),
                ("waitingCount", JsonTreeNode.FromInt64(world.Pending.Count(p => WaitsOn(p, team)))),
                ("applicationCount", JsonTreeNode.FromInt64(apps.Count)),
                ("assignmentCount", JsonTreeNode.FromInt64(team.Assignments.Count)),
                ("ownerGapCount", JsonTreeNode.FromInt64(OwnerGapsOf(s, apps).Count)),
                ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("team", team.ObjectId)))));
        }

        s.HideFromCaller("approval-record", world.Hidden.Count(h => page.Any(t => WaitsOn(h, t))));
        s.HideFromCaller("team-members", notRead);
        if (notRead > 0)
        {
            s.Call.Warn("teams.members-not-read", "Entra did not answer for " + Count(notRead, "team", "teams") + ", so their members are not counted now.", "Team", null);
        }

        string readFrom = MembersReadFrom(s);
        string? membership = readFrom == "not-read"
            ? "Membership is managed in Entra. This install does not read group members, so each team's members are listed in Entra."
            : readFrom == "demo-personas"
            ? "The members are the demo's fictional people in each group: the personas and the estate's other people. A real install reads them live from Entra and never stores them."
            : null;
        JsonTreeNode data = Js.Obj(
            ("items", Arr(items)),
            ("counts", counts),
            ("membersReadFrom", JsonTreeNode.FromString(readFrom)),
            ("membership", Js.Str(membership)),
            ("totalCount", JsonTreeNode.FromInt64(ordered.Count)),
            ("skipToken", Js.OptStr(next)));

        string summary = matches.Count == 0
            ? (member != null && directory == null ? "This install does not read group members, so it cannot say which teams the person is in. Membership is managed in Entra." : "No team matches.")
            : Count(matches.Count, "team", "teams") + (member != null ? " the person is in" : string.Empty) + ": "
                + KindWords(matches.Count(t => t.Kinds.Contains("committee")), matches.Count(t => t.Kinds.Contains("support-team")), matches.Count(t => t.Kinds.Contains("role-group")), matches.Count(t => t.Kinds.Contains("other"))) + "."
                + (kind != null ? " " + Count(ordered.Count, "is", "are") + " " + KindLabel(kind, ordered.Count != 1) + "." : string.Empty)
                + (readFrom == "not-read" ? " Membership is managed in Entra; this install does not read it." : readFrom == "demo-personas" ? " Members are the demo's fictional people." : string.Empty);
        return s.Answer(data, summary);
    }

    // ---- 96. get_team ----

    private static JsonTreeNode GetTeam(ToolScope s)
    {
        string id = s.RequireText("teamId");
        TeamWorld world = TeamWorld.Read(s);
        TeamFacts team = world.Teams.FirstOrDefault(t => Same(t.ObjectId, id))
            ?? throw s.NotFound("Team", id, "teamId");
        ResolveLabel(s, team);

        // Members, read once.
        IDirectoryReader? directory = s.Options.Directory;
        string readFrom = MembersReadFrom(s);
        List<DirectoryObject>? members = null;
        string? problem = null;
        if (directory != null)
        {
            DirectoryAnswer<IReadOnlyList<DirectoryObject>> answer = directory.GroupMembers(s.Session.Caller, team.ObjectId);
            if (answer.Answered)
            {
                members = answer.Value.OrderBy(m => m.DisplayLabel, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.ObjectId, StringComparer.OrdinalIgnoreCase).ToList();
            }
            else
            {
                problem = answer.Problem ?? "no answer";
                s.HideFromCaller("team-members", 1);
                s.Call.Warn("teams.members-not-read", "Entra did not answer, so the members of " + team.Label + " are not listed now.", "Team", team.ObjectId);
            }
        }

        int people = members?.Count(m => string.Equals(m.PrincipalKind, "user", StringComparison.Ordinal)) ?? 0;
        string membership = readFrom == "not-read"
            ? "Membership is managed in Entra. This install does not read group members, so open the group " + team.Label + " in Entra to see who is in it."
            : problem != null
            ? "Entra did not answer, so the members of " + team.Label + " are not listed now. Try again later."
            : readFrom == "demo-personas"
            ? (people == 0 ? "Nobody of the demo is in this fictional group." : "The members are the demo's fictional people in this group: the personas and the estate's other people.") + " A real install reads them live from Entra and never stores them."
            : "Read live from Entra for the signed-in person, and never stored." + (members != null && members.Count > people ? " " + Count(members.Count - people, "member is", "members are") + " not a person and holds no product role through this group." : string.Empty);

        // The product roles it holds, with what each role decides and approves.
        var answering = AnsweringRoleKeys(s);
        var roles = new List<JsonTreeNode>();
        var answers = new SortedDictionary<string, TypeAnswer>(StringComparer.Ordinal);
        TypeAnswer AnswerFor(string type)
        {
            if (!answers.TryGetValue(type, out TypeAnswer? entry))
            {
                entry = new TypeAnswer();
                answers[type] = entry;
            }

            return entry;
        }

        foreach (string roleKey in team.RoleKeys)
        {
            SnapshotRow? role = s.ConfigRow("role", roleKey);
            SnapshotRow binding = team.Bindings.First(b => Js.Text(b.Columns, "roleKey") == roleKey);
            var rights = s.ActiveRows("decision-right").Where(d => Js.Text(d.Columns, "roleKey") == roleKey).OrderBy(d => Js.Text(d.Columns, "decisionTypeKey") ?? string.Empty, StringComparer.Ordinal).ThenBy(d => d.KeyPath, StringComparer.Ordinal).ToList();
            var rules = s.ActiveRows("approval-rule").Where(a => Js.Text(a.Columns, "approverRoleKey") == roleKey).OrderBy(a => Js.Text(a.Columns, "decisionTypeKey") ?? string.Empty, StringComparer.Ordinal).ThenBy(a => a.KeyPath, StringComparer.Ordinal).ToList();
            foreach (SnapshotRow right in rights)
            {
                TypeAnswer entry = AnswerFor(Js.Text(right.Columns, "decisionTypeKey") ?? "unknown");
                entry.Letters.Add(Js.Text(right.Columns, "letter") ?? "responsible");
                entry.Roles.Add(roleKey);
            }

            foreach (SnapshotRow rule in rules)
            {
                TypeAnswer entry = AnswerFor(Js.Text(rule.Columns, "decisionTypeKey") ?? "unknown");
                bool consult = Js.Flag(rule.Columns, "isConsultation") == true;
                entry.Approves |= !consult;
                entry.Consulted |= consult;
                entry.Roles.Add(roleKey);
            }

            // A role that shares its key with an owner role held on data (the data owner): its people answer the
            // decisions an approval rule asks of that owner role on their own domains, as named holders (fix round 1,
            // product review: "Data owners" said it answers no decision while Kai Ostrowski had a data-use card waiting).
            foreach (SnapshotRow rule in s.ActiveRows("approval-rule").Where(a => Js.Text(a.Columns, "approverOwnerRoleKey") == roleKey && DataOwnerSubjects.Contains(Js.Text(a.Columns, "ownerSubject") ?? string.Empty, StringComparer.Ordinal)))
            {
                TypeAnswer entry = AnswerFor(Js.Text(rule.Columns, "decisionTypeKey") ?? "unknown");
                bool consult = Js.Flag(rule.Columns, "isConsultation") == true;
                entry.Approves |= !consult;
                entry.Consulted |= consult;
                entry.Roles.Add(roleKey);
                entry.ThroughOwnerRole ??= roleKey;
            }

            string? method = Js.Text(binding.Columns, "bindingMethod");
            roles.Add(Js.Obj(
                ("role", s.LabelledKey("role", roleKey)),
                ("party", role == null ? JsonTreeNode.Null : s.LabelledKey("party", Js.Text(role.Columns, "partyKey"))),
                ("duties", Js.Str(role == null ? null : Js.Text(role.Columns, "duties"))),
                ("answersDecisions", JsonTreeNode.FromBoolean(answering.Contains(roleKey))),
                ("binding", Js.Obj(
                    ("bindingKey", JsonTreeNode.FromString(binding.KeyPath)),
                    ("bindingMethod", method == "dataverse-group-team" || method == "entra-app-role-assignment" ? JsonTreeNode.FromString(method) : JsonTreeNode.Null),
                    ("lastVerifiedOn", ToolScope.MomentOrNull(Js.Text(binding.Columns, "lastVerifiedOn"))))),
                ("decisionRights", Arr(rights.Select(d => Js.Obj(
                    ("decisionRight", s.LabelledKey("decision-right", d.KeyPath)),
                    ("decisionType", s.LabelledKey("decision-type", Js.Text(d.Columns, "decisionTypeKey") ?? "unknown")),
                    ("letter", JsonTreeNode.FromString(Js.Text(d.Columns, "letter") ?? "responsible")),
                    ("scope", JsonTreeNode.FromString(ScopeWords(s, d.Columns))))))),
                ("approvals", Arr(rules.Select(a => Js.Obj(
                    ("approvalRule", s.LabelledKey("approval-rule", a.KeyPath)),
                    ("decisionType", s.LabelledKey("decision-type", Js.Text(a.Columns, "decisionTypeKey") ?? "unknown")),
                    ("isConsultation", JsonTreeNode.FromBoolean(Js.Flag(a.Columns, "isConsultation") == true)),
                    ("scope", JsonTreeNode.FromString(ScopeWords(s, a.Columns))),
                    ("guidelineRef", GuidelineRef(s, Js.Text(a.Columns, "guidelineKey")))))))));
        }

        JsonTreeNode decisionTypes = Arr(answers
            .OrderBy(a => s.Label("decision-type", a.Key), StringComparer.OrdinalIgnoreCase)
            .Select(a => Js.Obj(
                ("decisionType", s.LabelledKey("decision-type", a.Key)),
                ("letters", Js.Strs(a.Value.Letters)),
                ("approves", JsonTreeNode.FromBoolean(a.Value.Approves)),
                ("consulted", JsonTreeNode.FromBoolean(a.Value.Consulted)),
                ("roleKeys", Js.Strs(a.Value.Roles)),
                ("throughOwnerRole", a.Value.ThroughOwnerRole == null ? null : s.LabelledKey("owner-role", a.Value.ThroughOwnerRole)))));

        // The applications and the data it holds owner roles on, now.
        List<StoredRow> apps = TeamApplications(s, team);
        List<OwnerGapItem> gaps = OwnerGapsOf(s, apps);
        JsonTreeNode applications = Arr(apps.Select(app => Js.Obj(
            ("businessApplicationId", JsonTreeNode.FromString(app.Id)),
            ("name", JsonTreeNode.FromString(AppName(app))),
            ("recordNumber", Js.Str(app.Text("recordNumber"))),
            ("tier", s.LabelledKey("tier", app.Text("tier"))),
            ("stage", s.LabelledKey("lifecycle-stage", app.Text("lifecycleStage"))),
            ("registration", Js.Str(app.Text("statusMeaning"))),
            ("assignments", Arr(team.Assignments.Where(a => Same(a.Text("businessApplication"), app.Id)).OrderBy(a => a.Text("ownerRole"), StringComparer.Ordinal).ThenBy(a => a.Flag("isDeputy")).Select(a => TeamAssignment(s, a)))),
            ("ownerGapCount", JsonTreeNode.FromInt64(gaps.Count(g => g.App.Id == app.Id))),
            ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("business-application", app.Id))))));
        var tree = new Product.Engine.Core.Operations.DataTree(s.Snapshot);
        List<JsonTreeNode> dataAssignments = team.Assignments
            .Where(a => a.Text("businessApplication") == null && (a.Text("subjectDataDomain") != null || a.Text("subjectDataLocation") != null))
            .Select(a =>
            {
                string? domain = a.Text("subjectDataDomain");
                string? location = a.Text("subjectDataLocation");
                StoredRow? place = location == null ? null : s.Rows.Get("data-location", location);
                string label = domain != null ? (tree.Domain(domain) == null ? domain : tree.PathText(domain)) : place?.Text("labelAsSeen") ?? "Unnamed data location";
                return (Row: a, Label: label, Kind: domain != null ? "data-domain" : "data-location", Id: domain ?? location!);
            })
            .OrderBy(a => a.Kind, StringComparer.Ordinal).ThenBy(a => a.Label, StringComparer.OrdinalIgnoreCase).ThenBy(a => a.Row.Text("ownerRole"), StringComparer.Ordinal)
            .Select(a => Js.Obj(
                ("ownerAssignmentId", JsonTreeNode.FromString(a.Row.Id)),
                ("ownerRole", s.LabelledKey("owner-role", a.Row.Text("ownerRole"))),
                ("isDeputy", JsonTreeNode.FromBoolean(a.Row.Flag("isDeputy"))),
                ("startOn", ToolScope.DateOrNull(a.Row.Text("startOn"))),
                ("subjectKind", JsonTreeNode.FromString(a.Kind)),
                ("subjectId", JsonTreeNode.FromString(a.Id)),
                ("label", JsonTreeNode.FromString(a.Label)),
                ("recordUrl", JsonTreeNode.FromString(s.RecordUrl(a.Kind, a.Id)))))
            .ToList();
        JsonTreeNode ownerGaps = Arr(gaps.Select(g => Js.Obj(
            ("businessApplicationId", JsonTreeNode.FromString(g.App.Id)),
            ("applicationName", JsonTreeNode.FromString(AppName(g.App))),
            ("ownerRole", s.LabelledKey("owner-role", g.Role)),
            ("missing", JsonTreeNode.FromString(g.Missing)),
            ("holderDirectoryState", Js.Str(g.HolderDirectoryState)),
            ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("business-application", g.App.Id))))));

        // What waits on it now, and what its people answered through it lately.
        var waiting = world.Pending.Where(p => WaitsOn(p, team))
            .OrderBy(p => p.Record.Text("expiresOn") ?? "~", StringComparer.Ordinal).ThenBy(p => p.Record.Text("requestedOn") ?? "~", StringComparer.Ordinal).ThenBy(p => p.Record.Id, StringComparer.Ordinal)
            .ToList();
        var memberIds = members == null ? null : new HashSet<string>(members.Select(m => m.ObjectId), StringComparer.OrdinalIgnoreCase);
        int hiddenAnswers = 0;
        var answered = new List<PendingAsk>();
        foreach (StoredRow record in s.Query("approval-record", ("outcomeMeaning", "approved")).Concat(s.Query("approval-record", ("outcomeMeaning", "rejected"))))
        {
            if (record.Text("approver") == null || record.Text("respondedOn") == null
                || (record.Text("approverPrincipalKind") is string approverKind && approverKind != "user"))
            {
                continue;
            }

            StoredRow? decision = s.Rows.Get("decision", record.Text("decision") ?? string.Empty);
            if (decision == null)
            {
                continue;
            }

            var ask = new PendingAsk(record, decision, ApplicationOf(s, decision));
            if (!WaitsOn(ask, team) || (memberIds != null && !memberIds.Contains(record.Text("approver")!)))
            {
                continue;
            }

            if (HidesIntake(s, decision))
            {
                hiddenAnswers++;
                continue;
            }

            answered.Add(ask);
        }

        s.HideFromCaller("approval-record", world.Hidden.Count(h => WaitsOn(h, team)) + hiddenAnswers);
        var recent = answered.OrderByDescending(a => a.Record.Text("respondedOn") ?? string.Empty, StringComparer.Ordinal).ThenBy(a => a.Record.Id, StringComparer.Ordinal).Take(RecentAnswerLimit).ToList();

        // What its members are asked by name, and what they own in their own name (product review, fix round 1): a committee page
        // said "Nothing waits on this team now" while its data governance lead had three requests asked of her by name.
        var onMembers = new List<(PendingAsk Ask, string Member)>();
        var owned = new List<(string Member, StoredRow Row, string Kind, string Id, string Label)>();
        if (memberIds != null && memberIds.Count > 0)
        {
            var memberAssignments = s.Rows.Query(RowQuery.For("owner-assignment"))
                .Where(r => r.Text("principal") is string p && memberIds.Contains(p) && InForce(s, r))
                .ToList();
            foreach (PendingAsk ask in world.Pending.Where(p => !WaitsOn(p, team)))
            {
                string? member = AskedMember(ask, memberIds, memberAssignments);
                if (member != null)
                {
                    onMembers.Add((ask, member));
                }
            }

            var dataTree = new Product.Engine.Core.Operations.DataTree(s.Snapshot);
            foreach (StoredRow a in memberAssignments)
            {
                string member = a.Text("principal")!;
                if (a.Text("businessApplication") is string appId)
                {
                    StoredRow? app = s.Rows.Get("business-application", appId);
                    if (app != null && app.Text("statusMeaning") != "archived")
                    {
                        owned.Add((member, a, "business-application", app.Id, AppName(app)));
                    }
                }
                else if (a.Text("subjectDataDomain") is string domain)
                {
                    owned.Add((member, a, "data-domain", domain, dataTree.Domain(domain) == null ? domain : dataTree.PathText(domain)));
                }
                else if (a.Text("subjectDataLocation") is string location)
                {
                    owned.Add((member, a, "data-location", location, s.Rows.Get("data-location", location)?.Text("labelAsSeen") ?? "Unnamed data location"));
                }
            }
        }

        string MemberName(string objectId) => members?.FirstOrDefault(m => Same(m.ObjectId, objectId))?.DisplayLabel ?? objectId;
        var onMembersOrdered = onMembers
            .OrderBy(p => p.Ask.Record.Text("expiresOn") ?? "~", StringComparer.Ordinal).ThenBy(p => p.Ask.Record.Text("requestedOn") ?? "~", StringComparer.Ordinal).ThenBy(p => p.Ask.Record.Id, StringComparer.Ordinal)
            .ToList();
        var ownedOrdered = owned
            .OrderBy(o => MemberName(o.Member), StringComparer.OrdinalIgnoreCase).ThenBy(o => o.Kind == "business-application" ? 0 : 1).ThenBy(o => o.Label, StringComparer.OrdinalIgnoreCase).ThenBy(o => o.Row.Text("ownerRole"), StringComparer.Ordinal)
            .ToList();

        JsonTreeNode data = Js.Obj(
            ("team", Js.Obj(("objectId", JsonTreeNode.FromString(team.ObjectId)), ("displayLabel", JsonTreeNode.FromString(team.Label)))),
            ("kinds", Js.Strs(team.Kinds)),
            ("directoryState", Js.Str(team.Reference?.Text("directoryState"))),
            ("isKnownToProduct", JsonTreeNode.FromBoolean(team.Reference != null)),
            ("members", members == null ? JsonTreeNode.Null : Arr(members.Select(m => Js.Obj(
                ("objectId", JsonTreeNode.FromString(m.ObjectId)),
                ("displayLabel", JsonTreeNode.FromString(m.DisplayLabel)),
                ("principalKind", JsonTreeNode.FromString(m.PrincipalKind)))))),
            ("membersReadFrom", JsonTreeNode.FromString(readFrom)),
            ("membership", JsonTreeNode.FromString(membership)),
            ("roles", Arr(roles)),
            ("decisionTypes", decisionTypes),
            ("applications", applications),
            ("dataAssignments", Arr(dataAssignments)),
            ("ownerGaps", ownerGaps),
            ("waiting", Arr(waiting.Select(p => TeamAsk(s, p)))),
            ("waitingOnMembers", Arr(onMembersOrdered.Select(p => Js.Obj(("member", s.PrincipalRef(p.Member)), ("request", TeamAsk(s, p.Ask)))))),
            ("membersOwn", Arr(ownedOrdered.Take(MembersOwnLimit).Select(o => Js.Obj(
                ("member", s.PrincipalRef(o.Member)),
                ("ownerRole", s.LabelledKey("owner-role", o.Row.Text("ownerRole"))),
                ("isDeputy", JsonTreeNode.FromBoolean(o.Row.Flag("isDeputy"))),
                ("subjectKind", JsonTreeNode.FromString(o.Kind)),
                ("subjectId", JsonTreeNode.FromString(o.Id)),
                ("label", JsonTreeNode.FromString(o.Label)),
                ("recordUrl", JsonTreeNode.FromString(s.RecordUrl(o.Kind, o.Id))))))),
            ("recentAnswers", Arr(recent.Select(a => TeamAsk(s, a)))),
            ("counts", Js.Obj(
                ("members", members == null ? JsonTreeNode.Null : JsonTreeNode.FromInt64(people)),
                ("roles", JsonTreeNode.FromInt64(team.RoleKeys.Count)),
                ("decisionTypes", JsonTreeNode.FromInt64(answers.Count)),
                ("applications", JsonTreeNode.FromInt64(apps.Count)),
                ("assignments", JsonTreeNode.FromInt64(team.Assignments.Count)),
                ("waiting", JsonTreeNode.FromInt64(waiting.Count)),
                ("waitingOnMembers", JsonTreeNode.FromInt64(onMembersOrdered.Count)),
                ("membersOwn", JsonTreeNode.FromInt64(ownedOrdered.Count)),
                ("answered", JsonTreeNode.FromInt64(answered.Count)),
                ("ownerGaps", JsonTreeNode.FromInt64(gaps.Count)))));

        var summary = new List<string>
        {
            team.Label + ": " + KindPhrase(team.Kinds) + ".",
        };
        if (team.RoleKeys.Count > 0)
        {
            summary.Add("It holds " + string.Join(" and ", team.RoleKeys.Select(r => s.Label("role", r))) + (answers.Count == 0 ? "." : ", and answers " + Count(answers.Count, "decision type", "decision types") + "."));
        }

        summary.Add(members == null ? (readFrom == "not-read" ? "Membership is managed in Entra." : "Its members are not listed now.") : Count(people, "member", "members") + (readFrom == "demo-personas" ? " among the demo's fictional people." : ", read live from Entra."));
        summary.Add((waiting.Count == 0 ? "Nothing waits on it now" : Count(waiting.Count, "approval request waits", "approval requests wait") + " on it now")
            + (onMembersOrdered.Count == 0 ? "." : ", and " + Count(onMembersOrdered.Count, "request is", "requests are") + " asked of its members by name."));
        if (team.Assignments.Count > 0)
        {
            int domains = team.Assignments.Where(a => a.Text("businessApplication") == null).Select(a => a.Text("subjectDataDomain")).Where(d => d != null).Distinct(StringComparer.Ordinal).Count();
            int locations = team.Assignments.Where(a => a.Text("businessApplication") == null && a.Text("subjectDataDomain") == null).Select(a => a.Text("subjectDataLocation")).Where(l => l != null).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var held = new List<string>();
            if (apps.Count > 0)
            {
                held.Add(Count(apps.Count, "application", "applications"));
            }

            if (domains > 0)
            {
                held.Add(Count(domains, "data domain", "data domains"));
            }

            if (locations > 0)
            {
                held.Add(Count(locations, "data location", "data locations"));
            }

            summary.Add("It holds " + Count(team.Assignments.Count, "owner role", "owner roles") + (held.Count == 0 ? string.Empty : " on " + string.Join(" and ", held))
                + (gaps.Count == 0 ? "." : ", and its applications have " + Count(gaps.Count, "owner gap", "owner gaps") + "."));
        }

        return s.Answer(data, string.Join(" ", summary), s.RecordUrl("team", team.ObjectId));
    }

    // ---- Shared by both reads ----

    // The name of a team the product holds no name for, from the directory, for the rows a call shows only (security review, wave 4:
    // TeamWorld.Read names every team without the directory, so one read never turns into a Graph call per team).
    private static void ResolveLabel(ToolScope s, TeamFacts team)
    {
        if (team.Label == team.ObjectId && team.RoleKeys.Count > 0 && s.Options.Directory != null)
        {
            team.Label = GroupLabel(s, s.Options.Directory, team.RoleKeys[0], null, team.ObjectId);
        }
    }

    private const int MembersOwnLimit = 200;

    // Where an owner role is held on data (C32 Owner Subject).
    private static readonly string[] DataOwnerSubjects = { "data-domain", "parent-data-domain", "data-location", "touched-data-domains" };

    // An owner assignment in force today (O06: started, not ended).
    private static bool InForce(ToolScope s, StoredRow r)
    {
        DateTime day = s.Now.Date;
        return (Js.ParseMoment(r.Text("startOn")) ?? DateTime.MinValue).Date <= day
            && (r.Text("endOn") == null || (Js.ParseMoment(r.Text("endOn")) ?? DateTime.MaxValue).Date > day);
    }

    // The member a pending request asks, when it asks one of them: by name, or as the holder of the owner role it asks
    // on the decision's application or data domain. Null when it asks none of them.
    private static string? AskedMember(PendingAsk ask, HashSet<string> memberIds, List<StoredRow> memberAssignments)
    {
        if (ask.Record.Text("askedPrincipal") is string asked)
        {
            return memberIds.Contains(asked) ? memberIds.First(m => Same(m, asked)) : null;
        }

        if (ask.Record.Text("requiredOwnerRole") is not string ownerRole)
        {
            return null;
        }

        string? domain = ask.Decision.Text("subjectDataDomain");
        StoredRow? holder = memberAssignments
            .Where(a => a.Text("ownerRole") == ownerRole)
            .FirstOrDefault(a => (ask.ApplicationId != null && Same(a.Text("businessApplication"), ask.ApplicationId))
                || (domain != null && a.Text("subjectDataDomain") == domain));
        return holder?.Text("principal");
    }

    // Where the members come from on this install: the same source get_role_access names for the holders (holdersReadFrom).
    private static string MembersReadFrom(ToolScope s)
    {
        return s.Options.Directory == null ? "not-read" : s.Options.Directory is IDemoDirectory ? "demo-personas" : "entra-live";
    }

    // The product roles whose holders answer decisions: an active Approval Rule asks the role for its answer (to approve,
    // or as a consultation), or an active Decision Right gives it the responsible, accountable or consulted letter. Read
    // from the configuration in force, so an adopter's own rules decide which groups are committees. Under Balanced these
    // are the architect, change manager, change board, PMO, AI lead, security reviewer, platform admin, configuration admin
    // and data governance lead; the requester, maker, application owner, data owner (who answers as the owner of a
    // domain, a person), auditor and reader answer no decision through their role.
    private static HashSet<string> AnsweringRoleKeys(ToolScope s)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (SnapshotRow rule in s.ActiveRows("approval-rule"))
        {
            if (Js.Text(rule.Columns, "approverRoleKey") is string role)
            {
                keys.Add(role);
            }
        }

        foreach (SnapshotRow right in s.ActiveRows("decision-right").Where(r => DecidingLetters.Contains(Js.Text(r.Columns, "letter") ?? string.Empty, StringComparer.Ordinal)))
        {
            if (Js.Text(right.Columns, "roleKey") is string role)
            {
                keys.Add(role);
            }
        }

        return keys;
    }

    // The registered and draft applications a team holds a current owner role on, by name.
    private static List<StoredRow> TeamApplications(ToolScope s, TeamFacts team)
    {
        return team.Assignments
            .Select(a => a.Text("businessApplication"))
            .Where(a => a != null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(a => s.Rows.Get("business-application", a!))
            .Where(a => a != null && a.Text("statusMeaning") != "archived")
            .Select(a => a!)
            .OrderBy(a => AppName(a), StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Id, StringComparer.Ordinal)
            .ToList();
    }

    // A pending request waits on a team as it would wait on a person (AskedOfMe): a request that names someone is theirs
    // alone, so it waits on the team only when it names the team; else it waits on the team when the team holds the
    // product role it asks, or holds now the owner role it asks on the decision's application (a deputy too). The same
    // test tells which answers came through the team.
    private static bool WaitsOn(PendingAsk ask, TeamFacts team)
    {
        if (ask.Record.Text("askedPrincipal") is string asked)
        {
            return Same(asked, team.ObjectId);
        }

        return (ask.Record.Text("requiredRole") is string role && team.RoleKeys.Contains(role, StringComparer.Ordinal))
            || (ask.Record.Text("requiredOwnerRole") is string ownerRole && ask.ApplicationId != null
                && team.Assignments.Any(a => Same(a.Text("businessApplication"), ask.ApplicationId) && a.Text("ownerRole") == ownerRole));
    }

    // One request on a team page: the decision with its number and link, what it is about, and what it asks.
    private static JsonTreeNode TeamAsk(ToolScope s, PendingAsk ask)
    {
        StoredRow r = ask.Record;
        StoredRow d = ask.Decision;
        (string label, string? appId, string? appName) = DecisionSubjectLabel(s, d);
        DateTime? expires = Js.ParseMoment(r.Text("expiresOn"));
        return Js.Obj(
            ("approvalRecordId", JsonTreeNode.FromString(r.Id)),
            ("decision", Js.Obj(
                ("decisionId", JsonTreeNode.FromString(d.Id)),
                ("recordNumber", Js.Str(d.Text("recordNumber"))),
                ("decisionType", s.LabelledKey("decision-type", d.Text("decisionType") ?? "unknown")),
                ("statement", JsonTreeNode.FromString(d.Text("statement") ?? string.Empty)),
                ("status", s.StatusValue(d) is JsonTreeNode st && st.Kind != JsonTreeKind.Null ? st : ToolScope.StatusValue("proposed", null)))),
            ("subject", Js.Obj(
                ("subjectKind", JsonTreeNode.FromString(d.Text("subjectKind") ?? "business-application")),
                ("label", JsonTreeNode.FromString(label)),
                ("businessApplicationId", Js.Str(appId)),
                ("applicationName", Js.Str(appName)))),
            ("askedRole", s.LabelledKey("role", r.Text("requiredRole"))),
            ("askedOwnerRole", s.LabelledKey("owner-role", r.Text("requiredOwnerRole"))),
            ("isConsultation", JsonTreeNode.FromBoolean(r.Flag("isConsultation"))),
            ("outcome", s.StatusValue(r, "outcome", "outcomeMeaning") is JsonTreeNode o && o.Kind != JsonTreeKind.Null ? o : ToolScope.StatusValue("pending", null)),
            ("approver", s.PrincipalRef(r.Text("approver"))),
            ("requestedOn", ToolScope.MomentOrNull(r.Text("requestedOn"))),
            ("respondedOn", ToolScope.MomentOrNull(r.Text("respondedOn"))),
            ("expiresOn", ToolScope.MomentOrNull(r.Text("expiresOn"))),
            ("isExpired", JsonTreeNode.FromBoolean(r.Text("outcomeMeaning") == "pending" && expires.HasValue && expires.Value <= s.Now)),
            ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("decision", d.Id))));
    }

    private static JsonTreeNode TeamAssignment(ToolScope s, StoredRow a)
    {
        return Js.Obj(
            ("ownerAssignmentId", JsonTreeNode.FromString(a.Id)),
            ("ownerRole", s.LabelledKey("owner-role", a.Text("ownerRole"))),
            ("isDeputy", JsonTreeNode.FromBoolean(a.Flag("isDeputy"))),
            ("startOn", ToolScope.DateOrNull(a.Text("startOn"))));
    }

    // "2 committees, 4 support teams and 6 role groups"; a kind with none is left out.
    private static string KindWords(int committees, int support, int roleGroups, int other)
    {
        var parts = new List<string>();
        if (committees > 0)
        {
            parts.Add(Count(committees, "committee", "committees"));
        }

        if (support > 0)
        {
            parts.Add(Count(support, "support team", "support teams"));
        }

        if (roleGroups > 0)
        {
            parts.Add(Count(roleGroups, "role group", "role groups"));
        }

        if (other > 0)
        {
            parts.Add(Count(other, "other group", "other groups"));
        }

        return parts.Count <= 1 ? string.Join(string.Empty, parts) : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[parts.Count - 1];
    }

    private static string KindLabel(string kind, bool plural)
    {
        return kind switch
        {
            "committee" => plural ? "committees" : "a committee",
            "support-team" => plural ? "support teams" : "a support team",
            "role-group" => plural ? "role groups" : "a role group",
            _ => plural ? "other groups" : "another group",
        };
    }

    // "a committee and a support team".
    private static string KindPhrase(IReadOnlyList<string> kinds)
    {
        var words = kinds.Select(k => KindLabel(k, false)).ToList();
        return words.Count <= 1 ? string.Join(string.Empty, words) : string.Join(", ", words.Take(words.Count - 1)) + " and " + words[words.Count - 1];
    }

    // What a team's roles hold on one decision type: the decision right letters, and whether an approval rule asks it to
    // approve or consults it.
    private sealed class TypeAnswer
    {
        public SortedSet<string> Letters { get; } = new(StringComparer.Ordinal);

        public SortedSet<string> Roles { get; } = new(StringComparer.Ordinal);

        public bool Approves { get; set; }

        public bool Consulted { get; set; }

        // The owner role its people answer the type through, as named holders on their own data.
        public string? ThroughOwnerRole { get; set; }
    }

    // A pending or answered request, with its decision and the decision's application.
    private sealed class PendingAsk
    {
        public PendingAsk(StoredRow record, StoredRow decision, string? applicationId)
        {
            Record = record;
            Decision = decision;
            ApplicationId = applicationId;
        }

        public StoredRow Record { get; }

        public StoredRow Decision { get; }

        public string? ApplicationId { get; }
    }

    // One team as the two reads see it: its group, its role bindings, its current owner assignments and its kinds.
    private sealed class TeamFacts
    {
        public TeamFacts(string objectId, string label, StoredRow? reference, List<SnapshotRow> bindings, List<StoredRow> assignments, List<string> kinds)
        {
            ObjectId = objectId;
            Label = label;
            Reference = reference;
            Bindings = bindings;
            RoleKeys = bindings.Select(b => Js.Text(b.Columns, "roleKey")).Where(r => r != null).Select(r => r!).Distinct(StringComparer.Ordinal).ToList();
            Assignments = assignments;
            Kinds = kinds;
        }

        public string ObjectId { get; }

        // The stored or demo name; a live directory's name only once ResolveLabel looked it up for a row shown.
        public string Label { get; set; }

        public StoredRow? Reference { get; }

        public List<SnapshotRow> Bindings { get; }

        public List<string> RoleKeys { get; }

        public List<StoredRow> Assignments { get; }

        public List<string> Kinds { get; }
    }

    // Every team the install knows, and the pending requests, read once per call.
    private sealed class TeamWorld
    {
        private TeamWorld(List<TeamFacts> teams, List<PendingAsk> pending, List<PendingAsk> hidden)
        {
            Teams = teams;
            Pending = pending;
            Hidden = hidden;
        }

        public List<TeamFacts> Teams { get; }

        // The pending approval requests the caller may read.
        public List<PendingAsk> Pending { get; }

        // Pending requests on an intake request the caller may not read (intake-visibility): counted, never shown.
        public List<PendingAsk> Hidden { get; }

        public static TeamWorld Read(ToolScope s)
        {
            // The groups the product holds a reference to, and the groups a role is bound to.
            var references = new Dictionary<string, StoredRow>(StringComparer.OrdinalIgnoreCase);
            foreach (StoredRow row in s.Query("principal-reference", ("principalKind", "group")))
            {
                if (!row.Flag("isErased") && row.Text("entraObjectId") is string id && !references.ContainsKey(id))
                {
                    references[id] = row;
                }
            }

            var bindings = s.ActiveRows("role-group-binding")
                .Where(b => Js.Text(b.Columns, "entraGroupObjectId") != null && Js.Text(b.Columns, "roleKey") != null)
                .OrderBy(b => b.KeyPath, StringComparer.Ordinal)
                .GroupBy(b => Js.Text(b.Columns, "entraGroupObjectId")!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            var ids = new HashSet<string>(references.Keys, StringComparer.OrdinalIgnoreCase);
            ids.UnionWith(bindings.Keys);

            // Their owner assignments in force today (O06: started, not ended).
            DateTime day = s.Now.Date;
            var assignments = s.Rows.Query(RowQuery.For("owner-assignment"))
                .Where(r => r.Text("principal") is string p && ids.Contains(p))
                .Where(r => (Js.ParseMoment(r.Text("startOn")) ?? DateTime.MinValue).Date <= day)
                .Where(r => r.Text("endOn") == null || (Js.ParseMoment(r.Text("endOn")) ?? DateTime.MaxValue).Date > day)
                .GroupBy(r => r.Text("principal")!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderBy(r => r.Id, StringComparer.Ordinal).ToList(), StringComparer.OrdinalIgnoreCase);

            HashSet<string> answering = AnsweringRoleKeys(s);
            var teams = new List<TeamFacts>();
            foreach (string id in ids)
            {
                // The reference's own spelling of the object ID, else the binding's.
                string objectId = references.TryGetValue(id, out StoredRow? reference) ? reference.Text("entraObjectId")! : Js.Text(bindings[id][0].Columns, "entraGroupObjectId")!;
                List<SnapshotRow> bound = bindings.TryGetValue(id, out List<SnapshotRow>? b) ? b : new List<SnapshotRow>();
                List<StoredRow> held = assignments.TryGetValue(id, out List<StoredRow>? a) ? a : new List<StoredRow>();
                var roleKeys = bound.Select(r => Js.Text(r.Columns, "roleKey")!).Distinct(StringComparer.Ordinal).ToList();
                var kinds = new List<string>();
                if (roleKeys.Any(answering.Contains))
                {
                    kinds.Add("committee");
                }

                if (held.Any(r => SupportOwnerRoleKeys.Contains(r.Text("ownerRole") ?? string.Empty, StringComparer.Ordinal)))
                {
                    kinds.Add("support-team");
                }

                if (roleKeys.Count > 0 && !kinds.Contains("committee"))
                {
                    kinds.Add("role-group");
                }

                if (kinds.Count == 0)
                {
                    kinds.Add("other");
                }

                // No directory read here: the stored name, or the demo's name for a role group (security review, wave 4).
                string label = roleKeys.Count > 0
                    ? GroupLabel(s, s.Options.Directory is IDemoDirectory ? s.Options.Directory : null, roleKeys[0], null, objectId)
                    : reference?.Text("displayLabel") is string stored && stored.Length > 0 ? stored : "Unnamed group";
                teams.Add(new TeamFacts(objectId, label, reference, bound, held, kinds));
            }

            // The pending requests, each with its decision and application. A request on an intake the caller may not read
            // is counted and left out, as list_approvals does. Their decisions and the requests and changes those are
            // about come in a few key set reads (QA speed, fix round 1: one decision read per request).
            var pending = new List<PendingAsk>();
            var hidden = new List<PendingAsk>();
            IReadOnlyList<StoredRow> records = s.Query("approval-record", ("outcomeMeaning", "pending"));
            s.Call.PrefetchIds("decision", records.Select(r => r.Text("decision")));
            List<StoredRow> decisions = records.Select(r => s.Rows.Get("decision", r.Text("decision") ?? string.Empty)).Where(d => d != null).Select(d => d!).ToList();
            s.Call.PrefetchIds("intake-request", decisions.Select(d => d.Text("subjectIntakeRequest")));
            s.Call.PrefetchIds("change-request", decisions.Select(d => d.Text("subjectChangeRequest")));
            s.Call.PrefetchIds("business-application", assignments.Values.SelectMany(a => a).Select(a => a.Text("businessApplication")));
            foreach (StoredRow record in records)
            {
                StoredRow? decision = s.Rows.Get("decision", record.Text("decision") ?? string.Empty);
                if (decision == null)
                {
                    continue;
                }

                var ask = new PendingAsk(record, decision, ApplicationOf(s, decision));
                (HidesIntake(s, decision) ? hidden : pending).Add(ask);
            }

            return new TeamWorld(teams, pending, hidden);
        }
    }
}
