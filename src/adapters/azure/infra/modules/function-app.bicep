// The Flex Consumption plan and the function app (Linux, .NET 10 isolated worker) with a system-assigned managed
// identity. The app reads its code package from the deployment container with that identity. App settings are written
// by function-app-settings.bicep after the identity has its role assignments, because they name the app's own host
// name and the Key Vault secret it may read.

@description('Flex Consumption plan name.')
param planName string

@description('Function app name. It is also the display name of the app\'s managed identity and its Azure SQL user.')
param name string

@description('Azure region.')
param location string

@description('Tags for every resource.')
param tags object

@description('Service name in azure.yaml that azd deploys to this app.')
param azdServiceName string

@description('Blob endpoint of the deployment storage account, ending with a slash.')
param deploymentBlobEndpoint string

@description('Blob container that holds the deployment package.')
param deploymentContainerName string

@description('Maximum number of instances the app scales out to.')
@minValue(1)
@maxValue(1000)
param maximumInstanceCount int

@description('Memory per instance in MB.')
@allowed([
  512
  2048
  4096
])
param instanceMemoryMB int

@description('Always ready instances for the HTTP trigger group. 0 lets the app scale to zero.')
@minValue(0)
param alwaysReadyInstances int

@description('Resource ID of a user-assigned managed identity the app also carries (the directory reads\' federated credential). Empty adds none.')
param userAssignedIdentityId string = ''

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: planName
  location: location
  tags: tags
  kind: 'functionapp'
  sku: {
    tier: 'FlexConsumption'
    name: 'FC1'
  }
  properties: {
    reserved: true
  }
}

resource site 'Microsoft.Web/sites@2024-04-01' = {
  name: name
  location: location
  tags: union(tags, {
    'azd-service-name': azdServiceName
  })
  kind: 'functionapp,linux'
  identity: empty(userAssignedIdentityId)
    ? {
        type: 'SystemAssigned'
      }
    : {
        type: 'SystemAssigned, UserAssigned'
        userAssignedIdentities: {
          '${userAssignedIdentityId}': {}
        }
      }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    publicNetworkAccess: 'Enabled'
    clientAffinityEnabled: false
    siteConfig: {
      minTlsVersion: '1.2'
      scmMinTlsVersion: '1.2'
      ftpsState: 'Disabled'
      http20Enabled: true
    }
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${deploymentBlobEndpoint}${deploymentContainerName}'
          authentication: {
            type: 'SystemAssignedIdentity'
          }
        }
      }
      scaleAndConcurrency: {
        maximumInstanceCount: maximumInstanceCount
        instanceMemoryMB: instanceMemoryMB
        alwaysReady: alwaysReadyInstances > 0
          ? [
              {
                name: 'http'
                instanceCount: alwaysReadyInstances
              }
            ]
          : []
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '10.0'
      }
    }
  }
}

// No publishing passwords: azd deploys with the deploying person's Microsoft Entra sign-in.
resource ftpPolicy 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-04-01' = {
  parent: site
  name: 'ftp'
  properties: {
    allow: false
  }
}

resource scmPolicy 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-04-01' = {
  parent: site
  name: 'scm'
  properties: {
    allow: false
  }
}

output name string = site.name
output principalId string = site.identity.principalId
output defaultHostName string = site.properties.defaultHostName
