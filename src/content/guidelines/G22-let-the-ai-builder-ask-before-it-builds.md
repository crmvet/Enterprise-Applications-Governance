# G22. Let the AI builder ask before it builds

- **Number:** G22
- **Version:** 1.0, 2026-11-22
- **Status:** active
- **Home chapter:** 21 (Governance inside the AI build loop)
- **Cited in chapters:** 0, 1, 3, 16, 17, 21
- **Processes:** P01, P02, P07, P10

**Rule.** Before an AI client builds or changes an app, have it ask the governance questions: the tier, what the tier needs, the owners, the dependants and the missing documents. It drafts what it learns; people decide.

**Why it matters.** When a build takes a day, a review at the end means rework or a rubber stamp. By then the AI has already chosen the data, the identity and the audience. An AI client follows only what it can reach while it works, so governance has to answer inside the build, not after it.

**How the product is designed to support it.** It is designed to answer these questions through its own MCP server, for the signed-in person, while the AI client builds. It is designed so a tier answer from the AI client is a draft assessment, never a decision. No tool an AI client is served approves anything. The answer tool, `answer_approval`, is served only to a person signed in to the web app, who answers on the decision page.

## Microsoft anchors

- [Govern agents by risk](https://learn.microsoft.com/agents/center-of-excellence/govern-agents-risk) (agent Center of Excellence guidance, written for agents), checked 2026-11-22: "Apply the tiers when an idea arrives." "Treat the recommendation as an initial assessment, not approval or a substitute for release controls."
- [Design an agent front door for your Center of Excellence](https://learn.microsoft.com/agents/center-of-excellence/agent-front-door) (agent Center of Excellence guidance), checked 2026-11-22: "Designated reviewers make approval decisions, and every build must meet the release requirements for its risk tier."
- [Connect to Dataverse with Model Context Protocol](https://learn.microsoft.com/power-apps/maker/data-platform/data-platform-mcp) (Power Apps), checked 2026-11-22: Dataverse can act as an MCP server for "various MCP clients like Copilot Studio agents, Visual Studio (VS) Code GitHub Copilot, GitHub Copilot CLI, Claude desktop, Claude Code, and many others." MCP is already one way these AI clients reach business data while they work.

## Working shape

- **When:** when an idea arrives, before an AI client starts a build or a change, and again whenever the app's data, audience or autonomy changes.
- **Who:** the builder and their AI client ask; the platform team keeps the repository rules file; the owners and approvers decide.
- **Steps:**
  1. Put a rules file in each repository that tells AI clients to ask the governance questions before they build or change anything.
  2. The AI client checks whether the app already exists, gets the tier questions, and drafts the answers for the person to check.
  3. It asks what the tier needs, who owns the app, what depends on it, and which documents are missing or stale.
  4. It drafts the intake or the change, and writes the documents in the team's own tools.
  5. It submits for the person. A submission is a request; a person decides on the decision page in the web app (G17). An install may also switch on the approval runner (the optional flows that send approval notices). It is designed to send an answerable card in Outlook or Teams and relay the answer. The product takes a relayed card answer only while step-up sign-in (a fresh sign-in before each answer) is off.
- **Done when:** every app or change started with an AI client has a register entry from before the build, and every decision on it was made by a person.
- **Evidence:** intake and change records created before the first deployment, the performed-by marks on them, and the approval history with human approvers.
