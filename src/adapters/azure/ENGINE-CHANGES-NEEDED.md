# Engine and MCP core changes the Azure binding needs

The Azure binding builds on the engine (`engine/`) and the MCP server core (`mcp/`). Where it needs something those
projects do not offer, the need is written here with its reason and the workaround this folder uses until the change
lands. Changes that have landed stay listed at the end, with how this folder uses them now.

## Open

### 5. Roles resolved twice for SESSION_CONTEXT (optional)

Row-level security needs the caller's role keys and actor kind in `SESSION_CONTEXT`, but the runtime resolves the
caller inside its call and the store ports do not carry it.

- **Change.** Pass the resolved actor to the store, for example an optional caller argument on `IRecordStore.Begin`.
- **Workaround here.** The host resolves the caller once more with the public `CallerResolver` against the cached
  snapshot, on the same entry the request uses (the person entry for the web app, the AI client entry otherwise), and
  puts the result in an ambient context the SQL store reads when it opens a connection.

### 6. Product roles from app role values (optional)

Documents 22 (section 11.1) and 26 (section 5.6) read product roles from the token's `roles` claim when the client has
Entra ID P1, which avoids the group overage limit. The engine resolves roles only from group object IDs.

- **Change.** Let `CallerResolver` also match a Role Group Binding by its app role value.
- **Workaround here.** The API app registration emits the groups claim filtered to the groups assigned to the
  application. A token whose groups overflow is refused with a plain message.

### 7. Synchronous storage and live read ports (later)

The storage ports and the live read ports (`IDirectoryReader`, `IInventoryReader`) are synchronous so the same engine
runs in a Dataverse plug-in. On Azure each SQL round trip holds a thread, and so does each on-behalf-of exchange and
each Microsoft Graph or Power Platform API call of the directory reads.

- **Change.** Asynchronous port variants when load requires them.
- **Workaround here.** Synchronous SqlClient calls and short transactions; the request body read asynchronously before
  the engine runs; for the directory reads `HttpClient.Send`, a timeout per call, a time budget per tool and one retry on
  429.

### 9. The store cannot tell which engine operation a commit serves (optional)

Row-level security on Azure SQL lets the live configuration tables change only inside an activation (document 22,
section 11.3, `fn_ConfigWriter`), and the store writes the operation to `SESSION_CONTEXT` as `EngineOperation`. The
storage port's `Commit` carries no operation, so the store cannot know whether a transaction is the bootstrap, an
activation, a draft operation or another write.

- **Change.** Let the runtime name the operation of a transaction, for example an optional operation argument on
  `IRecordStore.Begin` (or a small commit context with the effect kinds the transaction applied), so the store writes
  the exact name and predicates such as document 22's `fn_DraftWriter` become possible.
- **Workaround here.** The row store infers `activation` when a commit inserts a Configuration Set Version, which only
  the bootstrap and an activation do (the engine's guard G-01), and `write` otherwise; seeding is `seed`. The draft
  predicates are reduced to the draft's own status (`fn_DraftOpen`). The approval outbox rows the same commit writes
  are the host's own table and are not engine rows.

### 10. The store sees only the row after the change (optional)

A block predicate sees either the stored row (before update) or the new row (after update), never both, so it cannot
check that a final Decision changes only Superseded By, or that a final Approval Record changes only Counts Toward
Requirement, the two writes the engine still makes on those rows (G-05, G-06).

- **Change.** None needed in the engine for correctness: its guard is the control. A later store could keep the
  allowed transitions in a stored procedure the app identity calls instead of a table update.
- **Workaround here.** `fn_DecisionOpen` lets a final Decision change while its Superseded By is empty, and
  `fn_ApprovalPending` lets a final Approval Record change in the approval runner's request or in a person's own answer
  request (the only requests that recompute approvals: an answer on the approval outcome endpoint, the approval refresh
  a claim runs, and `answer_approval` on the person entry, which names its record in `SESSION_CONTEXT` as
  `AnsweredApprovalId`). In a person's answer request both are tied to the decision of the record the request names:
  `fn_ApprovalPending` opens only that decision's records, and `fn_ApprovalOutcomeWriter` lets an answer stand only on
  that decision's records, stamped with the caller's own principal, and only on the record the request names (the
  person's own write) or on a record the engine rewrote (Performed By Actor Kind engine: the recompute's Counts Toward
  Requirement). `AnsweredApprovalId` is the caller's argument, read before the engine checks it, so the predicates never
  open anything beyond that one decision on its strength. Both still refuse everything the engine never writes from any
  other request.
  The engine's own expired and cancelled outcomes, and the restarts it opens, change pending rows or add new ones, which
  the predicate allows from any request.

### 16. Approver Holds Required Role checked by the engine for an owner role, and workload responders (optional)

OP-03 counts an answer when the stored Approver Holds Required Role is true, and only the approval runner writes it
(document 25, section 5.5). For an owner role the engine holds the facts itself (the current Owner Assignments of the
decision's application), so it can check the runner's value instead of trusting it. Likewise the engine knows every
Workload Grant: an answer whose approver is the writing workload itself, or any grant holder, is never a person's.

- **Change.** The approval writer guard sets Approver Holds Required Role to false for an owner role's answer when the
  approver holds no current Owner Assignment for that role on the day of Responded On, and refuses an answer whose
  approver is the calling workload or a Workload Grant principal.
- **Workaround here.** The approval outcome endpoint does both before the engine runs: it refuses a workload approver
  with 403 (nothing is written, and the record keeps waiting for a person), and it replaces a true Approver Holds
  Required Role with false for an owner role's answer from someone who holds no current assignment of that role. A
  product role's holders are the members of its Entra groups, which only the runner reads, so its value still stands as
  the runner sends it.

### 17. The same outcome from another responder reads as the same answer again (optional)

G-06 ("only once") compares the outcome only: when an Approval Record already holds approved and the approval runner
sends approved again, the engine answers that nothing changed, whoever the responder and whenever the answer was given.
A second person's answer to a request that already has its answer (a card sent again after a release, say) then reads
as a success.

- **Change.** Compare the responder and Responded On as well, and answer a different responder with
  `approval.outcome-final`, as a different outcome already is.
- **Workaround here.** The approval outcome endpoint answers 409 `conflict` before the engine runs when the record holds
  a final outcome and the answer has the same outcome but another responder or another time, unless the
  `Idempotency-Key` names a request the engine already recorded. With or without the key, nothing is written.

### 18. A late answer carries no warning (optional)

OP-03 does not count an answer given at or after Expires On, with the reason `approval.expired`. The reason's catalog
severity is information, and the envelope lists warnings only, so the runner's answer says that the answer does not
count and that the engine asked again, but not why.

- **Change.** Give `approval.expired` the warning severity when it judges an answer (not a pending record), or list the
  reasons that kept an answer from counting in the outcome's payload.
- **Workaround here.** None. `get_approval_status` shows the record's outcome, its Responded On and that it does not
  count; the claim that carried its card showed its Expires On.

### 19. A timely answer that arrives after the refresh expired its record (design needed)

OP-03 counts an answer given before Expires On. The approval refresh writes expired on a pending record as soon as a
claim runs at or after Expires On, and expired is final (G-06). An answer the person gave before Expires On that reaches
the product after that claim (the child flow posts it a few minutes later, or retries after a failure) is then refused
with 409 and recorded nowhere, although OP-03 would have counted it; the person must answer the restart's card. Before
the refresh existed, such an answer was recorded and counted.

- **Change.** One of: a grace period after Expires On before the refresh writes expired (a setting, at least the
  runner's poll interval plus its lease), so answers in transit land first; or let the approval runner's answer replace
  an engine-written expired outcome when its Responded On is before Expires On, cancelling the restart if the answer
  meets the requirement. Either changes document 25 (G-06, OP-03) and document 23 (S1 step 9).
- **Workaround here.** None. Guide A4 says that an answer reaching the product after its record expired is refused and
  that the person answers the new card.

## Done, and how this folder uses it

### 1. The caller entry (done)

`BackendCaller` takes a `CallerEntry`, and `McpServerOptions` has `Entry` (default `AiClient`), `PersonClientAppIds`
and `PersonClientLabel`. On the person entry the core admits only the declared web app client IDs (the verified `azp`),
skips the Known Client check and the AI-only gates, and keeps Tool Policy roles, the rate limit and the input schema.
The engine stamps the person, with no On Behalf Of; an agent user or agent identity keeps its kind on every entry.

- **Used here.** `POST /api/{tool}` and `GET /api/me` use the person entry when the token's `azp` is one of
  `Product:WebApp:ClientIds`, and the AI client entry for any other client. `/api/me` answers the actor kind and the
  person's name. The MCP endpoint is always the AI client entry. On the person entry the REST API serves the write tools
  too (D-36), checked by the core and the engine as on the MCP endpoint; for an AI client on the REST API a write tool
  answers `not_found`, because an AI client writes through the MCP endpoint.

### 2. The record tools' data (done)

`RecordEndpointAnswers.DeploymentData` and `CorrectionData` build the data of `record_deployment` and
`record_deployment_correction`, and the MCP tools answer through them.

- **Used here.** The record endpoint builds its answer with the same functions, so the MCP answer and the record
  endpoint answer come from one place. Its responses still validate against the record endpoint response schemas.

### 3. The engine-to-error map (done)

`EngineErrorMap` is public, with `Map(RuntimeOutcome, ContractSet, bool aiCaller)`.

- **Used here.** The record endpoint and the approval outcome endpoint map the engine's refusals through it.

### 4. The logical model (done)

`LogicalModel.Compiled` (namespace `Product.Engine.Core.Model`) is a read-only view of the logical model compiled into
the engine: entities, columns and unique keys.

- **Used here.** The stores' entity catalog is built from it, so the stores and the write guard read the same model.

### 8. The internal failure callback (done)

`McpServerOptions.OnInternalFailure` receives the exception and the correlation ID before the core answers a server
failure as a JSON-RPC internal error.

- **Used here.** The host logs the failure with the correlation ID there (the full exception in Development; elsewhere
  its type and stack, never its message, which may quote request data). The host no longer catches the exception itself.

### 12. The approver's principal kind (done)

For an approval outcome, the runtime fetches the approver's Principal Reference, and the guard stores its kind: a
non-user reference makes the answer recommendation only, and a user reference never upgrades a non-user kind from the
runner.

- **Used here.** The approval outcome endpoint still replaces a non-user kind before the engine runs, as a second layer.

### 11. A refused write by the approval runner no longer reads as an AI refusal (done)

The engine answers the approval runner's different second answer for a record that already holds a final outcome with
its own reason, `approval.outcome-final`, a conflict with the stored answer. `EngineErrorMap` gives
`ai_cannot_do_this` only to an AI caller: any other caller refused for a reason that `errors.json` lists under it gets
the code of the reason's catalog category.

- **Used here.** The approval outcome endpoint passes a different outcome for a settled record (an answer, expired or
  cancelled) to the engine and answers its refusal through the map: 409 `conflict`. The endpoint no longer answers that
  case itself. It keeps one check of its own (item 17).

### 13. One Approval Record per required answer (done)

The engine opens as many pending Approval Records as each counting requirement still needs to reach its Minimum Count
(document 23, S1 step 3; document 25: "everyone must approve" is met at Minimum Count too), the first step only of a
sequential group, and one record per consultation. OP-03 counts one person once within a requirement, in time order,
so a Minimum Count of 2 means two different people. When a decision settles, the engine cancels the records still
pending in the same transaction. When an answer does not count (from an AI client or an agent, from someone who does
not hold the role, given after Expires On, or a second answer from someone who already counts), the recompute opens a
new record for the requirement in the same transaction, and so does the next step of a sequential group.

- **Used here.** Nothing changed in the outbox: each new pending record gets its outbox row and each cancelled one is
  settled in the engine's own commit, through the commit hook. The runner sends one first-to-respond card per record. A
  claim item for an owner role leaves out the holders whose approval already counts for the same requirement, so the
  second card of a two-answer rule asks the others. The approval outcome endpoint's answer lists the records the
  engine opened (`openedApprovalRecordIds`) and cancelled (`cancelledApprovalRecordIds`) in the same transaction, and
  the decision's status (`decisionStatus`). For a product role the runner lists the group's members and cannot leave
  out who already answered, so such a person may get the next card too; their second answer does not count, and the
  engine asks again.

### 15. A person answering their own approval on the product's page (done, D-38)

D-38 makes the product's decision page the one place a person answers, with the token proving who answered; Teams and
Outlook only link to it. The engine now has the approval answer check (`ApprovalAnswerOperation`, a pure operation
with fixtures under `fixtures/approval-answer`) and `EngineRuntime.AnswerApproval` (`ComposedCall.AnswerApproval` in
a composed call): the responder is the caller, a person signed in as a user; the check refuses, before anything is
written, an answer from anyone the request does not ask, one a separation rule would not count, one to a request that
is answered, expired, cancelled or whose decision is settled, and one without a typed reason where needed. OP-08's G-06
gains a person path that accepts the person's own write only with the check's basis, which no request carries. The
recompute is the approval runner's. The MCP core serves the new tool `answer_approval` only on the person entry.

- **Used here.** `POST /api/answer_approval` on the person entry serves it; an AI client gets `ai_cannot_do_this` on the
  REST API and on the MCP endpoint (which does not list the tool), and an app-only token is refused before any tool runs.
  For that request only, the host enters a store call context with `AnsweredApprovalId`, and the Azure SQL store writes
  it to `SESSION_CONTEXT`; `fn_ApprovalPending` and `fn_ApprovalOutcomeWriter` take that request as they take the
  runner's (item 10). The commit hook settles the record's outbox row in the same transaction, so a card the runner
  already sent is answered for the outbox too. Still open: step-up sign-in for tier 3 and configuration changes (a
  setting, off in the demo; document 26, B26-7 proposes `approval-step-up`), the web app's decision page, and document
  23, S1, which D-38 rewrites.

### 14. Approval expiry and restarts (done)

`EngineRuntime.FindDueApprovals` lists the decisions with approval work due (a pending record at or past its Expires
On, or a pending record of a decision that is no longer proposed), and `EngineRuntime.RefreshApprovals` brings one
decision up to date: it writes expired on the records past Expires On, recomputes, and opens the restarts, or cancels
the pending records of a settled decision. Only the approval runner, holding a current approval writer grant, may call
either (`approval.refresh-not-allowed` otherwise). An answer whose Responded On is at or after Expires On is kept but
does not count (`approval.expired`), and the recompute restarts the requirement.

- **Used here.** Every claim (`POST /engine/v1/approval-runner/claims`) first runs the refresh as the caller, the
  approval runner: up to `Product:ApprovalRunner:MaxRefresh` decisions (default 50) that `FindDueApprovals` lists, in
  its order, and any other decision whose claim candidate it finds past Expires On or settled. The engine's own
  commits settle the outbox rows of expired and cancelled records and add the rows of the restarts, so the same claim
  offers the restarts it opened first. The claim's answer counts what the refresh did (`refresh`), and the log says the
  same. The approval outcome endpoint no longer refuses a late answer itself: it passes it to the engine, which keeps it
  without counting it and asks again. An answer to a record the engine already expired is the engine's conflict (409).
  The refresh runs only when the runner polls: with the runner's routes off, or its flows stopped, no record is
  written as expired, although OP-03 already reads a pending record past Expires On as expired. Document 23, S2 has a
  daily timer for the due-state refresh; the Azure binding has none yet.
