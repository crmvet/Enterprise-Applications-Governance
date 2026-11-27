# S-20 Requester: a request from the web app, with no AI

| | |
|---|---|
| Personas | Lena Okoro, requester; Nadia Ferreira, application owner |
| Use cases | P01, P02, UC-05, UC-07 |
| Screens | Intake (My requests), the intake wizard, Approvals, the Decision page |
| Time | 3 minutes in full |
| End-to-end spec | `src/tests/e2e/specs/S-20-requester.spec.ts` |
| Story section | W7 From request to retirement |
| Ready check | `demo check --base <url> --scenario S-20` |

**Before it starts.** Act as Lena Okoro ("Lena Okoro, requester"). Her draft "Depot first-aid kit checks" is not
submitted yet.

**Reset.** A fresh store: Lena's draft is finished and submitted, and Nadia accepts the role, here.

## Steps

### S-20.1 The requester's own requests

- **Persona:** Lena Okoro, requester.
- **Page and clicks:** Intake, the My requests view (`/app/intake?view=mine`).
- **Say:** "People do their work in the web app too. Every save goes through the same engine checks as an AI client's."
- **Proof on screen:** "My requests" with the count 2: "Spare parts returns from field vans", Submitted, "Suggested
  Tier 2"; "Depot first-aid kit checks", "Draft. Last saved today.", with Continue.
- **Reset:** nothing.

### S-20.2 Finish the draft

- **Persona:** Lena Okoro.
- **Page and clicks:** Continue on "Depot first-aid kit checks". In Business owner type `Nadia` and choose Nadia
  Ferreira. "Save draft", then "Save draft" in the sheet "Save the draft".
- **Say:** "The Accept gate needs a business owner, and the person named is asked by name to accept the role."
- **Proof on screen:** the sheet lists Title "Depot first-aid kit checks", Business owner "Nadia Ferreira", Proposed
  kind "Power Platform app", Horizon "Later". The answer: "The intake request is saved as a draft. It goes nowhere until
  it is submitted."
- **Reset:** a fresh store.

### S-20.3 A tier 1 answer

- **Persona:** Lena Okoro.
- **Page and clicks:** Next twice, to Tier questions. Choose "Reads only, or changes only the person's own items",
  "Little", "The builder or a small team" and "No". "Save tier answers", then "Save answers".
- **Say:** "A small, low-risk app gets the lightest path."
- **Proof on screen:** "Suggested tier: Tier 1. Only an accepted tier decision sets the tier."
- **Reset:** a fresh store.

### S-20.4 Submit

- **Persona:** Lena Okoro.
- **Page and clicks:** close the sheet, Next twice, to Review and submit. Read WHO DECIDES NEXT. Submit, then Submit in
  the sheet "Submit the request".
- **Say:** "At the lowest tier no approval request is needed beyond the owner's yes."
- **Proof on screen:** "The rules in force list no approval for the intake decision at this tier. At the lowest tier
  the engine accepts the intake on submit, with no approval request beyond a yes from the named business owner". The
  answer: "Submitted. The engine accepts the decision with no other approval request once the business owner the
  request names accepts the role." ASKED NOW: "Nadia Ferreira, asked by name about the business owner role".
- **Reset:** a fresh store.

### S-20.5 The owner says yes

- **Persona:** switch to Nadia Ferreira.
- **Page and clicks:** Approvals, Answer on "Accept the intake request: Depot first-aid kit checks". Approve with a
  typed reason.
- **Say:** "Her answer is the only one needed, and the application is registered."
- **Proof on screen:** before: "You are asked by name." and "The accept gate is not ready yet. If it is
  still not ready once every approval is in, the decision stays proposed until it is". After: "The decision is now accepted."
  "Depot first-aid kit checks" is in the register at the stage "Prioritize and fund", with Nadia Ferreira as business
  owner.
- **Reset:** a fresh store.
