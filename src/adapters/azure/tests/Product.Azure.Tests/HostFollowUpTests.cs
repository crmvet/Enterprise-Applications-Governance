using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Product.Azure.Host;
using Product.Mcp.Core.Telemetry;
using Xunit;

namespace Product.Azure.Tests;

// Wave 4, the list reads and speed slice: the host follow-ups. A request the client canceled is one information line,
// never a server failure; the telemetry lines always name a caller (by hash); Strict-Transport-Security on every https
// answer but a loopback one; /mcp answers 401 to any message before sign-in; the person's local day reaches the tools.
public sealed class HostFollowUpTests
{
    private const string Ping = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}";

    // ---- Canceled requests (wave 3 QA: "Unexpected end of request content" was logged as a failure) ----

    [Fact]
    public async Task ARequestTheClientCanceledIsOneInformationLineAndNoFailure()
    {
        var logs = new ListLogger();

        // The body ended early: Kestrel's own 400, answered as 400, logged at information with no stack.
        DefaultHttpContext cut = Context(logs);
        await ProductPipeline.FailureGuard(cut, _ => throw new Microsoft.AspNetCore.Http.BadHttpRequestException("Unexpected end of request content.", StatusCodes.Status400BadRequest));
        Assert.Equal(400, cut.Response.StatusCode);

        // The browser went away: any exception after the abort is the abort's, and nothing is answered.
        DefaultHttpContext gone = Context(logs);
        using var abort = new CancellationTokenSource();
        gone.RequestAborted = abort.Token;
        await abort.CancelAsync();
        await ProductPipeline.FailureGuard(gone, _ => throw new IOException("The client reset the connection."));
        await ProductPipeline.FailureGuard(gone, _ => throw new OperationCanceledException());

        Assert.DoesNotContain(logs.Entries, e => e.Level >= LogLevel.Warning);
        Assert.Equal(3, logs.Entries.Count(e => e.Level == LogLevel.Information));
        Assert.All(logs.Entries, e => Assert.DoesNotContain("   at ", e.Text, StringComparison.Ordinal));
        Assert.Contains(logs.Entries, e => e.Text.Contains("ended the body of request", StringComparison.Ordinal));
        Assert.Contains(logs.Entries, e => e.Text.Contains("canceled the request", StringComparison.Ordinal));

        // A real failure is still an error with the correlation ID.
        Assert.False(ProductPipeline.ClientWentAway(Context(logs), new InvalidOperationException("A server bug.")));
    }

    // ---- The caller field of the telemetry lines ----

    [Fact]
    public async Task WithoutAConfiguredSaltTheTelemetryStillNamesTheCallerByAHashOfThisProcess()
    {
        var events = new ConcurrentQueue<TelemetryEvent>();
        HostSettings settings = new();
        settings.Settings["Product:TelemetrySalt"] = string.Empty;
        settings.Services = services => services.Replace(ServiceDescriptor.Singleton<ITelemetrySink>(new Recorder(events)));
        await using ProductTestHost host = ProductTestHost.Start(settings);

        Assert.Equal(200, (await host.Post("/mcp", Personas.Priya, Ping)).Status);
        Assert.Equal(200, (await host.Post("/mcp", Personas.Priya, Ping)).Status);
        Assert.Equal(200, (await host.Post("/api/find_applications", Personas.PriyaWeb, "{\"text\":\"crew\"}")).Status);

        TelemetryEvent[] seen = events.ToArray();
        Assert.True(seen.Length >= 3);
        Assert.All(seen, e => Assert.False(string.IsNullOrEmpty(e.CallerHash)));
        Assert.Single(seen.Select(e => e.CallerHash).Distinct());
        Assert.DoesNotContain(seen, e => e.CallerHash!.Contains(Personas.PriyaId, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(host.Logs.Entries, l => l.Contains("No Product:TelemetrySalt is set", StringComparison.Ordinal));
    }

    // ---- Strict-Transport-Security ----

    [Theory]
    [InlineData("https", "product.example.org", null, true)]
    [InlineData("http", "product.example.org", "https", true)]
    [InlineData("http", "product.example.org", null, false)]
    [InlineData("https", "localhost", null, false)]
    [InlineData("http", "127.0.0.1", "https", false)]
    [InlineData("https", "[::1]", null, false)]
    public void EveryHttpsAnswerButALoopbackOneCarriesStrictTransportSecurity(string scheme, string hostName, string? forwarded, bool expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = scheme;
        context.Request.Host = new HostString(hostName);
        if (forwarded != null)
        {
            context.Request.Headers["X-Forwarded-Proto"] = forwarded;
        }

        Assert.Equal(expected, ProductPipeline.StrictTransportFor(context.Request, configured: false));

        // A host whose public URL is https outside Development sends it on every answer, as before.
        Assert.True(ProductPipeline.StrictTransportFor(context.Request, configured: true));
    }

    [Fact]
    public async Task ADevelopmentHostBehindAnHttpsFrontEndSendsStrictTransportSecurity()
    {
        await using ProductTestHost host = ProductTestHost.Start();

        HttpCall local = await host.Get("/health/live");
        Assert.Null(local.Header("Strict-Transport-Security"));

        HttpCall fronted = await host.Send(HttpMethod.Get, "/health/live", null, null, request =>
        {
            request.Headers.Host = "product.example.org";
            request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        });
        Assert.Equal(ProductPipeline.StrictTransportSecurity, fronted.Header("Strict-Transport-Security"));
    }

    // ---- /mcp before sign-in ----

    [Theory]
    [InlineData("core", "{}")]
    [InlineData("sdk", "{}")]
    [InlineData("core", "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}")]
    [InlineData("sdk", "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}")]
    public async Task TheMcpEndpointAnswers401ToAnyMessageBeforeSignIn(string transport, string message)
    {
        var settings = new HostSettings();
        settings.Settings["Product:Mcp:Transport"] = transport;
        await using ProductTestHost host = ProductTestHost.Start(settings);

        HttpCall call = await host.Post("/mcp", null, message, request => request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream"));
        Assert.True(call.Status == 401, transport + ": " + call);
        Assert.StartsWith("Bearer resource_metadata=", call.Header("WWW-Authenticate"), StringComparison.Ordinal);
    }

    // ---- The person's local day ----

    [Fact]
    public async Task ThePersonsLocalDayReachesTheDateTheServerStamps()
    {
        HostSettings settings = new();
        await using ProductTestHost host = ProductTestHost.Start(settings);
        string day = host.Clock.UtcNow.AddDays(-1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        HttpCall recorded = await host.Post("/api/record_document_link", Personas.NadiaWeb, "{\"businessApplicationId\":\"" + Personas.InspectionScheduler + "\",\"documentTypeKey\":\"wiki-page\","
            + "\"url\":\"https://devops.example.org/field-ops/wiki/inspection-scheduler\",\"clientRequestId\":\"local-day-link-1\"}");
        Assert.True(recorded.Status == 200, recorded + "\n" + host.Logs);
        string link = recorded.Json!["data"]!["documentLinkId"]!.GetValue<string>();

        HttpCall checkedToday = await host.Post("/api/confirm_document_checked", Personas.NadiaWeb, "{\"documentLinkId\":\"" + link + "\",\"stillTrue\":true}", request => request.Headers.TryAddWithoutValidation("X-Product-Local-Day", day));
        Assert.True(checkedToday.Status == 200, checkedToday + "\n" + host.Logs);
        Assert.Equal(day, checkedToday.Json!["data"]!["lastCheckedOn"]!.GetValue<string>());
    }

    // ---- Helpers ----

    private static DefaultHttpContext Context(ListLogger logs)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(new LoggerFactory(new[] { logs }));
        services.AddSingleton<IRequestCorrelation>(new RequestCorrelation());
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Request.Path = "/api/list_approvals";
        return context;
    }

    private sealed class Recorder : ITelemetrySink
    {
        private readonly ConcurrentQueue<TelemetryEvent> _events;

        public Recorder(ConcurrentQueue<TelemetryEvent> events)
        {
            _events = events;
        }

        public void Track(TelemetryEvent telemetryEvent)
        {
            _events.Enqueue(telemetryEvent);
        }
    }

    // Every log line at every level, with its level.
    private sealed class ListLogger : ILoggerProvider
    {
        private readonly ConcurrentQueue<(LogLevel Level, string Text)> _entries = new();

        public IReadOnlyList<(LogLevel Level, string Text)> Entries => _entries.ToArray();

        public ILogger CreateLogger(string categoryName)
        {
            return new Logger(this);
        }

        public void Dispose()
        {
        }

        private sealed class Logger : ILogger
        {
            private readonly ListLogger _owner;

            public Logger(ListLogger owner)
            {
                _owner = owner;
            }

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                _owner._entries.Enqueue((logLevel, formatter(state, exception) + (exception == null ? string.Empty : "\n" + exception)));
            }
        }
    }
}
