using Microsoft.Extensions.Configuration;
using Microsoft.RetireaBot.Models;
using Microsoft.RetireaBot.Models.AzureDevOps;

namespace Microsoft.RetireaBot.Helpers.AzureDevOps
{
    public class AuthModeService
    {
        private readonly string? _clientId;
        private readonly string? _tenantId;
        private readonly string? _clientSecret;
        private readonly string? _certificateId;
        private readonly string? _pat;

        private readonly AuthMode AuthMode;

        public AuthModeService(IConfiguration config)
        {
            _clientId = config.GetSection(ConfigKeys.AzureDevOps.ClientId).Get<string>();
            _tenantId = config.GetSection(ConfigKeys.AzureDevOps.TenantId).Get<string>();
            _clientSecret = config.GetSection(ConfigKeys.AzureDevOps.ClientSecret).Get<string>();
            _certificateId = config.GetSection(ConfigKeys.AzureDevOps.CertificateId).Get<string>();
            _pat = config.GetSection(ConfigKeys.AzureDevOps.PAT).Get<string>();

            AuthMode = (
                HasValue(_pat),
                HasValue(_clientId),
                HasValue(_tenantId),
                HasValue(_clientSecret),
                HasValue(_certificateId)
            ) switch
            {
                (_, true, true, _, true) => AuthMode.Certificate,
                (_, true, true, true, _) => AuthMode.ClientSecret,
                (_, true, true, _, _) => AuthMode.ManagedIdentity,
                (true, _, _, _, _) => AuthMode.PAT,
                _ => AuthMode.BuiltIn,
            };
        }

        public AuthMode GetAuthMode() { return AuthMode; }

        public string? GetPAT() { return _pat; }
        public string? GetClientId() { return _clientId; }
        public string? GetTenantId() { return _tenantId; }
        public string? GetClientSecret() { return _clientSecret; }
        public string? GetCertificateId() { return _certificateId; }

        private static bool HasValue(string? value) => !string.IsNullOrEmpty(value);
    }
}
