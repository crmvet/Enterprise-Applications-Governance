using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Product.Azure.Stores.SqlServer;
using Product.Engine.Core.Contract;
using Product.Engine.Runtime.Callers;
using Xunit;

namespace Product.Azure.Stores.Tests;

// The Azure SQL scripts: the checked-in files equal their generator, the runner reads them in order and splits them as
// sqlcmd does, every entity has its table and history type, row-level security covers every table, and the app's role
// cannot delete what the model never deletes. No database is needed; the live test is in the Azure SQL conformance class.
public sealed class SqlScriptTests
{
    // PRODUCT_WRITE_SQL=1 writes the generated scripts into sql/ instead of failing on a difference.
    [Fact]
    public void TheGeneratedScriptsEqualTheCheckedInFiles()
    {
        bool write = Environment.GetEnvironmentVariable("PRODUCT_WRITE_SQL") == "1";
        foreach (KeyValuePair<string, string> script in SqlServerSchemaScript.GenerateAll(EntityCatalog.Embedded))
        {
            string path = Path.Combine(Paths.Sql, script.Key);
            if (write)
            {
                File.WriteAllText(path, script.Value, new UTF8Encoding(false));
            }

            Assert.True(File.Exists(path), script.Key + " is missing: run the store tests with PRODUCT_WRITE_SQL=1.");
            Assert.True(script.Value == Normalize(File.ReadAllText(path)), script.Key + " differs from its generator: run the store tests with PRODUCT_WRITE_SQL=1 and review the change.");
        }
    }

    [Fact]
    public void TheRunnerReadsEveryFileInTheSqlFolderInOrder()
    {
        string[] files = Directory.GetFiles(Paths.Sql, "*.sql").Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray()!;
        Assert.Equal(files, SqlServerMigrations.All.Select(m => m.Name));
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, SqlServerMigrations.All.Select(m => m.Version));
        Assert.Equal(5, SqlServerMigrations.RequiredVersion);
        foreach (SqlMigration migration in SqlServerMigrations.All)
        {
            Assert.Equal(Normalize(File.ReadAllText(Path.Combine(Paths.Sql, migration.Name))), Normalize(migration.Text));
        }
    }

    [Fact]
    public void BatchesSplitOnLinesThatHoldOnlyGo()
    {
        IReadOnlyList<string> batches = SqlServerMigrations.SplitBatches("SELECT 1;\r\nGO\nSELECT 2;\n  go -- next\n-- only a comment\n\nGO\nGOTO done;\nSELECT 3; -- GO\n");
        Assert.Equal(new[] { "SELECT 1;\n", "SELECT 2;\n", "GOTO done;\nSELECT 3; -- GO\n" }, batches);
        Assert.Empty(SqlServerMigrations.SplitBatches("-- nothing\nGO\n\nGO\n"));
    }

    [Fact]
    public void EveryScriptSplitsAndNamesOnlyTheAppIdentityVariable()
    {
        foreach (SqlMigration migration in SqlServerMigrations.All)
        {
            Assert.NotEmpty(SqlServerMigrations.SplitBatches(migration.Text));
            Assert.All(SqlServerMigrations.VariablesOf(migration.Text), v => Assert.Equal("AppIdentityName", v));
        }

        Assert.Equal(new[] { "AppIdentityName" }, SqlServerMigrations.VariablesOf(SqlServerMigrations.All.Single(m => m.Version == 3).Text));
        string substituted = SqlServerMigrations.Substitute(SqlServerMigrations.All.Single(m => m.Version == 3).Text, new Dictionary<string, string> { { "appidentityname", "id-product-api" } });
        Assert.Contains("CREATE USER [id-product-api] FROM EXTERNAL PROVIDER", substituted);
        Assert.DoesNotContain("$(", substituted);
    }

    [Theory]
    [InlineData("name]; DROP TABLE x; --")]
    [InlineData("o'brien")]
    [InlineData("tab\there")]
    public void AVariableThatCouldCloseABracketOrAStringIsRefused(string value)
    {
        Assert.Throws<ArgumentException>(() => SqlServerMigrations.Substitute("[$(AppIdentityName)]", new Dictionary<string, string> { { "AppIdentityName", value } }));
    }

    [Fact]
    public void AMissingVariableIsRefused()
    {
        Assert.Throws<ArgumentException>(() => SqlServerMigrations.Substitute("[$(AppIdentityName)]", new Dictionary<string, string>()));
        Assert.Equal("[]", SqlServerMigrations.Substitute("[$(AppIdentityName)]", new Dictionary<string, string> { { "AppIdentityName", string.Empty } }));
    }

    [Fact]
    public void EveryEntityHasItsTableHistoryTypeAndIndexes()
    {
        string tables = SqlServerSchemaScript.GenerateTables(EntityCatalog.Embedded);
        Assert.Equal(EntityCatalog.Embedded.Entities.Count, Count(tables, "    CREATE TABLE "));
        foreach (EntityInfo e in EntityCatalog.Embedded.Entities)
        {
            int start = tables.IndexOf("    CREATE TABLE " + e.QualifiedTable + "\n", StringComparison.Ordinal);
            Assert.True(start > 0, e.QualifiedTable);
            int end = tables.IndexOf("END;", start, StringComparison.Ordinal);
            string create = tables.Substring(start, end - start);
            switch (e.Storage)
            {
                case EntityStorage.Temporal:
                    Assert.Contains("WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = " + e.QualifiedHistoryTable + "));", create);
                    Assert.Contains("PERIOD FOR SYSTEM_TIME", create);
                    break;
                case EntityStorage.UpdatableLedger:
                    Assert.Contains("WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = " + e.QualifiedHistoryTable + "), LEDGER = ON);", create);
                    Assert.DoesNotContain("PERIOD FOR SYSTEM_TIME", create);
                    break;
                default:
                    Assert.Contains("WITH (LEDGER = ON (APPEND_ONLY = ON));", create);
                    break;
            }

            foreach (PromotedColumn column in e.Promoted)
            {
                Assert.Contains("[" + column.SqlName + "] " + column.SqlType + " NULL", create);
            }

            Assert.Contains("CREATE INDEX [IX_" + e.Table + "_Key] ON " + e.QualifiedTable + " ([Key]);", tables);
        }

        Assert.Contains("CREATE UNIQUE INDEX [UX_AuditEntry_RequestKey] ON [audit].[AuditEntry] ([RequestKey]) WHERE [RequestKey] IS NOT NULL;", tables);
        Assert.Contains("CREATE UNIQUE INDEX [UX_DeploymentRecord_IdempotencyKey] ON [ops].[DeploymentRecord] ([IdempotencyKey]) WHERE [IdempotencyKey] IS NOT NULL;", tables);
        Assert.Contains("CREATE UNIQUE INDEX [UX_ConfigurationSetVersion_Version] ON [config].[ConfigurationSetVersion] ([ConfigurationSetKey], [VersionNumber]) WHERE [ConfigurationSetKey] IS NOT NULL AND [VersionNumber] IS NOT NULL;", tables);
        Assert.Contains("CREATE INDEX [IX_PrincipalReference_EntraObjectId] ON [ops].[PrincipalReference] ([EntraObjectId]);", tables);
    }

    [Fact]
    public void RowLevelSecurityCoversEveryTableAndEveryTemporalHistoryTable()
    {
        string security = SqlServerSchemaScript.GenerateSecurity(EntityCatalog.Embedded);
        foreach (EntityInfo e in EntityCatalog.Embedded.Entities)
        {
            string filter = "ADD FILTER PREDICATE [security].[fn_HasCaller]([Id]) ON " + e.QualifiedTable;
            Assert.True(security.Contains(filter + ",\n", StringComparison.Ordinal) || security.Contains(filter + "\n", StringComparison.Ordinal), filter);
            if (e.Storage == EntityStorage.Temporal)
            {
                Assert.Contains("ADD FILTER PREDICATE [security].[fn_HasCaller]([Id]) ON " + e.QualifiedHistoryTable, security);
            }
            else if (e.QualifiedHistoryTable != null)
            {
                Assert.DoesNotContain("ON " + e.QualifiedHistoryTable, security);
            }

            bool configWriter = e.IsConfiguration && e.Key != "configuration-draft";
            Assert.Equal(configWriter, security.Contains("[fn_ConfigWriter]([Id]) ON " + e.QualifiedTable + " AFTER INSERT", StringComparison.Ordinal));
        }

        Assert.Contains("[fn_ApprovalOutcomeWriter]([OutcomeMeaning]) ON [ops].[ApprovalRecord] AFTER UPDATE", security);
        Assert.Contains("[fn_ApprovalOutcomeWriter]([OutcomeMeaning]) ON [ops].[ApprovalRecord] AFTER INSERT", security);
        Assert.Contains("[fn_ApprovalPending]([OutcomeMeaning]) ON [ops].[ApprovalRecord] BEFORE UPDATE", security);
        Assert.Contains("[fn_DecisionOpen]([StatusMeaning], [SupersededById]) ON [ops].[Decision] BEFORE UPDATE", security);
        Assert.Contains("[fn_AssessmentOpen]([TierDecisionId]) ON [ops].[TierAssessment] BEFORE UPDATE", security);
        Assert.Contains("[fn_GapRunOpen]([FinishedOn]) ON [ops].[GapCheckRun] BEFORE UPDATE", security);
        Assert.Contains("[fn_DraftOpen]([DraftStatus]) ON [config].[ConfigurationDraft] BEFORE UPDATE", security);
        Assert.DoesNotContain("[ConfigurationSetVersion] AFTER UPDATE", security);
    }

    // The meanings the predicates name come from the engine's contract lists, so a renamed meaning changes the script.
    [Fact]
    public void ThePredicatesNameTheEnginesFinalMeaningsAndTheApprovalRunner()
    {
        string security = SqlServerSchemaScript.GenerateSecurity(EntityCatalog.Embedded);
        Assert.Contains("@StatusMeaning NOT IN (N'accepted', N'rejected', N'superseded', N'withdrawn')", security);
        Assert.Contains("@OutcomeMeaning NOT IN (N'approved', N'rejected', N'recommendation-only')", security);
        Assert.Contains("CAST(SESSION_CONTEXT(N'ActorKind') AS nvarchar(64)) = N'approval-runner'", security);
        Assert.Contains("@DraftStatus IN (N'draft', N'submitted')", security);
        Assert.Contains("CAST(SESSION_CONTEXT(N'EngineOperation') AS nvarchar(32)) = N'activation'", security);
        Assert.Contains("DATABASE_PRINCIPAL_ID() = 1", security);
    }

    [Fact]
    public void TheAppRoleCannotDeleteWhatTheModelNeverDeletesNorReadHistory()
    {
        string user = SqlServerSchemaScript.GenerateAppUser(EntityCatalog.Embedded);
        foreach (string table in new[] { "[ops].[PrincipalReference]", "[ops].[Decision]", "[ops].[ApprovalRecord]", "[ops].[DeploymentRecord]", "[ops].[EvidenceLink]" })
        {
            Assert.Contains("DENY DELETE ON " + table + " TO [product_app];", user);
        }

        Assert.Equal(5, Count(user, "DENY DELETE ON "));
        Assert.Contains("GRANT SELECT, INSERT, UPDATE ON SCHEMA::[config] TO [product_app];", user);
        Assert.Contains("GRANT SELECT, INSERT ON SCHEMA::[audit] TO [product_app];", user);
        Assert.Contains("history_table_id", user);
        Assert.Contains("ledger_view_id", user);
        Assert.DoesNotContain("DENY CONTROL", user);
        Assert.DoesNotContain("password", user, StringComparison.OrdinalIgnoreCase);
    }

    // 0005: the approval outbox is a host table the app reads, inserts and updates but never deletes from, with the states
    // the store writes and the columns it reads, and every statement checks before it acts.
    [Fact]
    public void TheApprovalOutboxScriptMatchesTheStoreAndGrantsNoDelete()
    {
        SqlMigration migration = SqlServerMigrations.All.Single(m => m.Version == 5);
        string script = Normalize(migration.Text);
        Assert.Equal("0005-approval-outbox.sql", migration.Name);
        Assert.Contains("IF OBJECT_ID(N'[host].[ApprovalOutbox]', N'U') IS NULL", script);
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_ApprovalOutbox_State'", script);
        Assert.Contains("CHECK ([State] IN (" + string.Join(", ", ApprovalOutboxStates.All.Select(s => "'" + s + "'")) + "))", script);
        foreach (string column in new[] { "ApprovalRecordId", "DecisionId", "EnqueuedOn", "State", "ClaimId", "ClaimedUntil", "Attempts", "CardRef", "RunnerRunUrl", "UpdatedOn", "RowVersion" })
        {
            Assert.Contains("[" + column + "] ", script);
        }

        Assert.Contains("GRANT SELECT, INSERT, UPDATE ON [host].[ApprovalOutbox] TO [product_app];", script);
        Assert.Contains("DENY DELETE ON [host].[ApprovalOutbox] TO [product_app];", script);
        Assert.DoesNotContain("GRANT DELETE", script, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(SqlServerMigrations.VariablesOf(script));
        Assert.Equal(3, SqlServerMigrations.SplitBatches(script).Count);
    }

    [Fact]
    public void NoScriptHoldsAnEnOrEmDash()
    {
        foreach (string file in Directory.GetFiles(Paths.Sql, "*.sql"))
        {
            string text = File.ReadAllText(file);
            Assert.DoesNotContain(((char)0x2013).ToString(), text);
            Assert.DoesNotContain(((char)0x2014).ToString(), text);
        }
    }

    // ---- The session context the store writes ----

    [Fact]
    public void TheSessionContextCarriesTheCallerAndTheOperation()
    {
        var context = new StoreCallContext("00000000-0000-4000-8000-000000000201", CallerEntry.AiClient, ActorKindMeaning.AiClientForPerson, "req-7",
            new[] { "requester", "configuration-admin", "requester" }, onBehalfOfId: "00000000-0000-4000-8000-000000000201", clientApp: "Claude Code", clientAppVerified: true);
        var values = SqlServerRowDatabase.SessionValues(context, "activation").ToDictionary(v => v.Key, v => v.Value);
        Assert.Equal("00000000-0000-4000-8000-000000000201", values["PrincipalId"]);
        Assert.Equal("ai-client", values["Entry"]);
        Assert.Equal("ai-client-for-person", values["ActorKind"]);
        Assert.Equal("00000000-0000-4000-8000-000000000201", values["OnBehalfOfId"]);
        Assert.Equal("Claude Code", values["ClientApp"]);
        Assert.Equal(true, values["ClientAppVerified"]);
        Assert.Equal("configuration-admin,requester", values["Roles"]);
        Assert.Equal("req-7", values["CorrelationId"]);
        Assert.Equal("activation", values["EngineOperation"]);
    }

    [Fact]
    public void WithoutACallerOnlyASeedSetsTheSessionContext()
    {
        Assert.Empty(SqlServerRowDatabase.SessionValues(null, "read"));
        Assert.Empty(SqlServerRowDatabase.SessionValues(null, "write"));
        var seed = SqlServerRowDatabase.SessionValues(null, "seed").ToDictionary(v => v.Key, v => v.Value);
        Assert.Equal(new[] { "EngineOperation", "Entry", "PrincipalId" }, seed.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal("setup", seed["Entry"]);
        Assert.Equal("setup", SqlServerRowDatabase.SessionValues(new StoreCallContext("someone", CallerEntry.Person, null, "req-9"), "seed").Single(v => v.Key == "Entry").Value);

        var runner = new StoreCallContext("00000000-0000-4000-8000-000000000301", CallerEntry.Workload, ActorKindMeaning.ApprovalRunner, "req-8");
        var values = SqlServerRowDatabase.SessionValues(runner, "write").ToDictionary(v => v.Key, v => v.Value);
        Assert.Equal("approval-runner", values["ActorKind"]);
        Assert.Equal("workload", values["Entry"]);
        Assert.False(values.ContainsKey("Roles"));
        Assert.False(values.ContainsKey("OnBehalfOfId"));
    }

    [Fact]
    public void AnAzureSqlStoreNamesItselfByServerAndDatabaseAndConnectsOnlyWhenUsed()
    {
        using var store = new SqlServerProductStore(new SqlServerStoreOptions
        {
            ConnectionString = "Server=tcp:contoso-fixture.database.windows.net,1433;Database=product;Authentication=Active Directory Default;Encrypt=True",
        });
        Assert.Equal("azuresql:contoso-fixture.database.windows.net/product", store.StoreId);
        Assert.Throws<ArgumentException>(() => new SqlServerProductStore(new SqlServerStoreOptions()));
        Assert.Throws<ArgumentException>(() => new SqlServerProductStore(new SqlServerStoreOptions { ConnectionString = "Server=tcp:x.database.windows.net" }));
    }

    [Fact]
    public void AnEngineWriteWithoutACallerIsRefusedBeforeItReachesAzureSql()
    {
        using var database = new SqlServerRowDatabase("Server=tcp:127.0.0.1,1;Database=product;Encrypt=True;Connect Timeout=1", EntityCatalog.Embedded, 30, setSessionContext: true);
        RowChange insert = RowChange.Insert(EntityCatalog.Embedded.Get("portfolio"), Guid.NewGuid().ToString("D"), null, Product.Engine.Core.Canonical.CanonicalJson.Parse("{}"));
        Assert.Null(StoreCallContext.Current);
        Assert.Contains("StoreCallContext", Assert.Throws<InvalidOperationException>(() => database.Apply(new[] { insert }, null, RowStoreOperations.Write)).Message);
        Assert.Contains("StoreCallContext", Assert.Throws<InvalidOperationException>(() => database.Apply(new[] { insert }, null, RowStoreOperations.Activation)).Message);
    }

    private static string Normalize(string text)
    {
        return text.Replace("\r\n", "\n");
    }

    private static int Count(string text, string part)
    {
        int count = 0;
        for (int at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
