# Chapter 0. Govern AI development of the apps

- **Version:** 0.2 draft, 2026-11-22
- **Track:** Start here
- **Parties:** all seven: the business, IT, AI, citizen developers, the PMO, change control and architects
- **Guidelines:** [G3](../guidelines/G03-set-a-risk-tier.md), [G17](../guidelines/G17-ai-drafts-people-approve.md), [G21](../guidelines/G21-give-ai-builders-a-paved-road.md), [G22](../guidelines/G22-let-the-ai-builder-ask-before-it-builds.md)
- **Processes:** [P01](processes.md#p01-intake-and-triage), [P05](processes.md#p05-deployment-record), [P10](processes.md#p10-ai-draft-and-human-accept)

## The keynote

**Govern AI development of your apps: the product is designed so people can build with AI while each app stays owned, tiered and decided by a person.**

People now build working apps with AI in days. Most governance was built for slow builds: one board for every change, a ticket queue, a review at the end and a yearly inventory spreadsheet. Against AI builders it either blocks them or gets skipped. This playbook is written to govern AI development of the apps, not to slow it down. It gives the organization a clear say in who may build with AI, who decides, and what each risk tier needs. It gives IT a paved road: environments, templates, pipelines the organization owns, and identities for people, agents and pipelines. The product is designed to put governance inside the AI build loop. Its own MCP server is designed so the AI builder, in any MCP client, can ask the governance questions while it builds: the risk tier, the required documents, the owners, the dependencies, and what needs a person's decision. The product's demo uses Claude Code. AI drafts and people decide, on the decision page in the web app; Teams and Outlook notices are designed to link to it. The organization's own pipelines deploy, and the product is designed to record the deployments they report. Where a Microsoft tool already does a job, the product is designed to link to it, never to rebuild it.

## Why this chapter exists

"By the time we review it, people already use it." "We blocked the tool, and they built it somewhere else." AI changed how fast apps get built. It did not change what every app needs: an owner, a risk tier, and a person who decides. Governance built for slow builds fails both ways at the new speed. It blocks builders, or builders skip it.

This chapter says what governing AI development of the apps means. It says what the playbook and the product are designed to enable, from the organization's view and from IT's view. It sets three rules that never bend, and it tells you how to read the rest of the playbook.

## What it is designed to enable

| Side | Who | What it is designed to enable | Where the playbook covers it |
|---|---|---|---|
| The organization | Leaders and sponsors, business owners, the AI lead, the PMO, change control | Builders keep their speed, and every app keeps an owner, a tier and a person who decides. A published answer to who may build with AI and on which path. One accountable role per decision. Review weight set by the tier, not by habit. Decisions on the decision page, not in meetings. Adoption measured by flow and coverage, not by apps shipped | [Organization track](README.md#organization-track): chapters 2, 3, 4, 6, 10, 14, 18 and 19 |
| IT | The platform team, architects, security | Self-service instead of tickets. A paved road builders use by themselves. Identities for people, agents and pipelines. Governance answers inside the AI client. Evidence as a query, not a project. A clear map of which Microsoft tool does which job | [IT track](README.md#it-track): chapters 7, 8, 13, 15, 16, 20, 21 and 22 |
| Both | Every party | One application record every party points to. Change and release across every tool. One set of AI rules. Operate and review on a cadence | [Shared](README.md#shared-both): chapters 5, 9, 11 and 12 |

## Two views, one record

The organization and IT look at the same apps and ask different questions.

| The organization's view | IT's view |
|---|---|
| Who may build with AI? | Where do AI builders build? |
| Who decides? | Which identity: a person, an agent or a pipeline? |
| What does each tier need? | How do AI clients reach the product? |
| Is AI app development working? | Where is the evidence? |
| | Which Microsoft tool does which job? |

Both views point to **one application record** per business application, however it was built. The product is designed to hold on that record:

- the owners and their deputies;
- the risk tier and what it needs;
- the lifecycle stage;
- the dependencies;
- the required documents and their status;
- the decisions, which no one edits once they are final;
- the changes and the deployments;
- the links to the Microsoft tools that hold each technical part.

This is application portfolio management, kept as the record underneath, not as the headline.

The product is designed so people reach the record two ways: MCP clients (the product's demo uses Claude Code) and the web app. Each is designed to act as the signed-in person, so the person's own rights are the limit. Notices in Outlook and Teams are designed to link to the decision page; they are not a way in. The organization decides on the decision page in the web app. IT links, maps and records.

Below the record, Microsoft's tools keep their jobs: the Power Platform admin center for [inventory](https://learn.microsoft.com/power-platform/admin/power-platform-inventory) and environments, Microsoft Entra for groups and [agent identities](https://learn.microsoft.com/entra/agent-id/best-practices-agent-id), [Microsoft Agent 365](https://learn.microsoft.com/microsoft-agent-365/overview) for the agent registry, your pipelines (Power Platform Pipelines, Azure Pipelines or GitHub Actions), your work tools (Azure Boards, Planner or GitHub), Microsoft Purview for [audit](https://learn.microsoft.com/agents/center-of-excellence/govern-agents-risk), and [Microsoft Defender](https://learn.microsoft.com/defender-xdr/security-for-ai/ai-agent-inventory) for security. The record is designed to link to them, never to copy them. [Chapter 22](22-which-microsoft-tool-does-which-job.md) maps each job to its tool.

The diagram "Two views, one record" shows this on one page.

![Diagram: Two views, one record. Each side asks its own questions, and both get their answers from one application record. At the top, two ways in reach the record as the signed-in person: MCP clients (the product's demo uses Claude Code; any MCP client is designed) and the web app in a browser. Beside them, in a dashed box marked designed, notices in Outlook and Teams are designed to link to the decision page; no arrow joins them to the record. On the left, the organization's view: leaders and sponsors, business owners, the AI lead, the PMO and change control ask who may build with AI, who decides, what each tier needs and whether AI app development is working, and they decide on the decision page. On the right, IT's view: the platform team, architects and security ask where AI builders build, which identity acts (a person, an agent or a pipeline), how AI clients reach the product, where the evidence is and which Microsoft tool does which job, and IT links, maps and records. In the middle, the record, one per business application of any kind, holds the owners and deputies, the risk tier and what it needs, the lifecycle stage, the dependencies, the required documents and their status, the decisions, final ones never change, the changes and deployments, and links to Microsoft tools. Each side and the record carry their playbook chapters. Below, Microsoft's tools keep their jobs: the Power Platform admin center, Microsoft Entra, Microsoft Agent 365, the pipelines, work tracking, Microsoft Purview and Microsoft Defender. The record links to them, not copies them, and links to Agent 365, Purview and Defender are designed. The footer gives the status on 27 November 2026: built and tested; runs as a demo install with fictional data; no release yet. Not a Microsoft product.](img/playbook-two-perspectives.png)

## The governed AI build loop

One change, from the ask to the record. Each step says who acts.

| Step | Who acts | What happens |
|---|---|---|
| 1. A person asks | A person | A builder asks an AI client for a new app or a change, and describes the need. |
| 2. The AI client asks first | The AI client | Before it builds, the AI client calls the product's own MCP server, as the signed-in person. The product's demo uses Claude Code; other MCP clients are designed to work the same way. |
| 3. The answers come back | The product | It is designed to answer with the proposed tier and why, what the tier needs, the owners and deputies, what depends on the app, the missing documents, and what needs a person. The answers shape the build. |
| 4. The AI drafts | The AI client | It builds the app or the change in your repository. It drafts the intake or the change request in the product, where it starts as a draft. It writes the documents in your own tools and links them. |
| 5. A person decides | A person | The holder of the accountable role answers on the decision page in the web app; a notice in Outlook or Teams is designed to link to it. The AI client submits a request, never an approval, and it cannot answer. After a rejection, a person can revise the work and propose it again. |
| 6. Your pipeline deploys | Your own tools | Power Platform Pipelines, Azure Pipelines or GitHub Actions build once and promote the tested artifact. The product is designed never to build, host or run them. |
| 7. The deployment is recorded | Your pipeline and the product | A last step in your pipeline, under the pipeline's own workload identity, calls the product's record endpoint. It does not go through the MCP server. The product is designed to record what was deployed, where, when and by which pipeline, and to flag a deployment that came another way, lacks an approval or falls outside its window. |

Then the loop starts again. Before the next change, the builder can ask the AI client "what needs me?".

By design, two things are never in the loop: an AI approval and a copy of Microsoft's inventory.

The diagram "The governed AI build loop" shows the seven steps as one loop.

![Diagram: The governed AI build loop, seven steps in a loop, from the ask to the record: where the AI drafts, where a person decides, where your own pipeline deploys and where the deployment goes on the product's record. 1, a person asks: a builder asks an AI client for an app or a change and describes the need. 2, the AI client asks first: it calls the product before it builds, as the signed-in person; the demo uses Claude Code, and any MCP client is designed. 3, the product answers with the proposed tier and why, what the tier needs, the owners and deputies, what depends on the app, the missing documents and what needs a person, and the answers shape the build. 4, the AI drafts the app or the change in your repository, the intake or the change as a draft, and the documents in your own tools, linked; it submits a request, never an approval. 5, a person decides on the decision page in the web app; Teams and Outlook notices are designed to link to it, and the AI cannot answer it. The approver is a named person in the accountable role. A dashed arrow leads from step 5 back to step 4: after a rejection, a person can revise the work and propose it again. 6, once approved, your own pipeline deploys: Power Platform Pipelines, Azure Pipelines or GitHub Actions build once and promote the tested artifact. 7, the product's record: a last pipeline step reports under the pipeline's own workload identity, and the record holds what was deployed, where, when and by which pipeline, with flags for another path, a missing approval or a deploy outside its window. A dashed arrow leads from step 7 back to step 1: for the next change, what needs me? In the middle, in four short lines: AI drafts, people decide, your pipeline deploys, and the product's part is to record. A note at the bottom right says that an AI approval and a copy of Microsoft's inventory are never in the loop. The footer gives the status on 27 November 2026: built and tested; runs as a demo install with fictional data; no release yet. Not a Microsoft product.](img/playbook-ai-build-loop.png)

[Chapter 21](21-governance-inside-the-ai-build-loop.md) covers the loop in detail.

## Three rules that never bend

1. **AI drafts and recommends; a named person decides.** Only a person's answer counts, given by default on the decision page in the web app. A pre-authorized decision, such as a standard change on an approved model, counts only because a named person approved that rule on record. No AI approves anything, including its own work.
2. **The tier, not the tool, sets the weight.** An app built with AI in a day gets the review its tier needs, no more and no less. Build effort is no longer a sign of risk.
3. **Links, not copies.** Microsoft's tools keep their jobs. The product is designed to link to them, listen for the facts they send, or read them live when an admin asks.

## What good looks like

- Every party knows its track and the chapter it starts with.
- Builders get a governed place to build, a template to start from and a pipeline to ship through, without filing a ticket.
- Before an AI client builds or changes an app, it asks the governance questions, and the answers shape the build.
- Every app, however it was built, has an owner, a tier and a register entry from the day the idea arrives.
- Every decision has a named person behind it: an answer, by default on the decision page, or a rule that person approved on record. No approval is made by an AI alone.
- Every production deployment comes through the organization's own pipeline and is on record.
- Nothing a Microsoft tool already holds is copied. Each technical part is a link.

## Who decides (default decision rights)

| Decision | Accountable | Does the work | Consulted | AI may |
|---|---|---|---|---|
| Adopt the keynote, the two tracks and the three rules | Sponsor | Platform owner, with the AI lead | Every party lead | Draft the adoption plan |
| Put the paved road and the build loop on the adoption plan | Platform owner | Platform admin | AI lead, architect, change manager, security reviewer | Draft the plan and list the gaps |

Every decision right is a default. [Chapter 1](01-adopt-and-tailor.md) shows how to change it.

## Guidelines this chapter applies

- **G3 Set a risk tier for every app, on tiers you define.** The tier, not the tool or the build effort, sets how much review an app needs.
- **G17 Let AI draft and recommend; people approve.** The AI client drafts and submits. A person decides on the decision page.
- **G21 Give AI builders a paved road.** Make the governed way to build the fastest way: environments without a ticket, templates, approved connectors and MCP servers, pipelines the organization owns, and identities ready to use.
- **G22 Let the AI builder ask before it builds.** The AI client asks for the tier, what the tier needs, the owners, the dependants and the missing documents before it builds or changes an app.

## Processes this chapter uses

- **P01 Intake and triage.** The front door, where the tier is set when the idea arrives.
- **P10 AI draft and human accept.** What the AI client may draft, how its rows are marked, and which channel counts as a person's acceptance.
- **P05 Deployment record.** The step that closes the loop: a last step in the organization's own pipeline reports the deployment.

## How the product is designed to support it

- Its own MCP server is designed to answer the governance questions inside the build, for any MCP client; the product's demo uses Claude Code. It is designed to act as the signed-in person.
- It is designed so that only a person's answer counts as a decision, given by default on the decision page in the web app. Notices in Outlook and Teams are designed to link to that page. A pre-authorized decision, such as a standard change, is designed to link the rule a person approved. No tool an AI client is served approves, rejects or accepts; the one answer tool is served only to a person in the web app.
- It is designed to take a deployment record from the last step of the organization's own pipeline, under that pipeline's own workload identity, and to flag deployments that came another way. It is designed never to build, host or run a pipeline.
- It is designed to hold each technical part as a link to the Microsoft tool that owns it: a Microsoft ID, a URL and a last seen date.
- It is designed to keep tiers, approvals, required documents and which roles may call which tools as configuration, so the organization changes the rules without a rebuild.
- Its first release is designed to run in the organization's own Azure subscription.

## What you can tailor

- The tiers and what each tier needs ([chapter 4](04-risk-tier.md)).
- The decision rights, and who holds each role ([chapter 2](02-parties-roles-and-decision-rights.md)).
- Who may build with AI, on which path, and which AI clients you sanction ([chapter 18](18-who-may-build-with-ai-and-how.md)).
- Which product tools each role may call from an AI client ([chapter 16](16-mcp-clients-pages-and-cards.md)).
- The pieces of your paved road ([chapter 20](20-the-paved-road-for-ai-builders.md)).

## Microsoft guidance it applies

Most of this guidance is written for agents. This playbook applies the same rules to apps built with AI. Microsoft does not say them about apps.

- [Govern agents by risk](https://learn.microsoft.com/agents/center-of-excellence/govern-agents-risk), checked 2026-11-22: "Match the depth of governance to the risk each agent poses to enable low-risk work quickly, and reserve heavier controls for agents that can cause real harm." For the lowest tier: "Keep the path light so people can build and adopt without waiting on a committee."
- [Govern agents by risk](https://learn.microsoft.com/agents/center-of-excellence/govern-agents-risk), checked 2026-11-22: "Don't build a parallel governance universe for agents." On audit: "Microsoft Purview provides audit of agent actions to support this record."
- [Build reusable assets and golden paths](https://learn.microsoft.com/agents/center-of-excellence/reusable-assets), checked 2026-11-22: "Teams move faster when the right way to build is also the easy way." And: "When the approved way to build an agent is slower than the improvised way, makers improvise."
- [Design an agent front door for your Center of Excellence](https://learn.microsoft.com/agents/center-of-excellence/agent-front-door), checked 2026-11-22: "Designated reviewers make approval decisions, and every build must meet the release requirements for its risk tier."
- [Architecture and governance for AI agents](https://learn.microsoft.com/azure/cloud-adoption-framework/ai-agents/architecture-governance) (Cloud Adoption Framework), checked 2026-11-22: "Centralized visibility doesn't require central approval of every agent." And: "Microsoft Agent 365 provides a common management experience for Microsoft agents and agents from supported third-party platforms."
- [Discover AI agents and assess security posture using Microsoft Defender](https://learn.microsoft.com/defender-xdr/security-for-ai/ai-agent-inventory), checked 2026-11-22: "Microsoft Defender provides a centralized inventory of AI agents in your organization and assesses their security posture."
- [CoE Starter Kit transition to the Power Platform admin center](https://learn.microsoft.com/power-platform/guidance/coe/starter-kit), checked 2026-11-22: "Use the Inventory experience to view and govern all apps, flows, and agents created across your tenant."
- [Connect to Dataverse with Model Context Protocol](https://learn.microsoft.com/power-apps/maker/data-platform/data-platform-mcp), checked 2026-11-22: Microsoft already names these AI tools among the clients of its own MCP servers: "various MCP clients like Copilot Studio agents, Visual Studio (VS) Code GitHub Copilot, GitHub Copilot CLI, Claude desktop, Claude Code, and many others."
- [Develop a tenant environment strategy](https://learn.microsoft.com/power-platform/guidance/adoption/environment-strategy), checked 2026-11-22: "Pipelines in Power Platform provides a simplified process for promoting assets from development to test and production environments, making continuous integration and deployment (CI/CD) available to all makers."
- [Workload identity federation](https://learn.microsoft.com/entra/workload-id/workload-identity-federation) (Microsoft Entra), checked 2026-11-22: "You can use workload identity federation in scenarios such as GitHub Actions, workloads running on Kubernetes, or workloads running in compute platforms outside of Azure."

## How to read this playbook

- **Four groups.** The [chapter index](README.md) puts the chapters in four groups: Start here (chapters 0, 17 and 1), the organization track, the IT track, and the shared chapters that both sides own.
- **A Track line in every header.** It says which group a chapter belongs to.
- **Two sections in every chapter except 0 and 17.** Right after "Why this chapter exists", each chapter has "How AI changes this" and "Old paradigm to avoid". The second one names one of the ten old paradigms in [chapter 17](17-old-paradigms-and-how-not-to-slide-back.md) and the signal that a team is sliding back to it.
- **Reading order.** Read chapter 17 next, then chapter 1, then your track. Each track in the [chapter index](README.md) gives a suggested order and the chapter to start with.
- **Product lines describe the design.** The product's status: built and tested; runs as a demo install with fictional data; no release yet. The chapters describe its design, so each product line says what the product "is designed to" do.

## Done when

- The sponsor, the platform owner and the AI lead have agreed the keynote and the three rules.
- Every party knows which track it reads and which chapter it starts with.
- The paved road ([chapter 20](20-the-paved-road-for-ai-builders.md)) and the governed build loop ([chapter 21](21-governance-inside-the-ai-build-loop.md)) are on the adoption plan, each with an owner and a date.
- The team has run the [quarterly slide-back check](17-old-paradigms-and-how-not-to-slide-back.md#the-quarterly-slide-back-check) once, as a baseline.
