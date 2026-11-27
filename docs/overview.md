# Enterprise Applications Governance: the next version

**Status: built and tested; runs as a demo install with fictional data; no release yet.** The code of the next version is in [`src/`](../src/README.md), under the MIT License. It runs end to end on one machine, and as a demo install on Azure, always with fictional people and applications. No organization's own data has been loaded, and there is no release or version tag. The 2021 release is still the only release, and it stays in this repository for reference.

For the reasons behind the product, the problems it is meant to solve and who it helps, read the product page on the Citizen Development Academy site: [Enterprise Applications Governance](https://citizendevelopmentacademy.com/solutions/enterprise-applications-governance/). This page and the rest of `docs/` hold the technical reference.

## What it is

Govern AI development of your apps: it is designed so people can build with AI while each app stays owned, tiered and decided by a person. Open-source application portfolio management underneath, designed to work with Microsoft, never instead of it.

The product puts governance inside the AI build loop. Its own MCP server lets the AI builder, such as Claude Code or another MCP client, ask the governance questions while it builds: the risk tier, the required documents, the owners, the dependencies, the data it may use, and what needs a person's decision. AI drafts and people decide, on the product's decision page in its web app.

Underneath, it provides application portfolio management for Microsoft Power Platform, Dataverse and the apps around them. It is a playbook, guidelines, processes, a rules engine, an MCP server and a web app. It is designed to run in an organization's own Azure subscription; today it runs as a demo install with fictional data. It is designed to give the business, IT, AI, citizen developers, the PMO, change control and architects one shared record for each business application:

- what the application is and what it is made of;
- who owns it, with a deputy for each owner;
- its risk tier and lifecycle stage, and the decision that set each one;
- what it depends on, and what breaks if it changes or retires;
- which business data it reads or writes, who owns that data and where it sits;
- which documents it needs, and whether they are current;
- its change requests, approvals and deployment records.

It is driven by configuration, not hardcoded. Tiers, stages, roles, approvals, gates, required documents and the data taxonomy are configuration rows that each organization sets for itself. The product ships ready-made presets to start from: four full presets and four overlays, each checked by the engine's validator in every build.

## Who it is for

The product is designed for organizations that run many business applications on Microsoft and need one place where decisions about them are made and kept. That includes Power Platform apps, agents, AI-built apps, code apps and SaaS from other vendors.

Most of the people it is designed for are owners and reviewers, not builders. They ask questions, answer approvals and record facts. The default configuration names seven parties:

| Party | Typical people | What they do in the product |
|---|---|---|
| Business | Application owners, data owners, sponsors, the data governance lead | Own applications and business data, approve changes for their apps, make portfolio and data decisions |
| IT | Platform admins, security reviewers | Triage intake, confirm tiers, run the gap check and access checks |
| AI | The AI lead | Keeps the AI guidelines, is consulted on agent autonomy |
| Citizen developers | Makers | Ask for apps, register apps they built, record deployments and documents |
| PMO | Portfolio and demand managers | Rank intake, run the portfolio review |
| Change control | Change managers, the change board | Authorize higher-risk and emergency changes, keep the change schedule |
| Architects | Enterprise and solution architects | Review designs, record architecture decisions |

Parties and roles are configuration rows too. An organization can rename, merge or drop any of them.

## Three ways in

People and tools reach the product in three ways, and all three go through the same rules engine.

1. **MCP clients.** The product has its own MCP server. AI tools ask product questions such as "who owns this app?" or "what breaks if it retires?", and record drafts and facts for the signed-in person. Claude Code is the reference client. See [MCP tools](architecture/mcp-tools.md).
2. **The web app.** A signed-in person does their work there: the register, My work, intake, decisions and approvals, owners, reviews, changes and deployments, data, configuration, roles and access, and more. A person answers an approval on the decision page. See [Screens](architecture/screens.md).
3. **Pipeline record steps.** A copy-paste step at the end of an organization's own pipeline is designed to report each deployment to the product's record endpoint. The samples are tested against a local stand-in, and a run the step cannot report is not recorded. The product never builds, hosts or runs a pipeline. See [Processes](architecture/processes/README.md).

Each way in (the web app, an MCP client, a pipeline step) reaches the same engine and its write guard (OP-08): a person's write meets the same rules under the person's own roles, and an AI client's write also meets the AI rules.

AI never approves. No tool an AI client is served approves anything, and the one tool that records an approval answer is served only to a person in the web app. See [Sign-in and security](architecture/sign-in-and-security.md).

## Where it runs

The product is designed to run in an organization's own Azure subscription. Today it runs as a demo install with fictional data, on these parts.

| Part | As built |
|---|---|
| One service | The API, the MCP server and the rules engine in one Azure Functions app on the Flex Consumption plan. A local server runs the same code on one machine |
| Data | Azure SQL Database. Its scripts create ledger tables for records that must never change, temporal tables for the rest, and row-level security. A SQLite store serves local runs and tests |
| Web app | A React and shadcn/ui app, served by the same service |
| Sign-in | Microsoft Entra ID, with product roles given only through Entra groups |
| Install | An Azure Developer CLI template with Bicep. It has deployed the demo install. A real install with an organization's own data cannot reach its first configuration version yet |
| Approval runner | A small group of Power Automate flows, shipped as a template, with its routes off unless an install switches them on. With step-up sign-in on, it sends a notice that links to the decision page. With step-up sign-in off, the default, it sends an answerable Power Automate approval card and relays the answer to the product. Both are tested through a stand-in for the runner; the flows have not run in a real tenant yet |

The product is designed so that people who use it through an MCP client or the web app, and people who answer an approval on the decision page, need no Power Platform license for it. Where an install switches on the approval runner with step-up sign-in off, people who answer inside a card need Power Automate rights. Microsoft says approvers can respond from Outlook, Teams or the Power Automate action center if they have a Power Automate license, or an Office 365 or Dynamics 365 license with built-in Power Automate capabilities ([Learn](https://learn.microsoft.com/power-automate/get-started-approvals), checked 2026-11-22). Microsoft also says "Users who respond to approval requests don't need a Premium license" ([Learn](https://learn.microsoft.com/power-platform/admin/power-automate-licensing/faqs), checked 2026-11-22).

The rules engine is a portable library with no database code inside. It builds for .NET 10, .NET Framework 4.8 and .NET Standard 2.0, and its tests run on the first two. The rules do not depend on one data store.

## Design principles

These are the rules the design follows, and the code follows them. The engine enforces the ones about who may write what, and its tests cover them.

- **Configuration, not code.** A new rule is a configuration row, never a code change or a redeploy.
- **AI drafts, people decide.** Records an AI creates start as Draft or Proposed. Only people approve, by default on the decision page. The records an AI drafts name the AI client and the person it acted for.
- **Roles only through Entra groups.** No product role is given to a person directly. Who holds a role is managed in Entra.
- **Links, not copies.** Where a Microsoft tool holds a thing, the product stores an ID, a link and a "last seen" date. See [How it fits Microsoft](microsoft-fit.md).
- **Data decisions go to the owner of the data.** Business data domains, their owners and where the data sits are part of the record. A use that a "never" rule forbids is refused when it is saved. See [Data taxonomy](architecture/data-taxonomy.md).
- **Final records stay final.** Decisions, approval records, deployment records and the audit trail are append-only once final. A changed decision is designed to be a new record that supersedes the old one; the engine already plans that effect.

## What it is not

The product is designed to stay narrow. It is not a replacement for any Microsoft tool, not an AI app builder, not a deployment or CI/CD tool, not an ITSM suite, not a project management tool, not a CMDB, not a data catalog, not an application performance monitoring tool, not a BI tool, not an agent registry or MCP gateway, not a rebuild of the CoE Starter Kit, not an approval engine and not a compliance certification. It is not a Microsoft product and is not endorsed by Microsoft.

It stays open source under the MIT License, with all source public, and it is designed to run in an organization's own Azure subscription.

## Status

| Item | Status |
|---|---|
| The next version | Built and tested. Runs as a demo install with fictional data. No release yet |
| The code | In [`src/`](../src/README.md), under the MIT License. The engine, the MCP server, the Azure adapter, the web app, the demo kit and their tests. [What works today](../src/adapters/azure/STATUS.md) says what is built and what is designed but not built |
| The tests | The repository's CI workflow builds the code and runs the .NET tests and the file checks on every push. The web app's own tests, and the browser end-to-end tests that play 23 demo scenarios at desktop and phone width on a local demo install, run outside it. See [Testing](../src/docs/testing.md) and the [build log](build-log/README.md) |
| Not built yet | Among the parts not built yet: Teams and Outlook approval notices in a real tenant, the notification flow, live reads of a real tenant (the gap check and the owner check are tested over recorded fictional responses), the reader of Microsoft's agent registry, linked change management end to end, a check step that can stop a deployment, sending rejected work back to its draft, moving open approvals to the successor when an owner leaves, a real install's first configuration version, and a Dataverse edition. Some are designed and some are open questions: a Dataverse edition is only a possible later option, and whether a check step may stop a deployment is an open design question. The pages say "designed to" or "open design question" where they describe one |
| The design documents | Published here, in `docs/` |
| The 2021 release | Still here, and still the only release. A Microsoft Dataverse solution, first published in 2021 under MIT. The unpacked source is in `Solutions/`, and the GitHub releases v1.1.7.7 and v1.1.7.8 carry managed solution zips. Kept for reference, not maintained |
| Upgrade path | The next version is designed as a new product, not an in-place upgrade of the 2021 release |

## Try the demo

- [The product demo](../src/docs/demo/README.md): the scenarios, the presenter scripts and the demo kit, all on fictional data.
- [Install guide](../src/docs/install.md): a local run on one machine, and a demo install on Azure.

## Read next

- [Architecture](architecture/README.md): configuration, data model, data taxonomy, MCP tools, sign-in and security, rules engine and screens.
- [How it fits Microsoft](microsoft-fit.md): the fit rule.
- [The playbook](../src/content/playbook/README.md): how the organization and IT govern AI development of their apps, and the old ways of working to avoid.
- [The guidelines](../src/content/guidelines/README.md): short numbered rules that the playbook and the product both cite.
- [Step 2 design](design/step2/README.md): the full logical design behind the architecture pages.
- [Decision records](decisions/README.md): one record per design decision, with its options and reasons.
- [Diagrams](diagrams/README.md): the public diagrams and their Visio source.
- [Build log](build-log/README.md): how the design and the code were made, with dates and times.
- [The code](../src/README.md): what is in `src/` and how to run its checks.
- [The product documentation](../src/docs/README.md): concepts, a guide per role, the admin, install, MCP and REST API guides.
- The [product page](https://citizendevelopmentacademy.com/solutions/enterprise-applications-governance/) on the Citizen Development Academy site: the why, and who it helps.

---

Microsoft, Azure, Dataverse, Entra, Outlook, Power Apps, Power Automate, Power BI, Power Platform and Teams are trademarks of the Microsoft group of companies. Claude and Claude Code are products of Anthropic. Other names are trademarks of their owners.
