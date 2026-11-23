# The playbook

- **Version:** 0.2 draft, 2026-11-22
- **Status:** design. The product it describes is in design, so every product line says what the product **is designed to** do.

**Govern AI development of your apps: people build with AI at speed, and every app stays owned, tiered and decided by a person.**

People now build working apps with AI in days, and governance built for slow builds either blocks them or gets skipped. This playbook governs AI development of the apps instead of slowing it down: the organization says who may build with AI, who decides and what each risk tier needs, and IT builds a paved road with identities for people, agents and pipelines. The product is designed to put governance inside the AI build loop, so the AI builder asks the governance questions while it builds, a person decides on an approval card, and the organization's own pipeline deploys.

Start with [chapter 0](00-govern-ai-development-of-the-apps.md).

## The four layers

The playbook covers every business application an organization runs in the Microsoft ecosystem: Power Platform apps, agents, apps built with AI tools, code apps and SaaS from other vendors. It brings seven parties into one way of working: the business, IT, AI, citizen developers, the PMO, change control and architects.

- **The playbook** (these chapters) says who the parties are, who decides what, and how work moves from idea to retirement.
- **The [guidelines](../guidelines/README.md)** are short numbered rules that the playbook and the product both cite.
- **The [processes](processes.md)** say what starts the work, who decides, and what is recorded.
- **The product** is designed to run the processes and keep their records. Where a Microsoft tool already does a job, it is designed to link to that tool, never to rebuild it.

## How each chapter is built

Every chapter starts with a header: its version, its track, the parties, the guidelines it applies and the processes it uses. The track says which group the chapter belongs to: Start here, the organization track, the IT track, or shared.

Then every chapter has the same parts: why it exists, what good looks like, how it works, who decides by default, the guidelines it applies, the processes it uses, how the product is designed to support it, what you can tailor, the Microsoft guidance it applies (with links and the date each was checked), and when it is done.

Every chapter except 0 and 17 has two more sections. Chapters 1 to 16 place them just before "Done when"; chapters 18 to 22 place them right after "Why this chapter exists":

- **How AI changes this:** what changes when people build apps with AI in days.
- **Old paradigm to avoid:** one of the ten old paradigms in [chapter 17](17-old-paradigms-and-how-not-to-slide-back.md), how it shows up in that chapter, and the signal that a team is sliding back to it. The signals in chapter 17 are the ones the quarterly check reviews; a chapter's own signal adds detail for that chapter.

Every decision right in a chapter is a default. Roles, tiers, stages, approvals and cadences are configuration that your organization sets (chapter 1).

## The chapters

### Start here

What governing AI development of the apps means, the old habits to avoid, and how to adopt the playbook.

| # | Chapter | Guidelines | Processes |
|---|---|---|---|
| 0 | [Govern AI development of the apps](00-govern-ai-development-of-the-apps.md) | G3, G17, G21, G22 | P01, P05, P10 |
| 17 | [Old paradigms, and how not to slide back](17-old-paradigms-and-how-not-to-slide-back.md) | G1, G3, G7, G9, G12, G14, G16, G17, G20, G21, G22, G23 | P01, P03, P05, P06, P07, P10 |
| 1 | [How to adopt and tailor this playbook](01-adopt-and-tailor.md) | G1, G2, G3, G4, G5, G6, G7, G8, G9, G12, G13, G14, G15, G16, G17, G18, G19, G20, G21, G22, G23, G24 | P10, P11 |

### Organization track

For leaders and sponsors, business owners, the AI lead, the PMO and change control: who may build with AI, who decides, what each tier needs, and whether AI app development is working.

Suggested order: start with chapter 18, then 2, 4, 3, 6, 19, 10 and 14. Then read the shared chapters.

| # | Chapter | Guidelines | Processes |
|---|---|---|---|
| 2 | [Parties, roles and decision rights](02-parties-roles-and-decision-rights.md) | G2, G4, G14 | P08, P11 |
| 3 | [The front door: intake and triage](03-front-door-intake-and-triage.md) | G1, G2, G3, G16, G22 | P01, P02, P06, P10 |
| 4 | [Risk tier and what each tier needs](04-risk-tier.md) | G3, G4, G12, G20 | P02, P04, P07 |
| 6 | [Portfolio and demand](06-portfolio-and-demand.md) | G4, G5, G18, G24 | P01, P09 |
| 10 | [Emergencies and exceptions](10-emergencies-and-exceptions.md) | G4, G12, G13 | P03, P05 |
| 14 | [Retirement](14-retirement.md) | G4, G6, G18 | P03, P06, P09 |
| 18 | [Who may build with AI, and how](18-who-may-build-with-ai-and-how.md) | G2, G3, G16, G17, G23 | P01, P02, P10, P11 |
| 19 | [Measure AI app development](19-measure-ai-app-development.md) | G1, G2, G4, G24 | P01, P03, P05, P06, P08 |

### IT track

For the platform team, architects and security: where AI builders build, identities for people, agents and pipelines, how AI clients reach the product, evidence, and which Microsoft tool does which job.

Suggested order: start with chapter 20, then 21, 16, 13, 22, 8, 7 and 15. Then read the shared chapters.

| # | Chapter | Guidelines | Processes |
|---|---|---|---|
| 7 | [Architecture review and decisions](07-architecture-review-and-decisions.md) | G4, G6, G20 | P03, P04, P07 |
| 8 | [Build paths: citizen, fusion, pro and AI-built, and graduation](08-build-paths-and-graduation.md) | G3, G7, G8, G17, G19, G21, G23 | P01, P02, P08, P10 |
| 13 | [Data owners and access](13-data-owners-and-access.md) | G7, G8, G14, G19 | P02, P04, P09 |
| 15 | [Evidence and audit](15-evidence-and-audit.md) | G4, G9, G15, G17 | P03, P05, P10, P11 |
| 16 | [Working with the product through MCP clients, pages and approval cards](16-mcp-clients-pages-and-cards.md) | G8, G17, G22 | P05, P06, P10, P11 |
| 20 | [The paved road for AI builders](20-the-paved-road-for-ai-builders.md) | G7, G9, G14, G19, G21 | P01, P04, P05 |
| 21 | [Governance inside the AI build loop](21-governance-inside-the-ai-build-loop.md) | G6, G8, G17, G20, G22 | P01, P02, P05, P07, P10 |
| 22 | [Which Microsoft tool does which job](22-which-microsoft-tool-does-which-job.md) | G1, G9, G15, G16 | P05, P06 |

### Shared (both)

Owned by both sides: one register, change and release, the AI rules, and operate and review.

| # | Chapter | Guidelines | Processes |
|---|---|---|---|
| 5 | [One register of business applications](05-one-register.md) | G1, G2, G5, G6, G20 | P01, P06, P07 |
| 9 | [Change control and release](09-change-control-and-release.md) | G6, G9, G12, G13, G17 | P03, P05 |
| 11 | [AI agents, AI-built apps, and AI inside the process](11-ai-agents-and-ai-built-apps.md) | G8, G16, G17, G23 | P02, P06, P08, P10, P11 |
| 12 | [Operate and review](12-operate-and-review.md) | G2, G3, G14, G20 | P02, P07, P08 |

## The diagrams

Three diagrams go with the playbook. Each picture is in `img/`, and its Visio source is in `visio/`.

- **Old paradigm, new practice.** The ten old paradigms on one page, each with its new practice and its slide-back signal (chapter 17).
- **Two views, one record.** The organization's view and IT's view around one application record, with the ways in above it and Microsoft's tools below it (chapter 0).
- **The governed AI build loop.** One change from the ask to the record: where the AI drafts, where a person decides, where your own pipeline deploys, and where the product is designed to record the deployment (chapters 0 and 21).

## The processes

| ID | Process |
|---|---|
| P01 | [Intake and triage](processes.md#p01-intake-and-triage) |
| P02 | [Tier assessment and required approvals](processes.md#p02-tier-assessment-and-required-approvals) |
| P03 | [Change management](processes.md#p03-change-management) |
| P04 | [Architecture review](processes.md#p04-architecture-review) |
| P05 | [Deployment record](processes.md#p05-deployment-record) |
| P06 | [Gap check and claim](processes.md#p06-gap-check-and-claim) |
| P07 | [Documentation tracking](processes.md#p07-documentation-tracking) |
| P08 | [Owner leaves](processes.md#p08-owner-leaves) |
| P09 | [Retirement](processes.md#p09-retirement) |
| P10 | [AI draft and human accept](processes.md#p10-ai-draft-and-human-accept) |
| P11 | [Configuration change](processes.md#p11-configuration-change) |

## Outside frameworks

Outside frameworks appear only as "maps to": a related idea, never a claim of compliance. Where this playbook uses the words standard, normal and emergency change, change authority or architecture board, they are common terms, not a claim to follow any one framework. "Paved road" is the playbook's own term. Microsoft's guidance speaks of [golden paths](https://learn.microsoft.com/agents/center-of-excellence/reusable-assets) and guardrails.

Microsoft, Azure, Azure DevOps, Copilot, Copilot Studio, Dataverse, Defender, Entra, Microsoft 365, Microsoft Agent 365, Microsoft Entra Agent ID, Outlook, Power Apps, Power Automate, Power Pages, Power Platform, Purview and Teams are trademarks of the Microsoft group of companies. GitHub and GitHub Copilot are trademarks of GitHub, Inc. Claude and Claude Code are trademarks of Anthropic. Framework names belong to their publishers.

The diagrams in `img/` use Microsoft's official product icons, unchanged. Those icons stay under Microsoft's terms, not under this repository's license.
