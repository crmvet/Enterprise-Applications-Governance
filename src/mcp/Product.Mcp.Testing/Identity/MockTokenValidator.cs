using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Core.Contract;
using Product.Mcp.Core.Identity;

namespace Product.Mcp.Testing;

// A mock identity for tests and the local harness: an opaque bearer value such as "mock-person-1" maps to claims
// registered in advance. It parses no JWT, checks no signature and calls no network, so it can never accept a real token.
// It still runs the checks a real validator runs on the claims (audience, tenant, lifetime, scope, app-only), so the
// server's refusals can be tested. It lives in the testing assembly, which no host references, so a production build
// cannot bind it (DependencyRuleTests in the MCP core tests).
public sealed class MockTokenValidator : ITokenValidator
{
    private const string BearerPrefix = "Bearer ";

    private readonly TokenAudience _audience;
    private readonly Dictionary<string, MockToken> _tokens = new(StringComparer.Ordinal);

    public MockTokenValidator(TokenAudience audience)
    {
        _audience = audience ?? throw new ArgumentNullException(nameof(audience));
    }

    // Registers a mock token. Values must start with "mock-", so a real token can never match one.
    public MockTokenValidator Add(string value, MockToken token)
    {
        if (string.IsNullOrEmpty(value) || !value.StartsWith("mock-", StringComparison.Ordinal))
        {
            throw new ArgumentException("A mock token value starts with \"mock-\".", nameof(value));
        }

        _tokens[value] = token ?? throw new ArgumentNullException(nameof(token));
        return this;
    }

    public TokenValidationResult Validate(string? authorizationHeader, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader) || !authorizationHeader!.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return TokenValidationResult.Missing("No bearer token was sent.");
        }

        string value = authorizationHeader.Substring(BearerPrefix.Length).Trim();
        if (!_tokens.TryGetValue(value, out MockToken? token))
        {
            return TokenValidationResult.Invalid("The token is not one this server accepts.");
        }

        if (!string.Equals(token.Audience, _audience.Audience, StringComparison.OrdinalIgnoreCase))
        {
            return TokenValidationResult.Invalid("The token was issued for another audience.");
        }

        if (!string.Equals(token.TenantId, _audience.TenantId, StringComparison.OrdinalIgnoreCase))
        {
            return TokenValidationResult.Invalid("The token comes from another tenant.");
        }

        if (utcNow >= token.ExpiresOnUtc)
        {
            return TokenValidationResult.Invalid("The token has expired.");
        }

        if (token.IsAppOnly)
        {
            return TokenValidationResult.Forbidden("An app-only token cannot call the MCP endpoint. Pipelines use the record endpoint.");
        }

        if (!token.Scopes.Contains(_audience.Scope, StringComparer.Ordinal))
        {
            return TokenValidationResult.Forbidden("The token does not carry the " + _audience.Scope + " scope.");
        }

        return TokenValidationResult.Valid(new ValidatedCaller(
            token.ObjectId,
            token.TenantId,
            token.ClientAppId,
            token.PrincipalKind,
            token.GroupObjectIds,
            token.Scopes,
            token.IsAppOnly,
            token.DirectoryRoleIds));
    }
}

// The claims of one mock token.
public sealed class MockToken
{
    public MockToken(string objectId, string audience, string tenantId, string clientAppId, DateTime expiresOnUtc)
    {
        ObjectId = objectId;
        Audience = audience;
        TenantId = tenantId;
        ClientAppId = clientAppId;
        ExpiresOnUtc = expiresOnUtc;
    }

    public string ObjectId { get; }

    public string Audience { get; }

    public string TenantId { get; }

    public string ClientAppId { get; }

    public DateTime ExpiresOnUtc { get; }

    public PrincipalKind PrincipalKind { get; set; } = PrincipalKind.User;

    public IReadOnlyList<string> GroupObjectIds { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> Scopes { get; set; } = new[] { "mcp.access" };

    public bool IsAppOnly { get; set; }

    public IReadOnlyList<string> DirectoryRoleIds { get; set; } = Array.Empty<string>();
}
