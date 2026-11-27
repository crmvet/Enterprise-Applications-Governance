# AI lead

**Role key:** `ai-lead`. **Under Balanced:** keeps the AI guidelines, is consulted on agent autonomy, decides on agents
Microsoft flags.

The AI lead watches the agents and the AI-built applications of the estate, and what AI clients do in the product.

## What an AI lead does

- **Keeps the agent register.** **Agents** (`/app/agents`) lists every agent with whether it is decided, reviewed,
  owned and ready to go live. Its views are the register, the flagged agents and the autonomy levels.
- **Proposes autonomy levels.** An agent's autonomy level is a decision the AI lead proposes and is consulted on; the
  security reviewer decides it under Balanced.
- **Decides on flagged agents.** An agent Microsoft flags gets a flagged-agent decision (keep, reassign, consolidate or
  retire), which the AI lead is accountable for and answers on the decision page.
- **Asks for security reviews** (`request_security_review`), for example before an agent's pilot, and proposes owner
  changes on agents.
- **Reads what AI did.** **AI activity** (`/app/ai-activity`), across the estate: drafts taken forward, edited,
  withdrawn or waiting, the calls the server refused with their rule, the AI answers stored as recommendations only,
  and the deployments whose only code approval came from an AI.
- **Is consulted on configuration changes**, and may claim what the gap check found (`claim_resource`).

## In the demo

Dmitri Halloran plays the AI lead.

- **S-08:** Supplier Price Agent (tier 3) has no autonomy level, and Dmitri proposes "assist" for the security
  reviewer to decide. The flagged Onboarding Buddy Agent waits for his decision. Customer Feedback Bot's security
  review, which he requested before its pilot, is due for the security reviewer. AI activity shows the Finance
  Approvals Agent's answer on the Invoice Matching 1.12 authorization, stored as a recommendation only.

## What the role may call

<!-- generated: role-tools (node tools/docs-gen.mjs) -->
- **Demo persona:** Dmitri Halloran (`dmitri`).
- **Reads:** 57 of the 61 read tools, among them the ones only some roles get: `list_claims`, `list_gap_check_runs`.

The shipped Tool Policy lets this role call 16 write tools. The engine then checks the relation each one names, so a tool on this list can still refuse a person who is not, for example, an owner of the application.

| No. | Tool | What it does | Who exactly | Served |
|---|---|---|---|---|
| 12 | `save_intake_draft` | Start or edit an intake request | requester, maker, application-owner, pmo, ai-lead, platform-admin | Web app and AI clients |
| 13 | `answer_tier_questions` | Which tier do my answers suggest? | Requester of the intake, owner of the app | Web app and AI clients |
| 14 | `save_application_draft` | Register an app already built | requester, maker, application-owner, ai-lead, platform-admin | Web app and AI clients |
| 16 | `propose_owner_change` | Hand over or add an owner of an application, a data domain or a data location | Owner of the app, ai-lead (agents), platform-admin; for a data domain, the domain's effective data owner, data-governance-lead and platform-admin; for a data location, its technical owner, data-governance-lead and platform-admin | Web app and AI clients |
| 17 | `save_decision_proposal` | Record a decision for acceptance | Holders of R, A or C for the decision type | Web app and AI clients |
| 18 | `submit_record` | Send it for approval | The record's requester or creator | Web app and AI clients |
| 19 | `withdraw_record` | Take it back | The record's requester or creator | Web app and AI clients |
| 33 | `record_document_link` | Here is the page, and its status | Owner of the app, document owner | Web app and AI clients |
| 34 | `confirm_document_checked` | Is this document still true? | Owner of the app, document owner | Web app and AI clients |
| 38 | `claim_resource` | That one is mine | maker, application-owner, ai-lead, platform-admin | Web app and AI clients |
| 45 | `save_link` | Link this work item or this evidence | Owner of the subject's app, change-manager, pmo; evidence on records the caller can write | Web app and AI clients |
| 46 | `record_owner_leave` | This owner leaves on this date | The leaving person (any role), an owner of an app the person owns, platform-admin | Web app and AI clients |
| 52 | `answer_approval` | Approve or reject a request I was asked | The person the approval request asks, through any role (the engine checks the role or owner role, the separation rules and the request's state) | Web app only |
| 62 | `complete_review` | This review is done | The person whose review it is (the engine checks the lead owner role, the decision right or the approval rule, and the accepted outcome decision) | Web app only |
| 94 | `request_security_review` | Please review the security of this agent or app | Owner of the app, ai-lead, security-reviewer, architect, platform-admin | Web app and AI clients |
| 100 | `renew_approval_request` | Ask an expired approval request again | The proposer of the decision, or a platform admin (the engine checks it) | Web app only |
<!-- end generated: role-tools -->
