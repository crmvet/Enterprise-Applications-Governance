# adapters/azure: the Azure binding

The product's first release is designed to run on Azure, in the client's own subscription: one Azure Functions app on
the Flex Consumption plan hosts the API, the product's MCP server and the engine, over Azure SQL Database with ledger
tables and row-level security, behind Microsoft Entra ID sign-in. The same app serves the product's web app under
`/app`. This folder is that binding. It is early work, not a release, and nothing from it is deployed: today it runs on
one machine over SQLite with a development identity (`STATUS.md` says what works and what is not built yet). It adds
hosts and stores around the code that already exists and uses only its public surface:

- the engine (`../../engine`), a portable library whose runtime talks to data only through two storage ports,
  `IRecordStore` and `IConfigurationStore` (`Product.Engine.Runtime/Ports/StorePorts.cs`);
- the MCP server core (`../../mcp/Product.Mcp.Core`), which holds every tool, the envelope, Tool Policy, the Known
  Client check, the caller entries (the AI client on the MCP endpoint, the person through the web app) and the
  in-process backend that runs the engine in the same process.

What the binding still needs from those two projects, and how it works around each need, is in
`ENGINE-CHANGES-NEEDED.md`.

The design is in documents 22 (logical data model, section 11 for Azure SQL), 24 (MCP surface, sections 4, 5 and 9.2),
25 (engine, section 12 for the Azure adapter) and 26 (security model, sections 5.6, 7.5, 8 and 9.4).

## Projects

| Project | What it is |
|---|---|
| `Product.Azure.Stores` | The storage ports implemented once over a small database gateway: overlay transactions (reads see their own writes, nothing reaches the database before Commit), optimistic row versions, the two unique keys, `StoreConflictException`, row IDs, the entity catalog read from the logical model compiled into the engine core, the rate limit counter, the caller context the SQL store writes to `SESSION_CONTEXT`, and seeding for local runs and tests. `StoreContracts.cs` is the contract the hosts bind (`IProductStore`, `ICounterStore`, `IStoreSeeder`, `RowStoreOptions`, `RowIds`, `StoreCallContext`) |
| `Product.Azure.Stores.Sqlite` | The ports over one SQLite file, for local runs, demos and tests |
| `Product.Azure.Stores.SqlServer` | The ports over Azure SQL Database (Microsoft.Data.SqlClient) with the app's managed identity, the caller in `SESSION_CONTEXT`, and the scripts in `sql/` with their migration runner |
| `Product.Azure.Host` | What both hosts share: `AddProduct`, `AddProductStore`, Entra sign-in with app roles, the claims-to-caller mapping, `UseProductPipeline` and `MapProduct` (MCP endpoint, REST API, record endpoint, approval outcome endpoint, health, protected resource metadata), the web app under `/app` (`WebApp/`), the approval runner's routes over the approval outbox (`ApprovalRunner/`), the directory reads on behalf of the signed-in admin (`Directory/`), and endpoint modules (`ProductModules.cs`) |
| `Product.Azure.Server` | The Kestrel host for local runs and containers, with the commands `seed`, `migrate` and `schema-script`, and the development identity (local runs only) |
| `Product.Azure.Functions` | The Azure Functions wrapper (isolated worker, ASP.NET Core integration): one catch-all HTTP function that hands each request to the same pipeline as the local server |
| `tests/Product.Azure.Stores.Tests` | Store semantics and parity with the in-memory store, SQLite across restarts, the same engine calls giving the same rows in memory and in SQLite, the entity catalog, the SQL scripts against their generator and against the logical model on disk (every table, history class and store key), and the Azure SQL cases when a test database is configured |
| `tests/Product.Azure.Tests` | The local server over the in-memory HTTP test server: the web app's files, configuration and person entry, the approval loop with a fake runner, the directory reads over recorded fictional responses and the on-behalf-of exchange with a fake token endpoint, the flow template, the sign-in refusals, the Microsoft Entra JWT path in Production with an in-memory test signing key (expired, foreign, unsigned and mis-addressed tokens, caller kinds per route), the security probes (path spellings of the MCP endpoint on both transports, browser preflights, proxy headers, response headers), the development identity guards, the MCP endpoint, the REST API, the record and approval endpoints, the engine's refusals, persistence across a restart, the tool list against the contracts on disk (names, schemas, annotations) through both MCP transports, the same routes and answers from the Functions wrapper's request delegate as from the local server, and the walkthrough transcript replayed over HTTP and through the official MCP SDK client, compared with an in-process run |
| `sql/` | Azure SQL scripts: tables from the logical model, ledger tables for the frozen and append-only history classes, row-level security on `SESSION_CONTEXT`, the app's database user, and the approval outbox (`0005`) |
| `flows/` | The approval runner as a Power Automate template: two solution-aware cloud flows and the script that writes the unpacked solution for a publisher prefix. See `flows/README.md` |
| `seed/` | The fictional demo seed for local runs: a client binding patch and the walkthrough estate |
| `infra/`, `azure.yaml` | The Azure Developer CLI template: Functions Flex Consumption with a system-assigned managed identity (and a user-assigned one for the directory reads, when they are on), Azure SQL with Entra-only authentication, Application Insights, and a pre-package hook that builds the web app |

`Product.Azure.sln` holds these projects and the engine and MCP projects they build on.

## How it maps to the design

### One service, two hosts, one mapping

`MapProduct` maps every endpoint once. The local server calls `app.UseProductPipeline()`. The Functions app builds the
same pipeline with `ProductPipeline.Build(services)` and calls it from one function whose route is `{*path}`, with the
route prefix set to empty in `host.json`, so both hosts answer the same paths with the same code (document 25,
section 12.1).

| Path | Who calls it | Token | What runs |
|---|---|---|---|
| `POST /mcp` | MCP clients such as Claude Code | Delegated, scope `mcp.access` | The MCP server core, stateless, through its in-process backend |
| `GET`, `DELETE /mcp` | | | 405: no server stream, no session |
| `GET /.well-known/oauth-protected-resource/mcp` (and the root form) | MCP clients | None | The core's protected resource metadata (RFC 9728) |
| `POST /api/{tool-name}` | The web app, or another API client | Delegated, scope `api.access` | The same tool and contract as on MCP (document 24, section 9.2), as the person when the token's client is the web app. Read tools only: the four pages are read-only |
| `GET /api/me`, `GET /api/info` | The web app | Delegated, scope `api.access` | The caller's name, actor kind and roles and the configuration in force; engine info |
| `GET /app/`, `GET /app/{path}` | Browsers | None: the page signs in itself | The web app's built files; every page route answers the page. Only when `Product:WebApp:Enabled` |
| `GET /app/config.json` | The web app | None | The page's runtime configuration, written from the host's settings: the API base and the sign-in (no secret) |
| `POST /api/deployments`, `POST /api/deployments/corrections` | Pipeline record steps | App-only, app role `Deployment.Record` | `EngineRuntime.RecordDeployment` as the pipeline's workload identity (document 24, section 4.6) |
| `POST /engine/v1/approval-records/{id}/outcome` | The approval runner | App-only, app role `Approval.Write` | The engine's guarded write as the approval runner (document 25, section 12.2), with the runner's `Idempotency-Key` |
| `POST /engine/v1/approval-runner/claims`, `.../items/{id}/sent`, `.../items/{id}/release` | The approval runner | App-only, app role `Approval.Write` | Lease waiting approval requests from the outbox, report a card as sent, give a request back. Only when `Product:ApprovalRunner:Enabled` |
| `GET /health/live`, `GET /health/ready` | Platform probes | None | Process up; store reachable. No data in the answer |

### Identity and the actor kind

The actor kind comes from the entry point and the token type, never from a field the caller sends (document 24,
section 4.5). Every route maps the validated token to the same caller facts (object ID, tenant, client app, groups,
scopes, app roles, app-only or not), then:

| Entry | Engine caller | Actor kind the engine stamps |
|---|---|---|
| `/mcp` | `CallerIdentity.AiClient` through the in-process backend | AI client for a person, on behalf of the person |
| `/api/{tool-name}`, `/api/me`, from the web app (`azp` in `Product:WebApp:ClientIds`) | The person entry through the in-process backend | Person, with no On Behalf Of. Reads stamp nothing; `SESSION_CONTEXT` names the person |
| `/api/{tool-name}`, from any other client | The AI client entry, as on `/mcp`, with the Known Client check | AI client for a person |
| `/api/deployments` | `CallerIdentity.Workload` | Pipeline, when the identity holds a current Workload Grant for `deployment-recorder` |
| `/engine/v1/approval-records/{id}/outcome` | `CallerIdentity.Workload` | Approval runner, when the identity holds a current Workload Grant for `approval-writer` |

The engine resolves product roles itself, from the configuration in force, on every call (document 26, RA-01). It
matches the caller's Entra group object IDs against the Role Group Binding rows, so the API app registration emits the
groups claim filtered to the groups assigned to the application, and assigns the app roles to those same groups. A
token whose groups overflow is refused with a plain message in this release.

The person entry is the web app's only: the core admits on it only the client IDs the host declares, matched against
the token's verified `azp`, so an AI client holding a token for the same API cannot act as the person. An agent user or
agent identity keeps its kind on every entry and holds no role. The kind comes from the Principal Reference and also
from the token itself: Microsoft Entra Agent ID describes agent tokens with the facet claims `xms_sub_fct` (the
subject) and `xms_act_fct` (the client), 11 for an agent identity and 13 for an agent's user account
([Token claims reference for agents](https://learn.microsoft.com/entra/agent-id/agent-token-claims)). A subject facet
makes the caller an agent user or agent identity whatever the store records, so it holds no role and an app-only agent
identity never holds a Workload Grant; an agent client facet keeps a token off the person entry. Microsoft lists these
facet claims among "optional claims" it supports for agent tokens; whether the API app registration must request them
is not checked against a real tenant yet. The Principal Reference check does not depend on them.

The engine always runs as the caller. No route forwards the caller's token anywhere; the directory reads exchange it
for a new token, below. AI clients never approve: no tool
approves, the approval endpoint takes only app-only tokens with `Approval.Write`, the engine refuses an approval
outcome from any actor but the approval runner and refuses any AI actor's attempt, and an Azure SQL block predicate
refuses the same write when the session's actor kind is not the approval runner. An answer counts only from a user:
when the responder's Principal Reference records an agent user, an agent identity or any other non-user kind, the
endpoint passes that kind to the engine in place of the runner's, and the answer is stored as recommendation only. An
answer whose responder is a workload (the identity posting it, or any principal a Workload Grant names) is refused with
403 and nothing is written. For an owner role the endpoint checks the responder against the application's current
Owner Assignments itself, and records an answer from anyone else as from someone who does not hold the role, so it does
not count whatever the runner reports.

### The development identity (local runs only)

The local server can sign a request in as a fictional persona named in a header, so a person can try the product with
no tenant. It exists only in `Product.Azure.Server`, which the Functions app does not reference, and only when all three
hold: the environment is Development, the switch `Product:DevIdentity:Enabled` is true, and the request comes from a
loopback address with no forwarding header and a loopback host name. If the switch is on in any other environment, on
a URL that is not loopback, or with `ASPNETCORE_FORWARDEDHEADERS_ENABLED`, startup fails. A persona yields the same
claims an Entra token would, so everything after sign-in is the production path. The development claims count only on
a principal this scheme signed in; a token that carries the same claim names gets nothing from them.

### The web app

The four pages (Overview, Application 360, the dependency map and the timeline; `../../ui/README.md`) are one build of
`../../ui/harness` (`npm run build:app`, into `dist/app`). The same app that serves the API serves them under `/app/`,
on the same origin, so the page needs no CORS and its bearer token goes only to `/api`. One build serves any tenant:
the page reads `/app/config.json`, which the host writes from its own settings (`Product:WebApp`): the API base, the
sign-in authority, the web app's client ID and the API scope. It holds no secret.

- **Sign-in.** The page signs in with MSAL for the browser (redirect flow, PKCE) as a single-page application
  registration, and asks for the API's `api.access` scope. Its tokens carry the web app's client ID as `azp`, which
  makes them the person entry above.
- **Headers.** The page and `config.json` are never cached; hashed files under `/app/assets/` are cached for a year.
  The page carries a Content-Security-Policy that allows scripts, styles and calls only from this origin, calls and
  frames to the sign-in host, inline styles (Fluent UI writes its styles at run time), no plug-ins, and no framing by
  another page. Outside Development, with an https public URL, every answer carries Strict-Transport-Security, so the
  page and its sign-in never load over plain HTTP. A link out of the product is rendered only for an https URL.
- **Read-only.** The REST API serves the read tools only, because the four pages are read-only by design. An AI client
  writes through the MCP endpoint, and a person's approval answer goes through the approval card.
- **Hosting.** On Azure the Functions app carries `dist/app` in its package (`wwwroot/app`; `azure.yaml` builds it
  before packaging). A separate Static Web Apps site was not chosen: it is a second origin, which needs CORS, and its
  linked backend sign-in layer would sit in front of an API that validates Microsoft Entra tokens itself.

### The approval runner

An approval answer reaches the engine only from the approval runner (document 23, S1; working default Q19): Power
Automate approval cards from a flow group in the client's tenant (`flows/README.md`). The runner pulls work:

1. When the engine opens an Approval Record, the same database transaction adds a row to the approval outbox
   (`host.ApprovalOutbox` on Azure SQL, `sql/0005-approval-outbox.sql`; the same table in SQLite). It holds IDs only. When
   the engine writes the record's final outcome, the same transaction settles the row.
2. The runner claims waiting requests with a lease (`/engine/v1/approval-runner/claims`). Each item carries a title and
   details the product builds (text people wrote is escaped), the record link, who to ask (the owner role's current
   holders, or the role groups bound to the role) and the configuration keys to answer with.
3. It sends one card per request, reports it as sent, and posts the first answer to the approval outcome endpoint with
   `Idempotency-Key: <recordId>:<responderId>:<respondedOn>`. The engine stores the key as the Audit Entry's request key,
   so the same answer sent again returns the first result. A different answer for a record that already holds a final
   outcome, or an answer given after Expires On, is a 409 before the engine runs.
4. A request the runner cannot send or wait for is given back (`release`), and becomes failed after
   `Product:ApprovalRunner:MaxAttempts`.

Every runner route takes only an app-only token with `Approval.Write`, and then serves only a caller that the
configuration in force resolves as the approval runner: a workload with a current `approval-writer` Workload Grant. The
routes are off until `Product:ApprovalRunner:Enabled` is true; the outbox fills either way. Document 23 has the API push
each request to an HTTP-triggered flow; the runner pulls instead, so the API stays inbound only with no outgoing
credential. That is a proposal for the design.

### Directory reads on behalf of the signed-in admin

`get_role_holders`, `run_owner_check`, `check_role_bindings` (Microsoft Graph) and `run_gap_check` (the Power Platform
inventory) read live services as the person who asked. `Product:Directory:Mode` chooses how:

| Mode | What happens |
|---|---|
| `Off` (the default) | No reader is bound; the four tools answer `upstream_unavailable` |
| `Live` | The on-behalf-of flow (Microsoft.Identity.Web): the person's validated token is sent only to the Microsoft Entra token endpoint, which issues a new token for Microsoft Graph (`https://graph.microsoft.com/.default`) or the Power Platform API (`https://api.powerplatform.com/.default`). Only that new token goes to those hosts, nothing logs a token, and app-only callers, the approval runner and agents never reach the readers. The API app proves itself with a federated identity credential that trusts a user-assigned managed identity of the function app: no client secret, and startup fails outside Development if one is configured |
| `Recorded` | Development only: the same readers over recorded, fictional responses in `Product:Directory:RecordedPath`, with no token exchange and no network |

Live reads need, on the API app registration, these delegated permissions with admin consent: Microsoft Graph
`GroupMember.Read.All`, `User.Read.All`, `User-LifeCycleInfo.Read.All` and `Application.Read.All`, and the Power Platform
API permission `ResourceQuery.Resources.Read` ("Query resources",
[Power Platform API permission reference](https://learn.microsoft.com/power-platform/admin/programmability-permission-reference)).
Whether a smaller set is enough is not verified yet (document 24, spike 14). Microsoft documents that the inventory API
"currently supports only delegated user authentication" and answers a service principal or managed identity with 403
([Power Platform inventory API](https://learn.microsoft.com/power-platform/admin/inventory-api)), and it lists the
Microsoft Entra roles that can see the inventory, such as Power Platform administrator and Global reader
([access requirements](https://learn.microsoft.com/power-platform/admin/power-platform-inventory#access-requirements)).
Without such a role `run_gap_check` says so in plain words. `infra/README.md` has the setup.

### MCP transport

The MCP endpoint is streamable HTTP on one path, stateless: every POST carries one JSON-RPC message and its bearer
token, and the answer is one JSON body (document 24, section 5). In this release the host hands each request to the core's
own JSON-RPC handling (`PassThroughMcpAdapter` behind `IMcpLibraryAdapter`), so the 401 and 403 answers, the per-caller
tool list, Tool Policy and the envelope are exactly what the core's contract tests prove. The tests also connect an MCP
client from the official C# SDK to the endpoint to prove that a third-party client speaks to it. The SDK's server
transport (`ModelContextProtocol.AspNetCore`, stateless mode) is the alternative behind the same seam, chosen with
`Product:Mcp:Transport` = `sdk`: the SDK then speaks the protocol, and every tools and resources request still goes to
the same core and in-process backend. The tests replay the walkthrough through both transports.

### Data tier

Both stores keep one row per engine row, in the port's own shape: the row ID, the configuration key path when there is
one, and the columns as JSON in logical names. On Azure SQL every entity of the first release gets its own table in the
schema of its kind (`config`, `ops`, `audit`), named as in the column map, with its history class applied: append-only
ledger tables for Configuration Set Version, Deployment Record, Evidence Link and Audit Entry; updatable ledger tables
for Configuration Draft, Tier Assessment, Decision and Approval Record; temporal tables for the rest (document 22,
sections 2.3 and 11.2). The two unique keys and the columns the block predicates and the version probe read are kept in
real columns beside the JSON. The fully typed schema the model generator drafts stays the target for a later migration,
once its open questions are settled.

Row-level security reads `SESSION_CONTEXT`, which the SQL store sets on every connection, read-only for the
connection's life: the caller's object ID, the entry, the actor kind, the client app, the role keys, the correlation ID
and the engine operation (document 22, section 11.1). The block predicates freeze final rows, keep approval outcomes to
the approval runner, and let configuration tables change only inside an activation.

## Packages

Every version is pinned in `Directory.Packages.props`, which imports `../../Directory.Packages.props` first.

| Package | Version | License | Used by |
|---|---|---|---|
| Microsoft.Identity.Web | 4.18.0 | MIT | Host: Entra JWT bearer validation |
| Microsoft.Data.Sqlite | 10.0.14 | MIT (native bundle Apache 2.0) | SQLite store |
| Microsoft.Data.SqlClient | 7.1.1 | MIT | Azure SQL store |
| Microsoft.Data.SqlClient.Extensions.Azure | 7.1.1 | MIT | Azure SQL store: managed identity authentication |
| System.Security.Cryptography.Pkcs | 10.0.14 | MIT | Azure SQL store: lifts a vulnerable transitive version |
| Azure.Identity | 1.21.0 | MIT | Functions wrapper: the managed identity credential of the telemetry exporter |
| Azure.Functions.Sdk (project SDK) | 1.0.1 | MIT | Functions wrapper |
| Microsoft.Azure.Functions.Worker | 2.52.0 | MIT | Functions wrapper |
| Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore | 2.1.1 | MIT | Functions wrapper |
| Microsoft.Azure.Functions.Worker.OpenTelemetry | 1.2.0 | MIT | Functions wrapper telemetry |
| Azure.Monitor.OpenTelemetry.Exporter | 1.10.0 | MIT | Functions wrapper telemetry |
| Azure.Monitor.OpenTelemetry.AspNetCore | 1.6.0 | MIT | Local server telemetry |
| ModelContextProtocol | 2.4.0 | Apache 2.0 | Tests: the third-party MCP client. The host gets it too, through ModelContextProtocol.AspNetCore |
| ModelContextProtocol.AspNetCore | 2.4.0 | Apache 2.0 | Host: the optional SDK server transport (`Product:Mcp:Transport` = `sdk`) |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.14 | MIT | Tests |

`../../THIRD-PARTY.md` (section ".NET: adapters/azure") lists these and every transitive package they bring, with each
license read from the package itself. Two transitive Microsoft packages carry the Microsoft Software License Terms
instead of MIT, Apache 2.0 or BSD: the native SQL Server network layer that Microsoft.Data.SqlClient needs on Windows
(Microsoft.Data.SqlClient.SNI.runtime), and the sign-in broker runtime that the SqlClient Azure extension brings
(Microsoft.Identity.Client.NativeInterop). They are restored from NuGet under Microsoft's terms and are not part of
this repository's MIT-licensed source. `THIRD-PARTY.md` explains both and what their terms allow.

## Build

From `src/adapters/azure`, with the .NET 10 SDK (the SDK pin in `src/global.json` applies):

```
dotnet build Product.Azure.sln
```

Pass `--artifacts-path <folder>` to keep the output away from other builds of the shared engine projects. The first
build of the Functions project may warn that its extensions project was restored during the build; later builds do not.

On Windows, unless long paths are enabled, keep the path of the folder that holds `src/` under about 120 characters.
The Functions project builds its extensions project deep under `obj/`, and a longer path stops the build with MSB3030
("Could not copy the file ... because it was not found").

On Windows 11 with Smart App Control on, the build succeeds but running what it built can fail: `dotnet run` or a test
run stops with `System.IO.FileLoadException` and "An Application Control policy has blocked this file" (0x800711C7),
because the product's own assemblies are freshly built and not signed. The Code Integrity event log names the file.
Run the steps below where Smart App Control does not apply, for example on Linux or macOS.

## Run it locally

Everything in this section runs on one machine with no Azure subscription and no sign-in: the local server over a
SQLite file, a fictional demo estate, and the development identity in place of Entra ID. Every name and ID in the demo
is fictional.

### Prerequisites

- The .NET 10 SDK (`src/global.json` pins 10.0.100 and rolls forward to later 10.0 feature bands).
- For the web app only: Node.js 22 or later with npm.
- Nothing else. The SQLite engine ships inside the Microsoft.Data.Sqlite package. `curl` helps for trying the endpoints
  by hand.

Run every command below from `src/adapters/azure`, unless it says otherwise.

### Seed the demo

```
dotnet run --project Product.Azure.Server -- seed --environment Development
```

The seed command writes version 1 of the configuration (the Balanced preset plus `seed/demo-binding.patch.json`: the role
groups, Workload Grants for the approval runner and the release pipeline, and Claude Code as a Known Client) and the
walkthrough estate (`seed/walkthrough-world.json`: people, a portfolio, four applications with owners, two targets,
components and dependencies). The store is `Product.Azure.Server/data/product.db`: `dotnet run` starts the program in
its project folder, and the store goes in a `data` folder under the folder the program starts in. Git ignores it. Seeding
refuses a store that already has a version in force, so delete the file to start over.

Options: `--preset <key>`, `--overlay <key>` (repeatable), `--binding <file>`, `--rows <file>`, `--validated-on <date>`,
and `--Product:Store:Sqlite:ConnectionString="Data Source=<file>"` for another file (pass the same setting to the
server; a relative path is relative to `Product.Azure.Server`). The command runs only in the Development environment.

### Run the server

```
dotnet run --project Product.Azure.Server -- --environment Development --urls http://127.0.0.1:5080 --Product:DevIdentity:Enabled=true
```

That one command runs the product: the MCP endpoint at `http://127.0.0.1:5080/mcp`, the REST API, the record endpoint
and the approval outcome endpoint. The development identity is on for this run only. It needs the Development
environment, the switch on the command line (it is off in every committed file) and a loopback request, and the server
refuses to start if the switch is on in another environment or with a URL that is not loopback. A request names a
fictional persona in the `X-Product-Dev-Identity` header; the personas live in
`Product.Azure.Server/appsettings.Development.json`:

| Persona | Who | Product roles (through the demo's role groups) |
|---|---|---|
| `priya` | Priya Castell, a maker in field operations | maker, requester |
| `nadia` | Nadia Ferreira, business owner of Inspection Scheduler | application-owner |
| `owen` | Owen Achterberg | platform-admin |
| `mateo` | Mateo Brandt, a planner | reader |
| `config-admin` | A configuration admin | configuration-admin, requester |
| `agent` | An agent user (Entra Agent ID) in the owners group | none: it signs in, but `whoami` answers `not_authorized` |
| `nadia-web`, `priya-web`, `owen-web`, `mateo-web`, `config-admin-web` | The same people signed in to the product's web app (a fictional web app client ID) | the same roles, as the person entry |
| `runner` | The approval runner, app-only, app role `Approval.Write` | the approval outcome endpoint only |
| `pipeline` | The release pipeline, app-only, app role `Deployment.Record` | the record endpoint only |

Try it from a second terminal (bash quoting shown):

```
curl -s http://127.0.0.1:5080/health/ready
curl -s http://127.0.0.1:5080/mcp -H 'Content-Type: application/json' -H 'X-Product-Dev-Identity: priya' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"whoami","arguments":{}}}'
curl -s http://127.0.0.1:5080/api/deployments -H 'Content-Type: application/json' -H 'X-Product-Dev-Identity: pipeline' \
  -d '{"idempotencyKey":"local-release-1","businessApplicationId":"00000000-0000-4000-8000-000000000511","artifactName":"InspectionScheduler_managed","artifactVersion":"3.0.0","artifactKindKey":"solution","deploymentTargetId":"00000000-0000-4000-8000-000000000522","finishedOn":"2026-11-23T08:00:00Z","resultMeaning":"succeeded","codeApprovalKind":"person"}'
```

`whoami` answers that Priya acts through Claude Code as `ai-client-for-person` with the roles maker and requester. The
record call answers 201 the first time and 200 with the same record when it is sent again.

### Open the web app

Build the pages once, with Node.js 22 or later, from `src/ui/harness`:

```
npm ci --ignore-scripts
npm run build:app
```

With the server running, open `http://127.0.0.1:5080/app/`. `appsettings.Development.json` switches the web app on and
points `Product:WebApp:RootPath` at `src/ui/harness/dist/app`. With the development identity on, the page offers the
`*-web` personas instead of Microsoft Entra sign-in; pick one, for example Nadia Ferreira, and the Overview opens on the
demo estate. `GET /api/me` answers `person` for her, with the same roles as through Claude Code. To work on the pages
with live reload, run `npm run dev:app` in `src/ui/harness` and open `http://127.0.0.1:4319/app/`: the Vite dev server
proxies `/api` to the local server.

### Try the approval runner

Start the server with `--Product:ApprovalRunner:Enabled=true` as well. When Priya submits an intake, the engine opens an
Approval Record and its outbox row. Ask Claude Code for it (see "Connect Claude Code" below), or send the two MCP calls
yourself, the second with the `recordId` the first answers:

```
curl -s http://127.0.0.1:5080/mcp -H 'Content-Type: application/json' -H 'X-Product-Dev-Identity: priya' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"save_intake_draft","arguments":{"title":"Field inspections app","need":"Inspectors book visits on paper.","proposedKindKey":"power-platform-app"}}}'
curl -s http://127.0.0.1:5080/mcp -H 'Content-Type: application/json' -H 'X-Product-Dev-Identity: priya' \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"submit_record","arguments":{"subjectKind":"intake-request","subjectId":"<recordId>"}}}'
```

The submit answer names the Approval Record and the role it asks (`platform-admin`). Then play the runner with the
`runner` persona (bash quoting shown; IDs from the answers):

```
curl -s http://127.0.0.1:5080/engine/v1/approval-runner/claims -H 'Content-Type: application/json' -H 'X-Product-Dev-Identity: runner' -d '{}'
curl -s http://127.0.0.1:5080/engine/v1/approval-runner/items/<approvalRecordId>/sent -H 'Content-Type: application/json' -H 'X-Product-Dev-Identity: runner' \
  -d '{"claimId":"<claimId>","cardRef":"local-card-1","runnerRunUrl":"https://runner.example.org/runs/1"}'
curl -s http://127.0.0.1:5080/engine/v1/approval-records/<approvalRecordId>/outcome -H 'Content-Type: application/json' -H 'X-Product-Dev-Identity: runner' \
  -H 'Idempotency-Key: <approvalRecordId>:00000000-0000-4000-8000-000000000213:<respondedOn>' \
  -d '{"outcome":"approval-approved","approver":"00000000-0000-4000-8000-000000000213","approverActorKind":"person","approverPrincipalKind":"user","respondedOn":"<respondedOn>","approverHoldsRequiredRole":true,"channel":"outlook"}'
```

`<respondedOn>` is the time of the answer in UTC, such as `2026-11-23T10:00:00.000Z`, after the request and, when the
record has one, before its Expires On. The answer records Owen Achterberg (a platform admin) as the approver; the same
call again returns the first result. The intake's decision is not accepted yet (`decisionAccepted` is false): the new
intake has no business owner, and the answer's warnings name the gate that is not ready.

### Directory reads without a tenant

Add `--Product:Directory:Mode=Recorded --Product:Directory:RecordedPath=../tests/Product.Azure.Tests/Recorded` to the
server command. `get_role_holders`, `run_owner_check`, `check_role_bindings` and `run_gap_check` then answer from
recorded, fictional Microsoft Graph and Power Platform responses, with no sign-in and no network.

### Connect Claude Code

With the server running, register it as an MCP server that signs in as a persona:

```
claude mcp add --transport http product-local http://127.0.0.1:5080/mcp --header "X-Product-Dev-Identity: priya"
```

This changes your Claude Code settings; `claude mcp remove product-local` takes it out again. Then ask Claude Code, for
example, "Which applications does Field operations have, and who owns Inspection Scheduler?" or "Submit an intake for a
new field inspections app for me." Claude Code gets the tools Priya's roles allow, every write it makes is stamped as an
AI client acting for Priya, and it can never approve: no tool approves, and the approval outcome endpoint takes only the
approval runner's app-only token. To act as another persona, add a second entry with another name and header.

Against a real tenant, Claude Code signs in with OAuth instead of the header, as a registered public client that is
preauthorized for the `mcp.access` scope and has `http://localhost:<port>/callback` as a redirect URI:
`claude mcp add --transport http --client-id <client id> --callback-port <port> product https://<host>/mcp`.

### Restart and keep the data

Stop the server (Ctrl+C) and start it again with the same command. The file keeps every row: reads answer as before,
the configuration version is the same, and a write sent again with the same `clientRequestId` (MCP) or idempotency key
(record endpoint) returns the first result instead of writing a second row.

### Run the tests

```
dotnet build Product.Azure.sln
dotnet test --solution Product.Azure.sln
```

The solution holds this folder's two test projects and the engine and MCP core suites they build on.
`tests/Product.Azure.Tests` starts the local server's own `Program` on the in-memory HTTP test server, with a SQLite file
per test, a manual clock and repeatable IDs. It replays the MCP core's walkthrough (`src/demo/walkthrough/transcript.json`)
over raw HTTP, through the official MCP C# SDK client and through the SDK server transport, and compares every exchange
with an in-process run of the core on the in-memory store: outcomes, answers and the rows each store holds at the end.
`tests/Product.Azure.Stores.Tests` runs its Azure SQL cases only when `PRODUCT_TEST_SQLSERVER` holds a connection string
to an empty test database, and skips them otherwise.

From `src/`, `node tools/ci.mjs` runs this build in Release and this folder's two test projects as its `azure` check,
on a developer machine and in the repository's CI workflow alike, with no secret and no database.

This folder's test projects find `src/` from their project folder, so they also run from an output folder elsewhere.
`dotnet test` takes no `--artifacts-path`, so after `dotnet build Product.Azure.sln --artifacts-path <folder>` run each
test app directly, for example `dotnet <folder>/bin/Product.Azure.Tests/debug/Product.Azure.Tests.dll`. The engine and
MCP suites find `src/` by walking up from their output folder, so they need an output folder inside the repository,
which is the default.

### What this does not do yet

- No card reaches Teams or Outlook from a local run: the approval runner is a Power Automate template for the client's
  tenant (`flows/README.md`), and the tests and the steps above play it.
- No live directory reads without a tenant: `Live` needs the client's app registrations and consent; locally, use
  `Recorded`.
- No deployment by the client's admin yet. The template in `infra/` has not been provisioned; the app registrations,
  the bootstrap of version 1 through the API and the client binding patch are steps for a later release. The rest is under
  "Not in this release" below.

## Not in this release

- Writes from the web app. The four pages are read-only by design; a page where a person answers their own approval is
  a proposal for the next design pass (`ENGINE-CHANGES-NEEDED.md`, item 15).
- Approval expiry writes and restarts in the engine, and rules that need more than one answer per requirement
  (`ENGINE-CHANGES-NEEDED.md`, items 13 and 14).
- The bootstrap of version 1 through the API in a real subscription, the engine endpoints of document 25, section 12.2
  other than the approval outcome and the runner's routes, the separate approval writer database principal, ledger
  digest storage and the private data tier option.
