namespace Microsoft.RetireaBot.Tests
{
    internal static class TestData
    {
        public static object CreateRetirementQueryResponse(int count = 1) => new
        {
            count,
            totalRecords = count,
            data = Enumerable.Range(1, count).Select(i => new
            {
                id = $"/subscriptions/sub-1/resourceGroups/rg-1/providers/Microsoft.Advisor/recommendations/rec-{i}",
                name = $"rec-{i}",
                type = "Microsoft.Advisor/recommendations",
                subscriptionId = "sub-1",
                resourceGroup = "rg-1",
                location = "eastus",
                resourceId = $"/subscriptions/sub-1/resourceGroups/rg-1/providers/Microsoft.Web/sites/myapp-{i}",
                ServiceID = "type-1",
                impact = "High",
                category = "HighAvailability",
                impactedField = "Microsoft.Web/sites",
                impactedValue = $"myapp-{i}",
                lastUpdated = "2025-01-01",
                retirementDate = "2026-06-01",
                retirementFeatureName = "App Service",
                maturityLevel = "GA",
                recommendationOfferingId = "offering-1",
            }).ToArray()
        };
    }
}