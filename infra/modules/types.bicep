@export()
@description('Configuration on how advisories are handled')
type AdvisoryOptions = {
  @description('Resource-group work-item filter. Does not change scan scopes or Reader assignments.')
  targetResourceGroup: string
  @description('Whether resolved or completed Azure Advisor advisories should be included when creating work items.')
  includeResolvedAdvisories: bool
}

@export()
@description('Optional overrides for advisory handling.')
type AdvisoryOptionsInput = {
  targetResourceGroup: string?
  includeResolvedAdvisories: bool?
}

@export()
@description('Configuration for the manual HTTP endpoint.')
type HttpEndpointOptions = {
  @description('Whether the manual HTTP endpoint is enabled.')
  enabled: bool
  @description('Whether the manual HTTP endpoint should display extended information about its run.')
  includeOutput: bool
  @description('Whether the manual HTTP endpoint should allow users to run dry-runs.')
  allowWhatIf: bool
}

@export()
@description('Optional overrides for the manual HTTP endpoint.')
type HttpEndpointOptionsInput = {
  enabled: bool?
  includeOutput: bool?
  allowWhatIf: bool?
}

@export()
@description('Resolved scan configuration shared by Reader provisioning and runtime query configuration.')
type ScanOptions = {
  mode: 'Explicit' | 'FromRouting' | 'DeploymentSubscription'
  scopes: string[]
  assignReader: bool
}

@export()
@description('Optional scan overrides. Defaults to DeploymentSubscription mode with Reader provisioning enabled.')
type ScanOptionsInput = {
  mode: ('Explicit' | 'FromRouting' | 'DeploymentSubscription')?
  scopes: string[]?
  assignReader: bool?
}

type ContainerMap = {
  @description('Azure container name or identifier. May be omitted when scopeId is supplied.')
  name: string?
  @description('The container type on Azure')
  type: ('ResourceGroup' | 'ManagementGroup' | 'Subscription')?
  @description('The target repository for the resources in that container')
  target: string
  @description('Fully qualified subscription or resource-group ID. Required for FromRouting scan mode; name and type can be omitted when scopeId is supplied.')
  scopeId: string?
}

type VendorRoutingOptionsInput = {
  scope: ('monolithic' | 'perContainer')?
  targetRepository: string?
  useTriageForUnmapped: bool?
  unmappedRepository: string?
  containerMappings: ContainerMap[]?
}

@export()
type LifecycleSignalsOptions = {
  @description('(Optional) Whether RetireaBot should check and create advisories for services reaching end of life (supported services: AKS, PostgreSQL flexible server)')
  enabled: bool
  @description('(Optional) How many days before a published end-of-life date a deployed resource should start producing a work item.')
  warningWindowDays: int
}

@export()
@description('Optional overrides for lifecycle signal processing.')
type LifecycleSignalsOptionsInput = {
  enabled: bool?
  warningWindowDays: int?
}

type WorkItemOptions = {
  @description('What label should be attached to all work items to identify it was created by RetireaBot.')
  advisoryLabel: string
  @description('What label should be attached to parent work items to identify them.')
  advisoryParentLabel: string
  @description('What prefix should be applied to label that uniquely identifies a work item based on their advisory.')
  advisoryLabelPrefix: string
  @description('What prefix should be applied to label that uniquely identifies a parent work item based on their advisory.')
  advisoryParentLabelPrefix: string
  @description('Whether parent work items should be created when processing advisories to track child work items')
  createParentWorkItems: bool
  @description('Whether child work items should be created when processing advisories')
  createChildWorkItems: bool
  @description('Whether the advisories should contain the resource ID of the affected resource')
  includeResourceId: bool
}

type WorkItemOptionsInput = {
  advisoryLabel: string?
  advisoryParentLabel: string?
  advisoryLabelPrefix: string?
  advisoryParentLabelPrefix: string?
  createParentWorkItems: bool?
  createChildWorkItems: bool?
  includeResourceId: bool?
}

type VendorRoutingOptions = {
  @description('Determines whether work items use one repository or are routed by Azure container.')
  scope: 'monolithic' | 'perContainer'
  @description('The default or parent repository for work items.')
  targetRepository: string
  @description('Should unmapped resources have their work items created in the triage repository/target repository in perContainer mode.')
  useTriageForUnmapped: bool
  @description('The repository work items should be created in when they are not mapped in perContainer mode.')
  unmappedRepository: string
  @description('Repository mappings used when scope is perContainer.')
  containerMappings: ContainerMap[]
}

@export()
type AzureDevOpsOptions = {
  @description('The URL of the Azure DevOps organisation (e.g. https://dev.azure.com/myorg)')
  organisationUrl: string
  @secure()
  @description('The PAT that allows RetireaBot to interact with your Azure DevOps organisation.')
  PAT: string
  @description('The client ID of the app registration used to authenticate with Azure DevOps')
  clientId: string
  @description('The tenant ID of the app registration used to authenticate with Azure DevOps')
  tenantId: string
  @secure()
  @description('The client secret of the app registration used to authenticate with Azure DevOps')
  clientSecret: string
  @description('The ID the certificate should be stored as in the KeyVault for Azure DevOps certificate auth')
  certificateId: string
  @description('The path of the certificate file (PFX/PEM) to be imported into the KeyVault for Azure DevOps certificate auth')
  certificatePath: string
  @description('The default assignee for work items created by RetireaBot in Azure DevOps')
  workItemDefaultAssignee: string
  @description('The state to use when opening work items in Azure DevOps.')
  workItemOpenState: string
  @description('The state to use when closing work items in Azure DevOps.')
  workItemClosedState: string
  @description('The work item type to create in Azure DevOps.')
  workItemType: string
  @description('Defines how this backend will process Advisories to work items')
  routing: VendorRoutingOptions
  @description('Defines how behave on this backend')
  workItemConfiguration: WorkItemOptions
}

@export()
@description('Optional overrides for Azure DevOps authentication, routing, and work item creation.')
type AzureDevOpsOptionsInput = {
  organisationUrl: string?
  @secure()
  PAT: string?
  clientId: string?
  tenantId: string?
  @secure()
  clientSecret: string?
  certificateId: string?
  certificatePath: string?
  workItemDefaultAssignee: string?
  workItemOpenState: string?
  workItemClosedState: string?
  workItemType: string?
  routing: VendorRoutingOptionsInput?
  workItemConfiguration: WorkItemOptionsInput?
}

@export()
type GitHubOptions = {
  @description('Whether GitHub CoPilot should be assigned to issues created by RetireaBot.')
  assignCopilot: bool
  @secure()
  @description('The PAT that allows RetireaBot to interact with your GitHub repository.')
  PAT: string
  @description('App Id of the registered GitHub App')
  appId: string
  @description('The install id of the GitHub App to be used for actions on repositories')
  installId: string
  @description('The id the private key for access to the GitHub App should be stored as in the KeyVault')
  privateKeyId: string
  @description('The path of the private key to be stored in the KeyVault for the GitHub App')
  privateKeyPath: string
  @description('Defines how this backend will process Advisories to work items')
  routing: VendorRoutingOptions
  @description('Defines how behave on this backend')
  workItemConfiguration: WorkItemOptions
}

@export()
@description('Optional overrides for GitHub authentication, routing, and issue creation.')
type GitHubOptionsInput = {
  assignCopilot: bool?
  @secure()
  PAT: string?
  appId: string?
  installId: string?
  privateKeyId: string?
  privateKeyPath: string?
  routing: VendorRoutingOptionsInput?
  workItemConfiguration: WorkItemOptionsInput?
}

@export()
type PowerBIOptions = {
  @description('The client ID of the app registration used to authenticate with Power BI')
  clientId: string
  @description('The tenant ID of the app registration used to authenticate with Power BI')
  tenantId: string
  @secure()
  @description('The client secret of the app registration used to authenticate with Power BI')
  clientSecret: string
  @description('The ID of the certificate of the app registration used to authenticate with Power BI')
  certificateId: string
  @description('The path of the certificate file (PFX/PEM) to be imported into the KeyVault for Power BI certificate auth')
  certificatePath: string
  @description('The ID of the workspace the dataset is located in')
  workspaceId: string
  @description('The ID of the dataset RetireaBot should push data into')
  datasetId: string
  @description('The table name that RetireaBot should create rows for')
  tableName: string
  @description('How RetireaBot should write to a PowerBI dataset')
  writeMode: 'Append' | 'Snapshot'
}

@export()
@description('Optional overrides for Power BI authentication and output.')
type PowerBIOptionsInput = {
  clientId: string?
  tenantId: string?
  @secure()
  clientSecret: string?
  certificateId: string?
  certificatePath: string?
  workspaceId: string?
  datasetId: string?
  tableName: string?
  writeMode: ('Append' | 'Snapshot')?
}
