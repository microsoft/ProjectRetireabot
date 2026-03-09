# RetireBot

This is the repository contains the source code to RetireBot, an under-development proof-of-concept to help customers be on top of their resource migrations of EoL Azure services and or SKUs.

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

<TODO: Write Deployment section>
