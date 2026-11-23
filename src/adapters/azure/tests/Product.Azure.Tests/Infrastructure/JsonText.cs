using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Product.Engine.Core;
using Product.Engine.Runtime;
using Product.Mcp.Core.Envelope;

namespace Product.Azure.Tests;

// JSON comparisons for the replays: canonical text with members in ordinal order and every correlation ID masked (the
// HTTP host gives each request its own, the in-process core numbers its calls), and the outcome digest the walkthrough
// recorder keeps for each exchange (ok, the fixed error code, the sorted reason codes).
internal static class JsonText
{
    public const string Masked = "(correlation)";

    // Canonical text of a JSON value: members sorted by name, correlation IDs masked, and the JSON text of a tool
    // result's content item (the envelope again, as text) canonicalized the same way.
    public static string Canonical(JsonNode? node)
    {
        return Normalize(node)?.ToJsonString() ?? "null";
    }

    public static string Canonical(string json)
    {
        return Canonical(JsonNode.Parse(json));
    }

    public static JsonNode? Normalize(JsonNode? node, string? name = null)
    {
        switch (node)
        {
            case null:
                return null;
            case JsonObject obj:
                var sorted = new JsonObject();
                foreach (KeyValuePair<string, JsonNode?> member in obj.OrderBy(m => m.Key, StringComparer.Ordinal))
                {
                    sorted[member.Key] = member.Key == "correlationId" && member.Value is JsonValue ? JsonValue.Create(Masked) : Normalize(member.Value, member.Key);
                }

                return sorted;
            case JsonArray array:
                return new JsonArray(array.Select(item => Normalize(item)).ToArray());
            default:
                if (name == "text" && node is JsonValue value && value.TryGetValue(out string? text) && text.StartsWith("{", StringComparison.Ordinal))
                {
                    try
                    {
                        return JsonValue.Create(Canonical(JsonNode.Parse(text)));
                    }
                    catch (System.Text.Json.JsonException)
                    {
                        return node.DeepClone();
                    }
                }

                return node.DeepClone();
        }
    }

    // The outcome digest of an MCP answer, as the walkthrough recorder computes it.
    public static JsonObject McpDigest(JsonNode? body)
    {
        JsonNode? envelope = body?["result"]?["structuredContent"];
        var reasons = new List<string>();
        string? errorCode = null;
        bool? ok = null;
        if (envelope != null)
        {
            ok = envelope["ok"]?.GetValue<bool>();
            errorCode = envelope["error"]?["code"]?.GetValue<string>();
            reasons.AddRange(WarningCodes(envelope));
        }
        else if (body?["error"] != null)
        {
            ok = false;
            errorCode = body["error"]!["data"]?["error"]?["code"]?.GetValue<string>() ?? "jsonrpc " + body["error"]!["code"]!.ToJsonString();
        }
        else if (body?["result"] != null)
        {
            ok = true;
        }

        return Digest(ok, errorCode, reasons);
    }

    // The outcome digest of a REST envelope (the record endpoint, the approval outcome endpoint): ok, the fixed error
    // code, and the codes of the warnings the engine reported.
    public static JsonObject EnvelopeDigest(JsonNode? envelope)
    {
        bool? ok = envelope?["ok"]?.GetValue<bool>();
        string? errorCode = envelope?["error"]?["code"]?.GetValue<string>();
        return Digest(ok, errorCode, WarningCodes(envelope));
    }

    // The outcome digest of an engine answer, as the walkthrough recorder computes it.
    public static JsonObject EngineDigest(RuntimeOutcome outcome, McpError? mapped)
    {
        return Digest(outcome.Succeeded, mapped?.Code, outcome.Reasons.Select(r => r.Code));
    }

    public static JsonObject Digest(bool? ok, string? errorCode, IEnumerable<string> reasons)
    {
        return new JsonObject
        {
            ["ok"] = ok,
            ["errorCode"] = errorCode,
            ["reasonCodes"] = new JsonArray(reasons.Distinct(StringComparer.Ordinal).OrderBy(r => r, StringComparer.Ordinal).Select(r => (JsonNode)JsonValue.Create(r)!).ToArray()),
        };
    }

    private static IEnumerable<string> WarningCodes(JsonNode? envelope)
    {
        foreach (JsonNode? warning in envelope?["warnings"]?.AsArray() ?? new JsonArray())
        {
            if (warning?["code"]?.GetValue<string>() is string code)
            {
                yield return code;
            }
        }
    }
}
