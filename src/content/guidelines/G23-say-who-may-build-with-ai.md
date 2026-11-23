# G23. Say who may build with AI, and on which path

- **Number:** G23
- **Version:** 1.0, 2026-11-22
- **Status:** active
- **Home chapter:** 18 (Who may build with AI, and how)
- **Cited in chapters:** 1, 8, 11, 17, 18
- **Processes:** P01, P02, P10

**Rule.** Publish who may build with AI, with which AI clients, at which tier and in which environments. Sanction the clients people need instead of banning AI, and review the paths as use grows.

**Why it matters.** A ban pushes AI use out of sight, into tools nobody sanctioned and with no record. A free-for-all loses track of who owns what. A short, published answer lets people build with AI on terms the organization chose.

**How the product is designed to support it.** It is designed to ship an AI lead role that each organization can rename, merge or drop; to ask at intake whether an app uses AI or is an agent; to show on every record when an AI client acted, and for whom; and to keep, as configuration, which roles may call which tools and which kinds of record an AI client may submit for a person.

## Microsoft anchors

- [Enable makers and grow the community](https://learn.microsoft.com/agents/center-of-excellence/enablement-community) (agent Center of Excellence guidance, written for agents), checked 2026-11-22: "Enablement gives people a clear path and helps them move faster."
- [Why does Shadow AI governance matter for the enterprise?](https://learn.microsoft.com/microsoft-agent-365/guidance/govern-shadow-ai) (Microsoft Agent 365 guidance), checked 2026-11-22: "The risk isn't that employees use AI; it's that an organization cannot account for the AI it doesn't know about." Also "Shadow AI is also evidence of unmet need, not only a policy violation".
- [Implement a zoned governance strategy](https://learn.microsoft.com/microsoft-copilot-studio/guidance/sec-gov-phase2) (Copilot Studio), checked 2026-11-22: three zones for agent creation, from "Anyone can make personal and team productivity agents based on content they already have access to." to "Pro developers build mission-critical, enterprise-grade agents for use across the organization." The playbook maps its default builder paths to these zones; no Microsoft page joins the two.
- [Configure the Dataverse MCP server for an environment](https://learn.microsoft.com/power-apps/maker/data-platform/data-platform-mcp-disable) (Power Apps), checked 2026-11-22: "You must enable the additional clients in the Power Platform admin center before you can connect to the client."

## Working shape

- **When:** before AI building is opened to people, whenever someone asks for a new AI client, and each quarter.
- **Who:** the sponsor approves the paths; the AI lead drafts and keeps them; the security reviewer sanctions each AI client; the platform admin puts builders on a path; IT switches the clients on.
- **Steps:**
  1. Define the builder paths by tier: who may build, where, and what needs a person (G3).
  2. List the sanctioned AI clients for each path, each with a recorded decision by the security reviewer.
  3. Allow exactly those clients in Microsoft's settings and in the clients' own settings, and give each path its own security group.
  4. Publish the paths at the front door and in each repository's rules file.
  5. Train people by role before they take a path, and name champions.
  6. Record AI use on every app (G16).
  7. Review the paths each quarter. Treat unsanctioned AI use as unmet need: offer a sanctioned option, or record why not.
- **Done when:** the paths are published, every builder is in the group for their path, and every sanctioned client is listed with its decision and allowed in Microsoft's settings.
- **Evidence:** the published paths with their date and version, the sanctioned client list with its decisions, links to the group memberships, and the AI use recorded on each app.
