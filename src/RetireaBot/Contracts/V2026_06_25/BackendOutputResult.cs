using System.Text.Json.Serialization;

namespace Microsoft.RetireaBot.Contracts.V2026_06_25
{
    public class BackendOutputResult
    {
        [JsonPropertyName("backendName")] public required string BackendName { get; set; }      // "GitHub", "AzureDevOps", "PowerBI"…
        [JsonPropertyName("status")] public required GetRetirementsResult Status { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
        [JsonPropertyName("existingCount")] public int ExistingCount { get; set; }
        [JsonPropertyName("created")] public List<WorkItem> Created { get; set; } = [];
        [JsonPropertyName("parents")] public List<ParentWorkItemResult> Parents { get; set; } = [];
    }
}