using Microsoft.RetireaBot.Domain;


namespace Microsoft.RetireaBot.Tests.Contracts
{
    public class ResponseProjectorTests
    {
        [Fact]
        public async Task Project_FlattensAdvisoryAndMapsEnums()
        {
            var report = new RetirementReport
            {
                Result = GetRetirementsResult.Partial,
                Description = "x",
                Advisories =
                [
                   TestData.CreateAdvisory(name: "myapp-advisory", retirementDate: "2026-06-01", impactedValue: "myapp")
                ],
            };

            var res = await new RetireaBot.Contracts.V2026_06_25.ResponseProjector().Project(report, "application/json");
            var json = res.Body.ToString();

            Assert.Contains("\"result\":\"Partial\"", json);
            Assert.Contains("\"resourceName\":\"myapp\"", json);
            Assert.Contains("\"retirementDate\":\"2026-06-01\"", json);
        }

        [Fact]
        public async Task Project_SerializesToFrozenSchema()
        {
            var res = await new RetireaBot.Contracts.V2026_06_25.ResponseProjector().Project(SampleReport(), "application/json");

            Assert.Equal(ExpectedJson, res.Body.ToString());
        }

        private static RetirementReport SampleReport() =>
            TestData.CreateRetirementReport(
                result: GetRetirementsResult.Success,
                description: "Completed",
                whatIf: false,
                timeElapsed: 1.5);

        private const string ExpectedJson = """{"result":"Success","resultDescription":"Completed","advisories":[{"id":"/subscriptions/sub-1/resourceGroups/rg-1/providers/Microsoft.Advisor/recommendations/myapp-advisory","name":"myapp-advisory","type":"Microsoft.Advisor/recommendations","subscriptionId":"sub-1","resourceGroup":"rg-1","resourceName":"myapp","category":"HighAvailability","impact":"High","retirementDate":"2026-06-01","retirementFeatureName":"ASEv2","lastUpdated":"2025-01-01T00:00:00Z","problem":"App Service Environment v2 retiring","solution":"Migrate to App Service Environment v3"}],"backendOutputs":[{"backendName":"GitHub","status":"Success","existingCount":1,"created":[{"id":"wi-2","number":2,"title":"New issue","state":"Open","url":"https://example.com/issues/2"}],"parents":[{"workItem":{"id":"wi-3","number":3,"title":"Parent issue","state":"Open","url":"https://example.com/issues/3"},"action":"Created","recommendationTypeId":"type-1","childCount":1}]}],"sinkOutputs":[{"backendName":"NoOp","status":"Success","pushedCount":1}],"timeElapsed":1.5,"whatIf":false}""";


        [Fact]
        public async Task Project_OmitsNullFields()
        {
            var res = await new RetireaBot.Contracts.V2026_06_25.ResponseProjector().Project(TestData.CreateMinimalReport(), "application/json");
            var json = res.Body.ToString();

            Assert.DoesNotContain("retirementDate", json);
            Assert.DoesNotContain("retirementFeatureName", json);
            Assert.DoesNotContain("\"error\"", json);
            Assert.DoesNotContain("\"url\"", json);
            Assert.DoesNotContain("null", json);
        }

        [Fact]
        public async Task Project_NullAdvisories_OmitKey()
        {
            var res = await new RetireaBot.Contracts.V2026_06_25.ResponseProjector().Project(TestData.CreateMinimalReport() with { Advisories = null! }, "application/json");

            Assert.DoesNotContain("advisories", res.Body.ToString());
        }
    }
}