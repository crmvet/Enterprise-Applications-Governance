# Decision records

This folder holds one short record for each design decision about the next version of Enterprise Applications Governance. Each record gives the context, the options that were considered, the choice and the reasons.

The product is still in design. Where a record describes what the product does, read it as what the product is designed to do.

## How decisions are recorded

- **One file per decision.** The file name starts with the decision number, for example `D-06-risk-tiers-are-configuration.md`.
- **Numbered in order.** Numbers are given in the order the decisions were made. A number is never reused and never changed, so a reference such as "D-06" always means the same decision.
- **Dated.** Each record carries the date of the decision and who made it.
- **Superseded, not rewritten.** When a later decision changes an earlier one, the earlier record keeps the decision as it was made. Its status line names the later decision, and the later record names what it replaces. Its consequences may point forward to the later decision, so a reader can follow the thread.
- **No gaps.** A decision that is not about the technical design is not published. Its number keeps a short stub, so the sequence has no gaps.

Each record has the same parts:

| Part | What it holds |
|---|---|
| Status | Where the decision stands, and which other decisions changed it or were changed by it |
| Date | The day the decision was made |
| Decided by | The role that made the decision |
| Context | The question and why it came up |
| Options considered | The choices on the table, with their trade-offs |
| Decision | What was chosen |
| Why | The reasons |
| Consequences | What the choice changes in the design, and what later decisions changed |
| Related | Other decisions, design pages and source folders |

## Status words

| Status | Meaning |
|---|---|
| Accepted | The decision stands |
| Open | Part of the question is still undecided. What is settled is listed in the record |
| Answered by | The record held an open question, and the named decision answered it |
| Superseded in part by | The named decision replaced part of this one. The record says which part still stands |
| Superseded by | The named decision replaced this one |
| Not published | The decision is not about the technical design, so only the number is kept |

## Start here

- [D-28](D-28-govern-ai-development-of-the-apps.md) sets what the product is about: governing AI development of the apps.
- [D-01](D-01-what-the-product-is.md) sets the operating model underneath it: a playbook, guidelines, processes and an engine.
- [D-26](D-26-first-release-runs-in-the-clients-azure.md) sets where the first release runs: the organization's own Azure subscription.
- [D-10](D-10-rules-are-configuration-and-the-engine-is-code.md) and [D-06](D-06-risk-tiers-are-configuration.md) set how rules work: configuration read by a code engine, with nothing hardcoded.
- [D-03](D-03-change-gate-record-or-stop.md) is the main question that is still open: whether a check should ever stop a deployment.

## Index

| No. | Title | Status | In one line |
|---|---|---|---|
| D-01 | [What the product is](D-01-what-the-product-is.md) | Accepted | An operating model plus a product: a playbook, guidelines, processes and an engine. It brings seven parties together and never rebuilds what Microsoft already ships. |
| D-02 | [How people and AI clients use the product](D-02-how-people-and-ai-clients-use-the-product.md) | Superseded in part by D-11, D-16, D-17, D-26 | MCP first, links to work items, no hosted documents, and AI never approves. Its Dataverse backend, its Copilot first stance and its wiki pages were later replaced. |
| D-03 | [Change gate: record or stop](D-03-change-gate-record-or-stop.md) | Open | Each organization picks a gate level by configuration: off, record, flag, advise or enforce. Whether a check should ever stop a deployment is still open. |
| D-04 | [Diagram style](D-04-diagram-style.md) | Accepted | Three diagram styles in one visual language and one palette, each published with its editable Visio source. |
| D-05 | [A product name, not a version label](D-05-a-product-name-not-a-version-label.md) | Accepted. Answered by D-31 | The product needs a real name that says what it is. D-31 chose Enterprise Applications Governance; the placeholder names in code are replaced in a change of their own. |
| D-06 | [Risk tiers are configuration, never hardcoded](D-06-risk-tiers-are-configuration.md) | Accepted | Three tiers ship by default, and an organization can have any number. Everything is taxonomy rows, and logic never branches on fixed values. |
| D-07 | [Decision rights are configuration, with presets to choose from](D-07-decision-rights-are-configuration-with-presets.md) | Accepted | Decision rights are configuration rows. Four full presets and three overlays give each organization a starting point. |
| D-08 | [Change management: built in by default, or linked to an existing tool](D-08-change-management-built-in-or-linked.md) | Accepted | Built-in change management is the default. Linked mode keeps an organization's existing change tool and stores only its reference. |
| D-09 | [The product can have its own MCP server](D-09-the-product-has-its-own-mcp-server.md) | Accepted | The product has its own MCP server with product-specific tools that act for the signed-in person. No tool can approve. |
| D-10 | [Rules are configuration, the engine is code, and AI makes the configuration changes](D-10-rules-are-configuration-and-the-engine-is-code.md) | Accepted. Superseded in part by D-26 | Every rule is configuration data, enforced by one code engine on every write. An AI drafts configuration changes and an admin accepts them. |
| D-11 | [Roles come only through Entra groups](D-11-roles-only-through-entra-groups.md) | Accepted. Supersedes in part D-02. Superseded in part by D-26 | People get product roles only through Entra groups, administered in Entra. No role is ever given to a person directly. |
| D-12 | [Planning tools: Azure DevOps first, with links to Planner and GitHub](D-12-planning-tools-azure-devops-first-links-only.md) | Accepted | An application record links to Azure DevOps work items, Planner plans and GitHub Projects. It never tracks work or copies plan facts. |
| D-13 | [Change windows: record them, flag deployments outside them, show them on the timeline](D-13-change-windows-record-flag-timeline.md) | Accepted | Each change has a window, a deployment outside it is flagged, and planned changes show read-only on the timeline. |
| D-14 | [Should the default configuration ship an AI lead role?](D-14-ai-lead-role-open-question.md) | Answered by D-20 | The open question of whether the AI lead is its own role or a duty of the platform admin. |
| D-15 | [An on-demand gap check instead of importing Microsoft's inventory](D-15-gap-check-instead-of-inventory-import.md) | Accepted | No scheduled copy of Microsoft's inventory. An admin runs a live gap check, and only unregistered items go on a claim list. |
| D-16 | [No Copilot or Teams agent: people use MCP clients, the app and approval cards](D-16-no-copilot-or-teams-agent.md) | Accepted. Supersedes in part D-02 | No dedicated agent for now. People use MCP clients, with Claude Code as the reference client, the app and approval cards. |
| D-17 | [The product tracks documentation and does not write it](D-17-track-documentation-do-not-write-it.md) | Accepted. Supersedes in part D-02 | The product says which documents are required, and records their links and status. People's own AI tools write them. |
| D-18 | [Modern pages are the core interface, and the first release is set](D-18-pages-are-the-core-interface.md) | Superseded in part by D-26. Amended by D-32 | Four pages are the core interface: Overview, Application 360, the dependency map and the timeline. The first release is the core plus the MCP server. |
| D-19 | [The product never builds pipelines: it maps them and records deployments](D-19-never-build-pipelines.md) | Accepted | Organizations keep their own pipelines. The product links to them, records every deployment and offers copy-paste step samples. |
| D-20 | [The AI lead role ships in the default configuration, configurable for each organization](D-20-ai-lead-role-in-default-configuration.md) | Accepted. Answers D-14 | The default configuration ships an AI lead role with its own Entra group. Each organization can merge it, separate it, rename it or drop it. |
| D-21 | [Not published](D-21-not-published.md) | Not published | Not about the technical design. The number is kept so the sequence has no gaps. |
| D-22 | [Choose the foundation by what the product needs and what it costs to run](D-22-choose-the-foundation-by-need-and-running-cost.md) | Accepted. Answered by D-26 | The 2021 release carries no weight in choosing the platform. Running cost and admin effort decide, and security is a must. |
| D-23 | [The 2021 release and its name are the author's own work](D-23-2021-release-and-name-are-the-authors-own.md) | Accepted. Amended by D-31 | The 2021 release and its name were published under the MIT License in 2021. D-31 made the name final for the next version. |
| D-24 | [Keep the code private until the main design choices are settled](D-24-keep-the-code-private-until-the-design-settles.md) | Superseded by D-27 | The code was to stay private until the data store, the name and the open questions were settled. |
| D-25 | [The technical reference lives in this repository](D-25-technical-reference-lives-in-this-repository.md) | Accepted | `docs/` holds the technical reference for readers, with one home for each piece of text and one record per decision. |
| D-26 | [The first release runs in the organization's own Azure subscription](D-26-first-release-runs-in-the-clients-azure.md) | Accepted. Answers D-22. Supersedes in part D-02, D-10, D-11, D-18. Amended by D-32 | Azure Functions, Azure SQL, a React web app and Entra sign-in, deployed as a template. It is designed so that no user needs a Power Platform license for the product. |
| D-27 | [The technical work is public under the MIT License](D-27-technical-work-is-public-under-mit.md) | Accepted. Supersedes D-24 | The source, the technical design, the decision records, the diagrams, the playbook and the guidelines are public under the MIT License. |
| D-28 | [Govern AI development of the apps, with a playbook for the organization and IT](D-28-govern-ai-development-of-the-apps.md) | Accepted | The product is about governing how an organization builds apps with AI. One playbook has an organization track and an IT track. |
| D-29 | [Who pushes to GitHub, and the license line](D-29-who-pushes-and-the-license-line.md) | Accepted | The AI session in the product workspace pushes both repositories, only after the final checks pass, never by force. The license line reads 2021-2026 crmvet. |
| D-30 | [Sixteen hours of independent work](D-30-sixteen-hours-of-independent-work.md) | Accepted | For about sixteen hours the AI session builds and publishes within fixed limits: no sign-in as a person, no Azure deployment, no release or tag, nothing private in public. |
| D-31 | [The product's name is Enterprise Applications Governance](D-31-the-name-is-enterprise-applications-governance.md) | Accepted. Answers D-05 | The full name is the public name. "EAGov" is only for technical keys, and public text never uses a bare "EAG" or "APM". |
| D-32 | [The web app is built on shadcn/ui](D-32-the-web-app-uses-shadcn-ui.md) | Accepted. Amends D-26 | The four pages move from Fluent UI to shadcn/ui: Radix primitives, Tailwind CSS and lucide icons, on the same Academy palette, light only. |

## Related

- [Overview](../overview.md)
- [How it fits Microsoft](../microsoft-fit.md)
- [Architecture](../architecture/README.md)
- [The source](../../src/)
