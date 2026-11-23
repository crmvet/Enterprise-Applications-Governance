using System;
using System.Linq;
using Product.Engine.Core;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Operations;
using Product.Engine.Core.Reasons;
using Product.Engine.Runtime;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Envelope;

namespace Product.Azure.Host;

// Maps a failed engine answer to one of the 11 fixed error codes, for the routes that call the engine directly (the
// record endpoint and the approval outcome endpoint). The MCP core's own map is internal (ENGINE-CHANGES-NEEDED.md,
// item 3), so this is the same map in the same order: the engine reasons errors.json names for a code, then the reason's
// catalog category, then the runtime outcome word. A refusal by an AI rule for an AI caller is ai_cannot_do_this.
internal static class ProductErrorMap
{
    private static readonly string[] ExplicitOrder =
    {
        McpErrorCodes.AiCannotDoThis, McpErrorCodes.Locked, McpErrorCodes.Conflict, McpErrorCodes.NotAuthorized, McpErrorCodes.NotFound,
        McpErrorCodes.ConfigurationUnavailable, McpErrorCodes.Validation, McpErrorCodes.RuleViolation,
    };

    private static readonly string[] AiRules = { "G-01", "G-06", "G-08", "G-17", "RA-06" };

    public static McpError Map(RuntimeOutcome outcome, ContractSet contracts, bool aiCaller)
    {
        if (outcome == null)
        {
            throw new ArgumentNullException(nameof(outcome));
        }

        Reason? first = outcome.Reasons.FirstOrDefault(r => r.Severity == ReasonSeverity.Error) ?? outcome.Reasons.FirstOrDefault();
        string message = first?.Message ?? "The engine refused the call (" + outcome.Outcome + ").";
        string? reference = first?.Ref;

        switch (outcome.Outcome)
        {
            case OperationOutcomes.InvalidInput:
                return new McpError(McpErrorCodes.Validation, message, reference);
            case RuntimeOutcome.NotFound:
                return new McpError(McpErrorCodes.NotFound, message, reference);
            case RuntimeOutcome.Conflict:
                return new McpError(McpErrorCodes.Conflict, message);
            case OperationOutcomes.Unavailable:
                return new McpError(McpErrorCodes.ConfigurationUnavailable, message, ruleKey: reference);
        }

        if (first == null)
        {
            return new McpError(McpErrorCodes.RuleViolation, message);
        }

        foreach (string code in ExplicitOrder)
        {
            if (contracts.Errors.TryGetValue(code, out ErrorCodeInfo? info) && info.Maps(first.Code))
            {
                return Build(code, first, message);
            }
        }

        ReasonCatalog.TryGet(first.Code, out ReasonCodeInfo? catalog);
        if (aiCaller && catalog != null && (catalog.RuleIds.Any(id => AiRules.Contains(id, StringComparer.Ordinal)) || first.Code.StartsWith("ai.", StringComparison.Ordinal)))
        {
            return Build(McpErrorCodes.AiCannotDoThis, first, message);
        }

        ReasonCategories categories = catalog?.Categories ?? ReasonCategories.RefusedRule;
        if (categories.HasFlag(ReasonCategories.RefusedRole))
        {
            return Build(McpErrorCodes.NotAuthorized, first, message);
        }

        if (categories.HasFlag(ReasonCategories.Conflict))
        {
            return Build(McpErrorCodes.Conflict, first, message);
        }

        if (categories.HasFlag(ReasonCategories.Unavailable) && !categories.HasFlag(ReasonCategories.RefusedRule))
        {
            return Build(McpErrorCodes.ConfigurationUnavailable, first, message);
        }

        if (categories.HasFlag(ReasonCategories.NotFound))
        {
            return Build(McpErrorCodes.NotFound, first, message);
        }

        if (categories.HasFlag(ReasonCategories.InvalidInput) && !categories.HasFlag(ReasonCategories.RefusedRule))
        {
            return Build(McpErrorCodes.Validation, first, message);
        }

        return Build(McpErrorCodes.RuleViolation, first, message);
    }

    // The HTTP status of a fixed code, from errors.json.
    public static int HttpStatus(string code, ContractSet contracts)
    {
        return contracts.Errors.TryGetValue(code, out ErrorCodeInfo? info) ? info.HttpStatus : 500;
    }

    private static McpError Build(string code, Reason reason, string message)
    {
        bool ruleCode = code == McpErrorCodes.RuleViolation || code == McpErrorCodes.Locked || code == McpErrorCodes.ConfigurationUnavailable;
        bool fieldCode = code == McpErrorCodes.Validation || code == McpErrorCodes.NotFound;
        return new McpError(code, message, fieldCode ? reason.Ref : null, ruleCode ? reason.Ref : null);
    }
}
