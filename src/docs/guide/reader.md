# Reader

**Role key:** `reader`. **Under Balanced:** reads the register, decisions and the change schedule. No audit entries,
no restricted columns. Never writes.

A reader is anyone who needs to see the portfolio without working in it: a manager, a planner, a colleague who wants
to know who owns an application.

## What a reader does

- **Finds applications.** **Applications** (`/app/applications`) with search and filters (portfolio, stage, tier,
  kind, owner, registration, uses AI, build path), and **Application 360** for one application: its owners, tier,
  stage, documents, dependencies and decisions.
- **Reads the portfolio.** The **Overview**, **Portfolios**, the **Timeline** and **Changes**, Schedule.
- **Reads decisions.** **Decisions** (`/app/decisions`) and each decision page.
- **Finds people and teams.** **Owners** and **Teams**: who owns what, and who is in each team.

A reader's role grants no writes. A reader who also holds an owner role on an application, or is the person an
approval request asks, acts through that: the Tool Policy lets every role call the person's own acts (answering a
request they were asked, recording their own leave), and the engine checks each one. A reader never sees the audit
trail or the fields marked restricted.

## In the demo

Theo Marsh plays the reader. He is technical owner of Desk Booking and Visitor Sign-In Kiosk and a deputy technical
owner of four more; My work shows one owner gap (Desk Booking has no deputy technical owner). He is a member of the
Service Desk team.

- **S-18:** search by name and by record number, the list filters kept in the address, a person's page, and what a
  reader may and may not do.

## What the role may call

<!-- generated: role-tools (node tools/docs-gen.mjs) -->
- **Demo persona:** Theo Marsh (`theo`).
- **Reads:** 55 of the 61 read tools. It gets none of the reads kept to a few roles.

The shipped Tool Policy lets this role call 4 write tools. The engine then checks the relation each one names, so a tool on this list can still refuse a person who is not, for example, an owner of the application.

| No. | Tool | What it does | Who exactly | Served |
|---|---|---|---|---|
| 46 | `record_owner_leave` | This owner leaves on this date | The leaving person (any role), an owner of an app the person owns, platform-admin | Web app and AI clients |
| 52 | `answer_approval` | Approve or reject a request I was asked | The person the approval request asks, through any role (the engine checks the role or owner role, the separation rules and the request's state) | Web app only |
| 62 | `complete_review` | This review is done | The person whose review it is (the engine checks the lead owner role, the decision right or the approval rule, and the accepted outcome decision) | Web app only |
| 100 | `renew_approval_request` | Ask an expired approval request again | The proposer of the decision, or a platform admin (the engine checks it) | Web app only |
<!-- end generated: role-tools -->
