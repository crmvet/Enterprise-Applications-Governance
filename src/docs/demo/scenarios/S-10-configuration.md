# S-10 Configuration admins: change the rules safely

| | |
|---|---|
| Personas | Yusuf Brennan, configuration admin; Jonah Pereira, the second configuration admin |
| Use cases | P11, UC-11 |
| Screens | Configuration (Rules in force, Drafts, Versions), the configuration draft, the Decision page |
| Time | 45 seconds in the 15-minute run, 4 minutes in full |
| End-to-end spec | `src/tests/e2e/specs/S-10-configuration.spec.ts` |
| Story section | W8 Rules that change safely |
| Ready check | `demo check --base <url> --scenario S-10` |

**Before it starts.** Act as Yusuf Brennan ("Yusuf Brennan, configuration admin"). Version 1 is in force, and Yusuf's
draft waits for a second configuration admin.

**Reset.** A fresh store: Jonah's answer puts version 2 in force, and every write after it is stamped with version 2.

## Steps

### S-10.1 The rules in force

- **Persona:** Yusuf Brennan, configuration admin.
- **Page and clicks:** More, Govern, Configuration (`/app/configuration`).
- **Say:** "Every rule is a configuration row, read in plain words: tiers, gates, decision rights, approval rules,
  reviews, roles and the data taxonomy. The engine is code over this configuration."
- **Proof on screen:** "Version 1 is in force since" the install day. The figures Version in force 1, Open drafts 1,
  Rules in force 693 ("Configuration rows of 43 kinds"), Health findings 0. The Data taxonomy area: Data classification
  4, Data domain level 3, Data domain 25, Data platform 20. "Gate levels for a normal change" by tier and stage.
- **Reset:** nothing.

### S-10.2 The draft

- **Persona:** Yusuf Brennan.
- **Page and clicks:** the Drafts view (`/app/configuration?view=drafts`).
- **Say:** "A configuration admin proposes. A draft changes nothing."
- **Proof on screen:** "Open drafts" with the count 1: "Configuration draft CFG-0001, Submitted", "The AI use rule of
  Workplace / Visitor log changes from not set to never.", "Instructed by Yusuf Brennan", "From version 1", "1 row
  change".
- **Reset:** nothing.

### S-10.3 What the change touches

- **Persona:** Yusuf Brennan.
- **Page and clicks:** open the draft.
- **Say:** "Before anyone accepts it, the draft shows the exact diff, the rules check, and what it would touch."
- **Proof on screen:** What it changes: "Data domain, Visitor log, AI use rule, Not set becomes Never". Rules check:
  "The rules check passed". Decision and approvals: "A second configuration admin answers on the decision page."
  What it touches: "No gate level moves", records in flight keep their rules, and DATA USES "DU-0030: Visitor Sign-In
  Kiosk, Workplace / Visitor log, Stays needs a decision".
- **Reset:** nothing.

### S-10.4 Another person accepts it

- **Persona:** switch to Jonah Pereira, configuration admin.
- **Page and clicks:** Approvals, Answer on "Accept the configuration change: the AI use rule of Workplace / Visitor
  log changes from not set to never, version 1 to 2." Approve with a typed reason.
- **Say:** "In the demo another admin accepts the exact change, and the data owner of Workplace is consulted by name.
  Balanced also lets the instructing admin answer, and the page says so; the Regulated preset adds a separation rule
  that refuses it."
- **Proof on screen:** What it decides about: "The AI use rule of Workplace / Visitor log changes from not set to
  never", in plain words. WHAT CHANGES: "Version 1 becomes version 2 once accepted." WHAT THE RULES REQUIRE:
  "Configuration admin. The first answer counts." CONSULTATIONS: Hugo Brandvold, "Consulted, advice only". After: "The
  engine accepted the decision. 1 request that is no longer needed was canceled."
- **Reset:** a fresh store.

### S-10.5 Version 2 is in force

- **Persona:** Jonah Pereira.
- **Page and clicks:** Configuration, the Versions view (`/app/configuration?view=versions`).
- **Say:** "Every write from now on is stamped with version 2. Older records keep the version they were checked under."
- **Proof on screen:** "Version 2 is in force since" today. "Version 2, In force", "Accepted by Jonah Pereira",
  "Instructed by Yusuf Brennan", a fingerprint. The page line reads "Configuration version 2."
- **Reset:** a fresh store.

### S-10.6 Optional: Claude Code proposes a standard change model

- **Persona:** Yusuf Brennan, in Claude Code, connected as `yusuf` on a local run and on a demo install
  ([claude-code.md](../claude-code.md), "S-10.6").
- **Page and clicks:** type `Propose a standard change model called Monthly content refresh: a tested content update
  to one application at tier 1 or 2, in a window of two hours or less. Check it first and do not save it.`
- **Say:** "An AI client may draft a configuration change for an admin. It goes as far as a draft; a person accepts it."
- **Proof on screen:** `propose_configuration_change` with `validateOnly`: "The change is valid. Nothing was stored
  (validateOnly).", the diff with `standard-change-model` `monthly-content-refresh` added, `isAiCreated` true, the
  stamp "Yusuf Brennan" for whom Claude Code acted, and
  "Submit the draft. A person accepts the decision on its decision page. Nothing changes until then."
- **Reset:** nothing: nothing was stored.
