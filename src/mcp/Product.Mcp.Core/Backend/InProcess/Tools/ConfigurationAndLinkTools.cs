using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Product.Engine.Configuration.Composition;
using Product.Engine.Configuration.Presets;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Configuration.Validation;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Operations;
using Product.Engine.Core.Snapshots;
using Product.Engine.Runtime;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Json;

namespace Product.Mcp.Core.Backend.InProcess;

// Configuration toolset (tools 40 to 44, 50 and 51) and links toolset (tool 45) of document 24, sections 6.10 and 6.11.
// No tool writes a configuration row: propose_configuration_change stores a C46 draft only.
internal static partial class ToolHandlers
{
    private static readonly Regex KeyPathShape = new("^[a-z][a-z0-9-]{0,63}(/[a-z][a-z0-9-]{0,63})?$", RegexOptions.CultureInvariant);

    // ---- 40. get_configuration ----

    private static JsonTreeNode GetConfiguration(ToolScope s)
    {
        int versionNumber = (int)(Js.Number(s.Args, "version") ?? s.Call.Snapshot.VersionNumber);
        CompiledSnapshot version = s.LoadVersion(versionNumber, "version");
        string? entity = s.Text("entity");
        if (entity != null && !version.Snapshot.TryGetEntity(entity, out _))
        {
            throw s.Invalid("entity", "entity \"" + entity + "\" is not a configuration entity of version " + versionNumber + ".");
        }

        var keys = new HashSet<string>(s.Strings("keys"), StringComparer.Ordinal);
        bool retired = s.Flag("includeRetired") ?? false;
        var rows = version.Snapshot.Entities
            .Where(e => entity == null || e.Name == entity)
            .SelectMany(e => e.Rows.Select(r => (Entity: e.Name, Row: r)))
            .Where(x => (retired || x.Row.IsActive) && (keys.Count == 0 || keys.Contains(x.Row.KeyPath)) && KeyPathShape.IsMatch(x.Row.KeyPath))
            .OrderBy(x => x.Entity, StringComparer.Ordinal).ThenBy(x => x.Row.KeyPath, StringComparer.Ordinal)
            .ToList();
        var page = s.Page(rows, out string? next);
        var members = new List<(string, JsonTreeNode?)>
        {
            ("configurationSet", Js.Obj(
                ("key", JsonTreeNode.FromString(version.ConfigurationSetKey)),
                ("version", JsonTreeNode.FromInt64(version.VersionNumber)),
                ("publishedOn", Js.Utc(s.Session.PublishedOnUtc ?? s.Now)),
                ("contentHash", JsonTreeNode.FromString(version.ContentHash)))),
            ("rows", Arr(page.Select(x => Js.Obj(
                ("entity", JsonTreeNode.FromString(x.Entity)),
                ("key", JsonTreeNode.FromString(x.Row.KeyPath)),
                ("label", JsonTreeNode.FromString(Js.Text(x.Row.Columns, "label") ?? x.Row.KeyPath)),
                ("isActive", JsonTreeNode.FromBoolean(x.Row.IsActive)),
                ("origin", JsonTreeNode.FromString(x.Row.Origin == Origin.Local ? "local" : "shipped")),
                ("fields", x.Row.Columns))))),
            ("skipToken", Js.OptStr(next)),
        };
        if (Js.Number(s.Args, "compareToVersion") is long compareTo)
        {
            CompiledSnapshot other = s.LoadVersion((int)compareTo, "compareToVersion");
            members.Add(("diff", Arr(Diff(other.Snapshot, version.Snapshot, entity))));
        }

        return s.Answer(Js.Obj(members.ToArray()), rows.Count + " configuration rows of version " + versionNumber + ".");
    }

    // Row changes from one version to another: added, updated, or retired (a row the new version lacks or holds inactive).
    private static IEnumerable<JsonTreeNode> Diff(ConfigurationSnapshot before, ConfigurationSnapshot after, string? entity)
    {
        var names = before.Entities.Select(e => e.Name).Union(after.Entities.Select(e => e.Name)).Where(n => entity == null || n == entity).OrderBy(n => n, StringComparer.Ordinal);
        foreach (string name in names)
        {
            var oldRows = before.TryGetEntity(name, out SnapshotEntity? o) ? o.Rows.ToDictionary(r => r.KeyPath, StringComparer.Ordinal) : new Dictionary<string, SnapshotRow>(StringComparer.Ordinal);
            var newRows = after.TryGetEntity(name, out SnapshotEntity? n) ? n.Rows.ToDictionary(r => r.KeyPath, StringComparer.Ordinal) : new Dictionary<string, SnapshotRow>(StringComparer.Ordinal);
            foreach (string key in oldRows.Keys.Union(newRows.Keys).Where(k => KeyPathShape.IsMatch(k)).OrderBy(k => k, StringComparer.Ordinal))
            {
                oldRows.TryGetValue(key, out SnapshotRow? was);
                newRows.TryGetValue(key, out SnapshotRow? now);
                string? op = was == null || !was.IsActive ? (now != null && now.IsActive ? "added" : null)
                    : now == null || !now.IsActive ? "retired"
                    : was.Columns.Equals(now.Columns) ? null : "updated";
                if (op != null)
                {
                    yield return Js.Obj(
                        ("entity", JsonTreeNode.FromString(name)),
                        ("key", JsonTreeNode.FromString(key)),
                        ("op", JsonTreeNode.FromString(op)),
                        ("before", was?.Columns ?? JsonTreeNode.Null),
                        ("after", op == "retired" ? JsonTreeNode.Null : now?.Columns ?? JsonTreeNode.Null));
                }
            }
        }
    }

    // ---- 50. explain_rules ----

    private static JsonTreeNode ExplainRules(ToolScope s)
    {
        string kind = s.RequireText("subjectKind");
        string id = s.RequireText("subjectId");
        var aspects = new HashSet<string>(s.Strings("aspects"), StringComparer.Ordinal);
        bool Want(string aspect) => aspects.Count == 0 || aspects.Contains(aspect);
        if (Js.Number(s.Args, "version") is long asked && asked != s.Call.Snapshot.VersionNumber)
        {
            s.Call.Warn("explain.version-stamped", "The evaluation runs under the version the record was stamped with, as the engine did; version " + asked + " was not used for it.", "Configuration Set Version", null);
        }

        var results = new List<JsonTreeNode>();
        int stamped = s.Call.Snapshot.VersionNumber;
        string? decisionId = kind switch
        {
            "decision" => s.RequireRow("decision", id, "subjectId").Id,
            "intake-request" => IntakeDecisionOf(s, s.RequireRow("intake-request", id, "subjectId")),
            "change-request" => s.RequireRow("change-request", id, "subjectId").Text("authorizationDecision"),
            _ => null,
        };
        if (decisionId != null)
        {
            stamped = (int)(s.Rows.Get("decision", decisionId)?.Number("configurationSetVersion") ?? stamped);
            if (Want("approvals"))
            {
                results.Add(Aspect("approvals", "OP-02", s.Call.RequiredApprovals(decisionId)));
            }

            if (Want("separation"))
            {
                EvaluationResult<JsonTreeNode?>? status = s.Call.ApprovalStatus(decisionId, out _, out _);
                if (status != null)
                {
                    results.Add(Aspect("separation", "OP-03", status));
                }
            }
        }

        if (kind == "business-application")
        {
            StoredRow app = s.RequireRow("business-application", id, "subjectId");
            stamped = (int)(app.Number("configurationSetVersion") ?? stamped);
            if (Want("requirements"))
            {
                foreach (SnapshotRow gate in s.ActiveRows("gate").OrderBy(g => g.KeyPath, StringComparer.Ordinal))
                {
                    results.Add(Aspect("requirements", "OP-07", s.Call.GateReadiness(app.Id, gate.KeyPath)));
                }
            }
        }

        if (kind == "deployment-record")
        {
            StoredRow record = s.RequireRow("deployment-record", id, "subjectId");
            stamped = (int)(record.Number("configurationSetVersion") ?? stamped);
            StoredRow? target = s.Rows.Get("deployment-target", record.Text("deploymentTarget") ?? string.Empty);
            StoredRow? app = s.Rows.Get("business-application", record.Text("businessApplication") ?? string.Empty);
            string? change = record.Text("changeRequest") is string c ? s.Rows.Get("change-request", c)?.Text("changeType") : s.Setting(EngineKeys.UnlinkedDeploymentChangeType);
            SnapshotRow? policy = s.ActiveRows("gate-policy").FirstOrDefault(p => Js.Text(p.Columns, "tierKey") == app?.Text("tier") && Js.Text(p.Columns, "changeTypeKey") == change && Js.Text(p.Columns, "targetStageKey") == target?.Text("targetStage"));
            if (Want("gate") || Want("flags"))
            {
                var trace = new List<JsonTreeNode>
                {
                    TraceStep(1, "gate-policy", policy?.KeyPath, policy != null, policy == null ? "No gate policy row covers this tier, change type and target stage." : "The gate policy row for tier " + app?.Text("tier") + ", change type " + change + " and stage " + target?.Text("targetStage") + "."),
                    TraceStep(2, "deployment-route", record.Text("deploymentRoute"), true, "The route the engine set from the entry point."),
                };
                results.Add(Js.Obj(
                    ("aspect", JsonTreeNode.FromString(Want("gate") ? "gate" : "flags")),
                    ("operation", JsonTreeNode.FromString("OP-04")),
                    ("winner", policy == null ? JsonTreeNode.Null : Js.Obj(("entity", JsonTreeNode.FromString("gate-policy")), ("key", JsonTreeNode.FromString(policy.KeyPath)), ("scope", Js.Obj(("tierKey", Js.OptStr(Js.Text(policy.Columns, "tierKey"))), ("changeTypeKey", Js.OptStr(Js.Text(policy.Columns, "changeTypeKey"))), ("targetStageKey", Js.OptStr(Js.Text(policy.Columns, "targetStageKey"))))))),
                    ("dropped", Js.EmptyArray),
                    ("trace", Arr(trace))));
            }
        }

        string summary = results.Count == 0 ? "No rule trace applies to this record for the aspects asked." : results.Count + " rule trace(s), under configuration version " + stamped + ".";
        JsonTreeNode data = Js.Obj(
            ("configurationSetVersion", JsonTreeNode.FromInt64(stamped)),
            ("engineVersion", JsonTreeNode.FromString(EngineInfo.Version)),
            ("aspects", Arr(results)),
            ("summary", JsonTreeNode.FromString(summary)));
        return s.Answer(data, summary);
    }

    // One aspect from an engine evaluation's rule trace (document 25: outcome, reasons, rule trace).
    private static JsonTreeNode Aspect(string aspect, string operation, EvaluationResult<JsonTreeNode?> result)
    {
        var steps = new List<JsonTreeNode>();
        JsonTreeNode winner = JsonTreeNode.Null;
        var dropped = new List<JsonTreeNode>();
        int step = 0;
        foreach (RuleTrace trace in result.Trace)
        {
            step++;
            string entity = EntityKey(trace.Entity);
            string? key = KeyPathShape.IsMatch(trace.Key) ? trace.Key : null;
            bool matched = trace.Role != RuleTraceRole.Dropped && trace.Role != RuleTraceRole.Skipped;
            string role = EngineContract.RuleTraceRoles.ToWire(trace.Role);
            steps.Add(TraceStep(step, entity, key, matched, trace.Entity + " " + trace.Key + ": " + role + "."));
            if (key != null && trace.Role == RuleTraceRole.Won && winner.Kind == JsonTreeKind.Null)
            {
                winner = Js.Obj(("entity", JsonTreeNode.FromString(entity)), ("key", JsonTreeNode.FromString(key)), ("scope", Js.EmptyObject));
            }
            else if (key != null && trace.Role == RuleTraceRole.Dropped)
            {
                dropped.Add(Js.Obj(("entity", JsonTreeNode.FromString(entity)), ("key", JsonTreeNode.FromString(key)), ("why", JsonTreeNode.FromString("A more specific row won."))));
            }
        }

        if (winner.Kind == JsonTreeKind.Null)
        {
            RuleTrace? first = result.Trace.FirstOrDefault(t => t.Role == RuleTraceRole.Matched && KeyPathShape.IsMatch(t.Key));
            if (first != null)
            {
                winner = Js.Obj(("entity", JsonTreeNode.FromString(EntityKey(first.Entity))), ("key", JsonTreeNode.FromString(first.Key)), ("scope", Js.EmptyObject));
            }
        }

        return Js.Obj(("aspect", JsonTreeNode.FromString(aspect)), ("operation", JsonTreeNode.FromString(operation)), ("winner", winner), ("dropped", Arr(dropped)), ("trace", Arr(steps)));
    }

    private static JsonTreeNode TraceStep(int step, string entity, string? key, bool matched, string why)
    {
        return Js.Obj(("step", JsonTreeNode.FromInt64(step)), ("entity", JsonTreeNode.FromString(entity)), ("key", Js.Str(key)), ("matched", JsonTreeNode.FromBoolean(matched)), ("why", JsonTreeNode.FromString(why)));
    }

    // "Approval Rule" becomes "approval-rule".
    private static string EntityKey(string name)
    {
        return string.Join("-", name.Split(' ').Where(p => p.Length > 0).Select(p => p.ToLowerInvariant()));
    }

    // ---- 51. get_configuration_health ----

    private static JsonTreeNode GetConfigurationHealth(ToolScope s)
    {
        CompiledSnapshot active = s.Call.Snapshot;
        var versions = s.Query("configuration-set-version", ("configurationSet", active.ConfigurationSetKey)).OrderByDescending(v => v.Number("versionNumber") ?? 0).ToList();
        StoredRow? activeRow = versions.FirstOrDefault(v => v.Number("versionNumber") == active.VersionNumber);
        DateTime activatedOn = Js.ParseMoment(activeRow?.Text("publishedOn")) ?? Js.ParseMoment(activeRow?.Text("engineCheckedOn")) ?? s.Session.PublishedOnUtc ?? s.Now;
        var findings = new List<JsonTreeNode>();
        if (activeRow != null && activeRow.Text("contentHash") is string stored && stored != active.ContentHash)
        {
            findings.Add(Js.Obj(("ruleKey", JsonTreeNode.FromString("snapshot.hash-mismatch")), ("message", JsonTreeNode.FromString("The stored content hash of the active version does not match its content."))));
        }

        long reviewDays = s.SettingNumber("configuration-review-days") ?? 0;
        var members = new List<(string, JsonTreeNode?)>
        {
            ("active", Js.Obj(
                ("version", JsonTreeNode.FromInt64(active.VersionNumber)),
                ("activatedOn", Js.Utc(activatedOn)),
                ("contentHash", JsonTreeNode.FromString(active.ContentHash)),
                ("decisionId", Js.Str(activeRow?.Text("changeDecision") ?? activeRow?.Text("acceptedByDecision"))))),
            ("engine", Js.Obj(
                ("engineVersion", JsonTreeNode.FromString(EngineInfo.Version)),
                ("snapshotSchemaVersion", JsonTreeNode.FromString(active.Snapshot.SchemaVersion.Major + "." + active.Snapshot.SchemaVersion.Minor)),
                ("snapshotHash", JsonTreeNode.FromString(active.ContentHash)),
                ("loaded", JsonTreeNode.True))),
            ("findings", Arr(findings)),
            ("reviewDueOn", reviewDays > 0 ? Js.Date(activatedOn.AddDays(reviewDays)) : JsonTreeNode.Null),
        };
        if (s.Flag("includeHistory") ?? true)
        {
            members.Add(("versions", Arr(versions.Select(v => Js.Obj(
                ("version", JsonTreeNode.FromInt64(v.Number("versionNumber") ?? 0)),
                ("status", JsonTreeNode.FromString(v.Number("versionNumber") == active.VersionNumber ? "active" : "superseded")),
                ("activatedOn", Js.Utc(Js.ParseMoment(v.Text("publishedOn")) ?? Js.ParseMoment(v.Text("engineCheckedOn")) ?? activatedOn)),
                ("decisionId", Js.Str(v.Text("changeDecision") ?? v.Text("acceptedByDecision"))),
                ("summary", JsonTreeNode.FromString(v.Text("changeSummary") ?? "Version " + (v.Number("versionNumber") ?? 0) + ".")))))));
        }

        if (s.Flag("includeDrafts") ?? true)
        {
            var drafts = s.Rows.Query(RowQuery.For("configuration-draft")).Where(d => d.Text("draftStatus") == "draft" || d.Text("draftStatus") == "submitted").OrderBy(d => d.Id, StringComparer.Ordinal).ToList();
            List<StoredRow> page = s.Page(drafts, out string? next);
            members.Add(("drafts", Arr(page.Select(d => Js.Obj(
                ("configurationDraftId", JsonTreeNode.FromString(d.Id)),
                ("status", JsonTreeNode.FromString(d.Text("draftStatus")!)),
                ("baseVersion", JsonTreeNode.FromInt64(d.Number("baseVersionNumber") ?? active.VersionNumber)),
                ("submittedOn", ToolScope.MomentOrNull(d.Text("submittedOn"))),
                ("expiresOn", ToolScope.MomentOrNull(d.Text("expiresOn"))),
                ("isUpgradeDraft", JsonTreeNode.FromBoolean(d.Flag("isUpgradeDraft"))))))));
            members.Add(("skipToken", Js.OptStr(next)));
        }

        return s.Answer(Js.Obj(members.ToArray()), "Version " + active.VersionNumber + " is active and loads; " + findings.Count + " finding(s).");
    }

    // ---- 41. list_presets ----

    private static JsonTreeNode ListPresets(ToolScope s)
    {
        var composer = new ConfigurationComposer(s.Catalog);
        var presets = new List<JsonTreeNode>();
        foreach (PresetSource preset in s.Catalog.Presets.OrderBy(p => p.IsFull ? 0 : 1).ThenBy(p => p.Key, StringComparer.Ordinal))
        {
            JsonTreeNode? bundle = null;
            if (preset.IsFull)
            {
                var issues = new ValidationIssues();
                bundle = composer.BuildPreset(preset.Key, issues).Bundle;
            }

            JsonTreeNode? entities = Js.Member(bundle, "entities");
            var tiers = Js.Items(entities, "tier").Where(t => Js.Flag(t, "isActive") != false).Select(t => Js.Obj(("key", JsonTreeNode.FromString(Js.Text(t, "key")!)), ("label", JsonTreeNode.FromString(Js.Text(t, "label") ?? Js.Text(t, "key")!))));
            var levels = Js.Items(entities, "gate-policy").Where(p => Js.Text(p, "tierKey") is string t && t != "*" && Js.Text(p, "targetStageKey") is string g && g != "*" && Js.Text(p, "changeTypeKey") == "normal")
                .Select(p => Js.Obj(("tierKey", JsonTreeNode.FromString(Js.Text(p, "tierKey")!)), ("targetStageKey", JsonTreeNode.FromString(Js.Text(p, "targetStageKey")!)), ("gateLevel", JsonTreeNode.FromString(Js.Text(p, "gateLevel")!))));
            string? mode = Js.Items(entities, "setting").FirstOrDefault(r => Js.Text(r, "key") == "change-management-mode") is JsonTreeNode m ? Js.Text(m, "value") : null;
            int rules = Js.Items(entities, "approval-rule").Count;
            string release = Js.Text(preset.Manifest, "productRelease") ?? EngineInfo.Version;
            presets.Add(Js.Obj(
                ("presetKey", JsonTreeNode.FromString(preset.Key)),
                ("label", JsonTreeNode.FromString(Js.Text(preset.Manifest, "label") ?? preset.Key)),
                ("presetKind", JsonTreeNode.FromString(preset.IsFull ? "full" : "overlay")),
                ("extends", Js.Str(preset.Extends)),
                ("conflictsWith", Js.Strs(preset.ConflictsWith)),
                ("productRelease", JsonTreeNode.FromString(release)),
                ("summary", Js.Obj(
                    ("tiers", Arr(tiers)),
                    ("gateLevelsByTierAndStage", Arr(levels)),
                    ("changeManagementMode", mode == "built-in" || mode == "linked" ? JsonTreeNode.FromString(mode) : JsonTreeNode.Null),
                    ("approvalsSummary", JsonTreeNode.FromString(preset.IsFull ? rules + " approval rules." : Js.Text(preset.Manifest, "description") ?? "An overlay, applied over a full preset."))))));
        }

        return s.Answer(Js.Obj(("presets", Arr(presets))), presets.Count + " presets. A client picks one full preset plus declared overlays, validated as one set.");
    }

    // ---- 42. propose_configuration_change ----

    // WORKING DEFAULT (open design question 4): a person accepts the engine's exact diff on one card; the instructing
    // admin may accept their own AI's draft unless strict separation is in force.
    private static JsonTreeNode ProposeConfigurationChange(ToolScope s)
    {
        s.RequireAiState("candidate");
        int baseVersion = (int)(Js.Number(s.Args, "baseVersionNumber") ?? 0);
        JsonTreeNode operations;
        if (s.Items("operations").Count > 0)
        {
            operations = Arr(s.Items("operations"));
        }
        else if (s.Text("fileContent") is string file)
        {
            if (!CanonicalJson.TryParse(file, out JsonTreeNode? parsed, out string? error) || Js.Items(parsed, "operations").Count == 0)
            {
                throw s.Invalid("fileContent", "fileContent must be a draft patch document with operations" + (error == null ? "." : ": " + error));
            }

            operations = Arr(Js.Items(parsed, "operations"));
        }
        else
        {
            throw s.Invalid("preset", "Proposing a whole preset as a draft is not built yet. Send operations instead.");
        }

        // A locked setting (C04 Is Locked), the AI approval lock among them, never changes through any tool.
        foreach (JsonTreeNode op in operations.Items)
        {
            if (Js.Text(op, "entity") == "setting" && s.ConfigRow("setting", Js.Text(op, "key")) is SnapshotRow setting && Js.Flag(setting.Columns, "isLocked") == true)
            {
                throw s.Refused(McpErrorCodes.Locked, "The setting " + setting.KeyPath + " is locked. A locked value does not change through any tool.", setting.KeyPath);
            }
        }

        bool validateOnly = s.ValidateOnly;
        JsonTreeNode changeSet = Js.Obj(
            ("schemaVersion", JsonTreeNode.FromString("1.0")),
            ("manifest", Js.Obj(("kind", JsonTreeNode.FromString("patch")), ("baseVersionNumber", JsonTreeNode.FromInt64(baseVersion)))),
            ("operations", operations));
        if (baseVersion != s.Call.Snapshot.VersionNumber && s.Text("configurationDraftId") == null)
        {
            s.Call.Warn("config.base-moved", "baseVersionNumber " + baseVersion + " is not the active version " + s.Call.Snapshot.VersionNumber + ". The engine rebases at activation and refuses an overlap.", "Configuration Draft", null);
        }

        var proposal = new ConfigurationProposal(changeSet, baseVersion, s.RequireText("proposalRationale")) { ValidateOnly = validateOnly, ConfigurationDraftId = s.Text("configurationDraftId") };
        string? draftId = s.Call.ProposeConfigurationChange(proposal, out JsonTreeNode? payload);
        JsonTreeNode? report = Js.Member(s.Call.Result("OP-09"), "payload");
        var errors = Js.Items(report, "issues").Where(i => Js.Text(i, "severity") == "error" && Js.Text(i, "ruleId") is string rule && Regex.IsMatch(rule, "^V[0-9]{2}$")).Select(i => Js.Obj(
            ("entity", Js.Str(Js.Text(i, "entity") is string e ? EntityKey(e) : null)),
            ("key", Js.Str(Js.Text(i, "ref") is string r && KeyPathShape.IsMatch(r) ? r : null)),
            ("ruleKey", JsonTreeNode.FromString(Js.Text(i, "ruleId")!)),
            ("message", JsonTreeNode.FromString(Js.Text(i, "message") ?? "A rule failed.")))).ToList();
        bool valid = Js.Flag(payload, "isValid") == true;

        var diff = new List<JsonTreeNode>();
        var byChange = new List<JsonTreeNode>();
        var gateChanges = new List<JsonTreeNode>();
        var apps = s.Rows.Query(RowQuery.For("business-application")).Where(a => a.Text("statusMeaning") != "archived").ToList();
        var affected = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonTreeNode op in operations.Items)
        {
            string entity = Js.Text(op, "entity") ?? string.Empty;
            string key = Js.Text(op, "key") ?? string.Empty;
            SnapshotRow? current = s.ConfigRow(entity, key);
            string kind = Js.Text(op, "op") == "retire" ? "retired" : current == null || !current.IsActive ? "added" : "updated";
            JsonTreeNode after = kind == "retired" ? JsonTreeNode.Null : Js.Obj((current?.Columns.Members ?? Array.Empty<KeyValuePair<string, JsonTreeNode>>()).Where(m => Js.Member(op, "values")?.TryGetMember(m.Key, out _) != true).Concat(Js.Member(op, "values")?.Members ?? Array.Empty<KeyValuePair<string, JsonTreeNode>>()));
            if (KeyPathShape.IsMatch(key) && KeyPathShape.IsMatch(entity))
            {
                diff.Add(Js.Obj(("entity", JsonTreeNode.FromString(entity)), ("key", JsonTreeNode.FromString(key)), ("op", JsonTreeNode.FromString(kind)), ("before", current?.Columns ?? JsonTreeNode.Null), ("after", after)));
                var touched = apps.Where(a => Touches(a, entity, key, current, after)).Select(a => a.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();
                touched.ForEach(id => affected.Add(id));
                byChange.Add(Js.Obj(("entity", JsonTreeNode.FromString(entity)), ("key", JsonTreeNode.FromString(key)), ("applicationsAffected", JsonTreeNode.FromInt64(touched.Count)), ("sampleApplicationIds", Js.Strs(touched.Take(5)))));
            }

            if (entity == "gate-policy")
            {
                JsonTreeNode source = after.Kind == JsonTreeKind.Object ? after : current?.Columns ?? Js.EmptyObject;
                if (Js.Text(source, "tierKey") is string tier && Js.Text(source, "changeTypeKey") is string change && Js.Text(source, "targetStageKey") is string stage)
                {
                    gateChanges.Add(Js.Obj(
                        ("tierKey", JsonTreeNode.FromString(tier)),
                        ("changeTypeKey", JsonTreeNode.FromString(change)),
                        ("targetStageKey", JsonTreeNode.FromString(stage)),
                        ("from", Js.Str(current == null ? null : Js.Text(current.Columns, "gateLevel"))),
                        ("to", Js.Str(kind == "retired" ? null : Js.Text(after, "gateLevel")))));
                }
            }
        }

        int inFlight = s.Rows.Query(RowQuery.For("decision").Where("statusMeaning", "proposed")).Count + s.Rows.Query(RowQuery.For("change-request").Where("statusMeaning", "submitted")).Count;
        string decisionType = operations.Items.Any(o => Js.Text(o, "entity") == "gate-policy") ? "gate-policy-change" : EngineKeys.ConfigurationChangeDecisionType;
        EvaluationResult<JsonTreeNode?> required = s.Call.Evaluate(EngineOperation.ResolveRequiredApprovals, Js.Obj(("decisionTypeKey", JsonTreeNode.FromString(decisionType)), ("portfolioPath", Js.EmptyArray)));
        var approvals = required.Outcome == "required" ? Js.Items(required.Payload, "requirements").Select(r => ApprovalNeeded(s, decisionType, r)).Where(r => r != null).Select(r => r!).ToList() : new List<JsonTreeNode>();

        List<(string, JsonTreeNode?)> result;
        if (draftId != null)
        {
            StoredRow draft = s.Rows.Get("configuration-draft", draftId)!;
            result = s.WriteResult(draft, s.Text("configurationDraftId") == null, true, "draftStatus", "draftStatus");
        }
        else
        {
            result = new List<(string, JsonTreeNode?)>
            {
                ("recordId", JsonTreeNode.Null),
                ("status", ToolScope.StatusValue("draft", "Draft")),
                ("performedBy", Js.Obj(
                    ("actorKind", JsonTreeNode.FromString(s.Session.ActorKind)),
                    ("principal", s.PrincipalRef(s.PersonId)),
                    ("onBehalfOf", s.IsAiCaller ? s.PrincipalRef(s.PersonId) : JsonTreeNode.Null),
                    ("clientApp", ToolScope.IsGuid(s.Session.Caller.ClientAppId) ? Js.Obj(("appId", JsonTreeNode.FromString(s.Session.Caller.ClientAppId)), ("label", JsonTreeNode.FromString(s.Session.Caller.ClientAppLabel ?? s.Session.Caller.ClientAppId))) : JsonTreeNode.Null))),
                ("isAiCreated", JsonTreeNode.FromBoolean(s.IsAiCaller)),
                ("engineStamp", Js.Obj(("engineVersion", JsonTreeNode.FromString(EngineInfo.Version)), ("engineCheckedOn", Js.Utc(s.Now)))),
                ("created", JsonTreeNode.False),
            };
        }

        JsonTreeNode data = ToolScope.Merge(
            result,
            ("valid", JsonTreeNode.FromBoolean(valid)),
            ("errors", Arr(errors)),
            ("configurationDraftId", Js.Str(s.Call.IsDryRun ? null : draftId)),
            ("diffSummary", Arr(diff)),
            ("contentHash", Js.Str(Js.Text(report, "contentHash") is string hash && Regex.IsMatch(hash, "^[0-9a-f]{64}$") ? hash : null)),
            ("impact", Js.Obj(
                ("applicationsAffected", JsonTreeNode.FromInt64(affected.Count)),
                ("byChange", Arr(byChange)),
                ("gateChanges", Arr(gateChanges)),
                ("recordsInFlight", JsonTreeNode.FromInt64(inFlight)))),
            ("approvalsNeeded", Arr(approvals)),
            ("activation", JsonTreeNode.FromString("Submit the draft. A person accepts the decision on an approval card. Nothing changes until then.")));
        string summary = !valid ? "The draft has " + errors.Count + " rule error(s); fix them before it is submitted." : draftId == null ? "The change is valid. Nothing was stored (validateOnly)." : "The configuration draft is stored. Nothing is in force until a person accepts its decision on a card.";
        return s.Answer(data, summary, draftId == null || s.Call.IsDryRun ? null : s.RecordUrl("configuration-draft", draftId),
            draftId == null || s.Call.IsDryRun ? Array.Empty<NextStep>() : new[] { ToolScope.Next("submit_record", ("subjectKind", JsonTreeNode.FromString("configuration-draft")), ("subjectId", JsonTreeNode.FromString(draftId))) });
    }

    // True when a change to the row touches the application: its tier, kind or stage row, or a gate policy row for its tier.
    private static bool Touches(StoredRow app, string entity, string key, SnapshotRow? before, JsonTreeNode after)
    {
        return entity switch
        {
            "tier" => app.Text("tier") == key,
            "application-kind" => app.Text("applicationKind") == key,
            "lifecycle-stage" => app.Text("lifecycleStage") == key,
            "gate-policy" => app.Text("tier") is string tier && (Js.Text(before?.Columns, "tierKey") == tier || Js.Text(after, "tierKey") == tier),
            _ => false,
        };
    }

    // ---- 43. export_configuration ----

    private static JsonTreeNode ExportConfiguration(ToolScope s)
    {
        int versionNumber = (int)(Js.Number(s.Args, "version") ?? s.Call.Snapshot.VersionNumber);
        CompiledSnapshot version = s.LoadVersion(versionNumber, "version");
        string setKey = Regex.IsMatch(version.ConfigurationSetKey, "^[a-z][a-z0-9-]*$") ? version.ConfigurationSetKey : "working";
        JsonTreeNode? entities = Js.Member(version.ContentJson, "entities");
        var files = (entities?.Members ?? Array.Empty<KeyValuePair<string, JsonTreeNode>>())
            .Where(e => Regex.IsMatch(e.Key, "^[a-z][a-z0-9-]*$"))
            .OrderBy(e => e.Key, StringComparer.Ordinal)
            .Select(e => Js.Obj(
                ("path", JsonTreeNode.FromString("config/presets/" + setKey + "/" + e.Key + ".json")),
                ("content", JsonTreeNode.FromString(Js.Write(Js.Obj(("entity", JsonTreeNode.FromString(e.Key)), ("rows", e.Value)))))))
            .ToList();
        JsonTreeNode data = Js.Obj(
            ("configurationSet", Js.Obj(("key", JsonTreeNode.FromString(setKey)), ("version", JsonTreeNode.FromInt64(versionNumber)))),
            ("files", Arr(files)),
            ("contentHash", JsonTreeNode.FromString(version.ContentHash)));
        return s.Answer(data, files.Count + " seed-format files of version " + versionNumber + ". They come back through propose_configuration_change.");
    }

    // ---- 44. check_role_bindings ----

    // WORKING DEFAULT (open design question 12): workload identities hold their role through a Workload Grant (C42);
    // people only through groups (D-11). Judgment call: auditEntryId is null, because an Audit Entry is written
    // only by the engine as the effect of a write it records (G-07), and this check writes nothing.
    private static JsonTreeNode CheckRoleBindings(ToolScope s)
    {
        IDirectoryReader directory = s.Options.Directory ?? throw s.Upstream("Microsoft Graph", "no Graph client is bound to this server");
        var findings = new List<string>();
        var bindings = new List<JsonTreeNode>();
        foreach (SnapshotRow binding in s.ActiveRows("role-group-binding").OrderBy(b => b.KeyPath, StringComparer.Ordinal))
        {
            string role = Js.Text(binding.Columns, "roleKey") ?? "unknown";
            string? group = Js.Text(binding.Columns, "entraGroupObjectId");
            DirectoryAnswer<DirectoryObject?> found = group == null ? DirectoryAnswer<DirectoryObject?>.Of(null) : directory.Group(s.Session.Caller, group);
            if (!found.Answered)
            {
                throw s.Upstream("Microsoft Graph", found.Problem);
            }

            if (found.Value == null)
            {
                findings.Add("The group bound to " + role + " (" + (group ?? "none") + ") does not exist in Entra.");
            }

            string? method = Js.Text(binding.Columns, "bindingMethod");
            bindings.Add(Js.Obj(
                ("roleKey", JsonTreeNode.FromString(role)),
                ("group", group == null ? JsonTreeNode.Null : Js.Obj(("objectId", JsonTreeNode.FromString(group)), ("displayLabel", JsonTreeNode.FromString(found.Value?.DisplayLabel ?? s.DisplayLabel(group))))),
                ("bindingMethod", method == "dataverse-group-team" || method == "entra-app-role-assignment" ? JsonTreeNode.FromString(method) : JsonTreeNode.Null),
                ("groupExists", JsonTreeNode.FromBoolean(found.Value != null)),
                ("lastVerifiedOn", Js.Utc(s.Now))));
        }

        DirectoryAnswer<IReadOnlyList<DirectAssignment>> direct = directory.DirectAssignments(s.Session.Caller);
        if (!direct.Answered)
        {
            throw s.Upstream("Microsoft Graph", direct.Problem);
        }

        var grants = new List<JsonTreeNode>();
        foreach (SnapshotRow grant in s.ActiveRows("workload-grant").OrderBy(g => g.KeyPath, StringComparer.Ordinal))
        {
            string principal = Js.Text(grant.Columns, "principalObjectId") ?? string.Empty;
            string role = Js.Text(grant.Columns, "workloadRoleKey") ?? "unknown";
            DateTime? expires = Js.Moment(grant.Columns, "expiresOn");
            bool expired = !expires.HasValue || s.Now >= expires.Value;
            bool found = direct.Value.Any(a => Same(a.Principal.ObjectId, principal) && a.RoleKey == role);
            string kind = s.Principal(principal)?.Text("principalKind") ?? "service-principal";
            if (expired)
            {
                findings.Add("The Workload Grant " + grant.KeyPath + " has expired.");
            }

            if (kind == "agent-identity" || kind == "agent-user")
            {
                findings.Add("The Workload Grant " + grant.KeyPath + " names an agent identity, which never holds a grant.");
            }

            if (!found)
            {
                findings.Add("The Workload Grant " + grant.KeyPath + " has no matching app role assignment in Entra.");
            }

            grants.Add(Js.Obj(
                ("principal", s.PrincipalRef(principal)),
                ("principalKind", JsonTreeNode.FromString(kind)),
                ("roleKey", JsonTreeNode.FromString(role)),
                ("reason", JsonTreeNode.FromString(Js.Text(grant.Columns, "reason") ?? string.Empty)),
                ("decisionId", Js.Str(Js.Text(grant.Columns, "grantedByDecisionId") is string d && ToolScope.IsGuid(d) ? d : null)),
                ("expiresOn", expires.HasValue ? Js.Date(expires.Value) : Js.Date(s.Now)),
                ("assignmentFound", JsonTreeNode.FromBoolean(found)),
                ("isExpired", JsonTreeNode.FromBoolean(expired))));
        }

        var assignments = new List<JsonTreeNode>();
        if (s.Flag("includeDirectAssignments") ?? true)
        {
            foreach (DirectAssignment assignment in direct.Value)
            {
                bool covered = s.ActiveRows("workload-grant").Any(g => Same(Js.Text(g.Columns, "principalObjectId"), assignment.Principal.ObjectId) && Js.Text(g.Columns, "workloadRoleKey") == assignment.RoleKey && (Js.Moment(g.Columns, "expiresOn") ?? DateTime.MinValue) > s.Now);
                if (!covered)
                {
                    findings.Add(assignment.Principal.DisplayLabel + " holds " + assignment.RoleKey + " directly with no current Workload Grant.");
                }

                assignments.Add(Js.Obj(
                    ("principal", Js.Obj(("objectId", JsonTreeNode.FromString(assignment.Principal.ObjectId)), ("displayLabel", JsonTreeNode.FromString(assignment.Principal.DisplayLabel)), ("principalKind", JsonTreeNode.FromString(assignment.Principal.PrincipalKind)))),
                    ("principalKind", JsonTreeNode.FromString(assignment.Principal.PrincipalKind)),
                    ("roleKey", JsonTreeNode.FromString(assignment.RoleKey)),
                    ("where", JsonTreeNode.FromString(assignment.Where)),
                    ("coveredByGrant", JsonTreeNode.FromBoolean(covered))));
            }
        }

        JsonTreeNode data = Js.Obj(
            ("bindings", Arr(bindings)),
            ("workloadGrants", Arr(grants)),
            ("directAssignments", Arr(assignments)),
            ("findings", Js.Strs(findings)),
            ("auditEntryId", JsonTreeNode.Null));
        return s.Answer(data, findings.Count == 0 ? "Every role is held through its group or a current Workload Grant." : findings.Count + " finding(s) on role bindings and grants.");
    }

    // ---- 45. save_link ----

    private static JsonTreeNode SaveLink(ToolScope s)
    {
        s.RequireAiState("link");
        string category = s.RequireText("linkCategory");
        string subjectKind = s.RequireText("subjectKind");
        string subjectId = s.RequireText("subjectId");
        SnapshotRow kind = s.RequireKey("link-kind", s.Text("linkKindKey"), "linkKindKey");
        if (Js.Text(kind.Columns, "linkCategory") != category)
        {
            throw s.Refused(McpErrorCodes.RuleViolation, "The link kind " + kind.KeyPath + " is of the " + Js.Text(kind.Columns, "linkCategory") + " category, not " + category + ".", kind.KeyPath);
        }

        (string Entity, string Column) subject = subjectKind switch
        {
            "business-application" => ("business-application", "subjectApplication"),
            "change-request" => ("change-request", "subjectChangeRequest"),
            "intake-request" => ("intake-request", "subjectIntakeRequest"),
            "decision" => ("decision", "subjectDecision"),
            "approval-record" => ("approval-record", "subjectApprovalRecord"),
            "deployment-record" => ("deployment-record", "subjectDeploymentRecord"),
            "review" => ("review", "subjectReview"),
            _ => ("claim", "subjectClaim"),
        };
        StoredRow row = s.RequireRow(subject.Entity, subjectId, "subjectId");
        string? app = subject.Entity == "business-application" ? row.Id : ApplicationOf(s, row) ?? (subject.Entity == "approval-record" && row.Text("decision") is string d && s.Rows.Get("decision", d) is StoredRow decision ? ApplicationOf(s, decision) : null);
        if (app != null)
        {
            RequireOwnerRelation(s, app);
        }

        ComposedWrite write;
        if (category == "delivery")
        {
            var columns = Js.Obj(
                ("subjectKind", JsonTreeNode.FromString(subjectKind)),
                (subject.Column, JsonTreeNode.FromString(subjectId)),
                ("linkKind", JsonTreeNode.FromString(kind.KeyPath)),
                ("externalId", Js.OptStr(s.Text("externalId"))),
                ("url", JsonTreeNode.FromString(s.RequireText("url"))),
                ("label", JsonTreeNode.FromString(s.RequireText("label"))),
                ("lastSeenOn", Js.Utc(s.Now)),
                ("isMoved", JsonTreeNode.FromBoolean(s.Text("movedToUrl") != null)),
                ("movedToUrl", Js.OptStr(s.Text("movedToUrl"))));
            string? existing = s.Text("linkId");
            write = existing == null
                ? s.Create("delivery-link", Js.With(columns, ("linkedOn", Js.Utc(s.Now))))
                : s.Update("delivery-link", s.RequireRow("delivery-link", existing, "linkId").Id, columns);
        }
        else
        {
            if (s.Text("linkId") != null)
            {
                throw s.Refused(McpErrorCodes.RuleViolation, "An evidence link is append-only. Save a new one instead of editing it.");
            }

            string level = s.Text("evidenceLevelKey") ?? "as-reported";
            RequireListValue(s, "evidence-level", level, "evidenceLevelKey");
            write = s.Create("evidence-link", Js.Obj(
                ("subjectKind", JsonTreeNode.FromString(subjectKind)),
                (subject.Column, JsonTreeNode.FromString(subjectId)),
                ("linkKind", JsonTreeNode.FromString(kind.KeyPath)),
                ("url", JsonTreeNode.FromString(s.RequireText("url"))),
                ("label", JsonTreeNode.FromString(s.RequireText("label"))),
                ("evidenceLevel", JsonTreeNode.FromString(level)),
                ("capturedOn", Js.Utc(s.Now)),
                ("digest", Js.OptStr(s.Text("digest")))));
        }

        string entity = category == "delivery" ? "delivery-link" : "evidence-link";
        JsonTreeNode data = ToolScope.Merge(
            s.WriteResult(entity, write, hasStatus: false),
            ("linkId", s.Call.IsDryRun && write.Created ? JsonTreeNode.Null : JsonTreeNode.FromString(write.RowId)),
            ("linkCategory", JsonTreeNode.FromString(category)));
        return s.Answer(data, "The " + category + " link is saved. It stores an ID, a URL and a label, never a copy.");
    }
}
