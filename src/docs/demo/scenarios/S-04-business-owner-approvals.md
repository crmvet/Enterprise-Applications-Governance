# S-04 Business owner: approvals, owners and the lifecycle

| | |
|---|---|
| Personas | Nadia Ferreira, application owner; Mateo Brandt, PMO, as the sponsor of Claims Triage Agent |
| Use cases | UC-01, P03, UC-02, UC-14, P08, P07, A-LIFECYCLE, A-GRADUATION |
| Screens | Approvals, the Decision page, Application 360 (Lifecycle and gates, Owners), Owners (Gaps) |
| Time | 1 minute 45 seconds in the 15-minute run, 6 minutes in full |
| End-to-end spec | `src/tests/e2e/specs/S-04-business-owner-approvals.spec.ts` |
| Story section | W3 People answer every approval |
| Ready check | `demo check --base <url> --scenario S-04` |

**Before it starts.** Act as Nadia Ferreira: "Nadia Ferreira, application owner" on a demo install, "Nadia Ferreira,
business owner" on a local run. S-03 has run, so the platform admin has answered "Weather alerts for outdoor jobs".

**Reset.** A fresh store: five answers, the runbook check, the hand-over and the deputy proposal are made here.

Every answer below uses the same sheet: Approve, a typed reason, "Check my answer", Approve, then Approve again on
"Approve this decision?". The answer is final.

## Steps

### S-04.1 What waits for the business owner

- **Persona:** Nadia Ferreira.
- **Page and clicks:** Approvals, "Waiting for me" (`/app/approvals?view=mine`).
- **Say:** "Everything asked of one person, through any of her roles or her owner roles, in one list."
- **Proof on screen:** "Waiting for me" with the count 7, among them "Authorize the change: Retire the old export
  flow", "Accept the intake request: Weather alerts for outdoor jobs", "Accept that inspection photos stay on devices
  for up to 7 days while offline." (marked "You proposed this"), "Allow Claims Triage Agent to send damage claims to the
  approved AI model to route them." and "End Wren Abernathy as business owner, make Nadia Ferreira business owner ...".
- **Reset:** nothing.

### S-04.2 A change on the decision page

- **Persona:** Nadia Ferreira.
- **Page and clicks:** Answer on "Authorize the change: Retire the old export flow". The Decision page opens.
- **Say:** "A person answers on the decision page, signed in as herself. The page says which rule asks her and when
  the request expires."
- **Proof on screen:** "Your answer is asked. You answer here, signed in as yourself." "You are asked as Business owner
  of the application. Expires in N days." (N is the days left: 4 on a fresh store) WHAT THE RULES REQUIRE: "Rule: Business owner authorizes the change."
- **Reset:** nothing.

### S-04.3 Approve the change

- **Persona:** Nadia Ferreira.
- **Page and clicks:** Approve with the reason "The workload view replaces the export, and the inspectors tested it
  last week."
- **Say:** "Her answer is the last one needed, so the engine accepts the decision and the change is authorized."
- **Proof on screen:** the check: "Your answer would count toward the approval. The decision would be accepted." The
  answer: "Your approval is recorded and counts. The engine accepted the decision." The decision reads Accepted.
- **Reset:** a fresh store.

### S-04.4 Her own risk acceptance

- **Persona:** Nadia Ferreira.
- **Page and clicks:** Approvals, Answer on "Accept that inspection photos stay on devices for up to 7 days while
  offline." Approve with a typed reason.
- **Say:** "Balanced lets the person who proposed a decision answer it, unless a separation rule applies. The page
  says so before she answers."
- **Proof on screen:** "A typed reason is needed to approve" and "You proposed this decision. The configuration in
  force lets the person who proposed a decision answer it, unless a separation rule applies." After the answer:
  "The engine accepted the decision."
- **Reset:** a fresh store.

### S-04.5 She accepts the business owner role

- **Persona:** Nadia Ferreira.
- **Page and clicks:** Approvals, Answer on "Accept the intake request: Weather alerts for outdoor jobs". Approve.
- **Say:** "The request names her as business owner, so she is asked by name. Her yes registers the application."
- **Proof on screen:** before: "The accept gate is not ready yet. If it is still not ready once every approval is in,
  the decision stays proposed until it is", and "Your answer may itself make it ready". After: "The decision is now accepted. 3 requests that are no longer needed were
  canceled." The register now holds "Weather alerts for outdoor jobs".
- **Reset:** a fresh store.

### S-04.6 A data-use card

- **Persona:** Nadia Ferreira, as data owner of Customers.
- **Page and clicks:** Approvals, Answer on "Allow Claims Triage Agent to send damage claims to the approved AI model
  to route them." Read "The data use it decides", then Approve with a typed reason.
- **Say:** "The data owner of the domain decides what an application may do with its data, including sending it to an
  AI model."
- **Proof on screen:** THE USE "DU-0013: Reads at Customer Prod", THE RULE "AI use: Decision", AI "Sends data to AI:
  The approved AI model, in the region", STATE "0 of 1 approval". After: "The decision is now accepted. 1 request that
  is no longer needed was canceled."
- **Reset:** a fresh store.

### S-04.7 The owner change, first answer

- **Persona:** Nadia Ferreira.
- **Page and clicks:** Approvals, Answer on "End Wren Abernathy as business owner, make Nadia Ferreira business owner
  and end Nadia Ferreira as deputy business owner of Claims Triage Agent." Approve.
- **Say:** "Wren leaves. The owner change says exactly what acceptance changes, and the role is never empty."
- **Proof on screen:** WHAT ACCEPTANCE CHANGES: "Wren Abernathy stops being business owner. Nadia Ferreira becomes
  business owner from" Wren's leave date. After her answer: "The decision stays proposed." The sponsor, Mateo Brandt,
  is still asked.
- **Reset:** a fresh store.

### S-04.8 The sponsor answers

- **Persona:** switch to Mateo Brandt, PMO.
- **Page and clicks:** Approvals, Answer on the same owner change. Approve with a typed reason.
- **Say:** "The business owner role changes hands, so Balanced also asks the sponsor."
- **Proof on screen:** "Asks Sponsor of the application" on his list. After: "The engine accepted the decision."
- **Reset:** a fresh store.

### S-04.9 The Go-live gate and the runbook

- **Persona:** switch back to Nadia Ferreira.
- **Page and clicks:** Inspection Scheduler's Application 360
  (`/app/records/business-application/00000000-0000-4000-8000-000000000511`), "Lifecycle and gates". On "Runbook
  current", "Confirm it is still true", then Confirm in the sheet.
- **Say:** "A gate passed once can stop being met. A stale runbook shows on the gate, and one confirmation fixes it."
- **Proof on screen:** before: "Go-live passed, but 2 requirements are not met now." with "Owner accepted after
  testing: Not met" and "Runbook current: Not met". After: "The check is recorded, as reported." and "Runbook current:
  Met"; the header reads "1 requirement is not met now".
- **Reset:** a fresh store.

### S-04.10 Hand over to IT

- **Persona:** Nadia Ferreira.
- **Page and clicks:** "Summary", then Hand over in Owners. New technical owner "Owen Achterberg", New deputy "Hugo
  Brandvold", Reason "The app graduates from the maker to IT: it runs every field inspection now." "Check first",
  "Propose the hand-over", then "Send for approval".
- **Say:** "One ownership decision names the new holder and deputy and ends the ones they replace. The new owners are
  asked by name."
- **Proof on screen:** "Now: Priya Castell holds the role, with Lena Okoro as deputy." The answer: "Submitted. 3
  approvals are asked." ASKED NOW: Business owner of the application, Owen Achterberg and Hugo Brandvold, each "asked by
  name for the technical owner role".
- **Reset:** a fresh store.

### S-04.11 Propose a deputy

- **Persona:** Nadia Ferreira.
- **Page and clicks:** Owners, the Gaps view (`/app/owners?view=gaps`). "Propose a deputy" on Inspection Scheduler,
  New deputy "Lena Okoro", a reason, Propose. On the notice, "Submit for approval", then Submit.
- **Say:** "A gap closes only when an ownership decision is accepted."
- **Proof on screen:** the notice "Proposed. Nobody is asked until it is submitted." The answer: "Submitted. 2
  approvals are asked." Inspection Scheduler shows "Owner change waiting". Weather alerts for outdoor jobs, registered
  in S-04.5, now lists "Business owner: no deputy" too.
- **Reset:** a fresh store.
