using System;

namespace Product.Azure.Host;

/// <summary>
/// Host settings for the product service, bound from the configuration section <c>Product</c>. None of them is product
/// configuration: they say where the service runs and how it talks, never what the product allows.
/// </summary>
public sealed class ProductHostOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Product";

    /// <summary>
    /// The public base URL of the service, such as <c>https://contoso-product.azurewebsites.net</c>. Required outside the
    /// Development environment. The MCP resource URL is this value plus <see cref="McpPath"/>, and it is also the
    /// Application ID URI of the API app registration.
    /// </summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>The path of the MCP endpoint. Default <c>/mcp</c>.</summary>
    public string McpPath { get; set; } = "/mcp";

    /// <summary>The server name the MCP <c>initialize</c> answer reports.</summary>
    public string ServerName { get; set; } = "product-mcp";

    /// <summary>
    /// Where record links point, such as <c>https://host/app/records</c>. Empty means <see cref="PublicBaseUrl"/> plus
    /// <c>/app/records</c>.
    /// </summary>
    public string RecordUrlBase { get; set; } = string.Empty;

    /// <summary>
    /// The secret salt of the caller hash in telemetry. Read from an app setting (a Key Vault reference in Azure), never
    /// from a file in the repository. No default: without it, telemetry carries no caller field. A value shorter than 16
    /// characters fails startup.
    /// </summary>
    public string? TelemetrySalt { get; set; }

    /// <summary>The shipped presets folder, relative to the application base directory.</summary>
    public string PresetsPath { get; set; } = "presets";

    /// <summary>The playbook chapter folder for the <c>playbook://chapter/{n}</c> resource, relative to the base directory.</summary>
    public string PlaybookPath { get; set; } = "content/playbook";

    /// <summary>
    /// Checks every MCP envelope against its output schema before it leaves. On in Development and in tests; a failing
    /// envelope is a server bug and is answered as a JSON-RPC internal error.
    /// </summary>
    public bool? ValidateOutputs { get; set; }

    /// <summary>The largest request body the MCP endpoint and the REST routes read, in bytes. Default 1 MiB.</summary>
    public int MaxRequestBytes { get; set; } = 1048576;

    /// <summary>
    /// Browser origins allowed to call the MCP endpoint (the streamable HTTP Origin check against DNS rebinding). A
    /// request without an Origin header is not a browser request and passes. Loopback origins also pass in Development.
    /// </summary>
    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();

    /// <summary>MCP transport settings, from the section <c>Product:Mcp</c>.</summary>
    public ProductMcpOptions Mcp { get; set; } = new();

    /// <summary>The MCP endpoint's full URL, the resource the protected resource metadata names.</summary>
    public string McpResourceUrl => PublicBaseUrl.TrimEnd('/') + "/" + McpPath.TrimStart('/');

    /// <summary>The record link base after defaults.</summary>
    public string EffectiveRecordUrlBase => string.IsNullOrWhiteSpace(RecordUrlBase) ? PublicBaseUrl.TrimEnd('/') + "/app/records" : RecordUrlBase;
}

/// <summary>Which transport answers the MCP endpoint.</summary>
public enum ProductMcpTransport
{
    /// <summary>
    /// The MCP server core's own JSON-RPC handling: one message per POST, one JSON answer, 405 for GET and DELETE. The
    /// 401 and 403 answers, the tool list and the envelope are exactly what the core's contract tests prove. The default.
    /// </summary>
    Core,

    /// <summary>
    /// The official MCP C# SDK (ModelContextProtocol.AspNetCore) in stateless streamable HTTP mode. The SDK speaks the
    /// protocol (initialize, every protocol revision it supports, server-sent events), and every tools and resources
    /// request is answered by the same MCP server core and in-process backend. Sign-in refusals are still the core's.
    /// </summary>
    Sdk,
}

/// <summary>MCP transport settings, bound from <c>Product:Mcp</c>.</summary>
public sealed class ProductMcpOptions
{
    /// <summary>The transport behind the MCP endpoint. Default <see cref="ProductMcpTransport.Core"/>.</summary>
    public ProductMcpTransport Transport { get; set; } = ProductMcpTransport.Core;
}

/// <summary>
/// Microsoft Entra ID settings of the API, bound from <c>Product:Auth</c>. One API app registration: its client ID is the
/// token audience, its Application ID URI is the MCP resource URL, it exposes the delegated scopes and the app roles.
/// </summary>
public sealed class ProductAuthOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Product:Auth";

    /// <summary>The Entra sign-in instance. Default <c>https://login.microsoftonline.com/</c>.</summary>
    public string Instance { get; set; } = "https://login.microsoftonline.com/";

    /// <summary>The client's own tenant (single tenant). Required outside Development.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>The API app registration's client ID. Required outside Development.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>The token audience. Empty means <see cref="ClientId"/>.</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>The delegated scope MCP clients request.</summary>
    public string McpScope { get; set; } = "mcp.access";

    /// <summary>The delegated scope the product's web app requests.</summary>
    public string ApiScope { get; set; } = "api.access";

    /// <summary>The app role a pipeline's workload identity holds to call the record endpoint.</summary>
    public string RecordAppRole { get; set; } = "Deployment.Record";

    /// <summary>The app role the approval runner holds to write approval outcomes.</summary>
    public string ApprovalAppRole { get; set; } = "Approval.Write";

    /// <summary>The audience after defaults.</summary>
    public string EffectiveAudience => string.IsNullOrWhiteSpace(Audience) ? ClientId : Audience;

    /// <summary>The authorization server the protected resource metadata names: the tenant's v2.0 issuer.</summary>
    public string AuthorizationServer => Instance.TrimEnd('/') + "/" + (string.IsNullOrWhiteSpace(TenantId) ? "organizations" : TenantId) + "/v2.0";

    /// <summary>True when Entra sign-in is configured (tenant and client ID set).</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(TenantId) && !string.IsNullOrWhiteSpace(ClientId);
}

/// <summary>Authentication scheme names of the product.</summary>
public static class ProductAuthenticationDefaults
{
    /// <summary>
    /// The default scheme: a policy scheme that forwards to the first <see cref="IProductSchemeSelector"/> that claims the
    /// request, else to <see cref="EntraScheme"/>.
    /// </summary>
    public const string PolicyScheme = "Product";

    /// <summary>Microsoft Entra ID JWT bearer validation (Microsoft.Identity.Web).</summary>
    public const string EntraScheme = "Bearer";

    /// <summary>
    /// The local server's development identity scheme. The caller mapper honors the development claims only on a
    /// principal this scheme signed in, and only in the Development environment.
    /// </summary>
    public const string DevelopmentScheme = "ProductDevIdentity";
}

/// <summary>Authorization policy names of the REST routes. The MCP endpoint has no policy: the core answers 401 and 403.</summary>
public static class ProductPolicies
{
    /// <summary>A delegated user token with the <c>api.access</c> scope (the web app).</summary>
    public const string ApiCaller = "product.api-caller";

    /// <summary>An app-only token with the <c>Deployment.Record</c> app role (a pipeline's record step).</summary>
    public const string RecordWriter = "product.record-writer";

    /// <summary>An app-only token with the <c>Approval.Write</c> app role (the approval runner).</summary>
    public const string ApprovalWriter = "product.approval-writer";
}
