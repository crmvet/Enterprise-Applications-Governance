using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Product.Azure.Stores;
using Product.Azure.Stores.Sqlite;
using Product.Azure.Stores.SqlServer;
using Product.Engine.Configuration.Composition;
using Product.Engine.Configuration.Patches;
using Product.Engine.Configuration.Presets;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Configuration.Validation;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Runtime.Callers;
using Product.Engine.Runtime.Ports;
using Product.Engine.Testing;
using Product.Engine.Testing.InMemory;
using Product.Mcp.Testing;
using Xunit;

namespace Product.Azure.Stores.Tests;

// Where the tests find src/, the presets and the SQL scripts.
internal static class Paths
{
    private static readonly Lazy<string> LazySource = new(() =>
    {
        string? project = typeof(Paths).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "ProjectDirectory")?.Value;
        return SourceRoot.Find(project ?? string.Empty) ?? SourceRoot.Require(AppContext.BaseDirectory);
    });

    // src/
    public static string Source => LazySource.Value;

    // src/adapters/azure
    public static string Adapter => Path.Combine(Source, "adapters", "azure");

    // src/adapters/azure/sql
    public static string Sql => Path.Combine(Adapter, "sql");

    // A new empty folder for one test's files, removed with the test's store.
    public static string NewScratchFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "product-store-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }
}

// A fictional client: Balanced plus a client binding patch (three group bindings and two workload grants), a small
// directory of Principal References, and an estate of one portfolio, one application with a business owner and one
// production target. The same world the engine runtime tests use; every ID is a patterned placeholder.
internal static class World
{
    public static readonly DateTime AsOf = new(2026, 11, 22, 9, 0, 0, DateTimeKind.Utc);

    public const string RequesterGroup = "00000000-0000-4000-8000-000000000101";
    public const string PlatformAdminGroup = "00000000-0000-4000-8000-000000000102";
    public const string ConfigurationAdminGroup = "00000000-0000-4000-8000-000000000103";
    public const string Person1 = "00000000-0000-4000-8000-000000000201";
    public const string Person2 = "00000000-0000-4000-8000-000000000202";
    public const string Person3 = "00000000-0000-4000-8000-000000000203";
    public const string Runner = "00000000-0000-4000-8000-000000000301";
    public const string Pipeline = "00000000-0000-4000-8000-000000000302";
    public const string NewPipeline = "00000000-0000-4000-8000-000000000303";

    private static readonly Lazy<PresetCatalog> LazyCatalog = new(() => PresetCatalog.Load(new[] { SourceRoot.PresetsFolder(Paths.Source) }));
    private static readonly ConcurrentDictionary<int, CompiledSnapshot> Compiled = new();

    public static PresetCatalog Catalog => LazyCatalog.Value;

    public static CallerIdentity AiClientOf1 => CallerIdentity.AiClient(Person1, new[] { RequesterGroup, ConfigurationAdminGroup }, "Claude Code");

    public static CallerIdentity PersonOne => CallerIdentity.Person(Person1, new[] { RequesterGroup, ConfigurationAdminGroup }, "Product page");

    public static CallerIdentity PersonTwo => CallerIdentity.Person(Person2, new[] { PlatformAdminGroup }, "Product page");

    public static CallerIdentity ApprovalRunner => CallerIdentity.Workload(Runner, clientApp: "approval runner");

    public static CallerIdentity ReleasePipeline => CallerIdentity.Workload(NewPipeline, clientApp: "release pipeline");

    // Balanced plus the client binding patch, validated in activation mode on the fixture date, as the version an
    // activation would leave in force. Compiled once per version number.
    public static CompiledSnapshot Version(int versionNumber)
    {
        return Compiled.GetOrAdd(versionNumber, n =>
        {
            ContentSource source = ContentSource.Preset("balanced").WithPatch(ConfigurationPatch.FromOperations(CanonicalJson.Parse("[" + BindingOperations + "]"), PatchKind.ClientBinding, "client binding patch"));
            ValidationOutcome outcome = new ConfigurationValidator(Catalog).Validate(new ValidationRequest(
                source,
                ValidationMode.Activation,
                versionNumber: n,
                facts: new ValidationFacts(validatedOn: AsOf, principalKinds: DirectoryKinds())));
            Assert.True(outcome.Snapshot != null, "the fixture version is invalid: " + string.Join(" | ", outcome.Report.Issues.Select(i => i.Reason.Code + " " + i.Reason.Message)));
            return outcome.Snapshot!;
        });
    }

    public static IEnumerable<KeyValuePair<string, PrincipalKind>> DirectoryKinds()
    {
        yield return new KeyValuePair<string, PrincipalKind>(RequesterGroup, PrincipalKind.Group);
        yield return new KeyValuePair<string, PrincipalKind>(PlatformAdminGroup, PrincipalKind.Group);
        yield return new KeyValuePair<string, PrincipalKind>(ConfigurationAdminGroup, PrincipalKind.Group);
        yield return new KeyValuePair<string, PrincipalKind>(Person1, PrincipalKind.User);
        yield return new KeyValuePair<string, PrincipalKind>(Person2, PrincipalKind.User);
        yield return new KeyValuePair<string, PrincipalKind>(Person3, PrincipalKind.User);
        yield return new KeyValuePair<string, PrincipalKind>(Runner, PrincipalKind.ServicePrincipal);
        yield return new KeyValuePair<string, PrincipalKind>(Pipeline, PrincipalKind.ServicePrincipal);
        yield return new KeyValuePair<string, PrincipalKind>(NewPipeline, PrincipalKind.ServicePrincipal);
    }

    // The directory and the estate, seeded outside the engine. Returns the application ID.
    public static string SeedDirectoryAndEstate(IStoreUnderTest store)
    {
        foreach (KeyValuePair<string, PrincipalKind> principal in DirectoryKinds())
        {
            string kind = EngineContract.PrincipalKinds.ToWire(principal.Value);
            store.Seed("principal-reference", "{\"principalKind\":\"" + kind + "\",\"entraObjectId\":\"" + principal.Key + "\",\"displayLabel\":\"Fixture " + kind + " "
                + principal.Key.Substring(principal.Key.Length - 3) + "\",\"isErased\":false}");
        }

        string portfolio = store.Seed("portfolio", "{\"level\":\"department\",\"isActive\":true}");
        string application = store.Seed("business-application", "{\"recordNumber\":\"APP-0001\",\"purpose\":\"Fixture expense approvals\",\"applicationKind\":\"power-platform-app\","
            + "\"portfolio\":\"" + portfolio + "\",\"status\":\"app-registered\",\"statusMeaning\":\"registered\",\"tier\":\"tier-1\",\"lifecycleStage\":\"operate\","
            + "\"stageSince\":\"2026-01-05T00:00:00Z\",\"configurationSetVersion\":1}");
        store.Seed("owner-assignment", "{\"businessApplication\":\"" + application + "\",\"ownerRole\":\"business-owner\",\"principal\":\"" + Person1 + "\",\"isDeputy\":false,\"startOn\":\"2026-01-05\"}");
        store.Seed("deployment-target", "{\"targetKind\":\"environment\",\"targetStage\":\"production\",\"changePath\":\"pipeline-only\",\"linkKind\":\"power-platform-resource\","
            + "\"externalId\":\"fixture-env-prod\",\"labelAsSeen\":\"Fixture production\"}");
        return application;
    }

    private static string Binding(string key, string role, string group)
    {
        return "{\"op\":\"upsert\",\"entity\":\"role-group-binding\",\"key\":\"" + key + "\",\"values\":{\"label\":\"" + key + "\",\"roleKey\":\"" + role
            + "\",\"entraGroupObjectId\":\"" + group + "\",\"bindingMethod\":\"entra-app-role-assignment\",\"bindingDetail\":\"App role " + role + "\"}}";
    }

    private static string Grant(string key, string role, string principal)
    {
        return "{\"op\":\"upsert\",\"entity\":\"workload-grant\",\"key\":\"" + key + "\",\"values\":{\"label\":\"" + key + "\",\"principalObjectId\":\"" + principal
            + "\",\"workloadRoleKey\":\"" + role + "\",\"reason\":\"Fixture workload " + key + "\",\"usesFederatedCredential\":true,\"bindingDetail\":\"App role assignment " + key + "\",\"expiresOn\":\"2027-06-30\"}}";
    }

    private static string BindingOperations => string.Join(",", new[]
    {
        Binding("requesters", "requester", RequesterGroup),
        Binding("platform-admins", "platform-admin", PlatformAdminGroup),
        Binding("configuration-admins", "configuration-admin", ConfigurationAdminGroup),
        Grant("approval-runner", "approval-writer", Runner),
        Grant("build-pipeline", "deployment-recorder", Pipeline),
    });
}

public enum StoreKind
{
    InMemory,
    Sqlite,
    SqlServer,
}

// One store under test, whatever its kind: the two engine ports, the counter, and seeding.
public interface IStoreUnderTest : IDisposable
{
    StoreKind Kind { get; }

    // The record port as the engine binds it. For the in-memory store, wrapped so new rows get repeatable GUIDs, as Azure
    // SQL rows do.
    IRecordStore Records { get; }

    IConfigurationStore Configuration { get; }

    // True for the row stores: optimistic versions, the model's entity list and append-only entities.
    bool IsRowStore { get; }

    string Seed(string entity, string columnsJson, string? id = null, string? key = null);

    void Replace(string entity, string id, string columnsJson);

    void SeedVersion(CompiledSnapshot snapshot);

    long Increment(string counterKey, DateTime windowStartUtc, TimeSpan windowLength);

    // Every committed row as canonical text, as InMemoryStore.Dump writes it.
    string Dump();
}

internal sealed class InMemoryUnderTest : IStoreUnderTest
{
    private readonly InMemoryStore _store = new("fixture-store");
    private readonly InMemoryRateLimitStore _counters = new();

    public InMemoryUnderTest()
    {
        Records = new GuidIdRecordStore(_store);
    }

    public StoreKind Kind => StoreKind.InMemory;

    public IRecordStore Records { get; }

    public IConfigurationStore Configuration => _store;

    public bool IsRowStore => false;

    public InMemoryStore Store => _store;

    public string Seed(string entity, string columnsJson, string? id = null, string? key = null)
    {
        return _store.Seed(entity, columnsJson, id, key);
    }

    public void Replace(string entity, string id, string columnsJson)
    {
        _store.Tamper(entity, id, CanonicalJson.Parse(columnsJson));
    }

    public void SeedVersion(CompiledSnapshot snapshot)
    {
        _store.SeedVersion(snapshot);
    }

    public long Increment(string counterKey, DateTime windowStartUtc, TimeSpan windowLength)
    {
        return _counters.Increment(counterKey, windowStartUtc, windowLength);
    }

    public string Dump()
    {
        return _store.Dump();
    }

    public void Dispose()
    {
    }
}

internal sealed class RowStoreUnderTest : IStoreUnderTest
{
    private readonly Action? _cleanup;

    public RowStoreUnderTest(StoreKind kind, RowStore store, Action? cleanup = null)
    {
        Kind = kind;
        Store = store;
        _cleanup = cleanup;
    }

    public StoreKind Kind { get; }

    public RowStore Store { get; }

    public IRecordStore Records => Store;

    public IConfigurationStore Configuration => Store;

    public bool IsRowStore => true;

    public string Seed(string entity, string columnsJson, string? id = null, string? key = null)
    {
        return Store.Seed(entity, CanonicalJson.Parse(columnsJson), id, key);
    }

    public void Replace(string entity, string id, string columnsJson)
    {
        Store.Replace(entity, id, CanonicalJson.Parse(columnsJson));
    }

    public void SeedVersion(CompiledSnapshot snapshot)
    {
        Store.SeedVersion(snapshot);
    }

    public long Increment(string counterKey, DateTime windowStartUtc, TimeSpan windowLength)
    {
        return Store.Increment(counterKey, windowStartUtc, windowLength);
    }

    public string Dump()
    {
        return Store.Dump();
    }

    public void Dispose()
    {
        Store.Dispose();
        _cleanup?.Invoke();
    }
}

// Makes stores of each kind. The row stores make the same IDs as the in-memory store wrapped in GuidIdRecordStore:
// sequential GUIDs for rows the engine creates, and "entity-000001" for seeded rows.
internal static class Stores
{
    public static IStoreUnderTest Create(StoreKind kind)
    {
        switch (kind)
        {
            case StoreKind.InMemory:
                return new InMemoryUnderTest();
            case StoreKind.Sqlite:
                return NewSqlite();
            default:
                return NewSqlServer();
        }
    }

    // A SQLite store on a new file in a new temporary folder; the folder goes with the store.
    public static RowStoreUnderTest NewSqlite(string? file = null, string idPrefix = "00000000-0000-4000-9000-")
    {
        string folder = file == null ? Paths.NewScratchFolder() : Path.GetDirectoryName(file)!;
        string path = file ?? Path.Combine(folder, "product.db");
        var store = new SqliteProductStore(new SqliteStoreOptions
        {
            ConnectionString = "Data Source=" + path,
            StoreId = "fixture-store",
            NewId = RowIds.Sequential(idPrefix),
            NewSeedId = RowIds.EntityCounter(),
        });
        return new RowStoreUnderTest(StoreKind.Sqlite, store, file == null ? () => TryDelete(folder) : null);
    }

    // An Azure SQL store on the database PRODUCT_TEST_SQLSERVER names, migrated once per test run. Skips the test when
    // the variable is not set. The database keeps ledger rows for ever, so these stores make random IDs.
    public static RowStoreUnderTest NewSqlServer()
    {
        string connection = SqlServerTarget.RequireConnectionString();
        var store = new SqlServerProductStore(new SqlServerStoreOptions
        {
            ConnectionString = connection,
            StoreId = "fixture-store-" + Guid.NewGuid().ToString("N"),
        });
        store.EnsureSchema();
        return new RowStoreUnderTest(StoreKind.SqlServer, store);
    }

    public static void TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

// The optional live Azure SQL target. PRODUCT_TEST_SQLSERVER: a connection string to an empty database the tests may
// migrate and fill (ledger rows can never be deleted, so use a new database for each run). The identity it signs in
// with must be the database owner. PRODUCT_TEST_SQLSERVER_APP_IDENTITY: optional, the managed identity name for
// 0003-app-user.sql (empty on a server without Microsoft Entra ID).
internal static class SqlServerTarget
{
    public const string ConnectionVariable = "PRODUCT_TEST_SQLSERVER";
    public const string AppIdentityVariable = "PRODUCT_TEST_SQLSERVER_APP_IDENTITY";

    private static readonly Lazy<string?> Migrated = new(() =>
    {
        string? connection = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (string.IsNullOrWhiteSpace(connection))
        {
            return null;
        }

        var variables = new Dictionary<string, string> { { "AppIdentityName", Environment.GetEnvironmentVariable(AppIdentityVariable) ?? string.Empty } };
        SqlServerMigrations.Apply(connection, variables, TextWriter.Null);
        return connection;
    });

    public static bool IsConfigured => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionVariable));

    public static string RequireConnectionString()
    {
        Assert.SkipUnless(IsConfigured, "Set " + ConnectionVariable + " to a connection string of an empty Azure SQL or SQL Server 2022 database to run the Azure SQL store tests.");
        return Migrated.Value!;
    }
}

// The caller every store test runs as. The Azure SQL store writes it to SESSION_CONTEXT; the others ignore it.
internal static class TestCaller
{
    public static IDisposable Enter(ActorKindMeaning? actorKind = ActorKindMeaning.Person)
    {
        return StoreCallContext.Enter(new StoreCallContext(World.Person1, CallerEntry.Person, actorKind, "req-store-test", new[] { "requester" }, clientApp: "Store tests", clientAppVerified: true));
    }
}
