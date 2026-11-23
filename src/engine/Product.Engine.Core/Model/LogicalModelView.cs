using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Operations;

namespace Product.Engine.Core.Model;

// A public, read-only view of the logical model compiled into the engine (model/logical-model.json, the same embedded
// resource and the same parse as the write guard's facts). Stores and hosts read entity keys, IDs, kinds, history
// classes, columns and unique keys from here instead of parsing the resource themselves. Every collection is read-only
// and every property get-only. Base class library only, so it runs on net48 and netstandard2.0 as well.
public sealed class LogicalModel
{
    private static readonly Lazy<LogicalModel> LazyCompiled = new(() => From(LogicalModelFacts.Root), LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly Dictionary<string, LogicalEntity> _byKey;

    private LogicalModel(string modelVersion, IList<LogicalEntity> entities)
    {
        ModelVersion = modelVersion;
        Entities = new ReadOnlyCollection<LogicalEntity>(entities);
        _byKey = entities.ToDictionary(e => e.Key, StringComparer.Ordinal);
    }

    // The model compiled into this engine build. Loaded once, on first use.
    public static LogicalModel Compiled => LazyCompiled.Value;

    // The model's modelVersion, such as "0.1.0".
    public string ModelVersion { get; }

    // Every entity, in the model's own order (C01 to C46, O01 to O24, then the L entities).
    public IReadOnlyList<LogicalEntity> Entities { get; }

    // By entity key, such as "approval-record".
    public bool TryGet(string key, [NotNullWhen(true)] out LogicalEntity? entity)
    {
        if (key == null)
        {
            entity = null;
            return false;
        }

        return _byKey.TryGetValue(key, out entity);
    }

    public LogicalEntity Get(string key)
    {
        return TryGet(key, out LogicalEntity? entity)
            ? entity
            : throw new KeyNotFoundException("The logical model compiled into the engine has no entity \"" + key + "\".");
    }

    private static LogicalModel From(JsonTreeNode root)
    {
        var sets = new Dictionary<string, IReadOnlyList<JsonTreeNode>>(StringComparer.Ordinal);
        foreach (JsonTreeNode set in LogicalModelFacts.Items(root, "columnSets"))
        {
            sets[LogicalModelFacts.Text(set, "name")] = LogicalModelFacts.Items(set, "attributes");
        }

        var entities = new List<LogicalEntity>();
        foreach (JsonTreeNode entity in LogicalModelFacts.Items(root, "entities"))
        {
            // Column set columns first, then the entity's own attributes. A name the entity repeats from a column set
            // (O24 restates On Behalf Of, Client App and Client App Verified) keeps its place and takes the entity's own
            // definition, so each contract name appears once.
            var columns = new List<LogicalColumn>();
            var positions = new Dictionary<string, int>(StringComparer.Ordinal);
            void Add(JsonTreeNode attribute, string? columnSet)
            {
                var column = new LogicalColumn(
                    LogicalModelFacts.Text(attribute, "name"),
                    LogicalModelFacts.Text(attribute, "type"),
                    LogicalModelFacts.Text(attribute, "required"),
                    columnSet);
                if (positions.TryGetValue(column.ContractName, out int at))
                {
                    columns[at] = column;
                }
                else
                {
                    positions[column.ContractName] = columns.Count;
                    columns.Add(column);
                }
            }

            foreach (JsonTreeNode set in LogicalModelFacts.Items(entity, "columnSets"))
            {
                string setName = set.GetString();
                if (!sets.TryGetValue(setName, out IReadOnlyList<JsonTreeNode>? attributes))
                {
                    throw new InvalidOperationException("The logical model names an unknown column set \"" + setName + "\".");
                }

                foreach (JsonTreeNode attribute in attributes)
                {
                    Add(attribute, setName);
                }
            }

            foreach (JsonTreeNode attribute in LogicalModelFacts.Items(entity, "attributes"))
            {
                Add(attribute, null);
            }

            var keys = LogicalModelFacts.Items(entity, "keys")
                .Select(k => new LogicalUniqueKey(
                    LogicalModelFacts.Text(k, "kind"),
                    LogicalModelFacts.Items(k, "columns").Select(c => c.GetString()),
                    LogicalModelFacts.Text(k, "enforcement"),
                    OptionalText(k, "condition")))
                .ToList();

            entities.Add(new LogicalEntity(
                LogicalModelFacts.Text(entity, "id"),
                LogicalModelFacts.Text(entity, "name"),
                LogicalModelFacts.Text(entity, "kind"),
                LogicalModelFacts.Text(entity, "status"),
                LogicalModelFacts.Text(entity, "release"),
                LogicalModelFacts.Text(entity, "historyClass"),
                LogicalModelFacts.Text(entity, "deletePolicy"),
                OptionalText(entity, "storedIn"),
                columns,
                keys));
        }

        return new LogicalModel(LogicalModelFacts.Text(root, "modelVersion"), entities);
    }

    private static string? OptionalText(JsonTreeNode node, string member)
    {
        return node.TryGetMember(member, out JsonTreeNode? value) && value.Kind == JsonTreeKind.String ? value.GetString() : null;
    }
}

// One entity of the logical model.
public sealed class LogicalEntity
{
    private readonly Dictionary<string, LogicalColumn> _columns;

    internal LogicalEntity(
        string id,
        string name,
        string kind,
        string status,
        string release,
        string historyClass,
        string deletePolicy,
        string? storedIn,
        IList<LogicalColumn> columns,
        IList<LogicalUniqueKey> uniqueKeys)
    {
        Id = id;
        Name = name;
        Key = LogicalModelFacts.EntityKey(name);
        Kind = kind;
        Status = status;
        Release = release;
        HistoryClass = historyClass;
        DeletePolicy = deletePolicy;
        StoredIn = storedIn;
        Columns = new ReadOnlyCollection<LogicalColumn>(columns);
        UniqueKeys = new ReadOnlyCollection<LogicalUniqueKey>(uniqueKeys);
        _columns = columns.ToDictionary(c => c.ContractName, StringComparer.Ordinal);
    }

    // The entity key, kebab-case of the logical name, such as "approval-record".
    public string Key { get; }

    // The document 20 ID, such as "O10".
    public string Id { get; }

    // The logical name, such as "Approval Record".
    public string Name { get; }

    // "configuration" or "operational".
    public string Kind { get; }

    // The model's status: "adopted", "proposal" or "later".
    public string Status { get; }

    // The release the entity belongs to, such as "R1".
    public string Release { get; }

    // The history class letter of document 22, section 2.3: A, F, E, M or V.
    public string HistoryClass { get; }

    // never, retire or draft-only (document 22, section 2.5).
    public string DeletePolicy { get; }

    // The ID of the entity whose rows hold this one (C03 Preset lives in C01), else null.
    public string? StoredIn { get; }

    // Every column: the column sets' columns first (common, operational, taxonomy, as the entity lists them), then the
    // entity's own attributes.
    public IReadOnlyList<LogicalColumn> Columns { get; }

    // The business and alternate keys of the model.
    public IReadOnlyList<LogicalUniqueKey> UniqueKeys { get; }

    // By contract name, such as "outcomeMeaning".
    public bool TryGetColumn(string contractName, [NotNullWhen(true)] out LogicalColumn? column)
    {
        if (contractName == null)
        {
            column = null;
            return false;
        }

        return _columns.TryGetValue(contractName, out column);
    }

    public override string ToString()
    {
        return Id + " " + Key;
    }
}

// One column of an entity.
public sealed class LogicalColumn
{
    internal LogicalColumn(string logicalName, string type, string required, string? columnSet)
    {
        LogicalName = logicalName;
        ContractName = LogicalModelFacts.ContractName(logicalName);
        Type = type;
        Required = required;
        ColumnSet = columnSet;
    }

    // The logical name, such as "Outcome Meaning".
    public string LogicalName { get; }

    // The contract layer's lowerCamelCase name, such as "outcomeMeaning" (document 20, section 3.3).
    public string ContractName { get; }

    // The model type, such as "text", "ref", "meaning" or "datetime".
    public string Type { get; }

    // The model's required flag: "Y" (required), "N" (optional) or "C" (conditional).
    public string Required { get; }

    public bool IsRequired => string.Equals(Required, "Y", StringComparison.Ordinal);

    // The column set the column comes from ("common", "operational" or "taxonomy"), or null for the entity's own.
    public string? ColumnSet { get; }

    public override string ToString()
    {
        return ContractName;
    }
}

// A business or alternate key of an entity.
public sealed class LogicalUniqueKey
{
    internal LogicalUniqueKey(string kind, IEnumerable<string> columns, string enforcement, string? condition)
    {
        Kind = kind;
        Columns = new ReadOnlyCollection<string>(columns.ToList());
        ContractColumns = new ReadOnlyCollection<string>(Columns.Select(LogicalModelFacts.ContractName).ToList());
        Enforcement = enforcement;
        Condition = condition;
    }

    // "business" or "alternate".
    public string Kind { get; }

    // The logical names of the key's columns, such as "Idempotency Key".
    public IReadOnlyList<string> Columns { get; }

    // The same columns as contract names, such as "idempotencyKey".
    public IReadOnlyList<string> ContractColumns { get; }

    // Who enforces it: "store" or "engine".
    public string Enforcement { get; }

    // When the key applies, such as "Once answered". Null when it always applies.
    public string? Condition { get; }
}
