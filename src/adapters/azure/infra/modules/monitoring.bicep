// Log Analytics workspace and a workspace-based Application Insights resource.
// Local (key based) ingestion is off by default, so only Microsoft Entra identities with the Monitoring Metrics
// Publisher role (the function app) can send telemetry.

@description('Log Analytics workspace name.')
param logAnalyticsName string

@description('Application Insights name.')
param applicationInsightsName string

@description('Azure region.')
param location string

@description('Tags for every resource.')
param tags object

@description('Days to keep logs in the workspace.')
@minValue(30)
@maxValue(730)
param retentionInDays int

@description('Turn off key based ingestion so telemetry needs a Microsoft Entra token.')
param disableLocalAuth bool

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: retentionInDays
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
  }
}

resource applicationInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: applicationInsightsName
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
    IngestionMode: 'LogAnalytics'
    DisableLocalAuth: disableLocalAuth
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

output logAnalyticsId string = logAnalytics.id
output applicationInsightsId string = applicationInsights.id
output applicationInsightsName string = applicationInsights.name
output applicationInsightsConnectionString string = applicationInsights.properties.ConnectionString
