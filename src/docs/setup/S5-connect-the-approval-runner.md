# Guide S5. Connect the approval runner

- **Status:** 0.2 draft, 2026-11-23. Nothing here was run against a tenant. The approval runner's flows are a template that nobody has imported yet; a stand-in plays the runner in the local steps and the tests.
- **Guide set:** setup guides S1 to S7 and admin guides A1 to A4. Index: [README](../README.md).
- **Design sources:** document 23, S1 and B23-6; document 25, section 12.2; document 26, sections 3.4, 7 and 8; document 28, issue 7, Q19 and question 12; `src/adapters/azure/flows/README.md`; `src/adapters/azure/README.md`, "The approval runner".
- **Open design questions that touch this guide:** 12 (marker below).

## Purpose

The product never approves anything. A person approves on a Power Automate approval card in Outlook, Teams or the Power Automate action center, and the engine decides whether that answer counts. The **approval runner** is the small group of flows in the client's tenant that carries each approval request from the product to the people asked, and carries each answer back. It is the only writer of approval outcomes, and it writes through the approval writer workload role (document 28, issue 7; working default Q19).

This guide connects the runner to a deployed product: its identity, its app role, its Workload Grant, the flows, and the switch that serves its routes. The click-by-click import steps are in `src/adapters/azure/flows/README.md`.

## How it works, in short

1. The engine opens one Approval Record per required answer (a rule with Minimum Count 2 gets two). When it opens a record, the same database transaction adds a row to the **approval outbox** (IDs only). When the engine writes the record's final outcome (an answer, expired or cancelled), the same transaction settles the row.
2. Every five minutes the runner's parent flow claims waiting requests with a lease (`POST /engine/v1/approval-runner/claims`) and starts one child flow per request. Each claim first has the engine, as the runner, expire the records past their Expires On, open their restarts, and cancel the records that settled decisions still hold open.
3. The child flow works out who to ask (the current holders of an owner role, or the user members of the Entra groups bound to a product role), sends one card, reports it as sent, waits for the first answer, and posts it to `POST /engine/v1/approval-records/{id}/outcome` with `Idempotency-Key: <recordId>:<responderId>:<respondedOn>`.
4. A request the child cannot send or wait for is given back (`release`). After `Product:ApprovalRunner:MaxAttempts` (default 3) it becomes failed and an admin looks at it (guide [A4](../admin/A4-approval-runner-operations.md)).

Every runner route takes only an app-only token with the `Approval.Write` app role, and then serves only a caller that the configuration in force resolves as the approval runner: a workload with a current `approval-writer` Workload Grant. A person, an AI client, a pipeline and an agent identity get 403 on every runner route.

## Who

| Who | Part |
|---|---|
| The client's Entra admin | Registers the runner's app, adds its certificate credential, assigns it the `Approval.Write` app role |
| The client's Power Platform admin | Creates the approvals environment, the flow group and its Process license; imports the flows |
| The Key Vault owner | Holds the runner's certificate and its password as secrets, and gives the approvals environment read access to them |
| The client's configuration admin | Instructs the Workload Grant for the runner as a configuration change; a person accepts it on a card |
| The client's platform admin | Turns on the runner's routes and runs the first test |

## Prerequisites

- The product is deployed (guide [S4](S4-prepare-entra-and-deploy.md)), and version 1 with the client binding patch is in force (guide [S1](S1-pick-presets-and-overlays.md), steps 7 and 8, which are not built yet).
- Every role a rule asks has a bound group with at least one user member, and every application whose rules ask an owner role has a current owner.
- A Power Platform environment with Dataverse for the runner only (the approvals environment), and an Azure Key Vault.
- A Power Automate Process license for a flow group that holds both flows. That is the product's working default; the licensing rules are Microsoft's, quoted under "Microsoft facts used" below and in `flows/README.md`, "Licenses: what Microsoft says". Prices are not given here; Microsoft publishes them. Microsoft lets an approver answer from Outlook, Teams or the action center "if they have a Power Automate license or an Office 365 or a Dynamics 365 license with built-in Power Automate capabilities"; a guest approver needs a Power Automate license (both quoted below).

## Steps

1. **Create the approvals environment.** A Power Platform environment with Dataverse, used only for the runner. Do not restrict it to a security group unless every approver is in that group: Microsoft gives approvers outside the environment the Approvals User role automatically, except when a security group controls access to the environment (or ad hoc subscriptions are off, or on US Government plans).

2. **Register the runner's identity.** An app registration named for the runner, for example "`<product>` approval runner", single tenant, with a certificate credential. No client secret. Put the certificate (base64 PFX content) and its password into Key Vault as two secrets. The template signs in with this certificate because no secretless way for a cloud flow's HTTP action to sign in to an Entra-protected API was confirmed for it (open point 1).

3. **Assign the `Approval.Write` app role** of the product's API to the runner's service principal, directly, as `infra/README.md`, section "3. Pipelines and the approval runner", shows (`New-MgServicePrincipalAppRoleAssignedTo`, or API permissions and admin consent on the runner's registration). Never through a group: Entra puts no app role into a service principal's token through a group. Note the assignment's ID.

4. **Record the Workload Grant.** The configuration admin instructs a configuration change (in the client binding patch at install, or later through `propose_configuration_change`) that upserts one `workload-grant` row:

   | Column | Value |
   |---|---|
   | `principalObjectId` | The runner's service principal object ID |
   | `workloadRoleKey` | `approval-writer` |
   | `reason` | Why the runner needs the role, in plain words |
   | `usesFederatedCredential` | `false` for this template, which signs in with a certificate |
   | `bindingDetail` | The app role assignment ID from step 3 |
   | `expiresOn` | A date no more than `workload-grant-max-days` (default 365) after the day the change is validated |

   A person accepts the change on a card; the engine stamps the deciding decision on the grant. The app role lets the token through the door; the grant is what the engine checks. WORKING DEFAULT (open design question 12)

5. **Create the flow group.** In the approvals environment, one flow group with one Process license, and add **both** flows to it after import (step 7): Microsoft counts parent and child flows separately, and a child flow does not inherit the group's capacity from its parent.

6. **Build and import the flows** with the client's own publisher prefix, as `flows/README.md`, section "Build, pack and import", describes: `build-solution.mjs` writes the unpacked solution outside the repository, `pac solution pack` packs it, and the import takes a settings file with the connection references and the six environment variables (`<prefix>_ApiBaseUrl`, `<prefix>_ApiAudience`, `<prefix>_TenantId`, `<prefix>_RunnerClientId`, and the two Key Vault secret references). `<prefix>_ApiAudience` is the API's Application ID URI, `https://<host>/mcp` once the custom domain is bound.

7. **Add both flows to the flow group, then turn them on:** the child flow first, then the parent.

8. **Serve the runner's routes.** From `src/adapters/azure`: `azd env set PRODUCT_APPROVAL_RUNNER_ENABLED true`, then `azd provision`. Until then every runner route answers `not_found`, and the outbox fills either way, so nothing is lost by turning it on later.

9. **Test with one request.** Have a requester submit a small intake (`save_intake_draft`, then `submit_record`) through Claude Code. Within one poll interval a card reaches the role holders. Answer it as one of them, then check the record (Checks below).

## Checks

- `check_role_bindings`, run by the platform admin, lists the runner's grant with `assignmentFound` true and `isExpired` false, and reports no direct assignment without a grant.
- A call to `POST /engine/v1/approval-runner/claims` with a person's token, or with a pipeline's token, answers 403.
- After step 9: `get_approval_status` on the decision shows the answer with its approver, the time of the answer and whether it counts (`countsTowardRequirement`). An answer counts only from a person who signed in as a user, holds the role the card asked for, and passes the separation rules in force.
- The same answer posted again returns the first result and writes nothing (the runner's retries are safe).
- The claim's answer has a `refresh` part that counts the decisions the engine brought up to date and the records it expired, cancelled and opened; the service's log says the same on every poll.
- The parent flow's run history shows one run every five minutes; a run that claims nothing ends at once.
- No run history shows the certificate: every HTTP action has secure inputs.

## What the engine does with several answers, expiry and restarts

- **Several answers for one requirement.** Each card is first to respond and its record holds one answer. A rule whose Minimum Count is above 1 (the strict separation overlay asks two change board members to decide an exception) gets that many records and cards, and is met when that many different people have approved; "everyone must approve" is met at Minimum Count too, and a sequential group sends its steps one at a time. One person counts once per requirement. For an owner role the next card leaves out the holders whose approval already counts.
- **Answers that do not count.** An answer from an AI client or an agent, from someone who does not hold the role, given at or after Expires On, or a second answer from someone who already counts is kept, does not count, and the engine opens a new request for the same requirement in the same transaction. Its card goes out on the next poll.
- **Expiry and restart.** On each poll the engine writes expired on the records past their Expires On and opens a new record for each, with a new Expires On. An answer to an expired record is refused with 409.
- **Settling.** When the decision is accepted or rejected, the engine cancels the records still pending; an answer to one of them is refused with 409.

## What the runner cannot do yet

- **Cancel a card.** The Approvals connector has no action that cancels an open approval, so a card for an expired or cancelled record can still be answered; the product then answers 409 and records nothing.
- **Expire while it is off.** Expiry and restarts run in the runner's claims, so while the parent flow is off no record is written as expired and no restart goes out. Document 23, S2 has a daily timer for this; the Azure binding has none yet.
- **Leave out a product role's earlier responders.** For a product role the child lists the group's members and cannot tell who already answered, so a person whose answer already counts may get the next card for the same requirement. Their second answer does not count, and the engine asks again.
- **Wait longer than 28 days.** A flow can wait at most 28 days; the child waits at most 27, then gives the request back, and a new card goes out on the next poll (for a restart, if the record expired meanwhile). The old card stays in the action center.

## Open points

1. **A secretless sign-in for the runner.** The template uses a certificate in a Key Vault-backed environment variable, so its grant has `usesFederatedCredential` false. The schema says a false value "is flagged", but no check in this release reads the column yet: neither `get_configuration_health` nor `check_role_bindings` reports it. Proposal: `check_role_bindings` lists credential-bearing grants as a finding, and a tenant test looks for a federated credential path for the HTTP action.
2. **The runner's Principal Reference.** `flows/README.md` asks for a Principal Reference that records the runner as a service principal. Outside the local seed, the product writes no Principal Reference row for a workload yet. Proposal: the binding patch apply, or the grant's activation, writes one for each grant's principal.
3. **Pull, not push.** Document 23 has the API push each request to a flow; the runner pulls instead (B23-6), so the API stays inbound only. The cost is the poll interval. This is a proposal for the design.
4. **Not verified in a tenant:** the responder ID and response date the Approvals connector returns, whether group member listing returns each member's type and whether it lists agent users, and whether **Run a Child Flow** returns when the child answers (`flows/README.md`, "Not verified yet").

## Microsoft facts used

| Fact | Source | Checked |
|---|---|---|
| "Currently, if you add a service principal to a group, and then assign an app role to that group, Microsoft Entra ID doesn't add the `roles` claim to tokens it issues." | https://learn.microsoft.com/entra/identity-platform/howto-add-app-roles-in-apps | 2026-11-23 |
| "You can assign a Process license directly to one solution-aware cloud flow or to a flow group. A flow group lets up to 25 solution-aware cloud flows share one Process license. You must explicitly add every parent and child flow that should use the group's capacity."; among the cases that need a Process license: "Your flows are running under Application user (flow owner is a Service Principal)." and "Your premium flow is invoked by multiple users. In this case, either everyone needs a Premium license, or the flow needs a Process license."; "The flow must be in a solution before a Process license can be assigned." | https://learn.microsoft.com/power-platform/admin/power-automate-licensing/faqs | 2026-11-23 |
| "Adding a parent flow to a flow group doesn't add its child flows. Add each child flow to the same group separately if it should use the group's shared Process capacity. Each parent and child flow counts separately toward the 25-flow limit." | https://learn.microsoft.com/power-automate/create-flow-group | 2026-11-23 |
| "An approval flow can wait for 28 days. If the wait time exceeds 28 days, that flow fails. This only impacts the flow itself, meaning that the approval continues to exist in the action center."; for guests: "The guest user must be assigned a valid Power Automate license (per user license or Microsoft 365-based user license) to view or respond to the approval." | https://learn.microsoft.com/power-automate/approvals-known-issues | 2026-11-23 |
| "When you assign an approval to users who aren't in your environment, they're automatically given the *Approvals User* Dataverse security role."; the configurations that don't allow this include "If a security group was used to control which users have access to the Dataverse environment."; "After you assign an approval request to a user, they can respond directly from an Outlook email, a Microsoft Teams adaptive card, or the Power Automate action center if they have a Power Automate license or an Office 365 or a Dynamics 365 license with built-in Power Automate capabilities." | https://learn.microsoft.com/power-automate/get-started-approvals | 2026-11-23 |
