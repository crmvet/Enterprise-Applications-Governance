# G4. Record each decision in one line: what, who, when, why

- **Number:** G4
- **Version:** 1.0, 2026-11-22
- **Status:** active
- **Home chapter:** 7 (Architecture review and decisions)
- **Cited in chapters:** 1, 2, 4, 6, 7, 10, 14, 15, 19
- **Processes:** P02, P03, P04, P09, P11

**Rule.** Record every significant decision about an app as a short entry: what was decided, by whom, when, and on what evidence. Never edit an accepted decision. Write a new one that supersedes it.

**Why it matters.** Decisions that are not written down get argued again, or quietly contradicted. An append-only log shows who said yes, to what, and under which rule.

**How the product is designed to support it.** It is designed to keep one append-only decision log shared by every party, with the status words Proposed, Accepted and Superseded, and decision types held as configuration.

## Microsoft anchors

- [Maintain an architecture decision record (ADR)](https://learn.microsoft.com/azure/well-architected/architect-role/architecture-decision-record), checked 2026-11-22: "The ADR serves as an append-only log." "Don't go back and edit accepted records." A record carries a status "such as Proposed, Accepted, or Superseded."

## Maps to

AICPA Trust Services Criteria CC8.1 (change management). ISO/IEC 27001:2022 control 8.32, Change management.

## Working shape

- **When:** whenever a gate is passed, a tier changes, an exception is granted, or a design choice would be hard to reverse.
- **Who:** the person who decides records or confirms the entry; anyone may propose one.
- **Steps:**
  1. Write the decision in one sentence, with its context and the options ruled out.
  2. Name the decider and the date, and link the evidence.
  3. If the decision changes later, add a new entry that supersedes the old one, and link the two.
- **Done when:** every gate, exception and tier change has an accepted decision entry.
- **Evidence:** the decision log, with each entry's status history.
