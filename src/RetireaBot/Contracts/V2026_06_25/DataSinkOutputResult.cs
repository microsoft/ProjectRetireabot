using System.Text.Json.Serialization;

namespace Microsoft.RetireaBot.Contracts.V2026_06_25
{
    public class DataSinkOutputResult
    {
        [JsonPropertyName("backendName")] public required string BackendName { get; set; }      // "NoOp", "PowerBI", ...
        [JsonPropertyName("status")] public required GetRetirementsResult Status { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
        [JsonPropertyName("pushedCount")] public int PushedCount { get; set; }
    }
}