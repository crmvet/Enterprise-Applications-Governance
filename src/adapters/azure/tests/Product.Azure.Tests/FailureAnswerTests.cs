using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Product.Azure.Host;
using Xunit;

namespace Product.Azure.Tests;

// A server failure on a product route answers a failure envelope that names only the request's correlation ID. The
// exception's message, which may quote request data or a store's internals, never reaches the answer, and outside
// Development it never reaches the log either: the log keeps the exception's type and stack, as the MCP endpoint does.
public sealed class FailureAnswerTests
{
    private const string ProbePath = "/api/probe-failure";
    private const string Secret = "Server=tcp:sql.example.org;Password=placeholder-not-a-secret;record 7f3c";

    [Fact]
    public async Task AFailureOutsideDevelopmentAnswersOnlyTheCorrelationIdAndLogsNoMessage()
    {
        var settings = new HostSettings { Environment = "Production", DevIdentity = false, Seed = false, Inventory = false };
        settings.Settings["Product:Auth:ClientId"] = Personas.Audience;
        settings.Services = services => services.AddProductEndpointModule<FailingModule>();
        await using ProductTestHost host = ProductTestHost.Start(settings);

        HttpCall call = await host.Send(HttpMethod.Get, ProbePath);

        AssertFailureAnswer(call);
        Assert.Contains(host.Logs.Entries, e => e.Contains(call.Header(ProductRequest.CorrelationHeader)!, StringComparison.Ordinal) && e.Contains(nameof(InvalidOperationException), StringComparison.Ordinal));
        Assert.DoesNotContain(host.Logs.Entries, e => e.Contains("placeholder-not-a-secret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFailureInDevelopmentLogsTheWholeExceptionButAnswersTheSame()
    {
        var settings = new HostSettings { Seed = false };
        settings.Services = services => services.AddProductEndpointModule<FailingModule>();
        await using ProductTestHost host = ProductTestHost.Start(settings);

        HttpCall call = await host.Send(HttpMethod.Get, ProbePath);

        AssertFailureAnswer(call);
        Assert.Contains(host.Logs.Entries, e => e.Contains("placeholder-not-a-secret", StringComparison.Ordinal));
    }

    private static void AssertFailureAnswer(HttpCall call)
    {
        Assert.True(call.Status == 500, call.ToString());
        Assert.DoesNotContain("placeholder-not-a-secret", call.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(InvalidOperationException), call.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", call.Body, StringComparison.Ordinal);
        Assert.Equal("application/json; charset=utf-8", call.Header("Content-Type"));
        Assert.Equal("nosniff", call.Header("X-Content-Type-Options"));
        Assert.Equal("no-store", call.Header("Cache-Control"));
        string correlation = call.Header(ProductRequest.CorrelationHeader)!;
        Assert.False(string.IsNullOrEmpty(correlation));
        Assert.False(call.Json!["ok"]!.GetValue<bool>());
        Assert.Equal(correlation, call.Json["correlationId"]!.GetValue<string>());
        Assert.Equal("upstream_unavailable", call.Json["error"]!["code"]!.GetValue<string>());
        Assert.Contains(correlation, call.Json["error"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    // A route that fails the way a store can: an exception whose message quotes a connection detail.
    private sealed class FailingModule : IProductEndpointModule
    {
        public void Map(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet(ProbePath, (RequestDelegate)(_ => throw new InvalidOperationException(Secret)));
        }
    }
}
