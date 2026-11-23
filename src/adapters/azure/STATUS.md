# Status of the Azure binding

As of 2026-11-23.

In short: this is early work, not a release. The binding builds clean, and the product runs end to end on one machine
over SQLite with the development identity: the MCP endpoint, the REST API, the product's web app with its four pages,
the approval loop with a stand-in for the approval runner (rules that need several answers, expiry and restarts
included), and the directory reads over recorded responses. Nothing has been deployed to Azure yet, and the Azure SQL
store has not yet run against a real database.

## What works today

### Build

`Product.Azure.sln` builds with 0 warnings and 0 errors. A clean first build of the Functions project may show the
known AZFW0108 notice (its extensions project is restored during the build); the next build shows none. The Bicep
template in `infra/` compiles with `az bicep build` with no warnings and no sign-in. The web app builds with
`npm run build:app` in `../../ui/harness`, and its 49 unit tests pass (`npm test`).

### Tests

| Project | Total | Passed | Skipped | Failed |
|---|---|---|---|---|
| `tests/Product.Azure.Tests` | 194 | 194 | 0 | 0 |
| `tests/Product.Azure.Stores.Tests` | 210 | 138 | 72 | 0 |

The 72 skipped store tests are the 50 Azure SQL cases, which run only when `PRODUCT_TEST_SQLSERVER` names an empty
test database, and 22 cases only the row stores support (row versions, store keys, a second create of one ID), which
the in-memory store skips. The engine and MCP core suites are part of the solution too; the `test` check of
`tools/ci.mjs` runs them: 1813 tests, all passing (engine core 453, configuration 322 and runtime 71, on both net10.0
and net48; MCP core 121 on net10.0). The engine command line runs the 617 fixtures and the 14 replay records, all
passing.

One host test, `DirectoryOptionsTests.LiveReadsWithoutTheManagedIdentityClientIdFailStartupOutsideDevelopment`, once
failed in a full run with "Cannot access a disposed object", the in-memory test server's report that its service
provider was already disposed. That report says nothing about the startup checks, so the startup failure tests now try
such a start again, up to three times; a start that fails a startup check is never tried again. The cause in the test
server is not found yet.

Known wording issue: in the `Recorded` directory mode, `get_role_holders` still answers `"readFrom": "Entra, live"`
(the MCP core writes that text whatever reader is bound). The data is the recorded, fictional set; nothing is read
live.

What the 194 host tests cover:

| Area | Tests |
|---|---|
| The directory reads: role holders, the owner check, role bindings and the gap check over recorded fictional responses, paging, 429 and 403 answers, foreign links refused, no token in a log or a wrong request (20); the on-behalf-of exchange with a fake token endpoint and the production JWT path (6); startup checks, no client secret anywhere (8); the product's own wiring (2); every directory request carries the marker that keeps it out of HTTP client traces, and the server's tracing leaves marked requests out (2) | 38 |
| Request limits per caller: 429 with `Retry-After` on each route group, one caller's count never limits another or another group, a new minute starts a new count, anonymous requests are never counted, the count lives in the store and survives a restart, the switches, startup checks, no object ID in the counter key | 27 |
| The approval runner: outbox rows in the engine's own commit, claims and leases, sent reports, idempotent answers, conflicts, the engine's expiry of a request and its judgment of a late answer, the refresh batch per claim, releases, restarts of the server, the routes closed to everyone but the runner (15); a rule that needs two answers: two different people, one person counted once, an AI's answer never counted, a rejection that cancels the other card, an expired request refused by the engine and restarted (3); the whole loop through the product's wiring, from an AI draft to a person's answer on a card to the web app's view of it (2) | 20 |
| The approver's ID: an agent user's, the approval runner's or the release pipeline's ID spelled another way (without hyphens, in braces, in parentheses) is refused and nothing is written (4); an agent user's answer in the usual form is a recommendation only, a self-approval is refused however the proposer's ID is spelled, an ID in capitals is stored in lower case, a second person's same answer that races the first is a conflict (4); an answer that says it was given before its request was made is refused (1) | 9 |
| Security probes: path spellings of the MCP endpoint on both transports, body size, browser preflights, proxy headers, response headers, the development identity guards, SQL passwords outside Development | 18 |
| The web app: off unless switched on, the page, its files and their caching, the Content-Security-Policy, nothing outside its folder, `config.json` for Entra sign-in and for a local run, startup checks, the Functions request delegate (8); the person entry: `/api/me` and `whoami` answer `person`, `SESSION_CONTEXT` names the person, read-only, only the declared web app, agents hold no role (5) | 13 |
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
   admins from the recorded directory; a write tool on the REST API answers that the pages are read-only. In headless
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

### Security posture today

- Production sign-in is Microsoft Entra ID JWT bearer with app roles. The development identity lives only in the local
  server and needs the Development environment, an explicit switch and a loopback request.
- The engine runs as the caller. People get product roles only through Entra groups bound in the configuration.
- The web app's tokens are the person entry only for the client IDs the host declares, matched against the token's
  verified `azp`; any other client is an AI client acting for the person and passes the Known Client check. Agent users
  and agent identities hold no role on any entry, whether a Principal Reference or the token's own Agent ID facet
  claims (`xms_sub_fct`, `xms_act_fct`) say so, and an agent client is never the person entry.
- AI clients never approve: no tool approves; the approval outcome endpoint and the runner's routes take only the
  approval runner's app-only token and then require its current Workload Grant, which an agent identity never holds;
  the engine refuses an approval outcome from any other actor; an answer from a responder whose Principal Reference is
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
  memory; over the limit the answer is 429 with `Retry-After`. Every answer refuses framing, referrers and
  cross-origin reads, and every answer outside the web app carries a Content-Security-Policy that lets nothing load.
- A server failure on any route answers a failure envelope with only the correlation ID; outside Development the log
  keeps the exception's type and stack, never its message. Outside Development every answer carries
  Strict-Transport-Security.
- No route forwards a caller's token. The directory reads exchange it at the Microsoft Entra token endpoint for a new
  token for Microsoft Graph or the Power Platform API, and only that token leaves the service. Their request URLs name
  people, so they stay out of the request logs and, with Azure Monitor on, out of the HTTP client traces.
- No secrets in files. The Azure SQL connection uses a managed identity; outside Development the server refuses a
  connection string with a password and any configured client secret; the directory reads use a federated identity
  credential that trusts a managed identity.

## Designed but not built yet

- **A timer for the due-state refresh.** The engine expires approvals and opens restarts in the approval refresh, which
  the runner's claims run. While the runner does not poll, nothing is written as expired (every read already judges a
  pending record past its Expires On as expired), and reminders and escalation (document 23, S2) have no timer at all.
- **A page to answer an approval.** The four pages are read-only by design; a page where a person answers their own
  approval is a proposal for the next design pass (`ENGINE-CHANGES-NEEDED.md`, item 15).
- **The approval runner in a tenant.** The flows are a template (`flows/README.md`). Not checked in a real tenant: a
  secretless way for the flow's HTTP action to sign in (the template uses a certificate in a Key Vault-backed
  environment variable, so its grant has `usesFederatedCredential` false, which no check reads yet), the responder
  output of the Approvals connector, whether group member listing leaves out agent users, and the 28-day wait limit.
  For a product role the runner cannot leave out a group member whose answer already counts.
- **The least Microsoft Graph and Power Platform API permissions** for the directory reads (document 24, spike 14).
- **Deployment.** The azd template in `infra/` compiles but has not been provisioned. Still to do in a real
  subscription: the app registrations, the bootstrap of version 1 through the API, the client binding patch, a run of
  the Azure SQL store tests against a real database (the store keys and the transient retries included), and a check
  that the real Functions worker behaves as the tests' request delegate does. Also not built: the engine endpoints
  other than the approval outcome and the runner's routes, the separate approval writer database principal, ledger
  digest storage and the private data tier option.
- **Typed schema.** Both stores keep each row's columns as JSON beside a few real columns. Of the logical model's 75
  store keys, the database now enforces 70: 3 through unique indexes (version number per configuration set, the
  deployment idempotency key, the audit request key) and 67 through the store key table (`sql/0006-store-keys.sql`).
  10 of those 70 are held on narrower terms than the model states, each to match what the engine writes and checks:
  the 8 configuration alternate keys count active rows only, Technical Component Link Kind and External Id counts
  components with no Retired On, and List Value Key is held as Value List and Key, because every shipped preset uses one
  value key in several lists, which the model's Key within the set would refuse. 3 are held back on purpose, because a
  documented engine behavior writes rows they would refuse (`sql/README.md`); 2 belong to entities that have no table in
  the first release. A test lists every key, and every narrowing, so a new one cannot slip by. The fully typed schema the
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
