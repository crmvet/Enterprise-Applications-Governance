# Azure SQL scripts

The schema of the Azure SQL store (`Product.Azure.Stores.SqlServer`). The scripts are compiled into that assembly and
applied in file order by `SqlServerMigrations`, which records each one in `host.SchemaVersion`.

| File | Written | What it does |
|---|---|---|
| `0001-tables.sql` | Generated | One table per first-release entity of the logical model, in the schema of its kind (`config`, `ops`, `audit`), with its history class applied: system-versioned temporal tables, updatable ledger tables for C46, O08, O09 and O10, append-only ledger tables for C02, O14, O20 and O24 (document 22, sections 2.3 and 11.2) |
| `0002-security.sql` | Generated | Row-level security on `SESSION_CONTEXT`: a filter predicate that hides every product row from a connection without a caller, and block predicates that back up the engine's guard (document 22, section 11.3) |
| `0003-app-user.sql` | Generated | The `product_app` role (read and write the engine's rows, no DDL, no delete where the model never deletes, no read of history tables or ledger views), the `product_migrator` role, and the app's managed identity as a database user in `product_app` |
| `0004-host.sql` | By hand | `host.RateLimitCounter`, the Tool Policy call counters, and the app's read of `host.SchemaVersion` |
| `0005-approval-outbox.sql` | By hand | `host.ApprovalOutbox`, the approval runner's work list: one row per Approval Record the engine opens, written in the same transaction as the engine's rows. IDs, states and times only. The app reads, inserts and updates it and never deletes from it |
| `0006-store-keys.sql` | By hand | `host.StoreKey`, the keys the logical model asks the store to enforce beyond the unique indexes of 0001: one row per key value of an engine row, with the entity, the key's name and the value's SHA-256 as the primary key. Hashes and IDs only. The app reads, inserts and deletes its rows and never updates them. Runs only on a database whose engine tables are still empty |
| `0007-record-numbers.sql` | By hand | `host.RecordNumber`, the last record number the engine gave per entity (document 25, B25-9). The store reserves the next number in a transaction of its own: it locks the entity's counter row, reads the highest number with the prefix that the entity's table holds, and moves the counter one above the larger. Entity keys and numbers only. The app reads, inserts and updates it and never deletes from it. Builds nothing from existing rows: the first number counts what the table holds |
| `0008-data-taxonomy.sql` | Generated | The data taxonomy (D-37; document 22, B22-14): the seven tables of C47 to C50 and O25 to O27 as system-versioned temporal tables in the shape of `0001`; `CK_OwnerAssignment_OneSubject`, a check on the row's JSON that exactly one of Business Application, Subject Data Domain and Subject Data Location holds a value and Subject Kind names it (an empty kind means the application, so every existing row passes `WITH CHECK`); the one security policy dropped and created again over every table; and the app's reads of the new history tables denied, as `0003` does for the others. `SqlServerSchemaScript.LaterTables` says which migration creates each table that came after the first release |
| `0009-location-key-in-service.sql` | By hand | The Data Location key (Data Platform and External Id) counts the locations in service: no Retired On, and not archived, as the engine counts them (wave 4b). Removes the key rows of archived locations, so a draft location withdrawn under an earlier build gives its External Id back. Changes no engine row |

## Who runs them

A deployment identity that owns the database: the server's Microsoft Entra administrator, which maps to `dbo`. The
local server's `migrate` command calls `SqlServerMigrations.Apply` with `Authentication=Active Directory Default`, and the
Azure Developer CLI template's post-provision hook runs that command as the person who deployed. Another deployment
identity needs `product_migrator` membership and DDL rights from a person.

The app's managed identity never runs DDL. At startup `SqlServerProductStore.EnsureSchema` only checks that
`host.SchemaVersion` has reached the version the build needs and stops the host when it has not.

`0003-app-user.sql` takes one variable in the sqlcmd form: `$(AppIdentityName)`, the managed identity's name as
Microsoft Entra ID shows it. An empty value skips the user, for a test server without Microsoft Entra ID.

Every statement checks before it acts, so a script that stops part way can run again from the start. A recorded
script never runs again.

## Row shape

Each table keeps the engine's rows in the shape of its storage ports: `Id` (the row ID, binary collation), `Key` (the
configuration key path, null for operational rows), `Columns` (the row's columns as canonical JSON in logical names) and
`Version` (the optimistic version). Beside them are the performer of the last write and a few promoted columns the
store copies from the JSON in the same statement: the two unique keys (Audit Entry Request Key, Deployment Record
Idempotency Key), the Configuration Set Version pair, the version probe, and the columns the block predicates read.

Audit Entry Request Key is unique on its own, which is stricter than the model's Operation and Request Key pair and is
what the engine's replay lookup reads.

## Store keys

The other keys the logical model asks the store to enforce are rows of `host.StoreKey` (`0006-store-keys.sql`): business
keys such as Record Number, Claim Key and Entra Object Id, the configuration keys within the set in force, and alternate
keys such as Link Kind and External Id. The store computes each key's value from the row it writes, takes its SHA-256,
and writes or removes the key row in the same transaction as the engine's row, so the primary key refuses a second row
with the same value in any transaction. The store answers that refusal as a conflict, and the engine retries the call
once, so a concurrent duplicate sees the first result (document 25, section 8). The SQLite store keeps the same table.

- A row holds no value for a key when a part of it is empty, as a unique index ignores nulls and a Dataverse alternate
  key ignores empty values: a document link without a requirement, a portfolio without a parent.
- A live configuration row's key is its key path, unique in its table, retired rows included. Configuration Set is not
  a part: the live tables hold the rows of the working set in force only. A List Value's key path holds its list's key,
  so List Value Key is held as Value List and Key: the model's Key within the set would refuse every shipped preset,
  which uses one value key (such as `other` or `medium`) in several lists.
- The alternate keys of the configuration entities (tier rank, stage rank, a binding's role, group and method, and
  others) count active rows only, as the engine's own checks of tier and stage ranks do: a retired row keeps its values
  and an active row may take them again.
- Technical Component Link Kind and External Id counts components with no Retired On, as the engine's key of the entity
  does, so a resource registered again after its component retired is a new component.
- Data Location Data Platform and External Id counts locations in service: no Retired On, and a status meaning other
  than archived, as the engine counts them. A draft location that is withdrawn is archived with no Retired On, so it
  gives its External Id back for a location added again (`0009-location-key-in-service.sql` removes the key rows that
  an earlier build kept). Data Mapping Data Domain, Data Location and Data Origin counts mappings with no Ended On.
- A string that is a GUID compares without regard to case, as the engine compares Microsoft IDs; other text compares
  exactly, trailing spaces included.
- Within one commit the store removes every value it gives up before it adds any, so values that move between rows (two
  tiers that swap ranks) never collide with themselves.

Three store keys of the model are held back, because a documented engine behavior writes rows they would refuse:
Configuration Set Version Content Hash (a version that returns to earlier content has its hash), Artifact Version
Artifact Name and Version (the record step looks an artifact up by application as well), and Review Business
Application, Review Type and Due On (two changes to one application finished on the same day open two post-change
reviews). `StoreKeyCatalog.HeldBack` gives each reason, and the store tests list every key of the model
(`LogicalModelConformanceTests`), so a key the model adds needs a decision.

`0006-store-keys.sql` builds no key for rows a database already holds, so it stops with error 50006 on a database whose
engine tables hold rows; a database created from these scripts is empty until its first seed or bootstrap. A SQLite file
from an earlier build gets its keys from its rows on first use, and is refused with the key named when two of its rows
share a value.

The fully typed schema in `model/generated/azure-sql-0001-initial.draft.sql` stays the target of a later migration, once
its open questions (circular required foreign keys at the bootstrap, constraints on ledger tables) are settled.

## The approval outbox

`host.ApprovalOutbox` is not an engine table and is not in the entity catalog. The store writes a pending row when an
engine commit inserts a pending Approval Record, and settles the row (answered, expired or cancelled) when a commit
writes the record's final outcome, in the same transaction as the engine's rows, so the two never disagree after a
commit or a rollback. The approval runner's routes in the host (`/engine/v1/approval-runner/...`) claim rows with a
lease, mark them sent and give them back, each as a compare and set on `RowVersion`. A row holds IDs, states and times
only, never a name or text a person wrote. The SQLite store keeps the same table for local runs and tests.

## Session context

The store sets these keys on every connection it opens, read-only for the connection's life: `PrincipalId`, `Entry`
(`person`, `ai-client`, `workload` or `setup`), `ActorKind`, `OnBehalfOfId`, `ClientApp`, `ClientAppVerified`, `Roles`,
`CorrelationId` and `EngineOperation` (`read`, `write`, `activation` or `seed`). The host enters the caller for each
request (`StoreCallContext.Enter`) before the engine runs; a connection without a caller sees no product row, and an
engine write without one is refused before it reaches the database.

## Changing a generated script

Change the logical model, rebuild, and run the store tests with `PRODUCT_WRITE_SQL=1`
(`tests/Product.Azure.Stores.Tests`), then review the difference. The tests fail while a generated file differs from
its generator. A change to a table that already exists in a database needs a new numbered script: ledger tables accept
only new nullable columns, and the security policies are schema-bound, so a new script drops and recreates them as
`0002-security.sql` does.

## Not verified on a live database yet

No Azure SQL database was available when these scripts were written. The store tests check the scripts' text, and the
same store logic runs against SQLite. To run the store suite and the migrations against a real database, set
`PRODUCT_TEST_SQLSERVER` to the connection string of a new, empty Azure SQL Database (or SQL Server 2022) database whose
identity owns it, and optionally `PRODUCT_TEST_SQLSERVER_APP_IDENTITY`. Use a new database for each run: ledger rows can
never be deleted. Points to confirm there: filtered unique indexes and `ISJSON` checks on ledger tables, block
predicates on ledger tables, filter predicates on temporal history tables, the cost of the row-level security
predicates per request, the cost of the store key rows on a bootstrap, and the error numbers a failover returns to the
store's transient retries (`SqlTransientRetry`).
