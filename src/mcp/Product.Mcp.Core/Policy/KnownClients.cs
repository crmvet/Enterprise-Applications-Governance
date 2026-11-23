using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Snapshots;
using Product.Mcp.Core.Json;

namespace Product.Mcp.Core.Policy;

// The Known Client check (proposed C45; document 26, sections 10.3 and 15): a list of the client apps that may call the
// product, with a label for each, so the Client App column and the audit trail name the client.
//
// Judgment calls:
// - While the configuration in force holds no active C45 row (C45 is a proposal and no preset ships one), the check does
//   not apply: the tenant's controls (assignment required on the server's app, consent for CIMD clients) still do.
//   The client is then labeled by its app ID.
// - Once any C45 row is active, the list is an allow list: a client whose app ID or CIMD URL matches no active row, or
//   matches a row with Is Allowed = No, is refused with not_authorized.
// - On the MCP endpoint every caller is recorded as an AI client for a person, whatever the row's Is AI Client says: the
//   actor kind comes from the entry point and the token type (document 24, section 4.5). A row can never turn an MCP call
//   into a person's own act.
public sealed class KnownClientCheck
{
    private readonly List<KnownClientRow> _rows;

    private KnownClientCheck(List<KnownClientRow> rows)
    {
        _rows = rows;
    }

    // True when at least one active C45 row is in force, so the allow list applies.
    public bool IsInForce => _rows.Count > 0;

    public static KnownClientCheck From(ConfigurationSnapshot? snapshot)
    {
        var rows = new List<KnownClientRow>();
        if (snapshot != null && snapshot.TryGetEntity("known-client", out SnapshotEntity? entity))
        {
            foreach (SnapshotRow row in entity.ActiveRows)
            {
                string? identifier = Js.Text(row.Columns, "clientIdentifier");
                if (identifier != null)
                {
                    rows.Add(new KnownClientRow(row.KeyPath, identifier, Js.Text(row.Columns, "label") ?? identifier, Js.Flag(row.Columns, "isAiClient") ?? true, Js.Flag(row.Columns, "isAllowed") ?? false));
                }
            }
        }

        return new KnownClientCheck(rows);
    }

    public KnownClientResult Check(string clientAppId)
    {
        if (string.IsNullOrEmpty(clientAppId))
        {
            return KnownClientResult.Refused("The token names no client app, so the client cannot be checked.");
        }

        if (!IsInForce)
        {
            return KnownClientResult.Allowed(null, clientAppId);
        }

        KnownClientRow? match = _rows.FirstOrDefault(r => string.Equals(r.ClientIdentifier, clientAppId, StringComparison.OrdinalIgnoreCase));
        if (match == null)
        {
            return KnownClientResult.Refused("The client app is not on the Known Client list of the configuration in force. A configuration admin adds it through a configuration change.");
        }

        return match.IsAllowed
            ? KnownClientResult.Allowed(match.Key, match.Label)
            : KnownClientResult.Refused("The client app " + match.Label + " is on the Known Client list with Is Allowed = No.");
    }

    private sealed class KnownClientRow
    {
        public KnownClientRow(string key, string clientIdentifier, string label, bool isAiClient, bool isAllowed)
        {
            Key = key;
            ClientIdentifier = clientIdentifier;
            Label = label;
            IsAiClient = isAiClient;
            IsAllowed = isAllowed;
        }

        public string Key { get; }

        public string ClientIdentifier { get; }

        public string Label { get; }

        public bool IsAiClient { get; }

        public bool IsAllowed { get; }
    }
}

public sealed class KnownClientResult
{
    private KnownClientResult(bool isAllowed, string? rowKey, string label, string? refusal)
    {
        IsAllowed = isAllowed;
        RowKey = rowKey;
        Label = label;
        Refusal = refusal;
    }

    public bool IsAllowed { get; }

    // The C45 row that allowed the client. Null when the list is not in force.
    public string? RowKey { get; }

    // The client's label for the Client App column.
    public string Label { get; }

    public string? Refusal { get; }

    public static KnownClientResult Allowed(string? rowKey, string label)
    {
        return new KnownClientResult(true, rowKey, label, null);
    }

    public static KnownClientResult Refused(string reason)
    {
        return new KnownClientResult(false, null, string.Empty, reason);
    }
}

// The X-Toolsets request header (document 24, section 2.1): a client asks for only the toolsets it needs, such as
// "portfolio,documentation". Without the header the caller gets every toolset its roles allow. Keys are matched exactly
// (they are lowercase configuration keys); an unknown key is refused, so a typo never silently widens or empties the list.
public sealed class ToolsetSelection
{
    public const string HeaderName = "X-Toolsets";

    private readonly HashSet<string>? _keys;

    private ToolsetSelection(HashSet<string>? keys, string? problem)
    {
        _keys = keys;
        Problem = problem;
    }

    // Every toolset (no header).
    public static ToolsetSelection All { get; } = new(null, null);

    public bool IsAll => _keys == null;

    // A plain sentence when the header is malformed or names an unknown toolset.
    public string? Problem { get; }

    public IReadOnlyCollection<string> Keys => _keys == null ? Array.Empty<string>() : _keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

    public static ToolsetSelection Parse(string? header, Func<string, bool> isToolset)
    {
        if (isToolset == null)
        {
            throw new ArgumentNullException(nameof(isToolset));
        }

        if (header == null)
        {
            return All;
        }

        string[] parts = header.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToArray();
        if (parts.Length == 0)
        {
            return new ToolsetSelection(new HashSet<string>(StringComparer.Ordinal), HeaderName + " is empty. Leave the header out to get every toolset your roles allow.");
        }

        string[] unknown = parts.Where(p => !isToolset(p)).ToArray();
        if (unknown.Length > 0)
        {
            return new ToolsetSelection(new HashSet<string>(StringComparer.Ordinal), HeaderName + " names an unknown toolset: " + string.Join(", ", unknown) + ".");
        }

        return new ToolsetSelection(new HashSet<string>(parts, StringComparer.Ordinal), null);
    }

    public bool Includes(string toolsetKey)
    {
        return _keys == null || _keys.Contains(toolsetKey);
    }
}
