# Guide S4. Prepare Microsoft Entra ID and deploy the product to Azure

- **Status:** 0.2 draft, 2026-11-23. Nothing here was run against a tenant or a subscription. The azd template in `src/adapters/azure/infra/` compiles with `az bicep build` and has never been provisioned.
- **Guide set:** setup guides S1 to S7 and admin guides A1 to A4. Index: [README](../README.md).
- **Design sources:** document 21, sections 4.8 and 11.2; document 22, section 11; document 24, sections 4 and 9.2; document 25, section 12; document 26, sections 3.4, 4, 5.6, 8 and 11; document 28, issue 13, Q20 and Q21; `src/adapters/azure/README.md` and `src/adapters/azure/infra/README.md`.
- **Open design questions that touch this guide:** 12 (marker below).

## Purpose

The first release is designed to run in the client's own Azure subscription (D-26): one Azure Functions app on the Flex Consumption plan hosts the API, the product's MCP server, the engine and the product's web app, over Azure SQL Database, behind Microsoft Entra ID sign-in. This guide puts the work in order: the role groups, the app registrations, the deployment and the first checks. The click-by-click steps live in `src/adapters/azure/infra/README.md`; this guide points to its sections instead of copying them, and adds what the product needs from each step.

At the end of this guide the service answers, but no configuration version is in force yet. Version 1 comes from guide [S1](S1-pick-presets-and-overlays.md), step 7, and that step is not built yet (part F).

## Who

| Who | Part |
|---|---|
| The client's Entra admin | Creates the role groups and the app registrations, assigns groups to app roles, grants admin consent. Needs Cloud Application Administrator or Application Administrator for the app registrations, Groups Administrator (or User Administrator) for the groups, and a role that can grant tenant-wide admin consent |
| The client's Azure team | Runs `azd up` signed in as themselves. Needs Owner, or Contributor plus Role Based Access Control Administrator, on the subscription (the template creates role assignments) |
| The client's platform owner | Decides the custom domain, which optional parts go on now (web app, approval runner, directory reads), and who holds the SQL administrator role (open point 3) |
| The client's configuration admin | Collects the group object IDs for the client binding patch (guide S1, step 8) |

## Prerequisites

- **Microsoft Entra ID P1 or P2** in the client's tenant. People get product roles only through Entra groups (D-11), and Microsoft says: "Group-based assignment requires Microsoft Entra ID P1 or P2 edition." Without it, see open point 2.
- **A verified domain** for the MCP endpoint's host name. MCP clients ask for a token for the MCP URL itself, so the API's Application ID URI is `https://<host>/mcp`, and Microsoft Entra accepts an `https` Application ID URI only on a verified custom domain or the tenant's initial domain. Until the domain is bound, the default `api://<api-client-id>` works for health, metadata, the REST API and the pipeline routes, but not for Claude Code.
- **Tools on the machine that runs the deployment:** the Azure Developer CLI, the .NET 10 SDK, Node.js 22 or later with npm (the pre-package hook builds the web app), and on Windows PowerShell 7 for the post-provision hook.
- **Decisions made before you start:** the region (one that offers the Flex Consumption plan), the environment name, a random telemetry salt of at least 16 characters (32 or more is better), and the preset with its overlays (guide S1, steps 1 to 6).

## Steps

### Part A. One Entra group per product role

1. **Create the groups.** One security group per human product role the chosen preset uses, with assigned membership. The 14 role keys of the shipped presets are `requester`, `maker`, `application-owner`, `data-owner`, `architect`, `change-manager`, `change-board`, `pmo`, `ai-lead`, `security-reviewer`, `platform-admin`, `configuration-admin`, `auditor` and `reader`. Document 26 proposes the name `<product>-role-<role-key>` (section 3.2). Starter and the small team overlay may let one group carry several roles (guide S1).
2. **Keep the groups flat.** Members are users, added directly. Microsoft: a group assignment "doesn't cascade to nested groups", and the groups claim option "Groups assigned to the application" includes a group only when the user is a direct member. A nested member gets no role.
3. **Keep workloads out.** No service principal, managed identity, agent identity or device in a role group. Microsoft: "if you add a service principal to a group, and then assign an app role to that group, Microsoft Entra ID doesn't add the `roles` claim to tokens it issues." Pipelines and the approval runner get their workload role directly, recorded as a Workload Grant (guides [S5](S5-connect-the-approval-runner.md) and [S6](S6-connect-pipeline-record-steps.md), and [A3](../admin/A3-workload-grants.md)). WORKING DEFAULT (open design question 12)
4. **Name the holders.** At least two people in every group of a role that approves, so a deputy exists (document 26, RR-07). Give each group an owner from the role's accountable side.
5. **Write down each group's object ID** against its role key. These IDs are client facts: they go into the client binding patch, which lives in the client's own private repository and never in a public preset (V44).

### Part B. The API app registration

Follow `infra/README.md`, section "1. The API app registration". What the product relies on:

| Setting | Value | Why the product needs it |
|---|---|---|
| Access token version | `api.requestedAccessTokenVersion` = 2 | The service accepts v2 access tokens only, whose audience is the client ID |
| Application ID URI | `https://<host>/mcp`, no trailing slash | The MCP resource URL; Claude Code asks for a token for it |
| Delegated scopes | `mcp.access` (MCP clients) and `api.access` (the web app), both "Admins only" | The MCP endpoint takes `mcp.access`; the REST API takes `api.access` |
| App roles for people | One per role key above, allowed member types "Users/Groups", the role key as the value | The assignment admits people and keeps each group's role visible in Entra |
| App roles for workloads | `Deployment.Record` and `Approval.Write`, allowed member type "Applications" | The record endpoint and the approval runner's routes take only app-only tokens with these roles |
| Groups claim | "Groups assigned to the application", group IDs in the access token (`groupMembershipClaims` = `ApplicationGroup`) | The engine matches these group object IDs against the Role Group Binding rows on every call (document 26, RA-01) |
| Optional claim | `idtyp` in the access token | Tells user tokens and app-only tokens apart |
| Enterprise application | Assignment required = Yes; each role group assigned to its app role | Only assigned people get a token at all |

Two things to know:

- **The engine reads groups, not the `roles` claim, for people.** A person whose group is assigned to an app role but not bound in the configuration in force gets no product role. A person in a bound group gets the role whatever the token's `roles` claim says. Matching by app role value is a proposal (`ENGINE-CHANGES-NEEDED.md`, item 6).
- **Overage.** Microsoft leaves the groups claim out of a JWT when the person is in more groups than the limit of 200. With "Groups assigned to the application", only the product's role groups count, so overage is unlikely; a token whose groups overflowed is refused with a plain message.

### Part C. The client registrations

Each is a separate app registration, created only when its part goes on:

| Registration | Guide | Notes |
|---|---|---|
| The Claude Code client (a public client) | [S2](S2-connect-claude-code.md), path 1; `infra/README.md`, section 2 | Pre-authorized for `mcp.access` only |
| The web app (a single-page application) | `infra/README.md`, section 4 | Redirect URI `https://<host>/app/` exactly; pre-authorized for `api.access` only; pass its client ID as `PRODUCT_WEB_APP_CLIENT_ID` |
| The approval runner's identity | [S5](S5-connect-the-approval-runner.md) | App role `Approval.Write`, plus a Workload Grant |
| Each pipeline's identity | [S6](S6-connect-pipeline-record-steps.md) | App role `Deployment.Record`, plus a Workload Grant |
| The directory reads' federated credential (on the API app registration itself) | [S7](S7-turn-on-directory-reads.md) | Optional |

The web app's tokens act as the person: the REST API admits the person entry only when the token's verified `azp` is one of the web app client IDs the host declares. Any other client with a token for the same API is treated as an AI client acting for the person, and must pass the Known Client check. The four pages are read-only, so the REST API serves the read tools only.

### Part D. Deploy with azd

From `src/adapters/azure`, signed in as yourself (`azd auth login`). The full list of values and defaults is in `infra/README.md`, sections "Parameters" and "Deploy".

```
azd auth login
azd env new <environment-name>
azd env set AZURE_LOCATION <region>
azd env set PRODUCT_API_CLIENT_ID <api-client-id>
azd env set SQL_CLIENT_IP_ADDRESS <this machine's public IPv4 address>
azd env set PRODUCT_TELEMETRY_SALT <random value, 32 characters or more>
azd up
```

1. **Leave the optional parts off for the first deployment** unless their registrations already exist: `PRODUCT_WEB_APP_CLIENT_ID` unset (the web app stays off), `PRODUCT_APPROVAL_RUNNER_ENABLED` false (guide S5 turns it on), `PRODUCT_DIRECTORY_READS` `Off` (guide S7 turns it on).
2. **What `azd up` does, all with your own sign-in:** it provisions the resources, runs the post-provision hook (the SQL migrations as you, which create the tables, the row-level security policies and the app's managed identity as a contained database user), builds the web app and the Functions project, and uploads the package. No step uses a password, a client secret or a storage key.
3. **The SQL administrator.** By default the SQL server's Microsoft Entra administrator is the person who ran azd. For production, set `SQL_ADMINISTRATOR_OBJECT_ID` to a group and keep that group for break-glass accounts (guide [A2](../admin/A2-urgent-revoke-and-break-glass.md), part B). See open point 3 for who then runs later migrations.
4. **Bind the custom domain** as `infra/README.md`, section "Custom domain and the Application ID URI", describes, then `azd env set PRODUCT_CUSTOM_BASE_URL https://<host>` and `azd provision`, then set the API's Application ID URI to `https://<host>/mcp`.
5. **Turn on the web app** once its registration exists: `azd env set PRODUCT_WEB_APP_CLIENT_ID <web-client-id>` and `azd provision`.

### Part E. First checks, before version 1

Use the base URL azd prints (`azd env get-values` lists `PRODUCT_PUBLIC_BASE_URL` and `PRODUCT_MCP_URL`).

1. `GET https://<host>/health/live` and `GET https://<host>/health/ready` answer 200 with a status word only (`{"status":"live"}`, `{"status":"ready"}`). `ready` also proves the database answers within `Product:Health:ReadyTimeoutSeconds` (default 5 seconds); otherwise it answers 503 with `{"status":"unavailable"}`.
2. `GET https://<host>/.well-known/oauth-protected-resource/mcp` answers the protected resource metadata: the resource `https://<host>/mcp` and the tenant's v2.0 issuer as its authorization server.
3. `POST https://<host>/mcp` with no token answers 401 with `WWW-Authenticate: Bearer resource_metadata="https://<host>/.well-known/oauth-protected-resource/mcp"`.
4. A signed-in call that needs the configuration answers `configuration_unavailable` ("No configuration version is in force, so the engine cannot answer. Bootstrap version 1 first."). That is the expected state until version 1 exists.
5. With the web app on, `GET https://<host>/app/config.json` names the tenant's sign-in, the web app's client ID and the API scope, and holds no secret.
6. In the Azure portal: the function app has publishing passwords off and HTTPS only; the SQL server has Microsoft Entra-only authentication; the storage account has shared key access off.

### Part F. Version 1 and the client binding patch (not built yet)

The engine has the bootstrap operation of document 21, section 4.8 (version 1 accepted directly by the installing person, recorded as a configuration change decision), but no route and no MCP tool calls it yet, so a deployed install cannot reach version 1 today. The local server's `seed` command is not a way around this: it refuses every environment but Development, and it writes version 1 and its rows past the engine for local runs and tests only. Guide S1, steps 7 and 8, describes the designed steps and their open points.

## Checks

- Every role key the preset uses has exactly one group, every group is assigned to its app role, and no group has a nested group, a service principal or a device as a member.
- The API app registration's manifest shows `requestedAccessTokenVersion` 2, `groupMembershipClaims` `ApplicationGroup` and the `idtyp` optional claim.
- The enterprise application has Assignment required = Yes, and its Users and groups list shows only the role groups (and, later, no user assigned directly; see open point 2).
- The six checks of part E pass (the fifth only with the web app on).
- No file in the client's repository holds a secret; the binding patch with the group IDs is in the client's private repository only.

## Open points

1. **The bootstrap of version 1 has no entry point** (guide S1, open points 1 and 2). Until one exists, this guide ends at part E.
2. **Without Entra ID P1.** `infra/README.md` offers "Security groups" in the groups claim and people assigned one by one. The engine still reads roles from group IDs, so D-11 holds, but `check_role_bindings` then reports every person as holding a role directly (it lists every user app role assignment with no Workload Grant), which is CHK-01's finding. Either the product states that P1 is required, or the check learns to tell a "Default Access" assignment from a role assignment. Proposal: state P1 as a requirement of the Azure binding.
3. **Who runs later migrations** once the SQL administrator is a break-glass group (guide A2, part B, step 3). The post-provision hook runs the migrations as the person who runs azd, and creating the app's database user needs the SQL administrator. Proposal: a deployment group, PIM-eligible, that is the SQL administrator only while a migration runs, separate from the break-glass accounts; document 26 would need to say so.
4. **The private data tier option** (private endpoints for Azure SQL, storage and Key Vault) is not in the template. Until it is, the SQL server allows Azure services and every hop uses Microsoft Entra authentication over public endpoints (document 26, section 11, recommends the hybrid option).

## Microsoft facts used

| Fact | Source | Checked |
|---|---|---|
| "Group-based assignment requires Microsoft Entra ID P1 or P2 edition."; "When you assign a group to an application, only users in the group have access. The assignment doesn't cascade to nested groups." | https://learn.microsoft.com/entra/identity/enterprise-apps/assign-user-or-group-access-portal | 2026-11-23 |
| `groupMembershipClaims` `ApplicationGroup` "Emits only the groups that are explicitly assigned to the application and that the user is a member of" | https://learn.microsoft.com/entra/identity/hybrid/connect/how-to-connect-fed-group-claims | 2026-11-23 |
| "Groups assigned to the application" includes a group only when the user is a direct member; keep the number of groups under the overage limit of 200 | https://learn.microsoft.com/security/zero-trust/develop/configure-tokens-group-claims-app-roles | 2026-11-23 |
| "Currently, if you add a service principal to a group, and then assign an app role to that group, Microsoft Entra ID doesn't add the `roles` claim to tokens it issues."; "App roles can also be assigned to the service principal for another application, or to the service principal for a managed identity." | https://learn.microsoft.com/entra/identity-platform/howto-add-app-roles-in-apps | 2026-11-23 |
| An `https` Application ID URI needs a verified custom domain or the tenant's initial domain; the value must not end with a slash | https://learn.microsoft.com/entra/identity-platform/identifier-uri-restrictions | 2026-11-23 |
| For an MCP server, "An HTTPS endpoint with a nonempty path and no trailing slash" and "Set the Application ID URI to the MCP server's canonical endpoint URL" | https://learn.microsoft.com/entra/agent-id/secure-mcp-server-with-entra-id | 2026-11-23 |
