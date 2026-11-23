# mcp/contracts

The MCP tool contracts, from document 24. Every schema is JSON Schema draft 2020-12.

It holds the response envelope, the error codes, the tool index, the input and output schemas for all 51 tools,
the three R1 resources, the Tool Policy seed rows (proposed C41) and the record endpoint contract for pipelines.

## Files

| File | What |
|---|---|
| `common.schema.json` | Shared types: IDs, keys, dates, paging, labels, principals, the engine meaning lists |
| `envelope.schema.json` | The response envelope: success, failure, error, the write result, performed by, engine stamp |
| `errors.json` | The 11 fixed error codes: meaning, retryable, HTTP status, client action, engine reason examples |
| `errors.schema.json` | Schema for `errors.json` |
| `tools.json` | Index of all 51 tools: number, name, toolset, read or write, default roles, AI write state, release, annotations, contract files |
| `tools.schema.json` | Schema for `tools.json` |
| `tools/<tool>.input.schema.json` | One per tool: the tool's arguments |
| `tools/<tool>.output.schema.json` | One per tool: the envelope, with `data` typed for that tool |
| `resources.json`, `resources.schema.json` | The three R1 resources (`guideline://G{n}`, `configuration://in-force`, `playbook://chapter/{n}`; document 24, section 6.12) |
| `resources/<resource>.content.schema.json` | One per resource: the JSON a read returns |
| `tool-policy.seed.json` | 51 Tool Policy seed rows, one per tool. Checked with `config/schema/config-bundle.schema.json#/$defs/entityFile` (C41, a proposal) |
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

## Entry points

The same tools and the same envelope serve two entry points. The entry decides how the engine stamps a write, never a
field the caller sends.

| Entry | Who calls | Actor kind | Notes |
|---|---|---|---|
| MCP endpoint | An AI client acting for the signed-in person | `ai-client-for-person` | Every write is AI-created and names the person as On Behalf Of. The Known Client check, Max AI State and AI May Submit apply |
| The product's web app, through its API | The person | `person` | Only the web app's own client IDs, which the host declares, may call. Max AI State and AI May Submit do not apply. Roles, the rate limit and the input schema apply as on MCP |

`whoami` answers the actor kind of the entry: `ai-client-for-person` on the MCP endpoint, `person` through the web app.
An agent user or agent identity holds no role on either entry. No tool approves on either entry.

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
  never a decision. No output of the 24 write tools holds the value approved, accepted, active or activated.
- No input carries a status, a tier, a stage or an outcome. The answer to `answer_tier_questions` says it is a suggestion.
- `tools.schema.json` refuses a tool name that starts with approve, reject, accept, authorize, activate, assign,
  grant, revoke, delete, remove or set. It refuses `destructiveHint` true and a read tool with a write state.
- `tools.json` holds the AI rules (`aiRules`), the eight AI write states and the acts that are not tools (`notTools`).

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
  and `retryAfterSeconds` on the error (section 3.5 says rate_limited carries a retry time).
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
