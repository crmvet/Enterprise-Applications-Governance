# Diagrams

This page lists the public diagrams of the design, with a link to each folder.

- The diagrams call the product **[Product name]**. That is a placeholder name.
- They are dated 2026-11-22. The Microsoft facts on them were checked on Microsoft Learn and other cited pages on
  2026-11-21 and 2026-11-22.
- Microsoft changes its products, labels and licenses often. Check Microsoft Learn before you rely on a status or a
  license shown here.
- The design decisions behind the diagrams are in [docs/decisions](../decisions/).

## The sets

| Set | What it shows | Folder |
|---|---|---|
| Systems map pictures | Which Microsoft tool does which job in governing apps, and how the product is designed to link to each one. One PNG per page: 3 ecosystem pages, 10 overview pages and 92 system pages. Seven pages are held back (see [Held back](#held-back)) | [docs/design/systems/img](../design/systems/img/) |
| Systems map Visio source | The editable Visio files behind those pictures. One `.vsdx` file per set of pages, 16 files in all. Three are held back (see [Held back](#held-back)) | [docs/design/systems/visio](../design/systems/visio/) |
| Design diagrams | The logical design: configuration, data model, processes, MCP tools and identity, engine and security, 18 pages. Plus one walkthrough page: one new app in eight steps, with fictional people. One PNG per page | [docs/architecture/img](../architecture/img/) |
| Design diagrams Visio source | The editable Visio files behind those pictures: six `.vsdx` files for the design, each with its icon stencil (`.vssx`), and one `.vsdx` file for the walkthrough | [docs/architecture/visio](../architecture/visio/) |
| UI page designs | The product's four pages (Overview, Application 360, dependency map and timeline), each at 1280 and 390 pixels wide, with fictional data | [docs/design/step2/img](../design/step2/img/) (the `ui-*.png` files) |

## How the Visio files and the PNGs relate

- The `.vsdx` files are the source. Each page in a Visio file has one PNG in the `img` folder, exported at
  1920 x 1080 pixels. The tables below map each Visio file to its PNGs.
- A Visio page and its PNG show the same content. The Visio page has editable shapes and text, with connectors glued
  to their cards, so you can change it.
- If you change a page in Visio, export that page again as a PNG, so the picture and the source stay the same.
- The UI page designs have no Visio source. They are screenshots of the React pages in [src/ui](../../src/ui/),
  rendered with headless Chrome from fictional data. Each one carries the label "Local harness, fictional data".

## Systems map

### Ecosystem

| Visio file | What it shows | PNGs, one per page |
|---|---|---|
| [ecosystem.vsdx](../design/systems/visio/ecosystem.vsdx) | Where the product fits among Microsoft's tools: the hub, the layers, and seven parties around one record | [ecosystem-hub](../design/systems/img/ecosystem-hub.png), [ecosystem-layers](../design/systems/img/ecosystem-layers.png), [ecosystem-parties](../design/systems/img/ecosystem-parties.png) |

### Overviews

| Visio file | What it shows | PNGs, one per page |
|---|---|---|
| [ov-landscape.vsdx](../design/systems/visio/ov-landscape.vsdx) | The whole landscape: ten families of systems | [ov-landscape-1](../design/systems/img/ov-landscape-1.png), [ov-landscape-2](../design/systems/img/ov-landscape-2.png) |
| [ov-jobs.vsdx](../design/systems/visio/ov-jobs.vsdx) | One job, one system: the division of labor to plan, build and share, and to own, protect and answer | [ov-jobs-1](../design/systems/img/ov-jobs-1.png), [ov-jobs-2](../design/systems/img/ov-jobs-2.png) |
| [ov-flow.vsdx](../design/systems/visio/ov-flow.vsdx) | How work flows across the systems, from idea to build and from test to retirement | [ov-flow-1](../design/systems/img/ov-flow-1.png), [ov-flow-2](../design/systems/img/ov-flow-2.png) |
| [ov-identity.vsdx](../design/systems/visio/ov-identity.vsdx) | Who connects as whom: people as themselves, integrations as their own identity, and the license each one needs | [ov-identity-1](../design/systems/img/ov-identity-1.png), [ov-identity-2](../design/systems/img/ov-identity-2.png) |
| [ov-overlaps.vsdx](../design/systems/visio/ov-overlaps.vsdx) | Where Microsoft tools overlap, which one to pick in each situation, and where the product keeps no third copy | [ov-overlaps-1](../design/systems/img/ov-overlaps-1.png), [ov-overlaps-2](../design/systems/img/ov-overlaps-2.png) |

### Systems, one page per system

Every system page has the same layout:

- **Top:** the system's name, its family, and three pills for status, license and Managed Environments.
- **Left:** the system's job in governance.
- **Middle:** the system card: its system of record, who runs it, and the parties who use it.
- **Right:** what the product is designed to do with that system, one row per relation, each with a chip.
- **Bottom:** the systems it works together with, and why the product does not overlap with it.

Each page has its own key in the top right corner. The main chips are:

| Chip | Meaning |
|---|---|
| READ LIVE | The product reads a fact when a person asks, as that person. It keeps no copy |
| EVENT | A Microsoft tool tells the product that something happened |
| LINK | The product keeps the ID and opens the tool's own page |
| EXPOSE | MCP clients use the product's tools, as the signed-in person |
| IN THE PRODUCT | Part of the product itself: screens, tables, roles and checks |
| NEVER REBUILT | Left to the Microsoft tool. The product adds no copy |
| POSSIBLE LATER | A possible route, not designed now |
| IN DESIGN, with a date | The product side of the page is a design, as of that date |
| (preview) | Microsoft's own preview label on Microsoft Learn, on the date it was checked |

| Visio file | Family | PNGs, one per page |
|---|---|---|
| sys-ppadmin.vsdx (held back) | Power Platform admin and governance, 14 pages | admin-center-actions (held back), coe-starter-kit (held back), [data-policies](../design/systems/img/data-policies.png), [admin-center-deployment](../design/systems/img/admin-center-deployment.png), [environment-groups](../design/systems/img/environment-groups.png), [admin-center-licensing](../design/systems/img/admin-center-licensing.png), [managed-environments](../design/systems/img/managed-environments.png), [admin-center-monitor](../design/systems/img/admin-center-monitor.png), [power-platform-admin-center](../design/systems/img/power-platform-admin-center.png), power-platform-api (held back), [power-platform-catalog](../design/systems/img/power-platform-catalog.png), power-platform-inventory (held back), [admin-center-security](../design/systems/img/admin-center-security.png), [tenant-settings-and-routing](../design/systems/img/tenant-settings-and-routing.png) |
| [sys-ppalm.vsdx](../design/systems/visio/sys-ppalm.vsdx) | Power Platform ALM, 11 pages | [configuration-data-migration](../design/systems/img/configuration-data-migration.png), [dataverse-git-integration](../design/systems/img/dataverse-git-integration.png), [deployment-settings](../design/systems/img/deployment-settings.png), [github-actions-power-platform](../design/systems/img/github-actions-power-platform.png), [build-tools-azure-devops](../design/systems/img/build-tools-azure-devops.png), [power-platform-cli](../design/systems/img/power-platform-cli.png), [plans-in-power-apps](../design/systems/img/plans-in-power-apps.png), [power-platform-pipelines](../design/systems/img/power-platform-pipelines.png), [solution-checker](../design/systems/img/solution-checker.png), [solution-history](../design/systems/img/solution-history.png), [solutions-publishers-layers](../design/systems/img/solutions-publishers-layers.png) |
| [sys-dataverse.vsdx](../design/systems/visio/sys-dataverse.vsdx) | Dataverse and the MCP layer, 8 pages | [dataverse-auditing](../design/systems/img/dataverse-auditing.png), [dataverse-business-skills](../design/systems/img/dataverse-business-skills.png), [dataverse-custom-apis](../design/systems/img/dataverse-custom-apis.png), [dataverse-mcp-server](../design/systems/img/dataverse-mcp-server.png), [product-mcp-server](../design/systems/img/product-mcp-server.png), [dataverse](../design/systems/img/dataverse.png), [generative-pages](../design/systems/img/generative-pages.png), [power-apps-in-copilot](../design/systems/img/power-apps-in-copilot.png) |
| sys-ai.vsdx (held back) | Copilot and agents, 9 pages | [agentic-coe-front-door](../design/systems/img/agentic-coe-front-door.png), [work-iq-business-apps](../design/systems/img/work-iq-business-apps.png), [copilot-agent-kit](../design/systems/img/copilot-agent-kit.png), [copilot-managed-runtime](../design/systems/img/copilot-managed-runtime.png), [agent-365](../design/systems/img/agent-365.png), [microsoft-365-copilot](../design/systems/img/microsoft-365-copilot.png), [copilot-studio](../design/systems/img/copilot-studio.png), [microsoft-foundry-agents](../design/systems/img/microsoft-foundry-agents.png), power-platform-admin-cowork (held back) |
| [sys-identity.vsdx](../design/systems/visio/sys-identity.vsdx) | Identity, 6 pages | [entra-agent-id](../design/systems/img/entra-agent-id.png), [entra-group-teams](../design/systems/img/entra-group-teams.png), [entra-id](../design/systems/img/entra-id.png), [entra-id-governance](../design/systems/img/entra-id-governance.png), [entra-pim](../design/systems/img/entra-pim.png), [entra-workload-id](../design/systems/img/entra-workload-id.png) |
| [sys-security.vsdx](../design/systems/visio/sys-security.vsdx) | Security and compliance, 10 pages | [defender-cloud-apps](../design/systems/img/defender-cloud-apps.png), [defender-ai-agents](../design/systems/img/defender-ai-agents.png), [fabric-onelake-catalog](../design/systems/img/fabric-onelake-catalog.png), [purview-audit](../design/systems/img/purview-audit.png), [purview-compliance-manager](../design/systems/img/purview-compliance-manager.png), [purview-data-governance](../design/systems/img/purview-data-governance.png), [purview-dspm](../design/systems/img/purview-dspm.png), [purview-information-protection](../design/systems/img/purview-information-protection.png), [security-dashboard-ai](../design/systems/img/security-dashboard-ai.png), [sentinel](../design/systems/img/sentinel.png) |
| [sys-ado.vsdx](../design/systems/visio/sys-ado.vsdx) | Azure DevOps, 8 pages | [azure-boards](../design/systems/img/azure-boards.png), [azure-devops-administration](../design/systems/img/azure-devops-administration.png), [azure-devops-mcp-server](../design/systems/img/azure-devops-mcp-server.png), [azure-devops-wiki](../design/systems/img/azure-devops-wiki.png), [azure-pipelines](../design/systems/img/azure-pipelines.png), [azure-repos](../design/systems/img/azure-repos.png), [azure-test-plans](../design/systems/img/azure-test-plans.png), [azure-devops-copilot-connectors](../design/systems/img/azure-devops-copilot-connectors.png) |
| sys-github.vsdx (held back) | GitHub, 8 pages | [github-actions](../design/systems/img/github-actions.png), github-code-security (held back), github-copilot (held back), [github-enterprise-cloud](../design/systems/img/github-enterprise-cloud.png), [github-issues-projects](../design/systems/img/github-issues-projects.png), [github-mcp-server](../design/systems/img/github-mcp-server.png), [github-repositories](../design/systems/img/github-repositories.png), [github-secret-protection](../design/systems/img/github-secret-protection.png) |
| [sys-work.vsdx](../design/systems/visio/sys-work.vsdx) | Work and collaboration, 8 pages | [microsoft-365-admin-center](../design/systems/img/microsoft-365-admin-center.png), [dynamics-365-project-operations](../design/systems/img/dynamics-365-project-operations.png), [microsoft-lists](../design/systems/img/microsoft-lists.png), [microsoft-loop](../design/systems/img/microsoft-loop.png), [microsoft-planner](../design/systems/img/microsoft-planner.png), [microsoft-teams](../design/systems/img/microsoft-teams.png), [power-automate-approvals](../design/systems/img/power-automate-approvals.png), [sharepoint](../design/systems/img/sharepoint.png) |
| [sys-arch.vsdx](../design/systems/visio/sys-arch.vsdx) | Architecture, enablement and monitoring, 10 pages | [architecture-decision-records](../design/systems/img/architecture-decision-records.png), [architecture-center](../design/systems/img/architecture-center.png), [azure-api-center](../design/systems/img/azure-api-center.png), [application-insights](../design/systems/img/application-insights.png), [azure-well-architected](../design/systems/img/azure-well-architected.png), [cloud-adoption-framework-ai](../design/systems/img/cloud-adoption-framework-ai.png), [maker-welcome-content](../design/systems/img/maker-welcome-content.png), [microsoft-learn-viva-learning](../design/systems/img/microsoft-learn-viva-learning.png), [power-platform-adoption](../design/systems/img/power-platform-adoption.png), [power-platform-well-architected](../design/systems/img/power-platform-well-architected.png) |

## Design diagrams

These pictures belong to the [architecture pages](../architecture/README.md), which explain them. The full logical
design behind them is in [docs/design/step2](../design/step2/README.md).

| Visio file | What it shows | PNGs, one per page |
|---|---|---|
| [pub-s2-config.vsdx](../architecture/visio/pub-s2-config.vsdx) | The configuration lifecycle, the presets and the gate ladder | [s2-config-1-lifecycle](../architecture/img/s2-config-1-lifecycle.png), [s2-config-2-presets](../architecture/img/s2-config-2-presets.png), [s2-config-3-gates](../architecture/img/s2-config-3-gates.png) |
| [pub-s2-erd.vsdx](../architecture/visio/pub-s2-erd.vsdx) | The logical data model: the core operational model, the configuration model, and references and history | [s2-erd-1-core](../architecture/img/s2-erd-1-core.png), [s2-erd-2-config](../architecture/img/s2-erd-2-config.png), [s2-erd-3-refs-history](../architecture/img/s2-erd-3-refs-history.png) |
| [pub-s2-process.vsdx](../architecture/visio/pub-s2-process.vsdx) | The value stream; intake, triage and the tier; change management; the deployment record and AI drafts | [s2-process-1-value-stream](../architecture/img/s2-process-1-value-stream.png), [s2-process-2-intake-tier](../architecture/img/s2-process-2-intake-tier.png), [s2-process-3-change](../architecture/img/s2-process-3-change.png), [s2-process-4-deploy-ai](../architecture/img/s2-process-4-deploy-ai.png) |
| [pub-s2-mcp.vsdx](../architecture/visio/pub-s2-mcp.vsdx) | The MCP toolsets, identity end to end, and the route a call takes | [s2-mcp-1-toolsets](../architecture/img/s2-mcp-1-toolsets.png), [s2-mcp-2-identity](../architecture/img/s2-mcp-2-identity.png), [s2-mcp-3-routes](../architecture/img/s2-mcp-3-routes.png) |
| [pub-s2-engine.vsdx](../architecture/visio/pub-s2-engine.vsdx) | One engine as a portable library, and one write through the engine | [s2-engine-1](../architecture/img/s2-engine-1.png), [s2-engine-2](../architecture/img/s2-engine-2.png) |
| [pub-s2-security.vsdx](../architecture/visio/pub-s2-security.vsdx) | Roles to rights, the four gates on Azure, and the top threats | [s2-security-1](../architecture/img/s2-security-1.png), [s2-security-2](../architecture/img/s2-security-2.png), [s2-security-3](../architecture/img/s2-security-3.png) |
| [application-portfolio-management-walkthrough.vsdx](../architecture/visio/application-portfolio-management-walkthrough.vsdx) | A walkthrough with fictional people: one new app in eight steps, from the maker's request to the questions people ask later | [application-portfolio-management-walkthrough-8-steps](../architecture/img/application-portfolio-management-walkthrough-8-steps.png) |

Each `pub-s2-*-icons.vssx` file next to a design `.vsdx` file is the stencil that holds that file's icons.

## UI page designs

These are the eight `ui-*.png` files in [docs/design/step2/img](../design/step2/img/). The 1280 files show the
desktop layout and the 390 files show the phone layout.

| Page | What it shows | PNGs |
|---|---|---|
| Overview | The portfolio at a glance: counts, owner gaps, flagged deployments, coming change windows, and applications by stage, tier and kind | [ui-overview-1280](../design/step2/img/ui-overview-1280.png), [ui-overview-390](../design/step2/img/ui-overview-390.png) |
| Application 360 | One application on one page | [ui-application-360-1280](../design/step2/img/ui-application-360-1280.png), [ui-application-360-390](../design/step2/img/ui-application-360-390.png) |
| Dependency map | What one application depends on, and what depends on it | [ui-dependency-map-1280](../design/step2/img/ui-dependency-map-1280.png), [ui-dependency-map-390](../design/step2/img/ui-dependency-map-390.png) |
| Timeline | Changes, deployments and change windows over time | [ui-timeline-1280](../design/step2/img/ui-timeline-1280.png), [ui-timeline-390](../design/step2/img/ui-timeline-390.png) |

## Held back

These files are part of the design, but they are held back from this repository while their wording is reviewed.

| Folder | Files |
|---|---|
| docs/design/systems/img | admin-center-actions.png, coe-starter-kit.png, github-code-security.png, github-copilot.png, power-platform-admin-cowork.png, power-platform-api.png, power-platform-inventory.png |
| docs/design/systems/visio | sys-ai.vsdx, sys-github.vsdx and sys-ppadmin.vsdx, because each one holds some of the pages above |

## Icons

The systems map diagrams use official Microsoft icons from five Microsoft icon sets. **The icons belong to Microsoft.
They are used under Microsoft's terms, not under this repository's MIT license.** The [MIT license](../../LICENSE) does not
cover them. If you copy or reuse a diagram, Microsoft's terms apply to the icons in it. Product names belong to their
owners. The same applies to every Microsoft icon in the design diagrams and their `.vssx` stencils.

| Icon set | Microsoft's page, with the terms and the download |
|---|---|
| Azure architecture icons | https://learn.microsoft.com/azure/architecture/icons/ |
| Microsoft Power Platform icons | https://learn.microsoft.com/power-platform/guidance/icons |
| Microsoft Dynamics 365 icons | https://learn.microsoft.com/dynamics365/get-started/icons |
| Microsoft 365 architecture diagram templates and icons | https://learn.microsoft.com/previous-versions/microsoft-365/solutions/architecture-icons-templates (Microsoft has archived this page, and its former address redirects here. The terms and the download links are unchanged) |
| Microsoft Entra architecture icons | https://learn.microsoft.com/entra/architecture/architecture-icons |

### Microsoft's terms

Quoted from the pages above, as read on 2026-11-22.

The Power Platform, Dynamics 365 and Microsoft 365 pages:

> Microsoft permits the use of these icons in architectural diagrams, training materials, or documentation. You may
> copy, distribute, and display the icons only for the permitted use unless granted explicit permission by
> Microsoft. Microsoft reserves all other rights.

The Azure page says the same, with "You can copy" in place of "You may copy":

> Microsoft permits the use of these icons in architectural diagrams, training materials, or documentation. You can
> copy, distribute, and display the icons only for the permitted use unless granted explicit permission by
> Microsoft. Microsoft reserves all other rights.

The Microsoft Entra page says the same, with "You might copy" in place of "You may copy":

> Microsoft permits the use of these icons in architectural diagrams, training materials, or documentation. You might
> copy, distribute, and display the icons only for the permitted use unless granted explicit permission by
> Microsoft. Microsoft reserves all other rights.

The Azure page's guidelines (the Power Platform, Dynamics 365 and Microsoft Entra pages give the same guidelines in
nearly the same words; the Microsoft 365 page gives none):

> In diagrams, we recommend including the product name somewhere close to the icon.

> Don't crop, flip, or rotate icons.
>
> Don't distort or change icon shape in any way.
>
> Don't use Microsoft product icons to represent your product or service.

The Microsoft Entra page adds one more:

> Don't use Microsoft product icons in Marketing communications.

### How these diagrams follow them

- The icons are copied from Microsoft's downloads unchanged: not cropped, flipped, rotated, distorted or recolored.
  Every page footer says "Icons: official Microsoft icon sets, unchanged".
- Each Microsoft product icon has the product's name next to it.
- The product itself never has a Microsoft product icon. It uses a generic concept icon from the Microsoft 365 set
  ("Apps List Detail").
- Where Microsoft's sets have no icon for a product (GitHub, for example), the page uses a generic concept icon from
  the Microsoft 365 set ("Code" for GitHub), with the product's name next to it.
- A few icons are placed as PNG renderings of the official SVG, at the same proportions, because Visio's SVG import
  draws those shapes wrongly.
- This repository uses the icons only in architecture diagrams and documentation.
