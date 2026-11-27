using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Product.Azure.Stores.Tests;

// The checked-in Azure SQL scripts and the SQLite store against the logical model as it is on disk
// (src/model/logical-model.json), not the copy compiled into the engine core: every first-release entity has exactly one
// table in the schema of its kind, its history class gives the table type the model names for Azure SQL, and the stores
// hold every key the model asks the store to enforce, on both providers alike, but the few held back on purpose.
public sealed class LogicalModelConformanceTests
{
    private static readonly Lazy<JsonDocument> LazyModel = new(() => JsonDocument.Parse(File.ReadAllText(Path.Combine(Paths.Source, "model", "logical-model.json"))));

    // The store keys of the model the row stores do not hold, each because a documented engine behavior writes rows the
    // key would refuse (StoreKeyCatalog.HeldBack gives the reasons). A store key the model adds fails this test until the
    // stores hold it or it joins this list on purpose.
    private static readonly string[] StoreKeysHeldBack =
    {
        "C02 Content Hash",
        "O13 Artifact Name+Version",
        "O21 Business Application+Review Type+Due On",
    };

    private static readonly Lazy<JsonDocument> LazyBundleSchema = new(() => JsonDocument.Parse(File.ReadAllText(Path.Combine(Paths.Source, "config", "schema", "config-bundle.schema.json"))));

    private static JsonElement Model => LazyModel.Value.RootElement;

    // The tables come from 0001 (the first release) and 0008 (the data taxonomy, D-37), each table from one of them.
    private static string TableScripts()
    {
        return File.ReadAllText(Path.Combine(Paths.Sql, "0001-tables.sql")) + File.ReadAllText(Path.Combine(Paths.Sql, "0008-data-taxonomy.sql"));
    }

    [Fact]
    public void EveryFirstReleaseEntityHasOneTableOfItsHistoryClass()
    {
        string tables = TableScripts().Replace("\r\n", "\n");
        Dictionary<string, string> storageOfClassF = ClassFStorage();
        var expected = new List<string>();
        foreach (JsonElement entity in FirstReleaseEntities())
        {
            string id = entity.GetProperty("id").GetString()!;
            string name = entity.GetProperty("name").GetString()!;
            string historyClass = entity.GetProperty("historyClass").GetString()!;
            string kind = entity.GetProperty("kind").GetString()!;
            string schema = id == "O24" ? "audit" : kind == "configuration" ? "config" : "ops";
            string table = "[" + schema + "].[" + string.Concat(name.Split(' ')) + "]";
            expected.Add(table);

            string storage = historyClass switch
            {
                "A" => "append-only",
                "F" => storageOfClassF.TryGetValue(id, out string? f) ? f : throw new InvalidOperationException("The model names no Azure SQL table type for class F entity " + id + "."),
                _ => "temporal",
            };

            int start = tables.IndexOf("    CREATE TABLE " + table + "\n", StringComparison.Ordinal);
            Assert.True(start > 0, id + " " + name + " has no table " + table + " in 0001-tables.sql.");
            string create = tables.Substring(start, tables.IndexOf("END;", start, StringComparison.Ordinal) - start);
            switch (storage)
            {
                case "append-only":
                    Assert.True(create.Contains("WITH (LEDGER = ON (APPEND_ONLY = ON));", StringComparison.Ordinal), id + " is class " + historyClass + ": an append-only ledger table.");
                    break;
                case "updatable":
                    string history = table.Substring(0, table.Length - 1) + "History]";
                    Assert.True(create.Contains("WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = " + history + "), LEDGER = ON);", StringComparison.Ordinal), id + " is class F: an updatable ledger table.");
                    Assert.DoesNotContain("APPEND_ONLY", create, StringComparison.Ordinal);
                    break;
                default:
                    Assert.True(create.Contains("PERIOD FOR SYSTEM_TIME", StringComparison.Ordinal) && !create.Contains("LEDGER", StringComparison.Ordinal), id + " is class " + historyClass + ": a system-versioned temporal table.");
                    break;
            }

            Assert.Contains("[Id] varchar(36) COLLATE Latin1_General_BIN2 NOT NULL", create, StringComparison.Ordinal);
            Assert.Contains("CONSTRAINT [PK_" + string.Concat(name.Split(' ')) + "] PRIMARY KEY CLUSTERED ([Id])", create, StringComparison.Ordinal);
        }

        string[] created = Regex.Matches(tables, @"^    CREATE TABLE (\[[a-z]+\]\.\[[A-Za-z]+\])\n", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(expected.OrderBy(t => t, StringComparer.Ordinal), created.OrderBy(t => t, StringComparer.Ordinal));
        Assert.Equal(76, created.Length);
    }

    [Fact]
    public void TheStoresHoldEveryStoreKeyOfTheModelButTheOnesHeldBack()
    {
        string tables = TableScripts();
        var storeKeys = new List<(string Id, string[] Columns)>();
        foreach (JsonElement entity in FirstReleaseEntities())
        {
            foreach (JsonElement key in entity.GetProperty("keys").EnumerateArray().Where(k => k.GetProperty("enforcement").GetString() == "store"))
            {
                storeKeys.Add((entity.GetProperty("id").GetString()!, key.GetProperty("columns").EnumerateArray().Select(c => c.GetString()!).ToArray()));
            }
        }

        // Held by a unique index on the entity's own table: every such index covers a store key of the model (the same
        // columns, or fewer, which is stricter), on both providers alike.
        var indexed = new HashSet<string>(StringComparer.Ordinal);
        foreach (EntityInfo entity in EntityCatalog.Embedded.Entities.Where(e => e.UniqueColumns.Any()))
        {
            string[] columns = entity.UniqueColumns.Select(c => c.JsonName).ToArray();
            (string Id, string[] Columns) match = storeKeys.FirstOrDefault(k => k.Id == entity.ModelId && columns.All(c => k.Columns.Select(JsonName).Contains(c, StringComparer.Ordinal)));
            Assert.True(match.Id != null, entity.ModelId + " holds a unique key on " + string.Join(", ", columns) + " that the model does not give the store.");
            indexed.Add(Label(match));

            // Both providers from the same columns: the Azure SQL script holds this unique index, and the SQLite store's
            // one unique index takes the value RowChange.UniqueKey builds from the entity's unique columns.
            string index = "CREATE UNIQUE INDEX [UX_" + entity.Table + "_" + entity.UniqueColumns.First().UniqueGroup + "] ON " + entity.QualifiedTable
                + " (" + string.Join(", ", entity.UniqueColumns.Select(c => "[" + c.SqlName + "]")) + ")";
            Assert.Contains(index, tables, StringComparison.Ordinal);
        }

        Assert.Equal(new[] { "C02 Configuration Set+Version Number", "O14 Idempotency Key", "O24 Operation+Request Key" }, indexed.OrderBy(k => k, StringComparer.Ordinal));

        // Held in the store key table: each model key once, by the entity it belongs to.
        var tabled = new List<string>();
        foreach (EntityInfo entity in EntityCatalog.Embedded.Entities)
        {
            foreach (StoreKey key in entity.StoreKeys)
            {
                Assert.Equal(entity.Key, key.EntityKey);
                Assert.NotEmpty(key.ModelKeys);
                Assert.All(key.ModelKeys, label => Assert.StartsWith(entity.ModelId + " ", label, StringComparison.Ordinal));
                tabled.AddRange(key.ModelKeys);
            }
        }

        string[] all = storeKeys.Select(Label).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        Assert.Equal(tabled.Count, tabled.Distinct(StringComparer.Ordinal).Count());
        Assert.All(tabled, label => Assert.Contains(label, all));
        Assert.Empty(tabled.Intersect(indexed, StringComparer.Ordinal));
        Assert.Empty(tabled.Intersect(StoreKeysHeldBack, StringComparer.Ordinal));

        // Nothing is left over: every store key of the model is held, or held back on purpose with its reason.
        Assert.Equal(StoreKeysHeldBack.OrderBy(k => k, StringComparer.Ordinal), StoreKeyCatalog.HeldBack.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.All(StoreKeyCatalog.HeldBack.Values, reason => Assert.True(reason.Length > 40, reason));
        string[] left = all.Where(k => !indexed.Contains(k) && !tabled.Contains(k, StringComparer.Ordinal)).ToArray();
        Assert.Equal(StoreKeysHeldBack.OrderBy(k => k, StringComparer.Ordinal), left);
        Assert.Equal(83, all.Length);
        Assert.Equal(77, tabled.Count);
    }

    // The model keys the store holds on narrower terms than the model states them, each to match what the engine itself
    // writes and checks (sql/README.md, "Store keys"). Like a held-back key, a new narrowing fails this test until it is
    // listed here on purpose.
    // - The configuration alternate keys count active rows only: the engine's own checks (V12 tier ranks, V14 stage ranks)
    //   count active rows, so a retired row keeps its values and an active row may take them again.
    // - O03 Link Kind and External Id counts rows with no Retired On, as the engine's key of the entity does, so a resource
    //   registered again after its component retired is a new component.
    // - C07 Key within the set is held as Value List and Key (the key path holds the list's key): every shipped preset
    //   uses one value key in several lists (other, low, medium, high, power-platform), which Key alone would refuse.
    [Fact]
    public void TheKeysTheStoreHoldsOnNarrowerTermsThanTheModelAreListed()
    {
        string[] narrowed = EntityCatalog.Embedded.Entities
            .SelectMany(e => e.StoreKeys)
            .Where(k => k.Scope != StoreKeyScope.AllRows)
            .SelectMany(k => k.ModelKeys)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            new[]
            {
                "C08 Rank+Configuration Set",
                "C11 Source Entity+Source Key+Vocabulary+Configuration Set",
                "C12 Stage Rank+Configuration Set",
                "C25 Review Type+Tier+Configuration Set",
                "C30 Role+Entra Group+Binding Method+Configuration Set",
                "C33 Tier+Change Type+Target Stage+Portfolio+Configuration Set",
                "C39 Number+Version+Configuration Set",
                "C42 Principal+Workload Role+Configuration Set",
                "C47 Rank+Configuration Set",
                "C48 Rank+Configuration Set",
                "O03 Link Kind+External Id",
                "O25 Data Platform+External Id",
                "O26 Data Domain+Data Location+Data Origin",
            },
            narrowed);
        Assert.All(
            EntityCatalog.Embedded.Entities.SelectMany(e => e.StoreKeys).Where(k => k.Scope == StoreKeyScope.ActiveRows),
            k => Assert.True(k.ModelKeys.All(m => m.StartsWith("C", StringComparison.Ordinal)), k + " counts active rows, which only live configuration rows have."));

        StoreKey listValue = EntityCatalog.Embedded.Get("list-value").StoreKeys.Single();
        Assert.True(listValue.UsesKeyPath);
        Assert.Equal(new[] { "C07 Key+Configuration Set", "C07 Value List+Key+Configuration Set" }, listValue.ModelKeys);
    }

    // The parts of every store key name what a stored row holds: an operational row's columns are the model's contract
    // names, and a live configuration row's are the configuration bundle's field names (or its key path).
    [Fact]
    public void EveryStoreKeyPartIsAFieldOfTheRowsItKeys()
    {
        JsonElement defs = LazyBundleSchema.Value.RootElement.GetProperty("$defs");
        foreach (EntityInfo entity in EntityCatalog.Embedded.Entities)
        {
            JsonElement model = FirstReleaseEntities().Single(e => e.GetProperty("id").GetString() == entity.ModelId);
            bool liveConfiguration = entity.IsConfiguration && entity.Key != "configuration-set" && entity.Key != "configuration-set-version" && entity.Key != "configuration-draft";
            HashSet<string> modelColumns = ModelColumns(model);
            var bundleFields = new HashSet<string>(StringComparer.Ordinal);
            foreach (string group in new[] { "seedFields", "workingFields", "engineFields" })
            {
                if (defs.TryGetProperty(entity.Key + "." + group, out JsonElement fields) && fields.TryGetProperty("properties", out JsonElement properties))
                {
                    bundleFields.UnionWith(properties.EnumerateObject().Select(p => p.Name));
                }
            }

            foreach (StoreKey key in entity.StoreKeys)
            {
                foreach (string part in key.Parts)
                {
                    if (part == StoreKey.KeyPathPart)
                    {
                        Assert.True(liveConfiguration, key + ": only a live configuration row has a key path.");
                    }
                    else if (liveConfiguration)
                    {
                        Assert.True(bundleFields.Contains(part), key + ": " + part + " is not a field of " + entity.Key + " in the configuration bundle schema.");
                    }
                    else
                    {
                        Assert.True(modelColumns.Contains(part), key + ": " + part + " is not a column of " + entity.ModelId + " in the logical model.");
                    }
                }

                // The scope's own column is a field of the rows too.
                if (key.Scope == StoreKeyScope.ActiveRows)
                {
                    Assert.True(liveConfiguration, key + " counts active rows, which only live configuration rows have.");
                }
                else if (key.Scope == StoreKeyScope.CurrentRows)
                {
                    Assert.Contains("retiredOn", modelColumns);
                }
                else if (key.Scope == StoreKeyScope.OpenRows)
                {
                    Assert.Contains("endedOn", modelColumns);
                }
                else if (key.Scope == StoreKeyScope.InServiceRows)
                {
                    Assert.Contains("retiredOn", modelColumns);
                    Assert.Contains("statusMeaning", modelColumns);
                }
            }
        }
    }

    // The subject checks the store makes are the model's: for each, the model's exactly-one constraint names the same
    // columns, and its subjects block gives each column the kind the check names, in the model's order.
    [Fact]
    public void TheStoresSubjectChecksAreTheModelsExactlyOneSubjects()
    {
        foreach (EntityInfo entity in EntityCatalog.Embedded.Entities.Where(e => e.SubjectCheck != null))
        {
            JsonElement model = FirstReleaseEntities().Single(e => e.GetProperty("id").GetString() == entity.ModelId);
            SubjectCheck check = entity.SubjectCheck!;
            JsonElement constraint = model.GetProperty("constraints").EnumerateArray().Single(c => c.GetProperty("kind").GetString() == "exactly-one");
            Assert.Equal(constraint.GetProperty("columns").EnumerateArray().Select(c => ContractName(c.GetString()!)), check.Subjects.Select(s => s.Value));
            JsonElement subjects = model.GetProperty("subjects");
            Assert.Equal(ContractName(subjects.GetProperty("kindColumn").GetString()!), check.KindColumn);
            Assert.Equal(
                subjects.GetProperty("options").EnumerateArray().Select(o => (EntityCatalog.EntityKey(o.GetProperty("value").GetString()!), ContractName(o.GetProperty("column").GetString()!))),
                check.Subjects.Select(s => (s.Key, s.Value)));
        }

        Assert.Contains(EntityCatalog.Embedded.Entities, e => e.Key == "owner-assignment" && e.SubjectCheck != null);
    }

    // ---- Helpers ----

    // The engine's contract name: "Subject Data Domain" gives "subjectDataDomain", "Reaches AI" gives "reachesAi".
    private static string ContractName(string logicalName)
    {
        string[] words = logicalName.Split(' ');
        return words[0].ToLowerInvariant() + string.Concat(words.Skip(1).Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1).ToLowerInvariant()));
    }

    private static IEnumerable<JsonElement> FirstReleaseEntities()
    {
        return Model.GetProperty("entities").EnumerateArray()
            .Where(e => e.GetProperty("status").GetString() != "later" && !e.TryGetProperty("storedIn", out _));
    }

    // Class F differs per entity; the model names the Azure SQL table type of each in its history class text.
    private static Dictionary<string, string> ClassFStorage()
    {
        string text = Model.GetProperty("historyClasses").EnumerateArray().Single(c => c.GetProperty("id").GetString() == "F").GetProperty("azureSql").GetString()!;
        var storage = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string id in Ids(Regex.Match(text, @"Updatable ledger table \(([^)]*)\)").Groups[1].Value))
        {
            storage[id] = "updatable";
        }

        foreach (string id in Ids(Regex.Match(text, @"append-only ledger table \(([^)]*)\)").Groups[1].Value))
        {
            storage[id] = "append-only";
        }

        foreach (string id in Ids(Regex.Match(text, @"([A-Z]\d\d(?: and [A-Z]\d\d)*): temporal table").Groups[1].Value.Replace(" and ", ",")))
        {
            storage[id] = "temporal";
        }

        Assert.Equal(7, storage.Count);
        return storage;
    }

    private static IEnumerable<string> Ids(string list)
    {
        return list.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).Where(s => Regex.IsMatch(s, @"^[A-Z]\d\d$"));
    }

    // The contract names of an entity's columns in the model on disk: its column sets' attributes and its own.
    private static HashSet<string> ModelColumns(JsonElement entity)
    {
        var columns = new HashSet<string>(StringComparer.Ordinal);
        if (entity.TryGetProperty("columnSets", out JsonElement sets))
        {
            foreach (JsonElement set in sets.EnumerateArray())
            {
                JsonElement definition = Model.GetProperty("columnSets").EnumerateArray().Single(c => c.GetProperty("name").GetString() == set.GetString());
                columns.UnionWith(definition.GetProperty("attributes").EnumerateArray().Select(a => JsonName(a.GetProperty("name").GetString()!)));
            }
        }

        columns.UnionWith(entity.GetProperty("attributes").EnumerateArray().Select(a => JsonName(a.GetProperty("name").GetString()!)));
        return columns;
    }

    private static string Label((string Id, string[] Columns) key)
    {
        return key.Id + " " + string.Join("+", key.Columns);
    }

    // "Request Key" gives "requestKey".
    private static string JsonName(string logicalName)
    {
        string[] words = logicalName.Split(' ');
        return words[0].ToLowerInvariant() + string.Concat(words.Skip(1));
    }
}
