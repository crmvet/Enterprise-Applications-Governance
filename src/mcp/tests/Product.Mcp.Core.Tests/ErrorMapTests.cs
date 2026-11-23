using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Core;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Operations;
using Product.Engine.Core.Reasons;
using Product.Engine.Runtime;
using Product.Engine.Runtime.Callers;
using Product.Mcp.Core.Backend.InProcess;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Envelope;
using Xunit;

namespace Product.Mcp.Core.Tests;

// The public engine-to-error map (EngineErrorMap), which the MCP backend uses and the record and approval outcome
// endpoints share. Every engine reason errors.json names maps to its code and HTTP status for AI and other callers alike;
// an AI rule's refusal is ai_cannot_do_this only for an AI caller; the runtime outcome words map first.
public sealed class ErrorMapTests
{
    private static readonly ContractSet Contracts = ContractSet.Embedded;

    // The order errors.json codes are tried in, as the map documents it.
    private static readonly string[] ExplicitOrder =
    {
        McpErrorCodes.AiCannotDoThis, McpErrorCodes.Locked, McpErrorCodes.Conflict, McpErrorCodes.NotAuthorized, McpErrorCodes.NotFound,
        McpErrorCodes.ConfigurationUnavailable, McpErrorCodes.Validation, McpErrorCodes.RuleViolation,
    };

    private static readonly string[] AiRules = { "G-01", "G-06", "G-08", "G-17", "RA-06" };

    [Fact]
    public void EveryReasonErrorsJsonNamesMapsToItsCodeForEveryCaller()
    {
        int checkedReasons = 0;
        foreach (ErrorCodeInfo info in Contracts.Errors.Values.Where(e => e.EngineReasons.Count > 0))
        {
            foreach (string pattern in info.EngineReasons)
            {
                string[] codes = pattern.EndsWith(".*", StringComparison.Ordinal)
                    ? ReasonCatalog.All.Select(r => r.Code).Where(c => c.StartsWith(pattern.Substring(0, pattern.Length - 1), StringComparison.Ordinal)).ToArray()
                    : new[] { pattern };
                Assert.True(codes.Length > 0, "errors.json names " + pattern + " under " + info.Code + ", and the reason catalog has no such reason.");
                foreach (string code in codes)
                {
                    string expected = ExplicitOrder.First(c => Contracts.Errors.TryGetValue(c, out ErrorCodeInfo? listed) && listed.Maps(code));
                    foreach (bool ai in new[] { true, false })
                    {
                        McpError mapped = EngineErrorMap.Map(OperationOutcomes.Refused, new[] { Error(code, "fixture-ref") }, Contracts, ai);
                        Assert.True(mapped.Code == expected, code + " for " + (ai ? "an AI client" : "another caller") + " maps to " + mapped.Code + ", not " + expected + ".");
                        Assert.Equal(Contracts.Errors[expected].HttpStatus, Contracts.Errors[mapped.Code].HttpStatus);
                        checkedReasons++;
                    }
                }
            }
        }

        Assert.True(checkedReasons > 20, "only " + checkedReasons + " mappings checked");
        Assert.Equal(403, Contracts.Errors[EngineErrorMap.Map(OperationOutcomes.Refused, new[] { Error(ReasonCodes.ApprovalOutcomeWriter) }, Contracts, false).Code].HttpStatus);
    }

    [Fact]
    public void TheRuntimeOutcomeWordsMapFirst()
    {
        var words = new Dictionary<string, string>
        {
            [OperationOutcomes.InvalidInput] = McpErrorCodes.Validation,
            [RuntimeOutcome.NotFound] = McpErrorCodes.NotFound,
            [RuntimeOutcome.Conflict] = McpErrorCodes.Conflict,
            [OperationOutcomes.Unavailable] = McpErrorCodes.ConfigurationUnavailable,
        };
        foreach (KeyValuePair<string, string> word in words)
        {
            foreach (bool ai in new[] { true, false })
            {
                // Whatever reason comes with the word, even an AI rule's: the word decides.
                Assert.Equal(word.Value, EngineErrorMap.Map(word.Key, new[] { Error(ReasonCodes.ApprovalOutcomeWriter, "fixture-ref") }, Contracts, ai).Code);
                Assert.Equal(word.Value, EngineErrorMap.Map(word.Key, Array.Empty<Reason>(), Contracts, ai).Code);
            }
        }

        // A refusal with no reason is a rule violation that names the outcome.
        McpError bare = EngineErrorMap.Map(OperationOutcomes.Refused, Array.Empty<Reason>(), Contracts, true);
        Assert.Equal(McpErrorCodes.RuleViolation, bare.Code);
        Assert.Contains("refused", bare.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAiRuleRefusalIsAiCannotDoThisOnlyForAnAiCaller()
    {
        int checkedReasons = 0;
        foreach (ReasonCodeInfo reason in ReasonCatalog.All)
        {
            bool listed = ExplicitOrder.Any(c => Contracts.Errors.TryGetValue(c, out ErrorCodeInfo? info) && info.Maps(reason.Code));
            bool aiRule = reason.RuleIds.Any(id => AiRules.Contains(id, StringComparer.Ordinal)) || reason.Code.StartsWith("ai.", StringComparison.Ordinal);
            string forAi = EngineErrorMap.Map(OperationOutcomes.Refused, new[] { Error(reason.Code) }, Contracts, true).Code;
            string forOther = EngineErrorMap.Map(OperationOutcomes.Refused, new[] { Error(reason.Code) }, Contracts, false).Code;
            Assert.True(Contracts.Errors.ContainsKey(forAi) && Contracts.Errors.ContainsKey(forOther), reason.Code);
            if (!listed)
            {
                Assert.True(aiRule == (forAi == McpErrorCodes.AiCannotDoThis), reason.Code + " for an AI client maps to " + forAi + ".");
                Assert.True(forOther != McpErrorCodes.AiCannotDoThis, reason.Code + " for another caller maps to ai_cannot_do_this.");
                if (!aiRule)
                {
                    Assert.Equal(forAi, forOther);
                }

                checkedReasons++;
            }
        }

        Assert.True(checkedReasons > 50, "only " + checkedReasons + " unlisted reasons checked");
        Assert.Equal(McpErrorCodes.AiCannotDoThis, EngineErrorMap.Map(OperationOutcomes.Refused, new[] { Error(ReasonCodes.AiStatusNotAllowed) }, Contracts, true).Code);
        Assert.NotEqual(McpErrorCodes.AiCannotDoThis, EngineErrorMap.Map(OperationOutcomes.Refused, new[] { Error(ReasonCodes.AiStatusNotAllowed) }, Contracts, false).Code);
    }

    [Fact]
    public void TheFirstErrorGivesTheMessageAndTheFieldOrRuleKey()
    {
        var reasons = new[]
        {
            new Reason(ReasonCodes.AiForcedDraft, ReasonSeverity.Info, "An info first.", "Intake Request", "intake-draft"),
            new Reason("link.url-pattern", ReasonSeverity.Error, "The URL does not match the link kind.", "Link Kind", "github-issue"),
        };
        McpError rule = EngineErrorMap.Map(OperationOutcomes.Refused, reasons, Contracts, false);
        Assert.Equal(McpErrorCodes.RuleViolation, rule.Code);
        Assert.Equal("The URL does not match the link kind.", rule.Message);
        Assert.Equal("github-issue", rule.RuleKey);
        Assert.Null(rule.Field);

        McpError field = EngineErrorMap.Map(OperationOutcomes.Refused, new[] { Error(ReasonCodes.InputMalformed, "title") }, Contracts, false);
        Assert.Equal(McpErrorCodes.Validation, field.Code);
        Assert.Equal("title", field.Field);
        Assert.Null(field.RuleKey);
    }

    // The overload over a RuntimeOutcome gives the same answer as the one over its word and reasons, for real refusals.
    [Fact]
    public void ARuntimeOutcomeMapsAsItsWordAndReasons()
    {
        McpHarness h = McpHarness.Create();
        CallerIdentity ai = CallerIdentity.AiClient(McpHarness.Owner, new[] { McpHarness.OwnerGroup }, "Claude Code", clientAppVerified: true);
        CallerIdentity person = CallerIdentity.Person(McpHarness.Owner, new[] { McpHarness.OwnerGroup }, "Web app");
        RuntimeOutcome proposed = h.Runtime.Write(ai, WriteCommand.Create("decision", "{\"decisionType\":\"design\",\"subjectKind\":\"business-application\",\"subjectApplication\":\"" + McpHarness.Expense + "\",\"statement\":\"Use the shared data model\",\"status\":\"decision-proposed\"}"));
        Assert.True(proposed.Succeeded, proposed.ToString());
        var outcomes = new List<(RuntimeOutcome Outcome, string ForAi, string ForOther)>
        {
            // No such decision: the outcome word not-found decides.
            (h.Runtime.Write(ai, WriteCommand.Update("decision", McpHarness.Expense, "{\"statement\":\"x\"}")), McpErrorCodes.NotFound, McpErrorCodes.NotFound),

            // An AI client sets a decision accepted: refused first by G-08 (ai.status-not-allowed), which errors.json does not
            // list, so it is ai_cannot_do_this for an AI caller and a rule violation for any other.
            (h.Runtime.Write(ai, WriteCommand.Update("decision", proposed.RowId!, "{\"status\":\"decision-accepted\"}")), McpErrorCodes.AiCannotDoThis, McpErrorCodes.RuleViolation),
            (h.Runtime.Write(person, WriteCommand.Create("intake-request", "{\"title\":\"x\",\"need\":\"y\",\"colour\":\"blue\"}")), McpErrorCodes.Validation, McpErrorCodes.Validation),
        };

        foreach ((RuntimeOutcome outcome, string forAi, string forOther) in outcomes)
        {
            Assert.False(outcome.Succeeded, outcome.ToString());
            foreach ((bool aiCaller, string expected) in new[] { (true, forAi), (false, forOther) })
            {
                McpError viaOutcome = EngineErrorMap.Map(outcome, Contracts, aiCaller);
                McpError viaWord = EngineErrorMap.Map(outcome.Outcome, outcome.Reasons, Contracts, aiCaller);
                Assert.True(viaOutcome.Code == expected, outcome + " maps to " + viaOutcome.Code + ", not " + expected + ".");
                Assert.Equal(viaOutcome.Code, viaWord.Code);
                Assert.Equal(viaOutcome.Message, viaWord.Message);
                Assert.Equal(viaOutcome.Field, viaWord.Field);
                Assert.Equal(viaOutcome.RuleKey, viaWord.RuleKey);
            }
        }

        Assert.Throws<ArgumentNullException>(() => EngineErrorMap.Map((RuntimeOutcome)null!, Contracts, false));
    }

    private static Reason Error(string code, string? reference = null)
    {
        return new Reason(code, ReasonSeverity.Error, "Fixture reason " + code + ".", null, reference);
    }
}
