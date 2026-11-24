using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.RetireaBot.Models;

namespace Microsoft.RetireaBot.Helpers.Settings
{
    public static class ScanConfiguration
    {
        public static ResolvedScanOptions? Load(IConfiguration config, IEnumerable<IVendorSettings> enabledBackends)
        {
            string? modeValue = config[ConfigKeys.Scan.Mode];
            string[] scopes = ReadScopes(config, ConfigKeys.Scan.Scopes);
            string[] deployedScopes = ReadScopes(config, ConfigKeys.Scan.ResolvedScopes);
            bool assignReader = config.GetValue<bool?>(ConfigKeys.Scan.AssignReader) ?? true;
            if (string.IsNullOrEmpty(modeValue))
            {
                if (scopes.Length != 0 || deployedScopes.Length != 0 || !assignReader)
                {
                    throw new InvalidOperationException("Select a scan mode when configuring scan scopes or disabling Reader provisioning.");
                }
                return null;
            }

            if (!Enum.GetNames<ScanMode>().Contains(modeValue, StringComparer.OrdinalIgnoreCase)
                || !Enum.TryParse(modeValue, true, out ScanMode mode))
            {
                throw new InvalidOperationException($"Unsupported scan mode: {modeValue}.");
            }

            var resolved = ScanScopeResolver.Resolve(new ScanOptions
            {
                Mode = mode,
                Scopes = scopes,
                AssignReader = assignReader
            }, config[ConfigKeys.Scan.DeploymentSubscriptionId] ?? "", enabledBackends);

            // Detect drift between the deployed RBAC scope list and runtime routing configuration.
            if (config.GetSection(ConfigKeys.Scan.ResolvedScopes).Exists())
            {
                var deployed = ScanScopeResolver.Resolve(new ScanOptions
                {
                    Mode = ScanMode.Explicit,
                    Scopes = deployedScopes
                }, "", []);
                if (!resolved.Scopes.Select(scope => scope.ScopeId).ToHashSet(StringComparer.OrdinalIgnoreCase)
                    .SetEquals(deployed.Scopes.Select(scope => scope.ScopeId)))
                {
                    throw new InvalidOperationException("Runtime scan scopes differ from deployed scopes. Redeploy the scan configuration.");
                }
            }

            return resolved;
        }

        private static string[] ReadScopes(IConfiguration config, string key)
        {
            IConfigurationSection section = config.GetSection(key);
            if (section.Value != null)
            {
                return JsonSerializer.Deserialize<string[]>(section.Value)
                    ?? throw new InvalidOperationException($"{key} must be a JSON array of scope IDs.");
            }
            return section.Get<string[]>() ?? [];
        }
    }
}
