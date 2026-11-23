# src: the product's source

This folder holds the product's code. Everything here builds and tests offline, with no sign-in and no tenant.
For what the product is designed to do, start with `docs/overview.md` at the repository root.

## What is here

| Path | What |
|---|---|
| `Product.sln` | The solution with every .NET project |
| `Directory.Build.props` | Language version, nullable, warnings as errors, target lists, net48 reference assemblies |
| `Directory.Build.targets` | Keeps test runs offline: removes the test platform telemetry hook |
| `Directory.Packages.props` | Central, exact package versions |
| `global.json` | .NET 10 SDK, tests on Microsoft.Testing.Platform |
| `config/schema/` | Configuration JSON Schemas, draft 2020-12 |
| `config/presets/` | Presets as per-entity files |
| `config/build/` | Built bundles. Generated, ignored by git |
| `model/` | Logical model JSON and generators |
| `engine/Product.Engine.Core/` | Pure engine core: contract records, the reason code catalog, the snapshot model, canonical hashing, and the operations OP-01 to OP-08 and OP-10 to OP-14 (`Operations/`, with `OperationJson`, the one wire path for every caller; OP-08 is the write guard, which takes each entity's history class and column list from the logical model compiled in as a resource). net10.0, net48, netstandard2.0. Base class library only |
| `engine/Product.Engine.Configuration/` | Preset and patch readers, wildcard expansion, the snapshot compiler, the diff, draft rebase, activation and the bootstrap of version 1 (`Activation/`), and the OP-09 validator (V01 to V44 plus the engine checks of document 25, section 5.11). Same three targets |
| `engine/Product.Engine.Runtime/` | The engine runtime: plan, fetch, evaluate; the storage ports; the snapshot cache with the version probe; roles resolved from the configuration in force; every write through OP-08 with its effects in one transaction (activation included). No store of its own: the in-memory store adapter lives in `Product.Engine.Testing`, and a dependency test keeps test doubles out. `Compose` runs one product operation (one MCP tool) as one engine call: one snapshot, one actor, one transaction, validateOnly as a dry run. Same three targets |
| `engine/Product.Engine.Testing/` | Fixture runner (OP-01 to OP-14, rebase, activate, bootstrap); the design invariants of document 25, section 10 (`Invariants/`, INV-01 to INV-11, with the selection generator); the replay tool of section 9 (`Replay/`); and `InMemory/InMemoryStore` with `ManualClock`, the in-memory store adapter for tests and the local harness. Only test projects and the command line reference it. net10.0 and net48 |
| `engine/Product.Engine.Cli/` | Command line: validate, build presets, run fixtures, replay recorded calls and capture them. net10.0 |
| `engine/tests/` | Engine tests. net10.0 and net48 |
| `fixtures/` | One JSON file per case: `fixtures/<operation>/<case>.json` (`op-01` to `op-14`, `rebase`, `activate`, `bootstrap`). `fixtures/presets/` holds fictional presets that only fixtures use, and `fixtures/replay/` the replay records |
| `mcp/contracts/` | Tool schemas, envelope, error codes, Tool Policy seed |
| `mcp/Product.Mcp.Core/` | MCP server core, independent of store and host: the contracts (embedded), the tool registry, the envelope and the 11 fixed error codes, Tool Policy (proposed C41) with its rate limit counted in the store port, `X-Toolsets`, the Known Client check (proposed C45), the R1 resources, input validation, the token validation interface, telemetry without tokens or typed text (the caller hash salt is a host setting with no default; without it no caller field is sent), the backend port, and the in-process backend (the Azure shape) with all 50 R1 tools. The MCP library sits behind `IMcpLibraryAdapter` (a pass-through stub until the library is picked). net10.0 |
| `mcp/Product.Mcp.Testing/` | The stand-ins for tests and the local harness: the mock identity (`MockTokenValidator`), the in-memory rate limit counter and the repeatable GUID wrapper over the in-memory store. No host references it, and a dependency test proves the core holds no test double. net10.0 |
| `mcp/tests/` | MCP contract tests through the whole core on the in-memory store: every R1 tool and resource, every fixed error code, policy, identity, telemetry. net10.0 |
| `adapters/` | Placeholder for the platform store adapters. Empty in this version |
| `ui/` | The four page designs (Overview, Application 360, dependency map, timeline): `design/pages.md`, the React and Fluent UI v9 harness in `harness/` with fictional fixtures, and `screenshots/`. See `ui/README.md` |
| `content/` | The playbook (23 chapters, 0 to 22, the processes P01 to P11 and the diagrams) and the guidelines G1 to G24 as Markdown |
| `docs/` | Setup and admin guide drafts. See `docs/README.md` |
| `samples/pipeline-steps/` | Copy-paste record step samples (Azure DevOps, GitHub Actions, a Power Platform Pipelines extension flow) against the record endpoint contract, a local stub that validates each request, and the Node test. See `samples/pipeline-steps/README.md` |
| `demo/walkthrough/` | The scripted walkthrough: eight steps through the MCP server core with fictional people, its transcript (`transcript.json`) and a renderer that turns the transcript into an HTML page. The runner is a test, `mcp/tests/Product.Mcp.Core.Tests/Walkthrough/`, so it runs in the `test` check. See `demo/walkthrough/README.md` |
| `tools/` | Checks and scripts (see below) |
| `THIRD-PARTY.md` | Every package, version and license |

## How to run the checks

You need the .NET 10 SDK and Node 20 or later. No sign-in and no tenant.

```
cd src
node tools/ci.mjs --no-scrub
```

It runs these checks in order and prints a summary. The exit code is 1 if any check fails.

| Check | What it does |
|---|---|
| packages | `npm ci --ignore-scripts` in `tools/` when the Node packages are missing or stale |
| restore | `dotnet restore Product.sln` |
| build | `dotnet build Product.sln -c Release` for net10.0, net48 and netstandard2.0 |
| offline | Fails if any test project registers the test platform telemetry extension |
| test | `dotnet test --solution Product.sln -c Release` on net10.0 and net48 |
| presets | The engine command line builds every full preset into `config/build/` and runs OP-09 on each, alone and with each allowed overlay combination |
| fixtures | The engine command line runs every fixture under `fixtures/` and reports pass or fail per fixture |
| replay | The engine command line replays every record under `fixtures/replay/` against its snapshot version. Each record must show exactly the differences it expects, and none for a true record |
| dash | `tools/dash-check.mjs`: no U+2013 and no U+2014 in any text file |
| scrub | A content scan the maintainers run outside this repository. Pass `--no-scrub` to skip it here |
| json | `tools/json-check.mjs`: every JSON file parses, every `*.schema.json` compiles, mapped files validate, schema examples validate |
| model | `model/check-model.mjs`: the logical model validates and its cross-checks hold |
| content | `tools/content-check.mjs`: every guideline and process a chapter cites exists (and no retired guideline is cited), chapter headers match their text, the indexes and cross-references agree, the guideline files match the Balanced guideline rows, links resolve, every image has alt text and is shown by a page, and the content files carry no internal markers |
| ui | `ui/harness/scripts/check-packages.mjs`: the page harness pins exact versions, takes packages from the npm registry only, keeps to the license list, and `THIRD-PARTY.md` lists every package of its lock file. Reads files only; building the harness is a separate step (`ui/README.md`) |
| samples | `samples/pipeline-steps/test/record-steps.test.mjs`: runs the record step samples against a local stub on 127.0.0.1 that validates every request against the record endpoint contract; checks fail soft, retries, idempotency, both URL shapes and that no secret is sent |
| generated | `model/check-generated.mjs`: two runs of the model generators give the same bytes, and `model/generated/` matches a fresh run. The outputs are drafts. Nothing is applied to any database or environment |

Options: `--skip dash,scrub` skips checks by name. `--configuration Debug` changes the build configuration.

## The engine command line

`engine/Product.Engine.Cli` reads files only. No sign-in, no tenant, no network. After a build, from `src/`:

```
dotnet engine/Product.Engine.Cli/bin/Release/net10.0/Product.Engine.Cli.dll <command>
```

| Command | What it does |
|---|---|
| `validate <preset-key or bundle.json> [--overlay <key>]... [--patch <file>]... [--mode preset-build or draft or activation] [--version <n>] [--as-of <yyyy-MM-dd>] [--out <file>] [--json]` | OP-09 over a preset with overlays and patches, or over a bundle file. A patch may be a draft patch or a client binding patch. With a patch the mode is draft, so the result is a working set snapshot |
| `build-presets [--out <dir>] [--check]` | Builds every full preset to `config/build/<preset-key>.bundle.json`, byte-stable, and validates each full preset alone and with each allowed overlay combination. `--check` writes nothing |
| `run-fixtures <folder> [--filter <text>]` | Runs every fixture file under the folder. Prints PASS, FAIL or SKIP per fixture. An operation that is not built is skipped (every operation is built now) |
| `replay <record.json or folder> [--snapshot <bundle.json>] [--version <n>] [--ignore-messages] [--json]` | Replays each recorded call (document 25, section 9). It loads the record's snapshot version, or the bundle given, evaluates the recorded request again and reports every difference from the recorded result or plan |
| `capture <record.json> [--out <file>] [--snapshot <bundle.json>] [--version <n>]` | Evaluates the request of a replay record and writes the record with its recorded result |
| `rules` | Lists the validation rules (V01 to V44) and the engine checks this engine runs |

Exit codes: 0 clean, 1 validation errors, failed fixtures or replay differences, 2 bad usage or unreadable input.
`--root <src>` and `--presets <dir>` work with every command.

Each tool also runs alone, for example `node tools/dash-check.mjs` or `node tools/json-check.mjs`.

Notes:
- The file checks skip `bin/`, `obj/` and `dist/` (`tools/lib/files.mjs`). Third-party DLLs in `bin/` hold words
  that a content scan can match, for example localized resource strings, so scans cover source files only.
- `tools/schema-map.json` says which files validate against which schema. Add an entry when you add a schema or data files.
  An entry whose schema is not written yet shows as pending.
- Keys that start with `x-` in a schema are allowed as annotations, for example a proposal marker.
- `ci.mjs` turns off .NET telemetry for its child processes. When you run `dotnet` by hand, set `DOTNET_CLI_TELEMETRY_OPTOUT=1`.

## Placeholders

The product has no final name yet (D-05).
- `Product.` is the placeholder in names, namespaces, folders and files, for example `Product.Engine.Core`.
- `<prefix>` is the placeholder for the Dataverse publisher prefix.

Use only these two forms. Then one run of `tools/rename-product.mjs` swaps them:

```
node tools/rename-product.mjs --name <PascalName> --prefix <prefix>           (dry run, prints the changes)
node tools/rename-product.mjs --name <PascalName> --prefix <prefix> --apply   (writes)
```

After `--apply`, delete `bin/` and `obj/`, then run `node tools/ci.mjs --no-scrub`.

## Working defaults

Some design questions are still open (see [Open design questions](../docs/architecture/README.md#open-design-questions)).
Code built on the recommended option carries this comment: `WORKING DEFAULT (open design question N)`. Search for it
to find every place an answer would change.

## Rules for this folder

- No real company or client names, no tenant host names, no tenant IDs or GUIDs from real environments, no real people
  in data. Fixtures and samples use fictional data only.
- Never commit a client binding patch or any tenant value.
- No em dashes or en dashes anywhere.
- Packages: well-known, exact version, MIT, Apache 2.0 or BSD, recorded in `THIRD-PARTY.md`.
