using System;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Json;

namespace Product.Mcp.Core.Envelope;

// The error object of the envelope (document 24, section 3.5): { code, message, field?, ruleKey?, guideline? }, plus
// retryAfterSeconds for rate_limited. The code is always one of the 11 fixed codes.
public sealed class McpError
{
    public McpError(string code, string message, string? field = null, string? ruleKey = null, string? guideline = null, int? retryAfterSeconds = null)
    {
        if (!McpErrorCodes.All.Contains(code, StringComparer.Ordinal))
        {
            throw new ArgumentException("The error code is not one of the fixed codes: " + code, nameof(code));
        }

        if (code == McpErrorCodes.RateLimited && (!retryAfterSeconds.HasValue || retryAfterSeconds.Value < 1))
        {
            throw new ArgumentException("rate_limited carries retryAfterSeconds of at least 1.", nameof(retryAfterSeconds));
        }

        Code = code;
        Message = Clip(string.IsNullOrWhiteSpace(message) ? "The call failed." : message, 1000);
        Field = field == null ? null : Clip(field, 200);
        RuleKey = ruleKey != null && IsKey(ruleKey) ? ruleKey : null;
        Guideline = guideline != null && IsGuideline(guideline) ? guideline : null;
        RetryAfterSeconds = retryAfterSeconds;
    }

    public string Code { get; }

    public string Message { get; }

    public string? Field { get; }

    public string? RuleKey { get; }

    public string? Guideline { get; }

    public int? RetryAfterSeconds { get; }

    public JsonTreeNode ToJson()
    {
        return Js.Obj(
            ("code", JsonTreeNode.FromString(Code)),
            ("message", JsonTreeNode.FromString(Message)),
            ("field", Js.OptStr(Field)),
            ("ruleKey", Js.OptStr(RuleKey)),
            ("guideline", Js.OptStr(Guideline)),
            ("retryAfterSeconds", RetryAfterSeconds.HasValue ? JsonTreeNode.FromInt64(RetryAfterSeconds.Value) : null));
    }

    public override string ToString()
    {
        return Code + ": " + Message;
    }

    internal static string Clip(string text, int max)
    {
        return text.Length <= max ? text : text.Substring(0, max - 3) + "...";
    }

    // A configuration key: lowercase letters, digits and hyphens, 1 to 64 characters, starting with a letter.
    internal static bool IsKey(string value)
    {
        if (value.Length == 0 || value.Length > 64 || value[0] < 'a' || value[0] > 'z')
        {
            return false;
        }

        foreach (char c in value)
        {
            if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-'))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsGuideline(string value)
    {
        if (value.Length < 2 || value.Length > 4 || value[0] != 'G' || value[1] < '1' || value[1] > '9')
        {
            return false;
        }

        for (int i = 2; i < value.Length; i++)
        {
            if (value[i] < '0' || value[i] > '9')
            {
                return false;
            }
        }

        return true;
    }
}
