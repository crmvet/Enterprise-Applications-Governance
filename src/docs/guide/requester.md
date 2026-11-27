# Requester

**Role key:** `requester`. **Under Balanced:** submits intake requests.

A requester is anyone in the business who needs an application, or a change to how work is done, and asks for it.
The product's front door is the intake request: it finds what already exists, works out the tier from a few
questions, and sends the request to the people who decide.

## What a requester does

- **Asks for something new.** **Intake**, New request (`/app/intake/new`) is a wizard. The reuse step shows
  applications that look like the request (`check_duplicates`), so the requester can ask to reuse one instead. The
  tier questions come next (`get_tier_questions`); the answers suggest a tier and show what drives it
  (`answer_tier_questions`), and the wizard lists what that tier needs. Once the request is saved, an answer the
  declared data implies shows under its question as a suggestion, never preset.
- **Sends it.** The wizard's last step, Review and submit, sends the request (`submit_record`). Under Balanced the platform admin is
  asked, and the architect, the PMO and the security reviewer are consulted (their answers are advice). The business
  owner the request names is asked to accept the role.
- **Keeps a draft.** A saved request stays a draft until it is sent, and only its requester reads it. **Intake**, Mine
  and Drafts list them.
- **Follows it.** **My work**, Waiting on others, shows each open request and who it waits for. The request's own page
  (`/app/records/intake-request/<id>`) shows its state and decisions.
- **Takes AI drafts forward.** When the requester's own AI client drafts a request, it shows in **My work** as
  "Drafted by <client> for <person>". The requester opens it in the wizard, edits it, or sends it as it is with
  Accept and submit.
- **Answers what is asked of them.** A requester who also holds an owner role, such as a deputy business owner,
  answers those approvals on the decision page from **My work**, Approvals asked of you.
- **Registers an application already built** (`save_application_draft`): it is saved as a draft with its proposed
  owners, and the decisions it needs wait as proposals until they are sent.

## In the demo

Lena Okoro plays the requester. She has a draft of her own and a request waiting on the platform admin, and, as a
deputy business owner, two answers waiting: the owner change that would make her business owner of Safety Findings
Tracker, and a Fleet Maintenance Log change. As a data steward she is also consulted on two data uses (Visit Report
Agent, and Field Data Lake through the MCP server).

- **S-20:** she finishes and submits her draft "Depot first-aid kit checks" in the intake wizard, with no AI client;
  Nadia Ferreira, the business owner the request names, accepts it on the decision page.
- **S-22:** she withdraws "Spare parts returns from field vans": the need went away, and its approval requests are
  canceled.
- **S-02:** the guided story's intake step is played by the maker and Claude Code.

## What the role may call

<!-- generated: role-tools (node tools/docs-gen.mjs) -->
- **Demo persona:** Lena Okoro (`lena`).
- **Reads:** 55 of the 61 read tools. It gets none of the reads kept to a few roles.

The shipped Tool Policy lets this role call 11 write tools. The engine then checks the relation each one names, so a tool on this list can still refuse a person who is not, for example, an owner of the application.

| No. | Tool | What it does | Who exactly | Served |
|---|---|---|---|---|
| 12 | `save_intake_draft` | Start or edit an intake request | requester, maker, application-owner, pmo, ai-lead, platform-admin | Web app and AI clients |
| 13 | `answer_tier_questions` | Which tier do my answers suggest? | Requester of the intake, owner of the app | Web app and AI clients |
| 14 | `save_application_draft` | Register an app already built | requester, maker, application-owner, ai-lead, platform-admin | Web app and AI clients |
| 17 | `save_decision_proposal` | Record a decision for acceptance | Holders of R, A or C for the decision type | Web app and AI clients |
| 18 | `submit_record` | Send it for approval | The record's requester or creator | Web app and AI clients |
| 19 | `withdraw_record` | Take it back | The record's requester or creator | Web app and AI clients |
| 45 | `save_link` | Link this work item or this evidence | Owner of the subject's app, change-manager, pmo; evidence on records the caller can write | Web app and AI clients |
| 46 | `record_owner_leave` | This owner leaves on this date | The leaving person (any role), an owner of an app the person owns, platform-admin | Web app and AI clients |
| 52 | `answer_approval` | Approve or reject a request I was asked | The person the approval request asks, through any role (the engine checks the role or owner role, the separation rules and the request's state) | Web app only |
| 62 | `complete_review` | This review is done | The person whose review it is (the engine checks the lead owner role, the decision right or the approval rule, and the accepted outcome decision) | Web app only |
| 100 | `renew_approval_request` | Ask an expired approval request again | The proposer of the decision, or a platform admin (the engine checks it) | Web app only |
<!-- end generated: role-tools -->
