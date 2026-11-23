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
// table in the schema of its kind, its history class gives the table type the model names for Azure SQL, and every key
// the stores enforce is a key the model asks the store to enforce, on both providers alike.
public sealed class LogicalModelConformanceTests
{
    private static readonly Lazy<JsonDocument> LazyModel = new(() => JsonDocument.Parse(File.ReadAllText(Path.Combine(Paths.Source, "model", "logical-model.json"))));

    // The store keys of the model that the row stores do not enforce in this release, as the in-memory store does not:
    // the engine checks them from its facts. They are the unique constraints of the typed schema draft
    // (model/generated/azure-sql-0001-initial.draft.sql), the target of a later migration. A store key the model adds
    // fails this test until the stores enforce it or it joins this list on purpose.
    private static readonly string[] StoreKeysLeftToTheEngine =
    {
        "C01 Name",
        "C01 Preset Key",
        "C02 Content Hash",
        "C04 Key+Configuration Set", "C05 Key+Configuration Set", "C06 Key+Configuration Set",
        "C07 Key+Configuration Set", "C07 Value List+Key+Configuration Set",
        "C08 Key+Configuration Set", "C08 Rank+Configuration Set",
        "C09 Key+Configuration Set", "C10 Key+Configuration Set",
        "C11 Key+Configuration Set", "C11 Source Entity+Source Key+Vocabulary+Configuration Set",
        "C12 Key+Configuration Set", "C12 Stage Rank+Configuration Set",
        "C13 Key+Configuration Set", "C14 Key+Configuration Set", "C15 Key+Configuration Set", "C16 Key+Configuration Set",
        "C17 Key+Configuration Set", "C18 Key+Configuration Set", "C19 Key+Configuration Set", "C20 Key+Configuration Set",
        "C21 Key+Configuration Set", "C22 Key+Configuration Set", "C23 Key+Configuration Set", "C24 Key+Configuration Set",
        "C25 Key+Configuration Set", "C25 Review Type+Tier+Configuration Set",
        "C26 Key+Configuration Set", "C27 Key+Configuration Set", "C28 Key+Configuration Set", "C29 Key+Configuration Set",
        "C30 Key+Configuration Set", "C30 Role+Entra Group+Binding Method+Configuration Set",
        "C31 Key+Configuration Set", "C32 Key+Configuration Set",
        "C33 Key+Configuration Set", "C33 Tier+Change Type+Target Stage+Portfolio+Configuration Set",
        "C34 Key+Configuration Set", "C35 Key+Configuration Set", "C36 Key+Configuration Set", "C37 Key+Configuration Set",
        "C38 Key+Configuration Set",
        "C39 Key+Configuration Set", "C39 Number+Version+Configuration Set",
        "C40 Key+Configuration Set", "C41 Key+Configuration Set",
        "C42 Key+Configuration Set", "C42 Principal+Workload Role+Configuration Set",
        "C43 Key+Configuration Set", "C44 Key+Configuration Set", "C45 Key+Configuration Set",
        "C46 Record Number",
        "O01 Name+Parent Portfolio",
        "O02 Record Number",
        "O03 Link Kind+External Id",
        "O05 Entra Object Id",
        "O07 Record Number",
        "O09 Record Number",
        "O11 Record Number",
        "O12 Change Request+Business Application",
        "O13 Artifact Name+Version",
        "O15 Link Kind+External Id",
        "O16 Link Kind+External Id",
        "O17 Business Application+Pipeline Reference+Deployment Target",
        "O19 Business Application+Documentation Requirement",
        "O21 Business Application+Review Type+Due On",
        "O23 Claim Key",
    };

    private static JsonElement Model => LazyModel.Value.RootElement;

    [Fact]
    public void EveryFirstReleaseEntityHasOneTableOfItsHistoryClass()
    {
        string tables = File.ReadAllText(Path.Combine(Paths.Sql, "0001-tables.sql"));
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
        Assert.Equal(69, created.Length);
    }

    [Fact]
    public void TheStoresEnforceOnlyKeysTheModelGivesTheStoreAndTheRestAreListed()
    {
        string tables = File.ReadAllText(Path.Combine(Paths.Sql, "0001-tables.sql"));
        var storeKeys = new List<(string Id, string[] Columns)>();
        foreach (JsonElement entity in FirstReleaseEntities())
        {
            foreach (JsonElement key in entity.GetProperty("keys").EnumerateArray().Where(k => k.GetProperty("enforcement").GetString() == "store"))
            {
                storeKeys.Add((entity.GetProperty("id").GetString()!, key.GetProperty("columns").EnumerateArray().Select(c => c.GetString()!).ToArray()));
            }
        }

        // Every unique key the stores hold covers a store key of the model (the same columns, or fewer, which is stricter).
        var enforced = new HashSet<string>(StringComparer.Ordinal);
        foreach (EntityInfo entity in EntityCatalog.Embedded.Entities.Where(e => e.UniqueColumns.Any()))
        {
            string[] columns = entity.UniqueColumns.Select(c => c.JsonName).ToArray();
            (string Id, string[] Columns) match = storeKeys.FirstOrDefault(k => k.Id == entity.ModelId && columns.All(c => k.Columns.Select(JsonName).Contains(c, StringComparer.Ordinal)));
            Assert.True(match.Id != null, entity.ModelId + " holds a unique key on " + string.Join(", ", columns) + " that the model does not give the store.");
            enforced.Add(Label(match));

            // Both providers from the same columns: the Azure SQL script holds this unique index, and the SQLite store's
            // one unique index takes the value RowChange.UniqueKey builds from the entity's unique columns.
            string index = "CREATE UNIQUE INDEX [UX_" + entity.Table + "_" + entity.UniqueColumns.First().UniqueGroup + "] ON " + entity.QualifiedTable
                + " (" + string.Join(", ", entity.UniqueColumns.Select(c => "[" + c.SqlName + "]")) + ")";
            Assert.Contains(index, tables, StringComparison.Ordinal);
        }

        Assert.Equal(new[] { "C02 Configuration Set+Version Number", "O14 Idempotency Key", "O24 Operation+Request Key" }, enforced.OrderBy(k => k, StringComparer.Ordinal));
        string[] left = storeKeys.Select(Label).Where(k => !enforced.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        Assert.Equal(StoreKeysLeftToTheEngine.OrderBy(k => k, StringComparer.Ordinal), left);
    }

    // ---- Helpers ----

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
