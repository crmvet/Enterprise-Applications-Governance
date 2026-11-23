using System;
using System.Collections.Generic;
using Product.Engine.Core.Contract;

namespace Product.Engine.Core;

// Why an outcome is what it is (document 25, section 5.1).
// Immutable. Use ReasonCatalog.Create to get the catalog's severity and plain message.
public sealed class Reason : IEquatable<Reason>
{
    public Reason(string code, ReasonSeverity severity, string message, string? entity = null, string? reference = null)
    {
        if (!IsWellFormedCode(code))
        {
            throw new ArgumentException("A reason code is lowercase kebab-case parts joined by dots.", nameof(code));
        }

        if (!Enum.IsDefined(typeof(ReasonSeverity), severity))
        {
            throw new ArgumentOutOfRangeException(nameof(severity));
        }

        if (string.IsNullOrEmpty(message))
        {
            throw new ArgumentException("A reason needs a plain message.", nameof(message));
        }

        Code = code;
        Severity = severity;
        Message = message;
        Entity = string.IsNullOrEmpty(entity) ? null : entity;
        Ref = string.IsNullOrEmpty(reference) ? null : reference;
    }

    // A stable reason code, such as approval.ai-actor-not-counted.
    public string Code { get; }

    public ReasonSeverity Severity { get; }

    // One plain sentence, no HTML.
    public string Message { get; }

    // The logical entity the reason is about, such as "Approval Record". Null when none.
    public string? Entity { get; }

    // The configuration key or row ID the reason points at. Null when none.
    public string? Ref { get; }

    // Orders reasons errors first, then by code, entity and reference (ordinal). Stable on every machine.
    public static IComparer<Reason> CanonicalOrder { get; } = new CanonicalComparer();

    // Lowercase kebab-case parts joined by dots, at least two parts. For example "approval.not-role-holder".
    public static bool IsWellFormedCode(string? code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return false;
        }

        string[] parts = code!.Split('.');
        if (parts.Length < 2)
        {
            return false;
        }

        foreach (string part in parts)
        {
            if (!WireFormat.IsKebabCase(part))
            {
                return false;
            }
        }

        return true;
    }

    public bool Equals(Reason? other)
    {
        return other != null
            && string.Equals(Code, other.Code, StringComparison.Ordinal)
            && Severity == other.Severity
            && string.Equals(Message, other.Message, StringComparison.Ordinal)
            && string.Equals(Entity, other.Entity, StringComparison.Ordinal)
            && string.Equals(Ref, other.Ref, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as Reason);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = StringComparer.Ordinal.GetHashCode(Code);
            hash = (hash * 31) + (int)Severity;
            hash = (hash * 31) + (Entity == null ? 0 : StringComparer.Ordinal.GetHashCode(Entity));
            hash = (hash * 31) + (Ref == null ? 0 : StringComparer.Ordinal.GetHashCode(Ref));
            return hash;
        }
    }

    public override string ToString()
    {
        // The same shape the Dataverse adapter puts in an error message (document 25, section 7).
        return "[" + Code + "] " + Message;
    }

    private sealed class CanonicalComparer : IComparer<Reason>
    {
        public int Compare(Reason? x, Reason? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x == null)
            {
                return -1;
            }

            if (y == null)
            {
                return 1;
            }

            // Error is the highest severity and comes first.
            int bySeverity = ((int)y.Severity).CompareTo((int)x.Severity);
            if (bySeverity != 0)
            {
                return bySeverity;
            }

            int result = string.CompareOrdinal(x.Code, y.Code);
            if (result != 0)
            {
                return result;
            }

            result = string.CompareOrdinal(x.Entity ?? string.Empty, y.Entity ?? string.Empty);
            if (result != 0)
            {
                return result;
            }

            result = string.CompareOrdinal(x.Ref ?? string.Empty, y.Ref ?? string.Empty);
            if (result != 0)
            {
                return result;
            }

            return string.CompareOrdinal(x.Message, y.Message);
        }
    }
}
