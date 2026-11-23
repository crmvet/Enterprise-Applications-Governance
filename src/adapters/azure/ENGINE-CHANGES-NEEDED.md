# Engine and MCP core changes the Azure binding needs

The Azure binding changes nothing under `engine/` or `mcp/`. Where it needs something those projects do not offer yet,
the need is written here with its reason and the workaround this folder uses until the change lands.

## 1. The in-process backend always runs as an AI client (needed)

`InProcessBackend.Identity(BackendCaller)` is internal and always builds `CallerIdentity.AiClient` for a user, and
`OpenSession` reports `ai-client-for-person`. That is right for the MCP endpoint (document 24, section 4.5), but the web
app's REST API calls the same tools as a person, who must be stamped as actor kind person with no On Behalf Of
(document 22, section 2.4). A person's write stamped as an AI client would be forced to draft and marked AI-created.

- **Change.** Let the host choose the caller entry: for example `BackendCaller.Entry` (a `CallerEntry`, default
  `AiClient`) that `Identity` and `OpenSession` honor, with `McpServerCore` always passing `AiClient`. The REST API would
  pass `Person`.
- **Workaround here.** `POST /api/{tool-name}` serves only read tools, which stamp nothing. A write tool there answers
  `not_found` with a message that it is served on the MCP endpoint in this release. `GET /api/me` is built from the
  session the backend opens, without the actor kind.

## 2. Pipelines cannot run the record tools (useful)

The record endpoint has the same contract as `record_deployment` and `record_deployment_correction` (record endpoint
contract), but the tool handlers run only through the in-process backend, which cannot take an app-only caller, and
they check the owner relation a pipeline does not have.

- **Change.** A workload entry for those two tools, or a public mapper from `DeploymentReport` and its outcome to the
  tool's `data`.
- **Workaround here.** The endpoint validates the body against the record endpoint schemas, calls
  `EngineRuntime.RecordDeployment` as `CallerIdentity.Workload`, as the walkthrough does, and builds `data` from the
  outcome and the stored record to match `deployment.response.schema.json`. A test compares it with the MCP tool's
  answer for the same report.

## 3. The engine-to-error map is internal (useful)

`EngineErrorMap.Map` turns a runtime outcome into one of the eleven fixed error codes. The record and approval
endpoints need the same map.

- **Change.** Make it public.
- **Workaround here.** A small map in the host library that uses `ContractSet.Errors` (each code's engine reasons and
  HTTP status) and the outcome word, in the order document 24, B24-1 gives. A test checks it against the MCP path for
  the same refusals.

## 4. The logical model has no public reader (useful)

`LogicalModelFacts` is internal. The stores need the entity keys, names, kinds and history classes.

- **Change.** A small public, read-only view of the compiled model.
- **Workaround here.** The entity catalog in `Product.Azure.Stores` reads the same resource from the engine core
  assembly (the name ending `.logical-model.json`) and parses it itself.

## 5. Roles resolved twice for SESSION_CONTEXT (optional)

Row-level security needs the caller's role keys and actor kind in `SESSION_CONTEXT`, but the runtime resolves the
caller inside its call and the store ports do not carry it.

- **Change.** Pass the resolved actor to the store, for example an optional caller argument on `IRecordStore.Begin`.
- **Workaround here.** The host resolves the caller once more with the public `CallerResolver` against the cached
  snapshot and puts the result in an ambient context the SQL store reads when it opens a connection.

## 6. Product roles from app role values (optional)

Documents 22 (section 11.1) and 26 (section 5.6) read product roles from the token's `roles` claim when the client has
Entra ID P1, which avoids the group overage limit. The engine resolves roles only from group object IDs.

- **Change.** Let `CallerResolver` also match a Role Group Binding by its app role value.
- **Workaround here.** The API app registration emits the groups claim filtered to the groups assigned to the
  application. A token whose groups overflow is refused with a plain message.

## 7. Synchronous storage ports (later)

The ports are synchronous so the same engine runs in a Dataverse plug-in. On Azure each SQL round trip holds a thread.

- **Change.** Asynchronous port variants when load requires them.
- **Workaround here.** Synchronous SqlClient calls, short transactions, and the request body read asynchronously
  before the engine runs.

## 8. The MCP core reports no internal failure (useful)

`McpServerCore.Handle` turns an unexpected exception into a JSON-RPC internal error that names only the correlation ID,
and the exception goes nowhere, so operators cannot see why a request failed (for example an envelope that does not
match its output schema when `ValidateOutputs` is on).

- **Change.** An optional failure callback on `McpServerOptions` (exception and correlation ID) that the core calls
  before it answers.
- **Workaround here.** The host builds each request's options with `RethrowInternalErrors` on, catches the exception
  itself, logs it with the correlation ID (the full exception in Development; elsewhere its type and stack, never its
  message, which may quote data), sends the telemetry event, and answers with the same JSON-RPC internal error body the
  core sends.

## 9. The store cannot tell which engine operation a commit serves (optional)

Row-level security on Azure SQL lets the live configuration tables change only inside an activation (document 22,
section 11.3, `fn_ConfigWriter`), and the store writes the operation to `SESSION_CONTEXT` as `EngineOperation`. The
storage port's `Commit` carries no operation, so the store cannot know whether a transaction is the bootstrap, an
activation, a draft operation or another write.

- **Change.** Let the runtime name the operation of a transaction, for example an optional operation argument on
  `IRecordStore.Begin` (or a small commit context with the effect kinds the transaction applied), so the store writes
  the exact name and predicates such as document 22's `fn_DraftWriter` become possible.
- **Workaround here.** The row store infers `activation` when a commit inserts a Configuration Set Version, which only
  the bootstrap and an activation do (the engine's guard G-01), and `write` otherwise; seeding is `seed`. The draft
  predicates are reduced to the draft's own status (`fn_DraftOpen`).

## 10. The store sees only the row after the change (optional)

A block predicate sees either the stored row (before update) or the new row (after update), never both, so it cannot
check that a final Decision changes only Superseded By, or that a final Approval Record changes only Counts Toward
Requirement, the two writes the engine still makes on those rows (G-05, G-06).

- **Change.** None needed in the engine for correctness: its guard is the control. A later store could keep the
  allowed transitions in a stored procedure the app identity calls instead of a table update.
- **Workaround here.** `fn_DecisionOpen` lets a final Decision change while its Superseded By is empty, and
  `fn_ApprovalPending` lets a final Approval Record change in the approval runner's request (the only request that
  recomputes approvals). Both still refuse everything the engine never writes from any other request.

## 11. A refused write by the approval runner reads as an AI refusal (optional)

`errors.json` lists `approval.outcome-writer` under `ai_cannot_do_this`, and the error map tries the codes `errors.json`
names before it looks at who the caller is. So when the approval runner sends a second, different answer to an Approval
Record that already holds a final outcome, the engine refuses it with `approval.outcome-writer` and the approval outcome
endpoint answers 403 `ai_cannot_do_this`, although the caller is a workload and no AI is involved
(`EngineRefusalTests.AWriteTheEngineGuardRefusesComesBackWithItsReason` shows it).

- **Change.** Map an AI-only reason to `ai_cannot_do_this` only for an AI caller, and give the runner's refusal its own
  reason (for example a final-outcome reason mapped to `conflict`), so an operator reading the runner's log sees why.
- **Workaround here.** None needed for safety: the write is refused and nothing changes. The endpoint passes the
  engine's message through, so the log still names the cause.

## 12. The engine takes the approver's principal kind from the runner (needed)

Document 22 defines Approver Principal Kind as the responder's directory object type copied from the Principal
Reference, and only `user` counts. The engine's write guard reads it from the approval runner's input instead, so an
answer an agent user gave on an approval card would count if the runner reported it as a user's: the runner sees a
responder's object ID and may not know it belongs to an agent.

- **Change.** In the guard for an approval outcome, read the principal kind of the Approver from its Principal Reference
  when one exists, and count the answer only when both the reference and the input say `user`.
- **Workaround here.** The approval outcome endpoint looks up the Principal Reference of `approver` before the engine
  runs. When it records anything but `user`, that kind replaces the runner's value, so the engine stores the answer as
  recommendation only with `approval.non-user-principal`. A responder with no Principal Reference keeps the runner's
  value, and a non-user kind from the runner is never upgraded.
