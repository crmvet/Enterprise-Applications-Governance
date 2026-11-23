# Chapter 16. Working with the product through MCP clients, pages and approval cards

- **Version:** 0.2 draft, 2026-11-22
- **Track:** IT
- **Parties:** all seven
- **Guidelines:** [G8](../guidelines/G08-decide-how-apps-reach-dataverse.md), [G17](../guidelines/G17-ai-drafts-people-approve.md), [G22](../guidelines/G22-let-the-ai-builder-ask-before-it-builds.md)
- **Processes:** [P05](processes.md#p05-deployment-record), [P06](processes.md#p06-gap-check-and-claim), [P10](processes.md#p10-ai-draft-and-human-accept), [P11](processes.md#p11-configuration-change)

## Why this chapter exists

Most people should never need a back office. A maker wants to register an app from the tool they already work in. An owner wants to answer one question and get on with the day. An approver wants a card in Outlook or Teams. AI clients need governance facts to act safely, and nothing tells them. This chapter shows the three ways people and AI clients are designed to reach the product, and the rules that keep each way safe.

## The three ways in

| Way in | Who uses it | What for |
|---|---|---|
| MCP clients, such as Claude Code | Makers, owners, admins, and their AI clients | Ask questions, draft intakes, changes and decisions, record links and deployments, run the gap check, prepare configuration changes |
| The product's pages | Anyone who prefers a screen | See an application, its dependencies, its timeline and what needs them |
| Approval cards in Outlook and Teams | Approvers and owners | Approve, reject or confirm. The only way a decision is made |

## What good looks like

- Every call from an MCP client runs as the signed-in person, so that person's own permissions decide what they can see and do.
- AI clients draft; people decide on cards.
- Every record says whether a person, a pipeline or an AI acting for a person made it.
- Pipelines record deployments with their own workload identity, not a person's account.
- Admin-only tools, such as the gap check and configuration changes, are open only to admin roles.

## How it works

1. **Connect your MCP client.** Your admin registers the product's MCP server for your organization. You sign in as yourself.
2. **Ask.** "Who owns this app?" "What does my tier need?" "What breaks if this retires?" "What needs me?" Answers follow your own access.
3. **Draft.** Your AI client drafts an intake, a change, a decision or a document link. It starts as a draft or a proposal.
4. **Submit or confirm.** Your AI client may submit an intake, a change or a decision for you where the configuration allows it. Registering an app the AI created, or marking a gate-relevant document current, goes to a person on a confirmation card.
5. **Decide on a card.** Every decision goes to the right person as an approval card in Outlook or Teams. The AI cannot answer it.
6. **Record facts.** Your AI tool can record a deployment after an import, or a link to a page it wrote. These need no separate acceptance, and show the AI as the performer.
7. **Admins run checks.** An admin runs the gap check from an MCP client, with their own sign-in. Nothing runs unattended.
8. **Admins change configuration.** An admin tells their AI what to change; the AI proposes it; the admin accepts the difference on a card.

## Who decides (default decision rights)

| Task | Who may do it | AI may |
|---|---|---|
| Read register facts | Anyone with a role, within their own access | Read as the person |
| Draft intakes, changes, decisions | Requesters, owners, builders | Draft and, where allowed, submit for the person |
| Approve or reject a decision | The holder of the required role, on a card | Never |
| Record a deployment | A pipeline step, an owner's AI tool, or a person | Record it for the person |
| Run the gap check | Admin roles | Run it as the admin |
| Propose a configuration change | Configuration admins | Prepare and propose for the admin |

## Guidelines this chapter applies

- **G8 Decide how each app reaches Dataverse.** The product's own MCP tools are designed to act on behalf of the signed-in person. The same rule applies to every AI client your organization runs.
- **G17 Let AI draft and recommend; people approve.** AI clients draft and propose; only a person on a card decides.
- **G22 Let the AI builder ask before it builds.** Before an AI client builds or changes an app, it asks the product's MCP server the governance questions, drafts what it learns, and leaves the decisions to people.

## Processes this chapter uses

- **P10 AI draft and human accept.** Drafts, the performed-by marks, and which channel counts as a person's acceptance.
- **P05 Deployment record.** Recording a deployment from an AI tool or a pipeline step.
- **P06 Gap check and claim.** The admin's live check from an MCP client.
- **P11 Configuration change.** Configuration through an AI, accepted by an admin.

## How the product is designed to support it

- It is designed to have its own MCP server, next to Microsoft's MCP servers, with governance tools that answer product questions.
- It is designed so that each call runs as the identity behind the connection, normally the signed-in person, and to check who the caller is rather than assume it.
- It is designed to describe its tables and fields for agents, so AI clients do not guess.
- It is designed to offer no tool that approves. Decisions are made only on approval cards, and only a person's answer counts.
- It is designed to let each organization set which tools are open to which roles, and which subject kinds an AI client may submit.

## What you can tailor

- Which tools are open to which roles.
- Which subject kinds an AI client may submit for a person, and which need a confirmation card.
- Which channel each kind of card uses.

## Microsoft guidance it applies

- [Dataverse MCP server](https://learn.microsoft.com/power-apps/maker/data-platform/data-platform-mcp), checked 2026-11-22: Claude Code is one of the MCP clients that can connect to Microsoft's Dataverse MCP server.
- [Azure DevOps MCP Server](https://learn.microsoft.com/azure/devops/mcp-server/mcp-server-overview), checked 2026-11-21: an owner's AI tool can write wiki pages and work items through it, as the same user.
- [Get started with approvals](https://learn.microsoft.com/power-automate/get-started-approvals) (Power Automate), checked 2026-11-22: approvals can be everyone must approve, first to respond, custom responses or sequential, answered from Outlook or Teams.
- [Control maker-provided credentials for authentication](https://learn.microsoft.com/microsoft-copilot-studio/configure-no-maker-authentication) (Copilot Studio), checked 2026-11-22: with maker credentials, "the agent uses the maker's credentials, not the end user's credentials".

## How AI changes this

- This is where the AI build loop meets the product. The product's MCP server is designed to be the governance voice inside the build.
- Before an AI client builds or changes an app, it asks: the tier, what the tier needs, the owners, what depends on the app, and which documents are missing (G22). [Chapter 21, Governance inside the AI build loop](21-governance-inside-the-ai-build-loop.md), walks through the loop step by step.
- The AI client drafts what it learns. Cards stay the only place a decision is made, so speed in a chat never turns into an approval.

## Old paradigm to avoid

[8. Let the tool decide](17-old-paradigms-and-how-not-to-slide-back.md#8-let-the-tool-decide). Here it shows up as approval by chat: a request to add an approve tool to an AI client, or a yes typed in a chat and treated as a decision.

**Slide-back signal:** requests for an "approve" tool, or decisions recorded outside cards.

## Done when

- People reach the product from the tool they already use, as themselves.
- Every decision was made by a person on a card.
- Every AI-made record shows the AI client and the person it acted for.
