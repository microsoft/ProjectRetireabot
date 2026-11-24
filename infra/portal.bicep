targetScope = 'resourceGroup'

metadata name = 'Azure RetireaBot - Portal Deployment'
metadata description = 'Portal-friendly deployment entry point with flattened configuration parameters.'

@description('Optional. A unique application/solution name for all resources in this deployment. This should be 3-16 characters long.')
@minLength(3)
@maxLength(16)
param deploymentName string = 'azeg'

@allowed([
  'GitHub'
  'AzureDevOps'
  'GitHubAndAzureDevOps'
  'None'
])
@description('The work item backend or backends RetireaBot should use.')
param workItemBackend string = 'GitHub'

@allowed([
  'None'
  'PowerBI'
])
@description('The data sink backend RetireaBot should use.')
param dataSinkBackend string = 'None'

@description('The NCRONTAB expression that determines how often the timer-triggered function runs.')
param timerTrigger string = '0 0 0 * * 1'

@description('Whether resolved or completed Azure Advisor advisories should be included.')
param advisoryIncludeResolvedAdvisories bool = false

@description('Resource-group work-item filter. Does not change scan scopes or Reader assignments.')
param advisoryTargetResourceGroup string = ''

@allowed(['Explicit', 'FromRouting', 'DeploymentSubscription'])
@description('Scan mode. Defaults to scanning only the deployment subscription.')
param scanMode string = 'DeploymentSubscription'

@description('Fully qualified subscription/RG IDs for Explicit scan mode.')
param scanScopes string[] = []

@description('Whether to provision scan Reader assignments. Does not affect scan filtering or own-component permissions.')
param scanAssignReader bool = true

@description('Whether lifecycle advisories should be generated for supported service version policies.')
param lifecycleSignalsEnabled bool = false

@minValue(1)
@description('How many days before end of life a resource should start producing a work item.')
param lifecycleSignalsWarningWindowDays int = 180

@description('Whether the manual HTTP endpoint is enabled.')
param httpEndpointEnabled bool = false

@description('Whether the manual HTTP endpoint should include extended run information.')
param httpEndpointIncludeOutput bool = false

@description('Whether the manual HTTP endpoint should allow dry runs.')
param httpEndpointAllowWhatIf bool = false

@description('The URL of the Azure DevOps organisation, for example https://dev.azure.com/myorg.')
param azureDevOpsOrganisationUrl string = ''

@secure()
@description('The PAT used to authenticate with Azure DevOps.')
param azureDevOpsPAT string = ''

@description('The client ID of the app registration used to authenticate with Azure DevOps.')
param azureDevOpsClientId string = ''

@description('The tenant ID of the app registration used to authenticate with Azure DevOps.')
param azureDevOpsTenantId string = ''

@secure()
@description('The client secret of the app registration used to authenticate with Azure DevOps.')
param azureDevOpsClientSecret string = ''

@description('The ID used to store the Azure DevOps authentication certificate in Key Vault.')
param azureDevOpsCertificateId string = ''

@description('The path of the Azure DevOps authentication certificate file.')
param azureDevOpsCertificatePath string = ''

@description('The default assignee for Azure DevOps work items.')
param azureDevOpsWorkItemDefaultAssignee string = ''

@description('The state used when opening Azure DevOps work items.')
param azureDevOpsWorkItemOpenState string = 'New'

@description('The state used when closing Azure DevOps work items.')
param azureDevOpsWorkItemClosedState string = 'Closed'

@description('The Azure DevOps work item type to create.')
param azureDevOpsWorkItemType string = 'Task'

@allowed([
  'monolithic'
  'perContainer'
])
@description('Whether Azure DevOps work items use one project or are routed by Azure container.')
param azureDevOpsRoutingScope string = 'monolithic'

@description('The default or parent Azure DevOps project for work items.')
param azureDevOpsTargetRepository string = ''

@description('Whether unmapped resources should use the triage project in per-container mode.')
param azureDevOpsUseTriageForUnmapped bool = false

@description('The Azure DevOps project used for unmapped resources in per-container mode.')
param azureDevOpsUnmappedRepository string = ''

@description('Azure container-to-project mappings used in per-container mode.')
param azureDevOpsContainerMappings array = []

@description('The label attached to Azure DevOps work items created by RetireaBot.')
param azureDevOpsAdvisoryLabel string = 'advisor'

@description('The label attached to parent Azure DevOps work items.')
param azureDevOpsAdvisoryParentLabel string = 'tracking'

@description('The prefix applied to labels that identify Azure DevOps advisory work items.')
param azureDevOpsAdvisoryLabelPrefix string = 'advisor-'

@description('The prefix applied to labels that identify Azure DevOps parent work items.')
param azureDevOpsAdvisoryParentLabelPrefix string = 'advisor-type-'

@description('Whether Azure DevOps parent work items should be created.')
param azureDevOpsCreateParentWorkItems bool = true

@description('Whether Azure DevOps child work items should be created.')
param azureDevOpsCreateChildWorkItems bool = true

@description('Whether Azure DevOps work items should include the affected resource ID.')
param azureDevOpsIncludeResourceId bool = false

@description('Whether GitHub Copilot should be assigned to issues created by RetireaBot.')
param gitHubAssignCopilot bool = false

@secure()
@description('The PAT used to authenticate with GitHub.')
param gitHubPAT string = ''

@description('The GitHub App ID.')
param gitHubAppId string = ''

@description('The GitHub App installation ID.')
param gitHubInstallId string = ''

@description('The ID used to store the GitHub App private key in Key Vault.')
param gitHubPrivateKeyId string = ''

@description('The path of the GitHub App private key file.')
param gitHubPrivateKeyPath string = ''

@allowed([
  'monolithic'
  'perContainer'
])
@description('Whether GitHub issues use one repository or are routed by Azure container.')
param gitHubRoutingScope string = 'monolithic'

@description('The default or parent GitHub repository for issues.')
param gitHubTargetRepository string = ''

@description('Whether unmapped resources should use the triage repository in per-container mode.')
param gitHubUseTriageForUnmapped bool = false

@description('The GitHub repository used for unmapped resources in per-container mode.')
param gitHubUnmappedRepository string = ''

@description('Azure container-to-repository mappings used in per-container mode.')
param gitHubContainerMappings array = []

@description('The label attached to GitHub issues created by RetireaBot.')
param gitHubAdvisoryLabel string = 'advisor'

@description('The label attached to parent GitHub issues.')
param gitHubAdvisoryParentLabel string = 'tracking'

@description('The prefix applied to labels that identify GitHub advisory issues.')
param gitHubAdvisoryLabelPrefix string = 'advisor-'

@description('The prefix applied to labels that identify GitHub parent issues.')
param gitHubAdvisoryParentLabelPrefix string = 'advisor-type-'

@description('Whether GitHub parent issues should be created.')
param gitHubCreateParentWorkItems bool = true

@description('Whether GitHub child issues should be created.')
param gitHubCreateChildWorkItems bool = true

@description('Whether GitHub issues should include the affected resource ID.')
param gitHubIncludeResourceId bool = false

@description('The client ID of the app registration used to authenticate with Power BI.')
param powerBIClientId string = ''

@description('The tenant ID of the app registration used to authenticate with Power BI.')
param powerBITenantId string = ''

@secure()
@description('The client secret of the app registration used to authenticate with Power BI.')
param powerBIClientSecret string = ''

@description('The ID of the Power BI authentication certificate.')
param powerBICertificateId string = ''

@description('The path of the Power BI authentication certificate file.')
param powerBICertificatePath string = ''

@description('The ID of the Power BI workspace containing the dataset.')
param powerBIWorkspaceId string = ''

@description('The ID of the Power BI dataset.')
param powerBIDatasetId string = ''

@description('The Power BI table to which RetireaBot writes rows.')
param powerBITableName string = ''

@allowed([
  'Append'
  'Snapshot'
])
@description('How RetireaBot should write to the Power BI dataset.')
param powerBIWriteMode string = 'Append'

var deploymentUniqueText = take(uniqueString(subscription().id, resourceGroup().name, deploymentName), 5)

var selectedWorkItemBackends = workItemBackend == 'GitHubAndAzureDevOps'
  ? [
      'GitHub'
      'AzureDevOps'
    ]
  : workItemBackend == 'GitHub'
    ? ['GitHub']
    : workItemBackend == 'AzureDevOps' ? ['AzureDevOps'] : []

var selectedDataSinkBackends = dataSinkBackend == 'PowerBI' ? ['PowerBI'] : []

module retireaBot 'main.bicep' = {
  name: 'retireaBot-${deploymentName}'
  params: {
    deploymentName: deploymentName
    deploymentUniqueText: deploymentUniqueText
    location: resourceGroup().location
    workItemBackend: selectedWorkItemBackends
    dataSinkBackend: selectedDataSinkBackends
    timerTrigger: timerTrigger
    advisoryOptions: {
      includeResolvedAdvisories: advisoryIncludeResolvedAdvisories
      targetResourceGroup: advisoryTargetResourceGroup
    }
    scanOptions: {
      mode: scanMode
      scopes: scanScopes
      assignReader: scanAssignReader
    }
    lifecycleSignalsOptions: {
      enabled: lifecycleSignalsEnabled
      warningWindowDays: lifecycleSignalsWarningWindowDays
    }
    httpEndpoint: {
      enabled: httpEndpointEnabled
      includeOutput: httpEndpointIncludeOutput
      allowWhatIf: httpEndpointAllowWhatIf
    }
    azureDevOpsOptions: {
      organisationUrl: azureDevOpsOrganisationUrl
      PAT: azureDevOpsPAT
      clientId: azureDevOpsClientId
      tenantId: azureDevOpsTenantId
      clientSecret: azureDevOpsClientSecret
      certificateId: azureDevOpsCertificateId
      certificatePath: azureDevOpsCertificatePath
      workItemDefaultAssignee: azureDevOpsWorkItemDefaultAssignee
      workItemOpenState: azureDevOpsWorkItemOpenState
      workItemClosedState: azureDevOpsWorkItemClosedState
      workItemType: azureDevOpsWorkItemType
      routing: {
        scope: azureDevOpsRoutingScope
        targetRepository: azureDevOpsTargetRepository
        useTriageForUnmapped: azureDevOpsUseTriageForUnmapped
        unmappedRepository: azureDevOpsUnmappedRepository
        containerMappings: azureDevOpsContainerMappings
      }
      workItemConfiguration: {
        advisoryLabel: azureDevOpsAdvisoryLabel
        advisoryParentLabel: azureDevOpsAdvisoryParentLabel
        advisoryLabelPrefix: azureDevOpsAdvisoryLabelPrefix
        advisoryParentLabelPrefix: azureDevOpsAdvisoryParentLabelPrefix
        createParentWorkItems: azureDevOpsCreateParentWorkItems
        createChildWorkItems: azureDevOpsCreateChildWorkItems
        includeResourceId: azureDevOpsIncludeResourceId
      }
    }
    gitHubOptions: {
      assignCopilot: gitHubAssignCopilot
      PAT: gitHubPAT
      appId: gitHubAppId
      installId: gitHubInstallId
      privateKeyId: gitHubPrivateKeyId
      privateKeyPath: gitHubPrivateKeyPath
      routing: {
        scope: gitHubRoutingScope
        targetRepository: gitHubTargetRepository
        useTriageForUnmapped: gitHubUseTriageForUnmapped
        unmappedRepository: gitHubUnmappedRepository
        containerMappings: gitHubContainerMappings
      }
      workItemConfiguration: {
        advisoryLabel: gitHubAdvisoryLabel
        advisoryParentLabel: gitHubAdvisoryParentLabel
        advisoryLabelPrefix: gitHubAdvisoryLabelPrefix
        advisoryParentLabelPrefix: gitHubAdvisoryParentLabelPrefix
        createParentWorkItems: gitHubCreateParentWorkItems
        createChildWorkItems: gitHubCreateChildWorkItems
        includeResourceId: gitHubIncludeResourceId
      }
    }
    powerBIOptions: {
      clientId: powerBIClientId
      tenantId: powerBITenantId
      clientSecret: powerBIClientSecret
      certificateId: powerBICertificateId
      certificatePath: powerBICertificatePath
      workspaceId: powerBIWorkspaceId
      datasetId: powerBIDatasetId
      tableName: powerBITableName
      writeMode: powerBIWriteMode
    }
  }
}

output WORK_ITEM_BACKEND string = retireaBot.outputs.WORK_ITEM_BACKEND
output GITHUB_PRIVATE_KEY_ID string = retireaBot.outputs.GITHUB_PRIVATE_KEY_ID
output GITHUB_PRIVATE_KEY_PATH string = retireaBot.outputs.GITHUB_PRIVATE_KEY_PATH
output AZURE_KEY_VAULT_NAME string = retireaBot.outputs.AZURE_KEY_VAULT_NAME
output ADO_CERTIFICATE_ID string = retireaBot.outputs.ADO_CERTIFICATE_ID
output ADO_CERTIFICATE_PATH string = retireaBot.outputs.ADO_CERTIFICATE_PATH
output POWERBI_CERTIFICATE_ID string = retireaBot.outputs.POWERBI_CERTIFICATE_ID
output POWERBI_CERTIFICATE_PATH string = retireaBot.outputs.POWERBI_CERTIFICATE_PATH
