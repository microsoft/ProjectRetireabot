targetScope = 'resourceGroup'

import {
  AdvisoryOptionsInput
  AzureDevOpsOptionsInput
  LifecycleSignalsOptionsInput
  GitHubOptionsInput
  HttpEndpointOptionsInput
  PowerBIOptionsInput
  ScanOptionsInput
} from 'modules/types.bicep'
import { normalizeMapping, resolveScanOptions } from 'modules/scanScopes.bicep'

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
param workItemBackend string[] = ['GitHub']

@allowed([
  'PowerBI'
])
@description('What data sink backend RetireaBot should use push data to')
param dataSinkBackend string[] = []

@description('(Optional) The NCRONTAB expression that determines how often the timer-triggered function runs. Default: every Monday at 00:00 UTC ("0 0 0 * * 1"). See https://learn.microsoft.com/azure/azure-functions/functions-bindings-timer for syntax.')
param timerTrigger string = '0 0 0 * * 1'

@description('Configuration for selecting which Azure Advisor recommendations RetireaBot processes.')
param advisoryOptions AdvisoryOptionsInput = {}

@description('Scan mode, approved scope IDs, and optional Reader provisioning. Defaults to scanning and granting Reader on the deployment subscription.')
param scanOptions ScanOptionsInput = {}

@description('Configuration for Azure DevOps authentication and work item creation.')
param azureDevOpsOptions AzureDevOpsOptionsInput = {}

@description('Configuration for GitHub authentication and issue creation.')
param gitHubOptions GitHubOptionsInput = {}

@description('Configuration for generating lifecycle advisories from supported service version policies.')
param lifecycleSignalsOptions LifecycleSignalsOptionsInput = {}

@description('Configuration for the manual HTTP endpoint.')
param httpEndpoint HttpEndpointOptionsInput = {}

@description('Configuration for Power BI authentication and retirement advisory data output.')
param powerBIOptions PowerBIOptionsInput = {}

var defaultAdvisoryOptions = {
  includeResolvedAdvisories: false
  targetResourceGroup: ''
}

var defaultRoutingOptions = {
  containerMappings: []
  scope: 'monolithic'
  targetRepository: ''
  useTriageForUnmapped: false
  unmappedRepository: ''
}

var defaultWorkItemOptions = {
  advisoryLabel: 'advisor'
  advisoryLabelPrefix: 'advisor-'
  advisoryParentLabel: 'tracking'
  advisoryParentLabelPrefix: 'advisor-type-'
  createChildWorkItems: true
  createParentWorkItems: true
  includeResourceId: false
}

var defaultAzureDevOpsOptions = {
  organisationUrl: ''
  PAT: ''
  clientId: ''
  tenantId: ''
  clientSecret: ''
  certificateId: ''
  certificatePath: ''
  workItemDefaultAssignee: ''
  workItemOpenState: 'New'
  workItemClosedState: 'Closed'
  workItemType: 'Task'
}

var defaultGitHubOptions = {
  assignCopilot: false
  appId: ''
  installId: ''
  PAT: ''
  privateKeyId: ''
  privateKeyPath: ''
}

var defaultLifecycleSignalsOptions = {
  enabled: false
  warningWindowDays: 180
}

var defaultHttpEndpointOptions = {
  enabled: false
  includeOutput: false
  allowWhatIf: false
}

var defaultPowerBIOptions = {
  clientId: ''
  tenantId: ''
  clientSecret: ''
  certificateId: ''
  certificatePath: ''
  workspaceId: ''
  datasetId: ''
  tableName: ''
  writeMode: 'Append'
}

var defaultScanOptions = {
  mode: 'DeploymentSubscription'
  scopes: []
  assignReader: true
}

var resolvedAdvisoryOptions = union(defaultAdvisoryOptions, advisoryOptions)
var resolvedAzureDevOpsOptions = union(defaultAzureDevOpsOptions, azureDevOpsOptions, {
  routing: union(defaultRoutingOptions, azureDevOpsOptions.?routing ?? {})
  workItemConfiguration: union(defaultWorkItemOptions, azureDevOpsOptions.?workItemConfiguration ?? {})
})
var resolvedGitHubOptions = union(defaultGitHubOptions, gitHubOptions, {
  routing: union(defaultRoutingOptions, gitHubOptions.?routing ?? {})
  workItemConfiguration: union(defaultWorkItemOptions, gitHubOptions.?workItemConfiguration ?? {})
})
var resolvedLifecycleSignalsOptions = union(defaultLifecycleSignalsOptions, lifecycleSignalsOptions)
var resolvedHttpEndpoint = union(defaultHttpEndpointOptions, httpEndpoint)
var resolvedPowerBIOptions = union(defaultPowerBIOptions, powerBIOptions)

var deploymentSuffix = toLower(trim(replace(
  replace(
    replace(replace(replace(replace('${deploymentName}${deploymentUniqueText}', '-', ''), '_', ''), '.', ''), '/', ''),
    ' ',
    ''
  ),
  '*',
  ''
)))

// ARM evaluates these validation variables even though they are not referenced elsewhere.
#disable-next-line no-unused-vars
var outputCheck = !(resolvedHttpEndpoint.enabled && resolvedHttpEndpoint.includeOutput) && length(workItemBackend) == 0 && length(dataSinkBackend) == 0
  ? fail('You need at least one output when trying to deploy this app. Minimum you need to have the HTTP endpoint enabled with output or at least one WorkItem/DataSink backend configured.')
  : null

#disable-next-line no-unused-vars
var gitHubCredentialValidation = empty(resolvedGitHubOptions.PAT) && empty(resolvedGitHubOptions.appId) && empty(resolvedGitHubOptions.installId) && empty(resolvedGitHubOptions.privateKeyId) && empty(resolvedGitHubOptions.privateKeyPath) && contains(
    workItemBackend,
    'GitHub'
  )
  ? fail('You must provide at least one way of authenticating with GitHub (PAT or App)')
  : null

var gitHubAppParamsPopulated = [
  !empty(resolvedGitHubOptions.appId) ? 1 : 0
  !empty(resolvedGitHubOptions.installId) ? 1 : 0
  !empty(resolvedGitHubOptions.privateKeyId) ? 1 : 0
  !empty(resolvedGitHubOptions.privateKeyPath) ? 1 : 0
]

var gitHubParamCount = reduce(gitHubAppParamsPopulated, 0, (cur, next) => cur + next)
#disable-next-line no-unused-vars
var gitHubParamCountValidation = !(gitHubParamCount == 0 || gitHubParamCount == 4) && contains(
    workItemBackend,
    'GitHub'
  )
  ? fail('To use GitHub App authentication, you need to populate all required fields')
  : null

#disable-next-line no-unused-vars
var adoOrganisationUrlValidation = empty(resolvedAzureDevOpsOptions.organisationUrl) && contains(
    workItemBackend,
    'AzureDevOps'
  )
  ? fail('You must provide the Azure DevOps organisation URL when using the AzureDevOps backend')
  : null

var validatedGitHubOptions = contains(workItemBackend, 'GitHub') && !resolvedGitHubOptions.workItemConfiguration.createChildWorkItems && !resolvedGitHubOptions.workItemConfiguration.createParentWorkItems
  ? fail('GitHub must create at least one type of issue.')
  : resolvedGitHubOptions

var validatedAzureDevOpsOptions = contains(workItemBackend, 'AzureDevOps') && !resolvedAzureDevOpsOptions.workItemConfiguration.createChildWorkItems && !resolvedAzureDevOpsOptions.workItemConfiguration.createParentWorkItems
  ? fail('Azure DevOps must create at least one type of work item.')
  : resolvedAzureDevOpsOptions

var normalizedGitHubOptions = union(validatedGitHubOptions, {
  routing: union(validatedGitHubOptions.routing, {
    containerMappings: contains(workItemBackend, 'GitHub')
      ? map(validatedGitHubOptions.routing.containerMappings, mapping => normalizeMapping(mapping))
      : []
  })
})
var normalizedAzureDevOpsOptions = union(validatedAzureDevOpsOptions, {
  routing: union(validatedAzureDevOpsOptions.routing, {
    containerMappings: contains(workItemBackend, 'AzureDevOps')
      ? map(validatedAzureDevOpsOptions.routing.containerMappings, mapping => normalizeMapping(mapping))
      : []
  })
})
var scanRoutingMappings = concat(
  contains(workItemBackend, 'GitHub') && normalizedGitHubOptions.routing.scope == 'perContainer'
    ? normalizedGitHubOptions.routing.containerMappings
    : [],
  contains(workItemBackend, 'AzureDevOps') && normalizedAzureDevOpsOptions.routing.scope == 'perContainer'
    ? normalizedAzureDevOpsOptions.routing.containerMappings
    : []
)
var resolvedScanOptions = resolveScanOptions(
  union(defaultScanOptions, scanOptions),
  subscription().subscriptionId,
  scanRoutingMappings
)

module keyVault 'modules/keyVault.bicep' = {
  name: 'keyVault-${deploymentSuffix}'
  params: {
    deploymentSuffix: deploymentSuffix
    location: location

    azureDevOpsOptions: validatedAzureDevOpsOptions
    gitHubOptions: validatedGitHubOptions
    powerBIOptions: resolvedPowerBIOptions

    dataSinkBackend: dataSinkBackend
    workItemBackend: workItemBackend
  }
}

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring-${deploymentSuffix}'
  params: {
    deploymentSuffix: deploymentSuffix
    location: location
  }
}

module storageAccount 'modules/storage.bicep' = {
  name: 'storageAccount-${deploymentSuffix}'
  params: {
    deploymentSuffix: deploymentSuffix
    location: location
  }
}

module managedIdentity 'modules/managedIdentity.bicep' = {
  name: 'managedIdentity-${deploymentSuffix}'
  params: {
    deploymentSuffix: deploymentSuffix
    location: location

    storageAccountName: storageAccount.outputs.name
    keyVaultName: keyVault.outputs.name
    applicationInsightsName: monitoring.outputs.appInsightsName

    workItemBackend: workItemBackend
    scanOptions: resolvedScanOptions
    azureDevOpsOptions: normalizedAzureDevOpsOptions
    powerBIOptions: resolvedPowerBIOptions

    dataSinkBackend: dataSinkBackend
    gitHubParamCount: gitHubParamCount
  }
}

module functionApp 'modules/functionApp.bicep' = {
  name: 'functionApp-${deploymentSuffix}'
  params: {
    deploymentSuffix: deploymentSuffix
    location: location

    managedIdentityResourceId: managedIdentity.outputs.resourceId
    managedIdentityClientId: managedIdentity.outputs.clientId
    workspaceResourceId: monitoring.outputs.workspaceResourceId
    applicationInsightsInstrumentationKey: monitoring.outputs.instrumentationKey
    applicationInsightsConnectionString: monitoring.outputs.connectionString
    storageAccountName: storageAccount.outputs.name
    keyVaultUri: keyVault.outputs.uri

    workItemBackend: workItemBackend
    dataSinkBackend: dataSinkBackend
    timerTrigger: timerTrigger
    advisoryOptions: resolvedAdvisoryOptions
    scanOptions: resolvedScanOptions
    azureDevOpsOptions: normalizedAzureDevOpsOptions
    gitHubOptions: normalizedGitHubOptions
    lifecycleSignalsOptions: resolvedLifecycleSignalsOptions
    httpEndpoint: resolvedHttpEndpoint
    powerBIOptions: resolvedPowerBIOptions
    gitHubParamCount: gitHubParamCount
  }
}

output WORK_ITEM_BACKEND string = join(workItemBackend, ',')
output GITHUB_PRIVATE_KEY_ID string = gitHubParamCount == 4 ? validatedGitHubOptions.privateKeyId : ''
output GITHUB_PRIVATE_KEY_PATH string = gitHubParamCount == 4 ? validatedGitHubOptions.privateKeyPath : ''
output AZURE_KEY_VAULT_NAME string = keyVault.outputs.name
output ADO_CERTIFICATE_ID string = !empty(validatedAzureDevOpsOptions.certificateId)
  ? validatedAzureDevOpsOptions.certificateId
  : ''
output ADO_CERTIFICATE_PATH string = !empty(validatedAzureDevOpsOptions.certificatePath)
  ? validatedAzureDevOpsOptions.certificatePath
  : ''
output POWERBI_CERTIFICATE_ID string = !empty(resolvedPowerBIOptions.certificateId)
  ? resolvedPowerBIOptions.certificateId
  : ''
output POWERBI_CERTIFICATE_PATH string = !empty(resolvedPowerBIOptions.certificatePath)
  ? resolvedPowerBIOptions.certificatePath
  : ''
