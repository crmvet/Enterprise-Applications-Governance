# S-07 Data: domains, a data-use card, the never rule and copies

| | |
|---|---|
| Personas | Hana Lindqvist, data governance lead; Kai Ostrowski, data owner; Noor Safadi, security reviewer; Priya Castell, maker; Owen Achterberg, platform admin |
| Use cases | P02-DATA, UC-13, P10, A-AI-BUILD-POLICY |
| Screens | Data (Domains, Uses, Mapping, Locations, Gaps), the Decision page, Application 360 (Data) |
| Time | 1 minute 30 seconds in the 15-minute run, 6 minutes in full |
| End-to-end spec | `src/tests/e2e/specs/S-07-data-taxonomy.spec.ts` |
| Story section | W5 Know the data |
| Ready check | `demo check --base <url> --scenario S-07` |

**Before it starts.** Act as Hana Lindqvist ("Hana Lindqvist, data governance lead"). Claude Code is connected as
Priya Castell for S-07.5.

**Reset.** A fresh store: Kai's and Noor's answers and Owen's declared use are made here. The refused uses write
nothing.

## Steps

### S-07.1 The domain tree

- **Persona:** Hana Lindqvist, data governance lead.
- **Page and clicks:** More, Register, Data (`/app/data`), the Domains tab.
- **Say:** "Business data in a tree: each domain with its classification, its data owner and deputy, and the rules for
  use, AI use and copies. A rule set higher up is inherited below."
- **Proof on screen:** the figures Domains 25, Locations 22, No data owner 2, Decisions missing 4, Breaking a rule 0,
  Copies outside production 2. Commercial reads "No data owner" with "Propose an owner". Payroll reads "Highly
  Confidential", "AI use: Never", "Copy: Never" and "No deputy"; Paychecks and Bank details read "from Payroll".
- **Reset:** nothing.

### S-07.2 The data owner's card

- **Persona:** switch to Kai Ostrowski, data owner.
- **Page and clicks:** My work. Answer on "Allow Visit Report Agent to send the open safety findings of a site to the
  approved AI model." Read "The data use it decides".
- **Say:** "Restricted data going to an AI model needs two answers: the domain's data owner and a security reviewer.
  The data steward is consulted."
- **Proof on screen:** "Data approvals asked of you" lists the card. On the Decision page: THE USE "DU-0004: Reads at
  Field Ops Prod", THE RULE "AI use: Decision", STATE "0 of 2 approvals", WHO IS ASKED Kai Ostrowski (Data owner), "Any
  security reviewer", Lena Okoro (Data steward, Consulted, advice only).
- **Reset:** nothing.

### S-07.3 The data owner approves

- **Persona:** Kai Ostrowski.
- **Page and clicks:** Approve with the reason "Only open findings of one site go to the approved model, and the
  inspector checks each draft." "Check my answer", Approve, Approve.
- **Say:** "His answer counts, and the decision still waits for the security reviewer."
- **Proof on screen:** "The decision stays proposed." After the page reads again, STATE "1 of 2 approvals".
- **Reset:** a fresh store.

### S-07.4 The security reviewer approves

- **Persona:** switch to Noor Safadi, security reviewer, on the same Decision page.
- **Page and clicks:** Approve with a typed reason.
- **Say:** "The second answer completes the rule. The use is allowed."
- **Proof on screen:** "The engine accepted the decision. 1 request that is no longer needed was canceled." STATE
  "Allowed: An accepted decision allows this use."
- **Reset:** a fresh store.

### S-07.5 Claude Code meets the never rule

- **Persona:** Priya Castell, in Claude Code.
- **Page and clicks:** type `Expense Assistant should check the bank account on each claim. Declare its use of the
  bank details with AI.`
- **Say:** "A never rule is refused when it is saved, for everyone, a person or an AI client. Nobody holds a privilege
  to save it."
- **Proof on screen:** Claude Code reports the refusal `rule_violation`: "Data rule never: the AI use rule is never,
  set on People / Payroll and inherited by People / Payroll / Bank details. Nobody has the privilege to save this data
  use; change the rule where it is set through a configuration change, or leave the data out." with guideline G16.
  AI activity, Refused, lists the call. On a local run the director made the same attempt before the demo.
- **Reset:** nothing: nothing was written.

### S-07.6 A person meets the same rule

- **Persona:** switch to Priya Castell, in the browser.
- **Page and clicks:** Data, the Uses tab, "Declare a use". Search "Expense", choose Expense Assistant. The data
  "People / Payroll / Bank details", Access Reads, Purpose "Checks the bank account on each expense claim.", tick "The
  data reaches an AI model or agent". Read "What the rules say", then "Declare the use".
- **Say:** "The sheet says before saving that this use cannot be saved, and the save is refused with the same words."
- **Proof on screen:** WHAT THE RULES SAY "Breaks a rule", "The AI use rule is Never, set on Payroll.", "This use
  cannot be saved". The save: "A rule refused this", the same "Data rule never ..." text, "The rule: Payroll", G16,
  "Nothing was written."
- **Reset:** nothing: nothing was written.

### S-07.7 An AI draft waits to be confirmed

- **Persona:** Priya Castell.
- **Page and clicks:** close the sheet; stay on the Uses tab.
- **Say:** "Claude Code may draft a data use for the person. It waits until the person confirms it."
- **Proof on screen:** the row Mileage Claims, "DU-0031, a draft", "Drafted by Claude Code for Priya Castell", People /
  Payroll / Paychecks, "Decision needed", with Confirm.
- **Reset:** nothing.

### S-07.8 Copies outside production

- **Persona:** switch to Hana Lindqvist.
- **Page and clicks:** Data, the Mapping tab (`/app/data/mapping`). Scroll to "Copies outside production".
- **Say:** "Where each domain's data sits, by stage. A copy of production data in test is judged by the copy rule."
- **Proof on screen:** "Copies outside production" with the count 2: Customers / Client accounts and contacts, "Copy
  of production at Customer Test (Test)", "Breaks a rule", with "Change the copy" and "End the mapping"; Commercial /
  Contracts / Supplier contracts, "Masked copy at Finance Test (Test)", "Waiting for approval".
- **Reset:** nothing.

### S-07.9 Locations

- **Persona:** Hana Lindqvist.
- **Page and clicks:** the Locations tab.
- **Say:** "Every store that holds data, with its technical owner and when it was last seen."
- **Proof on screen:** PAYR01, "Production. No technical owner", with PAYLIB inside it holding Bank details and
  Paychecks. TIMESQL01, "Technical owner Felix Arendt", "Not seen for" more than 200 days.
- **Reset:** nothing.

### S-07.10 Data gaps

- **Persona:** Hana Lindqvist.
- **Page and clicks:** the Gaps tab.
- **Say:** "Every finding names the one act that fixes it."
- **Proof on screen:** Severity All 31: High 10, Medium 7, Low 14. "DC-01 No owner" 2 (Commercial and Proposals and
  quotes, "Propose an owner"), "DC-03 No deputy" 2 (Wren Abernathy on Client orders, Greta Lund on Payroll), "DC-04
  Owner or deputy inactive" 3, "DC-05 Decision missing" 4, "DC-08 Copy outside production" 2, "DC-10 Undeclared data"
  13.
- **Reset:** nothing.

### S-07.11 Declare what Route Planner reads

- **Persona:** switch to Owen Achterberg, technical owner of Route Planner.
- **Page and clicks:** Route Planner's Application 360, Data section
  (`/app/records/business-application/00000000-0000-4000-8000-000000000512?tab=data`). Under "Data it reaches that no
  use declares", "Declare it". The data "Field operations / Inspections", Access Reads, Purpose "Reads the day's
  inspections to plan the crews' routes." "Check first", then "Declare the use".
- **Say:** "Route Planner reads data from Inspection Scheduler and declared none. Declaring it is one act, and the
  rules say whether a decision is needed."
- **Proof on screen:** before: "Route Planner reads or writes data of another application and declares no data use."
  WHAT THE RULES SAY "Not needed", "No decision is needed: the rule allows this use." After: "Data uses" with the count
  1, "Field operations / Inspections", a new DU number, "Reads", "Not needed"; "Data owners from its uses": Mateo
  Brandt.
- **Reset:** a fresh store.

## On a demo install

Mileage Claims' draft use (S-07.7) and Claude Code's draft mapping exist only when the director ran Claude Code's
part; then Mapping counts 29 and Uses 31, otherwise 28 and 30.
