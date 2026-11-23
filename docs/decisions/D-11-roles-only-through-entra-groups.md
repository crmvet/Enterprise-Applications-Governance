# D-11 Roles come only through Entra groups

- **Status:** Accepted. Supersedes in part D-02 ("teams for roles"). Superseded in part by D-26, which changes the mechanism to Entra app roles. The rule that roles come only through Entra groups stands.
- **Date:** 2026-11-21
- **Decided by:** the product owner

## Context

An earlier decision, D-02, said the product would use "teams for roles". The phrase could be read two ways:

- people get the product's security roles through teams. At the time the data store was planned as Dataverse, where a team can be backed by a Microsoft Entra group;
- Microsoft Teams, the chat app, is the place where people in each role work together and answer.

The design needed one meaning before the role model could be drawn.

## Options considered

- **A. Both readings.** Roles go to Dataverse group teams backed by Entra groups, one group per product role. Microsoft Teams is also a place where people meet the product.
  - Pros: access is managed in one place, Entra. It applies to the app and to every AI client alike, because AI clients act as the signed-in person.
  - Cons: membership changes need an Entra admin. The playbook must explain two meanings of "teams".
- **B. Group teams only.** Roles through Entra group teams. Microsoft Teams plays no part.
  - Pros: fewer moving parts.
  - Cons: people who live in Microsoft Teams must answer somewhere else.
- **C. Microsoft Teams only.** Roles are given to each user directly, and work happens in Microsoft Teams.
  - Pros: simple to explain.
  - Cons: the product's app could not run inside Microsoft Teams. Roles given person by person do not scale, and they drift when people move.

## Decision

The product owner answered that "teams for roles" means Entra group teams and their administration only. That keeps the first reading of option A and narrows it:

- People get product roles only through Entra groups. No role is ever given to a person directly, not even as a recorded exception.
- Administration happens in Entra. Who holds a role is managed through Entra group membership. Where an organization has Entra ID Governance, access reviews and entitlement management cover it too. The product records and shows which group carries which role. It does not manage membership.
- Microsoft Teams is not part of the role model. It stays only a channel for the optional Copilot agent and the approval cards.

## Why

- One membership change in Entra applies everywhere: to the app and to every AI client that acts for the person.
- Identity is something Microsoft already manages. The product does not rebuild what a Microsoft tool already does (D-01).
- Roles given person by person drift when people change jobs. Groups can be reviewed in one place.
- One meaning of "teams" keeps the playbook clear.

## Consequences

- The role model is configuration. A table maps each product role to its Entra group (D-06, D-10).
- A check is designed to flag any role given to a person directly.
- The organization creates one group per product role when it sets up the product.
- D-16 dropped the optional Copilot agent, so Microsoft Teams carries approval cards only.
- D-22 reopened the mechanism when it put the data store up for review. D-26 then chose Azure for the first release. There, each product role is an Entra app role on the product's API, assigned to Entra groups. Assigning groups to app roles needs Entra ID P1. Reading group membership through Microsoft Graph is the alternative. The rule itself stands: roles come only from Entra groups, administered in Entra.
- Pipelines are not people. Whether a pipeline's workload identity may hold the deployment recorder role directly, as a recorded and expiring grant, is an open design question. People still get roles only through groups.

## Related

- [D-01](D-01-what-the-product-is.md), [D-02](D-02-how-people-and-ai-clients-use-the-product.md), [D-06](D-06-risk-tiers-are-configuration.md), [D-10](D-10-rules-are-configuration-and-the-engine-is-code.md), [D-16](D-16-no-copilot-or-teams-agent.md), [D-20](D-20-ai-lead-role-in-default-configuration.md), [D-22](D-22-choose-the-foundation-by-need-and-running-cost.md), [D-26](D-26-first-release-runs-in-the-clients-azure.md)
- [Sign-in and security](../architecture/sign-in-and-security.md)
- [Configuration and presets](../architecture/configuration-and-presets.md)
- Guideline G14, grant access through groups, in [the guidelines](../../src/content/guidelines/README.md)
