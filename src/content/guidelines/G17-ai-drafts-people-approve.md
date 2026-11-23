# G17. Let AI draft and recommend; people approve

- **Number:** G17
- **Version:** 1.0, 2026-11-22
- **Status:** active
- **Home chapter:** 11 (AI agents, AI-built apps, and AI inside the process)
- **Cited in chapters:** 0, 1, 8, 9, 11, 15, 16, 17, 18, 21
- **Processes:** P10, P11

**Rule.** No AI agent or tool approves work, including its own. AI may draft, check and recommend. A named person makes each approval. AI coding agents follow a rules file kept in the repository.

**Why it matters.** Some approval tools now let an AI stage end a request as approved with no person involved. Keeping people as the deciders keeps accountability clear.

**How the product is designed to support it.** It is designed so that rows created by AI start as Draft or Proposed, only the approval flow writes an approval outcome, an AI stage counts as a recommendation, and each configuration change an AI makes records the admin it acted for.

## Microsoft anchors

- [Design an agent front door for your Center of Excellence](https://learn.microsoft.com/agents/center-of-excellence/agent-front-door), checked 2026-11-22: the front door agent recommends a route and does not approve the work. "Designated reviewers make approval decisions".
- [Use Copilot Studio agents in model-driven apps](https://learn.microsoft.com/power-platform/architecture/reference-architectures/contextual-ai-model-driven-app) (reference architecture), checked 2026-11-22: "Agent responses never autowrite to Dataverse." "Human makes final decisions. No autonomous updates."
- [Multistage and AI approvals in agent flows (preview)](https://learn.microsoft.com/microsoft-copilot-studio/flows-advanced-approvals) (Copilot Studio), checked 2026-11-22: "Multistage approvals are available exclusively in agent flows." "All of these capabilities are in preview." An AI stage produces an Approve or Reject decision, and with conditions a request "can bypass manager approval and automatically end as approved."

## Working shape

- **When:** whenever AI takes part in building, reviewing or approving work, or in changing configuration.
- **Who:** the app owner, the approvers, the AI lead where there is one, and the platform team for configuration.
- **Steps:**
  1. Keep a rules file in each repository that AI coding agents read, saying what they may and may not do.
  2. Mark records and drafts that AI creates as proposed until a person accepts them.
  3. If a flow uses an AI approval stage, route its result to a human stage before the request can end as approved.
- **Done when:** every approval has a named human approver, and work done by AI is marked as such.
- **Evidence:** the approval history with human approvers, and the "performed by" field on records AI made.
