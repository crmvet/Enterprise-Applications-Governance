# Demo scenarios: the catalog

Every scenario of the product demo, on fictional data. Each one has a presenter script in [scenarios/](scenarios/), a
ready check in the demo kit (`src/demo/kit/scenarios.json`), an end-to-end test of the same name in
`src/tests/e2e/specs/`, and the section of the product story it belongs to. Every person, group, application, host
and ID here is fictional.

- **Runs:** [5 minutes](run-5-minutes.md) for an executive, [15 minutes](run-15-minutes.md) for the guided story,
  [45 minutes](run-45-minutes.md) for the deep dive. [Tracks](tracks.md) give one route per audience.
- **Claude Code:** [claude-code.md](claude-code.md) holds the exact prompts, the answers and the refusals.
- **Before a demo:** [preparing.md](preparing.md). On stage: the one-page [cheat sheet](cheat-sheet.html).
- **In the web app:** the Demo guide (`/app/demo-guide`) lists the same scenarios and steps, and each link switches to
  the step's persona and opens the exact page.

## How a scenario is written

- **IDs.** A scenario is `S-01` to `S-23`. A step is the scenario ID and a number, such as `S-03.2`. The end-to-end
  test of the step carries the same ID at the start of its title, so a failing test names the presenter step.
- **Each step** gives the persona, the page and the clicks, what to say in one or two sentences, what proves it on
  screen, and how to reset it. "Proof on screen" quotes the words the app shows; the end-to-end test checks the same
  words.
- **Numbers** are those of a fresh store after the demo director, in the order of the 15-minute run. A count that a
  step before it changes says so. Dates move with the day the director ran, so the scripts name them relative to that
  day ("in about three weeks"), except dates the estate holds as facts.
- **Reset.** "Nothing" means the step only reads. "A fresh store" means the step writes, and the way back is a fresh
  local store (`demo local reset`, or `demo local up` again) or the sandbox rebuild of a demo install
  ([preparing.md](preparing.md)).

## The personas

The seventeen personas of `src/adapters/azure/seed/demo-personas.json`. On a demo install the "Acting as" menu in the
top bar switches between them. On a local run the development sign-in page and the account menu (the name at the top
right) list the same seventeen, by the same names and roles ("Mateo Brandt, PMO"). Claude Code connects with its own
header ([claude-code.md](claude-code.md)). The persona file's "members" are the estate's other fictional people (the
Change board's Ines Varga and Felix Arendt, the data owners Greta Lund and Wren Abernathy, and more):
nobody signs in as them, and the Teams page lists them in their committees and teams.

| Persona | Product role | Scenarios |
|---|---|---|
| Lena Okoro | Requester | S-20, S-22, S-23 |
| Priya Castell | Maker | S-02, S-07, S-12, S-21, S-22, S-23 |
| Nadia Ferreira | Application owner | S-04, S-09, S-20, S-22, S-23 |
| Kai Ostrowski | Data owner | S-07, S-16, S-23 |
| Elif Dunmore | Architect | S-06, S-17, S-22, S-23 |
| Hugo Brandvold | Change manager | S-05, S-22, S-23 |
| Tomas Rendel | Change board | S-16 |
| Marcus Oyelaran | Architect and change board | S-06, S-16 |
| Mateo Brandt | PMO | S-01, S-04, S-09, S-13, S-22, S-23 |
| Dmitri Halloran | AI lead | S-08, S-22, S-23 |
| Noor Safadi | Security reviewer | S-07, S-08, S-23 |
| Owen Achterberg | Platform admin | S-03, S-07, S-09, S-11, S-14, S-16, S-21, S-22, S-23 |
| Yusuf Brennan | Configuration admin | S-10, S-23 |
| Jonah Pereira | Configuration admin | S-10 |
| Bianca Ferro | Auditor | S-15, S-19 |
| Theo Marsh | Reader | S-18 |
| Hana Lindqvist | Data governance lead | S-07, S-22, S-23 |

## The catalog

| ID | Scenario | Personas | Use cases | Screens | Story section |
|---|---|---|---|---|---|
| [S-01](scenarios/S-01-portfolio-overview.md) | PMO: the portfolio at a glance | Mateo | UC-05, UC-06, UC-02, UC-03 | Overview, Owners, Reviews | W2 |
| [S-02](scenarios/S-02-ai-build-loop.md) | Maker and Claude Code: governance inside the AI build loop | Priya | P01, P02, P10, UC-05, UC-10, A-AI-BUILD-POLICY | Claude Code, My work, intake wizard, AI activity | W1 |
| [S-03](scenarios/S-03-triage-gap-check-places-leavers.md) | Platform admin: triage, the gap check, places and leavers | Owen | P01, A-DELIVERY, P06, A-PLACES, P08 | Intake, intake request, Gap check, Places, Owners | W7 |
| [S-04](scenarios/S-04-business-owner-approvals.md) | Business owner: approvals, owners and the lifecycle | Nadia, Mateo | UC-01, P03, UC-02, UC-14, P08, P07, A-LIFECYCLE, A-GRADUATION | Approvals, Decision, Application 360, Owners | W3 |
| [S-05](scenarios/S-05-changes-and-deployments.md) | Change manager: changes and flagged deployments | Hugo | P03, P05, A-EXCEPTIONS, A-DEPENDENCIES | New change, Timeline, change record, Changes, Deployments | W4 |
| [S-06](scenarios/S-06-reviews-and-exceptions.md) | Architect: reviews and exceptions | Elif, Marcus | P04, UC-03, UC-04, A-LIFECYCLE, A-EXCEPTIONS | Reviews, review record, Decision, Exceptions | W3 |
| [S-07](scenarios/S-07-data-taxonomy.md) | Data: domains, a data-use card, the never rule and copies | Hana, Kai, Noor, Priya, Owen | P02-DATA, UC-13, P10, A-AI-BUILD-POLICY | Data (all tabs), Decision, Application 360 | W5 |
| [S-08](scenarios/S-08-agents.md) | AI lead and security reviewer: agents | Dmitri, Noor | A-AGENTS, P02, P10 | Agents, AI activity, Approvals, Reviews | W6 |
| [S-09](scenarios/S-09-demand-portfolio-retirement.md) | PMO, business owner and platform admin: demand, the portfolio and retirement | Mateo, Nadia, Owen | A-DEMAND, UC-06, P09 | Demand, Decision, Application 360, Portfolios, Retirement, Data | W7 |
| [S-10](scenarios/S-10-configuration.md) | Configuration admins: change the rules safely | Yusuf, Jonah | P11, UC-11 | Configuration, configuration draft, Decision | W8 |
| [S-11](scenarios/S-11-roles-and-access.md) | Platform admin: roles and access | Owen | A-ROLES, UC-07 | Roles and access | W8 |
| [S-12](scenarios/S-12-guidelines.md) | Maker: guidelines and the playbook | Priya | UC-11, A-AI-BUILD-POLICY | AI activity, Guidelines | W1 |
| [S-13](scenarios/S-13-measures.md) | PMO: measures and slide-back signals | Mateo | A-MEASURES | Measures | W9 |
| [S-14](scenarios/S-14-admin.md) | Platform admin: run the product | Owen | A-ADMIN, UC-08 | Admin | W9 |
| [S-15](scenarios/S-15-auditor.md) | Auditor: answer from the record | Bianca | UC-09, A-EVIDENCE, UC-11, P10 | Evidence pack, Audit trail | W10 |
| [S-16](scenarios/S-16-teams-and-committees.md) | Teams and committees: who answers, who supports, what waits | Owen, Marcus, Tomas | UC-07, UC-02, A-ROLES | Teams, team page, Decision | W2 |
| [S-17](scenarios/S-17-dependency-map.md) | Architect: the dependency map and what breaks | Elif | A-DEPENDENCIES, P03, P09, UC-13 | Dependency map, Application 360, data location | W4 |
| [S-18](scenarios/S-18-search-and-lists.md) | Reader: search, filters and what a reader may do | Theo | UC-05, UC-07, UC-02 | Search, person page, Decision, Applications, Application 360, Teams | W11 |
| [S-19](scenarios/S-19-decision-log-and-documents.md) | Auditor: the decision log and the documents | Bianca | UC-04, UC-09, P07, UC-14 | Decisions, Documents | W10 |
| [S-20](scenarios/S-20-requester.md) | Requester: a request from the web app, with no AI | Lena, Nadia | P01, P02, UC-05, UC-07 | Intake, intake wizard, Approvals, Decision | W7 |
| [S-21](scenarios/S-21-claude-code-refusals.md) | Claude Code over MCP: what an AI client may do, and what it may not | Priya, Owen | UC-10, P10, A-AI-BUILD-POLICY, UC-11 | Claude Code, AI activity, Admin | W1 |
| [S-22](scenarios/S-22-everyday-records.md) | Everyday records: the work between the decisions | Priya, Elif, Nadia, Owen, Hugo, Hana, Lena, Dmitri, Mateo | UC-05, A-DEPENDENCIES, P04, P08, P05, P03, P07, UC-14, P06, A-PLACES, P02-DATA, UC-13, P01, UC-06, UC-01 | Applications, Dependency map, Application 360, Owners, Record a deployment, change record, Documents, Gap check, Places, Data, intake request, Portfolios, Approvals | W12 |
| [S-23](scenarios/S-23-every-other-action.md) | Every other action: drafts, answers, rules and records | Claude Code, Priya, Owen, Hugo, Elif, Nadia, Kai, Hana, Yusuf, Dmitri, Mateo, Lena, Noor | P10, UC-10, UC-01, P04, UC-04, P03, P02, P08, P02-DATA, P11, A-AGENTS, P07, A-MEASURES, UC-06, P09, P01, UC-08 | Decision, AI activity, My work, change, data and review records, Application 360, Data, Configuration, Roles and access, Agents, Documents, Decisions, Measures, Portfolios, Retirement, intake wizard, Admin, document record, evidence pack | W12 |

The end-to-end spec of each scenario is `src/tests/e2e/specs/<ID>-<slug>.spec.ts`, the presenter script's file name
with `.spec.ts`: for example `S-03-triage-gap-check-places-leavers.spec.ts`. `src/demo/kit/scenarios.json` names the
same files in `spec` and `script`.

## The story sections

Each scenario belongs to one section of the product story; the screenshots the end-to-end suite takes per step show
it.

| Section | Story | Scenarios |
|---|---|---|
| W1 | Governance inside the AI build loop: AI drafts, a person accepts, the rules hold the line | S-02, S-12, S-21 |
| W2 | Owners, teams and committees: every application and every domain has an owner and a deputy | S-01, S-16 |
| W3 | People answer every approval, on the decision page | S-04, S-06 |
| W4 | Changes, releases and what they touch | S-05, S-17 |
| W5 | Know the data: domains, owners, never rules and copies | S-07 |
| W6 | Agents with an agreed autonomy | S-08 |
| W7 | From request to retirement | S-03, S-09, S-20 |
| W8 | Rules that change safely, and who may do what | S-10, S-11 |
| W9 | Run it and measure it | S-13, S-14 |
| W10 | Answer the auditor from the record | S-15, S-19 |
| W11 | Find anything | S-18 |
| W12 | Everyday records: the register stays true between the decisions, and every action the web app offers | S-22, S-23 |

## Use cases and where each shows

| Use case | Scenarios |
|---|---|
| P01 Intake and triage | S-02, S-03, S-20, S-22, S-23 |
| P02 Tier and required approvals | S-02, S-08, S-20, S-23 |
| P02-DATA Data uses and data places | S-07, S-22, S-23 |
| P03 Change management | S-04, S-05, S-17, S-22, S-23 |
| P04 Architecture review | S-06, S-22, S-23 |
| P05 Deployment record | S-05, S-22 |
| P06 Gap check and claim | S-03, S-22 |
| P07 Documentation tracking | S-04, S-19, S-22, S-23 |
| P08 Owner leaves | S-03, S-04, S-22, S-23 |
| P09 Retirement | S-09, S-17, S-23 |
| P10 AI draft and human accept | S-02, S-07, S-08, S-15, S-21, S-23 |
| P11 Configuration change | S-10, S-23 |
| UC-01 Approvals | S-04, S-22, S-23 |
| UC-02 Owners across the portfolio | S-01, S-04, S-16, S-18 |
| UC-03 Reviews and cadences | S-01, S-06, S-23 |
| UC-04 Decisions and the decision log | S-06, S-19, S-23 |
| UC-05 The register and search | S-01, S-02, S-18, S-20, S-22 |
| UC-06 Portfolios | S-01, S-09, S-22, S-23 |
| UC-07 Each role sees what it needs | S-11, S-16, S-18, S-20, and every scenario by persona |
| UC-08 Notifications and reminders | S-14, S-23 |
| UC-09 Audit and the ledger | S-15, S-19 |
| UC-10 The MCP server and AI clients | S-02, S-21, S-23 |
| UC-11 The playbook and guidelines | S-10, S-12, S-15, S-21 |
| UC-12 The page plan | the whole run |
| UC-13 Data places | S-07, S-17, S-22, S-23 |
| UC-14 Documentation tracking | S-04, S-19, S-22, S-23 |
| A-AGENTS | S-08, S-23 |
| A-AI-BUILD-POLICY | S-02, S-07, S-12, S-21 |
| A-ADMIN | S-14 |
| A-DELIVERY | S-03 |
| A-DEMAND | S-09 |
| A-DEPENDENCIES | S-05, S-17, S-22 |
| A-EVIDENCE | S-15 |
| A-EXCEPTIONS | S-05, S-06 |
| A-GRADUATION | S-04 |
| A-LIFECYCLE | S-04, S-06, S-23 |
| A-MEASURES | S-13, S-23 |
| A-PLACES | S-03, S-22 |
| A-ROLES | S-11, S-16 |

## Screens and where each shows

| Screen group | Scenarios |
|---|---|
| My work, Approvals, the Decision page | S-02, S-04, S-07, S-08, S-09, S-10, S-16, S-20 |
| Overview, Portfolios, Demand, Retirement | S-01, S-09 |
| Applications, Application 360, Dependency map | S-04, S-07, S-09, S-17, S-18 |
| Intake and the intake wizard | S-02, S-03, S-20 |
| Owners, people, Teams and committees | S-01, S-03, S-04, S-16, S-18 |
| Reviews, Documents | S-01, S-06, S-08, S-19 |
| Changes, Timeline, Deployments, Places | S-03, S-05 |
| Gap check | S-03, S-09 |
| Data (Domains, Locations, Mapping, Uses, Gaps) | S-07, S-09, S-17 |
| Agents, AI activity | S-02, S-08, S-12, S-21 |
| Decisions, Exceptions, Audit trail, Evidence pack | S-06, S-15, S-19 |
| Configuration, Roles and access, Guidelines, Measures, Admin | S-10, S-11, S-12, S-13, S-14 |
| Search and list filters | S-18 |
| Everyday records: register, dependencies, leaves, deployments by hand, documents, places, data locations, withdraw, portfolios, the intake request's tier questions | S-22 |
| Every other action: AI drafts accepted, answers, supersede, ask again, tier and gate decisions, data decisions, configuration drafts, agents, reviews, documents and the document record, the evidence pack, the approval outbox | S-23 |

## Order between scenarios

The scenarios run in ID order on one fresh store. Where one needs another, the script says so:

- S-04.5 needs S-03.3 (the platform admin's answer on "Weather alerts for outdoor jobs").
- S-06.3 needs S-04.6 (every data use of Claims Triage Agent decided), or the design decision stays proposed until it
  is.
- S-09 needs S-02 (the crew overtime tracker on the table) and S-04.5 (the weather alerts application registered).
- S-15.3 reads "Version 2 is in force now" only after S-10.4.
- S-16.4 empties the Change board's "Waiting now"; S-19.3 counts one fewer exception waiting after it.
- S-22 plays last: each of its steps writes a record (a registered app, a dependency, a leave, a deployment, a closed
  change, a document link, claims, a target and a pipeline, a data location, a withdrawn request, a portfolio, a risk
  acceptance) that earlier steps would count. After S-22, a store is no longer fresh: reset it before another run.
- S-23 plays after S-22, on the same store: its steps accept what S-23.1 drafts, answer and supersede what S-04 and
  S-02 left waiting, and act on the records S-22 made. S-23.28 needs the demo kit's failed card, on a local run only.
- Played alone, a scenario still works on a fresh store, apart from the counts these notes name.

## A local run and a demo install

| | Local run (`demo local up`) | Demo install (Azure, environment `Demo`) |
|---|---|---|
| Switch persona | Account menu, Development persona | "Acting as" menu in the top bar |
| Claude Code | `X-Product-Dev-Identity: priya` on the local MCP endpoint | The operator's sign-in and `X-Product-Demo-Persona: priya` |
| Claude Code's seeded work | Always there: the director runs it | There after `demo sandbox rebuild` (its director runs over the install's database through a local server); a director run against the install itself does it only with the operator's Claude Code token |
| Writes are stamped | The persona, through "Web app" | The seeded rows: the persona, through "Web app" ("Claude Code" for Claude Code's). Live persona switches: "Web app, demo operator" |
| Reset | `demo local reset` (seconds), or `demo local up` again | `demo sandbox rebuild`, then `demo refresh` |

## What the demo data holds

The estate (`src/adapters/azure/seed/demo-estate.json`) is the static facts; the demo director
(`src/adapters/azure/seed/demo-director.mjs`) makes the open work live through the engine, as the personas, in 64
steps. On a fresh local store after the director:

| Screen | What it shows |
|---|---|
| My work, Approvals | Work for every persona but the auditor; the reader has one owner gap. Kai Ostrowski has the Visit Report Agent data card; Jonah Pereira has Yusuf's configuration change; Hana Lindqvist has the Onboarding Buddy Agent data card and the archive decision |
| Intake | Two requests in triage (three after S-02), Lena's draft "Depot first-aid kit checks", Claude Code's "Toolbox talk sign-off" draft |
| Demand | Four scored requests, one priority waiting, "Weather alerts for outdoor jobs" unscored |
| Changes, Timeline | The windows of the coming weeks, two drafts, a withdrawn change, the closed removal change and the closed emergency |
| Deployments | Seven flagged deployments; three AI-only code approvals (Route Planner 2.0.0-rc.1 from Claude Code, Invoice Matching 1.12.0 and 1.12.1) |
| Places | Thirteen targets, People Test registered by the platform admin; five pipelines |
| Gap check | One finished run: two open claims, one claimed, one dismissed, one vanished (Timesheets) |
| Reviews | Due, overdue and done reviews; Quote Builder's architecture review done today; Identity Directory Sync's access check opened by the daily check; Customer Feedback Bot's security review |
| Decisions, Exceptions | Two standing exceptions (one review overdue) and two waiting |
| Documents | Missing, stale, draft and current documents |
| Owners | Six owner gaps; Wren Abernathy and Amara Quist as leavers |
| Teams | 19 teams: 9 committees, 4 support teams and 6 role groups. The Change board has four members (Tomas Rendel and three people of the estate); every committee but the configuration admins lists more than its persona. Work waits on 8 of the 9 committees (not the Data governance leads, whose lead has her requests asked of her by name) |
| Agents | Six agents; an autonomy proposal and a flagged-agent decision waiting |
| Data | 25 domains, 22 locations, 29 mappings and 31 uses (Claude Code's drafts among them), six data decisions waiting, 31 gaps |
| Configuration | Version 1 in force; Yusuf's change submitted for the second configuration admin |
| Admin | The daily check's run with one review opened and its notices; Claude Code's calls and refused calls |
| AI activity | Drafts taken forward, changed first, withdrawn and waiting; four refused calls; one AI answer (a recommendation only); three AI-only code approvals |
| Audit trail, Evidence pack | Every director write and refused call; Invoice Matching's decisions, answers, deployments and flags |

## Every side sheet of the web app

Each side sheet a person submits in the web app (one form, one write and one answer) is played live, for real, in the
steps named here, and those steps' end-to-end tests perform it and check its proof on screen at both widths. A sheet is
named by its key, the file and component it sits in (the page shows it as `data-action-site`). The test of each step
declares the sheets it performs (`demo.writes('<tool>@<key>')`), and the run fails a step whose declared sheet was not
submitted for real and answered ok. `src/tests/e2e/tools/check-actions.mjs` (in `npm run check` and the `e2e` check of
`tools/ci.mjs`) fails when a sheet of the web app has no row here, when a row names a sheet the web app does not have,
when a row's step is not in a presenter script, or when that step's test does not perform the row's sheet. The last
column lists the other buttons that open the same sheet: they are listed, not played again (choice 14 of
`src/tests/e2e/README.md`), except the evidence pack's "Add evidence" (S-23.31).

| Action | Where | Tool | Sheet | Step | Same sheet, also from |
|---|---|---|---|---|---|
| Run the daily check now | Admin | `run_due_state_refresh` | `pages/admin/AdminPage#RefreshSheet` | S-14.3 |  |
| Send again, Close the card | Admin, Approval outbox | `retry_approval_card` | `pages/admin/AdminPage#CardSheet` | S-23.28 |  |
| Decide on a flagged agent | Agents | `save_decision_proposal` (flagged agent) | `pages/ai-audit/agentSheets#AgentDecisionSheet` | S-23.22 |  |
| Propose an autonomy level | Agents | `save_decision_proposal` (autonomy) | `pages/ai-audit/agentSheets#ProposeAutonomySheet` | S-08.2 |  |
| Request a security review | Agents | `request_security_review` | `pages/ai-audit/agentSheets#SecurityReviewSheet` | S-23.22 |  |
| Declare it, Check and confirm, Change, End the use | Application 360 Data, data use record | `save_data_use` | `pages/data/sheets#DataUseSheet` | S-07.11, S-23.6, S-23.18 | Data, Uses ("Declare a data use"); Application 360, Data ("Declare a data use", "Confirm", "End"); Data, Gaps ("End") |
| It holds no business data | Application 360, Data | `save_application_draft` (holds no business data) | `pages/data-records/App360DataPanel#NoBusinessDataSheet` | S-23.15 |  |
| Propose a decision, a tier decision, the design decision | Application 360, Decisions, Tier, Lifecycle and gates | `save_decision_proposal` | `pages/app360/sheets#ProposeDecisionSheet` | S-22.13, S-23.12, S-23.13 |  |
| Confirm it is still true | Application 360, Lifecycle and gates | `confirm_document_checked` | `pages/app360/sheets#DocumentCheckedSheet` | S-04.9 | Application 360, Summary |
| Confirm it is current (a draft) | Application 360, Summary | `record_document_link` (draft) | `pages/app360/sheets#DocumentCurrentSheet` | S-23.30 | Application 360, Lifecycle and gates |
| Hand over (graduation), Hand over the data owner role | Application 360, Summary and Data | `propose_owner_change` | `pages/app360/sheets#HandOverSheet` | S-04.10, S-23.14 | The person page ("Hand over") |
| Answer the tier questions, Answer again | Application 360, Tier | `answer_tier_questions` | `pages/app360/sheets#TierAnswersSheet` | S-23.12 |  |
| Request an architecture review | Application 360, Work | `request_architecture_review` | `pages/app360/sheets#ArchitectureReviewSheet` | S-22.3 | Application 360, Lifecycle and gates |
| Register an app already built | Applications | `save_application_draft` | `pages/register-extras/RegisterSheet#RegisterBuiltAppSheet` | S-22.1 |  |
| Close the change | Change record | `complete_change` | `pages/changes-records/ChangeActions#CompleteChangeSheet` | S-22.6 |  |
| Record the result, Change the result | Change record | `record_change_result` | `pages/changes-records/ChangeActions#RecordResultSheet` | S-22.6, S-22.28 |  |
| Submit a change, Accept and submit an AI-drafted change | Change record | `submit_record` (change) | `pages/changes-records/ChangeActions#SubmitChangeSheet` | S-05.3, S-23.5, S-23.11 |  |
| Withdraw a change | Change record | `withdraw_record` (change) | `pages/changes-records/ChangeActions#WithdrawChangeSheet` | S-22.30 |  |
| Link a work item, Add evidence | Change record, deployment record, retirement tracker, evidence pack | `save_link` | `pages/changes-records/parts#SaveLinkSheet` | S-22.27, S-22.29, S-23.26, S-23.31 | Audit trail evidence pack on changes, deployments and reviews |
| Import data domains | Configuration | `import_data_taxonomy` | `pages/configuration/importSheet#ImportDomainsSheet` | S-23.20 | Rules view ("Import data domains") |
| Change the draft, Propose a binding change | Configuration draft, Roles and access | `propose_configuration_change` | `pages/configuration/sheets#DraftBuilderSheet` | S-23.20, S-23.21 | The blank builder on Configuration; the Rules view's Add, Change and Retire; Data, Domains ("Propose a tree change" and its row pencil) |
| Move its rows | Data domain record | `move_data_references` | `pages/data/sheets#MoveDataReferencesSheet` | S-22.14 | Data, Gaps ("Move the rows") |
| Name a data domain owner, Propose a technical owner for a location | Data domain record, Data Locations | `propose_owner_change` (domain, location) | `pages/data/sheets#ProposeDataOwnerSheet` | S-22.34, S-23.19 | Data, Gaps ("Propose an owner") |
| Map data here, Change the copy, Change, End the mapping, Check and confirm | Data location record, Data Mapping, mapping record | `save_data_mapping` | `pages/data/sheets#DataMappingSheet` | S-22.10, S-22.35, S-23.7, S-23.17 | Application 360, Data ("Map the data") |
| Propose a decision (DC-05), a retention decision (DC-13), a copy decision | Data, Gaps and Mapping | `save_decision_proposal` (data) | `pages/data/sheets#DataDecisionSheet` | S-23.16, S-23.17 | Application 360, Data ("Propose retention"); the mapping record ("Propose the copy decision") |
| Add a location, Confirm it is still there, Change, Retire, Confirm seen, Check and confirm | Data, Locations, data location record | `save_data_location` | `pages/data/sheets#DataLocationSheet` | S-22.10, S-22.33, S-22.35, S-23.7, and 1 more |  |
| Approve, Reject, give advice | Decision page | `answer_approval` | `components/actions#AnswerSheet` | S-03.3, S-04.3, S-04.4, S-04.5, and 14 more | Approvals and My work ("Answer" opens the decision page); the team page ("Answer on the decision page") |
| Edit the AI draft | Decision page | `save_decision_proposal` (update) | `pages/work-approvals/decisionSheets#EditProposalSheet` | S-23.2 |  |
| Add evidence | Decision page | `save_link` (evidence) | `pages/work-approvals/decisionSheets#EvidenceSheet` | S-23.10 | Audit trail evidence pack on a decision |
| Supersede | Decision page | `save_decision_proposal` (supersedes) | `pages/work-approvals/decisionSheets#SupersedeSheet` | S-23.10 |  |
| Submit, Accept and submit | Decision page, AI activity, My work, configuration draft | `submit_record` | `pages/work-approvals/parts#SubmitSheet` | S-23.2, S-23.3, S-23.4, S-23.20 | Configuration history ("Submit"); the register sheet ("Send for confirmation") |
| Withdraw a decision, a draft use, location, mapping, configuration draft or draft application | Decision page, data records, configuration draft, Application 360 | `withdraw_record` | `pages/work-approvals/parts#WithdrawSheet` | S-22.19, S-22.36, S-23.6, S-23.7, and 1 more | Configuration history ("Withdraw") |
| Propose an exception | Decisions | `save_decision_proposal` (exception) | `pages/guide-log-docs/DecisionSheets#ProposeExceptionSheet` | S-23.25 |  |
| Propose a decision, Record a decision about a signal | Decisions, Measures | `save_decision_proposal` | `pages/guide-log-docs/DecisionSheets#ProposeDecisionSheet` | S-23.25 |  |
| Score the request | Demand | `score_intake_request` | `pages/intake/sheets#ScoreSheet` | S-09.2 | The intake request ("Score"); the intake list ("Score") |
| Propose priority | Demand | `save_decision_proposal` (priority) | `pages/work-approvals/decisionSheets#NewProposalSheet` | S-09.3 | The intake request ("Propose priority") |
| Change, End a dependency | Dependency map, Application 360 Data | `save_dependency` (change, end) | `pages/app360/sheets#DependencyChangeSheet` | S-22.24 |  |
| Add a dependency, Add a data link | Dependency map, Application 360 Data | `save_dependency` | `pages/app360/sheets#NewDependencySheet` | S-22.2, S-22.23 |  |
| Record a correction | Deployment record | `record_deployment_correction` | `pages/deployments-places/sheets#CorrectionSheet` | S-22.25 | Deployment record ("Not this change?") |
| Propose an exception | Deployment record | `save_decision_proposal` (exception) | `pages/Records#ProposeExceptionSheet` | S-22.26 | Deployments list ("Propose an exception") |
| Confirm still true, Mark stale | Document record | `confirm_document_checked` | `pages/Records#CheckedSheet` | S-23.24 |  |
| Confirm it is current (a draft) | Document record | `record_document_link` (draft) | `pages/Records#ConfirmCurrentSheet` | S-23.29 |  |
| Update the link, Link a missing document | Document record | `record_document_link` | `pages/Records#DocumentLinkSheet` | S-23.24 | Application 360, Summary ("Link a missing document") |
| Link a document | Documents | `record_document_link` | `pages/guide-log-docs/DocumentSheets#LinkDocumentSheet` | S-22.7 |  |
| Confirm current | Documents, Drafts | `confirm_document_checked` | `pages/guide-log-docs/DocumentSheets#ConfirmDocumentSheet` | S-23.24 |  |
| Send for approval | Follow-up sheet after a proposal | `submit_record` | `pages/data/sheetParts#SubmitFollowUp` | S-04.10, S-08.2, S-09.3, S-09.9, and 16 more | Every proposal sheet that saves a decision |
| Compare a list | Gap check | `compare_inventory_list` | `pages/deployments-places/sheets#CompareListSheet` | S-22.8 |  |
| Check the owners | Gap check | `run_owner_check` | `pages/deployments-places/sheets#OwnerCheckSheet` | S-22.18 |  |
| Run the gap check, Continue the run | Gap check | `run_gap_check` | `pages/deployments-places/sheets#RunGapCheckSheet` | S-22.15 |  |
| Dismiss, Still there, Reopen | Gap check | `dismiss_claim` | `pages/Records#DismissSheet` | S-03.7, S-22.16, S-22.17 | The claim record ("Dismiss", "Resolve") |
| Claim into an application, Claim into a new application | Gap check, Not registered | `claim_resource` | `pages/Records#ClaimSheet` | S-03.6, S-22.17 | The claim record ("Claim it") |
| Resolve: Link its new place | Gap check, Vanished | `link_component` (moved) | `pages/deployments-places/sheets#ResolveVanishedSheet` | S-22.16 |  |
| Link a work item or evidence | Intake request | `save_link` | `pages/intake/sheets#LinkSheet` | S-03.4 |  |
| Answer the tier questions | Intake request | `answer_tier_questions` | `pages/intake/sheets#RecordTierSheet` | S-22.38 |  |
| Withdraw a request or a draft | Intake request, intake wizard | `withdraw_record` (intake) | `pages/intake/sheets#WithdrawSheet` | S-22.11, S-23.27 |  |
| Save the tier answers | Intake wizard | `answer_tier_questions` | `pages/intake/Wizard#TierAnswersSheet` | S-02.5, S-20.3 |  |
| Save as a draft, Save draft | Intake wizard | `save_intake_draft` | `pages/intake/Wizard#Wizard` | S-20.2, S-23.27 |  |
| Submit the request, Accept and submit | Intake wizard, intake request | `submit_record` | `pages/intake/sheets#SubmitSheet` | S-02.7, S-20.4 |  |
| Ask again | My work | `renew_approval_request` | `pages/demo-admin/AskAgain#AskAgainSheet` | S-23.11 | The decision page ("Ask again") |
| Plan the change, Save draft, Edit the draft, Draft the removal change | New change, change record, Retirement tracker | `save_change_request_draft` | `pages/changes-records/ChangeDraftSheet#ChangeDraftSheet` | S-05.2, S-22.29, S-23.11, S-23.26 | Deployment record ("Draft a change after the fact" opens New change) |
| Submit for approval, after Cancel | Owners banner, review record | `submit_record` (banner) | `pages/owners-reviews/OwnerSheets#SubmitDecisionSheet` | S-04.11, S-23.23 |  |
| Propose a deputy, Propose a successor, End an owner assignment | Owners, Application 360 Summary | `propose_owner_change` | `pages/owners-reviews/OwnerSheets#ProposeOwnerChangeSheet` | S-04.11, S-22.21, S-22.22 | Application 360 owner rows ("Propose a successor"), Summary ("Propose an owner") and Data ("Propose a data owner", "Propose a deputy") |
| Record a leave, Change the leave date | Owners, Leavers | `record_owner_leave` | `pages/owners-reviews/OwnerSheets#RecordLeaveSheet` | S-22.4, S-22.20 | Application 360 owner rows ("Record a leave", or "Change the leave date" for a person with one) |
| Map a pipeline, Change the map | Places, Pipelines | `map_pipeline` | `pages/deployments-places/sheets#MapPipelineSheet` | S-22.9, S-22.32 |  |
| Register a target, Change the target | Places, target page | `save_deployment_target` | `pages/deployments-places/sheets#TargetSheet` | S-22.9, S-22.31 |  |
| Retire, Bring back | Portfolio page | `save_portfolio` | `pages/pmo-portfolio/sheets#PortfolioActiveSheet` | S-22.37 |  |
| New portfolio inside, Edit | Portfolio page | `save_portfolio` | `pages/pmo-portfolio/sheets#PortfolioSheet` | S-22.12, S-22.37 |  |
| Record the deployment | Record a deployment | `record_deployment` | `pages/deployments-places/sheets#RecordDeploymentSheet` | S-22.5 |  |
| Confirm retired | Retirement tracker | `link_component` (retired) | `pages/pmo-portfolio/sheets#RetireComponentSheet` | S-09.8 |  |
| Propose retirement complete, a portfolio decision, from the review pack | Retirement tracker, Portfolio page | `save_decision_proposal` | `pages/pmo-portfolio/sheets#ProposeDecisionSheet` | S-09.9, S-23.26 | Retirement tracker, step 3 ("Propose the retention decision") |
| Add evidence | Review record | `save_link` (evidence) | `pages/owners-reviews/ReviewSheets#AddEvidenceSheet` | S-23.23 |  |
| Complete the review | Review record | `complete_review` | `pages/owners-reviews/ReviewSheets#CompleteReviewSheet` | S-06.4 | Application 360, Work ("Complete the review") |
| Propose the outcome | Review record | `save_decision_proposal` (review outcome) | `pages/owners-reviews/ReviewSheets#ProposeOutcomeSheet` | S-23.23 |  |
| Retire, Return to service | Target page | `save_deployment_target` | `pages/deployments-places/sheets#RetireTargetSheet` | S-22.31 |  |

Not played on stage, by choice: the approval cards in Teams and Outlook need a tenant; on a local run or a demo install
the Admin page's approval outbox shows them queued for the approval runner.

## Choices and reasons

Recorded on 2026-11-25, when the scenarios were walked step by step on a fresh local store:

- **The 23 scenarios keep the guided story's order.** S-01 to S-15 are the fifteen steps of the guided story, S-16 is
  the Teams page, and S-17 to S-23 cover what the story only touched: the dependency map, search and filters, the
  decision log and documents, a requester without AI, Claude Code's refusals, the everyday records, and every other
  action. Why: the demo kit's checks, the Demo
  guide and earlier notes already used S-01 to S-16, so their IDs stay valid.
- **Step IDs are the test titles.** The end-to-end test of `S-03.2` starts its title with `S-03.2`. Why: a failing
  test then names the presenter step to fix, and a screenshot per step lands in a folder by the same ID.
- **Proof quotes the app's own words.** Every "Proof on screen" was read from the running app on a fresh local store,
  walked step by step. Why: the end-to-end suite checks the same words, so a script cannot drift from the product
  without a test failing.
- **Counts are a fresh store's, in run order.** Why: the store changes as the scenarios play; naming the order makes
  each count checkable.
- **A step that needs Claude Code says what happens without it.** Why: on a demo install the director does Claude
  Code's part only with the operator's token, and the scenario must still play.
- **No step relies on a record number, except as an example.** Why: record numbers follow the order the director and
  the live demo wrote them, which differs between a local run and a demo install.
- **"Check first" says what the submit would do.** Fix round 1 made the check of a submit list the requests the submit
  would open (one approval and three consultations for the crew overtime tracker), and the check of a proposal name
  the role the submit asks after its fallback (the business owner when no sponsor is named). Why: a preview that
  differs from the submit cannot be shown on stage, and an AI client reads the same answer.
- **A second architect and change board member answers.** Marcus Oyelaran, a second architect and change board member,
  is a persona (seventeen in all), so he answers S-06.3 (Elif's design decision) and S-16.4 (Tomas's Desk Booking
  exception): the person who asked is not the one who answers. S-04.4 shows Balanced letting a proposer answer at tier
  2, on purpose, and S-22.13 shows the tier 3 separation rule refusing it. Why: on stage, "can they approve their own?"
  has one deliberate yes and one refusal, and the other answers come from another person.
- **Shown live, and shown as seeded.** Every side sheet the web app offers is played live in a step and performed for
  real by that step's end-to-end test: the table "Every side sheet of the web app" above lists each one, and
  `src/tests/e2e/tools/check-actions.mjs` proves the list complete against the web app's own sheets. A button that
  opens a sheet already played is listed with that sheet, not played again; the evidence pack's "Add evidence" is played
  all the same (S-23.31), because it is where an auditor reads (fix round 1, 2026-11-26: the coverage was per write tool,
  and four sheets of tools already covered went unplayed). The everyday ones are S-22 and the rest S-23; both play last,
  so no earlier count changes. The places, the first gap check run and
  some data rows the earlier scripts show were written by the director, as a person would have written them earlier.
  The approval cards in Teams and Outlook need a tenant: on a local run or a demo install the Admin page's approval
  outbox shows them queued for the approval runner.
- **Moving a domain's rows is played on an existing merge.** S-22.14 moves the two mappings and two uses of Customers /
  Client cases and feedback into Customers / Client accounts and contacts: the same data owner, no configuration
  change, and no mapping that would collide with one already there. Why: Warehouse / Stock levels into Parts catalog
  was the first choice, but both are mapped at the same store as the system of record, and the move then met the
  store's key (fixed in this wave: such a move is now refused with the reason, in the check and the write). A split
  into a new subdomain needs a configuration change first, which S-23.20 shows as an import draft.
- **The gap check run reads the demo's fictional inventory.** On a demo install and a local run the product reads its
  own register, the claims' items and `src/adapters/azure/seed/demo-inventory.json` (one new item, two absent), in
  pages of 25 on every install, so S-22.15 shows a new claim, two vanished items and "Continue the run". The owner check
  of S-22.18 reads the demo's fictional directory the same way: the account states and leave dates the estate records.
