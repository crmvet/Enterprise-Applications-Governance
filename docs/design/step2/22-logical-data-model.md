# Step 2, document 22: logical data model

> **Platform decided 2026-11-22 (D-26).** The first release is designed to run in the client's own Azure subscription. Section 11 (Azure SQL Database, with ledger tables and row-level security) is its physical target. Section 10 (the Dataverse binding) is kept as a possible later option, not a plan. Sections 1 to 9 hold on either store, as written. The text below was written while the store was still open (D-22), so in places it still says "Neither store is chosen". The decision records are in [docs/decisions](../../decisions/).

- **Date:** 2026-11-22. **Status:** draft design document. It names no client.
- **Step:** 2 of 3 (the design). This document turns the entities of document 20 into a logical data model: attributes, keys, relationships, delete behavior, history and audit. Then it binds the model to two platforms and sizes it.
- **Names.** Every entity and column name comes from `20-glossary-and-entities.md` (document 20). The IDs C01 to C39, O01 to O24 and L01 to L05 are the same. Where this model needs a column or term that document 20 lacks, it is listed in section 13, "New terms", and marked "(new)" where it first appears. Document 20 is not edited. Two proposed entities are added here by the working defaults: C42 Workload Grant (reserved in document 20, section 2.10) and C46 Configuration Draft (a proposed ID that document 20, section 2.10 still has to reserve).
- **Working defaults.** This file is aligned to [28-working-defaults.md](28-working-defaults.md) (document 28, alignment pass 2026-11-22). A default from its sections 1 and 2 is cited as "28, issue n" or "28, Qn". Where a default answers one of the six open design questions (28, section 3), the text says "(working default, open design question n, see 28-working-defaults.md)". The product owner can override any default.
- **Platform-neutral (D-22).** Sections 1 to 9 hold on any store. Section 10 binds the model to Dataverse and section 11 to Azure SQL Database. Neither store is chosen here; D-26 later chose Azure (see the note at the top). The foundation study behind D-22 and D-26 compared their running cost and admin effort, and the D-26 record summarizes it.
- **Follows:** decisions D-01 to D-22 (the records are in [docs/decisions](../../decisions/)) and four rounds of Microsoft fit corrections, a review of the design against Microsoft's own tools. A tag such as "(fit correction 8)" names the correction a rule comes from. Where this file and a decision disagree, the decision wins. The 2021 tables are a naming reference only (D-22). One 2021 lesson is used: its "remove link" delete behavior left orphan rows, so this model restricts deletes instead.
- **Sweep proposals.** Proposals from a review of Microsoft and GitHub news (2026-11-21) that this model adopts are marked "(sweep proposal)". They are proposals, not decisions. Sources taken from that review carry J numbers in section 14.
- **Patterns** from earlier governance work are used as patterns only, generalized. A tag such as I-183 names the pattern.
- **Microsoft facts** carry an ID. S-numbers are the same facts and sources as in document 20. T-numbers are new here. Section 14 lists every URL with "checked 2026-11-22". UNVERIFIED marks what no source confirmed (section 15).
- **No Power BI. No Mermaid.** Diagrams to build later in Visio are specified in section 16.

---

## 0. In one screen

1. **65 entities for the first release** (41 configuration, 24 operational) plus 5 later ones. The 41 include two that the working defaults add: C42 Workload Grant (28, issue 8) and C46 Configuration Draft (28, issue 3). Documents 21, 24 and 26 propose five more configuration entities (C40, C41, C43, C44 and C45, reserved in document 20, section 2.10). They are not counted here, and their attributes stay in those documents. The logical shape is the same on both platforms.
2. **Configuration at the center (D-06, D-07).** Operational rows point to configuration rows by reference, inside the one working set in force. APIs and MCP tools carry keys, never internal IDs. A live configuration row is never deleted. It retires. So no reference ever breaks. A change starts as a patch on a Configuration Draft. Only the activation operation writes live configuration rows and the new version, in one transaction. No person, role or tool writes configuration tables directly (28, issue 3).
3. **Fixed meanings are copied onto rows.** Where an operational row has a configurable status, the engine also writes the fixed engine meaning next to it (a "meaning copy", new). The engine, row security and views read that one column. Labels stay configurable (D-06).
4. **Five history classes** (new): append-only, freeze on final, effective-dated, mutable with history, configuration versioned. Every entity has one.
5. **Decisions are append-only in effect.** Content can change only while a decision is proposed. Accepted, rejected and withdrawn decisions are frozen. A change is a new decision that supersedes the old one [S1]. "Superseded" is derived from the later decision, so the frozen row is never touched.
6. **Approval records are immutable once answered.** The engine writes the request. Only the approval writer writes the outcome, once. The approval writer is the workload identity of the approval runner (the approval flows), and it holds the approval writer role through a workload grant (28, issue 7). Only an approved outcome from a person who holds the required role counts. AI never approves (D-02, D-10).
7. **Deployment records are written once.** A retry with the same idempotency key returns the existing record (I-189). A correction is a new record that names the one it corrects.
8. **Every row says who performed it:** Created By Principal, Performed By Principal, Performed By Actor Kind, On Behalf Of, Client App and Client App Verified (three of these are new). Every row also carries the engine stamp, Engine Version and Engine Checked On, so a write outside the engine shows (28, issue 15 and Q16). Approval records add the approver kind (sweep proposal).
9. **The gap check claim holds only a Microsoft ID, a link, a label as seen and a last-seen date** (D-15). One claim per Microsoft item and finding kind. No copy of Microsoft's inventory.
10. **Deletes are restricted by default.** One owned child relationship cascades (Affected Application, while its change is a draft). Eight entities are never deleted.
11. **Reads are open by default, with three settings** (28, issue 10 and Q13). `intake-visibility` defaults to "own", `claim-visibility` to "admins and claimant", and `record-access-mode` to open read, with portfolio read available.
12. **Dataverse binding:** standard tables, configuration organization-owned, operational user-or-team owned (ownership can't change after creation [T1]), lookups for every configurable list, global choices only for the engine contract, alternate keys on keys and Microsoft IDs, auditing on.
13. **Azure SQL binding:** `config`, `ops` and `audit` schemas; temporal tables for mutable rows [S19]; ledger tables for decisions, approvals, deployment records, configuration drafts, configuration versions and audit (28, Q21) [S18][T16]; the engine connects with its managed identity and sets the person's ID in session context for row-level security (28, Q20) [T13]; native `json` columns [S20].
14. **Capacity for a 5,000-person organization:** about 43,000 operational rows in year 1 and about 172,000 by year 5. Under 2 GB of database storage on either platform by year 5. Storage is not the cost driver on either platform.

---

## 1. How to read this document

### 1.1 Logical types

| Type | Meaning | Dataverse | Azure SQL |
|---|---|---|---|
| id | The row's unique identifier, generated by the platform | Primary key (GUID) | `uniqueidentifier` |
| text(n) | Short text up to n characters | Single line of text, maximum n (at most 4,000) [T8] | `nvarchar(n)` |
| longtext | Multi-line plain text | Multiple lines of text, up to 1,048,576 characters [T8] | `nvarchar(max)` |
| int | Whole number | Whole number | `int` |
| decimal | Decimal number | Decimal number | `decimal(9,2)` |
| bool | Yes or no. Nullable only where "not stated" is a real answer | Yes/No | `bit` |
| date | Calendar date | Date only | `date` |
| datetime | Date and time, stored in UTC | Date and time | `datetime2(3)` |
| url | A link, up to 2,000 characters | Single line of text, URL format | `nvarchar(2000)` |
| json | A JSON object or array | Multiple lines of text holding JSON. The JSON text format exists "only in non-SQL stores like Audit" [T7] | `json` [S20]. Top level must be an object or array [T19] |
| hash | SHA-256 as 64 lowercase hex characters | Single line of text (64) | `char(64)` |
| key | A configuration key: lowercase letters, digits and hyphens, 1 to 64 characters, starting with a letter (document 20, section 3.2) | Single line of text (64) | `varchar(64)` with a check constraint |
| meaning(X) | One value of the fixed engine contract X (document 20, section 2.4) | Global choice | `varchar(32)` with a check constraint |
| ref(E) | A reference to one row of entity E | Lookup | `uniqueidentifier` with a foreign key |

### 1.2 Flags in the attribute tables

- **Req:** Y required, N optional, C conditional (the rule is in the description).
- **Ref:** CFG is a configuration reference (it points to a configuration row, and API and MCP payloads carry the row's key). OPS is a reference to an operational row. PR is a reference to a Principal Reference (O05). Blank means a plain value.

### 1.3 Delete behavior words

| Word | Meaning |
|---|---|
| Restrict | The parent can't be deleted while children point to it. The default everywhere |
| Cascade | Deleting the parent deletes the children. Used only for an owned child row, and only while the parent itself may still be deleted (a draft). Configuration children never cascade, because live configuration rows are never deleted and a proposed row exists only inside a draft's patch (28, issue 3) |
| Never | Rows of this entity are never deleted, by anyone. The engine refuses. On Azure the database also denies it |

### 1.4 Cardinality

Written from parent to child: "1 to 0..n" means one parent row has zero or more children, and each child has exactly one parent. "0..1 to 0..n" means the child's reference is optional.

---

## 2. Model-wide rules

### 2.1 Column sets

#### 2.1.1 Common columns (every entity)

Document 20, section 2.2, with types. Five columns are new: three here, and the two engine stamp columns from document 25 (28, issue 15).

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Id | id | Y | | The row's unique identifier. Never shown to people. Never used in MCP calls for configuration rows |
| Name | text(200) | Y | | A short human label and the primary display column. On taxonomy rows it holds the Label |
| Description | longtext | N | | Plain-text description, written for people and agents |
| Created On | datetime | Y | | When the row was created. Platform-supplied |
| Created By | platform identity | Y | | The platform identity that wrote the row: the Dataverse user, or the database principal on Azure. Platform-supplied |
| Modified On | datetime | Y | | When the row last changed. Platform-supplied |
| Modified By | platform identity | Y | | The platform identity that last changed it. Platform-supplied |
| Created By Principal (new) | ref(O05) | Y | PR | The person or non-human principal that created the row, resolved by the engine from the validated caller. Never changes |
| Performed By Principal (new) | ref(O05) | Y | PR | The principal behind the last write |
| Performed By Actor Kind | meaning(actor kind) | Y | | Who or what made the last write: person, AI client for a person, agent identity, pipeline, approval runner or engine (section 2.4) |
| On Behalf Of | ref(O05) | C | PR | The person an AI client, pipeline, the approval runner or the engine acted for. Empty when a person acted directly |
| Client App | text(200) | N | | The MCP client or application behind the last write, such as "Claude Code" or the product's web page ("client app X on behalf of user Y", sweep proposal) |
| Client App Verified (new) | bool | Y | | Yes when Client App came from a validated token claim. No when the caller only stated it (section 2.4) |
| Is AI Created | bool | Y | | Yes when an AI client or agent identity created the row. Such operational rows start as draft or proposed (D-02) |
| Configuration Set Version | ref(C02) | C | CFG | Operational rows only: the configuration set version in force when the row was created. It also fixes which guideline versions applied |
| Engine Version (new) | text(20) | Y | | The engine library version that evaluated the last write (document 25). Set by the engine only |
| Engine Checked On (new) | datetime | Y | | When the engine last evaluated the row. A later Modified On means a write outside the engine. A scheduled read-only check finds such rows and records each finding as an audit entry (28, Q16) |

#### 2.1.2 Taxonomy columns (C04 to C39, C42)

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Key | key | Y | | Stable text identifier, unique per entity within a configuration set. Never reused after retirement |
| Label | text(200) | Y | | The text people see. Can be translated. Stored in the Name column |
| Sort Order | int | N | | Display order. Never used as logic |
| Is Active | bool | Y | | No when the row is retired. Retired rows stay, so old records still resolve |
| Origin | meaning(origin) | Y | | Shipped (from a preset or seed) or local (added or changed by the client). Upgrades never overwrite local rows |
| Configuration Set | ref(C01) | Y | CFG | The configuration set this row belongs to |
| Engine Meaning | meaning(per entity) | C | | Only on C05, C12, C36 and C38: the fixed meaning the engine reads (C37 carries its contract value as Link Category). It can't change while any row uses it |

#### 2.1.3 Link columns (O03, O15, O16, O18, O19, O20, O23)

These columns hold "links, not copies" (D-12, D-15, D-17, D-19). Each linking entity uses the subset its table shows.

| Column | Type | Description |
|---|---|---|
| Link Kind | ref(C37) | What the link points to. The link kind's category must match the entity (document 20, section 2.4) |
| External Id | text(400) | The tool's own identifier, such as an inventory resource name, an Entra object ID or a work item number. Never a copy of the tool's data |
| Url | url | A link that opens the item in its own tool |
| Label As Seen | text(200) | The item's name when last seen. A label, not a copy |
| Last Seen On | datetime | When the product last confirmed the item exists, by gap check, record step or a person |

#### 2.1.4 Meaning copies (new)

An operational entity whose Status (or Outcome) references C05 also carries a meaning copy: Status Meaning or Outcome Meaning, of type meaning(X).

- The engine writes it from the referenced status row's Engine Meaning, at every write. Nobody else writes it.
- The engine, row-level security predicates, freeze rules and views read the meaning copy. They never read a label or a status row ID.
- A status row's Engine Meaning can't change while any row uses it. To change behavior, retire the row and add a new one.
- Entities with a meaning copy: O02, O07, O09, O10 (Outcome Meaning), O11, O14 (Result Meaning), O19, O21, O23.

### 2.2 Configuration references

1. **Store the reference, carry the key.** An operational row stores the configuration row's Id. API and MCP payloads carry the key (document 20, section 3.3). The engine resolves keys within the working set in force.
2. **Only the working set in force.** A referenced configuration row must belong to the working set in force when the operational row is written. The engine checks this on every write. No store expresses it cheaply.
3. **Matched by key, never deleted.** Activating an accepted draft (a preset, an upgrade or a configuration change) updates rows by key, adds missing rows and retires removed ones (Is Active = No). Only the activation operation does this, in one transaction with the new C02 row (28, issue 3). A live configuration row is never deleted, so every reference stays valid.
4. **What applied is provable.** The Configuration Set Version stamp on an operational row, plus that version's snapshot and hash (C02), shows exactly which rules applied when the row was written.
5. **Key lists inside JSON are validated.** Applies To Kinds (C09), Triggers Json (C10), Default Component Kinds (C15), Conditions Json (C21), Allowed Routes (C35), Emergency Rule Json (C33), Change Json (C46, C02) and Estates Covered (O22) hold keys. The engine validates every key when a draft is validated and again at activation, or when the operational row is written.
6. **List values belong to their list.** A reference to a List Value (C07) must point to a value of the named list, for example Horizon to the `horizon` list. The engine checks it.
7. **CFG marks every configuration reference** in the tables below. They are the only place where an operational row depends on a client's tailoring.

### 2.3 History classes (new)

| Class | Rule | Entities | Dataverse | Azure SQL |
|---|---|---|---|---|
| **A. Append-only** | Inserted once. Never updated, never deleted. C02 is inserted only by the activation operation | C02, O20, O24 | A synchronous plug-in refuses Update and Delete. Auditing on | Append-only ledger table: "only INSERT operations" [S18] |
| **F. Freeze on final** | Editable only while in its draft state. Frozen when it reaches a final state. Never deleted after it leaves draft. O14 is final at insert. O13 has fill-once columns. C46 is frozen at submit; after that only the engine writes its outcome columns, once | C46, O08, O09, O10, O13, O14, O22 | A synchronous plug-in refuses changes to frozen rows. Auditing on | Updatable ledger table (C46, O08, O09, O10) or append-only ledger table (O14) [T16][S18]. A block predicate refuses updates to frozen rows [T13]. O13 and O22: temporal table plus engine rule |
| **E. Effective-dated** | Updated in place, with history, but a fact is ended with a date, never deleted | O03 (Retired On), O04 (Ended On), O06 (End On) | Auditing on | System-versioned temporal table [S19] |
| **M. Mutable with history** | Updated in place. Every version kept | O01, O02, O05, O07, O11, O12, O15, O16, O17, O18, O19, O21, O23 | Auditing on | System-versioned temporal table [S19] |
| **V. Configuration versioned** | Updated in place inside a working set, only by the activation operation. Each activation creates an immutable snapshot (C02) | C01, C03 to C39, C42 | Auditing on, plus C02 | Temporal table, plus C02 |

**Proposed by the build (2026-11-22).** The final state of each class F entity, the O13 fill-once columns, the refusal of an empty update on a final row, and the platform-supplied columns are in B22-3 at the end of this document.

### 2.4 Who performed the write

**How each path fills the columns.** The engine sets these columns. No caller sets them directly.

| Path | Performed By Principal | Performed By Actor Kind | On Behalf Of | Client App | Client App Verified |
|---|---|---|---|---|---|
| A person in the product's web page or the model-driven app | The person | person | Empty | The page or app | Azure: yes (token claim). Dataverse: yes when the engine sets it from the request context, otherwise no |
| An AI client through the product's MCP server, on behalf of the signed-in person (for example Claude Code) | The person (the token's subject) | AI client for a person | The person | The MCP client | Azure: yes, from the validated token. Dataverse: no, because the MCP server passes it as a `tag` value that "client applications ... set any value they wish" [T10] |
| An AI client through Microsoft's Dataverse MCP server (Dataverse binding only) | The person | AI client for a person, when the engine can tell; otherwise person, and the row is flagged for review | The person | When known | No |
| An agent identity acting on its own | The agent identity | agent identity | Empty | The agent's app | Azure: yes. Dataverse: no |
| A pipeline record step | The pipeline's workload identity | pipeline | The person who started the run, as the step reports it | The pipeline tool, as reported | No (the step states it). O14 adds Started By AI Client and Merge Actor (sweep proposal) |
| The approval runner writing an outcome | The approval writer: the runner's workload identity, holding the approval writer role through a workload grant (C42) | approval runner | The approver | The approvals service | Yes for the identity. The channel is as reported |
| The engine itself (flags, computed dates, reminders, derived links) | The engine's identity | engine | The principal whose write triggered it | The engine | Yes |

**Rules.**

1. **Delegated tokens are the norm.** The product's MCP server calls the store on behalf of the signed-in person (D-09, fit correction 24). So the person is the performer and the AI client is the Client App. The token "represents the user while also identifying the calling application or agent" (sweep proposal; Conditional Access for agents, https://learn.microsoft.com/entra/identity/conditional-access/agent-id, checked 2026-11-22).
2. **AI-created rows start as drafts.** Is AI Created is Yes when the actor kind is AI client for a person or agent identity. The engine then forces the row's meaning copy to draft or proposed (D-02).
3. **Dataverse's own audit sees impersonation and AI assistance.** For impersonated calls, `createdonbehalfby` identifies the actual user [T9], and the audit table records `callinguserid` and the actions "Create - AI assisted" and "Update - AI assisted" [S16]. Whether a plug-in can read the AI-assisted signal at write time is UNVERIFIED.
4. **Approver kind (sweep proposal).** O10 records Approver Actor Kind and Approver Principal Kind. An approval counts toward a requirement only when the actor kind meaning is person, the principal kind is user, and the approver holds the required role when the outcome is written. Group rosters are read from Entra, because Dataverse group team membership is cached [S7] (fit correction 12).
5. **Approvals reported by a pipeline never count.** A record step can report the pipeline's own approvals and merge approvals, including AI approvals that a client's code tool now allows [J1]. They are stored as evidence on the deployment record (Reported Approvals Json, new) and never count toward the product's approval rules.
6. **Stateless MCP server (sweep proposal).** The product's MCP server keeps no session state. Every multi-step tool keeps its state in the store: drafts as draft rows, retries through idempotency keys, and call chains through the Correlation Id on O24.

### 2.5 Delete policy

| Scope | Rule |
|---|---|
| Configuration rows | A row proposed in a draft exists only inside the draft's patch, so there is nothing to delete. A live row is never deleted. It retires (Is Active = No). Presets are read-only |
| Operational drafts | A row whose meaning copy is draft (an unconfirmed application, a draft intake or change request) can be deleted by the engine, on a person's instruction, together with its owned children |
| Operational rows after draft | Never deleted. An application is archived (registration meaning archived). A request is withdrawn or cancelled. A dependency, owner assignment or component is ended with a date |
| Never deleted at all | C02 Configuration Set Version, C46 Configuration Draft (discarded instead), O05 Principal Reference, O09 Decision (a draft is withdrawn, not deleted), O10 Approval Record (cancelled instead), O14 Deployment Record, O20 Evidence Link, O24 Audit Entry |
| Erasure | Section 2.7 |

### 2.6 Subjects that can be one of several entities

O09 Decision, O18 Delivery Link and O20 Evidence Link each belong to one subject of several possible kinds. O24 Audit Entry names its row by entity name and row ID.

- **Logical shape:** a Subject Kind column plus one optional reference per allowed kind. Exactly one reference is set, and it matches Subject Kind.
- **Subject kind values (new term: subject kind list)** are logical entity names. They are fixed by the schema, because adding a kind adds a column. They are part of the engine contract, not configuration.
- **Same shape on both platforms.** On Dataverse a multi-table lookup is possible, but only through code: maker support is "a future release" [S14]. One plain lookup per kind works in views, forms and generative pages. On Azure it is one nullable foreign key per kind and a check constraint.

### 2.7 Personal data and erasure

1. **Person facts live in one place.** Only O05 Principal Reference holds a person's label and Entra object ID. Every other row holds only a reference, as Microsoft advises for append-only stores: "store personal data outside the event store and reference it by identifier" [S21].
2. **Free text stays business text.** Statement, Reason, Typed Reason, Need, Notes, Proposal Rationale and similar columns are for business content. The playbook states a rule against personal data in them (28, Q21). People can still break the rule, so it is a residual risk.
3. **Erasure** clears Display Label to "former user" and sets Is Erased (new). Where the client's policy requires it, the engine replaces Entra Object Id with a random tombstone value. History rows stay intact because they only hold the reference.
4. **Audit entries hold IDs, not labels.** Changed Values Json never copies a Principal Reference label.
5. **Ledger rows can't be deleted.** On Azure, deleting older data in append-only ledger tables, or in the history of updatable ledger tables, "isn't supported" [T15]. The working default accepts this for the frozen and append-only entities. It comes with the free-text rule in item 2, the label-clearing erasure in item 3, and a documented legal basis for keeping the audit record (28, Q21).

---

## 3. Configuration entities

Taxonomy columns (section 2.1.2) apply to C04 to C39 and C42 and are not repeated. Relationships, with cardinality and delete behavior, are in section 6. Keys are in section 7.

### 3.1 Configuration core

**C01 Configuration Set.** A whole, named set of configuration rows: a preset or a client's working set. Class V. No taxonomy columns.

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Set Kind | meaning(configuration set kind) | Y | | Preset or working |
| Based On Preset | ref(C01) | N | CFG | The preset a working set was copied from. Empty on presets |
| Is In Force | bool | Y | | Yes for the one working set the engine reads. At most one row is Yes. Areas that need other rules use the optional Portfolio scope on rule rows, never a second working set (28, Q2) |
| Current Version | ref(C02) | N | CFG | The active version. Empty before the first activation. Written only by the activation operation |
| Current Version Number (new) | int | N | | The active version's number, kept equal to Current Version by the activation operation. The one value the engine's version probe reads on every governed request (document 21, section 10.2) |
| Preset Key | key | C | | Presets only: `starter`, `balanced`, `regulated`, `microsoft-coe-baseline` (sweep proposal) |
| Product Release | text(20) | C | | Presets only: the product release that shipped the preset |

**C02 Configuration Set Version.** An immutable snapshot of a set, created only at activation and stamped on operational rows. Class A. No taxonomy columns. Drafts never live here. They live on C46 Configuration Draft (28, issue 3).

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Configuration Set | ref(C01) | Y | CFG | The set this snapshot belongs to |
| Version Number | int | Y | | A whole number that only goes up within the set. Assigned at activation, so discarded drafts leave no gaps |
| Version Status (new) | meaning(version status) | N | | Derived, never stored: active when C01 Current Version points here, superseded when a later version exists. So the append-only row is never updated. The draft and discarded states live on C46 |
| Base Version Number (new) | int | C | | The active version this one replaced. Empty on version 1 |
| Source Draft (new) | ref(C46) | C | CFG | The accepted draft this version came from. Required for every version after version 1 |
| Change Json (new) | json | C | CFG | The patch actually applied: the ordered upsert and retire operations by entity and key, after any rebase (document 21, section 4.5). Required for every version after version 1 |
| Content Json | json | Y | | The full snapshot of every row in the set |
| Content Hash | hash | Y | | SHA-256 of the canonical snapshot, so a record can prove which rules applied |
| Snapshot Schema Version (new) | text(20) | Y | | The version of the snapshot's own shape. The engine refuses a major version it does not know (document 25) |
| Validation Json (new) | json | Y | | The validation result at activation: no errors, and any warnings with rule IDs (document 21, V01 to V44) |
| Applied Presets Json (new) | json | C | | The full preset and overlays applied, each with its content version. Required on version 1 and on any version that re-bases on a preset or applies an upgrade |
| Published On | datetime | Y | | When this version came into force, at activation |
| Instructed By | ref(O05) | Y | PR | The admin, a person, who decided the change (D-10) |
| Change Decision | ref(O09) | C | OPS | The configuration change or gate policy change decision behind this version. Required for every version after version 1 |
| Change Summary | longtext | Y | | A plain list of what changed from the previous version, computed by the engine from the diff. Never the AI's own text |
| Seed Version | text(20) | N | | The product release whose seed this version started from, if any |

Document 21's earlier draft columns on C02 now split, and document 21 uses the same split: the draft states live in C46 Draft Status, while active and superseded are derived on C02 (Version Status, never stored); Base Version Number, Change Json, Validation Json and Applied Presets Json sit on both; Proposal Rationale sits on C46, reached through Source Draft.

**C46 Configuration Draft (new entity, proposed ID).** One proposed change to the working set, held as a patch until a person accepts it (28, issue 3; document 21, section 4.2). Class F: editable only while its status is draft, frozen at submit. Never deleted: an unused draft is discarded and kept as evidence of the proposal. No taxonomy columns. Only the engine's draft operations write it.

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Record Number (new) | text(20) | Y | | A number such as CFG-00012, assigned by the engine. MCP tools and pages name a draft by it |
| Configuration Set | ref(C01) | Y | CFG | The working set this draft would change |
| Draft Status (new) | meaning(draft status) | Y | | Draft (editable), submitted (frozen, its decision proposed), activated or discarded (a fixed list, new) |
| Base Version Number | int | Y | | The active version the draft started from. Used for concurrency and rebase (document 21, section 4.5) |
| Change Json | json | Y | CFG | The patch: ordered upsert and retire operations by entity and key (document 21, section 8.2). No operation deletes a row |
| Preview Content Json | json | N | | The snapshot the set would have if activated, in the bundle format. Shown for review; never read as live configuration (document 21) |
| Validation Json | json | N | | The last validation result: errors and warnings with rule IDs (document 21, V01 to V44) |
| Change Summary | longtext | C | | What changes, computed by the engine from the diff. Never the AI's own text. Set at submit and frozen with the draft. It is the diff the approval card shows. Required once submitted |
| Proposal Rationale (new) | longtext | N | | Why the change is wanted, in the words of the admin or the AI that drafted it |
| Applied Presets Json | json | C | | Upgrade and re-base drafts only: the full preset and overlays applied, each with its content version (28, Q5: an upgrade proposes a draft) |
| Instructed By | ref(O05) | Y | PR | The admin, a person, who instructed the change (D-10) |
| Change Decision | ref(O09) | C | OPS | The configuration change or gate policy change decision proposed at submit. Required once submitted |
| Submitted On | datetime | C | | When the draft was submitted and frozen. Required once submitted |
| Expires On | datetime | Y | | When an unsubmitted draft is discarded: created plus the setting `draft-expiry-days` |
| Resulting Version | ref(C02) | C | CFG | The version the activation created. Required when activated |
| Discarded On | datetime | C | | When the engine discarded it: on withdrawal, on rejection, or at Expires On without submit. Required when discarded |
| Discard Reason | text(20) | C | | Withdrawn, rejected, expired or rebase conflict. Required when discarded |

Rules: after submit only the engine writes, once: Draft Status to activated or discarded, with Resulting Version or Discarded On. A person accepts the decision on an approval card that shows the Change Summary (working default, open design question 4, see 28-working-defaults.md). The activation operation then applies Change Json to the live rows by key, inserts the C02 row and updates C01 Current Version and Current Version Number, all in one transaction (section 8.4).

**C03 Preset.** Stored as a C01 row whose Set Kind is preset, using its Preset Key and Product Release columns. Read-only: only a product upgrade writes it.

**C04 Setting.** One named switch with one value.

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Value Json | json | Y | | The value. A single value is wrapped as `{"value": ...}`, because the `json` type accepts only an object or an array at the top level [T19] |
| Value Type | text(50) | Y | | The expected shape: text, yes or no, number, a key of another entity, or a named JSON shape |
| Is Locked | bool | Y | | Yes when the engine refuses any change by configuration, as for `ai-approval-lock` |

Every setting, with its default and its owner, is listed in one settings table in document 21 (28, issue 15). This model does not keep a second list. The settings its columns, predicates and retention rules read are:

| Setting | Default | Read by |
|---|---|---|
| `change-management-mode` | built in (D-08) | O11 Mode |
| `ai-approval-lock` | on, locked | O10 counting |
| `draft-expiry-days` | 30 (document 21) | C46 Expires On |
| `workload-grant-max-days` | 365 (document 26) | C42 Expires On |
| `default-ai-only-code-approval-handling` | flag (document 21; working default, open design question 7, see 28-working-defaults.md) | C33, O14 |
| `intake-visibility` | own: the requester, plus the PMO, platform admin and architect roles (28, issue 10 and Q13) | `fn_CanReadIntake`, Dataverse ownership of O07 |
| `claim-visibility` | admins and claimant (28, issue 10 and Q13) | `fn_CanReadClaim`, Dataverse ownership of O22 and O23 |
| `record-access-mode` | open read, with portfolio read available (document 26; 28, issue 10 and Q13) | `fn_CanReadPortfolio`, Dataverse ownership in portfolio read |
| `audit-retention-days` | forever on Dataverse; the client's policy on Azure (document 26). The earlier `history-retention-days` is merged into it (28, issue 15) | Section 8.6 |

**C05 Status.** A configurable status label for one entity, mapped to one engine meaning.

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Applies To Entity | text(64) | Y | | The logical entity this status belongs to, such as Decision |
| Engine Meaning | meaning(status meaning) | Y | | The fixed meaning. It must be one of the meanings of that entity's contract (document 20, section 2.4) |

**C06 Value List.** A named list of labels with no engine behavior. Its Key is the list key, such as `horizon`.

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Is Extensible | bool | Y | | Yes when the client may add values |

**C07 List Value.** One label in a value list.

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Value List | ref(C06) | Y | CFG | The list this value belongs to. Its Key is unique within the list |

### 3.2 Taxonomy

**C08 Tier.**

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Rank | int | Y | | Higher means more serious. Unique within the set. The engine compares ranks, never tier numbers or labels |

**C09 Tier Question.**

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Question Text | text(500) | Y | | The question as people read it |
| Help Text | longtext | N | | A short explanation with an example |
| Applies To Kinds | json | N | CFG | Keys of the application kinds that see this question. Empty means all |

**C10 Tier Answer Option.**

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Tier Question | ref(C09) | Y | CFG | The question this answer belongs to |
| Answer Text | text(500) | Y | | The answer as people read it |
| Implied Tier | ref(C08) | Y | CFG | The tier this answer suggests. The most serious answer wins |
| Triggers Json | json | N | CFG | Keys of review types or gate requirements this answer adds, such as a security review |

**C11 Vocabulary Mapping.** It points to a taxonomy row by entity and key, not by reference, so one table maps any entity.

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Source Entity | text(64) | Y | | The taxonomy entity being mapped, such as Tier |
| Source Key | key | Y | CFG | The key of the row being mapped. Validated with the draft and again at activation |
| Vocabulary | ref(C07) | Y | CFG | Which outside vocabulary, from the `vocabulary` list |
| Term | text(200) | Y | | The outside term, such as "Critical" or "Zone 3" |
| Source Url | url | Y | | Where the term is defined |
| Checked On | date | Y | | When the term was last checked at its source |

**C12 Lifecycle Stage.**

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Stage Number | int | Y | | Display number, 0 to 10 by default. Never used as logic |
| Stage Rank (new) | int | Y | | Orders the stages so the engine can test "at or after a stage", for example for a documentation requirement's From Stage. Unique within the set. Compared, never shown as logic (document 25) |
| Engine Meaning | meaning(lifecycle) | Y | | Pre-live, live or retired |
| Leading Owner Role | ref(C28) | C | CFG | The owner role that leads this stage, when relative to the application. At most one of this and Leading Role |
| Leading Role | ref(C29) | C | CFG | The product role that leads this stage, when organization-wide |

**C13 Gate.**

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| After Stage | ref(C12) | Y | CFG | The stage this gate follows |
| Gate Decision Type | ref(C18) | Y | CFG | The decision type that records passing the gate |

**C14 Gate Requirement.**

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Gate | ref(C13) | Y | CFG | The gate this requirement belongs to |
| Tier | ref(C08) | N | CFG | Applies only at this tier |
| Application Kind | ref(C15) | N | CFG | Applies only to this kind |
| Requirement Kind | meaning(requirement kind) | Y | | Decision accepted, document current or review done. Document 21 adds owner assigned, with a Required Owner Role column (document 21, section 13.3) |
| Required Decision Type | ref(C18) | C | CFG | Required when the kind is decision accepted |
| Required Document Type | ref(C22) | C | CFG | Required when the kind is document current |
| Required Review Type | ref(C24) | C | CFG | Required when the kind is review done |
| Guideline | ref(C39) | N | CFG | The guideline this requirement applies |

Rule: exactly one Required column is set, and it matches Requirement Kind.

**C15 Application Kind.**

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Category | ref(C07) | N | CFG | A grouping label from the `application-category` list |
| Default Component Kinds | json | N | CFG | Keys of component kinds usually found in this kind of application |

Seed rows include runtime apps (sweep proposal). Fabric apps and Power BI data apps are component kinds (C16) in document 20, not application kinds.

**C16 Component Kind.**

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Source Tool | ref(C07) | Y | CFG | The tool that holds this kind, from the `tool` list |
| Source Type Hint | text(200) | N | | The tool's own type name, used to match gap check results. For the Power Platform inventory, the resource `type`, such as `microsoft.powerapps/canvasapps` [T22] |
| Is Gap Check Covered | bool | Y | | Yes when the gap check can see this kind [S6] |

**C17 Dependency Type.**

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Inverse Label | text(200) | Y | | The label read from the other side, such as "used by" |
| Counts For Impact | bool | Y | | Yes when the impact walk follows this type |

**C18 Decision Type.**

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Needs Options Considered | bool | Y | | Yes when a decision must list the options it rejected, as an ADR does [S1] |
| Default Subject Kind | text(64) | N | | The subject kind this decision type is usually about |
| Requires Approval (new) | bool | Y | | Yes when the approval rules (C32) decide who must approve. No sends one card to the accountable holder (C31; 28, issue 1). The engine accepts with no card only what the locked setting `engine-acceptable-decision-types` allows (document 21; working default, open design question 8, see 28-working-defaults.md) |

**C19 Decision Outcome.**

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Decision Type | ref(C18) | Y | CFG | The decision type this outcome belongs to |
| Next Lifecycle Stage | ref(C12) | N | CFG | The stage an accepted decision with this outcome moves the application to |

**C20 Change Type.**

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Requires Authorization | bool | Y | | Yes when a change of this type needs a change authorization decision |
| Allows Authorization After | bool | Y | | Yes when authorization may follow the change, as for emergencies |
| Requires Review After | bool | Y | | Yes when a post-change review is due |
| Review Due Days | int | C | | Working days until that review is due. Required when Requires Review After is Yes |
| Requires Standard Model (new) | bool | Y | | Yes when a change of this type must name an active standard change model (C21). The engine records the authorization itself when the model's conditions hold (document 25, OP-12; working default, open design question 8, see 28-working-defaults.md) |

**C21 Standard Change Model.**

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Change Type | ref(C20) | Y | CFG | The standard change type this model belongs to |
| Tier | ref(C08) | N | CFG | The highest tier the model applies to |
| Conditions Json | json | Y | CFG | The conditions that keep a change standard |
| Approved By Decision | ref(O09) | Y | OPS | The decision that listed this model |

**C22 Document Type.**

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Default Link Kind | ref(C37) | N | CFG | The link kind usually used, such as wiki page |
| Freshness Days | int | N | | Days after the last check when a current document becomes stale. Empty means the type does not go stale by age (document 21's seed leaves it empty for architecture decision records) |
| Template Url | url | N | | A link to a template in the playbook |
| Current Needs Person (new) | bool | Y | | Yes when a document of this type that an AI client marks current needs a person's confirmation card before it counts. The seed sets Yes for gate-relevant document types (working default, open design question 9, see 28-working-defaults.md; document 23) |

**C23 Documentation Requirement.**

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Document Type | ref(C22) | Y | CFG | The document type required |
| Tier | ref(C08) | N | CFG | Applies only at this tier |
| Application Kind | ref(C15) | N | CFG | Applies only to this kind |
| From Stage | ref(C12) | Y | CFG | The stage from which the document is required |

**C24 Review Type.**

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Produces Decision Type | ref(C18) | Y | CFG | The decision type a finished review records |
| Default Owner Role | ref(C28) | N | CFG | The owner role that usually leads this review |

**C25 Review Cadence.**

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Review Type | ref(C24) | Y | CFG | The review type this cadence is for |
| Tier | ref(C08) | Y | CFG | The tier this cadence is for |
| Interval Days | int | Y | | Days between reviews |
| Lead Days | int | Y | | Days before the due date when the first reminder goes |
| Escalate After Days | int | Y | | Days after the due date when the deputy is asked |

**C26 Reminder Rule.**

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Subject Kind | meaning(reminder subject) | Y | | What is due: review, document, approval or claim (a fixed list, new) |
| Lead Days | int | Y | | Days before due when the first reminder goes |
| Repeat Days | int | Y | | Days between repeat reminders |
| Escalate After Days | int | Y | | Days after due when the deputy is asked |
| Channel | ref(C07) | Y | CFG | Where the reminder goes, from the `channel` list |

**C27 Party.** Taxonomy columns only. The seven parties of D-01.

**C28 Owner Role.**

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Party | ref(C27) | Y | CFG | The party this owner role belongs to |
| Requires Deputy | bool | Y | | Yes when every holder must have a deputy |
| Maximum Holders | int | N | | How many principals may hold it for one application at once |

**C34 Target Stage.**

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Rank | int | Y | | Order from development to production. Used only for ordering |

**C35 Change Path.**

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Allowed Routes | json | Y | CFG | Keys of the deployment routes this path accepts |

**C36 Deployment Route.** Engine Meaning: pipeline step, AI tool, person in app or other.

**C37 Link Kind.**

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Link Category | meaning(link category) | Y | | Delivery, pipeline, document, evidence, Microsoft resource or external change |
| Tool | ref(C07) | Y | CFG | The tool, from the `tool` list |
| Url Pattern | text(400) | N | | A pattern a valid URL of this kind matches |
| Id Pattern | text(200) | N | | A pattern a valid ID of this kind matches |

**C38 Actor Kind.** Engine Meaning: one of the six actor meanings. Only "person" can approve. The rows supply labels and descriptions. Operational rows store the meaning itself (section 2.1.1), except O10, which also references the row.

**C39 Guideline.** One version of one numbered guideline. Its Key combines number and version, such as `g9-v2-1`.

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Number | text(8) | Y | | The permanent guideline number, such as G9 |
| Version | text(10) | Y | | Two-part version: the first part changes the rule, the second only the wording |
| Rule | text(500) | Y | | The rule in one line |
| Hub Url | url | Y | | The link to the canonical text |
| Chapter | text(100) | Y | | The playbook chapter this guideline belongs to |
| Local Text | longtext | N | | The client's own wording |
| Local Url | url | N | | The client's own page |
| Is Adopted | bool | Y | | No when the client chose not to adopt it |
| Not Adopted Reason | longtext | C | | Required when Is Adopted is No |
| Supersedes | ref(C39) | N | CFG | The earlier version this row replaces |

### 3.3 Rules

**C29 Role.** A product-wide responsibility, held only through an Entra group (D-11). The AI lead's default Duties text uses the sweep wording: "decides on the agents Microsoft flags" (sweep proposal, D-20).

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Party | ref(C27) | Y | CFG | The party this role belongs to |
| Duties | longtext | Y | | The role's duties in plain text, for people and agents |
| Can Change Configuration | bool | Y | | Yes only for the configuration admin role (D-10) |
| Is Read Only | bool | Y | | Yes for roles that never write, such as auditor |
| Is Workload Role | bool | Y | | Yes for roles held by non-human workloads through a workload grant (C42), never by people. Seed: `deployment-recorder` and `approval-writer` (28, issue 7) |

**C30 Role Group Binding.**

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Role | ref(C29) | Y | CFG | The product role being bound |
| Entra Group | ref(O05) | Y | PR | The group, as a principal reference whose kind is group |
| Binding Method | meaning(binding method) | Y | | Dataverse group team, or Entra app role assignment (a fixed list, new) |
| Binding Detail | text(200) | Y | | The team ID or the app role value on the platform |
| Last Verified On | datetime | N | | When the binding was last checked to exist on the platform |

Rule: C30 binds a role to a group, and only to a group (document 21, V35; 28, issue 8). It has no workload binding kind. Workload roles reach non-human principals only through C42.

**C42 Workload Grant (new entity, proposed ID; document 26, section 3.4).** One non-human principal holding one workload role directly, with a reason, the deciding decision and a required expiry (28, issue 8). It is the stated exception to D-11 for non-human workloads only (working default, open design question 12, see 28-working-defaults.md). People still get roles only through groups. Agent identities and agent users never get a grant. The setup service adds grants through a configuration change. None ship.

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Principal | ref(O05) | Y | PR | The non-human principal: a Principal Reference of kind service principal or managed identity. Never an agent identity or agent user |
| Workload Role | ref(C29) | Y | CFG | The workload role it holds: a role with Is Workload Role = Yes, such as deployment recorder or approval writer |
| Reason | longtext | Y | | Why this principal needs the role, in plain words, such as "Release pipeline of the expense app" |
| Uses Federated Credential | bool | Y | | Yes when the principal signs in without a stored secret (workload identity federation or a managed identity). No is flagged by the drift check |
| Binding Detail | text(200) | Y | | The application user ID on Dataverse, or the app role assignment ID on Azure |
| Granted By Decision | ref(O09) | Y | OPS | The configuration change decision that allowed the grant |
| Expires On | date | Y | | When the grant ends. Required: every grant expires, at most the `workload-grant-max-days` setting (default 365) after the day it is validated (document 21, V28), and is renewed by a new decision. An expired grant admits no app-only token (document 26, RA-07) and is flagged by the drift check (document 26, CHK-09) |
| Last Verified On | datetime | N | | When the drift check last confirmed the grant matches the platform (document 26, CHK-09) |

Rules: Workload Role must have Is Workload Role = Yes. Principal kind must be service principal or managed identity. The direct assignment check lists a granted assignment as allowed and flags any other direct assignment (document 26, CHK-02 and CHK-09).

**C31 Decision Right.**

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Decision Type | ref(C18) | Y | CFG | The decision type this right is for |
| Tier | ref(C08) | N | CFG | Optional scope |
| Lifecycle Stage | ref(C12) | N | CFG | Optional scope |
| Change Type | ref(C20) | N | CFG | Optional scope |
| Application Kind | ref(C15) | N | CFG | Optional scope |
| Portfolio | ref(O01) | N | OPS | Optional scope. The most specific row wins (28, Q2) |
| Letter | meaning(decision right letter) | Y | | Responsible, accountable, consulted, informed or supports |
| Owner Role | ref(C28) | C | CFG | The owner role that holds the letter, when relative to the application |
| Role | ref(C29) | C | CFG | The product role that holds the letter, when organization-wide |

Rules: exactly one of Owner Role and Role, except on a row whose letter is supports, which names neither (document 21, V31). Exactly one accountable per scope, checked when a draft is validated and again at activation.

**C32 Approval Rule.**

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Decision Type | ref(C18) | Y | CFG | The decision type that needs this approval |
| Tier, Lifecycle Stage, Change Type, Application Kind, Target Stage | ref(C08), ref(C12), ref(C20), ref(C15), ref(C34) | N | CFG | Optional scope columns. The most specific matching rule group wins (rule below) |
| Portfolio | ref(O01) | N | OPS | Optional scope. The most specific row wins (28, Q2) |
| Approver Owner Role | ref(C28) | C | CFG | The owner role that must approve, when relative to the application |
| Approver Role | ref(C29) | C | CFG | The product role that must approve, when organization-wide |
| Approval Mode | meaning(approval mode) | Y | | Everyone must approve, first to respond, or sequential [S10] |
| Sequence | int | C | | Order among rules for the same decision. Required when the mode is sequential |
| Minimum Count | int | Y | | How many approvals from this role are needed. At least 1 |
| Timeout Days | int | Y | | Days before the approval expires. A flow run lasts at most 30 days, pending approvals included [T24], and an approval flow fails after waiting 28 days [T25], so the engine restarts longer ones before day 28 |
| Requires Typed Reason | bool | Y | | Yes when the approver must type a reason |
| Is Consultation (new) | bool | Y | | Yes when the card asks a consulted role for input, not for approval. The outcome is stored as "recommendation only" and never counts toward acceptance (28, issue 12; document 23) |
| Guideline | ref(C39) | N | CFG | The guideline this rule applies |

Rule: exactly one of Approver Owner Role and Approver Role. Which rules apply follows document 21: the single most specific rule group wins, and broader groups drop out (28, issue 2).

**C33 Gate Policy.**

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Tier | ref(C08) | Y | CFG | The tier this row is for |
| Change Type | ref(C20) | Y | CFG | The change type this row is for |
| Target Stage | ref(C34) | Y | CFG | The target stage this row is for |
| Portfolio | ref(O01) | N | OPS | Optional scope. The most specific row wins (28, Q2) |
| Gate Level | meaning(gate level) | Y | | Off, record, flag, advise or enforce (D-03 is open) |
| Fail Mode | meaning(fail mode) | C | | Fail open or fail closed. Required when the level is enforce |
| Emergency Rule Json | json | N | CFG | How an emergency passes, and when its approval must follow |
| AI Only Code Approval Handling | meaning(AI-only code approval handling) | N | | Ignore, flag, or stop at enforce, when the only code approval came from an AI (sweep proposal; a fixed list, new). Empty means the setting `default-ai-only-code-approval-handling` applies (document 21). It ships as flag, because AI-only code approvals never count as authorization (working default, open design question 7, see 28-working-defaults.md) |
| Set By Decision | ref(O09) | Y | OPS | The gate policy change decision that set this row |

---

## 4. Operational entities

Common columns (section 2.1.1) apply to every entity and are not repeated. Link columns are listed where the entity uses them.

### 4.1 Register

**O01 Portfolio.** A business grouping of applications. Portfolios nest. Class M.

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Parent Portfolio | ref(O01) | N | OPS | The portfolio this one sits inside. Empty at the top. No cycles (engine check) |
| Level | ref(C07) | Y | CFG | The level label, such as portfolio or application group, from the `portfolio-level` list |
| Accountable Principal | ref(O05) | N | PR | The person who answers for the portfolio, if the client names one |
| Is Active (new) | bool | Y | | No when the portfolio is retired. Retired portfolios keep their applications' history |

**O02 Business Application.** The register's central record. Class M. Tier and stage change only through accepted decisions.

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Record Number (new) | text(20) | Y | | A human-friendly number such as APP-00042, assigned by the engine, unique, never reused |
| Purpose | longtext | Y | | What the application is for, in one or two plain sentences |
| Application Kind | ref(C15) | Y | CFG | Its category |
| Portfolio | ref(O01) | C | OPS | Where it sits. Required once registered |
| Status | ref(C05) | Y | CFG | Registration state label |
| Status Meaning | meaning(registration status) | Y | | Meaning copy: draft, registered or archived. AI-created rows start as draft (D-02) |
| Tier | ref(C08) | C | CFG | The current tier, a stored value. Required once registered |
| Tier Decision | ref(O09) | C | OPS | The accepted decision that set the current tier. Required whenever Tier is set |
| Lifecycle Stage | ref(C12) | Y | CFG | The current stage |
| Stage Decision | ref(O09) | C | OPS | The accepted decision that moved it to this stage. Empty only at the first stage |
| Stage Since | datetime | Y | | When it entered the current stage |
| Build Path | ref(C07) | N | CFG | Citizen, fusion, pro or AI-built, from the `build-path` list |
| Uses AI | bool | N | | The owner's statement that it uses AI models or tools. Empty means not yet stated |
| Agreed Autonomy Level | ref(C07) | C | CFG | For agents: the autonomy level agreed by decision, from the `autonomy-level` list. Enforcing it stays in Microsoft's tools |
| Autonomy Decision | ref(O09) | C | OPS | Required whenever Agreed Autonomy Level is set |
| Next Review On | date | N | | Computed by the engine from the review cadences |

**O03 Technical Component.** One part of an application in a tool, held as a link. Class E.

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Business Application | ref(O02) | Y | OPS | The application this part belongs to |
| Component Kind | ref(C16) | Y | CFG | Its type |
| Link Kind | ref(C37) | Y | CFG | The Microsoft resource or tool link kind (category Microsoft resource) |
| External Id | text(400) | C | | The tool's own ID, such as the inventory resource `name` [T22] or an Entra object ID. Required unless only a URL exists (a SaaS page) |
| Url | url | C | | A link that opens the item. At least one of External Id and Url |
| Label As Seen | text(200) | Y | | The item's name when last seen |
| Deployment Target | ref(O15) | N | OPS | Where the item lives, if it lives in a target |
| Last Seen On | datetime | N | | When the product last confirmed it exists |
| Owner Differs | bool | N | | Yes when the gap check found Microsoft's listed owner differs from the register's owners. Microsoft's owner is not stored (fit correction 8) |
| Owner Checked On | datetime | N | | When the owner comparison last ran |
| Replaces Component | ref(O03) | N | OPS | The component this one replaced, for example after a migration that creates a new app ID (sweep proposal) |
| Source Claim | ref(O23) | N | OPS | The claim this component came from |
| Retired On (new) | date | N | | When a person confirmed the item is gone. Set instead of deleting the row |

**O04 Dependency.** A typed, directed link from an application to an application or a component. Class E.

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| From Application | ref(O02) | Y | OPS | The application that depends |
| To Application | ref(O02) | C | OPS | The application depended on |
| To Component | ref(O03) | C | OPS | The component depended on |
| Dependency Type | ref(C17) | Y | CFG | The kind of dependency |
| Criticality | ref(C07) | N | CFG | How badly the dependant is hit if the target fails, from the `criticality` list |
| Notes | longtext | N | | Any detail an architect needs |
| Ended On (new) | date | N | | When the dependency stopped. Set instead of deleting the row. Impact walks ignore ended rows |

Rules: exactly one of To Application and To Component. To Application is never the From Application.

**O05 Principal Reference.** A minimal reference to a directory object. Class M. Never deleted.

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Principal Kind | meaning(principal kind) | Y | | User, group, agent identity, agent user, service principal or managed identity |
| Entra Object Id | text(36) | Y | | The directory object ID. The only identity fact the product relies on. For users it is the token's `oid` claim, an immutable identifier [T21]. For groups, the group object ID, which is immutable [T23] |
| Display Label | text(200) | N | | A name to show people, refreshed when seen. Cleared on erasure |
| Last Seen On | datetime | N | | When the product last saw the principal active |
| Platform User | platform user | N | | Dataverse binding only: the matching built-in user row, when one exists |
| Directory State (new) | meaning(directory state) | N | | Enabled, disabled, deleted or unknown, as last read from Entra (a fixed list, new). The owner check reads it (28, Q18; document 23, P08) |
| Directory State On (new) | datetime | N | | When Directory State was last read from Entra |
| Leaves On (new) | date | N | | The planned leave date, when a leave is recorded or read from Entra (document 23, P08) |
| Is Erased (new) | bool | Y | | Yes after an erasure request was carried out (section 2.7) |

**O06 Owner Assignment.** One principal holding one owner role for one application. Class E.

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Business Application | ref(O02) | Y | OPS | The application |
| Owner Role | ref(C28) | Y | CFG | The responsibility held |
| Principal | ref(O05) | Y | PR | Who holds it. A user, or a group where the client's playbook allows it (for example a support team) |
| Is Deputy | bool | Y | | Yes when this row is the deputy |
| Deputy For | ref(O06) | C | OPS | The main assignment this deputy covers. Required when Is Deputy is Yes |
| Start On | date | Y | | When the assignment starts |
| End On | date | N | | When it ends. Empty while current. Set instead of deleting the row |
| Ownership Decision | ref(O09) | N | OPS | The decision that made the assignment, where the client's decision rights require one |

### 4.2 Intake and decisions

**O07 Intake Request.** One request through the front door. Class M.

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Record Number (new) | text(20) | Y | | A number such as INT-00123, assigned by the engine |
| Requester | ref(O05) | Y | PR | Who asked |
| Title | text(200) | Y | | A short name for the request |
| Need | longtext | Y | | The business need in plain words |
| Proposed Kind | ref(C15) | N | CFG | The application kind the requester expects |
| Business Application | ref(O02) | N | OPS | The application this request is about, once known or created |
| Status | ref(C05) | Y | CFG | Status label |
| Status Meaning | meaning(record status) | Y | | Meaning copy: draft, submitted, in progress, completed, rejected, withdrawn or cancelled. An AI-drafted request stays draft until a person submits it |
| Duplicate Of | ref(O02) | N | OPS | An existing application this request matches |
| Score | decimal | N | | The PMO score for demand that needs central capacity |
| Horizon | ref(C07) | N | CFG | Now, next or later, from the `horizon` list |
| Size Band | ref(C07) | N | CFG | A rough size, from the `size-band` list |
| Expected Benefit | longtext | N | | The benefit claimed at intake |
| Uses AI | bool | N | | The requester's statement that it will use AI |
| Triage Decision | ref(O09) | N | OPS | The accepted triage decision. Kept apart from delivery status [S3] |

**O08 Tier Assessment.** One set of tier answers and the tier they imply. Class F: frozen when Tier Decision is set.

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Intake Request | ref(O07) | C | OPS | The request assessed, when assessed at intake |
| Business Application | ref(O02) | C | OPS | The application assessed, when assessed later |
| Answers Json | json | Y | CFG | The question keys and answer keys given, as `[{"question": "...", "answer": "..."}]` |
| Computed Tier | ref(C08) | Y | CFG | The most serious tier the answers imply, computed by the engine |
| Assessed By | ref(O05) | Y | PR | Who gave the answers |
| Tier Decision | ref(O09) | N | OPS | The decision that set the tier from this assessment. A person may raise the tier. Lowering it needs a decision |

Rule: exactly one of Intake Request and Business Application.

**O09 Decision.** One entry in the append-only decision log. Class F. Never deleted.

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Record Number (new) | text(20) | Y | | A number such as DEC-00456, assigned by the engine, never reused |
| Decision Type | ref(C18) | Y | CFG | The kind of decision |
| Subject Kind | text(64) | Y | | Which entity the decision is about (subject kind list, section 2.6) |
| Subject Application | ref(O02) | C | OPS | Set when the subject is a business application |
| Subject Intake Request | ref(O07) | C | OPS | Set when the subject is an intake request |
| Subject Change Request | ref(O11) | C | OPS | Set when the subject is a change request |
| Subject Deployment Record | ref(O14) | C | OPS | Set when the subject is a deployment record, for example an exception for a flagged deployment |
| Subject Component | ref(O03) | C | OPS | Set when the subject is a technical component |
| Subject Claim | ref(O23) | C | OPS | Set when the subject is a claim, for example the AI lead's keep, reassign, consolidate or retire decision on an agent Microsoft flagged (sweep proposal) |
| Subject Configuration Draft (new) | ref(C46) | C | CFG | Set for a configuration change or gate policy change decision: the submitted draft. It replaces the earlier subjects configuration set version and gate policy row, because neither exists until activation (28, issue 3) |
| Statement | text(500) | Y | | One sentence: "Decided: what" |
| Context | longtext | N | | Why the decision was needed |
| Options Considered | longtext | C | | The options weighed and rejected. Required to accept when the decision type needs them [S1] |
| Outcome | ref(C19) | C | CFG | The chosen outcome. Required to accept. Must belong to the decision type |
| Reason | longtext | C | | Why this outcome. Required to accept |
| Status | ref(C05) | Y | CFG | Status label |
| Status Meaning | meaning(decision status) | Y | | Meaning copy: proposed, accepted, rejected or withdrawn. "Superseded" is derived (section 8.1) |
| Decider | ref(O05) | C | PR | The accountable person. Required to accept. Must be a principal of kind user acting as a person |
| Decided On | datetime | C | | When it was accepted or rejected |
| Supersedes | ref(O09) | N | OPS | The earlier decision this one replaces [S1] |
| Amends | ref(O09) | N | OPS | The earlier decision this one changes in part. Declined amendments stay listed (I-191) |
| Confidence | ref(C07) | N | CFG | How sure the decider is, from the `confidence` list [S1] |
| Superseded By (new) | ref(O09) | N | OPS | Derived: the accepted decision whose Supersedes points here. Computed in a view on Azure. Stored by the engine on Dataverse, as the only write a frozen row ever receives (section 8.1) |

Rule: exactly one Subject column is set, and it matches Subject Kind.

**O10 Approval Record.** One required approval for one decision. Class F: frozen once Outcome Meaning leaves pending. Never deleted.

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Decision | ref(O09) | Y | OPS | The decision this approval is for |
| Approval Rule | ref(C32) | Y | CFG | The rule that required it |
| Required Owner Role | ref(C28) | C | CFG | The owner role asked, resolved from the rule |
| Required Role | ref(C29) | C | CFG | The product role asked, resolved from the rule |
| Asked Principal (new) | ref(O05) | N | PR | Who the card went to: a user, or the group bound to the required role |
| Approver | ref(O05) | C | PR | Who answered. Required once answered |
| Approver Actor Kind | ref(C38) | C | CFG | The actor kind of the answer (approver kind, sweep proposal) |
| Approver Actor Meaning (new) | meaning(actor kind) | C | | Meaning copy of the approver kind. Only "person" counts |
| Approver Principal Kind | meaning(principal kind) | C | | The directory object type of the responder, copied from the Principal Reference. Only "user" counts (sweep proposal) |
| Outcome | ref(C05) | Y | CFG | Outcome label |
| Outcome Meaning | meaning(approval outcome) | Y | | Meaning copy: pending, approved, rejected, expired, cancelled or recommendation only. A consultation card (C32 Is Consultation) and an AI stage in a client's own flow can only give recommendation only |
| Typed Reason | longtext | C | | The approver's reason, in their words. Required when the rule requires it |
| Requested On | datetime | Y | | When the approval card went out |
| Responded On | datetime | C | | When it was answered |
| Expires On | datetime | Y | | When it times out |
| Channel | ref(C07) | N | CFG | Outlook, Teams, the app or another channel, as reported, from the `channel` list |
| Flow Run Url | url | C | | A link to the approval runner's run that wrote the outcome. Required once answered |
| Counts Toward Requirement | bool | Y | | Computed by the engine when the outcome is written: yes only for an approved outcome from a person who holds the required role and passes the separation rules (C40, document 21; 28, issue 5) |

Rules: exactly one of Required Owner Role and Required Role. Only the approval writer (section 2.4) writes Approver, Approver Actor Kind, Approver Actor Meaning, Approver Principal Kind, Typed Reason, Responded On, Flow Run Url, and the outcomes approved, rejected and recommendation only. The engine writes only expired and cancelled. A restarted approval is a new row.

**Proposed by the build (2026-11-22).** Three more columns the runtime writes (Decision Right, Is Consultation, Approver Holds Required Role), and Approval Rule read as "Approval Rule or Decision Right", are in B22-1 at the end of this document.

### 4.3 Change and deployment

**O11 Change Request.** One planned change. Class M.

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Record Number (new) | text(20) | Y | | A number such as CHG-00789, assigned by the engine |
| Title | text(200) | Y | | A short name for the change |
| Business Application | ref(O02) | Y | OPS | The main application changed |
| Change Type | ref(C20) | Y | CFG | Standard, normal, emergency or a client type |
| Standard Change Model | ref(C21) | C | CFG | Required when the change type is a standard type |
| Status | ref(C05) | Y | CFG | Status label |
| Status Meaning | meaning(record status) | Y | | Meaning copy, as for O07 |
| Mode | meaning(change management mode) | Y | | Built in or linked, copied from the setting when the request was created (D-08) |
| External Change Number | text(100) | C | | Linked mode: the ITSM tool's change number. Required in linked mode. A recorded number alone never counts as authorization (28, Q22) |
| External Change Url | url | C | | Linked mode: the link to the ITSM change |
| Window Start | datetime | C | | When the change may start (D-13). Required to submit when the change type requires authorization |
| Window End | datetime | C | | When the change must be done. Not before Window Start |
| Planned Target | ref(O15) | N | OPS | The target the change is planned for |
| Authorization Decision | ref(O09) | C | OPS | The change authorization decision. Required before completion when the change type requires authorization, unless it allows authorization after |
| Result | ref(C07) | C | CFG | How the change ended, from the `change-result` list. Required when completed |

**O12 Affected Application.** A further application a change touches. Class M. Owned child of O11.

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Change Request | ref(O11) | Y | OPS | The change |
| Business Application | ref(O02) | Y | OPS | A further application it touches. Never the change's main application |
| Impact Role | ref(C07) | Y | CFG | Changed, depends on the change, or informed, from the `impact-role` list |

**O13 Artifact Version.** One version of one deployable artifact. Class F with fill-once columns (new): Digest, Source Commit and Source Repository Url can be filled once when empty and never changed.

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Business Application | ref(O02) | Y | OPS | The main application this artifact belongs to |
| Artifact Kind | ref(C07) | Y | CFG | Managed solution, container image, code package, static site, agent package, commit or other, from the `artifact-kind` list (I-179) |
| Artifact Name | text(200) | Y | | The artifact's name, such as a solution unique name or an image name |
| Version | text(100) | Y | | The version text. Artifact Name plus Version is unique (I-178) |
| Digest | hash | N | | A hash of the artifact, when the sender provides one. Fill-once |
| Source Commit | text(64) | N | | The source commit it was built from. Fill-once |
| Source Repository Url | url | N | | The repository it came from. Fill-once |
| Release Record Url | url | N | | A link to the client's own release record, such as the release record a CLI deployment keeps [J2] |

**O14 Deployment Record.** One artifact version reaching one target. Class F, final at insert. Never deleted.

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Business Application | ref(O02) | Y | OPS | The application deployed |
| Artifact Version | ref(O13) | Y | OPS | What was deployed |
| Deployment Target | ref(O15) | Y | OPS | Where it went |
| Change Request | ref(O11) | N | OPS | The change it belongs to, if any |
| Pipeline Reference | ref(O16) | N | OPS | The pipeline that ran it, if any |
| Deployment Route | ref(C36) | Y | CFG | How the record arrived |
| Run Url | url | N | | A link to the pipeline run or the tool's own record. The product's record outlives the run, because some tools delete run history (sweep proposal) |
| Started On | datetime | N | | When the deployment started |
| Finished On | datetime | Y | | When it finished |
| Result | ref(C05) | Y | CFG | Result label |
| Result Meaning | meaning(deployment result) | Y | | Meaning copy: succeeded, partly succeeded, failed or not deployed |
| Version Before | text(100) | N | | The version on the target before, when the sender read it (I-181) |
| Version After | text(100) | N | | The version on the target after, read back |
| Deployed By | ref(O05) | Y | PR | The principal that ran the deployment: a person, or a pipeline's workload identity |
| Started By AI Client | text(200) | N | | The AI client that queued the run on a person's behalf, as reported (sweep proposal) |
| Merge Actor | ref(O05) | N | PR | Who completed the merge that triggered the run, as reported (sweep proposal) |
| Merge Actor Kind (new) | meaning(actor kind) | N | | The actor kind of the merge actor, as reported, so an AI merge is visible (sweep proposal) |
| Reported Approvals Json (new) | json | N | | Approvals the pipeline or code tool reported: approver object ID, approver kind, time. Evidence only. Never counts toward the product's approval rules |
| Stage Order Run | json | N | | The order of pipeline stages actually run (sweep proposal) |
| Gate Level Applied | meaning(gate level) | Y | | The gate level in force for this change at this target when the record was written |
| Is Outside Window | bool | Y | | Yes when it finished outside the change request's window (D-13) |
| Is Other Path | bool | Y | | Yes when the route is not allowed by the target's change path (D-19) |
| Is Missing Approval | bool | Y | | Yes when the change lacked the approvals its policy requires, at write time |
| Idempotency Key | text(200) | Y | | The sender's run and stage identifier. Unique. A retry with the same key returns the existing record (I-189). Engine-generated for records entered by a person |
| Corrects Record (new) | ref(O14) | N | OPS | The earlier record this one corrects. The earlier record stays, and views show the latest correction |
| Tested In | ref(O14) | N | OPS | Later: the test deployment this production deployment came from (I-180) |

**O15 Deployment Target.** A place deployments land, held as a link. Class M.

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Target Kind | ref(C07) | Y | CFG | Power Platform environment, Microsoft 365 tenant, Azure resource group, GitHub environment, SaaS tenant or other, from the `target-kind` list |
| Target Stage | ref(C34) | Y | CFG | Development, test, production or a client stage |
| Change Path | ref(C35) | Y | CFG | The routes this target expects |
| Link Kind | ref(C37) | Y | CFG | The Microsoft resource or tool link kind |
| External Id | text(400) | Y | | The tool's ID, such as a Power Platform environment ID |
| Url | url | N | | A link that opens the target |
| Label As Seen | text(200) | Y | | The target's name when last seen |
| Last Seen On | datetime | N | | When the product last confirmed it exists |

**O16 Pipeline Reference.** A link to one client pipeline. The product never builds, hosts or runs it (D-19). Class M.

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Link Kind | ref(C37) | Y | CFG | The pipeline kind (category pipeline), such as an Azure Pipelines pipeline, a GitHub Actions workflow, or a CLI or runtime deployment (sweep proposal) |
| External Id | text(400) | Y | | The tool's ID for the pipeline |
| Url | url | Y | | A link that opens the pipeline |
| Label As Seen | text(200) | Y | | The pipeline's name when last seen |
| Last Seen On | datetime | N | | When a record step or a person last confirmed it |
| Is Moved | bool | Y | | Yes when the pipeline or its repository moved (sweep proposal) |
| Moved To Url | url | C | | The new location. Required when Is Moved is Yes |

**O17 Pipeline Map Entry.** This pipeline deploys this application to this target. Class M.

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Business Application | ref(O02) | Y | OPS | The application deployed |
| Pipeline Reference | ref(O16) | Y | OPS | The pipeline that deploys it |
| Deployment Target | ref(O15) | Y | OPS | The target it deploys to |
| Stage Order | int | Y | | The position of this target in the pipeline's stages |

### 4.4 Links and evidence

**O18 Delivery Link.** A link from an application, change or intake to work in another tool (D-12). Class M.

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Subject Kind | text(64) | Y | | Business application, change request or intake request |
| Subject Application | ref(O02) | C | OPS | Set when the subject is an application |
| Subject Change Request | ref(O11) | C | OPS | Set when the subject is a change request |
| Subject Intake Request | ref(O07) | C | OPS | Set when the subject is an intake request |
| Link Kind | ref(C37) | Y | CFG | Work item, repository, plan, project or another delivery kind (category delivery). Planner unified plans and premium plans are two kinds (fit correction 27) |
| External Id | text(400) | N | | The tool's ID, such as a work item number |
| Url | url | Y | | A link that opens the item |
| Label | text(200) | Y | | A short label |
| Linked On | datetime | Y | | When the link was added |
| Last Seen On | datetime | N | | When the link was last confirmed |
| Is Moved | bool | Y | | Yes when the target moved, such as a repository migrated to another host (sweep proposal) |
| Moved To Url | url | C | | The new location. Required when Is Moved is Yes |

**O19 Document Link.** One linked document for one application, with status (D-17). The product never writes the document. A row exists only once a link has been recorded. Class M.

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Business Application | ref(O02) | Y | OPS | The application documented |
| Documentation Requirement | ref(C23) | N | CFG | The requirement this document meets, if any |
| Document Type | ref(C22) | Y | CFG | The kind of document |
| Link Kind | ref(C37) | Y | CFG | Wiki page, repository file, SharePoint page or another document kind (category document) |
| Url | url | Y | | A link to the document |
| Status | ref(C05) | Y | CFG | Status label |
| Status Meaning | meaning(document status) | Y | | Meaning copy: draft, current or stale. Never missing |
| Document Owner | ref(O05) | Y | PR | Who keeps the document |
| Last Updated On | date | N | | When the owner says it last changed |
| Last Checked On | date | N | | When someone last confirmed it is still true |
| Stale On | date | N | | Computed: Last Checked On plus the document type's Freshness Days |

**"Missing" is computed, never stored (28, issue 16).** A required document is missing when an active C23 requirement applies to the application (tier, kind, and From Stage by Stage Rank) and no O19 row for it has the meaning draft, current or stale. The engine and the pages work this out from configuration, so a stored row and a computation can never disagree. The meaning "missing" stays in the document status contract (document 20, section 2.4) for labels in computed views, the same way "superseded" stays for decisions.

**O20 Evidence Link.** A link from a record to proof held elsewhere. Class A. Never deleted.

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Subject Kind | text(64) | Y | | Decision, approval record, change request, deployment record, review or claim |
| Subject Decision | ref(O09) | C | OPS | Set when the subject is a decision |
| Subject Approval Record | ref(O10) | C | OPS | Set when the subject is an approval record |
| Subject Change Request | ref(O11) | C | OPS | Set when the subject is a change request, for example an impact list a Microsoft tool produced (sweep proposal) |
| Subject Deployment Record | ref(O14) | C | OPS | Set when the subject is a deployment record |
| Subject Review | ref(O21) | C | OPS | Set when the subject is a review |
| Subject Claim | ref(O23) | C | OPS | Set when the subject is a claim |
| Link Kind | ref(C37) | Y | CFG | The evidence kind (category evidence) |
| Url | url | Y | | A link to the evidence |
| Label | text(200) | Y | | A short label |
| Evidence Level | ref(C07) | Y | CFG | Read, checked, as reported, proven, designed, or not read with a reason, from the `evidence-level` list (I-44) |
| Captured On | datetime | Y | | When the evidence was linked |
| Digest | hash | N | | A hash of the evidence file, when available |

A wrong evidence link is never removed. A new evidence link with Evidence Level "not read" and a reason sits next to it.

### 4.5 Reviews

**O21 Review.** One periodic or event check on an application. Class M.

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Business Application | ref(O02) | Y | OPS | The application reviewed |
| Review Type | ref(C24) | Y | CFG | The kind of review |
| Status | ref(C05) | Y | CFG | Status label |
| Status Meaning | meaning(review status) | Y | | Meaning copy: due, done or cancelled |
| Trigger (new) | meaning(review trigger) | Y | | What caused the review: cadence, tier answer trigger, owner check or request (a fixed list, new). Details, such as the trigger key or the requester's reason, go in Description (document 23, P04 step 1 and P08) |
| Due On | date | Y | | When it is due |
| Done On | date | C | | When it was done. Required when done |
| Reviewer | ref(O05) | C | PR | Who did it. Required when done |
| Outcome Decision | ref(O09) | C | OPS | The decision the review ended with. Required when done |
| Next Due On | date | N | | Computed from the cadence when done |

### 4.6 Gap check

**O22 Gap Check Run.** One execution of the gap check, run live by an admin as themselves (D-15). Class F: frozen when Finished On is set.

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Run By | ref(O05) | Y | PR | The admin who ran it |
| Admin Role Used | text(100) | Y | | The Entra role the admin held, as seen. Four roles see the whole inventory; AI administrator and AI reader see agents, agentic apps, agent flows, environments and environment groups only [S6] |
| Estates Covered | json | Y | CFG | Keys of the estates the run read, from the `estate` list |
| Started On | datetime | Y | | When it started |
| Finished On | datetime | N | | When it finished. Empty while running or after a failure |
| Items Seen | int | N | | How many items the run compared |
| New Claims | int | N | | How many new unregistered claims it added |
| Vanished Claims (new) | int | N | | How many registered components it did not see |

**Proposed by the build (2026-11-22).** Estates Covered must be a list of distinct keys; whether each key must be a value of the `estate` list is an open design question (B22-4 at the end of this document).

**O23 Claim.** One item on the claim list. Class M. Section 9 describes its lifecycle.

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Gap Check Run | ref(O22) | Y | OPS | The run that first found it |
| Last Seen Run (new) | ref(O22) | Y | OPS | The latest run that saw it |
| Finding Kind | meaning(finding kind) | Y | | Unregistered or vanished (the second kind is a sweep proposal) |
| Claim Key (new) | text(450) | Y | | Engine-built: finding kind, source type and External Id, joined with a separator. Unique. A later run updates the same claim instead of adding a new one |
| Component Kind | ref(C16) | Y | CFG | The item's type, matched through the component kind's Source Type Hint |
| Source Type As Seen (new) | text(200) | Y | | The tool's own type value, such as the inventory `type` [T22] |
| External Id | text(400) | Y | | The item's Microsoft ID, such as the inventory resource `name` [T22] |
| Url | url | N | | A link to the item in Microsoft's tool |
| Label As Seen | text(200) | Y | | Its name when found |
| Target As Seen | text(200) | N | | The environment or tenant it was found in, as a label |
| Suggested Claimant | ref(O05) | N | PR | The person Microsoft listed as owner at check time. A hint only (fit correction 8) |
| Status | ref(C05) | Y | CFG | Status label |
| Status Meaning | meaning(claim status) | Y | | Meaning copy: open, claimed or dismissed |
| Claimed By | ref(O05) | C | PR | Who claimed it. Required when claimed |
| Claimed On | datetime | C | | When it was claimed |
| Resulting Component | ref(O03) | C | OPS | Unregistered claims: the technical component created when it was claimed |
| Registered Component (new) | ref(O03) | C | OPS | Vanished claims: the registered component that was not seen. Required for that finding kind |
| Dismiss Reason | longtext | C | | Why it was dismissed, such as "not a business application". Required when dismissed |
| Last Seen On (new) | datetime | Y | | When a run last saw the item (unregistered) or last confirmed it missing (vanished) |
| Times Seen (new) | int | Y | | How many runs have seen it |

**Proposed by the build (2026-11-22).** The engine-built Claim Key can reach 614 characters where the column allows 450: widen it or store a hash (B22-2 at the end of this document).

### 4.7 Audit

**O24 Audit Entry.** One product event. Class A. Never deleted. Scope differs per platform (section 8.5).

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Entity Name | text(64) | Y | | The logical entity the event is about |
| Row Id | id | Y | | The row the event is about (no reference, because it can be any entity) |
| Operation | text(50) | Y | | Create, update, delete, approval outcome written, reminder sent, gap check run, configuration activated, write outside the engine, or another product event |
| Changed Values Json | json | N | | Old and new values for a data change. Never secrets, never principal labels |
| Actor | ref(O05) | Y | PR | The principal that acted |
| Actor Kind | meaning(actor kind) | Y | | The actor kind |
| On Behalf Of | ref(O05) | N | PR | The person an AI client or pipeline acted for |
| Client App | text(200) | N | | The client used |
| Client App Verified (new) | bool | Y | | As in the common columns |
| Occurred On | datetime | Y | | When it happened |
| Correlation Id | text(64) | Y | | A value that ties together the events of one call or one multi-step tool |
| Request Key (new) | text(200) | N | | The caller's idempotency key for a write call. Unique together with Operation when set. A replay with the same key and hash returns the first result (document 25) |
| Request Hash (new) | hash | C | | A hash of the request payload. A reused key with a different hash is refused. Required when Request Key is set |

---

## 5. Later entities

Kept in the model, not built first. Attributes are a first cut.

| ID | Entity | Class | Attributes (type, required) | Relationships |
|---|---|---|---|---|
| L01 | Data Area | M | Classification ref(C07) Y; Data Owner ref(O05) Y; Steward ref(O05) N; Retention Rule text(200) N; Copy To Test Rule ref(C07) N (I-86) | Decisions of type data use point to it (a new Subject Data Area column on O09) |
| L02 | Checklist Template | V | Applies To text(64) Y (stage, review type or retirement); Items Json json Y (item key, text, guideline key, evidence hint) | To C39 through item keys |
| L03 | Checklist Result | F | Checklist Template ref(L02) Y; Business Application ref(O02) Y; Results Json json Y (item key, result, by whom, date); Evidence through O20 (a new subject kind) (I-237) | Restrict to L02 and O02 |
| L04 | Standard | V | Owner Role ref(C28) or Role ref(C29), one Y; Standard Status ref(C05) Y; Standard Url url Y | Exception decisions point to it |
| L05 | Gate Check | A | Change Request ref(O11) N; Deployment Target ref(O15) Y; Gate Policy ref(C33) Y; Asked On datetime Y; Answer meaning(gate answer: go, warn, stop) Y; Reasons Json json N; Idempotency Key text(200) Y | Restrict to O11, O15, C33 (D-03, after the gate decision) |

---

## 6. Relationship catalog

Read "child column to parent entity". Cardinality is written parent to child. "Delete" is the behavior when someone tries to delete the parent. Every relationship not marked Cascade is Restrict. On Azure, Restrict is `ON DELETE NO ACTION` and Cascade is `ON DELETE CASCADE`.

### 6.1 Relationships every row has

| ID | Child column | Parent | Cardinality | Required | Delete | Note |
|---|---|---|---|---|---|---|
| R00a | (every entity) Created By Principal | O05 | 1 to 0..n | Y | Restrict | O05 is never deleted |
| R00b | (every entity) Performed By Principal | O05 | 1 to 0..n | Y | Restrict | |
| R00c | (every entity) On Behalf Of | O05 | 0..1 to 0..n | C | Restrict | |
| R00d | (every operational entity) Configuration Set Version | C02 | 1 to 0..n | Y | Restrict | C02 is never deleted |
| R00e | (every taxonomy entity, C04 to C39 and C42) Configuration Set | C01 | 1 to 0..n | Y | Restrict | A set with rows can't be deleted |

### 6.2 Configuration

| ID | Child column | Parent | Cardinality | Required | Delete | Note |
|---|---|---|---|---|---|---|
| R01 | C01 Based On Preset | C01 | 0..1 to 0..n | N | Restrict | Self, preset to working set |
| R02 | C01 Current Version | C02 | 0..1 to 0..1 | N | Restrict | Set by the first activation |
| R03 | C02 Configuration Set | C01 | 1 to 1..n | Y | Restrict | |
| R04 | C02 Change Decision | O09 | 0..1 to 0..n | C | Restrict | |
| R05 | C02 Instructed By | O05 | 1 to 0..n | Y | Restrict | |
| R06 | C07 Value List | C06 | 1 to 0..n | Y | Restrict | Owned child. Live configuration rows are never deleted (28, issue 3) |
| R07 | C10 Tier Question | C09 | 1 to 1..n | Y | Restrict | Owned child, as R06 |
| R08 | C10 Implied Tier | C08 | 1 to 0..n | Y | Restrict | |
| R09 | C12 Leading Owner Role | C28 | 0..1 to 0..n | C | Restrict | |
| R10 | C12 Leading Role | C29 | 0..1 to 0..n | C | Restrict | |
| R11 | C13 After Stage | C12 | 1 to 0..n | Y | Restrict | |
| R12 | C13 Gate Decision Type | C18 | 1 to 0..n | Y | Restrict | |
| R13 | C14 Gate | C13 | 1 to 0..n | Y | Restrict | Owned child, as R06 |
| R14 | C14 Tier, Application Kind | C08, C15 | 0..1 to 0..n | N | Restrict | Two relationships |
| R15 | C14 Required Decision Type, Required Document Type, Required Review Type | C18, C22, C24 | 0..1 to 0..n | C | Restrict | Three relationships |
| R16 | C14, C32 Guideline | C39 | 0..1 to 0..n | N | Restrict | Two relationships |
| R17 | C19 Decision Type | C18 | 1 to 1..n | Y | Restrict | Owned child, as R06 |
| R18 | C19 Next Lifecycle Stage | C12 | 0..1 to 0..n | N | Restrict | |
| R19 | C21 Change Type, Tier | C20, C08 | 1 to 0..n, 0..1 to 0..n | Y, N | Restrict | |
| R20 | C21 Approved By Decision | O09 | 1 to 0..n | Y | Restrict | |
| R21 | C22 Default Link Kind | C37 | 0..1 to 0..n | N | Restrict | |
| R22 | C23 Document Type, Tier, Application Kind, From Stage | C22, C08, C15, C12 | 1 or 0..1 to 0..n | Y, N, N, Y | Restrict | Four relationships |
| R23 | C24 Produces Decision Type, Default Owner Role | C18, C28 | 1 or 0..1 to 0..n | Y, N | Restrict | |
| R24 | C25 Review Type, Tier | C24, C08 | 1 to 0..n | Y | Restrict | Unique pair |
| R25 | C26 Channel; and every value list reference in configuration | C07 | 1 or 0..1 to 0..n | per column | Restrict | Includes C11 Vocabulary, C15 Category, C16 Source Tool, C37 Tool |
| R26 | C28 Party, C29 Party | C27 | 1 to 0..n | Y | Restrict | |
| R27 | C30 Role | C29 | 1 to 0..n | Y | Restrict | Every human role needs at least one binding to be usable. Workload roles have no binding; they are held through C42 |
| R28 | C30 Entra Group | O05 | 1 to 0..n | Y | Restrict | Principal kind group only (V35) |
| R29 | C31 Decision Type; C32 Decision Type | C18 | 1 to 0..n | Y | Restrict | |
| R30 | C31, C32 scope columns (Tier, Lifecycle Stage, Change Type, Application Kind; C32 also Target Stage) | C08, C12, C20, C15, C34 | 0..1 to 0..n | N | Restrict | |
| R31 | C31, C32, C33 Portfolio | O01 | 0..1 to 0..n | N | Restrict | Optional portfolio scope (28, Q2) |
| R32 | C31 Owner Role, Role; C32 Approver Owner Role, Approver Role | C28, C29 | 0..1 to 0..n | C | Restrict | Exactly one per row |
| R33 | C33 Tier, Change Type, Target Stage | C08, C20, C34 | 1 to 0..n | Y | Restrict | Unique triple per scope |
| R34 | C33 Set By Decision | O09 | 1 to 0..n | Y | Restrict | |
| R35 | C39 Supersedes | C39 | 0..1 to 0..1 | N | Restrict | Self |
| R36 | C46 Configuration Set | C01 | 1 to 0..n | Y | Restrict | Drafts of a working set |
| R37 | C46 Instructed By; C46 Change Decision | O05; O09 | 1 to 0..n; 0..1 to 0..1 | Y; C | Restrict | Two relationships. Change Decision is set at submit |
| R38 | C46 Resulting Version; C02 Source Draft | C02; C46 | 0..1 to 0..1 | C | Restrict | Two relationships, one per direction. Both set by the activation operation |
| R39 | C42 Principal; C42 Workload Role; C42 Granted By Decision | O05; C29; O09 | 1 to 0..n | Y | Restrict | Three relationships. Principal kind service principal or managed identity only. Workload Role with Is Workload Role = Yes only |

### 6.3 Operational

| ID | Child column | Parent | Cardinality | Required | Delete | Note |
|---|---|---|---|---|---|---|
| R40 | O01 Parent Portfolio | O01 | 0..1 to 0..n | N | Restrict | Self. No cycles |
| R41 | O01 Level | C07 | 1 to 0..n | Y | Restrict | |
| R42 | O01 Accountable Principal | O05 | 0..1 to 0..n | N | Restrict | |
| R43 | O02 Portfolio | O01 | 0..1 to 0..n | C | Restrict | |
| R44 | O02 Application Kind, Status, Tier, Lifecycle Stage, Build Path, Agreed Autonomy Level | C15, C05, C08, C12, C07, C07 | 1 or 0..1 to 0..n | per column | Restrict | CFG |
| R45 | O02 Tier Decision, Stage Decision, Autonomy Decision | O09 | 0..1 to 0..n | C | Restrict | |
| R46 | O03 Business Application | O02 | 1 to 0..n | Y | Restrict | A draft application's components are deleted by the engine first |
| R47 | O03 Component Kind, Link Kind | C16, C37 | 1 to 0..n | Y | Restrict | |
| R48 | O03 Deployment Target | O15 | 0..1 to 0..n | N | Restrict | |
| R49 | O03 Replaces Component | O03 | 0..1 to 0..1 | N | Restrict | Self |
| R50 | O03 Source Claim | O23 | 0..1 to 0..1 | N | Restrict | |
| R51 | O04 From Application | O02 | 1 to 0..n | Y | Restrict | |
| R52 | O04 To Application | O02 | 0..1 to 0..n | C | Restrict | |
| R53 | O04 To Component | O03 | 0..1 to 0..n | C | Restrict | |
| R54 | O04 Dependency Type, Criticality | C17, C07 | 1 or 0..1 to 0..n | Y, N | Restrict | |
| R55 | O06 Business Application | O02 | 1 to 0..n | Y | Restrict | |
| R56 | O06 Owner Role | C28 | 1 to 0..n | Y | Restrict | |
| R57 | O06 Principal | O05 | 1 to 0..n | Y | Restrict | |
| R58 | O06 Deputy For | O06 | 0..1 to 0..n | C | Restrict | Self |
| R59 | O06 Ownership Decision | O09 | 0..1 to 0..n | N | Restrict | |
| R60 | O07 Requester | O05 | 1 to 0..n | Y | Restrict | |
| R61 | O07 Business Application, Duplicate Of | O02 | 0..1 to 0..n | N | Restrict | Two relationships |
| R62 | O07 Proposed Kind, Status, Horizon, Size Band | C15, C05, C07, C07 | 0..1 or 1 to 0..n | per column | Restrict | |
| R63 | O07 Triage Decision | O09 | 0..1 to 0..1 | N | Restrict | |
| R64 | O08 Intake Request, Business Application | O07, O02 | 0..1 to 0..n | C | Restrict | Exactly one |
| R65 | O08 Computed Tier | C08 | 1 to 0..n | Y | Restrict | |
| R66 | O08 Assessed By | O05 | 1 to 0..n | Y | Restrict | |
| R67 | O08 Tier Decision | O09 | 0..1 to 0..1 | N | Restrict | |
| R68 | O09 Decision Type, Outcome, Status, Confidence | C18, C19, C05, C07 | 1 or 0..1 to 0..n | per column | Restrict | |
| R69 | O09 Decider | O05 | 0..1 to 0..n | C | Restrict | |
| R70 | O09 Supersedes | O09 | 0..1 to 0..1 | N | Restrict | Self. One decision supersedes at most one, and is superseded by at most one accepted decision (engine rule) |
| R71 | O09 Amends | O09 | 0..1 to 0..n | N | Restrict | Self |
| R72 | O09 Subject columns | O02, O07, O11, O14, O03, O23, C46 | 0..1 to 0..n | C | Restrict | Exactly one set |
| R73 | O10 Decision | O09 | 1 to 0..n | Y | Restrict | |
| R74 | O10 Approval Rule, Required Owner Role, Required Role, Approver Actor Kind, Outcome, Channel | C32, C28, C29, C38, C05, C07 | 1 or 0..1 to 0..n | per column | Restrict | |
| R75 | O10 Asked Principal, Approver | O05 | 0..1 to 0..n | N, C | Restrict | |
| R76 | O11 Business Application | O02 | 1 to 0..n | Y | Restrict | |
| R77 | O11 Change Type, Standard Change Model, Status, Result | C20, C21, C05, C07 | 1 or 0..1 to 0..n | per column | Restrict | |
| R78 | O11 Planned Target | O15 | 0..1 to 0..n | N | Restrict | |
| R79 | O11 Authorization Decision | O09 | 0..1 to 0..1 | C | Restrict | |
| R80 | O12 Change Request | O11 | 1 to 0..n | Y | **Cascade** | Owned child, only while the change is a draft |
| R81 | O12 Business Application, Impact Role | O02, C07 | 1 to 0..n | Y | Restrict | Unique pair with the change |
| R82 | O13 Business Application, Artifact Kind | O02, C07 | 1 to 0..n | Y | Restrict | |
| R83 | O14 Business Application, Artifact Version, Deployment Target | O02, O13, O15 | 1 to 0..n | Y | Restrict | |
| R84 | O14 Change Request, Pipeline Reference | O11, O16 | 0..1 to 0..n | N | Restrict | |
| R85 | O14 Deployment Route, Result | C36, C05 | 1 to 0..n | Y | Restrict | |
| R86 | O14 Deployed By, Merge Actor | O05 | 1 or 0..1 to 0..n | Y, N | Restrict | |
| R87 | O14 Corrects Record, Tested In | O14 | 0..1 to 0..n | N | Restrict | Self |
| R88 | O15 Target Kind, Target Stage, Change Path, Link Kind | C07, C34, C35, C37 | 1 to 0..n | Y | Restrict | |
| R89 | O16 Link Kind | C37 | 1 to 0..n | Y | Restrict | |
| R90 | O17 Business Application, Pipeline Reference, Deployment Target | O02, O16, O15 | 1 to 0..n | Y | Restrict | Unique triple |
| R91 | O18 Subject columns | O02, O11, O07 | 0..1 to 0..n | C | Restrict | Exactly one set |
| R92 | O18 Link Kind | C37 | 1 to 0..n | Y | Restrict | Category delivery |
| R93 | O19 Business Application | O02 | 1 to 0..n | Y | Restrict | |
| R94 | O19 Documentation Requirement, Document Type, Link Kind, Status | C23, C22, C37, C05 | 0..1 or 1 to 0..n | per column | Restrict | |
| R95 | O19 Document Owner | O05 | 1 to 0..n | Y | Restrict | |
| R96 | O20 Subject columns | O09, O10, O11, O14, O21, O23 | 0..1 to 0..n | C | Restrict | Exactly one set |
| R97 | O20 Link Kind, Evidence Level | C37, C07 | 1 to 0..n | Y | Restrict | Category evidence |
| R98 | O21 Business Application, Review Type, Status | O02, C24, C05 | 1 to 0..n | Y | Restrict | |
| R99 | O21 Reviewer, Outcome Decision | O05, O09 | 0..1 to 0..n | C | Restrict | |
| R100 | O22 Run By | O05 | 1 to 0..n | Y | Restrict | |
| R101 | O23 Gap Check Run, Last Seen Run | O22 | 1 to 0..n | Y | Restrict | Two relationships |
| R102 | O23 Component Kind, Status | C16, C05 | 1 to 0..n | Y | Restrict | |
| R103 | O23 Suggested Claimant, Claimed By | O05 | 0..1 to 0..n | N, C | Restrict | |
| R104 | O23 Resulting Component, Registered Component | O03 | 0..1 to 0..n | C | Restrict | |
| R105 | O24 Actor, On Behalf Of | O05 | 1 or 0..1 to 0..n | Y, N | Restrict | |

Dataverse allows only one parental (cascading) relationship per child table pair, and a related table can't be the child of two cascading relationships [T2]. The one cascading child above (O12) has exactly one cascading parent.

---

## 7. Keys catalog

Every entity has its Id as primary key. Business and alternate keys below are unique. "Within set" means unique together with Configuration Set.

| Entity | Business key | Alternate keys | Microsoft or tool ID held |
|---|---|---|---|
| C01 Configuration Set | Name | Preset Key (presets; null on working sets) | |
| C02 Configuration Set Version | Configuration Set + Version Number | Content Hash | |
| C46 Configuration Draft | Record Number | | |
| C04 to C39 and C42 taxonomy (each) | Key, within set | C05: Key within set (keys are entity-prefixed, such as `decision-accepted`). C07: Value List + Key within set. C08: Rank within set. C11: Source Entity + Source Key + Vocabulary within set. C12: Stage Rank within set. C25: Review Type + Tier within set. C30: Role + Entra Group + Binding Method within set. C33: Tier + Change Type + Target Stage + Portfolio within set. C39: Number + Version within set. C42: Principal + Workload Role within set | C30 Binding Detail (team ID or app role value). C42 Binding Detail (application user ID or app role assignment ID) |
| O01 Portfolio | Name + Parent Portfolio | | |
| O02 Business Application | Record Number | | |
| O03 Technical Component | Component Kind + External Id, among rows with no Retired On (engine check) | Link Kind + External Id (alternate key on both platforms) | External Id: the tool's ID, such as the inventory resource `name` [T22] |
| O04 Dependency | From Application + To Application or To Component + Dependency Type, among rows with no Ended On (engine check) | | |
| O05 Principal Reference | Entra Object Id | | Entra Object Id |
| O06 Owner Assignment | none (history rows) | | |
| O07 Intake Request | Record Number | | |
| O08 Tier Assessment | none | | |
| O09 Decision | Record Number | | |
| O10 Approval Record | none | Flow Run Url, once answered (engine check) | Flow Run Url |
| O11 Change Request | Record Number | External Change Number in linked mode (engine check, not unique across tools) | External Change Number |
| O12 Affected Application | Change Request + Business Application | | |
| O13 Artifact Version | Artifact Name + Version | | Digest, Source Commit |
| O14 Deployment Record | Idempotency Key | | Run Url |
| O15 Deployment Target | Link Kind + External Id | | External Id, such as a Power Platform environment ID |
| O16 Pipeline Reference | Link Kind + External Id | | External Id |
| O17 Pipeline Map Entry | Business Application + Pipeline Reference + Deployment Target | | |
| O18 Delivery Link | Subject + Link Kind + External Id (or Url when there is no ID) | | External Id |
| O19 Document Link | Business Application + Documentation Requirement, when a requirement is set | | |
| O20 Evidence Link | none | | |
| O21 Review | Business Application + Review Type + Due On | | |
| O22 Gap Check Run | none | | |
| O23 Claim | Claim Key | | External Id, Source Type As Seen |
| O24 Audit Entry | none | Operation + Request Key, when Request Key is set | |

Rules for keys that carry tool IDs:
- External Id values are stored exactly as the tool returns them. The engine compares them case-insensitively where the tool's IDs are GUIDs.
- Configuration keys use only lowercase letters, digits and hyphens. This avoids the characters that break Dataverse alternate-key requests, such as `/ < > * % & : \ ? + #` [S15].
- Dataverse alternate keys do not enforce uniqueness on null values [S15]. Keys with an optional column (C01 Preset Key, C33 Portfolio, O24 Request Key) rely on that, and the engine checks the rest.

---

## 8. History, immutability and audit

### 8.1 Decisions: append-only in effect

| State (meaning) | Who moves it there | What may change | Next states |
|---|---|---|---|
| proposed | Created by a person, an AI client (Is AI Created) or the engine | Every content column. Approval records are created for it | accepted, rejected, withdrawn |
| accepted | The engine, when every required approval counts, the decider is a person who holds the accountable right, and Outcome and Reason are set. A decision type with no approval rule still goes on one card, to the holder of its accountable letter (C31; 28, issue 1). The engine accepts without any card only for a locked list: lowest-tier intake and standard changes (working default, open design question 8, see 28-working-defaults.md) | Nothing. On Dataverse only, the engine may set Superseded By | (none stored) |
| rejected | The engine, when a required approval is rejected, or the decider rejects | Nothing | (none) |
| withdrawn | The proposer or the decider, before acceptance | Nothing | (none) |

- **Supersede, never edit.** To change an accepted decision, a person proposes a new decision with Supersedes pointing to it. When the new one is accepted, the old one reads as superseded. Microsoft's ADR guidance: "Don't go back and edit accepted records" [S1].
- **"Superseded" is derived.** It is not a stored status change. On Azure, a view computes Superseded By from the later decision's Supersedes link, so the frozen row is never updated. On Dataverse, a reverse lookup can't be computed in a column, so the engine stores Superseded By as the single permitted write to a frozen row, made in the same transaction that accepts the superseding decision. The C05 meaning "superseded" stays in the contract for labels and filters.
- **Amends** changes part of an earlier decision. A declined amendment stays listed (I-191).
- **Downstream links point to accepted decisions only.** Tier Decision, Stage Decision, Autonomy Decision, Triage Decision, Authorization Decision and Outcome Decision must reference a decision whose meaning is accepted. When a decision is superseded, the engine re-points the current application fields to the superseding decision and keeps the old link in history.
- **AI can propose, never accept.** A decision proposed by an AI client stays proposed until a person accepts it through the approval and decider rules.

### 8.2 Approval records: immutable once answered

The approval runner is the set of approval flows that send the cards. The approval writer is the runner's workload identity, which holds the approval writer role through a workload grant (C42; 28, issue 7). On Azure its database principal is `approval_writer` (section 11.1).

| Column group | Engine | Approval writer | Anyone else |
|---|---|---|---|
| Request columns (Decision, Approval Rule, Required roles, Asked Principal, Requested On, Expires On) | Writes at creation | Never | Never |
| Outcome approved, rejected or recommendation only, with Approver, approver kinds, Typed Reason, Responded On, Flow Run Url | Never | Writes once, while pending | Never |
| Outcome expired or cancelled | Writes, while pending | Never | Never |
| Counts Toward Requirement | Computes when the outcome is written | Never | Never |
| Anything, after the outcome left pending | Never | Never | Never |

- **AI never approves.** An answer whose approver actor meaning is not person, or whose principal kind is not user, never counts. A recommendation from an AI stage in a client's own approval flow is stored with the outcome meaning "recommendation only" (fit correction 15). So is the answer to a consultation card (C32 Is Consultation).
- **Expiry and restart.** A flow run lasts at most 30 days, and "after 30 days, any pending steps time out". Longer approvals need approvals stored in Dataverse and a second flow that acts on the response [T24]. An approval flow itself fails after waiting 28 days, while the approval stays in the action center [T25]. When a rule's Timeout Days passes, the engine sets expired and creates a new approval record. Reminders do not change the record. They write an audit entry.

### 8.3 Deployment records: written once

1. **One write.** The record step, the owner's AI tool or a person sends a complete record after the deploy. There is no "in progress" record.
2. **Idempotent retry.** A second write with the same Idempotency Key returns the existing record and writes nothing. If its content differs, the engine refuses it and writes an audit entry (I-189).
3. **Correction.** A wrong record is never edited. A person or the record step writes a new record with Corrects Record pointing to the old one. Views show the latest correction. The old one stays.
4. **Flags are facts at write time.** Is Outside Window, Is Other Path and Is Missing Approval are computed from the change request, the target's change path and the gate policy in force at write time, and never recomputed. If an emergency approval follows later, a view shows the flag as resolved by that later decision.
5. **Record steps never fail the deployment.** A governance write warns and lets the deployment finish (I-189). A missed write later shows up as a deployment the product never heard of, through Flag.

### 8.4 Configuration versions

The working default is document 21's model (28, issue 3).

1. **Draft.** An AI client, on an admin's instruction, or a person with a configuration role creates a C46 Configuration Draft. It holds the patch (Change Json), never live rows. The engine validates it and computes the diff.
2. **Submit.** Submitting freezes the draft and proposes the decision: configuration change, or gate policy change when the patch touches any C33 row. The decision's subject is the draft. The admin decides, the AI performs, and the rows show both (D-10).
3. **Accept.** A person accepts the decision on an approval card that shows the engine's diff (working default, open design question 4, see 28-working-defaults.md). No tool, page or AI activates anything.
4. **Activate.** The activation operation, in one transaction, applies the patch to the live rows by key, inserts one C02 row (the full snapshot, its hash, the patch applied, the validation result and the decision), updates C01 Current Version and Current Version Number, and sets the draft to activated with Resulting Version. This is the only operation that writes live configuration rows or C02. C02 rows are append-only.
5. **Discard.** A withdrawn, rejected or expired draft becomes discarded. It stays as evidence of the proposal.
6. **Stamp.** Operational rows stamp the version in force. The stamp plus the snapshot proves which rules applied.

**Snapshot size.** About 1,500 rows of about 500 characters each is about 0.75 million characters. That fits Azure's `json` type (up to 2 GB [T19]) and is close to the Dataverse multiline text limit of 1,048,576 characters [T8]. Section 10.3 gives the Dataverse fallback.

### 8.5 Audit entries per platform

| Platform | What records data changes | What O24 holds |
|---|---|---|
| Dataverse | Dataverse auditing, on for every product table. It records creates, updates and deletes, and an update log only "when a new value is different from the old value" [T3]. It records `userid` and `callinguserid` [S16]. Logs use log capacity [T3] | Product events only: approval outcome written, reminder sent, gap check run, configuration activated, refused writes, idempotent replays (with Request Key and Request Hash), writes outside the engine found by the scheduled check (28, Q16) |
| Azure SQL | Temporal history for mutable tables [S19] and ledger history for frozen tables [T16] | The full change log: one entry per write, with Changed Values Json, plus the product events |

### 8.6 Retention

- **Dataverse auditing:** the admin center offers "Forever" as the default retention [T3]. The organization setting `AuditRetentionPeriodV2` defaults to 30 days when set through the API, and -1 means forever [T4]. The setup service sets it explicitly.
- **Azure temporal history:** retention is infinite unless `HISTORY_RETENTION_PERIOD` is set [T17].
- **Azure ledger tables:** old rows can never be deleted [T15].
- **One setting:** `audit-retention-days` (document 26) controls temporal history and Dataverse audit retention for mutable rows. Its default is forever on Dataverse and the client's policy on Azure. The earlier proposal `history-retention-days` is merged into it (28, issue 15). Ledger tables ignore it, because their rows can never be deleted (28, Q21).

---

## 9. The gap check claim

The gap check (D-15) runs on demand, as the signed-in admin, because the inventory API "currently supports only delegated user authentication" and returns 403 to a service principal [T22].

### 9.1 What is stored, and what is not

| Stored on the claim | Not stored |
|---|---|
| The Microsoft ID (inventory resource `name`), the source type, a link, the label as seen, the environment label as seen, the last-seen date and run, how many runs saw it | Any other inventory property: creation date, connectors, usage, region, environment details |
| A suggested claimant, as a Principal Reference: the person Microsoft listed as owner at check time, as a hint only | Microsoft's owner as a fact. Inventory owners are weak: "Model-driven apps don't have an owner" (fit correction 8) |
| One claim per item and finding kind | A copy of the inventory, a snapshot of a run, or a list of everything seen |

### 9.2 Lifecycle

1. **A run starts.** The engine writes O22 with Run By, Admin Role Used and Estates Covered.
2. **Compare.** For each item Microsoft returns, in a covered estate and of a component kind with Is Gap Check Covered = Yes, the engine looks for a technical component with the same External Id.
3. **Unregistered.** No component found: the engine builds the Claim Key. If a claim with that key exists, it updates Last Seen On, Last Seen Run and Times Seen. A dismissed claim stays dismissed. Otherwise it creates a new open claim.
4. **Vanished (sweep proposal).** A registered, not retired component of a covered kind in a covered estate was not returned: the engine creates or updates a vanished claim with Registered Component set. It never retires the component itself.
5. **Claim.** An owner claims an unregistered item: the engine creates the technical component (External Id, Url, Label As Seen, Last Seen On, Source Claim) on the owner's chosen application, or a draft application, and sets the claim to claimed with Resulting Component.
6. **Resolve a vanished claim.** A person confirms the item is gone (the engine sets the component's Retired On), or that it moved (Replaces Component on a new component).
7. **Dismiss.** A person dismisses with a reason, such as "personal productivity app, not a business application".
8. **Finish.** The engine sets Finished On and the counts. The run row is then frozen.

### 9.3 Notes

- **Owner difference without a copy.** The gap check may compare Microsoft's listed owner with the register's owners and set Owner Differs and Owner Checked On on the component. It does not store Microsoft's owner.
- **Volume control.** A first run in a large tenant can return thousands of personal apps and flows. Covered estates and component kinds are configuration, so a client can start narrow (section 11.5).
- **Last seen elsewhere.** A record step also refreshes Last Seen On on the components and target it touches. A person can confirm a link by hand.
- **Agents stay in Agent 365.** An agent claim links to the agent. Ownerless-agent handling stays in Microsoft's tools [J5]. The product records the business decision (sweep proposal).

---

## 10. Physical binding: Dataverse

### 10.1 Rules for every table

| Item | Binding | Microsoft fact |
|---|---|---|
| Table type | Standard for every entity. No activity, virtual or elastic tables | "Use standard for most tables" [T1] |
| Ownership | Configuration (C01 to C39, C42 and C46, C02 included), O05 and O24: Organization. All other operational tables: User or team, owned by default by a records owner team that has no members (a lockbox pattern, I-88; new term). In portfolio read mode, a group team linked through Portfolio Access (C44, document 26) owns each portfolio's records | Ownership "can't be changed" after a table is created [T1]. User or team is what makes the three visibility settings possible (28, issue 10 and Q13) |
| Configuration writes | A synchronous plug-in on every configuration table refuses any write that does not come from the activation operation (the engine-nested marker, document 25). C46 is written only by the engine's draft operations (28, issue 3) | (design rule) |
| Roles | Security roles only on Entra group teams (D-11). Roles whose members must own or create user-owned rows use member's privilege inheritance | Group team roles can grant "User/Basic-level privileges as if the security role is directly assigned" [T12]. Membership is cached up to 8 hours [S7] |
| Names | `<prefix>_` plus the logical name in PascalCase, as document 20, section 3.4 proposes | Prefix rules [document 20, S11] |
| Configurable lists | Lookups to configuration tables, never choice columns (D-06) | (design rule) |
| Engine contract values | Global choices, one per contract. Status meanings across entities share one global choice, "status meaning", and the engine checks that a meaning fits the entity | (design rule; fixed meanings with configurable labels: working default, open design question 1, see 28-working-defaults.md) |
| Platform state | Is Active binds to the platform state (Active or Inactive) on configuration tables. Operational tables keep the platform state Active and use the product Status lookup | (design rule) |
| JSON | Multiple lines of text, Text format. The engine validates JSON on every write | The JSON format is for non-SQL stores only [T7] |
| Record numbers | Autonumber columns for Record Number on O02, O07, O09, O11 | Autonumber columns can be added programmatically [T11] |
| Alternate keys | On every key in section 7 that has only text and lookup columns. At most 10 per table [S15] | Valid key types include text and lookup; 900 bytes and 16 columns per key [S15] |
| Relationship behavior | "Referential, Restrict Delete" for Restrict. "Parental" for the one Cascade relationship (R80) | Delete options are Cascade All, Remove Link and Restrict [T2] |
| Auditing | On at environment, table and column level for every product table. Off for columns that change on every gap check run (Last Seen On, Times Seen, Last Seen Run, Owner Checked On) to keep log capacity low | Three levels of auditing; logs use log capacity [T3] |
| Enforcement | Synchronous plug-ins and custom APIs (D-10). Custom API signatures use scalar keys and IDs, never Entity parameters or EntityCollection binding, so generative pages can call them (sweep proposal) | Pages don't support those shapes [S13] |
| Performed by | The engine sets the common columns. Platform columns also show impersonation through `createdonbehalfby` [T9] | |

### 10.2 Table by table

Every table: Standard type, auditing on. "Choices" lists the global choices (engine contract). All other references are lookups.

| Entity | Ownership | Choices | Alternate keys | Notes |
|---|---|---|---|---|
| C01 Configuration Set | Organization | Set Kind | Name; Preset Key | A plug-in refuses any write outside the activation operation. Presets change only during an upgrade |
| C02 Configuration Set Version | Organization | | Configuration Set + Version Number; Content Hash | Append-only plug-in. Inserted only by the activation operation. Content Json may need the fallback in 10.3 |
| C46 Configuration Draft | Organization | draft status | Record Number | Freeze plug-in after submit. Written only by the engine's draft operations |
| C04 Setting | Organization | Origin | Key + Configuration Set | Is Locked enforced by plug-in |
| C05 Status | Organization | Origin, status meaning | Key + Configuration Set | Engine Meaning frozen while in use |
| C06 Value List | Organization | Origin | Key + Configuration Set | |
| C07 List Value | Organization | Origin | Value List + Key + Configuration Set | Owned child of C06, referential |
| C08 Tier | Organization | Origin | Key + Configuration Set; Rank + Configuration Set | |
| C09 Tier Question | Organization | Origin | Key + Configuration Set | |
| C10 Tier Answer Option | Organization | Origin | Key + Configuration Set | Owned child of C09, referential |
| C11 Vocabulary Mapping | Organization | Origin | Source Entity + Source Key + Vocabulary + Configuration Set | |
| C12 Lifecycle Stage | Organization | Origin, lifecycle | Key + Configuration Set; Stage Rank + Configuration Set | |
| C13 Gate | Organization | Origin | Key + Configuration Set | |
| C14 Gate Requirement | Organization | Origin, requirement kind | Key + Configuration Set | Owned child of C13, referential |
| C15 Application Kind | Organization | Origin | Key + Configuration Set | |
| C16 Component Kind | Organization | Origin | Key + Configuration Set | |
| C17 Dependency Type | Organization | Origin | Key + Configuration Set | |
| C18 Decision Type | Organization | Origin | Key + Configuration Set | |
| C19 Decision Outcome | Organization | Origin | Key + Configuration Set | Owned child of C18, referential |
| C20 Change Type | Organization | Origin | Key + Configuration Set | |
| C21 Standard Change Model | Organization | Origin | Key + Configuration Set | |
| C22 Document Type | Organization | Origin | Key + Configuration Set | |
| C23 Documentation Requirement | Organization | Origin | Key + Configuration Set | |
| C24 Review Type | Organization | Origin | Key + Configuration Set | |
| C25 Review Cadence | Organization | Origin | Key + Configuration Set; Review Type + Tier + Configuration Set | |
| C26 Reminder Rule | Organization | Origin, reminder subject | Key + Configuration Set | |
| C27 Party | Organization | Origin | Key + Configuration Set | |
| C28 Owner Role | Organization | Origin | Key + Configuration Set | |
| C29 Role | Organization | Origin | Key + Configuration Set | |
| C30 Role Group Binding | Organization | Origin, binding method | Role + Entra Group + Binding Method + Configuration Set | A check compares bindings with the group teams that exist. Groups only |
| C42 Workload Grant | Organization | Origin | Key + Configuration Set; Principal + Workload Role + Configuration Set | The drift check compares grants with the application users and their directly assigned roles (document 26, CHK-09) |
| C31 Decision Right | Organization | Origin, decision right letter | Key + Configuration Set | |
| C32 Approval Rule | Organization | Origin, approval mode | Key + Configuration Set | |
| C33 Gate Policy | Organization | Origin, gate level, fail mode, AI-only code approval handling | Tier + Change Type + Target Stage + Portfolio + Configuration Set | |
| C34 Target Stage | Organization | Origin | Key + Configuration Set | |
| C35 Change Path | Organization | Origin | Key + Configuration Set | |
| C36 Deployment Route | Organization | Origin, deployment route meaning | Key + Configuration Set | |
| C37 Link Kind | Organization | Origin, link category | Key + Configuration Set | |
| C38 Actor Kind | Organization | Origin, actor kind meaning | Key + Configuration Set | |
| C39 Guideline | Organization | Origin | Key + Configuration Set; Number + Version + Configuration Set | |
| O01 Portfolio | User or team | | | |
| O02 Business Application | User or team | status meaning | Record Number | Tier and stage writable only by the decision plug-in |
| O03 Technical Component | User or team | | Link Kind + External Id | Last Seen columns not audited |
| O04 Dependency | User or team | | | |
| O05 Principal Reference | Organization | principal kind, directory state | Entra Object Id | Optional lookup to the built-in user row |
| O06 Owner Assignment | User or team | | | |
| O07 Intake Request | User or team | status meaning | Record Number | Visibility per the `intake-visibility` setting, default own (28, Q13) |
| O08 Tier Assessment | User or team | | | Freeze plug-in |
| O09 Decision | User or team | status meaning | Record Number | Freeze plug-in. Superseded By stored by the engine |
| O10 Approval Record | User or team | actor kind meaning, principal kind, status meaning | | Outcome columns writable only by the approval writer's application user (workload grant, C42), checked by plug-in and protected by column security (28, issue 9 and Q15). System administrators keep access to secured columns (column-level security: https://learn.microsoft.com/power-platform/admin/field-level-security) |
| O11 Change Request | User or team | status meaning, change management mode | Record Number | |
| O12 Affected Application | User or team | | Change Request + Business Application | Parental to O11 |
| O13 Artifact Version | User or team | | Artifact Name + Version | Fill-once plug-in |
| O14 Deployment Record | User or team | status meaning, gate level, actor kind meaning | Idempotency Key | Upsert by alternate key gives the idempotent retry [S15]. A plug-in refuses any update that changes content |
| O15 Deployment Target | User or team | | Link Kind + External Id | |
| O16 Pipeline Reference | User or team | | Link Kind + External Id | |
| O17 Pipeline Map Entry | User or team | | Business Application + Pipeline Reference + Deployment Target | |
| O18 Delivery Link | User or team | | | Uniqueness checked by plug-in (it spans optional lookups) |
| O19 Document Link | User or team | status meaning | | Rows only once a link exists. Missing is computed (28, issue 16) |
| O20 Evidence Link | User or team | | | Append-only plug-in |
| O21 Review | User or team | status meaning, review trigger | | |
| O22 Gap Check Run | User or team | | | Visibility per the `claim-visibility` setting, default admins and claimant (28, Q13) |
| O23 Claim | User or team | status meaning, finding kind | Claim Key | Visibility per the `claim-visibility` setting (28, Q13). Last Seen columns not audited |
| O24 Audit Entry | Organization | actor kind meaning | Operation + Request Key | Append-only plug-in. Readable by the auditor and platform admin roles only (document 26, section 5.3) |

### 10.3 Dataverse notes and residual risks

- **Snapshot fallback.** If a configuration snapshot passes the multiline text limit [T8], the engine stores it compressed in a file column on C02 and keeps the hash in the row. File column limits are UNVERIFIED here.
- **Client App is "as reported".** The MCP server can pass the client app through the `tag` parameter, which reaches plug-ins as a shared variable [T10]. Any client can set it, so Client App Verified is No. Whether a plug-in can read the calling application's ID directly is UNVERIFIED.
- **Admins can change platform behavior.** Audit logs can be deleted, and the deletion is itself audited [T3]. Whether an administrator can deactivate the product's plug-in steps inside a managed solution is UNVERIFIED. The decision log's integrity therefore rests on plug-ins plus audit, not on a tamper-evident store.
- **Workload identities.** Pipeline record steps and the approval writer call as application users, which normally get security roles directly. Each such direct role is recorded as a C42 Workload Grant (28, issue 8; working default, open design question 12, see 28-working-defaults.md). Whether an application user can receive roles through an Entra group team is UNVERIFIED, so the design does not rely on it.

---

## 11. Physical binding: Azure SQL Database

### 11.1 Rules for every table

| Item | Binding | Microsoft fact |
|---|---|---|
| Schemas | `config` for C entities, `ops` for O entities, `audit` for O24, `security` for predicate functions | (document 20, section 3.5) |
| Primary key | `Id uniqueidentifier`, default `NEWSEQUENTIALID()` | |
| Foreign keys | One per reference. Restrict is `ON DELETE NO ACTION`. The one Cascade relationship (R80) is `ON DELETE CASCADE`. Self-references are always `NO ACTION` | |
| Engine contract values | `varchar(32)` with a check constraint listing the contract's meanings | |
| Keys | `varchar(64)` with `CHECK (Key NOT LIKE '%[^a-z0-9-]%' COLLATE Latin1_General_BIN2)` and a leading-letter check | |
| JSON | Native `json` columns. A `json` column can't be an index key, but JSON indexes exist [T19] | `json` is GA in Azure SQL Database [S20][T19] |
| Mutable history | System-versioned temporal tables, with a history table per table [S19]. Row-level security predicates must be added to the history table separately [T14] | |
| Frozen and append-only history | Updatable ledger tables for C46, O08, O09, O10: system-versioned, tamper-evident, history kept [T16]. Append-only ledger tables for C02, O14, O20, O24 [S18]. This is the working default for the frozen and append-only entities (28, Q21) | Ledger tables "can't be reverted" [S18] |
| Ledger schema changes | Only nullable columns can be added. Data type changes aren't supported. Dropped columns are kept, hidden [T15]. So ledger tables are designed with care, and new optional facts go into nullable columns | [T15] |
| Ledger digests | Automatic digest storage in Azure Blob Storage with immutability. Locally redundant storage isn't offered for it [T15] | [T15] |
| Database principals | `engine_app`: the managed identity of the one service that hosts the API, the product's MCP server and the engine (Azure Functions Flex Consumption, .NET 10; Container Apps as the fallback; 28, issue 13). The engine connects with it and never with the person's own token (28, Q20). `approval_writer`: a separate managed identity used only by the API's approval callback, which accepts app-only tokens from the approval writer's workload grant. `migrator`: used only for schema upgrades. No human logins, except a break-glass Entra admin | (design) |
| Permissions | `engine_app`: SELECT, INSERT and UPDATE on `config` and `ops`, INSERT only on append-only tables, no DELETE except through a draft-delete procedure for operational drafts. Updates to `config` entity tables and inserts to `config.ConfigurationSetVersion` pass only inside the activation operation (`fn_ConfigWriter`). `approval_writer`: UPDATE on the outcome columns of `ops.ApprovalRecord` only, plus INSERT on `audit.AuditEntry` | The RLS example also denies updates to sensitive columns [T13] |
| Session context | The API sets these keys on every connection with `@read_only = 1`: `PrincipalId`, `UserObjectId`, `ActorKind`, `OnBehalfOfId`, `ClientApp`, `ClientAppVerified`, `Roles` (product role keys from the token's `roles` claim), `PortfolioIds` (new: the top-level portfolios the caller's groups reach through Portfolio Access, used in portfolio read mode), `EngineOperation` (new: set to the operation's name only on the dedicated connection the activation and draft operations open), `CorrelationId`. This is the working default: managed identity plus the person's ID in session context (28, Q20) | `SESSION_CONTEXT` for middle-tier apps; `@read_only` stops changes until the connection closes [T13] |
| Roles | Entra app roles assigned to groups; the API reads the `roles` claim. Group claims in tokens are capped at 200 for JWT, which app roles avoid [T23]. Needs Entra ID P1 or P2; nested groups are not supported [S9]. Workload roles are app roles assigned directly to a workload's service principal, each recorded as a C42 Workload Grant (working default, open design question 12, see 28-working-defaults.md). The API accepts app-only tokens only from principals in a grant, and only for that grant's role (document 26, RA-07) | |

### 11.2 Table by table

"Temporal" means system-versioned temporal. "Updatable ledger" and "append-only ledger" are ledger table types. FKs follow section 6.

| Entity | Table | History | Unique constraints | JSON columns | RLS |
|---|---|---|---|---|---|
| C01 | `config.ConfigurationSet` | Temporal | Name; Preset Key (filtered, not null) | | Read: any product role. Write: only inside the activation operation (`fn_ConfigWriter`) |
| C02 | `config.ConfigurationSetVersion` | Append-only ledger | ConfigurationSetId + VersionNumber; ContentHash | ContentJson; ChangeJson; ValidationJson; AppliedPresetsJson | As C01 |
| C46 | `config.ConfigurationDraft` | Updatable ledger | RecordNumber | ChangeJson; ValidationJson; AppliedPresetsJson | Read: any product role. Write: only the engine's draft operations, for a caller with a configuration role (`fn_DraftWriter`), and only while the draft is editable (`fn_DraftOpen`) |
| C04 to C39 and C42 (each) | `config.<EntityName>`, such as `config.Tier`, `config.GatePolicy`, `config.WorkloadGrant` | Temporal | ConfigurationSetId + Key, plus the extra keys in section 7 | C04 ValueJson; C09 AppliesToKinds; C10 TriggersJson; C15 DefaultComponentKinds; C21 ConditionsJson; C33 EmergencyRuleJson; C35 AllowedRoutes | As C01 |
| O01 | `ops.Portfolio` | Temporal | Name + ParentPortfolioId | | Read: any product role |
| O02 | `ops.BusinessApplication` | Temporal | RecordNumber | | Read: any product role |
| O03 | `ops.TechnicalComponent` | Temporal | LinkKindId + ExternalId | | Read: any product role |
| O04 | `ops.Dependency` | Temporal | (engine check on active rows) | | Read: any product role |
| O05 | `ops.PrincipalReference` | Temporal, with a short history retention so erased labels age out | EntraObjectId | | Read: any product role |
| O06 | `ops.OwnerAssignment` | Temporal | | | Read: any product role |
| O07 | `ops.IntakeRequest` | Temporal | RecordNumber | | Filter by the `intake-visibility` setting, default own (28, Q13) |
| O08 | `ops.TierAssessment` | Updatable ledger | | AnswersJson | Read: any product role. Block updates once TierDecisionId is set |
| O09 | `ops.Decision`, plus view `ops.DecisionCurrent` (adds SupersededById) | Updatable ledger | RecordNumber | | Read: any product role. Block updates unless StatusMeaning is proposed |
| O10 | `ops.ApprovalRecord` | Updatable ledger | | | Read: any product role. Block updates unless OutcomeMeaning is pending. Outcome writer rules in 11.3 |
| O11 | `ops.ChangeRequest` | Temporal | RecordNumber | | Read: any product role |
| O12 | `ops.AffectedApplication` | Temporal | ChangeRequestId + BusinessApplicationId | | Read: any product role |
| O13 | `ops.ArtifactVersion` | Temporal | ArtifactName + Version | | Read: any product role. Fill-once by engine |
| O14 | `ops.DeploymentRecord` | Append-only ledger | IdempotencyKey | ReportedApprovalsJson; StageOrderRun | Read: any product role |
| O15 | `ops.DeploymentTarget` | Temporal | LinkKindId + ExternalId | | Read: any product role |
| O16 | `ops.PipelineReference` | Temporal | LinkKindId + ExternalId | | Read: any product role |
| O17 | `ops.PipelineMapEntry` | Temporal | BusinessApplicationId + PipelineReferenceId + DeploymentTargetId | | Read: any product role |
| O18 | `ops.DeliveryLink` | Temporal | (engine check) | | Read: any product role |
| O19 | `ops.DocumentLink` | Temporal | BusinessApplicationId + DocumentationRequirementId (filtered, not null) | | Read: any product role |
| O20 | `ops.EvidenceLink` | Append-only ledger | | | Read: any product role |
| O21 | `ops.Review` | Temporal | BusinessApplicationId + ReviewTypeId + DueOn | | Read: any product role |
| O22 | `ops.GapCheckRun` | Temporal | | EstatesCovered | Filter by the `claim-visibility` setting, default admins and claimant (28, Q13) |
| O23 | `ops.Claim` | Temporal | ClaimKey | | Filter by the `claim-visibility` setting (28, Q13) |
| O24 | `audit.AuditEntry` | Append-only ledger | Operation + RequestKey (filtered, not null) | ChangedValuesJson | Read: auditor and platform admin roles only (document 26, section 5.3) |

The polymorphic subjects (O09, O18, O20) each get one nullable foreign key per subject kind and `CHECK` constraints that exactly one is set and that it matches SubjectKind.

"Read: any product role" means open read, the default of `record-access-mode`. When a client switches it to portfolio read, `fn_CanReadPortfolio` also filters every `ops` table whose rows belong to one application (28, issue 10 and Q13).

### 11.3 Row-level security predicates

Filter predicates hide rows from reads. Block predicates refuse writes [T13]. Each predicate is an inline table-valued function in the `security` schema, bound by a security policy. Predicates on a current table do not reach its history table, so each history table gets its own filter predicate [T14].

| Predicate | Kind | Tables | Rule |
|---|---|---|---|
| `fn_HasProductRole` | Filter | Every `config` and `ops` table and their history tables | Rows are visible only when `SESSION_CONTEXT('Roles')` holds at least one product role key |
| `fn_CanReadAudit` | Filter | `audit.AuditEntry` | The session roles include auditor, platform admin or configuration admin |
| `fn_CanReadIntake` | Filter | `ops.IntakeRequest` | Reads the `intake-visibility` setting: "all" shows every row to any product role; "own" shows the requester's rows plus rows to the PMO, platform admin and architect roles |
| `fn_CanReadClaim` | Filter | `ops.Claim`, `ops.GapCheckRun` | Reads the `claim-visibility` setting: "admins and claimant" (the default) shows rows to platform admin and AI lead roles, plus claims whose Suggested Claimant or Claimed By is the session principal; "all" shows every row |
| `fn_CanReadPortfolio` (new) | Filter | Every `ops` table whose rows belong to one application, and their history tables | Reads the `record-access-mode` setting: "open" (the default) shows every row; "portfolio read" shows rows whose application's top-level portfolio is in `SESSION_CONTEXT('PortfolioIds')`, plus every row to the auditor, platform admin and architect roles (document 26, section 5.2) |
| `fn_ConfigWriter` | Block, after insert and after update | Every `config` table except `config.ConfigurationDraft` | The database principal is `engine_app` and `SESSION_CONTEXT('EngineOperation')` is the activation operation. No session role is enough on its own, so no person, role or tool writes configuration tables directly (28, issue 3) |
| `fn_DraftWriter` (new) | Block, after insert and after update | `config.ConfigurationDraft` | The database principal is `engine_app`, and either `EngineOperation` is a draft operation (create, edit, submit, withdraw) and the session roles include a role with Can Change Configuration, or `EngineOperation` is the activation operation or the expiry discard |
| `fn_DraftOpen` (new) | Block, before update | `config.ConfigurationDraft` | The current row's DraftStatus is draft, or `EngineOperation` is the activation operation, a withdrawal or rejection discard, or the expiry discard, which write the outcome columns once |
| `fn_DecisionOpen` | Block, before update | `ops.Decision` | The current row's StatusMeaning is proposed |
| `fn_ApprovalPending` | Block, before update | `ops.ApprovalRecord` | The current row's OutcomeMeaning is pending |
| `fn_ApprovalOutcomeWriter` | Block, after update | `ops.ApprovalRecord` | When the database principal is `approval_writer` (the approval writer, section 8.2), the new OutcomeMeaning is approved, rejected or recommendation only. When it is `engine_app`, the new OutcomeMeaning is pending, expired or cancelled. Predicates can test `DATABASE_PRINCIPAL_ID()` [T13] |
| `fn_AssessmentOpen` | Block, before update | `ops.TierAssessment` | The current row's TierDecisionId is null |

### 11.4 Azure notes and residual risks

- **The API is the only write path.** Session context is set by the API, so a compromised API could set any role. Separate database principals for approval outcomes, ledger tables and block predicates limit what it can do. Ledger digests make any tampering with frozen rows evident.
- **Erasure versus ledger.** Ledger rows can never be deleted [T15]. The working default accepts this with the free-text rule, label-clearing erasure and a documented legal basis (section 2.7; 28, Q21).
- **Workload identities.** If a service principal is added to a group and an app role is assigned to that group, Entra ID "doesn't add the roles claim" to its tokens [T20]. So pipeline identities and the approval writer can't receive a workload role through a group. Each gets the app role directly, recorded as a C42 Workload Grant (28, issue 8; working default, open design question 12, see 28-working-defaults.md).

### 11.5 Volume controls on both platforms

- The gap check's covered estates and component kinds are configuration (C16 Is Gap Check Covered, the `estate` list). A client can start with production environments only.
- Last Seen updates are not audited on Dataverse and are excluded from Changed Values Json on Azure.

---

## 12. Capacity

### 12.1 Assumptions for a 5,000-person organization

Planning figures, not measurements. About 250 makers, 400 application owners, 200 approvers and 40 people who work in the product every week. About 600 business applications registered in year 1 and 150 more each year. About 150 change requests and 500 deployments a month across all applications.

### 12.2 Rows

| Entity | Year 1 | By year 5 | Driver |
|---|---|---|---|
| O01 Portfolio | 40 | 60 | Business structure |
| O02 Business Application | 600 | 1,200 | Registration |
| O03 Technical Component | 1,800 | 4,000 | About 3 per application |
| O04 Dependency | 1,200 | 2,800 | About 2 per application |
| O05 Principal Reference | 1,600 | 3,500 | Owners, approvers, requesters, groups, workload identities |
| O06 Owner Assignment | 2,400 | 6,000 | About 4 per application, plus ended rows |
| O07 Intake Request | 400 | 2,000 | Front door |
| O08 Tier Assessment | 700 | 2,500 | Intake and tier reviews |
| O09 Decision | 4,000 | 20,000 | Triage, tier, gates, changes, ownership, reviews, configuration |
| O10 Approval Record | 5,000 | 25,000 | About 1.25 per decision |
| O11 Change Request | 1,800 | 9,000 | About 150 a month |
| O12 Affected Application | 1,000 | 5,000 | |
| O13 Artifact Version | 2,500 | 12,500 | |
| O14 Deployment Record | 6,000 | 30,000 | About 500 a month |
| O15 Deployment Target | 150 | 300 | Environments and other targets |
| O16 Pipeline Reference | 200 | 400 | |
| O17 Pipeline Map Entry | 600 | 1,200 | |
| O18 Delivery Link | 4,000 | 16,000 | Work items and repositories |
| O19 Document Link | 1,800 | 4,000 | About 3 linked documents per application. Missing documents have no row |
| O20 Evidence Link | 3,000 | 15,000 | |
| O21 Review | 900 | 5,000 | About 1.5 per application a year |
| O22 Gap Check Run | 50 | 250 | About weekly |
| O23 Claim | 3,000 | 6,000 | A large first run, then about 100 a month |
| **Operational total** | **about 43,000** | **about 172,000** | |
| Configuration rows (C04 to C40 and C42, the working set only) | about 1,500 | about 1,800 | About 1,500 rows in the one working set, plus one workload grant per pipeline identity. Presets are stored as one snapshot each (a C01 and a C02 row), never as rows (document 21, section 4.1) |
| C02 Configuration Set Version | 100 | 500 | About 2 activations a week; about 0.5 to 1 MB each |
| C46 Configuration Draft | 150 | 700 | Every proposed change, including discarded ones. A patch and a validation result, a few KB each |
| O24 Audit Entry, Azure (full change log) | about 120,000 | about 600,000 | About 3 writes per row created |
| O24 Audit Entry, Dataverse (product events only) | about 15,000 | about 75,000 | |
| Dataverse audit log records (platform) | about 150,000 | about 750,000 | Auditing on all product tables |

### 12.3 Storage

The per-row sizes are planning estimates (UNVERIFIED). Microsoft publishes no per-row figure for Dataverse.

| Platform | Year 1 | By year 5 | What it uses |
|---|---|---|---|
| Dataverse database capacity | about 0.3 to 0.5 GB | about 1.3 to 1.8 GB | Rows at about 3 to 5 KB each with system columns and indexes, plus snapshots |
| Dataverse log capacity | about 0.15 to 0.3 GB | about 0.75 to 1.5 GB | Audit logs live in log storage (AuditBase), together with plug-in trace logs [T5] |
| Azure SQL data | about 0.4 to 0.6 GB | about 1.5 to 2.5 GB | Rows at about 1 KB with indexes, audit entries at about 1.5 KB, snapshots, temporal and ledger history |

**What it costs.**
- **Dataverse:** capacity is pooled across the tenant, and "there's no technical limit on the size of a Dataverse environment"; the limits are entitlements [T5]. Extra capacity is bought in 1 GB increments [T6]. At these sizes the product fits most tenants' pooled entitlement. The capacity add-on price was not checked here (UNVERIFIED). The real cost driver on Dataverse is user licenses: from February 2027, users without a premium license can't open model-driven apps (https://learn.microsoft.com/power-platform/admin/powerapps-licensing-faq).
- **Azure SQL:** data storage is billed on the configured maximum size, chosen in 1 GB steps; 30 percent is added for the log; backup storage up to the maximum data size is free; point-in-time restore keeps 1 to 35 days [T18]. A maximum of 5 to 10 GB covers year 5 with room. Storage is a small part of the Azure cost. For current prices of the whole footprint, use Microsoft's Azure pricing calculator (https://azure.microsoft.com/pricing/calculator/) and the Azure SQL Database pricing page (https://azure.microsoft.com/pricing/details/azure-sql-database/single/).
- **Ledger digests** add a small Azure Blob Storage account with immutability [T15].

---

## 13. New terms

Terms and columns this document adds. Document 20 is the source for every other name. Each needs the product owner's agreement before document 20 absorbs it.

| Term | Definition | Where |
|---|---|---|
| Created By Principal | The principal that created a row, as a Principal Reference. Never changes | Common columns |
| Performed By Principal | The principal behind a row's last write, as a Principal Reference | Common columns |
| Client App Verified | Yes when Client App came from a validated token claim; No when the caller only stated it | Common columns, O24 |
| Engine Version, Engine Checked On | The engine stamp: the engine library version and the time of the last engine evaluation. A later Modified On means a write outside the engine (from document 25; 28, issue 15 and Q16) | Common columns |
| C46 Configuration Draft | A proposed change to the working set, held as a patch until a person accepts it. Proposed ID, to be reserved in document 20, section 2.10 (28, issue 3) | Section 3.1 |
| Draft status (contract) | Fixed list: draft, submitted, activated, discarded | C46 |
| Version status (contract) | Fixed list from document 21: draft, active, superseded, discarded. On C02 only active and superseded apply, derived, never stored | C02 |
| Current Version Number, Base Version Number, Source Draft, Change Json, Validation Json, Proposal Rationale, Applied Presets Json, Snapshot Schema Version, Resulting Version, Submitted On, Expires On, Discarded On | Version and draft columns (from documents 21 and 25; 28, issue 15) | C01, C02, C46 |
| Subject Configuration Draft | The O09 subject for a configuration change or gate policy change decision. Replaces the subjects configuration set version and gate policy row | O09 |
| C42 Workload Grant columns | Principal, Workload Role, Reason, Uses Federated Credential, Binding Detail, Granted By Decision, Expires On, Last Verified On (from document 26; 28, issue 8) | Section 3.3 |
| Approval runner, approval writer | The approval runner is the set of approval flows that send the cards. The approval writer is the workload role, and the runner's workload identity holding it, that writes approval outcomes (28, issue 7; recorded in document 20) | Sections 2.4, 8.2 |
| Stage Rank | An ordering number on C12 that the engine compares (from document 25) | C12 |
| Current Needs Person | C22 column: Yes when an AI client marking a document current needs a confirmation card (from document 23) | C22 |
| Requires Approval, Requires Standard Model | C18 and C20 columns from document 21 that the locked list and issue 1 depend on (added in a later review of this document) | C18, C20 |
| Is Consultation | C32 column: the card asks for input, and the answer is recommendation only (from document 23; 28, issue 12) | C32 |
| Directory State, Directory State On, Leaves On | O05 columns for the owner check (from document 23) | O05 |
| Directory state (contract) | Fixed list: enabled, disabled, deleted, unknown | O05 |
| Trigger, review trigger (contract) | O21 column and its fixed list: cadence, tier answer trigger, owner check, request | O21 |
| Request Key, Request Hash | O24 columns for idempotent write calls (from document 25) | O24 |
| Meaning copy | A column holding the engine meaning of a row's configurable status, written by the engine at every write (Status Meaning, Outcome Meaning, Result Meaning, Approver Actor Meaning) | O02, O07, O09, O10, O11, O14, O19, O21, O23 |
| History class | One of five rules for how an entity keeps history: append-only, freeze on final, effective-dated, mutable with history, configuration versioned | Section 2.3 |
| Fill-once column | A column that may be filled once while empty and never changed | O13 |
| Record Number | A human-friendly, unique, never-reused number such as APP-00042 | O02, O07, O09, O11 |
| Subject kind list | The fixed list of entity names a polymorphic subject can take. Part of the engine contract, because adding one adds a column | O09, O18, O20 |
| Superseded By | The accepted decision that supersedes this one. Derived | O09 |
| Corrects Record | The earlier deployment record a new record corrects | O14 |
| Reported Approvals Json | Approvals a pipeline or code tool reported, with approver kind. Evidence only, never counted | O14 |
| Merge Actor Kind | The actor kind of the merge actor, as reported | O14 |
| Asked Principal | The user or group an approval card went to | O10 |
| Claim Key | The engine-built unique key of a claim: finding kind, source type and Microsoft ID | O23 |
| Last Seen Run, Times Seen, Source Type As Seen, Registered Component | Claim columns for re-runs, the tool's own type value, and the component a vanished claim is about | O23 |
| Vanished Claims | The number of vanished claims a run raised | O22 |
| Retired On, Ended On, Is Active | End dates and a retire flag that replace deletes | O03, O04, O01 |
| Is Erased | Marks a Principal Reference whose personal data was erased | O05 |
| Reminder subject (contract) | Fixed list: review, document, approval, claim | C26 |
| Binding method (contract) | Fixed list: Dataverse group team, Entra app role assignment | C30 |
| AI-only code approval handling (contract) | Fixed list: ignore, flag, stop at enforce (sweep proposal) | C33 |
| Settings `intake-visibility`, `claim-visibility` | Two seed settings this model proposed. With `record-access-mode` (document 26) they form the three visibility settings (28, issue 10). `history-retention-days` is withdrawn and merged into `audit-retention-days` (28, issue 15). Every setting sits in document 21's one settings table | C04 |
| Records owner team | Dataverse binding: an owner team with no members that owns user-or-team rows by default (lockbox pattern) | Section 10 |
| Database principals `engine_app`, `approval_writer`, `migrator` | Azure binding: the only identities that connect to the database. `approval_writer` is the approval writer's database principal | Section 11 |
| Session context keys | Azure binding: the values the API sets on each connection for row-level security, including `PortfolioIds` and `EngineOperation` | Section 11 |
| Predicates `fn_CanReadPortfolio`, `fn_DraftWriter`, `fn_DraftOpen` | Azure binding: portfolio read, and the draft write rules | Section 11.3 |

---

## 14. Microsoft sources

"Checked 2026-11-22" means read on Microsoft Learn through the Microsoft Learn MCP tools on 2026-11-22. Rows marked "(document 20)" were checked on that date for document 20 and reused here without a fresh read.

| ID | Fact used | URL | Checked |
|---|---|---|---|
| S1 | ADRs are "an append-only log. Don't go back and edit accepted records"; a changed decision supersedes; status words Proposed, Accepted, Superseded; options considered and confidence | https://learn.microsoft.com/azure/well-architected/architect-role/architecture-decision-record | 2026-11-22 (document 20) |
| S3 | The front door "recommends a route" and "doesn't approve the work" | https://learn.microsoft.com/agents/center-of-excellence/agent-front-door | 2026-11-22 (document 20) |
| S6 | Inventory contents and the six Entra roles that can see it; AI administrator and AI reader see AI resources only | https://learn.microsoft.com/power-platform/admin/power-platform-inventory | 2026-11-22 (document 20) |
| S7 | Group team memberships are cached at sign-in, refreshed after 8 hours | https://learn.microsoft.com/power-platform/admin/manage-group-teams | 2026-11-22 (document 20) |
| S9 | Group-based app assignment needs Entra ID P1 or P2; nested groups not supported | https://learn.microsoft.com/entra/identity/enterprise-apps/assign-user-or-group-access-portal | 2026-11-22 (document 20) |
| S10 | Power Automate approval types: everyone must approve, first to respond, sequential | https://learn.microsoft.com/power-automate/get-started-approvals | 2026-11-22 (document 20) |
| S11 | Publisher prefix and schema name rules | https://learn.microsoft.com/power-apps/developer/data-platform/customize-entity-metadata | 2026-11-22 (document 20) |
| S13 | Generative pages don't support custom APIs with EntityCollection binding or Entity parameters | https://learn.microsoft.com/power-apps/maker/model-driven-apps/generative-page-external-tools | 2026-11-22 (document 20) |
| S14 | Multi-table lookups are created with code; UI support "in a future release" | https://learn.microsoft.com/power-apps/developer/data-platform/webapi/multitable-lookup | 2026-11-22 (document 20) |
| S15 | Alternate keys: text and lookup columns valid; 10 keys per table; 900 bytes and 16 columns; nulls don't enforce uniqueness; characters that break requests | https://learn.microsoft.com/power-apps/developer/data-platform/define-alternate-keys-entity | 2026-11-22 (document 20) |
| S16 | Audit holds `userid` and `callinguserid`; actions "Create - AI assisted" and "Update - AI assisted" | https://learn.microsoft.com/power-apps/developer/data-platform/reference/entities/audit | 2026-11-22 (document 20) |
| S18 | Append-only ledger tables allow only INSERT; can't be reverted | https://learn.microsoft.com/sql/relational-databases/security/ledger/ledger-append-only-ledger-tables | 2026-11-22 (document 20) |
| S19 | Temporal tables keep a history table and support point-in-time queries | https://learn.microsoft.com/sql/relational-databases/tables/temporal/overview | 2026-11-22 (document 20) |
| S20 | The `json` type is GA in Azure SQL Database | https://learn.microsoft.com/sql/t-sql/data-types/json-data-type | 2026-11-22 |
| S21 | For append-only stores, keep personal data outside and reference it by identifier | https://learn.microsoft.com/azure/architecture/patterns/event-sourcing | 2026-11-22 (document 20) |
| J1 | GitHub Copilot approvals can count toward required approvals (off by default) | https://github.blog/changelog/2026-10-28-copilot-code-review-merge-with-confidence-with-max-and-approvals | 2026-11-22 (news review) |
| J2 | CLI deployments: "Keep a release record with the source version, CLI version, target agent and environment, and test results" | https://learn.microsoft.com/microsoft-365/copilot-managed-runtime/developer/agents/cli/deploy | 2026-11-22 (news review) |
| J5 | The Agent 365 registry shows agents without owners and offers "Assign new owner" | https://learn.microsoft.com/microsoft-agent-365/admin/agent-registry-overview | 2026-11-22 (news review) |
| T1 | Custom table ownership is User or team, Organization or Filtered; "Once a table is created, the ownership type can't be changed"; "Use standard for most tables" | https://learn.microsoft.com/power-apps/maker/data-platform/types-of-entities ; https://learn.microsoft.com/power-apps/maker/data-platform/create-edit-entities-portal | 2026-11-22 |
| T2 | Delete behaviors Cascade All, Remove Link, Restrict ("Prevent the primary table row from being deleted when related table rows exist"); limits on multiple parental relationships | https://learn.microsoft.com/power-apps/maker/data-platform/create-edit-entity-relationships | 2026-11-22 |
| T3 | Auditing at environment, table and column level; audit logs "consume log storage capacity"; update logs only when the value differs; deletion of audit logs is audited; admin center retention default "Forever" | https://learn.microsoft.com/power-platform/admin/manage-dataverse-auditing | 2026-11-22 |
| T4 | `AuditRetentionPeriodV2`: "The default value is 30"; -1 retains forever | https://learn.microsoft.com/power-apps/developer/data-platform/auditing/configure | 2026-11-22 |
| T5 | Capacity types database, file and log; log holds AuditBase, PlugInTraceLogBase and elastic tables; "There's no technical limit on the size of a Dataverse environment" | https://learn.microsoft.com/power-platform/admin/capacity-storage | 2026-11-22 |
| T6 | "You can purchase more storage in 1-GB increments" | https://learn.microsoft.com/power-platform/admin/whats-new-storage | 2026-11-22 |
| T7 | The text JSON format is "Only in non-SQL stores like Audit" | https://learn.microsoft.com/power-apps/developer/data-platform/data-type-format-conversions | 2026-11-22 |
| T8 | Single line of text up to 4,000 characters; multiple lines up to 1,048,576 characters | https://learn.microsoft.com/power-apps/maker/data-platform/create-edit-field-solution-explorer | 2026-11-22 |
| T9 | Impersonation with `CallerObjectId`; `createdonbehalfby` identifies the actual user | https://learn.microsoft.com/power-apps/developer/data-platform/impersonate-another-user | 2026-11-22 |
| T10 | The `tag` parameter reaches plug-ins as a shared variable; "intended for client applications to set any value they wish" | https://learn.microsoft.com/power-apps/developer/data-platform/optional-parameters | 2026-11-22 |
| T11 | "You can add an auto-number column for any table. Currently, you can add the column programmatically" | https://learn.microsoft.com/power-apps/developer/data-platform/entity-attribute-metadata | 2026-11-22 |
| T12 | Group team roles with member's privilege inheritance grant "User/Basic-level privileges as if the security role is directly assigned" | https://learn.microsoft.com/power-platform/admin/manage-group-teams | 2026-11-22 |
| T13 | Row-level security: filter predicates and block predicates (after insert, after update, before update, before delete); `SESSION_CONTEXT` for middle-tier apps with `@read_only`; predicates can test `DATABASE_PRINCIPAL_ID()`; column-level DENY | https://learn.microsoft.com/sql/relational-databases/security/row-level-security | 2026-11-22 |
| T14 | "Security predicates on the current table aren't automatically replicated to the history table" | https://learn.microsoft.com/sql/relational-databases/security/row-level-security | 2026-11-22 |
| T15 | Ledger limits: deleting older data in append-only tables or updatable ledger history "isn't supported"; no TRUNCATE; only nullable columns can be added; type changes not supported; dropped columns are kept; unsupported types XML, sql_variant, user-defined types, FILESTREAM, vector; RLS predicates not replicated to history; automatic digest storage without LRS | https://learn.microsoft.com/sql/relational-databases/security/ledger/ledger-limits | 2026-11-22 |
| T16 | Updatable ledger tables are system-versioned with a history table and tamper evidence; a table can be both ledger and temporal | https://learn.microsoft.com/sql/relational-databases/security/ledger/ledger-updatable-ledger-tables | 2026-11-22 |
| T17 | Temporal history retention with `HISTORY_RETENTION_PERIOD`; infinite when omitted | https://learn.microsoft.com/sql/relational-databases/tables/temporal/manage-retention | 2026-11-22 |
| T18 | vCore storage: maximum data size from 1 GB in 1 GB steps, default 32 GB, billed on the maximum; 30 percent added for the log; backup storage up to the maximum data size at no extra charge; restore window 1 to 35 days | https://learn.microsoft.com/azure/azure-sql/database/service-tiers-sql-database-vcore | 2026-11-22 |
| T19 | `json` accepts only an object or array at the top level; up to 2 GB; can't be an index key column; JSON indexes exist | https://learn.microsoft.com/sql/t-sql/data-types/json-data-type | 2026-11-22 |
| T20 | "If you add a service principal to a group, and then assign an app role to that group, Microsoft Entra ID doesn't add the roles claim" | https://learn.microsoft.com/entra/identity-platform/howto-add-app-roles-in-apps | 2026-11-22 |
| T21 | The `oid` claim is an immutable object identifier that uniquely identifies a user | https://learn.microsoft.com/aspnet/core/blazor/security/webassembly/microsoft-entra-id-groups-and-roles | 2026-11-22 |
| T22 | Inventory API: delegated user tokens only, 403 for service principals and managed identities; resources carry `name`, `type` (such as `microsoft.powerapps/canvasapps`), `properties.environmentId`, `properties.displayName`, `properties.ownerId` | https://learn.microsoft.com/power-platform/admin/inventory-api | 2026-11-22 |
| T23 | The group object ID is immutable; tokens carry at most 200 groups for JWT; app roles avoid the overage | https://learn.microsoft.com/entra/identity/hybrid/connect/how-to-connect-fed-group-claims | 2026-11-22 |
| T24 | Flow run duration is 30 days, including pending approvals: "After 30 days, any pending steps time out". Long-running approvals store approvals in Dataverse and use a second flow that acts on the response | https://learn.microsoft.com/power-automate/limits-and-config ; https://learn.microsoft.com/power-automate/modern-approvals | 2026-11-22 |
| T25 | "An approval flow can wait for 28 days. If the wait time exceeds 28 days, that flow fails"; the approval stays in the action center | https://learn.microsoft.com/power-automate/approvals-known-issues | 2026-11-22 |

---

## 15. UNVERIFIED in this document

1. The per-row storage sizes in section 12 (planning estimates).
2. Whether the inventory resource `name` is unique across environments for every resource type. The claim key also includes the type to reduce the risk.
3. Whether a Dataverse application user (service principal) can receive security roles through an Entra group team.
4. Whether the `json` type works in Azure SQL ledger tables. Learn does not list it among the unsupported types [T15].
5. Whether a Dataverse plug-in can read the calling client application's ID, or the "AI assisted" signal, at write time.
6. Whether a Dataverse administrator can deactivate plug-in steps that ship in a managed solution.
7. Dataverse file column size limits, for the snapshot fallback.
8. The Dataverse capacity add-on price.
9. The volume of a first gap check run in a 5,000-person tenant.
10. Whether choice columns can be part of a Dataverse alternate key. The model avoids it.

---

## 16. Diagram specs

All diagrams use the D-04 palette: navy `0A2A4A` for text and lines, blue `0071DC` for group header bars, gold `FFC221` as the accent. Fills are light only: white `FFFFFF`, light blue `E8F1FB`, light gold `FFF4D6`, light gray `F3F5F8`. No dark fills. Built later in Visio, samples first, every PNG checked.

### D22-1 Logical data model, one page

- **Title:** The product's logical data model.
- **Purpose:** Every R1 entity with its key columns and its main relationships, with cardinality marks, platform-neutral.
- **Groups (boxes with blue header bars):**
  1. Configuration core (center, light gold): C01, C02, C46, C04, C05, C06, C07.
  2. Taxonomy (light blue): C08 to C28 and C34 to C39 that are not rules.
  3. Rules (light blue): C23, C25, C26, C29, C30, C42, C31, C32, C33.
  4. Register (white): O01, O02, O03, O04, O06.
  5. Decisions and approvals (white): O08, O09, O10, O21.
  6. Change and deployment (white): O11, O12, O13, O14, O15, O16, O17.
  7. Links and evidence (white): O18, O19, O20.
  8. Intake and gap check (white): O07, O22, O23.
  9. Identity and audit (light gray): O05, O24.
- **Nodes:** one box per entity with its ID, name, business key and history class letter (A, F, E, M, V) in a small gold tag.
- **Edges (navy, labeled, crow's-foot cardinality marks):** O02 to O01 "grouped in (0..n to 0..1)"; O03 to O02 "part of (0..n to 1)"; O04 to O02 "from" and "to"; O04 to O03 "to component"; O06 to O02, C28, O05; O09 to C18 "of type"; O09 to O09 "supersedes, amends"; O10 to O09 "approves (0..n to 1)"; O10 to C32 "required by"; O11 to O02 "changes"; O11 to O09 "authorized by"; O12 to O11 "also affects (cascade)"; O14 to O13, O15, O11, O16; O14 to O14 "corrects"; O17 to O02, O16, O15; O19 to C23; O20 to O09, O10, O11, O14, O21, O23 "proves"; O23 to O22 "found by"; O23 to O03 "becomes" and "about"; C30 to C29 and O05 "group only"; C42 to C29 and O05 "workload grant"; C46 to C01 "drafts"; C46 to C02 "becomes at activation"; O09 to C46 "decides"; C33 to C08, C20, C34. One bundled edge from every operational group to C02 "stamped with"; one bundled edge from every taxonomy and rule box to C01 "belongs to".
- **Accent:** gold outline on the one cascading relationship (O12 to O11); gold tag "never deleted" on C02, C46, O05, O09, O10, O14, O20, O24.

### D22-2 Decision and approval lifecycle

- **Title:** Decisions are superseded, never edited. Approvals are answered once.
- **Purpose:** Shows the states of O09 and O10 and who may write in each.
- **Lanes:** Person (proposer, decider); AI client; Engine; Approval writer (approval runner); Store.
- **Nodes:** "Decision proposed" (O09); "Approval records created" (O10, pending); "Card answered" (approval runner); "Outcome written once" (O10 frozen); "Counts toward requirement?" (diamond: person and user and holds role); "Decision accepted" (O09 frozen); "New decision proposed, Supersedes = old"; "New decision accepted"; "Old decision reads superseded (derived)"; "Expired: new approval record".
- **Edges:** AI client to "Decision proposed" labeled "can propose (Is AI Created)"; dotted gold from AI client to "Card answered" labeled "AI never approves"; "Card answered" to "Outcome written once" "approval writer only"; diamond yes to "Decision accepted" "all rules met"; diamond no labeled "not counted"; "Decision accepted" to "New decision proposed" "change needed"; "New decision accepted" to "Old decision reads superseded" "derived, no write".
- **Accent:** a light gold band behind the frozen states.

### D22-3 Deployment record write path

- **Title:** One deployment, one record, written once.
- **Purpose:** Shows the three arrival routes, the idempotent retry, the correction and the three flags.
- **Groups:** Outside the product (light gray): client pipeline record step, owner's AI tool through the MCP server, person in the app. Inside the product (white): engine, O14, O11, C33, O15 with C35.
- **Nodes:** "Record arrives (Idempotency Key)"; diamond "Key exists?"; "Return existing record"; "Content differs: refuse and audit"; "Compute flags at write time"; O14 with three light gold badges: outside window, other path, missing approval; "Correction: new record, Corrects Record = old".
- **Edges:** each route to "Record arrives"; "Key exists?" yes to "Return existing record"; yes and different to "refuse and audit"; no to "Compute flags"; "Compute flags" to O14 "insert once"; O11 to "Compute flags" "window, authorization"; C33 to "Compute flags" "gate level in force"; O15 and C35 to "Compute flags" "allowed routes"; dotted from O14 to "Correction" "never edited".

### D22-4 Gap check to claim to component

- **Title:** The gap check stores an ID, a link and a date. Nothing else.
- **Purpose:** Shows D-15 as data: run, compare, claim, dismiss, vanish.
- **Lanes:** Admin (as themselves); Microsoft inventory (light blue, outside); Engine; Claim list (O23); Register (O03, O02).
- **Nodes:** "Admin starts run" (O22); "Inventory query, delegated token"; "Item not in register"; "Claim Key exists?"; "Update last seen"; "New open claim"; "Owner claims"; "Technical component created (External Id, Url, Label As Seen, Last Seen On)"; "Dismissed with reason"; "Registered component not seen"; "Vanished claim"; "Person confirms gone: Retired On".
- **Edges:** labeled "as the admin", "compare by External Id", "same item again", "claim", "dismiss", "not returned", "confirm".
- **Annotation (gold accent):** "Never stored: inventory properties, Microsoft's owner as a fact, a copy of any run."

### D22-5 One model, two bindings

- **Title:** The same logical model on Dataverse and on Azure SQL.
- **Purpose:** Shows how each logical feature binds to each platform, for the D-22 platform choice (since settled by D-26: Azure first, Dataverse a possible later option).
- **Groups:** Left column "Logical" (white); middle "Dataverse" (light blue); right "Azure SQL" (light blue).
- **Rows (nodes in each column):** configuration reference (lookup; foreign key); engine meaning (global choice; check constraint); history class M (auditing; temporal table); history class F (freeze plug-in; updatable ledger plus block predicate); history class A (append-only plug-in; append-only ledger); configuration writes (activation-only plug-in; activation-only block predicate); roles (Entra group teams; app roles on groups); workload grants (application user with a direct role; app role on the service principal); row visibility (ownership and role depth; RLS filter predicates with session context); idempotent record step (upsert by alternate key; unique key and merge); who performed (engine columns plus `createdonbehalfby`; engine columns from the validated token).
- **Edges:** from each logical node to its two bindings, labeled with the mechanism.
- **Accent:** gold markers on the rows tied to working defaults the product owner can override: workload grants (open design question 12, see 28-working-defaults.md), ledger and erasure (28, Q21), row visibility (28, Q13).

---

## 17. Open design questions

Only questions not settled by a decision (the records are in [docs/decisions](../../decisions/)). Questions 2 and 3 are now working defaults in [28-working-defaults.md](28-working-defaults.md). Question 1 stays open: it is open design question 12 (see 28-working-defaults.md, section 3). The model uses its recommended option A as the working default until the product owner answers.

### Question 1: How do pipeline identities get the product's workload role, given "roles only through Entra groups"?

- **Status.** Open design question 12 (see 28-working-defaults.md, section 3). Working default: A.
- **Background.** D-11 says people get product roles only through Entra groups, and a check flags any direct assignment. D-19 says a step at the end of a client's pipeline records each deployment. That step signs in with the pipeline's own workload identity (a service principal or managed identity), not as a person, and it needs the deployment recorder role (C29, Is Workload Role). The approval writer has the same need (28, issue 7). On the Azure binding, if a service principal is put in a group and the app role is assigned to the group, Entra ID "doesn't add the roles claim" to its tokens [T20]. On Dataverse, application users normally get roles directly, and whether they can get them through a group team is UNVERIFIED.
- **Example.** A client has 40 pipelines across Azure DevOps and GitHub. Each uses its own federated workload identity. Each must be allowed to write deployment records, and nothing else.
- **Options.**
  - **A. A documented exception for non-human workloads only, recorded as workload grants.** Each workload identity gets its workload role directly (an app role assignment on Azure, a role on the application user on Dataverse). Each assignment is a C42 Workload Grant row with a reason, the deciding decision and an expiry, so the direct assignment check lists it as allowed. C30 stays group-only (V35). People still get roles only through groups. Agent identities and agent users never get a grant. *Pros:* works on both platforms today; each pipeline is visible and can be revoked alone; least privilege. *Cons:* one more row per pipeline; the rule "only through groups" gets a stated exception for non-people.
  - **B. Strict groups: the workload identity joins the recorder group.** *Pros:* one rule for everyone. *Cons:* does not work on the Azure binding today [T20]; UNVERIFIED on Dataverse.
  - **C. One shared recorder identity.** All record steps call a small relay that holds one identity with the role. *Pros:* one assignment. *Cons:* every pipeline shares one credential path; the record loses which pipeline identity wrote it; a larger blast radius.
  - **D. No workload role: record only through people.** Pipelines don't write. Owners record deployments through their AI tool or the app. *Pros:* D-11 holds with no exception. *Cons:* weakens D-19's main route and the Flag level, because records arrive late or not at all.
- **Recommendation.** A.
- **What the answer changes.** With A, C42 Workload Grant stays as designed in section 3.3, the direct assignment check reads it, and the setup service registers each pipeline identity through a configuration change. With B, C42 goes, and the design waits on a platform that puts group roles into a workload's token. With C, C42 shrinks to one grant, the product ships a relay component, and O14 records the relay plus a pipeline label as reported. With D, C42 keeps only the approval writer, the record step samples are dropped, and the Flag level depends on people.

### Question 2: On the Azure binding, should decisions, approvals and deployment records sit in ledger tables that can never delete a row?

Settled as a working default in 28-working-defaults.md (Q21); the product owner can override it.

### Question 3: By default, can every product role read every row, or only what they need?

Settled as a working default in 28-working-defaults.md (Q13); the product owner can override it.

---

## Proposed by the build (2026-11-22)

- **Source.** The build in `src/` and its logical model (`src/model/logical-model.json`, columns with status proposal).
- **Status.** Every item below is a proposal. The tables above keep their meaning until the product owner accepts an item: no settled decision and no working default changes. The design questions the build could not settle are marked as open design questions in the items below.

### B22-1 O10 Approval Record: three columns, and "Approval Rule or Decision Right"

The runtime writes three O10 columns this document does not list. The logical model carries them with status proposal.

| Column | Type | Req | Ref | Description |
|---|---|---|---|---|
| Decision Right (proposed) | ref(C31) | C | CFG | The decision right an accountable card asks for. Every decision type goes on at least one card, to the holder of its accountable letter (28, issue 1), and that card has no Approval Rule. Written by the engine at creation. Relationship RX28 (O10 to C31, 0..1 to 0..n, Restrict) is not in section 6 |
| Is Consultation (proposed) | bool | N | | Yes when the card is a consultation (C32 Is Consultation): the answer is stored as recommendation only and never counts. Written by the engine at creation |
| Approver Holds Required Role (proposed) | bool | C | | The approval runner's record of whether the responder held the required role or owner role when answering. Document 25, section 5.5 has the adapter supply it "from the approval runner's record", and no column held it. Written only by the approval writer, with the answer, never by the engine. Required once answered |

- **Approval Rule or Decision Right.** Approval Rule stays required in the logical model, but an accountable card carries Decision Right instead. Proposal: Approval Rule becomes conditional, with the rule "exactly one of Approval Rule and Decision Right".
- **Section 8.2.** Decision Right and Is Consultation join the request columns (the engine writes them at creation). Approver Holds Required Role joins the approval writer's row (written once, while pending, with the outcome).
- **Why every column freezes.** OP-03 reads Decision, Approval Rule or Decision Right, Outcome Meaning and Approver Holds Required Role from the stored row. If any of them could change on a final record, an approval given for one decision could count for another, or for a responder who lacked the role. The build therefore refuses every caller write to O10 except the approval runner's answer, and every engine write to a final record except Counts Toward Requirement or the same outcome again (document 25, B25-3). This matches the last row of section 8.2.

### B22-2 O23 Claim Key: 450 characters, or up to 614

The build forms the key as `<finding kind>|<source type>|<External Id>`, with the source type and the External Id lowercased, because Microsoft types and IDs compare without case. A vanished claim keys on the component kind instead (`vanished|<kind key>|<External Id>`), because a vanished item was not seen. With the longest values the columns allow, an unregistered key reaches 12 + 1 + 200 + 1 + 400 = 614 characters, while Claim Key is text(450).

- **A. Widen Claim Key** (for example text(620)). *Pros:* the key stays readable. *Cons:* on Dataverse an alternate key is limited to 900 bytes (document 20, section 2.9, [S15]); if a text character takes two bytes there, as the present 450 suggests, a longer key no longer fits (inference, UNVERIFIED).
- **B. Store a hash of the full key** (SHA-256 as 64 hex characters) in Claim Key. *Pros:* fits both bindings with room to spare; the readable parts already sit in Finding Kind, Source Type As Seen and External Id; the engine already hashes canonical text. *Cons:* the stored key is not readable; every lookup computes the hash first, which the engine does anyway.
- **C. Shorten the inputs** so the key fits 450 (for example cap Source Type As Seen). *Cons:* it cuts a Microsoft value the claim promises to keep as seen.
- **Recommendation.** B. It changes the O23 Claim Key row (type text(64), "the SHA-256 of the engine-built key"), section 9.2 step 3 (the engine builds the key, then hashes it), and the OP-13 plan's claimKey (document 25, B25-4). The alternate key stays on Claim Key.

**Times Seen counts runs, not pages.** OP-13 reads each claim's Times Seen and Last Seen Run and writes absolute values, so a replayed page changes nothing and a second page of the same run does not raise the count. That matches the O23 description "How many runs have seen it".

### B22-3 Final states, fill-once columns and platform-supplied columns

- **Final state per class F entity** (section 2.3), as the logical model's notes name it: O08 once Tier Decision is set; O22 once Finished On is set; O14 at insert; O13 at insert, apart from its fill-once columns Digest, Source Commit and Source Repository Url, which fill once while empty and never change (Release Record Url, Name and Description freeze with the rest); C46 at submit (G-16); O09 at a final status meaning (G-05); O10 once Outcome Meaning leaves pending. A class F entity with no rule of its own in the guard is final at insert, so a new class F entity is frozen until its rule is written. Marking a column fill-once is a model data change only.
- **No empty update on a final row.** An update that names no column is refused on a final row (`record.final`), because it would still restamp the row.
- **A final column the facts leave out.** Today it reads as empty, which matches a Dataverse pre-image that leaves out null attributes. Whether the guard should require it instead is an open design question.
- **Platform-supplied columns** (section 2.1.1: Id, Created On, Created By, Modified On, Modified By) are dropped from every write, by any writer, the engine included.
- **A correction is one insert.** `record_deployment_correction` writes Corrects Record and Description in the new O14 row's insert, never as a later update (section 8.3).

### B22-4 O22 Estates Covered

The engine checks that Estates Covered is a list of distinct keys, and refuses an inventory copy in it (`input.malformed`). Whether each key must also be a value of the `estate` list (section 2.2, items 5 and 6) is open: `run_owner_check` writes the estate `entra-owners` (documents 23 and 24), and no shipped preset's `estate` list holds that value yet. This is an open design question.

### B22-5 O03 holds no estate key

OP-13 needs "the registered components in covered estates", but O03 Technical Component has no estate column. The MCP core counts a registered component as in the estate when its component kind is gap-check covered (C16). Two ways to close it: an Estate column on O03 (ref C07, `estate` list), set when a claim creates the component; or an estate on the component kind (C16), when every kind belongs to one estate. The build proposes neither; the product owner picks one before the OP-13 claim writes are built. The run's progress needs no new column: `seenThisRun` comes from Last Seen On since the run started and from claims whose Last Seen Run is the run; the position travels in the tool's skipToken and the counts live on the O22 row.

### B22-6 Decisions, links and the registration status

- **Withdrawn by "the proposer or the decider"** (section 8.1). The build reads the proposer as Created By Principal, which never changes; an AI client's row has the person as its principal (section 2.4). On Behalf Of names only the last write.
- **Delete policy** (section 2.5). The undo window of a delivery or document link counts from Created On, and a Created On later than now refuses the delete.
- **Registration status.** A request sets only draft on a Business Application, or archives a draft. Registered, and any move away from registered or archived, come only from the update-subject effect of an accepted decision.
- **Unknown meanings.** A decision Status Meaning, a C46 Draft Status or an O02 Status Meaning in the facts that is not a contract value is invalid input. O10 reads an unknown Outcome Meaning as final.
- **A decision about a review.** O09 has no review subject kind, so the build points such a decision at the review's application and puts the review ID in its context. O21 Outcome Decision links back. No change proposed.

### B22-7 From the generator drafts (draft, written while the platform was open under D-22)

1. **C01, C02 and O05 need each other through NOT NULL references** (the common Created By Principal and Performed By Principal, C02 Configuration Set and Instructed By, O05 Configuration Set Version). SQL Server has no deferred constraints, so the bootstrap of version 1 cannot insert their first rows on Azure. Proposal: make O05 Configuration Set Version conditional (empty only for rows written before version 1). O05's two self references also need the caller to give the first row's Id.
2. **C46 history type.** This document makes C46 an updatable ledger table (sections 2.3 and 11.1); documents 20 (section 3.5) and 25 (section 12.3) call it a plain table. Align on this document.
3. **Created By and Modified By** have no Azure column, because the history records the principal. Whether temporal history shows the principal is UNVERIFIED.
4. **Dataverse.** Alternate keys on O01 and O19 that section 10.2 does not list (section 10.1's rule gives them); an autonumber on C46, which section 10.1 does not list; option values wait for the publisher; O24 Row Id as text(36), because custom tables offer no GUID column (UNVERIFIED).
5. **UNVERIFIED:** foreign keys, checks, filtered indexes and `json` columns on ledger tables, and `ALTER TABLE ADD CONSTRAINT` on a ledger table. A technical test checks them before the Azure schema is applied.
6. **Configuration row names.** Live configuration rows carry the bundle's field names (`roleKey`, `entraGroupObjectId`, `value`, `basedOnPresetKey`), not this document's (Role, Entra Group, Value Json, Based On Preset). The two should meet, or the column map should carry the bundle name, before an adapter maps configuration rows (document 21, B21-7).
