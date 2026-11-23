# D-06 Risk tiers are configuration, never hardcoded

- **Status:** Accepted
- **Date:** 2026-11-21
- **Decided by:** the product owner

## Context

Risk tiers decide how much governance an application gets: which questions it answers, who approves its changes, how often it is reviewed and which gate applies. The question was whether the product should keep exactly three tiers, or let each organization choose.

## Options considered

- **Exactly three tiers, built in.** Simple, and close to Microsoft's own guidance. But organizations that already use four or five tiers would have to remap, and logic would branch on fixed tier numbers.
- **A default of three, with any number allowed.** Ship three tiers as the default, and let an organization change their number and meaning as configuration.

## Decision

A default of three tiers, and any number allowed. In the product owner's words: "we ship it as default config, but clients can change it to 3 or 5 or 55 tiers. Everything is taxonomy records, never anything hardcoded."

- **The default.** The product ships three tiers as its default configuration. Each tier maps to three Microsoft vocabularies: Microsoft's agent risk tiers, the Copilot Studio governance zones and the Power Platform application support tiers. Each mapping is itself a configuration row.
- **Any number.** An organization can have 3, 5 or 55 tiers. Each tier is a taxonomy row with its label, questions, required approvals, review cadence, gate level and mappings.
- **A rule for the whole product: everything is taxonomy rows.** Nothing is hardcoded. Tiers, stages, gates, change types, decision types, parties, roles, application kinds, statuses, checklists and review cadences are all rows, not fixed values baked into logic.
- **Logic reads configuration.** Rules, approvals and process stages read the configuration rows. They never branch on a fixed tier number. Where a platform feature can only branch on fixed values, the design must not depend on it for anything an organization can configure.

## Why

Organizations already have their own risk language, and it rarely matches anyone else's. When tiers are data, the product adapts to the organization, not the other way round. A product that never branches on fixed values also stays correct when an organization adds, renames or removes a tier.

## Consequences

- The data model has a taxonomy and configuration layer at its center.
- The rules engine compares ranks and reads rows by key. It never branches on a fixed tier number, stage number, label or count.
- Adding a tier forces the same configuration change to add its gate rows. The validator names any that are missing.
- The mapping to Microsoft's vocabularies is the product's own default. Each mapping row stores the Microsoft term, its source link and the date it was checked.
- The Microsoft CoE baseline preset follows Microsoft's three agent risk tiers one to one.

## Related

- [D-03](D-03-change-gate-record-or-stop.md), [D-07](D-07-decision-rights-are-configuration-with-presets.md), [D-10](D-10-rules-are-configuration-and-the-engine-is-code.md)
- [Configuration and presets](../architecture/configuration-and-presets.md)
- [Rules engine](../architecture/rules-engine.md)
- [Data model](../architecture/data-model.md)
- [P02 Tier assessment](../architecture/processes/p02-tier-assessment.md)
- The shipped presets: [src/config/presets/](../../src/config/presets/)
