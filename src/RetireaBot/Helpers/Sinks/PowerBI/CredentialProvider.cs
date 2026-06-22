using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Certificates;
using Microsoft.Extensions.Logging;
using Microsoft.RetireaBot.Models.Sinks.PowerBI;
using System.Security.Cryptography.X509Certificates;

namespace Microsoft.RetireaBot.Helpers.Sinks.PowerBI
{
    public class CredentialProvider
    {
        public const string PowerBiScope = "https://analysis.windows.net/powerbi/api/.default";

        private readonly TokenCredential _tokenCredential;
        private readonly ILogger _logger;

        public CredentialProvider(
            ILoggerFactory loggerFactory,
            TokenCredential credentials,
            AuthModeService authModeService,
            CertificateClient? certClient = null)
        {
            _logger = loggerFactory.CreateLogger<CredentialProvider>();

            _tokenCredential = authModeService.GetAuthMode() switch
            {
                AuthMode.Certificate => CreateCertificateCredentials(
                    authModeService,
                    certClient ?? throw new InvalidOperationException(
                        "PowerBI certificate authentication requires KeyVault:Uri to be configured.")).GetAwaiter().GetResult(),
                AuthMode.ClientSecret => new ClientSecretCredential(
                    tenantId: authModeService.GetTenantId(),
                    clientId: authModeService.GetClientId(),
                    clientSecret: authModeService.GetClientSecret()),
                AuthMode.ManagedIdentity => new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(authModeService.GetClientId()!)),
                AuthMode.BuiltIn => credentials,
                _ => throw new InvalidOperationException("No supported PowerBI Credentials can be used. Please check your settings.")
            };
        }

        public TokenCredential TokenCredential => _tokenCredential;

        public async ValueTask<AccessToken> GetTokenAsync(CancellationToken cancellationToken = default)
        {
            AccessToken token = await _tokenCredential.GetTokenAsync(
                new TokenRequestContext(new[] { PowerBiScope }),
                cancellationToken);

            _logger.LogDebug("Acquired PowerBI access token, expires {Expiry}", token.ExpiresOn);
            return token;
        }

        private static async Task<TokenCredential> CreateCertificateCredentials(
            AuthModeService authModeSrv,
            CertificateClient certClient)
        {
            var options = new DownloadCertificateOptions(authModeSrv.GetCertificateId()!)
            {
                KeyStorageFlags = X509KeyStorageFlags.MachineKeySet
            };
            X509Certificate2 cert = await certClient.DownloadCertificateAsync(options);

            return new ClientCertificateCredential(authModeSrv.GetTenantId(), authModeSrv.GetClientId(), cert);
        }
    }
}
