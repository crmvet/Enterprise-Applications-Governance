# S-14 Platform admin: run the product

| | |
|---|---|
| Persona | Owen Achterberg, platform admin |
| Use cases | A-ADMIN, UC-08 |
| Screens | Admin |
| Time | 15 seconds in the 15-minute run, 3 minutes in full |
| End-to-end spec | `src/tests/e2e/specs/S-14-admin.spec.ts` |
| Story section | W9 Run it and measure it |
| Ready check | `demo check --base <url> --scenario S-14` |

**Before it starts.** Act as Owen Achterberg. The director ran the daily check before the demo, as the timer would.

**Reset.** Nothing to reset: a second daily check the same day changes and sends nothing.

## Steps

### S-14.1 The product at a glance

- **Persona:** Owen Achterberg, platform admin.
- **Page and clicks:** More, Govern, Admin (`/app/admin`).
- **Say:** "Run the product: the rules in force, the clients that call it, what the daily check sent, and the approval
  outbox."
- **Proof on screen:** the figures Configuration (the version in force), Open drafts, Clients that called 2, Refused
  calls, Notices sent, and "Outbox needs a look" 0.
- **Reset:** nothing.

### S-14.2 What the daily check did

- **Persona:** Owen Achterberg.
- **Page and clicks:** "The daily check", RECENT RUNS and SENT IN THE LAST 7 DAYS.
- **Say:** "The daily check opens reviews on their due day, marks documents stale, expires old requests and sends the
  reminders, each with who was told and how."
- **Proof on screen:** a run "run by Owen Achterberg: 1 review opened, 4 reviews marked overdue", "Opened: Access
  check: Identity Directory Sync". Notices such as "Access check: Identity Directory Sync ... To Elif Dunmore, the
  business owner, by teams, Overdue" and flag notices on the flagged deployments.
- **Reset:** nothing.

### S-14.3 Run it now, twice

- **Persona:** Owen Achterberg.
- **Page and clicks:** "Run the daily check now", "Check what it would do", then "Run it now", and "Run it now" again
  on "Run the daily check now?".
- **Say:** "A second run on the same day changes nothing and sends nothing."
- **Proof on screen:** the check: "Checked: nothing was written yet. ... Nothing was due. A rerun today changes
  nothing." The run: "The due-state refresh ran: nothing was due today."
- **Reset:** nothing.

### S-14.4 Clients and the approval outbox

- **Persona:** Owen Achterberg.
- **Page and clicks:** Clients, then Approval outbox.
- **Say:** "Which clients wrote or were refused, and the cards the approval runner sends. A person still answers on the
  decision page."
- **Proof on screen:** Claude Code, "AI client", its calls, refused calls and drafts, "Sanctioned"; Web app, "The
  product's own web app, where people work". The outbox states Failed 0, Expired 0, Waiting to send, Answered,
  Canceled, and "No request needs a look: nothing failed, and nothing waits too long."
- **Reset:** nothing.

### S-14.5 Host, engine and the Tool Policy

- **Persona:** Owen Achterberg.
- **Page and clicks:** "Host and engine", then "Tool Policy" (102).
- **Say:** "Only settings that are safe to show. The Tool Policy says who may call each tool, and how far an AI client
  may go."
- **Proof on screen:** "The product never shows a key, a connection string or a tenant." ENVIRONMENT, DAILY CHECK,
  STORE, ENGINE VERSION. "Tool Policy" with the count 102, grouped SESSION, PORTFOLIO, INTAKE, DECISIONS, CHANGE,
  DEPLOYMENT, DOCUMENTATION, GAP CHECK, CONFIGURATION, LINKS and DATA.
- **Reset:** nothing.
