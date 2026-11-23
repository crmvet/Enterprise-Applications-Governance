using System;
using Product.Engine.Core.Contract;

namespace Product.Engine.Core;

// One configuration row that decided an outcome (document 25, section 5.1).
// explain_rules returns these (document 24).
public sealed class RuleTrace : IEquatable<RuleTrace>
{
    public RuleTrace(string entity, string key, RuleTraceRole role)
    {
        if (string.IsNullOrEmpty(entity))
        {
            throw new ArgumentException("A rule trace names the configuration entity.", nameof(entity));
        }

        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentException("A rule trace names the deciding row's key.", nameof(key));
        }

        if (!Enum.IsDefined(typeof(RuleTraceRole), role))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }

        Entity = entity;
        Key = key;
        Role = role;
    }

    // The configuration entity of the deciding row, such as "Approval Rule".
    public string Entity { get; }

    // The deciding row's key. Child rows may use a key path such as "data-change/reads-only".
    public string Key { get; }

    // What the row did: matched, won, dropped, required or skipped.
    public RuleTraceRole Role { get; }

    public bool Equals(RuleTrace? other)
    {
        return other != null
            && string.Equals(Entity, other.Entity, StringComparison.Ordinal)
            && string.Equals(Key, other.Key, StringComparison.Ordinal)
            && Role == other.Role;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as RuleTrace);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = StringComparer.Ordinal.GetHashCode(Entity);
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(Key);
            return (hash * 31) + (int)Role;
        }
    }

    public override string ToString()
    {
        return Entity + " " + Key + " " + EngineContract.RuleTraceRoles.ToWire(Role);
    }
}
