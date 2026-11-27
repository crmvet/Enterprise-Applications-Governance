using System;
using System.Collections.Generic;
using System.Linq;

namespace Product.Azure.Host;

/// <summary>How the four live directory tools (<c>get_role_holders</c>, <c>run_owner_check</c>,
/// <c>check_role_bindings</c>, <c>run_gap_check</c>) read Microsoft Graph and the Power Platform inventory.</summary>
public enum DirectoryReadMode
{
    /// <summary>No live reads. The four tools answer <c>upstream_unavailable</c>, as before. The default.</summary>
    Off,

    /// <summary>
    /// Live reads on behalf of the signed-in person: the server exchanges the person's token with Microsoft Entra ID for a
    /// Graph or Power Platform API token (on-behalf-of) and calls the service with the new token only.
    /// </summary>
    Live,

    /// <summary>
    /// Development only: recorded, fictional responses from a folder on disk, for local demos with the development
    /// identity. Nothing goes to the network and no token is exchanged. Outside Development, startup fails.
    /// </summary>
    Recorded,

    /// <summary>
    /// The demo personas and their fictional groups, as the persona switch and the development identity trust them: in the
    /// Demo environment (always, when it has personas) or in Development (with the development identity on). Roles and
    /// access lists the holders from them and the binding check finds the groups. The gap check reads the demo's fictional
    /// inventory (<see cref="DemoInventoryReader"/>). No Entra read, no Power Platform read and no token.
    /// </summary>
    Demo,
}

/// <summary>
/// Settings of the live directory reads, bound from <c>Product:Directory</c>. Rosters and the inventory are read live and
/// never stored (D-15). The hosts the reads go to are fixed: the global Microsoft Graph service
/// (<see cref="GraphBaseUrl"/>) and the Power Platform API (<see cref="PowerPlatformApiUrl"/>).
/// </summary>
public sealed class DirectoryOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Product:Directory";

    /// <summary>The Microsoft Graph v1.0 base. Fixed: a bearer token goes only to this host.</summary>
    /// <remarks>https://learn.microsoft.com/graph/use-the-api</remarks>
    public const string GraphBaseUrl = "https://graph.microsoft.com/v1.0/";

    /// <summary>The Power Platform API base. Fixed: a bearer token goes only to this host.</summary>
    /// <remarks>https://learn.microsoft.com/rest/api/power-platform/</remarks>
    public const string PowerPlatformApiUrl = "https://api.powerplatform.com/";

    /// <summary>
    /// The on-behalf-of scope for Microsoft Graph. The on-behalf-of flow takes the resource's <c>.default</c> scope, which
    /// stands for the delegated permissions an admin consented to on the API app registration.
    /// </summary>
    /// <remarks>https://learn.microsoft.com/entra/identity-platform/v2-oauth2-on-behalf-of-flow</remarks>
    public const string GraphScope = "https://graph.microsoft.com/.default";

    /// <summary>The on-behalf-of scope for the Power Platform API (delegated permissions only).</summary>
    /// <remarks>https://learn.microsoft.com/power-platform/admin/programmability-authentication-v2</remarks>
    public const string PowerPlatformScope = "https://api.powerplatform.com/.default";

    /// <summary>The estate key the shipped presets give the Power Platform inventory.</summary>
    public const string PowerPlatformEstate = "power-platform-inventory";

    /// <summary>Off (the default), Live or Recorded (Development only).</summary>
    public DirectoryReadMode Mode { get; set; } = DirectoryReadMode.Off;

    /// <summary>The longest one downstream call may take, in seconds, token exchange included. Default 5.</summary>
    public int CallTimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// The longest all downstream calls of one request (one tool call) may take together, in seconds. Default 25. When it
    /// runs out, the tool answers <c>upstream_unavailable</c> with a plain message instead of holding the request.
    /// </summary>
    public int ToolBudgetSeconds { get; set; } = 25;

    /// <summary>The most Graph pages one read follows. Default 20 (up to 19,980 group members at 999 a page).</summary>
    public int MaxPages { get; set; } = 20;

    /// <summary>The most principals a role binding check looks up one by one to tell their kind. Default 25.</summary>
    public int MaxPrincipalLookups { get; set; } = 25;

    /// <summary>Items per inventory page (the query's <c>Top</c>). Default 500, at most 1000.</summary>
    public int InventoryPageSize { get; set; } = 500;

    /// <summary>
    /// The estate keys the live inventory reader answers for. Empty means only
    /// <see cref="PowerPlatformEstate"/>. Any other estate answers that it has no live reader, and
    /// <c>compare_inventory_list</c> stays the fallback.
    /// </summary>
    public string[] InventoryEstateKeys { get; set; } = Array.Empty<string>();

    /// <summary>The credential the API app proves itself with in the token exchange. Never a client secret.</summary>
    public DirectoryCredentialOptions Credential { get; set; } = new();

    /// <summary>Recorded mode only: the folder that holds the recorded responses (one JSON file per exchange).</summary>
    public string RecordedPath { get; set; } = string.Empty;

    /// <summary>
    /// The demo inventory file of <see cref="DemoInventoryReader"/>: fictional items no application holds yet, which the
    /// demo's inventory adds to the register's own components. Empty means <c>seed/demo-inventory.json</c> when the
    /// deployment carries it, else none. Named outside the Development and the Demo environment, startup fails.
    /// </summary>
    public string DemoInventoryFile { get; set; } = string.Empty;

    /// <summary>The estate keys after defaults.</summary>
    public IReadOnlyList<string> EffectiveInventoryEstateKeys =>
        InventoryEstateKeys.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).ToArray() is { Length: > 0 } keys
            ? keys
            : new[] { PowerPlatformEstate };
}

/// <summary>
/// The client credential of the API app registration for the on-behalf-of exchange, bound from
/// <c>Product:Directory:Credential</c>. Two kinds, both certificateless and secretless: a signed assertion from the app's
/// managed identity (Azure, through a federated identity credential on the API app registration), or a signed assertion
/// read from a file (Kubernetes workload identity, and the tests).
/// </summary>
/// <remarks>https://learn.microsoft.com/entra/msidweb/authentication/credentials-overview</remarks>
public sealed class DirectoryCredentialOptions
{
    /// <summary>The managed identity source type of Microsoft.Identity.Web.</summary>
    public const string ManagedIdentity = "SignedAssertionFromManagedIdentity";

    /// <summary>The file source type of Microsoft.Identity.Web.</summary>
    public const string FilePath = "SignedAssertionFilePath";

    /// <summary><see cref="ManagedIdentity"/> (the default) or <see cref="FilePath"/>.</summary>
    public string SourceType { get; set; } = ManagedIdentity;

    /// <summary>
    /// The client ID of the user-assigned managed identity that the federated identity credential on the API app
    /// registration trusts (issuer <c>https://login.microsoftonline.com/{tenant}/v2.0</c>, subject the identity's principal
    /// ID, audience <c>api://AzureADTokenExchange</c>). Required outside Development: only a user-assigned managed identity
    /// can be an app's credential.
    /// </summary>
    /// <remarks>https://learn.microsoft.com/entra/workload-id/workload-identity-federation-config-app-trust-managed-identity</remarks>
    public string ManagedIdentityClientId { get; set; } = string.Empty;

    /// <summary><see cref="FilePath"/> only: the file that holds the signed assertion.</summary>
    public string SignedAssertionFilePath { get; set; } = string.Empty;
}
