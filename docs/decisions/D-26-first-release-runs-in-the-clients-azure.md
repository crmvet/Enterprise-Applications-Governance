# D-26 The first release runs in the organization's own Azure subscription

- **Status:** Accepted. Answers D-22. Supersedes in part D-02, D-10, D-11 and D-18.
- **Date:** 2026-11-22
- **Decided by:** the product owner

## Context

D-22 left one question open. Should the product keep its data in Dataverse, or in a database in the organization's own Azure? A foundation study compared the options on running cost and admin effort, with security as a must.

Most of the product's users are owners and reviewers, not builders. There may be few builders at all. So the license each user needs weighs more here than it would in a tool for makers. The product owner also did not want the product locked into a Dataverse solution.

## Options considered

1. **Dataverse first, with Azure later as a second edition.**
   - For: the design already assumed it. Dataverse brings auditing once an admin turns it on, backups, and continuous access evaluation.
   - Against: from February 2027, every user who opens a model-driven app needs a premium Power Apps license. Running it takes three skill sets: Power Platform, licensing and Azure, since the MCP server runs on Azure anyway. Generative pages had open questions about the regions they run in. Moving to Azure later would mean building the data layer and the interface twice.
2. **Azure in the organization's subscription first, with Dataverse kept as a fallback.** This was the recommended option.
   - For: no user needs a Power Apps license for the product. Running it takes one Azure skill set. The data stays in the organization's subscription.
   - Against: there is more to build. The product must provide its own audit log, row-level security and admin screens. The organization's Azure team runs every release, the monitoring and the restore drills.
3. **Decide after a technical test against both stores.**
   - For: the choice would rest on evidence.
   - Against: the design stays generic for longer, and it needs two tests instead of one.

## Decision

Option 2. The first release runs on Azure, in the organization's own subscription:

- one service on Azure Functions (Flex Consumption plan) holds the API, the product's MCP server and the portable rules engine;
- Azure SQL Database, with ledger tables and row-level security;
- a React and Fluent UI web app;
- Microsoft Entra sign-in, with app roles assigned to groups;
- approval cards in Teams and Outlook. The design's working default is a small group of runner flows on one Power Automate Process license. Sending the cards through Microsoft Graph is the alternative;
- shipped as a template the organization deploys in its own subscription.

## Why

- Most users are owners and reviewers, not builders. On Dataverse, every user who writes would need a Power Apps Premium license from February 2027. On Azure, nobody needs a Power Platform license for the product.
- There is no lock-in to a Dataverse solution.
- The data stays in the organization's own subscription.
- The scope fits agents, code apps and SaaS from other vendors, not only Power Platform.

## Consequences

- D-02 (Dataverse as the backend) becomes Azure SQL Database in the organization's subscription.
- D-10 (plug-ins and custom APIs as the engine) becomes the portable engine library inside the one service. The API is the only write path. Rules stay configuration.
- D-11 (roles through Entra group teams) becomes Entra app roles assigned to groups, which needs Microsoft Entra ID P1, or group reads through Microsoft Graph. Roles still come only from Entra groups.
- D-18 (generative pages as the main interface) becomes a React web app with the same page designs: the overview, Application 360, the dependency map and the timeline.
- D-09, D-16 and D-19 stand as written.
- The engine stays a portable library that targets .NET Framework 4.8 and .NET 10. A Dataverse edition, or a read-only Dataverse view for an organization that wants one, could come later without a rewrite. That is a possible later choice, not a plan.
- The next version is not a Dataverse solution. The 2021 release still is.
- Some parts of the design still need a technical test, such as sign-in on behalf of the person on Azure and the route for approval cards.

## Related

- [D-02](D-02-how-people-and-ai-clients-use-the-product.md), [D-09](D-09-the-product-has-its-own-mcp-server.md), [D-10](D-10-rules-are-configuration-and-the-engine-is-code.md), [D-11](D-11-roles-only-through-entra-groups.md), [D-16](D-16-no-copilot-or-teams-agent.md), [D-18](D-18-pages-are-the-core-interface.md), [D-19](D-19-never-build-pipelines.md)
- [D-22 Choose the foundation by what the product needs and what it costs to run](D-22-choose-the-foundation-by-need-and-running-cost.md)
- [D-25 The technical reference lives in this repository](D-25-technical-reference-lives-in-this-repository.md)
- [Architecture: the design in one screen](../architecture/README.md)
- [Data model](../architecture/data-model.md)
- [Rules engine](../architecture/rules-engine.md)
- [Sign-in and security](../architecture/sign-in-and-security.md)
- [MCP tools](../architecture/mcp-tools.md)
- [Screens](../architecture/screens.md)
- [The engine source](../../src/engine/)
