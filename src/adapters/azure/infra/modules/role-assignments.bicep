// Azure role assignments for the function app's system-assigned managed identity, each on one resource only:
// - Storage Blob Data Owner on the storage account: host storage and the deployment package.
// - Storage Table Data Contributor on the storage account: the host's diagnostic events.
// - Monitoring Metrics Publisher on Application Insights: telemetry ingestion with Microsoft Entra authentication.
// - Key Vault Secrets User on the vault, when there is one: the telemetry salt reference.

@description('Object ID of the function app\'s managed identity.')
param principalId string

@description('Storage account name.')
param storageAccountName string

@description('Application Insights name.')
param applicationInsightsName string

@description('Key Vault name. Empty when no vault was deployed.')
param keyVaultName string

var storageBlobDataOwner = 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
var storageTableDataContributor = '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'
var monitoringMetricsPublisher = '3913510d-42f4-4e42-8a64-420c390055eb'
var keyVaultSecretsUser = '4633458b-17de-408a-b874-0445c86b69e6'

// A placeholder keeps every expression well formed when no vault exists; the vault assignment is skipped then.
var vaultName = empty(keyVaultName) ? 'no-key-vault' : keyVaultName

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageAccountName
}

resource applicationInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: applicationInsightsName
}

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: vaultName
}

resource blobOwner 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, principalId, storageBlobDataOwner)
  properties: {
    principalId: principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataOwner)
  }
}

resource tableContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, principalId, storageTableDataContributor)
  properties: {
    principalId: principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageTableDataContributor)
  }
}

resource metricsPublisher 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: applicationInsights
  name: guid(applicationInsights.id, principalId, monitoringMetricsPublisher)
  properties: {
    principalId: principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', monitoringMetricsPublisher)
  }
}

resource secretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(keyVaultName)) {
  scope: vault
  name: guid(vault.id, principalId, keyVaultSecretsUser)
  properties: {
    principalId: principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUser)
  }
}
