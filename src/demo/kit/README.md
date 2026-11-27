# The demo kit

One command line, `demo`, for the fictional demo of the product: start a local demo with fresh data, refresh a demo
before a meeting so no approval has expired, check every scenario before going on stage, play a scenario in a visible
browser, and a command that is designed to rebuild a demo install on Azure (only its dry run is tested). Every person, group, application, host and ID in the demo is fictional
(`../../adapters/azure/seed`).

It needs Node.js 22 or later and no packages (22.13 or later for the two rehearsal aids, which use the built-in
`node:sqlite`). `demo play` starts the end-to-end suite in `src/tests/e2e`, which has its own package. The server part
needs the .NET 10 SDK where the server runs.

```
node src/demo/kit/demo.mjs <command> [options]     # anywhere
src\demo\kit\demo.cmd <command> [options]          # Windows
sh src/demo/kit/demo <command> [options]           # Linux, macOS, WSL
```

Run `demo help` for the options. Exit codes: 0 done or ready, 1 a step failed or a scenario is not ready, 2 bad usage.

The product documentation puts the kit in context: a local run and a demo install on Azure in
[`install.md`](../../docs/install.md), the demo routines in [`admin.md`](../../docs/admin.md#the-demo-install), and
the checks in [`testing.md`](../../docs/testing.md). Each role's guide in [`../../docs/guide`](../../docs/README.md#the-guide-per-role)
has an "In the demo" section with the scenarios its persona plays.

| Command | What it does |
|---|---|
| `demo local up` | Builds the Release server and the web app if they are missing or stale, creates a fresh SQLite store, seeds the demo estate, starts the server on loopback, runs the demo director, and prints the URL and the personas |
| `demo local reset` | Puts back the store `local up` saved right after the director (`<folder>/data/demo.after-director.db`) and restarts the server on it: a fresh demo in seconds |
| `demo local down` | Stops the server `local up` started, by the PID it recorded |
| `demo local status` | Says whether that server runs and is ready |
| `demo local age-request --decision <number>` | Rehearsal and test aid, local run only: one pending request of that decision now expires within 48 hours, so "Ask again" shows for it |
| `demo local fail-card --decision <number> [--settled]` | Rehearsal and test aid, local run only: the approval card of one pending request of that decision fails, so the Admin page offers "Send again"; with `--settled`, the closed card of a request already answered, canceled or expired fails, so the Admin page offers "Close the card" |
| `demo refresh --base <url>` | Asks again every story approval request that expired or expires within 48 hours, then reruns the director. Prints what it renewed |
| `demo check --base <url>` | Go or no-go: health, config, personas, pending counts, the story's requests and every scenario's preconditions. Exit 1 if any scenario is not ready |
| `demo play <id> --base <url>` | Plays one scenario, a run (`run-5`, `run-15`) or a role track (`scenarios.json` "runs") in a visible browser through the Playwright suite |
| `demo sandbox rebuild` | Designed to rebuild a demo install on Azure step by step, reading every value from the azd environment. `--dry-run` prints the commands and runs nothing; only the dry run is tested |

## demo local up

```
node src/demo/kit/demo.mjs local up --port 5080
```

What it does, in order:

1. **Builds what is missing or stale.** The Release build of `Product.Azure.Server` is rebuilt when any source of the
   engine, the MCP core and its contracts, the presets or the Azure hosts and stores is newer than the built
   assembly. The web app (`src/ui/harness`, `npm run build:app`) is rebuilt when its sources are newer than
   `dist/app/index.html`; `npm ci --ignore-scripts` runs first when its packages are not installed. `--rebuild`
   builds both anyway; `--no-build` and `--no-web-build` use what is built.
2. **A fresh store.** It deletes `<folder>/data/demo.db` and seeds it with the Balanced preset, the demo binding
   (`seed/demo-binding.patch.json`) and the demo estate (`seed/demo-estate.json`). `--keep-store` keeps the store
   as it is.
3. **The server.** It starts the server on `http://127.0.0.1:<port>` in the Development environment, with the
   development identity, the demo flag (`--Product:WebApp:Demo=true`, the "Demo, fictional data" badge and the demo's
   own reads) and the directory mode Demo (`--Product:Directory:Mode=Demo`: role holders, the binding check and team
   members come from the fictional personas; the gap check reads the demo's fictional inventory, in pages of 25 as on every install, and the owner check the
   demo's fictional directory). The server runs detached, so it outlives the command. Its PID, port and
   store are in `<folder>/state.json`, its log in `<folder>/server.log`.
4. **The director.** It runs `seed/demo-director.mjs` against the server, so every persona has live work (log in
   `<folder>/director.log`). `--no-director` starts with the estate only. When the director finishes, the kit stops
   the server, copies the store to `<folder>/data/demo.after-director.db` for `demo local reset`, and starts it again.
   A fresh store removes the copy an earlier run saved, so after `--no-director` there is nothing for `demo local reset`
   to put back until a director run finishes.
5. **The summary.** The web app's URL, the MCP endpoint with the Claude Code command to add it, and the personas. The
   development sign-in page offers each persona by its name and role, such as "Lena Okoro, requester"; the kit and the
   director call the API as `demo-<name>`.

Options: `--port <n>` (default 5080), `--folder <dir>` (the work folder), `--timeout <s>` (the director's wait for
one answer, default 180), `--ready-timeout <s>` (default 180), `--rebuild`, `--no-build`, `--no-web-build`,
`--no-director`, `--keep-store`.

### From Windows: it drives WSL

On Windows the kit drives WSL, because Smart App Control can stop freshly built .NET assemblies from running there.
It builds the web app on Windows, where its packages are installed, copies `src/` into `<folder>/src` inside WSL with
rsync (build output, packages, local stores and azd state left out; the built web app kept), and runs this same kit
natively there with the same options. The server listens on loopback inside WSL, and the browser on Windows reaches
it at the same `http://127.0.0.1:<port>` address.

- `--folder` names a folder on WSL's own disk, default `/root/eag-demo-local`. Not under `/mnt`.
- `--wsl-distro <name>` (default `Ubuntu`) and `--wsl-user <name>` (default `root`). WSL needs the .NET 10 SDK,
  Node.js 22 or later and rsync.
- `--native` runs on Windows itself, where Smart App Control does not block the build.
- In Git Bash, put `MSYS_NO_PATHCONV=1` in front of a command that passes a Linux path, or Git Bash changes
  `/root/x` into a Windows path before the kit sees it. PowerShell and cmd leave it alone.

```
MSYS_NO_PATHCONV=1 node src/demo/kit/demo.mjs local up --port 5086 --folder /root/eag-kit
node src/demo/kit/demo.mjs check --base http://127.0.0.1:5086
MSYS_NO_PATHCONV=1 node src/demo/kit/demo.mjs local down --folder /root/eag-kit
```

## demo local down and status

`local down` reads `<folder>/state.json`, checks that the PID is still the demo server on that port (its command line
names the server and the URL), stops it, and says whether the port is free. It never stops a process by name. A
folder with no running server is not an error. `local status` exits 0 when the server runs and is ready.

## demo local age-request and fail-card: rehearsal and test aids

```
node src/demo/kit/demo.mjs local age-request --decision DEC-0206 [--hours 40] [--role <key>] [--json]
node src/demo/kit/demo.mjs local fail-card --decision DEC-0206 [--role <key>] [--json]
```

On a fresh local store every story request is days from its expiry and no approval card has failed, so the web app
rightly hides two of its actions: "Ask again" (a request that expired or has fewer than 48 hours left) and "Send
again" (a failed card in the Admin page's approval outbox). These two commands set up each case on a local run, so a
presenter can rehearse the action and the end-to-end suite can perform it for real:

- `local age-request` moves one pending request of the decision into its last 48 hours: its Expires On becomes now
  plus `--hours` (default 40, from 1 to 47). Its proposer and a platform admin then see "Ask again" on the decision
  page, the proposer also under Waiting on others in My work, and the engine asks it again. A request with no expiry never shows
  "Ask again", so the command refuses a decision whose requests have none.
- `local fail-card` marks the approval outbox row of one pending request of the decision failed, after the runner's
  default 3 attempts. A platform admin then sees "Send again" for it under "Requests that need a look" on the Admin
  page. With `--settled` it takes a request of the decision that is already answered, canceled or expired, and fails
  its closed card: the Admin page then offers "Close the card" for it. Live, the engine closes a card in the same commit as the
  answer, so only this aid reaches that state (the end-to-end test of S-23.28 uses both).
- The decision is named by its number (`DEC-0206`, or `206`) and must be proposed. When it has several pending
  requests, the command takes the oldest; `--role <key>` takes the one that asks that role or owner role. `--json`
  prints the result as JSON, for a test.

Both edit the SQLite store of `--folder` directly, past the engine, while the server runs or not. The audit trail does
not show the edit, and the changed row's Modified On is later than its Engine Checked On, as for any write outside the
engine. `demo local reset` undoes it. They refuse anything but a local run: the folder's `state.json` must name the
store `local up` created, `--base` (default: the folder's server) must be a loopback http URL of that server, `--mode
demo` is refused, and a server that answers must offer the development sign-in, not a demo install's persona switch.
They never reach Azure SQL, and they are not for a real install. From Windows they run in WSL with this checkout's
kit. They need Node.js 22.13 or later (the built-in `node:sqlite` module).

## demo refresh

```
node src/demo/kit/demo.mjs refresh --base http://127.0.0.1:5080
read -rs PRODUCT_DEMO_TOKEN && export PRODUCT_DEMO_TOKEN     # bash; PowerShell: $env:PRODUCT_DEMO_TOKEN = Read-Host -MaskInput
node src/demo/kit/demo.mjs refresh --base https://<the install's host>
unset PRODUCT_DEMO_TOKEN
```

On a demo install the token is the operator's bearer token for the web app, copied from the Authorization header of a
request the page sends to `/api`. Reading it without echo keeps it out of the shell history.

The engine expires an approval request when its rule's time runs out (five days for a change authorization, ten for
a risk acceptance or a data use). A demo held days after the director ran meets expired cards. Refresh is the answer,
not longer expiries:

1. `get_demo_freshness` lists the story's requests that expired, or expire within 48 hours (`--within-hours`, up to
   48: "Ask again" takes a request in its last 48 hours only).
2. `renew_approval_request` ("Ask again", tool 100) asks each one again, as the platform admin persona `owen`
   (`--as <persona>`; the proposer of a decision may also ask again). The engine closes the old request and opens a
   new one with the expiry its rule gives. A request asked again already is left as it is.
3. The director runs again (`--no-director` skips it). It checks before it creates, so it adds only what is missing.
4. `get_demo_freshness` again. The kit prints each request it renewed, with who is asked and until when, and the
   state after.

`--dry-run` sends each "Ask again" with `validateOnly` and skips the director: nothing is written. `--json` prints the
result as JSON. Exit 1 when a request could not be asked again, the director failed, or the demo is still not fresh.

## demo check

```
node src/demo/kit/demo.mjs check --base http://127.0.0.1:5080 [--scenario S-04 | --track data-governance] [--json]
```

`--track` takes a run or a track of `scenarios.json` (`runs`), such as `run-15` or `pmo`, and checks only the
scenarios it plays, including the scenarios of its `before` steps (the steps played off-screen first): `--track pmo`
checks S-03 for its S-03.3 as well as S-01, S-04, S-09, S-13, S-16 and S-17.

It writes nothing. It prints a table of the global checks, each persona's pending work, and one row per scenario:

- **Health:** `/health/live` and `/health/ready` answer 200.
- **Config:** `/app/config.json` says `demo: true`. On a demo install every persona of the persona file is offered by
  the persona switch; on a local run the development sign-in lists them.
- **Persona count and sign-in:** each persona of `seed/demo-personas.json` signs in as itself (`GET /api/me`) on a
  configuration version in force.
- **Story requests:** none has expired (`get_demo_freshness`). One that expires within 48 hours makes the check name
  `demo refresh`.
- **Pending work by persona:** approvals asked of them (their own proposals counted), waiting on others, drafts (by
  AI), reviews, documents, owner gaps, claims and the length of My work.
- **Scenarios:** each scenario of `scenarios.json` with its preconditions met or not, and what is missing.

Exit 1 when a global check fails or any scenario checked is not ready. On a demo install the preconditions marked
`"needs": "ai-client"` (Claude Code's seeded work) are checked only when the operator's Claude Code token is set
(`PRODUCT_DEMO_MCP_TOKEN`, or the variable `--ai-token-env` names), because the director does that work only then.

### scenarios.json

The catalog of scenarios the kit checks, validated by `scenarios.schema.json`, with the same IDs as the presenter
scripts of `src/docs/demo` (catalog: `src/docs/demo/scenarios.md`). S-01 to S-16 are the 15-minute guided story
(`story` is the time slot in that run); S-17 to S-22 are the deep dive (`story` null). Each entry names its presenter
script (`script`), the story section it belongs to (`section`) and its end-to-end spec in `src/tests/e2e` (`spec`,
`specs/<ID>-<slug>.spec.ts`).

A precondition is one read the kit makes as a persona, through `POST /api/{tool}`, with a read-only tool only (the
kit's tests check each tool's contract):

```json
{ "label": "Kai is asked about Visit Report Agent's use", "as": "kai", "tool": "list_approvals", "input": { "view": "mine" },
  "where": { "decision.statement": { "contains": "Visit Report Agent" } }, "count": { "min": 1 } }
```

- **A list** (`list`, default `data.items`, read to its last page unless `page` is false): the items that meet
  `where` are counted against `count` (`min`, `max`, `equals`; default at least 1).
- **One value** (`value`, a dot path such as `data.ownerGaps`): it meets `expect`, or, for an object such as
  `data.counts`, `where`.
- **A pending count** (`pending` and `field`): one persona's count from the pending table.
- **Matchers:** a plain value is equality (text ignores case); or `contains`, `in`, `min`, `max`, `exists`, `some` and
  `none` (an array with an element that meets, or none that meets, a nested `where`), and `length`.
- **Lookups:** an input value `{"$lookup": {"tool", "input", "list", "where", "pick"}}` is the field `pick` of the
  first item of that read that meets `where`, such as a portfolio's ID by its name.

## demo play

```
node src/demo/kit/demo.mjs play S-04 --base http://127.0.0.1:5080 [--record] [--phone] [--slow-mo 600] [--read-only]
```

It plays one scenario in a visible browser at human pace, with a caption bar naming the step and the persona, using
the same steps as the scenario's end-to-end test. The browser part is the Playwright suite in `src/tests/e2e`
([its README](../../tests/e2e/README.md)): the kit finds the scenario's spec in `scenarios.json` and starts the suite
headed, with one worker and no retries, on the project `desktop-1280` (`--phone`: `phone-390`). Install the suite
once with `PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1 npm ci --ignore-scripts` in `src/tests/e2e`; until then the command says
so and exits 2. A scenario that writes needs a fresh store, as any writing run of the suite does.

The settings the suite reads in play mode (`lib/play.mjs`, `playPlan`): `E2E_BASE_URL`, `DEMO_PLAY=1`,
`DEMO_SLOW_MO_MS`, `DEMO_CAPTIONS=1`, `DEMO_RECORD=1` for `--record` (one video of the scenario in the run
folder's `videos/`), and `E2E_READ_ONLY=1` for `--read-only`. The suite uses the installed Chrome, with no browser
download.

## demo sandbox rebuild

```
node src/demo/kit/demo.mjs sandbox rebuild --dry-run
node src/demo/kit/demo.mjs sandbox rebuild --yes [--steps seed,director,check] [--extra-binding <file>] [--extra-rows <file>]
```

The rebuild of a demo install on Azure, from `src/adapters/azure` (the azd project), in seven steps:

| Step | What it runs |
|---|---|
| `files` | Writes the role binding patch and the rows the seed writes into `--folder` (default `src/adapters/azure/.azure/demo-kit`, which git ignores): the demo's own files, plus `--extra-binding` (a file with `bindingOperations`, such as the install's own admin groups) and `--extra-rows` (a file with `rows`, which may not reuse a demo row's ID) |
| `reset` | `az sql db delete`: the database, so the provision creates it empty |
| `provision` | `azd env set PRODUCT_DEMO_PERSONAS true`; `azd provision --no-prompt --no-state` (after a reset the templates are unchanged, and a plain provision would skip the deployment that creates the database). `--client-ip <address>` sets `SQL_CLIENT_IP_ADDRESS` first |
| `deploy` | `azd deploy --no-prompt` (its prepackage hook builds the web app), then `/health/ready` and `/app/config.json` (demo true, every persona offered) |
| `seed` | Builds the Release server and seeds the demo estate in the Demo environment over the install's database, signed in as the person signed in to the Azure CLI |
| `director` | Starts a local server on loopback (`--local-port`, default 5099) over the install's database, in Development with the development identity, the demo flag and the directory mode Demo, and runs the director through it in dev mode (`--timeout`, default 240). The development personas carry the same fictional object IDs and groups as the demo personas, and no person's token is handled. Claude Code's seeded work is done too, as `priya` over the local MCP endpoint. The rows say "Web app" ("Claude Code" for Claude Code's); only a live persona switch on the install stamps "Web app, demo operator" |
| `check` | The install's health and config.json, then `demo check` through the same local server, which stops after the last step that uses it |

Every value comes from the azd environment (`azd env get-values`: `AZURE_RESOURCE_GROUP`, `AZURE_SQL_SERVER_NAME`,
`AZURE_SQL_SERVER_FQDN`, `AZURE_SQL_DATABASE_NAME`, `PRODUCT_PUBLIC_BASE_URL`, `PRODUCT_DEMO`), or from `--env-file`.
Every command is printed before it runs, with `$NAME` in place of each value, so no value of the install reaches the
screen. A local path is printed relative to `src/adapters/azure`, where every command runs, such as
`.azure/demo-kit/sandbox-world.json`; a file outside the source tree is printed as `<folder>/<file>` or as "the
`--extra-binding` file", so no checkout path, drive letter or home folder shows either. The output of az, azd and
dotnet goes to `<folder>/<step>.log`; the last lines are shown with IDs, URLs, host
names and addresses left out. `--dry-run` prints the steps and commands and runs nothing: no az, azd or dotnet command,
and no file written. A real run needs `--yes` whatever `--steps` says, and stops before its first step unless
`PRODUCT_DEMO` is true in the azd environment (it runs on a demo install only). Run it where .NET runs and where the
install's azd environment is: on Windows with Smart App Control, a clone of the repository inside WSL, with
`azd auth login` and `az login` run there (the copy `demo local up` makes leaves out `.azure/`).

## The director's options the kit uses

`seed/demo-director.mjs --timeout <seconds>` (default 180) is how long the director waits for one answer. A local
server answers in well under a second; the default leaves room for a slow connection between a server and a demo
install's database. The sandbox rebuild passes 240.

## Tests

`test/kit.test.mjs` tests the kit offline, against a small fake server on 127.0.0.1: the command line, the copy rules
into WSL, the scenario file (unique IDs, known personas, read-only tools), the precondition language, `check` and
`refresh` (go and no-go, renewed and refused requests, dry run, a track with its `before` steps), the sandbox dry run
(every step and command printed with `$NAME`, no value and no absolute path printed, no file written, no real run
without `--yes`, and no command at all on an install whose `PRODUCT_DEMO` is not true), the `play` hook (a scenario,
and a run or track grouped by scenario) and the two rehearsal aids on a small SQLite store (the request and the card
they change, and their refusals of a demo install, a non-loopback base and `--mode demo`). From `src/`:

```
node --test demo/kit/test/kit.test.mjs
```

`tools/ci.mjs` runs it as its `kit` check.

## Choices and reasons

Recorded when the kit was built:

- **One kit, natively or through WSL.** The kit runs the product where .NET runs. From Windows it copies the source
  into WSL and runs itself there, so there is one code path for the server and the Windows side only builds the web
  app and copies files. Why: Smart App Control blocks freshly built .NET on Windows, and a second, Windows-only server
  path would drift.
- **The server is detached and stopped by its recorded PID only,** after checking that the PID is still the server on
  that port. Why: other demo and test servers run on the same machine, and stopping by name could stop theirs.
- **Refresh renews as the platform admin persona.** "Ask again" allows the proposer or a platform admin; one persona
  covers every story request. Why: the proposers differ per request, and the audit trail still names who asked.
- **Refresh looks at most 48 hours ahead,** the engine's window for asking again early. Why: a wider window would list
  requests the engine refuses to renew.
- **Preconditions are data, not code.** The scenario file holds them, so the documentation phase can complete the
  catalog without touching the kit, and the schema and the kit's tests keep every precondition a read-only call.
- **A precondition about Claude Code's seeded work is marked `needs: ai-client`.** Why: on a demo install the director
  does Claude Code's part only with the operator's Claude Code token; without it the demo shows Claude Code live, and
  the check must not call that scenario not ready.
- **An expired story request is a no-go; one that expires within 48 hours is a warning.** Why: an expired card cannot
  be answered on stage, while one with hours left still works, and the warning names `demo refresh`.
- **The sandbox rebuild prints `$NAME`, never a value, and dry run touches nothing.** Why: the install's names and
  addresses stay off screens and recordings, and a dry run can be shown or reviewed safely.
- **The Change board's work in the demo is an exception about a change.** Under Balanced only the change board holds
  the decision right and the approval of an exception about a standard, normal or emergency change, so the director
  has Tomas Rendel propose one on the Desk Booking calendar connector update, and the Change board's card waits.
- **The demo check stops before any step on an install that is not a demo, and every real rebuild needs `--yes`.**
  Why: the reset deletes a database and the director writes through the persona switch; a selected production
  environment must never reach either (security review, fix round 1).
- **`local up` saves the store after the director, and `local reset` puts it back.** Why: a fresh demo for the next
  audience in seconds, instead of the seed and 64 director steps again; the server stops first, so SQLite has written
  everything to the file.
- **`demo play` holds the end of each step, and the caption moves beside an open sheet.** Why: a step with nothing to
  click went by in three seconds in a video, and the caption hid a sheet's final button.

Recorded in wave 4B (2026-11-25):

- **`demo check --track` checks the scenarios of the track's `before` steps too.** Why: the presenter plays those
  steps off-screen first, and the track's own steps stand on them, so a scenario that is not ready there breaks the
  track as surely as one it plays on screen.
- **The sandbox rebuild prints local paths relative to `src/adapters/azure`, or as a placeholder outside the source
  tree, and the seed command runs with the same relative path.** Why: the dry run is meant for a shared screen, and the
  full checkout path names a person's folders; every command runs in `src/adapters/azure`, so the relative path is the
  one it uses.
- **"Ask again" and "Send again" are rehearsed with two kit commands that edit a local store, not with a server
  switch or a test-only tool.** Why: on a fresh store both actions are rightly hidden, and waiting two days or breaking
  the approval runner is no rehearsal. A server switch or tool would ship a way to forge expiry and delivery state in
  every install; a kit command that refuses anything but the SQLite store of a local run keeps it off every real
  install and off the audit trail's normal path, and `demo local reset` undoes it.
- **The rehearsal aids take the decision's number and the oldest pending request, with `--role` to pick another.**
  Why: a presenter and a test name a decision by the number the page shows; the oldest request is the one the story
  asked first, and `--role` keeps a decision with several requests deterministic.
- **`age-request` defaults to 40 hours and accepts 1 to 47.** Why: the engine asks a request again in its last 48
  hours and the web app offers "Ask again" with fewer than 48 left, so 40 leaves the card valid for a whole rehearsal
  day with the button showing.
- **`fail-card` sets the runner's default 3 attempts.** Why: a card fails after its attempts, so the Admin page's
  "3 attempts to send" matches what a real failure shows.
- **From Windows the rehearsal aids run this checkout's kit in WSL, not the copy `local up` made.** Why: a folder
  started by an older kit has a copy without these commands, and the store is the same either way.
