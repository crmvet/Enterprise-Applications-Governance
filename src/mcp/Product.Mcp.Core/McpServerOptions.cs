using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Product.Engine.Runtime.Callers;

namespace Product.Mcp.Core;

// Host settings for the server core. None of them is configuration in the product's sense (D-06): they say where the
// server runs, not what the product allows.
public sealed class McpServerOptions
{
    // The server name in the initialize answer.
    public string ServerName { get; set; } = "product-mcp";

    // The canonical endpoint URL, which is also the Application ID URI (document 24, section 4.3), such as
    // https://<host>/mcp. Used in the protected resource metadata.
    public string ResourceUrl { get; set; } = "https://localhost/mcp";

    // The authorization server the metadata names: the tenant's /v2.0 identifier for a registered client (the setup
    // guide's lead path), or the versioned CIMD identifier (document 24, section 4.2).
    public string AuthorizationServer { get; set; } = "https://login.microsoftonline.com/common/v2.0";

    // The only delegated scope MCP clients request.
    public string Scope { get; set; } = "mcp.access";

    // The secret salt of the caller hash in telemetry. The host reads it
    // from its own configuration, such as an app setting that references a key vault secret, and never from a value in
    // code, so caller hashes from two hosts never join and a list of object IDs cannot be matched against them. There is
    // no default: with no salt, telemetry carries no caller field at all. A salt shorter than MinimumTelemetrySaltLength
    // is refused when the server is built (McpServerCore).
    public string? TelemetrySalt { get; set; }

    // The shortest salt the server accepts: a short word could be guessed and the hashes matched.
    public const int MinimumTelemetrySaltLength = 16;

    // Checks every envelope against the tool's output schema before it leaves (tests and the local harness). An envelope
    // that fails is a server bug and is answered as a JSON-RPC internal error.
    public bool ValidateOutputs { get; set; }

    // Tests only: let an unexpected failure surface as an exception instead of a JSON-RPC internal error.
    public bool RethrowInternalErrors { get; set; }

    // Called with an unexpected failure and its correlation ID before the core answers it as a JSON-RPC internal error, so
    // the host can log why a request failed (for example an envelope that does not match its output schema). Never called
    // for an expected refusal (every fixed error code is an answer, not a failure) or when RethrowInternalErrors is on. A
    // callback that throws is ignored and the answer does not change. The exception's message may quote request data, so
    // a host outside Development logs its type and stack only.
    public Action<Exception, string>? OnInternalFailure { get; set; }

    // How callers reach the engine through this server (document 22, section 2.4; document 24, section 4.5):
    // - AiClient (the default): the MCP endpoint. Every delegated caller is an AI client acting for the signed-in person,
    //   stamped ai-client-for-person, AI-created, with the person as On Behalf Of.
    // - Person: the product's own web app, through the host's REST API. The caller is the person, stamped person, with no
    //   On Behalf Of. Only the web app's own client IDs (PersonClientAppIds) may call on this entry; any other client is
    //   refused with not_authorized. The Known Client check (proposed C45) and the AI-only gates (Max AI State, AI May
    //   Submit) do not apply; Tool Policy roles, the rate limit and the input schema still do.
    // Workload is never a server entry: app-only callers use the record and approval outcome endpoints. The server core
    // refuses to start with Workload, or with Person and no web app client ID.
    public CallerEntry Entry { get; set; } = CallerEntry.AiClient;

    // The product's own web app client IDs (the azp claim of its tokens), set by the host from its configuration. Read only
    // with Entry Person. Matched ignoring case.
    public IReadOnlyList<string> PersonClientAppIds { get; set; } = Array.Empty<string>();

    // The Client App label stamped on rows a person writes through the web app.
    public string PersonClientLabel { get; set; } = "Web app";

    public Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    // One value per request, written to the envelope, the Audit Entry and telemetry.
    public Func<string> CorrelationIds { get; set; } = () => "mcp-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

    // The protected resource metadata URL (RFC 9728), which a 401 answer links to.
    public string ResourceMetadataUrl
    {
        get
        {
            var uri = new Uri(ResourceUrl);
            return uri.GetLeftPart(UriPartial.Authority) + "/.well-known/oauth-protected-resource" + uri.AbsolutePath.TrimEnd('/');
        }
    }

    // A repeatable correlation ID source for tests: "mcp-000001", "mcp-000002" and so on.
    public static Func<string> Sequential()
    {
        long sequence = 0;
        return () => "mcp-" + Interlocked.Increment(ref sequence).ToString("D6", CultureInfo.InvariantCulture);
    }
}
