# Architecture

**Status: in design.** These pages describe the logical design of the next version of Enterprise Applications Governance and how it binds to Azure. Nothing here is released or deployed. The code of the next version is in [`src/`](../../src/), under the MIT License. It is early work that follows this design, not a release.

Start with the [overview](../overview.md) if you have not read it. For the reasons behind the product, see the [product page](https://citizendevelopmentacademy.com/solutions/enterprise-applications-governance/) on the Citizen Development Academy site.

## The pages

| Page | What it covers |
|---|---|
| [Configuration and presets](configuration-and-presets.md) | Everything an organization tailors as configuration rows, versions, how a change is proposed and accepted, the shipped presets and the gate ladder |
| [Data model](data-model.md) | The logical entities and their relationships, history and audit rules, and the Azure SQL binding with ledger tables and row-level security |
| [MCP tools](mcp-tools.md) | The product's own MCP server: toolsets and tools, identity on behalf of the person, and what AI may and may not write |
| [Sign-in and security](sign-in-and-security.md) | Entra sign-in, roles only through Entra groups, app roles, workload grants, record and column access, approvals, audit, and the top threats |
| [Rules engine](rules-engine.md) | The portable rules library, its operations in plain words, one write through the engine, and the gate ladder at run time |
| [Screens](screens.md) | The four page designs: portfolio overview, Application 360, dependency map and timeline |
| [Processes](processes/README.md) | One page per process, from intake to retirement |

## The design in one screen

The next version is designed to run in your own Azure subscription.

| Part | Design |
|---|---|
| One service | The API, the product's MCP server and the rules engine in one Azure Functions app (Flex Consumption plan, .NET 10, isolated worker). Azure Container Apps is the fallback host if the sign-in or MCP tests fail on Functions |
| Data | Azure SQL Database with three schemas: `config`, `ops` and `audit`. Ledger tables for records that must never change, temporal tables for the rest, and row-level security on every table |
| Web app | React and shadcn/ui, with four read-only pages |
| Sign-in | Microsoft Entra ID. People get product roles only through Entra groups, as app roles assigned to groups |
| Approval cards | The approval runner: a small group of Power Automate flows that send approval cards in Outlook and Teams and write back each answer |
| Pipelines | Your own Azure Pipelines, GitHub Actions or Power Platform Pipelines. A copy-paste step at the end of a pipeline records each deployment |
| Install | A template your Azure team deploys in your own subscription |

As designed, the first release has:

| Item | Count |
|---|---|
| Entities | 66: 42 for configuration and 24 for operational records |
| Processes | 11, from intake to retirement, on three shared parts: approval cards, reminders and the engine write path |
| MCP tools | 51, in 10 toolsets |
| Engine operations | 14, in one portable .NET library |
| Default roles | 14 human roles, plus 2 workload roles for pipelines and the approval runner |
| Shipped presets | 4 full presets and 3 overlays |

These counts describe the design and can change before the first release.

## Words these pages use

| Word | Meaning |
|---|---|
| Business application | One thing the business runs and answers for, of any kind, made of one or more technical parts. The record every party points to |
| Technical component | One part of a business application inside a tool, such as an app, a flow, an agent or a repository. Held as the tool's ID, a link and a "last seen" date |
| Configuration set | A whole, named set of configuration rows. An organization has one working set in force |
| Configuration set version | An immutable, hashed snapshot of the working set. Every record stamps the version it was judged by |
| Preset | A shipped, read-only configuration set an organization starts from, such as Balanced or Regulated |
| Engine | The code that reads the configuration and checks every write, whoever the caller is |
| Engine contract | The short list of fixed meanings the engine must understand, such as "accepted" or "person". Every label stays configurable |
| Gate level | One rung of the gate ladder: off, record, flag, advise or enforce |
| Approval runner | The Power Automate flows that send approval cards and write back the answers. They record a person's answer and never decide |
| Workload grant | A recorded, expiring permission that lets one non-human workload, such as a pipeline, hold one workload role |
| AI client for a person | The kind of actor recorded when an AI tool, such as Claude Code, acts for a signed-in person |

## Diagrams

The diagrams on these pages are in the `img/` folder, with their Visio source in the `visio/` folder. They were drawn for the design and show its logical shape. Microsoft's icons on them stay under Microsoft's terms, not the MIT License (see [docs/diagrams](../diagrams/README.md)).

## Open design questions

Some choices in the design are still open. Each page says "open design question" where one applies. The design runs on a stated default until the question is closed, and the page says which default it uses.

---

Microsoft, Azure, Entra, Outlook, Power Automate, Power Platform and Teams are trademarks of the Microsoft group of companies. Claude and Claude Code are products of Anthropic. GitHub is a trademark of GitHub, Inc. Other names are trademarks of their owners.
