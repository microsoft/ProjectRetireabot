targetScope = 'resourceGroup'

metadata name = 'Azure RetireaBot'
metadata description = '''This module contains all the components needed to deploy Azure RetireaBot onto your subscription.

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

@allowed([
  'GitHub'
  'AzureDevOps'
])
@description('What work item backend RetireaBot should use to create work items in')
param workItemBackend string = 'GitHub'

@secure()
@description('The PAT that allows RetireaBot to interact with your GitHub repository.')
param gitHubPAT string = ''

@description('App Id of the registered GitHub App')
param gitHubAppId string = ''

@description('The install id of the GitHub App to be used for actions on repositories')
param gitHubInstallId string = ''

@description('The id the private key for access to the GitHub App should be stored as in the KeyVault')
param gitHubPrivateKeyId string = ''

@description('The path of the private key to be stored in the KeyVault for the GitHub App')
param gitHubPrivateKeyPath string = ''

@description('The URL of the Azure DevOps organisation (e.g. https://dev.azure.com/myorg)')
param adoOrganisationUrl string = ''

@secure()
@description('The PAT that allows RetireaBot to interact with your Azure DevOps organisation.')
param adoPAT string = ''

@description('The client ID of the app registration used to authenticate with Azure DevOps')
param adoClientId string = ''

@description('The tenant ID of the app registration used to authenticate with Azure DevOps')
param adoTenantId string = ''

@secure()
@description('The client secret of the app registration used to authenticate with Azure DevOps')
param adoClientSecret string = ''

@description('The ID the certificate should be stored as in the KeyVault for Azure DevOps certificate auth')
param adoCertificateId string = ''

@description('The path of the certificate file (PFX/PEM) to be imported into the KeyVault for Azure DevOps certificate auth')
param adoCertificatePath string = ''

@description('(Optional) The default assignee for work items created by RetireaBot in Azure DevOps')
param adoWorkItemDefaultAssignee string = ''

@description('(Optional) The state to use when opening work items in Azure DevOps. Default: New')
param adoWorkItemOpenState string = ''

@description('(Optional) The state to use when closing work items in Azure DevOps. Default: Closed')
param adoWorkItemClosedState string = ''

@description('(Optional) The work item type to create in Azure DevOps. Default: Task')
param adoWorkItemType string = ''

@description('Target repository to create work items on from advisories')
param targetRepository string = ''

@description('(Optional) The resource group RetireaBot should create issues for, leave blank any resource group')
param targetResourceGroup string = ''

@description('(Optional) What label should be attached to all work items to identify it was created by RetireaBot. Default: advisor')
param advisoryLabel string = ''

@description('(Optional) What label should be attached to parent work items to identify them. Default: tracking')
param advisoryParentLabel string = ''

@description('(Optional) What prefix should be applied to label that uniquely identifies a work item based on their advisory. Default: advisor-')
param advisoryLabelPrefix string = ''

@description('(Optional) What prefix should be applied to label that uniquely identifies a parent work item based on their advisory. Default: advisor-type-')
param parentLabelPrefix string = ''

@description('(Optional) Whether parent work items should be created when processing advisories to track child work items')
param createParentWorkItems bool = true

@description('(Optional) Whether child work items should be created when processing advisories')
param createChildWorkItems bool = true

@description('(Optional) Should unmapped resources have their work items created in the triage repository/target repository in perResourceGroup mode. Default: true')
param useTriageRepoForUnmapped bool = true

@description('(Optional) The repository work items should be created in when they are not mapped in perResourceGroup mode.')
param unmappedRepository string = ''

@description('(Optional) Whether the manual HTTP endpoint is enabled. Default false')
param httpEndpointEnable bool = false

@description('(Optional) Whether the manual HTTP endpoint should display extended information about its run. Default false')
param httpEndpointOutput bool = false

@description('(Optional) Whether the manual HTTP endpoint should allow users to run dry-runs. Default false')
param httpEndpointWhatIf bool = false

@description('(Optional) Whether GitHub CoPilot should be assigned to issues created by RetireaBot')
param gitHubCoPilotAssign bool = false

@allowed(['monolithic', 'perResourceGroup'])
@description('(Optional) Whether issues should be created in one "triage" repository or should be shared across multiple repositories with a parent issue in the repository. Default "monolithic"')
param workItemScope string = 'monolithic'

@description('(Optional) If "perResourceGroup" is selected, these mappings decide which repositories issues are created based on their resource groups.')
param resourceGroupRepositoryMap array = []

var storageBlobDataOwnerRoleId = 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
var storageBlobDataContributorRoleId = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
var storageQueueDataContributorId = '974c5e8b-45b9-4653-ba55-5f855dd0fb88'
var storageTableDataContributorId = '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'
var monitoringMetricsPublisherId = '3913510d-42f4-4e42-8a64-420c390055eb'

var keyvaultCertificateUserId = 'db79e9a7-68ee-4b58-9aeb-b90e7c24fcba'
var keyvaultCryptoUserId = '12338af0-0e69-4776-bea7-57ae8d297424'
var keyvaultSecretsUserId = '4633458b-17de-408a-b874-0445c86b69e6'

var deploymentSuffix = toLower(trim(replace(
  replace(
    replace(replace(replace(replace('${deploymentName}${deploymentUniqueText}', '-', ''), '_', ''), '.', ''), '/', ''),
    ' ',
    ''
  ),
  '*',
  ''
)))

var gitHubCredentialValidation = empty(gitHubPAT) && empty(gitHubAppId) && empty(gitHubInstallId) && empty(gitHubPrivateKeyId) && workItemBackend == 'GitHub'
  ? fail('You must provide at least one way of authenticating with GitHub (PAT or App)')
  : null

var gitHubAppParamsPopulated = [
  !empty(gitHubAppId) ? 1 : 0
  !empty(gitHubInstallId) ? 1 : 0
  !empty(gitHubPrivateKeyId) ? 1 : 0
  !empty(gitHubPrivateKeyPath) ? 1 : 0
]

var gitHubParamCount = reduce(gitHubAppParamsPopulated, 0, (cur, next) => cur + next)
var gitHubParamCountValidation = !(gitHubParamCount == 0 || gitHubParamCount == 4) && workItemBackend == 'GitHub'
  ? fail('To use GitHub App authentication, you need to populate all required fields')
  : null

var adoCredentialValidation = empty(adoPAT) && empty(adoClientId) && workItemBackend == 'AzureDevOps'
  ? fail('You must provide at least one way of authenticating with Azure DevOps (PAT, or ClientId for managed identity/client secret/certificate auth)')
  : null

var adoOrganisationUrlValidation = empty(adoOrganisationUrl) && workItemBackend == 'AzureDevOps'
  ? fail('You must provide the Azure DevOps organisation URL when using the AzureDevOps backend')
  : null

var issueCheck = !createChildWorkItems && !createParentWorkItems
  ? fail('You need at least one type of work item to be created when an advisory is found.')
  : null

var keyVaultResourceName = 'kv-${deploymentSuffix}'
resource vault 'Microsoft.KeyVault/vaults@2021-10-01' = {
  name: keyVaultResourceName
  location: location
  properties: {
    createMode: 'default'
    enableRbacAuthorization: true
    enableSoftDelete: true
    enabledForDeployment: false
    enabledForDiskEncryption: false
    enabledForTemplateDeployment: false
    publicNetworkAccess: 'Enabled'
    sku: {
      family: 'A'
      name: 'standard'
    }
    softDeleteRetentionInDays: 7
    tenantId: deployer().tenantId
  }
}

resource gitHubSecret 'Microsoft.KeyVault/vaults/secrets@2025-05-01' = if (!empty(gitHubPAT) && workItemBackend == 'GitHub') {
  parent: vault
  name: 'Github--PAT'
  properties: {
    value: gitHubPAT
  }
}

resource adoPATSecret 'Microsoft.KeyVault/vaults/secrets@2025-05-01' = if (!empty(adoPAT) && workItemBackend == 'AzureDevOps') {
  parent: vault
  name: 'AzureDevOps--PAT'
  properties: {
    value: adoPAT
  }
}

resource adoClientSecretSecret 'Microsoft.KeyVault/vaults/secrets@2025-05-01' = if (!empty(adoClientSecret) && workItemBackend == 'AzureDevOps') {
  parent: vault
  name: 'AzureDevOps--ClientSecret'
  properties: {
    value: adoClientSecret
  }
}

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
    dataSources: [
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
resource applicationInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: applicationInsightsResourceName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    Flow_Type: 'Bluefield'
    WorkspaceResourceId: logAnalyticsWS.outputs.resourceId
    DisableLocalAuth: true
  }
}

var storageAccountResourceName = 'jobstore${deploymentSuffix}'
resource storageAccount 'Microsoft.Storage/storageAccounts@2021-09-01' = {
  name: storageAccountResourceName
  location: location
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    allowCrossTenantReplication: true
    allowSharedKeyAccess: false
    defaultToOAuthAuthentication: false
    encryption: {
      keySource: 'Microsoft.Storage'
      services: {
        queue: {
          keyType: 'Service'
        }
        table: {
          keyType: 'Service'
        }
      }
    }
    isHnsEnabled: false
    isNfsV3Enabled: false
    isSftpEnabled: false
    minimumTlsVersion: 'TLS1_2'
    networkAcls: {
      defaultAction: 'Allow'
    }

    publicNetworkAccess: 'Enabled'
    supportsHttpsTrafficOnly: true
  }
  tags: {
    SecurityControl: 'Ignore'
  }
  sku: {
    name: 'Standard_LRS'
  }
}

var managedIdentityResourceName = 'uai-${deploymentSuffix}'
resource userAssignedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: managedIdentityResourceName
  location: location
}

var subscriptionRoleAssignmentsName = 'sra-${deploymentSuffix}-${uniqueString(location)}'
module subscriptionRoleAssignments 'subscriptionRoleAssignments.bicep' = {
  name: subscriptionRoleAssignmentsName
  scope: subscription()
  params: {
    principalId: userAssignedIdentity.properties.principalId
  }
}

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

resource roleAssignmentKeyVaultCertificate 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (workItemBackend == 'AzureDevOps' && !empty(adoCertificateId)) {
  name: guid(subscription().id, vault.id, userAssignedIdentity.id, 'Key Vault Certificate User')
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyvaultCertificateUserId)
    principalId: userAssignedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource roleAssignmentKeyVaultCrypto 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (workItemBackend == 'GitHub' && gitHubParamCount == 4) {
  name: guid(subscription().id, vault.id, userAssignedIdentity.id, 'Key Vault Crypto User')
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyvaultCryptoUserId)
    principalId: userAssignedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
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
    kind: 'functionapp'
    name: functionSiteResourceName
    serverFarmResourceId: serverfarm!.outputs.resourceId
    location: location
    managedIdentities: {
      userAssignedResourceIds: [
        userAssignedIdentity.id
      ]
    }
    keyVaultAccessIdentityResourceId: userAssignedIdentity.id
    siteConfig: {
      appSettings: union(
        [
          {
            name: 'APPINSIGHTS_INSTRUMENTATIONKEY'
            value: applicationInsights.properties.InstrumentationKey
          }
          {
            name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
            value: applicationInsights.properties.ConnectionString
          }
          {
            name: 'App__AssignGitHubCopilot'
            value: gitHubCoPilotAssign
          }
          {
            name: 'App__AdvisoryLabel'
            value: advisoryLabel
          }
          {
            name: 'App__AdvisoryParentLabel'
            value: advisoryParentLabel
          }
          {
            name: 'App__AdvisoryLabelPrefix'
            value: advisoryLabelPrefix
          }
          {
            name: 'App__ParentLabelPrefix'
            value: parentLabelPrefix
          }
          {
            name: 'App__CreateParentWorkItems'
            value: createParentWorkItems
          }
          {
            name: 'App__CreateChildWorkItems'
            value: createChildWorkItems
          }
          {
            name: 'App__HTTPEndpointEnable'
            value: httpEndpointEnable
          }
          {
            name: 'App__HTTPEndpointOutput'
            value: httpEndpointOutput
          }
          {
            name: 'App__HTTPEndpointWhatIf'
            value: httpEndpointWhatIf
          }
          {
            name: 'App__TargetRepository'
            value: targetRepository
          }
          {
            name: 'App__UseTriageRepoForUnmapped'
            value: useTriageRepoForUnmapped
          }
          {
            name: 'App__UnmappedRepository'
            value: unmappedRepository
          }
          {
            name: 'App__WorkItemBackend'
            value: workItemBackend
          }
          {
            name: 'App__WorkItemScope'
            value: workItemScope
          }
          {
            name: 'AZURE_CLIENT_ID'
            value: userAssignedIdentity.properties.clientId
          }
          {
            name: 'AzureWebJobsStorage__accountName'
            value: storageAccount.name
          }
          {
            name: 'AzureWebJobsStorage__clientId'
            value: userAssignedIdentity.properties.clientId
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
            value: vault.properties.vaultUri
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
        gitHubParamCountValidation == null || gitHubParamCount != 4 || workItemBackend != 'GitHub'
          ? []
          : [
              {
                name: 'GitHub__AppId'
                value: gitHubAppId
              }
              {
                name: 'GitHub__AppInstallId'
                value: gitHubInstallId
              }
              {
                name: 'GitHub__AppPrivateKeyId'
                value: gitHubPrivateKeyId
              }
            ],
        empty(targetResourceGroup)
          ? []
          : [
              { name: 'App__TargetResourceGroup', value: targetResourceGroup }
            ],
        workItemScope == 'monolithic'
          ? []
          : [
              { name: 'App__TargetResourceGroupMapping', value: resourceGroupRepositoryMap }
            ],
        workItemBackend != 'AzureDevOps'
          ? []
          : union(
              [
                { name: 'AzureDevOps__OrganisationUrl', value: adoOrganisationUrl }
              ],
              empty(adoClientId) ? [] : [{ name: 'AzureDevOps__ClientId', value: adoClientId }],
              empty(adoTenantId) ? [] : [{ name: 'AzureDevOps__TenantId', value: adoTenantId }],
              empty(adoCertificateId) ? [] : [{ name: 'AzureDevOps__CertificateId', value: adoCertificateId }],
              empty(adoWorkItemDefaultAssignee)
                ? []
                : [{ name: 'AzureDevOps__WorkItemDefaultAssignee', value: adoWorkItemDefaultAssignee }],
              empty(adoWorkItemOpenState)
                ? []
                : [{ name: 'AzureDevOps__WorkItemOpenState', value: adoWorkItemOpenState }],
              empty(adoWorkItemClosedState)
                ? []
                : [{ name: 'AzureDevOps__WorkItemClosedState', value: adoWorkItemClosedState }],
              empty(adoWorkItemType) ? [] : [{ name: 'AzureDevOps__WorkItemType', value: adoWorkItemType }]
            )
      )
    }
    diagnosticSettings: [{ workspaceResourceId: logAnalyticsWS!.outputs.resourceId }]
    tags: {
      'azd-service-name': 'retireabot'
    }
  }
}

output WORK_ITEM_BACKEND string = workItemBackend
output GITHUB_PRIVATE_KEY_ID string = gitHubParamCount == 4 ? gitHubPrivateKeyId : ''
output GITHUB_PRIVATE_KEY_PATH string = gitHubParamCount == 4 ? gitHubPrivateKeyPath : ''
output AZURE_KEY_VAULT_NAME string = vault.name
output ADO_CERTIFICATE_ID string = !empty(adoCertificateId) ? adoCertificateId : ''
output ADO_CERTIFICATE_PATH string = !empty(adoCertificatePath) ? adoCertificatePath : ''
