using System.Text.Json.Serialization;
using Microsoft.RetireaBot.Models.Azure;

namespace Microsoft.RetireaBot.Contracts.V2026_06_25
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum GetRetirementsResult
    {
        Unknown,
        Success,
        Partial,
        Failure
    }

    public sealed class GetRetirementsResponse
    {
        [JsonPropertyName("result")] public GetRetirementsResult Result { get; set; }
        [JsonPropertyName("resultDescription")] public string ResultDescription { get; set; } = string.Empty;
        [JsonPropertyName("advisories")] public List<Advisory>? Advisories { get; set; }
        [JsonPropertyName("backendOutputs")] public List<BackendOutputResult> BackendOutputs { get; set; } = [];
        [JsonPropertyName("sinkOutputs")] public List<DataSinkOutputResult> SinkOutputs { get; set; } = [];
        [JsonPropertyName("timeElapsed")] public double TimeElapsed { get; set; }
        [JsonPropertyName("whatIf")] public bool WhatIf { get; set; }
    }
}