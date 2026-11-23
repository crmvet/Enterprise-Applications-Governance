using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Product.Mcp.Core.Backend;

namespace Product.Azure.Host;

/// <summary>One answer from a downstream service, or why there was none. Never a token.</summary>
public sealed class DirectoryReply
{
    private DirectoryReply(int status, JsonNode? body, string? problem, string? errorCode, IReadOnlyList<JsonObject>? items)
    {
        Status = status;
        Body = body;
        Problem = problem;
        ErrorCode = errorCode;
        Items = items ?? Array.Empty<JsonObject>();
    }

    /// <summary>The HTTP status the service answered, or 0 when it did not answer.</summary>
    public int Status { get; }

    /// <summary>The JSON body, or null.</summary>
    public JsonNode? Body { get; }

    /// <summary>
    /// Why the service did not answer at all, in plain words: no token, a timeout, the request's budget, throttling, or a
    /// link to another host. Null when the service answered (with any status).
    /// </summary>
    public string? Problem { get; }

    /// <summary>The <c>error.code</c> of the service's error body, or null.</summary>
    public string? ErrorCode { get; }

    /// <summary>Every item of every page, for a paged read.</summary>
    public IReadOnlyList<JsonObject> Items { get; }

    /// <summary>True when the service answered.</summary>
    public bool Answered => Problem == null;

    /// <summary>True when the service answered with a 2xx status.</summary>
    public bool Ok => Answered && Status >= 200 && Status < 300;

    internal static DirectoryReply NoAnswer(string problem)
    {
        return new DirectoryReply(0, null, problem, null, null);
    }

    internal static DirectoryReply Of(int status, JsonNode? body)
    {
        return new DirectoryReply(status, body, null, ErrorCodeOf(body), null);
    }

    internal static DirectoryReply Paged(int status, IReadOnlyList<JsonObject> items)
    {
        return new DirectoryReply(status, null, null, null, items);
    }

    private static string? ErrorCodeOf(JsonNode? body)
    {
        return body is JsonObject obj && obj["error"] is JsonObject error ? DirectoryJson.Text(error, "code") : null;
    }
}

/// <summary>
/// The synchronous HTTP client of the directory reads. Each read gets a token from <see cref="IDownstreamTokenSource"/> and
/// sends it, as the only credential, to the one fixed host of its service: Microsoft Graph or the Power Platform API. A
/// URL on any other host, including a Graph next page link, is refused before anything is sent. Each call has a timeout
/// (<see cref="DirectoryOptions.CallTimeoutSeconds"/>) and all calls of one request share a budget
/// (<see cref="DirectoryOptions.ToolBudgetSeconds"/>). A 429 is retried once after its <c>Retry-After</c> delay. Nothing here
/// logs a token, a URL or a response body.
/// </summary>
/// <remarks>
/// Throttling: https://learn.microsoft.com/graph/throttling. Paging: https://learn.microsoft.com/graph/paging (use the
/// whole next link as given, and set custom headers such as ConsistencyLevel again on every page).
/// </remarks>
public sealed class DirectoryHttp
{
    /// <summary>The named HTTP client the directory reads use (redirects off, no request logging).</summary>
    public const string ClientName = "Product.Directory";

    internal const string BudgetKey = "Product.Directory.Budget";

    // A Retry-After longer than this is not waited out inside a request: the tool answers that the service is throttling.
    private static readonly TimeSpan LongestRetryAfter = TimeSpan.FromSeconds(10);

    private static readonly Uri Graph = new(DirectoryOptions.GraphBaseUrl);
    private static readonly Uri PowerPlatform = new(DirectoryOptions.PowerPlatformApiUrl);

    private readonly IHttpClientFactory _clients;
    private readonly IDownstreamTokenSource _tokens;
    private readonly IOptions<DirectoryOptions> _options;
    private readonly IHttpContextAccessor _accessor;
    private readonly ILogger<DirectoryHttp> _logger;

    /// <summary>Creates the client.</summary>
    public DirectoryHttp(IHttpClientFactory clients, IDownstreamTokenSource tokens, IOptions<DirectoryOptions> options, IHttpContextAccessor accessor, ILogger<DirectoryHttp> logger)
    {
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // How a retry waits. Tests replace it to record the delay instead of sleeping.
    internal Action<TimeSpan> Wait { get; set; } = delay => Thread.Sleep(delay);

    /// <summary>A GET to the service of <paramref name="api"/> as <paramref name="asPerson"/>.</summary>
    public DirectoryReply Get(DownstreamApi api, BackendCaller asPerson, string url, IReadOnlyDictionary<string, string>? headers = null)
    {
        return Run(api, asPerson, (token, budget) => Send(api, token, budget, HttpMethod.Get, url, null, headers));
    }

    /// <summary>A POST with a JSON body to the service of <paramref name="api"/> as <paramref name="asPerson"/>.</summary>
    public DirectoryReply Post(DownstreamApi api, BackendCaller asPerson, string url, string jsonBody)
    {
        return Run(api, asPerson, (token, budget) => Send(api, token, budget, HttpMethod.Post, url, jsonBody, null));
    }

    /// <summary>
    /// A paged Graph GET: follows <c>@odata.nextLink</c> as given, only on the Graph host and under the v1.0 path, up to
    /// <see cref="DirectoryOptions.MaxPages"/> pages, and collects every item of <c>value</c>.
    /// </summary>
    public DirectoryReply GetPages(BackendCaller asPerson, string firstUrl, IReadOnlyDictionary<string, string>? headers = null)
    {
        return Run(DownstreamApi.MicrosoftGraph, asPerson, (token, budget) =>
        {
            var items = new List<JsonObject>();
            string url = firstUrl;
            for (int page = 1; ; page++)
            {
                if (page > _options.Value.MaxPages)
                {
                    return DirectoryReply.NoAnswer("Microsoft Graph returned more than " + _options.Value.MaxPages.ToString(CultureInfo.InvariantCulture) + " pages, so the read stopped.");
                }

                DirectoryReply reply = Send(DownstreamApi.MicrosoftGraph, token, budget, HttpMethod.Get, url, null, headers);
                if (!reply.Ok)
                {
                    return reply;
                }

                if (reply.Body is JsonObject body && body["value"] is JsonArray values)
                {
                    foreach (JsonNode? value in values)
                    {
                        if (value is JsonObject item)
                        {
                            items.Add(item);
                        }
                    }
                }

                string? next = reply.Body is JsonObject withLink ? DirectoryJson.Text(withLink, "@odata.nextLink") : null;
                if (next == null)
                {
                    return DirectoryReply.Paged(reply.Status, items);
                }

                if (!Uri.TryCreate(next, UriKind.Absolute, out Uri? nextUri) || !Pinned(DownstreamApi.MicrosoftGraph, nextUri))
                {
                    _logger.LogWarning("Microsoft Graph sent a next page link outside the Graph v1.0 address; the read stopped.");
                    return DirectoryReply.NoAnswer("Microsoft Graph sent a next page link to another address. The read stopped, and no token was sent there.");
                }

                url = nextUri.AbsoluteUri;
            }
        });
    }

    // True when a URL is on the fixed host of the API: https, the exact host, the default port, no user information, and
    // for Graph the v1.0 path.
    internal static bool Pinned(DownstreamApi api, Uri uri)
    {
        Uri expected = api == DownstreamApi.MicrosoftGraph ? Graph : PowerPlatform;
        return uri.IsAbsoluteUri
            && uri.Scheme == Uri.UriSchemeHttps
            && string.Equals(uri.Host, expected.Host, StringComparison.OrdinalIgnoreCase)
            && uri.IsDefaultPort
            && string.IsNullOrEmpty(uri.UserInfo)
            && uri.AbsolutePath.StartsWith(expected.AbsolutePath, StringComparison.Ordinal);
    }

    private DirectoryReply Run(DownstreamApi api, BackendCaller asPerson, Func<DownstreamToken, DirectoryBudget, DirectoryReply> work)
    {
        if (asPerson == null)
        {
            throw new ArgumentNullException(nameof(asPerson));
        }

        DirectoryBudget budget = CurrentBudget();
        TimeSpan limit = budget.Limit(TimeSpan.FromSeconds(_options.Value.CallTimeoutSeconds));
        if (limit <= TimeSpan.Zero)
        {
            return DirectoryReply.NoAnswer(budget.SpentMessage());
        }

        DownstreamToken token;
        using (var timeout = new CancellationTokenSource(limit))
        {
            token = _tokens.ForUser(api, asPerson, timeout.Token);
        }

        return token.Issued ? work(token, budget) : DirectoryReply.NoAnswer(token.Problem ?? "No token was issued.");
    }

    private DirectoryReply Send(DownstreamApi api, DownstreamToken token, DirectoryBudget budget, HttpMethod method, string url, string? jsonBody, IReadOnlyDictionary<string, string>? headers)
    {
        string service = OnBehalfOfTokenSource.NameOf(api);
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || !Pinned(api, uri))
        {
            return DirectoryReply.NoAnswer("The product refused to send a token for " + service + " to another address.");
        }

        HttpClient client = _clients.CreateClient(ClientName);
        for (int attempt = 0; ; attempt++)
        {
            TimeSpan limit = budget.Limit(TimeSpan.FromSeconds(_options.Value.CallTimeoutSeconds));
            if (limit <= TimeSpan.Zero)
            {
                return DirectoryReply.NoAnswer(budget.SpentMessage());
            }

            using var request = new HttpRequestMessage(method, uri);
            token.Authorize(request);
            request.Headers.Accept.ParseAdd("application/json");
            if (headers != null)
            {
                foreach (KeyValuePair<string, string> header in headers)
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            if (jsonBody != null)
            {
                request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            }

            using var timeout = new CancellationTokenSource(limit);
            HttpResponseMessage response;
            try
            {
                response = client.Send(request, HttpCompletionOption.ResponseContentRead, timeout.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("{Service} did not answer a directory read in time.", service);
                return DirectoryReply.NoAnswer(service + " did not answer within " + Seconds(limit) + " seconds.");
            }
            catch (HttpRequestException exception)
            {
                _logger.LogWarning("{Service} could not be reached for a directory read ({Error}).", service, exception.HttpRequestError);
                return DirectoryReply.NoAnswer(service + " could not be reached.");
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    TimeSpan delay = RetryAfter(response) ?? TimeSpan.FromSeconds(1);
                    if (attempt > 0 || delay > LongestRetryAfter || delay >= budget.Remaining)
                    {
                        _logger.LogWarning("{Service} throttled a directory read (429).", service);
                        return DirectoryReply.NoAnswer(service + " is throttling the product's reads (HTTP 429) and asks to wait " + Seconds(delay) + " seconds. Try again later.");
                    }

                    Wait(delay);
                    continue;
                }

                JsonNode? body = ReadJson(response, timeout.Token);
                DirectoryReply reply = DirectoryReply.Of((int)response.StatusCode, body);
                if (!reply.Ok && reply.Status != 404)
                {
                    _logger.LogWarning("{Service} answered {Status} {ErrorCode} to a directory read.", service, reply.Status, reply.ErrorCode ?? "(no code)");
                }

                return reply;
            }
        }
    }

    private static JsonNode? ReadJson(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using Stream stream = response.Content.ReadAsStream(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string text = reader.ReadToEnd();
        if (text.Length == 0)
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The delay a 429 asks for in its Retry-After header: a number of seconds, or an HTTP date.
    // https://learn.microsoft.com/graph/throttling
    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Retry-After", out IEnumerable<string>? values))
        {
            return null;
        }

        foreach (string value in values)
        {
            string text = value.Trim();
            if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds))
            {
                return TimeSpan.FromSeconds(seconds);
            }

            if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset date))
            {
                TimeSpan wait = date - DateTimeOffset.UtcNow;
                return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
            }
        }

        return null;
    }

    private static string Seconds(TimeSpan span)
    {
        return Math.Ceiling(Math.Max(0, span.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
    }

    // The budget of the current request, shared by every downstream call it makes; a call outside a request gets its own.
    private DirectoryBudget CurrentBudget()
    {
        TimeSpan total = TimeSpan.FromSeconds(_options.Value.ToolBudgetSeconds);
        HttpContext? context = _accessor.HttpContext;
        if (context == null)
        {
            return new DirectoryBudget(total);
        }

        lock (context.Items)
        {
            if (context.Items.TryGetValue(BudgetKey, out object? existing) && existing is DirectoryBudget shared)
            {
                return shared;
            }

            var budget = new DirectoryBudget(total);
            context.Items[BudgetKey] = budget;
            return budget;
        }
    }
}

// The time all directory reads of one request may take together.
internal sealed class DirectoryBudget
{
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private readonly TimeSpan _total;

    public DirectoryBudget(TimeSpan total)
    {
        _total = total;
    }

    public TimeSpan Remaining => _total - _watch.Elapsed;

    // The time the next call may take: the call timeout, or less when the budget is nearly spent.
    public TimeSpan Limit(TimeSpan callTimeout)
    {
        TimeSpan remaining = Remaining;
        return remaining < callTimeout ? remaining : callTimeout;
    }

    public string SpentMessage()
    {
        return "The directory reads of this request used up their " + Math.Round(_total.TotalSeconds).ToString(CultureInfo.InvariantCulture) + " second budget. Ask for a smaller page and try again.";
    }
}

// Small readers over System.Text.Json nodes that never throw on an unexpected shape.
internal static class DirectoryJson
{
    public static string? Text(JsonObject? obj, string name)
    {
        if (obj == null || !obj.TryGetPropertyValue(name, out JsonNode? node) || node is not JsonValue value)
        {
            return null;
        }

        return value.TryGetValue(out string? text) && !string.IsNullOrWhiteSpace(text) ? text : null;
    }

    public static bool? Flag(JsonObject? obj, string name)
    {
        if (obj == null || !obj.TryGetPropertyValue(name, out JsonNode? node) || node is not JsonValue value)
        {
            return null;
        }

        return value.TryGetValue(out bool flag) ? flag : null;
    }

    public static DateTime? Moment(JsonObject? obj, string name)
    {
        string? text = Text(obj, name);
        return text != null && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset moment)
            ? moment.UtcDateTime
            : null;
    }

    public static bool IsGuid(string? text)
    {
        return text != null && Guid.TryParseExact(text, "D", out _);
    }
}
