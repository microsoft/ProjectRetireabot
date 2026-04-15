targetScope = 'subscription'

@description('The principal ID of the identity to assign the role to.')
param principalId string

var subscriptionReaderRoleId = 'acdd72a7-3385-48ef-bd42-f606fba81ae7'

resource roleAssignmentSubscriptionReader 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(subscription().id, principalId, 'Reader')
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', subscriptionReaderRoleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}
