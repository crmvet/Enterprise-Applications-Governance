using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Snapshots;
using Product.Engine.Runtime;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Json;
using Product.Mcp.Core.Policy;

namespace Product.Mcp.Core.Backend.InProcess;

// Tools 89 and 92 (document 24, B24-18): the Roles and access screen and the Admin screen.
// - get_role_access: who holds each product role and through which group binding (people only through groups, D-11;
//   workloads only through a Workload Grant), what each role may do (its Tool Policy rows, decision rights and approval
//   rules) and the separation rules. Any role reads it: every part is a configuration row, which all roles read (document
//   26, section 5.3), and the holders come from Entra as the signed-in person, as get_role_holders reads them.
// - get_admin_status: the Tool Policy as a readable list, the sanctioned clients and what each did recently, the host
//   settings that are safe to show, the configuration in force, the reminders and notices the daily refresh sent, the
//   approval outbox, and whether the caller may run the refresh now. The platform admin, the configuration admin and the
//   auditor read it; the notices name people, so only the platform admin and the auditor see them listed (document 26,
//   B26-11: the configuration admin reads no audit entry).
internal static partial class ToolHandlers
{
    // ---- 89. get_role_access ----

    private static JsonTreeNode GetRoleAccess(ToolScope s)
    {
        string? only = s.Text("roleKey");
        if (only != null)
        {
            s.RequireKey("role", only, "roleKey");
        }

        bool holders = s.Flag("includeHolders") ?? only != null;
        IDirectoryReader? directory = holders ? s.Options.Directory : null;
        if (holders && directory == null)
        {
            s.Call.Warn("roles.holders-not-read", "Members are read from Entra, and this install cannot read them now, so the holders of each role are not listed.", "Role", only);
        }

        ToolPolicySet policy = s.Session.ToolPolicy;
        var tools = s.Contracts.Tools.Where(t => t.IsR1 && policy.IsServed(t)).OrderBy(t => t.Number).ToList();
        var roles = s.ActiveRows("role").Where(r => only == null || r.KeyPath == only).OrderBy(r => RoleOrder(s, r.KeyPath)).ThenBy(r => r.KeyPath, StringComparer.Ordinal).ToList();
        int notRead = 0;
        var items = new List<JsonTreeNode>();
        foreach (SnapshotRow role in roles)
        {
            string key = role.KeyPath;
            bool workload = Js.Flag(role.Columns, "isWorkloadRole") == true;
            var bindings = s.ActiveRows("role-group-binding").Where(b => Js.Text(b.Columns, "roleKey") == key).OrderBy(b => b.KeyPath, StringComparer.Ordinal).ToList();
            JsonTreeNode holderList = JsonTreeNode.Null;
            if (!workload && directory != null)
            {
                var members = new SortedDictionary<string, JsonTreeNode>(StringComparer.OrdinalIgnoreCase);
                bool answered = true;
                foreach (SnapshotRow binding in bindings)
                {
                    string? group = Js.Text(binding.Columns, "entraGroupObjectId");
                    DirectoryAnswer<IReadOnlyList<DirectoryObject>> answer = group == null ? DirectoryAnswer<IReadOnlyList<DirectoryObject>>.Of(Array.Empty<DirectoryObject>()) : directory.GroupMembers(s.Session.Caller, group);
                    if (!answer.Answered)
                    {
                        answered = false;
                        break;
                    }

                    foreach (DirectoryObject member in answer.Value.Where(m => string.Equals(m.PrincipalKind, "user", StringComparison.Ordinal)))
                    {
                        members[member.ObjectId] = Js.Obj(
                            ("objectId", JsonTreeNode.FromString(member.ObjectId)),
                            ("displayLabel", JsonTreeNode.FromString(member.DisplayLabel)),
                            ("viaGroup", JsonTreeNode.FromString(group!)));
                    }
                }

                if (answered)
                {
                    holderList = Arr(members.Values);
                }
                else
                {
                    notRead++;
                }
            }

            var grants = workload
                ? s.ActiveRows("workload-grant").Where(g => Js.Text(g.Columns, "workloadRoleKey") == key).OrderBy(g => g.KeyPath, StringComparer.Ordinal).Select(g =>
                {
                    DateTime? expires = Js.Moment(g.Columns, "expiresOn");
                    return Js.Obj(
                        ("principal", s.PrincipalRef(Js.Text(g.Columns, "principalObjectId"))),
                        ("reason", Js.Str(Js.Text(g.Columns, "reason"))),
                        ("expiresOn", expires.HasValue ? Js.Date(expires.Value) : JsonTreeNode.Null),
                        ("isExpired", JsonTreeNode.FromBoolean(!expires.HasValue || s.Now >= expires.Value)));
                }).ToList()
                : new List<JsonTreeNode>();

            // A workload role calls no MCP tool (it records through the record endpoint under its grant).
            var mayCall = workload ? new List<JsonTreeNode>() : tools.Where(t => t.AnyoneSignedIn || (policy.TryGet(t.Name, out ToolPolicyRow row) && row.AllowedRoleKeys.Contains(key, StringComparer.Ordinal))).Select(t => ToolLine(s, policy, t)).ToList();
            var rights = s.ActiveRows("decision-right").Where(d => Js.Text(d.Columns, "roleKey") == key).OrderBy(d => Js.Text(d.Columns, "decisionTypeKey") ?? string.Empty, StringComparer.Ordinal).ThenBy(d => d.KeyPath, StringComparer.Ordinal)
                .Select(d => Js.Obj(
                    ("decisionRight", s.LabelledKey("decision-right", d.KeyPath)),
                    ("decisionType", s.LabelledKey("decision-type", Js.Text(d.Columns, "decisionTypeKey") ?? "unknown")),
                    ("letter", JsonTreeNode.FromString(Js.Text(d.Columns, "letter") ?? "responsible")),
                    ("scope", JsonTreeNode.FromString(ScopeWords(s, d.Columns))))).ToList();
            var approvals = s.ActiveRows("approval-rule").Where(a => Js.Text(a.Columns, "approverRoleKey") == key).OrderBy(a => Js.Text(a.Columns, "decisionTypeKey") ?? string.Empty, StringComparer.Ordinal).ThenBy(a => a.KeyPath, StringComparer.Ordinal)
                .Select(a => Js.Obj(
                    ("approvalRule", s.LabelledKey("approval-rule", a.KeyPath)),
                    ("decisionType", s.LabelledKey("decision-type", Js.Text(a.Columns, "decisionTypeKey") ?? "unknown")),
                    ("isConsultation", JsonTreeNode.FromBoolean(Js.Flag(a.Columns, "isConsultation") == true)),
                    ("scope", JsonTreeNode.FromString(ScopeWords(s, a.Columns))),
                    ("guidelineRef", GuidelineRef(s, Js.Text(a.Columns, "guidelineKey"))))).ToList();
            var separation = s.ActiveRows("separation-rule").Where(r => NamesRole(r, key)).OrderBy(r => r.KeyPath, StringComparer.Ordinal).Select(r => s.LabelledKey("separation-rule", r.KeyPath)).ToList();

            items.Add(Js.Obj(
                ("role", s.LabelledKey("role", key)),
                ("party", s.LabelledKey("party", Js.Text(role.Columns, "partyKey"))),
                ("duties", Js.Str(Js.Text(role.Columns, "duties"))),
                ("isWorkloadRole", JsonTreeNode.FromBoolean(workload)),
                ("isReadOnly", JsonTreeNode.FromBoolean(Js.Flag(role.Columns, "isReadOnly") == true)),
                ("canChangeConfiguration", JsonTreeNode.FromBoolean(Js.Flag(role.Columns, "canChangeConfiguration") == true)),
                ("heldByMe", JsonTreeNode.FromBoolean(s.HoldsRole(key))),
                ("bindings", Arr(bindings.Select(b =>
                {
                    string? group = Js.Text(b.Columns, "entraGroupObjectId");
                    string? method = Js.Text(b.Columns, "bindingMethod");
                    return Js.Obj(
                        ("group", group == null ? JsonTreeNode.Null : Js.Obj(("objectId", JsonTreeNode.FromString(group)), ("displayLabel", JsonTreeNode.FromString(GroupLabel(s, s.Options.Directory, key, null, group))))),
                        ("bindingMethod", method == "dataverse-group-team" || method == "entra-app-role-assignment" ? JsonTreeNode.FromString(method) : JsonTreeNode.Null),
                        ("lastVerifiedOn", ToolScope.MomentOrNull(Js.Text(b.Columns, "lastVerifiedOn"))));
                }))),
                ("holders", holderList),
                ("workloadGrants", Arr(grants)),
                ("tools", Arr(mayCall)),
                ("decisionRights", Arr(rights)),
                ("approvals", Arr(approvals)),
                ("separationRules", Arr(separation))));
        }

        s.HideFromCaller("role-holders", notRead);
        var rules = s.ActiveRows("separation-rule").OrderBy(r => r.KeyPath, StringComparer.Ordinal).Select(r => Js.Obj(
            ("separationRule", s.LabelledKey("separation-rule", r.KeyPath)),
            ("ruleKind", JsonTreeNode.FromString(Js.Text(r.Columns, "ruleKind") ?? "approver-differs")),
            ("enforcement", JsonTreeNode.FromString(Js.Text(r.Columns, "enforcement") ?? "refuse")),
            ("words", JsonTreeNode.FromString(SeparationWords(s, r))))).ToList();
        JsonTreeNode data = Js.Obj(
            ("roles", Arr(items)),
            ("separationRules", Arr(rules)),
            ("toolPolicySource", JsonTreeNode.FromString(policy.Source == ToolPolicySource.Configuration ? "configuration" : "shipped-seed")),
            // Where this install reads the holders, also on a read that lists none (wave 3 verification: the role list, where
            // the platform admin runs the binding check, said "against Entra" on a Demo-mode run because the list read
            // answered not-read). not-read only when the install cannot read them.
            ("holdersReadFrom", JsonTreeNode.FromString(s.Options.Directory == null ? "not-read" : s.Options.Directory is IDemoDirectory ? "demo-personas" : "entra-live")),
            ("myRoleKeys", Js.Strs(s.Session.RoleKeys.OrderBy(r => r, StringComparer.Ordinal))));
        int bound = roles.Count(r => s.ActiveRows("role-group-binding").Any(b => Js.Text(b.Columns, "roleKey") == r.KeyPath));
        string summary = only != null
            ? s.Label("role", only) + ": " + Count(items.Count == 0 ? 0 : Js.Items(items[0], "tools").Count, "tool", "tools") + ", " + Count(Js.Items(items[0], "decisionRights").Count, "decision right", "decision rights") + ", " + Count(Js.Items(items[0], "approvals").Count, "approval rule", "approval rules") + "."
            : Count(roles.Count, "role", "roles") + ", " + bound + " bound to a group or held through grants; " + Count(rules.Count, "separation rule", "separation rules") + ". People hold a role only through its group.";
        return s.Answer(data, summary);
    }

    // One tool a role may call: its number, toolset, read or write, the entries that serve it, how far an AI client may take
    // a record through it, and the subject kinds an AI client may submit.
    private static JsonTreeNode ToolLine(ToolScope s, ToolPolicySet policy, ToolContract tool)
    {
        policy.TryGet(tool.Name, out ToolPolicyRow row);
        return Js.Obj(
            ("tool", JsonTreeNode.FromString(tool.Name)),
            ("number", JsonTreeNode.FromInt64(tool.Number)),
            ("toolset", JsonTreeNode.FromString(tool.ToolsetKey)),
            ("access", JsonTreeNode.FromString(tool.IsWrite ? "write" : "read")),
            ("personOnly", JsonTreeNode.FromBoolean(tool.IsPersonOnly)),
            ("maxAiState", JsonTreeNode.FromString(tool.IsPersonOnly ? "read-only" : policy.EffectiveMaxAiState(tool))),
            ("aiMaySubmit", Js.Strs(row == null ? Array.Empty<string>() : row.AiMaySubmit.Where(p => p.Value).Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal))),
            ("relations", Js.Strs(tool.Relations.Where(r => r != "any-role" && r != "anyone-signed-in"))));
    }

    // The scope a decision right or approval rule applies to, in words: "tier 3, production, agent". "All" when none.
    private static string ScopeWords(ToolScope s, JsonTreeNode columns)
    {
        var parts = new List<string>();
        void Add(string column, string entity)
        {
            if (Js.Text(columns, column) is string key && key != "*")
            {
                parts.Add(s.Label(entity, key));
            }
        }

        Add("tierKey", "tier");
        Add("lifecycleStageKey", "lifecycle-stage");
        Add("changeTypeKey", "change-type");
        Add("applicationKindKey", "application-kind");
        Add("targetStageKey", "target-stage");
        Add("classificationKey", "data-classification");
        Add("dataDomainLevelKey", "data-domain-level");
        if (Js.Text(columns, "subjectKind") is string subject)
        {
            parts.Add("about a " + subject.Replace('-', ' '));
        }

        if (Js.Flag(columns, "aiUse") == true)
        {
            parts.Add("reaching AI");
        }

        if (Js.Text(columns, "portfolioId") is string portfolio && s.Rows.Get("portfolio", portfolio) is StoredRow p)
        {
            parts.Add(p.Text("name") ?? portfolio);
        }

        return parts.Count == 0 ? "All" : string.Join(", ", parts);
    }

    private static bool NamesRole(SnapshotRow rule, string roleKey)
    {
        return Js.Text(rule.Columns, "mustDifferFromRoleKey") == roleKey || Js.Strings(rule.Columns, "roles").Contains(roleKey, StringComparer.Ordinal) || Js.Strings(rule.Columns, "roleKeys").Contains(roleKey, StringComparer.Ordinal);
    }

    // A separation rule in one sentence: "At tier 3, the approver is not the person who proposed it (refused)."
    private static string SeparationWords(ToolScope s, SnapshotRow rule)
    {
        string scope = ScopeWords(s, rule.Columns);
        string type = Js.Text(rule.Columns, "decisionTypeKey") is string t ? " on " + s.Label("decision-type", t).ToLowerInvariant() + " decisions" : string.Empty;
        string enforcement = Js.Text(rule.Columns, "enforcement") == "flag" ? "flagged" : "refused";
        if (Js.Text(rule.Columns, "ruleKind") == "roles-in-different-groups")
        {
            var roles = Js.Strings(rule.Columns, "roles").Concat(Js.Strings(rule.Columns, "roleKeys")).Select(r => s.Label("role", r)).ToList();
            return "The roles " + string.Join(" and ", roles) + " are held through different groups" + type + " (" + enforcement + " otherwise).";
        }

        string other = Js.Text(rule.Columns, "mustDifferFromOwnerRoleKey") is string ownerRole ? "the " + s.Label("owner-role", ownerRole).ToLowerInvariant()
            : Js.Text(rule.Columns, "mustDifferFromRoleKey") is string role ? "a holder of " + s.Label("role", role)
            : (Js.Text(rule.Columns, "mustDifferFrom") ?? "proposer") switch
            {
                "proposer" => "the person who proposed it",
                "deployer" => "the person who deploys it",
                "requester" => "the person who asked for it",
                string m => "the " + m.Replace('-', ' '),
            };
        return (scope == "All" ? "Everywhere" : "At " + scope) + type + ", the approver is not " + other + " (" + enforcement + ").";
    }

    // Product roles in the order of document 24's seed list, then any the install added.
    private static int RoleOrder(ToolScope s, string roleKey)
    {
        int at = Array.IndexOf(s.Contracts.HumanRoleKeys.ToArray(), roleKey);
        return at < 0 ? 1000 : at;
    }

    // ---- 92. get_admin_status ----

    private static readonly string[] NoticeReaders = { "platform-admin", "auditor" };

    private static JsonTreeNode GetAdminStatus(ToolScope s)
    {
        long days = Js.Number(s.Args, "days") ?? 7;
        DateTime since = s.Now.AddDays(-days);
        bool noticesListed = NoticeReaders.Any(s.HoldsRole);
        HostFacts? host = null;
        try
        {
            host = s.Options.HostFacts?.Invoke();
        }
        catch (Exception)
        {
            // A host fact that cannot be read leaves the host section empty; the rest of the page still answers.
            s.Call.Warn("admin.host-facts-unread", "The host's settings could not be read, so the host section is empty.", "Host", null);
        }

        // The configuration in force.
        var versions = s.Query("configuration-set-version", ("configurationSet", s.Call.Snapshot.ConfigurationSetKey));
        StoredRow? activeRow = versions.FirstOrDefault(v => v.Number("versionNumber") == s.Call.Snapshot.VersionNumber);
        DateTime publishedOn = activeRow == null ? s.InForcePublishedOn : s.PublishedOnOf(activeRow);
        long reviewDays = s.SettingNumber("configuration-review-days") ?? 0;
        int openDrafts = s.Rows.Query(RowQuery.For("configuration-draft")).Count(d => d.Text("draftStatus") is "draft" or "submitted");

        // The Tool Policy as a list.
        ToolPolicySet policy = s.Session.ToolPolicy;
        bool includePolicy = s.Flag("includeToolPolicy") ?? true;
        var policyRows = includePolicy ? s.Contracts.Tools.OrderBy(t => t.Number).Select(t => PolicyLine(policy, t)).ToList() : new List<JsonTreeNode>();

        // The sanctioned clients and what each did in the window: write calls and refused calls from the audit entries,
        // and rows an AI client drafted.
        IReadOnlyList<StoredRow> allEntries = s.Rows.Query(RowQuery.For("audit-entry"));
        var entries = allEntries.Where(e => (Js.ParseMoment(e.Text("occurredOn")) ?? DateTime.MinValue) >= since).ToList();
        var known = s.ActiveRows("known-client").ToList();
        var byClient = new Dictionary<string, ClientTally>(StringComparer.OrdinalIgnoreCase);
        ClientTally Tally(string label)
        {
            if (!byClient.TryGetValue(label, out ClientTally? tally))
            {
                byClient[label] = tally = new ClientTally(label);
            }

            return tally;
        }

        foreach (StoredRow entry in entries)
        {
            string operation = entry.Text("operation") ?? string.Empty;
            string? client = entry.Text("clientApp");
            if (client == null || (operation != "composed-call" && operation != "call-refused"))
            {
                continue;
            }

            ClientTally tally = Tally(client);
            tally.Calls++;

            // A refused call of a person's own client (the web app) counts here; an AI client's refusals come from the AI
            // activity list below.
            if (operation == "call-refused" && !AiActorKinds.Contains(entry.Text("actorKind") ?? string.Empty, StringComparer.Ordinal))
            {
                tally.Refused++;
            }

            DateTime? on = Js.ParseMoment(entry.Text("occurredOn"));
            if (on.HasValue && (!tally.LastCallOn.HasValue || on.Value > tally.LastCallOn.Value))
            {
                tally.LastCallOn = on;
            }
        }

        // Drafts and refusals come from the AI activity list of get_ai_activity, over the same window by day (wave 4b review;
        // D-41): the rows an AI client created and the calls refused to an AI client, each counted once, as the AI activity
        // page counts them for the same client and dates. Calls stay every write or refused call any client made.
        foreach (Activity activity in AiActivities(s, allEntries).All.Where(a => a.Client != null && a.On.HasValue && a.On.Value.Date >= since.Date))
        {
            if (activity.AiCreated)
            {
                Tally(activity.Client!).Drafts++;
            }

            if (activity.Kind == "refusal")
            {
                Tally(activity.Client!).Refused++;
            }
        }

        foreach (SnapshotRow row in known)
        {
            Tally(Js.Text(row.Columns, "label") ?? row.KeyPath).Known = row;
        }

        var clients = byClient.Values
            .OrderBy(c => c.Known == null ? 1 : 0).ThenByDescending(c => c.Calls).ThenBy(c => c.Label, StringComparer.OrdinalIgnoreCase)
            .Select(c => Js.Obj(
                ("key", Js.Str(c.Known?.KeyPath)),
                ("label", JsonTreeNode.FromString(c.Label)),
                ("isKnown", JsonTreeNode.FromBoolean(c.Known != null)),
                ("isAiClient", c.Known == null ? JsonTreeNode.Null : JsonTreeNode.FromBoolean(Js.Flag(c.Known.Columns, "isAiClient") == true)),
                ("isAllowed", c.Known == null ? JsonTreeNode.Null : JsonTreeNode.FromBoolean(Js.Flag(c.Known.Columns, "isAllowed") != false)),
                ("calls", JsonTreeNode.FromInt64(c.Calls)),
                ("refused", JsonTreeNode.FromInt64(c.Refused)),
                ("drafts", JsonTreeNode.FromInt64(c.Drafts)),
                ("lastCallOn", c.LastCallOn.HasValue ? Js.Utc(c.LastCallOn.Value) : JsonTreeNode.Null))).ToList();

        // What the daily refresh sent in the window: counts for every reader, the notices themselves (they name people) for
        // the platform admin and the auditor.
        var notices = entries.Where(e => e.Text("operation") is EngineRuntime.ReminderSentOperation or EngineRuntime.FlagNoticeSentOperation).OrderByDescending(e => e.Text("occurredOn") ?? string.Empty, StringComparer.Ordinal).ThenBy(e => e.Id, StringComparer.Ordinal).ToList();
        DateTime? lastRefresh = null;
        foreach (StoredRow refresh in s.Rows.Query(RowQuery.For("audit-entry").Where("operation", EngineRuntime.DueStateRefreshedOperation)))
        {
            DateTime? on = Js.ParseMoment(refresh.Text("occurredOn"));
            if (on.HasValue && (!lastRefresh.HasValue || on.Value > lastRefresh.Value))
            {
                lastRefresh = on;
            }
        }

        int Kind(string kind) => notices.Count(n => Js.Text(n.Column("changedValuesJson"), "notice") == kind);
        if (!noticesListed && notices.Count > 0)
        {
            s.HideFromCaller("notice", notices.Count);
        }

        JsonTreeNode outbox = JsonTreeNode.Null;
        if (host?.Outbox is OutboxFacts box)
        {
            outbox = Js.Obj(
                ("counts", Js.Obj(new[] { "pending", "claimed", "sent", "answered", "expired", "cancelled", "failed" }.Select(state => new KeyValuePair<string, JsonTreeNode>(state, JsonTreeNode.FromInt64(box.Counts.TryGetValue(state, out int n) ? n : 0))))),
                ("attention", Arr(box.Attention.Take(50).Select(a => Js.Obj(
                    ("approvalRecordId", JsonTreeNode.FromString(a.ApprovalRecordId)),
                    ("decisionId", Js.Str(ToolScope.IsGuid(a.DecisionId) ? a.DecisionId : null)),
                    ("state", JsonTreeNode.FromString(a.State)),
                    ("attempts", JsonTreeNode.FromInt64(a.Attempts)),
                    ("enqueuedOn", Js.Utc(DateTime.SpecifyKind(a.EnqueuedOnUtc, DateTimeKind.Utc))),
                    ("updatedOn", Js.Utc(DateTime.SpecifyKind(a.UpdatedOnUtc, DateTimeKind.Utc))),
                    ("recordUrl", ToolScope.IsGuid(a.DecisionId) ? JsonTreeNode.FromString(s.RecordUrl("decision", a.DecisionId)) : JsonTreeNode.Null))))));
        }

        bool mayRefresh = s.HoldsRole("platform-admin");
        JsonTreeNode data = Js.Obj(
            ("host", host == null ? JsonTreeNode.Null : Js.Obj(
                ("environmentName", JsonTreeNode.FromString(host.EnvironmentName)),
                ("isDemo", JsonTreeNode.FromBoolean(host.IsDemo)),
                ("stepUpMode", JsonTreeNode.FromString(host.StepUpMode)),
                ("dueStateRefresh", Js.Obj(
                    ("enabled", JsonTreeNode.FromBoolean(host.DueStateRefreshHourUtc.HasValue)),
                    ("hourUtc", host.DueStateRefreshHourUtc.HasValue ? JsonTreeNode.FromInt64(host.DueStateRefreshHourUtc.Value) : JsonTreeNode.Null),
                    ("schedule", JsonTreeNode.FromString(host.DueStateRefreshHourUtc.HasValue
                        ? "Every day at " + host.DueStateRefreshHourUtc.Value.ToString("00", CultureInfo.InvariantCulture) + ":05 UTC."
                        : "Off: the daily timer is not set, so only a platform admin's run now refreshes due states.")))),
                ("store", Js.Obj(
                    ("kind", Js.Str(host.StoreKind)),
                    ("schemaVersion", host.SchemaVersion.HasValue ? JsonTreeNode.FromInt64(host.SchemaVersion.Value) : JsonTreeNode.Null),
                    ("requiredSchemaVersion", host.RequiredSchemaVersion.HasValue ? JsonTreeNode.FromInt64(host.RequiredSchemaVersion.Value) : JsonTreeNode.Null))))),
            ("engine", Js.Obj(
                ("engineVersion", JsonTreeNode.FromString(EngineInfo.Version)),
                ("snapshotSchemaVersion", JsonTreeNode.FromString(s.Snapshot.SchemaVersion.Major + "." + s.Snapshot.SchemaVersion.Minor)))),
            ("configuration", Js.Obj(
                ("configurationSetKey", JsonTreeNode.FromString(s.Call.Snapshot.ConfigurationSetKey)),
                ("version", JsonTreeNode.FromInt64(s.Call.Snapshot.VersionNumber)),
                ("contentHash", JsonTreeNode.FromString(s.Call.Snapshot.ContentHash)),
                ("publishedOn", Js.Utc(publishedOn)),
                ("reviewDueOn", reviewDays > 0 ? Js.Date(publishedOn.AddDays(reviewDays)) : JsonTreeNode.Null),
                ("openDrafts", JsonTreeNode.FromInt64(openDrafts)))),
            ("toolPolicy", Js.Obj(
                ("source", JsonTreeNode.FromString(policy.Source == ToolPolicySource.Configuration ? "configuration" : "shipped-seed")),
                ("rows", Arr(policyRows)))),
            ("clients", Arr(clients)),
            ("notices", Js.Obj(
                ("days", JsonTreeNode.FromInt64(days)),
                ("lastRefreshChangeOn", lastRefresh.HasValue ? Js.Utc(lastRefresh.Value) : JsonTreeNode.Null),
                ("recentRuns", RecentRefreshRuns(s, entries)),
                ("reminders", JsonTreeNode.FromInt64(Kind("remind") + Kind("repeat"))),
                ("escalations", JsonTreeNode.FromInt64(Kind("escalate"))),
                ("overdue", JsonTreeNode.FromInt64(Kind("overdue") + Kind("expired"))),
                ("flagNotices", JsonTreeNode.FromInt64(Kind("flag"))),
                ("items", noticesListed ? Arr(notices.Take(25).Select(n => NoticeJson(s, n.Column("changedValuesJson") ?? Js.EmptyObject))) : Js.EmptyArray))),
            ("outbox", outbox),
            ("actions", Arr(new[]
            {
                Js.Obj(
                    ("tool", JsonTreeNode.FromString("run_due_state_refresh")),
                    ("label", JsonTreeNode.FromString("Run the due-state refresh now")),
                    ("allowed", JsonTreeNode.FromBoolean(mayRefresh)),
                    ("why", JsonTreeNode.FromString(mayRefresh ? "A platform admin runs it now on the web app; the daily timer also runs it on a host that turns it on." : "Only a platform admin runs it now, on the web app."))),
            })));
        string summary = "Version " + s.Call.Snapshot.VersionNumber + " is in force" + (host == null ? "; the host gave no settings" : ", on " + (host.IsDemo ? "the demo install" : host.EnvironmentName)) + ". "
            + Count(clients.Count, "client", "clients") + " called in the last " + days + " days; " + Count(notices.Count, "notice was", "notices were") + " sent.";
        return s.Answer(data, summary, null, mayRefresh ? new[] { ToolScope.Next("run_due_state_refresh", ("validateOnly", JsonTreeNode.True)) } : Array.Empty<NextStep>());
    }

    // The notice kinds the daily refresh sends (EngineRuntime.DueStates): reminders (remind, repeat), escalations
    // (escalate), overdue reviews (overdue), expired approval requests (expired) and flagged deployments (flag). The
    // "Notices sent" tile and each run's total add up these six (wave 4b review), so a run's total and the tile agree.
    private static readonly string[] NoticeKinds = { "remind", "repeat", "escalate", "overdue", "expired", "flag" };

    // The last runs of the due-state refresh, from their run audit entries (newest first, at most 5): when, who, the
    // counts, and the reviews each opened with a link (product review, story step 14).
    private static JsonTreeNode RecentRefreshRuns(ToolScope s, IEnumerable<StoredRow> entries)
    {
        var runs = entries.Where(e => e.Text("operation") == EngineRuntime.DueStateRunOperation)
            .OrderByDescending(e => e.Text("occurredOn") ?? string.Empty, StringComparer.Ordinal).ThenBy(e => e.Id, StringComparer.Ordinal).Take(5).ToList();
        return Arr(runs.Select(run =>
        {
            JsonTreeNode? changed = run.Column("changedValuesJson");
            JsonTreeNode? counts = Js.Member(changed, "counts");
            long N(string name) => Js.Number(counts, name) ?? 0;
            string trigger = Js.Text(changed, "trigger") ?? "timer";
            var reviews = Js.Strings(changed, "reviewsOpened").Select(id => s.Rows.Get("review", id)).Where(r => r != null).Select(r => r!).Select(r =>
            {
                StoredRow? app = r.Text("businessApplication") is string appId ? s.Rows.Get("business-application", appId) : null;
                string due = Js.ParseMoment(r.Text("dueOn")) is DateTime d ? d.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture) : "no due date";
                return Js.Obj(
                    ("reviewId", JsonTreeNode.FromString(r.Id)),
                    ("label", JsonTreeNode.FromString(s.Label("review-type", r.Text("reviewType")) + ": " + (app == null ? "an application" : AppName(app)) + ", due " + due)),
                    ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("review", r.Id))));
            });
            return Js.Obj(
                ("ranOn", Js.ParseMoment(run.Text("occurredOn")) is DateTime on ? Js.Utc(on) : Js.Utc(s.Now)),
                ("trigger", JsonTreeNode.FromString(trigger)),
                ("ranBy", trigger == "timer" || run.Text("actorKind") == "engine" ? JsonTreeNode.Null : s.PrincipalRef(run.Text("actor"))),
                ("reviewsOpened", JsonTreeNode.FromInt64(N("reviewsOpened"))),
                ("reviewsOverdue", JsonTreeNode.FromInt64(N("reviewsOverdue"))),
                ("documentsStale", JsonTreeNode.FromInt64(N("documentsStale"))),
                ("notices", JsonTreeNode.FromInt64(Js.Member(changed, "noticeKinds") is JsonTreeNode kinds && kinds.Kind == JsonTreeKind.Object
                    ? NoticeKinds.Sum(k => Js.Number(kinds, k) ?? 0)
                    : N("reminders") + N("escalations") + N("flagNotices") + N("reviewsOverdue"))),
                ("reviews", Arr(reviews)));
        }));
    }

    // One Tool Policy row as the Admin page lists it: who may call the tool, through which entries, how far an AI client
    // may take a record, which subject kinds an AI client may submit, and the per-person limit.
    private static JsonTreeNode PolicyLine(ToolPolicySet policy, ToolContract tool)
    {
        bool hasRow = policy.TryGet(tool.Name, out ToolPolicyRow row);
        return Js.Obj(
            ("tool", JsonTreeNode.FromString(tool.Name)),
            ("number", JsonTreeNode.FromInt64(tool.Number)),
            ("toolset", JsonTreeNode.FromString(tool.ToolsetKey)),
            ("access", JsonTreeNode.FromString(tool.IsWrite ? "write" : "read")),
            ("entries", Js.Strs(tool.Entries)),
            ("release", JsonTreeNode.FromString(tool.Release)),
            ("isEnabled", JsonTreeNode.FromBoolean(hasRow && row.IsEnabled)),
            ("isServed", JsonTreeNode.FromBoolean(policy.IsServed(tool))),
            ("allowedRoleKeys", Js.Strs(tool.AnyoneSignedIn || !hasRow ? Array.Empty<string>() : row.AllowedRoleKeys.ToArray())),
            ("anyoneSignedIn", JsonTreeNode.FromBoolean(tool.AnyoneSignedIn)),
            ("relations", Js.Strs(tool.Relations.Where(r => r != "any-role" && r != "anyone-signed-in"))),
            ("maxAiState", JsonTreeNode.FromString(tool.IsPersonOnly ? "read-only" : policy.EffectiveMaxAiState(tool))),
            ("aiMaySubmit", Js.Strs(hasRow ? row.AiMaySubmit.Where(p => p.Value).Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal) : Enumerable.Empty<string>())),
            ("callsPerPersonPerHour", hasRow && row.CallsPerPersonPerHour.HasValue ? JsonTreeNode.FromInt64(row.CallsPerPersonPerHour.Value) : JsonTreeNode.Null));
    }

    private sealed class ClientTally
    {
        public ClientTally(string label)
        {
            Label = label;
        }

        public string Label { get; }

        public SnapshotRow? Known { get; set; }

        public int Calls { get; set; }

        public int Refused { get; set; }

        public int Drafts { get; set; }

        public DateTime? LastCallOn { get; set; }
    }
}
