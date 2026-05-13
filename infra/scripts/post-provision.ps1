if ([string]::Equals($env:WORK_ITEM_BACKEND, "GitHub")) {
    if ([string]::IsNullOrEmpty($env:GITHUB_PRIVATE_KEY_ID) -or [string]::IsNullOrEmpty($env:GITHUB_PRIVATE_KEY_PATH)) {
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
}
elseif ([string]::Equals($env:WORK_ITEM_BACKEND, "AzureDevOps")) {
    if ([string]::IsNullOrEmpty($env:ADO_CERTIFICATE_ID) -or [string]::IsNullOrEmpty($env:ADO_CERTIFICATE_PATH)) {
        Write-Host "Azure DevOps certificate auth not configured, skipping certificate upload."
        exit 0
    }

    $kvId = az keyvault show --name $env:AZURE_KEY_VAULT_NAME --query id -o tsv
    $certPath = Join-Path $PWD $env:ADO_CERTIFICATE_PATH
    Write-Host $certPath

    $userId = az ad signed-in-user show --query id -o tsv

    # Temporarily assign Certificates Officer
    $assignment = az role assignment create `
        --role "Key Vault Certificates Officer" `
        --assignee $userId `
        --scope $kvId `
        --query id -o tsv

    # Import the certificate
    az keyvault certificate import --name $env:ADO_CERTIFICATE_ID --file $certPath --vault-name $env:AZURE_KEY_VAULT_NAME

    # Remove the role assignment
    az role assignment delete --ids $assignment
}
else {
    Write-Host "No post-provision actions for " $env:WORK_ITEM_BACKEND
    exit 0
}