# PMO

**Role key:** `pmo`. **Under Balanced:** scores demand and runs portfolio reviews.

The PMO looks after the whole portfolio: what is asked for, what gets funded, how each portfolio stands and whether
AI app development is working.

## What the PMO does

- **Reads the portfolio.** The **Overview** (`/app/overview`) summarizes a portfolio: owner gaps, reviews overdue,
  approvals and AI drafts waiting, flagged deployments and change windows. Every number opens its rows, such as Owners,
  Gaps or Reviews, Overdue.
- **Scores demand.** **Demand** (`/app/demand`) lists the requests on the table. The PMO scores each one with a score,
  a size band and a horizon (`score_intake_request`), and proposes a priority (fund, defer or stop) on the top ones
  (`save_decision_proposal`, a priority decision). The request's sponsor decides it, or its business owner when it
  names no sponsor; it is the Commit gate.
- **Runs portfolio reviews.** **Portfolios** (`/app/portfolios`) is the tree, with a review pack per portfolio. The
  PMO proposes portfolio decisions, such as keep, invest, replace or retire, and may rename a portfolio, describe it
  or change its level (`save_portfolio`); a platform admin changes the rest.
- **Advises on intake.** Under Balanced every intake request consults the PMO. The answer is advice.
- **Reads the measures.** **Measures** (`/app/measures`) shows each measure over 7, 30 or 90 days with its trend, its
  baseline and its target from the configuration, and the slide-back signals: which fire, which are quiet, and the
  rows behind each. A signal can lead to a decision, recorded from the signal itself.
- **Reads the claims and the gap check runs**, which most roles do not see.

## In the demo

Mateo Brandt plays the PMO. He is also sponsor of Claims Triage Agent and a deputy business owner.

- **S-01:** the Overview of Field operations, its owner gaps and its overdue reviews.
- **S-04:** as sponsor, he approves the owner change that makes Nadia Ferreira business owner of Claims Triage Agent.
- **S-09:** he scores "Weather alerts for outdoor jobs", sets its horizon to now and proposes "fund"; the review pack
  of Field operations.
- **S-13:** the measures and the slide-back signals.

## What the role may call

<!-- generated: role-tools (node tools/docs-gen.mjs) -->
- **Demo persona:** Mateo Brandt (`mateo`).
- **Reads:** 57 of the 61 read tools, among them the ones only some roles get: `list_claims`, `list_gap_check_runs`.

The shipped Tool Policy lets this role call 12 write tools. The engine then checks the relation each one names, so a tool on this list can still refuse a person who is not, for example, an owner of the application.

| No. | Tool | What it does | Who exactly | Served |
|---|---|---|---|---|
| 12 | `save_intake_draft` | Start or edit an intake request | requester, maker, application-owner, pmo, ai-lead, platform-admin | Web app and AI clients |
| 13 | `answer_tier_questions` | Which tier do my answers suggest? | Requester of the intake, owner of the app | Web app and AI clients |
| 17 | `save_decision_proposal` | Record a decision for acceptance | Holders of R, A or C for the decision type | Web app and AI clients |
| 18 | `submit_record` | Send it for approval | The record's requester or creator | Web app and AI clients |
| 19 | `withdraw_record` | Take it back | The record's requester or creator | Web app and AI clients |
| 45 | `save_link` | Link this work item or this evidence | Owner of the subject's app, change-manager, pmo; evidence on records the caller can write | Web app and AI clients |
| 46 | `record_owner_leave` | This owner leaves on this date | The leaving person (any role), an owner of an app the person owns, platform-admin | Web app and AI clients |
| 52 | `answer_approval` | Approve or reject a request I was asked | The person the approval request asks, through any role (the engine checks the role or owner role, the separation rules and the request's state) | Web app only |
| 60 | `score_intake_request` | Score a request on the table: score, size band, horizon | pmo, platform-admin | Web app and AI clients |
| 62 | `complete_review` | This review is done | The person whose review it is (the engine checks the lead owner role, the decision right or the approval rule, and the accepted outcome decision) | Web app only |
| 74 | `save_portfolio` | Create or change a portfolio | platform-admin, pmo (name, description and level only) | Web app only |
| 100 | `renew_approval_request` | Ask an expired approval request again | The proposer of the decision, or a platform admin (the engine checks it) | Web app only |
<!-- end generated: role-tools -->
