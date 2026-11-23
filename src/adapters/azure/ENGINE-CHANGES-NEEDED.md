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
  `fn_ApprovalPending` lets a final Approval Record change in the approval runner's request (the only request that
  recomputes approvals). Both still refuse everything the engine never writes from any other request.

### 11. A refused write by the approval runner reads as an AI refusal (optional)

`errors.json` lists `approval.outcome-writer` under `ai_cannot_do_this`, and the error map tries the codes `errors.json`
names before it looks at who the caller is. So when the approval runner sends a second, different answer to an Approval
Record that already holds a final outcome, the engine refuses it with `approval.outcome-writer`, which maps to 403
`ai_cannot_do_this`, although the caller is a workload and no AI is involved.

- **Change.** Map an AI-only reason to `ai_cannot_do_this` only for an AI caller, and give the runner's refusal its own
  reason (for example a final-outcome reason mapped to `conflict`).
- **Workaround here.** The approval outcome endpoint answers a different answer for a record that already holds a
  final outcome with 409 `conflict` before the engine runs, when the runner sends its `Idempotency-Key` (it always
  does). Without the key, the engine's refusal comes back as before. Either way nothing changes.

### 13. One Approval Record per requirement (needed for some rules)

The engine opens one Approval Record for each required approval of the winning rule group, and each record holds one
answer. A rule whose Minimum Count is above 1 (the strict separation overlay asks two change board members) therefore
gets one record and can never be met, and "everyone must approve" and sequential rules collapse to one answer.
Document 23 (S1, step 3) asks for one record per required response.

- **Change.** Open as many Approval Records as the rule's Minimum Count (or one per named approver for "everyone"
  rules), and count them in OP-03 as document 23 describes.
- **Workaround here.** None. The approval runner sends one first-to-respond card per record (`flows/README.md`), and a
  decision under such a rule stays proposed. The configuration health check should flag such rules once the change is
  designed.

### 14. Approval expiry and restarts (needed)

Document 23 (S2) has the engine write an expired outcome when an Approval Record passes its Expires On, and restart the
approval with a new pending record. Neither is built. An answer stored as recommendation only (from an agent, or from
a responder who does not hold the role) also leaves its requirement unmet until a restart sends a new card.

- **Change.** The due-state refresh writes the expired outcome, and a restart opens a new pending record for the same
  requirement.
- **Workaround here.** The approval outcome endpoint refuses an answer given at or after Expires On with 409 and settles
  the outbox row as expired, so no new card goes out for it. The record itself stays pending in the engine.

### 15. A person answering their own approval on the product's page (design needed)

The four pages are read-only, and an approval answer reaches the engine only through the approval runner (document 26,
AN-01 and AN-02; document 23, S1). Working default Q19 offers the product's own page as the place to answer only where a
client's security review asks for token-level evidence of who answered.

- **Change.** A guarded engine path for a person answering their own Approval Record (the responder is the signed-in
  person, as the token proves), a change to the `fn_ApprovalPending` predicate for that path, a page specification,
  step-up sign-in for the answer, and a product owner decision on AN-02's wording. This goes to the next design pass as
  a proposal.
- **Workaround here.** None: the REST API serves the read tools only, and the approval outcome endpoint takes only the
  approval runner's app-only token.

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

## Done, and how this folder uses it

### 1. The caller entry (done)

`BackendCaller` takes a `CallerEntry`, and `McpServerOptions` has `Entry` (default `AiClient`), `PersonClientAppIds`
and `PersonClientLabel`. On the person entry the core admits only the declared web app client IDs (the verified `azp`),
skips the Known Client check and the AI-only gates, and keeps Tool Policy roles, the rate limit and the input schema.
The engine stamps the person, with no On Behalf Of; an agent user or agent identity keeps its kind on every entry.

- **Used here.** `POST /api/{tool}` and `GET /api/me` use the person entry when the token's `azp` is one of
  `Product:WebApp:ClientIds`, and the AI client entry for any other client. `/api/me` answers the actor kind and the
  person's name. The MCP endpoint is always the AI client entry. The REST API still serves the read tools only, because
  the four pages are read-only; a write tool there answers `not_found`.

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
