using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Product.Engine.Core.Contract;
using Product.Mcp.Core.Backend;

namespace Product.Azure.Host;

/// <summary>
/// One recorded exchange with Microsoft Graph or the Power Platform API, read from a JSON file: the request it answers
/// (method, full URL, optionally a text the body must contain) and one or more responses, served in order (the last one
/// repeats). Recordings hold fictional data only.
/// <code>
/// { "description": "...",
///   "request": { "method": "GET", "url": "https://graph.microsoft.com/v1.0/groups/...", "bodyContains": "..." },
///   "responses": [ { "status": 429, "headers": { "Retry-After": "1" } }, { "status": 200, "body": { ... } } ] }
/// </code>
/// </summary>
public sealed class RecordedExchange
{
    private int _served;

    private RecordedExchange(string source, string method, string url, string? bodyContains, IReadOnlyList<RecordedResponse> responses)
    {
        Source = source;
        Method = method;
        Url = url;
        BodyContains = bodyContains;
        Responses = responses;
    }

    /// <summary>The file the exchange came from.</summary>
    public string Source { get; }

    /// <summary>The HTTP method.</summary>
    public string Method { get; }

    /// <summary>The full request URL, unescaped.</summary>
    public string Url { get; }

    /// <summary>A text the request body must contain, or null.</summary>
    public string? BodyContains { get; }

    /// <summary>The responses, served in order.</summary>
    public IReadOnlyList<RecordedResponse> Responses { get; }

    /// <summary>Reads every recording (*.json) under a folder, in file name order.</summary>
    public static IReadOnlyList<RecordedExchange> LoadFolder(string folder)
    {
        if (!System.IO.Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException("The folder of recorded directory responses does not exist: " + folder);
        }

        return System.IO.Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => Parse(File.ReadAllText(f), f))
            .ToList();
    }

    /// <summary>Reads one recording.</summary>
    public static RecordedExchange Parse(string json, string source)
    {
        JsonObject root = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException(source + " is not a JSON object.");
        JsonObject request = root["request"] as JsonObject ?? throw new InvalidDataException(source + " has no request.");
        string method = DirectoryJson.Text(request, "method") ?? throw new InvalidDataException(source + " has no request method.");
        string url = DirectoryJson.Text(request, "url") ?? throw new InvalidDataException(source + " has no request URL.");
        var responses = new List<RecordedResponse>();
        foreach (JsonObject response in (root["responses"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (response["headers"] is JsonObject recordedHeaders)
            {
                foreach (KeyValuePair<string, JsonNode?> header in recordedHeaders)
                {
                    headers[header.Key] = header.Value?.ToString() ?? string.Empty;
                }
            }

            int status = response["status"] is JsonValue value && value.TryGetValue(out int code) ? code : 200;
            responses.Add(new RecordedResponse(status, headers, response["body"]?.ToJsonString()));
        }

        if (responses.Count == 0)
        {
            throw new InvalidDataException(source + " has no responses.");
        }

        return new RecordedExchange(source, method.ToUpperInvariant(), Normalize(url), DirectoryJson.Text(request, "bodyContains"), responses);
    }

    /// <summary>A URL in the form recordings are matched in: absolute, unescaped.</summary>
    public static string Normalize(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ? Uri.UnescapeDataString(uri.AbsoluteUri) : url;
    }

    /// <summary>True when this exchange answers the request.</summary>
    public bool Matches(string method, string url, string? body)
    {
        return string.Equals(Method, method, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Url, Normalize(url), StringComparison.Ordinal)
            && (BodyContains == null || (body != null && body.Contains(BodyContains, StringComparison.Ordinal)));
    }

    /// <summary>The next response.</summary>
    public RecordedResponse Next()
    {
        int index = Interlocked.Increment(ref _served) - 1;
        return Responses[Math.Min(index, Responses.Count - 1)];
    }
}

/// <summary>One recorded response.</summary>
public sealed class RecordedResponse
{
    /// <summary>Creates the response.</summary>
    public RecordedResponse(int status, IReadOnlyDictionary<string, string> headers, string? body)
    {
        Status = status;
        Headers = headers;
        Body = body;
    }

    /// <summary>The HTTP status.</summary>
    public int Status { get; }

    /// <summary>The headers.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; }

    /// <summary>The JSON body, or null.</summary>
    public string? Body { get; }

    /// <summary>The response as an HTTP message.</summary>
    public HttpResponseMessage ToMessage(HttpRequestMessage request)
    {
        var message = new HttpResponseMessage((HttpStatusCode)Status) { RequestMessage = request };
        message.Content = new StringContent(Body ?? string.Empty, Encoding.UTF8, "application/json");
        foreach (KeyValuePair<string, string> header in Headers)
        {
            if (!message.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                message.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return message;
    }
}

/// <summary>
/// Development only: answers the directory reads' HTTP requests from recordings on disk. It is the innermost handler, so a
/// request never reaches the network. A request no recording answers gets a 404 that says so.
/// </summary>
public sealed class RecordedDirectoryHandler : HttpMessageHandler
{
    private readonly IReadOnlyList<RecordedExchange> _exchanges;

    /// <summary>Creates the handler over recordings.</summary>
    public RecordedDirectoryHandler(IReadOnlyList<RecordedExchange> exchanges)
    {
        _exchanges = exchanges ?? throw new ArgumentNullException(nameof(exchanges));
    }

    /// <summary>The recording that answers a request, or null.</summary>
    public RecordedExchange? Find(string method, string url, string? body)
    {
        return _exchanges.FirstOrDefault(e => e.Matches(method, url, body));
    }

    /// <inheritdoc />
    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        // The bytes of the buffered body, without taking the content's own read stream from the next reader.
        string? body = request.Content == null ? null : Encoding.UTF8.GetString(request.Content.ReadAsByteArrayAsync(cancellationToken).GetAwaiter().GetResult());

        RecordedExchange? exchange = Find(request.Method.Method, request.RequestUri?.AbsoluteUri ?? string.Empty, body);
        if (exchange == null)
        {
            var missing = new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request };
            missing.Content = new StringContent("{\"error\":{\"code\":\"NotRecorded\",\"message\":\"No recording answers this request.\"}}", Encoding.UTF8, "application/json");
            return missing;
        }

        return exchange.Next().ToMessage(request);
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(Send(request, cancellationToken));
    }
}

/// <summary>
/// Development only: the token source of the recorded mode. It issues a fixed placeholder for a person (the recorded
/// handler never checks it and nothing reaches the network) and refuses everyone else, as the live source does.
/// </summary>
public sealed class RecordedTokenSource : IDownstreamTokenSource
{
    /// <summary>The placeholder the recorded mode sends. It is not a token.</summary>
    public const string Placeholder = "recorded-mode-placeholder";

    /// <inheritdoc />
    public DownstreamToken ForUser(DownstreamApi api, BackendCaller asPerson, CancellationToken cancellationToken)
    {
        if (asPerson == null)
        {
            throw new ArgumentNullException(nameof(asPerson));
        }

        return asPerson.PrincipalKind == PrincipalKind.User
            ? DownstreamToken.FromAccessToken(Placeholder)
            : DownstreamToken.Refused("Only a person reads the directory through the product. Agent identities, agent users and workloads never do.");
    }
}
