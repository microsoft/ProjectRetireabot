import { ScanOptions } from 'types.bicep'

func validateSubscriptionId(id string) string => length(id) == 36 && join(map(split(id, '-'), part => string(length(part))), ',') == '8,4,4,4,12'
  ? (empty(filter(range(0, length(replace(id, '-', ''))), index => !contains('0123456789abcdef', substring(toLower(replace(id, '-', '')), index, 1))))
      ? toLower(id)
      : fail('Scan subscription IDs must be GUIDs.'))
  : fail('Scan subscription IDs must be GUIDs.')

func validateGroupName(name string) string => length(name) >= 1 && length(name) <= 90 && !endsWith(name, '.')
  ? (empty(filter(range(0, length(name)), index => !contains('abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-().', substring(name, index, 1))))
      ? toLower(name)
      : fail('Scan resource-group names must contain only ASCII letters, digits, underscores, hyphens, periods, or parentheses.'))
  : fail('Invalid scan resource-group name.')

func parseParts(parts string[]) object => (length(parts) == 3 || length(parts) == 5)
  ? (parts[0] == '' && toLower(parts[1]) == 'subscriptions'
      ? {
          subscriptionId: validateSubscriptionId(parts[2])
          resourceGroupName: length(parts) == 5
            ? (toLower(parts[3]) == 'resourcegroups' ? validateGroupName(parts[4]) : fail('Invalid scan resource-group scope.'))
            : ''
        }
      : fail('Scan scopes must be fully qualified subscription or resource-group IDs.'))
  : fail('Scan scopes must be fully qualified subscription or resource-group IDs.')

@export()
func parseScanScope(id string) object => parseParts(split(id, '/'))

@export()
func normalizeScanScope(id string) string => length(split(id, '/')) == 5
  ? '/subscriptions/${parseScanScope(id).subscriptionId}/resourceGroups/${parseScanScope(id).resourceGroupName}'
  : '/subscriptions/${parseScanScope(id).subscriptionId}'

@export()
func normalizeMapping(mapping object) object => mapping.?scopeId != null
  ? normalizeScopedMapping(mapping, parseScanScope(mapping.scopeId))
  : (mapping.?name != null && mapping.?type != null
      ? mapping
      : fail('Routing mappings require scopeId, or both name and type.'))

func normalizeScopedMapping(mapping object, scope object) object => (mapping.?type == null || mapping.type == (empty(scope.resourceGroupName) ? 'Subscription' : 'ResourceGroup')) && (mapping.?name == null || toLower(mapping.name) == (empty(scope.resourceGroupName) ? scope.subscriptionId : scope.resourceGroupName))
  ? union(mapping, {
      scopeId: normalizeScanScope(mapping.scopeId)
      name: empty(scope.resourceGroupName) ? scope.subscriptionId : scope.resourceGroupName
      type: empty(scope.resourceGroupName) ? 'Subscription' : 'ResourceGroup'
    })
  : fail('Routing name/type must match scopeId when supplied.')

@export()
func resolveScanOptions(options ScanOptions, deploymentSubscriptionId string, mappings array) ScanOptions => union(options, {
  scopes: resolveScanScopes(options, deploymentSubscriptionId, mappings)
})

func resolveScanScopes(options ScanOptions, deploymentSubscriptionId string, mappings array) string[] => options.mode != 'Explicit' && !empty(options.scopes)
  ? fail('Scan scopes may only be supplied in Explicit mode.')
  : options.mode == 'DeploymentSubscription'
    ? ['/subscriptions/${validateSubscriptionId(deploymentSubscriptionId)}']
    : empty(options.mode == 'Explicit' ? options.scopes : mappings)
      ? fail('Restricted scan modes require at least one scope.')
      : union(map(
          options.mode == 'Explicit' ? options.scopes : map(mappings, mapping => mapping.?scopeId ?? fail('FromRouting requires scopeId on every enabled perContainer mapping.')),
          id => normalizeScanScope(id)
        ), [])
