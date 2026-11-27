# S-17 Architect: the dependency map and what breaks

| | |
|---|---|
| Persona | Elif Dunmore, architect |
| Use cases | A-DEPENDENCIES, P03, P09, UC-13 |
| Screens | Dependency map, Application 360 (Components, Lifecycle and gates), the data location record |
| Time | 45 seconds in the 5-minute run, 4 minutes in full |
| End-to-end spec | `src/tests/e2e/specs/S-17-dependency-map.spec.ts` |
| Story section | W4 Changes, releases and what they touch |
| Ready check | `demo check --base <url> --scenario S-17` |

**Before it starts.** Act as Elif Dunmore ("Elif Dunmore, architect"). Inspection Scheduler has the richest map in
the estate; Finance Ledger is the best example of a retirement's impact; PAYLIB shows the impact of a data location.

**Reset.** Nothing to reset: every step only reads.

## Steps

### S-17.1 Inspection Scheduler's map

- **Persona:** Elif Dunmore, architect.
- **Page and clicks:** Inspection Scheduler's dependency map
  (`/app/records/business-application/00000000-0000-4000-8000-000000000511/map`), or "Dependency map" on its
  Application 360.
- **Say:** "What breaks if it changes or retires, and what it needs to run, up to three hops each way. Components and
  data places are on the same map."
- **Proof on screen:** "What breaks if Inspection Scheduler changes or retires, and what it needs to run. Up to 3
  hops." Columns from "IT NEEDS, 3 HOPS" to "DEPENDS ON IT, 3 HOPS". "What breaks, in numbers: 9 applications depend on
  it within 3 hops, and more beyond"; "What it needs, in numbers: 7 applications". Data places Customer Prod and Field
  Ops Prod, and its components (canvas app, cloud flows, solution, repository).
- **Reset:** nothing.

### S-17.2 Only what breaks, as a list

- **Persona:** Elif Dunmore.
- **Page and clicks:** Show "What breaks", then View "List".
- **Say:** "The same walk as a list, each link with its type and criticality, ready for a change or a retirement."
- **Proof on screen:** "What breaks if Inspection Scheduler changes or retires" with the count 9. 1 HOP: Route
  Planner and Visit Report Agent ("Reads data from Inspection Scheduler", High), Crew Roster ("Calls API of", Medium),
  Safety Findings Tracker ("No business owner") and Customer Notice Sender. 2 HOPS: Crew Shift Mobile, Contractor
  Portal and Shift Swap Requests. 3 HOPS: Customer Self-Service Hub. Each link offers Change and End.
- **Reset:** nothing.

### S-17.3 What depends on one component

- **Persona:** Elif Dunmore.
- **Page and clicks:** Inspection Scheduler's Application 360, Components, "What depends on it?" on "Inspection
  Scheduler solution".
- **Say:** "Start from a technical part, not the application: the walk says how each application joined."
- **Proof on screen:** the dialog "What depends on Inspection Scheduler solution?": DIRECTLY "Inspection Scheduler,
  Built from Inspection Scheduler solution"; 2 HOPS AWAY Crew Roster, Customer Notice Sender, Route Planner, Safety
  Findings Tracker and Visit Report Agent, each with how it joined, such as "Reads data from Inspection Scheduler"; 3
  HOPS AWAY Contractor Portal, Crew Shift Mobile and Shift Swap Requests.
- **Reset:** nothing.

### S-17.4 The impact of retiring Finance Ledger

- **Persona:** Elif Dunmore.
- **Page and clicks:** Finance Ledger's Application 360, "Lifecycle and gates"
  (`/app/records/business-application/00000000-0000-4000-8000-000000001317?tab=lifecycle`). Under Retirement, "See
  what breaks".
- **Say:** "Before anyone proposes a retirement, the impact is one click away."
- **Proof on screen:** "No retirement is decided. The application stays in use. 5 applications depend on it." The map:
  "14 applications depend on it within 3 hops, and more beyond", Tier 2 10, High criticality 6, among them Invoice
  Matching ("Reads data from"), Supplier Onboarding and Purchase Approvals ("Writes data to").
- **Reset:** nothing.

### S-17.5 What depends on a data location

- **Persona:** Elif Dunmore.
- **Page and clicks:** the PAYLIB data location (`/app/records/data-location/00000000-0000-4000-8000-000000006017`),
  then "What depends on it?".
- **Say:** "A database or a store is a starting point too: the applications with a data use there, and what depends on
  them."
- **Proof on screen:** "PAYLIB, IBM i. 2 open data uses name it. Holds People / Payroll / Bank details (SoR), People /
  Payroll / Paychecks (SoR)." DIRECTLY Finance Ledger and Mileage Claims, "Has a data use at PAYLIB"; 2 HOPS AWAY
  Expense Assistant, Invoice Matching, Parts Catalog API, Purchase Approvals and Supplier Onboarding; 3 HOPS AWAY
  Inspection Scheduler, Quote Builder, Supplier Price Agent and Warehouse Stock Count. (Mileage Claims' use there is
  the draft Claude Code made on a local run; without it, "1 open data use".)
- **Reset:** nothing.
