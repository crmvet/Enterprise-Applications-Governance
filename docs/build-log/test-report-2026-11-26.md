# Test report: 2026-11-26

The record of the test runs behind this public push, made on 2026-11-26 and 2026-11-27. All times are US Eastern.

**Status:** built and tested; runs as a demo install with fictional data; no release yet. The 2021 Dataverse solution in
[Solutions](../../Solutions/) is still the only release. Every person, team and application in the demo is fictional.

AI sessions ran the tests and wrote this report. The author decides what is published.

## 1. What was run, and on which tree

- **The tree.** Sections 2 and 3 record the runs of 2026-11-26. The browser suite ran that morning, on the `src/` tree
  of the waves 4 and 4b commit (section 3). Later that day a privacy pass went over `src/`, with its review fixes. It
  edited comments, descriptions, test titles and notes, one docs check and one field name in the demo kit's scenario
  list, and wrote the walkthrough transcript and a generated field list again. Then the check runner and the web app
  checks ran again, on a fresh copy of that `src/` tree. The check runner ended at about 11:46 PM and the web app checks
  at about 11:47 PM. Their counts did not move.
- **The tree published in this commit.** On 2026-11-27 a public pass changed `src/` again, and the check runner and
  the browser suite ran again. [Section 6](#6-2026-11-27-the-tree-published-in-this-commit) records those runs.
- **The public CI.** The CI run on this commit repeats the build, the .NET tests and the offline checks on GitHub's
  Windows runner: [`.github/workflows/ci.yml`](../../.github/workflows/ci.yml) runs `node tools/ci.mjs --no-scrub`
  from `src/`. Its runs are listed on the repository's
  [Actions page](https://github.com/crmvet/Enterprise-Applications-Governance/actions/workflows/ci.yml).
- **Where the runs in this report ran.** The check runner, the web app checks and the browser suite ran on one
  developer machine, with no cloud, no tenant and no sign-in as a person:
  - The check runner ran on Linux (WSL2), so the .NET tests ran on their net10.0 target only. The CI job on Windows
    also runs the three engine test projects on .NET Framework 4.8, so its totals for them are larger than the counts
    below.
  - The web app checks ran with Node.js.
  - The browser suite ran in Chrome against a local demo server on a loopback address (section 3).

## 2. Totals

The runs of 2026-11-26. [Section 6](#6-2026-11-27-the-tree-published-in-this-commit) has the runs of 2026-11-27.

| Suite | Command | Result |
|---|---|---|
| Every check of the check runner | `node tools/ci.mjs --no-scrub` from `src/` | All checks passed; the scrub gate is skipped here and runs separately before the push |
| Engine and MCP tests (net10.0) | the runner's `test` check | 1,294 of 1,294 passed |
| Azure host tests | the runner's `azure` check | 256 passed, 1 skipped |
| Store tests | the runner's `azure` check | 167 passed, 85 skipped |
| Engine fixtures | the runner's `fixtures` check | 871 passed, none failed |
| Replay records | the runner's `replay` check | 14 passed, none failed |
| Web app type check and package check | `npm run lint` in `src/ui/harness` | Clean |
| Web app unit tests | `npm test` in `src/ui/harness` | 487 of 487 passed |
| Web app builds | `npm run build:app` and `npm run build` in `src/ui/harness` | Both built |
| Browser suite, desktop-1280 | `node run.mjs` in `src/tests/e2e` | 201 of 201 steps passed |
| Browser suite, phone-390 | the same run | 201 of 201 steps passed |
| Demo readiness check | `node src/demo/kit/demo.mjs check` | GO for all 23 scenarios |

The skips:

- **The one Azure host test skipped** is the read speed timing (`ReadSpeedTests`). It runs only with
  `PRODUCT_TIME_READS=1`.
- **The 85 store tests skipped** are of two kinds: the Azure SQL cases, which run only when `PRODUCT_TEST_SQLSERVER`
  names an empty test database, and the shared store cases that apply only to the row stores, which the in-memory
  store skips.

The check runner's other checks passed too: presets, dashes, JSON, the logical model, the playbook and guidelines
content, the web app's packages, the pipeline record step samples, the demo kit's own tests, the offline part of the
browser suite, the product docs and the model generators. [The testing guide](../../src/docs/testing.md) says what
each one does.

## 3. The browser run

- **When.** 2026-11-26, from about 5:12 to 5:52 AM.
- **Against what.** A local demo that the demo kit started (`demo local up`): the server on a loopback address with the
  development sign-in and the demo flag, a fresh store seeded with the fictional demo estate, and the demo director run
  after it. Before the phone project, the reset command put the same fresh store back, so each project played on a
  fresh store.
- **How.** `run.mjs`, a writing run (not read only), on one worker, in file order, with no retries, in the installed
  Chrome. Each test is one presenter step. A step passes only when every check in it passed and every side sheet it
  declares was sent for real and answered ok.

| Project | Viewport | Steps passed | Failed | Skipped | Skipped writes | Side sheets performed | Write tools performed |
|---|---|---|---|---|---|---|---|
| desktop-1280 | 1280 by 900 | 201 of 201 | 0 | 0 | 0 | 74 of 74 | 40 of 40 |
| phone-390 | 390 by 844 | 201 of 201 | 0 | 0 | 0 | 74 of 74 | 40 of 40 |
| **Both** | | **402 of 402** | **0** | **0** | **0** | | |

- **Side sheets.** A side sheet is a form through which a person writes. The web app has 74 of them, with 40 write
  tools behind them, as [`check-actions.mjs`](../../src/tests/e2e/tools/check-actions.mjs) counts them from the
  source. Each project performed all 74 for real.
- **Refusals the steps expect.** In each project, two steps prove a refusal, and the run lists it as a refused write.
  In S-07.6, an AI use rule of "never" on payroll data refuses a data use, and nothing is written. In S-22.7, linking
  the same runbook a second time is refused after the first link is recorded. Both steps passed, because the refusal
  is what they check.
- **Pacing.** In each project, 7 steps waited before a page load to stay under the server's request limit. Each wait
  is a note in the run's summary, not a failure.
- **What the run leaves.** One screenshot per step and width, the summary JSON and Playwright's HTML report, in a run
  folder that git ignores. They are not part of this repository.

### The scenario specs

The presenter scripts behind the specs are in [the scenario catalog](../../src/docs/demo/scenarios.md). S-22 and S-23
play last and perform the sheets that the earlier scenarios do not. Across the 23 specs, every side sheet the web app
offers is performed.

| Scenario | What it plays | Spec | Steps |
|---|---|---|---|
| S-01 | PMO: the portfolio at a glance | [S-01-portfolio-overview.spec.ts](../../src/tests/e2e/specs/S-01-portfolio-overview.spec.ts) | 5 |
| S-02 | Maker and Claude Code: governance inside the AI build loop | [S-02-ai-build-loop.spec.ts](../../src/tests/e2e/specs/S-02-ai-build-loop.spec.ts) | 8 |
| S-03 | Platform admin: triage, the gap check, places and leavers | [S-03-triage-gap-check-places-leavers.spec.ts](../../src/tests/e2e/specs/S-03-triage-gap-check-places-leavers.spec.ts) | 9 |
| S-04 | Business owner: approvals, owners and the lifecycle | [S-04-business-owner-approvals.spec.ts](../../src/tests/e2e/specs/S-04-business-owner-approvals.spec.ts) | 11 |
| S-05 | Change manager: changes and flagged deployments | [S-05-changes-and-deployments.spec.ts](../../src/tests/e2e/specs/S-05-changes-and-deployments.spec.ts) | 6 |
| S-06 | Architect: reviews and exceptions | [S-06-reviews-and-exceptions.spec.ts](../../src/tests/e2e/specs/S-06-reviews-and-exceptions.spec.ts) | 6 |
| S-07 | Data: domains, a data-use card, the never rule and copies | [S-07-data-taxonomy.spec.ts](../../src/tests/e2e/specs/S-07-data-taxonomy.spec.ts) | 11 |
| S-08 | AI lead and security reviewer: agents | [S-08-agents.spec.ts](../../src/tests/e2e/specs/S-08-agents.spec.ts) | 6 |
| S-09 | PMO, business owner and platform admin: demand, the portfolio and retirement | [S-09-demand-portfolio-retirement.spec.ts](../../src/tests/e2e/specs/S-09-demand-portfolio-retirement.spec.ts) | 11 |
| S-10 | Configuration admins: change the rules safely | [S-10-configuration.spec.ts](../../src/tests/e2e/specs/S-10-configuration.spec.ts) | 6 |
| S-11 | Platform admin: roles and access | [S-11-roles-and-access.spec.ts](../../src/tests/e2e/specs/S-11-roles-and-access.spec.ts) | 4 |
| S-12 | Maker: guidelines and the playbook | [S-12-guidelines.spec.ts](../../src/tests/e2e/specs/S-12-guidelines.spec.ts) | 3 |
| S-13 | PMO: measures and slide-back signals | [S-13-measures.spec.ts](../../src/tests/e2e/specs/S-13-measures.spec.ts) | 3 |
| S-14 | Platform admin: run the product | [S-14-admin.spec.ts](../../src/tests/e2e/specs/S-14-admin.spec.ts) | 5 |
| S-15 | Auditor: answer from the record | [S-15-auditor.spec.ts](../../src/tests/e2e/specs/S-15-auditor.spec.ts) | 4 |
| S-16 | Teams and committees: who answers, who supports, what waits | [S-16-teams-and-committees.spec.ts](../../src/tests/e2e/specs/S-16-teams-and-committees.spec.ts) | 6 |
| S-17 | Architect: the dependency map and what breaks | [S-17-dependency-map.spec.ts](../../src/tests/e2e/specs/S-17-dependency-map.spec.ts) | 5 |
| S-18 | Reader: search, filters and what a reader may do | [S-18-search-and-lists.spec.ts](../../src/tests/e2e/specs/S-18-search-and-lists.spec.ts) | 6 |
| S-19 | Auditor: the decision log and the documents | [S-19-decision-log-and-documents.spec.ts](../../src/tests/e2e/specs/S-19-decision-log-and-documents.spec.ts) | 4 |
| S-20 | Requester: a request from the web app, with no AI | [S-20-requester.spec.ts](../../src/tests/e2e/specs/S-20-requester.spec.ts) | 5 |
| S-21 | Claude Code over MCP: what an AI client may do, and what it may not | [S-21-claude-code-refusals.spec.ts](../../src/tests/e2e/specs/S-21-claude-code-refusals.spec.ts) | 8 |
| S-22 | Everyday records: the work between the decisions | [S-22-everyday-records.spec.ts](../../src/tests/e2e/specs/S-22-everyday-records.spec.ts) | 38 |
| S-23 | Every other action: drafts, answers, rules and records | [S-23-every-other-action.spec.ts](../../src/tests/e2e/specs/S-23-every-other-action.spec.ts) | 31 |
| **23 specs** | | | **201** |

## 4. What does not run in CI

| What | Why not | Where its result is public |
|---|---|---|
| The browser suite | It needs Chrome and a running demo server | This report |
| The web app's unit tests and builds | The check runner checks the web app's packages, not its tests or builds | This report |
| The demo readiness check | It needs a running demo server | This report |
| The Azure SQL store tests | They need an empty test database, named by `PRODUCT_TEST_SQLSERVER` | Nowhere yet: they skipped in these runs too |
| The read speed timings | They are opt in, with `PRODUCT_TIME_READS=1` | Nowhere yet: skipped in these runs too |

CI does run the offline part of the browser suite, the runner's `e2e` check. It holds every spec to its presenter
script (one test per step, with the same IDs, titles and order), checks each count a script names, and holds every
side sheet of the web app to a step that performs it.

## 5. How to rerun them

The runs need the .NET 10 SDK, Node.js 22 or later and, for the browser suite, Google Chrome.
[The testing guide](../../src/docs/testing.md) has every option. On Windows with Smart App Control, run the .NET checks
on Linux, for example in WSL, as the guide explains.

The check runner, from `src/` (`--no-scrub` skips the scrub gate and the content check's working default anchors,
whose files are not in this repository):

```
node tools/ci.mjs --no-scrub
```

The web app, from `src/ui/harness`:

```
npm ci --ignore-scripts
npm run lint
npm test
npm run build:app
npm run build
```

The browser suite ([its README](../../src/tests/e2e/README.md)). First a local demo on a fresh store, and the
readiness check, from the repository root:

```
node src/demo/kit/demo.mjs local up
node src/demo/kit/demo.mjs check --base http://127.0.0.1:5080
```

Then the run, from `src/tests/e2e`. It plays desktop-1280, puts the fresh store back, then plays phone-390:

```
PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1 npm ci --ignore-scripts
node run.mjs --base http://127.0.0.1:5080/app/ --reset "node ../../demo/kit/demo.mjs local reset" --kit-folder <the kit's --folder>
```

[The demo kit README](../../src/demo/kit/README.md) has the kit's options, its folder and the reset.

The Azure SQL store tests, from `src/adapters/azure`, against an empty test database:

```
PRODUCT_TEST_SQLSERVER="<connection string of an empty test database>" dotnet test --project tests/Product.Azure.Stores.Tests -c Release
```

## 6. 2026-11-27: the tree published in this commit

- **What changed.** The public pass of 2026-11-27 changed wording that people and MCP clients read: the MCP contract
  text, one sentence of a tool's answer, the field help in the web app, the playbook, the guidelines and the docs. It
  did not change what the engine decides.
- **The check runner.** It ran again, as in section 1, on a fresh copy of the `src/` tree published in this commit.
  All checks passed, with the same totals as on 2026-11-26. The skips are the ones section 2 explains.

| Suite | Command | Result |
|---|---|---|
| Every check of the check runner | `node tools/ci.mjs --no-scrub` from `src/` | All checks passed |
| Engine and MCP tests (net10.0) | the runner's `test` check | 1,294 of 1,294 passed |
| Azure host tests | the runner's `azure` check | 256 passed, 1 skipped |
| Store tests | the runner's `azure` check | 167 passed, 85 skipped |
| Engine fixtures | the runner's `fixtures` check | 871 passed, none failed |
| Replay records | the runner's `replay` check | 14 passed, none failed |

- **The browser suite.** It ran again on 2026-11-27, from about 1:17 to 1:57 AM (06:17 to 06:57 UTC), on the `src/`
  tree after the changes to the MCP contract text, the tool's answer and the field help, with the web app built from
  that tree. It ran as in section 3: a local demo on a fresh store, a writing run on one worker with no retries, and
  the fresh store put back before phone-390.

| Project | Viewport | Steps passed | Failed | Skipped | Skipped writes | Side sheets performed | Write tools performed |
|---|---|---|---|---|---|---|---|
| desktop-1280 | 1280 by 900 | 201 of 201 | 0 | 0 | 0 | 74 of 74 | 40 of 40 |
| phone-390 | 390 by 844 | 201 of 201 | 0 | 0 | 0 | 74 of 74 | 40 of 40 |
| **Both** | | **402 of 402** | **0** | **0** | **0** | | |

- **As in section 3.** In each project, S-07.6 and S-22.7 proved their refusal and passed, and 7 steps waited before a
  page load to stay under the server's request limit.
- **What the browser did not see.** Wording edits made after this run started, such as those to the playbook and the
  guidelines, were not played in the browser.
- **Not repeated here.** This section does not repeat the web app checks or the demo readiness check of section 2.
