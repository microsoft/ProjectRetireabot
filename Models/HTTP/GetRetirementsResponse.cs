using System.Text.Json.Serialization;
using Retirebot.Models.Azure;

namespace Retirebot.Models.HTTP
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum GetRetirementsResult
    {
        Unknown,
        Success,
        Failure
    }

    public class GetRetirementsResponse
    {
        public GetRetirementsResult Result { get; set; }
        public string ResultDescription { get; set; } = string.Empty;
        public List<Advisory>? Advisories { get; set; }
        public List<WorkItem>? ExistingWorkItems { get; set; }
        public List<WorkItem>? NewWorkItems { get; set; }
        public List<ParentWorkItemResult>? ParentWorkItems { get; set; }
        public double TimeElapsed { get; set; }
        public bool WhatIf { get; set; }
    }
}