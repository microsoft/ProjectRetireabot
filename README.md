# RetireaBot

This is the repository contains the source code to RetireaBot, an under-development proof-of-concept to help customers be on top of their resource migrations of EoL Azure services and or SKUs.

## How it works (with GitHub)

1. Retrieves all Azure Advisor advisories for your deployed resources in the subscriptions it has access to
2. Fetches the full advisory and extracts the key information from it
3. Checks if the advisory already has an issue, if not creates it as an issue on a specified repository and assign GitHub CoPilot to the issue
4. GitHub CoPilot attempts to resolve the issue and create a PR to review

## Requirements

- Azure Subscription(s)
- GitHub CoPilot Licenses (if assigning feature is enabled)

## Usage

By default this function will run every Monday at 00:00 UTC (`0 0 0 * * 1`). This can be tweaked by setting the `timerTrigger` parameter when deploying with `azd`, or by setting the `App__TimerTrigger` app setting on the deployed Function App.

## Deployment

The preferred way to deploy this program is using the [Azure Developer CLI](https://learn.microsoft.com/en-us/azure/developer/azure-developer-cli/install-azd), which handles both provisioning of the architecture and the deployment of the application to a target resource group.

Before provisioning the architecture, you need to specify parameters to define the behaviour of RetireaBot, copy the example json, and remove `.example`.

There are some key parameters you need to specify:

| Name                         | Required | Description                                                                                                                                                              |
| ---------------------------- | -------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| location                     | `true`   | The location where the resources are deployed                                                                                                                            |
| targetRepository<sub>1</sub> | `true`   | Target project or repository to create work items on from advisories                                                                                                     |
| workItemBackend              | `true`   | What work item backend RetireaBot should use to create work items in (Valid options: 'GitHub', 'AzureDevOps')                                                            |
| targetResourceGroup          | `false`  | The resource group RetireaBot should create issues for, leave blank any resource group                                                                                   |
| advisoryLabel                | `false`  | What label should be attached to all work items to identify it was created by RetireaBot. Default: advisor                                                               |
| advisoryParentLabel          | `false`  | What label should be attached to parent work items to identify them. Default: tracking                                                                                   |
| advisoryLabelPrefix          | `false`  | What prefix should be applied to label that uniquely identifies a work item based on their advisory. Default: advisor-                                                   |
| parentLabelPrefix            | `false`  | What prefix should be applied to label that uniquely identifies a parent work item based on their advisory. Default: advisor-type-                                       |
| createParentWorkItems        | `false`  | Whether parent work items should be created when processing advisories to track child work items                                                                         |
| createChildWorkItems         | `false`  | Whether child work items should be created when processing advisories                                                                                                    |
| deploymentName               | `false`  | A unique application/solution name for all resources in this deployment                                                                                                  |
| deploymentUniqueText         | `false`  | Unique text value for the solution. This is used to ensure resource names are unique for global resource                                                                 |
| httpEndpointEnable           | `false`  | Whether the manual HTTP endpoint should be enabled. Default: false                                                                                                       |
| httpEndpointOutput           | `false`  | Whether the manual HTTP endpoint should display extended information about its run Default: false                                                                        |
| httpEndpointWhatIf           | `false`  | Whether the manual HTTP endpoint should allow users to run dry-runs Default: false                                                                                       |
| timerTrigger                 | `false`  | NCRONTAB expression for the scheduled timer trigger. Default: `0 0 0 * * 1` (every Monday at 00:00 UTC)                                                                  |
| gitHubCoPilotAssign          | `false`  | Whether GitHub CoPilot should be assigned to try and mitigate issues (requires a GitHub CoPilot license) Default: false                                                  |
| resourceGroupRepositoryMap   | `false`  | If "perResourceGroup" mode is being used, these mappings decide which repositories issues are created based on their resource groups                                     |
| useTriageRepoForUnmapped     | `false`  | Should unmapped resources have their work items created in the triage repository/target repository in perResourceGroup mode. Default: true                               |
| unmappedRepository           | `false`  | The repository work items should be created in when they are not mapped in perResourceGroup mode.                                                                        |
| workItemScope                | `false`  | Whether issues should be created in one "triage" repository or should be shared across multiple repositories with a parent issue in the repository. Default "monolithic" |

<sub>1</sub> When workItemScope is set to "perResourceGroup", the repository specified here will be treated as the parent repository

### GitHub

When `workItemBackend` is set to `GitHub`, you have some additional properties you can set.

| Name                 | Required           | Description                                                                                                                         |
| -------------------- | ------------------ | ----------------------------------------------------------------------------------------------------------------------------------- |
| gitHubPAT            | `true`<sub>1</sub> | Personal access token for GitHub                                                                                                    |
| gitHubAppId          | `true`<sub>2</sub> | AppId of the App Registration on GitHub                                                                                             |
| gitHubInstallId      | `true`<sub>2</sub> | InstallId for the installation of the App registration to use                                                                       |
| gitHubPrivateKeyId   | `true`<sub>2</sub> | The id of the stored GitHub App's Private Key in KeyVault                                                                           |
| gitHubPrivateKeyPath | `true`<sub>2</sub> | The path to the private key to be used to authenticate and be stored in KeyVault (path is relative to the root of this repository). |

<sub>1</sub> Only required if no App Registration authentication is defined

<sub>2</sub> Only required if no PAT is defined and if another GitHub App Registration field is defined

Note: A deployment can have both a PAT and App Registration defined, the app with run in a 'Hybrid' mode, where PAT is used a secondary method to interact with GitHub.

Once you have configured RetireaBot with ensure your parameters file is called `main.parameters.json`, and run `azd up` at the root of the project directory, which it will then provision the architecture and deploy the application.

### Azure DevOps

When `workItemBackend` is set to `AzureDevOps`, you have some additional properties to configure how RetireaBot interacts with ADO.

| Name                       | Required            | Description                                                                                                   |
| -------------------------- | ------------------- | ------------------------------------------------------------------------------------------------------------- |
| adoOrganisationUrl         | `true`              | URL of the Azure DevOps Organisation                                                                          |
| adoPAT                     | `false`             | The PAT that allows RetireaBot to interact with your Azure DevOps organisation                                |
| adoClientId                | `false`             | The client ID of the app registration used to authenticate with Azure DevOps                                  |
| adoTenantId                | `false`             | The tenant ID of the app registration used to authenticate with Azure DevOps                                  |
| adoClientSecret            | `false`             | The client secret of the app registration used to authenticate with Azure DevOps                              |
| adoCertificateId           | `false`             | The ID the certificate should be stored as in the KeyVault for Azure DevOps certificate auth                  |
| adoCertificatePath         | `false`             | The path of the certificate file (PFX/PEM) to be imported into the KeyVault for Azure DevOps certificate auth |
| adoWorkItemDefaultAssignee | `false`<sub>1</sub> | (Optional) The default assignee for work items created by RetireaBot in Azure DevOps                          |
| adoWorkItemOpenState       | `false`<sub>1</sub> | (Optional) The state to use when opening work items in Azure DevOps. Default: New                             |
| adoWorkItemClosedState     | `false`<sub>1</sub> | (Optional) The state to use when closing work items in Azure DevOps. Default: Closed                          |
| adoWorkItemType            | `false`<sub>1</sub> | (Optional) The work item type to create in Azure DevOps. Default: Task                                        |

<sub>1</sub> Defaults of these values are set to the "Agile" Process. If you are using a different process for your project, you need to set these values to match, or work item creation will fail.

If no authentication method is specified here (via Managed Identity, Certificate, Client Secret, or PAT), the Azure DevOps connector with use the associated Managed Identity used by the function app.
