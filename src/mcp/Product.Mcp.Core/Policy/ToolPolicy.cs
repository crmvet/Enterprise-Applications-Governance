using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Snapshots;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Json;

namespace Product.Mcp.Core.Policy;

// One Tool Policy row (proposed C41; document 24, section 2.3): which roles may call a tool, and how far an AI client may
// move a record through it. Rows change like any configuration row, through a draft and an accepted decision.
public sealed class ToolPolicyRow
{
    private ToolPolicyRow(
        string toolKey,
        string? toolsetKey,
        bool isEnabled,
        IList<string> allowedRoleKeys,
        bool requiresOwnerRole,
        string? maxAiState,
        IDictionary<string, bool> aiMaySubmit,
        int? callsPerPersonPerHour,
        string? release,
        string? descriptionOverride)
    {
        ToolKey = toolKey;
        ToolsetKey = toolsetKey;
        IsEnabled = isEnabled;
        AllowedRoleKeys = new ReadOnlyCollection<string>(allowedRoleKeys);
        RequiresOwnerRole = requiresOwnerRole;
        MaxAiState = maxAiState;
        AiMaySubmit = new ReadOnlyDictionary<string, bool>(new Dictionary<string, bool>(aiMaySubmit, StringComparer.Ordinal));
        CallsPerPersonPerHour = callsPerPersonPerHour;
        Release = release;
        DescriptionOverride = descriptionOverride;
    }

    public string ToolKey { get; }

    public string? ToolsetKey { get; }

    public bool IsEnabled { get; }

    public IReadOnlyList<string> AllowedRoleKeys { get; }

    public bool RequiresOwnerRole { get; }

    public string? MaxAiState { get; }

    // Per subject kind (intake-request, change-request, decision, configuration-draft, review-request).
    public IReadOnlyDictionary<string, bool> AiMaySubmit { get; }

    // Null means no per-person limit until an adopter sets one (the seed rows leave it out).
    public int? CallsPerPersonPerHour { get; }

    // "r1" or "later".
    public string? Release { get; }

    public string? DescriptionOverride { get; }

    // Reads a row from its columns: a seed row of tool-policy.seed.json or a C41 snapshot row (the same field names).
    public static ToolPolicyRow FromColumns(JsonTreeNode columns)
    {
        string toolKey = Js.Text(columns, "toolKey") ?? throw new ArgumentException("A Tool Policy row names its tool.", nameof(columns));
        var submit = new Dictionary<string, bool>(StringComparer.Ordinal);
        JsonTreeNode? aiMaySubmit = Js.Member(columns, "aiMaySubmit");
        if (aiMaySubmit != null && aiMaySubmit.Kind == JsonTreeKind.Object)
        {
            foreach (KeyValuePair<string, JsonTreeNode> member in aiMaySubmit.Members)
            {
                submit[member.Key] = member.Value.Kind == JsonTreeKind.True;
            }
        }

        long? calls = Js.Number(columns, "callsPerPersonPerHour");
        return new ToolPolicyRow(
            toolKey,
            Js.Text(columns, "toolsetKey"),
            Js.Flag(columns, "isEnabled") ?? false,
            Js.Strings(columns, "allowedRoleKeys").ToList(),
            Js.Flag(columns, "requiresOwnerRole") ?? false,
            Js.Text(columns, "maxAiState"),
            submit,
            calls.HasValue && calls.Value > 0 && calls.Value <= int.MaxValue ? (int)calls.Value : null,
            Js.Text(columns, "release"),
            Js.Text(columns, "descriptionOverride"));
    }
}

// Where the rows came from.
public enum ToolPolicySource
{
    // Active C41 rows of the configuration in force.
    Configuration,

    // No C41 row is in force (C41 is a proposal and no preset ships its rows yet): the shipped seed rows of
    // mcp/contracts/tool-policy.seed.json apply.
    ShippedSeed,
}

// The Tool Policy in force for one request (document 24, section 2.3: "The server reads it on every request").
//
// Judgment call: while the configuration in force holds no active C41 row, the shipped seed rows apply, so the
// server behaves as document 24, section 6.1 says by default. Once any C41 row is in force, only those rows count, and
// a tool without a row is hidden from every caller (it refuses more, never less).
public sealed class ToolPolicySet
{
    // The order AI write states advance in (document 24, section 6.0). The other states (as-reported, link, candidate,
    // withdrawn) are kinds, not steps: a row can only name one the tool's contract has, or read-only.
    private static readonly string[] Progression = { "read-only", "draft", "proposed", "submitted" };

    private readonly Dictionary<string, ToolPolicyRow> _rows;

    private ToolPolicySet(ToolPolicySource source, IEnumerable<ToolPolicyRow> rows)
    {
        Source = source;
        _rows = rows.GroupBy(r => r.ToolKey, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
    }

    public ToolPolicySource Source { get; }

    public IEnumerable<ToolPolicyRow> Rows => _rows.Values.OrderBy(r => r.ToolKey, StringComparer.Ordinal);

    public static ToolPolicySet From(ConfigurationSnapshot? snapshot, ContractSet contracts)
    {
        if (contracts == null)
        {
            throw new ArgumentNullException(nameof(contracts));
        }

        if (snapshot != null && snapshot.TryGetEntity("tool-policy", out SnapshotEntity? entity) && entity.ActiveRows.Any())
        {
            return new ToolPolicySet(ToolPolicySource.Configuration, entity.ActiveRows.Select(r => ToolPolicyRow.FromColumns(r.Columns)));
        }

        return new ToolPolicySet(ToolPolicySource.ShippedSeed, contracts.SeedPolicyRows.Select(ToolPolicyRow.FromColumns));
    }

    public static ToolPolicySet FromRows(ToolPolicySource source, IEnumerable<ToolPolicyRow> rows)
    {
        return new ToolPolicySet(source, rows);
    }

    public bool TryGet(string toolKey, out ToolPolicyRow row)
    {
        if (_rows.TryGetValue(toolKey, out ToolPolicyRow? found))
        {
            row = found;
            return true;
        }

        row = null!;
        return false;
    }

    // True when the tool is served at all: an R1 tool with an enabled row whose release is r1.
    public bool IsServed(ToolContract tool)
    {
        return tool.IsR1 && TryGet(tool.Name, out ToolPolicyRow row) && row.IsEnabled && !string.Equals(row.Release, "later", StringComparison.OrdinalIgnoreCase);
    }

    // True when one of the caller's roles may call the tool. whoami answers every signed-in caller, also one with no
    // product role (its seed row says so).
    public bool Allows(ToolContract tool, IReadOnlyCollection<string> roleKeys)
    {
        if (!IsServed(tool))
        {
            return false;
        }

        if (tool.AnyoneSignedIn)
        {
            return true;
        }

        TryGet(tool.Name, out ToolPolicyRow row);
        return row.AllowedRoleKeys.Any(r => roleKeys.Contains(r, StringComparer.Ordinal));
    }

    // The furthest AI write state the row allows, capped by the contract: configuration can only tighten it (document 24,
    // section 2.3 and section 7, layer 2). A value the contract does not have (other than read-only) cannot widen it, so
    // the contract's own furthest state applies.
    public string EffectiveMaxAiState(ToolContract tool)
    {
        string contractMax = Furthest(tool.AiWriteStates);
        if (!TryGet(tool.Name, out ToolPolicyRow row) || row.MaxAiState == null)
        {
            return contractMax;
        }

        if (string.Equals(row.MaxAiState, "read-only", StringComparison.Ordinal))
        {
            return "read-only";
        }

        return tool.AiWriteStates.Contains(row.MaxAiState, StringComparer.Ordinal) ? row.MaxAiState : contractMax;
    }

    // True when an AI client may leave a record in the state through this tool.
    public bool AiMayReach(ToolContract tool, string state)
    {
        string max = EffectiveMaxAiState(tool);
        if (string.Equals(max, "read-only", StringComparison.Ordinal))
        {
            return false;
        }

        int maxRank = Array.IndexOf(Progression, max);
        int stateRank = Array.IndexOf(Progression, state);
        if (maxRank >= 0 && stateRank >= 0)
        {
            return stateRank <= maxRank;
        }

        // Kinds outside the progression: allowed when the contract has the state and the row did not tighten to a step
        // below draft.
        return tool.AiWriteStates.Contains(state, StringComparer.Ordinal);
    }

    // WORKING DEFAULT (open design question 9): an AI client may submit for a person only where the row says AI May
    // Submit = yes for that subject kind. A subject kind the row does not name is a no.
    public bool AiMaySubmit(ToolContract tool, string subjectKind)
    {
        return TryGet(tool.Name, out ToolPolicyRow row) && row.AiMaySubmit.TryGetValue(subjectKind, out bool allowed) && allowed;
    }

    public string Description(ToolContract tool)
    {
        return TryGet(tool.Name, out ToolPolicyRow row) && row.DescriptionOverride != null ? row.DescriptionOverride : tool.Description;
    }

    private static string Furthest(IReadOnlyList<string> states)
    {
        string? best = null;
        int bestRank = -1;
        foreach (string state in states)
        {
            int rank = Array.IndexOf(Progression, state);
            if (rank > bestRank)
            {
                best = state;
                bestRank = rank;
            }
        }

        return best ?? (states.Count > 0 ? states[states.Count - 1] : "read-only");
    }
}
