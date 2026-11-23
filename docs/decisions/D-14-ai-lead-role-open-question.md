# D-14 Should the default configuration ship an AI lead role?

- **Status:** Answered by D-20
- **Date:** 2026-11-21
- **Decided by:** the product owner

## Context

AI is one of the parties the product brings together (D-01). But AI never holds an accountability: every agent has a human owner. Someone still has to keep the AI guidelines, be consulted when an agent's autonomy changes, and chase agents that have no owner.

By D-06, D-07 and D-11, every role is a configuration record mapped to an Entra group. Any organization can add, rename or drop a role without code. So the open question was narrow: does the default configuration ship an AI lead role of its own, or do those duties go to the platform admin?

## Options considered

- **A. Its own role, which can be the same person.** The default configuration ships an AI lead role mapped to its own Entra group. A small organization puts the platform admin in that group too. A large one names a different person.
- **B. The platform admin holds it.** No AI lead in the default configuration. An organization can still add the role.
- **C. Its own role, always a different person.** As A, and the default configuration says the AI lead must not be the platform admin.

## Decision

None at this point. The product owner asked to discuss the role before deciding. D-20 answered it on the same day.

## Why

The role touches how heavy the default configuration is for small teams, and how agent reviews are routed. The product owner wanted to talk it through first.

## Consequences

- Until D-20, the design kept the AI lead as a proposed role.
- D-20 chose option A, configurable for each organization.

## Related

- [D-01](D-01-what-the-product-is.md), [D-06](D-06-risk-tiers-are-configuration.md), [D-07](D-07-decision-rights-are-configuration-with-presets.md), [D-11](D-11-roles-only-through-entra-groups.md), [D-20](D-20-ai-lead-role-in-default-configuration.md)
- [Sign-in and security](../architecture/sign-in-and-security.md)
