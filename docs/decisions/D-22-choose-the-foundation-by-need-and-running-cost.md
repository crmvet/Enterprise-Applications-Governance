# D-22 Choose the foundation by what the product needs and what it costs to run

- **Status:** Accepted. Its open question, the data store, is answered by D-26.
- **Date:** 2026-11-22
- **Decided by:** the product owner

## Context

The 2021 release is a Dataverse solution with a model-driven app. The first decisions for the next version kept that shape. Dataverse was the backend (D-02). Synchronous plug-ins were the engine (D-10). Roles came through Dataverse teams backed by Entra groups (D-11). Generative pages were the main interface (D-18).

By then the design had moved to MCP first (D-16). Most people are designed to reach the product through AI clients, approval cards and a few pages. That raised a question. Does the product still need Power Apps at all, or only a secure place to keep its data?

## Options considered

- **Keep the 2021 foundation.** Build on Dataverse because the 2021 release did.
- **Choose the foundation fresh.** Judge every platform by what the product needs, what it costs an organization to run and how much admin work it takes.

The foundation study that followed compared six shapes:

| Shape | What it is |
|---|---|
| Dataverse | A model-driven app, generative pages, a plug-in engine, and the product's own MCP server on Azure |
| Dataverse with a code front end | A code app, Power Pages or a React app over Dataverse |
| Azure in the organization's subscription | Azure SQL, one service for the API and the MCP server, a React web app, and Entra sign-in |
| A hosted multi-tenant service | One shared service, run outside the organization's tenant |
| Hybrid | An Azure core with an optional, read-only Dataverse view |
| Microsoft Lists and Teams | Lists, flows and a canvas app |

## Decision

The foundation is chosen fresh. The 2021 release carries no weight in the choice. Its tables, its platform and its build method are not constraints.

- The two tests are the organization's running cost and the admin effort. Security is a must on every option.
- The product does not need Power Apps. It needs a secure data store with managed access. Its interfaces are its own MCP server, approval cards and a web page. An internal web app in Azure is acceptable.
- Dataverse with a model-driven app stays an option. It is compared on cost, not assumed.
- The open question is the data store: Dataverse, or a database in the organization's own Azure.

## Why

The product exists to solve the problems in its design, not to extend the 2021 build. If it needs only a secure store with managed access, the cost of a per-user app platform has to be justified, not assumed. Running cost and admin effort decide whether an organization can keep the product running for years.

## Consequences

- D-02, D-10, D-11 and D-18 were reopened. They stayed as written until the data store was picked.
- The foundation study compared the options on both tests.
- D-26 answered the question the same day. The first release runs on Azure, in the organization's own subscription.
- The 2021 release stays in this repository as a Dataverse solution, for reference.

## Related

- [D-02](D-02-how-people-and-ai-clients-use-the-product.md), [D-10](D-10-rules-are-configuration-and-the-engine-is-code.md), [D-11](D-11-roles-only-through-entra-groups.md), [D-16](D-16-no-copilot-or-teams-agent.md), [D-18](D-18-pages-are-the-core-interface.md)
- [D-26 The first release runs in the organization's own Azure subscription](D-26-first-release-runs-in-the-clients-azure.md)
- [Overview](../overview.md)
- [How it fits Microsoft](../microsoft-fit.md)
- [Architecture](../architecture/README.md)
