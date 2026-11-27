using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;
using Product.Engine.Core.Snapshots;

namespace Product.Engine.Core.Operations;

// The three handling rules a data domain carries (C47 defaults, C49 tightenings; docs/architecture/data-taxonomy.md, The main fields and Effective values on a domain).
public enum DataRuleName
{
    // Reads and writes that do not reach AI.
    Use,

    // Uses that reach an AI model.
    Ai,

    // Production copies outside production.
    Copy,
}

// One effective handling rule and where it comes from: the domain that sets it, or the classification's default.
public sealed class EffectiveDataRule
{
    internal EffectiveDataRule(DataRuleName name, DataHandlingRule value, string? setOnDomainKey, string? classificationKey)
    {
        Name = name;
        Value = value;
        SetOnDomainKey = setOnDomainKey;
        ClassificationKey = classificationKey;
    }

    public DataRuleName Name { get; }

    public DataHandlingRule Value { get; }

    // The domain whose explicit value is the strictest, or null when the classification's default is.
    public string? SetOnDomainKey { get; }

    // The effective classification whose default took part.
    public string? ClassificationKey { get; }

    public string NameWire => DataTree.RuleWire(Name);

    internal JsonTreeNode ToJson()
    {
        return new WireWriter()
            .Add("name", NameWire)
            .Add("value", EngineContract.DataHandlingRules.ToWire(Value))
            .Add("setOn", SetOnDomainKey)
            .Add("fromClassification", SetOnDomainKey == null ? ClassificationKey : null)
            .Build();
    }
}

// The effective classification of a domain: the nearest explicit value on it or above it.
public sealed class EffectiveClassification
{
    internal EffectiveClassification(string key, int rank, string setOnDomainKey)
    {
        Key = key;
        Rank = rank;
        SetOnDomainKey = setOnDomainKey;
    }

    public string Key { get; }

    public int Rank { get; }

    public string SetOnDomainKey { get; }

    internal JsonTreeNode ToJson()
    {
        return new WireWriter().Add("key", Key).Add("rank", Rank).Add("setOn", SetOnDomainKey).Build();
    }
}

// One O06 Owner Assignment as the adapter passes it to OP-15, OP-16, OP-17 (a proposal of this build). The subject is
// a data domain key, a data location ID or an application ID; exactly one is set (G-19).
public sealed class DataOwnerFact
{
    internal static readonly string[] Members =
    {
        "assignmentId", "ownerRoleKey", "principalId", "isDeputy", "deputyForId", "startOn", "endOn", "dataDomainKey", "dataLocationId", "applicationId",
    };

    public DataOwnerFact(string assignmentId, string ownerRoleKey, string principalId, bool isDeputy, string? deputyForId, string? startOn, string? endOn, string? dataDomainKey, string? dataLocationId, string? applicationId)
    {
        AssignmentId = assignmentId;
        OwnerRoleKey = ownerRoleKey;
        PrincipalId = principalId;
        IsDeputy = isDeputy;
        DeputyForId = deputyForId;
        StartOn = startOn;
        EndOn = endOn;
        DataDomainKey = dataDomainKey;
        DataLocationId = dataLocationId;
        ApplicationId = applicationId;
    }

    public string AssignmentId { get; }

    public string OwnerRoleKey { get; }

    public string PrincipalId { get; }

    public bool IsDeputy { get; }

    public string? DeputyForId { get; }

    public string? StartOn { get; }

    public string? EndOn { get; }

    public string? DataDomainKey { get; }

    public string? DataLocationId { get; }

    public string? ApplicationId { get; }

    // Current on the day: Start On on or before it, End On empty or after it (document 26, RA-02).
    public bool IsCurrent(string day)
    {
        return (StartOn == null || string.CompareOrdinal(DataTree.Day(StartOn), day) <= 0)
            && (EndOn == null || string.CompareOrdinal(DataTree.Day(EndOn), day) > 0);
    }

    internal static DataOwnerFact FromWire(WireObject wire)
    {
        string? domain = wire.OptionalKey("dataDomainKey");
        string? location = wire.OptionalString("dataLocationId");
        string? app = wire.OptionalString("applicationId");
        if ((domain == null ? 0 : 1) + (location == null ? 0 : 1) + (app == null ? 0 : 1) != 1)
        {
            throw OperationStop.Input(ReasonCodes.InputMalformed, wire.Where + " must name exactly one of dataDomainKey, dataLocationId and applicationId.");
        }

        return new DataOwnerFact(
            wire.RequiredString("assignmentId"),
            wire.RequiredKey("ownerRoleKey"),
            wire.RequiredString("principalId"),
            wire.Bool("isDeputy", false),
            wire.OptionalString("deputyForId"),
            wire.OptionalString("startOn"),
            wire.OptionalString("endOn"),
            domain,
            location,
            app);
    }
}

// The directory state of one principal (O05 Directory State and Leaves On), as the owner check reads it.
public sealed class DataPrincipalFact
{
    internal static readonly string[] Members = { "principalId", "directoryState", "leavesOn", "isErased" };

    public DataPrincipalFact(string principalId, string? directoryState, string? leavesOn, bool isErased)
    {
        PrincipalId = principalId;
        DirectoryState = directoryState;
        LeavesOn = leavesOn;
        IsErased = isErased;
    }

    public string PrincipalId { get; }

    // enabled, disabled, deleted or unknown. Null means not read yet, which counts as enabled.
    public string? DirectoryState { get; }

    public string? LeavesOn { get; }

    public bool IsErased { get; }

    // Disabled, deleted, erased or past Leaves On: the deputy acts (P08, step 2).
    public bool CannotAct(string day)
    {
        return IsErased || DirectoryState == "disabled" || DirectoryState == "deleted" || (LeavesOn != null && string.CompareOrdinal(DataTree.Day(LeavesOn), day) <= 0);
    }

    // Disabled, deleted, erased or with a leave date: DC-04 shows it.
    public bool IsInactiveOrLeaving => IsErased || DirectoryState == "disabled" || DirectoryState == "deleted" || LeavesOn != null;

    internal static DataPrincipalFact FromWire(WireObject wire)
    {
        string? state = wire.OptionalString("directoryState");
        if (state != null && state != "enabled" && state != "disabled" && state != "deleted" && state != "unknown")
        {
            throw OperationStop.Input(ReasonCodes.InputContractValueUnknown, wire.Where + ".directoryState is \"" + state + "\", which is not one of enabled, disabled, deleted, unknown.");
        }

        return new DataPrincipalFact(wire.RequiredString("principalId"), state, wire.OptionalString("leavesOn"), wire.Bool("isErased", false));
    }
}

// Who holds one owner role for one subject, as OP-15 resolves it.
public sealed class OwnerResolution
{
    internal OwnerResolution(string ownerRoleKey, bool inheritsDown, bool requiresDeputy, IReadOnlyList<DataOwnerFact> holders, IReadOnlyList<DataOwnerFact> deputies, string? sourceKey, int distance, string? actingPrincipalId)
    {
        OwnerRoleKey = ownerRoleKey;
        InheritsDown = inheritsDown;
        RequiresDeputy = requiresDeputy;
        Holders = holders;
        Deputies = deputies;
        SourceKey = sourceKey;
        Distance = distance;
        ActingPrincipalId = actingPrincipalId;
    }

    public string OwnerRoleKey { get; }

    public bool InheritsDown { get; }

    public bool RequiresDeputy { get; }

    // The current main holders on the node that holds the role, earliest Start On first. Empty when nobody holds it.
    public IReadOnlyList<DataOwnerFact> Holders { get; }

    public IReadOnlyList<DataOwnerFact> Deputies { get; }

    // The domain key (or location ID) whose rows are used. Null when nobody holds the role.
    public string? SourceKey { get; }

    // Levels up from the subject: 0 when the subject holds the role itself.
    public int Distance { get; }

    // The person who answers now: the first holder who can act, else the first deputy who can act. Null for nobody.
    public string? ActingPrincipalId { get; }

    public DataOwnerFact? Holder => Holders.Count == 0 ? null : Holders[0];

    // Nobody holds the role: the subject or its parent does not exist.
    public static OwnerResolution Nobody(string ownerRoleKey)
    {
        return new OwnerResolution(ownerRoleKey, false, false, Array.Empty<DataOwnerFact>(), Array.Empty<DataOwnerFact>(), null, 0, null);
    }

    public bool IsInherited => Distance > 0;

    internal JsonTreeNode ToJson(Func<string, DataPrincipalFact?> principal)
    {
        JsonTreeNode Person(DataOwnerFact row)
        {
            DataPrincipalFact? p = principal(row.PrincipalId);
            return new WireWriter()
                .Add("assignmentId", row.AssignmentId)
                .Add("principalId", row.PrincipalId)
                .Add("directoryState", p?.DirectoryState)
                .Add("leavesOn", p?.LeavesOn)
                .Build();
        }

        return new WireWriter()
            .Add("ownerRoleKey", OwnerRoleKey)
            .Add("inheritsDown", InheritsDown)
            .Add("holder", Holder == null ? null : Person(Holder))
            .Add("otherHolders", Holders.Count > 1 ? WireWriter.Array(Holders.Skip(1).Select(Person)) : null)
            .Add("deputies", WireWriter.Array(Deputies.Select(Person)))
            .Add("inheritedFrom", IsInherited ? SourceKey : null)
            .Add("distance", Holder == null ? (int?)null : Distance)
            .Add("actingPrincipalId", ActingPrincipalId)
            .Build();
    }
}

// The data domain tree of the snapshot in force (C47 to C49) and the owner facts, read in memory (docs/architecture/data-taxonomy.md,
// Who answers and Effective values on a domain). Shared by OP-15, OP-16, OP-17, OP-08 (G-18, G-20, G-25) and the runtime.
//
// Choices of this build (D-41):
// - An effective rule is the strictest of every explicit value on the domain and its ancestors and the effective
//   classification's default. On a tie the nearest domain is named as its source, and a domain wins over the default.
// - When a node has more than one current main holder of a role, the earliest Start On holds it (then the smaller ID);
//   the others are listed. A deputy covers the holder it names, or any holder when it names none.
// - The walk stops after 32 levels or at a cycle, which V49 refuses before activation.
public sealed class DataTree
{
    private const int MaxDepth = 32;
    private readonly ConfigurationSnapshot _snapshot;
    private readonly Dictionary<string, SnapshotRow> _domains = new(StringComparer.Ordinal);

    public DataTree(ConfigurationSnapshot snapshot)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        foreach (SnapshotRow row in SnapshotRead.Rows(snapshot, "data-domain"))
        {
            _domains[row.KeyPath] = row;
        }
    }

    public ConfigurationSnapshot Snapshot => _snapshot;

    // data-taxonomy-mode. A snapshot without the setting reads as off, as every install before the data taxonomy
    // behaved; V38 requires the setting, so an activated set always has it.
    public DataTaxonomyMode Mode
    {
        get
        {
            SnapshotRow? row = SnapshotRead.FindActive(_snapshot, "setting", EngineKeys.DataTaxonomyModeSetting);
            JsonTreeNode? value = row == null ? null : SnapshotRead.Node(row, "value");
            return value != null && value.Kind == JsonTreeKind.String && EngineContract.DataTaxonomyModes.TryParse(value.GetString(), out DataTaxonomyMode mode) ? mode : DataTaxonomyMode.Off;
        }
    }

    public IEnumerable<SnapshotRow> Domains => _domains.Values.OrderBy(r => r.KeyPath, StringComparer.Ordinal);

    public IEnumerable<SnapshotRow> ActiveDomains => Domains.Where(r => r.IsActive);

    public SnapshotRow? Domain(string? key)
    {
        return key != null && _domains.TryGetValue(key, out SnapshotRow? row) ? row : null;
    }

    public bool IsActiveDomain(string? key)
    {
        return Domain(key)?.IsActive == true;
    }

    public SnapshotRow RequireDomain(string key, string field)
    {
        return Domain(key) ?? throw OperationStop.Input(ReasonCodes.InputUnknownKey, field + " is \"" + key + "\", which is not a data domain in the configuration in force.", key);
    }

    // The domain, then its parent, up to the top. Stops at a cycle or after 32 levels.
    public IReadOnlyList<SnapshotRow> Chain(string key)
    {
        var chain = new List<SnapshotRow>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        SnapshotRow? current = Domain(key);
        while (current != null && chain.Count < MaxDepth && seen.Add(current.KeyPath))
        {
            chain.Add(current);
            current = Domain(SnapshotRead.Text(current, "parentKey"));
        }

        return chain;
    }

    public string? LevelKeyOf(string domainKey)
    {
        SnapshotRow? row = Domain(domainKey);
        return row == null ? null : SnapshotRead.Text(row, "levelKey");
    }

    // The tier question the classifications imply an answer to (C47 Implied Tier Question), such as sensitivity.
    public string? ImpliedQuestionKey => SnapshotRead.ActiveRows(_snapshot, "data-classification").Select(r => SnapshotRead.Text(r, "impliedTierQuestionKey")).FirstOrDefault(q => q != null);

    public string? ParentKey(string key)
    {
        SnapshotRow? row = Domain(key);
        return row == null ? null : SnapshotRead.Text(row, "parentKey");
    }

    // "People / Payroll / Bank details": labels from the top down.
    public string PathText(string key)
    {
        return string.Join(" / ", Chain(key).Reverse().Select(Label));
    }

    public static string Label(SnapshotRow row)
    {
        return SnapshotRead.Text(row, "label") ?? row.KeyPath;
    }

    // Every domain whose chain passes through the key, the key itself first.
    public IReadOnlyList<string> Subtree(string key)
    {
        return new[] { key }.Concat(Domains.Where(d => d.KeyPath != key && Chain(d.KeyPath).Skip(1).Any(a => a.KeyPath == key)).Select(d => d.KeyPath)).ToArray();
    }

    public SnapshotRow? LevelOf(string domainKey)
    {
        SnapshotRow? row = Domain(domainKey);
        return row == null ? null : SnapshotRead.Find(_snapshot, "data-domain-level", SnapshotRead.Text(row, "levelKey"));
    }

    // Declarable defaults to yes (docs/architecture/data-taxonomy.md, The main fields).
    public bool IsDeclarable(string domainKey)
    {
        SnapshotRow? level = LevelOf(domainKey);
        return level == null || !level.TryGetColumn("declarable", out JsonTreeNode? value) || value.Kind != JsonTreeKind.False;
    }

    public bool OwnerRequired(string domainKey)
    {
        SnapshotRow? level = LevelOf(domainKey);
        return level != null && SnapshotRead.Flag(level, "ownerRequired");
    }

    public EffectiveClassification? Classification(string domainKey)
    {
        foreach (SnapshotRow node in Chain(domainKey))
        {
            string? key = SnapshotRead.Text(node, "classificationKey");
            if (key == null)
            {
                continue;
            }

            SnapshotRow? row = SnapshotRead.Find(_snapshot, "data-classification", key);
            int rank = row == null ? 0 : SnapshotRead.Int(row, "data-classification", "rank") ?? 0;
            return new EffectiveClassification(key, rank, node.KeyPath);
        }

        return null;
    }

    public SnapshotRow? ClassificationRow(string? key)
    {
        return SnapshotRead.Find(_snapshot, "data-classification", key);
    }

    // The strictest of the explicit values on the chain and the effective classification's default (docs/architecture/data-taxonomy.md, Effective values on a domain).
    public EffectiveDataRule Rule(string domainKey, DataRuleName name)
    {
        EffectiveClassification? classification = Classification(domainKey);
        DataHandlingRule best = DataHandlingRule.Allowed;
        string? source = null;
        bool found = false;
        SnapshotRow? classRow = ClassificationRow(classification?.Key);
        if (classRow != null && TryRule(classRow, DefaultColumn(name), out DataHandlingRule fallback))
        {
            best = fallback;
            found = true;
        }

        // From the top down, so the nearest domain wins a tie between domains, and a domain wins a tie with the default.
        foreach (SnapshotRow node in Chain(domainKey).Reverse())
        {
            if (TryRule(node, DomainColumn(name), out DataHandlingRule value) && (!found || value >= best))
            {
                best = value;
                source = node.KeyPath;
                found = true;
            }
        }

        return new EffectiveDataRule(name, best, source, classification?.Key);
    }

    // The governing rule (docs/architecture/data-taxonomy.md, The decision state): for a use without AI, the use rule; with AI, the stricter of the use rule and the
    // AI rule (the AI rule on a tie); for a copy, the copy rule.
    public EffectiveDataRule GoverningRuleForUse(string domainKey, bool reachesAi)
    {
        EffectiveDataRule use = Rule(domainKey, DataRuleName.Use);
        if (!reachesAi)
        {
            return use;
        }

        EffectiveDataRule ai = Rule(domainKey, DataRuleName.Ai);
        return use.Value > ai.Value ? use : ai;
    }

    public (string? Key, string? SetOn) Retention(string domainKey)
    {
        foreach (SnapshotRow node in Chain(domainKey))
        {
            string? key = SnapshotRead.Text(node, "retentionClassKey");
            if (key != null)
            {
                return (key, node.KeyPath);
            }
        }

        return (null, null);
    }

    // The implied answer to the implied tier question of the domain's effective classification (C47).
    public (string? QuestionKey, string? AnswerKey) ImpliedAnswer(string domainKey)
    {
        SnapshotRow? row = ClassificationRow(Classification(domainKey)?.Key);
        return row == null ? (null, null) : (SnapshotRead.Text(row, "impliedTierQuestionKey"), SnapshotRead.Text(row, "impliedAnswerKey"));
    }

    // The rank of an answer option: the rank of the tier it implies. Null when the option or its tier is unknown.
    public int? AnswerRank(string? questionKey, string? answerKey)
    {
        if (questionKey == null || answerKey == null)
        {
            return null;
        }

        SnapshotRow? option = SnapshotRead.Find(_snapshot, "tier-answer-option", questionKey + "/" + answerKey);
        SnapshotRow? tier = option == null ? null : SnapshotRead.Find(_snapshot, "tier", SnapshotRead.Text(option, "impliedTierKey"));
        return tier == null ? null : SnapshotRead.Int(tier, "tier", "rank");
    }

    // C34 Is Production of a target stage. An unknown stage is not production: refuse more, never less, because a copy
    // check that reads production would let a copy pass.
    public bool IsProductionStage(string? stageKey)
    {
        SnapshotRow? stage = SnapshotRead.Find(_snapshot, "target-stage", stageKey);
        return stage != null && SnapshotRead.Flag(stage, "isProduction");
    }

    // The active owner roles that can be held on a subject kind (C28 Subject Kinds; empty means the application only).
    public IReadOnlyList<SnapshotRow> OwnerRolesFor(OwnerSubjectKind kind)
    {
        return SnapshotRead.ActiveRows(_snapshot, "owner-role").Where(r => CanHold(r, kind)).OrderBy(r => r.KeyPath, StringComparer.Ordinal).ToArray();
    }

    public static bool CanHold(SnapshotRow ownerRole, OwnerSubjectKind kind)
    {
        IReadOnlyList<string> kinds = SnapshotRead.Strings(ownerRole, "subjectKinds");
        string wire = EngineContract.OwnerSubjectKinds.ToWire(kind);
        return kinds.Count == 0 ? kind == OwnerSubjectKind.BusinessApplication : kinds.Contains(wire, StringComparer.Ordinal);
    }

    // OP-15 rule: the role on the domain, walking up while the role inherits down (docs/architecture/data-taxonomy.md, Who answers).
    public OwnerResolution ResolveOnDomain(string ownerRoleKey, string domainKey, IEnumerable<DataOwnerFact> owners, Func<string, DataPrincipalFact?> principal, string day)
    {
        SnapshotRow? role = SnapshotRead.Find(_snapshot, "owner-role", ownerRoleKey);
        bool inherits = role != null && SnapshotRead.Flag(role, "inheritsDown");
        bool requiresDeputy = role != null && SnapshotRead.Flag(role, "requiresDeputy");
        List<DataOwnerFact> current = owners.Where(o => o.DataDomainKey != null && o.OwnerRoleKey == ownerRoleKey && o.IsCurrent(day)).ToList();
        int distance = 0;
        foreach (SnapshotRow node in Chain(domainKey))
        {
            List<DataOwnerFact> here = current.Where(o => o.DataDomainKey == node.KeyPath).ToList();
            if (here.Any(o => !o.IsDeputy))
            {
                return Resolve(ownerRoleKey, inherits, requiresDeputy, here, node.KeyPath, distance, principal, day);
            }

            if (!inherits)
            {
                break;
            }

            distance++;
        }

        return new OwnerResolution(ownerRoleKey, inherits, requiresDeputy, Array.Empty<DataOwnerFact>(), Array.Empty<DataOwnerFact>(), null, 0, null);
    }

    // The role on a data location: never inherited (a technical owner does not inherit down).
    public OwnerResolution ResolveOnLocation(string ownerRoleKey, string locationId, IEnumerable<DataOwnerFact> owners, Func<string, DataPrincipalFact?> principal, string day)
    {
        SnapshotRow? role = SnapshotRead.Find(_snapshot, "owner-role", ownerRoleKey);
        bool requiresDeputy = role != null && SnapshotRead.Flag(role, "requiresDeputy");
        List<DataOwnerFact> here = owners.Where(o => o.DataLocationId != null && string.Equals(o.DataLocationId, locationId, StringComparison.OrdinalIgnoreCase) && o.OwnerRoleKey == ownerRoleKey && o.IsCurrent(day)).ToList();
        if (!here.Any(o => !o.IsDeputy))
        {
            return new OwnerResolution(ownerRoleKey, false, requiresDeputy, Array.Empty<DataOwnerFact>(), Array.Empty<DataOwnerFact>(), null, 0, null);
        }

        return Resolve(ownerRoleKey, false, requiresDeputy, here, locationId, 0, principal, day);
    }

    // The role on an application: today's owners (O06 with the application as subject).
    public OwnerResolution ResolveOnApplication(string ownerRoleKey, string? applicationId, IEnumerable<DataOwnerFact> owners, Func<string, DataPrincipalFact?> principal, string day)
    {
        SnapshotRow? role = SnapshotRead.Find(_snapshot, "owner-role", ownerRoleKey);
        bool requiresDeputy = role != null && SnapshotRead.Flag(role, "requiresDeputy");
        List<DataOwnerFact> here = applicationId == null ? new List<DataOwnerFact>()
            : owners.Where(o => o.ApplicationId != null && string.Equals(o.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase) && o.OwnerRoleKey == ownerRoleKey && o.IsCurrent(day)).ToList();
        if (!here.Any(o => !o.IsDeputy))
        {
            return new OwnerResolution(ownerRoleKey, false, requiresDeputy, Array.Empty<DataOwnerFact>(), Array.Empty<DataOwnerFact>(), null, 0, null);
        }

        return Resolve(ownerRoleKey, false, requiresDeputy, here, applicationId!, 0, principal, day);
    }

    private static OwnerResolution Resolve(string role, bool inherits, bool requiresDeputy, List<DataOwnerFact> here, string source, int distance, Func<string, DataPrincipalFact?> principal, string day)
    {
        List<DataOwnerFact> holders = here.Where(o => !o.IsDeputy)
            .OrderBy(o => o.StartOn == null ? string.Empty : Day(o.StartOn), StringComparer.Ordinal)
            .ThenBy(o => o.AssignmentId, StringComparer.Ordinal)
            .ToList();
        var holderIds = new HashSet<string>(holders.Select(h => h.AssignmentId), StringComparer.OrdinalIgnoreCase);
        List<DataOwnerFact> deputies = here.Where(o => o.IsDeputy && (o.DeputyForId == null || holderIds.Contains(o.DeputyForId)))
            .OrderBy(o => o.AssignmentId, StringComparer.Ordinal)
            .ToList();
        bool CanAct(DataOwnerFact o) => principal(o.PrincipalId)?.CannotAct(day) != true;
        string? acting = holders.FirstOrDefault(CanAct)?.PrincipalId ?? deputies.FirstOrDefault(CanAct)?.PrincipalId;
        return new OwnerResolution(role, inherits, requiresDeputy, holders, deputies, source, distance, acting);
    }

    // The yyyy-MM-dd part of a date or a moment.
    internal static string Day(string value)
    {
        return value.Length >= 10 ? value.Substring(0, 10) : value;
    }

    public static string DayOf(DateTime moment)
    {
        return moment.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    // True when a data row with this End On still stands open on the given day: no End On, or an End On after it. A
    // row that names a future End On is open until that day (security review: G-25 and every read judge it as open).
    public static bool IsOpenOn(string? endedOn, string day)
    {
        return string.IsNullOrEmpty(endedOn) || string.CompareOrdinal(Day(endedOn!), Day(day)) > 0;
    }

    internal static string RuleWire(DataRuleName name)
    {
        switch (name)
        {
            case DataRuleName.Use:
                return "use";
            case DataRuleName.Ai:
                return "ai";
            default:
                return "copy";
        }
    }

    internal static string RuleWords(DataRuleName name)
    {
        switch (name)
        {
            case DataRuleName.Use:
                return "use rule";
            case DataRuleName.Ai:
                return "AI use rule";
            default:
                return "copy to test rule";
        }
    }

    private static string DomainColumn(DataRuleName name)
    {
        switch (name)
        {
            case DataRuleName.Use:
                return "useRule";
            case DataRuleName.Ai:
                return "aiUseRule";
            default:
                return "copyToTestRule";
        }
    }

    private static string DefaultColumn(DataRuleName name)
    {
        switch (name)
        {
            case DataRuleName.Use:
                return "defaultUseRule";
            case DataRuleName.Ai:
                return "defaultAiUseRule";
            default:
                return "defaultCopyToTestRule";
        }
    }

    private static bool TryRule(SnapshotRow row, string column, out DataHandlingRule value)
    {
        return EngineContract.DataHandlingRules.TryParse(SnapshotRead.Text(row, column), out value);
    }

    // One sentence naming the rule, the domain and where it is set, for refusals and findings (docs/architecture/data-taxonomy.md, Data checks).
    public string DescribeRule(EffectiveDataRule rule, string domainKey)
    {
        // The classification by its label ("Highly Confidential"), and the rule value in words (QA, fix round 1: "The AI
        // use rule is decision, the default of the classification restricted").
        SnapshotRow? classification = rule.ClassificationKey == null ? null : SnapshotRead.Find(_snapshot, "data-classification", rule.ClassificationKey);
        string classificationLabel = (classification == null ? null : SnapshotRead.Text(classification, "label")) ?? (rule.ClassificationKey ?? "none").Replace('-', ' ');
        string where = rule.SetOnDomainKey != null
            ? "set on " + PathText(rule.SetOnDomainKey) + (rule.SetOnDomainKey == domainKey ? string.Empty : " and inherited by " + PathText(domainKey))
            : "the default of the classification " + classificationLabel + " on " + PathText(domainKey);
        string value = rule.Value == DataHandlingRule.Decision ? "needs a decision" : "is " + EngineContract.DataHandlingRules.ToWire(rule.Value).Replace('-', ' ');
        return "the " + RuleWords(rule.Name) + " " + value + ", " + where;
    }
}
