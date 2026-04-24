using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Certificates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.OAuth;
using Microsoft.VisualStudio.Services.WebApi;
using Retirebot.Models;
using Retirebot.Models.AzureDevOps;

namespace Retirebot.Helpers.AzureDevOps
{
    public class CredentialProvider
    {
        private VssConnection? _connection;
        private VssCredentials? _credentials;
        private TokenCredential? _tokenCredentials;

        private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;

        private readonly ILogger _logger;
        private readonly string _organisationUrl;
        private readonly SemaphoreSlim _refreshLock = new(1, 1);
        private readonly AuthModeService _authModeSrv;
        private readonly CertificateClient _certClient;

        public CredentialProvider(IConfiguration config, ILoggerFactory loggerFactory, DefaultAzureCredential credentials, AuthModeService authModeService, CertificateClient certClient)
        {
            _authModeSrv = authModeService;
            _logger = loggerFactory.CreateLogger<CredentialProvider>();
            _organisationUrl = config.GetSection(ConfigKeys.AzureDevOps.OrganisationUrl).Get<string>()
                           ?? throw new InvalidOperationException("AzureDevOps:OrganisationUrl is not configured.");

            _certClient = certClient;
            _tokenCredentials = _authModeSrv.GetAuthMode() switch
            {
                AuthMode.Certificate => CreateCredentials(tenantId: _authModeSrv.GetTenantId(), clientId: _authModeSrv.GetClientId(), certificateId: _authModeSrv.GetCertificateId()),
                AuthMode.ClientSecret => new ClientSecretCredential(tenantId: _authModeSrv.GetTenantId(), clientId: _authModeSrv.GetClientId(), clientSecret: _authModeSrv.GetClientSecret()),
                AuthMode.ManagedIdentity => new DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = _authModeSrv.GetClientId() }),
                AuthMode.BuiltIn => credentials,
                AuthMode.PAT => null, // PAT doesn't use TokenCredential
                _ => throw new InvalidOperationException("No supported Azure DevOps Credentials can be used. Please check your settings.")
            };
        }

        private static TokenCredential CreateCredentials(string? tenantId, string? clientId, string? certificateId)
        {
            throw new NotImplementedException("Certificate Credentials not implemented yet");
        }

        public VssCredentials CreateCredentials()
        {
            if (_authModeSrv.GetAuthMode() == AuthMode.PAT)
            {
                return new VssBasicCredential("", _authModeSrv.GetPAT());
            }

            var token = _tokenCredentials!.GetToken(new TokenRequestContext(new[] { "499b84ac-1321-427f-aa17-267ca6975798/.default" }), default);

            _logger.LogInformation("Token acquired for Azure DevOps, expires: {Expiry}", token.ExpiresOn);
            
            _tokenExpiry = token.ExpiresOn;

            return new VssOAuthAccessTokenCredential(token.Token);
        }

        public async Task<VssConnection?> GetConnection()
        {
            if (DateTimeOffset.UtcNow >= _tokenExpiry.AddMinutes(-5))
            {
                await RefreshTokenAsync();
            }
            return _connection;
        }

        private async Task RefreshTokenAsync()
        {
            if (_tokenCredentials == null) return;

            await _refreshLock.WaitAsync();
            try
            {
                if (DateTimeOffset.UtcNow < _tokenExpiry) return;

                _logger.LogInformation("Refreshing Azure DevOps Connection");
                
                _credentials = CreateCredentials();
                _connection = new VssConnection(new Uri(_organisationUrl), _credentials);
            }
            finally
            {
                _refreshLock.Release();
            }
        }
    }
}
