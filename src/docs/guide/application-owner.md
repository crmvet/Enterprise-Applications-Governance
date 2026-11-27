# Application owner

**Role key:** `application-owner`. **Under Balanced:** holds owner roles on applications.

The product role says a person may own applications. What they may do on one application comes from the owner roles
they hold on it: business owner, deputy, sponsor, technical owner, support owner, security owner and the others
([concepts.md](../concepts.md#owners)). Under Balanced the business owner answers most approvals about their
application.

## What an application owner does

- **Answers approvals.** **My work**, Approvals asked of you, and **Approvals** (`/app/approvals`) list the requests
  for the owner's applications: change authorizations, risk acceptances, go-live, acceptance after testing, ownership
  changes, retirement and data uses where they are the data owner. Each opens on the **decision page**, which shows
  the statement, who else is asked, the rule and any separation rule. Approve or Reject, with a reason where the rule
  asks for one; the page then says whether the answer counts and what the engine did.
- **Looks after the application.** **Application 360** (`/app/records/business-application/<id>`) shows the
  lifecycle stepper with each gate and what it still needs, the tier, the owners, the documents, the components, the
  data uses, the approvals and the evidence. The owner confirms a document is still true there, which can turn a gate
  requirement met.
- **Hands over and fills gaps.** **Hand over** on Application 360 proposes a new holder, a deputy or a successor
  (`propose_owner_change`), as an ownership decision the rules send for approval. **Owners**, Gaps
  (`/app/owners?view=gaps`) lists roles with no holder or no deputy.
- **Plans and closes changes.** A change request (`save_change_request_draft`), its result (`record_change_result`),
  and closing it once authorized (`complete_change`, a person's act in the web app).
- **Keeps the facts current.** Components (`link_component`), dependencies (`save_dependency`), pipelines for its
  targets (`map_pipeline`, as technical owner), document links, and the data the application uses (`save_data_use`).
- **Asks for reviews.** Architecture (`request_architecture_review`) and security (`request_security_review`)
  reviews.

Balanced lets an owner answer a request about a decision they proposed themselves at tiers 1 and 2. At tier 3 the
separation rule refuses it.

## In the demo

Nadia Ferreira plays the application owner. She is business owner of Inspection Scheduler.

- **S-04:** she answers the Inspection Scheduler change, her own risk acceptance (allowed at tier 2), the owner change
  that makes her business owner of Claims Triage Agent, the business owner role a new request names her for, and a
  data-use request as data owner of Customers. On Application 360 she confirms the runbook current, uses Hand over, and
  proposes a deputy from Owners, Gaps. Mateo Brandt answers the owner change as sponsor.
- **S-09:** she funds the priority of "Weather alerts for outdoor jobs", and answers the retirement of Legacy
  Timesheet App as its deputy business owner.
- **S-20:** she accepts the business owner role that Lena Okoro's request "Depot first-aid kit checks" names her for,
  and the application is registered.
- **S-22:** she requests an architecture review of Contractor Portal, links its runbook, and proposes a risk acceptance
  on it: Contractor Portal is tier 3, so the separation rule keeps her from answering her own proposal.

## What the role may call

<!-- generated: role-tools (node tools/docs-gen.mjs) -->
- **Demo persona:** Nadia Ferreira (`nadia`).
- **Reads:** 57 of the 61 read tools, among them the ones only some roles get: `list_claims`, `list_gap_check_runs`.

The shipped Tool Policy lets this role call 28 write tools. The engine then checks the relation each one names, so a tool on this list can still refuse a person who is not, for example, an owner of the application.

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
| 22 | `save_change_request_draft` | Start or edit a change request | Owner of the app, maker, change-manager | Web app and AI clients |
| 26 | `record_change_result` | How did the change end? | Owner of the app, change-manager | Web app and AI clients |
| 27 | `record_deployment` | This version reached this target | Owner of the app, maker, change-manager | Web app and AI clients |
| 29 | `map_pipeline` | This pipeline deploys this app to these targets | Owner of the app (technical), platform-admin | Web app and AI clients |
| 33 | `record_document_link` | Here is the page, and its status | Owner of the app, document owner | Web app and AI clients |
| 34 | `confirm_document_checked` | Is this document still true? | Owner of the app, document owner | Web app and AI clients |
| 38 | `claim_resource` | That one is mine | maker, application-owner, ai-lead, platform-admin | Web app and AI clients |
| 45 | `save_link` | Link this work item or this evidence | Owner of the subject's app, change-manager, pmo; evidence on records the caller can write | Web app and AI clients |
| 46 | `record_owner_leave` | This owner leaves on this date | The leaving person (any role), an owner of an app the person owns, platform-admin | Web app and AI clients |
| 47 | `request_architecture_review` | Please review this design | Owner of the app, maker, architect, change-manager, platform-admin | Web app and AI clients |
| 48 | `record_deployment_correction` | That deployment belongs to this change, or a fact was wrong | Owner of the app, maker, change-manager, platform-admin | Web app and AI clients |
| 52 | `answer_approval` | Approve or reject a request I was asked | The person the approval request asks, through any role (the engine checks the role or owner role, the separation rules and the request's state) | Web app only |
| 62 | `complete_review` | This review is done | The person whose review it is (the engine checks the lead owner role, the decision right or the approval rule, and the accepted outcome decision) | Web app only |
| 64 | `complete_change` | This change is done | Owner of the app, change-manager | Web app only |
| 73 | `save_dependency` | This app depends on that one; that dependency ended | Owner of the app, architect, platform-admin | Web app and AI clients |
| 82 | `save_data_use` | This app reads or writes this data, or it stopped | Owner of the app, maker, application-owner, platform-admin | Web app and AI clients |
| 83 | `save_data_location` | This store exists, moved, or is gone (up to 100 items per call) | platform-admin, architect, application-owner, the location's technical owner, data-governance-lead | Web app and AI clients |
| 84 | `save_data_mapping` | This domain's data sits here, or it left | The location's technical owner, the domain's effective owner or steward, platform-admin, data-governance-lead | Web app and AI clients |
| 94 | `request_security_review` | Please review the security of this agent or app | Owner of the app, ai-lead, security-reviewer, architect, platform-admin | Web app and AI clients |
| 100 | `renew_approval_request` | Ask an expired approval request again | The proposer of the decision, or a platform admin (the engine checks it) | Web app only |
<!-- end generated: role-tools -->
