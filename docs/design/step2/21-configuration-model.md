# Step 2, document 21: the configuration and taxonomy model

> **Platform (D-26).** The first release is designed to run in the client's own Azure subscription. Configuration rows (and their JSON columns) live in Azure SQL, and the engine is a portable library inside the one service, whose API is the only write path. In section 11, the Azure binding is the one that applies. The Dataverse binding (including seed web resources) is kept as a possible later option. The text below was written while the platform was still open (D-22), so it describes both bindings.

- **Date:** 2026-11-22. **Status:** design draft, written client-neutral.
- **Step:** 2 of 3 (the design). This document designs the configuration layer at the center of the ERD (D-06): every configuration entity and its fields, versioned configuration sets, presets, the shipped seed file, validation, how the engine reads configuration, and how the gate ladder becomes configuration rows.
- **Names:** every entity, column and term comes from `20-glossary-and-entities.md` (document 20). Terms this document needs and document 20 lacks are listed in section 13, "New terms". Document 20 is not edited here.
- **Name of the product:** none yet, so "the product" or [Product name] (D-05).
- **Platform-neutral (D-22).** The model is logical. Section 11 binds it to Dataverse and to a database in the client's own Azure subscription. Where D-22 reopens D-02, D-10, D-11 or D-18, this document follows them as written and says what changes on the Azure binding.
- **Follows:** decisions D-01 to D-22 (the decision records are in [docs/decisions/](../../decisions/)) and the change gate options of D-03. D-03 is still open, so every gate value here is a preset value, not a decision.
- **Working defaults.** Aligned to [28-working-defaults.md](28-working-defaults.md) (2026-11-22). Its sections 1 and 2 are used as the design. A default that is still an open design question (its section 3) is marked "(working default, open design question N)", with N as numbered in 28. Other references read "(28, issue N)" or "(28, QN)". None of them is a decision, and the product owner can still change any of them.
- **Defaults this document uses most:** fixed engine meanings with configurable labels (working default, open design question 1); the AI approval lock is locked (decided, D-02 and D-10) and AI-only code approvals from client tools never count (working default, open design question 7); one working set with an optional portfolio scope on some rule rows (28, Q2). Nothing here re-asks them.
- **Sweep proposals** come from a review of Microsoft announcements up to 2026-11-21 and are marked "(sweep proposal)". They are proposals, not decisions.
- **Patterns** from governance practice are cited by idea ID (for example I-245), generalized and used as patterns only. The 2021 tables are not used (D-22).
- **Microsoft facts** carry a source ID. S-IDs are the sources of document 20; T-IDs are new sources checked for this document. Section 15 lists every URL with "checked 2026-11-22". UNVERIFIED marks what no source confirmed.

---

## 0. In one screen

1. **Everything a client can tailor is a configuration row** (D-06, D-07, D-10). Tiers, stages, gates, change types, roles, decision rights, approval rules, gate levels, documentation rules, review cadences and separation of duties are rows. The engine never branches on a fixed tier number or label.
2. **A client has one working set.** It changes only through versions. Each version is a full, hashed snapshot. Exactly one version is active. Every operational record stamps the version it was judged by.
3. **A draft becomes a version only at activation** (28, issue 3). A draft is a patch on a Configuration Draft row (C46). It moves draft, submitted, then activated or discarded. Activation writes a new, append-only Configuration Set Version (C02) and the live rows in one step; the previous version is then superseded. Nobody "activates" by hand, and no person, role or tool writes configuration tables directly. The engine activates a draft when its configuration change decision is accepted by the people its approval rules name.
4. **AI proposes, a person accepts** (D-02, D-10). An AI client, acting as the admin, builds a draft as a patch of operations by key. The engine validates it and computes the diff. A configuration admin accepts the engine's diff on an approval card (working default, open design question 4). No tool can activate a version.
5. **Presets are shipped configuration sets.** Four full presets: Balanced (the default three-tier preset, canonical), Starter and Regulated (Balanced with other gate rows and separation rules), and Microsoft CoE baseline (sweep proposal, shipped in the first release by working default, 28 Q6). Three overlays: small team and strict separation (D-20), and linked change management (D-08). A client picks one full preset and any compatible overlays, then owns the result (28, Q3).
6. **The seed is JSON, never the live copy** (D-10). One bundle per preset, references by stable key, never by ID. A JSON Schema checks shape; the engine's validator checks meaning (44 rules, section 9).
7. **The gate ladder is one Gate Policy row per tier, change type and target stage.** The level (off, record, flag, advise, enforce) is engine contract; which level applies where is configuration.
8. **The engine reads a compiled snapshot**, cached per process and checked against the active version number on every request. A configuration change takes effect on the next request, with no rebuild or redeploy.
9. **Bindings:** on Dataverse, configuration tables plus JSON in multiline text columns, a seed shipped as read-only web resources and applied by the engine's own API. On Azure, a `config` schema with native `json` columns, a seed loaded by a migration and applied by the same engine code.
10. **Seeded defaults people meet first:** every decision goes on a person's card, except the locked engine acceptance list (working default, open design question 8); the single most specific rule group applies (28, issue 2); the change manager authorizes tier 3 changes and emergencies, and the change board decides exceptions about a change (28, issue 4); one list of fourteen human roles plus two workload roles (28, issue 7); one list of every setting (section 3.6).

---

## 1. Rules this model follows

| # | Rule | Source |
|---|---|---|
| 1 | Configuration is data. A new rule is a row, never a code change, a flow change or a redeploy | D-06, D-10; I-246 |
| 2 | The only fixed values are the engine contract (document 20, section 2.4, plus the additions in section 13). Labels, counts and order stay configurable (working default, open design question 1) | D-06; 28, question 1 |
| 3 | The engine judges every write against the active version and stamps that version on the record | D-07, D-10; I-266 |
| 4 | AI never approves and never activates. Configuration tools can only propose | D-02, D-10 |
| 5 | Roles reach people only through Entra groups. Presets never hold group IDs | D-11 |
| 6 | References between configuration rows use stable keys. Seeds, presets, patches and MCP tools never carry internal IDs for configuration rows | D-07, D-10; document 20, section 3.2 |
| 7 | Rows in use are retired, never deleted. Keys are never reused | document 20, section 2.3; I-245 |
| 8 | Missing or broken configuration fails loudly. The engine never falls back to built-in defaults that hide a broken configuration | Field lesson: a page ran silently on fallbacks after its configuration file was renamed |
| 9 | Configuration is tested as code: the seed passes the same validator in the public repo's build as in the client's store | I-204 |
| 10 | Links, not copies. A mapping to a Microsoft term stores the term, the source URL and the date checked, never a copy of Microsoft's data or scores | D-15, D-17, D-19; sweep proposal for risk scores |
| 11 | Shipped presets hold no secrets and no tenant identifiers | Release pipeline practice: identifiers only, no secrets |
| 12 | No person, role or tool writes configuration tables directly. Drafts live on C46; the live rows, C02 and the version number change only inside the activation operation | D-10; 28, issue 3 |
| 13 | Every decision is accepted on a card answered by a person, except the decision types on the locked list in the setting `engine-acceptable-decision-types` (working default, open design question 8) | D-02; 28, issue 1 |

---

## 2. Three places a value can live

| Place | What lives there | Who changes it | How it changes | Example |
|---|---|---|---|---|
| **Configuration rows** (this document) | Every business rule and every taxonomy label | An AI client on a configuration admin's instruction; accepted by a person | A new configuration set version | Tier 3 production changes need the change manager |
| **Engine contract** (document 20, section 2.4) | The few meanings the engine must understand to enforce safety | Only a product release | A new engine version, published in the public docs | "accepted" means a decision satisfies a gate; only "person" can approve |
| **Environment** (Dataverse environment variables, Azure app settings) | Infrastructure switches needed before configuration can load, or that differ per environment and carry no business rule | The installer or the platform admin | Set at import or deployment | How often the engine probes for a new version (section 10) |

Nothing that decides who approves, what is required or how strict a gate is may live in the environment (D-06). Section 11 lists the few environment switches.

---

## 3. Configuration entities and their fields

### 3.1 How to read the field tables

- One table per entity, with the document 20 ID. **Field** is the payload and seed name: the lowerCamelCase form of the logical column name (document 20, section 3.3). A reference to another configuration row ends in `Key` (one) or `Keys` (several) and carries that row's key.
- **Type:** `text`, `int`, `bool`, `date`, `url`, `json` (an object or array with its own sub-schema), `key(entity)`, `keys(entity)`, `contract(name)` (one value of a fixed engine meaning list).
- **Seed:** `R` required in a seed row; `O` optional; `W` set only in the client's working set, never in a shipped preset (it carries a client fact such as a group ID or a portfolio ID); `S` set by the engine, never written by a seed or a patch.
- **(new)** marks a column document 20 does not have yet. Section 13 lists them for the glossary owner.
- `"*"` marks a scope field that accepts a wildcard in a seed (section 8.4). Stored rows never hold a wildcard.

### 3.2 Fields every configuration row has

Applies to C04 to C40. C01 to C03 and C46 have the common columns of document 20, section 2.2, only.

| Field | Type | Seed | Description |
|---|---|---|---|
| key | text | R | Stable identifier: lowercase letters, digits and hyphens, 1 to 64 characters, starting with a letter. Unique per entity in the set; for child entities (C07, C10, C19) unique within the parent (proposal, section 13). Never reused |
| label | text | R | The text people see. Can be translated |
| description | text | O | One or two plain sentences for people and agents: what the row means and why it exists |
| sortOrder | int | O | Display order only. Never used as logic |
| isActive | bool | O | False when the row is retired. Defaults to true in a seed. Retired rows stay so old records still resolve |
| origin | contract(origin) | S | Shipped or local. The engine sets local when a client change touches a shipped row. Upgrades never overwrite local rows |
| configurationSet | reference | S | The set the row belongs to |
| engineMeaning | contract | R where listed | Only on the entities that map labels to engine meanings: C05, C12, C36, C38 |

Common columns from document 20, section 2.2 (Performed By Actor Kind, On Behalf Of, Client App, Is AI Created, and the platform's created and modified columns) apply too and are set by the engine. So do the common columns Engine Version and Engine Checked On, which document 22 adds (28, issue 15 and Q16).

### 3.3 Configuration core (C01 to C07, C46)

**C01 Configuration Set.** A whole, named set of configuration rows: a preset, or the client's working set. Created by the apply operation (section 6.7), never by a seed.

| Field | Type | Seed | Description |
|---|---|---|---|
| setKind | contract(configuration set kind) | S | Preset or working |
| basedOnPresetKey | key(preset) | S | The full preset a working set was first copied from |
| isInForce | bool | S | True for the one working set the engine reads |
| currentVersionNumber | int | S | The active version's number. The one value the version probe reads (section 10.2) |

**C02 Configuration Set Version.** An append-only snapshot of a set. Only the activation operation creates one (class A, append-only; 28, issue 3), so a version never exists before a person accepted it. Its state is derived, never stored: a version is **active** when its number equals the set's currentVersionNumber, and **superseded** when a higher number exists. A preset's one version is written by the engine when it stages the release's seed (section 4.1).

| Field | Type | Seed | Description |
|---|---|---|---|
| configurationSet | reference | S | The set this version belongs to |
| versionNumber | int | S | A whole number that only goes up. Assigned at activation, so discarded drafts leave no gaps |
| sourceDraft (new) | reference(configuration draft) | S | The C46 draft this version activated. Empty for a preset's version and for version 1 under the bootstrap rule (section 4.8) |
| baseVersionNumber | int | S | The active version this one replaced. Empty on version 1 |
| changeJson (new) | json | S | The patch as applied, after any rebase: the ordered operations by entity and key that turn the previous version into this one (section 8.2) |
| contentJson | json | S | The full snapshot of every row, in the bundle format. Final |
| contentHash | text | S | SHA-256 of the canonical form of contentJson, so a record can prove which rules applied |
| snapshotSchemaVersion (new) | text | S | The seed format version contentJson follows, so the engine can still read old snapshots (document 25; 28, issue 15) |
| validationJson (new) | json | S | The validation result inside the activation transaction, with any warnings the approver acknowledged (section 9) |
| changeSummary | text | S | A plain list of what changed, computed by the engine from the diff. Never the AI's own text |
| appliedPresetsJson (new) | json | S | The full preset and overlays applied, each with its content version. Set on version 1 and on any later re-base |
| publishedOn | date | S | When this version came into force |
| instructedBy | reference(principal) | S | The admin who instructed the change (D-10) |
| changeDecision | reference(decision) | S | The configuration change or gate policy change decision behind this version |
| seedVersion | text | S | The product release whose seed this version started from, if any |

**C46 Configuration Draft (new entity, C46 adopted in document 20, section 2.10).** A proposed change to the working set, held as a patch until a person accepts it (28, issue 3). It exists on both bindings. Class F (freeze on final): editable while its status is draft, frozen on submit, final when activated or discarded. Only the engine's draft operations create and change it, acting for the caller; no seed or patch writes it.

| Field | Type | Seed | Description |
|---|---|---|---|
| configurationSet | reference | S | The working set the draft changes |
| recordNumber (new) | text | S | A number such as CFG-00012, assigned by the engine; tools and pages name a draft by it (document 22) |
| draftStatus (new) | contract(draft status) | S | Draft, submitted, activated or discarded (section 4.2) |
| baseVersionNumber | int | S | The active version the draft started from. Used for concurrency (section 4.5) |
| changeJson | json | S | The patch: the ordered operations by entity and key that turn the base version into the proposed set (section 8.2) |
| previewContentJson | json | S | The full set the patch would produce on its base version, in the bundle format. A preview only; the engine never reads it to judge a record |
| validationJson | json | S | The last validation result: errors and warnings with rule IDs (section 9) |
| changeSummary | text | S | A plain list of what would change, computed by the engine from the diff. Never the AI's own text |
| proposalRationale | text | O | Why the change is wanted, in the words of the admin or the AI that drafted it |
| appliedPresetsJson | json | S | The full preset and overlays applied, when the draft applies presets (install, a preset change, an upgrade) |
| instructedBy | reference(principal) | S | The admin who instructed the change (D-10) |
| changeDecision | reference(decision) | S | The configuration change or gate policy change decision, set on submit |
| submittedOn | date | S | When the draft was submitted and frozen |
| expiresOn | date | S | When an unsubmitted draft is discarded: created plus the setting `draft-expiry-days` |
| resultingVersion | reference(version) | S | The C02 version activation created. Empty unless the draft was activated |
| discardedOn | date | S | When the engine discarded it |
| discardReason | text | S | Withdrawn, rejected, expired or rebase conflict |

**C03 Preset.** A shipped, read-only configuration set that a client applies, copies and adjusts. In a seed it is the bundle's `manifest` (section 8.2).

| Field | Type | Seed | Description |
|---|---|---|---|
| presetKey | key | R | Stable key, such as `balanced` or `microsoft-coe-baseline` |
| productRelease | text | R | The product release that shipped this preset |
| presetKind (new) | contract(preset kind) | R | Full (a complete set) or overlay (a partial set that changes some rows of a full preset by key) |
| extends (new) | key(preset) | O | For a full preset written as changes to another full preset, the parent. The build resolves it into a flat bundle |
| conflictsWith (new) | keys(preset) | O | Overlays that must not be applied together with this one |
| areas (new) | text list | R | The areas it covers: taxonomy, people, rules, gates, change management, documentation |
| contentVersion (new) | text | R | Semantic version of the preset content: major when a key is removed or renamed or a meaning changes, minor when rows are added, patch for labels and descriptions (I-218) |
| schemaVersion (new) | text | R | The seed format version the bundle follows |
| setupNotes (new) | json | O | Steps the configuration service takes in Microsoft's own tools, each with text and a link. Never read or enforced by the engine |
| checkedOn (new) | date | O | When the Microsoft terms in the preset were last checked at their sources |

**C04 Setting.** One named switch with one value.

| Field | Type | Seed | Description |
|---|---|---|---|
| valueType | text | R | The value's shape: `text`, `number`, `yes-no`, `json`, `key:<entity>` (or `key:list-value:<list key>`), or `contract:<contract name>` |
| value | json | R | The value itself (Value Json). May be null only where the setting says what null means, or where a warning is acceptable until setup fills it |
| isLocked | bool | O | True when no configuration change may alter the row: the AI approval lock (decided, D-02 and D-10) and the engine acceptance list (working default, open design question 8) |
| narrowsSettingKey (new) | key(setting) | W | Set on a portfolio row: the setting this row narrows for one portfolio. Only `change-management-mode` may be narrowed (28, Q2). The row has its own key and the same value type |
| portfolioId (new) | reference(portfolio) | W | The portfolio a narrowing row applies to. The most specific row wins |

Every setting, with its default and owner document, is listed once in section 3.6 (28, issue 15).

**C05 Status.** A configurable status label for one entity, mapped to one engine meaning.

| Field | Type | Seed | Description |
|---|---|---|---|
| appliesToEntity | text | R | The entity key the status belongs to, such as `decision` or `change-request` |
| engineMeaning | contract(per entity) | R | The fixed meaning behind the label (document 20, section 2.4) |
| isDefaultForMeaning (new) | bool | O | True for the label the engine picks when it sets this meaning, for example the draft label for an AI-created row. Implied when only one active label maps to the meaning |

**C06 Value List.** A named list of labels with no engine behavior. The row's key is its List Key.

| Field | Type | Seed | Description |
|---|---|---|---|
| isExtensible | bool | O | True when the client may add values |
| values | C07 rows | R | The list's values, nested in the seed |

**C07 List Value.** One label in a value list. Only the common fields; its key is unique within its list.

### 3.4 Taxonomy (C08 to C24, C27, C28, C34 to C39)

**C08 Tier.** A risk tier, ordered by rank.

| Field | Type | Seed | Description |
|---|---|---|---|
| rank | int | R | Higher means more serious. Unique. The engine compares ranks, never tier numbers or labels |

**C09 Tier Question.** A plain question that suggests a tier.

| Field | Type | Seed | Description |
|---|---|---|---|
| questionText | text | R | The question as people read it |
| helpText | text | O | A short explanation with an example |
| appliesToKindKeys | keys(application-kind) | O | The application kinds that see this question; empty means all |
| answerOptions | C10 rows | R | The allowed answers, nested in the seed |

**C10 Tier Answer Option.** One allowed answer and the tier it implies.

| Field | Type | Seed | Description |
|---|---|---|---|
| answerText | text | R | The answer as people read it |
| impliedTierKey | key(tier) | R | The tier this answer suggests. The most serious answer wins |
| triggers | json | O | Triggers Json: `reviewTypeKeys` and `gateRequirementKeys` this answer adds, such as a security review |

**C11 Vocabulary Mapping.** Joins one taxonomy row to a Microsoft or framework term.

| Field | Type | Seed | Description |
|---|---|---|---|
| sourceEntity | text | R | The entity key of the mapped row, such as `tier` |
| sourceKey | text | R | The key of the mapped row |
| vocabularyKey | key(list value in `vocabulary`) | R | Which outside vocabulary, such as `agent-risk-tier` |
| term | text | R | The outside term, quoted as its source writes it |
| sourceUrl | url | R | Where the term is defined |
| checkedOn | date | R | When the term was last checked at its source |

**C12 Lifecycle Stage.** A stage between idea and retirement.

| Field | Type | Seed | Description |
|---|---|---|---|
| stageNumber | int | R | Display number, 0 to 10 by default. Unique. Never used as logic |
| stageRank (new) | int | R | The order logic uses, such as "from this stage on" in a documentation requirement. Unique (document 25; 28, issue 15) |
| engineMeaning | contract(lifecycle meaning) | R | Pre-live, live or retired |
| leadingOwnerRoleKey | key(owner-role) | O | The owner role that leads work in this stage, when it is relative to the application |
| leadingRoleKey | key(role) | O | The product role that leads work in this stage, when it is organization-wide |

**C13 Gate.** A decision point after a stage.

| Field | Type | Seed | Description |
|---|---|---|---|
| afterStageKey | key(lifecycle-stage) | R | The stage this gate follows |
| gateDecisionTypeKey | key(decision-type) | R | The decision type that records passing the gate |

**C14 Gate Requirement.** What must be true before a gate decision can be accepted.

| Field | Type | Seed | Description |
|---|---|---|---|
| gateKey | key(gate) | R | The gate this requirement belongs to |
| tierKey | key(tier) | O | Applies only at this tier |
| applicationKindKey | key(application-kind) | O | Applies only to this kind |
| requirementKind | contract(requirement kind) | R | Decision accepted, document current, review done, or owner assigned (new meaning, section 13) |
| requiredDecisionTypeKey | key(decision-type) | O | Set when the kind is decision accepted |
| requiredDocumentTypeKey | key(document-type) | O | Set when the kind is document current |
| requiredReviewTypeKey | key(review-type) | O | Set when the kind is review done |
| requiredOwnerRoleKey (new) | key(owner-role) | O | Set when the kind is owner assigned: an active assignment of this owner role must exist (guideline G2, "every app names business, technical and data owners") |
| guidelineKey | key(guideline) | O | The guideline this requirement applies |

**C15 Application Kind.** The category of a business application.

| Field | Type | Seed | Description |
|---|---|---|---|
| categoryKey | key(list value in `application-category`) | O | A grouping label |
| defaultComponentKindKeys | keys(component-kind) | O | Component kinds usually found in this kind of application |

**C16 Component Kind.** The type of a technical component.

| Field | Type | Seed | Description |
|---|---|---|---|
| sourceToolKey | key(list value in `tool`) | R | The tool that holds this kind |
| sourceTypeHint | text | O | The tool's own type name, used to match gap check results |
| isGapCheckCovered | bool | O | True when the gap check can see this kind [S6] |

**C17 Dependency Type.** The kind of a dependency.

| Field | Type | Seed | Description |
|---|---|---|---|
| inverseLabel | text | R | The label read from the other side, such as "used by" |
| countsForImpact | bool | O | True when the impact walk follows this type |

**C18 Decision Type.** The kind of a decision.

| Field | Type | Seed | Description |
|---|---|---|---|
| needsOptionsConsidered | bool | O | True when the decision must list the options it rejected, as an architecture decision record does [S1] |
| defaultSubjectKind | text | R | The entity key the decision is usually about. The validator uses it to know whether the decision carries a tier |
| requiresApproval (new) | bool | R | True when the approval rules (C32) of this type decide who must approve. False when the type has no counting approval rules: the decision still goes on one card, to the holder of its accountable letter (C31), and that person's approval accepts it (28, issue 1). Either way a person answers a card. The only exception is a decision that matches the locked setting `engine-acceptable-decision-types` (working default, open design question 8). V19 and V20 read this column. No tool or page accepts a decision |
| outcomes | C19 rows | R | The allowed outcomes, nested in the seed |

**C19 Decision Outcome.** One allowed result for a decision type.

| Field | Type | Seed | Description |
|---|---|---|---|
| nextLifecycleStageKey | key(lifecycle-stage) | O | The stage an accepted decision with this outcome moves the application to |

**C20 Change Type.** The kind of a change.

| Field | Type | Seed | Description |
|---|---|---|---|
| requiresAuthorization | bool | R | True when a change of this type needs a change authorization decision answered by a person on a card |
| requiresStandardModel (new) | bool | O | True when a change of this type must name an active standard change model, which stands in for a person's authorization. When the model's conditions hold, the engine records the change authorization as accepted and links the model's Approved By Decision. The locked list allows this (working default, open design question 8) |
| allowsAuthorizationAfter | bool | O | True when authorization may follow the change, as for emergencies |
| requiresReviewAfter | bool | O | True when a post-change review is due |
| reviewDueDays | int | O | Working days until the post-change review is due |

**C21 Standard Change Model.** A pre-approved pattern for a standard change. None ship; the client lists its own.

| Field | Type | Seed | Description |
|---|---|---|---|
| changeTypeKey | key(change-type) | R | The standard change type this model belongs to |
| tierKey | key(tier) | O | The highest tier the model applies to |
| conditions | json | R | Conditions Json: what keeps a change standard, such as "labels and views only, no schema change" |
| approvedByDecision | reference(decision) | W | The decision that listed this model |

**C22 Document Type.** A kind of document.

| Field | Type | Seed | Description |
|---|---|---|---|
| defaultLinkKindKey | key(link-kind) | O | The link kind usually used |
| freshnessDays | int | O | Days after the last check when a current document becomes stale |
| templateUrl | url | O | A link to a template in the playbook |
| currentNeedsPerson (new) | bool | O | True when "current" set by an AI client needs a person's confirmation card. The seed sets it on document types a gate requires (document 23; working default, open design question 9) |

**C23 Documentation Requirement.** Which document type an application needs, by kind, tier and stage.

| Field | Type | Seed | Description |
|---|---|---|---|
| documentTypeKey | key(document-type) | R | The document type required |
| tierKey | key(tier) | O | Applies only at this tier |
| applicationKindKey | key(application-kind) | O | Applies only to this kind |
| fromStageKey | key(lifecycle-stage) | R | The stage from which the document is required |

**C24 Review Type.** The kind of a review.

| Field | Type | Seed | Description |
|---|---|---|---|
| producesDecisionTypeKey | key(decision-type) | R | The decision type a finished review records |
| defaultOwnerRoleKey | key(owner-role) | O | The owner role that usually leads this review |

**C27 Party.** One of the seven parties of D-01. Common fields only.

**C28 Owner Role.** A responsibility for one business application.

| Field | Type | Seed | Description |
|---|---|---|---|
| partyKey | key(party) | R | The party this owner role belongs to |
| requiresDeputy | bool | O | True when every holder must have a deputy |
| maximumHolders | int | O | How many principals may hold it for one application at once |

**C34 Target Stage.** The ALM stage of a deployment target.

| Field | Type | Seed | Description |
|---|---|---|---|
| rank | int | R | Order from development to production. Used for ordering only |

**C35 Change Path.** The routes a target expects deployments to arrive by.

| Field | Type | Seed | Description |
|---|---|---|---|
| allowedRouteKeys | keys(deployment-route) | R | The deployment routes this path accepts |

**C36 Deployment Route.** How a deployment record arrived. Field: `engineMeaning` (pipeline step, AI tool, person in app, other), required.

**C37 Link Kind.** What a link points to.

| Field | Type | Seed | Description |
|---|---|---|---|
| linkCategory | contract(link category) | R | Delivery, pipeline, document, evidence, Microsoft resource or external change |
| toolKey | key(list value in `tool`) | R | The tool the link opens |
| urlPattern | text | O | A regular expression a valid URL of this kind matches |
| idPattern | text | O | A regular expression a valid ID of this kind matches |

**C38 Actor Kind.** Who or what acted. Field: `engineMeaning` (person, AI client for a person, agent identity, pipeline, approval runner, engine), required. Only "person" can approve. The meaning "approval runner" (earlier "approval flow") covers the approval runner: the flows that send cards and write outcomes as the approval writer workload role (28, issue 7).

**C39 Guideline.** One version of one numbered guideline, with local override.

| Field | Type | Seed | Description |
|---|---|---|---|
| number | text | R | The permanent number, such as G9 |
| version | text | R | Two parts: the first changes the rule, the second only the wording |
| rule | text | R | The rule in one line |
| hubUrl | url | O | The canonical text. Null until the public hub location is decided |
| chapter | text | O | The playbook chapter it belongs to |
| localText | text | W | The client's own wording |
| localUrl | url | W | The client's own page |
| isAdopted | bool | W | False when the client chose not to adopt it |
| notAdoptedReason | text | W | Why not |
| supersedesKey | key(guideline) | O | The earlier version this row replaces |

### 3.5 Rules (C25, C26, C29 to C33, C40)

**C25 Review Cadence.** How often a review type is due at a tier.

| Field | Type | Seed | Description |
|---|---|---|---|
| reviewTypeKey | key(review-type) | R | The review type |
| tierKey | key(tier), `"*"` | R | The tier |
| intervalDays | int | R | Days between reviews |
| leadDays | int | O | Days before the due date when the first reminder goes |
| escalateAfterDays | int | O | Days after the due date when the deputy is asked |

**C26 Reminder Rule.** When to remind and when to escalate to the deputy.

| Field | Type | Seed | Description |
|---|---|---|---|
| subjectKind | text | R | What is due: review, document, approval or claim |
| leadDays | int | O | Days before due when the first reminder goes |
| repeatDays | int | O | Days between repeat reminders |
| escalateAfterDays | int | O | Days after due when the deputy is asked |
| channelKey | key(list value in `channel`) | O | Where the reminder goes |

**C29 Role.** A product-wide responsibility, held only through an Entra group (D-11).

| Field | Type | Seed | Description |
|---|---|---|---|
| partyKey | key(party) | R | The party this role belongs to |
| duties | text | O | The role's duties in plain text, for people and agents |
| canChangeConfiguration | bool | O | True only for roles allowed to instruct and accept configuration changes (D-10) |
| isReadOnly | bool | O | True for roles that never write, such as auditor and reader |
| isWorkloadRole | bool | O | True for roles held by non-human workloads, not people: the deployment recorder (pipelines) and the approval writer (the approval runner). A workload role can never approve. It is never bound to a group through C30; a workload identity holds it only through a recorded, expiring Workload Grant (C42, document 26) (28, issue 7; working default, open design question 12) |

**C30 Role Group Binding.** Binds one role to one Entra group. Never in a shipped preset: the setup service writes it in a client binding patch (section 6.8).

| Field | Type | Seed | Description |
|---|---|---|---|
| roleKey | key(role) | W | The role being bound |
| entraGroupObjectId | text | W | The group's directory object ID, held through a Principal Reference of kind group (O05) |
| bindingMethod | text | W | Dataverse group team, or Entra app role assignment |
| bindingDetail | text | W | The team ID or app role value on the platform |
| lastVerifiedOn | date | S | When the binding was last checked to exist |

**C31 Decision Right.** Who is responsible, accountable, consulted, informed, or (AI only) supports, for a decision type in a scope.

| Field | Type | Seed | Description |
|---|---|---|---|
| decisionTypeKey | key(decision-type) | R | The decision type |
| tierKey, lifecycleStageKey, changeTypeKey, applicationKindKey | key | O | Optional scope; rows with identical scope form one rule group (section 10.3) |
| portfolioId | reference(portfolio) | W | Optional portfolio scope (28, Q2). Operational rows travel by ID |
| letter | contract(decision right letter) | R | Responsible, accountable, consulted, informed or supports. Exactly one accountable per decision type and scope (V22). The two configuration decision types have one accountable, the configuration admin; the roles for each area (change manager, AI lead, platform admin) are consulted, not accountable (28, issue 6) |
| ownerRoleKey | key(owner-role) | O | The owner role holding the letter, when relative to the application |
| roleKey | key(role) | O | The product role holding the letter, when organization-wide. A supports row names neither |

**C32 Approval Rule.** Which approvals a decision type needs in a scope.

| Field | Type | Seed | Description |
|---|---|---|---|
| decisionTypeKey | key(decision-type) | R | The decision type that needs this approval |
| tierKey, lifecycleStageKey, changeTypeKey, applicationKindKey, targetStageKey | key | O | Optional scope; rows with identical scope form one rule group |
| portfolioId | reference(portfolio) | W | Optional portfolio scope |
| approverOwnerRoleKey | key(owner-role) | O | The owner role that must approve. Exactly one of this and approverRoleKey |
| approverRoleKey | key(role) | O | The product role that must approve |
| approvalMode | contract(approval mode) | R | Everyone must approve, first to respond, or sequential [S10] |
| sequence | int | O | Order within the rule group when the mode is sequential |
| minimumCount | int | O | How many approvals from this role are needed. Defaults to 1, and is never 0: there is no zero waiver. The only way to accept without a card is the locked list (28, issue 1; V28) |
| timeoutDays | int | O | Days before the approval expires and is restarted or escalated |
| requiresTypedReason | bool | O | True when the approver must type a reason |
| isConsultation (new) | bool | O | True when the answer is asked for advice only. It is recorded with the status meaning "recommendation only" and never counts toward the requirement (document 25; 28, issues 12 and 15) |
| guidelineKey | key(guideline) | O | The guideline this rule applies |

**C33 Gate Policy.** The gate level for one tier, change type and target stage (section 7).

| Field | Type | Seed | Description |
|---|---|---|---|
| tierKey | key(tier), `"*"` | R | The tier |
| changeTypeKey | key(change-type), `"*"` | R | The change type |
| targetStageKey | key(target-stage), `"*"` | R | The target stage |
| portfolioId | reference(portfolio) | W | Optional portfolio scope |
| gateLevel | contract(gate level) | R | Off, record, flag, advise or enforce |
| failMode | contract(fail mode) | O | Fail open or fail closed. Required at enforce, absent otherwise |
| emergencyRule | json | O | Emergency Rule Json: how an emergency passes and when its approval must follow (section 7.5) |
| aiOnlyCodeApprovalHandling | contract (new list, section 13) | O | Ignore, flag or stop, when the only code approval came from an AI (sweep proposal). When absent, the setting `default-ai-only-code-approval-handling` applies (shipped: flag). Whatever the handling, an AI-only code approval never counts as authorization (working default, open design question 7) |
| flagNotify (new) | json | O | `ownerRoleKeys` and `roleKeys` told about a flag. When absent, the setting `default-flag-notify` applies |
| setByDecision | reference(decision) | S | The gate policy change decision behind the version that set this row |

**C40 Separation Rule (new entity, proposed ID).** A separation-of-duties rule: who must not be the same person. Needed for the small team and strict separation presets (D-20) and for the field pattern that the person who starts a production deployment should not be its only approver (I-148, with the small-team exception of I-206). This entity is the one shape for separation of duties: OP-03 (document 25) reads these rows when it counts an approval, and Approval Rule carries no separation columns (28, issue 5). Each preset ships its own rows (28, Q10; section 6).

| Field | Type | Seed | Description |
|---|---|---|---|
| ruleKind | contract(separation rule kind) | R | Approver differs (the person answering an approval must differ from someone), or roles in different groups (two roles may not bind to the same Entra group) |
| decisionTypeKey | key(decision-type) | O | Applies to approvals for this decision type; empty means every decision type that requires approval |
| tierKey | key(tier) | O | Applies only at this tier |
| targetStageKey | key(target-stage) | O | Applies only for this target stage |
| mustDifferFrom | contract(separation subject) | O | For approver differs: the proposer of the decision, the instructing admin of a configuration change, the deployer of the change, a holder of an owner role, or a holder of a role |
| mustDifferFromOwnerRoleKey | key(owner-role) | O | Set when mustDifferFrom is an owner role holder |
| mustDifferFromRoleKey | key(role) | O | Set when mustDifferFrom is a role holder |
| roleKeys | keys(role) | O | For roles in different groups: the roles that may not share an Entra group |
| enforcement | contract(separation enforcement) | R | Refuse; allow only with an accepted exception decision; or allow and flag |
| exceptionDecisionTypeKey | key(decision-type) | O | The decision type that records an exception, when enforcement is exception decision |

### 3.6 Every setting (one list)

This is the one list of C04 Setting rows (28, issue 15). Every row exists in every set without a portfolio; V38 checks it, so the engine never falls back to a built-in value (rule 8). "Owner" is the document that designs how the setting is used. `history-retention-days` (document 22) is merged into `audit-retention-days`.

| Key | Value type | Default (Balanced) | Locked | Owner | What it does |
|---|---|---|---|---|---|
| `change-management-mode` | contract:change-management-mode | built in | No | 21 (D-08) | Built in or linked. A portfolio row may narrow it (28, Q2) |
| `ai-approval-lock` | yes-no | on | Yes | 21 (D-02, D-10) | AI never approves. Decided (D-02, D-10); 28 question 7 asks only about AI approvals from client tools |
| `engine-acceptable-decision-types` | json | intake at the lowest tier; change authorization for a standard change whose model is active | Yes | 21 | The only decisions the engine may accept without a person's card. Each entry is a decision type plus an engine acceptance condition (section 13.4). Extending it needs a product release (working default, open design question 8) |
| `reminder-channel` | key:list-value:channel | Teams | No | 21 | Where reminders go when a reminder rule names no channel |
| `guideline-hub-base-url` | text | null until setup | No | 21 | The base URL of the public guideline hub. Null gives a warning until setup fills it |
| `draft-expiry-days` | number | 30 | No | 21 | Days before an unsubmitted draft is discarded |
| `configuration-review-days` | number | 180 (Regulated: 90) | No | 21 | Days between reviews of the whole configuration |
| `default-flag-notify` | json | business owner, technical owner, change manager | No | 21 | Who hears about a flag when the gate policy row names nobody |
| `default-ai-only-code-approval-handling` | contract:ai-only-code-approval-handling | flag | No | 21 | What happens when the only code approval came from an AI and the gate policy row says nothing. It never counts as authorization (working default, open design question 7) |
| `confirmation-channel` | json | AI client may submit intakes, changes and decisions for the person; registering an application and marking a gate-relevant document current need a confirmation card; links and deployment records need nothing | No | 23 | Which channel counts as a person's act for each kind of AI draft (working default, open design question 9) |
| `unlinked-deployment-change-type` | key:change-type | normal | No | 23 | The change type used to pick a gate policy row for a deployment with no change request |
| `intake-visibility` | contract:intake-visibility | own (requester, PMO, platform admin, architect) | No | 22 | Who reads intake requests (28, issue 10 and Q13) |
| `claim-visibility` | contract:claim-visibility | admins and claimant | No | 22 | Who reads gap check claims (28, issue 10 and Q13) |
| `record-access-mode` | contract:record-access-mode | open read (portfolio read available) | No | 26 | Read scope of the register (28, issue 10 and Q13) |
| `agent-read-access` | yes-no | off | No | 26 | Whether agent identities may read anything |
| `principal-refresh-days` | number | 7 | No | 26 | How often Principal Reference type and state are refreshed from Entra |
| `forbidden-url-patterns` | json | a seed list of URL parts such as `sig=`, `token=` and `code=` | No | 26 | Refuses links that carry secrets. A client can add patterns |
| `audit-retention-days` | number | null: forever on Dataverse; set from the client's policy on Azure | No | 26 | Retention of audit entries, and of temporal history and Dataverse audit for mutable rows. Ledger tables ignore it (28, issue 15) |
| `link-undo-minutes` | number | 30 | No | 26 | Window in which a link's creator may delete it |
| `workload-grant-max-days` | number | 365 | No | 26 | The longest a Workload Grant (C42) may run. Its Expires On is required and at most this many days after the day it is validated (V28); renewal is a new decision (28, issue 8; working default, open design question 12) |
| `impact-max-depth` | number | 5 | No | 24, 25 | The deepest dependency walk the impact operation allows |
| `mcp-page-size-max` | number | 100 | No | 24 | The largest page any list tool returns |
| `engine-read-cache-seconds` | number | 0 (check every call) | No | 25 | Time to live for cached read operations. Not the configuration probe, which runs on every request (section 10.2) |
| `working-days` | json | Monday to Friday | No | 25 | The weekdays counted as working days, for due dates such as review and emergency approval days |

---

## 4. Configuration sets and versions

### 4.1 One working set, many versions

- **A client has exactly one working set in force** (document 20, C01; 28, Q2). Areas that need other rules use the optional portfolio scope on C31, C32 and C33, and a portfolio row that narrows the change management mode setting (C04), never a second working set.
- **Presets are also configuration sets** (kind preset), read-only. On both bindings they are stored as a snapshot only (one C01 row and one C02 row each, written by the engine when it stages the release's seed), not as materialized rows, so the engine and the pages never mistake a preset row for a live one.
- **Every change creates a new version.** The live configuration rows always equal the active version. Drafts live on C46 and never touch the live rows or C02. No person, role or tool writes configuration tables directly; only the activation operation does (28, issue 3).

### 4.2 Draft status and version state

A draft's status is stored on C46:

| Draft status | Configuration change decision | What it means | Who or what moves it there |
|---|---|---|---|
| **draft** | none yet | Being prepared. Editable | An AI client or a person with a configuration role creates it through the engine's draft operations |
| **submitted** | proposed | Frozen. Waiting for the approvals its rules need. To change it, withdraw it and start a new draft | The engine, on submit |
| **activated** | accepted | Became a version: the engine wrote a new C02 row, the live rows and the new version number | The engine, when the decision is accepted |
| **discarded** | withdrawn, rejected, or none (expired) | Never became a version. Kept as evidence of the proposal | The engine: on withdrawal, on rejection, or after `draft-expiry-days` without submit |

A version's state is derived, never stored, because C02 rows never change:

| Version state | What it means |
|---|---|
| **active** | Its number equals the set's currentVersionNumber. In force. Exactly one per working set |
| **superseded** | A higher version number exists. Kept forever: records stamped with it still need it |

The decision type is `gate-policy-change` when the draft touches any Gate Policy row, and `configuration-change` otherwise. Both are configuration rows with their own approval rules.

### 4.3 What a draft and a version hold

- **The draft (C46):** the patch from the base version, by entity and key; the computed preview; the last validation; the rationale; who instructed it.
- **The version (C02):** the patch as applied after any rebase; the final snapshot (contentJson), in the bundle format of section 8, which is what the engine reads (section 10); the hash; the validation inside the activation transaction; the summary; the decision behind it.
- **The hash** (contentHash): SHA-256 over a canonical form (keys sorted, no whitespace). A record stamped with version 12 can prove which rules applied, even after the rows changed again.
- **The diff and the summary,** computed by the engine. The approval card shows the engine's diff, never only the AI's description.
- **Who:** instructed by (the admin), performed by (actor kind and client app, for example "Claude Code on behalf of" the admin), accepted by (the decider on the decision).

### 4.4 Who activates

- **Nobody activates by hand, and no AI can.** Activation is a side effect of an accepted configuration change decision.
- **The decision is accepted when its approval rules are met by people.** In Balanced, Starter and the Microsoft CoE baseline, one person holding a role with Can Change Configuration (the configuration admin role, held through its Entra group) answers the approval card, and that person may be the admin who instructed the change. Regulated and the strict separation overlay require a second configuration admin, through a separation rule (working default, open design question 4; 28, Q10).
- **Protected changes are judged by the active rules, not the proposed ones.** A draft that changes who may change configuration (approval rules, decision rights or separation rules for the two configuration decision types, roles with Can Change Configuration, locked settings) needs the approvals the active version requires. A draft can never lower its own bar (V34).
- **The approval runner writes the outcome** (document 20, O10), as the approval writer workload role (28, issue 7). The engine checks that the responder is a user principal acting as a person and holds the required role at the moment of response. Entra group team membership on Dataverse can lag by up to 8 hours [S7], so an urgent revoke disables the user, not only the group membership.

### 4.5 Concurrency and rebase

- A draft records its base version. At activation, the engine compares it with the current active version.
- **Same base:** activate.
- **Different base, no overlap:** the engine re-applies the draft's operations to the new active version, revalidates, and shows the admin the updated diff before activation. Overlap means an operation on an entity and key that changed between the two versions.
- **Different base, with overlap:** refused. The AI rebuilds the draft on the new active version. Nothing merges silently.
- **Proposed by the build (2026-11-22):** rebase as an operation with five outcomes, and a wider reading of what an operation touches (B21-2 at the end of this document).

### 4.6 Rollback

There is no in-place rollback. "Go back to version 9" creates a draft (C46) whose patch turns the active version into version 9's snapshot, with the same validation and the same acceptance. The revert is itself a new version, so the history stays append-only. This replaces the field pattern of restoring a deleted configuration row from audit (I-244): rows are never deleted, and any earlier state is one revert proposal away.

### 4.7 Records already in flight

- **Requirements are frozen when a decision is submitted.** The engine creates the approval records from the version active at submission. A later version neither adds nor removes approvals for that decision. To apply new rules, the decision is withdrawn and submitted again.
- **Gates read the version active when the gate decision is recorded.**
- **A deployment record is judged by the version active when it arrives.** Gate Level Applied (O14) keeps the level used.
- **Retired rows still resolve.** An application on a retired tier keeps it until a tier decision moves it; new records cannot pick a retired row.

### 4.8 Install and bootstrap

1. The installer (on Dataverse, a person with the environment's system administrator role; on Azure, the deployment identity) installs the product. The seed bundles arrive as read-only staging (section 11).
2. A person with platform admin rights runs the apply operation (section 6.7) with the chosen full preset and overlays, usually through Claude Code and the product's MCP server, as themselves.
3. Because no version exists yet, there are no approval rules to satisfy, no roles are bound and no approval runner can hold a grant. The bootstrap rule (a safety floor): version 1 is accepted directly by that person, recorded as a configuration change decision with that person as decider and the engine or AI client as performer. This is the one case outside the locked list with no card, and it is engine contract, not a setting.
4. The next change is the client binding patch (section 6.8): it binds the product roles to the client's Entra groups. From then on every change follows section 5.

### 4.9 Product upgrades

A new product release can ship changed presets. The engine never applies them on its own.

| Case for each entity and key | What the upgrade does |
|---|---|
| Shipped row, unchanged by the client, changed in the new seed | Proposed as an update |
| Shipped row the client changed (origin local) | Kept. Listed as a conflict for the admin |
| New row in the new seed | Proposed as an addition, unless the client retired a row with that key |
| Row removed from the new seed | Never deleted. Listed as "no longer shipped" |
| Row the client added | Untouched |

The result is one upgrade draft (performed by the engine, actor kind engine), validated and accepted by a configuration admin like any other change, so a client's local changes are never lost (28, Q5). This answers the design check "configuration upgrades that keep a client's changes" as a design; a spike still tests it.

---

## 5. How a configuration change happens (D-10)

### 5.1 Steps

| # | Who | What happens | What is recorded |
|---|---|---|---|
| 1 | Admin (person) | Tells their AI client what should change, in plain words. Example: "Tier 2 go-live should need the data owner too" | Nothing yet |
| 2 | AI client (for example Claude Code) | Asks the product's MCP server for the active configuration and an explanation of the rule in question ("why does this need X?") | Read only |
| 3 | AI client, as the admin | Starts a draft and adds operations by key (upsert or retire), or submits a configuration file in the patch format | C46 Configuration Draft (draft), performed by "AI client for a person", client app, on behalf of the admin |
| 4 | Engine | Checks the caller holds a role with Can Change Configuration through an Entra group; validates the draft (section 9); computes the preview, diff, hash and summary, and the live applications whose rules would change | C46 validationJson, changeSummary |
| 5 | AI client | Fixes errors and repeats step 3 until the draft validates; then submits it with a rationale (`submit_record`, subject kind configuration draft) | proposalRationale; C46 submitted (frozen) |
| 6 | Engine | Records a configuration change (or gate policy change) decision in Proposed, creates approval records from the active version's rules, and the approval runner sends the cards | O09 Decision, O10 Approval Records |
| 7 | Approver (person) | Reads the engine's diff and warnings on the card, approves or rejects with a typed reason | Outcome written by the approval runner, as the approval writer, only |
| 8 | Engine | When the rules are met: rebases if needed (section 4.5), revalidates inside the activation transaction, writes the changed live rows, a new C02 row with the snapshot, and the new version number. The previous version becomes superseded with no write to its row | C02 created; C46 activated; O24 Audit Entry "configuration activated" |
| 9 | Engine everywhere | The next request in every process sees the new version number and loads the new snapshot (section 10.2) | New operational rows stamp the new version |

### 5.2 What an AI client may and may not do

| May | May not |
|---|---|
| Read the active configuration, any version, any diff | Approve, accept or activate anything (no such tool exists) |
| Explain which rule group applied to a record and why | Write configuration rows, C02 or C46 directly |
| Create, edit, validate, submit and withdraw drafts as the admin | Change a locked setting, or any rule that makes an AI an approver |
| Draft a configuration file in the patch format | Supply its own diff as the truth: the engine computes the diff |
| Propose a revert to an earlier version | Hold any decision right letter other than supports |

The tools come from the MCP surface document (document 24; 28, issue 11): `get_configuration` reads; `explain_rules` returns the engine's rule trace; `propose_configuration_change` creates, edits and validates drafts, and with validate-only returns the diff and the effect on live applications; `submit_record` (subject kind configuration draft) submits a draft; `withdraw_record` withdraws a draft (configuration subject kind); `get_configuration_health` shows version history, waiting drafts and engine information; `export_configuration` returns a version as seed files. The product's MCP server keeps no draft in its own memory: the draft lives in the store on C46, so the server can stay stateless as the MCP specification of 2026-07-28 expects (sweep proposal, not re-checked here).

### 5.3 The configuration file route

D-10 allows changes "maybe of a config file so no need to rebuild and redeploy anything". The file is a patch in the format of section 8.2, kept wherever the admin's AI works (for example a private repository). Submitting the file creates a draft exactly as in step 3. The file is never the live copy; the store is. A client that wants its configuration in source control exports the active snapshot as a bundle after each activation.

### 5.4 Safety floors

These are engine contract and cannot be configured away, also not by a preset:

1. AI never approves (locked setting; only an approved outcome from a person counts).
2. Every decision is accepted on a card answered by a person. The only exceptions are the decisions the locked setting `engine-acceptable-decision-types` lists (working default, open design question 8) and the bootstrap of version 1 (section 4.8).
3. Every configuration change is accepted by at least one person holding a role with Can Change Configuration, except the bootstrap of version 1.
4. Activation happens only through an accepted decision; there is no activate tool or button. No person, role or tool writes configuration tables directly.
5. Protected changes are judged by the active rules (section 4.4).
6. Rows in use are retired, never deleted; keys are never reused.
7. The engine refuses governed writes when it cannot load a valid active version, or when a rule row it needs is missing. There is no built-in fallback (section 10.4; 28, issue 14).

### 5.5 Fast changes when a gate blocks work

When an Enforce gate stops releases because a rule is wrong, the fix is still a configuration change. It is fast because the steps are few: the admin's AI drafts "lower tier 3 production to flag for 24 hours", the cards go to the approvers the gate policy change rules name (in Balanced, a configuration admin, with the change manager consulted), and the change is accepted when the configuration admin approves. The change and the reason stay on record. The emergency path (section 7.5) covers the deployment itself.

---

## 6. Presets

### 6.1 Kinds

| Kind | What it is | How it is applied |
|---|---|---|
| **Full preset** | A complete configuration set. It may be written as changes to another full preset (`extends`), but the build resolves it into one flat bundle | Becomes the starting content of a working set |
| **Overlay** | A partial set: a patch that changes some rows of a full preset by key. It covers one area, such as the people model | Applied after the full preset, in the order the client chooses |

### 6.2 Shipped presets

| Preset key | Kind | Areas | In one line | Status |
|---|---|---|---|---|
| `balanced` | full | all | The default three-tier preset. Flag by default, Advise for tier 2 in production, Enforce for tier 3 in production (the D-03 change gate options) | Canonical: the others are written as changes to it |
| `starter` | full, extends `balanced` | gates, people | Record everywhere, Flag for production; self-approval at tier 3 only through a recorded exception decision | D-03 gate option |
| `regulated` | full, extends `balanced` | gates, rules, people | Enforce for production from tier 2 up, an audited emergency path, a quarterly configuration review; no self-approval at any tier, and a second configuration admin | D-03 gate option |
| `microsoft-coe-baseline` | full, extends `balanced` | taxonomy, people, rules, gates, documentation | Tiers and controls one to one with Microsoft's agent risk tiers and sample RACI [S2, T1]. Ships in the first release and is re-checked against Microsoft's guidance each release (28, Q6) | Sweep proposal, shipped by working default |
| `small-team` | overlay | people | One group may carry several roles; no change board; self-approval at the top tier needs a recorded exception | D-20 "small team" |
| `strict-separation` | overlay | people | Different people for proposer and approver, instructing and accepting admin, AI lead and platform admin; two board members for a change exception | D-20 "strict separation" |
| `linked-change-management` | overlay | change management | Sets the change management mode to linked (D-08) | Example of a one-row overlay |

"Default" in this document means Balanced is the source the other presets are written from. Which full preset installs first is part of D-03; the change gate options suggest Starter. The three gate variants differ only in gate rows, separation rules and one setting, listed below. A client picks one full preset plus declared overlays, and the result is validated as one set (28, Q3).

### 6.3 The default three-tier preset (Balanced, with Starter and Regulated)

What all three share (the full rows are in the example, section 12):

| Area | Default content |
|---|---|
| Tiers | Three, ranked 1 to 3, each mapped to three Microsoft vocabularies: support tiers [S4], Copilot Studio zones [S5], agent risk tiers [S2]. No Microsoft page joins the three (an absence, so UNVERIFIED) |
| Tier questions | Five: data change, sensitivity, reach, agent autonomy (agents only), failure impact, aligned with Microsoft's three agent risk tiers [S2]. The most serious answer wins |
| Stages and gates | Stages 0 to 10; gates Accept, Commit, Design, Go-live, Value (document 20, C12 and C13) |
| Decision types | The sixteen of document 20, C18, each with outcomes. Six need no approval rule (intake, priority, design, tier, ownership, portfolio); each still goes on one card to its accountable holder, except what the locked list allows (28, issue 1) |
| Change management | Built in (D-08). Standard (needs a standard change model), normal, emergency (authorization may follow, post-change review due in 5 working days) |
| People | Seven parties, eight owner roles, fourteen human roles including the AI lead (D-20), a security reviewer and a reader, plus two workload roles (deployment recorder, approval writer) held only through workload grants (28, issue 7) |
| Decision rights | One accountable per decision type and scope, shaped on Microsoft's adoption RACI (https://learn.microsoft.com/power-platform/guidance/adoption/roles) and agent CoE RACI [T1], with "supports" rows for AI drafting. The configuration admin is the one accountable for both configuration decision types; the change manager (gate policy), the AI lead (AI rules) and the platform admin (roles and bindings) are consulted (28, issue 6) |
| Approval rules | Business owner by default; the change manager at tier 3 and for emergencies, with its own group for a tier 3 emergency; the change board for exceptions about a change; business owner then security reviewer for tier 3 go-live; a configuration admin for configuration changes and for gate policy changes, with the change manager consulted on gate policy changes through a consultation card that never counts (28, issues 2, 4 and 6; Q11) |
| Separation | Balanced: nobody approves their own proposal at tier 3 (refuse); the deployer is not the only approver of a production change (flag). Starter and Regulated change the first rule (below) |
| Documentation | A wiki page for every application from build; a runbook from release at tiers 2 and 3; an architecture decision record at tier 3 from design; a support guide at tier 3 from operate (D-17) |
| Reviews | Owner check yearly, twice a year, quarterly by tier; tier check and benefit review yearly; access check by tier |

How Starter and Regulated differ from Balanced:

| Preset | Gate Policy rows changed | Other rows changed |
|---|---|---|
| Starter | Test: record. Production: flag at every tier. Emergencies: record in test, flag in production | Separation rule `no-self-approval-tier-3`: enforcement exception decision, exception type `exception` (28, Q10) |
| Regulated | Production: tier 2 and tier 3 enforce (tier 2 fail open, tier 3 fail closed). Emergency in production: enforce with the emergency path. AI-only code approvals: stop at enforce | `configuration-review-days` 90 (a quarterly policy review); a go-live group at tier 2 with two rows, business owner and data owner (the group replaces the broader one, section 10.3); separation rules `no-self-approval-all` (refuse, every tier, replacing `no-self-approval-tier-3`) and `config-accepter-differs` with its twin for gate policy changes (refuse), as in section 6.6 (28, Q10) |

The change board's rows are scoped by change type on the decision type `exception`, one row for each shipped change type. An exception about a change request (for example a deployment outside its window, or a self-approval exception on a change) therefore goes to the board, while an exception to a standard about an application stays with the architect. A window that covers several applications has no scope field of its own in C32. Until one exists, the change manager raises such a change to the board as an exception about the change (28, issue 4).

### 6.4 Microsoft CoE baseline (sweep proposal)

Purpose: a CoE that starts from Microsoft's agentic CoE guidance can adopt the product without remapping. Every row below is a configuration row; Microsoft's words are quoted from the Learn pages checked today [S2, T1]. Microsoft's tiers are written for agents; the preset applies them to every application kind and scopes the agent-only controls to the kinds `agent` and `ai-built-app`.

| Microsoft tier and control [S2] | Configuration rows in the preset |
|---|---|
| **"Tier 1: Low risk (individual productivity agents)"** | Tier `tier-1`, label "Tier 1: Low risk"; vocabulary mapping to the agent risk tier term |
| "A named owner who is accountable for the agent" | Gate requirement on Accept, every tier: owner assigned, `business-owner` |
| "Basic monitoring of usage and errors" | Document type `monitoring-plan` (a link to the monitoring set up in Microsoft's tools); required from Operate at tier 1 |
| "A standard release checklist" | Document type `release-checklist`, required from Change and release at tier 1 (checklists as entities come later, document 20 L02) |
| "Self-service deployment within published guardrails" | Gate policy tier 1: record in test and production; change authorization by the business owner alone |
| **"Tier 2: Medium risk (expert-knowledge and internal service agents)"** | Tier `tier-2`, label "Tier 2: Medium risk" |
| "A named owner, plus a domain-expert validator" | Owner role `domain-validator` (new seed row); go-live approval rule at tier 2 adds it |
| "Knowledge-quality monitoring that watches for stale or wrong content" | Document type `quality-monitoring-plan`, required from Operate at tier 2 |
| "A formal release gate with review before the agent goes live" | Review type `release-review`; gate requirement on Go-live at tier 2: review done. Gate policy tier 2 production: advise |
| "Accuracy tracking and feedback loops" | Review type `quality-review`, cadence 180 days at tier 2 |
| **"Tier 3: High risk (business-critical and external-facing agents)"** | Tier `tier-3`, label "Tier 3: High risk" |
| "A named owner, plus a formal process owner" | Gate requirement on Accept at tier 3: owner assigned, `process-owner` |
| "Production-grade service-level-agreement (SLA) monitoring" | Document type `sla-monitoring-plan`, required from Operate at tier 3 |
| "A security review and a responsible AI assessment before release" | Review types `security-review` and `responsible-ai-assessment`; gate requirements on Go-live at tier 3: review done (the second scoped to `agent` and `ai-built-app`) |
| "A decision-rights framework that sets what the agent may decide alone and what needs a human" | Gate requirement on Go-live at tier 3, kind `agent`: decision accepted, `autonomy-level` |
| "An incident-response plan" | Document type `incident-response-plan`, required from Change and release at tier 3 |
| "A quarterly maturity review" | Review type `maturity-review`, cadence 90 days at tier 3 |
| Front door: "Treat the recommendation as an initial assessment, not approval" | Intake decision right: AI holds supports only |

Roles and decision rights, from Microsoft's sample RACI ("a starting point, not a standard") and decision rights split [T1]:

| Microsoft role [T1] | Row in the preset |
|---|---|
| Executive sponsor | Owner role `sponsor` |
| Business owner | Owner role `business-owner` |
| CoE lead | Role `coe-lead` (new seed row) |
| Agent product owner | Owner role `product-owner` (new seed row) |
| Platform and operations | Role `platform-admin` |
| Security and risk | Role `security-reviewer` |
| Makers | Role `maker` |

| Microsoft RACI task [T1] | Decision rights in the preset |
|---|---|
| "Review and approve an agent for release": Security and risk A, CoE lead R | `go-live`: accountable `security-reviewer`, responsible `coe-lead`, consulted `product-owner` and `platform-admin`, informed `business-owner` |
| "Retire an agent": Business owner A, Agent product owner R | `retirement`: accountable `business-owner`, responsible `product-owner` |
| "Conduct risk and responsible AI review": Security and risk A | `risk-acceptance`: accountable `security-reviewer`, responsible `product-owner` |
| CoE owns "Risk tiers and the criteria that assign them", "Autonomy limits for agents", "Release-readiness gates" | `autonomy-level`: accountable `coe-lead`, the AI lead (D-20) consulted. `configuration-change` and `gate-policy-change`: accountable `configuration-admin`, as in every preset, with `coe-lead` consulted; a CoE can bind both roles to its CoE lead group (28, issue 6) |
| Domains own "Day-to-day operation of lower-risk agents" | `change-authorization` at tier 1: accountable `business-owner` |

Gate policy in this preset: tier 1 record (test and production); tier 2 flag in test, advise in production; tier 3 flag in test, enforce in production (fail closed); emergencies flag in production with the emergency rule. Each role and term mapping is also a Vocabulary Mapping row (vocabularies `agent-risk-tier` and `agent-coe-role`) with the Learn URL and date.

Setup notes (not engine configuration; sweep proposal, not re-checked here): point the configuration service to Microsoft controls instead of rebuilding them, such as the Work IQ tenant ceiling on agent actions (preview) and the control for sharing agents with embedded maker credentials (preview; https://learn.microsoft.com/power-platform/admin/security/control-sharing-agents-with-embedded-maker-credentials).

### 6.5 Small team overlay

For a team too small to separate duties (D-20 "map it to the same Entra group as the platform owner").

| Operation | Effect |
|---|---|
| Upsert separation rule `no-self-approval-tier-3`: enforcement exception decision, exception type `exception` | A tier 3 proposer may approve their own decision only with an accepted exception decision on record. Separation returns when a second person exists (I-206; 28, Q10) |
| Upsert approval rules `change-exception-standard`, `change-exception-normal` and `change-exception-emergency`: approver role `change-manager`; upsert the matching decision rights (`change-exception-standard-a`, `-normal-a`, `-emergency-a`): role `change-manager` | No change board is needed; the change manager also decides exceptions about a change. Tier 3 changes and emergencies already go to the change manager in Balanced (28, Q11) |
| Upsert separation rule `deployer-not-sole-approver`: enforcement flag | Unchanged level, stated explicitly |
| Setup note | One Entra group may carry `platform-admin`, `ai-lead`, `configuration-admin` and `change-manager`. Allowed because no roles-in-different-groups rule names them |

Conflicts with `strict-separation`. **Proposed by the build (2026-11-22):** Regulated also lists `small-team` as a conflict (B21-3 at the end of this document).

### 6.6 Strict separation overlay

| Operation | Effect |
|---|---|
| Upsert separation rule `no-self-approval-all`: approver differs from proposer, every decision type, refuse; retire `no-self-approval-tier-3` | Nobody approves their own proposal at any tier (28, Q10) |
| Upsert separation rule `config-accepter-differs`: decision type `configuration-change` (and a twin for `gate-policy-change`), differs from instructing admin, refuse | A second configuration admin accepts every configuration change (working default, open design question 4) |
| Upsert separation rule `ai-lead-own-group`: roles in different groups, `ai-lead` and `platform-admin`, refuse | D-20 "require a different person": the bindings must use different groups (V36) |
| Upsert approval rules `change-exception-standard`, `change-exception-normal` and `change-exception-emergency`: minimum count 2 | Two change board members decide an exception about a change. The engine counts approved responses until two arrive; whether one Power Automate approval can wait for exactly two of many approvers is UNVERIFIED, so the approval runner may send one card per member and the engine counts |
| Upsert separation rule `deployer-not-sole-approver`: refuse | At enforce, the check step stops a production deployment whose deployer is the only approver; at lower levels it is flagged |

Conflicts with `small-team`.

### 6.7 Selecting and mixing

1. **Pick one full preset** and any overlays, in order. Overlays that conflict (manifest `conflictsWith`) cannot be combined (V43).
2. **The engine builds the content:** the full preset's flat bundle, then each overlay's operations in order. A later operation on the same entity and key wins over an earlier one.
3. **The engine validates the result** as one set (section 9). An overlay that references a key its base lacks fails validation; nothing is applied halfway.
4. **The result becomes version 1** of the working set (at install), or a draft (when a client changes preset later). appliedPresetsJson records the preset keys and content versions.
5. **From then on the working set is the client's.** Presets stay read-only templates. Re-applying a preset later is a large change proposal: the diff shows every row that would change, and rows with origin local stay unless the admin's patch replaces them.

Example: a finance-heavy client picks `regulated` plus `strict-separation`. A ten-person team picks `starter` plus `small-team`. A CoE that follows Microsoft's guide picks `microsoft-coe-baseline` alone.

### 6.8 How the configuration service uses presets

The configuration service (the people who set up a client's configuration, D-07) picks the preset and overlays with the client, runs the apply as the client's admin, then delivers a **client binding patch**. When those people come from outside the client, they work as guests in a group that is PIM-eligible for the configuration admin role; each activation needs the client's approval and is time-bound. For a client without Entra ID P2, the client's own staff run the tools while the outside team advises (28, Q17). The binding patch holds one Role Group Binding per human role, pointing to the client's Entra groups (which the service creates, D-11), plus any portfolio-scoped rows. Workload roles get Workload Grants (C42, document 26) instead (working default, open design question 12). The binding patch carries client facts, so it lives in the client's own private repository or nowhere, never in the public presets (V44).

---

## 7. The gate ladder as configuration

### 7.1 One row per tier, change type and target stage

- The ladder levels (off, record, flag, advise, enforce) are engine contract: what each level does is fixed in the engine (section 7.2).
- **Which level applies where is configuration:** one Gate Policy row (C33) per combination of active tier, active change type and active target stage, optionally per portfolio.
- **Coverage is complete by rule:** every combination resolves to exactly one row (V23). Adding a tier or a change type therefore forces the same draft to add its gate rows; the validator names the missing combinations.
- **Seeds may use wildcards** to stay short; the loader expands them into explicit rows (section 8.4). The stored rows and the snapshot are always explicit, so an agent can read the level for any combination directly.
- **Changing a gate row is a gate policy change decision** (section 4.2), never a quiet edit (D-03).

### 7.2 What each level does

| Gate level | Approvals asked | Deployment record flags computed | Flag message sent (flagNotify) | Check step answer before deploy | Can stop a deployment |
|---|---|---|---|---|---|
| **off** | None | Outside window and other path only (facts); never missing approval | No | "Not governed" | No |
| **record** | As the approval rules say | All three | No ("nobody is told") | "Record" | No |
| **flag** | As the approval rules say | All three | Yes, to the owner roles and roles in flagNotify | "Record" | No |
| **advise** | As the approval rules say | All three | Yes | "Warn", with the missing approvals and flags | No |
| **enforce** | As the approval rules say | All three | Yes | "Stop" unless approvals are complete or the emergency rule applies | Yes, through the client's own check step |

No level stops a manual import: it can only be flagged when it is recorded (D-03). Advise and enforce need a check step in the client's pipeline (D-19 samples, after D-03).

**Proposed by the build (2026-11-22).** Off stores no flag at all, as documents 23 and 25 say (B21-6 at the end of this document).

### 7.3 How it meets approval rules, change types and change paths

- **Approval rules** decide who must approve the change authorization decision; the gate level decides what happens when they have not. At off, no change authorization approvals are required for that combination (V21 skips it).
- **Change types** decide whether authorization is needed at all (Requires Authorization), whether a standard change model replaces it (Requires Standard Model) and whether it may follow (Allows Authorization After). For a standard change, the engine records the change authorization itself and links the model's Approved By Decision, which the locked list allows (working default, open design question 8).
- **Change paths** (C35) on each deployment target set the other-path flag: a record whose route is not in the target's allowed routes.
- **Windows** (D-13) set the outside-window flag from the change request's window.
- **AI-only code approvals** (sweep proposal): when a record step reports that the only code approval came from an AI, the row's aiOnlyCodeApprovalHandling decides: ignore, flag, or stop (only at enforce). The shipped default is flag, and such an approval never counts as authorization (working default, open design question 7). The product's own approvals stay person-only in every case.

### 7.4 Fail mode and the check step

Fail mode applies only at enforce and only when the product cannot answer (D-03, open). A product that is down cannot tell the check step what to do, so the design point is: **every check step answer carries the fail mode of its row, and the step sample keeps the last value it received for that target.** When no answer comes, the step applies that value, or fails closed if it has none. The check step document (after D-03) designs the sample.

### 7.5 Emergency rule

Emergency Rule Json on a Gate Policy row, for change types that allow authorization after:

| Field | Type | Description |
|---|---|---|
| passWithoutApproval | bool | True when an emergency change may deploy before its approval |
| approvalDueWorkingDays | int | Working days within which the change authorization must be accepted afterwards |
| approverRoleKeys | keys(role) | Optional: who may authorize after the fact, when it differs from the approval rules |
| reviewTypeKey | key(review-type) | The review created after the change, normally `post-change` |

At enforce, an emergency that uses the path deploys, records the exception, and stays flagged until its approval and review are done.

### 7.6 Preset matrix (production column; development is off in every preset)

| Tier | Starter | Balanced | Regulated | Microsoft CoE baseline (sweep proposal) |
|---|---|---|---|---|
| Tier 1, normal and standard | flag | flag | flag | record |
| Tier 2, normal and standard | flag | advise | enforce, fail open | advise |
| Tier 3, normal and standard | flag | enforce, fail closed | enforce, fail closed | enforce, fail closed |
| Any tier, emergency | flag | flag, emergency rule | enforce, emergency rule | flag, emergency rule |
| Test target, any tier | record | flag (emergency: record) | flag | tier 1 record, tiers 2 and 3 flag |

All values are presets, not decisions (D-03 is open). Fail closed for tier 3 follows the suggestion made with the D-03 change gate options.

---

## 8. The seed file format

### 8.1 Files and folders

| Path (public repo) | What it holds |
|---|---|
| `config/schema/config-bundle.schema.json` | The JSON Schema for bundles and patches (section 8.5) |
| `config/presets/<preset-key>/preset.json` | The manifest (C03 fields) |
| `config/presets/<preset-key>/<entity-key>.json` | One file per entity: `{ "entity": "<entity-key>", "rows": [ ... ] }` (document 20, section 3.6) |
| `config/presets/<preset-key>/patch.json` | For a preset written with `extends`, or an overlay: the operations |
| `config/build/<preset-key>.bundle.json` | Built by the repo's build: the flat, validated bundle that the package ships |

Authors edit the small per-entity files; the build resolves `extends`, expands wildcards, runs the full validator (I-204) and writes one bundle per preset. Only bundles ship.

### 8.2 Bundle and patch

**Bundle** (a whole set: a built preset, a snapshot in contentJson, an export):

```json
{
  "schemaVersion": "1.0",
  "manifest": { "kind": "bundle", "presetKey": "balanced", "presetKind": "full", "...": "C03 fields" },
  "entities": {
    "tier": [ { "key": "tier-1", "label": "Tier 1", "rank": 1 } ],
    "...": "one array per entity key"
  }
}
```

**Patch** (an overlay, a preset with `extends`, an AI draft, a configuration file, a client binding patch):

```json
{
  "schemaVersion": "1.0",
  "manifest": { "kind": "patch", "baseVersionNumber": 12, "summary": "Tier 2 go-live also needs the data owner" },
  "operations": [
    { "op": "upsert", "entity": "approval-rule", "key": "go-live-tier-2-owner",
      "values": { "label": "Tier 2 go-live: business owner", "decisionTypeKey": "go-live",
                  "tierKey": "tier-2", "approverOwnerRoleKey": "business-owner",
                  "approvalMode": "everyone-must-approve" } },
    { "op": "upsert", "entity": "approval-rule", "key": "go-live-tier-2-data-owner",
      "values": { "label": "Tier 2 go-live: data owner", "decisionTypeKey": "go-live",
                  "tierKey": "tier-2", "approverOwnerRoleKey": "data-owner",
                  "approvalMode": "everyone-must-approve" } },
    { "op": "retire", "entity": "tier-question", "key": "failure-impact" }
  ]
}
```

The patch repeats the business owner, because the new tier 2 group replaces the broader go-live group at tier 2 (section 10.3).

- **upsert:** creates the row, or merges `values` into the existing row with that key. Fields not named keep their value. A new row must carry every required field.
- **retire:** sets isActive to false. There is no delete operation (rule 7).
- For child entities, the key is a path: `"key": "data-change/reads-only"` (question key, then answer key).
- A preset patch carries `presetKey`, `presetKind` and `extends` in its manifest; an AI draft carries `baseVersionNumber` instead.

### 8.3 Naming and references

| Item | Rule | Example |
|---|---|---|
| Entity keys (file names and `entities` properties) | kebab-case of the logical name | `approval-rule`, `tier-question` |
| Field names | lowerCamelCase of the logical column name (document 20, section 3.3) | `requiresTypedReason` |
| References to configuration rows | The target's key, in a field ending `Key` or `Keys` | `"tierKey": "tier-3"` |
| References to operational rows (working set only) | IDs, in a field ending `Id` | `"portfolioId": "..."` |
| Engine meaning values | kebab-case of the meaning | `"in-progress"`, `"recommendation-only"`, `"fail-closed"` |
| Entity names inside values | entity keys | `"appliesToEntity": "change-request"` |

### 8.4 Wildcards and extends

- **Wildcard `"*"`** is allowed only in the scope fields marked `"*"` in section 3 (C25 tierKey; C33 tierKey, changeTypeKey, targetStageKey).
- **Expansion:** the loader turns a wildcard row into one row per active target row, with key `<row key>--<target key>` (for several wildcards, the target keys joined in field order). Expanded keys must still pass V02, so wildcard rows keep short keys.
- **Explicit beats wildcard:** a combination given by a row with more explicit scope fields is not produced by a broader row. Two rows with the same number of explicit fields that produce the same combination are an error (V09); the author must add a row for that combination. The example in section 12 shows this with the emergency rows.
- **extends:** a full preset written as a patch over its parent. The build applies the parent bundle, then the patch, and ships the flat result. Chains are allowed but not cycles.

### 8.5 JSON Schema outline (draft 2020-12)

The schema checks shape only. Meaning (orphans, coverage, safety floors) is the validator's job, because JSON Schema cannot check that a key exists in another array.

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "config-bundle.schema.json",
  "title": "Configuration bundle or patch",
  "type": "object",
  "required": ["schemaVersion", "manifest"],
  "properties": {
    "schemaVersion": { "const": "1.0" },
    "manifest": { "$ref": "#/$defs/manifest" },
    "entities": { "$ref": "#/$defs/entities" },
    "operations": { "type": "array", "items": { "$ref": "#/$defs/operation" } }
  },
  "oneOf": [
    { "required": ["entities"], "properties": { "manifest": { "properties": { "kind": { "const": "bundle" } } } } },
    { "required": ["operations"], "properties": { "manifest": { "properties": { "kind": { "const": "patch" } } } } }
  ],
  "unevaluatedProperties": false,
  "$defs": {
    "key": { "type": "string", "pattern": "^[a-z][a-z0-9-]{0,63}$" },
    "childKeyPath": { "type": "string", "pattern": "^[a-z][a-z0-9-]{0,63}(/[a-z][a-z0-9-]{0,63})?$" },
    "keyOrAny": { "oneOf": [ { "$ref": "#/$defs/key" }, { "const": "*" } ] },
    "keys": { "type": "array", "items": { "$ref": "#/$defs/key" }, "uniqueItems": true },
    "manifest": {
      "type": "object",
      "required": ["kind"],
      "properties": {
        "kind": { "enum": ["bundle", "patch"] },
        "presetKey": { "$ref": "#/$defs/key" },
        "label": { "type": "string" },
        "description": { "type": "string" },
        "presetKind": { "enum": ["full", "overlay"] },
        "extends": { "oneOf": [ { "$ref": "#/$defs/key" }, { "type": "null" } ] },
        "conflictsWith": { "$ref": "#/$defs/keys" },
        "areas": { "type": "array", "items": { "enum": ["taxonomy", "people", "rules", "gates", "change-management", "documentation"] } },
        "productRelease": { "type": "string" },
        "contentVersion": { "type": "string", "pattern": "^[0-9]+\\.[0-9]+\\.[0-9]+$" },
        "checkedOn": { "type": "string", "format": "date" },
        "setupNotes": { "type": "array", "items": { "type": "object", "required": ["text"], "properties": { "text": { "type": "string" }, "url": { "type": "string", "format": "uri" } } } },
        "baseVersionNumber": { "type": "integer", "minimum": 1 },
        "summary": { "type": "string" }
      },
      "additionalProperties": false
    },
    "row": {
      "type": "object",
      "required": ["key", "label"],
      "properties": {
        "key": { "$ref": "#/$defs/key" },
        "label": { "type": "string", "minLength": 1 },
        "description": { "type": "string" },
        "sortOrder": { "type": "integer" },
        "isActive": { "type": "boolean" }
      }
    },
    "operation": {
      "type": "object",
      "required": ["op", "entity", "key"],
      "properties": {
        "op": { "enum": ["upsert", "retire"] },
        "entity": { "$ref": "#/$defs/entityKey" },
        "key": { "$ref": "#/$defs/childKeyPath" },
        "values": { "type": "object" }
      },
      "additionalProperties": false
    },
    "entityKey": { "enum": ["setting", "status", "value-list", "tier", "tier-question", "vocabulary-mapping", "lifecycle-stage", "gate", "gate-requirement", "application-kind", "component-kind", "dependency-type", "decision-type", "change-type", "standard-change-model", "document-type", "documentation-requirement", "review-type", "review-cadence", "reminder-rule", "party", "owner-role", "role", "role-group-binding", "decision-right", "approval-rule", "gate-policy", "target-stage", "change-path", "deployment-route", "link-kind", "actor-kind", "guideline", "separation-rule"] },
    "entities": {
      "type": "object",
      "propertyNames": { "$ref": "#/$defs/entityKey" },
      "properties": {
        "tier": { "type": "array", "items": { "$ref": "#/$defs/tier" } },
        "gate-policy": { "type": "array", "items": { "$ref": "#/$defs/gatePolicy" } },
        "approval-rule": { "type": "array", "items": { "$ref": "#/$defs/approvalRule" } }
      }
    },
    "tier": {
      "allOf": [ { "$ref": "#/$defs/row" } ],
      "required": ["rank"],
      "properties": { "rank": { "type": "integer" } },
      "unevaluatedProperties": false
    },
    "gatePolicy": {
      "allOf": [ { "$ref": "#/$defs/row" } ],
      "required": ["tierKey", "changeTypeKey", "targetStageKey", "gateLevel"],
      "properties": {
        "tierKey": { "$ref": "#/$defs/keyOrAny" },
        "changeTypeKey": { "$ref": "#/$defs/keyOrAny" },
        "targetStageKey": { "$ref": "#/$defs/keyOrAny" },
        "gateLevel": { "enum": ["off", "record", "flag", "advise", "enforce"] },
        "failMode": { "enum": ["fail-open", "fail-closed"] },
        "emergencyRule": { "$ref": "#/$defs/emergencyRule" },
        "aiOnlyCodeApprovalHandling": { "enum": ["ignore", "flag", "stop"] },
        "flagNotify": { "$ref": "#/$defs/notify" }
      },
      "if": { "properties": { "gateLevel": { "const": "enforce" } } },
      "then": { "required": ["failMode"] },
      "unevaluatedProperties": false
    },
    "approvalRule": {
      "allOf": [ { "$ref": "#/$defs/row" } ],
      "required": ["decisionTypeKey", "approvalMode"],
      "properties": {
        "decisionTypeKey": { "$ref": "#/$defs/key" },
        "tierKey": { "$ref": "#/$defs/key" },
        "lifecycleStageKey": { "$ref": "#/$defs/key" },
        "changeTypeKey": { "$ref": "#/$defs/key" },
        "applicationKindKey": { "$ref": "#/$defs/key" },
        "targetStageKey": { "$ref": "#/$defs/key" },
        "approverOwnerRoleKey": { "$ref": "#/$defs/key" },
        "approverRoleKey": { "$ref": "#/$defs/key" },
        "approvalMode": { "enum": ["everyone-must-approve", "first-to-respond", "sequential"] },
        "sequence": { "type": "integer", "minimum": 1 },
        "minimumCount": { "type": "integer", "minimum": 1 },
        "timeoutDays": { "type": "integer", "minimum": 0 },
        "requiresTypedReason": { "type": "boolean" },
        "isConsultation": { "type": "boolean" },
        "guidelineKey": { "$ref": "#/$defs/key" }
      },
      "oneOf": [ { "required": ["approverOwnerRoleKey"] }, { "required": ["approverRoleKey"] } ],
      "unevaluatedProperties": false
    },
    "emergencyRule": {
      "type": "object",
      "required": ["passWithoutApproval", "approvalDueWorkingDays"],
      "properties": {
        "passWithoutApproval": { "type": "boolean" },
        "approvalDueWorkingDays": { "type": "integer", "minimum": 0 },
        "approverRoleKeys": { "$ref": "#/$defs/keys" },
        "reviewTypeKey": { "$ref": "#/$defs/key" }
      },
      "additionalProperties": false
    },
    "notify": {
      "type": "object",
      "properties": { "ownerRoleKeys": { "$ref": "#/$defs/keys" }, "roleKeys": { "$ref": "#/$defs/keys" } },
      "additionalProperties": false
    }
  }
}
```

The other 31 entity definitions follow the same pattern from the field tables in section 3: `allOf` the common row, the R fields as `required`, references as `key` or `keys`, contracts as `enum`, and `unevaluatedProperties: false`. Child entities nest (`values`, `answerOptions`, `outcomes`). W and S fields are not allowed in a shipped preset; the client binding patch has its own small schema that allows C30, portfolio scopes and setting rows that narrow a setting for one portfolio.

---

## 9. Validation rules

The engine runs one validator everywhere: in the public repo's build (on every preset), on every draft edit, and again inside the activation transaction. An **error** blocks activation. A **warning** is shown on the approval card and must be acknowledged by the approver.

| ID | Group | Rule | Severity |
|---|---|---|---|
| V01 | Shape | The bundle or patch matches the JSON Schema: types, required fields, no unknown fields | Error |
| V02 | Keys | Every key matches `^[a-z][a-z0-9-]{0,63}$` | Error |
| V03 | Keys | Keys are unique per entity in the set; child keys are unique within their parent | Error |
| V04 | Keys | A new row never takes the key of a retired row of the same entity | Error |
| V05 | Keys | A patch never changes a row's key; renaming means changing the label | Error |
| V06 | References | No orphan references: every `...Key` and `...Keys` value resolves to a row of the named entity in the same set | Error |
| V07 | References | References point to active rows, unless the referencing row is itself retired | Error |
| V08 | References | A row referenced by any operational record or active configuration row can only be retired, never removed | Error |
| V09 | References | Wildcards only where allowed; after expansion, no combination comes from two rows of equal explicitness | Error |
| V10 | References | Exactly-one fields hold: a gate requirement sets the one target its kind needs; an approval rule names exactly one approver (owner role or role); a decision right names exactly one holder, or none for supports | Error |
| V11 | Taxonomy | At least one active tier | Error |
| V12 | Taxonomy | Tier ranks are unique whole numbers | Error |
| V13 | Taxonomy | Each active tier question has at least two active answer options, and every implied tier is active | Error |
| V14 | Taxonomy | At least one active lifecycle stage per lifecycle meaning (pre-live, live, retired); stage numbers and stage ranks unique | Error |
| V15 | Taxonomy | Each status-bearing entity has at least one active status per engine meaning in its contract, and exactly one default per meaning | Error |
| V16 | Taxonomy | The engine meaning of a status, stage or route that any operational record uses never changes; add a new row instead | Error |
| V17 | Taxonomy | At least one active change type, one target stage, one deployment route with meaning pipeline step, one actor kind per actor meaning, and one link kind per link category | Error |
| V18 | Taxonomy | Every active decision type has at least one active outcome | Error |
| V19 | Coverage | **Every tier has a person to ask:** for each decision type whose subject carries a tier, and for each active tier: with Requires Approval, at least one approval rule applies at that tier (a rule scoped to that tier, or one with no tier); without it, the resolved accountable holder (V22) is an owner role, or a role that is neither a workload role nor read-only, so the one card has a person to go to (28, issue 1) | Error |
| V20 | Coverage | Decision types that require approval and carry no tier (configuration change, gate policy change) have at least one approval rule. A decision type without Requires Approval has only consultation rules (Is Consultation), if any | Error |
| V21 | Coverage | For each active tier, each change type that requires authorization and each target stage whose gate level is record or higher, at least one change authorization rule group applies | Error |
| V22 | Coverage | For each decision type (and each active tier, where it carries a tier), the resolved decision right group holds exactly one accountable | Error |
| V23 | Coverage | Every combination of active tier, change type and target stage resolves to exactly one gate policy row | Error |
| V24 | Coverage | Fail mode is set at enforce (error if missing) and only at enforce (warning if set elsewhere) | Error, warning |
| V25 | Coverage | An emergency rule exists where the change type allows authorization after and the level is advise or enforce | Error |
| V26 | Coverage | No two matching rule groups of equal specificity for any reachable combination (approval rules, decision rights; separation rules per rule kind and subject, section 10.3) | Error |
| V27 | Coverage | Sequential approval rules in one group have unique sequence numbers, and every approval rule group of a decision type with Requires Approval has at least one rule that is not a consultation (a type without it may have consultation-only groups, V20) | Error |
| V28 | Values | Minimum count at least 1, with no zero waiver: the only way to accept without a card is the locked setting `engine-acceptable-decision-types` (28, issue 1); interval days above 0; lead, repeat, escalate, timeout and due days at least 0; `workload-grant-max-days` above 0, and every Workload Grant (C42) has an Expires On no later than that many days after the validation date (document 26, section 3.4) | Error |
| V29 | Coverage | A review type with a cadence at one tier has a cadence at every active tier | Warning |
| V30 | Safety floor | Approval rules name only roles that are neither workload roles nor read-only; no rule can make an AI or a pipeline an approver | Error |
| V31 | Safety floor | Supports rows name no holder; no AI-only construct holds accountable or responsible | Error |
| V32 | Safety floor | At least one active role with Can Change Configuration, and the configuration change decision type has an approval rule naming such a role | Error |
| V33 | Safety floor | Locked settings are unchanged: the AI approval lock stays on, and the engine acceptance list stays as shipped | Error |
| V34 | Safety floor | Protected changes are approved under the active version's rules (section 4.4) | Error at activation |
| V35 | Identity | Role group bindings point only to principal references of kind group (D-11), and never bind a workload role. Workload roles reach workload identities only through Workload Grants (C42; working default, open design question 12) | Error |
| V36 | Identity | Roles named together in a roles-in-different-groups separation rule bind to different groups | Error |
| V37 | Identity | Every human role used by an approval rule has at least one binding in the working set (not checked for presets) | Warning |
| V38 | Values | Every setting in section 3.6 exists exactly once without a portfolio, and every setting value matches its value type. A row with narrowsSettingKey narrows only `change-management-mode`, sets a portfolio and has the same value type (28, issue 15 and Q2) | Error |
| V39 | Values | JSON fields match their sub-schemas: emergency rule, conditions, triggers, flag notify, setting value | Error |
| V40 | Values | URL and ID patterns compile as regular expressions; source, hub and template URLs use https | Error |
| V41 | Platform | The snapshot fits the binding: on Dataverse, a multiline text column holds up to 1,048,576 characters [T3]; warning at 80 percent | Error, warning |
| V42 | Concurrency | The draft's base version equals the active version at activation, or the draft was rebased and revalidated (section 4.5) | Error |
| V43 | Presets | No two overlays that list each other in conflictsWith are applied together | Error |
| V44 | Presets | A shipped preset holds no secrets, no tenant identifiers (no GUID values, no tenant host names), no role group bindings and no portfolio scopes | Error (build) |

**Proposed by the build (2026-11-22).** Four more rules, V45 to V48, for the engine checks document 25 runs beyond V01 to V44, are in B21-1 at the end of this document.

Messages name the rule, the entity, the key and the fix in one sentence, so an AI client can act on them. Example: "V23: no gate policy for tier `tier-4`, change type `normal`, target stage `production`. Add a row or a wildcard row that covers it."

---

## 10. How the engine reads configuration

### 10.1 The compiled snapshot

- The engine reads the **active version's snapshot** (contentJson), never the live rows one by one. One read gives a consistent picture of every rule.
- On load, it compiles the snapshot into immutable lookup structures:
  - gate policy by (tier key, change type key, target stage key, portfolio);
  - approval rule groups and decision right groups by decision type, ordered by specificity;
  - statuses by entity and engine meaning;
  - tiers by rank; lifecycle stages by stage rank; keys to platform IDs for writing lookups;
  - separation rules by decision type, rule kind and subject;
  - settings, with any portfolio row that narrows one, and the engine acceptance list.
- The live rows exist for relationships (an application's tier is a lookup to a Tier row), for pages and views, and for people and agents who query them. Activation writes rows and snapshot together, so they agree.

### 10.2 Cache and version probe

| Step | What happens |
|---|---|
| 1. Probe | At the start of every governed request, the engine reads one value: the in-force working set's currentVersionNumber. A single small read by ID |
| 2. Hit | If it equals the cached version, the engine uses the cached compiled snapshot |
| 3. Miss | It loads that version's snapshot (one row), checks the hash, compiles it, and swaps the cache reference in one step. Requests already running keep the snapshot they started with |
| 4. Pin | The request uses that one snapshot from start to end and stamps its version number on every row it writes |
| 5. Invalidate | There is no push. Every process finds a new version on its next probe, so a change takes effect on the next request everywhere, with no rebuild or redeploy (D-10) |

- **Why probe every request:** enforcement must never act on an old rule after activation. The probe costs one small read. An environment switch may allow probing at most every N seconds for very busy installations; the default is every request (section 11).
- **Multiple processes:** each Dataverse sandbox worker and each Azure instance has its own cache. The probe makes them consistent without coordination.

### 10.3 Resolution: rule groups and specificity

This is the one rule for which approval rules and decision rights apply. 28, issue 2 makes it the rule for documents 20 (C32), 23 and 25 as well.

- A **rule group** is the set of rows of one entity with the same decision type and the same scope values. Several approval rules in one group are all required (for example business owner and security reviewer).
- **Specificity** is the number of scope fields set. For a record, the engine finds every group whose set scope fields all match, and picks the group with the highest specificity. Broader groups do not add to it: the most specific group replaces them. So a new group must repeat every approver it still wants (the example in section 8.2).
- **Combined cases get their own group in the seed.** Ties are refused at validation (V26), so the engine never needs a hidden tie-break. Example: an emergency rule (change type scoped) and a tier 3 rule (tier scoped) both match a tier 3 emergency change. The configuration must hold a group scoped to both, which the default preset does.
- **No matching approval rule group.** For a decision type without Requires Approval, the card goes to the holder of the accountable letter (C31), resolved by the same rule (28, issue 1).
- **Engine acceptance, never through a count.** Minimum Count is never 0 (V28). The engine accepts with no card only when the request fits an entry of the locked setting `engine-acceptable-decision-types` and the configuration asks no person there: lowest-tier intake when no counting rule group matches (intake has no Requires Approval in the shipped presets), and a change whose change type has Requires Standard Model and whose model's conditions hold (document 25, OP-02 rule 8 and OP-12). A client that wants a card for lowest-tier intake gives intake approval rules (V19) (working default, open design question 8).
- **Separation rules** are grouped by rule kind and subject (what must differ), not only by scope. Rules about different subjects all apply; for one subject, the most specific matching rule wins. So the tier 3 self-approval rule and the deployer rule both apply to a tier 3 production change.
- **Explain:** the engine can return, for any record, the version, the group chosen, the groups rejected and why. The MCP server exposes this as the read tool `explain_rules` (document 24; 28, issue 11).

### 10.4 Failure behavior

| Situation | Behavior |
|---|---|
| No valid active version (empty, hash mismatch, validation failure on load) | Governed writes are refused with a clear message naming the problem. Reads still work. The tool `get_configuration_health` reports the active version, its hash, waiting drafts and the last load error |
| A rule row the engine needs is missing, such as no gate policy row for a combination | Cannot happen after validation (V23). If a loaded snapshot still lacks the row, the engine refuses the governed write and reports a broken configuration. There is no built-in Flag or other fallback (rule 8; 28, issue 14) |
| Snapshot cannot be read (store unavailable) | The request fails; the engine never falls back to built-in defaults (rule 8) |
| A check step asks while the product is unavailable | The step applies its last known fail mode (section 7.4) |

### 10.5 Other readers

- **The product's MCP server** reads configuration through the engine's read API, cached by version the same way, to describe choices to AI clients (for example the list of active tiers). It never enforces; the engine does.
- **Pages** show labels from the live rows and never compute rules. Status is resolved by key and engine meaning, never by label (I-261). Numbers shown in several places are stored values with their rule version (I-266).
- **Check steps and record steps** send keys, never IDs, for configuration values, and get answers that name the version used.

---

## 11. Platform bindings

### 11.1 Dataverse binding

| Item | Design | Microsoft fact |
|---|---|---|
| Configuration tables | One table per entity C04 to C40 for the working set; C01 and C02 hold sets and versions; C46 holds drafts; presets are snapshot-only rows (section 4.1) | (design) |
| Versions and drafts | C02 is append-only: a plug-in refuses any update or delete. C46 is freeze on final: a plug-in refuses changes once a draft is submitted, except the engine's move to activated or discarded. C01's currentVersionNumber is the pointer (28, issue 3) | (design) |
| JSON columns | Multiline text columns with the Text format. The engine validates the JSON itself | The JSON format for multiline text is "Only allowed for non-SQL data provider" and the JSON text format is "Only in non-SQL stores like Audit" [T2]. Multiline text holds up to 1,048,576 characters [T3] |
| Stable keys | An alternate key on Key plus Configuration Set; for child entities Key plus parent plus set | Text and lookup columns are valid key columns; up to 10 keys per table [S15] |
| Engine contract values | Global choices, one per contract | (design, document 20 section 3.4) |
| Configurable lists | Lookups to configuration tables, never choice columns | D-06 |
| Who writes | No person, role or tool writes configuration tables, C02 or C46 directly (28, issue 3). The engine's draft operations write C46; only the activation custom API writes the live configuration rows, C02 and C01's version number. The engine runs as the caller, with an engine-nested marker, and never elevates (28, issue 9). A guard plug-in on these tables refuses any write without the marker of the right engine operation, including one by a system administrator. Which platform privileges the caller then needs is document 26's | ParentContext gives "the execution context from the parent pipeline operation" for internally generated requests [T10]. Whether it reliably identifies the activation API for every write is UNVERIFIED; the engine spike tests it |
| Who reads | Every product role reads configuration tables, so pages and views can show labels | A missing read or delete privilege on configuration tables is a known failure pattern in the field |
| Activation size | A typical change is a few rows, written in one transaction with the snapshot and version number. Large changes (install, upgrade, re-base) are staged by the installer in batches, then switched by one small activation | A message operation, including its synchronous plug-ins, must finish within 2 minutes; Microsoft advises keeping a plug-in under 2 seconds [T9] |
| Seed in the package | One JSON web resource per preset bundle in the managed solution. Read-only: it is the seed, never the live copy | Editing a managed component in place creates an unmanaged layer, which is blocked where "Block unmanaged customizations" is on [T5] |
| Applying the seed | Package Deployer custom code (or the setup service through the MCP server) calls the engine's apply operation after import. It creates version 1 or an upgrade draft | A Package Deployer package can hold solutions, Configuration Migration data files and "custom code that can run before, while, or after the package is deployed" [T6] |
| Not the Configuration Migration tool for configuration | It can "disable plug-ins before exporting data and then re-enable them", which would skip the engine's validation and versioning. It stays for demo data only | [T7] |
| Environment variables | At most two, both infrastructure: the probe interval (default 0, every request) and the product MCP server's base URL for links. Never a business rule | Data types include Decimal number, Text, JSON, Yes/No, Data source and Secret; values can be set at import; a value in a managed solution is an unmanaged record; canvas apps and flows may take up to an hour to see a changed value [T4]. Changing an environment variable is allowed when unmanaged customizations are blocked [T5] |
| Cache | A static, thread-safe, read-only reference to the compiled snapshot in the plug-in assembly, replaced whole on a version change | The platform caches plug-in instances for a time it manages and may reinitialize them; several threads can run one instance; "read-only, static, and constant members are inherently thread-safe" [T8] |
| History | C02 snapshots, plus Dataverse auditing on configuration tables | Audit records the calling user for impersonated calls [S16] |
| Roles | Configuration roles are Dataverse security roles carried by Entra group teams | Membership cached up to 8 hours [S7] |

### 11.2 Azure database binding (for example Azure SQL Database)

| Item | Design | Microsoft fact |
|---|---|---|
| Configuration tables | A `config` schema, one table per entity (document 20, section 3.5) | Identifiers up to 128 characters [S17] |
| JSON columns | Native `json` columns, with CHECK constraints where a shape rule is simple | The `json` type is GA in Azure SQL Database [S20]; it accepts only a JSON object or array, and constraints can be part of the column definition [T11]. With `nvarchar`, an `ISJSON` CHECK constraint rejects malformed text [T11] |
| Stable keys | Unique constraints on (ConfigurationSetId, Key), and (parent, Key) for child entities | (design) |
| Versions and drafts | `config.ConfigurationDraft` (C46) as an updatable ledger table, frozen by the API on submit; `config.ConfigurationSetVersion` (C02) as an append-only ledger table, written only at activation; `config.ConfigurationSet.CurrentVersionNumber` as the pointer. Superseded is derived: a row with a higher number exists (28, issue 3 and Q21) | Append-only ledger tables allow only inserts and cannot be reverted [S18] |
| History of rows | System-versioned temporal tables on the `config` entity tables | [S19] |
| Who writes | Only the API's managed identity has write permission on `config`, and it passes the person's ID in SESSION_CONTEXT for row-level security (28, Q20). No role's predicate allows a direct write to `config` tables; activation is one database transaction inside the engine (28, issue 3) | (design, D-26) |
| Schema migration | The release's schema is applied by a migration, for example a SQL database project published with SqlPackage. Tool choice belongs to the engine document | SqlPackage Publish "incrementally updates a database schema to match the schema of a source .dacpac file" [T12] |
| Seed loaded by the migration | A post-deployment script stores each preset bundle of the release in `config.SeedPackage` (preset key, release, content, hash), written so it can run again safely | A post-deployment script "is executed after the deployment plan completes" and runs every time the project is deployed [T12] |
| Seed applied by the engine | The deployment pipeline then runs the engine's apply command (the same library the API uses) with the workload identity, or the first admin runs it through the MCP server. It creates version 1 or an upgrade draft. T-SQL never writes configuration rows directly, so validation and versioning stay in one place | (design) |
| App settings | The same two infrastructure switches as on Dataverse, as app settings of the one service that hosts the API, the MCP server and the engine: an Azure Functions app (Flex Consumption, .NET 10), with Container Apps as the fallback (28, issue 13) | (design) |
| Cache | Each instance of that service holds the compiled snapshot in memory and probes CurrentVersionNumber per request | (design) |
| Roles | Entra app roles assigned to groups, read from the token | Group assignment needs Entra ID P1 or P2; nested groups not supported [S9] |

---

## 12. Example: the default preset seed (Balanced)

A complete, valid bundle for the default preset, kept small. Every entity is present, and the bundle passes every rule in section 9. Where the full default ships more rows of the same shape (guidelines G1 to G19, more component kinds and link kinds), the example carries a few. Role group bindings are empty on purpose (V44), and Workload Grants are client facts that no preset holds. The seed follows the working defaults of `28-working-defaults.md`: the change manager for tier 3 and emergencies, the board for exceptions about a change, one role list, one settings list and the locked engine acceptance list. The shipped file is JSON; rows are written one per line to keep it short.

```json
{
  "schemaVersion": "1.0",
  "manifest": {
    "kind": "bundle", "presetKey": "balanced", "label": "Balanced",
    "description": "The default three-tier preset. Flag by default, Advise for tier 2 in production, Enforce for tier 3 in production.",
    "presetKind": "full", "extends": null, "conflictsWith": [],
    "areas": ["taxonomy", "people", "rules", "gates", "change-management", "documentation"],
    "productRelease": "0.1.0", "contentVersion": "1.0.0", "checkedOn": "2026-11-22", "setupNotes": []
  },
  "entities": {
    "setting": [
      { "key": "change-management-mode", "label": "Change management mode", "valueType": "contract:change-management-mode", "value": "built-in" },
      { "key": "ai-approval-lock", "label": "AI never approves", "valueType": "yes-no", "value": true, "isLocked": true },
      { "key": "engine-acceptable-decision-types", "label": "Decisions the engine may accept without a card", "valueType": "json", "value": [ { "decisionTypeKey": "intake", "condition": "lowest-tier" }, { "decisionTypeKey": "change-authorization", "condition": "standard-model" } ], "isLocked": true, "description": "Working default, open design question 8. Extending the list needs a product release." },
      { "key": "reminder-channel", "label": "Reminder channel", "valueType": "key:list-value:channel", "value": "teams" },
      { "key": "guideline-hub-base-url", "label": "Guideline hub base URL", "valueType": "text", "value": null, "description": "Set during setup; the public hub location is still open." },
      { "key": "draft-expiry-days", "label": "Days before an unsubmitted draft is discarded", "valueType": "number", "value": 30 },
      { "key": "configuration-review-days", "label": "Days between reviews of the whole configuration", "valueType": "number", "value": 180 },
      { "key": "default-flag-notify", "label": "Who hears about a flag", "valueType": "json", "value": { "ownerRoleKeys": ["business-owner", "technical-owner"], "roleKeys": ["change-manager"] } },
      { "key": "default-ai-only-code-approval-handling", "label": "When the only code approval came from an AI", "valueType": "contract:ai-only-code-approval-handling", "value": "flag" },
      { "key": "confirmation-channel", "label": "What counts as a person's act for an AI draft", "valueType": "json", "value": { "decision": "approval-card", "application-registration": "confirmation-card", "gate-document-current": "confirmation-card", "intake-or-change-submission": "same-call", "links-deployments-checks": "none" }, "description": "Working default, open design question 9." },
      { "key": "unlinked-deployment-change-type", "label": "Change type for a deployment with no change request", "valueType": "key:change-type", "value": "normal" },
      { "key": "intake-visibility", "label": "Who reads intake requests", "valueType": "contract:intake-visibility", "value": "own" },
      { "key": "claim-visibility", "label": "Who reads gap check claims", "valueType": "contract:claim-visibility", "value": "admins-and-claimant" },
      { "key": "record-access-mode", "label": "Read scope of the register", "valueType": "contract:record-access-mode", "value": "open-read" },
      { "key": "agent-read-access", "label": "Agent identities may read", "valueType": "yes-no", "value": false },
      { "key": "principal-refresh-days", "label": "Days between principal refreshes from Entra", "valueType": "number", "value": 7 },
      { "key": "forbidden-url-patterns", "label": "URL parts a link may not carry", "valueType": "json", "value": ["[?&]sig=", "[?&]token=", "[?&]code=", "^[a-z]+://[^/]*@"] },
      { "key": "audit-retention-days", "label": "Days to keep audit and history", "valueType": "number", "value": null, "description": "Null keeps them forever, the Dataverse default. On Azure, set from the client's retention policy. Ledger tables ignore it." },
      { "key": "link-undo-minutes", "label": "Minutes in which a link's creator may delete it", "valueType": "number", "value": 30 },
      { "key": "workload-grant-max-days", "label": "Longest a workload grant may run, in days", "valueType": "number", "value": 365 },
      { "key": "impact-max-depth", "label": "Deepest dependency walk", "valueType": "number", "value": 5 },
      { "key": "mcp-page-size-max", "label": "Largest page a list tool returns", "valueType": "number", "value": 100 },
      { "key": "engine-read-cache-seconds", "label": "Seconds to cache read operations", "valueType": "number", "value": 0 },
      { "key": "working-days", "label": "Working days", "valueType": "json", "value": ["monday", "tuesday", "wednesday", "thursday", "friday"] }
    ],
    "status": [
      { "key": "decision-proposed", "label": "Proposed", "appliesToEntity": "decision", "engineMeaning": "proposed" },
      { "key": "decision-accepted", "label": "Accepted", "appliesToEntity": "decision", "engineMeaning": "accepted" },
      { "key": "decision-rejected", "label": "Rejected", "appliesToEntity": "decision", "engineMeaning": "rejected" },
      { "key": "decision-superseded", "label": "Superseded", "appliesToEntity": "decision", "engineMeaning": "superseded" },
      { "key": "decision-withdrawn", "label": "Withdrawn", "appliesToEntity": "decision", "engineMeaning": "withdrawn" },
      { "key": "intake-draft", "label": "Draft", "appliesToEntity": "intake-request", "engineMeaning": "draft" },
      { "key": "intake-submitted", "label": "Submitted", "appliesToEntity": "intake-request", "engineMeaning": "submitted" },
      { "key": "intake-in-progress", "label": "In progress", "appliesToEntity": "intake-request", "engineMeaning": "in-progress" },
      { "key": "intake-completed", "label": "Completed", "appliesToEntity": "intake-request", "engineMeaning": "completed" },
      { "key": "intake-rejected", "label": "Declined", "appliesToEntity": "intake-request", "engineMeaning": "rejected" },
      { "key": "intake-withdrawn", "label": "Withdrawn", "appliesToEntity": "intake-request", "engineMeaning": "withdrawn" },
      { "key": "intake-cancelled", "label": "Cancelled", "appliesToEntity": "intake-request", "engineMeaning": "cancelled" },
      { "key": "change-draft", "label": "Draft", "appliesToEntity": "change-request", "engineMeaning": "draft" },
      { "key": "change-submitted", "label": "Submitted", "appliesToEntity": "change-request", "engineMeaning": "submitted" },
      { "key": "change-in-progress", "label": "Scheduled", "appliesToEntity": "change-request", "engineMeaning": "in-progress" },
      { "key": "change-completed", "label": "Closed", "appliesToEntity": "change-request", "engineMeaning": "completed" },
      { "key": "change-rejected", "label": "Rejected", "appliesToEntity": "change-request", "engineMeaning": "rejected" },
      { "key": "change-withdrawn", "label": "Withdrawn", "appliesToEntity": "change-request", "engineMeaning": "withdrawn" },
      { "key": "change-cancelled", "label": "Cancelled", "appliesToEntity": "change-request", "engineMeaning": "cancelled" },
      { "key": "app-draft", "label": "Draft", "appliesToEntity": "business-application", "engineMeaning": "draft" },
      { "key": "app-registered", "label": "Registered", "appliesToEntity": "business-application", "engineMeaning": "registered" },
      { "key": "app-archived", "label": "Archived", "appliesToEntity": "business-application", "engineMeaning": "archived" },
      { "key": "approval-pending", "label": "Waiting", "appliesToEntity": "approval-record", "engineMeaning": "pending" },
      { "key": "approval-approved", "label": "Approved", "appliesToEntity": "approval-record", "engineMeaning": "approved" },
      { "key": "approval-rejected", "label": "Rejected", "appliesToEntity": "approval-record", "engineMeaning": "rejected" },
      { "key": "approval-expired", "label": "Expired", "appliesToEntity": "approval-record", "engineMeaning": "expired" },
      { "key": "approval-cancelled", "label": "Cancelled", "appliesToEntity": "approval-record", "engineMeaning": "cancelled" },
      { "key": "approval-recommendation", "label": "Recommendation only", "appliesToEntity": "approval-record", "engineMeaning": "recommendation-only" },
      { "key": "document-missing", "label": "Missing", "appliesToEntity": "document-link", "engineMeaning": "missing", "description": "Shown for a required document with no link. Computed from configuration, never stored." },
      { "key": "document-draft", "label": "Draft", "appliesToEntity": "document-link", "engineMeaning": "draft" },
      { "key": "document-current", "label": "Current", "appliesToEntity": "document-link", "engineMeaning": "current" },
      { "key": "document-stale", "label": "Stale", "appliesToEntity": "document-link", "engineMeaning": "stale" },
      { "key": "claim-open", "label": "Open", "appliesToEntity": "claim", "engineMeaning": "open" },
      { "key": "claim-claimed", "label": "Claimed", "appliesToEntity": "claim", "engineMeaning": "claimed" },
      { "key": "claim-dismissed", "label": "Dismissed", "appliesToEntity": "claim", "engineMeaning": "dismissed" },
      { "key": "deployment-succeeded", "label": "Succeeded", "appliesToEntity": "deployment-record", "engineMeaning": "succeeded" },
      { "key": "deployment-partly", "label": "Partly succeeded", "appliesToEntity": "deployment-record", "engineMeaning": "partly-succeeded" },
      { "key": "deployment-failed", "label": "Failed", "appliesToEntity": "deployment-record", "engineMeaning": "failed" },
      { "key": "deployment-not-deployed", "label": "Not deployed", "appliesToEntity": "deployment-record", "engineMeaning": "not-deployed" },
      { "key": "review-due", "label": "Due", "appliesToEntity": "review", "engineMeaning": "due" },
      { "key": "review-done", "label": "Done", "appliesToEntity": "review", "engineMeaning": "done" },
      { "key": "review-cancelled", "label": "Cancelled", "appliesToEntity": "review", "engineMeaning": "cancelled" }
    ],
    "value-list": [
      { "key": "vocabulary", "label": "Outside vocabulary", "isExtensible": true, "values": [ { "key": "support-tier", "label": "Power Platform support tier" }, { "key": "copilot-studio-zone", "label": "Copilot Studio zone" }, { "key": "agent-risk-tier", "label": "Agent risk tier" } ] },
      { "key": "tool", "label": "Tool", "isExtensible": true, "values": [ { "key": "azure-devops", "label": "Azure DevOps" }, { "key": "github", "label": "GitHub" }, { "key": "power-platform", "label": "Power Platform" }, { "key": "microsoft-365", "label": "Microsoft 365 admin center" }, { "key": "entra", "label": "Microsoft Entra" }, { "key": "sharepoint", "label": "SharePoint" }, { "key": "planner", "label": "Planner" }, { "key": "azure", "label": "Azure" }, { "key": "itsm", "label": "ITSM tool" }, { "key": "other", "label": "Other" } ] },
      { "key": "channel", "label": "Channel", "isExtensible": false, "values": [ { "key": "teams", "label": "Teams" }, { "key": "outlook", "label": "Outlook" }, { "key": "in-app", "label": "In the app" } ] },
      { "key": "application-category", "label": "Application category", "isExtensible": true, "values": [ { "key": "power-platform", "label": "Power Platform" }, { "key": "ai", "label": "AI and agents" }, { "key": "custom-code", "label": "Custom code" }, { "key": "external", "label": "External and SaaS" } ] },
      { "key": "horizon", "label": "Horizon", "isExtensible": false, "values": [ { "key": "now", "label": "Now" }, { "key": "next", "label": "Next" }, { "key": "later", "label": "Later" } ] },
      { "key": "size-band", "label": "Size band", "isExtensible": true, "values": [ { "key": "small", "label": "Small" }, { "key": "medium", "label": "Medium" }, { "key": "large", "label": "Large" } ] },
      { "key": "build-path", "label": "Build path", "isExtensible": true, "values": [ { "key": "citizen", "label": "Citizen" }, { "key": "fusion", "label": "Fusion" }, { "key": "pro", "label": "Pro" }, { "key": "ai-built", "label": "AI-built" } ] },
      { "key": "autonomy-level", "label": "Autonomy level", "isExtensible": true, "values": [ { "key": "assist", "label": "Assist" }, { "key": "route", "label": "Route" }, { "key": "execute", "label": "Execute" } ] },
      { "key": "target-kind", "label": "Target kind", "isExtensible": true, "values": [ { "key": "power-platform-environment", "label": "Power Platform environment" }, { "key": "microsoft-365-tenant", "label": "Microsoft 365 tenant" }, { "key": "azure-resource-group", "label": "Azure resource group" }, { "key": "github-environment", "label": "GitHub environment" }, { "key": "saas-tenant", "label": "SaaS tenant" }, { "key": "other", "label": "Other" } ] },
      { "key": "artifact-kind", "label": "Artifact kind", "isExtensible": true, "values": [ { "key": "managed-solution", "label": "Managed solution" }, { "key": "container-image", "label": "Container image" }, { "key": "code-package", "label": "Code package" }, { "key": "static-site", "label": "Static site" }, { "key": "agent-package", "label": "Agent package" }, { "key": "commit", "label": "Commit" }, { "key": "other", "label": "Other" } ] },
      { "key": "change-result", "label": "Change result", "isExtensible": true, "values": [ { "key": "succeeded", "label": "Succeeded" }, { "key": "partly-succeeded", "label": "Partly succeeded" }, { "key": "failed", "label": "Failed" }, { "key": "rolled-back", "label": "Rolled back" }, { "key": "cancelled", "label": "Cancelled" } ] },
      { "key": "evidence-level", "label": "Evidence level", "isExtensible": false, "values": [ { "key": "read", "label": "Read" }, { "key": "checked", "label": "Checked" }, { "key": "as-reported", "label": "As reported" }, { "key": "proven", "label": "Proven" }, { "key": "designed", "label": "Designed" }, { "key": "not-read", "label": "Not read, with a reason" } ] },
      { "key": "portfolio-level", "label": "Portfolio level", "isExtensible": true, "values": [ { "key": "portfolio", "label": "Portfolio" }, { "key": "application-group", "label": "Application group" } ] },
      { "key": "estate", "label": "Estate", "isExtensible": true, "values": [ { "key": "power-platform-inventory", "label": "Power Platform inventory" } ] },
      { "key": "confidence", "label": "Confidence", "isExtensible": false, "values": [ { "key": "low", "label": "Low" }, { "key": "medium", "label": "Medium" }, { "key": "high", "label": "High" } ] },
      { "key": "impact-role", "label": "Impact role", "isExtensible": false, "values": [ { "key": "changed", "label": "Changed" }, { "key": "depends-on-change", "label": "Depends on the change" }, { "key": "informed", "label": "Informed" } ] },
      { "key": "criticality", "label": "Criticality", "isExtensible": true, "values": [ { "key": "low", "label": "Low" }, { "key": "medium", "label": "Medium" }, { "key": "high", "label": "High" } ] }
    ],
    "tier": [
      { "key": "tier-1", "label": "Tier 1", "rank": 1, "description": "Personal or team use. Reads, or changes only the person's own items. No sensitive data." },
      { "key": "tier-2", "label": "Tier 2", "rank": 2, "description": "Department use. Changes shared data as the signed-in person, or touches internal data." },
      { "key": "tier-3", "label": "Tier 3", "rank": 3, "description": "Acts under its own identity, touches restricted data, reaches the whole organization or outside users, or executes changes as an agent." }
    ],
    "tier-question": [
      { "key": "data-change", "label": "Data change", "questionText": "Does it change data?", "helpText": "Example: a flow that updates customer records under its own identity is tier 3.", "answerOptions": [
        { "key": "reads-only", "label": "Reads only", "answerText": "Reads only, or changes only the person's own items", "impliedTierKey": "tier-1" },
        { "key": "as-user", "label": "As the user", "answerText": "Changes shared business data as the signed-in person", "impliedTierKey": "tier-2" },
        { "key": "own-identity", "label": "Own identity", "answerText": "Changes data under its own identity, or in a system of record", "impliedTierKey": "tier-3" } ] },
      { "key": "sensitivity", "label": "Sensitive data", "questionText": "Does it touch sensitive data?", "answerOptions": [
        { "key": "none", "label": "None", "answerText": "No", "impliedTierKey": "tier-1" },
        { "key": "internal", "label": "Internal", "answerText": "Internal data only", "impliedTierKey": "tier-2" },
        { "key": "restricted", "label": "Restricted", "answerText": "Personal, financial, regulated or otherwise restricted data", "impliedTierKey": "tier-3" } ] },
      { "key": "reach", "label": "Reach", "questionText": "Who can reach it?", "answerOptions": [
        { "key": "small-team", "label": "Small team", "answerText": "The builder or a small team", "impliedTierKey": "tier-1" },
        { "key": "department", "label": "Department", "answerText": "A department or business unit", "impliedTierKey": "tier-2" },
        { "key": "organization", "label": "Organization or outside", "answerText": "The whole organization, or anyone outside it", "impliedTierKey": "tier-3" } ] },
      { "key": "agent-autonomy", "label": "Agent autonomy", "questionText": "Does an agent act on its own?", "appliesToKindKeys": ["agent"], "answerOptions": [
        { "key": "assists", "label": "Assists", "answerText": "No, it assists a person who stays in the loop", "impliedTierKey": "tier-1" },
        { "key": "routes", "label": "Routes", "answerText": "It answers or routes, a person acts", "impliedTierKey": "tier-2" },
        { "key": "executes", "label": "Executes", "answerText": "It executes changes in systems", "impliedTierKey": "tier-3", "triggers": { "reviewTypeKeys": ["security-review"] } } ] },
      { "key": "failure-impact", "label": "Failure impact", "questionText": "What happens if it fails for a day?", "answerOptions": [
        { "key": "little", "label": "Little", "answerText": "Little", "impliedTierKey": "tier-1" },
        { "key": "team-slowed", "label": "Team slowed", "answerText": "A team is slowed", "impliedTierKey": "tier-2" },
        { "key": "business-hit", "label": "Business hit", "answerText": "Revenue, compliance or trust is hit", "impliedTierKey": "tier-3" } ] }
    ],
    "vocabulary-mapping": [
      { "key": "tier-1-support", "label": "Tier 1 support tier", "sourceEntity": "tier", "sourceKey": "tier-1", "vocabularyKey": "support-tier", "term": "Productivity", "sourceUrl": "https://learn.microsoft.com/power-platform/guidance/adoption/support-strategy-solutions", "checkedOn": "2026-11-22" },
      { "key": "tier-2-support", "label": "Tier 2 support tier", "sourceEntity": "tier", "sourceKey": "tier-2", "vocabularyKey": "support-tier", "term": "Important", "sourceUrl": "https://learn.microsoft.com/power-platform/guidance/adoption/support-strategy-solutions", "checkedOn": "2026-11-22" },
      { "key": "tier-3-support", "label": "Tier 3 support tier", "sourceEntity": "tier", "sourceKey": "tier-3", "vocabularyKey": "support-tier", "term": "Critical", "sourceUrl": "https://learn.microsoft.com/power-platform/guidance/adoption/support-strategy-solutions", "checkedOn": "2026-11-22" },
      { "key": "tier-1-zone", "label": "Tier 1 zone", "sourceEntity": "tier", "sourceKey": "tier-1", "vocabularyKey": "copilot-studio-zone", "term": "Zone 1 - Citizen Development Zone", "sourceUrl": "https://learn.microsoft.com/microsoft-copilot-studio/guidance/sec-gov-phase2", "checkedOn": "2026-11-22" },
      { "key": "tier-2-zone", "label": "Tier 2 zone", "sourceEntity": "tier", "sourceKey": "tier-2", "vocabularyKey": "copilot-studio-zone", "term": "Zone 2 - Partnered Development Zone", "sourceUrl": "https://learn.microsoft.com/microsoft-copilot-studio/guidance/sec-gov-phase2", "checkedOn": "2026-11-22" },
      { "key": "tier-3-zone", "label": "Tier 3 zone", "sourceEntity": "tier", "sourceKey": "tier-3", "vocabularyKey": "copilot-studio-zone", "term": "Zone 3 - Professional Development Zone", "sourceUrl": "https://learn.microsoft.com/microsoft-copilot-studio/guidance/sec-gov-phase2", "checkedOn": "2026-11-22" },
      { "key": "tier-1-agent-risk", "label": "Tier 1 agent risk", "sourceEntity": "tier", "sourceKey": "tier-1", "vocabularyKey": "agent-risk-tier", "term": "Tier 1: Low risk", "sourceUrl": "https://learn.microsoft.com/agents/center-of-excellence/govern-agents-risk", "checkedOn": "2026-11-22" },
      { "key": "tier-2-agent-risk", "label": "Tier 2 agent risk", "sourceEntity": "tier", "sourceKey": "tier-2", "vocabularyKey": "agent-risk-tier", "term": "Tier 2: Medium risk", "sourceUrl": "https://learn.microsoft.com/agents/center-of-excellence/govern-agents-risk", "checkedOn": "2026-11-22" },
      { "key": "tier-3-agent-risk", "label": "Tier 3 agent risk", "sourceEntity": "tier", "sourceKey": "tier-3", "vocabularyKey": "agent-risk-tier", "term": "Tier 3: High risk", "sourceUrl": "https://learn.microsoft.com/agents/center-of-excellence/govern-agents-risk", "checkedOn": "2026-11-22" }
    ],
    "lifecycle-stage": [
      { "key": "idea", "label": "Idea", "stageNumber": 0, "stageRank": 0, "engineMeaning": "pre-live", "leadingOwnerRoleKey": "business-owner" },
      { "key": "intake-and-triage", "label": "Intake and triage", "stageNumber": 1, "stageRank": 1, "engineMeaning": "pre-live", "leadingRoleKey": "platform-admin" },
      { "key": "prioritize-and-fund", "label": "Prioritize and fund", "stageNumber": 2, "stageRank": 2, "engineMeaning": "pre-live", "leadingRoleKey": "pmo" },
      { "key": "design-and-review", "label": "Design and architecture review", "stageNumber": 3, "stageRank": 3, "engineMeaning": "pre-live", "leadingRoleKey": "architect" },
      { "key": "risk-and-tier", "label": "Risk and tier", "stageNumber": 4, "stageRank": 4, "engineMeaning": "pre-live", "leadingRoleKey": "platform-admin" },
      { "key": "build", "label": "Build", "stageNumber": 5, "stageRank": 5, "engineMeaning": "pre-live", "leadingOwnerRoleKey": "builder" },
      { "key": "test", "label": "Test", "stageNumber": 6, "stageRank": 6, "engineMeaning": "pre-live", "leadingOwnerRoleKey": "builder" },
      { "key": "change-and-release", "label": "Change and release", "stageNumber": 7, "stageRank": 7, "engineMeaning": "pre-live", "leadingRoleKey": "platform-admin" },
      { "key": "operate", "label": "Operate", "stageNumber": 8, "stageRank": 8, "engineMeaning": "live", "leadingOwnerRoleKey": "technical-owner" },
      { "key": "review", "label": "Review", "stageNumber": 9, "stageRank": 9, "engineMeaning": "live", "leadingRoleKey": "pmo" },
      { "key": "retire", "label": "Retire", "stageNumber": 10, "stageRank": 10, "engineMeaning": "retired", "leadingRoleKey": "platform-admin" }
    ],
    "gate": [
      { "key": "accept", "label": "Accept", "afterStageKey": "intake-and-triage", "gateDecisionTypeKey": "intake" },
      { "key": "commit", "label": "Commit", "afterStageKey": "prioritize-and-fund", "gateDecisionTypeKey": "priority" },
      { "key": "design", "label": "Design", "afterStageKey": "risk-and-tier", "gateDecisionTypeKey": "design" },
      { "key": "go-live", "label": "Go-live", "afterStageKey": "change-and-release", "gateDecisionTypeKey": "go-live" },
      { "key": "value", "label": "Value", "afterStageKey": "review", "gateDecisionTypeKey": "portfolio" }
    ],
    "gate-requirement": [
      { "key": "accept-business-owner", "label": "A business owner is named", "gateKey": "accept", "requirementKind": "owner-assigned", "requiredOwnerRoleKey": "business-owner", "guidelineKey": "g2" },
      { "key": "design-adr-tier-3", "label": "Architecture decision recorded", "gateKey": "design", "tierKey": "tier-3", "requirementKind": "document-current", "requiredDocumentTypeKey": "architecture-decision-record" },
      { "key": "go-live-acceptance", "label": "Owner accepted after testing", "gateKey": "go-live", "requirementKind": "decision-accepted", "requiredDecisionTypeKey": "acceptance", "guidelineKey": "g9" },
      { "key": "go-live-runbook-tier-2", "label": "Runbook current", "gateKey": "go-live", "tierKey": "tier-2", "requirementKind": "document-current", "requiredDocumentTypeKey": "runbook" },
      { "key": "go-live-runbook-tier-3", "label": "Runbook current", "gateKey": "go-live", "tierKey": "tier-3", "requirementKind": "document-current", "requiredDocumentTypeKey": "runbook" },
      { "key": "go-live-security-tier-3", "label": "Security review done", "gateKey": "go-live", "tierKey": "tier-3", "requirementKind": "review-done", "requiredReviewTypeKey": "security-review" },
      { "key": "go-live-autonomy-agent", "label": "Autonomy level agreed", "gateKey": "go-live", "applicationKindKey": "agent", "requirementKind": "decision-accepted", "requiredDecisionTypeKey": "autonomy-level", "guidelineKey": "g17" }
    ],
    "application-kind": [
      { "key": "power-platform-app", "label": "Power Platform app", "categoryKey": "power-platform", "defaultComponentKindKeys": ["canvas-app", "model-driven-app", "cloud-flow", "solution"] },
      { "key": "agent", "label": "Agent", "categoryKey": "ai", "defaultComponentKindKeys": ["copilot-studio-agent", "entra-agent-identity"] },
      { "key": "ai-built-app", "label": "AI-built app", "categoryKey": "ai", "defaultComponentKindKeys": ["code-app", "repository"] },
      { "key": "code-app", "label": "Code app", "categoryKey": "custom-code", "defaultComponentKindKeys": ["code-app", "repository"] },
      { "key": "runtime-app", "label": "Copilot managed runtime app", "categoryKey": "ai", "defaultComponentKindKeys": ["copilot-managed-runtime-app"] },
      { "key": "saas", "label": "SaaS", "categoryKey": "external" },
      { "key": "external-system", "label": "External system", "categoryKey": "external" },
      { "key": "api", "label": "API", "categoryKey": "custom-code", "defaultComponentKindKeys": ["azure-resource"] },
      { "key": "mcp-server", "label": "MCP server", "categoryKey": "custom-code", "defaultComponentKindKeys": ["azure-resource", "repository"] },
      { "key": "practice-app", "label": "Practice app", "categoryKey": "power-platform", "defaultComponentKindKeys": ["canvas-app"] }
    ],
    "component-kind": [
      { "key": "canvas-app", "label": "Canvas app", "sourceToolKey": "power-platform", "isGapCheckCovered": true },
      { "key": "model-driven-app", "label": "Model-driven app", "sourceToolKey": "power-platform", "isGapCheckCovered": true },
      { "key": "code-app", "label": "Code app", "sourceToolKey": "power-platform", "isGapCheckCovered": true },
      { "key": "cloud-flow", "label": "Cloud flow", "sourceToolKey": "power-platform", "isGapCheckCovered": true },
      { "key": "copilot-studio-agent", "label": "Copilot Studio agent", "sourceToolKey": "power-platform", "isGapCheckCovered": true },
      { "key": "copilot-managed-runtime-app", "label": "Copilot managed runtime app", "sourceToolKey": "microsoft-365", "isGapCheckCovered": true },
      { "key": "power-pages-site", "label": "Power Pages site", "sourceToolKey": "power-platform", "isGapCheckCovered": true, "description": "Inventory coverage is preview." },
      { "key": "solution", "label": "Solution", "sourceToolKey": "power-platform", "isGapCheckCovered": false },
      { "key": "entra-agent-identity", "label": "Entra agent identity", "sourceToolKey": "entra", "isGapCheckCovered": false },
      { "key": "repository", "label": "Repository", "sourceToolKey": "github", "isGapCheckCovered": false },
      { "key": "azure-resource", "label": "Azure resource", "sourceToolKey": "azure", "isGapCheckCovered": false }
    ],
    "dependency-type": [
      { "key": "reads-data-from", "label": "Reads data from", "inverseLabel": "Data read by", "countsForImpact": true },
      { "key": "writes-data-to", "label": "Writes data to", "inverseLabel": "Data written by", "countsForImpact": true },
      { "key": "calls-api-of", "label": "Calls API of", "inverseLabel": "API called by", "countsForImpact": true },
      { "key": "hosted-on", "label": "Hosted on", "inverseLabel": "Hosts", "countsForImpact": true },
      { "key": "run-time-need-in-target", "label": "Run-time need in target", "inverseLabel": "Needed in target by", "countsForImpact": true },
      { "key": "replaces", "label": "Replaces", "inverseLabel": "Replaced by", "countsForImpact": false }
    ],
    "decision-type": [
      { "key": "intake", "label": "Intake", "defaultSubjectKind": "intake-request", "requiresApproval": false, "outcomes": [ { "key": "accept", "label": "Accept" }, { "key": "park", "label": "Park" }, { "key": "redirect", "label": "Redirect to reuse" }, { "key": "decline", "label": "Decline" } ] },
      { "key": "priority", "label": "Priority", "defaultSubjectKind": "intake-request", "requiresApproval": false, "outcomes": [ { "key": "fund", "label": "Fund" }, { "key": "defer", "label": "Defer" }, { "key": "stop", "label": "Stop" } ] },
      { "key": "design", "label": "Design", "defaultSubjectKind": "business-application", "requiresApproval": false, "needsOptionsConsidered": true, "outcomes": [ { "key": "approve", "label": "Approve" }, { "key": "rework", "label": "Rework" } ] },
      { "key": "data-use", "label": "Data use", "defaultSubjectKind": "business-application", "requiresApproval": true, "outcomes": [ { "key": "allow", "label": "Allow" }, { "key": "deny", "label": "Deny" } ] },
      { "key": "tier", "label": "Tier", "defaultSubjectKind": "business-application", "requiresApproval": false, "outcomes": [ { "key": "confirm", "label": "Confirm" }, { "key": "raise", "label": "Raise" }, { "key": "lower", "label": "Lower" } ] },
      { "key": "risk-acceptance", "label": "Risk acceptance", "defaultSubjectKind": "business-application", "requiresApproval": true, "outcomes": [ { "key": "accept", "label": "Accept" }, { "key": "decline", "label": "Decline" } ] },
      { "key": "acceptance", "label": "Acceptance", "defaultSubjectKind": "business-application", "requiresApproval": true, "outcomes": [ { "key": "accept", "label": "Accept" }, { "key": "reject", "label": "Reject" } ] },
      { "key": "change-authorization", "label": "Change authorization", "defaultSubjectKind": "change-request", "requiresApproval": true, "outcomes": [ { "key": "authorize", "label": "Authorize" }, { "key": "reject", "label": "Reject" } ] },
      { "key": "go-live", "label": "Go-live", "defaultSubjectKind": "business-application", "requiresApproval": true, "outcomes": [ { "key": "go", "label": "Go", "nextLifecycleStageKey": "operate" }, { "key": "no-go", "label": "No go" } ] },
      { "key": "ownership", "label": "Ownership", "defaultSubjectKind": "business-application", "requiresApproval": false, "outcomes": [ { "key": "assign", "label": "Assign" }, { "key": "end", "label": "End" } ] },
      { "key": "portfolio", "label": "Portfolio", "defaultSubjectKind": "business-application", "requiresApproval": false, "outcomes": [ { "key": "keep", "label": "Keep" }, { "key": "invest", "label": "Invest" }, { "key": "replace", "label": "Replace" }, { "key": "retire", "label": "Retire" } ] },
      { "key": "exception", "label": "Exception", "defaultSubjectKind": "business-application", "requiresApproval": true, "needsOptionsConsidered": true, "outcomes": [ { "key": "grant", "label": "Grant" }, { "key": "deny", "label": "Deny" } ] },
      { "key": "retirement", "label": "Retirement", "defaultSubjectKind": "business-application", "requiresApproval": true, "outcomes": [ { "key": "retire", "label": "Retire", "nextLifecycleStageKey": "retire" }, { "key": "keep", "label": "Keep" } ] },
      { "key": "autonomy-level", "label": "Autonomy level", "defaultSubjectKind": "business-application", "requiresApproval": true, "outcomes": [ { "key": "agree", "label": "Agree" }, { "key": "decline", "label": "Decline" } ] },
      { "key": "configuration-change", "label": "Configuration change", "defaultSubjectKind": "configuration-draft", "requiresApproval": true, "outcomes": [ { "key": "accept", "label": "Accept" }, { "key": "reject", "label": "Reject" } ] },
      { "key": "gate-policy-change", "label": "Gate policy change", "defaultSubjectKind": "configuration-draft", "requiresApproval": true, "outcomes": [ { "key": "accept", "label": "Accept" }, { "key": "reject", "label": "Reject" } ] }
    ],
    "change-type": [
      { "key": "standard", "label": "Standard", "requiresAuthorization": false, "requiresStandardModel": true },
      { "key": "normal", "label": "Normal", "requiresAuthorization": true },
      { "key": "emergency", "label": "Emergency", "requiresAuthorization": true, "allowsAuthorizationAfter": true, "requiresReviewAfter": true, "reviewDueDays": 5 }
    ],
    "standard-change-model": [],
    "document-type": [
      { "key": "wiki-page", "label": "Wiki page", "defaultLinkKindKey": "ado-wiki-page", "freshnessDays": 365 },
      { "key": "runbook", "label": "Runbook", "defaultLinkKindKey": "ado-wiki-page", "freshnessDays": 180, "currentNeedsPerson": true },
      { "key": "architecture-decision-record", "label": "Architecture decision record", "defaultLinkKindKey": "repository-file", "currentNeedsPerson": true },
      { "key": "support-guide", "label": "Support guide", "defaultLinkKindKey": "sharepoint-page", "freshnessDays": 365 }
    ],
    "documentation-requirement": [
      { "key": "wiki-page-all", "label": "Every application has a wiki page", "documentTypeKey": "wiki-page", "fromStageKey": "build" },
      { "key": "runbook-tier-2", "label": "Tier 2 runbook", "documentTypeKey": "runbook", "tierKey": "tier-2", "fromStageKey": "change-and-release" },
      { "key": "runbook-tier-3", "label": "Tier 3 runbook", "documentTypeKey": "runbook", "tierKey": "tier-3", "fromStageKey": "change-and-release" },
      { "key": "adr-tier-3", "label": "Tier 3 architecture decision record", "documentTypeKey": "architecture-decision-record", "tierKey": "tier-3", "fromStageKey": "design-and-review" },
      { "key": "support-guide-tier-3", "label": "Tier 3 support guide", "documentTypeKey": "support-guide", "tierKey": "tier-3", "fromStageKey": "operate" }
    ],
    "review-type": [
      { "key": "owner-check", "label": "Owner check", "producesDecisionTypeKey": "ownership", "defaultOwnerRoleKey": "business-owner" },
      { "key": "tier-check", "label": "Tier check", "producesDecisionTypeKey": "tier", "defaultOwnerRoleKey": "technical-owner" },
      { "key": "access-check", "label": "Access check", "producesDecisionTypeKey": "risk-acceptance", "defaultOwnerRoleKey": "business-owner", "description": "Links to Entra access review results; never reruns them." },
      { "key": "benefit", "label": "Benefit review", "producesDecisionTypeKey": "portfolio", "defaultOwnerRoleKey": "business-owner" },
      { "key": "architecture", "label": "Architecture review", "producesDecisionTypeKey": "design", "defaultOwnerRoleKey": "technical-owner" },
      { "key": "post-change", "label": "Post-change review", "producesDecisionTypeKey": "acceptance", "defaultOwnerRoleKey": "technical-owner" },
      { "key": "security-review", "label": "Security review", "producesDecisionTypeKey": "risk-acceptance", "defaultOwnerRoleKey": "security-owner" }
    ],
    "review-cadence": [
      { "key": "owner-check-tier-1", "label": "Owner check, tier 1", "reviewTypeKey": "owner-check", "tierKey": "tier-1", "intervalDays": 365, "leadDays": 14, "escalateAfterDays": 14 },
      { "key": "owner-check-tier-2", "label": "Owner check, tier 2", "reviewTypeKey": "owner-check", "tierKey": "tier-2", "intervalDays": 180, "leadDays": 14, "escalateAfterDays": 14 },
      { "key": "owner-check-tier-3", "label": "Owner check, tier 3", "reviewTypeKey": "owner-check", "tierKey": "tier-3", "intervalDays": 90, "leadDays": 14, "escalateAfterDays": 7 },
      { "key": "tier-check", "label": "Tier check", "reviewTypeKey": "tier-check", "tierKey": "*", "intervalDays": 365, "leadDays": 14, "escalateAfterDays": 14 },
      { "key": "access-check-tier-1", "label": "Access check, tier 1", "reviewTypeKey": "access-check", "tierKey": "tier-1", "intervalDays": 365, "leadDays": 14, "escalateAfterDays": 14 },
      { "key": "access-check-tier-2", "label": "Access check, tier 2", "reviewTypeKey": "access-check", "tierKey": "tier-2", "intervalDays": 180, "leadDays": 14, "escalateAfterDays": 14 },
      { "key": "access-check-tier-3", "label": "Access check, tier 3", "reviewTypeKey": "access-check", "tierKey": "tier-3", "intervalDays": 90, "leadDays": 14, "escalateAfterDays": 7 },
      { "key": "benefit", "label": "Benefit review", "reviewTypeKey": "benefit", "tierKey": "*", "intervalDays": 365, "leadDays": 30, "escalateAfterDays": 30 }
    ],
    "reminder-rule": [
      { "key": "review-reminder", "label": "Review due", "subjectKind": "review", "leadDays": 14, "repeatDays": 7, "escalateAfterDays": 14, "channelKey": "teams" },
      { "key": "document-reminder", "label": "Document missing or stale", "subjectKind": "document", "leadDays": 0, "repeatDays": 14, "escalateAfterDays": 30, "channelKey": "teams" },
      { "key": "approval-reminder", "label": "Approval waiting", "subjectKind": "approval", "leadDays": 0, "repeatDays": 2, "escalateAfterDays": 5, "channelKey": "outlook" },
      { "key": "claim-reminder", "label": "Claim open", "subjectKind": "claim", "leadDays": 0, "repeatDays": 7, "escalateAfterDays": 21, "channelKey": "teams" }
    ],
    "party": [
      { "key": "business", "label": "Business" },
      { "key": "it", "label": "IT" },
      { "key": "ai", "label": "AI" },
      { "key": "citizen-developers", "label": "Citizen developers" },
      { "key": "pmo", "label": "PMO" },
      { "key": "change-control", "label": "Change control" },
      { "key": "architects", "label": "Architects" }
    ],
    "owner-role": [
      { "key": "business-owner", "label": "Business owner", "partyKey": "business", "requiresDeputy": true, "maximumHolders": 1 },
      { "key": "sponsor", "label": "Sponsor", "partyKey": "business" },
      { "key": "data-owner", "label": "Data owner", "partyKey": "business", "requiresDeputy": true },
      { "key": "process-owner", "label": "Process owner", "partyKey": "business", "requiresDeputy": true },
      { "key": "technical-owner", "label": "Technical owner", "partyKey": "it", "requiresDeputy": true },
      { "key": "support-owner", "label": "Support owner", "partyKey": "it", "requiresDeputy": true },
      { "key": "security-owner", "label": "Security owner", "partyKey": "it" },
      { "key": "builder", "label": "Builder", "partyKey": "citizen-developers" }
    ],
    "role": [
      { "key": "requester", "label": "Requester", "partyKey": "business", "duties": "Submits intake requests." },
      { "key": "maker", "label": "Maker", "partyKey": "citizen-developers", "duties": "Builds in approved environments and records links." },
      { "key": "application-owner", "label": "Application owner", "partyKey": "business", "duties": "Holds owner roles on applications." },
      { "key": "data-owner", "label": "Data owner", "partyKey": "business", "duties": "Decides data use for a data area." },
      { "key": "architect", "label": "Architect", "partyKey": "architects", "duties": "Reviews designs and grants exceptions to standards." },
      { "key": "change-manager", "label": "Change manager", "partyKey": "change-control", "duties": "Runs the change schedule; authorizes tier 3 changes and emergencies." },
      { "key": "change-board", "label": "Change board", "partyKey": "change-control", "duties": "Decides exceptions about changes, and windows that cover several applications, as a group." },
      { "key": "pmo", "label": "PMO", "partyKey": "pmo", "duties": "Scores demand and runs portfolio reviews." },
      { "key": "ai-lead", "label": "AI lead", "partyKey": "ai", "duties": "Keeps the AI guidelines, is consulted on agent autonomy, decides on agents Microsoft flags." },
      { "key": "security-reviewer", "label": "Security reviewer", "partyKey": "it", "duties": "Runs security reviews and agrees agent autonomy levels." },
      { "key": "platform-admin", "label": "Platform admin", "partyKey": "it", "duties": "Triages intake, confirms tiers, runs the gap check." },
      { "key": "configuration-admin", "label": "Configuration admin", "partyKey": "it", "duties": "Instructs and accepts configuration changes.", "canChangeConfiguration": true },
      { "key": "auditor", "label": "Auditor", "partyKey": "change-control", "duties": "Reads everything, including audit entries, and changes nothing.", "isReadOnly": true },
      { "key": "reader", "label": "Reader", "partyKey": "business", "duties": "Reads the register, decisions and the change schedule. No audit entries, no restricted columns. Never writes.", "isReadOnly": true },
      { "key": "deployment-recorder", "label": "Deployment recorder", "partyKey": "it", "duties": "Held by pipeline identities through a workload grant. Writes deployment records.", "isWorkloadRole": true },
      { "key": "approval-writer", "label": "Approval writer", "partyKey": "it", "duties": "Held by the approval runner through a workload grant. Writes the person's answer on an approval record; never decides.", "isWorkloadRole": true }
    ],
    "role-group-binding": [],
    "decision-right": [
      { "key": "intake-a", "label": "Intake: accountable", "decisionTypeKey": "intake", "letter": "accountable", "roleKey": "platform-admin" },
      { "key": "intake-s", "label": "Intake: AI drafts", "decisionTypeKey": "intake", "letter": "supports" },
      { "key": "priority-a", "label": "Priority: accountable", "decisionTypeKey": "priority", "letter": "accountable", "ownerRoleKey": "sponsor" },
      { "key": "priority-r", "label": "Priority: responsible", "decisionTypeKey": "priority", "letter": "responsible", "roleKey": "pmo" },
      { "key": "design-a", "label": "Design: accountable", "decisionTypeKey": "design", "letter": "accountable", "roleKey": "architect" },
      { "key": "design-s", "label": "Design: AI drafts", "decisionTypeKey": "design", "letter": "supports" },
      { "key": "data-use-a", "label": "Data use: accountable", "decisionTypeKey": "data-use", "letter": "accountable", "ownerRoleKey": "data-owner" },
      { "key": "tier-a", "label": "Tier: accountable", "decisionTypeKey": "tier", "letter": "accountable", "roleKey": "platform-admin" },
      { "key": "tier-s", "label": "Tier: AI suggests", "decisionTypeKey": "tier", "letter": "supports" },
      { "key": "risk-acceptance-a", "label": "Risk acceptance: accountable", "decisionTypeKey": "risk-acceptance", "letter": "accountable", "ownerRoleKey": "business-owner" },
      { "key": "acceptance-a", "label": "Acceptance: accountable", "decisionTypeKey": "acceptance", "letter": "accountable", "ownerRoleKey": "business-owner" },
      { "key": "change-auth-a", "label": "Change: accountable", "decisionTypeKey": "change-authorization", "letter": "accountable", "ownerRoleKey": "business-owner" },
      { "key": "change-auth-s", "label": "Change: AI drafts", "decisionTypeKey": "change-authorization", "letter": "supports" },
      { "key": "change-auth-tier-3-a", "label": "Tier 3 change: accountable", "decisionTypeKey": "change-authorization", "tierKey": "tier-3", "letter": "accountable", "roleKey": "change-manager" },
      { "key": "change-auth-tier-3-c", "label": "Tier 3 change: consulted", "decisionTypeKey": "change-authorization", "tierKey": "tier-3", "letter": "consulted", "ownerRoleKey": "business-owner" },
      { "key": "change-auth-tier-3-s", "label": "Tier 3 change: AI drafts", "decisionTypeKey": "change-authorization", "tierKey": "tier-3", "letter": "supports" },
      { "key": "change-auth-emergency-a", "label": "Emergency: accountable", "decisionTypeKey": "change-authorization", "changeTypeKey": "emergency", "letter": "accountable", "roleKey": "change-manager" },
      { "key": "change-auth-tier-3-emergency-a", "label": "Tier 3 emergency: accountable", "decisionTypeKey": "change-authorization", "tierKey": "tier-3", "changeTypeKey": "emergency", "letter": "accountable", "roleKey": "change-manager" },
      { "key": "go-live-a", "label": "Go-live: accountable", "decisionTypeKey": "go-live", "letter": "accountable", "ownerRoleKey": "business-owner" },
      { "key": "ownership-a", "label": "Ownership: accountable", "decisionTypeKey": "ownership", "letter": "accountable", "ownerRoleKey": "business-owner" },
      { "key": "portfolio-a", "label": "Portfolio: accountable", "decisionTypeKey": "portfolio", "letter": "accountable", "ownerRoleKey": "business-owner" },
      { "key": "portfolio-r", "label": "Portfolio: responsible", "decisionTypeKey": "portfolio", "letter": "responsible", "roleKey": "pmo" },
      { "key": "exception-a", "label": "Exception: accountable", "decisionTypeKey": "exception", "letter": "accountable", "roleKey": "architect" },
      { "key": "change-exception-standard-a", "label": "Exception about a standard change: accountable", "decisionTypeKey": "exception", "changeTypeKey": "standard", "letter": "accountable", "roleKey": "change-board" },
      { "key": "change-exception-normal-a", "label": "Exception about a normal change: accountable", "decisionTypeKey": "exception", "changeTypeKey": "normal", "letter": "accountable", "roleKey": "change-board" },
      { "key": "change-exception-emergency-a", "label": "Exception about an emergency change: accountable", "decisionTypeKey": "exception", "changeTypeKey": "emergency", "letter": "accountable", "roleKey": "change-board" },
      { "key": "retirement-a", "label": "Retirement: accountable", "decisionTypeKey": "retirement", "letter": "accountable", "ownerRoleKey": "business-owner" },
      { "key": "retirement-r", "label": "Retirement: responsible", "decisionTypeKey": "retirement", "letter": "responsible", "roleKey": "platform-admin" },
      { "key": "autonomy-level-a", "label": "Autonomy: accountable", "decisionTypeKey": "autonomy-level", "letter": "accountable", "roleKey": "security-reviewer" },
      { "key": "autonomy-level-c", "label": "Autonomy: AI lead consulted", "decisionTypeKey": "autonomy-level", "letter": "consulted", "roleKey": "ai-lead" },
      { "key": "configuration-change-a", "label": "Configuration: accountable", "decisionTypeKey": "configuration-change", "letter": "accountable", "roleKey": "configuration-admin" },
      { "key": "configuration-change-c-ai-lead", "label": "Configuration: AI lead consulted on AI rules", "decisionTypeKey": "configuration-change", "letter": "consulted", "roleKey": "ai-lead" },
      { "key": "configuration-change-c-platform-admin", "label": "Configuration: platform admin consulted on roles and bindings", "decisionTypeKey": "configuration-change", "letter": "consulted", "roleKey": "platform-admin" },
      { "key": "configuration-change-s", "label": "Configuration: AI drafts", "decisionTypeKey": "configuration-change", "letter": "supports" },
      { "key": "gate-policy-change-a", "label": "Gate policy: accountable", "decisionTypeKey": "gate-policy-change", "letter": "accountable", "roleKey": "configuration-admin", "description": "Who owns the policy table is open under D-03." },
      { "key": "gate-policy-change-c", "label": "Gate policy: consulted", "decisionTypeKey": "gate-policy-change", "letter": "consulted", "roleKey": "change-manager" },
      { "key": "gate-policy-change-s", "label": "Gate policy: AI drafts", "decisionTypeKey": "gate-policy-change", "letter": "supports" }
    ],
    "approval-rule": [
      { "key": "data-use-approval", "label": "Data owner approves data use", "decisionTypeKey": "data-use", "approverOwnerRoleKey": "data-owner", "approvalMode": "everyone-must-approve", "timeoutDays": 10, "requiresTypedReason": true },
      { "key": "risk-acceptance-approval", "label": "Business owner accepts risk", "decisionTypeKey": "risk-acceptance", "approverOwnerRoleKey": "business-owner", "approvalMode": "everyone-must-approve", "timeoutDays": 10, "requiresTypedReason": true },
      { "key": "acceptance-approval", "label": "Business owner accepts", "decisionTypeKey": "acceptance", "approverOwnerRoleKey": "business-owner", "approvalMode": "everyone-must-approve", "timeoutDays": 10 },
      { "key": "change-auth-owner", "label": "Business owner authorizes the change", "decisionTypeKey": "change-authorization", "approverOwnerRoleKey": "business-owner", "approvalMode": "everyone-must-approve", "timeoutDays": 5 },
      { "key": "change-auth-tier-3", "label": "Change manager authorizes tier 3", "decisionTypeKey": "change-authorization", "tierKey": "tier-3", "approverRoleKey": "change-manager", "approvalMode": "first-to-respond", "minimumCount": 1, "timeoutDays": 5, "requiresTypedReason": true },
      { "key": "change-auth-emergency", "label": "Change manager authorizes an emergency", "decisionTypeKey": "change-authorization", "changeTypeKey": "emergency", "approverRoleKey": "change-manager", "approvalMode": "first-to-respond", "timeoutDays": 2 },
      { "key": "change-auth-tier-3-emergency", "label": "Change manager authorizes a tier 3 emergency", "decisionTypeKey": "change-authorization", "tierKey": "tier-3", "changeTypeKey": "emergency", "approverRoleKey": "change-manager", "approvalMode": "first-to-respond", "timeoutDays": 2, "requiresTypedReason": true },
      { "key": "go-live-owner", "label": "Business owner approves go-live", "decisionTypeKey": "go-live", "approverOwnerRoleKey": "business-owner", "approvalMode": "everyone-must-approve", "timeoutDays": 5 },
      { "key": "go-live-tier-3-owner", "label": "Tier 3 go-live: business owner first", "decisionTypeKey": "go-live", "tierKey": "tier-3", "approverOwnerRoleKey": "business-owner", "approvalMode": "sequential", "sequence": 1, "timeoutDays": 5 },
      { "key": "go-live-tier-3-security", "label": "Tier 3 go-live: then security", "decisionTypeKey": "go-live", "tierKey": "tier-3", "approverRoleKey": "security-reviewer", "approvalMode": "sequential", "sequence": 2, "timeoutDays": 5, "requiresTypedReason": true },
      { "key": "exception-approval", "label": "Architect grants an exception", "decisionTypeKey": "exception", "approverRoleKey": "architect", "approvalMode": "first-to-respond", "requiresTypedReason": true },
      { "key": "change-exception-standard", "label": "Change board decides an exception about a standard change", "decisionTypeKey": "exception", "changeTypeKey": "standard", "approverRoleKey": "change-board", "approvalMode": "first-to-respond", "minimumCount": 1, "timeoutDays": 5, "requiresTypedReason": true },
      { "key": "change-exception-normal", "label": "Change board decides an exception about a normal change", "decisionTypeKey": "exception", "changeTypeKey": "normal", "approverRoleKey": "change-board", "approvalMode": "first-to-respond", "minimumCount": 1, "timeoutDays": 5, "requiresTypedReason": true },
      { "key": "change-exception-emergency", "label": "Change board decides an exception about an emergency change", "decisionTypeKey": "exception", "changeTypeKey": "emergency", "approverRoleKey": "change-board", "approvalMode": "first-to-respond", "minimumCount": 1, "timeoutDays": 2, "requiresTypedReason": true },
      { "key": "retirement-approval", "label": "Business owner approves retirement", "decisionTypeKey": "retirement", "approverOwnerRoleKey": "business-owner", "approvalMode": "everyone-must-approve" },
      { "key": "autonomy-level-approval", "label": "Security agrees agent autonomy", "decisionTypeKey": "autonomy-level", "applicationKindKey": "agent", "approverRoleKey": "security-reviewer", "approvalMode": "first-to-respond", "requiresTypedReason": true },
      { "key": "configuration-change-approval", "label": "A configuration admin accepts", "decisionTypeKey": "configuration-change", "approverRoleKey": "configuration-admin", "approvalMode": "first-to-respond", "requiresTypedReason": true },
      { "key": "gate-policy-change-config-admin", "label": "Gate policy: configuration admin", "decisionTypeKey": "gate-policy-change", "approverRoleKey": "configuration-admin", "approvalMode": "everyone-must-approve", "requiresTypedReason": true },
      { "key": "gate-policy-change-change-manager", "label": "Gate policy: change manager consulted", "decisionTypeKey": "gate-policy-change", "approverRoleKey": "change-manager", "approvalMode": "first-to-respond", "isConsultation": true }
    ],
    "gate-policy": [
      { "key": "gp-development", "label": "Development is not governed", "tierKey": "*", "changeTypeKey": "*", "targetStageKey": "development", "gateLevel": "off" },
      { "key": "gp-test", "label": "Test: flag", "tierKey": "*", "changeTypeKey": "*", "targetStageKey": "test", "gateLevel": "flag" },
      { "key": "gp-prod-tier-1", "label": "Production tier 1: flag", "tierKey": "tier-1", "changeTypeKey": "*", "targetStageKey": "production", "gateLevel": "flag" },
      { "key": "gp-prod-tier-2", "label": "Production tier 2: advise", "tierKey": "tier-2", "changeTypeKey": "*", "targetStageKey": "production", "gateLevel": "advise" },
      { "key": "gp-prod-tier-3", "label": "Production tier 3: enforce", "tierKey": "tier-3", "changeTypeKey": "*", "targetStageKey": "production", "gateLevel": "enforce", "failMode": "fail-closed" },
      { "key": "gp-emergency-test", "label": "Emergency in test: record", "tierKey": "*", "changeTypeKey": "emergency", "targetStageKey": "test", "gateLevel": "record" },
      { "key": "gp-emergency-prod-tier-1", "label": "Emergency in production, tier 1", "tierKey": "tier-1", "changeTypeKey": "emergency", "targetStageKey": "production", "gateLevel": "flag", "emergencyRule": { "passWithoutApproval": true, "approvalDueWorkingDays": 2, "reviewTypeKey": "post-change" } },
      { "key": "gp-emergency-prod-tier-2", "label": "Emergency in production, tier 2", "tierKey": "tier-2", "changeTypeKey": "emergency", "targetStageKey": "production", "gateLevel": "flag", "emergencyRule": { "passWithoutApproval": true, "approvalDueWorkingDays": 2, "reviewTypeKey": "post-change" } },
      { "key": "gp-emergency-prod-tier-3", "label": "Emergency in production, tier 3", "tierKey": "tier-3", "changeTypeKey": "emergency", "targetStageKey": "production", "gateLevel": "flag", "emergencyRule": { "passWithoutApproval": true, "approvalDueWorkingDays": 2, "reviewTypeKey": "post-change" } }
    ],
    "target-stage": [
      { "key": "development", "label": "Development", "rank": 1 },
      { "key": "test", "label": "Test", "rank": 2 },
      { "key": "production", "label": "Production", "rank": 3 }
    ],
    "change-path": [
      { "key": "pipeline-only", "label": "Pipeline only", "allowedRouteKeys": ["pipeline-step"] },
      { "key": "pipeline-or-ai-tool", "label": "Pipeline or AI tool", "allowedRouteKeys": ["pipeline-step", "ai-tool"] },
      { "key": "any-route", "label": "Any route", "allowedRouteKeys": ["pipeline-step", "ai-tool", "person-in-app", "other"] }
    ],
    "deployment-route": [
      { "key": "pipeline-step", "label": "Pipeline step", "engineMeaning": "pipeline-step" },
      { "key": "ai-tool", "label": "Owner's AI tool", "engineMeaning": "ai-tool" },
      { "key": "person-in-app", "label": "Person in the app", "engineMeaning": "person-in-app" },
      { "key": "other", "label": "Other, found later", "engineMeaning": "other" }
    ],
    "link-kind": [
      { "key": "ado-work-item", "label": "Azure DevOps work item", "linkCategory": "delivery", "toolKey": "azure-devops", "idPattern": "^[0-9]+$" },
      { "key": "github-issue", "label": "GitHub issue", "linkCategory": "delivery", "toolKey": "github", "urlPattern": "^https://github\\.com/[^/]+/[^/]+/issues/[0-9]+$" },
      { "key": "planner-plan", "label": "Planner plan", "linkCategory": "delivery", "toolKey": "planner" },
      { "key": "azure-pipelines-pipeline", "label": "Azure Pipelines pipeline", "linkCategory": "pipeline", "toolKey": "azure-devops" },
      { "key": "github-actions-workflow", "label": "GitHub Actions workflow", "linkCategory": "pipeline", "toolKey": "github", "urlPattern": "^https://github\\.com/[^/]+/[^/]+/actions" },
      { "key": "power-platform-pipeline", "label": "Power Platform Pipelines pipeline", "linkCategory": "pipeline", "toolKey": "power-platform" },
      { "key": "ado-wiki-page", "label": "Azure DevOps wiki page", "linkCategory": "document", "toolKey": "azure-devops" },
      { "key": "repository-file", "label": "Repository file", "linkCategory": "document", "toolKey": "github" },
      { "key": "sharepoint-page", "label": "SharePoint page", "linkCategory": "document", "toolKey": "sharepoint", "urlPattern": "^https://[^/]+\\.sharepoint\\.com/" },
      { "key": "evidence-url", "label": "Evidence link", "linkCategory": "evidence", "toolKey": "other" },
      { "key": "power-platform-resource", "label": "Power Platform resource", "linkCategory": "microsoft-resource", "toolKey": "power-platform" },
      { "key": "entra-object", "label": "Entra object", "linkCategory": "microsoft-resource", "toolKey": "entra" },
      { "key": "itsm-change", "label": "ITSM change", "linkCategory": "external-change", "toolKey": "itsm" }
    ],
    "actor-kind": [
      { "key": "person", "label": "Person", "engineMeaning": "person" },
      { "key": "ai-client", "label": "AI client for a person", "engineMeaning": "ai-client-for-person" },
      { "key": "agent-identity", "label": "Agent identity", "engineMeaning": "agent-identity" },
      { "key": "pipeline", "label": "Pipeline", "engineMeaning": "pipeline" },
      { "key": "approval-runner", "label": "Approval runner", "engineMeaning": "approval-runner" },
      { "key": "engine", "label": "Engine", "engineMeaning": "engine" }
    ],
    "guideline": [
      { "key": "g1", "label": "G1 Keep one register", "number": "G1", "version": "1.0", "rule": "Keep one register of business applications; an inventory is not a catalog." },
      { "key": "g2", "label": "G2 Name the owners", "number": "G2", "version": "1.0", "rule": "Every app names business, technical and data owners." },
      { "key": "g9", "label": "G9 Ship what you tested", "number": "G9", "version": "1.0", "rule": "Ship the zip you tested." },
      { "key": "g17", "label": "G17 No AI approves", "number": "G17", "version": "1.0", "rule": "No AI approves its own work, or anyone's." }
    ],
    "separation-rule": [
      { "key": "no-self-approval-tier-3", "label": "No self-approval at tier 3", "ruleKind": "approver-differs", "tierKey": "tier-3", "mustDifferFrom": "proposer", "enforcement": "refuse" },
      { "key": "deployer-not-sole-approver", "label": "Deployer is not the only approver", "ruleKind": "approver-differs", "decisionTypeKey": "change-authorization", "targetStageKey": "production", "mustDifferFrom": "deployer", "enforcement": "flag" }
    ]
  }
}
```

Checks a reader can make against section 9: three active tiers (V11, V12); every tier question has three answers (V13); stages cover pre-live, live and retired, with unique numbers and ranks (V14); every status contract is complete (V15); the broad approval rules give every tier approval rules for each decision type that requires approval, and the six types without approval rules resolve an accountable person role or owner role at every tier (V19, V22); configuration and gate policy changes have rules naming the configuration admin (V20, V32); the tier 3 emergency rows exist for decision rights, approval rules and gate policies, so no two rule groups tie, and the board's exception rows carry no tier, so they tie with nothing (V26, V09); no minimum count is 0 (V28); every setting of section 3.6 is present and the two locked ones are locked (V33, V38); the gate wildcard rows expand to all 27 combinations of 3 tiers, 3 change types and 3 target stages with exactly one row each (V23); the only enforce row has a fail mode (V24).

---

## 13. New terms (for document 20)

Proposed additions. The glossary owner adds them; this document uses them meanwhile.

### 13.1 Terms

| Term | Definition | From |
|---|---|---|
| Full preset | A shipped preset that is a complete configuration set | D-07 |
| Overlay | A shipped partial preset: a patch that changes some rows of a full preset by key, covering one area such as the people model | D-07, D-20 |
| Small team (overlay) | Preset key `small-team`. Lets one group carry several roles, needs no change board, and allows self-approval at the top tier only with an accepted exception decision | D-20 |
| Strict separation (overlay) | Preset key `strict-separation`. Requires different people for proposer and approver, instructing and accepting admin, and the AI lead and platform admin groups; two board members for a change exception | D-20 |
| Linked change management (overlay) | Preset key `linked-change-management`. Sets the change management mode to linked | D-08 |
| Configuration Draft | A proposed change to the working set, held as a patch on C46 until a person accepts it. Never read to judge a record | D-10; 28, issue 3 |
| Draft status | Draft, submitted, activated or discarded, on a Configuration Draft | D-07, D-10 |
| Version state | Active or superseded. Derived from the version number, never stored, because C02 rows never change | D-07, D-10 |
| Engine acceptance list | The locked setting `engine-acceptable-decision-types`: the only decisions the engine may accept without a person's card (working default, open design question 8) | D-02; 28, issue 1 |
| Approval runner | The flows that send approval and confirmation cards, wait and write outcomes back. Its actor kind meaning is "approval runner" | 28, issue 7 |
| Approval writer | The workload role the approval runner holds through a workload grant. The only writer of approval outcomes | 28, issue 7 |
| Reader | A read-only human role: the register, decisions and the change schedule, without audit entries or restricted columns | 28, issue 7; document 26 |
| Change exception | An exception decision about a change request. The seed sends it to the change board | 28, issue 4 |
| Patch | An ordered list of upsert and retire operations by entity and key. Used by overlays, AI drafts, configuration files and client binding patches | D-10 |
| Bundle | A whole configuration set in the seed format: manifest plus one array per entity | D-10 |
| Base version | The active version a draft started from | D-10 |
| Rebase | Re-applying a draft's operations on a newer active version when no operation overlaps | D-10 |
| Compiled snapshot | The active version's content, compiled into lookup structures and cached by the engine | D-10 |
| Version probe | The one small read of the active version number at the start of every governed request | D-10 |
| Rule group | Rows of one rule entity with the same decision type and the same scope values. The single most specific matching group applies whole, and broader groups drop out. Separation rules group by rule kind and subject too | D-07; 28, issue 2 |
| Specificity | The number of scope fields a rule group sets | D-07 |
| Protected change | A change to who may change configuration; approved under the active version's rules | D-10 |
| Safety floor | An engine contract rule no configuration or preset can lower (section 5.4) | D-02, D-10 |
| Seed package | The release's preset bundles staged in the store before they are applied: a web resource on Dataverse, a `config.SeedPackage` row on Azure | D-10, D-22 |
| Upgrade draft | The draft a product upgrade proposes from a three-way comparison of old seed, new seed and the working set | D-10 |
| Client binding patch | The setup service's patch that binds product roles to the client's Entra groups. Never public | D-11 |
| Configuration health | The read tool `get_configuration_health`: the active version, its hash, version history, waiting drafts, engine information and the last load error | D-10; 28, issue 11 |
| Separation rule | See C40 | D-20 |

### 13.2 New entities

| ID (proposed) | Logical name | Purpose | Class | When |
|---|---|---|---|---|
| C40 | Separation Rule | Who must not be the same person for an approval, or which roles may not share an Entra group. The one shape for separation of duties (28, issue 5) | CONFIGURATION | R1 |
| C46 | Configuration Draft | A proposed change to the working set, held as a patch until activation, on both bindings (28, issue 3). Document 20, section 2.10 reserves and adopts C46 | CONFIGURATION, history class F (freeze on final) | R1 |

### 13.3 New columns

| Entity | Column | Description |
|---|---|---|
| C02 | Configuration Draft | The draft this version activated |
| C02 | Change Json | The patch as applied, after any rebase |
| C02 | Snapshot Schema Version | The seed format version of Content Json (document 25) |
| C02 | Validation Json | The validation result at activation |
| C02 | Applied Presets Json | The full preset and overlays applied, with content versions |
| C03 | Preset Kind, Extends, Conflicts With, Areas, Content Version, Schema Version, Setup Notes Json, Checked On | See section 3.3 |
| C04 | Narrows Setting, Portfolio | A portfolio row that narrows `change-management-mode` (28, Q2) |
| C05 | Is Default For Meaning | The label the engine picks for a meaning |
| C12 | Stage Rank | The stage order logic uses (document 25) |
| C14 | Required Owner Role | For the requirement kind owner assigned |
| C18 | Requires Approval | True when the approval rules decide who approves; false sends one card to the accountable holder (28, issue 1) |
| C20 | Requires Standard Model | True when a change of this type must name a standard change model |
| C22 | Current Needs Person | True when "current" set by an AI client needs a confirmation card (document 23) |
| C32 | Is Consultation | True when the answer is a recommendation only and never counts (document 25) |
| C33 | Flag Notify Json | Who is told about a flag on this row |
| C46 | Record Number, Configuration Set, Draft Status, Base Version Number, Change Json, Preview Content Json, Validation Json, Change Summary, Proposal Rationale, Applied Presets Json, Instructed By, Change Decision, Submitted On, Expires On, Resulting Version, Discarded On, Discard Reason (the names of document 20) | See section 3.3 |

### 13.4 New engine contract lists and values

| Contract | Fixed meanings | Where used |
|---|---|---|
| Draft status meaning | draft, submitted, activated, discarded | C46 |
| Engine acceptance condition | lowest tier (the active tier with the lowest rank), standard model (the change names an active standard change model whose conditions hold) | Setting `engine-acceptable-decision-types` |
| Preset kind | full, overlay | C03 |
| Patch operation | upsert, retire | Patches |
| Requirement kind (added value) | owner assigned (added to decision accepted, document current, review done) | C14 |
| AI-only code approval handling | ignore, flag, stop | C33, setting (the values document 20 lists in the column description, made a contract) |
| Separation rule kind | approver differs, roles in different groups | C40 |
| Separation subject | proposer, instructing admin, deployer, owner role holder, role holder | C40 |
| Separation enforcement | refuse, exception decision, flag | C40 |
| Setting value type | text, number, yes-no, json, key, contract | C04 |
| Setting value lists owned by other documents | intake visibility (own, all) and claim visibility (admins and claimant, all), document 22; record access mode (open read, portfolio read), document 26 | Section 3.6 |

### 13.5 Narrowed rule and seed rows

- **Child keys:** for C07 List Value, C10 Tier Answer Option and C19 Decision Outcome, the key is unique within the parent, not across the entity (document 20, section 2.3 says per entity). Without this, common values such as `other`, `accept` or `low` would need artificial prefixes. Alternate key: Key plus parent plus Configuration Set.
- **Settings:** section 3.6 is the one list (28, issue 15). Added by this document: `engine-acceptable-decision-types`, `draft-expiry-days`, `configuration-review-days`, `default-flag-notify`, `default-ai-only-code-approval-handling`. Listed from other documents: `confirmation-channel`, `unlinked-deployment-change-type` (23); `intake-visibility`, `claim-visibility` (22); `record-access-mode`, `agent-read-access`, `principal-refresh-days`, `forbidden-url-patterns`, `audit-retention-days`, `link-undo-minutes`, `workload-grant-max-days` (26); `impact-max-depth`, `mcp-page-size-max` (24); `engine-read-cache-seconds`, `working-days` (25). `history-retention-days` (22) is merged into `audit-retention-days`.
- **Roles:** one seed list, agreed with document 26 (28, issue 7): fourteen human roles (`requester`, `maker`, `application-owner`, `data-owner`, `architect`, `change-manager`, `change-board`, `pmo`, `ai-lead`, `security-reviewer`, `platform-admin`, `configuration-admin`, `auditor`, `reader`) and two workload roles (`deployment-recorder`, `approval-writer`).
- **Seed rows beyond document 20's defaults:** roles `security-reviewer` and `reader`, workload role `approval-writer` (default preset; 28, issue 7); review type `security-review` (default preset); the board's exception rows on decision type `exception`, one per shipped change type (28, issue 4); consulted decision rights for the AI lead and the platform admin on configuration changes, and a supports row for gate policy changes (28, issue 6); for the Microsoft CoE baseline only: role `coe-lead`, owner roles `domain-validator` and `product-owner`, review types `release-review`, `quality-review`, `maturity-review`, `responsible-ai-assessment`, document types `monitoring-plan`, `release-checklist`, `quality-monitoring-plan`, `sla-monitoring-plan`, `incident-response-plan`, vocabulary list value `agent-coe-role`.

---

## 14. Findings for other step 2 documents

| For | Finding |
|---|---|
| Document 20 (glossary) | Its UNVERIFIED item "whether a Dataverse multiline text column offers a JSON format option" is now answered: the format exists in the API but is "Only allowed for non-SQL data provider" [T2]. JSON columns on Dataverse are plain multiline text, validated by the engine |
| Document 20 (glossary) | C14's requirement kinds cannot express G2 ("every app names business, technical and data owners") without the owner assigned kind (section 13) |
| Document 20 (glossary) | Working defaults to record: C46 Configuration Draft as the next proposed ID; the most specific group rule on C32 (28, issue 2); the "Change board" line, now exceptions and windows that cover several applications (28, issue 4); the C29 seed list with `reader`, `security-reviewer` and `approval-writer` (28, issue 7); the terms approval runner and approval writer |
| Document 22 (data model) | C02 is class A and created only at activation; C46 holds drafts on both bindings (28, issue 3). New columns in section 13.3, and `history-retention-days` merged into `audit-retention-days` (28, issue 15) |
| Engine document | The activation custom API, the guard plug-in on configuration tables, C02 and C46 (ParentContext check UNVERIFIED), staged activation for large changes within the 2-minute message limit [T9], the compiled snapshot and probe (section 10). OP-02 uses the group rule of section 10.3 and has no Minimum Count 0; the locked list works through configuration facts, not a count (section 10.3); OP-03 reads C40; no fallback for a missing gate policy row (28, issues 1, 2, 5, 14) |
| MCP surface document | Configuration tools: `get_configuration`, `explain_rules`, `propose_configuration_change` (validate-only returns the diff and the effect on live applications), `withdraw_record` with a configuration subject kind, `get_configuration_health`, `export_configuration` (28, issue 11). Drafts are C46 rows, not C02 rows. No activate tool. Keys, never IDs, for configuration values |
| Check step document (after D-03) | Every answer carries the row's fail mode and the version; the step keeps the last fail mode per target (section 7.4) |
| Process diagrams | The diagram of a configuration change made by AI on an admin's instruction follows section 5.1 |
| Spikes | Upgrade keeps local rows (section 4.9); plug-in speed with the cached snapshot and the per-request probe; ParentContext guard |

---

## 15. Microsoft sources

"Checked 2026-11-22" means read on Microsoft Learn through the Microsoft Learn MCP tools on that date. S-rows were checked by document 20 on 2026-11-22; S2 was fetched again for this document.

| ID | Fact used | URL | Checked |
|---|---|---|---|
| S1 | Architecture decision records list options considered; append-only | https://learn.microsoft.com/azure/well-architected/architect-role/architecture-decision-record | 2026-11-22 (document 20) |
| S2 | Three tiers "Tier 1: Low risk (individual productivity agents)", "Tier 2: Medium risk (expert-knowledge and internal service agents)", "Tier 3: High risk (business-critical and external-facing agents)", with the required controls quoted in section 6.4; the front door recommendation is "an initial assessment, not approval" | https://learn.microsoft.com/agents/center-of-excellence/govern-agents-risk | 2026-11-22 (fetched again) |
| S4 | Support tiers Productivity, Important, Critical | https://learn.microsoft.com/power-platform/guidance/adoption/support-strategy-solutions | 2026-11-22 (document 20) |
| S5 | Copilot Studio zones 1 to 3, with the labels in section 12 | https://learn.microsoft.com/microsoft-copilot-studio/guidance/sec-gov-phase2 | 2026-11-22 (document 20) |
| S6 | What the Power Platform inventory lists | https://learn.microsoft.com/power-platform/admin/power-platform-inventory | 2026-11-22 (document 20) |
| S7 | Group team membership cached; changes at next sign-in or after 8 hours | https://learn.microsoft.com/power-platform/admin/manage-group-teams | 2026-11-22 (document 20) |
| S9 | Group-based app role assignment needs Entra ID P1 or P2; no nested groups | https://learn.microsoft.com/entra/identity/enterprise-apps/assign-user-or-group-access-portal | 2026-11-22 (document 20) |
| S10 | Approval types: everyone must approve, first to respond, sequential | https://learn.microsoft.com/power-automate/get-started-approvals | 2026-11-22 (document 20) |
| S15 | Alternate keys: text and lookup columns, up to 10 keys per table | https://learn.microsoft.com/power-apps/developer/data-platform/define-alternate-keys-entity | 2026-11-22 (document 20) |
| S16 | Audit holds the calling user for impersonated calls | https://learn.microsoft.com/power-apps/developer/data-platform/auditing/retrieve-audit-data | 2026-11-22 (document 20) |
| S17 | SQL identifiers 1 to 128 characters | https://learn.microsoft.com/sql/relational-databases/databases/database-identifiers | 2026-11-22 (document 20) |
| S18 | Append-only ledger tables allow only inserts and cannot be reverted | https://learn.microsoft.com/sql/relational-databases/security/ledger/ledger-append-only-ledger-tables | 2026-11-22 (document 20) |
| S19 | System-versioned temporal tables keep history | https://learn.microsoft.com/sql/relational-databases/tables/temporal/overview | 2026-11-22 (document 20) |
| S20 | The `json` data type is GA in Azure SQL Database | https://learn.microsoft.com/sql/t-sql/data-types/json-data-type | 2026-11-22 (document 20) |
| T1 | Agentic CoE sample RACI (roles Executive sponsor, Business owner, CoE lead, Agent product owner, Platform and operations, Security and risk, Makers; tasks quoted in section 6.4), "This matrix is a starting point, not a standard", "Assign exactly one accountable role per task", and the CoE and domain decision rights lists | https://learn.microsoft.com/agents/center-of-excellence/roles-responsibilities | 2026-11-22 |
| T2 | Memo (multiline) JSON format: "Only allowed for non-SQL data provider"; text JSON format: "Only in non-SQL stores like Audit" | https://learn.microsoft.com/power-apps/developer/data-platform/format-and-formatname-columns ; https://learn.microsoft.com/power-apps/developer/data-platform/data-type-format-conversions | 2026-11-22 |
| T3 | Multiline text accepts up to 1,048,576 characters | https://learn.microsoft.com/power-apps/maker/data-platform/types-of-fields | 2026-11-22 |
| T4 | Environment variable data types (Decimal number, Text, JSON, Yes/No, Data source, Secret); current value and default value; values entered at import; a value in a managed solution is an unmanaged record; canvas apps and flows may take up to an hour to get a changed value | https://learn.microsoft.com/power-apps/maker/data-platform/environmentvariables ; https://learn.microsoft.com/power-automate/environment-variables | 2026-11-22 |
| T5 | Block unmanaged customizations blocks "Adding unmanaged changes to existing managed components"; "Change of environment variable" stays allowed | https://learn.microsoft.com/power-platform/alm/block-unmanaged-customizations | 2026-11-22 |
| T6 | A Package Deployer package can hold solutions, Configuration Migration data files, and custom code that runs before, while or after deployment | https://learn.microsoft.com/power-platform/alm/package-deployer-tool | 2026-11-22 |
| T7 | The Configuration Migration tool matches records by a uniqueness condition and can disable plug-ins during import | https://learn.microsoft.com/power-platform/admin/manage-configuration-data | 2026-11-22 |
| T8 | The platform caches plug-in instances for a time it manages; several threads can run one instance; read-only, static and constant members are thread-safe | https://learn.microsoft.com/power-apps/developer/data-platform/best-practices/business-logic/develop-iplugin-implementations-stateless | 2026-11-22 |
| T9 | A message operation, including synchronous plug-ins, must complete within 2 minutes; keep a plug-in under 2 seconds | https://learn.microsoft.com/power-apps/developer/data-platform/analyze-performance | 2026-11-22 |
| T10 | ParentContext: "the execution context from the parent pipeline operation", populated for stage 20 or 40 when a request is internally generated | https://learn.microsoft.com/dotnet/api/microsoft.xrm.sdk.ipluginexecutioncontext.parentcontext | 2026-11-22 |
| T11 | The `json` type accepts only an object or array; constraints can be part of the column definition; `ISJSON` CHECK constraints validate JSON text | https://learn.microsoft.com/sql/t-sql/data-types/json-data-type ; https://learn.microsoft.com/azure/azure-sql/database/json-features | 2026-11-22 |
| T12 | SqlPackage Publish incrementally updates a schema to match a .dacpac; a post-deployment script runs after the deployment plan, every time the project is deployed | https://learn.microsoft.com/sql/tools/sqlpackage/sqlpackage ; https://learn.microsoft.com/sql/tools/sql-database-projects/concepts/pre-post-deployment-scripts | 2026-11-22 |

**UNVERIFIED in this document:** whether ParentContext reliably identifies the activation custom API for every configuration table write (engine spike); whether one Power Automate approval can complete on exactly two of many approvers (section 6.6); that no Microsoft page joins the three tier vocabularies (an absence); the Work IQ action ceiling and the maker credentials sharing control named in the CoE baseline setup notes (sweep proposal, not re-checked); the stateless MCP specification date (sweep proposal, not re-checked).

---

## 16. Diagram specs

All diagrams use the diagram style of D-04: navy `0A2A4A` for text and lines, blue `0071DC` for group header bars, gold `FFC221` as the accent. Fills are light only: white `FFFFFF`, light blue `E8F1FB`, light gold `FFF4D6`, light gray `F3F5F8`. No dark fills. The diagrams are drawn in Visio.

### D21-1 A configuration version's life

- **Title:** From draft to active: the admin decides, the AI drafts, the engine activates.
- **Purpose:** Shows section 4.2 and section 5.1 as one flow, with the draft status and version state at each step.
- **Lanes:** Admin (person); AI client (on the admin's behalf); The product's MCP server; Engine; Approval runner and card; Configuration store.
- **Nodes:**
  1. Admin: "Says what should change".
  2. AI client: "Reads active configuration and explains the rule".
  3. AI client: "Starts a draft, adds operations by key".
  4. MCP server: "Calls the engine as the admin".
  5. Engine: "Checks the configuration role through the Entra group".
  6. Engine: "Validates; computes diff, hash, summary".
  7. Store: "Configuration Draft: draft" (light gold badge).
  8. AI client: "Submits with a rationale" (draft: submitted).
  9. Engine: "Records the configuration change decision: Proposed".
  10. Approval card: "Configuration admin approves the engine's diff".
  11. Engine: "Rebases if needed, revalidates, activates".
  12. Store: "New version n+1 written (append-only); version n now superseded; draft: activated".
  13. Store side branch: "Draft: discarded" (on withdraw, reject or expiry).
- **Edges:** 1 to 2 "instruction"; 2 to 3 "draft"; 3 to 4 "tool call"; 4 to 5 "on behalf of the admin"; 5 to 6 "allowed"; 6 to 7 "saved"; 6 to 3 "errors to fix" (dashed); 7 to 8 "valid"; 8 to 9 "submit"; 9 to 10 "card"; 10 to 11 "approved by a person"; 11 to 12 "in force"; 10 to 13 "rejected" (dashed). A dotted gold edge from the AI client lane to node 10 labeled "AI cannot approve or activate".

### D21-2 How a working set is composed

- **Title:** One full preset, any compatible overlays, then the client's own changes.
- **Purpose:** Shows section 6.7 and the preset list of section 6.2.
- **Groups:** Shipped presets (light blue): Balanced, Starter (extends Balanced), Regulated (extends Balanced), Microsoft CoE baseline (extends Balanced, marked "sweep proposal, ships by working default"). Overlays (light gold): Small team, Strict separation, Linked change management. The client (white): "Version 1 of the working set", "Client binding patch", "Later changes, version 2 and up".
- **Nodes:** each preset and overlay; "Engine: build, then validate as one set"; the three client nodes.
- **Edges:** Starter, Regulated, CoE baseline to Balanced "extends"; one full preset to the engine node "pick one"; each overlay to the engine node "apply in order"; Small team to Strict separation "conflicts" (red is not allowed; use a dashed navy line with a gold "X" marker); engine node to "Version 1" "activate"; "Client binding patch" to "Version 1" "binds roles to Entra groups"; "Version 1" to "Later changes" "AI drafts, person accepts".

### D21-3 How the engine reads configuration

- **Title:** Probe, hit or load, pin, stamp.
- **Purpose:** Shows section 10.2 for two processes at once, so readers see that no push is needed.
- **Lanes:** Caller (app, MCP server, pipeline step); Engine process A; Engine process B; Configuration store.
- **Nodes:** "Request arrives"; "Probe: read current version number"; "Same as cache: use compiled snapshot"; "Different: load snapshot, check hash, compile, swap"; "Judge the write with that snapshot"; "Stamp version on the record"; in the store: "Configuration Set: current version number", "Version snapshot (contentJson)".
- **Edges:** "Request arrives" to "Probe" in both processes; "Probe" to the store's version number "one small read"; "Probe" to "Same as cache" "hit"; "Probe" to "Different" "miss"; "Different" to "Version snapshot" "one read"; both paths to "Judge"; "Judge" to "Stamp". A gold note: "Activation changes one number; every process sees it on its next request."

### D21-4 The gate ladder as configuration

- **Title:** One gate policy row per tier, change type and target stage.
- **Purpose:** Shows section 7 for the Balanced preset, and what each level does.
- **Groups:** Left (white): a grid with rows tier 1, tier 2, tier 3 and columns development, test, production, each cell a small box showing the level for normal changes; a second small grid for emergency changes. Right (light blue): five level boxes Off, Record, Flag, Advise, Enforce, each listing "approvals asked", "flags", "message", "check step answer", "can stop".
- **Fills for levels:** light gray for off, white for record, light blue for flag, light gold for advise, white with a gold border for enforce. No dark fills.
- **Edges:** from each grid cell group to its level box "uses"; from Enforce to a node "Client's own check step" labeled "stop, unless approvals complete or emergency rule"; from the check step to a gold note "fail mode travels with every answer".

### D21-5 From the release package to version 1, on both platforms

- **Title:** The seed is staged, then applied by the engine, never edited in place.
- **Purpose:** Shows section 11 side by side: the seed path on Dataverse and on Azure, meeting in the same engine apply step.
- **Groups:** Public repo build (white): "Per-entity preset files", "Validator (same as the engine)", "Flat bundles". Dataverse binding (light blue): "Managed solution with seed web resources", "Package Deployer custom code", "Activation custom API". Azure binding (light blue): "Schema migration (SqlPackage publish)", "Post-deployment script: config.SeedPackage", "Engine apply command". Shared (light gold): "Version 1 or upgrade draft", "Admin accepts (upgrade only)".
- **Edges:** "Per-entity preset files" to "Validator" "build"; "Validator" to "Flat bundles" "pass"; "Flat bundles" to both platform groups "ship in the release"; Dataverse: web resources to Package Deployer code "after import", code to activation API "apply"; Azure: migration to post-deployment script "after the plan", script to engine apply command "stage then apply"; both apply nodes to "Version 1 or upgrade draft"; that node to "Admin accepts (upgrade only)". A gold annotation: "Not used for configuration: in-place web resource edits, Configuration Migration tool imports, direct T-SQL inserts."

---

## 17. Open design questions

Only questions that no decision settles (the decision records are in [docs/decisions/](../../decisions/)). The open D-03 points (default preset after install, fail mode, who owns the policy table) are not repeated here. Document 20's three questions are now 28's questions 1, 7 and 2: questions 1 and 7 stay open design questions (section 3 of [28-working-defaults.md](28-working-defaults.md)), and question 2 is settled as a working default. This document's five questions map to 28 as follows: question 1 is 28 Q3, question 2 is 28 Q4, question 3 is 28 Q5, question 4 is 28 Q10, question 5 is 28 Q6.

### Question 1: May a client mix presets, or only pick one whole preset?

Settled as a working default in 28-working-defaults.md (Q3); it is not a decision and can still change.

### Question 2: When an admin instructs an AI to change configuration, does a separate acceptance follow?

- **Background.** D-10 says "only AI changes will be done", on an admin's instruction. It does not say whether the instruction itself is the approval, or whether the admin then accepts the exact change the AI made. An AI can misread an instruction, and the instruction is a chat message, not a record of what changed.
- **Example.** The admin says "make tier 2 stricter". The AI raises the tier 2 production gate to enforce and also adds the data owner to tier 2 go-live approvals. Did the admin approve both?
- **Options.**
  - **A. The admin accepts the engine's diff on an approval card (this document).** The instructing admin may accept their own AI's draft in the default presets; the strict separation overlay and the Regulated preset require a second admin. *Pros:* the record shows exactly what a person accepted; AI never activates anything; consistent with every other decision. *Cons:* one extra click per change.
  - **B. The instruction is the acceptance.** The AI's change takes effect when it validates. *Pros:* fastest. *Cons:* no person ever sees the actual diff; the audit shows a chat instruction, not an accepted change; an AI mistake goes live.
  - **C. Always two admins.** *Pros:* strongest control. *Cons:* blocks small teams, which D-20 explicitly allows for.
- **Recommendation.** A.
- **What the answer changes.** With B, the configuration change decision is accepted automatically by the engine on behalf of the instructing admin, which conflicts with "AI never approves" in spirit and needs its own safety floor. With C, the two-person separation rule moves from the strict separation overlay into the safety floors.
- **Status.** Open design question 4 (see [28-working-defaults.md](28-working-defaults.md), section 3). Until it is decided, this document uses option A as the working default.

### Question 3: When a product release changes the shipped defaults, what happens to a client's configuration?

Settled as a working default in 28-working-defaults.md (Q5); it is not a decision and can still change.

### Question 4: Is "nobody approves their own proposal at the top tier" a fixed floor, or configurable?

Settled as a working default in 28-working-defaults.md (Q10); it is not a decision and can still change.

### Question 5: Should the Microsoft CoE baseline preset ship in the first release?

Settled as a working default in 28-working-defaults.md (Q6); it is not a decision and can still change.

### Other open design questions that touch this document

Each is one of the six open design questions in [28-working-defaults.md](28-working-defaults.md), section 3. This document uses the recommended option as its working default and marks every use.

- **28 question 1:** a short list of fixed engine meanings under "never hardcoded" (rule 2, section 13.4).
- **28 question 7:** AI-only code approvals from client tools never count; the shipped handling is flag (C33, section 3.6).
- **28 question 8:** the locked setting `engine-acceptable-decision-types` (C18, C20, section 3.6, section 5.4).
- **28 question 9:** the `confirmation-channel` setting and C22 Current Needs Person (section 3.6).
- **28 question 12:** workload roles held through Workload Grants, never through C30 (C29, V35).

---

## Proposed by the build (2026-11-22)

- **Source.** The build in `src/` and its presets (`src/config/presets/`).
- **Status.** Every item below is a proposal. The sections above keep their meaning: no settled decision and no working default changes. Where an item touches one of the open design questions, it says so. None of the items is accepted yet. The open design questions are listed in [28-working-defaults.md](28-working-defaults.md).

### B21-1 Four more validation rules: V45 to V48

Document 25 (section 5.11) runs four engine checks beyond V01 to V44 and proposes them for this list. The build gives them IDs in that table's order. Each has a passing and a failing fixture, and none fires on a shipped preset. `snapshot.schema-unsupported` stays a compiler check with no V-ID.

| ID | Group | Rule | Severity |
|---|---|---|---|
| V45 | Safety floor | Can Change Configuration is set only on the configuration admin role (reason code `role.config-admin`). The engine knows that role by the fixed key `configuration-admin`, because documents 20, 22 and 26 say "only for the configuration admin role" (working default, open design question 1). Only active roles count | Error |
| V46 | Taxonomy | A standard change model belongs to a change type that is not emergency (`standard-model.type`). An emergency type is known by Allows Authorization After, the column V25 reads, not by the key `emergency` | Error |
| V47 | Values | Every condition in a model's Conditions Json uses a condition kind of the engine contract list (`standard-model.condition-kind`, OP-12). V01 and V39 also report an unknown kind; a build test keeps the schema's list equal to the contract's | Error |
| V48 | References | A vocabulary mapping points to an existing row (`vocabulary.source`), also when its source entity is not a configuration entity. A retired row still counts as existing | Warning |

### B21-2 Rebase as an operation (section 4.5)

- **Outcomes.** `same-base`, `rebased`, `invalid`, `conflict` and `invalid-input`. A conflict reuses V42's `config.base-moved`, so no new reason code is needed.
- **Always revalidated in activation mode,** so V34 judges a protected change by the active rules.
- **What an operation touches** is read more widely than the literal wording of section 4.5: wildcard expansions, the parent of a child row, the named children of a parent upsert, and every child of a parent retire. Each reading refuses more and never merges more.

### B21-3 Regulated lists small team as a conflict

- **Proposal.** The Regulated preset declares `conflictsWith: ["small-team"]`, which section 6 does not have (only small team against strict separation, sections 6.5 and 6.6).
- **Why.** Regulated refuses self-approval at every tier and retires `no-self-approval-tier-3` (section 6.3). The small team overlay upserts that same key with an exception decision (section 6.5). On Regulated that would give a retired key new content (V04), and it would weaken Regulated's "no self-approval at any tier" (28, Q10).
- **What it touches.** The working default for 28 Q3 (one full preset plus declared overlays) stays as it is, because conflicts are declared. But that question's example, a ten-person company with Regulated gates and the small team people model, is no longer a valid selection. V43 says "two overlays"; with this line it also covers a full preset that names an overlay.
- **Options.** A: keep the conflict. *Pros:* Regulated keeps its meaning, and no retired key comes back. *Cons:* a small team that needs Regulated gates takes Regulated's separation as it is, or picks another preset. B: drop the conflict and give the small team overlay a Regulated variant (an exception on `no-self-approval-all`, not an upsert of the retired key). *Pros:* question 3's example works. *Cons:* a second overlay variant to ship and test, and Regulated's floor gains an exception path.
- **Recommendation.** A for the first release; B later if a client asks.

### B21-4 The CoE baseline's two V29 warnings

Section 6.4 gives `quality-review` a cadence only at tier 2 and `maturity-review` only at tier 3, so V29 warns twice on this preset. The warnings do not block a build, and the build pins them as expected. Proposal: say in section 6.4 that the two warnings are expected, because Microsoft's guidance names each review at one tier. The other way is to add cadences at the other tiers, which Microsoft's guidance does not ask for.

### B21-5 Bootstrap, activation and submit: open points

- **The client binding patch after version 1** (section 4.8, step 4). After the bootstrap no product role is bound, so nobody holds Can Change Configuration through a group, and the engine cannot take the client binding patch as an ordinary change. The build's tests seed version 1 with the patch already applied. One way: let the bootstrap apply take the client binding patch with the presets, so version 1 already binds the roles. This is an open design point, together with the bootstrap entry point and decider (the build's setup guide S1, open point 1).
- **The bootstrap decision has no subject row.** O09 Subject Kind is required, and no C46 draft exists at bootstrap.
- **Install rights.** The bootstrap reads a proposed fact `hasInstallRights` from the platform, because no product role is bound before version 1.
- **A draft that cannot activate** (a rebase conflict, or an OP-09 error at activation) is discarded with that reason and no version is created. C46's Discard Reason lists rebase conflict; an error at activation would be one more reason. The accepted decision then has no version; this document could say what the decision shows.
- **Submit.** Document 25 says an error blocks submitting (sections 5.11 and 6.4, step 2), but no refusal code exists for it yet; the build discards such a draft at activation instead. The rule wants a code of its own.
- **Seed staging** (section 4.1, presets written by the engine when it stages a release) needs an effect kind of its own, because the guard refuses every write to a preset row (G-02).

### B21-6 Gate level off (section 7.2)

Section 7.2 says off still computes the window and path facts. Documents 25 (OP-04) and 23 say off stores no flag, and the build follows them. Proposal: the off row's "Deployment record flags computed" cell becomes "None".

### B21-7 Bundle field names and model column names (section 8.3)

Live configuration rows carry the bundle's field names (for example `roleKey`, `entraGroupObjectId`, `value`, `basedOnPresetKey`), while document 22 names the columns Role, Entra Group, Value Json and Based On Preset. The build does not column-check configuration rows for that reason; it refuses only a case variant of a model column, Key or Origin. Proposal: make the two name sets meet, or carry the bundle name in the generated column map, before an adapter maps configuration rows.

### B21-8 Seed rows

- **Two more workload roles** in the role seed, `authorization-reporter` and `consistency-checker`, marked as proposals (document 26, B26-1). Section 6.3's "two workload roles" would become four. Both rest on open design question 12.
- **The `estate` list** in the Balanced example (section 12) holds only `power-platform-inventory`, while documents 23 and 24 record the owner check under the estate `entra-owners`. Whether to add it is an open design question (open from the build, item 3, in [28-working-defaults.md](28-working-defaults.md), section 4).
