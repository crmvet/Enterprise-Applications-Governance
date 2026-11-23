# G13. Keep an emergency path that still tests and still records

- **Number:** G13
- **Version:** 1.0, 2026-11-22
- **Status:** active
- **Home chapter:** 10 (Emergencies and exceptions)
- **Cited in chapters:** 1, 9, 10
- **Processes:** P03, P05

**Rule.** Define the emergency change path in advance: who may approve it, what may be shortened, and what must still run. Every emergency change is tested, recorded at the time and reviewed afterwards.

**Why it matters.** Under pressure, teams skip steps. A path agreed in advance keeps the speed without losing control or the record.

**How the product is designed to support it.** It is designed to treat "emergency" as a change type in configuration, record the exception with its approver, and schedule the follow-up review.

## Microsoft anchors

- [Recommendations for safe deployment practices](https://learn.microsoft.com/power-platform/well-architected/operational-excellence/safe-deployments#key-design-strategies) (Power Platform Well-Architected, OE:10), checked 2026-11-22: "Make sure that you define who can approve safe deployment acceleration in an emergency and the criteria that must be met for acceleration to be approved." Gates "should still be run as quickly as possible as an out-of-band exercise."
- [Recommendations for designing a deployment failure mitigation strategy](https://learn.microsoft.com/power-platform/well-architected/operational-excellence/mitigation-strategy) (Power Platform Well-Architected, OE:11), checked 2026-11-22: "Like any other code, hotfixes need to go through your safe deployment practices." "Postmortems should follow all failed deployments without exception."

## Maps to

AICPA Trust Services Criteria CC8.1 (change management). ISO/IEC 27001:2022 control 8.32, Change management.

## Working shape

- **When:** when a production fault or security issue cannot wait for the normal path.
- **Who:** the named emergency approvers, the technical owner and the change manager.
- **Steps:**
  1. Name in advance who can approve an emergency change, and the criteria for using the path.
  2. Run the shortest test that proves the fix, then deploy through the same pipeline (G9).
  3. Record the emergency change and its approver at the time, not afterwards.
  4. Hold the review within an agreed time, and run any tests that were skipped.
- **Done when:** every emergency change has an approver, a test result and a completed review.
- **Evidence:** the emergency change record, the test results and the review notes.
