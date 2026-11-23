# infra: the Azure template

This folder and `../azure.yaml` are an Azure Developer CLI (azd) template. It deploys the product into the client's
own Azure subscription: one Azure Functions app that hosts the API, the MCP server and the engine, over Azure SQL
Database, behind Microsoft Entra ID sign-in.

The client's administrator deploys it with `azd up`, signed in as themselves (`azd auth login`). Nothing in the
template signs in for them, and no step uses a password, a client secret or a storage key.

## What gets deployed

Everything goes into one resource group, `rg-<environment name>` by default. Names end with a short token derived
from the subscription, the environment name and the region, shown here as `<token>`.

| Resource | Name | What it is for | Security settings |
|---|---|---|---|
| Function app | `func-<token>` | The service: `/mcp`, `/api/...`, the record endpoint, the approval outcome endpoint, health, the protected resource metadata. Linux, .NET 10 isolated worker, one catch-all HTTP function | System-assigned managed identity. HTTPS only, TLS 1.2 minimum, FTP off, publishing passwords off (azd deploys with your Microsoft Entra sign-in) |
| Flex Consumption plan | `plan-<token>` | Hosting plan (FC1). Scales out on demand and to zero when idle | |
| Storage account | `st<token>` | The Functions host's storage and the `deployment-package` container that holds the code package | Shared key access off, public blob access off, TLS 1.2 minimum. The app reaches it with its managed identity |
| Azure SQL logical server | `sql-<token>` | The product's data | Microsoft Entra-only authentication (no SQL logins), TLS 1.2 minimum, security audit events to Log Analytics |
| Azure SQL database | `product` | Serverless General Purpose database (1 vCore maximum, pauses after 60 idle minutes by default) | The app connects as its managed identity, a contained database user the post-provision hook creates |
| Log Analytics workspace | `log-<token>` | Logs, Application Insights data and SQL audit events | Resource-permission log access |
| Application Insights | `appi-<token>` | Telemetry from the Functions host and the worker (OpenTelemetry) | Key based ingestion off by default: only the app's identity can send telemetry |
| Key Vault | `kv-<token>` | The telemetry salt. Only deployed when you give a salt | Azure RBAC, soft delete. The app reads the secret through a Key Vault reference |

Role assignments for the app's managed identity, each on one resource only:

| Role | On | Why |
|---|---|---|
| Storage Blob Data Owner | The storage account | Host storage and the deployment package |
| Storage Table Data Contributor | The storage account | The Functions host's diagnostic events |
| Monitoring Metrics Publisher | Application Insights | Telemetry with Microsoft Entra authentication |
| Key Vault Secrets User | The Key Vault, when there is one | The telemetry salt reference |

App settings hold no secrets: the SQL connection string uses `Authentication=Active Directory Managed Identity`, host
storage uses `AzureWebJobsStorage__accountName` with the managed identity, and the telemetry salt is a Key Vault
reference. The template writes the full set of app settings on every provision, so add any new setting to
`modules/function-app-settings.bicep`, not in the portal.

The post-provision hook (`hooks/postprovision.ps1` on Windows, `hooks/postprovision.sh` elsewhere) runs the product's
SQL migrations as the person who ran azd: the tables, the row-level security policies, and the app's managed identity
as a contained database user with its grants. The app's own identity never changes the schema. The hook runs the local
server's `migrate` command (`../Product.Azure.Server`) with two configuration values: `ConnectionStrings__Product`, a
connection string with `Authentication=Active Directory Default` (the azd sign-in), and
`Product__Migrate__AppIdentityName`, the function app's name, which is the display name of its managed identity and so
the database user name.

| File | What it holds |
|---|---|
| `main.bicep` | Subscription-scope entry point: the resource group, the modules, the outputs azd stores |
| `main.parameters.json` | How azd environment values map to the Bicep parameters |
| `modules/function-app.bicep` | The plan, the function app and its identity |
| `modules/function-app-settings.bicep` | Every app setting |
| `modules/storage.bicep` | The storage account and the deployment container |
| `modules/sql.bicep` | The logical server, the database, firewall rules, auditing |
| `modules/monitoring.bicep` | Log Analytics and Application Insights |
| `modules/keyvault.bicep` | The Key Vault and the telemetry salt secret |
| `modules/role-assignments.bicep` | The app identity's role assignments |
| `hooks/` | The post-provision migration |

## Before you deploy: the Microsoft Entra app registrations

The template does not create Microsoft Entra objects. The client's administrator creates them by hand in the Microsoft
Entra admin center, once per tenant, and passes the API's client ID to azd. Three kinds are needed.

### 1. The API app registration

This is the app the service validates tokens for. Its client ID is the token audience.

1. **App registrations > New registration.** Any name, for example "Enterprise Applications Governance API".
   Supported account types: accounts in this organizational directory only. No redirect URI.
2. **Manifest.** Set `api.requestedAccessTokenVersion` to `2`. The service accepts v2 access tokens only.
3. **Expose an API.** Set the Application ID URI to the MCP endpoint's canonical URL, `https://<your host>/mcp`,
   with no trailing slash. Microsoft Entra accepts an `https` Application ID URI only on a domain your tenant has
   verified, so this needs the custom domain described in [Custom domain](#custom-domain-and-the-application-id-uri).
   Until the domain is bound, keep the default `api://<client id>`: health, metadata, the REST routes and the pipeline
   routes work, but MCP clients such as Claude Code need the canonical URL to get a token.
4. **Expose an API > Add a scope**, twice:
   - `mcp.access`: who can consent "Admins only", display name "Use the governance MCP server". MCP clients request it.
   - `api.access`: who can consent "Admins only", display name "Use the governance web app". The product's web app
     requests it.
5. **App roles > Create app role** for people. One per product role, allowed member types "Users/Groups", with the
   role key from the configuration as the value: `requester`, `maker`, `application-owner`, `data-owner`,
   `architect`, `change-manager`, `change-board`, `pmo`, `ai-lead`, `security-reviewer`, `platform-admin`,
   `configuration-admin`, `auditor`, `reader`.
6. **App roles > Create app role** for workloads, allowed member types "Applications":
   - `Deployment.Record`: pipelines that record deployments (`POST /api/deployments`).
   - `Approval.Write`: the approval runner that writes approval outcomes.
7. **Token configuration > Add groups claim.** Choose "Groups assigned to the application" and emit the group ID in
   the access token. (In the manifest this is `groupMembershipClaims` set to `ApplicationGroup`.) The engine reads
   product roles from these group object IDs.
8. **Token configuration > Add optional claim**, access token, `idtyp`. It tells user tokens and app-only tokens apart.
9. **Enterprise applications >** the same app **> Properties.** Set "Assignment required" to Yes, so only assigned
   people get a token.
10. **Enterprise applications > Users and groups.** Assign each product role's Entra group to its app role. Use the
    same groups your configuration admin binds to the roles in the product's configuration (Role Group Binding rows):
    the engine matches the group IDs in the token against those rows on every call.
11. Copy the **Directory (tenant) ID** and the **Application (client) ID**.

Assigning groups to an application needs Microsoft Entra ID P1 or P2. Without it, choose "Security groups" in step 7
and assign people one by one in step 10. A token whose groups overflow (more than 200 groups) is refused in this
release with a message that names the cause.

### 2. The Claude Code client

Claude Code signs in as a registered public client of your tenant. Microsoft recommends a registered client over the
Client ID Metadata Document option, which is in preview.

1. **App registrations > New registration.** Any name, for example "Governance MCP client". Accounts in this
   organizational directory only.
2. **Authentication > Add a platform > Mobile and desktop applications.** Add the redirect URI
   `http://localhost:<port>/callback` with a fixed free port, for example `33418`.
3. On the **API app registration > Expose an API > Add a client application**, add this client's ID and select the
   `mcp.access` scope. People then see no consent prompt. (Alternatively, add the `mcp.access` permission on this
   client under API permissions and grant admin consent.)
4. Each person connects with their own Claude Code:

   ```
   claude mcp add --transport http --client-id <client app ID> --callback-port <port> governance https://<your host>/mcp
   ```

   Claude Code reads the server's protected resource metadata, signs the person in through Microsoft Entra ID in the
   browser, and sends the token with each request. The service runs every tool as that person, stamped as an AI client
   acting for them; it never forwards the token anywhere.

Other MCP clients (for example Visual Studio Code with GitHub Copilot) follow the same pattern: one registered client
each, added to the API's authorized client applications.

### 3. Pipelines and the approval runner

App-only callers use their own workload identity: a managed identity, or an app registration with a federated
credential. Never a client secret.

1. Assign the API's app role to the workload's service principal: `Deployment.Record` for each pipeline that records
   deployments, `Approval.Write` for the approval runner. The admin center does not assign app roles to service
   principals, so use Microsoft Graph, for example with Microsoft Graph PowerShell:

   ```
   New-MgServicePrincipalAppRoleAssignedTo -ServicePrincipalId <API service principal object ID> `
     -PrincipalId <workload service principal object ID> `
     -ResourceId <API service principal object ID> `
     -AppRoleId <ID of the Deployment.Record or Approval.Write app role>
   ```

2. The workload requests a token for `<Application ID URI>/.default` with its own credential and calls the route.
3. A configuration admin records the same identity in the product as a Workload Grant (`deployment-recorder` or
   `approval-writer`). The app role lets the token through the door; the Workload Grant is what the engine checks.
   Without it the engine refuses the call.

The approval route takes only app-only tokens with `Approval.Write`. People and AI clients cannot write an approval
outcome on any route.

## Parameters

Set these with `azd env set <NAME> <value>` before `azd up`. azd passes them through `main.parameters.json`.

| azd environment value | Bicep parameter | Default | What it sets |
|---|---|---|---|
| `AZURE_LOCATION` | `location` | Asked by azd | Region for every resource. Pick one with the Flex Consumption plan |
| `PRODUCT_API_CLIENT_ID` | `apiClientId` | Asked by azd | Application (client) ID of the API app registration. Required |
| `PRODUCT_API_TENANT_ID` | `apiTenantId` | The subscription's tenant | Tenant that issues the tokens |
| `PRODUCT_API_AUDIENCE` | `apiAudience` | The API client ID | Accepted token audience. v2 tokens carry the client ID, so leave it empty |
| `PRODUCT_CUSTOM_BASE_URL` | `customBaseUrl` | The app's default host name | Public base URL once a custom domain is bound, such as `https://governance.contoso.com` |
| `SQL_CLIENT_IP_ADDRESS` | `sqlClientIpAddress` | None | Public IPv4 address of the machine that runs azd, let through the SQL firewall for the migration |
| `SQL_ADMINISTRATOR_OBJECT_ID` | `sqlAdministratorObjectId` | The signed-in deployer | Object ID of the SQL server's Microsoft Entra administrator. A group is better for production |
| `SQL_ADMINISTRATOR_PRINCIPAL_TYPE` | `sqlAdministratorPrincipalType` | From the deployer | `User`, `Group` or `Application`, when `SQL_ADMINISTRATOR_OBJECT_ID` is set |
| `PRODUCT_TELEMETRY_SALT` | `telemetrySalt` | None | A random value of at least 16 characters. When set, a Key Vault holds it and telemetry carries a salted caller hash; when not, telemetry carries no caller field |
| `AZURE_PRINCIPAL_ID`, `AZURE_PRINCIPAL_TYPE` | `principalId`, `principalType` | Set by azd | The signed-in deployer |

Edit these in `main.parameters.json` (or add them there) when the defaults do not fit:

| Bicep parameter | Default | What it sets |
|---|---|---|
| `maximumInstanceCount` | `10` | Most instances the app scales to. Each instance keeps its own SQL connection pool |
| `alwaysReadyInstances` | `0` | Instances kept warm for HTTP. `1` avoids cold starts |
| `instanceMemoryMB` | `2048` | Memory per instance: `512`, `2048` or `4096` |
| `sqlMaxVCores` | `1` | Serverless maximum vCores |
| `sqlMinCapacity` | `0.5` | Serverless minimum vCores |
| `sqlAutoPauseDelayMinutes` | `60` | Idle minutes before the database pauses; `-1` keeps it online. The first request after a pause waits for the database to resume |
| `sqlMaxSizeGB` | `32` | Maximum database size |
| `sqlBackupStorageRedundancy` | `Geo` | `Local`, `Zone`, `Geo` or `GeoZone` |
| `sqlAuditing` | `true` | SQL security audit events (sign-ins, schema, permission and principal changes) to Log Analytics. Statement text is not audited |
| `sqlAllowAzureServices` | `true` | Lets Azure services reach the SQL server; needed while the app has no virtual network integration |
| `applicationInsightsEntraOnly` | `true` | Application Insights accepts only Microsoft Entra authenticated telemetry |
| `logRetentionInDays` | `90` | Log retention |
| `keyVaultPurgeProtection` | `false` | Key Vault purge protection. Recommended for production; it cannot be turned off once on |
| `sqlDatabaseName` | `product` | Database name |
| `resourceGroupName` | `rg-<environment name>` | Resource group name |
| `tags` | `{}` | Extra tags on every resource |

## Deploy

You need:

- The Azure Developer CLI and the .NET 10 SDK. On Windows, PowerShell 7 for the hook.
- On the subscription: Owner, or Contributor plus Role Based Access Control Administrator (the template creates role
  assignments).
- The API app registration from step 1 above.

From `src/adapters/azure`:

```
azd auth login
azd env new <environment name>
azd env set AZURE_LOCATION <region>
azd env set PRODUCT_API_CLIENT_ID <API application (client) ID>
azd env set SQL_CLIENT_IP_ADDRESS <this machine's public IPv4 address>
azd env set PRODUCT_TELEMETRY_SALT <random value, 32 characters or more>
azd up
```

`azd up` provisions the resources, runs the post-provision hook (the SQL migrations, as you), then builds the Functions
project and uploads the package, all with your own sign-in. Run the hook again at any time with
`azd hooks run postprovision`; the migrations are idempotent.

Then check, with the URL azd prints (`azd env get-values` lists `PRODUCT_PUBLIC_BASE_URL` and `PRODUCT_MCP_URL`):

- `GET <base URL>/health/live` and `GET <base URL>/health/ready` answer 200 with no data.
- `GET <base URL>/.well-known/oauth-protected-resource/mcp` answers the protected resource metadata.
- `POST <base URL>/mcp` with no token answers 401 with a `WWW-Authenticate` header that links the metadata.

If the SQL server is created by a service principal (a pipeline) instead of a person, `CREATE USER ... FROM EXTERNAL
PROVIDER` needs the SQL server's own identity to read the directory (the Directory Readers role, or the Microsoft
Graph permissions User.Read.All, GroupMember.Read.All and Application.Read.All). A person running azd needs neither.

To check the template without deploying anything, build it: `az bicep build --file infra/main.bicep`. It needs no
sign-in.

## Custom domain and the Application ID URI

MCP clients ask Microsoft Entra ID for a token for the MCP URL itself, and Microsoft Entra accepts an `https`
Application ID URI only on a verified domain of the tenant. So a production install binds a custom domain:

1. Pick a host name on a domain your tenant has verified, for example `governance.contoso.com`.
2. In the Azure portal, open the function app, **Custom domains > Add custom domain**, create the DNS records it asks
   for (a CNAME to the app's default host name and the `asuid` TXT record), and add an App Service managed
   certificate.
3. Point the service at it and provision again:

   ```
   azd env set PRODUCT_CUSTOM_BASE_URL https://governance.contoso.com
   azd provision
   ```

4. On the API app registration, set the Application ID URI to `https://governance.contoso.com/mcp`.
5. People connect Claude Code to `https://governance.contoso.com/mcp`.

## After deployment

- The database starts empty: no configuration version is in force, so tools that need the configuration refuse.
  Loading the first version (a preset, plus your client binding: Role Group Bindings with your group IDs, Workload
  Grants, Known Clients) through the API comes in a later release. For a pilot, the local server's `seed` command
  (`../README.md`) can write version 1 into an empty database, run by the SQL administrator from a machine the
  firewall allows, with a binding file that holds your own group and client IDs. The command runs only in the
  Development environment, so pass `--environment Development` and `--Product:Store:Provider=sqlserver` with the
  connection string. The fictional demo binding in
  `../seed` is for local runs only.
- `azd deploy` publishes a new build. `azd provision` reapplies the infrastructure and the migrations.
- `azd down` deletes the resource group. Add `--purge` to purge the Key Vault too (not possible with purge protection
  on).

## Running the Functions app on your machine (optional)

For everyday local work, use the Kestrel server in `../Product.Azure.Server` (`../README.md`). To run the Functions
wrapper itself, you need Azure Functions Core Tools, an Azure Storage emulator (Azurite), and a `local.settings.json`
next to the project (never committed) with `FUNCTIONS_WORKER_RUNTIME` set to `dotnet-isolated`, `AzureWebJobsStorage`
set to `UseDevelopmentStorage=true`, `ConnectionStrings__Product` pointing at a database you can reach with
`Authentication=Active Directory Default`, and the `Product__PublicBaseUrl`, `Product__Auth__TenantId` and
`Product__Auth__ClientId` values. The Functions app has no development identity; every request needs a real Microsoft
Entra token.

## Not in this template yet

- Creating the app registrations, the custom domain binding and the managed certificate.
- Virtual network integration with private endpoints for Azure SQL, storage and Key Vault (the private data tier
  option). Until then the SQL server allows Azure services and the app reaches every resource over public endpoints,
  always with Microsoft Entra authentication.
- The separate approval writer database principal, ledger digest storage, approval cards, and on-behalf-of calls to
  Microsoft Graph and the Power Platform API.
