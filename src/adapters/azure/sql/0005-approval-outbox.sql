-- Azure SQL migration 0005: the approval outbox.
-- Written by hand. Every statement checks before it acts, so the script can run again after a stop part way.
--
-- host.ApprovalOutbox: one row per Approval Record the engine opens, so the approval runner can pull the work and send a
-- card (document 23, S1, step 4). The store writes a new row when an engine commit inserts a pending Approval Record and
-- settles the row when a commit writes the record's final outcome, in the same transaction as the engine's rows. The
-- approval runner's routes claim rows with a lease, mark them sent and give them back, each as a compare and set on
-- RowVersion. The row holds IDs, states and times only: no name, no statement and no other text a person wrote, so the
-- table needs no erasure step and no row-level security. The app identity reads, inserts and updates it, and never
-- deletes from it.

IF OBJECT_ID(N'[host].[ApprovalOutbox]', N'U') IS NULL
    CREATE TABLE [host].[ApprovalOutbox]
    (
        [ApprovalRecordId] varchar(36) COLLATE Latin1_General_BIN2 NOT NULL CONSTRAINT [PK_ApprovalOutbox] PRIMARY KEY,
        [DecisionId] varchar(36) COLLATE Latin1_General_BIN2 NOT NULL,
        [EnqueuedOn] datetime2(7) NOT NULL,
        [State] varchar(16) COLLATE Latin1_General_BIN2 NOT NULL
            CONSTRAINT [CK_ApprovalOutbox_State] CHECK ([State] IN ('pending', 'claimed', 'sent', 'answered', 'expired', 'cancelled', 'failed')),
        [ClaimId] varchar(64) COLLATE Latin1_General_BIN2 NULL,
        [ClaimedUntil] datetime2(7) NULL,
        [Attempts] int NOT NULL CONSTRAINT [DF_ApprovalOutbox_Attempts] DEFAULT (0),
        [CardRef] nvarchar(400) NULL,
        [RunnerRunUrl] nvarchar(2048) NULL,
        [UpdatedOn] datetime2(7) NOT NULL,
        [RowVersion] bigint NOT NULL CONSTRAINT [DF_ApprovalOutbox_RowVersion] DEFAULT (1)
    );
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_ApprovalOutbox_State' AND [object_id] = OBJECT_ID(N'[host].[ApprovalOutbox]'))
    CREATE INDEX [IX_ApprovalOutbox_State] ON [host].[ApprovalOutbox] ([State], [EnqueuedOn]) INCLUDE ([ClaimedUntil]);
GO

GRANT SELECT, INSERT, UPDATE ON [host].[ApprovalOutbox] TO [product_app];
DENY DELETE ON [host].[ApprovalOutbox] TO [product_app];
GO
