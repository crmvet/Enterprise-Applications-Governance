# Change board

**Role key:** `change-board`. **Under Balanced:** decides exceptions about changes, and windows that cover several
applications, as a group.

The change board is a committee: an Entra group bound to the role, whose members answer as one. Under Balanced it
holds the decision right and the approval of an exception about a standard, normal or emergency change. One member's
answer is enough under Balanced; the strict separation overlay asks for two.

## What a member of the change board does

- **Answers exceptions about changes.** **Approvals** and **My work** list the requests; each opens on the decision
  page, with the standard it sets aside and its review date. Approve or Reject, with a typed
  reason.
- **Proposes them too.** A member may propose an exception about a change (`save_decision_proposal`), for example when
  a change cannot follow its usual path in time.
- **Sees what waits on the board.** **Teams** (`/app/teams?kind=committee`) lists the committees, and the Change
  board's page (`/app/teams/<id>`) shows its members, what it decides, and every request waiting on it, soonest expiry
  first. The decision page names the team a request waits on: "Waiting on the Change board". A member answers on the
  decision page, never on the team page.
- **Reads the log.** **Decisions**, Exceptions lists the standing exceptions and when each is reviewed again.

## In the demo

Tomas Rendel plays the change board.

- **S-16:** Tomas proposed an exception about the Desk Booking calendar connector update (a normal change, with no
  rollback rehearsal before the vendor's old API closes), and the Change board's approval request waits for an answer. The
  Teams page shows it waiting on the board. Marcus Oyelaran (`marcus`), another member of the board and an architect,
  answers it on the decision page, so the member who asked is not the one who answers.

Tomas is also business owner of Parts Catalog API, so he answers its change as owner.

## What the role may call

<!-- generated: role-tools (node tools/docs-gen.mjs) -->
- **Demo persona:** Tomas Rendel (`tomas`).
- **Reads:** 55 of the 61 read tools. It gets none of the reads kept to a few roles.

The shipped Tool Policy lets this role call 8 write tools. The engine then checks the relation each one names, so a tool on this list can still refuse a person who is not, for example, an owner of the application.

| No. | Tool | What it does | Who exactly | Served |
|---|---|---|---|---|
| 17 | `save_decision_proposal` | Record a decision for acceptance | Holders of R, A or C for the decision type | Web app and AI clients |
| 18 | `submit_record` | Send it for approval | The record's requester or creator | Web app and AI clients |
| 19 | `withdraw_record` | Take it back | The record's requester or creator | Web app and AI clients |
| 45 | `save_link` | Link this work item or this evidence | Owner of the subject's app, change-manager, pmo; evidence on records the caller can write | Web app and AI clients |
| 46 | `record_owner_leave` | This owner leaves on this date | The leaving person (any role), an owner of an app the person owns, platform-admin | Web app and AI clients |
| 52 | `answer_approval` | Approve or reject a request I was asked | The person the approval request asks, through any role (the engine checks the role or owner role, the separation rules and the request's state) | Web app only |
| 62 | `complete_review` | This review is done | The person whose review it is (the engine checks the lead owner role, the decision right or the approval rule, and the accepted outcome decision) | Web app only |
| 100 | `renew_approval_request` | Ask an expired approval request again | The proposer of the decision, or a platform admin (the engine checks it) | Web app only |
<!-- end generated: role-tools -->
