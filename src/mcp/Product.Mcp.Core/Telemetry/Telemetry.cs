using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Product.Mcp.Core.Telemetry;

// Telemetry for the server (document 24, section 11: "tool name, outcome, latency, client app, a hash of the caller's
// object ID and the correlation ID. Never the token and never free text people typed").
//
// The event has only those fixed fields, so a token or typed text cannot reach a sink by construction: arguments, data,
// error messages and headers are never copied into it. A host binds a sink to Application Insights; the default sink
// drops every event.
public interface ITelemetrySink
{
    void Track(TelemetryEvent telemetryEvent);
}

public sealed class NullTelemetrySink : ITelemetrySink
{
    public static readonly NullTelemetrySink Instance = new();

    public void Track(TelemetryEvent telemetryEvent)
    {
    }
}

// One request: a tool call, a list, or a resource read.
public sealed class TelemetryEvent
{
    public TelemetryEvent(
        string operation,
        string? toolName,
        string outcome,
        long latencyMilliseconds,
        string? clientApp,
        string? callerHash,
        string correlationId,
        int? configurationSetVersion)
    {
        Operation = operation;
        ToolName = toolName;
        Outcome = outcome;
        LatencyMilliseconds = latencyMilliseconds;
        ClientApp = clientApp;
        CallerHash = callerHash;
        CorrelationId = correlationId;
        ConfigurationSetVersion = configurationSetVersion;
    }

    // The MCP method: tools/call, tools/list, resources/read, resources/list, initialize. A method the server does not
    // know is "(other)", and a message with none is "(none)": the client chooses the text, so it never reaches telemetry.
    public string Operation { get; }

    // The tool name for tools/call, or the resource template name for resources/read. A name that is not in the contract
    // is "(unknown)", for the same reason.
    public string? ToolName { get; }

    // "ok", one of the 11 fixed error codes, or a JSON-RPC error kind such as "invalid-request" or "internal-error".
    public string Outcome { get; }

    public long LatencyMilliseconds { get; }

    // The client app's label (from the Known Client row) or its app ID. Never a token.
    public string? ClientApp { get; }

    // A salted SHA-256 hash of the caller's object ID, so calls by one person can be counted without naming them. Null
    // when no caller is signed in, and always null when the host set no salt (McpServerOptions.TelemetrySalt).
    public string? CallerHash { get; }

    public string CorrelationId { get; }

    public int? ConfigurationSetVersion { get; }

    // The event as name and value pairs, in a fixed order, for a sink that writes key-value telemetry.
    public IReadOnlyList<KeyValuePair<string, string>> ToProperties()
    {
        var properties = new List<KeyValuePair<string, string>>
        {
            new("operation", Operation),
            new("outcome", Outcome),
            new("latencyMs", LatencyMilliseconds.ToString(CultureInfo.InvariantCulture)),
            new("correlationId", CorrelationId),
        };
        if (ToolName != null)
        {
            properties.Add(new("tool", ToolName));
        }

        if (ClientApp != null)
        {
            properties.Add(new("clientApp", ClientApp));
        }

        if (CallerHash != null)
        {
            properties.Add(new("callerHash", CallerHash));
        }

        if (ConfigurationSetVersion.HasValue)
        {
            properties.Add(new("configurationSetVersion", ConfigurationSetVersion.Value.ToString(CultureInfo.InvariantCulture)));
        }

        return properties;
    }

    // A salted hash of an object ID (lowercase hex). The salt is a host secret from the host's configuration, never a
    // value in code, so hashes from two hosts do not join. An empty salt is refused: an unsalted hash of an object ID is
    // matched by anyone with a list of IDs, so the server sends no caller field instead (McpServerCore).
    public static string HashCaller(string objectId, string salt)
    {
        if (string.IsNullOrEmpty(salt))
        {
            throw new ArgumentException("A caller hash needs the host's salt. With no salt, telemetry carries no caller field.", nameof(salt));
        }

        if (string.IsNullOrEmpty(objectId))
        {
            throw new ArgumentException("A caller hash needs an object ID.", nameof(objectId));
        }

        using SHA256 sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(salt + "|" + objectId.ToLowerInvariant()));
        var text = new StringBuilder(hash.Length * 2);
        foreach (byte b in hash)
        {
            text.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return text.ToString();
    }
}
