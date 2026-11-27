using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Product.Engine.Configuration.Json;
using Product.Engine.Configuration.Model;
using Product.Engine.Core.Canonical;

namespace Product.Engine.Configuration.Validation.Rules;

// Keys the engine itself names. Labels stay configurable. Keys never change (document 21, rule 7).
internal static class WellKnown
{
    // The three decision types the engine names: the locked list names change authorization,
    // and document 21, section 4.2 names the two configuration decision types.
    // WORKING DEFAULT (open design question 1): these keys are part of the short list of fixed engine meanings.
    public const string ChangeAuthorization = "change-authorization";
    public const string ConfigurationChange = "configuration-change";
    public const string GatePolicyChange = "gate-policy-change";

    public static readonly string[] ConfigurationDecisionTypes = { ConfigurationChange, GatePolicyChange };

    // The one role with Can Change Configuration (document 20, C29; document 26, RR-03). The engine check
    // role.config-admin reads it.
    // WORKING DEFAULT (open design question 1): this role key is part of the short list of fixed engine meanings.
    public const string ConfigurationAdminRole = "configuration-admin";

    // Subjects that carry a tier: the application, its intake (through its tier assessment, O08) and its change
    // requests. A configuration draft carries none (document 21, V19 and V20; document 22, O07 and O11).
    public static readonly HashSet<string> TieredSubjectKinds = new(StringComparer.Ordinal)
    {
        "business-application",
        "intake-request",
        "change-request",

        // A data use belongs to an application, so its retention decision carries the application's tier (data taxonomy).
        "data-use",
    };

    public const string AiApprovalLock = "ai-approval-lock";
    public const string EngineAcceptableDecisionTypes = "engine-acceptable-decision-types";
    public const string WorkloadGrantMaxDays = "workload-grant-max-days";
    public const string ChangeManagementMode = "change-management-mode";
    public const string ForbiddenUrlPatterns = "forbidden-url-patterns";
    public const string GuidelineHubBaseUrl = "guideline-hub-base-url";

    // WORKING DEFAULT (open design question 8): the shipped locked list of decisions the engine may accept
    // without a person's card. Extending it needs a product release (document 21, section 3.6).
    // The list lives in the core (EngineAcceptance), so V33 and OP-02 read the same entries.
    public static readonly KeyValuePair<string, string>[] ShippedEngineAcceptance = Core.Contract.EngineAcceptance.ShippedEntries
        .Select(entry => new KeyValuePair<string, string>(entry.DecisionTypeKey, entry.ConditionWire))
        .ToArray();
}

// Small readers over content rows.
internal static class RowFacts
{
    public static string? Text(ContentRow row, string field)
    {
        return JsonNodes.GetString(row.Node, field);
    }

    public static bool IsTrue(ContentRow row, string field)
    {
        return JsonNodes.Get(row.Node, field)?.Kind == JsonTreeKind.True;
    }

    public static bool Has(ContentRow row, string field)
    {
        JsonTreeNode? value = JsonNodes.Get(row.Node, field);
        return value != null && value.Kind != JsonTreeKind.Null;
    }

    // Active rows with a key. A child row of a retired parent counts as retired.
    public static IEnumerable<ContentRow> Active(ConfigurationContent content, string entity)
    {
        return content.RowsFor(entity).Where(r => r.Key != null && r.IsEffectivelyActive);
    }

    public static ContentRow? FindActive(ConfigurationContent content, string entity, string? keyPath)
    {
        return keyPath != null && content.TryFindRow(entity, keyPath, out ContentRow? row) && row.IsEffectivelyActive ? row : null;
    }

    // The keys of active rows, in row order.
    public static List<string> ActiveKeys(ConfigurationContent content, string entity)
    {
        return Active(content, entity).Select(r => r.KeyPath).Distinct(StringComparer.Ordinal).ToList();
    }

    // A JSON number as a decimal. False for anything else (V01 reports the type).
    public static bool TryNumber(JsonTreeNode? node, out decimal value)
    {
        value = 0;
        return node != null && node.Kind == JsonTreeKind.Number && node.TryGetDecimal(out value);
    }

    public static string Number(decimal value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    // A date in the form yyyy-MM-dd.
    public static bool TryDate(string? text, out DateTime value)
    {
        return DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    public static string Date(DateTime value)
    {
        return value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}

// What the validator knows about one role row (C29).
internal sealed class RoleInfo
{
    private RoleInfo(ContentRow row)
    {
        Row = row;
        IsWorkload = RowFacts.IsTrue(row, "isWorkloadRole");
        IsReadOnly = RowFacts.IsTrue(row, "isReadOnly");
        CanChangeConfiguration = RowFacts.IsTrue(row, "canChangeConfiguration");
    }

    public ContentRow Row { get; }

    public string Key => Row.KeyPath;

    public bool IsWorkload { get; }

    public bool IsReadOnly { get; }

    public bool CanChangeConfiguration { get; }

    // Every role row by key, retired ones too. With repeated keys (V03) the first row wins.
    public static Dictionary<string, RoleInfo> All(ConfigurationContent content)
    {
        var roles = new Dictionary<string, RoleInfo>(StringComparer.Ordinal);
        foreach (ContentRow row in content.RowsFor("role").Where(r => r.Key != null))
        {
            if (!roles.ContainsKey(row.KeyPath))
            {
                roles.Add(row.KeyPath, new RoleInfo(row));
            }
        }

        return roles;
    }

    // "a workload role" or "a read-only role", for messages.
    public string Kind => IsWorkload ? "a workload role" : IsReadOnly ? "a read-only role" : "a role";
}

// A rule group: the active rows of one entity with the same decision type and the same scope values
// (document 21, section 10.3). Specificity is the number of scope fields set.
internal sealed class RuleGroup
{
    private static readonly Dictionary<string, string> FieldWords = new(StringComparer.Ordinal)
    {
        { "decisionTypeKey", "decision type" },
        { "tierKey", "tier" },
        { "lifecycleStageKey", "lifecycle stage" },
        { "changeTypeKey", "change type" },
        { "applicationKindKey", "application kind" },
        { "targetStageKey", "target stage" },
        { "portfolioId", "portfolio" },
        { "subjectKind", "subject kind" },
        { "classificationKey", "classification" },
        { "aiUse", "AI use" },
        { "dataDomainLevelKey", "data domain level" },
        { "dataTaxonomyChange", "data taxonomy change" },
    };

    public RuleGroup(string entity, string decisionTypeKey, IList<KeyValuePair<string, string>> scope)
    {
        Entity = entity;
        DecisionTypeKey = decisionTypeKey;
        Scope = scope.ToList();
    }

    public string Entity { get; }

    public string DecisionTypeKey { get; }

    // The scope fields that are set, in field order.
    public IReadOnlyList<KeyValuePair<string, string>> Scope { get; }

    public int Specificity => Scope.Count;

    public List<ContentRow> Rows { get; } = new();

    public string? Value(string field)
    {
        foreach (KeyValuePair<string, string> pair in Scope)
        {
            if (string.Equals(pair.Key, field, StringComparison.Ordinal))
            {
                return pair.Value;
            }
        }

        return null;
    }

    // True when every set scope field equals the record's value for it.
    public bool Matches(IDictionary<string, string> record)
    {
        return Scope.All(pair => record.TryGetValue(pair.Key, out string? value) && string.Equals(value, pair.Value, StringComparison.Ordinal));
    }

    // True when the scope sets only these fields.
    public bool UsesOnly(params string[] fields)
    {
        return Scope.All(pair => fields.Contains(pair.Key, StringComparer.Ordinal));
    }

    // True when no field is set in both scopes with different values, so one record can match both.
    public bool CompatibleWith(RuleGroup other)
    {
        return Scope.All(pair => other.Value(pair.Key) == null || string.Equals(other.Value(pair.Key), pair.Value, StringComparison.Ordinal));
    }

    // The scope of both groups together.
    public Dictionary<string, string> MergedWith(RuleGroup other)
    {
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> pair in Scope.Concat(other.Scope))
        {
            merged[pair.Key] = pair.Value;
        }

        return merged;
    }

    // "decision type `go-live`, tier `tier-3`", or "decision type `go-live`, no scope".
    public string Describe()
    {
        return "decision type `" + DecisionTypeKey + "`, " + DescribeScope(Scope);
    }

    public static string DescribeScope(IEnumerable<KeyValuePair<string, string>> scope)
    {
        List<KeyValuePair<string, string>> list = scope.ToList();
        return list.Count == 0 ? "no scope" : string.Join(", ", list.Select(pair => Word(pair.Key) + " `" + pair.Value + "`"));
    }

    public static string Word(string field)
    {
        return FieldWords.TryGetValue(field, out string? word) ? word : field;
    }

    public string RowList()
    {
        return string.Join(", ", Rows.Select(r => "`" + r.KeyPath + "`"));
    }
}

internal static class RuleGroups
{
    // Optional scope fields of C32 and C31 (document 21, section 3.5).
    // The data taxonomy adds five scope fields (docs/architecture/data-taxonomy.md, Entities), counted for specificity like the others.
    public static readonly string[] ApprovalScope =
    {
        "tierKey", "lifecycleStageKey", "changeTypeKey", "applicationKindKey", "targetStageKey",
        "subjectKind", "classificationKey", "aiUse", "dataDomainLevelKey", "dataTaxonomyChange", "portfolioId",
    };

    public static readonly string[] DecisionRightScope =
    {
        "tierKey", "lifecycleStageKey", "changeTypeKey", "applicationKindKey",
        "subjectKind", "classificationKey", "aiUse", "dataDomainLevelKey", "dataTaxonomyChange", "portfolioId",
    };

    public static List<RuleGroup> ApprovalRules(ConfigurationContent content)
    {
        return Build(content, "approval-rule", ApprovalScope);
    }

    public static List<RuleGroup> DecisionRights(ConfigurationContent content)
    {
        return Build(content, "decision-right", DecisionRightScope);
    }

    // Groups the active rows of an entity by decision type and scope, in the order the groups first appear.
    public static List<RuleGroup> Build(ConfigurationContent content, string entity, string[] scopeFields)
    {
        var groups = new List<RuleGroup>();
        var byKey = new Dictionary<string, RuleGroup>(StringComparer.Ordinal);
        foreach (ContentRow row in RowFacts.Active(content, entity))
        {
            string? decisionType = RowFacts.Text(row, "decisionTypeKey");
            if (decisionType == null)
            {
                // V01 reports the missing required field.
                continue;
            }

            var scope = new List<KeyValuePair<string, string>>();
            foreach (string field in scopeFields)
            {
                JsonTreeNode? value = JsonNodes.Get(row.Node, field);
                if (value != null && value.Kind != JsonTreeKind.Null)
                {
                    string text = value.Kind == JsonTreeKind.String ? value.GetString() : value.Kind == JsonTreeKind.True ? "true" : value.Kind == JsonTreeKind.False ? "false" : value.ToString();
                    scope.Add(new KeyValuePair<string, string>(field, text));
                }
            }

            string identity = decisionType + "\u0000" + string.Join("\u0000", scope.Select(p => p.Key + "=" + p.Value));
            if (!byKey.TryGetValue(identity, out RuleGroup? group))
            {
                group = new RuleGroup(entity, decisionType, scope);
                byKey.Add(identity, group);
                groups.Add(group);
            }

            group.Rows.Add(row);
        }

        return groups;
    }
}

// Decision types by key, with the two columns the coverage rules read.
internal sealed class DecisionTypeInfo
{
    private DecisionTypeInfo(ContentRow row)
    {
        Row = row;
        RequiresApproval = RowFacts.IsTrue(row, "requiresApproval");
        string? subject = RowFacts.Text(row, "defaultSubjectKind");
        CarriesTier = subject != null && WellKnown.TieredSubjectKinds.Contains(subject);
    }

    public ContentRow Row { get; }

    public string Key => Row.KeyPath;

    public bool RequiresApproval { get; }

    public bool CarriesTier { get; }

    // Active decision types, in row order.
    public static List<DecisionTypeInfo> Active(ConfigurationContent content)
    {
        return RowFacts.Active(content, "decision-type").Select(r => new DecisionTypeInfo(r)).ToList();
    }
}

// Active gate policy rows without a portfolio, by tier, change type and target stage (document 21, section 7.1).
internal static class GatePolicies
{
    public static Dictionary<string, List<ContentRow>> ByCombination(ConfigurationContent content)
    {
        var index = new Dictionary<string, List<ContentRow>>(StringComparer.Ordinal);
        foreach (ContentRow row in RowFacts.Active(content, "gate-policy"))
        {
            if (RowFacts.Has(row, "portfolioId"))
            {
                continue;
            }

            string combination = Combination(RowFacts.Text(row, "tierKey"), RowFacts.Text(row, "changeTypeKey"), RowFacts.Text(row, "targetStageKey"));
            if (!index.TryGetValue(combination, out List<ContentRow>? rows))
            {
                rows = new List<ContentRow>();
                index.Add(combination, rows);
            }

            rows.Add(row);
        }

        return index;
    }

    public static string Combination(string? tier, string? changeType, string? targetStage)
    {
        return (tier ?? string.Empty) + "\u0000" + (changeType ?? string.Empty) + "\u0000" + (targetStage ?? string.Empty);
    }

    public static string Describe(string tier, string changeType, string targetStage)
    {
        return "tier `" + tier + "`, change type `" + changeType + "`, target stage `" + targetStage + "`";
    }
}
