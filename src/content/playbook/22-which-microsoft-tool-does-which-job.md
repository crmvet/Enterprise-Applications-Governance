# Chapter 22. Which Microsoft tool does which job

- **Version:** 0.2 draft, 2026-11-22
- **Track:** IT
- **Parties:** IT (the platform team, security, identity), architects, auditors
- **Guidelines:** [G1](../guidelines/G01-keep-one-register.md), [G9](../guidelines/G09-release-the-tested-artifact.md), [G15](../guidelines/G15-keep-an-evidence-pack.md), [G16](../guidelines/G16-record-ai-use.md)
- **Processes:** [P05](processes.md#p05-deployment-record), [P06](processes.md#p06-gap-check-and-claim)

## Why this chapter exists

"We built our own inventory sync, and now we have two lists that disagree." "Is this a job for the governance product or for Microsoft's admin center?" Microsoft already ships the inventory, the environments and their policies, the pipelines, the identities, the agent registry, the audit log and the security portal. A governance tool that copies them creates a second truth that drifts. This chapter is one map of which tool does which job, so that neither the organization nor the product oversteps Microsoft.

## How AI changes this

- AI makes it cheap to start your own copy of a Microsoft tool. An inventory sync, an approval engine, a deployment script or an agent list can be drafted in an afternoon.
- The copy still drifts from Microsoft's own, with duplicate rules and gaps between them, and someone has to own it for years.
- Microsoft also adds new surfaces for AI quickly, such as the agent registry and Copilot Managed Runtime. The map says where each new thing lives before anyone builds around it.

## Old paradigm to avoid

**[7. Rebuild what Microsoft already ships](17-old-paradigms-and-how-not-to-slide-back.md#7-rebuild-what-microsoft-already-ships).** Here it shows up as an in-house inventory sync, approval engine, deployment tool or agent list next to Microsoft's. Apply the fit rule instead: link to the Microsoft tool, listen for the facts it sends, or read it live when an admin asks. **Slide-back signal:** a project to copy Microsoft's inventory into your own tables on a schedule, or to build your own approval engine or deployment tool.

## What good looks like

- Every job on the map has its Microsoft tool and an owner.
- No copy of a Microsoft tool's data is kept on a schedule. Technical parts are held as links: a Microsoft ID, a URL and a last seen date.
- When a new need appears, someone asks the fit question first and records the answer as an architecture decision.
- The governance record holds only what no Microsoft tool holds: the business application, its owners, its tier, its decisions, its changes, its deployments across tools and its required documents.

## The fit rule

If a Microsoft tool can hold, enforce or show a thing, keep no copy and never rebuild it. Do one of three things instead:

1. **Link to it.** Store its Microsoft ID, a URL and the date it was last seen.
2. **Listen for the facts it sends.** For example, the last step of your pipeline reports each deployment.
3. **Read it live when an admin asks.** For example, the gap check reads Microsoft's inventory as the admin who asks, and keeps only the gaps.

Only what no Microsoft tool holds belongs in the governance record.

## The map

| Job | Microsoft tool | What the product is designed to do |
|---|---|---|
| Inventory of apps, flows, agents and sites | Power Platform inventory in the Power Platform admin center. Apps built with Copilot Managed Runtime also appear in the Microsoft 365 admin center | Hold the business application, and link each technical part by its Microsoft ID, a URL and a last seen date. Run the gap check live, as the admin who asks, with no background job and no stored copy |
| Environments and policies | Managed environments, environment groups and their rules, environment routing | Record which environments an app lives in, as links. Never create an environment or set a policy |
| Deployment | Power Platform Pipelines, Azure Pipelines with Power Platform Build Tools, GitHub Actions for Power Platform | Map which pipeline deploys each app, take the record from the pipeline's last step, and flag deployments that came another way. Never build, host or run a pipeline |
| Identity and roles | Microsoft Entra ID: users, groups, app roles, workload identities, and Microsoft Entra Agent ID with its sponsors | Take its own roles only from Entra groups, and record each app's identities. Hold no group membership and assign no role |
| Agents | Microsoft Agent 365 and its agent registry | Record each agent's business owner, tier, AI use and decisions, and link to the agent in the registry. Never act as an agent registry |
| Work | Azure Boards, Planner, GitHub | Link the work item, plan or issue to the app it changes. Track no work |
| Audit | Microsoft Purview Audit | Keep its own decision log and the history of its own records. Link to Purview for everything else |
| Security | The Microsoft Defender portal | Link findings to the application record. Do no security work |
| Monitoring | The Power Platform admin center (inventory, usage and monitoring), Application Insights | Link to them from the application record. Copy no telemetry |
| AI access to data | Microsoft's Dataverse MCP server for Dataverse tables; the Azure DevOps and GitHub MCP servers for work and code | Serve only its own governance tools, as the signed-in person. Never act as an MCP gateway or pass on other tools' data |
| Approvals | Power Automate approvals, answered in Outlook and Teams | Work out who must answer and whether each answer counts. Only a person's answer counts |
| Documents | Azure DevOps wiki, repositories, SharePoint | Say which documents each app needs, and track each one's link, status, owner and last check. Never write a document |

## What only the product is designed to hold

- One record per business application of any kind, grouping its technical parts as links.
- Owners and deputies, with their start and end dates.
- The risk tier, and what the tier needs.
- The decision log, append-only.
- Change records, built in or linked to your existing change tool.
- Deployment records across every tool that deploys.
- Required documents and their status.
- Gap check runs and the claims they produce.

## How it works

1. **Ask the fit question first.** When a new need appears, ask: does a Microsoft tool already hold, enforce or show this?
2. **If yes, choose link, listen or read live.** Never a scheduled copy.
3. **If no, put it in the governance record.** Only the facts no Microsoft tool holds.
4. **Record the answer as an architecture decision** (chapter 7), with the Microsoft page you checked and the date you checked it.
5. **Check the map again when Microsoft ships.** A new Microsoft surface, such as a new inventory or registry, can take over a job. When one does, move the job to it and retire your own part.

## Who decides (default decision rights)

| Decision or task | Accountable | Does the work | Consulted | AI may |
|---|---|---|---|---|
| Keep the map | Architect | Platform admin | Security reviewer, auditor | Draft the map and check it against Microsoft's pages |
| Answer a new fit question | Architect | Platform admin | Security reviewer, platform owner | Draft the decision record, with the pages it checked |
| Allow an exception that keeps a copy | Architect, with the security reviewer's agreement | Platform admin | Auditor | Draft the exception, with a review date |

## Guidelines this chapter applies

- **G1 Keep one register of business applications.** One record per business application, with its parts held as links, never copies.
- **G9 Release the tested artifact through your own pipeline.** Your pipeline deploys. The product is designed to record the deployment and to leave the pipeline to you.
- **G15 Keep an evidence pack, not assurances.** Evidence stays in the tool that made it, such as Purview or your pipeline, and the pack links to it.
- **G16 Record AI use on every app and agent.** Agents stay registered in Agent 365. The application record holds their business owner, AI use and decisions.

## Processes this chapter uses

- **P05 Deployment record.** Listen for the facts your own pipeline sends.
- **P06 Gap check and claim.** Read Microsoft's inventory live, as the admin who asks, and keep only the gaps.

## How the product is designed to support it

- It is designed to store each technical part as a link: a Microsoft ID, a URL and a last seen date.
- It is designed to run the gap check live, as the admin who asks, because Microsoft's inventory query accepts only delegated sign-in. It is designed with no background job and no stored copy.
- It is designed to take deployment facts from the last step of your own pipelines, and to leave the pipelines to you.
- It is designed to serve only its own governance tools on its MCP server. It is not designed as an agent registry or an MCP gateway.
- It is designed to show its own data in its own views, with no Power BI needed.
- It is designed to be deployed in your own Azure subscription, next to your Microsoft tools, not inside them.

## What you can tailor

- Which Microsoft tools you run, and which jobs each one covers in your tenant.
- Whether change management is built in or linked to your existing change tool.
- Which estates the gap check reads.
- Which link kinds you use for work, documents and evidence.

## Microsoft guidance it applies

- [Govern agents by risk](https://learn.microsoft.com/agents/center-of-excellence/govern-agents-risk), checked 2026-11-22: "Don't build a parallel governance universe for agents." Microsoft writes this about agents. This playbook applies the same rule to apps built with AI.
- [Architecture and governance for AI agents](https://learn.microsoft.com/azure/cloud-adoption-framework/ai-agents/architecture-governance) (Cloud Adoption Framework), checked 2026-11-22: "Organizations should extend their existing architecture and governance frameworks to support AI agents rather than establish separate systems." "Centralized visibility doesn't require central approval of every agent."
- [CoE Starter Kit transition to Power Platform admin center](https://learn.microsoft.com/power-platform/guidance/coe/starter-kit), checked 2026-11-22: "The Power Platform CoE Starter Kit is no longer actively maintained." Its core capabilities are part of the Power Platform admin center.
- [Power Platform inventory](https://learn.microsoft.com/power-platform/admin/power-platform-inventory), checked 2026-11-22: "Power Platform inventory gives tenant administrators a unified view of all agents, apps, flows, and sites built on Power Platform across their organization." It includes apps built in Copilot Managed Runtime. The inventory query supports only delegated user sign-in and returns HTTP 403 for service principals and managed identities.
- [Microsoft Copilot Managed Runtime overview for admins](https://learn.microsoft.com/microsoft-365/admin/manage/apps/?view=o365-worldwide), checked 2026-11-22: "Every app appears in the Microsoft 365 admin center with its owner, location, dependencies, usage, and health, whichever tool built it."
- [Environment groups](https://learn.microsoft.com/power-platform/admin/environment-groups), checked 2026-11-22: "When a rule is published at the environment group level, it's enforced across every environment within that group."
- [Overview of pipelines in Power Platform](https://learn.microsoft.com/power-platform/alm/pipelines), checked 2026-11-22: "the same solution artifact will be deployed" to each stage of a pipeline.
- [Agent Registry convergence with Microsoft Agent 365](https://learn.microsoft.com/entra/agent-id/agent-registry-convergence), checked 2026-11-22: "Microsoft Agent 365 is Microsoft's control plane for AI agents." Microsoft Entra continues to provide the identity foundation through Agent ID.
- [Overview of Microsoft Agent 365](https://learn.microsoft.com/microsoft-agent-365/overview), checked 2026-11-22: "Governance helps you make sure every agent has an owner, only the access it needs, and a record of what it does."
- [Learn about auditing solutions in Microsoft Purview](https://learn.microsoft.com/purview/audit-solutions-overview), checked 2026-11-22: Audit (Standard) logs and searches audited activities across most Microsoft services, and is on by default for organizations with the right subscription.
- [Incidents and alerts in the Microsoft Defender portal](https://learn.microsoft.com/defender-xdr/incidents-overview), checked 2026-11-22: the portal correlates related alerts into incidents that tell the full story of an attack.
- [Get started with approvals](https://learn.microsoft.com/power-automate/get-started-approvals) (Power Automate), checked 2026-11-22: approvers can respond from an Outlook email, a Microsoft Teams adaptive card or the Power Automate action center.
- [Connect to Dataverse with Model Context Protocol](https://learn.microsoft.com/power-apps/maker/data-platform/data-platform-mcp), checked 2026-11-22: Claude Code is one of the MCP clients that can connect to Microsoft's Dataverse MCP server.
- [What is Azure Boards?](https://learn.microsoft.com/azure/devops/boards/get-started/what-is-azure-boards), checked 2026-11-22: Azure Boards tracks work in work items, boards, backlogs and sprints.
- [Introduction to Application Insights](https://learn.microsoft.com/azure/azure-monitor/app/app-insights-overview), checked 2026-11-22: "Azure Monitor Application Insights is an application performance monitoring (APM) feature of Azure Monitor."

## Done when

- Every job on the map has its Microsoft tool and an owner.
- No copy of a Microsoft tool's data is kept on a schedule.
- Every exception that keeps a copy is a recorded decision with a review date.
- The map was checked against Microsoft's pages within its review cadence.
