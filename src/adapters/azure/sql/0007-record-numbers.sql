-- Azure SQL migration 0007: the record number counters.
-- Written by hand. Every statement checks before it acts, so the script can run again after a stop part way.
--
-- host.RecordNumber: the last record number the engine gave per entity (document 25, B25-9; Product.Engine.Core,
-- RecordNumbers). The store reserves the next number in a transaction of its own: it locks the entity's counter row,
-- reads the highest number with the entity's prefix that the entity's table holds, and moves the counter one above the
-- larger of the two before it lets the lock go. So two calls at the same moment never share a number, a number is never
-- given twice, and the numbers go on after the rows a seed or an earlier build wrote. A call that does not commit leaves
-- a gap, as a sequence does. Entity keys and numbers only. The app identity reads, inserts and updates its rows and never
-- deletes them.
--
-- The table starts empty on a database of any age: the first number an entity takes counts the rows its table already
-- holds, so nothing needs to be built here from existing rows.

IF OBJECT_ID(N'[host].[RecordNumber]', N'U') IS NULL
    CREATE TABLE [host].[RecordNumber]
    (
        [Entity] varchar(64) COLLATE Latin1_General_BIN2 NOT NULL CONSTRAINT [PK_RecordNumber] PRIMARY KEY,
        [LastNumber] bigint NOT NULL CONSTRAINT [CK_RecordNumber_LastNumber] CHECK ([LastNumber] > 0)
    );
GO

GRANT SELECT, INSERT, UPDATE ON [host].[RecordNumber] TO [product_app];
DENY DELETE ON [host].[RecordNumber] TO [product_app];
GO
