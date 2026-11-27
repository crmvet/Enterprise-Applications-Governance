# Status of the Azure binding

As of 2026-11-26.

In short: built and tested; runs as a demo install with fictional data; no release yet. The binding builds clean, and
the product runs end to end on one machine over SQLite with the development identity: the MCP endpoint, the REST API,
the product's web app (the four first page designs plus the work and record pages, D-36), the approval loop with a
stand-in for the approval runner (rules that need several answers, expiry and restarts included), and the directory
reads over recorded responses. On Azure it runs as a demo install with fictional data, over Azure SQL; the Azure SQL
cases of the store tests have not yet run against a real database, and no real install exists, because the bootstrap
of version 1 has no entry point.

**2026-11-26, wave 4b fix round 2: a withdrawn draft location frees its External Id (D-41).** The
store key of O25 (Data Platform and External Id) counts the locations in service: no Retired On, and not archived, as
the engine counts them (`StoreKeyScope.InServiceRows`). Before, a draft location withdrawn with an External Id kept its
value, and adding the same ID again passed the check and then answered HTTP 500. New Azure SQL migration
`sql/0009-location-key-in-service.sql` (by hand) removes the key rows that earlier builds kept for archived locations.
**The host now requires schema version 9**, so a deployed database needs `migrate` (a sandbox rebuild runs it). A
SQLite file goes to its schema version 4 and builds its store keys again on first use. `DataLocationKeyHostTests`
covers both sides on SQLite.

**2026-11-25, wave 4b: every web action demoed (D-41).** The owner check runs in a demo too:
`DemoDirectoryReader` answers an account state from the store's Principal References (enabled for a persona with none,
unknown otherwise), and the run says "checked in the demo's fictional directory". `seed/demo-inventory.json` adds one
item and leaves two out, so the first run of a demo opens a claim and raises two vanished items. Fixes the demo steps
found: a data move whose mapping would collide is refused (it answered an internal error), withdrawing a draft
application withdraws its ownership decision, and marking a registered application as holding no business data
answers within its output contract. Marcus Oyelaran is the seventeenth demo persona.

**2026-11-25, wave 4b: the backend follow-ups (D-41).** The gap check runs in a demo: the Demo
environment, and Development with the directory mode Demo, bind `DemoInventoryReader` (the register's covered components,
the claims' items and the optional `seed/demo-inventory.json`, less the components that vanished), so `run_gap_check`
answers instead of `upstream_unavailable`; a run of more than one page no longer raises a false vanished claim for the
components of its earlier pages. The seed writes Published On on version 1, so "in force since" and the configuration
review date are stable. The local run offers seventeen development personas (`demo-marcus`, architect and change board).
The MCP request log line reports the request's database round trips. The Data tabs' later pages read the list the first
page worked out (60 seconds, emptied by any write); the decision log, the approvals, the measures, the owner list and the
Data tabs prefetch, and the leave reads its owners and reviews in two reads: at 2,000 applications every page of them
stays under its ceiling in `ReadSpeedTests` (for example `get_measures` 219 to 24 reads, the leave's preview 3.3 s to
0.3 s, the application card 1.2 s to 0.4 s). New setting `review-request-due-days` (14) in Balanced, so the replay
records carry the new Balanced hash. No table, column or migration: the schema version stays 8.

**2026-11-25, wave 4: the demo kit (D-41).** `src/demo/kit` runs the local demo in one command (`demo local up`, `down`, `status`), checks every scenario before a demo (`demo check`), asks expired story approvals again (`demo refresh`, through `renew_approval_request`) and prints the rebuild of a demo install (`demo sandbox rebuild --dry-run`). The host change: on a local run `DemoDirectoryReader` adds every login of a named persona to that person, so `demo-priya` and `demo-owen`, which carry the teams, make Priya Castell a member of Field Apps Support and Owen Achterberg of Platform Team (before, the two teams showed no members locally; `DemoPersonaTests.OnALocalRunEverySupportTeamListsItsPersonas`). The demo director takes `--timeout` and proposes an exception about the Desk Booking calendar connector update, so work waits on the Change board. No table, column or migration was added: the schema version stays 8.

**2026-11-25, wave 4: the engine leftovers (D-41).** The REST API serves tools 99 to 102 with the same contracts as the MCP endpoint. The host gives the backend its approval outbox (`ApprovalOutboxPort` over `ApprovalOutbox`, `InProcessBackendOptions.ApprovalOutbox`), so a platform admin sends a failed or long-sent card again from the Admin page (`retry_approval_card`, person entry only): the row goes back to pending with no attempts used, a compare and set like the runner's own changes, and the call's Audit Entry names the person and the host row (`EngineLeftoverHostTests.APlatformAdminSendsAFailedCardAgainThroughTheRestApi`, `ApprovalOutboxStoreTests.ARetryPutsAFailedOrLongSentRowBackToPendingAndLeavesOtherRowsAlone`). `HostFacts.HoldsDemoEstate` carries `Product:WebApp:Demo`, so `get_demo_freshness` answers on a Demo install and on a local Development run that holds the demo estate, and not_found anywhere else. The seed's demo patch adds the measure targets the demo sponsor agreed. No table, column or migration was added: the schema version stays 8. A deployed database needs nothing new: the decision's Slide Back Signal is one more member of its JSON columns, and the two new settings arrive with the configuration (a rebuilt sandbox gets them from Balanced; an installed one through an upgrade draft).

**2026-11-25, wave 4: teams and committees (D-41).** The REST API serves the two new reads, `list_teams` (95) and `get_team` (96), with the same contracts as the MCP endpoint. A demo persona may name `teams`, fictional groups that bind no role: `DemoDirectoryReader` lists the persona among each team's members, and a request as the persona still carries only its role groups. The shipped personas put Priya Castell in Field Apps Support, Jonah Pereira in Finance Apps Support, Hugo Brandvold and Theo Marsh in Service Desk and Owen Achterberg in Platform Team, and the development personas carry the same `Teams` (`DemoPersonaTests.OnTheDemoEstateTheTeamsAreTheCommitteesAndSupportTeamsWithTheirPersonas` reads the demo estate's 19 teams through the REST API as a persona). No table, column or migration was added: the schema version stays 8. A deployed demo install shows the support teams' members once it runs with this persona file.

**2026-11-25, the wave 3 verification fixes (D-36 to D-41).** On a demo install or a local run, `get_role_holders` answers `readFrom` "Demo personas" and names each fictional group for the role it is bound to ("Reader group (demo)"), as the binding check does; `DemoDirectoryReader` itself names a group "Demo group", never for one of its members, and leaves out a development login with no name of its own ("A configuration admin"), so the story's two configuration admins are the only holders. The answer to a runner's card says when every approval is in and the gate still holds the decision. No table, column or migration was added: the schema version stays 8, and nothing here needs a deployed database to change.

**2026-11-25, the wave 3 fix pass (D-36 to D-41).** The host gains the directory mode `Demo` (`Product:Directory:Mode=Demo`, Development or Demo only): `DemoDirectoryReader` lists role holders and answers the binding check from the fictional demo personas and their groups (the persona file on a Demo install, the development identity's personas locally), reads no Entra and holds no token; a Demo install with personas uses it whatever the mode. It knows no account state, so the owner check and the gap check still say the directory is not connected, and the demo has no app role assignments, so a workload grant reads as having none. Record links follow a request's own address only when the request is loopback. The MCP initialize instructions of both transports say people answer approvals on the decision page. The demo director runs Claude Code's part on a Demo install when `PRODUCT_DEMO_MCP_TOKEN` holds the operator's Claude Code token for the MCP endpoint (`--ai-token-env` names another variable); without it those steps are skipped, as before. The estate names Noor Safadi security owner of Customer Feedback Bot and Priya Castell technical owner of Mileage Claims (909 rows). No table, column or migration was added: the schema version stays 8. The deployed sandbox gets the Balanced wording changes (G2, the sensitivity answer) through a configuration change, and the estate changes when it is seeded again.

**2026-11-25, the demo data of the data taxonomy and the demo of every wave 3 screen (D-37, D-39, D-41).** The demo estate (906 rows) holds the demo data of the data taxonomy (`docs/architecture/data-taxonomy.md`): 22 data locations, 28 mappings, 30 confirmed data uses, the owners of data domains and locations with their accepted ownership decisions, and one tier answer; the 25-row domain tree is configuration, written by the generator into `seed/demo-binding.patch.json` (the demo configuration patch, compiled into version 1 by the seed command). A sixteenth demo persona, Jonah Pereira, is a second configuration admin. The demo director makes the data cards, a copy and a retention decision, a configuration change, the daily refresh and a security review request live. One runtime fix: the data checks read a tier answer as the tools store it, so DC-09 fires from a stored answer. No table, column or migration was added: the schema version stays 8. The deployed sandbox gets the tree and the data rows only when it is seeded again from these files (seeding fills an empty store only).

**2026-11-24, the reads and the write of the screens still missing (D-36, D-41).** The REST API serves tools 87 to 94 with the same contracts as the MCP endpoint: the configuration versions and drafts and one draft, role access (holders read live through the host's Graph client), the guidelines and playbook chapters, the measures and slide-back signals, the Admin page's status, the agents, and a security review request. The host gives `get_admin_status` the settings that are safe to show through `HostFactsReader` (`InProcessBackendOptions.HostFacts`): the environment name, the demo flag, the step-up mode, the refresh hour, the store kind and the schema version this build needs, and the approval outbox by state with its failed and long-sent rows; never a URL, key, connection string, client ID or tenant ID (`LeftoverHostTests.TheAdminPageShowsTheHostSettingsThatAreSafeToShow`). A platform admin retires a deployment target in the web app, and the record endpoint refuses a new deployment to it with the reason (`deployment.target-retired`). O15 gains Retired On and Retired Reason in its JSON columns: no table, column or migration was added, and the schema version stays 8.

**2026-11-24, the wave 2 leftovers (D-36, D-38, D-41).** The engine's daily due-state refresh runs: the
Functions app's timer `due-state-refresh` fires at five past every hour and runs the refresh, as the engine, in the hour
`Product:DueStateRefresh:HourUtc` names (off while unset); the local server's `refresh-due-states` command runs it once;
a platform admin runs it now on the web app (`run_due_state_refresh`, REST only). The store's call context names the
engine (`Entry` engine, actor kind engine) for every row it writes. With step-up sign-in on, the runner's claim answer says
`"delivery": "link-only"` and the flow template sends a link-only email instead of an answerable card. On a local run with
no public base URL, record links follow the request's own loopback address. One package was added for the timer
(Microsoft.Azure.Functions.Worker.Extensions.Timer 4.3.1, MIT, in `../../THIRD-PARTY.md`). No table, column or migration
was added: the schema version stays 8. The template in `infra/` does not set `Product__DueStateRefresh__HourUtc` yet, so
a deployed install runs no daily refresh until that app setting is added.

**2026-11-24, the data tools (D-36, D-37, D-41).** The REST API serves the ten data tools (76 to 85)
with the same contracts as the MCP endpoint: six reads for any role, and `save_data_use`, `save_data_location`,
`save_data_mapping` and `move_data_references` for a person in the web app (an AI client writes them on the MCP endpoint,
and only previews a move). A person's data use whose governing rule is never is refused on the REST API with
`rule_violation` and a message that names the rule and where it is set (`WebAppApiTests`). No table, column or
migration was added: the schema version stays 8. The deployed sandbox needs the data configuration rows of slices 1
and 2 through a configuration change before the data tools have anything to read.

**2026-11-24, the data taxonomy model (D-37, D-41).** Seven tables join the store: C47 Data
Classification, C48 Data Domain Level, C49 Data Domain, C50 Data Platform, O25 Data Location, O26 Data Mapping and O27
Data Use. SQLite and the in-memory store keep them with no change of schema. On Azure SQL a new migration,
`sql/0008-data-taxonomy.sql`, creates them as system-versioned temporal tables, adds the check that an Owner Assignment
names exactly one subject (an application, a data domain or a data location; every existing row names its application
and passes), creates the one security policy again over every table, and denies the app the new history tables;
`0001` to `0007` do not change. **The host now requires schema version 8**, so a deployed database needs `migrate`
before the new build starts. The store makes the same subject check before a write on both providers, and holds three
new store keys (a location by platform and External Id, a mapping by domain, location and origin, a data use by its
DU record number). The demo binding binds the new role data-governance-lead to a fictional group, played by Hana
Lindqvist. The new configuration rows reach a deployed install only through a configuration change (seeding fills an
empty store only); until then every configuration draft reports the missing link kind of category data location
(V17) and the two data settings (V38).

**2026-11-24, fixes from the wave 2 QA run, security review and product review.** The approval
runner's outcome route refuses a person's answer relayed from a card while step-up sign-in is on (a card then only
links to the decision page, D-38), and writes nothing. In the Demo environment only, the MCP endpoint takes the persona
header from the operator's AI client, so Claude Code acts for a persona on a demo install, stamped "Claude Code, demo
operator"; the operator's ID never reaches the store. `record_deployment` answers a person's hand-recorded deployment
(route person-in-app) without the HTTP 500 its output check gave. The demo estate makes Mateo Brandt the sponsor of
Claims Triage Agent and Nadia Ferreira the deputy business owner of Legacy Timesheet App, and the demo director records
one deployment by hand. The intake request gains a Portfolio column, a JSON member of the row, so no migration was
added and the schema version stays 7. Request visibility, the refused-call budget (60 per caller and hour in the
ledger), leave dates and the restricted demand fields are in the MCP core (document 24, B24-14).

**2026-11-24, reads and writes for the remaining screens.** The REST API serves five tools to the
person entry only: the three above and `save_deployment_target` (a platform admin registers a place deployments land,
so `record_deployment` takes it) and `save_portfolio`. Every tool call that writes now leaves one audit entry, and a call
refused by a rule, a role or the AI rules leaves one in a transaction of its own (operation `call-refused`); both are JSON
rows of the existing audit table, so no migration was added and the schema version stays 7. New reads: the audit trail
(auditor and platform admin), the evidence pack, the AI activity, the tier assessment, the documentation list, the
deployment targets and the gap check runs; `save_dependency` writes the dependency map. The demo director, in a local
run, has Claude Code try to approve for Priya, so the AI activity shows the refused attempt.

**2026-11-24, completion and record fixes.** The REST API serves three tools to the person entry
only: `answer_approval`, and now `complete_review` (a review done once its outcome decision is accepted) and
`complete_change` (an authorized change closed with its result). An AI client on REST or MCP gets `ai_cannot_do_this` for
them, and the engine refuses an AI actor's done or completed on every path. The deployment record's AI-only code
approval flag is stored like the other flags; it is a JSON member of the row, so no migration was added and the schema
version stays 7. Seeded change authorizations, recorded as accepted with no approval request, now read
`settledBy: recorded`, so no page counts their approvals as missing. Step-up sign-in stays with approval answers only.

## What works today

### Build

`Product.Azure.sln` builds with 0 warnings and 0 errors. A clean first build of the Functions project may show the
known AZFW0108 notice (its extensions project is restored during the build); the next build shows none. The Bicep
template in `infra/` compiles with `az bicep build` with no warnings and no sign-in (checked before the demo persona switch added the `demoPersonas` parameter on 2026-11-24; that change has not been compiled, because no Azure command was run for it). The web app builds with
`npm run build:app` in `../../ui/harness`, its type and package check is clean (`npm run lint`), and its 487 unit
tests pass (`npm test`; run of 2026-11-26).

### Tests

| Project | Total | Passed | Skipped | Failed |
|---|---|---|---|---|
| `tests/Product.Azure.Tests` | 257 | 256 | 1 | 0 |
| `tests/Product.Azure.Stores.Tests` | 252 | 167 | 85 | 0 |

The counts are from the run of 2026-11-26 in WSL (the [test report](../../../docs/build-log/test-report-2026-11-26.md)).
The one skipped host test is the read speed timing (`ReadSpeedTests`), which runs only with `PRODUCT_TIME_READS=1`.
The 85 skipped store tests are the Azure SQL cases, which run only when `PRODUCT_TEST_SQLSERVER` names an empty test
database, and the cases only the row stores support (row versions, store keys, a second create of one ID), which the
in-memory store skips. The engine and MCP core suites are part of the solution too; the `test` check of
`tools/ci.mjs` runs them. The same run (in WSL, so net10.0 only; the net48 targets run on Windows): 1294 engine and
MCP tests, all passing. The engine command line runs the 871 fixtures and the 14 replay records, all passing.

One host test, `DirectoryOptionsTests.LiveReadsWithoutTheManagedIdentityClientIdFailStartupOutsideDevelopment`, once
failed in a full run with "Cannot access a disposed object", the in-memory test server's report that its service
provider was already disposed. That report says nothing about the startup checks, so the startup failure tests now try
such a start again, up to three times; a start that fails a startup check is never tried again. The cause in the test
server is not found yet.

Known wording issue: in the `Recorded` directory mode, `get_role_holders` still answers `"readFrom": "Entra, live"`
(the MCP core writes that text whatever reader is bound). The data is the recorded, fictional set; nothing is read
live.

What the host tests cover, as counted at 210 tests on 2026-11-24 (the tests added since are named in the dated notes
above):

| Area | Tests |
|---|---|
| The directory reads: role holders, the owner check, role bindings and the gap check over recorded fictional responses, paging, 429 and 403 answers, foreign links refused, no token in a log or a wrong request (20); the on-behalf-of exchange with a fake token endpoint and the production JWT path (6); startup checks, no client secret anywhere (8); the product's own wiring (2); every directory request carries the marker that keeps it out of HTTP client traces, and the server's tracing leaves marked requests out (2) | 38 |
| Request limits per caller: 429 with `Retry-After` on each route group, one caller's count never limits another or another group, a new minute starts a new count, anonymous requests are never counted, the count lives in the store and survives a restart, the switches, startup checks, no object ID in the counter key | 27 |
| The approval runner: outbox rows in the engine's own commit, claims and leases, sent reports, idempotent answers, conflicts, the engine's expiry of a request and its judgment of a late answer, the refresh batch per claim, releases, restarts of the server, the routes closed to everyone but the runner (15); a rule that needs two answers: two different people, one person counted once, an AI's answer never counted, a rejection that cancels the other card, an expired request refused by the engine and restarted (3); the whole loop through the product's wiring, from an AI draft to a person's answer on a card to the web app's view of it (2) | 20 |
| The approver's ID: an agent user's, the approval runner's or the release pipeline's ID spelled another way (without hyphens, in braces, in parentheses) is refused and nothing is written (4); an agent user's answer in the usual form is a recommendation only, a self-approval is refused however the proposer's ID is spelled, an ID in capitals is stored in lower case, a second person's same answer that races the first is a conflict (4); an answer that says it was given before its request was made is refused (1) | 9 |
| Security probes: path spellings of the MCP endpoint on both transports, body size, browser preflights, proxy headers, response headers, the development identity guards, SQL passwords outside Development | 18 |
| The web app: off unless switched on, the page, its files and their caching, the Content-Security-Policy, nothing outside its folder, `config.json` for Entra sign-in and for a local run, startup checks, the Functions request delegate (8); a demo install: the badge in `config.json` and the seed in the `Demo` environment, refused in every other (2); the person entry: `/api/me` and `whoami` answer `person`, `SESSION_CONTEXT` names the person, a person files and submits an intake as the person, an AI client still writes only through the MCP endpoint and no tool approves, the Tool Policy and the engine refuse a person without the role or the decision right and nothing is written, only the declared web app, agents hold no role (7); a person answers the approval request they were asked through `answer_approval` and the engine moves the decision, with the person not asked, a missing typed reason and a second answer refused, and an AI client or a workload never answers an approval on any entry and nothing is written (2); the Approvals list and the decision page read through the REST API: the web app offers the person `answer_approval` on a request that asks them, an AI client reads the same list with no next tool, and the page evaluates the rules while the decision is proposed and shows the stored records once it is settled (1) | 20 |
| The demo persona switch (D-39), with Microsoft Entra tokens in the `Demo` environment: a demo operator acts as the persona the header names (its object ID, name and roles), and its write is stamped with the persona and the Client App label "Web app, demo operator" in the row, the audit entries and `SESSION_CONTEXT`, with nothing naming the operator; a persona the install does not name is refused and nothing is written; the header is ignored for an AI client on both entries, for an agent, and outside the Demo environment; a host with demo personas outside Demo refuses to start, and the personas are checked (fictional IDs only, one list, the file present, the web app known); `config.json` lists them only in Demo; the Functions request delegate switches as the local server does; the shipped personas play all fifteen human roles through the demo binding, with a second configuration admin | 9 |
| Sign-in refusals on every product route, the development identity switch, the core's token rules over HTTP | 9 |
| MCP endpoint: initialize, tool lists per caller, a read and a write tool, resources, the REST facade, the official SDK client | 9 |
| The health probes: anonymous, status only; 503 while the store refuses and ready again without a restart; 503 within the time budget when the store hangs, with no second check started; the budget's startup check | 6 |
| Tool list against the contracts on disk (names, schemas, annotations) through both MCP transports | 6 |
| The engine's refusals: approvals from an AI actor on every path, the record endpoint's role and grant | 5 |
| The approval responder: the runner never answers as itself, a Workload Grant holder never answers, an owner role's answer counts only from a current owner of the application, a run link is an https URL | 5 |
| The approval runner's flow template: it parses, calls only the runner and outcome routes, sends exactly the members each route takes, takes credentials only from environment variables, holds no secret | 5 |
| Microsoft Entra JWT bearer in Production with a test signing key: expired, foreign, unsigned and mis-addressed tokens (4); the Agent ID facet claims: an agent's user account holds no role, an agent identity is never the approval runner, an agent client is never the person entry (3) | 7 |
| The same routes and answers from the Functions wrapper's request delegate as from the local server, and the route table | 4 |
| Walkthrough replay (below) | 4 |
| Load: concurrent requests against one limit are counted exactly; many callers read and write at once with no server failure; concurrent duplicates of one deployment record one row | 3 |
| A server failure on a product route answers only the correlation ID, and outside Development logs the exception's type and stack, never its message | 2 |
| Response headers: every answer refuses framing, referrers and cross-origin reads; the web app's page keeps its own policy | 2 |
| Persistence across a restart | 2 |

### Replay parity

The MCP core's walkthrough (`src/demo/walkthrough/transcript.json`, 22 exchanges, 16 of them MCP calls) is replayed
against the local server on SQLite three ways: over raw HTTP, through the official MCP C# SDK client, and with the SDK
server transport behind the endpoint. Each replay is compared with a fresh in-process run of the core on the in-memory
store. Every outcome digest and every answer matches (correlation IDs masked), and both stores hold the same rows at
the end. A fourth test checks that the in-process run still answers what the transcript recorded.

### Local run

Run by hand on 2026-11-23 from the README steps, with the web app freshly built (`npm ci --ignore-scripts`,
`npm test`, `npm run build:app`) and the server started from the Release build that `tools/ci.mjs` makes, each store in
its own SQLite file outside the repository (`--Product:Store:Sqlite:ConnectionString`):

1. Seed: version 1 of the configuration (the Balanced preset with the demo binding) and the walkthrough estate,
   32 rows.
2. Start the server in Development on a loopback address with the development identity, the approval runner's routes
   and the recorded directory reads switched on. `/health/ready` answers `{"status":"ready"}` with the hardening
   headers; `whoami` answers that Priya acts through Claude Code as `ai-client-for-person` with the roles maker and
   requester, and that the agent user is `not_authorized`; the record call answers 201, then 200 for the same key.
3. The web app: `/app` redirects to `/app/`; the page carries its Content-Security-Policy and is not cached;
   `/app/config.json` offers the five `*-web` personas; three encoded paths outside the web app's folder answer 404.
   `/api/me` as Nadia on the web app answers her name, `person` and her role; Owen on the web app reads the platform
   admins from the recorded directory; a write tool on the REST API answered that the pages are read-only (this run came before D-36 opened the write tools to the person; the host tests cover the new answers). In headless
   Chrome (`ui/harness/scripts/a11y.mjs --app`), the Overview, Application 360, the dependency map, the timeline, the
   applications list, a missing page and the development sign-in open at 1280 and 390 pixels with no issue (14 page
   loads). The same check on the harness build (`npm run build`, then `a11y.mjs`) opens the four pages on their
   fixtures, and again with every tool failing, at both widths with no issue (16 page loads).
4. The approval loop of the README: Priya submits an intake through the MCP endpoint; the runner persona claims it
   (the claim's `refresh` reports nothing due) and reports the card as sent. Owen's ID without hyphens and in braces
   answers 400 on `approver`, and an answer that says it was given before the request answers 400 on `respondedOn`;
   Priya, Owen, Owen on the web app and the agent user get 403 on the outcome route. Then the runner posts Owen's
   approval with its Idempotency-Key. The answer counts; the decision stays proposed because the new intake has no
   business owner, which the answer's warnings say (`gate.not-ready`, `gate.requirement-owner-missing`). The same
   answer again returns the first result, and nothing is left to claim. 29 checks with steps 2 and 3, all passed.
5. A rule that needs two answers: a second store seeded with the README's two-answer options (the change authorization
   rule at Minimum Count 2, and Ines Varga as a second business owner of Inspection Scheduler; 33 rows). Priya submits a
   change; the engine opens two requests, each asking both owners. Priya, Nadia and Nadia on the web app get 403 on the
   outcome route, and Priya on the claims route. Nadia approves both cards: the first counts, the second does not
   (`approval.responder-already-counted`), and the engine opens a third request that asks only Ines. The runner reports
   an AI client's answer for Ines: stored as recommendation only, and the engine opens a fourth request. Ines approves
   it: two different people, and the engine accepts the decision. The same answer replays; another person's answer to
   that request is a 409; Priya on the web app sees two counting answers; nothing is left to claim. 27 checks, all
   passed.
6. Stop the process on the second store (killed, not shut down cleanly) and start it again on the same file. The same
   configuration version; the same approval status, field for field; the same answer replays; nothing to claim.
7. A load run on a newly seeded store (`load/run-load.mjs --requests 2000 --concurrency 32`, reads only, eight call
   kinds over the REST API and the MCP endpoint): 2000 calls in 7.7 seconds, 1850 answered 200 and 150 answered 429 by
   the request limits per caller, none failed and no server failure logged; 31 ms at the median, 608 ms at the 95th
   percentile, 1388 ms at the 99th. The 150 is the limits' arithmetic: each of the five AI client personas sends 150
   MCP calls against the default of 120 a minute, and each web app persona sends 250 REST calls, under its 300. So the
   counts repeat only for a run that starts on fresh counters and ends inside one UTC minute: a second run inside the
   same minute answered 200 to 250 calls and 429 to 1750. The times depend on the machine.
8. Before version 1 (a server on an empty store): both probes answer 200, the protected resource metadata names the
   resource and the issuer, `POST /mcp` with no token answers 401 with `WWW-Authenticate`, and a signed-in call answers
   `configuration_unavailable`. The engine command line's dry runs of guide S1 answer as the guide says: Balanced with
   the small-team overlay in activation mode is valid with no warning, the CoE baseline is valid with its two V29
   warnings, and a binding patch in the schema's format over Balanced is valid with no V37 warning.
9. Stop. Nothing left running.

The server refuses to start with the development identity switched on in the Production environment or on a URL that
is not loopback. Both refusals were checked by hand in the same run. Expiry was not tried by hand, because the local
server keeps real time and the demo's rules wait five or ten days; the tests move a manual clock instead.

### Read speed (wave 4, 2026-11-25)

The three heaviest reads of the web app, timed on the SQLite store by `ReadSpeedTests` (opt in with
`PRODUCT_TIME_READS=1`; the numbers below are from WSL Ubuntu on the author's machine, so the times depend on it).
Each database read is one round trip on Azure SQL. "Off" reads row by row, as before wave 4 (`EngineRuntime.BatchReads`
false, the same reads the wave 3 code made: 767, 616, 633, 76 and 75 on the demo estate); "on" is the read cache and the
key set reads of document 25, B25-23. The 2,000-application estate is the demo estate with its applications copied, each
with its owners, decisions, reviews, documents, deployments, changes, components, links, dependencies and data uses, and
pending approvals on every fourth copy (`ScaledEstate`).

| Read (persona) | Demo estate, 48 apps: reads off / on | Time off / on | 2,000 apps: reads off / on | Time off / on |
|---|---|---|---|---|
| Overview, `get_portfolio_summary` (Mateo) | 766 / 22 | 141 ms / 53 ms | 27,099 / 22 | 45.1 s / 1.34 s |
| My work, `get_my_queue` (Nadia, owner) | 615 / 29 | 109 ms / 38 ms | 21,133 / 30 | 46.8 s / 1.12 s |
| My work, `get_my_queue` (Owen, platform admin) | 632 / 29 | 97 ms / 55 ms | 21,564 / 30 | 49.3 s / 1.25 s |
| Applications, `find_applications` (Priya) | 74 / 6 | 23 ms / 10 ms | 79 / 6 | 231 ms / 86 ms |
| Applications by owner, `find_applications` (Priya) | 73 / 6 | 28 ms / 8 ms | 2,079 / 6 | 15.2 s / 389 ms |

What it means: on Azure SQL each of these reads is one round trip, so over a slow connection a page's time follows the
count of reads, not the time to build the answer. The count no longer grows with the estate:
`ReadSpeedTests.TheHeavyReadsMakeAFewDatabaseReadsWhateverTheSizeOfTheEstate` holds every read to a ceiling (25, 34,
34, 8 and 8) on the demo estate and on 480 applications, and
`ReadSpeedTests.TheBatchedReadsGiveTheSameAnswersAsReadingRowByRow` shows 17 reads answer the same with the cache on and
off.

### The demo director

Run by hand on 2026-11-24 against a local server on a fresh SQLite store seeded with `seed/demo-estate.json` (681
rows), with the development identity on: `seed/demo-director.mjs` (README, "Make the demo's open work live") made 30
steps live as the fourteen demo personas: 4 intake requests (2 submitted with tier 2 answers, 1 drafted by Claude Code
through the MCP endpoint), 11 change requests (9 submitted), an architecture review request, 9 decision proposals, 2
owner changes and an owner leave, and 2 documents to confirm. A second run did nothing (30 steps already there), and a
run after a half-done one finished only the missing submits. Every persona except the auditor then had work of their
own in `get_my_queue` and `list_approvals`; the auditor answers nothing under Balanced and sees the open approvals of the
estate. Each of the 23 approval requests asked of a persona was tried with `answer_approval` and `validateOnly` (a dry
run): 21 would count, and the change manager's 2 on his own tier 3 proposals were refused by `no-self-approval-tier-3`,
as the rules say. The demo mode (a demo install, the operator's token and `X-Product-Demo-Persona`) was not run,
because no demo install exists yet; with a token the server refuses, the director stops before it writes anything.

**Since then (2026-11-24):** Balanced follows the process documents, so a
fresh run asks differently. An intake opens three consultation cards beside the platform admin's card; the ownership
steps ask the successor by name and the sponsor for the business owner role that changes hands (Safety Findings
Tracker: Ines Varga; Claims Triage Agent: Iris Calloway); the new tier 3 data use on Field Knowledge MCP Server asks
Kai Ostrowski's deputy and the security reviewer. The regenerated estate has 724 rows. The director was not run again
against this build; its own checks and step code are unchanged apart from the new proposal and the summary of who is
asked. The runner sends a card that names a person (Asked Principal) to that person only.

**Data for every wave 2 page (2026-11-24).** The estate (733 rows) and the director now give every page of the web app
rows to show (README, "Make the demo's open work live").
Run by hand on a fresh SQLite store seeded with `seed/demo-estate.json`, against a Release server on port 5081 with the
development identity on: 52 steps done, none failed; a second and a third run did nothing (52 already there). A
script then read each page's tools as the persona who uses it, and each had rows: the Demand page four scored requests
and one priority waiting; Deployments three AI-only code approvals, one recorded from Claude Code through the MCP
endpoint; Places thirteen targets with People Test; the gap check one finished run (33 items in one final page) with
two open claims, one claimed, one dismissed and one vanished (Timesheets); AI activity, for the AI lead, drafts taken
forward, edited first, withdrawn and waiting, three refused calls, one AI answer (a recommendation only) and three
AI-only code approvals; Reviews Quote Builder's architecture review done with the Design gate met; Changes the closed
Legacy Timesheet App removal change; the Retirement tracker of Legacy Timesheet App with steps 1 to 5 done and the
vanished component waiting; the exceptions register two standing exceptions and one waiting; the audit trail and
the Invoice Matching evidence pack. Every live step of the demo story was then tried with `validateOnly` as its
persona, and each would be written, except the data owner's own answer on the tier 3 data use, which
`no-self-approval-tier-3` refuses, as the story says. The demo mode was not run (no demo install).

**The data taxonomy and the wave 3 screens (2026-11-25, data taxonomy slice 5).** Run by hand on a fresh SQLite store on
WSL's own disk, seeded with `seed/demo-estate.json` (906 rows) and the demo binding, against a Release
server on 127.0.0.1:5081 with the development identity on and `--Product:WebApp:Demo=true`: the director made 63 steps
live as the sixteen personas, none failed, and a second run created nothing (63 already there; the owner change steps
first proposed their decision again, because the engine's statement no longer names IDs, and now find it by its
proposed changes). A script then read each screen's tools as the persona who uses it, and every one had rows: the six
wave 3 screens (Data with its domains, locations, mappings, uses and gaps; the data domain record; Configuration with
its rules, versions, drafts, presets and the draft record; Roles and access; Guidelines; Measures; Admin) and the
earlier screens. The Gaps tab counted DC-01 2, DC-03 2, DC-04 3, DC-05 4, DC-06 1, DC-08 2, DC-09 1, DC-10 13, DC-11 1
and DC-12 2 (each count is true of the estate: a recorded leave makes DC-04 3, four applications that use AI and declare
no data use make DC-10 13, and DC-13 shows once a retirement is proposed complete). The
live steps were then run as their personas: Visit Report Agent's use of safety findings with AI went from 0 of 2 to 1
of 2 when Kai Ostrowski approved and to allowed when Noor Safadi approved; Priya Castell's save of Expense Assistant's
AI use of bank details was refused (422 `rule_violation`, naming the rule and where it is set); Jonah Pereira accepted
Yusuf Brennan's configuration change, and version 2 was in force; a proposed retirement of Legacy Timesheet App made
DC-13 appear. The server was stopped by its PID. The demo mode was not run (no demo install).

**The role-by-role accessibility pass (2026-11-25).** `ui/harness/scripts/a11y.mjs --app` now opens
every page of the routes (`ui/harness/scripts/a11y-pages.mjs`, 69 pages and views, each record page on a demo record;
the claim, gap check run and configuration draft the director makes are read first through the REST API), as three
development personas (`--personas`, default `demo-lena`, `nadia-web`, `owen-web`: a requester, an application owner and
the platform admin), at 1280 and 390 pixels, and on each page opens the first action sheet the persona sees: the sheet
gets the same checks, plus focus moving into it, staying in it, Escape closing it and focus going back to its trigger.
A unit test (`src/app/a11yPages.test.ts`) fails when a page kind of the routes is not on the list. Run on a fresh
SQLite store on WSL's own disk, seeded with `seed/demo-estate.json` and directed (63 steps, none failed),
against a Release server on 127.0.0.1:5084 with the development identity on and `--Product:WebApp:Demo=true`, with
Chrome on Windows: 612 page and sheet states (420 page loads, 192 open sheets), 12 with issues, all of them
`aria-controls` on a collapsed disclosure that names a panel not rendered yet (the register's Owner filter, and each
guideline's toggle on the Guidelines page). Every sheet passed. The first run, three personas in parallel, passed the
REST API limit of 300 requests a minute per caller (`Product:Limits:ApiPerMinute`), and the 429 answers left KPI tiles
on Demand, Portfolios and the Data gaps tab marked as loading for good; the run that counts was made with that limit
off (`--Product:Limits:ApiPerMinute=0`, this server only). The server was stopped by its PID.

**The wave 4 accessibility pass (2026-11-25).** The page list (`ui/harness/scripts/a11y-pages.mjs`) now has 87 pages
and views: the Teams list (all, committees, support teams, a text search), three team pages (a committee, a support
team, a role group), the register's uses AI filter, the audit trail by client, and one entry for each wave 4 sheet that
is not a page's main action. Such an entry (`sheetOnly`) opens only its own trigger: "What depends on it?" on Application
360, a place and a data location, "Record a decision" on Measures, "Import data domains" on Configuration, "Ask again"
on My work and the decision page, and "Send again" on Admin. `--clock-days <n>` moves the page's clock ahead, so "Ask
again" (a request's last 48 hours) can be checked on a fresh store. Run on a fresh SQLite store (balanced preset, the
demo binding and estate, 909 rows), directed (64 steps, none failed), against a Release server on 127.0.0.1:5084 with
the development identity, `--Product:WebApp:Demo=true`, `--Product:Directory:Mode=Demo` and the API limit off as in wave
3, with Chrome on Windows: 744 page and sheet states for the three personas (522 page loads, 222 open sheets) passed
the script's own checks, and so did "Import data domains" as `config-admin-web` and "Ask again" as `demo-lena` with
`--clock-days 4`. The harness target passes too. The same pass with axe-core 4.13 (`--axe`) found two rules on pages
built before wave 4, none on the Teams pages or any sheet: `link-in-text-block` (links inside a sentence drawn with
`font-normal`, no underline and 1.12 to 1 against the text around them, on My work, Approvals and a data domain) and
`target-size` (16 px links stacked 2 px apart in link-only lists, and the Timeline's 10 to 12 px markers at 1280 px).
Not reached: "Send again" (a local run has no card that needs a look) and "Ask again" on the decision page (its demo
decision, DEC-0101, is accepted). The server was stopped by its PID.

### Security posture today

- Production sign-in is Microsoft Entra ID JWT bearer with app roles. The development identity lives only in the local
  server and needs the Development environment, an explicit switch and a loopback request.
- The engine runs as the caller. People get product roles only through Entra groups bound in the configuration.
- The web app's tokens are the person entry only for the client IDs the host declares, matched against the token's
  verified `azp`; any other client is an AI client acting for the person and passes the Known Client check. Agent users
  and agent identities hold no role on any entry, whether a Principal Reference or the token's own Agent ID facet
  claims (`xms_sub_fct`, `xms_act_fct`) say so, and an agent client is never the person entry. On the person entry the
  REST API serves the person's write tools (D-36) through the same Tool Policy roles and engine guards as the MCP
  endpoint; on the REST API an AI client's write tool answers `not_found`, so an AI client writes only through the MCP
  endpoint. `answer_approval` (D-38) is served only on the person entry: an AI client gets `ai_cannot_do_this` on the
  REST API and on the MCP endpoint, which does not list it.
- AI clients never approve: no tool an AI client is served approves; a person answers through `answer_approval` only
  after the engine's approval answer check allowed it for the signed-in person the request asks; the approval outcome
  endpoint and the runner's routes take only the approval runner's app-only token and then require its current Workload
  Grant, which an agent identity never holds; the engine refuses an approval outcome from any other actor; an answer from a responder whose Principal Reference is
  not a user is stored as recommendation only, in the engine and again at the endpoint; an answer whose responder is a
  workload (the runner itself or a Workload Grant holder) is refused; an owner role's answer counts only from a current
  holder of that owner role, as the product's own Owner Assignments show. The engine counts one person once per
  requirement, so a rule that needs two answers needs two different people, and it never counts an answer given after
  the request expired. Only the approval runner may run the engine's approval refresh, which writes only expired,
  cancelled, Counts Toward Requirement and new pending records, never an answer.
- One person has one ID. The outcome endpoint takes the approver's object ID only in the 36-character form with hyphens
  and stores it in lower case, so another spelling of an agent's, a workload's or the proposer's ID cannot pass the
  checks that tell people apart. The engine compares principal IDs in one form too, for the count of one person once
  and for every separation rule. An answer that says it was given before its request was made is refused, and a second
  person's same answer that races the first is a conflict.
- Every signed-in caller's requests are limited per route group and minute, counted in the store, never in a server's
  memory; over the limit the answer is 429 with `Retry-After`. On a demo install, the operator's requests as the demo
  personas are counted apart, under `Product:Limits:DemoOperatorPerMinute` (default 1,200 a minute; document 24,
  B24-21). Every answer refuses framing, referrers and
  cross-origin reads, and every answer outside the web app carries a Content-Security-Policy that lets nothing load.
- A server failure on any route answers a failure envelope with only the correlation ID; outside Development the log
  keeps the exception's type and stack, never its message. A request the client canceled, or whose body it cut off,
  is one information line, never a failure. Outside Development every answer carries Strict-Transport-Security, and in
  any environment every answer to a request that came over https (directly or through the platform's front end) does,
  except to a loopback host. The MCP endpoint answers 401 with its challenge to any message before sign-in. Telemetry
  names every caller by a salted hash, with a salt made for the process when none is configured.
- No route forwards a caller's token. The directory reads exchange it at the Microsoft Entra token endpoint for a new
  token for Microsoft Graph or the Power Platform API, and only that token leaves the service. Their request URLs name
  people, so they stay out of the request logs and, with Azure Monitor on, out of the HTTP client traces.
- Step-up sign-in for a person's approval answer is the host setting `Product:WebApp:StepUp` (Off by default and in
  the demo; FreshSignIn or AuthenticationContext). With it on, an answer without the fresh sign-in answers
  `step_up_required` (401) with a claims challenge and writes nothing (document 26, B26-9;
  `WebAppApiTests.WithStepUpOnAnAnswerNeedsAFreshSignIn`, `StepUpCheckTests.AFreshSignInMustBeWithinTheMaximumAge`).
- No secrets in files. The Azure SQL connection uses a managed identity; outside Development the server refuses a
  connection string with a password and any configured client secret; the directory reads use a federated identity
  credential that trusts a managed identity.

## Designed but not built yet

- **The notification flow.** The due-state refresh (built) records each reminder, escalation and flag notice with who
  is told and through which channel, as an Audit Entry; no flow sends those messages yet. The timer has not run in a real
  Functions host, and the Azure SQL row security around a final Approval Record does not name the actor kind engine
  (document 25, B25-18, choice 9).
- **Step-up sign-in beyond approval answers.** The pages call the person's write tools (D-36) and answer approvals on
  the decision page (D-38), and the web app's "Acting as" menu sends `X-Product-Demo-Persona` on a demo install
  (D-39). Step-up sign-in covers approval answers only; step-up for tier 3 and configuration changes is not built.
- **The approval runner in a tenant.** The flows are a template (`flows/README.md`). Not checked in a real tenant: a
  secretless way for the flow's HTTP action to sign in (the template uses a certificate in a Key Vault-backed
  environment variable, so its grant has `usesFederatedCredential` false, which no check reads yet), the responder
  output of the Approvals connector, whether group member listing leaves out agent users, and the 28-day wait limit.
  For a product role the runner cannot leave out a group member whose answer already counts.
- **The least Microsoft Graph and Power Platform API permissions** for the directory reads (document 24, spike 14).
- **Deployment.** The azd template in `infra/` has been provisioned as a demo install with fictional data and the
  persona switch on. Still to do for a real install: the bootstrap of version 1 through the API, the client binding
  patch, a run of
  the Azure SQL store tests against a real database (the store keys and the transient retries included), and a check
  that the real Functions worker behaves as the tests' request delegate does. Also not built: the engine endpoints
  other than the approval outcome and the runner's routes, the separate approval writer database principal, ledger
  digest storage and the private data tier option.
- **Typed schema.** Both stores keep each row's columns as JSON beside a few real columns. Of the logical model's 83
  store keys, the database now enforces 80: 3 through unique indexes (version number per configuration set, the
  deployment idempotency key, the audit request key) and 77 through the store key table (`sql/0006-store-keys.sql`).
  The engine's record numbers come from a counter table (`sql/0007-record-numbers.sql`, `host.RecordNumber`; the same
  table in SQLite), which reserves the next number after the highest the table holds (document 25, B25-9). A deployed
  database needs `migrate` to reach schema version 9. 14 of those 80 are held on scoped terms, each to match what the
  engine writes and checks: the 10 configuration alternate keys count active rows only, Technical Component Link Kind
  and External Id counts components with no Retired On, Data Location Data Platform and External Id counts the
  locations in service (no Retired On, and not archived), Data Mapping's key counts open rows (no Ended On, as the
  model's key says), and List Value Key is held as Value List and Key, because every shipped preset uses one value key
  in several lists, which the model's Key within the set would refuse. The other 3 are held back on purpose, because a
  documented engine behavior writes rows they would refuse (`sql/README.md`). A test lists every key, and every
  narrowing, so a new one cannot slip by
  (`LogicalModelConformanceTests.TheStoresHoldEveryStoreKeyOfTheModelButTheOnesHeldBack`). The fully typed schema the
  model generator drafts is the target of a later migration; it still holds the model's keys as stated, so its List
  Value key would refuse the shipped presets.

## Engine and MCP core changes

`ENGINE-CHANGES-NEEDED.md` has each with its reason and workaround.

| Item | Change | State |
|---|---|---|
| 1 | The caller entry, so the web app acts as the person | Done; the REST API uses it for the web app's tokens |
| 2 | A public mapper for the record tools' data | Done; the record endpoint answers through it |
| 3 | A public engine to error map | Done; the record and approval endpoints map through it |
| 4 | A public read-only view of the compiled logical model | Done; the stores' entity catalog reads it |
| 8 | A failure callback on the MCP core | Done; the host logs failures there |
| 11 | `ai_cannot_do_this` only for an AI caller, and the runner's different second answer as a conflict | Done; the outcome endpoint answers the engine's 409 |
| 12 | The approver's principal kind from its Principal Reference | Done; the endpoint keeps its own check as a second layer |
| 13 | One Approval Record per required answer | Done; the runner sends one card per record |
| 14 | Approval expiry and restarts | Done; every claim runs the engine's approval refresh |
| 15 | A person answering their own approval on the product's page | Design needed |
| 19 | A timely answer that reaches the product after the refresh expired its record | Design needed |
| 5 | Pass the resolved actor to the store for `SESSION_CONTEXT` | Optional |
| 6 | Match role bindings by app role value as well as group | Optional |
| 9 | Let the runtime name the operation of a transaction | Optional |
| 10 | Allowed transitions of final rows in a stored procedure | Optional |
| 16 | The engine checks an owner role's answer against Owner Assignments and refuses workload responders | Optional; the endpoint does both before the engine runs |
| 17 | The same outcome from another responder as a conflict | Optional; the endpoint answers 409 first |
| 18 | A warning for a late answer | Optional |
| 7 | Asynchronous storage and live read ports | Later |

## Package licenses

Every package this folder restores is listed in `../../THIRD-PARTY.md` (section ".NET: adapters/azure"), and every
package of the web app in its section "Node: ui/harness only". Two transitive Microsoft packages carry the Microsoft
Software License Terms instead of MIT, Apache 2.0 or BSD: the native SQL Server network layer that
Microsoft.Data.SqlClient brings (Microsoft.Data.SqlClient.SNI.runtime), and the sign-in broker runtime that the
SqlClient Azure extension brings (Microsoft.Identity.Client.NativeInterop). They are restored from NuGet under
Microsoft's terms and are not part of this repository's MIT-licensed source. Giving SqlClient its token through a
callback in place of the Azure extension would remove the broker runtime from every build; that change needs a live
Azure SQL database to verify and is not made yet.

The accessibility check (`ui/harness/scripts/a11y.mjs`) can also run axe-core from a file the person running it
provides. axe-core is licensed MPL-2.0, so it is never a dependency of any package here.
