using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Retirebot.Models
{
    public class Advisory
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public AdvisoryProperties Properties { get; set; } = new();
    }

    public class AdvisoryProperties
    {
        public string Category { get; set; } = string.Empty;
        public string Impact { get; set; } = string.Empty;
        public string ImpactedField { get; set; } = string.Empty;
        public string ImpactedValue { get; set; } = string.Empty;
        public DateTime LastUpdated { get; set; }
        public string RecommendationTypeId { get; set; } = string.Empty;
        public ExtendedProperties ExtendedProperties { get; set; } = new();
        public ResourceMetadata ResourceMetadata { get; set; } = new();
        public ShortDescription ShortDescription { get; set; } = new();
    }

    public class ExtendedProperties
    {
        public string MaturityLevel { get; set; } = string.Empty;
        public string RecommendationOfferingId { get; set; } = string.Empty;
        public string RecommendationSubCategory { get; set; } = string.Empty;
        public string? RetirementDate { get; set; }
        public string? RetirementFeatureName { get; set; }
    }

    public class ResourceMetadata
    {
        public string ResourceId { get; set; } = string.Empty;
    }

    public class ShortDescription
    {
        public string Problem { get; set; } = string.Empty;
        public string Solution { get; set; } = string.Empty;
    }
}
