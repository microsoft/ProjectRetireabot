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

Or alternatively you can:

[![Deploy to Azure](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fmicrosoft%2FProjectRetireaBot%2Fpublish%2Flatest%2Fportal.json)

Keep in mind, this will **only** deploy the architecture where azd will deploy everything.

The portal deployment presents each setting as an individual field. The grouped option objects documented below remain the interface used by azd and parameter files.

### Monitoring permissions

Set `scanOptions` to control which Azure resources RetireaBot scans, independently of work-item routing:

| Mode                               | Scan boundary                                                                                                                                                        |
| ---------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Explicit`                         | Only the fully qualified subscription and/or resource-group IDs in `scopes`. An empty list is rejected.                                                              |
| `FromRouting`                      | Scopes from `scopeId` on every mapping in enabled `perContainer` GitHub/ADO backends. Name-only and management-group mappings are rejected. No mappings is an error. |
| `DeploymentSubscription` (default) | Only the deployment subscription. Does not discover other accessible subscriptions.                                                                                  |

`assignReader` defaults to `true` and grants Reader at those same scopes. Set it to `false` for externally managed RBAC; scope validation and runtime filtering still apply. The app's storage, Key Vault, and telemetry permissions remain separate. The deployer needs module deployment and role-assignment permissions at every scope where Reader is provisioned. Cross-subscription scopes must be accessible to the managed identity in the same tenant.

For example, add this entry under `parameters` in an existing parameter file:

```json
"scanOptions": {
  "value": {
    "mode": "Explicit",
    "scopes": [
      "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg-production",
      "/subscriptions/22222222-2222-2222-2222-222222222222/resourceGroups/rg-shared"
    ],
    "assignReader": true
  }
}
```

Subscription IDs must be GUIDs. Scope IDs are case-insensitively deduplicated; RG names in explicit scope IDs accept ASCII letters, digits, underscores, hyphens, periods, and parentheses (1-90 characters, no trailing period). Resource/provider and management-group scope IDs are not accepted. Do not supply `scopes` in other modes.

RetireaBot queries the containing subscriptions directly without subscription discovery. Advisor recommendations and AKS/PostgreSQL lifecycle resources are restricted in Resource Graph and checked again before work items, sinks, or HTTP output. An RG-only scan excludes subscription-level advisories. Query/configuration failures never fall back to broader discovery. RBAC still controls what Azure makes visible; configuring a scope alone cannot grant visibility.

The portal exposes `scanMode`, `scanScopes`, and `scanAssignReader`. For direct runtime configuration, use `Scan__Mode`, `Scan__Scopes` (a JSON array), and `Scan__AssignReader`; `DeploymentSubscription` also requires `Scan__DeploymentSubscriptionId`. Deployment additionally sets `Scan__ResolvedScopes` to detect drift from provisioned scopes; redeploy after changing scopes or routing-derived boundaries.

**Defaults:** omitting `scanOptions` uses `DeploymentSubscription` with `assignReader: true`, provisioning Reader on the deployment subscription. Routing-derived access is opt-in through `FromRouting`; `advisoryOptions.targetResourceGroup` is only a work-item filter. For an existing RG-only deployment, select `Explicit` or `FromRouting` before redeploying to avoid adding subscription Reader.

**Migration:** choose an explicit mode, deploy both infrastructure and application code, then explicitly remove obsolete Reader assignments and check inherited grants. Incremental deployments do not delete old assignments, and `assignReader: false` does not revoke them. Existing routing and `targetResourceGroup` remain output settings, not scan boundaries. Live RG-only managed-identity verification is a separate deployment step.

### Parameters

Before provisioning the architecture, copy the example that matches your scenario to `infra/main.parameters.json` and replace its placeholder values:

| Scenario                                             | Example file                                      |
| ---------------------------------------------------- | ------------------------------------------------- |
| GitHub issues                                        | `infra/examples/github.parameters.json`           |
| Azure DevOps work items                              | `infra/examples/ado.parameters.json`              |
| Power BI output                                      | `infra/examples/powerbi.parameters.json`          |
| GitHub, Azure DevOps, Power BI                       | `infra/examples/mixed.parameters.json`            |
| Routing-derived RG scan with GitHub and Azure DevOps | `infra/examples/mixed-work-items.parameters.json` |
| Explicit RG-only scan with HTTP output               | `infra/examples/rg-only.parameters.json`          |

The GitHub, Azure DevOps, Power BI, and mixed-output examples use `"scanOptions": { "value": {} }` to inherit the deployment-subscription scan and Reader defaults. The routing-derived example scans only the RG identified by its GitHub mapping's `scopeId`; its monolithic Azure DevOps routing does not add scan scopes.

For either RG-scoped example, replace the placeholder subscription ID and resource-group name with an existing group before deployment. Reader provisioning remains enabled by default; add `"assignReader": false` inside the `scanOptions.value` object if access is managed externally. This does not revoke existing assignments.

The HTTP-only example needs no GitHub, Azure DevOps, or Power BI credentials. After deploying infrastructure and application code, invoke `/api/GetRetirementsManual?api-version=2026-06-25-preview&whatIf=true` with the Function key in the `x-functions-key` header. Output is returned over HTTP, with no work-item or sink writes.

There are some key parameters you need to specify:

| Name                    | Required            | Description                                                                                                                                                                       |
| ----------------------- | ------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| dataSinkBackend         | `true`              | What data sink backends RetireaBot should use to push data to (Valid Options: '`PowerBI`') Multiple can be used at once                                                           |
| location                | `true`              | The location where the resources are deployed                                                                                                                                     |
| workItemBackend         | `true`              | What work item backends RetireaBot should use to create work items in (Valid options: '`GitHub`', '`AzureDevOps`') Multiple can be used at once                                   |
| deploymentName          | `false`             | A unique application/solution name for all resources in this deployment                                                                                                           |
| deploymentUniqueText    | `false`             | Unique text value for the solution. This is used to ensure resource names are unique for global resource                                                                          |
| timerTrigger            | `false`             | NCRONTAB expression for the scheduled timer trigger. Default: `0 0 0 * * 1` (every Monday at 00:00 UTC)                                                                           |
| advisoryOptions         | `false`             | Behavior and configuration of advisories when passed to a backend, see [Advisory Settings](#advisory)                                                                             |
| scanOptions             | `false`             | Scan mode, approved scope IDs, and optional Reader provisioning; defaults to `DeploymentSubscription` with Reader enabled. See [Monitoring permissions](#monitoring-permissions). |
| lifecycleSignalsOptions | `false`             | How Lifecycle Signals should be handled and processed by RetireaBot, see [Lifecycle Signals](#lifecycle-signals)                                                                  |
| httpEndpoint            | `false`             | Settings related to HTTP Endpoints, whether they should be enabled, include detailed output and dry running. See [HTTP Options](#http-endpoint)                                   |
| azureDevOpsOptions      | `false`<sub>2</sub> | Settings for Azure DevOps authentication and work item creation, see [Azure DevOps Settings](#azure-devops)                                                                       |
| gitHubOptions           | `false`<sub>2</sub> | Settings for GitHub authentication, issue creation, and assigning CoPilot, see [GitHub Settings](#github)                                                                         |
| powerBIOptions          | `false`<sub>2</sub> | Settings for Power BI authentication and retirement advisory data output, see [Power BI Settings](#power-bi)                                                                      |

<sub>2</sub> Required when the backend is enabled in `dataSinkBackend` or `workItemBackend`

#### Advisory

Settings related to selecting which Azure Advisor recommendations RetireaBot processes are set under the `advisoryOptions` setting object.

| Name                      | Required | Description                                                                                                        |
| ------------------------- | -------- | ------------------------------------------------------------------------------------------------------------------ |
| includeResolvedAdvisories | `false`  | Whether resolved or completed Azure Advisor advisories should be included when creating work items. Default: false |
| targetResourceGroup       | `false`  | Resource-group work-item filter. Does not change scan scopes or Reader assignments.                                |

#### Lifecycle Signals

Settings related to generating advisories from supported service lifecycle policies are set under the `lifecycleSignalsOptions` setting object.

| Name              | Required | Description                                                                                                                                                  |
| ----------------- | -------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| enabled           | `false`  | Whether RetireaBot should check and create advisories for services reaching end of life (supported services: AKS, PostgreSQL flexible server) Default: false |
| warningWindowDays | `false`  | How many days before a published end-of-life date a deployed resource should start producing a work item. Default: 180                                       |

#### HTTP Endpoint

Settings related to the HTTP endpoint are set under the `httpEndpoint` setting object

| Name          | Required | Description                                                                                       |
| ------------- | -------- | ------------------------------------------------------------------------------------------------- |
| enabled       | `false`  | Whether the manual HTTP endpoint should be enabled. Default: false                                |
| includeOutput | `false`  | Whether the manual HTTP endpoint should display extended information about its run Default: false |
| allowWhatIf   | `false`  | Whether the manual HTTP endpoint should allow users to run dry-runs Default: false                |

### GitHub

When `workItemBackend` includes `GitHub`, you have some additional properties you can set under the `gitHubOptions`

| Name                  | Required           | Description                                                                                                                         |
| --------------------- | ------------------ | ----------------------------------------------------------------------------------------------------------------------------------- |
| PAT                   | `true`<sub>1</sub> | Personal access token for GitHub                                                                                                    |
| appId                 | `true`<sub>2</sub> | AppId of the App Registration on GitHub                                                                                             |
| installId             | `true`<sub>2</sub> | InstallId for the installation of the App registration to use                                                                       |
| privateKeyId          | `true`<sub>2</sub> | The id of the stored GitHub App's Private Key in KeyVault                                                                           |
| privateKeyPath        | `true`<sub>2</sub> | The path to the private key to be used to authenticate and be stored in KeyVault (path is relative to the root of this repository). |
| routing               | `true`             | Settings for routing work items to repositories based on their Azure container, see [Repository Settings](#repository)              |
| assignCopilot         | `false`            | Whether GitHub CoPilot should be assigned to try and mitigate issues (requires a GitHub CoPilot license) Default: false             |
| workItemConfiguration | `false`            | Optional overrides for the labels and work items created from advisories, see [Work Item Settings](#work-item)                      |

<sub>1</sub> Only required if no App Registration authentication is defined

<sub>2</sub> Only required if no PAT is defined and if another GitHub App Registration field is defined

Note: A deployment can have both a PAT and App Registration defined, the app with run in a 'Hybrid' mode, where PAT is used a secondary method to interact with GitHub.

Once you have configured RetireaBot with ensure your parameters file is called `main.parameters.json`, and run `azd up` at the root of the project directory, which it will then provision the architecture and deploy the application.

### Azure DevOps

When `workItemBackend` includes `AzureDevOps`, you have some additional properties to configure how RetireaBot interacts with ADO.

| Name                    | Required            | Description                                                                                                            |
| ----------------------- | ------------------- | ---------------------------------------------------------------------------------------------------------------------- |
| organisationUrl         | `true`              | URL of the Azure DevOps Organisation                                                                                   |
| routing                 | `true`              | Settings for routing work items to repositories based on their Azure container, see [Repository Settings](#repository) |
| PAT                     | `false`             | The PAT that allows RetireaBot to interact with your Azure DevOps organisation                                         |
| clientId                | `false`             | The client ID of the app registration used to authenticate with Azure DevOps                                           |
| tenantId                | `false`             | The tenant ID of the app registration used to authenticate with Azure DevOps                                           |
| clientSecret            | `false`             | The client secret of the app registration used to authenticate with Azure DevOps                                       |
| certificateId           | `false`             | The ID the certificate should be stored as in the KeyVault for Azure DevOps certificate auth                           |
| certificatePath         | `false`             | The path of the certificate file (PFX/PEM) to be imported into the KeyVault for Azure DevOps certificate auth          |
| workItemConfiguration   | `false`             | Optional overrides for the labels and work items created from advisories, see [Work Item Settings](#work-item)         |
| workItemDefaultAssignee | `false`<sub>1</sub> | (Optional) The default assignee for work items created by RetireaBot in Azure DevOps                                   |
| workItemOpenState       | `false`<sub>1</sub> | (Optional) The state to use when opening work items in Azure DevOps. Default: New                                      |
| workItemClosedState     | `false`<sub>1</sub> | (Optional) The state to use when closing work items in Azure DevOps. Default: Closed                                   |
| workItemType            | `false`<sub>1</sub> | (Optional) The work item type to create in Azure DevOps. Default: Task                                                 |

<sub>1</sub> Defaults of these values are set to the "Agile" Process. If you are using a different process for your project, you need to set these values to match, or work item creation will fail.

If no authentication method is specified here (via Managed Identity, Certificate, Client Secret, or PAT), the Azure DevOps connector with use the associated Managed Identity used by the function app.

### Shared Options

These settings are present on every work item backend

Nested option objects are merged with the defaults shown below. Parameter files only need to include values that differ from those defaults.

#### Work Item

Settings related to the labels and work items created from advisories are set under the `workItemConfiguration` setting object on the work item backend.

| Name                      | Required | Description                                                                                                                        |
| ------------------------- | -------- | ---------------------------------------------------------------------------------------------------------------------------------- |
| advisoryLabel             | `false`  | What label should be attached to all work items to identify it was created by RetireaBot. Default: advisor                         |
| advisoryLabelPrefix       | `false`  | What prefix should be applied to label that uniquely identifies a work item based on their advisory. Default: advisor-             |
| advisoryParentLabel       | `false`  | What label should be attached to parent work items to identify them. Default: tracking                                             |
| advisoryParentLabelPrefix | `false`  | What prefix should be applied to label that uniquely identifies a parent work item based on their advisory. Default: advisor-type- |
| createParentWorkItems     | `false`  | Whether parent work items should be created when processing advisories to track child work items                                   |
| createChildWorkItems      | `false`  | Whether child work items should be created when processing advisories                                                              |
| includeResourceId         | `false`  | Whether the advisories should contain the resource ID of the affected resource (Default: false)                                    |

#### Repository

Settings for routing work items to repositories or projects are set under the `routing` object on each work item backend.

| Name                         | Required | Description                                                                                                                                                                      |
| ---------------------------- | -------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| targetRepository<sub>1</sub> | `true`   | Target project or repository to create work items on from advisories                                                                                                             |
| containerMappings            | `false`  | If "perContainer" mode is being used, these mappings decide which repositories issues are created in based on their containing resource group, subscription, or management group |
| useTriageRepoForUnmapped     | `false`  | Should unmapped resources have their work items created in the triage repository/target repository in perContainer mode. Default: false                                          |
| unmappedRepository           | `false`  | The repository work items should be created in when they are not mapped in perContainer mode.                                                                                    |
| scope                        | `false`  | Whether issues should be created in one "triage" repository or should be shared across multiple repositories with a parent issue in the repository. Default "monolithic"         |

<sub>1</sub> When `scope` is set to "perContainer", the repository specified here will be treated as the parent repository

##### Mapping containers to repositories

When `scope` is set to `perContainer`, `containerMappings` decides which repository a work item is created in based on the Azure container the advisory's resource belongs to. Supported containers are resource groups, subscriptions, and management groups.

Each entry has the following properties:

| Name      | Description                                                                                                                                                                                   |
| --------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `name`    | The identifier of the container to match. For `ResourceGroup` use the resource group name, for `Subscription` use the subscription ID, and for `ManagementGroup` use the management group ID. |
| `type`    | The type of container to match. Valid options: `ResourceGroup`, `Subscription`, `ManagementGroup`.                                                                                            |
| `target`  | The repository or project work items should be created in for resources in this container.                                                                                                    |
| `scopeId` | Fully qualified subscription/RG ID. Required in `FromRouting` scan mode. When supplied, `name` and `type` may be omitted; if present they must agree.                                         |

Use `scopeId` to distinguish identically named resource groups in different subscriptions. It matches the advisory's affected resource, not just its recommendation ID. For example, combine `"scanOptions": { "mode": "FromRouting", "assignReader": true }` with this backend routing object (inside parameter `value` objects as appropriate):

```json
{
  "scope": "perContainer",
  "targetRepository": "ExampleUser/ParentRepo",
  "containerMappings": [
    {
      "scopeId": "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg-production",
      "target": "ExampleUser/ProductionRepo"
    },
    {
      "scopeId": "/subscriptions/22222222-2222-2222-2222-222222222222/resourceGroups/rg-production",
      "target": "ExampleUser/OtherProductionRepo"
    }
  ]
}
```

In `Explicit` mode, routing never expands the scan boundary. Name/type-only mappings remain supported for output routing outside `FromRouting` mode.

For example, the complete `routing` object can be configured as follows:

```json
{
  "routing": {
    "scope": "perContainer",
    "targetRepository": "ExampleUser/ParentRepo",
    "useTriageForUnmapped": false,
    "unmappedRepository": "ExampleUser/UnmappedRepo",
    "containerMappings": [
      {
        "name": "rg-production",
        "type": "ResourceGroup",
        "target": "ExampleUser/ProductionRepo"
      },
      {
        "name": "9d82d3a7-4bf7-49b4-9cbd-87a9d46d3423",
        "type": "Subscription",
        "target": "ExampleUser/SubscriptionRepo"
      },
      {
        "name": "b74c2c54-5e3f-4e94-9be5-88ebfce5ee9e",
        "type": "ManagementGroup",
        "target": "ExampleUser/PlatformRepo"
      }
    ]
  }
}
```

Resources that don't match any mapping fall back to the triage/target repository, or to `unmappedRepository` when `useTriageRepoForUnmapped` is `false`.

### Power BI

When `dataSinkBackend` includes `PowerBI`, you need to configure the following parameters for the Power BI data sink.

| Name            | Required            | Description                                                                                                                                    |
| --------------- | ------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------- |
| clientId        | `true`<sub>1</sub>  | The client ID of the app registration used to authenticate with Power BI                                                                       |
| tenantId        | `true`<sub>1</sub>  | The tenant ID of the app registration used to authenticate with Power BI                                                                       |
| clientSecret    | `false`<sub>1</sub> | The client secret of the app registration used to authenticate with Power BI                                                                   |
| certificateId   | `false`<sub>1</sub> | The ID of the certificate of the app registration used to authenticate with Power BI                                                           |
| certificatePath | `false`<sub>1</sub> | The path of the certificate file (PFX/PEM) to be imported into the KeyVault for Power BI certificate auth                                      |
| workspaceId     | `true`              | The ID of the workspace the dataset is located in                                                                                              |
| datasetId       | `true`              | The ID of the dataset RetireaBot should push data into                                                                                         |
| tableName       | `true`              | The table name that RetireaBot should create rows for                                                                                          |
| writeMode       | `false`             | How RetireaBot should write to a PowerBI dataset. (Allowed: Append, Snapshot (deletes previous rows, before pushing new data)) Default: Append |

<sub>1</sub> Only required when using a separate managed identity, client secret or certificate authentication is required.

If no authentication method is specified here (via Managed Identity, Certificate, or Client Secret), the PowerBI connector with use the associated Managed Identity used by the function app.
