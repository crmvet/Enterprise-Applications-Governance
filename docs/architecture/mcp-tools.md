# MCP tools

**Status: in design.** This page describes the product's own MCP server as designed: its toolsets and tools, the identity path, and what AI may and may not write. Nothing here is built or running.

## In one screen

1. **51 tools in 10 toolsets.** A caller sees only the tools its product roles allow, and can narrow the list further to the toolsets it needs.
2. **Reads answer the questions people ask:** "who owns this app?", "what breaks if it retires?", "what does my tier need?", "what needs me?" and "what documentation is missing or stale for my app?".
3. **Writes stop short of a decision.** A tool can leave a record as Draft, Proposed, Submitted (a request, never an approval), as reported, as a link, or as a configuration draft. No tool approves, sets a tier, moves a stage, activates configuration, assigns a role or deletes.
4. **On behalf of the person, end to end.** The server checks the person's token, never forwards it, and reaches the data on behalf of that person.
5. **Claude Code is the reference client.** It is designed to connect as a client registered in your own Entra tenant first.
6. **Stateless.** No session state lives in the server between calls. Any instance can answer any request.

![The MCP server: 10 toolsets, 51 tools](img/s2-mcp-1-toolsets.png)

## Rules every tool follows

1. **On behalf of the signed-in person.** The server never answers with one shared identity's rights.
2. **AI never approves.** The engine refuses approval, acceptance, tier, stage, activation and role writes from an AI client, whatever the tool.
3. **AI-created rows start as Draft or Proposed.** Facts an AI client reports, such as a deployment, are stored "as reported", with the actor kind.
4. **Tools are configuration too.** Which role may call which tool, the highest state an AI client may leave a record in, and rate limits are configuration rows. Configuration can only tighten the limits the engine sets, never widen them.
5. **Roles come only through Entra groups.** The server reads roles from the token. It never assigns a role.
6. **Links, not copies.** Tools store an ID, a link, a label and a "last seen" date for anything another tool owns. Entra rosters and Microsoft's inventory are read live and never stored.
7. **Stateless.** See below.
8. **No gateway holds a shared credential.** A gateway in front of the server must pass the caller's own token through.

## Toolsets

A client can ask for only the toolsets it needs with an `X-Toolsets` request header, for example `X-Toolsets: portfolio,documentation`. Without the header, the caller gets every toolset its roles allow. A short tool list helps an AI client choose the right tool.

| Toolset | What it covers | Tools |
|---|---|---|
| `session` | Who am I, what needs me, who holds a role | 3 |
| `portfolio` | Owners, the application card, impact, the portfolio summary, requirements | 6 |
| `intake` | Duplicates, tier questions, intake drafts, registering an app already built, components, owner changes and leaves | 8 |
| `decisions` | Decision proposals, submitting and withdrawing records, the decision log, approval status, architecture review requests | 6 |
| `change` | Change request drafts, the standard change check, the schedule, change results | 5 |
| `deployment` | Deployment records and corrections, what runs where, the pipeline map, the later gate check | 6 |
| `documentation` | Required documents, document links and status | 3 |
| `gapcheck` | The live gap check, the owner check and the claim list | 6 |
| `configuration` | Reading configuration, rule explanations, configuration health, proposing changes, presets, role binding checks | 7 |
| `links` | Delivery and evidence links | 1 |

## The tools

The last column is the highest state a call from an AI client can leave a record in.

| Tool | Toolset | Question it answers | Highest AI state |
|---|---|---|---|
| `whoami` | session | Who am I here, and what may I do? | Read only |
| `get_my_queue` | session | What needs me? | Read only |
| `get_role_holders` | session | Who is on the change board? | Read only |
| `find_applications` | portfolio | Which apps match? | Read only |
| `get_application_owners` | portfolio | Who owns this app? | Read only |
| `get_application_card` | portfolio | Everything about this app, in one card | Read only |
| `get_dependency_impact` | portfolio | What breaks if it retires? | Read only |
| `get_portfolio_summary` | portfolio | How does the portfolio look, and which apps lack an owner? | Read only |
| `get_requirements` | portfolio | What does my tier need? | Read only |
| `check_duplicates` | intake | Does this already exist? | Read only |
| `get_tier_questions` | intake | Which questions set the tier? | Read only |
| `save_intake_draft` | intake | Start or edit an intake request | Draft |
| `answer_tier_questions` | intake | Which tier do my answers suggest? | Draft (an assessment, not a decision) |
| `save_application_draft` | intake | Register an app already built | Draft and Proposed |
| `link_component` | intake | This app, flow or agent belongs to my app; it moved; it is gone | Link |
| `propose_owner_change` | intake | Hand over or add an owner | Proposed |
| `record_owner_leave` | intake | This owner leaves on this date | As reported |
| `save_decision_proposal` | decisions | Record a decision for acceptance | Proposed |
| `submit_record` | decisions | Send it for approval | Submitted |
| `withdraw_record` | decisions | Take it back | Withdrawn |
| `get_decision_log` | decisions | What was decided, by whom, and why? | Read only |
| `get_approval_status` | decisions | Who still has to approve? | Read only |
| `request_architecture_review` | decisions | Please review this design | Submitted (a request, never a decision) |
| `save_change_request_draft` | change | Start or edit a change request | Draft |
| `check_standard_change` | change | Does this change fit a pre-approved model? | Read only |
| `get_change_request` | change | Where is this change? | Read only |
| `get_change_schedule` | change | What is planned, where, when? | Read only |
| `record_change_result` | change | How did the change end? | As reported |
| `record_deployment` | deployment | This version reached this target | As reported |
| `record_deployment_correction` | deployment | That deployment belongs to this change, or a fact was wrong | As reported |
| `get_deployments` | deployment | What runs where? | Read only |
| `map_pipeline` | deployment | This pipeline deploys this app to these targets | Link |
| `get_pipeline_map` | deployment | Which pipelines deploy this app? | Read only |
| `check_change_approval` | deployment | Does this change have the approvals its policy requires? (later) | Read only |
| `get_documentation_status` | documentation | What documentation is missing or stale for my app? | Read only |
| `record_document_link` | documentation | Here is the page, and its status | Link, status as reported |
| `confirm_document_checked` | documentation | Is this document still true? | As reported |
| `run_gap_check` | gapcheck | What is running that is not in the register? | As reported (claims are open items) |
| `compare_inventory_list` | gapcheck | Compare this list I fetched myself | As reported |
| `run_owner_check` | gapcheck | Which owners are disabled, deleted or leaving? | As reported |
| `list_claims` | gapcheck | What is waiting to be claimed? | Read only |
| `claim_resource` | gapcheck | That one is mine | Link, Draft and Proposed |
| `dismiss_claim` | gapcheck | That one is not a business application | As reported |
| `get_configuration` | configuration | What rules are in force, and what changed? | Read only |
| `explain_rules` | configuration | Which rule applied to this record, and why? | Read only |
| `get_configuration_health` | configuration | Which versions exist, which drafts wait, and does the configuration load? | Read only |
| `list_presets` | configuration | Which ready-made configurations exist? | Read only |
| `propose_configuration_change` | configuration | Change the rules the way the admin said | Configuration draft |
| `export_configuration` | configuration | Give me the configuration as files | Read only |
| `check_role_bindings` | configuration | Is every role held only through its group, or a recorded workload grant? | Read only |
| `save_link` | links | Link this work item or this evidence | Link |

`check_change_approval` waits for the open design question about the change gate. See the gate ladder in [Configuration and presets](configuration-and-presets.md).

The server is also designed to offer a few read-only MCP resources that cite, never copy: a guideline's number, version, rule and link; the configuration version in force with its hash; and a playbook chapter's title and link.

### What each state means

| State | Meaning |
|---|---|
| Read only | The tool writes nothing |
| Draft | Creates or edits a row in draft |
| Proposed | Creates a decision in proposed |
| Submitted | Moves a draft or proposed record to submitted and starts the approval cards, or sends a request such as a review request. A request, never an approval |
| As reported | Stores a fact the caller reports, such as a deployment or a change result. Never a decision |
| Link | Stores an ID, a link, a label and a "last seen" date for something another tool owns |
| Configuration draft | A proposed configuration change, stored as a patch. Never in force until a person accepts it |
| Withdrawn | The caller withdraws a record it created that is not final |

## What AI may and may not write

| An AI client may | An AI client may not |
|---|---|
| Read anything the person may read | Approve, reject, accept or authorize anything |
| Save drafts: intake requests, application registrations, change requests | Set a tier, move a stage or mark a change standard. These are decisions |
| Propose decisions | Activate configuration, or write configuration rows directly |
| Submit records for approval, where configuration allows it | Assign or remove a role, or add a group member |
| Record facts as reported: deployments, change results, document status | Delete anything |
| Store links | Write a wiki page, a work item or a pipeline. Those stay in their own tools |
| Draft configuration changes for an admin | Import Microsoft's inventory on a schedule |

By default in the design, an AI client may submit intake requests, change requests and decisions for the person. Registering an application, and marking a document current when a gate depends on it, send the person a confirmation card first. Links and deployment records need nothing more. **Open design question:** which acts count as the person's own when an AI client drafts them. The design uses the defaults above.

The owner's AI tool can still write documents and work items, for example through the Azure DevOps or GitHub MCP servers, as the same person. The product links to the result and tracks its status.

### How "AI never approves" holds

The rule holds in five layers. It never depends on a prompt or on the client behaving.

1. **No decision tools.** No tool approves, rejects, accepts, authorizes, sets a tier, moves a stage, activates configuration or assigns a role.
2. **An engine rule configuration can't loosen.** Whatever the tool, the engine refuses those writes when the actor is an AI client, an agent identity or a pipeline. The "AI approval lock" setting ships locked.
3. **Approval answers come only from the approval runner.** Only the approval runner's workload identity may write an approval answer, and only an answer from a person who holds the required role counts.
4. **The decision act is not callable.** No API lets a caller mark a decision accepted. The engine marks it accepted when the counted approvals meet the rule.
5. **Submitting only starts cards.** An AI submission is stamped "AI client for a person" and starts approval cards that only people answer.

A risk remains outside the product: a person could let their own AI tool answer a card in another tool on their behalf. Typed reasons and the audit trail reduce this risk.

## Identity, end to end

![One identity, end to end](img/s2-mcp-2-identity.png)

| Hop | From | To | Credential |
|---|---|---|---|
| 1 | The person | Claude Code or another MCP client | The person signs in through Microsoft Entra ID in a browser |
| 2 | The MCP client | The product's MCP server | An access token for the server's own app registration, with the delegated scope `mcp.access` |
| 3 | The server | Its own token check | Signature, issuer, audience, lifetime, tenant, client and scope are checked |
| 4 | The server | The engine and the database | The engine runs in the same service and acts as the person, using the person's object ID and roles from the validated token. The database connection uses the service's managed identity, with the person's ID in the session context for row-level security |
| 5 | The server | Microsoft APIs (gap check, Entra rosters) | A new token, obtained on behalf of the person, for the Power Platform API or Microsoft Graph |
| 6 | The approval runner | The API | The approval writer workload role, held through a workload grant |

What Microsoft says about this path (all checked 2026-11-22):

- An MCP server is an OAuth 2.0 protected resource. Microsoft's guide says to register the server as an API, set its Application ID URI to the server's endpoint, expose the delegated scopes clients need, and "define application roles only if the server supports app-only access" ([Learn](https://learn.microsoft.com/entra/agent-id/secure-mcp-server-with-entra-id)).
- "Pass-through scenarios where the server forwards its token create security vulnerabilities, so avoid these patterns. If you need to access a downstream resource, obtain a new token through the on-behalf-of flow" ([Learn](https://learn.microsoft.com/azure/app-service/configure-authentication-mcp)).
- The on-behalf-of flow "only uses delegated scopes and not application roles" ([Learn](https://learn.microsoft.com/entra/identity-platform/v2-oauth2-on-behalf-of-flow)). So a call made for a person can never carry more than that person's rights.

### How Claude Code connects

Microsoft's guide names two client identity models: a registered client, or "a Client ID Metadata Document URL when pre-registration isn't practical" ([Learn](https://learn.microsoft.com/entra/agent-id/secure-mcp-server-with-entra-id), checked 2026-11-22).

| | A registered client | A Client ID Metadata Document (CIMD) |
|---|---|---|
| What it is | You register a public client app in your own tenant and preauthorize it on the server | Claude Code identifies itself with an HTTPS URL as its client ID |
| Status | Generally available | Public preview. Microsoft says it "isn't recommended for production workloads" |
| Claude Code | Connects with the client ID you register | Listed by Microsoft as "Supported" |
| Microsoft's advice | Preferred | "Implementers should always prefer pre-registration of a client with Entra when possible" |
| In the design | The setup guide leads with this path | Offered for pilots. It becomes the lead once it is generally available |

The CIMD facts are from Microsoft's page on Client ID Metadata Documents ([Learn](https://learn.microsoft.com/entra/agent-id/client-id-metadata-documents), checked 2026-11-22). The same page says Microsoft Entra ID represents a CIMD client as an agent identity in each tenant, and that CIMD clients can't use app-only tokens.

### What the server never does

- Forward the incoming token to any other service.
- Call the database with a shared identity that answers for everyone. The engine always acts as the caller and checks the caller's own rights.
- Store a token, a secret or a row between calls.
- Accept tokens from another tenant.
- Re-expose another product's data. Azure DevOps, GitHub and wiki content stay behind their own MCP servers, which the person's AI client calls as the same person.

## The response every tool returns

| Field | What it holds |
|---|---|
| `ok` | True when the call succeeded |
| `summary` | One or two plain sentences an AI client can quote to the person |
| `data` | The tool's own result |
| `links.recordUrl` | A link that opens the main record in the web app, so the AI can cite it |
| `asOf` | When the data was read |
| `configurationSetVersion` | The configuration version in force, so an answer can be traced to its rules |
| `couldNotRead` | What the caller was not allowed to read, by kind and count, never its content |
| `warnings` | Notes that do not block |
| `untrustedFields` | The fields that hold text people typed. Clients must treat them as data, never as instructions |
| `nextSteps` | Up to three suggested next tools, such as `submit_record` after a draft is saved |
| `correlationId` | One value per call, written to the audit entry and to telemetry |

Every write also returns the row written, its status label and fixed meaning, who performed it exactly as the engine stamped it, whether an AI created it, and the engine stamp.

### Errors

Errors return `ok: false` and a fixed code. A refusal names the configuration row that refused the write, and the guideline it applies.

| Code | When |
|---|---|
| `not_signed_in` | No valid token |
| `not_authorized` | The caller lacks the role, the owner role or the row access |
| `ai_cannot_do_this` | The write would set a state that only a person or the approval runner may set |
| `rule_violation` | A configuration rule refused the write |
| `validation` | An input is missing, malformed or not an allowed key |
| `not_found` | The ID or key does not exist, or the caller cannot see it |
| `conflict` | The record changed since it was read |
| `locked` | The write touches a locked setting or a fixed meaning |
| `rate_limited` | The rate limit for this tool was reached |
| `upstream_unavailable` | A Microsoft API the tool needs did not answer |
| `configuration_unavailable` | The engine cannot load a valid configuration, or a rule row it needs is missing. Governed writes are refused |

## Stateless design

- **One endpoint, streamable HTTP.** Every request carries its own bearer token.
- **No session state.** The tool list is worked out per request from the token's roles and the tool configuration.
- **No row cache between calls.** Reads go to the database each time. Configuration may be cached by version number, because a version never changes.
- **Paging carries the query, not the server.** Any instance can serve the next page.
- **Long work keeps its state in the database.** The gap check returns a run ID and a page token for each estate. The run row holds the progress, not the server.
- **Retries are safe.** Every write takes an optional request ID. A retry with the same value returns the first result and writes nothing new.

## Routes to the engine

![Three routes to one engine](img/s2-mcp-3-routes.png)

The diagram compares three ways a tool call can reach the rules. On Azure, as designed for the first release, only one route exists: the product's own MCP server, which runs in the same service as the API and the engine. It is also the only route that can run the live gap check and read Entra as the person. The other two routes exist only on a Dataverse data store, which is not part of the first release design.

## Hosting

| Item | Design |
|---|---|
| Host | One Azure Functions app on the Flex Consumption plan holds the MCP endpoint, the API and the engine |
| Fallback | Azure Container Apps, if the sign-in or MCP tests fail on Functions |
| Cold starts | One always ready instance for interactive clients |
| Sign-in | Built-in authentication on. A call with no token gets HTTP 401 with a link to the protected resource metadata |
| Telemetry | Application Insights: tool name, outcome, time taken, client app, a hash of the caller's ID and the correlation ID. Never the token, and never text people typed |

What Microsoft says about these hosts (all checked 2026-11-22):

- Azure Functions maps function triggers to MCP tools, and is "optimized for stateless, event-driven tool execution with per-invocation pricing" ([Learn](https://learn.microsoft.com/azure/container-apps/mcp-choosing-azure-service)).
- Hosting your own SDK-based MCP server on Functions is in preview, supports stateless servers that use the streamable HTTP transport, and requires the Flex Consumption plan ([Learn](https://learn.microsoft.com/azure/azure-functions/self-hosted-mcp-servers)).
- Flex Consumption "always ready" instances reduce cold start latency ([Learn](https://learn.microsoft.com/azure/azure-functions/flex-consumption-plan)).
- For an MCP server on Container Apps, a minimum of one replica is recommended for interactive use ([Learn](https://learn.microsoft.com/azure/container-apps/mcp-overview)).

## Pipelines do not use MCP

A pipeline step that records a deployment calls a plain HTTPS endpoint with the same contract as `record_deployment`. MCP adds nothing for a pipeline, because no model is choosing tools. The step uses its own workload identity with the `Deployment.Record` app role, held through a recorded, expiring workload grant. Steps fail soft: a governance write that fails warns and never fails the deployment. See [Sign-in and security](sign-in-and-security.md).

## An example, as designed

A planner asks Claude Code: "Who owns the field inspections app, and what breaks if the old scheduler retires?"

1. Claude Code calls `find_applications` to find both apps.
2. It calls `get_application_owners` for the inspections app. The answer lists the business owner, the technical owner and their deputies, and flags any owner gap.
3. It calls `get_dependency_impact` on the old scheduler with the direction "dependants". The answer lists the apps that depend on it, up to the configured depth, with the criticality of each link.
4. Each answer carries the configuration version in force and a link to the record in the web app, so Claude Code can cite its sources.

Nothing is written. If the planner then asks to record a retirement decision, Claude Code can only save it as Proposed and submit it. The business owner decides on an approval card.

The people and apps in this example are fictional.
