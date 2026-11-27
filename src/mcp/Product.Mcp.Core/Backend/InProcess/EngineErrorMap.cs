using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Core;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Operations;
using Product.Engine.Core.Reasons;
using Product.Engine.Runtime;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Envelope;

namespace Product.Mcp.Core.Backend.InProcess;

// Maps an engine answer that failed (document 25, section 7) to one of the 12 fixed error codes (document 24, section 3.5),
// the same way for every entry point: the MCP endpoint, the REST API, the record endpoint and the approval outcome
// endpoint. The HTTP status of the answer is the code's status in errors.json (ContractSet.Errors).
//
// Order: the runtime outcome words invalid-input, not-found, conflict and unavailable first; then the engine reasons
// errors.json names for a code (most specific, tried in a fixed order; the reasons it names under ai_cannot_do_this only
// for an AI caller); then, for an AI caller only, a refusal by an AI rule (G-01, G-06, G-08, G-17, RA-06, or an ai.
// reason) is ai_cannot_do_this; then the reason's catalog category; else rule_violation. So ai_cannot_do_this always
// answers an AI client, never the approval runner, a pipeline or a person. The first error reason (or the first reason) gives the message and the field or rule key. No exception
// text and no token reach the answer.
public static class EngineErrorMap
{
    // The codes errors.json may name engine reasons for, in the order they are tried.
    private static readonly string[] ExplicitOrder =
    {
        McpErrorCodes.AiCannotDoThis, McpErrorCodes.Locked, McpErrorCodes.Conflict, McpErrorCodes.NotAuthorized, McpErrorCodes.NotFound,
        McpErrorCodes.ConfigurationUnavailable, McpErrorCodes.Validation, McpErrorCodes.RuleViolation,
    };

    private static readonly string[] AiRules = { "G-01", "G-06", "G-08", "G-17", "RA-06" };

    // Maps a failed runtime outcome (call it only when outcome.Succeeded is false). aiCaller is true when the caller is an AI
    // client acting for a person (actor kind ai-client-for-person): only then does a refusal by an AI rule map to
    // ai_cannot_do_this by its rule. A reason errors.json lists under a code maps to that code for every caller, but for
    // ai_cannot_do_this, which only an AI caller gets. The error carries the engine's message and the field or rule key it
    // names.
    public static McpError Map(RuntimeOutcome outcome, ContractSet contracts, bool aiCaller)
    {
        if (outcome == null)
        {
            throw new ArgumentNullException(nameof(outcome));
        }

        return Map(outcome.Outcome, outcome.Reasons, contracts, aiCaller);
    }

    // The same map over an outcome word (refused, invalid-input, not-found, conflict or unavailable) and its reasons in
    // canonical order (errors first), for a host that holds them without a RuntimeOutcome.
    public static McpError Map(string outcomeWord, IEnumerable<Reason> reasons, ContractSet contracts, bool aiCaller)
    {
        if (outcomeWord == null)
        {
            throw new ArgumentNullException(nameof(outcomeWord));
        }

        if (contracts == null)
        {
            throw new ArgumentNullException(nameof(contracts));
        }

        IReadOnlyList<Reason> all = (reasons ?? Enumerable.Empty<Reason>()).ToList();
        Reason? first = all.FirstOrDefault(r => r.Severity == ReasonSeverity.Error) ?? all.FirstOrDefault();
        string message = first?.Message ?? "The engine refused the call (" + outcomeWord + ").";
        string? reference = first?.Ref;

        switch (outcomeWord)
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
            // ai_cannot_do_this says "AI never approves", so it answers only an AI caller. Any other caller (a person, the
            // approval runner, a pipeline) refused for the same reason gets the code of its reason's category below.
            if (code == McpErrorCodes.AiCannotDoThis && !aiCaller)
            {
                continue;
            }

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

    private static McpError Build(string code, Reason reason, string message)
    {
        bool ruleCode = code == McpErrorCodes.RuleViolation || code == McpErrorCodes.Locked || code == McpErrorCodes.ConfigurationUnavailable;
        bool fieldCode = code == McpErrorCodes.Validation || code == McpErrorCodes.NotFound;
        return new McpError(code, message, fieldCode ? reason.Ref : null, ruleCode ? reason.Ref : null, GuidelineRefs.ForReason(reason.Code));
    }
}
