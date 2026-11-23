-- Azure SQL migration 0006: the store keys.
-- Written by hand. Every statement checks before it acts, so the script can run again after a stop part way.
--
-- host.StoreKey: the keys the logical model asks the store to enforce beyond the unique indexes of 0001 (business keys
-- such as Record Number, Claim Key and Entra Object Id, the configuration keys within the set in force, and alternate
-- keys such as Link Kind and External Id). One row per key value of an engine row: the entity, the key's name, the
-- SHA-256 of the key's value and the row's ID, with the first three as the primary key, so the database refuses a
-- second row with the same value in any transaction. The store writes and removes these rows in the same transaction as
-- the engine's rows (Product.Azure.Stores, StoreKeys.cs). The table holds hashes and IDs only, never a name or other
-- text a person wrote, so it needs no erasure step and no row-level security. The app identity reads, inserts and
-- deletes its rows and never updates them.
--
-- The store keys of the rows a database already holds are built by the store as it writes them, so this script runs
-- only on a database whose engine tables are still empty, which every database created from these scripts is until the
-- first seed or bootstrap. On a database that holds rows it stops with error 50006 before it changes anything.

IF OBJECT_ID(N'[host].[StoreKey]', N'U') IS NULL
    AND EXISTS (
        SELECT 1
        FROM sys.partitions AS p
        JOIN sys.tables AS t ON t.[object_id] = p.[object_id]
        WHERE SCHEMA_NAME(t.[schema_id]) IN (N'config', N'ops', N'audit') AND p.[index_id] IN (0, 1) AND p.[rows] > 0)
BEGIN
    THROW 50006, N'Migration 0006 adds the store keys and needs empty engine tables, but this database already holds engine rows. Apply the migrations to a new database and seed it again.', 1;
END;
GO

IF OBJECT_ID(N'[host].[StoreKey]', N'U') IS NULL
    CREATE TABLE [host].[StoreKey]
    (
        [Entity] varchar(64) COLLATE Latin1_General_BIN2 NOT NULL,
        [KeyName] varchar(128) COLLATE Latin1_General_BIN2 NOT NULL,
        [ValueHash] binary(32) NOT NULL,
        [RowId] varchar(36) COLLATE Latin1_General_BIN2 NOT NULL,
        CONSTRAINT [PK_StoreKey] PRIMARY KEY ([Entity], [KeyName], [ValueHash])
    );
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_StoreKey_Row' AND [object_id] = OBJECT_ID(N'[host].[StoreKey]'))
    CREATE INDEX [IX_StoreKey_Row] ON [host].[StoreKey] ([Entity], [RowId]) INCLUDE ([KeyName]);
GO

GRANT SELECT, INSERT, DELETE ON [host].[StoreKey] TO [product_app];
DENY UPDATE ON [host].[StoreKey] TO [product_app];
GO
