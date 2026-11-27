# mcp/contracts

The MCP tool contracts, from document 24. Every schema is JSON Schema draft 2020-12.

It holds the response envelope, the error codes, the tool index, the input and output schemas for all 102 tools,
the three R1 resources, the Tool Policy seed rows (proposed C41) and the record endpoint contract for pipelines.

## Files

| File | What |
|---|---|
| `common.schema.json` | Shared types: IDs, keys, dates, paging, labels, principals, the engine meaning lists, `stampedBy` (who last wrote a record, as a read returns it), and the data taxonomy types (`dataDomainItem`, `dataUseItem`, `dataMappingItem`, `dataLocationItem`, `dataFinding`, `dataApprover` and the rule, state, origin and access lists) |
| `envelope.schema.json` | The response envelope: success, failure, error, the write result, performed by, engine stamp |
| `errors.json` | The 12 fixed error codes: meaning, retryable, HTTP status, client action, engine reason examples |
| `errors.schema.json` | Schema for `errors.json` |
| `tools.json` | Index of all 102 tools: number, name, toolset, read or write, the entries that serve it, default roles, AI write state, release, annotations, contract files |
| `tools.schema.json` | Schema for `tools.json` |
| `tools/<tool>.input.schema.json` | One per tool: the tool's arguments |
| `tools/<tool>.output.schema.json` | One per tool: the envelope, with `data` typed for that tool |
| `resources.json`, `resources.schema.json` | The three R1 resources (`guideline://G{n}`, `configuration://in-force`, `playbook://chapter/{n}`; document 24, section 6.12) |
| `resources/<resource>.content.schema.json` | One per resource: the JSON a read returns |
| `tool-policy.seed.json` | 102 Tool Policy seed rows, one per tool. Checked with `config/schema/config-bundle.schema.json#/$defs/entityFile` (C41, a proposal) |
| `record-endpoint.json`, `record-endpoint.schema.json` | The record endpoint for pipeline record steps: paths, app-only auth, fail-soft, idempotency, HTTP statuses |
| `record-endpoint/<request or response>.schema.json` | Requests and responses for deployments and corrections |

## How the schemas fit together

- Every tool output is the envelope. Its `oneOf` is success with typed `data`, or failure with `error`.
- Write tools extend `writeResult` from the envelope and close it with `unevaluatedProperties: false`.
- Refs are relative file paths, and no schema has `$id`. The JSON check keys each schema by its file URL,
  so the refs resolve on disk. The server inlines the refs into `$defs` before it publishes a tool
  schema to MCP clients or to the Dataverse plug-in.
- Inputs and outputs are closed. An unknown field fails validation.
- The root of every tool schema has `type: object`, as MCP needs.
- The `examples` in each schema validate against that schema. They use fictional IDs and labels only.
- Reads that return governed records add `isAiCreated` and `performedBy` (`common.schema.json#/$defs/stampedBy`) to each
  record: who made the row's last engine-checked write, as the engine stamped it, or null for a row the engine never
  stamped, such as seeded data. Tools 2, 4, 6, 20, 24 and 53 to 59 do; tool 60 returns the write result (document 24, section 3.4 and B24-7).

## Entry points

The same tools and the same envelope serve two entry points. The entry decides how the engine stamps a write, never a
field the caller sends.

| Entry | Who calls | Actor kind | Notes |
|---|---|---|---|
| MCP endpoint | An AI client acting for the signed-in person | `ai-client-for-person` | Every write is AI-created and names the person as On Behalf Of. The Known Client check, Max AI State and AI May Submit apply |
| The product's web app, through its API | The person | `person` | Only the web app's own client IDs, which the host declares, may call. Max AI State and AI May Submit do not apply. Roles, the rate limit and the input schema apply as on MCP |

`whoami` answers the actor kind of the entry: `ai-client-for-person` on the MCP endpoint, `person` through the web app.
An agent user or agent identity holds no role on either entry.

**One tool records a person's answer.** `answer_approval` (tool 52, D-38) records the signed-in person's own answer to an
approval request they were asked, on the product's decision page. Its `entries` in `tools.json` is `["person"]` and its AI
write state is `person-only`: the MCP endpoint never lists it, and a call to it from an AI client, on either entry,
answers `ai_cannot_do_this`. An app-only token never reaches any tool. The engine then checks that the person is the one
the request asks (the role, or the owner role on the decision's application, held now; no separation rule against them;
the request pending and not expired) and refuses anyone else. No other tool records an approval answer, and no tool
accepts a decision: the engine accepts it when the counted answers meet the rule.

**Two more are a person's acts** (document 24, B24-10). `complete_review` (tool 62) marks a review done once its outcome
decision is accepted, and `complete_change` (tool 64) closes an authorized change with its result. Both are
`person-only` in the same way: an AI client drafts the outcome and reports the result, and the engine refuses an AI
actor's done or completed on every path.

**Two writes of the register are a person's too** (document 24, B24-11). `save_deployment_target` (tool 71) and
`save_portfolio` (tool 74) are `person-only`: a target's stage and change path, and a portfolio's place in the tree,
decide which rules apply. Tools 65 to 74 serve the remaining screens: `get_audit_trail` (the auditor and the platform
admin only), `get_evidence`, `get_ai_activity`, `get_tier_assessment`, `list_documents`, `list_deployment_targets`,
`list_gap_check_runs` and `save_dependency` (as reported). `get_deployments` is also the Deployments list: it filters by
named flags and counts each.

**The due-state refresh** (tool 86, `run_due_state_refresh`, toolset `gapcheck`; document 24, B24-17) is `person-only`
and platform-admin: a platform admin runs the engine's daily refresh now from the web app. Its items commit in calls of
their own before the tool reads its answer. Record links (`links.recordUrl` and every `recordUrl`) use the
`recordUrl` definition of `common.schema.json`: an https URL, or an http URL on a loopback address for a local run with no
public base URL. Every `stampedBy` carries `draftedBy`, the AI client that created the row and the person it acted for.

**Keeping approval requests alive, and the data taxonomy import** (tools 99 to 102; document 24, B24-22).
`import_data_taxonomy` (configuration toolset, configuration-admin, candidate) turns a CSV or a catalog's rows into a
configuration draft of data domains, lists the domains in force the file leaves out, and returns the file's owners as
prepared owner changes and the catalog owner differences (DC-15), never live owners. `renew_approval_request` ("Ask
again", decisions toolset, person entry only) asks an expired request, or one in its last 48 hours, again for the
proposer or a platform admin. `retry_approval_card` (configuration toolset, platform admin, person entry only) sends a
failed or long-sent approval card again through the host's outbox port. `get_demo_freshness` (session toolset, any role)
lists the story requests that expired or expire soon, only on a demo install. Changed contracts: `get_measures` (targets
from configuration, `targetMet`, each signal's decisions), `save_decision_proposal` (`slideBackSignalKey`),
`get_decision` (`slideBackSignal`), `get_dependency_impact` (a data location start, `via`, `dataLocation`),
`list_deployment_targets` and `save_deployment_target` (`dataLocations`), `get_tier_questions` (`impliedAnswer`),
`get_data_gaps` (`appLevelDataOwners`, and DC-10's `suggestion` in `dataFinding`), `propose_configuration_change`
(`impact.dataUses`).

**The data rows across domains** (tools 97 and 98; document 24, B24-21). Two reads for any role, in the data toolset:
`list_data_uses` (every open data use across the domains, with its OP-16 state, counts by state, filters for domain and
its subtree, application, state, AI, access and words, and uses by ID) and `list_data_mappings` (every open mapping with
the copy state of a copy outside production, filters for domain, location, origin and copy state, and mappings by ID).
New filters on existing reads: `find_applications` (`usesAi`, `buildPathKeys`), `get_change_schedule` and
`get_deployments` (`portfolioId`), `get_audit_trail` (`clientApp`), `list_documents` (`documentLinkId`),
`list_gap_check_runs` (`gapCheckRunId`); `get_pipeline_map` with no input lists every pipeline.

**Teams and committees** (tools 95 and 96; document 24, B24-20). Two reads for any role, in the session toolset:
`list_teams` (every group the install knows, with its kinds derived on every read, the roles it holds, its member count,
what waits on it and its applications; filters for kind, text, role and member) and `get_team` (one team's members, roles,
the decision types it answers, applications and data owner roles, what waits on it now, recent answers and owner gaps).
Members come from the directory reader, never stored; `membersReadFrom` says from where, and with no directory reads
`membership` says membership is managed in Entra. A team's record link is `.../records/team/<objectId>`.

**The screens still missing** (tools 87 to 94; document 24, B24-18). Reads for any role: `list_configuration_changes`
(versions with who accepted each, and drafts), `get_configuration_draft` (diff, validation, impact), `get_role_access` (holders
read live, tools, decision rights, approval rules, separation rules), `list_guidelines` (guidelines, playbook chapters by
track, build paths, Known Clients), `get_measures` (nine measures and ten slide-back signals, with drill rows) and
`list_agents`. `get_admin_status` is for the platform admin, the configuration admin and the auditor. `request_security_review`
writes a review request (AI May Submit yes for review requests, working default question 9). `common.schema.json` gains
`guidelineRef`, the chip a requirement, a gate and an approval rule carry; an error's `guideline` names the guideline a
refusal keeps. `save_deployment_target` takes `retire` and `retiredReason`, and targets carry `retiredOn` and
`retiredReason`; `get_ai_activity` adds `clients`.

**The data toolset** (tools 76 to 85; data taxonomy slice 3, D-37 and D-41; document 24, B24-16). Six reads:
`find_data_domains`, `get_data_domain`, `find_data_locations` (external IDs and URLs are restricted read),
`resolve_data`, `check_data_use` and `get_data_gaps`. Four writes: `save_data_use` and `save_data_mapping` (draft, and
a proposed decision), `save_data_location` (draft), and `move_data_references`, whose only AI write state is
`read-only`: `tools.schema.json` lets a write tool name read-only alone, and the server then serves it to an AI client
for a `validateOnly` preview only. A person confirms the drafts in the web app, and a person's confirmed use or copy
sends its decision to the approvers. A use or a copy whose governing rule is never is refused for everyone, with
`rule_violation` and the reason `data.rule-never` (G-25). `submit_record` has AI May Submit yes for `data-use` and no for
`data-mapping`; an AI client never submits a copy or retention decision, whichever subject kind it names. Record pages:
`/records/data-domain/<key>`, `/records/data-location/<id>`, `/records/data-mapping/<id>`, `/records/data-use/<id>`
under the host's record URL base.

## Conventions

- Field names are lowerCamelCase forms of document 20 logical names (section 3.3).
  Configuration references are keys, in fields ending `Key` or `Keys`. Operational references are GUIDs, in fields ending `Id`.
- A key is lowercase letters, digits and hyphens, 1 to 64 characters, starting with a letter.
- An engine meaning value is the kebab-case of the meaning, such as `in-progress` (document 21, section 8.3).
- `date` is a calendar date. `dateTime` is UTC and ends in `Z`. Each field follows its document 22 type.
- Input lists hold at most 100 items, and at least 1 when given.
- Inputs leave out what is not given. Outputs use null for a fact with no value, and leave out sections not asked for.
- Lists take `top` and `skipToken`. The output returns `skipToken` for the next page, absent on the last page.

## Annotations

Keys that start with `x-` are annotations. The JSON check allows them.

| Key | Where | Meaning |
|---|---|---|
| `x-tool` | Root of each tool schema | The tool name |
| `x-ai-write-states` | Root of each output schema | The AI write states, the same as `tools.json` |
| `x-entity` | ID and key fields | The document 20 entity, such as `O02 Business Application` |
| `x-untrusted` | Output text fields | Text people typed. The server lists these paths in `untrustedFields` |
| `x-max-from-setting` | `top`, `depth` | The setting that caps the value. The cap is configuration, so the schema has no maximum |

## AI write state rules

- Output schemas pin the status a write can leave. An intake draft is always draft. An application draft is
  always draft (registration). An owner change is always proposed. A tier assessment, a component link and a leave have no status.
- The other write tools: a decision proposal is always proposed; `submit_record` leaves submitted, or proposed for a decision;
  `propose_configuration_change` leaves a Configuration Draft in draft; `request_architecture_review` leaves a review that is due,
  never a decision. A data use, a data location and a data mapping an AI client saves are draft; a person's are
  registered unless the person asks for a draft. No output of the 30 write tools an AI client is served holds the value
  approved, accepted, active or activated. `answer_approval`, served only to a person, leaves the approval record approved or rejected, and reports the
  decision status the engine set after its recompute.
- No input carries a status, a tier, a stage or an outcome. The answer to `answer_tier_questions` says it is a suggestion.
- `tools.schema.json` refuses a tool name that starts with approve, reject, accept, authorize, activate, assign,
  grant, revoke, delete, remove or set. It refuses `destructiveHint` true and a read tool with a write state, and it
  requires a `person-only` tool to name the person entry alone. A write tool names its AI write states, or `read-only`
  alone for a preview-only write (`move_data_references`).
- `tools.json` holds the AI rules (`aiRules`), the nine AI write states (`person-only` among them) and the acts that are
  not tools (`notTools`).

## Working defaults

Search for `WORKING DEFAULT`. Each marker names an
[open design question](../../../docs/architecture/README.md#open-design-questions). The contracts follow the working
default until the question is decided. The places:

- Question 1: `common.schema.json` (engine meaning lists), `find_applications` input, `get_requirements` output.
- Question 4: `tools.json`, `tool-policy.seed.json`, `propose_configuration_change` input and output.
- Question 7: `tools.json`, `tool-policy.seed.json`, `record_deployment` input and output, the deployment request of the record endpoint.
- Question 8: `tools.json`, `tool-policy.seed.json`, `submit_record` input and output.
- Question 9: `tools.json`, `tool-policy.seed.json`, and the inputs and outputs of `save_application_draft`, `submit_record`,
  `request_architecture_review`, `record_document_link` and `confirm_document_checked`, plus the `link_component` input.
- Question 12: `tools.json`, `tool-policy.seed.json`, `record-endpoint.json` and its four request and response schemas,
  and the inputs and outputs of `record_deployment`, `record_deployment_correction` and `check_role_bindings`.

## Where these differ from the document 24 outlines

- `applicationId` is `businessApplicationId`, and `componentId` is `technicalComponentId`.
  Document 24, section 3.1, says the contract follows document 20, section 3.3. The document 25 parameter
  dictionary uses `businessApplicationId`, and document 24 tool 12 already does.
- `fromStage` is `fromStageKey`, as in document 21 (C23).
- Added because a caller needs them: `ownerAssignmentId` on owner rows (to pass as `endAssignmentId`),
  `technicalComponentId` on card components (for a retired outcome), `status` on the card's application,
  `skipToken` on list outputs, `confirmationCardSent` on `save_application_draft` (question 9),
  `retryAfterSeconds` on the error (section 3.5 says rate_limited carries a retry time), and `recordUrl` on the
  error: the record a refusal points to, such as the document link to update instead (wave 4b).
- A warning is an object with `message`, and optional `code` and `ref`, like the document 25 Reason record.
- Every write input takes `clientRequestId` and `validateOnly` (section 3.3), also where a tool outline leaves them out.
- `link_component` with outcome moved needs `replacesComponentId`.

## Open points

- Document 24 has no error code for an unexpected failure. Document 25 maps a bug to HTTP 500. Until document 24
  names one, the server core answers a bug as a JSON-RPC internal error, outside the envelope, that names only the
  correlation ID. The host gets the exception and the same correlation ID through a failure callback, so operators can
  see why the request failed.
- `draftsOlderThanDays` in `get_portfolio_summary`: document 24 does not say which setting sets the age. It is typed as a count.
- `businessOwner` and `technicalOwner` assume the owner role keys `business-owner` and `technical-owner`. They are null when the configuration has no such role.
- `score` in `check_duplicates` is typed 0 to 1, and URLs are capped at 2048 characters. Document 24 gives neither.

## How to check

From `src/`: `node tools/json-check.mjs`. It parses every file and compiles every schema. `tools/schema-map.json`
maps `tools.json` to `tools.schema.json` and `errors.json` to `errors.schema.json`, so the check validates both.
