// Key Vault for the telemetry salt (the secret the MCP server mixes into caller hashes in telemetry). Deployed only
// when a salt is given. Access is by Azure RBAC; the function app reads the secret through a Key Vault reference in
// its app settings, with its own managed identity.

@description('Key Vault name: 3 to 24 letters, digits and hyphens.')
@minLength(3)
@maxLength(24)
param name string

@description('Azure region.')
param location string

@description('Tags for every resource.')
param tags object

@description('Days a deleted vault or secret can be recovered.')
@minValue(7)
@maxValue(90)
param softDeleteRetentionInDays int

@description('Purge protection. Once on, it cannot be turned off, and a deleted vault keeps its name until the retention ends.')
param enablePurgeProtection bool

@description('Name of the telemetry salt secret.')
param telemetrySaltSecretName string

@secure()
@description('The telemetry salt: at least 16 characters, random.')
param telemetrySalt string

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    tenantId: tenant().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: softDeleteRetentionInDays
    enablePurgeProtection: enablePurgeProtection ? true : null
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      // The function app resolves the Key Vault reference over the public endpoint while it has no virtual network.
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }
  }
}

resource telemetrySaltSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: vault
  name: telemetrySaltSecretName
  properties: {
    value: telemetrySalt
    contentType: 'text/plain'
  }
}

output id string = vault.id
output name string = vault.name
output telemetrySaltSecretName string = telemetrySaltSecret.name
