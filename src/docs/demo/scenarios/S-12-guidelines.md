# S-12 Maker: guidelines and the playbook

| | |
|---|---|
| Persona | Priya Castell, maker |
| Use cases | UC-11, A-AI-BUILD-POLICY |
| Screens | AI activity (Refused), Guidelines and playbook |
| Time | 15 seconds in the 15-minute run, 2 minutes in full |
| End-to-end spec | `src/tests/e2e/specs/S-12-guidelines.spec.ts` |
| Story section | W1 Governance inside the AI build loop |
| Ready check | `demo check --base <url> --scenario S-12` |

**Before it starts.** Act as Priya Castell. AI activity holds at least one refused call: on a local run the
director's `answer_approval` attempt carries G17. On a demo install without Claude Code's seeded work, use the G16
chip of the refusal from S-07.5, or open `/app/guidelines?g=G17` directly.

**Reset.** Nothing to reset: the scenario only reads.

## Steps

### S-12.1 From a refusal to its guideline

- **Persona:** Priya Castell, maker.
- **Page and clicks:** AI activity, the Refused view (`/app/ai-activity?view=refused`). Choose the chip "G17 Let AI
  draft and recommend; people approve" on a refused `answer_approval` call.
- **Say:** "Every refusal names the rule and the guideline behind it, so the maker learns why, not only no."
- **Proof on screen:** the chip opens `/app/guidelines?g=G17`. The page header "Guidelines and playbook", the figures In
  force 22, Retired 2, Playbook chapters 22.
- **Reset:** nothing.

### S-12.2 What G17 says and where it applies

- **Persona:** Priya Castell.
- **Page and clicks:** read G17 on the opened page.
- **Say:** "The guideline is applied by approval rules and gate requirements in the configuration in force."
- **Proof on screen:** "G17 Let AI draft and recommend; people approve. No AI agent or tool approves work, including
  its own." "Applied by 1 approval rule, 1 gate requirement. The engine names it when it refuses a call." APPROVAL
  RULES "AI lead decides on a flagged agent"; GATE REQUIREMENTS "Autonomy level agreed"; its playbook chapters, among
  them "chapter 21, Governance inside the AI build loop".
- **Reset:** nothing.

### S-12.3 Owners and data, G2

- **Persona:** Priya Castell.
- **Page and clicks:** scroll to G2, or open `/app/guidelines?g=G2`. Then "Playbook chapters".
- **Say:** "The playbook is in the product. Each chapter names the guidelines it applies."
- **Proof on screen:** "G2 Name the owners of every app, each with a deputy. Every application names a business owner
  and a technical owner, each with a deputy, and declares its data uses. Every data domain has a data owner and a
  deputy, who decides about the uses of its data." "Playbook chapters" with the count 22, by track, starting with
  "START HERE".
- **Reset:** nothing.
