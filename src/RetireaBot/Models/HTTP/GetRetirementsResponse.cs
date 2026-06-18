using System.Text.Json.Serialization;
using Microsoft.RetireaBot.Models.Azure;

namespace Microsoft.RetireaBot.Models.HTTP
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum GetRetirementsResult
    {
        Unknown,
        Success,
        Partial,
        Failure
    }

    public class GetRetirementsResponse
    {
        public GetRetirementsResult Result { get; set; }
        public string ResultDescription { get; set; } = string.Empty;
        public List<Advisory>? Advisories { get; set; }
        public List<BackendOutputResult> BackendOutputs { get; set; } = [];
        public List<DataSinkOutputResult> SinkOutputs { get; set; } = [];
        public double TimeElapsed { get; set; }
        public bool WhatIf { get; set; }
    }
}