using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Product.Azure.Host;
using Xunit;

namespace Product.Azure.Tests;

// The product's web app on the same origin as the API: the built files under /app/, the page for every page route, and
// /app/config.json written from the host's settings. The files here stand in for "npm run build:app" output; the page's
// own tests run in src/ui/harness (npm test).
public sealed class WebAppTests
{
    private const string Page = "<!doctype html><html><head><title>Product</title><script type=\"module\" src=\"/app/assets/index-Ab12Cd34.js\"></script></head><body><div id=\"root\"></div></body></html>";
    private const string Script = "console.log('web app');";
    private const string OutsideSecret = "a file next to the web app's folder";

    [Fact]
    public async Task SwitchedOffEveryWebAppPathAnswers404()
    {
        await using ProductTestHost host = ProductTestHost.Start(new HostSettings { Seed = false, WebApp = false });
        foreach (string path in new[] { "/app", "/app/", "/app/config.json", "/app/records/business-application/x" })
        {
            HttpCall call = await host.Get(path);
            Assert.True(call.Status == 404, path + " answered " + call);
            Assert.Equal("no-store", call.Header("Cache-Control"));
        }
    }

    [Fact]
    public async Task ThePageAndItsFilesAreServedWithTheirCachingAndPolicy()
    {
        string root = BuiltApp();
        await using ProductTestHost host = ProductTestHost.Start(Enabled(root));

        HttpCall bare = await host.Get("/app");
        Assert.Equal(308, bare.Status);
        Assert.Equal("/app/", bare.Header("Location"));

        // The page: never cached, framed by no one, scripts and calls only to this origin and the sign-in host.
        HttpCall page = await host.Get("/app/");
        Assert.Equal(200, page.Status);
        Assert.Equal(Page, page.Body);
        Assert.StartsWith("text/html", page.Header("Content-Type"), StringComparison.Ordinal);
        Assert.Equal("no-store", page.Header("Cache-Control"));
        Assert.Equal("nosniff", page.Header("X-Content-Type-Options"));
        Assert.Equal("DENY", page.Header("X-Frame-Options"));
        string policy = page.Header("Content-Security-Policy")!;
        Assert.Equal(ProductWebApp.ContentSecurityPolicy("https://login.microsoftonline.com"), policy);
        Assert.Contains("default-src 'self'", policy, StringComparison.Ordinal);
        Assert.Contains("script-src 'self';", policy, StringComparison.Ordinal);
        Assert.Contains("connect-src 'self' https://login.microsoftonline.com;", policy, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-eval", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("script-src 'self' 'unsafe-inline'", policy, StringComparison.Ordinal);

        // Every page route is the page: the record links the MCP answers carry open Application 360.
        foreach (string route in new[] { "/app/records/business-application/" + Personas.InspectionScheduler, "/app/map", "/app/timeline" })
        {
            HttpCall deep = await host.Get(route);
            Assert.True(deep.Status == 200 && deep.Body == Page, route + " answered " + deep);
            Assert.Equal("no-store", deep.Header("Cache-Control"));
        }

        // Hashed files are cached for a year; other files revalidate.
        HttpCall script = await host.Get("/app/assets/index-Ab12Cd34.js");
        Assert.Equal(200, script.Status);
        Assert.Equal(Script, script.Body);
        Assert.Contains("javascript", script.Header("Content-Type"), StringComparison.Ordinal);
        Assert.Equal("public, max-age=31536000, immutable", script.Header("Cache-Control"));
        Assert.Null(script.Header("Content-Security-Policy"));
        HttpCall icon = await host.Get("/app/favicon.svg");
        Assert.Equal(200, icon.Status);
        Assert.Equal("no-cache", icon.Header("Cache-Control"));

        // A file that is not there is not answered with the page.
        HttpCall missing = await host.Get("/app/assets/index-Missing1.js");
        Assert.Equal(404, missing.Status);
        Assert.DoesNotContain("<html", missing.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NothingOutsideTheBuiltFolderIsServed()
    {
        string root = BuiltApp();
        await using ProductTestHost host = ProductTestHost.Start(Enabled(root));
        string[] probes =
        {
            "/app/../outside.txt",
            "/app/%2e%2e/outside.txt",
            "/app/%2E%2E%2Foutside.txt",
            "/app/assets/..%2F..%2Foutside.txt",
            "/app/..%5Coutside.txt",
            "/app/.hidden",
            "/app/assets/.hidden",
            "/app/C:%5Cwindows%5Cwin.ini",
        };
        foreach (string probe in probes)
        {
            HttpCall call = await host.Get(probe);
            Assert.DoesNotContain(OutsideSecret, call.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("hidden file", call.Body, StringComparison.Ordinal);
            Assert.True(call.Status == 404 || (call.Status == 200 && call.Body == Page), probe + " answered " + call);
        }

        // config.json in the folder is never read: the host writes the page's configuration itself.
        HttpCall config = await host.Get("/app/config.json");
        Assert.DoesNotContain("from-disk", config.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheConfigurationNamesEntraSignInWithTheWebAppsClientAndTheApiScope()
    {
        string root = BuiltApp();
        HostSettings settings = Enabled(root);
        settings.DevIdentity = false;
        settings.Settings["Product:Auth:ClientId"] = Personas.Audience;
        await using ProductTestHost host = ProductTestHost.Start(settings);

        HttpCall call = await host.Get("/app/config.json");
        Assert.Equal(200, call.Status);
        Assert.Equal("no-store", call.Header("Cache-Control"));
        JsonNode config = call.Json!;
        Assert.Equal(1, config["schemaVersion"]!.GetValue<int>());
        Assert.Equal("/api", config["apiBase"]!.GetValue<string>());
        Assert.Equal("entra", config["auth"]!["mode"]!.GetValue<string>());
        Assert.Equal("https://login.microsoftonline.com/" + Personas.Tenant, config["auth"]!["authority"]!.GetValue<string>());
        Assert.Equal(Personas.WebApp, config["auth"]!["clientId"]!.GetValue<string>());
        Assert.Equal("api://" + Personas.Audience + "/api.access", config["auth"]!["apiScope"]!.GetValue<string>());
        Assert.Empty(config["development"]!["personas"]!.AsArray());

        // Nothing in it is a secret or a token: only these members.
        Assert.Equal(new[] { "apiBase", "auth", "development", "schemaVersion" }, config.AsObject().Select(m => m.Key).OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(new[] { "apiScope", "authority", "clientId", "mode" }, config["auth"]!.AsObject().Select(m => m.Key).OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public async Task ALocalRunOffersTheDevelopmentPersonasOnlyWithTheDevelopmentIdentityOn()
    {
        // The local server's Development settings (appsettings.Development.json) name the web app's personas.
        string root = BuiltApp();
        HostSettings settings = Enabled(root);
        await using (ProductTestHost host = ProductTestHost.Start(settings))
        {
            JsonNode config = (await host.Get("/app/config.json")).Json!;
            Assert.Equal("development", config["auth"]!["mode"]!.GetValue<string>());
            Assert.Null(config["auth"]!["clientId"]);
            string[] names = config["development"]!["personas"]!.AsArray().Select(p => p!["name"]!.GetValue<string>()).ToArray();
            Assert.Equal(new[] { "nadia-web", "priya-web", "owen-web", "mateo-web", "config-admin-web" }, names);
        }

        HostSettings off = Enabled(root);
        off.DevIdentity = false;
        await using (ProductTestHost host = ProductTestHost.Start(off))
        {
            JsonNode config = (await host.Get("/app/config.json")).Json!;
            Assert.Equal("entra", config["auth"]!["mode"]!.GetValue<string>());
            Assert.Empty(config["development"]!["personas"]!.AsArray());
        }
    }

    [Fact]
    public async Task TheFunctionsDelegateServesThePageAsTheLocalServerDoes()
    {
        string root = BuiltApp();
        HostSettings settings = Enabled(root);
        settings.Settings["Product:Auth:ClientId"] = Personas.Audience;
        await using ProductTestHost host = ProductTestHost.Start(settings);
        RequestDelegate functions = ProductPipeline.Build(host.Services);
        foreach (string path in new[] { "/app/", "/app/config.json", "/app/records/business-application/x", "/app/assets/index-Ab12Cd34.js", "/app/assets/none.js" })
        {
            HttpCall local = await host.Get(path);
            using var scope = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateScope(host.Services);
            var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            context.Request.Method = "GET";
            context.Request.Scheme = "http";
            context.Request.Host = new HostString("localhost");
            context.Request.Path = path;
            context.Connection.RemoteIpAddress = IPAddress.Loopback;
            var body = new MemoryStream();
            context.Response.Body = body;
            context.SetEndpoint(null);
            await functions(context);
            string answer = Encoding.UTF8.GetString(body.ToArray());
            Assert.True(local.Status == context.Response.StatusCode, path + ": " + local.Status + " and " + context.Response.StatusCode);
            Assert.Equal(local.Header("Content-Security-Policy"), string.IsNullOrEmpty(context.Response.Headers.ContentSecurityPolicy.ToString()) ? null : context.Response.Headers.ContentSecurityPolicy.ToString());
            if (path != "/app/config.json")
            {
                Assert.Equal(local.Body, answer);
            }
        }
    }

    [Fact]
    public void TheWebAppSettingsAreCheckedAtStartup()
    {
        string root = BuiltApp();
        HostSettings noClient = Production(("Product:WebApp:Enabled", "true"), ("Product:WebApp:RootPath", root));
        noClient.WebAppClient = false;
        Assert.Contains("Product:WebApp:ClientIds", Message(StartFails(noClient)), StringComparison.Ordinal);
        Assert.Contains("DevelopmentPersonas", Message(StartFails(Production(
            ("Product:WebApp:Enabled", "true"),
            ("Product:WebApp:RootPath", root),
            ("Product:WebApp:ClientIds:0", Personas.WebApp),
            ("Product:WebApp:DevelopmentPersonas:0:Name", "nadia-web"),
            ("Product:WebApp:DevelopmentPersonas:0:Label", "Nadia")))), StringComparison.Ordinal);
        Assert.Contains("API's own client ID", Message(StartFails(Production(("Product:WebApp:ClientIds:0", Personas.Audience)))), StringComparison.Ordinal);
        Assert.Contains("GUID", Message(StartFails(Production(("Product:WebApp:ClientIds:0", "web-app")))), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AProductionHostServesThePageWithEntraSignIn()
    {
        string root = BuiltApp();
        HostSettings settings = Production(("Product:WebApp:Enabled", "true"), ("Product:WebApp:RootPath", root), ("Product:WebApp:ClientIds:0", Personas.WebApp));
        settings.DatabaseFile = Path.Combine(TestPaths.NewScratchFolder(), "product.db");
        await using ProductTestHost host = ProductTestHost.Start(settings);
        JsonNode config = (await host.Get("/app/config.json")).Json!;
        Assert.Equal("entra", config["auth"]!["mode"]!.GetValue<string>());
        Assert.Equal(Personas.WebApp, config["auth"]!["clientId"]!.GetValue<string>());
        HttpCall page = await host.Get("/app/records/business-application/" + Personas.InspectionScheduler);
        Assert.Equal(200, page.Status);
        Assert.Contains("frame-ancestors 'none'", page.Header("Content-Security-Policy"), StringComparison.Ordinal);

        // The API next to it still needs a token.
        HttpCall me = await host.Get("/api/me");
        Assert.Equal(401, me.Status);

        // Outside Development, with an https public URL, the browser is told to use https only: the page keeps its
        // sign-in in session storage and must never load over plain HTTP.
        foreach (HttpCall call in new[] { page, me, await host.Get("/app/config.json"), await host.Get("/health/live") })
        {
            Assert.Equal(ProductPipeline.StrictTransportSecurity, call.Header("Strict-Transport-Security"));
        }
    }

    // ---- Helpers ----

    // A built web app in a scratch folder, with a secret file next to it and a config.json and a hidden file in it that
    // must never be served.
    private static string BuiltApp()
    {
        string folder = TestPaths.NewScratchFolder();
        string root = Path.Combine(folder, "app");
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        File.WriteAllText(Path.Combine(root, "index.html"), Page);
        File.WriteAllText(Path.Combine(root, "assets", "index-Ab12Cd34.js"), Script);
        File.WriteAllText(Path.Combine(root, "favicon.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\"/>");
        File.WriteAllText(Path.Combine(root, "config.json"), "{\"from-disk\":true}");
        File.WriteAllText(Path.Combine(root, ".hidden"), "hidden file");
        File.WriteAllText(Path.Combine(root, "assets", ".hidden"), "hidden file");
        File.WriteAllText(Path.Combine(folder, "outside.txt"), OutsideSecret);
        return root;
    }

    private static HostSettings Enabled(string root)
    {
        var settings = new HostSettings { Seed = false, WebApp = true };
        settings.Settings["Product:WebApp:RootPath"] = root;
        return settings;
    }

    private static HostSettings Production(params (string Key, string Value)[] more)
    {
        var settings = new HostSettings { Environment = "Production", DevIdentity = false, Seed = false, Inventory = false };
        settings.Settings["Product:Auth:ClientId"] = Personas.Audience;
        foreach ((string key, string value) in more)
        {
            settings.Settings[key] = value;
        }

        return settings;
    }

    private static Exception StartFails(HostSettings settings)
    {
        settings.DatabaseFile = Path.Combine(TestPaths.NewScratchFolder(), "product.db");
        return Assert.ThrowsAny<Exception>(() =>
        {
            ProductTestHost host = ProductTestHost.Start(settings);
            host.DisposeAsync().AsTask().GetAwaiter().GetResult();
        });
    }

    private static string Message(Exception exception)
    {
        var messages = new List<string>();
        for (Exception? e = exception; e != null; e = e.InnerException)
        {
            messages.Add(e.Message);
            if (e is AggregateException aggregate)
            {
                messages.AddRange(aggregate.InnerExceptions.Select(i => i.Message));
            }
        }

        return string.Join("\n", messages);
    }
}
