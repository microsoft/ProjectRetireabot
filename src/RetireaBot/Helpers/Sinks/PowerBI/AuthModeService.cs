using Microsoft.Extensions.Configuration;
using Microsoft.RetireaBot.Models;
using Microsoft.RetireaBot.Models.Sinks.PowerBI;

namespace Microsoft.RetireaBot.Helpers.Sinks.PowerBI
{
    public class AuthModeService
    {
        private readonly string? _clientId;
        private readonly string? _tenantId;
        private readonly string? _clientSecret;
        private readonly string? _certificateId;

        private readonly AuthMode AuthMode;

        public AuthModeService(IConfiguration config)
        {
            _clientId = config.GetSection(ConfigKeys.PowerBI.ClientId).Get<string>();
            _tenantId = config.GetSection(ConfigKeys.PowerBI.TenantId).Get<string>();
            _clientSecret = config.GetSection(ConfigKeys.PowerBI.ClientSecret).Get<string>();
            _certificateId = config.GetSection(ConfigKeys.PowerBI.CertificateId).Get<string>();

            AuthMode = (
                HasValue(_clientId),
                HasValue(_tenantId),
                HasValue(_clientSecret),
                HasValue(_certificateId)
            ) switch
            {
                (true, true, _, true) => AuthMode.Certificate,
                (true, true, true, _) => AuthMode.ClientSecret,
                (true, true, _, _) => AuthMode.ManagedIdentity,
                _ => AuthMode.BuiltIn,
            };
        }

        public AuthMode GetAuthMode() { return AuthMode; }

        public string? GetClientId() { return _clientId; }
        public string? GetTenantId() { return _tenantId; }
        public string? GetClientSecret() { return _clientSecret; }
        public string? GetCertificateId() { return _certificateId; }

        private static bool HasValue(string? value) => !string.IsNullOrEmpty(value);
    }
}
