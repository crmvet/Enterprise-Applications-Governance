# Concepts

The words the product uses, what each one means, and where it shows in the web app. The examples come from the
Balanced preset (`config/presets/balanced`), the configuration the demo runs on. An install can rename, add and
retire most of them through a configuration change; the engine meanings named below are fixed.

## The register

- **Business application.** The register's main record, of an application kind such as an app, an agent, an AI-built
  app, a code app, an API, an MCP server, a SaaS product, an external system, a Copilot managed runtime app or a practice
  app. Each has a record number such as `APP-0042`, a
  registration status, a lifecycle stage, a tier, owners, and the facts that hang off it: components, dependencies,
  deployments, documents, reviews, decisions and data uses. **Application 360** (`/app/records/business-application/<id>`)
  shows all of it on one page; **Applications** (`/app/applications`) is the list, with search and filters.
- **Components.** The technical parts of an application, such as a canvas app, a cloud flow, a solution, a Copilot
  Studio agent, an Entra agent identity or a repository. The product stores a link to each (an ID, a URL, a label and
  a last seen date), never a copy. The **gap check** compares the Power Platform inventory with the register and lists
  what no application claims.
- **Dependencies.** One application depends on another. The **dependency map** and the impact walk
  (`get_dependency_impact`) show what breaks when one changes or retires, starting from an application, a component or
  a data location.
- **Places.** The deployment targets where releases land, each with a stage (such as test or production) and a change
  path, and the pipelines that deploy to them. **Deployments** lists what reached which target, with the flags the
  engine raises, such as a release whose only code approval came from an AI.
- **Portfolios.** A tree of portfolios that groups applications. The **Overview** and **Portfolios** pages summarize
  each one.
- **Record numbers.** The engine numbers the records people talk about: `APP-` applications, `IR-` intake requests,
  `DEC-` decisions, `CHG-` change requests, `REV-` reviews, `DU-` data uses and `CFG-` configuration drafts. The search
  box in the top bar finds a record by its number.

## Lifecycle, gates and tiers

- **Lifecycle stages.** Balanced has eleven, from Idea, Intake and triage and Prioritize and fund, through Design and
  architecture review, Risk and tier, Build, Test and Change and release, to Operate, Review and Retire. Each stage has
  an engine meaning: pre-live, live or retired.
- **Gates.** A gate sits after a stage and is passed by an accepted decision of its type: Accept (an intake decision),
  Commit (a priority decision), Design (a design decision), Go-live (a go-live decision) and Value (a portfolio
  decision). A gate has requirements, such as a current runbook or a decided data use, and the gate decision waits
  until they are met. When a gate decision is accepted, the application moves to the next stage. Application 360 shows
  the stepper with each gate and what it still needs.
- **Tiers.** Tier 1 is personal or team use with no sensitive data. Tier 2 is department use, or touches internal data.
  Tier 3 acts under its own identity, touches restricted data, reaches the whole organization or outside users, or
  executes changes as an agent. A requester answers the tier questions (`answer_tier_questions`), the answers suggest
  a tier, and a tier decision confirms it. The tier decides which approvals, reviews and documents an application
  needs (`get_requirements`).

## Owners

- **Owner roles** are held on one record: an application, a data domain or a data location. Balanced has business
  owner (one holder and a deputy), sponsor, data owner, data steward, process owner, technical owner, support owner,
  security owner and builder. A holder is a person or an Entra group; a group that holds support owner or technical owner
  roles shows on the **Teams** page as a support team.
- **Owner assignments change through decisions.** `propose_owner_change` proposes a new holder, a deputy, a
  successor or an end; an ownership decision asks the people the rules name (under Balanced, the proposed owner by
  name and the business owner, with the sponsor as the fallback), and the engine writes the assignment when the
  decision is accepted. When a successor takes over a role, the old holder's assignment ends on the day the
  successor's starts, so the role is never empty.
- **Owner gaps** are roles with no holder, or no deputy where the role needs one. **Owners** (`/app/owners`) has four
  views: by person, by role, gaps and leavers. A leave date (`record_owner_leave`) opens owner checks on the leaver's
  applications before the day comes.

## Product roles and teams

- **Product roles** say what a person may do across the product: requester, maker, application owner, data owner,
  architect, security reviewer, change manager, change board, PMO, AI lead, platform admin, data governance lead,
  configuration admin, auditor and reader. Each guide in [guide/](README.md#the-guide-per-role) covers one.
- **A person holds a product role only through an Entra group** that the configuration binds to the role (a Role Group
  Binding). The engine reads the groups in the person's token and resolves the roles on every call. Membership is
  managed in Entra, never in the product.
- **Two workload roles** are held by machines through a recorded, expiring Workload Grant, never a group: the
  deployment recorder (pipelines that record deployments) and the approval writer (the approval runner).
- **Teams.** A team is an Entra group the install knows: one a role is bound to, or one that holds owner roles. Its
  kinds are worked out from the configuration on every read: a committee holds a role the rules ask to decide, approve
  or be consulted; a support team holds support owner or technical owner roles; a role group holds any other role.
  **Teams** (`/app/teams`) lists them with who is in them, what they decide, what they support and what waits on them.

## Decisions and approvals

- **A decision** has a type (intake, priority, design, tier, ownership, change authorization, go-live, risk
  acceptance, exception, data use, retirement, autonomy level, flagged agent, configuration change and others), a
  statement, an outcome such as accept or reject, and a status: proposed, then accepted or rejected, or withdrawn; a
  later decision can supersede an accepted one.
- **Decision rights** say who may propose and decide a type, with the letters responsible, accountable, consulted and
  informed, held through a product role or an owner role. `save_decision_proposal` refuses a person who holds no
  right for that type.
- **Approval rules** say who is asked when a decision is submitted (`submit_record`): a product role, an owner role on
  the application, or the proposed owners by name; how many answers count; whether a typed reason is needed; and how
  many days a request stays open. A consultation asks for advice and never counts toward acceptance.
- **Approval requests.** The engine opens one request (an Approval Record) for each answer the rule needs. A person
  answers on the **decision page** (`/app/records/decision/<id>`) with Approve or Reject, and a reason where the rule
  asks for one. The engine checks that the person is the one the request asks, that no separation rule stops the
  answer, and that the request is still open. Teams and Outlook messages, and the optional approval runner's cards,
  link to the same page.
- **The engine accepts.** No person and no tool accepts a decision. The engine accepts it when the counted answers
  meet the rule and, for a gate decision, every requirement of the gate is met. A few decision types the configuration lists may be accepted by the
  engine with no request at all, such as an intake at the lowest tier.
- **Separation rules.** Balanced refuses a proposer's own answer at tier 3 (`no-self-approval-tier-3`) and flags a
  production change whose deployer is its only approver. At tiers 1 and 2 Balanced lets a person answer a request
  about their own proposal; stricter presets refuse it.
- **Expiry and asking again.** A request past its time expires. The proposer of the decision or a platform admin may
  ask again ("Ask again", `renew_approval_request`) once a request has expired or has less than 48 hours left.
- **Approvals** (`/app/approvals`) lists the requests waiting for the person and for others; **Decisions**
  (`/app/decisions`) is the log, with exceptions as a view of their own.

## Changes, reviews and documents

- **Change requests** (`CHG-`) plan a change to an application in a window. The standard change check
  (`check_standard_change`) says whether a change fits a pre-approved model; a change that needs authorization asks
  the business owner, or the change manager at tier 3 and for emergencies. **Changes** and the **Timeline** show the
  schedule. A person closes a change with its result (`complete_change`).
- **Reviews** (`REV-`) are the architecture, security, access, owner, tier, benefit and post-change reviews that a
  tier and a cadence ask for. A review records an outcome decision, and a person marks it done (`complete_review`).
  **Reviews** lists the due, overdue and done ones.
- **Documents** are links to the pages an application needs (a runbook, a support guide), with a status such as
  current, stale or missing. An owner confirms that a document is still true (`confirm_document_checked`).

## The data taxonomy

- **Data domains** are the organization's own tree of business data: domains, subdomains and data sets (the three
  levels). The tree is configuration, so it changes through a configuration change. Each domain can have a data owner
  and a deputy, and data stewards; an owner on a domain also owns the levels below it unless they name their own.
- **Classifications.** Balanced has four, in Microsoft Purview's default label names: Public, General, Confidential
  and Highly Confidential. Each sets the default rules for a domain: whether a use, and an AI use, is allowed or needs a
  decision, and whether a copy outside production is allowed, masked only or never.
- **Rules.** A rule is allowed, decision (a data-use decision must allow it), never, or masked only. A domain inherits
  the rules of the domain above it unless it sets its own.
- **Data locations** are the stores where data sits, from any vendor: a Dataverse environment, a SQL database, a
  SharePoint site, a file share. Each has a platform and a stage, and a technical owner who looks after it.
- **Data mappings** say that a domain's data sits at a location, and where it comes from: the system of record, a
  replica, a production copy, a masked copy or synthetic data. A copy outside production needs a decision when the
  copy rule asks for one.
- **Data uses** (`DU-`) say that an application reads or writes a domain's data, with or without AI. A use that the
  rules ask about needs a data-use decision, which the domain's data owner answers. **A use that breaks a never rule is
  refused when it is saved**, with `rule_violation` and a message that names the rule and the domain it is set on.
- **Data gaps** are what is missing, broken or waiting: a domain with no owner, an application that reads restricted
  data with no decision, a location with no technical owner, a copy that breaks its rule. The **Data** page
  (`/app/data`) has tabs for domains, locations, mappings, uses and gaps, and each gap names the act that fixes it.
- **Modes.** The setting `data-taxonomy-mode` is `domains-optional` in Balanced: an application can declare no data
  uses, and its application-level data owner answers for it. `domains-required` (Regulated) asks for a declared use,
  or a statement that the application holds no business data. `off` turns the data requirements off.

## AI clients and their stamps

- **Actor kinds.** Every write is stamped with who or what made it: a person, an AI client for a person, an agent
  identity, a pipeline, the approval runner or the engine. The actor kind comes from the entry point and the token
  type, never from a field the caller sends.
- **The two human entries.** A person who signs in to the web app acts as themselves: the actor kind is person. An AI
  client such as Claude Code on the MCP endpoint acts for the person who signed in: the actor kind is AI client for a
  person, On Behalf Of names the person, and Client App names the client by the label of its Known Client row.
- **Drafted by.** A row an AI client creates records the client and the person it acted for, and the web app shows
  "Drafted by Claude Code for <person>". It starts as a draft or a proposal, and the person takes it forward, edits it
  or withdraws it. **AI activity** (`/app/ai-activity`) lists that work and every call the server refused.
- **AI never approves.** No tool an AI client is served approves, rejects, accepts or activates anything. The one tool
  that records an approval answer, `answer_approval`, is served only to a person in the web app. The full rules, and
  how far an AI client may go with each tool, are in [mcp.md](mcp.md#what-an-ai-client-may-do).
- **Agents.** An agent user or an agent identity (Microsoft Entra Agent ID) holds no product role on any entry. An
  answer from one is stored as a recommendation and never counts.

## Tool Policy

Every tool has one Tool Policy row: whether it is enabled, the product roles that may call it, whether it needs an
owner role, the furthest state an AI client may take a write to (Max AI State), whether an AI client may submit each
kind of record (AI May Submit), and an optional limit of calls per person per hour. The server lists for each caller
only the tools their roles may call, and every write path still goes through the engine's write guard. The rows are configuration: an install
changes them through a configuration change. Max AI State can only tighten the states each tool's contract allows.
Balanced ships no Tool Policy rows, so the shipped defaults (`mcp/contracts/tool-policy.seed.json`) apply; a
configuration that adds Tool Policy rows replaces the whole set, so it lists every tool. The generated tool list in
[mcp.md](mcp.md#the-tools) shows the shipped defaults; **Admin** and **Roles and access** show the rows in
force.

## Configuration versions

- **Presets.** The product ships full presets (Balanced, Starter, Regulated and the Microsoft CoE baseline) and
  overlays that change one area (small team, strict separation, linked change management, a data domains starter).
  [admin.md](admin.md#configuration-and-presets) lists them.
- **One version in force.** The configuration in force is one numbered Configuration Set Version. Every successful answer
  names it (`configurationSetVersion` in the envelope), and every operational row records the version in force when it was
  created.
- **Changes are drafts, then decisions.** A configuration admin, or their AI client, proposes a change
  (`propose_configuration_change`), which stores a draft (`CFG-`) with its diff and the rules check. Submitting it opens
  a configuration change decision. When the people the rules ask have approved it, the engine accepts the decision and
  activates the draft as the next version. Nobody activates a version by hand, and no tool writes a configuration row directly.
- **Configuration** (`/app/configuration`) shows the rules in force by area in plain words, the versions with who
  accepted each, the drafts, and the presets.
