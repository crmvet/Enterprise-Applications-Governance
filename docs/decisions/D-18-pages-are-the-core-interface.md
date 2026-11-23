# D-18 Modern pages are the core interface, and the first release is set

- **Status:** Superseded in part by D-26. The four page designs and the first release scope stand, now as a React web app.
- **Date:** 2026-11-21
- **Decided by:** the product owner

## Context

From the start, the product owner wanted the product to work with its data through generative pages or something better, not classic screens. Earlier drafts still put the pages in an optional "Pages" package, with classic model-driven forms and views as the main interface of the core.

At the same time, D-15, D-16 and D-17 had dropped three packages. What the first release contains was still open.

## Options considered

- **Pages as an optional package.** Classic forms and views are the main interface. Pages come later.
  - Pros: a smaller core.
  - Cons: the main experience would be dated forms.
- **Pages in the core.** The pages are the product's interface from the first release.
  - Pros: a modern, presentable interface from day one.
  - Cons: more to build in the core.

## Decision

- **Generative pages are part of the core,** not an optional package. Overview, Application 360, the dependency map and the timeline are the product's interface, with a modern, presentable design, light only.
- **Classic forms and views stay underneath,** only as the data-entry back office and the fallback where a page cannot run. They are never the main experience.
- **The pages are built from code** with developer tools.
- **The first release is set:**
  - the core: the engine, the configuration, the pages, the gap check, documentation tracking, built-in change management, roles through Entra groups, and approval cards;
  - plus the product's own MCP server.

  Playbook skills are a later, optional package. Pipeline connectors wait for the change gate decision (D-03). There is no Copilot agent package, no Microsoft imports package and no wiki publisher package.

## Why

- The product owner wants a neat, presentable interface, not dated screens.
- Owners, reviewers and change control need four views every day: the portfolio, an application's full picture, its dependencies and its timeline. Those belong in the core.
- With three packages dropped, the remaining scope fits one core plus the MCP server.

## Consequences

- D-19 then turned pipeline connectors into copy-paste samples, not a package.
- D-22 reopened the interface when it put the data store up for review.
- D-26 chose Azure for the first release. The four page designs became a React and Fluent UI web app with the same designs: Overview, Application 360, the dependency map and the timeline. The web app's pages are read-only. Writes go through the API, the MCP tools and approval cards.
- Dataverse forms and views apply only to a possible later Dataverse edition. That is an option, not a plan.

## Related

- [D-03](D-03-change-gate-record-or-stop.md), [D-15](D-15-gap-check-instead-of-inventory-import.md), [D-16](D-16-no-copilot-or-teams-agent.md), [D-17](D-17-track-documentation-do-not-write-it.md), [D-19](D-19-never-build-pipelines.md), [D-22](D-22-choose-the-foundation-by-need-and-running-cost.md), [D-26](D-26-first-release-runs-in-the-clients-azure.md)
- [Screens](../architecture/screens.md)
- The page designs and harness: [src/ui/](../../src/ui/)
