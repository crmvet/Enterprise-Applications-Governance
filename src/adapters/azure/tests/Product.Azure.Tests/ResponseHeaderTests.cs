using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Product.Azure.Host;
using Xunit;

namespace Product.Azure.Tests;

// The headers every answer carries: no framing, no referrer, no reading by another origin's page, no type sniffing, and
// outside the web app a policy that lets nothing load. The web app's page keeps its own policy.
public sealed class ResponseHeaderTests
{
    [Fact]
    public async Task EveryAnswerRefusesFramingReferrersAndCrossOriginReads()
    {
        await using ProductTestHost host = ProductTestHost.Start(RequestLimitTests.Limits(mcp: 3));

        var calls = new List<(string Name, HttpCall Call)>
        {
            ("me", await host.Get("/api/me", Personas.Priya)),
            ("me anonymous", await host.Get("/api/me")),
            ("tool", await host.Post("/api/find_applications", Personas.NadiaWeb, "{\"text\":\"crew\"}")),
            ("mcp", await host.Mcp(Personas.Priya, "tools/list")),
            ("mcp anonymous", await host.Mcp(null, "tools/list")),
            ("live", await host.Get("/health/live")),
            ("ready", await host.Get("/health/ready")),
            ("metadata", await host.Get("/.well-known/oauth-protected-resource/mcp")),
            ("unknown path", await host.Get("/no-such-route")),
            ("mcp get", await host.Send(HttpMethod.Get, "/mcp", Personas.Priya)),
            ("record refused", await host.Post("/api/deployments", Personas.Priya, "{}")),
        };
        await host.Mcp(Personas.Priya, "ping");
        await host.Mcp(Personas.Priya, "ping");
        calls.Add(("over the limit", await host.Mcp(Personas.Priya, "ping")));
        Assert.Equal(429, calls[^1].Call.Status);

        foreach ((string name, HttpCall call) in calls)
        {
            Assert.True(call.Header("Content-Security-Policy") == ProductPipeline.DefaultContentSecurityPolicy, name + ": " + call.Header("Content-Security-Policy"));
            Assert.True(call.Header("X-Frame-Options") == "DENY", name);
            Assert.True(call.Header("Referrer-Policy") == "no-referrer", name);
            Assert.True(call.Header("Cross-Origin-Resource-Policy") == "same-origin", name);
            Assert.True(call.Header("X-Content-Type-Options") == "nosniff", name);
        }
    }

    [Fact]
    public async Task TheWebAppPageKeepsItsOwnPolicyAndItsFilesGetNoPolicy()
    {
        string folder = TestPaths.NewScratchFolder();
        try
        {
            string root = Path.Combine(folder, "app");
            Directory.CreateDirectory(Path.Combine(root, "assets"));
            File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html><html lang=\"en\"><head><title>Product</title></head><body><div id=\"root\"></div></body></html>");
            File.WriteAllText(Path.Combine(root, "assets", "index-Ab12Cd34.js"), "console.log('web app');");
            var settings = new HostSettings { Seed = false, WebApp = true };
            settings.Settings["Product:WebApp:RootPath"] = root;
            await using ProductTestHost host = ProductTestHost.Start(settings);

            HttpCall page = await host.Get("/app/");
            Assert.Equal(200, page.Status);
            Assert.Equal(ProductWebApp.ContentSecurityPolicy("https://login.microsoftonline.com"), page.Header("Content-Security-Policy"));
            Assert.Equal("DENY", page.Header("X-Frame-Options"));
            Assert.Equal("no-referrer", page.Header("Referrer-Policy"));
            Assert.Equal("same-origin", page.Header("Cross-Origin-Resource-Policy"));

            HttpCall script = await host.Get("/app/assets/index-Ab12Cd34.js");
            Assert.Equal(200, script.Status);
            Assert.Null(script.Header("Content-Security-Policy"));
            Assert.Equal("DENY", script.Header("X-Frame-Options"));
            Assert.Equal("same-origin", script.Header("Cross-Origin-Resource-Policy"));
        }
        finally
        {
            TestPaths.TryDelete(folder);
        }
    }
}
