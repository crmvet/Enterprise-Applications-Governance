# D-25 The technical reference lives in this repository

- **Status:** Accepted
- **Date:** 2026-11-22
- **Decided by:** the product owner

## Context

The design work produced many working documents: the data model, the configuration model, the processes, the MCP tools, the security model, the engine and the diagrams. Working documents are written for the people who build the product. Readers who want to understand, evaluate or adopt it need pages written for them, in one place.

## Options considered

- **Keep the design private** until the product is built.
- **Publish the working documents** as they are.
- **Write a technical reference for readers,** keep it in this repository next to the source, and give each piece of text one home.

## Decision

The third option. This repository's `docs/` folder holds the technical reference:

- `docs/overview.md`;
- `docs/microsoft-fit.md`;
- `docs/architecture/`: configuration and presets, the data model, one page per process (11 in all), the MCP tools, sign-in and security, the rules engine, the screens, and public versions of the diagrams.

Each decision gets a short public record in `docs/decisions/`, with its context, its options, the choice and the reasons.

Each piece of text has one home. Other pages link to it instead of copying it.

The pages describe the design. They never say the product is built or running.

## Why

The depth belongs next to the source, where people who adopt the product will look for it. One home for each text keeps copies from drifting apart. Describing the product as designed, not as running, keeps the pages accurate while it is built.

## Consequences

- While the data store was open (D-22), the architecture pages were to show both bindings: Dataverse and the organization's own Azure. D-26 settled the question, so they describe the Azure binding.
- When this decision was made, the code stayed private under D-24. D-27 replaced D-24, so the code is public too.
- The working documents stay private. Public pages are written from them, for readers (D-27).

## Related

- [D-22 Choose the foundation by what the product needs and what it costs to run](D-22-choose-the-foundation-by-need-and-running-cost.md)
- [D-24 Keep the code private until the main design choices are settled](D-24-keep-the-code-private-until-the-design-settles.md)
- [D-26 The first release runs in the organization's own Azure subscription](D-26-first-release-runs-in-the-clients-azure.md)
- [D-27 The technical work is public under the MIT License](D-27-technical-work-is-public-under-mit.md)
- [Overview](../overview.md)
- [How it fits Microsoft](../microsoft-fit.md)
- [Architecture](../architecture/README.md)
- [Processes](../architecture/processes/README.md)
