using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Operations;
using Product.Engine.Core.Reasons;
using Product.Engine.Core.Snapshots;
using Product.Engine.Runtime;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Json;

namespace Product.Mcp.Core.Backend.InProcess;

// The data toolset, reads (tools 76 to 81; docs/architecture/data-taxonomy.md; data taxonomy slice 3, D-37 and
// D-41). The tree, the classifications and the rules come from the snapshot in force (C47 to C50); owners come from
// OP-15, decision states from OP-16 and findings from OP-17, each run inside the tool's own composed call, so a read
// sees exactly what the engine would judge. Nothing here writes.
internal static partial class ToolHandlers
{
    private static readonly string[] OpenMeaningsOfDataRows = { "draft", "registered" };

    // ---- 76. find_data_domains ----

    // The tree in order (each level by Sort Order, then label), filtered by words, level and effective classification,
    // then by owner and gaps, which OP-15 answers for every candidate at once.
    private static JsonTreeNode FindDataDomains(ToolScope s)
    {
        var tree = new DataTree(s.Snapshot);
        string? text = s.Text("text")?.Trim().ToLowerInvariant();
        IReadOnlyList<string> levels = s.Strings("levelKeys");
        IReadOnlyList<string> classes = s.Strings("classificationKeys");
        string? owner = s.Text("ownerObjectId");
        bool gapsOnly = s.Flag("gapsOnly") == true;
        bool retired = s.Flag("includeRetired") == true;
        List<SnapshotRow> candidates = (retired ? tree.Domains : tree.ActiveDomains)
            .Where(d => levels.Count == 0 || levels.Contains(Js.Text(d.Columns, "levelKey") ?? string.Empty, StringComparer.Ordinal))
            .Where(d => classes.Count == 0 || (tree.Classification(d.KeyPath)?.Key is string c && classes.Contains(c, StringComparer.Ordinal)))
            .Where(d => text == null || (tree.PathText(d.KeyPath) + " " + (Js.Text(d.Columns, "examples") ?? string.Empty) + " " + d.KeyPath).ToLowerInvariant().IndexOf(text, StringComparison.Ordinal) >= 0)
            .OrderBy(d => TreeOrder(tree, d.KeyPath), StringComparer.Ordinal)
            .ToList();
        // The owners of the domains and the people behind them in two reads, not two per domain (wave 4b review).
        s.Call.Prefetch("owner-assignment", "subjectDataDomain", candidates.Select(d => d.KeyPath));
        s.Call.PrefetchAll("principal-reference");
        IReadOnlyList<EvaluationResult<JsonTreeNode?>> resolved = s.Call.DataDomains(candidates.Select(d => d.KeyPath).ToList());
        DataCountsIndex counts = DataCounts(s);
        var items = new List<JsonTreeNode>();
        for (int i = 0; i < candidates.Count; i++)
        {
            JsonTreeNode? payload = resolved[i].Payload;
            JsonTreeNode item = DomainItem(s, tree, candidates[i].KeyPath, payload, counts);
            if (gapsOnly && Js.Items(item, "gaps").Count == 0)
            {
                continue;
            }

            if (owner != null && !new[] { "owner.principal.objectId", "deputy.objectId", "steward.principal.objectId" }.Any(p => Same(PathText(item, p), owner)))
            {
                continue;
            }

            items.Add(item);
        }

        List<JsonTreeNode> page = s.Page(items, out string? next);
        string summary = tree.Domains.Any()
            ? Count(items.Count, "data domain matches", "data domains match") + (items.Count(i => Js.Items(i, "gaps").Count > 0) is int gaps && gaps > 0 ? "; " + Count(gaps, "has an owner gap", "have an owner gap") : string.Empty) + "."
            : "This install has no data domains yet. The configuration admin draws the tree through a configuration change (propose_configuration_change).";
        return s.Answer(Js.Obj(("items", Arr(page)), ("totalCount", JsonTreeNode.FromInt64(items.Count)), ("skipToken", Js.OptStr(next))), summary);
    }

    // ---- 77. get_data_domain ----

    private static JsonTreeNode GetDataDomain(ToolScope s)
    {
        var tree = new DataTree(s.Snapshot);
        string key = s.RequireText("domainKey");
        SnapshotRow domain = tree.Domain(key) ?? throw s.NotFound("Data Domain", key, "domainKey");
        var sections = new HashSet<string>(s.Strings("sections"), StringComparer.Ordinal);
        bool Want(string section) => sections.Count == 0 || sections.Contains(section);
        JsonTreeNode? resolved = s.Call.DataDomains(new[] { key })[0].Payload;
        DataCountsIndex counts = DataCounts(s);
        (string? retention, string? retentionOn) = tree.Retention(key);
        (string? question, string? answer) = tree.ImpliedAnswer(key);

        JsonTreeNode domainJson = Js.Obj(
            ("key", JsonTreeNode.FromString(key)),
            ("label", JsonTreeNode.FromString(DataTree.Label(domain))),
            ("path", JsonTreeNode.FromString(tree.PathText(key))),
            ("pathKeys", Arr(tree.Chain(key).Reverse().Select(n => Js.Obj(
                ("key", JsonTreeNode.FromString(n.KeyPath)),
                ("label", JsonTreeNode.FromString(DataTree.Label(n))),
                ("level", s.LabelledKey("data-domain-level", Js.Text(n.Columns, "levelKey"))))))),
            ("parentKey", Js.Str(tree.ParentKey(key))),
            ("level", s.LabelledKey("data-domain-level", Js.Text(domain.Columns, "levelKey"))),
            ("isActive", JsonTreeNode.FromBoolean(domain.IsActive)),
            ("declarable", JsonTreeNode.FromBoolean(tree.IsDeclarable(key))),
            ("ownerRequired", JsonTreeNode.FromBoolean(tree.OwnerRequired(key))),
            ("description", Js.Str(Js.Text(domain.Columns, "description"))),
            ("examples", Js.Str(Js.Text(domain.Columns, "examples"))),
            ("classification", ClassificationValue(s, tree, key)),
            ("rules", DomainRules(s, tree, key)),
            ("retention", retention == null ? JsonTreeNode.Null : Js.Obj(("retentionClass", ListValue(s, "retention-class", retention)), ("setOn", DomainKeyLabel(tree, retentionOn)))),
            ("impliedAnswer", ImpliedAnswerValue(s, tree, question, answer, key)),
            ("children", Arr(tree.Domains.Where(d => tree.ParentKey(d.KeyPath) == key).OrderBy(d => TreeOrder(tree, d.KeyPath), StringComparer.Ordinal).Select(d => Js.Obj(
                ("key", JsonTreeNode.FromString(d.KeyPath)),
                ("label", JsonTreeNode.FromString(DataTree.Label(d))),
                ("isActive", JsonTreeNode.FromBoolean(d.IsActive)))))),
            ("counts", counts.For(key)),
            ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("data-domain", key))));

        List<StoredRow> mappings = s.Query("data-mapping", ("dataDomain", key)).ToList();
        List<StoredRow> uses = s.Query("data-use", ("dataDomain", key)).ToList();
        if (Want("uses") || Want("mappings"))
        {
            s.Call.PrefetchDataRows((Want("uses") ? uses.Select(u => u.Id) : Enumerable.Empty<string>()).Concat(Want("mappings") ? mappings.Select(m => m.Id) : Enumerable.Empty<string>()));
        }
        JsonTreeNode owners = !Want("owners") ? JsonTreeNode.Null : Arr(Js.Items(resolved, "owners").Select(o => Js.Obj(
            ("ownerRole", s.LabelledKey("owner-role", Js.Text(o, "ownerRoleKey"))),
            ("inheritsDown", JsonTreeNode.FromBoolean(Js.Flag(o, "inheritsDown") == true)),
            ("holder", Js.Member(o, "holder") is JsonTreeNode h && h.Kind == JsonTreeKind.Object ? PersonAt(s, h) : JsonTreeNode.Null),
            ("otherHolders", Arr(Js.Items(o, "otherHolders").Select(p => PersonAt(s, p)))),
            ("deputies", Arr(Js.Items(o, "deputies").Select(p => PersonAt(s, p)))),
            ("inheritedFrom", DomainKeyLabel(tree, Js.Text(o, "inheritedFrom"))),
            ("acting", s.PrincipalRef(Js.Text(o, "actingPrincipalId"))))));
        JsonTreeNode mappingList = !Want("mappings") ? JsonTreeNode.Null : Arr(mappings.Where(OpenOn(s)).OrderBy(m => m.Id, StringComparer.Ordinal).Select(m => MappingItem(s, tree, m)));
        JsonTreeNode useList = !Want("uses") ? JsonTreeNode.Null : Arr(uses.Where(OpenOn(s)).OrderBy(u => u.Text("recordNumber") ?? u.Id, StringComparer.Ordinal).Select(u => UseItem(s, tree, u)));
        JsonTreeNode decisions = JsonTreeNode.Null;
        if (Want("decisions"))
        {
            var useIds = new HashSet<string>(uses.Select(u => u.Id), StringComparer.OrdinalIgnoreCase);
            var mappingIds = new HashSet<string>(mappings.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
            decisions = Arr(NewestFirst(s.Rows.Query(RowQuery.For("decision")).Where(d => d.Text("subjectDataDomain") == key
                    || (d.Text("subjectDataUse") is string u && useIds.Contains(u))
                    || (d.Text("subjectDataMapping") is string m && mappingIds.Contains(m))))
                .Take(50)
                .Select(d => Js.Obj(
                    ("decisionId", JsonTreeNode.FromString(d.Id)),
                    ("recordNumber", Js.Str(d.Text("recordNumber"))),
                    ("decisionType", s.LabelledKey("decision-type", d.Text("decisionType") ?? "unknown")),
                    ("subjectKind", JsonTreeNode.FromString(d.Text("subjectKind") ?? "business-application")),
                    ("subjectId", JsonTreeNode.FromString(SubjectIdOf(d) ?? d.Id)),
                    ("statement", JsonTreeNode.FromString(d.Text("statement") ?? string.Empty)),
                    ("status", s.StatusValue(d) is JsonTreeNode st && st.Kind != JsonTreeKind.Null ? st : ToolScope.StatusValue("proposed", null)),
                    ("outcome", DecisionOutcome(s, d)),
                    ("decidedOn", ToolScope.MomentOrNull(d.Text("decidedOn"))),
                    ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("decision", d.Id))))));
        }

        JsonTreeNode matches = !Want("matches") ? JsonTreeNode.Null : Arr(s.ActiveRows("vocabulary-mapping")
            .Where(v => Js.Text(v.Columns, "sourceEntity") == "data-domain" && Js.Text(v.Columns, "sourceKey") == key)
            .OrderBy(v => v.KeyPath, StringComparer.Ordinal)
            .Select(v => Js.Obj(
                ("vocabularyKey", JsonTreeNode.FromString(Js.Text(v.Columns, "vocabularyKey") ?? "unknown")),
                ("term", JsonTreeNode.FromString(Js.Text(v.Columns, "term") ?? v.KeyPath)),
                ("url", HttpsOrNull(Js.Text(v.Columns, "sourceUrl"))),
                ("checkedOn", ToolScope.DateOrNull(Js.Text(v.Columns, "checkedOn"))))));
        JsonTreeNode gaps = !Want("gaps") ? JsonTreeNode.Null : Arr(DataFindings(s, tree, Js.Obj(("domainKey", JsonTreeNode.FromString(key)), ("includeSubtree", JsonTreeNode.False))));

        JsonTreeNode data = Js.Obj(("domain", domainJson), ("owners", owners), ("mappings", mappingList), ("uses", useList), ("decisions", decisions), ("matches", matches), ("gaps", gaps));
        string ownerWords = PathText(DomainItem(s, tree, key, resolved, counts), "owner.principal.displayLabel") is string ownerName && ownerName.Length > 0 ? " Data owner: " + ownerName + "." : " It has no data owner.";
        return s.Answer(data, tree.PathText(key) + (domain.IsActive ? string.Empty : " (retired)") + "." + ownerWords, s.RecordUrl("data-domain", key),
            ToolScope.Next("get_data_gaps", ("domainKey", JsonTreeNode.FromString(key))));
    }

    // ---- 97. list_data_uses ----

    // The data uses across every domain in one list (wave 4, B24-21): the Data page's Uses tab, a person's own uses, and
    // one use by its ID for its record page. Open uses by default; includeEnded adds the ended and archived ones. Each item
    // carries its decision state from OP-16, computed when read; counts.byState is for the same filters before the state
    // filter, so each count opens its rows. The facts OP-16 needs are loaded ahead in a few store calls (B25-23).
    private static JsonTreeNode ListDataUses(ToolScope s)
    {
        var tree = new DataTree(s.Snapshot);
        List<StoredRow> candidates = DataRowsInScope(s, tree, "data-use", "dataUseIds");
        string? app = s.Text("businessApplicationId");
        if (app != null)
        {
            RequireApplication(s);
        }

        bool? reachesAi = s.Flag("reachesAi");
        IReadOnlyList<string> access = s.Strings("accessKeys");
        candidates = candidates
            .Where(u => app == null || Same(u.Text("businessApplication"), app))
            .Where(u => !reachesAi.HasValue || u.Flag("reachesAi") == reachesAi.Value)
            .Where(u => access.Count == 0 || access.Contains(u.Text("access") ?? string.Empty, StringComparer.Ordinal))
            .ToList();
        string? text = s.Text("text")?.Trim().ToLowerInvariant();

        // The whole list with each use's OP-16 state, kept for the later pages of the same query (wave 4b review).
        List<(StoredRow Row, JsonTreeNode Item)> evaluated = s.PagedList(() =>
        {
            s.Call.PrefetchDataRows(candidates.Select(u => u.Id));
            s.Call.PrefetchAll("principal-reference");
            var all = new List<(StoredRow Row, JsonTreeNode Item)>();
            foreach (StoredRow use in candidates)
            {
                JsonTreeNode item = UseItem(s, tree, use);
                if (text != null && !(Js.Text(item, "applicationName") + " " + PathText(item, "domain.path") + " " + use.Text("purpose") + " " + use.Text("recordNumber") + " " + use.Text("aiServiceAsSeen")).ToLowerInvariant().Contains(text))
                {
                    continue;
                }

                all.Add((use, item));
            }

            return all;
        });

        IReadOnlyList<string> states = s.Strings("states");
        var ordered = evaluated
            .Where(e => states.Count == 0 || states.Contains(Js.Text(e.Item, "state") ?? string.Empty, StringComparer.Ordinal))
            .OrderBy(e => Js.Text(e.Item, "applicationName") ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => DomainOrder(tree, e.Row.Text("dataDomain")), StringComparer.Ordinal)
            .ThenBy(e => e.Row.Text("recordNumber") ?? e.Row.Id, StringComparer.Ordinal)
            .Select(e => e.Item)
            .ToList();
        List<JsonTreeNode> page = s.Page(ordered, out string? next);
        JsonTreeNode byState = Js.Obj(StateKeys.Select(k => new KeyValuePair<string, JsonTreeNode>(k, JsonTreeNode.FromInt64(evaluated.Count(e => Js.Text(e.Item, "state") == k)))));
        int needs = evaluated.Count(e => Js.Text(e.Item, "state") is "needed" or "no-one-to-ask" or "re-decide" or "breaks-rule");
        return s.Answer(
            Js.Obj(("items", Arr(page)), ("counts", Js.Obj(("all", JsonTreeNode.FromInt64(evaluated.Count)), ("byState", byState))), ("totalCount", JsonTreeNode.FromInt64(ordered.Count)), ("skipToken", Js.OptStr(next))),
            Count(ordered.Count, "data use matches", "data uses match") + (needs == 0 ? "." : "; " + Count(needs, "needs", "need") + " a decision or a change."));
    }

    // ---- 98. list_data_mappings ----

    // The data mappings across every domain in one list (wave 4, B24-21): the Data page's Mapping tab, the mappings of one
    // location, and one mapping by its ID for its record page. Open mappings by default; includeEnded adds the ended and
    // archived ones. A copy outside production carries its copy state from OP-16, computed when read.
    private static JsonTreeNode ListDataMappings(ToolScope s)
    {
        var tree = new DataTree(s.Snapshot);
        List<StoredRow> candidates = DataRowsInScope(s, tree, "data-mapping", "dataMappingIds");
        string? location = s.Text("dataLocationId");
        if (location != null)
        {
            s.RequireRow("data-location", location, "dataLocationId");
        }

        IReadOnlyList<string> origins = s.Strings("originKeys");
        candidates = candidates
            .Where(m => location == null || Same(m.Text("dataLocation"), location))
            .Where(m => origins.Count == 0 || origins.Contains(m.Text("dataOrigin") ?? string.Empty, StringComparer.Ordinal))
            .ToList();
        s.Call.PrefetchDataRows(candidates.Select(m => m.Id));
        s.Call.PrefetchAll("principal-reference");
        string? text = s.Text("text")?.Trim().ToLowerInvariant();
        IReadOnlyList<string> copyStates = s.Strings("copyStates");
        var items = new List<(StoredRow Row, JsonTreeNode Item)>();
        foreach (StoredRow mapping in candidates)
        {
            JsonTreeNode item = MappingItem(s, tree, mapping);
            if (text != null && !(PathText(item, "domain.path") + " " + PathText(item, "location.label")).ToLowerInvariant().Contains(text))
            {
                continue;
            }

            items.Add((mapping, item));
        }

        var ordered = items
            .Where(e => copyStates.Count == 0 || copyStates.Contains(Js.Text(e.Item, "copyState") ?? string.Empty, StringComparer.Ordinal))
            .OrderBy(e => DomainOrder(tree, e.Row.Text("dataDomain")), StringComparer.Ordinal)
            .ThenBy(e => PathText(e.Item, "location.label") ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Row.Id, StringComparer.Ordinal)
            .Select(e => e.Item)
            .ToList();
        List<JsonTreeNode> page = s.Page(ordered, out string? next);
        int copies = items.Count(e => Js.Text(e.Item, "copyState") != null);
        return s.Answer(
            Js.Obj(("items", Arr(page)), ("counts", Js.Obj(("all", JsonTreeNode.FromInt64(items.Count)), ("copies", JsonTreeNode.FromInt64(copies)))), ("totalCount", JsonTreeNode.FromInt64(ordered.Count)), ("skipToken", Js.OptStr(next))),
            Count(ordered.Count, "data mapping matches", "data mappings match") + (copies == 0 ? "." : "; " + Count(copies, "is a copy", "are copies") + " outside production."));
    }

    // The decision states a list counts (dataUseState in common.schema.json).
    private static readonly string[] StateKeys = { "not-needed", "needed", "proposed", "allowed", "denied", "re-decide", "breaks-rule", "no-one-to-ask" };

    // A domain's place in the tree order, or its key when the tree no longer has it.
    private static string DomainOrder(DataTree tree, string? key)
    {
        return key == null ? string.Empty : tree.Domain(key) == null ? key : TreeOrder(tree, key);
    }

    // The rows of a data entity a list starts from: the IDs asked for (each must exist), else every row; then the domain
    // filter (its subtree unless includeSubtree is false) and, unless includeEnded or IDs are given, open rows only (not
    // ended, retired or archived). A domain key that is not in the tree is refused on its field.
    private static List<StoredRow> DataRowsInScope(ToolScope s, DataTree tree, string entity, string idsField)
    {
        IReadOnlyList<string> ids = s.Strings(idsField);
        List<StoredRow> rows;
        if (ids.Count > 0)
        {
            rows = s.Rows.GetMany(entity, ids).ToList();
            string? missing = ids.FirstOrDefault(id => !rows.Any(r => Same(r.Id, id)));
            if (missing != null)
            {
                throw s.NotFound(ToolScope.EntityName(entity), missing, idsField);
            }
        }
        else
        {
            rows = s.Rows.Query(RowQuery.For(entity)).ToList();
        }

        string? domain = s.Text("domainKey");
        if (domain != null)
        {
            if (tree.Domain(domain) == null)
            {
                throw s.Invalid("domainKey", "domainKey \"" + domain + "\" is not a data domain of the configuration in force.");
            }

            var keys = new HashSet<string>(s.Flag("includeSubtree") == false ? new[] { domain } : tree.Subtree(domain), StringComparer.Ordinal);
            rows = rows.Where(r => keys.Contains(r.Text("dataDomain") ?? string.Empty)).ToList();
        }

        bool ended = s.Flag("includeEnded") == true || ids.Count > 0;
        return ended ? rows : rows.Where(OpenOn(s)).ToList();
    }

    // ---- 78. find_data_locations ----

    private static JsonTreeNode FindDataLocations(ToolScope s)
    {
        var tree = new DataTree(s.Snapshot);
        string? one = s.Text("locationId");
        IReadOnlyList<string> platforms = s.Strings("platformKeys");
        IReadOnlyList<string> stages = s.Strings("stageKeys");
        string? parent = s.Text("parentLocationId");
        string? text = s.Text("text")?.Trim().ToLowerInvariant();
        bool unmappedOnly = s.Flag("unmappedOnly") == true;
        bool staleOnly = s.Flag("staleOnly") == true;
        bool retired = s.Flag("includeRetired") == true || one != null;
        bool ids = MayReadToolIds(s);
        if (one != null)
        {
            s.RequireRow("data-location", one, "locationId");
        }

        var all = s.Rows.Query(RowQuery.For("data-location")).ToList();

        // Each location's mappings and owners, the people and the applications, in a few reads (wave 4b review).
        s.Call.Prefetch("data-mapping", "dataLocation", all.Select(l => l.Id));
        s.Call.Prefetch("owner-assignment", "subjectDataLocation", all.Select(l => l.Id));
        s.Call.PrefetchAll("principal-reference");
        var items = new List<JsonTreeNode>();
        foreach (StoredRow location in all
            .Where(l => one == null || Same(l.Id, one))
            .Where(l => retired || (l.Text("retiredOn") == null && l.Text("statusMeaning") != "archived"))
            .Where(l => platforms.Count == 0 || platforms.Contains(l.Text("dataPlatform") ?? string.Empty, StringComparer.Ordinal))
            .Where(l => parent == null || Same(l.Text("parentLocation"), parent))
            .Where(l => stages.Count == 0 || stages.Contains(LocationStageOf(s, l) ?? string.Empty, StringComparer.Ordinal))
            .Where(l => text == null || ((l.Text("labelAsSeen") ?? string.Empty) + " " + (ids ? l.Text("externalId") ?? string.Empty : string.Empty)).ToLowerInvariant().IndexOf(text, StringComparison.Ordinal) >= 0)
            .OrderBy(l => s.Label("data-platform", l.Text("dataPlatform")), StringComparer.OrdinalIgnoreCase)
            .ThenBy(l => LocationSortKey(s, l), StringComparer.OrdinalIgnoreCase)
            .ThenBy(l => l.Id, StringComparer.Ordinal))
        {
            JsonTreeNode item = LocationItem(s, tree, location, ids);
            if ((unmappedOnly && Js.Flag(Js.Member(item, "flags"), "unmapped") != true) || (staleOnly && Js.Flag(Js.Member(item, "flags"), "stale") != true))
            {
                continue;
            }

            items.Add(item);
        }

        List<JsonTreeNode> page = s.Page(items, out string? next);
        string summary = one != null
            ? (items.Count == 1 ? Js.Text(items[0], "label") + ": " + Count(Js.Items(items[0], "domains").Count, "domain", "domains") + " mapped here." : "The location is not shown.")
            : Count(items.Count, "data location matches", "data locations match") + ".";
        return s.Answer(Js.Obj(("items", Arr(page)), ("totalCount", JsonTreeNode.FromInt64(items.Count)), ("skipToken", Js.OptStr(next))), summary,
            one != null ? s.RecordUrl("data-location", one) : null);
    }

    // ---- 79. resolve_data ----

    // The build loop's first question (docs/architecture/data-taxonomy.md, In the AI build loop): which domain is this table, object or file? The
    // location is found by its row ID, or by its external ID or URL among locations not retired (narrowed by platform).
    // Mappings that name the container come first; a mapping with no container names covers the whole location.
    private static JsonTreeNode ResolveData(ToolScope s)
    {
        var tree = new DataTree(s.Snapshot);
        // A caller who may not read External Id or Url cannot confirm a guessed one either: the lookup by them answers as
        // unknown (security review, minor). The row ID lookup stays open to every role.
        bool ids = MayReadToolIds(s);
        StoredRow? location = FindLocation(s, s.Text("locationId"), ids ? s.Text("externalId") : null, ids ? s.Text("url") : null, s.Text("platformKey"));
        string? container = s.Text("container")?.Trim();
        if (location == null)
        {
            return s.Answer(
                Js.Obj(("location", JsonTreeNode.Null), ("matches", Js.EmptyArray), ("unmapped", JsonTreeNode.True)),
                "No registered data location matches. Register it with save_data_location, then map its data with save_data_mapping.",
                null,
                ToolScope.Next("save_data_location"));
        }

        var open = s.Query("data-mapping", ("dataLocation", location.Id)).Where(OpenOn(s)).ToList();
        var matched = new List<(StoredRow Mapping, bool Named)>();
        foreach (StoredRow mapping in open)
        {
            IReadOnlyList<string> containers = Containers(mapping);
            bool named = container != null && containers.Any(c => string.Equals(c, container, StringComparison.OrdinalIgnoreCase));
            if (container == null || named || containers.Count == 0)
            {
                matched.Add((mapping, named));
            }
        }

        var ordered = matched.OrderBy(m => m.Named ? 0 : 1).ThenBy(m => tree.PathText(m.Mapping.Text("dataDomain") ?? string.Empty), StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Mapping.Id, StringComparer.Ordinal).ToList();
        List<string> domainKeys = ordered.Select(m => m.Mapping.Text("dataDomain")).Where(k => k != null && tree.Domain(k) != null).Select(k => k!).Distinct(StringComparer.Ordinal).ToList();
        IReadOnlyList<EvaluationResult<JsonTreeNode?>> resolved = s.Call.DataDomains(domainKeys);
        var byDomain = new Dictionary<string, JsonTreeNode?>(StringComparer.Ordinal);
        for (int i = 0; i < domainKeys.Count; i++)
        {
            byDomain[domainKeys[i]] = resolved[i].Payload;
        }

        JsonTreeNode matches = Arr(ordered.Where(m => m.Mapping.Text("dataDomain") is string k && tree.Domain(k) != null).Select(m =>
        {
            string key = m.Mapping.Text("dataDomain")!;
            return Js.Obj(
                ("domain", DomainRef(tree, key)),
                ("dataMappingId", JsonTreeNode.FromString(m.Mapping.Id)),
                ("origin", JsonTreeNode.FromString(m.Mapping.Text("dataOrigin") ?? "system-of-record")),
                ("containers", Js.Strs(Containers(m.Mapping))),
                ("containerMatched", JsonTreeNode.FromBoolean(m.Named)),
                ("owner", OwnerValue(s, tree, byDomain[key], EngineKeys.DataOwnerRole)),
                ("classification", ClassificationValue(s, tree, key)),
                ("rules", DomainRules(s, tree, key)));
        }));
        bool unmapped = matches.Items.Count == 0;
        string where = location.Text("labelAsSeen") ?? location.Id;
        string summary = matches.Items.Count == 0
            ? (container == null ? where + " has no data mapped yet." : container + " in " + where + " is not mapped to a data domain.") + " Map it with save_data_mapping."
            : (container ?? where) + " is " + string.Join(", and ", matches.Items.Select(m => PathText(m, "domain.path") + (PathText(m, "owner.principal.displayLabel") is string o && o.Length > 0 ? ", owned by " + o : ", with no data owner"))) + ".";
        return s.Answer(
            Js.Obj(("location", LocationBrief(s, location)), ("matches", matches), ("unmapped", JsonTreeNode.FromBoolean(unmapped))),
            summary,
            s.RecordUrl("data-location", location.Id),
            matches.Items.Count == 0 ? new[] { ToolScope.Next("save_data_mapping") } : new[] { ToolScope.Next("check_data_use", ("domainKey", Js.Str(PathText(matches.Items[0], "domain.key")))) });
    }

    // ---- 80. check_data_use ----

    // OP-16 for a use that is not saved yet (docs/architecture/data-taxonomy.md, In the AI build loop and MCP tools and REST). The domain is named, or found from a
    // location and a container through the mappings; when the container sits in more than one domain, the caller names
    // the domain. A never rule answers breaks-rule with refusedWhenSaved: nobody may save the use (G-25).
    private static JsonTreeNode CheckDataUse(ToolScope s)
    {
        var tree = new DataTree(s.Snapshot);
        string? appId = s.Text("applicationId");
        string? intakeId = s.Text("intakeRequestId");
        StoredRow? app = appId == null ? null : s.RequireRow("business-application", appId, "applicationId");
        StoredRow? intake = intakeId == null ? null : s.RequireRow("intake-request", intakeId, "intakeRequestId");
        if (intake != null && !CanReadIntake(s, intake))
        {
            throw s.NotFound("Intake Request", intakeId!, "intakeRequestId");
        }

        string? locationId = s.Text("locationId");
        StoredRow? location = locationId == null ? null : s.RequireRow("data-location", locationId, "locationId");
        string domainKey = s.Text("domainKey") ?? DomainFromContainer(s, tree, location!, s.RequireText("container"));
        SnapshotRow domain = tree.Domain(domainKey) ?? throw s.Invalid("domainKey", "domainKey \"" + domainKey + "\" is not a data domain in the configuration in force. Find one with find_data_domains.");
        bool reachesAi = s.Flag("reachesAi") == true;
        bool deidentified = s.Flag("isDeidentified") == true;
        EvaluationResult<JsonTreeNode?> result = s.Call.DataUseQuestion(new DataUseQuestion(domainKey, app?.Id, intake?.Id, location?.Id, reachesAi, deidentified));
        JsonTreeNode payload = result.Payload ?? throw s.Call.Fail("invalid-input", result.Reasons.FirstOrDefault()?.Code ?? "input.malformed", result.Reasons.FirstOrDefault()?.Message ?? "The data use cannot be judged.", "Data Domain", "domainKey");
        s.Call.Report(result.Reasons);
        EffectiveDataRule governing = tree.GoverningRuleForUse(domainKey, reachesAi);
        string state = Js.Text(payload, "state") ?? "needed";
        bool never = governing.Value == DataHandlingRule.Never;
        string? existing = app == null ? null : OpenUseOf(s, app.Id, domainKey, location?.Id, reachesAi)?.Id;
        if (!domain.IsActive || !tree.IsDeclarable(domainKey))
        {
            s.Call.Warn(domain.IsActive ? ReasonCodes.DataLevelNotDeclarable : ReasonCodes.DataDomainRetired, tree.PathText(domainKey) + (domain.IsActive ? " sits at a level that is not declarable" : " is retired") + ", so a use cannot point at it. Pick a domain at a lower level, or an active one.", "Data Domain", domainKey);
        }

        JsonTreeNode approvers = Arr(Js.Items(payload, "approvers").Select(a => ApproverValue(s, tree, a)));
        string explanation = Explanation(s, tree, domainKey, state, governing, approvers, reachesAi);
        JsonTreeNode data = Js.Obj(
            ("state", JsonTreeNode.FromString(state)),
            ("domain", DomainRef(tree, domainKey)),
            ("location", location == null ? JsonTreeNode.Null : LocationBrief(s, location)),
            ("classification", ClassificationValue(s, tree, domainKey)),
            ("governingRule", GoverningRuleValue(s, tree, governing)),
            ("configurationSetVersion", JsonTreeNode.FromInt64(s.Call.Snapshot.VersionNumber)),
            ("decisionNeeded", JsonTreeNode.FromBoolean(Js.Flag(payload, "decisionNeeded") == true)),
            ("refusedWhenSaved", JsonTreeNode.FromBoolean(never)),
            ("decisionTypeKey", JsonTreeNode.FromString(EngineKeys.DataUseDecisionType)),
            ("approvalsNeeded", JsonTreeNode.FromInt64(Js.Number(payload, "approvalsNeeded") ?? 0)),
            ("approvers", approvers),
            ("impliedAnswer", ImpliedAnswerValue(s, tree, PathText(payload, "impliedAnswer.questionKey"), PathText(payload, "impliedAnswer.answerKey"), domainKey)),
            ("existingDataUseId", Js.Str(existing)),
            ("explanation", JsonTreeNode.FromString(explanation)));
        return s.Answer(data, explanation, null, never || app == null ? Array.Empty<NextStep>() : new[]
        {
            ToolScope.Next("save_data_use", ("applicationId", JsonTreeNode.FromString(app.Id)), ("domainKey", JsonTreeNode.FromString(domainKey)), ("access", JsonTreeNode.FromString(s.Text("access") ?? "read")), ("reachesAi", JsonTreeNode.FromBoolean(reachesAi))),
        });
    }

    // ---- 81. get_data_gaps ----

    private static JsonTreeNode GetDataGaps(ToolScope s)
    {
        var tree = new DataTree(s.Snapshot);
        var scope = new List<(string, JsonTreeNode?)>();
        if (s.Text("domainKey") is string domain)
        {
            if (tree.Domain(domain) == null)
            {
                throw s.NotFound("Data Domain", domain, "domainKey");
            }

            scope.Add(("domainKey", JsonTreeNode.FromString(domain)));
            scope.Add(("includeSubtree", JsonTreeNode.FromBoolean(s.Flag("includeSubtree") ?? true)));
        }

        if (s.Text("applicationId") is string app)
        {
            scope.Add(("applicationId", JsonTreeNode.FromString(s.RequireRow("business-application", app, "applicationId").Id)));
        }

        if (s.Text("locationId") is string location)
        {
            scope.Add(("locationId", JsonTreeNode.FromString(s.RequireRow("data-location", location, "locationId").Id)));
        }

        if (s.Text("principalObjectId") is string person)
        {
            scope.Add(("principalId", JsonTreeNode.FromString(person)));
        }

        // Every finding of the scope, kept for the later pages of the same query (wave 4b review).
        List<JsonTreeNode> all = s.PagedList(() => DataFindings(s, tree, scope.Count == 0 ? null : Js.Obj(scope.ToArray())));
        IReadOnlyList<string> checks = s.Strings("checks");
        IReadOnlyList<string> severities = s.Strings("severities");
        var counts = DataChecksResult.Checks.Select(c => new KeyValuePair<string, JsonTreeNode>(c, JsonTreeNode.FromInt64(all.Count(f => Js.Text(f, "check") == c)))).ToList();
        var filtered = all.Where(f => (checks.Count == 0 || checks.Contains(Js.Text(f, "check") ?? string.Empty, StringComparer.Ordinal))
                && (severities.Count == 0 || severities.Contains(Js.Text(f, "severity") ?? string.Empty, StringComparer.Ordinal)))
            .OrderBy(f => SeverityRank(Js.Text(f, "severity")))
            .ThenBy(f => Js.Text(f, "check"), StringComparer.Ordinal)
            .ThenBy(f => Js.Text(f, "subjectId"), StringComparer.Ordinal)
            .ToList();
        List<JsonTreeNode> page = s.Page(filtered, out string? next);

        // Under domains-required the application's own data owner no longer answers anything (D-41;
        // docs/architecture/data-taxonomy.md), so the current app-level data owner assignments are listed for clean-up:
        // each ends through propose_owner_change. Empty in the other modes, and outside an application or whole-estate scope.
        List<JsonTreeNode> cleanUp = tree.Mode == DataTaxonomyMode.DomainsRequired && s.Text("domainKey") == null && s.Text("locationId") == null
            ? AppLevelDataOwners(s, s.Text("applicationId"), s.Text("principalObjectId"))
            : new List<JsonTreeNode>();
        string summary = tree.Mode == DataTaxonomyMode.Off
            ? "The data checks are off: the data-taxonomy-mode setting is off."
            : (filtered.Count == 0 ? "No data gap in this scope." : Count(filtered.Count, "data finding", "data findings") + ", " + filtered.Count(f => Js.Text(f, "severity") == "high") + " of them high.")
                + (cleanUp.Count == 0 ? string.Empty : " " + Count(cleanUp.Count, "application-level data owner is", "application-level data owners are") + " left from before domains were required; end them through an owner change.");
        return s.Answer(Js.Obj(("counts", Js.Obj(counts)), ("items", Arr(page)), ("totalCount", JsonTreeNode.FromInt64(filtered.Count)), ("skipToken", Js.OptStr(next)), ("appLevelDataOwners", Arr(cleanUp))), summary);
    }

    // The current data owner assignments held on applications (O06 with Business Application), for clean-up.
    private static List<JsonTreeNode> AppLevelDataOwners(ToolScope s, string? applicationId, string? principalId)
    {
        IEnumerable<StoredRow> rows = applicationId != null ? s.Query("owner-assignment", ("businessApplication", applicationId)) : s.Query("owner-assignment", ("ownerRole", EngineKeys.DataOwnerRole));
        string today = s.Today;
        return rows.Where(o => o.Text("ownerRole") == EngineKeys.DataOwnerRole && o.Text("businessApplication") != null && o.Text("subjectDataDomain") == null && o.Text("subjectDataLocation") == null)
            .Where(o => (o.Text("endOn") == null || string.CompareOrdinal(o.Text("endOn")!.Substring(0, Math.Min(10, o.Text("endOn")!.Length)), today) > 0)
                && (o.Text("startOn") == null || string.CompareOrdinal(o.Text("startOn")!.Substring(0, Math.Min(10, o.Text("startOn")!.Length)), today) <= 0))
            .Where(o => principalId == null || Same(o.Text("principal"), principalId))
            .OrderBy(o => AppNameOf(s, o.Text("businessApplication")), StringComparer.OrdinalIgnoreCase).ThenBy(o => o.Flag("isDeputy")).ThenBy(o => o.Id, StringComparer.Ordinal)
            .Select(o => Js.Obj(
                ("ownerAssignmentId", JsonTreeNode.FromString(o.Id)),
                ("businessApplicationId", JsonTreeNode.FromString(o.Text("businessApplication")!)),
                ("applicationName", JsonTreeNode.FromString(AppNameOf(s, o.Text("businessApplication")))),
                ("principal", s.PrincipalRef(o.Text("principal"))),
                ("isDeputy", JsonTreeNode.FromBoolean(o.Flag("isDeputy"))),
                ("fixTool", JsonTreeNode.FromString("propose_owner_change")),
                ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("business-application", o.Text("businessApplication")!)))))
            .ToList();
    }

    // ---- Shared data helpers ----

    private static int SeverityRank(string? severity)
    {
        return severity == "high" ? 0 : severity == "medium" ? 1 : 2;
    }

    // A key that sorts the tree depth first: each level by Sort Order, then by label.
    private static string TreeOrder(DataTree tree, string key)
    {
        return string.Join("\u0001", tree.Chain(key).Reverse().Select(n => (Js.Number(n.Columns, "sortOrder") ?? 0).ToString("D6", CultureInfo.InvariantCulture) + DataTree.Label(n).ToLowerInvariant()));
    }

    private static string LocationSortKey(ToolScope s, StoredRow location)
    {
        StoredRow? parent = location.Text("parentLocation") is string p ? s.Rows.Get("data-location", p) : null;
        return (parent?.Text("labelAsSeen") ?? location.Text("labelAsSeen") ?? string.Empty) + "\u0001" + (parent == null ? string.Empty : location.Text("labelAsSeen") ?? string.Empty);
    }

    // A dotted path into a JSON object, as text; null when any step is missing.
    private static string? PathText(JsonTreeNode? node, string path)
    {
        JsonTreeNode? current = node;
        foreach (string part in path.Split('.'))
        {
            current = Js.Member(current, part);
            if (current == null || current.Kind == JsonTreeKind.Null)
            {
                return null;
            }
        }

        return current != null && current.Kind == JsonTreeKind.String ? current.GetString() : null;
    }

    // A data row stands open on the scope's day when it is not archived and has no End On (or Retired On) on or before
    // that day. A future End On keeps it open until then, as G-25 and OP-17 judge it (security review, blocker).
    private static bool IsOpenDataRow(StoredRow row, ToolScope s)
    {
        string day = s.Today;
        return row.Text("statusMeaning") != "archived" && DataTree.IsOpenOn(row.Text("endedOn"), day) && DataTree.IsOpenOn(row.Text("retiredOn"), day);
    }

    private static Func<StoredRow, bool> OpenOn(ToolScope s)
    {
        return row => IsOpenDataRow(row, s);
    }

    private static IReadOnlyList<string> Containers(StoredRow mapping)
    {
        JsonTreeNode? containers = mapping.Column("containers");
        if (containers != null && containers.Kind == JsonTreeKind.String && CanonicalJson.TryParse(containers.GetString(), out JsonTreeNode? parsed, out _))
        {
            containers = parsed;
        }

        return containers == null || containers.Kind != JsonTreeKind.Array
            ? Array.Empty<string>()
            : containers.Items.Where(c => c.Kind == JsonTreeKind.String).Select(c => c.GetString()).ToArray();
    }

    private static JsonTreeNode HttpsOrNull(string? url)
    {
        return url != null && url.StartsWith("https://", StringComparison.Ordinal) ? JsonTreeNode.FromString(url) : JsonTreeNode.Null;
    }

    private static JsonTreeNode DomainRef(DataTree tree, string key)
    {
        SnapshotRow? row = tree.Domain(key);
        return Js.Obj(
            ("key", JsonTreeNode.FromString(key)),
            ("label", JsonTreeNode.FromString(row == null ? key : DataTree.Label(row))),
            ("path", JsonTreeNode.FromString(row == null ? key : tree.PathText(key))));
    }

    private static JsonTreeNode DomainKeyLabel(DataTree tree, string? key)
    {
        if (key == null)
        {
            return JsonTreeNode.Null;
        }

        SnapshotRow? row = tree.Domain(key);
        return Js.Obj(("key", JsonTreeNode.FromString(key)), ("label", JsonTreeNode.FromString(row == null ? key : DataTree.Label(row))));
    }

    private static JsonTreeNode ClassificationValue(ToolScope s, DataTree tree, string domainKey)
    {
        EffectiveClassification? c = tree.Domain(domainKey) == null ? null : tree.Classification(domainKey);
        return c == null ? JsonTreeNode.Null : Js.Obj(
            ("key", JsonTreeNode.FromString(c.Key)),
            ("label", JsonTreeNode.FromString(s.Label("data-classification", c.Key))),
            ("rank", JsonTreeNode.FromInt64(Math.Max(1, c.Rank))),
            ("setOn", DomainKeyLabel(tree, c.SetOnDomainKey)));
    }

    private static JsonTreeNode RuleValue(ToolScope s, DataTree tree, EffectiveDataRule rule)
    {
        return Js.Obj(
            ("value", JsonTreeNode.FromString(EngineContract.DataHandlingRules.ToWire(rule.Value))),
            ("setOn", DomainKeyLabel(tree, rule.SetOnDomainKey)),
            ("fromClassification", rule.SetOnDomainKey == null && rule.ClassificationKey != null ? s.LabelledKey("data-classification", rule.ClassificationKey) : JsonTreeNode.Null));
    }

    private static JsonTreeNode GoverningRuleValue(ToolScope s, DataTree tree, EffectiveDataRule rule)
    {
        return Js.With(RuleValue(s, tree, rule), ("name", JsonTreeNode.FromString(rule.NameWire)));
    }

    private static JsonTreeNode DomainRules(ToolScope s, DataTree tree, string key)
    {
        return Js.Obj(
            ("use", RuleValue(s, tree, tree.Rule(key, DataRuleName.Use))),
            ("ai", RuleValue(s, tree, tree.Rule(key, DataRuleName.Ai))),
            ("copy", RuleValue(s, tree, tree.Rule(key, DataRuleName.Copy))));
    }

    private static JsonTreeNode ImpliedAnswerValue(ToolScope s, DataTree tree, string? question, string? answer, string? domainKey)
    {
        if (question == null || answer == null)
        {
            return JsonTreeNode.Null;
        }

        return Js.Obj(
            ("questionKey", JsonTreeNode.FromString(question)),
            ("answerKey", JsonTreeNode.FromString(answer)),
            ("label", JsonTreeNode.FromString(s.Label("tier-answer-option", question + "/" + answer))),
            ("domain", domainKey == null ? JsonTreeNode.Null : DomainRef(tree, domainKey)));
    }

    // One person of an OP-15 resolution, without the leave date (HR data, read only in list_owner_assignments).
    private static JsonTreeNode PersonAt(ToolScope s, JsonTreeNode person)
    {
        string? state = Js.Text(person, "directoryState");
        return Js.Obj(
            ("ownerAssignmentId", JsonTreeNode.FromString(Js.Text(person, "assignmentId") ?? string.Empty)),
            ("principal", s.PrincipalRef(Js.Text(person, "principalId"))),
            ("directoryState", state == null ? JsonTreeNode.Null : JsonTreeNode.FromString(state)),
            ("isLeaving", JsonTreeNode.FromBoolean(Js.Text(person, "leavesOn") != null)));
    }

    // The holder of an owner role in an OP-15 result, as { principal, ownerAssignmentId, inheritedFrom }, or null.
    private static JsonTreeNode OwnerValue(ToolScope s, DataTree tree, JsonTreeNode? resolved, string ownerRoleKey)
    {
        JsonTreeNode? role = Js.Items(resolved, "owners").FirstOrDefault(o => Js.Text(o, "ownerRoleKey") == ownerRoleKey);
        JsonTreeNode? holder = Js.Member(role, "holder");
        if (holder == null || holder.Kind != JsonTreeKind.Object)
        {
            return JsonTreeNode.Null;
        }

        return Js.Obj(
            ("principal", s.PrincipalRef(Js.Text(holder, "principalId"))),
            ("ownerAssignmentId", JsonTreeNode.FromString(Js.Text(holder, "assignmentId") ?? string.Empty)),
            ("inheritedFrom", DomainKeyLabel(tree, Js.Text(role, "inheritedFrom"))));
    }

    // One find_data_domains item from OP-15's answer.
    private static JsonTreeNode DomainItem(ToolScope s, DataTree tree, string key, JsonTreeNode? resolved, DataCountsIndex counts)
    {
        SnapshotRow row = tree.Domain(key)!;
        JsonTreeNode? dataOwner = Js.Items(resolved, "owners").FirstOrDefault(o => Js.Text(o, "ownerRoleKey") == EngineKeys.DataOwnerRole);
        string? deputy = Js.Items(dataOwner, "deputies").Select(d => Js.Text(d, "principalId")).FirstOrDefault(p => p != null);
        return Js.Obj(
            ("key", JsonTreeNode.FromString(key)),
            ("label", JsonTreeNode.FromString(DataTree.Label(row))),
            ("path", JsonTreeNode.FromString(tree.PathText(key))),
            ("parentKey", Js.Str(tree.ParentKey(key))),
            ("level", s.LabelledKey("data-domain-level", Js.Text(row.Columns, "levelKey"))),
            ("isActive", JsonTreeNode.FromBoolean(row.IsActive)),
            ("declarable", JsonTreeNode.FromBoolean(tree.IsDeclarable(key))),
            ("classification", ClassificationValue(s, tree, key)),
            ("owner", OwnerValue(s, tree, resolved, EngineKeys.DataOwnerRole)),
            ("deputy", s.PrincipalRef(deputy)),
            ("steward", OwnerValue(s, tree, resolved, EngineKeys.DataStewardRole)),
            ("rules", DomainRules(s, tree, key)),
            ("counts", counts.For(key)),
            ("gaps", Js.Strs(Js.Strings(resolved, "gaps"))),
            ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("data-domain", key))));
    }

    // The open mappings, open uses and proposed decisions per domain, counted once per call.
    private sealed class DataCountsIndex
    {
        public Dictionary<string, int> Mappings { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, int> Uses { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, int> Decisions { get; } = new(StringComparer.Ordinal);

        public JsonTreeNode For(string key)
        {
            return Js.Obj(
                ("mappings", JsonTreeNode.FromInt64(Mappings.TryGetValue(key, out int m) ? m : 0)),
                ("uses", JsonTreeNode.FromInt64(Uses.TryGetValue(key, out int u) ? u : 0)),
                ("openDecisions", JsonTreeNode.FromInt64(Decisions.TryGetValue(key, out int d) ? d : 0)));
        }
    }

    private static DataCountsIndex DataCounts(ToolScope s)
    {
        var index = new DataCountsIndex();
        var domainOfUse = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var domainOfMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (StoredRow mapping in s.Rows.Query(RowQuery.For("data-mapping")))
        {
            if (mapping.Text("dataDomain") is string key)
            {
                domainOfMapping[mapping.Id] = key;
                if (IsOpenDataRow(mapping, s))
                {
                    index.Mappings[key] = (index.Mappings.TryGetValue(key, out int n) ? n : 0) + 1;
                }
            }
        }

        foreach (StoredRow use in s.Rows.Query(RowQuery.For("data-use")))
        {
            if (use.Text("dataDomain") is string key)
            {
                domainOfUse[use.Id] = key;
                if (IsOpenDataRow(use, s))
                {
                    index.Uses[key] = (index.Uses.TryGetValue(key, out int n) ? n : 0) + 1;
                }
            }
        }

        foreach (StoredRow decision in s.Query("decision", ("statusMeaning", "proposed")))
        {
            string? key = decision.Text("subjectDataDomain")
                ?? (decision.Text("subjectDataUse") is string u && domainOfUse.TryGetValue(u, out string? fromUse) ? fromUse : null)
                ?? (decision.Text("subjectDataMapping") is string m && domainOfMapping.TryGetValue(m, out string? fromMapping) ? fromMapping : null);
            if (key != null)
            {
                index.Decisions[key] = (index.Decisions.TryGetValue(key, out int n) ? n : 0) + 1;
            }
        }

        return index;
    }

    // The effective target stage of a location: its own, or its parent's (docs/architecture/data-taxonomy.md, Stages and data origin).
    private static string? LocationStageOf(ToolScope s, StoredRow location)
    {
        string? stage = location.Text("targetStage");
        return stage ?? (location.Text("parentLocation") is string parent ? s.Rows.Get("data-location", parent)?.Text("targetStage") : null);
    }

    private static JsonTreeNode LocationBrief(ToolScope s, StoredRow location)
    {
        return Js.Obj(
            ("dataLocationId", JsonTreeNode.FromString(location.Id)),
            ("label", JsonTreeNode.FromString(location.Text("labelAsSeen") ?? location.Id)),
            ("platform", s.LabelledKey("data-platform", location.Text("dataPlatform") ?? "other")),
            ("stage", s.LabelledKey("target-stage", LocationStageOf(s, location))),
            ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("data-location", location.Id))));
    }

    // Current owner rows of a data subject (O06 with Subject Data Domain or Subject Data Location), started and not ended.
    private static List<StoredRow> CurrentSubjectOwners(ToolScope s, string column, string value)
    {
        DateTime day = s.Now.Date;
        return s.Query("owner-assignment", (column, value))
            .Where(r => (Js.ParseMoment(r.Text("startOn")) ?? DateTime.MinValue).Date <= day)
            .Where(r => r.Text("endOn") == null || (Js.ParseMoment(r.Text("endOn")) ?? DateTime.MaxValue).Date > day)
            .OrderBy(r => r.Text("startOn") ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(r => r.Id, StringComparer.Ordinal)
            .ToList();
    }

    // The technical owner of a location: its own holder, else its parent's (a store takes its instance's owner).
    private static (StoredRow? Row, bool FromParent) TechnicalOwnerOf(ToolScope s, StoredRow location)
    {
        StoredRow? own = CurrentSubjectOwners(s, "subjectDataLocation", location.Id).FirstOrDefault(r => r.Text("ownerRole") == EngineKeys.TechnicalOwnerRole && !r.Flag("isDeputy"));
        if (own != null || location.Text("parentLocation") is not string parent)
        {
            return (own, false);
        }

        return (CurrentSubjectOwners(s, "subjectDataLocation", parent).FirstOrDefault(r => r.Text("ownerRole") == EngineKeys.TechnicalOwnerRole && !r.Flag("isDeputy")), true);
    }

    // True when the person holds the technical owner role (a deputy too) on the location or on its parent.
    private static bool IsTechnicalOwnerOf(ToolScope s, StoredRow location)
    {
        foreach (string id in new[] { location.Id, location.Text("parentLocation") }.Where(i => i != null).Select(i => i!))
        {
            if (CurrentSubjectOwners(s, "subjectDataLocation", id).Any(r => r.Text("ownerRole") == EngineKeys.TechnicalOwnerRole && Same(r.Text("principal"), s.PersonId)))
            {
                return true;
            }
        }

        return false;
    }

    // True when the person is an effective data owner or data steward of the domain (OP-15): a holder, another holder or a
    // deputy of the node that answers for it.
    private static bool IsDomainOwnerOrSteward(ToolScope s, string domainKey)
    {
        if (new DataTree(s.Snapshot).Domain(domainKey) == null)
        {
            return false;
        }

        JsonTreeNode? resolved = s.Call.DataDomains(new[] { domainKey })[0].Payload;
        return Js.Items(resolved, "owners")
            .Where(o => Js.Text(o, "ownerRoleKey") == EngineKeys.DataOwnerRole || Js.Text(o, "ownerRoleKey") == EngineKeys.DataStewardRole)
            .SelectMany(o => new[] { Js.Member(o, "holder") }.Concat(Js.Items(o, "otherHolders")).Concat(Js.Items(o, "deputies")))
            .Any(p => p != null && Same(Js.Text(p, "principalId"), s.PersonId));
    }

    private static JsonTreeNode LocationItem(ToolScope s, DataTree tree, StoredRow location, bool ids)
    {
        string? stage = LocationStageOf(s, location);
        bool production = tree.IsProductionStage(stage);
        StoredRow? parent = location.Text("parentLocation") is string p ? s.Rows.Get("data-location", p) : null;
        var children = s.Query("data-location", ("parentLocation", location.Id)).Where(OpenOn(s)).ToList();
        var mappings = s.Query("data-mapping", ("dataLocation", location.Id)).Where(OpenOn(s)).OrderBy(m => tree.PathText(m.Text("dataDomain") ?? string.Empty), StringComparer.OrdinalIgnoreCase).ToList();
        bool childMapped = children.Any(c => s.Query("data-mapping", ("dataLocation", c.Id)).Any(OpenOn(s)));
        (StoredRow? owner, bool fromParent) = TechnicalOwnerOf(s, location);
        long staleDays = s.SettingNumber(EngineKeys.DataLocationStaleDays) ?? 180;
        // A child takes its parent's freshness (a choice under D-41): the instance is what a technical owner confirms.
        StoredRow fresh = parent ?? location;
        DateTime? seen = Js.ParseMoment(fresh.Text("lastSeenOn")) ?? Js.ParseMoment(location.Text("lastSeenOn"));
        bool stale = seen == null || seen.Value < s.Now.AddDays(-staleDays);
        string? hosted = location.Text("hostedByApplication");
        StoredRow? hostedApp = hosted == null ? null : s.Rows.Get("business-application", hosted);
        string? url = location.Text("url");
        return Js.Obj(
            ("dataLocationId", JsonTreeNode.FromString(location.Id)),
            ("label", JsonTreeNode.FromString(location.Text("labelAsSeen") ?? location.Id)),
            ("platform", s.LabelledKey("data-platform", location.Text("dataPlatform") ?? "other")),
            ("parent", parent == null ? JsonTreeNode.Null : Js.Obj(("dataLocationId", JsonTreeNode.FromString(parent.Id)), ("label", JsonTreeNode.FromString(parent.Text("labelAsSeen") ?? parent.Id)))),
            ("stage", s.LabelledKey("target-stage", stage)),
            ("isProduction", JsonTreeNode.FromBoolean(production)),
            ("deploymentTargetId", Js.Str(location.Text("deploymentTarget"))),
            ("hostedByApplication", hostedApp == null ? JsonTreeNode.Null : Js.Obj(("businessApplicationId", JsonTreeNode.FromString(hostedApp.Id)), ("name", JsonTreeNode.FromString(AppName(hostedApp))))),
            ("linkKind", s.LabelledKey("link-kind", location.Text("linkKind") ?? "external-data-system")),
            ("externalId", ids ? Js.Str(location.Text("externalId")) : JsonTreeNode.Null),
            ("url", ids ? HttpsOrNull(url) : JsonTreeNode.Null),
            ("lastSeenOn", ToolScope.MomentOrNull(location.Text("lastSeenOn"))),
            ("status", s.StatusValue(location) is JsonTreeNode st && st.Kind != JsonTreeKind.Null ? st : ToolScope.StatusValue("draft", null)),
            ("retiredOn", ToolScope.DateOrNull(location.Text("retiredOn"))),
            ("technicalOwner", owner == null ? JsonTreeNode.Null : Js.Obj(
                ("principal", s.PrincipalRef(owner.Text("principal"))),
                ("ownerAssignmentId", JsonTreeNode.FromString(owner.Id)),
                ("fromParent", JsonTreeNode.FromBoolean(fromParent)))),
            ("domains", Arr(mappings.Where(m => m.Text("dataDomain") != null).Select(m => Js.Obj(
                ("domain", DomainRef(tree, m.Text("dataDomain")!)),
                ("dataMappingId", JsonTreeNode.FromString(m.Id)),
                ("origin", JsonTreeNode.FromString(m.Text("dataOrigin") ?? "system-of-record")),
                ("containers", Js.Strs(Containers(m))))))),
            ("childCount", JsonTreeNode.FromInt64(children.Count)),
            ("flags", Js.Obj(
                ("draft", JsonTreeNode.FromBoolean(location.Text("statusMeaning") == "draft")),
                ("stale", JsonTreeNode.FromBoolean(stale)),
                ("unmapped", JsonTreeNode.FromBoolean(mappings.Count == 0 && !childMapped)),
                ("noTechnicalOwner", JsonTreeNode.FromBoolean(owner == null)),
                ("copyOutsideProduction", JsonTreeNode.FromBoolean(!production && mappings.Any(m => m.Text("dataOrigin") is "production-copy" or "masked-copy"))))),
            ("isAiCreated", AiCreated(location)),
            ("performedBy", Stamp(s, location)),
            ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("data-location", location.Id))));
    }

    // A location by its row ID, or by its external ID or URL among the locations not retired, narrowed by platform. An
    // ID or URL that more than one platform holds needs the platform.
    private static StoredRow? FindLocation(ToolScope s, string? id, string? externalId, string? url, string? platform)
    {
        if (id != null)
        {
            return s.RequireRow("data-location", id, "locationId");
        }

        if (externalId == null && url == null)
        {
            return null;
        }

        string column = externalId != null ? "externalId" : "url";
        string value = (externalId ?? url)!.Trim();
        var found = s.Query("data-location", (column, value))
            .Concat(s.Rows.Query(RowQuery.For("data-location")).Where(l => string.Equals(l.Text(column), value, StringComparison.OrdinalIgnoreCase)))
            .GroupBy(l => l.Id, StringComparer.Ordinal).Select(g => g.First())
            .Where(l => l.Text("retiredOn") == null && l.Text("statusMeaning") != "archived")
            .Where(l => platform == null || l.Text("dataPlatform") == platform)
            .ToList();
        if (found.Select(l => l.Text("dataPlatform")).Distinct(StringComparer.Ordinal).Count() > 1)
        {
            throw s.Invalid("platformKey", "More than one platform holds a location with this " + (externalId != null ? "external ID" : "URL") + ". Name the platform with platformKey.");
        }

        return found.OrderBy(l => l.Text("parentLocation") == null ? 0 : 1).ThenBy(l => l.Id, StringComparer.Ordinal).FirstOrDefault();
    }

    // The domain a container at a location sits in, through the open mappings (one domain only).
    private static string DomainFromContainer(ToolScope s, DataTree tree, StoredRow location, string container)
    {
        var mappings = s.Query("data-mapping", ("dataLocation", location.Id)).Where(OpenOn(s)).ToList();
        var named = mappings.Where(m => Containers(m).Any(c => string.Equals(c, container, StringComparison.OrdinalIgnoreCase))).ToList();
        var candidates = (named.Count > 0 ? named : mappings.Where(m => Containers(m).Count == 0).ToList())
            .Select(m => m.Text("dataDomain")).Where(k => k != null).Select(k => k!).Distinct(StringComparer.Ordinal).ToList();
        if (candidates.Count == 0)
        {
            throw s.Invalid("container", container + " in " + (location.Text("labelAsSeen") ?? location.Id) + " is not mapped to a data domain. Name the domain with domainKey, or map it first with save_data_mapping.");
        }

        if (candidates.Count > 1)
        {
            throw s.Invalid("domainKey", container + " in " + (location.Text("labelAsSeen") ?? location.Id) + " holds data of more than one domain: " + string.Join(", ", candidates.Select(k => k + " (" + tree.PathText(k) + ")")) + ". Name the one this use reaches with domainKey.");
        }

        return candidates[0];
    }

    // An open use of the application for the same domain, location and AI flag (docs/architecture/data-taxonomy.md, Keys: the second key of O27).
    private static StoredRow? OpenUseOf(ToolScope s, string applicationId, string domainKey, string? locationId, bool reachesAi)
    {
        return s.Query("data-use", ("businessApplication", applicationId))
            .Where(OpenOn(s))
            .FirstOrDefault(u => u.Text("dataDomain") == domainKey && u.Flag("reachesAi") == reachesAi && (locationId == null ? u.Text("dataLocation") == null : Same(u.Text("dataLocation"), locationId)));
    }

    private static JsonTreeNode ApproverValue(ToolScope s, DataTree tree, JsonTreeNode approver)
    {
        string? role = Js.Text(approver, "roleKey");
        string? ownerRole = Js.Text(approver, "ownerRoleKey");
        return Js.Obj(
            ("kind", JsonTreeNode.FromString(ownerRole != null ? "owner-role" : "role")),
            ("role", s.LabelledKey("role", role)),
            ("ownerRole", s.LabelledKey("owner-role", ownerRole)),
            ("ownerSubject", Js.Str(Js.Text(approver, "ownerSubject"))),
            ("principal", s.PrincipalRef(Js.Text(approver, "principalId"))),
            ("inheritedFrom", DomainKeyLabel(tree, Js.Text(approver, "inheritedFrom"))),
            ("isConsultation", JsonTreeNode.FromBoolean(Js.Flag(approver, "isConsultation") == true)),
            ("noOneToAsk", JsonTreeNode.FromBoolean(Js.Flag(approver, "noOneToAsk") == true)));
    }

    // The answer of check_data_use in plain words: the state, the rule and who is asked.
    private static string Explanation(ToolScope s, DataTree tree, string domainKey, string state, EffectiveDataRule governing, JsonTreeNode approvers, bool reachesAi)
    {
        string path = tree.PathText(domainKey);
        string rule = tree.DescribeRule(governing, domainKey);
        var asked = approvers.Items.Where(a => Js.Flag(a, "isConsultation") != true).Select(a =>
            PathText(a, "principal.displayLabel") is string person && person.Length > 0
                ? person + (PathText(a, "inheritedFrom.label") is string from ? " (from " + from + ")" : " (own)")
                : Js.Flag(a, "noOneToAsk") == true ? "nobody: the " + (PathText(a, "ownerRole.label") ?? "owner").ToLowerInvariant() + " is not held" : "the " + (PathText(a, "role.label") ?? PathText(a, "ownerRole.label") ?? "approver").ToLowerInvariant()).ToList();
        string askedWords = asked.Count == 0 ? string.Empty : " Asked: " + string.Join(" and ", asked) + ".";
        switch (state)
        {
            case "not-needed":
                return "No decision is needed: " + rule + ".";
            case "breaks-rule":
                return governing.Value == DataHandlingRule.Never
                    ? "Refused when saved, for everyone: " + rule + ". Change the rule where it is set through a configuration change, or leave the data out."
                    : "This use breaks the rule: " + rule + ", and the data is not de-identified. Use masked or de-identified data, or leave the data out.";
            case "no-one-to-ask":
                return "A decision is needed (" + rule + "), but nobody can answer it now." + askedWords + " Propose a data owner for " + path + " first.";
            default:
                return "A data-use decision is needed: " + rule + (reachesAi ? ", and the data reaches an AI model" : string.Empty) + "." + askedWords;
        }
    }

    // OP-17 findings in scope, as dataFinding items with the tool that fixes each.
    private static List<JsonTreeNode> DataFindings(ToolScope s, DataTree tree, JsonTreeNode? scope)
    {
        EvaluationResult<JsonTreeNode?> result = s.Call.DataChecks(scope);
        if (result.Payload == null)
        {
            Reason? first = result.Reasons.FirstOrDefault();
            throw s.Call.Fail(result.Outcome == "unavailable" ? "unavailable" : "invalid-input", first?.Code ?? "input.malformed", first?.Message ?? "The data checks could not run.", first?.Entity, first?.Ref);
        }

        var appNames = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        return Js.Items(result.Payload, "findings").Select(f =>
        {
            string check = Js.Text(f, "check") ?? "DC-00";
            string subjectKind = Js.Text(f, "subjectKind") ?? "data-domain";
            string subjectId = Js.Text(f, "subjectId") ?? string.Empty;
            string? domain = Js.Text(f, "domainKey");
            string? app = Js.Text(f, "applicationId");
            if (app != null && !appNames.ContainsKey(app))
            {
                appNames[app] = s.Rows.Get("business-application", app) is StoredRow row ? AppName(row) : null;
            }

            string? recordEntity = subjectKind switch
            {
                "data-domain" or "data-use" or "data-mapping" or "data-location" or "business-application" => subjectKind,
                _ => domain != null ? "data-domain" : null,
            };
            string? recordId = recordEntity == subjectKind ? subjectId : domain;
            return Js.Obj(
                ("check", JsonTreeNode.FromString(check)),
                ("code", JsonTreeNode.FromString(Js.Text(f, "code") ?? "data.finding")),
                ("severity", JsonTreeNode.FromString(Js.Text(f, "severity") ?? "low")),
                ("subjectKind", JsonTreeNode.FromString(subjectKind)),
                ("subjectId", JsonTreeNode.FromString(subjectId)),
                ("message", JsonTreeNode.FromString(Js.Text(f, "message") ?? check)),
                ("domain", domain == null ? JsonTreeNode.Null : DomainRef(tree, domain)),
                ("businessApplicationId", Js.Str(app)),
                ("applicationName", app == null ? JsonTreeNode.Null : Js.Str(appNames[app])),
                ("dataLocationId", Js.Str(Js.Text(f, "locationId"))),
                ("principal", s.PrincipalRef(Js.Text(f, "principalId"))),
                ("ownerRoleKey", Js.Str(Js.Text(f, "ownerRoleKey"))),
                ("isDeputy", Js.Member(f, "isDeputy") is JsonTreeNode deputy && (deputy.Kind == JsonTreeKind.True || deputy.Kind == JsonTreeKind.False) ? deputy : JsonTreeNode.Null),
                ("fixTool", Js.Str(FixToolOf(check))),
                ("recordUrl", recordEntity == null || recordId == null || recordId.Length == 0 ? JsonTreeNode.Null : JsonTreeNode.FromString(s.RecordUrl(recordEntity, recordId))),
                ("suggestion", check == "DC-10" && app != null ? UndeclaredDataSuggestion(s, tree, app) : JsonTreeNode.Null));
        }).ToList();
    }

    // DC-10's offer, "declare?" (docs/architecture/data-taxonomy.md): each other application this one reads data from or
    // writes data to, with the domains to declare, taken from what that application holds: the domains of its own open
    // data uses and the domains mapped at the locations it is. Null when it has no data dependency (it only uses AI).
    private static JsonTreeNode UndeclaredDataSuggestion(ToolScope s, DataTree tree, string appId)
    {
        List<StoredRow> dependencies = s.Query("dependency", ("fromApplication", appId))
            .Where(d => d.Text("endedOn") == null && d.Text("toApplication") != null && EngineKeys.DataDependencyTypes.Contains(d.Text("dependencyType") ?? string.Empty, StringComparer.Ordinal))
            .OrderBy(d => d.Id, StringComparer.Ordinal).ToList();
        if (dependencies.Count == 0)
        {
            return JsonTreeNode.Null;
        }

        var others = dependencies.GroupBy(d => d.Text("toApplication")!, StringComparer.OrdinalIgnoreCase).Select(g =>
        {
            StoredRow? other = s.Rows.Get("business-application", g.Key);
            var domains = new SortedSet<string>(StringComparer.Ordinal);
            foreach (StoredRow use in s.Query("data-use", ("businessApplication", g.Key)).Where(OpenOn(s)))
            {
                if (use.Text("dataDomain") is string key && tree.Domain(key) != null)
                {
                    domains.Add(key);
                }
            }

            foreach (StoredRow location in s.Query("data-location", ("hostedByApplication", g.Key)).Where(l => l.Text("retiredOn") == null))
            {
                foreach (StoredRow mapping in s.Query("data-mapping", ("dataLocation", location.Id)).Where(OpenOn(s)))
                {
                    if (mapping.Text("dataDomain") is string key && tree.Domain(key) != null)
                    {
                        domains.Add(key);
                    }
                }
            }

            bool writes = g.Any(d => d.Text("dependencyType") == "writes-data-to");
            return Js.Obj(
                ("businessApplicationId", JsonTreeNode.FromString(g.Key)),
                ("name", JsonTreeNode.FromString(other == null ? g.Key : AppName(other))),
                ("access", JsonTreeNode.FromString(writes ? "write" : "read")),
                ("domains", Arr(domains.Where(tree.IsDeclarable).Select(k => DomainRef(tree, k)))),
                ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("business-application", g.Key))));
        }).ToList();
        string names = string.Join(" and ", others.Select(o => Js.Text(o, "name")));
        return Js.Obj(
            ("text", JsonTreeNode.FromString((others.Count == 1 && Js.Text(others[0], "access") == "write" ? "Writes data to " : "Reads data from ") + names + ": declare?")),
            ("applications", Arr(others)));
    }

    // The tool that fixes a finding (docs/architecture/data-taxonomy.md, Screens: each finding with its fix tool).
    private static string? FixToolOf(string check)
    {
        return check switch
        {
            "DC-01" or "DC-02" or "DC-03" or "DC-04" or "DC-06" or "DC-12" => "propose_owner_change",
            "DC-05" or "DC-13" => "save_decision_proposal",
            "DC-07" or "DC-10" => "save_data_use",
            "DC-08" or "DC-11" => "save_data_mapping",
            "DC-09" => "answer_tier_questions",
            "DC-14" => "move_data_references",
            _ => null,
        };
    }

    // One data use as lists show it, with its state from OP-16.
    private static JsonTreeNode UseItem(ToolScope s, DataTree tree, StoredRow use, JsonTreeNode? evaluation = null)
    {
        evaluation ??= s.Call.DataUse(use.Id)?.Payload;
        string? appId = use.Text("businessApplication");
        StoredRow? app = appId == null ? null : s.Rows.Get("business-application", appId);
        StoredRow? location = use.Text("dataLocation") is string l ? s.Rows.Get("data-location", l) : null;
        string domain = use.Text("dataDomain") ?? "unknown";
        return Js.Obj(
            ("dataUseId", JsonTreeNode.FromString(use.Id)),
            ("recordNumber", Js.Str(use.Text("recordNumber"))),
            ("businessApplicationId", JsonTreeNode.FromString(appId ?? use.Id)),
            ("applicationName", JsonTreeNode.FromString(app == null ? appId ?? string.Empty : AppName(app))),
            ("domain", DomainRef(tree, domain)),
            ("location", location == null ? JsonTreeNode.Null : LocationBrief(s, location)),
            ("access", JsonTreeNode.FromString(use.Text("access") ?? "read")),
            ("purpose", JsonTreeNode.FromString(use.Text("purpose") ?? string.Empty)),
            ("reachesAi", JsonTreeNode.FromBoolean(use.Flag("reachesAi"))),
            ("aiServiceAsSeen", Js.Str(use.Text("aiServiceAsSeen"))),
            ("isDeidentified", JsonTreeNode.FromBoolean(use.Flag("isDeidentified"))),
            ("accessApproach", ListValue(s, "access-approach", use.Text("accessApproach"))),
            ("status", s.StatusValue(use) is JsonTreeNode st && st.Kind != JsonTreeKind.Null ? st : ToolScope.StatusValue("draft", null)),
            ("state", JsonTreeNode.FromString(Js.Text(evaluation, "state") ?? "needed")),
            ("approvalsNeeded", JsonTreeNode.FromInt64(Js.Number(evaluation, "approvalsNeeded") ?? 0)),
            ("approvalsGiven", Js.Number(evaluation, "approvalsGiven") is long given ? JsonTreeNode.FromInt64(given) : JsonTreeNode.Null),
            ("decisionId", Js.Str(Js.Text(evaluation, "decisionId"))),
            ("startOn", ToolScope.DateOrNull(use.Text("startOn"))),
            ("endedOn", ToolScope.DateOrNull(use.Text("endedOn"))),
            ("isAiCreated", AiCreated(use)),
            ("performedBy", Stamp(s, use)),
            ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("data-use", use.Id))));
    }

    // One data mapping as lists show it, with its copy state from OP-16 when it is a copy outside production.
    private static JsonTreeNode MappingItem(ToolScope s, DataTree tree, StoredRow mapping, JsonTreeNode? evaluation = null)
    {
        evaluation ??= s.Call.DataUse(mapping.Id)?.Payload;
        StoredRow? location = mapping.Text("dataLocation") is string l ? s.Rows.Get("data-location", l) : null;
        bool copy = Js.Member(evaluation, "governingRule") is JsonTreeNode rule && rule.Kind == JsonTreeKind.Object;
        return Js.Obj(
            ("dataMappingId", JsonTreeNode.FromString(mapping.Id)),
            ("domain", DomainRef(tree, mapping.Text("dataDomain") ?? "unknown")),
            ("location", location == null
                ? Js.Obj(("dataLocationId", JsonTreeNode.FromString(mapping.Text("dataLocation") ?? mapping.Id)), ("label", JsonTreeNode.FromString(mapping.Text("dataLocation") ?? string.Empty)), ("platform", s.LabelledKey("data-platform", "other")), ("stage", JsonTreeNode.Null), ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("data-location", mapping.Text("dataLocation")))))
                : LocationBrief(s, location)),
            ("containers", Js.Strs(Containers(mapping))),
            ("origin", JsonTreeNode.FromString(mapping.Text("dataOrigin") ?? "system-of-record")),
            ("copiedFromMappingId", Js.Str(mapping.Text("copiedFrom"))),
            ("status", s.StatusValue(mapping) is JsonTreeNode st && st.Kind != JsonTreeKind.Null ? st : ToolScope.StatusValue("draft", null)),
            ("copyState", copy ? Js.Str(Js.Text(evaluation, "state")) : JsonTreeNode.Null),
            ("decisionId", Js.Str(Js.Text(evaluation, "decisionId"))),
            ("approvalsNeeded", JsonTreeNode.FromInt64(Js.Number(evaluation, "approvalsNeeded") ?? 0)),
            ("confirmedOn", ToolScope.DateOrNull(mapping.Text("confirmedOn"))),
            ("startOn", ToolScope.DateOrNull(mapping.Text("startOn"))),
            ("endedOn", ToolScope.DateOrNull(mapping.Text("endedOn"))),
            ("isAiCreated", AiCreated(mapping)),
            ("performedBy", Stamp(s, mapping)),
            ("recordUrl", JsonTreeNode.FromString(s.RecordUrl("data-mapping", mapping.Id))));
    }
}
