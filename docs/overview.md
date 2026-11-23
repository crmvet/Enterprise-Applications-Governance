# Enterprise Applications Governance: the next version

**Status: in design.** Nothing in the next version is released or deployed yet. Every capability on this page is something the product is designed to do. The code of the next version is in [`src/`](../src/), under the MIT License. It is early work that follows this design, not a release. It runs only on a developer machine with fictional data ([what works today](../src/adapters/azure/STATUS.md)). The 2021 release stays in this repository for reference.

"Enterprise Applications Governance" is an interim name. It may change before the first release.

For the reasons behind the product, the problems it is meant to solve and who it helps, read the product page on the Citizen Development Academy site: [Enterprise Applications Governance](https://citizendevelopmentacademy.com/solutions/enterprise-applications-governance/). This page and the rest of `docs/` hold the technical reference.

## What it is designed to be

Govern AI development of your apps: people build with AI at speed, and every app stays owned, tiered and decided by a person. Open-source application portfolio management underneath, designed to work with Microsoft, never instead of it.

The product is designed to put governance inside the AI build loop. Its own MCP server is designed so the AI builder, such as Claude Code or another MCP client, can ask the governance questions while it builds: the risk tier, the required documents, the owners, the dependencies, and what needs a person's decision. AI drafts and people decide, on approval cards in Outlook and Teams.

Underneath, it is designed to provide application portfolio management for Microsoft Power Platform, Dataverse and the apps around them. The next version is designed as a playbook, guidelines, processes and an engine, and it is designed to run in your own Azure subscription. It is designed to give the business, IT, AI, citizen developers, the PMO, change control and architects one shared record for every business application:

- what the application is and what it is made of;
- who owns it, with a deputy for each owner;
- its risk tier and lifecycle stage, and the decision that set each one;
- what it depends on, and what breaks if it changes or retires;
- which documents it needs, and whether they are current;
- its change requests, approvals and deployment records.

It is designed to be driven by configuration, not hardcoded. Tiers, stages, roles, approvals, gates and required documents are configuration rows that each organization sets for itself. The product is designed to ship ready-made presets to start from.

## Who it is for

The product is designed for organizations that run many business applications on Microsoft and need one place where decisions about them are made and kept. That includes Power Platform apps, agents, AI-built apps, code apps and SaaS from other vendors.

Most of the people it is designed for are owners and reviewers, not builders. They ask questions, answer approval cards and record facts. The default configuration is designed to name seven parties:

| Party | Typical people | What they do in the product |
|---|---|---|
| Business | Application owners, data owners, sponsors | Own applications, approve changes for their apps, make portfolio decisions |
| IT | Platform admins, security reviewers | Triage intake, confirm tiers, run the gap check and access checks |
| AI | The AI lead | Keeps the AI guidelines, is consulted on agent autonomy |
| Citizen developers | Makers | Ask for apps, register apps they built, record deployments and documents |
| PMO | Portfolio and demand managers | Rank intake, run the portfolio review |
| Change control | Change managers, the change board | Authorize higher-risk and emergency changes, keep the change schedule |
| Architects | Enterprise and solution architects | Review designs, record architecture decisions |

Parties and roles are configuration rows too. An organization can rename, merge or drop any of them.

## Three ways in

The product is designed to be reached in three ways. All three are designed to go through the same rules engine.

1. **MCP clients.** The product is designed to have its own MCP server. It is designed so AI tools can ask product questions such as "who owns this app?" or "what breaks if it retires?", and record drafts and facts for the signed-in person. Claude Code is the reference client. See [MCP tools](architecture/mcp-tools.md).
2. **A modern web app.** Four read-only pages: the portfolio overview, Application 360, the dependency map and the timeline. See [Screens](architecture/screens.md).
3. **Approval cards in Outlook and Teams.** The product is designed so people approve on Power Automate approval cards. AI never approves. See [Sign-in and security](architecture/sign-in-and-security.md).

## Where it runs

The next version is designed to run in your own Azure subscription. As designed, the first release has these parts:

| Part | Design |
|---|---|
| One service | The API, the MCP server and the rules engine in one Azure Functions app on the Flex Consumption plan |
| Data | Azure SQL Database, with ledger tables for records that must never change and row-level security |
| Web app | A React and shadcn/ui app with the four pages |
| Sign-in | Microsoft Entra ID, with product roles given only through Entra groups |
| Approval cards | A small group of Power Automate flows that send the cards and write back the answers |
| Install | A template that your Azure team deploys in your own subscription |

People who use the product through an MCP client or the web app are designed to need no Power Platform license for it. People who answer approval cards need Power Automate rights. Microsoft says approvers can respond from Outlook, Teams or the Power Automate action center if they have a Power Automate license, or an Office 365 or Dynamics 365 license with built-in Power Automate capabilities ([Learn](https://learn.microsoft.com/power-automate/get-started-approvals), checked 2026-11-22). Microsoft also says "Users who respond to approval requests don't need a Premium license" ([Learn](https://learn.microsoft.com/power-platform/admin/power-automate-licensing/faqs), checked 2026-11-22).

The rules engine is designed as a portable library with no database code inside. The rules do not depend on one data store.

## Design principles

These are the rules the design follows. They describe what the product is designed to do, not something that is released or deployed.

- **Configuration, not code.** A new rule is a configuration row, never a code change or a redeploy.
- **AI drafts, people decide.** Records an AI creates start as Draft or Proposed. Only people approve, on approval cards. Every record shows who performed it, including "AI client for a person".
- **Roles only through Entra groups.** No product role is given to a person directly. Who holds a role is managed in Entra.
- **Links, not copies.** Where a Microsoft tool holds a thing, the product stores an ID, a link and a "last seen" date. See [How it fits Microsoft](microsoft-fit.md).
- **Records you can trust.** Decisions, approval records, deployment records and the audit trail are designed to be append-only once final. A changed decision is a new record that supersedes the old one.

## What it is not

The product is designed to stay narrow. It is not a replacement for any Microsoft tool, not an AI app builder, not a deployment or CI/CD tool, not an ITSM suite, not a project management tool, not a CMDB, not a data catalog, not an application performance monitoring tool, not a BI tool, not an agent registry or MCP gateway, not a rebuild of the CoE Starter Kit, not an approval engine and not a compliance certification. It is not a Microsoft product and is not endorsed by Microsoft.

It is designed to stay open source under the MIT License, with all source public, and to run in your own Azure subscription.

## Status

| Item | Status |
|---|---|
| The next version | In design. Not released and not deployed |
| The code of the next version | In [`src/`](../src/), under the MIT License. Early work that follows the design, not a release. It runs on a developer machine with fictional data. Its [status page](../src/adapters/azure/STATUS.md) says what works today |
| The design documents | Published here, in `docs/` |
| The 2021 release | Still here. A Microsoft Dataverse solution, first published in 2021 under MIT. The unpacked source is in `Solutions/`, and the GitHub releases v1.1.7.7 and v1.1.7.8 carry managed solution zips. Kept for reference, not maintained |
| Upgrade path | The next version is designed as a new product, not an in-place upgrade of the 2021 release |

## Read next

- [Architecture](architecture/README.md): configuration, data model, MCP tools, sign-in and security, rules engine and screens.
- [How it fits Microsoft](microsoft-fit.md): the fit rule.
- [The playbook](../src/content/playbook/README.md): how the organization and IT govern AI development of their apps, and the old ways of working to avoid.
- [The guidelines](../src/content/guidelines/README.md): short numbered rules that the playbook and the product both cite.
- [Step 2 design](design/step2/README.md): the full logical design behind the architecture pages.
- [Decision records](decisions/README.md): one record per design decision, with its options and reasons.
- [Diagrams](diagrams/README.md): the public diagrams and their Visio source.
- [Build log](build-log/README.md): how the design and the first code were made, with dates and times.
- [The code](../src/README.md): what is in `src/` and how to run its checks.
- The [product page](https://citizendevelopmentacademy.com/solutions/enterprise-applications-governance/) on the Citizen Development Academy site: the why, and who it helps.

---

Microsoft, Azure, Dataverse, Entra, Outlook, Power Apps, Power Automate, Power BI, Power Platform and Teams are trademarks of the Microsoft group of companies. Claude and Claude Code are products of Anthropic. Other names are trademarks of their owners.
