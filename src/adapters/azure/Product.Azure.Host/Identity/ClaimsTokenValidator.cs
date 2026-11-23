using System;
using Product.Engine.Core.Contract;
using Product.Mcp.Core.Identity;

namespace Product.Azure.Host;

/// <summary>
/// The MCP server core's token check, answered from a request the host has already authenticated. ASP.NET Core
/// authentication validated the signature, issuer, audience and lifetime (Microsoft.Identity.Web, or the local
/// development identity); this validator applies the core's remaining rules to the resulting caller and never reads the
/// header text. Its reasons are the same strings as the core's test validator, so the answers match the core's tests.
/// </summary>
public sealed class ClaimsTokenValidator : ITokenValidator
{
    /// <summary>The reason for a request with no credential.</summary>
    public const string MissingReason = "No bearer token was sent.";

    /// <summary>The reason for a credential that did not authenticate.</summary>
    public const string InvalidReason = "The token is not one this server accepts.";

    /// <summary>The reason for a token from another tenant.</summary>
    public const string OtherTenantReason = "The token comes from another tenant.";

    /// <summary>The reason for an app-only token at the MCP endpoint.</summary>
    public const string AppOnlyReason = "An app-only token cannot call the MCP endpoint. Pipelines use the record endpoint.";

    /// <summary>The reason for a token whose groups claim overflowed.</summary>
    public const string GroupsOverageReason = "The token lists too many groups to carry them all. Configure the API app registration to emit only the groups assigned to the application.";

    private readonly CallerClaims? _caller;
    private readonly bool _credentialPresent;
    private readonly string _requiredScope;
    private readonly Func<string, PrincipalKind> _principalKindOf;
    private readonly string _expectedTenantId;

    /// <summary>Creates the validator for one request.</summary>
    /// <param name="caller">The mapped caller, or null when the request did not authenticate.</param>
    /// <param name="authorizationHeaderPresent">True when the request carried a credential (an Authorization header or a development identity header).</param>
    /// <param name="requiredScope">The delegated scope this entry point needs.</param>
    /// <param name="principalKindOf">The engine principal kind of an object ID (User unless a Principal Reference says otherwise).</param>
    /// <param name="expectedTenantId">The client's tenant. Empty skips the tenant check (local runs without Entra only).</param>
    public ClaimsTokenValidator(CallerClaims? caller, bool authorizationHeaderPresent, string requiredScope, Func<string, PrincipalKind> principalKindOf, string expectedTenantId)
    {
        _caller = caller;
        _credentialPresent = authorizationHeaderPresent || caller != null;
        _requiredScope = string.IsNullOrWhiteSpace(requiredScope) ? throw new ArgumentException("A required scope is named.", nameof(requiredScope)) : requiredScope;
        _principalKindOf = principalKindOf ?? throw new ArgumentNullException(nameof(principalKindOf));
        _expectedTenantId = expectedTenantId ?? string.Empty;
    }

    /// <inheritdoc />
    public TokenValidationResult Validate(string? authorizationHeader, DateTime utcNow)
    {
        if (_caller == null)
        {
            return _credentialPresent ? TokenValidationResult.Invalid(InvalidReason) : TokenValidationResult.Missing(MissingReason);
        }

        if (_expectedTenantId.Length > 0 && !string.Equals(_caller.TenantId, _expectedTenantId, StringComparison.OrdinalIgnoreCase))
        {
            return TokenValidationResult.Invalid(OtherTenantReason);
        }

        if (string.IsNullOrEmpty(_caller.ClientAppId))
        {
            return TokenValidationResult.Invalid(InvalidReason);
        }

        if (_caller.IsAppOnly)
        {
            return TokenValidationResult.Forbidden(AppOnlyReason);
        }

        if (!_caller.HasScope(_requiredScope))
        {
            return TokenValidationResult.Forbidden("The token does not carry the " + _requiredScope + " scope.");
        }

        if (_caller.GroupsOverage)
        {
            return TokenValidationResult.Forbidden(GroupsOverageReason);
        }

        return TokenValidationResult.Valid(new ValidatedCaller(
            _caller.ObjectId,
            _caller.TenantId,
            _caller.ClientAppId,
            _principalKindOf(_caller.ObjectId),
            _caller.GroupObjectIds,
            _caller.Scopes,
            isAppOnly: false,
            _caller.DirectoryRoleIds));
    }
}
