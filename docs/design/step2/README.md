# Step 2 design: the logical design of the next version

**Status: in design.** These documents describe what the next version is designed to do. Nothing in them is released or deployed. The code in [`src/`](../../../src/) is early work that follows this design, under the MIT License, not a release.

This folder holds the logical design of the next version of Enterprise Applications Governance. It names every term and entity, sets out the configuration model and the logical data model, describes the processes, lists the tools on the product's own MCP server, designs the rules engine and the security model, and records the working defaults and the questions that are still open.

The design is logical first. Entity and column names do not depend on a data store, and each document shows how its concepts bind to a platform.

## Platform

The first release is designed to run in the client's own Azure subscription (D-26): one Azure Functions service that hosts the API, the MCP server and the engine, over Azure SQL, with a web app and Entra sign-in. The documents were written while the data store was still open (D-22), so most of them describe two bindings, Azure and Dataverse. The Azure binding applies. The Dataverse binding is kept as a later option, not a plan.

## The documents

| # | Document | What it holds |
|---|---|---|
| 20 | [Glossary and logical entities](20-glossary-and-entities.md) | The single source of names: the glossary, every entity with its ID, the engine contract (the few fixed meanings the engine knows) and the platform bindings at a glance |
| 21 | [Configuration and taxonomy model](21-configuration-model.md) | The configuration layer: configuration entities and fields, versioned configuration sets, presets and overlays, settings, validation rules, and the shipped seed |
| 22 | [Logical data model](22-logical-data-model.md) | Attributes, keys, relationships, delete behavior, history and audit for every entity, with the physical binding for each platform |
| 23 | [Processes](23-processes.md) | How work moves from idea to retirement: each process, its steps, who is responsible, and what it creates, asks and records |
| 24 | [MCP surface](24-mcp-surface.md) | Every tool on the product's own MCP server: inputs, outputs, records read and written, the role each tool needs, and the identity path |
| 25 | [Engine](25-engine.md) | The configuration-driven rules engine as a portable library: its operations, the guard on every write, validation, and the adapters that bind it |
| 26 | [Security model](26-security-model.md) | Who may see, change and decide what, how roles reach people and workloads, how that is enforced, and how it is proven afterwards |
| 28 | [Working defaults and open design questions](28-working-defaults.md) | The defaults every document follows, six open design questions with background, options and a recommendation, and three design questions the first code build left open |

## Diagrams

- The diagrams of this design (configuration, data model, processes, MCP surface, engine and security) are in [docs/architecture/img](../../architecture/img/), with their Visio source in [docs/architecture/visio](../../architecture/visio/).
- `img/` here holds the page design images at desktop and phone width.

The diagrams use Microsoft's icons, which stay under Microsoft's terms, not the MIT License. See the notice in [docs/diagrams/README.md](../../diagrams/README.md).

## How IDs are used across the documents

Each kind of item has one ID, given in one document. The other documents cite that ID rather than repeat the definition.

| ID | What it names | Defined in |
|---|---|---|
| C01 to C46 | Configuration entities, such as C32 Approval Rule or C46 Configuration Draft | Document 20, section 2.5 |
| O01 to O24 | Operational entities, such as O09 Decision or O14 Deployment Record | Document 20, section 2.6 |
| L01 to L05 | Later entities, not in the first release | Document 20, section 2.7 |
| P01 to P11 | Processes, such as P01 Intake and triage | Document 23 |
| OP-01 to OP-14 | Engine operations, such as OP-01 Evaluate tier or OP-08 Guard a write | Document 25, section 5.2 |
| G-01 to G-17 | Guard rules that OP-08 checks on every write | Document 25, section 5.10 |
| V01 to V48 | Validation rules for a configuration set (V45 to V48 were proposed by the build) | Document 21, section 9, and document 25, section 5.11 |
| Issue 1 to 16, Q2 to Q23 | Working defaults, cited as "28, issue n" or "28, Qn" | Document 28 |
| Q1, Q4, Q7, Q8, Q9, Q12 | The six open design questions. A line that follows one of them is marked as a working default with its question number | Document 28, section 3 |
| B20-1 and similar | Proposals the build made against a document, listed at its end. Each is a proposal and changes no decision | The end of documents 20 to 26 |
| Open from the build, items 1 to 3 | Design questions the first code build left open, cited as "open from the build, item n" | Document 28, section 4 |

Two look-alikes are different things. G-01 to G-17 (with a hyphen) are engine guard rules. G1 to G24 (no hyphen) are the guidelines in [src/content/guidelines](../../../src/content/guidelines/README.md), with G10 and G11 retired. Document 20 still says G1 to G19. B20-3 proposes G1 to G20, and G21 to G24 were added later for AI development. Each document also has its own numbered questions near its end; those are local to the document, and document 28 holds the merged list.

## Decisions

The design follows the product's decisions, cited by number (D-01, D-02 and so on). Each decision has a public record in [docs/decisions/](../../decisions/) with its context, the options, the choice and why. Where a document and a decision disagree, the decision wins.
