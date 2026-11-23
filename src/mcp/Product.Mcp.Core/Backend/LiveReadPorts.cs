using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Product.Mcp.Core.Backend;

// Live Microsoft reads as the signed-in person, behind ports. Tests bind recorded fixtures; no real client is built
// yet. The server exchanges the person's token for a Graph or Power Platform API token on behalf of the person
// (document 24, section 4.1, hop 5). Rosters and the inventory are read live and never stored (D-15). With no client bound, the tools that need one answer upstream_unavailable.

// Microsoft Graph as the person: group members, a group's existence and users' account state.
public interface IDirectoryReader
{
    // The members of an Entra group, as far as Graph lets the person see them.
    DirectoryAnswer<IReadOnlyList<DirectoryObject>> GroupMembers(BackendCaller asPerson, string groupObjectId);

    // The group, or null when it does not exist.
    DirectoryAnswer<DirectoryObject?> Group(BackendCaller asPerson, string groupObjectId);

    // A user's account state and leave date.
    DirectoryAnswer<DirectoryUserState> UserState(BackendCaller asPerson, string objectId);

    // Direct app role assignments of the server's app, with the role each grants (for check_role_bindings).
    DirectoryAnswer<IReadOnlyList<DirectAssignment>> DirectAssignments(BackendCaller asPerson);
}

// The Power Platform inventory as the admin (POST {PowerPlatformAPI url}/resourcequery/resources/query, delegated only).
public interface IInventoryReader
{
    DirectoryAnswer<InventoryPage> Query(BackendCaller asAdmin, string estateKey, IReadOnlyList<string>? environmentIds, string? skipToken);
}

// One answer from a live read: the value, or why the upstream did not answer. Never a token.
public sealed class DirectoryAnswer<T>
{
    private DirectoryAnswer(bool answered, T value, string? problem)
    {
        Answered = answered;
        Value = value;
        Problem = problem;
    }

    public bool Answered { get; }

    public T Value { get; }

    public string? Problem { get; }

    public static DirectoryAnswer<T> Of(T value)
    {
        return new DirectoryAnswer<T>(true, value, null);
    }

    public static DirectoryAnswer<T> Unavailable(string problem)
    {
        return new DirectoryAnswer<T>(false, default!, problem);
    }
}

public sealed class DirectoryObject
{
    public DirectoryObject(string objectId, string displayLabel, string principalKind = "user")
    {
        ObjectId = objectId;
        DisplayLabel = displayLabel;
        PrincipalKind = principalKind;
    }

    public string ObjectId { get; }

    public string DisplayLabel { get; }

    // An engine principal kind: user, group, service-principal, managed-identity, agent-identity, agent-user.
    public string PrincipalKind { get; }
}

public sealed class DirectoryUserState
{
    public DirectoryUserState(string state, DateTime? leavesOnUtc = null, string? displayLabel = null)
    {
        State = state;
        LeavesOnUtc = leavesOnUtc;
        DisplayLabel = displayLabel;
    }

    // enabled, disabled, deleted or unknown.
    public string State { get; }

    public DateTime? LeavesOnUtc { get; }

    public string? DisplayLabel { get; }
}

public sealed class DirectAssignment
{
    public DirectAssignment(DirectoryObject principal, string roleKey, string where)
    {
        Principal = principal;
        RoleKey = roleKey;
        Where = where;
    }

    public DirectoryObject Principal { get; }

    public string RoleKey { get; }

    // Where the assignment was found, such as "app role assignment on the server's app".
    public string Where { get; }
}

public sealed class InventoryItem
{
    public InventoryItem(string externalId, string sourceType, string labelAsSeen, string? targetAsSeen = null, string? componentKindKey = null, string? suggestedClaimantObjectId = null)
    {
        ExternalId = externalId;
        SourceType = sourceType;
        LabelAsSeen = labelAsSeen;
        TargetAsSeen = targetAsSeen;
        ComponentKindKey = componentKindKey;
        SuggestedClaimantObjectId = suggestedClaimantObjectId;
    }

    public string ExternalId { get; }

    public string SourceType { get; }

    public string LabelAsSeen { get; }

    public string? TargetAsSeen { get; }

    public string? ComponentKindKey { get; }

    public string? SuggestedClaimantObjectId { get; }
}

public sealed class InventoryPage
{
    public InventoryPage(IEnumerable<InventoryItem> items, string? nextSkipToken, string? adminRoleUsed)
    {
        Items = new ReadOnlyCollection<InventoryItem>((items ?? Enumerable.Empty<InventoryItem>()).ToList());
        NextSkipToken = nextSkipToken;
        AdminRoleUsed = adminRoleUsed;
    }

    public IReadOnlyList<InventoryItem> Items { get; }

    // Null on the last page.
    public string? NextSkipToken { get; }

    // The directory role the admin used, when the token's wids claim names it (document 24, tool 35; UNVERIFIED).
    public string? AdminRoleUsed { get; }
}
