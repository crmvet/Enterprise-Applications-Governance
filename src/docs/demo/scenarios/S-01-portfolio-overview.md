# S-01 PMO: the portfolio at a glance

| | |
|---|---|
| Persona | Mateo Brandt, PMO |
| Use cases | UC-05, UC-06, UC-02, UC-03 |
| Screens | Overview, Owners (Gaps), Reviews (Overdue) |
| Time | 45 seconds in the 15-minute run, 2 minutes in full |
| End-to-end spec | `src/tests/e2e/specs/S-01-portfolio-overview.spec.ts` |
| Story section | W2 Owners, teams and committees |
| Ready check | `demo check --base <url> --scenario S-01` |

**Before it starts.** A fresh store after the demo director ([preparing.md](../preparing.md)). Act as Mateo Brandt:
on a demo install choose him in the "Acting as" menu; on a local run choose "Mateo Brandt, PMO" on the development
sign-in page or in the account menu's Development persona list.

**Reset.** Nothing to reset: every step only reads.

## Steps

### S-01.1 The whole register

- **Persona:** Mateo Brandt, PMO.
- **Page and clicks:** Overview (`/app/overview`), with Portfolio set to "All portfolios".
- **Say:** "One register for every business application, whatever it is built on. The tiles count what needs a person now."
- **Proof on screen:** the line "All portfolios. 48 applications in the register." The tiles Applications 48, Owner
  gaps 6, Reviews overdue 4, Documents missing or stale 5, Approvals waiting, AI drafts waiting, Exceptions, Old drafts
  and Data gaps.
- **Reset:** nothing.

### S-01.2 One portfolio

- **Persona:** Mateo Brandt.
- **Page and clicks:** Portfolio, choose "Field operations".
- **Say:** "The PMO reads one portfolio at a time. Every figure follows the choice, the approvals and the AI drafts
  waiting too."
- **Proof on screen:** the address gains `?portfolio=00000000-0000-4000-8000-000000000501`. The nine tiles read
  Applications 15, Owner gaps 2, Reviews overdue 1, Documents missing or stale 2, Approvals waiting 14 ("Field
  operations, asked and not answered yet"), AI drafts waiting 1, Exceptions 0, Old drafts 0 ("About its applications")
  and Data gaps 5. No tile says "all portfolios".
- **Reset:** nothing.

### S-01.3 The owner gaps

- **Persona:** Mateo Brandt.
- **Page and clicks:** the Owner gaps tile. It opens Owners, the Gaps view, filtered to Field operations
  (`/app/owners?view=gaps&portfolio=...0501`).
- **Say:** "Every application needs a business owner and a deputy. Here are the two holes in Field operations."
- **Proof on screen:** the list "Owner gaps" with the count 2: Inspection Scheduler "Business owner: no deputy" and Safety Findings
  Tracker "Business owner: no holder", marked "Owner change waiting".
- **Reset:** nothing.

### S-01.4 The reviews overdue

- **Persona:** Mateo Brandt.
- **Page and clicks:** browser Back to the Overview, then the Reviews overdue tile (`/app/reviews?state=overdue&portfolio=...0501`).
- **Say:** "Reviews have cadences. An owner check that slipped shows up here, with whose it is."
- **Proof on screen:** the list "Overdue reviews" with the count 1: Owner check, "Opened by an owner check or a leave", Safety Findings
  Tracker, Field operations, "Overdue by" a number of days.
- **Reset:** nothing.

### S-01.5 What needs attention

- **Persona:** Mateo Brandt.
- **Page and clicks:** back on the Overview, scroll to "Needs attention" and "Coming up".
- **Say:** "Deployments the engine flagged, and the change windows of the next two weeks, sit on the same page."
- **Proof on screen:** under FLAGGED DEPLOYMENTS, "route-planner 2.0.0-rc.1 to Field Ops Test" with "AI-only code
  approval" (local run) and "invoice-matching 1.12.1 to Finance Prod" with "Missing approval" and "AI-only code
  approval". Under Coming up, "Release 3.5: inspector workload view" and "Retire the old export flow".
- **Reset:** nothing.

## On a demo install

Claude Code's seeded work (the Route Planner deployment and its drafts) exists only when the director ran with the
operator's Claude Code token. Without it the AI drafts waiting tile is lower and Route Planner is not flagged.
