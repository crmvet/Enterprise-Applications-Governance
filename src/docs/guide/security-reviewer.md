# Security reviewer

**Role key:** `security-reviewer`. **Under Balanced:** runs security reviews and agrees agent autonomy levels.

The security reviewer answers for the riskiest work: tier 3, restricted data sent to AI, and how far an agent may act
on its own.

## What a security reviewer does

- **Decides autonomy levels.** Under Balanced the security reviewer is accountable for an agent's autonomy level and
  answers its request on the decision page; the AI lead is consulted.
- **Answers tier 3 and restricted data.** Under Balanced the security reviewer is asked, beside the other approvers,
  on a tier 3 tier decision, a tier 3 go-live, a tier 3 data use, and any use of highly confidential data with AI.
  These answers need a typed reason.
- **Runs security reviews.** **Reviews** lists the security reviews due, often requested by an owner or the AI lead
  (`request_security_review`). The reviewer records the outcome decision and marks the review done
  (`complete_review`).
- **Advises on intake.** Under Balanced every intake request consults the security reviewer. The answer is advice.
- **Uses Agents and Data.** **Agents**, Autonomy shows the agents with no level or a level waiting; **Data**, Uses
  and Gaps show the restricted data each application uses.

A security reviewer who proposed a decision at tier 3 cannot answer its request.

## In the demo

Noor Safadi plays the security reviewer. She is also a deputy business owner, and security owner of Customer
Feedback Bot.

- **S-07:** after Kai Ostrowski approves Visit Report Agent's use of safety findings with AI (1 of 2), Noor approves
  as security reviewer, and the use is allowed.
- **S-08:** she decides Supplier Price Agent's autonomy level on its decision page, and Customer Feedback Bot's security review
  is due in her Reviews.

## What the role may call

<!-- generated: role-tools (node tools/docs-gen.mjs) -->
- **Demo persona:** Noor Safadi (`noor`).
- **Reads:** 55 of the 61 read tools. It gets none of the reads kept to a few roles.

The shipped Tool Policy lets this role call 11 write tools. The engine then checks the relation each one names, so a tool on this list can still refuse a person who is not, for example, an owner of the application.

| No. | Tool | What it does | Who exactly | Served |
|---|---|---|---|---|
| 17 | `save_decision_proposal` | Record a decision for acceptance | Holders of R, A or C for the decision type | Web app and AI clients |
| 18 | `submit_record` | Send it for approval | The record's requester or creator | Web app and AI clients |
| 19 | `withdraw_record` | Take it back | The record's requester or creator | Web app and AI clients |
| 33 | `record_document_link` | Here is the page, and its status | Owner of the app, document owner | Web app and AI clients |
| 34 | `confirm_document_checked` | Is this document still true? | Owner of the app, document owner | Web app and AI clients |
| 45 | `save_link` | Link this work item or this evidence | Owner of the subject's app, change-manager, pmo; evidence on records the caller can write | Web app and AI clients |
| 46 | `record_owner_leave` | This owner leaves on this date | The leaving person (any role), an owner of an app the person owns, platform-admin | Web app and AI clients |
| 52 | `answer_approval` | Approve or reject a request I was asked | The person the approval request asks, through any role (the engine checks the role or owner role, the separation rules and the request's state) | Web app only |
| 62 | `complete_review` | This review is done | The person whose review it is (the engine checks the lead owner role, the decision right or the approval rule, and the accepted outcome decision) | Web app only |
| 94 | `request_security_review` | Please review the security of this agent or app | Owner of the app, ai-lead, security-reviewer, architect, platform-admin | Web app and AI clients |
| 100 | `renew_approval_request` | Ask an expired approval request again | The proposer of the decision, or a platform admin (the engine checks it) | Web app only |
<!-- end generated: role-tools -->
