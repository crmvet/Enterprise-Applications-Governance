# D-03 Change gate: record or stop

- **Status:** Open. The configuration model is settled. Whether a check should ever stop a deployment, and which preset installs first, are still open.
- **Date:** 2026-11-21
- **Decided by:** the product owner

## Context

A deployment can arrive without the change approval that the product's rules require. Should the product then stop the deployment, or only record the gap and flag it?

The answer changes how the product connects to pipelines, how much it can break when it is down, and how much trust an organization must give it.

## Options considered

| Option | What it means | For | Against |
|---|---|---|---|
| Record only | The product keeps the approvals and the deployment record | Works with every way of deploying, including manual imports. No pipeline change | Nobody notices a missing approval until someone looks |
| Flag | Record, plus a visible flag and a message when a deployment arrived without its approval | Problems surface the same day. No risk of blocking a release | Detects, does not prevent |
| Advisory check | A step in the pipeline asks the product before it deploys and shows a warning | The person deploying sees the gap first. Never blocks a release | Needs a pipeline that can call the product. Warnings get ignored over time |
| Enforced gate through a custom Power Platform Pipelines host | An extension of the pipeline asks the product and approves or rejects the deployment | Real prevention inside Power Platform Pipelines | Needs a custom pipelines host and an admin-built pipeline |
| A check step in Azure DevOps or GitHub | The organization's own pipeline asks the product and stops without approval | Real prevention, with the pipeline's own checks | If the product cannot answer, releases can stop. Needs a fail mode and an emergency path |
| Microsoft's own pipeline approvals only | The product takes no part in the gate | Nothing new to run | The approval lives apart from the decision record |

No option stops a manual import. An admin who imports a solution by hand is outside every pipeline. The product can only flag it when it is recorded.

## Decision

No single answer yet. What is decided:

- **Every option stays open.** The design keeps all of them possible.
- **Each organization chooses through configuration, not code.** The choice can differ by risk tier, environment and kind of change, and the data model holds that configuration.
- **Not everything at once.** The product is designed as a core with optional additions, so an organization adds the stronger checks only when it needs them.

## Why

Every organization will answer this differently. The same organization will also answer it differently for a low-risk team app and for a critical finance app. A product that hardcodes one answer would be wrong for most of them.

## Consequences

- The design turns the options into a ladder of five gate levels: off, record, flag, advise and enforce. What each level does is fixed in the engine. Which level applies to which tier, change type and target stage is a configuration row (D-06, D-10).
- Every combination of tier, change type and target stage must resolve to exactly one gate row. If a row is missing, the engine refuses governed writes and reports the broken configuration. It never falls back to a built-in level.
- At enforce, each row says what happens when the product cannot answer: fail open (go and flag) or fail closed (stop).
- Advise and enforce work only through a check step in the organization's own pipeline. The product never builds, hosts or runs pipelines (D-19). The check steps are designed as copy-paste samples, not as a package.
- The shipped presets carry gate values for each tier. Those are preset values, not decisions.
- Still open: whether a check step should ever stop a deployment, which preset installs first, and which check step sample comes first.

## Related

- [D-06](D-06-risk-tiers-are-configuration.md), [D-07](D-07-decision-rights-are-configuration-with-presets.md), [D-10](D-10-rules-are-configuration-and-the-engine-is-code.md), [D-13](D-13-change-windows-record-flag-timeline.md), [D-19](D-19-never-build-pipelines.md)
- [Configuration and presets: the gate ladder](../architecture/configuration-and-presets.md#the-gate-ladder)
- [Rules engine](../architecture/rules-engine.md)
- [P05 Deployment record](../architecture/processes/p05-deployment-record.md)
- The pipeline step samples: [src/samples/pipeline-steps/](../../src/samples/pipeline-steps/)
