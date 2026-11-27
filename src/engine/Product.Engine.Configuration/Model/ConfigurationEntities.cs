using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Product.Engine.Configuration.Model;

// A scope field that accepts the wildcard "*" in a seed (document 21, section 8.4), and the entity it points at.
public sealed class WildcardField
{
    public WildcardField(string field, string targetEntity)
    {
        Field = field;
        TargetEntity = targetEntity;
    }

    public string Field { get; }

    public string TargetEntity { get; }
}

// One configuration entity as the seed format knows it (document 21, section 3; document 20 IDs and names).
public sealed class ConfigurationEntity
{
    internal ConfigurationEntity(
        string key,
        string id,
        string? parentKey = null,
        string? childField = null,
        string? childKey = null,
        WildcardField[]? wildcards = null,
        string[]? groupFields = null,
        bool isProposal = false)
    {
        Key = key;
        Id = id;
        LogicalName = ToLogicalName(key);
        ParentKey = parentKey;
        ChildField = childField;
        ChildKey = childKey;
        WildcardFields = new ReadOnlyCollection<WildcardField>(wildcards ?? Array.Empty<WildcardField>());
        GroupFields = new ReadOnlyCollection<string>(groupFields ?? Array.Empty<string>());
        IsProposal = isProposal;
    }

    // The entity key, kebab-case of the logical name, such as "gate-policy".
    public string Key { get; }

    // The document 20 ID, such as "C33".
    public string Id { get; }

    // The document 20 logical name, such as "Gate Policy".
    public string LogicalName { get; }

    // For a child entity (C07, C10, C19): the parent entity key. Null otherwise.
    public string? ParentKey { get; }

    // For a parent entity: the field that holds its child rows, such as "values". Null otherwise.
    public string? ChildField { get; }

    // For a parent entity: the child entity key, such as "list-value". Null otherwise.
    public string? ChildKey { get; }

    public bool IsChild => ParentKey != null;

    // Scope fields that accept "*" in a seed, in field order (C25 and C33 only).
    public IReadOnlyList<WildcardField> WildcardFields { get; }

    // Other fields that are part of a row's scope when wildcards expand, such as the review type of a cadence.
    public IReadOnlyList<string> GroupFields { get; }

    // C41 and C43 to C45 are proposals (documents 24 and 26), not adopted yet.
    public bool IsProposal { get; }

    private static string ToLogicalName(string key)
    {
        return string.Join(" ", key.Split('-').Select(part => char.ToUpperInvariant(part[0]) + part.Substring(1)));
    }
}

// The configuration entities a seed, a snapshot or a patch can hold.
// C01 to C03 and C46 are the set, version, preset and draft records themselves, so they are not listed. C47 to C50 are
// the data taxonomy's configuration entities.
public static class ConfigurationEntities
{
    private static readonly ConfigurationEntity[] Entries =
    {
        new("setting", "C04"),
        new("status", "C05"),
        new("value-list", "C06", childField: "values", childKey: "list-value"),
        new("list-value", "C07", parentKey: "value-list"),
        new("tier", "C08"),
        new("tier-question", "C09", childField: "answerOptions", childKey: "tier-answer-option"),
        new("tier-answer-option", "C10", parentKey: "tier-question"),
        new("vocabulary-mapping", "C11"),
        new("lifecycle-stage", "C12"),
        new("gate", "C13"),
        new("gate-requirement", "C14"),
        new("application-kind", "C15"),
        new("component-kind", "C16"),
        new("dependency-type", "C17"),
        new("decision-type", "C18", childField: "outcomes", childKey: "decision-outcome"),
        new("decision-outcome", "C19", parentKey: "decision-type"),
        new("change-type", "C20"),
        new("standard-change-model", "C21"),
        new("document-type", "C22"),
        new("documentation-requirement", "C23"),
        new("review-type", "C24"),
        new("review-cadence", "C25", wildcards: new[] { new WildcardField("tierKey", "tier") }, groupFields: new[] { "reviewTypeKey" }),
        new("reminder-rule", "C26"),
        new("party", "C27"),
        new("owner-role", "C28"),
        new("role", "C29"),
        new("role-group-binding", "C30"),
        new("decision-right", "C31"),
        new("approval-rule", "C32"),
        new(
            "gate-policy",
            "C33",
            wildcards: new[]
            {
                new WildcardField("tierKey", "tier"),
                new WildcardField("changeTypeKey", "change-type"),
                new WildcardField("targetStageKey", "target-stage"),
            },
            groupFields: new[] { "portfolioId" }),
        new("target-stage", "C34"),
        new("change-path", "C35"),
        new("deployment-route", "C36"),
        new("link-kind", "C37"),
        new("actor-kind", "C38"),
        new("guideline", "C39"),
        new("separation-rule", "C40"),
        new("tool-policy", "C41", isProposal: true),
        new("workload-grant", "C42"),
        new("column-protection-rule", "C43", isProposal: true),
        new("portfolio-access", "C44", isProposal: true),
        new("known-client", "C45", isProposal: true),

        // The data taxonomy (D-37, settled by D-41; docs/architecture/data-taxonomy.md). Data domains are flat rows
        // with a parent key, so no child entity is needed.
        new("data-classification", "C47"),
        new("data-domain-level", "C48"),
        new("data-domain", "C49"),
        new("data-platform", "C50"),
    };

    private static readonly Dictionary<string, ConfigurationEntity> ByKey = Entries.ToDictionary(e => e.Key, StringComparer.Ordinal);

    // Every entity, in document 20 ID order.
    public static IReadOnlyList<ConfigurationEntity> All { get; } = new ReadOnlyCollection<ConfigurationEntity>(Entries);

    public static bool TryGet(string? key, [NotNullWhen(true)] out ConfigurationEntity? entity)
    {
        if (key != null && ByKey.TryGetValue(key, out ConfigurationEntity? found))
        {
            entity = found;
            return true;
        }

        entity = null;
        return false;
    }

    public static ConfigurationEntity Get(string key)
    {
        if (TryGet(key, out ConfigurationEntity? entity))
        {
            return entity;
        }

        throw new ArgumentException("Unknown configuration entity: " + key, nameof(key));
    }

    // The logical name for messages, or the key itself when the entity is unknown.
    public static string LogicalNameOf(string key)
    {
        return TryGet(key, out ConfigurationEntity? entity) ? entity.LogicalName : key;
    }
}
