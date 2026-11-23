# D-13 Change windows: record them, flag deployments outside them, show them on the timeline

- **Status:** Accepted
- **Date:** 2026-11-21
- **Decided by:** the product owner

## Context

A change window is the agreed time a change may reach production, for example Saturday 06:00 to 10:00. Change control keeps the schedule and the windows, and it wants a known schedule. Where a deployment tool can enforce a window, it does so itself. Azure Pipelines, for example, has a Business Hours check.

Built-in change management is the product's default (D-08). So the design had to decide whether the product only records each change's window, or also shows planned changes as a calendar.

## Options considered

- **A. Record the window only.** A window start and end on each change record. A deployment outside it is flagged. Planned changes show only as a list.
  - Pros: the smallest build. It is clear that enforcement stays in the deployment tool.
  - Cons: no overview. The change manager filters a list to find clashes.
- **B. Record, plus planned changes on the timeline.** As A, and the timeline already in the design also shows planned changes with their windows, by environment and date. Read-only.
  - Pros: change control gets its known schedule across all tools. Little extra cost, because the timeline was already designed.
  - Cons: one more thing to design and test on the timeline. People may ask for booking and freezes next.
- **C. A full change calendar in the product.** Freeze periods, booking of windows and clash warnings, all managed in the product.
  - Pros: all scheduling in one place.
  - Cons: close to the change calendar of an IT service management tool, which the product does not rebuild. More configuration and more screens. Blocking outside a window would still need the enforce gate level (D-03).

## Decision

Option B, as recommended.

- A window start and end on each change record.
- An "outside window" flag on the deployment record.
- Planned changes shown read-only on the timeline that is already in the design.
- Not in scope: freeze tables and booking. Enforcing a window stays in the deployment tool, for example the Azure Pipelines Business Hours check.

## Why

- Change control gets a known schedule across every tool, and the product does not become a scheduling tool.
- The timeline was already in the design, so the extra cost is small.
- Enforcement stays where deployment tools already do it (D-01).

## Consequences

- At the time, the timeline was a grid view in the core and a richer page in a separate Pages package. D-18 moved the pages into the core. D-26 then made them pages of a React web app. The timeline stays read-only, with no booking and no freeze calendar.
- In the current design, the overview page lists the change windows coming up, and the timeline shows each window and each deployment.
- A deployment is linked to its change by the change's ID, or by match: the same application and target, finishing inside the window.
- The MCP tool `get_change_schedule` is designed to return planned changes and their windows.

## Related

- [D-01](D-01-what-the-product-is.md), [D-03](D-03-change-gate-record-or-stop.md), [D-08](D-08-change-management-built-in-or-linked.md), [D-18](D-18-pages-are-the-core-interface.md), [D-26](D-26-first-release-runs-in-the-clients-azure.md)
- [P03 Change management](../architecture/processes/p03-change-management.md)
- [P05 Deployment record](../architecture/processes/p05-deployment-record.md)
- [Screens](../architecture/screens.md)
