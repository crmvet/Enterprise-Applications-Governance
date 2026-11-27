# Claude Code and the MCP server

The product serves its tools to AI clients over the Model Context Protocol (MCP). An AI client such as Claude Code
signs in as the person who uses it, and every call runs as that person: the person's product roles, the Tool Policy,
the engine's rules and the person's own audit trail. The client acts for the person and never instead of them. AI
never approves.

## The endpoint

- **One path, stateless.** `POST <base>/mcp` takes one JSON-RPC 2.0 message and answers with one JSON body (streamable
  HTTP with no server stream and no session). `GET` and `DELETE` on the same path answer 405.
- **Methods.** `initialize`, `ping`, `tools/list`, `tools/call`, `resources/list`, `resources/templates/list` and
  `resources/read`. Any other method answers the JSON-RPC "method not found" error, and a notification gets no
  answer. The protocol version is `2025-06-18`.
- **Sign-in first.** Any message without a valid token answers 401 with a `WWW-Authenticate` header that names the
  protected resource metadata at `<base>/.well-known/oauth-protected-resource/mcp`. A valid token without the
  `mcp.access` scope answers 403.
- **The token.** A delegated Microsoft Entra ID access token (v2) for the product's API app registration, scope
  `mcp.access`. The service never forwards it.
- **Known clients.** Once the configuration in force holds any Known Client row, an AI client whose app ID matches no
  allowed row is refused with `not_authorized`. The label of its row becomes the Client App on every row it writes,
  such as "Claude Code".

## Connect Claude Code

### A local run (no tenant)

On the local server with the development identity (Development environment, the switch on, loopback only;
[install.md](install.md#a-local-run)), a header names a fictional persona instead of a sign-in:

```
claude mcp add --transport http product-local http://127.0.0.1:5080/mcp --header "X-Product-Dev-Identity: priya"
```

`priya` is Priya Castell, a maker. Other personas for the MCP endpoint are `nadia`, `owen`, `mateo` and
`config-admin`, and `agent` shows that an agent user holds no role
([the Azure README](../adapters/azure/README.md#run-the-server) lists every development persona). The command changes
Claude Code's own settings; `claude mcp remove product-local` takes the entry out. Add a second entry with another
name and header to act as another persona.

### A real install

The Entra admin registers a public client for Claude Code once per tenant, with the redirect URI
`http://localhost:<port>/callback`, and gives it the `mcp.access` scope (guide
[S2](setup/S2-connect-claude-code.md), path 1, and [install.md](install.md#2-the-claude-code-client)). The API's
Application ID URI must be the MCP endpoint's own URL, `https://<host>/mcp`, which needs a custom domain the tenant has
verified. Then each person runs:

```
claude mcp add --transport http --client-id <client-id> --callback-port <port> governance https://<host>/mcp
```

and signs in from `/mcp` in Claude Code with their work account. A configuration admin adds the client's app ID to the
Known Client list through a configuration change.

### A demo install

On a demo install (the Azure environment named `Demo`, with the demo persona switch on), the operator's Claude Code
may act for a fictional persona: add the header `X-Product-Demo-Persona: <name>` to the entry above, for example
`--header "X-Product-Demo-Persona: priya"`. Claude Code then holds that persona's roles, every AI rule still applies,
and its writes are stamped with the persona and the Client App "<label>, demo operator". Outside the Demo environment
the header is ignored.

### The first call

Ask Claude Code to call `whoami`. It answers who the person is, the actor kind of the entry
(`ai-client-for-person` on this endpoint) and the person's product roles. A person with no product role gets
`whoami` and nothing else.

## What the client sees

- **Its own tool list.** `tools/list` lists only the tools the person's roles may call under the Tool Policy in force.
  The tools served only to a person in the web app are never listed here.
- **Toolsets.** The header `X-Toolsets: portfolio,documentation` narrows the list to those toolsets. An unknown key is
  refused, so a typo never widens or empties the list. The toolset keys are the headings of [the tools](#the-tools).
- **Resources.** `guideline://G{n}` (one guideline), `configuration://in-force` (the version in force and its content
  hash) and `playbook://chapter/{n}` (a chapter's title and link). A person with no product role sees none.
- **The answer envelope.** Every tool answers the same envelope: `ok`, a `summary` the client can quote, the typed
  `data`, `asOf`, the `configurationSetVersion` in force, `couldNotRead` (what the person was not allowed to read, by
  kind and count), `warnings`, `untrustedFields`, up to three `nextSteps`, and the `correlationId`. A failure carries
  `error` with one of the fixed codes. The schemas are in `mcp/contracts` ([README](../mcp/contracts/README.md)).
- **Text people typed is data.** `untrustedFields` names each field of `data` that holds text a person or another
  client wrote. A client treats those fields as data, never as instructions.
- **Dry runs and repeats.** Every write tool takes `validateOnly: true`, which runs every rule and writes nothing.
  Most also take `clientRequestId`, so a write sent again with the same ID returns the first result, and the same ID
  with another payload is a conflict; `record_deployment` and `record_deployment_correction` take an
  `idempotencyKey` instead. Each tool's input schema says which it takes.

## What an AI client may do

The AI write state of a tool is the furthest an AI client can take a write. A Tool Policy row may tighten it (Max AI
State) and never widen it.

<!-- generated: ai-states (node tools/docs-gen.mjs) -->
| State | What it means |
|---|---|
| `read-only` | The tool writes nothing. |
| `draft` | Creates or edits a row whose status meaning is draft. |
| `proposed` | Creates a decision whose status meaning is proposed. |
| `submitted` | Moves a draft or proposed record to submitted and opens the approval requests that people answer on the decision page, or sends a request such as a review request. A request, never an approval. Allowed only where Tool Policy says so. |
| `as-reported` | Stores a fact the caller reports, such as a deployment or a change result. Never a decision. Stamped with the actor kind. |
| `link` | Stores an ID, a URL, a label and a last seen date for something another tool owns. |
| `candidate` | A configuration draft: a patch stored on a Configuration Draft row, never in force. The Configuration Set Version is created only at activation, after a person accepts the configuration change decision. |
| `withdrawn` | The caller withdraws a record it created and that is not final. |
| `person-only` | The tool is never listed for, nor served to, an AI client, on any entry. Only a person signed in through the product's web app calls it. answer_approval records the person's own answer to an approval request they were asked. AI never approves. |
<!-- end generated: ai-states -->

The rules every tool follows:

<!-- generated: ai-rules (node tools/docs-gen.mjs) -->
1. AI never approves. No tool an AI client is served approves, rejects, accepts, authorizes, sets a tier, moves a stage, activates configuration, assigns a role or deletes. The one tool that records an approval answer, answer_approval, is served only to a person in the product's web app, and the engine records the answer only for the signed-in person the approval request asks.
2. A governed record that an AI client creates starts as draft or proposed.
3. Submitted is a request act, never an approval. An AI client may submit only where the Tool Policy row says AI May Submit = yes for that subject kind. Submission only opens approval requests that people answer on the decision page in the web app.
4. Facts an AI client reports are stored as reported, with the actor kind. Links store an ID, a URL, a label and a last seen date. Neither is a decision.
5. A configuration change from an AI client is a candidate draft only. People answer its decision on the decision page in the web app. Once the decision is accepted, the engine activates it.
6. Max AI State on a Tool Policy row can only tighten these states, never widen them. The engine contract caps it.
7. Whatever the tool or route, the engine refuses these writes from an AI client, an agent identity or a pipeline: a decision status of accepted or rejected, any approval outcome, registration status registered, the current tier, the current stage, a new configuration set version, a configuration table row and a role binding.
<!-- end generated: ai-rules -->

Where an AI client may send a record for approval (`submit_record` and `request_architecture_review`, from the
shipped Tool Policy):

<!-- generated: ai-may-submit (node tools/docs-gen.mjs) -->
| Tool | Subject kind | An AI client may submit it |
|---|---|---|
| `submit_record` | `intake-request` | Yes |
| `submit_record` | `change-request` | Yes |
| `submit_record` | `decision` | Yes |
| `submit_record` | `configuration-draft` | Yes |
| `submit_record` | `data-use` | Yes |
| `submit_record` | `data-mapping` | No: a person submits it |
| `request_architecture_review` | `review-request` | Yes |
<!-- end generated: ai-may-submit -->

What is never a tool, and where it happens instead:

<!-- generated: not-tools (node tools/docs-gen.mjs) -->
| Act | Why it is not a tool | Where it happens instead |
|---|---|---|
| Approve, reject, accept, authorize | AI never approves. A person answers an approval request they were asked, on the product's decision page. | The product's decision page: the person answers through answer_approval, a tool served only to a person in the web app, and the token proves who answered. Teams and Outlook notices are designed to link to the page. Acceptance itself is never a tool: the engine accepts when the counted answers meet the rule. |
| Set a tier, move a stage, mark a change standard | These are decisions. | An accepted decision of the right type. The engine applies a pre-approved standard change model. |
| Activate configuration | Nobody activates by hand. Activation follows the accepted decision. | Acceptance of the configuration change decision, answered on the decision page in the web app. |
| Assign or remove a role, add a group member | Membership is administered in Entra. | Entra and Entra ID Governance. |
| Write configuration rows directly | Live configuration rows change only inside the activation operation. | propose_configuration_change stores a draft. Activation follows the accepted decision. |
| Delete anything | Records are append-only where it matters. Others are retired or withdrawn. | withdraw_record, record_deployment_correction, link_component with outcome retired, retire through configuration. |
| Write a wiki page, a work item or a pipeline | The product never writes into other tools. | The person's AI client, through the Azure DevOps or GitHub MCP servers, as the person. |
| Import Microsoft's inventory on a schedule | Links, not copies. | run_gap_check, on demand. |
| Generic table read and write | That is the Dataverse MCP server's job on Dataverse. | The Dataverse MCP server, under the same table rules. |
<!-- end generated: not-tools -->

## Refusals

A refused call answers `ok: false` with one of the fixed error codes ([api.md](api.md#error-codes) lists all of them
with their HTTP status). The ones an AI client meets most:

- **`not_found`** for a tool that does not exist or is not served to the caller, such as an approve tool. The message
  says to call `tools/list`.
- **`ai_cannot_do_this`** for a tool served only to a person (`answer_approval`, `complete_review`,
  `complete_change` and the others marked "Web app only" below), and for a write that would set a state only a person
  or the approval runner may set. The client points the person to the decision page or the page where a person acts.
- **`not_authorized`** when the person's roles do not include the tool. The message names the roles that may call it.
  It also answers a client that is not on the Known Client list.
- **`rule_violation`** when a configuration rule refuses the write, such as a data use whose rule is never. The
  message names the rule and where it is set; `error.ruleKey` names the row.
- **`validation`** for an input that is missing, malformed or not an allowed key. `error.field` names it.

A call refused by a rule, a role or the AI rules (`rule_violation`, `locked`, `not_authorized` or
`ai_cannot_do_this`) leaves one audit entry (operation `call-refused`) with the tool, the record it named, the code and
the rule, never the arguments: for an AI client on any tool, and for a person on a write or a person-only tool. An AI
client's call to a tool that does not exist leaves one too. A check (`validateOnly`), an input the schema refuses and a
call from someone with no product role leave none. The audit ledger records up to 60 refusals per caller and hour, and
a refusal past that is still answered. **AI activity** in the web app lists an AI client's refused calls, each with its
guideline where one applies.

## Limits

- **Requests.** Each signed-in caller may send 120 requests a minute to the MCP endpoint (`Product:Limits:McpPerMinute`),
  counted in the store so every instance sees the same count. Over the limit the answer is 429 with `Retry-After`.
- **Tool calls.** A Tool Policy row may set calls per person per hour; the shipped rows set none. Over it the tool
  answers `rate_limited` with `retryAfterSeconds`.
- **Page size.** A list tool returns at most 100 items a page (the setting `mcp-page-size-max`).

## The tools

<!-- generated: tool-list (node tools/docs-gen.mjs) -->
102 tools: 62 read and 40 write. 8 are served only to a person in the web app: `save_portfolio`, `complete_review`, `answer_approval`, `renew_approval_request`, `complete_change`, `save_deployment_target`, `retry_approval_card`, `run_due_state_refresh`. `check_change_approval` ships disabled, for a later release.

"Who may call" is the shipped Tool Policy default, which applies while the configuration holds no Tool Policy rows (Balanced holds none). A configuration that adds Tool Policy rows replaces the whole set, so it lists every tool; its Max AI State can only tighten the states each contract allows. The engine still checks the relation a row names (an owner of the application, the person the request asks).

### Session (`session`)

Who am I, what needs me, who holds a role, find people by name, find a record by its number, and the teams and committees with who is in them and what waits on them.

| No. | Tool | Question it answers | Writes, when an AI client calls it | Who may call | Served |
|---|---|---|---|---|---|
| 1 | `whoami` | Who am I here, and what may I do? | Read only | Anyone signed in | Web app and AI clients |
| 2 | `get_my_queue` | What needs me? | Read only | Any role | Web app and AI clients |
| 3 | `get_role_holders` | Who is on the change board? | Read only | Any role | Web app and AI clients |
| 61 | `find_people` | Who is this person? Find people by name | Read only | Any role | Web app and AI clients |
| 75 | `find_records` | Which record is DEC-0123? Find a record by its number | Read only | Any role | Web app and AI clients |
| 95 | `list_teams` | Which teams and committees are there, which roles do they hold, and what waits on them? | Read only | Any role | Web app and AI clients |
| 96 | `get_team` | Who is in this team, what does it decide, what does it support, and what waits on it now? | Read only | Any role | Web app and AI clients |
| 102 | `get_demo_freshness` | Is the demo fresh, or has a story approval request expired? | Read only | Any role, on a demo install only | Web app and AI clients |

### Portfolio (`portfolio`)

Portfolio questions: owners, card, impact, summary, requirements, the lists of reviews, portfolios and owner assignments across the portfolio, one review, the AI activity across the estate with its clients view, the measures and slide-back signals, the agents, and a person's portfolio write (the web app only).

| No. | Tool | Question it answers | Writes, when an AI client calls it | Who may call | Served |
|---|---|---|---|---|---|
| 4 | `find_applications` | Which apps match? | Read only | Any role | Web app and AI clients |
| 5 | `get_application_owners` | Who owns this app? | Read only | Any role | Web app and AI clients |
| 6 | `get_application_card` | Everything about this app, in one card | Read only | Any role | Web app and AI clients |
| 7 | `get_dependency_impact` | What breaks if it retires? | Read only | Any role | Web app and AI clients |
| 8 | `get_portfolio_summary` | How does the portfolio look, and which apps lack an owner? | Read only | Any role | Web app and AI clients |
| 9 | `get_requirements` | What does my tier need? | Read only | Any role | Web app and AI clients |
| 57 | `list_reviews` | Which reviews are overdue, due or done? | Read only | Any role | Web app and AI clients |
| 58 | `list_portfolios` | Which portfolios exist, and how do they look? | Read only | Any role | Web app and AI clients |
| 59 | `list_owner_assignments` | Who owns what across the portfolio, and who is leaving? | Read only | Any role | Web app and AI clients |
| 63 | `get_review` | Where is this review, and what does it need? | Read only | Any role | Web app and AI clients |
| 67 | `get_ai_activity` | What did AI clients do, and who decided? | Read only | Any role | Web app and AI clients |
| 74 | `save_portfolio` | Create or change a portfolio | None: served only to a person in the web app. A person shapes the portfolio tree | platform-admin, pmo (name, description and level only) | Web app only |
| 91 | `get_measures` | Is AI app development working? The measures, the slide-back signals and the rows behind them | Read only | Any role | Web app and AI clients |
| 93 | `list_agents` | Which agents exist, and is each one decided, reviewed, owned and ready to go live? | Read only | Any role | Web app and AI clients |

### Intake (`intake`)

The front door: duplicates, tier questions and the tier assessment, intake drafts, registering an app already built, components and dependencies, owner changes and leaves, and the intake list and record, and the PMO's demand fields.

| No. | Tool | Question it answers | Writes, when an AI client calls it | Who may call | Served |
|---|---|---|---|---|---|
| 10 | `check_duplicates` | Does this already exist? | Read only | Any role | Web app and AI clients |
| 11 | `get_tier_questions` | Which questions set the tier? | Read only | Any role | Web app and AI clients |
| 12 | `save_intake_draft` | Start or edit an intake request | Draft | requester, maker, application-owner, pmo, ai-lead, platform-admin | Web app and AI clients |
| 13 | `answer_tier_questions` | Which tier do my answers suggest? | Draft (an assessment, not a decision) | Requester of the intake, owner of the app | Web app and AI clients |
| 14 | `save_application_draft` | Register an app already built | Draft and Proposed | requester, maker, application-owner, ai-lead, platform-admin | Web app and AI clients |
| 15 | `link_component` | This app, function or agent belongs to my app; it moved; it is gone | Link (retired as reported) | Owner of the app, platform-admin | Web app and AI clients |
| 16 | `propose_owner_change` | Hand over or add an owner of an application, a data domain or a data location | Proposed | Owner of the app, ai-lead (agents), platform-admin; for a data domain, the domain's effective data owner, data-governance-lead and platform-admin; for a data location, its technical owner, data-governance-lead and platform-admin | Web app and AI clients |
| 46 | `record_owner_leave` | This owner leaves on this date | As reported | The leaving person (any role), an owner of an app the person owns, platform-admin | Web app and AI clients |
| 55 | `list_intake_requests` | Which intake requests wait for triage, and what is the demand? | Read only | Any role | Web app and AI clients |
| 56 | `get_intake_request` | Where is this intake request? | Read only | Any role | Web app and AI clients |
| 60 | `score_intake_request` | Score a request on the table: score, size band, horizon | As reported: stored as the caller reports it, stamped with the actor kind | pmo, platform-admin | Web app and AI clients |
| 68 | `get_tier_assessment` | Why is this tier, and what does it need? | Read only | Any role | Web app and AI clients |
| 73 | `save_dependency` | This app depends on that one; that dependency ended | As reported: stored as the caller reports it, stamped with the actor kind | Owner of the app, architect, platform-admin | Web app and AI clients |

### Decisions (`decisions`)

Decision proposals, submitting and withdrawing records, the decision log, approval status, architecture and security review requests, one decision with its approval state, the list of approval requests, a person's own answer to an approval request, completing a review (the web app only), the audit trail and the evidence pack.

| No. | Tool | Question it answers | Writes, when an AI client calls it | Who may call | Served |
|---|---|---|---|---|---|
| 17 | `save_decision_proposal` | Record a decision for acceptance | Proposed | Holders of R, A or C for the decision type | Web app and AI clients |
| 18 | `submit_record` | Send it for approval | Submitted | The record's requester or creator | Web app and AI clients |
| 19 | `withdraw_record` | Take it back | Withdrawn | The record's requester or creator | Web app and AI clients |
| 20 | `get_decision_log` | What was decided, by whom, and why? | Read only | Any role | Web app and AI clients |
| 21 | `get_approval_status` | Who still has to approve? | Read only | Any role | Web app and AI clients |
| 47 | `request_architecture_review` | Please review this design | Submitted (a request, never a decision) | Owner of the app, maker, architect, change-manager, platform-admin | Web app and AI clients |
| 52 | `answer_approval` | Approve or reject a request I was asked | None: served only to a person in the web app. AI never approves | The person the approval request asks, through any role (the engine checks the role or owner role, the separation rules and the request's state) | Web app only |
| 53 | `get_decision` | What is this decision, and who still has to answer? | Read only | Any role | Web app and AI clients |
| 54 | `list_approvals` | Which approval requests wait for me, or for others? | Read only | Any role | Web app and AI clients |
| 62 | `complete_review` | This review is done | None: served only to a person in the web app. A person marks a review done | The person whose review it is (the engine checks the lead owner role, the decision right or the approval rule, and the accepted outcome decision) | Web app only |
| 65 | `get_audit_trail` | What happened to this record, or what did this person do? | Read only | platform-admin, auditor | Web app and AI clients |
| 66 | `get_evidence` | What is the evidence for this application or this decision? | Read only | Any role | Web app and AI clients |
| 94 | `request_security_review` | Please review the security of this agent or app | Submitted (a request, never a decision) | Owner of the app, ai-lead, security-reviewer, architect, platform-admin | Web app and AI clients |
| 100 | `renew_approval_request` | Ask an expired approval request again | None: served only to a person in the web app. A person decides to ask again | The proposer of the decision, or a platform admin (the engine checks it) | Web app only |

### Change (`change`)

Change request drafts, the standard change check, the schedule, change results, and closing a change (the web app only).

| No. | Tool | Question it answers | Writes, when an AI client calls it | Who may call | Served |
|---|---|---|---|---|---|
| 22 | `save_change_request_draft` | Start or edit a change request | Draft | Owner of the app, maker, change-manager | Web app and AI clients |
| 23 | `check_standard_change` | Does this change fit a pre-approved model? | Read only | Any role | Web app and AI clients |
| 24 | `get_change_request` | Where is this change? | Read only | Any role | Web app and AI clients |
| 25 | `get_change_schedule` | What is planned, where, when? | Read only | Any role | Web app and AI clients |
| 26 | `record_change_result` | How did the change end? | As reported | Owner of the app, change-manager | Web app and AI clients |
| 64 | `complete_change` | This change is done | None: served only to a person in the web app. A person closes a change | Owner of the app, change-manager | Web app only |

### Deployment (`deployment`)

Deployment records and corrections, what runs where, the pipeline map, the deployment targets and a person's target registration and retirement (the web app only), the later gate check.

| No. | Tool | Question it answers | Writes, when an AI client calls it | Who may call | Served |
|---|---|---|---|---|---|
| 27 | `record_deployment` | This version reached this target | As reported | Owner of the app, maker, change-manager | Web app and AI clients |
| 28 | `get_deployments` | What runs where? | Read only | Any role | Web app and AI clients |
| 29 | `map_pipeline` | This pipeline deploys this app to these targets | Link | Owner of the app (technical), platform-admin | Web app and AI clients |
| 30 | `get_pipeline_map` | Which pipelines deploy this app? | Read only | Any role | Web app and AI clients |
| 31 | `check_change_approval` | Does this change have the approvals its policy requires? | Read only (logs the question) | Any role (preview); GateCheck.Ask app role on the record endpoint | Not served yet (ships disabled) |
| 48 | `record_deployment_correction` | That deployment belongs to this change, or a fact was wrong | As reported | Owner of the app, maker, change-manager, platform-admin | Web app and AI clients |
| 70 | `list_deployment_targets` | Where do deployments land, and what runs there? | Read only | Any role | Web app and AI clients |
| 71 | `save_deployment_target` | Register this place deployments land, or change it | None: served only to a person in the web app. A person states a target's stage and change path | platform-admin | Web app only |

### Documentation (`documentation`)

Required documents, document links and status, and the documentation list across the portfolio.

| No. | Tool | Question it answers | Writes, when an AI client calls it | Who may call | Served |
|---|---|---|---|---|---|
| 32 | `get_documentation_status` | What documentation is missing or stale for my app? | Read only | Any role | Web app and AI clients |
| 33 | `record_document_link` | Here is the page, and its status | Link, status as reported | Owner of the app, document owner | Web app and AI clients |
| 34 | `confirm_document_checked` | Is this document still true? | As reported | Owner of the app, document owner | Web app and AI clients |
| 69 | `list_documents` | Which documents are missing, stale or waiting across the portfolio? | Read only | Any role | Web app and AI clients |

### Gap check (`gapcheck`)

The live gap check, the owner check, the claim list and the run history, and the daily due-state refresh run now (the web app only).

| No. | Tool | Question it answers | Writes, when an AI client calls it | Who may call | Served |
|---|---|---|---|---|---|
| 35 | `run_gap_check` | What is running that is not in the register? | As reported (claims are open items) | platform-admin, plus an inventory Entra role | Web app and AI clients |
| 36 | `compare_inventory_list` | Compare this list I fetched myself | As reported | platform-admin | Web app and AI clients |
| 37 | `list_claims` | What is waiting to be claimed? | Read only | maker, application-owner, ai-lead, pmo, platform-admin, auditor | Web app and AI clients |
| 38 | `claim_resource` | That one is mine | Link, Draft and Proposed | maker, application-owner, ai-lead, platform-admin | Web app and AI clients |
| 39 | `dismiss_claim` | That one is not a business application | As reported (reversible) | platform-admin | Web app and AI clients |
| 49 | `run_owner_check` | Which owners are disabled, deleted or leaving? | As reported | platform-admin | Web app and AI clients |
| 72 | `list_gap_check_runs` | When was the gap check run, by whom, and what did it find? | Read only | maker, application-owner, ai-lead, pmo, platform-admin, auditor | Web app and AI clients |
| 86 | `run_due_state_refresh` | Run the daily due-state refresh now | None: served only to a platform admin in the web app. The engine runs the refresh every day on its own | platform-admin | Web app only |

### Configuration (`configuration`)

Reading configuration, rule traces, configuration health, the versions and drafts and one draft, proposing changes, presets, role binding checks and role access, the guidelines and playbook chapters, and the Admin page's status.

| No. | Tool | Question it answers | Writes, when an AI client calls it | Who may call | Served |
|---|---|---|---|---|---|
| 40 | `get_configuration` | What rules are in force, and what changed? | Read only | Any role | Web app and AI clients |
| 41 | `list_presets` | Which ready-made configurations exist? | Read only | Any role | Web app and AI clients |
| 42 | `propose_configuration_change` | Change the rules the way the admin said | Candidate | configuration-admin | Web app and AI clients |
| 43 | `export_configuration` | Give me the configuration as files | Read only | configuration-admin, platform-admin, auditor | Web app and AI clients |
| 44 | `check_role_bindings` | Is every role held only through its group, or a recorded workload grant? | Read only (logs the check) | platform-admin | Web app and AI clients |
| 50 | `explain_rules` | Which rule applied to this record, and why? | Read only | Any role | Web app and AI clients |
| 51 | `get_configuration_health` | Which versions exist, which drafts wait, and does the configuration load? | Read only | Any role | Web app and AI clients |
| 87 | `list_configuration_changes` | Which configuration versions exist, who accepted each and when, and which drafts wait? | Read only | Any role | Web app and AI clients |
| 88 | `get_configuration_draft` | What does this configuration draft change, is it valid, and what does it touch? | Read only | Any role | Web app and AI clients |
| 89 | `get_role_access` | Who holds each role, through which group, and what may each role do? | Read only | Any role | Web app and AI clients |
| 90 | `list_guidelines` | Why does each rule exist? The guidelines, the playbook chapters, the builder paths and the sanctioned clients | Read only | Any role | Web app and AI clients |
| 92 | `get_admin_status` | How is the product running: host settings, Tool Policy, clients, notices and the outbox? | Read only | platform-admin, configuration-admin, auditor | Web app and AI clients |
| 99 | `import_data_taxonomy` | Turn this CSV or catalog list into a configuration draft of data domains | Candidate | configuration-admin | Web app and AI clients |
| 101 | `retry_approval_card` | Send a stuck approval card again | None: served only to a platform admin in the web app | platform-admin | Web app only |

### Links (`links`)

Delivery and evidence links.

| No. | Tool | Question it answers | Writes, when an AI client calls it | Who may call | Served |
|---|---|---|---|---|---|
| 45 | `save_link` | Link this work item or this evidence | Link | Owner of the subject's app, change-manager, pmo; evidence on records the caller can write | Web app and AI clients |

### Data (`data`)

The business data domains, the data locations of any vendor, the mappings between them, the data uses of applications with their decision state, and the data gaps.

| No. | Tool | Question it answers | Writes, when an AI client calls it | Who may call | Served |
|---|---|---|---|---|---|
| 76 | `find_data_domains` | Which data domains match, and who owns them? | Read only | Any role | Web app and AI clients |
| 77 | `get_data_domain` | Everything about one data domain: owners up the chain, rules and their source, mappings, uses, decisions, outside matches, gaps | Read only | Any role | Web app and AI clients |
| 78 | `find_data_locations` | Which stores match a platform, stage, parent, text, unmapped or stale? With a locationId, the full detail of one | Read only | Any role. External IDs and URLs are restricted read | Web app and AI clients |
| 79 | `resolve_data` | Which data is this table, object or file? | Read only | Any role | Web app and AI clients |
| 80 | `check_data_use` | May this app use this data this way, and what is needed? | Read only | Any role | Web app and AI clients |
| 81 | `get_data_gaps` | What is missing, broken or waiting in the data? | Read only | Any role | Web app and AI clients |
| 82 | `save_data_use` | This app reads or writes this data, or it stopped | Draft; Proposed: an AI client's use is a draft and its data-use decision is proposed, never sent; a person confirms the use and sends the decision | Owner of the app, maker, application-owner, platform-admin | Web app and AI clients |
| 83 | `save_data_location` | This store exists, moved, or is gone (up to 100 items per call) | Draft: an AI client's location is a draft until a person confirms it | platform-admin, architect, application-owner, the location's technical owner, data-governance-lead | Web app and AI clients |
| 84 | `save_data_mapping` | This domain's data sits here, or it left | Draft; Proposed: an AI client's mapping is a draft and its copy decision is proposed, never sent | The location's technical owner, the domain's effective owner or steward, platform-admin, data-governance-lead | Web app and AI clients |
| 85 | `move_data_references` | Move mappings and uses off a domain | None: an AI client may preview with validateOnly; a person moves the rows | platform-admin, data-governance-lead | Web app and AI clients |
| 97 | `list_data_uses` | Which data uses are there across every domain, and is each one decided? | Read only | Any role | Web app and AI clients |
| 98 | `list_data_mappings` | Where is each domain's data kept, and is every copy outside production decided? | Read only | Any role | Web app and AI clients |
<!-- end generated: tool-list -->

The input and output schema of each tool is `mcp/contracts/tools/<tool>.input.schema.json` and
`<tool>.output.schema.json`.
