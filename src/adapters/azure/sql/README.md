# Azure SQL scripts

The schema of the Azure SQL store (`Product.Azure.Stores.SqlServer`). The scripts are compiled into that assembly and
applied in file order by `SqlServerMigrations`, which records each one in `host.SchemaVersion`.

| File | Written | What it does |
|---|---|---|
| `0001-tables.sql` | Generated | One table per first-release entity of the logical model, in the schema of its kind (`config`, `ops`, `audit`), with its history class applied: system-versioned temporal tables, updatable ledger tables for C46, O08, O09 and O10, append-only ledger tables for C02, O14, O20 and O24 (document 22, sections 2.3 and 11.2) |
| `0002-security.sql` | Generated | Row-level security on `SESSION_CONTEXT`: a filter predicate that hides every product row from a connection without a caller, and block predicates that back up the engine's guard (document 22, section 11.3) |
| `0003-app-user.sql` | Generated | The `product_app` role (read and write the engine's rows, no DDL, no delete where the model never deletes, no read of history tables or ledger views), the `product_migrator` role, and the app's managed identity as a database user in `product_app` |
| `0004-host.sql` | By hand | `host.RateLimitCounter`, the Tool Policy call counters, and the app's read of `host.SchemaVersion` |

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

The other keys the logical model asks the store to enforce (business keys such as Record Number, Claim Key and Entra
Object Id, the configuration keys within a set, and alternate keys such as Content Hash) are not unique indexes here,
as the in-memory store does not enforce them either: the engine checks them from its facts, so two concurrent writes
that each pass that check can both commit. Audit Entry Request Key is unique on its own, which is stricter than the
model's Operation and Request Key pair and is what the engine's replay lookup reads. The store tests list every such
key (`LogicalModelConformanceTests`), so a key the model adds needs a decision.

The fully typed schema in `model/generated/azure-sql-0001-initial.draft.sql` stays the target of a later migration, once
its open questions (circular required foreign keys at the bootstrap, constraints on ledger tables) are settled.

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
predicates on ledger tables, filter predicates on temporal history tables, and the cost of the row-level security
predicates per request.
