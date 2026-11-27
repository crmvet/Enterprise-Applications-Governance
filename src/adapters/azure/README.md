# adapters/azure: the Azure binding

The product's first release is designed to run on Azure, in the client's own subscription: one Azure Functions app on
the Flex Consumption plan hosts the API, the product's MCP server and the engine, over Azure SQL Database with ledger
tables and row-level security, behind Microsoft Entra ID sign-in. The same app serves the product's web app under
`/app`. This folder is that binding. It is early work, not a release: it runs on one machine over SQLite with a
development identity, and on Azure as a demo install with fictional data (`STATUS.md` says what works and what is not
built yet). It adds
hosts and stores around the code that already exists and uses only its public surface:

- the engine (`../../engine`), a portable library whose runtime talks to data only through two storage ports,
  `IRecordStore` and `IConfigurationStore` (`Product.Engine.Runtime/Ports/StorePorts.cs`);
- the MCP server core (`../../mcp/Product.Mcp.Core`), which holds every tool, the envelope, Tool Policy, the Known
  Client check, the caller entries (the AI client on the MCP endpoint, the person through the web app) and the
  in-process backend that runs the engine in the same process.

What the binding still needs from those two projects, and how it works around each need, is in
`ENGINE-CHANGES-NEEDED.md`.

The product documentation in [`../../docs`](../../docs/README.md) covers what this README does not repeat: the
install path on Azure ([`install.md`](../../docs/install.md)), the admin routines ([`admin.md`](../../docs/admin.md)),
connecting Claude Code ([`mcp.md`](../../docs/mcp.md)), the REST API and its error codes ([`api.md`](../../docs/api.md))
and every check ([`testing.md`](../../docs/testing.md)).

The design is in documents 22 (logical data model, section 11 for Azure SQL), 24 (MCP surface, sections 4, 5 and 9.2),
25 (engine, section 12 for the Azure adapter) and 26 (security model, sections 5.6, 7.5, 8 and 9.4).

## Projects

| Project | What it is |
|---|---|
| `Product.Azure.Stores` | The storage ports implemented once over a small database gateway: overlay transactions (reads see their own writes, nothing reaches the database before Commit), optimistic row versions, the two unique keys and the other keys the logical model asks the store to enforce (`StoreKeys.cs`), `StoreConflictException`, row IDs, the entity catalog read from the logical model compiled into the engine core, the rate limit counter, the caller context the SQL store writes to `SESSION_CONTEXT`, and seeding for local runs and tests. `StoreContracts.cs` is the contract the hosts bind (`IProductStore`, `ICounterStore`, `IStoreSeeder`, `RowStoreOptions`, `RowIds`, `StoreCallContext`) |
| `Product.Azure.Stores.Sqlite` | The ports over one SQLite file, for local runs, demos and tests |
| `Product.Azure.Stores.SqlServer` | The ports over Azure SQL Database (Microsoft.Data.SqlClient) with the app's managed identity, the caller in `SESSION_CONTEXT`, retries of transient failures (`SqlTransientRetry.cs`), and the scripts in `sql/` with their migration runner |
| `Product.Azure.Host` | What both hosts share: `AddProduct`, `AddProductStore`, Entra sign-in with app roles, the claims-to-caller mapping, `UseProductPipeline` and `MapProduct` (MCP endpoint, REST API, record endpoint, approval outcome endpoint, health, protected resource metadata), the web app under `/app` (`WebApp/`), the approval runner's routes over the approval outbox (`ApprovalRunner/`), the directory reads on behalf of the signed-in admin (`Directory/`), the request limits per caller and the response headers (`Pipeline/CallerRequestLimits.cs`, `ProductPipeline.cs`), the health probes (`Endpoints/HealthEndpoints.cs`), and endpoint modules (`ProductModules.cs`) |
| `Product.Azure.Server` | The Kestrel host for local runs and containers, with the commands `seed`, `migrate`, `schema-script` and `refresh-due-states` (the engine's daily due-state refresh, once, now), and the development identity (local runs only) |
| `Product.Azure.Functions` | The Azure Functions wrapper (isolated worker, ASP.NET Core integration): one catch-all HTTP function that hands each request to the same pipeline as the local server, and the timer `due-state-refresh` (five past every hour) that runs the engine's daily due-state refresh in the hour `Product:DueStateRefresh:HourUtc` names; off while that setting is unset |
| `tests/Product.Azure.Stores.Tests` | Store semantics and parity with the in-memory store, SQLite across restarts, the same engine calls giving the same rows in memory and in SQLite, the entity catalog, the SQL scripts against their generator and against the logical model on disk (every table, history class and store key), and the Azure SQL cases when a test database is configured |
| `tests/Product.Azure.Tests` | The local server over the in-memory HTTP test server: the web app's files, configuration and person entry, the approval loop with a fake runner, the directory reads over recorded fictional responses and the on-behalf-of exchange with a fake token endpoint, the flow template, the sign-in refusals, the Microsoft Entra JWT path in Production with an in-memory test signing key (expired, foreign, unsigned and mis-addressed tokens, caller kinds per route), the security probes (path spellings of the MCP endpoint on both transports, browser preflights, proxy headers, response headers), the development identity guards, the demo persona switch in the Demo environment and its startup refusals elsewhere (D-39), the MCP endpoint, the REST API, the record and approval endpoints, the engine's refusals, persistence across a restart, the tool list against the contracts on disk (names, schemas, annotations) through both MCP transports, the same routes and answers from the Functions wrapper's request delegate as from the local server, and the walkthrough transcript replayed over HTTP and through the official MCP SDK client, compared with an in-process run |
| `sql/` | Azure SQL scripts: tables from the logical model, ledger tables for the frozen and append-only history classes, row-level security on `SESSION_CONTEXT`, the app's database user, the approval outbox (`0005`), the store keys (`0006`) and the record number counters (`0007`) |
| `load/` | `run-load.mjs`: a load run against the local server with the fictional personas, loopback only, no packages |
| `flows/` | The approval runner as a Power Automate template: two solution-aware cloud flows and the script that writes the unpacked solution for a publisher prefix. See `flows/README.md` |
| `seed/` | The fictional demo seed for local runs and demo installs: a client binding patch that binds all fifteen human roles and holds the demo configuration patch (the data domain tree), the walkthrough estate, the demo estate and its generator, the demo personas (`demo-personas.json`, D-39), and the demo director (`demo-director.mjs`), which makes the demo's open work live through the tools as the personas |
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
| `POST /api/{tool-name}` | The web app, or another API client | Delegated, scope `api.access` | The same tool and contract as on MCP (document 24, section 9.2). From the web app, as the person, with the write tools too (D-36); from any other client, the read tools only |
| `GET /api/me`, `GET /api/info` | The web app | Delegated, scope `api.access` | The caller's name, actor kind and roles and the configuration in force; engine info |
| `GET /app/`, `GET /app/{path}` | Browsers | None: the page signs in itself | The web app's built files; every page route answers the page. Only when `Product:WebApp:Enabled` |
| `GET /app/config.json` | The web app | None | The page's runtime configuration, written from the host's settings: the API base and the sign-in (no secret) |
| `POST /api/deployments`, `POST /api/deployments/corrections` | Pipeline record steps | App-only, app role `Deployment.Record` | `EngineRuntime.RecordDeployment` as the pipeline's workload identity (document 24, section 4.6) |
| `POST /engine/v1/approval-records/{id}/outcome` | The approval runner | App-only, app role `Approval.Write` | The engine's guarded write as the approval runner (document 25, section 12.2), with the runner's `Idempotency-Key` |
| `POST /engine/v1/approval-runner/claims`, `.../items/{id}/sent`, `.../items/{id}/release` | The approval runner | App-only, app role `Approval.Write` | Lease waiting approval requests from the outbox, report a card as sent, give a request back. Only when `Product:ApprovalRunner:Enabled` |
| `GET /health/live`, `GET /health/ready` | Platform probes | None | Process up; store reachable within `Product:Health:ReadyTimeoutSeconds` (default 5), else 503. The answer is a status word only |

### Identity and the actor kind

The actor kind comes from the entry point and the token type, never from a field the caller sends (document 24,
section 4.5). Every route maps the validated token to the same caller facts (object ID, tenant, client app, groups,
scopes, app roles, app-only or not), then:

| Entry | Engine caller | Actor kind the engine stamps |
|---|---|---|
| `/mcp` | `CallerIdentity.AiClient` through the in-process backend | AI client for a person, on behalf of the person |
| `/api/{tool-name}`, `/api/me`, from the web app (`azp` in `Product:WebApp:ClientIds`) | The person entry through the in-process backend | Person, with no On Behalf Of, on every write. Reads stamp nothing; `SESSION_CONTEXT` names the person, and for `answer_approval` also the record answered (`AnsweredApprovalId`) |
| `/api/{tool-name}`, from any other client | The AI client entry, as on `/mcp`, with the Known Client check. Read tools only: a write tool answers `not_found`, and the person-only tools (`answer_approval`, `complete_review`, `complete_change`) answer `ai_cannot_do_this` | AI client for a person |
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
for a new token, below. AI clients never approve: the one tool that answers an approval, `answer_approval` (D-38), is
served only on the person entry, and an AI client calling it on any entry gets `ai_cannot_do_this`; the approval
endpoint takes only app-only tokens with `Approval.Write`; the engine records an approval outcome only from the approval
runner or from the signed-in person the request asks (after its approval answer check), and refuses any AI actor's
attempt; and Azure SQL block predicates refuse the same write outside the approval runner's request and a person's own
answer request, which may write an answer only on the one record it names. An answer counts only from a user:
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

### The demo persona switch (demo installs only, D-39)

In a demo install one person, the demo operator, shows every role. The host runs in the environment named `Demo`, and
`Product:WebApp:DemoPersonas` names the fictional personas: a name, a label, the persona's fictional object ID and its
fictional groups. `Product:WebApp:DemoPersonasFile` names a JSON file with a `personas` list instead (one or the other,
not both). The shipped `seed/demo-personas.json` has one persona per human role, a second configuration admin so a
configuration change one admin proposes is answered by another person, and Marcus Oyelaran, a second architect and change
board member who answers what Elif Dunmore and Tomas Rendel propose: seventeen, played by people of the demo estate, and
`seed/demo-binding.patch.json` binds each persona's groups to its role:

| Persona | Who | Product role |
|---|---|---|
| `lena` | Lena Okoro | requester |
| `priya` | Priya Castell | maker (and requester) |
| `nadia` | Nadia Ferreira | application-owner |
| `kai` | Kai Ostrowski | data-owner |
| `elif` | Elif Dunmore | architect |
| `hugo` | Hugo Brandvold | change-manager |
| `tomas` | Tomas Rendel | change-board |
| `marcus` | Marcus Oyelaran | architect and change-board |
| `mateo` | Mateo Brandt | pmo |
| `dmitri` | Dmitri Halloran | ai-lead |
| `noor` | Noor Safadi | security-reviewer |
| `owen` | Owen Achterberg | platform-admin |
| `yusuf` | Yusuf Brennan | configuration-admin |
| `bianca` | Bianca Ferro | auditor |
| `theo` | Theo Marsh | reader |
| `hana` | Hana Lindqvist | data-governance-lead |
| `jonah` | Jonah Pereira | configuration-admin (the second one) |

- **Teams.** A persona may also name `teams`: fictional groups of the demo estate that bind no role, such as the support
  teams that hold support owner roles. Priya Castell is in Field Apps Support, Jonah Pereira in Finance Apps Support,
  Hugo Brandvold and Theo Marsh in Service Desk, and Owen Achterberg in Platform Team. The demo's directory lists the
  persona among each team's members (the Teams page: `list_teams` and `get_team`, document 24, B24-20), but a request as
  the persona carries only its role groups, so a team never gives a persona a role. The development personas carry the
  same `Teams` for a local run.
- **Who may switch.** Only the person signed in to the product's web app with Microsoft Entra ID: the person entry, a
  token the web app asked for on the REST API. The page sends `X-Product-Demo-Persona: <name>` on a request. On the MCP
  endpoint, for an AI client on the REST API, for an agent and for a workload the header is ignored.
- **What changes.** For that request the persona's object ID and groups replace the operator's in the caller middleware,
  before anything reads them, so the Tool Policy, the engine's guards, the proposal right, the approval answer check and
  `SESSION_CONTEXT` all see the persona. The operator's own groups, app roles and directory roles do not carry over; the
  token's tenant, client and scopes do. `GET /api/me` answers for the persona and names it in `demoPersona` (null
  otherwise). Live directory reads refuse a persona, because the bearer token on the request is the operator's own and
  is never exchanged on a persona's behalf; a demo install uses the `Recorded` mode.
- **The stamp.** Every row and audit entry the request writes names the persona as the actor, with the actor kind
  `person`, and carries the Client App label `Web app, demo operator` (`Product:WebApp:ClientLabel` and ", demo
  operator"). The data model has no column for an operator, so the label is the mark. The operator's own object ID is
  never written.
- **Refusals.** A header that names no persona of the install answers 400 `validation` with the field
  `X-Product-Demo-Persona`, and nothing runs, so an operator never acts as themselves by mistake. Outside the Demo
  environment the header is ignored. A host that names demo personas, inline or as a file, in any other environment
  refuses to start: both hosts check before they build (`ProductDemo.Validate`, also called by the Functions app's
  startup guard), and the web app settings check runs again at startup. Each persona must be fictional: its object ID
  and groups must be patterned placeholders (`00000000-0000-4000-8000-` and twelve digits), which a real Entra object ID
  never is, so no real person or group can be acted as. The web app's client ID must be set, because the switch works
  only on the person entry.
- **config.json.** In the Demo environment `/app/config.json` lists the personas by name and label in `demoPersonas`;
  everywhere else the list is empty. Their IDs stay on the server. The web app then shows an "Acting as" menu marked
  Demo in its top bar (`src/ui/harness/src/app/DemoPersonaMenu.tsx`), with the personas and their roles and
  "Yourself"; it sends the chosen name on every call to the API, and hides the menu when the list is empty.

On Azure, `PRODUCT_DEMO_PERSONAS` turns it on (`infra/README.md`). For the local server in a container or on a machine,
run it in the Demo environment with Microsoft Entra sign-in (the development identity works only in Development) and
pass `--Product:WebApp:DemoPersonasFile=seed/demo-personas.json`; the server carries the seed folder next to its
assembly. Seed the demo with `seed/demo-estate.json` as the rows, so each persona has its Principal Reference and its
name shows, then run the demo director in demo mode, so each persona has work waiting ("Make the demo's open work
live", below).

### The web app

The web app (the four first page designs, Overview, Application 360, the dependency map and the timeline, plus the
work and record pages, D-36; `../../ui/README.md`) is one build of `../../ui/harness` (`npm run build:app`, into
`dist/app`). The same app that serves the API serves it under `/app/`,
on the same origin, so the page needs no CORS and its bearer token goes only to `/api`. One build serves any tenant:
the page reads `/app/config.json`, which the host writes from its own settings (`Product:WebApp`): the API base, the
sign-in authority, the web app's client ID and the API scope, and in a demo install the demo personas' names and
labels (D-39, above). It holds no secret.

- **Sign-in.** The page signs in with MSAL for the browser (redirect flow, PKCE) as a single-page application
  registration, and asks for the API's `api.access` scope. Its tokens carry the web app's client ID as `azp`, which
  makes them the person entry above.
- **Headers.** The page and `config.json` are never cached; hashed files under `/app/assets/` are cached for a year.
  The page carries a Content-Security-Policy that allows scripts, styles and calls only from this origin, calls and
  frames to the sign-in host, inline styles (the Radix primitives under the shadcn/ui components write a style tag at run time when a dialog
  or popover locks page scrolling), no plug-ins, and no framing by
  another page. Outside Development, with an https public URL, every answer carries Strict-Transport-Security, so the
  page and its sign-in never load over plain HTTP. A link out of the product is rendered only for an https URL.
- **Writes.** The web app acts (D-36): on the person entry the REST API serves every tool the Tool Policy gives the
  person, write tools included. Each write goes through the same core and engine as an AI client's on the MCP endpoint
  (Tool Policy roles, rate limit, input schema, the engine's guards, rules and ledger), stamped with the actor kind
  `person` and no On Behalf Of. A person answers an approval request they were asked through `answer_approval` (D-38),
  a tool served only on the person entry: the engine's approval answer check runs first (the person holds the role or
  owner role now, no separation rule keeps the answer from counting, the request is open, a typed reason where needed),
  then the answer is counted and the decision moved as for the runner's answer. An AI client on the REST API gets the
  read tools only: a write tool answers `not_found` (it writes through the MCP endpoint), and `answer_approval` answers
  `ai_cannot_do_this`. The pages call the write tools from their action sheets.
- **Hosting.** On Azure the Functions app carries `dist/app` in its package (`wwwroot/app`; `azure.yaml` builds it
  before packaging). A separate Static Web Apps site was not chosen: it is a second origin, which needs CORS, and its
  linked backend sign-in layer would sit in front of an API that validates Microsoft Entra tokens itself.

### The approval runner

Since D-38 a person answers on the product's decision page (`answer_approval` on the person entry, above), and Teams and
Outlook messages link there. The approval runner (document 23, S1; working default Q19) still brings an answer given on a
Power Automate approval card from a flow group in the client's tenant (`flows/README.md`); answering inside a card is to
become a per-install option. An answer on the page settles the record's outbox row in the same transaction, so the
runner offers no card for it afterwards. The runner pulls work:

1. The engine opens one Approval Record per required answer: a rule with Minimum Count 2 gets two records, so two
   cards. When it opens a record, the same database transaction adds a row to the approval outbox
   (`host.ApprovalOutbox` on Azure SQL, `sql/0005-approval-outbox.sql`; the same table in SQLite). It holds IDs only. When
   the engine writes the record's final outcome (an answer, expired or cancelled), the same transaction settles the row.
2. The runner claims waiting requests with a lease (`/engine/v1/approval-runner/claims`). Each claim first has the
   engine bring the due approvals up to date, as the approval runner: the engine writes expired on records past their
   Expires On and opens a new record for each restart, and cancels the records a settled decision still holds, at most
   `Product:ApprovalRunner:MaxRefresh` decisions a claim (default 50). The claim's answer counts what it did
   (`refresh`). Each item carries a title and details the product builds (text people wrote is escaped), the record
   link, who to ask (the owner role's current holders, leaving out those whose approval already counts for the same
   requirement, or the role groups bound to the role) and the configuration keys to answer with.
3. It sends one first-to-respond card per request, reports it as sent, and posts the first answer to the approval
   outcome endpoint with `Idempotency-Key: <recordId>:<responderId>:<respondedOn>`. The engine stores the key as the
   Audit Entry's request key, so the same answer sent again returns the first result. The engine then counts the answer
   (one person once per requirement, only a person signed in as a user who holds the role, only an answer given before
   Expires On), accepts or rejects the decision, cancels the records it no longer needs, and opens a new record for a
   requirement the answer did not meet. The endpoint's answer names the records opened and cancelled. A different answer
   for a record that already holds a final outcome is a 409: the engine's conflict for a different outcome, and the
   endpoint's own for the same outcome from another responder (`ENGINE-CHANGES-NEEDED.md`, item 17).
4. A request the runner cannot send or wait for is given back (`release`), and becomes failed after
   `Product:ApprovalRunner:MaxAttempts`.

Every runner route takes only an app-only token with `Approval.Write`, and then serves only a caller that the
configuration in force resolves as the approval runner: a workload with a current `approval-writer` Workload Grant. The
routes are off until `Product:ApprovalRunner:Enabled` is true; the outbox fills either way. Document 23 has the API push
each request to an HTTP-triggered flow; the runner pulls instead, so the API stays inbound only with no outgoing
credential. That is a proposal for the design. The approval refresh runs in the runner's claims and in the engine's
daily due-state refresh (the Functions timer `due-state-refresh`, once `Product:DueStateRefresh:HourUtc` is set, or a
platform admin's run on the Admin page), and every read already judges a pending record past its Expires On as
expired.

### Request limits and response headers

Every signed-in caller's requests are counted per route group in a fixed UTC minute, in the store
(`ICounterStore`, the same counter as the Tool Policy limit), never in a server's memory, so every instance sees the
same count (document 26). Over a limit the answer is 429 with `Retry-After`. The defaults (`Product:Limits`): 120
requests a minute on the MCP endpoint, 300 on the REST API, 60 on the record endpoint and 600 on the approval runner's
routes; 0 sets no limit for a group, and `Product:Limits:Enabled` false turns the counting off. The Tool Policy limit per
person and tool still applies to every tool call.

Every answer carries `X-Content-Type-Options: nosniff`, `Cross-Origin-Resource-Policy: same-origin`,
`X-Frame-Options: DENY` and `Referrer-Policy: no-referrer`, and every answer outside the web app a
Content-Security-Policy that lets nothing load, run or frame it; the web app's page keeps its own policy.

### Directory reads on behalf of the signed-in admin

`get_role_holders`, `run_owner_check`, `check_role_bindings` (Microsoft Graph) and `run_gap_check` (the Power Platform
inventory) read live services as the person who asked. `Product:Directory:Mode` chooses how:

| Mode | What happens |
|---|---|
| `Off` (the default) | No reader is bound; the four tools answer `upstream_unavailable` |
| `Live` | The on-behalf-of flow (Microsoft.Identity.Web): the person's validated token is sent only to the Microsoft Entra token endpoint, which issues a new token for Microsoft Graph (`https://graph.microsoft.com/.default`) or the Power Platform API (`https://api.powerplatform.com/.default`). Only that new token goes to those hosts, nothing logs a token, and app-only callers, the approval runner and agents never reach the readers. The API app proves itself with a federated identity credential that trusts a user-assigned managed identity of the function app: no client secret, and startup fails outside Development if one is configured |
| `Recorded` | Development only: the same readers over recorded, fictional responses in `Product:Directory:RecordedPath`, with no token exchange and no network |
| `Demo` | Development or Demo only: `run_gap_check` reads the demo's fictional inventory (`DemoInventoryReader`, below), and role holders and the binding check come from the fictional demo personas and their groups (the persona file on a Demo install, the development identity's personas locally); no Entra read and no token. A Demo install with personas uses it whatever the mode. The owner check and the gap check still answer `upstream_unavailable` (no account state) |

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

**The gap check in a demo (wave 4b).** A demo install, and a local run with the mode `Demo`, bind `DemoInventoryReader`
instead of the Power Platform inventory, so "Run the gap check" can be performed in a demo. Its inventory is the tenant
the demo estate describes: every registered component of a covered kind with an External Id that is not retired, as the
register holds it now; the item of every unregistered claim; and the items of the demo inventory file
(`Product:Directory:DemoInventoryFile`, default `seed/demo-inventory.json` when the deployment carries it, optional:
`{ "items": [ { "externalId", "labelAsSeen", "componentKindKey", "targetAsSeen", "url", "suggestedClaimantObjectId" } ],
"absent": [ "<External Id>" ] }`, fictional IDs and https links only), less every component that already has a vanished
claim and the External Ids listed as absent. A first run opens a claim for each new file item; a second run opens none.
It records "demo: the demo's fictional inventory" as the admin role used, pages by its own token, 25 items a page at
most on every install (so a run shows "Continue the run" as a large tenant does), and refuses an environment filter in
plain words. Naming the file outside the Development and the Demo environment fails startup. A
named file that is missing is reported on each run. The Functions package carries `seed/demo-inventory*.json` when the
seed has one.

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
real columns beside the JSON. The other keys the logical model asks the store to enforce (business keys such as
Record Number, the configuration keys within the set in force, alternate keys) are rows of a store key table
(`host.StoreKey`, `sql/0006-store-keys.sql`; the same table in SQLite) that holds the SHA-256 of each value, written in
the same transaction as the row, so the database refuses a second row with the same value and the engine's retry sees
the first result. `sql/README.md` lists the three keys held back and why. Transient Azure SQL failures are retried
as Microsoft's guidance describes: a new connection, at least 5 seconds before the first retry, twice as long each
time, at most 60 seconds, and never a commit that may have reached the server. The fully typed schema the model
generator drafts stays the target for a later migration, once its open questions are settled.

Row-level security reads `SESSION_CONTEXT`, which the SQL store sets on every connection, read-only for the
connection's life: the caller's object ID, the entry, the actor kind, the client app, the role keys, the correlation ID
and the engine operation (document 22, section 11.1). The block predicates freeze final rows, keep approval outcomes to
the approval runner, and let configuration tables change only inside an activation.

Every call the row store makes to its database counts as one round trip for the current HTTP request
(`StoreRoundTrips`, started by the pipeline for each request), and the MCP request log line (event 4100, `McpRequest`)
ends with "store round trips N", so a slow page shows whether it read row by row (wave 4b).

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

The demo kit (`../../demo/kit/README.md`) does the steps below in one command: `node src/demo/kit/demo.mjs local up`
builds what is missing or stale, seeds a fresh store with the demo estate, starts the server and runs the demo
director; `demo check` says whether every scenario is ready, `demo refresh` asks expired story approvals again, and
`demo local down` stops the server. From Windows it drives WSL.

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

The seed command writes version 1 of the configuration (the Balanced preset plus `seed/demo-binding.patch.json`: a
fictional role group for each of the fifteen human roles, Workload Grants for the approval runner and the release
pipeline, Claude Code as a Known Client, and the demo configuration patch of the data taxonomy: the 25-row business data
domain tree that `seed/demo-estate.gen.mjs` writes) and the
walkthrough estate (`seed/walkthrough-world.json`: people, a portfolio, four applications with owners, two targets,
components and dependencies). The store is `Product.Azure.Server/data/product.db`: `dotnet run` starts the program in
its project folder, and the store goes in a `data` folder under the folder the program starts in. Git ignores it. Seeding
refuses a store that already has a version in force, so delete the file to start over. It runs only in the Development
environment, or in the environment named `Demo` for a demo install with the demo badge on (`infra/README.md`, "After
deployment").

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
| `demo-lena`, `demo-priya`, `demo-nadia` and the rest: `demo-` and each name of `seed/demo-personas.json` | The seventeen demo personas (one per human role, a second configuration admin, and Marcus Oyelaran, architect and change board, as in "The demo persona switch" above) signed in to the web app | each persona's role, as the person entry. The demo director acts as them. The web app offers all of them except `demo-nadia`, `demo-priya` and `demo-owen`, which are the same people and roles as `nadia-web`, `priya-web` and `owen-web` (`demo-mateo` is Mateo Brandt as PMO; `mateo-web` is a reader) |
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
`*-web` personas and the `demo-*` personas instead of Microsoft Entra sign-in; pick one, for example Nadia Ferreira, and
the Overview opens on the demo estate. `GET /api/me` answers `person` for her, with the same roles as through Claude Code. To work on the pages
with live reload, run `npm run dev:app` in `src/ui/harness` and open `http://127.0.0.1:4319/app/`: the Vite dev server
proxies `/api` to the local server.

### Make the demo's open work live (the demo director)

The seed writes rows as given and stamps nothing, and a row the engine never stamped cannot move: `submit_record` asks
for the record's requester or creator, and the approval rules read the proposer from the same stamps. So the demo
estate holds no open work, and `seed/demo-director.mjs` makes it live through the tools, as the fictional personas of
`seed/demo-personas.json`, through the same core and engine as a person's work in the web app. Every stamp, approval
record and audit entry is then the engine's own. It needs Node.js 22 or later and no packages. From a second terminal,
with the server running on a store seeded with the demo estate:

```
dotnet run --project Product.Azure.Server -- seed --environment Development --rows seed/demo-estate.json
dotnet run --project Product.Azure.Server -- --environment Development --urls http://127.0.0.1:5080 --Product:DevIdentity:Enabled=true
node seed/demo-director.mjs --base http://127.0.0.1:5080
```

It acts as each persona with the development persona `demo-<name>` (the table above), and does Claude Code's work
through the MCP endpoint as `priya`, Claude Code acting for Priya Castell (and as `nadia` for the one mapping it drafts
for her, `--ai-owner-persona`). What it makes, so that every page of the web
app has rows to show:

- **Intake requests.** Lena Okoro and Priya Castell each file one, answer its tier questions (tier 2) and submit it, so
  the platform admin has two to triage; Lena keeps a second one as a draft, and Claude Code drafts one for Priya.
- **Demand.** Mateo Brandt (PMO) scores four requests and proposes the priority of the top one, "Supplier rebate
  tracking", so the Demand page has scored rows and a priority waiting for its business owner. Priya's own request,
  "Weather alerts for outdoor jobs", stays unscored for the live demo.
- **Change requests** for the coming weeks, drafted and submitted by Priya Castell (maker) and Hugo Brandvold (change
  manager): the business owner of a tier 1 or 2 application is asked, and the change manager for Contractor Portal and
  Asset Register (tier 3). Two more stay drafts.
- **Decision proposals,** each by a persona who holds the decision right, and submitted: risk acceptances on Inspection
  Scheduler, Service Desk Portal and Identity Directory Sync, the go-live of Field Knowledge MCP Server, the Customer
  Feedback Bot autonomy level, two portfolio decisions (keep
  Service Desk Portal; replace Crew Shift Mobile, which puts it on the Retirement page as proposed), an exception for
  the Invoice Matching 1.12.1 hotfix, a design decision on the architecture review Priya requests for Claims Triage
  Agent, a decision on the flagged agent Onboarding Buddy Agent, which the AI lead answers, and an exception about the
  Desk Booking calendar connector update (a normal change), which Tomas Rendel proposes and the Change board answers
  (the Teams page shows it waiting on the Change board).
- **A security review request.** Dmitri Halloran (AI lead) requests Customer Feedback Bot's security review before its
  pilot (tool 94); it is due for the security reviewer.
- **Data decisions** (the data taxonomy, D-37), each about a declared use or a copy and sent to the domain's data owner
  by name: Visit Report Agent's use of safety findings with AI (Elif Dunmore proposes; Kai Ostrowski and the security
  reviewer are asked, the demo's 0 of 2), Field Knowledge MCP Server's AI use of asset data (Hugo Brandvold; Ravi Holm),
  Claims Triage Agent's AI use of client cases (Owen Achterberg; Nadia Ferreira), Onboarding Buddy Agent's AI use of
  personnel records (Owen; Hana Lindqvist and the security reviewer), the masked copy of supplier contracts in Finance
  Test (Owen; Clara Voss), and the retention of Legacy Timesheet App's timesheet entries (Owen; Hana Lindqvist, with the
  business owner consulted).
- **A configuration change.** Yusuf Brennan drafts and submits "the AI use rule of Workplace / Visitor log becomes
  never": the configuration admin role is asked (Jonah Pereira answers in the demo, not Yusuf) and the data owner of
  Workplace, Hugo Brandvold, is consulted by name.
- **Owners.** Owen Achterberg proposes Lena Okoro as business owner of Safety Findings Tracker, records that Wren
  Abernathy leaves in about three weeks (owner checks open on her applications), and proposes Nadia Ferreira, her
  deputy, as her successor on Claims Triage Agent.
- **Documents to confirm.** The missing support guides of Contractor Portal and Identity Directory Sync, recorded as
  drafts by an owner, for the other owners to confirm.
- **Work finished before the demo.** Elif Dunmore completes Quote Builder's architecture review with the design
  decision the estate holds as accepted, so the review shows the Design gate met. Hugo Brandvold closes the Legacy
  Timesheet App removal change, whose window has passed, as succeeded, with a link to the removal check as evidence.
- **Places.** Owen Achterberg registers the deployment target People Test (test, pipeline only).
- **The daily due-state refresh.** Owen Achterberg runs it now (tool 86, the run the timer makes each day): it opens the
  next access check of Identity Directory Sync, whose done check came due again on November 23, and sends the day's
  notices. A second run the same day changes and sends nothing.
- **A gap check.** Owen Achterberg compares the recorded Power Platform inventory as one final page: every registered
  component the gap check covers, read from the register, plus four items no application holds. The run is finished:
  four new claims, and one vanished claim for the Timesheets canvas app, which the list leaves out because the removal
  change took it away (the Retirement tracker shows it). Priya claims one item into Inspection Scheduler and Owen
  dismisses one with a reason; the photo copy flow suggested to Priya and a personal test flow stay open for the demo.
- **Claude Code's work for Priya** (local runs only; the AI activity page): a Route Planner change she submits as
  drafted, a Route Planner tier decision whose context and reason she changes before she submits it (the platform
  admin is asked), a change she withdraws, a Shift Swap Requests tier decision left waiting for her, a deployment of
  Route Planner 2.0.0-rc.1 to Field Ops Test recorded from Claude Code with only an AI code approval (flagged: another
  path, AI-only code approval, missing approval), and three attempts the server refuses and records with their rule:
  an approve tool that does not exist (`not_found`), an approval answer, which is served only to a person
  (`ai_cannot_do_this`), and a risk acceptance on Asset Register, which Priya does not own (`not_authorized`). Then its
  data work: a draft data use on Mileage Claims for Priya to confirm, a draft mapping (client cases, a replica in the
  Sales CRM org) for Nadia Ferreira, and Expense Assistant's AI use of bank details, refused when it is saved because
  Payroll's AI use rule is never (`rule_violation`, reason `data.rule-never`, which names the rule and where it is set).

The estate itself holds the facts this work builds on (`seed/demo-estate.gen.mjs`, sections 16 and 17): the data
taxonomy's 22 locations, 28 mappings and 30 confirmed data uses, the owners of data domains and locations with their
accepted ownership decisions, the accepted decisions of the allowed uses, and Onboarding Buddy Agent's tier answer; Legacy Timesheet App
on its way out (the replace and retention decisions, and the removal change authorized for a window that has passed),
Quote Builder's accepted design decision and due architecture review, two standing exceptions with their standard and
review date (Contractor Portal, and Travel Requests with its review overdue), and the two answers on the Invoice
Matching 1.12 authorization: the Finance Approvals Agent's, stored as a recommendation only, and Clara Voss's, which
counts.

Then each role has work waiting in My work and Approvals:

| Persona | Waiting for them |
|---|---|
| `lena`, requester | Two answers as deputy business owner: the owner change that names her business owner of Safety Findings Tracker, and the Fleet Maintenance Log change. Her draft, and her intake waiting on the platform admin |
| `priya`, maker | Claude Code's waiting drafts (the "Toolbox talk sign-off" intake and the Shift Swap Requests tier decision), her draft change and her old intake draft, the Contractor Portal support guide to confirm, the photo copy flow the gap check suggests she claims, and her six changes, her Route Planner tier decision and her intake waiting on others |
| `nadia`, application owner | Three answers: the Inspection Scheduler change, the owner change that makes her business owner of Claims Triage Agent, and the Inspection Scheduler risk acceptance she proposed herself, which Balanced lets her answer at tier 2 (D-40). The support guide to confirm |
| `kai`, data owner | Visit Report Agent's use of safety findings with AI, as data owner of Safety findings: the demo's 0 of 2 card, which the security reviewer answers second |
| `elif`, architect | The Identity Directory Sync risk acceptance Hugo proposed, her own exception and design decision (tier 2), the two intake consultations, the Claims Triage Agent architecture review, and a support guide to confirm |
| `hugo`, change manager | The two tier 3 changes (a typed reason is needed) and the two Service Desk Portal decisions. His own Identity Directory Sync risk acceptance and Field Knowledge MCP Server go-live are listed too, and the tier 3 separation rule keeps his answer from counting, so the demo can show that refusal |
| `tomas`, change board | The Parts Catalog API 2.5 change (he is its business owner), the Desk Booking exception he proposed, on the Change board's card, and his old intake draft |
| `marcus`, architect and change board | The Desk Booking exception on the Change board's card and Claims Triage Agent's design decision, which he answers in S-16.4 and S-06.3, the architects' requests and consultations |
| `mateo`, PMO | The Field Knowledge MCP Server go-live as deputy business owner, and the two intake consultations. His Crew Shift Mobile portfolio decision and the rebate priority wait on others |
| `dmitri`, AI lead | The Onboarding Buddy Agent decision, and his autonomy proposal for Customer Feedback Bot, waiting on the security reviewer |
| `noor`, security reviewer | The Customer Feedback Bot autonomy level, the Visit Report Agent and Onboarding Buddy Agent data uses (restricted data to AI), the Service Desk Portal portfolio decision, the two intake consultations, her own risk acceptance there (deputy business owner, tier 2), and Customer Feedback Bot's security review |
| `owen`, platform admin | The two intake requests to triage and the Route Planner tier decision, his two owner changes waiting on Lena and Nadia and the sponsors, and the Onboarding Buddy Agent decision waiting on the AI lead |
| `yusuf`, configuration admin | His configuration change, waiting on another configuration admin, and the reviews and documents of his applications |
| `jonah`, configuration admin | Yusuf's configuration change to answer: a configuration admin other than its proposer accepts it |
| `hana`, data governance lead | The Onboarding Buddy Agent data use and the timesheet retention, as data owner of People |
| `bianca`, auditor | Nothing of her own: under Balanced an auditor answers no approval and holds no decision right. My work shows the open approvals across the estate |
| `theo`, reader | The owner gap of the application he owns |

- **Run it again at any time.** Every step first reads whether its work is there (by title or statement, on the same
  application, as the same persona) and does only what is missing, so a second run creates nothing and a run after a
  failure finishes the rest. Every write also carries a fixed `clientRequestId`. `--check` writes nothing and prints
  the pending work of each persona; `--json` prints the summary as JSON. `--timeout <seconds>` (default 180) is how
  long it waits for one answer, with room for a slow connection between the server and its database. The summary
  names personas, applications and titles only, never an ID, a URL or a token. Exit code 0 when every step is done, 1 when one failed, 2 when the
  server does not take the personas.
- **Dates count from the day it runs**, so the change windows and the leave are always ahead and Claude Code's
  deployment finished a few hours ago. Approval requests expire as their rules say (five days for a change
  authorization, ten for a risk acceptance or a data use), and a store that already has the work is left as it is.
  For a fresh demo, delete the store, seed it again and run the director again, shortly before the demo, or run
  `demo refresh` (the demo kit), which asks the expired and soon-expiring story requests again.
- **It never answers an approval** and never accepts a decision; the people in the demo do that on the decision page.
  The review it completes and the change it closes build on decisions the estate holds as accepted. It leaves the
  steps the demo story shows live to the demo: Claude Code drafting the crew overtime tracker, the triage of "Weather
  alerts for outdoor jobs" and its score and priority, the two open claims, the vanished Timesheets component and
  "retirement complete", the Supplier Price Agent autonomy level, every data card, and the answer to the configuration
  change.
- **On a demo install** (D-39), pass `--mode demo` and the operator's own bearer token for the web app in
  `PRODUCT_DEMO_TOKEN` (`--token-env` names another variable): sign in to the web app, copy the token from the
  `Authorization` header of a request the page sends to `/api` in the browser's developer tools, and set the variable
  for the one command, for example `PRODUCT_DEMO_TOKEN=<token> node seed/demo-director.mjs --mode demo --base
  https://<the app's host>`. Each request then names the persona in `X-Product-Demo-Persona`, so every write is stamped
  with the persona and "Web app, demo operator". The token is never printed or stored, and it expires after about an
  hour. Claude Code's work is skipped, because the persona switch works only on the person entry; in the demo Claude
  Code works live. The director first checks that the server takes every persona (`GET /api/me` answers for the
  persona) and stops if it does not.
- **Seed rows it replaces.** The demo estate no longer holds open work the engine could not have started: the eight
  proposed application decisions (DEC-0104, DEC-0109, DEC-0110, DEC-0112, DEC-0113, DEC-0119, DEC-0120, DEC-0123), the
  seven submitted changes and their proposed authorizations, and the three draft changes with no creator are gone
  (their IDs and record numbers stay unused, so the rest keep theirs), and the three intake requests still in triage
  are accepted by the platform admin. Accepted decisions now name a decider their rules ask: the security reviewer for
  the three autonomy levels, Kai Ostrowski for the end of his own ownership, and the business owner for the normal
  changes at tiers 1 and 2. Kai Ostrowski is the data owner of Visit Report Agent (deputy Mateo Brandt) and Clara Voss
  of Contract Clause Finder, so the earlier data-use decisions of Mateo Brandt (DEC-0106) and Clara Voss (DEC-0124) fit
  the data-use rule (`seed/demo-estate.gen.mjs`, header and sections 7, 8 and 15). Legacy Timesheet App is no longer
  retired: its retirement decision (DEC-0122) is gone too, so the live demo proposes it (section 16).

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

To try a rule that needs two answers, seed another file with a copy of `seed/demo-binding.patch.json` that adds one
operation to `bindingOperations`,
`{"op":"upsert","entity":"approval-rule","key":"change-auth-owner","values":{"minimumCount":2}}`, and a copy of
`seed/walkthrough-world.json` with a second business owner for Inspection Scheduler (an `owner-assignment` row for
Ines Varga, `00000000-0000-4000-8000-000000000215`): `seed --environment Development --binding <file> --rows <file>
--Product:Store:Sqlite:ConnectionString="Data Source=<file>"`, and start the server with the same store setting. A
change to Inspection Scheduler then opens two requests, each asking both owners. The second answer from the same person
does not count, and the engine opens a new request that asks only the other owner; the decision is accepted when two
different people have approved.

### Directory reads without a tenant

Add `--Product:Directory:Mode=Recorded --Product:Directory:RecordedPath=../tests/Product.Azure.Tests/Recorded` to the
server command. `get_role_holders`, `run_owner_check`, `check_role_bindings` and `run_gap_check` then answer from
recorded, fictional Microsoft Graph and Power Platform responses, with no sign-in and no network.

### Connect Claude Code

With the server running, `claude mcp add --transport http product-local http://127.0.0.1:5080/mcp --header
"X-Product-Dev-Identity: priya"` registers it as an MCP server that signs in as Priya Castell. Then ask Claude Code, for
example, "Which applications does Field operations have, and who owns Inspection Scheduler?" Claude Code gets the
tools Priya's roles allow, every write it makes is stamped as an AI client acting for Priya, and it can never approve.
[`../../docs/mcp.md`](../../docs/mcp.md) covers the rest: the sign-in against a real tenant, the persona header on a
demo install, the tool list, what an AI client may do and the refusals.

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

From `src/`, `node tools/ci.mjs --no-scrub` runs this build in Release and this folder's two test projects as its
`azure` check, with no secret and no database. [`../../docs/testing.md`](../../docs/testing.md) lists every check,
the Linux run for Windows machines with Smart App Control, and the opt-in runs.

This folder's test projects find `src/` from their project folder, so they also run from an output folder elsewhere.
`dotnet test` takes no `--artifacts-path`, so after `dotnet build Product.Azure.sln --artifacts-path <folder>` run each
test app directly, for example `dotnet <folder>/bin/Product.Azure.Tests/debug/Product.Azure.Tests.dll`. The engine and
MCP suites find `src/` from the folder they start in, then by walking up from their output folder, so when they are
built elsewhere, start them from `src/`, for example
`dotnet <folder>/bin/Product.Engine.Runtime.Tests/debug_net10.0/Product.Engine.Runtime.Tests.dll` run in `src/`.

### What this does not do yet

- No card reaches Teams or Outlook from a local run: the approval runner is a Power Automate template for the client's
  tenant (`flows/README.md`), and the tests and the steps above play it.
- No live directory reads without a tenant: `Live` needs the client's app registrations and consent; locally, use
  `Recorded`.
- No expiry on the local clock by hand: an Approval Record expires when a claim or the daily due-state refresh finds it
  past its Expires On, and the demo's rules wait five or ten days or have no timeout. The tests move a manual clock
  instead; before a demo held days later, `demo refresh` (the demo kit) asks the story's expired requests again.
- No real install yet. The template in `infra/` has been provisioned as a demo install with fictional data; a real
  install cannot reach version 1, because the bootstrap has no entry point
  ([`../../docs/install.md`](../../docs/install.md#a-real-install)). The rest is under "Not in this release" below.

## Not in this release

- Step-up sign-in covers a person's approval answer only (`Product:WebApp:StepUp`, off by default and in the demo);
  step-up for tier 3 and configuration changes is not built (`ENGINE-CHANGES-NEEDED.md`, item 15).
- A flow that sends the reminders and escalations the daily due-state refresh records. The template does not set
  `Product:DueStateRefresh:HourUtc` yet, so the Functions timer runs no refresh on a deployed install until it is set.
- The bootstrap of version 1 through the API in a real subscription, the engine endpoints of document 25, section 12.2
  other than the approval outcome and the runner's routes, the separate approval writer database principal, ledger
  digest storage and the private data tier option.
