using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace Product.Azure.Host;

/// <summary>
/// Chooses the authentication scheme for a request before the default (Entra) scheme. The product's policy scheme asks
/// every registered selector in order and forwards to the first that answers. The host library registers none; the local
/// server registers the development identity's selector, which answers only for local Development runs.
/// </summary>
public interface IProductSchemeSelector
{
    /// <summary>The scheme that should authenticate the request, or null to leave it to the next selector or Entra.</summary>
    string? SelectScheme(HttpContext context);
}

/// <summary>The claim types the product reads from an Entra access token (v2), short names as Entra issues them.</summary>
public static class ProductClaimTypes
{
    /// <summary>The caller's Entra object ID.</summary>
    public const string ObjectId = "oid";

    /// <summary>The tenant ID.</summary>
    public const string TenantId = "tid";

    /// <summary>The client app that asked for the token (v2).</summary>
    public const string AuthorizedParty = "azp";

    /// <summary>The client app that asked for the token (v1 tokens).</summary>
    public const string AppId = "appid";

    /// <summary>Delegated scopes, space separated.</summary>
    public const string Scope = "scp";

    /// <summary>App roles.</summary>
    public const string Roles = "roles";

    /// <summary>The caller's group object IDs (filtered to the groups assigned to the application).</summary>
    public const string Groups = "groups";

    /// <summary>Directory role template IDs.</summary>
    public const string DirectoryRoles = "wids";

    /// <summary>The token's identity type: <c>app</c> for an app-only token, <c>user</c> for a delegated one.</summary>
    public const string IdentityType = "idtyp";

    /// <summary>The token subject.</summary>
    public const string Subject = "sub";

    /// <summary>The token version, <c>2.0</c> for v2 tokens.</summary>
    public const string Version = "ver";

    /// <summary>Distributed claim names: holds <c>groups</c> when the groups claim overflowed.</summary>
    public const string ClaimNames = "_claim_names";

    /// <summary>
    /// Marks a principal issued by the local development identity. The caller mapper refuses any principal that carries
    /// it outside the Development environment.
    /// </summary>
    public const string DevIdentity = "product_dev_identity";

    /// <summary>
    /// The engine principal kind a development persona declares (such as <c>agent-user</c>). Honored only for a
    /// development identity in Development; real tokens get their kind from the Principal Reference rows.
    /// </summary>
    public const string DevPrincipalKind = "product_principal_kind";

    // Long names some handlers map the short claims to, read as a fallback.
    internal const string ObjectIdLong = "http://schemas.microsoft.com/identity/claims/objectidentifier";
    internal const string TenantIdLong = "http://schemas.microsoft.com/identity/claims/tenantid";
    internal const string ScopeLong = "http://schemas.microsoft.com/identity/claims/scope";
}

/// <summary>
/// The caller facts the product uses from a validated token or a development persona. Never the token itself.
/// </summary>
public sealed class CallerClaims
{
    /// <summary>Creates the caller facts.</summary>
    public CallerClaims(
        string objectId,
        string tenantId,
        string clientAppId,
        bool isAppOnly,
        IEnumerable<string>? scopes = null,
        IEnumerable<string>? appRoles = null,
        IEnumerable<string>? groupObjectIds = null,
        IEnumerable<string>? directoryRoleIds = null,
        bool groupsOverage = false,
        bool isDevIdentity = false,
        string? declaredPrincipalKind = null,
        string? tokenVersion = null)
    {
        ObjectId = objectId ?? string.Empty;
        TenantId = tenantId ?? string.Empty;
        ClientAppId = clientAppId ?? string.Empty;
        IsAppOnly = isAppOnly;
        Scopes = new ReadOnlyCollection<string>((scopes ?? Enumerable.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal).ToList());
        AppRoles = new ReadOnlyCollection<string>((appRoles ?? Enumerable.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal).ToList());
        GroupObjectIds = new ReadOnlyCollection<string>((groupObjectIds ?? Enumerable.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        DirectoryRoleIds = new ReadOnlyCollection<string>((directoryRoleIds ?? Enumerable.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        GroupsOverage = groupsOverage;
        IsDevIdentity = isDevIdentity;
        DeclaredPrincipalKind = string.IsNullOrWhiteSpace(declaredPrincipalKind) ? null : declaredPrincipalKind;
        TokenVersion = string.IsNullOrWhiteSpace(tokenVersion) ? null : tokenVersion;
    }

    /// <summary>The <c>oid</c> claim.</summary>
    public string ObjectId { get; }

    /// <summary>The <c>tid</c> claim.</summary>
    public string TenantId { get; }

    /// <summary>The <c>azp</c> claim, else <c>appid</c>.</summary>
    public string ClientAppId { get; }

    /// <summary>True for an app-only token: <c>idtyp</c> is <c>app</c>, or no scope and app roles with <c>oid</c> equal to <c>sub</c>.</summary>
    public bool IsAppOnly { get; }

    /// <summary>The <c>scp</c> claim, split.</summary>
    public IReadOnlyList<string> Scopes { get; }

    /// <summary>The <c>roles</c> claim.</summary>
    public IReadOnlyList<string> AppRoles { get; }

    /// <summary>The <c>groups</c> claim.</summary>
    public IReadOnlyList<string> GroupObjectIds { get; }

    /// <summary>The <c>wids</c> claim.</summary>
    public IReadOnlyList<string> DirectoryRoleIds { get; }

    /// <summary>True when the token's groups overflowed (<c>_claim_names</c> names <c>groups</c>).</summary>
    public bool GroupsOverage { get; }

    /// <summary>True for a principal the local development identity issued.</summary>
    public bool IsDevIdentity { get; }

    /// <summary>The principal kind a development persona declares, or null.</summary>
    public string? DeclaredPrincipalKind { get; }

    /// <summary>The <c>ver</c> claim, or null.</summary>
    public string? TokenVersion { get; }

    /// <summary>True when the token carries the scope.</summary>
    public bool HasScope(string scope)
    {
        return Scopes.Contains(scope, StringComparer.Ordinal);
    }

    /// <summary>True when the token carries the app role.</summary>
    public bool HasAppRole(string role)
    {
        return AppRoles.Contains(role, StringComparer.Ordinal);
    }
}

/// <summary>Maps an authenticated principal to the caller facts the product uses.</summary>
public interface ICallerMapper
{
    /// <summary>The caller facts, or null when the principal is not signed in or is not one the product accepts.</summary>
    CallerClaims? Map(ClaimsPrincipal? user);
}

/// <summary>
/// The default mapper: reads the Entra claims (short names, with the long mapped names as a fallback). A principal the
/// development identity issued is refused outside the Development environment, a second lock behind the local server's
/// own startup guard.
/// </summary>
public sealed class CallerMapper : ICallerMapper
{
    private readonly IHostEnvironment _environment;

    /// <summary>Creates the mapper.</summary>
    public CallerMapper(IHostEnvironment environment)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    /// <inheritdoc />
    public CallerClaims? Map(ClaimsPrincipal? user)
    {
        if (user?.Identity == null || !user.Identity.IsAuthenticated)
        {
            return null;
        }

        // Outside Development any principal that carries the development marker is refused. In Development the marker
        // and the declared principal kind count only on a principal the development identity scheme signed in: a token
        // from another scheme that happens to carry those claim names gets nothing from them.
        bool devClaim = string.Equals(First(user, ProductClaimTypes.DevIdentity), "true", StringComparison.OrdinalIgnoreCase);
        if (devClaim && !_environment.IsDevelopment())
        {
            return null;
        }

        bool dev = devClaim && user.Identities.Any(i =>
            i.IsAuthenticated
            && string.Equals(i.AuthenticationType, ProductAuthenticationDefaults.DevelopmentScheme, StringComparison.Ordinal)
            && i.HasClaim(c => c.Type == ProductClaimTypes.DevIdentity && string.Equals(c.Value, "true", StringComparison.OrdinalIgnoreCase)));

        string? objectId = First(user, ProductClaimTypes.ObjectId) ?? First(user, ProductClaimTypes.ObjectIdLong);
        string? tenantId = First(user, ProductClaimTypes.TenantId) ?? First(user, ProductClaimTypes.TenantIdLong);
        if (string.IsNullOrWhiteSpace(objectId) || string.IsNullOrWhiteSpace(tenantId))
        {
            return null;
        }

        string clientApp = First(user, ProductClaimTypes.AuthorizedParty) ?? First(user, ProductClaimTypes.AppId) ?? string.Empty;
        var scopes = All(user, ProductClaimTypes.Scope).Concat(All(user, ProductClaimTypes.ScopeLong))
            .SelectMany(s => s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            .ToList();
        var roles = All(user, ProductClaimTypes.Roles).Concat(All(user, ClaimTypes.Role)).ToList();
        string? identityType = First(user, ProductClaimTypes.IdentityType);
        string? subject = First(user, ProductClaimTypes.Subject) ?? First(user, ClaimTypes.NameIdentifier);
        bool appOnly = string.Equals(identityType, "app", StringComparison.OrdinalIgnoreCase)
            || (identityType == null && scopes.Count == 0 && roles.Count > 0 && string.Equals(objectId, subject, StringComparison.OrdinalIgnoreCase));
        string? claimNames = First(user, ProductClaimTypes.ClaimNames);
        bool overage = claimNames != null && claimNames.IndexOf("\"groups\"", StringComparison.Ordinal) >= 0;

        return new CallerClaims(
            objectId!,
            tenantId!,
            clientApp,
            appOnly,
            scopes,
            roles,
            All(user, ProductClaimTypes.Groups),
            All(user, ProductClaimTypes.DirectoryRoles),
            overage,
            dev,
            dev ? First(user, ProductClaimTypes.DevPrincipalKind) : null,
            First(user, ProductClaimTypes.Version));
    }

    private static string? First(ClaimsPrincipal user, string type)
    {
        string? value = user.FindFirst(type)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static IEnumerable<string> All(ClaimsPrincipal user, string type)
    {
        return user.FindAll(type).Select(c => c.Value).Where(v => !string.IsNullOrWhiteSpace(v));
    }
}
