$actionsRun = $false
$maxRetries = 6
$retryDelay = 10

function Invoke-WithRetry {
    param(
        [scriptblock]$Command,
        [string]$Description
    )

    for ($i = 1; $i -le $maxRetries; $i++) {
        $output = & $Command 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Output $output
            return
        }

        if ($i -lt $maxRetries -and ($output -match "Forbidden" -or $output -match "ForbiddenByRbac")) {
            Write-Host "  Waiting for RBAC propagation (attempt $i/$maxRetries)..."
            Start-Sleep -Seconds $retryDelay
        }
        else {
            Write-Error "Failed: $Description`n$output"
            return
        }
    }
}

if ($env:WORK_ITEM_BACKEND -like "*GitHub*") {
    if ([string]::IsNullOrEmpty($env:GITHUB_PRIVATE_KEY_ID) -or [string]::IsNullOrEmpty($env:GITHUB_PRIVATE_KEY_PATH)) {
        Write-Host "GitHub App auth not configured, skipping key upload."
    }
    else {
        $keyPath = Join-Path $PWD $env:GITHUB_PRIVATE_KEY_PATH

        if (-not (Test-Path $keyPath)) {
            Write-Host "GitHub private key file not found at $keyPath, skipping upload."
        }
        else {
            $kvId = az keyvault show --name $env:AZURE_KEY_VAULT_NAME --query id -o tsv
            Write-Host "Uploading GitHub private key: $keyPath"

            $userId = az ad signed-in-user show --query id -o tsv

            # Temporarily assign Crypto Officer
            $assignment = az role assignment create `
                --role "Key Vault Crypto Officer" `
                --assignee $userId `
                --scope $kvId `
                --query id -o tsv

            # Upload the key (with retry for RBAC propagation)
            Invoke-WithRetry -Description "GitHub key import" -Command {
                az keyvault key import --name $env:GITHUB_PRIVATE_KEY_ID --pem-file $keyPath --vault-name $env:AZURE_KEY_VAULT_NAME
            }

            # Remove the role assignment
            az role assignment delete --ids $assignment
            $actionsRun = $true
        }
    }
}

if ($env:WORK_ITEM_BACKEND -like "*AzureDevOps*") {
    if ([string]::IsNullOrEmpty($env:ADO_CERTIFICATE_ID) -or [string]::IsNullOrEmpty($env:ADO_CERTIFICATE_PATH)) {
        Write-Host "Azure DevOps certificate auth not configured, skipping certificate upload."
    }
    else {
        $certPath = Join-Path $PWD $env:ADO_CERTIFICATE_PATH

        if (-not (Test-Path $certPath)) {
            Write-Host "ADO certificate file not found at $certPath, skipping upload."
        }
        else {
            $kvId = az keyvault show --name $env:AZURE_KEY_VAULT_NAME --query id -o tsv
            Write-Host "Uploading ADO certificate: $certPath"

            $userId = az ad signed-in-user show --query id -o tsv

            # Temporarily assign Certificates Officer
            $assignment = az role assignment create `
                --role "Key Vault Certificates Officer" `
                --assignee $userId `
                --scope $kvId `
                --query id -o tsv

            # Import the certificate (with retry for RBAC propagation)
            Invoke-WithRetry -Description "ADO certificate import" -Command {
                az keyvault certificate import --name $env:ADO_CERTIFICATE_ID --file $certPath --vault-name $env:AZURE_KEY_VAULT_NAME
            }

            # Remove the role assignment
            az role assignment delete --ids $assignment
            $actionsRun = $true
        }
    }
}

if (-not [string]::IsNullOrEmpty($env:POWERBI_CERTIFICATE_ID) -and -not [string]::IsNullOrEmpty($env:POWERBI_CERTIFICATE_PATH)) {
    $certPath = Join-Path $PWD $env:POWERBI_CERTIFICATE_PATH

    if (-not (Test-Path $certPath)) {
        Write-Host "PowerBI certificate file not found at $certPath, skipping upload."
    }
    else {
        $kvId = az keyvault show --name $env:AZURE_KEY_VAULT_NAME --query id -o tsv
        Write-Host "Uploading PowerBI certificate: $certPath"

        $userId = az ad signed-in-user show --query id -o tsv

        # Temporarily assign Certificates Officer
        $assignment = az role assignment create `
            --role "Key Vault Certificates Officer" `
            --assignee $userId `
            --scope $kvId `
            --query id -o tsv

        # Import the certificate (with retry for RBAC propagation)
        Invoke-WithRetry -Description "PowerBI certificate import" -Command {
            az keyvault certificate import --name $env:POWERBI_CERTIFICATE_ID --file $certPath --vault-name $env:AZURE_KEY_VAULT_NAME
        }

        # Remove the role assignment
        az role assignment delete --ids $assignment
        $actionsRun = $true
    }
}

if (-not $actionsRun) {
    Write-Host "No post-provision actions required."
}