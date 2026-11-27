using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Product.Azure.Host;
using Product.Azure.Stores;
using Product.Azure.Stores.Sqlite;
using Product.Engine.Runtime.Ports;
using Product.Engine.Testing.InMemory;
using Product.Mcp.Core.Backend;
using Xunit;

namespace Product.Azure.Tests;

// How one test host runs.
internal sealed class HostSettings
{
    public string Environment { get; set; } = "Development";

    // Product:DevIdentity:Enabled for the run.
    public bool DevIdentity { get; set; } = true;

    // The SQLite file. Null makes a new file in a scratch folder.
    public string? DatabaseFile { get; set; }

    // Repeatable row IDs (the MCP core's GUID wrapper sequence) instead of random GUIDs.
    public bool SequentialIds { get; set; } = true;

    // Seed the demo (version 1 and the walkthrough estate) when the file is new.
    public bool Seed { get; set; } = true;

    // More client binding operations for version 1, as a JSON array, after the demo binding's own (for example an
    // Approval Rule that needs two answers). Null seeds the demo binding as it is.
    public string? BindingOperations { get; set; }

    // Configuration operations for version 1, as a JSON array, in a draft patch after the binding (for example a data
    // domain tree). Null seeds the demo configuration as it is.
    public string? DraftOperations { get; set; }

    public DateTime Now { get; set; } = DemoSeed.Start;

    // The remote address every request appears to come from (an in-memory test server has none). Null leaves it unset.
    public IPAddress? RemoteAddress { get; set; } = IPAddress.Loopback;

    // Register the walkthrough's inventory page as the live inventory reader.
    public bool Inventory { get; set; } = true;

    // Name the fictional web app client (Personas.WebApp) in Product:WebApp:ClientIds, so its tokens are the person entry.
    public bool WebAppClient { get; set; } = true;

    // Serve the web app (Product:WebApp:Enabled). The local server's Development settings switch it on for local runs;
    // the tests switch it off unless a test asks for it.
    public bool WebApp { get; set; }

    // More configuration, as command line settings of the run.
    public Dictionary<string, string> Settings { get; } = new(StringComparer.OrdinalIgnoreCase);

    // More test services, registered after the host's own (for example a static signing key for Entra sign-in).
    public Action<IServiceCollection>? Services { get; set; }
}

// The answer to one HTTP request.
internal sealed class HttpCall
{
    public HttpCall(int status, string body, IReadOnlyDictionary<string, string> headers)
    {
        Status = status;
        Body = body;
        Headers = headers;
    }

    public int Status { get; }

    public string Body { get; }

    public IReadOnlyDictionary<string, string> Headers { get; }

    public JsonNode? Json => Body.Length == 0 ? null : JsonNode.Parse(Body);

    public string? Header(string name)
    {
        return Headers.TryGetValue(name, out string? value) ? value : null;
    }

    public override string ToString()
    {
        return Status.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + Body;
    }
}

// The local server (Product.Azure.Server) on the in-memory test server: its own Program, its own pipeline and services,
// with a SQLite file in a scratch folder, a manual clock, repeatable correlation IDs and row IDs, the walkthrough's
// inventory page, and fictional personas for the development identity. Nothing listens on a port.
internal sealed class ProductTestHost : IAsyncDisposable
{
    public const string DevHeader = "X-Product-Dev-Identity";

    private readonly ProductFactory _factory;
    private readonly string? _scratch;

    private ProductTestHost(HostSettings settings, string databaseFile, string? scratch)
    {
        Settings = settings;
        DatabaseFile = databaseFile;
        _scratch = scratch;
        Clock = new ManualClock(settings.Now);
        RemoteAddress = settings.RemoteAddress;
        _factory = new ProductFactory(this);
        Client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("http://localhost") });
    }

    public HostSettings Settings { get; }

    public string DatabaseFile { get; }

    public ManualClock Clock { get; }

    public HttpClient Client { get; }

    public IServiceProvider Services => _factory.Services;

    // The remote address of the next requests. Tests may change it between requests.
    public IPAddress? RemoteAddress { get; set; }

    public CapturedLogs Logs { get; } = new();

    public static ProductTestHost Start(HostSettings? settings = null)
    {
        settings ??= new HostSettings();
        string? scratch = null;
        string file = settings.DatabaseFile ?? Path.Combine(scratch = TestPaths.NewScratchFolder(), "product.db");
        if (settings.Seed && !File.Exists(file))
        {
            using var store = new SqliteProductStore(new SqliteStoreOptions { ConnectionString = ConnectionString(file), NewSeedId = RowIds.EntityCounter() });
            store.EnsureSchema();
            DemoSeed.Into(store, settings.BindingOperations, settings.DraftOperations);
        }

        return new ProductTestHost(settings, file, scratch);
    }

    public static string ConnectionString(string file)
    {
        return "Data Source=" + file;
    }

    public T Get<T>()
        where T : notnull
    {
        return Services.GetRequiredService<T>();
    }

    // One request. A persona signs in through the development identity header.
    public async Task<HttpCall> Send(HttpMethod method, string path, Persona? persona = null, string? json = null, Action<HttpRequestMessage>? configure = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (persona != null)
        {
            request.Headers.TryAddWithoutValidation(DevHeader, persona.Name);
        }

        if (json != null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        configure?.Invoke(request);
        using HttpResponseMessage response = await Client.SendAsync(request, TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, IEnumerable<string>> header in response.Headers.Concat(response.Content.Headers))
        {
            headers[header.Key] = string.Join(", ", header.Value);
        }

        return new HttpCall((int)response.StatusCode, body, headers);
    }

    public Task<HttpCall> Post(string path, Persona? persona, string json, Action<HttpRequestMessage>? configure = null)
    {
        return Send(HttpMethod.Post, path, persona, json, configure);
    }

    public Task<HttpCall> Get(string path, Persona? persona = null)
    {
        return Send(HttpMethod.Get, path, persona);
    }

    // One JSON-RPC request to the MCP endpoint.
    public Task<HttpCall> Mcp(Persona? persona, string method, string? parametersJson = null, int id = 1)
    {
        string body = "{\"jsonrpc\":\"2.0\",\"id\":" + id.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"method\":\"" + method + "\"" + (parametersJson == null ? string.Empty : ",\"params\":" + parametersJson) + "}";
        return Post("/mcp", persona, body);
    }

    // A tools/call on the MCP endpoint; returns the envelope (structured content), or fails the test when there is none.
    public async Task<JsonNode> Call(Persona persona, string tool, string argumentsJson = "{}")
    {
        HttpCall call = await Mcp(persona, "tools/call", "{\"name\":\"" + tool + "\",\"arguments\":" + argumentsJson + "}");
        Assert.True(call.Status == 200, tool + " answered " + call);
        return call.Json?["result"]?["structuredContent"] ?? throw new InvalidOperationException("No structured content from " + tool + ": " + call.Body);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
        if (_scratch != null)
        {
            SqliteConnection.ClearAllPools();
            TestPaths.TryDelete(_scratch);
        }
    }

    private sealed class ProductFactory : WebApplicationFactory<Program>
    {
        private readonly ProductTestHost _host;

        public ProductFactory(ProductTestHost host)
        {
            _host = host;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            HostSettings settings = _host.Settings;
            builder.UseContentRoot(TestPaths.ServerProject);
            builder.UseEnvironment(settings.Environment);
            foreach (KeyValuePair<string, string> setting in BaseSettings(settings).Concat(settings.Settings))
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.ConfigureLogging(logging => logging.AddProvider(_host.Logs));
            builder.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<IEngineClock>(_host.Clock));
                services.Replace(ServiceDescriptor.Singleton<IRequestCorrelation>(RequestCorrelation.Sequential()));
                if (settings.Inventory)
                {
                    services.AddSingleton<IInventoryReader>(new WalkthroughInventory());
                }

                services.AddSingleton<IProductStore>(_ =>
                {
                    var store = new SqliteProductStore(new SqliteStoreOptions
                    {
                        ConnectionString = ConnectionString(_host.DatabaseFile),
                        NewId = settings.SequentialIds ? RowIds.Sequential() : null,
                        NewSeedId = RowIds.EntityCounter(),
                    });
                    store.EnsureSchema();
                    return store;
                });
                services.AddSingleton<IStartupFilter>(new RemoteAddressFilter(_host));
                settings.Services?.Invoke(services);
            });
        }

        private static IEnumerable<KeyValuePair<string, string>> BaseSettings(HostSettings settings)
        {
            yield return Pair("Logging:LogLevel:Default", "Warning");
            yield return Pair("Product:PublicBaseUrl", "https://mcp.example.org");
            yield return Pair("Product:RecordUrlBase", "https://product.example.org/records");
            yield return Pair("Product:TelemetrySalt", "fixture-host-telemetry-salt-0001");
            yield return Pair("Product:ValidateOutputs", "true");
            yield return Pair("Product:Auth:TenantId", Personas.Tenant);
            yield return Pair("Product:Store:Provider", "sqlite");
            yield return Pair("Product:Store:Sqlite:ConnectionString", ConnectionString(settings.DatabaseFile ?? "unused.db"));
            yield return Pair("Product:DevIdentity:Enabled", settings.DevIdentity ? "true" : "false");
            yield return Pair("Product:WebApp:Enabled", settings.WebApp ? "true" : "false");
            if (settings.WebAppClient)
            {
                yield return Pair("Product:WebApp:ClientIds:0", Personas.WebApp);
            }
            foreach (Persona persona in Personas.All)
            {
                foreach (KeyValuePair<string, string> setting in persona.Settings())
                {
                    yield return setting;
                }
            }
        }

        private static KeyValuePair<string, string> Pair(string key, string value)
        {
            return new KeyValuePair<string, string>(key, value);
        }
    }

    // Gives each request the remote address the host names: the in-memory test server has none, and the development
    // identity takes only loopback requests.
    private sealed class RemoteAddressFilter : IStartupFilter
    {
        private readonly ProductTestHost _host;

        public RemoteAddressFilter(ProductTestHost host)
        {
            _host = host;
        }

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.Use((context, nextMiddleware) =>
                {
                    if (_host.RemoteAddress != null)
                    {
                        context.Connection.RemoteIpAddress = _host.RemoteAddress;
                    }

                    return nextMiddleware(context);
                });
                next(app);
            };
        }
    }
}

// Warnings and errors the host logged, so a test can show why a request failed.
internal sealed class CapturedLogs : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _entries = new();

    public IReadOnlyList<string> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName)
    {
        return new Logger(this, categoryName);
    }

    public void Dispose()
    {
    }

    public override string ToString()
    {
        return string.Join("\n", _entries);
    }

    private sealed class Logger : ILogger
    {
        private readonly CapturedLogs _owner;
        private readonly string _category;

        public Logger(CapturedLogs owner, string category)
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
            return logLevel >= LogLevel.Warning;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                _owner._entries.Enqueue(logLevel + " " + _category + ": " + formatter(state, exception) + (exception == null ? string.Empty : "\n" + exception));
            }
        }
    }
}
