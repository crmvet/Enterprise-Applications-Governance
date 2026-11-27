# Enterprise-Applications-Governance

[![CI](https://github.com/crmvet/Enterprise-Applications-Governance/actions/workflows/ci.yml/badge.svg)](https://github.com/crmvet/Enterprise-Applications-Governance/actions/workflows/ci.yml)

Enterprise Applications Governance for Power Platform (the 2021 title)
https://crm.vet/blog/enterprise-applications-governance-for-power-platform/

## The next version

Govern AI development of your apps: it is designed so people can build with AI while each app stays owned, tiered and decided by a person. Underneath is open-source application portfolio management for Microsoft Power Platform, Dataverse and the apps around them. It is designed to run in an organization's own Azure subscription; today it runs as a demo install with fictional data.

One rules engine sits behind three ways in. The product's own MCP server lets an AI client such as Claude Code ask the governance questions while it builds. A web app is where a signed-in person does their work and answers approvals. A copy-paste step at the end of an organization's own pipeline is designed to report each deployment to the product's record endpoint. The samples are tested against a local stand-in, and a run the step cannot report is not recorded. Each way in (the web app, an MCP client, a pipeline step) reaches the same engine and its write guard (OP-08): a person's write meets the same rules under the person's own roles, and an AI client's write also meets the AI rules. AI drafts, and people decide.

The product page on the Citizen Development Academy site explains why it exists and who it helps: [Enterprise Applications Governance](https://citizendevelopmentacademy.com/solutions/enterprise-applications-governance/).

## Status

**Built and tested; runs as a demo install with fictional data; no release yet.**

- The code is in [`src/`](src/README.md), under the MIT License: a portable rules engine (.NET 10 and .NET Framework 4.8), the MCP server, an Azure adapter on Azure Functions (Flex Consumption) and Azure SQL with Microsoft Entra ID sign-in, a web app in React and shadcn/ui, a demo kit, and their tests.
- The [CI workflow](.github/workflows/ci.yml) builds the code and runs the .NET tests and the file checks on every push. The web app's own tests, and the browser end-to-end tests that play 23 demo scenarios at desktop and phone width, run outside it. The [build log](docs/build-log/README.md) gives the counts and links the test report.
- It runs on one machine, and as a demo install on Azure, always with fictional people and applications. No organization's own data has been loaded.
- There is no release and no version tag of the next version. The 2021 Dataverse solution below is still the only release.
- Among the parts not built yet: approval notices in Teams and Outlook in a real tenant, the notification flow, live reads of a real tenant (the gap check and the owner check are tested over recorded fictional responses), the reader of Microsoft's agent registry, linked change management end to end, a check step that can stop a deployment, sending rejected work back to its draft, moving open approvals to the successor when an owner leaves, a real install's first configuration version, and a Dataverse edition. [What works today](src/adapters/azure/STATUS.md) lists what is built and what is not.

## Try the demo on your machine

A local demo needs no Azure subscription and no sign-in. It runs the product over a local store with the fictional demo estate. You need the .NET 10 SDK and Node.js 22 or later. From the repository root:

```
node src/demo/kit/demo.mjs local up --port 5080
```

It prints the web app's address, the MCP endpoint and the fictional personas. On Windows the kit runs the server in WSL, so the .NET 10 SDK goes there.

- [The product demo](src/docs/demo/README.md): the 23 scenarios, the presenter scripts and the Claude Code prompts, all on fictional data.
- [Install guide](src/docs/install.md): the local run, and a demo install on Azure.

## Where things are

| What | Where |
|---|---|
| Overview: what the next version is, who it is for, and its status | [docs/overview.md](docs/overview.md) |
| The product documentation: concepts, a guide per role, and the admin, install, Claude Code and REST API guides | [src/docs](src/docs/README.md) |
| The product demo: the scenarios, the presenter scripts and the demo kit | [src/docs/demo](src/docs/demo/README.md) |
| The playbook: how the organization and IT govern AI development of their apps | [src/content/playbook](src/content/playbook/README.md) |
| The guidelines: short numbered rules that the playbook and the product both cite | [src/content/guidelines](src/content/guidelines/README.md) |
| Architecture: configuration, data model, data taxonomy, processes, MCP tools, sign-in and security, rules engine and screens | [docs/architecture](docs/architecture/README.md) |
| Step 2 design: the full logical design behind the architecture pages | [docs/design/step2](docs/design/step2/README.md) |
| Decision records: one per design decision, with its options and reasons | [docs/decisions](docs/decisions/README.md) |
| Diagrams: the public diagrams and their Visio source | [docs/diagrams](docs/diagrams/README.md) |
| Build log: how the design and the code were made, with dates, test counts and the test report | [docs/build-log](docs/build-log/README.md) |
| The code: the engine, the MCP server, the Azure adapter, the web app, the demo kit and their tests | [src](src/README.md) |
| The 2021 release: the Dataverse solution source | [Solutions](Solutions/) |

## The 2021 release

The 2021 release is a Microsoft Dataverse solution, open source under the MIT License since 2021. Its unpacked source is in [`Solutions/`](Solutions/), and the GitHub releases v1.1.7.7 and v1.1.7.8 carry managed solution zips. It is kept for reference and is no longer maintained. The next version is designed as a new product, not an in-place upgrade of it.

## License

The code and documents are under the [MIT License](LICENSE). The Microsoft icons in the diagrams stay under Microsoft's terms, not the MIT License. See [docs/diagrams](docs/diagrams/README.md#icons).
