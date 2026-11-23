# Enterprise-Applications-Governance
Enterprise Applications Governance for Power Platform
https://crm.vet/enterprise-applications-governance-for-power-platform/

## Next version (in design)

The next version of Enterprise Applications Governance is in design. Nothing in it is released or running yet. It is designed to govern AI development of your apps: people build with AI at speed, and every app stays owned, tiered and decided by a person. Underneath is [open-source application portfolio management](https://citizendevelopmentacademy.com/solutions/enterprise-applications-governance/) for Microsoft Power Platform, Dataverse and the apps around them. It is designed to run in your own Azure subscription.

- [Overview of the next version](docs/overview.md): what it is designed to be, who it is for, and its status.
- [Architecture](docs/architecture/README.md): the technical design.

The code of the next version is in [`src/`](src/), under the MIT License. It is early work that follows the design, not a release. The 2021 release in this repository stays here for reference.

## Where things are

| What | Where |
|---|---|
| Overview: what the next version is designed to be, who it is for, and its status | [docs/overview.md](docs/overview.md) |
| The playbook: how the organization and IT govern AI development of their apps | [src/content/playbook](src/content/playbook/README.md) |
| The guidelines: short numbered rules that the playbook and the product both cite | [src/content/guidelines](src/content/guidelines/README.md) |
| Architecture: configuration, data model, processes, MCP tools, sign-in and security, rules engine and screens | [docs/architecture](docs/architecture/README.md) |
| Step 2 design: the full logical design behind the architecture pages | [docs/design/step2](docs/design/step2/README.md) |
| Decision records: one per design decision, with its options and reasons | [docs/decisions](docs/decisions/README.md) |
| Diagrams: the public diagrams and their Visio source | [docs/diagrams](docs/diagrams/README.md) |
| Build log: how the design and the first code were made | [docs/build-log](docs/build-log/README.md) |
| The code: an offline core with its tests and checks | [src](src/README.md) |
| The 2021 release: the Dataverse solution source | [Solutions](Solutions/) |

## The 2021 release

The 2021 release is a Microsoft Dataverse solution, open source under the MIT License since 2021. Its unpacked source is in [`Solutions/`](Solutions/), and the GitHub releases v1.1.7.7 and v1.1.7.8 carry managed solution zips. It is kept for reference and is no longer maintained. The next version is designed as a new product, not an in-place upgrade of it.

## License

The code and documents are under the [MIT License](LICENSE). The Microsoft icons in the diagrams stay under Microsoft's terms, not the MIT License. See [docs/diagrams](docs/diagrams/README.md#icons).
