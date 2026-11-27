# S-11 Platform admin: roles and access

| | |
|---|---|
| Persona | Owen Achterberg, platform admin |
| Use cases | A-ROLES, UC-07 |
| Screens | Roles and access, one role's page, the binding check |
| Time | 30 seconds in the 15-minute run, 3 minutes in full |
| End-to-end spec | `src/tests/e2e/specs/S-11-roles-and-access.spec.ts` |
| Story section | W8 Rules that change safely |
| Ready check | `demo check --base <url> --scenario S-11` |

**Before it starts.** Act as Owen Achterberg ("Owen Achterberg, platform admin").

**Reset.** Nothing to reset: the binding check writes nothing.

## Steps

### S-11.1 Every product role

- **Persona:** Owen Achterberg, platform admin.
- **Page and clicks:** More, Govern, Roles and access (`/app/roles`).
- **Say:** "People hold a product role only through its Entra group. What each role may do comes from the configuration
  in force."
- **Proof on screen:** the figures Product roles 15, No group bound 0, Workload grants 2, Separation rules 2. "Roles"
  with the count 19: Requester "Through Product requesters", Maker "Through Field ops makers", Application owner
  "Through 5 owner roles", Platform admin "You hold it", Auditor and Reader "Reads only".
- **Reset:** nothing.

### S-11.2 The data governance lead

- **Persona:** Owen Achterberg.
- **Page and clicks:** open Data governance lead (`/app/roles?role=data-governance-lead`).
- **Say:** "Each role lists the tools it may call and how far an AI client acting for a holder may go with each."
- **Proof on screen:** "Appoints the owners of top-level data domains, keeps data locations and mappings current, and
  sees every data gap." "Tools it may call" with the count 67, each tool marked such as "AI clients may read it for the
  person" or "An AI client goes as far as a proposed decision, which a person submits." Further down: "Groups and
  holders", "Decision rights" and "Approvals it gives".
- **Reset:** nothing.

### S-11.3 Two configuration admins

- **Persona:** Owen Achterberg.
- **Page and clicks:** All roles, then Configuration admin (`/app/roles?role=configuration-admin`). Scroll to "Groups
  and holders".
- **Say:** "Two people hold it, so a change can be accepted by a second person. The Regulated preset makes that a
  rule; Balanced also lets the admin who instructed it answer."
- **Proof on screen:** "On this install the members are the demo's fictional people in each group (the personas and
  the estate's other people), not people read from Entra."
  HOLDERS NOW: Jonah Pereira and Yusuf Brennan. "Decision rights" 2 (Configuration change, Gate policy change) and
  "Approvals it gives" 4.
- **Reset:** nothing.

### S-11.4 The binding check

- **Persona:** Owen Achterberg.
- **Page and clicks:** All roles, then "Run the binding check". Scroll to "Binding check".
- **Say:** "Is every human role bound to a group that exists? On the demo, the groups and holders are the fictional
  personas, and the page says so."
- **Proof on screen:** "Checked ... against the demo personas. Every human role is bound to a group that exists. The
  findings below are about workload identities." GROUPS lists each role with "Group exists".
- **Reset:** nothing.
