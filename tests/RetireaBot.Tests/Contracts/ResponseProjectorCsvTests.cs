using Microsoft.RetireaBot.Domain;
using Microsoft.RetireaBot.Models;
using Csv = Microsoft.RetireaBot.Contracts.V2026_06_25;

namespace Microsoft.RetireaBot.Tests.Contracts
{
    public class ResponseProjectorCsvTests
    {
        private const string Header =
            "Id,Name,Type,SubscriptionId,ResourceGroup,ResourceId,ServiceId,Impact,Category," +
            "ImpactedField,ImpactedValue,RetirementDate,RetirementFeatureName," +
            "RecommendationOfferingId,ShortDescriptionProblem,ShortDescriptionSolution,LastUpdated";

        private static async Task<string> RenderCsv(RetirementReport report)
        {
            var res = await new Csv.ResponseProjector().Project(report, "text/csv");
            return res.Body.ToString();
        }

        [Fact]
        public async Task Csv_Header_IsFrozen()
        {
            var csv = await RenderCsv(TestData.CreateRetirementReport());
            Assert.Equal(Header, csv.Split("\r\n")[0]);
        }

        [Fact]
        public async Task Csv_ContentType_IsCsv()
        {
            var res = await new Csv.ResponseProjector().Project(TestData.CreateRetirementReport(), "text/csv");
            Assert.Equal("text/csv; charset=utf-8", res.ContentType);
        }

        [Fact]
        public async Task Csv_SerializesToFrozenSchema()
        {
            var csv = await RenderCsv(TestData.CreateRetirementReport());

            const string expectedRow =
                "/subscriptions/sub-1/resourceGroups/rg-1/providers/Microsoft.Advisor/recommendations/myapp-advisory," +
                "myapp-advisory,Microsoft.Advisor/recommendations,sub-1,rg-1," +
                "/subscriptions/sub-1/resourceGroups/rg-1/providers/Microsoft.Web/sites/myapp," +
                "type-1,High,HighAvailability,Microsoft.Web/sites,myapp,2026-06-01,ASEv2,," +
                "App Service Environment v2 retiring,Migrate to App Service Environment v3," +
                "2025-01-01T00:00:00.0000000Z";

            Assert.Equal($"{Header}\r\n{expectedRow}\r\n", csv);
        }

        [Fact]
        public async Task Csv_NoAdvisories_HeaderOnly()
        {
            var csv = await RenderCsv(TestData.CreateRetirementReport() with { Advisories = [] });

            Assert.EndsWith("\r\n", csv);
            Assert.Single(csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries));
        }

        [Fact]
        public async Task Csv_EscapesSpecialCharacters()
        {
            var report = TestData.CreateRetirementReport() with
            {
                Advisories =
                [
                    TestData.CreateAdvisory(
                        impactedValue: "a,b",                 // comma
                        retirementFeatureName: "say \"hi\"",  // quote
                        impactedField: "line1\r\nline2")      // newline
                ],
            };
            var csv = await RenderCsv(report);

            Assert.Contains("\"a,b\"", csv);
            Assert.Contains("\"say \"\"hi\"\"\"", csv);
            Assert.Contains("\"line1\r\nline2\"", csv);
        }

        [Fact]
        public async Task Csv_NullField_EmptyCell()
        {
            var report = TestData.CreateRetirementReport() with
            {
                Advisories = [ TestData.CreateAdvisory(retirementDate: null!, retirementFeatureName: null!) ],
            };
            var csv = await RenderCsv(report);
            var row = csv.Split("\r\n")[1];

            Assert.DoesNotContain("null", csv);
            Assert.Equal(17, row.Split(',').Length);
            Assert.Contains(",,", row);
        }

        [Fact]
        public async Task Csv_LastUpdated_InvariantRoundTrip()
        {
            var report = TestData.CreateRetirementReport() with
            {
                Advisories = [ TestData.CreateAdvisory(lastUpdated: new DateTime(2025, 3, 4, 5, 6, 7, DateTimeKind.Utc)) ],
            };
            var csv = await RenderCsv(report);

            Assert.Contains("2025-03-04T05:06:07.0000000Z", csv);
        }

        [Fact]
        public async Task Csv_MultipleAdvisories_OneRowEach()
        {
            var report = TestData.CreateRetirementReport() with
            {
                Advisories = [ TestData.CreateAdvisory(name: "a"), TestData.CreateAdvisory(name: "b") ],
            };
            var csv = await RenderCsv(report);

            Assert.Equal(3, csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
        }

        [Fact]
        public async Task Project_UnsupportedMediaType_Throws()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => new Csv.ResponseProjector().Project(TestData.CreateRetirementReport(), "application/xml"));
        }

        [Fact]
        public void Schema_ColumnsMatchRecordAndValues()
        {
            var props = typeof(RetirementRow).GetProperties().Select(p => p.Name).OrderBy(n => n);
            Assert.Equal(props, RetirementRowSchema.Columns.OrderBy(n => n));

            var row = RetirementRow.FromAdvisory(TestData.CreateAdvisory());
            Assert.Equal(RetirementRowSchema.Columns.Length, RetirementRowSchema.Values(row).Count());
        }
    }
}
