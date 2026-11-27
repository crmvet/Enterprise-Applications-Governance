# S-18 Reader: search, filters and what a reader may do

| | |
|---|---|
| Persona | Theo Marsh, reader |
| Use cases | UC-05, UC-07, UC-02 |
| Screens | Search, the person page, the Decision page, Applications, Application 360, My work |
| Time | 3 minutes in full |
| End-to-end spec | `src/tests/e2e/specs/S-18-search-and-lists.spec.ts` |
| Story section | W11 Find anything |
| Ready check | `demo check --base <url> --scenario S-18` |

**Before it starts.** Act as Theo Marsh ("Theo Marsh, reader"). He reads the register, decisions and the change
schedule, and never writes. Pick one decision number from the decision log for S-18.3; on a fresh local store the
Invoice Matching hotfix exception is DEC-0221.

**Reset.** Nothing to reset: every step only reads.

## Steps

### S-18.1 Search by name

- **Persona:** Theo Marsh, reader.
- **Page and clicks:** the top bar's box "Search applications, people and record numbers". Type `invoice` and press
  Enter.
- **Say:** "One search box for applications by name or number, people by name, and any record by its number."
- **Proof on screen:** `Results for "invoice"`: Applications 1, Invoice Matching, "AI-built app. Tier 2. Operate", with
  "Open in the register"; Intake requests 0; People 0.
- **Reset:** nothing.

### S-18.2 Search for a person

- **Persona:** Theo Marsh.
- **Page and clicks:** search `Wren`, then open Wren Abernathy.
- **Say:** "A person's page shows what they own now, their deputy roles and the gaps on their applications."
- **Proof on screen:** People 1, Wren Abernathy. Her page: Owner roles now 7 "On 7 applications", Deputy roles 4,
  "Owns now" by application (Claims Triage Agent Business owner and Data owner, deputy Nadia Ferreira), and "Member of".
- **Reset:** nothing.

### S-18.3 Search by record number

- **Persona:** Theo Marsh.
- **Page and clicks:** search the decision number, such as `DEC-0221`, and press Enter.
- **Say:** "A record number goes straight to its record."
- **Proof on screen:** the Decision page opens: "Grant an exception for the 1.12.1 hotfix, whose code was approved only
  by an AI reviewer.", Exception, Proposed, and "This decision does not ask you. The approvals below show who is
  asked."
- **Reset:** nothing.

### S-18.4 Filter the register

- **Persona:** Theo Marsh.
- **Page and clicks:** Applications. Open the "Uses AI" filter and choose yes (`/app/applications?ai=yes`). Then try
  Stage, Tier, Kind, Portfolio, Owner and Build path, and the search box.
- **Say:** "Search and filter the register by stage, tier, kind, portfolio, owner, AI use and build path. Every filter
  lives in the address, so a filtered list can be shared."
- **Proof on screen:** the chip "Uses AI" with "Clear all", and "14 applications match these filters.", among them
  Claims Triage Agent, Expense Assistant, Field Knowledge MCP Server, Invoice Matching, Supplier Price Agent and Visit
  Report Agent, each with a "Map" link.
- **Reset:** nothing.

### S-18.5 A reader changes nothing

- **Persona:** Theo Marsh.
- **Page and clicks:** Inspection Scheduler's Application 360
  (`/app/records/business-application/00000000-0000-4000-8000-000000000511`). Then My work.
- **Say:** "Each persona sees only what their roles allow. A reader gets no button that writes."
- **Proof on screen:** the 360 offers no Hand over, Propose or Confirm; only "What depends on it?" on each component.
  My work: Approvals 0, and "approval records are hidden by your access".
- **Reset:** nothing.

### S-18.6 Search inside a list

- **Persona:** Theo Marsh.
- **Page and clicks:** Teams, type `change` in "Search the teams by team or role name". Then Owners and its search,
  Approvals (Open, type `exception`) and Retirement (type `Legacy`).
- **Say:** "Every list has its own search, and its counts follow it. The words go into the address, so a filtered
  list can be linked."
- **Proof on screen:** the tabs read All 2, Committees 2, Support teams 0, Role groups 0; "All teams" lists the Change
  board and the Change managers. The search goes into the address as `q=change`. On Approvals `q=exception`, and on
  Retirement `q=Legacy` with Legacy Timesheet App listed. Retirement also filters by portfolio.
- **Reset:** nothing.
