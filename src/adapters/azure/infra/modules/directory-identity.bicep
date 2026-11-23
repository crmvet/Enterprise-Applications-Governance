// The user-assigned managed identity of the directory reads. The function app carries it next to its system-assigned
// identity, and the API app registration trusts it through a federated identity credential (README.md), so the
// on-behalf-of exchange to Microsoft Graph and the Power Platform API needs no client secret. Microsoft Entra accepts
// only a user-assigned managed identity as an app registration's federated credential. It holds no Azure role.

@description('Identity name.')
param name string

@description('Azure region.')
param location string

@description('Tags for every resource.')
param tags object

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: name
  location: location
  tags: tags
}

output id string = identity.id
output clientId string = identity.properties.clientId
output principalId string = identity.properties.principalId
