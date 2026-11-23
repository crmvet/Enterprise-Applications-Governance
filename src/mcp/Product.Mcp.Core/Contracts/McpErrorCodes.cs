using System;
using System.Collections.Generic;

namespace Product.Mcp.Core.Contracts;

// The 11 fixed error codes of document 24, section 3.5 (mcp/contracts/errors.json). Codes never change and are never
// reused. ContractSet checks at load time that errors.json holds exactly these.
public static class McpErrorCodes
{
    public const string NotSignedIn = "not_signed_in";
    public const string NotAuthorized = "not_authorized";
    public const string AiCannotDoThis = "ai_cannot_do_this";
    public const string RuleViolation = "rule_violation";
    public const string Validation = "validation";
    public const string NotFound = "not_found";
    public const string Conflict = "conflict";
    public const string Locked = "locked";
    public const string RateLimited = "rate_limited";
    public const string UpstreamUnavailable = "upstream_unavailable";
    public const string ConfigurationUnavailable = "configuration_unavailable";

    public static readonly IReadOnlyList<string> All = new[]
    {
        NotSignedIn, NotAuthorized, AiCannotDoThis, RuleViolation, Validation, NotFound, Conflict, Locked, RateLimited,
        UpstreamUnavailable, ConfigurationUnavailable,
    };
}

// One entry of errors.json.
public sealed class ErrorCodeInfo
{
    public ErrorCodeInfo(string code, int httpStatus, bool retryable, IReadOnlyList<string> engineReasons)
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        HttpStatus = httpStatus;
        Retryable = retryable;
        EngineReasons = engineReasons ?? Array.Empty<string>();
    }

    public string Code { get; }

    public int HttpStatus { get; }

    public bool Retryable { get; }

    // Engine reason codes that map to this code. A trailing ".*" means every code with that prefix.
    public IReadOnlyList<string> EngineReasons { get; }

    public bool Maps(string reasonCode)
    {
        foreach (string pattern in EngineReasons)
        {
            if (pattern.EndsWith(".*", StringComparison.Ordinal)
                ? reasonCode.StartsWith(pattern.Substring(0, pattern.Length - 1), StringComparison.Ordinal)
                : string.Equals(pattern, reasonCode, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
