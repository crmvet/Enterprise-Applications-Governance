using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Product.Engine.Core.Contract;

namespace Product.Mcp.Core.Identity;

// The token check of document 24, section 4.1, hop 3: signature, issuer, audience, lifetime, tenant, client and scope.
// The server core depends only on this interface. A host binds a real validator (Entra, with the server's own app
// registration); tests and the local harness bind MockTokenValidator from Product.Mcp.Testing, which no host references.
// The mock reads no real token and calls no network.
// The server never forwards the incoming token and never stores it (document 24, section 4.7).
public interface ITokenValidator
{
    TokenValidationResult Validate(string? authorizationHeader, DateTime utcNow);
}

public enum TokenValidationStatus
{
    // A valid delegated token with the mcp.access scope.
    Valid,

    // No Authorization header, or no bearer token in it. HTTP 401 with the protected resource metadata link.
    Missing,

    // A token that fails a check: signature, issuer, audience, lifetime or tenant. HTTP 401.
    Invalid,

    // A valid token without the server's scope, or an app-only token at the MCP endpoint. HTTP 403.
    Forbidden,
}

// What the validator found. A Valid result carries the caller; the others carry a plain reason, never the token.
public sealed class TokenValidationResult
{
    private TokenValidationResult(TokenValidationStatus status, ValidatedCaller? caller, string reason)
    {
        Status = status;
        Caller = caller;
        Reason = reason;
    }

    public TokenValidationStatus Status { get; }

    public ValidatedCaller? Caller { get; }

    public string Reason { get; }

    public static TokenValidationResult Valid(ValidatedCaller caller)
    {
        return new TokenValidationResult(TokenValidationStatus.Valid, caller ?? throw new ArgumentNullException(nameof(caller)), "valid");
    }

    public static TokenValidationResult Missing(string reason)
    {
        return new TokenValidationResult(TokenValidationStatus.Missing, null, reason);
    }

    public static TokenValidationResult Invalid(string reason)
    {
        return new TokenValidationResult(TokenValidationStatus.Invalid, null, reason);
    }

    public static TokenValidationResult Forbidden(string reason)
    {
        return new TokenValidationResult(TokenValidationStatus.Forbidden, null, reason);
    }
}

// The claims the server uses from a validated token. Never the token itself.
public sealed class ValidatedCaller
{
    public ValidatedCaller(
        string objectId,
        string tenantId,
        string clientAppId,
        PrincipalKind principalKind,
        IEnumerable<string>? groupObjectIds = null,
        IEnumerable<string>? scopes = null,
        bool isAppOnly = false,
        IEnumerable<string>? directoryRoleIds = null)
    {
        if (string.IsNullOrEmpty(objectId))
        {
            throw new ArgumentException("A validated caller has an object ID (oid).", nameof(objectId));
        }

        if (string.IsNullOrEmpty(tenantId))
        {
            throw new ArgumentException("A validated caller has a tenant ID (tid).", nameof(tenantId));
        }

        if (string.IsNullOrEmpty(clientAppId))
        {
            throw new ArgumentException("A validated caller has a client app ID (azp).", nameof(clientAppId));
        }

        ObjectId = objectId;
        TenantId = tenantId;
        ClientAppId = clientAppId;
        PrincipalKind = principalKind;
        GroupObjectIds = new ReadOnlyCollection<string>((groupObjectIds ?? Enumerable.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        Scopes = new ReadOnlyCollection<string>((scopes ?? Enumerable.Empty<string>()).ToList());
        IsAppOnly = isAppOnly;
        DirectoryRoleIds = new ReadOnlyCollection<string>((directoryRoleIds ?? Enumerable.Empty<string>()).ToList());
    }

    // The oid claim: the person's Entra object ID.
    public string ObjectId { get; }

    // The tid claim.
    public string TenantId { get; }

    // The azp claim: the client app that asked for the token (a registered client, or the app ID Entra derives for a
    // Client ID Metadata Document client).
    public string ClientAppId { get; }

    // User for a person. An agent user or agent identity token resolves to an actor that holds no role (RA-06).
    public PrincipalKind PrincipalKind { get; }

    // The groups the person is in (the groups claim, or a lookup when the claim overflows).
    public IReadOnlyList<string> GroupObjectIds { get; }

    // The scp claim.
    public IReadOnlyList<string> Scopes { get; }

    // True for an app-only token. Pipelines and the approval runner never use MCP (document 24, section 4.6).
    public bool IsAppOnly { get; }

    // The wids claim when present: the admin's directory role template IDs (document 24, tool 35, Admin Role Used).
    public IReadOnlyList<string> DirectoryRoleIds { get; }
}

// The server's own identity facts, against which a token is checked.
public sealed class TokenAudience
{
    public TokenAudience(string audience, string tenantId, string scope = "mcp.access")
    {
        Audience = audience ?? throw new ArgumentNullException(nameof(audience));
        TenantId = tenantId ?? throw new ArgumentNullException(nameof(tenantId));
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
    }

    // The server app's client ID: the aud claim (document 24, section 4.1).
    public string Audience { get; }

    // The adopter's own tenant only (document 24, section 4.3).
    public string TenantId { get; }

    // The only delegated scope MCP clients request.
    public string Scope { get; }
}
