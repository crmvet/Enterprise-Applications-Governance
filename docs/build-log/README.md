# Build log: Enterprise Applications Governance, the next version

How the next version of Enterprise Applications Governance was designed, and how its first code core was built, over the weekend of Saturday 2026-11-21 and Sunday 2026-11-22. All times are US Eastern. Counts are as of about 8:45 PM on the Sunday, unless a row says otherwise.

AI sessions drafted, built and checked the work. The author made every decision.

## Read this first

- **The product is in design.** Every capability of the next version is something it is designed to do. Nothing in this log says it is released, running or available.
- **The platform.** The first release is designed to run in the client's own Azure subscription ([D-26][dec]).
- **The code is an offline core, not the product.** Its libraries and tests run on a developer machine with fictional data, an in-memory store and a mock sign-in. It runs in no cloud and for no client.
- **The technical work is open source under the MIT License** ([D-27][dec]): the [source](../../src/), the [overview](../overview.md), the [architecture pages](../architecture/), the [step 2 design documents](../design/step2/), the [decision records](../decisions/) and the [diagrams](../diagrams/).
- **The 2021 release** is the only release that ships. It is a Microsoft Dataverse solution, open source under MIT since 2021 and no longer maintained. Its source is in [Solutions](../../Solutions/). It was the author's own work ([D-23][dec]).
- **"The author"** below is the product's author and owner.
- **Decision numbers** (D-01 to D-28) match the [decision records][dec]. D-21 is about internal coordination and is not published. Its number is kept so the sequence has no gaps.

## 1. Timeline

### Saturday 2026-11-21

| Time | What happened |
|---|---|
| About 4:30 PM | Work on the next version starts. A first research pass covers the 2021 release and Microsoft's governance and ALM tools. |
| About 6:00 PM | The research pass ends with a synthesis, checked by a completeness critic. |
| By 6:41 PM | A first plan, an upgrade of the 2021 release, is put on hold. The author chooses a clean, design-first rebuild: first the problems and the Microsoft fit, then the design, then the build. |
| 6:51 to 7:00 PM | Research notes on the problems, the fit with Microsoft's tools, and the interface options. |
| 7:14 to 7:28 PM | Three diagram style samples: a hub, layers, and "who asks what". |
| Shortly before 7:21 PM | [D-01][dec]: the product is an operating model plus a product. A playbook, guidelines, processes and an engine bring the business, IT, AI builders, citizen developers, the PMO, change control and architects together. It is designed never to rebuild what a Microsoft tool already does, and to link to that tool or listen to it instead. |
| 7:21 PM | The problems and fit study, draft 1. |
| 7:21 to 7:41 PM | [D-02][dec]: people and AI clients use the product through MCP first. [D-03][dec]: the change gate (record, flag, advise or enforce) is left open, chosen per client through configuration. [D-04][dec]: the diagrams use all three sample styles, in one palette. |
| 7:36 PM | Research notes on the operating model and on overlap with Microsoft's tools. |
| About 9:10 PM | [D-05][dec]: the product needs a name, not a version label. Until it has one, text says "the product". |
| About 9:23 PM | [D-06][dec]: risk tiers are configuration. Three are designed to ship by default, and a client can have any number. Nothing is hardcoded. [D-07][dec]: decision rights are configuration too, with ready-made presets to start from. |
| About 9:26 PM | [D-08][dec]: change management is built in by default, or linked to the client's own change tool. |
| About 10:25 PM | [D-09][dec]: the product can have its own MCP server. |
| About 10:27 PM | [D-10][dec]: every rule is configuration, and the engine is code. AI is designed to make configuration changes on an admin's instruction. Nobody edits rules by hand. |
| About 10:29 PM | [D-11][dec]: the product is designed to give people its roles only through Entra groups. |
| About 10:37 PM | [D-12][dec]: planning tools are linked, Azure DevOps first, plus Planner and GitHub. The product is designed never to track work itself. [D-13][dec]: change windows are designed to be recorded and flagged, with planned changes on the timeline. [D-14][dec] leaves the AI lead role open for discussion. |
| 10:40 PM | Research note on MCP and Copilot options, checked on Microsoft Learn. |
| About 10:58 PM | [D-15][dec]: no scheduled copy of Microsoft's inventory. An on-demand gap check instead. |
| About 10:59 PM | [D-16][dec]: no Copilot or Teams agent. The product is designed for MCP clients, the app and approval cards. Claude Code is the reference client. |
| About 11:01 PM | [D-17][dec]: the product is designed to require, link and track documentation. People's own AI tools write it. |
| About 11:04 PM | [D-18][dec]: modern pages are the core interface: Overview, Application 360, the dependency map and the timeline. Classic forms are not the main experience. |
| About 11:06 PM | [D-19][dec]: the product is designed never to build pipelines. It is designed to map them, record deployments and offer copy-paste steps. |
| About 11:08 PM | [D-20][dec]: an AI lead role is designed to ship in the default configuration, configurable per client. This settles D-14. |
| 11:14 PM | The problems and fit study, draft 2. |
| 11:35 PM | The author steps away for about 18 hours, with short check-ins overnight. The AI sessions keep working under written rules (section 5.3). |
| Late evening to about 12:10 AM | A Microsoft news sweep: six research notes. Twelve corrections to Microsoft facts are recorded and applied. |

### Sunday 2026-11-22, overnight

| Time | What happened |
|---|---|
| About 12:05 AM | [D-22][dec]: the platform is chosen by what the product needs and what it costs to run, not by the 2021 release. The product needs a secure data store with managed access, not Power Apps. The licensing facts are checked on Microsoft Learn. |
| About 12:40 AM | The step 2 logical design starts, written for both possible data stores. |
| About 12:50 AM | [D-23][dec]: the author confirms that the 2021 release and its name are the author's own work. |
| About 12:58 AM | The foundation study is complete. It compares Dataverse with a database in the client's own Azure on the author's two tests, running cost and admin effort, with security a must. Three independent AI judges rank the client's own Azure first. The author decides later. |
| About 1:18 AM | The step 2 logical design is finished: glossary, configuration model, logical data model, 11 processes, the MCP tool surface, a portable engine and a security model. A critic finds 16 internal conflicts and 23 questions. 17 questions become working defaults, and 6 stay open design questions. |
| About 1:48 AM | The systems map's first Visio set: the product in the middle and Microsoft's tools around it, 105 diagram pages. |
| About 2:13 AM | Visio batch 1: the two foundation options, the naming diagram, and two overview diagrams, a value stream and an ecosystem hub. |
| About 2:30 AM | The solution code starts, offline, from the step 2 design. |
| About 3:52 AM | Visio batch 2: six step 2 diagrams (18 pages) and a naming diagram. |
| About 4:00 AM | Code round R1: 958 of 958 tests pass. |
| About 5:25 AM | Code round R2: 11 of 11 checks pass. |
| About 6:53 AM | The systems map's text generator is fixed: none of its 6,859 text pieces is cut mid-sentence. |
| About 7:14 AM | Code round R3: 13 of 13 checks pass. |
| About 8:45 AM | Code round R4: 14 of 14 checks pass. The review finds no path for an AI to approve, accept or activate. |
| About 9:13 AM | All 16 systems map diagrams are rebuilt in Visio, every connector glued. The final picture critic passes. |
| About 10:01 AM | The systems map is closed: 98 views, 105 images, 1,557 internal links, scrub clean. |
| About 12:12 PM | Code round R5: 15 of 15 checks pass. The MCP security review passes. Seventeen adversarial probes find older gaps in the write guard. |
| About 1:50 PM | Code round R6: the write guard is hardened, then attacked with 108 probes. It holds on 90. No probe gets an AI to approve, accept or activate. 16 of the 18 breaks are fixed, and 2 stay open as design questions. |
| About 2:25 PM | Code round R7 and a design reconciliation: the code's proposals (57 reason codes, 20 data model items) are folded back into the design documents, marked as proposals. |
| About 3:05 PM | Code round R8: a scripted walkthrough in eight steps, with fictional people. In step 4 the AI tries to approve three ways and is refused each time. |

### Sunday 2026-11-22, evening

| Time | What happened |
|---|---|
| About 5:50 PM | [D-24][dec]: the code stays private until the main design choices are settled. D-27 replaces this at 8:25 PM. |
| About 6:00 PM | [D-25][dec]: the technical reference lives in this repository: an overview, the Microsoft fit, the architecture pages and one decision record per decision. |
| About 6:10 PM | [D-26][dec]: the first release is designed to run in the client's own Azure subscription. Most users are owners and reviewers, not builders, and the product is designed so they need no Power Platform license for it. It is designed so that nothing locks it to one platform and the data stays in the client's subscription. The engine stays a portable library, so a Dataverse edition could come later without a rewrite. |
| 8:17 PM | First commit of the working repository: 1,894 files. |
| About 8:25 PM | [D-27][dec]: the technical work is public under the MIT License: the code after a public pass, design documents written for readers, the decision records, the diagrams and this log. Internal notes, goals and plans stay private. This supersedes D-24. |
| About 8:28 PM | [D-28][dec]: the core idea is governing AI development of the apps. The product is designed to enable AI development, not to slow it down. One playbook has an organization track and an IT track, and it opens by naming the old ways of working to avoid. The product's own MCP server is designed so the AI builder can ask the governance questions while it builds. Application portfolio management stays as the record underneath. |
| About 8:30 PM | The platform decision is carried into the design documents, each change marked, not rewritten. |
| About 8:45 PM | The author confirms D-27. The public pass makes the technical files public-grade and writes one public decision record per decision, for the author to review and push. |
| About 11:15 PM | The author decides that an AI session, not the author, commits and pushes this repository once the checks pass. There is never a force push, and history is never rewritten. |
| By 11:17 PM | The two tracks of D-28 are written, as drafts: chapter 0 on governing AI development of the apps, chapter 17 on the old paradigms and how not to slide back, chapters 18 to 22, and guidelines G21 to G24. |

## 2. What the design produced

### 2.1 Design documents

| Document | What it holds |
|---|---|
| Problems and fit study | The problems the product is meant to solve, and how it fits next to Microsoft's tools. A final version after two drafts. |
| Step 2 logical design, seven documents | Glossary and entities, the configuration model, the logical data model, 11 processes, the MCP tool surface, the engine and the security model. Written for both possible data stores, so the platform choice did not mean redoing it. |
| Step 2 working defaults | The 16 internal conflicts settled, the 17 questions adopted as working defaults, and the 6 open design questions. |
| Technical reference for readers | The overview, the Microsoft fit, and the architecture pages: configuration and presets, the data model, one page per process, the MCP tools, sign-in and security, the rules engine and the screens. |
| Decision records | One per decision, D-01 to D-28, each with its context, options, choice and reasons. D-21 is not published. |
| Research notes | 24 notes at the first commit (8:17 PM), on Microsoft's tools, MCP and Copilot options, a Microsoft news sweep and a foundation study. Two more followed that evening, on governing AI development and the playbook outline. They stay private as working files. Public documents cite Microsoft Learn directly. |

### 2.2 Diagrams

Every diagram starts as a layout file. A build script draws it in Visio. Counts are Visio pages.

| Set | Visio files | Pages |
|---|---|---|
| Systems map: the product in the middle and Microsoft's tools around it | 16 | 105 |
| Step 2 design: configuration, data model, processes, MCP and identity, engine, security | 6 | 18 |
| Foundation options: two platform options side by side | 1 | 2 |
| Overview diagrams: value stream and ecosystem hub | 2 | 2 |
| Naming architecture: the places the product name appears | 1 | 1 |
| **Total** | **26** | **128** |

How the diagrams were made:

- Each diagram starts as a layout file with a browser preview.
- A build script draws it in Visio, one diagram at a time. It glues every connector, checks the routes and exports a picture.
- A critic checks every picture. Fixes go back into the layout file, and the diagram is rebuilt.
- Before a rebuilt set replaced the old one, 124 files were backed up.

The public diagrams, with their Visio source, are in [docs/diagrams](../diagrams/). Microsoft's product icons stay under Microsoft's terms, not the MIT License.

## 3. The code

The code is an offline core on a developer machine, with fictional data. The pass counts below come only from runs that happened, with their times. The names still use the placeholder "Product" until the product has a name ([D-05][dec]).

| Module or asset | Status | What it is | Size or tests |
|---|---|---|---|
| Product.Engine.Core | exists | The rules engine library: contracts, reason codes, the engine operations, and the write guard every write passes | 450 tests on .NET 10 and 450 on .NET Framework 4.8 (run 2:13 PM) |
| Product.Engine.Configuration | exists | Preset readers, the snapshot compiler, 44 validation rules, configuration activation and the first-version setup | 313 tests on each runtime (run 2:13 PM) |
| Product.Engine.Runtime | exists | Storage ports, the snapshot cache, and one call per product operation in one transaction | 54 tests on each runtime (run 2:13 PM) |
| Product.Engine.Testing | exists | The fixture runner, 11 design invariants as generated tests, and a replay tool | Used by the test runs |
| Product.Engine.Cli | exists | Command line: validate, build presets, run fixtures, replay | Used by the checks |
| Product.Mcp.Core | exists | The MCP server core: 50 first-release tools, run in one process on an in-memory store | 99 tests on .NET 10 (run 2:13 PM); 100 with the walkthrough test (R8) |
| Product.Mcp.Testing | exists | The mock sign-in and test doubles, kept out of the production code | Used by the tests |
| MCP tool contracts | exists | Input and output schemas for 51 tools | Every schema compiles |
| Configuration schema and presets | exists | Four full presets (Balanced, Starter, Regulated, a Microsoft CoE baseline) and three overlays | 22 preset choices, 0 errors |
| Logical model | exists | A machine-readable data model, plus generators for draft physical schemas | 75 entities |
| Fixtures | exists | Test cases with expected answers | 611 passed (run 2:13 PM), plus 14 replay records |
| Page designs | exists | Overview, Application 360, dependency map and timeline, in a local React and Fluent UI preview with made-up data | Checked at 1280 px and 390 px |
| Playbook and guidelines | exists | 16 playbook chapters, 11 processes, 20 numbered guidelines (2 retired) | Drafts. The two tracks of D-28 were not written yet at 8:45 PM. By 11:17 PM there were 23 chapters (0 to 22) and 24 numbered guidelines (2 retired), all drafts |
| Pipeline record step samples | exists | Azure DevOps, GitHub Actions and a Power Platform Pipelines extension flow | 33 of 33 tests (run 11:57 AM) |
| Setup and admin guides | exists | Five skeletons, 40 Microsoft Learn links, each with its check date | Skeletons |
| The registration effect, the record endpoint host, the approval runner | designed | Not built. The walkthrough marks them as stand-ins | None |

At the first commit the code had 11 .NET projects, 228 C# files and 1,227 files in all.

**Code rounds (all on Sunday 2026-11-22):**

| Round | When | What the round added | Checks | Tests passed | Fixtures passed |
|---|---|---|---|---|---|
| R1 | Done about 4:00 AM | Scaffold, configuration schema, presets and overlays, first engine libraries with 44 validation rules, the 51 tool contracts, the logical model | Builds clean on three targets. 3 of 11 checks could not run on .NET 10 (see note) | 958 of 958 | not reported |
| R2 | Review run 5:02 to 5:20 AM | Verification on both runtimes, 28 new preset tests | 11 of 11 | 1,169 of 1,169 | 119 |
| R3 | Review run 7:00 to 7:15 AM | The engine operations, the playbook and guidelines, the page designs | 13 of 13 | 1,389 of 1,389 | 329 |
| R4 | Review run 8:36 to 8:50 AM | Standard changes, the gap check, the write guard, activation, erasure, guide skeletons, pipeline samples | 14 of 14 | 1,475 of 1,475 | 538 |
| R5 | Run 11:57 to 11:59 AM | The engine runtime, the MCP server core with 50 tools, the generators | 15 of 15 | 1,666 of 1,666 | 547 |
| R6 | Final run 1:43 to 1:44 PM | Write guard hardening; 108 adversarial probes (12:55 to 1:13 PM) | 14 of 14 (samples skipped) | 1,687 of 1,687 | 611 |
| R7 | Run 2:13 to 2:14 PM | 11 invariants as generated tests over all 22 preset choices; the replay tool | 15 of 15 (samples skipped) | 1,733 of 1,733 | 611, plus 14 of 14 replays |
| R8 | About 2:30 to 3:00 PM | The scripted walkthrough, run as a test | 15 of 15 (samples skipped) | 1,734 of 1,734 | 611 |

Note on local builds: in R1 and later, Windows Smart App Control on the build machine sometimes refused to load newly built, unsigned files. Nobody tried to get around it. The same tests ran on .NET Framework 4.8 instead, and later builds loaded.

## 4. Verification practice

| Practice | What it does | Weekend numbers |
|---|---|---|
| The scrub gate | A private list of terms that must never go public, run over every file before it can ship | The code's own scrub step ran over 1,226 source files in R7. The gate also runs before anything is pushed to this repository |
| The claims check | Every statement about the next version is checked against the design. Next-version features are always "designed to", never present tense | Run on public drafts before they ship. This log follows the same rule |
| Adversarial probes | Attacks written to break the rules, run against the code | 17 probes in R5 found older gaps. 108 probes in R6: the guard held on 90, 16 breaks were fixed, and 2 stay open as design questions |
| Adversarial fact checks | Independent checkers try to prove a claim wrong | Three checkers matched every Microsoft quote in one set of drafts to its live Microsoft Learn page. One adversarial pass on the foundation study |
| Critics | A separate agent reviews each deliverable before it counts as done | On the step 2 design, the diagrams, the pictures and each code round |
| Microsoft Learn re-checks | Microsoft facts are checked against Microsoft Learn, with the check date | A news sweep with 12 corrections. Licensing facts checked on Learn when the question came up. 40 Learn links in the guides, each dated |
| Code checks | One command runs every check: build, tests, presets, fixtures, replays, dashes, scrub, JSON, model, content, pages, samples and generated files | 15 of 15 on the last runs |

## 5. How it was made with AI

### 5.1 Who did what

The author:

- started the rebuild, and chose a clean, design-first start over an upgrade of the 2021 release;
- made every decision in the decision records, one question at a time;
- set the two tests for the platform, running cost and admin effort, with security a must;
- chose the platform ([D-26][dec]), opened the technical work under MIT ([D-27][dec]) and set the core idea, governing AI development of the apps ([D-28][dec]);
- keeps the final name, the six step 2 design questions and the change gate open for now;
- decides what is published in this repository.

The AI sessions:

- researched Microsoft's tools and checked facts on Microsoft Learn;
- drafted every document and every option, and recorded each decision with what it means for the design;
- wrote the step 2 logical design for both possible data stores;
- built the 128 Visio diagram pages from layout files;
- wrote the offline code core and its tests, attacked it, fixed what broke, and kept two open points open instead of guessing;
- ran the scrub gate, the claims checks, the critics and the adversarial checks;
- compared the platforms and recommended one. They did not decide. AI never picked the platform or the name.

### 5.2 The decision practice

A person decides. AI drafts the options and applies the answer.

- Each question came with background, an example, options with pros and cons, a recommendation, and what the answer changes.
- Each answer was recorded with its date, time and reason, and with what it means for the design. Agents read the decisions before they write.
- When a later answer changes an earlier one, the record says what it supersedes. For example, D-26 superseded the Dataverse back end of D-02, and D-27 superseded D-24.
- Nothing settled is asked again.
- Two decisions recorded questions left open at the time (D-03 and D-14).

### 5.3 Rules for unattended work

While the author was away, the AI sessions worked under written rules:

- no commit, no push and no branch;
- no sign-in as a person, no interactive sign-in, no writes to any cloud tenant, and no outbound actions such as emails or posts;
- nothing internal goes into the public repository;
- no AI session can approve or decide anything for the author;
- open questions go into one list for the author, each with background, options and a recommendation.

### 5.4 Workflows

The work ran in Claude Code sessions. Inside a session, larger jobs ran as workflows: one agent builds, another integrates, and a critic checks the result before it counts as done.

[dec]: ../decisions/
