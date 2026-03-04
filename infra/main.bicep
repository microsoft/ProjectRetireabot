targetScope = 'resourceGroup'

metadata name = 'Azure Evergreen'
metadata description = '''This module contains all the components needed to deploy Azure Evergreen onto your subscription.

>**Note:** This module is currently considered a Proof-Of-Concept. Please review the module and its functionality to see if it matches your and or your customer's use case.
'''

@description('Optional. A unique application/solution name for all resources in this deployment. This should be 3-16 characters long.')
@minLength(3)
@maxLength(16)
param deploymentName string = 'azeg'

@maxLength(5)
@description('Optional. A unique text value for the solution. This is used to ensure resource names are unique for global resources. Defaults to a 5-character substring of the unique string generated from the subscription ID, resource group name, and solution name.')
param deploymentUniqueText string = take(uniqueString(subscription().id, resourceGroup().name, deploymentName), 5)

@metadata({ azd: { type: 'location' } })
param location string

@description('The PAT that allows EverGreen to interact with your GitHub repository.')
param githubPAT string

@description('Username of the repository belongs to')
param githubUsername string

@description('Name of the target repository only')
param githubRepository string


var deploymentSuffix = toLower(trim(replace(
  replace(
    replace(replace(replace(replace('${deploymentName}${deploymentUniqueText}', '-', ''), '_', ''), '.', ''), '/', ''),
    ' ',
    ''
  ),
  '*',
  ''
)))

var logAnalyticsWorkspaceResourceName = 'log-${deploymentSuffix}'
module logAnalyticsWS 'br/public:avm/res/operational-insights/workspace:0.15.0' = {
  name: take('avm.res.operational-insights.workspace.${logAnalyticsWorkspaceResourceName}', 64)
  params: {
    name: logAnalyticsWorkspaceResourceName
    location: location
    skuName: 'PerGB2018'
    dataRetention: 365
    features: { enableLogAccessUsingOnlyResourcePermissions: true }
    diagnosticSettings: [{ useThisWorkspace: true }]
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
    dataSources:  [
          {
            eventLogName: 'Application'
            eventTypes: [
              {
                eventType: 'Error'
              }
              {
                eventType: 'Warning'
              }
              {
                eventType: 'Information'
              }
            ]
            kind: 'WindowsEvent'
            name: 'applicationEvent'
          }
          {
            counterName: '% Processor Time'
            instanceName: '*'
            intervalSeconds: 60
            kind: 'WindowsPerformanceCounter'
            name: 'windowsPerfCounter1'
            objectName: 'Processor'
          }
          {
            kind: 'IISLogs'
            name: 'sampleIISLog1'
            state: 'OnPremiseEnabled'
          }
        ]
  }
}

var applicationInsightsResourceName = 'appi-${deploymentSuffix}'
module applicationInsights 'br/public:avm/res/insights/component:0.6.0' =  {
  name: take('avm.res.insights.component.${applicationInsightsResourceName}', 64)
  params: {
    name: applicationInsightsResourceName
    location: location
    retentionInDays: 365
    kind: 'web'
    disableIpMasking: false
    flowType: 'Bluefield'
    // WAF aligned configuration for Monitoring
    workspaceResourceId: logAnalyticsWS!.outputs.resourceId
    diagnosticSettings: [{ workspaceResourceId: logAnalyticsWS!.outputs.resourceId  }] 
  }
}

var managedIdentityResourceName = 'uai-${deploymentSuffix}'
module userAssignedIdentity 'br/public:avm/res/managed-identity/user-assigned-identity:0.5.0' = {
  name: take('avm.res.managed-identity.uai.${managedIdentityResourceName}', 64)
  params: {
    // Required parameters
    name: managedIdentityResourceName
    // Non-required parameters
    location: location
  }
}

var storageAccountResourceName = 'jobstore${deploymentSuffix}'
module storageAccount 'br/public:avm/res/storage/storage-account:0.31.2' = {
  name: take('avm.storage.storage-account.${storageAccountResourceName}', 64)
  params: {
    // Required parameters
    name: storageAccountResourceName
    location: location
    // Non-required parameters
    kind: 'StorageV2'
    skuName: 'Standard_LRS'
    accessTier: 'Hot'

    roleAssignments: [
      {
        principalId: userAssignedIdentity!.outputs.principalId
        roleDefinitionIdOrName: 'Storage Blob Data Owner'
        principalType: 'ServicePrincipal'
      }
      {
        principalId: userAssignedIdentity!.outputs.principalId
        roleDefinitionIdOrName: 'Storage Queue Data Contributor'
        principalType: 'ServicePrincipal'
      }
      {
        principalId: userAssignedIdentity!.outputs.principalId
        roleDefinitionIdOrName: 'Storage Table Data Contributor'
        principalType: 'ServicePrincipal'
      }
    ]
  }
}

var functionServerFarmResourceName = 'asp-${deploymentSuffix}'
module serverfarm 'br/public:avm/res/web/serverfarm:0.7.0' = {
  name: take('avm.res.web.serverfarm.${functionServerFarmResourceName}', 64)
  params: {
    // Required parameters
    name: functionServerFarmResourceName
    location: location
    reserved: false
    skuName: 'Y1'
    diagnosticSettings: [
      { 
        workspaceResourceId: logAnalyticsWS!.outputs.resourceId 
      }
    ]
    zoneRedundant: false
  }
}

var functionSiteResourceName = 'func-${deploymentSuffix}'
module site 'br/public:avm/res/web/site:0.22.0' = {
  name: take('avm.res.web.site.${functionSiteResourceName}', 64)
  params: {
    // Required parameters
    kind: 'functionapp,linux'
    name: functionSiteResourceName
    serverFarmResourceId: serverfarm!.outputs.resourceId
    location: location
    managedIdentities: {
      userAssignedResourceIds: [
        userAssignedIdentity!.outputs.resourceId
      ]
    }
    siteConfig: {
      appSettings: [
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: applicationInsights!.outputs.instrumentationKey
        }
        {
          name: 'AzureWebJobsStorage__blobServiceUri'
          value: storageAccount!.outputs.primaryBlobEndpoint

        }
        {
          name: 'AzureWebJobsStorage__credential'
          value: 'managedidentity'

        }
        {
          name: 'AzureWebJobsStorage__queueServiceUri'
          value: storageAccount!.outputs.serviceEndpoints.queue

        }
        {
          name: 'AzureWebJobsStorage__tableServiceUri'
          value: storageAccount!.outputs.serviceEndpoints.table
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
        }
        {
          name: 'GITHUB_PAT'
          value: githubPAT
        }
        {
          name: 'REPOSITORY_NAME'
          value: githubRepository
        }
        {
          name: 'REPOSITORY_OWNER'
          value: githubUsername
        }
        {
          name: 'WEBSITE_ENABLE_SYNC_UPDATE_SITE'
          value: 'true'
        }
        {
          name: 'WEBSITE_RUN_FROM_PACKAGE'
          value: '1'
        }
        {
          name: 'WEBSITE_USE_PLACEHOLDER_DOTNETISOLATED'
          value: '1'
        }
      ]
    }
  }
}
