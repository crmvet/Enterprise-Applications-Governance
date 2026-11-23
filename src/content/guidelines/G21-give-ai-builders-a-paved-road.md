# G21. Give AI builders a paved road

- **Number:** G21
- **Version:** 1.0, 2026-11-22
- **Status:** active
- **Home chapter:** 20 (The paved road for AI builders)
- **Cited in chapters:** 0, 1, 8, 17, 20
- **Processes:** P01, P04, P05

**Rule.** Make the governed way to build the fastest way. Give builders governed environments they get without a ticket, templates and approved connectors and MCP servers to start from, pipelines the organization owns, and identities ready for people, agents and pipelines.

**Why it matters.** An AI builder can have a working app in a day. If the governed way means a ticket and a wait of days, the work moves to wherever nobody is watching: the default environment, a personal account, or a tool nobody approved. A road that is faster than the shortcut is a control people choose to use. Microsoft calls such a road a golden path.

**How the product is designed to support it.** It is designed to tell a builder, for the app in front of them, its tier and what that tier needs; to map which pipeline deploys each app; to take the deployment record from the pipeline's last step under the pipeline's own identity; and to publish those record steps as samples. By design, it never builds environments or pipelines.

## Microsoft anchors

- [Build reusable assets and golden paths](https://learn.microsoft.com/agents/center-of-excellence/reusable-assets) (agent Center of Excellence guidance, written for agents), checked 2026-11-22: "Teams move faster when the right way to build is also the easy way." "When the approved way to build an agent is slower than the improvised way, makers improvise." It lists "A vetted set of connectors and MCP servers to your systems and data, already reviewed for security and least-privilege access." and advises "Name an owner for every golden path."
- [What is a personal environment?](https://learn.microsoft.com/power-platform/admin/personal-environments-overview) (Power Platform), checked 2026-11-22: "Environment routing takes makers to their personal environment and creates it on their first visit, so they don't need to request an environment or choose where to build." "Rules that you set on an environment group apply to every personal environment in the group, and makers can't override them."
- [Overview of pipelines in Power Platform](https://learn.microsoft.com/power-platform/alm/pipelines), checked 2026-11-22: pipelines "aim to democratize application lifecycle management (ALM) for Power Platform and Dynamics 365 customers". "The same solution artifact must pass through pipeline stages in sequential order."
- [Workload identity federation concepts](https://learn.microsoft.com/entra/workload-id/workload-identity-federation) (Microsoft Entra), checked 2026-11-22: of secrets and certificates, "These credentials pose a security risk and have to be stored securely and rotated regularly."
- [Administrative relationships in Microsoft Entra Agent ID](https://learn.microsoft.com/entra/agent-id/agent-owners-sponsors-managers) (Microsoft Entra), checked 2026-11-22: "Sponsors are required for agent identity blueprints and agent identities, ensuring every agent has a designated business owner."

## Working shape

- **When:** before people start to build with AI, when a new kind of app or AI client appears, and whenever a sign shows that builders go around the road, such as environment requests by ticket.
- **Who:** the platform owner owns the road; the architect owns the templates and patterns; the security reviewer owns the identity patterns; the AI lead is consulted.
- **Steps:**
  1. Route makers to personal environments, in environment groups whose rules apply to every environment in the group.
  2. Publish templates, a pattern library, and the approved connectors and MCP servers, each with a named owner.
  3. Put a rules file in each repository that AI coding agents read. It says what they may do and which governance questions to ask before they build.
  4. Provide pipelines the organization owns. They build once, promote the tested artifact, and record each deployment as their last step (G9).
  5. Have identities ready: people sign in as themselves; pipelines use workload identity federation, with no stored secrets; each agent gets its own agent identity with a human sponsor; unattended apps get their own narrow identity (G7).
  6. Publish the road at the front door, and review each piece when a signal fires.
- **Done when:** every builder lands in a governed environment, starts from a template, ships through a pipeline the organization owns, and has the identity they need, all without a ticket.
- **Evidence:** links to the routing rules and environment groups, the catalog of templates and approved connectors and MCP servers with their owners, the pipeline mapped on each app record, and the deployment records.
