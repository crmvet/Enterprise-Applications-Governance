using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Product.Engine.Core.Contract;
using Product.Mcp.Core.Backend;

namespace Product.Azure.Host;

/// <summary>
/// The Power Platform inventory as the signed-in admin, behind the MCP core's <see cref="IInventoryReader"/> port:
/// <c>POST https://api.powerplatform.com/resourcequery/resources/query?api-version=2024-10-01</c> on the
/// <c>PowerPlatformResources</c> table, one page per call, with the service's <c>skipToken</c> carried in the tool's own
/// skip token. The query takes delegated user tokens only: Microsoft answers 403 to a service principal or a managed
/// identity, so this reader always acts for the person. What the person sees follows their Microsoft Entra role: Global
/// Administrator, Power Platform Administrator, Dynamics 365 Administrator and Global Reader see everything; AI
/// Administrator and AI Reader see only agents, agentic apps, agent flows and environments. The API app registration needs
/// the delegated Power Platform API permission ResourceQuery.Resources.Read ("Query resources") with admin consent.
/// </summary>
/// <remarks>
/// https://learn.microsoft.com/power-platform/admin/inventory-api,
/// https://learn.microsoft.com/power-platform/admin/inventory-schema,
/// https://learn.microsoft.com/power-platform/admin/power-platform-inventory and
/// https://learn.microsoft.com/power-platform/admin/programmability-permission-reference
/// </remarks>
public sealed class PowerPlatformInventoryReader : IInventoryReader
{
    private const string QueryUrl = DirectoryOptions.PowerPlatformApiUrl + "resourcequery/resources/query?api-version=2024-10-01";

    // The resource types the gap check compares, with the component kind each maps to (the shipped component kinds).
    // Connectors, environments and environment groups are not components and are left out of the query.
    private static readonly IReadOnlyDictionary<string, string?> Types = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
    {
        ["microsoft.powerapps/canvasapps"] = "canvas-app",
        ["microsoft.powerapps/modeldrivenapps"] = "model-driven-app",
        ["microsoft.powerapps/codeapps"] = "code-app",
        ["microsoft.powerapps/apps"] = null,
        ["microsoft.powerautomate/cloudflows"] = "cloud-flow",
        ["microsoft.powerautomate/agentflows"] = null,
        ["microsoft.powerautomate/m365agentflows"] = null,
        ["microsoft.copilotstudio/agents"] = "copilot-studio-agent",
        ["microsoft.powerpages/sites"] = "power-pages-site",
    };

    // The Microsoft Entra roles that can query the inventory, by role template ID (the wids claim), in the order the
    // admin role used is reported. https://learn.microsoft.com/entra/identity/role-based-access-control/permissions-reference
    private static readonly (string Id, string Name)[] InventoryRoles =
    {
        ("62e90394-69f5-4237-9190-012177145e10", "Global administrator"),
        ("11648597-926c-4cf3-9c36-bcebb0ba8dcc", "Power Platform administrator"),
        ("44367163-eba1-44c3-98af-f5787879f96a", "Dynamics 365 administrator"),
        ("f2ef992c-3afb-46b9-b7cf-a126ee74c451", "Global reader"),
        ("d2562ede-74db-457e-a7b6-544e236ebb61", "AI administrator"),
        ("1fe13547-53f6-408d-ac04-7f8eed167b38", "AI reader"),
    };

    // An environment ID goes into the query as a quoted literal, so only letters, digits and hyphens are accepted
    // (a GUID, or Default- and the tenant ID).
    private static readonly Regex EnvironmentId = new("^[A-Za-z0-9-]{1,100}$", RegexOptions.CultureInvariant);

    private readonly DirectoryHttp _http;
    private readonly IOptions<DirectoryOptions> _options;

    /// <summary>Creates the reader.</summary>
    public PowerPlatformInventoryReader(DirectoryHttp http, IOptions<DirectoryOptions> options)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public DirectoryAnswer<InventoryPage> Query(BackendCaller asAdmin, string estateKey, IReadOnlyList<string>? environmentIds, string? skipToken)
    {
        if (asAdmin == null)
        {
            throw new ArgumentNullException(nameof(asAdmin));
        }

        if (asAdmin.PrincipalKind != PrincipalKind.User)
        {
            return DirectoryAnswer<InventoryPage>.Unavailable("Only a person reads the inventory through the product. Agent identities, agent users and workloads never do.");
        }

        if (!_options.Value.EffectiveInventoryEstateKeys.Contains(estateKey ?? string.Empty, StringComparer.Ordinal))
        {
            return DirectoryAnswer<InventoryPage>.Unavailable("The estate " + estateKey + " has no live inventory reader on this server. Use compare_inventory_list with a list from your own tool.");
        }

        var environments = (environmentIds ?? Array.Empty<string>()).Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        string? invalid = environments.FirstOrDefault(e => !EnvironmentId.IsMatch(e));
        if (invalid != null)
        {
            return DirectoryAnswer<InventoryPage>.Unavailable("The environment ID " + (invalid.Length > 100 ? invalid.Substring(0, 100) : invalid) + " is not a Power Platform environment ID, so the inventory was not queried.");
        }

        if (skipToken != null && skipToken.Length > 4096)
        {
            return DirectoryAnswer<InventoryPage>.Unavailable("The inventory skip token is too long to be one the inventory returned.");
        }

        string? adminRole = AdminRoleUsed(asAdmin);
        DirectoryReply reply = _http.Post(DownstreamApi.PowerPlatform, asAdmin, QueryUrl, Body(environments, skipToken, _options.Value.InventoryPageSize).ToJsonString());
        if (!reply.Answered)
        {
            return DirectoryAnswer<InventoryPage>.Unavailable(reply.Problem!);
        }

        if (reply.Status == 403)
        {
            return DirectoryAnswer<InventoryPage>.Unavailable(
                "The Power Platform inventory refused the query (403). It answers only a signed-in admin with one of these Microsoft Entra roles: "
                + "Global Administrator, Power Platform Administrator, Dynamics 365 Administrator or Global Reader (AI Administrator and AI Reader see only agents and agent flows). "
                + (adminRole == null ? "Your token names none of them. " : "Your token names " + adminRole + ". ")
                + "The product's API app also needs the delegated Power Platform API permission ResourceQuery.Resources.Read with admin consent.");
        }

        if (reply.Status == 401)
        {
            return DirectoryAnswer<InventoryPage>.Unavailable("The Power Platform API did not accept the exchanged token" + Code(reply) + ".");
        }

        if (!reply.Ok || reply.Body is not JsonObject body)
        {
            return DirectoryAnswer<InventoryPage>.Unavailable("The Power Platform inventory answered HTTP " + reply.Status + Code(reply) + ".");
        }

        var items = new List<InventoryItem>();
        if (body["data"] is JsonArray rows)
        {
            foreach (JsonObject row in rows.OfType<JsonObject>())
            {
                InventoryItem? item = ItemOf(row);
                if (item != null)
                {
                    items.Add(item);
                }
            }
        }

        return DirectoryAnswer<InventoryPage>.Of(new InventoryPage(items, DirectoryJson.Text(body, "skipToken"), adminRole));
    }

    // The directory role the admin used, from the token's wids claim (document 24, tool 35). Null when it names none.
    internal static string? AdminRoleUsed(BackendCaller caller)
    {
        foreach ((string id, string name) in InventoryRoles)
        {
            if (caller.DirectoryRoleIds.Contains(id, StringComparer.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        return null;
    }

    // The query: the component resource types, the environment's name joined in (the Power Platform admin center's own
    // pattern), an optional environment filter, a stable order for paging, and only the columns the gap check uses.
    internal static JsonObject Body(IReadOnlyList<string> environmentIds, string? skipToken, int pageSize)
    {
        var clauses = new JsonArray
        {
            new JsonObject { ["$type"] = "extend", ["FieldName"] = "joinKey", ["Expression"] = "tolower(tostring(properties.environmentId))" },
            new JsonObject
            {
                ["$type"] = "join",
                ["JoinKind"] = "leftouter",
                ["RightTable"] = new JsonObject
                {
                    ["TableName"] = "PowerPlatformResources",
                    ["Clauses"] = new JsonArray
                    {
                        new JsonObject { ["$type"] = "where", ["FieldName"] = "type", ["Operator"] = "==", ["Values"] = new JsonArray("'microsoft.powerplatform/environments'") },
                        new JsonObject { ["$type"] = "project", ["FieldList"] = new JsonArray("joinKey = tolower(name)", "environmentName = properties.displayName") },
                    },
                },
                ["LeftColumnName"] = "joinKey",
                ["RightColumnName"] = "joinKey",
            },
            new JsonObject { ["$type"] = "where", ["FieldName"] = "type", ["Operator"] = "in~", ["Values"] = new JsonArray(Types.Keys.OrderBy(t => t, StringComparer.Ordinal).Select(t => (JsonNode)JsonValue.Create("'" + t + "'")).ToArray()) },
        };
        if (environmentIds.Count > 0)
        {
            clauses.Add(new JsonObject { ["$type"] = "where", ["FieldName"] = "joinKey", ["Operator"] = "in~", ["Values"] = new JsonArray(environmentIds.Select(e => (JsonNode)JsonValue.Create("'" + e.ToLowerInvariant() + "'")).ToArray()) });
        }

        clauses.Add(new JsonObject
        {
            ["$type"] = "project",
            ["FieldList"] = new JsonArray(
                "name",
                "type",
                "displayName = tostring(properties.displayName)",
                "environmentId = tostring(properties.environmentId)",
                "environmentName = tostring(environmentName)",
                "ownerId = tostring(properties.ownerId)",
                "createdBy = tostring(properties.createdBy)",
                "subType = tostring(properties.subType)"),
        });
        clauses.Add(new JsonObject { ["$type"] = "orderby", ["FieldNamesAscDesc"] = new JsonObject { ["name"] = "asc" } });

        return new JsonObject
        {
            ["TableName"] = "PowerPlatformResources",
            ["Clauses"] = clauses,
            ["Options"] = new JsonObject { ["Top"] = pageSize, ["Skip"] = 0, ["SkipToken"] = skipToken ?? string.Empty },
        };
    }

    // One inventory row as an item the gap check compares. Managed apps built in Copilot managed runtime are the apps
    // type with the microsoftApp subtype.
    internal static InventoryItem? ItemOf(JsonObject row)
    {
        string? id = DirectoryJson.Text(row, "name");
        string? type = DirectoryJson.Text(row, "type");
        if (id == null || type == null)
        {
            return null;
        }

        Types.TryGetValue(type, out string? kind);
        if (kind == null && string.Equals(type, "microsoft.powerapps/apps", StringComparison.OrdinalIgnoreCase)
            && string.Equals(DirectoryJson.Text(row, "subType"), "microsoftApp", StringComparison.OrdinalIgnoreCase))
        {
            kind = "copilot-managed-runtime-app";
        }

        string? owner = DirectoryJson.Text(row, "ownerId");
        string? claimant = DirectoryJson.IsGuid(owner) ? owner : DirectoryJson.IsGuid(DirectoryJson.Text(row, "createdBy")) ? DirectoryJson.Text(row, "createdBy") : null;
        string? target = DirectoryJson.Text(row, "environmentName") ?? DirectoryJson.Text(row, "environmentId");
        return new InventoryItem(
            Cut(id, 400),
            type.ToLowerInvariant(),
            Cut(DirectoryJson.Text(row, "displayName") ?? id, 200),
            target == null ? null : Cut(target, 200),
            kind,
            claimant);
    }

    private static string Cut(string text, int length)
    {
        return text.Length <= length ? text : text.Substring(0, length);
    }

    private static string Code(DirectoryReply reply)
    {
        return reply.ErrorCode == null ? string.Empty : " (" + reply.ErrorCode + ")";
    }
}
