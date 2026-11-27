# S-22 Everyday records: the work between the decisions

| | |
|---|---|
| Personas | Priya Castell, maker; Elif Dunmore, architect; Nadia Ferreira, application owner; Owen Achterberg, platform admin; Hugo Brandvold, change manager; Hana Lindqvist, data governance lead; Lena Okoro, requester; Dmitri Halloran, AI lead; Mateo Brandt, PMO |
| Use cases | UC-05, A-DEPENDENCIES, P04, P08, P05, P03, P07, UC-14, P06, A-PLACES, P02-DATA, UC-13, P01, UC-06, UC-01 |
| Screens | Applications, Dependency map, Application 360 (Work, Decisions), Owners, Record a deployment, the change record, Documents, Gap check, Places, Data (Locations) and the data location record, the intake request, the portfolio page, Approvals |
| Time | 30 minutes in full; deep dive only |
| End-to-end spec | `src/tests/e2e/specs/S-22-everyday-records.spec.ts` |
| Story section | W12 Everyday records |
| Ready check | `demo check --base <url> --scenario S-22` |

**Before it starts.** Play it last. Every step writes a record that an earlier scenario would count (a new application
draft, a dependency, a leave, a deployment, a closed change, a document link, two claims, a target and its pipeline, a
data location and its mapping, a withdrawn request, a portfolio and a risk acceptance), so the earlier scripts' counts
hold only before it. Each step is one everyday act a person does in the web app, checked by the engine as it checks an
AI client's call. S-22.14 to S-22.37 play the rest of the everyday records: moves, the gap check run and its
answers, the owner check, leaves and successors, data links, corrections, change drafts and results, targets,
pipelines, locations, mappings, withdrawals and portfolio edits. Dates in the answers are the day you play it.

**Reset.** A fresh store (`demo local reset` on a local run).

## Steps

### S-22.1 Register an app already built

- **Persona:** Priya Castell, maker.
- **Page and clicks:** Applications, "Register an app already built". Name "Depot key register", What it is for "Lists
  which depot keys each crew member holds, and when each key comes back.", Kind "Power Platform app". The business
  owner is Priya, as the form starts. Check first, Save the draft, then "Send for approval" in "Send it for approval
  now?".
- **Say:** "An app built before the product came joins the register as a draft. It is registered only when its owners
  confirm, so nobody becomes an owner without saying yes."
- **Proof on screen:** after the send: "Submitted. 2 approvals are asked. Only people answer, on the decision page."
- **Reset:** a fresh store.

### S-22.2 Add a dependency

- **Persona:** switch to Elif Dunmore, architect.
- **Page and clicks:** Visit Report Agent's dependency map, "Add a dependency". Which way "Visit Report Agent needs the
  other application", the other application "Field Knowledge MCP Server", Kind of dependency "Calls API of", Criticality
  "Medium". Check first, then Record it.
- **Say:** "Dependencies are recorded as people report them, and the map, the change page and the retirement tracker
  read them at once."
- **Proof on screen:** "The dependency is recorded, as reported: Visit Report Agent calls API of Field Knowledge MCP
  Server." The map then lists Field Knowledge MCP Server.
- **Reset:** a fresh store.

### S-22.3 Request an architecture review

- **Persona:** switch to Nadia Ferreira, application owner.
- **Page and clicks:** Contractor Portal, the Work tab, "Request an architecture review". What to review "The new
  sign-in page sends contractors to their own company sign-in. Please check the design before the change goes ahead.",
  Due on two weeks from today. Check first, then Request the review.
- **Say:** "An owner asks the architect to look at a design. It is a request, never a decision: the architect's review
  produces the design decision."
- **Proof on screen:** "The architecture review is requested. It is a request, never a decision. A review was opened".
- **Reset:** a fresh store.

### S-22.4 Record a leave

- **Persona:** switch to Owen Achterberg, platform admin.
- **Page and clicks:** Owners, "Record a leave". The person "Ines Varga", the leave date 30 days from today, What
  happens "Leaves the organization", a note. Check first, Record the leave, and confirm.
- **Say:** "A leave is recorded as reported. Owner checks open on everything the person owns, and their pending
  approvals move to the deputy. Nobody changes owner by this alone."
- **Proof on screen:** "7 owner roles are affected, and 1 owner role on data (Field operations); a successor needs an
  accepted ownership decision." "12 owner check reviews cover this leave (9 new, 3 already open)."
- **Reset:** a fresh store.

### S-22.5 Record a deployment by hand

- **Persona:** switch to Hugo Brandvold, change manager.
- **Page and clicks:** Record a deployment for Inspection Scheduler, from the change "Rotate the reminder flow
  connection"
  (`/app/deployments/new?app=00000000-0000-4000-8000-000000000511&change=00000000-0000-4000-8000-000000004205`). "Record
  the deployment": Target "Field Ops Prod", Artifact name "InspectionScheduler", Version "3.4.3", Finished at the local
  time of 07:40 UTC on 25 Nov 2026 (the form is in the presenter's own time zone: 02:40 in New York), inside the
  change's window. Check first, then Record it.
- **Say:** "Some work never goes through a pipeline. A person records it, and the engine works out the flags: this
  target expects its pipeline."
- **Proof on screen:** "The deployment is recorded as reported. The engine set the route and the flags. The engine
  flagged it: came another way."
- **Reset:** a fresh store.

### S-22.6 Record the result and close the change

- **Persona:** Hugo Brandvold.
- **Page and clicks:** the change record "Rotate the reminder flow connection". "Record the result": Result
  "Succeeded", a note. Check first, Record the result. Then "Close the change", Check first, Close the change.
- **Say:** "How it ended is recorded as reported, and a person closes the change. A post-change review opens only when
  the change type asks for one or the change did not fully succeed."
- **Proof on screen:** "The change result is recorded as reported." Then "The change Rotate the reminder flow
  connection is closed with the result succeeded."
- **Reset:** a fresh store.

### S-22.7 Link a document

- **Persona:** switch to Dmitri Halloran, AI lead and deputy technical owner of Supplier Onboarding.
- **Page and clicks:** Documents, "Link a document". Application "Supplier Onboarding", Document type "Runbook", Link
  `https://wiki.example.org/finance/supplier-onboarding/runbook`, Status "Current: I read it and confirm it", Last
  updated three days ago. Check first, then Link it. Then "Link a document" again for the same runbook.
- **Say:** "The product keeps the link and the status people report. It never reads or writes the document. A second
  link for a requirement that already has one is refused with the link to update."
- **Proof on screen:** "The document link is recorded with status current, as reported. The product never writes the
  document." The second try is refused: "Supplier Onboarding already has its runbook linked. Update that link
  instead."
- **Reset:** a fresh store.

### S-22.8 Compare a list from your own tool

- **Persona:** switch to Owen Achterberg.
- **Page and clicks:** Gap check, "Compare a list". Where the list came from "CoE kit export, this morning", The items
  `flow-depot-keys | Depot keys reminder | Cloud flow | Field Ops Prod` and `app-canteen-orders | Canteen orders |
  Canvas app | Field Ops Prod`, one per line. Check first, then Compare it.
- **Say:** "Without a live read of the estate, an export from the admin's own tool does the same job. The run is marked
  as reported, and each item no application holds opens a claim."
- **Proof on screen:** "2 items seen, 2 new claims, 0 vanished. The list came from your own tool, so the run is marked
  as reported." A line with no kind is named in the sheet ("No kind. The gap check leaves out an item whose kind it does
  not know.") and is never sent.
- **Reset:** a fresh store.

### S-22.9 Register a target and map a pipeline

- **Persona:** Owen Achterberg.
- **Page and clicks:** Places, "Register a target": Name "Field Ops Training", Kind "Power Platform environment",
  Stage "Test", Change path "Pipeline only", Link kind "Power Platform resource", ID "field-ops-training". Check first,
  Register it. Then "Map a pipeline": Tool "Azure Pipelines pipeline", ID "inspection-scheduler-training", Link
  `https://dev.example.org/field-ops/_build?definitionId=118`, Name "Inspection Scheduler to training", Application
  "Inspection Scheduler", Step 1 target "Field Ops Training". Check first, Save the map.
- **Say:** "A place deployments land, with the gate level its stage sets and the routes it allows. The pipeline map
  says where a pipeline deploys each application; the product never runs it."
- **Proof on screen:** "The deployment target Field Ops Training is registered. Deployments to it can now be
  recorded." Then "The pipeline is mapped to 1 application and target pair. The product never runs the pipeline."
- **Reset:** a fresh store.

### S-22.10 Add a data location and map data to it

- **Persona:** switch to Hana Lindqvist, data governance lead.
- **Page and clicks:** Data, Locations, "Add a location": Platform "SharePoint", Stage "Production", Name "Depot safety
  binders", Link `https://fieldops.example.org/sites/depot-safety`. Add the location. Then open the new location and
  "Map data here": The data "Field operations / Safety findings", Where this data comes from "System of record". Check
  first, Map the data.
- **Say:** "Where the data sits is a record of its own. Map it once, and every application that uses the domain shows
  where its data lives."
- **Proof on screen:** "1 location is registered." Then "1 mapping is saved."
- **Reset:** a fresh store.

### S-22.11 Withdraw a request

- **Persona:** switch to Lena Okoro, requester.
- **Page and clicks:** her request "Spare parts returns from field vans" (Intake, My requests), "Withdraw". Reason "The
  vans now bring spare parts back through the depot store, so the need went away." Withdraw, and confirm.
- **Say:** "A requester withdraws a request when the need goes away. It stays on record, and nobody is asked any more."
- **Proof on screen:** "Withdrawn. 5 waiting approvals are canceled."
- **Reset:** a fresh store.

### S-22.12 A new portfolio

- **Persona:** switch to Owen Achterberg.
- **Page and clicks:** Portfolios, Field operations, "New portfolio inside": Name "Depot services", Level "Application
  group", Description "The depot apps: keys, stores and the canteen." Check first, then Create.
- **Say:** "The platform admin shapes the portfolio tree. The rules of the parent apply to everything inside it."
- **Proof on screen:** the sheet closes and the page opens the new portfolio, "Depot services", listed inside Field
  operations.
- **Reset:** a fresh store.

### S-22.13 A separation rule refuses a self-approval at tier 3

- **Persona:** switch to Nadia Ferreira.
- **Page and clicks:** Contractor Portal, the Decisions tab, "Propose a decision": Kind of decision "Risk acceptance",
  Proposed outcome "Accept", Decision "Accept that contractor accounts stay active for 30 days after a contract ends.",
  a context and a reason. Propose, then "Send for approval". Then Approvals, Waiting for me.
- **Say:** "In S-04.4 Nadia answered her own risk acceptance: Balanced allows it on a tier 2 application. Contractor
  Portal is tier 3, and Balanced's separation rule says a different person answers there."
- **Proof on screen:** "Submitted. 1 approval is asked." On Approvals, Waiting for me counts only what she may answer
  now: the card for "Accept that contractor accounts stay active for 30 days after a contract ends." sits apart, under
  "Asked of you, but another person answers", with "The separation rule No self-approval at tier 3 keeps you from
  answering.", and the header adds "1 more asks you, but a separation rule keeps you from answering." The decision waits
  for another business owner of Contractor Portal; the demo has none, so it stays proposed.
- **Reset:** a fresh store.

### S-22.14 Move a domain's rows

- **Persona:** switch to Hana Lindqvist.
- **Page and clicks:** the domain record Customers / Client cases and feedback
  (`/app/records/data-domain/client-cases`), "Move its rows". Move to "Customers / Client accounts and contacts", both
  boxes on, Reason "The service team now logs each case on the client's account, so Client cases and feedback is to be
  retired." "Check first: what would change", then Move, and Move on the confirm.
- **Say:** "A domain split or merge moves its rows. Both domains answer to the same data owner, so each row keeps its
  identity and its decisions. A row whose new domain already holds the same mapping is refused before anything moves."
- **Proof on screen:** before: Where its data sits 2, Applications using it 2. The check: "It would move 2 mappings and
  2 uses from ... to ... Both domains have the same data owner, so each row would keep its identity and its decisions."
  The answer: "2 mappings and 2 data uses moved from Customers / Client cases and feedback to Customers / Client
  accounts and contacts." After: Where its data sits 0, Applications using it 0.
- **Reset:** a fresh store.

### S-22.15 Run the gap check

- **Persona:** switch to Owen Achterberg.
- **Page and clicks:** Gap check, "Run the gap check". Check first, Run it. Then "Continue the run", Continue.
- **Say:** "On a demo install the gap check reads the demo's fictional inventory, never Microsoft's. A large estate
  comes in pages; vanished items are raised only after the last one."
- **Proof on screen:** the sheet says it reads the demo's fictional inventory. The first page: "25 items seen, 0 new
  claims, 0 vanished." and "The run has more pages." The last page: "9 items seen, 1 new claim, 2 vanished. 2
  registered items have vanished from the estate." Not registered lists Depot fuel log; Vanished lists Send planned
  work notices and Route purchase approvals.
- **Reset:** a fresh store.

### S-22.16 Resolve the vanished items

- **Persona:** Owen Achterberg.
- **Page and clicks:** Gap check, Vanished. "Resolve" on Send planned work notices: "It moved: link where it lives
  now", Application that holds it "Customer Notice Sender" (the component is chosen by its name), ID in its tool
  `flow-a41c07`, Link `https://flows.example.org/customer-services-prod/flow-a41c07`, a note. Check first, "Link its
  new place". Then "Still there" on Route purchase approvals, Reason "It runs in the Finance Prod environment, which the
  admin role of this check does not read." "Dismiss the finding".
- **Say:** "A vanished item is gone, has moved, or the check could not see it. Each answer is recorded with its reason."
- **Proof on screen:** "The component is linked to Customer Notice Sender. The component is recorded as moved." Then
  "The claim is dismissed. Reopen brings it back." Vanished no longer lists either.
- **Reset:** a fresh store.

### S-22.17 Reopen a claim and claim an item for a new application

- **Persona:** Owen Achterberg.
- **Page and clicks:** Gap check, Dismissed, "Reopen" on "Test reminder flow (personal)" (dismissed in S-03.7), Reason
  "Its maker kept the flow: it now sends the real reminders, so it needs an owner." Reopen. Then Not registered,
  "Claim" on Canteen orders (from S-22.8): "A new application: start an intake request", Name "Canteen orders", What it
  is for "Crews order lunch from the depot canteen before their shift.", Kind "Power Platform app". Check first, Claim
  it, and confirm.
- **Say:** "A dismissal can be undone. An item that belongs to nothing yet starts its own application, through intake."
- **Proof on screen:** "The claim is open again." Then "The item is claimed for a new draft application, and its intake
  request is started as a draft. Answer the tier questions and submit it for triage."
- **Reset:** a fresh store.

### S-22.18 Check the owners

- **Persona:** Owen Achterberg.
- **Page and clicks:** Gap check, "Check the owners", then "Check the owners" in the sheet.
- **Say:** "The owner check reads each owner's account. On a demo install it reads the demo's fictional directory: the
  account states and leave dates the estate records."
- **Proof on screen:** "25 owners checked in the demo's fictional directory, 2 findings." The answer adds "The check has
  more pages. Run it again to continue.": the estate has more owners than one page holds. Leave it there on stage;
  running it again checks the next page.
- **Reset:** a fresh store.

### S-22.19 Withdraw the draft application

- **Persona:** switch to Priya Castell.
- **Page and clicks:** Depot key register's Application 360 (the draft of S-22.1), "Withdraw" in the draft notice,
  Reason "The depot keys move into Asset Register, so the app is not needed." Check first, Withdraw, and confirm.
- **Say:** "A draft application that is no longer needed is withdrawn, and its ownership decision goes with it."
- **Proof on screen:** "Withdrawn. 2 waiting approvals are canceled."
- **Reset:** a fresh store.

### S-22.20 Change a leave date

- **Persona:** switch to Owen Achterberg.
- **Page and clicks:** Crew Roster's Application 360: the owner row of Ines Varga offers "Change the leave date", not a
  second "Record a leave". Then Owners, Leavers, "Change the leave date" for Ines Varga (a leaver since S-22.4): the
  leave date 45 days from today. Check first, Record the leave, and confirm.
- **Say:** "A leave date moves as people report it. The checks it opened stay open."
- **Proof on screen:** the sheet says the leave recorded now is replaced. The answer: "7 owner roles are affected, and 1
  owner role on data (Field operations); a successor needs an accepted ownership decision." and "12 owner check reviews
  cover this leave".
- **Reset:** a fresh store.

### S-22.21 Propose a successor for a leaver

- **Persona:** Owen Achterberg.
- **Page and clicks:** Owners, Leavers, Ines Varga, "Propose a successor" on her Crew Roster business owner role.
  Successor "Ravi Holm", the deputy; a reason. Check first, Propose, then "Send for approval".
- **Say:** "A successor needs an accepted ownership decision. The person named says yes on the decision page."
- **Proof on screen:** "Submitted. 2 approvals are asked." ASKED NOW names Ravi Holm by name about the business owner
  role.
- **Reset:** a fresh store.

### S-22.22 End an owner assignment

- **Persona:** Owen Achterberg.
- **Page and clicks:** Asset Register's Application 360, the owner row of Ines Varga as deputy data owner, "End", a
  reason. Check first, Propose, then "Send for approval".
- **Say:** "Ending an owner role is an ownership decision too: nobody loses or gains a role without one."
- **Proof on screen:** "Submitted. 1 approval is asked." The Decisions panel lists "End Ines Varga as deputy data owner
  of Asset Register."
- **Reset:** a fresh store.

### S-22.23 Add a data link on Application 360

- **Persona:** switch to Elif Dunmore.
- **Page and clicks:** Visit Report Agent's Application 360, Data, "Add a data link": the other application "Field Data
  Lake", Criticality "Low". Check first, Record it.
- **Say:** "The Data tab offers the data kinds of dependency only: where the application reads or sends its data."
- **Proof on screen:** "The dependency is recorded, as reported: Visit Report Agent reads data from Field Data Lake."
- **Reset:** a fresh store.

### S-22.24 Change and end a dependency

- **Persona:** Elif Dunmore.
- **Page and clicks:** Visit Report Agent's dependency map, the List view, "Change" on Field Knowledge MCP Server (the
  link of S-22.2): Criticality "High". Check first, Save. Then Application 360, Data, "End" on the Field Data Lake link
  of S-22.23. Check first, End it, and confirm.
- **Say:** "A link changes or ends as people report it; the map and the change page read it at once."
- **Proof on screen:** "The dependency is changed, as reported: Visit Report Agent calls API of Field Knowledge MCP
  Server." Then "The dependency is ended: Visit Report Agent reads data from Field Data Lake.", and the link leaves the
  list.
- **Reset:** a fresh store.

### S-22.25 Record a correction

- **Persona:** switch to Hugo Brandvold.
- **Page and clicks:** the deployment record of Inspection Scheduler 3.4.3 (S-22.5). It offers "Record a correction"
  and "Not this change?" (the same sheet). "Record a correction": What was wrong "The deployment finished five minutes
  later than recorded: the import took five more minutes.", Finished five minutes later than the record says (the local
  time of 07:45 UTC). Check first, Record the correction, Record it.
- **Say:** "A deployment record is final. A correction is a new record that names it, and the engine works out the flags
  again."
- **Proof on screen:** "The correction is recorded as a new deployment record; the old record stays. The engine flagged
  it: came another way."
- **Reset:** a fresh store.

### S-22.26 Propose an exception for the flagged deployment

- **Persona:** switch to Elif Dunmore.
- **Page and clicks:** the corrected deployment record (flag "Came another way"), "Propose an exception": What the
  exception accepts "Accept the hand deployment of Inspection Scheduler 3.4.3 to Field Ops Prod.", a context and a
  reason. Save the proposal, then "Send for approval".
- **Say:** "Under Balanced the architect proposes an exception about a deployment, and the architects answer it."
- **Proof on screen:** "Submitted. 1 approval is asked." ASKED NOW "Architect (the first answer counts". Decisions,
  Exceptions lists it.
- **Reset:** a fresh store.

### S-22.27 Add evidence on the deployment

- **Persona:** switch to Hugo Brandvold.
- **Page and clicks:** the same deployment record, "Add evidence": Kind "Evidence link", Link
  `https://dev.example.org/field-ops/_wiki/wikis/ops/deploy-log-3-4-3`, Label "Deployment log, Inspection Scheduler
  3.4.3". Add evidence.
- **Say:** "Evidence is a link to where it lives. The product keeps the link, never a copy."
- **Proof on screen:** "The evidence link is saved. It stores an ID, a URL and a label, never a copy."
- **Reset:** a fresh store.

### S-22.28 Change the result of an authorized change

- **Persona:** Hugo Brandvold.
- **Page and clicks:** the change "Retire the old export flow" (authorized in S-04.3), "Record the result": Result
  "Partly succeeded", a note. Check first, Record the result. Then "Change the result": "Succeeded", a note. Check
  first, Record the result.
- **Say:** "How a change ended is recorded as reported, and can be put right. A change that did not fully succeed opens
  its post-change review."
- **Proof on screen:** "The change result is recorded as reported." each time; the button reads "Change the result"
  after the first.
- **Reset:** a fresh store.

### S-22.29 Edit a change draft and link its work item

- **Persona:** Hugo Brandvold.
- **Page and clicks:** Changes, New change for Inspection Scheduler, "Plan the change": Title "Inspection Scheduler 3.5:
  reminder wording", Normal, a window next week. Save draft. "Edit the draft" on the same page: the window a day later,
  Save draft. Then on its change record, "Edit the draft" again, Save draft. "Link a work item": Kind "Azure DevOps work
  item", Link `https://dev.example.org/field-ops/_workitems/edit/4230`, a label, ID 4230, Link it. Then "Add evidence".
- **Say:** "A draft changes as often as it needs to before it is submitted. Work items and evidence are links."
- **Proof on screen:** "The change request is saved as a draft (built-in mode)." three times. Then "The delivery link
  is saved. It stores an ID, a URL and a label, never a copy." and "The evidence link is saved. It stores an ID, a URL
  and a label, never a copy."
- **Reset:** a fresh store.

### S-22.30 Withdraw a change

- **Persona:** Hugo Brandvold.
- **Page and clicks:** the change "Crew Roster 5.4: overtime column" (submitted in S-05.3), "Withdraw", a reason,
  Withdraw and confirm. Then the draft of S-22.29, "Withdraw", a reason, Withdraw and confirm.
- **Say:** "The person who created a change withdraws it, and its waiting approvals are canceled."
- **Proof on screen:** "Withdrawn. 1 waiting approval is canceled." Then "Withdrawn. No approval was waiting."
- **Reset:** a fresh store.

### S-22.31 Change a target, retire one and return it

- **Persona:** switch to Owen Achterberg.
- **Page and clicks:** Places, Field Ops Training (S-22.9), "Change the target": Change path "Pipeline or AI tool".
  Check first, Save the change. Then People Test, "Retire the target", Why it is retired "People Hub testing moves to
  the shared HR test environment." Check first, Retire it, Retire it. Then "Return to service", Check first, "Return it
  to service".
- **Say:** "A target's change path sets the routes it expects. A retired target keeps its records and refuses new
  deployments, until it returns to service."
- **Proof on screen:** "The deployment target Field Ops Training is changed." Then "The deployment target People Test
  is retired." and "The deployment target People Test is back in service."
- **Reset:** a fresh store.

### S-22.32 Change a pipeline map

- **Persona:** Owen Achterberg.
- **Page and clicks:** Places, Pipelines, "Change the map" on "Inspection Scheduler to training" (S-22.9). Choose
  another application, "Route Planner", Step 1: target "Field Ops Training". Check first, Save the map.
- **Say:** "The map says where a pipeline deploys each application; the steps of the other applications stay."
- **Proof on screen:** "The pipeline is mapped to 1 application and target pair. The product never runs the pipeline."
  The pipeline now lists Route Planner beside Inspection Scheduler.
- **Reset:** a fresh store.

### S-22.33 Confirm, change and add inside the new location

- **Persona:** switch to Hana Lindqvist.
- **Page and clicks:** the location Depot safety binders (S-22.10): "Confirm it is still there", Confirm seen. "Change":
  Link `https://fieldops.example.org/sites/depot-safety/binders`, Check first, Save the change. Then "Add one" under
  Locations inside it: Platform "SharePoint", Name "Depot safety checklists", a link. Check first, Add the location.
- **Say:** "A location is confirmed as people see it, changed when it moves, and holds the stores inside it."
- **Proof on screen:** "1 location is changed." twice, then "1 location is registered.", and Depot safety checklists is
  listed inside it.
- **Reset:** a fresh store.

### S-22.34 Propose a technical owner for a location

- **Persona:** Hana Lindqvist.
- **Page and clicks:** Data, Locations, "Propose an owner" on PAYR01 ("No technical owner"): New holder "Felix Arendt",
  a reason. Propose, then "Send for approval".
- **Say:** "A location's technical owner keeps it running; naming one is an owner change, answered on its decision
  page."
- **Proof on screen:** "Submitted. 2 approvals are asked." ASKED NOW names Felix Arendt by name about the technical
  owner role.
- **Reset:** a fresh store.

### S-22.35 Change and end the mapping, then retire the location

- **Persona:** Hana Lindqvist.
- **Page and clicks:** Depot safety binders, "The mapping" of Field operations / Safety findings: "Change", origin
  "Replica", Save the change. Then "End the mapping", and confirm. Then Depot safety checklists (S-22.33), "Retire", and
  confirm.
- **Say:** "A mapping changes or ends as the data moves. A retired location keeps its history; nothing new points at
  it."
- **Proof on screen:** "1 mapping is saved.", and the mapping's Origin reads Replica. End closes the sheet, and the
  record says "The product lists open mappings only, and this one has ended or was withdrawn."; the ended mapping keeps
  the Replica origin. Then "1 location is changed.", and the location reads Retired.
- **Reset:** a fresh store.

### S-22.36 Withdraw the risk acceptance

- **Persona:** switch to Nadia Ferreira.
- **Page and clicks:** the risk acceptance of S-22.13 on Contractor Portal, "Withdraw", Reason "Contract ends are now
  handled by the vendor's offboarding." Check first, Withdraw, and confirm.
- **Say:** "The proposer takes a decision back while it waits. Its requests are canceled."
- **Proof on screen:** "Withdrawn. 1 waiting approval is canceled."
- **Reset:** a fresh store.

### S-22.37 Edit, retire and bring back the new portfolio

- **Persona:** switch to Mateo Brandt, then Owen Achterberg.
- **Page and clicks:** as Mateo, Depot services (S-22.12), "Edit": Description "The depot apps: keys, stores, the
  canteen and the safety binders." Check first, Save. As Owen, "Retire", and confirm; then "Bring back", and confirm.
- **Say:** "The PMO keeps a portfolio's words. The platform admin retires or brings back a portfolio, because rules and
  rights are scoped to it."
- **Proof on screen:** "The portfolio Depot services is saved." after Edit, "The portfolio Depot services is
  retired." after Retire, and "The portfolio Depot services is back in service." after Bring back.
- **Reset:** a fresh store.

### S-22.38 Answer the tier questions on the new request

- **Persona:** switch to Owen Achterberg.
- **Page and clicks:** the intake request "Canteen orders" (started by the claim in S-22.17), Tier assessment, "Answer
  the tier questions": an answer to each question. "Check with the engine", then Save answers.
- **Say:** "The claim started the request; its tier questions are answered on the request itself. The answers suggest a
  tier, and only an accepted tier decision sets it."
- **Proof on screen:** "The answers suggest Tier" and a number. After it, Suggested tier shows the same tier.
- **Reset:** a fresh store.
