# Maker

**Role key:** `maker`. **Under Balanced:** builds in approved environments and records links.

A maker builds applications, often with an AI client such as Claude Code beside them. The product keeps governance
inside that loop: the AI client asks the same questions a person would, drafts what the person then takes forward, and
is refused, with the rule named, when it reaches for something only a person may do.

## What a maker does

- **Starts with the AI client.** Claude Code, connected as the maker ([mcp.md](../mcp.md)), checks for duplicates
  (`check_duplicates`), asks the tier questions (`get_tier_questions`), finds which data a table or file is
  (`resolve_data`) and whether a use is allowed (`check_data_use`), and saves drafts: an intake request, a change
  request, a data use. Each draft is stamped "Drafted by Claude Code for <person>".
- **Takes the drafts forward.** **My work**, Your drafts and AI drafts, lists them. The maker opens each one, edits it
  or sends it with Accept and submit, or withdraws it.
- **Plans changes.** **Changes**, New change (`/app/changes/new`) drafts a change request (`save_change_request_draft`)
  with its window. The standard change check says whether it fits a pre-approved model, and the preview names who will
  be asked.
- **Records deployments** that did not come through a pipeline (`record_deployment`, **Deployments**, Record a
  deployment), and corrects one that was wrong (`record_deployment_correction`).
- **Keeps documents current.** **Documents** lists missing, stale and draft documents; the maker links a document
  (`record_document_link`) and confirms one is still true (`confirm_document_checked`) on applications they own.
- **Declares data uses.** On Application 360's Data section, or **Data**, Uses, Declare a use (`save_data_use`). A use
  that breaks a never rule is refused before it is saved, with the rule and where it is set.
- **Claims what the gap check found.** **Gap check**, Open: an item suggested to the maker can be claimed into an
  application (`claim_resource`), and its owners are asked to confirm.
- **Asks for a review.** `request_architecture_review` sends a design to the architects.
- **Reads why.** **AI activity** shows what the maker's AI client did: drafts taken forward, edited, withdrawn or
  waiting, and every call the server refused with the rule that refused it. A refused call's guideline chip opens
  **Guidelines** at that guideline.

## What a maker cannot do

A maker never approves through an AI client: the approval tool is served only to a person in the web app, and an AI
client that tries gets `ai_cannot_do_this` or, for a tool that does not exist, `not_found`. A maker who is not an
owner of an application cannot accept its risks: the engine checks the decision right and refuses with
`not_authorized`.

## In the demo

Priya Castell plays the maker, with Claude Code beside the browser:

- **S-02:** Claude Code drafts the crew overtime tracker; Priya takes it forward in the intake wizard, and AI activity
  shows Claude Code's earlier work and its refused calls.
- **S-07:** Claude Code's attempt to declare Expense Assistant's use of bank details with AI is refused by the never
  rule, and the same refusal meets Priya on the web page.
- **S-12:** a refused call's guideline chip opens G17 on the Guidelines page.
- **S-21:** what Claude Code may do for her over MCP and what it may not: whoami, her queue, the dependants, the
  committees, and the refused approval, risk acceptance and never-rule data use.

- **S-22:** she registers "Depot key register", an app built before the product came, and sends its ownership for
  confirmation.

Priya is also a member of the Field Apps Support team.

## What the role may call

<!-- generated: role-tools (node tools/docs-gen.mjs) -->
- **Demo persona:** Priya Castell (`priya`).
- **Reads:** 57 of the 61 read tools, among them the ones only some roles get: `list_claims`, `list_gap_check_runs`.

The shipped Tool Policy lets this role call 19 write tools. The engine then checks the relation each one names, so a tool on this list can still refuse a person who is not, for example, an owner of the application.

| No. | Tool | What it does | Who exactly | Served |
|---|---|---|---|---|
| 12 | `save_intake_draft` | Start or edit an intake request | requester, maker, application-owner, pmo, ai-lead, platform-admin | Web app and AI clients |
| 13 | `answer_tier_questions` | Which tier do my answers suggest? | Requester of the intake, owner of the app | Web app and AI clients |
| 14 | `save_application_draft` | Register an app already built | requester, maker, application-owner, ai-lead, platform-admin | Web app and AI clients |
| 17 | `save_decision_proposal` | Record a decision for acceptance | Holders of R, A or C for the decision type | Web app and AI clients |
| 18 | `submit_record` | Send it for approval | The record's requester or creator | Web app and AI clients |
| 19 | `withdraw_record` | Take it back | The record's requester or creator | Web app and AI clients |
| 22 | `save_change_request_draft` | Start or edit a change request | Owner of the app, maker, change-manager | Web app and AI clients |
| 27 | `record_deployment` | This version reached this target | Owner of the app, maker, change-manager | Web app and AI clients |
| 33 | `record_document_link` | Here is the page, and its status | Owner of the app, document owner | Web app and AI clients |
| 34 | `confirm_document_checked` | Is this document still true? | Owner of the app, document owner | Web app and AI clients |
| 38 | `claim_resource` | That one is mine | maker, application-owner, ai-lead, platform-admin | Web app and AI clients |
| 45 | `save_link` | Link this work item or this evidence | Owner of the subject's app, change-manager, pmo; evidence on records the caller can write | Web app and AI clients |
| 46 | `record_owner_leave` | This owner leaves on this date | The leaving person (any role), an owner of an app the person owns, platform-admin | Web app and AI clients |
| 47 | `request_architecture_review` | Please review this design | Owner of the app, maker, architect, change-manager, platform-admin | Web app and AI clients |
| 48 | `record_deployment_correction` | That deployment belongs to this change, or a fact was wrong | Owner of the app, maker, change-manager, platform-admin | Web app and AI clients |
| 52 | `answer_approval` | Approve or reject a request I was asked | The person the approval request asks, through any role (the engine checks the role or owner role, the separation rules and the request's state) | Web app only |
| 62 | `complete_review` | This review is done | The person whose review it is (the engine checks the lead owner role, the decision right or the approval rule, and the accepted outcome decision) | Web app only |
| 82 | `save_data_use` | This app reads or writes this data, or it stopped | Owner of the app, maker, application-owner, platform-admin | Web app and AI clients |
| 100 | `renew_approval_request` | Ask an expired approval request again | The proposer of the decision, or a platform admin (the engine checks it) | Web app only |
<!-- end generated: role-tools -->
