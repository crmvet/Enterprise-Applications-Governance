using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry.Instrumentation.Http;
using Product.Azure.Host;
using Product.Azure.Server;
using Product.Mcp.Core.Backend;
using Xunit;

namespace Product.Azure.Tests;

// The directory reads name people in their URL paths (GET /users/{object ID}). Their request logs are off already; HTTP
// client tracing records the whole path too, so with Azure Monitor switched on a person's object ID would reach every
// dependency trace of a directory read. Every directory request carries a marker, and the local server's HTTP client
// tracing leaves marked requests out. Telemetry names a caller only by the salted hash.
public sealed class TracePrivacyTests
{
    private const string PowerPlatformAdministrator = "11648597-926c-4cf3-9c36-bcebb0ba8dcc";

    // A connection string for an ingestion endpoint on a closed loopback port: the server is built, never started, and
    // nothing is sent anywhere.
    private const string LocalConnectionString = "InstrumentationKey=00000000-0000-4000-8000-0000000000ff;IngestionEndpoint=https://127.0.0.1:9/";

    [Fact]
    public void EveryDirectoryRequestCarriesTheMarker()
    {
        using DirectoryRig rig = DirectoryRig.Create(RecordedHttpHandler.Create());
        BackendCaller owen = DirectoryRig.Person(Personas.OwenId, PowerPlatformAdministrator);

        rig.Graph.UserState(owen, Personas.NadiaId);
        rig.Graph.Group(owen, Personas.ReaderGroup);
        rig.Graph.DirectAssignments(owen);
        rig.Inventory.Query(owen, DirectoryOptions.PowerPlatformEstate, null, null);

        Assert.Contains(rig.Handler.Requests, r => r.Url.Contains("/users/" + Personas.NadiaId, StringComparison.Ordinal));
        Assert.All(rig.Handler.Requests, r => Assert.True(r.DirectoryMarked, r.Method + " " + r.Url));
    }

    [Fact]
    public async Task TheServersHttpClientTracingLeavesDirectoryRequestsOut()
    {
        WebApplication app = ProductServer.Build(new[] { "--environment=Development", "--APPLICATIONINSIGHTS_CONNECTION_STRING=" + LocalConnectionString });
        await using (app.ConfigureAwait(false))
        {
            HttpClientTraceInstrumentationOptions options = app.Services.GetRequiredService<IOptionsMonitor<HttpClientTraceInstrumentationOptions>>().Get(Options.DefaultName);
            Assert.NotNull(options.FilterHttpRequestMessage);

            using var directory = new HttpRequestMessage(HttpMethod.Get, DirectoryOptions.GraphBaseUrl + "users/" + Personas.NadiaId + "?$select=id");
            directory.Options.Set(DirectoryHttp.RequestMarker, true);
            Assert.False(options.FilterHttpRequestMessage!(directory));

            using var other = new HttpRequestMessage(HttpMethod.Get, "https://login.microsoftonline.com/" + Personas.Tenant + "/v2.0/.well-known/openid-configuration");
            Assert.True(options.FilterHttpRequestMessage(other));
        }
    }
}
