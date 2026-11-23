# D-09 The product can have its own MCP server

- **Status:** Accepted. D-16 moved the server to the center, and D-26 set where it runs.
- **Date:** 2026-11-21
- **Decided by:** the product owner

## Context

An early draft said the product would never host its own MCP server, chat window, document store or report tool, because Microsoft already hosts the Dataverse MCP server. Early research had also concluded that the product did not need its own MCP server.

The product owner rejected that statement. If the product has a custom MCP server, then it has an MCP server, even if that server runs as an Azure app.

## Options considered

- **Only Microsoft's Dataverse MCP server.** Nothing extra to host. But AI clients get plain table access, not product questions such as "what breaks if this app retires?".
- **The product's own MCP server.** Product-specific tools, hosted as an Azure app. More to host and secure, but the tools match how people actually ask.
- **Both, chosen per tool.** Each tool runs where it fits best.

## Decision

- **The product may have its own custom MCP server** with product-specific tools. Examples: "who owns this app?", "what breaks if it retires?", "start a change request" and "record a decision".
- **It is part of the product.** It is hosted as an Azure app, for example on Azure Functions or Azure Container Apps.
- **Each tool is placed by design.** Whether a tool runs through Microsoft's Dataverse MCP server, through the product's own server, or both, is a choice per tool. The trade-offs are hosting, identity on behalf of the user, cost and application lifecycle management.
- **Each surface is named plainly,** instead of one sweeping "never" statement.

## Why

People and AI builders ask product questions, not table questions. A server with product tools can answer them in one call, apply the product's rules, and act on behalf of the signed-in person. Hosting a small Azure app is a fair trade for that.

## Consequences

- The earlier research conclusion, that the product does not need its own MCP server, is superseded. The server is now a part the product takes where it adds value.
- D-16 made the product's own MCP server the main way in for MCP clients, with Claude Code as the reference client.
- D-26 placed the server in one Azure Functions app, together with the API and the rules engine, in the organization's own subscription. With Azure SQL as the data store, every tool goes through the product's own server in the first release design.
- As designed, the server has 51 tools in 10 toolsets. It acts on behalf of the signed-in person end to end, it is stateless, and no tool can approve.
- D-28 builds on it: the server is designed so the AI builder can ask governance questions while it builds.

## Related

- [D-02](D-02-how-people-and-ai-clients-use-the-product.md), [D-16](D-16-no-copilot-or-teams-agent.md), [D-26](D-26-first-release-runs-in-the-clients-azure.md), [D-28](D-28-govern-ai-development-of-the-apps.md)
- [MCP tools](../architecture/mcp-tools.md)
- [Sign-in and security](../architecture/sign-in-and-security.md)
- The MCP server core: [src/mcp/](../../src/mcp/)
- The tool contracts: [src/mcp/contracts/](../../src/mcp/contracts/)
