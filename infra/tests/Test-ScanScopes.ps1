param([string] $BicepPath = 'bicep')

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$result = & $BicepPath build-params (Join-Path $PSScriptRoot 'scanScopes.bicepparam') --stdout
if ($LASTEXITCODE -ne 0) { throw 'Scope fixture compilation failed.' }
$parameters = ($result | ConvertFrom-Json).parametersJson | ConvertFrom-Json
$groupScope = '/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg-one'
foreach ($name in @('explicitOptions', 'routingOptions')) {
    $scopes = $parameters.parameters.$name.value.scopes
    if ($scopes.Count -ne 1 -or $scopes[0] -cne $groupScope) { throw "Unexpected $name resolution." }
}
if ($parameters.parameters.defaultOptions.value.scopes[0] -cne '/subscriptions/11111111-1111-1111-1111-111111111111') {
    throw 'Unexpected deployment-subscription resolution.'
}
foreach ($mapping in $parameters.parameters.normalizedMappings.value) {
    if ($mapping.name -cne 'rg-one' -or $mapping.type -cne 'ResourceGroup' -or $mapping.scopeId -cne $groupScope) {
        throw 'Scope-only mapping normalization failed.'
    }
}
if ($parameters.parameters.crossSubscriptionOptions.value.scopes.Count -ne 3) {
    throw 'Cross-subscription or overlapping scopes were lost.'
}
if ($parameters.parameters.explicitOptions.value.mode -cne 'Explicit' -or $parameters.parameters.explicitOptions.value.assignReader -ne $false) {
    throw 'Explicit mode or externally managed Reader configuration was lost.'
}
if ($parameters.parameters.routingOptions.value.mode -cne 'FromRouting' -or $parameters.parameters.routingOptions.value.assignReader -ne $true) {
    throw 'Routing mode or default Reader configuration was lost.'
}
if ($parameters.parameters.defaultOptions.value.mode -cne 'DeploymentSubscription' -or $parameters.parameters.defaultOptions.value.assignReader -ne $true) {
    throw 'Default scan configuration was lost.'
}

$invalidExpressions = @(
    "resolveScanOptions(union(defaults, { mode: 'Explicit', scopes: [], assignReader: false }), '', [])"
    "resolveScanOptions(union(defaults, { mode: 'Explicit', scopes: ['invalid'], assignReader: false }), '', [])"
    "resolveScanOptions(union(defaults, { mode: 'Explicit', scopes: ['/subscriptions/not-a-guid'] }), '', [])"
    "resolveScanOptions(union(defaults, { mode: 'Explicit', scopes: ['/subscriptions/ 11111111-1111-1111-1111-111111111111 '] }), '', [])"
    "resolveScanOptions(union(defaults, { mode: 'Explicit', scopes: ['$groupScope/'] }), '', [])"
    "resolveScanOptions(union(defaults, { mode: 'Explicit', scopes: ['$groupScope.'] }), '', [])"
    "resolveScanOptions(union(defaults, { mode: 'FromRouting' }), '', [])"
    "resolveScanOptions(union(defaults, { mode: 'FromRouting', scopes: ['$groupScope'] }), '', [])"
    "resolveScanOptions(union(defaults, { mode: 'FromRouting' }), '', [{ name: 'rg-one', type: 'ResourceGroup', target: 'owner/repo' }])"
    "resolveScanOptions(defaults, '', [])"
    "normalizeMapping({ scopeId: '$groupScope', name: 'different', target: 'owner/repo' })"
    "normalizeMapping({ scopeId: '$groupScope', type: 'Subscription', target: 'owner/repo' })"
)
$temporaryFile = Join-Path $PSScriptRoot "scan-scopes-$([guid]::NewGuid().ToString('N')).bicepparam"
try {
    foreach ($expression in $invalidExpressions) {
        $source = "using none`nimport { normalizeMapping, resolveScanOptions } from '../modules/scanScopes.bicep'`nvar defaults = { mode: 'DeploymentSubscription', scopes: [], assignReader: true }`nparam result = $expression`n"
        [System.IO.File]::WriteAllText($temporaryFile, $source)
        $diagnostics = & $BicepPath build-params $temporaryFile --stdout 2>&1 | Out-String
        if ($LASTEXITCODE -eq 0 -or $diagnostics -notmatch 'BCP338') {
            throw "Expected scope validation failure for $expression`n$diagnostics"
        }
    }
}
finally {
    if (Test-Path -LiteralPath $temporaryFile) { Remove-Item -LiteralPath $temporaryFile }
}

$snapshotPath = Join-Path $PSScriptRoot 'mainScopes.snapshot.json'
try {
    & $BicepPath snapshot (Join-Path $PSScriptRoot 'mainScopes.bicepparam') --mode overwrite --subscription-id 11111111-1111-1111-1111-111111111111 --resource-group rg-scope-test --location uksouth
    if ($LASTEXITCODE -ne 0) { throw 'Main module snapshot failed.' }
    $snapshot = Get-Content -LiteralPath $snapshotPath -Raw | ConvertFrom-Json
    $site = @($snapshot.predictedResources | Where-Object type -eq 'Microsoft.Web/sites')
    if ($site.Count -ne 1) { throw 'Expected one Function App in the main module snapshot.' }
    # Resource references leave appSettings as an ARM expression in the offline snapshot.
    $settings = @{}
    foreach ($match in [regex]::Matches($site[0].properties.siteConfig.appSettings, "createObject\('name', '([^']+)', 'value', '((?:[^']|'')*)'\)")) {
        $settings[$match.Groups[1].Value] = $match.Groups[2].Value.Replace("''", "'")
    }
    if ($settings.Scan__Mode -cne 'FromRouting' -or $settings.Scan__Scopes -cne '[]' -or $settings.Scan__AssignReader -ine 'false') {
        throw 'Resolved scan mode, raw scopes, or Reader opt-out was not forwarded to the Function App.'
    }
    $expectedScopes = @($groupScope, '/subscriptions/22222222-2222-2222-2222-222222222222/resourceGroups/rg-two')
    $actualScopes = @($settings.Scan__ResolvedScopes | ConvertFrom-Json)
    if (($actualScopes | ConvertTo-Json -Compress) -cne ($expectedScopes | ConvertTo-Json -Compress)) {
        throw 'Resolved routing scopes were not forwarded to the Function App.'
    }
    $mappings = @($settings.GitHub__TargetContainerMapping | ConvertFrom-Json)
    if ($mappings.Count -ne $expectedScopes.Count) { throw 'Normalized routing mappings were lost.' }
    for ($index = 0; $index -lt $mappings.Count; $index++) {
        if ($mappings[$index].scopeId -cne $expectedScopes[$index] -or $mappings[$index].type -cne 'ResourceGroup' -or $mappings[$index].name -cne ($expectedScopes[$index] -split '/')[-1]) {
            throw 'Normalized routing mapping metadata was not forwarded to the Function App.'
        }
    }
    $roles = @($snapshot.predictedResources | Where-Object type -eq 'Microsoft.Authorization/roleAssignments')
    if ($roles.Count -eq 0) { throw 'Component role assignments were unexpectedly removed.' }
    if (@($roles | Where-Object { $_.properties.roleDefinitionId -like '*/acdd72a7-3385-48ef-bd42-f606fba81ae7' }).Count -ne 0) {
        throw 'Reader was assigned despite assignReader being false.'
    }
}
finally {
    if (Test-Path -LiteralPath $snapshotPath) { Remove-Item -LiteralPath $snapshotPath }
}
Write-Output 'Scan scope resolution, invalid-input, and module-wiring checks passed.'
exit 0
