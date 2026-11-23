// Azure SQL logical server with Microsoft Entra-only authentication (no SQL logins, no passwords) and one serverless
// General Purpose database. The Microsoft Entra administrator is the deploying person (or a group); the function
// app's managed identity becomes a contained database user later, when the post-provision hook runs the migration
// scripts as that administrator.

@description('Logical server name.')
param serverName string

@description('Database name.')
param databaseName string

@description('Azure region.')
param location string

@description('Tags for every resource.')
param tags object

@description('Display name recorded for the Microsoft Entra administrator. Azure SQL matches the administrator by object ID.')
param administratorLogin string

@description('Object ID of the Microsoft Entra administrator: a user, a group or a service principal.')
param administratorObjectId string

@description('Kind of the Microsoft Entra administrator.')
@allowed([
  'User'
  'Group'
  'Application'
])
param administratorPrincipalType string

@description('Allow connections from Azure services. Needed while the function app has no virtual network integration.')
param allowAzureServices bool

@description('Public IPv4 address allowed to connect, for the post-provision migration from the deploying machine. Empty adds no rule.')
param clientIpAddress string

@description('Public network access to the logical server.')
@allowed([
  'Enabled'
  'Disabled'
])
param publicNetworkAccess string

@description('Serverless maximum vCores.')
@allowed([
  1
  2
  4
  6
  8
  10
  12
  14
  16
])
param maxVCores int

@description('Serverless minimum vCores, as a decimal string such as 0.5.')
param minCapacity string

@description('Minutes of inactivity before the database pauses. -1 keeps it online.')
param autoPauseDelayMinutes int

@description('Maximum database size in GB.')
@minValue(1)
@maxValue(4096)
param maxSizeGB int

@description('Backup storage redundancy.')
@allowed([
  'Local'
  'Zone'
  'Geo'
  'GeoZone'
])
param backupStorageRedundancy string

@description('Send SQL security audit events (sign-ins, schema, permission and principal changes) to the Log Analytics workspace.')
param enableAuditing bool

@description('Log Analytics workspace resource ID for audit events.')
param logAnalyticsWorkspaceId string

resource server 'Microsoft.Sql/servers@2023-08-01' = {
  name: serverName
  location: location
  tags: tags
  properties: {
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: publicNetworkAccess
    restrictOutboundNetworkAccess: 'Disabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: administratorLogin
      sid: administratorObjectId
      tenantId: tenant().tenantId
      principalType: administratorPrincipalType
    }
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: server
  name: databaseName
  location: location
  tags: tags
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: maxVCores
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    autoPauseDelay: autoPauseDelayMinutes
    minCapacity: json(minCapacity)
    maxSizeBytes: maxSizeGB * 1073741824
    zoneRedundant: false
    requestedBackupStorageRedundancy: backupStorageRedundancy
  }
}

resource allowAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01' = if (allowAzureServices) {
  parent: server
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource allowClient 'Microsoft.Sql/servers/firewallRules@2023-08-01' = if (!empty(clientIpAddress)) {
  parent: server
  name: 'deploying-machine'
  properties: {
    startIpAddress: clientIpAddress
    endIpAddress: clientIpAddress
  }
}

// Server-level auditing to Log Analytics goes through the master database's diagnostic setting.
resource master 'Microsoft.Sql/servers/databases@2023-08-01' existing = {
  parent: server
  name: 'master'
}

resource auditDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = if (enableAuditing) {
  scope: master
  name: 'sql-security-audit'
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      {
        category: 'SQLSecurityAuditEvents'
        enabled: true
      }
    ]
  }
}

// Security-relevant events only. Statement text is not audited, so row contents never reach the logs.
resource auditing 'Microsoft.Sql/servers/auditingSettings@2023-08-01' = if (enableAuditing) {
  parent: server
  name: 'default'
  properties: {
    state: 'Enabled'
    isAzureMonitorTargetEnabled: true
    auditActionsAndGroups: [
      'SUCCESSFUL_DATABASE_AUTHENTICATION_GROUP'
      'FAILED_DATABASE_AUTHENTICATION_GROUP'
      'DATABASE_PRINCIPAL_CHANGE_GROUP'
      'DATABASE_ROLE_MEMBER_CHANGE_GROUP'
      'DATABASE_PERMISSION_CHANGE_GROUP'
      'SCHEMA_OBJECT_CHANGE_GROUP'
      'SCHEMA_OBJECT_PERMISSION_CHANGE_GROUP'
    ]
  }
  dependsOn: [
    auditDiagnostics
  ]
}

output serverName string = server.name
output serverFqdn string = server.properties.fullyQualifiedDomainName
output databaseName string = database.name
