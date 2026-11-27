# D-37 Business data domains, configured per install and mapped to technical locations

- **Status:** Accepted. Its details are answered by D-41.
- **Date:** 2026-11-23
- **Decided by:** the product owner

## Context

D-36 asked for screens for every use case, data among them. Until then the design had one data owner per application and a simple data area entity, with no hierarchy and no link to the systems that hold the data. A question about data went to the one data owner of the application.

The product owner was asked what these "data places" should be. The answer was that they needed design:

- Data differs by level, and it is not only in Microsoft products. It sits in CRM systems, in Dataverse, in SQL databases, in Cosmos DB and in legacy systems.
- Within one system there are several bodies of data, each with its own owner, such as client data, personnel data, contracts, payroll, timesheets, client orders, warehouse, field operations and proposals.
- Each body of data has a business name and a mapping to the technical places that hold it, such as Dataverse in a given environment, a SQL database or a legacy system.
- The taxonomies are configured for each organization and maintained.

## Options considered

- **Keep one data owner per application, with a simple data area list.**
  - Pros: nothing new to build.
  - Cons: no hierarchy, no owner for a body of data across applications, and no link to where the data lives. A question about data goes to the application's owner, not to the person who owns the data.
- **Register the data itself, down to tables and columns.**
  - Pros: exact.
  - Cons: heavy upkeep, above all for legacy systems. The product would become the data catalog it says it is not. Microsoft Fabric's OneLake catalog and Microsoft Purview already do that job.
- **A business taxonomy, configured per install, mapped to technical locations of any vendor.**
  - Pros: business words first, owners at any level, any vendor, and links to the catalog instead of copies.
  - Cons: more to build: new entities, engine rules, tools and screens.

## Decision

A business data taxonomy, configured per install and mapped to technical locations:

- **Business data domains in a tree** that each install draws for itself, for example Customers, then Client orders, or People, then Payroll, then Bank details. An owner, and a deputy, can sit at any level. A domain with no owner of its own takes the owner of the nearest domain above it.
- **Technical locations from any vendor:** a Dataverse environment, an Azure SQL database, a SQL Server on premises, a Cosmos DB account, a CRM, a legacy system, a SaaS tenant or a Fabric lakehouse. A location is held as a link, never as a copy of the data.
- **Two mappings.** A domain sits in one or more locations, and one location can hold many domains. An application declares the domains it reads or writes, where, and whether the data reaches an AI model.
- **The taxonomies are configured per install and maintained** through the product's own configuration change process, with versions.

The details were to be designed before the build. D-41 answers them.

## Why

- A question about data should reach the person who owns the data, not one owner per application.
- Organizations keep data in many systems, not only Microsoft's. The taxonomy has to name them all.
- Business words come first and platforms second. Each organization uses its own words, so the tree is configuration, not code (D-06, D-10).
- Links, not copies (D-15). The product records which data is where and who decides about it. The catalog keeps the assets, columns and lineage.
- An AI builder can ask, while it builds, which domain a table belongs to and whether a use needs a decision (D-28).

## Consequences

- The data area entity is replaced. A data-use decision goes to the owner of the data domain.
- The design weighed three approaches (business-first, technical-first and governance-first) and kept the governance-first backbone. Every fact serves a named decision or check. Handling rules sit on classifications, and a domain may make a rule stricter but never looser.
- D-41 answers the details: which facts are records and which are configuration, who appoints top-level owners, which uses need a decision by default, and what happens to a use that a "never" rule forbids.
- An install that turns the data taxonomy off keeps the earlier behavior.
- **Status.** The model, the engine rules, the MCP tools, the Data screens and the demo data are built and tested. They run as a demo install with fictional data. There is no release yet.

## Related

- [D-06 Risk tiers are configuration, never hardcoded](D-06-risk-tiers-are-configuration.md)
- [D-10 Rules are configuration, the engine is code](D-10-rules-are-configuration-and-the-engine-is-code.md)
- [D-15 An on-demand gap check instead of importing Microsoft's inventory](D-15-gap-check-instead-of-inventory-import.md)
- [D-28 Govern AI development of the apps](D-28-govern-ai-development-of-the-apps.md)
- [D-36 The web app acts, through the same engine as AI clients](D-36-the-web-app-acts-and-the-engine-checks-every-write.md)
- [D-41 The data taxonomy details, and a never rule cannot be broken](D-41-the-data-taxonomy-details-and-a-never-rule-cannot-be-broken.md)
- [Data taxonomy](../architecture/data-taxonomy.md)
- [Data model](../architecture/data-model.md)
- [MCP tools](../architecture/mcp-tools.md), the `data` toolset
- The engine's data operations: [src/engine/Product.Engine.Core/Operations/](../../src/engine/Product.Engine.Core/Operations/) (`DataTaxonomy.cs`, `DataOperations.cs` and `DataGuard.cs`)
- The data tools: [src/mcp/Product.Mcp.Core/Backend/InProcess/Tools/](../../src/mcp/Product.Mcp.Core/Backend/InProcess/Tools/) (`DataReadTools.cs` and `DataWriteTools.cs`)
- The Balanced preset's classifications, domain levels and platforms: [src/config/presets/balanced/](../../src/config/presets/balanced/)
