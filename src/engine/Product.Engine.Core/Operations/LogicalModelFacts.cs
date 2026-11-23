using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Product.Engine.Core.Canonical;

namespace Product.Engine.Core.Operations;

// The history classes of document 22, section 2.3, as the logical model names them (A, F, E, M, V).
internal enum HistoryClass
{
    // A. Append-only: inserted once, never updated, never deleted (C02, O20, O24).
    AppendOnly,

    // F. Freeze on final: editable only while in its draft state, frozen once final (C46, O08, O09, O10, O13, O14, O22).
    FreezeOnFinal,

    // E. Effective-dated: updated in place, a fact is ended with a date (O03, O04, O06).
    EffectiveDated,

    // M. Mutable with history: updated in place (O01, O02, O05, O07, O11, O12, O15 to O19, O21, O23).
    MutableWithHistory,

    // V. Configuration versioned: changed only by the activation operation (C01, C03 to C45).
    ConfigurationVersioned,
}

// One entity of the logical model, as OP-08 needs it: its history class and its column list in the contract layer's
// lowerCamelCase form (document 20, section 3.3: "Field names are lowerCamelCase forms of the logical names").
internal sealed class ModelEntity
{
    private readonly HashSet<string> _columns;
    private readonly HashSet<string> _fillOnce;

    public ModelEntity(string id, string name, HistoryClass historyClass, string deletePolicy, IEnumerable<string> columns, IEnumerable<string> fillOnce)
    {
        Id = id;
        Name = name;
        Key = LogicalModelFacts.EntityKey(name);
        HistoryClass = historyClass;
        DeletePolicy = deletePolicy;
        _columns = new HashSet<string>(columns, StringComparer.Ordinal);
        _fillOnce = new HashSet<string>(fillOnce, StringComparer.Ordinal);
        Columns = new ReadOnlyCollection<string>(_columns.OrderBy(c => c, StringComparer.Ordinal).ToList());
        FillOnceColumns = new ReadOnlyCollection<string>(_fillOnce.OrderBy(c => c, StringComparer.Ordinal).ToList());
    }

    // The document 20 ID, such as "O10".
    public string Id { get; }

    // The logical name, such as "Approval Record".
    public string Name { get; }

    // The entity key, kebab-case of the logical name, such as "approval-record".
    public string Key { get; }

    public HistoryClass HistoryClass { get; }

    // never, retire or draft-only (document 22, section 2.5).
    public string DeletePolicy { get; }

    // Every column of the entity: its column sets (common, operational, taxonomy) and its own attributes, sorted.
    public IReadOnlyList<string> Columns { get; }

    // Columns that may be filled once while empty and never changed (the model's fillOnce flag; O13 in document 22).
    public IReadOnlyList<string> FillOnceColumns { get; }

    public bool HasColumn(string column)
    {
        return _columns.Contains(column);
    }

    public bool IsFillOnce(string column)
    {
        return _fillOnce.Contains(column);
    }
}

// The logical model (model/logical-model.json) compiled into this assembly as an embedded resource, so the guard's
// column lists and history classes are the model's own and never drift from it. Reading the resource touches no file,
// no network and no environment: it is part of the loaded assembly, like the code. Loaded once, on first use.
internal static class LogicalModelFacts
{
    // Found by the end of its name, so the product rename does not break it.
    private const string ResourceSuffix = ".logical-model.json";

    private static readonly Lazy<Loaded> Model = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    public static string ModelVersion => Model.Value.Version;

    public static IEnumerable<ModelEntity> All => Model.Value.Entities.Values.OrderBy(e => e.Id, StringComparer.Ordinal);

    public static bool TryGet(string key, [NotNullWhen(true)] out ModelEntity? entity)
    {
        return Model.Value.Entities.TryGetValue(key, out entity);
    }

    public static ModelEntity Get(string key)
    {
        return TryGet(key, out ModelEntity? entity)
            ? entity
            : throw new InvalidOperationException("The logical model compiled into the engine has no entity \"" + key + "\".");
    }

    // The entity key of a logical name: "Approval Record" gives "approval-record".
    public static string EntityKey(string logicalName)
    {
        return string.Join("-", Words(logicalName).Select(w => w.ToLowerInvariant()));
    }

    // The contract layer's field name of a logical name: the first word in lowercase, every later word with its first
    // letter in uppercase and the rest in lowercase. "Outcome Meaning" gives "outcomeMeaning", "Is AI Created" gives
    // "isAiCreated" and "Flow Run Url" gives "flowRunUrl".
    public static string ContractName(string logicalName)
    {
        var text = new StringBuilder();
        string[] words = Words(logicalName);
        for (int i = 0; i < words.Length; i++)
        {
            string word = words[i].ToLowerInvariant();
            text.Append(i == 0 ? word : char.ToUpperInvariant(word[0]) + word.Substring(1));
        }

        return text.ToString();
    }

    private static string[] Words(string logicalName)
    {
        return logicalName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static Loaded Load()
    {
        Assembly assembly = typeof(LogicalModelFacts).Assembly;
        string? name = assembly.GetManifestResourceNames().SingleOrDefault(n => n.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("The engine core is built without the logical model (" + ResourceSuffix + ").");
        string text;
        using (Stream stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException("The logical model resource cannot be read."))
        using (var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true))
        {
            text = reader.ReadToEnd();
        }

        JsonTreeNode root = CanonicalJson.Parse(text);
        var sets = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (JsonTreeNode set in Items(root, "columnSets"))
        {
            sets[Text(set, "name")] = Items(set, "attributes").Select(a => ContractName(Text(a, "name"))).ToList();
        }

        var entities = new Dictionary<string, ModelEntity>(StringComparer.Ordinal);
        foreach (JsonTreeNode entity in Items(root, "entities"))
        {
            var columns = new List<string>();
            foreach (JsonTreeNode set in Items(entity, "columnSets"))
            {
                columns.AddRange(sets.TryGetValue(set.GetString(), out IReadOnlyList<string>? names)
                    ? names
                    : throw new InvalidOperationException("The logical model names an unknown column set \"" + set.GetString() + "\"."));
            }

            var fillOnce = new List<string>();
            foreach (JsonTreeNode attribute in Items(entity, "attributes"))
            {
                string column = ContractName(Text(attribute, "name"));
                columns.Add(column);
                if (attribute.TryGetMember("fillOnce", out JsonTreeNode? flag) && flag.Kind == JsonTreeKind.True)
                {
                    fillOnce.Add(column);
                }
            }

            var model = new ModelEntity(Text(entity, "id"), Text(entity, "name"), Class(Text(entity, "historyClass")), Text(entity, "deletePolicy"), columns, fillOnce);
            entities.Add(model.Key, model);
        }

        return new Loaded(Text(root, "modelVersion"), entities);
    }

    private static HistoryClass Class(string id)
    {
        switch (id)
        {
            case "A":
                return HistoryClass.AppendOnly;
            case "F":
                return HistoryClass.FreezeOnFinal;
            case "E":
                return HistoryClass.EffectiveDated;
            case "M":
                return HistoryClass.MutableWithHistory;
            case "V":
                return HistoryClass.ConfigurationVersioned;
            default:
                throw new InvalidOperationException("The logical model names an unknown history class \"" + id + "\".");
        }
    }

    private static IReadOnlyList<JsonTreeNode> Items(JsonTreeNode node, string member)
    {
        return node.TryGetMember(member, out JsonTreeNode? value) && value.Kind == JsonTreeKind.Array ? value.Items : Array.Empty<JsonTreeNode>();
    }

    private static string Text(JsonTreeNode node, string member)
    {
        return node.TryGetMember(member, out JsonTreeNode? value) && value.Kind == JsonTreeKind.String
            ? value.GetString()
            : throw new InvalidOperationException("The logical model has an entry without \"" + member + "\".");
    }

    private sealed class Loaded
    {
        public Loaded(string version, Dictionary<string, ModelEntity> entities)
        {
            Version = version;
            Entities = entities;
        }

        public string Version { get; }

        public Dictionary<string, ModelEntity> Entities { get; }
    }
}
