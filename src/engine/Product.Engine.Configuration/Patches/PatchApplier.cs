using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Json;
using Product.Engine.Configuration.Model;
using Product.Engine.Configuration.Validation;
using Product.Engine.Configuration.Validation.Rules;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;

namespace Product.Engine.Configuration.Patches;

// Applies a patch to a content, operation by operation, in order (document 21, section 8.2).
//
// - upsert creates the row, or merges values into the row with that key. Fields not named keep their value.
//   A child array in the values (values, answerOptions, outcomes) merges by child key in the same way,
//   so an upsert never drops a child row. A new row must carry every required field (V01 checks the result).
// - retire sets isActive to false. There is no delete operation (document 21, rule 7).
// - A child entity takes the key path "parent/child".
// Problems are reported, and the operation is skipped when it cannot apply. Nothing throws for bad input.
internal static class PatchApplier
{
    public static void Apply(ContentBuilder content, ConfigurationPatch patch, bool markTouched, ValidationIssues issues)
    {
        foreach (ConfigurationOperation op in patch.Operations)
        {
            string location = patch.Where + " " + op.Pointer;
            if (!ConfigurationEntities.TryGet(op.Entity, out ConfigurationEntity? entity))
            {
                // The schema check (V01) names the unknown entity.
                continue;
            }

            if (!V02KeyFormat.Instance.CheckKeyPath(op.Key, entity, location, issues))
            {
                continue;
            }

            if (entity.IsChild)
            {
                ApplyChild(content, entity, op, location, markTouched, issues);
            }
            else
            {
                ApplyTop(content, entity, op, location, markTouched, issues);
            }
        }
    }

    private static void ApplyTop(ContentBuilder content, ConfigurationEntity entity, ConfigurationOperation op, string location, bool markTouched, ValidationIssues issues)
    {
        List<JsonTreeNode> rows = content.Rows(entity.Key);
        int index = content.IndexOf(entity.Key, op.Key);

        if (op.Op == PatchOperation.Retire)
        {
            if (index < 0)
            {
                V06ReferenceResolves.Instance.Missing(
                    "the patch retires " + entity.LogicalName + " `" + op.Key + "`, which is not in the set. Fix the key, or drop the operation.",
                    entity.Key,
                    op.Key,
                    location,
                    issues);
                return;
            }

            rows[index] = JsonNodes.With(rows[index], "isActive", JsonTreeNode.False);
            Touch(content, entity.Key, op.Key, markTouched);
            return;
        }

        JsonTreeNode values = op.Values ?? JsonNodes.EmptyObject;
        V05KeyImmutable.Instance.CheckUpsert(entity.Key, op.Key, op.Key, values, location, issues);
        values = JsonNodes.Without(values, "key");

        if (index >= 0)
        {
            V04KeyReused.Instance.CheckUpsert(entity.Key, op.Key, rows[index], values, location, issues);
            rows[index] = Merge(rows[index], values, entity);
        }
        else
        {
            rows.Add(Merge(JsonTreeNode.CreateObject(JsonNodes.Member("key", op.Key)), values, entity));
        }

        Touch(content, entity.Key, op.Key, markTouched);
        if (markTouched && entity.ChildField != null && values.TryGetMember(entity.ChildField, out JsonTreeNode? children) && children.Kind == JsonTreeKind.Array)
        {
            foreach (JsonTreeNode child in children.Items)
            {
                string? childKey = JsonNodes.GetString(child, "key");
                if (childKey != null)
                {
                    content.MarkTouched(entity.ChildKey!, op.Key + "/" + childKey);
                }
            }
        }
    }

    private static void ApplyChild(ContentBuilder content, ConfigurationEntity entity, ConfigurationOperation op, string location, bool markTouched, ValidationIssues issues)
    {
        string[] parts = op.Key.Split('/');
        string parentKey = parts[0];
        string childKey = parts[1];
        ConfigurationEntity parent = ConfigurationEntities.Get(entity.ParentKey!);
        List<JsonTreeNode> parents = content.Rows(parent.Key);
        int parentIndex = content.IndexOf(parent.Key, parentKey);
        if (parentIndex < 0)
        {
            V06ReferenceResolves.Instance.Missing(
                "the patch changes " + entity.LogicalName + " `" + op.Key + "`, but " + parent.LogicalName + " `" + parentKey + "` is not in the set. Add the " + parent.LogicalName + " first, or fix the key.",
                entity.Key,
                op.Key,
                location,
                issues);
            return;
        }

        JsonTreeNode parentRow = parents[parentIndex];
        JsonTreeNode? existingChildren = JsonNodes.Get(parentRow, parent.ChildField!);
        List<JsonTreeNode> children = existingChildren != null && existingChildren.Kind == JsonTreeKind.Array
            ? existingChildren.Items.ToList()
            : new List<JsonTreeNode>();
        int childIndex = children.FindIndex(c => string.Equals(JsonNodes.GetString(c, "key"), childKey, StringComparison.Ordinal));

        if (op.Op == PatchOperation.Retire)
        {
            if (childIndex < 0)
            {
                V06ReferenceResolves.Instance.Missing(
                    "the patch retires " + entity.LogicalName + " `" + op.Key + "`, which is not in the set. Fix the key, or drop the operation.",
                    entity.Key,
                    op.Key,
                    location,
                    issues);
                return;
            }

            children[childIndex] = JsonNodes.With(children[childIndex], "isActive", JsonTreeNode.False);
        }
        else
        {
            JsonTreeNode values = op.Values ?? JsonNodes.EmptyObject;
            V05KeyImmutable.Instance.CheckUpsert(entity.Key, op.Key, childKey, values, location, issues);
            values = JsonNodes.Without(values, "key");
            if (childIndex >= 0)
            {
                V04KeyReused.Instance.CheckUpsert(entity.Key, op.Key, children[childIndex], values, location, issues);
                children[childIndex] = MergeMembers(children[childIndex], values);
            }
            else
            {
                children.Add(MergeMembers(JsonTreeNode.CreateObject(JsonNodes.Member("key", childKey)), values));
            }
        }

        parents[parentIndex] = JsonNodes.With(parentRow, parent.ChildField!, JsonTreeNode.CreateArray(children));
        Touch(content, entity.Key, op.Key, markTouched);
    }

    // Merges upsert values into a row. A child array merges by child key.
    private static JsonTreeNode Merge(JsonTreeNode row, JsonTreeNode values, ConfigurationEntity entity)
    {
        JsonTreeNode result = row;
        foreach (KeyValuePair<string, JsonTreeNode> member in values.Members)
        {
            if (entity.ChildField != null
                && string.Equals(member.Key, entity.ChildField, StringComparison.Ordinal)
                && member.Value.Kind == JsonTreeKind.Array)
            {
                result = JsonNodes.With(result, member.Key, MergeChildren(JsonNodes.Get(result, member.Key), member.Value));
                continue;
            }

            result = JsonNodes.With(result, member.Key, member.Value);
        }

        return result;
    }

    private static JsonTreeNode MergeChildren(JsonTreeNode? existing, JsonTreeNode incoming)
    {
        List<JsonTreeNode> children = existing != null && existing.Kind == JsonTreeKind.Array ? existing.Items.ToList() : new List<JsonTreeNode>();
        foreach (JsonTreeNode child in incoming.Items)
        {
            string? key = JsonNodes.GetString(child, "key");
            int index = key == null ? -1 : children.FindIndex(c => string.Equals(JsonNodes.GetString(c, "key"), key, StringComparison.Ordinal));
            if (index >= 0 && child.Kind == JsonTreeKind.Object)
            {
                children[index] = MergeMembers(children[index], child);
            }
            else
            {
                children.Add(child);
            }
        }

        return JsonTreeNode.CreateArray(children);
    }

    private static JsonTreeNode MergeMembers(JsonTreeNode row, JsonTreeNode values)
    {
        if (row.Kind != JsonTreeKind.Object)
        {
            return row;
        }

        JsonTreeNode result = row;
        foreach (KeyValuePair<string, JsonTreeNode> member in values.Members)
        {
            result = JsonNodes.With(result, member.Key, member.Value);
        }

        return result;
    }

    private static void Touch(ContentBuilder content, string entity, string keyPath, bool markTouched)
    {
        if (markTouched)
        {
            content.MarkTouched(entity, keyPath);
        }
    }
}
