# Guide A3. Workload grants: grant, check, renew and retire

- **Status:** 0.2 draft, 2026-11-23. Nothing here was run against a tenant.
- **Guide set:** setup guides S1 to S7 and admin guides A1 to A4. Index: [README](../README.md).
- **See also:** [admin.md](../admin.md#roles-and-access), [install.md](../install.md#3-pipelines-and-the-approval-runner); `config/schema/client-binding-patch.schema.json`.
- **Open design questions that touch this guide:** 4 and 12 (markers below).

## Purpose

People get product roles only through Entra groups, never by a direct assignment. Pipelines and the approval runner are not people, and Entra puts no app role into a service principal's token through a group, so each of them holds its workload role directly: an app role assignment on the product's API, recorded in the configuration as a **Workload Grant** with a reason, the decision that allowed it and an expiry. The engine checks the grant on every app-only call; the app role only lets the token through the door. WORKING DEFAULT (open design question 12)

This guide keeps the grants true over time: how one is granted, how to read the check, how to renew before a grant lapses, and how to retire one. Cutting a workload off now is in guide [A2](A2-urgent-revoke-and-break-glass.md), part A.

## The rules a grant follows

| Rule | Where it holds |
|---|---|
| One grant names one non-human principal and one workload role: `deployment-recorder` or `approval-writer` | The configuration schema; the engine resolves only a role with Is Workload Role = Yes |
| Agent identities and agent users never hold a grant | The engine refuses to resolve them as a workload; `check_role_bindings` reports a grant that names one |
| Expires On is required and at most `workload-grant-max-days` (default 365) after the day the change is validated | V28, at validation and again at activation |
| From Expires On the grant admits no app-only token | The engine, on every call |
| Renewal is a new configuration change decision | The schema: a new date needs a new decision |
| The grant records whether the principal signs in without a stored secret (`usesFederatedCredential`) | The schema says a false value is flagged; no check reads it yet (open point 2) |
| A grant is a client fact: it lives in the client's private repository or nowhere, never in a public preset | V44 |

## Who

| Who | Part |
|---|---|
| The workload's owner (a pipeline owner, the approval runner's owner) | Asks for the grant, names the reason, renews it in time |
| The client's Entra admin | Makes and removes the app role assignment; disables an identity or its credential |
| The client's configuration admin | Instructs the configuration change that adds, renews or retires a grant |
| A configuration admin who accepts | Accepts the engine's diff on the decision page |
| The client's platform admin | Runs `check_role_bindings` and follows up on its findings |

## Part A. Grant

1. **Make the app role assignment.** The Entra admin assigns `Deployment.Record` (a pipeline) or `Approval.Write` (the approval runner) of the product's API directly to the workload's service principal, never through a group (guide [S6](../setup/S6-connect-pipeline-record-steps.md), step 2, or [S5](../setup/S5-connect-the-approval-runner.md), step 3). Note the assignment's ID.
2. **Instruct the change.** The configuration admin, through Claude Code: `propose_configuration_change` with `validateOnly` first, to see the diff, then without it to store the draft, then `submit_record` with subject kind configuration draft. The draft upserts one `workload-grant` row: `principalObjectId`, `workloadRoleKey`, `reason`, `usesFederatedCredential`, `bindingDetail` (the assignment ID) and `expiresOn`.
3. **Accept on the decision page.** A configuration admin accepts the engine's diff. In Balanced, Starter and the CoE baseline the instructing admin may accept their own AI's draft; Regulated and `strict-separation` need a second configuration admin. WORKING DEFAULT (open design question 4)
4. **Activation.** The accepted decision activates a new version; the engine stamps the deciding decision on the grant (`grantedByDecisionId`).
5. **Check.** Run `check_role_bindings`: the grant shows `assignmentFound` true and `isExpired` false.

At install the first grants arrive in the client binding patch with the role bindings (guide [S1](../setup/S1-pick-presets-and-overlays.md), step 8, not built yet).

## Part B. Check

Run `check_role_bindings` as a platform admin at least monthly, and after any change to a workload's identity. It reads Microsoft Graph as you (guide [S7](../setup/S7-turn-on-directory-reads.md) turns the reads on) and answers the bindings, the grants, the direct assignments and a list of findings:

| Finding | What it means | What to do |
|---|---|---|
| "The Workload Grant `<key>` has expired." | The grant admits nothing any more; the workload is refused | Renew (part C) if the workload still runs, or retire (part D) |
| "The Workload Grant `<key>` names an agent identity, which never holds a grant." | A grant that should never have been accepted | Retire it now, and find out how it was accepted |
| "The Workload Grant `<key>` has no matching app role assignment in Entra." | The assignment was removed, never made, or points at another principal | Make the assignment if the grant is meant, or retire the grant |
| "`<principal>` holds `<role>` directly with no current Workload Grant." | A service principal holds a workload app role without a grant, or a person holds an app role directly instead of through a group | Remove the assignment (`Remove-MgServicePrincipalAppRoleAssignedTo`), or record a grant if a workload needs it. A person's direct assignment is always removed |

The tool lists grants by key. To see renewals coming, read the `expiresOn` of each grant in the answer and sort by date (open point 1).

## Part C. Renew

1. **Thirty days before Expires On**, the workload's owner asks for a renewal with the reason still true.
2. The configuration admin instructs a change that upserts the same grant key with a new `expiresOn` (no more than `workload-grant-max-days` ahead) and, if the credential changed, a new `bindingDetail` or `usesFederatedCredential`.
3. A configuration admin accepts it on the decision page, as in part A. WORKING DEFAULT (open design question 4)
4. `check_role_bindings` shows the new date.

If a grant lapses first: a pipeline's record step fails soft, so deployments go on and the missing records show as flags later; the approval runner is refused on every route, so its notices stop going out and answers given in its cards are not recorded. Answers on the decision page are still recorded. Requests still waiting in the outbox are claimed again once the grant is renewed; a request that was in flight when the grant lapsed may have used up an attempt or become failed (guide [A4](A4-approval-runner-operations.md)).

## Part D. Retire

For a planned end (a pipeline removed, a runner replaced):

1. The configuration admin instructs a change with a `retire` operation on the grant's key, and a configuration admin accepts it. From activation, the engine refuses the principal.
2. The Entra admin removes the app role assignment.
3. The workload's owner removes the federated credential or deletes the identity, if nothing else uses it.
4. `check_role_bindings` shows neither the grant nor the assignment.

For an urgent cut, reverse the order: stop the identity in Entra first, then retire the grant (guide A2, part A).

## Checks

- Every current grant has a reason a reviewer can read, a deciding decision, and an Expires On no more than `workload-grant-max-days` ahead.
- `check_role_bindings` has no finding, or each finding has an owner and a date.
- No person holds a product app role directly; every person's role comes from a bound group.
- No grant names an agent identity or an agent user.

## Open points

1. **The designed grant check lists every grant by Expires On, soonest first.** `check_role_bindings` lists them by key and flags only expired ones. Proposal: order the grants by Expires On and add a finding for each grant that expires within a set number of days.
2. **Credential-bearing grants are not flagged.** The schema says `usesFederatedCredential` false is flagged, and the approval runner's template grant is false (it signs in with a certificate). Neither `check_role_bindings` nor `get_configuration_health` reads the column yet. Proposal: a `check_role_bindings` finding for each such grant.
3. **No reminder before a grant lapses.** The daily due-state refresh is built since this draft, but it has no item for workload grants, so part C relies on people and on the check above.
4. **Principal References for workloads.** Outside the local seed, the product writes no Principal Reference row for a grant's principal, so the principal's kind comes from the token alone. Proposal: write one when a grant activates.
5. **Two more workload roles are proposed:** `authorization-reporter` and `consistency-checker`, marked as proposals in the Balanced preset's `role.json`. No route uses them yet.

## Microsoft facts used

| Fact | Source | Checked |
|---|---|---|
| "Currently, if you add a service principal to a group, and then assign an app role to that group, Microsoft Entra ID doesn't add the `roles` claim to tokens it issues."; "App roles can also be assigned to the service principal for another application, or to the service principal for a managed identity." | https://learn.microsoft.com/entra/identity-platform/howto-add-app-roles-in-apps | 2026-11-23 |
| `Remove-MgServicePrincipalAppRoleAssignedTo` deletes an app role assignment that a user, group or client service principal holds for a resource service principal | https://learn.microsoft.com/powershell/module/microsoft.graph.applications/remove-mgserviceprincipalapproleassignedto | 2026-11-23 |
