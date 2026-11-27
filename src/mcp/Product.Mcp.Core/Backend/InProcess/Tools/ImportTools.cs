using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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

// Tool 99, import_data_taxonomy (docs/architecture/data-taxonomy.md; document 24, B24-22): a CSV, or rows a Fabric or
// Purview domain list gave the admin's AI client, becomes a C46 configuration draft, exactly as propose_configuration_change
// stores one. Nothing is in force until a person accepts the draft's decision.
//
// Choices (docs/architecture/data-taxonomy.md):
// - Two ways in: csv (text with a header row, the columns in ImportColumns) or rows (objects with the same column names). The
//   caller passes the content, so the product holds no Fabric or Purview permission: the admin's AI client reads the list
//   with the admin's own token and maps each domain to a row. The source says which outside vocabulary a match_term names.
// - A row's key is its key column, else made from its label. A parent is a key or a path of labels ("People/Payroll"). A
//   level left out follows the depth: the top level for a root, the next rank down for each step. A classification is a
//   key or a Purview label name resolved through the C11 matches.
// - A re-import changes nothing that already matches: a row equal to the domain in force adds no operation. Domains in
//   force that the file leaves out are listed as not in source and never retired.
// - Owners in the file are never made live. owner, deputy and steward become prepared propose_owner_change steps; a value
//   that is an Entra object ID is ready to send, a user principal name names the person for the admin's AI client to
//   resolve, since the product reads no directory by name.
// - DC-15 is computed here, not stored: where the file names a data owner by object ID and the domain's current data owner
//   is someone else, the answer lists the difference ("the catalog names another owner"). The product never copies the
//   catalog's owner list, so the finding lives in the import's answer only.
internal static partial class ToolHandlers
{
    private const int ImportRowLimit = 500;

    private static readonly string[] ImportColumns =
    {
        "key", "label", "parent", "level", "classification", "description", "examples", "use_rule", "ai_rule", "copy_rule", "retention",
        "match_vocabulary", "match_term", "match_url", "owner", "deputy", "steward",
    };

    private static readonly Regex ObjectIdShape = new("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", RegexOptions.CultureInvariant);

    // ---- 99. import_data_taxonomy ----

    private static JsonTreeNode ImportDataTaxonomy(ToolScope s)
    {
        s.RequireAiState("candidate");
        string source = s.Text("source") ?? "csv";
        string rationale = s.RequireText("proposalRationale");
        int baseVersion = (int)(Js.Number(s.Args, "baseVersionNumber") ?? s.Call.Snapshot.VersionNumber);
        var problems = new List<JsonTreeNode>();
        List<Dictionary<string, string>> rows = s.Text("csv") is string csv ? ReadCsv(s, csv, problems)
            : s.Items("rows").Select((r, i) => RowOf(r, i + 1, problems)).ToList();
        if (rows.Count == 0)
        {
            throw s.Invalid(s.Text("csv") != null ? "csv" : "rows", "The import holds no domain rows. Give a header row and at least one row with a label.");
        }

        if (rows.Count > ImportRowLimit)
        {
            throw s.Invalid(s.Text("csv") != null ? "csv" : "rows", "The import holds " + rows.Count + " rows; one import takes at most " + ImportRowLimit + ". Split it by top-level domain.");
        }

        var tree = new DataTree(s.Snapshot);
        var plan = new ImportPlan(s, tree, source);
        for (int i = 0; i < rows.Count; i++)
        {
            plan.Read(i + 1, rows[i]);
        }

        plan.Resolve();
        problems.AddRange(plan.Problems);
        JsonTreeNode operations = Arr(plan.Operations);
        var notInSource = tree.Domains.Where(d => d.IsActive && !plan.Keys.Contains(d.KeyPath))
            .OrderBy(d => tree.PathText(d.KeyPath), StringComparer.OrdinalIgnoreCase)
            .Select(d => Js.Obj(("key", JsonTreeNode.FromString(d.KeyPath)), ("path", JsonTreeNode.FromString(tree.PathText(d.KeyPath))))).ToList();

        string? draftId = null;
        bool valid = false;
        var errors = new List<JsonTreeNode>();
        var diff = new List<JsonTreeNode>();
        if (problems.Count == 0 && plan.Operations.Count > 0)
        {
            JsonTreeNode changeSet = Js.Obj(
                ("schemaVersion", JsonTreeNode.FromString("1.0")),
                ("manifest", Js.Obj(("kind", JsonTreeNode.FromString("patch")), ("baseVersionNumber", JsonTreeNode.FromInt64(baseVersion)))),
                ("operations", operations));
            var proposal = new ConfigurationProposal(changeSet, baseVersion, rationale) { ValidateOnly = s.ValidateOnly };
            draftId = s.Call.ProposeConfigurationChange(proposal, out JsonTreeNode? payload);
            JsonTreeNode? report = Js.Member(s.Call.Result("OP-09"), "payload");
            errors = Js.Items(report, "issues").Where(i => Js.Text(i, "severity") == "error" && Js.Text(i, "ruleId") is string rule && Regex.IsMatch(rule, "^V[0-9]{2}$")).Select(i => Js.Obj(
                ("entity", Js.Str(Js.Text(i, "entity") is string e ? EntityKey(e) : null)),
                ("key", Js.Str(Js.Text(i, "ref") is string r && KeyPathShape.IsMatch(r) ? r : null)),
                ("ruleKey", JsonTreeNode.FromString(Js.Text(i, "ruleId")!)),
                ("message", JsonTreeNode.FromString(Js.Text(i, "message") ?? "A rule failed.")))).ToList();
            valid = Js.Flag(payload, "isValid") == true;
            diff = DraftImpactOf(s, operations).Diff;
        }

        int added = plan.Operations.Count(o => Js.Text(o, "entity") == "data-domain" && s.ConfigRow("data-domain", Js.Text(o, "key")) == null);
        int changed = plan.Operations.Count(o => Js.Text(o, "entity") == "data-domain") - added;
        bool stored = draftId != null && !s.Call.IsDryRun;
        StoredRow? draft = stored ? s.Rows.Get("configuration-draft", draftId!) : null;
        List<(string, JsonTreeNode?)> result = draft != null
            ? s.WriteResult(draft, true, true, "draftStatus", "draftStatus")
            : new List<(string, JsonTreeNode?)>
            {
                ("recordId", JsonTreeNode.Null),
                ("status", ToolScope.StatusValue("draft", "Draft")),
                ("performedBy", s.CallerPerformedBy()),
                ("isAiCreated", JsonTreeNode.FromBoolean(s.IsAiCaller)),
                ("engineStamp", Js.Obj(("engineVersion", JsonTreeNode.FromString(EngineInfo.Version)), ("engineCheckedOn", Js.Utc(s.Now)))),
                ("created", JsonTreeNode.False),
            };
        JsonTreeNode data = ToolScope.Merge(
            result,
            ("configurationDraftId", Js.Str(stored ? draftId : null)),
            ("valid", JsonTreeNode.FromBoolean(problems.Count == 0 && (plan.Operations.Count == 0 || valid))),
            ("rowsRead", JsonTreeNode.FromInt64(rows.Count)),
            ("counts", Js.Obj(
                ("domainsAdded", JsonTreeNode.FromInt64(added)),
                ("domainsChanged", JsonTreeNode.FromInt64(changed)),
                ("domainsUnchanged", JsonTreeNode.FromInt64(plan.Unchanged)),
                ("matchesAdded", JsonTreeNode.FromInt64(plan.Operations.Count(o => Js.Text(o, "entity") == "vocabulary-mapping"))),
                ("notInSource", JsonTreeNode.FromInt64(notInSource.Count)))),
            ("problems", Arr(problems)),
            ("errors", Arr(errors)),
            ("diffSummary", Arr(diff)),
            ("notInSource", Arr(notInSource)),
            ("preparedOwnerChanges", Arr(plan.OwnerSteps)),
            ("catalogOwnerDifferences", Arr(plan.Differences)));

        string summary = problems.Count > 0
            ? "The import has " + Count(problems.Count, "problem", "problems") + "; nothing was stored. Fix the rows the problems name and import again."
            : plan.Operations.Count == 0
                ? "Every row already matches the configuration in force, so there is nothing to propose." + (notInSource.Count == 0 ? string.Empty : " " + Count(notInSource.Count, "domain in force is", "domains in force are") + " not in the source; none is retired.")
                : !valid
                    ? "The draft would break " + Count(errors.Count, "rule", "rules") + "; nothing was stored. Fix them and import again."
                    : (stored ? "The configuration draft is stored: " : "The import is valid. Nothing was stored (validateOnly): ")
                        + Count(added, "domain added", "domains added") + ", " + Count(changed, "changed", "changed") + ", " + Count(plan.Unchanged, "unchanged", "unchanged") + "."
                        + (plan.OwnerSteps.Count == 0 ? string.Empty : " " + Count(plan.OwnerSteps.Count, "owner from the file waits", "owners from the file wait") + " as a prepared owner change; none is live.")
                        + " Nothing is in force until a person accepts the draft's decision.";
        var steps = new List<NextStep>();
        if (stored && valid)
        {
            steps.Add(ToolScope.Next("submit_record", ("subjectKind", JsonTreeNode.FromString("configuration-draft")), ("subjectId", JsonTreeNode.FromString(draftId!))));
        }

        return s.Answer(data, summary, stored ? s.RecordUrl("configuration-draft", draftId!) : null, steps.ToArray());
    }

    // A rows item as the CSV columns: every member a string, numbers and flags as their text. Unknown members are named.
    private static Dictionary<string, string> RowOf(JsonTreeNode item, int line, List<JsonTreeNode> problems)
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, JsonTreeNode> member in item.Kind == JsonTreeKind.Object ? item.Members : Array.Empty<KeyValuePair<string, JsonTreeNode>>())
        {
            string name = member.Key.Trim().ToLowerInvariant();
            if (Array.IndexOf(ImportColumns, name) < 0)
            {
                problems.Add(ImportProblem(line, member.Key, "The column " + member.Key + " is not one of " + string.Join(", ", ImportColumns) + "."));
                continue;
            }

            string? text = member.Value.Kind == JsonTreeKind.String ? member.Value.GetString() : member.Value.Kind == JsonTreeKind.Null ? null : Js.Write(member.Value);
            if (!string.IsNullOrWhiteSpace(text))
            {
                row[name] = text!.Trim();
            }
        }

        return row;
    }

    // The CSV import_data_taxonomy takes: a header row, then one domain per row. Quoted fields may hold commas, quotes ("") and line
    // breaks. Column names ignore case and spaces around them.
    private static List<Dictionary<string, string>> ReadCsv(ToolScope s, string csv, List<JsonTreeNode> problems)
    {
        List<List<string>> records = CsvRecords(csv);
        var rows = new List<Dictionary<string, string>>();
        if (records.Count == 0)
        {
            return rows;
        }

        List<string> header = records[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        if (!header.Contains("label"))
        {
            throw s.Invalid("csv", "The header row has no label column. The columns are: " + string.Join(", ", ImportColumns) + ".");
        }

        foreach (string unknown in header.Where(h => h.Length > 0 && Array.IndexOf(ImportColumns, h) < 0).Distinct())
        {
            problems.Add(ImportProblem(1, unknown, "The column " + unknown + " is not one of " + string.Join(", ", ImportColumns) + "."));
        }

        for (int r = 1; r < records.Count; r++)
        {
            List<string> fields = records[r];
            if (fields.All(f => f.Trim().Length == 0))
            {
                continue;
            }

            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["#line"] = (r + 1).ToString(CultureInfo.InvariantCulture) };
            for (int c = 0; c < header.Count && c < fields.Count; c++)
            {
                if (Array.IndexOf(ImportColumns, header[c]) >= 0 && fields[c].Trim().Length > 0)
                {
                    row[header[c]] = fields[c].Trim();
                }
            }

            rows.Add(row);
        }

        return rows;
    }

    private static List<List<string>> CsvRecords(string text)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"' && field.Length == 0)
            {
                quoted = true;
            }
            else if (c == ',')
            {
                record.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                record.Add(field.ToString());
                field.Clear();
                records.Add(record);
                record = new List<string>();
            }
            else
            {
                field.Append(c);
            }
        }

        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            records.Add(record);
        }

        return records;
    }

    private static JsonTreeNode ImportProblem(int line, string? field, string message)
    {
        return Js.Obj(("row", JsonTreeNode.FromInt64(line)), ("column", Js.Str(field)), ("message", JsonTreeNode.FromString(message)));
    }

    // The import's work: each row read, then parents, levels and owners resolved over the whole file.
    private sealed class ImportPlan
    {
        private readonly ToolScope _s;
        private readonly DataTree _tree;
        private readonly string _source;
        private readonly List<(int Line, string Key, Dictionary<string, string> Row)> _rows = new();

        public ImportPlan(ToolScope s, DataTree tree, string source)
        {
            _s = s;
            _tree = tree;
            _source = source;
        }

        public List<JsonTreeNode> Problems { get; } = new();

        public List<JsonTreeNode> Operations { get; } = new();

        public List<JsonTreeNode> OwnerSteps { get; } = new();

        public List<JsonTreeNode> Differences { get; } = new();

        public HashSet<string> Keys { get; } = new(StringComparer.Ordinal);

        public int Unchanged { get; private set; }

        public void Read(int index, Dictionary<string, string> row)
        {
            int line = row.TryGetValue("#line", out string? l) && int.TryParse(l, NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : index;
            if (!row.TryGetValue("label", out string? label))
            {
                Problems.Add(ImportProblem(line, "label", "The row has no label. Every domain needs one."));
                return;
            }

            string key = row.TryGetValue("key", out string? given) ? given : Slug(label);
            if (!KeyPathShape.IsMatch(key) || key.IndexOf('/') >= 0)
            {
                Problems.Add(ImportProblem(line, "key", "The key " + key + " is not a key: use lower case letters, digits and hyphens."));
                return;
            }

            if (!Keys.Add(key))
            {
                Problems.Add(ImportProblem(line, "key", "The key " + key + " is on another row too. Each domain is one row."));
                return;
            }

            _rows.Add((line, key, row));
        }

        public void Resolve()
        {
            var parents = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach ((int line, string key, Dictionary<string, string> row) in _rows)
            {
                parents[key] = row.TryGetValue("parent", out string? parent) ? ParentKey(parent, line) : null;
            }

            List<SnapshotRow> levels = _s.ActiveRows("data-domain-level").OrderBy(r => Js.Number(r.Columns, "rank") ?? long.MaxValue).ThenBy(r => r.KeyPath, StringComparer.Ordinal).ToList();
            foreach ((int line, string key, Dictionary<string, string> row) in _rows)
            {
                parents.TryGetValue(key, out string? parent);
                var values = new List<(string, JsonTreeNode?)> { ("label", JsonTreeNode.FromString(row["label"])) };
                if (parent != null)
                {
                    values.Add(("parentKey", JsonTreeNode.FromString(parent)));
                }

                string? level = row.TryGetValue("level", out string? givenLevel) ? givenLevel : null;
                if (level != null && !levels.Any(v => v.KeyPath == level))
                {
                    Problems.Add(ImportProblem(line, "level", "The level " + level + " is not an active data domain level. Use one of " + string.Join(", ", levels.Select(v => v.KeyPath)) + "."));
                }
                else if (level == null && levels.Count > 0)
                {
                    int depth = Depth(key, parents);
                    level = levels[Math.Min(depth, levels.Count - 1)].KeyPath;
                }

                if (level != null)
                {
                    values.Add(("levelKey", JsonTreeNode.FromString(level)));
                }

                if (row.TryGetValue("classification", out string? classification))
                {
                    string? resolved = Classification(classification);
                    if (resolved == null)
                    {
                        Problems.Add(ImportProblem(line, "classification", "The classification " + classification + " is neither a classification key nor a Purview label name the configuration matches."));
                    }
                    else
                    {
                        values.Add(("classificationKey", JsonTreeNode.FromString(resolved)));
                    }
                }

                Rule(row, line, "use_rule", "useRule", values);
                Rule(row, line, "ai_rule", "aiUseRule", values);
                Rule(row, line, "copy_rule", "copyToTestRule", values);
                if (row.TryGetValue("retention", out string? retention))
                {
                    if (_s.ActiveRows("list-value").Any(v => v.ParentKey == "retention-class" && v.Key == retention))
                    {
                        values.Add(("retentionClassKey", JsonTreeNode.FromString(retention)));
                    }
                    else
                    {
                        Problems.Add(ImportProblem(line, "retention", "The retention " + retention + " is not a value of the retention-class list."));
                    }
                }

                if (row.TryGetValue("description", out string? description))
                {
                    values.Add(("description", JsonTreeNode.FromString(description)));
                }

                if (row.TryGetValue("examples", out string? examples))
                {
                    values.Add(("examples", JsonTreeNode.FromString(examples.Length > 500 ? examples.Substring(0, 500) : examples)));
                }

                Domain(key, values);
                Match(key, row, line);
                Owners(key, row, line);
            }
        }

        // Adds the domain's upsert unless every value equals the domain in force (a re-import changes nothing).
        private void Domain(string key, List<(string Name, JsonTreeNode? Value)> values)
        {
            SnapshotRow? current = _s.ConfigRow("data-domain", key);
            if (current != null && current.IsActive && values.All(v => v.Value != null && Js.Member(current.Columns, v.Name) is JsonTreeNode have && Js.Write(have) == Js.Write(v.Value)))
            {
                Unchanged++;
                return;
            }

            Operations.Add(Js.Obj(("op", JsonTreeNode.FromString("upsert")), ("entity", JsonTreeNode.FromString("data-domain")), ("key", JsonTreeNode.FromString(key)), ("values", Js.Obj(values.ToArray()))));
        }

        // A C11 match to the outside domain the row names (match_vocabulary, match_term, match_url).
        private void Match(string key, Dictionary<string, string> row, int line)
        {
            if (!row.TryGetValue("match_term", out string? term))
            {
                return;
            }

            string vocabulary = row.TryGetValue("match_vocabulary", out string? v) ? v : _source == "purview" ? "purview-governance-domain" : "fabric-domain";
            if (!_s.ActiveRows("list-value").Any(x => x.ParentKey == "vocabulary" && x.Key == vocabulary))
            {
                Problems.Add(ImportProblem(line, "match_vocabulary", "The vocabulary " + vocabulary + " is not a value of the vocabulary list."));
                return;
            }

            if (!row.TryGetValue("match_url", out string? url) || !url.StartsWith("https://", StringComparison.Ordinal))
            {
                Problems.Add(ImportProblem(line, "match_url", "A match needs the https link where the outside domain is defined."));
                return;
            }

            string matchKey = key + "-" + vocabulary;
            SnapshotRow? current = _s.ConfigRow("vocabulary-mapping", matchKey);
            if (current != null && current.IsActive && Js.Text(current.Columns, "term") == term && Js.Text(current.Columns, "sourceUrl") == url)
            {
                return;
            }

            Operations.Add(Js.Obj(("op", JsonTreeNode.FromString("upsert")), ("entity", JsonTreeNode.FromString("vocabulary-mapping")), ("key", JsonTreeNode.FromString(matchKey)), ("values", Js.Obj(
                ("label", JsonTreeNode.FromString(row["label"] + " as " + (Js.Text(ListValue(_s, "vocabulary", vocabulary), "label") ?? vocabulary))),
                ("sourceEntity", JsonTreeNode.FromString("data-domain")),
                ("sourceKey", JsonTreeNode.FromString(key)),
                ("vocabularyKey", JsonTreeNode.FromString(vocabulary)),
                ("term", JsonTreeNode.FromString(term)),
                ("sourceUrl", JsonTreeNode.FromString(url)),
                ("checkedOn", JsonTreeNode.FromString(_s.Today))))));
        }

        // owner, deputy and steward become prepared propose_owner_change steps, never live owners; DC-15 where the file
        // names a data owner by object ID and the domain's current one is someone else.
        private void Owners(string key, Dictionary<string, string> row, int line)
        {
            foreach ((string column, string role, bool deputy) in new[] { ("owner", EngineKeys.DataOwnerRole, false), ("deputy", EngineKeys.DataOwnerRole, true), ("steward", EngineKeys.DataStewardRole, false) })
            {
                if (!row.TryGetValue(column, out string? person))
                {
                    continue;
                }

                bool isObjectId = ObjectIdShape.IsMatch(person);
                if (!isObjectId && person.IndexOf('@') < 0)
                {
                    Problems.Add(ImportProblem(line, column, "The " + column + " " + person + " is neither a user principal name nor an Entra object ID."));
                    continue;
                }

                List<StoredRow> current = _s.Query("owner-assignment", ("subjectDataDomain", key))
                    .Where(o => o.Text("ownerRole") == role && o.Flag("isDeputy") == deputy && o.Text("endOn") == null).ToList();
                bool alreadyHolds = isObjectId && current.Any(o => Same(o.Text("principal"), person));
                if (alreadyHolds)
                {
                    continue;
                }

                JsonTreeNode change = Js.Obj(("ownerRoleKey", JsonTreeNode.FromString(role)), ("isDeputy", JsonTreeNode.FromBoolean(deputy)), ("addObjectId", isObjectId ? JsonTreeNode.FromString(person) : null));
                OwnerSteps.Add(Js.Obj(
                    ("domainKey", JsonTreeNode.FromString(key)),
                    ("ownerRoleKey", JsonTreeNode.FromString(role)),
                    ("isDeputy", JsonTreeNode.FromBoolean(deputy)),
                    ("objectId", isObjectId ? JsonTreeNode.FromString(person) : JsonTreeNode.Null),
                    ("userPrincipalName", isObjectId ? JsonTreeNode.Null : JsonTreeNode.FromString(person)),
                    ("readyToSend", JsonTreeNode.FromBoolean(isObjectId)),
                    ("tool", JsonTreeNode.FromString("propose_owner_change")),
                    ("input", isObjectId ? Js.Obj(
                        ("dataDomainKey", JsonTreeNode.FromString(key)),
                        ("changes", Arr(new[] { change })),
                        ("reason", JsonTreeNode.FromString("Named in the imported domain list."))) : JsonTreeNode.Null),
                    ("note", JsonTreeNode.FromString(isObjectId
                        ? "Send it once the draft is accepted and the domain is in force."
                        : "Find the person's object ID with your own directory access, then send the owner change once the domain is in force."))));

                if (isObjectId && !deputy && role == EngineKeys.DataOwnerRole && current.Count > 0)
                {
                    StoredRow holder = current[0];
                    Differences.Add(Js.Obj(
                        ("check", JsonTreeNode.FromString("DC-15")),
                        ("domainKey", JsonTreeNode.FromString(key)),
                        ("ownerRoleKey", JsonTreeNode.FromString(role)),
                        ("catalogObjectId", JsonTreeNode.FromString(person)),
                        ("currentHolder", _s.PrincipalRef(holder.Text("principal"))),
                        ("message", JsonTreeNode.FromString("The catalog names another data owner for " + (_tree.Domain(key) == null ? row["label"] : _tree.PathText(key)) + " than the product holds (" + _s.DisplayLabel(holder.Text("principal") ?? string.Empty) + "). The catalog wins for the data item: check it, then change the owner through an owner change."))));
                }
            }
        }

        // A data handling rule column: one of the contract's values.
        private void Rule(Dictionary<string, string> row, int line, string column, string field, List<(string, JsonTreeNode?)> values)
        {
            if (!row.TryGetValue(column, out string? rule))
            {
                return;
            }

            if (EngineContract.DataHandlingRules.TryParse(rule, out _))
            {
                values.Add((field, JsonTreeNode.FromString(rule)));
            }
            else
            {
                Problems.Add(ImportProblem(line, column, "The rule " + rule + " is not one of " + string.Join(", ", EngineContract.DataHandlingRules.WireValues) + "."));
            }
        }

        // A parent key: a key of the file or of the tree, or a path of labels from the top ("People/Payroll").
        private string? ParentKey(string parent, int line)
        {
            if (parent.IndexOf('/') < 0 && (Keys.Contains(parent) || _tree.Domain(parent) != null))
            {
                return parent;
            }

            string? at = null;
            foreach (string part in parent.Split('/').Select(p => p.Trim()).Where(p => p.Length > 0))
            {
                string? next = _rows.Where(r => string.Equals(r.Row["label"], part, StringComparison.OrdinalIgnoreCase) && ParentOf(r.Row) == at).Select(r => r.Key).FirstOrDefault()
                    ?? _tree.Domains.Where(d => string.Equals(DataTree.Label(d), part, StringComparison.OrdinalIgnoreCase) && _tree.ParentKey(d.KeyPath) == at).Select(d => d.KeyPath).FirstOrDefault();
                if (next == null)
                {
                    Problems.Add(ImportProblem(line, "parent", "The parent " + parent + " names no domain of the file or of the configuration in force."));
                    return null;
                }

                at = next;
            }

            return at;
        }

        // The parent a file row names by key, for the path lookup (a path parent is resolved by its own row first).
        private string? ParentOf(Dictionary<string, string> row)
        {
            return row.TryGetValue("parent", out string? parent) && parent.IndexOf('/') < 0 ? parent : null;
        }

        private int Depth(string key, Dictionary<string, string?> parents)
        {
            int depth = 0;
            string? at = key;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (at != null && seen.Add(at))
            {
                string? parent = parents.TryGetValue(at, out string? p) ? p : _tree.ParentKey(at);
                if (parent == null)
                {
                    break;
                }

                depth++;
                at = parent;
            }

            return depth;
        }

        // A classification key, or a Purview label name the C11 matches resolve to a classification.
        private string? Classification(string value)
        {
            if (_s.ConfigRow("data-classification", value) is SnapshotRow row && row.IsActive)
            {
                return row.KeyPath;
            }

            return _s.ActiveRows("vocabulary-mapping")
                .Where(m => Js.Text(m.Columns, "sourceEntity") == "data-classification" && string.Equals(Js.Text(m.Columns, "term"), value, StringComparison.OrdinalIgnoreCase))
                .Select(m => Js.Text(m.Columns, "sourceKey")).FirstOrDefault();
        }

        private static string Slug(string label)
        {
            string slug = Regex.Replace(label.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
            return slug.Length == 0 ? "domain" : slug.Length > 60 ? slug.Substring(0, 60).TrimEnd('-') : slug;
        }
    }
}
