# Platform admin

**Role key:** `platform-admin`. **Under Balanced:** triages intake, confirms tiers, runs the gap check.

The platform admin runs the platform day to day: the front door, the tiers, the places deployments land, the gap
check, the owners who leave, and the product's own upkeep. Of all the roles, the shipped Tool Policy gives this one the
most write tools.

## What a platform admin does

- **Triages intake.** **Intake**, Triage (`/app/intake?view=triage`) lists the requests sent for approval. Under
  Balanced the platform admin is accountable for intake and confirms tiers; each request opens on the decision page,
  beside the consultations of the architect, the PMO and the security reviewer, which are advice.
- **Runs the gap check.** **Gap check** (`/app/gap-check`) compares the Power Platform inventory with the register
  (`run_gap_check`, which also needs an Entra role that can read the inventory), or a list the admin fetched
  (`compare_inventory_list`). Open items are claimed into an application (`claim_resource`) or dismissed with a reason
  (`dismiss_claim`); Vanished lists registered components the inventory no longer has.
- **Keeps the places.** **Places** (`/app/places`) lists the deployment targets by stage and the pipelines. The
  platform admin registers or changes a target (`save_deployment_target`) and retires one, and maps pipelines
  (`map_pipeline`).
- **Handles leavers.** **Owners**, Leavers lists the owners who are leaving or have left. The admin records a leave
  (`record_owner_leave`), runs the owner check (`run_owner_check`) and proposes a successor (`propose_owner_change`).
- **Checks roles and access.** **Roles and access** (`/app/roles`): each role's group and people, and the binding
  check (`check_role_bindings`, this role only).
- **Runs the product.** **Admin** (`/app/admin`): the daily check, run now (`run_due_state_refresh`); the approval
  outbox, with a stuck card sent again (`retry_approval_card`); the clients; the Tool Policy.
  [admin.md](../admin.md) covers each.
- **Asks again.** On the decision page the platform admin may ask any expired request again, or one with less than 48
  hours left (`renew_approval_request`).
- **Moves data references** when a data domain is split or merged (`move_data_references`).
- **Sees the audit trail** (`get_audit_trail`, with the auditor).

## In the demo

Owen Achterberg plays the platform admin. He is also a member of the Platform Team.

- **S-03:** triage of "Weather alerts for outdoor jobs"; the gap check's open, claimed, dismissed and vanished items;
  Places with People Test; the leavers on Owners.
- **S-07:** on Route Planner's Data section, he declares Inspections from "Reads data from Inspection Scheduler:
  declare?".
- **S-09:** he confirms the Timesheets component retired and proposes retirement complete for Legacy Timesheet App.
- **S-11:** Roles and access and the binding check.
- **S-14:** Admin, and the daily check run before the demo.
- **S-16:** the Teams page.
- **S-21:** the AI activity of Claude Code's refused calls, and Admin's clients view.
- **S-22:** the everyday records: he records a leave, compares a list from his own tool, registers a target and maps a
  pipeline, and creates a portfolio inside Field operations.

## What the role may call

<!-- generated: role-tools (node tools/docs-gen.mjs) -->
- **Demo persona:** Owen Achterberg (`owen`).
- **Reads:** 61 of the 61 read tools, among them the ones only some roles get: `list_claims`, `export_configuration`, `check_role_bindings`, `get_audit_trail`, `list_gap_check_runs`, `get_admin_status`.

The shipped Tool Policy lets this role call 34 write tools. The engine then checks the relation each one names, so a tool on this list can still refuse a person who is not, for example, an owner of the application.

| No. | Tool | What it does | Who exactly | Served |
|---|---|---|---|---|
| 12 | `save_intake_draft` | Start or edit an intake request | requester, maker, application-owner, pmo, ai-lead, platform-admin | Web app and AI clients |
| 13 | `answer_tier_questions` | Which tier do my answers suggest? | Requester of the intake, owner of the app | Web app and AI clients |
| 14 | `save_application_draft` | Register an app already built | requester, maker, application-owner, ai-lead, platform-admin | Web app and AI clients |
| 15 | `link_component` | This app, function or agent belongs to my app; it moved; it is gone | Owner of the app, platform-admin | Web app and AI clients |
| 16 | `propose_owner_change` | Hand over or add an owner of an application, a data domain or a data location | Owner of the app, ai-lead (agents), platform-admin; for a data domain, the domain's effective data owner, data-governance-lead and platform-admin; for a data location, its technical owner, data-governance-lead and platform-admin | Web app and AI clients |
| 17 | `save_decision_proposal` | Record a decision for acceptance | Holders of R, A or C for the decision type | Web app and AI clients |
| 18 | `submit_record` | Send it for approval | The record's requester or creator | Web app and AI clients |
| 19 | `withdraw_record` | Take it back | The record's requester or creator | Web app and AI clients |
| 29 | `map_pipeline` | This pipeline deploys this app to these targets | Owner of the app (technical), platform-admin | Web app and AI clients |
| 33 | `record_document_link` | Here is the page, and its status | Owner of the app, document owner | Web app and AI clients |
| 34 | `confirm_document_checked` | Is this document still true? | Owner of the app, document owner | Web app and AI clients |
| 35 | `run_gap_check` | What is running that is not in the register? | platform-admin, plus an inventory Entra role | Web app and AI clients |
| 36 | `compare_inventory_list` | Compare this list I fetched myself | platform-admin | Web app and AI clients |
| 38 | `claim_resource` | That one is mine | maker, application-owner, ai-lead, platform-admin | Web app and AI clients |
| 39 | `dismiss_claim` | That one is not a business application | platform-admin | Web app and AI clients |
| 45 | `save_link` | Link this work item or this evidence | Owner of the subject's app, change-manager, pmo; evidence on records the caller can write | Web app and AI clients |
| 46 | `record_owner_leave` | This owner leaves on this date | The leaving person (any role), an owner of an app the person owns, platform-admin | Web app and AI clients |
| 47 | `request_architecture_review` | Please review this design | Owner of the app, maker, architect, change-manager, platform-admin | Web app and AI clients |
| 48 | `record_deployment_correction` | That deployment belongs to this change, or a fact was wrong | Owner of the app, maker, change-manager, platform-admin | Web app and AI clients |
| 49 | `run_owner_check` | Which owners are disabled, deleted or leaving? | platform-admin | Web app and AI clients |
| 52 | `answer_approval` | Approve or reject a request I was asked | The person the approval request asks, through any role (the engine checks the role or owner role, the separation rules and the request's state) | Web app only |
| 60 | `score_intake_request` | Score a request on the table: score, size band, horizon | pmo, platform-admin | Web app and AI clients |
| 62 | `complete_review` | This review is done | The person whose review it is (the engine checks the lead owner role, the decision right or the approval rule, and the accepted outcome decision) | Web app only |
| 71 | `save_deployment_target` | Register this place deployments land, or change it | platform-admin | Web app only |
| 73 | `save_dependency` | This app depends on that one; that dependency ended | Owner of the app, architect, platform-admin | Web app and AI clients |
| 74 | `save_portfolio` | Create or change a portfolio | platform-admin, pmo (name, description and level only) | Web app only |
| 82 | `save_data_use` | This app reads or writes this data, or it stopped | Owner of the app, maker, application-owner, platform-admin | Web app and AI clients |
| 83 | `save_data_location` | This store exists, moved, or is gone (up to 100 items per call) | platform-admin, architect, application-owner, the location's technical owner, data-governance-lead | Web app and AI clients |
| 84 | `save_data_mapping` | This domain's data sits here, or it left | The location's technical owner, the domain's effective owner or steward, platform-admin, data-governance-lead | Web app and AI clients |
| 85 | `move_data_references` | Move mappings and uses off a domain | platform-admin, data-governance-lead | Web app and AI clients |
| 86 | `run_due_state_refresh` | Run the daily due-state refresh now | platform-admin | Web app only |
| 94 | `request_security_review` | Please review the security of this agent or app | Owner of the app, ai-lead, security-reviewer, architect, platform-admin | Web app and AI clients |
| 100 | `renew_approval_request` | Ask an expired approval request again | The proposer of the decision, or a platform admin (the engine checks it) | Web app only |
| 101 | `retry_approval_card` | Send a stuck approval card again | platform-admin | Web app only |
<!-- end generated: role-tools -->
