using none

import { normalizeMapping, resolveScanOptions } from '../modules/scanScopes.bicep'

var subscriptionId = '11111111-1111-1111-1111-111111111111'
var groupScope = '/subscriptions/${subscriptionId}/resourceGroups/rg-one'
var defaults = {
  mode: 'DeploymentSubscription'
  scopes: []
  assignReader: true
}
var mappings = [
  normalizeMapping({ scopeId: toUpper(groupScope), target: 'owner/one' })
  normalizeMapping({ scopeId: groupScope, target: 'owner/two' })
]

param explicitOptions = resolveScanOptions({
  mode: 'Explicit'
  scopes: [groupScope, toUpper(groupScope)]
  assignReader: false
}, subscriptionId, [])
param routingOptions = resolveScanOptions(union(defaults, { mode: 'FromRouting' }), subscriptionId, mappings)
param defaultOptions = resolveScanOptions(defaults, subscriptionId, [])
param normalizedMappings = mappings
param crossSubscriptionOptions = resolveScanOptions({
  mode: 'Explicit'
  scopes: [
    groupScope
    '/subscriptions/22222222-2222-2222-2222-222222222222/resourceGroups/rg-one'
    '/subscriptions/22222222-2222-2222-2222-222222222222'
  ]
  assignReader: true
}, subscriptionId, [])
