# S-08 AI lead and security reviewer: agents

| | |
|---|---|
| Personas | Dmitri Halloran, AI lead; Noor Safadi, security reviewer |
| Use cases | A-AGENTS, P02, P10 |
| Screens | Agents (Register, Flagged), AI activity (AI answers), Approvals, the Decision page, Reviews |
| Time | 45 seconds in the 15-minute run, 3 minutes in full |
| End-to-end spec | `src/tests/e2e/specs/S-08-agents.spec.ts` |
| Story section | W6 Agents with an agreed autonomy |
| Ready check | `demo check --base <url> --scenario S-08` |

**Before it starts.** Act as Dmitri Halloran ("Dmitri Halloran, AI lead").

**Reset.** A fresh store: the autonomy level of Supplier Price Agent is proposed and agreed here.

## Steps

### S-08.1 The agent register

- **Persona:** Dmitri Halloran, AI lead.
- **Page and clicks:** More, Register, Agents (`/app/agents`).
- **Say:** "Every agent with its agreed autonomy level, its security review, its go-live gate and its owners."
- **Proof on screen:** the figures Agents 6, No autonomy level 3, Autonomy decisions 1. Supplier Price Agent, "Tier 3,
  Risk and tier", Autonomy "Not agreed", "Needed before go-live", "No security review", with "Propose autonomy level".
- **Reset:** nothing.

### S-08.2 Propose an autonomy level

- **Persona:** Dmitri Halloran.
- **Page and clicks:** "Propose autonomy level" on Supplier Price Agent. Autonomy level Assist; What the agent does
  "Compares supplier price lists with the parts catalog and drafts a price change list for the buyer."; Why this level
  "A buyer checks and sends every price change; the agent changes nothing itself." "Check first", Propose, then "Send
  for approval".
- **Say:** "People agree how much an agent may do on its own. The level is stored on the agent only once it is agreed."
- **Proof on screen:** the levels Assist, Execute and Route. The check: "The decider would hold the
  role "Security reviewer"." The answer: "Submitted. 1 approval is asked, and 1 person is consulted." ASKED NOW: Security
  reviewer; AI lead (consulted, advice only).
- **Reset:** a fresh store.

### S-08.3 Flagged agents

- **Persona:** Dmitri Halloran.
- **Page and clicks:** the Flagged view (`/app/agents?view=flagged`).
- **Say:** "The product flags agents from its own records. The AI lead decides on each: keep, reassign, consolidate or
  retire."
- **Proof on screen:** "Agents that need attention" with the count 6. Onboarding Buddy Agent: "A flagged-agent decision
  waits for people to decide." Under "Decisions on flagged agents": "Keep Onboarding Buddy Agent, shared with the pilot
  group only until its go-live gate is met.", "Proposed by Owen Achterberg", with Answer.
- **Reset:** nothing.

### S-08.4 An AI answer is a recommendation only

- **Persona:** Dmitri Halloran.
- **Page and clicks:** AI activity, the AI answers view (`/app/ai-activity?view=answers`).
- **Say:** "An agent may answer an approval card. Its answer is stored as a recommendation and never counts."
- **Proof on screen:** "Approval answers by an AI actor" with the count 1: the decision
  `Authorize "Invoice Matching 1.12" in the November 14 window.`, the line `Finance Approvals Agent answered
  "Recommendation only"`, and the badge "Not counted".
- **Reset:** nothing.

### S-08.5 The security reviewer agrees the level

- **Persona:** switch to Noor Safadi, security reviewer.
- **Page and clicks:** Approvals, Answer on "Agree the autonomy level Assist for Supplier Price Agent". Approve with
  the reason "Assist fits: a buyer sends every price change, and the agent writes nothing."
- **Say:** "Under Balanced the security reviewer agrees an agent's autonomy, and the AI lead is consulted."
- **Proof on screen:** "The engine accepted the decision. 1 request that is no longer needed was canceled."
- **Reset:** a fresh store.

### S-08.6 A security review before the pilot

- **Persona:** Noor Safadi.
- **Page and clicks:** Reviews, Due, Review type "Security review" (`/app/reviews?state=due&type=security-review`).
- **Say:** "The AI lead asked for a security review of Customer Feedback Bot before its pilot. It is Noor's to do."
- **Proof on screen:** "Due reviews" with the count 4, among them Security review, "Asked for by a person", Customer
  Feedback Bot, Security owner Noor Safadi, "Yours".
- **Reset:** nothing.
