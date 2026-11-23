# D-15 An on-demand gap check instead of importing Microsoft's inventory

- **Status:** Accepted
- **Date:** 2026-11-21
- **Decided by:** the product owner

## Context

Earlier drafts had a "Microsoft imports" package. On a schedule, it would copy what exists from Microsoft's sources into the product: the Power Platform inventory, the agent registry and Entra data. The product owner asked who needs that copy, and what value it adds.

Microsoft already keeps the list of what exists. What it does not keep is the business view: which of those things nobody has registered, and who answers for them.

## Options considered

- **A scheduled import package.** A background job copies Microsoft's inventory, agent registry and Entra data into the product.
  - Pros: the data is always there to query.
  - Cons: a copy of Microsoft's data that goes stale. An unattended job needs a named admin account or an extra application permission, and possibly an extra license. It repeats what Microsoft already shows.
- **An on-demand gap check.** An admin asks, the AI reads Microsoft's inventory live as that admin, and only the unregistered items are listed.
  - Pros: no copy, no unattended job, no extra permission.
  - Cons: it runs only when an admin asks, and it sees only what that admin's role can see.

## Decision

The gap check was proposed in the design review in place of the scheduled copy, and the product owner agreed.

- **No scheduled import,** and no copy of Microsoft's inventory, agent registry or Entra data. Microsoft stays the source for what exists.
- **The gap check is one tool,** in the core or in the product's own MCP server (D-09). An admin asks in an MCP client, for example "what is running that is not in the register?" The AI queries Microsoft's inventory live, as that admin. It puts only the unregistered apps and agents on a claim list for owners.
- **Links, not copies.** When an owner claims a resource, the product stores only its Microsoft ID, a link and a "last seen" date on the business application. Agents and their owners stay in Agent 365, and sponsors stay in Entra. The product links to them.

## Why

- Microsoft already keeps the inventory. A copy adds cost and goes stale, and the product does not rebuild what a Microsoft tool already does (D-01).
- The gap that matters is the business view: what nobody registered, and who owns it.
- With no unattended job, there is no named-admin automation, no extra application permission and no extra Agent 365 license for it.

## Consequences

- The gap check is designed as process P06. Microsoft's inventory query accepts only a delegated user sign-in, so the check runs with the admin's own token. What it sees depends on the admin's Entra role, and each run records the role used.
- The product keeps gap check runs and claims. It keeps no inventory rows.
- For agents that Microsoft shows without an owner, the product records the business decision: keep, reassign, consolidate or retire. Reassigning the owner happens in the Microsoft 365 admin center, not in the product.
- The Microsoft imports package is gone. D-18 settles what the first release contains.

## Related

- [D-01](D-01-what-the-product-is.md), [D-09](D-09-the-product-has-its-own-mcp-server.md), [D-16](D-16-no-copilot-or-teams-agent.md), [D-18](D-18-pages-are-the-core-interface.md), [D-20](D-20-ai-lead-role-in-default-configuration.md)
- [P06 Gap check and claim](../architecture/processes/p06-gap-check-and-claim.md)
- [MCP tools](../architecture/mcp-tools.md)
- [Microsoft fit](../microsoft-fit.md)
