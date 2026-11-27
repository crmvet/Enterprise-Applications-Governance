# Step 2, document 24: the product's MCP surface

> **Platform (D-26, 2026-11-22).** The first release is designed to run in the client's own Azure subscription. The MCP server is designed to run in the same Azure Functions service as the API and the engine, over Azure SQL, so in section 9 the Azure binding applies. The Dataverse binding, and the routes that exist only on Dataverse (Microsoft's MCP management server and the Dataverse MCP server's `invoke_api`), are kept as a possible later option. D-09 and D-16 still apply. This document was written while the data store was still open (D-22), so it describes both bindings; the Dataverse parts show how that later option would work.

- **Date:** 2026-11-22. **Status:** design draft, written client-neutral.
- **Aligned to working defaults (2026-11-22).** This file follows `28-working-defaults.md`. Settled defaults cite "28" with their issue or question number. The six questions in section 3 of 28 are open design questions; where one touches this file, the text uses its recommended option, marked "(working default; open design question N, see 28-working-defaults.md)".
- **Step:** 2 of 3 (the design). This document lists every tool on the product's own MCP server: its name, purpose, inputs, outputs, the records it reads and writes, the state an AI client can leave a record in, and the product role it needs. It also sets the identity path, the stateless design, the three routes Microsoft documents, licensing, hosting and what adopters must allow.
- **Decisions it follows:** D-09 (the product may have its own MCP server), D-16 (MCP clients first, Claude Code is the reference client), D-18 (the first release is the core plus the product's own MCP server), and every other decision from D-01 to D-22 (the decision records are in [docs/decisions/](../../decisions/)). Where this file and a decision disagree, the decision wins.
- **Platform-neutral (D-22).** This document was written while the data store was open: Dataverse, or a database in the client's own Azure subscription. D-26 has since chosen Azure (note above). So every tool here is a logical contract. Section 9 shows how each tool binds to each platform. The tool names, inputs and outputs stay the same on both.
- **Names.** Entity and term names come from `docs/design/step2/20-glossary-and-entities.md` (called "doc 20" below), with its IDs (C01 to C39, O01 to O24, L01 to L05) and the IDs it reserves in section 2.10 (C40 to C46; C40 Separation Rule, C42 Workload Grant and C46 Configuration Draft are adopted as working defaults, the others are proposals). New terms are listed in section 15 and are not yet in doc 20.
- **Sweep proposals.** Proposals from a review of Microsoft announcements up to 2026-11-21 (the news sweep) that this design adopts are marked "(sweep proposal)". None of them is decided yet.
- **Patterns.** Ideas from governance practice are used as generalized patterns only, each with a pattern ID such as I-71. Section 14 lists them.
- **Microsoft facts** carry a source ID [T1] to [T41]. Vendor facts for Claude Code and GitHub carry [V1] to [V5]. Section 16 lists the URL and the date checked. UNVERIFIED marks what no source confirmed.
- **The product has no name yet** (D-05), so this file says "the product".

**Eight rules every tool obeys.**

1. **On behalf of the signed-in person, end to end.** The server validates the person's token, then reaches the data store as that person. It never forwards the incoming token and never answers with one shared identity's rights [T3][T5].
2. **AI never approves** (D-02, D-10). No tool an AI client is served approves, rejects, accepts, authorizes, sets a tier, moves a stage, activates configuration, assigns a role or deletes. The engine refuses those writes from an AI client whatever the tool. Since D-38 a person answers an approval request they were asked on the product's decision page, through tool 52, `answer_approval`, which is served only to a person in the web app and never to an AI client (B24-6).
3. **AI-created rows start as Draft or Proposed.** Facts an AI client reports (a deployment, a document status) are stored "as reported", with the actor kind.
4. **Everything is configuration** (D-06, D-07, D-10). Which role may call which tool, the highest state an AI client may set, page sizes and depth limits are configuration rows (section 2.3). No tool branches on a fixed tier number or label.
5. **Roles come only through Entra groups** (D-11). The server reads product roles from group-based bindings. It never assigns a role. Non-human workloads (pipelines, the approval runner) hold their workload role through a recorded, expiring Workload Grant (C42) instead (working default; open design question 12, see 28-working-defaults.md). They do not use MCP.
6. **Links, not copies** (D-15, D-17, D-19). Tools store an ID, a URL, a label and a "last seen" date for anything another tool owns. Entra rosters and Microsoft's inventory are read live and never stored.
7. **Stateless.** No session state lives in the server between calls. Any instance can answer any request (section 5).
8. **No gateway holds a shared credential.** A gateway in front of the server passes the caller's own token through [T15].

---

## 1. In one screen

1. **75 tools in 10 toolsets:** session, portfolio, intake, decisions, change, deployment, documentation, gap check, configuration and links. Six of them (tools 46 to 51) cover process steps that had no tool (28, issue 11). Tool 52, `answer_approval`, is served only to a person in the product's web app (D-38; B24-6). Tools 53 to 59 are the reads the web app's first screens need (B24-7). Tool 60, `score_intake_request`, records the PMO's demand fields and tool 61, `find_people`, finds people by name for the pickers (B24-9). Tool 62, `complete_review`, and tool 64, `complete_change`, are served only to a person too: a person marks a review done and closes a change. Tool 63, `get_review`, reads one review (B24-10). Tools 65 to 74 serve the remaining screens: the audit trail, the evidence pack, the AI activity, the tier assessment, the documentation list, the deployment targets, the gap check runs, and the dependency and portfolio writes; `save_deployment_target` and `save_portfolio` are served only to a person (B24-11). Tool 75, `find_records`, finds a record by its number for the web app's global search (B24-13). A caller sees only the tools its product roles allow, and can narrow the list further to the toolsets it needs.
2. **Reads answer the questions people ask:** "who owns this app?" (`get_application_owners`), "what breaks if it retires?" (`get_dependency_impact`), "what does my tier need?" (`get_requirements`), "what needs me?" (`get_my_queue`), "what documentation is missing or stale for my app?" (`get_documentation_status`).
3. **Writes leave records in Draft, Proposed, Submitted (a request, never an approval), as reported, as a link, or as a candidate configuration draft.** Section 6.0 defines each state.
4. **A person answers an approval on the product's decision page** (D-38). `submit_record` starts the approval requests; Teams and Outlook messages link to the page, and the person answers there through `answer_approval`, a tool served only on the web app's entry, with the token proving who answered. The approval runner (the approval flows) still writes an answer given on a card under the approval writer workload role (28, issue 7); answering inside a card is a later per-install option. Only a person counts.
5. **Configuration: the AI proposes, a person accepts, the engine activates.** `propose_configuration_change` validates and stores a configuration draft, a patch on a Configuration Draft row (28, issue 3). `submit_record` then records a Proposed configuration change decision and starts the card. Nothing changes until a person accepts the engine's diff on that card (working default; open design question 4, see 28-working-defaults.md). The engine then creates the new configuration set version inside the activation operation. Nobody activates by hand, and no tool writes configuration tables (document 21, section 4.4; document 23, P10).
6. **The gap check and the owner check run live as the signed-in admin** (D-15; 28, Q18). The server exchanges the admin's token for a Power Platform API token and queries the inventory, which accepts delegated tokens only [T18]. The owner check reads owners' account state from Graph the same way. No unattended job.
7. **Claude Code connects in one of two ways:** a client registered in the adopter's tenant (GA), which the setup guide leads with, or Client ID Metadata Documents (preview, Claude Code listed "Supported"), offered for pilots. CIMD becomes the lead once it is generally available (28, Q14). Microsoft says to prefer pre-registration where possible [T1].
8. **Three routes exist for the product's tools.** Route A, the product's own server, works on both platforms. Routes B (Microsoft's MCP management server, preview) and C (the Dataverse MCP server's `invoke_api`) exist only on Dataverse. Route A is the recommendation for every toolset, and the only one that can run the gap check and read Entra live (section 8).
9. **Licensing.** On Dataverse, every person who reaches the data through any MCP route needs a license. Pooling access through a server does not reduce the count (multiplexing) [T14]. On the Azure binding, people need no Power Platform license for MCP use.
10. **Hosting.** On the Azure binding, one service on Azure Functions Flex Consumption (.NET 10) hosts the API, the MCP server and the engine. Azure Container Apps is the fallback if the on-behalf-of or MCP spike fails on Functions (28, issue 13; spike test 13 confirms). On the Dataverse binding the server is a thin Functions app in front of the custom APIs. Both are stateless, with one always ready instance for interactive clients [T6][T7].
11. **Adopters allow the server in up to eight places:** Claude Code managed settings, GitHub Copilot managed settings, the Entra Global Secure Access MCP firewall (preview), Defender runtime protection (pilot in Audit mode) and Entra consent; optionally Agent 365 bring-your-own registration (preview) and an Azure API Center entry; and, for route C only, the Dataverse MCP allowed clients (section 12).
12. **Open design questions.** Two stay open here, each with a working default in the text: whether an AI client may submit on a person's behalf (open design question 9, see 28-working-defaults.md) and how pipeline identities get their role (open design question 12). The Claude Code client path and the database identity on the Azure binding are settled as working defaults (28, Q14 and Q20).

---

## 2. The shape of the surface

### 2.1 Toolsets

A toolset is a named group of tools. A client can ask for only the toolsets it needs with an `X-Toolsets` request header, for example `X-Toolsets: portfolio,documentation`. Without the header, the caller gets every toolset its roles allow. This keeps the list short for an AI client, which chooses tools from their descriptions. The Azure DevOps remote MCP server uses the same idea with its `X-MCP-Toolsets` header [T28].

| Toolset key | What it covers | Tools | Main parties |
|---|---|---|---|
| `session` | Who am I, what needs me, who holds a role | 3 | All |
| `portfolio` | Portfolio questions: owners, card, impact, summary, requirements, and the lists of reviews, portfolios and owner assignments | 9 | All |
| `intake` | The front door: duplicates, tier questions, intake drafts, registering an app already built, components, owner changes and leaves, and the intake list and record | 10 | Business, makers, AI, PMO |
| `decisions` | Decision proposals, submitting and withdrawing records, the decision log, approval status, architecture review requests, one decision with its approval state, the list of approval requests, and a person's own answer to an approval request (the web app only) | 9 | All |
| `change` | Change request drafts, the standard change check, the schedule, change results | 5 | Change control, owners |
| `deployment` | Deployment records and corrections, what runs where, the pipeline map, the later gate check | 6 | IT, owners, change control |
| `documentation` | Required documents, document links and status | 3 | Owners, architects |
| `gapcheck` | The live gap check, the owner check and the claim list | 6 | IT (admins), owners |
| `configuration` | Reading configuration, rule traces, configuration health, proposing changes, presets, role binding checks | 7 | Configuration admin, platform admin, auditor |
| `links` | Delivery and evidence links | 1 | All |
| `data` | Business data domains and their owners, data locations of any vendor, the mappings between them, the data uses of applications with their decision state, and the data gaps (data taxonomy, D-37; B24-16) | 10 | All; data owners, platform admin, data governance lead |

### 2.2 What each default role sees

Default product roles come from document 21's seed list, agreed with document 26: 15 human roles (28, issue 7; the data governance lead added by D-41, B24-15). "R" means the read tools of the toolset, "W" means its write tools. Owner roles (C28) narrow writes further to the applications a person owns. The table is the shipped default of the Tool Policy rows (section 2.3); a client changes it by configuration. It summarizes by toolset. The exact default for each tool is the "Default roles" column in section 6.1.

| Toolset | Requester | Maker | Application owner | Data owner | Architect | Security reviewer | Change manager | Change board | PMO | AI lead | Platform admin | Configuration admin | Auditor | Reader |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| session | R | R | R | R | R | R | R | R | R | R | R | R | R | R |
| portfolio | R | R | R | R | R | R | R | R | R | R | R | R | R | R |
| intake | R W | R W | R W | R | R | R | R | R | R W | R W | R W | R | R | R |
| decisions | R W | R W | R W | R W | R W | R W | R W | R W | R W | R W | R W | R W (configuration drafts) | R | R |
| change | R | R W | R W | R | R | R | R W | R | R | R | R W | R | R | R |
| deployment | R | R W | R W | R | R | R | R W | R | R | R | R W | R | R | R |
| documentation | R | R W | R W | R | R W | R W | R | R | R | R W | R W | R | R | R |
| gapcheck | - | R W (claims) | R W (claims) | - | - | - | - | - | R | R W (claims) | R W | - | R | - |
| configuration | R | R | R | R | R | R | R | R | R | R | R W (role checks) | R W | R | R |
| links | R W | R W | R W | R W | R W | R W | R W | R W | R W | R W | R W | R | R | R |

- **The four roles that were missing** now have defaults (28, issue 7). `data-owner` writes decisions (data use) and links. `security-reviewer` writes decisions (security review) and documentation links. `change-board` reads and writes decisions and links; its members answer board cards. `reader` only reads, like the auditor, but sees no audit entries and no restricted-read columns (document 26).
- **The configuration admin** needs W in `decisions` only for `submit_record` and `withdraw_record` on configuration drafts.
- **`record_owner_leave`** (tool 46) also lets the leaving person record their own leave, whatever their role.
- **Rows are filtered by the read visibility settings** (28, issue 10). `intake-visibility` defaults to "own": the requester, plus PMO, platform admin and architect, and the people the request involves: the business owner it names, the owners of the application it is linked to, and anyone asked on a decision about it (B24-14). `claim-visibility` defaults to "admins and claimant". `record-access-mode` defaults to open read, with portfolio read available.
- **Two workload roles never use MCP.** The deployment recorder calls the record endpoint (section 4.6). The approval writer is the role the approval runner (the approval flows) uses to write outcomes. Both are held through Workload Grants (C42), not group bindings (working default; open design question 12, see 28-working-defaults.md).

### 2.3 Tool Policy: tools are configuration too

Which roles may call a tool, and how far an AI client may move a record, must not be hardcoded (D-06, D-10). So each tool has a Tool Policy row, a new configuration entity proposed in section 15 (proposed C41; C40 is the Separation Rule proposed by document 21). The server reads it on every request to build the tool list and to refuse calls. The engine reads it again on every write.

| Column | Description |
|---|---|
| Tool Key | The tool's name, such as `record_deployment`. Stable, never reused |
| Toolset | The toolset key from section 2.1 |
| Is Enabled | No hides the tool from every caller |
| Allowed Roles | Keys of the product roles (C29) that may call it |
| Requires Owner Role | Yes when the caller must also hold an owner role (C28) on the subject's application |
| Max AI State | The highest state an AI client may leave a record in through this tool (section 6.0). Configuration can only tighten it; the engine contract caps it (section 7) |
| AI May Submit | For tools that start a request others act on (`submit_record` and `request_architecture_review`), one value per subject kind: whether an AI client may submit for a person. Shipped default: yes for intake requests, change requests, decisions, configuration drafts and review requests (working default; open design question 9, see 28-working-defaults.md) |
| Calls Per Person Per Hour | A rate limit per person, so one runaway agent cannot flood the store (I-107) |
| Release | R1 or Later |
| Description Override | An adopter's own wording of the tool description, for example in another language. The default description ships with the product |

- **Confirmation cards are configuration too** (working default; open design question 9, see 28-working-defaults.md). Registering an application, and marking current a document whose type has Current Needs Person = yes (C22, document 23), send the person a confirmation card through the `confirmation-channel` setting (document 23). Links and deployment records need nothing.
- **Tool Policy rows change like any configuration row:** through a configuration draft and an accepted decision. No tool writes them directly (28, issue 3).

### 2.4 Routes per platform

| Platform | Routes that exist | Recommended |
|---|---|---|
| Dataverse binding | A: the product's own server calls custom APIs through the Web API as the person. B: Microsoft's MCP management server builds a server from the same custom APIs (preview). C: the Dataverse MCP server's `invoke_api` calls the same custom APIs | A for every toolset. C stays available, because the Dataverse MCP server finds custom APIs on its own [T27]. B after it reaches GA, if a Microsoft-hosted option is wanted |
| Azure binding | A only. The server, the API and the engine run as one service on Azure Functions Flex Consumption (28, issue 13) | A |

Section 8 compares the routes tool group by tool group.

---

## 3. Contract rules for every tool

### 3.1 Names

- Tool names are lowercase `snake_case`, verb first: `get_application_card`, `save_change_request_draft`. The verbs mean the same thing everywhere: `get` and `find` read, `check` reads and evaluates, `save` creates or updates a draft, `propose` creates a Proposed decision, `submit` starts approvals, `record` stores a reported fact, `link` and `map` store links.
- No product prefix in the tool name. MCP clients already put the server name in front, so the name stays short. The physical custom API name on Dataverse adds the publisher prefix (section 9.1).
- The contract follows doc 20, section 3.3: field names are lowerCamelCase forms of the logical names, configuration references travel as keys (`tierKey: "tier-3"`), operational references travel as IDs (`changeRequestId`).

### 3.2 Input outline notation

Inputs in section 6 use a short notation instead of full JSON Schema. The server publishes full JSON Schema generated from the same definitions.

| Notation | Meaning |
|---|---|
| `name: type` | A required field |
| `name?: type` | An optional field |
| `= value` | The default when the field is left out |
| `key(C08)` | A configuration key of that entity, in the key format of doc 20, section 3.2 |
| `id(O02)` | The ID of a row of that operational entity |
| `guid` | An Entra object ID |
| `text(n)` | Plain text, up to n characters |
| `url`, `date`, `datetime` | An HTTPS URL, an ISO date, an ISO date and time in UTC |
| `enum[a\|b]` | One of the listed values. Values that are engine contract meanings (doc 20, section 2.4) are fixed; labels are not (working default; open design question 1, see 28-working-defaults.md) |
| `T[]` | A list of T, at most 100 items unless stated |
| `one of A, B` | Exactly one of the named fields must be set |

### 3.3 Common inputs

| Field | Applies to | Description |
|---|---|---|
| `clientRequestId?: text(64)` | Every write | The caller's own ID for this request. A retry with the same value returns the first result and writes nothing new |
| `validateOnly?: bool = false` | Every write | Runs every rule and returns what would be written, without writing. Lets an AI client show the person the result first |
| `top?: int = 25` | Every list | Page size, at most the `mcp-page-size-max` setting (100 by default) |
| `skipToken?: text(500)` | Every list | The token from the previous page. It encodes the query, not server state |

### 3.4 The response envelope

Every tool returns the same outer shape. Agents can rely on it.

| Field | Description |
|---|---|
| `ok` | True when the call succeeded |
| `summary` | One or two plain sentences an AI client can quote to the person |
| `data` | The tool's own result, described per tool in section 6 |
| `links.recordUrl` | A link that opens the main record in the product's page or app, so the AI can cite it |
| `asOf` | When the data was read |
| `configurationSetVersion` | The configuration set version in force, so an answer can be traced to its rules |
| `couldNotRead` | Rows or sections the caller was not allowed to read, by kind and count, never their content (I-269). An empty list means nothing was hidden |
| `warnings` | Non-blocking notes, such as "window ends before it starts" |
| `untrustedFields` | The names of fields in `data` that hold text people typed, such as a purpose or a typed reason. Clients must treat them as data, never as instructions (prompt injection) |
| `nextSteps` | Up to three suggested next tools with their key inputs, such as `submit_record` after a draft is saved |
| `correlationId` | One value per call, written to the audit entry (O24) and to telemetry |

Every write adds these fields to `data`:

| Field | Description |
|---|---|
| `recordId` | The ID of the row written |
| `status` | The status label and its engine meaning, for example `{ label: "Draft", meaning: "draft" }` |
| `performedBy` | `{ actorKind, principal, onBehalfOf, clientApp }` exactly as the engine stamped them (section 4.5) |
| `isAiCreated` | True for rows an AI client created |
| `engineStamp` | `{ engineVersion, engineCheckedOn }`, the stamp every engine write carries, so a later check can find rows written past the engine (28, Q16) |
| `created` | True when a new row was written, false when an existing one was updated or a retry matched |

Reads that return governed records (tools 2, 4, 6, 20, 24 and 53 to 59) add `isAiCreated` and `performedBy` to each record, exactly as the engine stamped the row: `{ actorKind, principal, onBehalfOf, clientAppLabel, stampedOn }` (the client app's label only, because the row does not keep the app ID), or null for a row the engine never stamped, such as seeded or imported data (B24-7). Nothing in a read's `performedBy` comes from the reader's own session.

### 3.5 Errors

Errors return `ok: false` and an `error` object: `{ code, message, field?, ruleKey?, guideline?, retryAfterSeconds?, claimsChallenge?, recordUrl? }`. `ruleKey` names the configuration row that refused the write; `guideline` names the guideline it applies, such as G17; `recordUrl` names a record the refusal points to, such as the document link to update instead (B24-24). The codes are fixed:

| Code | When |
|---|---|
| `not_signed_in` | No valid token. The HTTP layer returns 401 with the protected resource metadata link [T2] |
| `not_authorized` | Valid token, but the caller lacks the role, the owner role, or the row access. HTTP 403 for a missing scope [T2] |
| `step_up_required` | Only `answer_approval`, only with the step-up setting on (D-38): the answer needs a fresh sign-in. HTTP 401 with a claims challenge; `claimsChallenge` carries it for an authentication context (B24-9; document 26, B26-9) |
| `ai_cannot_do_this` | The write would set a state that only a person or the approval runner may set (section 7) |
| `rule_violation` | A configuration rule refused the write, for example a gate requirement or a window rule |
| `validation` | An input is missing, malformed or not an allowed key |
| `not_found` | The ID or key does not exist, or the caller cannot see it |
| `conflict` | The record changed since it was read, or `baseVersionNumber` is stale |
| `locked` | The write touches a locked setting or an engine contract value |
| `rate_limited` | The Tool Policy limit was reached. Includes a retry time |
| `upstream_unavailable` | A Microsoft API the tool needs did not answer, for example the inventory |
| `configuration_unavailable` | The engine cannot load a valid active configuration version, or a rule row it needs is missing, such as a gate policy row. Governed writes are refused and the broken configuration is reported. There is no built-in fallback (document 21, rule 8; 28, issue 14) |

**Proposed by the build (2026-11-22).** A rule for an unexpected failure (a JSON-RPC internal error that names only the correlation ID) and the order in which engine reasons map to these codes are in B24-1 at the end of this document. Contract gaps in tools 6 to 10 are in B24-2, and the record endpoint points in B24-4.

### 3.6 Tool annotations

Each tool also carries the MCP tool annotations that hint at its behavior, as hints only: read tools are marked read-only; `save_*`, `record_*` and `link_*` tools are marked idempotent when `clientRequestId` or `idempotencyKey` is set; no tool is destructive, because no tool deletes. These annotation names come from the MCP specification, not from Microsoft, and the server never relies on a client honoring them. The MCP management server also accepts per-tool annotations as JSON [T10].

---

## 4. Identity, end to end

### 4.1 The hops

| Hop | From | To | Credential | Rule |
|---|---|---|---|---|
| 1 | The person | Claude Code (or another MCP client) | The person signs in through Microsoft Entra ID in a browser | The client is either a CIMD client or a registered client (section 4.2) |
| 2 | Claude Code | The product's MCP server | An access token issued to the server's own app registration, with the delegated scope `mcp.access` | The server publishes protected resource metadata and returns 401 with a link to it when no token is sent [T2] |
| 3 | The server | Its own token check | None | Validate signature, issuer, audience, lifetime, tenant, client and scope. The `aud` claim is the server app's client ID GUID, the `scp` claim holds `mcp.access` [T2] |
| 4a | The server | Dataverse (Dataverse binding) | A new token from the on-behalf-of flow, with the delegated Dataverse permission `user_impersonation` [T22] | Never forward the incoming token: "Pass-through scenarios where the server forwards its token create security vulnerabilities" [T3]. The on-behalf-of flow "only uses delegated scopes and not application roles" [T5] |
| 4b | The server | The engine and the database (Azure binding) | The engine runs in the same process. The database hop uses the service's managed identity, with the person's ID in `SESSION_CONTEXT` for row-level security (28, Q20) | The engine authorizes each call from the validated token: the person's object ID and product roles |
| 5 | The server | Microsoft APIs (gap check, Entra rosters) | A new on-behalf-of token for the Power Platform API or Microsoft Graph, as the person | The inventory API "currently supports only delegated user authentication"; app-only calls return 403 [T18] |
| 6 | The approval runner (the approval flows) | The data store | The approval writer workload role: an app role on the runner's identity (Azure binding) or the approval writer application user (Dataverse), held through a Workload Grant (C42) | Only this identity writes approval outcomes (doc 20, O10; 28, issue 7) |

### 4.2 How Claude Code connects

Claude Code is the reference client (D-16). It reads the server's protected resource metadata on its own: it checks RFC 9728 at `/.well-known/oauth-protected-resource` first [V2]. Two client identity paths exist, and Microsoft's guide for securing an MCP server names both: "A registered Agent ID" or "A Client ID Metadata Document URL when pre-registration isn't practical" [T2].

| | Path 1: Client ID Metadata Document (CIMD) | Path 2: a registered client |
|---|---|---|
| What it is | Claude Code identifies itself with an HTTPS URL as its client ID. Entra fetches the JSON document at that URL [T1] | The adopter registers a public client app in its own tenant and preauthorizes it on the server app |
| Status | Public preview, "isn't recommended for production workloads" [T1] | GA (standard Entra app registration) |
| Claude Code | Listed "Supported", with metadata URL `https://claude.ai/oauth/claude-code-client-metadata` [T1]. Claude Code finds CIMD servers automatically [V2] | `claude mcp add --transport http --client-id <id> --callback-port <port> <name> https://<host>/mcp`; the redirect `http://localhost:<port>/callback` must match the registration [V2] |
| What the server publishes | `authorization_servers` set to `https://login.microsoftonline.com/<tenant-id>/oauth21/2026-08`, the versioned identifier that selects the CIMD endpoints [T2] | The ordinary `/v2.0` identifier [T2] |
| How Entra represents the client | An agent identity per tenant, with an app ID derived from the URL; it appears in agent identity inventory, audit logs and sign-in logs [T1] | An ordinary app registration and service principal |
| Consent | Under the default policy, users can grant CIMD clients only "a very limited set of OAuth scopes ... such as `openid` and `User.Read`". So an administrator adds the Claude Code CIMD client in advance and grants the product's scope [T1] | An administrator grants consent or preauthorizes the client on the server app |
| Limits | No client credentials flow, no confidential client, no publisher verification, and Entra "doesn't provide a tenant-managed domain allowlist or blocklist for CIMD URLs"; the client owner controls the document [T1] | None beyond normal app management |
| Microsoft's advice | "Implementers should always prefer pre-registration of a client with Entra when possible" [T1] | Preferred by Microsoft |
| Admin effort | One pre-add per tenant; nothing to maintain per client version | One app registration per adopter, plus one per extra client (GitHub Copilot, others) |

The setup guide leads with path 2, a registered client. It documents CIMD as an option for pilots, and CIMD becomes the lead once it is generally available (28, Q14). The infrastructure template publishes the ordinary `/v2.0` identifier by default. Both paths stay supported, because the server's metadata can serve either.

Other clients follow the same server contract. GitHub Copilot and Visual Studio Code are Microsoft first-party clients and do not use CIMD [T1]; they use a registered client. Copilot Studio, if a client adds it later (D-16), connects with OAuth 2.0 through its MCP wizard [T30].

### 4.3 The server's app registration

| Item | Value | Source |
|---|---|---|
| Application ID URI | The canonical endpoint URL, for example `https://<host>/mcp`, with a non-empty path and no trailing slash | [T2] |
| Delegated scope | `mcp.access`, the only scope MCP clients request | [T2] |
| App roles | Only for app-only callers: `Deployment.Record` for pipeline record steps, `Approval.Write` for the approval runner on the Azure binding (proposed name, for the approval writer role), and later `GateCheck.Ask` for check steps (section 4.6). Each is assigned directly to a workload identity and recorded as a Workload Grant (C42) (working default; open design question 12, see 28-working-defaults.md). Microsoft: "Define application roles only if the server supports app-only access" | [T2] |
| Downstream permissions | Dataverse `user_impersonation` (Dataverse binding); Power Platform API delegated access for the inventory (gap check); Microsoft Graph delegated read of group members (rosters, role binding check; exact least permission UNVERIFIED) | [T18][T22] |
| Sign-in audience | The adopter's own tenant only. One server per adopter tenant and per product store | Recommendation |
| Server credential for the on-behalf-of exchange | A federated identity credential backed by a managed identity, as Microsoft's own on-behalf-of MCP template does, or a certificate in Key Vault. Never a client secret in configuration, never stored in the product's tables | [T4] |

### 4.4 Downstream identity per platform

**Dataverse binding.**
- The server exchanges the person's token for a Dataverse token and calls the tool's custom API through the Web API. Dataverse then applies the person's security roles, inherited from Entra group teams (D-11), row access and column security.
- The plug-ins enforce every rule, on every route (D-10). They run as the caller, with an engine-nested marker, and never act with more rights than the caller (28, issue 9 and Q15).
- Rejected: Dataverse impersonation with the `CallerObjectId` header. The impersonating account needs the "Act on Behalf of Another User" privilege, which must be assigned directly, because "Users can't inherit this privilege through a Team" [T21]. That breaks D-11 and turns the server into a deputy with broad rights.
- Group team membership is cached at sign-in and refreshed after up to 8 hours (doc 20, S7). Whether a person who has never opened the environment gets group team membership on a first on-behalf-of call is a spike test (section 13).

**Azure binding.**
- The engine runs in the same process as the MCP endpoint and the API. It reads the person's object ID and product roles from the validated token. Roles arrive as Entra app roles assigned to the bound groups, which needs Entra ID P1 or P2 and does not support nested groups (doc 20, S9).
- The engine reaches the database with the service's managed identity, plus `SESSION_CONTEXT` carrying the person's ID for row-level security, a pattern Microsoft documents for middle-tier applications [T19] (28, Q20). An on-behalf-of token to Azure SQL, which Microsoft's Data API builder now supports [T20], stays an option if a client's security review asks for database-level identity.
- The database accepts writes only from the engine (doc 20, section 2.9).

### 4.5 What the engine stamps on every write

The actor kind comes from the entry point and the token type, never from a field the caller sends.

| Entry point | Token | Performed By Actor Kind | On Behalf Of | Client App |
|---|---|---|---|---|
| MCP endpoint (`/mcp`) | Delegated user token | AI client for a person | The person (`oid` claim) | From the token's client claim (`azp`), mapped to a label through the `client-app` value list (section 15) |
| Record endpoint (`/api/deployments`) | App-only token with the `Deployment.Record` app role | Pipeline | Empty; the AI client that queued the run goes into Started By AI Client when the step reports it (doc 20, O14) | The workload identity |
| The product's page (generative page or web page) | Delegated user token | Person | Empty | The page |
| Approval runner | The approval writer role: an app-only token with `Approval.Write` (Azure binding), or the approval writer application user (Dataverse) | Approval runner | Empty | The approval runner |
| The engine | None | Engine | Empty | None |

The engine always runs as the caller and never with more rights (28, issue 9). Actor kind "Engine" marks writes the engine performs as an effect, such as an owner check review or an upgrade draft.

**A known gap on Dataverse.** On route A the server passes the actor kind to the custom API. On route C, calls arrive from the Dataverse MCP server directly. Whether a plug-in can verify which application made a call is UNVERIFIED (section 17, item 3). So on Dataverse, the actor kind is an audit record, not a security control. Section 7 shows why "AI never approves" does not depend on it. On the Azure binding the engine knows the entry point for certain.

### 4.6 Pipelines do not use MCP

A pipeline record step (D-19) calls a plain HTTPS record endpoint with the same contract as `record_deployment`, using its workload identity with the `Deployment.Record` app role. MCP adds nothing for a pipeline: there is no model choosing tools. On the Dataverse binding the endpoint is the custom API behind `record_deployment`, called through the Web API. Each pipeline's workload identity holds the deployment recorder role directly, recorded as a Workload Grant (C42) with a reason, the deciding decision and an expiry (working default; open design question 12, see 28-working-defaults.md). People still get roles only through groups, and agent identities never get a grant. Record steps fail soft: a governance write warns and never fails the deployment (I-189).

### 4.7 What the server never does

- Forward the incoming token to any downstream service [T3].
- Call the data store with a shared identity that answers for everyone. Microsoft names this the confused-deputy pattern: "separate the server's execution identity from the caller's authorization" [T24].
- Store a token, a secret or a row between calls (section 5).
- Accept tokens from another tenant.
- Re-expose another product's data. Azure DevOps, GitHub and wiki content stay behind their own MCP servers, which the person's AI client calls as the same person (D-12, D-17).

### 4.8 Gateways

Any gateway in front of the server must pass the caller's token through and must not swap it for its own. Microsoft's AI Gateway says it plainly: "Backend authentication is separate from caller access. With a shared backend connection, authorized callers use the permissions of that connection" [T15]. So:

| Gateway | Allowed when | Notes |
|---|---|---|
| Azure API Management or Azure AI Gateway | It validates the caller's token and forwards the `Authorization` header, never a backend credential | API Management can forward the header to MCP tool calls [T25]. Rate limits and telemetry are fine there |
| Agent 365 Tooling Gateway (bring-your-own registration, preview) | A spike shows that a call through the gateway still reaches the server with a token for the person | The registration accepts `EntraOAuth` [T13]. Whether the server then sees the person's identity end to end is UNVERIFIED. Blocking in Agent 365 applies only through the gateway: "Don't assume blocking prevents direct access to the underlying server endpoint" [T13] |
| Any gateway with a shared backend connection | Never | It would answer with one identity's rights |

---

## 5. Stateless design

- **Transport:** streamable HTTP only, one endpoint path (`/mcp`). Microsoft's hosting guide describes Azure Functions as "optimized for stateless, event-driven tool execution" [T6]. GitHub reports that the MCP specification dated 2026-07-28 is stateless [V5]; this file did not read the specification itself (UNVERIFIED here).
- **No session state.** Every request carries its bearer token. The tool list is computed per request from the token's roles and the Tool Policy rows. If an MCP SDK issues a session ID, the server never relies on it for authorization or data.
- **No row cache between calls.** Reads go to the data store each time. The server may cache configuration by version number, as the plug-ins do (D-10), because a version never changes once published.
- **Token cache.** An in-memory cache of on-behalf-of tokens, keyed by the incoming token, is allowed for speed. Losing it only costs another token exchange.
- **Paging carries the query, not the server.** `skipToken` encodes the query position. Any instance can serve the next page.
- **Long work is split into calls, with state in the data store.** The gap check returns a `gapCheckRunId` and a `skipToken` per estate. The run row (O22) holds the progress, not the server. No background job (D-15).
- **Scale and failover:** any instance answers any request. One always ready instance on Flex Consumption avoids cold starts for interactive clients [T6][T7].

---

## 6. The tools

### 6.0 Legend

**AI write state** (the highest state a call from an AI client can leave a record in):

| State | Meaning |
|---|---|
| Read only | The tool writes nothing |
| Draft | Creates or edits a row whose status meaning is draft (doc 20, record or registration status) |
| Proposed | Creates a decision whose status meaning is proposed |
| Submitted | Moves a draft or proposed record to submitted and starts the approval cards, or sends a request such as a review request. A request, never an approval. Allowed only where Tool Policy says so (working default; open design question 9, see 28-working-defaults.md) |
| As reported | Stores a fact the caller reports, such as a deployment or a change result. Never a decision. Stamped with the actor kind |
| Link | Stores an ID, a URL, a label and a "last seen" date for something another tool owns |
| Candidate | A configuration draft: a patch stored on a C46 Configuration Draft row (document 21), never in force. The C02 Configuration Set Version row is created only at activation, after a person accepts the configuration change decision (28, issue 3) |
| Withdrawn | The caller withdraws a record it created and that is not final |

**Default roles** use the role keys of C29, from document 21's seed of 15 human roles (28, issue 7): `requester`, `maker`, `application-owner`, `data-owner`, `architect`, `security-reviewer`, `change-manager`, `change-board`, `pmo`, `ai-lead`, `platform-admin`, `data-governance-lead` (D-41, B24-15), `configuration-admin`, `auditor`, `reader`. The workload roles `deployment-recorder` and `approval-writer` call no MCP tool. "Owner of the app" means an active owner role (C28) on that application through an accepted ownership decision (O06). "Any role" means the caller holds at least one product role. Rows are always filtered by the caller's own access and by the read visibility settings (section 2.2).

### 6.1 All tools at a glance

Tools 46 to 51 were added in the alignment pass for process steps that had no tool (28, issue 11). Tool 52 was added by the build for D-38 (B24-6). Tools 53 to 59 were added by the build for the web app's screens (D-36; B24-7), and tools 60 and 61 for the PMO's demand list and the people pickers (B24-9). Tools 62 to 64 complete reviews and changes and read one review (B24-10). Tools 65 to 74 are the reads and writes of the remaining screens (B24-11). Tool 75 finds a record by its number (B24-13). Tools 76 to 85 are the data toolset of the data taxonomy (B24-16; section 6.14). Tool 86 runs the daily due-state refresh now (B24-17). Tools 87 to 94 are the reads and the one write of the screens still missing (B24-18). Tools 95 and 96 are the teams and committees (B24-20). Tools 97 and 98 are the data uses and mappings across domains (B24-21). `import_data_taxonomy`, planned as 86, then 87, then 95, then 97, comes in a later slice as 99. Each sits in its toolset. Numbers stay stable, so earlier references still hold.

| # | Tool | Toolset | Question it answers | Writes | AI write state | Default roles | Release |
|---|---|---|---|---|---|---|---|
| 1 | `whoami` | session | Who am I here, and what may I do? | None | Read only | Anyone signed in | R1 |
| 2 | `get_my_queue` | session | What needs me? | None | Read only | Any role | R1 |
| 3 | `get_role_holders` | session | Who is on the change board? | None | Read only | Any role | R1 |
| 61 | `find_people` | session | Who is this person? Find people by name | None | Read only | Any role | R1 |
| 75 | `find_records` | session | Which record is DEC-0123? Find a record by its number | None | Read only | Any role | R1 |
| 95 | `list_teams` | session | Which teams and committees are there, which roles do they hold, and what waits on them? | None | Read only | Any role | R1 |
| 96 | `get_team` | session | Who is in this team, what does it decide, what does it support, and what waits on it now? | None | Read only | Any role | R1 |
| 97 | `list_data_uses` | data | Which data uses are there across every domain, and is each one decided? | None | Read only | Any role | R1 |
| 98 | `list_data_mappings` | data | Where is each domain's data kept, and is every copy outside production decided? | None | Read only | Any role | R1 |
| 4 | `find_applications` | portfolio | Which apps match? | None | Read only | Any role | R1 |
| 5 | `get_application_owners` | portfolio | Who owns this app? | None | Read only | Any role | R1 |
| 6 | `get_application_card` | portfolio | Everything about this app, in one card | None | Read only | Any role | R1 |
| 7 | `get_dependency_impact` | portfolio | What breaks if it retires? | None | Read only | Any role | R1 |
| 8 | `get_portfolio_summary` | portfolio | How does the portfolio look, and which apps lack an owner? | None | Read only | Any role | R1 |
| 9 | `get_requirements` | portfolio | What does my tier need? | None | Read only | Any role | R1 |
| 57 | `list_reviews` | portfolio | Which reviews are overdue, due or done? | None | Read only | Any role | R1 |
| 58 | `list_portfolios` | portfolio | Which portfolios exist, and how do they look? | None | Read only | Any role | R1 |
| 59 | `list_owner_assignments` | portfolio | Who owns what across the portfolio, and who is leaving? | None | Read only | Any role | R1 |
| 63 | `get_review` | portfolio | Where is this review, and what does it need? | None | Read only | Any role | R1 |
| 67 | `get_ai_activity` | portfolio | What did AI clients do, and who decided? With the clients view (B24-18) | None | Read only | Any role (the estate for ai-lead, security-reviewer, platform-admin, auditor; the caller's own for anyone else) | R1 |
| 91 | `get_measures` | portfolio | Is AI app development working? The measures, the slide-back signals and the rows behind them | None | Read only | Any role | R1 |
| 93 | `list_agents` | portfolio | Which agents exist, and is each one decided, reviewed, owned and ready to go live? | None | Read only | Any role | R1 |
| 74 | `save_portfolio` | portfolio | Create or change a portfolio | O01 | None: served only to a person in the web app (B24-11) | platform-admin, pmo (name, description and level only) | R1 |
| 10 | `check_duplicates` | intake | Does this already exist? | None | Read only | Any role | R1 |
| 11 | `get_tier_questions` | intake | Which questions set the tier? | None | Read only | Any role | R1 |
| 12 | `save_intake_draft` | intake | Start or edit an intake request | O07 | Draft | requester, maker, application-owner, pmo, ai-lead, platform-admin | R1 |
| 13 | `answer_tier_questions` | intake | Which tier do my answers suggest? | O08 | Draft (an assessment, not a decision) | Requester of the intake, owner of the app | R1 |
| 14 | `save_application_draft` | intake | Register an app already built | O02, O03, O06, O09 | Draft and Proposed | requester, maker, application-owner, ai-lead, platform-admin | R1 |
| 15 | `link_component` | intake | This app, function or agent belongs to my app; it moved; it is gone | O03, O23 | Link (retired as reported) | Owner of the app, platform-admin | R1 |
| 16 | `propose_owner_change` | intake | Hand over or add an owner | O09, O06 | Proposed | Owner of the app, ai-lead (agents), platform-admin, data-governance-lead (the owners of top-level data domains, D-41) | R1 |
| 46 | `record_owner_leave` | intake | This owner leaves on this date | O05, O21 | As reported | The leaving person (any role), an owner of an app the person owns, platform-admin | R1 |
| 55 | `list_intake_requests` | intake | Which intake requests wait for triage, and what is the demand? | None | Read only | Any role (rows follow `intake-visibility`) | R1 |
| 56 | `get_intake_request` | intake | Where is this intake request? | None | Read only | Any role (rows follow `intake-visibility`) | R1 |
| 60 | `score_intake_request` | intake | Score a request on the table: score, size band, horizon | O07 | As reported | pmo, platform-admin | R1 |
| 68 | `get_tier_assessment` | intake | Why is this tier, and what does it need? | None | Read only | Any role | R1 |
| 73 | `save_dependency` | intake | This app depends on that one; that dependency ended | O04 | As reported | Owner of the app, architect, platform-admin | R1 |
| 17 | `save_decision_proposal` | decisions | Record a decision for acceptance | O09, O20 | Proposed | Holders of R, A or C for the decision type (C31; B24-5) | R1 |
| 18 | `submit_record` | decisions | Send it for approval | O07, O11, O09, O10, Configuration Draft | Submitted | The record's requester or creator | R1 |
| 19 | `withdraw_record` | decisions | Take it back | O07, O11, O09, O02, Configuration Draft | Withdrawn | The record's requester or creator | R1 |
| 20 | `get_decision_log` | decisions | What was decided, by whom, and why? | None | Read only | Any role | R1 |
| 21 | `get_approval_status` | decisions | Who still has to approve? | None | Read only | Any role | R1 |
| 47 | `request_architecture_review` | decisions | Please review this design | O21, O20 | Submitted (a request, never a decision) | Owner of the app, maker, architect, change-manager, platform-admin | R1 |
| 52 | `answer_approval` | decisions | Approve or reject a request I was asked | O10 (the engine's recompute may move O09) | None: served only to a person in the web app (D-38; B24-6) | The person the approval request asks, through any role; the engine checks it | R1 |
| 53 | `get_decision` | decisions | What is this decision, and who still has to answer? | None | Read only | Any role | R1 |
| 54 | `list_approvals` | decisions | Which approval requests wait for me, or for others? | None | Read only | Any role | R1 |
| 62 | `complete_review` | decisions | This review is done | O21, O20 | None: served only to a person in the web app (B24-10) | The person whose review it is; the engine checks the accepted outcome decision | R1 |
| 65 | `get_audit_trail` | decisions | What happened to this record, or what did this person do? | None | Read only | platform-admin, auditor | R1 |
| 66 | `get_evidence` | decisions | What is the evidence for this application or this decision? | None | Read only | Any role | R1 |
| 94 | `request_security_review` | decisions | Please review the security of this agent or app | O21, O20 | Submitted (a request, never a decision) | Owner of the app, ai-lead, security-reviewer, architect, platform-admin | R1 |
| 22 | `save_change_request_draft` | change | Start or edit a change request | O11, O12, O18 | Draft | Owner of the app, maker, change-manager | R1 |
| 23 | `check_standard_change` | change | Does this change fit a pre-approved model? | None | Read only | Any role | R1 |
| 24 | `get_change_request` | change | Where is this change? | None | Read only | Any role | R1 |
| 25 | `get_change_schedule` | change | What is planned, where, when? | None | Read only | Any role | R1 |
| 26 | `record_change_result` | change | How did the change end? | O11, O20, O21 | As reported | Owner of the app, change-manager | R1 |
| 64 | `complete_change` | change | This change is done | O11, O20, O21 | None: served only to a person in the web app (B24-10) | Owner of the app, change-manager | R1 |
| 27 | `record_deployment` | deployment | This version reached this target | O13, O14 | As reported | Owner of the app, maker, change-manager | R1 |
| 48 | `record_deployment_correction` | deployment | That deployment belongs to this change, or a fact was wrong | O14 | As reported | Owner of the app, maker, change-manager, platform-admin | R1 |
| 28 | `get_deployments` | deployment | What runs where? | None | Read only | Any role | R1 |
| 29 | `map_pipeline` | deployment | This pipeline deploys this app to these targets | O16, O17 | Link | Owner of the app (technical), platform-admin | R1 |
| 30 | `get_pipeline_map` | deployment | Which pipelines deploy this app? | None | Read only | Any role | R1 |
| 70 | `list_deployment_targets` | deployment | Where do deployments land, and what runs there? | None | Read only | Any role (external IDs and URLs hidden from the reader role) | R1 |
| 71 | `save_deployment_target` | deployment | Register this place deployments land, change it, or retire it (B24-18) | O15 | None: served only to a person in the web app (B24-11) | platform-admin | R1 |
| 31 | `check_change_approval` | deployment | Does this change have the approvals its policy requires? | L05 | Read only (logs the question) | Any role (preview); `GateCheck.Ask` app role on the record endpoint | Later (D-03) |
| 32 | `get_documentation_status` | documentation | What documentation is missing or stale for my app? | None | Read only | Any role | R1 |
| 33 | `record_document_link` | documentation | Here is the page, and its status | O19 | Link, status as reported | Owner of the app, document owner | R1 |
| 34 | `confirm_document_checked` | documentation | Is this document still true? | O19 | As reported | Owner of the app, document owner | R1 |
| 69 | `list_documents` | documentation | Which documents are missing, stale or waiting across the portfolio? | None | Read only | Any role | R1 |
| 35 | `run_gap_check` | gapcheck | What is running that is not in the register? | O22, O23, O03 | As reported (claims are open items) | platform-admin, plus an inventory Entra role | R1 |
| 36 | `compare_inventory_list` | gapcheck | Compare this list I fetched myself | O22, O23 | As reported | platform-admin | R1 |
| 49 | `run_owner_check` | gapcheck | Which owners are disabled, deleted or leaving? | O22, O05, O21 | As reported | platform-admin | R1 |
| 37 | `list_claims` | gapcheck | What is waiting to be claimed? | None | Read only | maker, application-owner, ai-lead, pmo, platform-admin, auditor | R1 |
| 72 | `list_gap_check_runs` | gapcheck | When was the gap check run, by whom, and what did it find? | None | Read only | maker, application-owner, ai-lead, pmo, platform-admin, auditor (runs follow `claim-visibility`) | R1 |
| 38 | `claim_resource` | gapcheck | That one is mine | O23, O03, O02, O07, O09, O10 | Link, Draft and Proposed (a person's claim submits its ownership decision, B24-10) | maker, application-owner, ai-lead, platform-admin | R1 |
| 39 | `dismiss_claim` | gapcheck | That one is not a business application | O23 | As reported (reversible) | platform-admin | R1 |
| 40 | `get_configuration` | configuration | What rules are in force, and what changed? | None | Read only | Any role | R1 |
| 50 | `explain_rules` | configuration | Which rule applied to this record, and why? | None | Read only | Any role | R1 |
| 51 | `get_configuration_health` | configuration | Which versions exist, which drafts wait, and does the configuration load? | None | Read only | Any role | R1 |
| 41 | `list_presets` | configuration | Which ready-made configurations exist? | None | Read only | Any role | R1 |
| 42 | `propose_configuration_change` | configuration | Change the rules the way the admin said | Configuration Draft | Candidate | configuration-admin | R1 |
| 43 | `export_configuration` | configuration | Give me the configuration as files | None | Read only | configuration-admin, platform-admin, auditor | R1 |
| 44 | `check_role_bindings` | configuration | Is every role held only through its group, or a recorded workload grant? | O24 | Read only (logs the check) | platform-admin | R1 |
| 87 | `list_configuration_changes` | configuration | Which configuration versions exist, who accepted each and when, and which drafts wait? | None | Read only | Any role | R1 |
| 88 | `get_configuration_draft` | configuration | What does this configuration draft change, is it valid, and what does it touch? | None | Read only | Any role | R1 |
| 89 | `get_role_access` | configuration | Who holds each role, through which group, and what may each role do? | None | Read only | Any role | R1 |
| 90 | `list_guidelines` | configuration | Why does each rule exist? The guidelines, the playbook chapters, the builder paths and the sanctioned clients | None | Read only | Any role | R1 |
| 92 | `get_admin_status` | configuration | How is the product running: host settings, Tool Policy, clients, notices and the outbox? | None | Read only | platform-admin, configuration-admin, auditor | R1 |
| 45 | `save_link` | links | Link this work item or this evidence | O18, O20 | Link | Owner of the subject's app, change-manager, pmo; evidence on records the caller can write | R1 |
| 76 | `find_data_domains` | data | Which data domains match, and who owns them? | None | Read only | Any role | R1 |
| 77 | `get_data_domain` | data | Everything about one data domain: owners up the chain, rules and their source, mappings, uses, decisions, outside matches, gaps | None | Read only | Any role | R1 |
| 78 | `find_data_locations` | data | Which stores match a platform, stage, parent, text, unmapped or stale? With a locationId, the full detail of one | None | Read only | Any role; external IDs and URLs are restricted read | R1 |
| 79 | `resolve_data` | data | Which data is this table, object or file? | None | Read only | Any role | R1 |
| 80 | `check_data_use` | data | May this app use this data this way, and what is needed? | None | Read only | Any role | R1 |
| 81 | `get_data_gaps` | data | What is missing, broken or waiting in the data? | None | Read only | Any role | R1 |
| 82 | `save_data_use` | data | This app reads or writes this data, or it stopped | O27, O09 | Draft; Proposed (the data-use decision) | Owner of the app, maker, application-owner, platform-admin | R1 |
| 83 | `save_data_location` | data | This store exists, moved, or is gone (up to 100 items per call) | O25 | Draft | platform-admin, architect, application-owner, the location's technical owner, data-governance-lead | R1 |
| 84 | `save_data_mapping` | data | This domain's data sits here, or it left | O26, O09 | Draft; Proposed (the copy decision) | The location's technical owner, the domain's effective owner or steward, platform-admin, data-governance-lead | R1 |
| 85 | `move_data_references` | data | Move mappings and uses off a domain | O26, O27, O09, O10 | Read only: an AI client may preview with validateOnly; a person moves the rows | platform-admin, data-governance-lead | R1 |

### 6.2 Session toolset

#### 1. `whoami`

- **Purpose:** tells the AI client who the person is in the product, which roles they hold and which tools they may use. A person with no product role also gets an answer, so they learn what to ask for.
- **In:** `{}`
- **Out:** `{ principal: { objectId: guid, displayLabel, principalKind }, productRoles: [{ roleKey, label, viaGroup: { objectId, displayLabel } }], actorKind: "ai-client-for-person", clientApp: { appId, label }, configurationSet: { key, version, publishedOn }, toolsets: [toolsetKey], tools: [toolKey] }`
- **Reads:** token claims, C29 Role, C30 Role Group Binding, O05 Principal Reference, C02 Configuration Set Version, proposed C41 Tool Policy. **Writes:** none.

#### 2. `get_my_queue`

- **Purpose:** "What needs me?" Approvals the person may answer now, reviews that are theirs to do, documents missing, stale or waiting for their confirmation, claims suggested to them, their drafts and the decisions they proposed but did not submit, and owner roles with no holder or deputy on their apps.
- **Reviews by role** (as built 2026-11-24; B24-7, narrowed in B24-10). A due review is the person's for one of three reasons: they hold the review type's lead owner role (C24 Default Owner Role) on its application now, a deputy too, or any owner role on it when nobody holds the lead role; they may propose the decision the review records (C24 Produces Decision Type) through a product role they hold now, a responsible, accountable or consulted Decision Right (C31) in the winning group for the application's scope, the engine's proposal right check (B24-5); or an approval rule asks a role they hold for that decision, or for the gate decision of a gate the review is required by (C14 review-done), in the application's scope (OP-02). In Balanced every architecture review reaches the architect (design), every benefit review the PMO (portfolio), and the tier 3 security review the security reviewer, whom the tier 3 go-live asks. A business owner no longer finds the architecture review of an application whose technical owner is someone else. `why` says which reason applies, and `nextTool` is `complete_review` once the review's outcome decision is accepted (null for an AI client).
- **Documents by owner** (as built 2026-11-24, B24-10). OP-06 gives each required document's effective status: missing (no link, computed, never stored), stale (marked stale, or current but past its Freshness Days), or draft (waiting for the person to mark it current). A document goes to its owner (document 23, P07): a link's Document Owner, and for a missing document or a link that names no owner, the application's technical owner, or any of its owners when no technical owner holds now.
- **The page to act on.** Each item's `recordUrl` is the record where its action is: the decision page for an approval, the review for a review, the document link for a stale or draft document, the claim for a claim, the draft itself for a draft, and the application for a missing document (where its link is added) and an owner gap (where an owner change is proposed). A draft application that an intake request carries, as a claim for a new application leaves it, points to that request, which registers it.
- **In:** `{ kinds?: enum[approval|review|document|claim|draft|owner-gap][], top?, skipToken? }`
- **Out:** `{ items: [{ kind, subjectKind, subjectId, title, applicationId, dueOn, status, why, recordUrl, nextTool, isAiCreated, performedBy }] }`. An approval is listed only when the engine's answer check lets the person answer it now (for an AI client, judged for the person it acts for), so a request a separation rule keeps them from answering is not work for them and is left out (B24-23, item 1). Approvals point to the decision page; on the web app's entry the next tool is `answer_approval`, and for an AI client it is null (an AI client never answers). `isAiCreated` and `performedBy` are the item's record's stamps (section 3.4); a draft an AI client made says "Drafted for you" in `why`.
- **Reads:** O06, O07, O09, O10, O11, O19, O21, O23, C22, C23, C24, C31, C26 Reminder Rule. **Writes:** none.

#### 3. `get_role_holders`

- **Purpose:** "Who is on the change board?" Reads the members of the role's bound Entra groups live, as the person. Never stored (doc 20, section 2.8).
- **In:** `{ roleKey: key(C29) }`
- **Out:** `{ role: { key, label }, groups: [{ objectId, displayLabel }], members: [{ objectId, displayLabel }], readFrom: "Entra, live", note }`. On the Dataverse binding the note says group team changes can take up to 8 hours to reach Dataverse (doc 20, S7). A demo install or a local run reads the members from the demo personas: `readFrom` is then `"Demo personas"`, and each group is named for the role it is bound to, such as "Reader group (demo)" (wave 3 verification; D-41, the AI sessions' choice: a fictional group has no name of its own, and naming it for one of its members misled).
- **Reads:** C29, C30, and Microsoft Graph group members through an on-behalf-of token. Graph decides what the person may see. **Writes:** none. **Route:** A only.

#### 61. `find_people` (built 2026-11-24, B24-9)

- **Purpose:** finds a person by name for a picker: a successor, a new owner or deputy, the business owner of an intake request. No page asks for an object ID.
- **In:** `{ text: text(2..100), principalKinds?: enum[user|group|agent-identity|agent-user|service-principal|managed-identity][] = [user], activeOnly?: bool = false, top?: int(1..50) = 20 }`. Every word given must start a word of the name, or the text must start the name.
- **Out:** `{ items: [{ objectId, displayLabel, principalKind, directoryState }], totalCount }`, names that start with the text first, then by name. An erased person is never found. `directoryState` is the account state the last owner check found; `activeOnly` leaves out people found disabled or deleted. A leave date is never returned here: it is HR data, and only a platform admin reads it (`list_owner_assignments`).
- **Reads:** O05 Principal Reference. **Writes:** none. Any role. The display label is an untrusted field.

#### 75. `find_records` (built 2026-11-24, B24-13)

- **Purpose:** "Which record is DEC-0123?" Finds a record by its record number for the web app's global search, so a number read on a card, in an email or in a meeting opens the record.
- **In:** `{ recordNumber: text(3..20) }`, as people write it: DEC-0123, dec 123 or IR0108. The prefix (APP, IR, DEC, CHG, REV or CFG; document 25, B25-9) names the entity; the digits match with or without leading zeros. Another prefix answers `validation`.
- **Out:** `{ items: [{ entity: enum[business-application|intake-request|decision|change-request|review|configuration-draft], recordId, recordNumber, label, status, businessApplicationId, recordUrl }] }`: usually one item, none when no record the caller may read has the number. `links.recordUrl` is the record's page when exactly one is found. `label` is the name, title, statement, review type and application, or draft name: an untrusted field.
- **Reads:** O02, O07, O09, O11, O21, C46 by Record Number. **Writes:** none. Any role. Intake requests follow `intake-visibility`, as `list_intake_requests` does; a hidden request is never found and never counted.

#### 95. `list_teams` (built 2026-11-25, B24-20)

- **Purpose:** "Which teams and committees are there?" Lists every group the install knows as a team: the Entra groups the product holds a Principal Reference for, and the groups a product role is bound to. Each has its kinds, the roles it holds, how many people are in it, how many approval requests wait on it, and the applications it holds owner roles on.
- **Kinds, derived on every read, never stored:** a **committee** is bound to a product role that answers decisions (an Approval Rule asks the role to approve or consults it, or a Decision Right gives it the responsible, accountable or consulted letter); a **support team** holds a current support owner or technical owner assignment; a **role group** is any other bound group; **other** is a known group that is none of these. A group can be a committee and a support team, or a role group and a support team.
- **In:** `{ kind?: enum[committee|support-team|role-group|other], text?: text(1..200), roleKey?: key(C29), memberObjectId?: guid, top?, skipToken? }`. `text` matches the team's name and the names of the roles it holds. `roleKey` finds the group a decision page says it waits on. `memberObjectId` is the person page's "member of": it reads the members of every team.
- **Out:** `{ items: [{ teamId, team: { objectId, displayLabel }, kinds, roles: [{ key, label }], directoryState, memberCount, waitingCount, applicationCount, assignmentCount, ownerGapCount, recordUrl }], counts: { all, committee, supportTeam, roleGroup, other }, membersReadFrom: enum[entra-live|demo-personas|not-read], membership, totalCount, skipToken }`. Committees first, then support teams, role groups and other groups, each by name. `counts` follow the text, role and member filters but never the kind filter, so each count opens its rows. `memberCount` is read for the page's rows only, and is null when the members are not read. `recordUrl` is the team's record link, `/app/records/team/<teamId>`, which opens `/app/teams/<teamId>`.
- **Reads:** O05, C29, C30, C31, C32, O06, O02, O09, O10, and the members through the directory reader: Microsoft Graph as the person, or the demo personas. Never stored. **Writes:** none. Any role; an AI client reads it too. Pending requests on an intake request the caller may not read are counted in `couldNotRead`, as `list_approvals` counts them.

#### 96. `get_team` (built 2026-11-25, B24-20)

- **Purpose:** "Who is on the change board, what does it decide, and what waits on it?" One team's page: its members, the product roles it holds, the decision types it answers, the applications and data it holds owner roles on, what waits on it now, the latest answers its people gave through it, and the owner gaps among its applications.
- **In:** `{ teamId: guid }`: the group's Entra object ID, as `list_teams`, an owner assignment or a role binding names it. A person, or a group the install does not know, answers `not_found`.
- **Out:** `{ team, kinds, directoryState, isKnownToProduct, members: [{ objectId, displayLabel, principalKind }] | null, membersReadFrom, membership, roles: [{ role, party, duties, answersDecisions, binding: { bindingKey, bindingMethod, lastVerifiedOn }, decisionRights: [...], approvals: [...] }], decisionTypes: [{ decisionType, letters, approves, consulted, roleKeys }], applications: [{ businessApplicationId, name, recordNumber, tier, stage, registration, assignments: [{ ownerAssignmentId, ownerRole, isDeputy, startOn }], ownerGapCount, recordUrl }], dataAssignments: [{ ..., subjectKind: enum[data-domain|data-location], subjectId, label, recordUrl }], ownerGaps: [{ businessApplicationId, applicationName, ownerRole, missing, holderDirectoryState, recordUrl }], waiting: [ask], waitingOnMembers: [{ member, request: ask }], membersOwn: [{ member, ownerRole, isDeputy, subjectKind: enum[business-application|data-domain|data-location], subjectId, label, recordUrl }], recentAnswers: [ask], counts }`. Fix round 1 of wave 4 (2026-11-25, D-41): `waitingOnMembers` lists the pending requests asked of one of its members, not of the team (by name, or as the holder of the owner role asked on the decision's application or data domain), so a committee whose lead is asked by name no longer reads "Nothing waits"; `membersOwn` lists what its members own in their own name (up to 200; `counts.membersOwn` counts all); a `decisionTypes` row carries `throughOwnerRole` when its people answer the type as the named holder of that owner role on their own data (the data owners answer the data-use and retention decisions of their domains). Why: the product owner asked for "owners of the apps and data"; a team page that hid what its people are asked and own did not answer it. Labels of a team with no stored name are read from the directory for the rows shown only, and `list_teams` with `memberObjectId` asks the directory once for the person (Graph: the person's transitive groups), not once per team (security review). An `ask` is `{ approvalRecordId, decision: { decisionId, recordNumber, decisionType, statement, status }, subject, askedRole, askedOwnerRole, isConsultation, outcome, approver, requestedOn, respondedOn, expiresOn, isExpired, recordUrl }`; its link is the decision page. `membership` says in one sentence where the members come from, and names the group to open in Entra when the install does not read it.
- **Reads:** as `list_teams`, for one group. **Writes:** none. Any role; an AI client reads it too.

### 6.3 Portfolio toolset

#### 4. `find_applications`

- **Purpose:** finds business applications by text and filters.
- **In:** `{ text?: text(200), portfolioId?: id(O01), applicationKindKeys?: key(C15)[], tierKeys?: key(C08)[], lifecycleStageKeys?: key(C12)[], ownerObjectId?: guid, registrationMeanings?: enum[draft|registered|archived][] = [registered], top?, skipToken? }`
- **Out:** `{ items: [{ applicationId, name, purpose, kind, tier, stage, businessOwner, technicalOwner, isAiCreated, performedBy, recordUrl }], totalCount }`. `purpose` is an untrusted field. `isAiCreated` and `performedBy` are the row's stamps (section 3.4; added 2026-11-24, B24-7).
- **Reads:** O02 Business Application, O01 Portfolio, O06 Owner Assignment, O05. **Writes:** none.

#### 5. `get_application_owners`

- **Purpose:** "Who owns this app?" A small, fast answer: every owner role, holder and deputy, and the gaps.
- **In:** `{ applicationId: id(O02), asOf?: date, includeDeputies?: bool = true }`
- **Out:** `{ owners: [{ ownerRoleKey, label, principal, isDeputy, deputyFor, startOn, endOn, ownershipDecisionId }], gaps: [{ ownerRoleKey, missing: enum[holder|deputy] }], microsoftOwnerDiffers: bool, ownerCheckedOn }`. The Microsoft owner itself is never returned or stored (doc 20, O03 Owner Differs).
- **Reads:** O06, O05, C28 Owner Role, O03 Technical Component. **Writes:** none.

#### 6. `get_application_card`

- **Purpose:** the whole application in one call, so an AI client can answer follow-up questions without ten more calls.
- **In:** `{ applicationId: id(O02), sections?: enum[owners|components|dependencies|decisions|changes|deployments|documents|reviews|links][] = all }`
- **Out:** `{ application: { id, name, purpose, kind, tier: { key, label, decisionId }, stage: { key, label, since, decisionId }, buildPath, usesAi, agreedAutonomyLevel, nextReviewOn, isAiCreated, performedBy }, owners, components: [{ kind, externalId, url, labelAsSeen, lastSeenOn }], dependencies: { dependsOnCount, usedByCount, top: [...] }, decisions: latest 5 (each with `isAiCreated` and `performedBy`), changes: open, deployments: latest per target, documents: status per required type (with the stored `linkKindKey`), reviews: due and last, links }`
- **Reads:** O02, O03, O04, O06, O09, O11, O14, O18, O19, O21. **Writes:** none.
- **Portfolio path, deployment flags and AI marks** (B24-11). `application.portfolio` is the portfolio path from the top of the tree down to the application's own portfolio. Each deployment carries `route`, `flags`, `gateLevelApplied`, `isAiCreated` and `performedBy`, as `get_deployments` gives them, and each open change `isAiCreated` and `performedBy`. The latest tier answers are `get_tier_assessment` (tool 68).
- **Components without a link** (B24-10). Every component that is not retired is listed; `url` is null when the product holds no link for it, such as an item claimed from a list that gave none. Before, such a component was left out, so a claimed item vanished from the card.

#### 7. `get_dependency_impact`

- **Purpose:** "What breaks if it retires?" Walks dependencies in one call, because the walk is recursive and one call is simpler for agents. The Dataverse SQL that the built-in `read_query` tool appears to use has no recursion [T31].
- **In:** `{ one of applicationId: id(O02), componentId: id(O03); direction?: enum[dependants|dependencies|both] = dependants, depth?: int = 3 (at most the impact-max-depth setting), dependencyTypeKeys?: key(C17)[], includeRetired?: bool = false }`
- **Out:** `{ root, nodes: [{ applicationId, name, tier, lifecycleMeaning, businessOwner, technicalOwner, dependencyTypeKey, criticalityKey, depth, path: [applicationId] }], counts: { byTier, byCriticality }, truncatedAtDepth: bool }`. Only dependency types with Counts For Impact = yes are followed (doc 20, C17).
- **Reads:** O04 Dependency, C17 Dependency Type, O02, O06. **Writes:** none.

#### 8. `get_portfolio_summary`

- **Purpose:** counts for a portfolio, from the product's own data, never Power BI (D-09). With `includeOwnerGaps` it also lists every application with an owner role that has no holder or no deputy, across the organization or in one portfolio (P08; 28, issue 11).
- **In:** `{ portfolioId?: id(O01), groupBy?: enum[tier|stage|kind|status|portfolio] = tier, includeOwnerGaps?: bool = false, top?, skipToken? }` (paging applies to the owner gap list)
- **Out:** `{ buckets: [{ key, label, count }], ownerGaps, reviewsOverdue, documentsMissingOrStale, draftsOlderThanDays, ownerGapList?: [{ applicationId, name, portfolio, ownerRoleKey, missing: enum[holder|deputy], holderDirectoryState }] }`. `holderDirectoryState` comes from the last owner check (tool 49).
- **Reads:** O01, O02, O05, O06, C28, O19, O21. **Writes:** none.

#### 9. `get_requirements`

- **Purpose:** "What does my tier need?" The gates, approvals, documents, reviews and gate levels that apply. With an application ID it also says which are already met.
- **Rule selection.** Approvals come from the single most specific matching rule group; broader groups drop out (doc 20, C32; 28, issue 2). A portfolio-scoped row wins over an unscoped one (28, Q2). `explain_rules` (tool 50) shows the trace for one record.
- **In:** `{ one of applicationId: id(O02), scope: { tierKey: key(C08), applicationKindKey?: key(C15), lifecycleStageKey?: key(C12), changeTypeKey?: key(C20), portfolioId?: id(O01) } }`
- **Out:** `{ gates: [{ gateKey, requirements: [{ requirementKind, decisionTypeKey | documentTypeKey | reviewTypeKey, guideline, met? }] }], approvals: [{ decisionTypeKey, approverRoleKey | approverOwnerRoleKey, approvalMode, minimumCount, ownerSubject? }], documents: [{ documentTypeKey, fromStage }], reviews: [{ reviewTypeKey, intervalDays }], gatePolicy: [{ targetStageKey, changeTypeKey, gateLevel, failMode }] }`
- **Reads:** C08, C12, C13, C14, C20, C23, C25, C31, C32, C33, C39, and for an application O09, O19, O21. **Writes:** none.
- **Data decisions (wave 3 verification; D-41).** The data-use decision type is resolved for the subject kind data-use, the way the product opens it about each use, so the list names the domain's data owner and not the application's. `ownerSubject` says where an owner role is held when it is not the application's: `data-domain`, `parent-data-domain`, `data-location` or `touched-data-domains` (C32 Owner Subject). The same optional field is in every `approvalNeeded` preview.

#### 57. `list_reviews` (built 2026-11-24, B24-7)

- **Purpose:** the reviews across the portfolio for the Reviews screen: overdue, due soon, done, by type, application and portfolio, and the architecture queue (every open architecture review). Says whose turn each review is.
- **In:** `{ state?: enum[overdue|due|done|cancelled|all] = due, dueWithinDays?: int (with state due: due within that many days, overdue included), reviewTypeKeys?: key(C24)[], applicationId?: id(O02), portfolioId?: id(O01), mineOnly?: bool = false, top?, skipToken? }`
- **Out:** `{ items: [{ reviewId, reviewType, applicationId, applicationName, portfolio, status, trigger, dueOn, doneOn, nextDueOn, isOverdue, reviewer, leadOwnerRole, leadOwner, producesDecisionType, outcomeDecisionId, isMine, isAiCreated, performedBy, recordUrl }], counts: { overdue, due, done }, totalCount }`. `isMine` (and `mineOnly`) use the same rule as `get_my_queue`: the review type's lead owner role, a product role that may propose the decision the review records, or a role an approval rule asks for it (B24-10). `counts` is for the same filters across the states. Each item also carries its `recordNumber` (REV-0042; null for a review written before the engine numbered reviews). A person marks a review done with `complete_review` (B24-10).
- **Reads:** O21, O02, O01, O06, C24, C31. **Writes:** none.

#### 58. `list_portfolios` (built 2026-11-24, B24-7)

- **Purpose:** the portfolio list for the Portfolios screen, in tree order, with the accountable person and the counts a portfolio review starts from.
- **In:** `{ parentPortfolioId?: id(O01), includeInactive?: bool = false, top?, skipToken? }`
- **Out:** `{ items: [{ portfolioId, name, level, parentPortfolioId, path: [{ portfolioId, name }], accountablePrincipal, isActive, counts: { applications, applicationsWithOwnerGap, reviewsOverdue, openDecisions }, recordUrl }], totalCount }`. Counts include child portfolios and leave out archived applications. A person changes a portfolio with `save_portfolio` (tool 74, B24-11).
- **Reads:** O01, O02, O06, O09, O21. **Writes:** none.

#### 59. `list_owner_assignments` (built 2026-11-24, B24-7)

- **Purpose:** owner assignments across the portfolio for the Owners and people screen: by person (ended ones too), by owner role, by application or portfolio, and the leavers. `get_application_owners` answers for one application and `get_role_holders` reads product role groups, so neither serves this screen.
- **In:** `{ principalObjectId?: guid, ownerRoleKeys?: key(C28)[], applicationId?: id(O02), portfolioId?: id(O01), state?: enum[current|ended|upcoming|all] = current, leaversOnly?: bool = false, top?, skipToken? }`
- **Out:** `{ items: [{ ownerAssignmentId, ownerRoleKey, label, principal, isDeputy, deputyFor, startOn, endOn, ownershipDecisionId, applicationId, applicationName, state, holder: { directoryState, directoryStateOn, leavesOn }, pendingApprovalCount, recordUrl }], coverage: [{ ownerRoleKey, label, requiresDeputy, applicationsHeld, applicationsWithDeputy, gaps }], applicationsInScope, totalCount }`. `leaversOnly` keeps holders with a recorded leave date, or whom the last owner check found disabled or deleted (O05). `pendingApprovalCount` counts the pending approval requests on the application that ask the assignment's owner role: what moves if the holder leaves. The owner gap list itself stays `get_portfolio_summary` with `includeOwnerGaps`.
- **Reads:** O06, O05, O02, O01, O09, O10, C28. **Writes:** none.

#### 63. `get_review` (built 2026-11-24, B24-10)

- **Purpose:** one review for its record page, instead of paging `list_reviews` until it shows up: the review, the evidence linked to it, its outcome decision, its cadence, the gates it feeds and whether the caller may complete it now.
- **In:** `{ reviewId: id(O21) }`
- **Out:** `{ review: { list_reviews' item fields, description }, yourPart: enum[lead|decides|approves] | null, evidence: [{ evidenceLinkId, linkKind, url, label, evidenceLevel, capturedOn, isAiCreated, performedBy }], outcomeDecision: { decisionId, recordNumber, statement, decisionType, status, decidedOn } | null, cadence: { cadenceKey, intervalDays, leadDays } | null, nextDueIfDoneToday: date | null, gates: [{ gateKey, gateLabel, feedsBy: enum[gate-decision|requirement], requirementKey, met, guideline }], canComplete, cannotComplete }`. `yourPart` is why the review is the caller's (the rule of `get_my_queue`). `gates` names each gate the review feeds: through its outcome decision when that is the gate's gate decision (the architecture review's design decision passes the Design gate), and through each review-done requirement (C14) of its type, as OP-07 evaluates it now. `canComplete` is true when `complete_review` would take the caller's call now; otherwise `cannotComplete` says why in plain words. The description, labels and evidence labels are untrusted fields.
- **Reads:** O21, O20, O09, O02, O06, C24, C25, C13, C14, C31, C32. **Writes:** none. Any role.

#### 67. `get_ai_activity` (built 2026-11-24, B24-11)

- **Purpose:** the AI activity screen, the keynote (guidelines G16 and G17): what AI clients did across the estate and who decided.
- **In:** `{ view?: enum[all|drafts|waiting|accepted|withdrawn|refused|answers|code-approvals] = all, mineOnly?: bool, onBehalfOfObjectId?: guid, clientApp?: text(200), businessApplicationId?: id(O02), portfolioId?: id(O01), from?: date, to?: date, top?, skipToken? }`. `portfolioId` keeps the AI work about the portfolio's applications, child portfolios included, or about an intake request in it with no application yet, in the scope the counts and items share; an unknown portfolio is `not_found` (B24-23, item 5).
- **Out:** `{ scope: enum[mine|estate], countsScope: enum[mine|estate], counts: { everything, drafts, waiting, takenForwardByPerson, submittedByAi, editedByPersonFirst, withdrawn, refused, answers, codeApprovals }, byClient: [{ clientApp, count }], byPerson: [{ onBehalfOf, count }], byApplication: [{ businessApplicationId, name, count }], knownClients: [{ key, label, isAiClient, isAllowed }], items: [{ kind: enum[draft|refusal|answer|code-approval], occurredOn, clientApp, onBehalfOf, application, record, state, takenForward: { actorKind, principal, on, fromHistory }, editedByPersonFirst, withdrawn, decided, refusal: { tool, code, ruleKey, reasonCode, ruleIds, rule, knownTool, auditEntryId }, answer, deployment, hasCallHistory }], totalCount }`. A draft is a row an AI client created (Is AI Created); `waiting` means no person took it forward yet, `takenForward` says who submitted it and when, and `editedByPersonFirst` whether a person changed it first. A refusal is a call the product refused an AI client (B24-11). An answer is an approval answer by an AI actor, which never counts. A code approval is a deployment whose only code approval came from an AI. The counts cover every view. `everything` counts each activity once and equals `totalCount` of view all; a deployment an AI client drafted and started is one code-approval activity that is also a draft, so the other counts can add up to one more (B24-23, item 6). `countsScope` says whose work the counts cover: the same as `scope`, except for the PMO, who reads the estate's counts and only the rows done for them, so a page says so beside the figures (wave 3 verification; D-41, the AI sessions' choice: name the scope of the figures rather than hide them from the PMO, since the Overview tile reads as estate-wide).
- **Reads:** every operational entity's AI marks, O24 (composed and refused calls), O10, O14, C45. **Writes:** none. Any role; the AI lead, the security reviewer, the platform admin and the auditor see the estate, anyone else the AI work done for them (document 26, B26-11). Labels are untrusted fields.

#### 74. `save_portfolio` (built 2026-11-24, B24-11)

- **Purpose:** creates or changes a portfolio for the Portfolios screen.
- **In:** `{ portfolioId?: id(O01), name?: text(200), description?: text(2000), levelKey?: key(C07), parentPortfolioId?: id(O01), clearParent?: bool, accountablePrincipalObjectId?: guid, clearAccountable?: bool, isActive?: bool, clientRequestId?, validateOnly? }`
- **Out:** `{ portfolioId, portfolio: { name, level, parentPortfolioId, path, accountablePrincipal, isActive } }` with the write result.
- **Writes:** O01. Served only to a person in the web app. The platform admin creates, moves, names the accountable person and retires; the PMO edits the name, description and level (document 26, section 5.3). A portfolio never sits inside itself or its own children (`rule_violation`), and two siblings never share a name.

### 6.4 Intake toolset

#### 10. `check_duplicates`

- **Purpose:** the duplicate and reuse prompt at the front door.
- **In:** `{ text: text(2000), proposedKindKey?: key(C15), componentExternalIds?: text(400)[] }`
- **Out:** `{ matches: [{ applicationId, name, reason: enum[name|purpose|component|owner], score }] }`
- **Reads:** O02, O03. **Writes:** none.

#### 11. `get_tier_questions`

- **Purpose:** the plain tier questions for a kind of application, with their answers.
- **In:** `{ applicationKindKey?: key(C15) }`
- **Out:** `{ questions: [{ questionKey, text, helpText, answers: [{ answerKey, text, impliedTier: { key, label } }] }] }`
- **Reads:** C09 Tier Question, C10 Tier Answer Option, C08 Tier. **Writes:** none.

#### 12. `save_intake_draft`

- **Purpose:** starts or edits an intake request: a new idea, or an application already built that needs registering. The requester is always the caller.
- **In:** `{ intakeRequestId?: id(O07) (edit an existing draft), title: text(200), need: text(4000), proposedKindKey?: key(C15), businessApplicationId?: id(O02), expectedBenefit?: text(2000), usesAi?: bool, horizonKey?: key(C07), businessOwnerObjectId?: guid (B24-9), clientRequestId?, validateOnly? }`. `businessOwnerObjectId` names the business owner by the object ID `find_people` gives: a person the store knows (O05), signed in as a user, not erased, not found disabled or deleted, and not past a leave date. Their label is always the Principal Reference's, never one the caller typed.
- **Out:** the write envelope, with status Draft and `nextSteps` pointing to `answer_tier_questions` and `submit_record`.
- **Reads:** O02 (duplicate check), O05, C15. **Writes:** O07 Intake Request (status meaning draft, with Business Owner). Only the requester may edit, and only while it is draft. The named business owner counts for the Accept gate's owner requirement, and becomes the application's business owner when the intake is accepted and the application has none (document 25, B25-10).

#### 13. `answer_tier_questions`

- **Purpose:** stores the answers and returns the tier they suggest. A suggestion only: the tier is set by an accepted tier decision, and lowering a tier needs a decision (doc 20, O08).
- **In:** `{ one of intakeRequestId: id(O07), applicationId: id(O02); answers: [{ questionKey: key(C09), answerKey: key(C10) }] }`
- **Out:** `{ tierAssessmentId, computedTier: { key, label, rank }, drivingAnswers: [questionKey], triggers: [{ reviewTypeKey | gateRequirementKey }], note: "A suggestion. The tier is set only by an accepted tier decision." }`
- **Reads:** C09, C10, C08. **Writes:** O08 Tier Assessment.

#### 14. `save_application_draft`

- **Purpose:** registers an application that already exists, as Draft (D-02: an AI-registered application stays draft until a person confirms it). The person confirms on a confirmation card; the AI client's call alone never registers it (working default; open design question 9, see 28-working-defaults.md). Proposed owners become owners only when their ownership decision is accepted.
- **In:** `{ applicationId?: id(O02) (edit a draft), name: text(200), purpose: text(2000), applicationKindKey: key(C15), portfolioId?: id(O01), buildPathKey?: key(C07), usesAi?: bool, proposedOwners?: [{ ownerRoleKey: key(C28), objectId: guid, isDeputy?: bool }], components?: [{ componentKindKey: key(C16), externalId: text(400), url: url, labelAsSeen?: text(200) }], intakeRequestId?: id(O07), clientRequestId?, validateOnly? }`
- **Out:** the write envelope, with registration status draft, the component rows, and `ownershipDecisionId` (Proposed) with the approvals it needs.
- **Reads:** O02, O03 (duplicates), C15, C16, C28. **Writes:** O02 (draft), O03 (links), O06 (pending until the decision), O09 (ownership, Proposed).

#### 15. `link_component`

- **Purpose:** records that an app, flow, agent, repository or Azure resource belongs to an application. A link only (D-15). It also resolves a component the gap check no longer sees: it moved (a new component replaces it) or it is gone (retired). This closes P06 step 8 and P09 step 6 (28, issue 11).
- **In:** `{ applicationId: id(O02), outcome?: enum[linked|moved|retired] = linked, componentKindKey?: key(C16), externalId?: text(400), url?: url, linkKindKey?: key(C37), labelAsSeen?: text(200), deploymentTargetId?: id(O15), replacesComponentId?: id(O03) (moved; sweep proposal), technicalComponentId?: id(O03) (retired), claimId?: id(O23) (the vanished claim it resolves), note?: text(1000), clientRequestId? }`. Linked and moved need the component kind, external ID and URL. Retired needs only the component.
- **Out:** `{ technicalComponentId, outcome, retiredOn?, claimStatus?, created }`. The URL is checked against the link kind's Url Pattern (C37).
- **Reads:** C16, C37, O23. **Writes:** O03 Technical Component (a new row, or Retired On set by the engine; never deleted); O23 (a resolved vanished claim moves to claimed, with Claimed By the person). An AI client may record "retired" as reported; it is a link fact, not a decision (working default; open design question 9, see 28-working-defaults.md).

#### 16. `propose_owner_change`

- **Purpose:** hands over, adds or ends an owner role. The current and new holders confirm on approval cards, as the approval rules say.
- **In:** `{ applicationId: id(O02), changes: [{ ownerRoleKey: key(C28), addObjectId?: guid, endAssignmentId?: id(O06), isDeputy?: bool, startOn?: date }], reason: text(2000) }`
- **Out:** `{ decisionId (Proposed), pendingAssignments: [id(O06)], approvalsNeeded }`
- **Reads:** O06, C28, C32. **Writes:** O09 (ownership, Proposed, with the changes in Proposed Changes Json). O06 changes only when the decision is accepted, by the update-subject effect (document 25, B25-8).
- **Example (sweep proposal):** the AI lead uses this tool, or `save_decision_proposal`, to record "reassign" for an agent Microsoft flags without an owner, with a link to the Agent 365 evidence.

#### 46. `record_owner_leave`

- **Purpose:** records a planned leave or role change for a person who holds owner roles, with a date (P08, announced trigger; 28, issue 11). The engine opens an owner check review for each application where the person holds an owner role. From the leave date, that role's reminders and cards go to the deputy. A successor still needs an accepted ownership decision (`propose_owner_change`).
- **In:** `{ principalObjectId: guid, leavesOn: date, kind: enum[leave|role-change], note?: text(1000), clientRequestId?, validateOnly? }`
- **Out:** the write envelope, plus `{ affected: [{ applicationId, ownerRoleKey, hasDeputy }], ownerCheckReviewIds: [id(O21)], reusedOwnerCheckReviewIds: [id(O21)], pendingApprovals: int }`, with `nextSteps` pointing to `propose_owner_change`. `ownerCheckReviewIds` lists every owner check that covers the leave; `reusedOwnerCheckReviewIds` names the ones that were already due. A check already due stays the one check, and its due date moves to the leave date when it was later.
- **Reads:** O05, O06, C28, O10. **Writes:** O05 Leaves On (as reported); O21 Review of type owner check (the engine creates them). The engine moves pending approvals on the leave date (document 23, P08 step 3).
- **Default roles:** the leaving person, whatever their role; an owner of an application the person owns; platform-admin.
- **Optional sample:** where a client has Entra ID Governance, a Lifecycle Workflows leaver task can call the same contract through the API (28, Q18). The sample's setup names its workload identity and its Workload Grant (C42). The sample is not part of the core.

#### 55. `list_intake_requests` (built 2026-11-24, B24-7)

- **Purpose:** the intake list for the Intake and demand screen: the triage queue, the person's own requests, drafts, and the demand view.
- **In:** `{ view?: enum[triage|mine|drafts|demand|all] = all, statusMeanings?: enum[draft|submitted|in-progress|completed|rejected|withdrawn|cancelled][], text?: text(200), sizeBandKeys?: key[], horizonKeys?: key[], priorityKeys?: text[], scored?: bool, orderBy?: enum[score|priority|size-band|horizon|recent|title], top?, skipToken? }`. triage is submitted requests; mine is requests the person requested or created; demand is requests on the table (submitted, in progress or completed), the highest score first. The demand filters and `orderBy` serve the PMO's demand list (B24-9): `"none"` in `sizeBandKeys` or `horizonKeys` matches a request with no value; `priorityKeys` takes an accepted priority outcome key (Balanced: fund, defer, stop), `"proposed"` or `"none"`; `orderBy` priority puts accepted outcomes first in the order the configuration lists them, then proposed, then none; size band and horizon follow their value lists.
- **Out:** `{ items: [{ intakeRequestId, recordNumber, title, status, requester, proposedKind, applicationId, applicationName, duplicateOfId, usesAi, horizon, sizeBand, score, priority: { decisionId, outcome, status, recordUrl } | null, businessOwner: principalRef | null, suggestedTier, triageDecision: { decisionId, decisionType, status, outcome, approvalsSummary }, decisions: [latest 5, same shape], isAiCreated, performedBy, recordUrl }], totalCount }`. `priority` is the latest priority decision that is not withdrawn; its outcome is the priority only once it is accepted (B24-9). `title` is an untrusted field. `suggestedTier` is the latest tier assessment's Computed Tier, a suggestion only. Rows follow `intake-visibility` (section 2.2); hidden rows are counted in `couldNotRead`.
- **Reads:** O07, O08, O09, O10, O02. **Writes:** none.

#### 56. `get_intake_request` (built 2026-11-24, B24-7)

- **Purpose:** one intake request for its page: the request, the demand fields, the tier assessments, every decision about it (the triage decision marked), and its links.
- **In:** `{ intakeRequestId: id(O07) }`
- **Out:** `{ intake: { the list item's fields (with priority and businessOwner), need, expectedBenefit, duplicateOfName }, tierAssessments: [{ tierAssessmentId, computedTier, assessedBy, tierDecisionId, isAiCreated, performedBy }], decisions: [{ decisionId, decisionType, statement, outcome, status, decider, decidedOn, approvalsSummary, isTriage, isAiCreated, performedBy, recordUrl }], links: [{ linkId, linkCategory, linkKind, url, label }] }`. A request the caller may not read answers `not_found`. `nextSteps` points to `get_decision` for the triage decision.
- **Reads:** O07, O08, O09, O10, O18, O20, O02. **Writes:** none.

#### 60. `score_intake_request` (built 2026-11-24, B24-9)

- **Purpose:** the PMO's demand fields on a request on the table (document 23, P01 step 8): the score, the size band, the horizon and the expected benefit. Priority is a decision, not a field: the PMO proposes it with `save_decision_proposal` (type priority) and its accountable holder answers it on the decision page.
- **In:** `{ intakeRequestId: id(O07), score?: number(0..100), clearScore?: bool, sizeBandKey?: key(C07), horizonKey?: key(C07), expectedBenefit?: text(2000), clientRequestId?, validateOnly? }`. At least one field. `clearScore` clears a score recorded earlier.
- **Out:** the write envelope for the intake request (its status unchanged), with `score`, `sizeBand`, `horizon` and `priority` as recorded, and `nextSteps` pointing to `save_decision_proposal` for a priority decision.
- **Reads:** O07, C07, O09. **Writes:** O07 Score, Size Band, Horizon, Expected Benefit, as reported. Only a request that is submitted, in progress or completed: a draft is the requester's (`save_intake_draft`), and a rejected, withdrawn or cancelled request is off the table (`rule_violation`). The caller must be able to read the request (`intake-visibility`).
- **Default roles:** pmo, platform-admin. **AI write state:** as reported: an AI client may record what the PMO tells it, stamped as AI-created.

#### 68. `get_tier_assessment` (built 2026-11-24, B24-11)

- **Purpose:** the tier panel of Application 360 and the intake record: why the tier is what it is, and what it needs.
- **In:** `{ one of businessApplicationId: id(O02), intakeRequestId: id(O07), tierAssessmentId: id(O08) }`
- **Out:** `{ assessment: { tierAssessmentId, intakeRequestId, businessApplicationId, computedTier: { key, label, rank }, assessedBy, tierDecision, isFrozen, isAiCreated, performedBy, configurationSetVersion } | null, answers: [{ questionKey, question, answerKey, answer, impliedTier, isDriving }], missingQuestionKeys, triggers: { reviewTypes, gateRequirementKeys }, appliedTier, differsFromApplied, vocabulary: [{ vocabulary, term, sourceUrl }], tierNeeds: { documents, reviews }, history }`. OP-01 runs again, read only, over the stored answers to name the driving answers. An application with no assessment of its own shows the one on the intake request it was registered from.
- **Reads:** O08, O07, O02, O09, C08 to C11, C23, C25. **Writes:** none. Any role.

#### 73. `save_dependency` (built 2026-11-24, B24-11)

- **Purpose:** the dependency map's write: this application depends on that application or component, or that dependency ended.
- **In:** `{ dependencyId?: id(O04), fromApplicationId?: id(O02), toApplicationId?: id(O02) | technicalComponentId?: id(O03), dependencyTypeKey?: key(C17), criticalityKey?: key(C07), notes?: text(2000), endedOn?: date, clientRequestId?, validateOnly? }`
- **Out:** `{ dependencyId, fromApplication, to, dependencyType, criticality, endedOn }` with the write result.
- **Writes:** O04, as reported. One open dependency per application, target and type: a save for a pair already linked changes it. OP-08 refuses a dependency that points to its own application or to both or neither target (`dependency.target-invalid`), and any change to an ended one (`record.final`; document 25, B25-13). Owner of the depending application, architect, platform-admin.

### 6.5 Decisions toolset

#### 17. `save_decision_proposal`

- **Purpose:** records a decision for acceptance: what, why, the options weighed, the outcome proposed. The decider is resolved from the decision rights (the accountable holder in C31) and cannot be set by the caller. Configuration and gate policy decisions use `propose_configuration_change` instead.
- **In:** `{ decisionId?: id(O09) (edit while Proposed), decisionTypeKey: key(C18), subjectKind: enum[business-application|intake-request|change-request|deployment-record|technical-component|claim|review], subjectId: id, statement: text(500), context: text(4000), optionsConsidered?: [{ option: text(500), whyNot: text(1000) }], proposedOutcomeKey: key(C19), reason: text(2000), confidenceKey?: key(C07), tierKey?: key(C08) (tier decision only: the tier it sets, B24-8), autonomyLevelKey?: key(C07) (autonomy level decision only, B24-8), exception?: { standard: text(200), reviewOn: date } (exception decision only, and required there, B24-9), supersedesDecisionId?: id(O09), amendsDecisionId?: id(O09), evidence?: [{ linkKindKey: key(C37), url: url, label: text(200) }], clientRequestId?, validateOnly? }`
- **Out:** the write envelope with status Proposed, `decider`, and `approvalsNeeded` resolved from C32.
- **Reads:** C18, C19, C31, C32. **Writes:** O09 Decision (Proposed), O20 Evidence Link.

#### 18. `submit_record`

- **Purpose:** sends a draft or proposed record on: an intake request to triage, a change request to authorization, a decision to acceptance, a configuration draft to acceptance. For a configuration draft the engine freezes the draft, records a configuration change decision (or a gate policy change decision when the draft touches a Gate Policy row) in Proposed, and starts the card (document 21, section 5.1). It starts the approval cards the rules require. It never approves.
- **Cards.** Every decision goes on at least one card, to the holder of its accountable letter (C31), even when its type has no approval rule (28, issue 1). The engine accepts without a card only for a locked list: lowest-tier intake and standard changes whose model a person approved once (working default; open design question 8, see 28-working-defaults.md). If a standard change model applies, the engine applies it and says so.
- **In:** `{ subjectKind: enum[intake-request|change-request|decision|configuration-draft], subjectId: id, note?: text(1000) }`
- **Out:** `{ status: submitted, decisionId?, approvalsRequested: [{ approvalRecordId, requiredRoleKey, approvalMode, expiresOn, channel }], standardChangeApplied?: { modelKey, approvedByDecisionId } }`
- **A check (validateOnly)** (fix round 1 of wave 4, D-41): the record keeps its own status (a draft stays a draft), `approvalsRequested` lists the requests the submit would open with a null `approvalRecordId`, and the summary says "Would be submitted: N approvals would be asked, and M people consulted. Nothing was stored (validateOnly)." Why: the check said "Submitted" with no request listed, while the submit asked four people, so the web sheet's "Check first" could not be shown on stage and an AI client read "Submitted" for a call that stored nothing. A proposal's `approvalsNeeded` (`save_decision_proposal` and the owner tools) names the owner role the submit asks after its fallback (the business owner the request names when no sponsor is named) and lists the consulted roles too, each with `isConsultation`.
- **Reads:** C20, C21, C32, C33, C40. **Writes:** the status of O07, O11, the decision's submission or the Configuration Draft (frozen); O09 for a configuration draft (Proposed); O10 Approval Record rows with outcome pending (the engine creates them); the approval runner starts. Allowed for an AI client only where the Tool Policy row says AI May Submit = yes for that subject kind (working default; open design question 9, see 28-working-defaults.md).

#### 19. `withdraw_record`

- **Purpose:** takes back a record the caller created. Cancels its pending approval cards. Accepted decisions are never withdrawn; a new decision supersedes them (doc 20, rule 6).
- **In:** `{ subjectKind: enum[intake-request|change-request|decision|application-draft|configuration-draft], subjectId: id, reason: text(1000) }`
- **Out:** the write envelope with status withdrawn, and `cancelledApprovals`.
- **Reads:** O10. **Writes:** status of O07, O11, O09 or O02 (draft only), or of a Configuration Draft (discarded, document 21, section 4.2; 28, issue 11). Withdrawing a submitted configuration draft also withdraws its Proposed decision. The engine writes the cancelled outcome on O10 and the approval runner cancels the card (document 22, section 8.2; document 23, S1).

#### 20. `get_decision_log`

- **Purpose:** what was decided, by whom, when and why, with the supersede and amend chain.
- **In:** `{ subjectKind?, subjectId?, applicationId?: id(O02), portfolioId?: id(O01) (B24-11), decisionTypeKeys?: key(C18)[], statusMeanings?: enum[proposed|accepted|rejected|superseded|withdrawn][], from?: date, to?: date, top?, skipToken? }`
- **Out:** `{ items: [{ decisionId, type, subjectKind, subjectId, subjectLabel, statement, reason, exception: { standard, reviewOn } | null, outcome, status, decider, decidedOn, supersedes, supersededBy, amends, approvalsSummary, configurationSetVersion, proposedBy, isAiCreated, performedBy, recordUrl }] }`. `proposedBy` is the Created By Principal: the person, also when their AI client drafted it (added 2026-11-24, B24-7). The exceptions register is this log with `decisionTypeKeys` [exception]: each item names the standard it departs from, its review date and what it covers (`subjectLabel`; a flagged deployment is a deployment-record subject). `portfolioId` keeps decisions about applications in the portfolio, child portfolios included (B24-11).
- **Reads:** O09, O10, C18, C19, C05 Status. **Writes:** none.

#### 21. `get_approval_status`

- **Purpose:** who was asked, who answered, what still counts and what is missing. Shows the approver kind (sweep proposal), so an AI recommendation is visible but never counted.
- **In:** `{ subjectKind: enum[decision|change-request|intake-request], subjectId: id }`
- **Out:** `{ required: [{ approvalRuleKey, requiredRoleKey, approvalMode, minimumCount, received }], records: [{ approvalRecordId, approver, approverActorKind, approverPrincipalKind, outcome, typedReason, respondedOn, countsTowardRequirement, refusedBySeparationRuleKey? }], missing: [...], ready: bool }`. `typedReason` is an untrusted field. An answer does not count when it is not a person's, or when a Separation Rule (C40) refuses it, for example self-approval at tier 3 in Balanced (28, issue 5 and Q10). A consulted role's answer (Is Consultation on C32) shows as a recommendation and never counts (28, issue 12). A settled decision is not evaluated again (B24-10): its records show what the engine stored when it settled, `required` and `missing` are empty, `ready` is true for an accepted one, and `settledBy` says how it was settled: answers (it holds approval records), engine (the locked list, no card), person (withdrawn by its proposer or decider) or recorded (no approval record and no engine stamp: recorded as settled outside the engine, such as the seeded demo estate). A seeded authorization therefore never reads as missing its approvals. `settledBy` is null while the decision is proposed.
- **Reads:** O10, C32, C40, O09, C38 Actor Kind. **Writes:** none.

#### 47. `request_architecture_review`

- **Purpose:** asks for an ad hoc architecture review of an application or a change (P04 step 1, the "request" trigger; 28, issue 11). The engine creates an O21 Review of type architecture, status due, and the review appears in the architect's queue. A request, never a decision: the review ends in a design decision that people accept on cards. If an open architecture review already exists for the subject, the tool returns it and writes nothing new.
- **In:** `{ applicationId: id(O02), changeRequestId?: id(O11), reason: text(2000), dueOn?: date (= the person's day plus the setting review-request-due-days, 14 in Balanced; B24-23, item 8), evidence?: [{ linkKindKey: key(C37), url: url, label: text(200) }], clientRequestId?, validateOnly? }`
- **Out:** the write envelope with status due, plus `{ reviewId, reviewTypeKey: "architecture", trigger: "on request", assignedRoleKey, dueOn }`, with `nextSteps` pointing to `save_decision_proposal` (decision type design) and `record_document_link` (the ADR).
- **Reads:** C24 Review Type, O02, O11, O21. **Writes:** O21 Review (status due, with its trigger; the trigger column is added to document 22 under 28, issue 15); O20 Evidence Link. `reason` is an untrusted field.
- **AI submission:** an AI client may send the request where the Tool Policy row says AI May Submit = yes for subject kind review request (shipped yes; working default; open design question 9, see 28-working-defaults.md).

#### 52. `answer_approval` (built 2026-11-23, D-38)

- **Purpose:** records the signed-in person's own answer to an approval request they were asked, on the product's decision page. The token proves who answered; the input names no responder. Teams and Outlook messages link to the page.
- **Served to:** a person in the product's web app only (`entries: ["person"]`, AI write state person-only). The MCP endpoint never lists it, and an AI client calling it on any entry gets `ai_cannot_do_this`. An app-only token never reaches a tool.
- **In:** `{ approvalRecordId: id(O10), answer: enum[approve|reject], reason?: text(2000) (required to reject, and to approve where the rule asks: AN-08), clientRequestId?, validateOnly? }`
- **Out:** the write envelope for the approval record (status approved or rejected, performed by the person), with `answeredAs { roleKey, ownerRoleKey }`, `isConsultation`, `countsTowardRequirement`, `decisionStatus` after the engine's recompute, and `openedApprovalRecordIds` and `cancelledApprovalRecordIds` of the same transaction.
- **The engine checks** (the approval answer check, then G-06's person path in OP-08): the caller is a person signed in as a user; the decision is proposed; the record is pending and before its Expires On; the person holds its role now, or its owner role on the decision's application now (a deputy too); the person's approval does not already count for the same requirement; no separation rule (C40) keeps the answer from counting, judged as OP-03 judges it; a typed reason where one is needed. A refusal writes nothing. Then OP-03 counts the answer and the engine accepts or rejects the decision when the rule is met, exactly as for the approval runner's answer.
- **Reads:** O10, O09, O06, C32, C40. **Writes:** O10 (the answer); the engine's recompute writes Counts Toward Requirement, may accept or reject O09, cancels the requests still pending, opens the next step or a restart, and writes O24.

#### 53. `get_decision` (built 2026-11-24, B24-7)

- **Purpose:** one decision for the decision page: what it says, who proposed it and whether an AI client drafted it, what it is about, every approval request and answer, what is still missing, the supersede and amend chain, and the evidence. `get_decision_log` has no decision ID filter, so the page needs this read.
- **In:** `{ decisionId: id(O09) }`
- **Out:** `{ decision: { decisionId, recordNumber, decisionType, statement, context, optionsConsidered, confidence, outcome, reason, status, decider, decidedOn, supersedes, supersededBy, amends, amendedBy, configurationSetVersion, proposedBy, exception: { standard, reviewOn } | null, proposedChanges: { owners: [{ ownerRole, add, isDeputy, startOn, endAssignmentId, ends }], tier, autonomyLevel } | null, isAiCreated, performedBy, recordUrl }, subject: { subjectKind, subjectId, label, applicationId, applicationName, recordUrl }, approvals: { decisionId, evaluated, required, records: [{ get_approval_status's record fields, askedRole, askedOwnerRole, askedPrincipal, isConsultation, requestedOn, expiresOn, canAnswer, cannotAnswer: { code, message, separationRule } | null }], missing, ready }, evidence: [{ evidenceLinkId, linkKind, url, label, evidenceLevel, capturedOn }], gate: { gateKey, label, ready, unmet: [{ requirementKey, label, requirementKind, requiredOwnerRoleKey, guideline }] } | null }`. `gate` is the gate whose gate decision type is the decision's type, read as the accept step reads it (OP-07, G-14): a proposed decision whose approvals are all in stays proposed while its gate is not ready, and `unmet` says what holds it. A decision about a review names the review as its subject: the review names the decision as its Outcome Decision (O21), set when the proposal is saved. For a proposed decision the engine evaluates the rules now (OP-02 under the decision's stamped version, OP-03 over its records; `evaluated` true). For a settled decision the records show what the engine stored when it settled; the rules are not run again (`evaluated` false, `required` and `missing` empty). A decision about an intake request the caller may not read answers `not_found`. The statement, context, options, reason, typed reasons and labels are untrusted fields. `proposedChanges` is what acceptance writes on the subject (Proposed Changes Json, B24-8), in labels: the person who starts and the person whose assignment ends, the tier, the autonomy level. `canAnswer` and `cannotAnswer` are the approval answer pre-check for the caller (B24-9): `canAnswer` is true only for a pending record that asks the caller when the engine would take their approval now; a record that asks the caller but that the check refuses carries `cannotAnswer`, with the separation rule (C40) that refuses when one does.
- **Reads:** O09, O10, O20, O02, O07, O11, C18, C19, C31, C32, C40. **Writes:** none.

#### 54. `list_approvals` (built 2026-11-24, B24-7)

- **Purpose:** approval requests across decisions for the Approvals screen, instead of one `get_approval_status` call per proposed decision.
- **In:** `{ view?: enum[mine|asked-by-me|open|answered] = mine, decisionTypeKeys?: key(C18)[], applicationId?: id(O02), portfolioId?: id(O01), decisionId?: id(O09), top?, skipToken? }`. `decisionId` keeps the requests on one decision, for its page. `portfolioId` keeps the requests on decisions about the portfolio's applications, child portfolios included, or, for an intake request with no application yet, the intake's own portfolio; an unknown portfolio is `not_found` (B24-23, item 5). mine is the pending requests the person may answer now (the record's role, or its owner role on the decision's application, deputies too; the engine checks again, with the separation rules, when they answer). asked-by-me is the pending requests on decisions the person proposed, or on the decision of an intake or change they requested. open is every pending request. answered is the requests the person answered.
- **Out:** `{ items: [{ approvalRecordId, decision: { decisionId, decisionType, statement, status, proposedBy, isAiCreated }, subject: { subjectKind, subjectId, label, applicationId, applicationName }, approvalRuleKey, isConsultation, askedRole, askedOwnerRole, askedPrincipal, outcome, approver, typedReason, countsTowardRequirement, requestedOn, respondedOn, expiresOn, otherPendingCount, isOwnProposal, recordUrl, canAnswer, cannotAnswer, nextTool }], totalCount, answerableCount }`. `canAnswer` and `cannotAnswer` are the approval answer pre-check for the caller, as on `get_decision` (B24-9). In view mine the check runs on every request that asks the person: the ones they may answer come first and are counted in `answerableCount` (null in the other views), and a request a separation rule keeps them from answering stays in the list, after them, so the decision page can say why; the summary says how many of those there are (B24-23, item 1). `nextTool` is `answer_approval` only for a request that asks the person and that the pre-check allows, on the web app's entry; for an AI client it is always null. `isOwnProposal` lets the page say that the separation rules (C40) decide whether the person's own answer counts (D-40). Requests about an intake request the caller may not read are counted in `couldNotRead`, except in mine.
- **Reads:** O09, O10, O06, O07, O11, O02. **Writes:** none. Nothing here answers a request.

#### 62. `complete_review` (built 2026-11-24, B24-10)

- **Purpose:** marks a due review done (document 23, P04 step 5 and B23-10): the review ends with its accepted outcome decision, the person as its reviewer, and the next due date from the cadence.
- **Served to:** a person in the product's web app only (`entries: ["person"]`, AI write state person-only), as `answer_approval` is. The reviewer attests the review, and an AI actor never sets done (OP-08, G-08 as document 25, B25-12 widens it).
- **In:** `{ reviewId: id(O21), outcomeDecisionId?: id(O09) (= the one the review names), doneOn?: date (= today, never later), evidence?: [{ linkKindKey: key(C37), url: url, label: text(200) }], clientRequestId?, validateOnly? }`
- **Out:** the write envelope for the review (status done, performed by the person), with `reviewId`, `doneOn`, `reviewer`, `outcomeDecisionId`, `nextDueOn` (Done On plus the C25 interval for the review type at the application's tier; null for an event review such as architecture or post-change), `gates` (as `get_review`) and `evidenceLinkIds`.
- **Checks.** The tool: the review is due; it is the person's (the rule of `get_my_queue`: lead owner role, decision right, or approval rule), else `not_authorized`; it names an outcome decision, and that decision is accepted, else `rule_violation`. The engine again (OP-08, B25-12): Done On, Reviewer and Outcome Decision are set, the decision is accepted, of the type the review type records, about the review's application; a done or cancelled review never changes again.
- **Reads:** O21, O09, O02, O06, C24, C25, C14. **Writes:** O21 Status, Done On, Reviewer, Outcome Decision, Next Due On; O20 Evidence Link. It opens no next review: the S2 due-state refresh opens it on its date, and that timer is not built yet.

#### 65. `get_audit_trail` (built 2026-11-24, B24-11)

- **Purpose:** the audit screen and the history of one record: the product's own audit entries (O24).
- **In:** `{ recordId?: id, principalObjectId?: guid, businessApplicationId?: id(O02), operations?: key[], actorKinds?: enum[...][], from?: date, to?: date, top?, skipToken? }`
- **Out:** `{ items: [{ auditEntryId, occurredOn, operation, what, tool, record: { entity, recordId, label, recordUrl }, actor, actorKind, onBehalfOf, clientApp, correlationId, statusAfter, refusal, hasRequestKey }], totalCount }`, newest first. A record's trail includes its approval requests' entries for a decision. Entries hold IDs; the names come from the store at read time.
- **Reads:** O24. **Writes:** none. platform-admin and auditor only (document 26, sections 3.2 and 5.3).

#### 66. `get_evidence` (built 2026-11-24, B24-11)

- **Purpose:** an evidence pack for one application or one decision (guideline G15), for the auditor and for anyone who has to show the record.
- **In:** `{ one of businessApplicationId: id(O02), decisionId: id(O09); from?: date, to?: date }`
- **Out:** `{ subject, configurationInForce: { configurationSetVersion, contentHash }, versionsStamped, decisions: [{ ..., approvals: [{ askedRole, askedOwnerRole, isConsultation, approver, approverActorKind, outcome, typedReason, requestedOn, respondedOn, countsTowardRequirement, configurationSetVersion }], settledBy, isAiCreated, performedBy, configurationSetVersion }], changes, deployments (as `get_deployments`), reviews, documents, evidenceLinks, counts, truncated }`. Each row names the configuration version it was checked under. Lists stop at 500 rows each.
- **Reads:** O09, O10, O11, O14, O19, O20, O21, C02. **Writes:** none. Any role; a decision about an intake request the caller may not read is left out and counted in `couldNotRead`.

### 6.6 Change toolset

#### 22. `save_change_request_draft`

- **Purpose:** starts or edits a change request. The change management mode is copied from the mode in force for the application's portfolio, where the most specific row wins (D-08; 28, Q2). In linked mode the ITSM change number and URL are required and the built-in approvals are skipped. A recorded number alone never counts as authorization. The client's ITSM tool reports the authorization state through a client-built step; where no step exists, the change manager attests on a confirmation card (28, Q22).
- **In:** `{ changeRequestId?: id(O11), title: text(200), applicationId: id(O02), changeTypeKey: key(C20), standardChangeModelKey?: key(C21), description: text(4000), windowStart?: datetime, windowEnd?: datetime, plannedTargetId?: id(O15), affected?: [{ applicationId: id(O02), impactRoleKey: key(C07) }], externalChangeNumber?: text(100), externalChangeUrl?: url, clientRequestId?, validateOnly? }`
- **Out:** the write envelope with status Draft, the mode, and a requirements preview for the change type, tier and target stage.
- **Reads:** C04 Setting (mode), C20, C21, C33, O15. **Writes:** O11 Change Request (draft), O12 Affected Application.

#### 23. `check_standard_change`

- **Purpose:** checks whether a change fits a pre-approved standard change model. The engine evaluates the model's conditions (document 25, OP-12; 28, issue 12); an AI client never decides that a change is standard (section 6.13).
- **In:** `{ changeRequestId: id(O11) }`
- **Out:** `{ qualifies: bool, model: { key, approvedByDecisionId }, failedConditions: [{ condition, why }] }`
- **Reads:** C21 Standard Change Model, O11, O02. **Writes:** none.

#### 24. `get_change_request`

- **Purpose:** one change with its state: type, mode, window, authorization, approvals, affected apps, deployments and their flags, result and links.
- **In:** `{ changeRequestId: id(O11) }`
- **Out:** `{ change: { ..., createdBy, isAiCreated, performedBy }, authorization: { decisionId, status, approvalsSummary, settledBy, linkedSource?: enum[itsm-step|attestation|none] }, affected, deployments: [{ deploymentRecordId, target, result, flags }], result, closable, postChangeReviews: [{ reviewId, status, dueOn, doneOn }], links }`. In linked mode `linkedSource` says how the authorization was reported; "none" means not authorized (document 25, OP-14; 28, Q22). `settledBy` is as on `get_approval_status`. `closable` is true when `complete_change` would close the change now. `postChangeReviews` are the post-change reviews of this change: O21 has no change column, so the review the engine opens names the change by its record number in its Description, and this read finds it by that (B24-10).
- **Reads:** O11, O12, O09, O10, O14, O18, O20. **Writes:** none.

#### 25. `get_change_schedule`

- **Purpose:** the read-only list of planned changes by window and target (D-13). No booking and no freeze tables.
- **In:** `{ from: date, to: date, targetIds?: id(O15)[], targetStageKeys?: key(C34)[], applicationId?: id(O02), statusMeanings?: enum[draft|submitted|in-progress|completed|rejected|withdrawn|cancelled][] }`
- **Out:** `{ items: [{ changeRequestId, title, application, target, windowStart, windowEnd, changeType, status, authorized: bool, authorizationStatus }] }` sorted by window start. `authorizationStatus` is the authorization decision's own status (proposed, accepted, rejected or withdrawn), null with no decision yet (B24-8).
- **Reads:** O11, O15, O02. **Writes:** none.

#### 26. `record_change_result`

- **Purpose:** records how a change ended, as reported. The tool opens a post-change review when the change type Requires Review After (C20, such as an emergency) or the result is failed, rolled back or partly succeeded, due Review Due Days working days later (C20 and the `working-days` setting; 14 days when the type sets none), once per change (B24-10: the build read a flag name no preset has and always gave 14 days). A closed change keeps its result. It never closes the change: a person does that with `complete_change`.
- **In:** `{ changeRequestId: id(O11), resultKey: key(C07), note?: text(2000), evidence?: [{ linkKindKey: key(C37), url: url, label: text(200) }] }`
- **Out:** the write envelope, and `postChangeReviewId` when one was created.
- **Reads:** C20, C24, the `working-days` setting. **Writes:** O11 Result (as reported), O20, O21 Review.

#### 64. `complete_change` (built 2026-11-24, B24-10)

- **Purpose:** closes a change with its result (document 23, P03 step 8). The change moves to completed (Balanced labels it Closed) and leaves the open part of the schedule.
- **Served to:** a person in the product's web app only (`entries: ["person"]`, AI write state person-only). Moving a record to completed is a person's act, and an AI actor never sets it (OP-08, G-08).
- **In:** `{ changeRequestId: id(O11), resultKey?: key(C07) (= the result recorded with record_change_result), evidence?: [{ linkKindKey: key(C37), url: url, label: text(200) }], clientRequestId?, validateOnly? }`
- **Out:** the write envelope for the change (status completed, performed by the person), with `result`, `postChangeReviewId` and `evidenceLinkIds`.
- **Checks.** Only an authorized change closes: one in progress (Balanced: Scheduled), or a submitted change whose type Allows Authorization After (C20, an emergency; document 22, O11 Authorization Decision). A draft, a change waiting for its authorization, a rejected, withdrawn or closed change answers `rule_violation`. A result is required. The post-change review opens as for `record_change_result`, once.
- **Reads:** O11, C20, C24. **Writes:** O11 Status and Result; O20; O21 Review.

### 6.7 Deployment toolset

#### 27. `record_deployment`

- **Purpose:** records that an artifact version reached a target, when the owner's AI tool reports it (D-19). The same contract is the record endpoint for pipeline steps (section 4.6). The engine sets the route and computes the flags; the caller cannot set them.
- **Reported code approvals** (28, issue 11). The caller may report who approved the code merge. They are stored as evidence (O14 Reported Approvals Json) and never count toward the product's approval rules. AI-only code approvals never count as authorization, and the shipped gate default flags them (working default; open design question 7, see 28-working-defaults.md).
- **In:** `{ idempotencyKey: text(200) (required), applicationId?: id(O02), artifactName: text(200), version: text(100), artifactKindKey?: key(C07), digest?: text(128), sourceCommit?: text(64), sourceRepositoryUrl?: url, one of targetId: id(O15), targetExternalId: text(400); changeRequestId?: id(O11), pipelineReferenceId?: id(O16), runUrl?: url, startedOn?: datetime, finishedOn: datetime, resultMeaning: enum[succeeded|partly-succeeded|failed|not-deployed], versionBefore?: text(100), versionAfter?: text(100), deployedByObjectId?: guid (= the caller), mergeActorObjectId?: guid (sweep proposal), stageOrderRun?: text(100)[] (sweep proposal), codeApprovalKind?: enum[person|ai-only|none|unknown] = unknown, reportedApprovals?: [{ approverObjectId?: guid, approverKind: enum[person|ai|unknown], approvedOn?: datetime, sourceUrl?: url }] }`
- **Out:** `{ deploymentRecordId, artifactVersionId, created, route: { key, meaning: "ai-tool" | "person-in-app" }, flags: { outsideWindow, otherPath, missingApproval, aiOnlyCodeApproval }, gateLevelApplied }`
- **Reads:** O11, O15, C33, C35 Change Path, C36 Deployment Route. **Writes:** O13 Artifact Version (upsert by name plus version), O14 Deployment Record (upsert by idempotency key, I-189, with Reported Approvals Json).

#### 48. `record_deployment_correction`

- **Purpose:** corrects a deployment record without editing it (P05 step 7; 28, issue 11). A deployment record is final at insert (document 22), so linking it to its change after the fact, or fixing a wrong fact, adds a new record whose Corrects Record names the old one. The old record stays; views show the latest correction. The engine computes the flags for the new record; the caller cannot set them.
- **In:** `{ correctsDeploymentRecordId: id(O14), idempotencyKey: text(200) (required), reason: text(1000), changeRequestId?: id(O11), resultMeaning?: enum[succeeded|partly-succeeded|failed|not-deployed], version?: text(100), finishedOn?: datetime, runUrl?: url }`. Fields left out are copied from the corrected record.
- **Out:** `{ deploymentRecordId, correctsRecordId, created, flags: { outsideWindow, otherPath, missingApproval, aiOnlyCodeApproval }, gateLevelApplied }`
- **Reads:** O14, O11, O15, C33, C35, C36. **Writes:** O14 Deployment Record (a new row with Corrects Record set; upsert by idempotency key). `reason` is an untrusted field.
- **Pipelines** send corrections to the record endpoint with the same contract (section 9.2).

#### 28. `get_deployments`

- **Purpose:** "What runs where?" The latest version per target, or the history, with flags.
- **In:** `{ applicationId?: id(O02), targetId?: id(O15), changeRequestId?: id(O11) (B24-11), latestOnly?: bool = true, flaggedOnly?: bool = false, flagKeys?: enum[outside-window|other-path|missing-approval|ai-only-code-approval][] (B24-11), resultMeanings?: enum[succeeded|partly-succeeded|failed|not-deployed][] (B24-11), from?: date, to?: date, top?, skipToken? }`
- **Out:** `{ items: [{ deploymentRecordId, application, target, targetStage, artifactName, version, result, finishedOn, route, flags, gateLevelApplied, runUrl, correctsRecordId, changeRequestId, pipelineReferenceId, versionBefore, versionAfter, startedByAiClient, isAiCreated, performedBy, configurationSetVersion, recordUrl }], counts: { records, flagged, outsideWindow, otherPath, missingApproval, aiOnlyCodeApproval }, totalCount }`. It is also the Deployments list (B24-11): `latestOnly` false gives the history, `flagKeys` keeps records with any of the named flags, and `counts` is for the same filters before the flag filters and paging. `flaggedOnly` keeps records with any flag: outside window, other path, missing approval or AI-only code approval. The AI-only code approval flag is stored on the record now, like the other three (document 22, B22-10), so it shows after the call that recorded it, not only in that call's answer.
- **Reads:** O14, O13, O15. **Writes:** none.

#### 29. `map_pipeline`

- **Purpose:** maps one of the client's pipelines to the applications and targets it deploys. The product never builds, hosts or runs a pipeline (D-19).
- **In:** `{ pipelineReferenceId?: id(O16), linkKindKey: key(C37), externalId: text(400), url: url, labelAsSeen?: text(200), entries: [{ applicationId: id(O02), targetId: id(O15), stageOrder: int }] }`
- **Out:** `{ pipelineReferenceId, entries: [id(O17)] }`
- **Reads:** C37 (pipeline category only). **Writes:** O16 Pipeline Reference, O17 Pipeline Map Entry.

#### 30. `get_pipeline_map`

- **Purpose:** which pipelines deploy an application, in which stage order.
- **In:** `{ one of applicationId: id(O02), pipelineReferenceId: id(O16) }`
- **Out:** `{ pipelines: [{ pipelineReferenceId, label, url, isMoved, entries: [{ application, target, stageOrder }] }] }`
- **Reads:** O16, O17, O15. **Writes:** none.

#### 70. `list_deployment_targets` (built 2026-11-24, B24-11)

- **Purpose:** the Places screen: where deployments land and what runs there.
- **In:** `{ targetKindKeys?: key[], targetStageKeys?: key(C34)[], text?: text(200), top?, skipToken? }`
- **Out:** `{ items: [{ deploymentTargetId, label, targetKind, targetStage: { key, label, rank }, changePath: { key, label, allowedRoutes }, linkKind, externalId, url, lastSeenOn, applications: [{ businessApplicationId, name, artifactName, artifactVersion, result, finishedOn, deploymentRecordId }], pipelines, deploymentCount, flaggedCount, isAiCreated, performedBy, recordUrl }], totalCount }`, by stage. `applications` is the latest deployment of each application to the target.
- **Reads:** O15, O14, O13, O16, O17, C34, C35. **Writes:** none. Any role; external IDs and URLs are null for a caller who holds only the reader role (document 26, section 6.2).

#### 71. `save_deployment_target` (built 2026-11-24, B24-11)

- **Purpose:** registers a place deployments land, or changes one. Process P05 needs it: `record_deployment` takes only a registered target.
- **In:** `{ deploymentTargetId?: id(O15), targetKindKey?: key(C07), targetStageKey?: key(C34), changePathKey?: key(C35), linkKindKey?: key(C37), externalId?: text(400), url?: url, labelAsSeen?: text(200), description?: text(2000), clientRequestId?, validateOnly? }`. A new target needs all but `url` and `description`.
- **Out:** `{ deploymentTargetId, target }` with the write result; `target` as `list_deployment_targets` gives it.
- **Writes:** O15. Served only to a person in the web app; platform-admin. Link Kind and External Id are the business key: a second target with the same pair is refused and named. A change of stage or change path leaves earlier records as recorded and warns `target.rules-changed`.

#### 31. `check_change_approval` (Later, after the gate decision D-03)

- **Purpose:** answers the check step's question: "does this change have the approvals its policy requires?" The pipeline acts on the answer at Advise or Enforce. The product never stops anything itself.
- **In:** `{ changeRequestId?: id(O11), applicationId: id(O02), one of targetId: id(O15), targetExternalId: text(400); artifactName?: text(200), version?: text(100), emergency?: bool = false }`
- **Out:** `{ gateLevel, approved: bool, missing: [...], failMode, emergencyPath, message }`
- **Reads:** O11, O10, C33. **Writes:** L05 Gate Check (logs the question and the answer).

### 6.8 Documentation toolset

#### 32. `get_documentation_status`

- **Purpose:** "What documentation is missing or stale for my app?" Required documents come from configuration (D-17). "Missing" is computed from configuration. A Document Link row exists only once a link has been recorded (28, issue 16).
- **In:** `{ one of applicationId: id(O02), mine: true; statusMeanings?: enum[missing|draft|current|stale][] }`
- **Out:** `{ applications: [{ applicationId, required: [{ documentTypeKey, fromStage, requirementKey }], documents: [{ documentLinkId, documentTypeKey, url, linkKindKey, status, owner, lastUpdatedOn, lastCheckedOn, staleOn }], missing: [documentTypeKey], stale: [documentLinkId] }] }`. `linkKindKey` is the kind the link was recorded with, so a form that updates the link can send it back.
- **Reads:** C22 Document Type, C23 Documentation Requirement, O19 Document Link, O02. **Writes:** none.

#### 33. `record_document_link`

- **Purpose:** records the link and status of a document the owner's own AI tool wrote, for example a wiki page written through the Azure DevOps MCP server. The product never writes the document (D-17) and never assumes the owner's write succeeded (sweep proposal): it stores what the caller reports.
- **In:** `{ documentLinkId?: id(O19), applicationId: id(O02), documentTypeKey: key(C22), url: url, statusMeaning?: enum[draft|current] = draft, lastUpdatedOn?: date, documentOwnerObjectId?: guid (= the caller), linkKindKey?: key(C37) }`
- **Out:** the write envelope with `staleOn` computed from the document type's freshness.
- **Reads:** C22, C23, C37. **Writes:** O19 (link, status as reported). `linkKindKey` left out takes the document type's default on a new link and keeps the stored kind on an update, so a confirm never checks a stored address against another kind's pattern. The first recorded link creates the row (28, issue 16). One link meets a documentation requirement of an application (C23), so a second new link for a requirement that already has one is refused with `rule_violation`, naming the link to update by its `documentLinkId` (fix round 2, 2026-11-25: it reached the store before and answered 500). How far an AI client may set the status is the Tool Policy's Max AI State. The shipped default allows "current" when the caller owns the document or the app. For a document type with Current Needs Person = yes (C22), a "current" from an AI client is stored as draft until the person confirms on a confirmation card (working default; open design question 9, see 28-working-defaults.md).

#### 34. `confirm_document_checked`

- **Purpose:** the owner confirms a document is still true, or marks it stale.
- **In:** `{ documentLinkId: id(O19), stillTrue: bool, note?: text(1000) }`
- **Out:** the write envelope with the new status and `staleOn`.
- **Reads:** C22. **Writes:** O19 Last Checked On and status. From an AI client, "still true" on a document type with Current Needs Person = yes follows the same confirmation card rule as tool 33 (working default; open design question 9, see 28-working-defaults.md).

#### 69. `list_documents` (built 2026-11-24, B24-11)

- **Purpose:** the Documents screen: the documentation list across the portfolio.
- **In:** `{ state?: enum[all|missing|stale|draft|current] = all, documentTypeKeys?: key(C22)[], businessApplicationId?: id(O02), portfolioId?: id(O01), mineOnly?: bool, top?, skipToken? }`
- **Out:** `{ items: [{ businessApplicationId, applicationName, documentType, state, documentLinkId, url, owner, lastCheckedOn, staleOn, requirementKey, requiredFrom, isAiCreated, performedBy, recordUrl }], counts: { missing, stale, draft, current }, totalCount }`, missing first. Missing is computed by OP-06 for each registered application in scope, never stored. `mineOnly` keeps the documents the person owns and the missing documents of applications they own.
- **Reads:** O02, O19, C22, C23. **Writes:** none. Any role.

### 6.9 Gap check toolset

#### 35. `run_gap_check`

- **Purpose:** "What is running that is not in the register?" Runs live, as the signed-in admin (D-15). The server exchanges the admin's token for a Power Platform API token and queries the inventory. Only unregistered items become claims. Registered components the check no longer sees become "vanished" claims (sweep proposal). The engine compares the result with the register and runs the claim lifecycle (document 25, OP-13; 28, issue 12).
- **In:** `{ gapCheckRunId?: id(O22) (continue a run), estateKeys?: key(C07)[] = all covered estates, environmentIds?: text(100)[], includeVanished?: bool = true, compareOnly?: bool = false (return the result, write no claims), skipToken? }`
- **Out:** `{ gapCheckRunId, adminRoleUsed, estatesCovered, itemsSeen, newClaims: [{ claimId, componentKind, labelAsSeen, targetAsSeen, suggestedClaimant }], vanished: [{ claimId, technicalComponentId }], lastSeenUpdated, more: bool, skipToken }`
- **Reads:** the Power Platform inventory, live through `POST {PowerPlatformAPI url}/resourcequery/resources/query` [T18]; O03 External Ids; O23; C16 Source Type Hint. **Writes:** O22 Gap Check Run, O23 Claim (open), O03 Last Seen On.
- **Who can run it:** the product role `platform-admin`, and Microsoft's own rule on top: six Entra roles can query the inventory, and only Global administrator, Power Platform administrator, Dynamics 365 administrator and Global reader see everything; AI administrator and AI reader see only agents, agentic apps, agent flows, environments and environment groups (doc 20, S6). Whether the two AI roles can use the inventory API is UNVERIFIED. The tool reads the admin's directory roles from the token's `wids` claim when present to fill Admin Role Used; whether that claim arrives in the server's token depends on the app registration's optional claims (UNVERIFIED).
- **Route:** A only.
- **In a demo** (built 2026-11-25, B24-23, item 2): a demo install and a local run with the directory mode Demo read the demo's fictional inventory instead of Microsoft's (the Azure host's `DemoInventoryReader`), so the gap check can be run in a demo. `adminRoleUsed` reads "demo: the demo's fictional inventory".

#### 36. `compare_inventory_list`

- **Purpose:** the fallback for routes B and C and for estates the server cannot read itself. The admin's own tool fetches the list as the admin (for example `pac resource-query`, preview [T18]) and passes it in. The product compares and writes claims. It cannot prove the list came from Microsoft, so the run is marked evidence level "as reported".
- **In:** `{ estateKey: key(C07), items: [{ externalId: text(400), componentKindKey?: key(C16), sourceType?: text(200), labelAsSeen: text(200), targetAsSeen?: text(200), suggestedClaimantObjectId?: guid, url?: url }] (up to 1000), sourceNote: text(500) }`. An item's `url` (https) is stored on its claim (O23 Url) and refreshed when the item is seen again; a claim copies it to the component it creates (B24-10). The live inventory gives no link, so `run_gap_check` leaves it empty.
- **Out:** as `run_gap_check`, with `evidenceLevel: "as reported"`. An item with no component kind the gap check covers, or from another estate, is left out by the comparison (OP-13, rule 5); the answer then carries the warning `gap-check.items-left-out` with the count, so "0 items seen" never stands alone (fix round 2, 2026-11-25). The web app's sheet names a pasted line with no kind and does not send it.
- **Reads:** O03, O23, C16. **Writes:** O22, O23.

#### 49. `run_owner_check`

- **Purpose:** the on-demand owner check (28, Q18 and issue 11). It runs live as the signed-in admin, like the gap check (D-15). The server reads, from Microsoft Graph through an on-behalf-of token, the account state and leave date of every principal with an active owner assignment. Disabled, deleted or leaving owners become findings, and the engine opens an owner check review for each affected application (document 23, P08 step 1). No unattended job.
- **In:** `{ gapCheckRunId?: id(O22) (continue a run), applicationIds?: id(O02)[], principalObjectIds?: guid[], skipToken? }`
- **Out:** `{ gapCheckRunId, principalsChecked, findings: [{ principal, directoryState: enum[enabled|disabled|deleted|unknown], leavesOn, applications: [{ applicationId, ownerRoleKey, hasDeputy }], ownerCheckReviewId }], more: bool, skipToken }`
- **Reads:** O06, O05, C28; Microsoft Graph users (account state, leave date) as the admin. **Writes:** O22 Gap Check Run with estate key `entra-owners` (document 23); O05 Directory State and Leaves On (as reported); O21 Review of type owner check (the engine).
- **Who can run it:** the product role `platform-admin`, and Graph's own rules on top. Reading a leave date needs `User-LifeCycleInfo.Read.All` and, delegated, the Lifecycle Workflows Administrator or Global Reader role (document 23, P08). The least permission for account state is UNVERIFIED (section 17).
- **Route:** A only.
- **In a demo (wave 4b, D-41):** with the directory mode Demo the account state comes from the demo's
  fictional directory (the host's DemoDirectoryReader): the directory state and leave date the store's Principal
  Reference records, enabled for a persona with none, unknown for anyone else. The run records the admin role used as
  "demo: the demo's fictional directory", and the summary reads "N owners checked in the demo's fictional directory, M
  findings." Reason: the web app offered "Check the owners" on every demo install and it always answered that the
  directory was not connected, so no demo could play it; the gap check reads the demo's fictional inventory the same
  way (tool 35).

#### 37. `list_claims`

- **Purpose:** the claim list for owners. Rows follow `claim-visibility`, which defaults to "admins and claimant" (28, issue 10).
- **In:** `{ statusMeanings?: enum[open|claimed|dismissed][] = [open], suggestedToMe?: bool, findingKinds?: enum[unregistered|vanished][], componentKindKeys?: key(C16)[], top?, skipToken? }`
- **Out:** `{ items: [{ claimId, findingKind, componentKind, labelAsSeen, targetAsSeen, url, suggestedClaimant, claimedBy, claimedOn, resultingComponentId, applicationId, ownershipDecisionId, intakeRequestId, foundOn }] }`. A claimed item names where it went: the application that holds its component, the ownership decision about it, and the intake request a claim for a new application started (B24-10).
- **Reads:** O23, O22, O03, O07, O09. **Writes:** none.

#### 38. `claim_resource`

- **Purpose:** an owner claims a found item (document 23, P06 step 6, as built in B23-10). For an existing application the component is added. When the caller holds no owner role on it, an ownership decision about the claim asks its owners to confirm the item belongs there: a person's claim submits it at once, so the cards go out; an AI client's claim leaves it proposed for the person to submit (warning `claim.decision-not-submitted`). For a new application the claim starts an intake (P01): a draft application holds the component, and a draft intake request names it, with the claimant, or the person given, as business owner. Triage decides; its acceptance registers the draft and makes the named person its business owner, so no ownership decision is opened.
- **In:** `{ claimId: id(O23), one of applicationId: id(O02), newApplication: { name: text(200), purpose: text(2000), applicationKindKey: key(C15), portfolioId?: id(O01), businessOwnerObjectId?: guid (= the claimant) } }`
- **Out:** `{ claimStatus: claimed, technicalComponentId, applicationId, applicationStatus, intakeRequestId, ownershipDecisionId, decisionSubmitted, approvalsRequested, engineAccepted, approvalsNeeded }`, with `nextSteps` to `answer_tier_questions` and `submit_record` for the intake, or `submit_record` for a decision left proposed.
- **Reads:** O23, O02, O06, O05. **Writes:** O23 (claimed), O03 (link, with the claim's link), O02 (draft, when new), O07 (draft, when new), O09 (ownership, Proposed, when needed), O10 (when submitted).

#### 39. `dismiss_claim`

- **Purpose:** records that a found item is not a business application, with the reason. Reversible.
- **In:** `{ claimId: id(O23), reason: text(1000), reasonKey?: key(C07), undo?: bool = false }`
- **Out:** the write envelope with status dismissed or open.
- **Reads:** O23. **Writes:** O23 status and Dismiss Reason.

#### 72. `list_gap_check_runs` (built 2026-11-24, B24-11)

- **Purpose:** the gap check's run history.
- **In:** `{ runByObjectId?: guid, from?: date, to?: date, top?, skipToken? }`
- **Out:** `{ items: [{ gapCheckRunId, runBy, adminRoleUsed, estatesCovered, startedOn, finishedOn, state: enum[finished|not-finished], itemsSeen, newClaims, vanishedClaims, openClaimsLastSeen, openVanishedLastSeen, isAiCreated, performedBy, recordUrl }], totalCount }`, newest first. `openClaimsLastSeen` counts the open unregistered claims last seen in the run and `openVanishedLastSeen` the open vanished ones, so the first matches the "Not registered" count (B24-23, item 7). A run from `compare_inventory_list` stays not finished until a list names its final page.
- **Reads:** O22, O23. **Writes:** none. The roles of `list_claims`; under `claim-visibility` admins-and-claimant (Balanced) only the platform admin, the AI lead and the auditor see the runs, and anyone else gets them counted in `couldNotRead`.

### 6.10 Configuration toolset

#### 40. `get_configuration`

- **Purpose:** what rules are in force, row by row, and what changed between two versions.
- **In:** `{ entity?: text(64) (an entity key such as "tier" or "gate-policy"), keys?: text(64)[], version?: int (= in force), compareToVersion?: int, includeRetired?: bool = false }`
- **Out:** `{ configurationSet: { key, version, publishedOn, contentHash }, rows: [{ entity, key, label, isActive, origin, fields }], diff?: [{ entity, key, op: enum[added|updated|retired], before, after }] }`
- **Reads:** every configuration entity: C01 to C39 and C40 to C46 (doc 20, section 2.10). **Writes:** none.

#### 50. `explain_rules`

- **Purpose:** "Why does this need X?" Returns the engine's rule trace for one record (28, issue 11): which rule groups matched, which single most specific group won and which broader groups dropped out (doc 20, C32; 28, issue 2), the gate policy row, the separation rules (C40) and the flags. The engine re-runs the evaluation in read mode against the configuration set version stamped on the record, so the answer matches what the engine did.
- **In:** `{ subjectKind: enum[decision|change-request|intake-request|deployment-record|business-application], subjectId: id, aspects?: enum[approvals|gate|flags|requirements|separation][] = all, version?: int (= the version stamped on the record) }`
- **Out:** `{ configurationSetVersion, engineVersion, aspects: [{ aspect, operation, winner: { entity, key, scope }, dropped: [{ entity, key, why }], trace: [{ step, entity, key, matched: bool, why }] }], summary }`
- **Reads:** the subject record; the C02 snapshot it is stamped with; the engine's evaluation result (document 25: outcome, reasons, rule trace). **Writes:** none.
- **Binding:** on Dataverse the custom API `<prefix>_ExplainRules` (a function).

#### 51. `get_configuration_health`

- **Purpose:** the state of the configuration as a whole (28, issue 11): the active version and its history, drafts that wait, the engine's own version, and whether the snapshot loads and validates. An upgrade draft from a product release shows here too, waiting for a configuration admin (28, Q5).
- **In:** `{ includeHistory?: bool = true, includeDrafts?: bool = true, top?, skipToken? }`
- **Out:** `{ active: { version, activatedOn, contentHash, decisionId }, engine: { engineVersion, snapshotSchemaVersion, snapshotHash, loaded: bool }, versions: [{ version, status: enum[active|superseded], activatedOn, decisionId, summary }], drafts: [{ configurationDraftId, status, baseVersion, submittedOn, expiresOn, isUpgradeDraft }], findings: [{ ruleKey, message }], reviewDueOn }`
- **Reads:** C01, C02, Configuration Draft, C04 (`draft-expiry-days`, `configuration-review-days`), and the engine's info (document 25). **Writes:** none.
- **Activation date** (B24-23, item 3): a version's `activatedOn` is its Published On, else its Engine Checked On, else the moment of the oldest audit entry, and only then now. The seed writes Published On on version 1, so "in force since" and `reviewDueOn` never move with the clock.
- **Binding:** on Dataverse the custom API `<prefix>_GetConfigurationHealth` (a function). It includes what document 25's `<prefix>_GetEngineInfo` returns.

#### 41. `list_presets`

- **Purpose:** the ready-made configuration sets a client can start from (D-07). A client picks one full preset plus declared overlays, such as small team or strict separation, and the result is validated as one set (28, Q3). The Microsoft CoE baseline preset ships in the first release, extending Balanced, and is re-checked against Microsoft's guidance each release (28, Q6).
- **In:** `{}`
- **Out:** `{ presets: [{ presetKey, label, presetKind: enum[full|overlay], extends, conflictsWith, productRelease, summary: { tiers, gateLevelsByTierAndStage, changeManagementMode, approvalsSummary } }] }`
- **Reads:** C03 Preset, C01. **Writes:** none.

#### 42. `propose_configuration_change`

- **Purpose:** turns an admin's instruction into a validated configuration draft (D-10). The AI client prepares the operations. The engine validates them against the engine contract and the locked settings. The result is the candidate: a patch stored on a Configuration Draft row (document 21; 28, issue 3). No configuration table changes, and no C02 row is written.
- **Then.** `submit_record` freezes the draft, records a Proposed configuration change decision (or a gate policy change decision when the draft touches a Gate Policy row) and starts the card. Nothing is in force until a person accepts that decision on one card showing the engine's diff. The instructing admin may accept their own AI's draft; strict separation needs a second admin (working default; open design question 4, see 28-working-defaults.md). The engine then creates the new C02 version inside the activation operation (document 21, section 4.4).
- **In:** `{ configurationDraftId?: id(Configuration Draft) (edit a draft not yet submitted), baseVersionNumber: int, proposalRationale: text(2000), one of operations: [{ entity: text(64), op: enum[upsert|retire] (the patch operations of document 21, section 8.2), key: text(64), fields: object }], preset: { presetKey: key(C03), overlayKeys?: key(C03)[] }, fileContent: text(2000000) (seed-format JSON, as from export_configuration); validateOnly?: bool = false, clientRequestId? }`
- **Out:** `{ valid: bool, errors: [{ entity, key, ruleKey, message }], configurationDraftId?, diffSummary: [{ entity, key, op, before, after }], contentHash, impact: { applicationsAffected, byChange: [{ entity, key, applicationsAffected, sampleApplicationIds }], gateChanges: [{ tierKey, changeTypeKey, targetStageKey, from, to }], recordsInFlight }, approvalsNeeded, activation: "Submit the draft. A person accepts the decision on an approval card. Nothing changes until then." }`
- **Effect on live applications** (28, issue 11). `impact` is returned also with `validateOnly`, so the admin sees which applications a change would touch before anything is stored. Records already in flight keep the requirements frozen at their submission (document 21, section 4.7), so `recordsInFlight` only counts them.
- **Refuses:** any change to a locked setting (C04 Is Locked), including the AI approval lock; any change to an engine contract meaning (doc 20, section 2.4); removing a key that rows still use (retire it instead); a stale `baseVersionNumber` (code `conflict`).
- **Reads:** every configuration entity; O02 and O11 for the impact. **Writes:** a Configuration Draft row (status draft) only.

#### 43. `export_configuration`

- **Purpose:** returns a configuration set version as seed-format files, so the configuration can live in a repository and come back through `propose_configuration_change` (D-10: "or a configuration file").
- **In:** `{ version?: int (= in force) }`
- **Out:** `{ files: [{ path: "config/presets/<set-key>/<entity-key>.json", content }], contentHash }` (paths follow doc 20, section 3.6)
- **Reads:** every configuration entity (C01 to C46). **Writes:** none.

#### 44. `check_role_bindings`

- **Purpose:** the direct assignment check (D-11). Confirms each product role is bound to an Entra group that exists, and flags any product role held outside a bound group, including by agent accounts, agent users or service principals (sweep proposal). It also lists the Workload Grants (C42): each pipeline or approval runner identity that holds a workload role directly. It flags a direct assignment with no grant, an expired grant, and any grant to an agent identity (working default; open design question 12, see 28-working-defaults.md).
- **In:** `{ includeDirectAssignments?: bool = true }`
- **Out:** `{ bindings: [{ roleKey, groupObjectId, bindingMethod, groupExists, lastVerifiedOn }], workloadGrants: [{ principal, principalKind, roleKey, reason, decisionId, expiresOn, assignmentFound }], directAssignments: [{ principal, principalKind, roleKey, where, coveredByGrant }], findings: [text] }`
- **Reads:** C29, C30, C42; Microsoft Graph through an on-behalf-of token as the admin; on Dataverse, user and team role assignments as the admin; on the Azure binding, app role assignments through Graph. **Writes:** O24 Audit Entry only. **Route:** A only.

### 6.11 Links toolset

#### 45. `save_link`

- **Purpose:** links a record to work or proof held elsewhere: an Azure DevOps work item, a GitHub issue, a Planner plan (unified or legacy premium), a test result, an assessment export, an impact list (sweep proposal). Also marks a link as moved, for example after a repository migration (sweep proposal).
- **In:** `{ linkId?: id(O18 or O20), linkCategory: enum[delivery|evidence], subjectKind: enum[business-application|change-request|intake-request|decision|approval-record|deployment-record|review|claim], subjectId: id, linkKindKey: key(C37), externalId?: text(400), url: url, label: text(200), evidenceLevelKey?: key(C07) (evidence only), digest?: text(128), movedToUrl?: url }`
- **Out:** `{ linkId, created }`. Delivery links accept only application, change request and intake request subjects; evidence links accept the others (doc 20, O18 and O20).
- **Reads:** C37 (Url Pattern, Id Pattern, Link Category). **Writes:** O18 Delivery Link or O20 Evidence Link.

### 6.12 Resources and prompts

- **Resources (route A only, R1):** read-only MCP resources that cite, never copy. `guideline://G<n>` returns the guideline's number, version, one-line rule and hub link (C39). `configuration://in-force` returns the configuration set key, version and content hash. `playbook://chapter/<n>` returns the chapter title and link only (D-17). Routes B and C offer tools only.
- **Prompts:** later, with the playbook skills package (D-18), so the playbook's steps live in one place.

### 6.13 Not tools, on purpose

| Not a tool | Why | Where it happens instead |
|---|---|---|
| Approve, reject, accept, authorize | AI never approves (D-02, D-10) | A person answers on the product's decision page through `answer_approval`, served only to a person in the web app (D-38); Teams and Outlook link there. The engine accepts a decision when the counted answers meet the rule |
| Set a tier, move a stage, mark a change standard | These are decisions | An accepted decision of the right type; the engine applies a pre-approved standard change model |
| Activate configuration | Nobody activates by hand; activation follows the accepted decision (D-10; document 21, section 4.4) | Acceptance of the configuration change decision on an approval card |
| Assign or remove a role, add a group member | Membership is administered in Entra (D-11) | Entra and Entra ID Governance |
| Write configuration rows directly | Live configuration rows change only inside the activation operation (28, issue 3) | `propose_configuration_change` stores a draft; activation follows the accepted decision |
| Delete anything | Records are append-only where it matters; others are retired or withdrawn | `withdraw_record`, `record_deployment_correction`, `link_component` with outcome retired, retire through configuration |
| Write a wiki page, a work item or a pipeline | The product never writes into other tools (D-17, D-19) | The person's AI client, through the Azure DevOps or GitHub MCP servers, as the person |
| Import Microsoft's inventory on a schedule | Links, not copies (D-15) | `run_gap_check`, on demand |
| Generic table read and write | That is the Dataverse MCP server's job on Dataverse (D-16) | The Dataverse MCP server, under the same table rules |

### 6.14 Data toolset (built 2026-11-24, B24-16)

The data taxonomy (D-37, settled by D-41; see [data taxonomy](../../architecture/data-taxonomy.md#mcp-tools-and-rest)). The tree, its levels, the
classifications and the platforms are configuration (C47 to C50); locations, mappings and uses are records (O25 to O27).
Owners come from OP-15, decision states from OP-16 and findings from OP-17, each run inside the tool's own composed call.

#### 76. `find_data_domains`

- **In:** `{ text?, levelKeys?, classificationKeys?, ownerObjectId?, gapsOnly?, includeRetired? = false, top?, skipToken? }`
- **Out:** `{ items: [{ key, label, path, parentKey, level, isActive, declarable, classification: { key, label, rank, setOn }, owner: { principal, ownerAssignmentId, inheritedFrom }, deputy, steward, rules: { use, ai, copy } each { value, setOn, fromClassification }, counts: { mappings, uses, openDecisions }, gaps, recordUrl }], totalCount, skipToken? }`. Tree order: each level by Sort Order, then label.

#### 77. `get_data_domain`

- **In:** `{ domainKey, sections?: [owners|mappings|uses|decisions|matches|gaps] }`. A section left out is null.
- **Out:** `{ domain: { ..., pathKeys, ownerRequired, description, examples (untrusted), retention, impliedAnswer, children, counts }, owners: [{ ownerRole, inheritsDown, holder, otherHolders, deputies, inheritedFrom, acting }], mappings, uses, decisions, matches (C11 rows to Fabric or Purview domains), gaps }`. A holder shows `isLeaving`, never the leave date.

#### 78. `find_data_locations`

- **In:** `{ locationId?, platformKeys?, stageKeys?, parentLocationId?, text?, unmappedOnly?, staleOnly?, includeRetired? = false, top?, skipToken? }`
- **Out:** locations with platform, parent, effective stage, isProduction, deployment target, hosted-by application, link kind, external ID and URL (null for a caller who holds only the reader role), last seen, status, technical owner (`fromParent` for a store), the domains mapped there, the child count and the flags draft, stale, unmapped, noTechnicalOwner and copyOutsideProduction.

#### 79. `resolve_data`

- **In:** one of `locationId`, `externalId` or `url`, plus `platformKey?` and `container?`.
- **Out:** `{ location, matches: [{ domain, dataMappingId, origin, containers, containerMatched, owner, classification, rules }], unmapped }`. A location nobody registered answers `location: null`, never an error: the build loop then registers it.

#### 80. `check_data_use`

- **In:** one of `applicationId` or `intakeRequestId`; `domainKey`, or `locationId` with `container`; `access`, `reachesAi`, `isDeidentified?`.
- **Out:** `{ state, domain, location, classification, governingRule: { name, value, setOn, fromClassification }, configurationSetVersion, decisionNeeded, refusedWhenSaved, decisionTypeKey, approvalsNeeded, approvers, impliedAnswer, existingDataUseId, explanation }`. `refusedWhenSaved` is true for a never rule (G-25).

#### 81. `get_data_gaps`

- **In:** `{ domainKey?, includeSubtree? = true, applicationId?, locationId?, principalObjectId?, checks?, severities?, top?, skipToken? }`
- **Out:** `{ counts: { DC-01 ... DC-14 }, items: [dataFinding with fixTool and recordUrl], totalCount, skipToken? }`, highest weight first.

#### 82. `save_data_use`

- **In:** `{ dataUseId?, applicationId, domainKey, locationId?, access, purpose, reachesAi, aiServiceAsSeen?, aiComponentId?, isDeidentified?, accessApproachKey?, startOn?, endOn?, confirm?, proposeDecision? = true, clientRequestId?, validateOnly? }`
- **Out:** the write result plus `{ dataUseId, recordNumber, replacedDataUseId, use, governingRule, decisionId, decisionSubmitted, approvalsNeeded, approvalsRequested }`.

#### 83. `save_data_location`

- **In:** `{ items: [{ dataLocationId?, platformKey, parentLocationId?, targetStageKey?, deploymentTargetId?, hostedByApplicationId?, linkKindKey?, externalId?, url?, labelAsSeen, seenNow?, confirm?, retire? }] (1 to 100), clientRequestId?, validateOnly? }`
- **Out:** the write result of the first item plus `{ items: [{ dataLocationId, created, matchedExisting, status, label, platform, recordUrl }], createdCount, updatedCount }`.

#### 84. `save_data_mapping`

- **In:** `{ items: [{ mappingId?, domainKey, locationId, containers?, origin, copiedFromMappingId?, endOn?, confirm? }] (1 to 100), proposeCopyDecision? = true, clientRequestId?, validateOnly? }`
- **Out:** the write result of the first item plus `{ items: [{ dataMappingId, created, matchedExisting, status, domain, dataLocationId, origin, copyState, decisionId, decisionSubmitted, approvalsNeeded, recordUrl }] }`.

#### 85. `move_data_references`

- **In:** `{ fromDomainKey, toDomainKey, include: [mappings|uses], reason, clientRequestId?, validateOnly? }`
- **Out:** the write result of the first row moved plus `{ fromDomain, toDomain, movedMappingIds, movedUseIds, changedUses: [{ dataUseId, stateBefore, stateAfter }], ownerChange }`. `ownerChange` is null when both domains answer to the same data owner; otherwise `{ newDataOwner, redeclaredCount, reaskedCount, openedCount, withdrawnAnsweredCount, withdrawnNoOwnerCount, waitingForOwnerCount }`, what the move did or, in a preview, would do. A preview lists the rows that would move by their own IDs.
- **A new data owner is asked (wave 3 verification).** When the two domains answer to different data owners, a proposed decision nobody answered is asked again from the start of the new owner (the engine's `ReaskApprovals`), one somebody answered is withdrawn by the engine and a new one goes to the new owner, and a decided row is declared again in the new domain with its new decision sent. The summary says what happened to the decisions; an AI client's preview says what would happen and names its client in `performedBy` (a choice under D-41).
- **Whoever moves the rows, the new owner is asked (wave 4 carry-over).** The engine opens each decision a moved row needs as its own nested write for the mover (`OpenDataDecision`), so the data governance lead, who holds no data-use proposal right, moves rows as the platform admin does. An AI client's preview is the person's own preview: the tool body runs first as the person the AI client acts for, in a dry run that commits nothing, so the duplicate check and every guard judge it as they judge the person's (a refusal there is the AI client's answer). A moved use that would duplicate an open use of the new domain is refused with `data.use-duplicate` on `toDomainKey`, in the words of a move (choices under D-41).

#### 97. `list_data_uses` (built 2026-11-25, B24-21)

- **In:** `{ domainKey?, includeSubtree? = true, businessApplicationId?, dataUseIds? (1 to 100), states?, reachesAi?, accessKeys?, text?, includeEnded? = false, top?, skipToken? }`
- **Out:** `{ items: [dataUseItem], counts: { all, byState: { <each of the eight states>: n } }, totalCount, skipToken? }`. The items are the ones `get_data_domain` lists under uses, by application, then the domain's place in the tree. `counts` are for every filter but `states`, so each count opens its rows. `dataUseIds` reads given uses whatever their state or end date (a use's record page); an ID with no row is `not_found`.

#### 98. `list_data_mappings` (built 2026-11-25, B24-21)

- **In:** `{ domainKey?, includeSubtree? = true, dataLocationId?, dataMappingIds? (1 to 100), originKeys?, copyStates?, text?, includeEnded? = false, top?, skipToken? }`
- **Out:** `{ items: [dataMappingItem], counts: { all, copies }, totalCount, skipToken? }`, in tree order, then by location. A copy outside production carries its copy state.

---

## 7. AI never approves: how the surface enforces it

The rule holds in five layers. Safety never depends on a prompt or on the client behaving.

1. **No decision tools.** Section 6.13 lists what no tool can do. An AI client has no tool to call for it. The one tool that records an approval answer, `answer_approval`, is served only to a person in the web app (D-38): the MCP endpoint never lists it, and the engine refuses it from any actor but a person signed in as a user.
2. **An engine contract that configuration cannot loosen.** Whatever the tool or route, the engine refuses these writes when the actor kind is "AI client for a person", "agent identity" or "pipeline": a decision status of accepted or rejected, any approval outcome, registration status registered, the current tier, the current stage, a new configuration set version (activation), a configuration table row, and a role binding. The "AI approval lock" setting ships locked (doc 20, C04 Is Locked). "AI never approves" in the product is decided (D-02, D-10). Whether AI-only approvals from client tools ever count is open: the working default is that they never count (working default; open design question 7, see 28-working-defaults.md). Tool Policy's Max AI State can only tighten this, never widen it.
3. **Approval outcomes come only from the approval runner, or from the person the request asks.** The approval writer workload role writes Outcome, Responded On and Typed Reason from a card, protected by column security (doc 20, O10; 28, issue 7 and Q15); since D-38 a person writes their own answer through `answer_approval`, and only after the engine's approval answer check allowed it for that person (B24-6). Only an answer from a person who holds the required role counts: Approver Actor Kind must be person and Approver Principal Kind must be user (sweep proposal). A Separation Rule (C40) can refuse an answer too (28, Q10).
4. **The decision act is not callable at all.** On both bindings no API lets a caller mark a decision accepted. The engine marks it accepted when the counted approvals meet the rule. So on Dataverse, where the actor kind of a route C call cannot be verified (section 4.5), an AI client still cannot accept anything: there is no door to push.
5. **Submission only starts cards.** Where Tool Policy allows AI submission (working default; open design question 9, see 28-working-defaults.md), the submission is stamped "AI client for a person" and starts approval cards that only people answer. The engine accepts without any card only for the locked list in tool 18 (working default; open design question 8, see 28-working-defaults.md).

**Residual risk.** The product cannot stop an approver's own AI from answering a card in another tool on the approver's behalf. Whether any Microsoft tool can answer a Power Automate approval for a person is UNVERIFIED (section 17, item 12). A typed reason and the audit trail reduce the risk. The approvals spike tests that an agent identity cannot complete a card (sweep proposal).

---

## 8. The three routes compared

### 8.1 Route by route

| Concern | A: the product's own server | B: Microsoft's MCP management server | C: the Dataverse MCP server's `invoke_api` |
|---|---|---|---|
| Exists on | Both bindings | Dataverse only: it builds tools from Dataverse custom APIs, connectors, Graph and REST APIs [T10] | Dataverse only: `invoke_api` calls Dataverse custom APIs [T8] |
| Status | The product's code. The Azure services are GA, except features marked preview in section 11 | Preview. "Microsoft might change preview MCP tool names and parameters" [T10]. The Microsoft 365 roadmap item says GA "December CY2026" [T37] | `invoke_api` is in the tool list of the GA endpoint `/api/mcp` [T8]. One Agent 365 page still labels the server preview [T12] |
| Hosting | The adopter's Azure subscription | Microsoft hosts it at `https://agent365.svc.cloud.microsoft/mcp/environments/{environmentId}/servers/...` [T10] | Microsoft hosts it at `https://{org}.crm.dynamics.com/api/mcp` [T8] |
| Who builds the tool list | The product, in code | The setup service calls `CreateToolWithCustomAPI` per tool, then `PublishMCPServer` to tenant scope [T10] | Nobody: the server discovers published custom APIs [T27] |
| Identity to the engine | On-behalf-of token as the person (section 4.4) | UNVERIFIED: the reference does not say whose identity a custom API tool runs under (section 17, item 1) | The signed-in person; the server "respects Dataverse security roles and row-level security" [T32] |
| How Claude Code connects | Directly, as a remote server with Entra OAuth: CIMD or a registered client (section 4.2) | UNVERIFIED. Publishing creates a `-PublicClients` app registration for "client hosts (such as Visual Studio Code and the Microsoft Copilot CLI)" [T11] | The `@microsoft/dataverse` local proxy, after one-time tenant admin consent and enabling the "Dataverse CLI" client; or the remote endpoint with the adopter's own Entra app holding `mcp.tools`, added as an allowed client [T9] |
| CIMD | Supported for "Custom enterprise-built MCP server" [T1] | UNVERIFIED | "Not supported yet" [T1] |
| Tool surface | Only the product's 64 tools (three of them for a person only), filtered by role and toolset | A curated list of the product's custom APIs | 16 generic tools plus `invoke_api`; the client must find the custom API by name with `search` and `describe` [T8] |
| Input shapes | Rich JSON Schema per tool | Limited to custom API parameter types; nested input travels as one JSON string (section 9.1) | Same as B |
| Resources and prompts | Yes (section 6.12) | No such tools in the reference [T10] (inference) | No |
| Actor stamping | Reliable on Azure; asserted by the server on Dataverse (section 4.5) | As C (UNVERIFIED) | The plug-in cannot tell an MCP call apart (UNVERIFIED) |
| Gap check, owner check and Entra reads | Yes, with on-behalf-of tokens to the Power Platform API and Graph | No: a custom API cannot call the inventory as the person; Graph and REST tool identity is UNVERIFIED. Fallback: `compare_inventory_list` | No. Fallback: `compare_inventory_list` |
| Cost per call | Azure consumption, plus the person's Power Platform request limits on Dataverse. Whether these Web API calls carry any AI charge is UNVERIFIED (D-16) | UNVERIFIED | Charged "when accessed by AI agents created outside of Microsoft Copilot Studio": 1 Copilot Credit per 10 responses for every tool except `search_data`, which is usage-based [T8][T32][T33] |
| License for people | Dataverse: a license per person (section 10). Azure: none from Power Platform | Dataverse: a license per person | Dataverse: a license per person |
| Admin steps | App registration and consent, the host, allowlists (section 12) | Publishing creates Entra app registrations, so the account needs `Application.ReadWrite.All`; "Publishing an MCP server doesn't approve it"; an AI Administrator or Global Administrator approves it in the Microsoft 365 admin center and grants consent [T11][T12] | Turn the server on per environment and allow each client [T34] |
| ALM | Its own release, which names the engine version it needs | Whether server definitions are solution-aware is UNVERIFIED | In the solution with the custom APIs |
| Main risk | The on-behalf-of flow must be right; the adopter must run Azure | Preview; unknown identity and licensing | Billing per call; generic tools sit next to raw table writes; weak actor stamping |

### 8.2 Route per toolset (Dataverse binding)

"Works" means the toolset can run on that route. All three routes reach the same custom APIs, so the rules are the same on every route (D-10).

| Toolset | A | B | C | Recommendation |
|---|---|---|---|---|
| session | Works | `whoami` works; `get_role_holders` no (needs Graph as the person) | Same as B | A |
| portfolio | Works | Works | Works, charged per call | A; C is the fallback |
| intake | Works | Works | Works | A |
| decisions | Works | Works | Works | A |
| change | Works | Works | Works | A |
| deployment (MCP tools) | Works | Works | Works | A. Pipelines use the record endpoint, not MCP (section 4.6) |
| documentation | Works | Works | Works | A |
| gapcheck | Works | `run_gap_check` and `run_owner_check` no; `compare_inventory_list` works | Same as B | A only for the live checks |
| configuration | Works | Works except `check_role_bindings` | Same as B | A, because validation output and diffs are large and structured |
| links | Works | Works | Works | A |

### 8.3 The Azure binding

Only route A exists. The server, the API and the engine run as one service on Azure Functions Flex Consumption (.NET 10), with Container Apps as the fallback (28, issue 13), and the web page calls the same operations through `/api`. Azure App Service built-in MCP (preview) can turn an OpenAPI description into MCP tools with no MCP code [T6]. It is a variant of route A, not proposed: every published operation becomes a tool, and "Built-in MCP doesn't enforce authorization on the underlying HTTP routes" [T26], so the product would lose the per-tool policy and the response envelope.

### 8.4 If the on-behalf-of spike fails

On the Dataverse binding the fallback is the Dataverse MCP server plus custom APIs (route C), with route B added when it reaches GA. The tool contracts in section 6 do not change, because they are the custom API contracts. What is lost: `get_role_holders`, `check_role_bindings`, `run_owner_check` and the live `run_gap_check` (replaced by `compare_inventory_list`), resources, rich input schemas and reliable actor stamping. On the Azure binding there is no fallback route. If the on-behalf-of or MCP spike fails only on Functions, the service moves to Container Apps (28, issue 13). If the on-behalf-of flow fails on both hosts, the spike is a go or no-go for that binding.

---

## 9. Platform bindings for each tool

### 9.1 Dataverse binding

- **One custom API per tool.** The unique name is the publisher prefix plus the tool name in PascalCase: `get_dependency_impact` becomes `<prefix>_GetDependencyImpact`. The unique name "must include a customization prefix", "can't contain any special characters" and "Cannot be changed after it is saved" (doc 20, S12).
- **Reads are functions, writes are actions.** All are Global, so generative pages, `invoke_api`, the management server and the product's own server call the same API (doc 20, section 3.4).
- **Parameter types.** Custom API parameters can be Boolean, DateTime, Decimal, Entity, EntityCollection, EntityReference, Float, Integer, Money, Picklist, String, StringArray or Guid [T23]. The product uses only Boolean, DateTime, Decimal, Integer, String, StringArray and Guid. Never Entity or EntityCollection, because generative pages do not support "Custom APIs that use the EntityCollection binding type or Entity parameters" (doc 20, S13).
- **Nested inputs** (lists of objects, such as `answers`, `operations` or `proposedOwners`) travel as one String parameter, `payloadJson`, which the plug-in validates against the same JSON Schema the server publishes. On route A the server builds `payloadJson` from the rich input, so the AI client never sees it. On routes B and C the client must write the JSON string itself.
- **Outputs.** One String `resultJson` holding the envelope's `data`, plus scalar outputs `recordId` and `statusMeaning` for pages.
- **Privileges.** Developers outside Microsoft cannot create new privileges [T35], so the plug-ins check the caller's roles and owner roles in code.
- **Copilot Managed Runtime.** Its Microsoft-managed default environment group blocks custom API actions [T38]. This affects runtime apps and pages that call the engine, not the product's MCP server, which calls the Web API.

### 9.2 Azure binding

- **One engine operation per tool,** reachable as an MCP tool at `/mcp` and as `POST /api/<tool-name>` for the web page. Same JSON contract on both.
- **The engine is a library behind a storage port,** so the same operation code could run against Dataverse if the Dataverse binding is added later (D-26).
- **The record endpoint** `POST /api/deployments` accepts app-only tokens with the `Deployment.Record` app role and nothing else. Corrections go to `POST /api/deployments/corrections` with the `record_deployment_correction` contract and the same app role.
- **The approval runner** writes outcomes through the API with the `Approval.Write` app role (the approval writer, section 4.3).
- **The host** is one Azure Functions app on Flex Consumption (.NET 10) for the MCP endpoint, the API and the engine; Container Apps is the fallback (section 11).

---

## 10. Licensing and cost

### 10.1 On Dataverse

- **Every person who reaches the data through the MCP server needs a license.** Microsoft defines multiplexing as using "hardware or software that a customer uses to pool connections, reroute information, or reduce the number of users that directly access or use Power Apps, Power Automate, and Microsoft Copilot Studio", and says "Using multiplexing as a mechanism to reduce the number of licenses to purchase is a license violation" [T14]. A server that calls Dataverse for many people is exactly that kind of software, so it changes nothing about who needs a license.
- **Tables with code plug-ins need licensed users.** Users of an app or flow over such tables need a Power Apps or Power Automate license [T39]. The engine uses plug-ins (D-10).
- **Which license, exactly, for MCP-only use is UNVERIFIED.** No Learn page names it. Plan for Power Apps Premium for every such person; for the price, see Microsoft's [Power Apps pricing page](https://www.microsoft.com/en-us/power-platform/products/power-apps/pricing).
- **Route C adds Copilot Credits** per tool call outside Copilot Studio (section 8.1). Route A adds Azure consumption. Whether route A's Web API calls carry any AI charge is UNVERIFIED (D-16).
- **Approvers** who only answer cards do not need a Premium license: "Users who respond to approval requests don't need a Premium license" [T14].

### 10.2 On the Azure binding

- **No Power Platform license** for people who use the product through MCP or the web page. They need an Entra account in the tenant.
- **Entra ID P1 or P2** for group-based app role assignment (doc 20, S9). Without it, the engine reads group membership through Graph instead (D-26).
- **Azure consumption:** the host and Azure SQL. For prices, see Microsoft's [Azure Functions pricing](https://azure.microsoft.com/pricing/details/functions/) and [Azure SQL Database pricing](https://azure.microsoft.com/pricing/details/azure-sql-database/single/) pages, or the [Azure pricing calculator](https://azure.microsoft.com/pricing/calculator/).
- **Approval cards** come from Power Automate approval cards sent by a runner flow group that carries one Process license (28, Q19). The product's own approval page is offered where a client's security review asks for token-level evidence of the approver.

### 10.3 Both bindings

The MCP client brings its own subscription, for example a Claude plan. The Azure DevOps MCP server, which people's AI clients use to write work items and wiki pages, is free to use; standard Azure DevOps pricing applies [T29].

---

## 11. Hosting options for the server

The working default (28, issue 13): on the Azure binding, one service on Azure Functions Flex Consumption (.NET 10) hosts the API, the MCP server and the engine. Container Apps is the fallback if the on-behalf-of or MCP spike fails on Functions. Spike test 13 confirms the choice.

| Option | What Learn says | Fit | Proposed for |
|---|---|---|---|
| Azure Functions with the MCP extension, Flex Consumption plan | "Map function triggers to MCP tools"; a good fit when "Each tool is independent and stateless" and "You want per-invocation pricing"; "For interactive MCP clients, use a Functions Premium plan to avoid cold-start latency on the Consumption plan" [T6]. Flex Consumption supports always ready instances; Premium keeps "perpetually warm" instances and always has at least one billed instance [T7] | On the Dataverse binding, each tool is one small function that calls one custom API. On the Azure binding, one app hosts the MCP endpoint, the API, the engine and the record endpoint. One always ready instance covers interactive clients | Both bindings (for the Azure binding, 28, issue 13) |
| Azure Container Apps (standalone) | "Deploy any MCP server you build as a container with HTTP ingress"; built-in Entra authentication; "For interactive MCP clients ... set a minimum replica count of 1"; "the most flexible default" [T6] | One container hosts the MCP endpoint, the API, the engine and the record endpoint | Fallback for the Azure binding, if the spike fails on Functions |
| Azure App Service | Build your own server, or built-in MCP from OpenAPI (preview) [T6] | Possible; built-in MCP rejected in section 8.3 | Not proposed |
| Azure Kubernetes Service | For teams that already run a cluster [T6] | Too much to run for one small service | Not proposed |

**Settings that apply to either host.**
- Built-in authentication on, unauthenticated calls answered with 401 and the protected resource metadata link [T2]. On Functions, replace the default system key with built-in MCP authorization [T40].
- One always ready instance (Functions) or one minimum replica (Container Apps) for interactive clients.
- Telemetry to Application Insights: tool name, outcome, latency, client app, a hash of the caller's object ID and the correlation ID. Never the token and never free text people typed.
- A public HTTPS endpoint if the adopter wants Agent 365 bring-your-own registration, which requires "a publicly accessible endpoint" [T13]. A private endpoint is possible otherwise.
- The product ships the code and an infrastructure template; the adopter's Azure team runs it (D-26).

---

## 12. What adopters must allow

These are the places an adopter's administrators allow the server. The product publishes the values they need: the server URL (`https://<host>/mcp`), the protected resource metadata URL, the Application ID URI, the scope `mcp.access`, the app roles, the tool names in section 6.1, and the Claude Code CIMD URL.

| Layer | Product | Entry the adopter adds | Status | Source |
|---|---|---|---|---|
| The device or the organization | Claude Code managed settings | `allowedMcpServers: [{ "serverUrl": "https://<host>/mcp" }]` with `allowManagedMcpServersOnly: true` for an approved catalog; or `managedMcpServers` to provide the server to every user (Claude Code v2.1.259 or later). Use `serverUrl`, because "A `serverName` entry ... is not a security control". With `strictPluginOnlyCustomization` covering `mcp`, ship the server entry in a plugin instead | Vendor docs | [V1] |
| The organization | GitHub Copilot enterprise managed settings | `allowedMcpServers` with a `serverUrl` entry in `managed-settings.json`, which GitHub calls "the more secure, generally available method". The MCP registry allowlist policy (registry URL, for example in Azure API Center, plus "Registry only") is public preview | Vendor docs | [V3][V4] |
| The network | Entra Global Secure Access MCP firewall | An Allow rule by Server URL, and optional tool-level rules by tool name. Needs a Microsoft Entra Internet Access license, TLS inspection and the Global Secure Access client on Entra joined devices. Inspects remote servers over streamable HTTP only | Preview | [T16] |
| The device | Defender for Endpoint AI agent runtime protection | Pilot in Audit mode (`AiAgentProtection` = 2) before Block (= 1). Claude Code is covered through its hooks. Known issue: in Block mode, "Claude Code might continue processing a tool response" after a reported block | Preview | [T17] |
| The tenant | Entra ID | Consent for the server's app registration and scope. For CIMD: an administrator adds the Claude Code CIMD client in advance, because default user consent covers only `openid` and `User.Read` for CIMD clients. For registered clients: preauthorize each one | CIMD preview; app registration GA | [T1][T2] |
| The environment (route C only) | Dataverse MCP server | Turn on the server; enable the "Dataverse CLI" client or the adopter's own app ID. "MCP allow listing applies only to the `/api/mcp` agent entrypoint", so it never restricts the custom APIs themselves | GA endpoint | [T9][T34] |
| The tenant (optional) | Agent 365 bring-your-own MCP server | Register with the Agent 365 CLI using `EntraOAuth`; an administrator approves it and grants consent. Supported preview clients: Copilot Studio, Visual Studio Code, Claude Code, GitHub Copilot CLI. "You can't republish new versions of your remote MCP server during preview". Calls through the gateway appear in Defender advanced hunting as `ExecuteToolByGateway` | Preview | [T13] |
| The registry (optional) | Azure API Center | A remote MCP server entry. It can also serve as the MCP registry GitHub Copilot reads | Registration GA; GitHub registry policy preview | [V3][T41] |

---

## 13. Spike tests (the on-behalf-of spike)

Each test runs from Claude Code with at least two product roles, on the chosen binding (Azure, D-26). Tests that name the Dataverse binding or routes B and C run only if that binding is added later.

1. Claude Code signs in through CIMD and through a registered client; both reach `whoami`.
2. The server rejects a token for another audience (403 or 401) and a token from another tenant.
3. Dataverse binding: a write through the server shows the person in Dataverse auditing, not the server.
4. A person added to an Entra group who has never opened the environment calls a tool; group team membership applies on that first call [T36].
5. An approver's Claude Code session tries every write that could approve: `submit_record` on its own decision, `save_decision_proposal` with an accepted status, a direct Web API update of an approval outcome. All are refused.
6. `run_gap_check` as a Power Platform administrator works; as an AI administrator returns only the agent half or fails (UNVERIFIED today); as a user with no admin role returns 403 from the inventory.
7. A gateway in front of the server (API Management or AI Gateway) passes the caller's token; a shared backend connection is refused by the server.
8. Agent 365 bring-your-own: a call through the Tooling Gateway reaches the server with the person's identity (UNVERIFIED today).
9. Route C: the product's custom APIs appear to Claude Code through `search` and `describe`, and `invoke_api` calls them with `payloadJson`.
10. Kill one instance mid-session; the next call succeeds on another instance (stateless).
11. Measure: the Copilot Credits of a "who owns X" plus "what breaks if X retires" session on route C, and the Power Platform requests of the same session on route A.
12. Remove a person from the group; record how long the person keeps access on each binding (doc 20, S7).
13. Azure binding host: on Functions Flex Consumption (.NET 10), the MCP endpoint, the protected resource metadata, built-in authentication and the on-behalf-of exchange all work from Claude Code. If any fails on Functions, repeat on Container Apps and record the move (28, issue 13).
14. `run_owner_check` as a platform admin reads account state and leave dates for owners through Graph. Record the least delegated permission and directory role that work (UNVERIFIED today).

---

## 14. Patterns used (from governance practice, generalized)

| Pattern | Idea ID | Where it lands here |
|---|---|---|
| AI tools reach data through one approved MCP server, as the person, gated by a group, writing only after the person accepts | I-71 | Rules 1 and 5; sections 4 and 12 |
| Every MCP tool is read-only by default and never approves | I-258 | Section 2.2 defaults; section 7 |
| Narrow doors: check the caller, return only what the feature needs, be idempotent | I-267 | Sections 3 and 9. This design keeps the caller's identity instead of running custom APIs under a system account |
| Read as the person, list what could not be read, treat source text as data | I-269 | `couldNotRead` and `untrustedFields` in the envelope |
| Upsert by key so retries never duplicate; fail soft so governance never breaks a deployment | I-189 | `record_deployment` and the record endpoint |
| Rate limits per person and a gateway only when needed | I-107 | Tool Policy Calls Per Person Per Hour; section 4.8 |
| Approved MCP servers on laptops | I-109 | Section 12 |
| "Performed by" kind on every change | I-183 | Section 4.5 |

---

## 15. New terms (not yet in doc 20)

| Term | Definition | Proposed home |
|---|---|---|
| Tool | One named operation on the product's MCP server, with a JSON input and the response envelope | (contract) |
| Toolset | A named group of tools a client can ask for with the `X-Toolsets` header | Proposed C41 column; `toolset` value list |
| Tool Policy | A configuration row per tool: enabled, allowed roles, owner role needed, Max AI State, AI May Submit, rate limit, release, description override (section 2.3) | Proposed C41 Tool Policy (CONFIGURATION, taxonomy columns apply) |
| Max AI State | The highest state an AI client may leave a record in through a tool. Values: read only, draft, proposed, submitted, as reported, link, candidate, withdrawn. Proposed as a new engine contract list, because the engine enforces it | Proposed engine contract row in doc 20, section 2.4 |
| AI write state | The state a tool's write leaves a record in when the caller is an AI client (section 6.0) | (contract) |
| Request act | Starting something others decide on, such as submitting an intake or requesting an architecture review. Not a decision. Contrast with a decision act, which only people do on cards | (process) |
| Candidate (configuration draft) | A C46 Configuration Draft row created by `propose_configuration_change`: a patch on the active version, never in force. The C02 Configuration Set Version row is created only at activation, after its configuration change decision is accepted (28, issue 3) | Configuration Draft (document 21); no ID reserved yet |
| Record endpoint | The plain HTTPS path (`/api/deployments`) that pipeline record steps call with an app-only token, using the `record_deployment` contract. Corrections use `/api/deployments/corrections` | (contract) |
| `Approval.Write` app role | The app role the approval runner's identity holds on the Azure binding, recorded as a Workload Grant (C42). Proposed name | (contract) |
| `configuration_unavailable` error | The engine cannot load a valid active configuration or a rule row it needs; governed writes are refused (28, issue 14) | (contract) |
| `review request` subject kind | The AI May Submit value for `request_architecture_review` | Proposed C41 value |
| Response envelope | The common outer shape of every tool result (section 3.4) | (contract) |
| `client-app` value list | Maps a token's client app ID to a label, such as "Claude Code", "GitHub Copilot" or "the product's page". Fills the Client App column | C06 and C07 rows |
| `dismiss-reason` value list | Reasons for dismissing a claim, such as "not a business application" or "personal test" | C06 and C07 rows |
| `toolset` value list | The ten toolset keys of section 2.1 | C06 and C07 rows |
| `impact-max-depth` setting | The deepest walk `get_dependency_impact` allows. Default 5 | C04 row, listed in document 21's one settings table (28, issue 15) |
| `mcp-page-size-max` setting | The largest page any list tool returns. Default 100 | C04 row, listed in document 21's one settings table (28, issue 15) |
| Inventory list as reported | A gap check run whose list came from the admin's own tool through `compare_inventory_list`, marked evidence level "as reported" | O22, uses the evidence level list |
| Route A, B, C | The product's own server; Microsoft's MCP management server; the Dataverse MCP server's `invoke_api` (doc 20 defines "MCP route") | (design) |

---

## 16. Sources

"Checked 2026-11-22" means read on Microsoft Learn (or the vendor's own documentation for [V] rows) through the Microsoft Learn MCP tools or WebFetch for this document on 2026-11-22. Rows T27 to T41 and V5 come from the earlier research behind this design: they were read on the date shown and were not re-checked for this document. The doc 20 row points to that document's own sources.

| ID | Fact used | URL | Checked |
|---|---|---|---|
| T1 | CIMD is public preview, "isn't recommended for production workloads". Claude Code is listed "Supported" with `https://claude.ai/oauth/claude-code-client-metadata`. Custom enterprise-built MCP servers are supported; the Dataverse MCP server and the Azure DevOps MCP server are "Not supported yet". Visual Studio Code and GitHub Copilot do not use CIMD. A CIMD client becomes an agent identity per tenant. Default consent lets users grant only scopes "such as `openid` and `User.Read`"; administrators can add CIMD clients in advance. No client credentials flow, no confidential client, no publisher verification, no tenant domain allowlist. "Implementers should always prefer pre-registration of a client with Entra when possible" | https://learn.microsoft.com/entra/agent-id/client-id-metadata-documents | 2026-11-22 |
| T2 | Register the server as an API; the Application ID URI is the canonical endpoint URL; expose delegated scopes; "Define application roles only if the server supports app-only access". Publish protected resource metadata at `/.well-known/oauth-protected-resource`; for CIMD advertise `https://login.microsoftonline.com/<tenant>/oauth21/2026-08`. Validate signature, issuer, audience, lifetime, tenant, client and permissions; `aud` is the client ID GUID. 401 for a missing token, 403 for a missing scope. Client identity models: a registered Agent ID, or a CIMD URL | https://learn.microsoft.com/entra/agent-id/secure-mcp-server-with-entra-id | 2026-11-22 |
| T3 | "Pass-through scenarios where the server forwards its token create security vulnerabilities ... obtain a new token through the on-behalf-of flow" | https://learn.microsoft.com/azure/app-service/configure-authentication-mcp | 2026-11-22 |
| T4 | Microsoft's on-behalf-of MCP template uses "a federated identity credential backed by a managed identity as its own credential during the OBO token exchange"; the OBO flow "doesn't grant a user access that they don't already have" | https://learn.microsoft.com/azure/developer/azure-mcp-server/how-to/deploy-remote-mcp-server-on-behalf-of | 2026-11-22 |
| T5 | The on-behalf-of flow "only uses delegated scopes and not application roles" | https://learn.microsoft.com/entra/identity-platform/v2-oauth2-on-behalf-of-flow | 2026-11-22 |
| T6 | Hosting options compared: Functions for independent, stateless tools with per-invocation pricing, Premium for interactive clients; Container Apps with a minimum of one replica, "the most flexible default"; App Service built-in MCP (preview) from OpenAPI | https://learn.microsoft.com/azure/container-apps/mcp-choosing-azure-service | 2026-11-22 |
| T7 | Flex Consumption supports always ready instances; Premium keeps "perpetually warm" instances and "always has at least one active (billed) instance" | https://learn.microsoft.com/azure/azure-functions/functions-scale ; https://learn.microsoft.com/azure/azure-functions/functions-premium-plan | 2026-11-22 |
| T8 | Dataverse MCP server URL `https://{org}.crm.dynamics.com/api/mcp`; 16 tools including `invoke_api` ("Invoke Dataverse Custom API"); `search_data` uses usage-based billing; all other tools use the basic rate per 10 responses | https://learn.microsoft.com/power-apps/maker/data-platform/data-platform-mcp | 2026-11-22 |
| T9 | Non-Microsoft clients: local proxy `@microsoft/dataverse` after one-time tenant admin consent and enabling the "Dataverse CLI" client; or the remote endpoint with your own Entra app holding Dynamics CRM `mcp.tools`, added as an allowed client | https://learn.microsoft.com/power-apps/maker/data-platform/data-platform-mcp-other-clients | 2026-11-22 |
| T10 | MCP management server: preview; tool names may change; endpoint per environment; scope `McpServers.Management.All`; `CreateToolWithCustomAPI`, `PublishMCPServer` to tenant scope, `UpdateTool` with `inputSchema` and `annotations` | https://learn.microsoft.com/microsoft-copilot-studio/mcp-management | 2026-11-22 |
| T11 | `a365 develop-mcp publish`: "Publishing an MCP server doesn't approve it"; creates `<server-name>-PublicClients` for client hosts such as Visual Studio Code and the Microsoft Copilot CLI; needs `Application.ReadWrite.All` | https://learn.microsoft.com/microsoft-agent-365/developer/reference/cli/develop-mcp | 2026-11-22 |
| T12 | Approving MCP server requests needs the Tools page and tenant-wide consent: AI Administrator or Global Administrator. The Agent 365 server list labels "Microsoft Dataverse (preview)" | https://learn.microsoft.com/microsoft-agent-365/admin/manage-plugins-skills-mcp-servers ; https://learn.microsoft.com/microsoft-agent-365/tooling-servers-overview | 2026-11-22 |
| T13 | Bring-your-own MCP server: preview; supported clients Copilot Studio, Visual Studio Code, Claude Code, GitHub Copilot CLI; publicly accessible endpoint; `EntraOAuth` among the auth types; no republish during preview; Defender advanced hunting `ExecuteToolByGateway`; blocking applies only through the gateway | https://learn.microsoft.com/microsoft-agent-365/admin/manage-byo-mcp-server | 2026-11-22 |
| T14 | Multiplexing: definition, and "Using multiplexing as a mechanism to reduce the number of licenses to purchase is a license violation". "Users who respond to approval requests don't need a Premium license" | https://learn.microsoft.com/power-platform/admin/power-automate-licensing/faqs#multiplexing | 2026-11-22 |
| T15 | AI Gateway: "Backend authentication is separate from caller access. With a shared backend connection, authorized callers use the permissions of that connection" | https://learn.microsoft.com/azure/ai-gateway/concepts | 2026-11-22 |
| T16 | Global Secure Access MCP firewall: preview; allow or block servers by URL and tools per server; needs an Entra Internet Access license, TLS inspection and the client on joined devices; remote streamable HTTP only, stdio not inspected | https://learn.microsoft.com/entra/global-secure-access/how-to-configure-mcp-firewall | 2026-11-22 |
| T17 | Defender AI agent runtime protection: preview; Audit and Block; Claude Code covered through hooks; `AiAgentProtection` 1 block, 2 audit; Block mode known issue for Claude Code | https://learn.microsoft.com/defender-endpoint/ai-agent-runtime-protection-overview ; https://learn.microsoft.com/windows/client-management/mdm/defender-csp | 2026-11-22 |
| T18 | Inventory API: "currently supports only delegated user authentication"; service principal or managed identity calls return 403; endpoint `POST {PowerPlatformAPI url}/resourcequery/resources/query`; `pac resource-query` is preview | https://learn.microsoft.com/power-platform/admin/inventory-api | 2026-11-22 |
| T19 | Row-level security for middle-tier applications: the application sets the user ID in `SESSION_CONTEXT`, and security policies filter and block rows | https://learn.microsoft.com/sql/relational-databases/security/row-level-security | 2026-11-22 |
| T20 | Data API builder 2.0 supports on-behalf-of user-delegated authentication to SQL Server "so the database authenticates as the actual calling user" | https://learn.microsoft.com/azure/data-api-builder/concept/security/row-level-security | 2026-11-22 |
| T21 | Dataverse impersonation needs `prvActOnBehalfOfAnotherUser`; "Users can't inherit this privilege through a Team"; the `CallerObjectId` header | https://learn.microsoft.com/power-apps/developer/data-platform/impersonate-another-user | 2026-11-22 |
| T22 | Dataverse delegated permission `user_impersonation` | https://learn.microsoft.com/power-apps/developer/data-platform/walkthrough-blazor-webassembly-single-tenant | 2026-11-22 |
| T23 | Custom API parameter types: Boolean, DateTime, Decimal, Entity, EntityCollection, EntityReference, Float, Integer, Money, Picklist, String, StringArray, Guid | https://learn.microsoft.com/power-apps/developer/data-platform/custom-api-tables | 2026-11-22 |
| T24 | "Avoid the confused-deputy pattern ... separate the server's execution identity from the caller's authorization" | https://learn.microsoft.com/azure/developer/azure-mcp-server/security | 2026-11-22 |
| T25 | API Management forwards request headers to MCP tool invocations and can forward the `Authorization` header | https://learn.microsoft.com/azure/api-management/secure-mcp-servers | 2026-11-22 |
| T26 | App Service built-in MCP: "Built-in MCP doesn't enforce authorization on the underlying HTTP routes"; every operation in the tool list is callable once connected | https://learn.microsoft.com/azure/app-service/configure-mcp-built-in | 2026-11-22 |
| T27 | The Dataverse MCP server discovers published custom APIs automatically through `search`, `describe` and `invoke_api`; Custom Actions are not supported (Power Platform blog, 2026-10-26) | https://www.microsoft.com/en-us/power-platform/blog/2026/10/26/custom-apis-just-landed-in-the-dataverse-mcp-server-agents-just-got-smarter/ | 2026-11-21 |
| T28 | The remote Azure DevOps MCP server selects toolsets with the `X-MCP-Toolsets` header, for example `wit,wiki` | https://learn.microsoft.com/azure/devops/mcp-server/remote-mcp-server | 2026-11-21 |
| T29 | "The Azure DevOps MCP Server is free to use"; normal Azure DevOps pricing applies | https://learn.microsoft.com/azure/devops/mcp-server/mcp-server-overview | 2026-11-21 |
| T30 | Copilot Studio connects an agent to an existing MCP server through its onboarding wizard, with OAuth 2.0 for the connection | https://learn.microsoft.com/microsoft-copilot-studio/mcp-add-existing-server-to-agent | 2026-11-21 |
| T31 | The Dataverse Web API SQL interface supports a subset of `SELECT`; subqueries and CTEs, and so recursion, are not supported | https://learn.microsoft.com/power-apps/developer/data-platform/webapi/query/sql | 2026-11-21 |
| T32 | Dataverse MCP server: calls run as the signed-in user, and Dataverse security roles and row-level security apply. Dataverse MCP tools are "charged when accessed by AI agents created outside of Microsoft Copilot Studio" | https://learn.microsoft.com/power-apps/maker/data-platform/data-platform-mcp-faq | 2026-11-21 |
| T33 | Copilot Studio billing rates: the "text and generative AI tools (basic)" rate is 1 Copilot Credit per 10 responses | https://learn.microsoft.com/microsoft-copilot-studio/requirements-messages-management | 2026-11-21 |
| T34 | The Dataverse MCP server is turned on per environment, with a list of allowed clients; "MCP allow listing applies only to the `/api/mcp` agent entrypoint" | https://learn.microsoft.com/power-apps/maker/data-platform/data-platform-mcp-disable | 2026-11-21 |
| T35 | Custom APIs: developers outside Microsoft cannot create new privileges; `ExecutePrivilegeName` uses an existing one | https://learn.microsoft.com/power-apps/developer/data-platform/custom-api | 2026-11-21 |
| T36 | Entra ID group teams: membership is applied just in time when a group member accesses the environment | https://learn.microsoft.com/power-apps/developer/data-platform/aad-group-team | 2026-11-21 |
| T37 | Microsoft 365 roadmap item 576752, the Microsoft MCP management server: GA "December CY2026" | https://www.microsoft.com/releasecommunications/api/v1/m365/576752 | 2026-11-21 |
| T38 | Copilot Managed Runtime default governance: the Microsoft-managed default environment group blocks the Dataverse action that invokes custom APIs | https://learn.microsoft.com/microsoft-365/admin/manage/apps/governance | 2026-11-22 |
| T39 | Users of an app or flow over tables with code plug-ins need a Power Apps or Power Automate license | https://learn.microsoft.com/power-apps/maker/data-platform/data-platform-entity-licenses | 2026-11-22 |
| T40 | Azure Functions MCP bindings: by default the MCP endpoint needs the system key `mcp_extension`; built-in MCP server authorization can take its place | https://learn.microsoft.com/azure/azure-functions/functions-bindings-mcp | 2026-11-21 |
| T41 | Azure API Center registers and lists remote MCP servers | https://learn.microsoft.com/azure/api-center/register-discover-mcp-server | 2026-11-21 |
| V1 | Claude Code managed MCP: `managed-mcp.json`, `managedMcpServers` (v2.1.259 or later), `allowedMcpServers` and `deniedMcpServers` with `serverUrl`, `serverCommand`, `serverName`; `allowManagedMcpServersOnly`; `strictPluginOnlyCustomization`; "A `serverName` entry ... is not a security control" | https://code.claude.com/docs/en/managed-mcp.md | 2026-11-22 |
| V2 | Claude Code remote OAuth: `--client-id`, `--callback-port`; discovery through RFC 9728 then RFC 8414; servers using CIMD are discovered automatically | https://code.claude.com/docs/en/mcp | 2026-11-22 |
| V3 | GitHub Copilot MCP allowlist policy with an MCP registry URL (Azure API Center supported) and "Registry only": public preview; the `managed-settings.json` allowlist is "the more secure, generally available method" | https://docs.github.com/en/copilot/how-tos/administer-copilot/manage-mcp-usage/restrict-based-on-registry | 2026-11-22 |
| V4 | GitHub `managed-settings.json`: `allowedMcpServers` and `deniedMcpServers`, `serverUrl` with wildcards, `serverCommand`; built-in servers always allowed | https://docs.github.com/en/copilot/how-tos/administer-copilot/manage-mcp-usage/configure-enterprise-allowlist | 2026-11-22 |
| V5 | GitHub changelog: the GitHub MCP Server supports the MCP specification of 2026-07-28, which is stateless (sessions and `initialize` removed) | https://github.blog/changelog/2026-07-23-github-mcp-server-supports-the-next-mcp-specification/ | 2026-11-21 |
| doc 20 | S6 inventory roles, S7 group team cache, S9 group-based app roles need P1 or P2, S12 custom API names, S13 generative page limits | `docs/design/step2/20-glossary-and-entities.md`, section 4 | 2026-11-22 (doc 20) |

## 17. UNVERIFIED in this document

1. Whose identity the MCP management server runs custom API tools under, whether Claude Code can call a server it publishes, its CIMD support, licensing and solution awareness.
2. Whether the Agent 365 Tooling Gateway passes the person's identity to a bring-your-own server registered with `EntraOAuth`.
3. Whether a Dataverse plug-in can verify which application called it, so the actor kind of a route C call is trustworthy.
4. Whether Web API calls the product's server makes to Dataverse carry any AI charge (D-16).
5. The exact license for a person who reaches Dataverse only through MCP.
6. Whether AI administrator and AI reader can use the inventory API.
7. Whether the `wids` claim reaches the server's token for the gap check's Admin Role Used.
8. The least Microsoft Graph delegated permission for reading group members and role assignments.
9. Whether a never-before-seen member of an Entra group gets Dataverse group team membership on a first on-behalf-of call.
10. That the MCP specification dated 2026-07-28 is stateless (reported by GitHub [V5]; the specification itself was not read here).
11. Whether an approval flow triggered by the API on the Azure binding needs a Process license. The working default carries one Process license on the runner flow group either way (28, Q19).
12. Whether any Microsoft tool can answer a Power Automate approval card on a person's behalf.
13. Whether a Dataverse application user (service principal) can receive a role through an Entra group team. The Azure half is answered: Learn says that if a service principal is added to a group and an app role is assigned to that group, "Microsoft Entra ID doesn't add the `roles` claim" (https://learn.microsoft.com/entra/identity-platform/howto-add-app-roles-in-apps, checked 2026-11-22; document 22, T20). The working default does not depend on it: workload identities hold their role through a Workload Grant (working default; open design question 12, see 28-working-defaults.md).
14. The least Microsoft Graph delegated permission and directory role for reading an owner's account state in `run_owner_check` (spike test 14).

---

## Diagram specs

All diagrams use the D-04 palette: navy `0A2A4A` for text and lines, blue `0071DC` for group header bars, gold `FFC221` as the accent. Fills are light only: white `FFFFFF`, light blue `E8F1FB`, light gold `FFF4D6`, light gray `F3F5F8`. No dark fills. Drawn in Visio from these specs.

### D24-1 Identity, end to end

- **Title:** One person, one identity, from Claude Code to the record.
- **Purpose:** Shows the on-behalf-of path, where the token is validated, where a new token is issued, and where the actor kind is stamped (sections 4.1 to 4.5).
- **Lanes (top to bottom):** Person; Claude Code; Microsoft Entra ID; The product's MCP server; Engine; Data store (Dataverse or Azure SQL, one box with two labels); Microsoft APIs (Power Platform inventory, Microsoft Graph); Approval runner (approval writer).
- **Nodes:**
  1. Person: "Asks in plain words".
  2. Claude Code: "Reads protected resource metadata".
  3. Entra: "Sign-in (CIMD or registered client)".
  4. Entra: "Token for the server (scope mcp.access)".
  5. Server: "Validates issuer, audience, scope, tenant".
  6. Server: "Builds the tool list from roles and Tool Policy".
  7. Entra: "On-behalf-of token for the store or API".
  8. Engine: "Checks role, owner role, rules".
  9. Engine: "Stamps AI client for a person, on behalf of, client app".
  10. Data store: "Row written as Draft or Proposed".
  11. Microsoft APIs: "Inventory and owner check queries as the admin".
  12. Approval runner: "Writes approval outcomes as the approval writer (persons only count)".
- **Edges:** 1 to 2 "prompt"; 2 to 3 "discovery (registered client first)"; 3 to 4 "consent"; 4 to 5 "bearer token"; 5 to 6 "valid"; 6 to 7 "token exchange"; 7 to 8 "as the person"; 8 to 9 "allowed"; 9 to 10 "write"; 7 to 11 "gap check and owner check only"; 12 to 10 "outcome". A dotted gold edge from 5 to 10 labeled "never forwards the incoming token". A dotted gold edge from 2 to 12 labeled "AI cannot answer a card".

### D24-2 Three routes to the same engine

- **Title:** Three routes, one engine, one set of rules.
- **Purpose:** Shows that routes B and C exist only on Dataverse, and that every route ends at the same custom APIs (section 8).
- **Groups:**
  1. Clients (white): Claude Code, GitHub Copilot, Copilot Studio (later).
  2. Route A (light blue): "The product's MCP server (adopter's Azure, Functions Flex Consumption)".
  3. Route B (light gray): "Microsoft MCP management server (preview)".
  4. Route C (light gray): "Dataverse MCP server, invoke_api".
  5. Dataverse binding (light gold, a bracket around B, C and the engine box): "Custom APIs and plug-ins".
  6. Azure binding (light blue): "Engine library and Azure SQL".
- **Nodes:** each client; each route; "Custom APIs and plug-ins"; "Engine library"; "Azure SQL"; "Dataverse tables".
- **Edges:** clients to A "OAuth, CIMD or registered client"; clients to B "UNVERIFIED for Claude Code"; clients to C "local proxy or own Entra app"; A to "Custom APIs" "on behalf of (Dataverse)"; A to "Engine library" "in process (Azure)"; B to "Custom APIs" "CreateToolWithCustomAPI"; C to "Custom APIs" "invoke_api"; "Custom APIs" to "Dataverse tables" "rules on every write"; "Engine library" to "Azure SQL" "only write path".
- **Accent:** a gold label on the Dataverse bracket: "Routes B and C exist only here". A gold note on A: "Only route with the live gap check and Entra reads".

### D24-3 Tool map by toolset

- **Title:** 51 tools, 10 toolsets, and what each one writes.
- **Purpose:** One page an agent or an adopter can scan: every tool, its toolset, read or write, and the entities it writes (section 6.1).
- **Groups:** one box per toolset with a blue header bar, in the order of section 2.1.
- **Nodes:** each tool as a small rounded box. Read tools in white, write tools in light blue, the Later tool (`check_change_approval`) with a dashed border.
- **Edges:** from each write tool to the entity boxes it writes (O02, O03, O05, O06, O07, O08, O09, O10, O11, O12, O13, O14, O16, O17, O18, O19, O20, O21, O22, O23, O24, Configuration Draft), drawn in a column on the right, labeled with the AI write state ("Draft", "Proposed", "Submitted", "as reported", "link", "candidate").
- **Accent:** a gold badge "person only" on O10, meaning no tool can set its outcome. A gold note on C02: "created only at activation, never by a tool".

### D24-4 Where adopters allow the server

- **Title:** Allowing the product's MCP server, layer by layer.
- **Purpose:** Shows the eight places an adopter allows the server and who administers each (section 12).
- **Groups (left to right):** Device (light gray): Claude Code managed settings, Defender runtime protection. Network (light blue): Global Secure Access MCP firewall. Tenant (white): Entra consent and CIMD client, Agent 365 bring-your-own (optional). Organization (light blue): GitHub Copilot managed settings, Azure API Center registry (optional). Environment (light gray, marked "route C only"): Dataverse MCP allowed clients.
- **Nodes:** each control, with its status ("preview" or "GA") in small text, and the server at the far right: "https://<host>/mcp".
- **Edges:** each control to the server, labeled with the entry: "serverUrl allow", "Audit mode first", "Allow rule by URL and tool", "pre-add Claude Code CIMD URL", "register with EntraOAuth", "allowedMcpServers", "remote MCP entry", "Dataverse CLI client".
- **Accent:** a gold note on Agent 365: "blocks only through the gateway".

---

## Open design questions

Only questions that no decision record settles (see [docs/decisions/](../../decisions/)). Questions 1 and 3 stay open: they are open design questions 9 and 12 in `28-working-defaults.md`, section 3. Questions 2 and 4 are settled as working defaults. Four more open questions in 28, section 3, touch this file and are marked in the text: question 1 (fixed engine meanings, section 3.2), question 4 (the admin accepts the configuration diff on a card, tool 42), question 7 (AI-only code approvals from client tools, tool 27) and question 8 (engine acceptance only for a locked list, tool 18).

### Question 1: May an AI client submit a record on a person's behalf, or must submitting be a human act outside the AI client?

- **Status.** Open design question 9 (see `28-working-defaults.md`, section 3), merged with document 23's question 4. This file uses option A as the working default.
- **Background.** D-02 and D-10 say AI never approves. Submitting is not approving: it sends an intake to triage, a change to authorization or a decision to acceptance, and people then answer approval cards. An earlier design draft said "Draft a change request or a decision for a person to submit", which reads as a human act. With MCP first (D-16), and maybe no Power Apps app at all (D-22), the person would then need another place to press "submit".
- **Example.** An owner tells Claude Code: "Raise a normal change for the expense app, window Saturday 6 to 8, and send it for approval." Claude Code saves the draft. May it also call `submit_record`, or must the owner open the product's page and press Submit?
- **Options.**
  - **A. AI may submit, configurable per subject kind.** Tool Policy rows set AI May Submit per kind; the shipped default is yes for intake requests, change requests, decisions and configuration drafts. Registering an application, and marking a gate-relevant document current, need a confirmation card. Links and deployment records need nothing. The record shows "AI client for a person". *Pros:* one conversation from idea to approval cards; no extra screen; the approval stays with people; proof where it matters. *Cons:* a careless prompt could flood approvers; mitigated by rate limits and `validateOnly`.
  - **B. A person always submits outside the AI client.** In the product's page, or on a confirmation card. *Pros:* a human act at every step. *Cons:* one more click for every record; on Dataverse the engine can only tell a page call from a server call if a plug-in can verify the calling app (UNVERIFIED), so B may need a confirmation card per submission.
  - **C. AI may submit only drafts it did not create.** *Pros:* splits drafting and sending. *Cons:* hard to explain, easy to work around.
- **Recommendation.** A (the working default). Submitting is a request; the decision stays on the cards, which only people answer.
- **What the answer changes.** With A, the Tool Policy defaults in section 2.3, the confirmation card rule and the "Submitted" state stay. With B, `submit_record` and `request_architecture_review` leave MCP, the Max AI State list loses "submitted", and the pages or a confirmation card flow carry submission. With C, the engine tracks who created each draft and checks it at submit.

### Question 2: Which Claude Code client path does the setup guide lead with?

Settled as a working default in 28-working-defaults.md (Q14); a later decision can still change it.

### Question 3: How does a pipeline's workload identity get the deployment recorder role?

- **Status.** Open design question 12 (see `28-working-defaults.md`, section 3), merged with documents 22 and 26. This file uses option B as the working default.
- **Background.** D-11 says roles come only through Entra groups, never directly. Pipeline record steps (D-19) call the record endpoint with a workload identity, not a person. Doc 20 (C29, Is Workload Role) left this as a step 2 design point. A service principal can be a member of an Entra security group, but on the Azure binding it does not inherit app roles assigned to that group: "if you add a service principal to a group, and then assign an app role to that group, Microsoft Entra ID doesn't add the `roles` claim" (document 22, T20; re-checked 2026-11-22). How a Dataverse application user gets a role through a group team is UNVERIFIED.
- **Example.** The client's Azure DevOps pipeline uses a federated workload identity. After it deploys the expense app to production, its record step calls `/api/deployments`. Something must say this identity may record deployments.
- **Options.**
  - **A. Strict D-11: the workload identity joins the deployment recorder group.** *Pros:* one rule for everyone; Entra access reviews cover pipelines too. *Cons:* does not work on the Azure binding today (T20 in document 22), and is UNVERIFIED on Dataverse.
  - **B. A documented exception for non-human workloads only: the role assigned directly to each workload identity, recorded as a Workload Grant (C42) with a reason, the deciding decision and an expiry, and reported by `check_role_bindings`.** People still get roles only through groups; agent identities and agent users never get a grant. *Pros:* works today on both platforms; each pipeline is visible and revocable alone; Microsoft's own access control guidance allows a few direct assignments as documented exceptions (doc 20, S8). *Cons:* a stated exception to D-11 for non-people.
  - **C. A per-pipeline token issued by the product.** *Pros:* no Entra change. *Cons:* a shared secret, which the design refuses elsewhere.
- **Recommendation.** B (the working default), recorded as a Workload Grant (C42, document 26, section 3.4) and reported by `check_role_bindings`. The same kind of grant covers the approval writer role of the approval runner (28, issue 7). The earlier recommendation (A) assumed group inheritance might work; Learn says it does not on the Azure binding.
- **What the answer changes.** The Workload Grant entity (C42) instead of a C30 binding kind, the `check_role_bindings` findings, the pipeline step samples' setup instructions, and one line in D-11's consequences.

### Question 4: On the Azure binding, how does the engine reach the database?

Settled as a working default in 28-working-defaults.md (Q20); a later decision can still change it.

---

## Proposed by the build (2026-11-22)

- **Source.** The build in `src/`: its MCP contracts (`src/mcp/contracts/`) and its MCP server core.
- **Status.** Every item below is a proposal. The sections above keep their meaning: no settled decision and no working default changes, and no tool gains an approve, accept, activate, assign or delete act. Where an item rests on one of the six open design questions, it names the question. Each item is accepted or dropped in design review.

### B24-1 Errors (section 3.5)

- **An unexpected failure has no fixed code.** The build answers it with a JSON-RPC internal error that names only the correlation ID, outside the envelope, with no message text from the failure. Proposal: write that rule into section 3.5, next to the fixed codes (twelve since B24-9).
- **Mapping engine reasons to the fixed codes.** A code that errors.json names for a reason wins, then the reason's catalog category, then the outcome word. A refusal by an AI rule (G-01, G-06, G-08, G-17, RA-06) for an AI client is `ai_cannot_do_this`. `validation` names the field, such as `answers[0].questionKey`. The server refuses any code outside the fixed list, and `rate_limited` always carries a retry time.

### B24-2 Gaps in tool contracts

- **Tool 6 `get_application_card`** has no portfolio, which the pages need. Proposal: `application.portfolio: { id, name }`.
- **Tool 7 `get_dependency_impact`** nodes carry no direction, so the dependency map makes two calls. OP-10 now returns each node's `direction` and the depth it applied. Proposal: add `direction` to each node and `maxDepthApplied` beside `truncatedAtDepth`.
- **Tool 8 `get_portfolio_summary`.** `draftsOlderThanDays` names no setting. The build counts drafts older than `draft-expiry-days` (30 in Balanced, document 21). Proposal: say so.
- **Owner fields.** `businessOwner` and `technicalOwner` assume the owner role keys `business-owner` and `technical-owner`, and are null when the configuration has no such role. That is a fixed key (open design question 1, see 28-working-defaults.md); the alternative is a list of owners by owner role key.
- **Tool 9 `get_requirements`** leaves out owner-assigned gate requirements, because the contract has no such kind, and warns. Proposal: add the kind owner assigned (document 21, C14).
- **Bounds the contracts set and this document does not:** `score` in tool 10 `check_duplicates` runs from 0 to 1, and every URL is capped at 2,048 characters. Outputs leave out components, links and pipelines without an https URL, because the contract requires one.
- **Tools 23 and 24** use other shapes than the OP-12 and OP-14 payloads (`model: { key, approvedByDecisionId }`, `failedConditions: [{ condition, why }]`, `linkedSource`); the server maps them.

### B24-3 How the server applies policy (sections 2.3, 3, 4, 5 and 11)

- **Tool Policy (proposed C41).** While no C41 row is in force, the shipped seed rows apply. Once any C41 row is in force, only those rows count, and a tool without a row is hidden. C41 rows need a `toolset` value list before they can ship. Max AI State only tightens: a value the tool's contract does not have cannot widen it, and read-only refuses an AI write. AI May Submit for a subject kind the row does not name is no (open design question 9, see 28-working-defaults.md).
- **Known Client (proposed C45).** With no active C45 row the check does not apply, and the client is labeled by its app ID; with any active row the list is an allow list. Document 26 says the reference client is seeded, but no preset seeds a C45 row yet.
- **`X-Toolsets`.** Keys match exactly, and an unknown key is refused. A tool outside the selection, an unknown tool, a Later tool and a disabled tool are all `not_found`; a missing role is `not_authorized`.
- **Order of checks on a tool call:** token, session, Known Client, tool served and selected, Tool Policy roles, Max AI State, rate limit, input schema, AI May Submit, backend, envelope, telemetry. The rate limit counts calls that pass the role check, also those that then fail the input schema; the window is a fixed UTC hour.
- **Versions.** Each tool call probes the version twice (the session, then the composed call); the envelope's configurationSetVersion is the call's.
- **`whoami` for an agent user** answers `not_authorized`: an agent identity holds no role (document 26, RA-06).
- **Telemetry** (section 11) keeps a method only when the server serves it (else `(other)`, or `(none)` when missing) and a tool name only when it is a contract tool or resource name (else `(unknown)`). The error sent back to the same client still names what it sent. The caller hash needs a salt the host sets; with none, no caller field is sent, and a salt under 16 characters is refused (document 26, B26-5).
- **Paging.** skipToken is the offset plus a hash of the query; a token from another query is `validation`.
- **Published schemas** are self-contained (references inlined, no comments), so no internal marker reaches an MCP client.

### B24-4 The record endpoint (sections 4.6, 9.1 and 9.2)

- **One body on both branches.** The build's record endpoint contract gives `<prefix>_RecordDeployment` the same request schema as `/api/deployments`. Document 25, section 11.4 names the custom API's parameters differently (`startedOnUtc`, `finishedOnUtc`, `resultKey`, `routeKey`) and leaves out `stageOrderRun` and `reportedApprovals`; section 9.1 here sends nested inputs as one `payloadJson` string. Proposal: the custom API takes the record endpoint body as `payloadJson`, so a step sends one body on both branches. Until the Dataverse binding settles it, the samples' Dataverse branch is UNVERIFIED.
- **No route from the caller.** The engine sets the route from the actor kind (document 25, B25-6), so the body carries no `routeKey`.
- **`validateOnly`.** The contract says a 200 can mean "validateOnly was true", but neither request schema allows `validateOnly`. Add it to both, or drop the words.
- **Corrections** (tool 48) carry Corrects Record and the reason in the insert; the new record is never updated afterwards (document 22, section 8.3).

### B24-5 Tool behavior as built

- **`submit_record` for a decision** reports the decision as proposed even when the engine accepts it under the locked list; `engineAccepted` carries the engine's act (open design question 8, see 28-working-defaults.md). No write tool output holds accepted.
- **Owner proposals.** `save_application_draft`, `propose_owner_change` and `claim_resource` record the proposal on a proposed ownership decision, because Owner Assignment rows are effect-only. `pendingAssignments` stays empty: the rows start only when the decision is accepted, by the update-subject effect (document 25, B25-8; B24-8).
- **`record_change_result`** writes only the Result, as reported, never the status. A post-change review opens for a failed, rolled-back or partly succeeded result, or for a change type with Requires Post Change Review.
- **Confirmation cards** (open design question 9, see 28-working-defaults.md) are not sent while no approval runner is connected: `confirmationCardSent` is false with a warning, and an AI client's "current" on a Current Needs Person document type is stored as draft.
- **Linked mode.** A linked change request submits with no card, and a warning says the ITSM tool authorizes it; `linkedSource` stays none until Evidence Link facts feed OP-14.
- **`propose_configuration_change`** takes operations, or a draft patch as file content; a whole preset is refused until upgrades and reverts are built. A locked setting is refused with `locked` before OP-09 runs. `recordsInFlight` counts proposed decisions and submitted change requests.
- **`check_role_bindings`** writes no Audit Entry (`auditEntryId` null): the check writes nothing.
- **`explain_rules`** runs under the version the record was stamped with; a version parameter only warns.
- **`run_gap_check`.** A registered component counts as in an estate when its component kind is gap-check covered, because O03 holds no estate key (document 22, B22-5). The run's position travels in skipToken, and the counts live on the O22 row.
- **`run_owner_check`** writes the estate `entra-owners`, which no shipped preset's `estate` list holds yet (an open design question; document 22, B22-4).
- **Who may propose (`save_decision_proposal`, built 2026-11-23).** After the Tool Policy roles, the engine checks the person behind the call, on the MCP endpoint and on the web app's person entry alike: the person must hold a responsible, accountable or consulted Decision Right (C31) for the decision type in the decision's scope, through a product role they hold now or an owner role they hold now on the subject's application (a deputy too, as for "own apps", document 26, section 5.3). The Decision Right groups resolve as OP-02 resolves the accountable letter: the single most specific matching group wins, and broader groups drop out. A refusal is `not_authorized` with the proposed reason `decision.propose-not-allowed`; it names who holds the letters and writes nothing. `validateOnly` is refused the same way, and an edit is checked again. **Judgment call:** the table above and the Tool Policy seed said R or C. The build adds A, because Balanced gives 9 of the 14 decision types this tool takes only an accountable row (intake, design, data use, tier, risk acceptance, acceptance, go-live, ownership and exception), and change authorization below tier 3 too, so with R or C alone nobody could propose them. The accountable holder answers for the outcome, so it may put the decision on the table; whether its own answer then counts is a separation rule's question (C40; D-40 keeps Balanced's self-approval as it is). Informed never proposes, and supports names no holder (AI only, V31). When Balanced gains the R rows of document 23's RACI tables, those holders may propose with no code change. Decisions other tools open keep those tools' own checks: the intake triage and change authorization decisions of `submit_record`, and the ownership decisions of `propose_owner_change`, `save_application_draft` and `claim_resource`. Accepted or dropped in design review.
- **Not built yet:** a read that lists the decision types the caller may propose for a subject (the web app's "Propose a decision" list should come from the same check), row-level portfolio read, the own-apps check (RA-02) for the record step, and per-portfolio change management mode rows.

### B24-6 A person answers an approval on the decision page (built 2026-11-23, D-38)

- **What D-38 changes here.** The decision page is the default place to answer, and the token proves who answered. The build adds tool 52, `answer_approval`, in the decisions toolset. Rule 2 above, section 6.13 and section 7, layers 1 and 3, now say so.
- **Served to a person only.** `tools.json` gains `entries` (absent means both entries) and the AI write state `person-only`; `tools.schema.json` requires a person-only tool to name the person entry alone. The server core never lists such a tool on the AI client entry and answers a call to it with `ai_cannot_do_this`; on the REST API an AI client gets the same answer, and an app-only token is refused before any tool runs. The Tool Policy seed row gives all 14 human roles (any role may be asked) with Max AI State read-only, and the new relation `asked-approver` says the engine checks the rest.
- **The engine's checks.** A new pure operation, the approval answer check (`ApprovalAnswerOperation`, fixtures under `src/fixtures/approval-answer`), refuses before anything is written: `approval.answer-person-only` (not a person signed in as a user; `ai_cannot_do_this` for an AI client, else `not_authorized`), `approval.answer-not-asked` (`not_authorized`), `approval.answer-separation` (`rule_violation`), `approval.answer-already-given`, `approval.answer-expired` and `approval.answer-closed` (`conflict`), and `approval.answer-reason-required` (`validation`, field `reason`). The separation rules are judged by OP-03's own code, so the check refuses exactly the answers OP-03 would not count; an "allow and flag" rule lets the answer through with `approval.separation-flagged`. D-40 keeps Balanced's self-approval as it is, so a proposer may answer their own request below tier 3.
- **The write.** The runtime then writes the answer as the person's own update, carrying the check's basis; OP-08's G-06 gains a person path that accepts it only from that same person, acting directly as a user, on a pending record, with approved or rejected, naming the caller as approver, as a person and a user, and no run link. The guard writes Responded On and Approver Holds Required Role itself. The recompute that follows is the approval runner's: OP-03 counts, the decision is accepted or rejected, pending requests are cancelled, and the next step or a restart opens. `performedBy` in the answer is the person's write; the row's last stamp is the engine's recompute.
- **Step-up sign-in** for tier 3 and configuration changes is a setting, off in the demo (D-38). The design names no key yet; document 26, B26-7 proposes one. It is not built.
- **Queue.** `get_my_queue` lists a waiting approval by its decision; on the web app's entry its next tool is `answer_approval`, and for an AI client it stays null with a note that only the person answers. `get_approval_status` gives the record IDs.
- **Not built yet:** step-up sign-in; a read field that says which pending record the signed-in person may answer (today the page can ask `answer_approval` with `validateOnly`); the web app's decision page itself; and answering inside a Teams or Outlook card as a per-install option. The diagram D24-3 and the toolsets image still show 51 tools.

### B24-7 Reads for the web app's screens (built 2026-11-24)

- **Why.** D-36 makes the web app the place people do their work. The screen design (a working file, not published) lists reads no tool served: one decision, approval requests across decisions, intake requests, reviews across the portfolio, portfolios, and owner assignments across the portfolio. The build adds tools 53 to 59 in their toolsets, all read only, for any role (Tool Policy seed rows with all 14 human roles and Max AI State read-only). They serve the AI client entry and the person entry alike.
- **Honest reads.** Rows a read visibility setting hides are counted in `couldNotRead` by kind and never shown (`list_intake_requests` and `list_approvals` under `intake-visibility`); a single record the caller may not read answers `not_found` (`get_intake_request`, and `get_decision` for an intake decision). Lists page with `skipToken`, which encodes the query (B24-3). Row-level portfolio read (`record-access-mode`) is still not built, so the lists read the whole register as `get_decision_log` does.
- **AI marks.** Tools 2, 4, 6, 20 and 24 and the new reads carry `isAiCreated` and `performedBy` as stamped (section 3.4), with the new common shape `stampedBy`. The decision reads add `proposedBy` (Created By Principal) and `get_change_request` adds `createdBy`. A read never fills a missing stamp from the reader's session, so a seeded row says null.
- **`get_my_queue`** (D-36). Reviews are role scoped through the proposal right check (section 6.2), documents include missing and date-stale ones from OP-06 and drafts waiting for confirmation, items carry the AI marks, and a decision the person proposed but did not submit is a draft item. The runtime gains a read-only form of the proposal right check (`ComposedCall.ProposalRight`) for a scope the caller names.
- **Judgment calls,** each accepted or dropped in design review: (1) "whose review it is" comes from the decision rights of the decision the review records, because C24 has no reviewer role; Balanced gives no product role a letter on risk acceptance, so security reviews reach the holders of the security owner role (C24 Default Owner Role) and not the security reviewer role. (2) `get_decision` evaluates the rules only for a proposed decision; a settled one shows the stored records, so a settled seeded decision opens even when OP-02 could not resolve its scope today. A proposed decision whose scope OP-02 cannot resolve fails as `get_approval_status` does (the screen check found such seeded tier 3 proposals). (3) The demand view is submitted, in progress and completed requests by score; nothing writes Score or Size Band yet. (4) `list_approvals` view open is served to every role, as `get_approval_status` is; the screen design shows it to the platform admin and the auditor.
- **Not built yet:** an age for drafts (the row's Created On is not in the stored columns, so only the last engine write's moment, `stampedOn`, is known); the data owner and change manager role sections of My work; an audit trail read; a portfolio-wide documentation list; a read of the decision types the caller may propose (B24-5); `list_deployment_targets`, `list_gap_check_runs`, `get_evidence` and global search. The diagram D24-3 and the toolsets image still show 51 tools.

### B24-8 The update-subject effect in the tools (built 2026-11-24, D-41)

- **Proposals carry their changes.** `propose_owner_change` and `save_application_draft` write the owner changes into the decision's Proposed Changes Json (document 22, B22-8), with outcome `assign` (or `end` when no holder starts). `save_decision_proposal` takes two optional inputs: `tierKey` for a tier decision (the tier it sets, when a person raises or lowers the computed one; leave it out to confirm) and `autonomyLevelKey` for an autonomy level decision. Each belongs to its own decision type; another type is a `validation` error. `claim_resource` still writes no owner change, so accepting its ownership decision changes no owner and says so (`subject.change-skipped`).
- **The decisions `submit_record` opens** name the outcome their statement says: `accept` for the intake triage and `authorize` for a change authorization, so their acceptance registers the application or schedules the change.
- **`submit_record` reports the caller's act.** When the engine accepts an intake or a standard change under the locked list in the same call and the effect moves the record on, the tool still reports the record as submitted, as it reports a decision as proposed, and `engineAccepted` carries the engine's act. The record's read shows the result.
- **Reads.** `get_change_schedule` adds `authorizationStatus`. The other reads already show the effect: `get_application_owners`, `get_application_card`, `find_applications`, `get_intake_request` and `get_change_request` read the rows the effect wrote.
- **Choice:** a change whose authorization the approvers rejected keeps the status submitted (a rejected decision changes nothing but itself; document 25, B25-8), and both change reads say rejected through the decision's state. A change authorized moves to in progress, which Balanced labels Scheduled. No authorized status is added.
- **Built since (B24-9):** `get_decision` returns the proposed changes, so the decision page shows what acceptance will write before a person answers.

### B24-9 Intake, PMO and decision fixes (built 2026-11-24, D-41)

The choices the author's AI sessions made under D-41 (decide the detail with the best recommendation, record it, build it):

- **A business owner on intake.** `save_intake_draft` takes `businessOwnerObjectId`, stored in O07 Business Owner (document 22, B22-9). The person comes from the store's Principal References, found by name with `find_people`; the label is never the caller's text, so a request never misnames a person. A person found disabled or deleted, or past a leave date, is refused: they could not own what the request starts. The engine counts the named person for the Accept gate's owner requirement and makes them the business owner on acceptance (document 25, B25-10), so a request for a new application can now pass triage. A directory search through Microsoft Graph, for a person the store has not met, is not built: the owner check and the seeded directory create references today.
- **The PMO's demand fields are a tool of their own** (`score_intake_request`, tool 60), not `save_intake_draft`: that tool is the requester's and works only while the request is a draft, and the PMO scores a request after it is submitted (document 23, P01 step 8). The PMO's write is stored as reported. **Priority is a decision, not a column:** the model has no priority column, and Balanced's priority decision type (fund, defer, stop) is accountable to the sponsor with the PMO responsible. The PMO proposes it with `save_decision_proposal`; the reads name the latest one as `priority`, and the demand list filters and orders by it.
- **Who answers a priority card for a request with no application** is not settled: Balanced's accountable letter is the sponsor owner role, which a new application does not have yet. A sponsor fallback is designed to settle it.
- **The approval answer pre-check.** `list_approvals` and `get_decision` run the engine's own approval answer check (the check `answer_approval` runs, with a typed reason assumed) for each pending record that asks the caller, read only. The page shows Approve and Reject only when it allows, and otherwise names the refusing rule. The check is the same code as the answer, so the page and the answer never disagree.
- **Step-up sign-in for an answer** (D-38: a setting, off in the demo) is a host setting, `Product:WebApp:StepUp` (document 26, B26-9): `Mode` Off, FreshSignIn (the token's `auth_time` within `MaxAgeMinutes`) or AuthenticationContext (the token's `acrs` names `AuthenticationContextId`). With it on, an answer without the fresh sign-in writes nothing and answers the twelfth error code, `step_up_required` (HTTP 401), with a claims challenge for the authentication context mode in `error.claimsChallenge` and the `WWW-Authenticate` header. The web app offers "Sign in again", which signs the person in with a login prompt (and the claims challenge), and the person answers again. A check (validateOnly) needs no fresh sign-in. It is a host setting, not a configuration row: it is about the sign-in, which the host sees and the engine does not.
- **Exception fields.** `save_decision_proposal` takes `exception: { standard, reviewOn }`, required for an exception decision and refused on any other type; the review date is today or later. They are stored in O09 Exception Standard and Exception Review On (document 22, B22-9), and OP-08 checks their shape too (document 25, B25-11). The link from an exception to the flagged deployment it covers is the decision's subject (subject kind deployment-record), which the tool already takes.
- **Record numbers.** New intake requests, decisions, change requests, reviews, applications and configuration drafts get IR-, DEC-, CHG-, REV-, APP- and CFG- numbers from the engine, after the highest number the store holds (document 25, B25-9). The reads show `recordNumber` as before.
- **People search** (`find_people`, tool 61) reads the store's Principal References, any role, with no leave date (HR data, platform admin only) and never an erased person.

### B24-10 Completion and record fixes (built 2026-11-24, D-41)

The choices the author's AI sessions made under D-41 (decide the detail with the best recommendation, record it, build it):

- **A person completes a review and closes a change** (`complete_review`, tool 62; `complete_change`, tool 64). Both are served only on the person entry, as `answer_approval` is: the reviewer attests the review, closing a change is a person's act, and an AI actor never sets done or completed (OP-08, G-08 as document 25, B25-12 widens it). An AI client drafts the outcome decision and records the result; a person completes.
- **A review is done only once its outcome decision is accepted** (document 22, O21: Done On, Reviewer and Outcome Decision are required when done). This settles document 23, B23-4. The engine checks it too, from a new OP-08 fact (document 25, B25-12). Next Due On comes from the cadence (C25) for the review type at the application's tier. The tool opens no next review: the S2 due-state refresh opens it on its date (not built yet), and a review opened months early would crowd My work.
- **The Design gate and the architecture review.** The architecture review records the design decision, which is the Design gate's gate decision, so accepting that decision passes the gate. Balanced keeps no review-done requirement for the architecture type on the Design gate: G-14 would then hold the design decision until the review is done, and the review is done only once that decision is accepted. `get_review` and `complete_review` name both paths in `gates`. A review-done requirement still works for a review whose outcome is not the gate decision, such as the tier 3 security review at go-live.
- **Whose review it is,** for `get_my_queue`, `list_reviews` (`isMine`), `get_review` and `complete_review`: the review type's lead owner role (C24 Default Owner Role; any owner when nobody holds it), the decision right on the decision it records, or an approval rule that asks the person's role for that decision or for the gate it feeds. This narrows the earlier "any owner role" (B24-7) and adds the approval path, so the tier 3 security review reaches the security reviewer. A new relation, `reviewer-of-review`, names it in `tools.json`.
- **Documents go to their owners** in `get_my_queue`: the Document Owner, and for a missing document the technical owner (document 23, P07: "by default the technical owner"), or any owner when the application has no technical owner.
- **The post-change review.** `record_change_result` read the flag `requiresPostChangeReview`, which no preset has, and always gave 14 days. It now reads C20 Requires Review After and Review Due Days, counted in working days (the schema says working days; the `working-days` setting, as OP-04 counts an emergency follow-up), with 14 calendar days for a type that sets none. The review opens once per change. O21 has no change column, so its Description names the change by record number, and `get_change_request` finds the change's post-change reviews by it; a change column on O21 is a later model proposal.
- **Change completion.** `complete_change` closes a change in progress (authorized) or a submitted emergency (Allows Authorization After, as document 22 says for Authorization Decision). An authorization accepted after the change closed leaves it closed; the update-subject effect skips it with `subject.change-skipped`. `record_change_result` no longer changes the result of a closed change.
- **Settled decisions read as settled.** `get_approval_status` no longer evaluates the rules for a decision that is not proposed; it returns the stored records and `settledBy`. A decision recorded as accepted outside the engine (the seeded demo estate's change authorizations, which hold no approval record) reads `settledBy: recorded`, and the pages say so instead of "still missing". The engine cannot give a seeded decision approval records after the fact, so labelling is the honest choice. `get_decision` and `get_change_request` carry `settledBy` too.
- **Claims.** Claims carry their link (O23 Url) from `compare_inventory_list`, and a claim copies it to the component. The live Power Platform inventory gives no link, and a built URL pattern is UNVERIFIED, so the live reader leaves it empty; `get_application_card` now lists a component with no link (`url` null) instead of hiding it. A claim for a new application starts an intake (P06 step 6) instead of only a draft application. A person's claim on an application they do not own submits its ownership decision; the decision proposes no owner change, because the claimant joins the item to the application, not its owners, so its acceptance writes nothing more (it has no outcome, so the update-subject effect does not run). A rejected confirmation leaves the component attached; its technical owner retires it with `link_component` (not automated yet).
- **The AI-only code approval flag** is stored on O14 (document 22, B22-10), so `get_deployments` and `flaggedOnly` show it.
- **Not built yet:** the S2 refresh that opens the next review on its date; retiring a component when its claim's confirmation is rejected; a link for items the live inventory finds; a Change Request column on O21; the confirmation card for a component a non-owner claims (P10), which the ownership decision stands in for.

### B24-11 Reads and writes for the remaining screens (built 2026-11-24, D-41)

The choices the author's AI sessions made under D-41 (decide the detail with the best recommendation, record it, build it):

- **Ten tools, 65 to 74,** serve the screens that still lacked them: `get_audit_trail` (decisions), `get_evidence` (decisions), `get_ai_activity` (portfolio), `get_tier_assessment` (intake), `list_documents` (documentation), `list_deployment_targets` and `save_deployment_target` (deployment), `list_gap_check_runs` (gap check), `save_dependency` (intake, beside the other register facts) and `save_portfolio` (portfolio). No new toolset: each fits one of the ten.
- **No `list_deployments`.** `get_deployments` already answered "what runs where" with paging and the flagged filter. It is now also the Deployments list: `flagKeys` (any of the named flags), `changeRequestId`, `resultMeanings`, the record's change, pipeline, versions, AI marks, version stamp and page link, and `counts` per flag. A second tool with the same rows would only grow `tools/list`, which every AI client reads.
- **The exceptions register and the portfolio filter.** `get_decision_log` items carry `subjectLabel` and `exception` (standard and review date), and the log takes `portfolioId`, so the Decision log screen and its exceptions view need no new tool.
- **No `list_agents`.** The Agents screen reads `find_applications` with the agent kind (its items carry the AI marks), `get_application_card` (the agreed autonomy level), `list_reviews` (security reviews), `get_decision_log` (autonomy decisions) and `get_ai_activity`. A Microsoft agent registry reader is a separate, later port.
- **AI marks on the reads.** `find_applications`, `get_application_card`, `get_decision_log` and `get_change_request` already carried `isAiCreated` and `performedBy` (B24-7). The card adds them to its open changes and deployments, with each deployment's route, flags and gate level, and the application's portfolio path.
- **The call history.** Every composed call that writes now leaves one audit entry, with or without a `clientRequestId` (document 25, B25-13), so the audit trail and the AI activity see the AI's own calls, not only the web app's keyed ones. That is document 26, section 9.4: on Azure, O24 is the full change log.
- **Refused calls are recorded** in a separate transaction (document 26, sections 9.2 and 9.4: "refused writes are written by the API to O24 in a separate transaction"), as operation `call-refused`, with the tool, the record the call named, the fixed error code, the rule key, the engine's reason code and the rules behind it. Which refusals: an AI client's or agent's refusal by a rule, a role or the AI rules (`ai_cannot_do_this`, `not_authorized`, `rule_violation`, `locked`) on any tool, and its call to a tool name that does not exist (an AI client trying to approve finds no approve tool); a person's refusal of a write by the same codes. Never a check (`validateOnly`), a malformed input, a missing record, a conflict, a rate limit or an upstream failure: those are not attempts against a rule. Only a caller who holds at least one product role is recorded: a person outside the product, an agent identity (which holds no role, RA-06) or a demo operator acting as nobody is turned away at the door, the request logs keep that, and the append-only ledger never names them (document 26, section 9.5). A refusal that cannot be recorded still answers the caller.
- **Who reads what** (document 26, B26-11). `get_audit_trail` follows document 26, sections 3.2 and 5.3: the auditor and the platform admin. `get_ai_activity` reads the call history and the refused calls for two derived facts only, never the Changed Values Json: the estate for the AI lead, the security reviewer, the platform admin and the auditor, and the AI work done for the caller for anyone else, so the maker in the demo story sees Claude Code's draft and its refused attempt. `list_gap_check_runs` follows `claim-visibility` as `list_claims` does, with the auditor added because the auditor reads everything. `list_deployment_targets` hides external IDs and URLs from a caller who holds only the reader role (document 26, section 6.2).
- **Person-only writes.** `save_deployment_target` and `save_portfolio` are served only on the person entry, like `answer_approval`: a target's stage decides the gate level and its change path which routes are flagged, and a portfolio's place in the tree scopes approval rules, decision rights and portfolio access. These rows have no draft state an AI client could leave for a person, so a person writes them. `save_dependency` is as reported, served to AI clients too: a dependency is a fact an AI build loop finds ("reads data from"), and the engine checks its shape.
- **The PMO on portfolios.** Document 26, section 5.3 gives the PMO "its own fields". The build reads that as the name, description and level of an existing portfolio; creating, moving, naming the accountable person and retiring stay with the platform admin.
- **Retiring a target is not built.** O15 has no retired column; a target that is gone keeps its records. A Retired On column (class M) is a later model proposal.
- **Not built yet:** the screens themselves; a guideline chip on a refusal (no configuration row ties a guard rule to a guideline number yet); component and data-place nodes in `get_dependency_impact`; a Microsoft agent registry reader; global search.

### B24-12 The tools follow the named approvals of Balanced (built 2026-11-24, D-41)

Balanced's alignment with the processes (document 21, B21-9) changes what some tools return. No tool is added, and tools.json still lists 74.

- **Who is asked, by name.** `submit_record` and `claim_resource` return `askedPrincipal` on each request they opened: the one person asked by name (a proposed owner, or the nearest portfolio's accountable person when no holder of the owner role is left), or null when the holders of the role are asked. `get_approval_status` and `get_decision` return `asksProposedOwners` on a requirement that asks each proposed owner by name, with no role or owner role; its Minimum Count is the number of people named. The `approvalNeeded` shape of the previews (`save_change_request_draft`, `propose_owner_change`, `save_application_draft`, `get_requirements` and the intake previews) takes `approverProposedOwners` as the third of its three kinds of approver.
- **Asked of me.** `list_approvals` (view mine) and `get_my_queue` list a request that asks a person by name for that person only, never for the holders of its role; the engine's answer check judges the same way (document 25, B25-14).
- **Owner gaps by scope.** An owner-assigned gate requirement makes an owner gap only for an application of its tier and kind (`get_application_owners`, `get_my_queue`, `list_owner_assignments`, `list_portfolios`, the portfolio summary). Balanced names a data owner at tiers 2 and 3 only, so a tier 1 application with no data owner has no gap. A read with no application in view counts every requirement, as before.
- **Consultation cards.** An intake now opens three consultation cards beside the platform admin's card (P01 step 5), so `submit_record` lists four requests, three with `isConsultation` true, and the views of `list_approvals` show them.
- **A standard change at submit.** `submit_record` on a change whose type requires a standard change model now gets the engine's OP-12 answer: a change that fits an approved model is accepted with no card (`engineAccepted.condition` standard-model) and scheduled, and `standardChangeApplied` names the model and the decision that approved it, which before was always null; a change that fits none is refused as not standard (`rule_violation`). Balanced's shipped model is approved by an ordinary configuration change (document 25, B25-14).
- **Outcomes.** A retirement decision's outcomes are complete and keep; a data use also takes retain; a flagged-agent decision takes keep, reassign, consolidate or retire. `save_decision_proposal` reads the outcomes from the configuration in force, so no schema changed for them.


### B24-13 The global search finds a record by its number (built 2026-11-24, D-41)

The web app's top bar has a global search: applications, people, and records by number such as DEC-0123. Applications (APP) and intake requests (IR) already matched their numbers through the `text` of `find_applications` and `list_intake_requests`, but no read took a decision, change or review number. The choices the author's AI sessions made under D-41:

- **One read, tool 75, `find_records`,** in the session toolset beside `find_people`: a number in, the record out, for every numbered entity. The alternative, a `text` filter on `get_decision_log`, `list_reviews` and the change schedule, would grow three contracts and still need three calls for one number.
- **Exact numbers only.** The search reads one number, not a prefix of one: "DEC-12" finds DEC-0012, never DEC-0120. People copy numbers whole, and a partial match would list unrelated records.
- **The same read rules as the lists.** Intake requests follow `intake-visibility`; the other numbered rows are read by any role, as their lists are, and row-level security on Azure SQL applies underneath. Configuration drafts are read by all roles (document 26, section 5.3, C46).
- **Words still go to the lists.** Words search applications (`find_applications`), intake requests (`list_intake_requests`) and people (`find_people`); the search page shows each in its own panel and opens a record at once when a number finds exactly one.

### B24-14 Fixes from the wave 2 QA run, security review and product review (built 2026-11-24, D-41)

The choices the author's AI sessions made under D-41, each with its reason. No tool is added; tools.json still lists 75.

- **Who reads an intake request.** `intake-visibility` "own" now also lets in the people a request involves: the business owner it names, a current owner (a deputy too) of the application it is linked to, and anyone an approval record on a decision about it asks now (by name, by a product role they hold, or by an owner role they hold) or was answered by. Reason: the decision page is the only place to answer (D-38), and Nadia, asked to fund Weather alerts, and Noor, consulted on triage, got "not found" there. A person with no part in the request still never finds it.
- **A decision about a hidden request is hidden as the request is.** `find_records` never finds a decision whose subject request the caller may not read: its statement names the request's title, and decision numbers run in sequence. `get_ai_activity` and `get_audit_trail` keep such a record's ID but show no label, and count it in `couldNotRead`; a refused call that named a hidden ID shows no record later. `get_tier_assessment` lends no answers from a request the caller may not read.
- **Linking a request to an application.** `save_intake_draft` takes `businessApplicationId` only for an application the requester holds an owner role on, or a draft application they created (`not_authorized` otherwise); `save_application_draft` links a draft to a request only for its requester or a platform admin. Reason: a requester-only person could otherwise have the engine register someone else's application and make their choice its business owner with no person deciding.
- **The named business owner is never the requester** (`validation` on `businessOwnerObjectId`). The named person answers the priority decision by name and becomes the owner of what the request starts, so it is someone else. Their answer on that card is their consent; a consent record for a request the engine accepts at the lowest tier is not built.
- **Tier answers are fixed under approval.** `answer_tier_questions` takes a request's answers only while it is a draft, and refuses an application's answers while a tier decision about it waits on approval records (`rule_violation`). A tier `confirm` with no `tierKey` is pinned at proposal: `save_decision_proposal` stores the tier the latest answers computed as Proposed Changes Json `tierKey`, so the approvers read, and acceptance applies, exactly that tier.
- **A decision that changes the application is about the application.** A tier, autonomy level or ownership decision, and a decision of an application type whose outcome moves the stage (go-live, retirement, design), takes subject kind `business-application` or `review` only (`validation` otherwise). Intake and priority decisions stay about the request; the engine scopes them at the higher of the request's computed tier and the linked application's tier (document 25, B25-15).
- **complete_review** refuses a different outcome decision when the review already names an accepted one. The engine also refuses a decision another done review used, or one decided before this review's cycle started (OP-08, document 25, B25-15). Reason: one old decision could otherwise close every recurring review.
- **Restricted demand fields.** `list_intake_requests` and `get_intake_request` return Score and Expected Benefit only to the PMO, the platform admin and the auditor, and to the row's requester (document 26, section 6.2). For anyone else the `scored` filter and the score order do nothing.
- **Leave dates stay HR data.** `record_owner_leave` and `run_owner_check` make an owner check due today, never on the leave date: any role reads a review's due date.
- **Refused calls in the ledger.** The core records at most 60 refused calls per caller and hour in the audit ledger; past that a refusal is still answered and logged by the host, but not written. Reason: a client looping on refused calls could otherwise fill the append-only ledger.
- **record_deployment by a person.** The output schema takes the route meaning `person-in-app` beside `ai-tool`: a person recording a deployment by hand saw HTTP 500 for a write that happened.
- **What a configuration change decision says.** Its statement is built from the draft's change summary ("Accept the configuration change: ..., version 1 to 2"), its outcome is `accept`, and `get_decision` returns `configurationChange` (the change summary, the base version, whether OP-09 passed, its problems) so the second admin reads what changes. `proposedBy` names the instructing admin. The draft's label in lists is its change summary.
- **What an owner change says.** `propose_owner_change` and `save_application_draft` word the statement from the change ("Make Kai Ostrowski deputy business owner of Inspection Scheduler.") and the context one sentence per change, never JSON or object IDs. `get_decision` drops the JSON block older decisions carry, and each owner change says `endsIsDeputy`.
- **The duplicate check** scores a name by the share of the name's key words the request holds (common words left out, a plural read as its singular), so a long request that names Crew Roster once still finds it. The purpose score is unchanged.
- **The portfolio of a request.** `save_intake_draft` takes `portfolioId`; the application an accepted request creates starts in it (O07 Portfolio, document 22, B22-13).
- **The card's components** say `vanishedInGapCheck` when an open vanished claim names them, so the retirement tracker reads the same for every role; the claim itself still follows `claim-visibility`.
- **Words.** Tool summaries say "1 approval is asked", never "card(s)", and point to the decision page (D-38).
- **Not built:** a Timeline portfolio filter; a sponsor consent record for an engine-accepted request; the `list_agents` read; record links that follow the request's own host in Development (`recordUrl` still names `https://localhost` there).

### B24-15 The data governance lead and the data tools' numbers (built 2026-11-24, D-37 and D-41)

- **A fifteenth human role.** `data-governance-lead` (D-41, question 4) joins `humanRoleKeys` in `tools.json`. Its Tool
  Policy rows: every tool any role or anyone signed in may call; `propose_owner_change` (its default roles add it: it
  names the owners of top-level data domains, and the engine still checks the owner relation for an application); and,
  like the data owner, `save_decision_proposal`, `submit_record`, `withdraw_record` and `save_link`.
- **Owner roles an application cannot have.** `propose_owner_change` and `save_application_draft` refuse, with
  `validation`, an owner role whose Subject Kinds leave out business-application (the data steward). The owner coverage of
  `list_owner_assignments` lists only the roles an application can have.
- **The data tools are 76 to 86.** The data taxonomy design proposed them as 52 to 62; tools 52 to 75 came first. They
  land in data taxonomy slice 3, in the new toolset `data` (86 `import_data_taxonomy` in `configuration`).

### B24-16 The data toolset and the changed tools (built 2026-11-24, D-36, D-37 and D-41)

Data taxonomy slice 3 (see [data taxonomy](../../architecture/data-taxonomy.md#mcp-tools-and-rest)).

- **Ten tools, 76 to 85, in the toolset `data`** (section 6.14). Six reads and four writes. 86 `import_data_taxonomy`
  waits for a later slice and has no contract yet. `tools.json` holds 85 tools; the build task's "from 65" came from an
  older count, and the contract file wins (a choice under D-41).
- **The web app acts, and the engine judges.** Every write runs OP-08, so a person's save on the web app is judged as an
  AI client's is: G-18, G-21 to G-25. A person's new use, location or mapping is confirmed when saved (`confirm: false`
  keeps it a draft); an AI client's is a draft, `confirm: true` from an AI client is refused on an existing row
  (`ai.data-row-draft-only`, so `ai_cannot_do_this`), and on a new row the engine forces the draft and the answer warns.
- **The never rule (G-25) for everyone.** A use, or a copy outside production, whose governing rule is never is
  refused when saved, with `rule_violation` and `data.rule-never`, on the MCP endpoint and on the REST API alike. The
  message names the rule, the domain and where the rule is set (`errors.json` lists `data.rule-never` under
  `rule_violation`).
- **Decisions follow the person.** A use or a copy that needs a decision gets a proposed data-use decision when it is
  saved, if the caller holds the proposal right (else a warning, never a refusal). A person's confirmed row sends it to
  the approvers at once; an AI client's never does. `submit_record` takes `data-use` (AI May Submit yes, once a person
  confirmed the use) and `data-mapping` (AI May Submit no); an AI client never submits a copy or retention decision, by
  any subject kind.
- **A preview-only write.** `move_data_references` names `read-only` as its only AI write state; `tools.schema.json`
  now allows that for a write tool, and the server serves it to an AI client with `validateOnly` only. Rows move in
  place, so their decisions stay with them; the answer lists the uses whose state changed.
- **The changed tools ([data taxonomy](../../architecture/data-taxonomy.md#mcp-tools-and-rest)):** `propose_owner_change` takes one of `businessApplicationId`,
  `dataDomainKey` or `dataLocationId`; for an application, the roles data-owner and data-governance-lead in its Tool
  Policy row no longer pass the owner relation, they stand for the data subjects. `get_application_owners` adds
  `dataOwners`; `get_application_card` adds the section `data`; `get_portfolio_summary` adds `dataGaps`;
  `get_requirements` lists `data-uses-declared` and `data-uses-decided`; `answer_tier_questions` adds `impliedAnswer` and
  warns DC-09; `save_decision_proposal` takes the subjects `data-use` and `data-mapping` and the type `data-retention` (a
  use that writes); `withdraw_record` takes draft `data-use`, `data-mapping` and `data-location`; `get_decision_log` takes
  `dataDomainKey` (with the domains below it); `get_my_queue` adds `data-gap` items and data drafts; `record_owner_leave`
  and `run_owner_check` add the data subjects; `explain_rules` takes `data-use`, `data-mapping` and `data-domain` with the
  aspect `data`; `get_decision` adds `data` (domain path, classification, governing rule and its source, the AI and
  de-identified flags, the state with approvals given, everyone asked, the stamped version) and `list_approvals` a
  `data` brief; `find_records` finds a data use by DU-. `get_configuration` and `propose_configuration_change` needed no
  change. A subject ID that is a domain key travels where an ID did (`get_decision`, `list_approvals`,
  `get_decision_log`, `get_my_queue`, `explain_rules`).
- **Record pages** ([data taxonomy](../../architecture/data-taxonomy.md#mcp-tools-and-rest)): `data-domain/<key>`, `data-location/<id>`, `data-mapping/<id>` and
  `data-use/<id>` under the host's record URL base, so `/app/records/...` on the Azure host.
- **Tool Policy rows** ([data taxonomy](../../architecture/data-taxonomy.md#mcp-tools-and-rest)): any role for the six reads; `save_data_use` maker,
  application-owner and platform-admin; `save_data_location` platform-admin, architect, application-owner and
  data-governance-lead; `save_data_mapping` platform-admin, data-governance-lead, data-owner and application-owner (the
  handler also asks for the domain's owner or steward, or the location's technical owner); `move_data_references`
  platform-admin and data-governance-lead; `propose_owner_change` gains data-owner. Two new relations name the data
  rights: `owner-of-data-domain` and `technical-owner-of-location`.

### B24-17 The wave 2 leftovers in the tools (built 2026-11-24, D-36 and D-41)

**Tool 86, `run_due_state_refresh`** (toolset `gapcheck`, person-only, platform-admin). A platform admin runs the engine's
daily due-state refresh now, on the web app (document 23, S2; document 25, B25-18). Input: only
`validateOnly`. Output: the counts, the reviews opened, the documents marked stale, the approval requests expired,
cancelled and asked again, every notice sent (reminder, repeat, escalation, overdue, expiry, flag) with its people,
roles and owner role, and the items that could not finish. The engine runs the same refresh every day on its own.

- **Numbering.** `tools.json` held 85 tools, so the new tool is 86. The data taxonomy had planned 86 for
  `import_data_taxonomy`, which has no contract yet; it moves to 87 ([data taxonomy](../../architecture/data-taxonomy.md#mcp-tools-and-rest)).
- **Its items commit one by one.** The tool runs the refresh before its read call (a new `ToolHandler.Before`), so each
  item commits in its own transaction and one failing item is reported as a warning while the rest are kept. The read call
  only labels the report. `validateOnly` runs every item and commits nothing. No `clientRequestId`: the refresh is
  idempotent.
- **Who.** The Tool Policy row allows platform-admin; the engine also refuses anyone but itself and a person holding the
  platform admin role (`due-state.refresh-not-allowed`, mapped to `not_authorized`). An AI client is never served it.

**Record links on a local run.** `links.recordUrl` and every record link followed the backend's configured base, so a local
run with no public base URL linked to `https://localhost/app/records`, which opens nothing. The server core now takes a
per-request base (`McpServerOptions.RecordUrlBase`, passed on the tool invocation). The host sets it from the request's
own scheme, host and port plus `/app/records`, only when it names no public base URL and no record base and the request
came to a loopback address (`127.0.0.1`, `localhost`, `::1`); otherwise the configured base stands. The contracts type a
record link with a new definition, `recordUrl`: an https URL, or an http URL on a loopback address. Evidence and other
links stay https only. The approval runner's cards still take only an https record link.

**The intake's portfolio.** `get_intake_request` and `list_intake_requests` return `portfolio` (`portfolioId`, `name`,
`fromApplication`): the request's own Portfolio (O07), else the portfolio of the application it is linked to. The list
takes `portfolioId` and keeps the requests in that portfolio and the ones below it, as `list_reviews` does. Choice: the
linked application's portfolio stands in, because a request about an existing application belongs where it lives.

**One owner gap count.** The Overview counted owner roles on registered and draft applications with no holder or no
deputy; the Owners page added holders whose account is off; the Portfolios page counted applications. They now share one
computation (`OwnerGapsOf`): owner roles on **registered** applications that have no holder where a gate requires one, no
deputy where the role requires one, or a main holder whose account the owner check found disabled or deleted, one gap per
application and role. `get_portfolio_summary.ownerGaps`, `list_portfolios` `counts.ownerGaps` (new) and
`list_owner_assignments.ownerGaps` (new, and the coverage per role) all give that number for the same scope;
`applicationsWithOwnerGap` (now also on the summary) counts the applications with one. The owner gap list keeps its no
holder and no deputy items; a holder whose account is off is counted, and the Owners page lists it from the owner
assignments, to propose a successor. Choices: draft applications are left out (they are still being set up, and their
named owner is asked on the intake); one gap per application and role, so a disabled holder with no deputy is one gap.

**What a tier needs.** `get_requirements` lists approvals only for the decision types that apply to an application: a type
whose default subject kind is a configuration draft, a data domain or a data location (configuration change, gate policy
change) is left out. A type with no default subject kind stays.

**One AI activity row per deployment.** A deployment an AI client recorded itself and that relies on an AI-only code
approval was listed twice, as a draft and as a code approval (Route Planner 2.0.0-rc.1 in the demo). It is now one
`code-approval` row whose `state` is `recorded` and whose client and person come from the draft. It counts once in
`drafts` (rows an AI client created) and once in `codeApprovals`, and shows in both views.

**The drafted by pair.** Every `stampedBy` gains `draftedBy` (`clientAppLabel`, `forPrincipal`): the AI client that created
the row and the person it acted for (document 22, B22-15), kept after a person edits it. Null for a row no AI client
created. A row from before falls back to its last stamp while an AI client wrote last, else to Created By Principal.
`get_ai_activity` takes the client and person of a draft from the pair first.

**The named business owner consents** (document 25, B25-18). `submit_record` on an intake whose engine acceptance waits for
the named owner's consent returns `engineAccepted` null, lists the consent request in `approvalsRequested` (asked by name,
owner role business-owner), and says so in its summary. The named person answers it on the decision page with
`answer_approval`, like any request that asks them; an AI client never does.

**Contracts.** `tools.json` holds 86 tools, `tool-policy.seed.json` 86 rows. New schemas
`run_due_state_refresh.input` and `.output`; `common.schema.json` gains `recordUrl` and `stampedBy.draftedBy`; the
intake, summary, portfolios, owner assignments and AI activity schemas carry the fields above; `errors.json` maps
`due-state.refresh-not-allowed` to `not_authorized`. The walkthrough transcript and its page are refreshed.

### B24-18 The reads and the write of the screens still missing (built 2026-11-24, D-36 and D-41)

The Configuration, Roles and access, Guidelines and playbook, Measures and slide-back signals, Admin, Places, Agents and AI
activity screens (in the screen design, a working file that is not published) each needed a read or a write the tools did not have.
Eight tools serve them, three tools changed, and refusals, requirements and gates now carry their guideline.

- **Numbering.** `tools.json` held 86 tools, so the new tools are 87 to 94. `import_data_taxonomy`, planned as 87 since
  B24-17, has no contract yet and moves to 95 ([data taxonomy](../../architecture/data-taxonomy.md#mcp-tools-and-rest)). The task's "from 65" came from an older
  count.

**The new tools**

| # | Tool | Toolset | Who | Reads |
|---|---|---|---|---|
| 87 | `list_configuration_changes` | configuration | Any role | C02 versions newest first with the decision that accepted each, the people whose approval counted and the day each answered (`acceptedBy`, `acceptedHow`: approval, engine, bootstrap or not-accepted); C46 drafts with their state, validity, dates and decision, paged |
| 88 | `get_configuration_draft` | configuration | Any role | One C46 draft as its record page shows it: the rationale, the row diff and the impact (applications touched, gate levels that move, gates whose requirements change, records in flight), the rule errors and warnings the engine stored, the decision with its approvals. nextSteps name `submit_record` and `withdraw_record` while it is open |
| 89 | `get_role_access` | configuration | Any role | Per product role: its party and duties, its group bindings (C30), the people in those groups read live from Entra as the person, its workload grants (C42), the tools it may call with how far an AI client goes through each, its decision rights (C31) and approval rules (C32) with their scope in words, and the separation rules (C40) in one sentence each |
| 90 | `list_guidelines` | configuration | Any role | C39 with the adopter's copy, adoption and where each guideline is used; the playbook chapters by track with the guidelines each applies; the build paths; the Known Client list |
| 91 | `get_measures` | portfolio | Any role | The nine default measures and the ten slide-back signals of playbook chapters 19 and 17, over equal windows that end today, each drilling to its rows |
| 92 | `get_admin_status` | configuration | platform-admin, configuration-admin, auditor | The host settings that are safe to show, the engine and configuration in force, the Tool Policy as a list, the clients and what each did recently, the notices the daily refresh sent, the approval outbox, and the run-now action |
| 93 | `list_agents` | portfolio | Any role | Every agent with its autonomy level (agreed, waiting or missing), owners and owner gaps, its security review, its go-live gate state (OP-07), its flags, its data uses and up to five Microsoft resources |
| 94 | `request_security_review` | decisions | Owner of the app, ai-lead, security-reviewer, architect, platform-admin | Writes O21 (type security-review, status due, trigger request) and O20 evidence links. An open security review is returned and nothing new is written |

**The changed tools**

- **`save_deployment_target`** takes `retire` and `retiredReason` (Places). `retire: true` sets the new O15 columns Retired
  On (today) and Retired Reason (document 22, B22-16) and changes nothing else; `retire: false` returns a retired target
  to service. While a target is retired, any other change is refused (`rule_violation`). The answer warns when pipeline
  map entries or open changes still point at the target. `list_deployment_targets` returns `retiredOn` and
  `retiredReason` and takes `state` (all, active or retired; all by default, so history stays visible).
- **`record_deployment`** (and the record endpoint, through the same runtime step) refuses a deployment to a retired
  target that finished on or after the retirement day, with the reason `deployment.target-retired` (document 25,
  B25-19), as `rule_violation` with guideline G9. `map_pipeline` refuses to map a pipeline to a retired target.
- **`get_ai_activity`** adds `clients`: per AI client, in the same scope as the counts, the calls it made that wrote or
  were refused, the rows it drafted, how many a person took forward, how many wait, its refused calls, and the Known
  Client key when the list holds it.
- **Guideline chips.** `get_requirements` items, `get_decision`'s gate hold, and the gates of `get_review` and
  `complete_review` add `guidelineRef` (`key`, `number`, `title`, `url`), a new `common.schema.json` definition, beside
  the guideline number they already carried. Every error envelope sets `error.guideline` when a guideline speaks to the
  refusal: every `ai_cannot_do_this` carries G17, and an engine reason carries the guideline of its prefix.

**Choices, each with its reason (D-41)**

1. **The configuration reads are any role's.** Configuration rows, C02 versions and C46 drafts are read by all roles
   (document 26, section 5.3), so the Configuration page shows the rules in force, the versions and the drafts to a
   reader too. Submit, withdraw and accept stay where they were: `submit_record`, `withdraw_record` and the decision page.
2. **Who accepted a version** is the people whose approval counted on its configuration change decision, with the day each
   answered. A version the engine accepted with no card names nobody (`acceptedHow` engine), and version 1 from the
   first install says bootstrap. Reason: the decision page is the only place a person accepts a configuration change
   (D-38), so its counted answers are the evidence.
3. **A draft's diff and impact are computed again** against the version in force at the time of the read, with the code
   `propose_configuration_change` uses (`DraftImpactOf`). A draft whose base moved says so (`baseMoved`). The stored
   validation stays the engine's word. Reason: the page shows what accepting the draft would change today, and the
   numbers match what the admin saw when proposing.
4. **A draft's AI mark comes from the call history.** The engine writes a draft as a nested write, so the row has no AI mark
   of its own; the first call that wrote it names its actor kind. Reason: the Configuration page must show an AI draft
   as every other AI draft is shown (D-36).
5. **Role access is any role's, and the holders are read live.** Every part is a configuration row, and
   `get_role_holders` already shows any role who holds a role. Holders are read only for one role by default, because all
   roles at once is one Graph call per bound group. A workload role lists its grants and no tools: it calls no MCP tool.
   Without Graph the holders are null and `couldNotRead` counts them.
6. **The guideline a refusal applies comes from a fixed map in the product** (`Envelope/GuidelineRefs.cs`): reason code
   prefixes to guideline numbers (ai to G17; separation, authorization and standard-model to G12; tier to G3; dependency
   to G6; documentation to G20; decision to G4; register to G1; role and actor to G14; deployment to G9; data to G8;
   audit to G15), the AI rules G-01, G-06, G-08, G-17 and RA-06 to G17, and every `ai_cannot_do_this` to G17. Reason:
   guideline numbers are permanent and reason codes are stable, and no configuration row ties a guard rule to a guideline
   (R13 left this open). An adopter who does not adopt a guideline still sees its number; `list_guidelines` says it is
   not adopted and why. A refusal no guideline speaks to carries none.
7. **The chip carries the key, number, title and link**, the link being the adopter's own copy, else the hub link. The
   error envelope keeps the number alone (its schema is shared by every tool), and the page takes the title from
   `list_guidelines`.
8. **`list_guidelines` serves on the REST API what the resources serve on MCP.** The design asked to serve `guideline://`
   and `playbook://chapter/` on REST; a read tool does that through the one REST route every tool has, under the same Tool
   Policy. The playbook index now reads each chapter's track and guideline list from its header (`PlaybookIndex`), never
   its text (D-17). Chapter 0 has no number in the index, as before, so the list holds chapters 1 to 22.
9. **Measures: flow by window, coverage now.** Lead time, decisions on time, standard change share, deployments on record
   and AI work accepted count events per window by the date that ends them. Owner coverage and documents current read
   the records as they are now, because the store keeps no history of a state, so only the newest window holds a value.
   Share registered reads each window's latest gap check run. Windows are 30 days, six of them, by default.
10. **Targets and thresholds are the playbook defaults, shipped and marked.** No configuration entity holds them yet
    (`targetSource` not-set, `thresholdSource` product-default). Adding one needs a new configuration entity, its
    validation, the presets and a migration, which is a configuration slice of its own; this read's shape already
    carries `target` for it. The baseline is the oldest window with a value, as chapter 19 says: measure first, then
    agree a target.
11. **Lead time** runs from the intake's submission (the submit call in the call history, else the first approval request
    of its decisions) to its application's first succeeded production deployment, the median per window. **Decisions on
    time** compare an answer with its approval rule's Timeout Days, else the request's Expires On. **Deployments on
    record** are production deployments with none of the four flags. **AI work accepted** is the drafts a person took
    forward out of the drafts taken forward, beside the drafts left waiting longer than `draft-expiry-days`.
12. **Two signals are read outside the product** (environment requests in the ticket tool, rebuild projects in
    architecture decisions): they answer `firing` null with where to read them, never a guess. Direct role assignments
    (signal 10) need Graph, so the signal names `check_role_bindings` and counts expired grants and pipeline-route
    records written by a person.
13. **No signal links to the decision it led to.** A decision about a signal is recorded as any decision (G4), but no
    column ties it to the signal yet; the page links to the decision log instead.
14. **The Admin page's host settings come from the host.** `InProcessBackendOptions.HostFacts` is a function the Azure host
    sets (`HostFactsReader`): environment name, demo flag, step-up mode, the refresh hour, the store kind and schema
    version, and the approval outbox by state with the failed rows and the rows sent more than two days ago. No URL, key,
    connection string, client ID or tenant ID. The schema version shown is the version the build needs, because the host
    does not start below it.
15. **The notices are listed for the platform admin and the auditor only.** They come from audit entries and name people;
    the configuration admin reads no audit entry (document 26, B26-11), so it sees the counts, and `couldNotRead` counts
    the rest. The clients' recent calls are counts by client, which every reader of the page may see.
16. **Retiring takes today and a reason, and nothing else changes in the same call.** A retired target may come back into
    service, because its link kind and external ID stay its business key: bringing it back is the only way to use that
    environment again. A deployment that finished before the retirement day is still taken, so a late pipeline report
    of history is never lost.
17. **`list_agents` replaces B24-11's "no list_agents".** The Agents screen needed one call per agent and five reads each,
    and the go-live gate state was in none of them. The agent kinds are the application kinds the autonomy level gate
    requirement covers (Balanced: agent), read from the configuration (D-06), else the kind keyed agent.
18. **`request_security_review` may be called by an AI client**, as `request_architecture_review` may (working default, open
    design question 9: AI May Submit is yes for review requests). A review request asks people to work and decides
    nothing; the review is completed only once its outcome decision is accepted. The Tool Policy row can tighten it to
    read-only. The review type is Balanced's `security-review`.
19. **The clients view counts recorded calls.** Reads leave no record (document 25, B25-13), so `calls` counts the write
    calls and the refused calls of each client, and the field says so.

**Contracts.** `tools.json` holds 94 tools, `tool-policy.seed.json` 94 rows. 16 new schemas; `common.schema.json` gains
`guidelineRef`; the requirement, decision, review, target and AI activity schemas carry the fields above. The web app's
`contracts.ts` types the eight tools and the changed fields for the screens slice. The walkthrough transcript and its
page are refreshed.

**Not built.** Configuration rows for measure targets and signal thresholds; a link from a signal to its decision; the
outbox retry action (the runner releases and retries a row by itself, and a failed row waits for a person);
preset-as-draft (`propose_configuration_change` still refuses a whole preset); a reader for Microsoft's agent registry
and its flags.

### B24-19 The tools after the wave 3 reviews (built 2026-11-25, D-36 and D-41)

The contract changes of the wave 3 fix pass. The tool count stays 94; no tool was added. Each change keeps tools.json,
the schemas, tool-policy.seed.json and the fixtures in step.

- `get_decision_log` items carry `ownerChanges` (each proposed owner change by owner role key, deputy flag and add or
  end), and `proposedBy` is set once: the instructing admin of a configuration draft, else the person an AI client
  drafted for, else the creator (also in `get_decision`, the evidence pack and the review reads). Pages match a waiting
  owner change to a role by key, never by the words of the statement.
- Owner change statements name the changes ("End Wren Abernathy as business owner, make Nadia Ferreira business owner
  and end Nadia Ferreira as deputy business owner of Claims Triage Agent."), never a count; an end takes the deputy flag
  of the assignment it ends, and dates read as the pages show them.
- `get_ai_activity` and `get_audit_trail` refusals carry `guideline` and `guidelineTitle` (G17 for an AI client's refusal
  and for a tool the product does not have), and `rule` is the engine's own words when the call-refused entry kept them
  (a rule refusal). AI activity lists data uses, mappings and locations an AI client drafted, and a person's confirm takes
  them forward. For the PMO the counts are the estate's; the rows stay the AI work done for the PMO (D-41).
- `get_decision` gate items of kind data-uses-decided list `openUses` (each undecided use, its state and the decision
  that would decide it), so the decision page links to what holds the gate.
- `get_configuration_draft` impact lists `dataUses`: each live use and copy on the touched domains, with its state now and
  under the change. The draft's change reads in labels on the decision page and the draft lists.
- `get_admin_status` notices list `recentRuns` of the due-state refresh (when, who, the counts, the reviews it opened).
- `get_data_gaps`, `get_data_domain` and `get_application_card` findings carry `ownerRoleKey` and `isDeputy` for DC-03 and
  DC-04 (in `common.schema.json`, `dataFinding`).
- `get_role_access` `holdersReadFrom` has the value `demo-personas`: on a demo install, or a local run with the directory
  mode Demo, the holders and the binding check come from the demo personas and their fictional groups. Since the wave 4
  carry-over it says where the holders come from on every read, also one that lists none, and a bound group carries the
  same name in `get_role_access`, `get_role_holders` and `check_role_bindings`: the name the product holds for it, else
  the directory's (a choice under D-41).
- `get_ai_activity`'s summary counts one scope at a time: "for you" is the reader's own scope, and the estate figures the
  PMO's counts carry follow, named as the estate's (a choice under D-41).
- `save_application_draft` takes `holdsNoBusinessData`; on a registered application it is the only field that changes,
  set by an owner or the platform admin, never by an AI client.
- `submit_record` for a data use or a copy reports the decision it sends as proposed. `resolve_data` answers a guessed
  External Id or Url as unknown to a caller who may not read them, and a location saved with no label is labeled
  "Unnamed <platform> location", never with its External Id or Url. A record link follows the request's own address only
  for a loopback request.
- Messages: people answer approvals on the decision page (D-38), so no answer speaks of approval cards; the refresh
  summary names the reviews it marked overdue; data findings name labels, never a key, a tool name or an ISO date.

### B24-20 Teams and committees (built 2026-11-25, D-41)

The product owner asked: "where is the page of teams/commettes and people, aka owners of the apps and data, to demo?" Two reads serve
the Teams page and each team's page: `list_teams` (95) and `get_team` (96), in the
session toolset beside `get_role_holders` and `find_people` (section 6.2).

- **Numbering.** `tools.json` held 94 tools, so the new tools are 95 and 96. `import_data_taxonomy`, planned as 95 since
  B24-18, has no contract yet and moves to 97 ([data taxonomy](../../architecture/data-taxonomy.md#mcp-tools-and-rest)).

**Choices, each with its reason (D-41)**

1. **A team is a group the install knows**: a Principal Reference of kind group, or a group a Role Group Binding of the
   configuration in force names. No new entity, and membership is never stored. Reason: the product stores links, not
   copies, and on a real install a bound group may have no Principal Reference: a binding is a configuration row, not a
   record that names the group.
2. **A team's ID is the group's Entra object ID.** Its record link is `/app/records/team/<objectId>`, which the web app
   opens at `/app/teams/<objectId>`, as a portfolio's record link opens `/app/portfolios/<id>`. Reason: owner
   assignments, role bindings and the person page all name a group by its object ID, and a group known only from a
   binding has no row ID.
3. **Kinds are derived on every read from the configuration in force, never stored.** A committee is bound to a role an
   Approval Rule asks (to approve, or as a consultation) or a Decision Right gives the responsible, accountable or
   consulted letter. Reason: a list of names goes stale when an adopter changes its rules. Under Balanced this makes nine
   committees: the seven the plan named (change board, architects, security reviewers, PMO, AI leads, data governance
   leads, configuration admins) and also the change managers (change authorization) and the platform admins (tier and
   ownership), whom Balanced asks to decide too. The requester, maker, application owner, data owner, auditor and reader
   groups are role groups: none of those roles answers a decision through the role. The data owner answers as the owner
   of a domain or an application, which is a person. A consultation counts, because under Balanced the PMO's only
   approval is a consultation on intake, and the PMO's decision rights are responsible ones (priority and portfolio).
4. **A support team holds a current support owner or technical owner assignment**, on an application, a data domain or
   a data location. A group that is none of the three kinds is "other", so every group has a kind and every count on
   the page opens rows. A group can be a committee and a support team, or a role group and a support team.
5. **Members come from the directory reader the install already has** (`InProcessBackendOptions.Directory`), never
   stored: Microsoft Graph as the signed-in person when the directory reads are on; the demo personas on a demo install
   and on a local run with the development identity; none when the reads are off. Then `members` is null and
   `membership` says "Membership is managed in Entra" and names the group to open there. When Entra does not answer,
   `members` is null, `couldNotRead` counts `team-members`, and a warning says so; the rest of the page still answers.
   `membersReadFrom` is the source `get_role_access` names in `holdersReadFrom`.
6. **The demo personas gain teams** (`seed/demo-personas.json`, `teams`; the development personas' `Teams`): the four
   support teams of the demo estate have members (Priya Castell in Field Apps Support, Jonah Pereira in Finance Apps
   Support, Hugo Brandvold and Theo Marsh in Service Desk, Owen Achterberg in Platform Team). A request as the persona
   still carries only its role groups, so a team never gives a persona a role, and every role group a persona signs in
   with stays bound (the Azure adapter tests check both). Reason: a support team with no member could not show "who is
   in it" in the demo; making the support teams role groups would change who holds which role.
7. **Member counts in the list are read for the page's rows only**, one directory call each, as role holders are read for
   one role at a time (B24-18, choice 5). The "member of" filter (`memberObjectId`) reads every team's members once per
   call; a Graph memberOf read for one person is the better answer for a large directory, later.
8. **What waits on a team is what would wait on a person** (`list_approvals` mine): a request that names someone is
   theirs alone, so it waits on the team only when it names the team; else it waits when the team holds the product role
   it asks, or holds now the owner role it asks on the decision's application (a deputy too). Requests on an intake the
   caller may not read are counted in `couldNotRead`, never shown, as `list_approvals` does.
9. **Recent answers are the latest 20 answers people gave through the team**: approved or rejected by a person (an AI
   recommendation never counts), on requests the same test matches. When the members are read, only a member's answer
   counts, so a holder of the same role through another group is not credited to this team; without the members, every
   answer through the team's role counts. `counts.answered` counts them all.
10. **Owner gaps are the one owner gap count** (B24-17) over the team's registered applications: every gap of those
    applications, not only the team's own roles. Reason: the team page is where its people see what the applications
    they support still lack, and the number matches the Owners page for the same applications.
11. **Both reads are any role's, and an AI client's as reads.** The members are names any role already reads through
    `get_role_holders` and `get_role_access`; everything else is configuration rows and records any role reads. The
    Tool Policy rows can tighten either.

**Contracts.** `tools.json` holds 96 tools and `tool-policy.seed.json` 96 rows (the two new ones for all fifteen human
roles, read only). Four new schemas. The web app's `contracts.ts` types both tools (`TeamList`, `TeamDetail`) for the
screens slice. The walkthrough transcript and its page are refreshed: the tools list for Priya's roles grew from 68 to
70.

**Not built here.** The screens (`/app/teams` and `/app/teams/<id>`, the menu entry, and the links in from the person
page, the decision page, Owners by role and Application 360's owners) and the web app's route for the `team` record
link: the screens slice. A Graph memberOf read for "member of".

### B24-21 The list reads the pages fanned out for, the person's day, and the sign-in first (built 2026-11-25, D-41)

The wave 3 leftovers: list reads the pages still fanned out for, date-only values, server logs, the request limit on a
demo install, and two security follow-ups (the sign-in check before a message is judged, and HSTS). Each choice is the
AI sessions' under D-41, with the reason.

1. **Two new reads, tools 97 and 98: `list_data_uses` and `list_data_mappings`.** The Data page's Uses and Mapping tabs
   read `get_data_domain` once per domain with rows (up to 60), and a use's or a mapping's record page looked for its row
   in its domain. One list across domains answers both, with the domain filter (its subtree by default) and one row or a
   few by ID. Each item is the `dataUseItem` or `dataMappingItem` that `get_data_domain` lists, with its OP-16 state
   computed when read, so a page shows the same row the same way wherever it reads it. Open rows by default; a row read
   by ID is read whatever its end date, so a record page still opens an ended use. Any role, read only, Max AI State
   read-only, like `get_data_domain`. Reason: two tools named for what they list read better for an AI client than one
   tool with a kind switch, and the shapes already exist. `import_data_taxonomy` moves to 99.
2. **The other list reads are filters on the tools that already answer the question.**
   - `list_documents` takes `documentLinkId`: one link, on its application, whatever the application's registration.
   - `list_gap_check_runs` takes `gapCheckRunId`: one run; claim-visibility still applies.
   - `get_pipeline_map` with neither input lists every pipeline the product holds; a pipeline mapped to no target comes
     with no entries, and the summary counts them. Both inputs at once stay refused.
   - `find_applications` takes `usesAi` (true or false; an application with no answer yet matches neither, because
     "not known" is a gap of its own, not a no) and `buildPathKeys` (keys of the build-path list).
   - `get_change_schedule` and `get_deployments` take `portfolioId`, child portfolios included, as
     `find_applications` and `list_documents` do (the Timeline by portfolio).
   - `get_audit_trail` takes `clientApp`: the entries made through that client, by the label the engine stamped, case
     ignored (AI activity by client).

   Reason: a new tool for each would repeat a list that exists; a filter keeps one answer shape per question.
3. **The person's own day for date-only defaults.** The server stamped a date-only default with the UTC day, so after 7
   pm on the US east coast a review done that evening read as done tomorrow. The web app now sends the person's day in
   the display zone with every request (`X-Product-Local-Day: YYYY-MM-DD`); the host passes it to the core
   (`McpServerOptions.CallerLocalDay`, `ToolInvocation.CallerLocalDay`), and `ToolScope.PersonToday` uses it when it is
   the UTC day or the day before or after (every time zone falls in that range), else the UTC day. It sets the day a
   review is done (`complete_review`, whose "not later than today" check uses it too), a document checked
   (`confirm_document_checked`), a review falls due by default (`request_architecture_review`, `request_security_review`,
   the owner check), a component or a deployment target retires, and the "has passed" check of an exception's review
   date. An AI client sends none and gets the UTC day, as before. Data rows (a use's or a mapping's start and end, a
   location's retirement) keep the UTC day: the engine judges a data row open or ended by the UTC day, and a move ends
   one row and starts the next on the same day, so a person's day there could leave both open for a day. Reason: the
   person sees the day they acted; one header covers every write without a field on each contract.
4. **The request limit on a demo install.** While a demo persona acts, the operator's requests to the REST API and the
   MCP endpoint are counted apart from their own, under `Product:Limits:DemoOperatorPerMinute`, default 1,200 a minute
   (the per-person API limit is 300, MCP 120). Reason: one person presenting fifteen personas reloads every page with
   each switch (a page makes 8 to 20 reads after this wave), and the demo director sends the story's calls as the operator
   in one burst; four times the API limit covers both and still stops a page stuck in a loop within the minute. 0 keeps
   the group's own limit. The operator's own requests keep the per-person limit.
5. **Server logs.** A request the client canceled (the browser moved on, the tab closed) or whose body it cut off is one
   information line with the correlation ID, never an error with a stack trace; a cut-off body is answered 400, as
   Kestrel answers it. The telemetry lines always name the caller by hash: a host with no `Product:TelemetrySalt` uses a
   random salt made for its process (kept in memory, logged once as a warning), and the ping line names its caller too.
   Reason: a canceled request was logged as a server failure ("Unexpected end of request content") with a stack trace,
   and the caller field of every telemetry line was blank.
6. **The MCP endpoint checks the sign-in first.** Any message without a valid token, whatever its shape (an empty
   object, a notification, a request), is answered 401 with the challenge that names the protected resource metadata,
   or 403 for a token without the scope, before the message is judged; `{}` was answered 200 before sign-in. A body that
   is not JSON, or a batch, is still refused by the transport with 400.
7. **Strict-Transport-Security.** Every answer to a request that reached the app over https carries it, directly or
   through the platform's front end (X-Forwarded-Proto), except to a loopback host; a host whose public URL is https
   outside Development sends it on every answer, as before. Reason: the header then follows how the request came, not
   only the configuration; a local run on 127.0.0.1 or localhost stays reachable over plain HTTP.
8. **Tests:** `ListReadTests` (MCP core: the two new reads, every new filter, the pipeline list, the read cache, the
   person's day, the sign-in order), `ToolContractTests` (a case for each new tool), `HostFollowUpTests` (canceled
   requests, the caller hash without a salt, HSTS, 401 before sign-in on both transports, the local day through the
   REST API), `DemoPersonaTests.TheDemoOperatorHasTheDemoLimitAcrossPersonasAndTheirOwnLimitAsThemselves`, and the web
   app's `apiClient.test.ts` for the header.

### B24-22 The engine leftovers: targets, "Ask again", the outbox retry, the demo's freshness, the impact walk and the data items (built 2026-11-25, D-41)

The wave 3 leftovers on the engine side: measure targets and thresholds, the outbox retry and the demo reset, the data
taxonomy's minor items, and the impact walk from a component or a data location. Four new tools, 99 to 102, so
`tools.json` and `tool-policy.seed.json` hold 102 entries. Each choice is the AI sessions' under D-41, with the reason.

1. **Measure targets and signal thresholds are two Setting rows, not a new entity.** `measure-targets` holds the target
   per measure key and `signal-thresholds` the numbers per signal key (`MeasureSettings` in the configuration engine;
   V38 requires both and refuses a key that is not a measure or a signal, or a number a signal does not take). Balanced
   ships `measure-targets` empty (chapter 19: measure first, then agree a target) and the playbook's three thresholds;
   the demo configuration patch adds the targets the demo sponsor agreed. `get_measures` answers `target`,
   `targetSource` configuration, and `targetMet` (the newest value read in the healthy direction), and each signal's
   `thresholdSource` says whether the setting gave its numbers. Reason: B24-18 choice 10 asked for a new configuration
   entity, which needs a table, a migration, the Dataverse metadata and a page of its own; a json setting is already a
   versioned configuration row that a configuration change proposes, validates, shows as a diff and a person accepts,
   and a measure has one number and a signal at most two. The read's shape was ready for it.
2. **A signal links to the decisions it led to through the decision.** `save_decision_proposal` takes
   `slideBackSignalKey` (one of the ten signal keys) and stores it on the decision (O09 Slide Back Signal, a proposal
   column, set while proposed and frozen with it). Each signal in `get_measures` lists `decisionCount` and the latest five
   decisions with their numbers, status and links; `get_decision` answers `slideBackSignal` (number, key, label). The
   decision keeps its own subject (an application, a claim, a change), as every decision has one. Reason: chapter 17 asks
   that every signal that fired has a recorded decision with an owner and a date; the decision log already records it,
   and one key on the decision is all the link needs. A JSON column of O09 needs no migration.
3. **"Ask again" is tool 100, `renew_approval_request`.** On the person entry only. The proposer of the decision (the
   admin who instructed a configuration draft, else the person an AI client drafted for, else the creator) or a platform
   admin asks a request again; the engine checks it (`approval.renew-not-allowed`). Only while the decision is proposed
   (`decision.final`), and only for a request that expired (its Expires On came, whether or not the daily refresh marked
   it) or one in the last 48 hours of its time (`approval.not-renewable`). The engine closes the old request (expired, or
   canceled when asked early) and opens the new one through the recompute the daily refresh runs after an expiry, with
   the Timeout Days of the decision's own configuration version; a consultation is opened again from OP-02. A request
   asked again already answers with the open one and writes nothing. Reason: the daily refresh restarts an expired
   request only when it runs (the timer is off until an hour is set), and a demo must never meet an expired request;
   the 48 hours let the demo kit's refresh renew what would lapse during a meeting. A person decides to ask again (D-36),
   so no AI client is served it.
4. **The outbox retry is tool 101, `retry_approval_card`.** On the person entry only, for the platform admin, from the
   Admin page's list of cards that need a person's eye. A failed card, or one sent more than two days ago with no answer,
   goes back to the approval runner with no attempts used; a card whose request was answered, expired or canceled
   meanwhile is closed instead (`action` closed), and any other card is refused with what it is waiting for. The outbox
   is a host table, so the host gives the backend a port (`IApprovalOutboxPort`); without one the tool answers that this
   host keeps no outbox. The call's Audit Entry lists the host's row (`byHost`), so the retry is on the record though no
   engine row changed. Reason: the runner stops after its attempts and a failed row waited for a person with no way to
   act; two days is the Admin page's own "sent long ago".
5. **The demo's freshness is tool 102, `get_demo_freshness`.** Any role, read only, on a demo install only: the Demo
   environment, or a local Development run whose `Product:WebApp:Demo` says it holds the demo estate; anywhere else
   not_found, as the runner's routes answer when they are off. It lists the story's approval requests that expired with
   nobody asked again, or expire within 48 hours (`withinHours`, up to a week), with the decision, the application and
   who is asked, and says "run demo refresh". Reason: the Demo guide must warn before a meeting, and a real install has no
   story to be fresh.
6. **The impact walk starts from a component and a data location too.** `get_dependency_impact` takes
   `dataLocationId`. From a component, the first ring is its own application (`via` part-of), the applications that
   depend on it, and those whose open data uses name it as their AI component; from a location, the applications whose
   open data uses name it or a location inside it (`via` data-use) and the application it is (`hosts-location`). OP-10
   walks the dependants on from that ring, one hop less. A node of the first ring carries no dependency type
   (`dependencyTypeKey` null); a location start answers `dataLocation` (what it holds, its child locations and its data
   uses). Reason: "what breaks if it retires?" is asked of a server or a database as often as of an application, and the
   data uses already say who reads and writes there. A component and a location depend on nothing the register holds, so
   their dependencies walk is empty.
7. **The data taxonomy's minor items** are these: `import_data_taxonomy` (tool 99)
   with DC-15 in its answer, DC-10's offer, the Places link (`dataLocations` on a deployment target), the implied answer
   before the person answers (`get_tier_questions` with an application or an intake), the app-level data owners to clean
   up (`get_data_gaps` `appLevelDataOwners`), the data-use effect in a proposal's own preview
   (`propose_configuration_change` `impact.dataUses`), the O25 Platform plus Url key, the C31 and C32 flags, and the small
   team overlay.
8. **Tests:** `EngineLeftoverToolTests` (MCP core, 16 cases), `ToolContractTests` cases for the four tools,
   `EngineLeftoverHostTests` (REST API: the retry through the host's outbox, the freshness read on and off a demo
   install), `ApprovalOutboxStoreTests.ARetryPutsAFailedOrLongSentRowBackToPendingAndLeavesOtherRowsAlone`, and three
   OP-08 fixtures for the O25 key.

### B24-23 The backend follow-ups of wave 4b (built 2026-11-25, D-41)

Wave 4b: the backend items of the verify round, the product review's main finding (every web-app write action played
and tested) and what the presenter walk found during the build. Each choice is the AI sessions' under D-41, with the
reason. No new tool; three new optional output fields (`answerableCount`, `counts.everything`, `openVanishedLastSeen`),
two new optional inputs (`portfolioId` on `list_approvals` and `get_ai_activity`) and one new warning
(`data-move.looser-target`).

1. **`list_approvals` view mine counts only what the person may answer, and `get_my_queue` lists only that.** The engine's
   answer check runs on every request that asks the person. The requests they may answer come first and are counted in
   `answerableCount`; a request a separation rule keeps them from answering stays in the list after them, with
   `canAnswer` false and `cannotAnswer` saying why, and the summary adds "N more ask you, but a separation rule keeps you
   from answering." `get_my_queue` leaves such a request out. For an AI client the check is judged for the person it acts
   for (the engine's `ApprovalAnswerCheck(..., forThePerson: true)`), because an AI client's own check always stops at "AI
   never approves". Reason: Nadia's own tier 3 risk acceptance was counted as waiting for her answer and offered
   `answer_approval`; the decision page still reads the blocked request from mine to say why, so the rows stay.
2. **The gap check runs in a demo.** The Azure host binds `DemoInventoryReader` in the Demo environment, and in
   Development with the directory mode Demo. Its inventory is the tenant the demo estate describes: every registered
   component of a covered kind that has an External Id and is not retired, as the register holds it now; the item of every
   unregistered claim, whatever its status; and the items of the demo inventory file (`Product:Directory:DemoInventoryFile`,
   default `seed/demo-inventory.json`, optional), less every component that already has a vanished claim and the External
   Ids the file lists as absent. It pages by its own skip token and refuses an environment filter in plain words. Reason: a
   static list would raise a vanished claim for every registered component it missed, including the ones a person
   registers or claims during the demo; the register is the demo's truth. A first run opens a claim for each new file item
   and a second run opens none. **Fixed with it:** a run of more than one page passed External Ids where OP-13 takes
   Technical Component IDs in `seenThisRun`, so every component of an earlier page vanished on the last page.
3. **Version 1's activation date is stable.** The seed writes Published On (the seed moment) on the Configuration Set
   Version row; `whoami`, `get_configuration`, `get_configuration_health` and `get_admin_status` read one helper: Published
   On, then Engine Checked On, then the oldest audit entry's moment (a store seeded before the fix), and only then now.
   Reason: "in force since" and the review due date followed the clock. A row value, so no migration.
4. **Marcus Oyelaran signs in on a local run** (`demo-marcus`, architect and change board), after Tomas Rendel, in the
   persona file's order: seventeen development personas.
5. **`list_approvals` and `get_ai_activity` take `portfolioId`**, child portfolios included, as `get_decision_log` does
   (B24-21); an intake request with no application counts in its own portfolio. Reason: the Overview's "Approvals waiting"
   and "AI drafts waiting" tiles stayed estate-wide under a portfolio.
6. **The AI activity counts add up.** `counts.everything` counts each activity once. `get_admin_status` takes each AI
   client's drafts and refusals from the same activity list as `get_ai_activity` (one builder, the window by day), and a
   person's own client keeps its refused calls from the audit entries. Reason: "Everything 13" stood against 12 rows, and
   Admin said 9 drafts where the AI activity page said 7.
7. **Two counts agree.** `list_gap_check_runs` `openClaimsLastSeen` counts open unregistered claims and the new
   `openVanishedLastSeen` the open vanished ones. The Admin "Notices sent" tile counts expired-approval notices with the
   overdue ones (the engine's notice kind is `expired`; the tile read `expiry`), and each recent run's total sums the six
   notice kinds the run wrote on its audit entry (`noticeKinds`: remind, repeat, escalate, overdue, expired, flag), so a
   run's total and the tile come from one definition.
8. **A review requested with no due date is due in 14 days.** The new setting `review-request-due-days` (number, 14 in
   Balanced; document 21, section 3.6) gives `request_architecture_review` and `request_security_review` their default.
   Reason: a review asked today was due today.
9. **The separation refusal names the rule by its label** ("The separation rule No self-approval at tier 3 keeps you from
   answering..."); the key stays in the reason's Ref.
10. **Only the newest record of an ask is asked again** (`renew_approval_request`; document 25, B25-24): an older lapsed
    record of an ask that was asked again answers the newer pending request (renewed false), or is refused with "asked
    again as <newest>; ask that one again", and writes nothing.
11. **A retired application owes no document** (OP-06 `isRetired`; its links stay as history), and **a move into a looser
    data domain warns** (`data-move.looser-target`, in the preview and the answer).
12. **Speed** (document 25, B25-23): the later pages of `get_data_gaps` and `list_data_uses` read the list the first page
    worked out, kept for 60 seconds and emptied by any write call in the process (key: tool, caller, roles, configuration
    content hash, input without top and skipToken), so a page costs a few reads, not the whole list again. Key set
    prefetches in `get_decision_log`, `list_approvals`, `list_owner_assignments`, `get_measures`, `find_data_locations` and
    `find_data_domains`; `record_owner_leave` reads its owners and reviews in two key set reads (a write call has no read
    cache); one application's data findings come from that application's facts (OP-17's input, document 25). At 2,000
    applications: `get_measures` 219 to 24 reads, `get_decision_log` about 300 to 11 a page, `list_approvals` mine 132 to
    11, `record_owner_leave`'s preview 1,346 reads and 3.3 s to 390 and 0.3 s, `get_application_card` 1.2 s to 0.4 s.
    `ReadSpeedTests` holds ceilings for these at 2,000 applications, every page. The MCP request log line reports the
    request's database round trips (`StoreRoundTrips`).
13. **Tests:** `WaveFourBTests` (MCP core: one passing and one refusing case per item), `DemoInventoryTests` and
    `StoreRoundTripTests` (host), `ReadSpeedTests.TheListReadsKeepAFewDatabaseReadsAPageAtTwoThousandApplications`, and
    engine fixtures for V38 (`review-request-due-days`), OP-06 (`retired-application-owes-no-document`,
    `live-application-stale-link-incomplete`) and the separation message (`messageContains` on
    `self-approval-at-tier-3-refused`).

### B24-24 Wave 4b, fix round 1: what the demo walk and the coverage check found (built 2026-11-26, D-41)

The fix round of wave 4b, after the QA coverage check, the presenter walk and the review. Each choice is the AI
sessions' under D-41, with the reason. No new tool; one new optional output field (`counts.recorded` on
`get_ai_activity`) and one new optional error field (`recordUrl`, envelope.schema.json).

1. **A person's confirm takes an AI draft location or mapping out of draft, and says so** (`save_data_location`,
   `save_data_mapping`). The engine already confirmed a row only on `confirm: true`; the web app's "Check and confirm"
   sent none, so the row stayed a draft. The record pages now open their sheets in a confirm mode that sends it, and
   "Confirm seen" on a draft sends it too, as its sheet says. Each answer adds "N draft is confirmed: no longer a
   draft.", and a mapping answer says "Drafts wait for confirmation." when one stays a draft, as a location answer
   already did. Reason: the button said confirm and nothing was confirmed; the words told only a careful reader.
2. **Ending a use or a mapping sends the row as it is on record now** (the web app). The end sheets seeded their form
   when the page opened, so an end right after a change on the same page put the old purpose or origin back. The input
   of an end is built from the record, never the form, and every data sheet starts from the record each time it opens.
3. **The end and retire sheets start from the person's own day.** The web app's end sheets default to the person's
   calendar day, and a retired location's Retired On is `ToolScope.PersonToday`. This narrows B24-21, item 3, for the
   days a person gives: the engine still judges a row open or ended by the UTC day, and a move still ends one row and
   starts the next on the UTC day. In a time zone ahead of UTC a row ended today stays open until the UTC day reaches
   the day given, at most 14 hours. Reason: in a US evening the sheet said "Today in UTC is" tomorrow, and the record read
   "Retired on" tomorrow beside a last change of today.
4. **Due and overdue count by the reader's day.** `list_reviews`, `get_review`, the agents' open review, the portfolio
   summary and the review tiles call a review overdue when its due date is before `ToolScope.PersonToday`. The daily
   refresh, which runs with no person, keeps the UTC day. `renew_approval_request` says how long the new request runs
   ("The new request expires in 2 days."), never its UTC date; the request in data keeps the moment. The demo director
   sends the operator's day (`X-Product-Local-Day`), as the web app does.
5. **A refusal can point to a record** (`error.recordUrl`). `record_document_link` refuses a second link for a
   requirement with "Update that link instead: ..." and the link record's URL; the web app shows "Open that record".
   An AI client also gets the link's ID in the words. Reason: the sentence showed a field name and a GUID to a person,
   with no way to open the link it named.
6. **The AI activity's parts add up** (`get_ai_activity`). A data use, location or mapping withdrawn as a draft is
   withdrawn, not decided (its status is archived, as a retired one's; the withdraw call in its history tells them
   apart). A draft taken forward and later withdrawn counts as withdrawn. The new `counts.recorded` counts the rows an AI
   client reported as facts (a link, a deployment, a claim, a run), so waiting, withdrawn, taken forward, submitted by the
   AI client and recorded add up to `drafts`. Under `businessApplicationId` or `portfolioId`, `clients[].calls` counts
   only the calls that named a record of that scope (review).
7. **Marking a registered application as holding no business data changes nothing else** (`save_application_draft`):
   components, proposed owners and an intake link are refused there, and the answer says "<name> is marked as holding no
   business data. It stays registered."
8. **A withdrawn draft mapping ends today**, so it frees its place in the store's key (O26: domain, location and origin
   among the rows with no Ended On), and the move check reads the same rows as that key: a withdrawn draft from before
   this fix, with no end date, still refuses the move, with the reason.
9. **The demo inventory pages by 25 at most on every install** (tool 35's demo reader), so a demo install shows
   "Continue the run" as a local run does.
10. **Settings added after the first release are optional with their default** (V38; document 21, section 3.6):
    `review-request-due-days`, `measure-targets` and `signal-thresholds`. A set seeded before them has no row, and every
    draft on it would fail V38 until it added the row. A row that is there keeps its value-type check. They stay optional
    until the product upgrade draft of document 21, section 4.9 adds them to an installed set.
11. **Words.** A portfolio retired or brought back says so ("is retired", "is back in service"); a changed deployment
    target no longer says deployments "can now be recorded"; a dismissed claim says "Reopen brings it back"; DC-09 names
    the question and the answers by their labels.
12. **Tests:** `WaveFourBFixTests` (MCP core: one passing and one refusing case per item), `DataMappingKeyHostTests`
    (host, on SQLite, which holds the key O26: a withdrawn draft frees its place for a move; an open twin refuses it
    as a validation error, never a 500), the fixture `v38-review-request-due-days-absent-valid`, and the web app's logic tests for the confirm
    and end inputs.

### B24-25 Wave 4b, fix round 2: what the verify round found (built 2026-11-26, D-41)

The second fix round of wave 4b, after its first verify round. Each choice is the AI sessions' under D-41, with the
reason. No new tool and no contract change.

1. **A withdrawn draft location frees its External Id** (`withdraw_record`, then `save_data_location`). Adding the
   platform and External Id of a withdrawn draft again passed the check and then answered HTTP 500, because the store's
   key of O25 still counted the archived row. The store's key now counts the locations in service (no Retired On, not
   archived), as the engine's match and reads do; Azure SQL migration `0009-location-key-in-service.sql` removes the key
   rows that earlier builds kept, and the host needs schema version 9. The withdrawn draft
   keeps no Retired On, so it still reads Withdrawn. Reason: the check and the write must agree, and a draft taken back
   never entered service.
2. **A configuration change names a role group binding by its role, and says "Keep" for a row proposed as it is**
   (`ChangeWords`: the draft's lines, its decision statement, the approval card). "Change the role group binding reader
   group." now reads "Keep the group binding of Reader as it is."; a changed field reads "The binding detail of the
   group binding of Reader changes from ... to ...". Reason: the run-on sentence named the row by its lowercase label,
   and said Change for a draft that changed nothing.
3. **Words in the web app.** A proposal whose decider is the person proposing says "You are the decider.", never their
   own name as if someone else decided. Ask again states the new expiry once: the engine's summary gives the days and
   the Asked now block gives the local date and time. A store inside an instance shows its instance's days on the stale
   badge ("Its instance not seen for N days"), as the engine's flag reads the instance, so confirming the store alone
   never leaves "Not seen for 0 days". A DC-13 sentence that two uses share is said once on the retirement tracker and
   on Application 360. An owner row on Application 360 offers "Change the leave date" for a person with a leave on
   record, and the Owners button's name matches its words. A withdrawn or retired location shows no gap badge, and the
   Locations tab marks a withdrawn one Withdrawn. The proposal sheets and the leave sheet start from the page's values
   each time they open: the tier decision sheet, kept mounted while the tier was still being read, opened once with an
   empty statement on a phone (S-23.12). An ended configuration draft gives its reason with one full stop.
4. **The coverage check names a site written as an expression** (`tools/check-actions.mjs`): "the sheet's site is not
   a string literal (site={where})", not "missing".
5. **Tests:** `DataLocationKeyHostTests` (host, on SQLite: a withdrawn draft frees its External Id in the check and the
   write; a location in service is matched again, never a second row), `StoreKeyTests` and `StoreConformanceTests` (the
   scope, the commit that frees the value, and a SQLite file from version 3), `SqlScriptTests` (0009),
   `WaveFourBFixTwoTests` (MCP core: the binding words, passing and changed), and the web app's logic tests.

