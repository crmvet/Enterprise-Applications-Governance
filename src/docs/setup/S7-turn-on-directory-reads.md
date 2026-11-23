# Guide S7. Turn on the directory reads

- **Status:** 0.2 draft, 2026-11-23. Nothing here was run against a tenant. The readers are tested over recorded, fictional Microsoft Graph and Power Platform responses and a fake token endpoint.
- **Guide set:** setup guides S1 to S7 and admin guides A1 to A4. Index: [README](../README.md).
- **Design sources:** document 23, P06 and P08; document 24, sections 5 and 10, spike 14; document 26, sections 4.5 and 8; document 28, Q18; D-15; `src/adapters/azure/README.md`, "Directory reads on behalf of the signed-in admin"; `src/adapters/azure/infra/README.md`, section 5.
- **Open design questions that touch this guide:** none.

## Purpose

Four tools read live Microsoft services as the person who asks, never as the product:

| Tool | Reads | Default product role (Tool Policy) |
|---|---|---|
| `get_role_holders` | Microsoft Graph: the members of the groups bound to a role | Any role |
| `run_owner_check` | Microsoft Graph: the account state of owners, deputies and approvers (CHK-07; working default Q18) | `platform-admin` |
| `check_role_bindings` | Microsoft Graph: the bound groups and the API's app role assignments, against the Role Group Binding and Workload Grant rows | `platform-admin` |
| `run_gap_check` | The Power Platform inventory, compared with the register (P06) | `platform-admin`, plus an Entra role that can see the inventory |

They are off by default and answer `upstream_unavailable` until this guide turns them on. The product keeps no copy of Microsoft's data (D-15) and runs no unattended job: each read happens when a person asks.

## How the token moves

The service never forwards the person's token. It sends it only to the Microsoft Entra token endpoint in the on-behalf-of exchange, which issues a new token for Microsoft Graph (`https://graph.microsoft.com/.default`) or the Power Platform API (`https://api.powerplatform.com/.default`), and only that new token goes to those two hosts. No token is logged. App-only callers, the approval runner and agents never reach the readers.

The API app proves itself in that exchange with a federated identity credential that trusts a user-assigned managed identity of the function app: no client secret. Outside Development the service refuses to start if any client secret is configured.

## Who

| Who | Part |
|---|---|
| The client's Entra admin | Adds the federated credential and the delegated permissions to the API app registration, grants admin consent |
| The client's Azure team | Sets the switch and provisions again |
| The client's platform admin | Runs the four tools, and for `run_gap_check` holds an Entra role that can see the Power Platform inventory |

## Prerequisites

- The product is deployed (guide [S4](S4-prepare-entra-and-deploy.md)) and a configuration version is in force (guide [S1](S1-pick-presets-and-overlays.md)).
- A role that can grant tenant-wide admin consent for Microsoft Graph delegated permissions.

## Steps

1. **Switch the reads to live and provision.** From `src/adapters/azure`:

   ```
   azd env set PRODUCT_DIRECTORY_READS Live
   azd provision
   ```

   The template adds the user-assigned managed identity `id-directory-<token>` to the function app and writes `Product:Directory:Mode` and the identity's client ID to the app settings.

2. **Add the federated credential** on the API app registration. `azd env get-values` prints `PRODUCT_FEDERATED_CREDENTIAL_ISSUER`, `PRODUCT_FEDERATED_CREDENTIAL_SUBJECT` (the managed identity's object ID) and `PRODUCT_FEDERATED_CREDENTIAL_AUDIENCE` (`api://AzureADTokenExchange`). In the Microsoft Entra admin center: the API app registration, Certificates & secrets, Federated credentials, Add credential, scenario "Managed Identity", and select that identity. Only a user-assigned managed identity can serve as this credential, which is why the template adds one.

3. **Add the delegated permissions** on the API app registration and grant admin consent for the tenant, so no person sees a consent prompt:
   - Microsoft Graph: `GroupMember.Read.All`, `User.Read.All`, `User-LifeCycleInfo.Read.All`, `Application.Read.All`.
   - Power Platform API: `ResourceQuery.Resources.Read` ("Query resources").

   Whether a smaller set is enough is not verified yet (document 24, spike 14).

4. **Give the gap check's runners an inventory role.** `run_gap_check` also needs the person to hold one of the Microsoft Entra roles that can see the whole Power Platform inventory: Global Administrator, Power Platform Administrator, Dynamics 365 Administrator or Global Reader. The AI Administrator and AI Reader roles see only agents and a few related kinds, so the gap check would miss apps and flows. Microsoft documents that the inventory API supports only delegated user authentication and answers a service principal or managed identity with 403. Without such a role, the tool says so in plain words.

5. **Run each tool once** as a platform admin through Claude Code (guide [S2](S2-connect-claude-code.md)) and compare with the Checks below.

## Checks

- `get_role_holders` for a bound role lists the group's members as the person sees them in Entra.
- `check_role_bindings` reports every binding's group as existing, every Workload Grant with `assignmentFound` true and `isExpired` false, and no direct assignment without a grant (guide [A3](../admin/A3-workload-grants.md) explains the findings).
- `run_owner_check` names no departed or disabled owner on a tier 3 application without a deputy.
- `run_gap_check` as a platform admin who holds an inventory role returns the inventory comparison; as one who does not, it answers in plain words that the role is missing.
- The same tools as a person without the product role answer `not_authorized`; a pipeline's or the runner's token never reaches them.
- The function app's configuration holds no client secret for the API app registration.

## Open points

1. **The least permissions** for the four reads (document 24, spike 14).
2. **Agent users in group members.** Whether the Graph read of group members returns the agent user subtype with the permissions above is not confirmed (document 26, section 4.5). `check_role_bindings` and the approval runner both rely on telling users from other principals.
3. **Checks designed but not in `check_role_bindings` yet:** nested groups (CHK-03), roles that require just-in-time membership (CHK-08) and guests on roles that do not allow guests (CHK-11). The tool reads group existence, direct assignments and Workload Grants today.
4. **The token cache.** The service keeps each downstream token in an in-memory cache until it expires (Microsoft.Identity.Web). Each use still needs a valid incoming token from the person, so the cache does not outlast the person's own access, but a revoke is honored only when the person's incoming token expires (guide [A2](../admin/A2-urgent-revoke-and-break-glass.md), open point 1).

## Microsoft facts used

| Fact | Source | Checked |
|---|---|---|
| Only a user-assigned managed identity can be the federated identity credential of an app; scenario "Managed Identity"; audience `api://AzureADTokenExchange`; subject is the managed identity's object (principal) ID; the app and the identity are in the same tenant | https://learn.microsoft.com/entra/workload-id/workload-identity-federation-config-app-trust-managed-identity | 2026-11-23 |
| "The inventory API currently supports only delegated user authentication."; "The API doesn't support app-only authentication through a service principal or managed identity. Requests that use these identities return HTTP 403 Forbidden." | https://learn.microsoft.com/power-platform/admin/inventory-api | 2026-11-23 |
| The permission table lists `ResourceQuery.Resources.Read`, display name "Query resources", "Allows querying of resources." | https://learn.microsoft.com/power-platform/admin/programmability-permission-reference | 2026-11-23 |
| "To view the Power Platform inventory, you must hold one of the supported Microsoft Entra roles."; the table gives Global administrator, Power Platform administrator, Dynamics 365 administrator and Global reader "All inventory resources", and AI administrator and AI reader only agents, agentic apps, agent flows, environments and environment groups | https://learn.microsoft.com/power-platform/admin/power-platform-inventory#access-requirements | 2026-11-23 |
