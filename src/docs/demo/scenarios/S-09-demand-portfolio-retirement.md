# S-09 PMO, business owner and platform admin: demand, the portfolio and retirement

| | |
|---|---|
| Personas | Mateo Brandt, PMO; Nadia Ferreira, application owner; Owen Achterberg, platform admin |
| Use cases | A-DEMAND, UC-06, P09 |
| Screens | Demand, the Decision page, Application 360 (Lifecycle and gates), Portfolios, Retirement, the retirement tracker, Data (Gaps) |
| Time | 1 minute 15 seconds in the 15-minute run, 6 minutes in full |
| End-to-end spec | `src/tests/e2e/specs/S-09-demand-portfolio-retirement.spec.ts` |
| Story section | W7 From request to retirement |
| Ready check | `demo check --base <url> --scenario S-09` |

**Before it starts.** S-02 and S-04.5 have run: the crew overtime tracker is on the table, and "Weather alerts for
outdoor jobs" is a registered application with Nadia Ferreira as business owner. Act as Mateo Brandt ("Mateo Brandt,
PMO"). Without S-02 the board shows On the table 6 and To score 2.

**Reset.** A fresh store: the score, the priority, the retirement steps and both answers are made here.

## Steps

### S-09.1 The demand board

- **Persona:** Mateo Brandt, PMO.
- **Page and clicks:** More, Work, Demand (`/app/demand`).
- **Say:** "The PMO scores and sizes demand. Priority is a decision that the sponsor, or the business owner, answers."
- **Proof on screen:** the figures On the table 7, To score 3, Priority waiting 1, Scored 4. "Priorities waiting":
  "Supplier rebate tracking", "Proposed priority: Fund". "Still to score" holds "Weather alerts for outdoor jobs",
  Horizon Now.
- **Reset:** nothing.

### S-09.2 Score the request

- **Persona:** Mateo Brandt.
- **Page and clicks:** Score on "Weather alerts for outdoor jobs" in "Still to score". Score 80, Size band Small,
  Horizon Now. Record.
- **Say:** "Score, size and horizon order the list. They are facts the PMO reports, not a decision."
- **Proof on screen:** "The demand fields of Weather alerts for outdoor jobs are recorded, as reported." In the demand
  list it sits second, with 80, Small, Now.
- **Reset:** a fresh store.

### S-09.3 Propose the priority

- **Persona:** Mateo Brandt.
- **Page and clicks:** "Propose priority" on its row. Statement "Fund weather alerts for outdoor jobs this quarter.",
  a context, Proposed outcome Fund, a reason. Propose, then "Send for approval" in "Send it for approval now?".
- **Say:** "No sponsor is named for a new application, so the business owner the request named decides."
- **Proof on screen:** the outcomes Defer, Fund and Stop. "Check first" names the Business owner, the role the submit
  asks: no sponsor is named, so the rule's fallback applies. The answer: "Submitted. 1 approval is asked." ASKED NOW:
  "Business owner of the application (the first answer counts)".
- **Reset:** a fresh store.

### S-09.4 The business owner funds it

- **Persona:** switch to Nadia Ferreira.
- **Page and clicks:** Approvals, Answer on "Fund weather alerts for outdoor jobs this quarter." Approve with a typed
  reason.
- **Say:** "This is the Commit gate. A funded priority moves the application on."
- **Proof on screen:** "You are asked as Business owner of the application." After: "The engine accepted the
  decision."
- **Reset:** a fresh store.

### S-09.5 The gate passed on Application 360

- **Persona:** Nadia Ferreira.
- **Page and clicks:** open "Weather alerts for outdoor jobs" from the register, then "Lifecycle and gates".
- **Say:** "Each gate decision moves the stage. Intake accepted, priority funded: the application is in design."
- **Proof on screen:** Stage "Design and architecture review, since" today. "Accept gate: passed", "Commit gate:
  passed" with "Fund weather alerts for outdoor jobs this quarter. Accepted. Outcome: Fund", "Design gate: ready".
- **Reset:** nothing.

### S-09.6 The portfolio review pack

- **Persona:** switch to Mateo Brandt.
- **Page and clicks:** More, Register, Portfolios, Field operations
  (`/app/portfolios/00000000-0000-4000-8000-000000000501`).
- **Say:** "One review pack per portfolio: owner, tier, stage, last review, open decisions and dependants."
- **Proof on screen:** "Review pack" with the count 15. Crew Shift Mobile, "No accepted decision", "Proposed:
  Replace". Under Portfolio decisions: "Replace Crew Shift Mobile with the phone view of Crew Roster 5.3."
- **Reset:** nothing.

### S-09.7 Applications on their way out

- **Persona:** Mateo Brandt.
- **Page and clicks:** More, Work, Retirement (`/app/retirement`).
- **Say:** "A retirement runs on purpose, from a portfolio decision to retirement complete."
- **Proof on screen:** the figures Retire proposed 1, On the way out 1. Crew Shift Mobile "Retire proposed"; Legacy
  Timesheet App "Replace, Accepted", "On the way out".
- **Reset:** nothing.

### S-09.8 Confirm the component retired

- **Persona:** switch to Owen Achterberg.
- **Page and clicks:** Legacy Timesheet App's tracker (`/app/retirement/00000000-0000-4000-8000-000000001323`). On
  step 6, "Confirm retired" on Timesheets, "Confirm retired" in the sheet, then "Mark retired" on "Mark Timesheets
  retired?".
- **Say:** "People remove it in Microsoft's tools. The product records it, and the gap check's vanished claim closes."
- **Proof on screen:** before: steps 1 to 5 Done, "What is left: 6. gap check confirmation; 7. retirement complete."
  After: "The component is recorded as retired, as reported. The component is marked as gone. The claim's status is
  now claimed." Step 6 reads Done.
- **Reset:** a fresh store.

### S-09.9 Propose retirement complete

- **Persona:** Owen Achterberg.
- **Page and clicks:** step 7, "Propose retirement complete". Keep the statement and the outcome Complete; add a
  context and a reason. "Check first", Propose, then Submit on the notice "Proposed, not submitted yet".
- **Say:** "The business owner accepts retirement complete. Here the owner is not in the demo, so the deputy answers."
- **Proof on screen:** the check: "The decider would be Rosa Tamaki. WILL BE ASKED ONCE SUBMITTED: Business owner of
  the application (the first answer counts)". The Decision page then shows "Waiting on Rosa Tamaki, or deputy Nadia
  Ferreira".
- **Reset:** a fresh store.

### S-09.10 Retention on Data gaps

- **Persona:** Owen Achterberg.
- **Page and clicks:** Data, the Gaps tab for Legacy Timesheet App
  (`/app/data/gaps?app=00000000-0000-4000-8000-000000001323`).
- **Say:** "What happens to the data is the data owner's decision. Its archive decision waits for Hana Lindqvist."
- **Proof on screen:** "DC-13 Retention open" with the count 1: "Legacy Timesheet App is retiring and writes People /
  Timesheets, with no retention decision yet.", with "Propose a retention decision".
- **Reset:** nothing.

### S-09.11 The deputy accepts retirement complete

- **Persona:** switch to Nadia Ferreira, deputy business owner of Legacy Timesheet App.
- **Page and clicks:** Approvals, Answer on "Legacy Timesheet App is retired: nobody uses it, and nothing of it is left
  running." Approve with a typed reason. Then reopen the tracker.
- **Say:** "Her answer counts for the business owner role. The application moves to Retire."
- **Proof on screen:** "The engine accepted the decision." The tracker reads "Retired" and "Stage: Retire". Step 3
  follows the retention rule: it is done only when the data owner of each domain the application writes has decided,
  so while the archive decision of People / Timesheets waits for Hana Lindqvist the tracker reads "What is left: 3.
  data retention decision. The application is retired, and these steps still have nothing on record." Once she has
  decided it reads "Every step is done." Step 3 names who decides from now on: "Each domain's retention is its data
  owner's to decide: People / Timesheets, Hana Lindqvist."
- **Reset:** a fresh store.
