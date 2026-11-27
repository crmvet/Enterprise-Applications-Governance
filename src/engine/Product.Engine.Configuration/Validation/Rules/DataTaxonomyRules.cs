using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Product.Engine.Configuration.Json;
using Product.Engine.Configuration.Model;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// The data domain tree of a configuration set, read from its content (C47 to C49; docs/architecture/data-taxonomy.md, Entities and Effective values on a domain).
// The engine core's DataTree reads the same rules from a compiled snapshot; this one serves OP-09 and the draft diff,
// which see content, not snapshots.
public sealed class DataContentTree
{
    private readonly ConfigurationContent _content;
    private readonly Dictionary<string, ContentRow> _domains = new(StringComparer.Ordinal);

    public DataContentTree(ConfigurationContent content)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
        foreach (ContentRow row in content.RowsFor("data-domain").Where(r => r.Key != null))
        {
            _domains[row.KeyPath] = row;
        }
    }

    public IEnumerable<ContentRow> Domains => _domains.Values;

    public IEnumerable<ContentRow> ActiveDomains => _domains.Values.Where(r => r.IsActive);

    public ContentRow? Domain(string? key)
    {
        return key != null && _domains.TryGetValue(key, out ContentRow? row) ? row : null;
    }

    // The domain, then its parent, up to the top. Stops at a cycle or after 32 levels.
    public List<ContentRow> Chain(string key)
    {
        var chain = new List<ContentRow>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        ContentRow? current = Domain(key);
        while (current != null && chain.Count < 32 && seen.Add(current.KeyPath))
        {
            chain.Add(current);
            current = Domain(RowFacts.Text(current, "parentKey"));
        }

        return chain;
    }

    public string Path(string key)
    {
        return string.Join(" / ", Chain(key).AsEnumerable().Reverse().Select(r => RowFacts.Text(r, "label") ?? r.KeyPath));
    }

    public int? Rank(string entity, string? key)
    {
        ContentRow? row = key == null ? null : (_content.TryFindRow(entity, key, out ContentRow? found) ? found : null);
        JsonTreeNode? rank = row == null ? null : JsonNodes.Get(row.Node, "rank");
        return rank != null && rank.TryGetInt64(out long value) ? (int)value : null;
    }

    // The nearest explicit classification on the domain or above it, with its rank.
    public (string? Key, int? Rank) Classification(string key)
    {
        foreach (ContentRow node in Chain(key))
        {
            string? value = RowFacts.Text(node, "classificationKey");
            if (value != null)
            {
                return (value, Rank("data-classification", value));
            }
        }

        return (null, null);
    }

    // The strictest of the explicit values on the chain and the effective classification's default, as a strictness
    // number: allowed 0, decision 1, masked-only 2, never 3. Null when nothing sets it.
    public int? Rule(string key, string domainField, string defaultField)
    {
        int? best = null;
        (string? classKey, _) = Classification(key);
        if (classKey != null && _content.TryFindRow("data-classification", classKey, out ContentRow? classRow))
        {
            best = Strictness(RowFacts.Text(classRow, defaultField));
        }

        foreach (ContentRow node in Chain(key))
        {
            int? value = Strictness(RowFacts.Text(node, domainField));
            if (value.HasValue && (!best.HasValue || value.Value > best.Value))
            {
                best = value;
            }
        }

        return best;
    }

    public static readonly (string Domain, string Default, string Words)[] Rules =
    {
        ("useRule", "defaultUseRule", "use rule"),
        ("aiUseRule", "defaultAiUseRule", "AI use rule"),
        ("copyToTestRule", "defaultCopyToTestRule", "copy to test rule"),
    };

    public static int? Strictness(string? wire)
    {
        return EngineContract.DataHandlingRules.TryParse(wire, out DataHandlingRule rule) ? (int)rule : null;
    }
}

// What a configuration draft does to the data taxonomy (docs/architecture/data-taxonomy.md, Approval rules, Data Taxonomy Change), computed
// from the diff of the active content and the draft's content, never from the AI's or the admin's words.
// - touches: a data classification, level, domain or platform row is added, changed or retired.
// - loosens: a domain active in both versions gets a lower effective classification rank or a looser effective rule, or
//   a domain an operational row uses (rowsInUse) is retired or moved.
// The touched domains are the domains whose row changed, with every domain below them; the approvers of the
// touched-data-domains owner subject are their effective data owners.
public sealed class DataTaxonomyDiff
{
    private static readonly string[] Entities = { "data-classification", "data-domain-level", "data-domain", "data-platform" };

    private DataTaxonomyDiff(DataTaxonomyChange? change, IReadOnlyList<string> touched, IReadOnlyList<string> loosened)
    {
        Change = change;
        TouchedDomainKeys = touched;
        LoosenedDomainKeys = loosened;
    }

    public DataTaxonomyChange? Change { get; }

    public IReadOnlyList<string> TouchedDomainKeys { get; }

    public IReadOnlyList<string> LoosenedDomainKeys { get; }

    public static DataTaxonomyDiff Compute(ConfigurationContent active, ConfigurationContent candidate, IEnumerable<string>? domainsInUse = null)
    {
        if (active == null || candidate == null)
        {
            throw new ArgumentNullException(active == null ? nameof(active) : nameof(candidate));
        }

        bool touches = false;
        foreach (string entity in Entities)
        {
            var before = active.RowsFor(entity).Where(r => r.Key != null).ToDictionary(r => r.KeyPath, r => r, StringComparer.Ordinal);
            var after = candidate.RowsFor(entity).Where(r => r.Key != null).ToDictionary(r => r.KeyPath, r => r, StringComparer.Ordinal);
            if (before.Count != after.Count || after.Any(a => !before.TryGetValue(a.Key, out ContentRow? b) || !b.Node.Equals(a.Value.Node)))
            {
                touches = true;
            }
        }

        var oldTree = new DataContentTree(active);
        var newTree = new DataContentTree(candidate);
        var changed = new HashSet<string>(StringComparer.Ordinal);
        foreach (ContentRow row in newTree.Domains)
        {
            ContentRow? old = oldTree.Domain(row.KeyPath);
            if (old == null || !old.Node.Equals(row.Node))
            {
                changed.Add(row.KeyPath);
            }
        }

        foreach (ContentRow row in oldTree.Domains.Where(r => newTree.Domain(r.KeyPath) == null))
        {
            changed.Add(row.KeyPath);
        }

        // A changed classification touches every domain it classifies.
        foreach (ContentRow classRow in candidate.RowsFor("data-classification").Where(r => r.Key != null))
        {
            bool same = active.TryFindRow("data-classification", classRow.KeyPath, out ContentRow? oldClass) && oldClass.Node.Equals(classRow.Node);
            if (!same)
            {
                foreach (ContentRow domain in newTree.Domains.Where(d => newTree.Classification(d.KeyPath).Key == classRow.KeyPath))
                {
                    changed.Add(domain.KeyPath);
                }
            }
        }

        var touched = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string key in changed)
        {
            touched.Add(key);
            foreach (ContentRow domain in newTree.Domains.Where(d => newTree.Chain(d.KeyPath).Any(c => c.KeyPath == key)))
            {
                touched.Add(domain.KeyPath);
            }
        }

        var loosened = new SortedSet<string>(StringComparer.Ordinal);
        foreach (ContentRow row in newTree.ActiveDomains)
        {
            ContentRow? old = oldTree.Domain(row.KeyPath);
            if (old == null || !old.IsActive)
            {
                continue;
            }

            int? oldRank = oldTree.Classification(row.KeyPath).Rank;
            int? newRank = newTree.Classification(row.KeyPath).Rank;
            bool lower = oldRank.HasValue && (!newRank.HasValue || newRank.Value < oldRank.Value);
            bool looser = DataContentTree.Rules.Any(r =>
            {
                int? was = oldTree.Rule(row.KeyPath, r.Domain, r.Default);
                int? now = newTree.Rule(row.KeyPath, r.Domain, r.Default);
                return was.HasValue && (!now.HasValue || now.Value < was.Value);
            });
            // A moved domain changes its inherited data owner and approver, and those of its subtree, so a move counts as
            // loosens: the touched owners approve it (security review, major; D-41).
            bool moved = !string.Equals(RowFacts.Text(old, "parentKey"), RowFacts.Text(row, "parentKey"), StringComparison.Ordinal);
            if (lower || looser)
            {
                loosened.Add(row.KeyPath);
            }

            if (moved)
            {
                loosened.Add(row.KeyPath);
                foreach (ContentRow below in newTree.ActiveDomains.Where(d => newTree.Chain(d.KeyPath).Any(c => c.KeyPath == row.KeyPath)))
                {
                    loosened.Add(below.KeyPath);
                }
            }
        }

        foreach (string key in domainsInUse ?? Enumerable.Empty<string>())
        {
            ContentRow? old = oldTree.Domain(key);
            ContentRow? now = newTree.Domain(key);
            bool retired = old != null && old.IsActive && (now == null || !now.IsActive);
            bool moved = old != null && now != null && !string.Equals(RowFacts.Text(old, "parentKey"), RowFacts.Text(now, "parentKey"), StringComparison.Ordinal);
            if (retired || moved)
            {
                loosened.Add(key);
            }
        }

        DataTaxonomyChange? change = loosened.Count > 0 ? DataTaxonomyChange.Loosens : touches ? DataTaxonomyChange.Touches : null;
        return new DataTaxonomyDiff(change, touched.ToArray(), loosened.ToArray());
    }
}

internal static class DataRuleSupport
{
    public static DataTaxonomyMode Mode(ConfigurationContent content)
    {
        ContentRow? row = RowFacts.FindActive(content, "setting", EngineKeys.DataTaxonomyModeSetting);
        string? value = row == null ? null : JsonNodes.GetString(row.Node, "value");
        return EngineContract.DataTaxonomyModes.TryParse(value, out DataTaxonomyMode mode) ? mode : DataTaxonomyMode.Off;
    }

    // The owner subject kind an owner subject is resolved on.
    public static string KindOf(string ownerSubject)
    {
        switch (ownerSubject)
        {
            case "data-domain":
            case "parent-data-domain":
            case "touched-data-domains":
                return "data-domain";
            case "data-location":
                return "data-location";
            default:
                return "business-application";
        }
    }

    public static bool RoleHeldOn(ConfigurationContent content, string? ownerRoleKey, string kind)
    {
        ContentRow? role = RowFacts.FindActive(content, "owner-role", ownerRoleKey);
        if (role == null)
        {
            return false;
        }

        JsonTreeNode? kinds = JsonNodes.Get(role.Node, "subjectKinds");
        if (kinds == null || kinds.Kind != JsonTreeKind.Array || kinds.Items.Count == 0)
        {
            return kind == "business-application";
        }

        return kinds.Items.Any(i => i.Kind == JsonTreeKind.String && i.GetString() == kind);
    }

    // The most specific active group of an entity for a decision type that matches the record. Ties are V26's to report.
    public static RuleGroup? Winner(List<RuleGroup> groups, string decisionType, IDictionary<string, string> record)
    {
        return groups.Where(g => g.DecisionTypeKey == decisionType && g.Matches(record)).OrderByDescending(g => g.Specificity).FirstOrDefault();
    }
}

// V49: a domain's parent is an active domain of the same set, with no cycle, and a retired domain has no active children.
public sealed class V49DomainParent : ValidationRule, IContentRule
{
    public static readonly V49DomainParent Instance = new();

    private V49DomainParent()
        : base("V49", ReasonCodes.DataDomainParent, "Domain parents form a tree")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        var tree = new DataContentTree(context.Content);
        foreach (ContentRow row in tree.ActiveDomains)
        {
            string? parent = RowFacts.Text(row, "parentKey");
            if (parent == null)
            {
                continue;
            }

            ContentRow? parentRow = tree.Domain(parent);
            if (parentRow != null && !parentRow.IsActive)
            {
                issues.Add(Issue("data domain " + Q(row.KeyPath) + " sits under " + Q(parent) + ", which is retired. A retired domain has no active children: move the child or retire it too.", "data-domain", row.KeyPath, row.Pointer));
                continue;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal) { row.KeyPath };
            ContentRow? current = parentRow;
            while (current != null)
            {
                if (!seen.Add(current.KeyPath))
                {
                    issues.Add(Issue("data domain " + Q(row.KeyPath) + " is its own ancestor through " + Q(parent) + ", so the tree has a cycle. Give one of them another parent.", "data-domain", row.KeyPath, row.Pointer));
                    break;
                }

                current = tree.Domain(RowFacts.Text(current, "parentKey"));
            }
        }
    }
}

// V50: a child's level ranks below its parent's level; a top-level domain not at the top level is a warning.
public sealed class V50DomainLevel : ValidationRule, IContentRule
{
    public static readonly V50DomainLevel Instance = new();

    private V50DomainLevel()
        : base("V50", ReasonCodes.DataDomainLevel, "Domain levels go down the tree")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        var tree = new DataContentTree(context.Content);
        List<int> ranks = RowFacts.Active(context.Content, "data-domain-level").Select(r => tree.Rank("data-domain-level", r.KeyPath)).Where(r => r.HasValue).Select(r => r!.Value).ToList();
        int? top = ranks.Count == 0 ? null : ranks.Min();
        foreach (ContentRow row in tree.ActiveDomains)
        {
            int? rank = tree.Rank("data-domain-level", RowFacts.Text(row, "levelKey"));
            string? parent = RowFacts.Text(row, "parentKey");
            ContentRow? parentRow = tree.Domain(parent);
            if (parentRow != null)
            {
                int? parentRank = tree.Rank("data-domain-level", RowFacts.Text(parentRow, "levelKey"));
                if (rank.HasValue && parentRank.HasValue && rank.Value <= parentRank.Value)
                {
                    issues.Add(Issue("data domain " + Q(row.KeyPath) + " is at level " + Q(RowFacts.Text(row, "levelKey")) + ", which does not rank below its parent " + Q(parent) + "'s level " + Q(RowFacts.Text(parentRow, "levelKey")) + ". Pick a lower level for the child.", "data-domain", row.KeyPath, row.Pointer));
                }
            }
            else if (parent == null && rank.HasValue && top.HasValue && rank.Value != top.Value)
            {
                issues.Add(Issue("data domain " + Q(row.KeyPath) + " has no parent and is not at the top level, so it can have no parent owner to appoint it. Give it a parent, or put it at the top level.", "data-domain", row.KeyPath, row.Pointer, ReasonSeverity.Warning));
            }
        }
    }
}

// V51: when any domain exists, at least one active level; active level ranks are unique.
public sealed class V51LevelRank : ValidationRule, IContentRule
{
    public static readonly V51LevelRank Instance = new();

    private V51LevelRank()
        : base("V51", ReasonCodes.DataLevelRank, "Domain levels exist and rank uniquely")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        var tree = new DataContentTree(context.Content);
        List<ContentRow> levels = RowFacts.Active(context.Content, "data-domain-level").ToList();
        if (tree.ActiveDomains.Any() && levels.Count == 0)
        {
            ContentRow first = tree.ActiveDomains.First();
            issues.Add(Issue("the set holds data domains and no active data domain level. Add at least one level, such as Domain.", "data-domain-level", null, first.Pointer));
        }

        foreach (IGrouping<int?, ContentRow> same in levels.GroupBy(l => tree.Rank("data-domain-level", l.KeyPath)).Where(g => g.Key.HasValue && g.Count() > 1))
        {
            ContentRow[] rows = same.ToArray();
            issues.Add(Issue("active data domain levels " + string.Join(" and ", rows.Select(r => Q(r.KeyPath))) + " share rank " + same.Key + ". Give each level its own rank.", "data-domain-level", rows[1].KeyPath, rows[1].Pointer));
        }
    }
}

// V52: classification ranks are unique; the implied question and answer resolve; every top-level domain is classified.
public sealed class V52Classification : ValidationRule, IContentRule
{
    public static readonly V52Classification Instance = new();

    private V52Classification()
        : base("V52", ReasonCodes.DataClassification, "Classifications rank uniquely and resolve")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        var tree = new DataContentTree(context.Content);
        List<ContentRow> classes = RowFacts.Active(context.Content, "data-classification").ToList();
        foreach (IGrouping<int?, ContentRow> same in classes.GroupBy(c => tree.Rank("data-classification", c.KeyPath)).Where(g => g.Key.HasValue && g.Count() > 1))
        {
            ContentRow[] rows = same.ToArray();
            issues.Add(Issue("active data classifications " + string.Join(" and ", rows.Select(r => Q(r.KeyPath))) + " share rank " + same.Key + ". Give each classification its own rank, in the label priority order.", "data-classification", rows[1].KeyPath, rows[1].Pointer));
        }

        foreach (ContentRow row in classes)
        {
            string? question = RowFacts.Text(row, "impliedTierQuestionKey");
            string? answer = RowFacts.Text(row, "impliedAnswerKey");
            if (answer == null)
            {
                continue;
            }

            if (question == null || RowFacts.FindActive(context.Content, "tier-answer-option", question + "/" + answer) == null)
            {
                issues.Add(Issue("data classification " + Q(row.KeyPath) + " implies answer " + Q(answer) + (question == null ? " without naming its tier question" : ", which is not an active answer option of tier question " + Q(question)) + ". Name an active answer of the implied question.", "data-classification", row.KeyPath, row.Pointer));
            }
        }

        foreach (ContentRow row in tree.ActiveDomains.Where(d => RowFacts.Text(d, "parentKey") == null && RowFacts.Text(d, "classificationKey") == null))
        {
            issues.Add(Issue("top-level data domain " + Q(row.KeyPath) + " sets no classification, so the domains below it have none to inherit. Set its classification.", "data-domain", row.KeyPath, row.Pointer));
        }
    }
}

// V53 (warning): a child's explicit classification ranks below its parent's effective classification.
public sealed class V53ClassificationLower : ValidationRule, IContentRule
{
    public static readonly V53ClassificationLower Instance = new();

    private V53ClassificationLower()
        : base("V53", ReasonCodes.DataClassificationLower, "A child is not classified lower than its parent")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        var tree = new DataContentTree(context.Content);
        foreach (ContentRow row in tree.ActiveDomains)
        {
            string? own = RowFacts.Text(row, "classificationKey");
            string? parent = RowFacts.Text(row, "parentKey");
            if (own == null || parent == null || tree.Domain(parent) == null)
            {
                continue;
            }

            (string? parentClass, int? parentRank) = tree.Classification(parent);
            int? rank = tree.Rank("data-classification", own);
            if (rank.HasValue && parentRank.HasValue && rank.Value < parentRank.Value)
            {
                issues.Add(Issue("data domain " + Q(row.KeyPath) + " is classified " + Q(own) + ", lower than " + Q(parentClass) + " above it. The approver acknowledges this, or the child inherits instead.", "data-domain", row.KeyPath, row.Pointer));
            }
        }
    }
}

// V54: a domain that operational records use cannot be retired; move them first (move_data_references).
public sealed class V54DomainInUse : ValidationRule, IContentRule
{
    public static readonly V54DomainInUse Instance = new();

    private V54DomainInUse()
        : base("V54", ReasonCodes.DataDomainInUse, "A domain in use is not retired")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        var tree = new DataContentTree(context.Content);
        foreach (ContentRow row in tree.Domains.Where(d => !d.IsActive))
        {
            bool wasActive = context.Active == null || (context.Active.TryFindRow("data-domain", row.KeyPath, out ContentRow? old) && old.IsActive);
            if (wasActive && context.RowsInUse.Contains(new RowIdentity("data-domain", row.KeyPath)))
            {
                issues.Add(Issue("data domain " + Q(row.KeyPath) + " still has owner assignments, mappings, uses or open decisions, so it cannot be retired. Move them with move_data_references and propose_owner_change first.", "data-domain", row.KeyPath, row.Pointer));
            }
        }
    }
}

// V55: every data decision the rules can require has someone to ask: an approval rule group with a counting rule, or
// an accountable decision right, for each active classification with and without AI (data-use), for copies
// (data-mapping) and for retention (data-retention). Only while data-taxonomy-mode is not off.
public sealed class V55DecisionRouted : ValidationRule, IContentRule
{
    public static readonly V55DecisionRouted Instance = new();

    private V55DecisionRouted()
        : base("V55", ReasonCodes.DataDecisionUnrouted, "Every data decision has someone to ask")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        ConfigurationContent content = context.Content;
        if (DataRuleSupport.Mode(content) == DataTaxonomyMode.Off)
        {
            return;
        }

        List<RuleGroup> rules = RuleGroups.ApprovalRules(content);
        List<RuleGroup> rights = RuleGroups.DecisionRights(content);
        var cases = new List<(string Type, Dictionary<string, string> Record, string Words)>();
        if (RowFacts.FindActive(content, "decision-type", EngineKeys.DataUseDecisionType) != null)
        {
            foreach (ContentRow c in RowFacts.Active(content, "data-classification"))
            {
                foreach (string ai in new[] { "false", "true" })
                {
                    cases.Add((EngineKeys.DataUseDecisionType, new Dictionary<string, string>(StringComparer.Ordinal) { { "subjectKind", "data-use" }, { "classificationKey", c.KeyPath }, { "aiUse", ai } }, "a data use of " + Q(c.KeyPath) + " data " + (ai == "true" ? "that reaches AI" : "without AI")));
                }

                cases.Add((EngineKeys.DataUseDecisionType, new Dictionary<string, string>(StringComparer.Ordinal) { { "subjectKind", "data-mapping" }, { "classificationKey", c.KeyPath } }, "a copy of " + Q(c.KeyPath) + " data outside production"));
            }
        }

        if (RowFacts.FindActive(content, "decision-type", EngineKeys.DataRetentionDecisionType) != null)
        {
            cases.Add((EngineKeys.DataRetentionDecisionType, new Dictionary<string, string>(StringComparer.Ordinal) { { "subjectKind", "data-use" } }, "the retention of a data use"));
        }

        foreach ((string type, Dictionary<string, string> record, string words) in cases)
        {
            RuleGroup? rule = DataRuleSupport.Winner(rules, type, record);
            if (rule != null && rule.Rows.Any(r => !RowFacts.IsTrue(r, "isConsultation")))
            {
                continue;
            }

            RuleGroup? right = DataRuleSupport.Winner(rights, type, record);
            if (right != null && right.Rows.Count(r => RowFacts.Text(r, "letter") == "accountable") == 1)
            {
                continue;
            }

            issues.Add(Issue("a " + Q(type) + " decision about " + words + " matches no approval rule group with a counting rule and no accountable decision right, so nobody can be asked. Add an approval rule or a decision right for it.", "approval-rule", null, null));
        }
    }
}

// V56: an owner subject names an owner role that can be held there; parent-data-domain only on rows scoped to subject
// kind data-domain; touched-data-domains only on configuration change rows.
public sealed class V56OwnerSubject : ValidationRule, IContentRule
{
    public static readonly V56OwnerSubject Instance = new();

    private V56OwnerSubject()
        : base("V56", ReasonCodes.DataOwnerSubjectRule, "Owner subjects fit their owner roles")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        foreach ((string entity, string roleField) in new[] { ("approval-rule", "approverOwnerRoleKey"), ("decision-right", "ownerRoleKey") })
        {
            foreach (ContentRow row in RowFacts.Active(context.Content, entity))
            {
                string? subject = RowFacts.Text(row, "ownerSubject");
                if (subject == null)
                {
                    continue;
                }

                string? role = RowFacts.Text(row, roleField);
                string kind = DataRuleSupport.KindOf(subject);
                string name = ConfigurationEntities.LogicalNameOf(entity);
                if (role == null)
                {
                    issues.Add(Issue(name + " " + Q(row.KeyPath) + " names owner subject " + Q(subject) + " without an owner role. Name the owner role, or remove the owner subject.", entity, row.KeyPath, row.Pointer));
                }
                else if (!DataRuleSupport.RoleHeldOn(context.Content, role, kind))
                {
                    issues.Add(Issue(name + " " + Q(row.KeyPath) + " resolves owner role " + Q(role) + " on a " + kind + ", and that role's Subject Kinds leave it out. Add the subject kind to the role, or name another role.", entity, row.KeyPath, row.Pointer));
                }

                if (subject == "parent-data-domain" && RowFacts.Text(row, "subjectKind") != "data-domain")
                {
                    issues.Add(Issue(name + " " + Q(row.KeyPath) + " names owner subject parent-data-domain on a row not scoped to subject kind data-domain. Scope it to data-domain.", entity, row.KeyPath, row.Pointer));
                }

                if (subject == "touched-data-domains" && !WellKnown.ConfigurationDecisionTypes.Contains(RowFacts.Text(row, "decisionTypeKey") ?? string.Empty, StringComparer.Ordinal))
                {
                    issues.Add(Issue(name + " " + Q(row.KeyPath) + " names owner subject touched-data-domains on a row that is not about a configuration change. Use it only on configuration change rows.", entity, row.KeyPath, row.Pointer));
                }
            }
        }
    }
}

// V57: ownership decisions about a data domain resolve exactly one accountable holder at each active level, a role or an
// owner role resolved on a domain, and never the parent at the top level; about a location, a role or an owner role
// resolved on the location. Only while data-taxonomy-mode is not off.
public sealed class V57OwnershipAccountable : ValidationRule, IContentRule
{
    public static readonly V57OwnershipAccountable Instance = new();

    private V57OwnershipAccountable()
        : base("V57", ReasonCodes.DataOwnershipAccountable, "Domain ownership has one accountable per level")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        ConfigurationContent content = context.Content;
        if (DataRuleSupport.Mode(content) == DataTaxonomyMode.Off || RowFacts.FindActive(content, "decision-type", EngineKeys.OwnershipDecisionType) == null)
        {
            return;
        }

        var tree = new DataContentTree(content);
        List<ContentRow> levels = RowFacts.Active(content, "data-domain-level").ToList();
        int? top = levels.Select(l => tree.Rank("data-domain-level", l.KeyPath)).Where(r => r.HasValue).Select(r => r!.Value).DefaultIfEmpty().Min();
        List<RuleGroup> rights = RuleGroups.DecisionRights(content);
        foreach (ContentRow level in levels)
        {
            bool atTop = tree.Rank("data-domain-level", level.KeyPath) == top;
            var record = new Dictionary<string, string>(StringComparer.Ordinal) { { "subjectKind", "data-domain" }, { "dataDomainLevelKey", level.KeyPath } };
            Check(rights, record, "a data domain at level " + Q(level.KeyPath), new[] { "data-domain", "parent-data-domain" }, atTop, issues, level);
        }

        if (RowFacts.Active(content, "data-platform").Any())
        {
            var record = new Dictionary<string, string>(StringComparer.Ordinal) { { "subjectKind", "data-location" } };
            Check(rights, record, "a data location", new[] { "data-location" }, false, issues, null);
        }
    }

    private void Check(List<RuleGroup> rights, Dictionary<string, string> record, string words, string[] subjects, bool atTop, ValidationIssues issues, ContentRow? at)
    {
        RuleGroup? winner = DataRuleSupport.Winner(rights, EngineKeys.OwnershipDecisionType, record);
        ContentRow[] accountable = winner == null ? Array.Empty<ContentRow>() : winner.Rows.Where(r => RowFacts.Text(r, "letter") == "accountable").ToArray();
        if (accountable.Length != 1)
        {
            issues.Add(Issue("an ownership decision about " + words + " resolves " + accountable.Length + " accountable decision rights instead of one. Add a decision right scoped to that subject kind.", "decision-right", winner?.Rows[0].KeyPath, at?.Pointer));
            return;
        }

        ContentRow row = accountable[0];
        string? ownerSubject = RowFacts.Text(row, "ownerSubject");
        if (RowFacts.Text(row, "ownerRoleKey") != null && (ownerSubject == null || !subjects.Contains(ownerSubject, StringComparer.Ordinal)))
        {
            issues.Add(Issue("decision right " + Q(row.KeyPath) + " makes owner role " + Q(RowFacts.Text(row, "ownerRoleKey")) + " accountable for " + words + " on " + Q(ownerSubject ?? "business-application") + ", which that subject has no holder of. Resolve it on " + string.Join(" or ", subjects) + ", or name a role.", "decision-right", row.KeyPath, row.Pointer));
        }
        else if (atTop && ownerSubject == "parent-data-domain")
        {
            issues.Add(Issue("decision right " + Q(row.KeyPath) + " asks the parent domain's owner about " + words + ", and a top-level domain has no parent. Add a decision right for the top level that names a role.", "decision-right", row.KeyPath, row.Pointer));
        }
    }
}

// V58: platforms name a link kind of category data-location, their patterns compile, and some target stage is production.
public sealed class V58Platform : ValidationRule, IContentRule
{
    public static readonly V58Platform Instance = new();

    private V58Platform()
        : base("V58", ReasonCodes.DataPlatform, "Platforms and stages fit the data checks")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        ConfigurationContent content = context.Content;
        List<ContentRow> platforms = RowFacts.Active(content, "data-platform").ToList();
        foreach (ContentRow row in platforms)
        {
            string? kindKey = RowFacts.Text(row, "defaultLinkKindKey");
            ContentRow? kind = RowFacts.FindActive(content, "link-kind", kindKey);
            if (kindKey != null && kind != null && RowFacts.Text(kind, "linkCategory") != "data-location")
            {
                issues.Add(Issue("data platform " + Q(row.KeyPath) + " names link kind " + Q(kindKey) + ", which is not of category data-location. Name a data location link kind.", "data-platform", row.KeyPath, row.Pointer));
            }

            foreach (string field in new[] { "idPattern", "urlPattern" })
            {
                string? pattern = RowFacts.Text(row, field);
                if (pattern != null && !Compiles(pattern))
                {
                    issues.Add(Issue("data platform " + Q(row.KeyPath) + " has a " + field + " that does not compile as a regular expression. Fix the pattern.", "data-platform", row.KeyPath, row.Pointer));
                }
            }
        }

        bool data = platforms.Count > 0 || RowFacts.Active(content, "data-classification").Any();
        if (data && !RowFacts.Active(content, "target-stage").Any(s => RowFacts.IsTrue(s, "isProduction")))
        {
            issues.Add(Issue("no active target stage is production, so the copy to test rule cannot tell production from a copy. Set Is Production on the production stage.", "target-stage", null, null));
        }
    }

    private static bool Compiles(string pattern)
    {
        try
        {
            _ = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

// V59 (warning): a draft that loosens a domain's effective rule or lowers its classification lists what it affects.
public sealed class V59Loosens : ValidationRule, IContentRule
{
    public static readonly V59Loosens Instance = new();

    private V59Loosens()
        : base("V59", ReasonCodes.DataLoosens, "A loosening is shown on the card")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        if (context.Active == null || context.IsPresetBuild)
        {
            return;
        }

        DataTaxonomyDiff diff = DataTaxonomyDiff.Compute(context.Active, context.Content);
        if (diff.LoosenedDomainKeys.Count == 0)
        {
            return;
        }

        var tree = new DataContentTree(context.Content);
        issues.Add(Issue("this draft loosens the rules or lowers the classification of " + string.Join(", ", diff.LoosenedDomainKeys.Select(k => Q(tree.Path(k)))) + ". Their data owners must approve. The card lists the uses that would then need no decision, and those that would break a rule.", "data-domain", diff.LoosenedDomainKeys[0], null));
    }
}

// V60: a shipped preset holds no domain with tenant facts, no Fabric or Purview domain match and no label ID.
public sealed class V60PresetTenant : ValidationRule, IContentRule
{
    public static readonly V60PresetTenant Instance = new();

    private static readonly Regex Tenant = new(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}|https?://|@[A-Za-z0-9-]+\.[A-Za-z]", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly string[] CatalogVocabularies = { "fabric-domain", "purview-governance-domain" };

    private V60PresetTenant()
        : base("V60", ReasonCodes.DataPresetTenant, "Presets hold no tenant data facts")
    {
    }

    public void Check(ValidationContext context, ValidationIssues issues)
    {
        if (!context.IsPresetBuild)
        {
            return;
        }

        foreach (ContentRow row in RowFacts.Active(context.Content, "data-domain"))
        {
            foreach (string field in new[] { "label", "description", "examples" })
            {
                string? text = RowFacts.Text(row, field);
                if (text != null && Tenant.IsMatch(text))
                {
                    issues.Add(Issue("data domain " + Q(row.KeyPath) + " holds a tenant fact in its " + field + " (an ID, a URL or an email). A shipped preset names business words only.", "data-domain", row.KeyPath, row.Pointer));
                }
            }
        }

        foreach (ContentRow row in RowFacts.Active(context.Content, "vocabulary-mapping"))
        {
            string? vocabulary = RowFacts.Text(row, "vocabularyKey");
            if (vocabulary != null && CatalogVocabularies.Contains(vocabulary, StringComparer.Ordinal) && RowFacts.Text(row, "sourceEntity") == "data-domain")
            {
                issues.Add(Issue("vocabulary mapping " + Q(row.KeyPath) + " matches a data domain to a " + vocabulary + ", which is a tenant catalog fact. A shipped preset never holds it; the install adds it.", "vocabulary-mapping", row.KeyPath, row.Pointer));
            }
            else if (vocabulary == "purview-sensitivity-label" && (RowFacts.Text(row, "term") ?? string.Empty) is string term && Regex.IsMatch(term, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            {
                issues.Add(Issue("vocabulary mapping " + Q(row.KeyPath) + " holds a sensitivity label ID, which is a tenant fact. A shipped preset names the default label only.", "vocabulary-mapping", row.KeyPath, row.Pointer));
            }
        }
    }
}
