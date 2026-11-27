# D-17 The product tracks documentation and does not write it

- **Status:** Accepted. Supersedes in part D-02 (documents as wiki pages).
- **Date:** 2026-11-21
- **Decided by:** the product owner

## Context

D-02 placed the playbook, the guidelines and a human page per application in wiki pages. Earlier drafts then added a "Wiki publisher" package, so the product would write those pages itself.

The product owner asked who would really write them. In practice, an application owner's own AI tool, such as Claude Code, can post a wiki page directly. The product's tools add little by writing it too.

## Options considered

- **Keep the Wiki publisher package.** The product writes and updates wiki pages.
  - Pros: documents appear without the owner writing them.
  - Cons: the product becomes an authoring tool inside other systems. The owner's own AI tool already does this. More to build and support.
- **Require, link and track.** The product says which documents are required, records their links and status, and reminds owners. People's own AI tools write the documents.
  - Pros: no authoring in other tools. Documentation becomes something gates and reviews can check.
  - Cons: the product depends on owners, or their AI tools, to record links and status.

## Decision

- **The Wiki publisher package is dropped.** The product never writes wiki pages or other documents.
- **Documentation tracking goes into the core:**
  - **What is required.** A configuration rule (D-06, D-10) says which documents each kind of app or tier needs, for example a wiki page, a runbook or an architecture decision.
  - **Per app.** The product records each required document's link, its status (missing, draft, current or stale), its owner, and the date it was last updated or checked.
  - **Prompts.** It reminds the owner when a document is missing or stale. Gate and review rules can require it.
  - **Through MCP.** The product's own MCP server answers "what documentation is missing or stale for my app?" and records a link or a status update. The owner's AI tool does the writing, outside the product, for example through the Azure DevOps MCP Server or git.
- **The principle behind it:** the product requires, tracks and links. It does not author content in other tools.
- **Two write-back ideas are dropped for the same reason,** unless they are reopened: setting environment groups by tier, and setting an Entra app reference.

## Why

- Owners already have AI tools that write documents. The product adds nothing by writing them too.
- What organizations lack is knowing which documents exist, which are missing and which are stale. Tracking fills that gap.
- It keeps a clear line. Other tools hold the content. The product holds the requirement, the link and the status.

## Consequences

- Documentation tracking is designed as process P07. Missing documents are worked out from the requirements for the app's kind, tier and stage. A document record exists only once a link has been recorded.
- For a document a gate needs, "current" set by an AI client stays a claim until the document owner confirms it on a card. AI drafts and people decide. Later, [D-38](D-38-a-person-answers-an-approval-on-the-decision-page.md) moved the default place a person answers to the decision page in the web app.
- At the time, the first release was set as the core, with the gap check and documentation tracking, plus the product's own MCP server. D-18 settles what the first release contains.

## Related

- [D-02](D-02-how-people-and-ai-clients-use-the-product.md), [D-06](D-06-risk-tiers-are-configuration.md), [D-10](D-10-rules-are-configuration-and-the-engine-is-code.md), [D-15](D-15-gap-check-instead-of-inventory-import.md), [D-16](D-16-no-copilot-or-teams-agent.md), [D-18](D-18-pages-are-the-core-interface.md), [D-38](D-38-a-person-answers-an-approval-on-the-decision-page.md)
- [P07 Documentation tracking](../architecture/processes/p07-documentation-tracking.md)
- [P10 AI draft and human accept](../architecture/processes/p10-ai-draft-and-human-accept.md)
- [MCP tools](../architecture/mcp-tools.md)
- Guideline G20, keep required documents current, in [the guidelines](../../src/content/guidelines/README.md)
