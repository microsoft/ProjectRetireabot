using Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.RetireaBot.Domain;
using Microsoft.RetireaBot.Models;
using Microsoft.RetireaBot.Models.Azure;

using Microsoft.RetireaBot.Models.Sinks.PowerBI;

namespace Microsoft.RetireaBot.Helpers.Sinks.PowerBI
{
    public sealed class PowerBIDataSink : IDataSinkClient
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _config;
        private readonly ILogger<PowerBIDataSink> _logger;

        public PowerBIDataSink(HttpClient httpClient, IConfiguration config, ILoggerFactory loggerFactory)
        {
            _httpClient = httpClient;
            _config = config;
            _logger = loggerFactory.CreateLogger<PowerBIDataSink>();
        }

        public DataSinkBackend Backend => DataSinkBackend.PowerBI;

        public async Task<DataSinkOutputResult> PushAsync(IReadOnlyList<Advisory> advisories, bool whatIf, CancellationToken cancellationToken = default)
        {
            if (whatIf)
            {
                _logger.LogInformation("[PowerBI] Dry-run requested; skipping push for {Count} advisories", advisories.Count);
                return new DataSinkOutputResult
                {
                    BackendName = Backend.ToString(),
                    Status = GetRetirementsResult.Success,
                    PushedCount = advisories.Count,
                };
            }

            WriteMode writeMode = Enum.Parse<WriteMode>(_config.GetSection(ConfigKeys.PowerBI.WriteMode).Get<string>() ?? "Append", ignoreCase: true);

            string? datasetId = _config.GetSection(ConfigKeys.PowerBI.DatasetId).Get<string>();
            string? tableName = _config.GetSection(ConfigKeys.PowerBI.TableName).Get<string>();
            if (string.IsNullOrWhiteSpace(datasetId))
            {
                throw new InvalidOperationException("PowerBI:DatasetId is not configured.");
            }

            if (string.IsNullOrWhiteSpace(tableName))
            {
                throw new InvalidOperationException("PowerBI:TableName is not configured.");
            }

            string? workspaceId = _config.GetSection(ConfigKeys.PowerBI.WorkspaceId).Get<string>();
            string endpoint = string.IsNullOrWhiteSpace(workspaceId)
                ? $"v1.0/myorg/datasets/{Uri.EscapeDataString(datasetId)}/tables/{Uri.EscapeDataString(tableName)}/rows"
                : $"v1.0/myorg/groups/{Uri.EscapeDataString(workspaceId)}/datasets/{Uri.EscapeDataString(datasetId)}/tables/{Uri.EscapeDataString(tableName)}/rows";

            if (writeMode == WriteMode.Snapshot)
            {
                using HttpResponseMessage deleteResponse = await _httpClient.DeleteAsync(endpoint, cancellationToken);
                if (!deleteResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("[PowerBI] Failed to clear rows ({StatusCode}); falling back to append", (int)deleteResponse.StatusCode);
                }
            }

            var rows = advisories.Select(ToPowerBiRow).ToList();
            _logger.LogInformation("[PowerBI] Pushing {Count} advisory rows to dataset {DatasetId} table {TableName} (endpoint {Endpoint})", rows.Count, datasetId, tableName, endpoint);

            using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(endpoint, new { rows }, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"Power BI push failed ({(int)response.StatusCode} {response.ReasonPhrase}) for {endpoint}. {body}");
            }

            return new DataSinkOutputResult
            {
                BackendName = Backend.ToString(),
                Status = GetRetirementsResult.Success,
                PushedCount = rows.Count,
            };
        }

        private static object ToPowerBiRow(Advisory advisory) => new
        {
            Id = advisory.Id,
            Name = advisory.Name,
            Type = advisory.Type,
            SubscriptionId = advisory.GetSubscriptionId(),
            ResourceGroup = advisory.GetResourceGroupName(),
            ResourceId = advisory.Properties.ResourceMetadata.ResourceId,
            ServiceId = advisory.Properties.RecommendationTypeId,
            Impact = advisory.Properties.Impact,
            Category = advisory.Properties.Category,
            ImpactedField = advisory.Properties.ImpactedField,
            ImpactedValue = advisory.Properties.ImpactedValue,
            RetirementDate = advisory.Properties.ExtendedProperties.RetirementDate,
            RetirementFeatureName = advisory.Properties.ExtendedProperties.RetirementFeatureName,
            RecommendationOfferingId = advisory.Properties.ExtendedProperties.RecommendationOfferingId,
            ShortDescriptionProblem = advisory.Properties.ShortDescription.Problem,
            ShortDescriptionSolution = advisory.Properties.ShortDescription.Solution,
            LastUpdated = advisory.Properties.LastUpdated,
        };
    }
}