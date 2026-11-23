using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Product.Engine.Core.Contract;

namespace Product.Engine.Core;

// Who or what is acting (document 25, section 5.1).
// The adapter builds it from the execution context or the token. The engine never reads a direct role assignment (D-11).
public sealed class ActorInfo
{
    public ActorInfo(
        string principalId,
        PrincipalKind principalKind,
        ActorKindMeaning actorKind,
        IEnumerable<string>? roleKeys = null,
        string? onBehalfOfPrincipalId = null,
        bool isEngineNested = false)
    {
        if (string.IsNullOrEmpty(principalId))
        {
            throw new ArgumentException("An actor needs the principal ID of the caller.", nameof(principalId));
        }

        if (!Enum.IsDefined(typeof(PrincipalKind), principalKind))
        {
            throw new ArgumentOutOfRangeException(nameof(principalKind));
        }

        if (!Enum.IsDefined(typeof(ActorKindMeaning), actorKind))
        {
            throw new ArgumentOutOfRangeException(nameof(actorKind));
        }

        // Roles are a set. Sorting them makes the record, and any hash over it, independent of input order.
        string[] roles = (roleKeys ?? Enumerable.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(role => role, StringComparer.Ordinal)
            .ToArray();
        foreach (string role in roles)
        {
            if (!ConfigurationKey.IsValid(role))
            {
                throw new ArgumentException("A role key must use the configuration key format.", nameof(roleKeys));
            }
        }

        PrincipalId = principalId;
        PrincipalKind = principalKind;
        ActorKind = actorKind;
        RoleKeys = new ReadOnlyCollection<string>(roles);
        OnBehalfOfPrincipalId = string.IsNullOrEmpty(onBehalfOfPrincipalId) ? null : onBehalfOfPrincipalId;
        IsEngineNested = isEngineNested;
    }

    // The Entra object ID of the caller (O05 Entra Object Id).
    public string PrincipalId { get; }

    public PrincipalKind PrincipalKind { get; }

    public ActorKindMeaning ActorKind { get; }

    // The person an AI client or pipeline acted for. Null when a person acted directly.
    public string? OnBehalfOfPrincipalId { get; }

    // The product roles (C29) the caller holds now, resolved from Entra group bindings (C30).
    // Distinct and sorted ordinal.
    public IReadOnlyList<string> RoleKeys { get; }

    // True only for a write the engine made itself, inside an earlier engine operation (document 25, section 11.2).
    public bool IsEngineNested { get; }

    public bool HoldsRole(string roleKey)
    {
        foreach (string role in RoleKeys)
        {
            if (string.Equals(role, roleKey, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
