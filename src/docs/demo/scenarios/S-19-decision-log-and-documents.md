# S-19 Auditor: the decision log and the documents

| | |
|---|---|
| Persona | Bianca Ferro, auditor |
| Use cases | UC-04, UC-09, P07, UC-14 |
| Screens | Decisions (Decision log, Exceptions), Documents |
| Time | 3 minutes in full |
| End-to-end spec | `src/tests/e2e/specs/S-19-decision-log-and-documents.spec.ts` |
| Story section | W10 Answer the auditor |
| Ready check | `demo check --base <url> --scenario S-19` |

**Before it starts.** Act as Bianca Ferro ("Bianca Ferro, auditor"). The counts grow with every decision played
before; the script names what to look for.

**Reset.** Nothing to reset: every step only reads.

## Steps

### S-19.1 Every decision in one line

- **Persona:** Bianca Ferro, auditor.
- **Page and clicks:** More, Record, Decisions (`/app/decisions`).
- **Say:** "Every significant decision in one line: what, who, when and why. Open one for its approvals and evidence."
- **Proof on screen:** the figures Waiting for answers, Accepted, Rejected or withdrawn, and Drafted by AI. "Decisions"
  lists each with its type, who proposed it, what it is about, the outcome, the status and "1 of 1 approval in".
- **Reset:** nothing.

### S-19.2 What an AI client drafted

- **Persona:** Bianca Ferro.
- **Page and clicks:** tick "Only those drafted by an AI client". Then the Type filter.
- **Say:** "A decision an AI client drafted stays marked, even after a person took it forward."
- **Proof on screen:** on a local run, the Route Planner tier decision ("Raise Route Planner to tier 2 ...") and the
  Shift Swap Requests tier decision ("Confirm tier 1 for Shift Swap Requests ..."). The Type filter narrows the log to
  one decision type.
- **Reset:** nothing.

### S-19.3 Exceptions and their review dates

- **Persona:** Bianca Ferro.
- **Page and clicks:** the Exceptions view.
- **Say:** "Exceptions are decisions too, each with the standard it sets aside and its next review."
- **Proof on screen:** the figures Standing 2, Review overdue 1, Review in 30 days 1 and Waiting for answers 2 on a fresh
  store (Standing 3, Review in 30 days 2 and Waiting for answers 1 after S-16.4: the Desk Booking exception is reviewed
  again within the month; its review date falls about a month out, by the hour of the director's run, and it counts in
  the window only when it is inside it). The Travel Requests exception reads "Review overdue by" some days.
- **Reset:** nothing.

### S-19.4 Documents by state

- **Persona:** Bianca Ferro.
- **Page and clicks:** More, Work, Documents (`/app/documents`), then the Stale view.
- **Say:** "Which required documents are missing, stale or waiting to be confirmed, and which gate needs each.
  Missing is worked out from the rules, never stored."
- **Proof on screen:** on a fresh store the figures Missing 2, Stale 3, Drafts to confirm 3 and Current 56. After the
  15-minute run, Stale is 2 (S-04.9 confirmed the runbook) and Missing 3 (S-06.4 met the Design gate of Claims Triage
  Agent, so it reached the build stage, where its wiki page is required). "Stale documents" lists the Wiki pages of
  Legacy Timesheet App and Safety Findings Tracker. "Which gate needs each": "Runbook. Needed current for the Go-live
  gate, at Tier 2 and Tier 3."
- **Reset:** nothing.
