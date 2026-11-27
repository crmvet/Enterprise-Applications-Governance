# Data taxonomy

**Status: built and tested; runs as a demo install with fictional data; no release yet.** This page describes how the product knows which business data an application uses, who owns that data and where it sits. The model, the engine rules, the MCP tools, the Data pages of the web app and the demo data are built, under the MIT License in [`src/`](../../src/README.md). Each section names its code. Anything not built yet is listed in [Not built yet](#not-built-yet).

Decision records: [D-37](../decisions/D-37-business-data-domains-mapped-to-technical-locations.md) set the shape, and [D-41](../decisions/D-41-the-data-taxonomy-details-and-a-never-rule-cannot-be-broken.md) answered the details. For the person's view, see [Concepts](../../src/docs/concepts.md#the-data-taxonomy) and the [admin guide](../../src/docs/admin.md#the-data-taxonomy).

## In one screen

1. **Business data domains in a tree** that each organization draws for itself. Examples: Customers, then Client orders; People, then Payroll, then Bank details. An owner and a deputy can sit at any level. A domain with no owner of its own takes the owner of the nearest domain above it.
2. **Technical locations from any vendor:** a Dataverse environment, an Azure SQL database, a SQL Server on premises, a Cosmos DB account, a Salesforce org, an IBM i library, a SaaS tenant or a Fabric lakehouse. A location is held as a link: an ID, a URL and the date it was last seen.
3. **Two mappings.** A domain sits in one or more locations, and one location can hold many domains. An application declares which domains it reads or writes, where, and whether the data reaches an AI model.
4. **Words are configuration, facts are records.** The domain tree, its levels, the classifications and the vendor platforms are configuration rows. They change through a configuration change, with versions. Owners, locations, mappings and data uses are records with history.
5. **A data decision goes to the owner of the data,** not to one data owner per application.
6. **Rules sit on classifications.** A domain may make a rule stricter, never looser.
7. **A "never" rule cannot be broken.** A use or a copy that a never rule forbids is refused when it is saved, for a person and an AI client alike. The refusal names the rule, the domain and where the rule is set.
8. **The AI builder asks first.** While it builds, an AI client such as Claude Code can ask which domain a table belongs to, who owns it, and whether a use needs a decision.
9. **Not a data catalog.** Assets, columns, lineage, glossary terms and quality stay in Microsoft Fabric's OneLake catalog. The product links to the catalog and never copies it.

## The decisions and checks that need data

Every fact in the taxonomy serves a named decision or check. Anything no decision needs is left out.

| Decision or check | Process | Who decides | Question |
|---|---|---|---|
| Data use | P02, P04 | The data owner of the domain | May this application read or write this body of data, for this purpose? |
| AI data use | P02, P10 | The data owner of the domain | May this application send this data to an AI model? |
| Restricted data to AI | P02 | The data owner and the security reviewer, both | The same question, when the data is restricted |
| Copy outside production | P03, P05 | The data owner of the domain | May production data of this domain sit in this test or development location? |
| Retention at retirement | P09 | The data owner, with the business owner consulted | What happens to the data this application wrote? |
| Owner check | P08 | The platform admin runs it | Does every domain that routes decisions have an owner who can answer, with a deputy? |
| Data gaps | P06 | The platform admin and the data owners | What data is used or held that nobody has mapped, owned or decided? |
| Sensitivity against data | P02 | The platform admin (tier) | Is the application's sensitivity answer at least what its data implies? |
| The build loop question | P01, P10 | Nobody: it is a read | Claude Code is about to read table X in environment Y. Which domain is that, who decides, and is a decision needed? |

The processes are in [Processes](processes/README.md).

**Left out on purpose.**

| Left out | Why |
|---|---|
| Data assets, columns, lineage, glossary terms, data products, quality scores and scans | They are the catalog's job. The product links to the catalog through vocabulary mappings and never copies it |
| A label on each asset | Microsoft Purview Information Protection labels content. The product uses only the label names |
| Connection strings, credentials, row counts and the data itself | Never stored. A guard rule refuses them |
| Region, residency and hosting | No decision in the table branches on them |
| Records of processing and legal basis | Out of scope. The reason on a data-use decision holds the business justification |
| Data flows between systems | Later. A replica or copy mapping that names its source already gives a simple lineage |

## Entities

Seven entities hold the taxonomy, and three existing ones gained columns. They are in the logical model ([`src/model/logical-model.json`](../../src/model/logical-model.json)) and in the stores. On Azure SQL, [`0008-data-taxonomy.sql`](../../src/adapters/azure/sql/0008-data-taxonomy.sql) creates them as system-versioned temporal tables. See also the [data model](data-model.md).

| ID | Name | Kind | Purpose |
|---|---|---|---|
| C47 | Data Classification | Configuration | Ranked sensitivity words in the organization's Purview label names. Each carries the default handling rules |
| C48 | Data Domain Level | Configuration | The levels of the tree for this install, with a rule for each level |
| C49 | Data Domain | Configuration | One node of the business data tree |
| C50 | Data Platform | Configuration | A kind of store from any vendor |
| O25 | Data Location | Operational, a link | One concrete store: an environment, server, database, account, org, tenant, host or library |
| O26 | Data Mapping | Operational | This domain's data sits at this location, in these containers, with this origin |
| O27 | Data Use | Operational | This application reads or writes this domain, at this location, for this purpose, with or without AI |
| O06, changed | Owner Assignment | Operational | Gains a subject: an application, a data domain or a data location. Exactly one is set |
| O09, changed | Decision | Operational | Gains four subject kinds: data use, data mapping, data domain and data location |
| O02, changed | Business Application | Operational | Gains Holds No Business Data, for an application such as meeting room signage |

The data area entity of the earlier design is withdrawn. Its classification, owner, steward and retention moved to the domain and to owner assignments.

### The main fields

- **Data Classification:** a rank (higher is more sensitive, unique, following the label priority), the tier answer this class implies for the sensitivity question, and three default rules: for use, for AI use and for a copy to test.
- **Data Domain Level:** a rank (1 is the top), whether each domain at this level needs its own data owner, and whether applications may declare uses at this level.
- **Data Domain:** its parent, its level, a classification (required at the top; empty below means "inherit"), optional stricter rules for the branch, an optional retention class, and examples in plain words ("salary, deductions, tax codes, bank details for pay") that an AI client can use to place data. The key never changes: a rename changes the label, a move changes the parent. Owners are not fields here; they are owner assignments.
- **Data Platform:** a location kind, a vendor, what an instance, a store and a container are called on this platform, the link kind its locations use, and optional patterns an ID or URL must match.
- **Data Location:** its platform, an optional parent (a database in a server, a library on a host; at most two levels), a stage, an optional deployment target and the application it is, the tool's own ID and URL, the label as last seen, and when it was last seen. Its External Id is a restricted column, like a deployment target's.
- **Data Mapping:** a domain, a location, the container names as seen (labels only, never data), the data origin (system of record, replica, production copy, masked copy or synthetic), and the mapping it was copied from.
- **Data Use:** a record number such as `DU-0042`, the application, the domain, the location when known, the access (read, write or read-write), the purpose in one sentence, whether the data reaches AI and through which service, whether only de-identified data is used, and the access approach.

### Keys

| Entity | Unique key |
|---|---|
| Classifications, levels, domains, platforms | The key within the configuration set, never reused. Classification and level ranks are unique too |
| Data Location | Platform and External Id, among locations in service (not retired and not archived). The URL when there is no ID |
| Data Mapping | Domain, location and origin, among mappings not ended |
| Data Use | The record number. Also application, domain, location and the AI flag, among uses not ended; an empty location counts as one value |

## Owners at any level

### Roles on data

| Owner role | Held on | Inherits down | Deputy | Duties |
|---|---|---|---|---|
| Data owner | A data domain, or an application where domains are optional | Yes | Required | Decides data use, AI use, copies and retention. Approves a loosened rule in their branch. Consulted on tree changes in their branch |
| Data steward | A data domain | Yes | Not required | Keeps mappings and examples current. Consulted on data-use decisions. Approves nothing by default |
| Technical owner | An application or a data location | No | Required | Keeps the location and its mappings current |

A new product role, the **data governance lead**, appoints the owners of top-level domains, keeps locations and mappings current, and sees every data gap. In the Starter preset the platform admin does this instead.

### Who answers

For an owner role that inherits down, the engine looks for a current holder on the domain itself, then on its parent, up to the top. The answer names the holder, the holder's deputies, and where the role was inherited from. If the holder is disabled, deleted or leaving, the design lets the deputy act. The engine already works out who would act, but no test checks that yet; the data checks report the inactive holder as a gap (DC-04). A deputy is never inherited apart from the holder, and a steward inherits on its own. This is engine operation OP-15, and the tree walk runs in memory from the configuration snapshot.

**Example, from the fictional demo.** Timesheets has no owner of its own. Its parent, People, has Hana Lindqvist as data owner and Rosa Tamaki as deputy. So Timesheets resolves to Hana, inherited from People one level up, with Rosa as deputy.

### Effective values on a domain

- **Classification:** the nearest value set on the domain or above it. Every top-level domain has one.
- **Each handling rule:** the strictest of every value set on the domain and above it, together with the classification's default. A branch can tighten a rule and never loosen it. To loosen a rule, change it where it is set, through a configuration change that the owners of the touched domains must approve.
- **Retention class:** the nearest value set. It may be empty.

### Appointing owners

An owner of a domain or a location is appointed by an ownership decision whose subject is that domain or location (`propose_owner_change`). The successor accepts, as for an application.

| Subject | Who is accountable (Balanced) |
|---|---|
| A top-level domain | The data governance lead |
| A lower domain | The data owner of the parent domain |
| A data location | The platform admin |

The owner check, the leaver process and the deputy rules cover domain and location owners with no new code path: `run_owner_check` reads every owner assignment whatever its subject, and `record_owner_leave` lists the domains and locations a leaver holds next to the applications.

## Technical locations

### Any vendor: platform, location, container

Balanced ships 20 platforms ([`data-platform.json`](../../src/config/presets/balanced/data-platform.json)). An organization adds its own through a configuration change. Only product names ship, never a tenant identifier.

| Location kind | Platforms in Balanced | Top-level location | Child location | Container names on a mapping |
|---|---|---|---|---|
| App platform | Dataverse | Environment | | table |
| CRM | Salesforce, Dynamics 365 | Org or environment | | object or table |
| ERP | SAP ERP, Dynamics 365 Finance | System | Client or legal entity | module or business object |
| Relational database | Azure SQL, SQL Server, Oracle Database, PostgreSQL | Server | Database | schema.table |
| Document database | Cosmos DB | Account | Database | container |
| File store | SharePoint, file share, SFTP drop | Site or server | Library or share | folder or file |
| Lake | Fabric lakehouse | Workspace | Lakehouse | table or path |
| SaaS application | SaaS application, Workday, ServiceNow | Tenant | | module or object |
| Legacy | IBM i, mainframe | Host or partition | Library or subsystem | file or dataset |
| Other | Other | Any | Any | Any |

Locations stop at two levels, an instance and a store inside it. Table names stay as text on the mapping, so the product does not drift into being the catalog.

### Stages and data origin

- Every top-level location has a target stage, and a child takes its parent's. The copy rule reads whether the stage is production, never a stage key.
- A Dataverse environment that is also a deployment target links to it, so the Locations tab and the Places page link to each other.
- **System of record:** where retention applies.
- **Replica:** a read copy, such as a lakehouse replica. Copies carry the class: a replica of safety findings is mapped to Safety findings, so an application that reads the replica meets the same rule as one that reads the source.
- **Production copy or masked copy:** checked against the copy rule when the location is not production. **Synthetic:** never checked.

### Never stored

- Connection strings, keys, passwords, tokens or signed URLs. A guard rule refuses them in a location's External Id, URL and container names.
- Data values, row counts, column lists or samples.
- Microsoft's inventory. A location is a link with a "last seen" date.

### How locations become known

A person or an AI client registers a location with `save_data_location`. An AI client's row is a draft until a person confirms it. A table becomes known only when an application declares a use or a steward maps a domain. The product never scans.

## How an application declares a data use

### In the AI build loop

From the fictional demo: Priya Castell asks Claude Code to build Mileage Claims, which writes mileage payments into payroll.

1. Claude Code calls `check_data_use` with the application, the domain Paychecks, access write and no AI. The answer: the data is restricted, the use rule is "decision", so a decision is needed; the data owner is Greta Lund, inherited from Payroll, with no deputy; and the sensitivity answer this data implies is "restricted".
2. Or, while it reads the IBM i library PAYLIB, file PAYMAST, it calls `resolve_data` and gets the same domain and owner.
3. `save_data_use` saves the use as a draft for Priya.
4. Priya confirms the use in the web app, and the decision goes to Greta Lund. No AI approves anything.

### In the web app

The Data panel on Application 360 has **Declare data use** in a side sheet, with two ways in: start from a domain in the tree, or start from a location and pick from the domains mapped there. The person sets access, purpose, AI and de-identified, previews the answer with `check_data_use`, then saves. The preview names who will be asked and why.

### The decision state

The state of a use is worked out when it is read (engine operation OP-16), never stored.

| State | When |
|---|---|
| Not needed | The governing rule is "allowed" |
| Needed | The rule asks for a decision, and there is no accepted allow |
| Proposed | A data-use decision is waiting. Pages show, for example, "1 of 2 approvals" |
| Allowed, Denied | An accepted decision, still valid |
| Re-decide | It was allowed, but the facts have tightened since: a higher classification, or a stricter rule. The engine compares the version the decision was judged by with the version in force |
| Breaks rule | The rule is masked-only and the data is not de-identified, or a never rule applies to a use saved before the rule tightened |
| No one to ask | A decision is needed, but no approver who counts can be found |

- **The governing rule:** for a use without AI, the use rule. For a use with AI, the stricter of the use rule and the AI use rule. For a copy outside production, the copy rule.
- **One decision per use.** Each data owner decides only about their own data. An application with uses in three domains gets three decisions, one per owner, and My work lists them together.
- **Widening a confirmed use** (it starts to write, starts to reach AI, or points at other data) ends it and starts a new one, so it needs a new decision. The earlier decision stays with the earlier row.

### A never rule cannot be broken

A data use, or a copy mapping outside production, whose governing rule is never is refused when it is saved, for a person and an AI client alike (guard rule G-25, reason `data.rule-never`). The refusal names the rule, the domain path and where the rule is set. Ending or archiving a row is never refused, so a use that a later rule change leaves breaking can always be ended. An AI builder hears the answer before it builds: `check_data_use` says the rule is never and that a save would be refused.

Other breaks are recorded and flagged, not refused. An unmasked copy where only masked copies are allowed is fixed in the source tool, so the register keeps showing it until then.

## Balanced defaults

### Classifications

Balanced uses Purview's default label names ([`data-classification.json`](../../src/config/presets/balanced/data-classification.json)). Setup renames them to the organization's own labels.

| Key | Label | Rank | Implied sensitivity answer | Use | AI use | Copy to test |
|---|---|---|---|---|---|---|
| public | Public | 1 | none | allowed | allowed | allowed |
| internal | General | 2 | internal | allowed | decision | allowed |
| confidential | Confidential | 3 | restricted | decision | decision | masked-only |
| restricted | Highly Confidential | 4 | restricted | decision | decision | never |

So in Balanced, public data needs no decision, internal data needs one only when it reaches AI, and confidential and restricted data need one for every use. Restricted data never goes to test.

### Levels and the mode

- Balanced has three levels: Domain (each needs its own data owner), Subdomain and Data set. Starter has two.
- The setting `data-taxonomy-mode` has three values. **Off:** no domains, and data-use decisions work per application. **Domains-optional** (Balanced): domains, mappings and uses all work, and an application that declares no use keeps the application-level data owner. **Domains-required** (Regulated): the application-level data owner no longer counts, and every data-use decision is about a declared use.
- `data-location-stale-days` (180 in Balanced) says when a location counts as stale.
- Balanced ships no domain tree: the tree is the organization's own. The overlay **Data domains starter** gives a generic tree to rename: six domains and fifteen subdomains, with no owners and no tenant facts.

### Approval rules

| Rule (Balanced) | Applies to | Who is asked |
|---|---|---|
| `data-use-domain` | A data use | The domain's data owner must approve, with a typed reason. The steward is consulted |
| `data-use-restricted-ai-owner` and `-security` | Restricted data that reaches AI | The domain's data owner and a security reviewer must both approve. The steward is consulted |
| `data-copy` | A copy outside production | The domain's data owner must approve |
| `data-retention-owner` | Retention of a written use | The domain's data owner approves. The application's business owner is consulted |
| `config-data-touches-admin` and `-owners` | A configuration change that touches the taxonomy | A configuration admin accepts. The touched domains' data owners are consulted |
| `config-data-loosens-admin` and `-owners` | A configuration change that loosens a rule | A configuration admin accepts, and the touched domains' data owners must approve |

On a data-use decision one person counts once across all its approvals, so a data owner who is also a security reviewer cannot meet "both must approve" alone. Balanced's tier 3 self-approval refusal applies here too.

### Gate requirements

At tiers 2 and 3 the Design gate needs data uses declared (at least one open use, or Holds No Business Data) and decided (no open use that is needed, re-decide, breaks rule or no one to ask). The retention check at retirement is the data check DC-13.

## Changing the taxonomy

- **A change to the tree is a configuration change** (process [P11](processes/p11-configuration-change.md)). The configuration admin, or their AI client as a draft, proposes it. The engine computes from the difference whether the change **touches** the taxonomy or **loosens** it: a lower classification, a looser rule, a domain moved to a new parent, or a domain in use retired. It never takes the AI's own words for it.
- **The draft lists the live uses and copies whose state it would move,** both in the proposal's preview and on the stored draft.
- **Moving rows between domains** (`move_data_references`, the platform admin or the data governance lead): a mapping or use moves in place when the data owner stays the same, so its decisions stay with it. When the data owner changes, a decided row is ended and declared again, and the engine asks the new owner. A move into a looser domain warns. A move whose mapping would collide with one already there is refused before anything is written. An AI client may only preview a move.
- **Import** (`import_data_taxonomy`): a CSV, or the rows of a Fabric or Purview domain list that the admin's AI client reads with the admin's own token, becomes one configuration draft. The product holds no Fabric or Purview permission. Owners in the file become prepared owner changes, never live owners. A re-import changes nothing that already matches, and rows missing from the source are listed, never retired. Where the file names another data owner, the import's answer lists the difference (DC-15); it is never stored.
- **Versions.** Every change to the taxonomy is a configuration version with its hash. A retired domain still resolves, so an old decision still shows the path and classification it was judged by. `explain_rules` traces a use's rule from the top domain down, for example "AI use never, set on People / Payroll".

## Engine

The engine's data operations are in [`src/engine/Product.Engine.Core/Operations/`](../../src/engine/Product.Engine.Core/Operations/) (`DataTaxonomy.cs`, `DataOperations.cs` and `DataGuard.cs`), and the validation rules in [`DataTaxonomyRules.cs`](../../src/engine/Product.Engine.Configuration/Validation/Rules/DataTaxonomyRules.cs). Each rule has a passing and a refusing fixture under [`src/fixtures/`](../../src/fixtures/README.md) (`op-08`, `op-09`, `op-15`, `op-16` and `op-17`).

| Operation | Answers |
|---|---|
| OP-15 Resolve data domain | The path, the level, the effective classification and rules with their source, and the effective holder and deputies per owner role, with "inherited from" and the gaps |
| OP-16 Evaluate data use | For a use or a copy: the governing rule and where it is set, the state, the decision type and the approvers |
| OP-17 Run data checks | The data checks DC-01 to DC-14 for a domain, an application, a location, a person or everything |
| OP-02, OP-07, OP-08, OP-09 | Approval rules scoped by subject, classification, AI use, level and taxonomy change; the two gate requirement kinds; guard rules G-18 to G-25; validation rules V49 to V60 |

### Guard rules

| Rule | What it refuses | Reason code |
|---|---|---|
| G-18 | A use or mapping on a retired domain or a level that takes no uses; a second open use with the same key; a second location with the same URL | `data.domain-retired`, `data.level-not-declarable`, `data.use-duplicate`, `data.location-duplicate` |
| G-19 | An owner assignment with no subject, two subjects, or a subject its owner role cannot hold | `data.owner-subject` |
| G-20 | A decision that would allow a use whose rule is never, or masked-only on data that is not de-identified | `data.decision-cannot-allow` |
| G-21 | An AI client confirming, changing or ending a data row a person confirmed. An AI client's new row starts as a draft | `ai.data-row-draft-only` |
| G-22 | A secret in a location's External Id or a mapping's container names | `link.secret` |
| G-23 | A production copy or masked copy declared at a production location | `data.copy-origin` |
| G-24 | A location more than two levels deep, or a child with a stage of its own | `location.depth`, `location.stage` |
| G-25 | A use or copy whose governing rule is never, when it is saved | `data.rule-never` |

### Validation rules

V49 to V60 check a configuration before it can be activated: the tree has no cycles and no active child under a retired domain; levels and ranks are in order; every top-level domain has a classification; a domain in use cannot be retired; every data decision has someone to ask, for every classification with and without AI; owner subjects match the owner roles; each level has one accountable person for domain ownership; at least one stage is production; and shipped presets hold no domains with tenant facts. V53 and V59 are warnings that the approver sees.

### Data checks

| Check | Finding | Severity |
|---|---|---|
| DC-01 | No data owner on the domain or above it | High |
| DC-02 | The level needs its own owner, and only an inherited one exists | Medium |
| DC-03 | An owner with no deputy, on a domain or a location | Medium |
| DC-04 | The owner or the deputy is disabled, deleted or leaving | High |
| DC-05 | A use needs a decision, or needs one again | High when the use reaches AI, otherwise medium |
| DC-06 | A use needs a decision, and no approver who counts can be found | High |
| DC-07 | A use breaks its rule | High |
| DC-08 | A copy outside production that needs a decision or breaks the rule | High |
| DC-09 | The application's sensitivity answer is below what its data implies | Medium |
| DC-10 | A data dependency, or AI use, with no declared data use and no "holds no business data" | Low |
| DC-11 | A use names a location where its domain has no mapping | Low |
| DC-12 | A location with no technical owner, or not seen for the stale days | Medium |
| DC-13 | A retiring application writes to a system of record with no accepted retention decision | High |
| DC-14 | Rows on a retired domain, only possible after a write outside the engine | High |
| DC-15 | The catalog names another data owner. Only in an import's answer, never stored | Low |

Each finding names the rule, the domain path and the fix in one sentence, and the Gaps tab offers one action per finding.

## MCP tools and REST

The data toolset is in [`src/mcp/Product.Mcp.Core/Backend/InProcess/Tools/`](../../src/mcp/Product.Mcp.Core/Backend/InProcess/Tools/) (`DataReadTools.cs` and `DataWriteTools.cs`), with the contracts in [`src/mcp/contracts/`](../../src/mcp/contracts/README.md). Every tool is also served as `POST /api/{tool}` with the same contract, Tool Policy and engine checks. See [MCP tools](mcp-tools.md).

| # | Tool | Question it answers | What an AI client may write |
|---|---|---|---|
| 76 | `find_data_domains` | Which domains match, and who owns them? | Read only |
| 77 | `get_data_domain` | Everything about one domain: owners up the chain, rules and their source, mappings, uses, decisions, gaps | Read only |
| 78 | `find_data_locations` | Which stores match a platform, stage, parent or text, which are unmapped or stale? | Read only |
| 79 | `resolve_data` | Which data is this table, object or file? | Read only |
| 80 | `check_data_use` | May this application use this data this way, and what is needed? | Read only |
| 81 | `get_data_gaps` | What is missing, broken or waiting in the data? | Read only |
| 82 | `save_data_use` | This application reads or writes this data, or it stopped | Draft; a proposed decision |
| 83 | `save_data_location` | This store exists, moved, or is gone | Draft |
| 84 | `save_data_mapping` | This domain's data sits here, or it left | Draft; a proposed copy decision |
| 85 | `move_data_references` | Move mappings and uses off a domain | Preview only |
| 97 | `list_data_uses` | Every data use across domains, and is each decided? | Read only |
| 98 | `list_data_mappings` | Where each domain's data is kept, and is every copy outside production decided? | Read only |
| 99 | `import_data_taxonomy` | Turn this CSV or catalog list into a configuration draft | A configuration draft |

Existing tools gained data: an application's derived data owners, the Data section of the application card, data gaps in the portfolio summary and in My work, the two data requirement kinds, the implied sensitivity answer as a hint, data subjects on decisions, and the inheritance trace in `explain_rules`. An AI client may submit the data-use decision of a use a person has confirmed. It never submits a copy or retention decision; a person does.

## Screens

The web app's Data page is at `/app/data`, with five tabs: **Domains** (the tree with owners, rules and gaps), **Locations**, **Mapping** (domains against locations, or against applications), **Uses** and **Gaps**. Each domain, location, mapping and use has its own record page under `/app/records/`. Application 360 has a Data panel with **Declare data use**, My work has a Data section for a person with a data part, and the decision page shows a data decision's domain, rule, classification and who else is asked. Each action is a side sheet that previews with the engine and shows its answer. The code is in [`src/ui/harness/src/pages/data/`](../../src/ui/harness/src/pages/data/) and [`data-records/`](../../src/ui/harness/src/pages/data-records/). See [Screens](screens.md).

## The demo

The demo estate holds a fictional taxonomy: a 25-domain tree under Customers, Commercial, People, Field operations, Warehouse and Workplace, 22 locations on nine platforms, from Dataverse environments to an IBM i host and a Fabric lakehouse, 28 mappings and the data uses of the estate's applications, with owners from the fictional demo people. Scenario [S-07](../../src/docs/demo/scenarios/S-07-data-taxonomy.md) plays it: a data owner and a security reviewer answer restricted data that reaches an AI model ("1 of 2 approvals", then allowed); Claude Code and then a person meet the never rule on bank details and are refused with the rule's name; an unmasked copy shows outside production; and a missing data use is declared. The browser tests play the same steps at desktop and phone width.

## Microsoft fit

| Microsoft tool | Holds | The product |
|---|---|---|
| Microsoft Purview Information Protection | The sensitivity label taxonomy | Uses the organization's label names for its classifications, through vocabulary mappings. Purview wins on the words |
| Purview data loss prevention and Power Platform data policies | Enforcement | Linked on a decision, never modeled. If a decision and a policy disagree, the policy is what happens |
| Microsoft Fabric OneLake catalog | Domains, items, item owners and labels | Domains can be matched by vocabulary mapping. Fabric lakehouses are locations keyed on stable IDs. The catalog wins for the asset |
| Purview data governance (Unified Catalog and Data Map) | Governance domains, assets and owners | A one-time import at most. Microsoft says new Purview data governance accounts can no longer be created, and existing capabilities stay in maintenance mode through November 12, 2027, as Microsoft consolidates data governance in Fabric ([Learn](https://learn.microsoft.com/purview/data-governance-overview), checked 2026-11-26) |
| Dataverse | Tables | Named on a mapping when used. Never scanned |

Microsoft suggests starting a label taxonomy with the names Personal, Public, General, Confidential and Highly Confidential, and its default labels use those names ([Learn](https://learn.microsoft.com/purview/get-started-with-sensitivity-labels), checked 2026-11-26). Balanced maps its four classifications to the last four. Personal is for data that is not business data, so it has no classification.

## Not built yet

- A location check in the gap check, which would read Dataverse environments from the Power Platform inventory and SQL and Cosmos DB stores from Azure Resource Graph, as the admin, and raise unregistered stores as claims.
- A review whose subject is a data domain. A domain owner who leaves shows as DC-04 instead.
- Answering several data decisions at once.
- An "as of" read of a domain's owners on a past date.
- A guideline for business data domains, which the copy rule's refusal would cite.
- Drafting of tree changes by the data office for its own subtree. Today the configuration admin drafts every tree change.
- The taxonomy on a Dataverse data store. Only the Azure binding is built.

---

Microsoft, Azure, Dataverse, Dynamics 365, Fabric, Purview, Power Platform, SharePoint and SQL Server are trademarks of the Microsoft group of companies. Claude and Claude Code are products of Anthropic. Other names are trademarks of their owners.
