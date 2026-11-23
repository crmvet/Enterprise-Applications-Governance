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
2. **AI never approves** (D-02, D-10). No tool approves, rejects, accepts, authorizes, sets a tier, moves a stage, activates configuration, assigns a role or deletes. The engine refuses those writes from an AI client whatever the tool.
3. **AI-created rows start as Draft or Proposed.** Facts an AI client reports (a deployment, a document status) are stored "as reported", with the actor kind.
4. **Everything is configuration** (D-06, D-07, D-10). Which role may call which tool, the highest state an AI client may set, page sizes and depth limits are configuration rows (section 2.3). No tool branches on a fixed tier number or label.
5. **Roles come only through Entra groups** (D-11). The server reads product roles from group-based bindings. It never assigns a role. Non-human workloads (pipelines, the approval runner) hold their workload role through a recorded, expiring Workload Grant (C42) instead (working default; open design question 12, see 28-working-defaults.md). They do not use MCP.
6. **Links, not copies** (D-15, D-17, D-19). Tools store an ID, a URL, a label and a "last seen" date for anything another tool owns. Entra rosters and Microsoft's inventory are read live and never stored.
7. **Stateless.** No session state lives in the server between calls. Any instance can answer any request (section 5).
8. **No gateway holds a shared credential.** A gateway in front of the server passes the caller's own token through [T15].

---

## 1. In one screen

1. **51 tools in 10 toolsets:** session, portfolio, intake, decisions, change, deployment, documentation, gap check, configuration and links. Six of them (tools 46 to 51) cover process steps that had no tool (28, issue 11). A caller sees only the tools its product roles allow, and can narrow the list further to the toolsets it needs.
2. **Reads answer the questions people ask:** "who owns this app?" (`get_application_owners`), "what breaks if it retires?" (`get_dependency_impact`), "what does my tier need?" (`get_requirements`), "what needs me?" (`get_my_queue`), "what documentation is missing or stale for my app?" (`get_documentation_status`).
3. **Writes leave records in Draft, Proposed, Submitted (a request, never an approval), as reported, as a link, or as a candidate configuration draft.** Section 6.0 defines each state.
4. **Approvals happen only on approval cards.** `submit_record` starts the cards. The approval runner (the approval flows) writes the outcome under the approval writer workload role (28, issue 7). Only a person counts.
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
| `portfolio` | Portfolio questions: owners, card, impact, summary, requirements | 6 | All |
| `intake` | The front door: duplicates, tier questions, intake drafts, registering an app already built, components, owner changes and leaves | 8 | Business, makers, AI, PMO |
| `decisions` | Decision proposals, submitting and withdrawing records, the decision log, approval status, architecture review requests | 6 | All |
| `change` | Change request drafts, the standard change check, the schedule, change results | 5 | Change control, owners |
| `deployment` | Deployment records and corrections, what runs where, the pipeline map, the later gate check | 6 | IT, owners, change control |
| `documentation` | Required documents, document links and status | 3 | Owners, architects |
| `gapcheck` | The live gap check, the owner check and the claim list | 6 | IT (admins), owners |
| `configuration` | Reading configuration, rule traces, configuration health, proposing changes, presets, role binding checks | 7 | Configuration admin, platform admin, auditor |
| `links` | Delivery and evidence links | 1 | All |

### 2.2 What each default role sees

Default product roles come from document 21's seed list, agreed with document 26: 14 human roles (28, issue 7). "R" means the read tools of the toolset, "W" means its write tools. Owner roles (C28) narrow writes further to the applications a person owns. The table is the shipped default of the Tool Policy rows (section 2.3); a client changes it by configuration. It summarizes by toolset. The exact default for each tool is the "Default roles" column in section 6.1.

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
- **Rows are filtered by the read visibility settings** (28, issue 10). `intake-visibility` defaults to "own": the requester, plus PMO, platform admin and architect. `claim-visibility` defaults to "admins and claimant". `record-access-mode` defaults to open read, with portfolio read available.
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

### 3.5 Errors

Errors return `ok: false` and an `error` object: `{ code, message, field?, ruleKey?, guideline? }`. `ruleKey` names the configuration row that refused the write; `guideline` names the guideline it applies, such as G17. The codes are fixed:

| Code | When |
|---|---|
| `not_signed_in` | No valid token. The HTTP layer returns 401 with the protected resource metadata link [T2] |
| `not_authorized` | Valid token, but the caller lacks the role, the owner role, or the row access. HTTP 403 for a missing scope [T2] |
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

**Default roles** use the role keys of C29, from document 21's seed of 14 human roles (28, issue 7): `requester`, `maker`, `application-owner`, `data-owner`, `architect`, `security-reviewer`, `change-manager`, `change-board`, `pmo`, `ai-lead`, `platform-admin`, `configuration-admin`, `auditor`, `reader`. The workload roles `deployment-recorder` and `approval-writer` call no MCP tool. "Owner of the app" means an active owner role (C28) on that application through an accepted ownership decision (O06). "Any role" means the caller holds at least one product role. Rows are always filtered by the caller's own access and by the read visibility settings (section 2.2).

### 6.1 All tools at a glance

Tools 46 to 51 were added in the alignment pass for process steps that had no tool (28, issue 11). Each sits in its toolset. Numbers stay stable, so earlier references still hold.

| # | Tool | Toolset | Question it answers | Writes | AI write state | Default roles | Release |
|---|---|---|---|---|---|---|---|
| 1 | `whoami` | session | Who am I here, and what may I do? | None | Read only | Anyone signed in | R1 |
| 2 | `get_my_queue` | session | What needs me? | None | Read only | Any role | R1 |
| 3 | `get_role_holders` | session | Who is on the change board? | None | Read only | Any role | R1 |
| 4 | `find_applications` | portfolio | Which apps match? | None | Read only | Any role | R1 |
| 5 | `get_application_owners` | portfolio | Who owns this app? | None | Read only | Any role | R1 |
| 6 | `get_application_card` | portfolio | Everything about this app, in one card | None | Read only | Any role | R1 |
| 7 | `get_dependency_impact` | portfolio | What breaks if it retires? | None | Read only | Any role | R1 |
| 8 | `get_portfolio_summary` | portfolio | How does the portfolio look, and which apps lack an owner? | None | Read only | Any role | R1 |
| 9 | `get_requirements` | portfolio | What does my tier need? | None | Read only | Any role | R1 |
| 10 | `check_duplicates` | intake | Does this already exist? | None | Read only | Any role | R1 |
| 11 | `get_tier_questions` | intake | Which questions set the tier? | None | Read only | Any role | R1 |
| 12 | `save_intake_draft` | intake | Start or edit an intake request | O07 | Draft | requester, maker, application-owner, pmo, ai-lead, platform-admin | R1 |
| 13 | `answer_tier_questions` | intake | Which tier do my answers suggest? | O08 | Draft (an assessment, not a decision) | Requester of the intake, owner of the app | R1 |
| 14 | `save_application_draft` | intake | Register an app already built | O02, O03, O06, O09 | Draft and Proposed | requester, maker, application-owner, ai-lead, platform-admin | R1 |
| 15 | `link_component` | intake | This app, function or agent belongs to my app; it moved; it is gone | O03, O23 | Link (retired as reported) | Owner of the app, platform-admin | R1 |
| 16 | `propose_owner_change` | intake | Hand over or add an owner | O09, O06 | Proposed | Owner of the app, ai-lead (agents), platform-admin | R1 |
| 46 | `record_owner_leave` | intake | This owner leaves on this date | O05, O21 | As reported | The leaving person (any role), an owner of an app the person owns, platform-admin | R1 |
| 17 | `save_decision_proposal` | decisions | Record a decision for acceptance | O09, O20 | Proposed | Holders of R or C for the decision type (C31) | R1 |
| 18 | `submit_record` | decisions | Send it for approval | O07, O11, O09, O10, Configuration Draft | Submitted | The record's requester or creator | R1 |
| 19 | `withdraw_record` | decisions | Take it back | O07, O11, O09, O02, Configuration Draft | Withdrawn | The record's requester or creator | R1 |
| 20 | `get_decision_log` | decisions | What was decided, by whom, and why? | None | Read only | Any role | R1 |
| 21 | `get_approval_status` | decisions | Who still has to approve? | None | Read only | Any role | R1 |
| 47 | `request_architecture_review` | decisions | Please review this design | O21, O20 | Submitted (a request, never a decision) | Owner of the app, maker, architect, change-manager, platform-admin | R1 |
| 22 | `save_change_request_draft` | change | Start or edit a change request | O11, O12, O18 | Draft | Owner of the app, maker, change-manager | R1 |
| 23 | `check_standard_change` | change | Does this change fit a pre-approved model? | None | Read only | Any role | R1 |
| 24 | `get_change_request` | change | Where is this change? | None | Read only | Any role | R1 |
| 25 | `get_change_schedule` | change | What is planned, where, when? | None | Read only | Any role | R1 |
| 26 | `record_change_result` | change | How did the change end? | O11, O20, O21 | As reported | Owner of the app, change-manager | R1 |
| 27 | `record_deployment` | deployment | This version reached this target | O13, O14 | As reported | Owner of the app, maker, change-manager | R1 |
| 48 | `record_deployment_correction` | deployment | That deployment belongs to this change, or a fact was wrong | O14 | As reported | Owner of the app, maker, change-manager, platform-admin | R1 |
| 28 | `get_deployments` | deployment | What runs where? | None | Read only | Any role | R1 |
| 29 | `map_pipeline` | deployment | This pipeline deploys this app to these targets | O16, O17 | Link | Owner of the app (technical), platform-admin | R1 |
| 30 | `get_pipeline_map` | deployment | Which pipelines deploy this app? | None | Read only | Any role | R1 |
| 31 | `check_change_approval` | deployment | Does this change have the approvals its policy requires? | L05 | Read only (logs the question) | Any role (preview); `GateCheck.Ask` app role on the record endpoint | Later (D-03) |
| 32 | `get_documentation_status` | documentation | What documentation is missing or stale for my app? | None | Read only | Any role | R1 |
| 33 | `record_document_link` | documentation | Here is the page, and its status | O19 | Link, status as reported | Owner of the app, document owner | R1 |
| 34 | `confirm_document_checked` | documentation | Is this document still true? | O19 | As reported | Owner of the app, document owner | R1 |
| 35 | `run_gap_check` | gapcheck | What is running that is not in the register? | O22, O23, O03 | As reported (claims are open items) | platform-admin, plus an inventory Entra role | R1 |
| 36 | `compare_inventory_list` | gapcheck | Compare this list I fetched myself | O22, O23 | As reported | platform-admin | R1 |
| 49 | `run_owner_check` | gapcheck | Which owners are disabled, deleted or leaving? | O22, O05, O21 | As reported | platform-admin | R1 |
| 37 | `list_claims` | gapcheck | What is waiting to be claimed? | None | Read only | maker, application-owner, ai-lead, pmo, platform-admin, auditor | R1 |
| 38 | `claim_resource` | gapcheck | That one is mine | O23, O03, O02, O09 | Link, Draft and Proposed | maker, application-owner, ai-lead, platform-admin | R1 |
| 39 | `dismiss_claim` | gapcheck | That one is not a business application | O23 | As reported (reversible) | platform-admin | R1 |
| 40 | `get_configuration` | configuration | What rules are in force, and what changed? | None | Read only | Any role | R1 |
| 50 | `explain_rules` | configuration | Which rule applied to this record, and why? | None | Read only | Any role | R1 |
| 51 | `get_configuration_health` | configuration | Which versions exist, which drafts wait, and does the configuration load? | None | Read only | Any role | R1 |
| 41 | `list_presets` | configuration | Which ready-made configurations exist? | None | Read only | Any role | R1 |
| 42 | `propose_configuration_change` | configuration | Change the rules the way the admin said | Configuration Draft | Candidate | configuration-admin | R1 |
| 43 | `export_configuration` | configuration | Give me the configuration as files | None | Read only | configuration-admin, platform-admin, auditor | R1 |
| 44 | `check_role_bindings` | configuration | Is every role held only through its group, or a recorded workload grant? | O24 | Read only (logs the check) | platform-admin | R1 |
| 45 | `save_link` | links | Link this work item or this evidence | O18, O20 | Link | Owner of the subject's app, change-manager, pmo; evidence on records the caller can write | R1 |

### 6.2 Session toolset

#### 1. `whoami`

- **Purpose:** tells the AI client who the person is in the product, which roles they hold and which tools they may use. A person with no product role also gets an answer, so they learn what to ask for.
- **In:** `{}`
- **Out:** `{ principal: { objectId: guid, displayLabel, principalKind }, productRoles: [{ roleKey, label, viaGroup: { objectId, displayLabel } }], actorKind: "ai-client-for-person", clientApp: { appId, label }, configurationSet: { key, version, publishedOn }, toolsets: [toolsetKey], tools: [toolKey] }`
- **Reads:** token claims, C29 Role, C30 Role Group Binding, O05 Principal Reference, C02 Configuration Set Version, proposed C41 Tool Policy. **Writes:** none.

#### 2. `get_my_queue`

- **Purpose:** "What needs me?" Approvals waiting for the person, reviews due on apps they own, documents missing or stale, claims suggested to them, their drafts not yet submitted, and owner roles with no holder or deputy on their apps.
- **In:** `{ kinds?: enum[approval|review|document|claim|draft|owner-gap][], top?, skipToken? }`
- **Out:** `{ items: [{ kind, subjectKind, subjectId, title, applicationId, dueOn, status, why, recordUrl, nextTool }] }`. Approvals point to the approval card; the tool never offers to answer it.
- **Reads:** O06, O07, O09, O10, O11, O19, O21, O23, C26 Reminder Rule. **Writes:** none.

#### 3. `get_role_holders`

- **Purpose:** "Who is on the change board?" Reads the members of the role's bound Entra groups live, as the person. Never stored (doc 20, section 2.8).
- **In:** `{ roleKey: key(C29) }`
- **Out:** `{ role: { key, label }, groups: [{ objectId, displayLabel }], members: [{ objectId, displayLabel }], readFrom: "Entra, live", note }`. On the Dataverse binding the note says group team changes can take up to 8 hours to reach Dataverse (doc 20, S7).
- **Reads:** C29, C30, and Microsoft Graph group members through an on-behalf-of token. Graph decides what the person may see. **Writes:** none. **Route:** A only.

### 6.3 Portfolio toolset

#### 4. `find_applications`

- **Purpose:** finds business applications by text and filters.
- **In:** `{ text?: text(200), portfolioId?: id(O01), applicationKindKeys?: key(C15)[], tierKeys?: key(C08)[], lifecycleStageKeys?: key(C12)[], ownerObjectId?: guid, registrationMeanings?: enum[draft|registered|archived][] = [registered], top?, skipToken? }`
- **Out:** `{ items: [{ applicationId, name, purpose, kind, tier, stage, businessOwner, technicalOwner, recordUrl }], totalCount }`. `purpose` is an untrusted field.
- **Reads:** O02 Business Application, O01 Portfolio, O06 Owner Assignment, O05. **Writes:** none.

#### 5. `get_application_owners`

- **Purpose:** "Who owns this app?" A small, fast answer: every owner role, holder and deputy, and the gaps.
- **In:** `{ applicationId: id(O02), asOf?: date, includeDeputies?: bool = true }`
- **Out:** `{ owners: [{ ownerRoleKey, label, principal, isDeputy, deputyFor, startOn, endOn, ownershipDecisionId }], gaps: [{ ownerRoleKey, missing: enum[holder|deputy] }], microsoftOwnerDiffers: bool, ownerCheckedOn }`. The Microsoft owner itself is never returned or stored (doc 20, O03 Owner Differs).
- **Reads:** O06, O05, C28 Owner Role, O03 Technical Component. **Writes:** none.

#### 6. `get_application_card`

- **Purpose:** the whole application in one call, so an AI client can answer follow-up questions without ten more calls.
- **In:** `{ applicationId: id(O02), sections?: enum[owners|components|dependencies|decisions|changes|deployments|documents|reviews|links][] = all }`
- **Out:** `{ application: { id, name, purpose, kind, tier: { key, label, decisionId }, stage: { key, label, since, decisionId }, buildPath, usesAi, agreedAutonomyLevel, nextReviewOn }, owners, components: [{ kind, externalId, url, labelAsSeen, lastSeenOn }], dependencies: { dependsOnCount, usedByCount, top: [...] }, decisions: latest 5, changes: open, deployments: latest per target, documents: status per required type, reviews: due and last, links }`
- **Reads:** O02, O03, O04, O06, O09, O11, O14, O18, O19, O21. **Writes:** none.

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
- **Out:** `{ gates: [{ gateKey, requirements: [{ requirementKind, decisionTypeKey | documentTypeKey | reviewTypeKey, guideline, met? }] }], approvals: [{ decisionTypeKey, approverRoleKey | approverOwnerRoleKey, approvalMode, minimumCount }], documents: [{ documentTypeKey, fromStage }], reviews: [{ reviewTypeKey, intervalDays }], gatePolicy: [{ targetStageKey, changeTypeKey, gateLevel, failMode }] }`
- **Reads:** C08, C12, C13, C14, C20, C23, C25, C31, C32, C33, C39, and for an application O09, O19, O21. **Writes:** none.

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
- **In:** `{ intakeRequestId?: id(O07) (edit an existing draft), title: text(200), need: text(4000), proposedKindKey?: key(C15), businessApplicationId?: id(O02), expectedBenefit?: text(2000), usesAi?: bool, horizonKey?: key(C07), clientRequestId?, validateOnly? }`
- **Out:** the write envelope, with status Draft and `nextSteps` pointing to `answer_tier_questions` and `submit_record`.
- **Reads:** O02 (duplicate check), C15. **Writes:** O07 Intake Request (status meaning draft). Only the requester may edit, and only while it is draft.

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
- **Reads:** O06, C28, C32. **Writes:** O09 (ownership, Proposed), O06 (pending until accepted).
- **Example (sweep proposal):** the AI lead uses this tool, or `save_decision_proposal`, to record "reassign" for an agent Microsoft flags without an owner, with a link to the Agent 365 evidence.

#### 46. `record_owner_leave`

- **Purpose:** records a planned leave or role change for a person who holds owner roles, with a date (P08, announced trigger; 28, issue 11). The engine opens an owner check review for each application where the person holds an owner role. From the leave date, that role's reminders and cards go to the deputy. A successor still needs an accepted ownership decision (`propose_owner_change`).
- **In:** `{ principalObjectId: guid, leavesOn: date, kind: enum[leave|role-change], note?: text(1000), clientRequestId?, validateOnly? }`
- **Out:** the write envelope, plus `{ affected: [{ applicationId, ownerRoleKey, hasDeputy }], ownerCheckReviewIds: [id(O21)], pendingApprovals: int }`, with `nextSteps` pointing to `propose_owner_change`.
- **Reads:** O05, O06, C28, O10. **Writes:** O05 Leaves On (as reported); O21 Review of type owner check (the engine creates them). The engine moves pending approvals on the leave date (document 23, P08 step 3).
- **Default roles:** the leaving person, whatever their role; an owner of an application the person owns; platform-admin.
- **Optional sample:** where a client has Entra ID Governance, a Lifecycle Workflows leaver task can call the same contract through the API (28, Q18). The sample's setup names its workload identity and its Workload Grant (C42). The sample is not part of the core.

### 6.5 Decisions toolset

#### 17. `save_decision_proposal`

- **Purpose:** records a decision for acceptance: what, why, the options weighed, the outcome proposed. The decider is resolved from the decision rights (the accountable holder in C31) and cannot be set by the caller. Configuration and gate policy decisions use `propose_configuration_change` instead.
- **In:** `{ decisionId?: id(O09) (edit while Proposed), decisionTypeKey: key(C18), subjectKind: enum[business-application|intake-request|change-request|deployment-record|technical-component|claim|review], subjectId: id, statement: text(500), context: text(4000), optionsConsidered?: [{ option: text(500), whyNot: text(1000) }], proposedOutcomeKey: key(C19), reason: text(2000), confidenceKey?: key(C07), supersedesDecisionId?: id(O09), amendsDecisionId?: id(O09), evidence?: [{ linkKindKey: key(C37), url: url, label: text(200) }], clientRequestId?, validateOnly? }`
- **Out:** the write envelope with status Proposed, `decider`, and `approvalsNeeded` resolved from C32.
- **Reads:** C18, C19, C31, C32. **Writes:** O09 Decision (Proposed), O20 Evidence Link.

#### 18. `submit_record`

- **Purpose:** sends a draft or proposed record on: an intake request to triage, a change request to authorization, a decision to acceptance, a configuration draft to acceptance. For a configuration draft the engine freezes the draft, records a configuration change decision (or a gate policy change decision when the draft touches a Gate Policy row) in Proposed, and starts the card (document 21, section 5.1). It starts the approval cards the rules require. It never approves.
- **Cards.** Every decision goes on at least one card, to the holder of its accountable letter (C31), even when its type has no approval rule (28, issue 1). The engine accepts without a card only for a locked list: lowest-tier intake and standard changes whose model a person approved once (working default; open design question 8, see 28-working-defaults.md). If a standard change model applies, the engine applies it and says so.
- **In:** `{ subjectKind: enum[intake-request|change-request|decision|configuration-draft], subjectId: id, note?: text(1000) }`
- **Out:** `{ status: submitted, decisionId?, approvalsRequested: [{ approvalRecordId, requiredRoleKey, approvalMode, expiresOn, channel }], standardChangeApplied?: { modelKey, approvedByDecisionId } }`
- **Reads:** C20, C21, C32, C33, C40. **Writes:** the status of O07, O11, the decision's submission or the Configuration Draft (frozen); O09 for a configuration draft (Proposed); O10 Approval Record rows with outcome pending (the engine creates them); the approval runner starts. Allowed for an AI client only where the Tool Policy row says AI May Submit = yes for that subject kind (working default; open design question 9, see 28-working-defaults.md).

#### 19. `withdraw_record`

- **Purpose:** takes back a record the caller created. Cancels its pending approval cards. Accepted decisions are never withdrawn; a new decision supersedes them (doc 20, rule 6).
- **In:** `{ subjectKind: enum[intake-request|change-request|decision|application-draft|configuration-draft], subjectId: id, reason: text(1000) }`
- **Out:** the write envelope with status withdrawn, and `cancelledApprovals`.
- **Reads:** O10. **Writes:** status of O07, O11, O09 or O02 (draft only), or of a Configuration Draft (discarded, document 21, section 4.2; 28, issue 11). Withdrawing a submitted configuration draft also withdraws its Proposed decision. The engine writes the cancelled outcome on O10 and the approval runner cancels the card (document 22, section 8.2; document 23, S1).

#### 20. `get_decision_log`

- **Purpose:** what was decided, by whom, when and why, with the supersede and amend chain.
- **In:** `{ subjectKind?, subjectId?, applicationId?: id(O02), decisionTypeKeys?: key(C18)[], statusMeanings?: enum[proposed|accepted|rejected|superseded|withdrawn][], from?: date, to?: date, top?, skipToken? }`
- **Out:** `{ items: [{ decisionId, type, statement, outcome, status, decider, decidedOn, supersedes, supersededBy, amends, approvalsSummary, configurationSetVersion, recordUrl }] }`
- **Reads:** O09, O10, C18, C19, C05 Status. **Writes:** none.

#### 21. `get_approval_status`

- **Purpose:** who was asked, who answered, what still counts and what is missing. Shows the approver kind (sweep proposal), so an AI recommendation is visible but never counted.
- **In:** `{ subjectKind: enum[decision|change-request|intake-request], subjectId: id }`
- **Out:** `{ required: [{ approvalRuleKey, requiredRoleKey, approvalMode, minimumCount, received }], records: [{ approvalRecordId, approver, approverActorKind, approverPrincipalKind, outcome, typedReason, respondedOn, countsTowardRequirement, refusedBySeparationRuleKey? }], missing: [...], ready: bool }`. `typedReason` is an untrusted field. An answer does not count when it is not a person's, or when a Separation Rule (C40) refuses it, for example self-approval at tier 3 in Balanced (28, issue 5 and Q10). A consulted role's answer (Is Consultation on C32) shows as a recommendation and never counts (28, issue 12).
- **Reads:** O10, C32, C40, O09, C38 Actor Kind. **Writes:** none.

#### 47. `request_architecture_review`

- **Purpose:** asks for an ad hoc architecture review of an application or a change (P04 step 1, the "request" trigger; 28, issue 11). The engine creates an O21 Review of type architecture, status due, and the review appears in the architect's queue. A request, never a decision: the review ends in a design decision that people accept on cards. If an open architecture review already exists for the subject, the tool returns it and writes nothing new.
- **In:** `{ applicationId: id(O02), changeRequestId?: id(O11), reason: text(2000), dueOn?: date (= today), evidence?: [{ linkKindKey: key(C37), url: url, label: text(200) }], clientRequestId?, validateOnly? }`
- **Out:** the write envelope with status due, plus `{ reviewId, reviewTypeKey: "architecture", trigger: "on request", assignedRoleKey, dueOn }`, with `nextSteps` pointing to `save_decision_proposal` (decision type design) and `record_document_link` (the ADR).
- **Reads:** C24 Review Type, O02, O11, O21. **Writes:** O21 Review (status due, with its trigger; the trigger column is added to document 22 under 28, issue 15); O20 Evidence Link. `reason` is an untrusted field.
- **AI submission:** an AI client may send the request where the Tool Policy row says AI May Submit = yes for subject kind review request (shipped yes; working default; open design question 9, see 28-working-defaults.md).

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
- **Out:** `{ change, authorization: { decisionId, status, approvalsSummary, linkedSource?: enum[itsm-step|attestation|none] }, affected, deployments: [{ deploymentRecordId, target, result, flags }], result, links }`. In linked mode `linkedSource` says how the authorization was reported; "none" means not authorized (document 25, OP-14; 28, Q22).
- **Reads:** O11, O12, O09, O10, O14, O18, O20. **Writes:** none.

#### 25. `get_change_schedule`

- **Purpose:** the read-only list of planned changes by window and target (D-13). No booking and no freeze tables.
- **In:** `{ from: date, to: date, targetIds?: id(O15)[], targetStageKeys?: key(C34)[], applicationId?: id(O02), statusMeanings?: enum[draft|submitted|in-progress|completed|rejected|withdrawn|cancelled][] }`
- **Out:** `{ items: [{ changeRequestId, title, application, target, windowStart, windowEnd, changeType, status, authorized: bool }] }` sorted by window start.
- **Reads:** O11, O15, O02. **Writes:** none.

#### 26. `record_change_result`

- **Purpose:** records how a change ended. The engine creates a post-change review when the change type requires one or the result failed (doc 20, C20).
- **In:** `{ changeRequestId: id(O11), resultKey: key(C07), note?: text(2000), evidence?: [{ linkKindKey: key(C37), url: url, label: text(200) }] }`
- **Out:** the write envelope, and `postChangeReviewId` when one was created.
- **Reads:** C20. **Writes:** O11 Result (as reported), O20, O21 Review (engine).

### 6.7 Deployment toolset

#### 27. `record_deployment`

- **Purpose:** records that an artifact version reached a target, when the owner's AI tool reports it (D-19). The same contract is the record endpoint for pipeline steps (section 4.6). The engine sets the route and computes the flags; the caller cannot set them.
- **Reported code approvals** (28, issue 11). The caller may report who approved the code merge. They are stored as evidence (O14 Reported Approvals Json) and never count toward the product's approval rules. AI-only code approvals never count as authorization, and the shipped gate default flags them (working default; open design question 7, see 28-working-defaults.md).
- **In:** `{ idempotencyKey: text(200) (required), applicationId?: id(O02), artifactName: text(200), version: text(100), artifactKindKey?: key(C07), digest?: text(128), sourceCommit?: text(64), sourceRepositoryUrl?: url, one of targetId: id(O15), targetExternalId: text(400); changeRequestId?: id(O11), pipelineReferenceId?: id(O16), runUrl?: url, startedOn?: datetime, finishedOn: datetime, resultMeaning: enum[succeeded|partly-succeeded|failed|not-deployed], versionBefore?: text(100), versionAfter?: text(100), deployedByObjectId?: guid (= the caller), mergeActorObjectId?: guid (sweep proposal), stageOrderRun?: text(100)[] (sweep proposal), codeApprovalKind?: enum[person|ai-only|none|unknown] = unknown, reportedApprovals?: [{ approverObjectId?: guid, approverKind: enum[person|ai|unknown], approvedOn?: datetime, sourceUrl?: url }] }`
- **Out:** `{ deploymentRecordId, artifactVersionId, created, route: { key, meaning: "ai-tool" }, flags: { outsideWindow, otherPath, missingApproval, aiOnlyCodeApproval }, gateLevelApplied }`
- **Reads:** O11, O15, C33, C35 Change Path, C36 Deployment Route. **Writes:** O13 Artifact Version (upsert by name plus version), O14 Deployment Record (upsert by idempotency key, I-189, with Reported Approvals Json).

#### 48. `record_deployment_correction`

- **Purpose:** corrects a deployment record without editing it (P05 step 7; 28, issue 11). A deployment record is final at insert (document 22), so linking it to its change after the fact, or fixing a wrong fact, adds a new record whose Corrects Record names the old one. The old record stays; views show the latest correction. The engine computes the flags for the new record; the caller cannot set them.
- **In:** `{ correctsDeploymentRecordId: id(O14), idempotencyKey: text(200) (required), reason: text(1000), changeRequestId?: id(O11), resultMeaning?: enum[succeeded|partly-succeeded|failed|not-deployed], version?: text(100), finishedOn?: datetime, runUrl?: url }`. Fields left out are copied from the corrected record.
- **Out:** `{ deploymentRecordId, correctsRecordId, created, flags: { outsideWindow, otherPath, missingApproval, aiOnlyCodeApproval }, gateLevelApplied }`
- **Reads:** O14, O11, O15, C33, C35, C36. **Writes:** O14 Deployment Record (a new row with Corrects Record set; upsert by idempotency key). `reason` is an untrusted field.
- **Pipelines** send corrections to the record endpoint with the same contract (section 9.2).

#### 28. `get_deployments`

- **Purpose:** "What runs where?" The latest version per target, or the history, with flags.
- **In:** `{ applicationId?: id(O02), targetId?: id(O15), latestOnly?: bool = true, flaggedOnly?: bool = false, from?: date, to?: date, top?, skipToken? }`
- **Out:** `{ items: [{ deploymentRecordId, application, target, targetStage, artifactName, version, result, finishedOn, route, flags, runUrl }] }`
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

#### 31. `check_change_approval` (Later, after the gate decision D-03)

- **Purpose:** answers the check step's question: "does this change have the approvals its policy requires?" The pipeline acts on the answer at Advise or Enforce. The product never stops anything itself.
- **In:** `{ changeRequestId?: id(O11), applicationId: id(O02), one of targetId: id(O15), targetExternalId: text(400); artifactName?: text(200), version?: text(100), emergency?: bool = false }`
- **Out:** `{ gateLevel, approved: bool, missing: [...], failMode, emergencyPath, message }`
- **Reads:** O11, O10, C33. **Writes:** L05 Gate Check (logs the question and the answer).

### 6.8 Documentation toolset

#### 32. `get_documentation_status`

- **Purpose:** "What documentation is missing or stale for my app?" Required documents come from configuration (D-17). "Missing" is computed from configuration. A Document Link row exists only once a link has been recorded (28, issue 16).
- **In:** `{ one of applicationId: id(O02), mine: true; statusMeanings?: enum[missing|draft|current|stale][] }`
- **Out:** `{ applications: [{ applicationId, required: [{ documentTypeKey, fromStage, requirementKey }], documents: [{ documentLinkId, documentTypeKey, url, status, owner, lastUpdatedOn, lastCheckedOn, staleOn }], missing: [documentTypeKey], stale: [documentLinkId] }] }`
- **Reads:** C22 Document Type, C23 Documentation Requirement, O19 Document Link, O02. **Writes:** none.

#### 33. `record_document_link`

- **Purpose:** records the link and status of a document the owner's own AI tool wrote, for example a wiki page written through the Azure DevOps MCP server. The product never writes the document (D-17) and never assumes the owner's write succeeded (sweep proposal): it stores what the caller reports.
- **In:** `{ documentLinkId?: id(O19), applicationId: id(O02), documentTypeKey: key(C22), url: url, statusMeaning?: enum[draft|current] = draft, lastUpdatedOn?: date, documentOwnerObjectId?: guid (= the caller), linkKindKey?: key(C37) }`
- **Out:** the write envelope with `staleOn` computed from the document type's freshness.
- **Reads:** C22, C23, C37. **Writes:** O19 (link, status as reported). The first recorded link creates the row (28, issue 16). How far an AI client may set the status is the Tool Policy's Max AI State. The shipped default allows "current" when the caller owns the document or the app. For a document type with Current Needs Person = yes (C22), a "current" from an AI client is stored as draft until the person confirms on a confirmation card (working default; open design question 9, see 28-working-defaults.md).

#### 34. `confirm_document_checked`

- **Purpose:** the owner confirms a document is still true, or marks it stale.
- **In:** `{ documentLinkId: id(O19), stillTrue: bool, note?: text(1000) }`
- **Out:** the write envelope with the new status and `staleOn`.
- **Reads:** C22. **Writes:** O19 Last Checked On and status. From an AI client, "still true" on a document type with Current Needs Person = yes follows the same confirmation card rule as tool 33 (working default; open design question 9, see 28-working-defaults.md).

### 6.9 Gap check toolset

#### 35. `run_gap_check`

- **Purpose:** "What is running that is not in the register?" Runs live, as the signed-in admin (D-15). The server exchanges the admin's token for a Power Platform API token and queries the inventory. Only unregistered items become claims. Registered components the check no longer sees become "vanished" claims (sweep proposal). The engine compares the result with the register and runs the claim lifecycle (document 25, OP-13; 28, issue 12).
- **In:** `{ gapCheckRunId?: id(O22) (continue a run), estateKeys?: key(C07)[] = all covered estates, environmentIds?: text(100)[], includeVanished?: bool = true, compareOnly?: bool = false (return the result, write no claims), skipToken? }`
- **Out:** `{ gapCheckRunId, adminRoleUsed, estatesCovered, itemsSeen, newClaims: [{ claimId, componentKind, labelAsSeen, targetAsSeen, suggestedClaimant }], vanished: [{ claimId, technicalComponentId }], lastSeenUpdated, more: bool, skipToken }`
- **Reads:** the Power Platform inventory, live through `POST {PowerPlatformAPI url}/resourcequery/resources/query` [T18]; O03 External Ids; O23; C16 Source Type Hint. **Writes:** O22 Gap Check Run, O23 Claim (open), O03 Last Seen On.
- **Who can run it:** the product role `platform-admin`, and Microsoft's own rule on top: six Entra roles can query the inventory, and only Global administrator, Power Platform administrator, Dynamics 365 administrator and Global reader see everything; AI administrator and AI reader see only agents, agentic apps, agent flows, environments and environment groups (doc 20, S6). Whether the two AI roles can use the inventory API is UNVERIFIED. The tool reads the admin's directory roles from the token's `wids` claim when present to fill Admin Role Used; whether that claim arrives in the server's token depends on the app registration's optional claims (UNVERIFIED).
- **Route:** A only.

#### 36. `compare_inventory_list`

- **Purpose:** the fallback for routes B and C and for estates the server cannot read itself. The admin's own tool fetches the list as the admin (for example `pac resource-query`, preview [T18]) and passes it in. The product compares and writes claims. It cannot prove the list came from Microsoft, so the run is marked evidence level "as reported".
- **In:** `{ estateKey: key(C07), items: [{ externalId: text(400), componentKindKey?: key(C16), sourceType?: text(200), labelAsSeen: text(200), targetAsSeen?: text(200), suggestedClaimantObjectId?: guid }] (up to 1000), sourceNote: text(500) }`
- **Out:** as `run_gap_check`, with `evidenceLevel: "as reported"`.
- **Reads:** O03, O23, C16. **Writes:** O22, O23.

#### 49. `run_owner_check`

- **Purpose:** the on-demand owner check (28, Q18 and issue 11). It runs live as the signed-in admin, like the gap check (D-15). The server reads, from Microsoft Graph through an on-behalf-of token, the account state and leave date of every principal with an active owner assignment. Disabled, deleted or leaving owners become findings, and the engine opens an owner check review for each affected application (document 23, P08 step 1). No unattended job.
- **In:** `{ gapCheckRunId?: id(O22) (continue a run), applicationIds?: id(O02)[], principalObjectIds?: guid[], skipToken? }`
- **Out:** `{ gapCheckRunId, principalsChecked, findings: [{ principal, directoryState: enum[enabled|disabled|deleted|unknown], leavesOn, applications: [{ applicationId, ownerRoleKey, hasDeputy }], ownerCheckReviewId }], more: bool, skipToken }`
- **Reads:** O06, O05, C28; Microsoft Graph users (account state, leave date) as the admin. **Writes:** O22 Gap Check Run with estate key `entra-owners` (document 23); O05 Directory State and Leaves On (as reported); O21 Review of type owner check (the engine).
- **Who can run it:** the product role `platform-admin`, and Graph's own rules on top. Reading a leave date needs `User-LifeCycleInfo.Read.All` and, delegated, the Lifecycle Workflows Administrator or Global Reader role (document 23, P08). The least permission for account state is UNVERIFIED (section 17).
- **Route:** A only.

#### 37. `list_claims`

- **Purpose:** the claim list for owners. Rows follow `claim-visibility`, which defaults to "admins and claimant" (28, issue 10).
- **In:** `{ statusMeanings?: enum[open|claimed|dismissed][] = [open], suggestedToMe?: bool, findingKinds?: enum[unregistered|vanished][], componentKindKeys?: key(C16)[], top?, skipToken? }`
- **Out:** `{ items: [{ claimId, findingKind, componentKind, labelAsSeen, targetAsSeen, url, suggestedClaimant, foundOn }] }`
- **Reads:** O23, O22. **Writes:** none.

#### 38. `claim_resource`

- **Purpose:** an owner claims a found item. It attaches to an existing application, or starts a new one as Draft. Claiming an item for an application the caller does not own creates a Proposed ownership decision instead of a silent change.
- **In:** `{ claimId: id(O23), one of applicationId: id(O02), newApplication: { name: text(200), purpose: text(2000), applicationKindKey: key(C15), portfolioId?: id(O01) } }`
- **Out:** `{ claimStatus: claimed, technicalComponentId, applicationId, applicationStatus, ownershipDecisionId? }`
- **Reads:** O23, O02, O06. **Writes:** O23 (claimed), O03 (link), O02 (draft, when new), O09 (ownership, Proposed, when needed).

#### 39. `dismiss_claim`

- **Purpose:** records that a found item is not a business application, with the reason. Reversible.
- **In:** `{ claimId: id(O23), reason: text(1000), reasonKey?: key(C07), undo?: bool = false }`
- **Out:** the write envelope with status dismissed or open.
- **Reads:** O23. **Writes:** O23 status and Dismiss Reason.

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
| Approve, reject, accept, authorize | AI never approves (D-02, D-10) | Approval cards in Outlook and Teams; the approval runner writes the outcome as the approval writer |
| Set a tier, move a stage, mark a change standard | These are decisions | An accepted decision of the right type; the engine applies a pre-approved standard change model |
| Activate configuration | Nobody activates by hand; activation follows the accepted decision (D-10; document 21, section 4.4) | Acceptance of the configuration change decision on an approval card |
| Assign or remove a role, add a group member | Membership is administered in Entra (D-11) | Entra and Entra ID Governance |
| Write configuration rows directly | Live configuration rows change only inside the activation operation (28, issue 3) | `propose_configuration_change` stores a draft; activation follows the accepted decision |
| Delete anything | Records are append-only where it matters; others are retired or withdrawn | `withdraw_record`, `record_deployment_correction`, `link_component` with outcome retired, retire through configuration |
| Write a wiki page, a work item or a pipeline | The product never writes into other tools (D-17, D-19) | The person's AI client, through the Azure DevOps or GitHub MCP servers, as the person |
| Import Microsoft's inventory on a schedule | Links, not copies (D-15) | `run_gap_check`, on demand |
| Generic table read and write | That is the Dataverse MCP server's job on Dataverse (D-16) | The Dataverse MCP server, under the same table rules |

---

## 7. AI never approves: how the surface enforces it

The rule holds in five layers. Safety never depends on a prompt or on the client behaving.

1. **No decision tools.** Section 6.13 lists what no tool can do. An AI client has no tool to call for it.
2. **An engine contract that configuration cannot loosen.** Whatever the tool or route, the engine refuses these writes when the actor kind is "AI client for a person", "agent identity" or "pipeline": a decision status of accepted or rejected, any approval outcome, registration status registered, the current tier, the current stage, a new configuration set version (activation), a configuration table row, and a role binding. The "AI approval lock" setting ships locked (doc 20, C04 Is Locked). "AI never approves" in the product is decided (D-02, D-10). Whether AI-only approvals from client tools ever count is open: the working default is that they never count (working default; open design question 7, see 28-working-defaults.md). Tool Policy's Max AI State can only tighten this, never widen it.
3. **Approval outcomes come only from the approval runner.** Only the approval writer workload role writes Outcome, Responded On and Typed Reason, protected by column security (doc 20, O10; 28, issue 7 and Q15). Only an answer from a person who holds the required role counts: Approver Actor Kind must be person and Approver Principal Kind must be user (sweep proposal). A Separation Rule (C40) can refuse an answer too (28, Q10).
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
| Tool surface | Only the product's 51 tools, filtered by role and toolset | A curated list of the product's custom APIs | 16 generic tools plus `invoke_api`; the client must find the custom API by name with `search` and `describe` [T8] |
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

- **An unexpected failure has no fixed code.** The build answers it with a JSON-RPC internal error that names only the correlation ID, outside the envelope, with no message text from the failure. Proposal: write that rule into section 3.5, next to the eleven codes.
- **Mapping engine reasons to the eleven codes.** A code that errors.json names for a reason wins, then the reason's catalog category, then the outcome word. A refusal by an AI rule (G-01, G-06, G-08, G-17, RA-06) for an AI client is `ai_cannot_do_this`. `validation` names the field, such as `answers[0].questionKey`. The server refuses any code outside the eleven, and `rate_limited` always carries a retry time.

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
- **Owner proposals.** `save_application_draft`, `propose_owner_change` and `claim_resource` record the proposal on a proposed ownership decision, because Owner Assignment rows are effect-only. `pendingAssignments` stays empty until the update-subject effect is designed (document 25, B25-3).
- **`record_change_result`** writes only the Result, as reported, never the status. A post-change review opens for a failed, rolled-back or partly succeeded result, or for a change type with Requires Post Change Review.
- **Confirmation cards** (open design question 9, see 28-working-defaults.md) are not sent while no approval runner is connected: `confirmationCardSent` is false with a warning, and an AI client's "current" on a Current Needs Person document type is stored as draft.
- **Linked mode.** A linked change request submits with no card, and a warning says the ITSM tool authorizes it; `linkedSource` stays none until Evidence Link facts feed OP-14.
- **`propose_configuration_change`** takes operations, or a draft patch as file content; a whole preset is refused until upgrades and reverts are built. A locked setting is refused with `locked` before OP-09 runs. `recordsInFlight` counts proposed decisions and submitted change requests.
- **`check_role_bindings`** writes no Audit Entry (`auditEntryId` null): the check writes nothing.
- **`explain_rules`** runs under the version the record was stamped with; a version parameter only warns.
- **`run_gap_check`.** A registered component counts as in an estate when its component kind is gap-check covered, because O03 holds no estate key (document 22, B22-5). The run's position travels in skipToken, and the counts live on the O22 row.
- **`run_owner_check`** writes the estate `entra-owners`, which no shipped preset's `estate` list holds yet (an open design question; document 22, B22-4).
- **Not built yet:** the C31 R or C letter check for `save_decision_proposal`, row-level portfolio read, the own-apps check (RA-02) for the record step, and per-portfolio change management mode rows.
