# Status of the Azure binding

As of 2026-11-23.

In short: this is early work, not a release. The binding builds clean, and the product runs end to end on one machine
over SQLite with the development identity: the MCP endpoint, the REST API, the product's web app with its four pages,
the approval loop with a stand-in for the approval runner, and the directory reads over recorded responses. Nothing has
been deployed to Azure yet, and the Azure SQL store has not yet run against a real database.

## What works today

### Build

`Product.Azure.sln` builds with 0 warnings and 0 errors. A clean first build of the Functions project may show the
known AZFW0108 notice (its extensions project is restored during the build); the next build shows none. The Bicep
template in `infra/` compiles with `az bicep build` with no warnings and no sign-in. The web app builds with
`npm run build:app` in `../../ui/harness`, and its 49 unit tests pass (`npm test`).

### Tests

| Project | Total | Passed | Skipped | Failed |
|---|---|---|---|---|
| `tests/Product.Azure.Tests` | 141 | 141 | 0 | 0 |
| `tests/Product.Azure.Stores.Tests` | 150 | 102 | 48 | 0 |

The 48 skipped store tests are the 39 Azure SQL cases, which run only when `PRODUCT_TEST_SQLSERVER` names an empty
test database, and 9 cases only the row stores support (row versions, a second create of one ID), which the in-memory
store skips. The engine and MCP core suites are part of the solution too; the `test` check of `tools/ci.mjs` runs them:
1782 tests, all passing (engine core 453, configuration 318 and runtime 60, on both net10.0 and net48; MCP core 120 on
net10.0).

One host test, `DirectoryOptionsTests.LiveReadsWithoutTheManagedIdentityClientIdFailStartupOutsideDevelopment`, failed
once in an earlier full run with "Cannot access a disposed object". It has passed in every run since, alone and in full
runs. The cause is not found yet.

Known wording issue: in the `Recorded` directory mode, `get_role_holders` still answers `"readFrom": "Entra, live"`
(the MCP core writes that text whatever reader is bound). The data is the recorded, fictional set; nothing is read
live.

What the 141 host tests cover:

| Area | Tests |
|---|---|
| The directory reads: role holders, the owner check, role bindings and the gap check over recorded fictional responses, paging, 429 and 403 answers, foreign links refused, no token in a log or a wrong request (20); the on-behalf-of exchange with a fake token endpoint and the production JWT path (6); startup checks, no client secret anywhere (8); the product's own wiring (2) | 36 |
| Security probes: path spellings of the MCP endpoint on both transports, body size, browser preflights, proxy headers, response headers, the development identity guards, SQL passwords outside Development | 18 |
| The approval runner: outbox rows in the engine's own commit, claims and leases, sent reports, idempotent answers, conflicts, expiry, releases, restarts, the routes closed to everyone but the runner (14); the whole loop through the product's wiring, from an AI draft to a person's answer on a card to the web app's view of it (2) | 16 |
| The web app: off unless switched on, the page, its files and their caching, the Content-Security-Policy, nothing outside its folder, `config.json` for Entra sign-in and for a local run, startup checks, the Functions request delegate (8); the person entry: `/api/me` and `whoami` answer `person`, `SESSION_CONTEXT` names the person, read-only, only the declared web app, agents hold no role (5) | 13 |
| Sign-in refusals on every product route, the development identity switch, the core's token rules over HTTP | 9 |
| MCP endpoint: initialize, tool lists per caller, a read and a write tool, resources, the REST facade, the official SDK client | 9 |
| Tool list against the contracts on disk (names, schemas, annotations) through both MCP transports | 6 |
| The engine's refusals: approvals from an AI actor on every path, the record endpoint's role and grant | 5 |
| The approval responder: the runner never answers as itself, a Workload Grant holder never answers, an owner role's answer counts only from a current owner of the application, a run link is an https URL | 5 |
| A server failure on a product route answers only the correlation ID, and outside Development logs the exception's type and stack, never its message | 2 |
| The approval runner's flow template: it parses, calls only the runner and outcome routes, sends exactly the members each route takes, takes credentials only from environment variables, holds no secret | 5 |
| Microsoft Entra JWT bearer in Production with a test signing key: expired, foreign, unsigned and mis-addressed tokens (4); the Agent ID facet claims: an agent's user account holds no role, an agent identity is never the approval runner, an agent client is never the person entry (3) | 7 |
| The same routes and answers from the Functions wrapper's request delegate as from the local server, and the route table | 4 |
| Walkthrough replay (below) | 4 |
| Persistence across a restart | 2 |

### Replay parity

The MCP core's walkthrough (`src/demo/walkthrough/transcript.json`, 22 exchanges, 16 of them MCP calls) is replayed
against the local server on SQLite three ways: over raw HTTP, through the official MCP C# SDK client, and with the SDK
server transport behind the endpoint. Each replay is compared with a fresh in-process run of the core on the in-memory
store. Every outcome digest and every answer matches (correlation IDs masked), and both stores hold the same rows at
the end. A fourth test checks that the in-process run still answers what the transcript recorded. The MCP endpoint's
answers did not change with the web app's person entry; only the size of the `whoami` output schema in the transcript's
tool list did, because its actor kind now also allows `person`.

### Local run

Run by hand from the README steps on 2026-11-23, with the Release build that the `azure` check of `tools/ci.mjs`
tested, on a fresh SQLite file, with the web app freshly built:

1. Seed: version 1 of the configuration (the Balanced preset with the demo binding) and the walkthrough estate,
   32 rows.
2. Start the server in Development on a loopback address with the development identity, the approval runner's routes
   and the recorded directory reads switched on.
3. The web app: `/app` redirects to `/app/`; the page carries the Content-Security-Policy and is not cached;
   `/app/config.json` offers the five `*-web` personas; a path outside the web app's folder answers 404. In a headless
   browser, the development sign-in as Nadia Ferreira opens the Overview on the demo estate (4 applications, 5 owner
   gaps, which are owner roles with no deputy, and 6 documents missing or stale). A record link opens Application 360
   for Inspection Scheduler, its dependency map shows the 3 applications that depend on it within 3 hops (2 of them
   directly), and the timeline opens. No console error or warning, and no policy violation.
4. The approval loop: Priya, through the MCP endpoint, drafts and submits a change to Inspection Scheduler (stamped as
   an AI client acting for her, AI-created); the engine asks the business owner. No tool approves, and the outcome and
   claim routes answer 403 to Priya's token and to the web app's token. The runner persona claims the request (asking
   Nadia Ferreira), reports the card as sent, and posts Nadia's approval with its Idempotency-Key: the answer counts and
   the engine accepts the decision. The same answer again returns the first result. Priya, on the web app, sees the
   approval as Nadia's and nothing missing; the timeline shows the authorized change window.
5. `/api/me` as Nadia on the web app answers her name, `person` and her role. Owen on the web app reads the platform
   admins from the recorded directory (4 members). A write tool on the REST API answers that the pages are read-only.
6. Stop the process (killed, not shut down cleanly) and start it again on the same file.
7. Verify: the same configuration version and content hash; the approval still Nadia's and nothing missing; the same
   answer replays; the outbox has nothing left to claim.
8. Stop. Nothing left running.

The server refuses to start with the development identity switched on in the Production environment or on a URL that
is not loopback. Both refusals were checked by hand in the same run.

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
  holder of that owner role, as the product's own Owner Assignments show.
- A server failure on any route answers a failure envelope with only the correlation ID; outside Development the log
  keeps the exception's type and stack, never its message. Outside Development every answer carries
  Strict-Transport-Security.
- No route forwards a caller's token. The directory reads exchange it at the Microsoft Entra token endpoint for a new
  token for Microsoft Graph or the Power Platform API, and only that token leaves the service.
- No secrets in files. The Azure SQL connection uses a managed identity; outside Development the server refuses a
  connection string with a password and any configured client secret; the directory reads use a federated identity
  credential that trusts a managed identity.

## Designed but not built yet

- **Approval expiry and restarts in the engine, and more than one answer per requirement.** Late answers are refused at
  the approval outcome endpoint, but an expired record stays pending in the engine, and a rule whose Minimum Count is
  above 1 cannot be met yet (`ENGINE-CHANGES-NEEDED.md`, items 13 and 14).
- **A page to answer an approval.** The four pages are read-only by design; a page where a person answers their own
  approval is a proposal for the next design pass (`ENGINE-CHANGES-NEEDED.md`, item 15).
- **The approval runner in a tenant.** The flows are a template (`flows/README.md`). Not checked in a real tenant: a
  secretless way for the flow's HTTP action to sign in (the template uses a certificate in a Key Vault-backed
  environment variable, which the configuration health check reports as a credential-bearing grant), the responder
  output of the Approvals connector, whether group member listing leaves out agent users, and the 28-day wait limit.
- **The least Microsoft Graph and Power Platform API permissions** for the directory reads (document 24, spike 14).
- **Deployment.** The azd template in `infra/` compiles but has not been provisioned. Still to do in a real
  subscription: the app registrations, the bootstrap of version 1 through the API, the client binding patch, a run of
  the Azure SQL store tests against a real database, and a check that the real Functions worker behaves as the tests'
  request delegate does. Also not built: the engine endpoints other than the approval outcome and the runner's routes,
  the separate approval writer database principal, ledger digest storage and the private data tier option.
- **Typed schema.** Both stores keep each row's columns as JSON beside a few real columns. They enforce 3 of the
  logical model's 75 store keys (version number per configuration set, the deployment idempotency key, and the audit
  request key); the engine checks the other 70 from its facts, and a test lists them so a new one cannot slip by. The
  fully typed schema the model generator drafts is the target of a later migration.

## Engine and MCP core changes

`ENGINE-CHANGES-NEEDED.md` has each with its reason and workaround.

| Item | Change | State |
|---|---|---|
| 1 | The caller entry, so the web app acts as the person | Done; the REST API uses it for the web app's tokens |
| 2 | A public mapper for the record tools' data | Done; the record endpoint answers through it |
| 3 | A public engine to error map | Done; the record and approval endpoints map through it |
| 4 | A public read-only view of the compiled logical model | Done; the stores' entity catalog reads it |
| 8 | A failure callback on the MCP core | Done; the host logs failures there |
| 12 | The approver's principal kind from its Principal Reference | Done; the endpoint keeps its own check as a second layer |
| 13 | One Approval Record per required answer | Needed for rules with Minimum Count above 1 |
| 14 | Approval expiry and restarts | Needed |
| 15 | A person answering their own approval on the product's page | Design needed |
| 5 | Pass the resolved actor to the store for `SESSION_CONTEXT` | Optional |
| 6 | Match role bindings by app role value as well as group | Optional |
| 9 | Let the runtime name the operation of a transaction | Optional |
| 10 | Allowed transitions of final rows in a stored procedure | Optional |
| 11 | Map an AI only reason to `ai_cannot_do_this` only for an AI caller | Optional; the endpoint answers 409 first |
| 16 | The engine checks an owner role's answer against Owner Assignments and refuses workload responders | Optional; the endpoint does both before the engine runs |
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
