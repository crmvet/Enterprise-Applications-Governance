# D-16 No Copilot or Teams agent: people use MCP clients, the app and approval cards

- **Status:** Accepted. Supersedes in part D-02 (Microsoft 365 Copilot first). Amended by D-38.
- **Date:** 2026-11-21
- **Decided by:** the product owner

## Context

D-02 put Microsoft 365 Copilot first among the AI clients. The drafts then planned an optional "Copilot agent" package: a Copilot Studio agent published to Microsoft Copilot and Teams. The product owner looked again at how people would really reach the product.

## Options considered

- **Keep the Copilot agent package.**
  - Pros: people who work in Copilot or Teams meet the product there.
  - Cons: one more package to build, publish and support. It favors one AI client over the others.
- **No dedicated agent.** People use the MCP clients they already work in, the app, and approval cards.
  - Pros: one MCP server serves every client. Less to build.
  - Cons: people in Copilot get no ready-made agent for now.

## Decision

In the product owner's words: "No agent in Copilot or Teams. People use other MCP clients, the app and the approval cards."

- **The Copilot agent package is dropped** for now. There is no Copilot Studio agent, and no agent published to Microsoft Copilot or Teams. D-02's "especially in Copilot" is superseded for the time being.
- **People reach the product through three surfaces:**
  - MCP clients, with Claude Code as the reference client for examples, testing and guides;
  - the app (at the time, a model-driven app for people with a premium license, D-02);
  - approval cards in Outlook and Teams.
- **The product's own MCP server (D-09) moves to the center.** It is how Claude Code and other MCP clients get the product's own tools.
- **Copilot can still connect later.** Microsoft Copilot or a Copilot Studio agent could act as an MCP client in a later release, without a dedicated agent package.

## Why

- People reach the product through the MCP clients they already use. One MCP server serves them all.
- A reference client gives one concrete target for examples, tests and setup guides.
- Fewer packages to build and support.

## Consequences

- The product's MCP server is the main way in. Its design covers the toolsets, identity on behalf of the person, and what AI may and may not write.
- Approval cards stay the way a person answers. They record a person's answer, and AI never approves.
- D-17 and D-18 changed the package list again. D-18 settles what the first release contains.
- D-26 moved the first release to Azure. The app is now a React web app in the organization's own subscription. This decision stands as written.

## Amendment, 2026-11-23

[D-38](D-38-a-person-answers-an-approval-on-the-decision-page.md) makes the product's decision page the default place a person answers an approval, and the person's own sign-in proves who answered. Teams and Outlook messages become notices that link to the page. Answering inside a card stays an option for each install, through the approval runner, while step-up sign-in is off. The rest of this decision stands.

## Related

- [D-02](D-02-how-people-and-ai-clients-use-the-product.md), [D-09](D-09-the-product-has-its-own-mcp-server.md), [D-17](D-17-track-documentation-do-not-write-it.md), [D-18](D-18-pages-are-the-core-interface.md), [D-26](D-26-first-release-runs-in-the-clients-azure.md)
- [MCP tools](../architecture/mcp-tools.md)
- [Sign-in and security](../architecture/sign-in-and-security.md)
- The MCP server core and its tool contracts: [src/mcp/](../../src/mcp/)
- Setup guide for Claude Code: [src/docs/setup/S2-connect-claude-code.md](../../src/docs/setup/S2-connect-claude-code.md)
