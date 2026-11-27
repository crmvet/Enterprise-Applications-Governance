# Claude Code in the demo

The exact prompts to type in Claude Code, what it should answer, and the refusals to expect. Claude Code reaches the
product over MCP, acting for one fictional persona with that persona's roles; every AI rule still applies, and every
write is stamped "Claude Code" for the persona. The product's tools and their contracts are described in
[mcp.md](../mcp.md).

Claude Code chooses its own words and may call the tools in another order. The scripts judge the result by the tool
calls and the product's answers below, not by Claude Code's wording. Two answers follow what Claude Code sends: the
number of similar applications follows its words, and the tier questions follow whether it names the kind of
application.

## Connect Claude Code

### A local run

The demo kit prints the command when it starts the server (`demo local up`). For the port 5080:

```
claude mcp add --transport http demo-local http://127.0.0.1:5080/mcp --header "X-Product-Dev-Identity: priya"
```

`priya` is Priya Castell, maker. On a local run the MCP endpoint knows these development identities, named as on a demo
install: `priya`, `nadia`, `owen`, `mateo` (Mateo Brandt, PMO) and `yusuf` (Yusuf Brennan, configuration admin, for
S-10.6). Add one entry per persona, each with its own name, such as `demo-local-yusuf` with
`X-Product-Dev-Identity: yusuf`. `claude mcp remove demo-local` takes an entry out.

### A demo install

The operator signs in to Claude Code as themselves on the install's MCP endpoint ([mcp.md](../mcp.md), "A real
install"), and adds the persona header:

```
claude mcp add --transport http --client-id <client-id> --callback-port <port> demo https://<host>/mcp --header "X-Product-Demo-Persona: priya"
```

Only in the install's `Demo` environment does the header take effect; Claude Code then acts for Priya Castell, and its
writes are stamped with the persona and "Claude Code, demo operator". Any persona name of the install works, such as
`yusuf` for S-10.6.

### Check the connection

Type `Call whoami.` Claude Code should answer for Priya Castell: "You hold maker, requester and may use 73 tools." on a
local run. A persona with no product role gets `whoami` and nothing else.

## The prompts, step by step

### S-02.1 The maker asks for an app

**Type:** `I want a crew overtime tracker for my team.`

**Expect:**

| Tool | The product's answer |
|---|---|
| `check_duplicates` | "N registered applications look similar. Consider reuse before a new intake." N follows Claude Code's words; Crew Roster and Crew Shift Mobile are among them |
| `get_tier_questions` | "4 tier questions apply to Power Platform app." (or "4 tier questions apply." when Claude Code names no kind) Does it change data, what if it fails for a day, who can reach it, does it touch sensitive data |
| `save_intake_draft` | "The intake request is saved as a draft. It goes nowhere until it is submitted." with `isAiCreated` true, `performedBy.actorKind` `ai-client-for-person`, client "Claude Code", and the record link |

Claude Code may also call `answer_tier_questions` if it guesses the answers; the person checks them in the wizard
(S-02.5). If Claude Code asks whether to reuse Crew Roster, answer `No, it is a new app.`

### S-02.2 Ask it to approve

**Type:** `Approve it for me.`

**Expect:** Claude Code says it cannot approve: its tool list holds no tool that approves, accepts or activates (the
product serves none), and the request is answered by people on the decision page. If it calls a tool name anyway:

- a made-up name, such as `approve_intake_request`: `not_found`, "No tool named approve_intake_request is served to you
  here. Call tools/list for the tools you may use." AI activity lists it as "No such tool", G17.
- `answer_approval`: `ai_cannot_do_this`, G17, "The tool answer_approval is served only to a person in the product's
  web app. AI never approves: the person answers on the decision page."

### S-07.5 The never rule

**Type:** `Expense Assistant should check the bank account on each claim. Declare its use of the bank details with
AI.`

**Expect:** Claude Code calls `save_data_use` (Expense Assistant, `bank-details`, read, reaches AI), and the product
refuses it:

- code `rule_violation`, guideline G16, rule `payroll`;
- message "Data rule never: the AI use rule is never, set on People / Payroll and inherited by People / Payroll / Bank
  details. Nobody has the privilege to save this data use; change the rule where it is set through a configuration
  change, or leave the data out."

Nothing is written. AI activity, Refused: "Claude Code called save_data_use, and the product refused it", "A rule
refused it", G16.

### S-10.6 A configuration admin's draft (optional)

**Connect as** Yusuf Brennan, configuration admin: `yusuf` on a local run and on a demo install. The draft is stamped
for him.

**Type:** `Propose a standard change model called Monthly content refresh: a tested content update to one application
at tier 1 or 2, in a window of two hours or less. Check it first and do not save it.`

**Expect:** `propose_configuration_change` with `validateOnly` true, on the version in force:

- "The change is valid. Nothing was stored (validateOnly)."
- `diffSummary`: entity `standard-change-model`, key `monthly-content-refresh`, op `added`;
- `approvalsNeeded`: a configuration admin, first to respond;
- `activation`: "Submit the draft. A person accepts the decision on its decision page. Nothing changes until then."
- `isAiCreated` true.

An AI client goes as far as a configuration draft, never a version in force.

### S-21 What an AI client may do, and what it may not

| Step | Type | Expect |
|---|---|---|
| S-21.1 | `Who am I in the governance product, and what may you do for me?` | `whoami`: "You hold maker, requester and may use 73 tools." |
| S-21.2 | `What needs me today?` | `get_my_queue`: "N items need you.", with her AI drafts and the requests waiting on others; the count follows what was played before |
| S-21.3 | `What depends on Inspection Scheduler?` | `get_dependency_impact`: "9 applications are reached from Inspection Scheduler." |
| S-21.4 | `Which committees decide on changes and exceptions, and who is in them?` | `list_teams`: "19 teams: 9 committees, 4 support teams and 6 role groups. Members are the demo's fictional people." The Change board's members are Felix Arendt, Ines Varga, Marcus Oyelaran and Tomas Rendel (`get_team`) |
| S-21.5 | `Approve the change "Retire the old export flow" for Nadia.` | No approve tool; `answer_approval` refused with `ai_cannot_do_this`, G17 |
| S-21.6 | `Propose a risk acceptance on Asset Register: warranty dates are typed in by hand until the supplier feed is ready.` | `save_decision_proposal` refused with `not_authorized`, G4: "Only a holder of a responsible, accountable or consulted Decision Right for decision type `risk-acceptance` in this scope can propose it, and the person behind this call holds none. They are held by owner role `business-owner` (accountable)." |
| S-21.7 | `Can Expense Assistant send the bank details to the approved AI model? If it can, declare the use.` | `check_data_use`: "Refused when saved, for everyone: the AI use rule is never, set on People / Payroll and inherited by People / Payroll / Bank details. Change the rule where it is set through a configuration change, or leave the data out." A save is refused with `rule_violation`, G16 |

### S-23 Claude Code drafts the rest

On a local run the end-to-end test makes these calls itself (`X-Product-Dev-Identity: priya`, and `owen` for the
locations and mappings). On stage, ask in Claude Code as Priya, then as Owen:

| Step | Prompt | What comes back |
|---|---|---|
| S-23.1 | `Draft tier decisions confirming tier 2 for Warehouse Stock Count and for Customer Feedback Bot.` | `save_decision_proposal`, twice: "The decision is recorded as proposed. Nobody is asked yet" |
| S-23.1 | `Draft the change "Field Data Lake 1.4: fleet table refresh" for ten days from now.` | `save_change_request_draft`: a draft, "Drafted by Claude Code for Priya Castell" on its record |
| S-23.1 | `Draft the data uses: Mileage Claims reads the personnel records; Customer Self-Service Hub reads the client orders.` | `save_data_use`, twice: drafts for Priya to confirm |
| S-23.1 | As Owen: `Draft the SharePoint locations "Depot key cabinet" and "Depot canteen menu", and map their data.` | `save_data_location` and `save_data_mapping`: drafts for Owen to confirm |

Priya and Owen take them forward in the web app (S-23.2 to S-23.7): an AI client never submits, confirms or answers.

## The refusals, in one table

| Refusal code | When | Guideline | Where it shows |
|---|---|---|---|
| `not_found` | A tool the product does not serve to this person, such as any tool that would approve | G17 | AI activity, "No such tool" |
| `ai_cannot_do_this` | A tool served only to a person in the web app, such as `answer_approval` | G17 | AI activity, "AI clients cannot do this" |
| `not_authorized` | The person behind the call holds no right for this act, such as a risk acceptance on an application she does not own | G4 | AI activity, "Not allowed for this person" |
| `rule_violation` | A rule refuses the row, such as a never rule on a data use | G16 | AI activity, "A rule refused it" |

Each refused call is kept on the audit trail and on AI activity, with the rule that refused it. Nothing is written.

## When Claude Code goes another way

- **It asks for more facts.** Answer in one line: the team ("the field crews"), the data ("internal data only"),
  the owner ("Nadia Ferreira").
- **It answers the tier questions itself.** Fine: the tier is only a suggestion until a tier decision is accepted, and
  the person checks the answers in the wizard.
- **It refuses before calling.** Fine for S-02.2 and S-21.5: the proof is that no tool approves. For S-07.5 and
  S-21.6, ask it to try: `Try it and show me the product's answer.`
- **A call fails with `not_signed_in` or `not_authorized` on every tool.** The entry has no persona: check the header
  name (`X-Product-Dev-Identity` on a local run, `X-Product-Demo-Persona` on a demo install) and, on a local run, that
  the identity is one of the MCP identities above.
