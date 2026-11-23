# D-20 The AI lead role ships in the default configuration, configurable for each organization

- **Status:** Accepted. Answers D-14.
- **Date:** 2026-11-21
- **Decided by:** the product owner

## Context

D-14 left one question open: should the default configuration ship an AI lead role? AI is one of the parties the product brings together (D-01), but AI never holds an accountability. Every agent has a human owner. Someone still has to keep the AI guidelines, be consulted when an agent's autonomy changes, and chase agents without an owner. Microsoft's agent registry already lists agents that have no owner.

## Options considered

- **A. Its own role, which can be the same person.** The default configuration ships an AI lead role mapped to its own Entra group. A small organization puts the platform admin in that group too. A large one names a different person.
  - Pros: duties stay clear when people change. Reviews about agents have a named target.
  - Cons: one more role to explain and fill. In a small team it is a second label on one person.
- **B. The platform admin holds it.** No AI lead in the default configuration. The platform admin's role covers agents and AI-built apps.
  - Pros: fewer roles. Fits small teams.
  - Cons: the AI party has no named person by default. Each organization that wants one must add the role and its decision rights itself.
- **C. Its own role, always a different person.** As A, and the default configuration says the AI lead must not be the platform admin.
  - Pros: running the platform and judging AI risk stay with different people.
  - Cons: too heavy for small teams, which may not have two people for it.

## Decision

Option A, as recommended, with one condition from the product owner: it must be configurable for each organization.

- **The default configuration ships an AI lead role:** a person and a deputy, with its own Entra group (D-11).
- **The AI lead's duties:** keep the AI guidelines, be consulted when an agent's autonomy level is set or an agent starts acting on its own, and chase agents without an owner.
- **Configurable for each organization,** like every role (D-06, D-07, D-10). An organization can:
  - map it to the same Entra group as the platform admin, for a small team;
  - require a different person, for strict separation;
  - rename it, or drop it;
  - change which decisions it approves and which it is only consulted on.
- **The presets (D-07) set the starting point.** The organization adjusts from there.

## Why

- The AI party gets a named person by default.
- Small teams can give the role to someone who already holds another role. This follows the playbook's rule that one person may hold several roles while the roles stay apart.
- Nothing is hardcoded (D-06), so an organization that does not want the role can rename it or drop it.

## Consequences

- The default decision rights consult the AI lead when an agent's autonomy level is set. The AI lead records the decision on agents that Microsoft flags: keep, reassign, consolidate or retire.
- The strict separation overlay keeps the AI lead and the platform admin in different groups.
- After D-26, the role is an Entra app role assigned to its group, not a Dataverse team.
- This closes D-14.

## Related

- [D-01](D-01-what-the-product-is.md), [D-06](D-06-risk-tiers-are-configuration.md), [D-07](D-07-decision-rights-are-configuration-with-presets.md), [D-10](D-10-rules-are-configuration-and-the-engine-is-code.md), [D-11](D-11-roles-only-through-entra-groups.md), [D-14](D-14-ai-lead-role-open-question.md), [D-26](D-26-first-release-runs-in-the-clients-azure.md)
- [Sign-in and security](../architecture/sign-in-and-security.md)
- [Configuration and presets](../architecture/configuration-and-presets.md)
- [P02 Tier assessment](../architecture/processes/p02-tier-assessment.md)
- [P06 Gap check and claim](../architecture/processes/p06-gap-check-and-claim.md)
- The presets: [src/config/presets/](../../src/config/presets/README.md)
