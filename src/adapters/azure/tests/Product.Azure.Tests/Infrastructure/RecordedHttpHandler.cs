using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Product.Azure.Host;
using Product.Engine.Core.Contract;
using Product.Mcp.Core.Backend;

namespace Product.Azure.Tests;

// One request the directory reads sent, as the fake service saw it.
internal sealed class SeenRequest
{
    public SeenRequest(string method, string url, string? authorization, string? body, bool recorded)
    {
        Method = method;
        Url = url;
        Authorization = authorization;
        Body = body;
        Recorded = recorded;
    }

    public string Method { get; }

    public string Url { get; }

    // The whole Authorization header, such as "Bearer fake-graph-token-0001".
    public string? Authorization { get; }

    public string? Body { get; }

    // False when no recording answered the request (the handler answered 404 NotRecorded).
    public bool Recorded { get; }

    public override string ToString()
    {
        return Method + " " + Url + (Recorded ? string.Empty : " (not recorded)");
    }
}

// Microsoft Graph and the Power Platform API as recorded, fictional responses: the files under Recorded/ (one exchange
// per file, the format RecordedExchange reads) and any inline recordings a test adds. Every request is kept, with its
// Authorization header, so a test can prove which token went where. Nothing reaches the network: the production
// recorded handler is the innermost handler.
internal sealed class RecordedHttpHandler : HttpMessageHandler
{
    private readonly RecordedDirectoryHandler _recordings;
    private readonly HttpMessageInvoker _invoker;
    private readonly ConcurrentQueue<SeenRequest> _seen = new();

    private RecordedHttpHandler(IReadOnlyList<RecordedExchange> exchanges)
    {
        _recordings = new RecordedDirectoryHandler(exchanges);
        _invoker = new HttpMessageInvoker(_recordings, disposeHandler: false);
    }

    // src/adapters/azure/tests/Product.Azure.Tests/Recorded
    public static string Folder => Path.Combine(TestPaths.Adapter, "tests", "Product.Azure.Tests", "Recorded");

    public IReadOnlyList<SeenRequest> Requests => _seen.ToArray();

    // The recorded files (graph and power-platform), with inline recordings first so a test can override a file.
    public static RecordedHttpHandler Create(params string[] inlineRecordings)
    {
        var exchanges = inlineRecordings.Select((json, i) => RecordedExchange.Parse(json, "inline-" + i)).ToList();
        exchanges.AddRange(RecordedExchange.LoadFolder(Folder));
        return new RecordedHttpHandler(exchanges);
    }

    // Only the inline recordings, for tests that script every answer.
    public static RecordedHttpHandler Only(params string[] inlineRecordings)
    {
        return new RecordedHttpHandler(inlineRecordings.Select((json, i) => RecordedExchange.Parse(json, "inline-" + i)).ToList());
    }

    // An inline recording: one request and its responses in order.
    public static string Exchange(string method, string url, params (int Status, string? Body, string? RetryAfter)[] responses)
    {
        var builder = new StringBuilder();
        builder.Append("{\"request\":{\"method\":\"").Append(method).Append("\",\"url\":").Append(System.Text.Json.JsonSerializer.Serialize(url)).Append("},\"responses\":[");
        for (int i = 0; i < responses.Length; i++)
        {
            (int status, string? body, string? retryAfter) = responses[i];
            builder.Append(i == 0 ? string.Empty : ",").Append("{\"status\":").Append(status);
            if (retryAfter != null)
            {
                builder.Append(",\"headers\":{\"Retry-After\":\"").Append(retryAfter).Append("\"}");
            }

            if (body != null)
            {
                builder.Append(",\"body\":").Append(body);
            }

            builder.Append('}');
        }

        return builder.Append("]}").ToString();
    }

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? body = request.Content == null ? null : Encoding.UTF8.GetString(request.Content.ReadAsByteArrayAsync(cancellationToken).GetAwaiter().GetResult());

        string url = request.RequestUri?.AbsoluteUri ?? string.Empty;
        bool recorded = _recordings.Find(request.Method.Method, url, body) != null;
        _seen.Enqueue(new SeenRequest(request.Method.Method, url, request.Headers.Authorization?.ToString(), body, recorded));
        return _invoker.Send(request, cancellationToken);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(Send(request, cancellationToken));
    }

    protected override void Dispose(bool disposing)
    {
        // The handler outlives the HTTP client factory's handler rotation in a test; nothing to release.
    }
}

// Every log entry at every level, so a test can prove that no token reaches a log.
internal sealed class TraceLogs : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _entries = new();

    public IReadOnlyList<string> Entries => _entries.ToArray();

    public string All => string.Join("\n", _entries);

    public ILogger CreateLogger(string categoryName)
    {
        return new Logger(this, categoryName);
    }

    public void Dispose()
    {
    }

    private sealed class Logger : ILogger
    {
        private readonly TraceLogs _owner;
        private readonly string _category;

        public Logger(TraceLogs owner, string category)
        {
            _owner = owner;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel != LogLevel.None;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _owner._entries.Enqueue(logLevel + " " + _category + ": " + formatter(state, exception) + (exception == null ? string.Empty : "\n" + exception));
        }
    }
}

// The readers over a recorded handler and a fake token source, with no host: the reader-level tests.
internal sealed class DirectoryRig : IDisposable
{
    private readonly ILoggerFactory _loggers;

    private DirectoryRig(RecordedHttpHandler handler, FakeTokenSource tokens, DirectoryOptions options, IHttpContextAccessor accessor)
    {
        Handler = handler;
        Tokens = tokens;
        Logs = new TraceLogs();
        _loggers = LoggerFactory.Create(builder => builder.AddProvider(Logs).SetMinimumLevel(LogLevel.Trace));
        Http = new DirectoryHttp(new OneClientFactory(handler), tokens, Options.Create(options), accessor, _loggers.CreateLogger<DirectoryHttp>())
        {
            Wait = delay => Waits.Add(delay),
        };
        var auth = new ProductAuthOptions { TenantId = Personas.Tenant, ClientId = Personas.Audience };
        Graph = new GraphDirectoryReader(Http, Options.Create(auth), Options.Create(options));
        Inventory = new PowerPlatformInventoryReader(Http, Options.Create(options));
    }

    public RecordedHttpHandler Handler { get; }

    public FakeTokenSource Tokens { get; }

    public TraceLogs Logs { get; }

    public DirectoryHttp Http { get; }

    public GraphDirectoryReader Graph { get; }

    public PowerPlatformInventoryReader Inventory { get; }

    public List<TimeSpan> Waits { get; } = new();

    public static DirectoryRig Create(RecordedHttpHandler handler, DirectoryOptions? options = null, IHttpContextAccessor? accessor = null)
    {
        return new DirectoryRig(handler, new FakeTokenSource(), options ?? new DirectoryOptions { Mode = DirectoryReadMode.Live }, accessor ?? new HttpContextAccessor());
    }

    // A signed-in person, as the MCP core hands the reader its caller.
    public static BackendCaller Person(string objectId, params string[] directoryRoleIds)
    {
        return new BackendCaller(objectId, PrincipalKind.User, Personas.ClaudeCode, new[] { Personas.PlatformAdminGroup }, directoryRoleIds);
    }

    public void Dispose()
    {
        _loggers.Dispose();
    }

    private sealed class OneClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public OneClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(_handler, disposeHandler: false);
        }
    }
}
