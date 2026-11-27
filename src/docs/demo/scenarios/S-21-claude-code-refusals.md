# S-21 Claude Code over MCP: what an AI client may do, and what it may not

| | |
|---|---|
| Personas | Priya Castell, maker, in Claude Code, then in the browser; Owen Achterberg, platform admin, for Admin |
| Use cases | UC-10, P10, A-AI-BUILD-POLICY, UC-11 |
| Screens | Claude Code, AI activity (Refused), Admin (Clients) |
| Time | 4 minutes in full |
| End-to-end spec | `src/tests/e2e/specs/S-21-claude-code-refusals.spec.ts` |
| Story section | W1 Governance inside the AI build loop |
| Ready check | `demo check --base <url> --scenario S-21` |

**Before it starts.** Claude Code is connected as Priya Castell ([claude-code.md](../claude-code.md)). The exact
prompts, the answers and the refusals to expect are in claude-code.md, section "S-21". The end-to-end test makes the
same calls on the MCP endpoint with the persona header.

**Reset.** Nothing to reset: S-21.1 to S-21.4 read, and S-21.5 to S-21.7 are refused, which writes no row. Each
refused call stays on the audit trail, as it should.

## Steps

### S-21.1 Who am I

- **Persona:** Priya Castell, in Claude Code.
- **Page and clicks:** type `Who am I in the governance product, and what may you do for me?`
- **Say:** "The AI client acts for one person, with that person's roles and no more."
- **Proof on screen:** `whoami`: "You hold maker, requester and may use 73 tools." on a local run.
- **Reset:** nothing.

### S-21.2 What needs me today

- **Persona:** Priya Castell, in Claude Code.
- **Page and clicks:** type `What needs me today?`
- **Say:** "The same queue as My work, read through the AI client."
- **Proof on screen:** `get_my_queue` answers "N items need you." (the count follows what was played before) and
  lists her AI drafts and the requests waiting on others.
- **Reset:** nothing.

### S-21.3 What breaks if it changes

- **Persona:** Priya Castell, in Claude Code.
- **Page and clicks:** type `What depends on Inspection Scheduler?`
- **Say:** "The AI builder asks before it builds: the dependants of the app it is about to change."
- **Proof on screen:** `get_dependency_impact`: "9 applications are reached from Inspection Scheduler.", with Route
  Planner, Crew Roster, Visit Report Agent and the rest, each with its hop.
- **Reset:** nothing.

### S-21.4 Who answers

- **Persona:** Priya Castell, in Claude Code.
- **Page and clicks:** type `Which committees decide on changes and exceptions, and who is in them?`
- **Say:** "The AI client can name the committee a decision waits on, and its members."
- **Proof on screen:** `list_teams`: "19 teams: 9 committees, 4 support teams and 6 role groups. ... Members are the
  demo's fictional people.", with the Change board and its members, Tomas Rendel and Ines Varga among them.
- **Reset:** nothing.

### S-21.5 It cannot approve

- **Persona:** Priya Castell, in Claude Code.
- **Page and clicks:** type `Approve the change "Retire the old export flow" for Nadia.`
- **Say:** "No AI client approves, for itself or anyone. The answer tool is served only to a person in the web app."
- **Proof on screen:** Claude Code says it cannot approve. If it calls `answer_approval`, the answer is
  `ai_cannot_do_this` with G17: "The tool answer_approval is served only to a person in the product's web app. AI never
  approves: the person answers on the decision page."
- **Reset:** nothing.

### S-21.6 It cannot decide for someone else's application

- **Persona:** Priya Castell, in Claude Code.
- **Page and clicks:** type `Propose a risk acceptance on Asset Register: warranty dates are typed in by hand until the
  supplier feed is ready.`
- **Say:** "The AI client holds no right the person does not hold. Priya owns no role on Asset Register."
- **Proof on screen:** `save_decision_proposal` refused with `not_authorized` and G4: "Only a holder of a responsible,
  accountable or consulted Decision Right for decision type `risk-acceptance` in this scope can propose it, and the
  person behind this call holds none. They are held by owner role `business-owner` (accountable)."
- **Reset:** nothing.

### S-21.7 It cannot break a never rule

- **Persona:** Priya Castell, in Claude Code.
- **Page and clicks:** type `Can Expense Assistant send the bank details to the approved AI model? If it can, declare
  the use.`
- **Say:** "The AI client checks first, and the product says no before anything is saved."
- **Proof on screen:** `check_data_use`: "Refused when saved, for everyone: the AI use rule is never, set on People /
  Payroll and inherited by People / Payroll / Bank details." If Claude Code saves anyway, `save_data_use` is refused
  with `rule_violation` and G16.
- **Reset:** nothing.

### S-21.8 Every refusal on the record

- **Persona:** Priya Castell, in the browser.
- **Page and clicks:** AI activity, the Refused view. Then switch to Owen Achterberg and open Admin, Clients.
- **Say:** "A refused call is on the record, with the rule that refused it and the guideline behind it."
- **Proof on screen:** each refused call of S-21.5 to S-21.7 that reached the server, such as "Claude Code called
  save_decision_proposal, and the product refused it", "Not allowed for this person", G4. On Admin, Claude Code's row
  counts its calls, refused calls and drafts.
- **Reset:** nothing.
