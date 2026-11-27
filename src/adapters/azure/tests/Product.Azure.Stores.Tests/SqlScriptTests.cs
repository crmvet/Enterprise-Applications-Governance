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
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, SqlServerMigrations.All.Select(m => m.Version));
        Assert.Equal(9, SqlServerMigrations.RequiredVersion);
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

    // 0001 creates the tables of the first release and 0008 those of the data taxonomy, so every entity has its table in
    // exactly one of them.
    [Fact]
    public void EveryEntityHasItsTableHistoryTypeAndIndexes()
    {
        string first = SqlServerSchemaScript.GenerateTables(EntityCatalog.Embedded);
        string taxonomy = SqlServerSchemaScript.GenerateDataTaxonomy(EntityCatalog.Embedded);
        string tables = first + taxonomy;
        Assert.Equal(EntityCatalog.Embedded.Entities.Count, Count(tables, "    CREATE TABLE "));
        Assert.Equal(69, Count(first, "    CREATE TABLE "));
        Assert.Equal(new[] { "C47", "C48", "C49", "C50", "O25", "O26", "O27" }, SqlServerSchemaScript.TablesOf(EntityCatalog.Embedded, 8).Select(e => e.ModelId));
        foreach (EntityInfo e in EntityCatalog.Embedded.Entities)
        {
            Assert.Equal(SqlServerSchemaScript.TableMigration(e) == 1, first.Contains("    CREATE TABLE " + e.QualifiedTable + "\n", StringComparison.Ordinal));
            Assert.Equal(SqlServerSchemaScript.TableMigration(e) == 8, taxonomy.Contains("    CREATE TABLE " + e.QualifiedTable + "\n", StringComparison.Ordinal));
        }
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

    // 0002 holds the policy over the tables of 0001, and 0008 the same policy over every table, its own included. The last
    // migration that writes the policy covers every table of the catalog.
    [Fact]
    public void RowLevelSecurityCoversEveryTableAndEveryTemporalHistoryTable()
    {
        string first = SqlServerSchemaScript.GenerateSecurity(EntityCatalog.Embedded);
        foreach (EntityInfo e in EntityCatalog.Embedded.Entities)
        {
            string filter = "ADD FILTER PREDICATE [security].[fn_HasCaller]([Id]) ON " + e.QualifiedTable;
            Assert.Equal(SqlServerSchemaScript.TableMigration(e) == 1, first.Contains(filter + ",\n", StringComparison.Ordinal) || first.Contains(filter + "\n", StringComparison.Ordinal));
        }

        string security = SqlServerSchemaScript.GenerateDataTaxonomy(EntityCatalog.Embedded);
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

        Assert.Contains("[fn_ApprovalOutcomeWriter]([OutcomeMeaning], [Id], [PerformedByActorKind], [PerformedByPrincipalId], [Columns]) ON [ops].[ApprovalRecord] AFTER UPDATE", security);
        Assert.Contains("[fn_ApprovalOutcomeWriter]([OutcomeMeaning], [Id], [PerformedByActorKind], [PerformedByPrincipalId], [Columns]) ON [ops].[ApprovalRecord] AFTER INSERT", security);
        Assert.Contains("[fn_ApprovalPending]([OutcomeMeaning], [Columns]) ON [ops].[ApprovalRecord] BEFORE UPDATE", security);
        Assert.Contains("[fn_DecisionOpen]([StatusMeaning], [SupersededById]) ON [ops].[Decision] BEFORE UPDATE", security);
        Assert.Contains("[fn_AssessmentOpen]([TierDecisionId]) ON [ops].[TierAssessment] BEFORE UPDATE", security);
        Assert.Contains("[fn_GapRunOpen]([FinishedOn]) ON [ops].[GapCheckRun] BEFORE UPDATE", security);
        Assert.Contains("[fn_DraftOpen]([DraftStatus]) ON [config].[ConfigurationDraft] BEFORE UPDATE", security);
        Assert.DoesNotContain("[ConfigurationSetVersion] AFTER UPDATE", security);
    }

    // Azure SQL refuses a second enabled security policy on a table that one already references (error 33264), which the
    // first deployment to a real database hit. So 0002 creates exactly one policy, and it drops the two older names.
    [Fact]
    public void RowLevelSecurityIsOnePolicy()
    {
        string security = SqlServerSchemaScript.GenerateSecurity(EntityCatalog.Embedded);
        Assert.Equal(1, Count(security, "CREATE SECURITY POLICY "));
        Assert.Contains("CREATE SECURITY POLICY [security].[" + SqlServerSchemaScript.PolicyName + "]", security);
        foreach (string old in new[] { "ProductReadPolicy", "ProductWritePolicy", SqlServerSchemaScript.PolicyName })
        {
            Assert.Contains("IF OBJECT_ID(N'[security].[" + old + "]', N'SP') IS NOT NULL DROP SECURITY POLICY [security].[" + old + "];", security);
        }

        // 0008 drops the one policy before it creates it again, and creates no second one.
        string taxonomy = SqlServerSchemaScript.GenerateDataTaxonomy(EntityCatalog.Embedded);
        Assert.Equal(1, Count(taxonomy, "CREATE SECURITY POLICY "));
        int drop = taxonomy.IndexOf("IF OBJECT_ID(N'[security].[" + SqlServerSchemaScript.PolicyName + "]', N'SP') IS NOT NULL DROP SECURITY POLICY [security].[" + SqlServerSchemaScript.PolicyName + "];", StringComparison.Ordinal);
        Assert.True(drop > 0 && drop < taxonomy.IndexOf("CREATE SECURITY POLICY ", StringComparison.Ordinal));
        Assert.DoesNotContain("CREATE OR ALTER FUNCTION", taxonomy);
    }

    // The meanings the predicates name come from the engine's contract lists, so a renamed meaning changes the script.
    [Fact]
    public void ThePredicatesNameTheEnginesFinalMeaningsAndTheApprovalRunner()
    {
        string security = SqlServerSchemaScript.GenerateSecurity(EntityCatalog.Embedded);
        Assert.Contains("@StatusMeaning NOT IN (N'accepted', N'rejected', N'superseded', N'withdrawn')", security);
        Assert.Contains("@OutcomeMeaning NOT IN (N'approved', N'rejected', N'recommendation-only')", security);
        Assert.Contains("CAST(SESSION_CONTEXT(N'ActorKind') AS nvarchar(64)) = N'approval-runner'", security);

        // D-38: a person's own answer request may write the answer on the one record it names, and the engine's recompute
        // may rewrite what counts on records already answered; nothing else in that request writes an answer.
        string personAnswer = "(CAST(SESSION_CONTEXT(N'ActorKind') AS nvarchar(64)) = N'person' AND CAST(SESSION_CONTEXT(N'Entry') AS nvarchar(16)) = N'person' AND CAST(SESSION_CONTEXT(N'AnsweredApprovalId') AS varchar(36)) IS NOT NULL)";
        string sameDecision = "EXISTS (SELECT 1 FROM [ops].[ApprovalRecord] AS [answered] WHERE [answered].[Id] = CAST(SESSION_CONTEXT(N'AnsweredApprovalId') AS varchar(36)) AND JSON_VALUE([answered].[Columns], N'$.decision') = JSON_VALUE(@Columns, N'$.decision'))";
        string byCaller = "LOWER(@PerformedByPrincipalId) = LOWER(CAST(SESSION_CONTEXT(N'PrincipalId') AS nvarchar(64)))";
        Assert.Contains("OR (" + personAnswer + " AND " + sameDecision + ")\n", security);
        Assert.Contains("OR (" + personAnswer + " AND (@Id = CAST(SESSION_CONTEXT(N'AnsweredApprovalId') AS varchar(36)) OR @PerformedByActorKind = N'engine') AND " + byCaller + " AND " + sameDecision + ")", security);
        Assert.Contains("@DraftStatus IN (N'draft', N'submitted')", security);
        Assert.Contains("CAST(SESSION_CONTEXT(N'EngineOperation') AS nvarchar(32)) = N'activation'", security);
        Assert.Contains("DATABASE_PRINCIPAL_ID() = 1", security);
    }

    // Security review: AnsweredApprovalId is the caller's own argument, read before the engine checks it, and Performed By
    // Actor Kind is copied from the row the writer sends. So in a person's answer request no predicate may open a record
    // on the strength of those alone: every person branch is tied to the answered record's decision, and a final outcome
    // must also carry the caller's own principal.
    [Fact]
    public void APersonsAnswerRequestReachesOnlyTheAnsweredRecordsDecision()
    {
        string security = SqlServerSchemaScript.GenerateSecurity(EntityCatalog.Embedded);
        string personAnswer = "(CAST(SESSION_CONTEXT(N'ActorKind') AS nvarchar(64)) = N'person' AND CAST(SESSION_CONTEXT(N'Entry') AS nvarchar(16)) = N'person' AND CAST(SESSION_CONTEXT(N'AnsweredApprovalId') AS varchar(36)) IS NOT NULL)";

        // No branch is the person's answer request on its own, in either function.
        Assert.DoesNotContain("OR " + personAnswer + "\n", security);
        Assert.DoesNotContain("OR " + personAnswer + ";", security);

        string pendingFn = Between(security, "CREATE OR ALTER FUNCTION [security].[fn_ApprovalPending]", "GO");
        string writerFn = Between(security, "CREATE OR ALTER FUNCTION [security].[fn_ApprovalOutcomeWriter]", "GO");
        foreach (string fn in new[] { pendingFn, writerFn })
        {
            Assert.Contains("FROM [ops].[ApprovalRecord] AS [answered] WHERE [answered].[Id] = CAST(SESSION_CONTEXT(N'AnsweredApprovalId') AS varchar(36))", fn);
            Assert.Contains("JSON_VALUE([answered].[Columns], N'$.decision') = JSON_VALUE(@Columns, N'$.decision')", fn);
        }

        Assert.Contains("LOWER(@PerformedByPrincipalId) = LOWER(CAST(SESSION_CONTEXT(N'PrincipalId') AS nvarchar(64)))", writerFn);
        Assert.Equal(1, Count(writerFn, "@PerformedByActorKind = N'engine'"));
    }

    private static string Between(string text, string start, string end)
    {
        int from = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, start);
        int to = text.IndexOf("\n" + end, from, StringComparison.Ordinal);
        Assert.True(to > from, end);
        return text.Substring(from, to - from);
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

    // 0007: the record number counters are a host table the app reads, inserts and updates but never deletes from, keyed
    // by the entity key the store writes, and every statement checks before it acts.
    [Fact]
    public void TheRecordNumberScriptMatchesTheStoreAndGrantsNoDelete()
    {
        SqlMigration migration = SqlServerMigrations.All.Single(m => m.Version == 7);
        string script = Normalize(migration.Text);
        Assert.Equal("0007-record-numbers.sql", migration.Name);
        IReadOnlyList<string> batches = SqlServerMigrations.SplitBatches(script);
        Assert.Equal(2, batches.Count);
        Assert.Contains("IF OBJECT_ID(N'[host].[RecordNumber]', N'U') IS NULL\n    CREATE TABLE [host].[RecordNumber]", batches[0]);
        Assert.Contains("[Entity] varchar(" + SqlServerRowDatabase.MaxEntityKeyLength + ") COLLATE Latin1_General_BIN2 NOT NULL CONSTRAINT [PK_RecordNumber] PRIMARY KEY", batches[0]);
        Assert.Contains("[LastNumber] bigint NOT NULL", batches[0]);
        Assert.Contains("GRANT SELECT, INSERT, UPDATE ON [host].[RecordNumber] TO [product_app];", batches[1]);
        Assert.Contains("DENY DELETE ON [host].[RecordNumber] TO [product_app];", batches[1]);
        Assert.DoesNotContain("GRANT DELETE", script, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(SqlServerMigrations.VariablesOf(script));
    }

    // 0006: the store keys are a host table the app reads, inserts and deletes from but never updates, keyed by entity, key
    // name and value hash as the store writes them, and the script stops before it changes a database that already holds
    // engine rows, whose keys it could not build.
    [Fact]
    public void TheStoreKeyScriptMatchesTheStoreAndRefusesAPopulatedDatabase()
    {
        SqlMigration migration = SqlServerMigrations.All.Single(m => m.Version == 6);
        string script = Normalize(migration.Text);
        Assert.Equal("0006-store-keys.sql", migration.Name);
        IReadOnlyList<string> batches = SqlServerMigrations.SplitBatches(script);
        Assert.Equal(4, batches.Count);

        // The guard comes first, checks before it acts, and reads the row counts of the engine's schemas only.
        Assert.StartsWith("IF OBJECT_ID(N'[host].[StoreKey]', N'U') IS NULL\n    AND EXISTS (", batches[0].Substring(batches[0].IndexOf("IF OBJECT_ID", StringComparison.Ordinal)));
        Assert.Contains("SCHEMA_NAME(t.[schema_id]) IN (N'config', N'ops', N'audit') AND p.[index_id] IN (0, 1) AND p.[rows] > 0", batches[0]);
        Assert.Contains("THROW 50006, N'Migration 0006 adds the store keys and needs empty engine tables", batches[0]);

        Assert.Contains("IF OBJECT_ID(N'[host].[StoreKey]', N'U') IS NULL\n    CREATE TABLE [host].[StoreKey]", batches[1]);
        Assert.Contains("[Entity] varchar(" + SqlServerRowDatabase.MaxEntityKeyLength + ") COLLATE Latin1_General_BIN2 NOT NULL", batches[1]);
        Assert.Contains("[KeyName] varchar(" + StoreKey.MaxNameLength + ") COLLATE Latin1_General_BIN2 NOT NULL", batches[1]);
        Assert.Contains("[ValueHash] binary(32) NOT NULL", batches[1]);
        Assert.Contains("[RowId] varchar(" + RowStore.MaxIdLength + ") COLLATE Latin1_General_BIN2 NOT NULL", batches[1]);
        Assert.Contains("CONSTRAINT [PK_StoreKey] PRIMARY KEY ([Entity], [KeyName], [ValueHash])", batches[1]);
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_StoreKey_Row'", batches[2]);
        Assert.Contains("CREATE INDEX [IX_StoreKey_Row] ON [host].[StoreKey] ([Entity], [RowId]) INCLUDE ([KeyName]);", batches[2]);
        Assert.Contains("GRANT SELECT, INSERT, DELETE ON [host].[StoreKey] TO [product_app];", batches[3]);
        Assert.Contains("DENY UPDATE ON [host].[StoreKey] TO [product_app];", batches[3]);
        Assert.Empty(SqlServerMigrations.VariablesOf(script));

        // Every entity key and key name fits the table.
        foreach (EntityInfo entity in EntityCatalog.Embedded.Entities)
        {
            Assert.True(entity.Key.Length <= SqlServerRowDatabase.MaxEntityKeyLength, entity.Key);
            Assert.All(entity.StoreKeys, k => Assert.True(k.Name.Length <= StoreKey.MaxNameLength, k.ToString()));
        }
    }

    // 0009: the data location key counts the locations in service (wave 4b, fix round 2). The script removes only the key
    // rows of archived locations, under the key's own entity and name, and changes no engine row.
    [Fact]
    public void TheLocationKeyScriptRemovesOnlyTheKeysOfArchivedLocations()
    {
        SqlMigration migration = SqlServerMigrations.All.Single(m => m.Version == 9);
        string script = Normalize(migration.Text);
        Assert.Equal("0009-location-key-in-service.sql", migration.Name);
        IReadOnlyList<string> batches = SqlServerMigrations.SplitBatches(script);
        Assert.Single(batches);
        StoreKey key = EntityCatalog.Embedded.Get("data-location").StoreKeys.Single();
        Assert.Equal(StoreKeyScope.InServiceRows, key.Scope);
        Assert.Contains("DELETE [k]\nFROM [host].[StoreKey] AS [k]\nJOIN [ops].[DataLocation] AS [r] ON [r].[Id] = [k].[RowId]", batches[0]);
        Assert.Contains("WHERE [k].[Entity] = '" + key.EntityKey + "'", batches[0]);
        Assert.Contains("AND [k].[KeyName] = '" + key.Name + "'", batches[0]);
        Assert.Contains("AND JSON_VALUE([r].[Columns], N'$.statusMeaning') = N'archived';", batches[0]);
        Assert.DoesNotContain("UPDATE", script, StringComparison.Ordinal);
        Assert.DoesNotContain("[ops].[DataLocation] SET", script, StringComparison.Ordinal);
        Assert.Empty(SqlServerMigrations.VariablesOf(script));
    }

    // 0008: the data taxonomy's tables, temporal like the other class E and V tables; the subject check on Owner Assignment
    // that every row written before the data taxonomy passes; and the app's history reads denied on the new tables.
    [Fact]
    public void TheDataTaxonomyScriptAddsItsTablesTheSubjectCheckAndThePolicyAgain()
    {
        SqlMigration migration = SqlServerMigrations.All.Single(m => m.Version == 8);
        string script = Normalize(migration.Text);
        Assert.Equal(SqlServerSchemaScript.DataTaxonomyFile, migration.Name);
        Assert.Empty(SqlServerMigrations.VariablesOf(script));
        foreach (string table in new[] { "[config].[DataClassification]", "[config].[DataDomainLevel]", "[config].[DataDomain]", "[config].[DataPlatform]", "[ops].[DataLocation]", "[ops].[DataMapping]", "[ops].[DataUse]" })
        {
            int start = script.IndexOf("    CREATE TABLE " + table + "\n", StringComparison.Ordinal);
            Assert.True(start > 0, table);
            string create = script.Substring(start, script.IndexOf("END;", start, StringComparison.Ordinal) - start);
            string history = table.Substring(0, table.Length - 1) + "History]";
            Assert.Contains("WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = " + history + "));", create);
            Assert.Contains("PERIOD FOR SYSTEM_TIME", create);
            Assert.DoesNotContain("LEDGER", create);
            Assert.Contains("IF OBJECT_ID(N'" + table + "', N'U') IS NULL", script);
            Assert.Contains("ADD FILTER PREDICATE [security].[fn_HasCaller]([Id]) ON " + history, script);
        }

        // The configuration tables change only inside an activation, as 0002 says for the others.
        Assert.Contains("ADD BLOCK PREDICATE [security].[fn_ConfigWriter]([Id]) ON [config].[DataDomain] AFTER INSERT", script);
        Assert.DoesNotContain("[fn_ConfigWriter]([Id]) ON [ops].[DataUse]", script);

        // The subject check: exactly one subject column holds a value and Subject Kind names it; empty means the application.
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE [name] = N'CK_OwnerAssignment_OneSubject' AND [parent_object_id] = OBJECT_ID(N'[ops].[OwnerAssignment]'))", script);
        Assert.Contains("ALTER TABLE [ops].[OwnerAssignment] WITH CHECK ADD CONSTRAINT [CK_OwnerAssignment_OneSubject] CHECK", script);
        foreach (string member in new[] { "businessApplication", "subjectDataDomain", "subjectDataLocation" })
        {
            Assert.Contains("CASE WHEN NULLIF(JSON_VALUE([Columns], N'$." + member + "'), N'') COLLATE Latin1_General_BIN2 IS NULL THEN 0 ELSE 1 END", script);
        }

        Assert.Contains("(NULLIF(JSON_VALUE([Columns], N'$.subjectKind'), N'') COLLATE Latin1_General_BIN2 IS NULL AND NULLIF(JSON_VALUE([Columns], N'$.businessApplication'), N'') COLLATE Latin1_General_BIN2 IS NOT NULL)", script);
        Assert.Contains("OR (NULLIF(JSON_VALUE([Columns], N'$.subjectKind'), N'') COLLATE Latin1_General_BIN2 = N'data-domain' AND NULLIF(JSON_VALUE([Columns], N'$.subjectDataDomain'), N'') COLLATE Latin1_General_BIN2 IS NOT NULL)", script);
        Assert.DoesNotContain("[ops].[Decision] WITH CHECK ADD CONSTRAINT", script);

        // The new history tables are denied to the app, as 0003 denies the others.
        Assert.Contains("history_table_id", script);
        Assert.Contains("ledger_view_id", script);
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

    // D-38: a person's answer through answer_approval names the record it answers, for that request only; every other
    // context carries no such key, and a seed never does.
    [Fact]
    public void APersonsAnswerRequestNamesTheRecordItAnswers()
    {
        var person = new StoreCallContext("00000000-0000-4000-8000-000000000212", CallerEntry.Person, ActorKindMeaning.Person, "req-10", new[] { "application-owner" }, clientApp: "Web app", clientAppVerified: true);
        Assert.DoesNotContain(SqlServerRowDatabase.SessionValues(person, "write"), v => v.Key == "AnsweredApprovalId");

        StoreCallContext answering = person.WithAnsweredApproval("00000000-0000-4000-8000-000000000701");
        var values = SqlServerRowDatabase.SessionValues(answering, "write").ToDictionary(v => v.Key, v => v.Value);
        Assert.Equal("00000000-0000-4000-8000-000000000701", values["AnsweredApprovalId"]);
        Assert.Equal("person", values["Entry"]);
        Assert.Equal("person", values["ActorKind"]);
        Assert.Equal("application-owner", values["Roles"]);
        Assert.Equal("Web app", values["ClientApp"]);
        Assert.DoesNotContain(SqlServerRowDatabase.SessionValues(answering, "seed"), v => v.Key == "AnsweredApprovalId");
        Assert.Null(person.AnsweredApprovalId);
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
