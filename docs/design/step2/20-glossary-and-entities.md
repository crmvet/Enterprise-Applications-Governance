# Step 2, document 20: glossary and logical entities

> **Platform (D-26, 2026-11-22).** The first release is designed to run in the client's own Azure subscription: Azure SQL, one Azure Functions service that hosts the API, the MCP server and the engine, a React and Fluent web app, and Entra app roles assigned to groups. The Dataverse binding is kept as a possible later option. So in section 2.9 the Azure column applies, and in section 3 the Azure conventions. In rule 4, "Entra groups" means Entra app roles assigned to groups, or group reads through Microsoft Graph (D-26 updates D-11 this way). The text below was written while the data store was still open (D-22), so it describes both bindings.

- **Date:** 2026-11-22. **Status:** draft design, written client-neutral.
- **Step:** 2 of 3 (the design). This document is the single source of names for every other step 2 document: the MCP surface, the ERD, the processes, the pages and the engine. When another document needs a term or an entity, it uses the name here. When it needs a new one, it is added here first.
- **Name:** the product has no name yet, so this file says "the product" or [Product name] (D-05).
- **Platform-neutral (D-22).** This document was written while the data store was open: Dataverse, or a database in the client's own Azure subscription. D-26 has since chosen Azure (note above). So every name here is logical. Physical names (publisher prefix, table and column names) wait for the product name (D-05). Section 3 proposes the conventions for both platforms. Section 2.9 shows how each logical concept binds to each platform.
- **Follows:** decisions D-01 to D-22 (the decision records are in [docs/decisions/](../../decisions/)) and the Microsoft fit review of the first design draft (2026-11-21). A tag such as "fit 8" names an item of that review. Where this file and a decision disagree, the decision wins.
- **Sweep proposals.** Proposals from a sweep of Microsoft news up to 2026-11-21 that this design adopts are marked "(sweep proposal)". No decision has confirmed them yet. The Microsoft sources the sweep relied on are the [J] rows in section 4.
- **Patterns.** Ideas from governance practice since the 2021 release are used as patterns only, generalized. Idea IDs such as I-183 are kept as provenance tags. The 2021 tables are a naming reference only (D-22).
- **Microsoft facts** carry a source ID [S1] to [S22] or [J1] to [J5]. Section 4 lists the URL and the date checked. UNVERIFIED marks what no source confirmed.
- **Working defaults (alignment pass, 2026-11-22).** This file follows `28-working-defaults.md`. A line marked "(working default, 28 issue n)" or "(working default, 28 Qn)" is adopted for the design, and a later decision can change it. A line marked "(working default, open design question N, see 28-working-defaults.md)" uses the recommended option of one of the six open design questions (28, section 3). `28-working-defaults.md` gives the background for each default.

**Seven rules every name in this file obeys.**

1. **Everything a client may tailor is a configuration row, never a fixed value** (D-06, D-07, D-10). The engine reads rows. It never branches on a fixed tier number, stage number or label.
2. **The only fixed vocabulary is the engine contract** (section 2.4): a short list of meanings the engine must understand to enforce its rules. Labels, counts and order stay configurable (working default, open design question 1, see 28-working-defaults.md).
3. **AI never approves** (D-02, D-10). No entity lets an AI client, an agent identity or a pipeline count as an approver. Only the approval writer writes approval outcomes.
4. **Roles come only through Entra groups** (D-11). No entity assigns a product role to a person directly. Non-human workloads (pipelines and the approval runner) hold a workload role through a Workload Grant (C42). This is a recorded, expiring exception for non-people only, and agent identities never get one (working default, open design question 12, see 28-working-defaults.md).
5. **Links, not copies** (D-15, D-17, D-19). The product stores an ID, a URL, a label and a "last seen" date for anything another tool owns.
6. **Append-only where it matters.** Decisions, approval records, audit entries and configuration set versions are never edited after they are final. A change is a new row that supersedes the old one [S1].
7. **Agent-readable.** Every entity and every column has a one-line description in plain English.

---

## 1. Glossary

Each term has one line. "From" is the decision or source that makes the term necessary. "Entity" is where the term lives in section 2 (C = configuration, O = operational, L = later).

### 1.1 Product and operating model

| Term | Definition | From | Entity |
|---|---|---|---|
| The product | The playbook, guidelines, processes and engine this design describes. Called [Product name] until the product has a name | D-01, D-05 | (all) |
| Playbook | How the organization works, in chapters, kept as wiki or web pages outside the engine. The engine links to it and tracks its status | D-01, D-17 | C39 Guideline (links) |
| Guideline | A short, numbered, permanent rule (G1 to G19) that the playbook and the engine both cite | D-01, D-10 | C39 Guideline |
| Process | The stages, gates, approvals and reviews from idea to retirement, held as configuration rows | D-01, D-06 | C12 Lifecycle Stage, C13 Gate |
| Engine | The code that reads the configuration and enforces it on every write, whoever calls. It always runs as the caller, never with more rights (working default, 28 issue 9). On Dataverse: synchronous plug-ins and custom APIs. On an Azure database: the API layer, which must be the only write path, hosted with the MCP server in one Azure Functions service (working default, 28 issue 13) | D-10, D-22 | (all) |
| Engine contract | The short list of fixed meanings the engine understands (section 2.4). Everything else is configuration | D-06, D-10 | 2.4 |
| Party | One of the seven groups the product brings together: business, IT, AI, citizen developers, PMO, change control, architects | D-01 | C27 Party |
| Role | A product-wide responsibility, such as change manager or AI lead. A person holds it only through membership of an Entra group. A workload role is held by a non-human principal only through a workload grant (working default, open design question 12, see 28-working-defaults.md) | D-11, D-20 | C29 Role |
| Owner role | A responsibility for one business application, such as business owner, technical owner, data owner, builder, sponsor or support owner | D-01, fit 8 | C28 Owner Role |
| Owner assignment | One principal holding one owner role for one business application, with a deputy flag and start and end dates | D-01 | O06 Owner Assignment |
| Deputy | The person who acts when the holder of an owner role is away. Each owner role needs one by default | D-01 [S2] | O06 Owner Assignment |
| Decision right | Who is responsible, accountable, consulted or informed for one decision type in one scope. Exactly one accountable per row | D-07 | C31 Decision Right |
| AI lead | A default role. It keeps the AI guidelines, is consulted when an agent's autonomy level is set, and decides on the agents Microsoft flags: keep, reassign, consolidate or retire, with the reason and a link to Microsoft's evidence (sweep proposal for the last duty's wording) | D-20 | C29 Role |
| Security reviewer | A default role for security and risk. It records security review decisions and keeps the security questions of the tier rules (working default, 28 issue 7) | D-01 | C29 Role |
| Reader | A default read-only role for people who only look things up. It reads the register, decisions and the change schedule, but not audit entries or restricted-read columns (working default, 28 issue 7) | D-11 | C29 Role |
| Configuration service | The people who set up a client's configuration: the client's own staff, or a team from outside the client's organization. Not a product feature. Access to a client's tenant for people from outside: a guest account in a group that is PIM-eligible for the configuration admin role, activated with the client's approval and time-bound. Without Entra ID P2, the client's staff run the tools while the outside helper advises (working default, 28 Q17) | D-03, D-07 | (none) |
| MCP first | The product's tools come first and its screens second | D-02, D-16 | (none) |
| The product's MCP server | The product's own MCP server with product-specific tools. The client hosts it and it calls the data store on behalf of the signed-in person | D-09, D-16 | (none) |
| MCP route (sweep proposal) | One documented way a tool reaches the engine: the Dataverse MCP server's `invoke_api`, Microsoft's MCP management server, or the product's own server. Chosen per tool in the MCP surface document | D-09 | (none) |
| Reference client | Claude Code, used for examples, tests and guides | D-16 | (none) |
| Client app | The MCP client or application that made a call, such as Claude Code or the product's own web page. Recorded on every write | D-10 (sweep proposal) | Common columns |
| Approval card | A Power Automate approval answered in Outlook or Teams, sent by the approval runner. The same on the Azure binding (working default, 28 Q19) | D-02, D-16 | O10 Approval Record |
| Generative page | The main screen on the Dataverse binding. On the Azure binding a web page plays the same part (D-22 reopens D-18) | D-18, D-22 | (none) |

### 1.2 Register

| Term | Definition | From | Entity |
|---|---|---|---|
| Business application | One thing the business runs and answers for, of any kind, made of one or more technical components. The record every party points to | D-01 | O02 Business Application |
| Application kind | The category of a business application, such as Power Platform app, agent, AI-built app, code app, SaaS, external system or API | D-06 | C15 Application Kind |
| Technical component | One part of a business application inside a tool, such as an app, a flow, an agent, a solution, an Entra identity or a repository. Held as the tool's ID, a link and a "last seen" date | D-15 | O03 Technical Component |
| Component kind | The type of a technical component, using the tool's own word. New Microsoft kinds are seed rows (sweep proposal) | D-06 | C16 Component Kind |
| Portfolio | A business grouping of applications. Portfolios can nest. The default configuration labels two levels: portfolio and application group | D-01, D-06 | O01 Portfolio |
| Application group | The default label of the second portfolio level. The word comes from the 2021 release | D-06 | O01 Portfolio |
| Tier | A risk tier. Any number per client, three by default. A stored value, set by a decision, never a formula | D-06 | C08 Tier |
| Tier rank | The number that orders tiers from least to most serious. The engine compares ranks, never tier numbers | D-06 | C08 Tier |
| Tier question | A plain question whose answer suggests a tier, such as "Does it change data under its own identity?" | D-06 | C09 Tier Question |
| Tier answer option | One allowed answer to a tier question, with the tier it implies | D-06 | C10 Tier Answer Option |
| Tier assessment | One set of answers for an intake request or an application, the tier they imply, and the decision that set the tier. The most serious answer wins | D-06 | O08 Tier Assessment |
| Vocabulary mapping | A row that joins one product taxonomy row to a term from Microsoft or a framework, such as default tier 3 to "Critical" [S4], "Zone 3" [S5] and agent risk "Tier 3: High risk" [S2] | D-06 | C11 Vocabulary Mapping |
| Microsoft CoE baseline (sweep proposal) | A shipped preset whose tiers map one to one to Microsoft's three agent risk tiers and their controls [S2]. It ships in the first release, extends Balanced, and is re-checked against Microsoft's guidance each release (working default, 28 Q6) | D-07 | C03 Preset |
| Lifecycle stage | Where an application is between idea and retirement. Eleven stages by default, numbered 0 to 10 | D-01, D-06 | C12 Lifecycle Stage |
| Stage rank | The number that orders lifecycle stages, so the engine can test "at or after a stage". Stage Number and Sort Order stay display only (working default, 28 issue 15) | D-06 | C12 Lifecycle Stage |
| Gate | A recorded decision that lets an application move past a stage. Defaults: Accept, Commit, Design, Go-live, Value | D-01, D-06 | C13 Gate |
| Gate requirement | What must be true before a gate decision can be accepted, such as "data-use decision accepted" or "runbook current", per tier or kind | D-06, D-17 | C14 Gate Requirement |
| Dependency | A typed, directed link: this application needs that application or component | D-01 | O04 Dependency |
| Dependency type | The kind of dependency, such as "reads data from", "calls API of", "run-time need in target" or "replaces" | D-06 | C17 Dependency Type |
| Impact ("what breaks") | The set of applications reached by walking dependencies in either direction to a set depth | D-01 | O04 Dependency (computed) |
| Build path | How an application is built: citizen, fusion, pro or AI-built | D-06 (I-24) | C07 List Value |
| Autonomy level | What an agent may do on its own, such as assist, route or execute. Microsoft calls the key boundary "the assist-to-execute line" [S2]. Agreed as a decision | D-20 | C07 List Value |
| Principal | A directory object the product refers to: a user, a group, an agent identity, or a service principal or managed identity | D-11, D-15 | O05 Principal Reference |

### 1.3 Decisions and approvals

| Term | Definition | From | Entity |
|---|---|---|---|
| Decision | One entry in the append-only decision log: what was decided, about what, by whom, when and why | D-01 [S1] | O09 Decision |
| Decision type | The kind of decision, such as intake, priority, design, data use, tier, risk acceptance, change authorization, go-live, ownership, portfolio, exception, retirement or configuration change | D-06 | C18 Decision Type |
| Decision outcome | One allowed result for a decision type, such as accept, park, redirect or decline for triage, or keep, invest, replace or retire for portfolio | D-06 | C19 Decision Outcome |
| Decision status | Where a decision is: Proposed, Accepted or Superseded as Microsoft names them [S1], plus Rejected and Withdrawn | D-01 [S1] | C05 Status |
| Supersedes | A later decision replaces an earlier one. The earlier one stays, marked Superseded [S1] | D-01 [S1] | O09 Decision |
| Amends | A later decision changes part of an earlier one. Declined amendments stay listed (I-191) | D-01 | O09 Decision |
| Approval rule | A configuration row that says which approvals a decision type needs in a scope: who, how many, in what mode and order. The single most specific matching rule group applies (working default, 28 issue 2). A decision type with no matching rule still goes on one card, to the holder of its accountable letter (C31). Only a locked list of types may be accepted by the engine without a card: lowest-tier intake and standard changes (working default, open design question 8, see 28-working-defaults.md) | D-07 | C32 Approval Rule |
| Rule group | The rows of one rule entity (C31 or C32) with the same decision type and the same scope values. All rows in the group apply together, for example business owner and security reviewer | D-07 | C31, C32 |
| Specificity | The number of scope fields a rule group sets. The engine applies the single most specific matching group whole, and broader groups drop out. Ties are refused at validation, so a combined case such as a tier 3 emergency gets its own group in the seed (working default, 28 issue 2) | D-07 | C31, C32 |
| Consultation | An approval rule row with Is Consultation set to yes. The card asks a consulted role for input. The answer is stored as recommendation only and never counts (working default, 28 issues 12 and 15) | D-07 | C32 Approval Rule |
| Approval record | One required approval for one decision: who was asked, who answered, the outcome, the typed reason and when. Outcome columns are written only by the approval writer | D-02, D-10 | O10 Approval Record |
| Approval runner | The small set of solution-aware cloud flows that send approval and confirmation cards, wait, write outcomes back and send notifications. One flow group with one Process license, on both bindings. Earlier drafts called it the approval flow or the approval flow identity (working default, 28 issue 7 and Q19) | D-02, D-16 | (component) |
| Approval writer | The workload role held by the approval runner's identity. It writes approval outcome columns and nothing else. It holds the role through a workload grant (working default, 28 issue 7; open design question 12, see 28-working-defaults.md). Earlier drafts called it the approval recorder | D-02, D-10 | C29 Role, C42 Workload Grant |
| Separation rule | A rule that two people must differ for an approval (for example the proposer and the approver), or that two roles may not bind to the same Entra group. Preset defaults set how strict it is (working default, 28 issue 5 and Q10) | D-20 | C40 Separation Rule |
| Approval mode | How several approvers combine: everyone must approve, first to respond, or sequential, as Power Automate approvals offer [S10] | D-07 [S10] | 2.4, C32 Approval Rule |
| Approver kind (sweep proposal) | The actor kind of whoever answered an approval. Only a person counts toward a requirement | D-02, D-10 | C38 Actor Kind |
| Recommendation | The outcome of an AI stage in a client's own approval flow, or an AI suggestion. Recorded, never counted as a decision or an approval (fit 15) | D-02 | O10 Approval Record |
| AI never approves | The rule that no AI client, agent identity or pipeline can approve. Enforced in the data by the engine, not only in prompts | D-02, D-10 | C04 Setting, 2.4 |
| Actor kind | Who or what made a change: a person, an AI client acting for a person, an agent identity, a pipeline, the approval runner, or the engine itself | D-10 (I-183) | C38 Actor Kind |
| Performed by | The actor kind, the principal and the client app behind a write. Every row carries it | D-10 | Common columns |
| On behalf of | The person an AI client or pipeline acted for. Empty when a person acted directly | D-10 | Common columns |
| Configuration change decision | The decision about one submitted configuration draft. People accept it on a card, and the acceptance triggers activation. The admin decides, the AI performs, and the row shows both. A draft that touches a Gate Policy row uses the gate policy change decision type instead | D-10 | O09 Decision |

### 1.4 Change management

| Term | Definition | From | Entity |
|---|---|---|---|
| Change request | One planned change to one or more applications, with type, window, authorization and result | D-08 | O11 Change Request |
| Change type | The kind of change. Standard, normal and emergency by default, all configuration rows | D-06, D-08 | C20 Change Type |
| Standard change model | A pre-approved pattern for a standard change at a tier, with the conditions that keep it standard | D-08 | C21 Standard Change Model |
| Change authority | Whoever must authorize a change of a type at a tier. Held as decision rights and approval rules for the change authorization decision type, not as a separate table | D-08 | C31, C32 |
| Change manager | A default role. By default it authorizes tier 3 changes and emergencies, and it keeps the change schedule and the standard change models (working default, 28 issue 4 and Q11) | D-08 | C29 Role |
| Change board | A role whose members decide as a group. By default it handles exceptions and windows that cover several applications. Tier 3 changes go to the change manager (working default, 28 issue 4 and Q11) | D-08 | C29 Role |
| Board decision | A change authorization or exception decision whose approvals come from change board members | D-08 | O09 Decision |
| Emergency change | A change of a type that may be authorized at once and reviewed after. The change manager authorizes it by default. There is no separate emergency approver role (working default, 28 issue 4) | D-08 | C20 Change Type |
| Change window | The planned start and end of a change, held on the change request | D-13 | O11 Change Request |
| Change schedule | The read-only list of planned changes by window and target. A view, not a table. No freeze tables and no booking | D-13 | O11 (view) |
| Change management mode | Built-in (the product runs change requests) or linked (the client's ITSM tool does, and the product stores its number and link). Built-in is the default. The mode may differ per portfolio through an optional portfolio scope (working default, 28 Q2) | D-08 | C04 Setting, 2.4 |
| External change reference | In linked mode, the ITSM tool's change number and URL on the change request. The tool's workflow is never copied. A recorded number alone never counts as authorization: the ITSM tool reports the state through a client-built step, or the change manager attests on a confirmation card (working default, 28 Q22) | D-08 | O11 Change Request |
| Affected application | An application a change request touches besides its main one | D-08 | O12 Affected Application |
| Change result | How a change ended, such as succeeded, partly succeeded, failed, rolled back or cancelled | D-08 | C07 List Value |
| Post-change review | The review after an emergency or failed change | D-08 | O21 Review |

### 1.5 Deployment

| Term | Definition | From | Entity |
|---|---|---|---|
| Deployment record | One record that an artifact version reached a target: what, where, when, by which pipeline, by which route, with which result | D-19 | O14 Deployment Record |
| Artifact | What a pipeline moves: a managed solution, a container image, a code package, a static site, an agent package or a commit | D-19 (I-179) | O13 Artifact Version |
| Artifact version | One version of one artifact, keyed by artifact name plus version, with an optional digest | D-19 (I-178) | O13 Artifact Version |
| Deployment target | Where something is deployed: a Power Platform environment, a Microsoft 365 tenant for runtime apps, an Azure resource group, a GitHub environment or a SaaS tenant. Linked, never copied | D-15, D-19 | O15 Deployment Target |
| Target stage | The ALM stage of a target, such as development, test or production. Gate policies are set per target stage | D-03 (I-136) | C34 Target Stage |
| Target kind | The platform type of a target | D-06 | C07 List Value |
| Change path | The routes a target expects deployments to arrive by, such as "pipeline only" | D-03 | C35 Change Path |
| Deployment route | How a deployment record arrived: a pipeline step, an owner's AI tool, a person in the app, or another way found later | D-19 | C36 Deployment Route |
| Pipeline reference | A link to one of the client's pipelines. The product never builds, hosts or runs it | D-19 | O16 Pipeline Reference |
| Pipeline map entry | One row that says this pipeline deploys this application to this target, in this order | D-19 | O17 Pipeline Map Entry |
| Record step | A copy-paste step at the end of the client's pipeline that writes a deployment record. A sample, not a package | D-19 | (sample) |
| Check step | A copy-paste step before the deploy that asks whether the change is approved (Advise and Enforce). After the gate decision | D-03, D-19 | L05 Gate Check |
| Gate level | One rung of the ladder: Off, Record, Flag, Advise, Enforce | D-03 | 2.4, C33 Gate Policy |
| Gate policy | A configuration row that sets the gate level for one tier, change type and target stage, optionally for one portfolio (working default, 28 Q2). Changing it is itself a decision. A missing row is a broken configuration: the engine refuses governed writes and has no built-in fallback (working default, 28 issue 14) | D-03 | C33 Gate Policy |
| Fail mode | What Enforce does when the product cannot answer: fail closed (stop) or fail open (go and flag) | D-03 | C33 Gate Policy |
| Outside-window flag | Set on a deployment record that landed outside its change request's window | D-13 | O14 Deployment Record |
| Other-path flag | Set on a deployment record whose route is not allowed by the target's change path | D-03, D-19 | O14 Deployment Record |
| Missing-approval flag | Set on a deployment record whose change lacks the approvals its policy requires | D-03 | O14 Deployment Record |
| Tested in (later) | A production deployment's link to the test deployment it was promoted from | D-19 (I-180) | O14 Deployment Record |
| Idempotency key | A value a record step sends (such as run ID plus stage) so a retry never creates a second record (I-189) | D-19 | O14 Deployment Record |

### 1.6 Documentation, links and evidence

| Term | Definition | From | Entity |
|---|---|---|---|
| Documentation requirement | A configuration row that says which document type an application of a kind or tier needs, from which stage | D-17 | C23 Documentation Requirement |
| Document type | A kind of document, such as wiki page, runbook or architecture decision record | D-17 | C22 Document Type |
| Document link | One document for one application: link, status, owner, and the dates it was last updated and checked. A row exists only once a link has been recorded. The product never writes the document | D-17 | O19 Document Link |
| Document status | Draft, current or stale, stored on a document link. "Missing" is computed from the documentation requirements and never stored (working default, 28 issue 16) | D-17 | C05 Status |
| Delivery link | A generic link from an application, change request or intake request to work in another tool: tool, kind, ID or URL, label, date | D-12 | O18 Delivery Link |
| Link kind | What a link points to, such as Azure DevOps work item, GitHub repository, Planner unified plan, Planner premium plan, wiki page or ITSM change | D-12 | C37 Link Kind |
| Link category | Which entity uses a link kind: delivery, pipeline, document, evidence, Microsoft resource or external change | D-12, D-15 | 2.4, C37 Link Kind |
| Evidence link | A link from a decision, approval, change, deployment, review or claim to proof held elsewhere, such as a test result, an assessment export or an impact list (sweep proposal for the last) | D-08, D-15 | O20 Evidence Link |
| Evidence level | How far the evidence was checked: read, checked, as reported, proven, designed, or not read with a reason (I-44) | D-01 | C07 List Value |
| Last seen | The date the product last confirmed a linked item still exists | D-15 | Link columns |
| Moved link (sweep proposal) | A link whose target moved, such as a repository migrated to another host. The old link stays, marked moved, with the new URL | D-12 | O18, O16 |

### 1.7 Intake and gap check

| Term | Definition | From | Entity |
|---|---|---|---|
| Intake request | One request through the front door: a new idea, or an application already built that needs registering | D-01 | O07 Intake Request |
| Triage | The intake decision: accept, park, redirect to reuse, or decline. Tracked apart from delivery [S3] | D-01 | O09 Decision |
| Horizon | When accepted demand should be worked on: now, next or later | D-01 | C07 List Value |
| Size band | A rough size for accepted demand | D-01 | C07 List Value |
| Expected benefit | The benefit a requester claims at intake, checked later in a benefit review | D-01 | O07 Intake Request |
| Duplicate | An intake request that matches an existing application | D-01 | O07 Intake Request |
| Gap check | An on-demand query that an admin runs live, as themselves: "what is running that is not in the register?" | D-15 | O22 Gap Check Run |
| Gap check run | One execution of the gap check: who ran it, with which admin role, over which estates, and what it found | D-15 | O22 Gap Check Run |
| Estate | A source the gap check reads, such as the Power Platform inventory, which lists agents, apps (including Copilot managed runtime and App Builder apps), flows and Power Pages sites (preview) [S6] | D-15 | C07 List Value |
| Claim | One item on the claim list: an unregistered resource the gap check found, or a registered component it no longer sees (sweep proposal for the second kind) | D-15 | O23 Claim |
| Finding kind | Why a claim exists: unregistered (someone should claim it) or vanished (a registered component was not seen) | D-15 | 2.4, O23 Claim |
| Suggested claimant | The person Microsoft lists as owner at check time. A hint only, because inventory owners are weak (fit 8) | D-15, fit 8 | O23 Claim |

### 1.8 Configuration

| Term | Definition | From | Entity |
|---|---|---|---|
| Taxonomy record | Any configuration row that names a value clients may change: a tier, a stage, a type, a kind, a status | D-06 | C04 to C39 |
| Configuration set | A whole, named set of configuration rows. A client has one working set in force. Areas that need other rules use the optional portfolio scope on rule rows, never a second working set (working default, 28 Q2) | D-07 | C01 Configuration Set |
| Configuration set version | An immutable snapshot of a configuration set. The engine creates it only at activation, and it is append-only. Every operational row stamps the version in force | D-07, D-10 | C02 Configuration Set Version |
| Configuration draft | A proposed change to the working set, held as a patch from a base version, with its validation result and rationale. It never touches the live rows (working default, 28 issue 3) | D-07, D-10 | C46 Configuration Draft |
| Patch | An ordered list of upsert and retire operations by entity and key. Drafts, overlays, configuration files and client binding patches use it | D-10 | C46, C03 |
| Activation | The engine operation that applies a draft once people accept its configuration change decision. It writes the live configuration rows and the new configuration set version in one transaction. It is the only place live configuration rows change. Nobody activates by hand, and no AI can. No person, role or tool writes configuration tables directly (working default, 28 issue 3) | D-10 | C02, C46 |
| Preset | A shipped configuration set a client can apply, copy and adjust, such as Starter, Balanced, Regulated or Microsoft CoE baseline (sweep proposal) | D-07 | C03 Preset |
| Full preset | A shipped preset that is a complete configuration set | D-07 | C03 Preset |
| Overlay | A shipped partial preset: a patch that changes some rows of a full preset by key, such as small team or strict separation. A client applies one full preset plus declared overlays, and the result is validated as one set (working default, 28 Q3) | D-07, D-20 | C03 Preset |
| Working set | The client's own configuration set, usually copied from a preset | D-07 | C01 Configuration Set |
| Key | A stable text identifier for a configuration row, such as `tier-3` or `emergency`. Seed files, presets and MCP tools use keys, never internal IDs | D-07, D-10 | Taxonomy columns |
| Origin | Whether a configuration row was shipped by the product or added locally by the client. Upgrades never overwrite local changes | D-10 | Taxonomy columns |
| Seed | The JSON file the package ships with default rows. The seed is never the live copy (D-10) | D-10 | C02 (Seed Version) |
| Setting | One named switch with one value, such as the change management mode. Document 21 keeps the one table of every setting, with its default and owner (working default, 28 issue 15) | D-10 | C04 Setting |
| Value list | A named list of labels with no engine behavior, such as horizon or size band | D-06 | C06 Value List |
| List value | One label in a value list | D-06 | C07 List Value |
| Status | A configurable status label for one entity, mapped to one engine meaning | D-06 | C05 Status |
| Engine meaning | The fixed meaning behind a configurable label, such as "accepted" behind a client's "Approved by board" | D-06, D-10 | 2.4 |

### 1.9 Identity and audit

| Term | Definition | From | Entity |
|---|---|---|---|
| Entra group team | A Dataverse team backed by an Entra group, which carries security roles. Membership is managed in Entra and cached up to 8 hours [S7] | D-11 | C30 Role Group Binding |
| Role group binding | A configuration row that binds one product role to one Entra group, and says how the platform applies it (group team on Dataverse, app role on Azure). Groups only: a non-group principal is refused (V35) | D-11 | C30 Role Group Binding |
| Workload role | A role held by a non-human workload, not a person: deployment recorder (pipelines) and approval writer (the approval runner). It can never approve (working default, 28 issue 7) | D-10, D-19 | C29 Role |
| Workload grant | A configuration row that gives one non-human principal one workload role, with a reason, the deciding decision and an expiry. A stated exception to D-11 for non-human workloads only, because Entra does not put a group's app roles into a service principal's token [S22]. People still get roles only through groups, and agent identities and agent users never get a grant (working default, open design question 12, see 28-working-defaults.md) | D-11, D-19 | C42 Workload Grant |
| Direct assignment check | A check that flags any product role held by a user, agent account, agent user or service principal outside a bound group or a current workload grant. A grant on an agent identity or agent user is always flagged (sweep proposal for non-human principals) | D-11 | C30, C42 (check) |
| Principal kind | The directory object type of a principal: user, group, agent identity, agent user, service principal or managed identity | D-10, D-11 | 2.4, O05 |
| Audit entry | One product event: who did what, through which client, on behalf of whom. The full change log on the Azure binding; product events only on the Dataverse binding | D-10, D-22 | O24 Audit Entry |
| Engine stamp | Engine Version plus Engine Checked On, written on every engine-evaluated write next to the configuration set version. A later Modified On shows a write outside the engine, which a scheduled read-only check finds and records (working default, 28 Q16) | D-10 | Common columns |
| Urgent revoke | Cutting someone off at once by disabling the user or application user, not only by changing group membership (fit 23) | D-11 | (process) |

### 1.10 Reviews and lifecycle

| Term | Definition | From | Entity |
|---|---|---|---|
| Review | One periodic or event check on an application: owner, tier, access, benefit, architecture or post-change. It ends with a decision | D-01 | O21 Review |
| Review type | The kind of review, a configuration row | D-06 | C24 Review Type |
| Review cadence | How often a review type is due at a tier, and when to escalate | D-06 | C25 Review Cadence |
| Reminder rule | When and how the product reminds someone that something is due, and when it escalates to the deputy | D-06 | C26 Reminder Rule |
| Escalation | Passing a due item to the deputy after a configured delay | D-01 | C26 Reminder Rule |
| Portfolio decision | Keep, invest, replace or retire, decided by the business owner | D-01 | O09 Decision |
| Graduation | Moving an application to a higher support tier and a new technical owner [S4] | D-01 | O09 Decision (ownership) |
| Retirement | Taking an application out of use on purpose, by decision, with linked evidence that nothing is left | D-01 | O09 Decision, L03 |

### 1.11 Words this design does not use

| Avoid | Use instead | Why |
|---|---|---|
| Inventory, catalog, registry (for the product's own data) | Register, business application | Microsoft owns those words and those tools (D-01, D-15) |
| Import, sync (from Microsoft tools) | Link, gap check, record step | Nothing is copied on a schedule (D-15) |
| Release (the 2021 table) | Change request, artifact version, deployment record | One 2021 word covered three things |
| Environment (as an entity name) | Deployment target | Targets are not only Power Platform environments |
| Pipeline (as an entity name) | Pipeline reference | The product never owns a pipeline (D-19) |
| Approval (as an entity name) | Approval record | Power Automate owns the approval itself; the product keeps the outcome |
| Approval flow identity, approval recorder, emergency approver | Approval runner (the flows), approval writer (the workload role), change manager (emergencies) | One name each (working default, 28 issues 4 and 7) |
| Application (alone, as an entity name) | Business application | "Application" also means an Entra app registration |
| Approve (for AI) | Recommend, draft, propose | AI never approves (D-02) |
| Owner (alone) | Business owner, technical owner, or another owner role | Microsoft's "owner" fields mean creator or sponsor (fit 8) |
| A version label used as the name (in product-facing text) | The product, [Product name] | D-05 |

---

## 2. Logical entities

### 2.1 How to read this section

- **Class.** CONFIGURATION means taxonomy and rule rows that an admin changes, through an AI acting on the admin's instruction (D-10). The AI client prepares a Configuration Draft (C46). People accept the configuration change decision on a card. The engine's activation operation then writes the live rows and the new Configuration Set Version. No person, role or tool writes configuration tables directly (working default, 28 issue 3). OPERATIONAL means records people, AI clients and pipeline steps create in daily work.
- **IDs.** C01 to C46 are configuration entities (C41, C43, C44 and C45 are still proposals, section 2.10), O01 to O24 operational, L01 to L05 later. Other step 2 documents cite these IDs.
- **When.** R1 means the first release (D-18). Later means kept in the design but not built first.
- **Relationships** are written "to X (many to one)" from the entity being described.
- **Logical names** are singular, plain English, with no prefix. Section 3 turns them into physical names.

### 2.2 Common columns (every entity)

| Column | Description |
|---|---|
| Id | The unique identifier the platform generates. Never shown to people and never used in MCP tool calls for configuration rows |
| Name | A short human label, the primary display column |
| Description | Plain-text description, written for people and agents |
| Created On | When the row was created (platform-supplied) |
| Created By | The principal that created the row (platform-supplied) |
| Modified On | When the row last changed (platform-supplied) |
| Modified By | The principal that last changed it (platform-supplied) |
| Performed By Actor Kind | The actor kind behind the last write: person, AI client for a person, agent identity, pipeline, approval runner or engine |
| On Behalf Of | The principal an AI client or pipeline acted for. Empty when a person acted directly |
| Client App | The MCP client or application that made the last write, such as "Claude Code" (sweep proposal: "client app X on behalf of user Y") |
| Is AI Created | Yes when an AI client created the row. AI-created operational rows start as Draft or Proposed (D-02) |
| Configuration Set Version | Operational rows only: the configuration set version in force when the row was created. It also fixes the guideline versions that applied, because guidelines are configuration rows |
| Engine Version | The engine library version that evaluated the last write (working default, 28 issue 15) |
| Engine Checked On | When the engine last evaluated the row. A later Modified On means a write outside the engine, which the scheduled read-only check finds (working default, 28 issue 15 and Q16) |

### 2.3 Taxonomy columns (every configuration entity except C01 to C03 and C46)

| Column | Description |
|---|---|
| Key | Stable text identifier, unique per entity within a configuration set. Lowercase letters, digits and hyphens (section 3.2). Never reused after removal |
| Label | The text people see. Can be translated |
| Sort Order | Display order. Never used as logic |
| Is Active | No when the row is retired. Retired rows stay so old records still resolve |
| Origin | Shipped (from a preset or seed) or local (added or changed by the client). Upgrades never overwrite local rows |
| Configuration Set | The configuration set this row belongs to |
| Engine Meaning | Only on entities listed in section 2.4: the fixed meaning the engine reads |

### 2.4 The engine contract: the only fixed meanings

The engine must understand a few meanings to enforce its rules (D-10). Each is a fixed value list. A client can rename, add or hide labels, but every label maps to one of these meanings. The list is treated as acceptable under D-06 (working default, open design question 1, see 28-working-defaults.md). See section 6, Question 1.

| Contract | Fixed meanings | Where it is used | Why the engine needs it |
|---|---|---|---|
| Decision status meaning | proposed, accepted, rejected, superseded, withdrawn | C05 Status for O09 Decision | Only accepted decisions satisfy gates; accepted rows become append-only [S1] |
| Record status meaning | draft, submitted, in progress, completed, rejected, withdrawn, cancelled | C05 Status for O07 Intake Request, O11 Change Request | AI-created rows must start as draft (D-02); gates read completion |
| Registration status meaning | draft, registered, archived | C05 Status for O02 Business Application | An AI-registered application stays draft until a person confirms it (D-02) |
| Approval outcome meaning | pending, approved, rejected, expired, cancelled, recommendation only | C05 Status for O10 Approval Record | Only approved outcomes from a person count (D-02) |
| Document status meaning | draft, current, stale (stored); missing (computed only, never stored on a row) | C05 Status for O19 Document Link | Gates and reminders read it (D-17). "Missing" keeps a label so pages and tools can show it (working default, 28 issue 16) |
| Claim status meaning | open, claimed, dismissed | C05 Status for O23 Claim | The claim list shows open items only |
| Deployment result meaning | succeeded, partly succeeded, failed, not deployed | C05 Status for O14 Deployment Record | Flags and the change result read it; "not deployed" covers abandoned runs (sweep proposal) |
| Review status meaning | due, done, cancelled | C05 Status for O21 Review | Reminders and escalation read it |
| Lifecycle meaning | pre-live, live, retired | C12 Lifecycle Stage | Reviews run for live applications; retired ones leave the claim comparison |
| Gate level | off, record, flag, advise, enforce | C33 Gate Policy | The ladder's behavior (D-03) |
| Fail mode | fail open, fail closed | C33 Gate Policy | Enforce behavior when the product cannot answer (D-03, open) |
| Approval mode | everyone must approve, first to respond, sequential | C32 Approval Rule | Mirrors Power Automate approval types [S10] |
| Decision right letter | responsible, accountable, consulted, informed, supports (AI only) | C31 Decision Right | Exactly one accountable per row, as Microsoft's agent CoE guidance advises ([roles and responsibilities](https://learn.microsoft.com/agents/center-of-excellence/roles-responsibilities)); AI can only support (D-02) |
| Actor kind meaning | person, AI client for a person, agent identity, pipeline, approval runner, engine | C38 Actor Kind | AI never approves; performed by (D-10). "Approval runner" replaces the earlier value "approval flow" (working default, 28 issue 7) |
| Principal kind | user, group, agent identity, agent user, service principal or managed identity | O05 Principal Reference | Only a user can approve; roles bind only to groups (D-11; sweep proposal for agent principals) |
| Deployment route meaning | pipeline step, AI tool, person in app, other | C36 Deployment Route | The other-path flag compares routes with the change path (D-19) |
| Link category | delivery, pipeline, document, evidence, Microsoft resource, external change | C37 Link Kind | Each link entity accepts only its category |
| Change management mode | built in, linked | C04 Setting | Linked mode stores the external reference and skips the built-in workflow (D-08) |
| Finding kind | unregistered, vanished | O23 Claim | Different actions on the claim list (D-15; sweep proposal for vanished) |
| Requirement kind | decision accepted, document current, review done | C14 Gate Requirement | How the engine tests a gate requirement |
| Configuration set kind | preset, working | C01 Configuration Set | Presets are read-only templates (D-07) |
| Preset kind | full, overlay | C03 Preset | One full preset plus declared overlays, validated as one set (working default, 28 Q3) |
| Draft status meaning | draft, submitted, activated, discarded | C46 Configuration Draft | Only a submitted draft can be accepted; only the engine activates (working default, 28 issue 3) |
| Patch operation | upsert, retire | C46 Configuration Draft, C02, overlays | Rows are never deleted; a retired row stays so old records resolve |
| Separation rule kind | approver differs, roles in different groups | C40 Separation Rule | How the engine tests separation of duties (working default, 28 issue 5) |
| Separation subject | proposer, instructing admin, deployer, owner role holder, role holder | C40 Separation Rule | Who the approver must differ from |
| Separation enforcement | refuse, exception decision, flag | C40 Separation Rule | What the engine does when the rule is broken |
| AI-only code approval handling | ignore, flag, stop | C33 Gate Policy, C04 Setting | What a deployment gets when its only code approval came from an AI (working default, open design question 7, see 28-working-defaults.md) |
| Origin | shipped, local | All configuration rows | Upgrade rule: never overwrite local rows |

### 2.5 Configuration entities

#### 2.5.1 Summary

| ID | Logical name | Purpose | Class | Key relationships | Default rows ship | From |
|---|---|---|---|---|---|---|
| C01 | Configuration Set | A whole, named set of configuration rows: a preset or a client's working set | CONFIGURATION | Has many Configuration Set Versions and Configuration Drafts; every taxonomy and rule row belongs to one set; optional Based On Preset (to C03) | Yes: one working set copied from the default preset | D-07 |
| C02 | Configuration Set Version | An immutable snapshot of a set, created by the engine only at activation and stamped on operational rows. Append-only (class A in document 22) | CONFIGURATION | To Configuration Set (many to one); to the configuration change Decision (many to one); to the Configuration Draft it activated (one to one); stamped by every operational row | Version 1 at install | D-07, D-10 (28 issue 3) |
| C03 | Preset | A shipped, read-only configuration set a client applies, copies and adjusts. Stored as a Configuration Set whose kind is preset. A full preset or an overlay | CONFIGURATION | Is a Configuration Set; working sets point to it through Based On Preset | Full: Starter, Balanced, Regulated, Microsoft CoE baseline (sweep proposal; ships in the first release, extending Balanced, working default 28 Q6). Overlays: small team, strict separation, linked change management (working default, 28 Q3), and data domains starter (D-37, B20-6) | D-07, D-03 |
| C04 | Setting | One named switch with one value | CONFIGURATION | To Configuration Set; optional Portfolio (change management mode only) | Change management mode = built in; AI approval lock = on (locked); reminder channel; guideline hub base URL; `intake-visibility` = own; `claim-visibility` = admins and claimant; `record-access-mode` = open, with portfolio read available (working default, 28 issue 10 and Q13); a locked list of decision types the engine may accept without a card (working default, open design question 8, see 28-working-defaults.md). The full list, with defaults and owners, is document 21's settings table; `audit-retention-days` replaces `history-retention-days` (working default, 28 issue 15) | D-08, D-10 |
| C05 | Status | A configurable status label for one entity, mapped to one engine meaning | CONFIGURATION | Used by O02, O07, O09, O10, O11, O14, O19, O21, O23 | One row per engine meaning per entity | D-06 |
| C06 | Value List | A named list of labels with no engine behavior | CONFIGURATION | Has many List Values | horizon, size band, build path, autonomy level, target kind, artifact kind, change result, evidence level, portfolio level, estate, channel, confidence, impact role, criticality, application category, vocabulary, tool; location kind, data vendor, retention class and access approach (D-37, B20-6) | D-06 |
| C07 | List Value | One label in a value list | CONFIGURATION | To Value List (many to one) | Yes, per list | D-06 |
| C08 | Tier | A risk tier, ordered by rank | CONFIGURATION | Has many Vocabulary Mappings, Review Cadences, Gate Policies, Approval Rules | Three tiers | D-06 |
| C09 | Tier Question | A plain question that suggests a tier | CONFIGURATION | Has many Tier Answer Options; optional filter by Application Kind | Five questions (data change, sensitivity, reach, agent autonomy, failure impact; document 21, section 6.3) | D-06 |
| C10 | Tier Answer Option | One allowed answer and the tier it implies | CONFIGURATION | To Tier Question (many to one); to Tier (many to one) | Yes | D-06 |
| C11 | Vocabulary Mapping | Joins one product taxonomy row to a Microsoft or framework term | CONFIGURATION | Points to one taxonomy row by entity and key; vocabulary name from a Value List | Default tiers to support tiers [S4], zones [S5], agent risk tiers [S2] | D-06 |
| C12 | Lifecycle Stage | A stage between idea and retirement | CONFIGURATION | Has many Gates; leading Owner Role or Role | Stages 0 to 10 | D-01, D-06 |
| C13 | Gate | A decision point after a stage | CONFIGURATION | To Lifecycle Stage (many to one); to Decision Type (many to one); has many Gate Requirements | Accept, Commit, Design, Go-live, Value | D-01, D-06 |
| C14 | Gate Requirement | What must be true before a gate decision can be accepted | CONFIGURATION | To Gate; optional Tier and Application Kind scope; target Decision Type, Document Type or Review Type | Yes, per default tier | D-06, D-17 |
| C15 | Application Kind | The category of a business application | CONFIGURATION | Used by O02, C09, C14, C23, C31, C32 | Power Platform app, agent, AI-built app, code app, runtime app, SaaS, external system, API, MCP server, practice app (I-02; sweep proposal for runtime apps) | D-06 |
| C16 | Component Kind | The type of a technical component | CONFIGURATION | Used by O03, O23 | Canvas app, model-driven app, code app, cloud flow, agent flow, Copilot Studio agent, Agent Builder agent, Copilot managed runtime app, App Builder app, Workflows agent flow, Power Pages site, solution [S6]; Fabric app, Power BI data app, CLI-deployed agent, Entra agent identity, Agent 365 registry entry, repository, Azure resource (sweep proposal for the non-inventory kinds) | D-06, D-15 |
| C17 | Dependency Type | The kind of a dependency | CONFIGURATION | Used by O04 | reads data from, writes data to, calls API of, hosted on, run-time need in target (I-194), replaces (sweep proposal) | D-06 |
| C18 | Decision Type | The kind of a decision | CONFIGURATION | Has many Decision Outcomes, Decision Rights, Approval Rules | intake, priority, design, data use, tier, risk acceptance, acceptance, change authorization, go-live, ownership, portfolio, exception, retirement, autonomy level, configuration change, gate policy change | D-01, D-06 |
| C19 | Decision Outcome | One allowed result for a decision type | CONFIGURATION | To Decision Type (many to one); optional Next Lifecycle Stage | Yes | D-06 |
| C20 | Change Type | The kind of a change | CONFIGURATION | Has many Standard Change Models, Gate Policies | standard, normal, emergency | D-06, D-08 |
| C21 | Standard Change Model | A pre-approved pattern for a standard change | CONFIGURATION | To Change Type; optional Tier; approved by a Decision | None (the client lists its own) | D-08 |
| C22 | Document Type | A kind of document | CONFIGURATION | Used by C23, O19; default Link Kind | wiki page, runbook, architecture decision record, support guide | D-17 |
| C23 | Documentation Requirement | Which document type an application needs, by kind, tier and stage | CONFIGURATION | To Document Type; optional Tier, Application Kind, Lifecycle Stage | Yes, per default tier | D-17 |
| C24 | Review Type | The kind of a review | CONFIGURATION | Has many Review Cadences; produces a Decision Type | owner check, tier check, access check (linked to Entra results), benefit, architecture, post-change | D-01, D-06 |
| C25 | Review Cadence | How often a review type is due at a tier | CONFIGURATION | To Review Type; to Tier | Yearly, twice a year, quarterly for the default tiers (document 21, section 6.3) | D-06 |
| C26 | Reminder Rule | When to remind and when to escalate to the deputy | CONFIGURATION | Applies to a subject kind (review, document, approval, claim) | Yes | D-06 |
| C27 | Party | One of the seven parties | CONFIGURATION | Has many Roles and Owner Roles | The seven parties of D-01 | D-01 |
| C28 | Owner Role | A responsibility for one subject: a business application, a data domain or a data location (D-37) | CONFIGURATION | To Party; used by O06, C31, C32 | business owner, sponsor, data owner, technical owner, builder, support owner, security owner, process owner (tier 3 agents [S2]); data steward (data domains only, D-37) | D-01, D-37 |
| C29 | Role | A product-wide responsibility. A human role is held only through an Entra group; a workload role only through a workload grant | CONFIGURATION | To Party; has Role Group Bindings (human roles) or Workload Grants (workload roles); used by C31, C32, C40 | 15 human roles: requester, maker, application owner, data owner, architect, change manager, change board, PMO, AI lead (D-20), platform admin, configuration admin (D-10), security reviewer, auditor (read only), reader (read only), data governance lead (D-41, question 4). 2 workload roles: deployment recorder, approval writer (working default, 28 issue 7) | D-11, D-20, D-41 |
| C30 | Role Group Binding | Binds one human Role to one Entra group and says how the platform applies it | CONFIGURATION | To Role (many to one); to Principal Reference of kind group (many to one). Groups only (V35); workloads use C42 | None (the setup service creates groups, D-11) | D-11 |
| C31 | Decision Right | Who is responsible, accountable, consulted or informed for a decision type in a scope | CONFIGURATION | To Decision Type; optional Tier, Lifecycle Stage, Change Type, Application Kind, Portfolio scope; to an Owner Role or a Role. The single most specific matching rule group applies | The default matrix (document 21, section 6.3), with the change manager accountable for tier 3 and emergency changes (working default, 28 issue 4) | D-07 |
| C32 | Approval Rule | Which approvals a decision type needs in a scope | CONFIGURATION | To Decision Type; optional scope as C31 plus Target Stage; to an Owner Role or a Role. The single most specific matching rule group applies (working default, 28 issue 2) | Yes, per default tier. Combined cases, such as a tier 3 emergency, get their own group. Tier 3 and emergency change authorization go to the change manager; the change board gets exceptions and windows that cover several applications (working default, 28 issue 4) | D-07 |
| C33 | Gate Policy | The gate level for one tier, change type and target stage | CONFIGURATION | To Tier, Change Type, Target Stage; optional Portfolio; set by a Decision | From the chosen preset (D-03 is open). Every combination has a row; a missing row is a broken configuration, not a fallback (working default, 28 issue 14) | D-03 |
| C34 | Target Stage | The ALM stage of a deployment target | CONFIGURATION | Used by O15, C32, C33 | development, test, production | D-03 |
| C35 | Change Path | The routes a target expects deployments to arrive by | CONFIGURATION | Allows many Deployment Routes; used by O15 | pipeline only; pipeline or AI tool; any route | D-03, D-19 |
| C36 | Deployment Route | How a deployment record arrived | CONFIGURATION | Used by O14, C35 | pipeline step, AI tool, person in app, other | D-19 |
| C37 | Link Kind | What a link points to, by tool and category | CONFIGURATION | Used by O03, O15, O16, O18, O19, O20 | Azure DevOps work item, repository, wiki page, Azure Pipelines pipeline; GitHub repository, issue, project, Actions workflow; Power Platform Pipelines pipeline; Planner unified plan, Planner premium plan (fit 27); SharePoint page; ITSM change; Copilot Studio CLI deployment, runtime GitHub Actions deploy, runtime platform deploy (sweep proposal); Dataverse environment, Azure data resource, SharePoint location, Fabric item and external data system, of the new category data location (D-37) | D-12, D-19, D-37 |
| C38 | Actor Kind | Who or what acted | CONFIGURATION | Used by every row's performed-by columns and by O10 | One row per engine meaning | D-10 |
| C39 | Guideline | One version of one numbered guideline, with local override | CONFIGURATION | Optional Supersedes (to C39); cited by C14, C32, C23 | G1 to G19 | D-01, D-10 |
| C40 | Separation Rule | Who must not be the same person for an approval, or which roles may not share an Entra group | CONFIGURATION | Optional Decision Type, Tier and Target Stage scope; optional Owner Role or Role to differ from; optional exception Decision Type. The engine reads it when it counts an approval | Per preset: Balanced refuses self-approval at tier 3 and flags a deployer who is the only approver. Regulated and the strict separation overlay refuse self-approval at every tier and need a second configuration admin. The strict separation overlay also keeps the AI lead and platform admin in different groups. Starter and the small team overlay allow self-approval only through a recorded exception decision (working default, 28 Q10) | D-20 (working default, 28 issue 5) |
| C42 | Workload Grant | Gives one non-human principal one workload role, with a reason, the deciding decision and an expiry | CONFIGURATION | To Principal Reference of kind service principal or managed identity (many to one); to Role with Is Workload Role = Yes (many to one); to the granting Decision | None (the setup service adds one per pipeline identity and one for the approval runner) | D-11, D-19 (working default, 28 issue 8; open design question 12, see 28-working-defaults.md) |
| C46 | Configuration Draft | A proposed change to the working set, held as a patch until people accept its configuration change decision and the engine activates it | CONFIGURATION | To Configuration Set (many to one); to the configuration change Decision (one to one, once submitted); to the resulting Configuration Set Version (one to one, once activated) | None (created by the draft tools and by product upgrades) | D-07, D-10 (working default, 28 issue 3) |
| C47 | Data Classification | A ranked sensitivity level in the tenant's Purview label names, with the default handling rules for data of that level | CONFIGURATION | Used by C49 and, from data taxonomy slice 2, C31 and C32; maps to Purview labels through C11 | Public, General, Confidential, Highly Confidential (Purview's default names; Starter has three) | D-37 |
| C48 | Data Domain Level | One level of the business data domain tree of an install, with its rules | CONFIGURATION | Used by C49 | Domain, subdomain, data set (Starter: two) | D-37 |
| C49 | Data Domain | One node of the business data domain tree each install draws for itself | CONFIGURATION | Optional Parent Domain (self); to Level, Classification, Retention Class (List Value); used by O26, O27, O06 and O09 subjects | None in a full preset; the overlay data domains starter holds a generic tree to rename | D-37 |
| C50 | Data Platform | A kind of data store from any vendor | CONFIGURATION | To Location Kind and Vendor (List Values), Default Link Kind; used by O25 | About twenty, from Dataverse and Azure SQL to Salesforce, Workday and IBM i | D-37 |


#### 2.5.2 Extra columns: configuration core

| Entity | Column | Description |
|---|---|---|
| C01 Configuration Set | Set Kind | Preset or working (engine contract) |
| C01 Configuration Set | Based On Preset | The preset a working set was copied from |
| C01 Configuration Set | Is In Force | Yes for the one working set the engine reads. A client has one; areas use the portfolio scope on rule rows (working default, 28 Q2) |
| C01 Configuration Set | Current Version Number | The active version's number. The one value the engine's version probe reads. The active C02 row is the one with this number (working default, 28 issue 15) |
| C02 Configuration Set Version | Configuration Set | The set this snapshot belongs to |
| C02 Configuration Set Version | Version Number | A whole number that only goes up, assigned at activation |
| C02 Configuration Set Version | Source Draft | The Configuration Draft this version activated. Empty only when the bootstrap rule creates version 1 (document 21, section 4.8) |
| C02 Configuration Set Version | Base Version Number | The version this one was built on |
| C02 Configuration Set Version | Change Json | The patch applied, copied from the draft |
| C02 Configuration Set Version | Content Json | The full snapshot of every row in the set, as JSON |
| C02 Configuration Set Version | Content Hash | A hash of the snapshot, so a record can prove which rules applied |
| C02 Configuration Set Version | Validation Json | The validation result inside the activation transaction, with any warnings the approver acknowledged |
| C02 Configuration Set Version | Snapshot Schema Version | The format version of Content Json, so a newer engine can still read an old snapshot (working default, 28 issue 15) |
| C02 Configuration Set Version | Applied Presets Json | The full preset and overlays applied, each with its content version. Set on version 1 and on any re-base |
| C02 Configuration Set Version | Published On | When this version came into force, at activation |
| C02 Configuration Set Version | Instructed By | The admin who decided the change (D-10) |
| C02 Configuration Set Version | Change Decision | The configuration change or gate policy change decision behind this version |
| C02 Configuration Set Version | Change Summary | A plain list of what changed from the previous version, computed by the engine |
| C02 Configuration Set Version | Seed Version | The product release whose seed this version started from, if any |
| C03 Preset | Preset Key | Stable key: `starter`, `balanced`, `regulated`, `microsoft-coe-baseline`, and the overlays `small-team`, `strict-separation`, `linked-change-management` |
| C03 Preset | Product Release | The product release that shipped this preset |
| C03 Preset | Preset Kind | Full or overlay (engine contract; working default, 28 Q3) |
| C04 Setting | Value Json | The setting's value as JSON |
| C04 Setting | Value Type | The expected shape of the value, such as text, yes or no, or a key from another entity |
| C04 Setting | Is Locked | Yes when the engine refuses any change by configuration, such as the AI approval lock (D-02, D-10) |
| C04 Setting | Portfolio | Optional scope, used only by the change management mode. A portfolio row repeats the setting's key; Key plus Portfolio is unique. The most specific row wins (working default, 28 Q2) |
| C46 Configuration Draft | Record Number | A number such as CFG-00012, assigned by the engine. Tools and pages name a draft by it |
| C46 Configuration Draft | Configuration Set | The working set the draft would change |
| C46 Configuration Draft | Draft Status | Draft, submitted, activated or discarded (engine contract). Editable only while draft; frozen on submit |
| C46 Configuration Draft | Base Version Number | The active version the draft started from. The engine rebases at activation when no operation overlaps, and refuses when one does |
| C46 Configuration Draft | Change Json | The patch: ordered upsert and retire operations by entity and key |
| C46 Configuration Draft | Preview Content Json | The snapshot the set would have if activated. Shown for review; never read as live configuration |
| C46 Configuration Draft | Validation Json | The last validation result: errors and warnings with rule IDs |
| C46 Configuration Draft | Change Summary | A plain list of what would change, computed by the engine from the diff. Never the AI's own text |
| C46 Configuration Draft | Proposal Rationale | Why the change is wanted, in the words of the admin or the AI that drafted it |
| C46 Configuration Draft | Applied Presets Json | The full preset and overlays applied, for a preset apply or re-base draft |
| C46 Configuration Draft | Instructed By | The admin who instructed the change (D-10) |
| C46 Configuration Draft | Change Decision | The configuration change or gate policy change decision proposed on submit |
| C46 Configuration Draft | Submitted On | When the draft was submitted |
| C46 Configuration Draft | Expires On | When an unsubmitted draft is discarded, from the `draft-expiry-days` setting |
| C46 Configuration Draft | Resulting Version | The Configuration Set Version created at activation |
| C46 Configuration Draft | Discarded On | When the engine discarded it |
| C46 Configuration Draft | Discard Reason | Why the draft was discarded: withdrawn, rejected, expired or a rebase conflict |

Rules for C02 and C46 (working default, 28 issue 3):

- C02 has no status column. The active version is the one whose number equals C01 Current Version Number. Every lower number is superseded, which is derived, never written.
- The engine writes C46 rows through the draft tools, as the caller. A draft never changes the live rows.
- Activation writes the live configuration rows and a new C02 row in one transaction, and only after people accept the change decision. It is the only write path to configuration tables on both bindings.
| C05 Status | Applies To Entity | The entity this status belongs to, such as Decision |
| C05 Status | Engine Meaning | The fixed meaning (section 2.4) |
| C06 Value List | List Key | Stable key of the list, such as `horizon` |
| C06 Value List | Is Extensible | Yes when clients may add values |
| C07 List Value | Value List | The list this value belongs to |

#### 2.5.3 Extra columns: taxonomy

| Entity | Column | Description |
|---|---|---|
| C08 Tier | Rank | Higher means more serious. The engine compares ranks, never tier numbers or labels |
| C09 Tier Question | Question Text | The question as people read it |
| C09 Tier Question | Help Text | A short explanation with an example |
| C09 Tier Question | Applies To Kinds | Keys of the application kinds that see this question; empty means all |
| C10 Tier Answer Option | Tier Question | The question this answer belongs to |
| C10 Tier Answer Option | Answer Text | The answer as people read it |
| C10 Tier Answer Option | Implied Tier | The tier this answer suggests. The most serious answer wins |
| C10 Tier Answer Option | Triggers Json | Review types or gate requirements this answer adds, such as a security review |
| C11 Vocabulary Mapping | Source Entity | The taxonomy entity being mapped, such as Tier |
| C11 Vocabulary Mapping | Source Key | The key of the row being mapped |
| C11 Vocabulary Mapping | Vocabulary | Which outside vocabulary, from the `vocabulary` value list |
| C11 Vocabulary Mapping | Term | The outside term, such as "Critical" or "Zone 3" |
| C11 Vocabulary Mapping | Source Url | Where the term is defined |
| C11 Vocabulary Mapping | Checked On | When the term was last checked at its source |
| C12 Lifecycle Stage | Stage Number | Display number, 0 to 10 by default. Never used as logic |
| C12 Lifecycle Stage | Stage Rank | Orders stages so the engine can test "at or after a stage". Unique within the set. Like Tier Rank, the engine compares ranks, never numbers or labels (working default, 28 issue 15) |
| C12 Lifecycle Stage | Engine Meaning | Pre-live, live or retired (section 2.4) |
| C12 Lifecycle Stage | Leading Owner Role | The owner role that leads work in this stage, if relative to the application |
| C12 Lifecycle Stage | Leading Role | The product role that leads work in this stage, if organization-wide |
| C13 Gate | After Stage | The lifecycle stage this gate follows |
| C13 Gate | Gate Decision Type | The decision type that records passing the gate |
| C14 Gate Requirement | Gate | The gate this requirement belongs to |
| C14 Gate Requirement | Tier | Optional: applies only at this tier |
| C14 Gate Requirement | Application Kind | Optional: applies only to this kind |
| C14 Gate Requirement | Requirement Kind | Decision accepted, document current or review done (engine contract) |
| C14 Gate Requirement | Required Decision Type | The decision type that must be accepted, when the kind is decision accepted |
| C14 Gate Requirement | Required Document Type | The document type that must be current, when the kind is document current |
| C14 Gate Requirement | Required Review Type | The review type that must be done, when the kind is review done |
| C14 Gate Requirement | Guideline | The guideline this requirement applies |
| C15 Application Kind | Category | A grouping label from the `application-category` value list |
| C15 Application Kind | Default Component Kinds | Keys of component kinds usually found in this kind of application |
| C16 Component Kind | Source Tool | The tool that holds this kind, from the `tool` value list |
| C16 Component Kind | Source Type Hint | The tool's own type name, used to match gap check results |
| C16 Component Kind | Is Gap Check Covered | Yes when the gap check can see this kind [S6] |
| C17 Dependency Type | Inverse Label | The label read from the other side, such as "used by" |
| C17 Dependency Type | Counts For Impact | Yes when the impact walk follows this type |
| C18 Decision Type | Needs Options Considered | Yes when the decision must list the options it rejected, as an ADR does [S1] |
| C18 Decision Type | Default Subject Kind | The entity this decision type is usually about |
| C18 Decision Type | Requires Approval | Yes when the approval rules (C32) decide who must approve. No sends one card to the holder of the accountable letter (C31). The engine accepts with no card only what the locked list allows (working default, 28 issue 1; open design question 8, see 28-working-defaults.md) |
| C19 Decision Outcome | Decision Type | The decision type this outcome belongs to |
| C19 Decision Outcome | Next Lifecycle Stage | Optional: the stage an accepted decision with this outcome moves the application to |
| C20 Change Type | Requires Authorization | Yes when a change of this type needs a change authorization decision |
| C20 Change Type | Allows Authorization After | Yes when authorization may be completed after the change, as for emergencies |
| C20 Change Type | Requires Review After | Yes when a post-change review is due |
| C20 Change Type | Review Due Days | Working days until the post-change review is due |
| C20 Change Type | Requires Standard Model | Yes when a change of this type must name an active standard change model (C21), which stands in for a person's authorization. The engine records the authorization itself when the model's conditions hold, under the locked list (working default, open design question 8, see 28-working-defaults.md) |
| C21 Standard Change Model | Change Type | The standard change type this model belongs to |
| C21 Standard Change Model | Tier | Optional: the highest tier the model applies to |
| C21 Standard Change Model | Conditions Json | The conditions that keep a change standard (condition kinds in document 25, OP-12) |
| C21 Standard Change Model | Approved By Decision | The decision that listed this model |
| C22 Document Type | Default Link Kind | The link kind usually used, such as wiki page |
| C22 Document Type | Freshness Days | Days after the last check when a current document becomes stale |
| C22 Document Type | Template Url | A link to a template in the playbook |
| C22 Document Type | Current Needs Person | Yes when "current" set by an AI client needs a person's confirmation card (working default, 28 issue 15; open design question 9, see 28-working-defaults.md) |
| C23 Documentation Requirement | Document Type | The document type required |
| C23 Documentation Requirement | Tier | Optional: applies only at this tier |
| C23 Documentation Requirement | Application Kind | Optional: applies only to this kind |
| C23 Documentation Requirement | From Stage | The lifecycle stage from which the document is required |
| C24 Review Type | Produces Decision Type | The decision type a finished review records |
| C24 Review Type | Default Owner Role | The owner role that usually leads this review |
| C25 Review Cadence | Review Type | The review type this cadence is for |
| C25 Review Cadence | Tier | The tier this cadence is for |
| C25 Review Cadence | Interval Days | Days between reviews |
| C25 Review Cadence | Lead Days | Days before the due date when the first reminder goes |
| C25 Review Cadence | Escalate After Days | Days after the due date when the deputy is asked |
| C26 Reminder Rule | Subject Kind | What is due: review, document, approval or claim |
| C26 Reminder Rule | Lead Days | Days before due when the first reminder goes |
| C26 Reminder Rule | Repeat Days | Days between repeat reminders |
| C26 Reminder Rule | Escalate After Days | Days after due when the deputy is asked |
| C26 Reminder Rule | Channel | Where the reminder goes, from the `channel` value list |
| C27 Party | (taxonomy columns only) | |
| C28 Owner Role | Party | The party this owner role belongs to |
| C28 Owner Role | Requires Deputy | Yes when every holder must have a deputy |
| C28 Owner Role | Maximum Holders | Optional: how many principals may hold it for one application at once |
| C34 Target Stage | Rank | Order from development to production. Never used as logic beyond ordering |
| C35 Change Path | Allowed Routes | Keys of the deployment routes this path accepts |
| C36 Deployment Route | Engine Meaning | Pipeline step, AI tool, person in app or other (section 2.4) |
| C37 Link Kind | Link Category | Delivery, pipeline, document, evidence, Microsoft resource or external change (section 2.4) |
| C37 Link Kind | Tool | The tool, from the `tool` value list |
| C37 Link Kind | Url Pattern | A pattern that a valid URL of this kind matches |
| C37 Link Kind | Id Pattern | A pattern that a valid ID of this kind matches |
| C38 Actor Kind | Engine Meaning | One of the six actor meanings (section 2.4). Only "person" can approve. The approval runner records an approval but never approves |
| C39 Guideline | Number | The permanent guideline number, such as G9 |
| C39 Guideline | Version | Two-part version: the first part changes the rule, the second only the wording |
| C39 Guideline | Rule | The rule in one line |
| C39 Guideline | Hub Url | The link to the canonical text |
| C39 Guideline | Chapter | The playbook chapter this guideline belongs to |
| C39 Guideline | Local Text | The client's own wording, if any |
| C39 Guideline | Local Url | The client's own page, if any |
| C39 Guideline | Is Adopted | No when the client chose not to adopt it |
| C39 Guideline | Not Adopted Reason | Why the client did not adopt it |
| C39 Guideline | Supersedes | The earlier version this row replaces |

#### 2.5.4 Extra columns: rules

| Entity | Column | Description |
|---|---|---|
| C29 Role | Party | The party this role belongs to |
| C29 Role | Duties | The role's duties in plain text, for people and agents |
| C29 Role | Can Change Configuration | Yes only for the configuration admin role (D-10). It may instruct configuration drafts and accept configuration change decisions. It gives no write access to configuration tables; only activation writes them (working default, 28 issue 3) |
| C29 Role | Is Read Only | Yes for roles that never write, such as auditor and reader |
| C29 Role | Is Workload Role | Yes for roles held by non-human workloads, not people: deployment recorder and approval writer. A workload role is held only through a Workload Grant (C42), never through a group, and can never approve (working default, 28 issue 7; open design question 12, see 28-working-defaults.md). Proposed by the build (2026-11-22): two more, authorization reporter and consistency checker (B20-2 at the end of this document) |
| C30 Role Group Binding | Role | The human product role being bound. A workload role is refused (V35) |
| C30 Role Group Binding | Entra Group | The group, as a principal reference of kind group |
| C30 Role Group Binding | Binding Method | Dataverse group team, or Entra app role assignment (section 2.9) |
| C30 Role Group Binding | Binding Detail | The team ID or app role value on the platform |
| C30 Role Group Binding | Last Verified On | When the binding was last checked to exist on the platform |
| C31 Decision Right | Decision Type | The decision type this right is for |
| C31 Decision Right | Tier | Optional scope |
| C31 Decision Right | Lifecycle Stage | Optional scope |
| C31 Decision Right | Change Type | Optional scope |
| C31 Decision Right | Application Kind | Optional scope |
| C31 Decision Right | Portfolio | Optional scope (working default, 28 Q2) |
| C31 Decision Right | Letter | Responsible, accountable, consulted, informed or supports (engine contract) |
| C31 Decision Right | Owner Role | The owner role that holds the letter, when relative to the application |
| C31 Decision Right | Role | The product role that holds the letter, when organization-wide |
| C32 Approval Rule | Decision Type | The decision type that needs this approval |
| C32 Approval Rule | Tier, Lifecycle Stage, Change Type, Application Kind, Target Stage, Portfolio | Optional scope columns, each one a lookup. Rows with the same decision type and the same scope form one rule group. The engine applies the single most specific matching group whole, and broader groups drop out (see the precedence rule below) |
| C32 Approval Rule | Approver Owner Role | The owner role that must approve, when relative to the application |
| C32 Approval Rule | Approver Role | The product role that must approve, when organization-wide |
| C32 Approval Rule | Approval Mode | Everyone must approve, first to respond or sequential [S10] |
| C32 Approval Rule | Sequence | Order among rules in the same group when the mode is sequential |
| C32 Approval Rule | Minimum Count | How many approvals from this role are needed. At least 1. There is no waiver by a count of 0 (working default, 28 issue 1) |
| C32 Approval Rule | Is Consultation | Yes when the card asks a consulted role for input. The answer is stored as recommendation only and never counts toward a requirement (working default, 28 issues 12 and 15) |
| C32 Approval Rule | Timeout Days | Days before the approval expires and is restarted or escalated |
| C32 Approval Rule | Requires Typed Reason | Yes when the approver must type a reason |
| C32 Approval Rule | Guideline | The guideline this rule applies |
| C33 Gate Policy | Tier | The tier this row is for |
| C33 Gate Policy | Change Type | The change type this row is for |
| C33 Gate Policy | Target Stage | The target stage this row is for |
| C33 Gate Policy | Portfolio | Optional scope (working default, 28 Q2) |
| C33 Gate Policy | Gate Level | Off, record, flag, advise or enforce |
| C33 Gate Policy | Fail Mode | Fail open or fail closed; used only at enforce |
| C33 Gate Policy | Emergency Rule Json | How an emergency passes, and when its approval must follow |
| C33 Gate Policy | AI Only Code Approval Handling | Optional. What a deployment gets when its only code approval came from an AI: ignore, flag, or stop at enforce (engine contract; sweep proposal). When empty, the `default-ai-only-code-approval-handling` setting applies, and it ships at flag. AI-only code approvals never count as authorization (working default, open design question 7, see 28-working-defaults.md) |
| C33 Gate Policy | Set By Decision | The gate policy change decision behind the version that set this row |
| C40 Separation Rule | Rule Kind | Approver differs, or roles in different groups (engine contract) |
| C40 Separation Rule | Decision Type | Optional: applies to approvals for this decision type. Empty means every decision type that needs approval |
| C40 Separation Rule | Tier | Optional: applies only at this tier |
| C40 Separation Rule | Target Stage | Optional: applies only for this target stage |
| C40 Separation Rule | Must Differ From | For approver differs: the proposer, the instructing admin of a configuration change, the deployer of the change, an owner role holder or a role holder (engine contract) |
| C40 Separation Rule | Must Differ From Owner Role | Set when Must Differ From is an owner role holder |
| C40 Separation Rule | Must Differ From Role | Set when Must Differ From is a role holder |
| C40 Separation Rule | Roles | For roles in different groups: keys of the roles that may not share an Entra group |
| C40 Separation Rule | Enforcement | Refuse, allow only with an accepted exception decision, or allow and flag (engine contract) |
| C40 Separation Rule | Exception Decision Type | The decision type that records an exception, when enforcement is exception decision |
| C42 Workload Grant | Principal | The non-human principal: a Principal Reference of kind service principal or managed identity. Never an agent identity or agent user |
| C42 Workload Grant | Workload Role | The role it holds: a C29 row with Is Workload Role = Yes, such as deployment recorder or approval writer |
| C42 Workload Grant | Reason | Why this principal needs the role, in plain words, such as "Release pipeline of the Expense app" |
| C42 Workload Grant | Uses Federated Credential | Yes when the principal signs in without a stored secret (workload identity federation or a managed identity). No is flagged |
| C42 Workload Grant | Binding Detail | The application user ID on Dataverse, or the app role assignment ID on Azure |
| C42 Workload Grant | Granted By Decision | The decision that allowed the grant |
| C42 Workload Grant | Expires On | When the grant ends. Required: every grant expires, at most the `workload-grant-max-days` setting (default 365) after the day it is validated (document 21, V28), and is renewed by a new decision. An expired grant admits no app-only token (document 26, RA-07) and is flagged (document 26, CHK-09) |
| C42 Workload Grant | Last Verified On | When the access check last confirmed the grant matches the platform (document 26, CHK-09) |

**Precedence rule for C31 and C32 (working default, 28 issue 2).** A rule group is the set of rows of one entity with the same decision type and the same scope values. Specificity is the number of scope fields a group sets. For a record, the engine finds every group whose set scope fields all match, and applies the one with the highest specificity, whole. Broader groups do not add to it. Ties are refused at validation (V26), so the engine never needs a hidden tie-break. A combined case, such as a tier 3 emergency change, needs its own group in the seed. The same "most specific row wins" rule applies to the portfolio scope on C33 and on the change management mode setting (working default, 28 Q2).

**Separation of duties (working default, 28 issue 5).** Separation of duties lives only in C40. Approval Rule carries no separation columns. When the engine counts an approval, it reads C40 for the decision's type, tier and target stage.

### 2.6 Operational entities

#### 2.6.1 Summary

| ID | Logical name | Purpose | Class | Key relationships | When | From |
|---|---|---|---|---|---|---|
| O01 | Portfolio | A business grouping of applications; nests | OPERATIONAL | Optional Parent Portfolio (self); level label from the `portfolio-level` value list; has many Business Applications | R1 | D-01 |
| O02 | Business Application | The register's central record | OPERATIONAL | To Portfolio, Application Kind, Tier, Lifecycle Stage, Status; has many Technical Components, Owner Assignments, Dependencies, Decisions, Change Requests, Deployment Records, Delivery Links, Document Links, Reviews | R1 | D-01 |
| O03 | Technical Component | One part of an application in a tool, as a link | OPERATIONAL | To Business Application, Component Kind, Link Kind (Microsoft resource), optional Deployment Target; optional Replaces Component (self, sweep proposal) | R1 | D-15 |
| O04 | Dependency | A typed, directed link between applications or to a component | OPERATIONAL | From Business Application; to exactly one of Business Application or Technical Component; to Dependency Type | R1 | D-01 |
| O05 | Principal Reference | A minimal reference to a directory object (user, group, agent identity, service principal) | OPERATIONAL | Referenced by owner, approver, decider, requester, deployer and performed-by columns; by Role Group Binding and Workload Grant | R1 | D-11, D-15 |
| O06 | Owner Assignment | One principal holding one owner role for one subject: an application, a data domain or a data location (D-37) | OPERATIONAL | Exactly one of Business Application, Subject Data Domain, Subject Data Location; to Owner Role, Principal Reference; optional Deputy For (self) | R1 | D-01, D-37 |
| O07 | Intake Request | One request through the front door | OPERATIONAL | To Principal Reference (requester); optional Business Application; to Status; has Tier Assessments and Decisions | R1 | D-01 |
| O08 | Tier Assessment | One set of tier answers and the tier they imply | OPERATIONAL | To Intake Request or Business Application; to Tier (computed); to the tier Decision | R1 | D-06 |
| O09 | Decision | One append-only entry in the decision log | OPERATIONAL | To Decision Type, Decision Outcome, Status, decider Principal Reference; one subject (application, intake, change request, deployment, component, claim, configuration draft, data use, data mapping, data domain, data location); optional Supersedes and Amends (self); has Approval Records and Evidence Links. Configuration change and gate policy change decisions are about a Configuration Draft (working default, 28 issue 3) | R1 | D-01 |
| O10 | Approval Record | One required approval for one decision | OPERATIONAL | To Decision, Approval Rule, approver Principal Reference, Actor Kind, Status | R1 | D-02, D-10 |
| O11 | Change Request | One planned change | OPERATIONAL | To Business Application (main), Change Type, optional Standard Change Model, Status, optional planned Deployment Target; to the authorization Decision; has Affected Applications, Deployment Records, Evidence Links | R1 | D-08, D-13 |
| O12 | Affected Application | A further application a change touches | OPERATIONAL | To Change Request; to Business Application | R1 | D-08 |
| O13 | Artifact Version | One version of one deployable artifact | OPERATIONAL | To Business Application (main); has many Deployment Records | R1 | D-19 |
| O14 | Deployment Record | One artifact version reaching one target | OPERATIONAL | To Business Application, Artifact Version, Deployment Target, Deployment Route, Status; optional Change Request, Pipeline Reference; optional Tested In (self, later) | R1 | D-13, D-19 |
| O15 | Deployment Target | A place deployments land, as a link | OPERATIONAL | To Target Stage, Change Path, target kind (List Value), Link Kind (Microsoft resource) | R1 | D-15, D-19 |
| O16 | Pipeline Reference | A link to one client pipeline | OPERATIONAL | To Link Kind (pipeline category); has Pipeline Map Entries and Deployment Records | R1 | D-19 |
| O17 | Pipeline Map Entry | This pipeline deploys this application to this target | OPERATIONAL | To Business Application, Pipeline Reference, Deployment Target | R1 | D-19 |
| O18 | Delivery Link | A link from an application, change or intake to work elsewhere | OPERATIONAL | One subject (Business Application, Change Request or Intake Request); to Link Kind (delivery category) | R1 | D-12 |
| O19 | Document Link | One recorded document for one application, with status. "Missing" is computed, never a row (working default, 28 issue 16) | OPERATIONAL | To Business Application, optional Documentation Requirement, Document Type, Status, owner Principal Reference, Link Kind (document category) | R1 | D-17 |
| O20 | Evidence Link | A link from a record to proof held elsewhere | OPERATIONAL | One subject (Decision, Approval Record, Change Request, Deployment Record, Review, Claim); to Link Kind (evidence category); evidence level (List Value) | R1 | D-08, D-15 |
| O21 | Review | One periodic or event check on an application | OPERATIONAL | To Business Application, Review Type, Status, reviewer Principal Reference; to the outcome Decision | R1 | D-01 |
| O22 | Gap Check Run | One execution of the gap check | OPERATIONAL | To Principal Reference (the admin); has many Claims | R1 | D-15 |
| O23 | Claim | One item on the claim list | OPERATIONAL | To Gap Check Run, Component Kind, Status; optional suggested claimant and claimed-by Principal References; optional resulting Technical Component | R1 | D-15 |
| O24 | Audit Entry | One product event, append-only | OPERATIONAL | To the row it is about (entity name plus row ID); to actor Principal Reference, Actor Kind | R1 (scope differs per platform, section 2.9) | D-10, D-22 |
| O25 | Data Location | One concrete data store from any vendor, held as a link | OPERATIONAL | To Data Platform, Link Kind (data location), Status; optional Parent Location (self, two levels at most), Target Stage, Deployment Target, Hosted By Application | R1 | D-37 |
| O26 | Data Mapping | This domain's data sits at this location, in these containers, with this origin | OPERATIONAL | To Data Domain, Data Location, Status; optional Copied From (self) | R1 | D-37 |
| O27 | Data Use | This application reads or writes this data domain, at this location, for this purpose, with or without AI | OPERATIONAL | To Business Application, Data Domain, Status; optional Data Location, AI Component, Access Approach (List Value) | R1 | D-37 |


#### 2.6.2 Columns per operational entity

Common columns (section 2.2) apply to every entity below and are not repeated.

**O01 Portfolio**

| Column | Description |
|---|---|
| Parent Portfolio | The portfolio this one sits inside; empty at the top |
| Level | The level label, such as portfolio or application group, from the `portfolio-level` value list |
| Accountable Principal | The person who answers for the portfolio, if the client names one |

**O02 Business Application**

| Column | Description |
|---|---|
| Purpose | What the application is for, in one or two plain sentences |
| Application Kind | Its category |
| Portfolio | Where it sits in the business grouping |
| Status | Registration state: draft (for example AI-created and not yet confirmed), registered or archived |
| Tier | The current tier, a stored value |
| Tier Decision | The accepted decision that set the current tier |
| Lifecycle Stage | The current stage |
| Stage Decision | The accepted decision that moved it to this stage |
| Stage Since | When it entered the current stage |
| Build Path | Citizen, fusion, pro or AI-built |
| Uses AI | The owner's statement that it uses AI models or tools, with links to Microsoft's views (problem row 25) |
| Agreed Autonomy Level | For agents: the autonomy level agreed by decision. Enforcing it stays in Microsoft's tools |
| Autonomy Decision | The decision that agreed the autonomy level |
| Next Review On | The next due date from the review cadences |

**O03 Technical Component**

| Column | Description |
|---|---|
| Business Application | The application this part belongs to |
| Component Kind | Its type |
| Link Kind | The Microsoft resource or tool link kind |
| External Id | The tool's own ID, such as a Microsoft resource ID or an Entra object ID |
| Url | A link that opens the item in its own tool |
| Label As Seen | The item's name when last seen. A label, not a copy of the tool's data |
| Deployment Target | Where the item lives, if it lives in a target |
| Last Seen On | When the product last confirmed it exists, by gap check or record step |
| Owner Differs | Yes when the gap check found Microsoft's listed owner differs from the register's owners (fit 8). Microsoft's owner is not stored |
| Owner Checked On | When the owner comparison last ran |
| Replaces Component | The component this one replaced, such as after a migration that creates a new app ID (sweep proposal) |
| Source Claim | The claim this component came from, if any |

**O04 Dependency**

| Column | Description |
|---|---|
| From Application | The application that depends |
| To Application | The application depended on, when the target is an application |
| To Component | The component depended on, when the target is a component |
| Dependency Type | The kind of dependency |
| Criticality | How badly the dependant is hit if the target fails, from the `criticality` value list |
| Notes | Any detail an architect needs |

Rule: exactly one of To Application and To Component is set.

**O05 Principal Reference**

| Column | Description |
|---|---|
| Principal Kind | User, group, agent identity, agent user, service principal or managed identity (engine contract) |
| Entra Object Id | The directory object ID. The only identity fact the product relies on |
| Display Label | A name to show people, refreshed when seen. Cleared on erasure, leaving "former user" |
| Last Seen On | When the principal was last seen active by the product |
| Directory State | Enabled, disabled, deleted or unknown, as last read from Entra by the owner check, with the date read (working default, 28 issue 15 and Q18) |
| Leaves On | The planned leave date, when an owner's leave is recorded or read from Entra (working default, 28 issue 15 and Q18) |
| Platform User | On the Dataverse binding: the matching built-in user row, when one exists |

Why this entity exists on both platforms: people who only answer approval cards may never open the product's environment, so a platform user row cannot be relied on. Dataverse creates group team users only when they access the environment [S7]. Keeping person facts in one table also lets append-only rows hold only an ID, as Microsoft advises for append-only stores that hold personal data [S21].

**O06 Owner Assignment**

| Column | Description |
|---|---|
| Business Application | The application |
| Owner Role | The responsibility held |
| Principal | Who holds it |
| Is Deputy | Yes when this row is the deputy |
| Deputy For | The main assignment this deputy covers |
| Start On | When the assignment starts |
| End On | When it ends; empty while current |
| Ownership Decision | The decision that made the assignment |

**O07 Intake Request**

| Column | Description |
|---|---|
| Requester | Who asked |
| Title | A short name for the request |
| Need | The business need in plain words |
| Proposed Kind | The application kind the requester expects |
| Business Application | The application this request is about, once known or created |
| Status | Draft, submitted, in progress, completed, rejected, withdrawn or cancelled (by meaning) |
| Duplicate Of | An existing application this request matches |
| Score | The PMO score for demand that needs central capacity |
| Horizon | Now, next or later |
| Size Band | A rough size |
| Expected Benefit | The benefit claimed at intake |
| Uses AI | The requester's statement that it will use AI |
| Triage Decision | The accepted triage decision |

**O08 Tier Assessment**

| Column | Description |
|---|---|
| Intake Request | The request assessed, when assessed at intake |
| Business Application | The application assessed, when assessed later |
| Answers Json | The question keys and answer keys given |
| Computed Tier | The most serious tier the answers imply |
| Assessed By | Who gave the answers |
| Tier Decision | The decision that set the tier from this assessment; a person may raise the tier, and lowering needs a decision |

**O09 Decision**

| Column | Description |
|---|---|
| Decision Type | The kind of decision |
| Subject Kind | Which entity the decision is about |
| Subject | The row the decision is about (one reference; section 2.9 shows how each platform holds it) |
| Statement | One sentence: "Decided: what" |
| Context | Why the decision was needed |
| Options Considered | The options weighed and rejected, when the decision type needs them [S1] |
| Outcome | The chosen outcome |
| Reason | Why this outcome |
| Status | Proposed, accepted, rejected, superseded or withdrawn (by meaning) |
| Decider | The accountable person |
| Decided On | When it was accepted |
| Supersedes | The earlier decision this one replaces |
| Amends | The earlier decision this one changes in part |
| Confidence | How sure the decider is, from the `confidence` value list [S1] |

Rule: once accepted, a decision is never edited. A change is a new decision that supersedes it [S1].

**O10 Approval Record**

| Column | Description |
|---|---|
| Decision | The decision this approval is for |
| Approval Rule | The rule that required it |
| Required Owner Role | The owner role asked, resolved from the rule |
| Required Role | The product role asked, resolved from the rule |
| Approver | Who answered |
| Approver Actor Kind | The actor kind of the answer. Only "person" counts (approver kind, sweep proposal) |
| Approver Principal Kind | The directory object type of the responder. Only "user" counts (sweep proposal) |
| Outcome | Pending, approved, rejected, expired, cancelled or recommendation only (by meaning) |
| Typed Reason | The approver's reason, in their words |
| Requested On | When the approval card went out |
| Responded On | When it was answered |
| Expires On | When it times out |
| Channel | Outlook, Teams, the app or another channel |
| Flow Run Url | A link to the approval runner's run that wrote the outcome |
| Counts Toward Requirement | Computed by the engine: yes only for an approved outcome from a person who holds the required role and passes the separation rules (C40). A consultation answer never counts |

Rule: only the approval writer (the approval runner's identity, through its workload grant) writes Outcome, Approver, Responded On, Typed Reason, Channel and Flow Run Url (D-10; working default, 28 issue 7).

**Proposed by the build (2026-11-22).** Three more columns (Decision Right, Is Consultation, Approver Holds Required Role) and "Approval Rule or Decision Right" are in B20-1 at the end of this document.

**O11 Change Request**

| Column | Description |
|---|---|
| Title | A short name for the change |
| Business Application | The main application changed |
| Change Type | Standard, normal, emergency or a client type |
| Standard Change Model | The pre-approved model, when the change is standard |
| Status | Draft, submitted, in progress, completed, rejected, withdrawn or cancelled (by meaning) |
| Mode | Built in or linked, copied from the setting when the request was created |
| External Change Number | In linked mode: the ITSM tool's change number |
| External Change Url | In linked mode: the link to the ITSM change. The number and link alone never count as authorization: the ITSM tool reports the state through a client-built step, or the change manager attests on a confirmation card (working default, 28 Q22) |
| Window Start | When the change may start (D-13) |
| Window End | When the change must be done (D-13) |
| Planned Target | The target the change is planned for |
| Authorization Decision | The change authorization decision |
| Result | How the change ended, from the `change-result` value list |

**O12 Affected Application**

| Column | Description |
|---|---|
| Change Request | The change |
| Business Application | A further application it touches |
| Impact Role | Changed, depends on the change, or informed, from the `impact-role` value list |

**O13 Artifact Version**

| Column | Description |
|---|---|
| Business Application | The main application this artifact belongs to |
| Artifact Kind | Managed solution, container image, code package, static site, agent package, commit or other |
| Artifact Name | The artifact's name, such as a solution unique name or image name |
| Version | The version text. Artifact Name plus Version is unique |
| Digest | A hash of the artifact, when the sender provides one |
| Source Commit | The source commit it was built from, when known |
| Source Repository Url | The repository it came from |
| Release Record Url | A link to the client's own release record, when one exists |

**O14 Deployment Record**

| Column | Description |
|---|---|
| Business Application | The application deployed |
| Artifact Version | What was deployed |
| Deployment Target | Where it went |
| Change Request | The change it belongs to, if any |
| Pipeline Reference | The pipeline that ran it, if any |
| Deployment Route | How the record arrived |
| Run Url | A link to the pipeline run or the tool's own record |
| Started On | When the deployment started |
| Finished On | When it finished |
| Result | Succeeded, partly succeeded, failed or not deployed (by meaning) |
| Version Before | The version on the target before, when the sender read it |
| Version After | The version on the target after, read back |
| Deployed By | The principal that ran the deployment: a person, or a pipeline's workload identity that holds a workload grant (C42) |
| Started By AI Client | The AI client that queued the run on a person's behalf, if any (sweep proposal) |
| Merge Actor | Who completed the merge that triggered the run, including an AI agent (sweep proposal) |
| Stage Order Run | The order of pipeline stages actually run (sweep proposal) |
| Gate Level Applied | The gate level in force for this change at this target when the record was written |
| Is Outside Window | Yes when it finished outside the change request's window (D-13) |
| Is Other Path | Yes when the route is not allowed by the target's change path |
| Is Missing Approval | Yes when the change lacks the approvals its policy requires |
| Idempotency Key | The sender's run and stage identifier, so a retry updates instead of duplicating (I-189) |
| Tested In | Later: the test deployment this production deployment came from (I-180) |

**O15 Deployment Target**

| Column | Description |
|---|---|
| Target Kind | Power Platform environment, Microsoft 365 tenant, Azure resource group, GitHub environment, SaaS tenant or other |
| Target Stage | Development, test, production or a client stage |
| Change Path | The routes this target expects |
| Link Kind | The Microsoft resource or tool link kind |
| External Id | The tool's ID, such as a Power Platform environment ID |
| Url | A link that opens the target in its tool |
| Label As Seen | The target's name when last seen |
| Last Seen On | When the product last confirmed it exists |

**O16 Pipeline Reference**

| Column | Description |
|---|---|
| Link Kind | The pipeline kind, such as Azure Pipelines pipeline or GitHub Actions workflow |
| External Id | The tool's ID for the pipeline |
| Url | A link that opens the pipeline |
| Label As Seen | The pipeline's name when last seen |
| Last Seen On | When a record step or a person last confirmed it |
| Is Moved | Yes when the pipeline or its repository moved (sweep proposal) |
| Moved To Url | The new location |

**O17 Pipeline Map Entry**

| Column | Description |
|---|---|
| Business Application | The application deployed |
| Pipeline Reference | The pipeline that deploys it |
| Deployment Target | The target it deploys to |
| Stage Order | The position of this target in the pipeline's stages |

**O18 Delivery Link**

| Column | Description |
|---|---|
| Subject Kind | Business application, change request or intake request |
| Subject | The row the link belongs to |
| Link Kind | Work item, repository, plan, project or another delivery kind |
| External Id | The tool's ID, such as a work item number |
| Url | A link that opens the item |
| Label | A short label |
| Linked On | When the link was added |
| Last Seen On | When the link was last confirmed |
| Is Moved | Yes when the target moved (sweep proposal) |
| Moved To Url | The new location |

**O19 Document Link**

| Column | Description |
|---|---|
| Business Application | The application documented |
| Documentation Requirement | The requirement this document meets, if any |
| Document Type | The kind of document |
| Link Kind | Wiki page, repository file, SharePoint page or another document kind |
| Url | A link to the document. Required: a row exists only once a link is recorded |
| Status | Draft, current or stale (by meaning). "Missing" is never stored |
| Document Owner | Who keeps the document |
| Last Updated On | When the owner says it last changed |
| Last Checked On | When someone last confirmed it is still true |
| Stale On | Computed: the date it becomes stale, from the document type's freshness |

Rule: the product never writes the document. The owner's own AI tool does, and records the link and status through the product's tools (D-17).

Rule: "missing" is computed, not stored. A document is missing when a C23 Documentation Requirement applies to the application (by kind, tier and stage) and no draft, current or stale Document Link meets it. Tools and pages compute it from configuration each time, so a stored row and a computation can never disagree (working default, 28 issue 16).

**O20 Evidence Link**

| Column | Description |
|---|---|
| Subject Kind | Decision, approval record, change request, deployment record, review or claim |
| Subject | The row the evidence supports |
| Link Kind | The evidence kind, such as test result, assessment export, impact list or release record |
| Url | A link to the evidence |
| Label | A short label |
| Evidence Level | Read, checked, as reported, proven, designed, or not read with a reason (I-44) |
| Captured On | When the evidence was linked |
| Digest | A hash of the evidence file, when one is available |

**O21 Review**

| Column | Description |
|---|---|
| Business Application | The application reviewed |
| Review Type | The kind of review |
| Trigger | What caused the review: its cadence, an ad hoc request, a tier answer, an owner leaving, an emergency or failed change, or another configured trigger (working default, 28 issue 15) |
| Status | Due, done or cancelled (by meaning) |
| Due On | When it is due |
| Done On | When it was done |
| Reviewer | Who did it |
| Outcome Decision | The decision the review ended with |
| Next Due On | Computed from the cadence when done |

**O22 Gap Check Run**

| Column | Description |
|---|---|
| Run By | The admin who ran it, as themselves (D-15) |
| Admin Role Used | The Entra role the admin held. Four roles see everything; AI administrator and AI reader see only agents, agentic apps, agent flows, environments and environment groups [S6] |
| Estates Covered | Keys of the estates the run read, from the `estate` value list |
| Client App | The MCP client used |
| Started On | When it started |
| Finished On | When it finished |
| Items Seen | How many items the run compared |
| New Claims | How many new claims it added |

**O23 Claim**

| Column | Description |
|---|---|
| Gap Check Run | The run that found it |
| Finding Kind | Unregistered or vanished (engine contract) |
| Component Kind | The item's type |
| External Id | The item's Microsoft ID |
| Url | A link to the item in Microsoft's tool |
| Label As Seen | Its name when found |
| Target As Seen | The environment or tenant it was found in, as a label |
| Suggested Claimant | The person Microsoft listed as owner at check time; a hint only (fit 8) |
| Status | Open, claimed or dismissed (by meaning) |
| Claimed By | Who claimed it |
| Claimed On | When |
| Resulting Component | The technical component created when it was claimed |
| Dismiss Reason | Why it was dismissed, such as "not a business application" |

**O24 Audit Entry**

| Column | Description |
|---|---|
| Entity Name | The entity the event is about |
| Row Id | The row the event is about |
| Operation | Create, update, delete, approve-outcome written, reminder sent, gap check run, configuration activated, write outside the engine found, or another product event |
| Changed Values Json | Old and new values for a data change, without secrets |
| Actor | The principal that acted |
| Actor Kind | The actor kind |
| On Behalf Of | The person an AI client or pipeline acted for |
| Client App | The client used |
| Occurred On | When it happened |
| Correlation Id | A value that ties together events of one call |
| Request Key | The caller's idempotency key for a write call, unique per operation (working default, 28 issue 15) |
| Request Hash | A hash of the request payload, so a reused key with a different payload is refused (working default, 28 issue 15) |

### 2.7 Later entities

L01 Data Area was withdrawn by the data taxonomy (D-37, settled by D-41): C49 Data Domain and its owners, O26 and O27 replace it (B20-6). Its ID is never reused.

| ID | Logical name | Purpose | Class | Key relationships | From |
|---|---|---|---|---|---|
| L02 | Checklist Template | A list of check items per stage, review type or retirement, each citing a guideline | CONFIGURATION | Has many checklist item rows; to Guideline | Product scope (checklists later); I-237 |
| L03 | Checklist Result | One checklist run on one application, item by item, with evidence | OPERATIONAL | To Checklist Template, Business Application; Evidence Links | Retirement checklist (problem row 4, later) |
| L04 | Standard | An architecture standard or pattern with owner and status, for intake and review to point to | CONFIGURATION | Owner Role or Role; Decisions of type exception | Problem row 20 (later) |
| L05 | Gate Check | One question a pipeline check step asked and the answer given (Advise or Enforce) | OPERATIONAL | To Change Request, Deployment Target, Gate Policy row | D-03 (after the gate decision) |

### 2.8 Not entities, on purpose

| Not stored | Why | Where it stays |
|---|---|---|
| A copy of Microsoft's inventory, agent registry or Entra data | D-15: links, not copies | Power Platform and Microsoft 365 admin centers, Agent 365, Entra |
| Who is in each Entra group (a roster) | D-11: membership is administered in Entra; rosters are read from Entra live (fit 12, 23) | Entra |
| Work items, plan tasks, plan facts | D-12: links only | Azure DevOps, GitHub, Planner |
| Document content | D-17: the product never writes documents | Wiki, repository, SharePoint |
| Pipeline definitions and run history | D-19: the product never builds, hosts or runs pipelines | The client's pipelines |
| Approval workflow state | The approval runner (Power Automate) runs the approval; the product keeps the outcome | Power Automate |
| Incidents, problems, service requests, configuration items | Not an ITSM suite or CMDB (D-01) | The client's ITSM tool |
| Freeze calendar, window booking | D-13: out of scope | The deployment tool, for example a Business Hours check |
| A multi-application deployment plan (the 2021 gray box) | Out of scope by design: not built now | Guidance |
| Reports or datasets for Power BI | No Power BI, by design (D-09) | Views and pages over the product's own data |
| Microsoft's owner of a resource | Fit 8: inventory owners are weak; only a "differs" flag is kept on the component. An open claim (O23) holds the listed owner only as a Suggested Claimant hint | Microsoft's tools |

### 2.9 Platform bindings at a glance (D-22)

| Logical concept | Dataverse binding | Azure database binding (for example Azure SQL Database) |
|---|---|---|
| Where configuration lives | Rows in configuration tables; JSON in multiline text columns (format option UNVERIFIED) | Rows in a `config` schema; native `json` columns, GA in Azure SQL Database [S20] |
| Configuration drafts (C46) | Own table, updatable while draft, frozen on submit | `config.ConfigurationDraft`, updatable while draft, frozen on submit |
| Who writes configuration rows | No product role holds create, write or delete on configuration tables. Only the engine's activation custom API writes them; a guard plug-in refuses any other write (working default, 28 issue 3) | Only the API's managed identity writes the `config` schema, inside the activation transaction. No role gets a row-level security write on `config` (working default, 28 issue 3) |
| The engine | Synchronous plug-ins and custom APIs on every write (D-10). Always runs as the caller, with an engine-nested marker; no elevation. Approval outcomes are protected by column security. Elevation only inside named engine effects, and only if spike S2 fails (working default, 28 issue 9) | API code; database permissions allow writes only from the API's identity (D-26). The API connects with its managed identity and puts the person's ID in `SESSION_CONTEXT` for row-level security (working default, 28 Q20) |
| Where the server and engine run | The engine runs inside Dataverse. The client hosts the product's MCP server (D-09) | One service on Azure Functions Flex Consumption (.NET 10) hosts the API, the product's MCP server and the engine. Azure Container Apps is the fallback if the on-behalf-of or MCP spike fails on Functions (working default, 28 issue 13) |
| Configuration history | Configuration Set Version snapshots plus Dataverse auditing | Configuration Set Version snapshots in an append-only ledger table [S18]; system-versioned temporal tables with a history table on the `config` entity tables [S19] |
| Append-only decisions and approvals | Plug-in refuses updates and deletes after a row is final | Ledger tables for the frozen and append-only entities: updatable ledger for Tier Assessment, Decision and Approval Record; append-only ledger for Configuration Set Version, Deployment Record, Evidence Link and Audit Entry (document 22). Append-only ledger tables allow inserts only and cannot be reverted [S18]. No personal data in free text, erasure clears the Principal Reference label, and the legal basis is documented (working default, 28 Q21) |
| Audit | Dataverse auditing records `userid`, `callinguserid` for impersonated calls, and actions including "Create - AI assisted" and "Update - AI assisted" [S16]. O24 Audit Entry holds only product events | O24 Audit Entry is the full change log, in an append-only ledger table [S18] |
| Roles through Entra groups | Entra group teams carry security roles; membership cached up to 8 hours; a team lists only members who accessed the environment [S7]; direct user roles only as documented exceptions [S8] | Entra app roles assigned to groups; needs Entra ID P1 or P2; nested groups not supported [S9] |
| Workload access (C42) | An application user with a custom security role, recorded as a workload grant. Whether an application user can get a role through a group team is UNVERIFIED | An app role with allowed member type Application, assigned to the service principal and recorded as a workload grant. The API checks the `roles` claim of the app-only token. A service principal in a group does not get the group's app roles in its token [S22] (working default, open design question 12, see 28-working-defaults.md) |
| Approval cards | The approval runner's flows in the product's environment; the approval writer writes outcomes through a service principal connection | The approval runner's flows in one Power Platform environment with Dataverse, kept for approvals, in a flow group with one Process license. They call the API as the approval writer. The product's own approval page is offered where a security review asks for token-level evidence of the approver (working default, 28 Q19) |
| Principal Reference | Own table, optionally linked to the built-in user row | Own table |
| A polymorphic subject (Decision, Delivery Link, Evidence Link) | A multi-table lookup, created with code; maker portal support is "a future release" [S14]. Or one lookup per subject kind | One nullable foreign key per subject kind plus a check constraint that exactly one is set |
| Stable keys | An alternate key on Key plus Configuration Set; up to 10 keys per table, 900 bytes, 16 columns; key values must avoid characters such as `/ < > * % & : \ ? + #` [S15] | A unique constraint on Key plus Configuration Set |
| Idempotent record steps | Upsert by alternate key on Idempotency Key [S15] | Upsert (merge) on a unique Idempotency Key |
| Screens | Generative pages; custom API calls are preview, and custom APIs with the EntityCollection binding type or Entity parameters are not supported [S13] | A web page in the client's Azure (D-22) |

### 2.10 Proposed and adopted IDs (step 2 design review and alignment pass, 2026-11-22)

Documents 21 to 26 proposed new configuration entities, and the working defaults add one more. The IDs are reserved here so no two documents claim the same ID. Adopted rows are defined in section 2.5. A proposal keeps its definition in the proposing document's "New terms" section until a design review accepts it.

| ID | Logical name | Proposed by | Status |
|---|---|---|---|
| C40 | Separation Rule | Document 21, section 3.5 | Adopted as a working default (28 issue 5). Defined in section 2.5. Document 26's four Approval Rule columns and document 25's single column are dropped |
| C41 | Tool Policy | Document 24, section 2.3 (first numbered C40 there, renumbered) | Proposal |
| C42 | Workload Grant | Document 26, section 3.4 | Adopted as a working default (28 issue 8; open design question 12, see 28-working-defaults.md). Defined in section 2.5. C30 stays group-only (V35); the workload binding kind on C30 that document 22 proposed is dropped |
| C43 | Column Protection Rule | Document 26, section 6.1 | Proposal |
| C44 | Portfolio Access | Document 26, section 5.2 | Proposal. The portfolio read mode it serves is available under the read visibility default (28 issue 10) |
| C45 | Known Client | Document 26, section 10.3 | Proposal; overlaps the `client-app` value list of document 24 |
| C46 | Configuration Draft | `28-working-defaults.md`, issue 3 (document 21's draft model, on both bindings) | Adopted as a working default. Defined in section 2.5 |
| C47 to C50 | Data Classification, Data Domain Level, Data Domain, Data Platform | The data taxonomy design ([data taxonomy](../../architecture/data-taxonomy.md), D-37, settled by D-41) | Adopted. Defined in section 2.5 and B20-6. Built 2026-11-24 |
| O25 to O27 | Data Location, Data Mapping, Data Use | The data taxonomy design | Adopted. Defined in section 2.6 and B20-6. Built 2026-11-24 |

---

## 3. Naming conventions for the physical model (proposal)

These are proposals. The publisher prefix and the product's short name wait for D-05 and D-22.

### 3.1 Logical layer (this document)

| Rule | Example |
|---|---|
| Entity names are singular Title Case English, with no prefix | Business Application, Deployment Record |
| Column names are Title Case English | Window Start, Last Seen On |
| A lookup is named after its target | Tier, Change Request |
| Two lookups to the same target get role names | From Application, To Application; Supersedes, Amends |
| Yes or no columns start with Is, Has, Can or Requires | Is Outside Window, Requires Deputy |
| Dates and date-times end with On | Decided On, Last Seen On |
| JSON columns end with Json | Answers Json, Content Json |
| Link columns end with Url | Run Url, Hub Url |
| A tool's own identifier is External Id | External Id |
| Counts end with Count, Days or a plural noun | Minimum Count, Lead Days, New Claims |

### 3.2 Keys for configuration rows (both platforms)

- Lowercase ASCII letters, digits and hyphens only, 1 to 64 characters, starting with a letter. Example: `tier-3`, `emergency`, `change-authorization`, `microsoft-coe-baseline`.
- Unique per entity within a configuration set. Never reused after a row is retired, like guideline numbers.
- This format avoids every character that breaks Dataverse alternate key requests [S15].
- Seed files, presets, MCP tool arguments and pipeline steps use keys, never internal IDs.

### 3.3 Contract layer (API and MCP payloads, both platforms)

- Field names are lowerCamelCase forms of the logical names: `businessApplicationId`, `windowStart`, `lastSeenOn`.
- Configuration references travel as keys: `tierKey: "tier-3"`, `changeTypeKey: "emergency"`.
- Operational references travel as IDs: `changeRequestId`.
- Because the contract uses logical names, the MCP surface stays the same whichever store D-22 picks.

### 3.4 Dataverse binding

| Item | Proposal | Microsoft fact |
|---|---|---|
| Publisher prefix | A short, product-specific prefix, chosen after the name | 2 to 8 alphanumeric characters, starts with a letter, not `mscrm`, followed by an underscore; cannot be changed later [S11] |
| Table schema name | `<prefix>_` plus the logical name in PascalCase: `<prefix>_BusinessApplication` | Schema name is Pascal case by convention; the logical name is its lowercase form and cannot change [S11] |
| Column schema name | `<prefix>_` plus PascalCase: `<prefix>_WindowStart` | As above |
| Lookup schema name | Ends with `Id`: `<prefix>_TierId`, `<prefix>_FromApplicationId` | Convention, not a platform rule |
| Relationship name | `<prefix>_<referenced>_<referencing>_<lookup>` in lowercase | Convention |
| Configurable lists | Lookups to configuration tables, never choice columns (D-06) | (design rule) |
| Engine contract values | Global choices, one per contract in section 2.4, named `<prefix>_<contract>` | Fixed by design (working default, open design question 1, see 28-working-defaults.md) |
| Stable key | An alternate key on `<prefix>_Key` plus the configuration set lookup | Text and lookup columns are valid key columns [S15] |
| Custom API unique name | `<prefix>_` plus a PascalCase verb and noun: `<prefix>_GetApplicationCard` | Must include the publisher prefix, no special characters, cannot change [S12] |
| Custom API binding | Global binding with explicit parameters, never EntityCollection binding or Entity parameters, so generative pages can call them (sweep proposal) | All custom APIs can be composed as Global [S12]; pages don't support the other shapes [S13] |
| Display names | The logical names, with plural forms for tables | (convention) |
| Maximum schema name length | Keep names under 40 characters including the prefix | UNVERIFIED: no Learn page found that states the limit |

### 3.5 Azure database binding

| Item | Proposal | Microsoft fact |
|---|---|---|
| Schemas | `config` for C entities, `ops` for O entities, `audit` for O24 | (convention) |
| Table names | Singular PascalCase, no prefix (the schema separates them): `ops.BusinessApplication` | Identifiers are 1 to 128 characters [S17] |
| Primary key | `Id`, a unique identifier | (convention) |
| Foreign keys | `<Target>Id` or a role name: `TierId`, `FromApplicationId` | (convention) |
| Constraint names | `PK_<Table>`, `FK_<Table>_<Column>`, `UQ_<Table>_<Columns>`, `IX_<Table>_<Columns>`, `CK_<Table>_<Rule>` | (convention) |
| JSON columns | Native `json` type | GA in Azure SQL Database [S20] |
| Configuration history | System-versioned temporal tables for `config` tables, with a retention policy | [S19] |
| Ledger tables | Updatable ledger tables for Tier Assessment, Decision and Approval Record. Append-only ledger tables for Configuration Set Version, Deployment Record, Evidence Link and Audit Entry. A change to a final row is a new superseding row (document 22; working default, 28 Q21) | Append-only ledger tables allow only inserts and cannot be reverted [S18] |
| Configuration drafts | `config.ConfigurationDraft` as a plain updatable table (C46) | (design) |
| PostgreSQL alternative | Lowercase snake_case names (`ops.business_application`) | Identifier rules UNVERIFIED here |

### 3.6 Files, presets and samples

- Seed and preset files: `config/presets/<preset-key>/<entity-key>.json`, for example `config/presets/balanced/tier.json`.
- Pipeline step samples use the product's short name in lowercase kebab case, such as `<name>-record-deployment`.
- MCP tool names follow the MCP surface document. They use the logical entity names in this file.

### 3.7 Names to keep away from

Collisions with Microsoft table or product names make physical names and agent prompts ambiguous. Avoid as bare names: Environment, Solution, Team, Application, Pipeline, Approval, Inventory, Catalog, Registry, Release. The logical names in section 2 already avoid them.

---

## 4. Microsoft sources

"Checked 2026-11-22" means read on Microsoft Learn (or GitHub's own docs) through the Microsoft Learn MCP tools for this document on 2026-11-22. [J] rows were checked on 2026-11-22 by the news sweep and not re-checked here.

| ID | Fact used | URL | Checked |
|---|---|---|---|
| S1 | "The ADR serves as an append-only log. Don't go back and edit accepted records." A changed decision gets a new record that supersedes the original. Status "such as *Proposed*, *Accepted*, or *Superseded*". Records include options considered and a confidence level | https://learn.microsoft.com/azure/well-architected/architect-role/architecture-decision-record | 2026-11-22 |
| S2 | Three agent risk tiers: "Tier 1: Low risk", "Tier 2: Medium risk", "Tier 3: High risk", each with required controls; tier 3 needs "a formal process owner" and "a quarterly maturity review". "The clearest risk signal is the assist-to-execute line." "Don't build a parallel governance universe for agents" | https://learn.microsoft.com/agents/center-of-excellence/govern-agents-risk | 2026-11-22 |
| S3 | The front door agent "recommends a route" and "doesn't approve the work"; "Designated reviewers make approval decisions" | https://learn.microsoft.com/agents/center-of-excellence/agent-front-door | 2026-11-22 |
| S4 | Support tiers Productivity, Important and Critical; "consider a graduation path"; a handover path for critical applications | https://learn.microsoft.com/power-platform/guidance/adoption/support-strategy-solutions | 2026-11-22 |
| S5 | Copilot Studio zones: "Zone 1 - Citizen Development Zone", "Zone 2 - Partnered Development Zone", "Zone 3 - Professional Development Zone" | https://learn.microsoft.com/microsoft-copilot-studio/guidance/sec-gov-phase2 | 2026-11-22 |
| S6 | The inventory lists agents, apps (canvas, model-driven, code, vibe, Copilot managed runtime, App Builder), flows (cloud, agent, Workflows agent), Power Pages sites (preview), connectors, environments and environment groups. Six Entra roles: Global administrator, Power Platform administrator, Dynamics 365 administrator and Global reader see all; AI administrator and AI reader see "Agents, agentic apps, agent flows, environments, and environment groups only". Built-in Power Platform RBAC roles are not supported for inventory access | https://learn.microsoft.com/power-platform/admin/power-platform-inventory | 2026-11-22 |
| S7 | Group team memberships "are cached upon the team member's sign in"; changes show at the next sign-in or "after 8 hours of continuous sign in"; the team "only displays the user members who have accessed the environment"; users are added when they first access the environment | https://learn.microsoft.com/power-platform/admin/manage-group-teams | 2026-11-22 |
| S8 | Assign a few roles directly to users only as documented exceptions to the group team pattern | https://learn.microsoft.com/power-platform/architecture/reference-architectures/standardize-dataverse-access-control | 2026-11-22 |
| S9 | "Group-based assignment requires Microsoft Entra ID P1 or P2 edition. Nested group memberships aren't currently supported" | https://learn.microsoft.com/entra/identity/enterprise-apps/assign-user-or-group-access-portal | 2026-11-22 |
| S10 | Approval types: Approve/Reject (everyone must approve, or first to respond), custom responses, and sequential approval | https://learn.microsoft.com/power-automate/get-started-approvals | 2026-11-22 |
| S11 | Custom table prefix: "between two and eight characters in length, all alphanumeric characters, and it must start with a letter. It can't start with `mscrm`", with an underscore after it. Schema name is typically Pascal case; logical name is its lowercase form; "you can't change it later" | https://learn.microsoft.com/power-apps/developer/data-platform/customize-entity-metadata ; https://learn.microsoft.com/power-apps/developer/data-platform/entity-metadata | 2026-11-22 |
| S12 | Custom API unique name "must include a customization prefix", "can't contain any special characters", "Cannot be changed after it is saved". "You can compose all your custom API as **Global**" | https://learn.microsoft.com/power-apps/developer/data-platform/custom-api-tables ; https://learn.microsoft.com/power-apps/developer/data-platform/custom-api | 2026-11-22 |
| S13 | Generative pages: up to six Dataverse tables per page; custom API actions and functions are preview; "Custom APIs that use the **EntityCollection** binding type or **Entity** parameters aren't supported" | https://learn.microsoft.com/power-apps/maker/model-driven-apps/generative-pages ; https://learn.microsoft.com/power-apps/maker/model-driven-apps/generative-page-external-tools | 2026-11-22 |
| S14 | Custom multi-table lookups are created with the SDK or Web API; "Interactive user interface support will be available in a future release" | https://learn.microsoft.com/power-apps/developer/data-platform/webapi/multitable-lookup | 2026-11-22 |
| S15 | Alternate keys: valid column types include single line of text and lookup; up to ten keys per table; 900 bytes and 16 columns per key; no column security; null values don't enforce uniqueness; values with characters such as `/ < > * % & : \ ? + #` break GET or PATCH | https://learn.microsoft.com/power-apps/developer/data-platform/define-alternate-keys-entity ; https://learn.microsoft.com/power-apps/maker/data-platform/define-alternate-keys-reference-records | 2026-11-22 |
| S16 | The audit table holds `userid` and `callinguserid` ("the calling user when impersonation is used"); audit actions include 123 "Create - AI assisted" and 124 "Update - AI assisted" | https://learn.microsoft.com/power-apps/developer/data-platform/auditing/retrieve-audit-data ; https://learn.microsoft.com/power-apps/developer/data-platform/reference/entities/audit | 2026-11-22 |
| S17 | "Both regular and delimited identifiers must contain from 1 through 128 characters" | https://learn.microsoft.com/sql/relational-databases/databases/database-identifiers | 2026-11-22 |
| S18 | Append-only ledger tables "allow only `INSERT` operations", so privileged users "can't alter data"; a ledger table "can't be reverted" | https://learn.microsoft.com/sql/relational-databases/security/ledger/ledger-append-only-ledger-tables | 2026-11-22 |
| S19 | System-versioned temporal tables keep a history table and support point-in-time queries | https://learn.microsoft.com/sql/relational-databases/tables/temporal/overview | 2026-11-22 |
| S20 | The `json` data type is generally available in Azure SQL Database | https://learn.microsoft.com/sql/t-sql/data-types/json-data-type | 2026-11-22 |
| S21 | For append-only stores, "store personal data outside the event store and reference it by identifier", so deletion does not break the stream | https://learn.microsoft.com/azure/architecture/patterns/event-sourcing | 2026-11-22 |
| S22 | "Currently, if you add a service principal to a group, and then assign an app role to that group, Microsoft Entra ID doesn't add the `roles` claim to tokens it issues" | https://learn.microsoft.com/entra/identity-platform/howto-add-app-roles-in-apps | 2026-11-22 (re-checked in the step 2 design review) |
| J1 | GitHub Copilot approvals are generally available and can count toward required approvals (off by default) | https://github.blog/changelog/2026-10-28-copilot-code-review-merge-with-confidence-with-max-and-approvals | 2026-11-22 (news sweep) |
| J2 | Copilot Studio CLI named deployments; "Keep a release record with the source version, CLI version, target agent and environment, and test results" | https://learn.microsoft.com/microsoft-365/copilot-managed-runtime/developer/agents/cli/deploy | 2026-11-22 (news sweep) |
| J3 | Planner basic plans are now unified plans outside Dataverse; premium plans stay supported | https://techcommunity.microsoft.com/blog/plannerblog/introducing-unified-tasks-and-plans-for-copilot-and-microsoft-365/4573958 | 2026-11-22 (news sweep) |
| J4 | Copilot managed runtime governance in the Microsoft 365 admin center | https://learn.microsoft.com/microsoft-365/admin/manage/apps/governance | 2026-11-22 (news sweep) |
| J5 | The Agent 365 registry shows "Agents without owners", and the agent actions page offers "Assign new owner" | https://learn.microsoft.com/microsoft-agent-365/admin/agent-registry-overview ; https://learn.microsoft.com/microsoft-agent-365/admin/agent-actions | 2026-11-22 (news sweep) |

**UNVERIFIED in this document:** the maximum length of a Dataverse table or column schema name; whether a Dataverse multiline text column offers a JSON format option; PostgreSQL identifier rules; whether owners who only answer approval cards ever appear as Dataverse users; whether Fabric apps, Power BI data apps and CLI-deployed agents can be seen by the gap check (sweep proposal kinds); whether a Dataverse application user can get a security role through a group team (28, question 12; section 6, Question 4).

---

## 5. Diagram specs

All diagrams use the D-04 palette: navy `0A2A4A` for text and lines, blue `0071DC` for group header bars, gold `FFC221` as the accent. Fills are light only: white `FFFFFF`, light blue `E8F1FB`, light gold `FFF4D6`, light gray `F3F5F8`. No dark fills. The diagrams are drawn in Visio.

### D20-1 Logical entity map

- **Title:** The product's logical entities, configuration at the center.
- **Purpose:** One page that shows every R1 entity, its class and its main relationships, platform-neutral (D-06, D-22).
- **Groups (boxes with blue header bars):**
  1. Configuration core (center, light gold fill): C01 Configuration Set, C02 Configuration Set Version, C46 Configuration Draft, C03 Preset, C04 Setting, C05 Status, C06 Value List, C07 List Value.
  2. Taxonomy (light blue): C08 Tier, C09 Tier Question, C10 Tier Answer Option, C11 Vocabulary Mapping, C12 Lifecycle Stage, C13 Gate, C14 Gate Requirement, C15 Application Kind, C16 Component Kind, C17 Dependency Type, C18 Decision Type, C19 Decision Outcome, C20 Change Type, C21 Standard Change Model, C22 Document Type, C24 Review Type, C27 Party, C28 Owner Role, C34 Target Stage, C35 Change Path, C36 Deployment Route, C37 Link Kind, C38 Actor Kind, C39 Guideline.
  3. Rules (light blue): C23 Documentation Requirement, C25 Review Cadence, C26 Reminder Rule, C29 Role, C30 Role Group Binding, C31 Decision Right, C32 Approval Rule, C33 Gate Policy, C40 Separation Rule, C42 Workload Grant.
  4. Register (white): O01 Portfolio, O02 Business Application, O03 Technical Component, O04 Dependency, O06 Owner Assignment.
  5. Decisions and approvals (white): O08 Tier Assessment, O09 Decision, O10 Approval Record, O21 Review.
  6. Change and deployment (white): O11 Change Request, O12 Affected Application, O13 Artifact Version, O14 Deployment Record, O15 Deployment Target, O16 Pipeline Reference, O17 Pipeline Map Entry.
  7. Links and evidence (white): O18 Delivery Link, O19 Document Link, O20 Evidence Link.
  8. Intake and gap check (white): O07 Intake Request, O22 Gap Check Run, O23 Claim.
  9. Identity and audit (light gray): O05 Principal Reference, O24 Audit Entry.
- **Edges (navy, labeled):**
  - every taxonomy and rule row to C01: "belongs to";
  - C02 to C01: "snapshot of"; C46 to C01: "draft for"; C02 to C46: "activated from";
  - every operational group to C02: "stamped with" (one bundled edge);
  - O02 to O01: "grouped in"; O02 to C08: "current tier"; O02 to C12: "current stage";
  - O03 to O02: "part of"; O04 from O02 to O02 or O03: "depends on";
  - O06 to O02: "owner of"; O06 to C28: "holds"; O06 to O05: "who";
  - O09 to C18: "of type"; O09 to O09: "supersedes / amends"; O10 to O09: "approves"; O10 to C32: "required by";
  - O11 to O02: "changes"; O11 to O09: "authorized by"; O14 to O11: "delivers"; O14 to O13: "deploys"; O14 to O15: "lands on"; O17 linking O02, O16 and O15: "maps";
  - O19 to C23: "meets"; O20 to O09, O11, O14: "proves";
  - O23 to O22: "found by"; O23 to O03: "becomes";
  - C30 to C29: "binds"; C30 to O05: "Entra group";
  - C42 to C29: "workload role"; C42 to O05: "non-human principal";
  - C40 to C32: "limits who counts";
  - C33 to C08, C20, C34: "per tier, change type, target stage".
- **Accent:** a small gold marker on C01, C03, C04, C05, C12, C14, C31, C32, C33, C36, C37, C38, C40, C46, O05 and O23 meaning "carries an engine contract value" (section 2.4).

### D20-2 How a configuration change happens

- **Title:** Configuration changes: the admin decides, the AI performs, the engine activates and stamps.
- **Purpose:** Shows D-07 and D-10 end to end: preset, copy, draft, decision, activation, version, stamp (working default, 28 issue 3).
- **Lanes:** Admin (person); AI client (for example Claude Code, on the admin's behalf); The product's MCP server; Engine; Approval runner (Power Automate); Configuration store; Operational records.
- **Nodes:**
  1. Full preset plus overlays (C03), in the configuration store lane.
  2. "Copy to working set" (C01, kind working).
  3. Admin: "Instructs a change" (plain words).
  4. AI client: "Prepares a configuration draft" (C46: a patch, or a configuration file).
  5. MCP server: "Calls the draft tools as the admin".
  6. Engine: "Checks the caller holds the configuration admin role, validates the draft, computes the diff".
  7. Engine: "On submit, freezes the draft and records a configuration change decision (Proposed)" (O09).
  8. Admin: "Accepts the diff on an approval card" (O10, person only; working default, open design question 4, see 28-working-defaults.md).
  9. Engine: "Activates: writes the live rows and Configuration Set Version n+1 in one transaction" (C02, with hash and summary).
  10. Operational records: "New rows stamp version n+1; old rows keep their version".
- **Edges:** 1 to 2 "apply preset"; 3 to 4 "instruction"; 4 to 5 "tool call"; 5 to 6 "on behalf of admin"; 6 to 7 "valid caller, valid draft"; 7 to 8 "approval card, sent by the approval runner"; 8 to 9 "accepted, outcome written by the approval writer"; 9 to 10 "in force". A dotted gold edge from 4 to 8 labeled "AI cannot approve". A dotted gold edge from 4 to the configuration store labeled "no direct writes".

### D20-3 Change and deployment slice

- **Title:** From change request to deployment record, with flags.
- **Purpose:** Shows how the change entities and the deployment entities connect, and where the three flags come from (D-08, D-13, D-19, D-03).
- **Groups:** Inside the product (white): O11, O12, O09, O10, C33, O13, O14, O15, O16, O17, O20. Outside the product (light gray): "Client pipeline (record step)", "Owner's AI tool", "Person in the app", "ITSM tool (linked mode)".
- **Nodes:** as listed, plus three flag badges in light gold on O14: "outside window", "other path", "missing approval".
- **Edges:** O11 to O12 "also affects"; O11 to O09 "authorization decision"; O10 to O09 "approvals (persons only)"; C33 to O14 "gate level applied"; "Client pipeline" to O14 "record step: upsert by idempotency key"; "Owner's AI tool" to O14 "record through MCP"; "Person in the app" to O14 "enter"; O14 to O13 "deploys"; O14 to O15 "lands on"; O15 to "Change path" "expects routes"; O17 to O16 and O15 "maps"; "ITSM tool" to O11 dotted "external change number and link; authorization state through a client-built step"; O20 to O11 and O14 "evidence".

### D20-4 Links, not copies

- **Title:** What the product links to, and what it never copies.
- **Purpose:** Shows D-12, D-15, D-17 and D-19 as data: each link entity and the tool it points to.
- **Groups:** The product (left, white): O03 Technical Component, O15 Deployment Target, O16 Pipeline Reference, O18 Delivery Link, O19 Document Link, O20 Evidence Link, O05 Principal Reference. Microsoft and other tools (right, light blue): Power Platform admin center inventory; Microsoft 365 admin center (runtime apps); Agent 365 registry; Entra (users, groups, agent identities); Azure DevOps (work items, repositories, pipelines, wiki); GitHub (repositories, Actions, projects); Planner (unified and premium plans); SharePoint; the client's ITSM tool.
- **Edges (navy, each labeled "ID, URL, last seen"):** O03 to inventory, Microsoft 365 admin center, Agent 365, Entra; O15 to inventory (environments); O16 to Azure DevOps and GitHub pipelines; O18 to Azure DevOps, GitHub, Planner; O19 to wiki, repository, SharePoint; O20 to any tool; O05 to Entra "object ID only".
- **Annotation (gold accent):** "Never copied: inventory rows, registry entries, group rosters, work items, plan tasks, document text, pipeline runs."

---

## 6. Open design questions

Only questions that no decision has settled (the decision records are in [docs/decisions/](../../decisions/)). Six design questions are still open. `28-working-defaults.md`, section 3, lists them with their context. All six touch this file:

- Q1 is Question 1 below. Q7 is Question 2 below. Q12 is Question 4 below.
- Q4, Q8 and Q9 touch only the lines marked with them. Q4 (the admin accepts the AI's configuration draft on a card) marks diagram D20-2, node 8. Q8 (the locked list of decisions the engine may accept without a card) marks the glossary line "Approval rule" and the C04 default rows. Q9 (which act counts as a person's for AI drafts that are not decisions) marks C22 Current Needs Person. Their context is in `28-working-defaults.md`, section 3.

Until a decision settles them, the design text uses each recommended option and marks it "(working default, open design question N, see 28-working-defaults.md)".

### Question 1: Is a short list of fixed engine meanings acceptable under "never hardcoded"?

- **Status.** Open design question 1 (see `28-working-defaults.md`, section 3). The design text uses option A as the working default (rule 2, section 2.4, section 3.4).
- **Background.** D-06 says statuses, kinds and types are taxonomy rows, and logic never branches on fixed values. D-10 says the engine enforces rules on every write. To enforce "AI never approves", append-only decisions, flags and gates, the engine must know what some labels mean. For example, it must know which decision status is final, which approver is a person, and which deployment route is a pipeline step.
- **Example.** A client renames "Accepted" to "Approved by board" and adds a new status "Deferred". The engine must still know that "Approved by board" satisfies a gate and that "Deferred" does not.
- **Options.**
  - **A. Fixed meanings, configurable labels.** About 20 small contracts (section 2.4). Every client label maps to one meaning. Clients rename, add, hide and reorder labels freely. *Pros:* the engine's safety rules cannot be configured away, also not by an AI making configuration changes; upgrades are predictable; tests stay finite. *Cons:* a client cannot invent a status with brand-new behavior without a product release.
  - **B. Configurable meanings.** The behavior of each status is itself a configuration rule (JSON). *Pros:* the most flexible. *Cons:* an AI configuration change could remove a safety rule; every client becomes a different product to test and audit; harder to explain to auditors.
  - **C. Fixed option sets.** Statuses are fixed choice values. *Pros:* simplest to build. *Cons:* contradicts D-06.
- **Recommendation.** A.
- **What the answer changes.** With A, the Status, Actor Kind, Deployment Route and Link Kind entities keep an Engine Meaning column, the contracts for C03, C40 and C46 stay fixed lists, the contract list is published in the public docs, and the physical model uses fixed choices only for these meanings. With B, section 2.4 becomes a rule language to design in the engine document.

### Question 2: Do AI approvals from client tools count?

- **Status.** Open design question 7 (see `28-working-defaults.md`, section 3). "AI never approves" inside the product is already decided (D-02, D-10) and is not asked again, so the AI approval lock ships locked. The design text uses option A as the working default (C33 AI Only Code Approval Handling, section 2.4).
- **Background.** D-02 and D-10 say AI never approves in the product, and D-10 makes the rule a configuration record. Client tools are another matter. GitHub Copilot approvals are now generally available and can count toward a repository's required approvals, off by default [J1]. The news sweep proposed a configurable rule for this (D-03 row).
- **Example.** A pull request is merged with only a Copilot approval. The client's pipeline deploys it to production, and the record step reports the merge actor as an AI. The change request in the product has its human change authorization. Is the code review "approved"?
- **Options.**
  - **A. AI-only code approvals never count, and the shipped gate default is "flag".** AI approvals from client tools are recorded with approver kind AI and never count toward any requirement. The AI Only Code Approval Handling column on Gate Policy can flag such a deployment or, at Enforce, stop it. Its setting default ships at flag. *Pros:* the product's core promise holds everywhere; simple for auditors; matches the product's "What it is not" list. *Cons:* a client that wants AI code approvals to count must handle that in its own tool.
  - **B. They never count, and the column ships at "ignore".** *Pros:* respects client toolchains; no surprise flags. *Cons:* AI-only merges pass silently unless a client turns the flag on.
  - **C. A switch per client.** An admin may let AI code approvals count, by a recorded decision. *Pros:* the literal reading of "everything is configuration". *Cons:* the product would then treat AI approvals as authorization, which undermines the product's core promise that AI never approves.
- **Recommendation.** A.
- **What the answer changes.** The AI Only Code Approval Handling column on Gate Policy, the `default-ai-only-code-approval-handling` setting and its default in each preset, and one line in the public "What it is not" list.

### Question 3: How does configuration differ per area, if a client needs that?

Settled as a working default in 28-working-defaults.md (Q2); a later decision can change it.

### Question 4: May each pipeline's workload identity hold the deployment recorder role directly, as a workload grant?

- **Status.** Open design question 12 (see `28-working-defaults.md`, section 3). The design text uses option A as the working default (rule 4, C29, C42, section 2.9).
- **Background.** D-11 says people get roles only through Entra group teams, never directly. D-19's record step signs in with the pipeline's workload identity, not a person. The approval runner also needs an identity of its own to write outcomes. Entra does not put a group's app roles into a service principal's token [S22]. Whether a Dataverse application user can get a role through a group team is UNVERIFIED.
- **Example.** A client has 40 pipelines across Azure DevOps and GitHub, each with its own federated identity. Each must write deployment records and nothing else.
- **Options.**
  - **A. A stated exception for non-human workloads only, as configuration rows.** Each workload identity gets its workload role directly: a role on the application user on Dataverse, an app role assignment on Azure. It is recorded as a Workload Grant (C42) with a reason, the deciding decision and an expiry, and the direct assignment check lists it. People still get roles only through groups. Agent identities and agent users never get a grant. *Pros:* works on both platforms today; each pipeline is visible and can be revoked alone; least privilege. *Cons:* a stated exception to D-11 for non-people.
  - **B. Strict groups: the workload identity joins the recorder group.** *Pros:* one rule for everyone. *Cons:* does not work on the Azure binding today [S22]; UNVERIFIED on Dataverse.
  - **C. One shared relay identity.** *Pros:* one assignment. *Cons:* every pipeline shares one credential path; records lose which pipeline wrote them.
  - **D. No workload role: people record deployments.** *Pros:* D-11 holds with no exception. *Cons:* weakens D-19's main route and the Flag level.
- **Recommendation.** A. A per-pipeline token issued by the product was also considered and rejected, because it is a shared secret.
- **What the answer changes.** With A, C42 Workload Grant stays, the direct assignment check lists grants, and one line goes into D-11's consequences. With B, C42 goes and workloads bind through C30 groups, which fails on the Azure binding today. With C, C42 holds one relay grant instead of one per pipeline. With D, the deployment recorder role goes, and the approval writer still needs a grant or another path.

---

## Proposed by the build (2026-11-22)

- **Source.** The build in `src/`: its logical model (`src/model/logical-model.json`) and its presets.
- **Status.** Every item below is a proposal. The sections above keep their meaning: no settled decision and no working default changes. Where an item rests on one of the six open design questions, it names the question. A later design review accepts or drops each item.

### B20-1 O10 Approval Record: three more columns

| Column | Description |
|---|---|
| Decision Right (proposed) | The decision right an accountable card asks for. An accountable card has no Approval Rule (28, issue 1), so Approval Rule becomes "Approval Rule or Decision Right": exactly one is set |
| Is Consultation (proposed) | Yes when the card is a consultation (C32 Is Consultation); the answer is recommendation only and never counts |
| Approver Holds Required Role (proposed) | The approval runner's record of whether the responder held the required role or owner role when answering. Written only by the approval writer, with the answer |

The O10 rule then adds Approver Holds Required Role to the columns only the approval writer writes. Types, requiredness and the relationship are in document 22, B22-1.

### B20-2 C29 Role: two more workload roles

The build's role seed adds two workload roles, marked as proposals: `authorization-reporter` (held by the client-built ITSM step through a workload grant; writes authorization facts and evidence links only) and `consistency-checker` (held by the scheduled consistency check; reads product rows and writes only "write outside the engine" audit findings). Like `deployment-recorder` and `approval-writer`, each is held only through a Workload Grant (C42), never through a group, and never approves (working default, open design question 12, see 28-working-defaults.md). Details in document 26, B26-1.

### B20-3 Guideline numbers

The glossary line "Guideline" and the C39 summary row say G1 to G19. The build follows the public guideline list version 1.0: G1 to G20, with G10 and G11 retired and never reused. Proposal: both places say "G1 to G20 (G10 and G11 retired)".

### B20-4 A few fixed keys beside the fixed meanings (section 2.4)

The build treats some row keys as engine knowledge, the way section 2.4 treats meanings: the roles `configuration-admin` (the only role with Can Change Configuration), `approval-writer`, `deployment-recorder` and `platform-admin` (performs an erasure); the decision types `intake`, `change-authorization` and `configuration-change` (the locked list and the bootstrap); the four C26 reminder subject kinds (review, document, approval, claim); and the erased label "former user". Labels stay configurable. Every such key carries the marker of open design question 1, because it widens "a short list of fixed meanings" to a few fixed keys. With question 1 option B these keys would become configuration.

### B20-5 Terms the build uses

| Term | Definition |
|---|---|
| Final state | The state in which a class F row freezes (document 22, B22-3), such as an O08 Tier Assessment once its Tier Decision is set |
| Fill-once column | A column that may be written once while empty and never changed after, such as O13 Digest, Source Commit and Source Repository Url |
| Platform-supplied column | A common column the store sets itself (Id, Created On, Created By, Modified On, Modified By). The engine drops it from every write (section 2.2) |
| Nested effect | The name of the effect an engine-nested write belongs to, which the adapter passes next to the engine-nested marker (document 25, B25-3) |
| No-op write | A write that repeats what the row already holds, such as the same approval outcome again. Allowed, and nothing changes |
| Unknown column | A column name that is not in the entity's column list in the logical model, in its contract form (section 3.3). Invalid input (`input.unknown-column`) |

### B20-6 The data taxonomy, slice 1 (built 2026-11-24, D-37 and D-41)

The [data taxonomy](../../architecture/data-taxonomy.md#entities) (settled by D-41) adds seven entities and replaces L01 Data Area.
Slice 1 builds the model; the engine rules, the tools and the screens follow. Types, keys and relationships are in
document 22, B22-14; fields, presets and settings in document 21, B21-11.

**New terms.**

| Term | Definition |
|---|---|
| Data domain | One node of the business data tree an install draws for itself, such as People, then Payroll (C49). Business words, not platforms |
| Data domain level | One level of that tree, such as domain, subdomain or data set, with whether it needs its own owner and whether data uses may point at it (C48) |
| Data classification | A ranked sensitivity level in the tenant's Purview label names, with default handling rules (C47) |
| Data handling rule | How data may be used: allowed, decision, masked only or never, in rising strictness. A classification gives the defaults; a domain may make them stricter, never looser |
| Data platform | A kind of store from any vendor: Dataverse, Azure SQL, Salesforce, IBM i (C50) |
| Data location | One concrete store, held as a link: an environment, server, database, account, org, tenant or host (O25). Two levels at most |
| Data mapping | This domain's data sits at this location, in these containers, with this origin (O26) |
| Data origin | System of record, replica, production copy, masked copy or synthetic |
| Data use | This application reads or writes this domain, where, for what, and whether the data reaches AI (O27) |
| Effective owner | The owner who answers for a domain: its own, or the owner of the nearest domain above it |
| Inherited owner | An effective owner that comes from a domain above |
| Data governance lead | The product role that appoints the owners of top-level domains and sees every data gap (C29, D-41, question 4) |
| Data steward | The owner role that keeps a domain's mappings and examples current and is consulted on its data uses (C28) |

**Columns of the new operational entities.**

| Entity | Columns |
|---|---|
| O25 Data Location | Data Platform, Parent Location, Target Stage, Deployment Target, Hosted By Application, Link Kind, External Id, Url, Label As Seen, Last Seen On, Status, Status Meaning, Retired On |
| O26 Data Mapping | Data Domain, Data Location, Containers, Data Origin, Copied From, Status, Status Meaning, Confirmed On, Start On, Ended On |
| O27 Data Use | Record Number (DU-0042), Business Application, Data Domain, Data Location, Access, Purpose, Reaches AI, AI Service As Seen, AI Component, Is Deidentified, Access Approach, Status, Status Meaning, Start On, Ended On |

**Changed entities.** O06 Owner Assignment gains Subject Kind, Subject Data Domain and Subject Data Location: exactly
one subject is set, and an empty Subject Kind means the application, as every row before the data taxonomy has. O09
Decision gains Subject Data Use, Subject Data Mapping, Subject Data Domain and Subject Data Location. O02 Business
Application gains Holds No Business Data. C28 Owner Role gains Subject Kinds and Inherits Down; C34 Target Stage gains Is
Production. The subject kind list (section 2.6 of document 22) gains Data Use, Data Mapping, Data Domain and Data
Location; the link category list gains data location.

### B20-7 The drafted by pair and the due-state refresh (built 2026-11-24, D-36 and D-41)

- **Drafted By Client App and Drafted For Principal** (proposals, document 22, B22-15) join the operational column set:
  the AI client that created a row and the person it acted for. The engine sets them once, on create, when an AI client
  acting for a person writes, and never changes them. The other stamps still name the last write. So the AI mark reads
  "Drafted by Claude Code for Priya Castell" after Priya edits the draft herself, and names a later editor when someone
  else edits it.
- **Due-state refresh** (glossary of document 23) is built: the engine's daily run that opens the next review on its Next
  Due On, moves a current document past its Stale On to stale, expires approval requests past their Expires On and asks
  again, and sends the reminders, escalations and flag notices of the day (document 25, B25-18). Its notices are
  Audit Entries with the operation `reminder-sent` or `flag-notice-sent`.
- **Consent** (new term): the approval record that asks by name a person whom a decision would make an owner, and whose
  answer is their acceptance of the role. A rule with Approver Proposed Owners asks for it (document 21, B21-9); Balanced
  now also asks it of the business owner an intake names (document 25, B25-18).
