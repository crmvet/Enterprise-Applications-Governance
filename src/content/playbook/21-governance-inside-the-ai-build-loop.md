# Chapter 21. Governance inside the AI build loop

- **Version:** 0.2 draft, 2026-11-22
- **Track:** IT
- **Parties:** builders and their AI clients, the platform team, architects, owners and approvers
- **Guidelines:** [G6](../guidelines/G06-map-dependencies.md), [G8](../guidelines/G08-decide-how-apps-reach-dataverse.md), [G17](../guidelines/G17-ai-drafts-people-approve.md), [G20](../guidelines/G20-keep-required-documents-current.md), [G22](../guidelines/G22-let-the-ai-builder-ask-before-it-builds.md)
- **Processes:** [P01](processes.md#p01-intake-and-triage), [P02](processes.md#p02-tier-assessment-and-required-approvals), [P05](processes.md#p05-deployment-record), [P07](processes.md#p07-documentation-tracking), [P10](processes.md#p10-ai-draft-and-human-accept)

## Why this chapter exists

"The AI client built the app before anyone asked what tier it was." "We found the new connector and the restricted data at the go-live review." An AI client acts on what it can reach while it works. If governance lives only in a review at the end, the AI has already chosen the data, the identity and the audience by the time anyone looks. This chapter shows how the AI builder itself asks the governance questions while it builds, through the product's own MCP server, and where a person decides.

## How AI changes this

- The builder is now a person plus an AI client. The AI client writes most of the code, the intake text and the documents.
- So the AI client is where governance must speak. A rule the AI client cannot reach while it works is a rule it will not follow.
- Speed does not move accountability. The AI client drafts and asks; a named person decides on the decision page.

## Old paradigm to avoid

**[4. Governance checked only at the end](17-old-paradigms-and-how-not-to-slide-back.md#4-governance-checked-only-at-the-end).** Here it shows up as a go-live review that meets an app the AI built last week, which ends in rework or a rubber stamp. Set the tier when the idea arrives, have the AI client ask what the tier needs before it builds, and let review triggers fire early. **Slide-back signal:** an app's first register entry appears at go-live or as a gap check claim, the tier is set after the build, or reviews open in the last week before release.

## What good looks like

- The organization's AI clients call the product before they build or change an app, and the answers shape the build.
- The tier is proposed when the idea arrives, and a person sets it before the build goes far.
- Every AI client call runs as the signed-in person, so the person's own access is the boundary.
- Everything an AI client writes starts as a draft, a proposal, a link or a fact as reported, and shows the AI client and the person it acted for.
- Every decision in the loop is made by a named person on the decision page, or under a pre-authorized rule a named person approved on record.
- The organization's own pipeline deploys, and the deployment is recorded with its flags.

## The loop in seven steps

The diagram "The governed AI build loop" shows these seven steps as one loop.

1. **A person asks.** A builder asks an AI client for an app or a change. The product's demo and guides use Claude Code; the product is designed so GitHub Copilot and other MCP clients use the same contract.
2. **The AI client asks first.** Before it builds, the AI client calls the product's MCP server, as the signed-in person.
3. **The product is designed to answer.** The proposed tier and why, what the tier needs, the owners and deputies, what depends on the app, which documents are missing, and what needs a person.
4. **The AI drafts.** It writes the app or the change in your repository. It saves the intake, the registration or the change as a draft in the product. It writes the documents in your own wiki, repository or SharePoint, then records their links.
5. **A person decides.** The AI client submits, and a submission is a request, never an approval. The decision goes to the accountable person, who answers on the decision page in the web app; Teams and Outlook notices are designed to link to it. The AI cannot answer it. After a rejection, a person can revise the work and propose it again.
6. **Your pipeline deploys.** Power Platform Pipelines, Azure Pipelines or GitHub Actions. Build once, then promote the tested artifact. By design, the product never runs the pipeline.
7. **The deployment is recorded.** The pipeline's last step is designed to record what, where, when and which pipeline, with the pipeline's own workload identity and not through MCP. A deployment that came another way, lacked its approval or landed outside its window is designed to be flagged.

The next change starts with the builder asking "what needs me?", and the loop turns again.

## The questions the AI builder asks

Tool names are design names and may change before a release. The plain question comes first. Every tool below is designed to run as the signed-in person and to return only what that person may see.

| Question | Tool | What comes back | What the AI client may write | The state it can leave |
|---|---|---|---|---|
| Who am I here, and what may I do? | `whoami` | The person's product roles and the groups that carry them, the tools they may use, and the configuration version in force | Nothing | Read only |
| Does this exist already? | `check_duplicates`, `find_applications` | Possible matches, each with the reason it matched | Nothing | Read only |
| Which questions set the tier? | `get_tier_questions` | The plain tier questions, their answers and the tier each answer implies | Nothing | Read only |
| Which tier do my answers suggest? | `answer_tier_questions` | A suggested tier, the answers that drove it, and the reviews it triggers | Its answers, stored as a tier assessment | Draft: a suggestion, never a decision. A person sets the tier |
| What does my tier need? | `get_requirements` | The gates, approvals, documents, reviews and gate levels that apply, the guideline behind each, and which are already met | Nothing | Read only |
| Why does it need that? | `explain_rules` | The rule that applied to this record, and why the broader rules dropped out | Nothing | Read only |
| Who owns it? | `get_application_owners` | Each owner role, its holder and deputy, and any gaps | Nothing | Read only |
| What depends on it? | `get_dependency_impact` | The apps that depend on it, with their tier and owners | Nothing | Read only |
| Which documents are missing or stale? | `get_documentation_status` | The required documents, with each link, status, owner and last check | Nothing | Read only |
| Does my change fit a standard model? | `check_standard_change` | Yes with the model, or the conditions it failed | Nothing | Read only. A person sets the change type |
| Start the intake, register an app already built, or start the change | `save_intake_draft`, `save_application_draft`, `save_change_request_draft` | The draft, what it needs next, and the suggested next tool | A draft. For a registration, its parts as links and proposed owners as a proposed ownership decision | Draft, or Proposed for ownership. A registration needs the business owner's confirmation |
| Here is the page I wrote | `record_document_link` | The link, its status and the date it goes stale | The link, and the status as reported | Link. A "current" on a gate-relevant document stays a draft until its owner confirms |
| Please review this design | `request_architecture_review` | The review, now in the architect's queue | A request | Submitted: a request, never a decision |
| Send it for approval | `submit_record` | The approval requests opened, and who must answer each one | The submission, made for the person | Submitted: a request, never an approval. Only where your configuration allows AI submission for that kind of record |
| Who still has to approve? | `get_approval_status` | Who was asked, who answered, and what still counts. An AI recommendation shows but never counts | Nothing | Read only |
| This version reached this target | `record_deployment` | The deployment record and its flags | A fact as reported, for example after a manual import | As reported. The flags are designed to be computed by the product, never set by the caller |
| What needs me? | `get_my_queue` | Approvals, reviews, documents, claims and drafts waiting for the person | Nothing | Read only. Approvals point to the decision page; the tool is designed never to offer to answer them |

## What needs a person (default)

| Act | What counts as a person's yes |
|---|---|
| Any decision: a tier, a stage, an ownership change, a design, a change authorization, a configuration change | An approval request to the holder of the accountable role, answered on the decision page. The AI cannot answer it, and no tool an AI client is served approves |
| Registering an app the AI created | The business owner's confirmation. Until then it stays a draft registration |
| Marking a gate-relevant document current | The document owner's confirmation. Until then the AI's "current" stays a draft |
| Submitting an intake, a change, a decision or a review request | The same call, made for the person, where your configuration allows it. It opens the approval requests; it never approves |
| Recording a link, a deployment or a change result, or running a check | No separate acceptance. The record shows the AI client and the person it acted for |
| A pre-authorized decision, such as a standard change on an approved model or a lowest-tier intake | No new approval request. A person approved the rule once, on record, and the decision links that approval. Only a configuration change a person accepts can add or widen such a rule |

**Why the decision page.** The product's MCP server is designed to act with the person's own sign-in, so it cannot prove that the person, and not the AI, pressed a button inside the AI client. So the one tool that records an answer is served only to a person signed in to the web app, never to an AI client, and the person's own sign-in proves who answered. The decision page is the channel it is designed to trust. An AI stage in your own approval flow is designed to be recorded as a recommendation that never counts.

## Tell the AI client to ask first

Put a rules file in every template repository, in the instructions file your AI client loads at the start of a session. A short example:

```text
Governance: read before you build or change this app.
1. Ask the governance server who I am and what I may do.
2. Check for an existing app before you start a new one.
3. Ask what this app's tier needs: approvals, documents and reviews.
4. Ask who owns it and what depends on it before you change a shared part.
5. Save intakes, registrations and changes as drafts. Never mark anything approved.
6. Write required documents in this repository or its wiki, then record their links.
7. Tell me what needs a person's decision, and who is asked.
```

This is practice, not a Microsoft rule. It is also not access control. A rules file reminds the AI client to ask; the product is designed to enforce rights in the service, whatever a prompt or a rules file says.

## Identity in the loop

- **As the signed-in person, end to end.** The AI client signs the person in with Microsoft Entra ID. The product's MCP server is designed to validate the person's token and then act as that person: in its own engine from the person's identity and roles, and toward Microsoft services through the on-behalf-of flow, which gives the person no access they do not already have.
- **Tokens are never forwarded.** The server is designed never to pass the incoming token on to another service, never to store a token between calls, and never to answer with one shared identity's rights. Any gateway in front of it must pass the caller's own token through, never a shared backend credential.
- **The person's access is the boundary.** An AI client is designed to see and do only what the person may. The product's roles are designed to reach people only through Microsoft Entra groups.
- **Every write says who.** Each record is designed to carry the actor kind, the AI client and the person it acted for, stamped from the token and never from a field the caller sends.
- **Pipelines do not use MCP.** A pipeline has no model choosing tools. Its last step is designed to call a plain record endpoint with the pipeline's own workload identity, which holds the deployment recorder role through a recorded, expiring grant.
- **Agent identities get no workload grant.** An agent that acts for a person is designed to use that person's delegated access. An agent's own identity is designed never to get a product workload role.
- **AI never approves.** No tool an AI client is served approves, rejects, accepts, authorizes, sets a tier, moves a stage, activates configuration or assigns a role; the one answer tool is served only to a person in the web app. The engine is designed to refuse those writes from an AI client whatever the tool or route, and configuration cannot loosen that lock. Only a person's answer counts.

## Who decides (default decision rights)

| Decision or task | Accountable | Does the work | Consulted | AI may |
|---|---|---|---|---|
| Open a tool to a role, or close it | Configuration admin | Configuration admin, through a configuration change accepted on the decision page | Platform owner, security reviewer, AI lead | Prepare the configuration change |
| Allow AI submission for a kind of record | Configuration admin | Configuration admin, through a configuration change accepted on the decision page | AI lead, change manager | Prepare the configuration change |
| Choose which document types need a person's confirmation to count as current | Configuration admin | Configuration admin | Architect, AI lead | Prepare the configuration change |
| Keep the rules file in each template repository | Architect | Platform admin | AI lead | Draft the file for a person to review |
| Every decision the loop produces | The accountable role for that decision, on the decision page | The requester and their AI client | As the decision rights say | Draft and recommend; never decide |

## Guidelines this chapter applies

- **G22 Let the AI builder ask before it builds.** The tier, what it needs, the owners, the dependants and the missing documents, asked before the build. The AI drafts what it learns; people decide.
- **G17 Let AI draft and recommend; people approve.** The AI client drafts and submits; only a person decides, on the decision page.
- **G6 Map what each app depends on, before you release or retire it.** The AI client asks what depends on an app before it changes a shared part.
- **G8 Decide how each app reaches Dataverse.** As the signed-in person by default. The product's own MCP tools follow the same rule.
- **G20 Keep each app's required documents current.** The builder's AI client writes the documents in your own tools as part of each change, and records their links.

## Processes this chapter uses

- **P01 Intake and triage.** The AI client checks for duplicates, drafts the intake or the registration, and submits.
- **P02 Tier assessment and required approvals.** The AI client proposes tier answers, a person sets the tier, and the product is designed to work out what the tier needs.
- **P07 Documentation tracking.** What is missing or stale, and the links the AI client records.
- **P10 AI draft and human accept.** Drafts, performed-by marks, and which channel counts as a person's yes.
- **P05 Deployment record.** The pipeline's record step that closes the loop.

## How the product is designed to support it

- It is designed to have its own MCP server, next to Microsoft's MCP servers, with governance tools grouped in toolsets such as session, portfolio, intake, decisions, change, deployment and documentation.
- It is designed so a caller sees only the tools its roles allow, and can narrow the list to the toolsets it needs, so the AI client chooses from a short list.
- It is designed so that which roles may call which tool, how far an AI client may move a record, and whether an AI client may submit each kind of record are configuration rows, changed only through a configuration change a person accepts.
- No tool an AI client is served approves; the one answer tool is served only to a person in the web app. It is designed to refuse approval writes from an AI client on every route.
- It is designed to return, with every answer, the configuration version in force and a link to the record, and to mark text people typed as data, never as instructions.
- It is designed to limit calls per person, so one runaway agent cannot flood the record.

## What you can tailor

- Which tools are open to which roles.
- How far an AI client may move a record through each tool, inside the lock that AI never approves.
- Which kinds of record an AI client may submit for a person.
- Which document types need a person's confirmation before they count as current.
- The wording of the rules file in each repository.

## Microsoft guidance it applies

- [Govern agents by risk](https://learn.microsoft.com/agents/center-of-excellence/govern-agents-risk), checked 2026-11-22: "Apply the tiers when an idea arrives." "Treat the recommendation as an initial assessment, not approval or a substitute for release controls." Microsoft writes this about agents. This playbook applies the same rule to apps built with AI.
- [Design an agent front door for your Center of Excellence](https://learn.microsoft.com/agents/center-of-excellence/agent-front-door), checked 2026-11-22: "Designated reviewers make approval decisions, and every build must meet the release requirements for its risk tier." "Instructions that tell an agent not to reveal other people's requests aren't an access control."
- [Connect to Dataverse with Model Context Protocol](https://learn.microsoft.com/power-apps/maker/data-platform/data-platform-mcp), checked 2026-11-22: Dataverse can act as an MCP server for "various MCP clients like Copilot Studio agents, Visual Studio (VS) Code GitHub Copilot, GitHub Copilot CLI, Claude desktop, Claude Code, and many others."
- [Configure built-in MCP server authorization (Preview)](https://learn.microsoft.com/azure/app-service/configure-authentication-mcp) (Azure App Service), checked 2026-11-22: "Pass-through scenarios where the server forwards its token create security vulnerabilities, so avoid these patterns." The page says to obtain a new token through the on-behalf-of flow instead.
- [Deploy Azure MCP Server with on-behalf-of authentication](https://learn.microsoft.com/azure/developer/azure-mcp-server/how-to/deploy-remote-mcp-server-on-behalf-of), checked 2026-11-22: "The OBO flow uses delegated permissions. It doesn't grant a user access that they don't already have through Azure role-based access control (RBAC) or the downstream service."
- [About wikis, READMEs, and Markdown](https://learn.microsoft.com/azure/devops/project/wiki/about-readme-wiki) (Azure DevOps), checked 2026-11-22: publishing code as a wiki "provides a way to maintain your code documentation synchronously with your code base".

## Done when

- The organization's AI clients ask the product before they build or change an app.
- Every AI-made record shows the AI client and the person it acted for.
- Every decision in the loop was made by a person on the decision page, and no approval has an AI as its only approver.
- Every production deployment in the loop came through the organization's own pipeline and is recorded.
