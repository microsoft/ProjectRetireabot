using System.Security.Cryptography;
using System.Text;
using Microsoft.RetireaBot.Models.Azure;
using Microsoft.RetireaBot.Models.Lifecycle;

namespace Microsoft.RetireaBot.Helpers.Lifecycle
{
    public class LifecycleAdvisoryGenerator
    {
        public static Advisory? TryCreate(
           VersionedResource resource,
           IReadOnlyDictionary<string, LifecycleEntry> entriesByVersion,
           LifecycleProduct product,
           DateTime now,
           TimeSpan warningWindow)
        {
            if (string.IsNullOrWhiteSpace(resource.Version)) return null;

            string normalisedVersion = NormaliseVersion(product, resource.Version);
            if (!entriesByVersion.TryGetValue(normalisedVersion, out var entry)) return null;

            TimeSpan timeUntilEol = entry.EndOfLife - now;
            if (timeUntilEol > warningWindow) return null;

            string productName = ProductDisplayName(product);
            string featureName = $"{productName} {normalisedVersion}";
            bool past = timeUntilEol.Ticks < 0;

            string problem = past
                ? $"{featureName} reached end of life on {entry.EndOfLife:yyyy-MM-dd}"
                : $"{featureName} reaches end of life on {entry.EndOfLife:yyyy-MM-dd}";
            string solution = $"Upgrade {resource.Name} to a supported {productName} version before {entry.EndOfLife:yyyy-MM-dd}.";

            string recommendationTypeId = $"retireabot-lifecycle-{product}-{normalisedVersion}".ToLowerInvariant();
            string name = StableAdvisoryName(recommendationTypeId, resource.Id);

            return new Advisory
            {
                Id = $"/subscriptions/{resource.SubscriptionId}/resourceGroups/{resource.ResourceGroup}/providers/Microsoft.RetireaBot/lifecycleRecommendations/{name}",
                Name = name,
                Type = "Microsoft.RetireaBot/lifecycleRecommendations",
                Properties = new AdvisoryProperties
                {
                    Category = "HighAvailability",
                    Impact = past ? "High" : "Medium",
                    ImpactedField = resource.Type,
                    ImpactedValue = resource.Name,
                    LastUpdated = now,
                    RecommendationTypeId = recommendationTypeId,
                    ShortDescription = new ShortDescription { Problem = problem, Solution = solution },
                    ExtendedProperties = new ExtendedProperties
                    {
                        MaturityLevel = "GA",
                        RecommendationOfferingId = recommendationTypeId,
                        RecommendationSubCategory = "ServiceUpgradeAndRetirement",
                        RetirementDate = entry.EndOfLife.ToString("yyyy-MM-dd"),
                        RetirementFeatureName = featureName,
                    },
                    ResourceMetadata = new ResourceMetadata { ResourceId = resource.Id },
                },
            };
        }

        public static string NormaliseVersion(LifecycleProduct product, string version)
        {
            string trimmed = version.Trim().TrimStart('v', 'V');
            return product switch
            {
                // Match "1.31.x" → "1.31"
                LifecycleProduct.AksKubernetes => string.Join('.', trimmed.Split('.').Take(2)),
                // PostgreSQL flexible server reports the major version directly, e.g. "13".
                LifecycleProduct.AzurePostgreSQLFlexible => trimmed.Split('.')[0],
                _ => trimmed,
            };
        }

        public static string ProductDisplayName(LifecycleProduct product) => product switch
        {
            LifecycleProduct.AksKubernetes => "AKS Kubernetes",
            LifecycleProduct.AzurePostgreSQLFlexible => "Azure Database for PostgreSQL flexible server",
            _ => product.ToString(),
        };

        private static string StableAdvisoryName(string recommendationTypeId, string resourceId)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{recommendationTypeId}|{resourceId}"));
            return $"lifecycle-{Convert.ToHexString(hash, 0, 8).ToLowerInvariant()}";
        }
    }
}