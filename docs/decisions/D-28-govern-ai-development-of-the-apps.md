# D-28 Govern AI development of the apps, with a playbook for the organization and IT

- **Status:** Accepted
- **Date:** 2026-11-22
- **Decided by:** the product owner

## Context

The earlier framing led with application portfolio management. AI was one of seven parties the product brings together (D-01), next to the business, IT, citizen developers, the PMO, change control and architects.

More and more apps are now built with AI tools. The product owner asked that the product make clear it enables AI development, from both the organization's view and IT's view. The product owner also asked it to "give a playbook, so we do not slide into old paradigms".

## Options considered

- **Keep portfolio management as the lead,** with AI as one party among seven.
- **Lead with governing AI development of the apps,** with the portfolio record underneath.

## Decision

The second option.

**The core idea.** The product is about governing how an organization builds apps with AI. It is designed to enable AI development, not to slow it down. The playbook and the public docs lead with that. Application portfolio management stays as the record underneath.

**One playbook, two tracks.**

- **The organization track:** leaders, business owners, the AI lead, the PMO and change control. It covers who may build with AI, who decides, which risk tier needs what, and how adoption is measured.
- **The IT track:** the platform team, architects and security. It covers environments, identities for people, agents and pipelines, the paved road for AI builders, how AI clients reach the product, evidence, and which Microsoft tool does which job.

**The old paradigms.** The playbook opens by naming the old ways of working and how to avoid sliding back into them. Examples:

- one approval board for every change;
- IT as a ticket queue;
- a yearly inventory spreadsheet;
- governance checked only at the end;
- a standards document nobody reads;
- banning AI tools until a policy exists;
- rebuilding what Microsoft already ships.

Each one gets the new practice, how the product supports it, and a sign that the team is sliding back.

**Governance inside the AI build loop.** The product's own MCP server is designed so the AI builder itself, such as Claude Code, GitHub Copilot or another MCP client, can ask the governance questions while it builds:

- the risk tier;
- the required documents;
- the owners;
- the dependencies;
- what needs a person's decision.

**AI drafts and people decide.** D-02, D-10 and guideline G17 stand. No AI approves its own work, or anyone's.

**The interim name stays:** Enterprise Applications Governance.

## Why

AI tools now draft apps, flows and agents. Governance that runs only after the build, or through one queue for every change, works against that. Asking the governance questions inside the build loop puts governance where the AI work happens. People still make every decision. Two tracks give each side of the organization the part of the playbook it acts on, and naming the old paradigms makes a slide back easy to spot.

## Consequences

- The playbook is reframed into an organization track and an IT track, with an opening chapter on the old paradigms.
- The public description, the docs and the README lead with governing AI development of the apps.
- The product's own MCP server stays the main surface for AI builders (D-09, D-16).
- Application portfolio management stays in the product as the shared record every party points to.

## Related

- [D-01](D-01-what-the-product-is.md), [D-02](D-02-how-people-and-ai-clients-use-the-product.md), [D-09](D-09-the-product-has-its-own-mcp-server.md), [D-10](D-10-rules-are-configuration-and-the-engine-is-code.md), [D-16](D-16-no-copilot-or-teams-agent.md), [D-20](D-20-ai-lead-role-in-default-configuration.md)
- [D-23 The 2021 release and its name are the author's own work](D-23-2021-release-and-name-are-the-authors-own.md)
- [D-27 The technical work is public under the MIT License](D-27-technical-work-is-public-under-mit.md)
- [Overview](../overview.md)
- [MCP tools](../architecture/mcp-tools.md)
- [P10 AI draft and human accept](../architecture/processes/p10-ai-draft-and-human-accept.md)
- [The playbook and the guidelines](../../src/content/README.md)
- [G17 Let AI draft and recommend; people approve](../../src/content/guidelines/G17-ai-drafts-people-approve.md)
