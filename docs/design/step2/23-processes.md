# Step 2, document 23: processes

> **Platform decided (D-26, 2026-11-22).** The first release is designed to run in the client's own Azure subscription. In each process's "Bindings" table the Azure column applies. The Dataverse binding is kept as a possible later option, not a plan. Approval cards on Azure follow working default Q19 in [28-working-defaults.md](28-working-defaults.md). The process text below was written while the data store was still open (D-22), so it describes both bindings.

- **Date:** 2026-11-22. **Status:** draft design document, written client-neutral.
- **Step:** 2 of 3 (the design). This document describes how work moves through the product, from idea to retirement, and what each process creates, asks and records.
- **Name:** the product has no name yet, so this file says "the product" (D-05).
- **Names.** Every entity, column and term comes from [20-glossary-and-entities.md](20-glossary-and-entities.md) (the glossary). Entity IDs such as O11 Change Request or C32 Approval Rule point there. Terms this document needs and the glossary lacks are in section 5, "New terms". The glossary is not edited.
- **Platform-neutral (D-22).** This document was written while the data store was open: Dataverse, or a database in the client's own Azure subscription. D-26 has since chosen Azure (note above). Each process is described once, in logical terms. A "Bindings" table at the end of each process says what runs where on each platform.
- **Follows:** decisions D-01 to D-22, with D-26 for the platform. The public decision records are in [docs/decisions/](../../decisions/). Where this file and a decision disagree, the decision wins.
- **Sweep proposals.** A news sweep reviewed what Microsoft changed in October and November 2026, including Ignite 2026. Its proposals that this design adopts are marked "(sweep proposal)". They are not decisions.
- **Patterns.** Some steps carry pattern IDs (for example I-189) as provenance tags. They are general governance patterns, generalized and used as patterns only.
- **Microsoft facts** carry a source ID. [P1] to [P21] are new in this document (section 6). [S1] to [S21] and [J1] to [J5] are the glossary's sources (glossary section 4). A few facts link their Microsoft Learn page inline instead. Anything no source confirmed is marked UNVERIFIED.
- **Working defaults.** Aligned to [28-working-defaults.md](28-working-defaults.md) (alignment pass, 2026-11-22). Its sections 1 and 2 are used as the design. A default that is still one of the six open design questions (its section 3) is marked "(working default, open design question N, see 28-working-defaults.md)", with N as numbered in 28. Other references to 28 read "(28, issue N)" or "(28, QN)". None of them is a decision.

**Seven rules every process here obeys.**

1. **Configuration, never fixed values** (D-06, D-07, D-10). Who decides, who approves, which tier needs what, which documents, which reviews and when: all rows. The engine never branches on a tier number, a stage number or a label. "Default tier 1" below means the shipped row with key `tier-1`. The engine does know a short list of fixed meanings, such as "this status is final" or "this approver is a person", while every label stays configurable (glossary 2.4; working default, open design question 1, see 28-working-defaults.md).
2. **AI never approves** (D-02, D-10). AI clients draft, check and propose. Only a person answering an approval card can approve. The data enforces it.
3. **Roles only through Entra groups** (D-11). A process asks for a role. The engine resolves the role to an Entra group through C30 Role Group Binding, or to an owner role holder through O06 Owner Assignment. Non-human workloads (pipelines, the approval runner) hold a workload role through a recorded, expiring Workload Grant (C42). People never get one, and agent identities never get one (working default, open design question 12, see 28-working-defaults.md).
4. **Links, not copies** (D-12, D-15, D-17, D-19). A process stores an ID, a URL, a label and a "last seen" date for anything another tool owns.
5. **The product writes only its own records** (D-17, D-19). It never writes wiki pages, work items, pipelines, Entra settings or Microsoft admin settings. People's own AI tools do that work outside, and the product records the link.
6. **Decisions are append-only** (glossary rule 6, [S1]). A process never edits an accepted decision. It records a new decision that supersedes it.
7. **Every write says who did it** (D-10). Actor kind, principal, on behalf of, client app and configuration set version are stamped on every row.

---

## 0. How to read this document

- **Process IDs.** P01 to P11 are the eleven processes the product owner named for the design. S1 to S3 are shared machinery that every process uses. Diagrams are D23-1 to D23-9 (section 7).
- **Template.** Every process has the same parts: purpose, trigger, parties and roles, steps, records, approvals, configuration points, MCP tools, Microsoft tools linked, and bindings.
- **RACI letters** follow the engine contract (glossary 2.4): **R** does the work, **A** answers for the outcome (exactly one per row), **C** gives input before, **I** is told after, **S** supports with a draft or a check that a person accepts (AI only). The RACI tables show the **default configuration** (C31 Decision Right rows). Every cell is a configuration row a client can change (D-07, D-20).
- **Role abbreviations used in RACI tables.**

| Abbreviation | Role | Kind (glossary entity) |
|---|---|---|
| RQ | Requester | Product role (C29) |
| MK | Builder (maker, fusion or pro team) | Owner role (C28) |
| BO | Business owner | Owner role (C28) |
| SP | Sponsor | Owner role (C28) |
| DO | Data owner | Owner role (C28) |
| TO | Technical owner | Owner role (C28) |
| PA | Platform admin (the platform team or CoE) | Product role (C29) |
| SR | Security reviewer | Product role (C29), a default seed row (28, issue 7) |
| PMO | PMO | Product role (C29) |
| AR | Architect | Product role (C29) |
| CM | Change manager | Product role (C29) |
| CB | Change board | Product role (C29) |
| AIL | AI lead | Product role (C29, D-20) |
| CA | Configuration admin | Product role (C29, D-10) |
| DR | Deployment recorder (a workload identity, not a person) | Workload role (C29, Is Workload Role), held through a Workload Grant (C42) |
| AI | An AI client acting for a person, such as Claude Code | Actor kind (C38), never a role holder |

- **MCP tool names** are document 24's names. The MCP surface document (step 2, document 24) owns the names and the route per tool, including the tools 28 (issue 11) adds. Section 5.1 maps the candidate names an earlier draft of this file used. The routes: the Dataverse MCP server's `invoke_api`, Microsoft's MCP management server, or the product's own MCP server (D-09; three routes, sweep proposal). Every tool here runs as the signed-in person, unless the table says otherwise.
- **"Card"** means an approval card: a Power Automate approval answered in Outlook or Teams (glossary term, D-16).

---

## 1. Process catalog

| ID | Process | Starts when | Main decision types (C18) | Main records | Decisions | When |
|---|---|---|---|---|---|---|
| S1 | Approvals through approval cards | Any proposed decision. Every decision type goes on a card, except a locked list | Any | O09, O10 | D-02, D-07, D-10, D-16 | R1 |
| S2 | Reminders, escalation and notifications | Something is due, late or flagged | None | O21, O19, O10, O23 (status only), O24 | D-06, D-13 | R1 |
| S3 | The engine write path | Any write from any caller | None | Every row | D-10, D-22 | R1 |
| P01 | Intake and triage | A person or their AI client asks for something, or registers an app already built | intake, priority | O07, O08, O09, O02, O06, O18 | D-01, D-02, D-12 | R1 |
| P02 | Tier assessment and required approvals | Tier answers are given or change | tier, risk acceptance, data use, autonomy level | O08, O09, O02, O19, O21 | D-06, D-07, D-20 | R1 |
| P03 | Change management | A change is ready, urgent, or found after the fact | change authorization, go-live | O11, O12, O09, O10, O21 | D-03, D-08, D-13 | R1 |
| P04 | Architecture review | A configured trigger fires | design, data use, exception | O21, O09, O19, O20 | D-01, D-17 | R1 |
| P05 | Deployment record | A pipeline step, an AI client or a person reports a deployment | (none; may start P03) | O13, O14, O15, O16, O20 | D-03, D-13, D-19 | R1. Check step later |
| P06 | Gap check and claim | An admin asks "what is running that is not in the register?" | ownership | O22, O23, O03, O02 | D-15 | R1 |
| P07 | Documentation tracking | A requirement applies, a document goes stale, or an owner records a link | (gate requirements read it) | O19, O20 | D-17 | R1 |
| P08 | Owner leaves | A leave is announced, or an Entra signal or an agent signal is found | ownership | O06, O05, O09, O10, O21 | D-01, D-11, D-15, D-20 | R1 |
| P09 | Retirement | A portfolio decision says retire, or a component vanished | portfolio, retirement, change authorization | O09, O11, O20, O02, O23 | D-01, D-15, D-19 | R1. Checklist later |
| P10 | AI draft and human accept | An AI client creates or changes a row | Any | Every operational row | D-02, D-10 | R1 |
| P11 | Configuration change | An admin instructs a change | configuration change, gate policy change | C46 Configuration Draft, C02 and C01 to C40 at activation, O09, O10 | D-06, D-07, D-10 | R1 |

---

## 2. The value stream from idea to retirement

The default configuration ships eleven lifecycle stages (C12, numbered 0 to 10 for display only) and five gates (C13). Stages, gates and gate requirements are configuration rows (D-06). Microsoft's agent lifecycle (Intake, Triage, Build, Deploy, Monitor, Improve, Retire) is the closest official shape ([agent lifecycle](https://learn.microsoft.com/agents/center-of-excellence/agent-lifecycle)); the product adds prioritization, design review, change control and portfolio review. Microsoft's front door guidance says to track the intake decision apart from delivery, and that the front door agent "recommends a route" and "doesn't approve the work" [S3].

| # | Stage (C12) | Lifecycle meaning | Leads (default) | Decides, one accountable (default) | Processes | Decision type (C18) | Gate after (C13) |
|---|---|---|---|---|---|---|---|
| 0 | Idea | pre-live | Requester or builder | Business owner: worth asking for | P01, P10 | (draft only) | |
| 1 | Intake and triage | pre-live | Platform admin | Platform admin: accept, park, redirect, decline | P01, P02 (initial tier), P06 (claims become intakes) | intake | **Accept** |
| 2 | Prioritize and fund | pre-live | PMO | Sponsor: fund, defer, stop | P01 | priority | **Commit** (only when money or central capacity is needed) |
| 3 | Design and architecture review | pre-live | Architect (default tiers 2 and 3); builder self-check (default tier 1) | Architect: design and build path. Data owner: data use | P04, P07 | design, data use, exception | |
| 4 | Risk and tier | pre-live | Platform admin | Platform admin: the tier. Business owner: residual risk at default tier 3 | P02 | tier, risk acceptance, autonomy level | **Design** (after stages 3 and 4) |
| 5 | Build | pre-live | Builder | Builder: done when tests pass | Delivery links (P01 step 9), P07 | (none) | |
| 6 | Test | pre-live | Builder with the business owner | Business owner: accepted. Security reviewer: passed (default tier 3) | P02 (review types it added), P04 | acceptance | |
| 7 | Change and release | pre-live | Platform admin | Business owner (default tiers 1 and 2); change manager (default tier 3 and every emergency) | P03, P05 | change authorization, go-live | **Go-live** (after stages 6 and 7) |
| 8 | Operate | live | Platform admin (default tiers 2 and 3); builder (default tier 1) | Business owner: supported and earns its place | P05 (later changes), P07, S2 | (none) | |
| 9 | Review | live | PMO (portfolio); platform admin (owner and access checks) | Business owner: keep, invest, replace, retire | S2, P08, P02 (tier check), P04 | portfolio, ownership | **Value** |
| 10 | Retire | retired | Platform admin | Business owner: retirement complete | P09 | retirement | |

**Three rules shape the stream.**

1. **The tier sets the weight.** In the default configuration a default tier 1 app skips prioritization and design review, and its business owner authorizes its changes. A default tier 3 app gets a design review, a security review and the change manager. The engine reads each tier row's rules. It never branches on a tier number (D-06). Microsoft's agent guidance gives three risk tiers with required controls, and says tier 3 needs "a formal process owner" and "a quarterly maturity review" [S2].
2. **Intake and delivery are tracked apart.** An accepted idea can still be in design. Acceptance is not a release gate [S3].
3. **Stages 5 to 7 repeat for every change after go-live.** A change returns to stage 3 only when it crosses a design trigger (P04), and to stage 4 when it could change the tier (P02).

**The maker's path and graduation.** A builder registers an app as soon as anyone else uses it (P01). When a graduation trigger fires (wider audience, shared data, restricted data, a team depends on it, the builder leaves), the product prompts the business owner (prompts are later). Graduation itself is a tier decision (P02) plus an ownership decision with a new technical owner and deputy (P08 shape, glossary "Graduation"). Microsoft's support guidance asks for "a graduation path" and a handover path for critical apps [S4].

**Gates are recorded decisions, not meetings.** A gate decision (C13 Gate Decision Type) can be accepted only when every gate requirement (C14) in scope holds: a decision accepted, a document current, or a review done. The engine checks this on the acceptance write (S3), whoever writes.

---

## 3. Shared machinery

### 3.1 S3: the engine write path (every write, every caller)

Every process writes through the same path, so the rules hold for the app, the web page, both MCP routes, pipeline steps, imports and Work IQ (D-10).

| # | Step | What the engine does | Fails when |
|---|---|---|---|
| 1 | Identify the caller | Reads the calling principal and, when an AI client or a pipeline acted for a person, the person (On Behalf Of). Records the client app ("client app X on behalf of user Y", sweep proposal) | The caller cannot be identified |
| 2 | Resolve the actor kind | Person, AI client for a person, agent identity, pipeline, approval runner, or engine (C38). Rows written by an AI client get Is AI Created = yes | (never fails; unknown means "AI client for a person" when the call arrived through MCP) |
| 3 | Check the role | The caller must hold a product role that allows this write: through a bound Entra group (C30), or an owner role on this application (O06), or a workload role held through a Workload Grant (C42), such as the deployment recorder (working default, open design question 12, see 28-working-defaults.md) | No role allows it |
| 4 | Validate against configuration | Reads the configuration set version in force (C01, C02): keys exist, statuses are allowed transitions by engine meaning, required fields per entity rules, gate requirements on gate acceptances. A missing rule row, such as a gate policy row, is a broken configuration: the engine refuses the governed write and reports it, with no built-in fallback (document 21, rule 8; 28, issue 14) | A rule fails, or the configuration is broken. The error names the rule and the guideline (C39) |
| 5 | Guard the protected columns | Only the approval writer (the approval runner's workload role) may write approval outcomes (O10 Outcome, Responded On, Typed Reason). Live configuration rows change only inside the activation operation (P11). Accepted decisions, approval records and audit entries are append-only | Anyone else writes them |
| 6 | Apply AI defaults | A row an AI client creates starts as draft (record status) or proposed (decision status) (D-02). See P10 | (never fails; it downgrades the status) |
| 7 | Stamp | Configuration set version, performed-by columns, timestamps, and Engine Checked On, so a scheduled read-only check can find rows written past the engine (28, Q16) | (never fails) |
| 8 | Derive | Computes flags (P05), Counts Toward Requirement (S1), Stale On (P07), Next Review On (S2). "Missing" documents are computed when read, never stored (28, issue 16) | (never fails) |
| 9 | Audit and emit | Writes an O24 Audit Entry for product events, and emits an event when a process needs to continue (approvals, notifications) | (never fails the write; a failed emit is retried) |

**Bindings for S3.**

| Concern | Dataverse binding | Azure database binding |
|---|---|---|
| Where the path runs | Synchronous plug-ins on the Create and Update messages of every product table, plus custom APIs for one-call process actions (D-10). The plug-ins hold the rules, because the Dataverse MCP server's `create_record` and `update_record`, the Web API and Work IQ reach the tables directly ([Dataverse MCP tools](https://learn.microsoft.com/power-apps/maker/data-platform/data-platform-mcp)) | The API layer, as a rules library behind a storage port, in one Azure Functions app (Flex Consumption, .NET 10) with the MCP server (28, issue 13). Database permissions allow writes only from the API's managed identity, which passes the person's ID in SESSION_CONTEXT for row-level security (glossary 2.9; [row-level security](https://learn.microsoft.com/sql/relational-databases/security/row-level-security); 28, Q20) |
| Whose rights | The engine runs as the caller, with an engine-nested marker. It never elevates. Column security protects approval outcomes. If spike S2 fails, elevation is allowed only inside named engine effects (28, issue 9 and Q15) | The same rule: the API acts for the calling person and checks that person's rights |
| Custom API shape | Global binding with simple parameters. Lists travel as a JSON string, because generative pages cannot call custom APIs with the EntityCollection binding or Entity parameters [S13] (sweep proposal) | Plain HTTP endpoints with the same contract names (glossary 3.3) |
| Caller identity | `userid` and `callinguserid` in Dataverse auditing [S16]. Whether a plug-in can tell an MCP call from an app call is UNVERIFIED, so the product's own MCP server stamps the client app itself | The token's `oid` and the calling application's ID, read from the validated token |
| Append-only | The plug-in refuses updates and deletes of final rows | Ledger tables for the frozen and append-only entities [S18], with no personal data in free text (28, Q21) |
| Events out | A custom API with no plug-in, cataloged as a business event, starts cloud flows through the "When an action is performed" trigger [P11] | An outbox row plus an Azure Service Bus queue message, picked up by a function |

### 3.2 S1: approvals through approval cards

Every process that needs a person's yes uses this one sub-process. It is how D-02 ("AI never approves"), D-07 (decision rights as configuration) and D-16 (approval cards in Outlook and Teams) meet.

**Trigger.** A decision (O09) reaches status proposed, or a confirmation card is needed (P10). Every decision type goes on at least one card (28, issue 1).

**Steps.**

1. **Resolve the rules.** The engine finds the C32 Approval Rule groups for the decision type whose optional scope columns (tier, lifecycle stage, change type, application kind, target stage, portfolio) match. The single most specific matching group wins, and broader groups drop out (document 21, section 10.3; 28, issue 2). Combined cases, such as a tier 3 emergency, have their own group in the seed. When no rule group matches, the card goes to the holder of the decision type's accountable letter (C31) (28, issue 1). The engine may accept a decision with no card only when it fits the locked list (`engine-acceptable-decision-types`): lowest-tier intake where no counting rule group matches, and standard changes whose model a person approved once (document 25, OP-02 rule 8; working default, open design question 8, see 28-working-defaults.md). Minimum Count is never 0 (document 21, V28). `explain_rules` returns the engine's rule trace, so a person can see which group applied.
2. **Resolve the approvers.** For an owner role (Approver Owner Role), the current holders in O06 Owner Assignment for this application. For a product role (Approver Role), the members of the bound Entra group, read live from Entra, because a Dataverse group team lists only members who have opened the environment and memberships are cached for up to 8 hours [S7].
3. **Create pending approval records.** One O10 Approval Record per required response (Minimum Count per rule, always at least 1), outcome pending, with Requested On and Expires On (Timeout Days). A rule with Is Consultation = yes sends a consultation card instead, whose outcome is recorded as "recommendation only" and never counts (section 5).
4. **Send the card.** The approval runner (section 5) starts one Power Automate approval per rule, with the approval mode from the rule: everyone must approve, first to respond, or sequential [S10]. The card shows the statement, the context, a link to the record, and the required typed reason when Requires Typed Reason is yes.
5. **Wait.** People answer in Outlook, Teams or the Power Automate action center. A flow can wait at most 28 days; after that the flow fails and the approval stays open in the action center [P5]. For longer waits Microsoft's pattern is two flows: one creates the approval and another acts on the response [P6].
6. **Write the outcome.** The approval runner, and only it, writes Outcome, Approver, Responded On, Typed Reason, Channel and Flow Run Url. It writes through the approval writer workload role (28, issue 7). Its actor kind is "approval runner".
7. **Count it.** OP-03 (document 25) sets Counts Toward Requirement to yes only when all of these hold: the outcome is approved; Approver Actor Kind is person; Approver Principal Kind is user (sweep proposal); the responder holds the required role at response time (checked live, as in step 2); the responder passes the C40 Separation Rule rows in force (I-148; 28, issue 5). Shipped defaults: Balanced refuses self-approval at tier 3; Regulated and the strict separation overlay refuse it at every tier; Starter and the small team overlay allow it only through a recorded exception decision (I-206; 28, Q10).
8. **Settle the decision.** When every rule in the winning group is satisfied, the engine moves the decision to accepted and stamps Decided On. The decider is the person whose approval satisfied the accountable letter (C31). For a decision type on the locked list (step 1), the engine accepts with actor kind engine and links the decision that set the rule, such as the C21 model's Approved By Decision for a standard change. A rejected required approval moves the decision to rejected. Gate requirements are checked again at this moment (section 2).
9. **Time out.** At Expires On the engine sets the outcome to expired, cancels the Power Automate approval, and asks S2 to remind or escalate to the deputy (C26). A new card goes out for a restart.

**Rules that protect "AI never approves".**

| Rule | How it holds | Residual risk |
|---|---|---|
| No approve tool | No MCP tool, custom API or endpoint approves, rejects or accepts a decision | None inside the product |
| Only the runner writes outcomes | Column security (Dataverse) or the API's role check (Azure) lets only the approval writer workload role write outcomes, and refuses any other writer, the approver's own AI client included | None inside the product |
| Only a person counts | Step 7 above. An AI stage in a client's own agent flow is recorded as "recommendation only" | None inside the product |
| No decision without a person, outside the locked list | Step 1 above. A decision type with no matching rule still goes to its accountable holder. The locked list is a locked setting, so a client cannot extend it by configuration | The locked list itself (working default, open design question 8, see 28-working-defaults.md) |
| Reassignment | Power Automate lets an approver reassign a request; the requester cannot [P7]. The engine counts the new responder only if they hold the required role | A reassigned card answered by someone without the role is recorded and flagged, never counted |
| Sequential steps | Power Automate does not allow the same approver on two steps [P8]. The engine checks this when it builds a sequential card | None |
| An approver's own tools answering for them | Microsoft Graph has an Approvals app API (beta, "not supported in production") whose `approvalItemResponse` create call takes a delegated permission (`ApprovalSolution.ReadWrite` or `ApprovalSolutionResponse.ReadWrite`) [P10]. Whether flow approvals are reachable through it is UNVERIFIED. Agent accounts that answer email and Teams are announced (sweep proposal) | The product cannot see who pressed the button inside Microsoft's approval. Mitigations: the setup guide tells admins not to consent those Graph permissions to AI client apps; typed reasons; Purview keeps the Teams approval audit [P9]; the approvals spike tests that an agent identity cannot complete a card (sweep proposal) |

**What approvers need.** "Users who respond to approval requests don't need a Premium license" [P1]. They need Power Automate rights, and the Approvals User role is not granted when ad hoc subscriptions are off, when a security group gates the environment, or on US Government plans [P4]. Guest approvers need a Power Automate license [P5].

**Card hygiene.** If an approver answers by email or in the action center, the Teams card does not update [P5]. The default configuration therefore sends one channel per rule (C26 Channel), not both.

#### 3.2.1 Learn check: can a back end that is not Dataverse start Power Automate approvals, and what license does the flow need?

Checked on Microsoft Learn on 2026-11-22.

| Question | Answer | Source |
|---|---|---|
| Can an outside back end start an approval flow? | Yes. A cloud flow with the "When an HTTP request is received" trigger can require a Microsoft Entra token. Its "Specific users in my tenant" mode accepts object IDs of service principals, so only the product's API identity can start it. The token's audience must be `https://service.flow.microsoft.com/` (public cloud) | [P3] |
| Which connector sends the card? | The Approvals connector, which is a standard connector | [P4] |
| Can the flow call back the back end? | Yes, through an HTTP action or a custom connector to the product's API. A custom connector needs a standalone Power Apps or Power Automate license, or a Process license on the flow | [P19], [P2] |
| Can Microsoft Graph create the approval instead? | Only through the Approvals app API in beta: "Use of these APIs in production applications is not supported", and creating an `approvalItem` takes a delegated permission only ("Application: Not supported"). A back end acting as itself cannot use it | [P10] |
| Where does approval data live? | In Dataverse: "When you create approval flows, they're saved in Dataverse", and the first approval flow in a non-default environment provisions the database. So even the Azure binding needs one Power Platform environment with Dataverse for the approval runner. It holds only the approvals' titles and details, not the product's records | [P4] |
| Whose license does the flow run on? | "Automated and scheduled flows use the flow owner's license context. Instant flows (button, Power Apps) use the invoking user's license context." A flow owned by a service principal needs a Process license, or a designated licensed user | [P1] |
| Is one licensed owner enough when the API starts approvals for many people? | Not safely. The FAQ says a premium flow "invoked by multiple users" needs every user licensed or a Process license, and calls a user who "indirectly triggers the flow" and "gets value" from it a case of multiplexing when unlicensed. Whether the product's API starting a flow for many requesters counts as "invoked by multiple users" is UNVERIFIED | [P1] |
| What does a Process license do? | It "lets it use premium and custom connectors, no matter what license the owning or triggering user has". "Flows must be in a solution for a Process license to be assigned." Up to 25 solution-aware flows can share one Process license as a flow group | [P2], [P1] |
| Is the HTTP request trigger premium? | The Learn pages read in this pass do not state its tier. Treat it as premium for costing: UNVERIFIED | (none) |
| Price | Not stated here, because list prices change. See Microsoft's Power Automate pricing page for the Premium and Process prices | https://www.microsoft.com/power-platform/products/power-automate/pricing |

**Design consequence.** The approval runner is a small set of solution-aware cloud flows (send card, wait, write outcome, notify) placed in one flow group with one Process license. That answers the license question the same way on both bindings, and the requesters and approvers need nothing extra. A Logic Apps runner avoids Power Automate licensing but can send only Outlook approval emails (the Office 365 Outlook "Send approval email" action [P15]); whether the Approvals connector exists in Logic Apps is UNVERIFIED. For the Azure binding the working default is the Power Automate runner flow group with one Process license. The product's own approval page is offered where a client's security review asks for token-level evidence of the approver (28, Q19).

**Bindings for S1.**

| Concern | Dataverse binding | Azure database binding |
|---|---|---|
| What starts the runner | The engine's custom API `RequestApprovals` (no plug-in, cataloged as a business event) fires the "When an action is performed" trigger [P11]. The flow owner needs Read on the Custom API, Process and SDK Message tables to see the catalog [P11] | The API writes an outbox row; a function posts it to the runner flow's HTTP trigger, restricted to the API's service principal [P3] |
| Runner location | A cloud flow in the product's own environment, so approvals live next to the product's data | A cloud flow in a Power Platform environment with Dataverse, used only for approvals [P4] |
| Runner identity | An application user that holds the approval writer workload role through a Workload Grant (C42); column security lets only it write outcome columns (working default, open design question 12, see 28-working-defaults.md) | An Entra app identity with the approval writer app role, recorded as a Workload Grant (C42); the API's role check lets only it write outcomes |
| Waiting longer than 28 days | Two-flow pattern [P6], or the engine restarts the card at Expires On | The same, or a Durable Functions orchestration that races a durable timer against the outcome event and restarts or escalates [P14] |
| License for the runner | Process license on the flow group [P1][P2] | Process license on the flow group [P1][P2]. The product's own approval page where a security review asks for token-level evidence (28, Q19) |
| Duplicate outcomes | The plug-in ignores a second outcome for the same approval | External events arrive at least once; the API deduplicates by approval ID [P14] |

### 3.3 S2: reminders, escalation and notifications

**Trigger.** A daily due-state refresh, or an event (a flag, a timeout, a claim).

**Steps.**

1. **Due-state refresh (daily).** The engine moves document links from current to stale when Stale On passes (P07), creates reviews whose Next Review On has come (O21 status due; C25 cadence per tier), expires approvals past Expires On (S1 step 9), and lists open claims older than the claim reminder rule (P06).
2. **Remind.** For each due item the engine applies the C26 Reminder Rule for its subject kind (review, document, approval, claim): first reminder Lead Days before due, repeats every Repeat Days.
3. **Escalate.** After Escalate After Days the reminder goes to the deputy (O06 Is Deputy). If there is no deputy, it goes to the accountable role in C31 for that decision type.
4. **Notify flags.** At gate level Flag or higher, a deployment flag (P05) sends a message to the owner roles and roles in the gate policy row's Flag Notify list, or the `default-flag-notify` setting (by default business owner, technical owner and change manager; document 21, C33) (D-03, D-13).
5. **Record.** Each reminder and escalation is an O24 Audit Entry (operation "reminder sent"), so "who was told, when" is provable.

**Configuration points.** C25 Review Cadence, C26 Reminder Rule (Lead Days, Repeat Days, Escalate After Days, Channel), C24 Review Type, the `channel` value list (C06).

**Bindings for S2.**

| Concern | Dataverse binding | Azure database binding |
|---|---|---|
| Daily refresh | A scheduled cloud flow calls the custom API `RefreshDueStates`; the plug-in does the work, so the logic stays in the engine (D-10) | A timer-triggered function calls the same rules library |
| Messages | A cloud flow posts a Teams message or sends an Outlook email (not an approval) | The approval runner flow group also carries the notification flow |
| Idempotency | Each reminder carries the item ID and the reminder number, so a rerun never sends twice | The same |

### 3.4 What runs where, at a glance

| Part | Dataverse binding | Azure database binding |
|---|---|---|
| Engine | Plug-ins and custom APIs (D-10) | API with a rules library (D-22), in one Azure Functions app (Flex Consumption, .NET 10) that also hosts the MCP server. Container Apps is the fallback if the on-behalf-of or MCP spike fails on Functions (28, issue 13) |
| The product's own MCP server | Azure-hosted (D-09, D-16). Calls the custom APIs on behalf of the person; an end-to-end on-behalf-of spike runs first | In the same Functions app as the API and the engine |
| Engine database access | Inside Dataverse, as the caller | Managed identity, plus the person's ID in SESSION_CONTEXT for row-level security (28, Q20) |
| Live reads as a person (gap check, owner check) | In the MCP server, with the admin's delegated token, because a plug-in cannot hold a person's token for another API | The same |
| Approval cards and notifications | Cloud flows in the product's environment | Power Automate approval cards from a runner flow group with one Process license, in a Power Platform environment with Dataverse, started over HTTP. The product's own approval page where a security review asks for token-level evidence (28, Q19) |
| Timers | Scheduled cloud flows that call custom APIs | Timer-triggered functions; Durable Functions for long waits [P14] |
| Pipeline record step | Web API call to the `RecordDeployment` custom API as an application user with a Workload Grant (C42) | HTTPS call to the API with an Entra token from workload identity federation, for a service principal with a Workload Grant (C42) |
| Roles | Entra group teams for people (D-11); workload roles on application users, each recorded as a Workload Grant (C42) [S8] (working default, open design question 12, see 28-working-defaults.md) | Entra app roles on groups for people [S9]; workload app roles on service principals, each recorded as a Workload Grant (C42) |
| Audit | Dataverse auditing plus O24 product events [S16] | O24 as the full change log in a ledger table [S18] |
| Screens | Generative pages, classic forms underneath (D-18) | A web page in the client's Azure (D-22) |

**MCP server design notes that every process inherits (sweep proposals).** Tools are stateless; a long operation (the gap check) returns a run ID and progress, never server-held session state. Claude Code reaches the server through a registered Entra client first, with Client ID Metadata Documents ([CIMD](https://learn.microsoft.com/entra/agent-id/client-id-metadata-documents), preview) as an option for pilots; CIMD becomes the lead once it is generally available (28, Q14). Any gateway in front passes the caller's token through, never a shared backend credential.

---

## 4. The processes

### P01 Intake and triage

**Purpose.** One front door for every kind of business application: ideas, apps already built, agents, AI-built apps and SaaS. It records the request, the initial tier and the triage decision, and keeps intake apart from delivery.

**Decisions it follows.** D-01, D-02, D-06, D-07, D-12, D-15, D-16.

**Trigger.**
- A requester or builder asks through an MCP client ("I want an app that...", "register the app I built"), the app or the web page.
- A claim from the gap check that belongs to no registered application (P06 step 6).
- An owner asks to re-register after a graduation trigger (later prompts).

**Parties and roles (default configuration).**

| Step or decision | RQ | MK | BO | PA | SR | PMO | AR | SP | AI |
|---|---|---|---|---|---|---|---|---|---|
| Submit an idea, or register an app already built | R | R | A | I | | I | I | | S |
| Triage: accept, park, redirect to reuse, decline | I | I | C | A/R | C | C | C | | S |
| Set the initial tier from the plain questions | R | R | C | A | C | | | | S |
| Score and rank demand that needs central capacity | | I | C | C | | A/R | C | | S |
| Fund, defer or stop | | I | | C | | R | C | A | |
| Choose the build path | | C | C | R | | C | A | | S |

**Steps.**

1. **Draft.** The person describes the need. Their AI client calls `save_intake_draft`. The engine creates an O07 Intake Request in draft with Is AI Created = yes (P10). Only governance questions are asked: purpose, business owner, data, audience, uses AI, proposed kind.
2. **Answer the tier questions.** The AI client fetches the questions (`get_tier_questions`, C09 filtered by Applies To Kinds) and records the answers as a draft O08 Tier Assessment (`answer_tier_questions`). P02 computes the initial tier.
3. **Check for duplicates and reuse.** `check_duplicates` searches the register by name, purpose and kind. A match fills Duplicate Of as a suggestion, never as a decision.
4. **Submit.** The requester submits (`submit_record`, subject kind intake request). An intake submission is a request, not a decision. An MCP call on the person's behalf is enough where Tool Policy (C41) lets an AI client submit this subject kind. The shipped default allows intake, change and decision submissions (P10; working default, open design question 9, see 28-working-defaults.md). Status moves to submitted.
5. **Open the triage decision.** The engine creates an O09 Decision of type intake, status proposed, with the outcomes from C19 (accept, park, redirect, decline). It notifies the consulted roles from C31 (architect for reuse, PMO for demand, security reviewer for initial risk). Consulted input, when configured, arrives as a consultation card (C32 Is Consultation = yes). OP-03 records its outcome as "recommendation only", and it never counts (section 5, "Consultation"; 28, issue 12).
6. **Decide.** The platform admin, who holds the accountable letter for intake, answers the triage card with a typed reason (S1). The exception is lowest-tier intake: it is on the locked list, and the shipped presets give intake no approval rule, so the engine accepts it with no card and links the configuration decision that set the rule (document 25, OP-02 rule 8; working default, open design question 8, see 28-working-defaults.md). A client that wants a card here gives intake an approval rule (document 21, V19).
7. **Act on the outcome.**
   - **Accept:** the engine creates the O02 Business Application (registration status registered, stage from C19 Next Lifecycle Stage) or links the existing one, and proposes an ownership decision for the named business owner. Nobody becomes an owner without their own yes on a card (P08 shape).
   - **Park:** the PMO sets Horizon (now, next, later). The intake stays visible to the requester.
   - **Redirect:** Duplicate Of is confirmed; the requester is pointed to the existing application and its owner.
   - **Decline:** the reason is visible to the requester. Intake status moves to rejected.
8. **Prioritize when needed.** If an answer says the request needs money or central capacity (a C10 Triggers Json entry), the engine opens a priority decision. The PMO records Score, Horizon, Size Band and Expected Benefit; the sponsor decides on a card. The Commit gate follows.
9. **Link the delivery work.** The requester's AI tool creates the work item in Azure DevOps through the Azure DevOps MCP server as the same user, then calls `save_link` (link category delivery; O18, link kind Azure DevOps work item). Planner plans and GitHub Projects are linked the same way (D-12). The product never reads the work item or the plan (D-12).

**Records created or changed.**

| Entity | What happens |
|---|---|
| O07 Intake Request | Created in draft, submitted, then completed, rejected, withdrawn or cancelled by meaning |
| O08 Tier Assessment | Created with Answers Json and Computed Tier |
| O09 Decision | intake, and priority when needed; ownership on accept |
| O10 Approval Record | One per required response (S1) |
| O02 Business Application | Created or linked on accept |
| O06 Owner Assignment | Created after the ownership decision is accepted |
| O05 Principal Reference | Created for requester, owners and deciders on first sight |
| O18 Delivery Link | Added by the requester's AI tool or a person |

**Approvals (default rules).**

| Decision type | Default approver | Mode | Notes |
|---|---|---|---|
| intake | Platform admin role (the accountable holder) | First to respond | Typed reason required. Lowest-tier intake is on the locked list the engine may accept with no card (working default, open design question 8, see 28-working-defaults.md) |
| priority | Sponsor (owner role) | First to respond | Only when the request needs money or central capacity |
| ownership | The named business owner | Everyone must approve | The named person must accept |

**Configuration points.** C09 and C10 (questions, answers, Triggers Json), C15 Application Kind, C18 and C19 (intake and priority outcomes, Next Lifecycle Stage), C31 and C32 (who triages, who is consulted), C13 and C14 (Accept and Commit gate requirements), C06 lists `horizon` and `size band`, C37 Link Kind (delivery category), C41 Tool Policy (AI May Submit per subject kind), the setting `intake-visibility` (default "own": the requester, PMO, platform admin and architect; 28, issue 10).

**MCP tools used (document 24 names).**

| Tool | What it does | Identity |
|---|---|---|
| `save_intake_draft` | Creates or updates a draft intake | Signed-in person |
| `get_tier_questions` | Returns the questions for a kind | Signed-in person |
| `answer_tier_questions` | Records the tier answers as a draft assessment | Signed-in person |
| `check_duplicates` | Searches the register for duplicates and reuse | Signed-in person |
| `submit_record` | Submits a draft (subject kind intake request) | Signed-in person |
| `get_my_queue` | "What needs me": intakes to triage, cards waiting | Signed-in person |
| `save_link` | Records a link to a work item, plan, project or repository (link category delivery) | Signed-in person |

**Microsoft tools linked, never rebuilt.** Azure Boards work items and the Azure DevOps MCP server (D-12); GitHub Issues and Projects; Planner plans (link only); Microsoft's agent front door guidance and its sample as a reference [S3]; the Power Platform catalog for reuse.

**Bindings.**

| Concern | Dataverse binding | Azure database binding |
|---|---|---|
| Draft and submit | Custom APIs `SaveIntakeDraft`, `SubmitRecord` (named after their tools, document 24, section 9.1); plug-ins validate and stamp | API endpoints with the same names |
| Duplicate search | Dataverse search or a custom API with a simple query | SQL full-text or a simple match in the API (design point for the engine document) |
| Triage card | S1 runner flow | S1 runner flow |

---

### P02 Tier assessment and required approvals

**Purpose.** Turn plain answers into a stored tier, decided by a person, and from that tier derive everything the application now needs: approvals per decision type, gate requirements, documents, review cadence and the gate policy rows.

**Decisions it follows.** D-06, D-07, D-17, D-20.

**Trigger.**
- An intake is submitted (initial tier, stage 1).
- Design is done (confirmed tier, stage 4).
- An answer changes, a change request could change the tier (P03), a tier check review is due (S2), or a graduation trigger fires.

**Parties and roles (default configuration).**

| Step or decision | MK | BO | DO | PA | SR | AR | CM | AIL | AI |
|---|---|---|---|---|---|---|---|---|---|
| Confirm an application's tier | R | C | C | A | C | C | I | | S |
| Accept residual risk (default tier 3) | I | A | C | C | C | C | I | | |
| Approve a data use | I | I | A | R | C | C | | | |
| Approve restricted data reaching an AI model | I | I | A | R | C (must agree) | C | | C | |
| Set an agent's autonomy level | I | C | C | R | A | C | | C | |

The restricted-data row has a second yes: the security reviewer must agree. RACI allows one A, so the default configuration expresses it as an approval rule with two approver roles and mode "everyone must approve". The AI lead is consulted on autonomy levels (D-20).

**Steps.**

1. **Answer.** The person or their AI client records answers in O08 (`answer_tier_questions`). The AI may propose answers from the intake text; it never sets the tier.
2. **Compute.** The engine maps each answer to C10 Implied Tier and takes the most serious by C08 Rank. It also collects every Triggers Json entry (for example "add review type security", "add review type architecture", "add review type responsible-ai").
3. **Explain.** `answer_tier_questions` returns which answer drove the result, and `explain_rules` returns the engine's rule trace. The Microsoft vocabulary for the tier comes from C11 Vocabulary Mapping through `get_configuration` (support tier, Copilot Studio zone, agent risk tier) [S2][S4][S5].
4. **Propose the tier decision.** The engine creates an O09 Decision of type tier, status proposed, outcome the computed tier. A person may raise the tier on the same decision. Lowering it below the computed tier needs a typed reason and the full approval rule set (glossary O08).
5. **Approve.** S1 sends cards per C32 for the decision type tier at this scope. At the highest default tier the engine also opens a risk acceptance decision for the business owner.
6. **Store.** On acceptance the engine writes O02 Tier and Tier Decision. The tier is a stored value, never a formula (glossary "Tier").
7. **Derive what the tier needs.** The engine recomputes, from configuration, for this application:
   - the approval rule group that now applies per decision type and stage (C32; the most specific group wins);
   - the gate requirements per gate (C14), including the review types the answers added;
   - the required documents (C23), from each requirement's From Stage. "Missing" is computed from configuration (OP-06), and an O19 Document Link row exists only once a link is recorded (P07; 28, issue 16);
   - the review cadence (C25): O02 Next Review On is set;
   - the gate policy row per change type and target stage (C33), shown to the owner.
8. **Agents.** For an application of an agent kind, the engine opens an autonomy level decision. The agreed level is stored on O02 (Agreed Autonomy Level, Autonomy Decision). Enforcing it stays in Microsoft's tools: Copilot Studio, Agent 365 and Entra [S2].
9. **Answer "what does my tier need?"** `get_requirements` returns the list from step 7 in plain words, with links to the guidelines (C39).

**Records created or changed.**

| Entity | What happens |
|---|---|
| O08 Tier Assessment | Answers and Computed Tier; Tier Decision filled on acceptance |
| O09 Decision | tier; risk acceptance; data use; autonomy level |
| O10 Approval Record | Per S1 |
| O02 Business Application | Tier, Tier Decision, Next Review On, Agreed Autonomy Level |
| O19 Document Link | None created here. New requirements show as computed "missing" until a link is recorded (P07) |
| O21 Review | Due rows scheduled from the cadence |

**Approvals (default rules).**

| Decision type | Default tiers 1 and 2 | Default tier 3 |
|---|---|---|
| tier | Platform admin | Platform admin and security reviewer (everyone must approve) |
| risk acceptance | (not required) | Business owner |
| data use | Data owner | Data owner; plus security reviewer for restricted data to an AI model |
| autonomy level | Security reviewer (AI lead consulted) | Security reviewer (AI lead consulted) |

**Configuration points.** C08 Tier (Rank), C09 Tier Question, C10 Tier Answer Option (Implied Tier, Triggers Json), C11 Vocabulary Mapping, C14 Gate Requirement, C23 Documentation Requirement, C24 and C25 (review types and cadence), C31 and C32 (deciders and approvers per tier), C33 Gate Policy. The Microsoft CoE baseline preset maps its tiers one to one to Microsoft's three agent risk tiers [S2] (sweep proposal). It ships in the first release, extending Balanced, and is re-checked against Microsoft's guidance each release (28, Q6).

**MCP tools used (document 24 names).** `get_tier_questions`, `answer_tier_questions`, `explain_rules`, `save_decision_proposal` (decision type tier; opens the decision and cannot accept it), `get_requirements`.

**Microsoft tools linked, never rebuilt.** Power Platform support tiers [S4], Copilot Studio zones [S5], agent risk tiers [S2] as vocabularies; Managed Environments and environment groups (linked through O15, never set by the product, D-17); Entra Agent ID sponsors (linked).

**Bindings.**

| Concern | Dataverse binding | Azure database binding |
|---|---|---|
| Compute and derive | Plug-in on O08 and on decision acceptance; custom API `GetRequirements` (Function, named after its tool) | API rules library |
| Missing documents | Computed by OP-06 when read; no row is created | The same |

---

### P03 Change management

**Purpose.** Plan, authorize, schedule and close every change to a business application, across every tool that deploys it. Built-in change management is the default; linked mode keeps the client's ITSM tool as the system of record (D-08).

**Decisions it follows.** D-03 (open: the gate ladder), D-06, D-08, D-13, D-19.

**Trigger.**
- A change is ready: an owner, builder or their AI client starts a change request.
- An emergency: service down or a security exposure.
- A deployment record arrives with no change request (P05), at a gate level where the policy expects one.
- A retirement needs the removal scheduled (P09).

**Mode.** O11 Mode is copied from the C04 setting "change management mode" (built in or linked) when the request is created. The setting may carry an optional portfolio scope, and the most specific row wins (28, Q2).

**Parties and roles (default configuration).**

| Step or decision | MK | BO | DO | PA | SR | AR | CM | CB | PMO | AI |
|---|---|---|---|---|---|---|---|---|---|---|
| Keep the standard change models per tier | I | I | | R | C | C | A | | | |
| Authorize a normal change (default tiers 1 and 2) | I | A | C | R | | | I | | | S |
| Authorize a normal change (default tier 3) | I | C | C | R | C | C | A | C (exceptions, cross-app) | I | S |
| Approve an emergency change | I | C | | R | C | | A | | I | |
| Keep the change schedule and windows | I | C | | R | | | A | | I | |
| Deploy, in the client's own tool | I | I | | A/R | | | I | | | |
| Declare the application live (go-live) | I | A | | R | | | I | | I | |
| Review after an emergency or failed change | C | C | | R | C | | A | | | S |

The change manager authorizes default tier 3 changes and every emergency. The change board handles exceptions and windows that cover several applications. There is no separate emergency approver role (28, issue 4 and Q11).

**Steps, built-in mode.**

1. **Draft.** `save_change_request_draft` creates an O11 Change Request in draft: title, main application, proposed change type, window start and end, planned target. The engine proposes O12 Affected Application rows by walking dependencies (`get_dependency_impact`, OP-10, glossary "Impact"), with the owners of dependants listed for information.
2. **Classify.** `check_standard_change` calls document 25's standard change evaluation (OP-12), which evaluates the C21 Standard Change Model conditions (Conditions Json) it can see (28, issue 12) (model listed for this tier; no new data use, connector, permission or identity in the answers; no dependants for a shared component; same tested artifact). It returns "qualifies" or "does not qualify, because". The AI proposes; a person sets the type. Changes that always become normal: a new restricted data use, a role change for the app's own identity, an agent model or instruction change, a change to a shared component with dependants, anything that could raise the tier. Microsoft's agent guidance: "Changes that can materially affect agent behavior should trigger re-evaluation before production deployment. Model replacements and instruction updates can change agent outputs" [P21].
3. **Submit.** The change owner, or their AI client where Tool Policy allows it, submits (`submit_record`, subject kind change request; working default, open design question 9, see 28-working-defaults.md). The engine reads C20 for the change type: Requires Authorization, Allows Authorization After, Requires Review After.
4. **Authorize.**
   - **Standard:** the engine accepts the change authorization decision as pre-authorized, citing the C21 model and the decision that listed it (Approved By Decision). Standard changes are on the locked list of decision types the engine may accept with no card (working default, open design question 8, see 28-working-defaults.md).
   - **Normal:** an O09 Decision of type change authorization goes to S1 with the most specific approval rule group for this tier and change type. Default: business owner for default tiers 1 and 2; change manager for default tier 3, with security reviewer and architect consulted; the change board for exceptions and changes that cross many applications.
   - **Emergency:** the change manager authorizes at once on a card, through the emergency rule group (tier 3 emergencies have their own group in the seed). Microsoft's guidance says to "define who can approve safe deployment acceleration in an emergency" [P20]; the change manager's approval rule for the emergency change type is that answer, as configuration. There is no separate emergency approver role (28, issue 4). Because C20 Allows Authorization After is yes, the deployment may happen first and the record is completed after. The emergency rule (C33 Emergency Rule Json) sets how long the follow-up may take. A post-change review becomes due (C20 Review Due Days).
5. **Schedule.** The window (D-13) shows on the change schedule view and, read-only, on the timeline. No freeze tables, no booking. Enforcing a window stays in the deployment tool, for example the Azure Pipelines Business Hours check [P13].
6. **Deploy.** The client's own tool deploys. Deployment records arrive through P05 and link to the change by its ID, or by match (same application and target, inside the window).
7. **Go-live.** For a first production deployment the engine proposes a go-live decision. The Go-live gate requirements (C14) are checked at acceptance: acceptance decision accepted, change authorized, a succeeded deployment record at a production target stage, required documents current, required reviews done.
8. **Close.** The engine suggests the Result (succeeded, partly succeeded, failed, rolled back, cancelled) from the linked deployment results. `record_change_result` records the result as reported. The change owner confirms, because moving the status to completed is a person's act (document 25, G-08).
9. **Review.** A failed change or any emergency opens an O21 Review of type post-change, due per C20. Microsoft's guidance: "Postmortems should follow all failed deployments without exception" [P20].

**Steps, linked mode.**

1. The change request holds External Change Number and External Change Url, entered by a person, an AI client or a client-built record step. The ITSM tool's workflow is never copied (D-08).
2. The product does not run its own authorization workflow. The client's ITSM tool reports the authorization state through a client-built step (the same kind as a pipeline record step), which records an authorization fact with an evidence link (O20). Where no step exists, the change manager attests "authorized in the external tool" on a confirmation card. A recorded number alone never counts (28, Q22).
3. The missing-approval flag (P05) treats a linked change as authorized only when that authorization fact exists. Document 25's OP-14 evaluates the linked-mode authorization fact (28, issue 12).
4. Windows, deployment records, flags, the schedule and the timeline work as in built-in mode.

**Records created or changed.**

| Entity | What happens |
|---|---|
| O11 Change Request | Created, submitted, completed or cancelled; Mode, window, external reference, Result |
| O12 Affected Application | Proposed by the impact walk, confirmed by the change owner |
| O09 Decision | change authorization; go-live; post-change review outcome |
| O10 Approval Record | Per S1 |
| O14 Deployment Record | Linked to the change (P05) |
| O20 Evidence Link | Test results, impact lists, a client's own release record, an ITSM authorization fact |
| O21 Review | post-change |

**Approvals.** As in step 4. Changing a gate policy row is itself a decision of type gate policy change (P11).

**Configuration points.** C04 change management mode; C20 Change Type (Requires Authorization, Allows Authorization After, Requires Review After, Review Due Days); C21 Standard Change Model (Conditions Json); C31 and C32 for change authorization and go-live; C40 Separation Rule (who may approve their own change); C33 Gate Policy (gate level, fail mode, Emergency Rule Json); C17 Dependency Type (Counts For Impact); C14 for the Go-live gate.

**Gate levels (D-03, still open).** Off, Record and Flag work with the core alone. Advise and Enforce need a check step before the deploy, published as a copy-paste sample (D-19); the check step and L05 Gate Check come after the gate decision. A resource administrator in Azure DevOps can bypass an Invoke REST API or Business Hours check, and the bypass is shown in the checks panel [P13]; the product sees such a deployment through P05 and flags it.

**MCP tools used (document 24 names).** `save_change_request_draft` (also takes the external change number and URL in linked mode), `check_standard_change`, `get_dependency_impact`, `submit_record`, `get_change_schedule`, `record_change_result`, `get_my_queue`. The work item for the change is created through the Azure DevOps MCP server as the same user, then linked with `save_link` (link category delivery).

**Microsoft tools linked, never rebuilt.** Power Platform Pipelines approvals and gated extensions [P12]; Azure Pipelines approvals and checks, including Business Hours [P13]; GitHub environments and required reviewers; Agent 365 publish approvals for agents (sweep proposal); Copilot Studio CLI named deployments, whose guidance asks teams to "Keep a release record" [J2], linked as evidence; Power Platform Admin in Cowork impact lists, linked as evidence (sweep proposal).

**Bindings.**

| Concern | Dataverse binding | Azure database binding |
|---|---|---|
| Change actions | Custom APIs `SaveChangeRequestDraft`, `CheckStandardChange` (Function), `SubmitRecord`, `RecordChangeResult`, named after their tools; plug-ins enforce status rules | API endpoints |
| Linked-mode authorization | The client-built step calls the API as a workload identity with a Workload Grant (C42); attestation through an S1 confirmation card | The same, on the API endpoint |
| Impact walk | Custom API `GetDependencyImpact` (Function; the name follows document 24, section 9.1), because the Dataverse SQL tool appears to lack recursion ([Dataverse SQL](https://learn.microsoft.com/power-apps/developer/data-platform/webapi/query/sql); UNVERIFIED whether `read_query` uses exactly that subset) | A recursive query in the API |
| Authorization cards | S1 | S1 |
| Schedule | A view and a generative page timeline | A view and the web page timeline |

---

### P04 Architecture review

**Purpose.** Make the review request find the architect when a configured trigger fires, and keep every review and architecture decision in the shared decision log with Microsoft's ADR status words [S1].

**Decisions it follows.** D-01, D-06, D-17.

**Trigger** (all configuration).
- A tier answer adds the review type architecture through C10 Triggers Json: writes under its own identity, restricted data, an organization-wide or external audience, an agent that executes actions, a pro-code part or hosting outside Power Platform.
- A change to a shared component that others depend on (P03 step 1 found dependants with Counts For Impact).
- A request to depart from a standard (an exception; standards list L04 later).
- A Design gate requirement of kind "review done" or "decision accepted" for decision type design.

**Parties and roles (default configuration).**

| Step or decision | MK | BO | DO | PA | SR | AR | CM | AI |
|---|---|---|---|---|---|---|---|---|
| Review a design that crosses a trigger | R | I | C | C | C | A | I | S |
| Record an architecture decision | C | | | C | | A/R | | S |
| Grant an exception to a standard | C | I | | C | C | A/R | | |
| Impact review of a shared component (owners of dependants consulted) | C | C | | R | | A | I | S |

The architect role is the design authority by default, with one approval rule group per tier on the decision type design. The seed role list has no architecture board role (28, issue 7). A client that wants a board for default tier 3 and exceptions adds that role and gives the tier 3 group to it, as configuration.

**Steps.**

1. **Request.** The engine creates an O21 Review of type architecture, status due, with the trigger that caused it in the O21 trigger column (28, issue 15). A person, or their AI client, can also ask for one with `request_architecture_review` (28, issue 11).
2. **Prepare.** The builder's AI client drafts the architecture decision record from the discussion: context, options considered, outcome, confidence [S1]. The full text lives in the repository or wiki, written by the builder's own AI tool (D-17). The product records the link (`record_document_link`; O19, document type architecture decision record) and a proposed O09 Decision of type design with Options Considered (`save_decision_proposal`).
3. **Review.** The architect reviews. Linked assessments (for example a Well-Architected assessment export) become O20 Evidence Links with an evidence level (I-44), recorded with `save_link` (link category evidence).
4. **Decide.** S1 sends the design decision to the architect (or to a board role, where a client added one). A data-use decision goes to the data owner when data use changed (P02).
5. **Close the review.** O21 moves to done, with Outcome Decision. The engine recomputes the Design gate.
6. **Exceptions.** An exception decision records the standard it departs from, the reason and the review date. It is a normal decision and can be superseded.

**Records created or changed.** O21 Review (architecture); O09 Decision (design, data use, exception); O10 Approval Record; O19 Document Link (the ADR); O20 Evidence Link (assessments, impact lists).

**Configuration points.** C10 Triggers Json; C24 Review Type (architecture; proposed new defaults security and responsible-ai, section 5); C18 Decision Type (Needs Options Considered = yes for design); C31 and C32 (the design authority, one rule group per tier); C14 Design gate requirements; C22 and C23 (ADR as a required document per tier).

**MCP tools used (document 24 names).** `request_architecture_review`, `get_my_queue` (kind review), `save_decision_proposal` (creates a proposed decision; cannot accept it), `get_dependency_impact`, `record_document_link`, `save_link` (link category evidence).

**Microsoft tools linked, never rebuilt.** Well-Architected assessments and ADR guidance [S1]; the Architecture Center; Azure API Center entries for APIs and MCP servers (link targets); the Azure DevOps wiki and repositories where the ADR text lives.

**Bindings.**

| Concern | Dataverse binding | Azure database binding |
|---|---|---|
| Review creation from triggers | Plug-in on tier acceptance and on change request submit; custom API `RequestArchitectureReview` for a request by a person | API rules library; an endpoint with the same name |
| Decision cards | S1 | S1 |

---

### P05 Deployment record

**Purpose.** Record every deployment of every artifact to every target, whatever tool ran it, and flag the ones that came another way, lacked their approval or landed outside their window. The product never builds, hosts or runs a pipeline (D-19).

**Decisions it follows.** D-03, D-13, D-15, D-19.

**Trigger: three arrival paths (D-19).**

| Path | Who sends it | How | Route (C36) | Actor kind |
|---|---|---|---|---|
| **A. Pipeline step** | A step at the end of the client's Azure Pipelines, GitHub Actions or Power Platform Pipelines run (a copy-paste sample). For Power Platform Pipelines, a flow in a custom host on `OnDeploymentCompleted` [P12] | Calls the product's API with the pipeline's workload identity | pipeline step | pipeline |
| **B. MCP** | The owner's AI tool, for example Claude Code after a `pac` import, a Copilot Studio CLI deploy or a runtime deploy | `record_deployment` through the product's MCP server, on behalf of the person | AI tool | AI client for a person |
| **C. Manual** | A person in the app or the web page | A form over the same action | person in app | person |

Path A signs in with the pipeline's own workload identity, which holds the deployment recorder role through a Workload Grant (C42) (working default, open design question 12, see 28-working-defaults.md).

**Parties and roles (default configuration).**

| Step or decision | MK | TO | BO | PA | CM | DR | AI |
|---|---|---|---|---|---|---|---|
| Record a deployment (path A) | | I | I | A | I | R | |
| Record a deployment (path B or C) | R | R | I | A | I | | S |
| Follow up a flag: open a change after the fact, or record an exception | C | C | C | R | A | | S |

**Steps.**

1. **Receive.** The sender passes: application, artifact name and version (with digest when known), target, route, run URL, started and finished times, result, version before and after, change request ID if known, pipeline reference, and an Idempotency Key (run ID plus stage plus attempt, I-189). Optional (sweep proposals): the merge actor, the AI client that queued the run on a person's behalf, the stage order actually run, and the reported code approvals (who approved the code merge: a person, AI only, none or unknown). `record_deployment` takes the reported code approvals as an input (28, issue 11).
2. **Upsert.** The engine upserts O13 Artifact Version by artifact name plus version, then O14 Deployment Record by Idempotency Key, so a retry never creates a second row (I-189). An abandoned run is recorded with result "not deployed" (sweep proposal).
3. **Resolve links.** Target by External Id (O15). Pipeline by External Id (O16; an unknown pipeline is created with a "not mapped" note for the platform admin). Change request by ID, or by match: an open change for the same application and target whose window covers Finished On. If several match, none is linked and the record is flagged for a person.
4. **Apply the gate policy.** The engine reads the C33 row for the application's tier, the change type (from the linked change; without one, from the setting `unlinked-deployment-change-type`, section 5) and the target's stage, and stores Gate Level Applied. There is no built-in fallback. If no row matches, the configuration is broken: the engine refuses the write and reports it, and the record step warns without stopping the deployment. V23 makes this case impossible after validation (document 21, rule 8; 28, issue 14).
5. **Compute the flags.**
   - **Is Other Path:** the route is not in the target's C35 Change Path Allowed Routes.
   - **Is Outside Window:** Finished On is outside the linked change's window (D-13).
   - **Is Missing Approval:** the change has no accepted authorization decision (with counting approvals, or pre-authorized by a standard change model), or a linked change has no authorization fact (P03), or there is no change where the policy expects one. An emergency inside its follow-up time (C33 Emergency Rule Json) is not flagged until the time runs out. (Proposed by the build, 2026-11-22: documents 21 and 25 keep it flagged with its due date; B23-1 at the end of this document.)
   - **AI-only code approval:** when the sender reports that the only code review approval came from an AI (GitHub Copilot approvals can count toward required approvals, off by default [J1]), the C33 column AI Only Code Approval Handling decides: ignore, flag, or stop at Enforce (sweep proposal). AI-only code approvals never count as authorization, and the shipped default is flag (working default, open design question 7, see 28-working-defaults.md).
6. **Act by gate level.** Off: nothing beyond the record. Record: flags are stored; nobody is told. Flag: S2 notifies the Flag Notify list (S2 step 4) the same day. Advise and Enforce: the same, plus the check step before the deploy (later, after the gate decision; L05).
7. **Follow up.** For a flagged deployment the change manager decides: open a change request after the fact (P03) or record an exception decision. O14 is final at insert, so `record_deployment_correction` links the deployment to the new change by adding a correcting record (28, issue 11). No gate level stops a manual import; it can only be flagged (D-03).
8. **Keep facts current.** The record updates Last Seen On on O15 and O16. The product's record stays the durable evidence when GitHub deletes old runs (sweep proposal).
9. **Go-live hint.** A first succeeded production record for an application in a pre-live stage prompts the go-live decision (P03 step 7).

**Records created or changed.** O13 Artifact Version; O14 Deployment Record (flags, Gate Level Applied, Idempotency Key, Reported Approvals Json; correcting records that link a change); O15 Deployment Target and O16 Pipeline Reference (Last Seen On); O17 Pipeline Map Entry (checked, not changed); O20 Evidence Link (run, client release record, CLI release record [J2]); O11 Change Request (Result suggestion).

**Approvals.** None in this process. Follow-up decisions use S1.

**Configuration points.** C33 Gate Policy (Gate Level, Fail Mode, Emergency Rule Json, AI Only Code Approval Handling, shipped at flag); C34 Target Stage; C35 Change Path; C36 Deployment Route; C37 Link Kind for pipeline kinds, including the proposed runtime and CLI kinds (sweep proposal); C04 setting `unlinked-deployment-change-type` (section 5); C26 for flag notifications; C42 Workload Grant for each pipeline identity.

**MCP tools used (document 24 names).** `record_deployment` (with reported code approvals), `get_deployments` (`flaggedOnly` for flags), `record_deployment_correction`. A pipeline step calls the API directly, not MCP.

**Microsoft tools linked, never rebuilt.** Power Platform Pipelines (custom host for events and gates; personal pipelines "can't be extended") [P12]; Azure Pipelines approvals and checks [P13]; GitHub Actions and environments; Copilot Studio CLI named deployments [J2]; runtime app deployments (sweep proposal).

**Bindings.**

| Concern | Dataverse binding | Azure database binding |
|---|---|---|
| Write | Custom API `RecordDeployment` (Global, simple parameters; optional lists as a JSON string), upsert by alternate key on Idempotency Key [S15] | API endpoint; upsert (merge) on a unique Idempotency Key |
| Flags | Synchronous plug-in, same transaction | API rules library, same transaction |
| Pipeline identity | An application user holding the deployment recorder role directly, recorded as a Workload Grant (C42) with a reason, the deciding decision and an expiry [S8] (working default, open design question 12, see 28-working-defaults.md) | An Entra app role assigned directly to the pipeline's service principal, recorded as a Workload Grant (C42); workload identity federation, no secret. A group's app roles do not reach a service principal's token ([app roles](https://learn.microsoft.com/entra/identity-platform/howto-add-app-roles-in-apps)) |
| Failure behavior | Fail soft: the record step warns and never fails the deployment at Off, Record and Flag (I-189) | The same |

---

### P06 Gap check and claim

**Purpose.** Answer "what is running that is not in the register?" live, as the admin who asks, and put only the gaps on a claim list for owners. No scheduled import, no copy of Microsoft's inventory (D-15).

**Decisions it follows.** D-15.

**Trigger.** An admin asks in Claude Code or another MCP client. Nothing runs unattended.

**Parties and roles (default configuration).**

| Step or decision | RQ (candidate owner) | BO | TO | PA | AIL | AI |
|---|---|---|---|---|---|---|
| Run the gap check | | | | A/R | | S |
| Claim an item for an application | R | A (accepts ownership) | C | I | | S |
| Dismiss an item, with a reason | | | | A/R | | |
| Confirm a vanished component (retired, moved, replaced) | | I | R | A | | S |
| Decide on an agent Microsoft flags (keep, reassign, consolidate, retire) | | C | | R | A | S |

The last row follows the sweep proposal to reword the AI lead's duty to "decides on the agents Microsoft flags" (D-20).

**Steps.**

1. **Ask.** The admin calls `run_gap_check` with the estates to read (C06 list `estate`).
2. **Check the admin's role.** The inventory accepts delegated calls only; a service principal gets HTTP 403 ([inventory API](https://learn.microsoft.com/power-platform/admin/inventory-api)). Global administrator, Power Platform administrator, Dynamics 365 administrator and Global reader see everything; AI administrator and AI reader see "Agents, agentic apps, agent flows, environments, and environment groups only" [S6]. The run records Admin Role Used. Whether the two AI roles can call the inventory API is UNVERIFIED.
3. **Read live.** The product's MCP server queries Microsoft's inventory with the admin's delegated token. The inventory lists agents, apps (including Copilot managed runtime and App Builder apps), flows (including agent flows and Workflows agent flows) and Power Pages sites (preview) [S6].
4. **Compare.** Document 25's gap check comparison operation (OP-13) matches each item's Microsoft ID to O03 Technical Component External Id (and environments to O15) and runs the claim lifecycle: claim key, vanished, and dismissed stays dismissed (28, issue 12). Results:
   - **matched:** Last Seen On is updated; if Microsoft's listed owner differs from the register's owners, Owner Differs is set. Microsoft's owner is not stored;
   - **unregistered:** a new O23 Claim, finding kind unregistered, with Suggested Claimant as a hint only;
   - **vanished:** a registered component of a non-retired application that the run did not see becomes an O23 Claim, finding kind vanished (sweep proposal). Components of kinds the run cannot see (C16 Is Gap Check Covered = no) are never marked vanished.
5. **Record the run.** O22 Gap Check Run: who, role used, estates, client app, counts. Existing open claims for the same External Id are updated, not duplicated.
6. **Claim.** A candidate owner calls `claim_resource`:
   - **to an existing application:** the engine creates the O03 Technical Component (Source Claim set). If the claimant holds no owner role on that application, a confirmation card goes to its technical owner (P10);
   - **to a new application:** the claim starts an intake (P01) with the component pre-filled.
7. **Dismiss.** The platform admin dismisses an item with a Dismiss Reason (for example "not a business application", or a Cowork file that is not a runtime app, sweep proposal).
8. **Resolve vanished items.** The technical owner says what happened, with `link_component`: retired (outcome retired, which sets the component's Retired On; P09), moved (a migration created a new app ID: the new component gets Replaces Component through `replacesComponentId`, sweep proposal), or still there in an estate the run could not read (28, issue 11).
9. **Agents.** For agents, Microsoft already flags those without owners ("Agents without owners", "Assign new owner") [J5]. The product links to that evidence and records the business decision (AI lead, last RACI row). It never reassigns owners in Agent 365 (D-15, D-17).

**Records created or changed.** O22 Gap Check Run; O23 Claim (open, claimed, dismissed); O03 Technical Component (created or Last Seen On, Owner Differs, Replaces Component, Retired On); O02 (through P01 when new); O09 (ownership, and agent decisions).

**Approvals.** Ownership of a new application uses S1 (the named business owner accepts). Attaching a component to an application by a non-owner uses a confirmation card.

**Configuration points.** C06 list `estate`; C16 Component Kind (Source Type Hint, Is Gap Check Covered); C26 Reminder Rule for subject kind claim; C31 and C32 for ownership; the `tool` value list; the setting `claim-visibility` (default "admins and claimant"; 28, issue 10).

**MCP tools used (document 24 names).** `run_gap_check` (admin only), `list_claims`, `claim_resource`, `dismiss_claim`, `link_component` (outcome retired, or `replacesComponentId` for moved).

**Microsoft tools linked, never rebuilt.** The Power Platform inventory and its API [S6]; the Microsoft 365 admin center for Copilot managed runtime apps [J4] (whether the gap check can read it through an API is UNVERIFIED); the Agent 365 registry [J5]; Entra Agent ID; Power Platform Admin in Cowork for tenant questions (sweep proposal).

**Bindings.**

| Concern | Dataverse binding | Azure database binding |
|---|---|---|
| Live read | In the product's MCP server (Azure), with the admin's delegated token for the inventory API. A plug-in cannot do this | The same |
| Write results | Custom API `RecordGapCheckRun` with the inventory items as a JSON string; the engine's gap check comparison upserts O23 by claim key, not in the adapter | API endpoint, one transaction, same engine operation |
| Long runs | The MCP server returns a run ID and reports progress; work continues in an Azure queue | The same |

---

### P07 Documentation tracking

**Purpose.** Say which documents each application needs, track each one's link and status, and remind owners. The product never writes a document (D-17).

**Decisions it follows.** D-12, D-17.

**Trigger.**
- A documentation requirement starts to apply: a tier, kind or stage change (P02 step 7).
- A document passes its Stale On date (S2 daily refresh).
- An owner or their AI tool records a link, a status or a check.
- A gate needs a current document (C14 requirement kind "document current").

**Parties and roles (default configuration).**

| Step or decision | MK | TO | BO | PA | AR | AI |
|---|---|---|---|---|---|---|
| Set which documents each kind and tier needs (configuration, P11) | I | I | I | A | C | |
| Write or update the document, outside the product | R | A | | | | S |
| Record the link and status | R | A | | | | S |
| Confirm a gate-relevant document is current | | A | C | | | |

The Document Owner on each O19 row is a person; by default the technical owner.

**Steps.**

1. **Require.** OP-06 computes the missing documents from the C23 requirements in scope for the application's kind, tier and stage. Nothing is stored for a missing document. An O19 Document Link row exists only once a link has been recorded (28, issue 16).
2. **Ask.** The owner, or their AI client, asks "what documentation is missing or stale for my app?" (`get_documentation_status`).
3. **Write outside.** The owner's AI tool writes the page, for example Claude Code through the Azure DevOps MCP server's `wiki_upsert_page` or through git ([Azure DevOps MCP Server tools](https://learn.microsoft.com/azure/devops/mcp-server/remote-mcp-server)). The product is not involved.
4. **Record.** The AI tool calls `record_document_link` with the URL and status draft or current. The product never assumes the write succeeded (sweep proposal); it stores what it is told, with performed-by.
5. **Confirm when it matters.** For document types that a gate requires (C22 Current Needs Person = yes), "current" set by an AI client stays a claim until the document owner confirms it on a confirmation card (P10; working default, open design question 9, see 28-working-defaults.md). Other document types accept "current" from the AI client with performed-by recorded.
6. **Check.** When a person or their AI tool confirms the document is still true, `confirm_document_checked` sets Last Checked On. The product does not read the document itself.
7. **Go stale.** S2 moves current to stale at Stale On (Last Checked On plus C22 Freshness Days), reminds the document owner (C26 subject kind document) and escalates to the deputy.
8. **Gate.** A gate decision with a "document current" requirement cannot be accepted while the document is missing or stale.

**Records created or changed.** O19 Document Link (Url, Status, Document Owner, Last Updated On, Last Checked On, Stale On); O20 Evidence Link when a document is evidence for a decision; O24 for reminders.

**Approvals.** Only confirmation cards for gate-relevant documents (P10).

**Configuration points.** C22 Document Type (Default Link Kind, Freshness Days, Template Url; column Current Needs Person, section 5); C23 Documentation Requirement (tier, kind, From Stage); C14 (document current); C26 (document reminders); C37 Link Kind (document category); the setting `confirmation-channel`.

**MCP tools used (document 24 names).** `get_documentation_status`, `record_document_link` (also sets the status), `confirm_document_checked`.

**Microsoft tools linked, never rebuilt.** The Azure DevOps wiki and its MCP server, which the owner's tool uses to write; GitHub repositories; SharePoint pages. Microsoft retires cross-environment comment import and points to source-controlled documentation or a wiki instead (news sweep).

**Bindings.**

| Concern | Dataverse binding | Azure database binding |
|---|---|---|
| Missing documents | Computed by OP-06 when read (custom API `GetDocumentationStatus`); no stored row | Computed by the API rules library; no stored row |
| Stale refresh | `RefreshDueStates` from a scheduled flow (S2) | Timer-triggered function (S2) |

---

### P08 Owner leaves

**Purpose.** Make sure no application is left without an answerable owner: a deputy acts at once, a successor is named before access goes, open approvals move, and agents flagged by Microsoft get a business decision.

**Decisions it follows.** D-01, D-11, D-15, D-17, D-20.

**Trigger.**
- **Announced:** the owner or their manager records a planned leave or role change, with a date.
- **Entra signal:** an owner's account is disabled, deleted, or has a leave date. The product notices in two ways (28, Q18):
  - an **owner check** an admin runs on demand, like the gap check (`run_owner_check`): the product's MCP server reads the principals in O06 from Entra with the admin's delegated token. This is in the core;
  - an optional **Entra ID Governance leaver workflow** sample, for clients with ID Governance: a custom task extension calls an Azure Logic App, which calls the product's API. Lifecycle Workflows need Entra ID Governance licenses [P16].
  - Not chosen: an **unattended read** by the engine with an application permission, or a Microsoft Graph change notification subscription on users, which must be renewed in under 29 days [P17]. It needs an application permission over all users, against D-15's reasoning.
- **Agent signal:** Agent 365 shows agents without owners [J5]; Entra Agent ID lifecycle policies reconfirm or disable agent identities (sweep proposal; [agent lifecycle policies](https://learn.microsoft.com/entra/id-governance/agent-lifecycle-policies)).
- **Review:** the owner check review type comes due (C24, C25).

**Parties and roles (default configuration).**

| Step or decision | Leaving owner | Deputy | Successor | BO | SP | PA | PMO | AIL | AI |
|---|---|---|---|---|---|---|---|---|---|
| Record the leave or the signal | R | I | | I | | A/R | | | S |
| Name the successor and hand over | C | R | C (accepts) | A | | R | I | | S |
| Hand over when the business owner leaves | C | R | C (accepts) | | A | R | I | | S |
| Move open approvals and reviews | | I | I | I | | A/R | | | |
| Decide on a flagged agent | | | | C | | R | | A | S |

When the business owner is the one leaving, the default decision right passes the A to the sponsor, then to the portfolio's Accountable Principal (O01). These are configuration rows.

**Steps.**

1. **Detect.** The leave arrives by one of the triggers. An announced leave is recorded with `record_owner_leave`. The engine records it on O05 Principal Reference (columns Directory State and Leaves On, section 5; 28, issue 15) and opens O21 Review rows of type owner check, due now, for every application where that principal holds an owner role.
2. **Deputy acts.** Until a successor is accepted, reminders, cards and escalations for that owner role go to the deputy (O06 Is Deputy). Each owner role with Requires Deputy needs one (glossary "Deputy").
3. **Move open approvals.** The engine finds the leaver's pending O10 rows (I-230). It cancels each Power Automate approval and sends a new card to the deputy or another holder of the required role. The cancelled record stays, outcome cancelled.
4. **Name a successor.** The engine proposes an ownership decision: the deputy becomes the holder, or a named successor does. The successor must accept on a card. A new deputy is then required.
5. **End the old assignment.** On acceptance O06 End On is set on the leaver's rows and new rows start. The leaver's name stays on past decisions, held only as an ID plus a display label (O05; [S21]).
6. **Access is Entra's job.** Removing the leaver's access happens in Entra and the deployment tools, not in the product (D-11, D-17). An urgent revoke disables the user, not only the group membership, because group team memberships are cached for up to 8 hours [S7].
7. **Agents.** For an agent owned by the leaver, or flagged as ownerless by Microsoft, the AI lead records keep, reassign, consolidate or retire, with the reason and a link to Microsoft's evidence (sweep proposal wording of D-20). Reassigning the owner in Agent 365 or the sponsor in Entra happens there. Rule-based reassignment needs Agent 365 licenses; manual reassignment does not.

**Records created or changed.** O05 Principal Reference (Directory State, Leaves On, Display Label cleared on erasure); O06 Owner Assignment (End On, new rows); O09 Decision (ownership; agent decisions); O10 Approval Record (cancelled and new); O21 Review (owner check).

**Approvals.** Ownership decisions: the successor accepts, the accountable (business owner, or sponsor) approves. Both through S1.

**Configuration points.** C28 Owner Role (Requires Deputy, Maximum Holders); C31 and C32 for decision type ownership, including the fallback when the business owner leaves; C24 and C25 (owner check cadence); C26 (escalation to deputy); the `estate` list entry for the owner check (`entra-owners`, section 5).

**MCP tools used (document 24 names).** `record_owner_leave`, `run_owner_check` (admin only, delegated), `propose_owner_change` (names the successor), `get_portfolio_summary` (owner-gap list: "which apps have no current owner or no deputy"), `get_application_owners` (gaps for one application).

**Microsoft tools linked, never rebuilt.** Entra users and groups; Entra ID Governance Lifecycle Workflows [P16]; Agent 365 registry ownerless views [J5]; Entra Agent ID sponsors and lifecycle policies (sweep proposal). Reading a user's leave date through Graph needs `User-LifeCycleInfo.Read.All` and, delegated, the Lifecycle Workflows Administrator or Global Reader role; Learn names `User.EnableDisableAccount.All` plus `User.Read.All` as the least privileged combination "to read and write" `accountEnabled` [P18]. Whether `User.Read.All` alone reads `accountEnabled` is UNVERIFIED.

**Bindings.**

| Concern | Dataverse binding | Azure database binding |
|---|---|---|
| Owner check | In the MCP server, delegated, like the gap check | The same |
| Leaver workflow sample | Logic App calls the custom API `RecordOwnerLeave` as a workload identity with a Workload Grant (C42) | Logic App calls the API endpoint with the same name |
| Moving approvals | Plug-in marks records; the runner flow cancels and resends | API marks records; the runner flow cancels and resends |

---

### P09 Retirement

**Purpose.** Take an application out of use on purpose: a recorded decision, an impact check, a scheduled removal done in Microsoft's tools, linked evidence that nothing is left, and a final stage.

**Decisions it follows.** D-01, D-15, D-17, D-19.

**Trigger.**
- A portfolio decision with outcome retire (review, stage 9).
- An owner asks to retire something nobody uses (Microsoft's Actions can show unused resources; link).
- A vanished claim resolved as "retired" (P06 step 8).
- A replacing application went live (dependency type "replaces", sweep proposal).

**Parties and roles (default configuration).**

| Step or decision | MK | BO | DO | PA | SR | AR | CM | PMO | AI |
|---|---|---|---|---|---|---|---|---|---|
| Decide the future: keep, invest, replace, retire | I | A | C | C | | C | I | R | |
| Approve the removal change | (per P03) | | | | | | | | |
| Retire and clean up, with evidence | C | A | C | R | C | C | I | I | S |
| Declare retirement complete | | A | | R | | | I | I | |

**Steps.**

1. **Decide.** A portfolio decision (outcome retire) is accepted through S1. The PMO prepares the facts; the business owner decides.
2. **Check impact.** `get_dependency_impact` walks dependencies in both directions. Owners of dependants are informed; a dependant that needs a replacement gets its own change or intake.
3. **Settle the data.** The data owner decides retention: a data-use decision of the retirement kind, with evidence links to where the data went.
4. **Schedule the removal.** A change request (P03, normal by default) carries the window and the targets. The authorization follows the tier.
5. **Remove, in Microsoft's tools.** People and their AI tools disable before they delete: quarantine and delete in the Power Platform admin center, Block and Delete in the Microsoft 365 admin center for runtime apps, identities and grants removed in Entra, agents handled in Agent 365. The product does none of this (D-17). For agents, Microsoft's guidance says retirement "should disable agent execution and revoke associated permissions", stored information "should follow applicable retention requirements", and "Agent owners should confirm that retirement is complete" [P21]. Each step done is linked as an O20 Evidence Link with an evidence level (I-44), through `save_link` (link category evidence). A full checklist (L02, L03) comes later.
6. **Confirm nothing is left.** The admin runs the gap check (P06). The application's components should now be "vanished"; each is resolved as retired with `link_component` (outcome retired). A component still seen keeps the retirement open.
7. **Complete.** A retirement decision (outcome complete) is accepted. The engine moves the application to the stage whose meaning is retired (C12), cancels due reviews, stops document reminders, and sets registration status archived when configured. Dependencies stay, marked by the retired stage, so history still reads.

**Records created or changed.** O09 Decision (portfolio, data use, retirement); O11 Change Request and O14 Deployment Records for the removal; O20 Evidence Links; O23 Claims (vanished, resolved); O02 (stage, status); O21 (cancelled).

**Approvals.** Portfolio and retirement decisions: business owner. The removal change: per P03.

**Configuration points.** C18 and C19 (portfolio outcomes; retirement outcomes with Next Lifecycle Stage); C12 (the retired stage); C14 (requirements before retirement completes, for example "data use decided" and "gap check shows no component"); C17 (Counts For Impact); C31 and C32.

**MCP tools used (document 24 names).** `save_decision_proposal` (decision type portfolio or retirement; proposed only), `get_dependency_impact`, `save_change_request_draft`, `save_link` (link category evidence), `run_gap_check`, `link_component` (outcome retired). Retirement status has no single tool: `get_application_card` plus `get_requirements` answer it.

**Microsoft tools linked, never rebuilt.** Power Platform admin center quarantine and delete; Microsoft 365 admin center Block and Delete for runtime apps [J4]; Entra lifecycle workflows [P16]; Agent 365 [J5]; Microsoft's [safe decommissioning guidance](https://learn.microsoft.com/azure/well-architected/operational-excellence/safe-deployments#plan-and-execute-safe-decommissioning).

**Bindings.**

| Concern | Dataverse binding | Azure database binding |
|---|---|---|
| Stage move and clean-up of due items | Plug-in on the retirement decision's acceptance | API rules library |
| Confirmation | The gap check in the MCP server (P06) | The same |

---

### P10 AI draft and human accept

**Purpose.** Let AI clients do most of the typing while a person decides everything that matters, and make the data show which is which (D-02, D-10).

**Decisions it follows.** D-02, D-09, D-10, D-16.

**Trigger.** Any write whose actor kind is "AI client for a person" or "agent identity" (S3 step 2).

**Parties and roles.** The person the AI acts for is R and A for accepting their own drafts. The record's accountable role (C31) is A for decisions. The AI client is S only.

**What AI may and may not do.**

| AI may | AI may not |
|---|---|
| Read register facts as the person it acts for | Approve, reject or authorize anything |
| Draft intakes, decisions, change requests, release notes, review packs | Hold an accountability, or be the only reviewer of its own work (I-113) |
| Propose a tier, a change type, a duplicate match, a standard change | Set the tier, or decide that a change is standard |
| Record links, deployments and document status for the person | Change configuration without an admin's instruction and acceptance (P11) |
| Run read-only checks, including the gap check as the admin | Act for a person without that being recorded |

**Steps.**

1. **Draft.** The AI client writes through a tool. The engine sets Is AI Created = yes and stamps actor kind, On Behalf Of and Client App.
2. **Downgrade.** Operational records start in draft (record status meaning), decisions in proposed, applications in registration status draft (glossary 2.4). The AI cannot write a later status in the same call.
3. **Accept.** What counts as a person's acceptance depends on what is accepted:

| What is accepted | Default channel for acceptance | Why |
|---|---|---|
| A decision (any decision type) | An approval card only (S1). The engine accepts only the locked list with no card (working default, open design question 8, see 28-working-defaults.md) | Only the approval runner writes outcomes; this is "AI never approves" |
| Registration of an application the AI created (draft to registered) | A confirmation card to the named business owner | An application must not enter the register on an AI's word (glossary 2.4) |
| A gate-relevant document set to current | A confirmation card to the document owner | Gates read it (P07) |
| Submission of an intake, a change request or a decision | The same MCP call (`submit_record`), on the person's behalf, where Tool Policy (C41) lets an AI client submit that subject kind. Shipped: yes for intake, change and decision submissions | A submission is a request, not a decision; the decision that follows needs a card |
| Links, deployment records, checks | No separate acceptance | They record facts the person reports; performed-by shows the AI |

   The table is the default of the `confirmation-channel` setting, set per subject kind (section 5; working default, open design question 9, see 28-working-defaults.md).
4. **Recommendations.** An AI stage in a client's own approval flow, such as Copilot Studio multistage approvals (preview, agent flows only), is recorded as outcome "recommendation only" and never counts.
5. **Show it.** Every page and every tool answer marks AI-created rows and shows "performed by AI client X for person Y".

**Why an MCP call is never a human confirmation.** The product's MCP server acts with the person's delegated token, so the engine cannot prove that a person, not the AI, pressed the button. Copilot Studio clients may even run with maker-provided credentials, which are on by default. An approval or confirmation card answered in Outlook or Teams, and written back only by the approval runner, is the one channel the engine can trust.

**Records created or changed.** Any operational row (Is AI Created, performed-by columns); O10 Approval Records for confirmation cards; O24 Audit Entries. On Dataverse, auditing also records actions such as "Create - AI assisted" [S16].

**Configuration points.** C04 settings: "AI approval lock" (on and locked; "AI never approves" in the product is decided, D-02 and D-10) and `confirmation-channel` (section 5); C41 Tool Policy (AI May Submit per subject kind); C38 Actor Kind; C05 Status rows mapped to draft and proposed meanings. Whether AI approvals from client tools, such as a pull request approved only by GitHub Copilot, ever count is P05's concern (working default, open design question 7, see 28-working-defaults.md).

**MCP tools used.** All write tools. No tool accepts a decision or confirms a draft.

**Microsoft tools linked, never rebuilt.** Power Automate approvals [P4]; Dataverse auditing [S16]; Purview audit for Teams approvals [P9].

**Bindings.**

| Concern | Dataverse binding | Azure database binding |
|---|---|---|
| Downgrade and stamp | Plug-in (S3) | API rules library (S3) |
| Telling the product UI apart from an MCP client | Not reliable in a plug-in (UNVERIFIED) | The token's calling application ID; still not proof of a person, so cards stay the default |
| Confirmation cards | S1 runner, custom responses "Confirm" and "Discard" | S1 runner |

---

### P11 Configuration change

**Purpose.** Change any rule without code, rebuild or redeploy: the admin decides, an AI performs the change, the engine validates and versions it, and the record shows both (D-06, D-07, D-10).

**Decisions it follows.** D-03, D-06, D-07, D-10, D-11, D-20.

**Trigger.**
- An admin instructs a change in plain words, in Claude Code or another MCP client.
- A client applies or switches a preset (C03), including the Microsoft CoE baseline preset (sweep proposal). A client picks one full preset plus declared overlays (small team, strict separation), and the engine validates the result as one set (28, Q3).
- A product upgrade ships new seed rows.

**Parties and roles (default configuration).**

| Step or decision | CA | PA | SR | CM | AIL | AR | Auditor | AI |
|---|---|---|---|---|---|---|---|---|
| Instruct and accept a configuration change | A/R | C | | | | | I | S (performs) |
| Change tier rules and tier questions | A/R | C | C | C | | C | I | S |
| Change a gate policy row (decision type gate policy change) | A/R | C | C | C | | | I | S |
| Change AI rules (autonomy levels, AI lead duties) | A/R | C | C | | C | | I | S |
| Change roles and their Entra group bindings | A/R | C | C | | | | I | S |

There is one accountable per configuration decision type: the configuration admin (document 21, V22; 28, issue 6). The roles for each area are consulted, not accountable: the change manager for gate policy, the AI lead for AI rules, the platform admin for roles and bindings, and the security reviewer for tier rules. Who owns the gate policy at a client is still one of D-03's open points; the row above is a default only.

**Steps.**

1. **Instruct.** The admin says what should change, for example "add a fourth tier between 2 and 3" or "require a runbook for every agent".
2. **Prepare.** The AI client reads the current configuration (`get_configuration`, by keys) and prepares the change as a patch of `upsert` and `retire` operations, or a configuration file in the same format, using keys, never internal IDs (glossary 3.2; document 21, section 8.2).
3. **Propose.** The AI client calls `propose_configuration_change` as the admin (with `presetKey` to apply a preset). The engine checks that the caller holds a role with Can Change Configuration (C29).
4. **Validate.** OP-09 runs the full validator set, V01 to V44 (document 21, section 9; 28, issue 12). It refuses a change that breaks the engine contract or the configuration's own integrity. Examples: a key that does not exist; a decision right row with no accountable or two; an approval rule whose approver is not a person role; a Minimum Count of 0 (V28); a change to a locked setting (Is Locked); a status mapped to no engine meaning; a gate requirement that points to a retired row; a sequential approval rule that repeats an approver [P8].
5. **Show the difference, then submit.** The engine stores the patch on a Configuration Draft (C46, both bindings), with its base version and Change Summary. No C02 row exists yet (document 21; 28, issue 3). `propose_configuration_change` with `validateOnly` returns the diff and the effect on live applications (for example "12 applications move to the new tier's review cadence") (28, issue 11). When the draft validates, the AI client submits it with `submit_record` (subject kind configuration draft; document 24, tool 18). The engine freezes the draft and opens a decision of type configuration change (or gate policy change), status proposed.
6. **Decide.** S1 sends one card showing the diff to the configuration admin role. The instructing admin may accept their own AI's draft; the strict separation overlay needs a second configuration admin (C40) (working default, open design question 4, see 28-working-defaults.md). The consulted roles for the area (table above) get consultation cards, which never count. The AI cannot answer any card.
7. **Activate.** On acceptance the engine activates the draft inside one activation operation: it rebases if needed, revalidates, writes the changed configuration rows, and creates C02 version n+1 (append-only) with Content Json, Content Hash, Published On, Instructed By and Change Decision. Live configuration rows change only here; no person, role or tool writes configuration tables directly (28, issue 3). Plug-in caches and API caches reload when they see a higher version number.
8. **Stamp.** New operational rows stamp version n+1. Old rows keep the version they were made under, so a record can prove which rules applied (glossary C02).
9. **Withdraw or undo.** An unaccepted draft can be withdrawn with `withdraw_record` (configuration subject kind); the engine marks the draft discarded and, if it was submitted, writes the cancelled outcome on its decision, and the runner cancels the card. An undo of an active version is a new change that restores an earlier version's content and supersedes the decision. Nothing is edited in place.
10. **Upgrade.** A product upgrade proposes a Configuration Draft that adds missing shipped rows and updates shipped rows the client has not changed. A configuration admin accepts it on a card. Local rows are never overwritten, only reported, so a client's local changes are never lost (Origin; 28, Q5).
11. **Check health.** `get_configuration_health` returns the version history and the engine information (engine version, snapshot schema version, snapshot hash) (28, issue 11).

**Records created or changed.** C46 Configuration Draft (the patch); C01 to C40 configuration rows, written only inside activation; C02 Configuration Set Version, created at activation; O09 Decision (configuration change, gate policy change); O10 Approval Record; O24 Audit Entry (operation "configuration activated").

**Approvals.** As in step 6.

**Configuration points.** C01 Configuration Set (Set Kind, Based On Preset, Is In Force); C02; C03 Preset; C04 Setting (Is Locked); C29 Role (Can Change Configuration); C31 and C32 for the two decision types (one accountable: the configuration admin); C40 Separation Rule (a second configuration admin under strict separation).

**MCP tools used (document 24 names).** `get_configuration`, `propose_configuration_change` (with `presetKey` for a preset and `validateOnly` for the diff and its effect on live applications), `submit_record` (configuration draft subject kind), `withdraw_record` (configuration subject kind), `get_configuration_health`, `explain_rules`.

**Microsoft tools linked, never rebuilt.** None directly. Presets name which Microsoft controls to set (for example the Work IQ action ceiling or the maker credentials sharing control) instead of rebuilding them (sweep proposal).

**Bindings.**

| Concern | Dataverse binding | Azure database binding |
|---|---|---|
| Live configuration | Rows in configuration tables, JSON in text columns; a shipped web resource is only the seed (D-10). Written only by the engine's activation custom API; a guard plug-in refuses any other write | Rows in a `config` schema with native `json` columns [S20]; optional temporal history [S19]. Only the API's managed identity writes `config`, and activation is one transaction |
| Drafts | A Configuration Draft table holds the patch until activation | A Configuration Draft table in the same database |
| Validation | Custom API `ProposeConfigurationChange` with a plug-in | API rules library |
| Cache reload | Plug-ins compare the cached version number with C01 Current Version Number | The API does the same |

---

## 5. New terms

Terms this document needs that the glossary does not hold. Each is a proposal for the glossary owner; the glossary itself is not edited here.

| Term | Definition | Kind | Used in |
|---|---|---|---|
| Approval runner | The small set of solution-aware cloud flows that send approval and confirmation cards, wait, write outcomes back and send notifications. Its actor kind is approval runner. It writes outcomes through the approval writer workload role. One flow group with one Process license [P1][P2]. Recorded in document 20 (28, issue 7) | Component | S1, S2, all |
| Approval writer | The workload role the approval runner holds to write approval outcomes, held through a Workload Grant (C42), never by a person. Column security (Dataverse) or the API's role check (Azure) lets only it write O10 outcome columns. Recorded in document 20 (28, issue 7) | Workload role (C29) | S1, S3 |
| Configuration Draft | The patch an AI client builds for a configuration change, with its base version and Change Summary, stored on its own entity on both bindings. C02 is created only when the draft is activated (document 21; 28, issue 3) | Entity C46 (document 20, section 2.10) | P11 |
| Confirmation card | A Power Automate approval with custom responses ("Confirm", "Discard") that asks a person to accept an AI draft that is not a decision. Stored as an O10 Approval Record against the drafted row | Use of O10 | P06, P07, P10 |
| Human confirmation | A write that shows a person acted: by default an approval or confirmation card outcome written by the approval runner. Never an MCP call | Rule | P10 |
| Confirmation channel (setting) | C04 Setting `confirmation-channel`, listed in document 21's settings table (28, issue 15). For each subject kind of AI draft, which channel counts as a person's acceptance (table in P10; working default, open design question 9, see 28-working-defaults.md) | C04 row | P07, P10 |
| Pre-authorized decision | A decision the engine accepts by itself, with actor kind engine. Allowed only for decision types on a locked list (shipped: lowest-tier intake and standard changes). It links the decision that set the rule, such as the C21 model's Approved By Decision, whose decider answers for it. Every other decision type goes on a card to its accountable holder (28, issue 1; working default, open design question 8, see 28-working-defaults.md) | Rule | S1, P01, P03; Question 5 |
| Consultation | The C32 Approval Rule column Is Consultation (yes or no), now in documents 20 and 22 (28, issue 15). When yes, the card asks a consulted role for input. OP-03 stores the outcome as "recommendation only", and it never counts (28, issue 12) | C32 column | S1, P01, P02, P11 |
| Security reviewer | Default C29 Role row in the one seed list (28, issue 7): reviews security for the highest default tier, is consulted on tier rules, is accountable for agent autonomy levels, must agree on restricted data reaching an AI model | C29 row | P02, P03, P04, P11 |
| Security review | Proposed default C24 Review Type row, added by tier answers through Triggers Json | Proposed C24 row | P02, P04 |
| Responsible AI review | Proposed default C24 Review Type row for agents that execute actions. Microsoft asks for "A security review and a responsible AI assessment before release" for tier 3 agents [S2] | Proposed C24 row | P02, P04 |
| Unlinked deployment change type | C04 Setting `unlinked-deployment-change-type`, listed in document 21's settings table (28, issue 15): the change type key the engine uses to pick a C33 Gate Policy row for a deployment with no change request. Default `normal` | C04 row | P05 |
| Owner check | An on-demand run, like the gap check, in which an admin's delegated read of Entra finds owners who are disabled, deleted or leaving. Run with `run_owner_check`, and recorded as an O22 Gap Check Run with estate key `entra-owners` (a new `estate` list value). In the core; an Entra Lifecycle Workflows sample is optional (28, Q18) | Process step | P08 |
| Directory State | O05 Principal Reference column, now in documents 20 and 22 (28, issue 15): enabled, disabled, deleted or unknown, as last read from Entra, with the date | O05 column | P08 |
| Leaves On | O05 Principal Reference column, now in documents 20 and 22 (28, issue 15): the planned leave date when an owner's leave is recorded or read from Entra | O05 column | P08 |
| Current Needs Person | C22 Document Type column, now in documents 20 and 22 (28, issue 15): yes when "current" set by an AI client needs a confirmation card | C22 column | P07 |
| Due-state refresh | The daily engine run that moves documents to stale, creates due reviews, expires approvals and lists old claims | Process step | S2 |

The earlier "Emergency approver" proposal is withdrawn. The change manager authorizes emergencies (28, issue 4).

### 5.1 Candidate tool names mapped to document 24 (added in the step 2 review, 2026-11-22; updated in the alignment pass)

Document 24 owns the tool names. Section 4 now uses them. This table keeps the candidate names an earlier draft of this file used, so older notes can still be traced. The process steps that had no tool (28, issue 11) are covered by the tools 28 adds or extends, marked "New" or "Extended". Only retirement status stays partial.

| Candidate name here | Document 24 tool | Note |
|---|---|---|
| `draft_intake_request` | `save_intake_draft` | |
| `get_tier_questions` | `get_tier_questions` | |
| `find_similar_applications` | `check_duplicates` | |
| `submit_intake_request`, `submit_change_request` | `submit_record` | Subject kind intake request or change request |
| `get_my_queue`, `get_review_queue` | `get_my_queue` | Review queue is kind `review` |
| `add_delivery_link`, `add_evidence_link` | `save_link` | Link category delivery or evidence |
| `assess_tier` | `answer_tier_questions` | |
| `explain_tier` | `answer_tier_questions` (driving answers), `explain_rules` (rule trace) and `get_requirements` | `explain_rules` is new. The tier's vocabulary mappings (C11) come from `get_configuration` |
| `propose_tier`, `record_decision`, `propose_portfolio_decision` | `save_decision_proposal` | With the decision type key |
| `get_tier_requirements` | `get_requirements` | |
| `start_change_request`, `link_external_change` | `save_change_request_draft` | External change number and URL are inputs |
| `check_standard_change` | `check_standard_change` | |
| `get_impact` | `get_dependency_impact` | |
| `get_change_schedule` | `get_change_schedule` | |
| `close_change_request` | `record_change_result` | Records the result as reported; moving the status to completed is a person's act (document 25, G-08) |
| `request_review` | `request_architecture_review` | New: asks for an ad hoc architecture review (O21) |
| `record_document_link`, `set_document_status` | `record_document_link` | |
| `confirm_document_checked` | `confirm_document_checked` | |
| `get_documentation_gaps` | `get_documentation_status` | |
| `record_deployment` | `record_deployment` | Extended: reported code approvals as an input |
| `get_what_runs_where`, `get_flagged_deployments` | `get_deployments` | `flaggedOnly` for flags |
| `link_deployment_to_change` | `record_deployment_correction` | New: O14 is final at insert (document 22), so the tool links a deployment to a change by adding a correcting record |
| `run_gap_check` | `run_gap_check` | |
| `get_claim_list` | `list_claims` | |
| `claim_item` | `claim_resource` | |
| `dismiss_claim` | `dismiss_claim` | |
| `resolve_vanished` | `link_component` | Extended: outcome retired sets Retired On; `replacesComponentId` covers "moved" |
| `record_leave`, `run_owner_check` | `record_owner_leave`, `run_owner_check` | New (28, Q18) |
| `propose_successor` | `propose_owner_change` | |
| `get_owner_gaps` | `get_portfolio_summary` (owner-gap list), `get_application_owners` (gaps), `get_my_queue` (kind `owner-gap`) | Extended: `get_portfolio_summary` now lists the gaps organization-wide, not only counts |
| `get_retirement_status` | `get_application_card` plus `get_requirements` | Partial: no single tool. Not in 28's tool list |
| `get_configuration` | `get_configuration` | |
| `get_configuration_history` | `get_configuration_health` | New: version history and engine information |
| `propose_configuration_change`, `apply_preset` | `propose_configuration_change` | `presetKey` input for a preset |
| `preview_configuration_change` | `propose_configuration_change` with `validateOnly` | Extended: the diff and the effect on live applications |
| (none) | `withdraw_record` | Extended: configuration subject kind, to withdraw a Configuration Draft (P11 step 9) |
| (none) | `explain_rules` | New: returns the engine's rule trace (S1 step 1, P02, P11) |

---

## 6. Microsoft sources

"Checked 2026-11-22" means read on Microsoft Learn through the Microsoft Learn MCP tools for this document on 2026-11-22.

| ID | Fact used | URL | Checked |
|---|---|---|---|
| P1 | "Users who respond to approval requests don't need a Premium license." "Automated and scheduled flows use the flow owner's license context. Instant flows (button, Power Apps) use the invoking user's license context." A premium flow "invoked by multiple users" needs everyone licensed or a Process license. A flow owned by a service principal needs a Process license or a designated licensed user. Multiplexing "is a license violation"; a user who "indirectly triggers the flow" and "gets value from the flow" must be licensed. A flow group lets up to 25 solution-aware flows share one Process license. Flows that support a Power Apps app and share its data sources run in the app's context | https://learn.microsoft.com/power-platform/admin/power-automate-licensing/faqs | 2026-11-22 |
| P2 | A Process license "lets it use premium and custom connectors, no matter what license the owning or triggering user has." "Flows must be in a solution for a Process license to be assigned" | https://learn.microsoft.com/power-platform/admin/power-automate-licensing/types | 2026-11-22 |
| P3 | The "When an HTTP request is received" trigger can be restricted to "Any user in my tenant" or "Specific users in my tenant", which accepts "object IDs of service principal users"; "Anyone" is a legacy open setting. Required claims include `aud` `https://service.flow.microsoft.com/` for the public cloud | https://learn.microsoft.com/power-automate/oauth-authentication | 2026-11-22 |
| P4 | "When you create approval flows, they're saved in Dataverse." The first approval flow in a non-default environment provisions a database. "The approvals connector is a standard connector." Assignees outside the environment get the Approvals User role automatically, except when the Entra setting AllowAdHocSubscriptions is disabled, when a security group controls access to the Dataverse environment, or on US Government plans | https://learn.microsoft.com/power-automate/get-started-approvals | 2026-11-22 |
| P5 | "An approval flow can wait for 28 days. If the wait time exceeds 28 days, that flow fails"; the approval stays in the action center. A Teams card does not update when the approver answers by email or in the action center. Guest approvers need a Power Automate license | https://learn.microsoft.com/power-automate/approvals-known-issues | 2026-11-22 |
| P6 | "If your flow might run for more than 30 days, store your approvals in Microsoft Dataverse" and use two flows, one to send and one to act on responses. Cancelling is supported on "Create an approval (v2)" | https://learn.microsoft.com/power-automate/modern-approvals | 2026-11-22 |
| P7 | An approver can reassign an approval request; "if you are the requester, you cannot reassign" | https://learn.microsoft.com/power-automate/approvals-howto | 2026-11-22 |
| P8 | Sequential approvals: "You can't assign the same approver to different steps of your approval" | https://learn.microsoft.com/power-automate/set-up-sequential-approvals | 2026-11-22 |
| P9 | Approvals created from the Teams Approvals app are stored in the default Dataverse environment; approvers are added to the Approvals User role on first assignment; the Approvals app logs audit events in the Microsoft Purview portal | https://learn.microsoft.com/microsoftteams/approval-admin | 2026-11-22 |
| P10 | The Approvals app API in Microsoft Graph is beta: "Use of these APIs in production applications is not supported." Creating an `approvalItem` takes `ApprovalSolution.ReadWrite` (delegated); "Application: Not supported." Creating an `approvalItemResponse` takes `ApprovalSolution.ReadWrite` or `ApprovalSolutionResponse.ReadWrite` (delegated only) | https://learn.microsoft.com/graph/approvals-app-api ; https://learn.microsoft.com/graph/api/approvalsolution-post-approvalitems?view=graph-rest-beta ; https://learn.microsoft.com/graph/api/approvalitem-post-responses?view=graph-rest-beta | 2026-11-22 |
| P11 | A custom API can be cataloged as a business event and used as a trigger through the Dataverse connector's "When an action is performed" trigger. Catalog and CatalogAssignment define what is exposed; roles other than Environment Maker need Read on Custom API, Process and SDK Message to see the catalog | https://learn.microsoft.com/power-apps/developer/data-platform/business-events ; https://learn.microsoft.com/power-apps/developer/data-platform/catalog-catalogassignment ; https://learn.microsoft.com/power-automate/dataverse/action-trigger | 2026-11-22 |
| P12 | Pipelines gated extensions (pre-export step, delegated deployment, pre-deployment step) and triggers including `OnDeploymentCompleted`, through "When an action is performed". "You can't extend personal pipelines." "The same managed artifact, per version, deploys to all subsequent stages" | https://learn.microsoft.com/power-platform/alm/extend-pipelines | 2026-11-22 |
| P13 | Azure Pipelines checks run in five categories, including Invoke REST API and Business Hours; checks are not defined in YAML; an administrator of the resource can bypass a check, and the bypass shows in the checks panel | https://learn.microsoft.com/azure/devops/pipelines/process/approvals | 2026-11-22 |
| P14 | Durable Functions human interaction pattern: a durable timer races an external event; external events have "an at-least-once delivery guarantee", so deduplicate | https://learn.microsoft.com/azure/durable-task/common/durable-task-human-interaction ; https://learn.microsoft.com/azure/durable-task/common/durable-task-external-events | 2026-11-22 |
| P15 | Logic Apps approval workflows use the Office 365 Outlook "Send approval email" action | https://learn.microsoft.com/azure/logic-apps/tutorial-process-mailing-list-subscriptions-workflow | 2026-11-22 |
| P16 | Lifecycle Workflows custom task extensions call Azure Logic Apps (consumption); "Using this feature requires Microsoft Entra ID Governance licenses" | https://learn.microsoft.com/entra/id-governance/lifecycle-workflow-extensibility ; https://learn.microsoft.com/graph/api/resources/identitygovernance-lifecycleworkflows-overview?view=graph-rest-1.0 | 2026-11-22 |
| P17 | Graph change notification subscriptions on user, group and other directory resources last at most 41,760 minutes (under 29 days) | https://learn.microsoft.com/graph/change-notifications-overview | 2026-11-22 |
| P18 | Reading `employeeLeaveDateTime` needs `User-LifeCycleInfo.Read.All` and, delegated, the Lifecycle Workflows Administrator or Global Reader role. "User.EnableDisableAccount.All + User.Read.All is the least privileged combination of permissions to read and write the accountEnabled property" | https://learn.microsoft.com/graph/api/user-get?view=graph-rest-1.0 | 2026-11-22 |
| P19 | "To access premium, on-premises, and custom connectors, you need a standalone Power Apps or Power Automate license" | https://learn.microsoft.com/troubleshoot/power-platform/power-automate/flow-run-issues/triggers-troubleshoot | 2026-11-22 |
| P20 | "Postmortems should follow all failed deployments without exception." Emergency protocols: "define who can approve safe deployment acceleration in an emergency" | https://learn.microsoft.com/power-platform/well-architected/operational-excellence/mitigation-strategy ; https://learn.microsoft.com/power-platform/well-architected/operational-excellence/safe-deployments | 2026-11-22 |
| P21 | "Changes that can materially affect agent behavior should trigger re-evaluation before production deployment. Model replacements and instruction updates can change agent outputs." Retirement "should disable agent execution and revoke associated permissions"; "Agent owners should confirm that retirement is complete" | https://learn.microsoft.com/azure/cloud-adoption-framework/ai-agents/operational-standards | 2026-11-22 |

Glossary sources used: S1, S2, S3, S4, S5, S6, S7, S8, S9, S10, S13, S15, S16, S18, S19, S20, S21, J1, J2, J4, J5 (glossary section 4). Facts linked inline to a Microsoft Learn page were checked during the design research of 2026-11-21 and 2026-11-22 and are not re-checked here.

**UNVERIFIED in this document.**

1. Whether the "When an HTTP request is received" trigger is a premium feature (not stated on the pages read).
2. Whether the product's API starting approval flows for many requesters counts as a flow "invoked by multiple users" (multiplexing classification).
3. Whether the Approvals connector is available in Logic Apps.
4. Whether flow approvals can be answered through the Graph Approvals app API, and whether agent accounts can complete an approval card.
5. Whether a Dataverse group team can carry a service principal (workload role binding). The design no longer depends on it, because workload grants are direct (28, issue 8).
6. Whether a plug-in can tell a call from an MCP client apart from a call from the app.
7. Whether the AI administrator and AI reader roles can call the inventory API.
8. Whether the gap check can read the Microsoft 365 admin center's runtime app list through an API.
9. Whether `User.Read.All` alone reads `accountEnabled`.
10. Whether the Dataverse SQL tool behind `read_query` lacks recursion.

---

## 7. Diagram specs

All diagrams use the product's diagram palette (D-04): navy `0A2A4A` for text and lines, blue `0071DC` for lane and group header bars, gold `FFC221` as the accent. Fills are light only: white `FFFFFF`, light blue `E8F1FB`, light gold `FFF4D6`, light gray `F3F5F8`. No dark fills. The diagrams are drawn in Visio from these specs. Decisions are gold-outlined diamonds; AI drafts are dotted navy edges; "outside the product" shapes have a light gray fill.

### D23-1 The value stream with swimlanes

- **Title:** From idea to retirement: who does what, and where the gates are.
- **Purpose:** One page that shows stages 0 to 10, the five gates and the process that runs each stage.
- **Lanes (top to bottom):** Business (business owner, sponsor, data owner); Builders (makers, fusion and pro teams); PMO; Architects; IT (platform admin, security reviewer); Change control (change manager, change board); AI clients (dotted lane, light gray).
- **Nodes:** 0 Idea (Business); 1 Register and triage (Builders, IT); 2 Score and fund (PMO, Business); 3 Design review and data use (Architects, Business); 4 Confirm tier (IT); 5 Build (Builders); 6 Accept after test, security review (Business, IT); 7 Change request and authorization (Change control, Business); 7 Deploy in the client's tool (IT, light gray "outside"); 8 Operate (IT); 9 Review and portfolio decision (PMO, Business); 10 Retire (IT, Business). Gates as gold diamonds: Accept after 1, Commit after 2, Design after 4, Go-live after 7, Value at 9. AI lane node: "Draft, check, summarize. A person decides."
- **Edges:** 0 to 1 "ask"; 1 to 2 "needs money or central capacity"; 1 to 4 "lowest tier self-build"; 2 to 3 "funded"; 3 to 4 "design agreed"; 4 to 5 "Design gate"; 5 to 6 "built"; 6 to 7 "accepted"; 7 to 8 "Go-live gate"; 8 to 9 "review due"; 9 to 1 "change" (loop label "stages 5 to 7 repeat"); 9 to 10 "retire". Dotted edges from the AI lane to 1, 3, 7 and 9 labeled "draft".

### D23-2 Approvals through approval cards (S1)

- **Title:** How a decision gets a person's yes, and why AI cannot give it.
- **Purpose:** Shows S1 end to end, with the "only the runner writes outcomes" rule.
- **Lanes:** Person or AI client; The product's MCP server; Engine; Approval runner (Power Automate); Approver in Outlook or Teams; Data store.
- **Nodes:** 1 "Propose decision" (Person or AI client); 2 "Call tool as the person" (MCP server); 3 "Most specific rule group, or the accountable holder" (Engine); 4 "Create pending approval records" (Data store); 5 "Send card" (Runner); 6 "Approve or reject, typed reason" (Approver); 7 "Write outcome" (Runner); 8 "Count: person, user, holds role, passes separation rules" (Engine); 9 "Accept or reject decision" (Engine); 10 "Timeout: remind, escalate, restart" (Engine).
- **Edges:** 1 to 2 "tool call"; 2 to 3 "on behalf of"; 3 to 4 "one per required response"; 4 to 5 "event"; 5 to 6 "card"; 6 to 7 "response"; 7 to 8 "outcome (approval writer only)"; 8 to 9 "all rules met"; 5 to 10 "28-day limit". A dotted gold edge from "Person or AI client" to node 7 labeled "refused: AI cannot write outcomes".
- **Binding note box (light blue):** "Dataverse: business event starts the flow. Azure: API posts to an HTTP trigger restricted to its service principal. Both: Process license on the runner flow group."

### D23-3 Change management paths (P03)

- **Title:** Standard, normal and emergency changes, built in or linked.
- **Purpose:** Shows how a change type and a tier choose the authorization path, and where linked mode differs.
- **Groups:** Built-in mode (white); Linked mode (light gray, "ITSM tool keeps the workflow").
- **Nodes:** "Change ready"; diamond "Standard model listed and conditions hold?"; diamond "Urgent?"; "Standard: pre-authorized by model"; "Normal"; diamond "Tier row says"; "Business owner authorizes"; "Change manager authorizes (board for exceptions)"; "Change manager authorizes at once"; "Window on the schedule"; "Client tool deploys" (light gray); "Deployment records arrive (P05)"; "Close with result"; "Post-change review" (gold outline). Linked group: "External change number and link"; "ITSM step reports authorization, or the change manager attests".
- **Edges:** "yes" from the standard diamond to standard; "no" to the urgent diamond; "yes" to emergency; "no" to normal; normal to tier diamond; tier diamond to business owner "lower tiers" and to change manager "highest tier"; all authorizations to "Window"; window to "Client tool deploys"; to "Deployment records"; to "Close"; emergency and failed to "Post-change review". Dotted edge from linked group to "Deployment records" labeled "flags work the same".

### D23-4 Deployment record and flags (P05)

- **Title:** Three ways a deployment arrives, and the three flags.
- **Purpose:** Shows D-19's three paths and how the engine sets Is Other Path, Is Outside Window and Is Missing Approval.
- **Groups:** Outside the product (light gray): "Client pipeline record step", "Power Platform Pipelines custom host flow", "Owner's AI tool", "Person in the app". Inside (white): "Upsert by idempotency key", "Resolve target, pipeline, change", "Read gate policy row", three flag checks, "Act by gate level".
- **Nodes:** as listed, plus flag badges in light gold on the record: "other path", "outside window", "missing approval", and a fourth small badge "AI-only code approval (sweep proposal)".
- **Edges:** each outside node to "Upsert" labeled with its route (pipeline step, pipeline step, AI tool, person in app); "Upsert" to "Resolve"; "Resolve" to "Read gate policy row"; to each flag check; flags to "Act by gate level" with labels "Off: nothing", "Record: stored", "Flag: notify owner and change manager", "Advise and Enforce: check step later".

### D23-5 Gap check and claim (P06)

- **Title:** What is running that is not in the register: live, as the admin.
- **Purpose:** Shows the delegated live read, the comparison and the claim list.
- **Lanes:** Admin in an MCP client; The product's MCP server; Microsoft inventory (light gray); Engine; Candidate owner; Platform admin.
- **Nodes:** "Ask: run gap check"; "Read inventory with the admin's token"; "Inventory answers (agents, apps, flows, sites)"; "Compare by Microsoft ID"; three outcome boxes "matched: last seen", "unregistered: open claim", "vanished: open claim"; "Claim to an app"; "Claim to a new app: intake"; "Dismiss with reason"; "Resolve vanished: retired, moved, not visible".
- **Edges:** labeled "delegated only; service principal gets 403" on the inventory call; "no copy kept" on the inventory answer; claim edges labeled "confirmation card if not an owner"; vanished to "Retirement (P09)" labeled "retired".

### D23-6 Owner leaves (P08)

- **Title:** When an owner leaves: deputy now, successor next, access in Entra.
- **Purpose:** Shows the three detection options and the hand-over.
- **Lanes:** Signals (light gray: "Announced leave", "Owner check (on demand)", "Lifecycle Workflows leaver extension (optional sample)", "Agent 365 ownerless agents"); Engine; Deputy; Successor; Accountable (business owner or sponsor); Entra and Agent 365 (light gray).
- **Nodes:** "Record leave on principal"; "Owner check reviews due"; "Deputy receives reminders and cards"; "Move open approvals"; "Propose successor"; "Successor accepts on card"; "Accountable approves"; "End old assignment"; "Disable user, remove access" (Entra lane); "AI lead decides on agent" (gold outline).
- **Edges:** each signal to "Record leave"; "Record leave" to "Owner check reviews due" and "Move open approvals"; "Propose successor" to "Successor accepts" to "Accountable approves" to "End old assignment"; dotted edge to the Entra lane labeled "done in Entra, not by the product".

### D23-7 Retirement (P09)

- **Title:** Retire on purpose, with evidence that nothing is left.
- **Purpose:** Shows the decision, the impact check, the removal in Microsoft's tools and the gap check confirmation.
- **Nodes:** "Portfolio decision: retire" (diamond); "Impact walk: dependants informed"; "Data owner decides retention"; "Removal change request (P03)"; "Removal in admin centers, Entra, Agent 365" (light gray); "Evidence links"; "Gap check: components vanished"; "Retirement complete" (diamond); "Stage: retired".
- **Edges:** in sequence, labeled "accepted", "no blocking dependant", "retention decided", "authorized", "done outside", "linked", "nothing seen", "accepted".

### D23-8 AI draft and human accept, and configuration change (P10, P11)

- **Title:** AI types, people decide: drafts, cards and configuration versions.
- **Purpose:** Shows that MCP writes start as drafts, which channel accepts each kind, and how a configuration change becomes a new version.
- **Lanes:** Admin or person; AI client; The product's MCP server; Engine; Approval runner; Configuration store; Operational records.
- **Nodes:** "Instruction"; "AI prepares draft or change"; "Tool call as the person"; "Downgrade to draft or proposed, stamp AI"; "Validate configuration change (V01 to V44)"; "Configuration draft (patch) and decision"; "Card with the diff to the configuration admin"; "Activate: write rows, create version n+1"; "New rows stamp n+1".
- **Edges:** "instruction"; "tool call"; "on behalf of"; "configuration path" to validate; "operational path" to downgrade; "card"; "accepted"; "in force". A dotted gold edge from AI client to "Card" labeled "AI cannot answer".

### D23-9 What runs where (bindings)

- **Title:** The same processes on two foundations.
- **Purpose:** Shows section 3.4 side by side, so a reader can see what changes between the two bindings (D-22, settled by D-26).
- **Groups:** Left "Dataverse binding" (light blue): plug-ins, custom APIs, business events, scheduled flows, approval runner flows, group teams, workload grants, generative pages. Right "Azure database binding" (light blue): one Functions app with the API, rules library and MCP server, outbox and queue, timer functions, Durable Functions, approval runner flows over HTTP in a Power Platform environment with Dataverse, app roles, workload grants, web page. Center (white, shared): "The product's MCP server in Azure", "Live reads as the admin (gap check, owner check)", "Pipeline record step", "Approvers in Outlook and Teams".
- **Edges:** from the shared MCP server to each side's engine labeled "on behalf of the person"; from the record step to each side labeled "workload identity"; from each side's runner to "Approvers" labeled "approval cards"; a gold note on the right group: "Approvals still need Dataverse for approval storage [P4]".

---

## 8. Open design questions

Only questions that no decision settles (decision records in [docs/decisions/](../../decisions/)). D-03's own open points (the gate ladder, fail mode, which check step first, who owns the policy table) belong to D-03 and are not repeated here.

Questions settled as working defaults keep their heading and one line. Questions 4 and 5 are still open: they are open design questions 9 and 8 in [28-working-defaults.md](28-working-defaults.md), section 3, and the design uses their recommended option as the working default. Four more of the six open design questions in 28 touch this document and are asked there, not here: question 1 (fixed engine meanings), question 4 (the admin accepts the exact configuration change on a card, P11 step 6), question 7 (AI-only code approvals, P05 step 5) and question 12 (workload grants for pipelines and the approval runner). The text marks each place.

### Question 1: On the Azure binding, what sends the approval cards?

Settled as a working default in 28-working-defaults.md (Q19), which applies now that D-26 has chosen Azure. It is not yet a decision.

### Question 2: How should the product notice that an owner has left?

Settled as a working default in 28-working-defaults.md (Q18). It is not yet a decision.

### Question 3: In linked mode, how does the product know a change was authorized?

Settled as a working default in 28-working-defaults.md (Q22). It is not yet a decision.

### Question 4: Which channel counts as a person accepting an AI draft that is not a decision?

- **Open design question.** This is open design question 9 in [28-working-defaults.md](28-working-defaults.md), section 3. It touches D-02 and D-16. The design uses option A as the working default.
- **Background.** D-02 says rows AI creates start as draft or proposed and people approve on cards. For decisions this is settled: only approval cards (S1). For drafts that are not decisions, such as an AI-registered application, an AI-submitted intake, or a document an AI marks "current", the decisions do not say which channel proves a person accepted. An MCP call cannot prove it, because the AI holds the person's token.
- **Example.** Claude Code registers a new code app for a builder. Should it enter the register as soon as the builder says "yes, submit", or only after the named business owner confirms on a card?
- **Options.**
  - **A. The P10 default table, held as configuration per subject kind (working default).** An AI client may submit intakes, changes and decisions for the person where Tool Policy (C41) allows it. Registering an application and marking a gate-relevant document current need a confirmation card. Links and deployment records need nothing. *Pros:* proof where it matters, low friction elsewhere. *Cons:* a rule set to explain.
  - **B. Cards for every AI draft.** *Pros:* simplest rule, strongest proof. *Cons:* card fatigue; owners start approving without reading.
  - **C. The person's word in the AI client is enough for everything except decisions.** *Pros:* fastest. *Cons:* an AI could register applications and mark documents current with no person in the loop; weak audit story.
- **Recommendation.** A, held in the `confirmation-channel` setting so a client can move toward B.
- **What the answer changes.** P10, the `confirmation-channel` setting, the C22 column Current Needs Person, the Tool Policy AI May Submit defaults, P06 claim confirmation, and the MCP surface document (which tools return "awaiting confirmation").

### Question 5: May the engine accept a decision by itself when the rules require no approval?

- **Open design question.** This is open design question 8 in [28-working-defaults.md](28-working-defaults.md), section 3. It touches D-02. The design uses option B as the working default (S1 step 1).
- **Background.** D-06 and D-07 make approvals configuration, so a client could set Minimum Count 0 for some decision types in some scopes (for example the lowest tier's intake). Standard changes already work this way: a person once approves the standard change model, and each matching change is pre-authorized. The engine is not an AI, but D-02's spirit is that a person answers for every decision.
- **Example.** A maker registers a tier 1 personal app. If the preset gives that intake no card, who is the decider on record?
- **Options.**
  - **A. Allow pre-authorized decisions for any decision type.** The engine accepts, records actor kind engine, and links the configuration change decision that set the rule; that decider answers for it. *Pros:* full flexibility, consistent with standard changes. *Cons:* a client could configure away human decisions for go-live or retirement.
  - **B. Allow them only for decision types listed in a locked setting (working default).** Shipped list: intake at the lowest tier and change authorization for standard changes. Every other decision type, including one with no approval rule, goes on a card to its accountable holder (C31). *Pros:* low friction where it is safe; the core promise holds for weighty decisions. *Cons:* one more locked setting; extending the list needs a product release.
  - **C. Never.** Every decision needs at least one person's card. *Pros:* the simplest promise. *Cons:* card fatigue for the lowest tier; standard changes would need a card each time, which defeats the point of a standard change.
- **Recommendation.** B.
- **What the answer changes.** S1 step 1, the "pre-authorized decision" rule (section 5), a locked C04 setting with the allowed decision types, the presets' approval rules, and the validation step in P11.

---

## Proposed by the build (2026-11-22)

- **Source.** The first build of the code in `src/` and its pipeline record step samples (`src/samples/pipeline-steps/`).
- **Status.** Every item below is a proposal. The processes above keep their meaning: no settled decision and no working default changes. Where an item rests on one of the six open design questions (28-working-defaults.md, section 3), it names the question. A later design pass accepts or drops each item. Design questions the build could not settle are marked "open" in the items below.

### B23-1 P05: an emergency stays flagged inside its follow-up time

P05 step 5 says an emergency inside its follow-up time is not flagged until the time runs out. Document 21 (section 7.5: the emergency "stays flagged until its approval and review are done") and document 25 (OP-04: the follow-up due date comes with the flag) keep it flagged, and the build follows them. Proposal: step 5 says the emergency is flagged with its follow-up due date, and a view shows the flag as resolved once the authorization follows (document 22, section 8.3, item 4).

### B23-2 P05: conventions of the record step samples

- **Idempotency Key** (step 1): `ado-<collection>-<build>-<stage>-<job>-<job attempt>` for Azure DevOps, `gh-<repository>-<run>-<job>-<run attempt>` for GitHub Actions, `pp-pipelines-<stage run ID>` for Power Platform Pipelines. A job rerun is a new deployment with a new key.
- **Result words.** SucceededWithIssues becomes partly succeeded; cancelled and skipped become not deployed. An unknown word sends nothing and warns.
- **Route** (step 2). The engine sets it from the actor kind; the step never sends one (document 25, B25-6).
- **Fail soft.** Both pipeline steps run on always and continue on error, and the script always exits 0 with a warning in the platform's format; it retries only where the error contract allows.
- **Secretless.** Workload identity federation only: the Azure CLI signed in by the pipeline task, or GitHub's OIDC token exchanged with Microsoft Entra ID. No client secret, PAT or certificate is read. The token's scope follows from the endpoint URL (its origin plus `/.default`). The script refuses plain http to anything but the local machine, and a URL with a key in its query, before it asks for a token, and it sends the token only to the one endpoint origin, with no redirect.
- **Power Platform Pipelines.** Whether a cloud flow can call the endpoint with a managed identity or a federated credential is UNVERIFIED. If it cannot, that sample is withdrawn rather than given a secret.

### B23-3 P06: claims

- **Claim Key.** The key is lowercased for source type and External Id, and a vanished claim keys on the component kind. Its length is a document 22 point (B22-2).
- **Retired components** are not registered: an item that matches only a retired component becomes a new unregistered claim, and a retired component never vanishes. A component held by URL only never vanishes.
- **Estates.** O03 holds no estate key, so "registered components in covered estates" has no column to read (document 22, B22-5). The owner check's estate `entra-owners` is not yet in the shipped `estate` list; this is open.
- **Open, needs a design answer.** No rule says what happens to an open vanished claim whose component is seen again. Nothing guards against an empty final page that would mark every registered component of an estate vanished, for example when the admin's role sees fewer kinds than the run claims to cover; the covered component kinds input is the only tool today.

### B23-4 P04 and P07: reviews and documents

- **P07.** OP-08 refuses an AI actor's update to current on a gate-relevant document (C22 Current Needs Person) on every path. Until an approval runner is connected, no confirmation card is sent: the tool stores the AI's "current" as draft and says so (working default, open design question 9, see 28-working-defaults.md).
- **P04.** Whether an AI client may mark a review done, and whether done needs Done On, Reviewer and an accepted Outcome Decision, is open. A decision about a review points at the review's application, with the review ID in its context, because O09 has no review subject kind.

### B23-5 P01, P03, P08 and S1: behavior as built

- **P01.** The engine accepts a lowest-tier intake with no card only after the accept gate's readiness check (G-14); without a business owner the intake decision stays proposed. Tier answers never move the tier (working default, open design question 8, see 28-working-defaults.md).
- **P03, linked mode.** The record step does not yet read authorization facts from Evidence Links, so a linked change is always flagged as missing approval today. The client-built ITSM step would hold the proposed `authorization-reporter` workload role (document 26, B26-1).
- **P08.** `record_owner_leave` is allowed for the person, an owner of the person's application, or a platform admin, and its engine write sets only Leaves On, Directory State and its date on O05.
- **S1.** The engine creates Approval Records pending, with Counts Toward Requirement false; the recompute after each answer sets it. A gate decision whose gate is not ready stays proposed, and the answer is kept with a warning. A restart after an approval timeout (an expired record, then a new pending record) is not built yet.
