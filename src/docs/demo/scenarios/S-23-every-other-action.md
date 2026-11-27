# S-23 Every other action: drafts, answers, rules and records

| | |
|---|---|
| Personas | Claude Code for Priya and Owen; Priya Castell, maker; Owen Achterberg, platform admin; Hugo Brandvold, change manager; Elif Dunmore, architect; Nadia Ferreira, application owner; Kai Ostrowski, data owner; Hana Lindqvist, data governance lead; Yusuf Brennan, configuration admin; Dmitri Halloran, AI lead; Mateo Brandt, PMO; Lena Okoro, requester; Noor Safadi, security reviewer |
| Use cases | P10, UC-10, UC-01, P04, UC-04, P03, P02, A-LIFECYCLE, P08, P02-DATA, UC-13, P11, A-AGENTS, UC-03, P07, UC-14, A-MEASURES, UC-06, P09, P01, UC-08 |
| Screens | Claude Code, the Decision page, AI activity, My work, the change record, the data use, location and mapping records, Approvals, Application 360 (Tier, Lifecycle and gates, Data), Data (Gaps, Mapping, Locations), the data domain record, Configuration and the configuration draft, Roles and access, Agents, the review record, Documents and the document record, Decisions, Measures, Portfolios, Retirement, the intake wizard, Admin, the evidence pack |
| Time | 35 minutes in full; deep dive only |
| End-to-end spec | `src/tests/e2e/specs/S-23-every-other-action.spec.ts` |
| Story section | W12 Everyday records |
| Ready check | `demo check --base <url> --scenario S-23` |

**Before it starts.** Play it after S-22, on the same store. Each step performs one more side sheet the web app
offers, for real, so that every side sheet is shown live in a step and tested by its end-to-end test (the table
"Every side sheet of the web app" in [the catalog](../scenarios.md) lists each one with its steps). S-23.1 is Claude
Code's work on a local run; on a demo install without Claude Code the steps that accept its drafts skip, as S-02's do.
S-23.28 needs a failed approval card, which only the demo kit makes, on a local run (`demo local fail-card`).

**Reset.** A fresh store (`demo local reset` on a local run).

## Steps

### S-23.1 Claude Code drafts the rest

- **Persona:** Claude Code, for Priya Castell and for Owen Achterberg ([claude-code.md](../claude-code.md), "S-23").
- **Page and clicks:** in Claude Code, for Priya: a tier decision draft for Warehouse Stock Count and one for Customer
  Feedback Bot (she builds both), the change "Field Data Lake 1.4: fleet table refresh", and two data use drafts
  (Mileage Claims reads Personnel records; Customer Self-Service Hub reads Client orders). For Owen: the locations
  "Depot key cabinet" and "Depot canteen menu", each with a mapping. Then AI activity, Waiting, as Priya.
- **Say:** "An AI client drafts; nobody is asked until a person takes it forward."
- **Proof on screen:** each draft answers "The decision is recorded as proposed. Nobody is asked yet" or its own draft
  words. AI activity, Waiting, lists the Warehouse Stock Count draft.
- **Reset:** a fresh store.

### S-23.2 Edit the AI draft and accept it

- **Persona:** Priya Castell.
- **Page and clicks:** the decision page of the Warehouse Stock Count draft ("An AI draft, not submitted yet"), "Edit":
  a new reason, "Save the changes". Then "Accept and submit", and confirm.
- **Say:** "A person reads the draft, changes it, and makes it hers. The engine then asks the people the rules name."
- **Proof on screen:** "Submitted. 1 approval is asked."
- **Reset:** a fresh store.

### S-23.3 Accept and submit from AI activity

- **Persona:** Priya Castell.
- **Page and clicks:** AI activity, Waiting, "Accept and submit" on the Customer Feedback Bot draft, and confirm.
- **Say:** "The same act from the list of what AI clients drafted for her."
- **Proof on screen:** "Submitted. 1 approval is asked."
- **Reset:** a fresh store.

### S-23.4 Accept and submit from My work

- **Persona:** Priya Castell.
- **Page and clicks:** My work, "Accept and submit" on "Confirm tier 1 for Shift Swap Requests: it only swaps shifts
  inside one crew, and a supervisor approves each swap." (Claude Code's draft from the director), and confirm.
- **Say:** "And from her own work list."
- **Proof on screen:** "Submitted. 1 approval is asked." My work no longer lists the draft.
- **Reset:** a fresh store.

### S-23.5 Accept and submit an AI-drafted change

- **Persona:** Priya Castell.
- **Page and clicks:** the change "Field Data Lake 1.4: fleet table refresh" ("Drafted by Claude Code for Priya
  Castell"), "Accept and submit", Submit, and confirm.
- **Say:** "A change an AI client drafted is submitted only by the person it was drafted for."
- **Proof on screen:** "Submitted. 1 approval is asked."
- **Reset:** a fresh store.

### S-23.6 Confirm the AI-drafted use, send it, withdraw the other

- **Persona:** Priya Castell.
- **Page and clicks:** the draft use of Mileage Claims on People / Personnel records, "Check and confirm", "Confirm the
  use". Then the draft use of Customer Self-Service Hub, "Withdraw", a reason, Withdraw and confirm.
- **Say:** "Confirming a use the rules decide sends its decision at once. A draft that is not needed is withdrawn."
- **Proof on screen:** "The data use is confirmed." and "A data-use decision was opened and sent to the people who
  answer it." The withdrawn draft's page then says it is withdrawn.
- **Reset:** a fresh store.

### S-23.7 Confirm the AI-drafted location and mapping, withdraw the others

- **Persona:** switch to Owen Achterberg.
- **Page and clicks:** the location Depot key cabinet, "Check and confirm", Check first, Confirm the location. "The
  mapping" there, "Check and confirm", Check first, Confirm the mapping. Then Depot canteen menu: its mapping,
  "Withdraw", a reason, confirm; the location, "Withdraw", a reason, confirm.
- **Say:** "Locations and mappings an AI client drafts wait for a person to confirm them or take them back."
- **Proof on screen:** "1 location is changed. 1 draft is confirmed: no longer a draft." and "1 mapping is saved. 1
  draft is confirmed: no longer a draft."; neither record offers "Check and confirm" any more, the mapping shows the day
  it was confirmed, and AI activity counts both as taken forward. Withdrawing the mapping draft closes its sheet, and
  its page says it is no longer listed; the location's page reads Withdrawn, with no gap badge beside it.
- **Reset:** a fresh store.

### S-23.8 A named deputy declines

- **Persona:** switch to Hugo Brandvold.
- **Page and clicks:** Approvals, Waiting for me, "Answer" on the hand-over of Inspection Scheduler's technical owner
  role (S-04.10 names him the new deputy). "Reject", Reason "I cannot take the deputy role this quarter.", "Check my
  answer", Reject, Reject.
- **Say:** "A person named in an owner change says no on the decision page, and the change is rejected."
- **Proof on screen:** "Your answer counts toward the approval." and "The decision is now rejected."
- **Reset:** a fresh store.

### S-23.9 A consulted architect gives advice

- **Persona:** switch to Elif Dunmore.
- **Page and clicks:** Approvals, Waiting for me, "Answer" on "Accept the intake request: Crew overtime tracker" (S-02.7
  consults the architect). "Approve" with a reason, "Check my answer", Approve, Approve.
- **Say:** "A consultation is advice: it is recorded and shown, and never counts toward the approval."
- **Proof on screen:** "Your answer is recorded as advice. A consultation never counts toward the approval."
- **Reset:** a fresh store.

### S-23.10 Supersede a decision and add evidence

- **Persona:** switch to Nadia Ferreira.
- **Page and clicks:** the risk acceptance accepted in S-04.4 ("Accept that inspection photos stay on devices for up to
  7 days while offline."), "Supersede": Statement "Accept that inspection photos stay on devices for up to 3 days while
  offline.", a context and a reason. Save the proposal, then "Send for approval". Back on the old decision, "Add
  evidence": Address `https://dev.example.org/field-ops/_wiki/wikis/ops/photo-sync-test`, a label. Add the link.
- **Say:** "An accepted decision is never edited; a new one supersedes it, and evidence links to it."
- **Proof on screen:** "Submitted. 1 approval is asked." Then "The evidence link is saved. It stores an ID, a URL and
  a label, never a copy."
- **Reset:** a fresh store.

### S-23.11 Ask again before a request runs out

- **Persona:** switch to Priya Castell.
- **Page and clicks:** Changes, New change for Inspection Scheduler, "Plan the change": Title "Inspection Scheduler
  3.4.4: reminder flow outage", Emergency, a description. Save draft, then Submit and confirm. My work, Waiting on
  others, "Ask again" on it: Check first, Ask again, and confirm.
- **Say:** "An emergency gives the change manager two days. Inside the last 48 hours the proposer asks again, and the
  request starts over with new time."
- **Proof on screen:** "Submitted. 1 approval is asked." Then "Decision DEC-nnnn asks the change manager again. The new
  request expires in 2 days." Asked now gives the new expiry's local date and time; the answer says it only once.
  Asked right after the submit, the new expiry is only the minutes since the submit later
  than the first one; to show a clear gain on stage, age the request first with `demo local age-request --decision
  <number>` (a local run), or say so.
- **Reset:** a fresh store.

### S-23.12 Tier answers and a tier decision on Application 360

- **Persona:** switch to Nadia Ferreira, then Owen Achterberg.
- **Page and clicks:** as Nadia, Contractor Portal, Tier, "Answer the tier questions" (or "Answer again"): an answer to
  each question, "Check with the engine", Save the answers. As Owen, "Propose a tier decision": a context and a reason,
  Propose, then "Send for approval".
- **Say:** "Answers suggest a tier; only an accepted tier decision sets it. Under Balanced the platform admin proposes
  it."
- **Proof on screen:** "The answers suggest Tier 3. This is a suggestion; only an accepted tier decision sets the
  tier." Then "Submitted. 2 approvals are asked."
- **Reset:** a fresh store.

### S-23.13 Propose the design decision

- **Persona:** switch to Elif Dunmore.
- **Page and clicks:** Weather alerts for outdoor jobs (Design stage since S-09.4), Lifecycle and gates, "Propose the
  design decision": an outcome, the decision, a context, a reason and an option weighed with why not. Propose, then
  "Send for approval".
- **Say:** "The design gate passes with the design decision; the architect proposes it from the gate itself."
- **Proof on screen:** "Submitted. 1 approval is asked."
- **Reset:** a fresh store.

### S-23.14 Hand over the data owner role

- **Persona:** switch to Kai Ostrowski, data owner.
- **Page and clicks:** Field Knowledge MCP Server, Data, "Hand over": New data owner "Mateo Brandt", New deputy "Ravi
  Holm", a reason. Check first, "Propose the hand-over", then "Send for approval".
- **Say:** "A data owner hands the role over through one ownership decision; the people named say yes."
- **Proof on screen:** "Submitted. 3 approvals are asked."
- **Reset:** a fresh store.

### S-23.15 It holds no business data

- **Persona:** switch to Owen Achterberg.
- **Page and clicks:** Site Access Badges, Data, "It holds no business data", "Mark it".
- **Say:** "An application that moves no business data says so once, and its data-uses-declared check is met."
- **Proof on screen:** the sheet answers, and "It holds no business data" no longer shows.
- **Reset:** a fresh store.

### S-23.16 Data decisions from Data gaps

- **Persona:** Owen Achterberg.
- **Page and clicks:** Data, Gaps, the first DC-05 row's "Propose a decision": a statement, a context, outcome "Allow",
  a reason. Propose, then "Send for approval". Then "Propose a retention decision" on the DC-13 row of Legacy
  Timesheet App: a statement, a context, outcome "Keep", a reason. Propose, then "Send for approval".
- **Say:** "Each data gap names the fix and offers it to the person who may make it."
- **Proof on screen:** "Submitted. 2 approvals are asked, and 1 person is consulted." Then "Submitted. 1 approval is
  asked, and 1 person is consulted."
- **Reset:** a fresh store.

### S-23.17 A copy outside production

- **Persona:** switch to Hana Lindqvist, then Owen Achterberg.
- **Page and clicks:** as Hana, Data, Mapping, Copies outside production, "Change the copy" on Customers / Client
  accounts and contacts at Customer Test: "Masked copy", Save the change. As Owen, "Propose a copy decision" on the same
  row: a statement, a context, "Allow", a reason. Propose, then "Send for approval".
- **Say:** "A copy outside production follows the copy rule. Under Balanced the platform admin proposes its decision,
  and the data owner answers."
- **Proof on screen:** "1 mapping is saved. 1 copy outside production needs a decision or breaks the copy rule." Then
  "Submitted. 1 approval is asked."
- **Reset:** a fresh store.

### S-23.18 Change and end a data use; confirm a stale location

- **Persona:** Owen Achterberg, then Hana Lindqvist.
- **Page and clicks:** as Owen, Route Planner's use of Field operations / Inspections (S-07.11), "Change": a new
  purpose, Save the change. "End the use", and confirm. As Hana, Data, Locations, "Confirm seen" on the TIMESQL01 row
  itself (not the Timesheets database listed inside it).
- **Say:** "A use changes and ends as people report it. A location not seen for a while is confirmed when someone sees
  it."
- **Proof on screen:** "The data use is confirmed." and the new purpose on the record. After the end: "The product
  lists open uses only, and this one has ended or was withdrawn." Then "1 location is changed."
- **Reset:** a fresh store.

### S-23.19 Propose a data domain owner

- **Persona:** Hana Lindqvist.
- **Page and clicks:** the domain record Commercial, which has no data owner, "Name one here": New holder "Clara Voss",
  a reason. Propose, then "Send for approval".
- **Say:** "The data governance lead appoints the owners of the top-level domains, by an owner change."
- **Proof on screen:** "Submitted. 2 approvals are asked."
- **Reset:** a fresh store.

### S-23.20 Import data domains, change the draft, submit it

- **Persona:** switch to Yusuf Brennan.
- **Page and clicks:** Configuration, "Import data domains": The CSV text `label,parent` then `Desk bookings,workplace`,
  Why the import is wanted "Desk Booking keeps its desk bookings on the parent Workplace domain; they belong to their
  own subdomain." Save the draft, and confirm. On the draft's page, "Change the draft": a longer reason, "Save the draft
  again". Then Submit, and confirm.
- **Say:** "Domains come from a spreadsheet as a configuration draft. Nothing changes until another configuration admin
  accepts it."
- **Proof on screen:** "The configuration draft is stored: 1 domain added, 0 changed, 0 unchanged." Change the draft
  closes its sheet and opens the new draft, which shows the new reason. Then "Submitted. 1 approval is asked."
- **Reset:** a fresh store.

### S-23.21 Propose a rule change from a page, then withdraw it

- **Persona:** Yusuf Brennan.
- **Page and clicks:** Roles and access, Reader, "Propose a binding change": the prefilled binding, a reason, Save the
  draft. On the draft's page, "Withdraw", a reason, Withdraw and confirm.
- **Say:** "A rule change starts on the page that shows the rule. A draft nobody needs is withdrawn."
- **Proof on screen:** "The configuration draft is stored. Nothing is in force until a person accepts its decision on
  the decision page. 1 row changes." The draft reads "Keep the group binding of Reader as it is." Then "Withdrawn. No
  approval was waiting.", and the draft page ends "The contractor group waits for the new quarter. It changed nothing."
- **Reset:** a fresh store.

### S-23.22 Agents: decide on a flagged agent, request a security review

- **Persona:** switch to Dmitri Halloran.
- **Page and clicks:** Agents, Flagged, "Decide on a flagged agent": the agent, what you propose, a statement, why it
  was flagged, a reason. Propose, then "Send for approval". Agents, "Request a security review" on the first agent
  listed (Claims Triage Agent on a fresh store): what to look at, wanted by in two weeks. Request the review.
- **Say:** "The AI lead decides on an agent Microsoft flags, and asks the security reviewer to look at one."
- **Proof on screen:** "Submitted. 1 approval is asked." Then "The security review of" the agent "is requested. It is a
  request, never a decision".
- **Reset:** a fresh store.

### S-23.23 Reviews: propose the outcome and add evidence

- **Persona:** switch to Elif Dunmore.
- **Page and clicks:** the architecture review Nadia asked for in S-22.3 (Contractor Portal), "Propose the outcome": the
  reason, an outcome, the decision, what the review looked at, an option and why not. Propose, then "Send for approval".
  Then "Add evidence": a link and a label, "Add the link".
- **Say:** "A review ends in a decision. Its evidence is a link."
- **Proof on screen:** "Submitted. 1 approval is asked." Then "The evidence link is saved. It stores an ID, a URL and
  a label, never a copy."
- **Reset:** a fresh store.

### S-23.24 Documents: confirm current, mark stale, update the link

- **Persona:** switch to Nadia Ferreira.
- **Page and clicks:** Documents, Drafts to confirm, "Confirm current" on the first draft she may confirm. Then the
  record of Inspection Scheduler's runbook: "Mark stale" with a note, then "Update the link" to a new address, Save the
  link.
- **Say:** "A person confirms a draft current, says when a document went stale, and points it at its new version."
- **Proof on screen:** "The check is recorded, as reported." Then "The document is marked stale, as reported." and
  "The document link is recorded with status current, as reported. The product never writes the document."
- **Reset:** a fresh store.

### S-23.25 Decision log and Measures proposals

- **Persona:** switch to Mateo Brandt, then Elif Dunmore.
- **Page and clicks:** as Mateo, Decisions, "Propose a decision": Portfolio, Fleet Maintenance Log, Keep, a statement, a
  context, a reason. Propose, then "Send for approval". Measures, "Record a decision" on the first slide-back signal,
  "Changes wait for a meeting": Portfolio, Fleet Maintenance Log, a statement about the change board's slots, an
  outcome, a context, a reason. Propose, then "Send for approval". As Elif,
  Decisions, "Propose an exception": Training Records, the standard it departs from, a statement, a context, an
  outcome, a review date, a reason and an option weighed. Propose, then "Send for approval".
- **Say:** "Decisions start where people see the need: the log, a slide-back signal, an exception to a standard."
- **Proof on screen:** "Submitted. 1 approval is asked." for each of the three.
- **Reset:** a fresh store.

### S-23.26 Portfolio and retirement decisions

- **Persona:** Mateo Brandt, then Hugo Brandvold.
- **Page and clicks:** as Mateo, Portfolios, Field operations, Review pack, "Propose" on the first application whose row
  reads "No portfolio decision": a statement, Keep, a context, a reason. Propose, then "Send for approval". The
  retirement tracker of Depot first-aid kit checks (registered in S-20.5): "Propose a portfolio decision": Retire, a
  statement, a context, a reason. Propose, then "Send for approval". As Hugo, "Draft the removal change": a title,
  Normal, a description, a window. Save draft. Then "Add evidence" on the tracker.
- **Say:** "The review pack and the retirement tracker each offer the next decision. The removal change and its evidence
  follow."
- **Proof on screen:** "Submitted. 1 approval is asked." twice. The tracker lists the removal change. Then "The evidence
  link is saved. It stores an ID, a URL and a label, never a copy."
- **Reset:** a fresh store.

### S-23.27 A new intake draft, then withdraw it

- **Persona:** switch to Lena Okoro.
- **Page and clicks:** Intake, New request: Title "Canteen pre-orders", The need "Crews pre-order lunch from the depot
  canteen before they leave for the day." "Save draft", then Save draft in the sheet. Then "Withdraw", a reason,
  Withdraw and confirm.
- **Say:** "A draft goes nowhere until it is submitted, and the requester can take it back."
- **Proof on screen:** "The intake request is saved as a draft. It goes nowhere until it is submitted." Then "Withdrawn.
  No approval was waiting."
- **Reset:** a fresh store.

### S-23.28 The approval outbox: send again, close

- **Persona:** switch to Owen Achterberg.
- **Page and clicks:** on a local run, the kit first fails the card of one open request (`demo local fail-card
  --decision <number>`) and the card of a request already answered (`--settled`). Then Admin, Approval outbox: "Send
  again" on the first, Check first, Send again, and confirm; "Close the card" on the second, Check first, Close the
  card, and confirm.
- **Say:** "A card the runner could not deliver goes back to it; a card whose request is already settled is closed. The
  audit trail records who asked."
- **Proof on screen:** "The card goes back to the approval runner, which sends it again on its next pull." Then "The
  request needs no card any more (answered), so its outbox row is closed."
- **Reset:** a fresh store.

### S-23.29 Confirm a draft document on its record

- **Persona:** switch to Noor Safadi, security reviewer.
- **Page and clicks:** Documents, the support guide of Identity Directory Sync (a draft; Noor is its document owner),
  "Confirm it is current": Check first, Confirm current.
- **Say:** "A draft document is confirmed by a person who read it, here its owner, on the document's own page."
- **Proof on screen:** "The document link is recorded with status current, as reported." The record reads Current and
  no longer offers "Confirm it is current".
- **Reset:** a fresh store.

### S-23.30 Confirm a draft document on Application 360

- **Persona:** switch to Dmitri Halloran.
- **Page and clicks:** Supplier Price Agent, Summary, Documents: "Confirm it is current" on the architecture decision
  record (a draft; Dmitri is its document owner). Check first, Confirm current.
- **Say:** "The same confirmation sits beside the document on the application's own page."
- **Proof on screen:** "The document link is recorded with status current, as reported." The row reads Current and no
  longer offers "Confirm it is current".
- **Reset:** a fresh store.

### S-23.31 Add evidence on the evidence pack

- **Persona:** switch to Owen Achterberg.
- **Page and clicks:** Invoice Matching's evidence pack (Audit trail, Evidence pack, Invoice Matching): "Add evidence"
  on its first decision. Kind Evidence link, Link
  "https://dev.example.org/finance/_wiki/wikis/ap/invoice-matching-audit", Label "Invoice Matching audit sample,
  November". Check first, Add evidence.
- **Say:** "The evidence pack is where an auditor reads; the people who keep the records add the proof there too, as a
  link, never a copy."
- **Proof on screen:** "The evidence link is saved. It stores an ID, a URL and a label, never a copy." The pack lists
  "Invoice Matching audit sample, November".
- **Reset:** a fresh store.
