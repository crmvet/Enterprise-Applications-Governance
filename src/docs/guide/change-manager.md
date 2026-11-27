# Change manager

**Role key:** `change-manager`. **Under Balanced:** runs the change schedule; authorizes tier 3 changes and
emergencies.

The change manager keeps the change schedule, authorizes the changes the business owner does not (tier 3 and
emergencies, under Balanced), and follows up deployments the engine flags.

## What a change manager does

- **Plans changes.** **Changes**, New change (`/app/changes/new`): the impact walk proposes the affected
  applications, the standard change check (`check_standard_change`) says whether the change fits a pre-approved model,
  and the preview names who approves before anything is saved. Submit sends it.
- **Keeps the schedule.** **Changes**, Schedule, and the **Timeline** (`/app/timeline`) show every window; a window
  opens its change record (`/app/records/change-request/<id>`).
- **Authorizes.** Tier 3 and emergency change authorizations ask the change manager, on the decision page; a tier 3
  answer needs a typed reason.
- **Follows up flagged deployments.** **Deployments**, Flagged (`/app/deployments?view=flagged`): a deployment outside
  its window, on another path, with a missing approval, or with only an AI code approval. Each record explains its
  flags, and is followed up from the record: propose an exception, or draft a change after the fact.
- **Records and closes.** A deployment recorded by hand (`record_deployment`, route person in the app), a change's
  result (`record_change_result`), and closing an authorized change (`complete_change`, a person's act in the web
  app).
- **Is consulted** on changes to the gate policy.

A change manager cannot authorize their own tier 3 change: the separation rule refuses the answer, and the decision
page says why.

## In the demo

Hugo Brandvold plays the change manager.

- **S-05:** a new change on Crew Roster with the impact walk and the standard check; its window on the Timeline; the
  closed Legacy Timesheet App removal change with its evidence; and the AI-only code approvals among the flagged
  deployments (Route Planner 2.0.0-rc.1, from Claude Code, and Invoice Matching 1.12.0 and 1.12.1).

- **S-22:** he records the deployment of the standard change "Rotate the reminder flow connection" by hand, records
  that it succeeded, and closes the change.

Hugo is also a member of the Service Desk team, and data owner of Workplace.

## What the role may call

<!-- generated: role-tools (node tools/docs-gen.mjs) -->
- **Demo persona:** Hugo Brandvold (`hugo`).
- **Reads:** 55 of the 61 read tools. It gets none of the reads kept to a few roles.

The shipped Tool Policy lets this role call 14 write tools. The engine then checks the relation each one names, so a tool on this list can still refuse a person who is not, for example, an owner of the application.

| No. | Tool | What it does | Who exactly | Served |
|---|---|---|---|---|
| 17 | `save_decision_proposal` | Record a decision for acceptance | Holders of R, A or C for the decision type | Web app and AI clients |
| 18 | `submit_record` | Send it for approval | The record's requester or creator | Web app and AI clients |
| 19 | `withdraw_record` | Take it back | The record's requester or creator | Web app and AI clients |
| 22 | `save_change_request_draft` | Start or edit a change request | Owner of the app, maker, change-manager | Web app and AI clients |
| 26 | `record_change_result` | How did the change end? | Owner of the app, change-manager | Web app and AI clients |
| 27 | `record_deployment` | This version reached this target | Owner of the app, maker, change-manager | Web app and AI clients |
| 45 | `save_link` | Link this work item or this evidence | Owner of the subject's app, change-manager, pmo; evidence on records the caller can write | Web app and AI clients |
| 46 | `record_owner_leave` | This owner leaves on this date | The leaving person (any role), an owner of an app the person owns, platform-admin | Web app and AI clients |
| 47 | `request_architecture_review` | Please review this design | Owner of the app, maker, architect, change-manager, platform-admin | Web app and AI clients |
| 48 | `record_deployment_correction` | That deployment belongs to this change, or a fact was wrong | Owner of the app, maker, change-manager, platform-admin | Web app and AI clients |
| 52 | `answer_approval` | Approve or reject a request I was asked | The person the approval request asks, through any role (the engine checks the role or owner role, the separation rules and the request's state) | Web app only |
| 62 | `complete_review` | This review is done | The person whose review it is (the engine checks the lead owner role, the decision right or the approval rule, and the accepted outcome decision) | Web app only |
| 64 | `complete_change` | This change is done | Owner of the app, change-manager | Web app only |
| 100 | `renew_approval_request` | Ask an expired approval request again | The proposer of the decision, or a platform admin (the engine checks it) | Web app only |
<!-- end generated: role-tools -->
