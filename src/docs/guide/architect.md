# Architect

**Role key:** `architect`. **Under Balanced:** reviews designs and grants exceptions to standards.

An architect is accountable for design decisions and for exceptions to the organization's standards, and runs the
architecture reviews a tier asks for.

## What an architect does

- **Runs architecture reviews.** **Reviews** (`/app/reviews?type=architecture`) lists every architecture review that
  is due, overdue or done, not only those on the architect's own applications. A review's page
  (`/app/records/review/<id>`) links the design decision it records, with its options considered and the links to the
  design record. The architect approves the design decision on the decision page, then marks the review done
  (`complete_review`, a person's act in the web app) once the decision is accepted. On Application 360 the Design gate
  then turns met.
- **Decides exceptions.** An exception sets a standard aside for an application until a review date. Under Balanced
  the architect approves exceptions to standards; **Decisions**, Exceptions (`/app/decisions?view=exceptions`) lists the
  standing ones, the overdue reviews and the ones waiting. An exception about a change goes to the change board
  instead.
- **Advises on intake.** Under Balanced every intake request consults the architect. The answer is advice and does
  not count toward acceptance.
- **Maps dependencies.** The **dependency map** (`/app/records/business-application/<id>/map`) and
  `save_dependency` record which applications depend on which. `get_dependency_impact` walks the impact from an
  application, a component or a data location; "What depends on it?" opens that walk from a component on
  Application 360 and from a data location.
- **Records data locations** (`save_data_location`) and asks for security reviews (`request_security_review`).

## In the demo

Elif Dunmore plays the architect.

- **S-06:** Claims Triage Agent's architecture review is due. Elif approves its design decision and completes the
  review, and the Design gate turns met. Quote Builder's review was completed before the demo. The exceptions view
  shows Contractor Portal (standing), Travel Requests (its review overdue) and the Invoice Matching 1.12.1 hotfix
  (waiting).
- **S-17:** Inspection Scheduler's dependency map: what breaks within three hops, the impact of retiring Finance
  Ledger, and what depends on a data location.
- S-16 mentions the Architects as a committee on the Teams page, with work waiting on them; it is played by the
  platform admin and the change board.
- **S-22:** she adds a dependency on Visit Report Agent's map: it calls the API of Field Knowledge MCP Server.
- Marcus Oyelaran, a second architect, answers what Elif proposes: the design decision of S-06.3, so the person who
  asked is not the one who answers. Under Balanced a proposer may answer at tier 2 (S-04.4 shows it).

## What the role may call

<!-- generated: role-tools (node tools/docs-gen.mjs) -->
- **Demo persona:** Elif Dunmore (`elif`).
- **Reads:** 55 of the 61 read tools. It gets none of the reads kept to a few roles.

The shipped Tool Policy lets this role call 14 write tools. The engine then checks the relation each one names, so a tool on this list can still refuse a person who is not, for example, an owner of the application.

| No. | Tool | What it does | Who exactly | Served |
|---|---|---|---|---|
| 17 | `save_decision_proposal` | Record a decision for acceptance | Holders of R, A or C for the decision type | Web app and AI clients |
| 18 | `submit_record` | Send it for approval | The record's requester or creator | Web app and AI clients |
| 19 | `withdraw_record` | Take it back | The record's requester or creator | Web app and AI clients |
| 33 | `record_document_link` | Here is the page, and its status | Owner of the app, document owner | Web app and AI clients |
| 34 | `confirm_document_checked` | Is this document still true? | Owner of the app, document owner | Web app and AI clients |
| 45 | `save_link` | Link this work item or this evidence | Owner of the subject's app, change-manager, pmo; evidence on records the caller can write | Web app and AI clients |
| 46 | `record_owner_leave` | This owner leaves on this date | The leaving person (any role), an owner of an app the person owns, platform-admin | Web app and AI clients |
| 47 | `request_architecture_review` | Please review this design | Owner of the app, maker, architect, change-manager, platform-admin | Web app and AI clients |
| 52 | `answer_approval` | Approve or reject a request I was asked | The person the approval request asks, through any role (the engine checks the role or owner role, the separation rules and the request's state) | Web app only |
| 62 | `complete_review` | This review is done | The person whose review it is (the engine checks the lead owner role, the decision right or the approval rule, and the accepted outcome decision) | Web app only |
| 73 | `save_dependency` | This app depends on that one; that dependency ended | Owner of the app, architect, platform-admin | Web app and AI clients |
| 83 | `save_data_location` | This store exists, moved, or is gone (up to 100 items per call) | platform-admin, architect, application-owner, the location's technical owner, data-governance-lead | Web app and AI clients |
| 94 | `request_security_review` | Please review the security of this agent or app | Owner of the app, ai-lead, security-reviewer, architect, platform-admin | Web app and AI clients |
| 100 | `renew_approval_request` | Ask an expired approval request again | The proposer of the decision, or a platform admin (the engine checks it) | Web app only |
<!-- end generated: role-tools -->
