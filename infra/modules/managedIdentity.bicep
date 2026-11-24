targetScope = 'resourceGroup'

import {
  AzureDevOpsOptions
  PowerBIOptions
  ScanOptions
} from 'types.bicep'
import { parseScanScope } from 'scanScopes.bicep'

param deploymentSuffix string
param location string

param storageAccountName string
param keyVaultName string
param applicationInsightsName string

param workItemBackend string[]

param azureDevOpsOptions AzureDevOpsOptions
param powerBIOptions PowerBIOptions
param scanOptions ScanOptions

param dataSinkBackend string[]
param gitHubParamCount int

var effectiveScanScopes = map(scanOptions.scopes, id => parseScanScope(id))
var readerSubscriptions = scanOptions.assignReader
  ? filter(effectiveScanScopes, scope => empty(scope.resourceGroupName))
  : []
var readerResourceGroups = scanOptions.assignReader
  ? filter(effectiveScanScopes, scope => !empty(scope.resourceGroupName))
  : []

var storageBlobDataOwnerRoleId = 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
var storageBlobDataContributorRoleId = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
var storageQueueDataContributorId = '974c5e8b-45b9-4653-ba55-5f855dd0fb88'
var storageTableDataContributorId = '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'
var monitoringMetricsPublisherId = '3913510d-42f4-4e42-8a64-420c390055eb'

var keyvaultCertificateUserId = 'db79e9a7-68ee-4b58-9aeb-b90e7c24fcba'
var keyvaultCryptoUserId = '12338af0-0e69-4776-bea7-57ae8d297424'
var keyvaultSecretsUserId = '4633458b-17de-408a-b874-0445c86b69e6'

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageAccountName
}

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

var managedIdentityResourceName = 'uai-${deploymentSuffix}'
resource userAssignedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: managedIdentityResourceName
  location: location
}

resource applicationInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: applicationInsightsName
}

var subscriptionRoleAssignmentsName = 'sra-${deploymentSuffix}-${uniqueString(location)}'
module subscriptionRoleAssignments 'subscriptionRoleAssignments.bicep' = [
  for readerScope in readerSubscriptions: {
    name: readerScope.subscriptionId == subscription().subscriptionId
      ? subscriptionRoleAssignmentsName
      : 'sra-${deploymentSuffix}-${uniqueString(readerScope.subscriptionId)}'
    scope: subscription(readerScope.subscriptionId)
    params: {
      principalId: userAssignedIdentity.properties.principalId
    }
  }
]

module resourceGroupRoleAssignments 'resourceGroupRoleAssignments.bicep' = [
  for readerScope in readerResourceGroups: {
    name: 'rg-reader-${deploymentSuffix}-${uniqueString(readerScope.subscriptionId, readerScope.resourceGroupName)}'
    scope: resourceGroup(readerScope.subscriptionId, readerScope.resourceGroupName)
    params: {
      principalId: userAssignedIdentity.properties.principalId
    }
  }
]

resource roleAssignmentBlobDataOwner 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(subscription().id, storageAccount.id, userAssignedIdentity.id, 'Storage Blob Data Owner')
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataOwnerRoleId)
    principalId: userAssignedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource roleAssignmentBlob 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(subscription().id, storageAccount.id, userAssignedIdentity.id, 'Storage Blob Data Contributor')
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      storageBlobDataContributorRoleId
    )
    principalId: userAssignedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource roleAssignmentQueueStorage 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(subscription().id, storageAccount.id, userAssignedIdentity.id, 'Storage Queue Data Contributor')
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageQueueDataContributorId)
    principalId: userAssignedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource roleAssignmentTableStorage 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(subscription().id, storageAccount.id, userAssignedIdentity.id, 'Storage Table Data Contributor')
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageTableDataContributorId)
    principalId: userAssignedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource roleAssignmentAppInsights 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(subscription().id, applicationInsights.id, userAssignedIdentity.id, 'Monitoring Metrics Publisher')
  scope: applicationInsights
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', monitoringMetricsPublisherId)
    principalId: userAssignedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource roleAssignmentKeyVaultReader 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(subscription().id, vault.id, userAssignedIdentity.id, 'Key Vault Secrets User')
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyvaultSecretsUserId)
    principalId: userAssignedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource roleAssignmentKeyVaultCertificate 'Microsoft.Authorization/roleAssignments@2022-04-01' = if ((contains(
  workItemBackend,
  'AzureDevOps'
) && !empty(azureDevOpsOptions.certificateId)) || (contains(dataSinkBackend, 'PowerBI') && !empty(powerBIOptions.certificateId))) {
  name: guid(subscription().id, vault.id, userAssignedIdentity.id, 'Key Vault Certificate User')
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyvaultCertificateUserId)
    principalId: userAssignedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource roleAssignmentKeyVaultCrypto 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (contains(
  workItemBackend,
  'GitHub'
) && gitHubParamCount == 4) {
  name: guid(subscription().id, vault.id, userAssignedIdentity.id, 'Key Vault Crypto User')
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyvaultCryptoUserId)
    principalId: userAssignedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

output resourceId string = userAssignedIdentity.id
output principalId string = userAssignedIdentity.properties.principalId
output clientId string = userAssignedIdentity.properties.clientId

output readerResourceGroupIds string[] = [
  for readerScope in readerResourceGroups: '/subscriptions/${readerScope.subscriptionId}/resourceGroups/${readerScope.resourceGroupName}'
]
