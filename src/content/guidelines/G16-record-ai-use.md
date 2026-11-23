# G16. Record AI use on every app and agent

- **Number:** G16
- **Version:** 1.0, 2026-11-22
- **Status:** active
- **Home chapter:** 11 (AI agents, AI-built apps, and AI inside the process)
- **Cited in chapters:** 1, 3, 11, 17, 18, 22
- **Processes:** P01, P02, P06

**Rule.** Record where each app uses AI and every agent it runs, with an accountable owner, a risk tier and the agreed level of autonomy. Link agents to Microsoft's agent registry instead of keeping a second list.

**Why it matters.** AI use spreads quietly through apps, flows and agents. Without a record, nobody can say which AI acts on which data, or who agreed to it.

**How the product is designed to support it.** It is designed to record AI use and agreed autonomy on the app record, link each agent by its Microsoft ID, and route autonomy decisions to an AI lead role that each organization can rename, remap or drop.

## Microsoft anchors

- [Architecture and governance for AI agents](https://learn.microsoft.com/azure/cloud-adoption-framework/ai-agents/architecture-governance) (Cloud Adoption Framework), checked 2026-11-22: "Each inventory entry should identify the agent's accountable owner and risk classification."
- [Overview of Microsoft Agent 365](https://learn.microsoft.com/microsoft-agent-365/overview), checked 2026-11-22: the agent registry lists agents from Microsoft, from partners and from your own teams. "For each agent, you can see who owns it, how much people use it, and whether it has any risks."
- [Microsoft Agent 365 service description](https://learn.microsoft.com/office365/servicedescriptions/microsoft-agent-365/microsoft-agent-365), checked 2026-11-22: Agent 365 "is available as a stand-alone subscription for use with eligible Microsoft 365 subscriptions and is also included with Microsoft 365 E7."
- [Govern agents by risk](https://learn.microsoft.com/agents/center-of-excellence/govern-agents-risk), checked 2026-11-22: high-risk agents need "A decision-rights framework that sets what the agent may decide alone and what needs a human".

## Maps to

NIST AI RMF GOVERN 1.6 (an inventory of AI systems).

## Working shape

- **When:** at intake, and whenever an agent gains new tools, data or autonomy.
- **Who:** the app or agent owner; the AI lead, where your organization has one; a security reviewer for higher tiers.
- **Steps:**
  1. At intake, ask whether the app uses AI or is an agent, and what it may do without a person.
  2. Record the owner, the tier and the agreed autonomy, and link the agent's registry entry.
  3. Review the record whenever the agent's tools, data or autonomy change.
- **Done when:** every known agent links to a registered application with an owner and an agreed autonomy level.
- **Evidence:** the AI use entry and the autonomy decision on the app record, with the registry link.
