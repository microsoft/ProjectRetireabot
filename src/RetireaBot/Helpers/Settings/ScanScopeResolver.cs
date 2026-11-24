using System.Text.RegularExpressions;
using Microsoft.RetireaBot.Models;
using Microsoft.RetireaBot.Models.Azure;

namespace Microsoft.RetireaBot.Helpers.Settings
{
    public static class ScanScopeResolver
    {
        public static ResolvedScanOptions Resolve(
            ScanOptions options,
            string deploymentSubscriptionId,
            IEnumerable<IVendorSettings> enabledBackends)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(options.Scopes);
            ArgumentNullException.ThrowIfNull(enabledBackends);

            if (!Enum.IsDefined(options.Mode))
            {
                throw new InvalidOperationException($"Unsupported scan mode: {options.Mode}.");
            }

            if (options.Mode != ScanMode.Explicit && options.Scopes.Length != 0)
            {
                throw new InvalidOperationException("Scan scopes may only be supplied in Explicit mode.");
            }

            IEnumerable<ScanScope> scopes = options.Mode switch
            {
                ScanMode.Explicit => options.Scopes.Select(ParseScope),
                ScanMode.FromRouting => enabledBackends
                    .Where(backend => backend.WorkItemScope == WorkItemScope.PerContainer)
                    .SelectMany(backend => backend.TargetContainerMapping)
                    .Select(ParseMapping),
                ScanMode.DeploymentSubscription => [ParseScope($"/subscriptions/{deploymentSubscriptionId}")],
                _ => throw new InvalidOperationException($"Unsupported scan mode: {options.Mode}.")
            };

            ScanScope[] resolved = scopes
                .DistinctBy(scope => scope.ScopeId, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (resolved.Length == 0)
            {
                throw new InvalidOperationException($"{options.Mode} scan mode requires at least one subscription or resource-group scope.");
            }

            return new ResolvedScanOptions(options.Mode, Array.AsReadOnly(resolved), options.AssignReader);
        }

        private static ScanScope ParseMapping(AzureRepositoryMap mapping)
        {
            if (string.IsNullOrWhiteSpace(mapping.ScopeId))
            {
                throw new InvalidOperationException("FromRouting scan mode requires scopeId on every enabled perContainer mapping; names alone are not scan scopes.");
            }

            AzureRepositoryMap normalized = NormalizeMapping(mapping);
            return ParseScope(normalized.ScopeId!);
        }

        public static AzureRepositoryMap NormalizeMapping(AzureRepositoryMap mapping)
        {
            if (mapping.ScopeId == null)
            {
                if (mapping.Name == null || mapping.Type == null)
                {
                    throw new InvalidOperationException("Routing mappings require scopeId, or both name and type.");
                }
                return mapping;
            }

            ScanScope scope = ParseScope(mapping.ScopeId);
            AzureContainerType type = scope.ResourceGroupName == null
                ? AzureContainerType.Subscription : AzureContainerType.ResourceGroup;
            string name = scope.ResourceGroupName ?? scope.SubscriptionId;
            if ((mapping.Type != null && mapping.Type != type)
                || (mapping.Name != null && !string.Equals(mapping.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Routing name/type must match scopeId when supplied.");
            }

            return new AzureRepositoryMap { Name = name, Type = type, ScopeId = scope.ScopeId, Target = mapping.Target };
        }

        private static ScanScope ParseScope(string scopeId)
        {
            if (string.IsNullOrWhiteSpace(scopeId))
            {
                throw new InvalidOperationException("Scan scopes must be fully qualified subscription or resource-group IDs.");
            }

            string[] segments = scopeId.Split('/');
            if ((segments.Length != 3 && segments.Length != 5)
                || segments[0] != ""
                || !segments[1].Equals("subscriptions", StringComparison.OrdinalIgnoreCase)
                || segments[2].Length != 36
                || !Guid.TryParseExact(segments[2], "D", out Guid subscriptionId))
            {
                throw new InvalidOperationException($"Invalid scan scope '{scopeId}'. Expected /subscriptions/<guid> or /subscriptions/<guid>/resourceGroups/<name>.");
            }

            string? resourceGroupName = null;
            if (segments.Length == 5)
            {
                if (!segments[3].Equals("resourceGroups", StringComparison.OrdinalIgnoreCase)
                    || !Regex.IsMatch(segments[4], @"\A[-a-zA-Z0-9_().]{1,90}(?<!\.)\z", RegexOptions.CultureInvariant))
                {
                    throw new InvalidOperationException($"Invalid resource-group scan scope '{scopeId}'.");
                }

                resourceGroupName = segments[4].ToLowerInvariant();
            }

            string normalizedId = $"/subscriptions/{subscriptionId:D}";
            if (resourceGroupName != null)
            {
                normalizedId += $"/resourceGroups/{resourceGroupName}";
            }

            return new ScanScope(normalizedId, subscriptionId.ToString("D"), resourceGroupName);
        }
    }
}
