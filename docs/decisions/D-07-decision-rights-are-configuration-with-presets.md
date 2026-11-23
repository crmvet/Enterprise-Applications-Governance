# D-07 Decision rights are configuration, with presets to choose from

- **Status:** Accepted
- **Date:** 2026-11-21
- **Decided by:** the product owner

## Context

Decision rights say who decides, who approves, who is consulted and who is informed, for each tier, stage and kind of change. The question was which decision rights the product should ship by default.

## Options considered

- **One fixed set of decision rights.** Simple to explain, but it fits few organizations as it is.
- **One default set, all configuration.** Organizations start from the default and change any row.
- **Several ready-made sets.** Organizations pick the set closest to how they work, then adjust it.

## Decision

- **Decision rights are configuration rows only.** The product ships a default set.
- **Ready-made sets to choose from.** The product offers several complete configurations, or presets, that an organization selects and then adjusts. Presets cover the whole operating model, not only the change gate.
- **Whole sets, not single rows.** A preset is a named configuration set that can be applied, copied and versioned.

## Why

Organizations differ in size, regulation and how much separation of duties they need. A small team and a regulated bank should not have to start from the same rules. Presets give each one a sensible starting point, and configuration lets it adjust from there without code.

## Consequences

- The configuration layer holds whole, named configuration sets, with immutable versions. Every record stamps the configuration version it was judged by.
- The design ships four full presets and three overlays:
  - Full presets: Balanced (the default three-tier preset), Starter, Regulated and the Microsoft CoE baseline.
  - Overlays: small team, strict separation and linked change management.
- An organization picks one full preset and any compatible overlays. The engine validates the result as one set, and nothing is applied halfway. Overlays that conflict, such as small team and strict separation, cannot be combined.
- After that, the working set belongs to the organization. Presets stay read-only templates.
- A product upgrade never applies changed presets by itself. It proposes a draft, and rows the organization changed are kept and listed as conflicts.
- The validator checks that each decision right has exactly one accountable role, and that no rule makes an AI an approver.

## Related

- [D-03](D-03-change-gate-record-or-stop.md), [D-06](D-06-risk-tiers-are-configuration.md), [D-08](D-08-change-management-built-in-or-linked.md), [D-10](D-10-rules-are-configuration-and-the-engine-is-code.md)
- [Configuration and presets: presets](../architecture/configuration-and-presets.md#presets)
- The shipped presets: [src/config/presets/](../../src/config/presets/)
- The configuration schemas: [src/config/schema/](../../src/config/schema/)
