# D-40 Self-approval stays as the Balanced preset has it

- **Status:** Accepted
- **Date:** 2026-11-23
- **Decided by:** the product owner

## Context

Separation of duties is configuration (D-07). A separation rule says who the person answering must differ from, and each preset ships its own rules. Balanced, the default preset, refuses self-approval at tier 3, and it flags a deployer who is the only approver of a production change. The Regulated preset and the strict separation overlay refuse self-approval at every tier.

With D-36 and D-38, people answer approvals in the web app, so the demo shows the rule at work: a person who proposed a tier 1 or tier 2 decision may answer it. The question was whether the default should stay that way, or become stricter.

## Options considered

- **Keep Balanced as it is.** A proposer may approve their own decision at tiers 1 and 2, for the decision types Balanced allows. Nobody approves their own proposal at tier 3.
  - Pros: low-risk work does not wait for a second person. A small team can work with the default.
  - Cons: at tiers 1 and 2, one person may both propose and approve.
- **Refuse self-approval at every tier in Balanced.**
  - Pros: two people on every decision.
  - Cons: every low-risk decision waits for a second person. That moves toward one approval board for every change, one of the old paradigms the playbook names (D-28).

## Decision

Keep Balanced as it is. The stricter rule stays available: the Regulated preset and the strict separation overlay refuse self-approval at every tier, and they need a second configuration admin to accept a configuration change.

## Why

- Risk tiers exist so that the checks match the risk (D-06). At tier 3 two people decide. Lower tiers keep moving.
- An organization that wants two people on every decision selects Regulated, or adds the strict separation overlay. Nothing in the code changes (D-07).

## Consequences

- No separation rule changed. Balanced keeps its two rules: no self-approval at tier 3 (refuse), and a deployer who is the only approver of a production change (flag).
- **The decision page says so.** It tells a proposer when the configuration in force lets them answer their own decision. When a separation rule keeps them out, the page names the rule and says that another person who is asked answers it. The page asks the engine's approval answer check first, so the page and the engine never disagree.
- Later in the build, tier and ownership decisions came to require approval too. At tier 3, the rule that nobody approves their own proposal now covers them as well, as it covers every decision type that requires approval. This decision still holds.
- **One exception.** A proposed owner's own yes is consent to take the role, not a check on the proposal, so no separation rule applies to it. A person who proposed a tier 3 ownership decision that names them as the new owner may still give that consent on their own named request. The business owner's or sponsor's approval carries the check.
- Engine fixtures prove both sides: self-approval is allowed at tier 2 and refused at tier 3. Two more fixtures show the exception: [src/fixtures/approval-answer/proposed-owner-who-proposed-it-allowed-at-tier-3.json](../../src/fixtures/approval-answer/proposed-owner-who-proposed-it-allowed-at-tier-3.json) and [src/fixtures/op-03/proposed-owner-consent-counts-at-tier-3.json](../../src/fixtures/op-03/proposed-owner-consent-counts-at-tier-3.json).

## Related

- [D-06 Risk tiers are configuration, never hardcoded](D-06-risk-tiers-are-configuration.md)
- [D-07 Decision rights are configuration, with presets to choose from](D-07-decision-rights-are-configuration-with-presets.md)
- [D-28 Govern AI development of the apps](D-28-govern-ai-development-of-the-apps.md)
- [D-36 The web app acts, and the engine checks every write](D-36-the-web-app-acts-and-the-engine-checks-every-write.md)
- [D-38 A person answers an approval on the decision page](D-38-a-person-answers-an-approval-on-the-decision-page.md)
- [Sign-in and security](../architecture/sign-in-and-security.md), section "Separation of duties"
- [Configuration and presets](../architecture/configuration-and-presets.md)
- Balanced's separation rules: [src/config/presets/balanced/separation-rule.json](../../src/config/presets/balanced/separation-rule.json)
- The strict separation overlay: [src/config/presets/strict-separation/](../../src/config/presets/strict-separation/)
- The fixtures: [src/fixtures/approval-answer/](../../src/fixtures/approval-answer/)
