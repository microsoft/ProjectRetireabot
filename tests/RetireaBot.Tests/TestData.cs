using Microsoft.RetireaBot.Domain;
using Microsoft.RetireaBot.Models;
using Microsoft.RetireaBot.Models.Azure;


namespace Microsoft.RetireaBot.Tests
{
    internal static class TestData
    {
        public static Advisory CreateAdvisory(
            string name = "test-advisory-1",
            string typeId = "type-1",
            string impact = "High",
            string resourceGroup = "rg-1",
            string subscriptionId = "sub-1",
            string impactedField = "Microsoft.Web/sites",
            string impactedValue = "my-app-service",
            string retirementDate = "2025-08-31",
            string retirementFeatureName = "ASEv2",
            DateTime? lastUpdated = null) => new()
            {
                Id = $"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.Advisor/recommendations/{name}",
                Name = name,
                Type = "Microsoft.Advisor/recommendations",
                Properties = new AdvisoryProperties
                {
                    Category = "HighAvailability",
                    Impact = impact,
                    ImpactedField = impactedField,
                    ImpactedValue = impactedValue,
                    RecommendationTypeId = typeId,
                    LastUpdated = lastUpdated ?? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    ShortDescription = new ShortDescription
                    {
                        Problem = "App Service Environment v2 retiring",
                        Solution = "Migrate to App Service Environment v3"
                    },
                    ExtendedProperties = new ExtendedProperties
                    {
                        RetirementDate = retirementDate,
                        RetirementFeatureName = retirementFeatureName
                    },
                    ResourceMetadata = new ResourceMetadata
                    {
                        ResourceId = $"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.Web/sites/{impactedValue}"
                    }
                }
            };

        public static RetirementReport CreateMinimalReport() => new()
        {
            Result = GetRetirementsResult.Success,
            Description = "Completed",
            Advisories =
            [
                // retirementDate & retirementFeatureName left null
                CreateAdvisory(name: "a", retirementDate: null!, retirementFeatureName: null!)
            ],
            BackendOutputs =
            [
            new BackendOutputResult
                {
                    BackendName = "GitHub",
                    Status = GetRetirementsResult.Success,
                    // Error null; one WorkItem with Url null
                    Created = [ new WorkItem { Id="wi-1", Number=1, Title="t", Url = null } ],
                }
            ],
            SinkOutputs = [new DataSinkOutputResult { BackendName = "NoOp", Status = GetRetirementsResult.Success }], // Error null
            TimeElapsed = 0,
            WhatIf = false,
        };

        public static WorkItem CreateWorkItem(
            string id = "wi-1",
            int number = 1,
            string title = "test-work-item",
            WorkItemState state = WorkItemState.Open) => new()
            {
                Id = id,
                Number = number,
                Title = title,
                Body = "Migrate before the retirement date.",
                State = state,
                Labels = ["retirement"],
                Assignees = ["octocat"],
                Url = $"https://example.com/issues/{number}",
            };

        public static RetirementReport CreateRetirementReport(
            GetRetirementsResult result = GetRetirementsResult.Success,
            string description = "Completed",
            bool whatIf = false,
            double timeElapsed = 1.5) => new()
            {
                Result = result,
                Description = description,
                Advisories =
                [
                    CreateAdvisory(name: "myapp-advisory", impactedValue: "myapp", retirementDate: "2026-06-01")
                ],
                BackendOutputs =
                [
                    new BackendOutputResult
                    {
                        BackendName = "GitHub",
                        Status = GetRetirementsResult.Success,
                        Existing = [ CreateWorkItem(id: "wi-1", number: 1, title: "Existing issue") ],
                        Created = [ CreateWorkItem(id: "wi-2", number: 2, title: "New issue") ],
                        Parents =
                        [
                            new ParentWorkItemResult
                            {
                                WorkItem = CreateWorkItem(id: "wi-3", number: 3, title: "Parent issue"),
                                Action = ParentWorkItemAction.Created,
                                RecommendationTypeId = "type-1",
                                ChildCount = 1,
                            }
                        ],
                    }
                ],
                SinkOutputs =
                [
                    new DataSinkOutputResult
                    {
                        BackendName = "NoOp",
                        Status = GetRetirementsResult.Success,
                        PushedCount = 1,
                    }
                ],
                TimeElapsed = timeElapsed,
                WhatIf = whatIf,
            };

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