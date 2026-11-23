-- Azure SQL migration 0004: tables the hosts use outside the engine.
-- Written by hand. Every statement checks before it acts, so the script can run again after a stop part way.
--
-- host.RateLimitCounter: the Tool Policy call counters (Calls Per Person Per Hour), kept in the database and never in a
-- server's memory (document 24, section 2.3). The store increments a counter with one locked UPDATE, or an INSERT for the
-- first call, in one transaction; a counter of an earlier window starts again at one.
-- host.SchemaVersion: created by the migration runner before the first script; the app reads it at startup to check that
-- the migrations it needs are applied.

IF OBJECT_ID(N'[host].[RateLimitCounter]', N'U') IS NULL
    CREATE TABLE [host].[RateLimitCounter]
    (
        [CounterKey] nvarchar(400) COLLATE Latin1_General_BIN2 NOT NULL CONSTRAINT [PK_RateLimitCounter] PRIMARY KEY,
        [WindowStart] datetime2(7) NOT NULL,
        [Count] bigint NOT NULL
    );
GO

GRANT SELECT, INSERT, UPDATE ON [host].[RateLimitCounter] TO [product_app];
GRANT SELECT ON [host].[SchemaVersion] TO [product_app];
GO
