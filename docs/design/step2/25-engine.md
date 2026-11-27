# Step 2, document 25: the configuration-driven engine as a portable library

> **Platform decided 2026-11-22 (D-26).** The first release is designed to run in the client's own Azure subscription. The Azure adapter (Azure Functions over Azure SQL, with the API as the only write path) hosts the engine. D-26 replaces D-10's plug-ins and custom APIs with this library inside one service. The Dataverse binding (plug-ins and custom APIs) is kept as a possible later option. The library stays portable (net48 and net10.0) on purpose, so that option would need no rewrite. The text below was written while the data store was still open (D-22). It designs both adapters and is not rewritten.

- **Date:** 2026-11-22. **Status:** design draft, written client-neutral.
- **Step:** 2 of 3 (the design). This document designs the engine: the code that reads the configuration and enforces it on every write (D-10). It uses the names in `20-glossary-and-entities.md` exactly. Terms it needs that the glossary lacks are listed in section 16, "New terms".
- **Name:** the product has no name yet, so this file says "the product" or [Product name] (D-05). Code names use the placeholder `<Product>` and the Dataverse publisher prefix uses `<prefix>`.
- **Platform-neutral (D-22).** When this document was written, the data store was still open: Dataverse, or a database in the client's own Azure. So the engine is one plain .NET rules library with no Dataverse SDK and no database driver inside it. Two thin adapters bind it: a Dataverse adapter (plug-ins and custom APIs) and an Azure adapter (Azure Functions over Azure SQL). A rules engine written as a plain library behind a storage port is the hedge against a change of store, and it was the first step in choosing the foundation (D-22, D-26).
- **Follows:** decisions D-01 to D-22 (the decision records are in [docs/decisions/](../../decisions/)) and repeated checks against Microsoft's documentation. Where this file and a decision disagree, the decision wins.
- **Working defaults (alignment pass, 2026-11-22).** This file follows [28-working-defaults.md](28-working-defaults.md). A line marked "(working default, 28 issue n)" or "(working default, 28 Qn)" is adopted for the design and can still be changed. A line marked "(working default, open design question N; see 28-working-defaults.md)" uses the recommended option of one of the six open design questions (28, section 3). Four of them touch the engine: question 1 (fixed engine meanings), question 4 (the admin accepts the configuration diff on a card), question 7 (AI approvals from client tools) and question 8 (the locked list of decisions the engine may accept without a card). Section 19 lists them. The background and the options are in 28-working-defaults.md. Section 15 says what changes if a question is answered otherwise.
- **"AI never approves" in the product is decided** (D-02, D-10). The AI approval lock is a locked setting. Only AI approvals from client tools are still open (open design question 7; see 28-working-defaults.md).
- **Sweep proposals** adopted here are marked "(sweep proposal)". They come from a review of current practice and are proposals, not decisions.
- **Patterns** from governance practice are used as patterns only, generalized, each with a pattern ID (for example I-267). The 2021 tables carry no weight (D-22).
- **Microsoft facts** carry a source ID [L1] to [L26] (section 17, all checked 2026-11-22 on Microsoft Learn), or a glossary source ID such as [S15] (document 20, section 4). UNVERIFIED marks what no source confirmed.

**Ten rules the engine obeys.**

1. **Configuration decides, code enforces** (D-06, D-07, D-10). The engine never branches on a fixed tier number, stage number, label or count. It reads configuration rows by key and compares ranks.
2. **The only fixed vocabulary is the engine contract** (document 20, section 2.4). Every rule in this document is written against those meanings, never against labels (working default, open design question 1; see 28-working-defaults.md).
3. **AI never approves** (D-02, D-10). Only an approved outcome from a person, signed in as a user, who holds the required role, counts. No setting turns this off.
4. **Roles come only through Entra groups** (D-11). The engine receives the caller's product roles from the adapter, which resolves them from Entra group bindings (C30). It never reads a direct assignment as a role.
5. **Links, not copies** (D-15, D-17, D-19). The engine evaluates IDs, URLs, statuses and dates. It never fetches or stores another tool's content, and it makes no outbound calls.
6. **Pure core.** The rules library does no input or output, reads no clock and generates no random values. The same inputs and the same configuration snapshot always give the same answer.
7. **One engine, every path.** The app, both MCP routes, pages, the approval runner and pipeline steps reach the same rules. On Dataverse, rules sit on the table messages, not only in custom APIs. On Azure, the API is the only write path.
8. **Refuse in plain words.** Every refusal carries a stable reason code and one plain sentence a person or an AI client can act on.
9. **Always as the caller.** The engine never acts with more rights than the caller. Its own follow-on writes carry an engine-nested marker (working default, 28 issue 9 and Q15).
10. **No silent fallback.** A missing or broken configuration makes the engine refuse governed writes and report the problem. It never falls back to a built-in default (document 21, rule 8; working default, 28 issue 14).

---

## 1. In one screen

| Question | Answer |
|---|---|
| What is the engine? | A plain .NET library (the engine core and the engine runtime) plus two adapters. The core holds fourteen pure operations, from tier evaluation to configuration validation, standard change checks, gap check comparison and linked-mode authorization (section 5) |
| What does it target? | The source compiles once for `net48` (the build that goes into the Dataverse plug-in package) and `net10.0` (the Azure Functions build), with a `netstandard2.0` compile check. Section 4 explains why not `net462` and why not .NET 8 |
| How does it get configuration? | It reads only the active Configuration Set Version snapshot (C02 Content Json), never the live configuration rows. A change waits as a patch on a Configuration Draft (C46). The live rows and a new C02 row change only inside activation, after people accept the configuration change decision (section 6; working default, 28 issue 3) |
| How fast is it? | One cached snapshot per version, one version check per call, and one to three fact reads per evaluation. The target is well under Microsoft's 2-second guidance for synchronous plug-ins [L3] |
| How does it fail? | Rule outcomes are results, not exceptions. Refusals carry a reason code and map to `InvalidPluginExecutionException` on Dataverse and to problem details on Azure (section 7) |
| How are retries safe? | Every write operation takes an idempotency key. A replay returns the first result. A reused key with a different payload is refused (section 8) |
| How is it tested? | JSON fixtures per operation, every shipped preset validated in CI, invariant tests ("AI never counts"), and one fixture suite run against both adapters (binding parity, section 10) |
| How does Dataverse call it? | Synchronous plug-in steps on Create, Update and Delete (PreValidation guard, PreOperation stamp and compute, PostOperation effects), run as the caller, and 16 global custom APIs with simple parameter types only (section 11) |
| How does Azure call it? | One service on Azure Functions Flex Consumption (isolated worker, .NET 10) that hosts the API, the MCP server and the engine. Container Apps is the fallback if the on-behalf-of or MCP spike fails on Functions (working default, 28 issue 13). REST endpoints mirror the custom APIs one to one, over Azure SQL with the managed identity and session context for row-level security (section 12; working default, 28 Q20) |
| What does portability cost? | Ports, a logical-to-physical column map and the plan, fetch, evaluate split (section 3.3). If the store changes, the library and every fixture carry over, and only the other adapter is new (section 13) |

---

## 2. Why a portable library

| Reason | Source |
|---|---|
| D-22 reopened the data store. The rules must not be rewritten if the store changes | D-22 |
| D-10 makes rules configuration and the engine code. Neither D-10 nor D-22 needs the code to know which store holds the rows | D-10, D-22 |
| Unit tests without a Dataverse environment or a database. Rules are tested in milliseconds with JSON fixtures | Pattern I-185 (proof per stage, fixtures in development) |
| Auditors get one rule set to read, one version number per release, and one reason code catalog, whatever the store | D-07 (versioned configuration sets) |
| The open-source package already ships compiled plug-in code (D-10). A plain library is easier for adopters to read and trust than plug-in classes full of SDK calls | D-10 ("source sits in the public repo") |

What the hedge does not cover: the physical schema, migrations, the user interface, the MCP server and the approval runner are store-specific and belong to other step 2 documents.

---

## 3. Shape: core, runtime, adapters, hosts

### 3.1 Parts

| Part | Assembly (placeholder) | What it holds | Depends on |
|---|---|---|---|
| Engine core | `<Product>.Engine.Core` | The contract records, the fourteen pure operations, the reason code catalog, the snapshot model, canonical hashing | Nothing beyond the base class library |
| Engine configuration | `<Product>.Engine.Configuration` | Reads and writes snapshot JSON, applies a draft's patch to a base snapshot, rebases a patch, reads seed and preset files, computes the diff and the change summary | Engine core; `System.Text.Json`, packaged explicitly on Dataverse [L2] |
| Engine runtime | `<Product>.Engine.Runtime` | Orchestration: loads the snapshot through a port, asks the core which facts it needs, fetches them through a port, evaluates, returns a write plan. Holds the snapshot cache | Engine core, engine configuration |
| Engine testing | `<Product>.Engine.Testing` | Fixture runner, fixture schema, preset loader for tests, invariant generators | All of the above |
| Dataverse adapter | `<Product>.Engine.Dataverse` | Plug-in classes, custom API handlers, actor mapping from the execution context, logical-to-physical column map, port implementations over `IOrganizationService` | Engine runtime; Dataverse SDK assemblies already present in the sandbox [L2] |
| Azure adapter | `<Product>.Engine.Azure` | Port implementations over Azure SQL, actor mapping from the Entra token, problem details mapping | Engine runtime; a SQL client driver |
| Azure host | `<Product>.Api` | One service on Azure Functions Flex Consumption (.NET 10): the HTTP endpoints, the product's MCP server and the engine, in process. Container Apps is the fallback if the on-behalf-of or MCP spike fails on Functions. Spike S11 confirms the host (working default, 28 issue 13) | Azure adapter |

### 3.2 Dependency rules

| Rule | Why |
|---|---|
| The core references no Dataverse SDK, no SQL driver, no JSON library, no logging library | The core must build and run anywhere, including inside the plug-in sandbox, and its tests must need nothing installed |
| The core never calls a clock, `Guid.NewGuid` or a random source. Time arrives as `asOfUtc` in the context | Determinism and replay (section 9) |
| String comparisons are ordinal. Keys are already lowercase ASCII (document 20, section 3.2), so no culture rules apply | Same answer on every machine and locale |
| The core works on logical names (`Business Application`, `Window Start`). Only adapters know physical names | One rule set for both platforms (document 20, section 3.3) |
| Adapters never contain a rule. If an adapter needs an `if` about tiers, approvals, stages or gates, that `if` moves into the core | Rules tested once, enforced the same everywhere |
| No parallel execution inside the engine | Microsoft does not support multithreading in plug-ins [L7]; keeping it out of the core keeps both builds the same |

### 3.3 Plan, fetch, evaluate

The core is pure, so it cannot read rows. Each complex operation runs in three steps:

1. **Plan.** The runtime asks the core: "for this request and this snapshot, which facts do you need?" The core answers with a fact plan: a short list of typed queries, such as "accepted decisions of types `data-use` and `go-live` for application X" or "document links for application X".
2. **Fetch.** The adapter runs the queries through its port, batching them where it can (one to three reads in the usual case).
3. **Evaluate.** The core evaluates the request with the snapshot and the fetched facts, and returns an evaluation result and, for writes, a write plan.

Why: fixtures can hold the facts inline, so every rule is testable without a store; and the fact plan depends on configuration (a client with five gate requirements needs more facts than one with two), which the core knows and the adapter should not.

---

## 4. Target frameworks and host limits

### 4.1 What Microsoft Learn says (checked 2026-11-22)

| Fact | Source |
|---|---|
| "Dataverse supports any .NET assembly project that targets Microsoft .NET Framework 4.6.2 through 4.8 for use as a plug-in assembly." The tip says plug-in projects "should target .NET Framework 4.8" | [L1] |
| Plug-in projects "must target .NET Framework". An assembly "can't be larger than 16 MB". Without plug-in packages, assemblies must be signed. The Dataverse SDK assemblies "already exist in the server's sandbox runtime" | [L2] |
| Dependent assemblies travel in a NuGet plug-in package, stored in the `PluginPackage` table and extracted to the sandbox at run time. "Microsoft doesn't support ILMerge." Assemblies in a package need not be signed. "You can't change the name and version of the plug-in package (on the server) once created" | [L2] |
| Every project in a plug-in package must be SDK style. For an added class library, Learn says to start from a .NET or .NET Standard template and set `<TargetFramework>net462</TargetFramework>` | [L2] |
| "Don't depend on System.Text.Json": the sandbox copy "might not be the same version"; include it explicitly in the package | [L2] |
| Plug-in packages do not support custom workflow activities, on-premises environments or unmanaged code | [L2] |
| "A Dataverse message operation must complete within 2 minutes", including all registered plug-ins, with CPU and memory limits. "Limit the time your plug-in takes to execute to no more than 2 seconds" | [L3] |
| Plug-ins may call out over HTTP and HTTPS only; no localhost; no IP addresses, only DNS names | [L13] |
| The platform caches plug-in instances and may run one instance on several threads. "Read-only, static, and constant members are inherently thread-safe" | [L6] |
| To share code between .NET Framework and .NET, "your library should target .NET Standard 2.0". Consuming .NET Standard libraries from .NET Framework 4.6.1 has "several issues"; Microsoft recommends 4.7.2 or higher | [L21] |
| .NET Framework 4.6.2 reaches end of support on January 12, 2027 | [L22] |
| .NET 8 (LTS) ended servicing on November 10, 2026. .NET 10 (LTS) and .NET 11 (STS) are supported until November 2028 | [L23] |
| Azure Functions 4.x isolated worker supports .NET 11, .NET 10, .NET 9, .NET 8 and .NET Framework 4.8. "Support ends for the in-process model on November 10, 2026." .NET 10 or later on Linux needs the Flex Consumption plan, not Consumption | [L24] |
| An HTTP-triggered function has at most 230 seconds to respond, whatever the timeout setting. Flex Consumption's default function timeout is 30 minutes | [L25] |

### 4.2 What the design chooses

| Choice | Decision | Reason |
|---|---|---|
| Source language level | Plain C# that compiles for `net48`, `net10.0` and `netstandard2.0`. Immutable classes with get-only properties; no APIs newer than .NET Standard 2.0 in the core | One source, three builds |
| Dataverse build | `net48` for the plug-in project and for the library builds packed into the plug-in package | Learn's tip says 4.8 [L1]; 4.6.2 ends support on 2027-01-12 [L22], before the first release. Learn's package page shows `net462` in its example [L2]; that a `net48` dependent assembly loads from a package is UNVERIFIED (very likely; spike S1) |
| Azure build | `net10.0`, isolated worker, Flex Consumption on Linux | .NET 8 is out of support since 2026-11-10 [L23]. .NET 10 is the current LTS to November 2028 [L23] and is supported by the isolated worker [L24] |
| `netstandard2.0` | A compile check in CI, and a build others may consume from their own hosts. Not shipped into the plug-in package | Learn recommends .NET Standard 2.0 for shared code [L21] but the plug-in package guidance targets .NET Framework [L2]. Loading a `netstandard2.0` assembly inside the sandbox is UNVERIFIED and not needed |
| JSON | `System.Text.Json`, only in the configuration assembly, packed explicitly into the plug-in package | Learn says not to rely on the sandbox copy [L2]. The core stays free of it |
| Outbound calls | None. The engine never calls Graph, Entra, pipelines or any web service | Rule 5. The sandbox's HTTP rules [L13] therefore do not constrain the engine |
| Package name | Final only after the product name (D-05) | The plug-in package name and version cannot change once created [L2] |
| Number of plug-in types | Four classes (guard, compute, effects, API dispatcher) | Packages with hundreds of types import slowly [L2]; four is far from that |

---

## 5. The evaluation API

All operations are plain functions over plain records. Field names below are the contract layer's lowerCamelCase forms (document 20, section 3.3). Configuration references travel as keys; operational references travel as IDs.

### 5.1 Common records

**EvaluationContext** (passed to every operation)

| Field | Type | Description |
|---|---|---|
| configurationVersion | integer | The Configuration Set Version number the caller loaded |
| snapshotHash | string | The Content Hash of that version; proves which rules applied |
| asOfUtc | date-time, UTC | The moment the rules apply at. The core never reads a clock |
| actor | ActorInfo | Who or what is acting |
| correlationId | string | Ties every event of one call together (O24 Correlation Id) |
| clientApp | string | The MCP client or application named by the caller, such as "Claude Code" |

**ActorInfo**

| Field | Type | Description |
|---|---|---|
| principalId | string | The Entra object ID of the caller (O05 Entra Object Id) |
| principalKind | string | One principal kind meaning: user, group, agent identity, agent user, service principal or managed identity |
| actorKind | string | One actor kind meaning: person, AI client for a person, agent identity, pipeline, approval runner or engine |
| onBehalfOfPrincipalId | string | The person an AI client or pipeline acted for; empty when a person acted directly |
| roleKeys | list of keys | The product roles (C29) the caller holds now, resolved by the adapter from Entra group bindings (C30) |
| isEngineNested | boolean | True only when the write was made by the engine itself inside an earlier engine operation (section 11.2) |

**EvaluationResult** (returned by every operation)

| Field | Type | Description |
|---|---|---|
| outcome | string | One outcome word per operation, listed with each operation |
| payload | record | The operation's own output record |
| reasons | list of Reason | Why the outcome is what it is; empty when nothing needs saying |
| trace | list of RuleTrace | The configuration rows that decided the outcome |
| configurationVersion | integer | The version actually used |
| snapshotHash | string | The hash actually used |
| engineVersion | string | The library's semantic version, such as `1.4.2` |

**Reason**

| Field | Type | Description |
|---|---|---|
| code | string | A stable reason code, such as `approval.ai-actor-not-counted`. Never reused once retired |
| severity | string | info, warning or error |
| message | string | One plain sentence, no HTML (Learn: don't use HTML in plug-in error text [L8]) |
| entity | string | The logical entity the reason is about, such as `Approval Record` |
| ref | string | The configuration key or row ID the reason points at |

**RuleTrace**

| Field | Type | Description |
|---|---|---|
| entity | string | The configuration entity of the deciding row, such as `Approval Rule` |
| key | string | The deciding row's key |
| role | string | What the row did: matched, won, dropped (a broader group that lost to a more specific one), required, skipped |

### 5.2 Operations catalog

| ID | Operation | Kind | What it answers | Main configuration read |
|---|---|---|---|---|
| OP-01 | Evaluate tier | read | Which tier do these answers imply? | C08, C09, C10, C15 |
| OP-02 | Resolve required approvals | read | Which approvals and consultations does this decision need in this scope, or may the engine accept it without a card? | C18, C32, C31, C28, C29, C04 (engine acceptance list) |
| OP-03 | Evaluate approval status | read | Are the required approvals satisfied, and which answers count? | C32, C38, C40, C04 (AI approval lock) |
| OP-04 | Evaluate gate decision | read | At this gate level, what happens to this deployment? | C33, C20, C34, C35, C36, C04 |
| OP-05 | Check change window | read | Is this moment inside the change request's window? | none (dates only) |
| OP-06 | Evaluate documentation completeness | read | Which required documents are missing, draft or stale? | C22, C23, C12 |
| OP-07 | Evaluate gate readiness | read | Is every requirement of this lifecycle gate met? | C13, C14 |
| OP-08 | Guard a write | write rule | May this caller make this change, what must be stamped, and what follows? | C29, C05, C37, C38, C02, C46 |
| OP-09 | Validate a configuration set | read | Is this snapshot safe to activate, and what changed? Runs V01 to V44 | the whole snapshot |
| OP-10 | Walk impact | read | What breaks if this application changes or retires? | C17 |
| OP-11 | Compute due dates and reminders | read | When is the next review due, and who is reminded or escalated today? | C24, C25, C26 |
| OP-12 | Evaluate a standard change | read | Does this change fit a standard change model that a person approved? | C21, C20, C08 |
| OP-13 | Compare a gap check result | read, returns a write plan | Which items are unregistered, which registered components vanished, and which claims change? | C16, C07 (estate) |
| OP-14 | Evaluate linked-mode authorization | read | In linked mode, does the product know the external change was authorized? | C04 (change management mode), C31, C37 |

### 5.3 OP-01 Evaluate tier

**Input: TierRequest**

| Field | Type | Description |
|---|---|---|
| applicationKindKey | key | The application kind (C15); filters questions by Applies To Kinds |
| answers | list of {questionKey, answerKey} | The tier answers given (C09, C10) |
| proposedTierKey | key, optional | A tier a person proposes, to compare with the computed one |

**Rule.** Applicable questions are the active C09 rows whose Applies To Kinds is empty or contains the kind. Each answer implies a tier (C10 Implied Tier). The computed tier is the implied tier with the highest rank (the most serious answer wins). Unanswered applicable questions make the outcome `incomplete`. Triggers Json on chosen answers adds review types or gate requirements.

**Output: TierResult**

| Field | Type | Description |
|---|---|---|
| computedTierKey | key | The tier the answers imply |
| computedTierRank | integer | Its rank, for display and comparison |
| decisiveAnswers | list of {questionKey, answerKey} | The answers that implied the winning rank |
| missingQuestionKeys | list of keys | Applicable questions not yet answered |
| triggeredReviewTypeKeys | list of keys | Review types the answers add (C10 Triggers Json) |
| triggeredGateRequirementKeys | list of keys | Gate requirements the answers add |
| proposedIsLower | boolean | True when the proposed tier ranks below the computed tier; lowering needs a tier decision with a reason (glossary O08) |

**Outcomes:** `computed`, `incomplete`. The tier itself is set only by an accepted tier decision (OP-08 effect); AI may suggest, never set.

### 5.4 OP-02 Resolve required approvals

**Input: ApprovalScopeRequest**

| Field | Type | Description |
|---|---|---|
| decisionTypeKey | key | The decision type (C18) |
| tierKey | key, optional | Scope: the subject's tier |
| lifecycleStageKey | key, optional | Scope: the subject's stage |
| changeTypeKey | key, optional | Scope: the change type, for change authorization |
| applicationKindKey | key, optional | Scope: the application kind |
| targetStageKey | key, optional | Scope: the target stage, for deployment-related decisions |
| portfolioPath | list of IDs | The subject's portfolio and its ancestors, nearest first |
| standardChange | OP-12 result, optional | For change authorization: whether the change fits an approved standard change model |

**Rule (precedence: document 21, section 10.3; working default, 28 issue 2).**
1. Candidates are the active C32 Approval Rule rows for the decision type.
2. A **rule group** is the set of rows with the same decision type and the same scope values (tier, lifecycle stage, change type, application kind, target stage, portfolio).
3. A group matches when every scope column it sets equals the request's value. A portfolio scope matches when the portfolio is in the portfolio path.
4. **Specificity** is the number of scope columns a group sets. The single most specific matching group wins. Broader groups drop out: they never add approvers. Every rule in the winning group applies.
5. Between two matching groups that set the same scope columns and differ only in portfolio, the nearer portfolio in the path wins (one working set with an optional portfolio scope; working default, 28 Q2).
6. Any other tie is a configuration error. OP-09 refuses it before activation (V26, `rule-group.ambiguous`). If one ever reaches run time, the engine has no tie-break: it refuses with `rule-group.ambiguous` and reports a broken configuration (rule 10). Combined cases, such as a tier 3 emergency, have their own group in the seed.
7. **No matching group** (working default, 28 issue 1). Unless rule 8 gives `engine-accept`, the decision still goes on one card, to the holder of the accountable letter (C31) for the decision type in this scope. C31 groups resolve with the same rule. OP-02 returns one RequiredApproval with source `accountable`, mode first to respond and Minimum Count 1. V22 makes sure exactly one accountable exists. If none resolves at run time, the engine refuses and reports a broken configuration.
8. **Engine acceptance only for the locked list, never through a count** (working default, 28 issue 1; open design question 8; see 28-working-defaults.md). Minimum Count is always at least 1 (document 21, V28): there is no zero waiver. The engine accepts with no card only when the request fits an entry of the locked setting `engine-acceptable-decision-types` (document 21) and the active configuration asks no person's approval there. Each entry is a decision type plus an engine acceptance condition. The shipped entries: intake with condition `lowest-tier` (the subject's tier has the lowest active rank), which applies only when no counting approval rule group matches (intake has Requires Approval false in every shipped preset); and change authorization with condition `standard-model`, which applies when the change type has Requires Standard Model (C20) and OP-12 says the change fits an active model whose conditions hold and whose Approved By Decision is accepted. The standard-model check runs before group resolution, so a standard change never falls through to the broader change authorization groups. Then the outcome is `engine-accept`. When the change type has Requires Standard Model but OP-12 says the change fits no approved model, OP-02 refuses with `standard-model.does-not-qualify`: the change is not standard and needs another change type. A Minimum Count of 0 anywhere is an error (V28, `approval-rule.zero-count`), and at run time the engine refuses and reports a broken configuration. The setting is locked, so a configuration change cannot extend it. A client that wants a card for these cases changes ordinary configuration: approval rules for intake (Requires Approval, V19), or no Requires Standard Model on the change type. (A design review settled this reading on 2026-11-22 so that documents 20, 21, 22, 23 and 25 agree.)
9. **Consultations** (working default, 28 issue 12). A rule in the winning group with Is Consultation yes becomes a consultation, not a requirement. It sends a card that asks a consulted role for input. OP-03 never counts its answer, and it never holds a decision pending. A winning group of a decision type with Requires Approval must hold at least one rule that is not a consultation (V27, `approval-rule.consultation-only`). For a type without Requires Approval, a group may hold only consultations: their cards go out beside the one accountable card of rule 7.

**Output: ApprovalScopeResult**

| Field | Type | Description |
|---|---|---|
| requirements | list of RequiredApproval | The approvals that must be satisfied; empty only for `engine-accept` |
| consultations | list of RequiredApproval | The consultation cards to send; their answers are stored as recommendation only |
| acceptanceBasis | string, optional | For `engine-accept`: the list entry used (intake with `lowest-tier`, or change authorization with `standard-model`) and, for a standard change, the model's Approved By Decision |
| ruleGroupTrace | list of RuleTrace | The group that won and the groups that dropped out, with why. `explain_rules` returns this trace (document 24) |

**RequiredApproval**

| Field | Type | Description |
|---|---|---|
| source | string | `approval-rule` or `accountable` (rule 7) |
| ruleKey | key, optional | The Approval Rule, when the source is a rule |
| decisionRightKey | key, optional | The Decision Right row, when the source is `accountable` |
| approverRoleKey | key, optional | The product role that must approve, when organization-wide |
| approverOwnerRoleKey | key, optional | The owner role that must approve, when relative to the application |
| isConsultation | boolean | True for a consultation (rule 9) |
| approvalMode | string | everyone must approve, first to respond or sequential (engine contract) |
| sequence | integer | Order among sequential rules |
| minimumCount | integer | How many counting approvals are needed; at least 1 |
| timeoutDays | integer | Days before the request expires |
| requiresTypedReason | boolean | Whether the approver must type a reason |
| guidelineKey | key | The guideline the rule applies |

**Outcomes:** `required` (at least one card), `engine-accept` (locked list only). There is no "none required" outcome: outside the locked list a person always answers.

**Tests.** A tier 3 emergency change matches the combined group only, and the broader tier 3 and emergency groups drop out. A decision type with no rule group goes to its accountable holder. Minimum Count 0 on any group is refused by OP-09 (V28) and, if forced into a snapshot, refused at run time. Lowest-tier intake with no counting rule group gives `engine-accept`; tier 2 intake goes to its accountable holder; lowest-tier intake gets a card once a client gives intake an approval rule. A standard change gives `engine-accept` only when OP-12 qualifies, never falls through to the business owner group, and otherwise returns `standard-model.does-not-qualify`. A group made only of consultations is refused for a type with Requires Approval. Two equally specific groups give `rule-group.ambiguous`, never a silent winner.

### 5.5 OP-03 Evaluate approval status, with the approver kind rule

**Input: ApprovalStatusRequest**

| Field | Type | Description |
|---|---|---|
| requiredApprovals | list of RequiredApproval | From OP-02, recomputed with the decision's stamped configuration version. Consultations are passed too, so they can be shown |
| records | list of ApprovalFact | One per Approval Record (O10) for the decision |
| scope | {decisionTypeKey, tierKey, targetStageKey} | The scope that selects C40 Separation Rule rows |
| parties | SeparationFacts | The people the separation rules compare with the responder |
| acceptedExceptionDecisionTypeKeys | list of keys | Decision types with an accepted exception decision linked to this decision (fetched) |

**SeparationFacts**

| Field | Type | Description |
|---|---|---|
| proposerPrincipalId | string | The person who proposed the decision. For an AI-drafted decision, the person it acted for |
| instructingAdminPrincipalId | string, optional | For a configuration change: the admin who instructed it |
| deployerPrincipalIds | list of strings | For change authorization: the people who started deployments of the change, once known |
| ownerRoleHolders | map of owner role key to principal IDs | Current holders of owner roles for the application (O06) |
| roleHolders | map of role key to principal IDs | Current holders of product roles that a matching C40 row names |

**ApprovalFact**

| Field | Type | Description |
|---|---|---|
| approvalRecordId | ID | The Approval Record row |
| ruleKey | key | The rule that required it |
| responderPrincipalId | string | Who answered |
| responderActorKind | string | The actor kind meaning of the answer |
| responderPrincipalKind | string | The directory object type of the responder |
| responderHoldsRequiredRole | boolean | Supplied by the adapter from the approval runner's record: the responder held the required role or owner role when answering |
| outcome | string | The approval outcome meaning |
| hasTypedReason | boolean | Whether a reason was typed |
| respondedOnUtc | date-time | When it was answered |
| expiresOnUtc | date-time | When it expires |

**The approver kind rule (sweep proposal).** An approval record counts toward a requirement only when all of these hold. Each failed test returns its reason code.

| Test | Reason code when it fails |
|---|---|
| Outcome meaning is approved | `approval.not-approved` |
| Actor kind meaning is person | `approval.ai-actor-not-counted` (AI client, agent identity), `approval.workload-not-counted` (pipeline, approval runner, engine) |
| Principal kind is user | `approval.non-user-principal` (agent identity, agent user, service principal, managed identity, group) |
| The responder holds the required role or owner role | `approval.not-role-holder` |
| A typed reason exists when the rule requires one | `approval.reason-missing` |
| It was answered before it expired | `approval.expired` |
| It is not a consultation | `approval.consultation-not-counted` (info) |
| It passes every matching C40 Separation Rule (below) | `approval.separation-refused`, `approval.separation-exception-missing` |

A "recommendation only" outcome (an AI stage in a client's flow, or the answer to a consultation card) is kept and shown, and never counts. The AI approval lock (C04 Setting, locked on, D-02 and D-10) cannot switch the rule off: OP-09 refuses any snapshot where the lock is off (V33, `setting.ai-approval-lock-off`).

**The separation test (working default, 28 issue 5).** OP-03 reads C40 Separation Rule rows from the snapshot. Columns on Approval Rule are not used for separation. A row matches when its rule kind is "approver differs" and its optional decision type, tier and target stage equal the scope (empty means any). For each matching row, the engine compares the responder with the people its Must Differ From names: the proposer, the instructing admin, the deployers, the holders of an owner role, or the holders of a role. When the responder is one of them, the row's enforcement applies:

| Enforcement | Effect on the record | Reason code |
|---|---|---|
| Refuse | Does not count | `approval.separation-refused` |
| Allow only with an accepted exception decision | Counts only when an accepted decision of the row's Exception Decision Type is linked to this decision | `approval.separation-exception-missing` when it is not |
| Allow and flag | Counts, with a warning on the decision | `approval.separation-flagged` (warning) |

Rows of kind "roles in different groups" are checked by OP-09 on the role group bindings (V36), not here. A deployer is often known only after the deployment. OP-04 in record mode therefore re-runs the test for the change's authorization decision and adds `approval.separation-flagged` to the deployment's reasons when the deployer was a counting approver under an "allow and flag" row. Preset defaults (working default, 28 Q10): Balanced refuses self-approval at tier 3. Regulated and the strict separation overlay refuse it at every tier. Starter and the small team overlay allow it only through a recorded exception decision.

**Mode rules.** Everyone must approve: satisfied when counting approvals reach Minimum Count and no counting rejection exists. First to respond: the first counting answer decides. Sequential: a requirement starts counting only when the previous sequence is satisfied. Consultations take no part in the mode rules.

**Output: ApprovalStatusResult**

| Field | Type | Description |
|---|---|---|
| overall | string | pending, satisfied, rejected or expired |
| requirements | list | Per RequiredApproval: state, counting record IDs, non-counting record IDs with reason codes |
| countsByRecord | map of ID to boolean | The value the engine writes into O10 Counts Toward Requirement |

**Outcomes:** `pending`, `satisfied`, `rejected`, `expired`. When the result is satisfied, OP-08 returns an accept-decision effect (document 23, S1 step 8).

**Tests.** An AI client's, an agent identity's and a pipeline's approval never count. A self-approval at tier 3 is refused under Balanced and counts under Starter only with a linked accepted exception decision. An "allow and flag" row counts the approval and returns the warning. A consultation answer of "approved" never counts and never blocks a satisfied decision. A responder who lost the required role before answering does not count.

### 5.6 OP-04 Evaluate gate decision

One operation serves both moments of the ladder (D-03): a check before a deploy (Advise and Enforce, through a check step, after the gate decision) and a record after a deploy (Record and Flag, through a record step, the owner's AI tool or the app).

**Input: GateRequest**

| Field | Type | Description |
|---|---|---|
| mode | string | `check` (before the deploy) or `record` (after it) |
| tierKey | key | The application's current tier |
| changeTypeKey | key, optional | The change type, when a change request is linked |
| targetStageKey | key | The deployment target's stage (C34) |
| portfolioPath | list of IDs | For portfolio-scoped gate policies |
| change | ChangeFact, optional | The linked change request's facts; empty when none is linked |
| routeKey | key | The deployment route (C36) the record arrived by |
| allowedRouteKeys | list of keys | The routes the target's change path (C35) allows |
| eventUtc | date-time | Planned start in check mode; finish time in record mode |
| codeApprovalKind | string | person, ai-only, none or unknown: who approved the code merge, as the record step reports it (sweep proposal). Derived from the reported approvals that `record_deployment` passes in; evidence only, never counted |

**ChangeFact**

| Field | Type | Description |
|---|---|---|
| changeManagementMode | string | built in or linked (engine contract), copied onto the change request at creation |
| requiresAuthorization | boolean | From the change type (C20 Requires Authorization) |
| allowsAuthorizationAfter | boolean | From the change type (C20 Allows Authorization After), true for emergencies |
| approvalOverall | string | Built-in mode: the OP-03 overall result for the authorization decision |
| linkedAuthorization | OP-14 result, optional | Linked mode: whether the product knows the external change was authorized. A recorded external change number alone never counts |
| windowStartUtc, windowEndUtc | date-time | The change window (D-13) |

**Rule.**
1. Find the C33 Gate Policy row for tier, change type and target stage (portfolio-scoped rows first, nearest wins; working default, 28 Q2). If none exists, there is no fallback (working default, 28 issue 14; document 21, rule 8). The engine returns `gate-policy.missing` in the Unavailable category (section 7) and reports a broken configuration. In check mode the check step then applies its cached fail mode. In record mode the deployment record write is refused, and the record step retries it later with the same idempotency key. V23 makes this case impossible after validation.
2. Compute four flags:
   - `isOutsideWindow` from OP-05;
   - `isOtherPath` when the route is not in the allowed routes;
   - `isMissingApproval` when the change type requires authorization and the authorization is not known. Built-in mode: the OP-03 result is not satisfied. Linked mode: OP-14 does not say authorized. The ITSM tool reports the state through a client-built step, or the change manager attests on a confirmation card. A recorded external change number alone never counts (working default, 28 Q22; D-08);
   - `isAiOnlyCodeApproval` when codeApprovalKind is ai-only and the policy's AI Only Code Approval Handling (or the `default-ai-only-code-approval-handling` setting) is flag or stop. AI-only code approvals never count as authorization, and the shipped gate default flags them (working default, open design question 7; see 28-working-defaults.md).
3. Apply the level:

| Gate level | Check mode verdict | Record mode result |
|---|---|---|
| off | not-applicable | No flags stored |
| record | allow | Flags stored as data; no message goes out (a proposal: the ladder says "nobody is told", which still allows the data to exist) |
| flag | allow | Flags stored; `notify` true when any flag is set |
| advise | warn when any flag is set, otherwise allow | As flag |
| enforce | stop when isMissingApproval, or isAiOnlyCodeApproval with handling stop; otherwise allow | As flag |

4. Window and path never stop a deployment. Enforcing a window stays in the deployment tool (D-13), and the check step itself is the pipeline route.
5. Emergency: when the change type allows authorization after, Enforce returns `allow-with-exception` instead of `stop`, records the exception and returns the follow-up due date from the policy's Emergency Rule Json. Working days count Monday to Friday in the first release; holiday calendars later (a proposal).

**Output: GateDecision**

| Field | Type | Description |
|---|---|---|
| gateLevel | string | off, record, flag, advise or enforce (engine contract) |
| verdict | string | not-applicable, allow, warn, stop or allow-with-exception |
| isOutsideWindow | boolean | Flag for O14 |
| isOtherPath | boolean | Flag for O14 |
| isMissingApproval | boolean | Flag for O14 |
| isAiOnlyCodeApproval | boolean | Shown with the missing-approval flag (sweep proposal; working default, open design question 7; see 28-working-defaults.md) |
| notify | boolean | Whether flows should message the owner roles and roles in the policy's Flag Notify list, or the `default-flag-notify` setting when the row has none (document 21, C33) |
| failMode | string | fail open or fail closed, returned so the check step can cache it for times the product cannot answer |
| emergencyFollowUpDueUtc | date-time, optional | When the emergency's authorization must follow |

**Fail mode.** The engine never fails open on its own. When it cannot answer (no valid snapshot, store unreachable), the adapter returns `engine.unavailable` and the check step applies the fail mode it cached from the last answer, or its own configured default. The same happens for a broken configuration (`gate-policy.missing`). The fail mode default itself is still open (D-03).

**Tests.** A snapshot with a gate policy gap returns `gate-policy.missing`, never Flag. In linked mode, a change with only an external change number is flagged as missing approval; one with an authorized fact and an evidence link is not. An ai-only code approval under the default handling sets the flag and never satisfies authorization. Enforce never stops for window or path alone.

### 5.7 OP-05 Check change window

| Field | Direction | Type | Description |
|---|---|---|---|
| windowStartUtc | in | date-time, optional | When the change may start |
| windowEndUtc | in | date-time, optional | When the change must be done |
| eventUtc | in | date-time | The moment to test |
| state | out | string | inside, before, after or no-window |
| minutesOutside | out | integer | How far outside, 0 when inside |

**Rule:** the start is inclusive, the end exclusive, all in UTC. Time zones are a display concern. Outcomes: `inside`, `outside`, `no-window`.

### 5.8 OP-06 Evaluate documentation completeness

**Input**

| Field | Type | Description |
|---|---|---|
| applicationKindKey | key | The application's kind |
| tierKey | key | The application's tier |
| lifecycleStageKey | key | The application's stage |
| links | list of DocumentFact | The application's Document Links (O19) |

**DocumentFact**

| Field | Type | Description |
|---|---|---|
| documentLinkId | ID | The Document Link row |
| documentTypeKey | key | Its document type (C22) |
| requirementKey | key, optional | The Documentation Requirement it meets, if set |
| status | string | The stored document status meaning |
| hasUrl | boolean | Whether a link is recorded |
| lastCheckedOnUtc | date-time, optional | When someone last confirmed it |

**Rule.**
1. Applicable requirements are active C23 rows whose optional tier and kind match, and whose From Stage ranks at or below the application's stage. This needs an ordering of stages the engine may use: Stage Rank, a new term (section 16), because glossary Stage Number and Sort Order are display only.
2. For each requirement, the best link is the one tied to the requirement, else the newest of the same document type.
3. Effective status: missing when no link or no URL; stale when the stored status is current and Last Checked On plus the type's Freshness Days is before asOfUtc; otherwise the stored status.

**Output**

| Field | Type | Description |
|---|---|---|
| complete | boolean | True when every applicable requirement is current |
| items | list | Per requirement: requirementKey, documentTypeKey, effective status, staleOnUtc, documentLinkId |
| missingCount, draftCount, staleCount | integer | Counts for pages and MCP answers ("what documentation is missing or stale for my app", D-17) |

**Outcomes:** `complete`, `incomplete`. The engine writes the computed Stale On into O19 (an engine-owned column).

### 5.9 OP-07 Evaluate gate readiness

| Field | Direction | Type | Description |
|---|---|---|---|
| gateKey | in | key | The lifecycle gate (C13) |
| tierKey, applicationKindKey | in | key | Scope for C14 rows |
| acceptedDecisionTypeKeys | in | list of keys | Decision types with an accepted decision for the subject (fetched) |
| documentation | in | OP-06 result | Document statuses (fetched or computed) |
| doneReviewTypeKeys | in | list of keys | Review types done for the subject (fetched) |
| ready | out | boolean | True when every applicable requirement is met |
| requirements | out | list | Per C14 row: requirementKey, requirement kind, met, reason code |

**Rule:** a requirement of kind decision accepted is met by an accepted decision of the required type; document current by an effective status of current (OP-06); review done by a done review of the required type. Outcomes: `ready`, `not-ready`. A gate decision whose gate is not ready cannot be accepted (OP-08 refuses `gate.not-ready`).

### 5.10 OP-08 Guard a write

The heart of D-10: every write, from any caller, passes through this rule set.

**Input: WriteRequest**

| Field | Type | Description |
|---|---|---|
| entity | string | The logical entity, such as `Decision` |
| operation | string | create, update or delete |
| rowId | ID, optional | The row, for update and delete |
| before | map | The current values of the columns the rules need (from a pre-image on Dataverse, from the row on Azure) |
| after | map | The changed columns and their new values |
| context | EvaluationContext | Actor, time, version |

**Rules.** The rules are fixed engine behavior over engine-contract meanings; the values they compare come from configuration.

| ID | Rule | Reason code |
|---|---|---|
| G-01 | Live configuration rows (C01, C04 to C45) and Configuration Set Version rows (C02) are written only inside activation, as engine-nested writes (section 6.4). Any other write is refused, whoever calls: a configuration admin, a tool and a system administrator included (working default, 28 issue 3). A bypass of the guard itself is detected, not prevented (section 11.5) | `config.activation-only` |
| G-02 | Rows of a configuration set of kind preset never change | `config.preset-read-only` |
| G-03 | Inside activation, a configuration row's Key never changes; a changed shipped row becomes Origin local | `config.key-immutable` |
| G-04 | A configuration row referenced by a version or by operational rows is never deleted; activation retires it (Is Active no) instead (pattern I-245) | `config.in-use` |
| G-05 | A decision with a final status meaning (accepted, rejected, superseded, withdrawn) never changes. "Superseded" is derived from the later decision, not a stored status change; on Dataverse the engine may set only Superseded By on the frozen row (document 22, section 8.1) | `decision.final` |
| G-06 | Approval Record Outcome, Responded On and Typed Reason are written only by the approval runner (actor kind approval runner), which holds the approval writer workload role, and only once. Column security on Dataverse, and the API's role check on Azure, also lets only the approval writer write these columns (working default, 28 issue 7 and Q15) | `approval.outcome-writer` |
| G-07 | Audit Entry rows are never updated or deleted; a Configuration Set Version never changes once created | `audit.append-only`, `config.version-immutable` |
| G-08 | An AI actor (AI client for a person, agent identity) cannot set a status mapped to accepted, registered, approved or completed, nor a review's done (B25-12). Rows it creates are forced to draft or proposed, and Is AI Created is set | `ai.status-not-allowed` |
| G-09 | Engine-owned columns (new term) take only engine values. Caller values are replaced and an info reason is returned | `engine.column-overwritten` |
| G-10 | A status written must be a C05 row for that entity | `status.unknown` |
| G-11 | A link written must use a C37 link kind of the entity's link category, and its URL must match the kind's Url Pattern when one is set | `link.wrong-category`, `link.url-pattern` |
| G-12 | A Role Group Binding must point to a principal of kind group (D-11) | `role.binding-not-group` |
| G-13 | A Business Application's Tier, Stage, Tier Decision and Stage Decision change only as an effect of an accepted decision (engine-nested) | `register.decision-required` |
| G-14 | A gate decision cannot be accepted while OP-07 says the gate is not ready | `gate.not-ready` |
| G-15 | A reused idempotency key with a different payload is refused (section 8) | `request.idempotency-key-reused` |
| G-16 | A Configuration Draft (C46) is created, edited, submitted or withdrawn only for a person who holds a role with Can Change Configuration (C29), directly or through an AI client acting for that person. A submitted draft is frozen; only the engine writes its outcome columns | `config.not-configuration-admin`, `config.draft-frozen` |
| G-17 | A decision is accepted only by an accept-decision effect: after OP-03 says satisfied, or when OP-02 returns `engine-accept` for an entry of the locked list (working default, open design question 8; see 28-working-defaults.md). No caller sets an accepted status directly | `decision.accept-by-engine-only` |

**Output: WritePlan**

| Field | Type | Description |
|---|---|---|
| allowed | boolean | False when any rule refused |
| refusals | list of Reason | The refusing reasons, errors first |
| setColumns | map | Stamps and computed values to write in the same operation: Performed By Actor Kind, On Behalf Of, Client App, Is AI Created, Configuration Set Version, Engine Version and Engine Checked On (common columns in document 22; working default, 28 issue 15), forced status, engine-owned values from OP-01, OP-03, OP-04, OP-05, OP-06 |
| effects | list of Effect | Follow-on writes the adapter applies in the same transaction |

**Effect** (new term)

| Field | Type | Description |
|---|---|---|
| kind | string | update-subject, supersede-decision, accept-decision, activate-configuration, recompute-approvals, write-audit-entry |
| entity | string | The logical entity to change |
| rowId | ID | The row to change |
| columns | map | The logical columns and values |

Examples: an approval outcome written gives a recompute-approvals effect on its decision; when OP-03 then says satisfied, an accept-decision effect follows (document 23, S1 step 8); a tier decision accepted gives an update-subject effect on the Business Application (Tier and Tier Decision); a configuration change decision accepted gives an activate-configuration effect (section 6.4).

**As the caller (working default, 28 issue 9 and Q15).** Effects run as the identity whose write started them, with the engine-nested marker. After an approval outcome, that identity is the approval runner's. So the privilege set behind the approval writer role must cover the tables the effects write (Decision, the decision's subject, configuration tables for activation). The guard limits those privileges to engine-nested writes (G-01, G-13, G-17). If spike S2 shows the marker can be forged, elevation is allowed only inside these named effects, and each elevated write adds an Audit Entry. Document 26 holds the privilege sets.

**Proposed by the build (2026-11-22).** How the build runs these rules (the column check and history classes from the logical model, the O10 freeze, G-09 in two classes, and the new WriteRequest and WritePlan fields) is in B25-3 at the end of this document.

**Tests.** A configuration admin's direct update of a Tier configuration row (C08) is refused with `config.activation-only`. An AI client's attempt to set a decision to accepted is refused. A second, different outcome on a final approval record is refused. A write to an engine-owned column without the engine-nested marker is refused.

### 5.11 OP-09 Validate a configuration set

**Input:** a snapshot to check: a draft's preview (the base snapshot with the draft's patch applied), a preset bundle, or any version. Optionally the active snapshot, for the diff and for V34 and V42. **Output: ValidationReport**

| Field | Type | Description |
|---|---|---|
| isValid | boolean | True when no issue has severity error |
| issues | list of Reason | Every finding, with code, V-ID, severity, entity and key |
| changeSummary | list of strings | Plain lines of what changed from the active version, kept on the draft and copied to C02 Change Summary at activation |
| contentHash | string | SHA-256 over the canonical JSON (section 9), for C02 Content Hash |

**Checks** (working default, 28 issue 12). OP-09 runs every rule of document 21, section 9, with the same IDs and severities. It runs in the public repo's build on every preset, on every draft edit, and again inside activation. An error blocks submitting and activation. A warning is shown on the approval card. The full rule text is in document 21; this table gives each rule its reason code.

| V-ID | Reason code | Severity | Check (short) |
|---|---|---|---|
| V01 | `shape.schema` | error | The bundle or patch matches the JSON Schema |
| V02 | `key.format` | error | Every key matches the key format (document 20, section 3.2) |
| V03 | `key.duplicate` | error | Keys are unique per entity; child keys are unique within their parent |
| V04 | `key.reused` | error | A new row never takes the key of a retired row |
| V05 | `key.immutable` | error | A patch never changes a row's key |
| V06 | `ref.unresolved` | error | Every key reference resolves to a row of the named entity in the set |
| V07 | `ref.inactive` | error | References point to active rows, unless the referencing row is retired |
| V08 | `ref.in-use` | error | A row in use is retired, never removed |
| V09 | `ref.wildcard` | error | Wildcards only where allowed; no combination comes from two equally explicit rows |
| V10 | `ref.exactly-one` | error | Exactly-one fields hold: one gate requirement target, one approver, one decision right holder (none for supports) |
| V11 | `tier.none-active` | error | At least one active tier |
| V12 | `tier.rank-duplicate` | error | Tier ranks are unique whole numbers |
| V13 | `tier.question-options` | error | Each active tier question has at least two active options, and every implied tier is active |
| V14 | `stage.meaning-coverage` | error | At least one active stage per lifecycle meaning; stage numbers are unique. The engine also needs unique Stage Ranks (`stage.rank-duplicate`) |
| V15 | `meaning.missing` | error | Each status-bearing entity has an active status per engine meaning, and one default per meaning |
| V16 | `meaning.changed-in-use` | error | The engine meaning of a row that records use never changes |
| V17 | `taxonomy.minimum` | error | At least one change type, target stage, pipeline-step route, actor kind per meaning and link kind per category |
| V18 | `decision-type.outcome` | error | Every active decision type has an active outcome |
| V19 | `approval-rule.tier-coverage` | error | For decision types with Requires Approval that carry a tier, a rule applies at every active tier; without Requires Approval, the resolved accountable holder is an owner role or a role that is neither a workload role nor read-only (28, issue 1) |
| V20 | `approval-rule.coverage` | error | Decision types with no tier that require approval have a rule; a decision type without Requires Approval has only consultation rules, if any |
| V21 | `approval-rule.change-coverage` | error | A change authorization rule group applies for each tier, change type that requires authorization, and governed target stage |
| V22 | `decision-right.accountable` | error | The resolved decision right group holds exactly one accountable |
| V23 | `gate-policy.coverage` | error | Every tier, change type and target stage resolves to exactly one gate policy |
| V24 | `gate-policy.fail-mode` | error, warning | Fail mode is set at enforce (error if missing) and only at enforce (warning) |
| V25 | `gate-policy.emergency-rule` | error | An emergency rule exists where the change type allows authorization after and the level is advise or enforce |
| V26 | `rule-group.ambiguous` | error | No two matching rule groups of equal specificity (approval rules, decision rights, separation rules) |
| V27 | `approval-rule.sequence`, `approval-rule.consultation-only` | error | Sequential rules in one group have unique sequence numbers; every rule group of a decision type with Requires Approval holds at least one rule that is not a consultation (OP-02, rule 9) |
| V28 | `value.range`, `approval-rule.zero-count` | error | Minimum Count is at least 1 everywhere, with no zero waiver; engine acceptance comes only from the locked list through OP-02 rule 8 (working default, 28 issue 1; open design question 8; see 28-working-defaults.md); day values are in range |
| V29 | `cadence.coverage` | warning | A review type with a cadence at one tier has one at every active tier |
| V30 | `approval-rule.workload-approver` | error | No approval rule names a workload role or a read-only role |
| V31 | `decision-right.ai-letter` | error | Supports rows name no holder; no AI construct holds responsible or accountable |
| V32 | `role.config-admin-missing` | error | A role with Can Change Configuration exists, and the configuration change decision type has a rule naming it |
| V33 | `setting.locked-changed`, `setting.ai-approval-lock-off` | error | Locked settings are unchanged: the AI approval lock stays on, and `engine-acceptable-decision-types` keeps its shipped entries |
| V34 | `config.protected-change` | error at activation | Protected changes are approved under the active version's rules |
| V35 | `role.binding-not-group` | error | Role group bindings point only to principals of kind group (D-11) |
| V36 | `separation.shared-group` | error | Roles named in a roles-in-different-groups separation rule bind to different groups |
| V37 | `role.unbound` | warning | Every role an approval rule uses has a binding (working sets only) |
| V38 | `setting.required` | error | Required settings exist and match their value types, including the change management mode |
| V39 | `json.schema` | error | JSON fields match their sub-schemas: emergency rule, conditions, triggers, flag notify, setting values |
| V40 | `pattern.invalid` | error | URL and ID patterns compile; source, hub and template URLs use https |
| V41 | `snapshot.size` | error, warning | The snapshot fits the binding; warning at 80 percent (section 6.2) |
| V42 | `config.base-moved` | error | The draft's base equals the active version, or the draft was rebased and revalidated |
| V43 | `preset.conflict` | error | No two overlays that conflict are applied together |
| V44 | `preset.tenant-data` | error (build) | A shipped preset holds no secrets, tenant identifiers, role group bindings or portfolio scopes |

**Engine checks beyond V01 to V44** (proposed for document 21's list).

| Reason code | Severity | Check |
|---|---|---|
| `snapshot.schema-unsupported` | error | The snapshot's schema major version is one the engine knows |
| `role.config-admin` | error | Can Change Configuration is set only on the configuration admin role |
| `standard-model.type` | error | Standard change models belong to a change type that is not emergency |
| `standard-model.condition-kind` | error | Every condition in Conditions Json uses a known condition kind (OP-12) |
| `vocabulary.source` | warning | Vocabulary mappings point to an existing row |

**Proposed by the build (2026-11-22).** The last four rows take the IDs V45 to V48, in table order; `snapshot.schema-unsupported` stays a compiler check with no V-ID (B25-2 at the end of this document; document 21, B21-1).

**Tests.** One failing and one passing fixture per V-ID and per engine check. Every shipped full preset, alone and with each allowed overlay combination, returns zero errors (working default, 28 Q3).

### 5.12 OP-10 Walk impact and OP-11 due dates

**OP-10 Walk impact ("what breaks", D-01).**

| Field | Direction | Type | Description |
|---|---|---|---|
| startApplicationId | in | ID | The application to start from |
| direction | in | string | dependants, dependencies or both |
| maxDepth | in | integer | Hops to walk; capped by a setting |
| edges | in | list of {fromId, toApplicationId or toComponentId, dependencyTypeKey} | The Dependency rows (O04), fetched by the adapter |
| reached | out | list | Per application: ID, depth, dependency type keys on the path, path of IDs |
| cycleDetected | out | boolean | True when the walk met an application twice |

Only dependency types with Counts For Impact yes are followed. Output is sorted by depth, then ID. One call, because the walk is recursive and agents do better with one tool.

**OP-11 Compute due dates and reminders.** Input: the review cadence or reminder rule keys, due date, done date, asOfUtc. Output: next due date, and for today a list of actions (remind, repeat, escalate to the deputy) with the rule key that caused each. Flows send the messages (D-10: flows only send approvals and reminders); the engine only says what is due.

### 5.13 OP-12 Evaluate a standard change

Backs the tool `check_standard_change` (document 24) and rule 8 of OP-02. An AI client never decides that a change is standard; the engine evaluates the model's conditions (working default, 28 issue 12).

**Input: StandardChangeRequest**

| Field | Type | Description |
|---|---|---|
| changeTypeKey | key | The change request's change type (C20) |
| standardChangeModelKey | key, optional | The model the requester names; when empty, every active model of the change type is tried |
| change | StandardChangeFacts | The facts the conditions test |
| models | list of {modelKey, approvedByDecisionStatus} | The status meaning of each candidate model's Approved By Decision (fetched) |

**StandardChangeFacts**

| Field | Type | Description |
|---|---|---|
| tierRank | integer | The rank of the main application's tier |
| targetStageKeys | list of keys | The stages of the planned targets |
| affectedApplicationCount | integer | The main application plus Affected Applications (O12) |
| componentKindKeys | list of keys | The component kinds the change touches, as stated on the change request |
| windowMinutes | integer, optional | The length of the change window |
| statedConditionKeys | list of keys | The stated conditions the requester confirmed on the change request |

**Rule.**
1. A candidate model is active, belongs to the change type, and the change type is not emergency (`standard-model.type`).
2. The model's Approved By Decision must have the status meaning accepted. Otherwise the model never qualifies (`standard-model.not-approved`).
3. Each entry of the model's Conditions Json has one condition kind, a small engine contract list: `max-tier-rank`, `target-stages-in`, `max-affected-applications`, `component-kinds-within`, `max-window-minutes` and `stated`. The first five compare a fact with the condition's value. A `stated` condition is met only when its key is in statedConditionKeys; the trace marks it "as reported", because the engine cannot see the work itself.
4. The change qualifies for a model when every condition is met. With several qualifying models, the one the requester named wins; otherwise the first by key. The result names the model and its Approved By Decision.

**Output: StandardChangeResult**

| Field | Type | Description |
|---|---|---|
| qualifies | boolean | True when one model's conditions are all met |
| modelKey | key, optional | The qualifying model |
| approvedByDecisionId | ID, optional | The decision that approved the model; OP-02 cites it as the acceptance basis |
| failedConditions | list of {modelKey, conditionIndex, conditionKind, why} | Every condition that failed, per model |

**Outcomes:** `qualifies`, `does-not-qualify`.

**Tests.** One passing and one failing fixture per condition kind. A model whose Approved By Decision is proposed or superseded never qualifies. An emergency change type never qualifies. A `stated` condition fails when the requester did not confirm it. The same input always picks the same model.

### 5.14 OP-13 Compare a gap check result

Backs `run_gap_check` and `compare_inventory_list` (document 24). Before this operation the comparison sat in the adapter; it is a rule, so it moves into the core (working default, 28 issue 12). The lifecycle is document 22, section 9.2.

**Input: GapCompareRequest**

| Field | Type | Description |
|---|---|---|
| gapCheckRunId | ID | The run (O22) |
| coveredEstateKeys | list of keys | The estates the run covers (C07 `estate`) |
| coveredComponentKindKeys | list of keys | Component kinds with Is Gap Check Covered yes (C16) |
| isFinalPage | boolean | True when this call carries the last page of every covered estate |
| items | list of InventoryItem | The items seen in this page: externalId, sourceType, componentKindKey, estateKey, labelAsSeen, targetAsSeen, suggestedClaimantObjectId |
| components | list of {technicalComponentId, externalId, componentKindKey, estateKey, isRetired} | Registered components in covered estates and kinds (fetched) |
| claims | list of {claimId, claimKey, statusMeaning, findingKind} | Existing claims for the item keys and components in play (fetched) |
| seenThisRun | list of technicalComponentIds | Components already seen in earlier pages of this run (fetched) |

**Rule.**
1. **Registered.** An item whose External Id matches a registered component updates that component's Last Seen On. No claim.
2. **Claim Key.** For an item with no component, the engine builds the Claim Key from the finding kind, the source type and the External Id, joined with a separator (document 22, O23).
3. **Unregistered.** If a claim with that key exists, its Last Seen On, Last Seen Run and Times Seen are updated, whatever its status. A dismissed claim stays dismissed and a claimed claim stays claimed. Otherwise a new open claim is created, with the suggested claimant as a hint only.
4. **Vanished (sweep proposal).** Only when isFinalPage is true: every registered, not retired component of a covered kind in a covered estate that the run did not see gets a vanished claim (created or updated by its key, with Registered Component set). Partial runs never raise vanished claims. The engine never retires a component; a person confirms it later.
5. Items outside the covered estates or kinds are ignored and counted.

**Output: GapComparePlan** (the runtime applies each write through OP-08)

| Field | Type | Description |
|---|---|---|
| componentLastSeen | list of technicalComponentIds | Components to stamp with Last Seen On |
| newClaims | list | Claims to create: claimKey, findingKind, item fields |
| updatedClaims | list | Claims to update: claimId, Last Seen On, Last Seen Run, Times Seen |
| vanishedClaims | list | Vanished claims to create or update, with technicalComponentId |
| counts | record | itemsSeen, newClaims, vanishedClaims, ignored, for O22 |

**Evidence level.** The caller cannot raise it. A list passed in from outside is "as reported" (`compare_inventory_list`). Where the product's own server ran the live query as the admin, in the same process, the run is marked read.

**Outcomes:** `compared`.

**Proposed by the build (2026-11-22).** Each fetched claim adds `timesSeen` and `lastSeenRunId`, so the plan writes absolute values and a second page of the same run does not raise Times Seen; the Claim Key lowercases source type and External Id, and its length exceeds O23's text(450) (B25-4 at the end of this document; document 22, B22-2).

**Tests.** A second run over the same items creates no new claims and raises Times Seen. A dismissed claim stays dismissed. A run with more pages raises no vanished claim until the final page. A retired component never gets a vanished claim. An item of an uncovered kind is ignored.

### 5.15 OP-14 Evaluate linked-mode authorization

Backs the missing-approval flag in linked mode (OP-04) and the check step. Working default, 28 Q22: the client's ITSM tool reports the authorization state through a client-built step; where no step exists, the change manager attests on a confirmation card; a recorded number alone never counts.

**Input: LinkedAuthorizationRequest**

| Field | Type | Description |
|---|---|---|
| changeManagementMode | string | built in or linked, as copied onto the change request |
| hasExternalChangeNumber | boolean | Whether the ITSM change number is recorded (shown, never decisive) |
| facts | list of AuthorizationFact | The authorization facts recorded on the change request (fetched) |
| attesterRoleKeys | list of keys | The roles allowed to attest: the holder of the accountable letter for change authorization in this scope (C31; the change manager by default, 28 issue 4) |

**AuthorizationFact** (new term; stored as an Evidence Link, O20, on the change request, with the fields below; the ERD decides the columns)

| Field | Type | Description |
|---|---|---|
| sourceKind | string | `itsm-step` (a client-built step, a workload principal) or `attestation` (a person on a confirmation card) |
| state | string | authorized, not authorized or cancelled, as the ITSM tool or the attester reports it |
| hasEvidenceUrl | boolean | Whether the fact links to the ITSM record |
| reportedOnUtc | date-time | When the state was reported |
| actorKind, principalKind | string | Who recorded it |
| attesterHoldsRole | boolean | For an attestation: the person held an attester role when answering |

**Rule.**
1. Built-in mode: not applicable; OP-04 uses OP-03.
2. A fact counts only with an evidence URL.
3. An `itsm-step` fact counts only from a workload principal (actor kind pipeline), never from an AI client or an agent identity.
4. An `attestation` fact counts only from actor kind person, principal kind user, holding an attester role, answered on a confirmation card through the approval runner. AI never attests.
5. The latest counting fact by reportedOnUtc decides. "Not authorized" or "cancelled" after "authorized" removes the authorization.
6. With no counting fact the change is not authorized, whatever the external change number says.

**Output: LinkedAuthorizationResult**

| Field | Type | Description |
|---|---|---|
| authorized | boolean | True only when the latest counting fact says authorized |
| basis | string | `itsm-step`, `attestation` or `none` |
| decidingFactId | ID, optional | The fact that decided |
| ignoredFacts | list of {factId, reason code} | Facts that did not count: `authorization.no-evidence`, `authorization.ai-not-counted`, `authorization.not-attester` |

**Outcomes:** `authorized`, `not-authorized`, `not-applicable`.

**Proposed by the build (2026-11-22).** AuthorizationFact adds `answeredOnConfirmationCard` (default false), which rule 4 reads; a fact dated after the evaluation moment never authorizes (`authorization.reported-later`), while a later removal still removes (B25-4 at the end of this document).

**Tests.** A change with only an external change number is not authorized. An ITSM step fact with an evidence link authorizes; a later "cancelled" fact removes it. An attestation by a person without the attester role does not count. An attestation written by an AI client never counts.

---

## 6. Configuration loading and caching

### 6.1 Drafts are patches; the active snapshot is in force

Working default, 28 issue 3 (document 21's model, on both bindings).

- **The engine reads only the active version's snapshot** (C02 Content Json). It never reads the live configuration rows when it evaluates.
- **A draft is a patch.** An admin instructs; the admin's AI calls the product's configuration tools (D-10). The engine stores the change as a Configuration Draft (C46): the patch (upsert and retire operations by entity and key), the base version number, the computed preview, the validation result, the diff and the rationale. No live row changes.
- **Live rows change only in activation.** The live configuration rows always equal the active version. They change only inside the activation operation (section 6.4). No person, role or tool writes configuration tables directly, a configuration admin included (G-01). Pages therefore never show labels that are not in force.
- **C02 is created only at activation.** A Configuration Set Version is append-only (class A in document 22). There is no staged or draft C02 row.
- **Why:** an AI edit can never change live behavior by itself; every behavior change has a decision and a version number; and the engine's input is one immutable document, which makes caching and replay simple. D-10's "no rebuild, no redeploy" still holds.

### 6.2 Snapshot format

| Element | Description |
|---|---|
| snapshotSchemaVersion | The version of the snapshot's own shape (new term). The engine refuses a major version it does not know, with `snapshot.schema-unsupported` |
| configurationSetKey | The working set's identity |
| versionNumber | The C02 Version Number |
| entities | One array per configuration entity, keyed by the entity's logical name; rows carry their key, active flag, origin, engine meaning and extra columns |
| Canonical form | Properties in ordinal order, arrays sorted by key, UTC dates in ISO 8601, no insignificant whitespace. The hash is computed over this form |

**Size.** A Dataverse multiline text column holds up to 1,048,576 characters [L20]. A default snapshot is estimated at tens of kilobytes; a client with 55 tiers and rich rules may reach a few hundred kilobytes (an estimate). OP-09 warns at 80 percent. If a client ever exceeds the limit, the fallback is a file column (UNVERIFIED for read latency in a plug-in; spike S10). On Azure the snapshot sits in a native `json` column [S20].

### 6.3 Loading and the cache

| Step | Dataverse adapter | Azure adapter |
|---|---|---|
| 1. Which version is in force? | One retrieve of the in-force Configuration Set's current version number (C01), by alternate key [S15] | One indexed select of the in-force set's current version number |
| 2. Cache hit? | A static, read-only, thread-safe dictionary keyed by organization ID, set key and version number. Learn says read-only static members are thread-safe [L6] | An in-memory cache per Functions instance, keyed the same way |
| 3. On a miss | Read C02 Content Json, check its hash against Content Hash, parse once into indexed lookups, swap in atomically | Same |
| 4. Every result | Carries the version and hash used | Same |

- **No time-to-live by default for writes.** The version check costs one indexed read per call, so an activated change applies on the next call. Reads may use a short time-to-live through a setting (section 6.5).
- **Why the organization ID is in the key:** whether one sandbox worker process ever serves two environments is UNVERIFIED, so the key never assumes it does not.
- **The cache is only an optimization.** If a worker restarts or Dataverse drops the cached plug-in instance [L6], the next call reloads. Correctness never depends on the cache.
- **The seed** (JSON files shipped per preset, document 20, section 3.6) is read by the same configuration assembly at install and in tests. The seed is never the live copy (D-10).

### 6.4 Activating a version

1. The admin's AI calls Propose configuration change with a patch (upsert and retire operations by entity and key), the base version number and the admin's rationale.
2. The engine checks that the person behind the call holds a role with Can Change Configuration (G-16). It applies the patch in memory to the base snapshot, runs OP-09 on the preview, and computes the diff, hash and change summary. It saves a Configuration Draft (C46). A draft with errors is saved with its issues and cannot be submitted. No live row changes.
3. On submit (`submit_record` with subject kind configuration draft, document 24) the draft is frozen. The engine records a configuration change decision, or a gate policy change decision when the draft touches Gate Policy rows. It is Proposed, with Instructed By the admin and performed by the AI client (D-10).
4. OP-02 resolves the approvals from the active version's rules, so a draft can never lower its own bar (V34). The approval runner sends one card with the engine's diff. Only a person can accept (OP-03). The instructing admin may accept their own AI's draft, and the strict separation overlay needs a second admin through a C40 row (working default, open design question 4; see 28-working-defaults.md).
5. On acceptance, OP-08 returns an activate-configuration effect. Inside one transaction, activation: rebases the patch if the active version moved, and refuses on overlap (V42); runs OP-09 again; writes the changed live rows as engine-nested writes; creates the new C02 row with Content Json, Content Hash, Version Number and Change Summary; sets the C01 current version number; marks the draft activated; and writes an Audit Entry "configuration activated". On Dataverse this runs in PostOperation of the decision update (synchronous). On Azure it is one database transaction.
6. The next engine call anywhere sees the new version number; earlier rows keep their stamped version.
7. A rejected, withdrawn or expired draft is discarded and kept as evidence. No version is created.
8. Install and upgrades use the apply operation (document 21, sections 4.8 and 4.9). Version 1 is created through the bootstrap rule. A release's changed defaults arrive as an upgrade draft that a configuration admin accepts, and local changes are never lost (working default, 28 Q5).

**Activation is not callable.** It has no custom API, endpoint or tool of its own, so nobody can start it, and no AI can (document 21, safety floor 3). What document 21 calls the activation custom API is this effect. The engine recognizes its writes by the engine-nested marker (spike S2).

**Proposed by the build (2026-11-22).** Outcome words for rebase, activation and the bootstrap, discarding a draft that cannot activate, and the fixed order of activation writes are in B25-5 at the end of this document.

### 6.5 Settings the engine reads (C04)

Document 21 keeps the one table of every setting, with its default and owner (working default, 28 issue 15). This list shows only the settings the engine reads.

| Setting key | Value | Status |
|---|---|---|
| `change-management-mode` | built in or linked; may differ per portfolio through the optional portfolio scope (28 Q2) | In document 20 |
| `ai-approval-lock` | on, locked | In document 20 (D-02, D-10) |
| `engine-acceptable-decision-types` | Locked. The only decisions the engine may accept without a card: intake with `lowest-tier`, change authorization with `standard-model` | In document 21 (working default, open design question 8; see 28-working-defaults.md) |
| `default-flag-notify` | Owner roles and roles told about a flag when a gate policy row names none | In document 21 |
| `default-ai-only-code-approval-handling` | ignore, flag or stop; shipped as flag | In document 21 (working default, open design question 7; see 28-working-defaults.md) |
| `engine-read-cache-seconds` | Time-to-live for read operations; 0 means check every call | New, a proposal |
| `impact-max-depth` | The cap for OP-10 | New, a proposal (also in document 24) |
| `working-days` | The weekdays counted as working days | New, a proposal |

---

## 7. Error model

**Principle.** A rule outcome is a result, not an exception. "Not approved", "not ready", "outside window" and "stop" are normal answers. Exceptions are for refusals of a write, bad input and failures.

| Category | When | Library returns | Dataverse mapping | Azure mapping |
|---|---|---|---|---|
| Outcome | A read operation answers | EvaluationResult with outcome and reasons | Custom API response properties; no exception | 200 with the result body |
| Invalid input | Missing or malformed parameter, unknown key | Reason `input.*` | `InvalidPluginExecutionException` with HTTP 400 | 400 problem details |
| Refused | OP-08 refuses a write, or the caller lacks the role for an operation | WritePlan allowed false | `InvalidPluginExecutionException` in PreValidation: HTTP 403 for role refusals, HTTP 400 for rule refusals | 403 or 422 problem details |
| Not found | A referenced row does not exist | Reason `ref.not-found` | HTTP 404 | 404 problem details |
| Conflict | Idempotency key reused with a different payload; append-only violation by a race | Reason `request.*` or `audit.append-only` | HTTP 409 | 409 problem details |
| Unavailable | No valid snapshot in force; snapshot schema unknown; hash mismatch; a broken configuration at run time (a missing gate policy, an ambiguous rule group, no accountable holder). The engine never falls back to a built-in default (rule 10) | Reason `engine.unavailable`, `snapshot.*`, `gate-policy.missing`, `rule-group.ambiguous`, `decision-right.accountable` | HTTP 503 | 503 problem details |
| Unexpected | A bug | Caught at the adapter edge, logged with the correlation ID | `InvalidPluginExecutionException` with a plain message, never a stack trace to users [L8] | 500 problem details |

- **Dataverse facts.** `InvalidPluginExecutionException` returns "a friendly message and an `IsvAborted` error code"; any other exception shows the user a stack trace [L8]. The SDK reference lists constructors that take a `PluginHttpStatusCode` and an error code [L9]; whether the sandbox's SDK version offers the HTTP status constructor is UNVERIFIED (spike S3). A constructor with an exception details dictionary exists, and Web API callers read details through the `Prefer: odata.include-annotations="*"` header, including a `SubErrorCode` annotation [L10].
- **How the reason code travels on Dataverse.** The message starts with the code in brackets, then the plain sentence: `[approval.outcome-writer] Only the approval runner can record an approval outcome.` The same code goes into the exception details dictionary. Clients that cannot read annotations still get the code from the message.
- **Where to refuse.** Learn says to cancel an operation in PreValidation, because canceling later rolls back the transaction at a performance cost [L4]. The guard therefore refuses in PreValidation wherever the pre-image is enough.
- **Generative pages.** Page calls never throw for expected failures; they resolve with `ok: false` and an error code [L17]. A refusal from a custom API reaches the page as an HTTP status code string. `not_permitted` means the API is not on the environment's allow list for pages, not a missing privilege [L17].
- **Azure.** Problem details (RFC 9457): `type` is a URI ending in the reason code, `title` the plain sentence, `status` the HTTP status, plus an `errors` array of Reason records and the correlation ID.
- **Reason code catalog.** Published in the public repo with each release. Codes are lowercase, dot-separated, never reused. Labels may be translated later; codes never change.
- **Proposed by the build (2026-11-22).** The build's catalog adds 57 codes this section does not name, among them `input.unknown-column`, `input.contract-value-unknown`, `record.final` and `standard-model.no-candidate`, and reports 17 OP-09 codes at run time as unavailable (B25-1 at the end of this document).

---

## 8. Idempotency

| Case | Design |
|---|---|
| Read operations | Naturally idempotent: pure functions over a fixed snapshot and fetched facts |
| Record step (Deployment Record) | Upsert by Idempotency Key (glossary O14, pattern I-189). Dataverse: alternate key [S15]. Azure: unique constraint. A replay returns the existing record with `created` false |
| Other write operations through custom APIs or endpoints | An optional `idempotencyKey` parameter. The adapter writes an Audit Entry with a Request Key and a Request Hash (new terms) under a unique key in the same transaction. A replay with the same hash returns the first result; a different hash is refused with `request.idempotency-key-reused` (409) |
| Concurrent duplicates | The unique key makes the second insert fail inside its transaction; the caller retries and receives the first result |
| Generative pages | When a page call ends `indeterminate`, Learn says "don't automatically retry" because the server might have committed [L17]. With an idempotency key the page can retry safely; the product's page code always sends one for writes |
| Stateless MCP | Each tool call carries its own key; the server keeps no session state (sweep proposal: stateless MCP specification) |
| Plug-in re-runs | Learn notes Update plug-ins can run twice in certain cases [L4]. Stamps are idempotent (same values), and effects check the target's current state before writing |
| Approval outcomes | Writing the same outcome again is a no-op; a different outcome after a final one is refused (G-06) |
| Activation | Activating a draft that is already activated is a no-op that returns its version |
| Gap check pages | A page sent again with the same idempotency key returns the first result; claims are keyed by Claim Key, so a repeated item never creates a second claim (OP-13) |

Pattern I-267 ("be idempotent; think about side effects") applies: notifications are sent by flows that key on the row ID and the version, so a replayed write never sends a second message.

---

## 9. Determinism

| Rule | How |
|---|---|
| Same inputs, same output | The core is pure: no clock (asOfUtc is an input), no random values, no new GUIDs, no environment reads, ordinal comparisons |
| Stable ordering | Every list in a result is sorted by a stable key; ties never depend on dictionary order |
| Canonical hashing | Content Hash is SHA-256 over canonical JSON (section 6.2). The same rows give the same hash on both platforms |
| Replay | Each result carries configurationVersion, snapshotHash and engineVersion. An auditor can load that version and the same facts and get the same answer. A replay tool in the testing assembly does exactly that |
| Versioned behavior | A change in rule behavior is a new engine version with a release note and a new fixture. Configuration changes are new configuration versions. The two never mix |
| Engine stamp | Every engine-evaluated write stamps Engine Version and Engine Checked On next to the Configuration Set Version. Both are common columns in document 22 (working default, 28 issue 15). A row can always say which code and which rules made it |

---

## 10. Unit testing with fixtures

| Layer | What | Where it runs |
|---|---|---|
| Fixtures | One JSON file per case: `fixtures/<operation>/<case>.json` with a `snapshot` (a preset key plus inline overrides), `context`, `facts`, `input` and `expected` (outcome, payload, reason codes). Fictional data only (pattern I-280) | Every build, both target frameworks |
| Preset validation | OP-09 over every shipped full preset (`starter`, `balanced`, `regulated`, `microsoft-coe-baseline`), alone and with each allowed overlay combination, must return zero errors. The CoE baseline ships in the first release (working default, 28 Q6); presets combine as one full preset plus overlays (working default, 28 Q3) | Every build |
| Invariant tests | Generated snapshots and inputs check rules that must hold for any configuration: an AI or workload actor never counts toward any approval; no decision outside the locked list is accepted without a person's card; a consultation never counts; a separation refusal never counts; a recorded external change number alone never authorizes; a missing gate policy never falls back to a level; a more serious answer never lowers the tier; Enforce never stops for window or path alone; OP-09 rejects any snapshot with the AI lock off; no write outside activation changes a configuration row; the same input gives the same hash | Every build |
| Test vector per behavior change | Every pull request that changes a rule adds or changes a fixture (pattern I-185) | Pull request check |
| Binding parity | The same fixtures run through the Dataverse adapter (with an in-memory execution context and organization service double) and through the Azure adapter (against a local or test Azure SQL database). Both must return the same outcome and reason codes as the library alone | Nightly and before release |
| Environment tests | A short scripted suite in a managed development environment: an approver's AI client tries to write an approval outcome and is refused; a configuration admin tries to edit a configuration row directly and is refused; a bulk import hits the guard; timings stay under 2 seconds; the bypass header is detected (section 11.5). Test personas and disposable records, cleaned up by captured ID | Before each release |

The fixture format is a contract others can use too: anyone who builds a third adapter proves it with the same suite.

**Proposed by the build (2026-11-22).** The build numbers the invariant tests INV-01 to INV-11 in this order, and its replay tool (section 9) uses a replay record format and difference kinds of its own (B25-7 at the end of this document).

---

## 11. Dataverse adapter

### 11.1 Packaging

| Item | Design | Source |
|---|---|---|
| Project | SDK-style plug-in project from `pac plugin init`, target `net48` | [L1], [L2] |
| Package | One NuGet plug-in package with the plug-in assembly, the engine core, configuration and runtime builds for `net48`, and `System.Text.Json` | [L2] |
| Signing | Not required inside a package. If the release signs any assembly, every assembly it depends on must be signed too | [L2] |
| Size | Well under 16 MB | [L2] |
| Plug-in types | `EngineGuard`, `EngineCompute`, `EngineEffects`, `EngineApi` (one dispatcher for all custom APIs, by message name) | [L2] (import time grows with type count) |
| Stateless | No instance fields hold context or services; only the static snapshot cache | [L6] |
| Configuration data for steps | None. Unsecure and secure step configuration are not used; all configuration is in tables. (A custom API main plug-in cannot receive step configuration anyway [L14]) | [L14] |
| Column map | Generated from the physical model: logical entity and column names to schema names | Document 20, section 3.4 |
| Tracing | `ITracingService` for diagnostics; optional Application Insights export | [L7] |

### 11.2 Actor mapping from the execution context

| ActorInfo field | Source in Dataverse | Notes |
|---|---|---|
| principalId | `InitiatingUserAzureActiveDirectoryObjectId` [L19] | The Entra object ID of the initiating user |
| principalKind | `InitiatingUserApplicationId` is non-empty for an application user and empty otherwise [L19]; application users are matched to O05 rows (service principal, managed identity) | Agent users from Entra agent accounts: mapping UNVERIFIED (announced for December 2026 in a secondary source) |
| actorKind | approval runner and pipeline: the application user's O05 row and its workload role (approval writer or deployment recorder); AI client for a person: asserted by the product's MCP server in the `actorKind` parameter; otherwise person | A caller may assert a lower-trust kind (ai-client) but never a higher one; "approval runner" and "engine" are never accepted from a parameter |
| clientApp | The `clientApp` parameter from the product's MCP server; `InitiatingUserAgent` [L19] as a hint | Whether Dataverse exposes the delegated token's client application to plug-ins is UNVERIFIED; whether the Dataverse MCP server sets a recognizable user agent is UNVERIFIED (spike S4) |
| roleKeys | The caller's membership of the group teams bound in C30 | Membership is cached up to 8 hours and a team lists only members who accessed the environment [S7] |
| isEngineNested | A marker the engine sets in `SharedVariables` for its own nested writes, read through `ParentContext` | Whether a client can forge this marker (clients can pass a `tag` optional parameter) is UNVERIFIED (spike S2). If it can, elevation is allowed only inside named engine effects, each with an Audit Entry (working default, 28 issue 9 and Q15) |

**Safety does not depend on detecting AI.** "AI never approves" rests on G-06 (only the approval runner, through the approval writer role, writes outcomes, with column security on those columns) and OP-03 (only a person signed in as a user counts). If an AI client writes through the Dataverse MCP server's `update_record` and is stamped as a person, it still cannot approve. Actor detection makes "performed by" accurate; it is not the lock.

### 11.3 Message and stage registration

Steps run synchronously as the calling user, in execution order 1. No step impersonates another user or an engine application user: the engine always runs as the caller (working default, 28 issue 9 and Q15; section 5.10 says what that means for effects). Update steps list filtering attributes [L7]. Steps are solution components and ship in the solution [L5]. The registration list is generated from the ERD by a script, not entered by hand.

| # | Message | Tables | Stage | Mode | Images | Library | Purpose |
|---|---|---|---|---|---|---|---|
| 1 | Create | All product tables (configuration tables, C46 Configuration Draft, O01 to O24) | PreValidation (10) | Sync | none | OP-08 | Refuse early: configuration tables only through activation, drafts only for a configuration admin, presets read-only, AI status limits, link categories, binding to groups. PreValidation runs before the platform's security checks and outside the transaction for the first operation [L4] |
| 2 | Update | All product tables | PreValidation (10) | Sync | Pre-image of status, set kind, key, draft state and outcome columns | OP-08 | Refuse configuration writes outside activation, changes to frozen drafts, final and append-only changes, key changes, outcome writes by anyone but the approval runner |
| 3 | Delete | All product tables | PreValidation (10) | Sync | Pre-image of status and key | OP-08 | Refuse deletes of decisions, approval records, audit entries, versions and drafts; refuse every delete of a configuration row (activation retires instead, I-245) |
| 4 | Create, Update | Operational tables (O01 to O24) | PreOperation (20) | Sync | Pre-image on Update | OP-08 with OP-01, OP-03, OP-04, OP-05, OP-06 | Stamp performed-by, configuration version and engine stamp; force draft or proposed for AI rows; compute engine-owned columns: O08 Computed Tier, O10 Counts Toward Requirement, O14 Gate Level Applied and the three flags, O19 Stale On. Learn: change values in PreOperation [L4] |
| 5 | Update | O09 Decision (status), O10 Approval Record (outcome) | PostOperation (40) | Sync | Pre-image and post-image of status or outcome | OP-08 effects | Apply effects in the same transaction: recompute approval status, accept the decision, update the subject, supersede the earlier decision, activate the accepted configuration draft. Post-images exist only in PostOperation [L5] |
| 6 | Custom API main operation | The 16 custom APIs in section 11.4 | MainOperation (30) | Sync | n/a | All | Learn: MainOperation is for custom APIs [L4]; the handler checks `MessageName` and stage 30 [L14] |

Registered per table, this matrix comes to about 240 steps. The step registration is generated, not written by hand, and packaged with the solution.

**Not registered, on purpose.**

| Not registered | Why |
|---|---|
| Retrieve, RetrieveMultiple | Learn advises limiting synchronous logic on reads [L7]. Read security is Dataverse roles |
| CreateMultiple, UpdateMultiple | Dataverse merges the pipelines: Create and Update steps also run for the bulk messages [L11]. Registering both would apply the logic twice [L11]. Moving to the bulk messages later is an import-performance step, done as a swap, never as an addition |
| Associate, Disassociate | The logical model has no many-to-many relationships (document 20) |
| Asynchronous steps | Messages for Flag level, reminders and approvals are flows (D-10). The engine adds no asynchronous plug-ins in the first release |
| `Execute` | Learn says never register on it [L4] |

**Budget.** Each synchronous step targets under 100 milliseconds warm: one version read, zero to three fact reads, in-memory evaluation. Learn's guidance is under 2 seconds per plug-in and 2 minutes per message operation [L3]. Measured in spike S6.

### 11.4 Custom API signatures

**Shape rules.**

| Rule | Why |
|---|---|
| Binding type Global for every API | "You can compose all your custom API as Global" [L14]. Pages do not support the EntityCollection binding type [L16] |
| No Entity, EntityCollection, EntityReference, Picklist, Money or StringArray parameters or responses | Pages do not support Entity parameters [L16]; Guid and String are portable to the Azure endpoints and to every MCP client. StringArray support in pages and in `invoke_api` is UNVERIFIED, so lists travel as JSON strings |
| Types used: String, Guid, Integer, Boolean, DateTime | From the custom API type list [L15] |
| Parameter unique names in lowerCamelCase, the contract layer's names | Unique names cannot contain special characters and cannot change after saving [L15]; one name set for both platforms (document 20, section 3.3) |
| Reads with only keys and IDs are Functions; anything with free text or JSON input is an Action | Functions are GET with a 32 KB URL limit, and string values with characters such as `/ < > * % & : \ ? +` need parameter aliases [L14]. Power Automate's Dataverse connector calls only actions [L14] |
| Every Function returns at least one response property | Required for a valid Function [L14] |
| Is Private false | Private messages are hidden from `$metadata` and cannot be used in plug-ins [L14]; MCP `search` and `describe` and page binding need them visible (UNVERIFIED which metadata `invoke_api` reads) |
| Allowed custom processing step type None | Nobody else may change or cancel engine operations [L14] |
| Is Customizable false in the managed solution | Learn: set it to false when shipping [L14] |
| Execute Privilege Name from the product's own table privileges | New privileges cannot be created; Learn suggests using a custom table's privileges [L14] |
| Every response carries `outcome`, `resultJson` (the full EvaluationResult) and `configurationVersion` | One parsing path for pages, MCP and pipelines |
| The Function or Action choice is final | It cannot change after creation [L14] |
| Not Enabled for Workflow | Workflow mode forbids Functions and some types [L14]; flows call actions through the connector instead |

**The 16 engine APIs.** Names use `<prefix>_` plus a PascalCase verb and noun (document 20, section 3.4). An engine API that backs an MCP tool takes that tool's name (document 24, section 9.1), so `GetDependencyImpact` matches `get_dependency_impact` and `CheckChangeApproval` matches `check_change_approval`. Business tools (register an application, start a change request, record a decision) are designed in the MCP surface document; they are plain table writes or their own custom APIs, and they all pass through the guard.

| Custom API | Kind | Operation | Request parameters | Response properties beyond the common three | Execute privilege (proposal) | When |
|---|---|---|---|---|---|---|
| `<prefix>_EvaluateTier` | Action (no writes) | OP-01 | applicationKindKey, answersJson, proposedTierKey (optional), clientApp | computedTierKey | Read on Tier Assessment | R1 |
| `<prefix>_GetRequiredApprovals` | Function | OP-02 | decisionTypeKey, businessApplicationId (optional), changeRequestId (optional), targetStageKey (optional) | requiredCount | Read on Decision | R1 |
| `<prefix>_GetApprovalStatus` | Function | OP-03 | decisionId | isSatisfied | Read on Decision | R1 |
| `<prefix>_RequestApprovals` | Action | OP-02 then writes (approval records, consultation cards, or an accept-decision effect for the locked list) | decisionId, idempotencyKey, clientApp, actorKind | requestedCount (0 with `engine-accept`) | Write on Decision | R1 |
| `<prefix>_CheckChangeWindow` | Function | OP-05 | changeRequestId, atUtc | windowState, minutesOutside | Read on Change Request | R1 |
| `<prefix>_GetDocumentationStatus` | Function | OP-06 | businessApplicationId | isComplete, missingCount, staleCount | Read on Document Link | R1 |
| `<prefix>_GetGateReadiness` | Function | OP-07 | businessApplicationId, gateKey | isReady | Read on Business Application | R1 |
| `<prefix>_GetDependencyImpact` | Function | OP-10 | businessApplicationId, direction, depth | reachedCount | Read on Dependency | R1 |
| `<prefix>_RecordDeployment` | Action | OP-04 record mode, then upsert | idempotencyKey, businessApplicationId (optional), artifactName, artifactVersion, artifactKindKey, digest, sourceCommit, targetExternalId, pipelineExternalId, routeKey, runUrl, startedOnUtc, finishedOnUtc, resultKey, changeRequestId (optional), versionBefore, versionAfter, codeApprovalKind, startedByAiClient, mergeActor, clientApp, actorKind | recordId, created, gateLevel, isOutsideWindow, isOtherPath, isMissingApproval | Create on Deployment Record | R1 (record step sample) |
| `<prefix>_CheckChangeApproval` | Action | OP-04 check mode | businessApplicationId, changeRequestId (optional), targetExternalId, plannedStartUtc, routeKey, codeApprovalKind, idempotencyKey | verdict, gateLevel, failMode | Read on Change Request | After the gate decision (D-03); writes L05 Gate Check later |
| `<prefix>_ProposeConfigurationChange` | Action | OP-09, then save or update a Configuration Draft (C46), unless validateOnly. Submitting is a separate call: `submit_record` with subject kind configuration draft (document 24, tool 18), which freezes the draft and records the decision | configurationDraftId (optional, to edit an unsubmitted draft), changeSetJson, baseVersionNumber, proposalRationale, validateOnly (optional), idempotencyKey, clientApp, actorKind | configurationDraftId, isValid, errorCount | Write on Configuration Draft | R1 |
| `<prefix>_ValidateConfiguration` | Function | OP-09 | configurationDraftId or configurationSetVersionId (one of them) | isValid, errorCount, warningCount | Read on Configuration Set Version | R1 |
| `<prefix>_GetEngineInfo` | Function | none | none | engineVersion, snapshotSchemaVersion, snapshotHash | Read on Configuration Set | R1 |
| `<prefix>_CheckStandardChange` | Function | OP-12 | changeRequestId | qualifies, modelKey | Read on Change Request | R1 |
| `<prefix>_CompareInventoryList` | Action | OP-13, then claim writes | gapCheckRunId (optional), estateKey, itemsJson, isFinalPage, sourceNote, idempotencyKey, clientApp, actorKind | gapCheckRunId, newClaims, vanishedClaims | Write on Claim | R1 |
| `<prefix>_RecordChangeAuthorization` | Action | OP-14 after the write | changeRequestId, authorizationState, evidenceUrl, reportedOnUtc, externalChangeNumber, idempotencyKey, clientApp | authorized, basis | Create on Evidence Link | R1, for client-built ITSM steps (working default, 28 Q22). Attestation arrives through the approval runner's confirmation card instead |

**Parameter dictionary** (each name means the same thing on every API and on the Azure endpoints).

| Name | Type | Description |
|---|---|---|
| actorKind | String | Optional assertion by the product's MCP server: `ai-client` when an AI client acts for the signed-in person. Can only lower trust |
| answersJson | String | JSON array of `{questionKey, answerKey}` |
| applicationKindKey | String | Key of an Application Kind (C15) |
| artifactKindKey | String | Key of a value in the `artifact-kind` value list |
| artifactName | String | The artifact's name, such as a solution unique name or image name |
| artifactVersion | String | The artifact's version text |
| atUtc | DateTime | The moment to test, in UTC |
| authorizationState | String | authorized, not authorized or cancelled, as the ITSM tool reports it |
| baseVersionNumber | Integer | The active version number the patch starts from (V42) |
| businessApplicationId | Guid | A Business Application row (O02) |
| changeRequestId | Guid | A Change Request row (O11) |
| changeSetJson | String | The patch: upsert and retire operations by logical entity and key (document 21, section 8.2) |
| clientApp | String | The MCP client or app making the call, such as "Claude Code" |
| codeApprovalKind | String | person, ai-only, none or unknown: who approved the code merge (sweep proposal). Evidence only |
| configurationDraftId | Guid | A Configuration Draft row (C46) |
| configurationSetVersionId | Guid | A Configuration Set Version row (C02) |
| decisionId | Guid | A Decision row (O09) |
| decisionTypeKey | String | Key of a Decision Type (C18) |
| depth | Integer | Hops to walk, capped by `impact-max-depth` |
| digest | String | The artifact's hash, when the sender has one |
| direction | String | dependants, dependencies or both |
| estateKey | String | Key of a value in the `estate` value list |
| evidenceUrl | String | A link to the ITSM record that shows the authorization state |
| externalChangeNumber | String | The ITSM change number, matched to O11 External Change Number. Shown, never decisive |
| finishedOnUtc | DateTime | When the deployment finished |
| gapCheckRunId | Guid | A Gap Check Run row (O22); empty to start a run |
| gateKey | String | Key of a lifecycle Gate (C13) |
| idempotencyKey | String | The caller's retry key, 1 to 100 characters, such as run ID plus stage |
| isFinalPage | Boolean | True when the call carries the last page of every covered estate; only then are vanished claims raised |
| itemsJson | String | JSON array of inventory items: externalId, sourceType, componentKindKey, labelAsSeen, targetAsSeen, suggestedClaimantObjectId (up to 1000) |
| mergeActor | String | Who completed the merge that triggered the run, including an AI agent (sweep proposal) |
| pipelineExternalId | String | The pipeline's ID in its own tool, matched to O16 External Id |
| plannedStartUtc | DateTime | When the deployment is about to start |
| proposalRationale | String | Why the change is wanted, in the admin's words. The change summary is computed by the engine, never taken from the caller |
| proposedTierKey | String | A tier a person proposes |
| reportedOnUtc | DateTime | When the ITSM tool reported the authorization state |
| resultKey | String | Key of a Deployment Record status (C05) |
| routeKey | String | Key of a Deployment Route (C36) |
| runUrl | String | A link to the pipeline run |
| sourceCommit | String | The source commit the artifact was built from |
| sourceNote | String | Where the inventory list came from, in plain words; the run is marked "as reported" |
| startedByAiClient | String | The AI client that queued the run for a person, if any (sweep proposal) |
| startedOnUtc | DateTime | When the deployment started |
| targetExternalId | String | The target's ID in its own tool, matched to O15 External Id. May contain `/`, hence Actions only |
| targetStageKey | String | Key of a Target Stage (C34) |
| validateOnly | Boolean | True to validate and return the diff and the effect on live applications without saving a draft |
| versionBefore, versionAfter | String | The versions read on the target before and after |

**Common response properties**

| Name | Type | Description |
|---|---|---|
| outcome | String | The operation's outcome word |
| resultJson | String | The full EvaluationResult as JSON, including reasons and trace |
| configurationVersion | Integer | The configuration version used |

**Generative pages.** Page calls to custom APIs are preview, run as the signed-in user under Dataverse security, and are "available worldwide in public clouds" through the AI code tools [L16]. Pages bind only Global or Entity APIs [L17]; all 16 are Global. Classic forms stay the fallback while page features are preview (D-18).

**MCP.** The Dataverse MCP server's `invoke_api` calls custom APIs [L18]. "MCP allow listing applies only to the `/api/mcp` agent entrypoint", and custom APIs are not restricted by it [L18], so security stays in plug-ins and roles, never in an MCP allow list (sweep proposal, D-10 row). The product's own MCP server calls the same APIs through the Web API on behalf of the user (D-09).

### 11.5 Privileged bypass and the consistency check

**The fact.** A caller can send `BypassBusinessLogicExecution` with `CustomSync` to skip custom synchronous logic, including the engine. It needs the `prvBypassCustomBusinessLogic` privilege, which "by default, only users with the system administrator security role have" [L12]. Microsoft-published solutions are exempt; the product is not [L12].

**What it means.** On Dataverse, an environment system administrator can write past the engine. On Azure, a database owner can write past the API. No design prevents a platform's top administrator; the engine can only detect.

**Detect, plus guidance (working default, 28 Q16).**
1. Every engine-evaluated write stamps Engine Version and Engine Checked On in PreOperation (G-09). Both are common columns in document 22 (working default, 28 issue 15).
2. A scheduled consistency check (new term) finds product rows whose Modified On is later than Engine Checked On beyond a small tolerance. It runs with a read-only workload identity and never changes the rows it reads. It re-runs OP-08 on each row in memory and records each finding as an Audit Entry "write outside the engine", with the result.
3. The finding appears on the platform admin's queue and, by configuration, to the change board.
4. The setup guide keeps the top admin roles for break-glass accounts managed in Entra: system administrator on Dataverse and database owner on Azure. No other role gets the bypass privilege.

---

## 12. Azure adapter

### 12.1 Host

| Item | Design | Source |
|---|---|---|
| Host | One Azure Functions app, isolated worker model, `net10.0`, on the Flex Consumption plan (Linux) | [L24]; working default, 28 issue 13 |
| Same service | The REST endpoints, the product's MCP server and the engine live in one app, both calling the engine runtime in process | D-26; working default, 28 issue 13 |
| Fallback | Azure Container Apps, if the on-behalf-of spike or the MCP spike fails on Functions (spike S11). The spike confirms the host. The engine library and the adapter do not change | Working default, 28 issue 13 |
| Timeouts | Engine calls take milliseconds; even an activation stays far below the 230-second HTTP limit [L25] | [L25] |
| Sign-in | Entra bearer tokens. Product roles arrive as app roles assigned to the Entra groups bound in C30 (needs Entra ID P1 or P2; nested groups not supported) [S9] | D-11 |
| Only write path | Database permissions allow writes only from the app's managed identity. Every write endpoint runs OP-08 first | D-10, D-22 |
| Idempotency | An `Idempotency-Key` request header for writes, stored as the Audit Entry Request Key (section 8) | Section 8 |
| Concurrency | Optimistic: an ETag from the row version, `If-Match` on updates; a mismatch returns 412 | (convention) |
| Description | An OpenAPI description generated from the endpoint contracts, published with each release | (convention) |

### 12.2 Endpoints

Every endpoint returns the same EvaluationResult JSON as the matching custom API's `resultJson`. Field names are identical to the custom API parameter dictionary.

| Operation | Endpoint | Matches |
|---|---|---|
| OP-01 | `POST /engine/v1/tier-evaluations` | `<prefix>_EvaluateTier` |
| OP-02 | `GET /engine/v1/required-approvals?decisionTypeKey=&businessApplicationId=&changeRequestId=&targetStageKey=` | `<prefix>_GetRequiredApprovals` |
| OP-03 | `GET /engine/v1/decisions/{decisionId}/approval-status` | `<prefix>_GetApprovalStatus` |
| OP-02 and writes | `POST /engine/v1/decisions/{decisionId}/approval-requests` | `<prefix>_RequestApprovals` |
| OP-05 | `GET /engine/v1/change-requests/{changeRequestId}/window-check?atUtc=` | `<prefix>_CheckChangeWindow` |
| OP-06 | `GET /engine/v1/business-applications/{id}/documentation-status` | `<prefix>_GetDocumentationStatus` |
| OP-07 | `GET /engine/v1/business-applications/{id}/gates/{gateKey}/readiness` | `<prefix>_GetGateReadiness` |
| OP-10 | `GET /engine/v1/business-applications/{id}/impact?direction=&depth=` | `<prefix>_GetDependencyImpact` |
| OP-04 record mode | `POST /engine/v1/deployment-records` | `<prefix>_RecordDeployment` |
| OP-04 check mode | `POST /engine/v1/gate-checks` | `<prefix>_CheckChangeApproval` (after the gate decision) |
| OP-09 and save a draft | `POST /engine/v1/configuration/drafts` | `<prefix>_ProposeConfigurationChange` |
| OP-09 | `GET /engine/v1/configuration/validation?configurationDraftId=&configurationSetVersionId=` | `<prefix>_ValidateConfiguration` |
| Info | `GET /engine/v1/info` | `<prefix>_GetEngineInfo` |
| OP-12 | `GET /engine/v1/change-requests/{changeRequestId}/standard-check` | `<prefix>_CheckStandardChange` |
| OP-13 | `POST /engine/v1/gap-check-runs/items` | `<prefix>_CompareInventoryList` |
| OP-14 after the write | `POST /engine/v1/change-requests/{changeRequestId}/authorization-facts` | `<prefix>_RecordChangeAuthorization` |
| Approval outcome | `POST /engine/v1/approval-records/{id}/outcome` | On Dataverse, the approval runner updates the row and the guard checks it. On Azure, the approval runner calls this endpoint with the approval writer workload role (working default, 28 issue 7 and Q19) |

All other Azure binding writes (register an application, start a change request, record a decision, link a document) are ordinary resource endpoints designed with the MCP surface; each runs OP-08 before it writes.

### 12.3 Data access and security

| Item | Design | Source |
|---|---|---|
| Driver and identity | A SQL client driver with the app's managed identity | (convention) |
| Per-request user context | After opening a connection, the adapter sets session context with the caller's object ID, actor kind and client app, read-only for the connection's life, so row-level security predicates and audit triggers can read them. Microsoft documents this pattern for middle-tier apps that share one database user. The engine connects with the app's managed identity, never with the person's own SQL token (working default, 28 Q20) | [L26] |
| Transactions | One transaction per write request: OP-08 plan, row writes, effects and the Audit Entry together | (convention) |
| Ledger tables | As document 22, section 11.2: updatable ledger tables for Tier Assessment, Decision and Approval Record, frozen once final by block predicates; append-only ledger tables for Configuration Set Version, Deployment Record, Evidence Link and Audit Entry [S18] (working default, 28 Q21). Configuration Draft is an ordinary updatable table, frozen at submit by the guard | [S18] |
| Configuration history | Configuration Set Version snapshots, plus optional temporal tables on the `config` schema [S19] | [S19] |
| Snapshot storage | Native `json` column for Content Json [S20] | [S20] |
| Fact queries | One parameterized query per fact plan entry, batched in one round trip where possible | (convention) |

---

## 13. Cost of the portable shape

- **What the hedge costs.** Writing the same rules straight into plug-in classes would avoid the ports, the column map and the plan, fetch, evaluate split (section 3.3). That saving is a small part of the Dataverse path.
- **What the hedge buys.** If the store changes, the library and every fixture carry over; only the other adapter is new.
- **Where the platforms differ.** Most of the difference between a Dataverse build and an Azure build sits in the schema, audit, security and screens, not in the engine.
- **Not covered here.** The physical schema and migrations, the screens, the MCP server, the approval runner and the pipeline step samples belong to other step 2 documents.

---

## 14. Spikes and risks

| ID | Spike | What would change |
|---|---|---|
| S1 | A `net48` library and `System.Text.Json` load from a plug-in package in a managed development environment | If not, the library build for Dataverse drops to `net462` (Learn's example [L2]) |
| S2 | The engine-nested marker through `SharedVariables` and `ParentContext` cannot be forged by a client's `tag`, and it identifies activation's writes to configuration tables | If it can be forged, elevation is allowed only inside named engine effects, each with an Audit Entry (working default, 28 issue 9 and Q15) |
| S3 | The `PluginHttpStatusCode` constructor works in the sandbox [L9] | If not, status codes come from the platform's default mapping and the reason code stays in the message |
| S4 | What `InitiatingUserAgent` shows for the product's MCP server and for the Dataverse MCP server [L19] | Better or worse "performed by" accuracy; no safety change (section 11.2) |
| S5 | An approver's own AI client, signed in as the approver, tries to write an approval outcome: refused | None expected; a failure is a release blocker |
| S6 | Warm and cold timings of guard plus compute, with a 55-tier snapshot | Cache strategy and step split |
| S7 | A Function returning `resultJson` is callable from a generative page and through `invoke_api` with String and Guid parameters | If not, more Actions, or flattened response properties |
| S8 | A system administrator's bypass header is caught by the consistency check | Detection design (section 11.5) |
| S9 | Azure: session context, row-level security and a ledger insert per request stay under 100 milliseconds | Connection and batching design |
| S10 | Snapshot size limits and the file column fallback | Snapshot storage on Dataverse |
| S11 | Azure: the on-behalf-of flow and the MCP server both work on Functions Flex Consumption (shared with document 24's spikes) | If either fails, the host moves to Azure Container Apps; the library and the adapter do not change (working default, 28 issue 13) |

---

## 15. Hand-offs to other step 2 documents

| Document | What this design asks of it |
|---|---|
| 20 Glossary and entities | Add the new terms in section 16. Add Snapshot Schema Version to C02 (C02 is created only at activation; there is no staged C02 row); the condition kinds of OP-12 and the authorization fact source kinds of OP-14 to the engine contract list. Stage Rank, Configuration Draft (C46), activation, approval runner and approval writer are already recorded there (28, issues 3, 7 and 15) |
| 21 Configuration model | V28: no zero waiver, as document 21 says; the locked list works through the configuration facts of OP-02 rule 8, never through a Minimum Count 0 (settled by a design review on 2026-11-22); the engine checks after the V01 to V44 table (section 5.11); the condition kinds for Conditions Json (C21) |
| 22 Logical data model and ERD | Engine Version and Engine Checked On as common columns, and Request Key and Request Hash on O24 (28, issue 15); Is Consultation on C32; C46 Configuration Draft; the authorization fact's columns on O20 or its own record; alternate keys on O14 Idempotency Key, O23 Claim Key and O24 Request Key; engine-owned columns marked read-only in forms |
| 23 Processes | The emergency follow-up (automatic acceptance is settled in document 23, S1 step 8); the consistency check finding's owner; the linked-mode attestation card (OP-14) |
| 24 MCP surface | Each tool names the engine operation it calls. `check_standard_change` calls OP-12; `run_gap_check` and `compare_inventory_list` call OP-13; `explain_rules` returns the rule trace that every EvaluationResult carries; `get_configuration_health` reads `GetEngineInfo` and the C02 history; `propose_configuration_change` saves a C46 draft, not a C02 row. The product's MCP server always passes `clientApp`, `actorKind` and an `idempotencyKey` for writes |
| 26 Security model | Effects run as the caller (section 5.10), so the approval writer's privilege set covers the tables the effects write, and the guard limits them to engine-nested writes. Column security on approval outcome columns. The read-only identity of the consistency check |
| Change gate design (D-03) | Check steps cache the fail mode returned by OP-04 and use it when the product cannot answer, including on a broken configuration |
| If the open design questions in 28, section 3, are answered differently | Question 1 option B turns section 5's fixed rules into a rule language (much larger library). Question 7 option B ships `default-ai-only-code-approval-handling` at ignore; option C makes the AI approval rules a switch per client. Question 8 option B removes `engine-acceptable-decision-types` and the `engine-accept` outcome; option C lets configuration decide which decisions need no card. If working default Q2 is overridden, option B gives each portfolio its own snapshot and cache key, and option C removes portfolio precedence from OP-02 and OP-04 |

---

## 16. New terms

Proposed for `20-glossary-and-entities.md`, except where a row says it is now there. This document does not edit the glossary. Configuration Draft (C46), activation, approval runner and specificity come from document 20 and are not repeated here.

| Term | Definition |
|---|---|
| Engine core | The platform-neutral rules library: contract records and the fourteen pure operations. No input or output |
| Engine runtime | The platform-neutral layer that loads snapshots, plans and fetches facts through ports, and returns results and write plans |
| Port | An interface the engine runtime defines and an adapter implements, such as the configuration store or the fact store |
| Adapter | The thin, store-specific layer that implements the ports and maps callers, errors and names for one platform |
| Host | The process that runs an adapter: the Dataverse plug-in sandbox, or the Azure Functions app |
| Configuration snapshot | The in-memory, indexed form of the active Configuration Set Version's Content Json. The engine's only configuration input |
| Snapshot schema version | The version of the snapshot's own shape. The engine refuses a major version it does not know |
| Stage Rank | A number on each Lifecycle Stage (C12) that orders stages so the engine can test "at or after a stage". Like Tier Rank: compared, never shown as logic. Now in document 20 (28, issue 15) |
| Engine Version | Common column: the engine library version that evaluated the last write. Added to document 22 (28, issue 15) |
| Engine Checked On | Common column: when the engine last evaluated the row. A later Modified On means a write outside the engine. Added to document 22 (28, issue 15) |
| Engine stamp | Engine Version plus Engine Checked On, next to the Configuration Set Version stamp |
| Engine-owned column | A column only the engine sets, such as Counts Toward Requirement, the deployment flags, Gate Level Applied, Computed Tier and Stale On |
| Engine-nested write | A write the engine makes as an effect of an earlier engine operation, recognized by a marker the adapter sets |
| Reason code | A stable, lowercase, dot-separated code for every refusal, warning and note, such as `approval.ai-actor-not-counted` |
| Evaluation result | The common return record of every operation: outcome, payload, reasons, rule trace, versions and hash |
| Rule trace | The configuration rows, by entity and key, that decided an outcome |
| Fact plan | The list of typed queries the engine core asks the adapter to run before it evaluates |
| Write plan | OP-08's output: allowed or refused, the columns to stamp, and the effects to apply |
| Effect | A follow-on write the engine asks the adapter to apply in the same transaction, such as updating a decision's subject |
| Request Key | O24 column: the caller's idempotency key for a write call, unique per operation |
| Request Hash | O24 column: a hash of the request payload, so a reused key with a different payload is refused |
| Binding parity test | A run of the same fixtures through the library alone and through each adapter, which must agree |
| Consistency check | A scheduled check, run by a read-only workload identity, that finds product rows changed outside the engine and records each finding as an Audit Entry (working default, 28 Q16) |
| Approval writer | The workload role (C29) held by the approval runner's identity; the only role that may write approval outcomes. Earlier drafts of this document called it the approval recorder role. Now in document 20 (28, issue 7) |
| Engine acceptance list | The locked setting `engine-acceptable-decision-types`: the only decisions the engine may accept without a person's card, intake at the lowest tier and standard changes (working default, open design question 8; see 28-working-defaults.md). Defined in document 21 |
| Accept-decision effect | The effect that moves a decision to accepted, after OP-03 says satisfied or for an entry of the engine acceptance list. The only way a decision becomes accepted (G-17) |
| Activate-configuration effect | The effect that runs activation for an accepted configuration change decision (section 6.4). It has no API of its own |
| Condition kind | One of a short engine contract list of tests a standard change model's Conditions Json may use (OP-12): `max-tier-rank`, `target-stages-in`, `max-affected-applications`, `component-kinds-within`, `max-window-minutes`, `stated` |
| Authorization fact | In linked mode, a recorded statement that the external change is authorized, not authorized or cancelled, with an evidence link, from a client-built ITSM step or a person's attestation (OP-14; working default, 28 Q22) |
| Gap compare plan | OP-13's output: the component stamps and the claim creations and updates that the runtime applies through OP-08 |

---

## 17. Microsoft sources

All checked 2026-11-22 on Microsoft Learn through the Microsoft Learn MCP tools. Glossary sources [S7], [S9], [S15], [S18], [S19], [S20] are listed in document 20, section 4.

| ID | Fact used | URL |
|---|---|---|
| L1 | Plug-in assemblies may target .NET Framework 4.6.2 through 4.8; plug-in projects should target 4.8 | https://learn.microsoft.com/power-apps/developer/data-platform/supported-customizations#support-for-net-framework-versions |
| L2 | Plug-ins must target .NET Framework; 16 MB limit; signing rules; SDK assemblies in the sandbox; dependent assemblies in NuGet plug-in packages; no ILMerge; package name and version fixed once created; SDK-style projects; class library example with `net462`; include `System.Text.Json` explicitly; package limitations; import time grows with type count | https://learn.microsoft.com/power-apps/developer/data-platform/build-and-package |
| L3 | A message operation must finish within 2 minutes including all plug-ins; keep a plug-in under 2 seconds; CPU and memory limits | https://learn.microsoft.com/power-apps/developer/data-platform/analyze-performance |
| L4 | Pipeline stages: PreValidation before security checks and outside the transaction; change values in PreOperation; MainOperation for custom APIs; PostOperation; rollback on synchronous exceptions; cancel in PreValidation; Update may run twice; don't register on Execute | https://learn.microsoft.com/power-apps/developer/data-platform/event-framework |
| L5 | Entity images by stage and message; post-images only in PostOperation; steps are solution components | https://learn.microsoft.com/power-apps/developer/data-platform/register-plug-in |
| L6 | Plug-in instances are cached and shared across threads; read-only, static and constant members are thread-safe | https://learn.microsoft.com/power-apps/developer/data-platform/best-practices/business-logic/develop-iplugin-implementations-stateless |
| L7 | Best practices: no parallel execution, no batch requests in plug-ins, filtering attributes, limit Retrieve and RetrieveMultiple plug-ins, use tracing | https://learn.microsoft.com/power-apps/developer/data-platform/best-practices/business-logic/ |
| L8 | `InvalidPluginExecutionException` returns a friendly message with `IsvAborted`; other exceptions show a stack trace; no HTML in messages | https://learn.microsoft.com/power-apps/developer/data-platform/best-practices/business-logic/use-invalidpluginexecutionexception-plugin-workflow-activities |
| L9 | Constructors with `PluginHttpStatusCode` and an error code (SDK reference, package Microsoft.PowerPlatform.Dataverse.Client 1.2.26) | https://learn.microsoft.com/dotnet/api/microsoft.xrm.sdk.invalidpluginexecutionexception.-ctor |
| L10 | A constructor with an exception details dictionary; Web API error annotations including `SubErrorCode` through the `Prefer` header | https://learn.microsoft.com/power-apps/developer/data-platform/write-plugin-multiple-operation ; https://learn.microsoft.com/power-apps/developer/data-platform/webapi/compose-http-requests-handle-errors |
| L11 | Bulk message pipelines are merged with Create and Update; logic on both applies twice | https://learn.microsoft.com/power-apps/developer/data-platform/bulk-operations |
| L12 | `BypassBusinessLogicExecution` skips custom logic; needs `prvBypassCustomBusinessLogic`, held by default only by system administrators; Microsoft-published solutions are exempt | https://learn.microsoft.com/power-apps/developer/data-platform/bypass-custom-business-logic |
| L13 | Plug-ins may reach the network over HTTP and HTTPS only; no localhost; no IP addresses | https://learn.microsoft.com/power-apps/developer/data-platform/access-web-services |
| L14 | Custom APIs: binding types, compose all as Global, Functions are GET with a 32 KB URL limit and special-character aliases, Functions need a response property, Function or Action fixed at creation, Is Private effects, Execute Privilege Name with existing or custom table privileges, Is Customizable false when shipping, processing step types, main plug-in gets no step configuration, the Dataverse connector calls actions only, workflow-enabled limits | https://learn.microsoft.com/power-apps/developer/data-platform/custom-api |
| L15 | Custom API parameter and response types (Boolean to Guid); unique names without special characters, fixed once saved | https://learn.microsoft.com/power-apps/developer/data-platform/custom-api-tables |
| L16 | Generative pages: custom API calls preview; EntityCollection binding and Entity parameters not supported; run as the signed-in user; AI code tools available worldwide in public clouds | https://learn.microsoft.com/power-apps/maker/model-driven-apps/generative-page-external-tools |
| L17 | Page `executeAction` and `executeFunction` results: `ok`, `status`, `outputs`, `indeterminate` (don't auto-retry), error codes including `not_permitted`; bound APIs are Global or Entity | https://learn.microsoft.com/power-apps/developer/model-driven-apps/generative-page/data-api/dataverse-operation-methods |
| L18 | Dataverse MCP server tools include `invoke_api`; MCP allow listing applies only to `/api/mcp`, not to custom APIs | https://learn.microsoft.com/power-apps/maker/data-platform/data-platform-mcp ; https://learn.microsoft.com/power-apps/maker/data-platform/data-platform-mcp-disable |
| L19 | Execution context: `InitiatingUserAzureActiveDirectoryObjectId`, `InitiatingUserApplicationId` (empty for non-application users), `InitiatingUserAgent` | https://learn.microsoft.com/dotnet/api/microsoft.xrm.sdk.ipluginexecutioncontext2.initiatinguserapplicationid ; https://learn.microsoft.com/dotnet/api/microsoft.xrm.sdk.ipluginexecutioncontext5.initiatinguseragent |
| L20 | Multiline text holds up to 1,048,576 characters | https://learn.microsoft.com/power-apps/maker/data-platform/types-of-fields |
| L21 | Share code between .NET Framework and .NET through .NET Standard 2.0; consuming it from 4.6.1 has issues; use 4.7.2 or higher | https://learn.microsoft.com/dotnet/standard/net-standard |
| L22 | .NET Framework 4.6.2 ends support on January 12, 2027 | https://learn.microsoft.com/lifecycle/end-of-support/end-of-support-2027 |
| L23 | .NET 8 ended servicing November 10, 2026; .NET 10 (LTS) and .NET 11 (STS) supported until November 2028 | https://learn.microsoft.com/lifecycle/end-of-support/end-of-support-2026 ; https://learn.microsoft.com/dotnet/core/releases-and-support |
| L24 | Azure Functions 4.x isolated worker supports .NET 11, 10, 9, 8 and .NET Framework 4.8; in-process support ends November 10, 2026; .NET 10 or later on Linux needs Flex Consumption | https://learn.microsoft.com/azure/azure-functions/dotnet-isolated-process-guide#supported-versions |
| L25 | HTTP-triggered functions must respond within 230 seconds; Flex Consumption default timeout 30 minutes | https://learn.microsoft.com/azure/azure-functions/functions-scale#timeout |
| L26 | Row-level security with `SESSION_CONTEXT` for middle-tier apps sharing one database user | https://learn.microsoft.com/sql/relational-databases/security/row-level-security#MidTier |

**UNVERIFIED in this document:** a `net48` dependent assembly loading from a plug-in package; a `netstandard2.0` assembly loading in the sandbox; the `PluginHttpStatusCode` constructor in the sandbox; whether a client can forge the engine-nested marker; whether the delegated token's client application is visible to plug-ins; what `InitiatingUserAgent` shows for MCP callers; StringArray support in pages and in `invoke_api`; which metadata `invoke_api` reads; whether one sandbox worker serves several environments; agent user mapping in Dataverse; file column latency for oversized snapshots.

---

## 18. Diagram specs

All diagrams use the palette of D-04: navy `0A2A4A` for text and lines, blue `0071DC` for group header bars, gold `FFC221` as the accent. Fills are light only: white `FFFFFF`, light blue `E8F1FB`, light gold `FFF4D6`, light gray `F3F5F8`. No dark fills. Built in Visio.

### D25-1 The engine's layers on both platforms

- **Title:** One engine, two bindings.
- **Purpose:** Shows that the rules live once in a platform-neutral library and that each store gets a thin adapter (D-10, D-22).
- **Groups (boxes with blue header bars):**
  1. Callers (light gray, top): MCP clients (Claude Code as reference), the product's MCP server, generative page or web page, approval runner, pipeline record and check steps, ITSM step, model-driven app (Dataverse only).
  2. Platform-neutral library (light gold, center): Engine core (OP-01 to OP-14), Engine configuration (snapshots, draft patches, seed), Engine runtime (plan, fetch, evaluate; cache), Engine testing (fixtures, parity).
  3. Dataverse binding (light blue, left): plug-in package (`net48`), four plug-in types, 16 global custom APIs, Dataverse tables.
  4. Azure binding (light blue, right): Azure Functions app (`net10.0`, isolated, Flex Consumption; Container Apps as fallback), 17 endpoints, Azure SQL (ledger, row-level security).
- **Nodes:** as listed, plus a small node "Reason code catalog" under the core.
- **Edges (navy, labeled):** callers to custom APIs "invoke_api, Web API, executeFunction"; callers to endpoints "HTTPS, Entra token"; custom APIs and plug-ins to Engine runtime "same library"; endpoints to Engine runtime "same library"; Engine runtime to Dataverse tables "fact and configuration ports"; Engine runtime to Azure SQL "fact and configuration ports"; Engine testing to both bindings "binding parity".
- **Accent:** a gold border on the library group with the label "no Dataverse SDK, no SQL driver inside".

### D25-2 A write through the Dataverse pipeline

- **Title:** Every write meets the engine three times.
- **Purpose:** Shows where the guard, the stamps and the effects run in Dataverse's stages [L4].
- **Lanes (left to right):** Caller; PreValidation (10); Platform security check; PreOperation (20); Main operation (platform write); PostOperation (40); Flows (after commit).
- **Nodes:**
  1. Caller: "Create or update a row (app, MCP, page, flow, import)".
  2. PreValidation: "OP-08 guard: configuration only through activation, preset, final row, outcome writer, AI status".
  3. Decision diamond: "Refused?" with a branch to "InvalidPluginExecutionException with reason code".
  4. Security check: "Dataverse roles through Entra group teams".
  5. PreOperation: "Stamp performed-by, configuration version, engine stamp; compute engine-owned columns".
  6. Main operation: "Row written".
  7. PostOperation: "Effects: recompute approvals, accept decision, update subject, supersede, activate configuration".
  8. Flows: "Notify (Flag), send approval cards, reminders".
- **Edges:** 1 to 2 "request"; 2 to 3 "plan"; 3 to refusal "yes"; 3 to 4 "no"; 4 to 5; 5 to 6; 6 to 7 "same transaction"; 7 to 8 dotted "after commit".
- **Accent:** a gold note on lane 2: "AI never approves: only the approval runner writes outcomes".

### D25-3 A request on the Azure binding

- **Title:** The API is the only write path.
- **Purpose:** Shows the Azure request flow, the session context and the append-only tables (D-22).
- **Lanes:** Caller; Azure Functions app; Engine runtime; Azure SQL.
- **Nodes:** caller "MCP client, web page, pipeline step, ITSM step, approval runner"; app "Validate Entra token; map app roles to product roles"; app "Read Idempotency-Key"; runtime "Load snapshot (cache by version)"; runtime "Plan facts"; SQL "Fact queries with session context"; runtime "Evaluate; write plan"; SQL "One transaction: rows, effects, ledger insert, audit entry"; app "Result or problem details".
- **Edges:** labeled "bearer token", "roles", "version check", "facts", "write plan", "commit", "200, 201, 4xx, 503".
- **Accent:** a gold lock icon on SQL with "writes only from the app's managed identity".

### D25-4 How a configuration version comes into force

- **Title:** Drafts are patches; the active snapshot is in force.
- **Purpose:** Shows section 6.4 end to end (D-07, D-10; working default, 28 issue 3).
- **Lanes:** Admin (person); AI client (on the admin's behalf); Engine; Configuration store; Approval card; Every engine call.
- **Nodes:** 1 Admin "Instructs a change"; 2 AI client "Prepares a patch"; 3 Engine "Guard: configuration admin role for drafts"; 4 Engine "Apply patch to a preview"; 5 Engine "OP-09 validate"; 6 Store "Configuration Draft (patch, preview, diff); live rows unchanged"; 7 Engine "Configuration change decision: Proposed"; 8 Approval card "Admin accepts the diff (person only)"; 9 Engine "Activate: rebase, revalidate, write live rows, new C02 version, current version number, audit entry"; 10 Every engine call "Version check, reload snapshot, stamp new version".
- **Edges:** 1 to 2 "plain words"; 2 to 3 "ProposeConfigurationChange"; 3 to 4; 4 to 5; 5 to 6 "valid"; 5 back to 2 dotted "issues"; 6 to 7; 7 to 8 "card"; 8 to 9 "accepted"; 9 to 10 "next call".
- **Accent:** a dotted gold edge from 2 to 8 labeled "AI cannot accept".

### D25-5 The gate check and its fail mode

- **Title:** Advise and Enforce, and what happens when the product cannot answer.
- **Purpose:** Shows OP-04 in check mode and the cached fail mode (D-03, open).
- **Lanes:** Client pipeline (check step); Product (custom API or endpoint); Engine.
- **Nodes:** "Check step asks before deploy"; "CheckChangeApproval"; "Find gate policy"; "Compute flags"; "Apply level"; outcomes "allow", "warn", "stop", "allow-with-exception"; "engine.unavailable"; check step "Apply cached fail mode: fail closed stops, fail open goes and flags".
- **Edges:** labeled "plannedStartUtc, route, change", "policy row", "flags", "verdict, failMode", "no answer".
- **Accent:** gold note: "Window and path never stop a deployment (D-13)".

---

## 19. Open design questions

Only questions not settled in the decision records ([docs/decisions/](../../decisions/)). The fail mode default and the first check step sample are already open under D-03 and are not repeated here. This document's own four questions are now working defaults. Four of the six open design questions touch the engine; they are listed after them in short form.

### Question 1: May the engine ever act with more rights than the caller?

Settled as a working default in 28-working-defaults.md (Q15).

### Question 2: How should the product treat writes that bypass the engine?

Settled as a working default in 28-working-defaults.md (Q16).

### Question 3: May the person who asked for something also approve it?

Settled as a working default in 28-working-defaults.md (Q10).

### Question 4: Should the engine library also ship as a NuGet package?

Settled as a working default in 28-working-defaults.md (Q23).

### Open design questions that touch the engine

Each stays open. They are listed, with their context and the recommended option, in [28-working-defaults.md](28-working-defaults.md), section 3, under the same numbers. This document uses the recommended option, marked "(working default, open design question N; see 28-working-defaults.md)".

| 28 Q | Question | Working default used here | What a different answer changes in this document |
|---|---|---|---|
| 1 | Is a short list of fixed engine meanings acceptable under "never hardcoded"? | Yes. About 20 fixed meanings, all labels configurable | Option B turns section 5's fixed rules, and OP-12's condition kinds, into a rule language. The library grows a lot |
| 4 | Does the admin accept the exact configuration change on a card? | Yes, one card with the engine's diff. The instructing admin may accept their own AI's draft; strict separation needs a second admin | Option B lets activation follow the instruction with no card (section 6.4, steps 3 and 4). Option C adds a fixed two-admin C40 floor |
| 7 | Do AI approvals from client tools count? | No. AI-only code approvals never count as authorization, and the shipped gate default flags them | Option B ships `default-ai-only-code-approval-handling` at ignore (OP-04). Option C makes it a switch per client |
| 8 | May the engine ever accept a decision without a person's card? | Only for a locked list: lowest-tier intake and standard changes | Option B removes `engine-acceptable-decision-types` and the `engine-accept` outcome (OP-02, rule 8). Option C lets configuration decide, and the setting is no longer locked |

---

## Proposed by the build (2026-11-22)

- **Source.** The build in `src/`, its reason catalog (`src/engine/Product.Engine.Core/Reasons/ReasonCatalog.cs`) and its logical model (`src/model/logical-model.json`).
- **Status.** Every item below is a proposal. The text above this section keeps its meaning: no settled decision and no working default changes. Where an item rests on one of the six open design questions, it names the question. Each item is accepted or dropped in a later design pass. Design questions the build could not settle are named as open points in the items below.

### B25-1 Reason codes the catalog marks proposed

The build's catalog holds 58 codes that this document does not name yet. Each is used with the severity and category shown. They join the published catalog (section 7) if accepted.

| Code | Operation | Severity | Category | Plain sentence |
|---|---|---|---|---|
| `approval.no-requirement` | OP-03 | info | outcome | The answer belongs to no current requirement of the decision, so it does not count. |
| `approval.sequence-waiting` | OP-03 | info | outcome | A sequential requirement counts only after the earlier requirements are satisfied. |
| `decision.engine-accepted` | OP-02 | info | outcome | The engine accepts this decision without a card because it fits the locked engine acceptance list. (working default, open design question 8; see 28-working-defaults.md) |
| `tier.incomplete` | OP-01 | info | outcome | Some applicable tier questions have no answer yet. |
| `tier.proposed-lower` | OP-01 | warning | outcome | The proposed tier is lower than the answers imply, so lowering it needs a tier decision with a reason. |
| `tier.no-applicable-questions` | OP-01 | error | unavailable | No active tier question applies to the application kind, so no tier can be computed. |
| `gate.outside-window` | OP-04 | warning | outcome | The deployment is outside the change window. |
| `gate.other-path` | OP-04 | warning | outcome | The deployment arrived by a route that the change path of the target does not allow. |
| `gate.missing-approval` | OP-04 | warning | outcome | The change needs authorization and the product does not know that it was authorized. |
| `gate.ai-only-code-approval` | OP-04 | warning | outcome | The only code approval came from an AI, and that never counts as authorization. (working default, open design question 7; see 28-working-defaults.md) |
| `gate.emergency-exception` | OP-04 | warning | outcome | The emergency change may go ahead, and its authorization must follow by the due date. |
| `window.outside` | OP-05 | info | outcome | The moment is outside the change window. |
| `window.none` | OP-05 | info | outcome | The change request has no window. |
| `documentation.missing` | OP-06 | warning | outcome | A required document has no link. |
| `documentation.draft` | OP-06 | warning | outcome | A required document is still a draft. |
| `documentation.stale` | OP-06 | warning | outcome | A required document is past its freshness date. |
| `gate.requirement-decision-missing` | OP-07 | warning | outcome | The gate needs an accepted decision of the required type. |
| `gate.requirement-document-not-current` | OP-07 | warning | outcome | The gate needs a current document of the required type. |
| `gate.requirement-review-not-done` | OP-07 | warning | outcome | The gate needs a done review of the required type. |
| `gate.requirement-owner-missing` | OP-07 | warning | outcome | The gate needs an active assignment of the required owner role. |
| `engine.effect-only` | OP-08 | error | refused (rule) | This row or column changes only as an engine effect, never from a request. |
| `ai.forced-draft` | OP-08 | info | outcome | An AI actor created this row, so the engine set it to draft or proposed and marked it AI created. |
| `record.delete-not-allowed` | OP-08 | error | refused (rule) | This row is never deleted, so withdraw, cancel, archive or end it instead. |
| `record.append-only` | OP-08 | error | refused (rule) | This row is written once and never changes, so add a new row instead. |
| `record.final` | OP-08 | error | refused (rule) | This row reached its final state, so it never changes again, and a new or correcting row is added instead. |
| `subject.change-skipped` | OP-08 | warning | outcome | A change the decision proposed no longer applies to its subject, so the engine skipped it (B25-8). |
| `review.done-incomplete` | OP-08 | error | refused (rule) | A review is done only with its Done On, Reviewer and Outcome Decision (B25-12). |
| `review.outcome-not-accepted` | OP-08 | error | refused (rule) | A review is done only once its outcome decision, of the type its review type records and about its application, is accepted (B25-12). |
| `actor.agent-write-refused` | OP-08 | error | refused (role) | An agent identity or agent user may not write, so a person must make this change. |
| `link.forbidden-url` | OP-08 | error | refused (rule) | The URL carries a part that may hold a secret, such as a signature or a token. |
| `principal.erasure-not-allowed` | OP-08 | error | refused (role) | Only a person holding the platform admin role can erase a principal. (working default, open design question 1; see 28-working-defaults.md) |
| `principal.erasure-final` | OP-08 | error | refused (rule) | An erased principal stays erased, so its label and erasure flag cannot be restored. |
| `decision.withdraw-not-allowed` | OP-08 | error | refused (role) | Only the person who proposed the decision, or its decider, can withdraw it. |
| `actor.workload-grant-missing` | Runtime | error | refused (role) | An app-only caller is accepted only with a current workload grant, and only for that grant's workload role. (working default, open design question 12; see 28-working-defaults.md) |
| `activation.discarded` | Activation | warning | outcome | The accepted draft could not activate, so the engine discarded it and created no version. |
| `activation.already-activated` | Activation | info | outcome | The draft is already activated, so activation returns its version and changes nothing. |
| `bootstrap.version-exists` | Bootstrap | error | refused (rule) | A configuration version already exists, so version 1 cannot be bootstrapped again. |
| `bootstrap.not-allowed` | Bootstrap | error | refused (role) | Only a person signed in as a user, with the platform's install rights, can bootstrap version 1. |
| `impact.cycle-detected` | OP-10 | info | outcome | The dependency walk met an application twice. |
| `impact.depth-capped` | OP-10 | info | outcome | The dependency walk stopped at the maximum depth. |
| `reminder.no-rule` | OP-11 | warning | outcome | No active reminder rule exists for this subject kind, so nobody is reminded. |
| `review.no-cadence` | OP-11 | info | outcome | The review type has no cadence at this tier, so no next due date follows from it. |
| `standard-model.condition-not-met` | OP-12 | info | outcome | A condition of the standard change model is not met. |
| `standard-model.condition-as-reported` | OP-12 | info | outcome | A stated condition counts as reported, because the engine cannot see the work itself. |
| `standard-model.qualifies` | OP-12 | info | outcome | The change fits an approved standard change model. |
| `standard-model.no-candidate` | OP-12 | info | outcome | No active standard change model of this change type can be tried, so the change is not standard. |
| `gap-check.item-ignored` | OP-13 | info | outcome | The item is outside the estates or component kinds this run covers, so it was ignored. |
| `gap-check.as-reported` | OP-13 | info | outcome | The inventory list came from outside the product, so its evidence level is as reported. |
| `gap-check.partial-run` | OP-13 | info | outcome | Vanished components are found only when the final page of a run arrives. |
| `authorization.wrong-source` | OP-14 | warning | outcome | An ITSM step fact counts only from a pipeline, and an attestation only from a person on a confirmation card. |
| `authorization.number-only` | OP-14 | warning | outcome | A recorded external change number alone never authorizes a change. |
| `authorization.revoked` | OP-14 | info | outcome | A later fact says the change is not authorized or is cancelled. |
| `authorization.reported-later` | OP-14 | warning | outcome | An authorization fact dated after the moment of the evaluation never authorizes the change. |
| `input.required` | section 7 | error | invalid input | A required value is missing. |
| `input.malformed` | section 7 | error | invalid input | A value does not have the expected shape. |
| `input.unknown-key` | section 7 | error | invalid input | A configuration key does not exist in the configuration in force. |
| `input.unknown-column` | OP-08, section 7 | error | invalid input | A write names a column that is not in the entity's column list in the logical model. |
| `input.contract-value-unknown` | section 7 | error | invalid input | A value is not one of the fixed meanings of its engine contract list. |
| `input.json-invalid` | section 7, section 9 | error | invalid input | The JSON text is not valid for canonical hashing. |
| `snapshot.hash-mismatch` | section 6.3, section 7 | error | unavailable | The snapshot content does not match its stored hash. |

- **Existing codes with a run-time category.** When the snapshot in force is broken, the build reports these OP-09 codes at run time as an unavailable answer (rule 10, no fallback): `shape.schema`, `ref.unresolved`, `ref.exactly-one`, `tier.rank-duplicate`, `tier.question-options`, `stage.meaning-coverage`, `meaning.missing`, `gate-policy.coverage`, `gate-policy.emergency-rule`, `approval-rule.consultation-only`, `approval-rule.zero-count`, `value.range`, `setting.locked-changed`, `setting.required`, `json.schema`, `pattern.invalid` and `standard-model.condition-kind`. Section 7's Unavailable row could list them. `standard-model.type` also serves as OP-12's run-time reason for an emergency change type.
- **Outcome words beyond each operation's own.** Every operation may also answer `invalid-input`, `refused` or `unavailable`, with an empty payload, and the context must name the snapshot's version and hash or the answer is `snapshot.hash-mismatch`. OP-10 answers `walked` and OP-11 `computed`. The configuration operations have their own words (B25-5).

### B25-2 The engine checks as V45 to V48

The four engine checks after `snapshot.schema-unsupported` in section 5.11 take rule IDs, in table order: V45 `role.config-admin`, V46 `standard-model.type`, V47 `standard-model.condition-kind`, V48 `vocabulary.source`. `snapshot.schema-unsupported` stays a compiler check with no V-ID. Each has a passing and a failing fixture, and none fires on a shipped preset. The rule text is proposed in document 21, B21-1.

### B25-3 OP-08 as the build runs it

Each point refuses more, or writes less, than a literal reading of section 5.10. None opens a write path, and none lets an AI client approve, accept, activate or complete anything.

- **Column check from the logical model.** Every column a write names in `after` must be a column of its entity in the logical model (O01 to O24, C02, C46), in the contract form of document 20, section 3.3. An unknown name is invalid input before any rule runs (`input.unknown-column`, one reason listing every unknown name, with a hint when only the case differs). Only `after` is checked; `before` is facts. Live configuration rows (C01, C03 to C45) carry the bundle's field names (document 21, section 8.3) and are not column-checked, but a name that is a model column, Key or Origin in another case is invalid input. The core embeds the logical model as a resource (about 290 KB) and reads it once, so it still reads no file and no network (section 3.2).
- **History classes from the logical model** (document 22, section 2.3). Class A (C02, O20, O24): no update and no delete, by any writer, the engine included; C02 and O24 keep their G-07 codes, O20 uses `record.append-only`. Class F (C46, O08, O09, O10, O13, O14, O22): frozen once final, for every writer, the engine included (`record.final`; RA-05 names no code). O09 keeps G-05, C46 keeps G-16, O10 has its own rule (next point). An update with no columns on a final row is refused too, because it would restamp the row. The final state per entity is in document 22, B22-3.
- **O10 Approval Record.** (a) No caller but the approval runner writes any O10 column: request columns are `engine.effect-only`, the runner's columns `approval.outcome-writer`, and a write of only stamps or G-09 columns is refused. (b) The runner writes only its answer (the outcome, the responder columns and Approver Holds Required Role), only with an outcome, once, on a pending record; the same outcome again is a no-op. (c) An engine-nested write never names Outcome Meaning (the guard copies it from Outcome, as G-10 does), and on a final record writes only Counts Toward Requirement or the same outcome again. (d) The engine never writes Approver Holds Required Role or any other responder column (Approver Actor Meaning included). The engine creates an Approval Record only with the pending meaning, in any effect or in none, and a nested create without an outcome is invalid input. It may still change the request columns of a pending record (document 22, section 8.2). The three O10 columns are proposed in document 22, B22-1.
- **Platform-supplied columns** (Id, Created On, Created By, Modified On, Modified By; document 20, section 2.2) are dropped from every write, by any writer, and listed in `removedColumns` with `engine.column-overwritten`. Live configuration rows are left alone.
- **G-09 in two classes.** G-09 says caller values are replaced; its own tests and document 26 (RA-12, section 6.1) say they are refused. The build replaces stamps, meaning copies and values the engine computes in the same write (`engine.column-overwritten`; values the guard does not compute itself, such as the O14 flags, are dropped and listed in `removedColumns`), and refuses columns only an engine effect writes (`engine.effect-only`): Owner Assignment rows, Decided On, Superseded By, the O05 identity columns, the C46 engine columns and the autonomy columns.
- **G-08 and RA-06.** An AI create is downgraded to draft or proposed, never refused (`ai.forced-draft`); an AI update to accepted, registered, approved or completed is refused. An agent identity or agent user is refused for every write (`actor.agent-write-refused`, document 26, RA-06), so G-08's agent branch is not reachable in the default configuration. An AI actor's update to the meaning current on a Document Link whose document type has Current Needs Person is refused (`ai.status-not-allowed`) on every path, not only in the tool (working default, open design question 9; see 28-working-defaults.md).
- **Registration status** (document 26, RA-04). A request sets only draft on a Business Application, or archives a draft (as `withdraw_record` does). Registered, and any move away from registered or archived, come only from the update-subject effect (`engine.effect-only`).
- **Withdrawing a decision** (document 22, section 8.1). Only its proposer, read as Created By Principal (it never changes, and an AI client's row has the person as principal), or its decider, in person or through an AI client for them (`decision.withdraw-not-allowed`). The engine's own writes are not judged by this rule.
- **Deletes** (document 22, section 2.5). A draft row only by its creator (a person, or an AI client for that person); a delivery or document link only by its creator within `link-undo-minutes`, counted from Created On, and a Created On later than now refuses the delete; every other operational row never (`record.delete-not-allowed`).
- **Unknown meanings in the facts are invalid input** (`input.contract-value-unknown`): the decision Status Meaning on update and delete, the C46 Draft Status, and the O02 Status Meaning the registration rule reads. O10 reads an unknown Outcome Meaning as final, which refuses more.
- **WriteRequest and WritePlan fields.** `nestedEffect` names the effect a nested write belongs to, set by the adapter next to the engine-nested marker (without it G-01 cannot tell an activation write from another engine write; a caller that names it without the marker is invalid input; it rests on spike S2). The plan adds `requestHash`, `isReplay`, `isNoOp` (the same approval outcome again), `removedColumns`, and the guard rule on each refusal.
- **G-06.** The approval writer is the fixed key `approval-writer` (open design questions 1 and 12; see 28-working-defaults.md): an active role flagged Is Workload Role, held by actor kind approval runner as a service principal or managed identity, in its own name. A missing approver, actor kind or principal kind is invalid input: the runner must say who answered.
- **G-15, G-16, G-17.** The Request Hash covers entity, operation, row, after and nested effect, not the pre-image or the context, so a retry from another moment is the same request. On a C46 draft the caller writes only Change Json and Proposal Rationale; the engine's draft operations are engine-nested writes with no effect that still need the person's configuration role. The bootstrap basis passes only for a nested create of the activate-configuration effect, while no version exists, for the decision type `configuration-change` (a fixed key, open design question 1; see 28-working-defaults.md).
- **Links.** `forbidden-url-patterns` is matched ignoring case, so it refuses more, never less; a link kind's Url Pattern is matched as written. Patterns run with a one-second timeout; a timeout or a pattern that does not compile is a broken configuration (unavailable).
- **Smaller rules.** A new application starts at the first active lifecycle stage by Stage Rank, with Stage Since, set by the engine. C43 Column Protection Rule rows add protection and never remove it. Erasure is performed by a person, never an AI client (open design question 9; see 28-working-defaults.md), holding the active `platform-admin` role (a fixed key, open design question 1; see 28-working-defaults.md); the object ID is replaced only when the adapter passes a tombstone, because the core makes no random values.
- **Open points.** The update-subject effect is now planned and built (B25-8, 2026-11-24). Erasure has no decision type, tool, custom API or endpoint. Seed staging (document 21, section 4.1) needs an effect kind of its own, because G-02 refuses every preset row write.

### B25-4 Operation inputs and outputs

- **OP-01.** An answer to a question that does not apply to the kind, or to a retired option, is ignored and listed in `ignoredAnswers`. An incomplete result still shows the tier the given answers imply. No applicable question at all is a broken configuration (`tier.no-applicable-questions`).
- **OP-02.** `acceptanceBasis` is an object (decision type, condition, model, approving decision), not a string. The accountable card is first to respond, count 1, with no timeout and no typed reason. A change type with Requires Standard Model needs the OP-12 result, else `input.required`; the named model must be active and of that change type.
- **OP-03.** `countsByRecord` gives each record's counting state. First to respond honors a Minimum Count above 1, in time order. Expired means every record of the requirement expired. A C40 row without a decision type applies only to types with Requires Approval, and the proposer must be known when a proposer row applies.
- **OP-04.** Off stores no flag, as this document says; document 21, section 7.2 says off still computes the window and path facts (document 21, B21-6). `notify` comes from the row or `default-flag-notify`. The emergency exception waives only the missing-approval stop, never an AI-only stop. The follow-up due date comes whenever an emergency's authorization is missing and the row has a rule, at any level from record up; a missing rule is refused only at enforce. An emergency stays flagged inside its follow-up time, as documents 21 (section 7.5) and 25 say (document 23, B23-1). Proposed facts: `hasExternalChangeNumber` (shown, never decisive), `deployerFlaggedBySeparation`, `gatePolicyKey`, `changeTypeKey`, `windowState`. With no change linked, the change type comes from `unlinked-deployment-change-type`.
- **OP-05.** `minutesOutside` rounds up. A window that ends at or before its start is `input.malformed`.
- **OP-06.** `lastUpdatedOnUtc` is a proposed DocumentFact field (the newest link needs a date), and `staleOnByLink` gives every link's Stale On. A current link with neither date counts as stale. A stored missing is refused as `input.malformed` (28, issue 16). A missing `hasUrl` reads as true; the runtime always sends it.
- **OP-07.** Proposed facts `assignedOwnerRoleKeys` (document 21's owner-assigned kind) and `triggeredGateRequirementKeys` (C10 Triggers Json). The tier is optional: without one, only rows without a tier apply.
- **OP-10.** `startComponentId` starts from a component (document 24, tool 7). Each reached node carries its `direction`; both directions are two walks. `cycleDetected` means a real loop, so a diamond is not one. The payload adds `truncatedAtDepth` and `maxDepthApplied`.
- **OP-11.** One item per call. The C26 subject kinds are a fixed list (open design question 1; see 28-working-defaults.md). A review's cadence lead and escalate days replace the rule's. Whole UTC calendar days. An escalation replaces a reminder on the same day, and `reminderNumber` counts the rule's steps. A day the refresh did not run is not caught up.
- **OP-12.** A named model is the only candidate, so the engine never moves a change to a model nobody named (section 5.13 says both "every active model is tried" and "the one the requester named wins"; the other reading is a one-line change). A condition whose fact the change does not state fails. The C21 Tier column is tested as one more max-tier-rank condition. No status, or no Approved By Decision, means not accepted. `standard-model.no-candidate` (B25-1) covers no active model of the type, or a named model that is retired or of another type.
- **OP-13.** The Claim Key is `<finding kind>|<source type>|<External Id>` with source type and External Id lowercased, and a vanished claim keys on the component kind (`vanished|<kind key>|<External Id>`); its length is a document 22 point (B22-2). Each fetched claim adds `timesSeen` (required) and `lastSeenRunId`, so the plan writes absolute values: a replay changes nothing, and a second page of the same run does not raise Times Seen. A retired component is not registered and never vanishes; a component held by URL only never vanishes. `coveredComponentKindKeys` is optional (absent: every active kind with Is Gap Check Covered; given: a subset). An item with no kind matches the C16 Source Type Hint ignoring case. `isLiveQuery` (default false) is set only by the product's own server. Items carry only the fields a claim stores, and any other property is `input.malformed`. O23 Url is not in InventoryItem, so the plan carries no URL; adding `url` to InventoryItem closes it. Open: no rule for an open vanished claim whose component is seen again, and no guard against an empty final page that would mark every registered component of an estate vanished (the covered kinds input is the only tool today).
- **OP-14.** AuthorizationFact gains `answeredOnConfirmationCard` (default false): without it the engine cannot tell a card answer from a row any client wrote, and a person's attestation without it fails with `authorization.wrong-source`. `attesterRoleKeys` may name a C29 role or a C28 owner role, each active, or the request is refused; with none named, no attestation counts; the runtime passes OP-02's accountable result, and the engine does not resolve C31 itself. The ITSM step counts only from actor kind pipeline as a service principal or managed identity (open design question 12; see 28-working-defaults.md); an agent principal is treated as AI. Every failed test is listed. At the same Reported On a removal beats an authorization, then the larger fact ID decides; `basis` is `none` whenever the change is not authorized, while `decidingFactId` names the removing fact. A fact dated after the evaluation moment never authorizes (`authorization.reported-later`), while a later removal still removes, because the engine never guesses in favor. `authorization.number-only` is raised only when a number is recorded and no fact counts.
- **Hand-over.** OP-02 and OP-04 accept the whole OP-12 and OP-14 payloads and read past the extra fields. The MCP contracts of tools 23 and 24 use other shapes, which the server maps.

### B25-5 Rebase, activation and the bootstrap

- **Rebase** (document 21, section 4.5) is an operation with the outcomes `same-base`, `rebased`, `invalid`, `conflict` and `invalid-input`. A conflict reuses V42's `config.base-moved`. A rebased draft is always revalidated in activation mode, so V34 judges a protected change by the active rules. What an operation touches is read widely (document 21, B21-2).
- **Activation** (section 6.4, step 5) answers `activated`, `already-activated`, `discarded`, `refused` or `invalid-input`. The context's moment is the validation date (V28) and the published-on date. A draft that cannot activate (a rebase conflict, or an OP-09 error at activation) is discarded with that reason (`activation.discarded`) and no version is created; discarding keeps the card's answer, where a refusal would roll it back. A replay returns the version (`activation.already-activated`). Live row writes never delete: a row the new content lacks is written retired. The writes come in a fixed order: live rows, C02, C01, C46, Audit Entry.
- **The bootstrap of version 1** (document 21, section 4.8) answers `bootstrapped`, `invalid`, `refused` or `invalid-input`. The decider is the person behind the call, a user principal, never a nested write. `hasInstallRights` is a proposed fact, because no product role is bound before version 1. Version 1 is validated in activation mode with no active version (V34 and V42 do not apply; V28 is strict). A second bootstrap is `bootstrap.version-exists`; a caller without install rights, or a workload, is `bootstrap.not-allowed`.
- **Submit.** Submitting does not yet refuse a draft with validation errors (section 6.4, step 2 says it cannot be submitted); activation runs OP-09 again and discards it. The rule wants a refusal code of its own.

### B25-6 The runtime

- **Roles on every call** (RA-01). The adapter passes the caller's object ID, kind, entry point and group memberships, never a role. A person, or an AI client for a person, holds the human roles of the C30 bindings of their groups; an app-only caller holds only the workload roles of its current C42 grants, and is refused without one (`actor.workload-grant-missing`, B25-1). A grant is current until its Expires On day starts (UTC). A workload holding `approval-writer` is the approval runner; any other workload is a pipeline. A delegated token from an agent user resolves to the agent identity actor kind, with no role.
- **The record step.** A workload caller of `RecordDeployment` needs `deployment-recorder` (RA-07); a person, or an AI client for a person, may record without a role (document 24, tool 27). The engine sets the route from the actor kind (pipeline-step for a pipeline or the approval runner, ai-tool for an AI client, person-in-app for a person, else other; the active C36 row with that meaning, first by key), as document 24, tool 27 says. The build's record endpoint contract therefore takes no `routeKey`, which section 11.4 still lists. The record endpoint's custom API parameters are a document 24 point (B24-4).
- **Effects.** A gate decision whose gate is not ready stays proposed: the call goes on, the answer that triggered the recompute is kept, and the reason returns as a warning; any other refused nested write rolls the whole call back. Effects run depth first in plan order. Nested writes restamp the row as performed by the engine, and the Audit Entry keeps the caller. A nested write never carries an idempotency key.
- **One product operation, one call** (`Compose`): one snapshot, one actor and one transaction per operation, such as one MCP tool, with `validateOnly` as a dry run (every rule runs, nothing commits) and the caller's idempotency key over the whole operation. `RequestApprovals` and `ProposeConfigurationChange` take an idempotency key, and the first result is kept in the Audit Entry's Changed Values Json.
- **Not built yet:** the OP-13 claim writes (O03 holds no estate key, document 22, B22-5), a restart after an approval timeout, linked mode in the record step, role holders for OP-03's separation test, RA-06 for reads, RA-10, a stored O19 Stale On, and OP-12 planned for OP-02.

### B25-7 Invariant numbers and the replay record

Sections 9 and 10 keep their text.

- **INV-01 to INV-11.** Section 10 lists the invariant tests without numbers. The build numbers them INV-01 to INV-11 in that order and copies each text word for word, so a search finds it. Each has a generated test over every shipped selection (each full preset alone and with each allowed overlay combination, 22 today), and a build test fails if an invariant has none. Proposal: section 10 carries the numbers.
- **How three invariants are read.** INV-02 reads "accepted" as "stored with the meaning accepted": a caller's create that names accepted may pass as proposed (G-08), but no such write stores accepted. INV-06 retires gate policy rows in memory, keeping the snapshot's hash and version. INV-09 counts any AI approval lock value that is not a JSON true (false, retired, the string "true", 0) as off, as V33 does.
- **The replay record** (section 9) is a new schema, not a fixture. It holds the snapshot version (a preset selection with inline operations, or a bundle), the context (with no version or hash, because those come from the loaded snapshot), the request as one wire input (input and fetched facts, as an adapter would store it) and the recorded result in the form of section 5.1 (for OP-08, the write plan). Proposal: section 9 names this format.
- **Difference kinds.** The replay reports `snapshot-hash`, `configuration-version`, `engine-version`, `outcome`, `payload` (by JSON pointer), `reasons` (as a set) and `trace` (row by row). A different hash, version or engine version is reported and the answer is still compared, so an auditor sees both what was loaded and what changed. By default a reworded message is a difference; an option ignores messages, because labels may be translated later. OP-09 is not replayed: validation recomputes its report from the same configuration.
- **The engine command line** may reference the testing assembly, because it is a tool that runs fixtures and replays, not a host. A dependency test names it as the only exception besides test projects, and the in-memory store now lives in the testing assembly.

### B25-8 The update-subject effect (built 2026-11-24, D-41)

Section 5.10 names the update-subject effect and gives one example: an accepted tier decision writes Tier and Tier Decision. B25-3 left the rest open. Under D-41 the build decided the rest, wrote it here, and built it. Nothing below lets an AI client, a pipeline or the approval runner change a subject by itself. Every write is an engine-nested write of the update-subject effect, planned by OP-08 when it accepts a decision (G-17) and guarded again by OP-08 like any other write (G-13, the effect-only columns, RA-04).

- **When.** In the same transaction as the acceptance, after the decision's own effects (its Audit Entry, an activation, a supersede), on every path that accepts: the approval runner's answer, a person's answer on the decision page (`answer_approval`, D-38), and the engine's acceptance for the locked list. Each write restamps the row as performed by the engine, with the caller as On Behalf Of (B25-6), and names the decision as its basis in the subject's own decision column: Tier Decision, Stage Decision, Autonomy Decision, Ownership Decision, Triage Decision or Authorization Decision. One Audit Entry, operation `subject-updated`, names the decision and lists every row written.
- **A rejected or withdrawn decision changes nothing but itself.** The same holds for an accepted decision with no outcome, and for the decision types and outcomes that change nothing on the subject (data use, risk acceptance, design, exception, acceptance, priority, portfolio).
- **Any outcome's C19 Next Lifecycle Stage** moves the application there (document 21, C19: "the stage an accepted decision with this outcome moves the application to"). Balanced: go-live `go` to operate, retirement `complete` to retire (`retire` until document 21, B21-9). A stage whose meaning is retired also cancels the application's reviews that are still due (document 23, P09 step 7). Archiving the registration on retirement "when configured" waits for a setting; none exists.
- **Intake** (document 23, P01 step 7). `accept` registers the application. A linked draft becomes registered, or a new application is created, registered, from the request (Title as Name, Need as Purpose, Proposed Kind as Application Kind, Uses AI), and the request names it. Its stage is the outcome's Next Lifecycle Stage, or else the first active stage after the Accept gate's After Stage (Balanced: prioritize and fund), because accepting the intake passes that gate. A linked application only moves forward. The request is then completed. `redirect` completes the request (the reuse answers it). `decline` rejects it. `park` keeps it open. Each names the decision as the request's Triage Decision. A request that is no longer open (withdrawn, cancelled, completed or rejected) does not change.
- **Change authorization** (document 23, P03 step 4). `authorize` moves the change request to the meaning in progress, which Balanced labels Scheduled. `reject` moves it to rejected. **Choice:** no authorized status is added. The record status meanings are a fixed contract list (document 22, O07 and O11), and in progress already reads as authorized and scheduled. The authorization itself stays readable as the decision's state: `get_change_request` returns `authorization.status`, and `get_change_schedule` returns `authorized` and, new, `authorizationStatus`. A change whose authorization the approvers rejected therefore reads rejected in both, while its own status stays submitted, by the rule above.
- **Tier** (document 23, P02 step 6). The tier the decision names in Proposed Changes Json (`tierKey`), or, for `confirm`, the tier the subject's latest assessment computed. The assessment takes the decision as its Tier Decision and is then final (document 22, O08). A raise or lower with no tier named changes nothing and says so.
- **Autonomy level** (document 23, P02 step 8). `agree` stores the level named in Proposed Changes Json (`autonomyLevelKey`, a value of the `autonomy-level` list) as Agreed Autonomy Level.
- **Ownership** (document 23, P08 steps 4 and 5). Each change in Proposed Changes Json (`owners`, the items of `propose_owner_change`) ends the assignment it names and starts the new holder, on the change's Start On when that is later than today, else today. A deputy covers the new main holder of its role in the same decision, or the current one. The deputies of an ended holder move to the successor. A role with Maximum Holders keeps that many main holders: when a new holder starts and nobody is ended, the earliest current holders end on the same day. A person who already holds the role is not added twice.
- **Proposed Changes Json** (document 22, O09, a proposed column, B22-8) carries what the decision changes, so the approver reads exactly what acceptance applies. OP-08 checks it when a caller writes it: only the member of its decision type (`owners`, `tierKey` or `autonomyLevelKey`), with keys active in the configuration in force. Anything else is invalid input. The engine reads it again at acceptance.
- **Facts.** OP-08 takes a new fact, `facts.subject`: the subject rows as the store holds them (the application, the intake request, the change request, the latest tier assessment, the current owner assignments, the due reviews) and `newRowIds`, the IDs the adapter gives the rows the effect creates, because the core makes no random values (as with the erasure tombstone). Accepting a decision whose acceptance changes its subject without these facts is invalid input. The runtime fetches them only when the gate lets the decision through.
- **What can no longer apply** is skipped and named with the proposed warning `subject.change-skipped` (B25-1): an owner assignment that already ended, a retired owner role or tier, a request no longer open. The decision is still accepted: what people decided stays decided, and the warning tells the caller what the engine did not write.
- **Fixed keys** (working default, open design question 1): the decision types `intake`, `change-authorization`, `tier`, `ownership` and `autonomy-level`, and the outcomes `accept`, `park`, `redirect`, `decline`, `authorize`, `reject`, `confirm` and `agree`. A type or outcome a client adds changes only the stage, through its Next Lifecycle Stage.
- **The decisions the engine opens itself** (the intake decision and the change authorization at `submit_record`) now name the outcome their statement already says: `accept` and `authorize`.
- **Not built.** Next Review On from the cadence after a tier decision; the archived registration on retirement; the ownership card of an application with no owner yet (who answers it is not designed); a business owner named on an intake (built since, B25-10); and the confirmation card that registers an application an AI client saved as a draft (P10). G-14 also holds a declined or parked intake while the Accept gate is not ready, because it reads the decision type, not the outcome. That is a separate design point.
- **Tests.** 29 OP-08 fixtures (tag `update-subject`): each effect, the skips, the missing facts, the proposal shape, and the nested writes themselves next to the same write from a person. Runtime tests in `SubjectEffectTests.cs` cover the card path, the engine's acceptance and a rejection. MCP tests in `SubjectEffectToolTests.cs` cover an owner change, an intake and a change answered on the decision page, and a raised tier answered through the runner.

### B25-9 Record numbers (built 2026-11-24, D-41)

Document 22 gives O02, O07, O09, O11 and C46 a Record Number "assigned by the engine", and nothing assigned one: every row the engine created had none, and pages named records by ID. Under D-41 the build decided the rest:

- **Which rows.** Every new row of a numbered entity, the engine's own nested creates included (the application an accepted intake registers): business application `APP`, intake request `IR`, decision `DEC`, change request `CHG`, review `REV` and configuration draft `CFG` (`Product.Engine.Core`, `RecordNumbers`). **Choice:** the intake prefix is `IR`, not the model's earlier `INT`, because the demo estate already numbers its requests IR-0101 to IR-0107 and people read them that way; the model's prefix pattern now takes two to four letters. Reviews gain a Record Number (document 22, B22-9).
- **The format.** The prefix, a hyphen and at least four digits (IR-0108), as the estate writes them. A number never changes and is never reused.
- **Who gives it.** The store, as it gives row IDs (section 3.2: the core makes no random or sequential values). The runtime asks the transaction for `NextRecordNumber(entity, prefix)` after OP-08 allowed a create, and writes the number into the row. OP-08 drops a number a caller sends, on a create or an update, with the info reason `engine.column-overwritten` (G-09): `recordNumber` is an engine-computed column of the five operational entities, and already an engine-only column of C46.
- **Unique under concurrency.** The store reserves the number at once, outside the call's transaction, the way a database sequence does: on Azure SQL one transaction locks the entity's row of `host.RecordNumber` (`sql/0007-record-numbers.sql`), reads the highest number with the prefix that the entity's table holds, and moves the counter one above the larger of the two before it lets the lock go; SQLite does the same under its write lock; the in-memory store under its gate. So two calls at the same moment never share a number, the numbers go on after a seeded estate or rows an earlier build wrote, and the store key on Record Number still refuses a second row with a number taken (the engine's one retry then sees the first result). **Choice:** a counter that reserves at once, not the next number read inside the call: a number read inside the call is only checked at commit, and two concurrent calls would then need more than the engine's one retry.
- **Gaps.** A call that does not commit (refused, or failed) leaves a gap, as a sequence does. A dry run (validateOnly) takes no number, so checking a form never uses one up; its answer shows no number.
- **The Audit Entry** of the create lists the caller's columns as OP-08 planned them; the number, like the row ID, is the store's.
- **Migration.** `0007-record-numbers.sql` adds the counter table; it builds nothing from existing rows, because the first number an entity takes counts the rows its table holds. The app reads, inserts and updates the counters and never deletes them.

### B25-10 The business owner an intake names (built 2026-11-24, D-41)

Balanced's Accept gate asks for a business owner (gate requirement `accept-business-owner`, owner-assigned). A request for a new application has no application, so no owner assignment could meet it, and no new-application intake could pass triage. Under D-41:

- **The column.** O07 Intake Request gains Business Owner (a Principal Reference; document 22, B22-9), which `save_intake_draft` writes from a person the store knows (document 24, B24-9).
- **The gate.** OP-07's facts count the owner role `business-owner` as assigned when the subject's intake request is open (draft, submitted or in progress) and names a business owner, beside the application's own current assignments. The runtime adds it (`AssignedOwnerRoles`); OP-07 itself is unchanged. **Fixed key** (working default, open design question 1): the owner role `business-owner` (`EngineKeys.BusinessOwnerRole`).
- **The effect.** When the intake is accepted, the update-subject effect (B25-8) starts an Owner Assignment for the named person, owner role `business-owner`, main holder, from today, with the intake decision as its Ownership Decision (the accepted decision that started it), after it registers or creates the application. The runtime gives the assignment an ID in `newRowIds`, after the new application's. A linked application that already has a current main business owner keeps that owner: the named person is skipped with `subject.change-skipped`, because changing an owner is an ownership decision. A named person who already holds the role adds nothing. A retired `business-owner` role skips it too.
- **Fixtures.** `subject-intake-accept-assigns-named-business-owner-allowed` and `subject-intake-business-owner-already-held-skipped` (tag `intake-business-owner`). Runtime tests in `IntakeAndRecordNumberTests.cs`; MCP tests in `IntakeAndDecisionToolTests.cs` (a new-application intake answered on the decision page registers the application and assigns the owner; the same request with no named owner stays proposed, held by the gate).
- **Still open.** G-14 reads the gate by decision type, not by outcome, so a declined or parked intake is still held while the Accept gate is not ready (B25-8). With a named owner this now rarely bites; the outcome rule stays a separate design point.

### B25-11 OP-08 checks the exception fields (built 2026-11-24, D-41)

O09 Exception Standard and Exception Review On (document 22, B22-9) belong to an exception decision only. OP-08 refuses either on another decision type, and a review date that is not a date, as invalid input (`input.malformed`), as it checks Proposed Changes Json (B25-8). Fixtures `exception-fields-on-exception-decision-allowed`, `exception-fields-on-tier-decision-invalid-input` and `exception-review-date-malformed-invalid-input` (tag `exception-fields`).

### B25-12 OP-08 guards the end of a review; the AI-only code approval flag is stored (built 2026-11-24, D-41)

Document 23, B23-4 left open whether an AI client may mark a review done, and whether done needs Done On, Reviewer and an accepted Outcome Decision. The author's AI sessions decided under D-41, as follows.

- **A review is done only with what the model requires.** Document 22 marks Done On, Reviewer and Outcome Decision as required when done. A write that sets the meaning done and leaves any of them empty is refused with the proposed code `review.done-incomplete`.
- **Only once its outcome is accepted.** The decision the review names must be accepted, of the decision type its review type records (C24 Produces Decision Type), about the review's own application; otherwise the proposed code `review.outcome-not-accepted`. A proposed outcome is not an outcome yet, and a review cannot end with another type's decision or another application's.
- **A new fact.** OP-08 reads the decision from `facts.reviewOutcome` (`decisionId`, `statusMeaning`, `decisionTypeKey`, `subjectApplicationId`), which the runtime fetches when a review write names a status (WriteRequest gains `ReviewOutcomeFact`, a proposal; the fixture schema's `guardFacts` gains the member). Without it a write to done is invalid input (`input.required`): the engine never guesses.
- **G-08 widened.** Done joins accepted, registered, approved and completed as a positive end no AI actor sets: the reviewer is a person, who attests the review. A create is still downgraded, never refused.
- **Done stays done.** A done or cancelled review never changes its status again, nor a done review its Done On, Reviewer, Outcome Decision or Next Due On (`record.final`). The next review is a new row.
- **The AI-only code approval flag** (O14 Is AI Only Code Approval, document 22, B22-10) is engine-computed like the other three flags: OP-04 computes it, the runtime stores it with the record, and a caller's value is dropped with `engine.column-overwritten` (G-09). Before, OP-04 computed it and the runtime dropped it, so only the call that recorded the deployment could see it.
- **Fixtures** (op-08, 229 now): `review-done-with-accepted-outcome-allowed`, `review-done-outcome-still-proposed-refused`, `review-done-outcome-of-other-type-refused`, `review-done-without-reviewer-refused`, `review-done-without-outcome-fact-invalid-input`, `g08-ai-marks-review-done-refused`, `review-done-stays-done-refused` (tag `review-done`) and `g09-ai-only-code-approval-flag-dropped` (tag `ai-only-code-approval`). Runtime test `ReviewDoneTests` proves the runtime fetches the fact.
- **Not in the engine:** that the reviewer is the person who writes, and who may complete a review. The tool checks both (document 24, B24-10); a platform admin may record a review someone else did, through a later tool. Who closes a change, and that a change closes only once authorized, is also the tool's check: the engine refuses only an AI actor's completed (G-08).

### B25-13 The call history, refused calls and the O04 rules (built 2026-11-24, D-41)

The author's AI sessions decided these under D-41.

- **Every composed call that writes leaves one Audit Entry.** Before, only a call with an idempotency key did (to replay it). Document 26, section 9.4 says O24 is the full change log on Azure; one entry per call, naming the tool (Entity Name), the record (Row Id) and the caller's stamps, is the smallest step that makes the audit trail and the AI activity see an AI client's own calls. A keyed call keeps its replay payload in Changed Values Json; an unkeyed call stores the rows it wrote (entity, operation, row ID, status meaning) and no values or labels. A dry run (validateOnly) writes none. The entry is the runtime's own nested write, as `request-recorded` is.
- **A refused call is recorded in a transaction of its own** (`EngineRuntime.RecordRefusedCall`; document 26, sections 9.2 and 9.4). The refused call rolled back, so the entry is the only row written. The MCP server core decides which refusals to record (document 24, B24-11) and passes the engine's reason code and its rule IDs from the reason catalog. A caller the configuration does not resolve, or a store that cannot take the entry, loses the record and still gets the refusal.
- **OP-08 checks O04** (document 22, O04 rules): a dependency points to exactly one of To Application and To Component, and never to its own application (proposed code `dependency.target-invalid`); the shape is judged on a create and on any write that names an end. An ended dependency never changes again (`record.final`): class E keeps the history of who depended on what. The existing class E fixture (setting Ended On) still passes.
- **Fixtures** (op-08, 233 now): `dependency-create-allowed`, `dependency-to-itself-refused`, `dependency-two-targets-refused`, `dependency-ended-never-changes-refused` (tag `dependency-shape`). Runtime tests: `CallAuditTests`.
- **Not in the engine:** the O04 business key among open rows and O01's no-cycle rule need facts the runtime does not fetch yet; `save_dependency` and `save_portfolio` check them.

### B25-14 Who an approval asks: proposed owners, fallbacks and the tier at stake (built 2026-11-24, D-41)

Balanced's alignment with the process documents (document 21, B21-9) needs three things configuration could not say: "the successor accepts" (P08), "the sponsor when the business owner is leaving" (P08) and "tier 3 needs the platform admin and the security reviewer" for a tier decision that raises an application to tier 3 (P02). The author's AI sessions decided them under D-41 and built them.

- **OP-02.** A RequiredApproval carries two more members, written only when set: `approverProposedOwners` (C32 Approver Proposed Owners) and `fallbackOwnerRoleKey` (C32 and C31 Fallback Owner Role). Rule 9 grows: a winning group of a type that requires approval whose counting rules all ask the proposed owners is broken (`approval-rule.consultation-only`, as V27), because a decision that names nobody new skips such a rule. OP-02 stays a function of configuration and scope.
- **The runtime resolves who is asked** when it opens a record (`DecisionScope.Ask`), from the decision's own facts:
  - a role: as before;
  - the proposed owners: one record per person the decision's Proposed Changes Json adds (`owners[].addObjectId`), with Required Owner Role the role they would take and Asked Principal the person. The requirement's Minimum Count becomes the number of people named, so everyone named must say yes (one person counts once, OP-03); with nobody named the requirement drops out;
  - an owner role: its current holders on the application (a deputy too). With a fallback configured, the role is passed over when the decision hands it over (it ends a main holder's assignment or starts a new main holder): the fallback owner role's holders are asked, then the Accountable Principal of the nearest portfolio on the path, by name, and when nobody is found that way the owner role's own holders after all. With no holder at all the record names the owner role and waits;
  - for a request with no application, the business owner the intake names holds the business owner role (as the Accept gate counts it, B25-10), so a card for that role asks that person by name. Balanced uses this for the priority card, whose sponsor falls back to the business owner.
  **Choice:** the route is fixed when the record opens and written on the record (Required Role, Required Owner Role, Asked Principal; document 22, O10), so the decision page, the runner and the answer check read one answer. A restart resolves again.
- **The answer check** (D-38) reads O10 Asked Principal: a record that asks one person by name is that person's only, whatever roles they hold (`approval.answer-not-asked` for anyone else), and its result says `askedByName`. The approval runner sends such a card to that person only, and its reported Approver Holds Required Role stands only for that person.
- **Consent is not a check.** A proposed owner's own yes is consent to take the role. No separation rule applies to it, in OP-03 and in the answer check, so a successor who proposed their own appointment at tier 3 still counts. The business owner's card, or its fallback, carries the check, and `no-self-approval-tier-3` applies there as always (D-40).
- **The tier at stake.** A tier decision about an application takes, as its scope tier, the highest ranked of the application's tier, the tier the decision names (Proposed Changes Json `tierKey`) and the tier its latest answers computed. OP-02, OP-03, the separation rules and the answer check all read it. So raising a tier 2 application to tier 3, confirming a computed tier 3, and lowering a tier 3 application all take the tier 3 approvals (P02 step 4: lowering needs the full set).
- **Fixtures.** OP-02: `tier-decision-tier-3-two-approvers` (replaces `tier-decision-accountable-card`), `tier-decision-tier-2-platform-admin`, `ownership-proposed-owners-and-business-owner`, `priority-sponsor-with-business-owner-fallback`, `data-use-tier-3-data-owner-and-security`, `flagged-agent-ai-lead`, and the broken `proposed-owners-only-group-unavailable`. OP-03: `proposed-owner-consent-counts-at-tier-3` and `proposer-approves-business-owner-card-refused-at-tier-3`. Approval answer: `asked-by-name-the-named-person-allowed`, `asked-by-name-another-person-refused` and `proposed-owner-who-proposed-it-allowed-at-tier-3`. OP-09: `v10-proposed-owners-and-owner-role`, `v10-fallback-without-owner-role`, `v10-fallback-same-as-owner-role`, `v27-proposed-owners-only` and `v27-proposed-owners-with-owner-role-valid`. OP-07 and OP-12 fixtures follow the new Balanced rows.
- **Tests.** `ApproverRoutingTests` (runtime): the sponsor answers a hand-over, the portfolio's accountable person is asked by name, only the named person may answer, a proposed owner's consent at tier 3, the priority card of a request with no application, and the tier at stake. MCP: an owner change needs the successor's own answer on the decision page.
- **Activation names the approval of a model.** A standard change model counts only once a person approved it (C21 Approved By Decision, a working set field). When an accepted configuration change activates, the runtime names its decision as the Approved By Decision of every model the draft upserts without naming one, so approving Balanced's shipped model is an ordinary configuration change (P11). A changed model is approved again by the decision that changed it. The draft keeps its Change Json as written. Test: `ConfigurationFlowTests.ActivationNamesTheAcceptedDecisionAsTheApprovalOfAModelItLists`.
- **OP-12 at submit.** For a change authorization whose change type requires a standard change model, the runtime now runs OP-12 over the store's facts (the application's tier rank, the planned target's stage, the main application plus its affected applications, the component kinds it holds, the window) and each active model of the type with the status of its approving decision, and gives the result to OP-02 as `standardChange`. A change that fits an approved model is accepted by the engine with no card, citing the model and its approving decision (rule 8), and the update-subject effect schedules it; one that fits none is refused as not standard (`standard-model.does-not-qualify`). Before, OP-02 answered invalid input there. Test: `ApproverRoutingTests.AStandardChangeIsAcceptedOnlyThroughAnApprovedModelItFits`.
- **Not built:** stated conditions on a change request (OP-12 `stated` conditions have no column to read, so a model that names one never fits); a data scope for approval rules (data-taxonomy.md).

### B25-15 Scope, effects and review outcomes after the security review (built 2026-11-24, D-41)

The choices the author's AI sessions made under D-41 after the security review of wave 2, each with its reason.

- **OP-02 input `subjectApplicationRegistered`.** True for a decision about a request linked to an application already in the register (not a draft); the runtime sets it. The lowest-tier engine acceptance (rule 8) then does not apply, and the request goes to the accountable holder. Reason: the locked list covers a request that starts a new application; a request about an existing one changes it, so a person decides. Fixture `op-02/intake-linked-registered-app-not-engine-accepted`.
- **The scope tier of a request linked to an application** is the higher of the application's tier and the request's computed tier; a tier decision about a request with an application takes the tier at stake as about the application (B25-14). Reason: the effect of such a decision lands on the application, so its approvals and separation rules are never those of a lower tier than the application has.
- **The named business owner is assigned only on an application the effect creates, or a draft it registers.** On an application already in the register the effect skips it with `subject.change-skipped`: owners there change through an ownership decision. Fixture `subject-intake-linked-registered-app-owner-skipped`.
- **The facts' application is the decision's.** The update-subject effect refuses `facts.subject.application` unless it is the decision's Subject Application, or the application its subject request or change names (`input.malformed`). Reason: an adapter that built facts wrongly could otherwise change another application. Fixture `subject-application-not-the-decisions-invalid-input`.
- **A stage moves forward only.** A Next Lifecycle Stage that ranks below the application's stage is skipped with `subject.change-skipped`. Reason: Balanced now moves the stage on intake, priority and design acceptance (document 21, B21-10), and a late gate decision must never move an application back. Fixture `subject-stage-never-moves-back-skipped`.
- **The request's portfolio** becomes the new application's, and a registered draft's when it has none. Fixture `subject-intake-accept-new-app-takes-portfolio-allowed`.
- **Review outcomes are used once, in their own cycle.** `facts.reviewOutcome` gains `usedByReviewId` (another done review that names the decision), `decidedOn` and `cycleStartedOn` (the latest Done On of an earlier done review of the same type and application); the runtime fetches them. OP-08 refuses a reused decision, and one decided before the cycle started, with `review.outcome-not-accepted`. Fixtures `review-done-outcome-used-by-another-review-refused`, `review-done-outcome-from-earlier-cycle-refused` and `review-done-outcome-of-this-cycle-allowed`.
- **Configuration change decisions** take the outcome `accept` and a statement from the draft's change summary when the draft is submitted (document 24, B24-14).

### B25-16 The data taxonomy's engine: OP-15 to OP-17, G-18 to G-25, V49 to V60 (built 2026-11-24, D-37 and D-41)

Data taxonomy slice 2 adds three read operations and the data rules to the engine. The design is
summarized in [data taxonomy](../../architecture/data-taxonomy.md#engine).

- **OP-15 Resolve data domain, OP-16 Evaluate data use, OP-17 Run data checks.** Pure reads over the snapshot's domain
  tree and the owner, location, mapping, use and decision facts the adapter passes. Their outcome words are proposals:
  `resolved`, the data use state ([the decision state](../../architecture/data-taxonomy.md#the-decision-state)), and
  `checked`.
- **OP-02** matches five more scope fields (subject kind, classification, AI use, data domain level, data taxonomy
  change) and passes each requirement's Owner Subject. It names where an owner role is resolved; OP-16 and the runtime
  resolve the person through OP-15. A request with no subject kind is about an application.
- **OP-07** judges the requirement kinds `data-uses-declared` and `data-uses-decided` from `facts.dataUses`, by
  `data-taxonomy-mode`.
- **OP-08** adds G-18 to G-24 and **G-25**: a data use or a copy whose governing rule is never is refused when it is
  saved, for a person and an AI client alike, with `data.rule-never` (D-41, question 7, as the product owner changed it). The
  update-subject effect of an ownership decision about a data domain or location writes the owner assignment with that
  subject.
- **OP-09** adds V49 to V60.

### B25-17 The data reads inside a tool's call (built 2026-11-24, D-37 and D-41)

Data taxonomy slice 3 ([data taxonomy](../../architecture/data-taxonomy.md#mcp-tools-and-rest)). The data tools run OP-15, OP-16 and OP-17 inside
their own composed call, so a read sees the call's own writes, and a save or its validateOnly preview answers the state
of the row it just wrote.

- **`ComposedCall.DataDomains(keys)`** runs OP-15 for each domain, with the owner rows of every chain fetched once.
- **`ComposedCall.DataUse(id)`** runs OP-16 for one stored use or mapping, the call's writes included.
- **`ComposedCall.DataUseQuestion(question)`** runs OP-16 for a use that is not saved yet (`check_data_use`): the
  application, or an intake request's linked application, proposed kind, latest computed tier and sensitivity answer.
- **`ComposedCall.DataChecks(scope)`** runs OP-17 over the store and the call's writes. `RunDataChecks` shares its input.
- **`ComposedCall.Report(reasons)`** adds a read's warnings to the call's answer.

Nothing in the operations changed: these are the facts the runtime already gathered for slice 2, offered inside a call.

### B25-18 The due-state refresh, the named owner's consent and the drafted by pair (built 2026-11-24, D-36 and D-41)

**The engine's own caller.** The daily refresh runs with no person behind it, so the runtime gains a caller entry
`Engine` (`CallerIdentity.Engine()`), with the fixed principal ID `EngineKeys.EnginePrincipalId`
(`00000000-0000-0000-0000-00000000e000`, version digit 0, so never an Entra object ID) and the Client App label
"Due-state refresh". Only the host builds it, in process; no token and no request maps to it. It resolves to the actor
kind engine with no role. Every write it makes is engine-nested, so OP-08 judges each one under its own rules.

**`EngineRuntime.RefreshDueStates`** (document 23, S2 and S1 step 9). One read call plans what is due
today; then each item runs in a call of its own, so one item that fails is reported (`due-state.item-failed`, a warning)
and the others commit; last, one Audit Entry for the run (operation `due-state-refresh-run`) when it changed something
or a person ran it. Only the engine's own caller, or a person holding the platform admin role, runs it
(`due-state.refresh-not-allowed`). A dry run runs every item and commits nothing. At most 500 items a run by default
(`more` says the rest wait; a rerun picks them up).

- **Approvals.** A decision with a pending Approval Record past its Expires On, or one no longer proposed that still holds
  one, gets the approval refresh (as `RefreshApprovals`: expired, cancelled, the restarts), and each record it expires
  sends its escalation (notice `expired`, S1 step 9). One Audit Entry (operation `due-state-refreshed`) lists the rows.
- **Reviews.** A done review whose Next Due On has come opens the next review of its application and type: status due,
  trigger cadence, due on that date, its REV- number from the store, unless a due review of them exists or one is due on
  that date already, and never for an archived or retired application. A due review gets the C26 steps of the day (the
  cadence's lead and escalate days replace the rule's, as OP-11 does), and on its first overdue day a notice `overdue` to
  its lead owner. Overdue stays computed: the status meanings are still due, done and cancelled.
- **Documents.** A current Document Link whose Stale On (Last Checked On, else Last Updated On, plus the C22 Freshness Days,
  else the stored Stale On) has passed becomes stale; a stale one gets the document steps counted from its Stale On.
- **Claims.** An open claim gets the claim steps counted from the day it was found, to its suggested claimant, escalated to
  the platform admin role.
- **Flags** (S2 step 4). A deployment flagged at gate level flag, advise or enforce, finished within the last seven days,
  sends one notice to the default-flag-notify setting's owner roles (their holders on the application) and roles.
- **Notices.** Each is one Audit Entry (operation `reminder-sent`, or `flag-notice-sent`) whose Request Key is
  `due-state|<kind>|<entity>|<row ID>|<rule>|<step>`, so a rerun never sends twice, and whose Changed Values hold the
  notice, the subject, the rule and step, the channel, the due date and who is told: people by object ID, product roles by
  key, the owner role and whether it went to deputies. IDs and keys only, never a label (document 22, section 2.7).

**Choices (D-41), each with its reason.**

1. **Items commit one by one,** not in one transaction: a broken stamped version under one decision must not stop the
   reviews and reminders of the whole estate. Each item checks its own precondition again, so a rerun is safe.
2. **The next review opens on its Next Due On,** as `complete_review` promised, not lead days earlier: a review opened early
   crowds My work. The cadence's lead days still give reminders, from the day it opens.
3. **An approval request's clock starts at Requested On,** and a step on that day is not sent: the card is the first message.
   With Balanced (repeat every 2 days, escalate after 5) a request is reminded on days 2 and 4, before it expires, and
   escalated on day 5 when it waits ten days. A consultation gets no reminder: its answer never counts and the decision
   does not wait for it.
4. **Escalation goes to the deputy of the role asked;** with no deputy, to the accountable letter of the decision type (C31:
   its role, or its owner role's holders, else its fallback owner role's). A document escalates to the technical owner's
   deputy, else the business owner, because it records no decision type. A claim escalates to the platform admin role.
5. **Expiry escalates at once** (S1 step 9: "asks S2 to remind or escalate to the deputy"), because with Balanced's
   five-day timeouts the C26 escalation day falls on the expiry day.
6. **A document with no date is left alone,** although OP-06 reads a current link with neither date as stale: the refresh
   writes only a date it can name.
7. **Flags read the default-flag-notify setting.** A deployment record does not keep which gate policy row applied, so a
   row's own Flag Notify list is not read yet. A first run on an old estate tells only the last week's flags.
8. **The engine records, flows send** (D-10). The notices name who is told and through which channel; the notification flow
   that sends them is not built yet.
9. **Azure SQL, not verified live.** The timer's session context carries the actor kind engine. The row security that
   guards a final Approval Record (`fn_ApprovalPending`) lets only the approval runner or an answering person change one,
   so a Counts Toward Requirement change on a final record during a refresh would be refused by the database. A refresh
   with no new answer changes no count on a final record; if one ever does, that item fails and the runner's next claim
   refresh does it. No migration was added: the schema version stays 8.

**The named business owner's consent.** The business owner an intake names was counted by the Accept gate and assigned by
the accepted intake without their own answer. Now:

- **OP-02.** A rule that asks only the proposed owners (C32 Approver Proposed Owners) is their consent, never the group's
  counting rule. For a type that does not require approval (rule 9 still refuses such a group for one that does), a
  winning group whose counting rules all ask the proposed owners goes on as if no counting group matched: to the engine's
  acceptance (rule 8) or the accountable card (rule 7), with the consents kept as requirements beside it. For
  engine-accept the requirements hold only consents, often none. V20 lets such a rule stand on a type without Requires
  Approval.
- **Balanced** gains `intake-business-owner-consent` (decision type intake, Approver Proposed Owners, everyone must approve,
  ten days, guideline G2). Starter, Regulated and the CoE baseline inherit it. 648 rows.
- **The runtime.** For an intake decision, the proposed owners include the business owner the request names when the
  accepted intake would assign them: the request is open and starts a new application, or registers a draft with no main
  business owner. One record asks them by name (Required Owner Role business-owner). A request that names nobody drops
  the consent.
- **The engine's acceptance waits.** A lowest-tier intake that needs a consent opens only that record (no consultation,
  as the fast path opens none) and reports `waitingForConsent` with `decision.consent-waiting`; when the consent counts,
  the recompute accepts the decision on the engine-accept basis. A refused consent rejects the decision, as for a proposed
  owner of an ownership decision.
- **The Accept gate** counts the named business owner only once their consent record is approved and counts, and the
  update-subject effect assigns them only with the new fact `facts.subject.intakeOwnerConsented` (absent reads as false;
  without it the effect skips them with `subject.change-skipped`).
- **Choice.** A client configuration that removes the consent rule leaves the named person uncounted, so its Accept gate
  waits for an ownership decision: nobody becomes an owner without their own answer.

**The drafted by pair** (document 22, B22-15). OP-08's stamps add Drafted By Client App and Drafted For Principal on the
create of an operational row by an AI client acting for a person; both are stamp columns, so a caller's value is dropped
and no update changes them.

**Fixtures and tests.** OP-02: the five intake fixtures carry the consent (the lowest-tier fixture keeps engine-accept with
the consent as its one requirement); OP-08: `subject-intake-accept-assigns-named-business-owner-allowed` gives the consent
fact and the new `subject-intake-business-owner-without-consent-skipped` skips the owner; OP-09:
`v20-consent-without-approval-allowed`; the schema example; the replay records take the new Balanced hash and the drafted
by stamps. Runtime tests: `DueStateRefreshTests` (4), the consent paths in `IntakeAndRecordNumberTests` and
`FullWriteTests`. Reason codes `due-state.refresh-not-allowed`, `due-state.item-failed` and `decision.consent-waiting`
(proposed).

### B25-19 A retired deployment target takes no new record (built 2026-11-24, D-41)

The record step (`RecordDeploymentIn`, used by the record endpoint and the MCP tool `record_deployment`) refuses a
deployment to a target whose Retired On is set (document 22, B22-16) when the deployment finished on or after that day.
The refusal is `deployment.target-retired` (proposed, a rule refusal, so `rule_violation` on every entry), its message
names the target, the day and the reason a person gave, and nothing is written.

Choices: the check sits in the runtime's record step beside the check that the target is registered, not in an OP-08
guard, because it reads one fact the step already loads (the target row) and has no configuration to judge. A replay of
an idempotency key recorded before the retirement returns the first record, as before. A deployment that finished
before the retirement day is still taken, so a pipeline's late report of history is never lost. Test:
`FullWriteTests.ARetiredTargetRefusesANewDeploymentAndKeepsItsHistory` (the refusing and the passing cases).

### B25-20 The engine after the wave 3 reviews (built 2026-11-25, D-41)

The security, product, data design and end-to-end reviews of wave 3 found these engine problems. Each fix is the AI
sessions' choice under D-41.

- **A successor never leaves a role empty.** When one ownership decision starts a new main holder of a role later than
  today, each assignment of that role it ends with no date of its own ends on the new holder's start date
  (`SubjectEffect.OwnershipChanges`). An end that names its own date keeps it. The demo director now puts the start
  date on every end item too. Fixtures `subject-ownership-successor-ends-on-start-allowed` and
  `subject-ownership-end-with-own-date-kept-allowed`. Reason: the accepted successor change on Claims Triage Agent ended
  Wren Abernathy's role three weeks before Nadia Ferreira's started.
- **The proposer is set once.** `DecisionScope.ProposerPrincipalId`, and every read that names a proposer, take the admin
  who instructed a configuration draft, else the person an AI client drafted for (Drafted For Principal), else the
  creator. Never On Behalf Of, which every later write restamps, so neither the decision page nor a separation rule
  names the person whose answer accepted the decision.
- **A data owner subject never asks nobody.** At the first request, a counting requirement whose owner sits on a data
  domain, its parent, a location or the touched domains and resolves to no person refuses the call with
  `data.no-one-to-ask` (DC-06). The approval answer takes a record of such a rule only from the person it names.
- **One person counts once on a data-use decision**, across all of its counting requirements (approval answer, step 5).
- **G-25 judges every create; a future End On is still open** (`DataTree.IsOpenOn`), in the guard, OP-16, OP-17 and the
  reads. **G-21 on update:** an AI actor cannot change or end a data row a person confirmed; Last Seen On is the only
  refresh it may write. **G-18** refuses a second open use with the same application, domain, location and AI flag
  (`data.use-duplicate`, the second key of O27; fact `duplicateUse`).
- **A domain move loosens.** The data taxonomy diff counts a new parent as loosens for the domain and its subtree, and
  the runtime passes the domains in use, so a retired domain in use loosens too.
- **The gate words its requirements with labels**, never keys ("The requirement "Data uses are decided" needs ..."), and
  a declared use (or Holds No Business Data) answers "a data owner is named" when data domains are on; under
  domains-required the app-level data owner does not count (a choice under D-41).
- **The configuration change decision is worded with labels** (`ChangeWords`): "the AI use rule of Workplace / Visitor log
  changes from decision to never". The engine's change summary stays the stored record of the draft.
- **The due-state refresh plans again once it opens a review**, for its reminders and its overdue notice, in the same
  run, so a second run the same day changes and sends nothing. The audit entry that opens the next review carries the
  request key `due-state|open-review|<done review>|<due on>`, so the unique index refuses a second run that opens the same
  review at the same time. The run's own audit entry lists the reviews it opened and the documents it turned stale,
  which the Admin page shows as the recent runs.
- **A review's cycle starts no earlier than the review was opened**, so a decision accepted before a first review was
  requested cannot close it.
- **The data-use effect of a configuration change** (`DataUseImpact`): OP-16 for each open use and copy on the touched
  domains, under the version in force and under the change's compiled preview.

### B25-21 Held gate decisions settle, and a moved row is asked again (built 2026-11-25, D-41)

The wave 3 verification found two engine gaps. Each fix is the AI sessions' choice under D-41, with the reason.

- **A gate decision that waited for its gate settles once the gate is ready.** G-14 keeps a decision proposed while
  OP-07 says its gate is not ready, even with every approval in, and before this build nothing looked at it again: a
  design decision Elif approved before the data-use card was decided stayed proposed after Nadia approved the card.
  Now the engine looks again at an application's proposed gate decisions (`SettleHeldGateDecisions`) at three points:
  when another decision of the application is accepted (inside the accept step, so in the same call as the answer that
  accepted it), at the end of each composed write call that touched the application's rows (a decision, a review, a
  document link, an owner assignment, a tier assessment, a data use, the application), and in the daily due-state
  refresh, which plans an approvals item for each held decision that would settle now (an owner who starts on a later
  day). A decision settles only when it was requested (it holds an Approval Record), OP-03 finds its approvals satisfied
  and OP-07 finds its gate ready; the recompute then accepts it exactly as the last answer would have. A decision nobody
  requested yet is left for its proposer to submit. The checks are reads: their reasons are dropped, so a call that
  settles nothing says nothing about it. Reason: settling at the moment the gate turns ready is what the demo story
  promises ("the decision page would list that use with a link to its card"), and doing it in the engine keeps every
  path (the decision page, a card the runner brings, a review closed, the daily run) the same.
- **`ReaskApprovals` asks a requested decision again from the start.** It cancels the pending Approval Records and opens
  the requests OP-02 needs now, consultations included, of the people the decision's scope names now. It refuses a
  decision with an answer on record (`approval.outcome-final`) and one with nobody to ask (`data.no-one-to-ask`).
  Reason: `RequestApprovals` answers "already requested" for a decision that holds any record, cancelled ones included,
  so a move that cancelled the old owner's request and asked again made nothing, and nobody was asked.
- **`WithdrawDataDecision` withdraws a proposed data-use decision as the engine's own write.** Only a proposed decision of
  the data-use type; it cancels the pending records and writes the withdrawn status with a reason. Reason: when the rows
  move to another data owner after someone answered, or to a domain with no data owner, the question no longer stands as
  it was asked, and the person who moves the rows is usually not its proposer (G-16 lets only the proposer or the
  decider withdraw).
- **Tests:** `DataToolTests.MovingUsesToADomainWithAnotherDataOwnerAsksTheNewOwner` and
  `DataToolTests.AHeldDesignDecisionSettlesOnceTheDataUseItsGateWaitsForIsDecided` (MCP core). The runtime suite runs
  the settle step on every composed write, and no earlier test changed.

### B25-22 The engine opens a moved row's decision, and a plain write settles held decisions (built 2026-11-25, D-41)

Wave 4 starts with the wave 3 findings still open. Each choice is the AI sessions' under D-41, with the reason.

- **`OpenDataDecision`: the engine opens and sends the data-use decision a moved row needs.** When
  `move_data_references` moves rows to a domain with another data owner, a decided row is declared again, an answered
  decision is withdrawn, and a confirmed row can be left needing a decision. Before this build the tool opened that
  decision as the mover's own write, so it needed the mover's proposal right (C31): the data governance lead, one of the
  two roles that may move rows, holds none, and the new row stayed "needed" with nobody asked. Now the engine opens the
  decision as its own nested write, proposed with the allow outcome, and requests its approvals at once. It is stamped
  as the engine's write for the mover (Performed By Principal, Created By Principal and On Behalf Of name the mover;
  Performed By Actor Kind is engine), so the mover is its proposer for the separation rules and may withdraw it. Only a
  confirmed, open data use or data mapping with no proposed data-use decision; anything else is invalid input. Reason:
  the move itself was allowed (its Tool Policy and OP-08 on every moved row), and the decision is what the move makes
  necessary, so no second right is asked, just as none is for the requests a re-ask opens. One path for every mover keeps
  the stamps the same whoever moves the rows (a choice under D-41).
- **A plain write settles held gate decisions.** `Runtime.Write` (a caller's single write outside a composed call, such
  as an adapter's or the approval runner's) now ends with the same `SettleHeldGateDecisions` step a composed call ends
  with, over the applications its writes touched, unless the write was a replay or changed nothing. Reason: the fix pass
  left such a held decision for the next accepted decision of the application or the daily refresh; the engine should
  settle it in the call that made the gate ready, whatever the entry point.
- **The gate words do not sound final.** G-14's message reads "so its gate decision stays proposed until the gate is
  ready" (it read "cannot be accepted", which the wave 3 verification found sounded permanent), and an unmet requirement
  is named in straight double quotes like every other label.
- **Tests:** `DataTaxonomyRuntimeTests.APlainWriteSettlesAHeldGateDecision` (runtime; it fails without the step), and
  `DataToolTests.MovingUsesToADomainWithAnotherDataOwnerAsksTheNewOwner` for the platform admin and the data governance
  lead, covering the unanswered, answered and decided paths and a needed row with no decision (MCP core).

### B25-23 A read call reads each row once, and loads many rows in one store call (built 2026-11-25, D-41)

Measured through a local server: the portfolio summary, My work and the applications list were slow, because each made
hundreds of store round trips (the summary 767 on the demo estate). Each choice is the AI sessions' under D-41, with the
reason.

- **Key set reads are a store port.** `IKeySetReader` (Ports/KeySetReads.cs) has two reads: `GetMany(entity, ids)` and
  `QueryIn(entity, column, values)`, each one store call, answering exactly what one `Get` or one `Query` per key
  answers: the same rows, each once, by ID; a value compares as a JSON string, ordinal. Every store implements it:
  InMemoryStore and its transaction, the row store over SQLite (`json_each`) and Azure SQL (`OPENJSON` with a typed
  `WITH` column, so the clustered key is sought), and each transaction overlay with its own writes on top. A value longer
  than the 4,000 characters a database narrows by is read one query each, as before. `KeySetReads` falls back to one
  read per key on any other reader, so an adapter that does not batch still answers the same. Reason: a port, not a
  cache trick, is what lets every store answer a page in one round trip; no schema change is needed (no migration, the
  schema stays at version 8).
- **A read call has a read cache of its own.** `CallReadCache` wraps the transaction of every read-purpose call
  (`SnapshotPurpose.Read`: every read tool, OP-15 to OP-17 reads, the approval and decision reads). It asks the store
  about each row, key and query once; a query with no condition loads the whole entity, after which every read of that
  entity is answered from it (by a column index built once); a key set read loads the rows of many values of a column;
  a query with one text condition counts as a key set read of its value. An answer from the cache is the one the store
  would give: the same rows, filtered by `RowQuery.Matches`, by ID. Reason: a read call never commits, so nothing it
  reads can change under it except through a concurrent write, which a single answer should not mix in anyway; a write
  through the cache empties it first. A write call keeps reading from the store as before.
- **Composed reads load ahead.** `ComposedCall.Prefetch(entity, column, values)`, `PrefetchIds` and `PrefetchAll` load
  in one store call the rows a read will look at many times; they do nothing in a write call. `get_portfolio_summary`,
  `get_my_queue` and `find_applications` load the portfolios, the people, and the owners, document links, decisions and
  applications of every application in scope up front. OP-16 and OP-17 load their per-row facts in about fifteen store
  calls (`EngineRuntime.PrefetchDataFacts`): the decisions about every use and mapping and the approval records of the
  open ones, the owners of every domain and location, each application's decisions, tier assessments and intake
  requests. The items are then built exactly as before. Reason: the tools keep their logic; only where the rows come
  from changes, which is what makes "identical results" provable.
- **The session reads less.** The backend reads the labels of a caller's role groups in one key set read, and keeps the
  publish date of a configuration version for the process (a version row is append-only, class A), so the session no
  longer reads every version's content on every call.
- **The answers are the same; the cost no longer grows with the estate.** On the SQLite store: the summary makes 22
  database reads (767 before), My work 29 or 30 (616 and 633), the applications list 6 (76), on the demo estate and on
  the same estate grown to 480 or 2,000 applications alike. `EngineRuntime.BatchReads` (default true) turns the cache
  off; the tests use it to show that every heavy read and list read answers the same either way. Timings:
  src/adapters/azure/STATUS.md.
- **Tests:** `StoreConformanceTests.KeySetReadsAnswerWhatOneReadPerKeyAnswers` (in-memory, SQLite, and Azure SQL when a
  database is configured); `ReadSpeedTests.TheHeavyReadsMakeAFewDatabaseReadsWhateverTheSizeOfTheEstate` (a ceiling
  per read, on the demo estate and on 480 applications), `ReadSpeedTests.TheBatchedReadsGiveTheSameAnswersAsReadingRowByRow`
  (17 reads with the cache on and off, on the demo estate and on 240 applications) and the opt-in timing test
  (PRODUCT_TIME_READS=1); `ListReadTests.AReadCallReadsEachRowOnceAndItsCostDoesNotGrowWithTheEstate` (MCP core). Every
  existing engine, MCP and host test now runs with the cache on.

### B25-24 "Ask again", host changes on the record, the O25 key and the measure settings (built 2026-11-25, D-41)

The engine side of document 24, B24-22. Each choice is the AI sessions' under D-41, with the reason.

- **`ComposedCall.RenewApprovalRequest`.** One composed write: the request and its decision (refused unless proposed,
  `decision.final`); the caller (a person who proposed the decision or holds platform-admin, `approval.renew-not-allowed`);
  the request's state (expired, or pending past or within 48 hours of its Expires On, `approval.not-renewable`). A request
  that expired and whose requirement already has a fresh pending request (same rule or decision right, consultation
  flag, role and owner role) answers renewed false and writes nothing. Otherwise the engine writes expired (past its
  time) or cancelled (asked early) on the old request, then runs the recompute (`RecomputeApprovals`), which opens what
  the requirement still needs, with the decision's stamped version and its Timeout Days, exactly as the daily refresh's
  restart does; a consultation, which the recompute never restarts, is opened again from OP-02's consultation for its
  rule. Nothing opened is refused (the requirement is met, or nobody holds the role), and a new record that names nobody
  for a data owner subject is refused with DC-06, as a first request is. Every write is an engine-nested write OP-08
  judges (G-06). Reason: one path with the refresh keeps who is asked and when the request expires the same however the
  request comes back.
- **Host changes are on the record.** A tool that changes a host table (the approval outbox) calls
  `ComposedCall.RecordHostChange(entity, rowId, state)`. The composed call then writes its one Audit Entry as it does for a
  call that wrote engine rows, keyed or not, and lists the host row beside the writes (`byHost` true, `byEngine` false);
  a dry run records nothing. Reason: the person's act must be in the audit trail even when no engine row changes; the
  engine still writes no host table itself.
- **The alternate key of O25 is part of G-18.** A data location with a URL and no External Id is known by its platform and
  URL, so a second one in service with the same pair is refused (`data.location-duplicate`); the runtime passes the
  other one as the fact `duplicateLocation`. A location with an External Id is known by it, and retiring one is never
  refused for its key. Fixtures `g18-duplicate-location-refused`, `g18-duplicate-location-with-external-id-allowed` and
  `g18-retiring-a-duplicate-location-allowed`. Reason: section 22 choice 8 left the key recorded as a gap; `save_data_location`
  already matches an existing location by the pair, so only an update that points one location at another's URL, or a
  write past the tool, meets the rule.
- **V38 knows the measure settings.** `measure-targets` and `signal-thresholds` are required json settings, and V38
  checks their keys and numbers against `MeasureSettings` (a measure key, a signal that takes a number, a number of zero
  or more). The bundle schema checks the shape too. Balanced grows from 648 to 650 rows, and the replay records carry the
  new Balanced hash.
- **O09 Slide Back Signal** joins the logical model as a proposal column (a key of the ten signals of chapter 17), with
  the generated drafts. The C31 and C32 data scope columns and their two contracts (owner subject, data taxonomy change)
  are marked adopted, as data taxonomy slice 2 made them.

### B25-25 The engine follow-ups of wave 4b (built 2026-11-25, D-41)

Each choice is the AI sessions' under D-41, with the reason. The tools' side is document 24, B24-23.

- **Only the newest record of an ask is asked again** (`ComposedRenewApprovalRequest`). The engine finds the newest
  record of the same request (same rule, right, role, owner role and consultation flag) asked after this one, in any
  outcome: requested later, or at the same moment while this one is closed and that one pending (the close and the
  restart of one call). If there is one, a pending one not yet due answers renewed false and names it; anything else is
  refused (`approval.not-renewable`): "Approval request <id> was asked again as <newest>; ask that one again." Two records
  asked together, both pending or both closed, are two answers of one rule, not one asked again as the other. Reason: the
  "asked again already" test looked only for a pending request not yet due, so an older record of an ask whose newer
  request had also lapsed opened a second new request.
- **The separation refusal names the rule by its label** (`ApprovalAnswer`): "The separation rule No self-approval at
  tier 3 keeps you from answering this approval request..."; the key stays in the reason's Ref. Reason: the message
  reaches AI clients and the decision page as it is, and a key in backticks is not a sentence a person reads. The fixture
  runner learned `messageContains` on an expected reason, so `self-approval-at-tier-3-refused` holds the words.
- **The answer check for the person an AI client acts for** (`ComposedCall.ApprovalAnswerCheck(id, forThePerson: true)`):
  the same check with the person as the actor, read only. Reason: an AI client's own check stops at "AI never approves",
  which hid whether a separation rule keeps the person from answering, so the queue and the approvals count could not
  leave such a request out for an AI client.
- **OP-06 takes `isRetired`.** A retired application (its stage means retired, or its registration is archived) owes no
  document: no requirement applies, and its links stay as history, so nothing is missing or stale. Fixtures
  `retired-application-owes-no-document` and `live-application-stale-link-incomplete`. Reason: the retired Legacy Timesheet
  App still listed a stale wiki page among the documents to fix; the due-state refresh already skipped a retired
  application's documents.
- **The due-state run writes each notice kind's count** on its audit entry (`noticeKinds`), so the Admin page sums the six
  kinds (remind, repeat, escalate, overdue, expired, flag) for a run and for the tile alike. Reason: the run's
  `escalations` counted the escalated flag, which an expired-approval notice also carries, while the tile counted kinds.
- **One application's OP-17 facts** (`ChecksInput`, scope `{ applicationId }` alone): that application and its uses, with
  the decisions about those uses; every owner, location, mapping and dependency stays. A finding names an application
  only when it is about one of its uses (DC-05 to DC-07, DC-11, DC-13, DC-14) or about the application (DC-09, DC-10), so
  the answer is the same as the estate's findings about it (`WaveFourBTests.OneApplicationsDataFindingsAreTheEstateFindingsAboutIt`).
  Reason: the Application 360 data panel built the facts of all 2,000 applications for one; 1.2 s became 0.4 s.
- **The seed writes Published On** on the version it seeds (`InMemoryStore.SeedVersion` and `RowStore.SeedVersion` take
  the seed moment). A column, so no seeded ID or order changes.
- **V38 requires `review-request-due-days`** (number; Balanced 14; document 21, section 3.6). Balanced grows from 650 to
  651 rows, and the replay records carry the new Balanced hashes (Balanced, and Balanced with linked change management).
