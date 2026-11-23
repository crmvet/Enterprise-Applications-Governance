# Chapter 11. AI agents, AI-built apps, and AI inside the process

- **Version:** 0.2 draft, 2026-11-22
- **Track:** Shared
- **Parties:** AI (the AI lead and each agent's owner), makers, security, the business owner, the configuration admin
- **Guidelines:** [G8](../guidelines/G08-decide-how-apps-reach-dataverse.md), [G16](../guidelines/G16-record-ai-use.md), [G17](../guidelines/G17-ai-drafts-people-approve.md), [G23](../guidelines/G23-say-who-may-build-with-ai.md)
- **Processes:** [P02](processes.md#p02-tier-assessment-and-required-approvals), [P06](processes.md#p06-gap-check-and-claim), [P08](processes.md#p08-owner-leaves), [P10](processes.md#p10-ai-draft-and-human-accept), [P11](processes.md#p11-configuration-change)

## Why this chapter exists

"An agent did this work. Who approved it, and was it allowed to?" "I don't know which AI touches our data." AI now plays three parts at once: a tool that builds apps in days, a product that acts on its own, and a helper that drafts most of the text in every stage. Each part follows one rule: AI drafts and checks; a person decides and answers for it.

**Read with:** [chapter 0, Govern AI development of the apps](00-govern-ai-development-of-the-apps.md), which sets the keynote for the whole playbook, and [chapter 21, Governance inside the AI build loop](21-governance-inside-the-ai-build-loop.md), the IT track's chapter on how these rules work while an AI client builds.

## The three parts AI plays

| Part | What it means here | The rule |
|---|---|---|
| A builder's tool | Apps built with AI coding tools in days | The same path as any app. The tier, not the tool, decides the reviews |
| A product | An agent is a business application | Same register entry, a human owner, a tier and an agreed autonomy level. Linked to Microsoft's agent registry, not copied |
| A helper inside the process | Drafts intakes, decisions, change requests, release notes and review packs | Every write is a draft that a person accepts. Every record says who performed it |

## What AI may and may not do

| AI may | AI may not |
|---|---|
| Read register facts as the person it acts for | Approve, reject or authorize anything |
| Draft intakes, decisions, change requests, release notes and review packs | Hold an accountability, or be the only reviewer of its own work |
| Propose a tier, a change type, a duplicate match or a standard change | Set the tier, or decide that a change is standard |
| Record links, deployments and document status for the person | Change configuration without an admin's instruction and acceptance |
| Run read-only checks, including the gap check as the admin | Act for a person without that being recorded |

## What good looks like

- Every agent and every AI-built app is in the register, with a human owner, a tier and, for agents, an agreed autonomy level.
- Every AI client reaches data as the person it acts for, so that person's own permissions decide.
- No approval anywhere is made by an AI alone. An AI stage in an approval flow is a recommendation that a person then decides on.
- Every record shows whether a person, a pipeline or an AI acting for a person made it.

## How it works

1. **Ask at intake.** Does the app use AI, or is it an agent? What may it do without a person? (chapter 3)
2. **Tier the agent.** The tier questions include whether an agent acts on its own. Crossing from assisting to executing is the clearest risk signal.
3. **Agree the autonomy level.** For an agent, the security reviewer decides what it may do alone and what needs a person, with the AI lead consulted. Enforcing that level stays in Copilot Studio, Agent 365 and Entra.
4. **Link, do not copy.** Link each agent to its entry in Microsoft's agent registry by its Microsoft ID.
5. **Choose how it connects.** Prefer end-user credentials. A tool that runs with the maker's credentials needs a recorded decision.
6. **Draft, then accept.** When an AI client writes, the record starts as a draft or a proposal. What counts as the person's acceptance depends on what is accepted. In the default configuration: a decision needs an approval card; registering an app the AI created needs a confirmation card to the business owner; marking a gate-relevant document current needs a confirmation card to its owner; submitting an intake, a change or a decision can be the same call, made for the person; links, deployment records and checks need no separate acceptance.
7. **Configuration through AI, decided by the admin.** An admin tells an AI what to change. The AI prepares and proposes the change. In the default configuration the admin accepts the exact difference on one card, and may accept their own AI's draft; with strict separation a second admin accepts.
8. **Decide on flagged agents.** When Microsoft flags an agent without an owner, or an owner leaves, the AI lead records keep, reassign, consolidate or retire, with a link to Microsoft's evidence. Reassigning owners happens in Microsoft's tools.
9. **Re-evaluate on change.** A model, instruction, tool or permission change to an agent is a normal change that triggers re-evaluation before production.

## Who decides (default decision rights)

| Decision | Accountable | Does the work | Consulted | AI may |
|---|---|---|---|---|
| Set an agent's autonomy level | Security reviewer | Platform admin | AI lead, business owner, data owner, architect | Nothing |
| Approve restricted data reaching an AI model | Data owner, with the security reviewer's agreement | Platform admin | AI lead, architect | Nothing |
| Decide on an agent Microsoft flags | AI lead | Platform admin | Business owner | Gather the evidence |
| Accept an AI-prepared configuration change | Configuration admin | Configuration admin | The role that owns the area | Prepare and propose |

## Guidelines this chapter applies

- **G16 Record AI use on every app and agent.** Owner, tier, agreed autonomy and the registry link on every agent.
- **G17 Let AI draft and recommend; people approve.** No AI approves, including its own work. Drafts stay drafts until a person accepts.
- **G8 Decide how each app reaches Dataverse.** AI clients and agents connect as the person by default; maker credentials need a recorded decision.
- **G23 Say who may build with AI, and on which path.** The organization publishes who may build with AI, with which AI clients and at which tier (chapter 18). The rules in this chapter apply on every path.

## Processes this chapter uses

- **P10 AI draft and human accept.** What AI may draft, how its rows are marked, and which channel counts as a person's acceptance.
- **P02 Tier assessment and required approvals.** The agent's tier and its autonomy level decision.
- **P06 Gap check and claim.** Finds unregistered agents, and links Microsoft's ownerless agent evidence.
- **P08 Owner leaves.** Decisions on agents whose owner left.
- **P11 Configuration change.** The admin instructs, the AI performs, the admin accepts.

## How the product is designed to support it

- It is designed so that no tool, endpoint or flow it ships can approve, reject or accept a decision for an AI.
- It is designed so that rows created by AI start as draft or proposed, and that only the flow that sends approval cards writes an approval outcome.
- It is designed to count only a person's answer on a card, and to keep an AI stage from your own approval flow as a recommendation that never counts.
- It is designed to stamp every record with who performed it, including "AI client X for person Y", and to show that on every page and tool answer.
- It is designed to route autonomy decisions to an AI lead role that each organization can rename, remap or drop.

## Why a card, not a chat

The product's own MCP server is designed to act with the person's sign-in. So it cannot prove that a person, not the AI, pressed the button. Copilot Studio clients may even run with maker-provided credentials, which are on by default. An approval or confirmation card answered in Outlook or Teams is the one channel it can trust.

## What you can tailor

- Whether you have an AI lead, and what that role decides.
- Which subject kinds an AI client may submit for a person, and which need a confirmation card.
- The tier questions about autonomy, and which answers add a responsible AI review.

## Microsoft guidance it applies

- [Govern agents by risk](https://learn.microsoft.com/agents/center-of-excellence/govern-agents-risk), checked 2026-11-22: "The clearest risk signal is the assist-to-execute line", and high-risk agents need "A decision-rights framework that sets what the agent may decide alone and what needs a human".
- [Design an agent front door for your Center of Excellence](https://learn.microsoft.com/agents/center-of-excellence/agent-front-door), checked 2026-11-22: the front door agent recommends a route and does not approve the work.
- [Use Copilot Studio agents in model-driven apps](https://learn.microsoft.com/power-platform/architecture/reference-architectures/contextual-ai-model-driven-app) (reference architecture), checked 2026-11-22: "Human makes final decisions. No autonomous updates."
- [Multistage and AI approvals in agent flows (preview)](https://learn.microsoft.com/microsoft-copilot-studio/flows-advanced-approvals) (Copilot Studio), checked 2026-11-22: an AI stage can let a request "bypass manager approval and automatically end as approved."
- [Overview of Microsoft Agent 365](https://learn.microsoft.com/microsoft-agent-365/overview), checked 2026-11-22: the agent registry shows, for each agent, who owns it, how much it is used and whether it has risks.
- [Architecture and governance for AI agents](https://learn.microsoft.com/azure/cloud-adoption-framework/ai-agents/architecture-governance) (Cloud Adoption Framework), checked 2026-11-22: "Each inventory entry should identify the agent's accountable owner and risk classification."

## How AI changes this

- This chapter holds the AI rules for the whole playbook. Who may build with AI, with which AI clients and on which path, is the organization's published answer in [chapter 18, Who may build with AI, and how](18-who-may-build-with-ai-and-how.md) (G23). These rules apply on every path.
- The product's MCP server is designed so the AI builder can ask the governance questions while it builds (chapter 21). The rules here then reach the build at the start, not at go-live.
- AI plays builder, product and helper, often for the same app in the same week. One rule holds for all three: AI drafts and checks; a named person decides.

## Old paradigm to avoid

[8. Let the tool decide](17-old-paradigms-and-how-not-to-slide-back.md#8-let-the-tool-decide). Here it shows up as an AI stage in an approval flow that ends a request as approved, an agent that is the only reviewer of its own work, or people who accept AI drafts without reading them. An AI recommendation is an input; a named person decides on a card.

**Slide-back signal:** any approval with no named human approver, or an AI stage counted as the decision.

## Done when

- Every known agent links to a registered application with an owner, a tier and an agreed autonomy level.
- Every approval has a named human approver, and work done by AI is marked as such.
- Every configuration change made through an AI was accepted by an admin on a card.
