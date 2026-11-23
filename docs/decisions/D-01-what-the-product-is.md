# D-01 What the product is

- **Status:** Accepted
- **Date:** 2026-11-21
- **Decided by:** the product owner

## Context

The 2021 release of Enterprise Applications Governance is a Dataverse solution that keeps a register of business applications. Its tables hold portfolios, groups, applications, dependencies, releases, deployments and environments.

Design of the next version started with a narrow question. Should its core be a business register, plus a decision log, a thin deployment record, intake and a risk tier?

## Options considered

- **A register with a few records around it.** A business register, a decision log, a thin deployment record, intake and a risk tier. Small and easy to explain. But it says what exists, not how the organization decides, and it leaves most of the people who make those decisions outside the product.
- **An operating model plus a product.** A playbook, guidelines and processes that say how the organization works, and an engine that runs those processes and keeps their records. Broader, and it gives every party that touches an application a place.

## Decision

The next version is an operating model plus a product. In the product owner's words, it is "a playbook and engine and process and guideline for the company to merge in harmony the business and IT and AI and citizen developers and PMO and change control and Architects. It will include everything, but not overstep on microsoft offerings to manage things."

It has four parts:

- a **playbook**: how the organization works;
- **guidelines**: the standards each party follows;
- **processes**: from idea to retirement;
- an **engine**: the code that runs those processes and keeps their records.

It brings these parties together:

| Party | Its place in the product |
|---|---|
| Business | Owners and demand |
| IT | The platform |
| AI | AI-built apps and agents |
| Citizen developers | Makers |
| PMO | Demand, the portfolio and stage gates |
| Change control | Change requests, approvals and change windows |
| Architects | Design review, standards and architecture decisions |

"It will include everything" means every party has a place, a role and a process in the product.

"But not overstep on Microsoft offerings" means that where a Microsoft tool already manages something, the product links to it or reads from it. It never rebuilds it. That covers inventory, monitoring, security, identity, deployment, work tracking, project tools and agent registries.

## Why

A register answers "what do we have?". It does not answer "who decides?", "what does this change need?" or "who may build with AI?". Those answers need every party in one shared record, working through the same processes.

Staying out of what Microsoft already ships keeps the product narrow. It is designed to sit next to the tools an organization already runs, not to compete with them.

## Consequences

- The product is designed as a playbook, guidelines, processes and an engine, not only a set of tables.
- Every party has roles and processes in the default configuration. Parties and roles are configuration rows, so an organization can rename, merge or drop any of them (D-06, D-07).
- The fit rule applies everywhere. Where a Microsoft tool holds a thing, the product stores an ID, a link and a "last seen" date. D-15 later removed scheduled imports, so the product keeps links, not copies.
- The design names what the product is not, for example a deployment tool, an ITSM suite, a project tool or an agent registry.
- D-28 later set the headline: governing AI development of the apps. The operating model set here stays underneath it.

## Related

- [D-02](D-02-how-people-and-ai-clients-use-the-product.md), [D-06](D-06-risk-tiers-are-configuration.md), [D-07](D-07-decision-rights-are-configuration-with-presets.md), [D-15](D-15-gap-check-instead-of-inventory-import.md), [D-28](D-28-govern-ai-development-of-the-apps.md)
- [Overview](../overview.md), including [what it is not](../overview.md#what-it-is-not)
- [How it fits Microsoft](../microsoft-fit.md)
- The playbook and the guidelines: [src/content/](../../src/content/)
