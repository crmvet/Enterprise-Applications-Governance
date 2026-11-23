# Status of the Azure binding

As of 2026-11-23.

In short: this is early work, not a release. The binding builds clean, every test in `Product.Azure.sln` passes, and
the product runs end to end on one machine over SQLite with the development identity. Nothing has been deployed to
Azure yet, and the Azure SQL store has not yet run against a real database.

## What works today

### Build

`Product.Azure.sln` builds in Debug and in Release with 0 warnings and 0 errors. A clean first build of the Functions
project may show the known AZFW0108 notice (its extensions project is restored during the build); the next build shows
none. The Bicep template in `infra/` compiles with the Bicep CLI with no warnings.

### Tests

| Project | Total | Passed | Skipped | Failed |
|---|---|---|---|---|
| `tests/Product.Azure.Tests` | 61 | 61 | 0 | 0 |
| `tests/Product.Azure.Stores.Tests` | 136 | 93 | 43 | 0 |
| Product.Engine.Core.Tests (net10.0 and net48) | 450 | 450 | 0 | 0 |
| Product.Engine.Configuration.Tests (net10.0 and net48) | 313 | 313 | 0 | 0 |
| Product.Engine.Runtime.Tests (net10.0 and net48) | 54 | 54 | 0 | 0 |
| Product.Mcp.Core.Tests | 100 | 100 | 0 | 0 |

The 43 skipped store tests are 34 Azure SQL cases, which run only when `PRODUCT_TEST_SQLSERVER` names an empty test
database, and 9 cases that only the row stores support (row versions and a second create of one ID), which the
in-memory store skips. The engine and MCP core suites are unchanged by this folder; they are listed because the
solution builds and runs them.

What the 61 host tests cover:

| Area | Tests |
|---|---|
| Security probes: path spellings of the MCP endpoint on both transports, body size, browser preflights, proxy headers, response headers, the development identity guards, SQL passwords outside Development | 18 |
| Sign-in refusals on every product route, the development identity switch, the core's token rules over HTTP | 9 |
| MCP endpoint: initialize, tool lists per caller, a read and a write tool, resources, the REST facade, the official SDK client | 9 |
| Tool list against the contracts on disk (names, schemas, annotations) through both MCP transports | 6 |
| The engine's refusals: approvals from an AI actor on every path, the record endpoint's role and grant | 5 |
| Microsoft Entra JWT bearer in Production with a test signing key: expired, foreign, unsigned and mis-addressed tokens | 4 |
| The same routes and answers from the Functions wrapper's request delegate as from the local server | 4 |
| Walkthrough replay (below) | 4 |
| Persistence across a restart | 2 |

### Replay parity

The MCP core's walkthrough (`src/demo/walkthrough/transcript.json`, 22 exchanges, 16 of them MCP calls) is replayed
against the local server on SQLite three ways: over raw HTTP, through the official MCP C# SDK client, and with the SDK
server transport behind the endpoint. Each replay is compared with a fresh in-process run of the core on the in-memory
store. Every outcome digest and every answer matches (correlation IDs masked), and both stores hold the same rows at
the end. The one app-only exchange the SDK client cannot make (its handshake is refused by design) is compared by its
403 status. A fourth test checks that the in-process run still answers what the transcript recorded.

### Local run

Run by hand from the README steps, on a fresh SQLite file:

1. Seed: version 1 of the configuration (the Balanced preset with the demo binding) and the walkthrough estate,
   32 rows.
2. Start the server in Development on a loopback address with the development identity switched on.
3. Calls: health answers ready; an unsigned MCP call gets 401; `initialize` answers; the maker persona sees 39 tools and
   none of them approves; `whoami` answers `ai-client-for-person` with the roles maker and requester; a duplicate check
   finds Inspection Scheduler; an intake draft is created and stamped as AI-created for the person; the pipeline
   persona records a deployment (201); the agent persona gets `not_authorized`; a call to `approve_decision` is refused
   because no such tool is served; the approval outcome endpoint refuses the maker with 403.
4. Stop the process (killed, not shut down cleanly) and start it again on the same file.
5. Verify: the same configuration version and content hash; the intake sent again with the same `clientRequestId`
   returns the same record with `created` false; the deployment sent again with the same idempotency key returns 200
   and the same record; the draft shows in the maker's queue and the deployment in `get_deployments`.
6. Stop.

The server refuses to start with the development identity switched on in the Production environment or on a URL that
is not loopback.

### Security posture today

- Production sign-in is Microsoft Entra ID JWT bearer with app roles. The development identity lives only in the local
  server and needs the Development environment, an explicit switch and a loopback request.
- The engine runs as the caller. No route forwards the caller's token anywhere.
- AI clients never approve: no tool approves; the approval outcome endpoint takes only the approval runner's app-only
  token; the engine refuses an approval outcome from any other actor; an answer from a responder whose Principal
  Reference is not a user is stored as recommendation only.
- No secrets in files. The Azure SQL connection uses a managed identity, and outside Development the server refuses a
  connection string with a password.

## Designed but not built yet

- **Web app wiring.** The REST API serves the read tools, `/api/me` and `/api/info`. Writes from a page as a person
  wait for the engine change listed first below; until then a write tool on the REST API answers that it is served on
  the MCP endpoint.
- **Approval cards.** No approval runner is connected, so no card goes to Teams or Outlook. The approval outcome
  endpoint the runner will call is built and tested, with the tests playing the runner. The outbox that would send the
  cards is not built.
- **Gap check and directory reads through on-behalf-of.** There are no on-behalf-of tokens to Microsoft Graph or the
  Power Platform API yet, so `run_gap_check`, `get_role_holders`, `run_owner_check` and `check_role_bindings` answer
  `upstream_unavailable`. `compare_inventory_list` works.
- **Deployment.** The azd template in `infra/` compiles but has not been provisioned. Still to do in a real
  subscription: the app registrations, the bootstrap of version 1 through the API, the client binding patch, a run of
  the Azure SQL store tests against a real database, and a check that the real Functions worker behaves as the tests'
  request delegate does. Also not built: the engine endpoints other than the approval outcome, the separate approval
  writer database principal, ledger digest storage and the private data tier option.
- **Typed schema.** Both stores keep each row's columns as JSON beside a few real columns. They enforce 3 of the
  logical model's 75 store keys (version number per configuration set, the deployment idempotency key, and the audit
  request key); the engine checks the other 70 from its facts, and a test lists them so a new one cannot slip by. The
  fully typed schema the model generator drafts is the target of a later migration.

## Open engine and MCP core changes

These are written up with reasons and workarounds in `ENGINE-CHANGES-NEEDED.md`. Nothing under `engine/` or `mcp/`
was changed. Each has a workaround in this folder, so none blocks what is listed above as working.

| Item | Change | Weight |
|---|---|---|
| 1 | Let the host choose the caller entry, so the REST API can act as a person and not as an AI client | Needed for web app writes |
| 12 | Read the approver's principal kind from its Principal Reference in the engine's approval guard | Needed; the endpoint does it today |
| 2 | A workload entry for the record tools, or a public mapper for their data | Useful |
| 3 | Make the engine to error map public | Useful |
| 4 | A public read only view of the compiled logical model | Useful |
| 8 | A failure callback on the MCP core so operators see why a request failed | Useful |
| 5 | Pass the resolved actor to the store for `SESSION_CONTEXT` | Optional |
| 6 | Match role bindings by app role value as well as group | Optional |
| 9 | Let the runtime name the operation of a transaction | Optional |
| 10 | Allowed transitions of final rows in a stored procedure | Optional |
| 11 | Map an AI only reason to `ai_cannot_do_this` only for an AI caller | Optional |
| 7 | Asynchronous storage ports | Later |

## Package licenses

Every package this folder restores is listed in `../../THIRD-PARTY.md` (section ".NET: adapters/azure"). Two transitive
Microsoft packages carry the Microsoft Software License Terms instead of MIT, Apache 2.0 or BSD: the native SQL Server
network layer that Microsoft.Data.SqlClient brings (Microsoft.Data.SqlClient.SNI.runtime), and the sign-in broker
runtime that the SqlClient Azure extension brings (Microsoft.Identity.Client.NativeInterop). They are restored from
NuGet under Microsoft's terms and are not part of this repository's MIT-licensed source. Giving SqlClient its token
through a callback in place of the Azure extension would remove the broker runtime from every build; that change needs
a live Azure SQL database to verify and is not made yet.
