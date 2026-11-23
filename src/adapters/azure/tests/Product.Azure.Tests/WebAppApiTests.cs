using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Product.Azure.Host;
using Product.Azure.Stores;
using Product.Engine.Core.Contract;
using Product.Engine.Runtime.Callers;
using Product.Engine.Runtime.Ports;
using Xunit;

namespace Product.Azure.Tests;

// The REST API as the product's web app calls it: a token the web app asked for (its azp is one of
// Product:WebApp:ClientIds) is the person entry, so the engine runs as the person; every other client is an AI client
// acting for the person and passes the Known Client check, as on the MCP endpoint. The pages are read-only, so the REST
// API serves the read tools only.
public sealed class WebAppApiTests
{
    [Fact]
    public async Task TheWebAppsSessionIsThePersonAndAnAiClientsIsTheAiClient()
    {
        await using ProductTestHost host = ProductTestHost.Start();

        HttpCall web = await host.Get("/api/me", Personas.NadiaWeb);
        Assert.True(web.Status == 200, web.ToString());
        Assert.Equal("person", web.Json!["actorKind"]!.GetValue<string>());
        Assert.Equal("Nadia Ferreira", web.Json["name"]!.GetValue<string>());
        Assert.Equal(Personas.NadiaId, web.Json["objectId"]!.GetValue<string>());
        Assert.Contains(web.Json["roles"]!.AsArray(), r => r!["roleKey"]!.GetValue<string>() == "application-owner");

        HttpCall ai = await host.Get("/api/me", Personas.Nadia);
        Assert.Equal(200, ai.Status);
        Assert.Equal("ai-client-for-person", ai.Json!["actorKind"]!.GetValue<string>());

        // The same roles on both entries: roles come from the groups and the configuration, never from the entry.
        Assert.Equal(Roles(web.Json), Roles(ai.Json));
    }

    [Fact]
    public async Task ThePagesReadThroughTheWebAppAndTheEngineStampsThePersonNotAnAiClient()
    {
        var contexts = new List<StoreCallContext>();
        var settings = new HostSettings { Services = services => services.AddSingleton<IStartupFilter>(new CallContextRecorder(contexts)) };
        await using ProductTestHost host = ProductTestHost.Start(settings);

        foreach ((string tool, string input) in new[]
        {
            ("get_portfolio_summary", "{}"),
            ("find_applications", "{\"text\":\"inspection\"}"),
            ("get_application_card", "{\"businessApplicationId\":\"" + Personas.InspectionScheduler + "\"}"),
            ("get_dependency_impact", "{\"businessApplicationId\":\"" + Personas.InspectionScheduler + "\"}"),
        })
        {
            HttpCall call = await host.Post("/api/" + tool, Personas.NadiaWeb, input);
            Assert.True(call.Status == 200 && call.Json!["ok"]!.GetValue<bool>(), tool + " answered " + call);
        }

        // SESSION_CONTEXT carries the person: the person entry, the actor kind person, no On Behalf Of, the web app.
        StoreCallContext person = contexts.Last();
        Assert.Equal(Personas.NadiaId, person.PrincipalId);
        Assert.Equal(CallerEntry.Person, person.Entry);
        Assert.Equal(ActorKindMeaning.Person, person.ActorKind);
        Assert.Null(person.OnBehalfOfId);
        Assert.Equal("Web app", person.ClientApp);
        Assert.Contains("application-owner", person.RoleKeys);

        // The same person through Claude Code is the AI client acting for her.
        HttpCall viaAi = await host.Post("/api/get_portfolio_summary", Personas.Nadia, "{}");
        Assert.Equal(200, viaAi.Status);
        StoreCallContext ai = contexts.Last();
        Assert.Equal(CallerEntry.AiClient, ai.Entry);
        Assert.Equal(ActorKindMeaning.AiClientForPerson, ai.ActorKind);
        Assert.Equal(Personas.NadiaId, ai.OnBehalfOfId);
    }

    [Fact]
    public async Task ThePagesAreReadOnlySoNoWriteToolIsServedToThePerson()
    {
        await using ProductTestHost host = ProductTestHost.Start();
        string before = ((RowStore)host.Get<IProductStore>()).Dump();
        foreach ((string tool, string input) in new[]
        {
            ("save_intake_draft", "{\"title\":\"Field inspections app\",\"need\":\"Paper forms.\",\"proposedKindKey\":\"power-platform-app\"}"),
            ("submit_record", "{\"subjectKind\":\"intake-request\",\"subjectId\":\"00000000-0000-4000-9000-000000000001\"}"),
            ("save_decision_proposal", "{}"),
        })
        {
            HttpCall call = await host.Post("/api/" + tool, Personas.PriyaWeb, input);
            Assert.Equal(404, call.Status);
            Assert.Equal("not_found", call.Json!["error"]!["code"]!.GetValue<string>());
            Assert.Contains("read-only", call.Json["error"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);
        }

        // No approve tool on any entry, and the approval outcome route is closed to a person's token.
        Assert.Equal(404, (await host.Post("/api/approve_decision", Personas.OwenWeb, "{}")).Status);
        HttpCall outcome = await host.Post("/engine/v1/approval-records/00000000-0000-4000-9000-000000000001/outcome", Personas.OwenWeb, "{}");
        Assert.Equal(403, outcome.Status);
        Assert.Equal(before, ((RowStore)host.Get<IProductStore>()).Dump());
    }

    [Fact]
    public async Task OnlyTheDeclaredWebAppIsThePersonAndAnAgentHoldsNoRoleOnAnyEntry()
    {
        await using ProductTestHost host = ProductTestHost.Start();

        // An AI client that is not a Known Client gets nothing, though the same person reads through the web app.
        HttpCall unknownClient = await host.Post("/api/get_portfolio_summary", Personas.PriyaOtherClient, "{}");
        Assert.Equal(403, unknownClient.Status);
        Assert.Equal("not_authorized", unknownClient.Json!["error"]!["code"]!.GetValue<string>());
        Assert.Equal(200, (await host.Post("/api/get_portfolio_summary", Personas.PriyaWeb, "{}")).Status);

        // An agent user signed in through the web app is still an agent: no role, no read.
        HttpCall agent = await host.Post("/api/get_portfolio_summary", Personas.AgentWeb, "{}");
        Assert.Equal(403, agent.Status);
        Assert.Equal("not_authorized", agent.Json!["error"]!["code"]!.GetValue<string>());
        HttpCall agentWhoAmI = await host.Post("/api/whoami", Personas.AgentWeb, "{}");
        Assert.Equal(403, agentWhoAmI.Status);

        // The web app's token is for the REST API only: the MCP endpoint wants its own scope.
        HttpCall mcp = await host.Mcp(Personas.NadiaWeb, "tools/call", "{\"name\":\"whoami\",\"arguments\":{}}");
        Assert.True(mcp.Status == 403, mcp.ToString());
    }

    [Fact]
    public async Task WhoamiThroughTheWebAppAnswersPersonWithTheSameRoles()
    {
        await using ProductTestHost host = ProductTestHost.Start();
        HttpCall web = await host.Post("/api/whoami", Personas.OwenWeb, "{}");
        Assert.True(web.Status == 200, web.ToString());
        Assert.Equal("person", web.Json!["data"]!["actorKind"]!.GetValue<string>());
        Assert.Equal("Web app", web.Json["data"]!["clientApp"]!["label"]!.GetValue<string>());

        JsonNode ai = await host.Call(Personas.Owen, "whoami");
        Assert.Equal("ai-client-for-person", ai["data"]!["actorKind"]!.GetValue<string>());
        Assert.Equal(
            ai["data"]!["productRoles"]!.AsArray().Select(r => r!["roleKey"]!.GetValue<string>()).OrderBy(k => k, StringComparer.Ordinal),
            web.Json["data"]!["productRoles"]!.AsArray().Select(r => r!["roleKey"]!.GetValue<string>()).OrderBy(k => k, StringComparer.Ordinal));
    }

    private static IEnumerable<string> Roles(JsonNode me)
    {
        return me["roles"]!.AsArray().Select(r => r!["roleKey"]!.GetValue<string>()).OrderBy(k => k, StringComparer.Ordinal).ToList();
    }

    // Records the store call context each request ran under (what the Azure SQL store writes to SESSION_CONTEXT).
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
