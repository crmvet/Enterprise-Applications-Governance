# S-03 Platform admin: triage, the gap check, places and leavers

| | |
|---|---|
| Persona | Owen Achterberg, platform admin |
| Use cases | P01, A-DELIVERY, P06, A-PLACES, P08 |
| Screens | Intake (Triage), the intake request, Gap check, Places, Owners (Leavers) |
| Time | 1 minute 15 seconds in the 15-minute run, 4 minutes in full |
| End-to-end spec | `src/tests/e2e/specs/S-03-triage-gap-check-places-leavers.spec.ts` |
| Story section | W7 From request to retirement |
| Ready check | `demo check --base <url> --scenario S-03` |

**Before it starts.** Act as Owen Achterberg ("Owen Achterberg, platform admin"). S-02 has run, so three requests wait
for triage; on a store where S-02 has not run, two wait.

**Reset.** A fresh store: the triage answer, the link, the claim and the dismissal are made here.

## Steps

### S-03.1 The triage queue

- **Persona:** Owen Achterberg, platform admin.
- **Page and clicks:** Intake, the Triage view (`/app/intake?view=triage`).
- **Say:** "Every request comes through one front door. The platform admin triages what is submitted."
- **Proof on screen:** "Waiting for triage" with the count 3: "Crew overtime tracker" (Drafted by Claude Code for
  Priya Castell), "Spare parts returns from field vans" and "Weather alerts for outdoor jobs", each "Suggested Tier 2".
- **Reset:** nothing.

### S-03.2 Who is asked, and who only advises

- **Persona:** Owen Achterberg.
- **Page and clicks:** open "Weather alerts for outdoor jobs".
- **Say:** "The rules name who decides and who is only consulted. A consultation is advice; it never counts."
- **Proof on screen:** "You are asked. As Platform admin." WHO IS ASKED: "Nadia Ferreira, the owner the request names,
  everyone must approve"; "Platform admin, the first answer counts"; Architect, PMO and Security reviewer, each
  "Consulted: a recommendation only, never counts".
- **Reset:** nothing.

### S-03.3 The platform admin approves

- **Persona:** Owen Achterberg.
- **Page and clicks:** Approve. Type a reason, such as "No current app covers weather checks before mast jobs; Crew
  Roster plans crews but has no forecast." "Check my answer", then Approve, then Approve again on "Approve this
  decision?".
- **Say:** "The answer is final and recorded under the person's name. The engine counts it under the approval rule."
- **Proof on screen:** the check says "Your answer would count toward the approval. The decision would stay
  proposed." The answer says "Your approval is recorded and counts. The decision still waits for other answers." After
  the page reads again: "Still needed: 1 from Nadia Ferreira, the owner the request names."
- **Reset:** a fresh store.

### S-03.4 Link the delivery work item

- **Persona:** Owen Achterberg.
- **Page and clicks:** under Links, "Add a link". Kind of link "Azure DevOps work item", Address
  `https://dev.example.org/field-ops/_workitems/edit/4182`, Label "Weather alerts for outdoor jobs", ID in its own tool
  `4182`. "Save the link".
- **Say:** "The product stores links, not copies: the ID, the address and a label."
- **Proof on screen:** "The delivery link is saved. It stores an ID, a URL and a label, never a copy." Links shows 1,
  "Weather alerts for outdoor jobs, Azure DevOps work item, Delivery".
- **Reset:** a fresh store.

### S-03.5 The gap check

- **Persona:** Owen Achterberg.
- **Page and clicks:** More, Work, Gap check (`/app/gap-check`).
- **Say:** "What runs in the estate but is not in the register, and what the register holds that has gone."
- **Proof on screen:** the figures Not registered 2, Vanished 1, Claimed 1, Dismissed 1, Runs 1. The latest run:
  "Finished ... 33 items seen, 4 new claims, 1 vanished."
- **Reset:** nothing.

### S-03.6 Claim the photo flow

- **Persona:** Owen Achterberg.
- **Page and clicks:** Claim on "Copy visit photos to SharePoint" (Suggested to Priya Castell). In the Application
  search type "Visit Report" and choose Visit Report Agent. "Check first", "Claim it", then "Claim it" on "Claim this
  item?".
- **Say:** "Claiming into an application someone else owns asks its owners to confirm it."
- **Proof on screen:** the check lists "WOULD BE ASKED: Business owner of the application (everyone must
  approve)". The answer: "The item is claimed and added to Visit Report Agent. 1 owner is asked to confirm it on the
  decision page."
- **Reset:** a fresh store.

### S-03.7 Dismiss the personal test flow

- **Persona:** Owen Achterberg.
- **Page and clicks:** Dismiss on "Test reminder flow (personal)". Reason "A personal test flow; its maker removes it
  this week." Dismiss. Then the Vanished view.
- **Say:** "Not every item is a business application. A dismissal keeps its reason and can be undone."
- **Proof on screen:** "The claim is dismissed. Reopen brings it back." Not registered shows "Nothing
  unregistered is waiting." Vanished lists the Timesheets canvas app.
- **Reset:** a fresh store.

### S-03.8 Places

- **Persona:** Owen Achterberg.
- **Page and clicks:** More, Register, Places (`/app/places`). Then the Pipelines view.
- **Say:** "Where applications are built and run: the targets by stage, and the pipelines that reach them in order."
- **Proof on screen:** Targets 13: Development 1, Test 5, Production 7. People Test, "Last seen" today, "Nothing
  deployed yet". Pipelines shows 5, such as "Inspection Scheduler release: 1. Field Ops Dev then 2. Field Ops Test then
  3. Field Ops Prod".
- **Reset:** nothing.

### S-03.9 Leavers

- **Persona:** Owen Achterberg.
- **Page and clicks:** Owners, the Leavers view (`/app/owners?view=leavers`).
- **Say:** "People leave. Each owner role they hold needs a successor through an ownership decision."
- **Proof on screen:** "Leavers" with the count 2. Amara Quist, "Account disabled in Entra", Travel Requests,
  Business owner, "No deputy: the role has nobody once they leave." Wren Abernathy, "Leaves" in about three weeks,
  "Holds 7 owner roles and 4 deputy roles on 7 applications.", Claims Triage Agent "Owner change proposed".
- **Reset:** nothing.
