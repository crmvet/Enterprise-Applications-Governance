using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Core.Reasons;

namespace Product.Mcp.Core.Envelope;

// The guideline a refusal applies (document 24, B24-18): the envelope's error.guideline, which the pages show as a chip
// beside the refusal and open from list_guidelines. Guideline numbers are permanent (content/guidelines/README.md) and the
// reason codes are stable (document 25, section 7), so the product ships this map. Choice: a fixed map in the product, not
// a configuration column, because no configuration row ties a guard rule to a guideline and every adopter keeps the
// same numbers; an adopter who does not adopt a guideline still sees its number, and list_guidelines says it is not
// adopted and why. A refusal no guideline speaks to carries none.
public static class GuidelineRefs
{
    // The AI rules of the error map (G-01, G-06, G-08, G-17, RA-06): AI drafts and recommends; people approve.
    private static readonly string[] AiRules = { "G-01", "G-06", "G-08", "G-17", "RA-06" };

    // Reason code prefixes and the guideline each applies, most specific first.
    private static readonly (string Prefix, string Guideline)[] ByPrefix =
    {
        ("ai.", "G17"),
        ("approval.outcome-writer", "G17"),
        ("approval.separation", "G12"),
        ("separation.", "G12"),
        ("authorization.", "G12"),
        ("standard-model.", "G12"),
        ("tier.", "G3"),
        ("dependency.", "G6"),
        ("documentation.", "G20"),
        ("decision.", "G4"),
        ("register.", "G1"),
        ("role.", "G14"),
        ("actor.", "G14"),
        ("deployment.", "G9"),
        // Data reasons by meaning (product review, major; D-41): owner gaps go to G2, decisions to
        // G4, and the AI use and data rules to G16. A copy rule refusal has no guideline yet, and neither do business
        // data domains. G8 is about Dataverse and never applies.
        ("data.rule-never", "G16"),
        ("data.breaks-rule", "G16"),
        ("data.undeclared", "G16"),
        ("data.decision", "G4"),
        ("data.re-decide", "G4"),
        ("data.owner", "G2"),
        ("data.own-owner", "G2"),
        ("data.no-deputy", "G2"),
        ("data.no-one-to-ask", "G2"),
        ("audit.", "G15"),
    };

    // Every prefix the map knows, for list_guidelines (which refusals point to which guideline).
    public static IReadOnlyList<(string Prefix, string Guideline)> Prefixes => ByPrefix;

    // The guideline number for an engine reason code, or null.
    public static string? ForReason(string? code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return null;
        }

        if (ReasonCatalog.TryGet(code, out ReasonCodeInfo? info) && info.RuleIds.Any(id => AiRules.Contains(id, StringComparer.Ordinal)))
        {
            return "G17";
        }

        foreach ((string prefix, string guideline) in ByPrefix)
        {
            if (code!.StartsWith(prefix, StringComparison.Ordinal))
            {
                return guideline;
            }
        }

        return null;
    }

    // The guideline for an error code alone: every ai_cannot_do_this answer applies G17, whatever refused it (the Tool
    // Policy, the server core or the engine).
    public static string? ForErrorCode(string code)
    {
        return code == Contracts.McpErrorCodes.AiCannotDoThis ? "G17" : null;
    }

    // The guideline of a refused call as its audit entry holds it: the reason's, else the error code's. A call to a tool
    // the product does not have (an approve tool, AN-01) applies G17 too: AI drafts and recommends; people approve.
    public static string? ForRefusal(string? reasonCode, string code, bool knownTool)
    {
        return ForReason(reasonCode) ?? ForErrorCode(code) ?? (knownTool ? null : "G17");
    }
}
