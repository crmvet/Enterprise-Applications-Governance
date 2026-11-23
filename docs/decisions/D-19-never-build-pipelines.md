# D-19 The product never builds pipelines: it maps them and records deployments

- **Status:** Accepted
- **Date:** 2026-11-21
- **Decided by:** the product owner

## Context

Earlier drafts listed a "Pipeline connectors" package. The product owner asked what it was. The product needs to map each application's pipelines and Azure DevOps items. It does not need to build pipelines. The product owner asked for that to be stated clearly.

## Options considered

- **A pipeline connectors package.** The product ships and maintains its own connectors or pipelines.
  - Pros: one supported way to connect each deployment tool.
  - Cons: overlaps with Azure Pipelines, GitHub Actions and Power Platform Pipelines. One more package to support.
- **Map, record and offer samples.** The product links to each organization's own pipelines, records every deployment, and offers copy-paste steps that organizations add themselves.
  - Pros: organizations keep their own tools. A small, clear scope.
  - Cons: each organization adds the steps to its own pipelines.

## Decision

- **The product never builds, hosts or runs pipelines.** Organizations keep their own: Azure Pipelines, GitHub Actions or Power Platform Pipelines.
- **Map.** Each application record links to the pipelines that deploy it and to its Azure DevOps or GitHub items, such as work items and repositories. Links, not copies.
- **Record.** Every deployment is recorded: what, where, when, by which pipeline, and who approved. A record arrives in one of three ways:
  - a step at the end of the organization's own pipeline calls the product's API or MCP server;
  - the owner's AI tool, for example Claude Code, records it through the MCP server;
  - a person enters it in the app.

  Deployments that came another way are flagged (the flag level of D-03).
- **Check, optional.** The same kind of step, placed before the deploy, asks the product whether the change is approved. This is the advise and enforce levels of D-03. The change gate decision is still open.
- **Pipeline connectors are no longer a package.** They become copy-paste samples with instructions in the public repo: an Azure DevOps step, a GitHub Actions step, and a Power Platform Pipelines extension flow. Organizations add them to their own pipelines.

## Why

- Pipelines are already well served by Microsoft and GitHub tools. The product does not rebuild them (D-01).
- What is missing is the business record: which application changed, under which change, who approved it, and whether it matched.
- Samples are easy to adopt and adapt. They keep the product out of the deployment path unless an organization chooses a check.

## Consequences

- The deployment record is designed as process P05. Flags cover a missing approval, a deployment outside its window (D-13), one that came another way, and a code approval that came only from an AI.
- No gate level stops a manual import. It can only be flagged.
- A pipeline's identity records deployments through its own app role. A separate app role for the check before a deploy is designed for later, once the change gate is settled.
- The record step samples are in the source, with a local stub of the record endpoint that validates every request, and a test.
- The package list is now the core plus the product's own MCP server for the first release, then playbook skills. Pipeline steps are samples, not a package.

## Related

- [D-01](D-01-what-the-product-is.md), [D-03](D-03-change-gate-record-or-stop.md), [D-12](D-12-planning-tools-azure-devops-first-links-only.md), [D-13](D-13-change-windows-record-flag-timeline.md), [D-15](D-15-gap-check-instead-of-inventory-import.md), [D-17](D-17-track-documentation-do-not-write-it.md), [D-18](D-18-pages-are-the-core-interface.md), [D-26](D-26-first-release-runs-in-the-clients-azure.md)
- [P05 Deployment record](../architecture/processes/p05-deployment-record.md)
- [P03 Change management](../architecture/processes/p03-change-management.md)
- [Sign-in and security](../architecture/sign-in-and-security.md)
- The record step samples: [src/samples/pipeline-steps/](../../src/samples/pipeline-steps/README.md)
- The record endpoint contract: [src/mcp/contracts/](../../src/mcp/contracts/README.md)
