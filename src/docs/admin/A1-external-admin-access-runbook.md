# Guide A1. External configuration admin runbook: time-bound access to the adopter's tenant

- **Status:** 0.2 draft, 2026-11-23. Nothing here was run against a tenant.
- **Guide set:** setup guides S1 to S7 and admin guides A1 to A4. Index: [README](../README.md).
- **See also:** [admin.md](../admin.md#roles-and-access), [concepts.md](../concepts.md#product-roles-and-teams).
- **Open design questions that touch this guide:** 1, 4 and 12 (markers below).

## Purpose

An adopter may ask a person from outside its own organization, for example a contractor or another company's implementation team, to apply its configuration: the preset workshop, the client binding patch, documentation rules and later changes. This guide calls that person the external admin. The external admin needs the configuration admin role in the adopter's product for hours at a time, never standing access, and the adopter must approve and see every activation.

The working default: the external admin is a guest in the adopter's tenant, an eligible member of a group that is PIM-eligible for the configuration admin role. Each activation needs the adopter's approval and is time-bound. For adopters without Entra ID P2 or ID Governance, the adopter's own staff run the tools while the external admin advises.

## Who

| Who | Part |
|---|---|
| The adopter's Entra admin | Invites the guest; sets up PIM for Groups on the role group. Needs User Administrator or Guest Inviter to invite; Privileged Role Administrator (role-assignable group) or Groups Administrator or Identity Governance Administrator (other groups), or an active group owner, to manage the PIM policy |
| The adopter's approvers (at least two, for example the platform admin and a deputy) | Approve each activation in PIM |
| The adopter's configuration admin | Stays accountable for every configuration decision. Accepts the drafts the external admin instructs (step B5) |
| The external admin | Requests activation, instructs configuration drafts through Claude Code, never accepts their own work where the adopter can do so |

## Prerequisites

- The product is installed and version 1 is active (guide [S1](../setup/S1-pick-presets-and-overlays.md)). If the binding patch is not applied yet, the adopter's own admin does the first steps until the role group is bound.
- Entra ID P2 or Microsoft Entra ID Governance coverage in the adopter's directory for every eligible member (the guest included) and every approver. How a guest's coverage is counted is not covered here (UNVERIFIED; the adopter's license owner confirms). Without P2 or ID Governance, use the fallback (part D).
- B2B collaboration allowed for the external admin's home organization (the adopter's external collaboration and cross-tenant access settings).
- A Conditional Access policy that requires MFA for guests (Microsoft's Zero Trust starting point for guest access).
- A written agreement that names: the roles in scope (configuration admin; platform admin only if a task needs it), the end date, the maximum activation length (this runbook uses 8 hours), the approvers, and that the client binding patch and every adopter fact stay in the adopter's own repository.
- [branch: Dataverse]
  - Guest access to Dataverse is allowed in the product's environment. Learn: by default guest access to Dataverse is restricted for all new environments (`restrictGuestUserAccess`), and an admin must allow it per environment.
  - The guest has a license assigned in the adopter's tenant. Learn's user page says a license from the guest's own tenant is not considered valid (open point 3).
  - The Dataverse group team for the configuration admin role has the membership type "Members and guests" (or a separate "Guests" team carries the same security role). "Members" excludes guests, and the membership type cannot be changed after the team is created (open point 1).
  - If the environment is locked to a security group, the role group is a member of that security group.
- [branch: Azure]
  - The role group is assigned to the product API's app role for the configuration admin (group-based assignment needs Entra ID P1 or P2; P2 covers it).
  - If the product's enterprise app has "assignment required" on, the role group is assigned (it is, through the app role).

## Steps

### A. Before the work starts (the adopter)

1. **Invite the external admin as a guest.** One named guest account per person, never a shared account. The inviter becomes the guest's sponsor.
2. **Allow guests on the role, by configuration.** The configuration admin role row's Allows Guests column is No by default for every role. The adopter's configuration admin changes it to Yes on this role only, through a configuration change decision accepted on the decision page. This is a protected change, judged by the active rules. Without it, the designed guest check flags the guest.
3. **Put the role group under PIM for Groups.** In the Microsoft Entra admin center: ID Governance, Privileged Identity Management, Groups, Discover groups, then manage the configuration admin role group (for example `<product>-role-configuration-admin`, the naming guide [S4](../setup/S4-prepare-entra-and-deploy.md) suggests).
4. **Set the member role settings** (Settings, Member):
   - Activation maximum duration: 8 hours (Learn allows 1 to 24; see open point 4).
   - On activation, require Azure MFA, or a Conditional Access authentication context.
   - Require justification on activation; optionally require ticket information (the reference of the agreed work).
   - Require approval to activate, with at least two approvers from the adopter. There are no default approvers.
   - Eligible assignments expire: no permanent eligible assignment for guests.
   - Notifications to the adopter's platform admin.
5. **Make the guest an eligible member** of the group, with an end date equal to the agreed end date. Not an active member.
6. **Platform admin access, only if needed.** Repeat steps 2 to 5 for the platform admin role group only for tasks that need it (for example running the gap check during the claim drive). Default: not granted.

### B. Each working session

1. **Request activation.** The external admin activates the eligible membership in PIM (the Entra admin center, or the My Access portal under Roles, Groups, which is in preview), signed in to the adopter's tenant, with justification and the work reference.
2. **Approve.** An adopter approver approves in PIM. The activation starts the clock.
3. **Pick up the new membership.**
   - [branch: Dataverse] Dataverse caches a user's group memberships at sign-in. The external admin signs out of every session against the environment and signs in again after activation, so the group team role applies.
   - [branch: Azure] The product reads the person's group IDs from the token and resolves the role from the bound groups. The external admin re-authenticates the server in Claude Code (`/mcp`, Re-authenticate), so the new token carries the activated group. The API's groups claim includes only groups assigned to the application in which the person is a direct member, so the PIM-managed group must be the bound role group itself, not a group nested inside it.
4. **Connect and work.** Claude Code, signed in with the guest account in the adopter's tenant (guide [S2](../setup/S2-connect-claude-code.md)). The external admin instructs configuration drafts: `get_configuration` and `explain_rules` to read, `propose_configuration_change` with `validateOnly` to see the diff and the impact on live applications, then without it to store the draft, then `submit_record` (subject kind configuration draft). The record shows "AI client for a person", on behalf of the guest.
5. **Acceptance.** The engine's diff goes to the configuration admin role as one approval request, answered on the decision page. The engine allows the instructing admin to accept their own AI's draft in Balanced, Starter and the CoE baseline; Regulated and `strict-separation` require a second configuration admin. WORKING DEFAULT (open design question 4). Runbook rule: the adopter's configuration admin accepts every draft the external admin instructed, so the decider on record is the adopter's own person. The external admin does not answer those requests.
6. **End the session.** The external admin deactivates the membership in PIM before it expires, and clears the server's authentication in Claude Code (`/mcp`, Clear authentication, or `claude mcp logout <server-name>`).
   - [branch: Dataverse] A removed membership reaches Dataverse only at the external admin's next sign-in or after 8 hours of continuous sign-in. Signing out of every session is part of ending the session.
   - [branch: Azure] A token already issued keeps its groups until it expires (60 to 90 minutes by default). The server does not honor continuous access evaluation in this release (guide [A2](A2-urgent-revoke-and-break-glass.md), open point 1).

### C. When the work ends (the adopter)

1. **Remove the eligible assignment** (or confirm it expired on the end date).
2. **Turn Allows Guests back to No** on the configuration admin role, through a configuration change decision.
3. **Disable the guest account and revoke its sessions** in Entra, or delete it if no further work is planned. On Dataverse, continuous access evaluation acts on disable and session revocation in near real time.
4. **Run `check_role_bindings`** as the platform admin: no role is assigned to a person directly. The guest check (a guest holding a role that does not allow guests) is designed but not in `check_role_bindings` yet, so also look at the role group's members in Entra for any guest (guide [S7](../setup/S7-turn-on-directory-reads.md), open point 3).
5. **Keep the evidence.** Export the PIM audit history for the work period and link it as evidence (`save_link`, evidence category) to the configuration change decisions of that period.
6. **Hand over.** The client binding patch and any configuration files stay in the adopter's private repository. The external admin keeps no copy of adopter facts.

### D. Fallback for adopters without Entra ID P2 or ID Governance

1. The external admin holds no role and no guest account in the product.
2. The adopter's configuration admin runs Claude Code and is the instructing admin; the external admin advises in a shared session.
3. Where useful, the external admin prepares a patch file in the seed format (as the preset patches use; [`config/presets/README.md`](../../config/presets/README.md)); the adopter's admin submits it through `propose_configuration_change` with `fileContent`, so the adopter is the instructing admin on record.
4. Acceptance follows part B, step 5, with the adopter's people only.
5. Later, an adopter that adds P2 can move to parts A to C.

### What the external admin never does

- Answer an approval request for an adopter decision, or hold any role through a direct assignment.
- Write configuration rows directly (no person, role or tool can; drafts only).
- Keep a binding patch, group object IDs or other adopter facts outside the adopter's repository.
- Change the keys of fixed engine meanings, such as `configuration-admin`; labels are the adopter's to rename. WORKING DEFAULT (open design question 1)
- Assign a workload role to a pipeline in Entra. The adopter's admin assigns it, and the binding patch records it as a Workload Grant with a reason, the deciding decision and an expiry. WORKING DEFAULT (open design question 12)

## Checks

- Outside a session, PIM shows the guest as Eligible, never Active, on the role group.
- A test activation without approval does not complete.
- No role marked Requires Just In Time has a group with permanent members, where the adopter marked the role (see guide S1, open point 3). The check is designed, not built: check the group's PIM assignments by hand.
- No guest holds a role whose row says Allows Guests = No. The check is designed, not built: check the role groups' members by hand.
- Every configuration change decision of the work period shows the guest as instructing admin (performed by "AI client for a person") and an adopter's person as decider.
- After part C: the guest is disabled or deleted, no eligible assignment remains, Allows Guests is No again, and `check_role_bindings` is clean.

## Open points

1. **Dataverse group team membership type.** Guests are included only by the "Members and guests" or "Guests" types, and the type cannot change after creation. The Role Group Binding's platform detail and the group team setup script must choose it when the team is created; the data model and the security design do not mention it yet.
2. **The Dataverse cache stretches every PIM window.** An activation reaches Dataverse at the next sign-in, and its end is honored at the next sign-in or after up to 8 hours. The runbook relies on sign-out at session end and on disabling the guest when the work ends. A test in a real tenant could also time this for a PIM-managed group.
3. **Guest license tenant.** Learn's "Create users" page says a guest needs a license from the environment's tenant; Learn's "Share a model-driven app" page says the license for a custom model-driven app may be in either the home or the hosting tenant. The two pages disagree. MCP-only use is UNVERIFIED either way.
4. **PIM activation maximum.** The PIM for Groups settings page allows 1 to 24 hours; the Graph PIM for Groups API overview says activation is always time-bound for a maximum of eight hours. This runbook uses 8 hours, which fits both.
5. **Who accepts.** The rule in part B, step 5 (the adopter accepts the external admin's drafts) is runbook guidance. The engine enforces it only under Regulated or `strict-separation`. A separation rule scoped to guest instructing admins would need a new kind of separation rule.

## Microsoft facts used

| Fact | Source | Checked |
|---|---|---|
| PIM for Groups: just-in-time membership or ownership; approval, MFA, justification and maximum activation time per group policy | https://learn.microsoft.com/entra/id-governance/privileged-identity-management/concept-pim-for-groups | 2026-11-22 |
| Member role settings: activation maximum 1 to 24 hours; MFA or Conditional Access authentication context; justification; ticket information; approval with no default approvers, at least two advised; eligible and active assignment durations; roles that can manage the policy | https://learn.microsoft.com/entra/id-governance/privileged-identity-management/groups-role-settings | 2026-11-22 |
| PIM for Groups APIs: "The activation is always time-bound for a maximum of eight hours" | https://learn.microsoft.com/graph/api/resources/privilegedidentitymanagement-for-groups-api-overview?view=graph-rest-1.0 | 2026-11-22 |
| "Ensure that your directory has Microsoft Entra ID P2 or Microsoft Entra ID Governance licenses for the following categories of users:", among them "Users with eligible and/or time-bound assignments as members or owners of PIM for Groups" and "Users able to approve or reject activation requests in PIM" | https://learn.microsoft.com/entra/id-governance/licensing-fundamentals | 2026-11-23 |
| "Every user who is eligible for membership in or ownership of a PIM for Groups must have a Microsoft Entra ID P2 or Microsoft Entra ID Governance license."; an assignment "Can't be assigned for a duration of less than five minutes"; membership of a role-assignable group needs a role "such as Privileged Role Administrator or Global Administrator, or be an active owner of the group", and of other groups a role "such as Groups Administrator or Identity Governance Administrator, or be an active owner of the group" | https://learn.microsoft.com/entra/id-governance/privileged-identity-management/groups-assign-member-owner | 2026-11-23 |
| "Group membership and ownership activation through the My Access portal is in preview."; in the Entra admin center, "**ID Governance** > **Privileged Identity Management** > **My roles** > **Groups**"; "In some situations, application access might not immediately reflect the fact that user was added to the group or removed from it." | https://learn.microsoft.com/entra/id-governance/privileged-identity-management/groups-activate-roles | 2026-11-23 |
| Guest and member user types; the inviter becomes the guest's sponsor | https://learn.microsoft.com/entra/external-id/user-properties | 2026-11-22 |
| Require MFA for guests and external users as the starting point | https://learn.microsoft.com/security/zero-trust/zero-trust-identity-device-access-policies-guest-access | 2026-11-22 |
| Dataverse group teams: membership types Members and guests, Members, Owners, Guests; the type cannot change after creation; memberships cached at sign-in and refreshed at next sign-in or after 8 hours of continuous sign-in; a group team needs the environment's security group membership | https://learn.microsoft.com/power-platform/admin/manage-group-teams | 2026-11-22 |
| Guest access to Dataverse is restricted by default in new environments (`restrictGuestUserAccess`) | https://learn.microsoft.com/power-platform/admin/security/guest-access | 2026-11-22 |
| "Guest users should also have a license from the environment's tenant" | https://learn.microsoft.com/power-platform/admin/create-users | 2026-11-22 |
| Custom model-driven apps: the license may be in the user's home tenant or the hosting tenant | https://learn.microsoft.com/power-apps/maker/model-driven-apps/share-model-driven-app | 2026-11-22 |
| Continuous access evaluation for Dataverse: generally available in public cloud; acts on account disable, password reset and session revocation | https://learn.microsoft.com/power-platform/admin/continuous-access-evaluation | 2026-11-22 |
| "The default lifetime of an access token is variable. When issued, the Microsoft identity platform assigns a random value ranging between 60-90 minutes (75 minutes on average) as the default lifetime of an access token." | https://learn.microsoft.com/entra/identity-platform/access-tokens | 2026-11-23 |
