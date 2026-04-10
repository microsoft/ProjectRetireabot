if ([string]::IsNullOrEmpty($env:GITHUB_PRIVATE_KEY_ID) || [string]::IsNullOrEmpty($env:GITHUB_PRIVATE_KEY_PATH)) {
    Write-Host "GitHub App auth not configured, skipping key upload."
    exit 0
}

$kvId = az keyvault show --name $env:AZURE_KEY_VAULT_NAME --query id -o tsv
$keyPath = Join-Path $PWD $env:GITHUB_PRIVATE_KEY_PATH
Write-Host $keyPath

$userId = az ad signed-in-user show --query id -o tsv

# Temporarily assign Crypto Officer
$assignment = az role assignment create `
    --role "Key Vault Crypto Officer" `
    --assignee $userId `
    --scope $kvId `
    --query id -o tsv

# Upload the key
az keyvault key import --name $env:GITHUB_PRIVATE_KEY_ID --pem-file $keyPath --vault-name $env:AZURE_KEY_VAULT_NAME

# Remove the role assignment
az role assignment delete --ids $assignment