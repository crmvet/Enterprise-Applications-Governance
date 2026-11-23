# How it fits Microsoft

**Status: in design.** This page states the rule the design follows. It describes what the product is designed to do, not something that runs today.

## The fit rule

If a Microsoft tool can hold, enforce or show a thing, the product is designed to keep no copy of it and never rebuild it. Instead it is designed to do one of three things:

1. **Link to the tool.** The product stores the tool's ID, a link, a label and the date the item was last seen. Examples: a Power Platform environment, an agent, a pipeline, a work item, a wiki page.
2. **Listen for facts your own tools send.** Example: a step at the end of your own pipeline records a deployment. The product never builds, hosts or runs the pipeline.
3. **Read the tool live, when an admin asks.** Example: the gap check asks Microsoft's inventory "what is running that nobody registered?" as the admin who asks, at that moment, and keeps no copy of the inventory.

The third way follows how Microsoft built the inventory. Microsoft says the inventory query action in the Power Platform API and its admin connector "currently support only delegated user authentication", and return HTTP 403 Forbidden to service principals and managed identities ([Learn](https://learn.microsoft.com/power-platform/admin/power-platform-inventory), checked 2026-11-22). So the gap check is designed to run as a person, on demand, with no background job.

## What the product keeps

The product is designed to keep only what no Microsoft tool holds for a business application:

- the business application itself, its owners and deputies, its tier and its lifecycle stage;
- the decisions about it, with who decided, when and why;
- its change requests, approval records and deployment records;
- which documents it needs, with each one's link, status and owner;
- the organization's own configuration: tiers, stages, roles, approvals and gates.

Everything else stays in the Microsoft tool that owns it.

## Each Microsoft tool, one by one

The Citizen Development Academy site keeps the tool-by-tool view: what each Microsoft tool does, and how the product links to it. See the [Microsoft tools hub](https://citizendevelopmentacademy.com/power-platform-governance/microsoft-tools/). This repository does not repeat it.

For the technical side of the same rule, see:

- [Data model](architecture/data-model.md): the link columns and the gap check claim.
- [MCP tools](architecture/mcp-tools.md): the tools that read Microsoft APIs as the signed-in person.
- [Sign-in and security](architecture/sign-in-and-security.md): roles through Microsoft Entra groups, with membership managed in Entra.

The product is not a Microsoft product and is not endorsed by Microsoft. It is designed to work with Microsoft Power Platform, Dataverse and Azure.

---

Microsoft, Azure, Dataverse, Entra and Power Platform are trademarks of the Microsoft group of companies. Other names are trademarks of their owners.
