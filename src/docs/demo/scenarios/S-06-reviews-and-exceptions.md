# S-06 Architect: reviews and exceptions

| | |
|---|---|
| Personas | Elif Dunmore, architect; Marcus Oyelaran, architect and change board (S-06.3) |
| Use cases | P04, UC-03, UC-04, A-LIFECYCLE, A-EXCEPTIONS |
| Screens | Reviews (Due, Done), the review record, the Decision page, Decisions (Exceptions) |
| Time | 45 seconds in the 15-minute run, 3 minutes in full |
| End-to-end spec | `src/tests/e2e/specs/S-06-reviews-and-exceptions.spec.ts` |
| Story section | W3 People answer every approval |
| Ready check | `demo check --base <url> --scenario S-06` |

**Before it starts.** Act as Elif Dunmore ("Elif Dunmore, architect"). S-04.6 has run, so every data use of Claims
Triage Agent is decided.

**Reset.** A fresh store: the design decision is approved and the review completed here.

## Steps

### S-06.1 The architecture queue

- **Persona:** Elif Dunmore, architect.
- **Page and clicks:** Reviews, Due, with the Review type "Architecture review" (`/app/reviews?state=due&type=architecture`).
- **Say:** "The architect's queue: reviews a person asked for, each with its due date."
- **Proof on screen:** "Due reviews" with the count 1: Architecture review, "Asked for by a person", Claims Triage
  Agent, Customer services, Technical owner Marcus Oyelaran, "Due", "Yours".
- **Reset:** nothing.

### S-06.2 What completing the review needs

- **Persona:** Elif Dunmore.
- **Page and clicks:** open the review.
- **Say:** "A review completes on an accepted outcome decision, and its outcome feeds the Design gate."
- **Proof on screen:** "What completing it needs: Propose the outcome, have it accepted, then complete the review",
  with "Propose the outcome: a design decision, Done", "People accept the decision, To do", "Feeds the design gate ...
  Not met yet". Evidence: "ADR 2: claim routing".
- **Reset:** nothing.

### S-06.3 Approve the design decision

- **Persona:** switch to Marcus Oyelaran, architect and change board.
- **Page and clicks:** Reviews, Due, the Claims Triage Agent review, then open "Route claims with a Copilot Studio agent
  and keep claim photos in the claims system." Approve with a typed reason, "Check my answer", Approve, Approve.
- **Say:** "The decision records the options weighed and why each was not taken. Elif proposed it, and Marcus, a second
  architect, answers: the person who asked is not the one who answers."
- **Proof on screen:** OPTIONS WEIGHED: "Copy the photos into the agent's own store. Why not: A second copy of personal
  photos to protect and delete." and "A custom model in a code app. Why not: ...". The page does not say "You proposed
  this decision". After: "The engine accepted the decision."
- **Reset:** a fresh store.

### S-06.4 Complete the review

- **Persona:** Elif Dunmore.
- **Page and clicks:** back on the review, "Complete the review". Leave Done on empty (today, her own calendar day).
  "Complete the review".
- **Say:** "Done is the reviewer's own calendar day, and the gate the review feeds is checked at once."
- **Proof on screen:** "The architecture review of Claims Triage Agent is done. It feeds 1 gate, 1 met now."
- **Reset:** a fresh store.

### S-06.5 A review done before the demo

- **Persona:** Elif Dunmore.
- **Page and clicks:** Reviews, Done.
- **Say:** "Done reviews keep their outcome, and the next one is due by its cadence."
- **Proof on screen:** Quote Builder's Architecture review, "Done" today, beside Claims Triage Agent's.
- **Reset:** nothing.

### S-06.6 The exceptions register

- **Persona:** Elif Dunmore.
- **Page and clicks:** More, Record, Decisions, the Exceptions view (`/app/decisions?view=exceptions`).
- **Say:** "Every exception names the standard it sets aside, why, and when it is reviewed again."
- **Proof on screen:** "Exceptions register" with the count 4: the Desk Booking calendar connector update (Proposed),
  the 1.12.1 hotfix whose code only an AI reviewer approved (Proposed), the Contractor Portal sign-in fix (Accepted,
  "Review in" some days) and the Travel Requests daily allowance rates (Accepted, "Review overdue").
- **Reset:** nothing.
