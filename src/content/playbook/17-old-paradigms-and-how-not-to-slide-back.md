# Chapter 17. Old paradigms, and how not to slide back

- **Version:** 0.2 draft, 2026-11-22
- **Track:** Start here
- **Parties:** all seven: the business, IT, AI, citizen developers, the PMO, change control and architects; the platform owner and the AI lead watch the signals
- **Guidelines:** [G1](../guidelines/G01-keep-one-register.md), [G3](../guidelines/G03-set-a-risk-tier.md), [G7](../guidelines/G07-narrow-app-identities.md), [G9](../guidelines/G09-release-the-tested-artifact.md), [G12](../guidelines/G12-approve-production-changes-on-record.md), [G14](../guidelines/G14-grant-access-through-groups.md), [G16](../guidelines/G16-record-ai-use.md), [G17](../guidelines/G17-ai-drafts-people-approve.md), [G20](../guidelines/G20-keep-required-documents-current.md), [G21](../guidelines/G21-give-ai-builders-a-paved-road.md), [G22](../guidelines/G22-let-the-ai-builder-ask-before-it-builds.md), [G23](../guidelines/G23-say-who-may-build-with-ai.md)
- **Processes:** [P01](processes.md#p01-intake-and-triage), [P03](processes.md#p03-change-management), [P05](processes.md#p05-deployment-record), [P06](processes.md#p06-gap-check-and-claim), [P07](processes.md#p07-documentation-tracking), [P10](processes.md#p10-ai-draft-and-human-accept)

## Why this chapter exists

"We tried the new way for a quarter, then the board started meeting weekly again." Under pressure, teams fall back on what they know. Most of what they know was built for slow builds. When people build apps with AI in days, those old habits either block the builders or get skipped, and both outcomes leave apps with no owner, no tier and no decision on record.

This chapter names ten old paradigms. For each one it says why it fails when people build with AI, what to do instead, how the product is designed to help, and the signal that a team is sliding back. Naming the habit, and watching its signal, keeps the new practice in place.

## How to use this chapter

- **Five parts per paradigm.** Each paradigm below has the old practice, why it fails when people build with AI, the new practice, how the product is designed to support it, and the slide-back signal. Each also gives the Microsoft source behind it and the chapters that fix it.
- **Stable headings.** Every paradigm keeps its number and heading, so other chapters link to it. Teams can name a habit by its number: "this is paradigm 4 again".
- **Microsoft sources.** Every Microsoft source in this chapter was checked on 2026-11-22. Most of this guidance is written for agents. The playbook applies the same rules to apps built with AI. Where Microsoft keeps a gate or differs from the playbook, the paradigm says so.
- **Review the signals.** Look at the signals at every portfolio review ([chapter 6](06-portfolio-and-demand.md)), next to the measures in [chapter 19](19-measure-ai-app-development.md). Run the [quarterly slide-back check](#the-quarterly-slide-back-check) at the end of this chapter once a quarter.

The diagram "Old paradigm, new practice" shows all ten on one page.

![Diagram: Old paradigm, new practice. Ten rows, each with the old paradigm, how the product is designed to help, the new practice and the slide-back signal. 1, one board approves every change, becomes weight by tier with the board for exceptions. 2, IT as a ticket queue, becomes builders self-serve on a paved road. 3, the yearly inventory spreadsheet, becomes register at intake and check live. 4, governance checked only at the end, becomes ask at the idea and while building. 5, the standards document nobody reads, becomes golden paths and answerable rules. 6, ban AI tools until a policy exists, becomes say who may build and on which path. 7, rebuild what Microsoft already ships, becomes link to the Microsoft tool. 8, let the tool decide, becomes AI drafts and a named person decides. 9, documentation as a phase after the build, becomes the builder's AI writes docs with the change. 10, shared accounts and stored secrets, becomes identities for people, agents and pipelines.](img/playbook-paradigm-shift.png)

### 1. One board approves every change

- **Old practice:** one change board meets weekly and approves every production change, whatever its size or risk.
- **Why it fails when people build with AI:** AI multiplies small changes. A weekly meeting becomes the bottleneck. Teams then skip it, batch changes into bigger and riskier releases, or label routine changes as emergencies to get through. Reviewers who see every request also start to approve without reading.
- **New practice:** weight the review by the tier (G3). Standard change models are pre-authorized: a routine change moves on a model a person approved once. The business owner authorizes normal changes at the lower tiers, and the change manager at the top tier. Every production change is still approved on record, never by the person who started it (G12). The board handles exceptions, and windows that cover several applications (P03).
- **How the product is designed to support it:** tiers, approval rules, change types and gate levels are designed as configuration records. The single most specific approval rule is designed to win. A standard change check is designed to say why a change does not fit a model. Approval cards are designed to go to the person who holds the accountable role, not to a meeting.
- **Slide-back signal:** lower-tier normal changes wait for a meeting. The share of standard changes falls. The share of emergency changes rises.
- **Where Microsoft keeps a gate:** Microsoft keeps gates where reach and risk grow, such as a formal release gate for medium-risk agents and a security review for high-risk ones. The old paradigm is one gate for everything, not every gate.
- **Microsoft source:** [Govern agents by risk](https://learn.microsoft.com/agents/center-of-excellence/govern-agents-risk): "A single governance checklist applied to every agent fails in both directions." [Agentic transformation patterns](https://learn.microsoft.com/agents/adoption-patterns/): "A CoE isn't a committee that meets monthly, a set of approvals that slow you down, or a silo that hoards control." [Operational standards for AI agents](https://learn.microsoft.com/azure/cloud-adoption-framework/ai-agents/operational-standards): "Standards should require evaluation proportional to the potential impact rather than mandate full testing for every change." [From safety to controllability in enterprise AI](https://learn.microsoft.com/startups/build/enterprise-readiness/ai-guardrails/safety-to-controllability-enterprise-ai): "If every action requires approval, reviewers become a bottleneck and might approve requests without reading them."
- **Fix it in:** [chapter 4](04-risk-tier.md) (the tier), [chapter 9](09-change-control-and-release.md) (change types and standard models), [chapter 10](10-emergencies-and-exceptions.md) (a real emergency path).

### 2. IT as a ticket queue

- **Old practice:** builders file a ticket for an environment, a connector, a service account or a deployment, and wait for IT to do it.
- **Why it fails when people build with AI:** an AI builder has a working app in a day. A wait of days pushes the work to wherever nobody is watching: the default environment, a personal account, or a tool IT never approved.
- **New practice:** IT builds the paved road once and governs through the rules on the road and the tier (G21). The road holds personal environments in governed environment groups, templates, approved connectors and MCP servers, pipelines the organization owns, and identities ready to use. Builders serve themselves. Registration still happens at the front door (P01), and deployments still go through the organization's pipeline (P05).
- **How the product is designed to support it:** intake from any MCP client, as the signed-in person. Lowest-tier intake accepted under a rule a person approved once. "What does my tier need?" answered for the app in front of the builder. Pipeline record steps published as copy-paste samples for the organization's own pipelines. By design, the product never builds environments or pipelines.
- **Slide-back signal:** environment requests by ticket rise. Apps are built in the default environment. The time from intake to first build grows.
- **Where Microsoft keeps a gate:** Microsoft still suggests that only admins create new production environments ([Develop a tenant environment strategy](https://learn.microsoft.com/power-platform/guidance/adoption/environment-strategy)): "If users have to request environments from central IT, it's easier to see what people are working on if admins are the gatekeepers." That gate is fine for production. Routine building does not wait on it, because environment routing gives each maker a governed personal environment.
- **Microsoft source:** [What is a personal environment?](https://learn.microsoft.com/power-platform/admin/personal-environments-overview): "Environment routing takes makers to their personal environment and creates it on their first visit, so they don't need to request an environment or choose where to build." Same page: "Rules that you set on an environment group apply to every personal environment in the group, and makers can't override them." [Pillar 5: Organization and culture](https://learn.microsoft.com/agents/adoption-maturity-model/maturity-model-readiness): an effective CoE "Operates as a service and enablement hub, not a bottleneck." [Build an agentic Center of Excellence](https://learn.microsoft.com/agents/center-of-excellence/) names the gatekeeper failure mode: "Reviews everything, enables nothing, and becomes the bottleneck. Teams route around it and build shadow agents."
- **Fix it in:** [chapter 20](20-the-paved-road-for-ai-builders.md) (the paved road), [chapter 8](08-build-paths-and-graduation.md) (build paths and graduation).

### 3. The yearly inventory spreadsheet

- **Old practice:** once a year someone exports every app, flow and agent to a spreadsheet and chases owners by email.
- **Why it fails when people build with AI:** AI-built apps and agents appear every week. The list is stale on the day it is finished. It copies weak owner fields. Abandoned apps stay, because nobody looks again.
- **New practice:** register at the front door, one record per business application (G1). Link each technical part; never copy it. An admin runs a live gap check on a set cadence, and likely owners claim what it finds (P06). Review depth can still follow the tier: a yearly review of a low-risk app is fine. A yearly spreadsheet as the only inventory is not.
- **How the product is designed to support it:** one record per business application of any kind. Each technical part held as a Microsoft ID, a URL and a last seen date. A gap check designed to read Microsoft's inventory live, as the admin who asks, with no background job and no stored copy. Claims for what the gap check finds, and a flag for parts that vanished.
- **Slide-back signal:** the inventory is exported to a spreadsheet "to reconcile". The last gap check is older than its cadence. Open claims grow.
- **Where Microsoft allows a yearly rhythm:** the Cloud Adoption Framework ([Guidance to set up your organization's AI governance process](https://learn.microsoft.com/azure/cloud-adoption-framework/ai/govern)) says to "Schedule quarterly risk assessments for high-risk AI workloads and annual assessments for lower-risk systems". That is about review depth, not about the inventory, which stays live.
- **Microsoft source:** [CoE Starter Kit transition to the Power Platform admin center](https://learn.microsoft.com/power-platform/guidance/coe/starter-kit): "The Power Platform CoE Starter Kit is no longer actively maintained." The admin center experiences that replace it "provide real-time visibility into resources, usage, operational health, and governance insights in a centralized, enterprise-scale experience." [Architecture and governance for AI agents](https://learn.microsoft.com/azure/cloud-adoption-framework/ai-agents/architecture-governance): "Every agent should be discoverable through an approved inventory process." And: "Each inventory entry should identify the agent's accountable owner and risk classification."
- **Fix it in:** [chapter 5](05-one-register.md) (one register), [chapter 14](14-retirement.md) (retirement).

### 4. Governance checked only at the end

- **Old practice:** governance is a go-live review: forms, a design review and sign-offs after the build.
- **Why it fails when people build with AI:** when the build takes a day, a review at the end means rework or a rubber stamp. By then the AI already chose the data, the identity and the audience.
- **New practice:** governance at the idea and inside the AI build loop. The tier is set when the idea arrives (G3, P01). The AI client asks what the tier needs before it builds (G22). Review triggers fire early, from the tier answers, not in the last week.
- **How the product is designed to support it:** the product's own MCP server is designed to answer during the build: possible duplicates, the tier questions, a draft tier assessment, what the tier needs, the owners, the dependants and the missing documents. Review triggers are designed to come from the tier answers.
- **Slide-back signal:** an app's first register entry appears at go-live or as a gap check claim. The tier is set after the build. Reviews open in the last week before release.
- **Microsoft source:** [Govern agents by risk](https://learn.microsoft.com/agents/center-of-excellence/govern-agents-risk): "Apply the tiers when an idea arrives." Same page: "Don't wait for governance to feel complete before you ship. Publish the guardrails, let teams build within them, and refine as you learn." [Apply responsible AI](https://learn.microsoft.com/agents/center-of-excellence/responsible-ai): "Responsible AI is an architecture decision, not a final inspection." [Pillar 5: Organization and culture](https://learn.microsoft.com/agents/adoption-maturity-model/maturity-model-readiness), on security, risk and compliance partners: "Act as partners early in the process rather than as late-stage gates."
- **Fix it in:** [chapter 3](03-front-door-intake-and-triage.md) (the front door), [chapter 21](21-governance-inside-the-ai-build-loop.md) (the build loop), [chapter 15](15-evidence-and-audit.md) (evidence as the work happens).

### 5. The standards document nobody reads

- **Old practice:** standards live in a long document on a wiki. Compliance means people read it and remember it.
- **Why it fails when people build with AI:** people read less as speed rises. An AI client follows only what it can reach while it works. A standard that no tool can answer is a standard nobody follows.
- **New practice:** turn standards into golden paths: templates on the paved road with the decisions already made (G21). Turn the rest into configuration rules the product can answer. Guidelines keep permanent numbers, and answers cite them. Put a short rules file in each repository that tells the AI client to ask the product before it builds.
- **How the product is designed to support it:** guidelines designed as data: a number, a version, the rule, a link, and local text or a "not adopted" reason. Requirement answers designed to name the guideline behind each requirement. An explanation of which rule applied to a record, and why. Every record stamped with the guideline and configuration versions in force.
- **Slide-back signal:** builders ask "where is the standard?". A standards document changes, but the configuration does not. Findings cite rules that no requirement answer returns.
- **Microsoft source:** [Build reusable assets and golden paths](https://learn.microsoft.com/agents/center-of-excellence/reusable-assets): "A golden path is a proven and approved route to build and ship an agent. It's not a suggestion or a wiki page." Same page: "Name an owner for every golden path." [Pillar 3: AI governance and security](https://learn.microsoft.com/agents/adoption-maturity-model/maturity-model-security-governance) lists, as an anti-pattern: "Teams document policies but don't translate them into enforceable technical controls". [Best practices for Microsoft Entra Agent ID](https://learn.microsoft.com/entra/agent-id/best-practices-agent-id) shows why the AI client must be able to reach the rule: "AI coding assistants often default to creating a standard app registration when asked to build an agent, because that's the traditional workload identity pattern." Its advice: "Keep these guardrails visible in your instructions".
- **Fix it in:** [chapter 1](01-adopt-and-tailor.md) (guidelines as configuration), [chapter 7](07-architecture-review-and-decisions.md) (architecture standards), [chapter 20](20-the-paved-road-for-ai-builders.md) (templates on the road), [chapter 21](21-governance-inside-the-ai-build-loop.md) (the rules file).

### 6. Ban AI tools until a policy exists

- **Old practice:** AI tools are blocked until a complete AI policy is written and approved.
- **Why it fails when people build with AI:** people use AI anyway, in tools nobody sanctioned and with no record. The organization loses sight of what was built, and the chance to steer it.
- **New practice:** say who may build with AI, with which AI clients, at which tier and on which path (G23). Connect the sanctioned AI clients to the product. Record AI use on every app and agent (G16). Publish the guardrails, let people build within them, and refine as use grows.
- **How the product is designed to support it:** intake designed to ask whether an app uses AI or is an agent. An AI lead role in the default configuration. Every record designed to show "performed by AI client X for person Y". The MCP server designed to act as the signed-in person. Rows an AI creates designed to start as Draft or Proposed (P10). Which roles may call which tools is configuration.
- **Slide-back signal:** discovery of unsanctioned AI tools rises. Apps registered as "no AI use" turn out to be built with AI. The front door answers "wait for the policy".
- **Microsoft source:** [Why does Shadow AI governance matter for the enterprise?](https://learn.microsoft.com/microsoft-agent-365/guidance/govern-shadow-ai): "The risk isn't that employees use AI; it's that an organization cannot account for the AI it doesn't know about." Same page: "Shadow AI is also evidence of unmet need, not only a policy violation". And: "the durable move is to make the governed route the easier one". [Enable makers and grow the community](https://learn.microsoft.com/agents/center-of-excellence/enablement-community): "Enablement gives people a clear path and helps them move faster."
- **Fix it in:** [chapter 18](18-who-may-build-with-ai-and-how.md) (who may build with AI), [chapter 11](11-ai-agents-and-ai-built-apps.md) (the AI rules), [chapter 3](03-front-door-intake-and-triage.md) (the AI use question at intake).

### 7. Rebuild what Microsoft already ships

- **Old practice:** build an in-house inventory sync, approval engine, deployment tool or agent list next to Microsoft's.
- **Why it fails when people build with AI:** AI makes such a tool cheap to start, in an afternoon. It is still a second copy that drifts from Microsoft's own, with duplicate rules and gaps between them.
- **New practice:** apply the fit rule. If a Microsoft tool holds, enforces or shows a thing, link to it, listen for the facts it sends, or read it live when an admin asks. Keep only the business view on top, in one register (G1). Release the tested artifact through your own pipeline (G9).
- **How the product is designed to support it:** links instead of copies, and a live gap check instead of a scheduled import (P06). Organizations keep their own pipelines, and the pipeline records each deployment (P05). Approval cards designed to go out through Microsoft's own tools, such as Power Automate approvals, not a home-grown approval engine. Agents stay registered in Microsoft Agent 365. The MCP server designed to serve only the product's own tools: it is not an agent registry and not an MCP gateway. Views designed to read the product's own data, with no Power BI needed.
- **Slide-back signal:** a project to copy Microsoft's inventory into your own tables on a schedule, or to build your own approval engine or deployment tool.
- **Microsoft source:** [Govern agents by risk](https://learn.microsoft.com/agents/center-of-excellence/govern-agents-risk): "Don't build a parallel governance universe for agents." [Architecture and governance for AI agents](https://learn.microsoft.com/azure/cloud-adoption-framework/ai-agents/architecture-governance): "Organizations should extend their existing architecture and governance frameworks to support AI agents rather than establish separate systems." [Develop a tenant environment strategy](https://learn.microsoft.com/power-platform/guidance/adoption/environment-strategy): "Prioritize the built-in features of the platform for managing environments when possible, instead of building your own tools." [Operational standards for AI agents](https://learn.microsoft.com/azure/cloud-adoption-framework/ai-agents/operational-standards): "Existing release management processes should provide the foundation for agent change control."
- **Fix it in:** [chapter 22](22-which-microsoft-tool-does-which-job.md) (which Microsoft tool does which job), [chapter 5](05-one-register.md) (links, not copies), [chapter 9](09-change-control-and-release.md) (your own pipeline).

### 8. Let the tool decide

- **Old practice:** auto-approval rules that no named person answers for, and now AI approval stages, end a request as approved. "The system said yes."
- **Why it fails when people build with AI:** accountability disappears. When AI drafts most of the text, people also start to accept drafts without reading them.
- **New practice:** AI drafts, checks and recommends. A named person decides on an approval card (G17). An AI-only approval never counts as authorization for a governance decision (P10). A pre-authorized decision, such as a standard change (paradigm 1), is not this paradigm: a named person approved the rule on record, and each decision under it links that approval.
- **How the product is designed to support it:** no tool designed to approve, reject or accept. An AI stage in the organization's own approval flow recorded as a recommendation. Only a person's answer on a card counts. A deployment whose only code approval came from an AI designed to be flagged.
- **Slide-back signal:** any approval with no named human approver. An AI stage counted as the decision. A request to add an "approve" tool to an AI client.
- **Where Microsoft differs:** Microsoft ships AI approval stages ([FAQ for AI approvals](https://learn.microsoft.com/microsoft-copilot-studio/faqs-ai-approvals)): "AI approvals allow an agent flow to automatically approve or reject requests based on predefined criteria, while ensuring humans remain in control for important decisions." This playbook is stricter for governance decisions. That is a choice for governance records, not a claim that Microsoft forbids AI approvals in business processes.
- **Microsoft source:** [Design an agent front door for your Center of Excellence](https://learn.microsoft.com/agents/center-of-excellence/agent-front-door): "Designated reviewers make approval decisions, and every build must meet the release requirements for its risk tier." [Govern agents by risk](https://learn.microsoft.com/agents/center-of-excellence/govern-agents-risk): "Treat the recommendation as an initial assessment, not approval or a substitute for release controls." [Multistage and AI approvals in agent flows (preview)](https://learn.microsoft.com/microsoft-copilot-studio/flows-advanced-approvals), for sensitive approvals: "ensure that human approval stages are reached in the flow so that humans remain in ultimate control of important decisions."
- **Fix it in:** [chapter 11](11-ai-agents-and-ai-built-apps.md) (the AI rules), [chapter 16](16-mcp-clients-pages-and-cards.md) (cards, not chat), [chapter 21](21-governance-inside-the-ai-build-loop.md) (what needs a person).

### 9. Documentation as a phase after the build

- **Old practice:** documentation is a later phase, often written by someone else, often just before an audit.
- **Why it fails when people build with AI:** the AI that wrote the change can write its documentation in the same change. A later phase rarely comes, and the documents drift from the app.
- **New practice:** each tier says which documents an app needs (G20). The builder's own AI tool writes them in the team's repository, wiki or SharePoint as part of each change. The owner confirms the ones a gate depends on.
- **How the product is designed to support it:** required documents per kind and tier as configuration. For each document, a link, a status (missing, draft, current or stale), an owner and a last check date (P07). Missing documents computed, and stale ones flagged when their freshness time passes. Reminders and escalation. A "current" mark set by an AI client on a document a gate depends on stays a claim until the owner confirms it on a card. By design, the product never writes a document.
- **Slide-back signal:** missing or stale documents rise. Documents appear only in the week before an audit. AI "current" claims stay unconfirmed.
- **Microsoft source:** [About wikis, READMEs, and Markdown](https://learn.microsoft.com/azure/devops/project/wiki/about-readme-wiki) (Azure DevOps): publishing code as a wiki "provides a way to maintain your code documentation synchronously with your code base". [Manage the agent lifecycle](https://learn.microsoft.com/agents/center-of-excellence/agent-lifecycle): "Treat every agent as a product, not a project."
- **Fix it in:** [chapter 12](12-operate-and-review.md) (documents kept current), [chapter 15](15-evidence-and-audit.md) (evidence as the work happens).

### 10. Shared accounts and stored secrets

- **Old practice:** apps, agents and pipelines run as shared, powerful accounts, with long-lived secrets in pipeline settings.
- **Why it fails when people build with AI:** AI multiplies non-human identities: agents, pipelines and AI clients. A shared account gives an AI client more than the person it acts for, and a leaked secret travels far.
- **New practice:** identities for people, agents and pipelines. People sign in as themselves. Pipelines use workload identity federation, with no stored secrets and separate identities per stage. Agents get a Microsoft Entra agent identity with a human sponsor. Unattended apps get their own narrow identity (G7). Roles reach people only through groups (G14).
- **How the product is designed to support it:** every MCP call designed to run as the signed-in person. Product roles designed to come only through Microsoft Entra groups. A role binding check designed to flag direct assignments, expired workload grants and any grant to an agent identity. Pipelines designed to record deployments with their own workload identity, under a recorded grant that expires (P05). Each app's identities and access approach on its record.
- **Slide-back signal:** direct role assignments are found. Workload grants expire unnoticed. A pipeline records deployments under a person's account. Agents use maker credentials with no recorded decision.
- **Microsoft source:** [Workload identity federation](https://learn.microsoft.com/entra/workload-id/workload-identity-federation), on secrets and certificates: "These credentials pose a security risk and have to be stored securely and rotated regularly." [Development security implementation patterns](https://learn.microsoft.com/security/saf/disciplines/development/implementation-patterns): "Separate identities for build, test, and production deployment when their privileges differ." [Best practices for Microsoft Entra Agent ID](https://learn.microsoft.com/entra/agent-id/best-practices-agent-id): "Don't create AI agents as plain app registrations or service principals outside the Agent ID framework." Same page: "Every blueprint and agent identity must have a sponsor, the person or supported group accountable for the agent's purpose."
- **Fix it in:** [chapter 13](13-data-owners-and-access.md) (data owners and access), [chapter 20](20-the-paved-road-for-ai-builders.md) (identities on the road).

## Slide-back signals at a glance

The default watcher for each signal. Your organization can change any of them.

| # | Signal | Where to read it | Who watches | Chapter that fixes it |
|---|---|---|---|---|
| 1 | Lower-tier normal changes wait for a meeting; the standard share falls; the emergency share rises | Change records and approval dates in the product | Change manager | [9](09-change-control-and-release.md) |
| 2 | Environment requests by ticket rise; apps built in the default environment | Your ticket tool; the [Power Platform inventory](https://learn.microsoft.com/power-platform/admin/power-platform-inventory) | Platform owner | [20](20-the-paved-road-for-ai-builders.md) |
| 3 | Inventory exported to a spreadsheet; last gap check older than its cadence; open claims grow | Gap check runs and the claim list in the product | Platform admin | [5](05-one-register.md) |
| 4 | First register entry at go-live or as a gap check claim; tier set after the build | Intake, tier and go-live dates in the product | Platform admin | [3](03-front-door-intake-and-triage.md) |
| 5 | "Where is the standard?"; a standards document changes but the configuration does not | Questions at office hours; the configuration change history | Configuration admin | [1](01-adopt-and-tailor.md) |
| 6 | Unsanctioned AI tools discovered; "no AI use" apps built with AI | Microsoft's shadow AI discovery ([Global Secure Access](https://learn.microsoft.com/entra/global-secure-access/concept-shadow-ai-discovery), or the [Shadow AI page](https://learn.microsoft.com/microsoft-agent-365/admin/agent-shadow-ai) in the Microsoft 365 admin center, preview); AI use answers in the register | AI lead | [18](18-who-may-build-with-ai-and-how.md) |
| 7 | A project to sync, copy or rebuild a Microsoft tool's job | Architecture decisions and project requests | Architect | [22](22-which-microsoft-tool-does-which-job.md) |
| 8 | An approval with no named human approver; a request for an "approve" tool | Approval records in the product; requests to change AI client tools | AI lead | [11](11-ai-agents-and-ai-built-apps.md) |
| 9 | Missing or stale documents rise; AI "current" claims stay unconfirmed | Document status counts in the product | PMO | [12](12-operate-and-review.md) |
| 10 | Direct role assignments; expired workload grants; a pipeline recording as a person | The role binding check; deployment records | Security reviewer | [13](13-data-owners-and-access.md) |

## What good looks like

- Every signal has a named watcher, a threshold and a last review date.
- A signal that fires leads to a recorded decision with an owner and a date, not to a new committee.
- The quarterly slide-back check runs at the portfolio review, and its trend goes to the sponsor.
- When a team falls back on an old habit, people name the paradigm by its number and fix the cause, not the person.

## Who decides (default decision rights)

| Decision | Accountable | Does the work | Consulted | AI may |
|---|---|---|---|---|
| Watch the signals and run the quarterly check | Platform owner | PMO | AI lead, change manager, architect | Gather the signal data and draft the review |
| Decide the fix when a signal fires | The accountable role in the chapter that fixes it | The watcher of that signal | Platform owner, AI lead | Draft options with pros and cons |

## Guidelines this chapter applies

- **G1 Keep one register of business applications.** Paradigms 3 and 7: register at the front door, and link instead of copy.
- **G3 Set a risk tier for every app, on tiers you define.** Paradigms 1 and 4: the tier sets the weight, and it is set when the idea arrives.
- **G7 Give each app identity its own narrow role.** Paradigm 10: unattended apps get their own narrow identity.
- **G9 Release the tested artifact through your own pipeline.** Paradigm 7: keep your pipeline; do not build a second one.
- **G12 Approve every production change on record, never by the person who started it.** Paradigm 1: fewer meetings, never fewer records.
- **G14 Grant access through groups, review it, and keep a kill switch.** Paradigm 10: roles only through groups.
- **G16 Record AI use on every app and agent.** Paradigm 6: AI use on record, instead of a ban.
- **G17 Let AI draft and recommend; people approve.** Paradigm 8: a named person decides.
- **G20 Keep each app's required documents current.** Paradigm 9: documents written with each change.
- **G21 Give AI builders a paved road.** Paradigms 2 and 5: the governed way is the fast way, and standards live in templates.
- **G22 Let the AI builder ask before it builds.** Paradigm 4: governance inside the build loop.
- **G23 Say who may build with AI, and on which path.** Paradigm 6: a published answer, not a ban.

## Processes this chapter uses

- **P01 Intake and triage.** Paradigms 2 and 4: fast intake at the idea.
- **P03 Change management.** Paradigm 1: change types, standard models and approvals by tier.
- **P05 Deployment record.** Paradigms 2, 7 and 10: the organization's own pipeline records each deployment under its own identity.
- **P06 Gap check and claim.** Paradigms 3 and 7: a live check instead of a copy.
- **P07 Documentation tracking.** Paradigm 9: required, linked and tracked documents.
- **P10 AI draft and human accept.** Paradigms 6 and 8: AI rows start as drafts, and only a person accepts.

## How the product is designed to support it

- Each paradigm above has its own product line. Together they are designed to make the new practice the easy one: tiers and rules as configuration, answers inside the AI build loop, links instead of copies, a person's answer on a card as the only decision that counts, and identities for people, agents and pipelines.
- It is designed to keep the records and dates these signals use: change types and approvals, intake and tier dates, gap check runs and claims, document status, deployment flags, and the role binding check.
- An AI client is designed to gather the numbers for the quarterly check, as the person who asks, and to draft the review. A person decides what to do about each signal.
- It is designed not to repeat what Microsoft already shows, such as shadow AI discovery. The check links to Microsoft's own reports instead.

## What you can tailor

- Which signals you watch, and which you add. New paradigms take the next numbers, from 11 up, so the first ten keep their links.
- The threshold for each signal, for example how many lower-tier changes may wait for a meeting before the signal fires.
- Who watches each signal, and where they read it.
- The cadence of the slide-back check. Quarterly is the default.

## Microsoft guidance it applies

The quotes sit under each paradigm above. This list gives each page once, with the paradigms it backs.

- [Govern agents by risk](https://learn.microsoft.com/agents/center-of-excellence/govern-agents-risk), checked 2026-11-22: paradigms 1, 4, 7 and 8.
- [Agentic transformation patterns](https://learn.microsoft.com/agents/adoption-patterns/), checked 2026-11-22: paradigm 1.
- [Operational standards for AI agents](https://learn.microsoft.com/azure/cloud-adoption-framework/ai-agents/operational-standards) (Cloud Adoption Framework), checked 2026-11-22: paradigms 1 and 7.
- [From safety to controllability in enterprise AI](https://learn.microsoft.com/startups/build/enterprise-readiness/ai-guardrails/safety-to-controllability-enterprise-ai), checked 2026-11-22: paradigm 1.
- [What is a personal environment?](https://learn.microsoft.com/power-platform/admin/personal-environments-overview), checked 2026-11-22: paradigm 2.
- [Develop a tenant environment strategy](https://learn.microsoft.com/power-platform/guidance/adoption/environment-strategy), checked 2026-11-22: paradigms 2 and 7.
- [Pillar 5: Organization and culture](https://learn.microsoft.com/agents/adoption-maturity-model/maturity-model-readiness), checked 2026-11-22: paradigms 2 and 4.
- [Build an agentic Center of Excellence](https://learn.microsoft.com/agents/center-of-excellence/), checked 2026-11-22: paradigm 2.
- [CoE Starter Kit transition to the Power Platform admin center](https://learn.microsoft.com/power-platform/guidance/coe/starter-kit), checked 2026-11-22: paradigm 3.
- [Architecture and governance for AI agents](https://learn.microsoft.com/azure/cloud-adoption-framework/ai-agents/architecture-governance) (Cloud Adoption Framework), checked 2026-11-22: paradigms 3 and 7.
- [Guidance to set up your organization's AI governance process](https://learn.microsoft.com/azure/cloud-adoption-framework/ai/govern) (Cloud Adoption Framework), checked 2026-11-22: paradigm 3.
- [Apply responsible AI](https://learn.microsoft.com/agents/center-of-excellence/responsible-ai), checked 2026-11-22: paradigm 4.
- [Build reusable assets and golden paths](https://learn.microsoft.com/agents/center-of-excellence/reusable-assets), checked 2026-11-22: paradigm 5.
- [Pillar 3: AI governance and security](https://learn.microsoft.com/agents/adoption-maturity-model/maturity-model-security-governance), checked 2026-11-22: paradigm 5.
- [Best practices for Microsoft Entra Agent ID](https://learn.microsoft.com/entra/agent-id/best-practices-agent-id), checked 2026-11-22: paradigms 5 and 10.
- [Why does Shadow AI governance matter for the enterprise?](https://learn.microsoft.com/microsoft-agent-365/guidance/govern-shadow-ai), checked 2026-11-22: paradigm 6.
- [Enable makers and grow the community](https://learn.microsoft.com/agents/center-of-excellence/enablement-community), checked 2026-11-22: paradigm 6.
- [Shadow AI discovery in Global Secure Access](https://learn.microsoft.com/entra/global-secure-access/concept-shadow-ai-discovery), checked 2026-11-22: signal 6, a network-based view of AI apps and tools used without IT approval.
- [Shadow AI in Microsoft 365 admin center (preview)](https://learn.microsoft.com/microsoft-agent-365/admin/agent-shadow-ai), checked 2026-11-22: signal 6, a view for discovering and governing unmanaged AI agents.
- [Design an agent front door for your Center of Excellence](https://learn.microsoft.com/agents/center-of-excellence/agent-front-door), checked 2026-11-22: paradigm 8.
- [Multistage and AI approvals in agent flows (preview)](https://learn.microsoft.com/microsoft-copilot-studio/flows-advanced-approvals), checked 2026-11-22: paradigm 8.
- [FAQ for AI approvals](https://learn.microsoft.com/microsoft-copilot-studio/faqs-ai-approvals), checked 2026-11-22: paradigm 8.
- [About wikis, READMEs, and Markdown](https://learn.microsoft.com/azure/devops/project/wiki/about-readme-wiki), checked 2026-11-22: paradigm 9.
- [Manage the agent lifecycle](https://learn.microsoft.com/agents/center-of-excellence/agent-lifecycle), checked 2026-11-22: paradigm 9.
- [Workload identity federation](https://learn.microsoft.com/entra/workload-id/workload-identity-federation), checked 2026-11-22: paradigm 10.
- [Development security implementation patterns](https://learn.microsoft.com/security/saf/disciplines/development/implementation-patterns), checked 2026-11-22: paradigm 10.

## Done when

- Every signal has a named watcher, a threshold and a last review date.
- The quarterly slide-back check has run at least once, as a baseline, and its answers are on record.
- Every signal that fired has a recorded decision with an owner and a date.

## The quarterly slide-back check

Run it once a quarter, at the portfolio review. The PMO prepares it. An AI client can gather the numbers and draft the answers, as the person who asks. The platform owner signs it off. Each "yes" becomes a recorded decision with an owner and a date.

1. **Board.** Did a lower-tier normal change wait for a meeting, or did the share of standard changes fall, or the share of emergency changes rise?
2. **Queue.** Did anyone ask IT by ticket for an environment, a connector or a deployment that the paved road should provide? Was a new app built in the default environment?
3. **Spreadsheet.** Is the last gap check older than its cadence? Did anyone export the inventory to a spreadsheet to reconcile it?
4. **End.** Was an app first registered at go-live, or first found by the gap check? Was a tier set after the build?
5. **Shelf.** Did a standard change in a document but not in the configuration? Did builders ask where a standard is?
6. **Ban.** Did discovery find AI tools nobody sanctioned, or apps built with AI but registered as "no AI use"?
7. **Rebuild.** Did anyone start a project to copy a Microsoft tool's data or rebuild its job?
8. **Tool decides.** Is there an approval with no named person, or a request to let an AI client approve?
9. **Docs later.** Did missing or stale documents rise? Are AI "current" claims waiting for an owner?
10. **Shared accounts.** Did the role binding check find a direct assignment, an expired workload grant, or a pipeline recording as a person?

Close the check: compare the answers with last quarter, record the decisions, and send the trend to the sponsor with the measures in [chapter 19](19-measure-ai-app-development.md).
