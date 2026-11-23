// The Azure binding of the product in the client's own subscription, for the Azure Developer CLI (azd).
//
// One resource group with:
// - an Azure Functions app on the Flex Consumption plan (Linux, .NET 10 isolated worker) that hosts the API, the
//   MCP server and the engine, with a system-assigned managed identity;
// - a storage account for the Functions host and the deployment package (no shared keys);
// - an Azure SQL logical server with Microsoft Entra-only authentication and a serverless database;
// - Log Analytics and Application Insights (Microsoft Entra authenticated ingestion by default);
// - a Key Vault for the telemetry salt, only when a salt is given;
// - the role assignments the app's identity needs, each on one resource.
//
// Microsoft Entra app registrations are not created here: the client's administrator creates them by hand (README.md)
// and passes the API's client ID and, for the web app, the web app's client ID. The post-provision hook then runs the
// database migrations as the signed-in deployer, which also creates the app's managed identity as a contained database
// user. The same function app serves the product's web app under /app, on the same origin as the API.

targetScope = 'subscription'

// ---------------------------------------------------------------------------------------------------------------
// Environment
// ---------------------------------------------------------------------------------------------------------------

@minLength(1)
@maxLength(64)
@description('Name of the azd environment. Used in the resource group name and the azd-env-name tag.')
param environmentName string

@minLength(1)
@description('Azure region for every resource. Pick one where the Flex Consumption plan is available.')
param location string

@description('Resource group name. Empty uses rg-<environment name>.')
param resourceGroupName string = ''

@description('Extra tags for every resource.')
param tags object = {}

@description('Object ID of the signed-in deployer. azd fills it in. It becomes the Azure SQL Microsoft Entra administrator unless sqlAdministratorObjectId is set.')
param principalId string = ''

@description('Kind of the signed-in deployer, as azd reports it: User, or ServicePrincipal for a pipeline.')
param principalType string = 'User'

// ---------------------------------------------------------------------------------------------------------------
// Microsoft Entra sign-in for the API (values from the app registration the administrator created)
// ---------------------------------------------------------------------------------------------------------------

@minLength(36)
@maxLength(36)
@description('Application (client) ID of the API app registration. Tokens must be issued to this app.')
param apiClientId string

@description('Accepted token audience. Empty means the API client ID, which is the audience of v2 access tokens.')
param apiAudience string = ''

@description('Microsoft Entra tenant ID that issues the tokens. Empty means the tenant of this subscription.')
param apiTenantId string = ''

@description('Microsoft Entra sign-in authority host, with a trailing slash. Empty uses the sign-in endpoint of the current Azure cloud.')
param authInstance string = ''

@description('Public base URL of the service, such as https://governance.contoso.com, with no trailing slash. Set it once a custom domain on a verified domain of the tenant is bound to the app. Empty uses the app\'s default host name.')
param customBaseUrl string = ''

// ---------------------------------------------------------------------------------------------------------------
// The product's web app, the approval runner and the directory reads (all off unless set)
// ---------------------------------------------------------------------------------------------------------------

@description('Application (client) ID of the web app registration (a single-page application with the redirect URI https://<host>/app/). Empty leaves the web app off. Tokens this client asks for are the person entry of the REST API.')
param webAppClientId string = ''

@description('The scope the web app asks for. Empty means api://<API client ID>/api.access; set it to <Application ID URI>/api.access when the API uses another Application ID URI.')
param webAppApiScope string = ''

@description('Serve the approval runner\'s routes (the Power Automate runner pulls approval requests). Turn it on once the runner flows, the runner\'s app registration and its Workload Grant exist.')
@allowed([
  'true'
  'false'
])
param approvalRunnerEnabled string = 'false'

@description('Directory reads for the gap check, role holders, owner check and role bindings: Off, or Live (on-behalf-of the signed-in admin to Microsoft Graph and the Power Platform API, with the app\'s managed identity as the API app\'s federated credential; needs the delegated permissions in README.md).')
@allowed([
  'Off'
  'Live'
])
param directoryReads string = 'Off'

// ---------------------------------------------------------------------------------------------------------------
// Azure SQL
// ---------------------------------------------------------------------------------------------------------------

@description('Database name.')
param sqlDatabaseName string = 'product'

@description('Object ID of the Azure SQL Microsoft Entra administrator. Empty uses the signed-in deployer. A group is the better choice for production.')
param sqlAdministratorObjectId string = ''

@description('Display name recorded for the Azure SQL Microsoft Entra administrator.')
param sqlAdministratorLogin string = 'product-sql-administrator'

@description('Kind of sqlAdministratorObjectId: User, Group or Application. Empty derives it from principalType for the signed-in deployer.')
param sqlAdministratorPrincipalType string = ''

@description('Public IPv4 address of the deploying machine, allowed through the SQL firewall so the post-provision migration can connect. Empty adds no rule.')
param sqlClientIpAddress string = ''

@description('Allow connections from Azure services to the SQL server. Needed while the function app has no virtual network integration.')
param sqlAllowAzureServices bool = true

@description('Serverless maximum vCores.')
param sqlMaxVCores int = 1

@description('Serverless minimum vCores, as a decimal string.')
param sqlMinCapacity string = '0.5'

@description('Minutes of inactivity before the database pauses. -1 keeps it online. The first request after a pause waits for the database to resume.')
param sqlAutoPauseDelayMinutes int = 60

@description('Maximum database size in GB.')
param sqlMaxSizeGB int = 32

@description('Backup storage redundancy.')
@allowed([
  'Local'
  'Zone'
  'Geo'
  'GeoZone'
])
param sqlBackupStorageRedundancy string = 'Geo'

@description('Send SQL security audit events to Log Analytics.')
param sqlAuditing bool = true

// ---------------------------------------------------------------------------------------------------------------
// Functions
// ---------------------------------------------------------------------------------------------------------------

@description('Maximum number of instances. Each instance keeps its own SQL connection pool, so keep it in line with the database size.')
param maximumInstanceCount int = 10

@description('Memory per instance in MB.')
@allowed([
  512
  2048
  4096
])
param instanceMemoryMB int = 2048

@description('Always ready instances for HTTP. 0 lets the app scale to zero; 1 avoids cold starts.')
param alwaysReadyInstances int = 0

// ---------------------------------------------------------------------------------------------------------------
// Telemetry
// ---------------------------------------------------------------------------------------------------------------

@secure()
@description('Telemetry salt: a random value of at least 16 characters. When set, it is stored in a new Key Vault and the app reads it through a Key Vault reference; telemetry then carries a salted caller hash. Empty sends no caller field.')
param telemetrySalt string = ''

@description('Application Insights accepts only Microsoft Entra authenticated telemetry.')
param applicationInsightsEntraOnly bool = true

@description('Days to keep logs.')
param logRetentionInDays int = 90

@description('Key Vault purge protection. Recommended for production; it cannot be turned off once on.')
param keyVaultPurgeProtection bool = false

// ---------------------------------------------------------------------------------------------------------------
// Names
// ---------------------------------------------------------------------------------------------------------------

var resourceToken = toLower(uniqueString(subscription().id, environmentName, location))
var allTags = union(tags, {
  'azd-env-name': environmentName
})
var rgName = empty(resourceGroupName) ? 'rg-${environmentName}' : resourceGroupName
var functionAppName = 'func-${resourceToken}'
var hasSalt = !empty(telemetrySalt)
var keyVaultName = hasSalt ? 'kv-${resourceToken}' : ''
var telemetrySaltSecretName = 'telemetry-salt'
var tenantIdForTokens = empty(apiTenantId) ? tenant().tenantId : apiTenantId
var signInAuthority = empty(authInstance) ? environment().authentication.loginEndpoint : authInstance
var sqlAdminObjectId = empty(sqlAdministratorObjectId) ? principalId : sqlAdministratorObjectId
var sqlAdminType = !empty(sqlAdministratorPrincipalType)
  ? sqlAdministratorPrincipalType
  : (contains(['serviceprincipal', 'application'], toLower(principalType)) ? 'Application' : 'User')

// ---------------------------------------------------------------------------------------------------------------
// Resources
// ---------------------------------------------------------------------------------------------------------------

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: rgName
  location: location
  tags: allTags
}

module monitoring 'modules/monitoring.bicep' = {
  scope: rg
  name: 'monitoring'
  params: {
    logAnalyticsName: 'log-${resourceToken}'
    applicationInsightsName: 'appi-${resourceToken}'
    location: location
    tags: allTags
    retentionInDays: logRetentionInDays
    disableLocalAuth: applicationInsightsEntraOnly
  }
}

module storage 'modules/storage.bicep' = {
  scope: rg
  name: 'storage'
  params: {
    name: 'st${resourceToken}'
    location: location
    tags: allTags
    deploymentContainerName: 'deployment-package'
  }
}

module sql 'modules/sql.bicep' = {
  scope: rg
  name: 'sql'
  params: {
    serverName: 'sql-${resourceToken}'
    databaseName: sqlDatabaseName
    location: location
    tags: allTags
    administratorLogin: sqlAdministratorLogin
    administratorObjectId: sqlAdminObjectId
    administratorPrincipalType: sqlAdminType
    allowAzureServices: sqlAllowAzureServices
    clientIpAddress: sqlClientIpAddress
    publicNetworkAccess: 'Enabled'
    maxVCores: sqlMaxVCores
    minCapacity: sqlMinCapacity
    autoPauseDelayMinutes: sqlAutoPauseDelayMinutes
    maxSizeGB: sqlMaxSizeGB
    backupStorageRedundancy: sqlBackupStorageRedundancy
    enableAuditing: sqlAuditing
    logAnalyticsWorkspaceId: monitoring.outputs.logAnalyticsId
  }
}

module keyVault 'modules/keyvault.bicep' = if (hasSalt) {
  scope: rg
  name: 'keyvault'
  params: {
    name: 'kv-${resourceToken}'
    location: location
    tags: allTags
    softDeleteRetentionInDays: 90
    enablePurgeProtection: keyVaultPurgeProtection
    telemetrySaltSecretName: telemetrySaltSecretName
    telemetrySalt: telemetrySalt
  }
}

module directoryIdentity 'modules/directory-identity.bicep' = if (directoryReads == 'Live') {
  scope: rg
  name: 'directory-identity'
  params: {
    name: 'id-directory-${resourceToken}'
    location: location
    tags: allTags
  }
}

module functionApp 'modules/function-app.bicep' = {
  scope: rg
  name: 'function-app'
  params: {
    planName: 'plan-${resourceToken}'
    name: functionAppName
    location: location
    tags: allTags
    azdServiceName: 'api'
    deploymentBlobEndpoint: storage.outputs.blobEndpoint
    deploymentContainerName: storage.outputs.deploymentContainerName
    maximumInstanceCount: maximumInstanceCount
    instanceMemoryMB: instanceMemoryMB
    alwaysReadyInstances: alwaysReadyInstances
    userAssignedIdentityId: directoryReads == 'Live' ? directoryIdentity!.outputs.id : ''
  }
}

module roles 'modules/role-assignments.bicep' = {
  scope: rg
  name: 'role-assignments'
  params: {
    principalId: functionApp.outputs.principalId
    storageAccountName: storage.outputs.name
    applicationInsightsName: monitoring.outputs.applicationInsightsName
    keyVaultName: hasSalt ? keyVault!.outputs.name : ''
  }
}

module appSettings 'modules/function-app-settings.bicep' = {
  scope: rg
  name: 'function-app-settings'
  params: {
    functionAppName: functionApp.outputs.name
    storageAccountName: storage.outputs.name
    applicationInsightsConnectionString: monitoring.outputs.applicationInsightsConnectionString
    applicationInsightsEntraOnly: applicationInsightsEntraOnly
    sqlServerFqdn: sql.outputs.serverFqdn
    sqlDatabaseName: sql.outputs.databaseName
    publicBaseUrl: customBaseUrl
    authInstance: signInAuthority
    authTenantId: tenantIdForTokens
    apiClientId: apiClientId
    apiAudience: apiAudience
    keyVaultName: keyVaultName
    telemetrySaltSecretName: telemetrySaltSecretName
    webAppClientId: webAppClientId
    webAppApiScope: webAppApiScope
    approvalRunnerEnabled: approvalRunnerEnabled
    directoryReads: directoryReads
    directoryIdentityClientId: directoryReads == 'Live' ? directoryIdentity!.outputs.clientId : ''
  }
  dependsOn: [
    roles
  ]
}

// ---------------------------------------------------------------------------------------------------------------
// Outputs (azd stores them in the environment; the post-provision hook reads the SQL and identity values)
// ---------------------------------------------------------------------------------------------------------------

output AZURE_LOCATION string = location
output AZURE_TENANT_ID string = tenant().tenantId
output AZURE_RESOURCE_GROUP string = rg.name

output AZURE_FUNCTION_APP_NAME string = functionApp.outputs.name
output PRODUCT_PUBLIC_BASE_URL string = appSettings.outputs.publicBaseUrl
output PRODUCT_MCP_URL string = '${appSettings.outputs.publicBaseUrl}/mcp'
output PRODUCT_PROTECTED_RESOURCE_METADATA_URL string = '${appSettings.outputs.publicBaseUrl}/.well-known/oauth-protected-resource/mcp'
output PRODUCT_DEFAULT_HOST_NAME string = functionApp.outputs.defaultHostName

// The app's identity: its display name is the database user name the migration creates.
output PRODUCT_APP_IDENTITY_NAME string = functionApp.outputs.name
output PRODUCT_APP_IDENTITY_PRINCIPAL_ID string = functionApp.outputs.principalId

// The web app: its redirect URI for the single-page application registration.
output PRODUCT_WEB_APP_URL string = '${appSettings.outputs.publicBaseUrl}/app/'

// The federated identity credential the API app registration needs for the directory reads (directoryReads Live): the
// directory identity as the issuer's subject, so the on-behalf-of exchange needs no client secret. Empty when off.
output PRODUCT_FEDERATED_CREDENTIAL_ISSUER string = directoryReads == 'Live' ? '${signInAuthority}${tenantIdForTokens}/v2.0' : ''
output PRODUCT_FEDERATED_CREDENTIAL_SUBJECT string = directoryReads == 'Live' ? directoryIdentity!.outputs.principalId : ''
output PRODUCT_FEDERATED_CREDENTIAL_AUDIENCE string = directoryReads == 'Live' ? 'api://AzureADTokenExchange' : ''

// The approval runner's routes, for the runner flows' ApiBaseUrl environment variable.
output PRODUCT_APPROVAL_RUNNER_BASE_URL string = appSettings.outputs.publicBaseUrl

output AZURE_SQL_SERVER_NAME string = sql.outputs.serverName
output AZURE_SQL_SERVER_FQDN string = sql.outputs.serverFqdn
output AZURE_SQL_DATABASE_NAME string = sql.outputs.databaseName

output APPLICATIONINSIGHTS_NAME string = monitoring.outputs.applicationInsightsName
output AZURE_KEY_VAULT_NAME string = keyVaultName
