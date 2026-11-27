using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Product.Engine.Core.Contract;
using Product.Mcp.Core.Backend;

namespace Product.Azure.Host;

/// <summary>
/// Microsoft Graph as the signed-in person, behind the MCP core's <see cref="IDirectoryReader"/> port. Every call goes
/// through <see cref="DirectoryHttp"/> with an on-behalf-of token for Graph; nothing is stored (D-15).
/// <list type="bullet">
/// <item><c>GET /groups/{id}</c>: a role group, 404 meaning it does not exist.</item>
/// <item><c>GET /groups/{id}/members</c>: the group's direct members, every page.</item>
/// <item><c>GET /users/{id}</c>: account state and leave date, 404 meaning the user is deleted or never existed.</item>
/// <item><c>GET /servicePrincipals(appId='{api}')</c> and its <c>appRoleAssignedTo</c>: who holds the API's app roles
/// directly.</item>
/// </list>
/// Delegated permissions the API app registration needs, with admin consent: GroupMember.Read.All, User.Read.All,
/// User-LifeCycleInfo.Read.All and Application.Read.All. Graph also applies the signed-in person's own directory roles.
/// The least set is UNVERIFIED (document 24, spike 14).
/// </summary>
public sealed class GraphDirectoryReader : IDirectoryReader
{
    private const string Graph = DirectoryOptions.GraphBaseUrl;

    // The default access app role: an assignment that grants no named role.
    private const string DefaultAccessRole = "00000000-0000-0000-0000-000000000000";

    private static readonly Regex RoleKey = new("^[a-z][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant);

    // Advanced query parameters: List group members supports $select, $top and $count only with ConsistencyLevel
    // eventual, and the header is not carried to the next pages by itself.
    // https://learn.microsoft.com/graph/api/group-list-members and https://learn.microsoft.com/graph/paging
    private static readonly IReadOnlyDictionary<string, string> AdvancedQuery = new Dictionary<string, string>(StringComparer.Ordinal) { ["ConsistencyLevel"] = "eventual" };

    private readonly DirectoryHttp _http;
    private readonly IOptions<ProductAuthOptions> _auth;
    private readonly IOptions<DirectoryOptions> _options;

    /// <summary>Creates the reader.</summary>
    public GraphDirectoryReader(DirectoryHttp http, IOptions<ProductAuthOptions> auth, IOptions<DirectoryOptions> options)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _auth = auth ?? throw new ArgumentNullException(nameof(auth));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public DirectoryAnswer<IReadOnlyList<DirectoryObject>> GroupMembers(BackendCaller asPerson, string groupObjectId)
    {
        if (PersonOnly<IReadOnlyList<DirectoryObject>>(asPerson) is { } refused)
        {
            return refused;
        }

        if (!DirectoryJson.IsGuid(groupObjectId))
        {
            return DirectoryAnswer<IReadOnlyList<DirectoryObject>>.Of(Array.Empty<DirectoryObject>());
        }

        // Known issue: on v1.0 this list does not return service principals (and so agent identities) that are members.
        // https://learn.microsoft.com/graph/known-issues#get-groupsidmembers-doesnt-return-service-principals-in-v10
        DirectoryReply reply = _http.GetPages(asPerson, Graph + "groups/" + groupObjectId + "/members?$count=true&$top=999&$select=id,displayName", AdvancedQuery);
        if (!reply.Answered)
        {
            return DirectoryAnswer<IReadOnlyList<DirectoryObject>>.Unavailable(reply.Problem!);
        }

        if (reply.Status == 404)
        {
            return DirectoryAnswer<IReadOnlyList<DirectoryObject>>.Of(Array.Empty<DirectoryObject>());
        }

        if (!reply.Ok)
        {
            return DirectoryAnswer<IReadOnlyList<DirectoryObject>>.Unavailable(Problem(reply, "read the members of a role group", "GroupMember.Read.All and User.Read.All"));
        }

        var members = new List<DirectoryObject>();
        foreach (JsonObject item in reply.Items)
        {
            string? id = DirectoryJson.Text(item, "id");
            string? kind = KindOf(item);
            if (id == null || kind == null)
            {
                continue;
            }

            // With too little permission Graph still lists a member, but only its type and ID.
            // https://learn.microsoft.com/graph/permissions-overview#limited-information-returned-for-inaccessible-member-objects
            members.Add(new DirectoryObject(id, DirectoryJson.Text(item, "displayName") ?? id, kind));
        }

        return DirectoryAnswer<IReadOnlyList<DirectoryObject>>.Of(members);
    }

    /// <summary>
    /// The groups among <paramref name="groupObjectIds"/> the member is in, in one paged read for the member: their
    /// transitive group memberships (security review: list_teams with memberObjectId read every team's members,
    /// one Graph call each).
    /// </summary>
    public DirectoryAnswer<IReadOnlyList<string>> MemberGroups(BackendCaller asPerson, string memberObjectId, IReadOnlyList<string> groupObjectIds, out int notRead)
    {
        notRead = 0;
        if (PersonOnly<IReadOnlyList<string>>(asPerson) is { } refused)
        {
            return refused;
        }

        if (!DirectoryJson.IsGuid(memberObjectId) || groupObjectIds == null || groupObjectIds.Count == 0)
        {
            return DirectoryAnswer<IReadOnlyList<string>>.Of(Array.Empty<string>());
        }

        DirectoryReply reply = _http.GetPages(asPerson, Graph + "users/" + memberObjectId + "/transitiveMemberOf/microsoft.graph.group?$count=true&$top=999&$select=id", AdvancedQuery);
        if (!reply.Answered)
        {
            return DirectoryAnswer<IReadOnlyList<string>>.Unavailable(reply.Problem!);
        }

        if (reply.Status == 404)
        {
            return DirectoryAnswer<IReadOnlyList<string>>.Of(Array.Empty<string>());
        }

        if (!reply.Ok)
        {
            return DirectoryAnswer<IReadOnlyList<string>>.Unavailable(Problem(reply, "read the groups a person is in", "GroupMember.Read.All and User.Read.All"));
        }

        var wanted = new HashSet<string>(groupObjectIds, StringComparer.OrdinalIgnoreCase);
        var found = new List<string>();
        foreach (JsonObject item in reply.Items)
        {
            if (DirectoryJson.Text(item, "id") is string id && wanted.Contains(id))
            {
                found.Add(groupObjectIds.First(g => string.Equals(g, id, StringComparison.OrdinalIgnoreCase)));
            }
        }

        return DirectoryAnswer<IReadOnlyList<string>>.Of(found);
    }

    /// <inheritdoc />
    public DirectoryAnswer<DirectoryObject?> Group(BackendCaller asPerson, string groupObjectId)
    {
        if (PersonOnly<DirectoryObject?>(asPerson) is { } refused)
        {
            return refused;
        }

        if (!DirectoryJson.IsGuid(groupObjectId))
        {
            return DirectoryAnswer<DirectoryObject?>.Of(null);
        }

        DirectoryReply reply = _http.Get(DownstreamApi.MicrosoftGraph, asPerson, Graph + "groups/" + groupObjectId + "?$select=id,displayName");
        if (!reply.Answered)
        {
            return DirectoryAnswer<DirectoryObject?>.Unavailable(reply.Problem!);
        }

        if (reply.Status == 404)
        {
            return DirectoryAnswer<DirectoryObject?>.Of(null);
        }

        if (!reply.Ok || reply.Body is not JsonObject group)
        {
            return DirectoryAnswer<DirectoryObject?>.Unavailable(Problem(reply, "read a role group", "GroupMember.Read.All"));
        }

        string id = DirectoryJson.Text(group, "id") ?? groupObjectId;
        return DirectoryAnswer<DirectoryObject?>.Of(new DirectoryObject(id, DirectoryJson.Text(group, "displayName") ?? id, "group"));
    }

    /// <inheritdoc />
    public DirectoryAnswer<DirectoryUserState> UserState(BackendCaller asPerson, string objectId)
    {
        if (PersonOnly<DirectoryUserState>(asPerson) is { } refused)
        {
            return refused;
        }

        if (!DirectoryJson.IsGuid(objectId))
        {
            return DirectoryAnswer<DirectoryUserState>.Of(new DirectoryUserState("unknown"));
        }

        // Reading employeeLeaveDateTime needs User-LifeCycleInfo.Read.All and, delegated, the signed-in admin's Lifecycle
        // Workflows Administrator or Global Reader role. https://learn.microsoft.com/graph/api/user-get
        DirectoryReply reply = _http.Get(DownstreamApi.MicrosoftGraph, asPerson, Graph + "users/" + objectId + "?$select=id,displayName,accountEnabled,employeeLeaveDateTime");
        if (!reply.Answered)
        {
            return DirectoryAnswer<DirectoryUserState>.Unavailable(reply.Problem!);
        }

        // Graph answers 404 for an ID no user has: a deleted user (also while it can still be restored) or one that never
        // existed. The owner assignment named a person, so the owner check reads it as deleted.
        if (reply.Status == 404)
        {
            return DirectoryAnswer<DirectoryUserState>.Of(new DirectoryUserState("deleted"));
        }

        if (!reply.Ok || reply.Body is not JsonObject user)
        {
            return DirectoryAnswer<DirectoryUserState>.Unavailable(Problem(
                reply,
                "read an owner's account state and leave date",
                "User.Read.All and User-LifeCycleInfo.Read.All",
                "To read a leave date you also need the Lifecycle Workflows Administrator or Global Reader role."));
        }

        string state = DirectoryJson.Flag(user, "accountEnabled") switch
        {
            true => "enabled",
            false => "disabled",
            null => "unknown",
        };
        return DirectoryAnswer<DirectoryUserState>.Of(new DirectoryUserState(state, DirectoryJson.Moment(user, "employeeLeaveDateTime"), DirectoryJson.Text(user, "displayName")));
    }

    /// <inheritdoc />
    public DirectoryAnswer<IReadOnlyList<DirectAssignment>> DirectAssignments(BackendCaller asPerson)
    {
        if (PersonOnly<IReadOnlyList<DirectAssignment>>(asPerson) is { } refused)
        {
            return refused;
        }

        ProductAuthOptions auth = _auth.Value;
        if (!DirectoryJson.IsGuid(auth.ClientId))
        {
            return DirectoryAnswer<IReadOnlyList<DirectAssignment>>.Unavailable("The API app's client ID is not configured (Product:Auth:ClientId), so its app role assignments cannot be read.");
        }

        // The API's service principal by its appId, with the app roles it defines.
        // https://learn.microsoft.com/graph/api/serviceprincipal-get and
        // https://learn.microsoft.com/graph/api/serviceprincipal-list-approleassignedto
        DirectoryReply sp = _http.Get(DownstreamApi.MicrosoftGraph, asPerson, Graph + "servicePrincipals(appId='" + auth.ClientId + "')?$select=id,appId,appRoles");
        if (!sp.Answered)
        {
            return DirectoryAnswer<IReadOnlyList<DirectAssignment>>.Unavailable(sp.Problem!);
        }

        if (sp.Status == 404)
        {
            return DirectoryAnswer<IReadOnlyList<DirectAssignment>>.Unavailable("The product's API app has no service principal in this tenant, so no app role assignment can be read.");
        }

        string? spId = sp.Ok && sp.Body is JsonObject spBody ? DirectoryJson.Text(spBody, "id") : null;
        if (!sp.Ok || spId == null || !DirectoryJson.IsGuid(spId))
        {
            return DirectoryAnswer<IReadOnlyList<DirectAssignment>>.Unavailable(Problem(sp, "read the product's API app", "Application.Read.All"));
        }

        var roleValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (((JsonObject)sp.Body!)["appRoles"] is JsonArray roles)
        {
            foreach (JsonObject role in roles.OfType<JsonObject>())
            {
                string? roleId = DirectoryJson.Text(role, "id");
                string? value = DirectoryJson.Text(role, "value");
                if (roleId != null && value != null)
                {
                    roleValues[roleId] = value;
                }
            }
        }

        DirectoryReply assigned = _http.GetPages(asPerson, Graph + "servicePrincipals/" + spId + "/appRoleAssignedTo");
        if (!assigned.Answered)
        {
            return DirectoryAnswer<IReadOnlyList<DirectAssignment>>.Unavailable(assigned.Problem!);
        }

        if (!assigned.Ok)
        {
            return DirectoryAnswer<IReadOnlyList<DirectAssignment>>.Unavailable(Problem(
                assigned,
                "read who holds the product's app roles",
                "Application.Read.All",
                "Listing app role assignments also needs a directory role that reads them, such as Directory Readers or Cloud Application Administrator."));
        }

        var result = new List<DirectAssignment>();
        var kinds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int lookups = 0;
        foreach (JsonObject assignment in assigned.Items)
        {
            string? principalId = DirectoryJson.Text(assignment, "principalId");
            string? principalType = DirectoryJson.Text(assignment, "principalType");
            string? appRoleId = DirectoryJson.Text(assignment, "appRoleId");

            // Groups are the bound way to hold a role; check_role_bindings compares them with the bindings in force.
            if (principalId == null || principalType == null || appRoleId == null || string.Equals(principalType, "Group", StringComparison.OrdinalIgnoreCase)
                || string.Equals(appRoleId, DefaultAccessRole, StringComparison.OrdinalIgnoreCase)
                || !roleValues.TryGetValue(appRoleId, out string? value))
            {
                continue;
            }

            string? roleKey = RoleKeyOf(value, auth);
            if (roleKey == null)
            {
                continue;
            }

            if (!kinds.TryGetValue(principalId, out string? kind))
            {
                kind = lookups < _options.Value.MaxPrincipalLookups ? LookUpKind(asPerson, principalId, principalType) : DefaultKind(principalType);
                lookups++;
                kinds[principalId] = kind;
            }

            string label = DirectoryJson.Text(assignment, "principalDisplayName") ?? principalId;
            result.Add(new DirectAssignment(new DirectoryObject(principalId, label, kind), roleKey, "Entra app role assignment " + value + " on the product's API app"));
        }

        return DirectoryAnswer<IReadOnlyList<DirectAssignment>>.Of(result);
    }

    // The engine principal kind of a directory object from its @odata.type: user, agent-user, group, service-principal,
    // managed-identity or agent-identity. Null for objects that cannot hold a role (devices, contacts, anything else).
    // An agent user is a subtype of user (#microsoft.graph.agentUser) and an agent identity a subtype of service principal
    // (#microsoft.graph.agentIdentity). https://learn.microsoft.com/graph/api/resources/agentuser and
    // https://learn.microsoft.com/graph/api/resources/agentidentity. A guest is a user: the contract's principal kinds have
    // no guest. UNVERIFIED: that a group member list reports agent users with the agentUser type.
    internal static string? KindOf(JsonObject item)
    {
        string type = DirectoryJson.Text(item, "@odata.type") ?? string.Empty;
        switch (type.TrimStart('#'))
        {
            case "microsoft.graph.user":
                return DirectoryJson.Text(item, "identityParentId") != null ? "agent-user" : "user";
            case "microsoft.graph.agentUser":
                return "agent-user";
            case "microsoft.graph.group":
                return "group";
            case "microsoft.graph.agentIdentity":
            case "microsoft.graph.agentIdentityBlueprintPrincipal":
                return "agent-identity";
            case "microsoft.graph.servicePrincipal":
                return string.Equals(DirectoryJson.Text(item, "servicePrincipalType"), "ManagedIdentity", StringComparison.OrdinalIgnoreCase) ? "managed-identity" : "service-principal";
            default:
                return null;
        }
    }

    // The product role key an app role value grants: the workload app roles map to their workload roles, and a person's
    // app role value is the product role key itself (infra/README, app registration steps 5 and 6).
    internal static string? RoleKeyOf(string value, ProductAuthOptions auth)
    {
        if (string.Equals(value, auth.RecordAppRole, StringComparison.Ordinal))
        {
            return "deployment-recorder";
        }

        if (string.Equals(value, auth.ApprovalAppRole, StringComparison.Ordinal))
        {
            return "approval-writer";
        }

        return RoleKey.IsMatch(value) ? value : null;
    }

    // Tells an assignee's kind from Graph (an agent user, a managed identity, an agent identity). A failed lookup keeps the
    // kind the assignment names; it never fails the read.
    private string LookUpKind(BackendCaller asPerson, string principalId, string principalType)
    {
        if (!DirectoryJson.IsGuid(principalId))
        {
            return DefaultKind(principalType);
        }

        string url = string.Equals(principalType, "ServicePrincipal", StringComparison.OrdinalIgnoreCase)
            ? Graph + "servicePrincipals/" + principalId + "?$select=id,servicePrincipalType"
            : Graph + "users/" + principalId + "?$select=id,identityParentId";
        DirectoryReply reply = _http.Get(DownstreamApi.MicrosoftGraph, asPerson, url);
        if (!reply.Ok || reply.Body is not JsonObject found)
        {
            return DefaultKind(principalType);
        }

        if (DirectoryJson.Text(found, "@odata.type") == null)
        {
            found = (JsonObject)found.DeepClone();
            found["@odata.type"] = string.Equals(principalType, "ServicePrincipal", StringComparison.OrdinalIgnoreCase) ? "#microsoft.graph.servicePrincipal" : "#microsoft.graph.user";
        }

        return KindOf(found) ?? DefaultKind(principalType);
    }

    private static string DefaultKind(string principalType)
    {
        return string.Equals(principalType, "ServicePrincipal", StringComparison.OrdinalIgnoreCase) ? "service-principal" : "user";
    }

    // Only a person reads the directory through the product: never an agent identity, an agent user or a workload.
    private static DirectoryAnswer<T>? PersonOnly<T>(BackendCaller asPerson)
    {
        if (asPerson == null)
        {
            throw new ArgumentNullException(nameof(asPerson));
        }

        return asPerson.PrincipalKind == PrincipalKind.User
            ? null
            : DirectoryAnswer<T>.Unavailable("Only a person reads the directory through the product. Agent identities, agent users and workloads never do.");
    }

    // A plain message for a Graph answer that is not the data: a 401 or 403 names the permissions and consent, anything
    // else names the status and Graph's error code.
    private static string Problem(DirectoryReply reply, string what, string permissions, string? roleNote = null)
    {
        string code = reply.ErrorCode == null ? string.Empty : " (" + reply.ErrorCode + ")";
        if (reply.Status == 401)
        {
            return "Microsoft Graph did not accept the exchanged token to " + what + code + ".";
        }

        if (reply.Status == 403)
        {
            return "Microsoft Graph refused to " + what + code + ". The product's API app needs the delegated Graph permissions "
                + permissions + " with admin consent, and Graph also checks your own directory roles." + (roleNote == null ? string.Empty : " " + roleNote);
        }

        return "Microsoft Graph answered HTTP " + reply.Status + code + " when asked to " + what + ".";
    }
}
