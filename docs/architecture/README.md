# Architecture

**Status: built and tested; runs as a demo install with fictional data; no release yet.** These pages describe the logical design of the next version of Enterprise Applications Governance and how it binds to Azure. The code that builds it is in [`src/`](../../src/README.md), under the MIT License. Each page says which parts are built and which are still designed, and [What works today](../../src/adapters/azure/STATUS.md) lists them for the Azure binding.

Start with the [overview](../overview.md) if you have not read it. For the reasons behind the product, see the [product page](https://citizendevelopmentacademy.com/solutions/enterprise-applications-governance/) on the Citizen Development Academy site.

## The pages

| Page | What it covers |
|---|---|
| [Configuration and presets](configuration-and-presets.md) | Everything an organization tailors as configuration rows, versions, how a change is proposed and accepted, the shipped presets and the gate ladder |
| [Data model](data-model.md) | The logical entities and their relationships, history and audit rules, and the Azure SQL binding with ledger tables and row-level security |
| [Data taxonomy](data-taxonomy.md) | Business data domains, their owners at any level, technical locations of any vendor, the mappings and data uses, the never rule, and the data checks |
| [MCP tools](mcp-tools.md) | The product's own MCP server: toolsets and tools, identity on behalf of the person, and what AI may and may not write |
| [Sign-in and security](sign-in-and-security.md) | Entra sign-in, roles only through Entra groups, app roles, workload grants, record and column access, approvals, audit, and the top threats |
| [Rules engine](rules-engine.md) | The portable rules library, its operations in plain words, one write through the engine, and the gate ladder at run time |
| [Screens](screens.md) | The web app: the four first page designs (portfolio overview, Application 360, dependency map and timeline) and the work pages where a person acts |
| [Processes](processes/README.md) | One page per process, from intake to retirement |

## The design in one screen

The product is designed to run in an organization's own Azure subscription. Today it runs as a demo install with fictional data, on these parts.

| Part | As built |
|---|---|
| One service | The API, the product's MCP server and the rules engine in one Azure Functions app (Flex Consumption plan, .NET 10, isolated worker). Azure Container Apps stays the designed fallback host, should Functions not carry the sign-in or the MCP server |
| Data | Azure SQL Database with the `config`, `ops` and `audit` schemas for the product's records: ledger tables for records that must never change, temporal tables for the rest, and row-level security on every product table. The host's own tables (rate limits, the approval outbox, store keys, record numbers and the schema version) sit in a `host` schema outside it |
| Web app | React and shadcn/ui, served by the same service. A signed-in person does their work there. Its writes reach the same engine and its write guard (OP-08) as every other way in, under the person's own roles |
| Sign-in | Microsoft Entra ID. People get product roles only through Entra groups, as app roles assigned to groups |
| Approvals | A person answers on the decision page in the web app by default. The approval runner, a small group of Power Automate flows shipped as a template with its routes off unless an install switches them on, is designed to send Teams and Outlook messages. With step-up sign-in on, a message only links to the decision page. With step-up sign-in off, the default, the runner sends an answerable approval card and relays its answer. It is tested through a stand-in and has not run in a real tenant yet |
| Pipelines | Your own Azure Pipelines, GitHub Actions or Power Platform Pipelines. A copy-paste step at the end of an organization's own pipeline is designed to report each deployment to the product's record endpoint. The samples are tested against a local stand-in, and a run the step cannot report is not recorded |
| Install | An Azure Developer CLI template with Bicep, which an organization's Azure team deploys in its own subscription. It has deployed the demo install. A real install cannot reach its first configuration version yet |

The code holds:

| Item | Count |
|---|---|
| Entities | 81 in the logical model: 77 for the first release (50 for configuration, 4 of them still proposals, and 27 for operational records) and 4 kept for later |
| Processes | 11, from intake to retirement, on three shared parts: approvals, reminders and the engine write path |
| MCP tools | 102, in 11 toolsets |
| Engine operations | 17, in one portable .NET library |
| Default roles | 15 human roles, plus 2 workload roles for pipelines and the approval runner, and 2 more workload roles proposed |
| Shipped presets | 4 full presets and 4 overlays |

These counts are taken from the code and can change before the first release.

## Words these pages use

| Word | Meaning |
|---|---|
| Business application | One thing the business runs and answers for, of any kind, made of one or more technical parts. The record every party points to |
| Technical component | One part of a business application inside a tool, such as an app, a flow, an agent or a repository. Held as the tool's ID, a link and a "last seen" date |
| Configuration set | A whole, named set of configuration rows. An organization has one working set in force |
| Configuration set version | An immutable, hashed snapshot of the working set. Every record stamps the version it was judged by |
| Preset | A shipped, read-only configuration set an organization starts from, such as Balanced or Regulated |
| Engine | The code that reads the configuration and checks the writes that reach it. A person's write meets the same rules under the person's own roles, and an AI client's write also meets the AI rules |
| Engine contract | The short list of fixed meanings the engine must understand, such as "accepted" or "person". Every label stays configurable |
| Gate level | One rung of the gate ladder: off, record, flag, advise or enforce |
| Approval runner | The Power Automate flows that send approval notices or cards and, for a card, write back the answer. They record a person's answer and never decide |
| Workload grant | A recorded, expiring permission that lets one non-human workload, such as a pipeline, hold one workload role |
| AI client for a person | The kind of actor recorded when an AI tool, such as Claude Code, acts for a signed-in person |

## Diagrams

The diagrams on these pages are in the `img/` folder, with their Visio source in the `visio/` folder. They were drawn for the design and show its logical shape, so some show earlier counts, such as 51 MCP tools in 10 toolsets. Microsoft's icons on them stay under Microsoft's terms, not the MIT License (see [docs/diagrams](../diagrams/README.md)).

## Open design questions

Some choices in the design are still open. Each page says "open design question" where one applies. The design runs on a stated default until the question is closed, and the page says which default it uses.

---

Microsoft, Azure, Entra, Outlook, Power Automate, Power Platform and Teams are trademarks of the Microsoft group of companies. Claude and Claude Code are products of Anthropic. GitHub is a trademark of GitHub, Inc. Other names are trademarks of their owners.
