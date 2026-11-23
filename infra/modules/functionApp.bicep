targetScope = 'resourceGroup'

import {
  AdvisoryOptions
  AzureDevOpsOptions
  LifecycleSignalsOptions
  GitHubOptions
  HttpEndpointOptions
  PowerBIOptions
  ScanOptions
} from 'types.bicep'

param deploymentSuffix string
param location string

param managedIdentityResourceId string
param managedIdentityClientId string
param workspaceResourceId string
param applicationInsightsInstrumentationKey string
param applicationInsightsConnectionString string
param storageAccountName string
param keyVaultUri string

param workItemBackend string[]
param dataSinkBackend string[]
param timerTrigger string
param advisoryOptions AdvisoryOptions
param azureDevOpsOptions AzureDevOpsOptions
param gitHubOptions GitHubOptions
param lifecycleSignalsOptions LifecycleSignalsOptions
param httpEndpoint HttpEndpointOptions
param powerBIOptions PowerBIOptions
param scanOptions ScanOptions
param gitHubParamCount int

var functionServerFarmResourceName = 'asp-${deploymentSuffix}'
resource serverfarm 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: functionServerFarmResourceName
  location: location
  kind: 'functionapp'
  sku: {
    tier: 'Dynamic'
    name: 'Y1'
  }
  properties: {
    reserved: false
  }
}

var functionSiteResourceName = 'func-${deploymentSuffix}'
resource site 'Microsoft.Web/sites@2024-04-01' = {
  name: functionSiteResourceName
  location: location
  kind: 'functionapp'
  tags: {
    'azd-service-name': 'retireabot'
  }
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${managedIdentityResourceId}': {}
    }
  }
  properties: {
    serverFarmId: serverfarm.id
    clientAffinityEnabled: false
    httpsOnly: true
    keyVaultReferenceIdentity: managedIdentityResourceId
    siteConfig: {
      netFrameworkVersion: 'v10.0'
      use32BitWorkerProcess: false
      appSettings: union(
        [
          { name: 'Scan__Mode', value: scanOptions.mode }
          { name: 'Scan__Scopes', value: string(scanOptions.mode == 'Explicit' ? scanOptions.scopes : []) }
          { name: 'Scan__ResolvedScopes', value: string(scanOptions.scopes) }
          { name: 'Scan__DeploymentSubscriptionId', value: subscription().subscriptionId }
          { name: 'Scan__AssignReader', value: string(scanOptions.assignReader) }
          {
            name: 'APPINSIGHTS_INSTRUMENTATIONKEY'
            value: applicationInsightsInstrumentationKey
          }
          {
            name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
            value: applicationInsightsConnectionString
          }
          {
            name: 'APPLICATIONINSIGHTS_AUTHENTICATION_STRING'
            value: 'Authorization=AAD;ClientId=${managedIdentityClientId}'
          }
          {
            name: 'App__DataSinkBackend'
            value: join(dataSinkBackend, ',')
          }
          {
            name: 'App__HTTPEndpointEnable'
            value: httpEndpoint.enabled
          }
          {
            name: 'App__HTTPEndpointOutput'
            value: httpEndpoint.includeOutput
          }
          {
            name: 'App__HTTPEndpointWhatIf'
            value: httpEndpoint.allowWhatIf
          }
          {
            name: 'App__LifecycleSignalsEnable'
            value: lifecycleSignalsOptions.enabled
          }
          {
            name: 'App__IncludeResolvedAdvisories'
            value: advisoryOptions.includeResolvedAdvisories
          }
          {
            name: 'App__LifecycleWarningWindowDays'
            value: lifecycleSignalsOptions.warningWindowDays
          }
          {
            name: 'App__TimerTrigger'
            value: timerTrigger
          }
          {
            name: 'App__WorkItemBackend'
            value: join(workItemBackend, ',')
          }
          {
            name: 'AZURE_CLIENT_ID'
            value: managedIdentityClientId
          }
          {
            name: 'AzureWebJobsStorage__accountName'
            value: storageAccountName
          }
          {
            name: 'AzureWebJobsStorage__clientId'
            value: managedIdentityClientId
          }
          {
            name: 'AzureWebJobsStorage__credential'
            value: 'managedidentity'
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
            name: 'KeyVault__Uri'
            value: keyVaultUri
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
        ],
        gitHubParamCount != 4 || !contains(workItemBackend, 'GitHub')
          ? []
          : [
              {
                name: 'GitHub__AppId'
                value: gitHubOptions.appId
              }
              {
                name: 'GitHub__AppInstallId'
                value: gitHubOptions.installId
              }
              {
                name: 'GitHub__AppPrivateKeyId'
                value: gitHubOptions.privateKeyId
              }
            ],
        !contains(workItemBackend, 'GitHub')
          ? []
          : union(
              [
                { name: 'GitHub__AdvisoryLabel', value: gitHubOptions.workItemConfiguration.advisoryLabel }
                { name: 'GitHub__AdvisoryParentLabel', value: gitHubOptions.workItemConfiguration.advisoryParentLabel }
                { name: 'GitHub__AdvisoryLabelPrefix', value: gitHubOptions.workItemConfiguration.advisoryLabelPrefix }
                {
                  name: 'GitHub__AdvisoryParentLabelPrefix'
                  value: gitHubOptions.workItemConfiguration.advisoryParentLabelPrefix
                }
                {
                  name: 'GitHub__CreateParentWorkItems'
                  value: gitHubOptions.workItemConfiguration.createParentWorkItems
                }
                {
                  name: 'GitHub__CreateChildWorkItems'
                  value: gitHubOptions.workItemConfiguration.createChildWorkItems
                }
                {
                  name: 'GitHub__IncludeResourceId'
                  value: gitHubOptions.workItemConfiguration.includeResourceId
                }
                { name: 'GitHub__TargetRepository', value: gitHubOptions.routing.targetRepository }
                { name: 'GitHub__UnmappedRepository', value: gitHubOptions.routing.unmappedRepository }
                { name: 'GitHub__UseTriageRepoForUnmapped', value: gitHubOptions.routing.useTriageForUnmapped }
                { name: 'GitHub__AssignCopilot', value: gitHubOptions.assignCopilot }
                { name: 'GitHub__WorkItemScope', value: gitHubOptions.routing.scope }
              ],
              empty(advisoryOptions.targetResourceGroup)
                ? []
                : [{ name: 'GitHub__TargetResourceGroup', value: advisoryOptions.targetResourceGroup }],
              gitHubOptions.routing.scope == 'monolithic'
                ? []
                : [{ name: 'GitHub__TargetContainerMapping', value: string(gitHubOptions.routing.containerMappings) }]
            ),
        !contains(workItemBackend, 'AzureDevOps')
          ? []
          : union(
              [
                { name: 'AzureDevOps__OrganisationUrl', value: azureDevOpsOptions.organisationUrl }
                { name: 'AzureDevOps__AdvisoryLabel', value: azureDevOpsOptions.workItemConfiguration.advisoryLabel }
                {
                  name: 'AzureDevOps__AdvisoryParentLabel'
                  value: azureDevOpsOptions.workItemConfiguration.advisoryParentLabel
                }
                {
                  name: 'AzureDevOps__AdvisoryLabelPrefix'
                  value: azureDevOpsOptions.workItemConfiguration.advisoryLabelPrefix
                }
                {
                  name: 'AzureDevOps__AdvisoryParentLabelPrefix'
                  value: azureDevOpsOptions.workItemConfiguration.advisoryParentLabelPrefix
                }
                {
                  name: 'AzureDevOps__CreateParentWorkItems'
                  value: azureDevOpsOptions.workItemConfiguration.createParentWorkItems
                }
                {
                  name: 'AzureDevOps__CreateChildWorkItems'
                  value: azureDevOpsOptions.workItemConfiguration.createChildWorkItems
                }
                {
                  name: 'AzureDevOps__IncludeResourceId'
                  value: azureDevOpsOptions.workItemConfiguration.includeResourceId
                }
                { name: 'AzureDevOps__TargetRepository', value: azureDevOpsOptions.routing.targetRepository }
                { name: 'AzureDevOps__UnmappedRepository', value: azureDevOpsOptions.routing.unmappedRepository }
                {
                  name: 'AzureDevOps__UseTriageRepoForUnmapped'
                  value: azureDevOpsOptions.routing.useTriageForUnmapped
                }
                { name: 'AzureDevOps__WorkItemScope', value: azureDevOpsOptions.routing.scope }
              ],
              empty(advisoryOptions.targetResourceGroup)
                ? []
                : [{ name: 'AzureDevOps__TargetResourceGroup', value: advisoryOptions.targetResourceGroup }],
              azureDevOpsOptions.routing.scope == 'monolithic'
                ? []
                : [
                    {
                      name: 'AzureDevOps__TargetContainerMapping'
                      value: string(azureDevOpsOptions.routing.containerMappings)
                    }
                  ],
              empty(azureDevOpsOptions.clientId)
                ? []
                : [{ name: 'AzureDevOps__ClientId', value: azureDevOpsOptions.clientId }],
              empty(azureDevOpsOptions.tenantId)
                ? []
                : [{ name: 'AzureDevOps__TenantId', value: azureDevOpsOptions.tenantId }],
              empty(azureDevOpsOptions.certificateId)
                ? []
                : [{ name: 'AzureDevOps__CertificateId', value: azureDevOpsOptions.certificateId }],
              empty(azureDevOpsOptions.workItemDefaultAssignee)
                ? []
                : [{ name: 'AzureDevOps__WorkItemDefaultAssignee', value: azureDevOpsOptions.workItemDefaultAssignee }],
              empty(azureDevOpsOptions.workItemOpenState)
                ? []
                : [{ name: 'AzureDevOps__WorkItemOpenState', value: azureDevOpsOptions.workItemOpenState }],
              empty(azureDevOpsOptions.workItemClosedState)
                ? []
                : [{ name: 'AzureDevOps__WorkItemClosedState', value: azureDevOpsOptions.workItemClosedState }],
              empty(azureDevOpsOptions.workItemType)
                ? []
                : [{ name: 'AzureDevOps__WorkItemType', value: azureDevOpsOptions.workItemType }]
            ),
        !contains(dataSinkBackend, 'PowerBI')
          ? []
          : union(
              [
                { name: 'PowerBI__WorkspaceId', value: powerBIOptions.workspaceId }
                { name: 'PowerBI__DatasetId', value: powerBIOptions.datasetId }
                { name: 'PowerBI__TableName', value: powerBIOptions.tableName }
                { name: 'PowerBI__WriteMode', value: powerBIOptions.writeMode }
              ],
              empty(powerBIOptions.clientId) ? [] : [{ name: 'PowerBI__ClientId', value: powerBIOptions.clientId }],
              empty(powerBIOptions.tenantId) ? [] : [{ name: 'PowerBI__TenantId', value: powerBIOptions.tenantId }],
              empty(powerBIOptions.certificateId)
                ? []
                : [{ name: 'PowerBI__CertificateId', value: powerBIOptions.certificateId }]
            )
      )
    }
    redundancyMode: 'None'
    publicNetworkAccess: 'Enabled'
  }
}

resource siteDiagnosticSettings 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: '${site.name}-diagnosticSettings'
  scope: site
  properties: {
    workspaceId: workspaceResourceId
    logs: [
      {
        categoryGroup: 'allLogs'
        enabled: true
      }
    ]
    metrics: [
      {
        category: 'AllMetrics'
        enabled: true
      }
    ]
  }
}

output resourceId string = site.id
output name string = site.name
