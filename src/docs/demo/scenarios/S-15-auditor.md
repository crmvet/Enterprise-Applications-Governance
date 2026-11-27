# S-15 Auditor: answer from the record

| | |
|---|---|
| Persona | Bianca Ferro, auditor |
| Use cases | UC-09, A-EVIDENCE, UC-11, P10 |
| Screens | Evidence pack, Audit trail |
| Time | 1 minute in the 15-minute run, 3 minutes in full |
| End-to-end spec | `src/tests/e2e/specs/S-15-auditor.spec.ts` |
| Story section | W10 Answer the auditor |
| Ready check | `demo check --base <url> --scenario S-15` |

**Before it starts.** Act as Bianca Ferro ("Bianca Ferro, auditor"). She reads everything and changes nothing.

**Reset.** Nothing to reset: the scenario only reads.

## Steps

### S-15.1 The evidence pack

- **Persona:** Bianca Ferro, auditor.
- **Page and clicks:** Invoice Matching's evidence pack
  (`/app/evidence/business-application/00000000-0000-4000-8000-000000001306`).
- **Say:** "The auditor asks for proof, not assurances. Every decision with each answer, the changes, the deployments
  with their flags, the reviews and the documents, each with the rules version it was checked under."
- **Proof on screen:** the figures Decisions 5 ("2 approval answers"), Changes 2, Deployments 2 ("2 flagged"),
  Reviews 1, Documents 2, Evidence links 0, and "Print the pack".
- **Reset:** nothing.

### S-15.2 Who answered, and whose answer counted

- **Persona:** Bianca Ferro.
- **Page and clicks:** "Decisions and approval answers", the decision on "Invoice Matching 1.12" in the November 14
  window.
- **Say:** "An agent's answer is stored and shown, and it never counts. The business owner's answer counted."
- **Proof on screen:** "Finance Approvals Agent, asked as Business owner, Recommendation only. A recommendation only: an
  AI answer never counts. Agent identity." and "Clara Voss, asked as Business owner, Approved, Counted toward the
  approval".
- **Reset:** nothing.

### S-15.3 Deployments, flags and the rules version

- **Persona:** Bianca Ferro.
- **Page and clicks:** "Deployments" and "Configuration versions" in the pack.
- **Say:** "Both releases carry the flag that only an AI approved their code. Each row says which rules version
  judged it."
- **Proof on screen:** "invoice-matching 1.12.1, To Finance Prod ... Missing approval, AI-only code approval" and
  "invoice-matching 1.12.0 ... AI-only code approval". Each row: "Checked under version 1". After S-10 the pack adds
  "Version 2 is in force now, so one older version applies to some rows."
- **Reset:** nothing.

### S-15.4 The audit trail of AI calls

- **Persona:** Bianca Ferro.
- **Page and clicks:** the Audit trail with the Actor "AI client for a person" chosen
  (`/app/audit?actor=ai-client-for-person`). The pack's own "Audit trail" link opens the trail of this application.
- **Say:** "Every call that wrote or was refused leaves one entry: who, through which client, for whom, and the rule."
- **Proof on screen:** the figures Entries, Refused calls, By AI clients. Entries such as "Refused the call to answer
  an approval request ... By Priya Castell through Claude Code", "The rule: AI never approves", G17, and "Refused the
  call to save a data use", "For the audit: code rule_violation, reason data.rule-never". Each entry offers "Everything
  for Priya Castell" and "Everything through Claude Code".
- **Reset:** nothing.

## On a demo install

The audit entries of S-15.4 are the calls Claude Code made live in S-02 and S-07.5, plus the director's when it ran
with the operator's Claude Code token. Invoice Matching's pack is the same on every install.
