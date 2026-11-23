# D-08 Change management: built in by default, or linked to an existing tool

- **Status:** Accepted
- **Date:** 2026-11-21
- **Decided by:** the product owner

## Context

Change control is one of the parties the product brings together (D-01). The question was where the change record lives: in the product, or in a change tool the organization already runs, such as an IT service management (ITSM) tool.

No Microsoft product for change requests or a change board was found during the design. That is a finding of absence, so it is not verified.

## Options considered

- **Always in the product.** Simple and complete, but it competes with organizations that already run a change process in an ITSM tool.
- **Always in an external tool.** Light for the product, but it leaves organizations without a change tool with nothing.
- **Both, chosen by configuration.** Built in by default, with a linked mode for organizations that keep their own tool.

## Decision

Two modes, chosen by configuration:

- **Built-in change management, the default.** The product runs it: change requests, change types (standard, normal and emergency, all taxonomy rows per D-06), the change authority and the change board, approvals, change windows, the schedule, and the outcome linked to deployments.
- **Linked mode.** The organization keeps its existing change tool. The product stores that tool's change number and a link, and does not copy its workflow.

The mode is part of the configuration set and its presets (D-07). It can differ by organization, and by area where an organization needs that.

## Why

No Microsoft product for change requests or a change board was found, so an organization without its own change tool would otherwise have nothing. Built-in change management fills that gap. Organizations that already run a change tool should not have to run two, and linked mode lets them keep theirs.

## Consequences

- The data model holds full change management: change requests, change types, the change authority, board decisions, windows and the schedule, and outcomes. It also holds an external reference for linked mode.
- Each change request copies the mode from a setting when it is created. The setting can be scoped to a portfolio, and the most specific setting wins.
- The linked change management overlay sets the mode to linked.
- Work items still live in Azure DevOps or GitHub, and deployments still run in the organization's own deployment tools (D-12, D-19).
- D-13 adds change windows: a window on each change, an "outside window" flag on the deployment record, and planned changes on the timeline.

## Related

- [D-01](D-01-what-the-product-is.md), [D-06](D-06-risk-tiers-are-configuration.md), [D-07](D-07-decision-rights-are-configuration-with-presets.md), [D-12](D-12-planning-tools-azure-devops-first-links-only.md), [D-13](D-13-change-windows-record-flag-timeline.md), [D-19](D-19-never-build-pipelines.md)
- [P03 Change management](../architecture/processes/p03-change-management.md)
- [Data model](../architecture/data-model.md)
- The linked change management overlay: [src/config/presets/linked-change-management/](../../src/config/presets/linked-change-management/)
