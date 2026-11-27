using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;
using Product.Engine.Core.Snapshots;

namespace Product.Engine.Core.Operations;

// A rule group: the active rows of one entity with the same decision type and the same scope values
// (document 21, section 10.3; document 25, OP-02 rule 2). Specificity is the number of scope columns it sets.
internal sealed class ScopeGroup
{
    public ScopeGroup(IList<KeyValuePair<string, string>> scope)
    {
        Scope = new ReadOnlyCollection<KeyValuePair<string, string>>(scope);
        Columns = string.Join(",", scope.Select(pair => pair.Key));
    }

    // The scope columns that are set, in field order.
    public IReadOnlyList<KeyValuePair<string, string>> Scope { get; }

    // The names of the columns set, such as "tierKey,changeTypeKey". Two groups that differ only in portfolio share it.
    public string Columns { get; }

    public int Specificity => Scope.Count;

    public List<SnapshotRow> Rows { get; } = new();

    public string? Value(string column)
    {
        foreach (KeyValuePair<string, string> pair in Scope)
        {
            if (string.Equals(pair.Key, column, StringComparison.Ordinal))
            {
                return pair.Value;
            }
        }

        return null;
    }

    public string Describe()
    {
        return Scope.Count == 0 ? "no scope" : string.Join(", ", Scope.Select(pair => pair.Key + " " + SnapshotRead.Quote(pair.Value)));
    }
}

// The result of resolving rule groups for one request.
internal sealed class ScopeResolution
{
    public ScopeResolution(ScopeGroup? winner, IReadOnlyList<ScopeGroup> dropped)
    {
        Winner = winner;
        Dropped = dropped;
    }

    // The single most specific matching group. Null when no group matches.
    public ScopeGroup? Winner { get; }

    // Matching groups that lost to the winner. They never add rows (document 25, OP-02 rule 4).
    public IReadOnlyList<ScopeGroup> Dropped { get; }
}

// Resolves the most specific rule group (document 25, OP-02 rules 2 to 6; working default, 28 issue 2 and Q2).
// The same rule serves Approval Rule (C32) and Decision Right (C31).
internal static class ScopeGroups
{
    public const string PortfolioColumn = "portfolioId";

    // Optional scope columns of C32 and C31 (document 21, section 3.5).
    // The data taxonomy adds five (docs/architecture/data-taxonomy.md, Approval rules): subjectKind, classificationKey, aiUse (a boolean, matched as "true" or
    // "false"), dataDomainLevelKey and dataTaxonomyChange. They count for specificity like the others.
    public static readonly string[] ApprovalRuleScope =
    {
        "tierKey", "lifecycleStageKey", "changeTypeKey", "applicationKindKey", "targetStageKey",
        "subjectKind", "classificationKey", "aiUse", "dataDomainLevelKey", "dataTaxonomyChange", PortfolioColumn,
    };

    public static readonly string[] DecisionRightScope =
    {
        "tierKey", "lifecycleStageKey", "changeTypeKey", "applicationKindKey",
        "subjectKind", "classificationKey", "aiUse", "dataDomainLevelKey", "dataTaxonomyChange", PortfolioColumn,
    };

    // Groups the active rows of one decision type by scope, in the order the groups first appear (row key order).
    public static List<ScopeGroup> Build(ConfigurationSnapshot snapshot, string entityKey, string decisionTypeKey, string[] scopeColumns)
    {
        var groups = new List<ScopeGroup>();
        var byIdentity = new Dictionary<string, ScopeGroup>(StringComparer.Ordinal);
        foreach (SnapshotRow row in SnapshotRead.ActiveRows(snapshot, entityKey))
        {
            if (!string.Equals(SnapshotRead.Text(row, "decisionTypeKey"), decisionTypeKey, StringComparison.Ordinal))
            {
                continue;
            }

            var scope = new List<KeyValuePair<string, string>>();
            foreach (string column in scopeColumns)
            {
                string? value = ScopeValue(row, column);
                if (value != null)
                {
                    scope.Add(new KeyValuePair<string, string>(column, value));
                }
            }

            string identity = string.Join("\u0000", scope.Select(pair => pair.Key + "=" + pair.Value));
            if (!byIdentity.TryGetValue(identity, out ScopeGroup? group))
            {
                group = new ScopeGroup(scope);
                byIdentity.Add(identity, group);
                groups.Add(group);
            }

            group.Rows.Add(row);
        }

        return groups;
    }

    // A scope column's value as text. A boolean scope (aiUse) reads as "true" or "false".
    public static string? ScopeValue(SnapshotRow row, string column)
    {
        if (!row.TryGetColumn(column, out JsonTreeNode? value))
        {
            return null;
        }

        switch (value.Kind)
        {
            case JsonTreeKind.String:
                return value.GetString();
            case JsonTreeKind.True:
                return "true";
            case JsonTreeKind.False:
                return "false";
            default:
                return null;
        }
    }

    // Finds the single most specific group whose set scope columns all match the request.
    // A portfolio scope matches when the portfolio is in the path. Between groups that set the same columns and
    // differ only in portfolio, the nearer portfolio wins. Any other tie is a broken configuration: the engine has
    // no tie-break, so it refuses with rule-group.ambiguous (rule 6; V26 refuses it before activation).
    public static ScopeResolution Resolve(
        IReadOnlyList<ScopeGroup> groups,
        IDictionary<string, string?> request,
        IReadOnlyList<string> portfolioPath,
        string entityKey,
        string decisionTypeKey)
    {
        List<ScopeGroup> matching = groups.Where(group => Matches(group, request, portfolioPath)).ToList();
        if (matching.Count == 0)
        {
            return new ScopeResolution(null, Array.Empty<ScopeGroup>());
        }

        int top = matching.Max(group => group.Specificity);
        List<ScopeGroup> candidates = matching.Where(group => group.Specificity == top).ToList();
        ScopeGroup winner = candidates[0];
        if (candidates.Count > 1)
        {
            bool onlyPortfolioDiffers = candidates.All(group => string.Equals(group.Columns, candidates[0].Columns, StringComparison.Ordinal))
                && candidates[0].Value(PortfolioColumn) != null;
            if (!onlyPortfolioDiffers)
            {
                string entity = SnapshotRead.LogicalName(entityKey);
                throw OperationStop.Broken(
                    ReasonCodes.RuleGroupAmbiguous,
                    entity + " groups of decision type " + SnapshotRead.Quote(decisionTypeKey) + " with " + candidates[0].Describe() + " and with "
                    + candidates[1].Describe() + " both match with the same specificity, so no single group wins. The configuration is broken: add a group scoped to both.",
                    entity,
                    candidates[1].Rows[0].KeyPath);
            }

            winner = candidates.OrderBy(group => IndexOf(portfolioPath, group.Value(PortfolioColumn)!)).First();
        }

        return new ScopeResolution(winner, matching.Where(group => !ReferenceEquals(group, winner)).ToList());
    }

    private static bool Matches(ScopeGroup group, IDictionary<string, string?> request, IReadOnlyList<string> portfolioPath)
    {
        foreach (KeyValuePair<string, string> pair in group.Scope)
        {
            if (string.Equals(pair.Key, PortfolioColumn, StringComparison.Ordinal))
            {
                if (IndexOf(portfolioPath, pair.Value) < 0)
                {
                    return false;
                }

                continue;
            }

            if (!request.TryGetValue(pair.Key, out string? value) || !string.Equals(value, pair.Value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static int IndexOf(IReadOnlyList<string> path, string value)
    {
        for (int i = 0; i < path.Count; i++)
        {
            if (string.Equals(path[i], value, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    // Trace rows for a resolution: the winning group's rows won, the dropped groups' rows dropped.
    public static IEnumerable<RuleTrace> Trace(ScopeResolution resolution, string entityKey)
    {
        string entity = SnapshotRead.LogicalName(entityKey);
        if (resolution.Winner != null)
        {
            foreach (SnapshotRow row in resolution.Winner.Rows)
            {
                yield return new RuleTrace(entity, row.KeyPath, RuleTraceRole.Won);
            }
        }

        foreach (ScopeGroup group in resolution.Dropped)
        {
            foreach (SnapshotRow row in group.Rows)
            {
                yield return new RuleTrace(entity, row.KeyPath, RuleTraceRole.Dropped);
            }
        }
    }
}
