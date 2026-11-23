# Chapter 18. Who may build with AI, and how

- **Version:** 0.2 draft, 2026-11-22
- **Track:** Organization
- **Parties:** leaders and sponsors, business owners, the AI lead, the PMO; IT (the platform team and security) consulted
- **Guidelines:** [G2](../guidelines/G02-name-owners-and-deputies.md), [G3](../guidelines/G03-set-a-risk-tier.md), [G16](../guidelines/G16-record-ai-use.md), [G17](../guidelines/G17-ai-drafts-people-approve.md), [G23](../guidelines/G23-say-who-may-build-with-ai.md)
- **Processes:** [P01](processes.md#p01-intake-and-triage), [P02](processes.md#p02-tier-assessment-and-required-approvals), [P10](processes.md#p10-ai-draft-and-human-accept), [P11](processes.md#p11-configuration-change)

## Why this chapter exists

"Can I use AI to build this?" "Which AI tool are we allowed to use?" "Who said yes?" Many organizations answer with silence, a ban or a free-for-all. Silence and a ban push AI work out of sight. A free-for-all loses track of who owns what. This chapter is the organization's answer: who may build with AI, with which AI clients, on which path and at which tier, and who decides. IT's half of the answer, the paved road these paths run on, is in [chapter 20](20-the-paved-road-for-ai-builders.md).

## How AI changes this

Anyone can now produce a working app with AI, often in a day. Microsoft's own tools now build apps with AI as well. For apps built with Microsoft Copilot Managed Runtime, Microsoft's page on [personal environments](https://learn.microsoft.com/power-platform/admin/personal-environments-overview) says "app building is on by default for eligible makers". So the question is no longer "who can build?". It is "who may build what, at which tier, and with which AI clients?". The answer must be published, short and easy to follow, or people will make up their own.

## Old paradigm to avoid

[6. Ban AI tools until a policy exists](17-old-paradigms-and-how-not-to-slide-back.md#6-ban-ai-tools-until-a-policy-exists). Here it shows up as a front door that answers "wait for the policy", or a list of AI clients that only ever says no. People then build with AI anyway, in tools nobody sanctioned and with no record. The opposite failure is just as costly: anyone, any tool, any data, and no owner. **Signal:** discovery of unsanctioned AI tools rises; apps registered as "no AI use" turn out to be built with AI; the front door answers "wait for the policy".

## What good looks like

- The organization publishes its builder paths: who may build with AI, at which tier, with which AI clients and where.
- Every builder knows their path. Moving up a path is a short, recorded step, not a project.
- The sanctioned AI clients are listed for each path, and IT has allowed exactly those in Microsoft's settings and in the clients' own settings.
- AI use is recorded on every app at intake, and every record shows whether a person, a pipeline or an AI client acting for a person made it.
- No path lets an AI approve anything. AI drafts; a named person decides.
- Unsanctioned AI use is read as a sign of unmet need. The answer is a sanctioned option, not only a block.
- The paths are reviewed each quarter, and they widen as people show they use them well.

## Builder paths by tier (default)

The playbook starts with three paths, one for each default tier (chapter 4). The paths sit on top of the build lanes in chapter 8: the lane says how an app is built and supported; the path says who may build with AI, at which tier, with which clients.

| | Personal path | Partnered path | Professional path |
|---|---|---|---|
| Tier | 1 | 2 | 3 |
| Who may build | Anyone who has finished the basic AI building course | Trained makers, each with a named coach from the platform team | Professional teams: IT, or a fusion team that IT leads |
| Sanctioned AI clients | The clients on the organization's list for everyone | The list for everyone, plus the clients approved for shared business data | The clients approved for code repositories and work bound for production |
| Where they build | Their own personal environment, in the environment group for personal work | A shared development environment in the environment group for partnered work | Team development environments and repositories |
| How it reaches production | It stays with the builder or a small team. When more people rely on it, it graduates | Through a pipeline the organization owns | Through a pipeline the organization owns, with full release management |
| What the AI client asks first ([chapter 21](21-governance-inside-the-ai-build-loop.md)) | Does this exist already? Which tier do my answers suggest? What does that tier need? | The same, plus who owns it, what depends on it and which documents are missing | All of these, plus which reviews the tier answers trigger |
| What needs a person | The business owner confirms an app the AI registered. The platform admin confirms the tier | The tier decision, the business owner's change authorization, and the data owner's yes for a new data use | The design and security reviews, the change manager's authorization, and the business owner's risk acceptance |

- **Moving up.** An app can start on a lower path and move up. A graduation trigger (chapter 8) opens a new tier decision, and the new tier sets the new path.
- **Moving down.** A builder on a higher path may always build on a lower one.
- **One security group per path.** Give each path its own Microsoft Entra security group. Microsoft's environment routing then places each builder in the right place: "Routing rules based on security groups place different makers in different environment groups, each with its own rules." ([What is a personal environment?](https://learn.microsoft.com/power-platform/admin/personal-environments-overview)). Putting a builder on a path is then one group membership.
- **Microsoft's words.** The paths map to Microsoft's three Copilot Studio zones: the personal path maps to Zone 1 (citizen development), the partnered path to Zone 2 (partnered development) and the professional path to Zone 3 (professional development). Microsoft wrote the zones for Copilot Studio agents. The playbook applies the same split to apps built with AI. No Microsoft page joins the two vocabularies; the mapping is the playbook's own default. Microsoft's Zone 2 agents "require formal assistance and oversight from a coach, but are built by trained citizen developers" ([Implement a zoned governance strategy](https://learn.microsoft.com/microsoft-copilot-studio/guidance/sec-gov-phase2)). That is where the partnered path's coach comes from.

## Sanctioned AI clients

The organization decides which AI clients may connect to its data and to the product. IT then allows exactly those clients (chapter 20).

- **The organization's list.** For each path, list the AI clients people may use, what each may reach, and who sanctioned it. Start from what people already use. When many people use a tool nobody sanctioned, treat it as a request, not only a breach.
- **What IT switches on.** Microsoft gives IT the switches. For Dataverse data, Microsoft says of its Dataverse MCP server: "By default, the Microsoft Copilot Studio client for Dataverse MCP is enabled for all environments. You must enable the additional clients in the Power Platform admin center before you can connect to the client." ([Configure the Dataverse MCP server for an environment](https://learn.microsoft.com/power-apps/maker/data-platform/data-platform-mcp-disable)). For any client that signs in with Microsoft Entra, "Before an application can access your organization's data, a user must grant the application permissions to do so" ([Configure how users consent to applications](https://learn.microsoft.com/entra/identity/enterprise-apps/configure-user-consent)), and IT decides who may grant that consent. Some AI clients also offer managed settings for organizations; where a client offers them, IT uses them too.
- **The product is one destination.** An AI client reaches the product through the product's own MCP server, signed in as the person. The product is designed so that a sanctioned client can never do more than the person it acts for.
- **One client, one decision.** Sanctioning a client is a recorded decision by the security reviewer, with the AI lead consulted. It names the paths the client is allowed on and the data it may reach.

## The AI lead

The playbook's default has an AI lead: one person, with a deputy, who keeps the AI side of governance working. The product's default configuration is designed to include this role. The AI lead:

- keeps the AI guidelines (G16, G17 and G23) and the builder paths current;
- proposes which AI clients to sanction, and the security reviewer decides;
- is consulted when the security reviewer sets an agent's autonomy level (chapter 11);
- decides on agents Microsoft flags as having no owner: keep, reassign, consolidate or retire;
- runs enablement with the platform team;
- watches the AI signals: unsanctioned AI use, AI drafts nobody accepted, and apps registered as "no AI use" that were built with AI.

Rename, merge or drop the role. In a small team the platform admin can hold it. With strict separation, a different person must. If you drop it, give each duty above to a named role.

## Enablement

Access without training is a gamble. Training without access is theory. Microsoft's Cloud Adoption Framework says organizations "should pair access with training so employees understand how to use approved AI capabilities effectively and responsibly" ([AI adoption](https://learn.microsoft.com/azure/cloud-adoption-framework/ai/strategy)).

- **Training by role.** A short basic course opens the personal path. A longer course, with a coach, opens the partnered path. Business owners learn to read a tier and answer an approval card. Approvers learn to read an AI draft before they say yes.
- **Champions.** Find the early builders who go further than the training asks. Give them early access, connect them to each other, and let them answer questions where people work.
- **Office hours.** Hold recurring sessions where builders bring an app, or an AI client that is stuck, and get unblocked live.
- **Leaders who build in the open.** Sponsors and managers use AI for some of their own work and say so.
- **Paths, not a policy document.** The front door and each repository's rules file say which path applies, so nobody has to read a long document to start.

## How it works

1. **Draft the paths.** The AI lead starts from the default paths above, with the platform owner, the security reviewer and the PMO. An AI client can draft them.
2. **Name the AI lead.** Name a person and a deputy, or give each AI lead duty to a named role.
3. **List the sanctioned AI clients.** For each path, list the clients, what they may reach, and the security reviewer's decision for each.
4. **Switch them on.** IT allows exactly those clients in Microsoft's settings and in the clients' own settings, and binds each path to a security group (chapter 20).
5. **Publish the paths.** The sponsor approves the paths. They go on the front door (chapter 3) and into the template for each repository's rules file.
6. **Train and enable.** Run the courses, name the champions and start office hours.
7. **Record AI use.** At intake, every app records whether it uses AI or is an agent (G16), and the record shows which AI client did what for whom.
8. **Review each quarter.** The AI lead brings the signals to the sponsor. Unsanctioned use leads to a sanctioned option or a recorded block. Paths widen where people use them well.

## Who decides (default decision rights)

| Decision | Accountable | Does the work | Consulted | AI may |
|---|---|---|---|---|
| Set the builder paths, and who may take each | Sponsor | AI lead | Platform owner, security reviewer, PMO, architect | Draft the paths and the training plan |
| Sanction an AI client for a path | Security reviewer | Platform admin | AI lead, platform owner, data owner | Gather the client's facts for the review |
| Put a builder on the partnered path | Platform admin | Platform admin | The builder's coach, the AI lead | List who has finished the training |
| Review the paths each quarter | Sponsor | AI lead | PMO, platform owner, security reviewer | Gather the signal data and draft the review |

## Guidelines this chapter applies

- **G23 Say who may build with AI, and on which path.** This chapter is its home: publish the paths, sanction the clients people need, and review the paths as use grows.
- **G3 Set a risk tier for every app, on tiers you define.** The tier picks the path. A new tier moves the app to a new path.
- **G16 Record AI use on every app and agent.** Intake asks whether the app uses AI or is an agent, and the record keeps the answer.
- **G17 Let AI draft and recommend; people approve.** No path, at any tier, lets an AI approve.
- **G2 Name the owners of every app, each with a deputy.** An app built with AI still has a business owner and a technical owner, each a person with a deputy.

## Processes this chapter uses

- **P01 Intake and triage.** The front door tells a builder which path applies and asks about AI use.
- **P02 Tier assessment and required approvals.** The tier decision sets the path, and a new tier decision moves the app up.
- **P10 AI draft and human accept.** What an AI client may write, and what needs a person's acceptance.
- **P11 Configuration change.** Roles, tool access and what an AI client may submit are configuration. The AI lead is consulted on AI rules.

## How the product is designed to support it

- It is designed to ship an AI lead role in its default configuration, which each organization can rename, merge or drop.
- It is designed to ask at intake whether an app uses AI or is an agent, and to keep the AI use, the agreed autonomy and the build path on the application record.
- It is designed to stamp every record with who performed it, including "AI client X for person Y".
- It is designed so its MCP server acts as the signed-in person, so an AI client can never do more than the person it acts for.
- It is designed to keep, as configuration, which roles may call which tools and which kinds of record an AI client may submit for a person.
- It is designed to tell a builder, for the app in front of them, its tier and what that tier needs.
- By design it never grants environments, licenses or client access. Those stay in Microsoft's tools and in the clients' own settings.

## What you can tailor

- How many paths you have, their names and their tiers.
- Who may take each path, and the training each one needs.
- The sanctioned AI clients for each path, and what each may reach.
- Whether you have an AI lead, and which decisions it holds.
- Which kinds of record an AI client may submit for a person.
- How often you review the paths.

## Microsoft guidance it applies

Most of this guidance is written for AI agents. The playbook applies the same rules to apps built with AI.

- [Enable makers and grow the community](https://learn.microsoft.com/agents/center-of-excellence/enablement-community) (agent Center of Excellence guidance), checked 2026-11-22: "Enablement gives people a clear path and helps them move faster." Also "Leaders who use agents openly are a strong driver of adoption."
- [Why does Shadow AI governance matter for the enterprise?](https://learn.microsoft.com/microsoft-agent-365/guidance/govern-shadow-ai) (Microsoft Agent 365 guidance), checked 2026-11-22: "The risk isn't that employees use AI; it's that an organization cannot account for the AI it doesn't know about." Also "Shadow AI is also evidence of unmet need, not only a policy violation", and "the durable move is to make the governed route the easier one".
- [Implement a zoned governance strategy](https://learn.microsoft.com/microsoft-copilot-studio/guidance/sec-gov-phase2) (Copilot Studio), checked 2026-11-22: three zones for agent creation. In Zone 1, "Anyone can make personal and team productivity agents based on content they already have access to." In Zone 3, "Pro developers build mission-critical, enterprise-grade agents for use across the organization."
- [What is a personal environment?](https://learn.microsoft.com/power-platform/admin/personal-environments-overview) (Power Platform), checked 2026-11-22: "Environment routing takes makers to their personal environment and creates it on their first visit, so they don't need to request an environment or choose where to build." Also "Routing rules based on security groups place different makers in different environment groups, each with its own rules."
- [AI adoption](https://learn.microsoft.com/azure/cloud-adoption-framework/ai/strategy) (Cloud Adoption Framework), checked 2026-11-22: "Broad access to approved AI capabilities can help organizations identify valuable uses for AI and build organizational experience." Also "Organizations should pair access with training so employees understand how to use approved AI capabilities effectively and responsibly."
- [Configure the Dataverse MCP server for an environment](https://learn.microsoft.com/power-apps/maker/data-platform/data-platform-mcp-disable) (Power Apps), checked 2026-11-22: "You must enable the additional clients in the Power Platform admin center before you can connect to the client."
- [Configure how users consent to applications](https://learn.microsoft.com/entra/identity/enterprise-apps/configure-user-consent) (Microsoft Entra), checked 2026-11-22: "Before an application can access your organization's data, a user must grant the application permissions to do so."
- [Govern agents by risk](https://learn.microsoft.com/agents/center-of-excellence/govern-agents-risk) (agent Center of Excellence guidance), checked 2026-11-22: "Don't wait for governance to feel complete before you ship. Publish the guardrails, let teams build within them, and refine as you learn."

## Done when

- The builder paths are published, with a date and an owner.
- Every builder knows their path, and each path's security group holds the right people.
- The sanctioned AI clients are listed for each path, each with a recorded decision, and allowed in Microsoft's settings.
- The AI lead is named with a deputy, or each of its duties has a named role.
- The paths were reviewed in the last quarter.
