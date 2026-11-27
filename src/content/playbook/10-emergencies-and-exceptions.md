# Chapter 10. Emergencies and exceptions

- **Version:** 0.2 draft, 2026-11-22
- **Track:** Organization
- **Parties:** change control (the change manager), IT, the business owner, security
- **Guidelines:** [G4](../guidelines/G04-record-each-decision.md), [G12](../guidelines/G12-approve-production-changes-on-record.md), [G13](../guidelines/G13-keep-an-emergency-path.md)
- **Processes:** [P03](processes.md#p03-change-management), [P05](processes.md#p05-deployment-record)

## Why this chapter exists

"Urgent fixes have no written fast path, and no record." Under pressure, teams skip steps, and afterwards nobody can say who agreed to what. A path agreed in advance keeps the speed without losing control or the record. The same goes for exceptions: a rule that cannot be followed should be departed from on record, not quietly.

## What good looks like

- The emergency path is written down before it is needed: who approves, what may be shortened, what must still run.
- Every emergency change is approved by a named person at the time, tested, deployed through the same pipeline, and reviewed afterwards.
- Every exception, such as a small team approving its own change, is a recorded decision with a reason and a review date.
- Every failed change and every emergency gets a review, without exception.

## The emergency path (default)

1. **Use it only when it fits.** A production fault or a security exposure that cannot wait for the normal path.
2. **Get the yes, by name.** The change manager authorizes the emergency at once, on the decision page. There is no separate emergency approver role in the default configuration; the change manager's approval rule for the emergency type is the answer to "who may approve acceleration".
3. **Cut the narrowest switch first.** When an app reaches more than it should, disable the app's identity or remove a role, record what it held, then fix normally.
4. **Still test, still use the pipeline.** Run the shortest test that proves the fix, then deploy a new, higher version through the same pipeline. Anything done by hand is recorded and flagged as such.
5. **Record at the time.** The emergency change is recorded when it happens. The record may be completed after the deployment, within the follow-up time your configuration sets.
6. **Review afterwards.** The product is designed to open the review automatically, due within the agreed days. The review records the cause, runs any skipped tests, and names any rule that would have stopped the fault.

## Exceptions

- **Self-approval in a small team.** Where nobody else can approve, the person records an exception decision, and the change is reviewed afterwards. Separation returns as soon as a second person exists.
- **Departing from a standard.** An exception decision names the standard, the reason and the date it will be looked at again (chapter 7).
- **A deployment that came another way.** The change manager either opens a change after the fact or records an exception. The original deployment record stays; the correction is a new record.
- **Every exception is a decision.** It can be superseded later, never edited.

## Who decides (default decision rights)

| Decision | Accountable | Does the work | Consulted | AI may |
|---|---|---|---|---|
| Approve an emergency change | Change manager | Platform admin | Business owner, security reviewer | Nothing |
| Review after an emergency or failed change | Change manager | Platform admin | Builder, business owner, security reviewer | Draft the review |
| Follow up a flagged deployment | Change manager | Platform admin | Builder, technical owner, business owner | Draft the follow-up |
| Record a self-approval exception | The change's accountable role | Requester | Change manager | Draft the reason |

## Guidelines this chapter applies

- **G13 Keep an emergency path that still tests and still records.** The path itself.
- **G12 Approve every production change on record.** Emergencies are still approved, by name, and a small team's self-approval is a recorded exception.
- **G4 Record each decision in one line.** Every emergency approval and every exception is an entry in the decision log.

## Processes this chapter uses

- **P03 Change management.** The emergency type, its authorization, the follow-up time and the review after the change.
- **P05 Deployment record.** Records the emergency deployment, holds back the missing-approval flag until the follow-up time runs out, and flags manual deployments.

## How the product is designed to support it

- It is designed to treat "emergency" as a change type in configuration, with its own approval rule, the right to authorize after the deployment, a follow-up time and a review.
- It is designed to record the emergency with its approver at the time, and to schedule the review after it.
- It is designed to flag a deployment that lacks its approval once the follow-up time has passed.
- It is designed to keep every exception as a decision with a reason, which can be superseded but never edited.

## What you can tailor

- Who approves emergencies, per tier, and whether a top-tier emergency has its own rule.
- How long the follow-up may take, and when the review is due.
- Whether self-approval is allowed with a recorded exception, refused at the top tier, or refused at every tier.

## Microsoft guidance it applies

- [Recommendations for safe deployment practices](https://learn.microsoft.com/power-platform/well-architected/operational-excellence/safe-deployments#key-design-strategies) (Power Platform Well-Architected, OE:10), checked 2026-11-22: "Make sure that you define who can approve safe deployment acceleration in an emergency and the criteria that must be met for acceleration to be approved."
- [Recommendations for designing a deployment failure mitigation strategy](https://learn.microsoft.com/power-platform/well-architected/operational-excellence/mitigation-strategy) (Power Platform Well-Architected, OE:11), checked 2026-11-22: "Like any other code, hotfixes need to go through your safe deployment practices." "Postmortems should follow all failed deployments without exception."

## How AI changes this

- An AI can produce a fix in minutes. That tempts a manual push straight into production, around the approver and the pipeline.
- The emergency path keeps both. A named person approves on the decision page, and the fix goes through the same pipeline as a new, higher version.
- An AI-made fix is still tested before it ships, with the shortest test that proves it.
- The AI drafts the review afterwards, from the change and deployment records. A person completes it and records the outcome. No AI may approve an emergency.

## Old paradigm to avoid

[1. One board approves every change](17-old-paradigms-and-how-not-to-slide-back.md#1-one-board-approves-every-change). Here it shows up as emergencies becoming the way around a slow board: when the normal path waits a week, routine changes get labeled urgent. Fix the normal path (chapter 9) instead of widening the emergency path.

**Slide-back signal:** the share of emergency changes rises, or reviews find emergencies that could have waited.

## Done when

- The emergency path is written, with named approvers and criteria.
- Every emergency change has an approver, a test result and a completed review.
- Every exception is a recorded decision with a reason.
