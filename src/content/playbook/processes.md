# The processes

- **Version:** 0.1 draft, 2026-11-22
- **Status:** design. Nothing here is built yet. Every product line says what the product is designed to do.

The playbook chapters say who does what and why. The processes say how work moves: what starts it, who decides, and what is recorded. There are eleven, numbered P01 to P11. Every chapter names the processes it uses, and every process names the chapters that use it.

## How to read a process

- **Starts when** is the trigger.
- **Who decides** is the default decision right: exactly one accountable role per decision. Every role, tier, stage and approval in it is a configuration record your organization can change.
- **Steps** are written from the people's side. Where the product takes part, the text says what it is designed to do.
- **Recorded** is what the product is designed to keep. Anything another tool owns is kept as a link: an ID, a URL, a label and a "last seen" date, never a copy.

## The value stream the processes serve

The default configuration is designed with eleven lifecycle stages and five gates. Stages, gates and gate requirements are configuration records, so you can add, merge or rename them. A gate is a recorded decision, not a meeting.

| Stage | Name | Decides (default) | Gate after it |
|---|---|---|---|
| 0 | Idea | Business owner: worth asking for | |
| 1 | Intake and triage | Platform admin: accept, park, redirect or decline | Accept |
| 2 | Prioritize and fund | Sponsor: fund, defer or stop | Commit (only when money or central capacity is needed) |
| 3 | Design and architecture review | Architect: design and build path. Data owner: data use | |
| 4 | Risk and tier | Platform admin: the tier. Business owner: residual risk at the top tier | Design (after stages 3 and 4) |
| 5 | Build | Builder: done when tests pass | |
| 6 | Test | Business owner: accepted. Security reviewer: passed (top tier) | |
| 7 | Change and release | Business owner (lower tiers); change manager (top tier and every emergency) | Go-live (after stages 6 and 7) |
| 8 | Operate | Business owner: supported and earns its place | |
| 9 | Review | Business owner: keep, invest, replace or retire | Value |
| 10 | Retire | Business owner: retirement complete | |

Three rules shape the stream. The tier sets the weight: a low-tier app skips prioritization and design review, and a top-tier app gets a design review, a security review and the change manager. Intake and delivery are tracked apart: an accepted idea can still be in design, and acceptance is not a release gate. Stages 5 to 7 repeat for every change after go-live.

## Shared parts every process uses

- **Approval requests.** A decision that needs a person's yes goes to that person as an approval request, answered on the decision page in the web app; Teams and Outlook notices are designed to link to it. The product is designed to work out which approval rule applies (the single most specific rule wins), who holds the required role at that moment, and whether each answer counts. Only a person counts. An AI stage in an approval flow is kept as a recommendation and never counts.
- **Reminders and escalation.** The product is designed to remind people about due reviews, stale documents, open approval requests and open claims, and to escalate to the deputy after a set number of days. Each reminder is recorded, so "who was told, and when" can be shown.
- **One write path.** Every write, from a page, an MCP client, a pipeline step or a person, is designed to pass the same checks: who the caller is, which role allows the write, which configuration version is in force, and who performed it.

---

## P01 Intake and triage

- **Purpose:** one front door for every kind of business application: ideas, apps already built, agents, AI-built apps and SaaS. It is designed to record the request, the initial tier and the triage decision.
- **Starts when:** a person, or their AI client, asks for something or registers an app already built; or a gap check claim belongs to no registered application (P06).
- **Who decides:** the platform admin triages (accept, park, redirect to reuse, decline). The sponsor funds, defers or stops work that needs money or central capacity. The architect chooses the build path.
- **Steps:**
  1. The requester describes the need. Their AI client may draft the intake, which starts as a draft.
  2. The requester answers the plain tier questions, and P02 works out the initial tier.
  3. The product is designed to search the register for duplicates and reuse, and to suggest a match, never decide it.
  4. The requester submits. A submission is a request, not a decision.
  5. The platform admin decides on the decision page, with a typed reason. In the default configuration, lowest-tier intake is accepted with no approval request, under a rule a person approved once.
  6. On accept, the application is registered and the named business owner is asked to accept ownership on the decision page. On park, the PMO sets a horizon. On redirect, the requester is pointed to the existing app. On decline, the reason stays visible.
  7. If money or central capacity is needed, the PMO scores the request and the sponsor decides (the Commit gate).
  8. The work item, plan or repository is linked, never copied.
- **Recorded:** the intake, the tier answers, the intake and priority decisions, the application, its owners, and delivery links.
- **Guidelines:** G1, G2, G3, G5, G16, G21, G22, G23, G24
- **Used in chapters:** 0, 3, 5, 6, 8, 17, 18, 19, 20, 21

## P02 Tier assessment and required approvals

- **Purpose:** turn plain answers into a stored tier, decided by a person, and from that tier work out everything the app now needs.
- **Starts when:** an intake is submitted (initial tier), design is done (confirmed tier), an answer changes, a change could raise the tier, a tier check review is due, or a graduation trigger fires.
- **Who decides:** the platform admin confirms the tier. The business owner accepts residual risk at the top tier. The data owner approves data use, and the security reviewer must also agree when restricted data reaches an AI model. The security reviewer sets an agent's autonomy level, with the AI lead consulted.
- **Steps:**
  1. The person, or their AI client, answers the tier questions. An AI may propose answers; it never sets the tier.
  2. The product is designed to take the most serious answer as the computed tier, and to explain which answer drove it.
  3. A tier decision is proposed. Anyone may raise the tier. Lowering it below the computed tier needs a typed reason and the full approval.
  4. Approval requests go to the approvers the tier decision needs.
  5. On acceptance, the product is designed to store the tier on the application, as a stored value, never a formula.
  6. The product is designed to work out, from configuration, what the tier now needs: approvals per decision type, gate requirements, required documents, the review cadence and the gate level per change type.
  7. For an agent, an autonomy level decision is opened. Enforcing the agreed level stays in Microsoft's tools.
  8. Anyone can ask "what does my tier need?" and get the list in plain words, with links to the guidelines.
- **Recorded:** the tier answers, the tier, risk acceptance, data use and autonomy level decisions, the next review date.
- **Guidelines:** G3, G4, G8, G16, G19, G22, G23
- **Used in chapters:** 3, 4, 8, 11, 12, 13, 18, 21

## P03 Change management

- **Purpose:** plan, authorize, schedule and close every change to a business application, across every tool that deploys it. Built-in change management is the default; in linked mode your existing change tool stays the system of record.
- **Starts when:** a change is ready, an emergency happens, a deployment arrives with no change (P05), or a retirement needs the removal scheduled (P09).
- **Who decides:** the business owner authorizes normal changes at the lower tiers. The change manager authorizes top-tier changes and every emergency. The change board handles exceptions and windows that cover several applications. Standard changes are pre-authorized by a model the change manager approved.
- **Steps:**
  1. The change owner, or their AI client, drafts the change: application, proposed type, window and target. The product is designed to list affected applications by walking the dependency map, and to inform their owners.
  2. The product is designed to check whether the change fits a standard change model, and to say why when it does not. A person sets the type. A new restricted data use, a role change for the app's own identity, an agent model or instruction change, a change to a shared component, and anything that could raise the tier are always normal.
  3. The change owner submits.
  4. Standard changes are accepted as pre-authorized. Normal changes go for approval per the most specific rule for the tier and type. Emergencies go to the change manager at once, and the record may be completed after the deployment.
  5. The window shows on the change schedule. Enforcing a window stays in the deployment tool.
  6. Your own tool deploys. Deployment records arrive through P05 and link to the change.
  7. For a first production deployment, a go-live decision is proposed and checked against the Go-live gate.
  8. The change owner confirms the result. A failed change or any emergency opens a review after the change.
- **Linked mode:** the change is designed to hold your change tool's number and link, and never to copy its workflow. A recorded number alone never counts as authorization: your tool reports the authorization through a step you build, or the change manager attests it on the decision page.
- **Recorded:** the change, affected applications, the authorization and go-live decisions, linked deployments, evidence links and the review.
- **Guidelines:** G4, G6, G9, G12, G13
- **Used in chapters:** 7, 9, 10, 14, 15, 17, 19

## P04 Architecture review

- **Purpose:** make the review request find the architect when a configured trigger fires, and keep every review and architecture decision in the shared decision log.
- **Starts when:** a tier answer adds an architecture review (writes under its own identity, restricted data, a wide or outside audience, an agent that executes actions, pro-code or hosting outside Power Platform); a change touches a shared component; someone asks to depart from a standard; or a Design gate requires a review.
- **Who decides:** the architect, as design authority by default. The data owner decides data use. A board role can be added for the top tier and exceptions.
- **Steps:**
  1. A review is opened with the trigger that caused it. A person, or their AI client, can also ask for one.
  2. The builder's AI client may draft the architecture decision record from the discussion. The full text lives in the team's repository or wiki; the product is designed to record the link and a proposed design decision with the options considered.
  3. The architect reviews. Linked assessments become evidence links.
  4. The design decision goes to the architect, who answers on the decision page. A data use decision goes to the data owner when data use changed.
  5. The review closes with its outcome decision, and the Design gate is checked again.
  6. An exception records the standard it departs from, the reason and a review date.
- **Recorded:** the review, design, data use and exception decisions, the decision record link, evidence links.
- **Guidelines:** G4, G6, G7, G8, G19, G21
- **Used in chapters:** 4, 7, 13, 20

## P05 Deployment record

- **Purpose:** record every deployment of every artifact to every target, whatever tool ran it, and flag the ones that came another way, lacked their approval or landed outside their window. By design, the product never builds, hosts or runs a pipeline.
- **Starts when:** a deployment is reported in one of three ways: a step at the end of your own pipeline, the owner's AI tool through the product's MCP server, or a person.
- **Who decides:** nobody, for the record itself. For a flagged deployment the change manager decides: open a change after the fact (P03) or record an exception.
- **Steps:**
  1. The sender passes the application, artifact and version, target, route, run link, times, result and the change if known, with a key that makes retries safe.
  2. The product is designed to link the target, the pipeline and the change (by ID, or by matching application, target and window).
  3. It is designed to apply the gate level your configuration sets for the tier, change type and target stage.
  4. It is designed to compute the flags: another path than the target's expected route, outside the change window, missing approval, and code whose only approval came from an AI.
  5. At the Flag level and above, the owners and the change manager are told the same day. No gate level stops a manual import; it can only be flagged.
  6. The change manager follows up each flag. A correction is a new record that links the deployment to the change; the original stays.
  7. A first successful production deployment prompts the go-live decision (P03).
- **Recorded:** the artifact version, the deployment record with its flags and gate level, target and pipeline links with "last seen" dates, evidence links.
- **Guidelines:** G7, G9, G12, G13, G15, G21
- **Used in chapters:** 0, 9, 10, 15, 16, 17, 19, 20, 21, 22

## P06 Gap check and claim

- **Purpose:** answer "what is running that is not in the register?" live, as the admin who asks, and put only the gaps on a claim list for owners. No scheduled import and no copy of Microsoft's inventory.
- **Starts when:** an admin asks in an MCP client such as Claude Code. Nothing runs unattended.
- **Who decides:** the platform admin runs the check and dismisses items. The business owner accepts ownership of a claimed item. The AI lead decides on agents Microsoft flags: keep, reassign, consolidate or retire.
- **Steps:**
  1. The admin starts the gap check for the estates they choose.
  2. The product's MCP server is designed to query Microsoft's inventory with the admin's own sign-in, because the inventory accepts only delegated calls. Which admin role was used is recorded.
  3. The product is designed to match each item by its Microsoft ID: matched items get a new "last seen" date, unregistered items become claims, and registered parts that were not seen become "vanished" claims.
  4. A likely owner claims an item for an existing application, or starts an intake for a new one (P01).
  5. The platform admin dismisses items that are not business applications, with a reason.
  6. The technical owner resolves vanished items: retired, moved, or still there in an estate the check could not read.
  7. For agents Microsoft already flags as ownerless, the product is designed to link that evidence and record the business decision. Reassigning owners stays in Microsoft's tools.
- **Recorded:** the gap check run, claims and their outcomes, technical parts with "last seen" dates, ownership decisions.
- **Guidelines:** G1, G16, G18, G24
- **Used in chapters:** 3, 5, 11, 14, 16, 17, 19, 22

## P07 Documentation tracking

- **Purpose:** say which documents each application needs, track each one's link and status, and remind owners. By design, the product never writes a document.
- **Starts when:** a documentation requirement starts to apply (a tier, kind or stage change), a document passes its freshness date, an owner records a link or status, or a gate needs a current document.
- **Who decides:** the platform admin sets which documents each kind and tier needs, as configuration (P11). The technical owner answers for each document by default and confirms gate-relevant documents are current.
- **Steps:**
  1. The product is designed to work out which documents are missing from the requirements in scope. A missing document is computed, never stored.
  2. The owner, or their AI client, asks what is missing or stale.
  3. The owner's own tool writes the page, in a wiki, a repository or SharePoint.
  4. The owner's AI tool records the link and the status.
  5. For documents a gate depends on, "current" set by an AI client stays a claim until the document owner confirms it.
  6. When someone confirms a document is still true, the last check date moves.
  7. A document goes stale when its freshness time passes. The owner is reminded, then the deputy.
  8. A gate that needs a current document cannot be accepted while it is missing or stale.
- **Recorded:** each document's link, status, owner, last update and last check, and the reminders sent.
- **Guidelines:** G20, G22
- **Used in chapters:** 4, 5, 7, 12, 17, 21

## P08 Owner leaves

- **Purpose:** make sure no application is left without an answerable owner. The deputy holds the owner role and may answer its requests, and reminders escalate to the deputy. The product is designed to let the deputy act at once and to move open approvals. A successor is named, and agents flagged by Microsoft get a business decision.
- **Starts when:** a leave or role change is announced; an admin runs the on-demand owner check, which reads owners' account state from Entra with the admin's own sign-in; an optional leaver workflow reports a leaver; Microsoft flags an agent without an owner; or an owner check review comes due.
- **Who decides:** the business owner approves the successor, and the successor accepts on the decision page. When the business owner leaves, the sponsor decides. The AI lead decides on flagged agents.
- **Steps:**
  1. The leave is recorded, and an owner check review opens for every app where that person holds an owner role.
  2. Until a successor accepts, reminders and escalations go to the deputy.
  3. The product is designed to cancel the leaver's open approval requests and open new ones for the deputy or another holder of the role.
  4. An ownership decision names the successor, who must accept. A new deputy is then required.
  5. The leaver's assignments end. Their name stays on past decisions.
  6. Removing access happens in Entra and the deployment tools, not in the product. An urgent revoke disables the user, not only the group membership.
  7. For an agent owned by the leaver, the AI lead records keep, reassign, consolidate or retire, with a link to Microsoft's evidence.
- **Recorded:** the leave, owner assignment history, ownership decisions, moved approvals and the owner check review.
- **Guidelines:** G2, G14, G24
- **Used in chapters:** 2, 8, 11, 12, 19

## P09 Retirement

- **Purpose:** take an application out of use on purpose: a recorded decision, an impact check, a scheduled removal done in Microsoft's tools, linked evidence that nothing is left, and a final stage.
- **Starts when:** a portfolio decision says retire, an owner asks to retire something nobody uses, a vanished claim is resolved as retired (P06), or a replacing app went live.
- **Who decides:** the business owner decides the future and declares retirement complete. The PMO prepares the facts. The data owner decides retention. The removal change follows P03.
- **Steps:**
  1. A portfolio decision to retire is accepted on the decision page.
  2. The dependency map is walked in both directions. Owners of dependants are told; a dependant that needs a replacement gets its own change or intake.
  3. The data owner decides what is kept and where, with evidence links.
  4. A change schedules the removal (P03).
  5. People and their AI tools disable, then delete, in Microsoft's admin centers, Entra and Agent 365. Each step done is linked as evidence. By design, none of this happens in the product.
  6. The admin runs the gap check (P06). Each part should now show as vanished and be resolved as retired.
  7. A retirement decision marks it complete. The product is designed to move the app to the retired stage, cancel due reviews and stop document reminders. Dependencies stay, so history still reads.
- **Recorded:** the portfolio, data use and retirement decisions, the removal change and deployments, evidence links, resolved claims, the final stage.
- **Guidelines:** G4, G5, G6, G7, G18
- **Used in chapters:** 6, 13, 14

## P10 AI draft and human accept

- **Purpose:** let AI clients do most of the typing while a person decides everything that matters, and make the records show which is which.
- **Starts when:** any write whose author is an AI client acting for a person, or an agent identity.
- **Who decides:** the person the AI acts for accepts their own drafts. The record's accountable role decides decisions. The AI only supports.
- **Steps:**
  1. The AI client writes through a tool. The product is designed to mark the row as AI-created and stamp the AI client and the person it acted for.
  2. AI-created records start as draft, decisions as proposed, and applications as draft registrations. The AI cannot move them further in the same call.
  3. What counts as a person's acceptance depends on what is accepted. In the default configuration: a decision needs a person's answer on the decision page; registering an application the AI created needs the business owner's confirmation; a gate-relevant document set to current needs its owner's confirmation; a submission of an intake, change or decision can be the same MCP call, made for the person; links, deployment records and checks need no separate acceptance.
  4. An AI stage in your own approval flow is kept as a recommendation and never counts.
  5. Every page and tool answer shows "performed by AI client X for person Y".
- **Why the decision page:** the product's MCP server is designed to act with the person's sign-in, so it cannot prove that a person, not the AI, pressed the button. So the one tool that records an answer is served only to a person signed in to the web app, and the person's own sign-in proves who answered.
- **Recorded:** the AI-created mark and performed-by columns on every row, the person's confirmations, audit entries.
- **Guidelines:** G8, G15, G17, G22, G23
- **Used in chapters:** 0, 1, 3, 8, 11, 15, 16, 17, 18, 21

## P11 Configuration change

- **Purpose:** change any rule without code, rebuild or redeploy: the admin decides, an AI performs the change, the product is designed to validate and version it, and the record shows both.
- **Starts when:** an admin instructs a change in plain words, a preset or overlay is applied, or a product upgrade ships new default rows.
- **Who decides:** the configuration admin, for every configuration decision. The area roles are consulted: the change manager for gate levels, the AI lead for AI rules, the platform admin for roles and group bindings, the security reviewer for tier rules.
- **Steps:**
  1. The admin says what should change, for example "add a fourth tier" or "require a runbook for every agent".
  2. The AI client reads the current configuration and prepares the change as a patch of named keys.
  3. The AI client proposes it as the admin. The product is designed to check that the admin may change configuration.
  4. The product is designed to validate the whole result and refuse a change that breaks its rules, such as a decision with no accountable role, an approver who is not a person, or a change to a locked setting.
  5. The admin sees the difference and its effect on live applications, then the draft is submitted.
  6. One approval request goes to the configuration admin, who sees the difference and answers on the decision page. In the default configuration the instructing admin may accept their own AI's draft; with strict separation a second admin must accept. Consulted roles get consultation requests, which never count. The AI cannot answer any of them.
  7. On acceptance, a new configuration version is activated. New records stamp the new version; old records keep theirs.
  8. An undo is a new change that restores an earlier version. Nothing is edited in place.
  9. An upgrade proposes a draft that adds missing default rows and never overwrites your own changes.
- **Recorded:** the draft, the configuration change decision, the new configuration version with who instructed it, audit entries.
- **Guidelines:** G4, G5, G14, G15, G17
- **Used in chapters:** 1, 2, 11, 15, 16, 18
