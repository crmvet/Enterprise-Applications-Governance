# Install on Azure

The product runs in the organization's own Azure subscription: one Azure Functions app on the Flex Consumption plan
hosts the API, the MCP server, the engine and the web app (under `/app`, on the same origin), over Azure SQL Database,
behind Microsoft Entra ID sign-in. The template is an Azure Developer CLI (azd) project in `adapters/azure`
(`azure.yaml` and `infra/`). The person who installs runs it signed in as themselves; no step of the template uses a
password, a client secret or a storage key.

This page is the install path in order. The full resource list, every parameter and every app setting are in
[the infra README](../adapters/azure/infra/README.md), and the longer setup guides
([S4](setup/S4-prepare-entra-and-deploy.md) and the others) explain each Entra step with Microsoft's own words.

**What works today.** The template has been provisioned as a demo install with fictional data and the demo persona
switch on. A real install with the organization's own data cannot reach its first configuration version yet: the
engine's bootstrap of version 1 exists, but no route or tool calls it ([A real install](#a-real-install), below).

Placeholders on this page: `<host>` (the install's public host name), `<tenant-id>`, `<api-client-id>`,
`<web-client-id>`, `<client-id>` (an MCP client's registration), `<port>`, `<environment-name>` and `<region>`.

## Before the install

- The Azure Developer CLI, the .NET 10 SDK and Node.js 22 or later with npm (the pre-package hook builds the web
  app). On Windows, PowerShell 7 for the hooks.
- On the subscription: Owner, or Contributor plus Role Based Access Control Administrator (the template creates role
  assignments).
- In Microsoft Entra ID: an admin who can create app registrations and groups, assign groups to an application and
  grant tenant-wide admin consent (guide [S4](setup/S4-prepare-entra-and-deploy.md#who) names the roles). Assigning
  groups to an application needs Microsoft Entra ID P1 or P2; without it, people are assigned one by one.

## What gets deployed

One resource group (`rg-<environment-name>` by default) with the function app and its system-assigned managed
identity, the Flex Consumption plan, a storage account with shared key access off, an Azure SQL logical server with
Microsoft Entra-only authentication and a serverless database, Log Analytics and Application Insights, and, only when
needed, a Key Vault for the telemetry salt and a user-assigned managed identity for the directory reads. The
post-provision hook runs the product's SQL migrations as the person who ran azd, and creates the app's managed
identity as a contained database user. The app's own identity never changes the schema.

## 1. The API app registration

The app the service validates tokens for. In the Microsoft Entra admin center:

1. **New registration**, accounts in this organizational directory only, no redirect URI.
2. **Manifest:** `api.requestedAccessTokenVersion` set to `2`. The service accepts v2 access tokens only.
3. **Expose an API:** the Application ID URI. For MCP clients it must be the MCP endpoint's own URL,
   `https://<host>/mcp`, which Entra accepts only on a domain the tenant has verified ([custom domain](#custom-domain)).
   Until then keep `api://<api-client-id>`: the web app, the REST API and the pipeline routes work, but MCP clients
   cannot get a token.
4. **Two scopes**, admin consent only: `mcp.access` (MCP clients ask for it) and `api.access` (the web app asks for it).
5. **App roles for people**, allowed member types Users/Groups, one per product role, with the role key as the value:
   <!-- generated: human-roles (node tools/docs-gen.mjs) -->
   `requester`, `maker`, `application-owner`, `data-owner`, `architect`, `security-reviewer`, `change-manager`, `change-board`, `pmo`, `ai-lead`, `platform-admin`, `data-governance-lead`, `configuration-admin`, `auditor`, `reader`
   <!-- end generated: human-roles -->
6. **App roles for workloads**, allowed member type Applications: `Deployment.Record` (pipelines that record
   deployments) and `Approval.Write` (the approval runner).
7. **Token configuration:** the groups claim, "Groups assigned to the application", group IDs in the access token. The
   engine reads product roles from these group IDs. Add the optional claim `idtyp` to the access token, which tells
   user tokens and app-only tokens apart. A token whose groups overflow Entra's limit is refused with a message that
   says so.
8. **Enterprise application, Properties:** "Assignment required" set to Yes, so only assigned people get a token.
9. **Enterprise application, Users and groups:** assign each role's Entra group to its app role. Use the same groups
   the configuration binds to the roles: the engine matches the group IDs in the token against the configuration's
   Role Group Bindings on every call.
10. Note the tenant ID and the application (client) ID: `<tenant-id>` and `<api-client-id>`.

## 2. The Claude Code client

Needed only for people who use Claude Code (or another MCP client) with the product. One public client registration
per MCP client:

1. **New registration**, accounts in this organizational directory only.
2. **Authentication, Mobile and desktop applications:** the redirect URI `http://localhost:<port>/callback`, with one
   fixed free port. Entra ignores the port of a localhost redirect URI when it matches, so do not add a second
   localhost entry that differs only by port.
3. On the API registration, **Expose an API, Add a client application:** this client's ID with the `mcp.access`
   scope, so people see no consent prompt.
4. A configuration admin adds the client's app ID to the Known Client list (a configuration change), with the label
   the product stamps on its writes, such as "Claude Code".

Each person then connects as [mcp.md](mcp.md#a-real-install) says.

## 3. Pipelines and the approval runner

Optional. App-only callers use their own workload identity, a managed identity or an app registration with a
federated credential, never a client secret. Assign `Deployment.Record` to each pipeline's service principal and
`Approval.Write` to the approval runner's. A configuration admin then records the same identity as a Workload Grant
(`deployment-recorder` or `approval-writer`): the app role lets the token through the door, and the grant is what the
engine checks. Guides [S5](setup/S5-connect-the-approval-runner.md) and [S6](setup/S6-connect-pipeline-record-steps.md)
have the steps. Turn the runner's routes on with `azd env set PRODUCT_APPROVAL_RUNNER_ENABLED true` once its identity,
app role and grant exist.

## 4. The web app registration

1. **New registration**, accounts in this organizational directory only.
2. **Authentication, Single-page application:** the redirect URI `https://<host>/app/` exactly, with the trailing
   slash, and the same URI as the front-channel logout URL. A single-page application has no secret. The URI is known
   after the first provision (`azd env get-values` prints `PRODUCT_WEB_APP_URL`), so this step usually follows the
   first `azd up`, then one more `azd provision`.
3. On the API registration, **Expose an API, Add a client application:** this client's ID with the `api.access` scope
   only.
4. `azd env set PRODUCT_WEB_APP_CLIENT_ID <web-client-id>`. The template then switches the web app on, declares the
   client as the person entry and writes `/app/config.json` from the app settings. When the Application ID URI is not
   `api://<api-client-id>`, also set `PRODUCT_WEB_APP_API_SCOPE` to `<Application ID URI>/api.access`.

## 5. The directory reads (optional)

`get_role_holders`, `run_owner_check`, `check_role_bindings` and `run_gap_check` can read Microsoft Graph and the
Power Platform inventory as the person who asks. They are off until `azd env set PRODUCT_DIRECTORY_READS Live` and a
provision. Live reads then need a federated identity credential on the API registration that trusts the template's
user-assigned managed identity (no client secret), and delegated Microsoft Graph and Power Platform API permissions
with admin consent. Guide [S7](setup/S7-turn-on-directory-reads.md) and the infra README's section on the directory
reads list both. With the reads off, those tools answer `upstream_unavailable`, and the Teams page says membership is
managed in Entra.

## Deploy

From `src/adapters/azure`:

```
azd auth login
azd env new <environment-name>
azd env set AZURE_LOCATION <region>
azd env set PRODUCT_API_CLIENT_ID <api-client-id>
azd env set SQL_CLIENT_IP_ADDRESS <this machine's public IPv4 address>
azd env set PRODUCT_TELEMETRY_SALT <a random value of 32 characters or more>
azd up
```

`azd up` provisions the resources, runs the migrations as the signed-in person, builds the web app and the Functions
project, and uploads the package. Pick a region that offers the Flex Consumption plan. Without a telemetry salt the
service makes a random one each time a process starts, so the caller hashes in telemetry change on every restart.

## Checks after deploy

With the base URL that `azd env get-values` prints as `PRODUCT_PUBLIC_BASE_URL`:

- `GET <base>/health/live` and `GET <base>/health/ready` answer 200 with a status word only. `ready` answers 503 while
  the database does not answer in time; a serverless database that paused takes a moment to resume.
- `GET <base>/.well-known/oauth-protected-resource/mcp` answers the protected resource metadata.
- `POST <base>/mcp` with no token answers 401 with a `WWW-Authenticate` header that names the metadata.
- With the web app on, `GET <base>/app/config.json` names the sign-in, the web app's client ID and the API scope, and
  `<base>/app/` signs a person in.

## A real install

The database starts empty, with no configuration version in force, so every tool that needs the configuration
answers `configuration_unavailable`. Version 1 is meant to come from the engine's bootstrap: a person with install
rights applies a preset, its overlays and the organization's binding patch (its Role Group Bindings, Workload Grants
and Known Clients), and is recorded as the decider. The engine operation is built, but no route and no MCP tool calls
it yet, so a real install cannot reach version 1 today. Guide [S1](setup/S1-pick-presets-and-overlays.md), steps 7
and 8, describes the designed steps.

The local server's `seed` command is not a way around this. It writes version 1 and its rows past the engine, with no
person recorded as the decider, and it runs only in the Development environment and in the environment named `Demo`.

## A demo install

A demo install shows the product on fictional data, never real data. Every page carries a "Demo, fictional data" ("Demo data" on a phone)
badge, and one person, the demo operator, can act as each of the fictional demo personas.

1. **Before `azd up`:** `azd env set PRODUCT_DEMO true` and `azd env set PRODUCT_DEMO_PERSONAS true`, with the web app
   registration in place. The app then runs in the environment named `Demo` and reads the fictional persona file the
   package carries (`adapters/azure/seed/demo-personas.json`). The app refuses to start with personas in any other
   environment, or without the demo flag.
2. **Seed and direct it.** The demo kit's `demo sandbox rebuild` runs the steps in order from `src/adapters/azure`,
   reading every value from the azd environment: write the seed files, delete the database (only with `--yes`),
   provision (with `--no-state`, so the deployment that creates the database runs again), deploy, seed the demo estate
   in the Demo environment, run the demo director through a local server on loopback over the install's database, and
   check. `--dry-run` prints every command, with `$NAME` in place of each value, and runs nothing. The kit's tests cover
   the dry run. Details: [the demo kit README](../demo/kit/README.md#demo-sandbox-rebuild).
3. **Let only the operators in.** Anyone who can sign in to the web app of a demo install can act as any persona, the
   configuration admin among them. Keep "Assignment required" on and assign only the demo operators. An operator needs
   no product role of their own. To give the install's own admins product roles as themselves, pass
   `--extra-binding` and `--extra-rows` with files kept out of source control.
4. **Before each demo:** `demo refresh` asks again every story approval request that expired or expires within 48
   hours, and `demo check` gives a go or no-go per scenario ([admin.md](admin.md#the-demo-install)).

## Custom domain

MCP clients ask Entra for a token for the MCP URL itself, so an install used with Claude Code binds a custom domain on
a domain the tenant has verified:

1. On the function app, **Custom domains, Add custom domain**: the CNAME and `asuid` TXT records it asks for, and an
   App Service managed certificate.
2. `azd env set PRODUCT_CUSTOM_BASE_URL https://<host>`, then `azd provision`.
3. On the API registration, set the Application ID URI to `https://<host>/mcp`.

## Later changes

- `azd deploy` publishes a new build. `azd provision` reapplies the infrastructure and runs the migrations again; they
  are idempotent. Each build needs a database schema version (version 9 today) and refuses an Azure SQL schema older
  than that, so run the migrations before a new build that needs a newer schema.
- The template writes the full set of app settings on every provision, so a setting added in the portal is lost at
  the next provision. Add settings to `infra/modules/function-app-settings.bicep`.
- **The daily check.** The Functions timer `due-state-refresh` fires at five past every hour and runs the engine's
  daily due-state refresh in the hour `Product:DueStateRefresh:HourUtc` names. The template does not set that hour
  yet, so a deployed install runs no daily refresh on its own; a platform admin runs it from the Admin page
  ([admin.md](admin.md#the-daily-check)).
- `azd down` deletes the resource group. Add `--purge` to purge the Key Vault too.

## A local run

Everything runs on one machine with no Azure subscription and no sign-in: the local server over a SQLite file, the
fictional demo estate, and a development identity in place of Entra sign-in. From the repository root:

```
node src/demo/kit/demo.mjs local up --port 5080
```

It builds what is missing or stale, seeds a fresh store, starts the server on `http://127.0.0.1:5080` with the demo
flag and the directory mode Demo, runs the demo director, and prints the web app's URL, the MCP endpoint and the
personas. `node src/demo/kit/demo.mjs local down` stops it. On Windows the kit runs the server in WSL, because Smart
App Control can stop freshly built .NET assemblies from running. The steps by hand, and every development persona,
are in [the Azure README](../adapters/azure/README.md#run-it-locally).
