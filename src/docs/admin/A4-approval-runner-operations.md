# Guide A4. Run the approval runner day to day

- **Status:** 0.2 draft, 2026-11-23. Nothing here was run against a tenant. The runner's flows have not been imported anywhere; the outbox, its routes and the approval outcome endpoint are built and tested on the local server.
- **Guide set:** setup guides S1 to S7 and admin guides A1 to A4. Index: [README](../README.md).
- **Design sources:** document 23, S1, S2 and B23-6; document 26, section 7; document 21, section 4.7; `src/adapters/azure/flows/README.md`; `src/adapters/azure/README.md`, "The approval runner"; `src/adapters/azure/sql/0005-approval-outbox.sql`.
- **Open design questions that touch this guide:** none.

## Purpose

Once the approval runner is connected (guide [S5](../setup/S5-connect-the-approval-runner.md)), most requests need nobody: a card goes out within one poll, a person answers, and the engine counts the answer. This guide is for the rest: a card that never went out, a request the runner gave up on, an answer that came too late or does not count, and a card that outlived its flow.

Two rules hold in every case below. No tool, page or person other than the approval runner writes an approval outcome, and the engine alone decides whether an answer counts. An admin fixes the cause and sends the request through again; an admin never answers for someone.

## Who

| Who | Part |
|---|---|
| The client's platform admin | Watches the requests, finds the cause, asks the right owner to fix it |
| The runner's owner (the Power Platform admin of the approvals environment) | The flows, their connections, the flow group and its license |
| The requester of a decision | Withdraws and submits again when a request cannot finish |
| The client's configuration admin | Renews the runner's Workload Grant (guide [A3](A3-workload-grants.md)) |

## Where to look

| Place | What it shows | Who can see it |
|---|---|---|
| `get_approval_status` on the decision | Each Approval Record: the role or owner role asked, the outcome, the approver, the time of the answer, whether it counts (`countsTowardRequirement`), the separation rule that refused it, and what is still needed | Any product role |
| The service's logs (Application Insights) | "Approval runner claimed N item(s) ... M item(s) needed no card. The refresh brought D decision(s) up to date: E record(s) expired, C cancelled, O opened." per poll; "More decisions have approval work due than one claim refreshes (N); the next claim goes on." when more than `Product:ApprovalRunner:MaxRefresh` (default 50) decisions are due at once; a warning that names the decision when a refresh did not run, and one when the engine could not list the approvals due; and "Approval runner released `<record ID>` (`<reason>`); attempts n, now `<state>`" per release. Reasons come from a closed list, so no text a person wrote reaches the log | Whoever the client lets read the logs |
| The parent and child flows' run history | One parent run every five minutes, one child run per request, and why a child failed | The runner's owner |
| The outbox table, `host.ApprovalOutbox` in Azure SQL | One row per Approval Record: state, attempts, card reference, run link, times. IDs only | Database principals with read rights; no product route lists it yet (open point 1) |

The outbox states:

| State | Meaning | Moves to |
|---|---|---|
| `pending` | Waiting for the runner to claim it | `claimed` |
| `claimed` | Leased to one runner call until its lease ends (15 minutes by default) | `sent`; back to claimable when the lease runs out, with one more attempt |
| `sent` | A card went out; the runner waits for the answer | `answered`, `expired`, `cancelled`, or back to `pending` on a release |
| `answered` | The record holds an answer (approved, rejected or recommendation only) | Never changes again |
| `expired` | The engine wrote expired on the record, which passed its Expires On, and opened a restart with a row of its own | Never changes again |
| `cancelled` | The engine cancelled the record: on a withdraw, or because the decision was accepted or rejected while the record was still pending | Never changes again |
| `failed` | Given back as many times as `Product:ApprovalRunner:MaxAttempts` allows (default 3); no new card goes out | Nothing moves it yet (open point 2) |

## Situations

### No card goes out at all

1. A runner route that answers `not_found` means the routes are off: `azd env set PRODUCT_APPROVAL_RUNNER_ENABLED true`, then `azd provision`. The outbox kept filling, so nothing is lost.
2. A 403 in the parent flow's run means the runner's token or grant: check the `Approval.Write` assignment and the runner's Workload Grant with `check_role_bindings` (guide A3, part B).
3. No parent run in the last few minutes means the flows are off, a connection broke, or the flow group lost its Process license (a flow that depended on the group's capacity is suspended until relicensed).

### The runner gave a request back

The reason in the log says why. Each release returns the request to `pending` with one more attempt.

| Reason | Usual cause | Fix |
|---|---|---|
| `no-approvers` | The group bound to the role has no user members, or the owner role has no current holder on the application | Add the right people to the group, or record the owner (`propose_owner_change`) |
| `send-failed` | The Approvals connection or the Office 365 connections failed | The runner's owner repairs the connection |
| `timeout` | The child waited 27 days, the most a flow can wait | A new card goes out on the next poll if the record has not expired; the old card stays in the action center (below) |
| `outcome-refused` | The approval outcome endpoint refused the answer with something other than 409, for example 403 because the responder was a workload | The record keeps waiting for a person; look at who answered |
| `other` | Anything else | The child's run history |

### A request failed

After `MaxAttempts` releases or lapsed leases the request is `failed` and no card goes out for it. Fix the cause first (the table above), then ask the requester to withdraw the record (`withdraw_record`) and submit it again (document 21, section 4.7). The withdraw cancels the decision's pending Approval Records, which settles their outbox rows as `cancelled`; the new submission opens new records and new outbox rows. When the record's rule has a timeout and nobody withdraws, the engine expires the failed request at its Expires On and opens a restart, which goes out as a new request. No product route returns a failed request to `pending` yet (open point 2), and an admin does not edit the outbox table by hand.

### A request expired

On the first poll after a record's Expires On, the engine writes expired on it, which settles its outbox row as `expired`, and opens a new record for the same requirement with a new Expires On, whose card goes out on that poll or the next. Nobody needs to act. An answer that reaches the product later for the expired record is refused with 409 and records nothing; the person answers the new card. While the runner's routes are off or its parent flow is stopped, nothing is written as expired, and the restarts go out once the polls resume (open point 4).

An answer given at or after Expires On that reaches the product before the engine expired its record is kept, does not count, and the engine opens the restart in the same transaction.

### An answer does not count

`get_approval_status` shows `countsTowardRequirement` false. The engine counts an answer only from a person signed in as a user who holds the role the card asked for (for an owner role, a current Owner Assignment on the application), passes the separation rules in force, answered before Expires On, and has not already given a counting answer for the same requirement; `refusedBySeparationRuleKey` names a separation rule that refused it. An answer from an agent user, an agent identity or another non-user principal is stored as recommendation only. The engine opens a new request for the requirement in the same transaction, so a new card goes out on the next poll: make sure it reaches someone who holds the role. If no such person exists, the runner gives the request back with `no-approvers` (above).

### A rule needs more than one answer

A rule with Minimum Count above 1 gets that many requests and cards, and "everyone must approve" is met at Minimum Count too; a sequential group sends its steps one at a time. The engine counts one person once per requirement, so the rule is met only when that many different people have approved. For an owner role, the next card leaves out the holders whose approval already counts; if fewer people hold the role than the rule needs, the runner gives the request back with `no-approvers`, and the fix is another holder or a configuration change. For a product role, see open point 3. A configuration admin finds such rules with `get_configuration` for `approval-rule` (the strict separation overlay has one, for change exceptions).

### A decision settled while cards were still out

When the decision is accepted or rejected, the engine cancels the records still pending in the same transaction, which settles their outbox rows as `cancelled`. Their cards stay in the action center (below); an answer on one of them is refused with 409.

### Old cards in the action center

A flow that waited past its limit, or a card for a record that has since settled, leaves an approval in the Power Automate action center with no flow waiting on it. The Approvals connector has no action that cancels it, so the product cannot. Microsoft: the requestor or an environment admin deletes such approvals from the action center by hand. An answer on an old card reaches nothing, or is refused with 409 when its record has expired, been cancelled or been answered.

## Routine

- **Weekly:** read the release log lines and the refresh warnings; look for `failed` requests (open point 1) and for decisions whose `get_approval_status` shows a record pending for longer than its rule's timeout, a sign that the runner did not poll.
- **Monthly:** `check_role_bindings` for the runner's grant and assignment; the flow group's capacity page; the old approvals in the action center.
- **Thirty days before the runner's grant expires:** renew it (guide A3, part C).
- **Never:** grant an AI client app the Microsoft Graph permissions that answer approvals (`ApprovalSolution.ReadWrite`, `ApprovalSolutionResponse.ReadWrite`; document 23, S1), add reassignment to the cards, or give anyone but the runner the `Approval.Write` app role.

## Checks

- No request is `failed` without an owner working on it.
- Every request older than its rule's timeout is either answered, expired or withdrawn.
- The runner's grant is current, and its expiry is more than thirty days ahead or a renewal is under way.
- No identity other than the runner's holds `Approval.Write`.

## Open points

1. **No product view of the outbox.** Only database principals can read `host.ApprovalOutbox`, and on a production server those are the app's identity and the break-glass SQL administrators (guide A2, part B). A request that fails because its lease ran out too often is marked `failed` during a claim without a log line of its own. Proposal: a read route or tool for platform admins that lists `failed` and long-`sent` requests, and a log line when a claim marks a request failed.
2. **No way to send a failed request again** other than withdraw and submit. The engine opens a new request only when a record expires or an answer does not count, not when the runner gives up. Proposal: a runner route that returns a failed request to `pending`, or an engine-side restart of a failed request.
3. **Several answers for a product role.** The runner cannot leave out a group member whose answer already counts, so that person may get the next card too; their second answer does not count, and the engine asks again. Proposal: the claim item lists the responders who already count, and the child flow leaves them out.
4. **Reminders, escalation, and expiry while the runner is off** (document 23, S2) need a timer the Azure binding does not have yet; expiry and restarts run in the runner's claims today.
5. **An answer given in time that arrives after the expiry.** A claim that runs at or after Expires On has the engine write expired at once, so an answer the person gave before Expires On, which the engine would count, is refused with 409 when its child flow posts it after that claim; the person answers the restart's card. Proposal: a grace period after Expires On before the engine writes expired, or an engine path that takes such an answer in place of the expired outcome (the Azure binding's `ENGINE-CHANGES-NEEDED.md`, item 19).

## Microsoft facts used

| Fact | Source | Checked |
|---|---|---|
| "An approval flow can wait for 28 days. If the wait time exceeds 28 days, that flow fails. This only impacts the flow itself, meaning that the approval continues to exist in the action center."; "The requestor or environment admin then needs to manually delete these approvals from the action center." | https://learn.microsoft.com/power-automate/approvals-known-issues | 2026-11-23 |
| "Removing a flow's Process license or switching it to a different plan removes the flow from its group and breaks any associations. Flows that depended on the group's Process capacity are suspended until relicensed." | https://learn.microsoft.com/power-automate/create-flow-group | 2026-11-23 |
