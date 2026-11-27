# Testing

Every check runs offline: no sign-in, no tenant, no subscription. Packages come from the public nuget.org and npmjs
feeds only.

## The full check run

From `src/`, with the .NET 10 SDK and Node.js 22 or later:

```
node tools/ci.mjs --no-scrub
```

It runs every check in order, prints a summary, and ends with `CI: all checks passed.` or the number that failed; the
exit code is 1 when any failed. `--skip <name,name>` skips checks by name, and `--configuration Debug` changes the
build configuration. `--no-scrub` skips the one check whose script is not part of the source (a content scan the
maintainers run outside the repository).

| Check | What it does |
|---|---|
| `packages` | `npm ci --ignore-scripts` in `tools/` when its packages are missing or stale |
| `restore`, `build` | `dotnet restore` and `dotnet build` of `Product.sln` (net10.0, net48 and netstandard2.0) |
| `offline` | Fails when a test project registers the test platform's telemetry extension |
| `test` | The engine and MCP core test suites. On Windows both net10.0 and net48; elsewhere net10.0 only |
| `azure` | Builds `adapters/azure/Product.Azure.sln` and runs its two test projects: the host tests and the store tests |
| `presets` | Builds every full preset and validates it alone and with each allowed overlay combination |
| `fixtures` | Runs every engine fixture under `fixtures/`: one JSON file per case, each with the outcome it expects |
| `replay` | Replays every recorded engine call under `fixtures/replay/` against its configuration version |
| `dash` | No en dash and no em dash in any text file |
| `scrub` | The maintainers' content scan; skipped with `--no-scrub` |
| `json` | Every JSON file parses, every schema compiles, mapped files and schema examples validate |
| `model` | The logical model validates and its cross-checks hold |
| `content` | The playbook and guidelines: citations, indexes, links, images, no internal markers |
| `ui` | The web app's packages: exact versions, registry sources, licenses, and `THIRD-PARTY.md` against its lock file |
| `samples` | The pipeline record step samples against a local stub that validates every request |
| `kit` | The demo kit's own tests (`node --test demo/kit/test/kit.test.mjs`), offline, against a fake server on 127.0.0.1 |
| `e2e` | The end-to-end suite without a browser: its packages by the `ui` check's rules (`check-packages.mjs --package tests/e2e`), its specs against the presenter scripts (`tests/e2e/tools/check-suite.mjs`: one test per step, same IDs, titles and order), the counts the scripts' proofs name (`tests/e2e/tools/check-scripts.mjs`: each one checked by its step's test), and every side sheet of the web app (`tests/e2e/tools/check-actions.mjs`: each sheet, keyed by its file and component, is performed by a step's test and listed in the catalog's table of side sheets; each write tool in `WRITE_TOOLS` has a sheet) |
| `docs` | `node tools/docs-gen.mjs --check`: the generated parts of these pages match the contracts, and every link in `docs/` resolves inside `src/` |
| `generated` | Two runs of the model generators give the same bytes, and `model/generated/` matches a fresh run |

The public repository's CI workflow runs the same command on Windows.

### On Windows with Smart App Control

Smart App Control can stop the freshly built, unsigned product assemblies from running ("An Application Control
policy has blocked this file", 0x800711C7): the build passes, and the checks that run what it built fail. Run the
checks on Linux instead, for example in WSL. Copy `src/` to the Linux disk without build output, so Windows and Linux
builds never share `bin/` or `obj/`, then run the same command there:

```
rsync -a --delete --exclude bin/ --exclude obj/ --exclude node_modules/ --exclude dist/ --exclude TestResults/ \
  --exclude '*.db' --exclude '*.db-shm' --exclude '*.db-wal' --exclude .azure/ \
  /mnt/<drive>/<path to the repository>/src/ ~/eag/src/
cd ~/eag/src && node tools/ci.mjs --no-scrub
```

The net48 tests then do not run; the Windows CI job runs them. When a test fails in a way the source does not
explain, clear every `bin/` and `obj/` folder in the Linux copy and run again: rsync keeps the Windows file times, so
a build can miss a file that changed.

### Faster loops

- One test project: `dotnet test --project <test project folder> -c Release -f net10.0`, for example
  `engine/tests/Product.Engine.Runtime.Tests` or `adapters/azure/tests/Product.Azure.Tests` (from
  `src/adapters/azure`, the path is `tests/Product.Azure.Tests`).
- The engine fixtures alone: `dotnet engine/Product.Engine.Cli/bin/Release/net10.0/Product.Engine.Cli.dll run-fixtures fixtures --filter <text>`.

### Opt-in runs and generated files

| What | How |
|---|---|
| The Azure SQL store tests | Set `PRODUCT_TEST_SQLSERVER` to a connection string of an empty test database; without it they skip |
| The read speed timings | `PRODUCT_TIME_READS=1` runs `ReadSpeedTests` in the host tests, which times the heaviest reads on the demo estate and on a generated estate |
| The Azure SQL scripts in `adapters/azure/sql` | Generated from the logical model. `PRODUCT_WRITE_SQL=1` on the store tests writes them instead of failing on a difference |
| The walkthrough transcript | `WALKTHROUGH_WRITE=1 dotnet test --project mcp/tests/Product.Mcp.Core.Tests -c Release --no-build` writes `demo/walkthrough/transcript.json`; then `node demo/walkthrough/render-page.mjs`. Needed when a tool list changes size ([the walkthrough README](../demo/walkthrough/README.md)) |
| These pages' generated parts | `node tools/docs-gen.mjs` after a change to `mcp/contracts/tools.json`, `tool-policy.seed.json`, `errors.json`, the Balanced roles or the demo personas |
| A load run | `node load/run-load.mjs --base http://127.0.0.1:5080` from `src/adapters/azure`, against a local server only |

## The web app checks

From `src/ui/harness`, with Node.js 22 or later, after `npm ci --ignore-scripts`:

| Command | What it does |
|---|---|
| `npm run lint` | The TypeScript type check and the package check |
| `npm test` | The unit tests: every `src/**/*.test.ts`, bundled with esbuild and run with Node's own test runner, with fakes for the network and storage. No browser and no server |
| `npm run build:app` | The web app the product serves under `/app` (into `dist/app`) |
| `npm run build` | The page harness on fictional fixtures (into `dist/harness`) |

The web app takes no package beyond its own list; the `ui` check enforces it.

## The end-to-end tests

The suite in `tests/e2e` ([its README](../tests/e2e/README.md)) plays every demo scenario in the web app against a
running server, one Playwright Test spec per scenario of [the scenario catalog](demo/scenarios.md). Each presenter step
is one test whose title starts with the step ID (`S-03.2`), so a failing test names the presenter step. It runs on a
desktop (1280 px) and a phone (390 px) project with the installed Chrome (no browser download), saves one screenshot
per step and width (`<scenario>-<step>-<width>.png`), and writes a summary JSON and an HTML report to a folder per run.

It needs a browser and a server, so it is not part of `tools/ci.mjs`; the `e2e` check there runs its offline part. On a
machine with Chrome, from `src/tests/e2e`:

```
PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1 npm ci --ignore-scripts
node run.mjs --base http://127.0.0.1:5080/app/ --reset "node ../../demo/kit/demo.mjs local reset" --kit-folder <the kit's --folder>
```

- **The server:** a fresh demo store with the demo director run, such as `node demo/kit/demo.mjs local up`. On Windows
  with Smart App Control the kit runs the server in WSL, and the suite runs from Windows against its loopback port.
- **Writes:** the steps write as the personas do (approvals with a typed reason, proposals, claims, a configuration
  change), so a writing run needs a fresh store per project. `run.mjs` plays the desktop project, runs the reset
  command, then plays the phone project. Without a reset command it plays the desktop project only. Each step declares
  the side sheets it performs (`demo.writes('<tool>@<sheet>')`), and a step fails when one of them was not submitted for
  real and answered ok; `summary.json` lists, per step, the tools and sheets the page performed, the writes the server
  refused, and the engine's answer of each side sheet. A writing run against a local server needs `--kit-folder`, and
  it fails when a step skipped a write: there every state a step needs must be on the store.
- **Read only:** `--read-only` (or `E2E_READ_ONLY=1`) skips every write and checks what each page shows before anyone
  acts. Both projects then share one store. Against a deployed Demo install the suite drives a Chrome the demo operator
  signed in to (`E2E_MODE=demo`, `E2E_CDP_PORT`, the operator's token in `PRODUCT_DEMO_TOKEN`), read only.
- **Claude Code's steps** call the MCP endpoint with the development identity (`priya`, and `config-admin` for S-10.6)
  on a local server. On a Demo install they are skipped and named in the summary.
- **Pace:** the server counts a person's REST calls, the page's and the suite's alike, against its request limit
  (`Product:Limits:ApiPerMinute`, 300 a minute by default). The suite keeps each persona under 200 a minute and waits
  when a page answers "Too many requests just now"; each wait is a note in `summary.json`.
- **demo play:** `node demo/kit/demo.mjs play <scenario, run or track> --base <url>` plays a spec, or a run's steps in
  order, in a visible Chrome at human pace, with a caption bar naming the step and the persona; `--record` saves a
  video and prints where.
- **The reset between projects:** `node ../../demo/kit/demo.mjs local reset` puts back the store saved after the
  director, in seconds (`--reset` of `run.mjs`).
- **Ask again and Send again:** on a fresh store no request is in its last 48 hours and no approval card has failed, so
  the web app hides both actions. Two rehearsal and test aids of the demo kit set each case up on a local run only:
  `demo local age-request --decision <number>` moves one pending request of the decision into its last 48 hours, and
  `demo local fail-card --decision <number>` fails its approval card (`--settled` fails the closed card of a request
  already answered, so "Close the card" shows). They edit the folder's SQLite store directly and refuse a demo install. S-23.11
  needs neither (an emergency change gives two days); S-23.28 runs `fail-card` itself with `run.mjs --kit-folder`
  (`E2E_KIT_FOLDER`). A read-only run never runs it. [The demo kit README](../demo/kit/README.md#demo-local-age-request-and-fail-card-rehearsal-and-test-aids)
  has the details.

The specs S-01 to S-15 replace the earlier package-free story script; the suite adds S-16 to S-23. S-22 and S-23 play
last: together they perform every side sheet the web app offers.

## Accessibility

`scripts/a11y.mjs` in `src/ui/harness` checks pages with headless Chrome at 1280 px and 390 px, with no packages:

- **The harness** (the default): the four harness pages on their fixtures, and again with every tool failing.
- **The web app** (`--app http://127.0.0.1:<port>/app/`, a loopback URL only): every page and view of the routes, each
  record page on a demo record, signed in as each development persona of `--personas` (by default a requester, an
  application owner and the platform admin). On each page it also opens the first action sheet the persona sees and
  checks it, and that focus moves into the sheet, stays there, leaves with Escape and goes back to its trigger. A sheet
  that is not a page's main action has a page entry of its own that opens only that sheet, such as "What depends on
  it?", "Record a decision" on Measures, "Import data domains" (a configuration admin: `--persona config-admin-web`)
  and "Ask again". A unit test fails when a page kind of the routes is missing from the list.
- `--clock-days <n>` moves the page's own clock n days ahead (the server's stays), so a sheet that shows only near an
  expiry can be checked on a fresh demo: "Ask again" offers itself in a request's last 48 hours, so
  `--clock-days 4 --pages work-ask-again` shows it to the requester. Nothing is written.
- **What it checks:** the page language and title, one main landmark, one level 1 heading and no skipped level, a name
  for every image, control, link and tab, no duplicate IDs, ARIA references that resolve, no positive tabindex, text
  contrast (4.5 to 1, or 3 to 1 for large text), no sideways scrolling at 390 px, and a walk with the Tab key with no
  keyboard trap. It does not judge how easy the focus outline is to see, the reading order or what a screen reader
  says; a person checks those.
- `--axe <file>` also runs axe-core from a file the person running it provides. axe-core is MPL-2.0, so it is never a
  dependency here.

`scripts/screenshots.mjs` renders the harness pages at both widths and fails on a page error or sideways scrolling.

## Before a demo: the demo kit check

`node src/demo/kit/demo.mjs check --base <url>` writes nothing and gives a go or no-go: health, `config.json`, every
persona's sign-in, the story's approval requests (an expired one is a no-go; one that expires within 48 hours names
`demo refresh`), each persona's pending work, and every scenario's preconditions from `demo/kit/scenarios.json`. It
exits 1 when any scenario is not ready. `--scenario S-04` checks one, and `--track run-15` checks the scenarios a run or track plays, with the scenarios of its `before` steps. Its own tests run in the `kit` check above.
[The demo kit README](../demo/kit/README.md) has the precondition language and every option.
