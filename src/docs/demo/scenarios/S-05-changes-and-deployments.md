# S-05 Change manager: changes and flagged deployments

| | |
|---|---|
| Persona | Hugo Brandvold, change manager |
| Use cases | P03, P05, A-EXCEPTIONS, A-DEPENDENCIES |
| Screens | New change, Timeline, the change record, Changes (List), Deployments (Flagged), the deployment record |
| Time | 1 minute in the 15-minute run, 4 minutes in full |
| End-to-end spec | `src/tests/e2e/specs/S-05-changes-and-deployments.spec.ts` |
| Story section | W4 Changes, releases and what they touch |
| Ready check | `demo check --base <url> --scenario S-05` |

**Before it starts.** Act as Hugo Brandvold ("Hugo Brandvold, change manager"). Pick a window about a week ahead for
the new change.

**Reset.** A fresh store: the new Crew Roster change is drafted and submitted here.

## Steps

### S-05.1 A new change on Crew Roster

- **Persona:** Hugo Brandvold, change manager.
- **Page and clicks:** Changes, New change, with Crew Roster chosen
  (`/app/changes/new?app=00000000-0000-4000-8000-000000000513`).
- **Say:** "The application's tier sets who authorizes the change, and the dependency walk proposes what it touches."
- **Proof on screen:** "1. The application": Crew Roster, Tier 2, Operate, Business owner Ines Varga. "Who authorizes
  it, by type": Emergency, change manager; Normal, business owner; Standard, no authorization when an approved
  standard model fits. "Applications that depend on it": Crew Shift Mobile and Shift Swap Requests, "Directly".
- **Reset:** nothing.

### S-05.2 Plan the change

- **Persona:** Hugo Brandvold.
- **Page and clicks:** "Plan the change". Title "Crew Roster 5.4: overtime column", Change type Normal, What changes
  "Adds an overtime column to the shift view, so supervisors see who is near the limit.", a one-hour window next week,
  Planned target "Field Ops Prod, production". Leave both dependants ticked. "Check first", then "Save draft".
- **Say:** "The person sets the type. An AI client never decides that a change is standard."
- **Proof on screen:** the check: "This change needs authorization before it is deployed. 2 other applications would
  be marked as affected. WILL BE ASKED ONCE SUBMITTED: Business owner of the application (everyone must approve)." The
  save: "The change request is saved as a draft (built-in mode)."
- **Reset:** a fresh store.

### S-05.3 Check and submit

- **Persona:** Hugo Brandvold.
- **Page and clicks:** close the sheet. Read "3. Check and submit", then Submit on the page, Submit in the sheet (with
  its note), and Submit again on "Submit this change?": three presses.
- **Say:** "The engine checks the standard models, names who authorizes it and the gate level at the target before
  anyone is asked."
- **Proof on screen:** STANDARD CHANGE CHECK "Does not fit a standard change model"; WHO WILL AUTHORIZE IT "Business
  owner of the application"; GATE LEVEL AT THE TARGET "Advise". The answer: "Submitted. 1 approval is asked."
- **Reset:** a fresh store.

### S-05.4 The window on the Timeline

- **Persona:** Hugo Brandvold.
- **Page and clicks:** More, Record, Timeline (`/app/timeline`). Choose the Crew Roster 5.4 window.
- **Say:** "Every planned window and every deployment, by target. A window opens its change."
- **Proof on screen:** the bar's label "Change window: Crew Roster 5.4: overtime column, ... Submitted. Open the change
  record." The change record shows "Submitted", "Normal", "Not authorized yet" and "Affected applications" 2.
- **Reset:** nothing.

### S-05.5 Changes closed before the demo

- **Persona:** Hugo Brandvold.
- **Page and clicks:** Changes, the List view (`/app/changes?view=list`).
- **Say:** "Past changes keep how they ended, with their evidence: a removal and an emergency fix."
- **Proof on screen:** "Remove the Timesheets canvas app", Legacy Timesheet App, Normal, Closed, Authorized. "Fix the
  shift export after a payroll change", Crew Roster, Emergency, Closed, Authorized.
- **Reset:** nothing.

### S-05.6 Deployments that only an AI approved

- **Persona:** Hugo Brandvold.
- **Page and clicks:** More, Work, Deployments, the Flagged view with the flag "AI-only code approval"
  (`/app/deployments?view=flagged&flag=ai-only-code-approval`). Open Invoice Matching 1.12.1.
- **Say:** "A code approval from an AI never counts as authorization. The engine raises the flag; nobody can set or
  clear it."
- **Proof on screen:** "Flagged deployments" with the count 3: Route Planner 2.0.0-rc.1 ("Drafted by Claude Code for
  Priya Castell", Owner's AI tool), Invoice Matching 1.12.1 ("Exception waiting") and Invoice Matching 1.12.0. The
  record reads "Missing approval: raised" with G12, "AI-only code approval: raised. The only code approval came from an
  AI. It never counts as authorization." with G17, and "Open the exception waiting".
- **Reset:** nothing.

## On a demo install

Route Planner 2.0.0-rc.1 exists only when the director ran Claude Code's part. Invoice Matching's two deployments are
in the estate on every install.
