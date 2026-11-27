using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Product.Azure.Host;
using Product.Azure.Host.Seeding;
using Product.Azure.Stores;
using Product.Azure.Stores.Sqlite;
using Product.Engine.Core.Contract;
using Product.Engine.Runtime.Callers;
using Product.Engine.Runtime.Ports;
using Xunit;

namespace Product.Azure.Tests;

// The demo persona switch (D-39). In a demo install only (the host runs in the environment named Demo), the person signed
// in to the web app may act as one of the fictional personas of seed/demo-personas.json by sending X-Product-Demo-Persona.
// The request then runs as the persona (the Tool Policy, the engine's checks and SESSION_CONTEXT all see the persona),
// and every row and audit entry it writes carries the Client App label "Web app, demo operator". Outside Demo the header
// is ignored, and a host that names demo personas refuses to start.
public sealed class DemoPersonaTests
{
    // The demo operator: a real person signed in with Entra through the web app, in none of the demo binding's groups.
    private const string OperatorId = "00000000-0000-4000-8000-000000000231";

    private const string RiskAcceptance = "{\"decisionTypeKey\":\"risk-acceptance\",\"subjectKind\":\"business-application\",\"subjectId\":\"" + Personas.InspectionScheduler
        + "\",\"statement\":\"Accept the paper checklist risk\",\"context\":\"Inspectors keep a paper copy while the app is offline\",\"proposedOutcomeKey\":\"accept\",\"reason\":\"Low impact\",\"clientRequestId\":\"demo-operator-risk-1\"}";

    private static string PersonasFile => Path.Combine(TestPaths.Adapter, "seed", "demo-personas.json");

    // Marcus Oyelaran, architect and change board (wave 4b verify round).
    private const string MarcusId = "00000000-0000-4000-8000-000000001115";

    [Fact]
    public async Task ADemoOperatorActsAsTheChosenPersonaAndEveryWriteSaysDemoOperator()
    {
        var contexts = new List<StoreCallContext>();
        HostSettings settings = Demo();
        Action<IServiceCollection> signIn = settings.Services!;
        settings.Services = services =>
        {
            signIn(services);
            services.AddSingleton<IStartupFilter>(new CallContextRecorder(contexts));
        };
        await using ProductTestHost host = ProductTestHost.Start(settings);
        IRecordStore records = host.Get<IRecordStore>();
        string token = EntraTokens.Token(EntraTokens.WebApp(OperatorId));

        // As themselves the operator is a person with no product role: the demo binding binds only fictional groups.
        HttpCall self = await host.Send(HttpMethod.Get, "/api/me", null, null, EntraTokens.Bearer(token));
        Assert.True(self.Status == 200, self + "\n" + host.Logs);
        Assert.Equal(OperatorId, self.Json!["objectId"]!.GetValue<string>());
        Assert.Empty(self.Json["roles"]!.AsArray());
        Assert.Null(self.Json["demoPersona"]);

        // As Nadia, everything is Nadia's: her object ID, her name from her Principal Reference, her role.
        HttpCall nadia = await host.Send(HttpMethod.Get, "/api/me", null, null, EntraTokens.Bearer(token, "nadia"));
        Assert.True(nadia.Status == 200, nadia.ToString());
        Assert.Equal(Personas.NadiaId, nadia.Json!["objectId"]!.GetValue<string>());
        Assert.Equal("Nadia Ferreira", nadia.Json["name"]!.GetValue<string>());
        Assert.Equal("person", nadia.Json["actorKind"]!.GetValue<string>());
        Assert.Equal(new[] { "application-owner" }, Roles(nadia.Json));
        Assert.Equal("nadia", nadia.Json["demoPersona"]!["name"]!.GetValue<string>());
        Assert.Equal("Nadia Ferreira, application owner", nadia.Json["demoPersona"]!["label"]!.GetValue<string>());

        // whoami on the person entry names the client the engine stamps: the web app, as a demo operator.
        HttpCall whoami = await host.Send(HttpMethod.Post, "/api/whoami", null, "{}", EntraTokens.Bearer(token, "nadia"));
        Assert.True(whoami.Status == 200, whoami.ToString());
        Assert.Equal(Personas.NadiaId, whoami.Json!["data"]!["principal"]!["objectId"]!.GetValue<string>());
        Assert.Equal("Web app, demo operator", whoami.Json["data"]!["clientApp"]!["label"]!.GetValue<string>());

        // Without a persona the operator may not propose anything; as Nadia, the business owner of Inspection Scheduler,
        // the engine accepts the risk acceptance proposal.
        HttpCall refused = await host.Send(HttpMethod.Post, "/api/save_decision_proposal", null, RiskAcceptance, EntraTokens.Bearer(token));
        Assert.True(refused.Status == 403, refused.ToString());
        Assert.Equal("not_authorized", refused.Json!["error"]!["code"]!.GetValue<string>());

        HashSet<string> auditsBefore = new(records.Query(RowQuery.For("audit-entry")).Select(r => r.Id), StringComparer.Ordinal);
        HttpCall proposed = await host.Send(HttpMethod.Post, "/api/save_decision_proposal", null, RiskAcceptance, EntraTokens.Bearer(token, "nadia"));
        Assert.True(proposed.Status == 200 && proposed.Json!["ok"]!.GetValue<bool>(), proposed + "\n" + host.Logs);
        JsonNode data = proposed.Json!["data"]!;
        Assert.Equal("person", data["performedBy"]!["actorKind"]!.GetValue<string>());
        Assert.Equal(Personas.NadiaId, data["performedBy"]!["principal"]!["objectId"]!.GetValue<string>());
        Assert.Null(data["performedBy"]!["onBehalfOf"]);
        Assert.Equal("Web app, demo operator", data["performedBy"]!["clientApp"]!["label"]!.GetValue<string>());

        // The ledger: the persona did it, a demo operator acted, and nothing names the operator.
        StoredRow decision = records.Get("decision", data["recordId"]!.GetValue<string>())!;
        Assert.Equal(Personas.NadiaId, decision.Text("createdByPrincipal"));
        Assert.Equal(Personas.NadiaId, decision.Text("performedByPrincipal"));
        Assert.Equal("person", decision.Text("performedByActorKind"));
        Assert.Null(decision.Text("onBehalfOf"));
        Assert.False(decision.Flag("isAiCreated"));
        Assert.Equal("Web app, demo operator", decision.Text("clientApp"));
        Assert.True(decision.Flag("clientAppVerified"));

        // The audit entries of the request say the same.
        List<StoredRow> audits = records.Query(RowQuery.For("audit-entry")).Where(r => !auditsBefore.Contains(r.Id)).ToList();
        Assert.NotEmpty(audits);
        Assert.All(audits, a => Assert.Equal(Personas.NadiaId, a.Text("actor")));
        Assert.All(audits, a => Assert.Equal("person", a.Text("actorKind")));
        Assert.All(audits, a => Assert.Equal("Web app, demo operator", a.Text("clientApp")));

        // SESSION_CONTEXT names the persona, on the person entry, with the demo operator label.
        StoreCallContext written = contexts.Last();
        Assert.Equal(Personas.NadiaId, written.PrincipalId);
        Assert.Equal(CallerEntry.Person, written.Entry);
        Assert.Equal(ActorKindMeaning.Person, written.ActorKind);
        Assert.Equal("Web app, demo operator", written.ClientApp);
        Assert.Contains("application-owner", written.RoleKeys);

        // Nothing in the store names the operator.
        Assert.DoesNotContain(OperatorId, ((RowStore)host.Get<IProductStore>()).Dump(), StringComparison.OrdinalIgnoreCase);

        // The persona is chosen per request: the next request may be another persona, with that persona's role only.
        HttpCall owen = await host.Send(HttpMethod.Get, "/api/me", null, null, EntraTokens.Bearer(token, "OWEN"));
        Assert.Equal(Personas.OwenId, owen.Json!["objectId"]!.GetValue<string>());
        Assert.Equal(new[] { "platform-admin" }, Roles(owen.Json));
    }

    [Fact]
    public async Task APersonaTheInstallDoesNotNameIsRefusedAndNothingIsWritten()
    {
        await using ProductTestHost host = ProductTestHost.Start(Demo());
        string token = EntraTokens.Token(EntraTokens.WebApp(OperatorId));
        string before = ((RowStore)host.Get<IProductStore>()).Dump();

        foreach (string name in new[] { "nobody", "nadia-web", "00000000-0000-4000-8000-000000000212" })
        {
            HttpCall write = await host.Send(HttpMethod.Post, "/api/save_decision_proposal", null, RiskAcceptance, EntraTokens.Bearer(token, name));
            Assert.True(write.Status == 400, name + ": " + write);
            Assert.Equal("validation", write.Json!["error"]!["code"]!.GetValue<string>());
            Assert.Equal(ProductDemo.PersonaHeader, write.Json["error"]!["field"]!.GetValue<string>());
        }

        Assert.Equal(before, ((RowStore)host.Get<IProductStore>()).Dump());
    }

    [Fact]
    public async Task InTheDemoTheHeaderCountsForThePersonOnTheWebAppAndForTheirAiClientOnMcp()
    {
        await using ProductTestHost host = ProductTestHost.Start(Demo());

        // The demo operator's Claude Code on the MCP endpoint (product review 2026-11-24): the header picks the persona
        // the AI client acts for, so the story's AI build loop runs on a demo install. The persona's roles apply, the AI
        // rules still hold (it acts as an AI client), and the engine stamps "Claude Code, demo operator".
        string claude = EntraTokens.Token(EntraTokens.ClaudeCode(OperatorId));
        HttpCall mcp = await host.Send(HttpMethod.Post, "/mcp", null, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"whoami\",\"arguments\":{}}}", EntraTokens.Bearer(claude, "priya"));
        Assert.True(mcp.Status == 200, mcp + "\n" + host.Logs);
        JsonNode who = mcp.Json!["result"]!["structuredContent"]!;
        Assert.True(who["ok"]!.GetValue<bool>(), who.ToJsonString());
        Assert.Equal(Personas.PriyaId, who["data"]!["principal"]!["objectId"]!.GetValue<string>());
        Assert.Equal("ai-client-for-person", who["data"]!["actorKind"]!.GetValue<string>());
        Assert.EndsWith(", demo operator", who["data"]!["clientApp"]!["label"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.DoesNotContain(OperatorId, ((RowStore)host.Get<IProductStore>()).Dump(), StringComparison.OrdinalIgnoreCase);

        // A persona the install does not name is refused on the MCP endpoint too.
        HttpCall unknown = await host.Send(HttpMethod.Post, "/mcp", null, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"whoami\",\"arguments\":{}}}", EntraTokens.Bearer(claude, "nobody"));
        Assert.True(unknown.Status == 400, unknown.ToString());

        // On the REST API an AI client is never switched: the header is ignored there.
        HttpCall rest = await host.Send(HttpMethod.Get, "/api/me", null, null, EntraTokens.Bearer(claude, "owen"));
        Assert.True(rest.Status == 200, rest.ToString());
        Assert.Equal(OperatorId, rest.Json!["objectId"]!.GetValue<string>());
        Assert.Equal("ai-client-for-person", rest.Json["actorKind"]!.GetValue<string>());
        Assert.Null(rest.Json["demoPersona"]);

        // An agent acting through the web app's client ID is never the person entry, so it never picks a persona.
        string agent = EntraTokens.Token(With(EntraTokens.WebApp(OperatorId), ("xms_act_fct", "11")));
        HttpCall agentMe = await host.Send(HttpMethod.Get, "/api/me", null, null, EntraTokens.Bearer(agent, "owen"));
        Assert.True(agentMe.Status == 200, agentMe.ToString());
        Assert.Equal(OperatorId, agentMe.Json!["objectId"]!.GetValue<string>());
        Assert.Null(agentMe.Json["demoPersona"]);
    }

    [Fact]
    public async Task OutsideTheDemoEnvironmentTheHeaderIsIgnored()
    {
        // Production with Entra sign-in: the person signed in to the web app stays the person.
        await using (ProductTestHost production = ProductTestHost.Start(EntraTokens.Host("Production")))
        {
            string nadia = EntraTokens.Token(EntraTokens.WebApp(Personas.NadiaId, Personas.OwnerGroup));
            HttpCall me = await production.Send(HttpMethod.Get, "/api/me", null, null, EntraTokens.Bearer(nadia, "owen"));
            Assert.True(me.Status == 200, me + "\n" + production.Logs);
            Assert.Equal(Personas.NadiaId, me.Json!["objectId"]!.GetValue<string>());
            Assert.Equal(new[] { "application-owner" }, Roles(me.Json));
            Assert.Null(me.Json["demoPersona"]);
        }

        // A local Development run: the development persona stays itself, and its writes say "Web app" only.
        var contexts = new List<StoreCallContext>();
        var settings = new HostSettings { Services = services => services.AddSingleton<IStartupFilter>(new CallContextRecorder(contexts)) };
        await using ProductTestHost development = ProductTestHost.Start(settings);
        HttpCall local = await development.Send(HttpMethod.Get, "/api/me", Personas.NadiaWeb, null, r => r.Headers.TryAddWithoutValidation(ProductDemo.PersonaHeader, "owen"));
        Assert.True(local.Status == 200, local.ToString());
        Assert.Equal(Personas.NadiaId, local.Json!["objectId"]!.GetValue<string>());
        Assert.Null(local.Json["demoPersona"]);
        Assert.Equal("Web app", contexts.Last().ClientApp);
        Assert.Equal(Personas.NadiaId, contexts.Last().PrincipalId);
    }

    [Fact]
    public void AHostWithDemoPersonasRefusesToStartOutsideTheDemoEnvironment()
    {
        const string refusal = "work only in the Demo environment";

        // The inline list in Production, and the persona file in a local Development run.
        HostSettings production = EntraTokens.Host("Production");
        production.Seed = false;
        foreach (KeyValuePair<string, string> setting in Inline("nadia", Personas.NadiaId, Personas.OwnerGroup))
        {
            production.Settings[setting.Key] = setting.Value;
        }

        Assert.Contains(refusal, StartupFailures.Messages(production), StringComparison.Ordinal);

        var development = new HostSettings { Seed = false };
        development.Settings[ProductDemo.PersonasFileKey] = PersonasFile;
        Assert.Contains(refusal, StartupFailures.Messages(development), StringComparison.Ordinal);

        // The guard both hosts run before they build (the Functions app calls it from FunctionsHostGuard).
        IConfiguration inline = new ConfigurationBuilder().AddInMemoryCollection(Inline("nadia", Personas.NadiaId, Personas.OwnerGroup).Select(p => new KeyValuePair<string, string?>(p.Key, p.Value))).Build();
        IConfiguration file = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [ProductDemo.PersonasFileKey] = "seed/demo-personas.json" }).Build();
        IConfiguration none = new ConfigurationBuilder().Build();
        foreach (string environment in new[] { "Production", "Development", "Staging", "demo-like" })
        {
            Assert.Contains(refusal, Assert.Throws<InvalidOperationException>(() => ProductDemo.Validate(Environment(environment), inline)).Message, StringComparison.Ordinal);
            Assert.Contains(refusal, Assert.Throws<InvalidOperationException>(() => ProductDemo.Validate(Environment(environment), file)).Message, StringComparison.Ordinal);
            ProductDemo.Validate(Environment(environment), none);
        }

        ProductDemo.Validate(Environment("Demo"), inline);
        ProductDemo.Validate(Environment("Demo"), file);
        string guard = File.ReadAllText(Path.Combine(TestPaths.Adapter, "Product.Azure.Functions", "FunctionsHostGuard.cs"));
        Assert.Contains("ProductDemo.Validate(environment, configuration);", guard, StringComparison.Ordinal);
    }

    [Fact]
    public void InTheDemoEnvironmentThePersonasAreChecked()
    {
        // A real-looking object ID: a demo persona is never a real person.
        HostSettings real = Demo(file: false);
        real.Seed = false;
        foreach (KeyValuePair<string, string> setting in Inline("someone", "3f2b8c1e-5d4a-4e9b-8c7d-1a2b3c4d5e6f", Personas.OwnerGroup))
        {
            real.Settings[setting.Key] = setting.Value;
        }

        Assert.Contains("fictional object ID", StartupFailures.Messages(real), StringComparison.Ordinal);

        // The inline list and the file together, and a file that is not there.
        HostSettings both = Demo();
        both.Seed = false;
        foreach (KeyValuePair<string, string> setting in Inline("nadia", Personas.NadiaId, Personas.OwnerGroup))
        {
            both.Settings[setting.Key] = setting.Value;
        }

        Assert.Contains("not both", StartupFailures.Messages(both), StringComparison.Ordinal);

        HostSettings missing = Demo(file: false);
        missing.Seed = false;
        missing.Settings[ProductDemo.PersonasFileKey] = "seed/no-such-personas.json";
        Assert.Contains("not in this deployment", StartupFailures.Messages(missing), StringComparison.Ordinal);

        // Security review: the environment name alone does not make a demo install. An install that does not say it is a
        // demo (no demo badge, so possibly real data) refuses to start with personas, even in the Demo environment.
        HostSettings notADemo = Demo();
        notADemo.Seed = false;
        notADemo.Settings["Product:WebApp:Demo"] = "false";
        Assert.Contains("Product:WebApp:Demo", StartupFailures.Messages(notADemo), StringComparison.Ordinal);

        // The persona switch works only for the person entry, so the web app's client ID must be known.
        HostSettings noClient = Demo();
        noClient.Seed = false;
        noClient.WebAppClient = false;
        Assert.Contains("Product:WebApp:ClientIds", StartupFailures.Messages(noClient), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfigJsonListsTheDemoPersonasOnlyInTheDemoEnvironment()
    {
        string root = BuiltApp();
        HostSettings demo = Demo();
        demo.Seed = false;
        demo.WebApp = true;
        demo.Settings["Product:WebApp:RootPath"] = root;
        await using (ProductTestHost host = ProductTestHost.Start(demo))
        {
            HttpCall config = await host.Get("/app/config.json");
            Assert.True(config.Status == 200, config + "\n" + host.Logs);
            string[] names = config.Json!["demoPersonas"]!.AsArray().Select(p => p!["name"]!.GetValue<string>()).ToArray();
            // Marcus Oyelaran, the second architect and change board member, follows Tomas Rendel (wave 4b verify round).
            Assert.Equal(new[] { "lena", "priya", "nadia", "kai", "elif", "hugo", "tomas", "marcus", "mateo", "dmitri", "noor", "owen", "yusuf", "bianca", "theo", "hana", "jonah" }, names);
            Assert.Equal("Kai Ostrowski, data owner", config.Json["demoPersonas"]![3]!["label"]!.GetValue<string>());

            // Names and labels only: the fictional IDs stay on the server.
            Assert.DoesNotContain("objectId", config.Body, StringComparison.Ordinal);
            Assert.DoesNotContain(Personas.NadiaId, config.Body, StringComparison.Ordinal);
            Assert.Equal("entra", config.Json["auth"]!["mode"]!.GetValue<string>());
        }

        HostSettings production = EntraTokens.Host("Production");
        production.Seed = false;
        production.WebApp = true;
        production.Settings["Product:WebApp:RootPath"] = root;
        await using (ProductTestHost host = ProductTestHost.Start(production))
        {
            JsonNode config = (await host.Get("/app/config.json")).Json!;
            Assert.Empty(config["demoPersonas"]!.AsArray());
        }
    }

    [Fact]
    public async Task TheFunctionsDelegateSwitchesThePersonaAsTheLocalServerDoes()
    {
        await using ProductTestHost host = ProductTestHost.Start(Demo());
        RequestDelegate functions = ProductPipeline.Build(host.Services);
        string token = EntraTokens.Token(EntraTokens.WebApp(OperatorId));

        HttpCall local = await host.Send(HttpMethod.Get, "/api/me", null, null, EntraTokens.Bearer(token, "kai"));
        (int status, string body) = await ThroughFunctionsDelegate(host, functions, "/api/me", token, "kai");

        Assert.True(local.Status == 200, local.ToString());
        Assert.Equal(local.Status, status);
        JsonNode viaFunctions = JsonNode.Parse(body)!;
        Assert.Equal("00000000-0000-4000-8000-000000001119", viaFunctions["objectId"]!.GetValue<string>());
        Assert.Equal(new[] { "data-owner" }, Roles(viaFunctions));
        Assert.Equal(Roles(local.Json!), Roles(viaFunctions));
        Assert.Equal("kai", viaFunctions["demoPersona"]!["name"]!.GetValue<string>());

        (int refusedStatus, _) = await ThroughFunctionsDelegate(host, functions, "/api/me", token, "nobody");
        Assert.Equal(400, refusedStatus);
    }

    [Fact]
    public void TheShippedDemoPersonasPlayEveryHumanRoleThroughTheDemoBinding()
    {
        List<ProductDemoPersona> personas = ProductDemo.ReadFile(PersonasFile);
        JsonNode binding = JsonNode.Parse(File.ReadAllText(TestPaths.DemoBinding))!;
        Dictionary<string, string> roleOfGroup = binding["bindingOperations"]!.AsArray()
            .Where(o => o!["entity"]!.GetValue<string>() == "role-group-binding")
            .ToDictionary(o => o!["values"]!["entraGroupObjectId"]!.GetValue<string>(), o => o!["values"]!["roleKey"]!.GetValue<string>(), StringComparer.OrdinalIgnoreCase);
        string[] humanRoles = JsonNode.Parse(File.ReadAllText(Path.Combine(TestPaths.Source, "config", "presets", "balanced", "role.json")))!["rows"]!.AsArray()
            .Where(r => r!["isWorkloadRole"]?.GetValue<bool>() != true)
            .Select(r => r!["key"]!.GetValue<string>())
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(15, humanRoles.Length);

        // The demo binding binds every human role, each to its own fictional group.
        Assert.Equal(humanRoles, roleOfGroup.Values.Distinct().OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.All(roleOfGroup.Keys, g => Assert.True(ProductDemo.IsFictionalId(g), g));

        // One persona per role, plus a second configuration admin so a configuration change one admin proposes is answered by
        // another person, and Marcus Oyelaran, architect and change board member (wave 4b verify round), who answers a change board
        // request a person on it proposed: seventeen. Marcus is one of the estate's people in the file either way, a persona
        // or a member, never both: the local run offers him already (demo-marcus), and the demo step moves him from the
        // members to the personas (V3-demo), so until then the file holds sixteen personas. Every persona's group is bound,
        // and every role is played.
        List<ProductDemoPersona> fileMembers = new ConfigurationBuilder().AddJsonFile(PersonasFile).Build().GetSection("members").Get<List<ProductDemoPersona>>() ?? new List<ProductDemoPersona>();
        bool marcusIsPersona = personas.Any(p => string.Equals(p.ObjectId, MarcusId, StringComparison.OrdinalIgnoreCase));
        Assert.True(marcusIsPersona ^ fileMembers.Any(m => string.Equals(m.ObjectId, MarcusId, StringComparison.OrdinalIgnoreCase)), "Marcus Oyelaran is in the persona file once: a persona or a member.");
        Assert.Equal(marcusIsPersona ? 17 : 16, personas.Count);
        Assert.All(personas, p => Assert.All(p.Groups, g => Assert.True(roleOfGroup.ContainsKey(g), p.Name + " is in the unbound group " + g)));
        string[] played = personas.SelectMany(p => p.Groups).Select(g => roleOfGroup[g]).Distinct().OrderBy(k => k, StringComparer.Ordinal).ToArray();
        Assert.Equal(humanRoles, played);
        Assert.Equal(2, personas.Count(p => p.Groups.Any(g => roleOfGroup[g] == "configuration-admin")));

        // The people are the demo estate's fictional people, and each label starts with the person's name.
        Dictionary<string, (string Kind, string Label)> estate = Principals(Path.Combine(TestPaths.Adapter, "seed", "demo-estate.json"));
        foreach (ProductDemoPersona persona in personas)
        {
            Assert.True(ProductDemo.IsFictionalId(persona.ObjectId), persona.Name);
            Assert.True(estate.TryGetValue(persona.ObjectId, out var person), persona.Name + " is not a person of the demo estate.");
            Assert.Equal("user", person.Kind);
            Assert.StartsWith(person.Label + ", ", persona.Label, StringComparison.Ordinal);
        }

        Assert.Equal(personas.Count, personas.Select(p => p.ObjectId).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        // Every bound group has its Principal Reference in the walkthrough estate the tests seed and in the demo estate.
        Dictionary<string, (string Kind, string Label)> walkthrough = Principals(TestPaths.WalkthroughWorld);
        Assert.All(roleOfGroup.Keys, g => Assert.Equal("group", walkthrough[g].Kind));
        Assert.All(roleOfGroup.Keys, g => Assert.Equal("group", estate[g].Kind));

        // A persona's teams (document 24, B24-20) are groups of the demo estate that bind no role, so the Teams page lists the
        // persona among their members and the persona holds nothing more through them. Each support team has a persona.
        string[] teams = personas.SelectMany(p => p.Teams).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.Ordinal).ToArray();
        Assert.Equal(4, teams.Length);
        Assert.All(teams, t => Assert.True(ProductDemo.IsFictionalId(t), t));
        Assert.All(teams, t => Assert.False(roleOfGroup.ContainsKey(t), t + " is bound to a role, so it is not a team a persona may be in without the role."));
        Assert.All(teams, t => Assert.Equal("group", estate[t].Kind));

        // The local Development personas keep working: every group they sign in with is still bound, and their teams are
        // the demo personas' own.
        JsonNode development = JsonNode.Parse(File.ReadAllText(Path.Combine(TestPaths.ServerProject, "appsettings.Development.json")))!;
        foreach ((string name, JsonNode? persona) in development["Product"]!["DevIdentity"]!["Personas"]!.AsObject())
        {
            foreach (JsonNode? group in persona!["Groups"]?.AsArray() ?? new JsonArray())
            {
                Assert.True(roleOfGroup.ContainsKey(group!.GetValue<string>()), name + " signs in with an unbound group.");
            }

            // The same person in the persona file, a persona or one of the estate's members (Marcus Oyelaran, until the demo
            // step makes him a persona), is in the same teams.
            string[] devTeams = (persona["Teams"]?.AsArray() ?? new JsonArray()).Select(t => t!.GetValue<string>()).ToArray();
            if (devTeams.Length > 0)
            {
                string objectId = persona["ObjectId"]!.GetValue<string>();
                ProductDemoPersona same = personas.Concat(fileMembers).First(p => string.Equals(p.ObjectId, objectId, StringComparison.OrdinalIgnoreCase));
                Assert.Equal(same.Teams, devTeams);
            }
        }

        // The seed version compiles with every binding.
        Assert.Equal(humanRoles.Length + 2, Bindings(DemoSeed.Binding));
    }

    // ---- The Teams page on the demo estate (document 24, B24-20) ----

    // On a demo install the teams are the demo estate's groups, their kinds come from the demo binding and the Balanced
    // rules, and their members are the demo personas: the role groups each persona signs in with, and the support teams it
    // is in. Read through the REST API as a persona, as the web app reads it.
    [Fact]
    public async Task OnTheDemoEstateTheTeamsAreTheCommitteesAndSupportTeamsWithTheirPersonas()
    {
        string file = Path.Combine(TestPaths.NewScratchFolder(), "product.db");
        using (var store = new SqliteProductStore(new SqliteStoreOptions { ConnectionString = ProductTestHost.ConnectionString(file), NewSeedId = RowIds.EntityCounter() }))
        {
            store.EnsureSchema();
            ProductSeeding.SeedDemo(store, DemoSeed.Catalog, DemoSeed.Preset, null, DemoSeed.Binding, ProductSeeding.ReadJson(Path.Combine(TestPaths.Adapter, "seed", "demo-estate.json")), DemoSeed.ValidatedOn);
        }

        HostSettings settings = Demo();
        settings.DatabaseFile = file;
        await using ProductTestHost host = ProductTestHost.Start(settings);
        string token = EntraTokens.Token(EntraTokens.WebApp(OperatorId));

        HttpCall list = await host.Send(HttpMethod.Post, "/api/list_teams", null, "{\"top\":50}", EntraTokens.Bearer(token, "theo"));
        Assert.True(list.Status == 200, list + "\n" + host.Logs);
        JsonNode data = list.Json!["data"]!;
        Assert.Equal("demo-personas", data["membersReadFrom"]!.GetValue<string>());

        // Fifteen role groups and four support teams. Balanced asks nine of the bound roles to decide or approve.
        Assert.Equal(19, data["counts"]!["all"]!.GetValue<int>());
        Assert.Equal(9, data["counts"]!["committee"]!.GetValue<int>());
        Assert.Equal(6, data["counts"]!["roleGroup"]!.GetValue<int>());
        Assert.Equal(4, data["counts"]!["supportTeam"]!.GetValue<int>());
        JsonNode Row(string name) => data["items"]!.AsArray().Single(t => t!["team"]!["displayLabel"]!.GetValue<string>() == name)!;
        // Fix round 1 (product review): the estate's other people (the persona file's members) are in their committees and teams.
        Assert.Equal("committee", Row("Change board")["kinds"]![0]!.GetValue<string>());
        Assert.Equal(4, Row("Change board")["memberCount"]!.GetValue<int>());
        Assert.Equal("role-group", Row("Portfolio readers")["kinds"]![0]!.GetValue<string>());
        Assert.Equal(3, Row("Service Desk")["memberCount"]!.GetValue<int>());
        Assert.Equal(2, Row("Platform Team")["memberCount"]!.GetValue<int>());
        Assert.All(new[] { "Field Apps Support", "Finance Apps Support", "Service Desk", "Platform Team" }, n => Assert.Equal("support-team", Row(n)["kinds"]![0]!.GetValue<string>()));

        // The Service Desk page: who is in it, the applications it supports, and the data location it is deputy for.
        string desk = Row("Service Desk")["teamId"]!.GetValue<string>();
        HttpCall team = await host.Send(HttpMethod.Post, "/api/get_team", null, "{\"teamId\":\"" + desk + "\"}", EntraTokens.Bearer(token, "theo"));
        Assert.True(team.Status == 200, team.ToString());
        JsonNode deskData = team.Json!["data"]!;
        Assert.Equal(new[] { "Clara Voss", "Hugo Brandvold", "Theo Marsh" }, deskData["members"]!.AsArray().Select(m => m!["displayLabel"]!.GetValue<string>()));
        Assert.Equal(6, deskData["applications"]!.AsArray().Count);
        Assert.Contains(deskData["applications"]!.AsArray(), a => a!["name"]!.GetValue<string>() == "Contractor Portal" && a["assignments"]![0]!["isDeputy"]!.GetValue<bool>() == false);
        Assert.Single(deskData["dataAssignments"]!.AsArray());
        Assert.EndsWith("/team/" + desk, team.Json["links"]!["recordUrl"]!.GetValue<string>(), StringComparison.Ordinal);

        // The change board decides exceptions; Tomas Rendel is in it, with three people of the estate.
        string board = Row("Change board")["teamId"]!.GetValue<string>();
        JsonNode boardData = (await host.Send(HttpMethod.Post, "/api/get_team", null, "{\"teamId\":\"" + board + "\"}", EntraTokens.Bearer(token, "tomas"))).Json!["data"]!;
        Assert.Equal(new[] { "Felix Arendt", "Ines Varga", "Marcus Oyelaran", "Tomas Rendel" }, boardData["members"]!.AsArray().Select(m => m!["displayLabel"]!.GetValue<string>()));
        Assert.Contains(boardData["decisionTypes"]!.AsArray(), d => d!["decisionType"]!["key"]!.GetValue<string>() == "exception" && d["approves"]!.GetValue<bool>());

        // Product review: the data owners answer the data-use decisions of their own domains, as named holders, and the team page
        // lists what its members own.
        string owners = Row("Data owners")["teamId"]!.GetValue<string>();
        JsonNode ownersData = (await host.Send(HttpMethod.Post, "/api/get_team", null, "{\"teamId\":\"" + owners + "\"}", EntraTokens.Bearer(token, "theo"))).Json!["data"]!;
        Assert.Contains(ownersData["decisionTypes"]!.AsArray(), d => d!["throughOwnerRole"]?["key"]?.GetValue<string>() == "data-owner");
        Assert.Contains(ownersData["membersOwn"]!.AsArray(), o => o!["subjectKind"]!.GetValue<string>() == "data-domain" && o["member"]!["displayLabel"]!.GetValue<string>() == "Kai Ostrowski");

        // Hugo Brandvold's "member of": the change managers and the Service Desk.
        JsonNode hugo = (await host.Send(HttpMethod.Post, "/api/list_teams", null, "{\"memberObjectId\":\"00000000-0000-4000-8000-000000001117\"}", EntraTokens.Bearer(token, "theo"))).Json!["data"]!;
        Assert.Equal(new[] { "Change managers", "Service Desk" }, hugo["items"]!.AsArray().Select(t => t!["team"]!["displayLabel"]!.GetValue<string>()).OrderBy(n => n, StringComparer.Ordinal));
    }

    // On a local run (Development, the development identity on, directory mode Demo) the directory is the development
    // personas of appsettings.Development.json. A person with several logins (priya, priya-web and demo-priya) is one
    // member, and every login adds its groups and teams, so each support team lists the personas the persona file puts in
    // it, as on a demo install. A harness login with no name of its own ("A configuration admin") is in no group.
    [Fact]
    public void OnALocalRunEverySupportTeamListsItsPersonas()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(TestPaths.ServerProject, "appsettings.Development.json"))
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Product:DevIdentity:Enabled"] = "true" })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new LocalRun());
        services.AddSingleton(configuration);
        services.AddOptions<ProductWebAppOptions>().Bind(configuration.GetSection(ProductWebAppOptions.SectionName));
        using ServiceProvider provider = services.BuildServiceProvider();
        DemoDirectoryReader reader = DemoDirectoryReader.From(provider) ?? throw new InvalidOperationException("No demo directory on a local run.");

        List<ProductDemoPersona> personas = ProductDemo.ReadFile(PersonasFile);
        List<ProductDemoPersona> estate = new ConfigurationBuilder().AddJsonFile(PersonasFile).Build().GetSection("members").Get<List<ProductDemoPersona>>()!;
        string[] teams = personas.SelectMany(p => p.Teams).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Assert.Equal(4, teams.Length);
        foreach (string team in teams)
        {
            // The personas in the team, and the estate's other people in it (product review, fix round 1).
            string[] expected = personas.Where(p => p.Teams.Contains(team, StringComparer.OrdinalIgnoreCase)).Select(p => p.Label.Split(',')[0])
                .Concat(estate.Where(m => m.Teams.Concat(m.Groups).Contains(team, StringComparer.OrdinalIgnoreCase)).Select(m => m.Label))
                .OrderBy(n => n, StringComparer.Ordinal).ToArray();
            string[] members = reader.GroupMembers(null!, team).Value.Select(m => m.DisplayLabel).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Assert.Equal(expected, members);
        }

        // Priya Castell signs in as priya, priya-web and demo-priya, and is listed once in the makers' group.
        string makers = personas.Single(p => p.Name == "priya").Groups[1];
        Assert.Single(reader.GroupMembers(null!, makers).Value, m => m.DisplayLabel == "Priya Castell");
        Assert.DoesNotContain(reader.GroupMembers(null!, personas.Single(p => p.Name == "yusuf").Groups[0]).Value, m => m.DisplayLabel.StartsWith("A ", StringComparison.Ordinal));
    }

    private sealed class LocalRun : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";

        public string ApplicationName { get; set; } = "Product.Azure.Server";

        public string ContentRootPath { get; set; } = TestPaths.ServerProject;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    // ---- The request limit on a demo install (document 24, B24-21) ----

    // The operator's requests as the demo personas are counted together, apart from the operator's own, under the demo
    // operator's limit (Product:Limits:DemoOperatorPerMinute): switching personas never resets it, and presenting many
    // personas does not run into the per-person limit, which the operator's own requests keep.
    [Fact]
    public async Task TheDemoOperatorHasTheDemoLimitAcrossPersonasAndTheirOwnLimitAsThemselves()
    {
        HostSettings settings = Demo();
        settings.Settings["Product:Limits:ApiPerMinute"] = "2";
        settings.Settings["Product:Limits:DemoOperatorPerMinute"] = "4";
        await using ProductTestHost host = ProductTestHost.Start(settings);
        string token = EntraTokens.Token(EntraTokens.WebApp(OperatorId));

        foreach (string persona in new[] { "theo", "tomas", "theo", "nadia" })
        {
            HttpCall ok = await host.Send(HttpMethod.Get, "/api/me", null, null, EntraTokens.Bearer(token, persona));
            Assert.True(ok.Status == 200, persona + ": " + ok + "\n" + host.Logs);
        }

        HttpCall over = await host.Send(HttpMethod.Get, "/api/me", null, null, EntraTokens.Bearer(token, "tomas"));
        Assert.Equal(429, over.Status);
        Assert.Contains("more than 4 requests a minute", over.Json!["error"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);

        // As themselves the operator has the per-person limit, counted apart.
        Assert.Equal(200, (await host.Send(HttpMethod.Get, "/api/me", null, null, EntraTokens.Bearer(token))).Status);
        Assert.Equal(200, (await host.Send(HttpMethod.Get, "/api/me", null, null, EntraTokens.Bearer(token))).Status);
        HttpCall own = await host.Send(HttpMethod.Get, "/api/me", null, null, EntraTokens.Bearer(token));
        Assert.Equal(429, own.Status);
        Assert.Contains("more than 2 requests a minute", own.Json!["error"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    // ---- Helpers ----

    private static HostSettings Demo(bool file = true)
    {
        HostSettings settings = EntraTokens.Host(ProductDemo.EnvironmentName);
        settings.Settings["Product:WebApp:Demo"] = "true";
        if (file)
        {
            settings.Settings[ProductDemo.PersonasFileKey] = PersonasFile;
        }

        return settings;
    }

    private static IEnumerable<KeyValuePair<string, string>> Inline(string name, string objectId, string group)
    {
        yield return new KeyValuePair<string, string>(ProductDemo.PersonasKey + ":0:Name", name);
        yield return new KeyValuePair<string, string>(ProductDemo.PersonasKey + ":0:Label", name + ", a persona");
        yield return new KeyValuePair<string, string>(ProductDemo.PersonasKey + ":0:ObjectId", objectId);
        yield return new KeyValuePair<string, string>(ProductDemo.PersonasKey + ":0:Groups:0", group);
    }

    private static IHostEnvironment Environment(string name)
    {
        return new NamedEnvironment(name);
    }

    private static string[] Roles(JsonNode me)
    {
        return me["roles"]!.AsArray().Select(r => r!["roleKey"]!.GetValue<string>()).OrderBy(r => r, StringComparer.Ordinal).ToArray();
    }

    private static int Bindings(Product.Engine.Core.Canonical.JsonTreeNode binding)
    {
        return Product.Mcp.Core.Json.Js.Items(binding, "bindingOperations").Count;
    }

    private static Dictionary<string, (string Kind, string Label)> Principals(string seedFile)
    {
        return JsonNode.Parse(File.ReadAllText(seedFile))!["rows"]!.AsArray()
            .Where(r => r!["entity"]!.GetValue<string>() == "principal-reference")
            .ToDictionary(
                r => r!["columns"]!["entraObjectId"]!.GetValue<string>(),
                r => (r!["columns"]!["principalKind"]!.GetValue<string>(), r!["columns"]!["displayLabel"]!.GetValue<string>()),
                StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, object> With(Dictionary<string, object> claims, params (string Name, object Value)[] more)
    {
        var copy = new Dictionary<string, object>(claims, StringComparer.Ordinal);
        foreach ((string name, object value) in more)
        {
            copy[name] = value;
        }

        return copy;
    }

    // A built web app in a scratch folder: the page only.
    private static string BuiltApp()
    {
        string root = Path.Combine(TestPaths.NewScratchFolder(), "app");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html><html><head><title>Product</title></head><body></body></html>");
        return root;
    }

    // One request through the delegate the Functions app builds, as ProductFunction hands it over.
    private static async Task<(int Status, string Body)> ThroughFunctionsDelegate(ProductTestHost host, RequestDelegate pipeline, string path, string token, string demoPersona)
    {
        using IServiceScope scope = host.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Method = "GET";
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("mcp.example.org");
        context.Request.Path = path;
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        context.Request.Headers["Accept"] = "application/json";
        context.Request.Headers["Authorization"] = "Bearer " + token;
        context.Request.Headers[ProductDemo.PersonaHeader] = demoPersona;
        var response = new MemoryStream();
        context.Response.Body = response;
        context.SetEndpoint(null);
        context.Request.RouteValues.Clear();
        await pipeline(context);
        return (context.Response.StatusCode, Encoding.UTF8.GetString(response.ToArray()));
    }

    // A host environment with only a name, for the startup guard.
    private sealed class NamedEnvironment : IHostEnvironment
    {
        public NamedEnvironment(string name)
        {
            EnvironmentName = name;
        }

        public string EnvironmentName { get; set; }

        public string ApplicationName { get; set; } = "Product.Azure.Server";

        public string ContentRootPath { get; set; } = TestPaths.ServerProject;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    // Records the store call context of every request, after the request ran.
    private sealed class CallContextRecorder : IStartupFilter
    {
        private readonly List<StoreCallContext> _contexts;

        public CallContextRecorder(List<StoreCallContext> contexts)
        {
            _contexts = contexts;
        }

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.Use(async (context, nextMiddleware) =>
                {
                    await nextMiddleware(context);
                    StoreCallContext? recorded = ProductRequest.CallContext(context);
                    if (recorded != null)
                    {
                        lock (_contexts)
                        {
                            _contexts.Add(recorded);
                        }
                    }
                });
                next(app);
            };
        }
    }
}
