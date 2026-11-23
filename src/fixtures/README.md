# fixtures

One JSON file per case at `fixtures/<operation>/<case>.json` (document 25, section 10). Fictional data only. Each file validates against `config/schema/fixture.schema.json`, but the replay records in `fixtures/replay/`, which validate against `config/schema/replay-record.schema.json` (section "fixtures/replay" below).

## Run them

```
cd src
dotnet engine/Product.Engine.Cli/bin/Release/net10.0/Product.Engine.Cli.dll run-fixtures fixtures
```

`node tools/ci.mjs` runs the same command as the `fixtures` check. The test project runs them too.
The runner needs no test framework. It prints PASS, FAIL or SKIP per fixture. Every operation is built, so no fixture is skipped.

How a fixture is judged:

1. The file must match the fixture schema, and `case` must match the file name.
2. The operation runs. An operation that is not built is skipped, not failed (today every operation is built).
3. `expected.outcome` must equal the outcome word.
4. `expected.payload` is matched as a subset, or exactly with `payloadMatch: exact`.
5. `expected.reasons` is matched by code, plus severity, entity, ref and ruleId when given. `reasonsMatch: exact` allows no other reason.
6. `expected.trace` is matched by entity, key and role. `traceMatch: includes` (the default) asks for at least these rows; `exact` allows no other row. Order does not matter.
7. `ruleIds` must hold: in a failing case (OP-09 `invalid`; rebase `conflict` or `invalid`) each listed rule reports an error; in a passing case (OP-09 `valid`; rebase `same-base` or `rebased`) none does.

## OP-09 fixtures

The case names start with the rule they break, such as `v07-retired-reference`. A case that ends in `-allowed` or `-valid` shows that the rule passes.

What the snapshot can be:

| Form | Meaning |
|---|---|
| `presetKey` and `overlays` | A full preset and overlays from `config/presets` or `fixtures/presets`. Overlays apply in order |
| `bundle` | A small snapshot written inline |
| `operations` | Draft operations applied after the preset or bundle. The preset alone is then the active version for the diff |
| `raw` | Any document. A raw patch document (`manifest.kind` patch) is a draft applied to `input.activeSnapshot`. Use it for a broken patch the fixture schema would refuse as inline operations, such as a bad key |

`input.mode` is `preset-build` (the default, V44 on), `draft` (a working set: every row gets an origin, and the result compiles to a snapshot) or `activation` (V34 and V42 run). `input.activeSnapshot` is the version to compare with (diff, V08, V16, V33, V34).

The facts OP-09 cannot read from the configuration:

| Field | Rule | Meaning |
|---|---|---|
| `context.asOfUtc` | V28 | The validation date. A workload grant expires at most `workload-grant-max-days` after it |
| `input.baseVersionNumber` | V42 | The version the draft started from. Without it, the raw patch's manifest gives it |
| `input.activeSnapshot.versionNumber` | V34, V42 | The active version (default 1). Without an active snapshot, the base version stands in |
| `facts.rowsInUse` | V08, V16 | Rows that operational records use, as `[{ "entity", "key" }]` |
| `facts.isRebased` | V42 | True when activation rebased the draft onto the active version |
| `facts.approvalRulesVersionNumber` | V34 | The version whose rules created the approvals of the change decision |
| `facts.principals` | V35 | The kind of each directory object, as `[{ "objectId", "principalKind" }]` |
| `facts.snapshotCharacterLimit` | V41 | The binding's limit for one snapshot. Default 1,048,576 (Dataverse) |

A case where a rule only warns ends in `-warning` (V24, V29, V37, and the engine check `vocabulary.source`). Its outcome is `valid`, and `expected.reasons` lists the warning.

### Engine checks beyond V44

Document 25, section 5.11 lists engine checks that have a reason code but no V-ID yet. Their issues carry no `ruleId`, so their cases cannot use `ruleIds`. Instead the case name starts with the reason code, dots written as dashes, and the case carries the tag `engine-check`. A test checks that each engine check has a failing case that reports its code and a passing case (ending in `-valid`) that does not.

| Reason code | Severity | Failing case | Passing case |
|---|---|---|---|
| `role.config-admin` | error | `role-config-admin-second-role` | `role-config-admin-valid` |
| `standard-model.type` | error | `standard-model-type-emergency` | `standard-model-type-valid` |
| `standard-model.condition-kind` | error | `standard-model-condition-kind-unknown` | `standard-model-condition-kind-valid` |
| `vocabulary.source` | warning | `vocabulary-source-missing-row-warning` | `vocabulary-source-valid` |

`snapshot.schema-unsupported`, the fifth engine check, belongs to the snapshot compiler. The core tests cover it.

## Rebase fixtures

`fixtures/rebase/` holds the cases of document 21, section 4.5: a draft started from one version, and another version is active now. The operation name `rebase` and its outcome words are proposals of the fixture schema.

| Field | Meaning |
|---|---|
| `snapshot` | The active version, with its `versionNumber` |
| `input.baseSnapshot` | The version the draft started from, with its `versionNumber` |
| `input.draft` | The draft patch. Its manifest names the base version |
| `facts`, `context.asOfUtc` | The OP-09 facts the revalidation reads, as for OP-09 |

| Outcome | Meaning |
|---|---|
| `same-base` | The draft starts from the active version. Validated again in activation mode; it may activate |
| `rebased` | The base moved and no operation overlaps. The operations were re-applied to the active version and the result is valid. The payload holds the patch as applied, the updated diff and `diffChanged` |
| `invalid` | No overlap, but the result fails OP-09, so it cannot activate |
| `conflict` | An operation touches a row that changed in between. Refused with V42 (`config.base-moved`), one reason per overlapping row |
| `invalid-input` | The request cannot be judged, such as a base version above the active one |

What an operation touches: its own entity and key; the rows a wildcard row of that key expanded into; for a child operation, its parent row; for an upsert of a parent, the child rows its values name; for a retire of a parent, every child row. Everything after the first goes beyond the literal wording of section 4.5. Each makes the rebase refuse more, never merge more.

## OP-01 to OP-05 fixtures

`fixtures/op-01` to `fixtures/op-05` hold the cases of document 25, sections 5.3 to 5.7 (part 1). The runner sends each through `OperationJson` in the engine core, the same wire path an adapter uses.

| Folder | Operation | Cases | Outcomes in the cases | Reason codes the cases expect |
|---|---|---|---|---|
| `op-01` | Evaluate tier | 15 | computed, incomplete, invalid-input, unavailable | `tier.incomplete`, `tier.proposed-lower`, `tier.question-options`, `tier.no-applicable-questions`, `snapshot.hash-mismatch`, `input.unknown-key`, `input.malformed` |
| `op-02` | Resolve required approvals | 24 | required, engine-accept, refused, invalid-input, unavailable | `decision.engine-accepted`, `standard-model.does-not-qualify`, `rule-group.ambiguous`, `approval-rule.consultation-only`, `decision-right.accountable`, `setting.locked-changed`, `input.required`, `input.unknown-key` |
| `op-03` | Evaluate approval status | 31 | satisfied, pending, rejected, expired, invalid-input, unavailable | every `approval.*` code of the approver kind rule and the separation test, `approval-rule.zero-count`, `input.required` |
| `op-04` | Evaluate gate decision | 30 | not-applicable, allow, warn, stop, allow-with-exception, invalid-input, unavailable | the four flag codes `gate.*`, `gate.emergency-exception`, `approval.separation-flagged`, `gate-policy.missing`, `gate-policy.coverage`, `gate-policy.emergency-rule`, `input.required`, `input.unknown-key` |
| `op-05` | Check change window | 11 | inside, outside, no-window, invalid-input | `window.outside`, `window.none`, `input.malformed` |

How these cases are built:

- **The snapshot** is a preset (with overlays and inline operations) or a bundle, composed as a working set and compiled, as activation leaves it in force. It is **not validated first**, so a case can hold a broken configuration and prove that the engine refuses and reports it (`unavailable`) instead of falling back (document 25, rule 10). OP-05 reads no configuration: its snapshot is `{ "none": true }`.
- **`input` and `facts`.** `input` is what the caller sends; `facts` is what the adapter fetches (document 25, section 3.3). The runner joins the two into the operation's wire input, and refuses a field given in both. The fixture schema keeps each field on its side: OP-02 `standardChange` (the OP-12 result); OP-03 `requiredApprovals`, `records`, `parties` and `acceptedExceptionDecisionTypeKeys`; OP-04 `allowedRouteKeys` and `change` (with the OP-14 result as `linkedAuthorization`). OP-01 and OP-05 fetch nothing. The core's wire readers are as strict as the schema: an unknown field is `input.malformed`.
- **Context.** `configurationVersion` and `snapshotHash` default to the compiled snapshot's. A case that gives another hash is refused with `snapshot.hash-mismatch`.
- **Error outcomes** follow document 25, section 7: `invalid-input` (an `input.*` code), `refused` (a rule refuses the request as asked: `standard-model.does-not-qualify`), and `unavailable` (a broken configuration at run time). Their payload is empty.
- **Two cases are unit tests instead,** because the fixture schema rightly refuses the broken value as an inline operation: a Minimum Count of 0 on an approval rule in OP-02 (`OperationTests.AZeroMinimumCountForcedIntoASnapshotIsRefusedAtRunTime`), and an unreadable snapshot schema version (the compiler; core tests). OP-03 has a zero-count fixture, because there the requirement arrives as a fact.
- **Working defaults.** Cases resting on an open question list it in `workingDefaults`: 4 (the instructing admin accepts their own AI's draft), 7 (AI-only code approvals) and 8 (the locked list).

## OP-06, OP-07, OP-10 and OP-11 fixtures

`fixtures/op-06`, `op-07`, `op-10` and `op-11` hold the cases of document 25, sections 5.8, 5.9 and 5.12 (part 2). They run the same way as the part 1 cases: through `OperationJson`, with `input` and `facts` joined by the runner and the snapshot compiled but not validated.

| Folder | Operation | Cases | Outcomes in the cases | Reason codes the cases expect |
|---|---|---|---|---|
| `op-06` | Evaluate documentation completeness | 23 (17 answers, 6 refusals) | complete, incomplete, invalid-input, unavailable | `documentation.missing`, `documentation.draft`, `documentation.stale`, `input.malformed`, `input.unknown-key`, `ref.unresolved`, `snapshot.hash-mismatch` |
| `op-07` | Evaluate gate readiness | 17 (13 answers, 4 refusals) | ready, not-ready, invalid-input, unavailable | the four `gate.requirement-*` codes, `gate.not-ready`, `input.required`, `input.unknown-key`, `snapshot.hash-mismatch` |
| `op-10` | Walk impact | 21 (15 answers, 6 refusals) | walked, invalid-input, unavailable | `impact.cycle-detected`, `impact.depth-capped`, `input.malformed`, `input.required`, `input.unknown-key`, `setting.required` |
| `op-11` | Compute due dates and reminders | 20 (14 answers, 6 refusals) | computed, invalid-input, unavailable | `review.no-cadence`, `reminder.no-rule`, `input.contract-value-unknown`, `input.malformed`, `input.required`, `input.unknown-key`, `rule-group.ambiguous` |

What each side holds:

| Operation | `input` (the caller) | `facts` (the adapter) |
|---|---|---|
| OP-06 | `applicationKindKey`, `tierKey` (absent while the application has no tier), `lifecycleStageKey` | `links`: the Document Links (O19). `status` is draft, current or stale; missing is computed, and a stored missing is refused |
| OP-07 | `gateKey`, `tierKey` (absent while there is no tier), `applicationKindKey` | `acceptedDecisionTypeKeys`, `documentation` (the OP-06 result, passed as it came), `doneReviewTypeKeys`, and two proposals: `assignedOwnerRoleKeys` (for owner-assigned) and `triggeredGateRequirementKeys` (C10 Triggers Json) |
| OP-10 | exactly one of `startApplicationId` and `startComponentId` (a proposal, after document 24, tool 7), `direction`, `maxDepth` | `edges`: the Dependency rows (O04), each to one application or one component |
| OP-11 | `subjectKind`, `dueOnUtc`, `doneOnUtc`, and for a review `reviewTypeKey` and `tierKey` | none |

Notes on the cases:

- **Missing is computed** (working default, 28 issue 16). `missing-computed-without-link` has no link at all, and the requirement is missing. `stored-missing-invalid-input` proves a stored missing is refused. The fixture schema allows `missing` in a link only so that case can exist.
- **The best link** is the newest link tied to the requirement, else the newest of the document type, by Last Updated On, then Last Checked On, then the smaller ID (`tied-link-wins-over-newer`, `newest-of-type-when-not-tied`, `tier-change-reuses-link-of-other-requirement`).
- **Stale** means stored current and past Last Checked On plus Freshness Days. A link never checked counts from Last Updated On, and a current link with neither date counts as stale (`never-checked-counts-from-update`, `current-without-dates-is-stale`): both are proposals.
- **The walk** is breadth first per direction, so a node is listed once per direction at its shortest depth, ties to the smaller ID. A loop is a real cycle among the nodes the walk saw, not just a node met twice: a diamond is no loop (`shortest-path-wins`). `truncatedAtDepth` says more lay beyond the depth; `impact.depth-capped` names the setting when the setting, not the request, set the depth.
- **Reminders** come from every active C26 rule of the subject kind, on whole UTC days: the first reminder Lead Days before the due date, repeats every Repeat Days, and the escalation on Escalate After Days past it, which replaces a reminder that falls on the same day. For a review, the cadence's lead and escalate days replace the rule's (`escalate-cadence-overrides-rule`). `reminderNumber` counts the rule's steps, so flows key on item, rule and number. Only today's actions are listed; `nextActionOnUtc` says when the next one falls.
- **Working defaults.** Four OP-07 cases count a current gate-relevant document and list question 9.

Three broken configurations are unit tests in `OperationPart2Tests.cs`, because the fixture schema rightly refuses them as inline operations: a gate requirement with two targets (`ref.exactly-one`), a stage without a stage rank (`shape.schema`) and a negative lead days (`value.range`).

## OP-12, OP-13 and OP-14 fixtures

`fixtures/op-12`, `op-13` and `op-14` hold the cases of document 25, sections 5.13 to 5.15 (part 3). They run the same way as the part 1 and part 2 cases: through `OperationJson`, with `input` and `facts` joined by the runner and the snapshot compiled but not validated.

| Folder | Operation | Cases | Outcomes in the cases | Reason codes the cases expect |
|---|---|---|---|---|
| `op-12` | Evaluate a standard change | 32 (27 answers, 5 refusals) | qualifies, does-not-qualify, invalid-input, unavailable | `standard-model.qualifies`, `standard-model.condition-not-met`, `standard-model.condition-as-reported`, `standard-model.not-approved`, `standard-model.type`, `standard-model.no-candidate` (proposed), `input.unknown-key`, `input.required`, `input.malformed`, `snapshot.hash-mismatch` |
| `op-13` | Compare a gap check result | 31 (24 answers, 7 refusals) | compared, invalid-input, unavailable | `gap-check.partial-run`, `gap-check.as-reported`, `gap-check.item-ignored`, `input.unknown-key`, `input.malformed`, `input.required`, `snapshot.hash-mismatch` |
| `op-14` | Evaluate linked-mode authorization | 27 (24 answers, 3 refusals) | authorized, not-authorized, not-applicable, invalid-input, unavailable | `authorization.number-only`, `authorization.no-evidence`, `authorization.ai-not-counted`, `authorization.not-attester`, `authorization.wrong-source`, `authorization.revoked`, `authorization.reported-later` (proposed), `input.unknown-key`, `input.malformed`, `snapshot.hash-mismatch` |

What each side holds:

| Operation | `input` (the caller) | `facts` (the adapter) |
|---|---|---|
| OP-12 | `changeTypeKey`, `standardChangeModelKey` (the model the requester names) | `change` (StandardChangeFacts: `tierRank`, `targetStageKeys`, `affectedApplicationCount`, `componentKindKeys`, `windowMinutes`, `statedConditionKeys`) and `models` (each candidate model's `approvedByDecisionStatus`) |
| OP-13 | `gapCheckRunId`, `coveredEstateKeys`, `coveredComponentKindKeys` (optional: a subset of the covered kinds), `isFinalPage`, `isLiveQuery` (a proposal), `items` | `components` (O03 in covered estates and kinds), `claims` (O23 for the keys in play, with the proposed `timesSeen` and `lastSeenRunId`), `seenThisRun` |
| OP-14 | `changeManagementMode`, `hasExternalChangeNumber` | `facts` (the Authorization Facts, with the proposed `answeredOnConfirmationCard`) and `attesterRoleKeys` (a C29 role or a C28 owner role) |

Notes on the cases:

- **Condition kinds.** Each of the six kinds has a passing case (`<kind>-met`, at the limit) and a failing case (`<kind>-not-met`), each on a model with that one condition. A fact the change does not state never meets a condition (`unstated-facts-never-meet-conditions`): an empty target or component list cannot be shown to stay inside the allowed keys.
- **The approving decision.** A model whose Approved By Decision is proposed or superseded, has no status given, or names no decision never qualifies (`model-approval-*`, `model-without-approving-decision-never-qualifies`). An emergency change type never qualifies (`emergency-change-type-never-qualifies`), known by Allows Authorization After as in the engine check.
- **Which model.** A named model is the only one tried (`named-model-wins`, `named-model-fails-others-not-tried`); otherwise the first qualifying model by key wins and the others drop out (`first-by-key-when-none-named`). A named model of another change type, or a retired one, is no candidate (`standard-model.no-candidate`). The C21 Tier column is tested as one more max-tier-rank condition with no index (`model-tier-column-limits-tier`).
- **The claim lifecycle.** The Claim Key is `<finding kind>|<source type>|<External Id>`, with source type and External Id lowercased. A second run raises Times Seen, a later page of the same run does not (`second-run-raises-times-seen`, `same-run-later-page-keeps-times-seen`); a dismissed or claimed claim is updated and keeps its status, because an update carries no status (`dismissed-claim-stays-dismissed`, `claimed-claim-stays-claimed`). Vanished claims come only on the final page, never for a retired, URL-only, uncovered or already seen component. `plan-holds-no-inventory-copy` matches the whole plan exactly: only component IDs for registered items and only stored claim fields for new claims.
- **Linked authorization.** A recorded number alone never authorizes (`external-number-alone-not-authorized`, tag `invariant`). The latest counting fact decides, and at the same moment a removal wins (`same-moment-removal-wins`). Every failed test of a fact is listed, in a fixed order: AI, evidence, source, attester (`every-failed-test-is-listed`). A fact an AI client or agent identity recorded never counts and never revokes (`ignored-later-fact-does-not-revoke`). A fact dated after the evaluation moment never authorizes (`future-dated-authorization-does-not-count`; `authorization.reported-later`, proposed), so a clock that runs ahead or a forged date cannot outrank a cancellation; a removal dated later still removes (`future-dated-cancellation-still-removes`), because the engine never guesses in favor.
- **Hand-over.** `op-02/standard-change-op-12-payload-as-is` and `op-04/linked-op-14-payload-as-is` (and `linked-op-14-number-only-missing-approval`) pass the OP-12 and OP-14 results on unchanged: OP-02 and OP-04 now take the whole payload.
- **Working defaults.** OP-12 cases list question 8 (the locked list) and, for the condition kinds, question 1. OP-14 cases about the ITSM step list question 12 (the step is a workload holding the proposed authorization-reporter role).

Some cases are unit tests in `OperationPart3Tests.cs` instead, because the fixture schema rightly refuses them inline: an unknown condition kind (`standard-model.condition-kind`), four malformed conditions and a model with no conditions (`json.schema`), a model whose Tier does not exist (`ref.unresolved`), three inventory items that each carry a property a claim does not store (`input.malformed`), and a Source Type Hint shared by two kinds (the item is ignored, never guessed).

## OP-08 fixtures (the write guard)

`fixtures/op-08` holds the cases of document 25, section 5.10: G-01 to G-17, the approver kind rule, the erasure effect, the delete policy of document 22, section 2.5, and RA-06 of document 26, plus the history classes of document 22, section 2.3 and the column check. They run through `OperationJson` like the other evaluation operations. 170 cases: 60 allowed, 91 refused, 18 invalid-input, 1 unavailable. The 40 cases tagged `hardening-1` (10 allowed, 25 refused, 5 invalid-input) hold 17 adversarial probes of the write guard and a case for every history class. The 22 cases tagged `hardening-2` (10 allowed, 8 refused, 4 invalid-input) hold a second set of adversarial probes and an allowed case beside each rule, so each fix is shown to refuse no more than it should.

What each side holds:

| Side | Fields |
|---|---|
| `input` (the caller) | `entity` (the entity key, such as `decision` or `tier`), `operation` (create, update or delete), `rowId`, `after` (the changed columns, lowerCamelCase, configuration references as keys), `idempotencyKey`, and `nestedEffect` (a proposal: the engine effect an engine-nested write belongs to, set by the adapter next to the engine-nested marker) |
| `facts` (the adapter) | `before` (the pre-image columns the rules read), `configurationSetKind` (G-02), `bindingPrincipalKind` (G-12), `acceptance` (G-17: `approvals-satisfied` with the OP-03 outcome, `engine-accept` with the OP-02 outcome and condition, or `bootstrap` with `noVersionExists`), `gateReadiness` (G-14: the OP-07 payload, passed on unchanged) and `priorRequestHash` (G-15) |
| `context.actor` | Who writes. `isEngineNested` marks the engine's own writes; a caller that names `nestedEffect` without the marker is refused as invalid input |

The payload is the WritePlan: `allowed`, `requestHash` (the hash the adapter stores with the idempotency key), `isReplay`, `isNoOp` (a proposal), `refusals` (each code with its guard rule), `setColumns` (stamps, meaning copies, forced statuses), `removedColumns` (a proposal: caller values in columns the engine computes) and `effects` (kind, entity, operation, rowId or key, columns).

`ruleIds` is judged for OP-08 as for OP-09: a refused case must report a refusal of every guard rule it lists (the reason catalog maps the code to the rule), and an allowed case must report none of them.

| Rule | Cases (prefix) | What they show |
|---|---|---|
| G-01 | `g01-` | A configuration admin, a person with no role, an AI client and an engine effect other than activation are refused; activation's nested writes (row and version) are allowed |
| G-02 | `g02-` | A preset's row never changes, not even in activation |
| G-03 | `g03-`, `g01-activation-updates-tier-allowed` | A key never changes; a changed shipped row becomes local |
| G-04 | `g04-` | Activation never deletes a configuration row; a person's delete is refused by G-01 and G-04 together |
| G-05 | `g05-` | A final decision never changes or goes away; the supersede-decision effect may set Superseded By |
| G-06 | `g06-`, `approver-kind-` | Only the approval runner holding the approval writer role writes an outcome, and only once (the same outcome again is a no-op); an answer that is not from a person signed in as a user is stored as recommendation only. The engine's own writes (`g06-engine-`): it creates an Approval Record only with the pending outcome (the request and every restart; a create without an outcome is invalid input), then writes only expired or cancelled, only once, on a pending record (the same outcome again is a no-op, another on a final record is refused), and never a responder column (Approver, its kinds and meaning, Typed Reason, Responded On, Channel, Flow Run Url, Approver Holds Required Role), with or without an outcome. It never names Outcome Meaning: the guard writes it from the outcome (G-10). The approval runner writes its answer only with an outcome, and nothing else; no other caller writes any O10 column, not even one G-09 would drop |
| G-07 | `g07-` | Audit entries and versions never change |
| G-08 | `g08-` | AI-created rows start as draft or proposed whatever status they name; an AI update never reaches accepted, registered, approved or completed; an AI client may submit an intake (question 9) |
| G-09 | `g09-` | Caller stamps are replaced, computed flags are dropped, and columns only an engine effect writes (owner assignments, Decided On) are refused (`engine.effect-only`, proposed) |
| G-10 | `g10-` | An unknown, retired or foreign status is refused |
| G-11 | `g11-` | A link kind of another category, a URL off the kind's pattern, and a URL that may carry a secret (`link.forbidden-url`, proposed, from the `forbidden-url-patterns` setting) are refused |
| G-12 | `g12-` | Inside activation, a binding to a user is refused and a binding to a group allowed |
| G-13 | `g13-` | Tier and stage change only through the update-subject effect, with the Tier Decision; a new application starts at the first lifecycle stage |
| G-14 | `g14-` | A gate decision is not accepted while OP-07 says not ready, and needs the OP-07 payload |
| G-15 | `g15-` | The same request with the same key is a replay; another request with that key is a conflict |
| G-16 | `g16-` | Only a configuration admin (directly or through an AI client) works on a draft; a submitted draft is frozen; the draft status is the engine's; activation marks it activated |
| G-17 | `g17-` | Acceptance only by the accept-decision effect: after OP-03 satisfied, for a locked-list entry (question 8), or for the bootstrap of version 1; a configuration change decision accepted plans the activation of its draft, and a superseding decision the supersede-decision effect |
| Erasure | `erasure-`, `erased-`, `principal-` | A platform admin (a person, question 9) erases: the label becomes "former user", Is Erased is set, the object ID is kept unless the adapter passes a tombstone, and the Audit Entry holds IDs, never the label. An erased principal stays erased and keeps its label |
| Deletes | `delete-`, `evidence-link-update-refused` | A draft row is deleted only by its creator; a link only within `link-undo-minutes`; everything else is never deleted, and an Evidence Link never changes |
| Class A | `class-a-`, `evidence-link-update-refused`, `g07-` | The history class comes from the logical model (`model/logical-model.json`, compiled into the engine). C02, O20 and O24 are inserted once and never updated or deleted, by any writer, the engine included. C02 and O24 keep their G-07 codes; O20 refuses with `record.append-only` and `record.delete-not-allowed` (proposed) |
| Class F | `class-f-`, `g05-`, `g16-` | Frozen once final, for every writer, the engine included (`record.final`, proposed). O14 is final at insert (a correction is a new record with Corrects Record in its insert); O13 too, apart from its fill-once columns, which the model marks and which fill once while empty; O08 once Tier Decision is set; O22 once Finished On is set; O10 once Outcome Meaning leaves pending (only Counts Toward Requirement from the engine, and the same outcome again, remain); O09 keeps G-05 (only Superseded By from the supersede-decision effect) and C46 keeps G-16. An update with no columns is refused on a final row too, because it would still restamp it |
| Class E and M | `class-e-`, `class-m-` | Updated in place: a dependency is ended with a date; a portfolio changes its description |
| Withdraw | `withdraw-` | A proposed decision is withdrawn only by its proposer (Created By Principal) or its decider, in person or through an AI client acting for them (document 22, section 8.1; `decision.withdraw-not-allowed`, proposed) |
| Registration status | `register-status-` | The registration status is engine-written (document 26, sections 5.3 and 6.2): a request sets only draft, or archives a draft; registered, and any move away from registered, come only from the update-subject effect of an accepted decision (`engine.effect-only`) |
| Gate-relevant documents | `g08-ai-marks-`, `g08-person-marks-` | An AI actor never marks a Document Link current when its document type has Current Needs Person (RA-03; question 9); a person may, and so may an AI actor for a type without it |
| Platform columns | `g09-platform-`, `delete-link-with-future-created-on-refused` | Id, Created On, Created By, Modified On and Modified By are platform-supplied (document 22, section 2.1.1): a value given is dropped and listed in `removedColumns`; a link whose Created On is later than now cannot be deleted, because its undo window cannot be counted |
| Facts | `facts-` | A meaning copy or draft status in the pre-image outside its contract list (such as `Accepted`) is invalid input, never read as not final |
| Estates Covered | `estates-covered-` | O22 Estates Covered is a list of distinct estate keys, never inventory items (document 22, sections 2.2 and 9.1) |
| Columns | `columns-` | Every column a write names is a column of its entity in the logical model, in its exact lowerCamelCase form, for the operational entities, C02 and C46 (`input.unknown-column`, proposed, invalid input before any rule runs). `Status` for `status` or `RespondedOn` for `respondedOn` can no longer pass G-06, G-08 or G-17 unjudged. Live configuration rows carry the bundle's field names and change only inside activation (G-01), so they are not checked against the list, but a model column in another case (`Key` for `key`) is still invalid input |
| Callers | `agent-identity-write-refused`, `unknown-entity-`, `nested-effect-`, `missing-after-`, `configuration-set-kind-missing-`, `snapshot-hash-mismatch-` | Agent identities never write (RA-06); bad input is refused; a broken context is unavailable |

## Activation and bootstrap fixtures

`fixtures/activate` holds the cases of document 25, section 6.4, step 5, and `fixtures/bootstrap` those of document 21, section 4.8. They run through the configuration assembly (`ConfigurationActivator`), like rebase. The operation names `activate` and `bootstrap`, their outcome words and their inputs are proposals of the fixture schema.

| Folder | Fields | Outcomes in the cases |
|---|---|---|
| `activate` (11) | `snapshot` is the active version with its `versionNumber`; `input.baseSnapshot` and `input.draft` as for rebase, plus `configurationDraftId`, `draftStatus`, `resultingVersionNumber`, `changeDecisionId`, `changeDecisionStatus`, `instructedByPrincipalId`; `facts` are the OP-09 facts of the revalidation; `context.asOfUtc` is the validation date passed to OP-09 (V28) and the published-on date; the actor must be engine-nested | activated, discarded, already-activated, refused, invalid-input |
| `bootstrap` (8) | `snapshot` is `{ "none": true }` (no version in force); `input.presetKey`, `input.overlays`, `input.noVersionExists`, `input.hasInstallRights` (a proposal: the platform states the install rights, because no product role is bound yet) | bootstrapped, invalid, refused, invalid-input |

Notes on the cases:

- **The validation date reaches V28.** `grant-too-long-at-activation-discarded` and `grant-within-limit-at-activation-activated` activate the same draft with a workload grant that expires on 2027-11-30: on 2026-11-22 the grant runs 373 days and the draft is discarded; on 2026-12-15 it runs 350 days and version 2 is created.
- **A draft that cannot activate is discarded,** with the reason `rebase conflict` or `invalid at activation`, kept as evidence, and no version is created (C46 Discard Reason). The decision stays accepted. What follows for that decision is still an open point.
- **Activation is not callable** (`not-engine-nested-refused`), follows only an accepted decision and a submitted draft, and is a no-op for a draft already activated (`already-activated-replay`).
- **A row is never deleted.** A row the new version retires is written retired (`retired-row-written-as-retire`).
- **The bootstrap rule.** Version 1 is accepted directly by the person behind the call (directly or through an AI client), only while no version exists and only with install rights. Its change summary names the presets and the rows each entity starts with, instead of one line per row.

## Generated checks

The invariants of document 25, section 10 that these operations carry are generated over every shipped preset in `engine/tests/Product.Engine.Configuration.Tests/OperationTests.cs`: an AI or workload actor never counts; no decision outside the locked list is accepted without a card; a consultation never counts; a recorded external change number alone never authorizes; a missing gate policy never falls back; a more serious answer never lowers the tier; Enforce never stops for window or path alone; the same input gives the same answer. The matching fixtures carry the tag `invariant`.

For part 2, `OperationPart2Tests.cs` adds: over every shipped selection, kind, tier (and none) and stage, OP-06 computes every applicable requirement as missing with no link and complete with fresh links; over every gate, OP-07 is ready with every fact met and not ready with none exactly when a requirement applies; 150 generated graphs check OP-10 against a separate breadth-first search and a separate loop check, in any edge order; every shipped review cadence gives OP-11's next due date; and a day-by-day run around a due date shows one first reminder, one escalation and rising reminder numbers for each subject kind.

For the write guard, `WriteGuardTests.cs` adds: over every shipped selection, every status of the five entities with a draft or proposed meaning, created by an AI client, starts as draft or proposed and is marked AI created, and an AI update to an accepted, registered, approved or completed status is refused; over every full preset, every configuration entity, every operation, six kinds of caller and every engine effect but activation, no write outside activation changes a configuration row, and activation writes but never deletes; every combination of actor kind, principal kind, role and marker is refused an approval outcome except the approval runner as a service principal or managed identity; over every shipped selection and every effect (or none), an engine-nested write never sets a responder column, alone or with any outcome, on a create or an update, and the engine creates an Approval Record only pending, closes a pending one only as expired or cancelled, and on a final one writes the same outcome as a no-op and refuses any other; over every shipped selection, every actor kind row and principal kind, an answer counts only from a person signed in as a user, else it is stored as recommendation only; every write an activation plans passes the guard as an engine-nested write of its effect kind and is refused without the marker; activation is deterministic and a replay changes nothing; the bootstrap gives version 1 for every shipped selection, with the hash of the selection compiled as a working set, and its decision passes the guard only while no version exists; and an erasure's plan never carries the erased label.

For the second set of probes, `WriteGuardTests.cs` adds: over every shipped selection, every document type and every status meaning current, an AI client's update to current is refused exactly when the type has Current Needs Person, and a person's is allowed; an update with no columns on a final row of every class F entity is refused from every writer; and for every entity whose columns are checked, each platform-supplied column, on a create or an update, by a person or the engine, is refused with the write or dropped, never set. `OperationPart3Tests.cs` adds to the OP-14 combinations a pipeline fact dated one second after the evaluation moment: an authorized one never changes the answer and is listed as `authorization.reported-later`; a cancelled one still removes the authorization.

For part 3, `OperationPart3Tests.cs` adds: each max condition kind qualifies exactly up to its limit (every limit 0 to 5 against every fact 0 to 6) and each list kind exactly when every stated key is allowed (every subset of four keys against every allowed subset); the same model is picked in every fact order; over every shipped selection, no change type qualifies without a client's model, and with one, the OP-12 payload passed through the wire makes OP-02 accept a standard change or refuse it; 40 generated scenarios of five runs each, split into one to three pages with an overlapping item, apply each OP-13 plan to a small store and check one claim per key, Times Seen equal to the runs that saw the item, no status ever changed by the engine, no vanished claim before the final page, and a replayed plan that changes nothing; and every set of up to three authorization facts from a pool of fourteen, in every order, with and without a number, gives OP-14's answer as a separate computation does, with no order effect, and an added AI fact never changes it. Over every shipped selection with the linked overlay, the OP-14 payload passed through the wire decides OP-04's missing-approval flag, and a number alone leaves the approval missing at every level but off.

For the design invariants, `InvariantTests.cs` gives every invariant of document 25, section 10 a generated test over every shipped selection (22 today), cataloged as INV-01 to INV-11 in `engine/Product.Engine.Testing/Invariants/DesignInvariants.cs`. A test carries `[Invariant("INV-nn")]`, and `EveryDesignInvariantHasAGeneratedTest` fails if an invariant has none. New: INV-01 (an approved answer from every actor and principal kind but a person signed in as a user never counts, over every OP-02 scope with cards, with people as the control), INV-02 (no caller and no effect but accept-decision stores accepted, and accept-decision only on a satisfied OP-03 or a locked-list engine-accept, with the gate ready), INV-03 (shipped consultations, and any requirement passed as one, never count), INV-04 (a winning refuse rule refuses every answer of the person it names), INV-05 (OP-14 and OP-04 with a number alone or with only facts that never count), INV-06 (every gate policy retired, and each one retired alone, in memory), INV-07 (every application kind, others low and high), INV-09 (the lock false, retired or not true, in all three modes, and in activation) and INV-11 (fresh compiles, text round trips with members reversed, and a replay with no difference). Marked from earlier slices: INV-01 and INV-02 (`WriteGuardTests`, `OperationTests`), INV-08 (`EnforceNeverStopsForWindowOrPathAlone`) and INV-10 (`NoWriteOutsideActivationChangesAConfigurationRow`, now over every shipped selection, not only the four full presets).

## fixtures/replay

Replay records for the replay tool of document 25, section 9 (`engine/Product.Engine.Testing/Replay/ReplayTool.cs`), one per file, checked against `config/schema/replay-record.schema.json`, not the fixture schema. The fixture runner skips any folder named `replay`. A record holds the snapshot version (as in a fixture), the context (without a version or hash: those come from the loaded snapshot), the request (the caller's input and the fetched facts as one wire input) and the recorded result (for OP-08, the write plan).

```
cd src
dotnet engine/Product.Engine.Cli/bin/Release/net10.0/Product.Engine.Cli.dll replay fixtures/replay
dotnet engine/Product.Engine.Cli/bin/Release/net10.0/Product.Engine.Cli.dll replay fixtures/replay/op-04-enforce-stops.json --snapshot config/build/balanced.bundle.json
dotnet engine/Product.Engine.Cli/bin/Release/net10.0/Product.Engine.Cli.dll capture <record.json> --out <file>
```

The replay loads the version (or the bundle `--snapshot` names, such as a C02 Content Json export, with `--version`), evaluates the request again and reports every difference: `snapshot-hash`, `configuration-version`, `engine-version`, `outcome`, `payload` (by JSON pointer), `reasons` and `trace` (row by row). `--ignore-messages` leaves the reason sentences out. A record passes with no difference; a fixture with `expectedDifferences` passes when it finds exactly those kinds. `capture` evaluates a record's request and writes the record with its recorded result. `node tools/ci.mjs` runs the folder as the `replay` check.

| Records | What they show |
|---|---|
| `op-01-tier-computed`, `op-02-go-live-cards`, `op-03-ai-answer-not-counted`, `op-04-enforce-stops`, `op-05-inside-window`, `op-08-ai-accept-refused`, `op-08-ai-create-forced-proposed`, `op-14-number-alone` | True records, each made from a fixture (such as `op-04/enforce-check-stops-missing-approval.json`) and captured: the replay gives the same answer |
| `op-04-recorded-verdict-tampered` | A stored stop changed to allow: outcome, payload and reasons differ |
| `op-08-recorded-plan-tampered` | A stored write plan that says accepted: the payload differs at the two status columns |
| `op-02-other-version-loaded` | A result made with version 2, replayed against version 1: snapshot hash and version differ |
| `op-03-other-engine-version` | Recorded by engine 0.0.9: only the engine version differs |
| `op-14-recorded-reason-dropped` | The number-only warning left out: the reasons differ |
| `op-01-recorded-trace-changed` | The deciding row recorded as only matched: one trace row differs |

`ReplayTests.cs` also turns every evaluation fixture (OP-01 to OP-08, OP-10 to OP-14) into a record, captures it and replays it with no difference, and shows that the same version loaded from an exported bundle replays the same.

## fixtures/presets

Fictional presets that only fixtures use. Never shipped.

| Preset | Kind | Why |
|---|---|---|
| `fixture-overlay-a` | overlay | Lists `fixture-overlay-b` in `conflictsWith`, for V43 |
| `fixture-overlay-b` | overlay | The other half of the V43 conflict |
| `fixture-overlay-c` | overlay | Conflicts with nothing, for the passing V43 case |

`tools/schema-map.json` checks them with the preset schemas, not the fixture schema.
