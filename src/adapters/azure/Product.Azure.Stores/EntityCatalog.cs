using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Model;

namespace Product.Azure.Stores;

// How the Azure SQL store keeps an entity's history (document 22, sections 2.3 and 11.2). The SQLite store keeps no
// history but refuses the same changes to append-only rows, so a local run fails where Azure SQL would.
public enum EntityStorage
{
    // System-versioned temporal table with a history table: classes E, M and V, and O13 and O22 of class F.
    Temporal,

    // Updatable ledger table with a history table: C46, O08, O09 and O10 of class F.
    UpdatableLedger,

    // Append-only ledger table: class A (C02, O20, O24) and O14 of class F. Rows are inserted once and never changed.
    AppendOnlyLedger,
}

// What kind of value a promoted column holds.
public enum PromotedKind
{
    // A non-empty JSON string, else null (StoredRow.Text).
    Text,

    // True only for JSON true (StoredRow.Flag).
    Flag,

    // A JSON integer that fits in 32 bits, else null (StoredRow.Number).
    Number,
}

// A value of the row's JSON copied into a real column when the row is written, so a unique index, the version probe or
// a row-level security predicate can read it. Never a computed column: the store writes it with the JSON in one
// statement.
public sealed class PromotedColumn
{
    internal PromotedColumn(string jsonName, string sqlName, PromotedKind kind, int maxLength = 0, string? uniqueGroup = null, bool indexed = false)
    {
        JsonName = jsonName;
        SqlName = sqlName;
        Kind = kind;
        MaxLength = maxLength;
        UniqueGroup = uniqueGroup;
        Indexed = indexed;
    }

    // The column's contract name in the row JSON, such as "requestKey".
    public string JsonName { get; }

    // The column's name in the Azure SQL table, such as "RequestKey".
    public string SqlName { get; }

    public PromotedKind Kind { get; }

    // For text: the longest value the column holds. A longer value is refused for a unique column and stored as null
    // otherwise (no promoted text column is longer than a contract value it copies).
    public int MaxLength { get; }

    // The unique key this column belongs to, or null. A unique key with several columns lists each with the same group.
    public string? UniqueGroup { get; }

    // True when the column has its own non-unique index.
    public bool Indexed { get; }

    public bool IsUnique => UniqueGroup != null;

    // The Azure SQL type of the column. Text compares ordinally (binary collation), like the engine.
    public string SqlType
    {
        get
        {
            switch (Kind)
            {
                case PromotedKind.Flag:
                    return "bit";
                case PromotedKind.Number:
                    return "int";
                default:
                    return "nvarchar(" + MaxLength.ToString(CultureInfo.InvariantCulture) + ") COLLATE Latin1_General_BIN2";
            }
        }
    }

    // The value to store: a string, a bool or an int, or null.
    public object? ValueOf(JsonTreeNode columns)
    {
        JsonTreeNode? value = columns.TryGetMember(JsonName, out JsonTreeNode? found) ? found : null;
        switch (Kind)
        {
            case PromotedKind.Flag:
                return value != null && value.Kind == JsonTreeKind.True;
            case PromotedKind.Number:
                return value != null && value.Kind == JsonTreeKind.Number && value.TryGetInt64(out long number) && number >= int.MinValue && number <= int.MaxValue ? (int)number : null;
            default:
                if (value == null || value.Kind != JsonTreeKind.String || value.GetString().Length == 0)
                {
                    return null;
                }

                string text = value.GetString();
                return text.Length <= MaxLength ? text : null;
        }
    }

    // True when a text value is too long for this column. Only a unique column refuses it (RowStore checks before a
    // write reaches the database).
    public bool IsTooLong(JsonTreeNode columns)
    {
        return Kind == PromotedKind.Text && columns.TryGetMember(JsonName, out JsonTreeNode? value) && value.Kind == JsonTreeKind.String && value.GetString().Length > MaxLength;
    }
}

// A check on a row's subject columns (document 22, section 2.6): exactly one subject column holds a value, and the
// row's Subject Kind names it. An empty Subject Kind means the first subject, the one every row had before the others
// existed (O06 Owner Assignment: business-application, the only subject before the data taxonomy, D-37). The store makes
// the check before a write reaches the database, on SQLite and Azure SQL alike, and Azure SQL also holds it as a CHECK
// constraint on the row's JSON (sql/0008-data-taxonomy.sql), so the two agree: a column holds a value when it is a JSON
// scalar other than null and the empty string, which is what JSON_VALUE and NULLIF give in the constraint.
public sealed class SubjectCheck
{
    internal SubjectCheck(string name, string kindColumn, IEnumerable<KeyValuePair<string, string>> subjects)
    {
        Name = name;
        KindColumn = kindColumn;
        Subjects = new ReadOnlyCollection<KeyValuePair<string, string>>(subjects.ToList());
        if (Subjects.Count < 2)
        {
            throw new InvalidOperationException("A subject check needs at least two subjects.");
        }
    }

    // The constraint's name after the table, such as "OneSubject" in CK_OwnerAssignment_OneSubject.
    public string Name { get; }

    // The row JSON member that names the kind, such as "subjectKind".
    public string KindColumn { get; }

    // Each subject kind's wire value and the row JSON member that holds it. The first is the kind an empty Subject Kind
    // means.
    public IReadOnlyList<KeyValuePair<string, string>> Subjects { get; }

    // Null when the row passes, else what is wrong, in one sentence.
    public string? Problem(JsonTreeNode columns)
    {
        if (columns == null)
        {
            throw new ArgumentNullException(nameof(columns));
        }

        string[] set = Subjects.Where(s => HasValue(columns, s.Value)).Select(s => s.Value).ToArray();
        if (set.Length != 1)
        {
            return "sets " + set.Length.ToString(CultureInfo.InvariantCulture) + " of the subject columns " + string.Join(", ", Subjects.Select(s => s.Value))
                + ", and exactly one must hold a value.";
        }

        string expected = HasValue(columns, KindColumn) && columns.TryGetMember(KindColumn, out JsonTreeNode? kind) ? ScalarText(kind) : Subjects[0].Key;
        KeyValuePair<string, string> match = Subjects.FirstOrDefault(s => string.Equals(s.Key, expected, StringComparison.Ordinal));
        if (match.Value == null)
        {
            return "names the subject kind \"" + expected + "\", which is not one of " + string.Join(", ", Subjects.Select(s => s.Key)) + ".";
        }

        return string.Equals(match.Value, set[0], StringComparison.Ordinal)
            ? null
            : "has the subject kind \"" + expected + "\" but sets " + set[0] + " instead of " + match.Value + ".";
    }

    // True when the member is a JSON scalar other than null and the empty string.
    internal static bool HasValue(JsonTreeNode columns, string member)
    {
        if (!columns.TryGetMember(member, out JsonTreeNode? value))
        {
            return false;
        }

        switch (value.Kind)
        {
            case JsonTreeKind.String:
                return value.GetString().Length > 0;
            case JsonTreeKind.Number:
            case JsonTreeKind.True:
            case JsonTreeKind.False:
                return true;
            default:
                return false;
        }
    }

    private static string ScalarText(JsonTreeNode value)
    {
        return value.Kind == JsonTreeKind.String ? value.GetString() : CanonicalJson.Write(value, CanonicalProfile.Request);
    }
}

// One entity of the logical model as the stores keep it: its key, its table and its history class.
public sealed class EntityInfo
{
    internal EntityInfo(string key, string modelId, string logicalName, string schema, string table, char historyClass, bool isConfiguration, string deletePolicy, EntityStorage storage, IEnumerable<PromotedColumn> promoted, IEnumerable<StoreKey>? storeKeys = null, SubjectCheck? subjectCheck = null)
    {
        SubjectCheck = subjectCheck;
        Key = key;
        ModelId = modelId;
        LogicalName = logicalName;
        Schema = schema;
        Table = table;
        HistoryClass = historyClass;
        IsConfiguration = isConfiguration;
        DeletePolicy = deletePolicy;
        Storage = storage;
        Promoted = new ReadOnlyCollection<PromotedColumn>(promoted.ToList());
        StoreKeys = new ReadOnlyCollection<StoreKey>((storeKeys ?? Enumerable.Empty<StoreKey>()).ToList());
    }

    // The entity key, kebab-case of the logical name, such as "approval-record".
    public string Key { get; }

    // The document 20 ID, such as "O10".
    public string ModelId { get; }

    // The logical name, such as "Approval Record".
    public string LogicalName { get; }

    // The Azure SQL schema: config, ops or audit (document 22, section 11.1).
    public string Schema { get; }

    // The Azure SQL table name, the logical name in PascalCase as model/generated/column-map.json writes it.
    public string Table { get; }

    // The history class of document 22, section 2.3: A, F, E, M or V.
    public char HistoryClass { get; }

    public bool IsConfiguration { get; }

    // never, retire or draft-only (document 22, section 2.5).
    public string DeletePolicy { get; }

    public EntityStorage Storage { get; }

    // The promoted columns, in table order: the performer columns first, then the entity's own.
    public IReadOnlyList<PromotedColumn> Promoted { get; }

    // "[ops].[ApprovalRecord]".
    public string QualifiedTable => "[" + Schema + "].[" + Table + "]";

    // The history table of a temporal or updatable ledger table, or null for an append-only ledger table.
    public string? HistoryTable => Storage == EntityStorage.AppendOnlyLedger ? null : Table + "History";

    public string? QualifiedHistoryTable => HistoryTable == null ? null : "[" + Schema + "].[" + HistoryTable + "]";

    public IEnumerable<PromotedColumn> UniqueColumns => Promoted.Where(p => p.IsUnique);

    // The other keys of the model the store enforces for this entity, held in the store key table (StoreKeys.cs). The
    // keys the promoted unique columns hold are not repeated here.
    public IReadOnlyList<StoreKey> StoreKeys { get; }

    // The check on the row's subject columns, or null when the store makes none (document 22, section 2.6).
    public SubjectCheck? SubjectCheck { get; }

    public override string ToString()
    {
        return ModelId + " " + Key;
    }
}

// The entities of the first release, read from the logical model compiled into the engine core through the engine's
// public read-only view (Product.Engine.Core.Model.LogicalModel.Compiled), so the store and the write guard read the same
// model. Every entity whose status is not "later" and that is not stored inside another entity (C03 Preset lives in
// C01) gets a table.
public sealed class EntityCatalog
{
    // Class F differs per entity (document 22, section 11.2). A class F entity of the first release that is missing here
    // stops the catalog: refuse more, never less.
    private static readonly Dictionary<string, EntityStorage> ClassFStorage = new(StringComparer.Ordinal)
    {
        { "C46", EntityStorage.UpdatableLedger },
        { "O08", EntityStorage.UpdatableLedger },
        { "O09", EntityStorage.UpdatableLedger },
        { "O10", EntityStorage.UpdatableLedger },
        { "O13", EntityStorage.Temporal },
        { "O14", EntityStorage.AppendOnlyLedger },
        { "O22", EntityStorage.Temporal },
    };

    private static readonly Lazy<EntityCatalog> LazyEmbedded = new(() => From(LogicalModel.Compiled));

    private readonly Dictionary<string, EntityInfo> _byKey;

    private EntityCatalog(string modelVersion, IEnumerable<EntityInfo> entities)
    {
        ModelVersion = modelVersion;
        Entities = new ReadOnlyCollection<EntityInfo>(entities.OrderBy(e => e.ModelId, StringComparer.Ordinal).ToList());
        _byKey = Entities.ToDictionary(e => e.Key, StringComparer.Ordinal);
    }

    // The catalog of the logical model compiled into the engine core this store runs with.
    public static EntityCatalog Embedded => LazyEmbedded.Value;

    public string ModelVersion { get; }

    // By document 20 ID: C01, C02, C04 to C46, then O01 to O24.
    public IReadOnlyList<EntityInfo> Entities { get; }

    public bool TryGet(string entityKey, [NotNullWhen(true)] out EntityInfo? info)
    {
        if (entityKey == null)
        {
            info = null;
            return false;
        }

        return _byKey.TryGetValue(entityKey, out info);
    }

    public EntityInfo Get(string entityKey)
    {
        return TryGet(entityKey, out EntityInfo? info)
            ? info
            : throw new InvalidOperationException("The logical model has no first-release entity \"" + entityKey + "\", so the store has no table for it.");
    }

    // The entity key of a logical name: "Approval Record" gives "approval-record".
    public static string EntityKey(string logicalName)
    {
        return string.Join("-", Words(logicalName).Select(w => w.ToLowerInvariant()));
    }

    // The catalog of a logical model: every first-release entity that has its own table.
    public static EntityCatalog From(LogicalModel model)
    {
        if (model == null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        var entities = new List<EntityInfo>();
        foreach (LogicalEntity entity in model.Entities)
        {
            if (string.Equals(entity.Status, "later", StringComparison.Ordinal) || entity.StoredIn != null)
            {
                continue;
            }

            string id = entity.Id;
            string historyClass = entity.HistoryClass;
            if (historyClass.Length != 1)
            {
                throw new InvalidOperationException("The logical model gives " + id + " the history class \"" + historyClass + "\".");
            }

            bool isConfiguration = string.Equals(entity.Kind, "configuration", StringComparison.Ordinal);
            string schema = id == "O24" ? "audit" : isConfiguration ? "config" : "ops";
            string key = EntityKey(entity.Name);
            PromotedColumn[] promoted = PromotedFor(key).ToArray();
            entities.Add(new EntityInfo(
                key,
                id,
                entity.Name,
                schema,
                string.Concat(Words(entity.Name)),
                historyClass[0],
                isConfiguration,
                entity.DeletePolicy,
                StorageOf(id, historyClass[0]),
                promoted,
                StoreKeyCatalog.For(entity, key, promoted),
                SubjectCheckFor(key)));
        }

        return new EntityCatalog(model.ModelVersion, entities);
    }

    // The subject checks the store makes (document 22, section 2.6). O06 Owner Assignment gained its data subjects with
    // the data taxonomy (D-37; document 22, B22-14). O09 Decision's subjects stay the engine's to check: its tools also
    // write Subject Application beside a subject of another kind, as the subject's application scope, and its rows sit
    // frozen in an updatable ledger table that a failed check could never correct (document 22, B22-14).
    private static SubjectCheck? SubjectCheckFor(string key)
    {
        switch (key)
        {
            case "owner-assignment":
                return new SubjectCheck(
                    "OneSubject",
                    "subjectKind",
                    new[]
                    {
                        new KeyValuePair<string, string>("business-application", "businessApplication"),
                        new KeyValuePair<string, string>("data-domain", "subjectDataDomain"),
                        new KeyValuePair<string, string>("data-location", "subjectDataLocation"),
                    });
            default:
                return null;
        }
    }

    private static EntityStorage StorageOf(string id, char historyClass)
    {
        switch (historyClass)
        {
            case 'A':
                return EntityStorage.AppendOnlyLedger;
            case 'E':
            case 'M':
            case 'V':
                return EntityStorage.Temporal;
            case 'F':
                return ClassFStorage.TryGetValue(id, out EntityStorage storage)
                    ? storage
                    : throw new InvalidOperationException(id + " is class F with no Azure SQL table type (document 22, section 11.2).");
            default:
                throw new InvalidOperationException("The logical model gives " + id + " the unknown history class \"" + historyClass + "\".");
        }
    }

    // The promoted columns of an entity. Every table carries the performer of the last write (document 22, section
    // 2.4); the rest are the two unique keys the in-memory store enforces, the version probe and version read, and the
    // columns the row-level security block predicates read.
    private static IEnumerable<PromotedColumn> PromotedFor(string key)
    {
        yield return new PromotedColumn("performedByPrincipal", "PerformedByPrincipalId", PromotedKind.Text, 64);
        yield return new PromotedColumn("performedByActorKind", "PerformedByActorKind", PromotedKind.Text, 32);
        switch (key)
        {
            case "audit-entry":
                yield return new PromotedColumn("requestKey", "RequestKey", PromotedKind.Text, 400, uniqueGroup: "RequestKey");
                break;
            case "deployment-record":
                yield return new PromotedColumn("idempotencyKey", "IdempotencyKey", PromotedKind.Text, 400, uniqueGroup: "IdempotencyKey");
                break;
            case "configuration-set":
                yield return new PromotedColumn("isInForce", "IsInForce", PromotedKind.Flag);
                yield return new PromotedColumn("currentVersionNumber", "CurrentVersionNumber", PromotedKind.Number);
                break;
            case "configuration-set-version":
                yield return new PromotedColumn("configurationSet", "ConfigurationSetKey", PromotedKind.Text, 400, uniqueGroup: "Version");
                yield return new PromotedColumn("versionNumber", "VersionNumber", PromotedKind.Number, uniqueGroup: "Version");
                break;
            case "principal-reference":
                yield return new PromotedColumn("entraObjectId", "EntraObjectId", PromotedKind.Text, 64, indexed: true);
                break;
            case "decision":
                yield return new PromotedColumn("statusMeaning", "StatusMeaning", PromotedKind.Text, 32);
                yield return new PromotedColumn("supersededBy", "SupersededById", PromotedKind.Text, 64);
                break;
            case "approval-record":
                yield return new PromotedColumn("outcomeMeaning", "OutcomeMeaning", PromotedKind.Text, 32);
                break;
            case "tier-assessment":
                yield return new PromotedColumn("tierDecision", "TierDecisionId", PromotedKind.Text, 64);
                break;
            case "configuration-draft":
                yield return new PromotedColumn("draftStatus", "DraftStatus", PromotedKind.Text, 32);
                break;
            case "gap-check-run":
                yield return new PromotedColumn("finishedOn", "FinishedOn", PromotedKind.Text, 40);
                break;
        }
    }

    private static string[] Words(string logicalName)
    {
        return logicalName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
    }
}
