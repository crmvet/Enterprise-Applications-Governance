# Chapter 20. The paved road for AI builders

- **Version:** 0.2 draft, 2026-11-22
- **Track:** IT
- **Parties:** IT (the platform team, the identity team, security), architects; the AI lead consulted
- **Guidelines:** [G7](../guidelines/G07-narrow-app-identities.md), [G9](../guidelines/G09-release-the-tested-artifact.md), [G14](../guidelines/G14-grant-access-through-groups.md), [G19](../guidelines/G19-review-anonymous-access.md), [G21](../guidelines/G21-give-ai-builders-a-paved-road.md)
- **Processes:** [P01](processes.md#p01-intake-and-triage), [P04](processes.md#p04-architecture-review), [P05](processes.md#p05-deployment-record)

## Why this chapter exists

"My AI client built the app in an afternoon. Then I waited a week for an environment, a connector and someone to deploy it." "We approve every environment request by hand, and the default environment still fills up." When the governed way is slower than the improvised way, builders improvise: the default environment, a personal account, a tool IT never approved. This chapter sets out IT's job in AI development: build the road once, so the safe way is also the fast way, and govern through the road instead of through tickets.

## How AI changes this

- Builders now arrive with a working app, not a request. A wait of days pushes the work to wherever nobody is watching.
- IT's value moves from doing each task by hand to shaping where and how the work is done: the environment a builder lands in, the template they start from, the pipeline they ship through and the identity each part runs as.
- The road must be readable by the AI client too. A rules file in each repository tells it to ask the product before it builds, and the answers name the template, the pipeline and the documents that apply (chapter 21).

## Old paradigm to avoid

**[2. IT as a ticket queue](17-old-paradigms-and-how-not-to-slide-back.md#2-it-as-a-ticket-queue).** Here it shows up as a ticket for every environment, connector, service account and deployment, each waiting for a person in IT. Build the road once and let builders serve themselves on it. The rules on the road and the tier do the governing. **Slide-back signal:** environment requests by ticket rise, apps are built in the default environment, or the time from intake to first build grows.

## What good looks like

- A new builder lands in a governed environment on their first visit, with no ticket.
- Every builder starts from a template, an approved connector or an approved MCP server, not from a blank page with unknown parts.
- Every repository holds a rules file that tells AI coding agents to ask the product before they build.
- Every app ships through a pipeline the organization owns. The same tested artifact moves through each stage, and the last step records the deployment.
- People sign in as themselves. Pipelines use workload identity federation, with no stored secrets. Agents have a Microsoft Entra agent identity with a human sponsor. Unattended apps have their own narrow identity.
- Each piece of the road has a named owner and a review date.
- No site built on the road goes public before its anonymous access is reviewed.

## The road, piece by piece

| Piece | What IT provides | The Microsoft tool that does the job | What the product is designed to do |
|---|---|---|---|
| Where to build | A personal environment for every maker, created on the first visit through environment routing, inside an environment group whose rules apply. Shared development, test and production environments for fusion and pro teams | Environment routing, environment groups and managed environments in the Power Platform admin center. For apps built with Copilot Managed Runtime, Microsoft's own personal environments and default environment group, with rules you review in the Microsoft 365 admin center or the Power Platform admin center | Record which environments an app lives in, as links. By design it never creates an environment or sets a policy |
| What to start from | Templates and starter apps, a pattern library, a vetted set of connectors and MCP servers, and a rules file for each repository | Connector policies as environment group rules. The Global Secure Access MCP firewall (preview) can allow or block MCP servers for devices that run the Global Secure Access client | Name the guideline behind each requirement it returns. Let review triggers be set so that an app built from an approved template at the lowest tier needs no design review |
| How to ship | Pipelines the organization owns. Build once, then promote the tested artifact through each stage. A last step that records the deployment | Power Platform Pipelines, Azure Pipelines with Power Platform Build Tools, GitHub Actions for Power Platform | Map which pipeline deploys each app. Take the deployment record from the pipeline's last step, and flag deployments that came another way. Publish the record steps as copy-paste samples. By design it never builds, hosts or runs a pipeline |
| Identities | People sign in as themselves. Pipelines use workload identity federation, with a separate identity per stage where privileges differ. Agents get a Microsoft Entra agent identity with a human sponsor. Unattended apps get their own narrow identity. Roles reach people only through groups | Microsoft Entra ID: groups, workload identity federation, managed identities and Microsoft Entra Agent ID | Record each app's identities and access approach on its record. Take its own roles only from Entra groups. Flag direct role assignments, expired workload grants and any grant to an agent identity |
| Which AI clients may connect | The organization's list of sanctioned AI clients (chapter 18), allowed in each client's managed settings and in Microsoft Entra consent | Microsoft Entra consent and app registrations. The Global Secure Access MCP firewall (preview) on the network | Run every call as the signed-in person, and record which AI client acted for whom. Which roles may call which tools is configuration |
| Where the answers come from | One place an AI client asks before it builds | None: these are governance facts that only the product is designed to hold. Microsoft's Dataverse MCP server stays the way to reach Dataverse tables | Answer, for the app in front of the builder, its tier, what the tier needs, its owners, what depends on it and which documents are missing (chapter 21) |

**One road, three paths.** The road carries the three builder paths the organization publishes in [chapter 18](18-who-may-build-with-ai-and-how.md), one per tier. Start small and widen each path as use grows. The personal path is the light one: it takes a lowest-tier app from a personal environment to its first users through self-service. The partnered path adds a shared environment, source control and a pipeline. The professional path routes a top-tier app through the design and security reviews its tier needs. Each path should still be faster than the way around it. Where it is not, fix the path.

## How it works

1. **Build the road once.** The platform owner sets up environment routing into environment groups, the template set, the approved connectors and MCP servers, the pipelines and the identity patterns. Start small: one path for each tier.
2. **Publish it at the front door.** When a builder registers an idea or an app (chapter 3), the answer names their path: where to build, what to start from and how to ship.
3. **Point every AI client at it.** Add a rules file to each template repository. It tells AI coding agents to call the product before they build, and to use the approved templates, connectors and pipelines.
4. **Keep each piece owned.** Every template, pattern, pipeline and identity pattern has a named owner and a review date. A piece nobody maintains drifts, and builders stop trusting it.
5. **Ship through the road.** Builders deploy through the organization's own pipeline. Its last step records the deployment with the pipeline's own workload identity.
6. **Review the road when a signal fires.** Environment tickets, apps in the default environment, or deployments flagged as another path mean the road is slower than the way around it. Fix the road, not the builder.

## Who decides (default decision rights)

| Decision or task | Accountable | Does the work | Consulted | AI may |
|---|---|---|---|---|
| Own the road and decide what is on it | Platform owner | Platform admin | Architect, security reviewer, AI lead | Draft the road description and list its gaps |
| Own templates and patterns | Architect | Platform admin, builders | AI lead, security reviewer | Draft a template or pattern for a person to review |
| Own identity patterns for people, agents and pipelines | Security reviewer | Identity team | Platform owner, AI lead | Draft the pattern for a person to review |
| Own a pipeline and its record step | Platform admin | Platform admin | Change manager | Draft the pipeline step for a person to review |
| Grant a pipeline's workload identity the deployment recorder role | Platform admin | Identity team | Security reviewer | Nothing |
| Approve an anonymous table permission before a site goes public | Data owner | Site owner | Security reviewer, platform admin | List the permissions |

## Guidelines this chapter applies

- **G21 Give AI builders a paved road.** Make the governed way to build the fastest way: governed environments without a ticket, templates and approved connectors and MCP servers to start from, pipelines the organization owns, and identities ready for people, agents and pipelines.
- **G7 Give each app identity its own narrow role.** Unattended apps, pipelines and agents each get their own identity, with the smallest role that works.
- **G9 Release the tested artifact through your own pipeline.** Build once, promote the same artifact, and end with a record step.
- **G14 Grant access through groups, review it, and keep a kill switch.** People reach roles only through groups, including the product's own roles.
- **G19 Review anonymous access before any site goes public.** The check is part of the road, so a site built in an afternoon still gets it.

## Processes this chapter uses

- **P01 Intake and triage.** The front door tells a builder which path they are on.
- **P04 Architecture review.** Review triggers are configuration, so an approved template at the lowest tier can mean no design review.
- **P05 Deployment record.** The record step at the end of the organization's own pipeline.

## How the product is designed to support it

- It is designed to tell a builder, for the app in front of them, its tier, what the tier needs and which path applies, through its own MCP server or its web app.
- It is designed to map which of your pipelines deploys each app, and to take the deployment record from the pipeline's last step. The pipeline signs in with its own workload identity, which holds the deployment recorder role through a recorded, expiring grant.
- It is designed to flag deployments that arrive another way, lack their approval or fall outside their window.
- It is designed to publish the record steps as copy-paste samples: an Azure Pipelines step, a GitHub Actions step and a Power Platform Pipelines extension. By design, it never builds, hosts or runs a pipeline, and never creates an environment.
- It is designed to take its own roles only from Microsoft Entra groups, and to flag direct role assignments, expired workload grants and any grant to an agent identity.
- It is designed to record each app's identities, environments and access approach as links on the application record.

## What you can tailor

- The paths on the road, and which tiers each path serves.
- The environment groups, their rules, and the routing rules that place each builder.
- Which templates, connectors and MCP servers count as approved.
- Which pipeline counts as the expected route into each environment.
- The identity patterns, and how long a workload grant lasts before it needs renewal.

## Microsoft guidance it applies

- [Build reusable assets and golden paths](https://learn.microsoft.com/agents/center-of-excellence/reusable-assets), checked 2026-11-22: "When the approved way to build an agent is slower than the improvised way, makers improvise." "A golden path is a proven and approved route to build and ship an agent. It's not a suggestion or a wiki page." "Name an owner for every golden path." Microsoft writes this about agents. This playbook applies the same rule to apps built with AI.
- [What is a personal environment?](https://learn.microsoft.com/power-platform/admin/personal-environments-overview), checked 2026-11-22: "Environment routing takes makers to their personal environment and creates it on their first visit, so they don't need to request an environment or choose where to build." "Rules that you set on an environment group apply to every personal environment in the group, and makers can't override them." Routing for Copilot Managed Runtime apps is always on.
- [Environment routing](https://learn.microsoft.com/power-platform/admin/default-environment-routing), checked 2026-11-22: "Environment routing offers makers a personal, safe space to build with Microsoft Dataverse without the fear of others accessing their apps or data."
- [Microsoft Copilot Managed Runtime overview for admins](https://learn.microsoft.com/microsoft-365/admin/manage/apps/?view=o365-worldwide), checked 2026-11-22: "Every app is governed from the moment it's created, with no extra setup required". App creation with the runtime's CLI and SDK is off by default. As checked, the page carries no preview label for the runtime itself; the Copilot Studio apps page it links to is marked preview.
- [Overview of pipelines in Power Platform](https://learn.microsoft.com/power-platform/alm/pipelines), checked 2026-11-22: "the same solution artifact will be deployed" to each stage. "This ensures customization can't bypass QA environments or your approval processes."
- [Workload identity federation](https://learn.microsoft.com/entra/workload-id/workload-identity-federation), checked 2026-11-22: of secrets and certificates, "These credentials pose a security risk and have to be stored securely and rotated regularly." GitHub Actions is one of the supported scenarios.
- [Development security implementation patterns](https://learn.microsoft.com/security/saf/disciplines/development/implementation-patterns), checked 2026-11-22: "Separate identities for build, test, and production deployment when their privileges differ."
- [What is Microsoft Entra ID Governance?](https://learn.microsoft.com/entra/id-governance/identity-governance-overview), checked 2026-11-22: "Every agent identity requires a human sponsor accountable for the agent's purpose and lifecycle decisions."
- [Best practices for Microsoft Entra Agent ID](https://learn.microsoft.com/entra/agent-id/best-practices-agent-id), checked 2026-11-22: "Don't create AI agents as plain app registrations or service principals outside the Agent ID framework."
- [Configure Global Secure Access MCP firewall to secure MCP traffic](https://learn.microsoft.com/entra/global-secure-access/how-to-configure-mcp-firewall), checked 2026-11-22: in preview. It can allow or block MCP servers by server URL, and tools per server, for traffic from devices that run the Global Secure Access client.

## Done when

- Every builder lands in a governed environment, has a template to start from, ships through a pipeline the organization owns, and has the identity they need, all without a ticket.
- Every piece of the road has a named owner and a review date.
- Every pipeline that records deployments uses its own workload identity under a recorded, expiring grant, with no stored secret where federation is available.
- Environment requests by ticket and apps built in the default environment are falling, or each has a recorded reason.
