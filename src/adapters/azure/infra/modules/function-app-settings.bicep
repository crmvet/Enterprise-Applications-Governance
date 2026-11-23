// Every app setting of the function app, written in one place after the identity has its role assignments.
// No setting holds a secret: storage, Azure SQL and Application Insights use the app's managed identity, and the
// telemetry salt is a Key Vault reference. This resource replaces the full set on every provision, so settings added
// by hand in the portal do not survive "azd provision"; add them here instead.

@description('Function app name.')
param functionAppName string

@description('Storage account name for the host (AzureWebJobsStorage).')
param storageAccountName string

@description('Application Insights connection string.')
param applicationInsightsConnectionString string

@description('Application Insights accepts only Microsoft Entra authenticated telemetry.')
param applicationInsightsEntraOnly bool

@description('Fully qualified domain name of the Azure SQL logical server.')
param sqlServerFqdn string

@description('Azure SQL database name.')
param sqlDatabaseName string

@description('Public base URL of the service, with no trailing slash. Empty uses the app\'s default host name.')
param publicBaseUrl string

@description('Microsoft Entra sign-in authority host.')
param authInstance string

@description('Microsoft Entra tenant ID that issues the tokens the API accepts.')
param authTenantId string

@description('Application (client) ID of the API app registration.')
param apiClientId string

@description('Accepted token audience. Empty means the API client ID, which is the audience of v2 access tokens.')
param apiAudience string

@description('Key Vault name holding the telemetry salt. Empty means no salt, so telemetry carries no caller hash.')
param keyVaultName string

@description('Name of the telemetry salt secret.')
param telemetrySaltSecretName string

resource site 'Microsoft.Web/sites@2024-04-01' existing = {
  name: functionAppName
}

var baseUrl = empty(publicBaseUrl) ? 'https://${site.properties.defaultHostName}' : publicBaseUrl

var coreSettings = {
  // Host storage with the system-assigned identity (no account key).
  AzureWebJobsStorage__accountName: storageAccountName
  AzureWebJobsStorage__credential: 'managedidentity'
  // The catch-all route also answers the root path, so the platform's default home page is off.
  AzureWebJobsDisableHomepage: 'true'
  APPLICATIONINSIGHTS_CONNECTION_STRING: applicationInsightsConnectionString
  // Azure SQL with the app's managed identity: no user name, no password.
  ConnectionStrings__Product: 'Server=tcp:${sqlServerFqdn},1433;Database=${sqlDatabaseName};Authentication=Active Directory Managed Identity;Encrypt=True;TrustServerCertificate=False;Connect Timeout=60;'
  Product__PublicBaseUrl: baseUrl
  Product__Auth__Instance: authInstance
  Product__Auth__TenantId: authTenantId
  Product__Auth__ClientId: apiClientId
  Product__Auth__Audience: empty(apiAudience) ? apiClientId : apiAudience
}

var entraTelemetrySettings = applicationInsightsEntraOnly
  ? {
      APPLICATIONINSIGHTS_AUTHENTICATION_STRING: 'Authorization=AAD'
    }
  : {}

var saltSettings = empty(keyVaultName)
  ? {}
  : {
      Product__TelemetrySalt: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=${telemetrySaltSecretName})'
    }

resource appSettings 'Microsoft.Web/sites/config@2024-04-01' = {
  parent: site
  name: 'appsettings'
  properties: union(coreSettings, entraTelemetrySettings, saltSettings)
}

output publicBaseUrl string = baseUrl
