using '../main.bicep'

param location = 'uksouth'
param deploymentName = 'scopetest'
param deploymentUniqueText = 'local'
param scanOptions = {
  mode: 'FromRouting'
  assignReader: false
}
param gitHubOptions = {
  PAT: 'test-placeholder'
  routing: {
    scope: 'perContainer'
    targetRepository: 'owner/triage'
    containerMappings: [
      {
        scopeId: '/SUBSCRIPTIONS/11111111-1111-1111-1111-111111111111/RESOURCEGROUPS/RG-ONE'
        target: 'owner/one'
      }
      {
        scopeId: '/subscriptions/22222222-2222-2222-2222-222222222222/resourceGroups/rg-two'
        target: 'owner/two'
      }
    ]
  }
}
