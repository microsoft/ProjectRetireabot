using System.Globalization;

namespace Microsoft.RetireaBot.Models
{
    public sealed record RetirementRow()
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string Type { get; init; }
        public string? SubscriptionId { get; init; }
        public string? ResourceGroup { get; init; }
        public string? ResourceId { get; init; }
        public string? ServiceId { get; init; }
        public string? Impact { get; init; }
        public string? Category { get; init; }
        public string? ImpactedField { get; init; }
        public string? ImpactedValue { get; init; }
        public string? RetirementDate { get; init; }
        public string? RetirementFeatureName { get; init; }
        public string? RecommendationOfferingId { get; init; }
        public string? ShortDescriptionProblem { get; init; }
        public string? ShortDescriptionSolution { get; init; }
        public string? LastUpdated { get; init; }

        public static RetirementRow FromAdvisory(Models.Azure.Advisory a) => new()
        {
            Id = a.Id,
            Name = a.Name,
            Type = a.Type,
            SubscriptionId = a.GetSubscriptionId(),
            ResourceGroup = a.GetResourceGroupName(),
            ResourceId = a.Properties.ResourceMetadata.ResourceId,
            ServiceId = a.Properties.RecommendationTypeId,
            Impact = a.Properties.Impact,
            Category = a.Properties.Category,
            ImpactedField = a.Properties.ImpactedField,
            ImpactedValue = a.Properties.ImpactedValue,
            RetirementDate = a.Properties.ExtendedProperties.RetirementDate,
            RetirementFeatureName = a.Properties.ExtendedProperties.RetirementFeatureName,
            RecommendationOfferingId = a.Properties.ExtendedProperties.RecommendationOfferingId,
            ShortDescriptionProblem = a.Properties.ShortDescription.Problem,
            ShortDescriptionSolution = a.Properties.ShortDescription.Solution,
            LastUpdated = a.Properties.LastUpdated.ToString("O", CultureInfo.InvariantCulture),
        };
    }

    public static class RetirementRowSchema
    {
        public static readonly string[] Columns =
        {
            "Id","Name","Type","SubscriptionId","ResourceGroup","ResourceId","ServiceId",
            "Impact","Category","ImpactedField","ImpactedValue","RetirementDate",
            "RetirementFeatureName","RecommendationOfferingId",
            "ShortDescriptionProblem","ShortDescriptionSolution","LastUpdated",
        };

        public static IEnumerable<string?> Values(RetirementRow r) =>
            new[] { r.Id, r.Name, r.Type, r.SubscriptionId, r.ResourceGroup, r.ResourceId, r.ServiceId,
            r.Impact, r.Category, r.ImpactedField, r.ImpactedValue, r.RetirementDate,
            r.RetirementFeatureName, r.RecommendationOfferingId,
            r.ShortDescriptionProblem, r.ShortDescriptionSolution, r.LastUpdated
        };
    }
}