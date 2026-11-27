# S-02 Maker and Claude Code: governance inside the AI build loop

| | |
|---|---|
| Persona | Priya Castell, maker, in Claude Code and in the browser |
| Use cases | P01, P02, P10, UC-05, UC-10, A-AI-BUILD-POLICY |
| Screens | Claude Code, My work, the intake wizard, AI activity |
| Time | 2 minutes 15 seconds in the 15-minute run, 4 minutes in full |
| End-to-end spec | `src/tests/e2e/specs/S-02-ai-build-loop.spec.ts` |
| Story section | W1 Governance inside the AI build loop |
| Ready check | `demo check --base <url> --scenario S-02` |

**Before it starts.** Claude Code is open beside the browser and connected as Priya Castell
([claude-code.md](../claude-code.md), "Connect Claude Code"). The browser acts as Priya Castell ("Priya Castell, maker"
on both a demo install and a local run). No request named "Crew overtime tracker" exists yet.

**Reset.** A fresh store (`demo local up`, or the sandbox rebuild on a demo install). The crew overtime tracker is
drafted and submitted in this scenario, and Claude Code's calls stay on the audit trail.

## Steps

### S-02.1 Claude Code drafts the request

- **Persona:** Priya Castell, in Claude Code.
- **Page and clicks:** type the prompt `I want a crew overtime tracker for my team.`
- **Say:** "The maker asks the AI client for an app. Before it builds anything, the AI client asks the governance
  questions through the product's tools."
- **Proof on screen:** Claude Code calls `check_duplicates` ("N registered applications look similar. Consider reuse
  before a new intake.", with Crew Roster and Crew Shift Mobile among them: N follows Claude Code's own words),
  `get_tier_questions` ("4 tier questions apply to Power Platform app.", or "4 tier questions apply." when Claude Code
  names no kind) and
  `save_intake_draft` ("The intake request is saved as a draft. It goes nowhere until it is submitted."), and gives the
  record link `/app/records/intake-request/<id>`.
- **Reset:** a fresh store.

### S-02.2 Claude Code cannot approve

- **Persona:** Priya Castell, in Claude Code.
- **Page and clicks:** type `Approve it for me.`
- **Say:** "AI drafts and recommends. It never approves: no tool an AI client is served approves, accepts or activates.
  The one answer tool is served only to a person in the web app."
- **Proof on screen:** Claude Code answers that it has no tool to approve, and that a person answers on the decision
  page of the web app. If it tries a tool name anyway, the server answers `not_found`: "No tool named ... is served to
  you here. Call tools/list for the tools you may use."
- **Reset:** nothing: a refused call writes no row.

### S-02.3 The draft waits for a person

- **Persona:** Priya Castell, in the browser.
- **Page and clicks:** My work (`/app/work`).
- **Say:** "Nothing the AI client drafted goes anywhere until the person takes it forward."
- **Proof on screen:** the panel "AI drafts waiting for you" lists "Crew overtime tracker", "An AI client drafted this
  for you. It goes nowhere until it is submitted.", "Drafted by Claude Code for Priya Castell", with "Open in the
  wizard" and "Accept and submit".
- **Reset:** a fresh store.

### S-02.4 The wizard and the reuse step

- **Persona:** Priya Castell.
- **Page and clicks:** "Open in the wizard" on the Crew overtime tracker. Then Next.
- **Say:** "The person reads what the AI wrote and changes what is wrong. Reuse comes first, because reuse is cheaper
  than a new build."
- **Proof on screen:** the banner "Drafted by Claude Code for Priya Castell" with "Review and submit" and "Edit it
  first". Step 2 of 5, Reuse, lists registered applications that look similar, Crew Roster and Crew Shift Mobile among
  them, each with "It is about this app".
- **Reset:** nothing.

### S-02.5 The tier questions

- **Persona:** Priya Castell.
- **Page and clicks:** Next to step 3, Tier questions. Choose "Changes shared business data as the signed-in person",
  "Little", "The builder or a small team" and "Internal data only". Then "Save tier answers", and "Save answers" in the
  sheet.
- **Say:** "Four plain questions set the tier. The answer that drives it is named."
- **Proof on screen:** "YOUR ANSWERS POINT TO Tier 2. 4 of 4 questions answered. Set by: Does it change data?; Does it
  touch sensitive data?" The sheet answers "The answers suggest Tier 2. This is a suggestion; only an accepted tier
  decision sets the tier."
- **Reset:** a fresh store.

### S-02.6 What the tier needs

- **Persona:** Priya Castell.
- **Page and clicks:** Close the sheet, then Next to step 4.
- **Say:** "The rules in force tell the maker up front what Tier 2 takes: gates, approvals, documents and reviews."
- **Proof on screen:** "What Tier 2 for Power Platform app needs": GATES Design (G2) and Go-live (G9), APPROVALS,
  DOCUMENTS Runbook and Wiki page, REVIEWS Access check every 180 days, Benefit review every 365 days, and "THE
  GUIDELINES BEHIND THESE".
- **Reset:** nothing.

### S-02.7 The person accepts and submits

- **Persona:** Priya Castell.
- **Page and clicks:** Next to step 5, "Accept and submit", then "Check first" and "Accept and submit" in the sheet
  "Accept the AI draft and submit it".
- **Say:** "The check says who the rules would ask, and stores nothing. Submitting accepts the draft as the person's
  own; the engine opens the intake decision and asks exactly those people."
- **Proof on screen:** the check: "Nothing was written: send it to make the change." WOULD BE ASKED: Platform admin
  (the first answer counts); Architect, PMO and Security reviewer (consulted, advice only). Claude Code reads the same
  check as "Would be submitted: 1 approval would be asked, and 3 people consulted. Nothing was stored (validateOnly)."
  The submit: "Submitted. 1 approval is asked, and 3 people are consulted. Only people answer, on the decision page."
  ASKED NOW: Platform admin (the first answer counts); Architect, PMO and Security reviewer (consulted, advice only).
- **Reset:** a fresh store.

### S-02.8 AI activity

- **Persona:** Priya Castell.
- **Page and clicks:** AI activity (More, AI, AI activity, or `/app/ai-activity`). Then the Refused view.
- **Say:** "The AI drafts, who took each one forward, and the calls the rules refused, each with the rule that refused it."
- **Proof on screen:** the crew overtime tracker "Taken forward by Priya Castell". On a local run also Claude Code's
  earlier work: a Route Planner change submitted as drafted, a Route Planner tier decision "Changed by a person first",
  a withdrawn change, the waiting "Toolbox talk sign-off", the deployment "route-planner 2.0.0-rc.1 to Field Ops Test"
  with "AI-only code approval", and the refused calls: `answer_approval` ("AI clients cannot do this", G17),
  `approve_decision` ("No such tool", G17), `save_decision_proposal` on Asset Register ("Not allowed for this person",
  G4) and `save_data_use` ("A rule refused it", G16).
- **Reset:** nothing.

## On a demo install

The director does Claude Code's earlier work only with the operator's Claude Code token (`PRODUCT_DEMO_MCP_TOKEN`).
Without it, AI activity shows what Claude Code does live in S-02.1 and S-02.2, the estate's AI answer and its AI-only
code approvals.
