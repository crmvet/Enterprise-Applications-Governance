using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Product.Azure.Host;
using Product.Engine.Core.Contract;
using Product.Mcp.Core.Backend;
using Xunit;

namespace Product.Azure.Tests;

// The live directory reads over recorded, fictional Microsoft Graph and Power Platform API responses, with a fake token
// source in place of the on-behalf-of exchange: what each reader asks for and how it maps the answers (agent users,
// account state, leave dates, deleted users, direct app role assignments, inventory pages), how it fails (403 with the
// permission and role to fix, 429 retried once, a next page link to another host refused, the request's budget), that
// agents and workloads never read, that every request carries only the token the source issued for its service, and that
// no token reaches a log. Then the four tools end to end on the local server: they answer from the live reads instead of
// upstream_unavailable. No tenant, no network.
public sealed class DirectoryReadTests
{
    private const string PowerPlatformAdministrator = "11648597-926c-4cf3-9c36-bcebb0ba8dcc";
    private const string Graph = DirectoryOptions.GraphBaseUrl;
    private const string Query = DirectoryOptions.PowerPlatformApiUrl + "resourcequery/resources/query?api-version=2024-10-01";
    private const string Group105 = "00000000-0000-4000-8000-000000000105";
    private const string Members105 = Graph + "groups/" + Group105 + "/members?$count=true&$top=999&$select=id,displayName";

    private static readonly BackendCaller Owen = DirectoryRig.Person(Personas.OwenId, PowerPlatformAdministrator);

    [Fact]
    public void RoleHoldersFollowEveryPageAndTellAnAgentUserFromAPerson()
    {
        using DirectoryRig rig = DirectoryRig.Create(RecordedHttpHandler.Create());

        DirectoryAnswer<DirectoryObject?> group = rig.Graph.Group(Owen, Group105);
        DirectoryAnswer<IReadOnlyList<DirectoryObject>> members = rig.Graph.GroupMembers(Owen, Group105);

        Assert.True(group.Answered, group.Problem);
        Assert.Equal("Platform admins", group.Value!.DisplayLabel);
        Assert.Equal("group", group.Value.PrincipalKind);
        Assert.True(members.Answered, members.Problem);
        var kinds = members.Value.ToDictionary(m => m.ObjectId, m => m.PrincipalKind);
        Assert.Equal("user", kinds[Personas.OwenId]);
        Assert.Equal("agent-user", kinds[Personas.AgentUserId]);
        Assert.Equal("group", kinds["00000000-0000-4000-8000-000000000109"]);

        // The guest is a user (the contract has no guest kind); the device on page 2 holds no role and is left out.
        Assert.Equal("user", kinds["00000000-0000-4000-8000-000000000216"]);
        Assert.DoesNotContain("00000000-0000-4000-8000-000000000601", kinds.Keys);
        Assert.Equal(new[] { Members105, Members105 + "&$skiptoken=fictional-page-2" }, rig.Handler.Requests.Where(r => r.Url.Contains("/members", StringComparison.Ordinal)).Select(r => Unescaped(r.Url)).ToArray());
        Assert.All(rig.Handler.Requests, r => Assert.True(r.Recorded, r.ToString()));
    }

    [Fact]
    public void TheOwnerCheckReadsEnabledDisabledLeavingAndDeletedOwners()
    {
        using DirectoryRig rig = DirectoryRig.Create(RecordedHttpHandler.Create());

        DirectoryUserState enabled = Answer(rig.Graph.UserState(Owen, Personas.PriyaId));
        DirectoryUserState disabled = Answer(rig.Graph.UserState(Owen, Personas.NadiaId));
        DirectoryUserState leaving = Answer(rig.Graph.UserState(Owen, "00000000-0000-4000-8000-000000000215"));
        DirectoryUserState deleted = Answer(rig.Graph.UserState(Owen, "00000000-0000-4000-8000-000000000299"));

        Assert.Equal(("enabled", (DateTime?)null, "Priya Castell"), (enabled.State, enabled.LeavesOnUtc, enabled.DisplayLabel));
        Assert.Equal("disabled", disabled.State);
        Assert.Equal("enabled", leaving.State);
        Assert.Equal(new DateTime(2026, 12, 15, 17, 0, 0, DateTimeKind.Utc), leaving.LeavesOnUtc);
        Assert.Equal(DateTimeKind.Utc, leaving.LeavesOnUtc!.Value.Kind);
        Assert.Equal("deleted", deleted.State);

        // An ID that is not a GUID is never put into a URL.
        int sent = rig.Handler.Requests.Count;
        Assert.Equal("unknown", Answer(rig.Graph.UserState(Owen, "../me")).State);
        Assert.Equal(sent, rig.Handler.Requests.Count);
    }

    [Fact]
    public void DirectAssignmentsNameEveryoneWhoHoldsAnAppRoleOutsideAGroup()
    {
        using DirectoryRig rig = DirectoryRig.Create(RecordedHttpHandler.Create());

        IReadOnlyList<DirectAssignment> assignments = Answer(rig.Graph.DirectAssignments(Owen));

        var found = assignments.Select(a => (a.Principal.ObjectId, a.Principal.PrincipalKind, a.RoleKey)).OrderBy(a => a.ObjectId, StringComparer.Ordinal).ToArray();
        Assert.Equal(
            new[]
            {
                (Personas.MateoId, "user", "maker"),
                (Personas.RunnerId, "service-principal", "approval-writer"),
                (Personas.PipelineId, "managed-identity", "deployment-recorder"),
                (Personas.UnknownWorkloadId, "agent-identity", "deployment-recorder"),
            },
            found);
        Assert.All(assignments, a => Assert.StartsWith("Entra app role assignment ", a.Where, StringComparison.Ordinal));

        // The group assignment is the bound way to hold a role, and the default access assignment grants none.
        Assert.DoesNotContain(assignments, a => a.Principal.ObjectId == Group105 || a.Principal.ObjectId == "00000000-0000-4000-8000-000000000215");
        Assert.Contains(rig.Handler.Requests, r => Unescaped(r.Url) == Graph + "servicePrincipals(appId='" + Personas.Audience + "')?$select=id,appId,appRoles");
    }

    [Fact]
    public void TheGapCheckReadsTheInventoryOnePageAtATime()
    {
        using DirectoryRig rig = DirectoryRig.Create(RecordedHttpHandler.Create());

        InventoryPage first = Answer(rig.Inventory.Query(Owen, DirectoryOptions.PowerPlatformEstate, null, null));
        InventoryPage second = Answer(rig.Inventory.Query(Owen, DirectoryOptions.PowerPlatformEstate, null, first.NextSkipToken));

        Assert.Equal(new[] { "00000000-0000-0000-0000-000000000201", "00000000-0000-0000-0000-000000000202" }, first.Items.Select(i => i.ExternalId).ToArray());
        Assert.Equal(new[] { "canvas-app", "model-driven-app" }, first.Items.Select(i => i.ComponentKindKey).ToArray());
        Assert.Equal("fictional-page-2-of-2", first.NextSkipToken);
        Assert.Equal("Power Platform administrator", first.AdminRoleUsed);
        Assert.Null(second.NextSkipToken);
        InventoryItem owned = second.Items.Single(i => i.ExternalId == "00000000-0000-0000-0000-000000000210");
        Assert.Equal(("Crew Overtime Tracker", "Field Ops Prod", "microsoft.powerapps/canvasapps", Personas.PriyaId), (owned.LabelAsSeen, owned.TargetAsSeen, owned.SourceType, owned.SuggestedClaimantObjectId));

        // The query names the table and the component types, and pages with the service's own skip token.
        SeenRequest[] posts = rig.Handler.Requests.Where(r => r.Method == "POST").ToArray();
        Assert.Equal(2, posts.Length);
        Assert.All(posts, p => Assert.Equal(Query, Unescaped(p.Url)));
        JsonNode body = JsonNode.Parse(posts[1].Body!)!;
        Assert.Equal("PowerPlatformResources", body["TableName"]!.GetValue<string>());
        Assert.Equal("fictional-page-2-of-2", body["Options"]!["SkipToken"]!.GetValue<string>());
        Assert.Contains("'microsoft.powerapps/canvasapps'", QueryValues(posts[0].Body!, "type"));
    }

    [Fact]
    public void AnEnvironmentFilterGoesIntoTheQueryOnlyAsAnEnvironmentId()
    {
        using DirectoryRig rig = DirectoryRig.Create(RecordedHttpHandler.Only(RecordedHttpHandler.Exchange("POST", Query, (200, "{\"data\":[]}", null))));

        Assert.True(rig.Inventory.Query(Owen, DirectoryOptions.PowerPlatformEstate, new[] { "Field-Ops-Prod" }, null).Answered);
        DirectoryAnswer<InventoryPage> injected = rig.Inventory.Query(Owen, DirectoryOptions.PowerPlatformEstate, new[] { "x') | project secrets" }, null);
        DirectoryAnswer<InventoryPage> otherEstate = rig.Inventory.Query(Owen, "github", null, null);

        Assert.Equal(new[] { "'field-ops-prod'" }, QueryValues(rig.Handler.Requests.Single().Body!, "joinKey"));
        Assert.False(injected.Answered);
        Assert.Contains("is not a Power Platform environment ID", injected.Problem, StringComparison.Ordinal);
        Assert.False(otherEstate.Answered);
        Assert.Contains("compare_inventory_list", otherEstate.Problem, StringComparison.Ordinal);
        Assert.Single(rig.Handler.Requests);
    }

    [Fact]
    public void AnInventory403NamesTheAdminRolesAndThePermissionToGrant()
    {
        using DirectoryRig rig = DirectoryRig.Create(RecordedHttpHandler.Only(RecordedHttpHandler.Exchange("POST", Query, (403, "{\"error\":{\"code\":\"Forbidden\",\"message\":\"Fictional.\"}}", null))));
        BackendCaller maker = DirectoryRig.Person(Personas.PriyaId);

        DirectoryAnswer<InventoryPage> page = rig.Inventory.Query(maker, DirectoryOptions.PowerPlatformEstate, null, null);

        Assert.False(page.Answered);
        Assert.Contains("refused the query (403)", page.Problem, StringComparison.Ordinal);
        Assert.Contains("Power Platform Administrator", page.Problem, StringComparison.Ordinal);
        Assert.Contains("Your token names none of them", page.Problem, StringComparison.Ordinal);
        Assert.Contains("ResourceQuery.Resources.Read", page.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void AGraph403NamesThePermissionsAndTheDirectoryRole()
    {
        string user = Graph + "users/" + Personas.NadiaId + "?$select=id,displayName,accountEnabled,employeeLeaveDateTime";
        using DirectoryRig rig = DirectoryRig.Create(RecordedHttpHandler.Only(RecordedHttpHandler.Exchange("GET", user, (403, "{\"error\":{\"code\":\"Authorization_RequestDenied\",\"message\":\"Insufficient privileges to complete the operation.\"}}", null))));

        DirectoryAnswer<DirectoryUserState> state = rig.Graph.UserState(Owen, Personas.NadiaId);

        Assert.False(state.Answered);
        Assert.Contains("(Authorization_RequestDenied)", state.Problem, StringComparison.Ordinal);
        Assert.Contains("User-LifeCycleInfo.Read.All", state.Problem, StringComparison.Ordinal);
        Assert.Contains("Lifecycle Workflows Administrator or Global Reader", state.Problem, StringComparison.Ordinal);
        Assert.DoesNotContain("Insufficient privileges", state.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A429IsRetriedOnceAfterItsRetryAfterDelay()
    {
        string group = Graph + "groups/" + Group105 + "?$select=id,displayName";
        const string Found = "{\"id\":\"" + Group105 + "\",\"displayName\":\"Platform admins\"}";
        using DirectoryRig once = DirectoryRig.Create(RecordedHttpHandler.Only(RecordedHttpHandler.Exchange("GET", group, (429, null, "2"), (200, Found, null))));
        using DirectoryRig twice = DirectoryRig.Create(RecordedHttpHandler.Only(RecordedHttpHandler.Exchange("GET", group, (429, null, "1"), (429, null, "1"), (200, Found, null))));
        using DirectoryRig tooLong = DirectoryRig.Create(RecordedHttpHandler.Only(RecordedHttpHandler.Exchange("GET", group, (429, null, "120"), (200, Found, null))));

        DirectoryAnswer<DirectoryObject?> retried = once.Graph.Group(Owen, Group105);
        DirectoryAnswer<DirectoryObject?> throttled = twice.Graph.Group(Owen, Group105);
        DirectoryAnswer<DirectoryObject?> notWaited = tooLong.Graph.Group(Owen, Group105);

        Assert.True(retried.Answered, retried.Problem);
        Assert.Equal("Platform admins", retried.Value!.DisplayLabel);
        Assert.Equal(new[] { TimeSpan.FromSeconds(2) }, once.Waits);
        Assert.Equal(2, once.Handler.Requests.Count);
        Assert.All(once.Handler.Requests, r => Assert.Equal("Bearer " + FakeTokenSource.GraphToken, r.Authorization));

        Assert.False(throttled.Answered);
        Assert.Contains("throttling", throttled.Problem, StringComparison.Ordinal);
        Assert.Single(twice.Waits);
        Assert.Equal(2, twice.Handler.Requests.Count);

        Assert.False(notWaited.Answered);
        Assert.Contains("asks to wait 120 seconds", notWaited.Problem, StringComparison.Ordinal);
        Assert.Empty(tooLong.Waits);
        Assert.Single(tooLong.Handler.Requests);
    }

    [Theory]
    [InlineData("https://graph.microsoft.com.example.org/v1.0/groups/" + Group105 + "/members?$skiptoken=next")]
    [InlineData("https://other.example.org/v1.0/groups/" + Group105 + "/members?$skiptoken=next")]
    [InlineData("http://graph.microsoft.com/v1.0/groups/" + Group105 + "/members?$skiptoken=next")]
    [InlineData("https://graph.microsoft.com/beta/groups/" + Group105 + "/members?$skiptoken=next")]
    [InlineData("https://graph.microsoft.com:8443/v1.0/groups/" + Group105 + "/members?$skiptoken=next")]
    public void ANextPageLinkOffTheGraphV1AddressIsRefusedAndGetsNoToken(string nextLink)
    {
        string page = "{\"value\":[{\"@odata.type\":\"#microsoft.graph.user\",\"id\":\"" + Personas.OwenId + "\",\"displayName\":\"Owen Achterberg\"}],\"@odata.nextLink\":" + System.Text.Json.JsonSerializer.Serialize(nextLink) + "}";
        using DirectoryRig rig = DirectoryRig.Create(RecordedHttpHandler.Only(RecordedHttpHandler.Exchange("GET", Members105, (200, page, null))));

        DirectoryAnswer<IReadOnlyList<DirectoryObject>> members = rig.Graph.GroupMembers(Owen, Group105);

        Assert.False(members.Answered);
        Assert.Contains("next page link to another address", members.Problem, StringComparison.Ordinal);
        SeenRequest only = Assert.Single(rig.Handler.Requests);
        Assert.Equal(Members105, Unescaped(only.Url));
    }

    [Fact]
    public void AgentsAndWorkloadsNeverReadTheDirectory()
    {
        using DirectoryRig rig = DirectoryRig.Create(RecordedHttpHandler.Create());
        var callers = new[]
        {
            new BackendCaller(Personas.AgentUserId, PrincipalKind.AgentUser, Personas.ClaudeCode, new[] { Personas.OwnerGroup }),
            new BackendCaller("00000000-0000-4000-8000-000000000402", PrincipalKind.AgentIdentity, Personas.ClaudeCode),
            new BackendCaller(Personas.RunnerId, PrincipalKind.ServicePrincipal, "00000000-0000-4000-8000-000000000311"),
            new BackendCaller(Personas.PipelineId, PrincipalKind.ManagedIdentity, "00000000-0000-4000-8000-000000000312"),
        };

        foreach (BackendCaller caller in callers)
        {
            var problems = new[]
            {
                rig.Graph.GroupMembers(caller, Group105).Problem,
                rig.Graph.Group(caller, Group105).Problem,
                rig.Graph.UserState(caller, Personas.PriyaId).Problem,
                rig.Graph.DirectAssignments(caller).Problem,
                rig.Inventory.Query(caller, DirectoryOptions.PowerPlatformEstate, null, null).Problem,
            };
            Assert.All(problems, p => Assert.Contains("Only a person reads", p, StringComparison.Ordinal));
        }

        Assert.Empty(rig.Handler.Requests);
        Assert.Empty(rig.Tokens.Asked);
    }

    [Fact]
    public void ARefusedTokenStopsTheReadWithItsReasonAndSendsNothing()
    {
        using DirectoryRig rig = DirectoryRig.Create(RecordedHttpHandler.Create());
        rig.Tokens.RefuseWith = "An administrator has not granted consent for the product's API to call Microsoft Graph on behalf of people.";

        DirectoryAnswer<IReadOnlyList<DirectoryObject>> members = rig.Graph.GroupMembers(Owen, Group105);
        DirectoryAnswer<InventoryPage> page = rig.Inventory.Query(Owen, DirectoryOptions.PowerPlatformEstate, null, null);

        Assert.Equal(rig.Tokens.RefuseWith, members.Problem);
        Assert.Equal(rig.Tokens.RefuseWith, page.Problem);
        Assert.Empty(rig.Handler.Requests);
    }

    [Fact]
    public void TheRequestsBudgetStopsFurtherReads()
    {
        var context = new DefaultHttpContext();
        context.Items[DirectoryHttp.BudgetKey] = new DirectoryBudget(TimeSpan.Zero);
        using DirectoryRig rig = DirectoryRig.Create(RecordedHttpHandler.Create(), accessor: new HttpContextAccessor { HttpContext = context });

        DirectoryAnswer<DirectoryObject?> group = rig.Graph.Group(Owen, Group105);

        Assert.False(group.Answered);
        Assert.Contains("used up their 0 second budget", group.Problem, StringComparison.Ordinal);
        Assert.Empty(rig.Handler.Requests);
        Assert.Empty(rig.Tokens.Asked);
    }

    [Fact]
    public void EveryRequestCarriesOnlyTheTokenIssuedForItsServiceAndNoTokenIsLogged()
    {
        using DirectoryRig rig = DirectoryRig.Create(RecordedHttpHandler.Create(
            RecordedHttpHandler.Exchange("GET", Graph + "groups/" + Personas.ReaderGroup + "?$select=id,displayName", (500, "{\"error\":{\"code\":\"serviceNotAvailable\",\"message\":\"Fictional.\"}}", null))));

        rig.Graph.GroupMembers(Owen, Group105);
        rig.Graph.Group(Owen, Group105);
        rig.Graph.Group(Owen, Personas.ReaderGroup);
        rig.Graph.UserState(Owen, Personas.NadiaId);
        rig.Graph.DirectAssignments(Owen);
        InventoryPage first = Answer(rig.Inventory.Query(Owen, DirectoryOptions.PowerPlatformEstate, null, null));
        rig.Inventory.Query(Owen, DirectoryOptions.PowerPlatformEstate, null, first.NextSkipToken);

        Assert.True(rig.Handler.Requests.Count >= 10, string.Join("\n", rig.Handler.Requests));
        foreach (SeenRequest request in rig.Handler.Requests)
        {
            DownstreamApi api = new Uri(request.Url).Host == "graph.microsoft.com" ? DownstreamApi.MicrosoftGraph : DownstreamApi.PowerPlatform;
            Assert.Equal("Bearer " + FakeTokenSource.TokenFor(api), request.Authorization);
        }

        Assert.Contains(rig.Handler.Requests, r => r.Authorization == "Bearer " + FakeTokenSource.PowerPlatformToken);
        Assert.All(rig.Tokens.Asked, a => Assert.Equal(Personas.OwenId, a.ObjectId));

        // The failed read was logged, without the token and without the URL.
        Assert.Contains(rig.Logs.Entries, e => e.Contains("answered 500 serviceNotAvailable", StringComparison.Ordinal));
        Assert.DoesNotContain(FakeTokenSource.GraphToken, rig.Logs.All, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeTokenSource.PowerPlatformToken, rig.Logs.All, StringComparison.Ordinal);
        Assert.DoesNotContain("graph.microsoft.com", rig.Logs.All, StringComparison.Ordinal);
    }

    [Fact]
    public void KindsAndRoleKeysMapAsTheContractNamesThem()
    {
        var auth = new ProductAuthOptions();
        Assert.Equal("managed-identity", GraphDirectoryReader.KindOf(Item("#microsoft.graph.servicePrincipal", ("servicePrincipalType", "ManagedIdentity"))));
        Assert.Equal("service-principal", GraphDirectoryReader.KindOf(Item("#microsoft.graph.servicePrincipal", ("servicePrincipalType", "Application"))));
        Assert.Equal("agent-identity", GraphDirectoryReader.KindOf(Item("#microsoft.graph.agentIdentityBlueprintPrincipal")));
        Assert.Equal("agent-user", GraphDirectoryReader.KindOf(Item("#microsoft.graph.user", ("identityParentId", "00000000-0000-4000-8000-000000000402"))));
        Assert.Null(GraphDirectoryReader.KindOf(Item("#microsoft.graph.orgContact")));
        Assert.Null(GraphDirectoryReader.KindOf(Item("#microsoft.graph.device")));
        Assert.Equal("deployment-recorder", GraphDirectoryReader.RoleKeyOf("Deployment.Record", auth));
        Assert.Equal("approval-writer", GraphDirectoryReader.RoleKeyOf("Approval.Write", auth));
        Assert.Equal("platform-admin", GraphDirectoryReader.RoleKeyOf("platform-admin", auth));
        Assert.Null(GraphDirectoryReader.RoleKeyOf("Tasks.ReadWrite", auth));
    }

    [Fact]
    public async Task TheFourToolsAnswerFromTheLiveReadsInsteadOfUpstreamUnavailable()
    {
        var tokens = new FakeTokenSource();
        RecordedHttpHandler handler = RecordedHttpHandler.Create();
        await using ProductTestHost host = ProductTestHost.Start(Live(handler, tokens));

        JsonNode holders = await host.Call(Personas.Owen, "get_role_holders", "{\"roleKey\":\"platform-admin\"}");
        Assert.True(holders["ok"]!.GetValue<bool>(), holders.ToJsonString());
        Assert.Equal("Entra, live", holders["data"]!["readFrom"]!.GetValue<string>());
        Assert.Equal("Platform admins", holders["data"]!["groups"]![0]!["displayLabel"]!.GetValue<string>());
        Assert.Equal(4, holders["data"]!["members"]!.AsArray().Count);

        JsonNode bindings = await host.Call(Personas.Owen, "check_role_bindings");
        Assert.True(bindings["ok"]!.GetValue<bool>(), bindings.ToJsonString());
        string[] findings = bindings["data"]!["findings"]!.AsArray().Select(f => f!.GetValue<string>()).ToArray();
        Assert.Contains(findings, f => f.Contains("bound to architect", StringComparison.Ordinal) && f.Contains("does not exist in Entra", StringComparison.Ordinal));
        Assert.Contains("Mateo Brandt holds maker directly with no current Workload Grant.", findings);
        Assert.Contains("Release helper agent holds deployment-recorder directly with no current Workload Grant.", findings);
        Assert.All(bindings["data"]!["workloadGrants"]!.AsArray(), g => Assert.True(g!["assignmentFound"]!.GetValue<bool>(), g.ToJsonString()));

        JsonNode owners = await host.Call(Personas.Owen, "run_owner_check");
        Assert.True(owners["ok"]!.GetValue<bool>(), owners.ToJsonString());
        var states = owners["data"]!["findings"]!.AsArray().ToDictionary(f => f!["principal"]!["objectId"]!.GetValue<string>(), f => f!["directoryState"]!.GetValue<string>());
        Assert.Equal("disabled", states[Personas.NadiaId]);
        Assert.Equal("enabled", states["00000000-0000-4000-8000-000000000215"]);
        Assert.DoesNotContain(Personas.PriyaId, states.Keys);
        Assert.Equal("2026-12-15", owners["data"]!["findings"]!.AsArray().Single(f => f!["principal"]!["objectId"]!.GetValue<string>() == "00000000-0000-4000-8000-000000000215")!["leavesOn"]!.GetValue<string>());

        JsonNode first = await host.Call(Personas.Owen, "run_gap_check");
        Assert.True(first["ok"]!.GetValue<bool>(), first.ToJsonString());
        Assert.True(first["data"]!["more"]!.GetValue<bool>());
        Assert.Equal("Power Platform administrator", first["data"]!["adminRoleUsed"]!.GetValue<string>());
        string next = "{\"gapCheckRunId\":\"" + first["data"]!["gapCheckRunId"]!.GetValue<string>() + "\",\"skipToken\":\"" + first["data"]!["skipToken"]!.GetValue<string>() + "\"}";
        JsonNode second = await host.Call(Personas.Owen, "run_gap_check", next);
        Assert.True(second["ok"]!.GetValue<bool>(), second.ToJsonString());
        Assert.False(second["data"]!["more"]!.GetValue<bool>());
        Assert.Equal(new[] { "Crew Overtime Tracker", "Field Inspections" }, second["data"]!["newClaims"]!.AsArray().Select(c => c!["labelAsSeen"]!.GetValue<string>()).OrderBy(l => l, StringComparer.Ordinal).ToArray());

        // Every downstream request carried only the token the source issued for its service, never the caller's
        // credential, and the source acted for the signed-in person only.
        Assert.All(handler.Requests, r => Assert.True(r.Recorded, r.ToString()));
        Assert.All(handler.Requests, r => Assert.Equal("Bearer " + FakeTokenSource.TokenFor(new Uri(r.Url).Host == "graph.microsoft.com" ? DownstreamApi.MicrosoftGraph : DownstreamApi.PowerPlatform), r.Authorization));
        Assert.All(tokens.Asked, a => Assert.Equal(Personas.OwenId, a.ObjectId));
        Assert.DoesNotContain(FakeTokenSource.GraphToken, host.Logs.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRunnerAndTheAgentNeverReachTheReaders()
    {
        var tokens = new FakeTokenSource();
        RecordedHttpHandler handler = RecordedHttpHandler.Create();
        await using ProductTestHost host = ProductTestHost.Start(Live(handler, tokens));

        HttpCall runner = await host.Mcp(Personas.Runner, "tools/call", "{\"name\":\"get_role_holders\",\"arguments\":{\"roleKey\":\"platform-admin\"}}");
        JsonNode agent = await host.Call(Personas.Agent, "get_role_holders", "{\"roleKey\":\"platform-admin\"}");
        JsonNode agentGapCheck = await host.Call(Personas.Agent, "run_gap_check");

        Assert.Equal(403, runner.Status);
        Assert.False(agent["ok"]!.GetValue<bool>());
        Assert.Equal("not_authorized", agent["error"]!["code"]!.GetValue<string>());
        Assert.False(agentGapCheck["ok"]!.GetValue<bool>());
        Assert.Empty(handler.Requests);
        Assert.Empty(tokens.Asked);
    }

    // The local server in Development with live reads: the readers over the recorded handler and the fake token source
    // (the dev identity signs the personas in; the on-behalf-of exchange is OnBehalfOfTests' subject).
    internal static HostSettings Live(RecordedHttpHandler handler, IDownstreamTokenSource tokens)
    {
        var settings = new HostSettings { Inventory = false };
        settings.Settings["Product:Auth:ClientId"] = Personas.Audience;
        settings.Settings["Product:Directory:Mode"] = "Live";
        settings.Settings["Product:Directory:Credential:SourceType"] = DirectoryCredentialOptions.FilePath;
        settings.Settings["Product:Directory:Credential:SignedAssertionFilePath"] = "not-used-by-the-fake-token-source.jwt";
        settings.Settings["Product:DevIdentity:Personas:t-owen:DirectoryRoleIds:0"] = PowerPlatformAdministrator;
        settings.Services = services =>
        {
            services.AddProductDirectoryReads();
            services.Replace(ServiceDescriptor.Singleton(tokens));
            services.AddHttpClient(DirectoryHttp.ClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        };
        return settings;
    }

    private static T Answer<T>(DirectoryAnswer<T> answer)
    {
        Assert.True(answer.Answered, answer.Problem);
        return answer.Value;
    }

    // The values of the query's top-level where clauses on a field.
    private static string[] QueryValues(string body, string field)
    {
        return JsonNode.Parse(body)!["Clauses"]!.AsArray()
            .Where(c => c!["$type"]!.GetValue<string>() == "where" && c["FieldName"]!.GetValue<string>() == field)
            .SelectMany(c => c!["Values"]!.AsArray().Select(v => v!.GetValue<string>()))
            .ToArray();
    }

    private static string Unescaped(string url)
    {
        return Uri.UnescapeDataString(url);
    }

    private static JsonObject Item(string type, params (string Name, string Value)[] more)
    {
        var item = new JsonObject { ["@odata.type"] = type, ["id"] = "00000000-0000-4000-8000-000000000999" };
        foreach ((string name, string value) in more)
        {
            item[name] = value;
        }

        return item;
    }
}
