# The end-to-end suite

One Playwright Test spec per demo scenario, played in the web app against a running server, the way the presenter
plays it. The scenarios, their steps and the words each step proves on screen are in
[the scenario catalog](../../docs/demo/scenarios.md) and its presenter scripts. Every person, group, application, host
and ID the suite uses is fictional.

- **One test per presenter step.** The test title starts with the step ID, such as `S-03.2 Who is asked, and who only
  advises`, so a failing test names the presenter step to look at.
- **Two projects:** `desktop-1280` (1280 by 900) and `phone-390` (390 by 844).
- **A screenshot per step and width**, `<scenario>-<step>-<width>.png` (such as `S-03-2-1280.png`): the
  pictures of each step.
- **A read-only mode** for a deployed Demo install, or for any store that must not change.
- **The installed Chrome** (Playwright's `chrome` channel). No browser is downloaded.

## Install

Node.js 22 or later and Google Chrome. In `src/tests/e2e`:

```
PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1 npm ci --ignore-scripts
```

The only package is `@playwright/test` 1.65.0 (Apache-2.0) with its two parts, pinned in `package-lock.json` and listed
in `src/THIRD-PARTY.md`. Set `E2E_CHROME` to a Chrome executable when Chrome is not installed where the `chrome`
channel looks.

## Run it against a local demo

The suite needs a server with a fresh demo store: the demo estate, seeded, and the demo director run after it. On a
local run that is `node src/demo/kit/demo.mjs local up` (port 5080 by default; `--port` and `--folder` choose others).
The server runs with the development sign-in, the demo flag and the directory mode Demo, as the kit starts it.

Then, from `src/tests/e2e`:

```
node run.mjs --base http://127.0.0.1:5080/app/ --reset "node ../../demo/kit/demo.mjs local reset" --kit-folder <folder>
```

`run.mjs` plays every scenario in order on `desktop-1280`, runs the reset command, then plays them again on
`phone-390`. `demo local reset` puts back the store `local up` saved right after the director and restarts the server
on it, in seconds; `demo local up` again does the same from the seed, in a few minutes. Any command that leaves the
same server on a fresh demo store will do. Pass the kit's own `--folder` (and `--port` for `local up`) when the server
is not the default one. Without `--reset` (or `E2E_RESET_COMMAND`) it plays the desktop project only and says so: a writing run
changes the store, so each project needs its own fresh store. `--projects phone-390` plays one project; spec names
after the options narrow the run, such as `node run.mjs S-04` for one scenario. `--kit-folder` names the kit's
`--folder` of the local server (`E2E_KIT_FOLDER`): S-23.28 runs the kit's `demo local fail-card` there to make a failed
approval card. A writing run against a local server is refused without it, and it fails when any step skipped a write:
on a fresh local store every state a step needs is there, so a skip means an earlier step went wrong. From Git Bash,
put `MSYS_NO_PATHCONV=1` in front, or the folder reaches the suite as a Windows path.

Playwright's own command line works too, one project at a time on a fresh store, with the kit's folder for S-23.28
(without it that step fails on a local run, since its failed card cannot be made):

```
E2E_KIT_FOLDER=<folder> E2E_BASE_URL=http://127.0.0.1:5080/app/ npx playwright test --project=desktop-1280
E2E_BASE_URL=http://127.0.0.1:5080/app/ npx playwright test --project=desktop-1280 specs/S-16-teams-and-committees.spec.ts
```

A writing run that selects both projects at once is refused before the first step, with the reason. Write `--project=<name>` with the equals sign: `--project` takes several names, so a spec after `--project <name>` would be read as another project.

### On Windows with Smart App Control

Smart App Control can stop the product's freshly built server from running on Windows, so the server runs in WSL and
the suite runs from Windows against it. WSL forwards its loopback ports to Windows, and the development sign-in takes
loopback requests only. `demo local up` from Windows does this on its own: it builds the web app, copies `src/` into
WSL and starts the server there. Keep the store and the build on WSL's own disk, never under `/mnt`.

## Read only

```
node run.mjs --base http://127.0.0.1:5080/app/ --read-only
```

`--read-only` (or `E2E_READ_ONLY=1`) skips every write: each step opens its pages and checks what they show before
anyone acts, and the summary lists each write it skipped. Both projects then play on the same store. A step whose
proof is the result of a write checks the state before the write and says what it skipped. Claude Code's reads on
the MCP endpoint still run on a local server; its refused calls and drafts are skipped.

### A deployed Demo install

A Demo install signs in with Microsoft Entra ID, which the suite never does. The demo operator starts Chrome with its
DevTools port open on 127.0.0.1 and a profile of their own, signs in to the web app in one tab, and the suite drives
that tab, read only:

```
"C:\Program Files\Google\Chrome\Application\chrome.exe" --remote-debugging-port=9333 --user-data-dir=<a profile folder> https://<the install's host>/app/
read -rs PRODUCT_DEMO_TOKEN && export PRODUCT_DEMO_TOKEN    # bash; PowerShell: $env:PRODUCT_DEMO_TOKEN = Read-Host -MaskInput
E2E_MODE=demo E2E_CDP_PORT=9333 E2E_READ_ONLY=1 node run.mjs --base https://<the install's host>/app/ --read-only
unset PRODUCT_DEMO_TOKEN
```

The persona switch picks each step's persona in that tab (the same storage key the web app's "Acting as" menu uses),
and the checks behind the pages call the REST API with the operator's bearer token and the persona header. The token
comes from `PRODUCT_DEMO_TOKEN` (`E2E_TOKEN_ENV` names another variable); copy it from the Authorization header of a
request the page sends to `/api`, as for the demo director. It is never printed or stored, and in demo mode the suite records no Playwright trace (a trace's network log keeps
request headers, the bearer token among them). On an install rebuilt by `demo sandbox rebuild`, Claude Code's seeded
work is there (the kit's director runs in dev mode over the install's database): set `E2E_AI_SEEDED=1` so the steps
check it. Claude Code's calls are
skipped there: the MCP endpoint takes no development identity on a Demo install. When the run ends, the tab acts as the
operator again.

## What a run leaves

Each run writes one folder, `runs/<run>/` (git ignores `runs/`):

| Path | What it holds |
|---|---|
| `screenshots/` | One full-page screenshot per step and width, `<scenario>-<step>-<width>.png`, taken at the end of the step, also when it failed |
| `summary.json` | Every step played: project, outcome, seconds, the checks that failed, the writes it skipped, the write tools the page performed (`performed`: sent for real and answered ok), the side sheets it performed (`sites`, as `tool@sheet`), the writes the server refused (`refusedWrites`), the engine's answer of each side sheet (`answers`), its notes and its screenshot; totals per project and the failed steps |
| `report/index.html` | Playwright's HTML report (`npx playwright show-report runs/<run>/report`) |
| `test-results/` | For a failed step: its trace (`npx playwright show-trace <file>`) and the error context |
| `videos/` | `demo play --record` only: one video per scenario, `<scenario>-<width>.avi` (Motion JPEG) |
| `run.json` | The run's settings: the web app, the mode, read only or not, the projects |

The run's name is its start time, or `--run <name>` (`E2E_RUN_ID`).

## demo play

```
node src/demo/kit/demo.mjs play S-04 --base http://127.0.0.1:5080 [--phone] [--record] [--slow-mo 600] [--read-only]
```

The demo kit plays one scenario's spec in a visible Chrome at human pace: headed, one worker, no retries, a pause of
`--slow-mo` milliseconds between browser actions, and a caption bar at the foot of the page naming the step and the
persona. `--record` saves a video of the scenario in the run folder's `videos/`. The suite reads the kit's settings
from `DEMO_PLAY`, `DEMO_SLOW_MO_MS`, `DEMO_CAPTIONS`, `DEMO_RECORD` and `E2E_READ_ONLY` (`lib/env.ts`). A scenario
that writes needs a fresh store, as any writing run does.

## Checks without a browser

`npm run check` here, or the `e2e` check of `node tools/ci.mjs` from `src/`:

- `tools/check-suite.mjs`: every scenario of `src/demo/kit/scenarios.json` has its spec and its presenter script, and
  each spec has one test per step of the script, with the same ID and title, in the same order, and no other test.
- `tools/check-actions.mjs`: every side sheet of the web app (each `<ActionSheet>` in `src/ui/harness/src`, keyed
  `<path>#<component>`, such as `pages/app360/sheets#DocumentCurrentSheet`) is declared by a step's test
  (`demo.writes('<tool>@<key>')`) and has a row in the catalog's table "Every side sheet of the web app", and each
  row's steps perform its sheet. Each sheet's `site` attribute must be its key; every tool in `WRITE_TOOLS`
  (`ui/harness/src/data/contracts.ts`) has a sheet; a tool or a site written other than as a string literal fails.
  `--list` prints each sheet with its steps.
- `tools/check-scripts.mjs`: every count a step's "Proof on screen" names (such as "Submitted. 2 approvals are asked.")
  appears in the test of the same step, so a script cannot promise a number no test checks. Dates, times, IDs,
  version numbers and links do not count. A count that falls as the store ages is written as N, with what a fresh
  store shows in brackets ending "on a fresh store"; `--list` prints each proof a miss stands in.
- `node ui/harness/scripts/check-packages.mjs --package tests/e2e`: exact versions, packages from the public registry
  with sha512 hashes, allowed licenses, and `THIRD-PARTY.md` against the lock file.

## How a spec is written

```ts
import { scenario } from '../lib/demo';

scenario('S-01', 'PMO: the portfolio at a glance', step => {
  step('S-01.1 The whole register', async ({ demo }) => {
    await demo.open('overview', 'mateo');
    await demo.see('All portfolios. 48 applications in the register.');
    await demo.count('Owner gaps', 6);
  });
});
```

`step` is the suite's Playwright `test`, so the report points at the spec's own line. Its title is the step ID, a
space and the step's title from the presenter script; `tools/check-suite.mjs` holds the two together. The scenario's
hooks set the caption and the trace before each step and take its screenshot after it, also when it failed.

| Helper | What it does |
|---|---|
| `demo.as(persona)`, `demo.open(path, persona)` | Signs in as the persona from the next page load (the development persona on a local run, the demo persona on a Demo install) and opens a page of the web app |
| `demo.see(text or pattern)`, `demo.count(label, n)`, `demo.notSee(...)` | Checks what the page shows. A quoted string also matches without its punctuation, because a heading and the line under it read as one sentence in the scripts |
| `demo.click({ text, near, scope, role })`, `fill`, `choose`, `select`, `person`, `setValue` | Acts the way a person does: by the words on the control, in the open side sheet or the page |
| `demo.sheet({...})`, `demo.answer(reason)`, `demo.openCard(statement)`, `demo.proposeAndSubmit({...})` | Side sheets with "Check first", confirms and the engine's answer; an approval answered on the decision page; a proposal sent for approval the way the page offers |
| `demo.act(what, fn)` | A write and what it checks after. Read-only mode skips it and the summary names it |
| `demo.writes('tool@sheet', ...)` | Declares the side sheets the step performs in the web app, by the sheet's key (`data-action-site` on the page). The step fails when one was not submitted for real (not "Check first") and answered ok, unless the run is read only or the step skipped its write (`demo.skip(why)`). A plain `'tool'` declares a tool from any sheet |
| `demo.missing(what)` | A state an earlier step makes is not on the store: skipped on a demo install, a read-only run or a store without Claude Code's seeded work, and a failed check on a local writing run |
| `sheet({ closes: true })` | The sheet closes itself after the write (its record leaves the page), so the step checks the page after it. Without it, a sheet that closed with no answer and no `answerShows` is a failed check |
| `demo.mcp(what, fn)`, `demo.mcpRead(what, fn)` | Claude Code's calls on the MCP endpoint (`lib/api.ts`, `mcpCall`) as a development identity; writes and refusals, or reads |
| `demo.api(what, fn)` | A check that reads the REST API as a persona |
| `demo.phone` | True on the phone project, where a card shows less than a table row |

Rules the specs follow:

- **No state passes between steps in memory.** A step finds what it needs on the server by name (`lib/api.ts`,
  `lib/story.ts`), so any step or scenario plays alone, and `demo play` can start anywhere.
- **Counts follow the run's order.** Where a count depends on an earlier scenario, the step reads the state and
  expects the matching count, as the presenter script says ("Without S-02 the board shows On the table 6").
- **A check names what it proves**, so a failure reads like the presenter script.

## Choices and reasons

Recorded on 2026-11-25, when the suite was built:

1. **One test per step, inside one describe per scenario, on one worker, in file order.** Why: the scenarios share one
   store and follow the 15-minute run's order; a failed step must not skip the steps after it, so the tests are not in
   Playwright's serial mode.
2. **One browser context per worker, kept across steps.** Why: a scenario plays like one person's session and `demo
   play --record` gives one video per scenario. A failed step restarts the worker, so the next step starts clean.
3. **Checks are soft, actions are hard.** A check that fails is recorded and the step goes on; an action that cannot
   finish stops the step with what it was doing. Why: one wrong count should not hide the rest of the step's proof.
4. **A writing run plays one project per fresh store; `run.mjs` resets between projects.** Why: the second project
   would otherwise meet the first one's answers, and its checks of the state before each write would fail. Read-only
   runs share one store.
5. **The phone project sets the width only (390 by 844), with no touch emulation.** Why: the layout follows the width;
   touch emulation changes how Playwright clicks, not what the page shows.
6. **The persona switch writes the web app's own storage key before the page loads.** Why: it is what the account
   menu's Development persona list and the "Acting as" menu store, so the page signs in exactly as a presenter's does.
7. **Controls are found by their words, then clicked by Playwright.** Why: the scripts name buttons by their words,
   and Playwright's click waits until the control can take it.
8. **story-e2e.mjs is retired.** Its fifteen steps are ported into these specs (S-01 to S-15), with its checks, and
   the specs add S-16 to S-22. The package-free `a11y.mjs` and `screenshots.mjs` in `ui/harness` stay.
9. **The `e2e` check of `tools/ci.mjs` runs without a browser.** Why: the CI checks run on Linux with no Chrome and no
   server; the suite itself runs on a machine with both.
10. **The suite paces each persona under the server's request limit.** The server counts a person's REST calls, the
    page's and the suite's alike, against `Product:Limits:ApiPerMinute` (300 a minute by default), and one page can
    send 60 calls (the portfolio review pack). Before each page load the suite keeps the persona under 200 calls in the
    last minute, and when the page or a sheet answers "Too many requests just now" it waits as the answer says and goes
    on. Why: the suite reads faster than a person, and a refused read is not what the step proves. Each wait is a note
    in `summary.json`.
11. **`--record` writes Chrome's own screencast as a Motion JPEG AVI.** Why: Playwright's video needs an ffmpeg
    binary that it downloads, and this suite downloads nothing. VLC and Windows Media Player play the file; any video
    tool converts it to another format. While a video records, the step screenshots are of the viewport, so the video
    never shows a full-page capture. The caption bar is hidden for every screenshot except while a video records (it
    blinked off at the end of every step in the video). The video starts at the first page load and ends one second
    after the last step, so it holds no blank start and no teardown frame.
12. **demo play holds the end of each step** (`--hold`, `DEMO_HOLD_MS`, 4 seconds by default), and the caption runs
    along the foot of the page, beside an open side sheet or at the top when the sheet fills a phone's width. Why: a
    step with nothing to click went by in three seconds in a video, and the caption hid a sheet's final button.
13. **A step declares the side sheets it performs, and the run holds it to them** (wave 4b; QA, fix round 1).
    An init script wraps the page's fetch: each POST to `/api/<write tool>` is reported with the sheet that was open
    (`data-action-site`), whether it was a check only (`validateOnly`), and whether the server answered it ok. A write
    counts as performed only when it was sent for real and answered ok (review). Why: "every web action demoed and
    tested" is then a fact the run checks per sheet, not per tool, and `tools/check-actions.mjs` holds the declared
    sheets to the web app's own sheets without a browser. A side sheet that closes once its record leaves the page (an
    ended mapping or use, a withdrawn draft) says `closes: true`, and the step checks what the page says next and, where
    the page cannot show it, the record through the API.
14. **Coverage is per side sheet, and an entry point that opens a sheet already played is listed, not played again**
    (QA, fix round 1, D-41). The catalog table has one row per sheet, with the other buttons that open the same
    sheet in its last column. Why: a sheet is one form, one write and one answer; playing it from every button would
    double the suite for the same proof. Two entry points are played on their own all the same: the evidence pack's
    "Add evidence", because an auditor's page writes there, and the review's and Owners page's "Submit for approval",
    because it is a sheet of its own.
