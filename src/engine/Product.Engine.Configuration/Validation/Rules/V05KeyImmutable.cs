using System;
using Product.Engine.Configuration.Json;
using Product.Engine.Configuration.Model;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Configuration.Validation.Rules;

// V05: a patch never changes a row's key; renaming means changing the label.
public sealed class V05KeyImmutable : ValidationRule
{
    public static readonly V05KeyImmutable Instance = new();

    private V05KeyImmutable()
        : base("V05", ReasonCodes.KeyImmutable, "Keys never change")
    {
    }

    // An upsert's values name a key that is not the operation's key. Returns false when it reports an issue.
    public bool CheckUpsert(string entityKey, string keyPath, string rowKey, JsonTreeNode values, string? location, ValidationIssues issues)
    {
        JsonTreeNode? key = JsonNodes.Get(values, "key");
        if (key == null || (key.Kind == JsonTreeKind.String && string.Equals(key.GetString(), rowKey, StringComparison.Ordinal)))
        {
            return true;
        }

        string shown = key.Kind == JsonTreeKind.String ? key.GetString() : key.ToString();
        issues.Add(Issue(
            "the patch tries to change the key of " + ConfigurationEntities.LogicalNameOf(entityKey) + " " + Q(keyPath) + " to " + Q(shown) + ". Keys never change: change the label instead, or add a new row and retire this one.",
            entityKey,
            keyPath,
            location));
        return false;
    }
}
