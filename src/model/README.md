# model

The logical data model of document 22 as one JSON file, and the generators that read it.

## Files

| File | What |
|---|---|
| `logical-model.json` | Every entity, column, key, relationship, history class and delete policy. The source of truth for generators, and for the engine: `Product.Engine.Core` compiles it in as a resource, so the write guard (OP-08) takes each entity's history class, column list and fill-once columns from it |
| `logical-model.schema.json` | JSON Schema (draft 2020-12) for the model file |
| `check-model.mjs` | Validates the model against the schema, then runs the cross-checks below |

## Run the check

```
cd src
node model/check-model.mjs
```

It uses the pinned ajv in `tools/node_modules`, so run `npm ci --ignore-scripts` in `tools/` first. `node tools/ci.mjs` does that and runs this check as the `model` step.

Exit code: 0 clean, 1 problems, 2 setup error (ajv missing, a file does not parse, the schema does not compile).

## What the check proves

- The model validates against the schema.
- Every relationship target exists. Every reference column has exactly one relationship, and the relationship matches it.
- Every CFG reference points to a configuration entity. OPS points to an operational entity. PR points to O05.
- Every key list in a JSON column marked CFG (`keyOf`) names configuration entities.
- No duplicate names: entity IDs, entity names, columns per entity (with its column sets), relationship IDs, contracts, value lists.
- Keys, tool IDs, constraints and subjects name columns that exist.
- Each operational status column has its meaning copy (document 22, section 2.1.4).
- One cascading parent at most per child. Self references are Restrict. Class A entities are never deleted.
- The `counts` block matches the entities.

## What is in the model

- 75 entities. 65 come from document 22 for the first release: 41 configuration and 24 operational.
- C40 Separation Rule is adopted as a working default. Its columns come from documents 20 and 21.
- C41 Tool Policy (document 24) and C43 Column Protection Rule, C44 Portfolio Access and C45 Known Client (document 26) are proposals, marked `"status": "proposal"`.
- L01 to L05 are later entities, marked `"status": "later"`.
- C03 Preset has no table. It is stored as a C01 row (`"storedIn": "C01"`).
- Column proposals from documents 21 and 26 that document 22 does not list carry `"status": "proposal"` and a `source`.
- `openPoints` lists where the design documents disagree and which one the model follows.

## How the file reads

- Names are the logical names of document 20. Physical names come from a generator, for example `<prefix>_BusinessApplication`.
- `required`: Y, N or C (conditional). `ref`: CFG, OPS or PR, as in document 22, section 1.2.
- Column sets (`common`, `operational`, `taxonomy`) hold the columns many entities share. An entity lists the sets it uses.
- Relationship IDs follow the catalog of document 22, section 6. A catalog row that covers several columns gets a letter per column, such as `R14a`. `RX` IDs are relationships the catalog does not list.
- Cardinality is written parent to child. `Restrict` is the default delete behavior. Only R80 cascades.

## Working defaults

Places built on one of the six open questions (document 28, section 3) carry a `workingDefault` text that starts with
`WORKING DEFAULT (open design question N)`. Search for it to find every place an answer would change. The check prints how many there are.

## Generators

Status of every output: **draft, platform under review (D-22)**. The generators write text files only. Nothing is
applied to any database or environment, and nothing here opens a connection. No packages, Node 20 or later.

```
cd src
node model/generate.mjs names                 both physical names for every entity and column (stdout)
node model/generate.mjs names --entity O09    one entity
node model/generate.mjs write                 the three files below, into model/generated/
node model/check-generated.mjs                the determinism check (tools/ci.mjs runs it as the step "generated")
```

| File in `generated/` | What | Binding |
|---|---|---|
| `column-map.json` | Every logical entity and column name with its Dataverse schema and logical names and its Azure SQL name and type. One column per line. What the adapters map logical names with (document 25, sections 3.2 and 11.1) | Both |
| `azure-sql-0001-initial.draft.sql` | The first migration: schemas, `CREATE TABLE` per entity with primary key, unique constraints, check constraints (key format, engine meanings, exactly one subject), the history class as a temporal, updatable ledger or append-only ledger table, then the foreign keys and the view `ops.DecisionCurrent` | Document 22, section 11 |
| `dataverse-metadata.draft.json` | Tables, ownership, columns with types, choices versus lookups, the global choices, alternate keys, relationships with their delete behavior, autonumber columns, and what the engine checks instead | Document 22, section 10 |

| Generator file | What |
|---|---|
| `generate.mjs` | The command line |
| `generators/physical.mjs` | The binding rules both platforms share: names (document 20, section 3), types (document 22, section 1.1), history classes (sections 2.3 and 11.2), keys, ownership, choices, and the checks that stop generation |
| `generators/names.mjs`, `column-map.mjs`, `azure-sql.mjs`, `dataverse.mjs` | One renderer per output |
| `generators/outputs.mjs` | The list of outputs, shared by the command line and the check |
| `check-generated.mjs` | Runs `write` twice and `names` twice as separate processes, compares the bytes, compares `generated/` with a fresh run (line endings aside, because git may check out CRLF), and checks the draft marker, ASCII text and the working default markers |

What the generators decide, and where:
- Proposals (C41, C43 to C45) get tables, marked as proposals. Later entities (L01 to L05) get names in the column map and
  the printout, but no table. C03 Preset is rows of C01.
- A derived column that is never stored (C02 Version Status) has no physical column. O09 Superseded By is stored on
  Dataverse and computed in the view `ops.DecisionCurrent` on Azure (document 22, section 8.1).
- Class F maps per entity on Azure (document 22, section 11.2). A new class F entity with no mapping stops the generator.
- A key with an optional column is a filtered unique index on Azure, so both bindings refuse the same rows (a Dataverse
  alternate key ignores rows with an empty key column). The engine checks the rest.
- Every status contract shares one global choice, status meaning, on Dataverse (document 22, section 10.1).
- The generator stops (exit 1) on a reference with no relationship, a physical name used twice in one table, an Azure
  name over 128 characters, or a constraint name used twice.

Review points the outputs carry (each file lists its own):
- C01, C02 and O05 need each other through NOT NULL references. SQL Server has no deferred constraints, so the
  bootstrap of version 1 cannot insert their first rows. Open until the migration runner is built; the model is unchanged.
- Ledger features this draft relies on (foreign keys, checks, filtered indexes, `json` columns, `ALTER TABLE ADD
  CONSTRAINT`) are UNVERIFIED here. They are still to be checked on Azure SQL.
- C46 is an updatable ledger table, as document 22 says. Documents 20 and 25 call it a plain table.
- Dataverse: alternate keys on O01 and O19 that document 22, section 10.2 does not list; an autonumber on C46; option
  values wait for the publisher.

Not generated yet: the plug-in step list, row-level security and permissions, security roles.

The working default for question 1 (fixed engine meanings) is stated in each output, because every meaning column rests
on it. Entity and column markers from the model (questions 4, 7, 8, 9 and 12) are copied where those columns appear.

## Changing the model

Edit `logical-model.json` by hand, then run the check. Update `counts` when you add or remove an entity. Then run
`node model/generate.mjs write`, or the `generated` step of `tools/ci.mjs` fails. Rebuild the engine too: a write that names a column the model's entity does not hold is invalid input (`input.unknown-column`), and the history class decides what the guard freezes.
