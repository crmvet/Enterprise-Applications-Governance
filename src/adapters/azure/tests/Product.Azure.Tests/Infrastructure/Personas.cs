using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Product.Engine.Core.Contract;
using Product.Mcp.Testing;

namespace Product.Azure.Tests;

// One fictional caller: the claims an Entra token for it would carry. The HTTP host signs it in through the local
// development identity (a persona named in a header); the in-process world signs the same claims in through the MCP
// core's mock identity (an opaque "mock-" token). Every ID is a patterned placeholder.
internal sealed class Persona
{
    public Persona(string name, string objectId, string principalKind = "user", string[]? groups = null, bool appOnly = false, string[]? appRoles = null, string[]? scopes = null, string tenantId = Personas.Tenant, string clientAppId = Personas.ClaudeCode, string? mockToken = null)
    {
        Name = name;
        ObjectId = objectId;
        PrincipalKind = principalKind;
        Groups = groups ?? Array.Empty<string>();
        AppOnly = appOnly;
        AppRoles = appRoles ?? Array.Empty<string>();
        Scopes = scopes ?? new[] { "mcp.access", "api.access" };
        TenantId = tenantId;
        ClientAppId = clientAppId;
        MockToken = mockToken ?? "mock-" + name;
    }

    public string Name { get; }

    public string ObjectId { get; }

    public string PrincipalKind { get; }

    public string[] Groups { get; }

    public bool AppOnly { get; }

    public string[] AppRoles { get; }

    public string[] Scopes { get; }

    public string TenantId { get; }

    public string ClientAppId { get; }

    // The bearer value the in-process world's mock identity maps to these claims.
    public string MockToken { get; }

    // The development identity settings for this persona (section Product:DevIdentity:Personas:<name>).
    public IEnumerable<KeyValuePair<string, string>> Settings()
    {
        string prefix = "Product:DevIdentity:Personas:" + Name + ":";
        yield return Pair(prefix + "ObjectId", ObjectId);
        yield return Pair(prefix + "TenantId", TenantId);
        yield return Pair(prefix + "ClientAppId", ClientAppId);
        yield return Pair(prefix + "PrincipalKind", PrincipalKind);
        yield return Pair(prefix + "AppOnly", AppOnly ? "true" : "false");
        for (int i = 0; i < Groups.Length; i++)
        {
            yield return Pair(prefix + "Groups:" + i.ToString(CultureInfo.InvariantCulture), Groups[i]);
        }

        for (int i = 0; i < Scopes.Length; i++)
        {
            yield return Pair(prefix + "Scopes:" + i.ToString(CultureInfo.InvariantCulture), Scopes[i]);
        }

        for (int i = 0; i < AppRoles.Length; i++)
        {
            yield return Pair(prefix + "AppRoles:" + i.ToString(CultureInfo.InvariantCulture), AppRoles[i]);
        }
    }

    // The same claims as a mock token for the MCP core's mock identity.
    public MockToken Token(DateTime expiresOnUtc)
    {
        EngineContract.PrincipalKinds.TryParse(PrincipalKind, out PrincipalKind kind);
        return new MockToken(ObjectId, Personas.Audience, TenantId, ClientAppId, expiresOnUtc)
        {
            PrincipalKind = kind,
            GroupObjectIds = Groups,
            Scopes = Scopes,
            IsAppOnly = AppOnly,
        };
    }

    private static KeyValuePair<string, string> Pair(string key, string value)
    {
        return new KeyValuePair<string, string>(key, value);
    }
}

// The callers of the tests: the walkthrough's people and workloads (the same object IDs and groups as the MCP core's
// walkthrough world and the demo seed), and a few more for the refusals.
internal static class Personas
{
    public const string Tenant = "00000000-0000-4000-8000-0000000000aa";
    public const string Audience = "00000000-0000-4000-8000-0000000000ab";
    public const string ClaudeCode = "00000000-0000-4000-8000-0000000000ac";

    // An AI client that is not a Known Client of the demo binding.
    public const string OtherClient = "00000000-0000-4000-8000-0000000000ad";

    // The product's own web app (Product:WebApp:ClientIds of every test host).
    public const string WebApp = "00000000-0000-4000-8000-0000000000ae";
    public const string OtherTenant = "00000000-0000-4000-8000-0000000000fe";

    public const string RequesterGroup = "00000000-0000-4000-8000-000000000101";
    public const string MakerGroup = "00000000-0000-4000-8000-000000000102";
    public const string OwnerGroup = "00000000-0000-4000-8000-000000000103";
    public const string ChangeManagerGroup = "00000000-0000-4000-8000-000000000104";
    public const string PlatformAdminGroup = "00000000-0000-4000-8000-000000000105";
    public const string ConfigurationAdminGroup = "00000000-0000-4000-8000-000000000106";
    public const string ReaderGroup = "00000000-0000-4000-8000-000000000107";
    public const string ArchitectGroup = "00000000-0000-4000-8000-000000000108";

    public const string PriyaId = "00000000-0000-4000-8000-000000000211";
    public const string NadiaId = "00000000-0000-4000-8000-000000000212";
    public const string OwenId = "00000000-0000-4000-8000-000000000213";
    public const string MateoId = "00000000-0000-4000-8000-000000000214";
    public const string AllRolesId = "00000000-0000-4000-8000-000000000221";
    public const string RunnerId = "00000000-0000-4000-8000-000000000301";
    public const string PipelineId = "00000000-0000-4000-8000-000000000302";
    public const string UnknownWorkloadId = "00000000-0000-4000-8000-000000000303";
    public const string AgentUserId = "00000000-0000-4000-8000-000000000401";

    // The walkthrough estate (src/adapters/azure/seed/walkthrough-world.json).
    public const string FieldOperations = "00000000-0000-4000-8000-000000000501";
    public const string InspectionScheduler = "00000000-0000-4000-8000-000000000511";
    public const string RoutePlanner = "00000000-0000-4000-8000-000000000512";
    public const string FieldOpsProd = "00000000-0000-4000-8000-000000000522";

    public static readonly Persona Priya = new("t-priya", PriyaId, groups: new[] { RequesterGroup, MakerGroup }, mockToken: "mock-priya-castell");
    public static readonly Persona Nadia = new("t-nadia", NadiaId, groups: new[] { OwnerGroup }, mockToken: "mock-nadia-ferreira");
    public static readonly Persona Owen = new("t-owen", OwenId, groups: new[] { PlatformAdminGroup }, mockToken: "mock-owen-achterberg");
    public static readonly Persona Mateo = new("t-mateo", MateoId, groups: new[] { ReaderGroup }, mockToken: "mock-mateo-brandt");
    public static readonly Persona Pipeline = new("t-pipeline", PipelineId, "service-principal", appOnly: true, appRoles: new[] { "Deployment.Record" }, scopes: Array.Empty<string>(), mockToken: "mock-release-pipeline-app-only");
    public static readonly Persona Runner = new("t-runner", RunnerId, "service-principal", appOnly: true, appRoles: new[] { "Approval.Write" }, scopes: Array.Empty<string>());

    // Every role group, so every tool the Tool Policy serves is listed.
    public static readonly Persona AllRoles = new("t-all-roles", AllRolesId, groups: new[] { RequesterGroup, MakerGroup, OwnerGroup, ChangeManagerGroup, PlatformAdminGroup, ConfigurationAdminGroup, ReaderGroup, ArchitectGroup });

    // An agent user (Entra Agent ID) in the owners group: it signs in, but resolves to no product role.
    public static readonly Persona Agent = new("t-agent", AgentUserId, "agent-user", groups: new[] { OwnerGroup });

    // The same people signed in to the product's own web app: tokens for the api.access scope that the web app asked for.
    public static readonly Persona NadiaWeb = new("t-nadia-web", NadiaId, groups: new[] { OwnerGroup }, scopes: new[] { "api.access" }, clientAppId: WebApp);
    public static readonly Persona OwenWeb = new("t-owen-web", OwenId, groups: new[] { PlatformAdminGroup }, scopes: new[] { "api.access" }, clientAppId: WebApp);
    public static readonly Persona PriyaWeb = new("t-priya-web", PriyaId, groups: new[] { RequesterGroup, MakerGroup }, scopes: new[] { "api.access" }, clientAppId: WebApp);
    public static readonly Persona MateoWeb = new("t-mateo-web", MateoId, groups: new[] { ReaderGroup }, scopes: new[] { "api.access" }, clientAppId: WebApp);

    // The agent user with a token the web app asked for: an agent identity holds no role on any entry.
    public static readonly Persona AgentWeb = new("t-agent-web", AgentUserId, "agent-user", groups: new[] { OwnerGroup }, scopes: new[] { "api.access" }, clientAppId: WebApp);

    // Priya through an AI client that is not a Known Client.
    public static readonly Persona PriyaOtherClient = new("t-priya-other-client", PriyaId, groups: new[] { RequesterGroup, MakerGroup }, clientAppId: OtherClient);

    // Priya's claims from another tenant.
    public static readonly Persona OtherTenantPriya = new("t-other-tenant", PriyaId, groups: new[] { RequesterGroup, MakerGroup }, tenantId: OtherTenant);

    // Priya with a token for another API (no product scope).
    public static readonly Persona NoScope = new("t-no-scope", PriyaId, groups: new[] { RequesterGroup, MakerGroup }, scopes: new[] { "User.Read" });

    // An app-only identity with the Approval.Write app role but no Workload Grant in the configuration.
    public static readonly Persona UnknownWorkload = new("t-unknown-workload", UnknownWorkloadId, "service-principal", appOnly: true, appRoles: new[] { "Approval.Write", "Deployment.Record" }, scopes: Array.Empty<string>());

    // The release pipeline's identity given the Approval.Write app role too: its Workload Grant is for deployment-recorder only.
    public static readonly Persona PipelineAsRunner = new("t-pipeline-as-runner", PipelineId, "service-principal", appOnly: true, appRoles: new[] { "Approval.Write" }, scopes: Array.Empty<string>());

    public static readonly Persona[] All = { Priya, Nadia, Owen, Mateo, Pipeline, Runner, AllRoles, Agent, OtherTenantPriya, NoScope, UnknownWorkload, PipelineAsRunner, NadiaWeb, OwenWeb, PriyaWeb, MateoWeb, AgentWeb, PriyaOtherClient };

    // The persona that signs a transcript token in.
    public static Persona ForMockToken(string token)
    {
        return All.First(p => string.Equals(p.MockToken, token, StringComparison.Ordinal));
    }
}
