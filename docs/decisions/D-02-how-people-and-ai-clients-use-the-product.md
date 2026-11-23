# D-02 How people and AI clients use the product

- **Status:** Superseded in part by D-11, D-16, D-17 and D-26. MCP first, links to work items, no hosted documents and "AI never approves" still stand.
- **Date:** 2026-11-21
- **Decided by:** the product owner

## Context

The design had to say how people reach the product. Who works inside the product's own app, and who only answers from Outlook or Teams? People who work inside a model-driven Power Apps app need a premium Power Apps license, so the answer also decides who needs which license.

## Options considered

- **App first.** Most people work in a model-driven app, and AI tools come later. Familiar, but every user needs a premium license, and AI clients get whatever the screens happen to offer.
- **Outlook and Teams for most people.** A few people work in the app, and everyone else only answers approvals. Light on licenses, but it gives people no good way to ask questions or record facts.
- **MCP first.** People use the product mainly through AI clients over the Model Context Protocol (MCP). The tools come first, and screens come second.

## Decision

MCP first, with a data store and record keeping underneath. As decided on this date:

- **MCP first.** The primary way to use the product is through AI clients over MCP. Microsoft 365 Copilot was named first, with Copilot Studio agents, Claude, GitHub Copilot and others. The design starts from the MCP surface: the tools, what each one does, and under whose identity it runs. Screens come second.
- **Dataverse as the backend.** Dataverse is the system of record for the data model and record keeping.
- **Work items in Azure DevOps.** The product links to work items. It does not track work itself.
- **Documents as wiki pages.** The playbook, the guidelines and a human page for each application live as wiki pages. The product does not host documents.
- **Roles through teams.**
- **A premium license inside the model-driven app.** People who open the app hold a premium Power Apps license. That is accepted, not avoided.
- **An optional AI helper.** The product may have a built-in AI helper on top of its data and records if one is needed. It is optional, not the core, and it never approves.

## Why

AI clients are becoming the place where people ask questions and get work done. When the product's tools are designed first, every MCP client gets the same capabilities, and the screens become one way in, not the only one.

AI never approves. That rule was part of the design from the first draft.

## Consequences

Later decisions changed parts of this one:

- **D-11:** "teams for roles" means Microsoft Entra group teams and their administration only. Microsoft Teams is not part of the roles concept.
- **D-16:** no agent in Microsoft Copilot or Teams for now. People use MCP clients, the app and approval cards. Claude Code is the reference client. The product's own MCP server (D-09) moves to the center.
- **D-17:** the product never writes wiki pages or other documents. It says which documents are required, and it tracks and links them. People's own AI tools write them.
- **D-26:** the first release is designed to run on Azure, in the organization's own subscription. Azure SQL replaces Dataverse as the data store, and a React web app replaces the model-driven app. People who use the product through an MCP client or the web app are designed to need no Power Platform license for it.

What still stands: MCP first, links to work items rather than tracking work, the product never hosts documents, and AI never approves.

## Related

- [D-09](D-09-the-product-has-its-own-mcp-server.md), [D-11](D-11-roles-only-through-entra-groups.md), [D-16](D-16-no-copilot-or-teams-agent.md), [D-17](D-17-track-documentation-do-not-write-it.md), [D-26](D-26-first-release-runs-in-the-clients-azure.md)
- [MCP tools](../architecture/mcp-tools.md)
- [Sign-in and security](../architecture/sign-in-and-security.md)
- [Screens](../architecture/screens.md)
- The MCP server core and its tool contracts: [src/mcp/](../../src/mcp/)
