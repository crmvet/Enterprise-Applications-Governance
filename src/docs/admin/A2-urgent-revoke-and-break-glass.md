# Guide A2. Urgent revoke and break-glass accounts

- **Status:** 0.2 draft, 2026-11-23. Nothing here was run against a tenant.
- **Guide set:** setup guides S1 to S7 and admin guides A1 to A4. Index: [README](../README.md).
- **Design sources:** document 26, sections 3.4, 4.3, 4.4, 4.5 (CHK-07, CHK-09, CHK-10, CHK-10b), 9.2 and 11; document 25, section 11.5; document 28, Q16 (working default, settled).
- **Open design questions that touch this guide:** 1 and 12 (markers below).

## Purpose

Two procedures an adopter's admins must have ready before go-live:

1. **Urgent revoke.** Cut a person or a workload off now. Removing someone from a bound Entra group is not fast enough: Dataverse caches group memberships until the next sign-in or for up to 8 hours, and on Azure an issued token keeps its roles until it expires (document 26, sections 4.3 and 4.4).
2. **Break-glass accounts.** No design stops a platform's top administrator. On Dataverse a system administrator can send the bypass header and write past the engine; on Azure a database owner can write past the API. The product detects such writes; the guidance is to keep those top roles for break-glass accounts managed in Entra (working default, 28, Q16; document 25, section 11.5).

## Who

| Who | Part |
|---|---|
| The adopter's Entra admin | Disables users and revokes sessions: User Administrator for non-admin users, Privileged Authentication Administrator for admin accounts |
| The adopter's application admin | Disables a workload's service principal: Cloud Application Administrator, Application Administrator or the service principal's owner |
| [branch: Dataverse] An environment admin | Deactivates an application user in the Power Platform admin center |
| [branch: Azure] The data tier's owner | Removes database and Azure role assignments; ends open database sessions |
| The product's platform admin | Records the decision, moves ownership to the deputy, runs the checks |
| The reviewer named in the decision | Decides whether and when access returns |
| Security operations | Owns break-glass monitoring and the drills |

## Prerequisites

- Every owner role and every approving role has a deputy, so work can move before access goes (document 26, RR-07 and I-59: name the successor first, then remove access).
- The adopter knows, per binding, how long a removal takes (table below), and has decided who may run each step out of hours.
- At least two cloud-only emergency access accounts exist in Entra, as Microsoft describes (part B).

| Binding | A group removal takes effect | Disable plus session revocation takes effect |
|---|---|---|
| [branch: Dataverse] | At the next sign-in, or after up to 8 hours of continuous sign-in | Near real time: Dataverse supports continuous access evaluation, generally available in public cloud |
| [branch: Azure] | When the person's access token expires: 60 to 90 minutes by default | The same: the product's server does not honor continuous access evaluation in this release (open point 1) |

## Part A. Urgent revoke

### Steps for a person

1. **Disable the account and revoke its sessions.** Microsoft Entra admin center: Entra ID, Users, select the user, clear Account enabled, Save, then Revoke sessions on the user's Overview page. For a hybrid account, disable it in on-premises Active Directory first.
   - [branch: Dataverse] Continuous access evaluation ends Dataverse sessions in near real time. The product's MCP server can no longer get a new on-behalf-of token for the person. A Dataverse token the server already cached for the person may still be presented; whether Dataverse rejects it through continuous access evaluation depends on the server asking for CAE-capable tokens (UNVERIFIED, open point 1).
   - [branch: Azure] The person's access token to the product's server stays valid until it expires (60 to 90 minutes by default), and the API runs as the caller from that token. Treat the token lifetime as the worst case. The directory reads keep the downstream token they got for the person in an in-memory cache until it expires, but use it only inside a call that carries a valid incoming token, so the cache adds nothing to that worst case.
2. **Remove the person from the bound groups.** This keeps them out when the account is re-enabled for other reasons.
3. **Record the decision.** A decision of type exception or ownership with the reason. Document 26, section 9.2 designs an "urgent revoke recorded" product event in the audit trail; it is not built yet, so this decision is the product's record of the revoke.
4. **Move their work.** Record the leave with `record_owner_leave` (stored as reported; the engine opens an owner check review for each application the person owns), and the successor with `propose_owner_change`, an ownership decision that changes nothing until it is accepted. Then no application is left without an owner, and the deputy is a current holder. Approval cards cannot be reassigned: the runner sends them with reassignment off, on purpose (guide [A4](A4-approval-runner-operations.md)). What happens to the person's open cards:
   - A card for a product role went to every user member of the bound group, so the other members can still answer it.
   - A card for an owner role asked the owners of that application. It waits until its Expires On; the engine then expires it and opens a restart that asks the current owners, so the deputy gets the next card. If the rule has no timeout, ask the requester to withdraw and submit again (guide A4, "A request failed").
5. **Re-enable only after the reviewer decides** (I-91). Re-enabling is a new decision, not an undo.

### Steps for a workload (a pipeline, the approval runner, the consistency check)

1. **Stop it from getting tokens.** Disable the service principal (Enterprise applications, Properties, "Enabled for users to sign-in?" No, which sets `accountEnabled` to false and stops tokens for it), or remove its federated identity credential.
   - [branch: Dataverse] Also deactivate its application user in the Power Platform admin center (Environments, Settings, Users + permissions, Application users, Deactivate). Deactivating breaks every integration that uses that application user, which is the point.
   - [branch: Azure] Also remove its app role assignment on the product API (`Deployment.Record` or `Approval.Write`; guide [A3](A3-workload-grants.md), part D). For the approval runner, also turn its flows off, the parent first. Microsoft notes that Entra revocation does not end an already open SQL connection; the product's workloads reach the database only through the API's managed identity, so no product workload holds its own SQL session. If an attacker reached the data tier itself, block the identity before ending its sessions, or it reconnects.
2. **End the workload grant.** Shorten or retire its Workload Grant through a configuration draft and an accepted decision. From Expires On the grant admits no app-only token, and CHK-09 flags it. WORKING DEFAULT (open design question 12)
3. **Record the decision** with the reason, as for a person.
4. **Expect soft failures.** A pipeline's record step fails soft: deployments continue, and the missing records show as flags later. The approval runner stops writing outcomes, so open cards wait and new requests stay in the approval outbox; a request in flight may fail and need a withdraw and a new submission (guide [A4](A4-approval-runner-operations.md)). Name the restart owner.

### Checks

- Entra sign-in logs show failed sign-ins for the account or the service principal after the revoke.
- `check_role_bindings`: no binding, direct assignment or workload grant still covers the principal (CHK-01, CHK-02, CHK-09).
- The owner check (CHK-07) shows the departed person on no tier 3 application without a deputy.
- After the owner change is accepted and an owner role card restarts, the deputy's `get_my_queue` lists the approval that asks them.
- [branch: Dataverse] The user is disabled in the environment, or the application user shows inactive.
- [branch: Azure] The server's telemetry (with the telemetry salt set, it logs a salted hash of the caller's object ID, never the token) shows no successful call for the person after the token lifetime.

## Part B. Break-glass accounts

### Steps

1. **Create the emergency access accounts in Entra,** as Microsoft describes: at least two, cloud-only (not federated), on the `*.onmicrosoft.com` domain, not assigned to one person, with a permanent Global Administrator assignment (not PIM-eligible), strong phishing-resistant authentication not tied to a personal device, and at least one of them excluded from every Conditional Access policy, so a policy mistake cannot lock everyone out.
2. **Alert on every use.** A high-priority alert on each sign-in, password change, role or permission change, and credential or method change for these accounts. Treat any use as a security incident unless it was approved in advance.
3. **Keep the top data-store roles for break-glass only.**
   - [branch: Dataverse] In the product's environment, the System Administrator role is held only by the break-glass accounts. `prvBypassCustomBusinessLogic` stays where Microsoft puts it by default, on System Administrator only; never add it to another role. Global, Power Platform and Dynamics 365 admins are no longer System Administrator automatically: they self-elevate when needed, each elevation is logged in Microsoft Purview, and with PIM the System Administrator role is removed when the PIM assignment expires. So make the Power Platform Administrator role PIM-eligible, not permanent. CHK-10 counts System Administrator holders and any other role with the bypass privilege.
   - [branch: Azure] Turn on Microsoft Entra-only authentication for the database server (SQL authentication off; the generated `CloudSA` login is not a break-glass path). Set the server's Microsoft Entra admin to a group whose members are only the break-glass accounts. Keep the Azure Owner role on the product's resources to the break-glass accounts plus PIM-eligible owners. The API's managed identity stays the only principal with general write permission on the product's tables. CHK-10b counts database owner and Azure Owner holders; it is designed but not built, so count them by hand. Migrations need the SQL administrator, so decide who runs them once that group is break-glass only (guide [S4](../setup/S4-prepare-entra-and-deploy.md), open point 3).
4. **Write the break-glass runbook** next to this guide: who may retrieve the credentials, how many people it takes, who is told, and how each use is justified and recorded.
5. **After any use,** run the consistency check (designed, not built yet; until it is, review the SQL security audit events in Log Analytics for the period of use, which show sign-ins and schema and permission changes but not statement text): it finds product rows written after their Engine Checked On stamp and records each as an audit entry "write outside the engine". It runs under a read-only workload identity (the proposed `consistency-checker` role). WORKING DEFAULT (open design question 12). Then run `check_role_bindings` and record a decision with the reason for the use.
6. **Drill at least every 90 days,** and after staff or license changes: confirm the accounts sign in, the alerts fire, the list of authorized people is current, and the MFA device is reachable.

### Checks

- CHK-10 (Dataverse) or CHK-10b (Azure) lists exactly the break-glass accounts, plus time-bound elevations that match PIM activations.
- No role other than System Administrator holds `prvBypassCustomBusinessLogic` [branch: Dataverse].
- A drill sign-in raised the alert within the agreed time.
- The consistency check after a drill finds no unexplained "write outside the engine".
- The product's roles never include System Administrator or database owner; product roles come only through bound groups. The actor kinds and principal kinds the checks read are a fixed engine list. WORKING DEFAULT (open design question 1)

## Open points

1. **Revocation through the product's server.** The incoming token lasts 60 to 90 minutes by default (document 26's SM18b says one hour; Learn now says variable), and the Azure server, as built, validates its signature and lifetime but does not honor continuous access evaluation. Its on-behalf-of cache keeps downstream tokens only for use inside calls that carry a valid incoming token. Proposal for documents 24 and 26: have the server request CAE-capable tokens and handle claim challenges, or state the token lifetime as the revoke window. A test in a real tenant should time a revoke through the server, not only on the store.
2. **The consistency check identity** is the proposed `consistency-checker` workload role. It needs the product owner's yes and rests on open design question 12.
3. **No "urgent revoke recorded" event yet.** Document 26, section 9.2 designs it as a product event in the audit trail; the product does not write it in this release, so part A relies on the decision record (step 3). Proposal: the engine writes the event when a decision records an urgent revoke.
4. **Break-glass accounts and Dataverse licenses.** Learn says unlicensed Entra admins are added to Dataverse as a "Setup user" with administrative access only. Whether that is enough for a break-glass repair in the product's environment (for example a data fix under the bypass header) is not checked here.

## Microsoft facts used

| Fact | Source | Checked |
|---|---|---|
| Revoke access: clear Account enabled, then Revoke sessions; roles needed (User Administrator, Privileged Authentication Administrator); applications using access tokens lose access when the token expires; continuous access evaluation for capable apps | https://learn.microsoft.com/entra/identity/users/users-revoke-access | 2026-11-22 |
| Continuous access evaluation for Dataverse: generally available in public cloud; acts on disable or delete, password change or reset, session revocation; only Dataverse among Power Platform services | https://learn.microsoft.com/power-platform/admin/continuous-access-evaluation | 2026-11-22 |
| "The default lifetime of an access token is variable. When issued, the Microsoft identity platform assigns a random value ranging between 60-90 minutes (75 minutes on average) as the default lifetime of an access token." | https://learn.microsoft.com/entra/identity-platform/access-tokens | 2026-11-23 |
| Dataverse group team memberships cached at sign-in, refreshed at next sign-in or after 8 hours of continuous sign-in | https://learn.microsoft.com/power-platform/admin/manage-group-teams | 2026-11-22 |
| Disable a service principal: "Enabled for users to sign-in?" No sets `accountEnabled` false and stops tokens; roles Cloud Application Administrator, Application Administrator or owner | https://learn.microsoft.com/entra/identity/enterprise-apps/disable-user-sign-in-portal | 2026-11-22 |
| Risky workload identities can be disabled to block further sign-ins; remove compromised credentials | https://learn.microsoft.com/entra/id-protection/concept-workload-identity-risk | 2026-11-22 |
| Activate or deactivate a Dataverse application user in the Power Platform admin center | https://learn.microsoft.com/power-platform/admin/manage-application-users | 2026-11-22 |
| Revoking Entra sessions does not end open SQL connections or workload identity access; block the identity before killing its sessions | https://learn.microsoft.com/azure/azure-sql/database/ransomware-recovery-contain | 2026-11-22 |
| Emergency access accounts: two or more, cloud-only, Global Administrator, for break-glass only; validate at least every 90 days; exclude from Conditional Access; alert on use | https://learn.microsoft.com/entra/identity/role-based-access-control/security-emergency-access | 2026-11-22 |
| Emergency accounts on `*.onmicrosoft.com`, permanent (not PIM-eligible) Global Administrator, phishing-resistant authentication, not tied to a personal phone | https://learn.microsoft.com/security/zero-trust/adopt/implement-privileged-access-identity | 2026-11-22 |
| High-priority alert on every sign-in, password change, role change and credential change of an emergency account | https://learn.microsoft.com/entra/architecture/security-operations-privileged-accounts | 2026-11-22 |
| `BypassBusinessLogicExecution` needs `prvBypassCustomBusinessLogic`, by default only on System Administrator; it can be added to other roles only through the API | https://learn.microsoft.com/power-apps/developer/data-platform/bypass-custom-business-logic | 2026-11-22 |
| Admins are no longer System Administrator automatically; they self-elevate; every elevation is logged to Microsoft Purview; with PIM the role is removed when the PIM assignment expires | https://learn.microsoft.com/power-platform/admin/manage-high-privileged-admin-roles | 2026-11-22 |
| Unlicensed Entra admins are enabled in Dataverse as "Setup user" with administrative access only | https://learn.microsoft.com/power-platform/admin/create-users | 2026-11-22 |
| Entra-only authentication disables SQL authentication; the generated `CloudSA` login is not a fallback or break-glass path | https://learn.microsoft.com/azure/azure-sql/database/authentication-azure-ad-only-authentication | 2026-11-22 |
| The server's Microsoft Entra admin can be a group; admin accounts are `db_owner` in every user database | https://learn.microsoft.com/azure/azure-sql/database/authentication-aad-overview | 2026-11-22 |
