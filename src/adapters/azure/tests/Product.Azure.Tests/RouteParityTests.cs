using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Product.Azure.Host;
using Xunit;

namespace Product.Azure.Tests;

// The Azure Functions wrapper and the local server answer the same routes. Both map them with MapProduct, but they build
// the pipeline differently: the local server on its WebApplication (UseProductPipeline), the Functions app as one request
// delegate (ProductPipeline.Build) that its catch-all function calls after clearing the worker's own endpoint. These
// tests check the route table, that the function's trigger accepts every method the table maps under an empty route
// prefix, and that the Functions-style delegate answers each route as the local server does.
public sealed class RouteParityTests
{
    private static readonly string[] CoreRoutes =
    {
        "DELETE /mcp",
        "GET /.well-known/oauth-protected-resource",
        "GET /.well-known/oauth-protected-resource/mcp",
        "GET /api/info",
        "GET /api/me",
        "GET /app",
        "GET /app/config.json",
        "GET /app/{**path}",
        "GET /health/live",
        "GET /health/ready",
        "GET /mcp",
        "POST /api/deployments",
        "POST /api/deployments/corrections",
        "POST /api/{tool}",
        "POST /engine/v1/approval-records/{id}/outcome",
        "POST /engine/v1/approval-runner/claims",
        "POST /engine/v1/approval-runner/items/{id}/release",
        "POST /engine/v1/approval-runner/items/{id}/sent",
        "POST /mcp",
    };

    [Theory]
    [InlineData("core")]
    [InlineData("sdk")]
    public async Task TheRouteTableIsTheDocumentedOneAndTheFunctionTriggerTakesEveryMethod(string transport)
    {
        await using ProductTestHost host = ProductTestHost.Start(Transport(transport));

        // The SDK's stateless transport maps only POST; routing then answers GET and DELETE with 405, as the core does.
        string[] routes = Routes(host.Services).ToArray();
        Assert.Equal(transport == "sdk" ? CoreRoutes.Where(r => r != "GET /mcp" && r != "DELETE /mcp") : CoreRoutes, routes);

        // The function: anonymous at the Functions level, a catch-all route, and every method the table maps.
        string function = File.ReadAllText(Path.Combine(TestPaths.Adapter, "Product.Azure.Functions", "ProductFunction.cs"));
        Match trigger = Regex.Match(function, @"\[HttpTrigger\(AuthorizationLevel\.Anonymous,(?<methods>[^\]]*?)Route = ""\{\*path\}""\)\]");
        Assert.True(trigger.Success, "ProductFunction has one anonymous HTTP trigger with the route {*path}.");
        string[] methods = Regex.Matches(trigger.Groups["methods"].Value, "\"([a-z]+)\"").Select(m => m.Groups[1].Value.ToUpperInvariant()).ToArray();
        Assert.All(routes.Select(r => r.Split(' ')[0]).Distinct(), m => Assert.Contains(m, methods));

        // host.json keeps the paths as they are: no "api" route prefix in front of /mcp and /.well-known.
        JsonNode hostJson = JsonNode.Parse(File.ReadAllText(Path.Combine(TestPaths.Adapter, "Product.Azure.Functions", "host.json")))!;
        Assert.Equal(string.Empty, hostJson["extensions"]!["http"]!["routePrefix"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("core")]
    [InlineData("sdk")]
    public async Task TheFunctionsDelegateAnswersEveryRouteAsTheLocalServerDoes(string transport)
    {
        await using ProductTestHost host = ProductTestHost.Start(Transport(transport));
        RequestDelegate functions = ProductPipeline.Build(host.Services);
        string toolsList = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}";
        var requests = new List<(string Method, string Path, Persona? Persona, string? Body, bool CompareBody)>
        {
            ("GET", "/health/live", null, null, true),
            ("GET", "/health/ready", null, null, true),
            ("GET", "/.well-known/oauth-protected-resource/mcp", null, null, true),
            ("GET", "/.well-known/oauth-protected-resource", null, null, true),
            ("POST", "/mcp", null, toolsList, true),
            ("POST", "/mcp", Personas.Priya, toolsList, true),
            ("POST", "/mcp", Personas.Pipeline, toolsList, true),
            ("GET", "/api/me", Personas.Priya, null, true),
            ("GET", "/api/info", Personas.Priya, null, true),
            ("GET", "/api/me", Personas.Pipeline, null, false),
            ("POST", "/api/find_applications", Personas.Mateo, "{\"text\":\"inspection\"}", true),
            ("POST", "/api/save_intake_draft", Personas.Priya, "{\"title\":\"x\",\"need\":\"y\",\"proposedKindKey\":\"power-platform-app\"}", false),
            ("POST", "/api/deployments", Personas.Priya, "{}", false),
            ("POST", "/api/deployments/corrections", Personas.Nadia, "{}", false),
            ("POST", "/engine/v1/approval-records/00000000-0000-4000-9000-000000000001/outcome", Personas.Priya, "{}", false),
            ("POST", "/engine/v1/approval-records/00000000-0000-4000-9000-000000000001/outcome", Personas.Runner, "{}", false),
            ("POST", "/engine/v1/approval-runner/claims", Personas.Runner, "{}", true),
            ("POST", "/engine/v1/approval-runner/claims", Personas.Priya, "{}", false),
            ("GET", "/api/me", Personas.NadiaWeb, null, true),
            ("POST", "/api/get_portfolio_summary", Personas.NadiaWeb, "{}", true),
            ("POST", "/api/save_intake_draft", Personas.PriyaWeb, "{\"title\":\"Depot key register\",\"need\":\"Keys go missing.\",\"proposedKindKey\":\"power-platform-app\"}", false),
            ("POST", "/api/answer_approval", Personas.Priya, "{\"approvalRecordId\":\"00000000-0000-4000-9000-000000000001\",\"answer\":\"approve\"}", true),
            ("POST", "/api/answer_approval", Personas.PriyaWeb, "{\"approvalRecordId\":\"00000000-0000-4000-9000-000000000001\",\"answer\":\"approve\"}", true),
            ("GET", "/app/", null, null, false),
            ("GET", "/app/config.json", null, null, true),
            ("GET", "/no/such/path", Personas.Priya, null, false),
            ("GET", "/mcp", Personas.Priya, null, false),
            ("DELETE", "/mcp", Personas.Priya, null, false),
        };

        foreach ((string method, string path, Persona? persona, string? body, bool compareBody) in requests)
        {
            HttpCall local = await host.Send(new HttpMethod(method), path, persona, body, r =>
            {
                if (path == "/mcp")
                {
                    r.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
                }
            });
            (int status, string answer) = await ThroughFunctionsDelegate(host, functions, method, path, persona, body);

            string label = method + " " + path + " as " + (persona?.Name ?? "no one");
            Assert.True(local.Status == status, label + ": the local server answered " + local.Status + ", the Functions delegate " + status + ".\n" + local.Body + "\n" + answer);
            string? localJson = JsonOf(local.Body);
            if (compareBody && localJson != null)
            {
                string? answerJson = JsonOf(answer);
                Assert.True(answerJson != null && JsonText.Canonical(localJson) == JsonText.Canonical(answerJson), label + ": the answers differ.\n" + local.Body + "\n" + answer);
                Assert.True(local.Status != 200 || JsonNode.Parse(localJson)!["error"] == null, label + ": a 200 answer carries a JSON-RPC error.\n" + local.Body);
            }
        }
    }

    // ---- Helpers ----

    // The JSON of an answer: the body itself, or the data of the one server-sent event the SDK transport answers with.
    private static string? JsonOf(string body)
    {
        string trimmed = body.TrimStart();
        if (trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            return trimmed;
        }

        string[] data = body.Replace("\r\n", "\n").Split('\n')
            .Where(l => l.StartsWith("data:", StringComparison.Ordinal))
            .Select(l => l.Substring("data:".Length).TrimStart())
            .ToArray();
        return data.Length == 1 && data[0].StartsWith("{", StringComparison.Ordinal) ? data[0] : null;
    }

    // Every route the host maps, as "METHOD /pattern", sorted.
    private static IEnumerable<string> Routes(IServiceProvider services)
    {
        return services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(e => (e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? new[] { "ANY" }).Select(m => m + " /" + e.RoutePattern.RawText!.Trim('/')))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(r => r, StringComparer.Ordinal);
    }

    // One request through the delegate the Functions app builds, the way ProductFunction hands it over: a fresh scope,
    // no endpoint and no route values from the worker's routing.
    private static async Task<(int Status, string Body)> ThroughFunctionsDelegate(ProductTestHost host, RequestDelegate pipeline, string method, string path, Persona? persona, string? body)
    {
        using IServiceScope scope = host.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Method = method;
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost");
        context.Request.Path = path;
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Headers["Accept"] = "application/json, text/event-stream";
        if (persona != null)
        {
            context.Request.Headers[ProductTestHost.DevHeader] = persona.Name;
        }

        if (body != null)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bytes.Length;
            context.Request.Body = new MemoryStream(bytes);
        }

        var response = new MemoryStream();
        context.Response.Body = response;
        context.SetEndpoint(null);
        context.Request.RouteValues.Clear();
        await pipeline(context);
        return (context.Response.StatusCode, Encoding.UTF8.GetString(response.ToArray()));
    }

    private static HostSettings Transport(string transport)
    {
        var settings = new HostSettings();
        settings.Settings["Product:Mcp:Transport"] = transport;
        return settings;
    }
}
