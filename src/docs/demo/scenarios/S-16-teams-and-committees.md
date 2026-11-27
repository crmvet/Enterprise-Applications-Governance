# S-16 Teams and committees: who answers, who supports, what waits

| | |
|---|---|
| Personas | Owen Achterberg, platform admin; Marcus Oyelaran, architect and change board; Tomas Rendel, change board |
| Use cases | UC-07, UC-02, A-ROLES |
| Screens | Teams and committees, the team page, the Decision page |
| Time | 45 seconds in the 15-minute run, 3 minutes in full |
| End-to-end spec | `src/tests/e2e/specs/S-16-teams-and-committees.spec.ts` |
| Story section | W2 Owners, teams and committees |
| Ready check | `demo check --base <url> --scenario S-16` |

**Before it starts.** Act as Owen Achterberg. The Change board's card (the Desk Booking exception Tomas Rendel
proposed) is still open.

**Reset.** A fresh store when Marcus answers in S-16.4; until then nothing changes.

## Steps

### S-16.1 Every team the product knows

- **Persona:** Owen Achterberg, platform admin.
- **Page and clicks:** Teams (`/app/teams`).
- **Say:** "Governance is run by committees and supported by teams. A team is an Entra group the product already knows;
  its kind comes from the rules in force."
- **Proof on screen:** the tabs All 19, Committees 9, Support teams 4, Role groups 6. "The members are the demo's
  fictional people in each group: the personas and the estate's other people. A real install reads them live from
  Entra and never stores them." Work waits on 7 of the 9 committees: not the Data governance leads, and not the
  Configuration admins once Jonah accepted their change in S-10 (8 of the 9 when S-16 plays alone on a fresh store);
  Service Desk reads "6 applications, 7 owner roles held" and "1 gap".
- **Reset:** nothing.

### S-16.2 A committee and what waits on it

- **Persona:** Owen Achterberg.
- **Page and clicks:** the Committees tab (`/app/teams?kind=committee`), then Change board.
- **Say:** "The Change board decides exceptions about changes. Its page shows who is in it, what it decides, and what
  waits on it now."
- **Proof on screen:** "Change board, Committee. A committee that holds 1 product role and answers 1 decision type."
  Members: Felix Arendt, Ines Varga, Marcus Oyelaran and Tomas Rendel. Decisions it answers: Exception, "Through the
  Change board role", Approves, Accountable. "Waiting now": "Grant an exception: the Desk Booking calendar connector
  update goes ahead without a rollback rehearsal in test.", "Answer on the decision page". "What its members own"
  lists the applications its members own in their own name.
- **Reset:** nothing.

### S-16.3 A support team and its gaps

- **Persona:** Owen Achterberg.
- **Page and clicks:** All teams and committees, the Support teams tab, then Service Desk.
- **Say:** "A support team holds owner roles on applications, so its page shows their gaps."
- **Proof on screen:** "Service Desk, Support team. A support team that holds owner roles on 6 applications." Members
  Clara Voss, Hugo Brandvold and Theo Marsh. "Applications and owner roles" 6, among them Inspection Scheduler "Support owner
  (deputy)", "1 gap". "Owner gaps": Inspection Scheduler "Business owner: no deputy", "Propose on Owners".
- **Reset:** nothing.

### S-16.4 The committee member answers

- **Persona:** switch to Marcus Oyelaran, architect and change board.
- **Page and clicks:** Teams, Change board, "Answer on the decision page". Approve with the reason "The connector only
  reads calendars, and the vendor can put the old version back in minutes."
- **Say:** "The member answers on the decision page, never on the team page. Any of the four board members may answer;
  Tomas proposed it, so Marcus, another member, answers."
- **Proof on screen:** "You are asked as Change board. Expires in" a few days. The page does not say "You proposed this
  decision". After: "The engine accepted the decision."
- **Reset:** a fresh store.

### S-16.5 The answer shows on the team

- **Persona:** switch to Tomas Rendel, change board.
- **Page and clicks:** back to the Change board's page (`/app/teams/00000000-0000-4000-8000-000000000111`).
- **Say:** "What the committee answered stays with the committee."
- **Proof on screen:** "Waiting now" no longer lists the Desk Booking exception: "Nothing waits on the team as a
  whole now." (or "Nothing waits on this team or its members now." when no member is asked by name). "Recent answers"
  with the count 1: the Desk Booking exception, "Approved" by Marcus Oyelaran.
- **Reset:** a fresh store.

### S-16.6 The owners of the data

- **Persona:** Owen Achterberg.
- **Page and clicks:** Teams, the Role groups tab, then Data owners. Then Kai Ostrowski's person page
  (`/app/people/00000000-0000-4000-8000-000000001119`).
- **Say:** "Data owners answer for their own domains, by name. The team page shows what is asked of each of them and
  what they own; a person's page shows the data they own beside their applications."
- **Proof on screen:** Decisions it answers: Data use, "As the named data owner of their own data domains, not through
  the team". The Data owner role card says the same: "Answers as named owners". Waiting now, "Asked of its members by name": the requests asked of Greta Lund by name (Kai Ostrowski
  answered his data-use card in S-07; played alone, his card is listed too). "What its members own": "Field
  operations / Safety findings". On Kai's page: the figure "Data domains" and "Data it owns": "Field operations /
  Safety findings".
- **Reset:** nothing.
