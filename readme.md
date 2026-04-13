# Evergreen

This is the repository contains the source code to Evergreen, an under-development proof-of-concept to help customers be on top of their resource migrations of EoL Azure services and or SKUs.

## How it works

1. Retrieves all Azure Advisor advisories for your deployed resources in the subscriptions it has access to
2. Fetches the full advisory and extracts the key information from it
3. Checks if the advisory already has an issue, if not creates it as an issue on a specified GitHub repository and assign GitHub CoPilot to the issue
4. GitHub CoPilot attempts to resolve the issue and create a PR to review

## Requirements

- Azure Subscription(s)
- GitHub CoPilot Licenses

## Usage

By default this function will run every Monday at 00:00 `"0 0 0 * * 1`. This can be tweaked by changing the Cron expression inside of [GetRetirements.cs](Functions/GetRetirements.cs).

## Deployment

The preferred way to deploy this program is using the [Azure Developer CLI](https://learn.microsoft.com/en-us/azure/developer/azure-developer-cli/install-azd), which handles both provisioning of the architecture and the deployment of the application to a target resource group.

Before provisioning the architecture, you need to specify parameters to define the behaviour of EverGreen, copy the example json, and remove `.example`.

There are some key parameters you need to specify:

| Name                 | Required           | Description                                                                                                                         |
| -------------------- | ------------------ | ----------------------------------------------------------------------------------------------------------------------------------- |
| location             | `true`             | The location where the resources are deployed                                                                                       |
| gitHubPAT            | `true`<sub>1</sub> | Personal access token for GitHub                                                                                                    |
| gitHubAppId          | `true`<sub>2</sub> | AppId of the App Registration on GitHub                                                                                             |
| gitHubInstallId      | `true`<sub>2</sub> | InstallId for the installation of the App registration to use                                                                       |
| gitHubPrivateKeyId   | `true`<sub>2</sub> | The id of the stored GitHub App's Private Key in KeyVault                                                                           |
| gitHubPrivateKeyPath | `true`<sub>2</sub> | The path to the private key to be used to authenticate and be stored in KeyVault (path is relative to the root of this repository). |
| targetRepository     | `true`             | Target GitHub Repository to create issues on from advisories                                                                        |
| targetResourceGroup  | `false`            | The resource group EverGreen should create issues for, leave blank any resource group                                               |
| deploymentName       | `false`            | A unique application/solution name for all resources in this deployment                                                             |
| deploymentUniqueText | `false`            | Unique text value for the solution. This is used to ensure resource names are unique for global resource                            |
| enableHTTPEndpoint   | `false`            | Whether the manual HTTP endpoint should be enabled.                                                                                 |

<sub>1</sub> Only required if no App Registration authentication is defined

<sub>2</sub> Only required if no PAT is defined and if another GitHub App Registration field is defined

Note: A deployment can have both a PAT and App Registration defined, the app with run in a 'Hybrid' mode, where PAT is used a secondary method to interact with GitHub.

Once you have configured EverGreen with ensure your parameters file is called `main.parameters.json`, and run `azd up` at the root of the project directory, which it will then provision the architecture and deploy the application.
